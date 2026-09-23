using System;
using System.Collections.Generic;
using Raphael.Config;
using Raphael.Services;
using Raphael.UI.Framework.CustomLib.Panel;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.UI.Framework.UniverseLib.UI.Panels;
using Raphael.UI.ModContent.Data;
using Raphael.Utils;
using TMPro;
using UnityEngine;
using UIBase = Raphael.UI.Framework.UniverseLib.UI.UIBase;

namespace Raphael.UI.ModContent;

// 0.52: "Quick Spawn" overlay — one-click summon for a small, user-chosen set
// of familiars (up to 5), assigned regardless of which box they live in.
//
// Each slot button summons its assigned familiar by NAME via Bloodcraft's
// smart-bind (`.fam sb "Name"`), so the user never has to navigate to the
// box first. The two footer buttons separate the two distinct "put away"
// concepts the user asked for:
//   • Dismiss / Recall (`.fam t`) — toggles the ACTIVE familiar offline /
//     online WITHOUT unbinding it. It stays your active familiar; it's just
//     hidden / out of combat. (This is what "despawn" means here.)
//   • Unbind (`.fam ub`)          — releases the active binding so you can
//     summon a different familiar. Non-destructive: the box record is kept.
//
// Slot click semantics:
//   • assigned familiar is NOT the active one → switch to it (unbind the
//     current one if any, then smart-bind the assigned one).
//   • assigned familiar IS already the active one → Dismiss / Recall it
//     (`.fam t`), so the same button both summons and toggles it.
//
// Assignment is done on the main panel's All Familiars tab (the cross-box
// list) — this overlay only reads the saved slot list (Settings) and renders
// it. RaiseSlotsChanged() lets that tab live-refresh this overlay.
public class FamiliarQuickSpawnOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "FamiliarQuickSpawnOverlay";
    public override PanelType PanelType => PanelType.FamiliarQuickSpawnOverlay;

    public override int MinWidth  => 150;
    public override int MinHeight => 96;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Right side, a little above center so it doesn't sit on top of the
    // Familiar Browser overlay's default spot.
    public override Vector2 DefaultPosition  => new(
         Owner.Scaler.m_ReferenceResolution.x * 0.5f - 320,
         120);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.FamiliarQuickSpawnOverlayTransparency);
    public override bool UsesCustomBackgroundColor => true;

    // 0.52: lets the All Familiars assignment UI re-render any open overlay
    // the moment a slot is added / cleared, without a manager round-trip.
    public static event Action SlotsChanged;
    public static void RaiseSlotsChanged() => SlotsChanged?.Invoke();

    private TextMeshProUGUI _activeLabel;
    private GameObject _slotContainer;
    private ButtonRef _toggleBtn;
    private ButtonRef _unbindBtn;
    private bool _subscribed;

    public FamiliarQuickSpawnOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Quick Spawn");

        var rootVlg = ContentRoot.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (rootVlg != null) rootVlg.childForceExpandHeight = false;

        // Active-familiar status line.
        var status = UIFactory.CreateLabel(ContentRoot, "QSActive", "Active: (none)",
            Theme.OverlayMidlineAlignment(), color: null, fontSize: Theme.ScaledOverlay(12));
        UIFactory.SetLayoutElement(status.GameObject,
            minWidth: 140, preferredWidth: 220, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        status.TextMesh.enableWordWrapping = false;
        status.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        _activeLabel = status.TextMesh;

        // Slot buttons live in their own container so Render() can rebuild
        // just the slots without touching the status line or footer.
        _slotContainer = UIFactory.CreateVerticalGroup(ContentRoot, "QSSlots",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 3, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_slotContainer,
            minWidth: 140, preferredWidth: 220, flexibleWidth: 1,
            minHeight: 30, flexibleHeight: 1);

        BuildFooter();

        if (!_subscribed)
        {
            PlayerStateService.FamiliarChanged += OnStateChanged;
            SlotsChanged += OnStateChanged;
            _subscribed = true;
        }

        Render();
    }

    private void BuildFooter()
    {
        var footer = UIFactory.CreateHorizontalGroup(ContentRoot, "QSFooter",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(footer,
            minWidth: 140, preferredWidth: 220, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);

        _toggleBtn = UIFactory.CreateButton(footer, "QSToggle", "Dismiss / Recall");
        UIFactory.SetLayoutElement(_toggleBtn.GameObject,
            minWidth: 70, preferredWidth: 120, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var tTxt = _toggleBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (tTxt != null) tTxt.fontSize = Theme.ScaledOverlay(11);
        _toggleBtn.OnClick = () => Enqueue(MessageService.BCCOM_FAM_TOGGLE);
        TooltipHover.Attach(_toggleBtn.GameObject,
            ".fam t — Toggle your ACTIVE familiar offline/online (dismiss or recall). It stays bound as your active familiar; it's just hidden / out of combat. Does NOT switch or release the binding.");

        _unbindBtn = UIFactory.CreateButton(footer, "QSUnbind", "Unbind");
        UIFactory.SetLayoutElement(_unbindBtn.GameObject,
            minWidth: 56, preferredWidth: 90, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var uTxt = _unbindBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (uTxt != null) uTxt.fontSize = Theme.ScaledOverlay(11);
        _unbindBtn.OnClick = () => Enqueue(MessageService.BCCOM_FAM_UNBIND);
        TooltipHover.Attach(_unbindBtn.GameObject,
            ".fam ub — Release the active binding so you can summon a different familiar. Non-destructive: the familiar returns to its box (box record kept). Use Dismiss / Recall if you only want to hide it temporarily.");
    }

    private static void Enqueue(string command)
    {
        if (!MessageService.IsInitialized)
        {
            LogUtils.LogWarning($"FamiliarQuickSpawnOverlay: cannot send '{command}' — MessageService not bound yet.");
            return;
        }
        MessageService.EnqueueMessage(command);
    }

    private void OnStateChanged() => Render();

    private void Render()
    {
        if (_slotContainer == null) return;

        bool disabled = PlayerStateService.IsSystemReliablyDisabled(PlayerStateService.SystemKind.Familiar);
        if (disabled)
        {
            _activeLabel.text = "(Familiars disabled on this server)";
            if (_toggleBtn != null) _toggleBtn.Component.interactable = false;
            if (_unbindBtn != null) _unbindBtn.Component.interactable = false;
            ClearChildren(_slotContainer);
            return;
        }

        // Active familiar line. Under Eclipse stand-down we get no stream, so
        // the active name is unknown — the .fam commands still work server-side.
        bool standDown = EclipseProtocolService.StandDownForEclipse();
        var fam = PlayerStateService.Familiar;
        string activeName = (!standDown && fam.HasActive) ? fam.Name : null;
        _activeLabel.text = string.IsNullOrEmpty(activeName)
            ? (standDown ? "Active: (hidden under Eclipse)" : "Active: (none bound)")
            : $"Active: {activeName}";

        bool enableActions = (fam.HasActive || standDown);
        if (_toggleBtn != null) _toggleBtn.Component.interactable = enableActions;
        if (_unbindBtn != null) _unbindBtn.Component.interactable = enableActions;

        ClearChildren(_slotContainer);

        var slots = Settings.GetFamiliarQuickSpawnSlots();
        if (slots.Count == 0)
        {
            AddHint("(No familiars assigned yet — add them on the");
            AddHint("main panel's All Familiars tab.)");
            return;
        }

        foreach (var name in slots)
        {
            bool isActive = !string.IsNullOrEmpty(activeName)
                            && string.Equals(activeName, name, StringComparison.OrdinalIgnoreCase);
            string label = isActive ? $"● {name}" : name;

            var btn = UIFactory.CreateButton(_slotContainer, $"QSSlot_{name}", label);
            // Scale the button HEIGHT with the overlay font multiplier (not just the font):
            // at large overlay scales a fixed 26px row can't fit the bigger glyphs, so the
            // line was being dropped entirely. ScaledOverlayHeight grows the row in lockstep.
            UIFactory.SetLayoutElement(btn.GameObject,
                minWidth: 140, preferredWidth: 220, flexibleWidth: 1,
                minHeight: Theme.ScaledOverlayHeight(24), preferredHeight: Theme.ScaledOverlayHeight(26), flexibleHeight: 0);
            var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null)
            {
                t.alignment = Theme.OverlayMidlineAlignment();
                t.enableWordWrapping = false;
                // Auto-shrink so a long familiar name (or a large overlay font scale) shrinks to
                // stay readable instead of vanishing: with a fixed font + Ellipsis, text that
                // couldn't fit the button was dropped to nothing. fontSizeMax keeps the intended
                // 12pt look; the 7pt floor lets it shrink to fit; Ellipsis only truncates with
                // "…" when even the floor won't fit. (enableAutoSizing makes TMP ignore fontSize,
                // so we drive the size via the min/max pair.)
                t.enableAutoSizing = true;
                t.fontSizeMax = Theme.ScaledOverlay(12);
                t.fontSizeMin = 7f;
                t.overflowMode = TextOverflowModes.Ellipsis;
                if (isActive) t.fontStyle = FontStyles.Bold;
            }
            TooltipHover.Attach(btn.GameObject, isActive
                ? $"{name} is your active familiar — click to Dismiss / Recall it (.fam t)."
                : $"Summon {name} (.fam sb) — switches from your current familiar if one is active.");
            string captured = name;
            btn.OnClick = () => OnSlotClicked(captured);
        }
    }

    private void OnSlotClicked(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!MessageService.IsInitialized)
        {
            LogUtils.LogWarning("FamiliarQuickSpawnOverlay: cannot summon — MessageService not bound yet.");
            return;
        }

        bool standDown = EclipseProtocolService.StandDownForEclipse();
        var fam = PlayerStateService.Familiar;

        // Already the active familiar (only knowable when not standing down):
        // treat the click as Dismiss / Recall.
        if (!standDown && fam.HasActive
            && string.Equals(fam.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            Enqueue(MessageService.BCCOM_FAM_TOGGLE);
            return;
        }

        // Switch: release the current binding first (if one is bound, or
        // unconditionally under stand-down where we can't tell), then
        // smart-bind the requested familiar by name across all boxes.
        if (standDown || fam.HasActive)
            Enqueue(MessageService.BCCOM_FAM_UNBIND);
        Enqueue(string.Format(MessageService.BCCOM_FAM_SMARTBIND_FORMAT, name));
    }

    private void AddHint(string text)
    {
        var lbl = UIFactory.CreateLabel(_slotContainer, "QSHint", text,
            Theme.OverlayMidlineAlignment(), color: null, fontSize: Theme.ScaledOverlay(11));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 140, preferredWidth: 220, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Italic;
    }

    /// <summary>Public hook so the assignment UI / manager can force a refresh.</summary>
    public void RefreshSlots() => Render();

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
            PlayerStateService.FamiliarChanged -= OnStateChanged;
            SlotsChanged -= OnStateChanged;
            _subscribed = false;
        }
    }
}
