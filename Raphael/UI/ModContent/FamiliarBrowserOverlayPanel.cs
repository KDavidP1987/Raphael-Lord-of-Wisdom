using System;
using System.Collections.Generic;
using Raphael.Config;
using Raphael.Services;
using Raphael.UI.Framework.CustomLib.Panel;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.UI.Framework.UniverseLib.UI.Panels;
using Raphael.UI.ModContent.Data;
using Raphael.Utils;
using TMPro;
using UnityEngine;
using UIBase = Raphael.UI.Framework.UniverseLib.UI.UIBase;
using Raphael.UI.Framework.CustomLib.Util;

namespace Raphael.UI.ModContent;

// Secondary overlay: minimal Boxes-tab-equivalent for switching between boxes
// and binding/unbinding familiars without opening the main panel.
//
// Layout:
//   ┌──────────────────────────────────┐
//   │ ◄  ActiveBoxName  ►   [Refresh]  │  ← box header
//   │ (no boxes — click Refresh)       │  ← only when BoxList empty
//   │ Active: GoblinMaster             │  ← active-familiar indicator
//   │ ⚠  warning here when swap armed  │  ← reserved when idle
//   ├──────────────────────────────────┤
//   │ 01 — Skeleton  Lv 12             │  ← scrollable familiar list
//   │ 02 — Wolf      Lv 8  ★ Storm     │
//   │ 03 — Bat       Lv 5              │
//   │ ...                              │
//   ├──────────────────────────────────┤
//   │      [Unbind active]             │  ← footer
//   └──────────────────────────────────┘
//
// Bind / auto-swap mirror the main Boxes tab's two-click destruction-confirm
// (clicking another familiar while one is active arms the swap; second click
// within 5s sends `.fam ub` → `.fam b N`). State is local to the overlay so
// arming here doesn't carry into the main panel.
//
// On first show, if PlayerStateService.BoxList is empty, sends `.fam boxes`
// so the user doesn't have to open the main Boxes tab to populate the list.
public class FamiliarBrowserOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "FamiliarBrowserOverlay";
    public override PanelType PanelType => PanelType.FamiliarBrowserOverlay;

    public override int MinWidth  => 280;
    // 0.15.0: lowered from 440 to 220. The toolbar / box-name / status rows
    // sum to ~70 px at their minHeights, the footer to 26 px, and the scroll
    // view's own minHeight is 80 px — so 220 (with chrome) is the practical
    // floor below which content starts overlapping. The header and footer
    // rows have flexibleHeight: 0 while the scroll view has flexibleHeight:
    // 1, which means ALL of the shrinkage between the default 440 and the
    // new 220 floor comes out of the familiar list area — header buttons,
    // box name + count, and the Unbind footer stay at their natural sizes.
    // Friend-test 0.14.0: users with large text settings reported a sliver
    // of unused space below the list when they wanted to fit the overlay
    // into a small monitor corner.
    public override int MinHeight => 220;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Default to right side, slightly above center so it sits below the daily-quest
    // overlay if both are toggled on.
    public override Vector2 DefaultPosition  => new(
         Owner.Scaler.m_ReferenceResolution.x * 0.5f - 320,
        -40);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.FamiliarBrowserTransparency);
    // 0.12.0: shares both color pickers with the main panel. The five info
    // overlays opt into the OUTER color too (friend-test redirect after the
    // first pre-release: "all overlays should honor the color theme"), but
    // only the main panel and this overlay have scroll views to recolor via
    // the inner picker, so UsesCustomInnerBackgroundColor stays scoped.
    public override bool UsesCustomBackgroundColor      => true;
    public override bool UsesCustomInnerBackgroundColor => true;

    // Header
    private TextMeshProUGUI _boxNameLabel;
    private TextMeshProUGUI _activeFamLabel;
    private TextMeshProUGUI _swapWarningLabel;
    // Dynamic familiar list
    private GameObject _famListContainer;
    // Footer — v0.12.0 splits the old single "Unbind active" button into
    // Toggle (.fam t, recallable) + Unbind (.fam ub, destructive). User
    // feedback on v0.11.2: toggle-on/off is a far more common operation
    // than destroy, and the old single-button footer made it easy to
    // unbind by accident when you meant "dismiss for now."
    private ButtonRef _toggleBtn;
    private ButtonRef _unbindBtn;
    // 0.10.1: sort cycle button in the header.
    private ButtonRef _sortBtn;
    // 0.10.10: in-overlay scan button — same backend as the V-Bloods tab.
    private ButtonRef _scanBtn;
    // 0.10.5: V-Blood view toggle. The overlay can render either the
    // current box's familiars (BoxView, original behavior) or a compact
    // list of every captured V-Blood from VBloodCollection (VBloodView).
    // The two modes share the same scroll container — Render() repopulates
    // it differently based on _viewMode. Header elements specific to one
    // mode (box-cycle arrows in BoxView, scan status in VBloodView) are
    // toggled visible/hidden alongside.
    private enum ViewMode { BoxView, VBloodView }
    private ViewMode _viewMode = ViewMode.BoxView;
    private ButtonRef _viewBtn;
    private GameObject _boxNavRow;     // ← BoxName → Reload — hidden in V-Blood mode
    private TextMeshProUGUI _vbStatusLabel; // shown in V-Blood mode (replaces _activeFamLabel)
    private bool _vbSummonStatusSubscribed;
    private bool _vbCollectionSubscribed;

    // Two-click destruction-confirm state — mirrors MainPanel's auto-swap
    // logic but kept local so arming in the overlay doesn't leak into the
    // main panel and vice versa.
    private int   _pendingSwapIndex = -1;
    private float _pendingSwapDeadline = -1f;
    private const float SWAP_CONFIRM_WINDOW_SECONDS = 5f;

    private bool _subscribed;

    public FamiliarBrowserOverlayPanel(UIBase owner) : base(owner) { }

    private bool _autoPullDone = false;

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        // 0.10.13: force-expand=false on the ContentRoot VLG so the
        // toolbar / name / status / footer rows don't absorb extra
        // height when the overlay is resized — all extra space goes
        // to the scrollview (flex=1) instead. Mirrors the same fix
        // applied to the main panel; friend-test: "the box label
        // container is still unnecessarily large." The box-name row
        // was being force-expanded beyond its preferredHeight=22
        // because Unity's VLG distributes extra space evenly among
        // all children when forceExpand is true.
        var rootVlg = ContentRoot.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (rootVlg != null) rootVlg.childForceExpandHeight = false;

        BuildHeader();
        BuildFamiliarList();
        BuildFooter();

        if (!_subscribed)
        {
            PlayerStateService.BoxListChanged     += OnAnyBoxStateChanged;
            PlayerStateService.BoxContentsChanged += OnAnyBoxStateChanged;
            PlayerStateService.ActiveBoxChanged   += OnAnyBoxStateChanged;
            PlayerStateService.FamiliarChanged    += OnAnyBoxStateChanged;
            _subscribed = true;
        }
        // 0.10.5: V-Blood collection updates also trigger a render so the
        // overlay's V-Blood view reflects scanner progress in real time.
        if (!_vbCollectionSubscribed)
        {
            PlayerStateService.VBloodCollectionChanged += OnAnyBoxStateChanged;
            VBloodScannerService.ScanStateChanged      += OnScanStateChanged;
            _vbCollectionSubscribed = true;
        }
        // Apply initial view-mode visibility (BoxView default; ApplyViewModeVisibility
        // shows the box-nav row).
        ApplyViewModeVisibility();

        // Per-frame ticker that fires the first-load auto-pull as soon as
        // MessageService is ready. Pre-0.8.1 the auto-pull was an inline check
        // here in ConstructPanelContent — but with 0.6.0's overlay-restore-on-
        // init, this method runs DURING UIOnInitialize, which happens BEFORE
        // CommonClientDataSystem has a chance to set local user/character.
        // The inline check saw MessageService.IsInitialized=false and silently
        // skipped, so the user's restored overlay opened with an empty box
        // list and no auto-fetch ever happened. The ticker fires once the
        // service is bound (typically a frame or two later), then unregisters.
        Behaviors.CoreUpdateBehavior.Actions.Add(TickDeferredAutoPull);

        Render();
    }

    private void TickDeferredAutoPull()
    {
        if (_autoPullDone) return;
        if (!MessageService.IsInitialized) return; // wait for character/user binding
        _autoPullDone = true;
        // Skip the network round-trip if the box list is already populated
        // (e.g. from the main Boxes tab being opened first this session).
        if (PlayerStateService.BoxList != null && PlayerStateService.BoxList.Count > 0)
            return;
        EnqueueOrWarn(MessageService.BCCOM_FAM_BOXES);
    }

    /// <summary>0.10.10: header rebuilt for clarity. Friend-testing 0.10.9
    /// surfaced "box name overlaps the ◄ / ► nav buttons in some states."
    /// New layout has FIVE stacked rows instead of two:
    ///
    ///   Row 1 — toolbar: ◄  ►  Reload  Scan  View  Sort
    ///   Row 2 — box name + count (own line; "Name (X / N)")
    ///   Row 3 — mode / filter / active-familiar status
    ///   Row 4 — scroll list (built by BuildFamiliarList)
    ///   Row 5 — Unbind footer
    ///
    /// All controls sit on Row 1, so the box-name label can't be crowded
    /// by buttons regardless of overlay width. Row 2 is hidden in V-Blood
    /// view, Row 3 takes its place describing the alternate mode.</summary>
    private void BuildHeader()
    {
        // ── Row 1: toolbar (every button) ───────────────────────────────
        var toolbar = UIFactory.CreateHorizontalGroup(ContentRoot, "OverlayToolbar",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(toolbar,
            minWidth: 260, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        // Box prev (←) — narrowed slightly so a 6-button toolbar still fits
        // the overlay's default width.
        var prev = UIFactory.CreateButton(toolbar, "BoxPrev", "←");
        UIFactory.SetLayoutElement(prev.GameObject,
            minWidth: 28, preferredWidth: 30, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var prevTxt = prev.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (prevTxt != null) { prevTxt.fontSize = Theme.ScaledOverlay(16); prevTxt.fontStyle = FontStyles.Bold; }
        prev.OnClick = () => CycleBox(-1);
        UI.TooltipHover.Attach(prev.GameObject, "Previous box (cycles left through your familiar boxes).");

        // Box next (→).
        var next = UIFactory.CreateButton(toolbar, "BoxNext", "→");
        UIFactory.SetLayoutElement(next.GameObject,
            minWidth: 28, preferredWidth: 30, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var nextTxt = next.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (nextTxt != null) { nextTxt.fontSize = Theme.ScaledOverlay(16); nextTxt.fontStyle = FontStyles.Bold; }
        next.OnClick = () => CycleBox(+1);
        UI.TooltipHover.Attach(next.GameObject, "Next box (cycles right through your familiar boxes).");

        // Reload button — refreshes box list + current box contents.
        var refresh = UIFactory.CreateButton(toolbar, "BoxRefresh", "Reload");
        UIFactory.SetLayoutElement(refresh.GameObject,
            minWidth: 54, preferredWidth: 60, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var refreshTxt = refresh.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (refreshTxt != null) refreshTxt.fontSize = Theme.ScaledOverlay(11);
        refresh.OnClick = () =>
        {
            EnqueueOrWarn(MessageService.BCCOM_FAM_BOXES);
            EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
        };
        UI.TooltipHover.Attach(refresh.GameObject, "Re-pull the box list AND the current box's familiar list from the server.");

        // 0.10.10: Scan button — triggers a full V-Blood box-sweep without
        // requiring the user to switch to the main UI's V-Bloods tab. Same
        // backend as the V-Bloods tab's Scan all; cycles to "Cancel" while
        // a scan is running.
        _scanBtn = UIFactory.CreateButton(toolbar, "OverlayScanBtn", FormatScanBtnText());
        UIFactory.SetLayoutElement(_scanBtn.GameObject,
            minWidth: 54, preferredWidth: 60, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var scanTxt = _scanBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (scanTxt != null) scanTxt.fontSize = Theme.ScaledOverlay(11);
        _scanBtn.OnClick = () =>
        {
            if (VBloodScannerService.Scanning) VBloodScannerService.CancelScan();
            else                                VBloodScannerService.StartScan();
            RefreshScanBtnText();
        };
        UI.TooltipHover.Attach(_scanBtn.GameObject,
            "Run the V-Blood box-sweep scan from the overlay — same as the V-Bloods tab's Scan all button. Walks every box; ~30-60s; restores your active box at the end. Click again while running to cancel.");

        // View toggle — Box ↔ V-Blood collection.
        _viewBtn = UIFactory.CreateButton(toolbar, "ViewModeBtn", FormatViewBtnText());
        UIFactory.SetLayoutElement(_viewBtn.GameObject,
            minWidth: 60, preferredWidth: 70, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var viewTxt = _viewBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (viewTxt != null) viewTxt.fontSize = Theme.ScaledOverlay(10);
        _viewBtn.OnClick = () =>
        {
            _viewMode = _viewMode == ViewMode.BoxView ? ViewMode.VBloodView : ViewMode.BoxView;
            RefreshViewBtnText();
            ApplyViewModeVisibility();
            Render();
        };
        UI.TooltipHover.Attach(_viewBtn.GameObject,
            "Switch between the box-by-box familiar browser (default) and a compact V-Blood collection view sourced from the V-Blood scanner. Click Scan above to populate V-Blood data.");

        // Sort cycle.
        _sortBtn = UIFactory.CreateButton(toolbar, "FamSortBtn", FormatSortBtnText());
        UIFactory.SetLayoutElement(_sortBtn.GameObject,
            minWidth: 50, preferredWidth: 56, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var sortTxt = _sortBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (sortTxt != null) sortTxt.fontSize = Theme.ScaledOverlay(10);
        _sortBtn.OnClick = () =>
        {
            var current = Settings.FamiliarSortOrderSetting;
            var effective = current == Settings.FamiliarSortOrder.Location
                ? Settings.FamiliarSortOrder.Default
                : current;
            var nextMode = effective switch
            {
                Settings.FamiliarSortOrder.Default      => Settings.FamiliarSortOrder.Alphabetical,
                Settings.FamiliarSortOrder.Alphabetical => Settings.FamiliarSortOrder.Level,
                Settings.FamiliarSortOrder.Level        => Settings.FamiliarSortOrder.Default,
                _                                       => Settings.FamiliarSortOrder.Default,
            };
            Settings.SetFamiliarSortOrder(nextMode);
            RefreshSortBtnText();
            Render();
        };
        UI.TooltipHover.Attach(_sortBtn.GameObject,
            "Cycle the list sort order: Box (server order) → A→Z → Level (descending).");

        // ── Row 2: box name + count (its OWN line so nothing crowds it) ─
        // 0.10.11: tighter row height (was 24/26 → 20/22) and zero
        // padding — friend-test 0.10.10 surfaced ~6 px of unused space
        // around this row that compressed the visible familiar list.
        var nameRow = UIFactory.CreateHorizontalGroup(ContentRoot, "BoxNameRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 0, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(nameRow,
            minWidth: 260, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        _boxNavRow = nameRow; // hidden in V-Blood view via ApplyViewModeVisibility

        var nameLbl = UIFactory.CreateLabel(nameRow, "BoxName", "(no box)",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledOverlay(13));
        UIFactory.SetLayoutElement(nameLbl.GameObject,
            minWidth: 200, preferredWidth: 260, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        nameLbl.TextMesh.fontStyle = FontStyles.Bold;
        nameLbl.TextMesh.enableWordWrapping = false;
        nameLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        _boxNameLabel = nameLbl.TextMesh;

        // ── Row 3: mode-aware status (active familiar / scan progress) ──
        // 0.10.11: word-wrap OFF so a long familiar name can't grow this
        // row into a 2-line block that steals scroll-list space. Overflow
        // mode set to Ellipsis so long names truncate cleanly.
        var status = UIFactory.CreateLabel(ContentRoot, "StatusLine",
            "Active: (none)", Theme.OverlayMidlineAlignment(), color: null, fontSize: Theme.ScaledOverlay(12));
        UIFactory.SetLayoutElement(status.GameObject,
            minWidth: 260, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        status.TextMesh.enableWordWrapping = false;
        status.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        _activeFamLabel = status.TextMesh;
        _swapWarningLabel = status.TextMesh; // same widget, dual-purpose
    }

    /// <summary>0.10.10: scan-button label format. Mirrors the V-Bloods tab's
    /// Scan all/Cancel cycle.</summary>
    private string FormatScanBtnText() => VBloodScannerService.Scanning ? "Cancel" : "Scan";

    private void RefreshScanBtnText()
    {
        if (_scanBtn == null) return;
        var t = _scanBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = FormatScanBtnText();
    }

    private void BuildFamiliarList()
    {
        // Wrap the dynamic familiar buttons in a ScrollView so a full box of
        // 10+ familiars doesn't push the Unbind footer off-screen. The
        // ScrollView's content GameObject already has a VerticalLayoutGroup +
        // ContentSizeFitter applied by CreateScrollView, so the buttons just
        // get added to it normally.
        var scrollWrap = UIFactory.CreateScrollView(ContentRoot, "FamListScroll",
            out _famListContainer, out _);
        UIFactory.SetLayoutElement(scrollWrap,
            minWidth: 260, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 80, flexibleHeight: 1);

        // 0.10.8: gutter padding on the scroll content so V-Blood / familiar
        // row labels don't run flush against the scrollbar's left edge.
        // ResizeablePanelBase.ApplyContentEdgePadding pads the panel's
        // outermost VerticalLayoutGroup — but the scroll content is its
        // own nested VLG that the outer padding doesn't reach, and the
        // scrollbar lives at the right inside of the panel's padded area,
        // so without an explicit right pad here the row labels still touch
        // the scrollbar's left edge. Use the same setting so left/right
        // padding stays symmetric across overlays.
        if (_famListContainer != null)
        {
            var contentVlg = _famListContainer.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (contentVlg != null)
            {
                int pad = Config.Settings.OverlayEdgePadding;
                var p = contentVlg.padding;
                p.left  = pad;
                p.right = pad;
                contentVlg.padding = p;
            }
        }
    }

    private void BuildFooter()
    {
        // 0.10.11: tighter footer height (was 30/32 → 26/28) so the
        // overlay shows ~4 px more familiar list. The buttons themselves
        // stay at 24/26, leaving just ~2 px of breathing room top/bottom.
        var footer = UIFactory.CreateHorizontalGroup(ContentRoot, "Footer",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(footer,
            minWidth: 260, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);

        // 0.12.0: Toggle (left) — disables / re-enables the active
        // familiar (`.fam t`). The common case is the NPC-dominate
        // workflow: dominating an NPC auto-disables your familiar and
        // leaves it disabled (unlike flying / teleporting which
        // auto-re-enable on land). Toggle puts it back into combat
        // without changing the binding.
        _toggleBtn = UIFactory.CreateButton(footer, "Toggle", "Toggle");
        UIFactory.SetLayoutElement(_toggleBtn.GameObject,
            minWidth: 60, preferredWidth: 100, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        _toggleBtn.OnClick = () => EnqueueOrWarn(MessageService.BCCOM_FAM_TOGGLE);
        UI.TooltipHover.Attach(_toggleBtn.GameObject,
            ".fam t — Toggles your active familiar between enabled (visible / in combat) and disabled (hidden / out of combat). Flying and teleporting auto-disable the familiar but auto-re-enable on landing; dominating an NPC also disables it but does NOT re-enable — use Toggle to bring it back. Doesn't change which familiar is bound. Disabled when no familiar is bound.");

        // 0.12.0: Unbind (right) — removes the active binding so the
        // familiar returns to the box. NOT destructive — the box record
        // is preserved; the user can re-bind from the box list. Permanent
        // deletion is `.fam r [N]` (exposed in the main panel only).
        _unbindBtn = UIFactory.CreateButton(footer, "Unbind", "Unbind active");
        UIFactory.SetLayoutElement(_unbindBtn.GameObject,
            minWidth: 90, preferredWidth: 140, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        _unbindBtn.OnClick = () => EnqueueOrWarn(MessageService.BCCOM_FAM_UNBIND);
        UI.TooltipHover.Attach(_unbindBtn.GameObject,
            ".fam ub — Removes the active binding. Familiar returns to your box and can be re-bound any time; the box record is preserved. Use Toggle if you only want to temporarily disable the familiar in combat. (Permanent box deletion is .fam r N, available on the main Familiars tab.)");
    }

    private static void EnqueueOrWarn(string command)
    {
        if (!MessageService.IsInitialized)
        {
            LogUtils.LogWarning($"FamiliarBrowserOverlay: cannot send '{command}' — MessageService not bound yet.");
            return;
        }
        MessageService.EnqueueMessage(command);
    }

    private void CycleBox(int direction)
    {
        var boxes = PlayerStateService.BoxList;
        if (boxes == null || boxes.Count == 0) return;

        // Find current active box index, default to 0 if not in list.
        int idx = boxes.IndexOf(PlayerStateService.ActiveBox ?? "");
        if (idx < 0) idx = 0;

        idx = (idx + direction + boxes.Count) % boxes.Count;
        var target = boxes[idx];
        ClearPendingSwap();
        PlayerStateService.SetActiveBox(target);
        EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_SWITCH_BOX_FORMAT, target));
        EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
    }

    private void OnAnyBoxStateChanged()
    {
        Render();
    }

    /// <summary>0.10.10: keep the Scan button label in sync with the
    /// scanner's running state, and surface live scan progress in the
    /// status line.</summary>
    private void OnScanStateChanged()
    {
        RefreshScanBtnText();
        if (_activeFamLabel == null) return;
        if (VBloodScannerService.Scanning)
        {
            string box = VBloodScannerService.CurrentBoxBeingScanned;
            string suffix = string.IsNullOrEmpty(box) ? "" : $" — {box}";
            _activeFamLabel.text = $"Scanning… box {VBloodScannerService.CompletedForCurrentScan + 1} / {VBloodScannerService.TotalForCurrentScan}{suffix}";
        }
        // When scan ends we let the normal Render() reset the active-fam
        // label on the next BoxList / FamiliarChanged event.
    }

    private void Render()
    {
        if (_boxNameLabel == null) return;

        // 0.15.2: when Familiar system is reliably detected disabled
        // server-side, short-circuit both view modes and show a single
        // hint line instead of "no boxes loaded — click Reload" (which
        // would just spam empty replies). The list area is cleared and
        // the toggle/unbind footer buttons are disabled because there's
        // nothing meaningful to act on.
        if (PlayerStateService.IsSystemReliablyDisabled(PlayerStateService.SystemKind.Familiar))
        {
            _boxNameLabel.text   = "(Familiars disabled on this server)";
            _activeFamLabel.text = "The server admin has Bloodcraft's FamiliarSystem turned off.";
            if (_toggleBtn != null) _toggleBtn.Component.interactable = false;
            if (_unbindBtn != null) _unbindBtn.Component.interactable = false;
            ClearChildren(_famListContainer);
            return;
        }

        // 0.10.5: branch on view mode. V-Blood view bypasses the box-cycling
        // header rendering and shows the collection grid instead. Box view
        // (default) runs the existing box-by-box render path.
        if (_viewMode == ViewMode.VBloodView)
        {
            RenderVBloodView();
            return;
        }

        // Header label "Name  (X / N)"
        var boxes = PlayerStateService.BoxList;
        var active = PlayerStateService.ActiveBox;
        if (string.IsNullOrEmpty(active))
        {
            _boxNameLabel.text = "(no box selected)";
        }
        else if (boxes != null && boxes.Count > 0)
        {
            int idx = boxes.IndexOf(active);
            if (idx >= 0)
                _boxNameLabel.text = $"{active}  ({idx + 1} / {boxes.Count})";
            else
                _boxNameLabel.text = active; // active box not in list yet — race during refresh
        }
        else
        {
            _boxNameLabel.text = active;
        }

        // Active familiar
        var fam = PlayerStateService.Familiar;
        _activeFamLabel.text = string.IsNullOrEmpty(fam.Name)
            ? "Active: (none bound)"
            : $"Active: {fam.Name}   Lv {fam.Level}";
        bool hasFam = !string.IsNullOrEmpty(fam.Name);
        // 0.17.1: under Eclipse stand-down Raphael gets no live "active familiar" from
        // the stream (hasFam is always false), but `.fam t` / `.fam ub` still work
        // server-side — they act on whatever's actually bound. Keep the buttons
        // usable so manual toggle/unbind work under Eclipse.
        bool enableActions = hasFam || EclipseProtocolService.StandDownForEclipse();
        if (_toggleBtn != null) _toggleBtn.Component.interactable = enableActions;
        if (_unbindBtn != null) _unbindBtn.Component.interactable = enableActions;

        // Familiar list
        ClearChildren(_famListContainer);

        if (PlayerStateService.BoxList == null || PlayerStateService.BoxList.Count == 0)
        {
            AddListLine("(no boxes loaded — click Reload above)");
            return;
        }

        if (string.IsNullOrEmpty(active))
        {
            AddListLine("(use ← / → above to pick a box)");
            return;
        }

        if (!PlayerStateService.BoxContents.TryGetValue(active, out var entries) || entries.Count == 0)
        {
            AddListLine($"(loading familiars for {active}…)");
            return;
        }

        // 0.10.1: respect the shared FamiliarSortOrder setting (cycled via
        // the Sort button in the overlay header). Default leaves the
        // server-provided box order; Alphabetical / Level reorder per the
        // setting. We work on a local copy so we don't mutate the cached
        // BoxContents list (other consumers rely on its index-aligned form).
        var sortedEntries = new System.Collections.Generic.List<PlayerStateService.FamiliarBoxEntry>(entries);
        switch (Settings.FamiliarSortOrderSetting)
        {
            case Settings.FamiliarSortOrder.Alphabetical:
                sortedEntries.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
                break;
            case Settings.FamiliarSortOrder.Level:
                sortedEntries.Sort((a, b) =>
                {
                    if (b.Level != a.Level) return b.Level.CompareTo(a.Level);
                    return string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                });
                break;
            case Settings.FamiliarSortOrder.Location:
                // 0.10.2: region-based ordering. V-Bloods in the box sort by
                // their canonical region (1=Farbane through 7=Endgame); regular
                // familiars (not in the V-Blood registry) collapse to region 99
                // and sink to the bottom, alphabetized within that bucket.
                // "Primal <name>" entries match their base form's region.
                sortedEntries.Sort((a, b) =>
                {
                    string keyA = a.Name != null && a.Name.StartsWith("Primal ", System.StringComparison.OrdinalIgnoreCase)
                        ? a.Name.Substring("Primal ".Length) : a.Name;
                    string keyB = b.Name != null && b.Name.StartsWith("Primal ", System.StringComparison.OrdinalIgnoreCase)
                        ? b.Name.Substring("Primal ".Length) : b.Name;
                    int ra = Raphael.Resources.VBloodRegistry.RegionOrderFor(keyA);
                    int rb = Raphael.Resources.VBloodRegistry.RegionOrderFor(keyB);
                    if (ra != rb) return ra.CompareTo(rb);
                    return string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                });
                break;
            // Default falls through unchanged (server-provided order).
        }

        foreach (var entry in sortedEntries)
        {
            int idx = entry.Index;
            string label = $"{entry.Index:00}  —  {entry.Name}";
            if (entry.Level > 0)    label += $"  Lv {entry.Level}";
            if (entry.Prestige > 0) label += $"  P{entry.Prestige}";
            if (entry.IsShiny)
            {
                label += "  ★";
                var school = entry.ShinySchool;
                if (!string.IsNullOrEmpty(school)) label += $" {school}";
            }

            var b = UIFactory.CreateButton(_famListContainer, $"FamBtn_{entry.Index}", label);
            UIFactory.SetLayoutElement(b.GameObject,
                minWidth: 240, preferredWidth: 260, flexibleWidth: 1,
                minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
            var t = b.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null)
            {
                t.alignment = Theme.OverlayMidlineAlignment();
                t.fontSize = Theme.ScaledOverlay(12);
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Overflow;
            }
            b.OnClick = () => OnFamiliarClicked(idx);
        }
    }

    // 0.10.1: public hook for MainPanel's V-Bloods tab to notify us when the
    // shared sort setting changes — keeps the overlay's button text + list
    // in sync without forcing the user to interact with the overlay directly.
    public void NotifySortOrderChanged()
    {
        RefreshSortBtnText();
        if (Enabled) Render();
    }

    // 0.10.5: view-mode toggle helpers.
    private string FormatViewBtnText() => _viewMode == ViewMode.VBloodView ? "View: V-Bloods" : "View: Box";

    private void RefreshViewBtnText()
    {
        if (_viewBtn == null) return;
        var t = _viewBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = FormatViewBtnText();
    }

    private void ApplyViewModeVisibility()
    {
        // Hide the box-nav row in V-Blood mode (no per-box cycling makes
        // sense when we're showing the cross-box V-Blood collection).
        if (_boxNavRow != null) _boxNavRow.SetActive(_viewMode == ViewMode.BoxView);
        // The "Active: …" status line is reused for V-Blood summon status
        // text in V-Blood mode. Visible in both modes — content differs.
    }

    private string FormatSortBtnText()
    {
        // 0.10.4: compact 1-line labels so the button stays narrow.
        var mode = Settings.FamiliarSortOrderSetting;
        return mode switch
        {
            Settings.FamiliarSortOrder.Alphabetical => "A→Z",
            Settings.FamiliarSortOrder.Level        => "Lv↓",
            Settings.FamiliarSortOrder.Location     => "Box",  // overlay treats Region as Default (box order)
            _                                       => "Box",
        };
    }

    private void RefreshSortBtnText()
    {
        if (_sortBtn == null) return;
        var t = _sortBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = FormatSortBtnText();
    }

    // 0.11.0: V-Blood overlay rows now mirror the BoxView per-familiar row
    // format (index — name  Lv X  Pn  ★ school) instead of the compact
    // chip bar. Friend-test feedback: "can't see shiny status, attribute,
    // or level." Each captured variant gets its own row (so a V-Blood with
    // basic + shiny + primal + primal-shiny shows four rows, one per
    // instance) — same shape as the regular box view, just filtered to
    // names in the V-Blood registry across every box. Uncaptured V-Bloods
    // are not listed (they live in the V-Bloods tab of the main panel).
    //
    // Sort modes available here:
    //   Default      — group by box, in scan order
    //   Alphabetical — by base name, basic before primal
    //   Level        — descending level, then name
    //   Location     — region order from VBloodRegistry, then base name
    private void RenderVBloodView()
    {
        // Build the flat row list from VBloodCollection.Instances (one row
        // per captured variant). Each row carries enough state to render
        // and dispatch the summon click — we don't reach back into the
        // collection per row at click time.
        var rows = new System.Collections.Generic.List<VBloodOverlayRow>();
        foreach (var kv in PlayerStateService.VBloodCollection)
        {
            var slot = kv.Value;
            if (slot.Instances == null) continue;
            foreach (var inst in slot.Instances)
            {
                rows.Add(new VBloodOverlayRow
                {
                    BaseName   = slot.Name,
                    DisplayName = inst.IsPrimal ? "Primal " + slot.Name : slot.Name,
                    Box        = inst.Box,
                    Index      = inst.Index,
                    Level      = inst.Level,
                    Prestige   = inst.Prestige,
                    IsShiny    = inst.IsShiny,
                    IsPrimal   = inst.IsPrimal,
                    ShinySchool = inst.ShinySchool,
                });
            }
        }

        // Header summary — counts the number of captured INSTANCES (rows
        // visible), plus how many distinct V-Blood entries that covers, so
        // the user can see both numbers at a glance.
        int total = Resources.VBloodRegistry.All.Length;
        int distinct = 0, primals = 0, shinies = 0;
        foreach (var slot in PlayerStateService.VBloodCollection.Values)
        {
            if (slot.HasBasic || slot.HasShiny || slot.HasPrimal || slot.HasPrimalShiny) distinct++;
            if (slot.HasPrimal || slot.HasPrimalShiny) primals++;
            if (slot.HasShiny || slot.HasPrimalShiny) shinies++;
        }
        string header = $"V-Bloods: {distinct} / {total}  ({rows.Count} captured)";
        if (primals > 0) header += $" · {primals}P";
        if (shinies > 0) header += $" · {shinies}★";
        var summonStatus = Services.VBloodSummonService.LastStatus;
        if (!string.IsNullOrEmpty(summonStatus)) header += $"   |  {summonStatus}";
        _activeFamLabel.text = header;

        // No per-row footer-action concept in V-Blood view (rows summon directly).
        if (_toggleBtn != null) _toggleBtn.Component.interactable = false;
        if (_unbindBtn != null) _unbindBtn.Component.interactable = false;

        ClearChildren(_famListContainer);

        if (rows.Count == 0)
        {
            AddListLine(PlayerStateService.VBloodCollection.Count == 0
                ? "(no scan yet — click Scan above)"
                : "(no V-Bloods captured in your boxes)");
            return;
        }

        // Sort variants. Within the same base name, basic always precedes
        // primal, and within those, non-shiny precedes shiny — so the
        // default per-row ordering reads naturally.
        int VariantOrder(VBloodOverlayRow r)
            => (r.IsPrimal ? 2 : 0) + (r.IsShiny ? 1 : 0);

        switch (Settings.FamiliarSortOrderSetting)
        {
            case Settings.FamiliarSortOrder.Alphabetical:
                rows.Sort((a, b) =>
                {
                    int c = string.Compare(a.BaseName, b.BaseName, System.StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return VariantOrder(a).CompareTo(VariantOrder(b));
                });
                break;
            case Settings.FamiliarSortOrder.Level:
                rows.Sort((a, b) =>
                {
                    if (b.Level != a.Level) return b.Level.CompareTo(a.Level);
                    int c = string.Compare(a.BaseName, b.BaseName, System.StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return VariantOrder(a).CompareTo(VariantOrder(b));
                });
                break;
            case Settings.FamiliarSortOrder.Location:
                rows.Sort((a, b) =>
                {
                    int ra = Resources.VBloodRegistry.RegionOrderFor(a.BaseName);
                    int rb = Resources.VBloodRegistry.RegionOrderFor(b.BaseName);
                    if (ra != rb) return ra.CompareTo(rb);
                    int c = string.Compare(a.BaseName, b.BaseName, System.StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return VariantOrder(a).CompareTo(VariantOrder(b));
                });
                break;
            default:
                // Default: group by box, then by index inside box — mirrors
                // BoxView's natural ordering for the boxes the scanner walked.
                rows.Sort((a, b) =>
                {
                    int c = string.Compare(a.Box ?? "", b.Box ?? "", System.StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return a.Index.CompareTo(b.Index);
                });
                break;
        }

        foreach (var r in rows)
        {
            // 0.11.1 friend-test: match BoxView exactly — no [box] suffix.
            // The overlay's job here is to surface the captured variant
            // (name + level + prestige + shiny school). Which box it lives
            // in is interesting context for the V-Bloods tab but clutters
            // the overlay's compact row.
            string label = $"{r.Index:00}  —  {r.DisplayName}";
            if (r.Level > 0)    label += $"  Lv {r.Level}";
            if (r.Prestige > 0) label += $"  P{r.Prestige}";
            if (r.IsShiny)
            {
                label += "  ★";
                if (!string.IsNullOrEmpty(r.ShinySchool)) label += $" {r.ShinySchool}";
            }

            var btn = UIFactory.CreateButton(_famListContainer, $"VBRow_{r.BaseName}_{r.Box}_{r.Index}_{(r.IsPrimal?'P':'B')}{(r.IsShiny?'S':'N')}", label);
            UIFactory.SetLayoutElement(btn.GameObject,
                minWidth: 240, preferredWidth: 260, flexibleWidth: 1,
                minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
            var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null)
            {
                t.alignment = Theme.OverlayMidlineAlignment();
                t.fontSize = Theme.ScaledOverlay(12);
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Overflow;
            }
            // Click → smart summon via shared service. Pass the base name —
            // the summon service picks the best available variant based on
            // VBloodSummonService's internal preference (basic over primal).
            string captured_name = r.BaseName;
            btn.OnClick = () =>
            {
                if (!_vbSummonStatusSubscribed)
                {
                    Services.VBloodSummonService.StatusChanged += OnVBSummonStatus;
                    _vbSummonStatusSubscribed = true;
                }
                Services.VBloodSummonService.SummonVBlood(captured_name);
            };
        }
    }

    private void OnVBSummonStatus(string status)
    {
        // Re-render header so the status text appears. Cheap.
        if (_viewMode == ViewMode.VBloodView && Enabled) Render();
    }

    private struct VBloodOverlayRow
    {
        public string BaseName;
        public string DisplayName;
        public string Box;
        public int    Index;
        public int    Level;
        public int    Prestige;
        public bool   IsShiny;
        public bool   IsPrimal;
        public string ShinySchool;
    }

    private void AddListLine(string text)
    {
        var lbl = UIFactory.CreateLabel(_famListContainer, "ListLine", text,
            Theme.OverlayMidlineAlignment(), color: null, fontSize: Theme.ScaledOverlay(12));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 240, preferredWidth: 260, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Italic;
    }

    private void OnFamiliarClicked(int index)
    {
        // 0.17.1: under Eclipse stand-down we can't read the active familiar from
        // the stream, so the click-to-arm-swap UX can't fire (it always fell
        // through to "no active → just bind", which fails server-side if one is
        // already bound — the symptom the user saw). Make a click switch directly:
        // unbind whatever's active (a server no-op if none) then bind the clicked
        // one. Pure unbind (no rebind) is the "Unbind active" footer button.
        if (EclipseProtocolService.StandDownForEclipse())
        {
            ClearPendingSwap();
            EnqueueOrWarn(MessageService.BCCOM_FAM_UNBIND);
            EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, index));
            return;
        }

        bool hasActive = !string.IsNullOrEmpty(PlayerStateService.Familiar.Name);
        if (!hasActive)
        {
            ClearPendingSwap();
            EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, index));
            return;
        }

        float now = Time.realtimeSinceStartup;
        bool armed = _pendingSwapIndex == index && now <= _pendingSwapDeadline;
        if (armed)
        {
            ClearPendingSwap();
            EnqueueOrWarn(MessageService.BCCOM_FAM_UNBIND);
            EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, index));
            return;
        }

        string targetName = $"#{index}";
        var act = PlayerStateService.ActiveBox;
        if (!string.IsNullOrEmpty(act)
            && PlayerStateService.BoxContents.TryGetValue(act, out var entries))
        {
            foreach (var e in entries) if (e.Index == index) { targetName = e.Name; break; }
        }

        _pendingSwapIndex = index;
        _pendingSwapDeadline = now + SWAP_CONFIRM_WINDOW_SECONDS;
        if (_swapWarningLabel != null)
            _swapWarningLabel.text = $"Active: {PlayerStateService.Familiar.Name}. Click {targetName} again within {(int)SWAP_CONFIRM_WINDOW_SECONDS}s to unbind current and bind it.";
    }

    private void ClearPendingSwap()
    {
        _pendingSwapIndex = -1;
        _pendingSwapDeadline = -1f;
        if (_swapWarningLabel != null) _swapWarningLabel.text = "";
    }

    private static void ClearChildren(GameObject parent)
    {
        if (parent == null) return;
        var t = parent.transform;
        for (int i = t.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
    }

    internal override void Reset()
    {
        if (_subscribed)
        {
            PlayerStateService.BoxListChanged     -= OnAnyBoxStateChanged;
            PlayerStateService.BoxContentsChanged -= OnAnyBoxStateChanged;
            PlayerStateService.ActiveBoxChanged   -= OnAnyBoxStateChanged;
            PlayerStateService.FamiliarChanged    -= OnAnyBoxStateChanged;
            _subscribed = false;
        }
        if (_vbCollectionSubscribed)
        {
            PlayerStateService.VBloodCollectionChanged -= OnAnyBoxStateChanged;
            VBloodScannerService.ScanStateChanged      -= OnScanStateChanged;
            _vbCollectionSubscribed = false;
        }
        // 0.65.6: the V Blood summon-status subscription (made lazily on first summon click) was never detached,
        // so every overlay rebuild left the old instance rooted by VBloodSummonService.StatusChanged.
        if (_vbSummonStatusSubscribed)
        {
            Services.VBloodSummonService.StatusChanged -= OnVBSummonStatus;
            _vbSummonStatusSubscribed = false;
        }
        // Always remove the auto-pull ticker so it doesn't keep firing after
        // panel destruction. Idempotent — Remove on a non-registered handler
        // is a no-op.
        Behaviors.CoreUpdateBehavior.Actions.Remove(TickDeferredAutoPull);
        _autoPullDone = false;
    }
}
