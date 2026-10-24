using Raphael.Config;
using Raphael.Services.Beelzebub;
using Raphael.UI.Framework.CustomLib.Panel;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.UI.Framework.UniverseLib.UI.Panels;
using Raphael.UI.ModContent.Data;
using TMPro;
using UnityEngine;
using UIBase = Raphael.UI.Framework.UniverseLib.UI.UIBase;

namespace Raphael.UI.ModContent;

// 0.19: "Summons" overlay — a small on-screen panel that toggles your active
// Beelzebub minions between STASHED and RESTORED with one click (.beelz summons
// stash / restore). Summon management is a general Beelzebub feature (independent
// of being transformed — Beelz v0.45), so it gets its own quick-access overlay
// rather than living buried in the Transforms tab. Clear rides along for
// convenience.
//
// There's no live "are my summons stashed?" feed from the server (handoff §7.4),
// so the toggle's stash/restore state is CLIENT-side optimistic — same approach
// as the action-bar's client-side cooldown rings. The primary button flips its
// own label on each press; the small buttons are stateless one-shots.
public class BeelzSummonsOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "BeelzSummonsOverlay";
    public override PanelType PanelType => PanelType.BeelzSummonsOverlay;

    public override int MinWidth  => 120;
    public override int MinHeight => 58;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Lower-left, offset so it doesn't stack on the Quick Actions / Beelz bar overlays.
    public override Vector2 DefaultPosition  => new(
        -Owner.Scaler.m_ReferenceResolution.x * 0.5f + 220f,
        -Owner.Scaler.m_ReferenceResolution.y * 0.5f + 340f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.BeelzSummonsOverlayTransparency);
    public override bool UsesCustomBackgroundColor => true;

    private static readonly Color STASH_COLOR   = new(0.30f, 0.18f, 0.36f, 0.95f); // muted violet (Beelzebub theme)
    private static readonly Color RESTORE_COLOR = new(0.18f, 0.34f, 0.22f, 0.95f); // green-ish "bring them back"

    private bool _stashed; // client-side optimistic state — flips on each toggle press
    private ButtonRef _toggleBtn;

    public BeelzSummonsOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Summons");

        // Primary toggle: Stash ⟷ Restore.
        _toggleBtn = UIFactory.CreateButton(ContentRoot, "BeelzSummonsToggle", "Stash Summons");
        UIFactory.SetLayoutElement(_toggleBtn.GameObject,
            minWidth: 100, preferredWidth: 150, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 32, flexibleHeight: 0);
        _toggleBtn.OnClick = OnToggle;
        ApplyToggleVisual();
        TooltipHover.Attach(_toggleBtn.GameObject,
            "Toggle your summons between stashed and restored.\n" +
            "• Stash (.beelz summons stash) — tuck your minions away (e.g. before a waygate).\n" +
            "• Restore (.beelz summons restore) — bring the stashed minions back.\n" +
            "The label tracks what the next press will do (client-side — the server is authoritative).");

        // Secondary one-shot button: Clear. (0.73.0: Recall removed — `.beelz tp` only stashes, which the
        // toggle above already does. Clear's flexibleWidth fills the row on its own; no size change needed.)
        var row = UIFactory.CreateHorizontalGroup(ContentRoot, "BeelzSummonsRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 2, 0));
        UIFactory.SetLayoutElement(row,
            minWidth: 100, preferredWidth: 150, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 28, flexibleHeight: 0);

        var clearBtn = UIFactory.CreateButton(row, "BeelzSummonsClear", "Clear");
        UIFactory.SetLayoutElement(clearBtn.GameObject,
            minWidth: 48, preferredWidth: 72, flexibleWidth: 1, minHeight: 22, preferredHeight: 26, flexibleHeight: 0);
        clearBtn.OnClick = () => { Send(".beelz summons clear"); _stashed = false; ApplyToggleVisual(); };
        TooltipHover.Attach(clearBtn.GameObject, "Despawn ALL your summons (.beelz summons clear).");
    }

    private void OnToggle()
    {
        if (!_stashed) { Send(".beelz summons stash"); _stashed = true; }
        else           { Send(".beelz summons restore"); _stashed = false; }
        ApplyToggleVisual();
    }

    private void ApplyToggleVisual()
    {
        if (_toggleBtn == null) return;
        var txt = _toggleBtn.Component != null ? _toggleBtn.Component.GetComponentInChildren<TextMeshProUGUI>() : null;
        if (txt != null) txt.text = _stashed ? "Restore Summons" : "Stash Summons";
        var img = _toggleBtn.Component != null ? _toggleBtn.Component.GetComponent<UnityEngine.UI.Image>() : null;
        if (img != null) img.color = _stashed ? RESTORE_COLOR : STASH_COLOR;
    }

    private static void Send(string cmd)
    {
        try { BeelzClient.SendUser(cmd); }
        catch (System.Exception ex) { Utils.LogUtils.LogError($"Beelz summons overlay '{cmd}' failed: {ex}"); }
    }

    internal override void Reset() { /* no tickers / subscriptions to clean up */ }
}
