using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Raphael.Config;

/// <summary>0.15.0: lightweight serializable hotkey. BepInEx 6 (V Rising's
/// IL2CPP build) dropped the older 5.x KeyboardShortcut from its public
/// surface so we own a minimal copy here. Stored in the .cfg as a "+"-
/// delimited string ("Insert", "F3", "LeftControl+H", "LeftShift+F5").
/// IsDown() returns true on the frame the main key transitions Up -> Down
/// AND every modifier is currently held — same semantics as the upstream
/// KeyboardShortcut.IsDown().</summary>
public struct BCHotkey : IEquatable<BCHotkey>
{
    public KeyCode MainKey;
    public KeyCode[] Modifiers;

    public static BCHotkey Empty => default;
    public bool IsEmpty => MainKey == KeyCode.None;

    public bool IsDown()
    {
        if (IsEmpty) return false;
        if (!Input.GetKeyDown(MainKey)) return false;
        if (Modifiers != null)
        {
            for (int i = 0; i < Modifiers.Length; i++)
                if (!Input.GetKey(Modifiers[i])) return false;
        }
        return true;
    }

    public override string ToString()
    {
        if (IsEmpty) return string.Empty;
        if (Modifiers == null || Modifiers.Length == 0) return MainKey.ToString();
        return string.Join("+", Modifiers) + "+" + MainKey;
    }

    public static BCHotkey Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Empty;
        var parts = s.Split('+');
        if (parts.Length == 0) return Empty;
        var main = ParseKey(parts[parts.Length - 1].Trim());
        if (main == KeyCode.None) return Empty;
        var mods = new List<KeyCode>();
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var m = ParseKey(parts[i].Trim());
            if (m != KeyCode.None) mods.Add(m);
        }
        return new BCHotkey { MainKey = main, Modifiers = mods.Count > 0 ? mods.ToArray() : null };
    }

    private static KeyCode ParseKey(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return KeyCode.None;
        // Accept short modifier aliases for ergonomics in the .cfg file.
        switch (s.Trim().ToLowerInvariant())
        {
            case "ctrl":
            case "control":  return KeyCode.LeftControl;
            case "alt":      return KeyCode.LeftAlt;
            case "shift":    return KeyCode.LeftShift;
            case "cmd":
            case "win":
            case "windows":  return KeyCode.LeftWindows;
        }
        return Enum.TryParse<KeyCode>(s, true, out var k) ? k : KeyCode.None;
    }

    public bool Equals(BCHotkey other)
    {
        if (MainKey != other.MainKey) return false;
        int a = Modifiers?.Length ?? 0;
        int b = other.Modifiers?.Length ?? 0;
        if (a != b) return false;
        for (int i = 0; i < a; i++) if (Modifiers[i] != other.Modifiers[i]) return false;
        return true;
    }
    public override bool Equals(object obj) => obj is BCHotkey o && Equals(o);
    public override int GetHashCode() => (int)MainKey;
}

