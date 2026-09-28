using System.Collections.Generic;
using Raphael.Config;
using Raphael.Services.Beelzebub;
using Raphael.UI.Framework.CustomLib.Panel;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Panels;
using Raphael.UI.ModContent.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UIBase = Raphael.UI.Framework.UniverseLib.UI.UIBase;

namespace Raphael.UI.ModContent;

// 0.18: "Beelz Action Bar" overlay — on-screen buttons for Beelzebub abilities
// BEYOND the 6 native spell slots (the headline "extra abilities" vision). One
// tile per `.beelz hotkey` binding; clicking force-casts it (.beelz cast <name>).
//
// Cooldown rings are CLIENT-side: Beelzebub exposes no live per-ability cooldown
// feed (handoff §7.4), only a static cooldown_seconds × cooldown_scale from
// `api info`. So on the authoritative `[BEELZ:event] type=cast` (relayed by
// BeelzProtocolService.AbilityCast) we start a local countdown of that effective
// cooldown and sweep a radial overlay, mirroring the Shift-spell overlay. If we
// don't yet know the ability's cooldown we lazily request `api info` and use a
// short default sweep until it arrives.
public class BeelzActionBarOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "BeelzActionBarOverlay";
    public override PanelType PanelType => PanelType.BeelzActionBarOverlay;

    public override int MinWidth  => 120;
    public override int MinHeight => 90;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Centered, a little above the bottom — clear of the native action bar.
    public override Vector2 DefaultPosition  => new(
        0f, -Owner.Scaler.m_ReferenceResolution.y * 0.5f + 200f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.BeelzActionBarOverlayTransparency);
    public override bool UsesCustomBackgroundColor => true;

    private const float DEFAULT_COOLDOWN_SECONDS = 8f; // used until api info reveals the real value
    private static readonly Color TILE_READY = new(0.30f, 0.18f, 0.36f, 0.95f); // muted violet (Beelzebub theme)
    private static readonly Color TILE_BUSY  = new(0.16f, 0.12f, 0.20f, 0.95f);
    private static readonly Color TILE_SLOT_BG = new(0.08f, 0.07f, 0.11f, 0.96f); // 0.18.4: dark backing behind the icon
    private static readonly Color RADIAL     = new(0.04f, 0.04f, 0.06f, 0.78f);

    private sealed class Tile
    {
        public string HotkeyName;
        public string AbilityGuid;
        public Image  Bg;
        public Image  Icon;          // 0.18.4: resolved ability icon (like the Shift overlay)
        public TextMeshProUGUI Name; // hotkey/ability name label — hidden once the icon resolves
        public Image  Radial;
        public TextMeshProUGUI Countdown;
        public int    IconHash;      // numeric PrefabGUID parsed from AbilityGuid (0 = none/unparseable)
        public bool   IconResolved;  // true once the icon sprite is assigned (stops the retry)
        public int    IconTries;     // 0.18.4: bounded retry — give up (text label) after a few attempts
        public float CooldownEnd;   // realtimeSinceStartup when ready again
        public float CooldownTotal; // for the fill fraction
    }

    private GameObject _tileRow;
    private TextMeshProUGUI _emptyLabel;
    private readonly List<Tile> _tiles = new();
    private System.Action _ticker;
    private bool _subscribed;
    private float _nextIconTryAt; // 0.18.4: throttle the per-tile icon-resolution retry

    // Shared 1×1 white sprite so the radial Image actually fills (sprite-less
    // Images ignore fillAmount). Same trick as ShiftSpellOverlayPanel.
    private static Sprite _whiteSprite;
    private static Sprite GetWhiteSprite()
    {
        if (_whiteSprite != null) return _whiteSprite;
        try
        {
            _whiteSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f));
        }
        catch { _whiteSprite = null; }
        return _whiteSprite;
    }

    public BeelzActionBarOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Beelz Action Bar");

        _tileRow = UIFactory.CreateHorizontalGroup(ContentRoot, "BeelzTileRow",
            forceExpandWidth: false, forceExpandHeight: false,
            childControlWidth: false, childControlHeight: false,
            spacing: 4, padding: new Vector4(2, 2, 2, 2),
            childAlignment: TextAnchor.MiddleCenter);
        UIFactory.SetLayoutElement(_tileRow,
            minWidth: MinWidth - 8, preferredWidth: MinWidth - 8, flexibleWidth: 1,
            minHeight: 66, preferredHeight: 66, flexibleHeight: 0);

        _emptyLabel = UIFactory.CreateLabel(ContentRoot, "BeelzActionEmpty",
            "No hotkey abilities bound.\nBind some on the Beelzebub → Hotkeys tab.",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledOverlay(11)).TextMesh;
        UIFactory.SetLayoutElement(_emptyLabel.gameObject,
            minWidth: 100, preferredWidth: 140, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 44, flexibleHeight: 0);
        _emptyLabel.fontStyle = FontStyles.Italic;

        if (!_subscribed)
        {
            BeelzState.HotkeysChanged += RebuildTiles;
            BeelzProtocolService.AbilityCast += OnAbilityCast;
            _subscribed = true;
        }
        if (_ticker == null)
        {
            _ticker = OnTick;
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_ticker);
        }

        RebuildTiles();
        // Ask for the latest bindings if we don't have them yet.
        if (BeelzState.Present && BeelzState.Hotkeys.Count == 0)
            BeelzClient.RequestHotkeys();
    }

    private void RebuildTiles()
    {
        if (_tileRow == null) return;

        // Tear down existing tiles.
        for (int i = _tileRow.transform.childCount - 1; i >= 0; --i)
        {
            var c = _tileRow.transform.GetChild(i);
            if (c != null) UnityEngine.Object.Destroy(c.gameObject);
        }
        _tiles.Clear();

        var hotkeys = BeelzState.Hotkeys;
        bool any = hotkeys.Count > 0 && BeelzState.HotkeysEnabled;
        // Collapse the tile row when empty so its reserved height doesn't push the
        // "no hotkeys bound" note past the bottom edge of the overlay (overlap fix).
        if (_tileRow != null) _tileRow.SetActive(any);
        if (_emptyLabel != null) _emptyLabel.gameObject.SetActive(!any);
        if (!any) return;

        foreach (var hk in hotkeys)
            _tiles.Add(BuildTile(hk));
    }

    private Tile BuildTile(BeelzHotkey hk)
    {
        var tile = new Tile { HotkeyName = hk.Name, AbilityGuid = hk.AbilityGuid };

        var go = UIFactory.CreateUIObject($"BeelzTile_{hk.Name}", _tileRow);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(56, 56);
        UIFactory.SetLayoutElement(go,
            minWidth: 56, preferredWidth: 56, flexibleWidth: 0,
            minHeight: 56, preferredHeight: 56, flexibleHeight: 0);

        // Background acts as the click target (force-cast on click).
        tile.Bg = go.AddComponent<Image>();
        tile.Bg.color = TILE_READY;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = tile.Bg;
        string castName = hk.Name;
        btn.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
        {
            try { BeelzClient.Cast(castName); }
            catch (System.Exception ex) { Utils.LogUtils.LogError($"Beelz cast '{castName}' failed: {ex}"); }
        }));
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0.85f, 0.80f, 0.90f, 0.9f);
        outline.effectDistance = new Vector2(1.1f, -1.1f);

        // 0.18.4: dark slot backing (inset, so the violet bg reads as a thin frame) + the ability ICON
        // on top of it — so transparent areas of the icon show dark, not violet (the Shift-overlay
        // "no blue film" lesson). The icon is resolved from the ability's PrefabGUID and stays hidden
        // until it's available; the name label below covers the not-yet-resolved / no-icon case.
        var backingGo = UIFactory.CreateUIObject("Backing", go);
        var backingRt = backingGo.GetComponent<RectTransform>();
        backingRt.anchorMin = Vector2.zero; backingRt.anchorMax = Vector2.one;
        backingRt.offsetMin = new Vector2(2.5f, 2.5f); backingRt.offsetMax = new Vector2(-2.5f, -2.5f);
        var backing = backingGo.AddComponent<Image>();
        backing.color = TILE_SLOT_BG;
        backing.raycastTarget = false;

        var iconGo = UIFactory.CreateUIObject("Icon", go);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = Vector2.zero; iconRt.anchorMax = Vector2.one;
        iconRt.offsetMin = new Vector2(4f, 4f); iconRt.offsetMax = new Vector2(-4f, -4f);
        tile.Icon = iconGo.AddComponent<Image>();
        tile.Icon.raycastTarget = false;
        tile.Icon.preserveAspect = true;
        tile.Icon.enabled = false; // no sprite yet → resolved + shown in OnTick
        int.TryParse(hk.AbilityGuid, out tile.IconHash);

        // Radial cooldown sweep (over the background, under the text).
        var radialGo = UIFactory.CreateUIObject("Radial", go);
        var radialRt = radialGo.GetComponent<RectTransform>();
        radialRt.anchorMin = Vector2.zero; radialRt.anchorMax = Vector2.one;
        radialRt.offsetMin = Vector2.zero; radialRt.offsetMax = Vector2.zero;
        tile.Radial = radialGo.AddComponent<Image>();
        tile.Radial.color = RADIAL;
        tile.Radial.raycastTarget = false;
        var ws = GetWhiteSprite();
        if (ws != null)
        {
            tile.Radial.sprite = ws;
            tile.Radial.type = Image.Type.Filled;
            tile.Radial.fillMethod = Image.FillMethod.Radial360;
            tile.Radial.fillOrigin = 2;       // top
            tile.Radial.fillClockwise = true;
            tile.Radial.fillAmount = 0f;
        }
        else { var col = tile.Radial.color; col.a = 0f; tile.Radial.color = col; }
        tile.Radial.enabled = false; // 0.18.4: only on while actually cooling (no lingering film)

        // Label: ability/hotkey name (top), countdown (center, while cooling).
        var nameGo = UIFactory.CreateUIObject("Name", go);
        var nameRt = nameGo.GetComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0, 0); nameRt.anchorMax = new Vector2(1, 1);
        nameRt.offsetMin = Vector2.zero; nameRt.offsetMax = Vector2.zero;
        var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
        nameTmp.alignment = TextAlignmentOptions.Top;
        nameTmp.fontSize = Theme.ScaledOverlay(9);
        nameTmp.color = Color.white;
        nameTmp.enableWordWrapping = false;
        nameTmp.overflowMode = TextOverflowModes.Ellipsis;
        nameTmp.raycastTarget = false;
        nameTmp.outlineWidth = 0.2f; nameTmp.outlineColor = new Color32(0, 0, 0, 210);
        nameTmp.text = string.IsNullOrEmpty(hk.Name) ? "?" : hk.Name;
        tile.Name = nameTmp; // hidden once the icon resolves (the icon then represents the ability)

        var cdGo = UIFactory.CreateUIObject("Countdown", go);
        var cdRt = cdGo.GetComponent<RectTransform>();
        cdRt.anchorMin = Vector2.zero; cdRt.anchorMax = Vector2.one;
        cdRt.offsetMin = Vector2.zero; cdRt.offsetMax = Vector2.zero;
        tile.Countdown = cdGo.AddComponent<TextMeshProUGUI>();
        tile.Countdown.alignment = TextAlignmentOptions.Center;
        tile.Countdown.fontSize = Theme.ScaledOverlay(18);
        tile.Countdown.fontStyle = FontStyles.Bold;
        tile.Countdown.color = Color.white;
        tile.Countdown.raycastTarget = false;
        tile.Countdown.outlineWidth = 0.25f; tile.Countdown.outlineColor = new Color32(0, 0, 0, 220);
        tile.Countdown.text = "";

        // DYNAMIC tooltip: shows the ability name + description once `api info`/`info-guid` resolves
        // for this guid (the static version only ever showed the hotkey name → the "No name/desc on
        // hover" report). Falls back to the friendly hotkey/ability name until the info arrives.
        string abilGuid = hk.AbilityGuid;
        string fallbackName = string.IsNullOrEmpty(hk.AbilityName) ? hk.Name : BeelzNames.Ability(hk.AbilityName);
        TooltipHover.Attach(go, () =>
        {
            string title = fallbackName;
            string desc = "";
            if (BeelzState.TryGetAbilityInfo(abilGuid, out var info) && info != null)
            {
                if (!string.IsNullOrEmpty(info.Label)) title = info.Label;
                else if (!string.IsNullOrEmpty(info.AbilityName)) title = BeelzNames.Ability(info.AbilityName);
                if (!string.IsNullOrEmpty(info.Desc)) desc = $"\n{info.Desc}";
                if (BeelzState.SupportsRuntimeCooldowns && info.UsesCharges) desc += "\nUses charges";
                else if (BeelzState.SupportsRuntimeCooldowns) desc += $"\nCooldown {info.EffectiveCooldownSeconds:0.#}s";
            }
            // api32: `.beelz cast` refuses an ability an incompatibility lock is keeping off the bar.
            if (BeelzState.IsLockedNow(abilGuid, out var lockGroup))
                desc += $"\n<color=#FF8080>Locked by '{lockGroup}' — the server will refuse this cast until the lock clears.</color>";
            return $"{title} — click to force-cast (.beelz cast {castName}).{desc}" +
                   "\nThe ring shows an estimated cooldown (client-side; the server enforces the real one).";
        });

        // Fetch the ability's tooltip data (name/desc/cooldown). RequestInfoForAbility maps the guid to a
        // capture index for `api info`, else falls back to `api info-guid` (works for non-captured/active
        // bar abilities) — so the dynamic tooltip fills in once the reply arrives.
        if (!BeelzState.TryGetAbilityInfo(hk.AbilityGuid, out _))
            RequestInfoForAbility(hk.AbilityGuid);

        // 0.18.4: icon resolution is deferred to the throttled OnTick (off this heavy build frame, and
        // only when Settings.EnableBeelzAbilityIcons is on) — see the crash note in OnTick.

        return tile;
    }

    private static void RequestInfoForAbility(string abilityGuid)
    {
        if (string.IsNullOrEmpty(abilityGuid)) return;
        foreach (var cap in BeelzState.Captures)
        {
            if (cap.AbilityGuid == abilityGuid)
            {
                BeelzClient.RequestInfo(cap.Index);
                return;
            }
        }
        // Not in the capture list (active-bar / admin-granted ability) → fetch by GUID directly
        // (Beelz v0.84 `api info-guid`), so the cooldown ring + tooltip get real data instead of
        // the default sweep / "No name".
        BeelzClient.RequestInfoGuid(abilityGuid);
    }

    private void OnAbilityCast(string abilityGuid)
    {
        float total = DEFAULT_COOLDOWN_SECONDS;
        if (BeelzState.TryGetAbilityInfo(abilityGuid, out var info))
            total = info.EffectiveCooldownSeconds;

        float now = Time.realtimeSinceStartup;
        foreach (var t in _tiles)
        {
            if (t.AbilityGuid != abilityGuid) continue;
            t.CooldownTotal = total;
            t.CooldownEnd   = now + total;
        }
    }

    private void OnTick()
    {
        if (_tiles.Count == 0) return;
        float now = Time.realtimeSinceStartup;

        // 0.18.4: resolve ability icons — gated OFF by default (Settings.EnableBeelzAbilityIcons) after a
        // traceless native crash on the Beelz server; when off, this whole interop path stays dormant and
        // the buttons keep their text labels. When on: throttled, ONE tile per tick (spreads the managed
        // prefab reads off any single frame), bounded retries (give up → text label) so an unresolvable
        // ability can't hammer the resolver forever.
        if (Config.Settings.EnableBeelzAbilityIcons && now >= _nextIconTryAt)
        {
            _nextIconTryAt = now + 0.4f;
            foreach (var t in _tiles)
            {
                if (t.IconResolved || t.IconHash == 0 || t.IconTries >= 12) continue;
                t.IconTries++;
                TryResolveTileIcon(t);
                break; // one per tick
            }
        }

        foreach (var t in _tiles)
        {
            if (t.Radial == null) continue;
            float remaining = t.CooldownEnd - now;
            bool cooling = remaining > 0.05f;
            if (!cooling)
            {
                // Ready: cooldown layer fully OFF (no film over the icon), tile in its ready color.
                if (t.Radial.enabled) t.Radial.enabled = false;
                if (t.Bg != null) t.Bg.color = TILE_READY;
                if (t.Countdown != null && t.Countdown.text.Length != 0) t.Countdown.text = "";
                continue;
            }
            if (!t.Radial.enabled) t.Radial.enabled = true;
            t.Radial.fillAmount = t.CooldownTotal > 0.01f ? Mathf.Clamp01(remaining / t.CooldownTotal) : 0f;
            if (t.Bg != null) t.Bg.color = TILE_BUSY;
            if (t.Countdown != null)
                t.Countdown.text = remaining < 10f ? $"{remaining:0.0}" : $"{(int)remaining}";
        }
    }

    // 0.18.4: resolve the ability's icon from its PrefabGUID and show it on the tile (like the Shift
    // overlay). Hides the name label once the icon is up — the art represents the ability; the full
    // name stays on the tooltip. No-op once resolved. Best-effort: a tile with no resolvable icon just
    // keeps its name label (the prior look).
    private void TryResolveTileIcon(Tile t)
    {
        if (t == null || t.Icon == null) return;
        if (Services.AbilityIconResolver.TryGetIcon(t.IconHash, out var sprite) && sprite != null)
        {
            t.Icon.sprite = sprite;
            if (!t.Icon.enabled) t.Icon.enabled = true;
            t.IconResolved = true;
            if (t.Name != null && t.Name.gameObject.activeSelf) t.Name.gameObject.SetActive(false);
        }
    }

    internal override void Reset()
    {
        if (_subscribed)
        {
            BeelzState.HotkeysChanged -= RebuildTiles;
            BeelzProtocolService.AbilityCast -= OnAbilityCast;
            _subscribed = false;
        }
        if (_ticker != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_ticker);
            _ticker = null;
        }
        _tiles.Clear();
    }
}
