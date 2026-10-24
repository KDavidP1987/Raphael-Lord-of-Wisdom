using Raphael.Config;
using Raphael.Services.Beelzebub;
using Raphael.UI.Framework.CustomLib.Panel;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.UI.Framework.UniverseLib.UI.Panels;
using Raphael.UI.ModContent.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Raphael.UI.Framework.CustomLib.Util;
using UIBase = Raphael.UI.Framework.UniverseLib.UI.UIBase;

namespace Raphael.UI.ModContent;

// 0.20: "Transforms" overlay — a browser-style on-screen panel for the Beelzebub boss forms, so you can
// transform / switch phase / revert without opening the main panel. Mirrors the Familiar Browser overlay:
// it lists your unlocked transforms; DOUBLE-CLICK a row to transform into it. The active form is shown at
// top with Phase 1 / Phase 2 / Revert buttons.
//
// All data comes from BeelzState (api transforms + api active); no live ticker — it refreshes on the
// TransformsChanged / ActiveChanged events (and a Refresh button re-pulls).
public class BeelzTransformOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "BeelzTransformOverlay";
    public override PanelType PanelType => PanelType.BeelzTransformOverlay;

    public override int MinWidth  => 220;
    public override int MinHeight => 150;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Left-center by default, offset from the other Beelz overlays.
    public override Vector2 DefaultPosition  => new(
        -Owner.Scaler.m_ReferenceResolution.x * 0.5f + 240f, 60f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.BeelzTransformOverlayTransparency);
    public override bool UsesCustomBackgroundColor => true;

    private static readonly Color ROW_ACTIVE = new(0.18f, 0.34f, 0.22f, 0.95f); // green: the active form
    private static readonly Color ROW_ARMED  = new(0.30f, 0.18f, 0.36f, 0.95f); // violet: click-again-to-transform

    private GameObject _activeRow;
    private GameObject _listContainer;
    private TextMeshProUGUI _status;
    private bool _subscribed;

    // Double-click-to-transform (non-destructive, so a soft two-click rather than a confirm): first click
    // arms the row, a second click on the SAME row within the window transforms.
    private string _armedGuid;
    private float _armedAt;
    private const float DOUBLE_CLICK_SECONDS = 0.6f;

    public BeelzTransformOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Transforms");

        var toolbar = UIFactory.CreateHorizontalGroup(ContentRoot, "BeelzTfOvToolbar",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(toolbar, minWidth: MinWidth - 8, flexibleWidth: 1, minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var refreshBtn = UIFactory.CreateButton(toolbar, "BeelzTfOvRefresh", "Refresh");
        UIFactory.SetLayoutElement(refreshBtn.GameObject, minWidth: 70, preferredWidth: 90, flexibleWidth: 0, minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        refreshBtn.OnClick = () => { if (BeelzState.Present) { BeelzClient.RequestTransforms(); BeelzClient.RequestActive(); } };
        TooltipHover.Attach(refreshBtn.GameObject, "Re-pull your transforms + the active form (api transforms / active).");

        _activeRow = UIFactory.CreateVerticalGroup(ContentRoot, "BeelzTfOvActive",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(_activeRow, minWidth: MinWidth - 8, flexibleWidth: 1, minHeight: 26, flexibleHeight: 0);

        _listContainer = UIFactory.CreateVerticalGroup(ContentRoot, "BeelzTfOvList",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(_listContainer, minWidth: MinWidth - 8, flexibleWidth: 1, minHeight: 40, flexibleHeight: 1);

        _status = UIFactory.CreateLabel(ContentRoot, "BeelzTfOvStatus", "",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledOverlay(10)).TextMesh;
        UIFactory.SetLayoutElement(_status.gameObject, minWidth: MinWidth - 8, flexibleWidth: 1, minHeight: 16, preferredHeight: 18, flexibleHeight: 0);
        _status.fontStyle = FontStyles.Italic;
        _status.enableWordWrapping = true;

        if (!_subscribed)
        {
            BeelzState.TransformsChanged += Rebuild;
            BeelzState.ActiveChanged += Rebuild;
            _subscribed = true;
        }

        Rebuild();
        if (BeelzState.Present && BeelzState.Transforms.Count == 0) { BeelzClient.RequestTransforms(); BeelzClient.RequestActive(); }
    }

    private void SetStatus(string msg)
    {
        if (_status == null) return;
        _status.text = msg ?? "";
        _status.gameObject.SetActive(!string.IsNullOrEmpty(msg));
    }

    private void Rebuild()
    {
        if (_listContainer == null || _activeRow == null) return;
        for (int i = _activeRow.transform.childCount - 1; i >= 0; --i) UnityEngine.Object.Destroy(_activeRow.transform.GetChild(i).gameObject);
        for (int i = _listContainer.transform.childCount - 1; i >= 0; --i) UnityEngine.Object.Destroy(_listContainer.transform.GetChild(i).gameObject);

        if (!BeelzState.Present) { AddSimpleLabel(_listContainer, "(Beelzebub not detected)"); return; }

        // ---- active form + phase / revert controls ----
        var active = BeelzState.Active;
        bool isActive = active != null && !active.None;
        if (isActive)
        {
            string name = BeelzNames.Unit(active.UnitName);
            string ttl = string.IsNullOrEmpty(active.Ttl) ? "" : $"  ({active.Ttl})";
            AddSimpleLabel(_activeRow, $"<color=#90EE90>Active:</color> {name}   <color=#BFBFBF>Phase {active.Phase}{ttl}</color>");
            var ctl = UIFactory.CreateHorizontalGroup(_activeRow, "BeelzTfOvActiveBtns",
                forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
                spacing: 4, padding: new Vector4(0, 0, 0, 0));
            UIFactory.SetLayoutElement(ctl, minWidth: MinWidth - 12, flexibleWidth: 1, minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
            // 0.73.0: phase clicks go through BeelzClient's shared in-flight lock (also used by the Transforms
            // tab's Phase cycle button), so a switch can't be re-sent / raced before the last one lands.
            AddOverlayButton(ctl, "BeelzTfOvP1", "Phase 1", "Switch to phase 1 (.beelz phase 1). Ignored for a moment while the last switch lands.", () => OnPhaseClick(1));
            AddOverlayButton(ctl, "BeelzTfOvP2", "Phase 2", "Switch to phase 2 (.beelz phase 2). Ignored for a moment while the last switch lands.", () => OnPhaseClick(2));
            AddOverlayButton(ctl, "BeelzTfOvRevert", "Revert", "End the active transform (.beelz revert).", () => { BeelzClient.Revert(); SetStatus("Reverting…"); }, danger: true);
        }
        else
        {
            AddSimpleLabel(_activeRow, "<color=#BFBFBF>No active transform — double-click a form below.</color>");
        }

        // ---- the unlocked transforms ----
        var transforms = BeelzState.Transforms;
        if (transforms.Count == 0)
        {
            AddSimpleLabel(_listContainer, "(no transforms unlocked — defeat a transform boss)");
            return;
        }
        foreach (var t in transforms)
        {
            string guid = t.UnitGuid;
            string disp = BeelzNames.Unit(t.UnitName);
            bool isThisActive = isActive && active.UnitGuid == guid;
            bool armed = _armedGuid == guid;

            var rowGo = UIFactory.CreateUIObject($"BeelzTfOvRow_{guid}", _listContainer);
            UIFactory.SetLayoutElement(rowGo, minWidth: MinWidth - 12, flexibleWidth: 1, minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
            var bg = rowGo.AddComponent<Image>();
            bg.color = isThisActive ? ROW_ACTIVE : (armed ? ROW_ARMED : new Color(0.16f, 0.14f, 0.20f, 0.85f));
            var btn = rowGo.AddComponent<Button>();
            btn.targetGraphic = bg;

            var lbl = UIFactory.CreateLabel(rowGo, "Label",
                $"{disp}{(t.Enabled ? "" : "  <color=#FFB070>(disabled)</color>")}{(t.Shard ? "  <color=#BFBFBF>· shard</color>" : "")}",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledOverlay(12));
            var lrt = lbl.GameObject.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(6, 0); lrt.offsetMax = new Vector2(-6, 0);
            lbl.TextMesh.raycastTarget = false;
            lbl.TextMesh.enableWordWrapping = false;
            lbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;

            // NOTE: `.beelz transform <arg>` resolves <arg> as an INDEX (when 0..unlockCount-1) or a NAME —
            // NOT a raw unit GUID (that's what `tform` accepts). So transform by the form's INDEX, exactly
            // like the main-panel Transforms tab. We keep the GUID only for the active-row highlight + arming.
            string capName = disp; string capGuid = guid; int capIndex = t.Index;
            btn.onClick.AddListener((UnityEngine.Events.UnityAction)(() => OnRowClick(capGuid, capIndex, capName)));
            TooltipHover.Attach(rowGo, $"Double-click to transform into {disp} (.beelz transform). Failure reasons reply in chat.");
        }
    }

    private void OnPhaseClick(int n)
    {
        if (BeelzClient.TryPhase(n)) SetStatus($"Switching to phase {n}…");
        else if (BeelzClient.PhaseSwitchInFlight) SetStatus("A phase switch is still landing — try again in a moment.");
    }

    private void OnRowClick(string guid, int index, string name)
    {
        float now = Time.realtimeSinceStartup;
        if (_armedGuid == guid && now - _armedAt < DOUBLE_CLICK_SECONDS)
        {
            _armedGuid = null;
            try { BeelzClient.TransformSafe(index.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            catch (System.Exception ex) { Utils.LogUtils.LogError($"Beelz transform '{name}' failed: {ex}"); }
            SetStatus($"<color=#90EE90>Transforming into {name}…</color> (Watch chat for the result.)");
        }
        else
        {
            _armedGuid = guid; _armedAt = now;
            SetStatus($"Click {name} again to transform.");
        }
        Rebuild(); // reflect the armed highlight
    }

    private void AddSimpleLabel(GameObject parent, string text)
    {
        var l = UIFactory.CreateLabel(parent, "Lbl", text, TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledOverlay(11));
        UIFactory.SetLayoutElement(l.GameObject, minWidth: MinWidth - 12, flexibleWidth: 1, minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        l.TextMesh.enableWordWrapping = true;
    }

    private void AddOverlayButton(GameObject parent, string name, string label, string tooltip, System.Action onClick, bool danger = false)
    {
        var b = UIFactory.CreateButton(parent, name, label);
        UIFactory.SetLayoutElement(b.GameObject, minWidth: 56, preferredWidth: 80, flexibleWidth: 1, minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var txt = b.Component != null ? b.Component.GetComponentInChildren<TextMeshProUGUI>() : null;
        if (txt != null) { txt.fontSize = Theme.ScaledOverlay(11); txt.alignment = TextAlignmentOptions.Center; }
        if (danger) { var img = b.Component != null ? b.Component.GetComponent<Image>() : null; if (img != null) img.color = new Color(0.55f, 0.18f, 0.18f, 0.95f); }
        b.OnClick = () => onClick?.Invoke();
        TooltipHover.Attach(b.GameObject, tooltip);
    }

    internal override void Reset()
    {
        if (_subscribed)
        {
            BeelzState.TransformsChanged -= Rebuild;
            BeelzState.ActiveChanged -= Rebuild;
            _subscribed = false;
        }
    }
}