// Static settings registry. Modeled on BloodCraftUI's Config/Settings.cs.
// The copied UI framework references `Settings.UITransparency` (etc.) as
// static, so this class is static-by-design.
//
// To add a setting:
//   1. Pick a section constant below (or add one).
//   2. Add a public-static getter that reads from ConfigEntries via nameof().
//   3. Add a matching InitConfigEntry(...) call in InitConfig().
public class Settings
{
    private static string CONFIG_PATH = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME);
    private static readonly Dictionary<string, ConfigEntryBase> ConfigEntries = new();

    public const string UI_SETTINGS_GROUP       = "UISettings";
    public const string FAM_SETTINGS_GROUP      = "FamiliarSettings";
    public const string GENERAL_SETTINGS_GROUP  = "GeneralOptions";
    public const string OVERLAY_SETTINGS_GROUP  = "Overlays";
    // 0.17.2: crash-compatibility kill-switches. These let an affected player (or a
    // tester) reduce Raphael's always-on footprint to bisect the intermittent 0.16.x
    // load crash — an upstream Il2CppInterop GC-finalizer fault that Raphael only
    // *triggers* under login-window pressure, worse when other client mods churn at
    // the same time. All default to full functionality; flip OFF only to diagnose.
    public const string COMPAT_SETTINGS_GROUP   = "Compatibility";

    // ---- UI / general ----
    public static float UITransparency =>
        (ConfigEntries[nameof(UITransparency)] as ConfigEntry<float>)?.Value ?? 0.6f;

    // 0.9.0: two-axis text scaling. The UI scale affects the main tabbed
    // panel; the overlay scale affects the four secondary overlays (XP /
    // Familiar / Familiar Browser / Daily Quest / Professions). Stored as a
    // float multiplier so the user can pick Small/Standard/Large from a
    // segmented control and a future "fine-grained" slider can drop in
    // without a config schema change.
    public static float UITextScale =>
        (ConfigEntries[nameof(UITextScale)] as ConfigEntry<float>)?.Value ?? 1.0f;
    public static float OverlayTextScale =>
        (ConfigEntries[nameof(OverlayTextScale)] as ConfigEntry<float>)?.Value ?? 1.0f;
    public static void SetUITextScale(float v)       => SetFloat(nameof(UITextScale), v);
    public static void SetOverlayTextScale(float v)  => SetFloat(nameof(OverlayTextScale), v);

    // 0.9.0: per-overlay background transparency. User semantics (per
    // friend-testing direction): 0.0 = solid (opaque), 1.0 = invisible.
    // B6 (0.19): floor raised 0.95 → 1.0 so 100% is TRULY invisible (tester
    // request — the old 5% film read as "not actually transparent"). This is
    // safe to find/move: the background Image keeps raycastTarget=true (alpha
    // doesn't gate raycasts), so the panel is still draggable even when the
    // background is fully clear; foreground content (text/icons) stays opaque;
    // and the "Lock overlays" toggle + Settings → reset are escape hatches.
    public const float OVERLAY_TRANSPARENCY_FLOOR = 1.0f;

    public static float XPOverlayTransparency        => GetFloat(nameof(XPOverlayTransparency),        UITransparency);
    public static float FamiliarOverlayTransparency  => GetFloat(nameof(FamiliarOverlayTransparency),  UITransparency);
    public static float FamiliarBrowserTransparency  => GetFloat(nameof(FamiliarBrowserTransparency),  UITransparency);
    public static float FamiliarQuickSpawnOverlayTransparency => GetFloat(nameof(FamiliarQuickSpawnOverlayTransparency), UITransparency);
    public static float FaustBossTrackerOverlayTransparency => GetFloat(nameof(FaustBossTrackerOverlayTransparency), UITransparency);
    public static float ShiftSpellOverlayTransparency => GetFloat(nameof(ShiftSpellOverlayTransparency), UITransparency);
    public static float QuickActionsOverlayTransparency => GetFloat(nameof(QuickActionsOverlayTransparency), UITransparency);
    public static float BeelzActionBarOverlayTransparency => GetFloat(nameof(BeelzActionBarOverlayTransparency), UITransparency);
    public static float BeelzSummonsOverlayTransparency => GetFloat(nameof(BeelzSummonsOverlayTransparency), UITransparency);
    public static float BeelzTransformOverlayTransparency => GetFloat(nameof(BeelzTransformOverlayTransparency), UITransparency);
    public static float UrielSharedOverlayTransparency => GetFloat(nameof(UrielSharedOverlayTransparency), UITransparency);
    public static float UrielObjectSpawnerOverlayTransparency => GetFloat(nameof(UrielObjectSpawnerOverlayTransparency), UITransparency);
    public static float ChatWindowOverlayTransparency => GetFloat(nameof(ChatWindowOverlayTransparency), UITransparency);
    public static float DailyQuestTransparency       => GetFloat(nameof(DailyQuestTransparency),       UITransparency);
    public static float ProfessionOverlayTransparency => GetFloat(nameof(ProfessionOverlayTransparency), UITransparency);
    public static void SetXPOverlayTransparency(float v)        => SetFloat(nameof(XPOverlayTransparency), v);
    public static void SetFamiliarOverlayTransparency(float v)  => SetFloat(nameof(FamiliarOverlayTransparency), v);
    public static void SetFamiliarBrowserTransparency(float v)  => SetFloat(nameof(FamiliarBrowserTransparency), v);
    public static void SetFamiliarQuickSpawnOverlayTransparency(float v) => SetFloat(nameof(FamiliarQuickSpawnOverlayTransparency), v);
    public static void SetFaustBossTrackerOverlayTransparency(float v) => SetFloat(nameof(FaustBossTrackerOverlayTransparency), v);
    public static void SetShiftSpellOverlayTransparency(float v) => SetFloat(nameof(ShiftSpellOverlayTransparency), v);
    public static void SetQuickActionsOverlayTransparency(float v) => SetFloat(nameof(QuickActionsOverlayTransparency), v);
    public static void SetBeelzActionBarOverlayTransparency(float v) => SetFloat(nameof(BeelzActionBarOverlayTransparency), v);
    public static void SetBeelzSummonsOverlayTransparency(float v) => SetFloat(nameof(BeelzSummonsOverlayTransparency), v);
    public static void SetBeelzTransformOverlayTransparency(float v) => SetFloat(nameof(BeelzTransformOverlayTransparency), v);
    public static void SetUrielSharedOverlayTransparency(float v) => SetFloat(nameof(UrielSharedOverlayTransparency), v);
    public static void SetUrielObjectSpawnerOverlayTransparency(float v) => SetFloat(nameof(UrielObjectSpawnerOverlayTransparency), v);
    public static void SetChatWindowOverlayTransparency(float v) => SetFloat(nameof(ChatWindowOverlayTransparency), v);
    public static void SetDailyQuestTransparency(float v)       => SetFloat(nameof(DailyQuestTransparency), v);
    public static void SetProfessionOverlayTransparency(float v) => SetFloat(nameof(ProfessionOverlayTransparency), v);

    /// <summary>Convert a user-facing "transparency" value (0=opaque, 1=invisible)
    /// to an alpha multiplier suitable for Image.color, applying the legibility
    /// floor so panel chrome remains visible.</summary>
    public static float TransparencyToAlpha(float userTransparency)
    {
        var clamped = UnityEngine.Mathf.Clamp(userTransparency, 0f, OVERLAY_TRANSPARENCY_FLOOR);
        return 1f - clamped;
    }

    /// <summary>Visibility-suppress flag for the master overlay toggle on the
    /// floating-button strip. Session-only — not persisted — so the user can't
    /// suppress everything and forget how to bring it back. Reset on game
    /// restart. Per friend-testing direction: this must NEVER make
    /// hidden-by-config overlays visible; it only re-hides / re-shows the
    /// overlays that were already visible per their per-overlay config flags.</summary>
    public static bool OverlaysSuppressedByUser { get; set; }

    private static float GetFloat(string key, float fallback)
    {
        return (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<float> f)
            ? f.Value : fallback;
    }
    private static void SetFloat(string key, float value)
    {
        if (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<float> f)
            f.Value = value;
    }
    public static bool UseHorizontalContentLayout =>
        (ConfigEntries[nameof(UseHorizontalContentLayout)] as ConfigEntry<bool>)?.Value ?? true;
    public static bool ClearServerMessages =>
        (ConfigEntries[nameof(ClearServerMessages)] as ConfigEntry<bool>)?.Value ?? true;   // 0.67.2: fallback matches the registered default
    public static void SetClearServerMessages(bool v) => SetBool(nameof(ClearServerMessages), v);

    // 0.9.2 (introduced): when on, the XP overlay + Prestige info display
    // rendered a horizontal progress bar alongside the % text.
    //
    // 0.14.0 friend-test v2 (deprecated for overlays): per-system bar flags
    // were introduced — Settings.ShowProgressBarXP / *Familiar / *Expertise /
    // *Legacy / *Professions. The standalone overlays + combined overlay all
    // moved to those. This legacy ShowProgressBars setting is now only read
    // by the Prestige info display in the Prestige tab (MainPanel.RenderPrestigeInfo).
    // Left intact rather than renamed to avoid migrating existing users'
    // saved .cfg values; a rename would silently flip the Prestige info bar
    // on/off for upgraders. If a future version removes the Prestige info
    // bar, this setting can be deleted entirely.
    public static bool ShowProgressBars =>
        (ConfigEntries[nameof(ShowProgressBars)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetShowProgressBars(bool v) => SetBool(nameof(ShowProgressBars), v);

    // 0.9.6: when on, the XP overlay shows the chosen bonus-stat names AND
    // their current numeric values for the equipped weapon expertise and the
    // current blood legacy beneath each row (e.g. "+12% PhysicalPower"). The
    // values come from .wep get / .bl get replies which are auto-fetched on
    // overlay show + every OverlayBonusStatsRefreshSeconds while visible.
    // Default off so users who want a minimal overlay keep the compact view.
    public static bool ShowOverlayBonusStats =>
        (ConfigEntries[nameof(ShowOverlayBonusStats)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetShowOverlayBonusStats(bool v) => SetBool(nameof(ShowOverlayBonusStats), v);

    // B5 (0.19): abbreviate the bonus-stat names on the overlays (Eclipse-style — PhysicalPower →
    // PhysPwr, etc.) so each stat stays on one line and the wrapped sub-row can't overlap the bar
    // above / the row below at Large / X-Large overlay text. Off by default (full names).
    public static bool ShowOverlayStatAcronyms =>
        (ConfigEntries[nameof(ShowOverlayStatAcronyms)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetShowOverlayStatAcronyms(bool v) => SetBool(nameof(ShowOverlayStatAcronyms), v);

    // 0.10.7: optional numerical XP-progress row under Weapon and Legacy on
    // the XP overlay. Renders "Exp: 123 / 4500 (2.7%)" so the user can see
    // exactly how much expertise / essence is needed for the next level.
    // Values come from parsing the .wep get / .bl get reply preamble (chat
    // reply contains the raw numbers; the Eclipse stream only has the
    // percentage). Off by default — the existing "Lv X (P%)" title row is
    // sufficient for most users.
    public static bool ShowOverlayXpCounter =>
        (ConfigEntries[nameof(ShowOverlayXpCounter)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetShowOverlayXpCounter(bool v) => SetBool(nameof(ShowOverlayXpCounter), v);

    // 0.10.7: progress-bar height settings.
    // Absolute = explicit pixel height (clamped 4..24, default 8).
    // Relative = bar height scales with the overlay's content area (current pre-0.10.7
    // behavior). User feedback was that bars grew too aggressively when the overlay was
    // enlarged for additional info rows; absolute is the new default.
    public const int PROGRESS_BAR_HEIGHT_MIN = 4;
    public const int PROGRESS_BAR_HEIGHT_MAX = 24;
    public static int ProgressBarHeight =>
        UnityEngine.Mathf.Clamp(
            (ConfigEntries[nameof(ProgressBarHeight)] as ConfigEntry<int>)?.Value ?? 8,
            PROGRESS_BAR_HEIGHT_MIN, PROGRESS_BAR_HEIGHT_MAX);
    public static void SetProgressBarHeight(int v) => SetInt(nameof(ProgressBarHeight),
        UnityEngine.Mathf.Clamp(v, PROGRESS_BAR_HEIGHT_MIN, PROGRESS_BAR_HEIGHT_MAX));
    public static bool ProgressBarHeightRelative =>
        (ConfigEntries[nameof(ProgressBarHeightRelative)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetProgressBarHeightRelative(bool v) => SetBool(nameof(ProgressBarHeightRelative), v);

    // 0.10.10: auto-scan-on-open toggle. Pre-0.10.10 the V-Bloods tab
    // unconditionally fired VBloodScannerService.StartScan() the first
    // time the user opened it after a session start. The new box-sweep
    // scanner (0.10.9) is much faster than the old .fam s scan, but it
    // STILL switches the active box ~10-15 times and (until the silent
    // suppression flag added in 0.10.10) leaked the `.fam cb`/`.fam l`
    // confirmations into chat. Friend-testing surfaced this as the
    // dominant new-version annoyance — auto-scan now defaults to OFF,
    // and the V-Bloods tab waits for the user to click "Scan all".
    public static bool AutoScanVBloodsOnTabOpen =>
        (ConfigEntries[nameof(AutoScanVBloodsOnTabOpen)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetAutoScanVBloodsOnTabOpen(bool v) => SetBool(nameof(AutoScanVBloodsOnTabOpen), v);

    // 0.12.0: configurable background color for the main panel and the
    // Familiar Browser overlay. Stored as a hex string so power users can
    // pick any color via .cfg, while the Settings UI offers a preset row.
    // Default "#121212" matches the historical Theme.DarkBackground RGB
    // (~0.07, 0.07, 0.07) so users who never touch the setting see no
    // change. ALPHA IS NOT STORED HERE — per-panel transparency continues
    // to flow through UITransparency / FamiliarBrowserTransparency so
    // existing alpha controls keep working.
    public const string DEFAULT_PANEL_BG_HEX = "#121212";
    public static string PanelBackgroundColorHex =>
        (ConfigEntries.TryGetValue(nameof(PanelBackgroundColorHex), out var entry) && entry is ConfigEntry<string> s)
            ? s.Value : DEFAULT_PANEL_BG_HEX;
    public static void SetPanelBackgroundColorHex(string hex)
    {
        if (ConfigEntries.TryGetValue(nameof(PanelBackgroundColorHex), out var entry) && entry is ConfigEntry<string> s)
            s.Value = hex;
    }

    /// <summary>0.12.0: parsed RGB form of PanelBackgroundColorHex. Alpha=1
    /// by design — the per-panel transparency setting decides the final
    /// alpha at apply time. Falls back to the documented default if the
    /// user's .cfg contains garbage so a bad edit can't crash the UI.</summary>
    public static UnityEngine.Color PanelBackgroundColor
    {
        get
        {
            if (UnityEngine.ColorUtility.TryParseHtmlString(PanelBackgroundColorHex, out var c))
                return new UnityEngine.Color(c.r, c.g, c.b, 1f);
            if (UnityEngine.ColorUtility.TryParseHtmlString(DEFAULT_PANEL_BG_HEX, out var fb))
                return new UnityEngine.Color(fb.r, fb.g, fb.b, 1f);
            return new UnityEngine.Color(0.07f, 0.07f, 0.07f, 1f);
        }
    }

    // 0.12.0: interior background color (companion to PanelBackgroundColorHex).
    // Targets the scroll-view wrapper Images and viewports inside the main
    // panel and the Familiar Browser overlay — these were red by framework
    // default (UIFactory.CreateScrollView used Theme.Level1 = bright red for
    // the wrapper, plus Theme.ViewportBackground dark grey for the viewport).
    // Friend-test feedback on v0.12.0 pre-release: "the outer panel recolors
    // but there's still a red strip inside." That's what this setting
    // controls. Independent of the outer color so users can build a two-tone
    // theme (e.g. wine outer + black interior).
    public const string DEFAULT_INNER_BG_HEX = "#121212";
    public static string InnerPanelBackgroundColorHex =>
        (ConfigEntries.TryGetValue(nameof(InnerPanelBackgroundColorHex), out var entry) && entry is ConfigEntry<string> s)
            ? s.Value : DEFAULT_INNER_BG_HEX;
    public static void SetInnerPanelBackgroundColorHex(string hex)
    {
        if (ConfigEntries.TryGetValue(nameof(InnerPanelBackgroundColorHex), out var entry) && entry is ConfigEntry<string> s)
            s.Value = hex;
    }
    public static UnityEngine.Color InnerPanelBackgroundColor
    {
        get
        {
            if (UnityEngine.ColorUtility.TryParseHtmlString(InnerPanelBackgroundColorHex, out var c))
                return new UnityEngine.Color(c.r, c.g, c.b, 1f);
            if (UnityEngine.ColorUtility.TryParseHtmlString(DEFAULT_INNER_BG_HEX, out var fb))
                return new UnityEngine.Color(fb.r, fb.g, fb.b, 1f);
            return new UnityEngine.Color(0.07f, 0.07f, 0.07f, 1f);
        }
    }

    // 0.18.4: button background color — the Raphael/OV launcher buttons, "Stash All", the Familiar
    // Browser buttons, and every other THEMED button Raphael builds (anything created without an explicit
    // color). Buttons with a deliberate color — the red Danger/WIPE buttons etc. — are NOT affected.
    // Default "#4D4D4D" matches the historical Theme.SliderFill RGB (~0.3) so users who never touch
    // it see no change. RGB only — the button's alpha keeps coming from Theme.SliderFill.
    public const string DEFAULT_BUTTON_BG_HEX = "#4D4D4D";
    public static string ButtonBackgroundColorHex =>
        (ConfigEntries.TryGetValue(nameof(ButtonBackgroundColorHex), out var entry) && entry is ConfigEntry<string> s)
            ? s.Value : DEFAULT_BUTTON_BG_HEX;
    public static void SetButtonBackgroundColorHex(string hex)
    {
        if (ConfigEntries.TryGetValue(nameof(ButtonBackgroundColorHex), out var entry) && entry is ConfigEntry<string> s)
            s.Value = hex;
    }
    public static UnityEngine.Color ButtonBackgroundColor
    {
        get
        {
            if (UnityEngine.ColorUtility.TryParseHtmlString(ButtonBackgroundColorHex, out var c))
                return new UnityEngine.Color(c.r, c.g, c.b, 1f);
            if (UnityEngine.ColorUtility.TryParseHtmlString(DEFAULT_BUTTON_BG_HEX, out var fb))
                return new UnityEngine.Color(fb.r, fb.g, fb.b, 1f);
            return new UnityEngine.Color(0.3f, 0.3f, 0.3f, 1f);
        }
    }

    // 0.18.4: scale for the always-on Raphael / OV launcher buttons (top-right corner). Some displays
    // render the 40px buttons large; this lets users shrink (or grow) them. Applied as the floating
    // panel's localScale. Clamped 0.5–1.5; default 1.0 (unchanged).
    public static float FloatingButtonScale =>
        UnityEngine.Mathf.Clamp((ConfigEntries.TryGetValue(nameof(FloatingButtonScale), out var e) && e is ConfigEntry<float> f) ? f.Value : 1.0f, 0.5f, 1.5f);
    public static void SetFloatingButtonScale(float v)
    {
        if (ConfigEntries.TryGetValue(nameof(FloatingButtonScale), out var entry) && entry is ConfigEntry<float> s)
            s.Value = UnityEngine.Mathf.Clamp(v, 0.5f, 1.5f);
    }

    // 0.18.4: toggle for the Beelz action-bar ability-ICON resolution (buttons fall back to text labels
    // when off). DEFAULT ON. (It was briefly defaulted OFF while investigating a server-switch crash, but
    // that crash was reproduced WITH icons off — it's the server-switch UI handoff, not this — so icons
    // are back on. Resolution is bounded: one tile/tick, capped retries, off the panel build frame.)
    public static bool EnableBeelzAbilityIcons =>
        (ConfigEntries.TryGetValue(nameof(EnableBeelzAbilityIcons), out var e) && e is ConfigEntry<bool> b) ? b.Value : true;
    public static void SetEnableBeelzAbilityIcons(bool v) => SetBool(nameof(EnableBeelzAbilityIcons), v);

    // 0.10.14: overlay-lock toggle. When on, every overlay panel's
    // IsPinned flag is set true, which short-circuits PanelDragger's
    // per-frame Update — no user drag, no user resize, no resize-hover
    // cursor. Programmatic resize via Rect.sizeDelta (used by the
    // auto-resize-on-data path and by the Display Settings nudge
    // buttons) is unaffected: IsPinned only blocks the dragger, not
    // direct rect mutations. Friend-test: "lock overlays in place so
    // I don't accidentally drag them while playing."
    public static bool LockOverlays =>
        (ConfigEntries[nameof(LockOverlays)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetLockOverlays(bool v) => SetBool(nameof(LockOverlays), v);
    // 0.18.3: when ON, the upper-right "hide all overlays" toggle ALSO hides the chat window. Default
    // OFF — chat stays visible while other overlays hide (the current, preferred behavior).
    public static bool HideChatWithOverlaysToggle =>
        (ConfigEntries[nameof(HideChatWithOverlaysToggle)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetHideChatWithOverlaysToggle(bool v) => SetBool(nameof(HideChatWithOverlaysToggle), v);

    // 0.28: Overlay Visibility — the master hide (OV button / hide-all hotkey) gains three options.
    //
    // OverlayTimedHide — when ON, a hide acts as a TIMED hide: it auto-restores after
    // OverlayHideDurationSeconds instead of staying hidden until the user toggles back. When OFF
    // (default), the master hide is a sticky toggle (press to hide, press to show) — the historical
    // behavior. The countdown is driven per-frame by CoreUpdateBehavior; un-hiding manually (button /
    // hotkey) cancels any pending restore.
    public static bool OverlayTimedHide =>
        (ConfigEntries[nameof(OverlayTimedHide)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetOverlayTimedHide(bool v) => SetBool(nameof(OverlayTimedHide), v);

    // Duration of a timed hide, in seconds. Clamped 5..600 (up to 10 minutes — long enough for a
    // timed video capture); default 25.
    public const int OVERLAY_HIDE_DURATION_MIN = 5;
    public const int OVERLAY_HIDE_DURATION_MAX = 600;
    public static int OverlayHideDurationSeconds =>
        UnityEngine.Mathf.Clamp(
            (ConfigEntries[nameof(OverlayHideDurationSeconds)] as ConfigEntry<int>)?.Value ?? 25,
            OVERLAY_HIDE_DURATION_MIN, OVERLAY_HIDE_DURATION_MAX);
    public static void SetOverlayHideDurationSeconds(int v) => SetInt(nameof(OverlayHideDurationSeconds),
        UnityEngine.Mathf.Clamp(v, OVERLAY_HIDE_DURATION_MIN, OVERLAY_HIDE_DURATION_MAX));

    // HideLauncherButtonsWithOverlays — when ON, the master hide ALSO hides the always-on Raphael / OV
    // launcher cluster (top-right). Default OFF so the buttons stay reachable. SAFETY: hiding the
    // buttons is only honored when there's a guaranteed way back — i.e. timed-hide is on OR a
    // hide-all hotkey is bound. CanHideLauncherButtons gates the actual effect; the Settings UI warns
    // when neither escape route exists.
    public static bool HideLauncherButtonsWithOverlays =>
        (ConfigEntries[nameof(HideLauncherButtonsWithOverlays)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetHideLauncherButtonsWithOverlays(bool v) => SetBool(nameof(HideLauncherButtonsWithOverlays), v);

    /// <summary>0.28: true only when hiding the launcher buttons is SAFE — the user has opted in AND
    /// there is a non-button way to bring the UI back (timed auto-restore, or a bound hide-all hotkey).
    /// Drives RefreshFloatingButtonVisibility so a misconfiguration can never strand the user with no
    /// way to reopen the panel.</summary>
    public static bool CanHideLauncherButtons =>
        HideLauncherButtonsWithOverlays && (OverlayTimedHide || !HotkeyToggleAllOverlays.IsEmpty);

    // KeepNativeChatHiddenWhileOverlaysHidden — fixes the "vanilla chat reappears" quirk. When the
    // master hide drops the Raphael chat overlay (only relevant if HideChatWithOverlaysToggle is on), the
    // game's native chat would normally pop back because Raphael only suppresses it while the Raphael chat
    // overlay is the active replacement. With this ON (default), the native chat is kept hidden too for
    // a clean screen; turn OFF if you'd rather have the game chat available while Raphael overlays are hidden.
    public static bool KeepNativeChatHiddenWhileOverlaysHidden =>
        (ConfigEntries[nameof(KeepNativeChatHiddenWhileOverlaysHidden)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetKeepNativeChatHiddenWhileOverlaysHidden(bool v) => SetBool(nameof(KeepNativeChatHiddenWhileOverlaysHidden), v);

    // 0.10.8: per-overlay edge padding. Pre-0.10.8 text in the overlays sat
    // flush with the panel border, which read as cramped — especially
    // visible on the Familiar Browser overlay where long V-Blood names
    // butted up against the scrollbar gutter on the right. This setting
    // controls the LEFT / RIGHT inner padding applied to every overlay's
    // content area via ResizeablePanelBase.ApplyOverlayEdgePadding. The
    // value is applied at construct time; rebuilding the overlay (toggle
    // off + back on, or change overlay text scale) picks up the new
    // padding live.
    public const int OVERLAY_EDGE_PADDING_MIN = 0;
    public const int OVERLAY_EDGE_PADDING_MAX = 32;
    public static int OverlayEdgePadding =>
        UnityEngine.Mathf.Clamp(
            (ConfigEntries[nameof(OverlayEdgePadding)] as ConfigEntry<int>)?.Value ?? 6,
            OVERLAY_EDGE_PADDING_MIN, OVERLAY_EDGE_PADDING_MAX);
    public static void SetOverlayEdgePadding(int v) => SetInt(nameof(OverlayEdgePadding),
        UnityEngine.Mathf.Clamp(v, OVERLAY_EDGE_PADDING_MIN, OVERLAY_EDGE_PADDING_MAX));

    // 0.10.7: prestige-progress sub-line inside the main XP/expertise/legacy
    // bars. Eclipse renders a thin secondary fill ABOVE/BELOW the main bar
    // showing how far you are toward the next prestige tier; users asked for
    // the same. Implementation draws a slim 25%-height inset bar inside the
    // existing MiniBar. Off by default.
    public static bool ShowPrestigeSubLine =>
        (ConfigEntries[nameof(ShowPrestigeSubLine)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetShowPrestigeSubLine(bool v) => SetBool(nameof(ShowPrestigeSubLine), v);

    // 0.10.7: V-Bloods tab view mode. "Chips" (default) — one row per
    // V-Blood NAME with the B/S/P/Ps capture chips (matches 0.10.0..0.10.6
    // behavior). "Instances" — one row per CAPTURED FAMILIAR (level / box
    // / shiny / primal / summon button), enumerated from PlayerStateService
    // .BoxContents. The instance view requires the user to have navigated
    // each box that contains V-Bloods at least once (so its contents are
    // cached); future "Deep Scan" feature will automate the sweep.
    public static bool VBloodPerInstanceView =>
        (ConfigEntries[nameof(VBloodPerInstanceView)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetVBloodPerInstanceView(bool v) => SetBool(nameof(VBloodPerInstanceView), v);

    // 0.10.6: Chat Logging — three per-category visibility toggles + master
    // helpers for the diagnostic Settings section. Suppression only applies
    // to commands whose replies Raphael already shows in its own UI surfaces
    // (BoxList, BloodInfo, V-Blood scanner, etc.). Commands whose replies
    // would otherwise be invisible (admin actions, KindredCommands without
    // structured parsing) stay visible regardless of these toggles — see
    // CommandClassification.HasBchUIDisplay.
    //
    // Defaults:
    //   ShowChatBchAuto    = false (silent; Raphael-fired auto traffic stays hidden)
    //   ShowChatBloodcraft = true  (user sees their Bloodcraft command replies)
    //   ShowChatKindred    = true  (user sees their Kindred command replies)
    //
    // These DO NOT touch ClearServerMessages (the global admin clear) — that
    // remains a separate setting with its own meaning.
    public static bool ShowChatBchAuto =>
        (ConfigEntries[nameof(ShowChatBchAuto)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowChatBloodcraft =>
        (ConfigEntries[nameof(ShowChatBloodcraft)] as ConfigEntry<bool>)?.Value ?? true;
    public static bool ShowChatKindred =>
        (ConfigEntries[nameof(ShowChatKindred)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetShowChatBchAuto(bool v)    => SetBool(nameof(ShowChatBchAuto), v);
    public static void SetShowChatBloodcraft(bool v) => SetBool(nameof(ShowChatBloodcraft), v);
    public static void SetShowChatKindred(bool v)    => SetBool(nameof(ShowChatKindred), v);

    /// <summary>Master "show all" — flip every Chat Logging category on.</summary>
    public static void ShowAllChat()
    {
        SetShowChatBchAuto(true);
        SetShowChatBloodcraft(true);
        SetShowChatKindred(true);
    }

    /// <summary>Master "hide all" — flip every Chat Logging category off. Does
    /// NOT touch ClearServerMessages (that's a separate admin-level setting).</summary>
    public static void HideAllChat()
    {
        SetShowChatBchAuto(false);
        SetShowChatBloodcraft(false);
        SetShowChatKindred(false);
    }

    // 0.10.1: sort order applied to the Familiar Browser overlay's familiar
    // list AND the V-Bloods tab grid. Persisted as an int (the enum value)
    // so adding new modes later doesn't break the saved config.
    //   Default      — server-provided order (familiars: as listed by .fam l;
    //                  V-Bloods: alphabetical from VBloodRegistry)
    //   Alphabetical — by name, ascending
    //   Level        — by familiar level, DESCENDING (high to low)
    //   Location     — by map region. RESERVED; not selectable in 0.10.1
    //                  because we lack a canonical location table; planned
    //                  for 0.10.2 with user-supplied data.
    public enum FamiliarSortOrder { Default = 0, Alphabetical = 1, Level = 2, Location = 3 }

    // 0.10.2: text alignment for overlay rows. Default Left preserves the
    // pre-0.10.2 visual; Right is for users who pin the overlay to the
    // right edge of the screen and want the values closer to the panel
    // border so the eye doesn't have to track left.
    public enum OverlayAlignment { Left = 0, Right = 1 }

    public static OverlayAlignment OverlayTextAlignmentSetting
    {
        get
        {
            var raw = (ConfigEntries[nameof(OverlayTextAlignmentSetting)] as ConfigEntry<int>)?.Value ?? 0;
            if (raw < 0 || raw > 1) raw = 0;
            return (OverlayAlignment)raw;
        }
    }
    public static void SetOverlayTextAlignment(OverlayAlignment v)
    {
        if (ConfigEntries.TryGetValue(nameof(OverlayTextAlignmentSetting), out var entry) && entry is ConfigEntry<int> i)
            i.Value = (int)v;
    }

    public static FamiliarSortOrder FamiliarSortOrderSetting
    {
        get
        {
            var raw = (ConfigEntries[nameof(FamiliarSortOrderSetting)] as ConfigEntry<int>)?.Value ?? 0;
            // Defensive clamp so a corrupted cfg can't crash the dropdown logic.
            if (raw < 0 || raw > 3) raw = 0;
            return (FamiliarSortOrder)raw;
        }
    }
    public static void SetFamiliarSortOrder(FamiliarSortOrder v)
    {
        if (ConfigEntries.TryGetValue(nameof(FamiliarSortOrderSetting), out var entry) && entry is ConfigEntry<int> i)
            i.Value = (int)v;
    }

    // 0.13.0: per-profession visibility toggles for the Professions overlay.
    // Friend-test feedback: many players only level 2–3 of the 8 professions
    // (typically the gathering ones) and want the overlay to hide the rest.
    // Default true preserves the v0.12.x render. Settings → Display has the
    // checkboxes; ProfessionOverlayPanel.Render gates each row + bar.
    //
    // The same flags will gate component visibility inside the planned v0.14.0
    // combined overlay (one section per profession driven by these toggles),
    // so naming + defaults are forward-compatible.
    public static bool ShowProfessionEnchanting    => (ConfigEntries.TryGetValue(nameof(ShowProfessionEnchanting),    out var e1) && e1 is ConfigEntry<bool> b1) ? b1.Value : true;
    public static bool ShowProfessionAlchemy       => (ConfigEntries.TryGetValue(nameof(ShowProfessionAlchemy),       out var e2) && e2 is ConfigEntry<bool> b2) ? b2.Value : true;
    public static bool ShowProfessionHarvesting    => (ConfigEntries.TryGetValue(nameof(ShowProfessionHarvesting),    out var e3) && e3 is ConfigEntry<bool> b3) ? b3.Value : true;
    public static bool ShowProfessionBlacksmithing => (ConfigEntries.TryGetValue(nameof(ShowProfessionBlacksmithing), out var e4) && e4 is ConfigEntry<bool> b4) ? b4.Value : true;
    public static bool ShowProfessionTailoring     => (ConfigEntries.TryGetValue(nameof(ShowProfessionTailoring),     out var e5) && e5 is ConfigEntry<bool> b5) ? b5.Value : true;
    public static bool ShowProfessionWoodcutting   => (ConfigEntries.TryGetValue(nameof(ShowProfessionWoodcutting),   out var e6) && e6 is ConfigEntry<bool> b6) ? b6.Value : true;
    public static bool ShowProfessionMining        => (ConfigEntries.TryGetValue(nameof(ShowProfessionMining),        out var e7) && e7 is ConfigEntry<bool> b7) ? b7.Value : true;
    public static bool ShowProfessionFishing       => (ConfigEntries.TryGetValue(nameof(ShowProfessionFishing),       out var e8) && e8 is ConfigEntry<bool> b8) ? b8.Value : true;
    public static void SetShowProfessionEnchanting(bool v)    => SetBool(nameof(ShowProfessionEnchanting),    v);
    public static void SetShowProfessionAlchemy(bool v)       => SetBool(nameof(ShowProfessionAlchemy),       v);
    public static void SetShowProfessionHarvesting(bool v)    => SetBool(nameof(ShowProfessionHarvesting),    v);
    public static void SetShowProfessionBlacksmithing(bool v) => SetBool(nameof(ShowProfessionBlacksmithing), v);
    public static void SetShowProfessionTailoring(bool v)     => SetBool(nameof(ShowProfessionTailoring),     v);
    public static void SetShowProfessionWoodcutting(bool v)   => SetBool(nameof(ShowProfessionWoodcutting),   v);
    public static void SetShowProfessionMining(bool v)        => SetBool(nameof(ShowProfessionMining),        v);
    public static void SetShowProfessionFishing(bool v)       => SetBool(nameof(ShowProfessionFishing),       v);

    // 0.9.1: when on, the chat copy of action-confirmation messages
    // (.fam b / .fam ub / .fam t / .fam cb / .fam mb / .fam sb / .fam r)
    // is suppressed. Friend-testing feedback: switching boxes and bouncing
    // between familiars produces a wall of confirmation chat that's noisy
    // for users who already see the live state in the UI. The UI continues
    // to work because the data feeds (.fam boxes / .fam l intercepts + the
    // Eclipse stream) aren't affected — only the human-readable confirmation
    // lines are eaten.
    public static bool SuppressFamiliarActionChatter =>
        (ConfigEntries[nameof(SuppressFamiliarActionChatter)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetSuppressFamiliarActionChatter(bool v) => SetBool(nameof(SuppressFamiliarActionChatter), v);

    // 0.18: suppress VampireCommandFramework (VCF) system replies — the "[error]" / "[denied]" /
    // "parameter conversion errors" lines a command framework prints when a command Raphael sends (or a
    // mis-clicked admin button) isn't usable on this server. Raphael is primarily a Bloodcraft client but
    // can load on Beelzebub-only / Kindred-only / vanilla servers; those VCF replies are never Raphael's
    // own structured data, so they're noise. Default ON so the chat stays clean out of the box; turn
    // OFF if you want to see why a command you typed yourself failed.
    public static bool SuppressCommandFrameworkErrors =>
        (ConfigEntries[nameof(SuppressCommandFrameworkErrors)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetSuppressCommandFrameworkErrors(bool v) => SetBool(nameof(SuppressCommandFrameworkErrors), v);
    public static int GlobalQueryIntervalInSeconds { get; } = 2;
    public static int FamStatsQueryIntervalInSeconds
    {
        get
        {
            var value = (ConfigEntries[nameof(FamStatsQueryIntervalInSeconds)] as ConfigEntry<int>)?.Value ?? 10;
            if (value < 5) value = 5;
            return value;
        }
    }

    // ---- Familiar UI flags ----
    public static bool IsFamStatsPanelEnabled  => (ConfigEntries[nameof(IsFamStatsPanelEnabled)]  as ConfigEntry<bool>)?.Value ?? true;
    public static bool IsBoxPanelEnabled       => (ConfigEntries[nameof(IsBoxPanelEnabled)]       as ConfigEntry<bool>)?.Value ?? true;
    public static bool IsBindButtonEnabled     => (ConfigEntries[nameof(IsBindButtonEnabled)]     as ConfigEntry<bool>)?.Value ?? true;
    public static bool IsCombatButtonEnabled   => (ConfigEntries[nameof(IsCombatButtonEnabled)]   as ConfigEntry<bool>)?.Value ?? true;
    public static bool IsPrestigeButtonEnabled => (ConfigEntries[nameof(IsPrestigeButtonEnabled)] as ConfigEntry<bool>)?.Value ?? true;
    public static bool IsToggleButtonEnabled   => (ConfigEntries[nameof(IsToggleButtonEnabled)]   as ConfigEntry<bool>)?.Value ?? true;
    public static bool AutoEnableFamiliarEquipment =>
        (ConfigEntries[nameof(AutoEnableFamiliarEquipment)] as ConfigEntry<bool>)?.Value ?? true;

    public static string LastBindCommand
    {
        get => (ConfigEntries[nameof(LastBindCommand)] as ConfigEntry<string>)?.Value ?? "";
        set => ConfigEntries[nameof(LastBindCommand)].BoxedValue = value;
    }

    // ---- Secondary overlays (Raphael addition) ----
    // These are read on UIOnInitialize to restore each overlay's last visibility
    // state across sessions. BCHubUIManager.ToggleOverlay writes the new value
    // here so a flip persists. (Pre-0.6.0 these settings existed but were never
    // wired into the toggle path, so the overlays always defaulted to off.)
    // 0.14.0: combined overlay — single panel containing all info-overlay
    // sections (XP / Familiar / Weapon Expertise / Blood Legacy / Professions /
    // Daily Quest). Mutually exclusive with the individual overlays — when
    // ShowCombinedOverlay is true, the standalone info overlays are hidden
    // regardless of their own ShowXxxOverlay flag (the flags persist so
    // toggling combined off restores the previous individual state).
    public static bool ShowCombinedOverlay         => (ConfigEntries.TryGetValue(nameof(ShowCombinedOverlay),         out var ec) && ec is ConfigEntry<bool> bc) ? bc.Value : false;
    public static bool CombinedOverlayShowXP       => (ConfigEntries.TryGetValue(nameof(CombinedOverlayShowXP),       out var e1) && e1 is ConfigEntry<bool> b1) ? b1.Value : true;
    public static bool CombinedOverlayShowFamiliar => (ConfigEntries.TryGetValue(nameof(CombinedOverlayShowFamiliar), out var e2) && e2 is ConfigEntry<bool> b2) ? b2.Value : true;
    public static bool CombinedOverlayShowExpertise=> (ConfigEntries.TryGetValue(nameof(CombinedOverlayShowExpertise),out var e3) && e3 is ConfigEntry<bool> b3) ? b3.Value : true;
    public static bool CombinedOverlayShowLegacy   => (ConfigEntries.TryGetValue(nameof(CombinedOverlayShowLegacy),   out var e4) && e4 is ConfigEntry<bool> b4) ? b4.Value : true;
    public static bool CombinedOverlayShowProfessions => (ConfigEntries.TryGetValue(nameof(CombinedOverlayShowProfessions), out var e5) && e5 is ConfigEntry<bool> b5) ? b5.Value : true;
    public static bool CombinedOverlayShowQuests   => (ConfigEntries.TryGetValue(nameof(CombinedOverlayShowQuests),   out var e6) && e6 is ConfigEntry<bool> b6) ? b6.Value : true;
    // 0.14.0 friend-test v2: UNIFIED per-system progress-bar flags. These
    // apply to BOTH the standalone overlays AND the combined overlay so
    // toggling "show XP bar" controls visibility consistently regardless
    // of which overlay mode the user is in. Defaults true preserve the
    // existing bars-on look most users have. The earlier global
    // `ShowProgressBars` setting is kept solely for the Prestige Info
    // display in the Prestige tab — every other call site moved to these
    // per-system flags.
    public static bool ShowProgressBarXP          => (ConfigEntries.TryGetValue(nameof(ShowProgressBarXP),          out var pb1) && pb1 is ConfigEntry<bool> sb1) ? sb1.Value : true;
    public static bool ShowProgressBarFamiliar    => (ConfigEntries.TryGetValue(nameof(ShowProgressBarFamiliar),    out var pb2) && pb2 is ConfigEntry<bool> sb2) ? sb2.Value : true;
    public static bool ShowProgressBarExpertise   => (ConfigEntries.TryGetValue(nameof(ShowProgressBarExpertise),   out var pb3) && pb3 is ConfigEntry<bool> sb3) ? sb3.Value : true;
    public static bool ShowProgressBarLegacy      => (ConfigEntries.TryGetValue(nameof(ShowProgressBarLegacy),      out var pb4) && pb4 is ConfigEntry<bool> sb4) ? sb4.Value : true;
    public static bool ShowProgressBarProfessions => (ConfigEntries.TryGetValue(nameof(ShowProgressBarProfessions), out var pb5) && pb5 is ConfigEntry<bool> sb5) ? sb5.Value : true;
    public static void SetShowProgressBarXP(bool v)          => SetBool(nameof(ShowProgressBarXP), v);
    public static void SetShowProgressBarFamiliar(bool v)    => SetBool(nameof(ShowProgressBarFamiliar), v);
    public static void SetShowProgressBarExpertise(bool v)   => SetBool(nameof(ShowProgressBarExpertise), v);
    public static void SetShowProgressBarLegacy(bool v)      => SetBool(nameof(ShowProgressBarLegacy), v);
    public static void SetShowProgressBarProfessions(bool v) => SetBool(nameof(ShowProgressBarProfessions), v);
    public static float CombinedOverlayTransparency => GetFloat(nameof(CombinedOverlayTransparency), UITransparency);
    public static void SetShowCombinedOverlay(bool v)          => SetBool(nameof(ShowCombinedOverlay), v);
    public static void SetCombinedOverlayShowXP(bool v)        => SetBool(nameof(CombinedOverlayShowXP), v);
    public static void SetCombinedOverlayShowFamiliar(bool v)  => SetBool(nameof(CombinedOverlayShowFamiliar), v);
    public static void SetCombinedOverlayShowExpertise(bool v) => SetBool(nameof(CombinedOverlayShowExpertise), v);
    public static void SetCombinedOverlayShowLegacy(bool v)    => SetBool(nameof(CombinedOverlayShowLegacy), v);
    public static void SetCombinedOverlayShowProfessions(bool v) => SetBool(nameof(CombinedOverlayShowProfessions), v);
    public static void SetCombinedOverlayShowQuests(bool v)    => SetBool(nameof(CombinedOverlayShowQuests), v);
    public static void SetCombinedOverlayTransparency(float v) => SetFloat(nameof(CombinedOverlayTransparency), v);

    public static bool ShowExperienceOverlay   => (ConfigEntries[nameof(ShowExperienceOverlay)]   as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowFamiliarOverlay     => (ConfigEntries[nameof(ShowFamiliarOverlay)]     as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowFamiliarBrowser     => (ConfigEntries[nameof(ShowFamiliarBrowser)]     as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowFamiliarQuickSpawnOverlay => (ConfigEntries[nameof(ShowFamiliarQuickSpawnOverlay)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowFaustBossTrackerOverlay => (ConfigEntries[nameof(ShowFaustBossTrackerOverlay)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool FaustBossTrackerAutoRefresh => (ConfigEntries[nameof(FaustBossTrackerAutoRefresh)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowDailyQuestOverlay   => (ConfigEntries[nameof(ShowDailyQuestOverlay)]   as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowProfessionOverlay   => (ConfigEntries[nameof(ShowProfessionOverlay)]   as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowShiftSpellOverlay   => (ConfigEntries[nameof(ShowShiftSpellOverlay)]   as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowQuickActionsOverlay => (ConfigEntries[nameof(ShowQuickActionsOverlay)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowBeelzActionBarOverlay => (ConfigEntries[nameof(ShowBeelzActionBarOverlay)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowBeelzSummonsOverlay => (ConfigEntries[nameof(ShowBeelzSummonsOverlay)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowBeelzTransformOverlay => (ConfigEntries[nameof(ShowBeelzTransformOverlay)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowUrielSharedOverlay => (ConfigEntries[nameof(ShowUrielSharedOverlay)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowUrielObjectSpawnerOverlay => (ConfigEntries[nameof(ShowUrielObjectSpawnerOverlay)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowChatWindowOverlay => (ConfigEntries[nameof(ShowChatWindowOverlay)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ChatShowTimestamps => (ConfigEntries[nameof(ChatShowTimestamps)] as ConfigEntry<bool>)?.Value ?? true;
    public static bool ChatShowChannelTags => (ConfigEntries[nameof(ChatShowChannelTags)] as ConfigEntry<bool>)?.Value ?? true;
    public static bool HideNativeChat => (ConfigEntries[nameof(HideNativeChat)] as ConfigEntry<bool>)?.Value ?? false;
    // 0.65.1: experimental stronger native-chat hide. Zeroes the ENTIRE chat window's alpha via a
    // root CanvasGroup every frame (not just the message-list ContentCanvasGroup), so the element
    // that still flickered through on some PCs is covered too. Alpha-only (never .interactable), so
    // it keeps the same freeze-safety as the normal hide. OFF by default; opt in if you still see
    // the native chat flicker behind Raphael's chat.
    public static bool AggressiveNativeChatHide => (ConfigEntries[nameof(AggressiveNativeChatHide)] as ConfigEntry<bool>)?.Value ?? false;
    // 0.17.0: on the All tab, send to Global (true) or Local (false) by default.
    public static bool ChatAllTabDefaultGlobal => (ConfigEntries[nameof(ChatAllTabDefaultGlobal)] as ConfigEntry<bool>)?.Value ?? false;
    // 0.17.0: chat-window text size, INDEPENDENT of OverlayTextScale. The chat
    // log used to be sized via Theme.ScaledOverlay (the shared overlay
    // multiplier), so enlarging chat text also enlarged every OTHER overlay
    // (friend-test report). This dedicated multiplier scales ONLY the tabbed
    // chat window. Clamped 0.5..3.0 so a bad .cfg edit can't produce illegible
    // or absurd sizes.
    public static float ChatTextScale =>
        UnityEngine.Mathf.Clamp(GetFloat(nameof(ChatTextScale), 1.0f), 0.5f, 3.0f);
    public static void SetChatTextScale(float v) =>
        SetFloat(nameof(ChatTextScale), UnityEngine.Mathf.Clamp(v, 0.5f, 3.0f));
    // 0.17.0: newest chat line at the BOTTOM (true, default — game-like) or TOP (false).
    public static bool ChatNewestAtBottom => (ConfigEntries[nameof(ChatNewestAtBottom)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatNewestAtBottom(bool v) => SetBool(nameof(ChatNewestAtBottom), v);
    // 0.17.0: auto-scroll the chat log to keep the newest message in view as lines arrive.
    public static bool ChatAutoScroll => (ConfigEntries[nameof(ChatAutoScroll)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatAutoScroll(bool v) => SetBool(nameof(ChatAutoScroll), v);

    // 0.65.3: auto-hide the standalone chat window when it's been idle, like the game's native chat.
    // OFF by default (opt-in). Which activity brings it back is configurable so it can't "blink" from
    // Raphael's own regular Bloodcraft pings (System channel): by default player chat + hovering the
    // window + opening chat wake it, System-channel lines do NOT. The fade is driven by a CanvasGroup
    // on the window root — it never flips the persisted ShowChatWindowOverlay visibility (it's a fade,
    // not a close).
    public static bool ChatAutoHideEnabled => (ConfigEntries[nameof(ChatAutoHideEnabled)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetChatAutoHideEnabled(bool v) => SetBool(nameof(ChatAutoHideEnabled), v);
    // Idle seconds before it fades out. Clamped to a sane range so the ticker can't be starved or spam.
    public static float ChatAutoHideSeconds =>
        UnityEngine.Mathf.Clamp(GetFloat(nameof(ChatAutoHideSeconds), 12f), 3f, 120f);
    public static void SetChatAutoHideSeconds(float v) =>
        SetFloat(nameof(ChatAutoHideSeconds), UnityEngine.Mathf.Clamp(v, 3f, 120f));
    // Wake toggles. Message (non-System player chat) default ON; System (BC pings / broadcasts) default
    // OFF — that's the anti-blink default. With both OFF, only hovering the window / opening chat wakes it.
    public static bool ChatAutoHideWakeOnMessage => (ConfigEntries[nameof(ChatAutoHideWakeOnMessage)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatAutoHideWakeOnMessage(bool v) => SetBool(nameof(ChatAutoHideWakeOnMessage), v);
    public static bool ChatAutoHideWakeOnSystem => (ConfigEntries[nameof(ChatAutoHideWakeOnSystem)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetChatAutoHideWakeOnSystem(bool v) => SetBool(nameof(ChatAutoHideWakeOnSystem), v);

    // Chat scrolling aids (0.50 r11). Clickable ▲/▼ arrows on the scrollbar (default ON). Keyboard scrolling is
    // OPT-IN so it can't steal gameplay keys: PageUp/PageDown (default ON — rarely bound) and Up/Down arrows
    // (default OFF — commonly used in gameplay) are separate toggles.
    public static bool ChatScrollArrowButtons => (ConfigEntries[nameof(ChatScrollArrowButtons)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatScrollArrowButtons(bool v) => SetBool(nameof(ChatScrollArrowButtons), v);
    public static bool ChatScrollPageKeys => (ConfigEntries[nameof(ChatScrollPageKeys)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatScrollPageKeys(bool v) => SetBool(nameof(ChatScrollPageKeys), v);
    public static bool ChatScrollArrowKeys => (ConfigEntries[nameof(ChatScrollArrowKeys)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetChatScrollArrowKeys(bool v) => SetBool(nameof(ChatScrollArrowKeys), v);
    // 0.17.0: channel label format — short acronym ([G]/[L]/[Sys]/[W], false/default)
    // or the spelled-out name ([Global]/[Local]/[System]/[Whisper], true).
    public static bool ChatChannelLabelsSpelledOut => (ConfigEntries[nameof(ChatChannelLabelsSpelledOut)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetChatChannelLabelsSpelledOut(bool v) => SetBool(nameof(ChatChannelLabelsSpelledOut), v);
    // 0.17.0: tint each channel tab's label in that channel's color (Global in the
    // Global color, Local in its blue, Clan green, etc.). The All tab stays neutral.
    public static bool ChatColorTabs => (ConfigEntries[nameof(ChatColorTabs)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatColorTabs(bool v) => SetBool(nameof(ChatColorTabs), v);
    // 0.17.3: render the chat log in aligned columns (time | channel+sender | message)
    // with wrapped message lines hanging-indented under the message column — cleaner
    // for the mixed All view. Off by default (free-flowing text, the original look).
    public static bool ChatTabularLayout => (ConfigEntries[nameof(ChatTabularLayout)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetChatTabularLayout(bool v) => SetBool(nameof(ChatTabularLayout), v);
    // 0.17.3: in tabular layout, put the channel label and the sender name in SEPARATE
    // columns (time | channel | sender | message) instead of sharing one (time |
    // channel+sender | message). Default TRUE (separate). Only affects tabular layout.
    public static bool ChatTabularSeparateChannelName => (ConfigEntries[nameof(ChatTabularSeparateChannelName)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatTabularSeparateChannelName(bool v) => SetBool(nameof(ChatTabularSeparateChannelName), v);
    // 0.17.3: tabular column SIZING. When TRUE (default) the time/channel/name columns
    // get FIXED widths and the message column absorbs ALL remaining width — so widening
    // the chat window grows the message column first (not every column proportionally),
    // and the name column auto-fits the longest visible name (minimal dead space). When
    // FALSE, columns are LOCKED at proportional %-of-width positions (the original
    // behavior — every column scales with the window). Only affects tabular layout.
    public static bool ChatTabularAutoFitColumns => (ConfigEntries[nameof(ChatTabularAutoFitColumns)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatTabularAutoFitColumns(bool v) => SetBool(nameof(ChatTabularAutoFitColumns), v);
    // 0.17.3: double-click a player's name in the chat log to start a whisper to them
    // (jumps to the All tab with that whisper as the selected compose target and focuses
    // the input). Default TRUE.
    public static bool ChatDoubleClickNameWhisper => (ConfigEntries[nameof(ChatDoubleClickNameWhisper)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatDoubleClickNameWhisper(bool v) => SetBool(nameof(ChatDoubleClickNameWhisper), v);
    // B3 (0.29.8): on whispers YOU send, show WHO you sent to (received whispers already show the sender, but
    // a sent line shows your own name, so the "All Whispers" view gave no hint of the recipient). Two bools
    // give three states via the computed ChatWhisperRecipientMode: master off = Off; on + in-channel = the
    // recipient rides the channel column ("[Whisper → Name]"); on + not-in-channel = it rides the name column
    // ("→ Name"). Default: show, in the channel column.
    public enum WhisperRecipientDisplay { Off, Channel, Sender }
    public static bool ChatShowWhisperRecipient => (ConfigEntries[nameof(ChatShowWhisperRecipient)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatShowWhisperRecipient(bool v) => SetBool(nameof(ChatShowWhisperRecipient), v);
    public static bool ChatWhisperRecipientInChannelColumn => (ConfigEntries[nameof(ChatWhisperRecipientInChannelColumn)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatWhisperRecipientInChannelColumn(bool v) => SetBool(nameof(ChatWhisperRecipientInChannelColumn), v);
    public static WhisperRecipientDisplay ChatWhisperRecipientMode =>
        !ChatShowWhisperRecipient ? WhisperRecipientDisplay.Off
        : ChatWhisperRecipientInChannelColumn ? WhisperRecipientDisplay.Channel
        : WhisperRecipientDisplay.Sender;
    // B5 (0.29.8): a whisper to YOURSELF reads as "Note to self" (true, default, like the base game) or as a
    // normal whisper to your own name (false — then B3's recipient display applies to it too).
    public static bool ChatSelfWhisperAsNoteToSelf => (ConfigEntries[nameof(ChatSelfWhisperAsNoteToSelf)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatSelfWhisperAsNoteToSelf(bool v) => SetBool(nameof(ChatSelfWhisperAsNoteToSelf), v);
    // 0.17.3: show a short "you're missing X (free power)" hint on the Class / Weapon
    // Expertise / Blood Legacy pages + overlays when that element isn't set up yet. Helps
    // new players; off for those who intentionally skip a system and don't want the nudge.
    // Default TRUE. Only ever shown for systems the server actually has enabled.
    public static bool ShowMissingElementHints => (ConfigEntries[nameof(ShowMissingElementHints)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetShowMissingElementHints(bool v) => SetBool(nameof(ShowMissingElementHints), v);
    // 0.17.3: per-channel inclusion in the consolidated "All" tab. All default TRUE
    // (All shows everything, as before). Uncheck one to hide that channel from the
    // All aggregate only — its own dedicated tab is unaffected.
    public static bool AllTabShowGlobal  => (ConfigEntries[nameof(AllTabShowGlobal)]  as ConfigEntry<bool>)?.Value ?? true;
    public static void SetAllTabShowGlobal(bool v)  => SetBool(nameof(AllTabShowGlobal), v);
    public static bool AllTabShowLocal   => (ConfigEntries[nameof(AllTabShowLocal)]   as ConfigEntry<bool>)?.Value ?? true;
    public static void SetAllTabShowLocal(bool v)   => SetBool(nameof(AllTabShowLocal), v);
    public static bool AllTabShowClan    => (ConfigEntries[nameof(AllTabShowClan)]    as ConfigEntry<bool>)?.Value ?? true;
    public static void SetAllTabShowClan(bool v)    => SetBool(nameof(AllTabShowClan), v);
    public static bool AllTabShowSystem  => (ConfigEntries[nameof(AllTabShowSystem)]  as ConfigEntry<bool>)?.Value ?? true;
    public static void SetAllTabShowSystem(bool v)  => SetBool(nameof(AllTabShowSystem), v);
    public static bool AllTabShowWhisper => (ConfigEntries[nameof(AllTabShowWhisper)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetAllTabShowWhisper(bool v) => SetBool(nameof(AllTabShowWhisper), v);
    // Hide NOTES TO SELF (whispers to your own character) from the main chat's 'All' tab — for players who
    // route their self-notes to the secondary view-only window instead (see SecondaryChatShowNotesToSelf).
    // The Whispers tab still shows them; only the consolidated All view hides them.
    public static bool AllTabExcludeNotesToSelf => (ConfigEntries[nameof(AllTabExcludeNotesToSelf)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetAllTabExcludeNotesToSelf(bool v) => SetBool(nameof(AllTabExcludeNotesToSelf), v);

    // 0.24: a SECOND, VIEW-ONLY chat window (SecondaryChatOverlayPanel) that mirrors a chosen subset of
    // channels — for watching two streams at once. ShowSecondaryChatOverlay = was it open at last logout;
    // the SecondaryChatShow* flags pick which channels appear in it (default Clan + System).
    public static bool ShowSecondaryChatOverlay => (ConfigEntries[nameof(ShowSecondaryChatOverlay)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetShowSecondaryChatOverlay(bool v) => SetBool(nameof(ShowSecondaryChatOverlay), v);
    public static bool SecondaryChatShowGlobal  => (ConfigEntries[nameof(SecondaryChatShowGlobal)]  as ConfigEntry<bool>)?.Value ?? false;
    public static void SetSecondaryChatShowGlobal(bool v)  => SetBool(nameof(SecondaryChatShowGlobal), v);
    public static bool SecondaryChatShowLocal   => (ConfigEntries[nameof(SecondaryChatShowLocal)]   as ConfigEntry<bool>)?.Value ?? false;
    public static void SetSecondaryChatShowLocal(bool v)   => SetBool(nameof(SecondaryChatShowLocal), v);
    public static bool SecondaryChatShowClan    => (ConfigEntries[nameof(SecondaryChatShowClan)]    as ConfigEntry<bool>)?.Value ?? true;
    public static void SetSecondaryChatShowClan(bool v)    => SetBool(nameof(SecondaryChatShowClan), v);
    public static bool SecondaryChatShowSystem  => (ConfigEntries[nameof(SecondaryChatShowSystem)]  as ConfigEntry<bool>)?.Value ?? true;
    public static void SetSecondaryChatShowSystem(bool v)  => SetBool(nameof(SecondaryChatShowSystem), v);
    public static bool SecondaryChatShowWhisper => (ConfigEntries[nameof(SecondaryChatShowWhisper)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetSecondaryChatShowWhisper(bool v) => SetBool(nameof(SecondaryChatShowWhisper), v);
    // Show ONLY notes-to-self (whispers to your own character) — independent of the Whisper toggle, so the
    // secondary window can mirror a running scratchpad of self-notes without all the whisper traffic.
    public static bool SecondaryChatShowNotesToSelf => (ConfigEntries[nameof(SecondaryChatShowNotesToSelf)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetSecondaryChatShowNotesToSelf(bool v) => SetBool(nameof(SecondaryChatShowNotesToSelf), v);
    // Extended chat retention (performance). The standalone tabbed window is the only DURABLE chat history —
    // the SERVER drops old lines after a short window (see ChatRelayService), so retaining them is the whole
    // point of the feature. On servers whose mods emit a heavy SYSTEM-message stream that can add up, so this
    // master switch turns the durable capture off entirely, and the per-channel switches let you keep the
    // channels you care about (Global/Clan/Whisper) while dropping a noisy one (System). All default ON, so
    // out-of-box behavior is unchanged. A channel that isn't captured simply won't appear in the tabbed /
    // secondary windows; the game's own native chat still shows it transiently as usual.
    public static bool ExtendedChatCaptureEnabled => (ConfigEntries[nameof(ExtendedChatCaptureEnabled)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetExtendedChatCaptureEnabled(bool v) => SetBool(nameof(ExtendedChatCaptureEnabled), v);
    public static bool ExtendedCaptureGlobal  => (ConfigEntries[nameof(ExtendedCaptureGlobal)]  as ConfigEntry<bool>)?.Value ?? true;
    public static void SetExtendedCaptureGlobal(bool v)  => SetBool(nameof(ExtendedCaptureGlobal), v);
    public static bool ExtendedCaptureLocal   => (ConfigEntries[nameof(ExtendedCaptureLocal)]   as ConfigEntry<bool>)?.Value ?? true;
    public static void SetExtendedCaptureLocal(bool v)   => SetBool(nameof(ExtendedCaptureLocal), v);
    public static bool ExtendedCaptureClan    => (ConfigEntries[nameof(ExtendedCaptureClan)]    as ConfigEntry<bool>)?.Value ?? true;
    public static void SetExtendedCaptureClan(bool v)    => SetBool(nameof(ExtendedCaptureClan), v);
    public static bool ExtendedCaptureSystem  => (ConfigEntries[nameof(ExtendedCaptureSystem)]  as ConfigEntry<bool>)?.Value ?? true;
    public static void SetExtendedCaptureSystem(bool v)  => SetBool(nameof(ExtendedCaptureSystem), v);
    public static bool ExtendedCaptureWhisper => (ConfigEntries[nameof(ExtendedCaptureWhisper)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetExtendedCaptureWhisper(bool v) => SetBool(nameof(ExtendedCaptureWhisper), v);
    public static bool ExtendedCaptureOther   => (ConfigEntries[nameof(ExtendedCaptureOther)]   as ConfigEntry<bool>)?.Value ?? true;
    public static void SetExtendedCaptureOther(bool v)   => SetBool(nameof(ExtendedCaptureOther), v);
    // 0.17.3: switch chat tabs with <Modifier>+1..6 while the chat window is open and
    // you're NOT typing in it. Modifier is Shift / Ctrl / Alt / None. Tab order:
    // 1=All, 2=Global, 3=Local, 4=Clan, 5=System, 6=Whispers.
    public static bool ChatTabHotkeysEnabled => (ConfigEntries[nameof(ChatTabHotkeysEnabled)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetChatTabHotkeysEnabled(bool v) => SetBool(nameof(ChatTabHotkeysEnabled), v);
    public static string ChatTabHotkeyModifier =>
        (ConfigEntries.TryGetValue(nameof(ChatTabHotkeyModifier), out var e) && e is ConfigEntry<string> s && !string.IsNullOrWhiteSpace(s.Value))
            ? s.Value : "Alt";
    public static void SetChatTabHotkeyModifier(string v)
    {
        if (ConfigEntries.TryGetValue(nameof(ChatTabHotkeyModifier), out var e) && e is ConfigEntry<string> s) s.Value = v;
    }
    // 0.17.0: configurable color for the GLOBAL channel (label tag + tab). Global
    // had no distinct color before (rendered plain white); default is a warm coral
    // that stands apart from Local-blue / Clan-green / System-gold / Whisper-pink.
    // Per-channel chat colors. Each drives BOTH the channel's message-text tag AND its tab (when
    // "Color tabs by channel" is on) via ChatWindowOverlayPanel.ChannelColorHex. Defaults match the
    // colors the channels used to be hardcoded to. User picks persist via the registered ConfigEntries.
    public const string DEFAULT_CHAT_GLOBAL_HEX  = "#FF8A5B";
    public const string DEFAULT_CHAT_LOCAL_HEX   = "#B0E0FF";
    public const string DEFAULT_CHAT_CLAN_HEX    = "#90EE90";
    public const string DEFAULT_CHAT_SYSTEM_HEX  = "#FFD700";
    public const string DEFAULT_CHAT_WHISPER_HEX = "#FF9CEF";
    private static string ChatColorHex(string key, string fallback)
        => (ConfigEntries.TryGetValue(key, out var e) && e is ConfigEntry<string> s && !string.IsNullOrWhiteSpace(s.Value)) ? s.Value : fallback;
    private static void SetChatColorHex(string key, string hex)
    {
        if (ConfigEntries.TryGetValue(key, out var e) && e is ConfigEntry<string> s) s.Value = hex;
    }
    public static string ChatGlobalColorHex  => ChatColorHex(nameof(ChatGlobalColorHex),  DEFAULT_CHAT_GLOBAL_HEX);
    public static string ChatLocalColorHex   => ChatColorHex(nameof(ChatLocalColorHex),   DEFAULT_CHAT_LOCAL_HEX);
    public static string ChatClanColorHex    => ChatColorHex(nameof(ChatClanColorHex),    DEFAULT_CHAT_CLAN_HEX);
    public static string ChatSystemColorHex  => ChatColorHex(nameof(ChatSystemColorHex),  DEFAULT_CHAT_SYSTEM_HEX);
    public static string ChatWhisperColorHex => ChatColorHex(nameof(ChatWhisperColorHex), DEFAULT_CHAT_WHISPER_HEX);
    public static void SetChatGlobalColorHex(string hex)  => SetChatColorHex(nameof(ChatGlobalColorHex),  hex);
    public static void SetChatLocalColorHex(string hex)   => SetChatColorHex(nameof(ChatLocalColorHex),   hex);
    public static void SetChatClanColorHex(string hex)    => SetChatColorHex(nameof(ChatClanColorHex),    hex);
    public static void SetChatSystemColorHex(string hex)  => SetChatColorHex(nameof(ChatSystemColorHex),  hex);
    public static void SetChatWhisperColorHex(string hex) => SetChatColorHex(nameof(ChatWhisperColorHex), hex);

    // 0.21: when ON, a message's BODY text is tinted by its channel color (above) — not just the [tag]/tab.
    public static bool ChatColorMessageByChannel => (ConfigEntries[nameof(ChatColorMessageByChannel)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetChatColorMessageByChannel(bool v) => SetBool(nameof(ChatColorMessageByChannel), v);
    // 0.21: when ON, YOUR OWN messages' body text uses ChatOwnMessageColorHex on every tab, so you can spot
    // your own text apart from the channel text. Overrides the channel tint for your messages.
    public static bool ChatColorOwnMessages => (ConfigEntries[nameof(ChatColorOwnMessages)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetChatColorOwnMessages(bool v) => SetBool(nameof(ChatColorOwnMessages), v);
    public const string DEFAULT_CHAT_OWN_HEX = "#FFE08A";
    public static string ChatOwnMessageColorHex => ChatColorHex(nameof(ChatOwnMessageColorHex), DEFAULT_CHAT_OWN_HEX);
    public static void SetChatOwnMessageColorHex(string hex) => SetChatColorHex(nameof(ChatOwnMessageColorHex), hex);
    // 0.17.0: chat window's OWN background theme color, independent of the main
    // panel's PanelBackgroundColorHex (so the chat window can be themed separately).
    // Same preset palette as the main panel picker. Default matches the panel default.
    public static string ChatWindowBackgroundColorHex =>
        (ConfigEntries.TryGetValue(nameof(ChatWindowBackgroundColorHex), out var e) && e is ConfigEntry<string> s && !string.IsNullOrWhiteSpace(s.Value))
            ? s.Value : DEFAULT_PANEL_BG_HEX;
    public static void SetChatWindowBackgroundColorHex(string hex)
    {
        if (ConfigEntries.TryGetValue(nameof(ChatWindowBackgroundColorHex), out var e) && e is ConfigEntry<string> s)
            s.Value = hex;
    }
    public static UnityEngine.Color ChatWindowBackgroundColor
    {
        get
        {
            if (UnityEngine.ColorUtility.TryParseHtmlString(ChatWindowBackgroundColorHex, out var c))
                return new UnityEngine.Color(c.r, c.g, c.b, 1f);
            if (UnityEngine.ColorUtility.TryParseHtmlString(DEFAULT_PANEL_BG_HEX, out var fb))
                return new UnityEngine.Color(fb.r, fb.g, fb.b, 1f);
            return new UnityEngine.Color(0.07f, 0.07f, 0.07f, 1f);
        }
    }
    public static bool ShiftSpellOverlayShowDiagnostics => (ConfigEntries[nameof(ShiftSpellOverlayShowDiagnostics)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowShiftSpellIcon      => (ConfigEntries[nameof(ShowShiftSpellIcon)]      as ConfigEntry<bool>)?.Value ?? true;
    public static bool OverlaysBehindGameMenus => (ConfigEntries[nameof(OverlaysBehindGameMenus)] as ConfigEntry<bool>)?.Value ?? true;
    public static bool EnableCustomRecipes     => (ConfigEntries[nameof(EnableCustomRecipes)]     as ConfigEntry<bool>)?.Value ?? false;
    public static bool SuppressGameInputWhileUIOpen => (ConfigEntries[nameof(SuppressGameInputWhileUIOpen)] as ConfigEntry<bool>)?.Value ?? false;
    // 0.18.2: default-ON kill-switch for the form-field half of the keyboard lock. Chat-window
    // typing always locks the keyboard (proven since 0.17.0); this gates the newer form-field
    // coverage so it can be disabled instantly without losing chat suppression.
    public static bool LockKeyboardInFormFields => (ConfigEntries[nameof(LockKeyboardInFormFields)] as ConfigEntry<bool>)?.Value ?? true;
    // 0.25.0: CONSOLE keybindings (`keybinding create` — admin-assigned hotkeys) are read raw by
    // Stunlock.Console OUTSIDE the game's input pipeline, so even the native chat's typing lock
    // can't stop them. This gates Raphael's suppression of those binds while typing / panel-open
    // (the same per-frame EnableKeybindingUpdates=false contract the game's own
    // DisableConsoleKeybindingsOnFocus component uses). Default ON.
    public static bool SuppressConsoleKeybindsWhileTyping => (ConfigEntries[nameof(SuppressConsoleKeybindsWhileTyping)] as ConfigEntry<bool>)?.Value ?? true;
    // B3 (0.19): when ON, also suppress the primary attack / spell cast while the cursor is over ANY
    // Raphael panel or overlay (not just the chat window, which is always covered). Default OFF — some
    // players want to keep casting with the cursor parked over an overlay. Feeds ONLY the (proven-safe)
    // AbilityInputSystem suppression — never movement, never the menu patches.
    public static bool BlockInputWhenPointerOverUI => (ConfigEntries[nameof(BlockInputWhenPointerOverUI)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetBlockInputWhenPointerOverUI(bool v) => SetBool(nameof(BlockInputWhenPointerOverUI), v);

    // -------------------------------------------------------------------------
    // 0.17.2: compatibility / crash-bisect kill-switches (Compatibility section).
    //
    // The 0.16.x intermittent load crash is an upstream Il2CppInterop
    // GarbageCollector_RunFinalizer_Patch fault: non-deterministic, machine- and
    // mod-mix-specific, with no managed exception logged. Raphael alone is stable; the
    // crash shows up for players running Raphael *plus other client mods*, where the
    // combined IL2CPP churn in the login window tips the latent bug. A prior
    // "all features off" diagnostic still crashed — so the remaining suspects are
    // Raphael's always-on Harmony patches (applied at load regardless of any feature
    // setting). These switches let an affected user drop each patch GROUP (the
    // patch isn't applied at all, not merely no-op'd) to find the trigger, and let
    // Raphael push its UI construction out of the volatile login window.
    //
    // ALL default to full functionality. Only turn them off to diagnose a crash.

    // When OFF, Raphael does not patch the chat systems at all: inbound command-reply
    // parsing AND the tabbed chat window stop working (big functionality loss —
    // bisect only).
    public static bool EnableChatSystemHooks =>
        (ConfigEntries[nameof(EnableChatSystemHooks)] as ConfigEntry<bool>)?.Value ?? true;

    // When OFF, Raphael does not patch the input/menu systems: typing in a Raphael form or
    // the chat window no longer suppresses character movement / abilities / menu
    // hotkeys. Functionality otherwise intact.
    public static bool EnableInputSuppressionPatches =>
        (ConfigEntries[nameof(EnableInputSuppressionPatches)] as ConfigEntry<bool>)?.Value ?? true;

    // 0.25.0: kill-switch for the native typing keyboard lock (TypingInputLock — a Raphael
    // input context registered in the game's own input-consumer stack, the exact
    // mechanism the native chat uses to lock the keyboard while typing). When OFF, only
    // the older protections run (menu-request drain + movement/ability skips) and direct
    // menu/hotkey/admin keybinds may leak while typing into Raphael fields.
    public static bool EnableNativeTypingLock =>
        (ConfigEntries[nameof(EnableNativeTypingLock)] as ConfigEntry<bool>)?.Value ?? true;

    // When OFF, Raphael does not patch UICanvasSystem: overlays always render on top of
    // in-game menus (the OverlaysBehindGameMenus feature is unavailable).
    public static bool EnableOverlayLayeringPatch =>
        (ConfigEntries[nameof(EnableOverlayLayeringPatch)] as ConfigEntry<bool>)?.Value ?? true;

    // Seconds to wait after login before Raphael restores its overlays (and starts the
    // V-Blood scanner). Building overlays synchronously the instant the player
    // spawns piled allocations into the fragile login window; deferring moves that
    // off the login frame onto a quiet one. Clamped 0..30. 0 = legacy synchronous
    // behavior (restore immediately on spawn). Default 3.
    public static int UiBuildDelaySeconds =>
        UnityEngine.Mathf.Clamp((ConfigEntries[nameof(UiBuildDelaySeconds)] as ConfigEntry<int>)?.Value ?? 3, 0, 30);

    public static void SetShowExperienceOverlay(bool v) => SetBool(nameof(ShowExperienceOverlay), v);
    public static void SetShowFamiliarOverlay(bool v)   => SetBool(nameof(ShowFamiliarOverlay),   v);
    public static void SetShowFamiliarBrowser(bool v)   => SetBool(nameof(ShowFamiliarBrowser),   v);
    public static void SetShowFamiliarQuickSpawnOverlay(bool v) => SetBool(nameof(ShowFamiliarQuickSpawnOverlay), v);

    // 0.52: ordered list (up to 5) of familiar NAMES the user pinned to the
    // Quick Spawn overlay. Stored as a single '|'-delimited string (familiar
    // names never contain '|'). Assigned from the All Familiars tab.
    public const int FamiliarQuickSpawnMaxSlots = 5;
    private static string FamiliarQuickSpawnSlotsRaw
    {
        get => (ConfigEntries.TryGetValue(nameof(FamiliarQuickSpawnSlotsRaw), out var e) && e is ConfigEntry<string> s) ? (s.Value ?? "") : "";
        set { if (ConfigEntries.TryGetValue(nameof(FamiliarQuickSpawnSlotsRaw), out var e) && e is ConfigEntry<string> s) s.Value = value ?? ""; }
    }
    public static System.Collections.Generic.List<string> GetFamiliarQuickSpawnSlots()
    {
        var list = new System.Collections.Generic.List<string>();
        var raw = FamiliarQuickSpawnSlotsRaw;
        if (!string.IsNullOrEmpty(raw))
            foreach (var part in raw.Split('|'))
            {
                var t = part.Trim();
                if (t.Length > 0 && list.Count < FamiliarQuickSpawnMaxSlots) list.Add(t);
            }
        return list;
    }
    private static void WriteFamiliarQuickSpawnSlots(System.Collections.Generic.List<string> slots)
    {
        var clean = new System.Collections.Generic.List<string>();
        if (slots != null)
            foreach (var s in slots)
            {
                var t = (s ?? "").Trim();
                if (t.Length > 0 && clean.Count < FamiliarQuickSpawnMaxSlots) clean.Add(t);
            }
        FamiliarQuickSpawnSlotsRaw = string.Join("|", clean);
    }
    /// <summary>Append a familiar to the Quick Spawn list (no-op if already present or list is full).
    /// Returns true if added.</summary>
    public static bool AddFamiliarQuickSpawnSlot(string name)
    {
        var t = (name ?? "").Trim();
        if (t.Length == 0) return false;
        var list = GetFamiliarQuickSpawnSlots();
        if (list.Count >= FamiliarQuickSpawnMaxSlots) return false;
        if (list.Exists(x => string.Equals(x, t, System.StringComparison.OrdinalIgnoreCase))) return false;
        list.Add(t);
        WriteFamiliarQuickSpawnSlots(list);
        return true;
    }
    public static void RemoveFamiliarQuickSpawnSlot(string name)
    {
        var t = (name ?? "").Trim();
        var list = GetFamiliarQuickSpawnSlots();
        list.RemoveAll(x => string.Equals(x, t, System.StringComparison.OrdinalIgnoreCase));
        WriteFamiliarQuickSpawnSlots(list);
    }
    public static bool IsFamiliarQuickSpawnSlot(string name)
    {
        var t = (name ?? "").Trim();
        if (t.Length == 0) return false;
        return GetFamiliarQuickSpawnSlots().Exists(x => string.Equals(x, t, System.StringComparison.OrdinalIgnoreCase));
    }

    // ── Faust boss-tracker overlay (up to 3 tracked V Bloods). Stored '|'-delimited (friendly names). ──
    public const int FaustBossTrackerMaxSlots = 3;
    public static void SetShowFaustBossTrackerOverlay(bool v) => SetBool(nameof(ShowFaustBossTrackerOverlay), v);
    public static void SetFaustBossTrackerAutoRefresh(bool v) => SetBool(nameof(FaustBossTrackerAutoRefresh), v);
    private static string FaustBossTrackerSlotsRaw
    {
        get => (ConfigEntries.TryGetValue(nameof(FaustBossTrackerSlotsRaw), out var e) && e is ConfigEntry<string> s) ? (s.Value ?? "") : "";
        set { if (ConfigEntries.TryGetValue(nameof(FaustBossTrackerSlotsRaw), out var e) && e is ConfigEntry<string> s) s.Value = value ?? ""; }
    }
    public static System.Collections.Generic.List<string> GetFaustBossTrackerSlots()
    {
        var list = new System.Collections.Generic.List<string>();
        var raw = FaustBossTrackerSlotsRaw;
        if (!string.IsNullOrEmpty(raw))
            foreach (var part in raw.Split('|'))
            {
                var t = part.Trim();
                if (t.Length > 0 && list.Count < FaustBossTrackerMaxSlots) list.Add(t);
            }
        return list;
    }
    private static void WriteFaustBossTrackerSlots(System.Collections.Generic.List<string> slots)
    {
        var clean = new System.Collections.Generic.List<string>();
        if (slots != null)
            foreach (var s in slots)
            {
                var t = (s ?? "").Trim();
                if (t.Length > 0 && clean.Count < FaustBossTrackerMaxSlots) clean.Add(t);
            }
        FaustBossTrackerSlotsRaw = string.Join("|", clean);
    }
    /// <summary>Append a V Blood to the boss-tracker list (no-op if already present or full). Returns true if added.</summary>
    public static bool AddFaustBossTrackerSlot(string name)
    {
        var t = (name ?? "").Trim();
        if (t.Length == 0) return false;
        var list = GetFaustBossTrackerSlots();
        if (list.Count >= FaustBossTrackerMaxSlots) return false;
        if (list.Exists(x => string.Equals(x, t, System.StringComparison.OrdinalIgnoreCase))) return false;
        list.Add(t);
        WriteFaustBossTrackerSlots(list);
        return true;
    }
    public static void RemoveFaustBossTrackerSlot(string name)
    {
        var t = (name ?? "").Trim();
        var list = GetFaustBossTrackerSlots();
        list.RemoveAll(x => string.Equals(x, t, System.StringComparison.OrdinalIgnoreCase));
        WriteFaustBossTrackerSlots(list);
    }

    public static void SetShowDailyQuestOverlay(bool v) => SetBool(nameof(ShowDailyQuestOverlay), v);
    public static void SetShowProfessionOverlay(bool v) => SetBool(nameof(ShowProfessionOverlay), v);
    public static void SetShowShiftSpellOverlay(bool v) => SetBool(nameof(ShowShiftSpellOverlay), v);
    public static void SetShowQuickActionsOverlay(bool v) => SetBool(nameof(ShowQuickActionsOverlay), v);
    public static void SetShowBeelzActionBarOverlay(bool v) => SetBool(nameof(ShowBeelzActionBarOverlay), v);
    public static void SetShowBeelzSummonsOverlay(bool v) => SetBool(nameof(ShowBeelzSummonsOverlay), v);
    public static void SetShowBeelzTransformOverlay(bool v) => SetBool(nameof(ShowBeelzTransformOverlay), v);
    public static void SetShowUrielSharedOverlay(bool v) => SetBool(nameof(ShowUrielSharedOverlay), v);
    // Faust bar charts: stretch to fill the panel width (true, default = dynamic) vs a compact, left-anchored
    // static width (false). Set via Faust → Settings.
    public static bool FaustChartStretch => (ConfigEntries[nameof(FaustChartStretch)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetFaustChartStretch(bool v) => SetBool(nameof(FaustChartStretch), v);
    // Color theme for Faust bar charts / graphs: 0=Green, 1=Teal, 2=Blue, 3=Red, 4=Amber, 5=Violet.
    public static int FaustChartColor => UnityEngine.Mathf.Clamp((ConfigEntries[nameof(FaustChartColor)] as ConfigEntry<int>)?.Value ?? 0, 0, 5);
    public static void SetFaustChartColor(int v) => SetInt(nameof(FaustChartColor), UnityEngine.Mathf.Clamp(v, 0, 5));
    // Heat-map intensity gradient (low→high): 0=Theme color (black→chart color), 1=Heat (black→red→yellow→white),
    // 2=Green (black→green→white), 3=Mono (black→white). A true cold→hot ramp reads far better than one flat hue.
    public static int FaustHeatGradient => UnityEngine.Mathf.Clamp((ConfigEntries[nameof(FaustHeatGradient)] as ConfigEntry<int>)?.Value ?? 1, 0, 6);
    public static void SetFaustHeatGradient(int v) => SetInt(nameof(FaustHeatGradient), UnityEngine.Mathf.Clamp(v, 0, 6));
    // Heat-map render detail: 0=Native (Faust's finest cell), 1=Grouped 2×, 2=Grouped 4×. Coarsening merges
    // N×N cells client-side to smooth sparse data into blobs. Cannot go FINER than Faust's [Faust.Heatmap] CellSize.
    public static int FaustHeatDetail => UnityEngine.Mathf.Clamp((ConfigEntries[nameof(FaustHeatDetail)] as ConfigEntry<int>)?.Value ?? 0, 0, 2);
    public static void SetFaustHeatDetail(int v) => SetInt(nameof(FaustHeatDetail), UnityEngine.Mathf.Clamp(v, 0, 2));
    // Heat-map board extent: 0=Map (draw to the full buildable-map bounds when Faust sends them — true scale, sparse
    // data = a few dots on the real outline), 1=Zoom (size to just the occupied cells). Falls back to Zoom if Faust
    // doesn't send mapbounds (api <17).
    public static int FaustHeatScale => UnityEngine.Mathf.Clamp((ConfigEntries[nameof(FaustHeatScale)] as ConfigEntry<int>)?.Value ?? 0, 0, 1);
    public static void SetFaustHeatScale(int v) => SetInt(nameof(FaustHeatScale), UnityEngine.Mathf.Clamp(v, 0, 1));

    // ---- Faust world-map underlay (#3): a coordinate-grid frame of reference + optional drop-in map image
    // behind the World Map asset plot and the Heat Map, so the dots have a real frame of reference instead of a
    // bare black board. The image is a user-supplied PNG at config/Raphael/worldmap.png (extract it from the game
    // assets); it's stretched to the world rectangle [MinX..MaxX]×[MinZ..MaxZ] below, which the user calibrates so
    // the dots land in the right place. The grid always draws (cheap, no asset). ----
    public static bool  FaustWorldMapGrid     => (ConfigEntries[nameof(FaustWorldMapGrid)] as ConfigEntry<bool>)?.Value ?? true;
    public static void  SetFaustWorldMapGrid(bool v) => SetBool(nameof(FaustWorldMapGrid), v);
    public static bool  FaustWorldMapImage    => (ConfigEntries[nameof(FaustWorldMapImage)] as ConfigEntry<bool>)?.Value ?? true;
    public static void  SetFaustWorldMapImage(bool v) => SetBool(nameof(FaustWorldMapImage), v);
    public static float FaustWorldMapOpacity  => UnityEngine.Mathf.Clamp01(GetFloat(nameof(FaustWorldMapOpacity), 0.55f));
    public static void  SetFaustWorldMapOpacity(float v) => SetFloat(nameof(FaustWorldMapOpacity), UnityEngine.Mathf.Clamp01(v));
    // World rectangle the map image covers (game X/Z). Defaults derived from Faust's authoritative buildable
    // mapbounds on the test server (cell-25 × [-105:-115:-21:12] ≈ X[-2625,-500] Z[-2875,325]); calibrate per map.
    public static float FaustWorldMapMinX => GetFloat(nameof(FaustWorldMapMinX), -2900f);
    public static float FaustWorldMapMaxX => GetFloat(nameof(FaustWorldMapMaxX),   180f);
    public static float FaustWorldMapMinZ => GetFloat(nameof(FaustWorldMapMinZ), -2400f);
    public static float FaustWorldMapMaxZ => GetFloat(nameof(FaustWorldMapMaxZ),   650f);
    public static void  SetFaustWorldMapBounds(float minX, float maxX, float minZ, float maxZ)
    {
        SetFloat(nameof(FaustWorldMapMinX), minX); SetFloat(nameof(FaustWorldMapMaxX), maxX);
        SetFloat(nameof(FaustWorldMapMinZ), minZ); SetFloat(nameof(FaustWorldMapMaxZ), maxZ);
    }
    // Per-query-type cooldown (seconds) on Faust server reads — stops spam-clicking Refresh from hammering
    // the server (important with many simultaneous players). 0 disables. Default 5.
    public static int FaustQueryCooldownSeconds => (ConfigEntries[nameof(FaustQueryCooldownSeconds)] as ConfigEntry<int>)?.Value ?? 5;
    public static void SetShowUrielObjectSpawnerOverlay(bool v) => SetBool(nameof(ShowUrielObjectSpawnerOverlay), v);
    public static void SetShowChatWindowOverlay(bool v) => SetBool(nameof(ShowChatWindowOverlay), v);
    public static void SetChatShowTimestamps(bool v) => SetBool(nameof(ChatShowTimestamps), v);
    public static void SetChatShowChannelTags(bool v) => SetBool(nameof(ChatShowChannelTags), v);
    public static void SetHideNativeChat(bool v) => SetBool(nameof(HideNativeChat), v);
    public static void SetAggressiveNativeChatHide(bool v) => SetBool(nameof(AggressiveNativeChatHide), v);
    public static void SetChatAllTabDefaultGlobal(bool v) => SetBool(nameof(ChatAllTabDefaultGlobal), v);
    public static void SetOverlaysBehindGameMenus(bool v) => SetBool(nameof(OverlaysBehindGameMenus), v);
    public static void SetSuppressGameInputWhileUIOpen(bool v) => SetBool(nameof(SuppressGameInputWhileUIOpen), v);

    private static void SetBool(string key, bool value)
    {
        if (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<bool> b)
            b.Value = value;
    }

    private static void SetInt(string key, int value)
    {
        if (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<int> i)
            i.Value = value;
    }

    // Auto-resize: main panel grows vertically to fit content (capped at 90% of
    // screen height). User-toggleable via the footer checkbox - some players
    // prefer a fixed-size panel they can manually resize.
    public static bool IsPanelAutoResizeEnabled =>
        (ConfigEntries[nameof(IsPanelAutoResizeEnabled)] as ConfigEntry<bool>)?.Value ?? true;
    // 0.9.7: setter for the new Size & Positioning section's Auto-size button.
    // The existing footer toggle writes via direct Config.Bind — exposing a
    // proper setter keeps the new callsite aligned with how every other
    // Settings flag handles writes (Set* methods + SetBool helper).
    public static void SetIsPanelAutoResizeEnabled(bool v) => SetBool(nameof(IsPanelAutoResizeEnabled), v);

    // Left-rail accordion: opening one tab group (Bloodcraft / Beelzebub / Kindred / Uriel / Faust /
    // Settings & Help) auto-collapses the others, so the rail stays short on small screens. Default ON;
    // turn it off to keep multiple groups expanded at once.
    public static bool LeftRailAccordion =>
        (ConfigEntries[nameof(LeftRailAccordion)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetLeftRailAccordion(bool v) => SetBool(nameof(LeftRailAccordion), v);

    // (SuspendGameInputWhileTyping was removed in 0.8.2 — its Harmony prefix on
    // InputActionSystem.OnUpdate also wedged Unity's UI input pipeline,
    // locking the entire game when users typed into a form. A proper fix needs
    // a different patch target; until then the feature is gone. SuspendGameInputWhileUIOpen
    // was removed in 0.1.2 for the same reason. Stale .cfg entries are inert.)

    // Tristate per-server-mod availability (Auto / On / Off). Auto uses a
    // probe to decide:
    //   - Bloodcraft: present iff EclipseProtocolService.UserRegistered ever
    //     becomes true within the session (the server ACK'd our handshake).
    //   - Kindred: no protocol indicator, so Auto defaults to "assume present"
    //     for now; flip to Off manually if your server doesn't have it.
    // Set to Off and the corresponding tab group renders collapsed + grayed.
    public enum ModAvailability { Auto, On, Off }

    private static ModAvailability ReadAvailability(string key)
    {
        var raw = (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<string> s) ? s.Value : "Auto";
        return raw switch { "On" => ModAvailability.On, "Off" => ModAvailability.Off, _ => ModAvailability.Auto };
    }

    public static ModAvailability BloodcraftAvailability => ReadAvailability(nameof(BloodcraftAvailability));
    public static ModAvailability KindredAvailability    => ReadAvailability(nameof(KindredAvailability));
    // 0.18: Beelzebub is a still-in-development server mod most servers lack.
    // Auto = present iff the `.beelz api version` handshake ACKs ready=1.
    public static ModAvailability BeelzebubAvailability  => ReadAvailability(nameof(BeelzebubAvailability));
    // 0.26: Uriel is a still-in-development sibling server mod most servers lack.
    // Auto = present iff the `.uriel api version` handshake ACKs ready=1.
    public static ModAvailability UrielAvailability      => ReadAvailability(nameof(UrielAvailability));
    // Faust is a still-in-development sibling server mod (investigation/information) most servers lack.
    // Auto = present iff the `.faust api version` handshake ACKs ready=1.
    public static ModAvailability FaustAvailability      => ReadAvailability(nameof(FaustAvailability));
    public static void SetBloodcraftAvailability(ModAvailability v) => SetAvailability(nameof(BloodcraftAvailability), v);
    public static void SetKindredAvailability(ModAvailability v)    => SetAvailability(nameof(KindredAvailability), v);
    public static void SetBeelzebubAvailability(ModAvailability v)  => SetAvailability(nameof(BeelzebubAvailability), v);
    public static void SetUrielAvailability(ModAvailability v)      => SetAvailability(nameof(UrielAvailability), v);
    public static void SetFaustAvailability(ModAvailability v)      => SetAvailability(nameof(FaustAvailability), v);
    // Nyarlathotep: admin-run chaos/event sibling server mod most servers lack.
    // Auto = present iff the `.nyar api version` handshake ACKs ready=1.
    public static ModAvailability NyarAvailability       => ReadAvailability(nameof(NyarAvailability));
    public static void SetNyarAvailability(ModAvailability v)       => SetAvailability(nameof(NyarAvailability), v);

    // 0.18: Beelzebub diagnostic detail (default off). When ON: the loadout tables show
    // each ability's ID (PrefabGUID) + raw prefab name, and Raphael writes a verbose wire
    // trace ([Beelz][diag] >>/<< lines) to the BepInEx log so testers/admins can report
    // exactly which abilities work or need tuning. Also honored when the global
    // DiagnosticMode is on (see BeelzDiag). Toggled from the Beelzebub → Settings tab.
    public static bool BeelzDiagnostics => (ConfigEntries.TryGetValue(nameof(BeelzDiagnostics), out var e) && e is ConfigEntry<bool> b) && b.Value;
    public static void SetBeelzDiagnostics(bool v) => SetBool(nameof(BeelzDiagnostics), v);

    // 0.26: Uriel diagnostic detail (default off). When ON, Raphael writes a verbose [Uriel][diag] wire
    // trace (commands sent + raw [URIEL:*] replies) to the BepInEx LogOutput.log so testers/admins can
    // report exactly which Uriel commands fired and what came back. Toggled from the Uriel → Settings
    // tab; also implied while the global DiagnosticMode is active (see UrielDiag).
    public static bool UrielDiagnostics => (ConfigEntries.TryGetValue(nameof(UrielDiagnostics), out var eu) && eu is ConfigEntry<bool> bu) && bu.Value;
    public static void SetUrielDiagnostics(bool v) => SetBool(nameof(UrielDiagnostics), v);

    // Faust diagnostic detail (default off). When ON, Raphael writes a verbose [Faust][diag] wire trace
    // (commands sent + raw [FAUST:*] replies) to the BepInEx LogOutput.log so testers/admins can report
    // exactly which Faust queries fired and what came back. Toggled from the Faust → Settings tab; also
    // implied while the global DiagnosticMode is active (see FaustDiag).
    public static bool FaustDiagnostics => (ConfigEntries.TryGetValue(nameof(FaustDiagnostics), out var ef) && ef is ConfigEntry<bool> bf) && bf.Value;
    public static void SetFaustDiagnostics(bool v) => SetBool(nameof(FaustDiagnostics), v);

    // Nyarlathotep diagnostic detail (default off): verbose [Nyar][diag] wire trace in LogOutput.log.
    // Toggled from Nyarlathotep → Settings; also implied while the global DiagnosticMode is active (NyarDiag).
    public static bool NyarDiagnostics => (ConfigEntries.TryGetValue(nameof(NyarDiagnostics), out var en) && en is ConfigEntry<bool> bn) && bn.Value;
    public static void SetNyarDiagnostics(bool v) => SetBool(nameof(NyarDiagnostics), v);

    // 0.67.2: show a mod's machine-readable protocol lines ([BEELZ:*] / [URIEL:*] / [FAUST:*] / [NYAR:*]) in chat
    // instead of removing them after Raphael reads them (default off). Raphael still parses every line. Replies to a
    // `.<mod> …` command the player sent themselves pass through for a few seconds even while this is off
    // (see OutboundLedger). Toggled from Settings and Help → Chat & Diagnostics.
    private static bool GetBoolOr(string key, bool fallback)
        => ConfigEntries.TryGetValue(key, out var e) && e is ConfigEntry<bool> b ? b.Value : fallback;
    public static bool BeelzShowProtocolInChat => GetBoolOr(nameof(BeelzShowProtocolInChat), false);
    public static bool UrielShowProtocolInChat => GetBoolOr(nameof(UrielShowProtocolInChat), false);
    public static bool FaustShowProtocolInChat => GetBoolOr(nameof(FaustShowProtocolInChat), false);
    public static bool NyarShowProtocolInChat  => GetBoolOr(nameof(NyarShowProtocolInChat),  false);
    public static void SetBeelzShowProtocolInChat(bool v) => SetBool(nameof(BeelzShowProtocolInChat), v);
    public static void SetUrielShowProtocolInChat(bool v) => SetBool(nameof(UrielShowProtocolInChat), v);
    public static void SetFaustShowProtocolInChat(bool v) => SetBool(nameof(FaustShowProtocolInChat), v);
    public static void SetNyarShowProtocolInChat(bool v)  => SetBool(nameof(NyarShowProtocolInChat),  v);

    // Faust decay/duration display granularity (Castle Info + All Plots decay timers). 0=Auto (the two
    // largest non-zero units, so a weeks-long timer never renders as a huge hour count), 1=Hours+minutes
    // (legacy), 2=Days/hours/minutes, 3=Weeks/days/hours/minutes. Cycled from the Faust → Settings tab.
    public static int FaustDurationStyle => (ConfigEntries[nameof(FaustDurationStyle)] as ConfigEntry<int>)?.Value ?? 0;
    public static void SetFaustDurationStyle(int v) => SetInt(nameof(FaustDurationStyle), v);

    // 0.19: after a Raphael grant/unslot, auto-send `.beelz refresh` so the in-game action bar reflects the
    // new ability immediately (the server's own refresh doesn't always show it). Default ON.
    public static bool BeelzAutoRefreshBar =>
        (ConfigEntries.TryGetValue(nameof(BeelzAutoRefreshBar), out var e2) && e2 is ConfigEntry<bool> b2) ? b2.Value : true;
    public static void SetBeelzAutoRefreshBar(bool v) => SetBool(nameof(BeelzAutoRefreshBar), v);

    // 0.22: first-run onboarding flag. False until the panel's first-ever open auto-routes the user to the
    // Quick Start tab (a one-time welcome instead of dropping them straight into a feature tab). Set true
    // right after. Reset to false in the .cfg to see the welcome again.
    public static bool HasSeenWelcome =>
        (ConfigEntries.TryGetValue(nameof(HasSeenWelcome), out var ehw) && ehw is ConfigEntry<bool> bhw) && bhw.Value;
    public static void SetHasSeenWelcome(bool v) => SetBool(nameof(HasSeenWelcome), v);

    // 0.19: label the Loadout slot buttons with the KEY each slot uses (LM/Q/Sp/Sh/E/R/C/T) vs numbers
    // (P/1-6/U). 0.24: default flipped to KEYS (the keys read more clearly than the slot numbers); numbers
    // are now the alternative. Existing installs keep whatever they already saved in the .cfg.
    public static bool BeelzSlotKeyLabels =>
        (ConfigEntries.TryGetValue(nameof(BeelzSlotKeyLabels), out var e3) && e3 is ConfigEntry<bool> b3) ? b3.Value : true;
    public static void SetBeelzSlotKeyLabels(bool v) => SetBool(nameof(BeelzSlotKeyLabels), v);

    // 0.15.0: configurable keyboard hotkeys for the two floating-button
    // actions. Opt-in by design — default value is KeyboardShortcut.Empty
    // so first-time users get the v0.14-and-earlier mouse-only experience.
    // Users bind via Settings → Display → Hotkeys (click-to-bind UI) or
    // by editing the .cfg directly. The bind UI accepts a single key OR
    // a key + modifier combo (Ctrl+/Alt+/Shift+) — BepInEx's
    // KeyboardShortcut handles the combo natively.
    //
    // Friend-test 0.14.0 motivation: streamers / users who set the
    // floating button to very low transparency couldn't find it again
    // to click; the Raphael/OV buttons were also susceptible to a controller-
    // A-press ghost-activation regression (Item 4 in 0.15.0). Hotkeys
    // give an alternative entry point that never depends on cursor or
    // gamepad focus state.
    public static BCHotkey HotkeyToggleMainPanel  => ReadHotkey(nameof(HotkeyToggleMainPanel));
    public static BCHotkey HotkeyToggleAllOverlays => ReadHotkey(nameof(HotkeyToggleAllOverlays));
    public static void SetHotkeyToggleMainPanel(BCHotkey v)  => WriteHotkey(nameof(HotkeyToggleMainPanel), v);
    public static void SetHotkeyToggleAllOverlays(BCHotkey v) => WriteHotkey(nameof(HotkeyToggleAllOverlays), v);

    // 0.26: Uriel build-mode hotkeys (move / rotate / remove the nearest spawned object). The binds
    // persist; the build-MODE itself is a session-only in-memory flag (UrielBuildMode.Active) that
    // resets OFF on every login, so the keys only do anything while the player has deliberately turned
    // build mode on. Empty by default — bind from Uriel → Object Spawning → Building hotkeys.
    public static BCHotkey UrielBuildMoveKey   => ReadHotkey(nameof(UrielBuildMoveKey));
    public static BCHotkey UrielBuildRotateKey => ReadHotkey(nameof(UrielBuildRotateKey));
    public static BCHotkey UrielBuildRemoveKey => ReadHotkey(nameof(UrielBuildRemoveKey));
    public static void SetUrielBuildMoveKey(BCHotkey v)   => WriteHotkey(nameof(UrielBuildMoveKey), v);
    public static void SetUrielBuildRotateKey(BCHotkey v) => WriteHotkey(nameof(UrielBuildRotateKey), v);
    public static void SetUrielBuildRemoveKey(BCHotkey v) => WriteHotkey(nameof(UrielBuildRemoveKey), v);
    private static BCHotkey ReadHotkey(string key)
    {
        if (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<string> s)
            return BCHotkey.Parse(s.Value);
        return BCHotkey.Empty;
    }
    private static void WriteHotkey(string key, BCHotkey v)
    {
        if (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<string> s)
            s.Value = v.ToString();
    }

    // 0.18: per-ability keyboard shortcuts for the Beelzebub action bar. Beelzebub "hotkeys"
    // are dynamic (server-driven, keyed by NAME), so a fixed ConfigEntry per binding won't do —
    // the whole map is serialized into one string entry as "name=combo;name=combo". Kept in an
    // in-memory dictionary (the per-frame poller reads it without re-parsing every frame).
    private static readonly Dictionary<string, BCHotkey> _beelzKeybinds = new(StringComparer.OrdinalIgnoreCase);
    private static bool _beelzKeybindsLoaded;

    public static IReadOnlyDictionary<string, BCHotkey> BeelzKeybinds { get { EnsureBeelzKeybindsLoaded(); return _beelzKeybinds; } }

    public static BCHotkey GetBeelzKeybind(string name)
    {
        EnsureBeelzKeybindsLoaded();
        return (!string.IsNullOrEmpty(name) && _beelzKeybinds.TryGetValue(name, out var v)) ? v : BCHotkey.Empty;
    }

    public static void SetBeelzKeybind(string name, BCHotkey v)
    {
        if (string.IsNullOrEmpty(name)) return;
        EnsureBeelzKeybindsLoaded();
        // The map serializes with ';' / '=' delimiters; keep them out of the key.
        string key = name.Replace(';', '_').Replace('=', '_');
        if (v.IsEmpty) _beelzKeybinds.Remove(key);
        else           _beelzKeybinds[key] = v;
        WriteBeelzKeybinds();
    }

    private static void EnsureBeelzKeybindsLoaded()
    {
        if (_beelzKeybindsLoaded) return;
        _beelzKeybindsLoaded = true;
        if (!ConfigEntries.TryGetValue("BeelzHotkeyBinds", out var entry) || entry is not ConfigEntry<string> s) return;
        if (string.IsNullOrWhiteSpace(s.Value)) return;
        foreach (var pair in s.Value.Split(';'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string nm = pair.Substring(0, eq).Trim();
            var hk = BCHotkey.Parse(pair.Substring(eq + 1).Trim());
            if (!string.IsNullOrEmpty(nm) && !hk.IsEmpty) _beelzKeybinds[nm] = hk;
        }
    }

    private static void WriteBeelzKeybinds()
    {
        if (!ConfigEntries.TryGetValue("BeelzHotkeyBinds", out var entry) || entry is not ConfigEntry<string> s) return;
        var parts = new List<string>();
        foreach (var kv in _beelzKeybinds)
            if (!kv.Value.IsEmpty) parts.Add($"{kv.Key}={kv.Value}");
        s.Value = string.Join(";", parts);
    }

    // 0.15.0 friend-test v3: three-state diagnostic mode. Only Off /
    // Always persist to the .cfg — Session is a runtime-only override
    // that resets on game restart so users who flip it on to
    // reproduce a single bug don't accidentally leave verbose logging
    // running forever.
    //
    //   Off       — no [DIAG] logs.
    //   Session   — log THIS run only; .cfg stays at Off.
    //   Always    — log every run until the user changes it.
    public enum DiagnosticModeChoice { Off, Session, Always }

    // Persisted "Off" or "Always" — set by SetDiagnosticMode. Session is
    // a per-process override and never reaches this value.
    private static bool DiagnosticPersistedAlways
    {
        get => (ConfigEntries.TryGetValue(nameof(DiagnosticMode), out var e) && e is ConfigEntry<string> s)
            && string.Equals(s.Value, "Always", System.StringComparison.OrdinalIgnoreCase);
        set
        {
            if (ConfigEntries.TryGetValue(nameof(DiagnosticMode), out var e) && e is ConfigEntry<string> s)
                s.Value = value ? "Always" : "Off";
        }
    }

    // Runtime-only flag for "Session" mode. Set by the radio UI; never
    // written to disk. Defaults false; cleared by SetDiagnosticMode(Off).
    private static bool _diagnosticSessionActive;

    /// <summary>True if LogDiagnostic should emit. Used by
    /// LogUtils.LogDiagnostic as the cheap gate on every call.</summary>
    public static bool DiagnosticMode => DiagnosticPersistedAlways || _diagnosticSessionActive;

    /// <summary>Three-state read for the UI. "Session" wins over
    /// "Always" only when the user explicitly picked Session this run.
    /// In practice the UI radio buttons preserve mutual exclusion so
    /// the three states never overlap.</summary>
    public static DiagnosticModeChoice DiagnosticModeSetting
    {
        get
        {
            if (DiagnosticPersistedAlways) return DiagnosticModeChoice.Always;
            if (_diagnosticSessionActive)  return DiagnosticModeChoice.Session;
            return DiagnosticModeChoice.Off;
        }
    }

    public static void SetDiagnosticMode(DiagnosticModeChoice choice)
    {
        switch (choice)
        {
            case DiagnosticModeChoice.Off:
                DiagnosticPersistedAlways = false;
                _diagnosticSessionActive = false;
                break;
            case DiagnosticModeChoice.Session:
                DiagnosticPersistedAlways = false; // stays Off in .cfg
                _diagnosticSessionActive = true;   // active for THIS session only
                break;
            case DiagnosticModeChoice.Always:
                DiagnosticPersistedAlways = true;
                _diagnosticSessionActive = true;   // immediate effect without restart
                break;
        }
    }
    private static void SetAvailability(string key, ModAvailability v)
    {
        if (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<string> s)
            s.Value = v.ToString();
    }

    // (Settings.IsAdmin was removed in 0.8.2 — the toggle gate it backed was
    // unreliable: ShowTab(ActiveTab) on the same tab didn't always rebuild the
    // page, so the user had to fully relaunch the game for admin tabs to
    // surface. Admin tabs are now always visible with an info note at the top;
    // the server enforces permissions, so non-admins clicking commands just
    // get rejection messages.)

    public Settings InitConfig()
    {
        if (!Directory.Exists(CONFIG_PATH)) Directory.CreateDirectory(CONFIG_PATH);

        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(ClearServerMessages),         true,  "Clear server and command messages from chat.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(SuppressFamiliarActionChatter), false, "Suppress the chat confirmation lines that Bloodcraft prints when you switch boxes / bind / unbind / move / smartbind familiars. The UI still updates normally (box list, contents, and overlays read from separate pipes). Off by default; toggle in Display settings.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(SuppressCommandFrameworkErrors), true,  "Hide command-framework (VCF) error chatter from chat — the '[error]', '[denied]', and 'parameter conversion error' lines that appear when a command Raphael sends (or an admin button clicked by a non-admin) isn't usable on this server. These are never Raphael's own data, so they're noise — especially on servers that only have Beelzebub / Kindred (no Bloodcraft). On by default. Since 0.67.2 this only hides errors caused by Raphael's own background queries — errors for commands you send yourself always show. Settings and Help → Chat & Diagnostics.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowProgressBars),            false, "Show experience progress and prestige progress as horizontal bars alongside the % numbers. Affects the XP overlay (XP%) and the Prestige info box (level/max). Off by default; toggle in Display settings.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowOverlayBonusStats),       false, "Show the chosen bonus-stat names AND their current numeric values for your weapon expertise and blood legacy on the XP overlay. Auto-fetches .wep get + .bl get every 10s while the overlay is visible. Off by default for a minimal overlay; toggle in Display settings.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowOverlayStatAcronyms),     false, "Abbreviate bonus-stat names on the overlays (Eclipse-style: PhysicalPower -> PhysPwr, SpellCriticalStrikeChance -> SpellCritCh, etc.) so each stat stays on one line and the wrapped sub-row can't overlap the bar/row around it at large text. Off = full names. Toggle in Display settings.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowOverlayXpCounter),        false, "Show a numerical XP-progress row under Weapon and Legacy on the XP overlay (e.g. \"Exp: 123 / 4500 (2.7%)\"). Values come from parsing .wep get / .bl get chat replies. Off by default — the existing 'Lv X (P%)' title row is sufficient for most users.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ProgressBarHeight),           8,     "Progress bar height in pixels when 'Scale bar with overlay' is OFF. Clamped 4..24. Default 8.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ProgressBarHeightRelative),   false, "Scale progress bar height with the overlay (pre-0.10.7 behavior). Off by default — bars stay at the fixed pixel height regardless of overlay size.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(OverlayEdgePadding),          6,     "Left/right inner padding (pixels) applied to every overlay's content. Prevents text from sitting flush with the panel border. Clamped 0..32. Default 6. Applied at overlay construction; toggle an overlay off and back on (or change the overlay text scale) to pick up a new value live.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(PanelBackgroundColorHex),     DEFAULT_PANEL_BG_HEX, "Background color for every Raphael panel — main panel + Familiar Browser + all five info overlays. Hex string (e.g. #121212 = default near-black, #1A0A0A = warm dark, #0A0F1A = cool dark). Light colors may reduce text legibility — the white labels in Raphael assume a dark background. Pick from presets in Settings → Display, or edit manually for any color. Transparency is configured separately by the per-panel transparency sliders.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(InnerPanelBackgroundColorHex), DEFAULT_INNER_BG_HEX, "Interior background color for the main panel and Familiar Browser — specifically the scroll-view wrapper + viewport surfaces where tab content or familiar rows render. Pre-0.12.0 this was bright red by framework default (UIFactory.CreateScrollView painted the wrapper Theme.Level1). Independent of the outer panel color so users can build a two-tone theme.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ButtonBackgroundColorHex),    DEFAULT_BUTTON_BG_HEX, "Background color for the buttons Raphael builds — the Raphael/OV launcher buttons, Stash All, the Familiar Browser buttons, and most others. Hex string (default #4D4D4D = the original neutral grey). Buttons with a deliberate color (the red Danger/WIPE buttons) are not affected. Pick from presets in Settings → Display, or edit manually. Recolors live.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(FloatingButtonScale),         1.0f,  "Size of the always-on Raphael / OV launcher buttons in the top-right corner, as a scale factor. Some displays render them large; lower this to shrink them. Clamped 0.5–1.5 (50%–150%); default 1.0. Adjust in Settings → Display → 'Launcher button size'.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(EnableBeelzAbilityIcons),     true,  "Show each Beelzebub hotkey ability's ICON on the Beelz action-bar overlay buttons (instead of a text label). Default ON. (Was briefly off while investigating a server-switch crash, but that crash reproduced with icons OFF too, so it's not this — icons are back on.) Turn off if you prefer text labels.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(BeelzAutoRefreshBar),         true,  "After you grant/unslot a Beelzebub ability from Raphael, automatically re-apply your action bar (.beelz refresh) so the new ability shows immediately. Default ON; turn off if the server already refreshes reliably or it causes chat noise.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(HasSeenWelcome),              false, "Internal first-run flag. The very first time the Raphael panel opens it routes you to the Quick Start tab as a one-time welcome, then sets this true. Reset to false to see the welcome again on next open.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(BeelzSlotKeyLabels),          true,  "Label the Loadout slot buttons with the KEY each slot uses (LM / Q / Sp / Sh / E / R / C / T). Default ON. Turn OFF to use slot numbers (P / 1-6 / U) instead. Toggle in Beelzebub → Settings.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowProfessionEnchanting),    true,  "Profession overlay: show Enchanting row.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowProfessionAlchemy),       true,  "Profession overlay: show Alchemy row.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowProfessionHarvesting),    true,  "Profession overlay: show Harvesting row.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowProfessionBlacksmithing), true,  "Profession overlay: show Blacksmithing row.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowProfessionTailoring),     true,  "Profession overlay: show Tailoring row.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowProfessionWoodcutting),   true,  "Profession overlay: show Woodcutting row.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowProfessionMining),        true,  "Profession overlay: show Mining row.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowProfessionFishing),       true,  "Profession overlay: show Fishing row.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(AutoScanVBloodsOnTabOpen),    false, "Automatically run a V-Blood scan the first time you open the V-Bloods tab in a session. Off by default — the scanner switches your active box ~10-15 times to walk all boxes; the user-controlled 'Scan all' button is the default trigger. Turn on if you want the scan to fire without a click.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(LockOverlays),                false, "Lock the position and size of every overlay so they can't be moved or resized by accident during play. Programmatic resize when settings change (e.g. enabling progress bars on the XP overlay) still works. Toggle via the 'Lock overlays' switch beside Auto-resize on the main panel.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(HideChatWithOverlaysToggle),  false, "When ON, the upper-right 'hide all overlays' button ALSO hides the Raphael chat window. Default OFF — chat stays visible while the other overlays hide (the usual behavior). Toggle via the 'Hide chat too' switch beside 'Lock overlays' on the main panel.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(OverlayTimedHide),            false, "When ON, the master overlay hide (OV button / hide-all hotkey) is a TIMED hide: overlays auto-reappear after the configured duration instead of staying hidden until you toggle back. Default OFF (sticky toggle). Configure in Settings → Display → Overlay Visibility.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(OverlayHideDurationSeconds),  25,    "How long a timed overlay hide lasts before everything auto-reappears, in seconds. Only used when 'Timed hide' is on. Clamped 5–600 (up to 10 minutes, for timed video captures); default 25.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(HideLauncherButtonsWithOverlays), false, "When ON, the master overlay hide ALSO hides the always-on Raphael / OV launcher buttons (top-right) for a fully clean screen. Only takes effect when there's a way back — i.e. 'Timed hide' is on OR a hide-all hotkey is bound — otherwise it's ignored so you can't get stranded. Default OFF.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(KeepNativeChatHiddenWhileOverlaysHidden), true, "When the master hide also hides Raphael chat ('Hide chat too' on), keep the GAME's native chat hidden as well for a clean screen instead of letting it pop back. Default ON. Turn OFF if you'd rather still have the game chat available while Raphael overlays are hidden.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowPrestigeSubLine),         false, "Show a thin secondary fill inside the main XP/expertise/legacy bars reflecting prestige progress, like Eclipse's overlay. Off by default.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(VBloodPerInstanceView),       false, "V-Bloods tab: render one row per CAPTURED FAMILIAR (level / box / shiny / primal / summon) instead of one row per V-Blood name with capture chips. Requires box contents to have been cached (navigate each box at least once). Off by default — chip view shows the full registry at a glance.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(FamiliarSortOrderSetting),    0,     "Sort order for the Familiar Browser overlay and the V-Bloods tab. 0=Default (server/registry order), 1=Alphabetical by name, 2=By level (descending), 3=By region. Cycle the Sort button in either UI to change.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(OverlayTextAlignmentSetting), 0,     "Overlay text alignment. 0=Left (default), 1=Right. Toggle in Display Settings → HUD extras. Rebuilds open overlays so the change takes effect immediately.");
        // 0.10.6: Chat Logging diagnostic toggles. Default hides Raphael's own
        // auto-fired chat (V-Blood scanner, overlay bonus-stats refresh, tab
        // auto-refresh — all silent by default). Bloodcraft + Kindred default
        // visible so user-initiated commands surface their replies as today.
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(ShowChatBchAuto),             false, "Diagnostic: show Raphael's own auto-fired command replies in chat (V-Blood scanner, overlay bonus-stats refresh, etc.). Off by default — turn on if a Raphael feature isn't working and you want to see the raw server replies.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(ShowChatBloodcraft),          true,  "Show chat copies of replies to Bloodcraft commands whose data Raphael already mirrors to its UI (BoxList, BloodInfo, etc.). On = chat + UI both show the info. Off = UI-only (less chat noise). Action confirmations and admin replies that Raphael doesn't parse stay visible regardless.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(ShowChatKindred),             true,  "Same as the Bloodcraft toggle but for KindredCommands / KindredLogistics replies Raphael structurally parses. Raphael doesn't structurally parse any Kindred replies yet, so this toggle has no effect today; reserved for when Kindred structured parsing lands in a future version.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FamStatsQueryIntervalInSeconds), 10,  "Query interval for familiar stats update (min 5s).");

        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(UseHorizontalContentLayout),  true,  "Horizontal vs vertical layout for the main content panel.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(UITransparency),              0.6f,  "Background opacity for all panels (0=transparent .. 1=opaque).");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsFamStatsPanelEnabled),      true,  "Show the familiar stats panel.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsBoxPanelEnabled),           true,  "Show the box panel.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsBindButtonEnabled),         true,  "Show the bind button.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsCombatButtonEnabled),       true,  "Show the combat button.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsPrestigeButtonEnabled),     true,  "Show the prestige button.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsToggleButtonEnabled),       true,  "Show the toggle button.");

        InitConfigEntry(FAM_SETTINGS_GROUP,     nameof(LastBindCommand),             "",    "Last bind command sent (used to restore selection).");
        InitConfigEntry(FAM_SETTINGS_GROUP,     nameof(AutoEnableFamiliarEquipment), true,  "Automatically enable familiar equipment management on UI bring-up.");

        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowExperienceOverlay),       false, "Whether the XP overlay was visible at last logout. Restored automatically on UI bring-up.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowFamiliarOverlay),         false, "Whether the Familiar overlay (active stats) was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowFamiliarBrowser),         false, "Whether the Familiar Browser overlay was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowFamiliarQuickSpawnOverlay), false, "Whether the Familiar Quick Spawn overlay (up to 5 one-click familiar-summon buttons) was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(FamiliarQuickSpawnSlotsRaw),   "",    "The familiars pinned to the Quick Spawn overlay — a '|'-delimited list of up to 5 familiar names. Edited from the All Familiars tab.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowFaustBossTrackerOverlay),  false, "Whether the Faust boss-tracker overlay (up to 3 tracked V Blood statuses) was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(FaustBossTrackerSlotsRaw),     "",    "The V Bloods pinned to the boss-tracker overlay — a '|'-delimited list of up to 3 boss names. Edited from the Faust → Boss Status tab.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(FaustBossTrackerAutoRefresh),  false, "When on, the boss-tracker overlay re-queries the Faust boss board every ~5s (bounded by the Faust query cooldown). Off by default to avoid extra server load.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowDailyQuestOverlay),       false, "Whether the Daily Quest overlay was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowProfessionOverlay),       false, "Whether the Professions overlay (Bloodcraft profession levels) was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowShiftSpellOverlay),       false, "Whether the Shift-spell cooldown overlay (Eclipse-style visual readout for the slot-3 ability) was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowQuickActionsOverlay),     false, "Whether the Quick Actions overlay (one-click Kindred command buttons, e.g. Stash All) was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowBeelzActionBarOverlay),   false, "Whether the Beelz Action Bar overlay (on-screen buttons + cooldown rings for Beelzebub hotkey abilities beyond the 6 spell slots) was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowBeelzSummonsOverlay),     false, "Whether the Beelz Summons overlay (one-click stash/restore + recall/clear for your Beelzebub summons) was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowUrielSharedOverlay),       false, "Whether the Uriel \"Nearby Public Storage\" overlay (client-side list of Uriel-shared containers/cells around you) was visible at last logout.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustQueryCooldownSeconds),      5,     "Minimum seconds between repeat Faust server queries of the same kind (anti-spam — a fast double-click or held click won't fire a second request until this elapses). Protects the server when many players query at once. 0 disables. Default 5.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustChartStretch),              true,  "Faust bar charts: ON = stretch the bars to fill the panel width (dynamic); OFF = a compact, left-anchored static width. Changes apply when the chart re-renders (switch view or Refresh). Set via Faust → Settings.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustChartColor),                0,     "Color theme for Faust bar charts / graphs: 0=Green, 1=Teal, 2=Blue, 3=Red, 4=Amber, 5=Violet. Applies when a chart re-renders. Set via Faust → Settings → Charts.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustHeatGradient),              1,     "Faust heat-map color ramp (low→high traffic): 0=Theme (black→chart color), 1=Heat (black→red→yellow→white), 2=Green (black→green→white), 3=Mono (black→white). Set via Faust → Settings → Heat map.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustHeatDetail),                0,     "Faust heat-map render detail: 0=Native (Faust's finest cell), 1=Grouped 2×, 2=Grouped 4×. Higher = merge cells into bigger blobs (smooths sparse data); cannot exceed Faust's own CellSize resolution. Set via Faust → Settings → Heat map.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustHeatScale),                 0,     "Faust heat-map board extent: 0=Map (draw to the full buildable-map bounds — true scale, needs Faust api 17+), 1=Zoom (size to just the occupied cells). Set via Faust → Settings → Heat map.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustWorldMapGrid),               true,  "Faust World Map / Heat Map: draw a coordinate grid + axis labels behind the dots as a frame of reference (no asset needed).");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustWorldMapImage),              true,  "Faust World Map / Heat Map: draw the drop-in world-map image (config/Raphael/worldmap.png) behind the dots when present. Extract it from the game assets; calibrate with the bounds below.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(FaustWorldMapOpacity),            0.55f, "Faust World Map / Heat Map underlay image opacity (0.0=invisible, 1.0=solid).");
        // Default calibration = the values KDPen aligned in-game against the captured Vardoran map (v0.59.2),
        // a good baseline for both the World Map and the player-position Heat Map (they share these bounds).
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustWorldMapMinX),               -2900f, "Faust map underlay calibration: the world X the LEFT edge of worldmap.png corresponds to.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustWorldMapMaxX),                180f,   "Faust map underlay calibration: the world X the RIGHT edge of worldmap.png corresponds to.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustWorldMapMinZ),               -2400f, "Faust map underlay calibration: the world Z the BOTTOM edge of worldmap.png corresponds to.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustWorldMapMaxZ),                650f,   "Faust map underlay calibration: the world Z the TOP edge of worldmap.png corresponds to.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowUrielObjectSpawnerOverlay), false, "Whether the Uriel object-spawn palette overlay (quick-build list of your unlocked objects with per-row Spawn buttons) was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowBeelzTransformOverlay),   false, "Whether the Beelz Transforms overlay (double-click a form to transform; phase/revert controls) was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowChatWindowOverlay),       false, "Whether the standalone tabbed chat window (Game UI) was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatShowTimestamps),          true,  "Show the [HH:mm] timestamp on each line in the tabbed chat window.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatShowChannelTags),         true,  "Show the channel label ([G]/[L]/[Clan]/[Sys]/[W]) on each line in the tabbed chat window.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(HideNativeChat),              false, "Replace the game's default chat with the tabbed chat window: hide the native chat (invisible + non-interactive) while the tabbed window is open. Default off. The native chat returns when the tabbed window is closed.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(AggressiveNativeChatHide),    false, "EXPERIMENTAL. If the game's native chat still FLICKERS through behind Raphael's chat, turn this on: it hides the ENTIRE native chat window (not just the message list) every frame, which covers the element that flickers. Alpha-only, so it keeps the same freeze-safety as the normal hide. Only takes effect while 'Hide native chat' is also on. Default off — enable only if you see the flicker.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatAllTabDefaultGlobal),     false, "On the tabbed chat's All tab, send typed messages to Global (true) or Local (false) by default. Default Local.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatTextScale),               1.0f,  "Font size multiplier for the tabbed chat window ONLY — independent of 'Overlay text size'. (Small=0.85, Standard=1.0, Large=1.2, X-Large=1.5.) Lets you enlarge chat text without enlarging the XP / Familiar / etc. overlays. Changes apply immediately.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatNewestAtBottom),          true,  "Tabbed chat: show the newest message at the BOTTOM (true, like the game's own chat) or at the TOP (false).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatAutoScroll),              true,  "Tabbed chat: automatically scroll to keep the newest message in view as new lines arrive (to the bottom or top per the 'newest at bottom' setting). Turn off to scroll back through history freely without being snapped to the newest line.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatAutoHideEnabled),         false, "Tabbed chat: fade the chat window out when it's been idle (like the game's native chat), and fade it back on activity. Default OFF. The fade is visual only — it never closes the window or changes its saved visibility. What counts as activity is configurable (below) so it won't blink from Raphael's own background Bloodcraft pings.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatAutoHideSeconds),         12f,   "Tabbed chat: seconds of inactivity before the chat window fades out when auto-hide is on (3–120). Default 12.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatAutoHideWakeOnMessage),   true,  "Tabbed chat (auto-hide on): a new PLAYER chat message (Global / Local / Clan / Whisper) fades the window back in. Default ON. Hovering the window or opening chat always wakes it regardless.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatAutoHideWakeOnSystem),    false, "Tabbed chat (auto-hide on): SYSTEM-channel lines (server broadcasts AND Raphael's regular Bloodcraft handshake pings) fade the window back in. Default OFF — leaving this off is what prevents the window from blinking in and out on a chatty/pinging server. Turn on only if you want every system line to surface the window.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatScrollArrowButtons),      true,  "Tabbed chat: show clickable ▲/▼ scroll arrows at the top and bottom of the chat scrollbar. Each click nudges the history up/down a few lines — handy when the scrollbar gets tiny on a big buffer. Default on. Set via Game UI → chat → Scrolling.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatScrollPageKeys),          true,  "Tabbed chat: let PageUp / PageDown scroll the chat history (a page at a time) while the chat window is open. Default on (these keys rarely clash with gameplay). Set via Game UI → chat → Scrolling.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatScrollArrowKeys),         false, "Tabbed chat: let the Up / Down arrow keys scroll the chat history while the chat window is open. Default OFF because the arrow keys are commonly used for gameplay — turn on only if you don't bind them in-game. Set via Game UI → chat → Scrolling.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatChannelLabelsSpelledOut), false, "Tabbed chat: spell out channel labels in full ([Global] / [Local] / [Clan] / [System] / [Whisper]) instead of the short acronyms ([G] / [L] / [Clan] / [Sys] / [W]). Only applies when 'Show channel labels' is on.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatColorTabs),                true,  "Tabbed chat: tint each channel tab's label in that channel's color (Global / Local / Clan / System / Whispers). The All tab stays neutral. Off = all tab labels use the default text color.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatTabularLayout),             false, "Tabbed chat: render messages in aligned COLUMNS (time | channel + sender | message) with wrapped lines hanging-indented under the message column — cleaner for the mixed All tab. Off by default (free-flowing text). Best with timestamps + channel labels on.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatTabularSeparateChannelName), true,  "Tabbed chat (tabular layout only): put the channel label and the player name in SEPARATE columns (time | channel | name | message). Turn off to combine them into one column (time | channel+name | message). Default on.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatTabularAutoFitColumns),      true,  "Tabbed chat (tabular layout only): auto-fit the name column to the longest visible name so there's no dead space; the MESSAGE column always absorbs the extra width when you widen the window (it grows first, not every column). Turn off to LOCK the name column at a fixed width instead of fitting to content (the message column still gets the extra width). Default on.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatDoubleClickNameWhisper),    true,  "Tabbed chat: double-click a player's name in the chat log to start a whisper to them (jumps to the All tab with that whisper selected and focuses the input). Default on.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatShowWhisperRecipient),      true,  "Tabbed chat: on whispers YOU send, show who you sent it to (received whispers already show the sender). Without this a sent whisper just shows your own name, so the 'All Whispers' view gives no hint of the recipient. Default on.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatWhisperRecipientInChannelColumn), true, "Tabbed chat: when showing the whisper recipient (above), put it in the CHANNEL column ('[Whisper → Name]'). Turn off to put it in the NAME column instead ('→ Name'). Default on (channel column).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatSelfWhisperAsNoteToSelf),   true,  "Tabbed chat: a whisper to YOURSELF shows as 'Note to self' (default, like the base game). Turn off to show it as a normal whisper to your own name instead.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowMissingElementHints),       true,  "Show a short 'you're missing this — free power' hint on the Class / Weapon Expertise / Blood Legacy pages and overlays when you haven't set that element up yet (no class chosen, or no expertise/legacy bonus stats picked). Helpful for new players; turn off if you intentionally skip a system. Only shown for systems your server has enabled.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatGlobalColorHex),           DEFAULT_CHAT_GLOBAL_HEX, "Tabbed chat: color for the Global channel — used for its [G]/[Global] label tag AND its tab when 'Color tabs by channel' is on. Hex string (e.g. #FF8A5B coral default, #FFFFFF white, #66CCFF blue).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatLocalColorHex),            DEFAULT_CHAT_LOCAL_HEX,   "Tabbed chat: color for the Local channel — its label tag + tab. Hex string (default #B0E0FF blue).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatClanColorHex),             DEFAULT_CHAT_CLAN_HEX,    "Tabbed chat: color for the Clan channel — its label tag + tab. Hex string (default #90EE90 green).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatSystemColorHex),           DEFAULT_CHAT_SYSTEM_HEX,  "Tabbed chat: color for System messages — their label tag + tab. Hex string (default #FFD700 gold).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatWhisperColorHex),          DEFAULT_CHAT_WHISPER_HEX, "Tabbed chat: color for Whispers — their label tag + tab. Hex string (default #FF9CEF pink).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatColorMessageByChannel),     false,                    "Tabbed chat: tint each message's BODY text by its channel color (not just the [tag]/tab). Default off.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatColorOwnMessages),          false,                    "Tabbed chat: show YOUR OWN messages' body text in a distinct color (ChatOwnMessageColorHex) on every tab, so you can pick out your own text. Default off.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatOwnMessageColorHex),        DEFAULT_CHAT_OWN_HEX,     "Tabbed chat: color for your own messages when 'highlight my own messages' is on. Hex string (default #FFE08A gold).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatWindowBackgroundColorHex), DEFAULT_PANEL_BG_HEX, "Tabbed chat window background theme color (independent of the main panel color). Hex string; same presets as Settings → Display panel color. Default #121212 near-black. Transparency is set separately by the chat window transparency control.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(AllTabShowGlobal),             true,  "Tabbed chat: include GLOBAL messages in the consolidated 'All' tab. Uncheck to hide Global from All (its own Global tab is unaffected).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(AllTabShowLocal),              true,  "Tabbed chat: include LOCAL messages in the 'All' tab. Uncheck to hide Local from All (its own Local tab is unaffected).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(AllTabShowClan),               true,  "Tabbed chat: include CLAN messages in the 'All' tab. Uncheck to hide Clan from All (its own Clan tab is unaffected).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(AllTabShowSystem),             true,  "Tabbed chat: include SYSTEM messages in the 'All' tab. Uncheck to hide system/server messages from All (its own System tab is unaffected).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(AllTabShowWhisper),            true,  "Tabbed chat: include WHISPERS in the 'All' tab. Uncheck to hide whispers from All (the Whispers tab is unaffected).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(AllTabExcludeNotesToSelf),     false, "Tabbed chat: hide NOTES TO SELF (whispers to your own character) from the 'All' tab — for players who route self-notes to the secondary window. The Whispers tab still shows them.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowSecondaryChatOverlay),     false, "Whether the secondary VIEW-ONLY chat window was open at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(SecondaryChatShowGlobal),       false, "Secondary chat window: show GLOBAL messages.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(SecondaryChatShowLocal),        false, "Secondary chat window: show LOCAL messages.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(SecondaryChatShowClan),         true,  "Secondary chat window: show CLAN messages. Default ON.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(SecondaryChatShowSystem),       true,  "Secondary chat window: show SYSTEM / server messages. Default ON.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(SecondaryChatShowWhisper),      false, "Secondary chat window: show WHISPERS.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(SecondaryChatShowNotesToSelf),  false, "Secondary chat window: show NOTES TO SELF (whispers to your own character) — independent of the Whisper toggle.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ExtendedChatCaptureEnabled),    true,  "Tabbed chat: keep a DURABLE history of chat (the server drops old lines quickly; this is what lets the standalone window retain them). Turn OFF to stop retaining chat entirely — lowest overhead. The per-channel switches below let you keep only the channels you want. Default ON.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ExtendedCaptureGlobal),         true,  "Tabbed chat: retain GLOBAL messages in the durable history. Off = Global is not kept (it still shows transiently in the game's native chat).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ExtendedCaptureLocal),          true,  "Tabbed chat: retain LOCAL / region messages in the durable history.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ExtendedCaptureClan),           true,  "Tabbed chat: retain CLAN messages in the durable history.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ExtendedCaptureSystem),         true,  "Tabbed chat: retain SYSTEM / server messages in the durable history. Turn this OFF if a server mod floods system chat and you only want to keep player conversation. Default ON.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ExtendedCaptureWhisper),        true,  "Tabbed chat: retain WHISPERS (including notes to self) in the durable history.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ExtendedCaptureOther),          true,  "Tabbed chat: retain OTHER / uncategorized messages in the durable history.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatTabHotkeysEnabled),        true,  "Tabbed chat: switch tabs with hotkeys (a modifier + number 1-6) while the chat window is open and you are NOT typing in it. Tab order: 1=All, 2=Global, 3=Local, 4=Clan, 5=System, 6=Whispers.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatTabHotkeyModifier),        "Alt",   "Tabbed chat: modifier held with number keys 1-6 to switch tabs. One of: Shift, Ctrl, Alt, None. Default Alt (so Alt+1..6) — Shift+number is the game's consumable bar and Ctrl pops the action wheel, so Alt is the safest free default. Change it here if Alt also conflicts for you.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShiftSpellOverlayShowDiagnostics), false, "Show the small italic 'pf/cg/si/end/srv' debug line under the Shift overlay's SHIFT label. Off by default; flip on if you need to debug why the cooldown isn't updating.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowShiftSpellIcon),               true,  "Show the slotted spell's actual icon on the Shift-spell overlay tile (like Eclipse). When off, the overlay shows the plain colored cooldown tile instead.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(OverlaysBehindGameMenus),          true,  "When an in-game menu (inventory, character sheet, map, etc.) is open, drop Raphael's overlays/panels BEHIND it instead of floating over the top. Set false to keep them always on top (the pre-0.16 behavior).");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(EnableCustomRecipes),               false, "Show Bloodcraft's custom crafting recipes (vampiric dust, copper wires, soul-shard extraction, primal jewel, etc.) in the in-game crafting stations when the server has them enabled. Client-side display only; automatically skipped if the Eclipse mod is installed (it applies them itself). DEFAULT OFF as of 0.16.1: applying these recipes does a burst of ECS structural changes at login that can trigger a rare, non-deterministic Il2CppInterop GC crash on some machines. Turn ON to opt in; when on, application is deferred to a quiet frame a few seconds after login to minimize that risk.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(SuppressGameInputWhileUIOpen),       false, "Stop your character moving / attacking / casting (incl. hotkeyed commands) while the Raphael main panel is open, so background actions don't fire while you click buttons or type into forms. Default OFF — enable to try it. Blanks your input data AFTER the game reads it (never blocks the input system), so it cannot freeze the UI like the removed 0.1.x attempt did.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(LockKeyboardInFormFields),           true,  "Lock the game keyboard while you're typing in a Raphael form field (search/name/admin boxes), exactly like the chat window already does: your character won't move, attack, or cast, menus won't open, and your bound hotkeys (incl. ability keybinds) won't fire — so a keystroke meant for the text box can never trigger a game action. Default ON. Reads each field's real focus every frame (can't get stuck) and Escape always frees the keyboard. Turn OFF to restore the old behavior (only the chat window locks input).");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(BlockInputWhenPointerOverUI),         false, "Don't fire your primary attack / spell cast when you LEFT-CLICK while the cursor is over any Raphael panel or overlay (the chat window is always covered regardless). Stops a click on a button/overlay from leaking into the world as an attack or a stuck cast. Default OFF — leave off if you want to keep casting with the cursor parked over an overlay. Only suppresses the attack/cast (never movement, never menus), so it can't freeze the game.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(SuppressConsoleKeybindsWhileTyping),  true,  "ADMINS: stop console keybindings (hotkeys assigned with the console's 'keybinding create' command) from firing while you type in a Raphael field or have the main panel open (panel case requires SuppressGameInputWhileUIOpen). The game reads these binds outside its normal input pipeline, so even the native chat's typing lock can't block them — Raphael disables them the same way the game's own UI text fields do, and the game automatically re-enables them the moment you stop typing. Default ON; harmless for non-admins (the binds only execute on console-enabled sessions).");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsPanelAutoResizeEnabled),    true,  "Auto-resize the main panel vertically to fit the active tab's content (capped at 90% of screen height).");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(LeftRailAccordion),           true,  "Left-rail accordion: opening one tab group (Bloodcraft / Beelzebub / Kindred / Uriel / Faust / Settings & Help) collapses the others, keeping the rail short on small screens. Default ON; turn off to keep multiple groups expanded at once.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(UITextScale),                 1.0f,  "Font scale multiplier for the main panel (Small=0.85, Standard=1.0, Large=1.2, X-Large=1.5). Changes apply when the panel is closed and reopened.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(OverlayTextScale),            1.0f,  "Font scale multiplier for the secondary overlays (Small=0.85, Standard=1.0, Large=1.2, X-Large=1.5). Changes apply when each overlay is toggled off and back on.");

        // Per-overlay background transparency. User semantics:
        //   0.0 = solid (opaque), 1.0 = invisible. Floored at 0.95 internally
        //   so the panel chrome / drag handle stays visible at "100%".
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(XPOverlayTransparency),       0.4f,  "XP overlay background transparency (0.0=solid, 1.0=invisible). Floor at 0.95 keeps drag handle visible.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(FamiliarOverlayTransparency), 0.4f,  "Familiar overlay background transparency.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(FamiliarBrowserTransparency), 0.4f,  "Familiar Browser overlay background transparency.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(FamiliarQuickSpawnOverlayTransparency), 0.4f, "Familiar Quick Spawn overlay background transparency (0.0=solid, 1.0=invisible).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(FaustBossTrackerOverlayTransparency), 0.4f, "Faust boss-tracker overlay background transparency (0.0=solid, 1.0=invisible).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(DailyQuestTransparency),      0.4f,  "Daily quest overlay background transparency.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ProfessionOverlayTransparency), 0.4f, "Profession overlay background transparency.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShiftSpellOverlayTransparency), 0.4f, "Shift-spell cooldown overlay background transparency (0.0=solid, 1.0=invisible).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(QuickActionsOverlayTransparency), 0.4f, "Quick Actions overlay background transparency (0.0=solid, 1.0=invisible).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(BeelzActionBarOverlayTransparency), 0.4f, "Beelz Action Bar overlay background transparency (0.0=solid, 1.0=invisible).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(BeelzSummonsOverlayTransparency), 0.4f, "Beelz Summons overlay background transparency (0.0=solid, 1.0=invisible).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(UrielSharedOverlayTransparency), 0.4f, "Uriel Nearby Public Storage overlay background transparency (0.0=solid, 1.0=invisible).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(UrielObjectSpawnerOverlayTransparency), 0.4f, "Uriel object-spawn palette overlay background transparency (0.0=solid, 1.0=invisible).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(BeelzTransformOverlayTransparency), 0.4f, "Beelz Transforms overlay background transparency (0.0=solid, 1.0=invisible).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ChatWindowOverlayTransparency), 0.3f, "Tabbed chat window background transparency (0.0=solid, 1.0=invisible).");
        // 0.14.0: combined overlay registration.
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowCombinedOverlay),         false, "Show the combined overlay — one panel with XP / Familiar / Weapon / Blood / Professions / Quests sections. When on, the individual info overlays auto-hide. Toggle in Settings → Display.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(CombinedOverlayShowXP),        true,  "Combined overlay: include the XP / Experience section.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(CombinedOverlayShowFamiliar),  true,  "Combined overlay: include the active-familiar section.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(CombinedOverlayShowExpertise), true,  "Combined overlay: include the weapon expertise section.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(CombinedOverlayShowLegacy),    true,  "Combined overlay: include the blood legacy section.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(CombinedOverlayShowProfessions), true,"Combined overlay: include the professions section (per-profession checkboxes in 'Professions tracked' still apply within the section).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(CombinedOverlayShowQuests),    true,  "Combined overlay: include the Daily + Weekly quest section.");
        // 0.14.0 friend-test v2: per-system progress bars apply to BOTH the
        // standalone overlays AND the combined overlay so toggling one of
        // these flags has consistent effect regardless of which overlay
        // mode the user is in. Replaces the old CombinedOverlayShow*Bar
        // settings (those entries are inert — left out of the bundled cfg).
        InitConfigEntry(UI_SETTINGS_GROUP, nameof(ShowProgressBarXP),          true, "Show XP progress bar (applies to both the standalone XP overlay and the XP section of the combined overlay).");
        InitConfigEntry(UI_SETTINGS_GROUP, nameof(ShowProgressBarFamiliar),    true, "Show Familiar progress bar (standalone Familiar overlay + combined Familiar section).");
        InitConfigEntry(UI_SETTINGS_GROUP, nameof(ShowProgressBarExpertise),   true, "Show Weapon Expertise progress bar (Weapon row of standalone XP overlay + Weapon section of combined overlay).");
        InitConfigEntry(UI_SETTINGS_GROUP, nameof(ShowProgressBarLegacy),      true, "Show Blood Legacy progress bar (Blood row of standalone XP overlay + Blood section of combined overlay).");
        InitConfigEntry(UI_SETTINGS_GROUP, nameof(ShowProgressBarProfessions), true, "Show Professions progress bars (8 per-profession bars on standalone Profession overlay + per-profession rows in combined Professions section when bars are on).");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(CombinedOverlayTransparency),  0.4f,  "Combined overlay background transparency (0.0=solid, 1.0=invisible).");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(BloodcraftAvailability),      "Auto", "Whether the server has the Bloodcraft mod. Auto = present iff the server ACK'd our Eclipse handshake. On = always assume present. Off = always disable the BLOODCRAFT tab group.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(KindredAvailability),         "Auto", "Whether the server has the Kindred suite (KindredCommands + KindredLogistics). No protocol probe is wired yet, so Auto currently means 'assume present'. Set to Off explicitly if your server doesn't have these mods to grey out the KINDRED tab group.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(BeelzebubAvailability),       "Auto", "Whether the server has the Beelzebub mod (ability capture/transform). Auto = present iff the server answered our `.beelz api version` handshake. On = always assume present. Off = always disable the BEELZEBUB tab group. Most servers don't have Beelzebub, so Auto stays disabled there.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(BeelzDiagnostics),            false,  "Beelzebub diagnostic details (default off). ON: the Loadout tables show each ability's ID (PrefabGUID) + raw prefab name, and Raphael writes a verbose [Beelz][diag] wire trace (commands sent + raw [BEELZ:*] replies) to the BepInEx LogOutput.log so testers/admins can report exactly which abilities work or need tuning. Toggle it from the Beelzebub → Settings tab; also implied while the global DiagnosticMode is active.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(UrielAvailability),           "Auto", "Whether the server has the Uriel mod (storage sharing / public prisons / stair swap / object spawning). Auto = present iff the server answered our `.uriel api version` handshake. On = always assume present. Off = always disable the URIEL tab group. Most servers don't have Uriel, so Auto stays disabled there.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(UrielDiagnostics),            false,  "Uriel diagnostic details (default off). ON: Raphael writes a verbose [Uriel][diag] wire trace (commands sent + raw [URIEL:*] replies) to the BepInEx LogOutput.log so testers/admins can report exactly which Uriel commands fired and what came back. Toggle it from the Uriel → Settings tab; also implied while the global DiagnosticMode is active.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustAvailability),           "Auto", "Whether the server has the Faust mod (investigation/information: castle/plot/player/server queries). Auto = present iff the server answered our `.faust api version` handshake. On = always assume present. Off = always disable the FAUST tab group. Most servers don't have Faust, so Auto stays disabled there.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustDiagnostics),            false,  "Faust diagnostic details (default off). ON: Raphael writes a verbose [Faust][diag] wire trace (commands sent + raw [FAUST:*] replies) to the BepInEx LogOutput.log so testers/admins can report exactly which Faust queries fired and what came back. Toggle it from the Faust → Settings tab; also implied while the global DiagnosticMode is active.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(NyarAvailability),            "Auto", "Whether the server has the Nyarlathotep mod (admin-run world events: waves, empowered NPCs, boss adds, defended zones, sieges, stats). Auto = present iff the server answered our `.nyar api version` handshake. On = always assume present. Off = always disable the NYARLATHOTEP tab group.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(NyarDiagnostics),             false,  "Nyarlathotep diagnostic details (default off). ON: Raphael writes a verbose [Nyar][diag] wire trace (commands sent + raw [NYAR:*] replies) to the BepInEx LogOutput.log. Toggle it from the Nyarlathotep → Settings tab; also implied while the global Diagnostic mode is on.");
        foreach (var (key, mod) in new[] { (nameof(BeelzShowProtocolInChat), "Beelzebub"), (nameof(UrielShowProtocolInChat), "Uriel"),
                                           (nameof(FaustShowProtocolInChat), "Faust"),     (nameof(NyarShowProtocolInChat),  "Nyarlathotep") })
            InitConfigEntry(GENERAL_SETTINGS_GROUP, key, false, $"Show {mod}'s machine-readable protocol lines in chat instead of removing them after Raphael reads them (default off; Raphael still parses them). Replies to a command you sent to {mod} yourself show for a few seconds even when this is off. Settings and Help → Chat & Diagnostics.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FaustDurationStyle),          0,      "How Faust writes long durations (castle decay timers in the Castle Info / All Plots tabs). 0 = Auto (the two largest non-zero units, e.g. '3w 2d' or '6h 30m' — no mental math); 1 = Hours & minutes (legacy, e.g. '734h 30m'); 2 = Days, hours, minutes; 3 = Weeks, days, hours, minutes. Cycle it from the Faust → Settings tab.");

        // 0.15.0: opt-in keyboard hotkeys for the floating Raphael / OV button
        // actions. Both default to KeyboardShortcut.Empty (no binding) so
        // first-time users keep the v0.14-and-earlier mouse-only entry
        // point. Bind via Settings → Display → Hotkeys (click-to-bind UI)
        // or edit this .cfg directly with values like "Insert", "F3",
        // "Ctrl+H", "Shift+F5", etc.
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(HotkeyToggleMainPanel),  string.Empty,
            "Hotkey to open / close the main Raphael panel. Empty by default — bind via Settings → Display → Hotkeys, or set here directly. Format: a single key name (e.g. 'Insert', 'F3') OR a modifier-prefixed combo joined with '+' (e.g. 'LeftControl+H', 'Ctrl+H', 'Shift+F5'). Aliases accepted for modifiers: ctrl, alt, shift, win.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(HotkeyToggleAllOverlays), string.Empty,
            "Hotkey to show / hide all overlays at once (master overlay toggle). Empty by default — same format and bind-via-Settings UI as the main panel hotkey.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(UrielBuildMoveKey),   string.Empty,
            "Uriel BUILD-MODE hotkey: move the nearest spawned object to your cursor (.uriel move). Only fires while Build Mode is ON (Uriel → Object Spawning → Building hotkeys); Build Mode resets OFF every login. Empty by default. Same key format as the other hotkeys.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(UrielBuildRotateKey), string.Empty,
            "Uriel BUILD-MODE hotkey: rotate the nearest spawned object (.uriel rotate). Only fires while Build Mode is ON. Empty by default.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(UrielBuildRemoveKey), string.Empty,
            "Uriel BUILD-MODE hotkey: remove (despawn) the nearest spawned object (.uriel despawn). Only fires while Build Mode is ON. Empty by default.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, "BeelzHotkeyBinds", string.Empty,
            "Optional keyboard shortcuts for Beelzebub action-bar abilities (press the key → casts the ability via .beelz cast). Managed from the Beelzebub → Hotkeys tab. Format: semicolon-separated 'name=combo' pairs, e.g. 'Bolt=F1;Heal=LeftControl+F2'.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(DiagnosticMode),         "Off",
            "Diagnostic mode: emit detailed [DIAG]-prefixed trace logs to BepInEx for UI clicks, overlay toggles, protocol state changes, and hotkey fires. Valid persistent values: 'Off' or 'Always'. Use Settings → Display → Hotkeys & diagnostics to also pick 'Session' (this run only — resets to Off on game restart). Cheap when off (one bool check + early return per call site).");

        // 0.17.2: compatibility / crash-bisect switches. Leave these ON unless you
        // are diagnosing the intermittent 0.16.x load crash (see docs/LESSONS_LEARNED).
        InitConfigEntry(COMPAT_SETTINGS_GROUP, nameof(EnableChatSystemHooks),         true,
            "ON (default): Raphael patches the chat systems for inbound command-reply parsing AND the tabbed chat window. OFF: those chat patches are NOT applied — the tabbed chat window and command-reply parsing stop working. Turn OFF only to test whether Raphael's chat patches contribute to the intermittent 0.16.x load crash.");
        InitConfigEntry(COMPAT_SETTINGS_GROUP, nameof(EnableInputSuppressionPatches), true,
            "ON (default): Raphael patches the input/menu systems so typing in a Raphael form or chat window suppresses character movement, abilities, and menu hotkeys. OFF: those input patches are NOT applied — your character may act on keystrokes while you type. Turn OFF only to test whether these patches contribute to the intermittent 0.16.x load crash.");
        InitConfigEntry(COMPAT_SETTINGS_GROUP, nameof(EnableNativeTypingLock),        true,
            "ON (default): while you type in a Raphael text field, Raphael registers an input consumer in V Rising's own input stack (the same mechanism the native chat uses) so game keybinds — menus, abilities, action bar, emotes, admin — are consumed at the source and can never fire mid-typing. OFF: fall back to the older, partial protections only. Turn OFF only if you suspect this new lock of causing a problem.");
        InitConfigEntry(COMPAT_SETTINGS_GROUP, nameof(EnableOverlayLayeringPatch),    true,
            "ON (default): Raphael patches UICanvasSystem so overlays can render BEHIND in-game menus (the 'Overlays behind game menus' feature). OFF: that patch is NOT applied — overlays always render on top. Turn OFF only to test whether this patch contributes to the intermittent 0.16.x load crash.");
        InitConfigEntry(COMPAT_SETTINGS_GROUP, nameof(UiBuildDelaySeconds),           3,
            "Seconds to wait after you spawn into the world before Raphael restores its overlays and starts the V-Blood scanner. Building them the instant you spawn piled work into the fragile login window; deferring moves it onto a quiet frame, which can avoid the intermittent 0.16.x load crash on some setups. Clamped 0..30. 0 = restore immediately (old behavior). Default 3.");

        return this;
    }

    private static ConfigEntry<T> InitConfigEntry<T>(string section, string key, T defaultValue, string description)
    {
        var entry = Plugin.Instance.Config.Bind(section, key, defaultValue, description);

        // Honor any value the user already set in the .cfg on disk.
        var cfgFile = Path.Combine(Paths.ConfigPath, $"{MyPluginInfo.PLUGIN_GUID}.cfg");
        if (File.Exists(cfgFile))
        {
            var config = new ConfigFile(cfgFile, true);
            if (config.TryGetEntry(section, key, out ConfigEntry<T> existing))
                entry.Value = existing.Value;
        }

        ConfigEntries[key] = entry;
        return entry;
    }
}
