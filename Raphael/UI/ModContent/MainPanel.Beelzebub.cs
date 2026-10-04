using System;
using System.Collections.Generic;
using Raphael.Services.Beelzebub;
using Raphael.Utils;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.UI.ModContent.Data;
using TMPro;
using UnityEngine;

namespace Raphael.UI.ModContent;

// 0.18: Beelzebub tab group (client UI for the server-side ability-capture/transform mod).
// Phase A ships the Bestiary collection tracker (the headline, mirroring the
// AllFamiliars / V-Bloods tabular pattern) + the action-bar overlay. Loadout,
// Hotkeys and Transforms render their live state read-only here with a Refresh,
// and gain full editing in Phase B/C. Admin tabs follow the established Raphael model
// (always visible + a "requires admin" note; the server enforces permissions).
//
// All data comes from BeelzState (populated by BeelzProtocolService from the
// [BEELZ:*] wire stream). The UI never parses raw lines; it binds to BeelzState
// and re-renders on its change events.
public partial class MainPanel
{
    // ---- shared subscription guard for every Beelzebub tab ----
    private bool _beelzSubscribed;

    // ---- Bestiary tab state (full per-ability collector checklist from the catalog scan) ----
    private enum BeelzBestiaryFilter { All, Captured, Missing }
    private BeelzBestiaryFilter _beelzBestiaryFilter = BeelzBestiaryFilter.All;
    private string _beelzBestiarySearch = "";
    private string _beelzBestiaryCategory = "";          // "" = all; else a cat= value
    private BeelzAbilKind _beelzBestiaryKind = BeelzAbilKind.All;
    private GameObject      _beelzBestiaryRowContainer;
    private TextMeshProUGUI _beelzBestiaryStatsLabel;
    private ButtonRef       _beelzBestiaryFilterButton;
    private ButtonRef       _beelzBestiaryCategoryButton;
    private ButtonRef       _beelzBestiaryKindButton;
    private ButtonRef       _beelzBestiaryGroupButton;    // #4: group-by cycle (None/Category/Kind/Unit)
    private ButtonRef       _beelzBestiaryPrevButton;     // #5: pagination
    private ButtonRef       _beelzBestiaryNextButton;
    private TextMeshProUGUI _beelzBestiaryPageLabel;
    private InputFieldRef   _beelzBestiarySearchInput;
    private TextMeshProUGUI _beelzBestiaryScanStatus;     // "Scan all" progress / done line
    private TextMeshProUGUI _beelzAbilTableScanStatus;    // "Scan all" status on the Admin: Abilities tab (own field; was sharing the Bestiary one → stale)
    private TextMeshProUGUI _beelzAbilExportStatus;       // "Copy config → clipboard" feedback line
    private string _beelzBestiaryGroupMode = "";          // ""|Category|Kind|Unit|Status
    private int    _beelzBestiaryPage;                     // 0-based; #5 pagination
    private readonly HashSet<string> _beelzBestiaryCollapsed = new(StringComparer.OrdinalIgnoreCase); // F12: collapsed group keys
    private readonly List<string>    _beelzBestiaryLastGroupKeys = new();                              // F12: groups in the last render (for collapse-all)
    // Tab-switch freeze fix: only rebuild the heavy lists on tab-show when their data changed since the
    // last build (the rows persist while hidden). Set true by data-change events; cleared after a build.
    private bool _beelzLoadoutAssignDirty = true;
    private bool _beelzBestiaryDirty = true;
    // F13: mass ability-config tab state.
    private GameObject      _beelzAbilTableContainer;
    private TextMeshProUGUI _beelzAbilTablePageLabel;
    private ButtonRef       _beelzAbilTableCategoryButton;
    private string _beelzAbilTableSearch = "";
    private string _beelzAbilTableCategory = "";   // "" = all
    private int    _beelzAbilTablePage;
    private int    _beelzAbilTableEnabled;         // A7: 0=all, 1=enabled only, 2=disabled only
    // api25/26 client-side filters for the admin abilities table — they filter the already-scanned
    // abilities-all rows in memory (NO re-scan). 0 = All; the cycle order lives in the Format/Cycle
    // helpers + the static value arrays. Buttons are only shown when the server emits the data.
    private int    _beelzAbilTableReview;          // 0=all,1=Unreviewed,2=Reviewed,3=Approved,4=Blocked,5=Hidden
    private int    _beelzAbilTableTier;            // 0=all,1=T1,2=T2,3=T3,4=T4,5=VBlood
    private ButtonRef _beelzAbilTableReviewButton;
    private ButtonRef _beelzAbilTableTierButton;
    private GameObject _beelzAbilReviewHeaderGo;   // Review column header — built always, shown per gate
    private TextMeshProUGUI _beelzBestiaryCurationNote;  // (d) curation note — built always, shown per gate
    // Preset quick-scan rows (one per scan target) — built always, shown only when the server supports
    // catalog filters (toggled in UpdateBeelzScanStatusLabels, which runs on handshake + scan).
    private readonly List<GameObject> _beelzPresetRows = new();
    private static readonly string[] BeelzReviewFilterValues = { "", "Unreviewed", "Reviewed", "Approved", "Blocked", "Hidden" };
    private static readonly string[] BeelzTierFilterValues   = { "", "T1", "T2", "T3", "T4", "VBlood" };
    private readonly HashSet<string> _beelzAbilTableExpanded = new(StringComparer.OrdinalIgnoreCase); // A6: rows with the full sub-form open
    private const int BEELZ_ABIL_TABLE_PAGE = 50;
    // A6: the full per-ability field set for the expandable sub-form (token, label, kind).
    private static readonly (string Field, string Label, string Kind)[] BeelzAbilFullFields =
    {
        ("enabled", "Enabled", "bool"), ("cooldown", "Cooldown (s)", "num"), ("range", "Range", "num"),
        ("charges", "Charges", "num"), ("chargetime", "Charge time (s)", "num"), ("aoe", "AoE radius", "num"),
        ("projspeed", "Projectile speed", "num"), ("duration", "Effect duration (s)", "num"),
        ("healing", "Healing ×", "num"), ("forcetimeout", "Force timeout (s)", "num"),
        ("freelymove", "Free-move (s)", "num"), ("interruptonhit", "Interrupt on hit", "tri"),
        ("interruptible", "Interruptible", "tri"), ("freemove", "Free move", "onoff"), ("castspeed", "Cast speed (0-1)", "num"),
        ("powerwindow", "Power window (s)", "num"), ("leapheight", "Leap height", "num"),
        ("maxstacks", "Max stacks", "num"), ("projcount", "Projectile count", "num"), ("knockback", "Knockback ×", "num"),
        ("lifetime", "Lifetime (s)", "num"), ("casttime", "Cast time ×", "num"),
        ("summoncap", "Summon stacks", "num"), ("summontimeout", "Summon timeout (s)", "num"), ("summonunits", "Summon units", "num"),
        ("damagescale", "Damage ×", "num"), ("cooldownscale", "Cooldown ×", "num"),
        ("weapons", "Weapons", "text"), ("forms", "Forms", "text"), ("notes", "Notes", "text"),
    };
    // Per-field help, sourced from Beelzebub's ABILITY_CONFIG.md + CHANGELOG (read-only reference).
    // Shown as a tooltip on each sub-form row AND on the collapsed-row cells where they apply.
    private static readonly Dictionary<string, string> BeelzAbilFieldTooltips = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enabled"]        = "Hard kill-switch. When false the ability can't be captured OR used by anyone — including the source NPC/boss.",
        ["cooldown"]       = "Exact cooldown in seconds (overrides the baked value). Blank = baseline; type a number, or 'clear' to remove. Server-wide — also changes the source NPC's cast.",
        ["range"]          = "Maximum cast range/distance (overrides baked). Blank = baseline; 'clear' removes the override.",
        ["charges"]        = "Number of charges for charge-based abilities (AbilityChargesData on the group).",
        ["chargetime"]     = "Seconds to regenerate one charge.",
        ["aoe"]            = "Area-of-effect radius of the ability's spawned AoE.",
        ["projspeed"]      = "Projectile travel speed. A few oddly-nested projectiles won't respond — verify in-game.",
        ["duration"]       = "Absolute duration (seconds) of the buffs/debuffs the ability applies.",
        ["healing"]        = "Healing multiplier (1.0 = unchanged) for the ability's healing effect.",
        ["forcetimeout"]   = "Force the ability's spawned effects/buffs to auto-expire after N seconds, even if they had no built-in duration. 0 or 'clear' removes it. (Use 'duration' for effects that already have a length.)",
        ["freelymove"]     = "Frees you to move N seconds into a cast/channel that would otherwise root you (also lifts a channel's move-lock). 0 = move immediately; 'clear' restores the lock.",
        ["interruptonhit"] = "Whether taking a hit interrupts this ability's cast. on / off; 'auto' = the baked default.",
        ["interruptible"]  = "Whether a dash/shield can cancel the cast. on / off; 'auto' = the baked default.",
        ["freemove"]       = "Lets the player move the instant the cast FINISHES (on/off). (Distinct from Free-move seconds, which frees you DURING a channel.)",
        ["castspeed"]      = "Movement speed during the cast: 0 = rooted, 1 = full speed. 'clear' = baked default.",
        ["powerwindow"]    = "Granted-cast power window in seconds — how long power-scaled DoT/AoE effects keep applying after the cast (Beelz v0.120+). Write-only: the server doesn't report the current value, so this shows blank; type a number to set it, or 'clear' for the default.",
        ["leapheight"]     = "Leap/travel height for a leap ability's phase buff (TravelBuff.Height) — tame a boss leap that flings the caster sky-high (vanilla ~250; try ~20-40). Beelz v0.125+; GLOBAL prefab edit (changes the source boss's leap too). Write-only: not reported back, so it shows blank; type a number, or 'clear'/'defaults' to revert.",
        ["maxstacks"]      = "How many times the ability's own buff can stack (1-255). Beelz v0.134+. The table scan doesn't report it, so it shows blank here (Admin: Players → Ability shaping → Load current settings shows the live value). 'clear' reverts.",
        ["projcount"]      = "Projectiles per volley on fan/multishot/cluster abilities (capped at 3× the ability's own count, max 16). Beelz v0.134+. Blank here = not reported on the scan; 'clear' reverts.",
        ["knockback"]      = "Knockback multiplier (distance + push time): 1.0 = unchanged, 0 = none. Beelz v0.134+. Blank here = not reported on the scan; 'clear' reverts.",
        ["lifetime"]       = "Seconds the ability's projectiles/areas last (buff length is 'Effect duration'). Beelz v0.134+. Blank here = not reported on the scan; 'clear' reverts.",
        ["casttime"]       = "EXPERIMENTAL cast-time multiplier (windup + recovery; not channels, hold-to-cast or charges). Beelz v0.134+. Blank here = not reported on the scan; 'clear' reverts.",
        ["summoncap"]      = "Summon STACKS — how many casts of this summon can be active at once (i.e. how many times you can use the summon before older ones must expire). The cast itself keeps its full unit count.",
        ["summontimeout"]  = "Seconds before summoned units expire.",
        ["summonunits"]    = "How many units a SINGLE cast summons (e.g. trim a 10-skeleton horde to fewer). 0 = the ability's natural count. Independent of Summon stacks — whichever limit is hit first applies.",
        ["damagescale"]    = "Damage multiplier for granted casts (1.0 = no change).",
        ["cooldownscale"]  = "Cooldown multiplier. Applies to .beelz cast force-casts; native spell-bar slot cooldowns stay the game's own.",
        ["weapons"]        = "Weapon-family restriction (Beelz v0.101+). Comma-separated. A plain family (e.g. 'Sword,Axe') = usable ONLY with those; a '!'-prefixed family (e.g. '!Sword') = usable everywhere EXCEPT that one. Blank = no restriction. ENFORCED: a weapon-grant of a locked ability is refused.",
        ["forms"]          = "Form restriction (Beelz v0.101+). Comma-separated. A plain form (e.g. 'Wolf,Bear') = usable ONLY in those; a '!'-prefixed form (e.g. '!Mounted') = usable everywhere EXCEPT there. Blank = no restriction. ENFORCED on form-grant.",
        ["notes"]          = "Admin annotation only — not used at runtime.",
    };
    // Slim collapsed-row column widths for the Admin: Abilities table — header and rows share these so
    // they line up. (Per-field editing moved to the expanded sub-form; the row is now an overview.)
    private const int ABILT_EXPAND_W   = 22;
    private const int ABILT_NAME_MIN   = 90,  ABILT_NAME_PREF  = 160;
    private const int ABILT_ID_MIN     = 80,  ABILT_ID_PREF    = 120;
    private const int ABILT_UNIT_MIN   = 70,  ABILT_UNIT_PREF  = 110;
    private const int ABILT_REVIEW_MIN = 84,  ABILT_REVIEW_PREF = 120;   // api25: Review (status + tag), gated
    private const int ABILT_EN_W       = 60;
    // Page size for the Bestiary (replaces the old hard 200-row cap with real paging so the full
    // 1000+ catalog is browsable without a freeze).
    private const int BEELZ_BESTIARY_PAGE = 100;

    // ---- list containers ----
    private GameObject _beelzLoadoutRowContainer;     // 6 current slot rows (+ Clear)
    private GameObject _beelzLoadoutAssignContainer;  // captured-ability → slot assignment list
    private GameObject _beelzHotkeyRowContainer;      // current hotkey bindings (+ Cast/Clear)
    private GameObject _beelzHotkeyBindContainer;     // captured-ability → bind-new-hotkey list
    private GameObject _beelzTransformRowContainer;
    private GameObject _beelzTxConfigContainer;       // per-category transform mode/duration/cooldown
    private TextMeshProUGUI _connBloodcraftReadout;   // Connection tab — Bloodcraft state line
    private TextMeshProUGUI _connBeelzReadout;        // Connection tab — Beelzebub state line
    private TextMeshProUGUI _connUrielReadout;        // Connection tab — Uriel state line (0.26)
    private TextMeshProUGUI _connFaustReadout;        // Connection tab — Faust state line (0.50)
    private TextMeshProUGUI _connRefreshReadout;      // Connection tab — "Refresh Raphael" result line (0.65.6)

    // Hotkey-bind state.
    private string _beelzHotkeyName = "";
    private string _beelzHotkeyBindSearch = "";
    private TextMeshProUGUI _beelzHotkeyStatusLabel;
    // Bind-list filters — parity with the loadout assign list (own state so they're independent).
    private BeelzAbilSource _beelzHkSource = BeelzAbilSource.All;
    private string          _beelzHkCategory = "";
    private BeelzAbilKind   _beelzHkKind = BeelzAbilKind.All;
    private BeelzGroupMode  _beelzHkGroupMode = BeelzGroupMode.None;
    private readonly HashSet<string> _beelzHkCollapsed = new();
    private readonly List<string>    _beelzHkLastGroupKeys = new();
    private ButtonRef _beelzHkSourceBtn, _beelzHkCatBtn, _beelzHkKindBtn, _beelzHkGroupBtn;

    // Loadout group selection: "" = the universal "any" set, else a weapon-family name.
    // Beelzebub's weapon-grant/weapon-unslot take an EXPLICIT family, so any group is
    // editable without wielding the weapon; the server auto-activates the matching set
    // when you swap to that weapon (empty slots fall through to your vanilla abilities).
    private string _beelzSelectedGroup = "";
    private string _beelzCopyFromGroup = "";          // source group for the "copy from" helper
    private string _beelzPresetName = "";             // server-side named-preset name input
    private bool   _beelzGroupInitialized;            // default the selector to the live weapon once
    private TMP_Dropdown _beelzGroupDropdown;
    private TMP_Dropdown _beelzCopyDropdown;
    private TextMeshProUGUI _beelzLiveBadgeLabel;     // "● live: <weapon>"
    private TextMeshProUGUI _beelzLoadoutStatusLabel; // copy / clear feedback line

    // Loadout assign-list filters (operate on the captured-ability list).
    private enum BeelzAbilSource { All, VBlood, Regular }
    private BeelzAbilSource _beelzAssignSource = BeelzAbilSource.All;
    private string _beelzAssignCategory = "";   // "" = all categories; else a cat= value (Summon/Spell/…)
    private string _beelzAssignSearch = "";
    private ButtonRef _beelzAssignSourceButton, _beelzAssignCategoryButton, _beelzAssignGroupButton, _beelzAssignKindButton;

    // Group-by mode for the assign list (single toggle; no nested sub-grouping). Each row
    // still shows the complementary columns (unit · category · kind · creature-type) so the
    // player has what they need regardless of how the list is grouped.
    private enum BeelzGroupMode { None, Unit, Category, Kind, Weapon }
    private BeelzGroupMode _beelzAssignGroupMode = BeelzGroupMode.None;

    // Kind: Weapon = weapon-bound, Form = form-restricted, else Magic. Derived from the
    // catalog scan (weapons=/forms=); unknown ("") until the user runs Scan all.
    private enum BeelzAbilKind { All, Magic, Weapon, Form }
    private BeelzAbilKind _beelzAssignKind = BeelzAbilKind.All;
    // Collapsed group keys in the grouped assign list (works for any group mode).
    private readonly HashSet<string> _beelzCollapsedGroups = new();
    // Group keys produced by the last assign rebuild — drives "Collapse all".
    private readonly List<string> _beelzLastAssignGroupKeys = new();

    // Assign-list table column widths (kept in sync between the header and each row).
    private const int BEELZ_COL_ABILITY_MIN = 90,  BEELZ_COL_ABILITY_PREF = 160;
    private const int BEELZ_COL_UNIT_MIN    = 64,  BEELZ_COL_UNIT_PREF    = 104;
    private const int BEELZ_COL_CAT_MIN     = 50,  BEELZ_COL_CAT_PREF     = 62;
    // 0.18 diagnostics: an extra "ID" column (the ability PrefabGUID) shown only when
    // Settings.BeelzDiagnostics is on. Header + rows both gate on the same flag so they
    // stay aligned; the list is rebuilt when the toggle flips (see the Settings tab).
    private const int BEELZ_COL_ID_MIN      = 74,  BEELZ_COL_ID_PREF      = 100;
    private const int BEELZ_ASSIGN_BTN_W    = 18;  // narrow bind buttons → room for the text columns
    private const int BEELZ_ASSIGN_BTN_SP   = 2;   // spacing between the bind buttons
    // Slots a Raphael loadout targets: primary (0, left-click), the 6 spell slots (1-6), and ultimate
    // (7, the T key) — primary/ultimate added by Beelz v0.91/0.94. The assign row renders one bind
    // button per entry in this order (P 1 2 3 4 5 6 U).
    private static readonly int[] BeelzSlotOrder = { 0, 1, 2, 3, 4, 5, 6, 7 };
    // The bind buttons live in ONE fixed-width sub-group so a row has the same columns
    // (Ability | Unit | Cat | Slots) as the header — otherwise the extra per-button gaps
    // shift the data columns out of line with the header. Width = 8 buttons + 7 gaps.
    // 0.24.8: computed (not const) because the per-button width scales with the UI font
    // multiplier (AddBeelzSmallButton applies Theme.ScaledWidth) — at Large+ the narrow
    // 18px buttons word-wrapped their captions vertically. Header + row must agree.
    private static int BeelzSlotsColW => Theme.ScaledWidth(BEELZ_ASSIGN_BTN_W) * 8 + BEELZ_ASSIGN_BTN_SP * 7;
    // 0.18.4: cap how many ability rows auto-fetch `api info` (description/cooldown) when a list is
    // (re)built. Only the top chunk — roughly what's on screen — is enriched; feeding the WHOLE filtered
    // list walked the entire collection across rebuilds and froze the UI on a fully-collected server.
    private const int BEELZ_ENRICH_MAX_ROWS = 40;

    private string FormatBeelzAssignKind() => _beelzAssignKind switch
    {
        BeelzAbilKind.Magic  => "Kind: Magic",
        BeelzAbilKind.Weapon => "Kind: Weapon",
        BeelzAbilKind.Form   => "Kind: Form",
        _                    => "Kind: All",
    };

    // True if an ability matches a Kind filter. Uses the catalog scan; everything matches
    // "All", and until a scan runs Magic/Weapon/Form match nothing (catalog unknown → "").
    private static bool BeelzKindMatches(string abilityName, BeelzAbilKind filter)
    {
        if (filter == BeelzAbilKind.All) return true;
        string kind = BeelzState.AbilityKind(abilityName); // "Magic" / "Weapon" / "Form" / ""
        return filter switch
        {
            BeelzAbilKind.Magic  => kind == "Magic",
            BeelzAbilKind.Weapon => kind == "Weapon",
            BeelzAbilKind.Form   => kind == "Form",
            _                    => true,
        };
    }
    private bool BeelzMatchesKind(BeelzCapture cap) => BeelzKindMatches(cap.AbilityName, _beelzAssignKind);

    // Parameterized filter-button captions (shared by the loadout + hotkey bind lists).
    private static string FmtBeelzSource(BeelzAbilSource s) => s switch
    { BeelzAbilSource.VBlood => "Src: V-Blood", BeelzAbilSource.Regular => "Src: Regular", _ => "Src: All" };
    private static string FmtBeelzKind(BeelzAbilKind k) => k switch
    { BeelzAbilKind.Magic => "Kind: Magic", BeelzAbilKind.Weapon => "Kind: Weapon", BeelzAbilKind.Form => "Kind: Form", _ => "Kind: All" };
    private static string FmtBeelzGroup(BeelzGroupMode m) => m switch
    { BeelzGroupMode.Unit => "Group: Unit", BeelzGroupMode.Category => "Group: Category", BeelzGroupMode.Kind => "Group: Kind", BeelzGroupMode.Weapon => "Group: Weapon", _ => "Group: off" };
    private static string FmtBeelzCat(string c) => string.IsNullOrEmpty(c) ? "Cat: All" : $"Cat: {c}";

    private void CycleBeelzHkCategory()
    {
        var cats = BeelzCategories();
        if (cats.Count == 0) { _beelzHkCategory = ""; return; }
        int idx = string.IsNullOrEmpty(_beelzHkCategory)
            ? -1 : cats.FindIndex(x => x.Equals(_beelzHkCategory, StringComparison.OrdinalIgnoreCase));
        idx++;
        _beelzHkCategory = idx >= cats.Count ? "" : cats[idx];
    }

    // The 3 shared text columns (Ability | Unit | Cat) + the dynamic detail hover, used by
    // BOTH the loadout assign rows and the hotkey bind rows so they line up identically. The
    // caller adds the trailing action (6 slot buttons, or one Bind button) after this.
    private void AddBeelzCaptureColumns(GameObject row, BeelzCapture cap)
    {
        void Col(string name, string text, int min, int pref, int flex, int fs)
        {
            var l = UIFactory.CreateLabel(row, name, text, TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(fs));
            UIFactory.SetLayoutElement(l.GameObject, minWidth: min, preferredWidth: pref, flexibleWidth: flex, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
            l.TextMesh.enableWordWrapping = false; l.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        }
        string src = cap.Source == 'V' ? "<color=#FFD75A>V</color> " : "";
        // api32: badge an ability an incompatibility lock would keep off the bar (its group is already full).
        if (!string.IsNullOrEmpty(BeelzState.LockConflict(cap.AbilityGuid, cap.Category))) src = "<color=#FF8080>[Locked]</color> " + src;
        Col("Abil", $"{src}{cap.DisplayAbility}",                                                       BEELZ_COL_ABILITY_MIN, BEELZ_COL_ABILITY_PREF, 1, 12);
        Col("Unit", $"<color={Theme.MutedBodyHex}>{BeelzUnitName(cap.UnitGuid, cap.UnitName)}</color>", BEELZ_COL_UNIT_MIN,    BEELZ_COL_UNIT_PREF,    1, 11);
        Col("Cat",  $"<color={Theme.MutedBodyHex}>{(string.IsNullOrEmpty(cap.Category) ? "—" : cap.Category)}</color>", BEELZ_COL_CAT_MIN, BEELZ_COL_CAT_PREF, 0, 11);
        if (Config.Settings.BeelzDiagnostics)
            AddBeelzIdCopyCell(row, cap.AbilityGuid);   // #F11: ID is click-to-copy (the guid only)
        TooltipHover.Attach(row, () => BeelzAbilityHoverText(cap));
    }

    // Friendly unit name: bestiary's resolved name when known, else humanized prefab.
    private static string BeelzUnitName(string unitGuid, string rawName)
        => BeelzNames.Unit(BeelzState.ResolvedUnit(unitGuid, rawName));

    // #F11: an ID cell sized to the ID column that is itself a click-to-copy button — copies ONLY the
    // ability GUID (separate from the full-detail Copy). Same width as the header "ID" column so the
    // table stays aligned. Empty guid → a muted "—" (uncaptured rows have no guid on the wire).
    private void AddBeelzIdCopyCell(GameObject row, string guid)
    {
        string g = guid ?? "";
        if (g.Length == 0)
        {
            var l = UIFactory.CreateLabel(row, $"BeelzIdNone_{row.transform.childCount}", $"<color={Theme.MutedBodyHex}>—</color>",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(10));
            UIFactory.SetLayoutElement(l.GameObject, minWidth: BEELZ_COL_ID_MIN, preferredWidth: BEELZ_COL_ID_PREF, flexibleWidth: 0, minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
            return;
        }
        var btn = UIFactory.CreateButton(row, $"BeelzIdCopy_{g}_{row.transform.childCount}", g);
        UIFactory.SetLayoutElement(btn.GameObject, minWidth: BEELZ_COL_ID_MIN, preferredWidth: BEELZ_COL_ID_PREF, flexibleWidth: 0, minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(10); t.color = new Color(0.62f, 0.82f, 1f); t.alignment = TextAlignmentOptions.MidlineLeft; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis; }
        TooltipHover.Attach(btn.GameObject, $"Click to copy this ability ID ({g}).");
        btn.OnClick = () => { try { UnityEngine.GUIUtility.systemCopyBuffer = g; } catch { } };
    }

    // Compact metadata columns for an ability row: category (cat=) · kind (catalog) ·
    // creature-type (type=) · weapon/form restriction (catalog). Unit is shown separately.
    private static string BeelzAbilityTags(BeelzCapture cap)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(cap.Category)) parts.Add(cap.Category);
        string kind = BeelzState.AbilityKind(cap.AbilityName);
        if (!string.IsNullOrEmpty(kind)) parts.Add(kind);
        if (!string.IsNullOrEmpty(cap.UnitType)) parts.Add(cap.UnitType);
        if (BeelzState.TryGetCatalog(cap.AbilityName, out var c))
        {
            if (c.Weapons != null && c.Weapons.Count > 0) parts.Add(string.Join("/", c.Weapons));
            if (c.Forms   != null && c.Forms.Count   > 0) parts.Add("forms:" + string.Join("/", c.Forms));
        }
        return parts.Count == 0 ? "" : $"  <color={Theme.MutedBodyHex}>[{string.Join(" · ", parts)}]</color>";
    }

    // Rich search haystack: ability (friendly + raw) + unit + category + kind + creature-type
    // + weapon/form families — so a single search box matches by ANY of them (ability name,
    // mob/unit, weapon type, category, kind, …). Catalog fields fill in after Scan all.
    private static string BeelzSearchHaystack(BeelzCapture cap)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(BeelzNames.Ability(cap.AbilityName)).Append(' ').Append(cap.AbilityName).Append(' ');
        sb.Append(BeelzUnitName(cap.UnitGuid, cap.UnitName)).Append(' ');
        sb.Append(cap.Category).Append(' ').Append(cap.UnitType).Append(' ');
        sb.Append(BeelzState.AbilityKind(cap.AbilityName)).Append(' ');
        if (BeelzState.TryGetCatalog(cap.AbilityName, out var c))
        {
            if (c.Weapons != null) sb.Append(string.Join(" ", c.Weapons)).Append(' ');
            if (c.Forms   != null) sb.Append(string.Join(" ", c.Forms));
        }
        return sb.ToString();
    }

    // Header refs so the column header can be repopulated when the diagnostics ID column is
    // toggled. The header lives OUTSIDE the rebuildable row container (it stays put while the
    // list scrolls), so a diagnostics flip must repopulate it explicitly or it drifts out of
    // alignment with the rows. Loadout uses the default last column; hotkeys uses "Bind"/54.
    private GameObject _beelzLoadoutHeaderRow;
    private GameObject _beelzHkHeaderRow;
    private const string BEELZ_HK_HEADER_LASTCOL = "Bind";
    private const int    BEELZ_HK_HEADER_LASTCOLW = 54;

    // Column header for the tabular capture lists (column widths kept in sync with the rows).
    // lastCol/lastColW set the trailing column (loadout: "Slots 1-6"; hotkeys: "Bind").
    // lastColW = -1 → the scaled slots-column width (can't be a default param — it's computed).
    // Returns the header row so the caller can stash it for diagnostics-driven repopulation.
    private GameObject AddBeelzAssignColumnHeader(GameObject parent, string lastCol = "Slots", int lastColW = -1, bool copyCol = false)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "BeelzAssignColHeader",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(18), preferredHeight: Theme.ScaledHeight(20), flexibleHeight: 0);
        PopulateBeelzAssignColumnHeader(row, lastCol, lastColW, copyCol);
        return row;
    }

    // (Re)build the header's column labels — including the diagnostics-only ID column — into an
    // existing header row. Clears children first, so it's safe to call repeatedly on a toggle.
    private void PopulateBeelzAssignColumnHeader(GameObject row, string lastCol, int lastColW, bool copyCol = false)
    {
        if (row == null) return;
        if (lastColW < 0) lastColW = BeelzSlotsColW;   // -1 → the (font-scaled) slots-column width
        ClearChildren(row);
        void Col(string t, int min, int pref, int flex)
        {
            var l = UIFactory.CreateLabel(row, $"BeelzAsgCol_{t}", $"<color={Theme.MutedBodyHex}><b>{t}</b></color>",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(l.GameObject, minWidth: min, preferredWidth: pref, flexibleWidth: flex, minHeight: Theme.ScaledHeight(16), preferredHeight: Theme.ScaledHeight(18), flexibleHeight: 0);
            l.TextMesh.enableWordWrapping = false; l.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        }
        Col("Ability",   BEELZ_COL_ABILITY_MIN, BEELZ_COL_ABILITY_PREF, 1);
        Col("Unit",      BEELZ_COL_UNIT_MIN,    BEELZ_COL_UNIT_PREF,    1);
        Col("Cat",       BEELZ_COL_CAT_MIN,     BEELZ_COL_CAT_PREF,     0);
        if (Config.Settings.BeelzDiagnostics) Col("ID", BEELZ_COL_ID_MIN, BEELZ_COL_ID_PREF, 0);
        Col(lastCol,     lastColW,              lastColW,               0);
        // Loadout assign rows add a diagnostics-only "Copy" button (width 46) AFTER the slot buttons;
        // reserve a matching header column so the flexible Ability/Unit columns line up with the rows.
        // (Scaled like the button itself — AddBeelzSmallButton applies Theme.ScaledWidth.)
        if (copyCol && Config.Settings.BeelzDiagnostics) Col("", Theme.ScaledWidth(46), Theme.ScaledWidth(46), 0);
    }

    // Repopulate both capture-list column headers (loadout + hotkeys) so the ID column appears/
    // disappears in lockstep with the rows when diagnostics is toggled or a Beelz tab is shown.
    private void RebuildBeelzColumnHeaders()
    {
        if (_beelzLoadoutHeaderRow != null) PopulateBeelzAssignColumnHeader(_beelzLoadoutHeaderRow, "Slots", BeelzSlotsColW, copyCol: true);
        if (_beelzHkHeaderRow      != null) PopulateBeelzAssignColumnHeader(_beelzHkHeaderRow, BEELZ_HK_HEADER_LASTCOL, Theme.ScaledWidth(BEELZ_HK_HEADER_LASTCOLW));
    }

    // ---- two-click confirmation (guards every reset / clear / delete / wipe action) ----
    private string _beelzPendingConfirmKey;
    private float  _beelzPendingConfirmDeadline = -1f;
    private const float BEELZ_CONFIRM_WINDOW = 3f;

    // A button whose action only fires on a SECOND click within BEELZ_CONFIRM_WINDOW.
    // First click relabels to "Confirm?"; arming a different confirm button cancels this one.
    private ButtonRef AddBeelzConfirmButton(GameObject parent, string name, string label, string tooltip,
                                            Action action, int width = 84, Color? color = null)
    {
        var btn = color.HasValue
            ? UIFactory.CreateButton(parent, name, label, color.Value)
            : UIFactory.CreateButton(parent, name, label);
        // 0.24.8: scale width/height with the UI font + never word-wrap (see AddBeelzSmallButton).
        int w = Theme.ScaledWidth(width);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: w, preferredWidth: w, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null)
        {
            t.fontSize = Theme.ScaledUI(11); t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow;
        }
        if (!string.IsNullOrEmpty(tooltip)) TooltipHover.Attach(btn.GameObject, tooltip + "  (Two-click confirm — click again within 3s.)");
        string key = name;
        btn.OnClick = () =>
        {
            if (!BeelzState.Present) return;
            float now = Time.realtimeSinceStartup;
            var lbl = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
            bool armed = _beelzPendingConfirmKey == key && now <= _beelzPendingConfirmDeadline;
            if (armed)
            {
                _beelzPendingConfirmKey = null; _beelzPendingConfirmDeadline = -1f;
                if (lbl != null) lbl.text = label;
                action();
            }
            else
            {
                _beelzPendingConfirmKey = key; _beelzPendingConfirmDeadline = now + BEELZ_CONFIRM_WINDOW;
                if (lbl != null) lbl.text = "Confirm?";
            }
        };
        return btn;
    }

    // ---- admin tab state ----
    private GameObject _beelzAdminConfigContainer;
    private string _beelzAdminConfigSearch = "";
    private string _beelzAdminPlayerName = "";
    private string _beelzAdminUnitGuid = "";
    private string _beelzAdminAbilityGuid = "";
    private TextMeshProUGUI _beelzAdminStatusLabel;
    // Per-ability shaping editor (Admin: Players tab). Field token sent to `.beelz admin ability`.
    private string _beelzAdminShapeField = "cooldown";
    private string _beelzAdminShapeValue = "";
    // F5: current-settings readout for the ability being shaped (filled from api info-guid).
    private TextMeshProUGUI _beelzShapeInfoLabel;
    private string _beelzShapeInfoGuid = "";
    // Incompatibility locks admin (Beelz v0.135 / api32) — group name, member list, max, live group readout.
    private string _beelzLockGroup = "", _beelzLockMembers = "", _beelzLockMax = "";
    private TextMeshProUGUI _beelzLocksLabel;
    // F8 (lighter): live "current target" banners repeated near destructive admin actions.
    private readonly List<TextMeshProUGUI> _beelzAdminTargetBanners = new();
    // The server-wide shaping/tuning fields `.beelz admin ability <id> <field> <value>` accepts
    // (Beelz v0.65–v0.87). Curation fields (enabled/weapons/forms/notes) stay in the per-ability help.
    private static readonly string[] BeelzShapeFields =
    {
        "cooldown", "range", "charges", "chargetime", "aoe", "projspeed", "duration", "healing",
        "forcetimeout", "freelymove", "interruptonhit", "interruptible", "freemove", "castspeed",
        "summoncap", "summontimeout", "summonunits", "damagescale", "cooldownscale",
        // Beelz v0.120/v0.125 write-only tunables + v0.134 (api31) tuning knobs.
        "leapheight", "powerwindow", "maxstacks", "projcount", "knockback", "lifetime", "casttime",
    };
    // Capture-filter admin (Admin: Config) — substring pattern + GUID inputs.
    private string _beelzAdminFilterPattern = "";
    private string _beelzAdminFilterGuid = "";
    // Per-unit transform tuning (Admin: Players) — `.beelz admin transform-set <CHAR_unit> <field> <value>`.
    private string _beelzAdminTxUnit = "";
    private string _beelzAdminTxField = "enabled";
    private string _beelzAdminTxValue = "";
    private static readonly string[] BeelzTxSetFields =
    {
        "enabled", "difficulty", "tier", "damagescale", "cooldownscale", "healthscale", "speedscale",
        "fullreplace", "powerscalingmode",
        // Beelz v0.100: per-transformation duration / cooldown overrides of the category defaults
        // (value in seconds, or "inherit" to clear the override and fall back to the category).
        "duration", "cooldown",
        "notes",
    };

    // #9: config keys whose value is an enum → render a dropdown of valid values instead of free text
    // (the wire sends only type=, not the allowed set). Keyed by the config KEY name.
    private static readonly Dictionary<string, string[]> BeelzConfigEnums = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Grant_PowerScalingMode"]      = new[] { "PlayerScaled", "Boosted" },
        ["Transform_PowerScalingMode"]  = new[] { "CuratedScales", "PrefabAbsolute", "PlayerScaled", "PlayerLeveled" },
        ["Transform_Mode_Regular"]      = new[] { "Toggle", "Timed", "Disabled" },
        ["Transform_Mode_VBlood"]       = new[] { "Toggle", "Timed", "Disabled" },
        ["Transform_Mode_ShardBoss"]    = new[] { "Toggle", "Timed", "Disabled" },
        ["Transform_PhaseMode"]         = new[] { "Manual", "Auto" },
        ["Transform_MountedSummonMode"] = new[] { "Stash", "Follow" },
        // Beelz v0.100: how the transform cooldown budget is scoped.
        ["Transform_CooldownScope"]     = new[] { "PerCategory", "PerTransformation", "Global" },
        ["Server_DifficultyMode"]       = new[] { "Basic", "Brutal" },
        ["Capture_ShareCreditMode"]     = new[] { "KillerOnly", "Proximity" },
        ["DefaultVerbosity"]            = new[] { "Silent", "Summary", "Verbose" },
    };

    // #10: client-side descriptions for the common config keys (the wire doesn't send them). Shown as a
    // hover tooltip on the config row. Partial coverage by design — keys not here just show no tooltip.
    private static readonly Dictionary<string, string> BeelzConfigDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CaptureOnKill"] = "Master switch: capture abilities on kills.",
        ["Capture_InclusiveMode"] = "Make abilities across ALL V-Bloods/NPCs broadly capturable (bypasses deny/allow + the difficulty gate). OFF = only the curated rules. The full collection catalog/Bestiary depends on this being ON.",
        ["Grant_EnforceTransformOnly"] = "If ON, abilities flagged transform-only can't be granted to the normal bar.",
        ["Capture_ShareCreditMode"] = "Who gets capture credit on a kill: KillerOnly, or Proximity (nearby players share).",
        ["Capture_ShareCreditRadius"] = "Radius (m) for Proximity credit sharing.",
        ["DropChance_Ability_Regular"] = "Per-kill chance (0–1) to capture an ability from a regular mob.",
        ["DropChance_Ability_VBlood"] = "Per-kill chance (0–1) to capture an ability from a V-Blood.",
        ["DropChance_Devour_Regular"] = "Rare jackpot chance (0–1) to Devour a regular unit (learn ALL its abilities at once).",
        ["DropChance_Devour_VBlood"] = "Rare jackpot chance (0–1) to Devour a V-Blood (learn ALL its abilities at once).",
        ["Capture_PityIncrementPerKill"] = "How much bad-luck protection builds per dry kill (ability capture).",
        ["Capture_PityMaxBonus"] = "Cap on accumulated ability-capture pity bonus.",
        ["Capture_PityIncrement_Devour"] = "Pity increment for the Devour jackpot.",
        ["Capture_PityMax_Devour"] = "Cap on Devour pity bonus.",
        ["Capture_PitySessionBased"] = "ON: pity resets each logout (session-based). OFF: permanent across sessions.",
        ["DefaultVerbosity"] = "Default chat-notification level for players: Silent / Summary / Verbose.",
        ["Abilities_ApplyConfig"] = "Master switch for per-ability shaping (cooldown/range/etc.). Default ON; OFF disables all baked ability edits.",
        ["Forms_CustomAbilities_Enabled"] = "Inject your loadout abilities onto vanilla shapeshift forms. Required for per-form sets to apply in-game.",
        ["Forms_AutoFillFromCaptures"] = "A shapeshift form with no per-form or universal binds: OFF (default) keeps the form's own kit; ON fills its bar with your first six captures (the pre-0.137 behavior).",
        ["Grant_PowerScalingMode"] = "How granted abilities scale: PlayerScaled (with your power) or Boosted.",
        ["Grant_PowerScalingFactor"] = "Multiplier applied when scaling granted-ability power.",
        ["Grant_MinimumCooldownSeconds"] = "Global floor on every ability's cooldown (0 = none).",
        ["Grant_EnforceWeaponMatch"] = "If ON, hard-block granting a weapon-bound ability to the universal bar.",
        ["Server_DifficultyMode"] = "Capture/transform difficulty gate: Basic or Brutal.",
        ["Hotkeys_Enabled"] = "Allow players to bind extra-ability hotkeys (the action-bar buttons).",
        ["Hotkeys_MaxPerPlayer"] = "Max named hotkeys per player.",
        ["Transform_Mode_Regular"] = "Regular-unit transform mode: Toggle / Timed / Disabled.",
        ["Transform_Mode_VBlood"] = "V-Blood transform mode: Toggle / Timed / Disabled.",
        ["Transform_Mode_ShardBoss"] = "Shard-boss transform mode: Toggle / Timed / Disabled.",
        ["Transform_DurationSeconds_Regular"] = "Timed-mode duration for regular transforms (s).",
        ["Transform_DurationSeconds_VBlood"] = "Timed-mode duration for V-Blood transforms (s).",
        ["Transform_DurationSeconds_ShardBoss"] = "Timed-mode duration for shard-boss transforms (s).",
        ["Transform_CooldownSeconds_Regular"] = "Cooldown after a regular transform ends (s).",
        ["Transform_CooldownSeconds_VBlood"] = "Cooldown after a V-Blood transform ends (s).",
        ["Transform_CooldownSeconds_ShardBoss"] = "Cooldown after a shard-boss transform ends (s).",
        ["Transform_ShardBossNames"] = "Comma-separated unit names treated as shard bosses (own mode/cooldown bucket).",
        ["Transform_ReconnectGraceSeconds"] = "Keep a transform + summons alive this long after a disconnect (0 = revert on DC, -1 = until manual revert).",
        ["Transform_SummonLifetimeSeconds"] = "Auto-despawn summons after this many seconds (0 = never).",
        ["Transform_MaxStacksPerSummonAbility"] = "Max concurrent 'uses' of a given summon ability.",
        ["Transform_MountedSummonMode"] = "On a horse: Stash summons, or keep them Following.",
        ["Transform_PhaseMode"] = "Multi-phase boss form switching: Manual or Auto (by HP).",
        // Beelz v0.100 transform-admin keys.
        ["Transform_Enabled"] = "Master switch for ALL transformations server-wide. OFF disables every form (overrides the per-category / per-unit settings).",
        ["Transform_CooldownScope"] = "How the transform cooldown is budgeted: PerCategory (one cooldown per Regular/V-Blood/Shard bucket), PerTransformation (each form its own — e.g. 30 min/day per form), or Global (one shared cooldown across all forms). Pairs with per-form duration/cooldown for 'once a day for 30 min' rules.",
        ["DropChance_TransformUnlock_VBlood"] = "Chance (0–1) on a V-Blood kill to unlock its TRANSFORM (a separate, rarer roll than Devour).",
        ["DropChance_TransformUnlock_Regular"] = "Chance (0–1) on a regular-unit kill to unlock its TRANSFORM (e.g. the basic werewolf), separate from Devour.",
        ["Capture_PityIncrement_Transform"] = "How much bad-luck protection builds per dry kill toward a transform unlock.",
        ["Capture_PityMax_Transform"] = "Cap on accumulated transform-unlock pity bonus.",
        ["Broadcast_CollectionComplete_Enabled"] = "Announce server-wide when a player reaches 100% collection.",
        ["Broadcast_Leaderboard_Enabled"] = "Periodically broadcast the collection leaderboard.",
        ["Broadcast_Leaderboard_IntervalMinutes"] = "Minutes between leaderboard broadcasts.",
        ["Broadcast_Leaderboard_TopN"] = "How many top collectors to list (1–5).",
        ["Broadcast_CollectionComplete_Messages"] = "Message pool for the 100%-collection broadcast (%player% = the player's name). One is picked at random. Managed below via add/edit/remove (admin broadcast-msg).",
        ["Broadcast_Leaderboard_Messages"] = "Message pool for the periodic leaderboard broadcast (%top% = the list, %count% = entries). Managed below via add/edit/remove (admin broadcast-msg).",
        ["VerboseLogging"] = "Server-side verbose logging (diagnostics).",
        // F9: keys filled in from Beelzebub's Settings.cs descriptions.
        ["Capture_ShareCreditRadius"] = "Radius (~meters) for Proximity credit sharing. Ignored when ShareCreditMode = KillerOnly.",
        ["Capture_TierMidThreshold"] = "Unit level at which a killed unit moves from Low to Mid tier (for the tier drop-chance multipliers).",
        ["Capture_TierHighThreshold"] = "Unit level at which a killed unit moves from Mid to High tier.",
        ["Capture_TierMultiplier_Low"] = "Drop-chance multiplier for Low-tier units (level < mid threshold). 1.0 = no effect.",
        ["Capture_TierMultiplier_Mid"] = "Drop-chance multiplier for Mid-tier units.",
        ["Capture_TierMultiplier_High"] = "Drop-chance multiplier for High-tier units (level ≥ high threshold).",
        ["Capture_GrantSignatureSummons"] = "Grant a unit's signature summon as a standalone capturable ability when you unlock/devour it.",
        ["Transform_SummonsAreAllies"] = "Summoned minions fight as YOUR allies (vs. neutral/hostile).",
        ["Transform_SummonLeashRadius"] = "How far a summon will wander from you before being pulled back (~meters).",
        ["Transform_SummonMatchPlayerLevel"] = "Scale a summon to your level on spawn (vs. the original boss-add's level).",
        ["Transform_SummonPowerFactor"] = "Extra power multiplier on summons (hit/tank harder or softer).",
        ["Transform_SummonCooldownSeconds"] = "Cooldown for the signature add-summon (.beelz summon).",
        ["Transform_ManualDetonateCooldownSeconds"] = "Cooldown for manually firing a transform's detonation AoE (.beelz detonate).",
        ["Transform_DespawnSummonsOnDisconnect"] = "Despawn a player's summons when they disconnect.",
        ["Transform_DespawnBudgetPerFrame"] = "Max summons cleaned up per frame (despawn throttle).",
        ["Transform_PlayerLeveled_MaxLevel"] = "Max level used when PowerScalingMode = PlayerLeveled.",
        ["Transform_SummonCounterBuffGuid"] = "Optional buff PrefabGUID for an on-screen summon counter (0 = off).",
    };

    private string FormatBeelzAssignSource() => _beelzAssignSource switch
    {
        BeelzAbilSource.VBlood  => "Src: V-Blood",
        BeelzAbilSource.Regular => "Src: Regular",
        _                       => "Src: All",
    };
    private string FormatBeelzAssignGroup() => _beelzAssignGroupMode switch
    {
        BeelzGroupMode.Unit     => "Group: Unit",
        BeelzGroupMode.Category => "Group: Category",
        BeelzGroupMode.Kind     => "Group: Kind",
        BeelzGroupMode.Weapon   => "Group: Weapon",
        _                       => "Group: off",
    };
    private string FormatBeelzBestiaryCategory() => string.IsNullOrEmpty(_beelzBestiaryCategory) ? "Cat: All" : $"Cat: {_beelzBestiaryCategory}";
    private string FormatBeelzBestiaryKind() => _beelzBestiaryKind switch
    {
        BeelzAbilKind.Magic  => "Kind: Magic",
        BeelzAbilKind.Weapon => "Kind: Weapon",
        BeelzAbilKind.Form   => "Kind: Form",
        _                    => "Kind: All",
    };

    // Group key(s) for an ability under the current group mode. Usually one, but Weapon
    // mode returns MANY — a sword+axe ability lands under both "Sword" and "Axe" so it's
    // discoverable from either (abilities are legitimately multi-weapon).
    private IEnumerable<string> BeelzGroupKeysFor(BeelzCapture cap) => BeelzGroupKeysFor(cap, _beelzAssignGroupMode);
    private IEnumerable<string> BeelzGroupKeysFor(BeelzCapture cap, BeelzGroupMode groupMode)
    {
        switch (groupMode)
        {
            case BeelzGroupMode.Unit:
                return new[] { BeelzUnitName(cap.UnitGuid, cap.UnitName) };
            case BeelzGroupMode.Category:
                return new[] { string.IsNullOrEmpty(cap.Category) ? "(uncategorized)" : cap.Category };
            case BeelzGroupMode.Kind:
            {
                if (!BeelzState.CatalogLoaded) return new[] { "(scan for Kind)" };
                string k = BeelzState.AbilityKind(cap.AbilityName);
                return new[] { string.IsNullOrEmpty(k) ? "(unknown)" : k };
            }
            case BeelzGroupMode.Weapon:
            {
                if (!BeelzState.CatalogLoaded) return new[] { "(scan for Weapon)" };
                if (BeelzState.TryGetCatalog(cap.AbilityName, out var c) && c.Weapons != null && c.Weapons.Count > 0)
                {
                    // Group only by ALLOW (whitelist) families. A `!`-blacklist token (Beelz v0.101) means
                    // "usable everywhere EXCEPT this weapon" — not a per-weapon membership — so it falls
                    // through to the universal bucket below rather than spawning a "!Sword" group.
                    var allow = new List<string>();
                    foreach (var w in c.Weapons) if (!string.IsNullOrEmpty(w) && w[0] != '!') allow.Add(w);
                    if (allow.Count > 0) return allow;   // multi-membership: one group per weapon family
                }
                return new[] { "Any / no weapon" };       // Magic / universal / blacklist-only abilities
            }
            default: return Array.Empty<string>();
        }
    }
    private string FormatBeelzAssignCategory() => string.IsNullOrEmpty(_beelzAssignCategory) ? "Cat: All" : $"Cat: {_beelzAssignCategory}";

    private List<string> BeelzCategories()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in BeelzState.Captures)
            if (!string.IsNullOrEmpty(c.Category)) set.Add(c.Category);
        return new List<string>(set);
    }
    private void CycleBeelzAssignCategory()
    {
        var cats = BeelzCategories();
        if (cats.Count == 0) { _beelzAssignCategory = ""; return; }
        int idx = string.IsNullOrEmpty(_beelzAssignCategory)
            ? -1 : cats.FindIndex(x => x.Equals(_beelzAssignCategory, StringComparison.OrdinalIgnoreCase));
        idx++;
        _beelzAssignCategory = idx >= cats.Count ? "" : cats[idx];
    }
    private static void SetBeelzButtonText(ButtonRef b, string text)
    {
        var t = b?.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = text;
    }

    // Beelzebub spell-bar slot meanings. 1-6 are the spell slots; Beelz v0.91/0.94 added the
    // primary (engine slot 0, left-click attack) and ultimate (engine slot 7, the T key) as bindable
    // targets — used especially for shapeshift forms (e.g. wolf form renders on the ultimate slot).
    private static string BeelzSlotLabel(int n) => n switch
    {
        0 => "Primary (LMB)",
        1 => "Slot 1 (Q)",  2 => "Travel (Space)", 3 => "Shift",
        4 => "Heavy (E)",   5 => "Spell 1 (R)",    6 => "Spell 2 (C)",
        7 => "Ultimate (T)",
        _ => $"Slot {n}",
    };
    // Short caption for a slot button / row prefix. Numeric mode: P (primary), U (ultimate), else 1-6.
    // Key mode (Settings.BeelzSlotKeyLabels): the key each slot uses — LM/Q/Sp/Sh/E/R/C/T.
    private static string BeelzSlotBtn(int n)
    {
        if (Config.Settings.BeelzSlotKeyLabels)
            return n switch { 0 => "LM", 1 => "Q", 2 => "Sp", 3 => "Sh", 4 => "E", 5 => "R", 6 => "C", 7 => "T", _ => n.ToString() };
        return n == 0 ? "P" : n == 7 ? "U" : n.ToString();
    }

    // PERF (Beelzebub-server UI lag): the Beelzebub tabs are pre-built once by
    // BuildContentArea and only toggled visible. Their state-change handlers must NOT
    // rebuild while their tab is hidden — the `api info` enrichment fires ~4x/sec for
    // the whole (multi-minute) drain on a heavily-collected server, and left ungated it
    // rebuilt the big loadout/bestiary lists off-screen the entire time (and for 1-2 min
    // after closing the panel, while the queue kept draining). That was the cause of the
    // severe UI-open frame drop seen only on a server that has Beelzebub. Gate every
    // dynamic rebuild on its owning tab being visible; ShowTab re-syncs a Beelz tab when
    // you switch to it. (Enabled = the main panel is open; ActiveTab = the shown tab.)
    private bool BeelzTabVisible(PanelType tab) => Enabled && ActiveTab == tab;

    // Re-sync a Beelzebub tab's dynamic lists from current BeelzState when it becomes
    // visible (state-change events are ignored while it's hidden). Called from ShowTab.
    internal void RefreshBeelzTabOnShow(PanelType tab)
    {
        switch (tab)
        {
            // Tab-switch freeze fix: rows persist while hidden, so only rebuild the heavy lists if their
            // data changed since the last build (dirty). Header/slots are cheap → always refresh.
            case PanelType.BeelzBestiaryTab:    RefreshBeelzBestiaryHeader(); if (_beelzBestiaryDirty) RebuildBeelzBestiaryRows(); break;
            case PanelType.BeelzLoadoutTab:     RebuildBeelzColumnHeaders(); RebuildBeelzLoadoutSlots(); if (_beelzLoadoutAssignDirty) RebuildBeelzLoadoutAssign(); break;
            case PanelType.BeelzHotkeysTab:     RebuildBeelzColumnHeaders(); RebuildBeelzHotkeyRows(); RebuildBeelzHotkeyBindList(); break;
            case PanelType.BeelzTransformsTab:
                RebuildBeelzTransformRows(); RebuildBeelzTxConfig();
                if (BeelzState.Present) { BeelzClient.RequestTransformConfig(); BeelzClient.RequestCooldowns(); }
                break;
            case PanelType.BeelzSettingsTab:    RefreshBeelzDiagnosticsReadout(); break;
            case PanelType.BeelzAdminConfigTab: RebuildBeelzAdminConfig(); break;
            case PanelType.BeelzAdminAbilityTableTab: RebuildBeelzAbilityTable(); break;
            case PanelType.ConnectionTab:       RefreshConnectionReadout(); break;
        }
    }

    // The catalog scan ("Scan all") commits once on completion (CatalogChanged) and emits a
    // per-page progress tick (ScanProgress) for the button label. Both refresh ONLY the
    // visible Beelz tab (gated), so an off-screen scan can't thrash the UI.
    private TextMeshProUGUI _beelzLoadoutScanStatus;   // "Scan all" status on the Loadout tab
    private void OnBeelzCatalogChanged()
    {
        // Rebuild the visible list FIRST (so it always reflects the freshly-scanned catalog
        // regardless of any active filter — the reported "list went empty after scan with an
        // AOE filter" was the refresh not landing). Update the status labels after, guarded,
        // so a stale label ref can never skip the rebuild.
        _beelzLoadoutAssignDirty = true; _beelzBestiaryDirty = true;   // catalog changed → rebuild on next show
        if (BeelzTabVisible(PanelType.BeelzBestiaryTab)) { RebuildBeelzBestiaryRows(); RefreshBeelzBestiaryHeader(); }
        if (BeelzTabVisible(PanelType.BeelzLoadoutTab))  RebuildBeelzLoadoutAssign();
        if (BeelzTabVisible(PanelType.BeelzHotkeysTab))  RebuildBeelzHotkeyBindList();
        try { UpdateBeelzScanStatusLabels(); } catch { /* label-only; never block the rebuild */ }
    }
    // Beelz v0.100: the ADMIN `abilities-all` catalog is its own scope (every ability for the config
    // table), separate from the player collectible catalog the Bestiary/Loadout use. Only the Admin:
    // Abilities table keys on it.
    private void OnBeelzCatalogAllChanged()
    {
        if (BeelzTabVisible(PanelType.BeelzAdminAbilityTableTab)) RebuildBeelzAbilityTable();
        try { UpdateBeelzScanStatusLabels(); } catch { /* label-only; never block the rebuild */ }
    }
    private void OnBeelzScanProgress() => UpdateBeelzScanStatusLabels();

    private void UpdateBeelzScanStatusLabels()
    {
        string playerTxt = BeelzScanStatusText(admin: false);
        string adminTxt  = BeelzScanStatusText(admin: true);
        // Each label may belong to a tab that was since rebuilt/destroyed — guard so a stale ref can't throw.
        try { if (_beelzBestiaryScanStatus != null) _beelzBestiaryScanStatus.text = playerTxt; } catch { _beelzBestiaryScanStatus = null; }
        try { if (_beelzLoadoutScanStatus  != null) _beelzLoadoutScanStatus.text  = playerTxt; } catch { _beelzLoadoutScanStatus = null; }
        try { if (_beelzAbilTableScanStatus != null) _beelzAbilTableScanStatus.text = adminTxt; } catch { _beelzAbilTableScanStatus = null; }
        // Show the preset quick-scan rows only on servers that support catalog filters (re-evaluated here so
        // they resolve at handshake even if the panel was built during the handshake window).
        bool showPresets = BeelzState.SupportsCatalogFilters;
        foreach (var r in _beelzPresetRows) { try { if (r != null) r.SetActive(showPresets); } catch { } }
    }
    // admin=true → the `abilities-all` scope (Admin: Abilities table); else the player collectible scope.
    private static string BeelzScanStatusText(bool admin = false)
    {
        if (admin)
        {
            if (BeelzProtocolService.CatalogAllScanInProgress)
            {
                int l = BeelzProtocolService.CatalogAllScanLoaded, t = BeelzProtocolService.CatalogAllScanTotal;
                return t > 0 ? $"<color=#FFD75A>Scanning… {l}/{t}</color>" : $"<color=#FFD75A>Scanning… {l}</color>";
            }
            if (BeelzState.CatalogAllLoaded)
                return $"<color=#90EE90>Scanned: {BeelzState.CatalogAllAbilities.Count} abilities ({BeelzState.CatalogAllEnabledCount} enabled).</color>"
                     + BeelzCatalogSuffix(BeelzState.CatalogAllComplete, BeelzState.CatalogAllCacheInfo);
            return $"<color={Theme.MutedBodyHex}>Not scanned yet — load every ability (enabled + disabled) to configure them.</color>";
        }
        if (BeelzProtocolService.CatalogScanInProgress)
        {
            int loaded = BeelzProtocolService.CatalogScanLoaded, total = BeelzProtocolService.CatalogScanTotal;
            return total > 0 ? $"<color=#FFD75A>Scanning… {loaded}/{total}</color>"
                             : $"<color=#FFD75A>Scanning… {loaded}</color>";
        }
        if (BeelzState.CatalogLoaded)
            return $"<color=#90EE90>Scanned: {BeelzState.CatalogEnabledCount} collectible abilities.</color>"
                 + BeelzCatalogSuffix(BeelzState.CatalogComplete, BeelzState.CatalogCacheInfo);
        return $"<color={Theme.MutedBodyHex}>Not scanned yet — the full collectible list and the Kind filter need a one-time scan.</color>";
    }

    // Trailing hint for the scan status: a partial (filtered-only) set prompts a full scan; a cache-warmed
    // set notes it's cached + offers a refresh. A live, complete scan gets no suffix.
    private static string BeelzCatalogSuffix(bool complete, string cacheInfo)
    {
        if (!complete)
            return "  <color=#FFB070>(partial — Scan all for the full collection)</color>";
        if (!string.IsNullOrEmpty(cacheInfo))
            return $"  <color={Theme.MutedBodyHex}>(cached {cacheInfo} — Re-scan to refresh)</color>";
        return "";
    }

    // Shared "Scan all" control (Bestiary + Loadout headers). Triggers the paginated catalog
    // scan with a perf warning; nothing scans automatically.
    private enum BeelzScanTarget { Bestiary, Loadout, AbilityTable }

    private void AddBeelzScanAllButton(GameObject parent, BeelzScanTarget target)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "BeelzScanRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 8, padding: new Vector4(0, 2, 0, 0));
        UIFactory.SetLayoutElement(row, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);

        // The Admin: Abilities table scans the ADMIN `abilities-all` scope (every ability incl. disabled);
        // the Bestiary/Loadout scan the player collectible scope.
        bool adminScope = target == BeelzScanTarget.AbilityTable;

        var btn = UIFactory.CreateButton(row, "BeelzScanAll", "Scan all abilities");
        UIFactory.SetLayoutElement(btn.GameObject, minWidth: 130, preferredWidth: 150, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        var bt = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (bt != null) { bt.fontSize = Theme.ScaledUI(12); bt.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(btn.GameObject, adminScope
            ? "Load EVERY ability from the server (enabled + disabled/denied) for configuration — one-time; can " +
              "take up to a minute on a big server. Runs in the background. This is the admin config set, broader " +
              "than the player collectible list."
            : "Load the FULL collectible ability list from the server (one-time; can take up to a minute on a big " +
              "server). It runs in the background — you control when. Powers the collector checklist, the metadata " +
              "columns, and the Magic/Weapon/Form filter. You don't need it just to bind abilities.");
        btn.OnClick = () =>
        {
            if (!BeelzState.Present) return;
            if (adminScope) BeelzProtocolService.ScanCatalogAll(); else BeelzProtocolService.ScanCatalog();
            UpdateBeelzScanStatusLabels();
        };

        // Status on its OWN full-width line under the button. The pre-scan copy ("Not scanned yet — …")
        // is long; info labels don't wrap or clip, so in the button row it overflowed across the panel.
        var status = AddInfoLabel(parent, "BeelzScanStatus", BeelzScanStatusText(adminScope), FontStyles.Italic, Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(status.gameObject, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        switch (target)
        {
            case BeelzScanTarget.Loadout:      _beelzLoadoutScanStatus  = status; break;
            case BeelzScanTarget.AbilityTable: _beelzAbilTableScanStatus = status; break;
            default:                           _beelzBestiaryScanStatus = status; break;
        }

        // api23+/27 preset quick-scans: pull just a slice instead of the full ~1,700 rows — a fast
        // cold-start, or a targeted refresh of one group's config. Filtered scans MERGE into the catalog
        // (refresh a slice; they don't replace the full set). Built ALWAYS and shown only when the server
        // supports filters (toggled in UpdateBeelzScanStatusLabels — runs on handshake + scan).
        //
        // 0.22: replaced the 3 fixed buttons (Summons / V-Bloods / Spells) with a compact picker that
        // covers far more slices — every common ability category, plus per-shapeshift-form and per-weapon
        // slices (form/weapon are server-supported filter keys; values come from the known form/weapon
        // sets). Pick a slice, click Scan. Category tokens mirror the server's AbilityCategory strings
        // (the same ones the Loadout "category" filter cycles); an unknown token simply scans nothing.
        var presetRow = UIFactory.CreateHorizontalGroup(parent, "BeelzScanPresets",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 2));
        UIFactory.SetLayoutElement(presetRow, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        AddBeelzControlLabel(presetRow, "BeelzScanPresetsLbl", "Quick scan:");

        // (display label, filter key, filter value) — each is one server-side slice load.
        var presets = new List<(string Label, string Key, string Val)>
        {
            ("Summons",       "cat", "Summon"),
            ("Spells",        "cat", "Spell"),
            ("Projectiles",   "cat", "Projectile"),
            ("AoE",           "cat", "Aoe"),
            ("Buffs",         "cat", "Buff"),
            ("Travel",        "cat", "Travel"),
            ("Weapon spells", "cat", "WeaponSpell"),
            ("V-Bloods",      "vblood", "1"),
        };
        foreach (var fm in BeelzClient.Forms)          presets.Add(($"Form: {fm}",   "form",   fm));
        foreach (var wp in BeelzClient.WeaponFamilies) presets.Add(($"Weapon: {wp}", "weapon", wp));

        var presetLabels = new string[presets.Count];
        for (int i = 0; i < presets.Count; i++) presetLabels[i] = presets[i].Label;
        int presetSel = 0;
        var presetDdObj = UIFactory.CreateDropdown(presetRow, "BeelzScanPresetDd", out var presetDd,
            presetLabels[0], Theme.ScaledUI(11),
            i => { if (i >= 0 && i < presets.Count) presetSel = i; }, presetLabels);
        UIFactory.SetLayoutElement(presetDdObj, minWidth: 150, preferredWidth: 180, flexibleWidth: 0, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        BeelzDropdownNoWrap(presetDd); presetDd.SetValueWithoutNotify(0);
        Raphael.UI.Forms.FormDropdownRegistry.Register(presetDd);
        TooltipHover.Attach(presetDdObj,
            "Pick a slice — an ability category, V-Bloods, a shapeshift form's abilities, or a weapon family's " +
            "abilities — then Scan to merge just that slice into your catalog (no full scan needed).");

        AddBeelzSmallButton(presetRow, "BeelzScanPresetGo", "Scan",
            "Scan only the selected slice — merges into your catalog without a full scan.",
            () =>
            {
                if (!BeelzState.Present) return;
                var p = presets[presetSel];
                if (adminScope) BeelzProtocolService.ScanCatalogAll(p.Key, p.Val);
                else            BeelzProtocolService.ScanCatalog(p.Key, p.Val);
                UpdateBeelzScanStatusLabels();
            }, 64);
        _beelzPresetRows.Add(presetRow);
        presetRow.SetActive(BeelzState.SupportsCatalogFilters);
    }

    private void EnsureBeelzSubscribed()
    {
        if (_beelzSubscribed) return;
        BeelzState.PresenceChanged  += OnBeelzPresenceChanged;
        BeelzState.BestiaryChanged  += OnBeelzCollectionChanged;
        BeelzState.CapturesChanged  += OnBeelzCollectionChanged;
        BeelzState.ProgressChanged  += OnBeelzCollectionChanged;
        BeelzState.SlotsChanged     += RebuildBeelzLoadoutSlots;    // only the 6 slot rows depend on slots
        BeelzState.CapturesChanged  += RebuildBeelzLoadoutRows;     // assign list is built from captures
        BeelzState.CapturesChanged  += RebuildBeelzHotkeyBindList;  // bind list is built from captures
        BeelzState.HotkeysChanged   += RebuildBeelzHotkeyRows;
        BeelzState.TransformsChanged += RebuildBeelzTransformRows;
        BeelzState.ActiveChanged    += RebuildBeelzTransformRows;
        BeelzState.TxConfigChanged  += RebuildBeelzTxConfig;        // transform mode/duration/cooldown profile
        BeelzState.CooldownsChanged += RebuildBeelzTxConfig;        // live transform cooldown remaining
        BeelzState.CatalogChanged   += OnBeelzCatalogChanged;       // full ability matrix (Scan all)
        BeelzState.CatalogAllChanged += OnBeelzCatalogAllChanged;   // admin abilities-all matrix (Beelz v0.100)
        BeelzProtocolService.ScanProgress += OnBeelzScanProgress;   // per-page scan progress (label only)
        BeelzProtocolService.ScanProgressAll += OnBeelzScanProgress; // admin-scope per-page progress (label only)
        BeelzState.ConfigChanged    += RebuildBeelzAdminConfig;
        Raphael.Services.PlayerStateService.LastResponseChanged += OnBeelzLastResponse;   // broadcast-msg list capture (0.20)
        BeelzState.TformKitChanged   += RebuildBeelzTformKitList;     // structured transform kit (api22)
        BeelzState.TformBindsChanged += RebuildBeelzTformKitList;     // structured current binds (api22)
        BeelzState.BroadcastMsgsChanged += OnBeelzBroadcastMsgsChanged; // structured broadcast pool (api22)
        // Connection tab readout (always-available group) — keep it live as detection resolves.
        BeelzState.PresenceChanged  += RefreshConnectionReadout;
        BeelzProtocolService.AvailabilityChanged += RefreshConnectionReadout;
        Raphael.Services.EclipseProtocolService.AvailabilityChanged += RefreshConnectionReadout;
        // 0.26: keep the Uriel connection line live as detection resolves.
        Services.Uriel.UrielState.PresenceChanged += RefreshConnectionReadout;
        Services.Uriel.UrielProtocolService.AvailabilityChanged += RefreshConnectionReadout;
        BeelzState.AbilityInfoChanged += RefreshShapeInfo;   // F5: admin shaping current-settings readout
        BeelzState.LocksChanged += RefreshBeelzLocksLabel;     // api32 lock groups (admin readout)
        BeelzState.LocksChanged += RebuildBeelzLoadoutRows;    // api32 [Locked] badges on the assign list
        BeelzState.LocksChanged += RebuildBeelzHotkeyBindList; // …and the hotkey bind list
        _beelzSubscribed = true;
    }

    // Called from MainPanel.Reset() to avoid leaking handlers across panel rebuilds.
    private void UnsubscribeBeelz()
    {
        if (!_beelzSubscribed) return;
        BeelzState.PresenceChanged  -= OnBeelzPresenceChanged;
        BeelzState.BestiaryChanged  -= OnBeelzCollectionChanged;
        BeelzState.CapturesChanged  -= OnBeelzCollectionChanged;
        BeelzState.ProgressChanged  -= OnBeelzCollectionChanged;
        BeelzState.SlotsChanged     -= RebuildBeelzLoadoutSlots;
        BeelzState.CapturesChanged  -= RebuildBeelzLoadoutRows;
        BeelzState.CapturesChanged  -= RebuildBeelzHotkeyBindList;
        BeelzState.HotkeysChanged   -= RebuildBeelzHotkeyRows;
        BeelzState.TransformsChanged -= RebuildBeelzTransformRows;
        BeelzState.ActiveChanged    -= RebuildBeelzTransformRows;
        BeelzState.TxConfigChanged  -= RebuildBeelzTxConfig;
        BeelzState.CooldownsChanged -= RebuildBeelzTxConfig;
        BeelzState.CatalogChanged   -= OnBeelzCatalogChanged;
        BeelzState.CatalogAllChanged -= OnBeelzCatalogAllChanged;
        BeelzProtocolService.ScanProgress -= OnBeelzScanProgress;
        BeelzProtocolService.ScanProgressAll -= OnBeelzScanProgress;
        BeelzState.ConfigChanged    -= RebuildBeelzAdminConfig;
        Raphael.Services.PlayerStateService.LastResponseChanged -= OnBeelzLastResponse;
        BeelzState.TformKitChanged   -= RebuildBeelzTformKitList;
        BeelzState.TformBindsChanged -= RebuildBeelzTformKitList;
        BeelzState.BroadcastMsgsChanged -= OnBeelzBroadcastMsgsChanged;
        BeelzState.PresenceChanged  -= RefreshConnectionReadout;
        BeelzProtocolService.AvailabilityChanged -= RefreshConnectionReadout;
        Raphael.Services.EclipseProtocolService.AvailabilityChanged -= RefreshConnectionReadout;
        Services.Uriel.UrielState.PresenceChanged -= RefreshConnectionReadout;   // 0.26
        Services.Uriel.UrielProtocolService.AvailabilityChanged -= RefreshConnectionReadout;
        BeelzState.AbilityInfoChanged -= RefreshShapeInfo;
        BeelzState.LocksChanged -= RefreshBeelzLocksLabel;
        BeelzState.LocksChanged -= RebuildBeelzLoadoutRows;
        BeelzState.LocksChanged -= RebuildBeelzHotkeyBindList;
        _beelzSubscribed = false;
    }

    private void OnBeelzPresenceChanged()
    {
        RefreshBeelzBestiaryHeader();
        RebuildBeelzBestiaryRows();
        // Resolve handshake-gated chrome (preset quick-scan rows + cached/partial status) now that the api
        // version is known — the panel may have been built during the handshake window.
        try { UpdateBeelzScanStatusLabels(); } catch { /* label-only; never block presence handling */ }
    }

    private void OnBeelzCollectionChanged()
    {
        // Data changed (captures / bestiary / progress) → both heavy lists need a rebuild next time they
        // show. RebuildBeelzBestiaryRows clears the bestiary flag when it actually builds (visible).
        _beelzLoadoutAssignDirty = true; _beelzBestiaryDirty = true;
        RebuildBeelzBestiaryRows();
        RefreshBeelzBestiaryHeader();
    }

    // If the user forced the group On but no server replied, every tab degrades to
    // a friendly note rather than an empty page.
    private bool AddBeelzAbsentNote(GameObject page)
    {
        if (BeelzState.Present) return false;
        var note = UIFactory.CreateLabel(page, "BeelzAbsent",
            BeelzProtocolService.DetectionGaveUp
                ? "Beelzebub wasn't detected on this server. This tab group is forced On in Settings; switch it back to Auto to hide it where Beelzebub isn't installed."
                : "Looking for Beelzebub on this server… if nothing appears shortly, the server doesn't have the mod.",
            TextAlignmentOptions.TopLeft, color: new Color(1f, 0.85f, 0.5f), fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 48, flexibleHeight: 0);
        note.TextMesh.enableWordWrapping = true;
        return true;
    }

    private ButtonRef AddBeelzRefreshButton(GameObject parent, string tooltip, Action onClick)
    {
        var btn = UIFactory.CreateButton(parent, "BeelzRefreshBtn", "Refresh");
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 90, preferredWidth: 110, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(12); t.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(btn.GameObject, tooltip);
        btn.OnClick = () => { if (BeelzState.Present) onClick(); };
        return btn;
    }

    // Compact action button used in slot/transform/assign rows.
    // 0.24.8: width/height scale with the UI font multiplier (widths were tuned for 1.0×) and the
    // caption never word-wraps — at Large+ the fixed widths made TMP stack short captions
    // VERTICALLY, one letter per line (reported on the Loadout assign-row slot buttons).
    private ButtonRef AddBeelzSmallButton(GameObject parent, string name, string label, string tooltip,
                                          Action onClick, int width = 64, Color? color = null)
    {
        var btn = color.HasValue
            ? UIFactory.CreateButton(parent, name, label, color.Value)
            : UIFactory.CreateButton(parent, name, label);
        int w = Theme.ScaledWidth(width);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: w, preferredWidth: w, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null)
        {
            t.fontSize = Theme.ScaledUI(11); t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow;
        }
        if (!string.IsNullOrEmpty(tooltip)) TooltipHover.Attach(btn.GameObject, tooltip);
        btn.OnClick = () => { if (BeelzState.Present) onClick(); };
        return btn;
    }

    // A small muted caption that prefixes a control row (e.g. "Group:" / "Filter:") so the user can tell
    // structural grouping controls apart from narrowing filters at a glance. Fixed narrow width, no wrap.
    private void AddBeelzControlLabel(GameObject parent, string name, string text)
    {
        var lbl = UIFactory.CreateLabel(parent, name, $"<color={Theme.MutedBodyHex}>{text}</color>",
            TextAlignmentOptions.MidlineRight, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 44, preferredWidth: 48, flexibleWidth: 0, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        lbl.TextMesh.fontStyle = FontStyles.Italic;
    }

    // ============================ BESTIARY ============================

    private void BuildBeelzBestiaryTab(GameObject page)
    {
        EnsureBeelzSubscribed();

        if (AddBeelzAbsentNote(page)) return;

        var headerCard = AddCard(page, "BeelzBestiaryHeaderCard");
        AddSectionHeading(headerCard, "Ability Bestiary — collector checklist");
        AddBodyText(headerCard,
            "Every ability in the server's collectible catalog — plus anything you've already captured — and whether " +
            $"you've got it: your collector's checklist toward 100%. {Mono("Scan all")} loads the catalog once (it's " +
            "heavy; you control when). Without a scan this shows just what you've captured so far. Abilities you " +
            "captured that aren't in the curated catalog are marked <color=#9FD0FF>(off-catalog)</color> — they count " +
            "as collected but sit outside the catalog's 100% target (common while the server's inclusive-capture mode is on).");

        // api25+ (Beelz v0.115): the server's review-status curation gate removes Blocked/Hidden abilities
        // from the collectible set, so the 100% denominator legitimately shrinks. Surfaced so a count drop
        // after a server update reads as curation, not lost captures. Built ALWAYS and shown/hidden by the
        // gate in RefreshBeelzBestiaryHeader (runs on presence + tab-show, post-handshake) so it appears
        // reliably even if the panel was constructed during the handshake window; hidden on older servers.
        _beelzBestiaryCurationNote = AddBodyText(headerCard,
            "<color=#FFD9A0>Note:</color> abilities the server admins mark <color=#FF8080>Blocked</color>/" +
            "<color=#FF8080>Hidden</color> are curated out of the collectible set, so the 100% target counts " +
            "only shippable abilities. If your collection total dropped after a server update, that's the curated " +
            "list tightening — not lost captures.");
        _beelzBestiaryCurationNote.gameObject.SetActive(BeelzState.SupportsReviewMeta);

        AddSpacer(page, 6);

        var statusCard = AddCard(page, "BeelzBestiaryStatusCard");

        // Stats on their OWN full-width line. Pre-scan the message is long ("Abilities N captured (X%)
        // · Scan all for the full list") and, because info labels render single-line with overflow (not
        // clipped), it used to bleed rightward over the Refresh / Leaderboard / My-odds buttons until a
        // scan shortened it. On its own line any overflow runs into empty space, never the buttons.
        _beelzBestiaryStatsLabel = AddInfoLabel(statusCard, "BeelzBestiaryStats", "—",
            FontStyles.Bold, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(_beelzBestiaryStatsLabel.gameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);

        var statusRow = UIFactory.CreateHorizontalGroup(statusCard, "BeelzBestiaryStatusRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 2, 0, 0));
        UIFactory.SetLayoutElement(statusRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(30), preferredHeight: Theme.ScaledHeight(32), flexibleHeight: 0);

        AddBeelzRefreshButton(statusRow,
            "Re-sync captures + progress from the server (api list / progress).",
            () => { BeelzClient.RequestList(); BeelzClient.RequestProgress(); });
        // Beelz v0.83 player extras — reply in chat (the player reads them there).
        AddBeelzSmallButton(statusRow, "BeelzTop", "Leaderboard",
            "Show the server collection leaderboard in chat (.beelz top).", BeelzClient.Top, 86);
        AddBeelzSmallButton(statusRow, "BeelzOdds", "My odds",
            "Show your live ability / Devour drop chances + pity in chat (.beelz odds).", BeelzClient.Odds, 70);
        // (Clear all moved to a typed-CONFIRM "Danger zone" card at the bottom — see below.)

        // Scan all — load the full collectible ability list (heavy; user-controlled).
        AddBeelzScanAllButton(page, BeelzScanTarget.Bestiary);

        AddSpacer(page, 6);

        var filterCard = AddCard(page, "BeelzBestiaryFilterCard");
        var filterRow = UIFactory.CreateHorizontalGroup(filterCard, "BeelzBestiaryFilterRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(filterRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(30), preferredHeight: Theme.ScaledHeight(32), flexibleHeight: 0);

        _beelzBestiarySearchInput = UIFactory.CreateInputField(filterRow, "BeelzBestiarySearch", "Filter by ability name…");
        UIFactory.SetLayoutElement(_beelzBestiarySearchInput.GameObject,
            minWidth: 140, preferredWidth: 200, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        _beelzBestiarySearchInput.OnValueChanged += (string val) =>
        {
            _beelzBestiarySearch = val ?? "";
            _beelzBestiaryPage = 0;
            RebuildBeelzBestiaryRows();
        };

        _beelzBestiaryFilterButton = UIFactory.CreateButton(filterRow, "BeelzBestiaryFilterBtn", FormatBeelzBestiaryFilter());
        UIFactory.SetLayoutElement(_beelzBestiaryFilterButton.GameObject,
            minWidth: 108, preferredWidth: 124, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        var filterTxt = _beelzBestiaryFilterButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (filterTxt != null) { filterTxt.fontSize = Theme.ScaledUI(12); filterTxt.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(_beelzBestiaryFilterButton.GameObject, "Cycle: All → Captured → Missing.");
        _beelzBestiaryFilterButton.OnClick = () =>
        {
            _beelzBestiaryFilter = _beelzBestiaryFilter switch
            {
                BeelzBestiaryFilter.All      => BeelzBestiaryFilter.Captured,
                BeelzBestiaryFilter.Captured => BeelzBestiaryFilter.Missing,
                _                            => BeelzBestiaryFilter.All,
            };
            SetBeelzButtonText(_beelzBestiaryFilterButton, FormatBeelzBestiaryFilter());
            _beelzBestiaryPage = 0;
            RebuildBeelzBestiaryRows();
        };
        _beelzBestiaryCategoryButton = AddBeelzSmallButton(filterRow, "BeelzBestiaryCat", FormatBeelzBestiaryCategory(),
            "Cycle category (built from the scanned catalog / your captures).",
            () => { CycleBeelzBestiaryCategory(); _beelzBestiaryPage = 0; SetBeelzButtonText(_beelzBestiaryCategoryButton, FormatBeelzBestiaryCategory()); RebuildBeelzBestiaryRows(); }, 116);
        _beelzBestiaryKindButton = AddBeelzSmallButton(filterRow, "BeelzBestiaryKind", FormatBeelzBestiaryKind(),
            "Cycle kind: All → Magic → Weapon → Form (needs Scan all).",
            () => { _beelzBestiaryKind = _beelzBestiaryKind switch { BeelzAbilKind.All => BeelzAbilKind.Magic, BeelzAbilKind.Magic => BeelzAbilKind.Weapon, BeelzAbilKind.Weapon => BeelzAbilKind.Form, _ => BeelzAbilKind.All };
                    _beelzBestiaryPage = 0; SetBeelzButtonText(_beelzBestiaryKindButton, FormatBeelzBestiaryKind()); RebuildBeelzBestiaryRows(); }, 104);
        _beelzBestiaryGroupButton = AddBeelzSmallButton(filterRow, "BeelzBestiaryGroup", FormatBeelzBestiaryGroup(),
            "Group by None / Category / Kind / Unit / Status (Captured vs Missing). Click a group header to collapse it. " +
            "Note: uncaptured abilities show their owning unit only if your Beelzebub build streams it on scan (unit=); otherwise Unit grouping buckets them under \"(uncaptured)\".",
            () => { CycleBeelzBestiaryGroup(); _beelzBestiaryPage = 0; SetBeelzButtonText(_beelzBestiaryGroupButton, FormatBeelzBestiaryGroup()); RebuildBeelzBestiaryRows(); }, 116);

        // #5: pagination controls (the list pages instead of capping at 200).
        var pageRow = UIFactory.CreateHorizontalGroup(filterCard, "BeelzBestiaryPageRow",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 2, 0, 0));
        UIFactory.SetLayoutElement(pageRow, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        _beelzBestiaryPrevButton = AddBeelzSmallButton(pageRow, "BeelzBestiaryPrev", "Prev",
            "Previous page.", () => { if (_beelzBestiaryPage > 0) { _beelzBestiaryPage--; RebuildBeelzBestiaryRows(); } }, 64);
        _beelzBestiaryPageLabel = AddInfoLabel(pageRow, "BeelzBestiaryPageLbl", "", FontStyles.Normal, Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(_beelzBestiaryPageLabel.gameObject, minWidth: 140, preferredWidth: 200, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        _beelzBestiaryPageLabel.alignment = TextAlignmentOptions.Center;
        _beelzBestiaryNextButton = AddBeelzSmallButton(pageRow, "BeelzBestiaryNext", "Next",
            "Next page.", () => { _beelzBestiaryPage++; RebuildBeelzBestiaryRows(); }, 64);
        // F12: collapse/expand all groups (only meaningful when a Group mode is active).
        AddBeelzSmallButton(pageRow, "BeelzBestiaryCollapseAll", "Collapse all",
            "Collapse every group (when grouping is on).",
            () => { foreach (var k in _beelzBestiaryLastGroupKeys) _beelzBestiaryCollapsed.Add(k); _beelzBestiaryPage = 0; RebuildBeelzBestiaryRows(); }, 92);
        AddBeelzSmallButton(pageRow, "BeelzBestiaryExpandAll", "Expand all",
            "Expand every group.",
            () => { _beelzBestiaryCollapsed.Clear(); _beelzBestiaryPage = 0; RebuildBeelzBestiaryRows(); }, 84);

        AddSpacer(page, 6);

        var rowsCard = AddCard(page, "BeelzBestiaryRowsCard", padding: 4, innerSpacing: 2);
        _beelzBestiaryRowContainer = UIFactory.CreateVerticalGroup(rowsCard, "BeelzBestiaryRows",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(_beelzBestiaryRowContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 60, flexibleHeight: 0);

        // Danger zone at the very bottom — typed CONFIRM gate (see helper).
        AddBeelzClearAllConfirm(page);

        RebuildBeelzBestiaryRows();
        RefreshBeelzBestiaryHeader();

        // Auto-pull captures + progress on first open (cheap); the full list needs Scan all.
        if (BeelzState.Present && BeelzState.Captures.Count == 0)
        { BeelzClient.RequestList(); BeelzClient.RequestProgress(); }
    }

    // Clear-all gated behind TYPED confirmation (not a fast double-click): the user must
    // type "confirm" into the box before the Wipe button does anything. On top of that the
    // command itself carries Beelzebub's literal CONFIRM token (server-enforced). This makes
    // an accidental wipe effectively impossible.
    private void AddBeelzClearAllConfirm(GameObject page)
    {
        AddSpacer(page, 8);
        var card = AddCard(page, "BeelzClearAllCard");
        AddSectionHeading(card, "Danger zone — clear all captures");
        AddBodyText(card,
            "<color=#FF8080>This permanently deletes EVERY captured ability and ALL slot bindings (your " +
            "Dracula/Morgana transform unlocks are kept). It cannot be undone.</color> To avoid accidents you " +
            "must type <b>confirm</b> in the box, then click Wipe — a stray click does nothing.");

        var row = MakeBeelzRow(card, "BeelzClearAllRow");
        string typed = "";
        var input = UIFactory.CreateInputField(row, "BeelzClearAllInput", "type confirm");
        UIFactory.SetLayoutElement(input.GameObject, minWidth: 120, preferredWidth: 150, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        input.OnValueChanged += (string v) => typed = v ?? "";

        var status = AddInfoLabel(row, "BeelzClearAllStatus", "", FontStyles.Italic, Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(status.gameObject, minWidth: 120, preferredWidth: 180, flexibleWidth: 1, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);

        AddBeelzSmallButton(row, "BeelzClearAllWipe", "Wipe abilities",
            "Permanently delete ALL captures + slot bindings. Disabled until you type 'confirm' in the box.",
            () =>
            {
                if (!string.Equals((typed ?? "").Trim(), "confirm", StringComparison.OrdinalIgnoreCase))
                { status.text = "<color=#FFB070>Type 'confirm' in the box first.</color>"; return; }
                BeelzClient.ClearAll();
                status.text = "<color=#90EE90>Wipe sent.</color>";
            },
            110, new Color(0.6f, 0.12f, 0.12f));
    }

    // One rendered checklist row.
    private readonly struct BeelzChecklistRow
    {
        public readonly string Name, Category, Kind, Unit, CreatureType, AbilityGuid;
        public readonly bool Captured;
        public BeelzChecklistRow(string name, string category, string kind, bool captured, string unit, string creatureType, string abilityGuid = "")
        { Name = name; Category = category; Kind = kind; Captured = captured; Unit = unit; CreatureType = creatureType; AbilityGuid = abilityGuid; }
    }

    private List<string> BeelzBestiaryCategories()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (BeelzState.CatalogLoaded)
        {
            foreach (var c in BeelzState.CatalogAbilities.Values)
                if (c.Enabled && !string.IsNullOrEmpty(c.Category)) set.Add(c.Category);
        }
        else
        {
            foreach (var c in BeelzState.Captures)
                if (!string.IsNullOrEmpty(c.Category)) set.Add(c.Category);
        }
        return new List<string>(set);
    }
    private void CycleBeelzBestiaryCategory()
    {
        var cats = BeelzBestiaryCategories();
        if (cats.Count == 0) { _beelzBestiaryCategory = ""; return; }
        int idx = string.IsNullOrEmpty(_beelzBestiaryCategory)
            ? -1 : cats.FindIndex(x => x.Equals(_beelzBestiaryCategory, StringComparison.OrdinalIgnoreCase));
        idx++;
        _beelzBestiaryCategory = idx >= cats.Count ? "" : cats[idx];
    }

    private string FormatBeelzBestiaryFilter() => _beelzBestiaryFilter switch
    {
        BeelzBestiaryFilter.Captured => "Filter: Captured",
        BeelzBestiaryFilter.Missing  => "Filter: Missing",
        _                            => "Filter: All",
    };

    private string FormatBeelzBestiaryGroup() => $"Group: {(_beelzBestiaryGroupMode.Length == 0 ? "None" : _beelzBestiaryGroupMode)}";
    private void CycleBeelzBestiaryGroup()
    {
        _beelzBestiaryGroupMode = _beelzBestiaryGroupMode switch
        {
            "" => "Category", "Category" => "Kind", "Kind" => "Unit", "Unit" => "Status", _ => "",
        };
        _beelzBestiaryCollapsed.Clear();   // a different grouping has different keys
    }

    private void RefreshBeelzBestiaryHeader()
    {
        if (!BeelzTabVisible(PanelType.BeelzBestiaryTab)) return;
        if (_beelzBestiaryStatsLabel == null) return;
        // (d) api25 curation note — toggle here (post-handshake refresh path) so it appears once the api
        // version is known even if the page was built during the handshake window.
        if (_beelzBestiaryCurationNote != null) _beelzBestiaryCurationNote.gameObject.SetActive(BeelzState.SupportsReviewMeta);
        if (!BeelzState.Present) { _beelzBestiaryStatsLabel.text = "Beelzebub not detected"; return; }

        var p = BeelzState.Progress;
        string tx = p != null ? $"  ·  Transforms {p.TransformsUnlocked}/{p.TransformsTotal}" : "";

        if (BeelzState.CatalogLoaded)
        {
            // Compute the collector % CLIENT-side against the enabled catalog so it can't
            // exceed 100% (the server's api progress counts can — e.g. V/R duplicates or
            // abilities no longer in the enabled set). Captured = enabled catalog entries
            // whose name matches one of your captures.
            var capNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in BeelzState.Captures)
                if (!string.IsNullOrEmpty(c.AbilityName)) capNames.Add(c.AbilityName);
            int total = BeelzState.CatalogEnabledCount, captured = 0;
            foreach (var c in BeelzState.CatalogAbilities.Values)
                if (c.Enabled && capNames.Contains(c.Name)) captured++;
            int pct = total > 0 ? Math.Min(100, (int)Math.Round(captured * 100.0 / total)) : 0;
            // Captures that aren't in the curated catalog (inclusive-capture extras) — shown as
            // bonus ✓ rows but kept OUT of the % so it stays honest against the curated target.
            int offCatalog = 0;
            foreach (var n in capNames) if (!BeelzState.TryGetCatalog(n, out _)) offCatalog++;
            string off = offCatalog > 0 ? $"  ·  +{offCatalog} off-catalog" : "";
            _beelzBestiaryStatsLabel.text = $"Abilities {captured}/{total} ({pct}%){off}{tx}";
        }
        else if (p != null)
        {
            // Pre-scan: use the server's count, clamped to 100% defensively.
            int pct = Math.Min(100, (int)Math.Round(p.AbilitiesPct));
            _beelzBestiaryStatsLabel.text = $"Abilities {p.AbilitiesCaptured} captured ({pct}%)  ·  Scan all for the full list{tx}";
        }
        else
        {
            _beelzBestiaryStatsLabel.text = $"{BeelzState.Captures.Count} abilities captured  ·  Scan all for the full list";
        }
    }

    private void RebuildBeelzBestiaryRows()
    {
        if (!BeelzTabVisible(PanelType.BeelzBestiaryTab)) return;
        if (_beelzBestiaryRowContainer == null) return;
        _beelzBestiaryDirty = false;   // building now → clean until the next data change
        ClearChildren(_beelzBestiaryRowContainer);

        if (!BeelzState.Present) { AddSimpleRow(_beelzBestiaryRowContainer, "(Beelzebub not detected on this server)", italic: true); return; }

        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
        string search = (_beelzBestiarySearch ?? "").Trim();

        // captured ability name → its capture (for the ✓ flag + unit/creature-type display).
        var capByName = new Dictionary<string, BeelzCapture>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in BeelzState.Captures)
            if (!string.IsNullOrEmpty(c.AbilityName) && !capByName.ContainsKey(c.AbilityName)) capByName[c.AbilityName] = c;

        // Source set: the full enabled catalog once scanned, else just the player's captures.
        var rows = new List<BeelzChecklistRow>();
        if (BeelzState.CatalogLoaded)
        {
            foreach (var c in BeelzState.CatalogAbilities.Values)
            {
                if (!c.Enabled) continue;
                bool captured = capByName.TryGetValue(c.Name, out var cap);
                // Unit: from the capture when captured; otherwise from the scanned catalog (Beelz emits
                // unit=/unitguid= on catalog-ability) so UNCAPTURED abilities group by their owner too —
                // no longer all bucketed under "(uncaptured)". Falls back to "" only when neither has it.
                string rowUnit = captured ? BeelzUnitName(cap.UnitGuid, cap.UnitName)
                               : !string.IsNullOrEmpty(c.Unit) ? BeelzUnitName(c.UnitGuid, c.Unit) : "";
                rows.Add(new BeelzChecklistRow(c.Name, c.Category, BeelzState.AbilityKind(c.Name), captured,
                    rowUnit, captured ? cap.UnitType : "",
                    captured ? cap.AbilityGuid : c.AbilityGuid));
            }
            // Union in captures that aren't in the curated catalog at all. Under the default
            // Capture_InclusiveMode you can capture abilities outside Beelzebub's curated AbilityMap
            // (which is all `api catalog abilities` exposes). Without this they'd show pre-scan, then
            // VANISH after Scan all (the catalog loop above only lists curated entries) and never read
            // as ✓. They're flagged "off-catalog" in the row; the header %% stays against the catalog.
            foreach (var cap in capByName.Values)
                if (!BeelzState.TryGetCatalog(cap.AbilityName, out _))
                    rows.Add(new BeelzChecklistRow(cap.AbilityName, cap.Category, BeelzState.AbilityKind(cap.AbilityName),
                        true, BeelzUnitName(cap.UnitGuid, cap.UnitName), cap.UnitType, cap.AbilityGuid));
        }
        else
        {
            foreach (var cap in capByName.Values)
                rows.Add(new BeelzChecklistRow(cap.AbilityName, cap.Category, BeelzState.AbilityKind(cap.AbilityName), true,
                    BeelzUnitName(cap.UnitGuid, cap.UnitName), cap.UnitType, cap.AbilityGuid));
        }

        var shown = new List<BeelzChecklistRow>();
        foreach (var r in rows)
        {
            if (_beelzBestiaryFilter == BeelzBestiaryFilter.Captured && !r.Captured) continue;
            if (_beelzBestiaryFilter == BeelzBestiaryFilter.Missing  &&  r.Captured) continue;
            if (!string.IsNullOrEmpty(_beelzBestiaryCategory) && !_beelzBestiaryCategory.Equals(r.Category, OIC)) continue;
            if (!BeelzKindMatches(r.Name, _beelzBestiaryKind)) continue;
            if (search.Length > 0)
            {
                string hay = BeelzNames.Ability(r.Name) + " " + r.Name + " " + r.Unit + " " + r.Category + " " + r.Kind + " " + r.CreatureType;
                if (BeelzState.TryGetCatalog(r.Name, out var rc))
                {
                    if (rc.Weapons != null) hay += " " + string.Join(" ", rc.Weapons);
                    if (rc.Forms   != null) hay += " " + string.Join(" ", rc.Forms);
                }
                if (hay.IndexOf(search, OIC) < 0) continue;
            }
            shown.Add(r);
        }
        // #4/F12: group-by key (None/Category/Kind/Unit/Status). Sort by group then name when grouping.
        string GroupKey(BeelzChecklistRow r) => _beelzBestiaryGroupMode switch
        {
            "Category" => string.IsNullOrEmpty(r.Category) ? "—" : r.Category,
            "Kind"     => string.IsNullOrEmpty(r.Kind)     ? "—" : r.Kind,
            "Unit"     => string.IsNullOrEmpty(r.Unit)     ? "(uncaptured)" : r.Unit,
            "Status"   => r.Captured ? "Captured" : "Missing",
            _          => "",
        };
        if (_beelzBestiaryGroupMode.Length > 0)
            shown.Sort((a, b) => { int c = string.Compare(GroupKey(a), GroupKey(b), OIC); return c != 0 ? c : string.Compare(BeelzNames.Ability(a.Name), BeelzNames.Ability(b.Name), OIC); });
        else
            shown.Sort((a, b) => string.Compare(BeelzNames.Ability(a.Name), BeelzNames.Ability(b.Name), OIC));

        if (shown.Count == 0)
        {
            AddSimpleRow(_beelzBestiaryRowContainer,
                BeelzState.Captures.Count == 0 && !BeelzState.CatalogLoaded
                    ? "(nothing captured yet — kill units to collect, or Scan all to see the full list)"
                    : "(no abilities match this filter)", italic: true);
            _beelzBestiaryLastGroupKeys.Clear();
            UpdateBestiaryPageLabel(0, 0, 0);
            return;
        }

        // F12: build a flat list of render items — a group HEADER per group (collapsible), then its rows
        // unless the group is collapsed (collapsed groups contribute only their header). Then paginate the
        // item list, so collapse + pagination compose cleanly.
        var items = new List<(bool IsHeader, string GroupKey, int GroupCount, BeelzChecklistRow Row)>();
        _beelzBestiaryLastGroupKeys.Clear();
        if (_beelzBestiaryGroupMode.Length > 0)
        {
            int gi = 0;
            while (gi < shown.Count)
            {
                string gk = GroupKey(shown[gi]);
                int gj = gi; while (gj < shown.Count && string.Equals(GroupKey(shown[gj]), gk, OIC)) gj++;
                _beelzBestiaryLastGroupKeys.Add(gk);
                items.Add((true, gk, gj - gi, default));
                if (!_beelzBestiaryCollapsed.Contains(gk))
                    for (int k = gi; k < gj; k++) items.Add((false, gk, 0, shown[k]));
                gi = gj;
            }
        }
        else
        {
            foreach (var r in shown) items.Add((false, "", 0, r));
        }

        // #5: paginate the item list (BEELZ_BESTIARY_PAGE per page) so the full 1000+ catalog is browsable.
        int pageCount = Math.Max(1, (items.Count + BEELZ_BESTIARY_PAGE - 1) / BEELZ_BESTIARY_PAGE);
        _beelzBestiaryPage = Math.Clamp(_beelzBestiaryPage, 0, pageCount - 1);
        int start = _beelzBestiaryPage * BEELZ_BESTIARY_PAGE;
        int end = Math.Min(items.Count, start + BEELZ_BESTIARY_PAGE);
        UpdateBestiaryPageLabel(_beelzBestiaryPage + 1, pageCount, shown.Count);

        AddBeelzBestiaryColumnHeader();
        for (int i = start; i < end; i++)
        {
            var it = items[i];
            if (it.IsHeader) AddBeelzBestiaryGroupHeader(it.GroupKey, it.GroupCount);
            else BuildBeelzChecklistRow(it.Row);
        }
    }

    // F12: a clickable group divider that toggles collapse for its group key.
    private void AddBeelzBestiaryGroupHeader(string gk, int count)
    {
        bool collapsed = _beelzBestiaryCollapsed.Contains(gk);
        var btn = UIFactory.CreateButton(_beelzBestiaryRowContainer, $"BeelzBestGrp_{gk}", $"{(collapsed ? "[+]" : "[–]")}  {gk}  ({count})");
        UIFactory.SetLayoutElement(btn.GameObject, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(11); t.alignment = TextAlignmentOptions.MidlineLeft; t.color = new Color(0.62f, 0.82f, 1f); t.fontStyle = FontStyles.Bold; }
        TooltipHover.Attach(btn.GameObject, "Click to collapse / expand this group.");
        string gkCopy = gk;
        btn.OnClick = () => { if (!_beelzBestiaryCollapsed.Remove(gkCopy)) _beelzBestiaryCollapsed.Add(gkCopy); RebuildBeelzBestiaryRows(); };
    }

    private void UpdateBestiaryPageLabel(int page1, int pageCount, int total)
    {
        if (_beelzBestiaryPageLabel == null) return;
        _beelzBestiaryPageLabel.text = total == 0 ? "—" : $"Page {page1} / {pageCount}  ·  {total} shown";
    }

    // Tabular column header for the Bestiary (matches BuildBeelzChecklistRow's columns).
    private void AddBeelzBestiaryColumnHeader()
    {
        var row = MakeBeelzRow(_beelzBestiaryRowContainer, "BeelzBestiaryColHdr");
        void Col(string t, int min, int pref, int flex)
        {
            var l = UIFactory.CreateLabel(row, $"BeelzBestCol_{t}", $"<color={Theme.MutedBodyHex}><b>{t}</b></color>",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(l.GameObject, minWidth: min, preferredWidth: pref, flexibleWidth: flex, minHeight: Theme.ScaledHeight(16), preferredHeight: Theme.ScaledHeight(18), flexibleHeight: 0);
            l.TextMesh.enableWordWrapping = false; l.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        }
        Col("",        20, 20, 0);
        Col("Ability", BEELZ_COL_ABILITY_MIN, BEELZ_COL_ABILITY_PREF, 1);
        Col("Cat",     BEELZ_COL_CAT_MIN,     BEELZ_COL_CAT_PREF,     0);
        Col("Kind",    52, 64, 0);
        Col("Unit",    BEELZ_COL_UNIT_MIN,    BEELZ_COL_UNIT_PREF,    1);
        if (Config.Settings.BeelzDiagnostics) { Col("ID", BEELZ_COL_ID_MIN, BEELZ_COL_ID_PREF, 0); Col("", 46, 46, 0); }
    }

    // One checklist row, tabular: ✓/– | Ability | Category | Kind | Unit. Full detail on hover.
    private void BuildBeelzChecklistRow(BeelzChecklistRow r)
    {
        var row = MakeBeelzRow(_beelzBestiaryRowContainer, $"BeelzChk_{r.Name}");
        void Col(string name, string text, int min, int pref, int flex, int fs)
        {
            var l = UIFactory.CreateLabel(row, name, text, TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(fs));
            UIFactory.SetLayoutElement(l.GameObject, minWidth: min, preferredWidth: pref, flexibleWidth: flex, minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
            l.TextMesh.enableWordWrapping = false; l.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        }
        bool offCat = r.Captured && BeelzState.CatalogLoaded && !BeelzState.TryGetCatalog(r.Name, out _);
        string status = r.Captured ? "<color=#90EE90>✓</color>" : $"<color={Theme.MutedBodyHex}>–</color>";
        string nm = (r.Captured ? BeelzNames.Ability(r.Name) : $"<color={Theme.MutedBodyHex}>{BeelzNames.Ability(r.Name)}</color>")
                    + (offCat ? "  <color=#9FD0FF>(off-cat)</color>" : "");
        Col("Chk",  status, 20, 20, 0, 12);
        Col("Abil", nm, BEELZ_COL_ABILITY_MIN, BEELZ_COL_ABILITY_PREF, 1, 12);
        Col("Cat",  string.IsNullOrEmpty(r.Category) ? "—" : $"<color={Theme.MutedBodyHex}>{r.Category}</color>", BEELZ_COL_CAT_MIN, BEELZ_COL_CAT_PREF, 0, 11);
        Col("Kind", string.IsNullOrEmpty(r.Kind) ? "" : $"<color={Theme.MutedBodyHex}>{r.Kind}</color>", 52, 64, 0, 11);
        Col("Unit", string.IsNullOrEmpty(r.Unit) ? "" : $"<color={Theme.MutedBodyHex}>{r.Unit}</color>", BEELZ_COL_UNIT_MIN, BEELZ_COL_UNIT_PREF, 1, 11);
        // #F11: diagnostics — click-to-copy ID + a full-detail Copy, matching the loadout. The ID is only
        // known for captured abilities (the catalog carries no guid for uncaptured rows → "—").
        if (Config.Settings.BeelzDiagnostics)
        {
            AddBeelzIdCopyCell(row, r.AbilityGuid);
            var rr = r;
            AddBeelzSmallButton(row, $"BeelzChkCopy_{r.Name}", "Copy",
                "Copy this ability's details to the clipboard (name, ID, category/kind, unit). Diagnostics-only.",
                () => CopyBeelzChecklistDetails(rr), 46);
        }
        // Creature type + off-catalog detail on hover (kept off the row to save width).
        if (!string.IsNullOrEmpty(r.CreatureType))
            TooltipHover.Attach(row, $"{BeelzNames.Ability(r.Name)} — {(r.Captured ? "captured" : "missing")}" +
                $"{(string.IsNullOrEmpty(r.Unit) ? "" : $" · from {r.Unit}")}{(string.IsNullOrEmpty(r.CreatureType) ? "" : $" · {r.CreatureType}")}");
    }

    // #F11: plain-text dump of a Bestiary row for the clipboard (bug reports / testing docs).
    private void CopyBeelzChecklistDetails(BeelzChecklistRow r)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Ability: ").Append(BeelzNames.Ability(r.Name)).Append("  (prefab ").Append(r.Name).Append(")\n");
        if (!string.IsNullOrEmpty(r.AbilityGuid)) sb.Append("ID: ").Append(r.AbilityGuid).Append('\n');
        sb.Append("Status: ").Append(r.Captured ? "Captured" : "Missing").Append('\n');
        if (!string.IsNullOrEmpty(r.Category))     sb.Append("Category: ").Append(r.Category).Append('\n');
        if (!string.IsNullOrEmpty(r.Kind))         sb.Append("Kind: ").Append(r.Kind).Append('\n');
        if (!string.IsNullOrEmpty(r.Unit))         sb.Append("Unit: ").Append(r.Unit).Append('\n');
        if (!string.IsNullOrEmpty(r.CreatureType)) sb.Append("Creature: ").Append(r.CreatureType).Append('\n');
        try { UnityEngine.GUIUtility.systemCopyBuffer = sb.ToString(); } catch { }
    }

    // ============================ LOADOUT ============================

    private void BuildBeelzLoadoutTab(GameObject page)
    {
        EnsureBeelzSubscribed();
        if (AddBeelzAbsentNote(page)) return;

        var card = AddCard(page, "BeelzLoadoutHeaderCard");
        AddSectionHeading(card, "Loadout — ability sets");
        AddBodyText(card,
            "Build a separate ability set per weapon, on the 6 bar slots: <b>1</b> Primary (Q/left-click) · " +
            "<b>2</b> Travel (Space) · <b>3</b> Shift · <b>4</b> Heavy (E) · <b>5</b> Spell 1 (R) · <b>6</b> Spell 2 (C). " +
            "(No separate ultimate slot — slot 1 is the primary attack.)");
        AddBodyText(card,
            "<b>Universal</b> is the basic / fallback set — it applies on any weapon. A <b>per-weapon</b> set overrides " +
            "Universal on the slots you fill, and the server switches to it automatically when you equip that weapon. " +
            "Empty slots keep your normal in-game abilities. Pick a set below, then click a slot number on a captured " +
            "ability to bind it — you can edit a weapon's set even while it's not in your hands.");
        AddBodyText(card,
            "<color=#FFB070>Note:</color> a weapon-tagged ability only appears on the live bar while you're wielding its " +
            "matching weapon (and not transformed). If a bind doesn't show, check chat for a ✋ weapon hint, equip that " +
            $"weapon, then {Mono(".beelz refresh")} (the Fix bar button).");

        var actionRow = UIFactory.CreateHorizontalGroup(page, "BeelzLoadoutActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 4, 0, 0));
        AddBeelzRefreshButton(actionRow, "Re-sync slot bindings + captures (api slots / list).",
            () => { BeelzClient.RequestSlots(); BeelzClient.RequestList(); });
        AddBeelzConfirmButton(actionRow, "BeelzResetBar", "Clear bar",
            "Clear EVERY set's slot bindings (universal + all weapons + all forms) → vanilla bar (keeps your captures/unlocks). .beelz clearbar.",
            () => BeelzClient.ClearBar(), 86, new Color(0.55f, 0.30f, 0.18f));
        AddBeelzSmallButton(actionRow, "BeelzRefreshBar", "Fix bar",
            "Re-apply the correct spell bar if it looks blank/wrong (.beelz refresh).", BeelzClient.RefreshBar, 70);
        // Beelz v0.137 / api33: `.beelz resetbar CONFIRM` is one layered reset (saved binds -> equip rows -> form
        // sources -> engine mods -> weapon skills re-applied); spells leave the bar at once and spellbook spells stay.
        // It replaces the old revert -> resetbar pair. Two-click confirm: it clears every saved bind.
        if (BeelzState.SupportsLayeredBarReset)
        {
            AddBeelzConfirmButton(actionRow, "BeelzUnstickBar", "Reset bar",
                "Complete bar reset (.beelz resetbar CONFIRM): clears every saved bind and puts your weapon's own skills " +
                "back, with no weapon swap. Keeps your captures, unlocks and the spells you picked in the spellbook (J). " +
                "Still stuck? Ask an admin to run Diagnose bar (Beelzebub → Admin: Players → Recovery).",
                BeelzClient.ResetBar, 86, new Color(0.55f, 0.30f, 0.18f));
        }
        else
        {
            // Older servers: recovery for a bar STUCK on a transformation's abilities after the transform ended.
            // Reverts any active transform (no-op if not transformed), then on Beelz v0.120+ runs the AUTHORITATIVE
            // `.beelz resetbar` (a plain `.beelz refresh` can't clear the engine's cached slot values). Pre-0.120
            // servers fall back to refresh.
            AddBeelzSmallButton(actionRow, "BeelzUnstickBar", "Unstick bar",
                "Stuck showing a transformation's abilities after it ended? This reverts any active transform, then " +
                "(Beelz v0.120+) authoritatively clears the engine's cached slot values and RE-APPLIES your saved " +
                "loadout (.beelz revert → .beelz resetbar) — a plain Fix bar can't clear a deeply-stuck slot. On " +
                "older servers it falls back to .beelz refresh. If a bar SURVIVES a relog, the guaranteed cure is an " +
                "admin Respawn (Beelzebub → Admin: Players → Recovery).",
                () =>
                {
                    BeelzClient.Revert();
                    if (BeelzState.SupportsAuthoritativeBarReset) BeelzClient.ResetBar();
                    else                                          BeelzClient.RefreshBar();
                }, 90);
        }

        // Scan all (loads catalog metadata → enables the Kind filter + row columns).
        AddBeelzScanAllButton(page, BeelzScanTarget.Loadout);

        // Group selector + group-level actions (built once; persists across slot rebuilds).
        BuildBeelzGroupSelector(page);

        AddSpacer(page, 6);
        var slotsCard = AddCard(page, "BeelzLoadoutSlotsCard", padding: 4, innerSpacing: 2);
        AddSectionHeading(slotsCard, "Slots in this set");
        _beelzLoadoutRowContainer = UIFactory.CreateVerticalGroup(slotsCard, "BeelzLoadoutSlotRows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(_beelzLoadoutRowContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 60, flexibleHeight: 0);

        AddSpacer(page, 6);
        var assignCard = AddCard(page, "BeelzLoadoutAssignCard", padding: 4, innerSpacing: 2);
        AddSectionHeading(assignCard, "Assign a captured ability");

        // Filter controls. Grouping (how the list is STRUCTURED into collapsible groups) is now visually
        // separated from filters (what the list is NARROWED to): a full-width search line, then a labeled
        // "Group" row, then a labeled "Filter" row — instead of mixing search + grouping on one row.
        var searchRow = UIFactory.CreateHorizontalGroup(assignCard, "BeelzAssignSearchRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(searchRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var searchInput = UIFactory.CreateInputField(searchRow, "BeelzAssignSearch", "Search name / unit / weapon / category…");
        UIFactory.SetLayoutElement(searchInput.GameObject,
            minWidth: 200, preferredWidth: 360, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        searchInput.OnValueChanged += (string v) => { _beelzAssignSearch = v ?? ""; RebuildBeelzLoadoutAssign(); };

        // Grouping row — structure the list into collapsible groups.
        var groupRow = UIFactory.CreateHorizontalGroup(assignCard, "BeelzAssignGroupRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(groupRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        AddBeelzControlLabel(groupRow, "BeelzAssignGroupLbl", "Group:");
        _beelzAssignGroupButton = AddBeelzSmallButton(groupRow, "BeelzAssignGroup", FormatBeelzAssignGroup(),
            "Cycle grouping: off → Unit → Category → Kind → Weapon. A weapon-shared ability appears under EACH of its " +
            "weapon families. Each row still shows unit · category · kind · creature-type. Weapon/Kind need Scan all.",
            () => { _beelzAssignGroupMode = _beelzAssignGroupMode switch { BeelzGroupMode.None => BeelzGroupMode.Unit, BeelzGroupMode.Unit => BeelzGroupMode.Category, BeelzGroupMode.Category => BeelzGroupMode.Kind, BeelzGroupMode.Kind => BeelzGroupMode.Weapon, _ => BeelzGroupMode.None };
                    _beelzCollapsedGroups.Clear(); SetBeelzButtonText(_beelzAssignGroupButton, FormatBeelzAssignGroup()); RebuildBeelzLoadoutAssign(); }, 120);
        AddBeelzSmallButton(groupRow, "BeelzAssignExpandAll", "Expand all",
            "Expand every group (only when grouping is on).", () => { _beelzCollapsedGroups.Clear(); RebuildBeelzLoadoutAssign(); }, 82);
        AddBeelzSmallButton(groupRow, "BeelzAssignCollapseAll", "Collapse all",
            "Collapse every group (only when grouping is on).", () => { foreach (var k in _beelzLastAssignGroupKeys) _beelzCollapsedGroups.Add(k); RebuildBeelzLoadoutAssign(); }, 92);

        // Filter row — narrow the list by source / category / kind.
        var filterRow2 = UIFactory.CreateHorizontalGroup(assignCard, "BeelzAssignFilter2",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(filterRow2,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        AddBeelzControlLabel(filterRow2, "BeelzAssignFilterLbl", "Filter:");
        _beelzAssignSourceButton = AddBeelzSmallButton(filterRow2, "BeelzAssignSrc", FormatBeelzAssignSource(),
            "Cycle source: All → V-Blood → Regular.",
            () => { _beelzAssignSource = _beelzAssignSource switch { BeelzAbilSource.All => BeelzAbilSource.VBlood, BeelzAbilSource.VBlood => BeelzAbilSource.Regular, _ => BeelzAbilSource.All };
                    SetBeelzButtonText(_beelzAssignSourceButton, FormatBeelzAssignSource()); RebuildBeelzLoadoutAssign(); }, 110);
        _beelzAssignCategoryButton = AddBeelzSmallButton(filterRow2, "BeelzAssignCat", FormatBeelzAssignCategory(),
            "Cycle category (Summon / Spell / Projectile / Aoe / Buff / Travel / WeaponSpell / …) — built from your captures.",
            () => { CycleBeelzAssignCategory(); SetBeelzButtonText(_beelzAssignCategoryButton, FormatBeelzAssignCategory()); RebuildBeelzLoadoutAssign(); }, 128);
        _beelzAssignKindButton = AddBeelzSmallButton(filterRow2, "BeelzAssignKind", FormatBeelzAssignKind(),
            "Cycle kind: All → Magic → Weapon (weapon-bound) → Form (form-restricted). Needs Scan all (catalog metadata).",
            () => { _beelzAssignKind = _beelzAssignKind switch { BeelzAbilKind.All => BeelzAbilKind.Magic, BeelzAbilKind.Magic => BeelzAbilKind.Weapon, BeelzAbilKind.Weapon => BeelzAbilKind.Form, _ => BeelzAbilKind.All };
                    SetBeelzButtonText(_beelzAssignKindButton, FormatBeelzAssignKind()); RebuildBeelzLoadoutAssign(); }, 110);

        _beelzLoadoutHeaderRow = AddBeelzAssignColumnHeader(assignCard, copyCol: true);
        // Put the (long) assign list in its OWN bounded-height scroll pane so browsing
        // abilities scrolls WITHIN this box — the "Ability set" + "Slots in this set" sections
        // above stay put. (Bounds the loadout page height too; enlarge the panel if the top
        // sections still scroll on a small window.)
        var assignScroll = UIFactory.CreateScrollView(assignCard, "BeelzAssignScroll",
            out var assignContent, out _, color: new Color(0f, 0f, 0f, 0f));
        // Taller pane so more abilities are visible at once (was 180/300). The list scrolls within this box.
        UIFactory.SetLayoutElement(assignScroll,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 260, preferredHeight: 440, flexibleHeight: 0);
        _beelzLoadoutAssignContainer = assignContent; // content already has a VerticalLayoutGroup + ContentSizeFitter

        BuildBeelzFormsPlaceholder(page);
        BuildBeelzPresets(page);

        RebuildBeelzLoadoutRows();
        if (BeelzState.Present && BeelzState.Slots.Count == 0)    BeelzClient.RequestSlots();
        if (BeelzState.Present && BeelzState.Captures.Count == 0) BeelzClient.RequestList();
    }

    // Group selector dropdown + live badge + group-level actions (Clear set / Copy from).
    // Built ONCE per tab build and kept in its own card so the frequent slot rebuilds
    // (SlotsChanged / CapturesChanged) don't tear the dropdowns down mid-interaction.
    private void BuildBeelzGroupSelector(GameObject page)
    {
        // First time the tab is built this session, default the editing target to the
        // weapon you're holding (falls back to Universal when unarmed / unknown).
        if (!_beelzGroupInitialized) { _beelzSelectedGroup = BeelzLiveWeaponKey(); _beelzGroupInitialized = true; }

        AddSpacer(page, 6);
        var card = AddCard(page, "BeelzLoadoutGroupCard", padding: 4, innerSpacing: 4);
        AddSectionHeading(card, "Ability set");
        AddBodyText(card,
            "<b>Universal</b> is your baseline — it applies on any weapon. A <b>per-weapon</b> or <b>per-form</b> set " +
            "overrides Universal on the slots it fills. Your normal (vanilla) spellbook picks are the default <i>only</i> " +
            "on the Universal bar; any ability you bind here takes precedence over a vanilla pick on that slot (a " +
            "per-weapon/form bind outranks a Universal one). To keep a vanilla spell as your across-weapons default, " +
            "leave that slot unbound in every Beelzebub set.");

        var selRow = UIFactory.CreateHorizontalGroup(card, "BeelzGroupSelRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 8, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(selRow, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);

        var editLbl = UIFactory.CreateLabel(selRow, "BeelzGroupLbl", $"<color={Theme.MutedBodyHex}>Editing:</color>",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(editLbl.GameObject, minWidth: 52, preferredWidth: 56, flexibleWidth: 0, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);

        var opts = BeelzGroupOptions();
        int sel = Mathf.Clamp(BeelzGroupIndexFromKey(_beelzSelectedGroup), 0, opts.Length - 1);
        var ddObj = UIFactory.CreateDropdown(selRow, "BeelzGroupDropdown", out _beelzGroupDropdown,
            opts[sel], Theme.ScaledUI(12), OnBeelzGroupChanged, opts);
        UIFactory.SetLayoutElement(ddObj, minWidth: 150, preferredWidth: 190, flexibleWidth: 0, minHeight: 26, preferredHeight: 26, flexibleHeight: 0);
        BeelzDropdownNoWrap(_beelzGroupDropdown);
        _beelzGroupDropdown.SetValueWithoutNotify(sel);
        Raphael.UI.Forms.FormDropdownRegistry.Register(_beelzGroupDropdown);
        TooltipHover.Attach(ddObj,
            "Pick the set to edit: Universal (applies on any weapon), a specific weapon family, or a shapeshift " +
            "form (shown as \"<form> (form)\"). You can edit any set without wielding the weapon / being in the " +
            "form; the server activates it automatically when you equip that weapon or enter that form.");

        _beelzLiveBadgeLabel = AddInfoLabel(selRow, "BeelzLiveBadge", "", FontStyles.Normal, Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(_beelzLiveBadgeLabel.gameObject, minWidth: 130, preferredWidth: 200, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        _beelzLiveBadgeLabel.overflowMode = TextOverflowModes.Ellipsis;

        var actRow = UIFactory.CreateHorizontalGroup(card, "BeelzGroupActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 2, 0, 0));
        UIFactory.SetLayoutElement(actRow, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);

        AddBeelzConfirmButton(actRow, "BeelzClearGroup", "Clear set",
            "Clear ALL 6 slots of the set you're editing (one unslot per bound slot). Doesn't touch other sets or your captures.",
            OnBeelzClearGroup, 88, new Color(0.55f, 0.18f, 0.18f));

        var copyLbl = UIFactory.CreateLabel(actRow, "BeelzCopyLbl", $"<color={Theme.MutedBodyHex}>Copy from</color>",
            TextAlignmentOptions.MidlineRight, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(copyLbl.GameObject, minWidth: 62, preferredWidth: 68, flexibleWidth: 0, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);

        var copyOpts = BeelzGroupOptions();
        int csel = Mathf.Clamp(BeelzGroupIndexFromKey(_beelzCopyFromGroup), 0, copyOpts.Length - 1);
        var copyObj = UIFactory.CreateDropdown(actRow, "BeelzCopyDropdown", out _beelzCopyDropdown,
            copyOpts[csel], Theme.ScaledUI(11), OnBeelzCopyFromChanged, copyOpts);
        UIFactory.SetLayoutElement(copyObj, minWidth: 130, preferredWidth: 160, flexibleWidth: 0, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        BeelzDropdownNoWrap(_beelzCopyDropdown);
        _beelzCopyDropdown.SetValueWithoutNotify(csel);
        Raphael.UI.Forms.FormDropdownRegistry.Register(_beelzCopyDropdown);
        TooltipHover.Attach(copyObj, "Pick a set to copy bindings FROM. 'Copy' clones its 6 slots into the set you're editing (overwrites those slots).");

        AddBeelzConfirmButton(actRow, "BeelzCopyGroup", "Copy",
            "Clone the 'Copy from' set's bindings into the set you're editing (overwrites). Two-click confirm.",
            OnBeelzCopyGroup, 60, new Color(0.28f, 0.45f, 0.45f));

        _beelzLoadoutStatusLabel = AddInfoLabel(card, "BeelzLoadoutStatus", "", FontStyles.Italic, Theme.ScaledUI(11));
        _beelzLoadoutStatusLabel.gameObject.SetActive(false);

        RefreshBeelzLoadoutBadge();
    }

    private void BuildBeelzFormsPlaceholder(GameObject page)
    {
        AddSpacer(page, 8);
        var card = AddCard(page, "BeelzFormsInfoCard");
        AddSectionHeading(card, "Per-form sets");
        AddBodyText(card,
            "Each shapeshift <b>form</b> (Wolf, Bear, Rat, Spider, Toad) has its own ability " +
            "set — pick it in the <b>Editing</b> dropdown above (shown as e.g. \"Wolf (form)\") and bind captured " +
            "abilities the same way as a weapon set. Entering that form from the shapeshift wheel auto-loads its " +
            "abilities; a form with no set falls back to your Universal set. Per-form abilities take effect in-game " +
            "only when the server has <i>Forms_CustomAbilities_Enabled</i> (on by default) — but you can build the " +
            "sets either way.");
    }

    private void BuildBeelzPresets(GameObject page)
    {
        AddSpacer(page, 8);
        var card = AddCard(page, "BeelzPresetsCard", padding: 4, innerSpacing: 4);
        AddSectionHeading(card, "Loadout presets");
        AddBodyText(card,
            "Save your current slot bindings as a named, server-side preset and load it back later " +
            $"({Mono(".beelz preset")}). Replies appear in chat; use {Mono("List")} to see your saved names.");
        var row = UIFactory.CreateHorizontalGroup(card, "BeelzPresetRow",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 2, 0, 0));
        UIFactory.SetLayoutElement(row, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var nameInput = UIFactory.CreateInputField(row, "BeelzPresetName", "preset name");
        UIFactory.SetLayoutElement(nameInput.GameObject, minWidth: 110, preferredWidth: 150, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        nameInput.OnValueChanged += (string v) => { _beelzPresetName = v ?? ""; };
        AddBeelzSmallButton(row, "BeelzPresetSave", "Save", "Save current bindings as this preset (.beelz preset save).", () => { if (ReqPreset(out var n)) BeelzClient.PresetSave(n); }, 50);
        AddBeelzSmallButton(row, "BeelzPresetLoad", "Load", "Load this preset onto your bar (.beelz preset load).", () => { if (ReqPreset(out var n)) BeelzClient.PresetLoad(n); }, 50);
        AddBeelzConfirmButton(row, "BeelzPresetDelete", "Delete", "Delete this preset (.beelz preset delete).", () => { if (ReqPreset(out var n)) BeelzClient.PresetDelete(n); }, 56, new Color(0.55f, 0.18f, 0.18f));
        AddBeelzSmallButton(row, "BeelzPresetList", "List", "List your saved presets in chat (.beelz preset list).", BeelzClient.PresetList, 46);
    }

    private bool ReqPreset(out string name)
    {
        name = (_beelzPresetName ?? "").Trim();
        if (name.Length == 0) { SetBeelzLoadoutStatus("<color=#FFB070>Type a preset name first.</color>"); return false; }
        return true;
    }

    private bool BeelzGroupIsUniversal => string.IsNullOrEmpty(_beelzSelectedGroup);

    // A set "key" is "" (universal), a WeaponFamily, or a Form name. Forms don't collide with weapon
    // families, so the bare name is a safe key. (Beelz v0.59+ per-form loadouts.)
    private static bool BeelzGroupIsFormKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        foreach (var f in BeelzClient.Forms)
            if (f.Equals(key, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // Dispatch a grant/unslot to the right command family for a set key (universal / weapon / form).
    private static void BeelzGrantTo(string key, int slot, int index)
    {
        if (string.IsNullOrEmpty(key)) BeelzClient.Grant(slot, index);
        else if (BeelzGroupIsFormKey(key)) BeelzClient.FormGrant(key, slot, index);
        else BeelzClient.WeaponGrant(key, slot, index);
    }
    private static void BeelzUnslotFrom(string key, int slot)
    {
        if (string.IsNullOrEmpty(key)) BeelzClient.Unslot(slot);
        else if (BeelzGroupIsFormKey(key)) BeelzClient.FormUnslot(key, slot);
        else BeelzClient.WeaponUnslot(key, slot);
    }

    // Ordered set keys: "" (universal), each weapon family, then each form. Index 0 = universal.
    private static string[] BeelzGroupKeys()
    {
        var fam = BeelzClient.WeaponFamilies; var forms = BeelzClient.Forms;
        var arr = new string[1 + fam.Length + forms.Length];
        arr[0] = "";
        Array.Copy(fam, 0, arr, 1, fam.Length);
        Array.Copy(forms, 0, arr, 1 + fam.Length, forms.Length);
        return arr;
    }
    // Dropdown labels parallel to BeelzGroupKeys: "Universal", weapon names, then "<Form> (form)".
    private static string[] BeelzGroupOptions()
    {
        var keys = BeelzGroupKeys();
        var labels = new string[keys.Length];
        for (int i = 0; i < keys.Length; i++) labels[i] = BeelzGroupLabel(keys[i]);
        return labels;
    }
    private static string BeelzGroupKeyFromIndex(int i)
    {
        var keys = BeelzGroupKeys();
        return (i < 0 || i >= keys.Length) ? "" : keys[i];
    }
    private static int BeelzGroupIndexFromKey(string key)
    {
        var keys = BeelzGroupKeys();
        for (int i = 0; i < keys.Length; i++)
            if (string.Equals(keys[i], key ?? "", StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }
    private static string BeelzGroupLabel(string key)
        => string.IsNullOrEmpty(key) ? "Universal" : (BeelzGroupIsFormKey(key) ? $"{key} (form)" : key);

    // The live weapon family as a group key ("" if unarmed / none / not a known family).
    private static string BeelzLiveWeaponKey()
    {
        string lw = BeelzState.CurrentWeapon;
        if (string.IsNullOrEmpty(lw) || lw.Equals("None", StringComparison.OrdinalIgnoreCase)) return "";
        return BeelzGroupIndexFromKey(lw) > 0 ? lw : "";
    }

    // The binding in a given set ("" = universal/any, a weapon family, or a form name) for slot n, or null.
    private static BeelzSlot FindBeelzSlot(string groupKey, int n)
    {
        if (BeelzGroupIsFormKey(groupKey))
        {
            foreach (var fs in BeelzState.FormSlots)
                if (fs.Slot == n && fs.Form.Equals(groupKey, StringComparison.OrdinalIgnoreCase))
                    return new BeelzSlot(groupKey, fs.Slot, fs.AbilityGuid, fs.AbilityName, fs.Label);
            return null;
        }
        bool uni = string.IsNullOrEmpty(groupKey);
        foreach (var s in BeelzState.Slots)
        {
            bool isUni = string.IsNullOrEmpty(s.Bucket) || s.Bucket.Equals("any", StringComparison.OrdinalIgnoreCase);
            bool match = uni ? isUni : (!isUni && s.Bucket.Equals(groupKey, StringComparison.OrdinalIgnoreCase));
            if (match && s.Slot == n) return s;
        }
        return null;
    }

    private static void BeelzDropdownNoWrap(TMP_Dropdown dd)
    {
        try
        {
            if (dd == null) return;
            if (dd.captionText != null) { dd.captionText.enableWordWrapping = false; dd.captionText.overflowMode = TextOverflowModes.Ellipsis; }
            if (dd.itemText != null)    { dd.itemText.enableWordWrapping    = false; dd.itemText.overflowMode    = TextOverflowModes.Ellipsis; }
        }
        catch { }
    }

    private void SetBeelzLoadoutStatus(string msg)
    {
        if (_beelzLoadoutStatusLabel == null) return;
        _beelzLoadoutStatusLabel.text = msg;
        _beelzLoadoutStatusLabel.gameObject.SetActive(true);
    }

    private void OnBeelzGroupChanged(int i)
    {
        string prev = _beelzSelectedGroup ?? "";
        _beelzSelectedGroup = BeelzGroupKeyFromIndex(i);
        _beelzPendingConfirmKey = null; _beelzPendingConfirmDeadline = -1f; // a group switch cancels any armed confirm
        // PERF: switching which SET you're editing only changes the 6 slot rows — the captured-ability
        // assign list is identical for every set (only the bind target changes, read live at click time).
        // Rebuilding the big assign list here is what caused the 1-2s freeze on set switch, so normally
        // rebuild just the slots.
        RebuildBeelzLoadoutSlots();
        // …EXCEPT when the slot RESTRICTION changes (e.g. to/from Mounted = 5/6/7 only; 3/6/7 before api34): the assign rows'
        // per-ability bind buttons (P/1-6/U) are restricted too, so they must rebuild now instead of going
        // stale until a manual Refresh. Ordinary weapon↔weapon / ↔Universal switches keep the same all-8
        // set, so this is skipped there and the freeze never happens.
        if (!BeelzSlotSetEqual(BeelzAllowedSlotsForGroup(prev), BeelzAllowedSlotsForGroup(_beelzSelectedGroup)))
            RebuildBeelzLoadoutAssign();
    }

    // The slots a set key permits (null = all 8). Forms may restrict (Mounted = 5/6/7, or 3/6/7 before api34); weapons/universal = all.
    private static int[] BeelzAllowedSlotsForGroup(string key)
        => BeelzGroupIsFormKey(key) ? BeelzClient.FormAllowedSlots(key) : null;
    private static bool BeelzSlotSetEqual(int[] a, int[] b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;
        for (int k = 0; k < a.Length; k++) if (a[k] != b[k]) return false;
        return true;
    }
    private void OnBeelzCopyFromChanged(int i) => _beelzCopyFromGroup = BeelzGroupKeyFromIndex(i);

    private void OnBeelzClearGroup()
    {
        if (!BeelzState.Present) return;
        string g = _beelzSelectedGroup ?? "";
        // This button clears the WHOLE selected set, so `.beelz clearbar <bucket>` (one server call that
        // clears all slots 0-7) is the right call here. (Per-SLOT Clear buttons use unslot/weapon-unslot/
        // form-unslot, which since Beelz v0.100 accept the primary/ultimate tokens too — see BeelzUnslotFrom.)
        string bucket = string.IsNullOrEmpty(g) ? "universal" : g;
        BeelzClient.ClearBar(bucket);
        SetBeelzLoadoutStatus($"<color=#90EE90>Clearing the {BeelzGroupLabel(g)} set (all slots incl. primary/ultimate)…</color>");
    }

    private void OnBeelzCopyGroup()
    {
        if (!BeelzState.Present) return;
        string target = _beelzSelectedGroup ?? "";
        string source = _beelzCopyFromGroup ?? "";
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        { SetBeelzLoadoutStatus("<color=#FFB070>Pick a different source in 'Copy from'.</color>"); return; }

        // grant is index-based (into api list); map ability guid -> current capture index.
        var idxByGuid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in BeelzState.Captures)
            if (!string.IsNullOrEmpty(c.AbilityGuid)) idxByGuid[c.AbilityGuid] = c.Index;

        int copied = 0, total = 0, skipped = 0;
        foreach (int n in BeelzSlotOrder)
        {
            var b = FindBeelzSlot(source, n);
            if (b == null || string.IsNullOrEmpty(b.AbilityGuid)) continue;
            total++;
            if (idxByGuid.TryGetValue(b.AbilityGuid, out int idx))
            {
                BeelzGrantTo(target, n, idx);
                copied++;
            }
            else skipped++;
        }
        SetBeelzLoadoutStatus(total == 0
            ? $"<color=#FFB070>The {BeelzGroupLabel(source)} set has nothing to copy.</color>"
            : $"<color=#90EE90>Copying {copied}/{total} from {BeelzGroupLabel(source)} → {BeelzGroupLabel(target)}…</color>"
              + (skipped > 0 ? $" <color=#FFB070>({skipped} skipped — Refresh, then retry)</color>" : ""));
    }

    private void RefreshBeelzLoadoutBadge()
    {
        if (_beelzLiveBadgeLabel == null) return;
        string lw = BeelzState.CurrentWeapon;
        bool none = string.IsNullOrEmpty(lw) || lw.Equals("None", StringComparison.OrdinalIgnoreCase);
        bool editingLive = string.Equals(BeelzLiveWeaponKey(), _beelzSelectedGroup ?? "", StringComparison.OrdinalIgnoreCase);
        string liveText = none ? "Universal (no weapon)" : lw;
        _beelzLiveBadgeLabel.text =
            $"<color=#90EE90>● live:</color> {liveText}" +
            (editingLive ? "   <color=#90EE90>(editing your live set)</color>" : "");
    }

    // Subscribed to BOTH SlotsChanged and CapturesChanged — rebuilds both sections.
    private void RebuildBeelzLoadoutRows()
    {
        RebuildBeelzLoadoutSlots();
        RebuildBeelzLoadoutAssign();
    }

    private void RebuildBeelzLoadoutSlots()
    {
        if (!BeelzTabVisible(PanelType.BeelzLoadoutTab)) return;
        RefreshBeelzLoadoutBadge();
        if (_beelzLoadoutRowContainer == null) return;
        ClearChildren(_beelzLoadoutRowContainer);

        if (!BeelzState.Present) { AddSimpleRow(_beelzLoadoutRowContainer, "(Beelzebub not detected)", italic: true); return; }

        string g = _beelzSelectedGroup ?? "";
        // Most sets show all 8 bindable slots: primary (left-click, 0), the six spell slots (1-6), and
        // ultimate (T, 7). Unbound primary/ultimate read "(empty → vanilla)" like any other empty slot.
        // A form may restrict its slots (Mounted only accepts 5/6/7 on api34+, 3/6/7 before — the horse owns the rest); hide the
        // others and add a one-line note so the user isn't confused why a slot is missing.
        int[] allowedSlots = BeelzGroupIsFormKey(g) ? BeelzClient.FormAllowedSlots(g) : null;
        if (allowedSlots != null)
            AddSimpleRow(_beelzLoadoutRowContainer,
                BeelzState.SupportsSaddleRCT
                    ? $"<color={Theme.MutedBodyHex}>{BeelzGroupLabel(g)} only binds slots 5 (R) / 6 (C) / 7 (Ultimate) — the horse owns the rest.</color>"
                    : $"<color={Theme.MutedBodyHex}>{BeelzGroupLabel(g)} only binds slots 3 (Shift) / 6 (C) / 7 (Ultimate) — the horse owns the rest.</color>", italic: true);
        foreach (int n in BeelzSlotOrder)
        {
            if (allowedSlots != null && Array.IndexOf(allowedSlots, n) < 0) continue;
            var found = FindBeelzSlot(g, n);
            var row = MakeBeelzRow(_beelzLoadoutRowContainer, $"BeelzSlot_{n}");
            // api28: prefer the server's curated label= (correct friendly name even for boss/NPC abilities);
            // fall back to humanizing the raw prefab name on older servers / when label is absent.
            string ability = (found == null || string.IsNullOrEmpty(found.AbilityName))
                ? $"<color={Theme.MutedBodyHex}>(empty → vanilla)</color>"
                : (!string.IsNullOrEmpty(found.Label) ? found.Label : BeelzNames.Ability(found.AbilityName));
            // Diagnostics: append the bound ability's ID (PrefabGUID) so testers can report it.
            string idSuffix = (Config.Settings.BeelzDiagnostics && found != null && !string.IsNullOrEmpty(found.AbilityGuid))
                ? $"  <color=#9FD0FF>[{found.AbilityGuid}]</color>" : "";
            AddBeelzRowLabel(row, $"<color={Theme.MutedBodyHex}>{BeelzSlotBtn(n)}. {BeelzSlotLabel(n)}</color>   {ability}{idSuffix}");
            int slotN = n;
            if (found != null && !string.IsNullOrEmpty(found.AbilityName))
                AddBeelzSmallButton(row, $"BeelzSlotClear_{n}", "Clear",
                    $"Clear {BeelzSlotLabel(slotN)} of the {BeelzGroupLabel(g)} set.",
                    () => BeelzUnslotFrom(g, slotN),
                    56, new Color(0.55f, 0.18f, 0.18f));
        }
    }

    private void RebuildBeelzLoadoutAssign()
    {
        if (!BeelzTabVisible(PanelType.BeelzLoadoutTab)) return;
        if (_beelzLoadoutAssignContainer == null) return;
        _beelzLoadoutAssignDirty = false;   // building now → clean until the next data change
        ClearChildren(_beelzLoadoutAssignContainer);

        if (!BeelzState.Present) { AddSimpleRow(_beelzLoadoutAssignContainer, "(Beelzebub not detected)", italic: true); return; }
        if (BeelzState.Captures.Count == 0)
        {
            AddSimpleRow(_beelzLoadoutAssignContainer, "(no captured abilities yet — capture some, then Refresh)", italic: true);
            return;
        }

        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
        string search = (_beelzAssignSearch ?? "").Trim();

        var filtered = new List<BeelzCapture>();
        foreach (var c in BeelzState.Captures)
        {
            if (_beelzAssignSource == BeelzAbilSource.VBlood  && c.Source != 'V') continue;
            if (_beelzAssignSource == BeelzAbilSource.Regular && c.Source == 'V') continue;
            if (!string.IsNullOrEmpty(_beelzAssignCategory) && !_beelzAssignCategory.Equals(c.Category, OIC)) continue;
            if (!BeelzMatchesKind(c)) continue;
            // Grouped/dynamic search — matches name / unit / weapon / category / kind / creature.
            if (search.Length > 0 && BeelzSearchHaystack(c).IndexOf(search, OIC) < 0) continue;
            filtered.Add(c);
        }

        // 0.18.4 FREEZE FIX: enrich only the FIRST chunk of rows (≈ what's on screen), NOT the whole
        // filtered list. Feeding all of `filtered` walked the ENTIRE collection across rebuilds — each
        // `api info` reply rebuilds the list, which re-fed the next slice — so on a fully-collected
        // server it flooded hundreds of `api info` fetches + hundreds of 250-row rebuilds and froze the
        // UI (and re-ran on every UI-open, incl. after a rejoin, piling interop onto the crash window).
        // The dynamic tooltip + the diagnostics Copy button fetch any other row on demand.
        var visibleIdx = new List<int>(System.Math.Min(filtered.Count, BEELZ_ENRICH_MAX_ROWS));
        for (int i = 0; i < filtered.Count && i < BEELZ_ENRICH_MAX_ROWS; i++) visibleIdx.Add(filtered[i].Index);
        BeelzProtocolService.EnrichAbilityInfo(visibleIdx);

        string kindNote = (_beelzAssignKind != BeelzAbilKind.All && !BeelzState.CatalogLoaded)
            ? " — the Kind filter needs Scan all" : "";
        AddSimpleRow(_beelzLoadoutAssignContainer,
            $"<color={Theme.MutedBodyHex}>Click 1–6 to bind to the set selected above. {filtered.Count} shown.{kindNote}</color>");

        if (filtered.Count == 0) { AddSimpleRow(_beelzLoadoutAssignContainer, "(no abilities match this filter)", italic: true); return; }

        _beelzLastAssignGroupKeys.Clear();
        const int CAP = 250; // bound the row build so a huge collection can't hitch

        if (_beelzAssignGroupMode == BeelzGroupMode.None)
        {
            filtered.Sort((a, b) => string.Compare(BeelzNames.Ability(a.AbilityName), BeelzNames.Ability(b.AbilityName), OIC));
            int n2 = Math.Min(filtered.Count, CAP);
            for (int i = 0; i < n2; i++) BuildBeelzAssignRow(filtered[i]);
            if (filtered.Count > n2)
                AddSimpleRow(_beelzLoadoutAssignContainer, $"<color={Theme.MutedBodyHex}>… and {filtered.Count - n2} more — refine the filters / search.</color>");
            return;
        }

        // Group by the selected axis, collapsible headers. An ability can belong to MULTIPLE
        // groups (Weapon mode: a sword+axe ability appears under both), so add each cap to
        // every key it returns. Each row still shows all attribute columns.
        var groups = new Dictionary<string, List<BeelzCapture>>(StringComparer.OrdinalIgnoreCase);
        foreach (var cap in filtered)
            foreach (var key in BeelzGroupKeysFor(cap))
            {
                if (!groups.TryGetValue(key, out var glist)) { glist = new List<BeelzCapture>(); groups[key] = glist; }
                glist.Add(cap);
            }
        var keys = new List<string>(groups.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        _beelzLastAssignGroupKeys.AddRange(keys);

        int rendered = 0; bool capped = false;
        foreach (var key in keys)
        {
            var glist = groups[key];
            glist.Sort((a, b) => string.Compare(BeelzNames.Ability(a.AbilityName), BeelzNames.Ability(b.AbilityName), OIC));
            bool collapsed = _beelzCollapsedGroups.Contains(key);

            var hdrBtn = UIFactory.CreateButton(_beelzLoadoutAssignContainer, $"BeelzGrpHdr_{key}",
                $"{(collapsed ? "▶" : "▼")} {key}  ({glist.Count})");
            UIFactory.SetLayoutElement(hdrBtn.GameObject,
                minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
            var ht = hdrBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (ht != null) { ht.fontSize = Theme.ScaledUI(12); ht.alignment = TextAlignmentOptions.MidlineLeft; ht.fontStyle = FontStyles.Bold; }
            TooltipHover.Attach(hdrBtn.GameObject, "Click to collapse / expand this group.");
            string gKey = key;
            hdrBtn.OnClick = () =>
            {
                if (!_beelzCollapsedGroups.Remove(gKey)) _beelzCollapsedGroups.Add(gKey);
                RebuildBeelzLoadoutAssign();
            };

            if (!collapsed && !capped)
                foreach (var cap in glist)
                {
                    if (rendered >= CAP) { capped = true; break; }
                    BuildBeelzAssignRow(cap); rendered++;
                }
        }
        if (capped)
            AddSimpleRow(_beelzLoadoutAssignContainer, $"<color={Theme.MutedBodyHex}>(list truncated at {CAP} rows — refine the filters / search or collapse groups.)</color>");
    }

    // One assign row: shared Ability | Unit | Cat columns + the 1-6 bind buttons (in a fixed-
    // width sub-group so the row keeps the same 4 columns as the header). Full detail on hover.
    private void BuildBeelzAssignRow(BeelzCapture cap)
    {
        var row = MakeBeelzRow(_beelzLoadoutAssignContainer, $"BeelzCap_{cap.Index}");
        AddBeelzCaptureColumns(row, cap);

        var btnRow = UIFactory.CreateHorizontalGroup(row, "BeelzAssignBtns",
            forceExpandWidth: false, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: BEELZ_ASSIGN_BTN_SP, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(btnRow, minWidth: BeelzSlotsColW, preferredWidth: BeelzSlotsColW, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);

        int idx = cap.Index;
        // A restricted form (Mounted = 5/6/7, or 3/6/7 before api34) hides the bind buttons the server would refuse, so the
        // ability's slot column lines up with the slot rows above.
        string selKey = _beelzSelectedGroup ?? "";
        int[] allowedSlots = BeelzGroupIsFormKey(selKey) ? BeelzClient.FormAllowedSlots(selKey) : null;
        foreach (int n in BeelzSlotOrder)
        {
            if (allowedSlots != null && Array.IndexOf(allowedSlots, n) < 0) continue;
            int slotN = n;
            // No per-button tooltip → hovering a button still shows the row's ability detail.
            // P = primary (left-click), U = ultimate (T), 1-6 = spell slots; binds to the edited set.
            AddBeelzSmallButton(btnRow, $"BeelzAssign_{cap.Index}_{n}", BeelzSlotBtn(n), null,
                () => BeelzGrantTo(_beelzSelectedGroup ?? "", slotN, idx),
                BEELZ_ASSIGN_BTN_W);
        }

        // 0.18.4: DIAGNOSTICS-ONLY "Copy" button — copies this ability's full details (name, IDs, unit,
        // category/kind/creature, weapon/form restrictions, scalars, description/cooldown if fetched) to
        // the system clipboard so testers can paste it straight into the Discord testing docs. Only built
        // when Beelzebub diagnostic details are enabled (Beelzebub → Settings); never shown otherwise.
        if (Config.Settings.BeelzDiagnostics)
            AddBeelzSmallButton(row, $"BeelzCopy_{cap.Index}", "Copy",
                "Copy this ability's full details to the clipboard (name, IDs, unit, category/kind, scalars) — for bug reports / testing docs. Diagnostics-only.",
                () => CopyBeelzAbilityDetails(cap), 46);
    }

    // 0.18.4: copy an ability's details to the OS clipboard (diagnostics tool — see BuildBeelzAssignRow).
    private void CopyBeelzAbilityDetails(BeelzCapture cap)
    {
        try
        {
            bool haveInfo = BeelzState.TryGetAbilityInfo(cap.AbilityGuid, out _);
            UnityEngine.GUIUtility.systemCopyBuffer = BeelzAbilityCopyText(cap);
            if (haveInfo)
            {
                SetBeelzLoadoutStatus($"<color=#90EE90>Copied '{BeelzNames.Ability(cap.AbilityName)}' details to the clipboard — paste into Discord.</color>");
            }
            else
            {
                // Description/cooldown haven't been fetched for THIS row yet (we only auto-fetch the
                // top rows to avoid the freeze). Request just this ONE ability — a single fetch, never
                // a scan — so a second Copy in a moment includes them.
                try { BeelzClient.RequestInfo(cap.Index); } catch { }
                SetBeelzLoadoutStatus($"<color=#90EE90>Copied '{BeelzNames.Ability(cap.AbilityName)}' (name, IDs, tags).</color>  <color=#FFD75A>Fetching its description/cooldown — Copy again in a moment for the full details.</color>");
            }
        }
        catch (Exception ex)
        {
            Utils.LogUtils.LogError($"Beelz copy-ability failed: {ex}");
            SetBeelzLoadoutStatus("<color=#FFB070>Copy failed — see the BepInEx log.</color>");
        }
    }

    // Plain-text (no rich-text tags) ability dump for the clipboard / Discord. Mirrors the hover text
    // but Discord-friendly. Includes IDs + raw prefab name unconditionally (this is the diagnostics copy).
    private static string BeelzAbilityCopyText(BeelzCapture cap)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(BeelzNames.Ability(cap.AbilityName));
        if (cap.Source == 'V') sb.Append("  [V-Blood]");
        sb.Append("\nPrefab: ").Append(cap.AbilityName);
        if (!string.IsNullOrEmpty(cap.AbilityGuid)) sb.Append("   ID: ").Append(cap.AbilityGuid);
        sb.Append("\nUnit: ").Append(BeelzUnitName(cap.UnitGuid, cap.UnitName));
        if (!string.IsNullOrEmpty(cap.UnitGuid)) sb.Append(" (id ").Append(cap.UnitGuid).Append(')');
        if (!string.IsNullOrEmpty(cap.Category)) sb.Append("\nCategory: ").Append(cap.Category);
        string kind = BeelzState.AbilityKind(cap.AbilityName);
        if (!string.IsNullOrEmpty(kind)) sb.Append("   Kind: ").Append(kind);
        if (!string.IsNullOrEmpty(cap.UnitType)) sb.Append("   Creature: ").Append(cap.UnitType);
        if (BeelzState.TryGetCatalog(cap.AbilityName, out var c))
        {
            if (c.Weapons != null && c.Weapons.Count > 0) sb.Append("\nWeapons: ").Append(string.Join("/", c.Weapons));
            if (c.Forms   != null && c.Forms.Count   > 0) sb.Append("\nForms: ").Append(string.Join("/", c.Forms));
            if (c.TransformOnly) sb.Append("\n(transform-only)");
            if (c.DamageScale   > 0f && c.DamageScale   != 1f) sb.Append("\nDamage x").Append(c.DamageScale.ToString("0.##"));
            if (c.CooldownScale > 0f && c.CooldownScale != 1f) sb.Append("   Cooldown x").Append(c.CooldownScale.ToString("0.##"));
        }
        if (BeelzState.TryGetAbilityInfo(cap.AbilityGuid, out var info))
        {
            if (!string.IsNullOrEmpty(info.Desc) && !info.Desc.Equals("none", StringComparison.OrdinalIgnoreCase))
                sb.Append("\nDesc: ").Append(info.Desc.Replace('_', ' '));
            if (BeelzState.SupportsRuntimeCooldowns && info.UsesCharges) sb.Append("\nCooldown: uses charges");
            else if (info.CooldownSeconds > 0f || info.Shaping?.Cooldown != null)
                sb.Append("\nCooldown: ").Append(info.EffectiveCooldownSeconds.ToString("0.#")).Append('s');
        }
        return sb.ToString();
    }

    // Rich hover text for an ability: name + unit + category/kind/creature-type + weapon/form
    // restrictions (from the catalog) + the real description & damage/cooldown scalars when an
    // `api info` fetch has populated them (the action-bar overlay fetches those for hotkeys).
    private static string BeelzAbilityHoverText(BeelzCapture cap)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<b>").Append(BeelzNames.Ability(cap.AbilityName)).Append("</b>");
        sb.Append("\nUnit: ").Append(BeelzUnitName(cap.UnitGuid, cap.UnitName));
        if (!string.IsNullOrEmpty(cap.Category)) sb.Append("   Category: ").Append(cap.Category);
        string kind = BeelzState.AbilityKind(cap.AbilityName);
        if (!string.IsNullOrEmpty(kind)) sb.Append("   Kind: ").Append(kind);
        if (!string.IsNullOrEmpty(cap.UnitType)) sb.Append("   Creature: ").Append(cap.UnitType);
        BeelzState.TryGetAbilityInfo(cap.AbilityGuid, out var info);
        bool haveCat = BeelzState.TryGetCatalog(cap.AbilityName, out var c);
        AppendBeelzRestrictionLines(sb, haveCat ? c : null, info);
        if (haveCat)
        {
            if (c.TransformOnly) sb.Append("   (transform-only)");
            if (c.DamageScale   > 0f && c.DamageScale   != 1f) sb.Append($"\nDamage ×{c.DamageScale:0.##} (admin-tuned)");
            if (c.CooldownScale > 0f && c.CooldownScale != 1f) sb.Append($"   Cooldown ×{c.CooldownScale:0.##}");
        }
        if (info != null)
        {
            if (!string.IsNullOrEmpty(info.Desc) && !info.Desc.Equals("none", StringComparison.OrdinalIgnoreCase))
                sb.Append("\n").Append(info.Desc.Replace('_', ' '));
            AppendBeelzCooldownDamageLine(sb, info);
        }
        string lockGroup = BeelzState.LockConflict(cap.AbilityGuid, cap.Category);
        if (!string.IsNullOrEmpty(lockGroup))
            sb.Append($"\n<color=#FF8080>Locked by '{lockGroup}': that group is already full on your bar, so this stays off it until you remove one.</color>");
        // Diagnostics: raw prefab name + IDs for bug reports (toggle in Beelzebub → Settings).
        if (Config.Settings.BeelzDiagnostics)
        {
            sb.Append("\n<color=#9FD0FF>ID: ").Append(string.IsNullOrEmpty(cap.AbilityGuid) ? "?" : cap.AbilityGuid);
            sb.Append("   prefab: ").Append(cap.AbilityName);
            if (!string.IsNullOrEmpty(cap.UnitGuid)) sb.Append("   unit-id: ").Append(cap.UnitGuid);
            sb.Append("</color>");
        }
        return sb.ToString();
    }

    // ============================ HOTKEYS ============================

    private void BuildBeelzHotkeysTab(GameObject page)
    {
        EnsureBeelzSubscribed();
        if (AddBeelzAbsentNote(page)) return;

        var card = AddCard(page, "BeelzHotkeysHeaderCard");
        AddSectionHeading(card, "Hotkey abilities");
        AddBodyText(card,
            "Add abilities BEYOND the 6 bar slots. <b>These are on-screen action-bar BUTTONS, not key " +
            "binds</b> — the \"name\" is just the button's label. Each bound ability becomes a tile on the " +
            "<b>Beelz Action Bar</b> overlay (Show it below); click a tile (or the Cast button here) to " +
            $"force-cast it, respecting its cooldown. ({Mono(".beelz hotkey set")} / {Mono("clear")} / {Mono("cast")}.)");
        AddBodyText(card,
            "<color=#FFB070>Optional key:</color> V Rising won't let a mod bind a native ability key, but each bound " +
            "ability below has a <b>+ key</b> button — set a client-side shortcut and pressing it casts the ability " +
            "(via chat; convenient, not frame-perfect). It won't fire while you're typing in a text field.");

        var actionRow = UIFactory.CreateHorizontalGroup(page, "BeelzHotkeysActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 4, 0, 0));
        AddBeelzRefreshButton(actionRow, "Re-sync hotkeys + captures (api hotkeys / list).",
            () => { BeelzClient.RequestHotkeys(); BeelzClient.RequestList(); });

        var overlayBtn = UIFactory.CreateButton(actionRow, "BeelzOverlayToggle",
            (Plugin.UIManager?.IsOverlayOpen(PanelType.BeelzActionBarOverlay) ?? false) ? "Hide Action Bar" : "Show Action Bar");
        UIFactory.SetLayoutElement(overlayBtn.GameObject,
            minWidth: 120, preferredWidth: 150, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var obTxt = overlayBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (obTxt != null) { obTxt.fontSize = Theme.ScaledUI(12); obTxt.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(overlayBtn.GameObject,
            "Show/hide the on-screen \"Beelz Action Bar\" overlay — a button per hotkey ability with a cooldown ring. " +
            "Drag it where you like; it's also in the overlay lock + transparency controls.");
        overlayBtn.OnClick = () =>
        {
            Plugin.UIManager?.ToggleOverlay(PanelType.BeelzActionBarOverlay);
            bool on = Plugin.UIManager?.IsOverlayOpen(PanelType.BeelzActionBarOverlay) ?? false;
            var t = overlayBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.text = on ? "Hide Action Bar" : "Show Action Bar";
        };

        // 0.19: Summons management — moved here from the Transforms tab. Summons are a GENERAL Beelzebub
        // feature (independent of being transformed — Beelz v0.45), so they live with the other on-screen
        // action controls. Stash/restore is a toggle; it also gets its own draggable overlay (the
        // "Summons Overlay" toggle below) so you can stash/restore without opening this panel.
        AddSpacer(page, 6);
        var summonsCard = AddCard(page, "BeelzHotkeysSummonsCard", padding: 4, innerSpacing: 2);
        AddSectionHeading(summonsCard, "Summons");
        AddBodyText(summonsCard,
            "Manage your active summoned minions (works untransformed too). " +
            $"Stash before a waygate, then restore on the other side. ({Mono(".beelz summons stash")} / " +
            $"{Mono("restore")} / {Mono("clear")}, {Mono(".beelz tp")} to recall.)");

        var summonsRow = UIFactory.CreateHorizontalGroup(summonsCard, "BeelzSummonsActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 4, 0, 0));
        AddBeelzSmallButton(summonsRow, "BeelzSummonsStash", "Stash summons",
            "Stash your active summons before a waygate (.beelz summons stash).",
            () => BeelzClient.SendUser(".beelz summons stash"), 104);
        AddBeelzSmallButton(summonsRow, "BeelzSummonsRestore", "Restore",
            "Restore stashed summons (.beelz summons restore).",
            () => BeelzClient.SendUser(".beelz summons restore"), 76);
        AddBeelzSmallButton(summonsRow, "BeelzSummonsRecall", "Recall",
            "Recall your summons to you (.beelz tp).",
            () => BeelzClient.SendUser(".beelz tp"), 64);
        AddBeelzConfirmButton(summonsRow, "BeelzSummonsClear", "Clear summons",
            "Despawn ALL your summons (.beelz summons clear).",
            () => BeelzClient.SendUser(".beelz summons clear"), 104, new Color(0.55f, 0.18f, 0.18f));

        // Toggle the on-screen Summons overlay (a small draggable stash/restore toggle + recall/clear).
        var summonsOverlayRow = UIFactory.CreateHorizontalGroup(summonsCard, "BeelzSummonsOverlayRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 2, 0, 0));
        var summonsOverlayBtn = UIFactory.CreateButton(summonsOverlayRow, "BeelzSummonsOverlayToggle",
            (Plugin.UIManager?.IsOverlayOpen(PanelType.BeelzSummonsOverlay) ?? false) ? "Hide Summons Overlay" : "Show Summons Overlay");
        UIFactory.SetLayoutElement(summonsOverlayBtn.GameObject,
            minWidth: 140, preferredWidth: 180, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var soTxt = summonsOverlayBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (soTxt != null) { soTxt.fontSize = Theme.ScaledUI(12); soTxt.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(summonsOverlayBtn.GameObject,
            "Show/hide the on-screen \"Summons\" overlay — a small draggable panel that toggles your summons " +
            "between stashed and restored with one click (plus recall + clear). Drag it where you like.");
        summonsOverlayBtn.OnClick = () =>
        {
            Plugin.UIManager?.ToggleOverlay(PanelType.BeelzSummonsOverlay);
            bool on = Plugin.UIManager?.IsOverlayOpen(PanelType.BeelzSummonsOverlay) ?? false;
            var t = summonsOverlayBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.text = on ? "Hide Summons Overlay" : "Show Summons Overlay";
        };

        AddSpacer(page, 6);
        var curCard = AddCard(page, "BeelzHotkeysCurCard", padding: 4, innerSpacing: 2);
        AddSectionHeading(curCard, "Your hotkeys");
        _beelzHotkeyRowContainer = UIFactory.CreateVerticalGroup(curCard, "BeelzHotkeyRows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(_beelzHotkeyRowContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 40, flexibleHeight: 0);

        AddSpacer(page, 6);
        var bindCard = AddCard(page, "BeelzHotkeysBindCard", padding: 4, innerSpacing: 2);
        AddSectionHeading(bindCard, "Bind a new hotkey");
        // 0.18.4: the name becomes a chat-command token (`.beelz cast <name>`), so it must be a SINGLE
        // word — letters / numbers / _ / - only. Spaces or symbols make the bind silently fail (the
        // server only reads the first word). The Bind button enforces this too.
        AddBodyText(bindCard,
            "Use a single word for the name — letters, numbers, _ or - only (no spaces or symbols). " +
            "The name is sent as a chat command, so spaces would break it.");

        var nameRow = UIFactory.CreateHorizontalGroup(bindCard, "BeelzHotkeyNameRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(nameRow, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var nameInput = UIFactory.CreateInputField(nameRow, "BeelzHotkeyName", "Hotkey name (e.g. Bolt)…");
        UIFactory.SetLayoutElement(nameInput.GameObject, minWidth: 110, preferredWidth: 150, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        nameInput.OnValueChanged += (string v) => { _beelzHotkeyName = v ?? ""; };
        var bindSearch = UIFactory.CreateInputField(nameRow, "BeelzHotkeyBindSearch", "Search name / unit / weapon / category…");
        UIFactory.SetLayoutElement(bindSearch.GameObject, minWidth: 110, preferredWidth: 170, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        bindSearch.OnValueChanged += (string v) => { _beelzHotkeyBindSearch = v ?? ""; RebuildBeelzHotkeyBindList(); };

        // Filters (parity with the Loadout assign list): source / category / kind ; group + expand/collapse.
        var hkF1 = UIFactory.CreateHorizontalGroup(bindCard, "BeelzHkFilter1",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(hkF1, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        _beelzHkSourceBtn = AddBeelzSmallButton(hkF1, "BeelzHkSrc", FmtBeelzSource(_beelzHkSource), "Cycle source: All → V-Blood → Regular.",
            () => { _beelzHkSource = _beelzHkSource switch { BeelzAbilSource.All => BeelzAbilSource.VBlood, BeelzAbilSource.VBlood => BeelzAbilSource.Regular, _ => BeelzAbilSource.All };
                    SetBeelzButtonText(_beelzHkSourceBtn, FmtBeelzSource(_beelzHkSource)); RebuildBeelzHotkeyBindList(); }, 110);
        _beelzHkCatBtn = AddBeelzSmallButton(hkF1, "BeelzHkCat", FmtBeelzCat(_beelzHkCategory), "Cycle category (built from your captures).",
            () => { CycleBeelzHkCategory(); SetBeelzButtonText(_beelzHkCatBtn, FmtBeelzCat(_beelzHkCategory)); RebuildBeelzHotkeyBindList(); }, 116);
        _beelzHkKindBtn = AddBeelzSmallButton(hkF1, "BeelzHkKind", FmtBeelzKind(_beelzHkKind), "Cycle kind: All → Magic → Weapon → Form (needs Scan all).",
            () => { _beelzHkKind = _beelzHkKind switch { BeelzAbilKind.All => BeelzAbilKind.Magic, BeelzAbilKind.Magic => BeelzAbilKind.Weapon, BeelzAbilKind.Weapon => BeelzAbilKind.Form, _ => BeelzAbilKind.All };
                    SetBeelzButtonText(_beelzHkKindBtn, FmtBeelzKind(_beelzHkKind)); RebuildBeelzHotkeyBindList(); }, 104);

        var hkF2 = UIFactory.CreateHorizontalGroup(bindCard, "BeelzHkFilter2",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(hkF2, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        _beelzHkGroupBtn = AddBeelzSmallButton(hkF2, "BeelzHkGroup", FmtBeelzGroup(_beelzHkGroupMode), "Cycle grouping: off → Unit → Category → Kind → Weapon.",
            () => { _beelzHkGroupMode = _beelzHkGroupMode switch { BeelzGroupMode.None => BeelzGroupMode.Unit, BeelzGroupMode.Unit => BeelzGroupMode.Category, BeelzGroupMode.Category => BeelzGroupMode.Kind, BeelzGroupMode.Kind => BeelzGroupMode.Weapon, _ => BeelzGroupMode.None };
                    _beelzHkCollapsed.Clear(); SetBeelzButtonText(_beelzHkGroupBtn, FmtBeelzGroup(_beelzHkGroupMode)); RebuildBeelzHotkeyBindList(); }, 120);
        AddBeelzSmallButton(hkF2, "BeelzHkExpand", "Expand all", "Expand every group.", () => { _beelzHkCollapsed.Clear(); RebuildBeelzHotkeyBindList(); }, 82);
        AddBeelzSmallButton(hkF2, "BeelzHkCollapse", "Collapse all", "Collapse every group.",
            () => { foreach (var k in _beelzHkLastGroupKeys) _beelzHkCollapsed.Add(k); RebuildBeelzHotkeyBindList(); }, 92);

        _beelzHotkeyStatusLabel = AddInfoLabel(bindCard, "BeelzHotkeyStatus", "", FontStyles.Italic, Theme.ScaledUI(11));
        _beelzHotkeyStatusLabel.gameObject.SetActive(false);

        _beelzHkHeaderRow = AddBeelzAssignColumnHeader(bindCard, BEELZ_HK_HEADER_LASTCOL, Theme.ScaledWidth(BEELZ_HK_HEADER_LASTCOLW));
        _beelzHotkeyBindContainer = UIFactory.CreateVerticalGroup(bindCard, "BeelzHotkeyBindRows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(_beelzHotkeyBindContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 60, flexibleHeight: 0);

        RebuildBeelzHotkeyRows();
        RebuildBeelzHotkeyBindList();
        if (BeelzState.Present && BeelzState.Hotkeys.Count == 0)  BeelzClient.RequestHotkeys();
        if (BeelzState.Present && BeelzState.Captures.Count == 0) BeelzClient.RequestList();
    }

    private void RebuildBeelzHotkeyRows()
    {
        if (!BeelzTabVisible(PanelType.BeelzHotkeysTab)) return;
        if (_beelzHotkeyRowContainer == null) return;
        ClearChildren(_beelzHotkeyRowContainer);

        if (!BeelzState.HotkeysEnabled)
            AddSimpleRow(_beelzHotkeyRowContainer, "<color=#FFB070>Hotkeys are disabled on this server (admin setting Hotkeys_Enabled).</color>");

        if (BeelzState.Hotkeys.Count == 0)
        {
            AddSimpleRow(_beelzHotkeyRowContainer, BeelzState.Present ? "(no hotkeys bound yet — bind one below)" : "(Beelzebub not detected)", italic: true);
            return;
        }
        foreach (var h in BeelzState.Hotkeys)
        {
            var row = MakeBeelzRow(_beelzHotkeyRowContainer, $"BeelzHk_{h.Name}");
            AddBeelzRowLabel(row, $"<b>{h.Name}</b>   <color={Theme.MutedBodyHex}>{BeelzNames.Ability(h.AbilityName)}</color>");
            string nm = h.Name;
            AddBeelzKeybindControls(row, nm);  // optional client-side keyboard shortcut → .beelz cast
            AddBeelzSmallButton(row, $"BeelzHkCast_{h.Name}", "Cast", $"Force-cast {h.Name} now (.beelz cast {h.Name}).", () => BeelzClient.Cast(nm), 50);
            AddBeelzSmallButton(row, $"BeelzHkClear_{h.Name}", "Clear", $"Remove the {h.Name} hotkey + its key (.beelz hotkey clear {h.Name}).",
                () => { Config.Settings.SetBeelzKeybind(nm, Config.BCHotkey.Empty); BeelzClient.HotkeyClear(nm); }, 50, new Color(0.55f, 0.18f, 0.18f));
        }
        AddSimpleRow(_beelzHotkeyRowContainer,
            $"<color={Theme.MutedBodyHex}>{BeelzState.Hotkeys.Count}/{(BeelzState.HotkeysMax > 0 ? BeelzState.HotkeysMax.ToString() : "?")} bindings used</color>");
    }

    // Compact keyboard-shortcut control for one action-bar hotkey: a button showing the
    // current key (click → press a key; Escape cancels) + a Clr button. Persisted per hotkey
    // NAME in Settings; BeelzProtocolService.TickKeybinds fires `.beelz cast` when pressed.
    // Mirrors the Settings-tab AddHotkeyRow capture loop.
    private void AddBeelzKeybindControls(GameObject row, string hotkeyName)
    {
        ButtonRef setBtn = null;
        System.Action listener = null;

        void Refresh()
        {
            var hk = Config.Settings.GetBeelzKeybind(hotkeyName);
            SetBeelzButtonText(setBtn, hk.IsEmpty ? "+ key" : hk.ToString());
        }

        setBtn = AddBeelzSmallButton(row, $"BeelzKeySet_{hotkeyName}", "+ key",
            "Bind a keyboard shortcut to this ability — click, then press the key (or modifier+key); Escape " +
            "cancels. Pressing it in-game casts the ability (via chat). It won't fire while you're typing.",
            () =>
            {
                if (listener != null) { Behaviors.CoreUpdateBehavior.Actions.Remove(listener); listener = null; }
                SetBeelzButtonText(setBtn, "press…");
                listener = () =>
                {
                    try
                    {
                        if (Input.GetKeyDown(KeyCode.Escape))
                        { Behaviors.CoreUpdateBehavior.Actions.Remove(listener); listener = null; Refresh(); return; }
                        KeyCode pressed = KeyCode.None;
                        for (int k = (int)KeyCode.Backspace; k < (int)KeyCode.JoystickButton0; k++)
                        { var kc = (KeyCode)k; if (IsModifierKey(kc)) continue; if (Input.GetKeyDown(kc)) { pressed = kc; break; } }
                        if (pressed == KeyCode.None) return;
                        var mods = new List<KeyCode>();
                        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) mods.Add(KeyCode.LeftControl);
                        if (Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt))     mods.Add(KeyCode.LeftAlt);
                        if (Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift))   mods.Add(KeyCode.LeftShift);
                        if (Input.GetKey(KeyCode.LeftWindows) || Input.GetKey(KeyCode.RightWindows)) mods.Add(KeyCode.LeftWindows);
                        Config.Settings.SetBeelzKeybind(hotkeyName,
                            new Config.BCHotkey { MainKey = pressed, Modifiers = mods.Count > 0 ? mods.ToArray() : null });
                        Behaviors.CoreUpdateBehavior.Actions.Remove(listener); listener = null; Refresh();
                    }
                    catch { if (listener != null) { Behaviors.CoreUpdateBehavior.Actions.Remove(listener); listener = null; } Refresh(); }
                };
                Behaviors.CoreUpdateBehavior.Actions.Add(listener);
            }, 64);

        AddBeelzSmallButton(row, $"BeelzKeyClr_{hotkeyName}", "Clr", "Clear this ability's keyboard shortcut.",
            () => { if (listener != null) { Behaviors.CoreUpdateBehavior.Actions.Remove(listener); listener = null; } Config.Settings.SetBeelzKeybind(hotkeyName, Config.BCHotkey.Empty); Refresh(); }, 32);

        Refresh();
    }

    private void RebuildBeelzHotkeyBindList()
    {
        if (!BeelzTabVisible(PanelType.BeelzHotkeysTab)) return;
        if (_beelzHotkeyBindContainer == null) return;
        ClearChildren(_beelzHotkeyBindContainer);

        if (!BeelzState.Present) { AddSimpleRow(_beelzHotkeyBindContainer, "(Beelzebub not detected)", italic: true); return; }
        if (!BeelzState.HotkeysEnabled) { AddSimpleRow(_beelzHotkeyBindContainer, "(hotkeys are disabled by the server admin)", italic: true); return; }
        if (BeelzState.Captures.Count == 0) { AddSimpleRow(_beelzHotkeyBindContainer, "(no captured abilities yet — capture some, then Refresh)", italic: true); return; }

        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
        string search = (_beelzHotkeyBindSearch ?? "").Trim();
        AddSimpleRow(_beelzHotkeyBindContainer, $"<color={Theme.MutedBodyHex}>Type a name above, then Bind an ability.</color>");

        var filtered = new List<BeelzCapture>();
        foreach (var c in BeelzState.Captures)
        {
            if (_beelzHkSource == BeelzAbilSource.VBlood  && c.Source != 'V') continue;
            if (_beelzHkSource == BeelzAbilSource.Regular && c.Source == 'V') continue;
            if (!string.IsNullOrEmpty(_beelzHkCategory) && !_beelzHkCategory.Equals(c.Category, OIC)) continue;
            if (!BeelzKindMatches(c.AbilityName, _beelzHkKind)) continue;
            if (search.Length > 0 && BeelzSearchHaystack(c).IndexOf(search, OIC) < 0) continue;
            filtered.Add(c);
        }
        // 0.18.4 FREEZE FIX: cap enrichment to the first chunk of rows (see RebuildBeelzLoadoutAssign).
        var visIdx = new List<int>(System.Math.Min(filtered.Count, BEELZ_ENRICH_MAX_ROWS));
        for (int i = 0; i < filtered.Count && i < BEELZ_ENRICH_MAX_ROWS; i++) visIdx.Add(filtered[i].Index);
        BeelzProtocolService.EnrichAbilityInfo(visIdx);

        if (filtered.Count == 0) { AddSimpleRow(_beelzHotkeyBindContainer, "(no abilities match this filter)", italic: true); return; }
        _beelzHkLastGroupKeys.Clear();
        const int CAP = 250;

        if (_beelzHkGroupMode == BeelzGroupMode.None)
        {
            filtered.Sort((a, b) => string.Compare(BeelzNames.Ability(a.AbilityName), BeelzNames.Ability(b.AbilityName), OIC));
            int n = Math.Min(filtered.Count, CAP);
            for (int i = 0; i < n; i++) BuildBeelzHkBindRow(filtered[i]);
            if (filtered.Count > n)
                AddSimpleRow(_beelzHotkeyBindContainer, $"<color={Theme.MutedBodyHex}>… and {filtered.Count - n} more — refine the filters / search.</color>");
            return;
        }

        var groups = new Dictionary<string, List<BeelzCapture>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in filtered)
            foreach (var key in BeelzGroupKeysFor(c, _beelzHkGroupMode))
            {
                if (!groups.TryGetValue(key, out var g)) { g = new List<BeelzCapture>(); groups[key] = g; }
                g.Add(c);
            }
        var keys = new List<string>(groups.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        _beelzHkLastGroupKeys.AddRange(keys);

        int rendered = 0; bool capped = false;
        foreach (var key in keys)
        {
            var g = groups[key];
            g.Sort((a, b) => string.Compare(BeelzNames.Ability(a.AbilityName), BeelzNames.Ability(b.AbilityName), OIC));
            bool collapsed = _beelzHkCollapsed.Contains(key);
            var hdr = UIFactory.CreateButton(_beelzHotkeyBindContainer, $"BeelzHkGrp_{key}", $"{(collapsed ? "▶" : "▼")} {key}  ({g.Count})");
            UIFactory.SetLayoutElement(hdr.GameObject, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
            var ht = hdr.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (ht != null) { ht.fontSize = Theme.ScaledUI(12); ht.alignment = TextAlignmentOptions.MidlineLeft; ht.fontStyle = FontStyles.Bold; }
            TooltipHover.Attach(hdr.GameObject, "Click to collapse / expand this group.");
            string gk = key;
            hdr.OnClick = () => { if (!_beelzHkCollapsed.Remove(gk)) _beelzHkCollapsed.Add(gk); RebuildBeelzHotkeyBindList(); };
            if (!collapsed && !capped)
                foreach (var c in g) { if (rendered >= CAP) { capped = true; break; } BuildBeelzHkBindRow(c); rendered++; }
        }
        if (capped)
            AddSimpleRow(_beelzHotkeyBindContainer, $"<color={Theme.MutedBodyHex}>(list truncated at {CAP} rows — refine the filters / search or collapse groups.)</color>");
    }

    // One hotkey-bind row: shared Ability | Unit | Cat columns + a single Bind button (which
    // binds to the name typed above). Same column layout as the loadout, so they line up.
    private void BuildBeelzHkBindRow(BeelzCapture cap)
    {
        var row = MakeBeelzRow(_beelzHotkeyBindContainer, $"BeelzHkBind_{cap.Index}");
        AddBeelzCaptureColumns(row, cap);
        int idx = cap.Index;
        string abilDisp = BeelzNames.Ability(cap.AbilityName);
        AddBeelzSmallButton(row, $"BeelzHkBindBtn_{cap.Index}", "Bind",
            "Bind this ability to the hotkey name typed above (becomes an Action Bar tile).",
            () => OnBeelzBindHotkey(idx, abilDisp), 54);
    }

    private void OnBeelzBindHotkey(int captureIndex, string abilityDisplay)
    {
        string name = (_beelzHotkeyName ?? "").Trim();
        if (_beelzHotkeyStatusLabel != null)
        {
            if (string.IsNullOrEmpty(name))
            {
                _beelzHotkeyStatusLabel.text = "<color=#FFB070>Type a hotkey name in the box above first.</color>";
                _beelzHotkeyStatusLabel.gameObject.SetActive(true);
                return;
            }
            // 0.18.4: the name is sent as a chat-command token, so reject anything but a single safe word
            // (letters/digits/_/-). Spaces or symbols silently fail server-side — explain instead.
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9_\-]+$"))
            {
                _beelzHotkeyStatusLabel.text = "<color=#FFB070>Hotkey name must be a single word — letters, numbers, _ or - only (no spaces or symbols). It's sent as a chat command, so spaces break it.</color>";
                _beelzHotkeyStatusLabel.gameObject.SetActive(true);
                return;
            }
            _beelzHotkeyStatusLabel.text = $"<color=#90EE90>Bound '{name}' → {abilityDisplay}.</color> It'll appear under \"Your hotkeys\" + as a tile on the Action Bar overlay. (Watch chat for the server's reply.)";
            _beelzHotkeyStatusLabel.gameObject.SetActive(true);
        }
        BeelzClient.HotkeySet(name, captureIndex);
        // Refresh the list right after (queued behind the set, so the read reflects it) — the
        // server also emits a hotkey-set event, but this makes the bind show even if it doesn't.
        BeelzClient.RequestHotkeys();
    }

    // ============================ TRANSFORMS ============================

    private void BuildBeelzTransformsTab(GameObject page)
    {
        EnsureBeelzSubscribed();
        if (AddBeelzAbsentNote(page)) return;

        var card = AddCard(page, "BeelzTransformsHeaderCard");
        AddSectionHeading(card, "Transforms");
        AddBodyText(card,
            "Your unlocked boss forms (Dracula, Morgana, and the newer Werewolf / Golem / Gargoyle forms). " +
            "Transform into one, switch its phase, fire its signature summon or " +
            $"detonation, then revert. ({Mono(".beelz transform")} / " +
            $"{Mono("phase")} / {Mono("summon")} / {Mono("detonate")} / {Mono("revert")}.)\n" +
            "<color=#FFB070>Summon management (stash / restore / recall / clear) moved to the Hotkeys tab</color> — " +
            "it works untransformed too (summons are independent of transforms), and it has its own toggle overlay there.");

        var actionRow = UIFactory.CreateHorizontalGroup(page, "BeelzTransformsActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 4, 0, 0));
        AddBeelzRefreshButton(actionRow, "Re-sync transforms + active form (api transforms / active).",
            () => { BeelzClient.RequestTransforms(); BeelzClient.RequestActive(); });
        // Toggle the on-screen Transforms overlay (browser-style: double-click a form to transform + phase/revert).
        var tfOverlayBtn = UIFactory.CreateButton(actionRow, "BeelzTformOverlayToggle",
            (Plugin.UIManager?.IsOverlayOpen(PanelType.BeelzTransformOverlay) ?? false) ? "Hide Transforms Overlay" : "Show Transforms Overlay");
        UIFactory.SetLayoutElement(tfOverlayBtn.GameObject, minWidth: 150, preferredWidth: 190, flexibleWidth: 0, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var tfOvTxt = tfOverlayBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (tfOvTxt != null) { tfOvTxt.fontSize = Theme.ScaledUI(12); tfOvTxt.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(tfOverlayBtn.GameObject,
            "Show/hide the on-screen \"Transforms\" overlay — a draggable list of your forms; double-click one to " +
            "transform, with Phase 1 / Phase 2 / Revert buttons. Auto-hides when Beelzebub isn't detected.");
        tfOverlayBtn.OnClick = () =>
        {
            Plugin.UIManager?.ToggleOverlay(PanelType.BeelzTransformOverlay);
            bool on = Plugin.UIManager?.IsOverlayOpen(PanelType.BeelzTransformOverlay) ?? false;
            var t = tfOverlayBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.text = on ? "Hide Transforms Overlay" : "Show Transforms Overlay";
        };

        AddSpacer(page, 6);
        var rowsCard = AddCard(page, "BeelzTransformsRowsCard", padding: 4, innerSpacing: 2);
        _beelzTransformRowContainer = UIFactory.CreateVerticalGroup(rowsCard, "BeelzTransformRows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(_beelzTransformRowContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 60, flexibleHeight: 0);

        RebuildBeelzTransformRows();

        // Per-form custom loadout editor (Beelz v0.100 `.beelz tform`).
        AddSpacer(page, 6);
        BuildBeelzTransformLoadoutEditor(page);

        // Transform settings (mode / duration / live cooldown per category) — api transform-config + cooldowns.
        AddSpacer(page, 6);
        var cfgCard = AddCard(page, "BeelzTxConfigCard", padding: 4, innerSpacing: 2);
        AddSectionHeading(cfgCard, "Transform settings (server)");
        _beelzTxConfigContainer = UIFactory.CreateVerticalGroup(cfgCard, "BeelzTxConfigRows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(_beelzTxConfigContainer, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 24, flexibleHeight: 0);
        RebuildBeelzTxConfig();

        if (BeelzState.Present)
        {
            if (BeelzState.Transforms.Count == 0) { BeelzClient.RequestTransforms(); BeelzClient.RequestActive(); }
            BeelzClient.RequestTransformConfig();
            BeelzClient.RequestCooldowns();
        }
    }

    // Render the 3 transform categories (Regular / V-Blood / Shard boss): mode, duration, and live
    // cooldown remaining (api transform-config for the static profile + api cooldowns for the timer).
    private void RebuildBeelzTxConfig()
    {
        if (!BeelzTabVisible(PanelType.BeelzTransformsTab)) return;
        if (_beelzTxConfigContainer == null) return;
        ClearChildren(_beelzTxConfigContainer);
        if (!BeelzState.Present) { AddSimpleRow(_beelzTxConfigContainer, "(Beelzebub not detected)", italic: true); return; }
        if (BeelzState.TxConfigs.Count == 0) { AddSimpleRow(_beelzTxConfigContainer, "(loading…)", italic: true); return; }

        foreach (var c in BeelzState.TxConfigs)
        {
            string label = c.Src switch { 'R' => "Regular", 'V' => "V-Blood", 'S' => "Shard boss", _ => c.Src.ToString() };
            string cdKey = c.Src switch { 'R' => "regular", 'V' => "vblood", 'S' => "shard", _ => "" };
            string mode = string.IsNullOrEmpty(c.Mode) ? "—" : c.Mode;
            string dur = c.Mode.Equals("Timed", StringComparison.OrdinalIgnoreCase) ? $", {c.Duration:0}s" : "";
            string baseCd = c.Cooldown > 0 ? $", cooldown {c.Cooldown:0}s" : "";
            string live = "";
            if (!string.IsNullOrEmpty(cdKey) && BeelzState.Cooldowns.TryGetValue(cdKey, out var rem) && rem > 0.5f)
                live = $"   <color=#FFB070>(on cooldown: {rem:0}s)</color>";
            AddBeelzRowLabel(MakeBeelzRow(_beelzTxConfigContainer, $"BeelzTxCfg_{c.Src}"),
                $"<b>{label}</b>   <color={Theme.MutedBodyHex}>{mode}{dur}{baseCd}</color>{live}");
        }
    }

    private void RebuildBeelzTransformRows()
    {
        if (!BeelzTabVisible(PanelType.BeelzTransformsTab)) return;
        if (_beelzTransformRowContainer == null) return;
        ClearChildren(_beelzTransformRowContainer);

        var active = BeelzState.Active;
        bool isActive = active != null && !active.None;

        // Active form + live controls.
        var activeRow = MakeBeelzRow(_beelzTransformRowContainer, "BeelzActiveRow");
        if (isActive)
        {
            AddBeelzRowLabel(activeRow,
                $"<color=#90EE90>Active:</color> {BeelzUnitName(active.UnitGuid, active.UnitName)}" +
                (string.IsNullOrEmpty(active.Ttl) ? "" : $"  <color={Theme.MutedBodyHex}>({active.Ttl})</color>"));
            AddBeelzSmallButton(activeRow, "BeelzRevert", "Revert", "End the active transform (.beelz revert).", BeelzClient.Revert, 60);
            AddBeelzSmallButton(activeRow, "BeelzPhase", "Phase", "Switch to the next form/phase (.beelz phase).", () => BeelzClient.SendUser(".beelz phase"), 54);
            AddBeelzSmallButton(activeRow, "BeelzSummon", "Summon", "Force-cast the form's signature summon (.beelz summon).", BeelzClient.Summon, 64);
            AddBeelzSmallButton(activeRow, "BeelzDetonate", "Detonate", "Fire the form's signature AoE (.beelz detonate).", BeelzClient.Detonate, 72);
        }
        else
        {
            AddBeelzRowLabel(activeRow, $"<color={Theme.MutedBodyHex}>No active transform.</color>");
        }

        if (BeelzState.Transforms.Count == 0)
        {
            AddSimpleRow(_beelzTransformRowContainer,
                BeelzState.Present ? "(no transforms unlocked — defeat a transform boss: Dracula, Morgana, Werewolf Chieftain, Geomancer, the Tailor…)" : "(Beelzebub not detected)", italic: true);
            return;
        }

        foreach (var t in BeelzState.Transforms)
        {
            var row = MakeBeelzRow(_beelzTransformRowContainer, $"BeelzTxRow_{t.UnitGuid}");
            string disp = BeelzUnitName(t.UnitGuid, t.UnitName);
            AddBeelzRowLabel(row,
                $"<b>{disp}</b>" + (t.Enabled ? "" : $"  <color=#FFB070>(disabled)</color>") +
                (t.Shard ? $"  <color={Theme.MutedBodyHex}>· shard</color>" : ""));
            string txi = t.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            AddBeelzSmallButton(row, $"BeelzTxGo_{t.UnitGuid}", "Transform",
                $"Transform into {disp} (.beelz transform {txi}). If you're already transformed, Raphael reverts you to vampire form first, then transforms (Beelz v0.120 refuses a direct transform→transform). Any failure — not unlocked, on cooldown, disabled, or Brutal-only on a Basic server — replies in chat.",
                () => BeelzClient.TransformSafe(txi), 84);
            AddBeelzSmallButton(row, $"BeelzTxPrev_{t.UnitGuid}", "Preview",
                $"Preview {disp}'s abilities without committing — reply shows in chat (.beelz preview {txi}).",
                () => BeelzClient.SendUser($".beelz preview {txi}"), 62);
        }
    }

    // ============================ ADMIN: CONFIG ============================

    private void BuildBeelzAdminConfigTab(GameObject page)
    {
        EnsureBeelzSubscribed();
        RenderAdminInfoNote(page, "Beelzebub config");
        if (AddBeelzAbsentNote(page)) return;
        page = BeginAdminGate(page);   // gray out + disable everything below for non-admins

        var card = AddCard(page, "BeelzAdminConfigCard");
        AddSectionHeading(card, "Server settings");
        AddBodyText(card,
            $"Every Beelzebub setting, read live from {Mono("api config")} and changed with {Mono("admin set")} " +
            "(drop chances, transform modes/durations/cooldowns, shard bosses, hotkey limits, …). New settings appear " +
            "automatically. Requires server-admin permission; changes persist to the server config.");
        AddBodyText(card,
            "<color=#FFD9A0>Dual-mod (Bloodcraft) tip:</color> Bloodcraft can write slot <b>3</b> (its Shift / class " +
            "spell) and slots <b>1</b>+<b>4</b> (Unarmed spells); Beelzebub writes slots 0–7. On a slot BOTH claim, " +
            "the winner is load-order-dependent unless you set a priority. Two fixes (use either): " +
            $"(1) search {Mono("Interop_SlotInjectionPriority")} below (Beelz v0.120) and set it to <b>1</b> so " +
            "Beelzebub wins deterministically (0 = tie; negative = Beelzebub yields); or " +
            "(2) set Bloodcraft's <b>ShiftSlot=false</b> / <b>UnarmedSlots=false</b> in io.zfolmt.Bloodcraft.cfg to " +
            "hand those slots to Beelzebub entirely. While transformed, Beelzebub's carrier buff owns the bar and " +
            "Bloodcraft's class/shift spells return on revert — no conflict there.");

        var actionRow = UIFactory.CreateHorizontalGroup(page, "BeelzAdminConfigActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 4, 0, 0));
        AddBeelzRefreshButton(actionRow, "Re-read all settings (api config).", BeelzClient.RequestConfig);
        var search = UIFactory.CreateInputField(actionRow, "BeelzConfigSearch", "Filter settings…");
        UIFactory.SetLayoutElement(search.GameObject, minWidth: 150, preferredWidth: 220, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        search.OnValueChanged += (string v) => { _beelzAdminConfigSearch = v ?? ""; RebuildBeelzAdminConfig(); };

        // Announcements (Beelz v0.88): the enables + message pools are config KEYS in the list below
        // (set them generically); these are the live actions that aren't config (status / send-now)
        // plus a quick leaderboard on/off.
        AddSpacer(page, 4);
        var annCard = AddCard(page, "BeelzAdminAnnounceCard"); AddSectionHeading(annCard, "Announcements (broadcast)");
        AddBodyText(annCard,
            "Server-wide 100%-collection + periodic leaderboard broadcasts. Message pools and on/off enables are " +
            "config keys (Broadcast_*) in the list below; these buttons are the live actions.");
        var annRow = MakeBeelzRow(annCard, "BeelzAdminAnnounceRow");
        AddBeelzSmallButton(annRow, "BeelzAnnStatus", "Status", "Show current announcement settings in chat (admin broadcast status).", () => AdminSend(".beelz admin broadcast status"), 60);
        AddBeelzSmallButton(annRow, "BeelzAnnTest", "Test", "Send one leaderboard broadcast now (admin broadcast test).", () => AdminSend(".beelz admin broadcast test"), 50);
        AddBeelzSmallButton(annRow, "BeelzAnnLbOn", "Leaderboard on", "Enable the periodic leaderboard broadcast (admin broadcast leaderboard on).", () => AdminSend(".beelz admin broadcast leaderboard on"), 116);
        AddBeelzSmallButton(annRow, "BeelzAnnLbOff", "Leaderboard off", "Disable the periodic leaderboard broadcast (admin broadcast leaderboard off).", () => AdminSend(".beelz admin broadcast leaderboard off"), 120);

        // Capture filters (Beelz v0.53): control what's capturable. Deny/Allow take a name SUBSTRING;
        // the GUID lists + transform-only take an exact PrefabGUID. Persists to ability_rules.json.
        AddSpacer(page, 4);
        var filtCard = AddCard(page, "BeelzAdminFiltersCard"); AddSectionHeading(filtCard, "Capture filters");
        AddBodyText(filtCard,
            "Control which abilities are capturable. Deny/Allow take a name SUBSTRING; the GUID lists and " +
            "transform-only take an exact PrefabGUID. Changes persist to ability_rules.json (Reload re-reads it).");
        var fRow1 = UIFactory.CreateHorizontalGroup(filtCard, "BeelzFiltPatRow",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 2, 0, 0));
        UIFactory.SetLayoutElement(fRow1, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var patInput = UIFactory.CreateInputField(fRow1, "BeelzFiltPat", "name substring");
        UIFactory.SetLayoutElement(patInput.GameObject, minWidth: 100, preferredWidth: 140, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        patInput.OnValueChanged += (string v) => { _beelzAdminFilterPattern = v ?? ""; };
        AddBeelzSmallButton(fRow1, "BeelzFiltDeny", "Deny+", "Add a deny substring (admin deny).", () => { if (ReqPat(out var p)) AdminSend($".beelz admin deny {p}"); }, 52);
        AddBeelzSmallButton(fRow1, "BeelzFiltUndeny", "Deny−", "Remove a deny substring (admin undeny).", () => { if (ReqPat(out var p)) AdminSend($".beelz admin undeny {p}"); }, 52);
        AddBeelzSmallButton(fRow1, "BeelzFiltAllow", "Allow+", "Add an allow substring — when ANY allow exists it's an exclusive whitelist (admin allow).", () => { if (ReqPat(out var p)) AdminSend($".beelz admin allow {p}"); }, 54);
        AddBeelzSmallButton(fRow1, "BeelzFiltUnallow", "Allow−", "Remove an allow substring (admin unallow).", () => { if (ReqPat(out var p)) AdminSend($".beelz admin unallow {p}"); }, 54);
        var fRow2 = UIFactory.CreateHorizontalGroup(filtCard, "BeelzFiltGuidRow",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 2, 0, 0));
        UIFactory.SetLayoutElement(fRow2, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var guidInput = UIFactory.CreateInputField(fRow2, "BeelzFiltGuid", "PrefabGUID");
        UIFactory.SetLayoutElement(guidInput.GameObject, minWidth: 90, preferredWidth: 120, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        guidInput.OnValueChanged += (string v) => { _beelzAdminFilterGuid = v ?? ""; };
        AddBeelzSmallButton(fRow2, "BeelzFiltDenyGAdd", "DenyGUID+", "Add a deny GUID (admin denyguid add).", () => { if (ReqGuid(out var g)) AdminSend($".beelz admin denyguid add {g}"); }, 72);
        AddBeelzSmallButton(fRow2, "BeelzFiltDenyGRem", "−", "Remove a deny GUID (admin denyguid remove).", () => { if (ReqGuid(out var g)) AdminSend($".beelz admin denyguid remove {g}"); }, 24);
        AddBeelzSmallButton(fRow2, "BeelzFiltAllowGAdd", "AllowGUID+", "Add an allow GUID (admin allowguid add).", () => { if (ReqGuid(out var g)) AdminSend($".beelz admin allowguid add {g}"); }, 76);
        AddBeelzSmallButton(fRow2, "BeelzFiltAllowGRem", "−", "Remove an allow GUID (admin allowguid remove).", () => { if (ReqGuid(out var g)) AdminSend($".beelz admin allowguid remove {g}"); }, 24);
        AddBeelzSmallButton(fRow2, "BeelzFiltTOAdd", "T-only+", "Reserve an ability for transformation by pattern/GUID (admin transformonly add).", () => { if (ReqGuid(out var g)) AdminSend($".beelz admin transformonly add {g}"); }, 60);
        AddBeelzSmallButton(fRow2, "BeelzFiltTORem", "−", "Remove a transform-only reservation (admin transformonly remove).", () => { if (ReqGuid(out var g)) AdminSend($".beelz admin transformonly remove {g}"); }, 24);
        var fRow3 = MakeBeelzRow(filtCard, "BeelzFiltActRow");
        AddBeelzSmallButton(fRow3, "BeelzFiltRules", "Show rules", "Print the current filter rules in chat (admin rules).", () => AdminSend(".beelz admin rules"), 80);
        AddBeelzSmallButton(fRow3, "BeelzFiltReload", "Reload rules", "Re-read ability_rules.json from disk after a hand-edit (admin reload).", () => AdminSend(".beelz admin reload"), 90);

        AddSpacer(page, 6);
        var rowsCard = AddCard(page, "BeelzAdminConfigRowsCard", padding: 4, innerSpacing: 2);
        _beelzAdminConfigContainer = UIFactory.CreateVerticalGroup(rowsCard, "BeelzAdminConfigRows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(_beelzAdminConfigContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 60, flexibleHeight: 0);

        RebuildBeelzAdminConfig();
        if (BeelzState.Present && BeelzState.Config.Count == 0) BeelzClient.RequestConfig();
    }

    private void RebuildBeelzAdminConfig()
    {
        if (!BeelzTabVisible(PanelType.BeelzAdminConfigTab)) return;
        if (_beelzAdminConfigContainer == null) return;
        ClearChildren(_beelzAdminConfigContainer);
        if (!BeelzState.Present) { AddSimpleRow(_beelzAdminConfigContainer, "(Beelzebub not detected)", italic: true); return; }
        if (BeelzState.Config.Count == 0) { AddSimpleRow(_beelzAdminConfigContainer, "(no config loaded — click Refresh)", italic: true); return; }

        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
        string search = (_beelzAdminConfigSearch ?? "").Trim();
        var list = new List<BeelzConfigEntry>(BeelzState.Config);
        list.Sort((a, b) => { int c = string.Compare(a.Section, b.Section, OIC); return c != 0 ? c : string.Compare(a.Key, b.Key, OIC); });

        string lastSection = null;
        int shown = 0;
        foreach (var e in list)
        {
            if (search.Length > 0 && ($"{e.Key} {e.Section} {e.Value}").IndexOf(search, OIC) < 0) continue;
            if (!string.Equals(e.Section, lastSection, OIC)) { AddSimpleRow(_beelzAdminConfigContainer, $"<color=#9FD0FF><b>— {e.Section} —</b></color>"); lastSection = e.Section; }
            BuildBeelzConfigRow(e);
            shown++;
        }
        if (shown == 0) AddSimpleRow(_beelzAdminConfigContainer, "(no settings match this filter)", italic: true);
    }

    // #F3: each setting is a VERTICAL block — name/value on top (wraps), the description under it
    // (wraps), then the control on its own full-width row — so long keys/values/messages can't overlap
    // the input (the "Broadcast_CollectionComplete_Messages" overlap report).
    private void BuildBeelzConfigRow(BeelzConfigEntry e)
    {
        string key = e.Key;
        var wrap = UIFactory.CreateVerticalGroup(_beelzAdminConfigContainer, $"BeelzCfg_{e.Key}",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 1, padding: new Vector4(2, 3, 2, 4));
        UIFactory.SetLayoutElement(wrap, minWidth: 340, preferredWidth: 390, flexibleWidth: 1, minHeight: 24, flexibleHeight: 0);

        // For message-pool keys the value is a long pipe-joined string that squished the header; it's
        // shown cleanly (one per line) in the multi-line editor below, so the header shows just the key.
        bool isMsgPool = key.EndsWith("_Messages", StringComparison.OrdinalIgnoreCase);
        string nameLblText = isMsgPool
            ? $"<b>{e.Key}</b>"
            : $"<b>{e.Key}</b>  <color={Theme.MutedBodyHex}>= {e.Value}</color>";
        var nameLbl = UIFactory.CreateLabel(wrap, $"BeelzCfgName_{e.Key}",
            nameLblText, TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(nameLbl.GameObject, minWidth: 320, preferredWidth: 380, flexibleWidth: 1, minHeight: 16, flexibleHeight: 0);
        nameLbl.TextMesh.enableWordWrapping = true;

        if (BeelzConfigDescriptions.TryGetValue(e.Key, out var cfgDesc))
        {
            TooltipHover.Attach(nameLbl.GameObject, cfgDesc);
            var descLbl = UIFactory.CreateLabel(wrap, $"BeelzCfgDesc_{e.Key}",
                $"<color={Theme.MutedBodyHex}><i>{cfgDesc}</i></color>", TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(10));
            UIFactory.SetLayoutElement(descLbl.GameObject, minWidth: 320, preferredWidth: 380, flexibleWidth: 1, minHeight: 14, flexibleHeight: 0);
            descLbl.TextMesh.enableWordWrapping = true;
        }

        // Message-pool keys (…_Messages, e.g. the broadcast pools) hold SEVERAL alternative messages
        // joined by '|' with %token% placeholders. A single-line field squished them together; give them
        // a tall MULTI-LINE editor (one message per line) + a format help line instead. (Must run before
        // the generic control row below.)
        if (key.EndsWith("_Messages", StringComparison.OrdinalIgnoreCase))
        {
            BuildBeelzMessagePoolEditor(wrap, e);
            return;
        }

        var ctl = UIFactory.CreateHorizontalGroup(wrap, $"BeelzCfgCtl_{e.Key}",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true, spacing: 8, padding: new Vector4(0, 1, 0, 0));
        UIFactory.SetLayoutElement(ctl, minWidth: 320, preferredWidth: 380, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);

        bool isBool = !string.IsNullOrEmpty(e.Type) && e.Type.IndexOf("bool", StringComparison.OrdinalIgnoreCase) >= 0;
        if (isBool)
        {
            bool cur = e.Value.Equals("true", StringComparison.OrdinalIgnoreCase) || e.Value == "1";
            AddBeelzSmallButton(ctl, $"BeelzCfgToggle_{e.Key}", cur ? "True" : "False",
                $"Toggle {e.Key} (admin set {e.Key} {(cur ? "false" : "true")}).",
                () => BeelzClient.SendUser($".beelz admin set {key} {(cur ? "false" : "true")}"), 64);
        }
        else if (BeelzConfigEnums.TryGetValue(e.Key, out var enumOpts))
        {
            // #9: enum-valued key → dropdown of valid values; selecting one sets it live.
            int sel = Math.Max(0, Array.FindIndex(enumOpts, o => string.Equals(o, e.Value, StringComparison.OrdinalIgnoreCase)));
            var ddObj = UIFactory.CreateDropdown(ctl, $"BeelzCfgDD_{e.Key}", out var dd, enumOpts[sel], Theme.ScaledUI(11),
                i => { if (i >= 0 && i < enumOpts.Length) BeelzClient.SendUser($".beelz admin set {key} {enumOpts[i]}"); }, enumOpts);
            UIFactory.SetLayoutElement(ddObj, minWidth: 150, preferredWidth: 180, flexibleWidth: 0, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
            BeelzDropdownNoWrap(dd); dd.SetValueWithoutNotify(sel);
            Raphael.UI.Forms.FormDropdownRegistry.Register(dd);
        }
        else
        {
            var input = UIFactory.CreateInputField(ctl, $"BeelzCfgInput_{e.Key}", e.Value); // placeholder shows current value
            UIFactory.SetLayoutElement(input.GameObject, minWidth: 180, preferredWidth: 300, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
            string typed = e.Value; string current = e.Value;
            input.OnValueChanged += (string v) => typed = v;
            AddBeelzSmallButton(ctl, $"BeelzCfgSet_{e.Key}", "Set",
                $"Set {e.Key} to the typed value (admin set). Leave blank to keep current.",
                () => { var val = string.IsNullOrWhiteSpace(typed) ? current : typed.Trim(); BeelzClient.SendUser($".beelz admin set {key} {val}"); }, 48);
        }
    }

    // ---- Broadcast announcement pools (Beelz v0.100 `admin broadcast-msg`) ----
    // The pooled Broadcast_*_Messages config VALUE is SafeToken-mangled over `api config` (so the old
    // "edit the joined string" editor showed garbled text). Beelzebub instead manages each pool by index:
    //   .beelz admin broadcast-msg <complete|leaderboard> <list|add "<text>"|remove <n>|edit <n> "<text>">
    // `list` replies as plain chat (a "=== … ===" header + "  [n] <text>" lines). Raphael captures that reply
    // (see MessageService.IsBeelzBroadcastListCommand) into PlayerStateService.LastResponse and parses the
    // numbered messages here, so the editor shows real rows with per-row Edit/Remove + an Add field.
    private readonly Dictionary<string, List<string>> _beelzBroadcastPool = new(StringComparer.OrdinalIgnoreCase); // pool -> messages (1-indexed in UI)
    private readonly HashSet<string> _beelzBroadcastRequested = new(StringComparer.OrdinalIgnoreCase);             // pools we've auto-listed once

    // Map a config KEY (Broadcast_CollectionComplete_Messages / Broadcast_Leaderboard_Messages) to the
    // broadcast-msg pool token the server expects.
    private static string BeelzBroadcastPoolForKey(string key)
        => key.IndexOf("Leaderboard", StringComparison.OrdinalIgnoreCase) >= 0 ? "leaderboard" : "complete";

    // Capture handler: route a captured Beelz plain-text reply (broadcast-msg list / tform abilities) to
    // the matching parser, then refresh the owning tab so its editor rows render.
    private void OnBeelzLastResponse()
    {
        var r = Raphael.Services.PlayerStateService.LastResponse;
        string cmd = r.Command ?? "";
        if (Raphael.Services.MessageService.IsBeelzBroadcastListCommand(cmd))
        {
            // ".beelz admin broadcast-msg <pool> list" → pool is the 4th token.
            var toks = cmd.Split(' ');
            if (toks.Length < 5) return;
            string pool = toks[3];
            var msgs = new List<string>();
            var rx = new System.Text.RegularExpressions.Regex(@"^\s*\[(\d+)\]\s+(.*)$");
            if (r.Lines != null)
                foreach (var raw in r.Lines)
                {
                    var m = rx.Match(StripTags(raw ?? ""));
                    if (m.Success) msgs.Add(m.Groups[2].Value.Trim());
                }
            // An empty pool replies "no custom messages…" (no [n] lines) → store an empty list (still "loaded").
            _beelzBroadcastPool[pool] = msgs;
            if (BeelzTabVisible(PanelType.BeelzAdminConfigTab)) RebuildBeelzAdminConfig();
            return;
        }
        if (Raphael.Services.MessageService.IsBeelzTformAbilitiesCommand(cmd))
        {
            // ".beelz tform <unit> abilities" → <unit> is the 3rd token (the index we sent).
            var toks = cmd.Split(' ');
            if (toks.Length < 4) return;
            string unit = toks[2];
            var kit = new List<BeelzTformKitEntry>();
            // Kit line: "  [i] <name> (id <guid>)".
            var rx = new System.Text.RegularExpressions.Regex(@"^\s*\[(\d+)\]\s+(.+?)\s+\(id\s+(-?\d+)\)\s*$");
            if (r.Lines != null)
                foreach (var raw in r.Lines)
                {
                    var m = rx.Match(StripTags(raw ?? ""));
                    if (m.Success && int.TryParse(m.Groups[1].Value, out int idx) && int.TryParse(m.Groups[3].Value, out int gid))
                        kit.Add(new BeelzTformKitEntry(idx, m.Groups[2].Value.Trim(), gid));
                }
            _beelzTformKit[unit] = kit;
            if (BeelzTabVisible(PanelType.BeelzTransformsTab)) RebuildBeelzTformKitList();
            return;
        }
    }

    private void RequestBeelzBroadcastList(string pool)
    {
        _beelzBroadcastRequested.Add(pool);
        // api22: structured read; older: the human-text `... list` capture path.
        if (BeelzState.SupportsStructuredReads) BeelzClient.RequestBroadcastMsgs(pool);
        else BeelzClient.SendUser($".beelz admin broadcast-msg {pool} list");
    }

    private void OnBeelzBroadcastMsgsChanged()
    {
        if (BeelzTabVisible(PanelType.BeelzAdminConfigTab)) RebuildBeelzAdminConfig();
    }

    // Unified current-messages list for a pool: structured (api22) preferred, else the human-text fallback.
    // Returns 1-based (index, text) rows; loaded=false until a read has landed.
    private List<(int Idx, string Text)> GetBeelzBroadcastMessages(string pool, out bool loaded)
    {
        var outl = new List<(int, string)>();
        if (BeelzState.SupportsStructuredReads)
        {
            if (BeelzState.TryGetBroadcastMsgs(pool, out var msgs)) { loaded = true; foreach (var m in msgs) outl.Add((m.Index, m.Text)); }
            else loaded = false;
        }
        else if (_beelzBroadcastPool.TryGetValue(pool, out var list)) { loaded = true; for (int i = 0; i < list.Count; i++) outl.Add((i + 1, list[i])); }
        else loaded = false;
        return outl;
    }

    // ---- Custom transform loadout editor (Beelz v0.100 `.beelz tform`) ----
    // Beelz api22 exposes STRUCTURED reads: `api tform-kit <unit>` (the form's full kit) + `api tform-binds
    // <unit>` (the player's CURRENT custom binds per phase/slot) — so the editor shows what's bound and lets
    // you clear individual slots. On an older server (api<22) it falls back to parsing the human-text
    // `tform <unit> abilities` reply (kit only, build-and-apply). Mutations are always the chat commands
    // `set <phase> <slot> <index>` / `clear <phase> <slot>` / `defaults`.
    private readonly struct BeelzTformKitEntry
    {
        public BeelzTformKitEntry(int idx, string name, int guid) { Index = idx; Name = name; Guid = guid; }
        public int Index { get; } public string Name { get; } public int Guid { get; }
    }
    private readonly Dictionary<string, List<BeelzTformKitEntry>> _beelzTformKit = new(StringComparer.Ordinal); // human-text fallback: unit guid -> kit
    private readonly HashSet<string> _beelzTformRequested = new(StringComparer.Ordinal);                        // units requested once
    private string _beelzTformSelUnit = "";   // the selected transform's UNIT GUID (string) — sent as the <unit> token
    private int _beelzTformSelPhase = 1;      // 1 or 2 (forms have up to two phases)
    private int _beelzTformSelSlot = 1;       // 0=primary, 1-6 spells, 7=ultimate
    private GameObject _beelzTformKitContainer;
    private TextMeshProUGUI _beelzTformStatus;

    // Request a form's kit (+ current binds when the server supports it). api22 = structured reads; older =
    // the human-text kit parse (captured via OnBeelzLastResponse).
    private void RequestBeelzTform(string unitGuid)
    {
        if (string.IsNullOrEmpty(unitGuid)) return;
        _beelzTformRequested.Add(unitGuid);
        if (BeelzState.SupportsStructuredReads) { BeelzClient.RequestTformKit(unitGuid); BeelzClient.RequestTformBinds(unitGuid); }
        else BeelzClient.SendUser($".beelz tform {unitGuid} abilities");
    }

    private void BuildBeelzTransformLoadoutEditor(GameObject page)
    {
        var card = AddCard(page, "BeelzTformLoadoutCard", padding: 4, innerSpacing: 2);
        AddSectionHeading(card, "Customize transform loadout");
        bool structured = BeelzState.SupportsStructuredReads;
        AddBodyText(card,
            "Build a custom ability set for one of YOUR transforms: pick a form, a phase, and a slot " +
            "(0 = primary/left-click, 7 = ultimate), then bind one of that form's abilities below. " +
            $"({Mono(".beelz tform")} {Mono("set")} / {Mono("clear")} / {Mono("defaults")}.) " +
            (structured
                ? "Your current binds are shown per phase below; binding/clearing updates live if you're in the form, else re-enter it."
                : "<color=#FFB070>This server is too old to report your current binds, so this is build-and-apply</color> — watch chat for each confirmation."));

        var transforms = BeelzState.Transforms;
        if (!BeelzState.Present) { AddSimpleRow(card, "(Beelzebub not detected)", italic: true); return; }
        if (transforms.Count == 0) { AddSimpleRow(card, "(no transforms unlocked yet — defeat a transform boss first)", italic: true); return; }

        // Default the selected unit to the first transform's GUID if unset / stale.
        bool selValid = false;
        foreach (var t in transforms) if (t.UnitGuid == _beelzTformSelUnit) { selValid = true; break; }
        if (!selValid) _beelzTformSelUnit = transforms[0].UnitGuid;

        // Row 1: form selector + refresh.
        var selRow = UIFactory.CreateHorizontalGroup(card, "BeelzTformSelRow",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 2, 0, 0));
        UIFactory.SetLayoutElement(selRow, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var formNames = new List<string>();
        var formUnits = new List<string>();
        foreach (var t in transforms)
        {
            formNames.Add(BeelzUnitName(t.UnitGuid, t.UnitName));
            formUnits.Add(t.UnitGuid);
        }
        int selIdx = Math.Max(0, formUnits.IndexOf(_beelzTformSelUnit));
        var formDdObj = UIFactory.CreateDropdown(selRow, "BeelzTformForm", out var formDd, formNames[selIdx], Theme.ScaledUI(12),
            i => { if (i >= 0 && i < formUnits.Count) { _beelzTformSelUnit = formUnits[i]; if (!_beelzTformRequested.Contains(_beelzTformSelUnit)) RequestBeelzTform(_beelzTformSelUnit); RebuildBeelzTformKitList(); } },
            formNames.ToArray());
        UIFactory.SetLayoutElement(formDdObj, minWidth: 150, preferredWidth: 190, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        BeelzDropdownNoWrap(formDd); formDd.SetValueWithoutNotify(selIdx);
        Raphael.UI.Forms.FormDropdownRegistry.Register(formDd);
        AddBeelzSmallButton(selRow, "BeelzTformRefresh", "Refresh", "Re-read this form's kit + your current binds from the server.",
            () => RequestBeelzTform(_beelzTformSelUnit), 80);

        // Row 2: phase + slot + clear + defaults.
        var psRow = UIFactory.CreateHorizontalGroup(card, "BeelzTformPSRow",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 2, 0, 0));
        UIFactory.SetLayoutElement(psRow, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var phaseOpts = new[] { "Phase 1", "Phase 2" };
        int phaseSel = Math.Clamp(_beelzTformSelPhase - 1, 0, 1);
        var phaseDdObj = UIFactory.CreateDropdown(psRow, "BeelzTformPhase", out var phaseDd, phaseOpts[phaseSel], Theme.ScaledUI(12),
            i => { _beelzTformSelPhase = i + 1; RebuildBeelzTformKitList(); }, phaseOpts);
        UIFactory.SetLayoutElement(phaseDdObj, minWidth: 90, preferredWidth: 110, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        BeelzDropdownNoWrap(phaseDd); phaseDd.SetValueWithoutNotify(phaseSel);
        Raphael.UI.Forms.FormDropdownRegistry.Register(phaseDd);
        var slotNames = new List<string>();
        var slotVals = new List<int>();
        foreach (int n in BeelzSlotOrder) { slotNames.Add($"{BeelzSlotBtn(n)}. {BeelzSlotLabel(n)}"); slotVals.Add(n); }
        int slotSel = Math.Max(0, slotVals.IndexOf(_beelzTformSelSlot));
        var slotDdObj = UIFactory.CreateDropdown(psRow, "BeelzTformSlot", out var slotDd, slotNames[slotSel], Theme.ScaledUI(12),
            i => { if (i >= 0 && i < slotVals.Count) _beelzTformSelSlot = slotVals[i]; }, slotNames.ToArray());
        UIFactory.SetLayoutElement(slotDdObj, minWidth: 120, preferredWidth: 150, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        BeelzDropdownNoWrap(slotDd); slotDd.SetValueWithoutNotify(slotSel);
        Raphael.UI.Forms.FormDropdownRegistry.Register(slotDd);
        AddBeelzSmallButton(psRow, "BeelzTformClear", "Clear slot", "Clear the selected phase+slot back to the curated default (tform <unit> clear <phase> <slot>).",
            () => { BeelzClient.SendUser($".beelz tform {_beelzTformSelUnit} clear {_beelzTformSelPhase} {_beelzTformSelSlot}");
                    SetBeelzTformStatus($"<color=#90EE90>Cleared phase {_beelzTformSelPhase} {BeelzSlotLabel(_beelzTformSelSlot)}.</color>"); RefreshBeelzTformBinds(); }, 88);
        AddBeelzConfirmButton(psRow, "BeelzTformDefaults", "Reset form", "Reset THIS form's whole loadout to the curated defaults (tform <unit> defaults).",
            () => { BeelzClient.SendUser($".beelz tform {_beelzTformSelUnit} defaults");
                    SetBeelzTformStatus("<color=#90EE90>Reset this form to the curated defaults.</color>"); RefreshBeelzTformBinds(); }, 84, new Color(0.5f, 0.35f, 0.15f));

        _beelzTformStatus = AddInfoLabel(card, "BeelzTformStatus", "", FontStyles.Italic, Theme.ScaledUI(11));
        _beelzTformStatus.gameObject.SetActive(false);

        // Kit list (each ability → "Bind here" to the selected phase+slot).
        _beelzTformKitContainer = UIFactory.CreateVerticalGroup(card, "BeelzTformKitRows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true, spacing: 2, padding: new Vector4(0, 2, 2, 2));
        UIFactory.SetLayoutElement(_beelzTformKitContainer, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 40, flexibleHeight: 0);

        if (!_beelzTformRequested.Contains(_beelzTformSelUnit)) RequestBeelzTform(_beelzTformSelUnit);
        RebuildBeelzTformKitList();
    }

    private void SetBeelzTformStatus(string msg)
    {
        if (_beelzTformStatus == null) return;
        _beelzTformStatus.text = msg;
        _beelzTformStatus.gameObject.SetActive(!string.IsNullOrEmpty(msg));
    }

    // After a set/clear/defaults, re-pull the structured binds so the current-binds display refreshes.
    private void RefreshBeelzTformBinds()
    {
        if (BeelzState.SupportsStructuredReads && !string.IsNullOrEmpty(_beelzTformSelUnit))
            BeelzClient.RequestTformBinds(_beelzTformSelUnit);
    }

    private void RebuildBeelzTformKitList()
    {
        if (!BeelzTabVisible(PanelType.BeelzTransformsTab)) return;
        if (_beelzTformKitContainer == null) return;
        ClearChildren(_beelzTformKitContainer);
        bool structured = BeelzState.SupportsStructuredReads;

        // ---- Current binds for the selected phase (structured api22 only) ----
        if (structured && BeelzState.TryGetTformBinds(_beelzTformSelUnit, out var binds))
        {
            AddBeelzRowLabel(MakeBeelzRow(_beelzTformKitContainer, "BeelzTformBindsHdr"),
                $"<color={Theme.MutedBodyHex}><b>Current binds — phase {_beelzTformSelPhase}</b> (empty = curated default)</color>");
            foreach (int n in BeelzSlotOrder)
            {
                BeelzTformBind bound = null;
                foreach (var b in binds) if (b.Phase == _beelzTformSelPhase && b.Slot == n) { bound = b; break; }
                var row = MakeBeelzRow(_beelzTformKitContainer, $"BeelzTformBind_{n}");
                string ability = bound == null
                    ? $"<color={Theme.MutedBodyHex}>(default)</color>"
                    : BeelzNames.Ability(bound.AbilityName);
                AddBeelzRowLabel(row, $"<color={Theme.MutedBodyHex}>{BeelzSlotBtn(n)}. {BeelzSlotLabel(n)}</color>   {ability}");
                if (bound != null)
                {
                    int slotN = n;
                    AddBeelzSmallButton(row, $"BeelzTformBindClear_{n}", "Clear",
                        $"Clear {BeelzSlotLabel(slotN)} of phase {_beelzTformSelPhase} (reverts to the curated default).",
                        () => { BeelzClient.SendUser($".beelz tform {_beelzTformSelUnit} clear {_beelzTformSelPhase} {slotN}");
                                SetBeelzTformStatus($"<color=#90EE90>Cleared {BeelzSlotLabel(slotN)}.</color>"); RefreshBeelzTformBinds(); },
                        56, new Color(0.55f, 0.18f, 0.18f));
                }
            }
            AddSimpleRow(_beelzTformKitContainer, $"<color={Theme.MutedBodyHex}>— bind from this form's kit below —</color>");
        }

        // ---- The form's kit (structured if available, else the human-text fallback) ----
        var kitDisplay = new List<(int Index, string Name, string Guid)>();
        if (structured && BeelzState.TryGetTformKit(_beelzTformSelUnit, out var skit))
            foreach (var ab in skit) kitDisplay.Add((ab.Index, ab.AbilityName, ab.AbilityGuid));
        else if (!structured && _beelzTformKit.TryGetValue(_beelzTformSelUnit, out var hkit))
            foreach (var ab in hkit) kitDisplay.Add((ab.Index, ab.Name, ab.Guid.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        else { AddSimpleRow(_beelzTformKitContainer, "(loading this form's abilities… click Refresh if this persists)", italic: true); return; }

        if (kitDisplay.Count == 0) { AddSimpleRow(_beelzTformKitContainer, "(this form has no eligible abilities)", italic: true); return; }
        foreach (var ab in kitDisplay)
        {
            var row = MakeBeelzRow(_beelzTformKitContainer, $"BeelzTformKit_{ab.Index}");
            AddBeelzRowLabel(row, $"<b>{BeelzNames.Ability(ab.Name)}</b>   <color={Theme.MutedBodyHex}>[{ab.Guid}]</color>");
            int abilIdx = ab.Index; string abilName = ab.Name;
            AddBeelzSmallButton(row, $"BeelzTformBindBtn_{ab.Index}", "Bind here",
                "Bind this ability to the selected phase + slot above (tform <unit> set <phase> <slot> <index>).",
                () => { BeelzClient.SendUser($".beelz tform {_beelzTformSelUnit} set {_beelzTformSelPhase} {_beelzTformSelSlot} {abilIdx}");
                        SetBeelzTformStatus($"<color=#90EE90>Bound {BeelzNames.Ability(abilName)} → phase {_beelzTformSelPhase}, {BeelzSlotLabel(_beelzTformSelSlot)}.</color>"); RefreshBeelzTformBinds(); }, 78);
        }
    }

    // Strip TMP <color>/<b>/… tags so the [n] regex matches whether or not the line is styled.
    private static string StripTags(string s)
        => string.IsNullOrEmpty(s) ? "" : System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");

    // Editor for a broadcast message pool (Beelz v0.100). Driven by `admin broadcast-msg`.
    private void BuildBeelzMessagePoolEditor(GameObject wrap, BeelzConfigEntry e)
    {
        string key = e.Key;
        string pool = BeelzBroadcastPoolForKey(key);
        bool leaderboard = pool.Equals("leaderboard", StringComparison.OrdinalIgnoreCase);
        string tokens = leaderboard
            ? "<b>%top%</b> = the ranked list  ·  <b>%count%</b> = number of entries"
            : "<b>%player%</b> = the player's name";
        var help = UIFactory.CreateLabel(wrap, $"BeelzMsgHelp_{key}",
            $"<color={Theme.MutedBodyHex}>The server broadcasts <b>one of these messages at random</b>. Each is managed " +
            $"individually below. Placeholders: {tokens}. (Empty pool → the built-in default is used.)</color>",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(10));
        UIFactory.SetLayoutElement(help.GameObject, minWidth: 320, preferredWidth: 380, flexibleWidth: 1, minHeight: 14, flexibleHeight: 0);
        help.TextMesh.enableWordWrapping = true;

        // Auto-list once per pool so the rows populate without a manual click; Refresh re-pulls.
        var topRow = MakeBeelzRow(wrap, $"BeelzMsgTop_{key}");
        AddBeelzSmallButton(topRow, $"BeelzMsgRefresh_{key}", "Refresh list",
            "Re-read the current messages from the server.",
            () => RequestBeelzBroadcastList(pool), 96);
        // ⚠ ADMIN-GATE THE AUTO-PROBE. `api broadcast-msgs` is a VCF adminOnly endpoint; firing it for a
        // non-admin makes VCF reply "[vcf] [denied] broadcast-msgs" in THEIR chat — and because the panel
        // opens to the last-active mod tab on login, a non-admin who left off on the Beelz Admin Config tab
        // sees that denial every login (BCH_INTEGRATION_HANDOFF login-noise report, Beelz v0.131). The manual
        // Refresh button above is already CanvasGroup-disabled for non-admins by BeginAdminGate, but this
        // auto-fire is a raw Send during the editor BUILD and bypasses that — so guard it on IsLocalAdmin().
        if (!_beelzBroadcastRequested.Contains(pool) && BeelzState.Present && Services.MessageService.IsLocalAdmin())
            RequestBeelzBroadcastList(pool);

        // Current messages — each with Edit + Remove. Source = structured (api22) or human-text fallback.
        var msgs = GetBeelzBroadcastMessages(pool, out bool loaded);
        if (loaded)
        {
            if (msgs.Count == 0)
                AddSimpleRow(wrap, "(no custom messages — the built-in default is used. Add one below.)", italic: true);
            foreach (var (idx, current) in msgs)
            {
                int idxN = idx;                  // server is 1-indexed
                var msgRow = UIFactory.CreateVerticalGroup(wrap, $"BeelzMsgRow_{key}_{idxN}",
                    forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
                    spacing: 1, padding: new Vector4(2, 2, 2, 2));
                UIFactory.SetLayoutElement(msgRow, minWidth: 320, preferredWidth: 380, flexibleWidth: 1, minHeight: 24, flexibleHeight: 0);
                var edit = UIFactory.CreateInputField(msgRow, $"BeelzMsgEdit_{key}_{idxN}", current);
                edit.Text = current;
                UIFactory.SetLayoutElement(edit.GameObject, minWidth: 300, preferredWidth: 360, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
                string typed = current;
                edit.OnValueChanged += (string v) => typed = v;
                var rowBtns = MakeBeelzRow(msgRow, $"BeelzMsgRowBtns_{key}_{idxN}");
                AddBeelzRowLabel(rowBtns, $"<color={Theme.MutedBodyHex}>[{idxN}]</color>");
                AddBeelzSmallButton(rowBtns, $"BeelzMsgSave_{key}_{idxN}", "Save",
                    "Replace this message (admin broadcast-msg edit <n> \"<text>\").",
                    () => { var t = (typed ?? "").Trim(); if (t.Length == 0 || t.Contains('|')) return;
                            BeelzClient.SendUser($".beelz admin broadcast-msg {pool} edit {idxN} \"{t}\""); RequestBeelzBroadcastList(pool); }, 54);
                AddBeelzConfirmButton(rowBtns, $"BeelzMsgDel_{key}_{idxN}", "Remove",
                    "Delete this message (admin broadcast-msg remove <n>).",
                    () => { BeelzClient.SendUser($".beelz admin broadcast-msg {pool} remove {idxN}"); RequestBeelzBroadcastList(pool); },
                    66, new Color(0.55f, 0.18f, 0.18f));
            }
        }
        else
        {
            AddSimpleRow(wrap, "(loading messages… click Refresh if this persists)", italic: true);
        }

        // Add a new message.
        var addRow = MakeBeelzRow(wrap, $"BeelzMsgAdd_{key}");
        var addInput = UIFactory.CreateInputField(addRow, $"BeelzMsgAddInput_{key}", "New message…");
        UIFactory.SetLayoutElement(addInput.GameObject, minWidth: 200, preferredWidth: 300, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        string addText = "";
        addInput.OnValueChanged += (string v) => addText = v;
        AddBeelzSmallButton(addRow, $"BeelzMsgAddBtn_{key}", "Add",
            "Add a new message to this pool (admin broadcast-msg add \"<text>\"). No '|' allowed.",
            () => { var t = (addText ?? "").Trim(); if (t.Length == 0 || t.Contains('|')) return;
                    BeelzClient.SendUser($".beelz admin broadcast-msg {pool} add \"{t}\""); addInput.Text = ""; RequestBeelzBroadcastList(pool); }, 48);
    }

    // ============================ ADMIN: ABILITIES (F13 mass config) ============================

    private void BuildBeelzAdminAbilityTableTab(GameObject page)
    {
        EnsureBeelzSubscribed();
        RenderAdminInfoNote(page, "Beelzebub ability config");
        if (AddBeelzAbsentNote(page)) return;
        page = BeginAdminGate(page);   // gray out + disable everything below for non-admins

        var card = AddCard(page, "BeelzAbilTableCard");
        AddSectionHeading(card, "Mass ability config");
        AddBodyText(card,
            "Edit many abilities at once. Change a cell and that row's <b>Save</b> lights up; Save sends one " +
            $"{Mono("admin ability")} command per changed field (the server takes one field per command). " +
            "Applies server-wide when Abilities_ApplyConfig is on (default) — it also changes the source NPC/boss " +
            "cast. Cooldown/Range are overrides (blank = baseline; type a number, or 'clear' to remove). " +
            "Dmg×/CD× are damage/cooldown scales. Run <b>Scan all</b> first to load every ability.");
        AddBeelzScanAllButton(card, BeelzScanTarget.AbilityTable);

        // Export the scanned ability config (overrides + scales + enabled) to the clipboard, so an admin
        // can keep a local copy / diff it / paste it elsewhere. Built from the in-memory catalog.
        var exportRow = MakeBeelzRow(card, "BeelzAbilTableExportRow");
        AddBeelzSmallButton(exportRow, "BeelzAbilExport", "Copy config → clipboard",
            "Copy a text snapshot of every scanned ability's config (enabled, cooldown/range/charges/… overrides, damage×/cooldown×, notes) to your clipboard. Run Scan all first. Only abilities with a non-baseline setting are listed, plus a header with the totals.",
            CopyBeelzAbilityConfigToClipboard, 180);
        _beelzAbilExportStatus = AddInfoLabel(exportRow, "BeelzAbilExportStatus", "", FontStyles.Italic, Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(_beelzAbilExportStatus.gameObject, minWidth: 120, preferredWidth: 180, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);

        var filterRow = UIFactory.CreateHorizontalGroup(card, "BeelzAbilTableFilter",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 4, 0, 0));
        UIFactory.SetLayoutElement(filterRow, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var search = UIFactory.CreateInputField(filterRow, "BeelzAbilTableSearch", "Filter by ability name…");
        UIFactory.SetLayoutElement(search.GameObject, minWidth: 130, preferredWidth: 190, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        search.OnValueChanged += (string v) => { _beelzAbilTableSearch = v ?? ""; _beelzAbilTablePage = 0; RebuildBeelzAbilityTable(); };
        _beelzAbilTableCategoryButton = AddBeelzSmallButton(filterRow, "BeelzAbilTableCat", FormatBeelzAbilTableCategory(),
            "Cycle category filter (from the scanned catalog).",
            () => { CycleBeelzAbilTableCategory(); _beelzAbilTablePage = 0; SetBeelzButtonText(_beelzAbilTableCategoryButton, FormatBeelzAbilTableCategory()); RebuildBeelzAbilityTable(); }, 120);
        ButtonRef enFilterBtn = null;
        enFilterBtn = AddBeelzSmallButton(filterRow, "BeelzAbilTableEn", FormatBeelzAbilTableEnabled(),
            "Cycle: All / Enabled only / Disabled only.",
            () => { _beelzAbilTableEnabled = (_beelzAbilTableEnabled + 1) % 3; _beelzAbilTablePage = 0; SetBeelzButtonText(enFilterBtn, FormatBeelzAbilTableEnabled()); RebuildBeelzAbilityTable(); }, 96);
        // api25/26: curation + source-tier filter cycles, applied CLIENT-SIDE over the already-scanned
        // abilities-all rows (no re-scan). Built ALWAYS (panel is constructed lazily — see the Review
        // header note) and shown/hidden by the gate in RebuildBeelzAbilityTable, so they appear reliably
        // and never hide everything on an older server (the filter loop also double-guards on the gates).
        _beelzAbilTableReviewButton = AddBeelzSmallButton(filterRow, "BeelzAbilTableReview", FormatBeelzAbilTableReview(),
            "Cycle review-status filter: All / Unreviewed / Reviewed / Approved / Blocked / Hidden.",
            () => { _beelzAbilTableReview = (_beelzAbilTableReview + 1) % BeelzReviewFilterValues.Length; _beelzAbilTablePage = 0; SetBeelzButtonText(_beelzAbilTableReviewButton, FormatBeelzAbilTableReview()); RebuildBeelzAbilityTable(); }, 116);
        _beelzAbilTableReviewButton.GameObject.SetActive(BeelzState.SupportsReviewMeta);
        _beelzAbilTableTierButton = AddBeelzSmallButton(filterRow, "BeelzAbilTableTier", FormatBeelzAbilTableTier(),
            "Cycle source-tier filter: All / T1 / T2 / T3 / T4 / VBlood.",
            () => { _beelzAbilTableTier = (_beelzAbilTableTier + 1) % BeelzTierFilterValues.Length; _beelzAbilTablePage = 0; SetBeelzButtonText(_beelzAbilTableTierButton, FormatBeelzAbilTableTier()); RebuildBeelzAbilityTable(); }, 100);
        _beelzAbilTableTierButton.GameObject.SetActive(BeelzState.SupportsSourceTier);

        var pageRow = MakeBeelzRow(card, "BeelzAbilTablePageRow");
        AddBeelzSmallButton(pageRow, "BeelzAbilTablePrev", "Prev", "Previous page.", () => { if (_beelzAbilTablePage > 0) { _beelzAbilTablePage--; RebuildBeelzAbilityTable(); } }, 60);
        _beelzAbilTablePageLabel = AddInfoLabel(pageRow, "BeelzAbilTablePageLbl", "", FontStyles.Normal, Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(_beelzAbilTablePageLabel.gameObject, minWidth: 140, preferredWidth: 200, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        _beelzAbilTablePageLabel.alignment = TextAlignmentOptions.Center;
        AddBeelzSmallButton(pageRow, "BeelzAbilTableNext", "Next", "Next page.", () => { _beelzAbilTablePage++; RebuildBeelzAbilityTable(); }, 60);

        // column header
        var hdr = MakeBeelzRow(card, "BeelzAbilTableHdr");
        GameObject HCol(string t, int min, int pref, int flex)
        {
            var l = UIFactory.CreateLabel(hdr, $"BeelzAbilHdr_{t}", $"<color={Theme.MutedBodyHex}><b>{t}</b></color>", TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(l.GameObject, minWidth: min, preferredWidth: pref, flexibleWidth: flex, minHeight: Theme.ScaledHeight(16), preferredHeight: Theme.ScaledHeight(18), flexibleHeight: 0);
            l.TextMesh.enableWordWrapping = false; l.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
            return l.GameObject;
        }
        // Slim overview columns (everything else lives in the expanded sub-form): [expand] Ability · ID · Unit · Enabled.
        HCol("", ABILT_EXPAND_W, ABILT_EXPAND_W, 0);
        HCol("Ability", ABILT_NAME_MIN, ABILT_NAME_PREF, 1);
        // Beelz v0.100 streams the numeric ability GUID on catalog-ability (a=), so this column shows the
        // real ID for any ability the server resolved; it falls back to the prefab/asset name (also usable
        // directly in `.beelz admin ability <name>`) when the GUID is unknown. See BeelzAbilResolvedGuid.
        HCol("Asset / ID", ABILT_ID_MIN, ABILT_ID_PREF, 0);
        HCol("Unit", ABILT_UNIT_MIN, ABILT_UNIT_PREF, 0);
        // api25 (Beelz v0.112): curation column (status + audit tag). Combined into ONE column to keep the
        // fixed-pixel table within the panel width; the row tooltip shows the same data in full. Built
        // ALWAYS (the panel is constructed lazily on first open, possibly during the handshake window when
        // ApiVersion isn't known yet) and shown/hidden by the gate in RebuildBeelzAbilityTable — which runs
        // on every tab-show + scan, both post-handshake — so it appears reliably and never shows empty on
        // an older server. The row cell uses the same live gate, so header + cells always agree.
        _beelzAbilReviewHeaderGo = HCol("Review", ABILT_REVIEW_MIN, ABILT_REVIEW_PREF, 0);
        _beelzAbilReviewHeaderGo.SetActive(BeelzState.SupportsReviewMeta);
        HCol("Enabled", ABILT_EN_W, ABILT_EN_W, 0);

        AddSpacer(page, 4);
        var rowsCard = AddCard(page, "BeelzAbilTableRowsCard", padding: 4, innerSpacing: 2);
        _beelzAbilTableContainer = UIFactory.CreateVerticalGroup(rowsCard, "BeelzAbilTableRows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true, spacing: 2, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(_beelzAbilTableContainer, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 60, flexibleHeight: 0);

        RebuildBeelzAbilityTable();
    }

    private string FormatBeelzAbilTableCategory() => $"Cat: {(_beelzAbilTableCategory.Length == 0 ? "All" : _beelzAbilTableCategory)}";
    private string FormatBeelzAbilTableEnabled() => _beelzAbilTableEnabled switch { 1 => "On only", 2 => "Off only", _ => "On+Off" };
    private string FormatBeelzAbilTableReview() => $"Review: {(_beelzAbilTableReview == 0 ? "All" : BeelzReviewFilterValues[_beelzAbilTableReview])}";
    private string FormatBeelzAbilTableTier()   => $"Tier: {(_beelzAbilTableTier == 0 ? "All" : BeelzTierFilterValues[_beelzAbilTableTier])}";
    // Tier filter: "VBlood" matches the is_vblood flag; T1–T4 match the source_tier band.
    private static bool BeelzMatchesTierFilter(BeelzCatalogAbility c, string sel)
        => sel.Equals("VBlood", StringComparison.OrdinalIgnoreCase) ? c.IsVBlood
         : sel.Equals(c.SourceTier, StringComparison.OrdinalIgnoreCase);
    // Categories for the Admin: Abilities table come from the ADMIN scope (every ability incl. disabled),
    // not the player collectible catalog — so the filter covers disabled-ability categories too.
    private List<string> BeelzAbilTableCategories()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (BeelzState.CatalogAllLoaded)
            foreach (var c in BeelzState.CatalogAllAbilities.Values)
                if (!string.IsNullOrEmpty(c.Category)) set.Add(c.Category);
        return new List<string>(set);
    }
    private void CycleBeelzAbilTableCategory()
    {
        var cats = BeelzAbilTableCategories();
        if (cats.Count == 0) { _beelzAbilTableCategory = ""; return; }
        int idx = string.IsNullOrEmpty(_beelzAbilTableCategory) ? -1 : cats.FindIndex(x => x.Equals(_beelzAbilTableCategory, StringComparison.OrdinalIgnoreCase));
        idx++;
        _beelzAbilTableCategory = idx >= cats.Count ? "" : cats[idx];
    }

    private void RebuildBeelzAbilityTable()
    {
        if (!BeelzTabVisible(PanelType.BeelzAdminAbilityTableTab)) return;
        if (_beelzAbilTableContainer == null) return;

        // Re-evaluate the api25/26-gated chrome on every rebuild (tab-show + scan, both post-handshake) so
        // the Review column + curation/tier filter buttons appear reliably even if the panel was built
        // during the handshake window. The row cells below use the same live gate, so they always agree.
        if (_beelzAbilReviewHeaderGo != null) _beelzAbilReviewHeaderGo.SetActive(BeelzState.SupportsReviewMeta);
        if (_beelzAbilTableReviewButton != null) _beelzAbilTableReviewButton.GameObject.SetActive(BeelzState.SupportsReviewMeta);
        if (_beelzAbilTableTierButton != null) _beelzAbilTableTierButton.GameObject.SetActive(BeelzState.SupportsSourceTier);
        ClearChildren(_beelzAbilTableContainer);
        if (!BeelzState.Present) { AddSimpleRow(_beelzAbilTableContainer, "(Beelzebub not detected)", italic: true); if (_beelzAbilTablePageLabel != null) _beelzAbilTablePageLabel.text = "—"; return; }
        if (!BeelzState.CatalogAllLoaded) { AddSimpleRow(_beelzAbilTableContainer, "(no catalog yet — click Scan all to load every ability)", italic: true); if (_beelzAbilTablePageLabel != null) _beelzAbilTablePageLabel.text = "—"; return; }

        RebuildBeelzAbilUnitLookup();   // unit + GUID columns (from captures) — built BEFORE filtering so search-by-ID/unit works

        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
        string search = (_beelzAbilTableSearch ?? "").Trim();
        var list = new List<BeelzCatalogAbility>();
        foreach (var c in BeelzState.CatalogAllAbilities.Values)   // admin scope: every ability (incl. disabled)
        {
            if (c == null || string.IsNullOrEmpty(c.Name)) continue;
            if (_beelzAbilTableEnabled == 1 && !c.Enabled) continue;   // A7: enabled only
            if (_beelzAbilTableEnabled == 2 && c.Enabled) continue;    // A7: disabled only
            if (!string.IsNullOrEmpty(_beelzAbilTableCategory) && !_beelzAbilTableCategory.Equals(c.Category, OIC)) continue;
            // api25/26 client-side filters — double-guarded on the support gate so a lingering filter state
            // from a newer server can't hide every row after switching to an older one (buttons would be gone).
            if (BeelzState.SupportsReviewMeta && _beelzAbilTableReview != 0
                && !BeelzReviewFilterValues[_beelzAbilTableReview].Equals(c.ReviewStatus, OIC)) continue;
            if (BeelzState.SupportsSourceTier && _beelzAbilTableTier != 0
                && !BeelzMatchesTierFilter(c, BeelzTierFilterValues[_beelzAbilTableTier])) continue;
            if (search.Length > 0 && !BeelzContains(BeelzNames.Ability(c.Name), search) && !BeelzContains(c.Name, search)
                && !BeelzContains(BeelzAbilResolvedGuid(c), search) && !BeelzContains(c.Unit, search)) continue;
            list.Add(c);
        }
        list.Sort((a, b) => string.Compare(BeelzNames.Ability(a.Name), BeelzNames.Ability(b.Name), OIC));

        int pageCount = Math.Max(1, (list.Count + BEELZ_ABIL_TABLE_PAGE - 1) / BEELZ_ABIL_TABLE_PAGE);
        _beelzAbilTablePage = Math.Clamp(_beelzAbilTablePage, 0, pageCount - 1);
        if (_beelzAbilTablePageLabel != null) _beelzAbilTablePageLabel.text = list.Count == 0 ? "—" : $"Page {_beelzAbilTablePage + 1} / {pageCount}  ·  {list.Count}";
        if (list.Count == 0) { AddSimpleRow(_beelzAbilTableContainer, "(no abilities match this filter)", italic: true); return; }

        int start = _beelzAbilTablePage * BEELZ_ABIL_TABLE_PAGE, end = Math.Min(list.Count, start + BEELZ_ABIL_TABLE_PAGE);
        for (int i = start; i < end; i++) BuildBeelzAbilityConfigRow(list[i]);
    }

    // Best-effort ability-prefab-name → unit label, built from captures (the catalog carries no unit).
    // Rebuilt each table rebuild. Most uncaptured abilities have no unit here → "—" (Beelzebub would
    // need to add unit= to catalog-ability to cover them).
    private readonly Dictionary<string, string> _beelzAbilUnitLookup = new(StringComparer.OrdinalIgnoreCase);
    // Ability prefab-name → real PrefabGUID, from captures. CAPTURED abilities carry their GUID on the
    // wire (same source the Bestiary's ID column uses), so we can show the true numeric ID for them in
    // the admin table even before Beelzebub streams a= on catalog-ability. Uncaptured abilities still
    // fall back to the prefab/asset name until the server emits a=.
    private readonly Dictionary<string, string> _beelzAbilGuidLookup = new(StringComparer.OrdinalIgnoreCase);
    private void RebuildBeelzAbilUnitLookup()
    {
        _beelzAbilUnitLookup.Clear();
        _beelzAbilGuidLookup.Clear();
        foreach (var cap in BeelzState.Captures)
        {
            if (cap == null || string.IsNullOrEmpty(cap.AbilityName)) continue;
            if (!_beelzAbilUnitLookup.ContainsKey(cap.AbilityName))
            {
                string unit = !string.IsNullOrEmpty(cap.UnitLabel) ? cap.UnitLabel
                            : !string.IsNullOrEmpty(cap.UnitName) ? BeelzNames.Unit(cap.UnitName) : "";
                if (!string.IsNullOrEmpty(unit)) _beelzAbilUnitLookup[cap.AbilityName] = unit;
            }
            if (!_beelzAbilGuidLookup.ContainsKey(cap.AbilityName) && !string.IsNullOrEmpty(cap.AbilityGuid) && cap.AbilityGuid != "0")
                _beelzAbilGuidLookup[cap.AbilityName] = cap.AbilityGuid;
        }
    }

    // The numeric ability ID when we have it (catalog a= from Beelz, else captures), else "" (no GUID yet).
    private string BeelzAbilResolvedGuid(BeelzCatalogAbility c)
        => !string.IsNullOrEmpty(c.AbilityGuid) ? c.AbilityGuid
         : _beelzAbilGuidLookup.TryGetValue(c.Name, out var g) ? g : "";

    private void BuildBeelzAbilityConfigRow(BeelzCatalogAbility c)
    {
        var row = MakeBeelzRow(_beelzAbilTableContainer, $"BeelzAbilCfg_{c.Name}");
        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
        string name = c.Name;

        // [+/–] expand the full per-ability sub-form (all fields + notes + cancel/reset).
        bool expanded = _beelzAbilTableExpanded.Contains(name);
        AddBeelzSmallButton(row, $"AbilEx_{c.Name}", expanded ? "–" : "+",
            "Expand the full config for this ability (all fields + notes + cancel + reset to defaults).",
            () => { if (!_beelzAbilTableExpanded.Remove(name)) _beelzAbilTableExpanded.Add(name); RebuildBeelzAbilityTable(); }, ABILT_EXPAND_W);

        // Ability (friendly name).
        var nameLbl = UIFactory.CreateLabel(row, "AbilName", BeelzNames.Ability(c.Name), TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(nameLbl.GameObject, minWidth: ABILT_NAME_MIN, preferredWidth: ABILT_NAME_PREF, flexibleWidth: 1, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        nameLbl.TextMesh.enableWordWrapping = false; nameLbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;

        // ID — the real numeric PrefabGUID when known: from Beelz `a=` (all abilities) OR from captures
        // (captured abilities carry their GUID, same as the Bestiary's ID column). Falls back to the
        // prefab/asset name only for uncaptured abilities the server hasn't sent a GUID for yet.
        string resolvedGuid = BeelzAbilResolvedGuid(c);
        string idText = !string.IsNullOrEmpty(resolvedGuid) ? resolvedGuid : c.Name;
        var idLbl = UIFactory.CreateLabel(row, "AbilId", $"<color=#9FD0FF>{idText}</color>", TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(10));
        UIFactory.SetLayoutElement(idLbl.GameObject, minWidth: ABILT_ID_MIN, preferredWidth: ABILT_ID_PREF, flexibleWidth: 0, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        idLbl.TextMesh.enableWordWrapping = false; idLbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;

        // Unit — from the scanned catalog (unit=/unitguid=) when present, else best-effort from captures,
        // else "—". So once Beelzebub streams the source unit, EVERY row shows its owner, not just captured.
        string unit = !string.IsNullOrEmpty(c.Unit) ? BeelzUnitName(c.UnitGuid, c.Unit)
                    : _beelzAbilUnitLookup.TryGetValue(c.Name, out var u) ? u : "—";
        var unitLbl = UIFactory.CreateLabel(row, "AbilUnit", $"<color={Theme.MutedBodyHex}>{unit}</color>", TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(10));
        UIFactory.SetLayoutElement(unitLbl.GameObject, minWidth: ABILT_UNIT_MIN, preferredWidth: ABILT_UNIT_PREF, flexibleWidth: 0, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        unitLbl.TextMesh.enableWordWrapping = false; unitLbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;

        // api25 Review column — status (colored) + audit tag (muted). Gated to align with the header.
        if (BeelzState.SupportsReviewMeta)
        {
            string rvText = $"<color={BeelzReviewHex(c.ReviewStatus)}>{(string.IsNullOrEmpty(c.ReviewStatus) ? "—" : c.ReviewStatus)}</color>";
            if (!string.IsNullOrEmpty(c.ReviewTag)) rvText += $" <color={Theme.MutedBodyHex}>· {c.ReviewTag}</color>";
            var revLbl = UIFactory.CreateLabel(row, "AbilReview", rvText, TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(10));
            UIFactory.SetLayoutElement(revLbl.GameObject, minWidth: ABILT_REVIEW_MIN, preferredWidth: ABILT_REVIEW_PREF, flexibleWidth: 0, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
            revLbl.TextMesh.enableWordWrapping = false; revLbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        }

        // Enabled — quick inline toggle that APPLIES IMMEDIATELY (single, reversible field). Everything
        // else is edited in the expanded sub-form (with its own Save / Cancel). ALWAYS send on change
        // (not "only if it differs from the build-time value") — otherwise toggling false→true after a
        // prior change would match the original and silently NOT re-enable. SetValueWithoutNotify below
        // seeds the initial value so this fires only on real user interaction.
        string origEnabled = c.Enabled ? "true" : "false";
        var enDd = UIFactory.CreateDropdown(row, "AbilEn", out var dd, origEnabled, Theme.ScaledUI(10),
            i => { string v = i == 0 ? "true" : "false"; BeelzClient.SendUser($".beelz admin ability {name} enabled {v}"); },
            new[] { "true", "false" });
        UIFactory.SetLayoutElement(enDd, minWidth: ABILT_EN_W, preferredWidth: ABILT_EN_W, flexibleWidth: 0, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        BeelzDropdownNoWrap(dd); dd.SetValueWithoutNotify(c.Enabled ? 0 : 1);
        Raphael.UI.Forms.FormDropdownRegistry.Register(dd);
        TooltipHover.Attach(enDd, BeelzAbilFieldTooltips["enabled"] + " Applies immediately when you change it.");

        string idLine = !string.IsNullOrEmpty(resolvedGuid) ? $"GUID: {resolvedGuid}\nPrefab: {c.Name}" : $"ID (prefab): {c.Name}";
        var tip = new System.Text.StringBuilder();
        tip.Append($"{BeelzNames.Ability(c.Name)}\n{idLine}\nUnit: {unit}\n");
        AppendBeelzMetaLines(tip, c.Condition, c.ConditionMods, c.ConditionSource,
            c.ReviewStatus, c.ReviewTag, c.SourceLevel, c.SourceTier, c.IsVBlood);
        tip.Append("Expand (+) for full config.");
        TooltipHover.Attach(nameLbl.GameObject, tip.ToString());

        if (expanded) BuildBeelzAbilitySubForm(c);
    }

    // A6: current value of one config field for an ability, as the string the sub-form shows / sends.
    private static string BeelzAbilFieldValue(BeelzCatalogAbility c, string field)
    {
        string Fmt(float? f) => f.HasValue ? f.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "";
        var s = c.Shaping;
        switch (field)
        {
            case "enabled":       return c.Enabled ? "true" : "false";
            case "cooldown":      return Fmt(s?.Cooldown);
            case "range":         return Fmt(s?.Range);
            case "charges":       return s?.Charges?.ToString() ?? "";
            case "chargetime":    return Fmt(s?.ChargeTime);
            case "aoe":           return Fmt(s?.Aoe);
            case "projspeed":     return Fmt(s?.ProjSpeed);
            case "duration":      return Fmt(s?.Duration);
            case "healing":       return Fmt(s?.HealMult);
            case "forcetimeout":  return Fmt(s?.ForceTimeout);
            case "freelymove":    return Fmt(s?.FreeMoveSecs);
            case "interruptonhit":return string.IsNullOrEmpty(s?.InterruptOnHit) ? "auto" : s.InterruptOnHit;
            case "interruptible": return string.IsNullOrEmpty(s?.Interruptible) ? "auto" : s.Interruptible;
            case "freemove":      return (s != null && (s.FreeMove == "1" || s.FreeMove.Equals("on", StringComparison.OrdinalIgnoreCase))) ? "on" : "off";
            case "castspeed":     return s?.CastSpeed ?? "";
            case "summoncap":     return s?.SummonCap?.ToString() ?? "";
            case "summontimeout": return Fmt(s?.SummonTimeout);
            case "summonunits":   return s?.SummonUnits?.ToString() ?? "";
            case "damagescale":   return c.DamageScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            case "cooldownscale": return c.CooldownScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            // Beelz v0.101/api23: comma-separated restriction lists (wire format). A plain name = allow-list
            // (usable ONLY there); a `!`-prefixed name = block-list (usable everywhere EXCEPT there).
            case "weapons":       return c.Weapons == null ? "" : string.Join(",", c.Weapons);
            case "forms":         return c.Forms   == null ? "" : string.Join(",", c.Forms);
            case "notes":         return c.Notes ?? "";
            default:              return "";
        }
    }

    // A6: the expandable full-config sub-form for one ability — every field + notes, a Save (sends a
    // command per changed field) and Reset to defaults. Appended under the ability's row.
    private void BuildBeelzAbilitySubForm(BeelzCatalogAbility c)
    {
        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
        string name = c.Name;
        var card = AddCard(_beelzAbilTableContainer, $"BeelzAbilSub_{c.Name}", padding: 6, innerSpacing: 2);
        AddBodyText(card, $"<b>{BeelzNames.Ability(c.Name)}</b> <color={Theme.MutedBodyHex}>— full config (prefab {c.Name}). One command per changed field on Save.</color>");

        var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var revert = new List<Action>();   // reset every control to its original value (the Cancel button)
        ButtonRef saveBtn = null;
        ButtonRef cancelBtn = null;
        void RefreshButtons()
        {
            bool dirty = changes.Count > 0;
            if (saveBtn?.Component != null) saveBtn.Component.interactable = dirty;
            if (cancelBtn?.Component != null) cancelBtn.Component.interactable = dirty;
        }
        void Mark(string field, string orig, string cur)
        {
            cur = (cur ?? "").Trim();
            if (string.Equals(cur, orig ?? "", OIC)) changes.Remove(field); else changes[field] = cur;
            RefreshButtons();
        }

        foreach (var f in BeelzAbilFullFields)
        {
            string orig = BeelzAbilFieldValue(c, f.Field);
            var fr = MakeBeelzRow(card, $"AbilSubRow_{c.Name}_{f.Field}");
            var lbl = UIFactory.CreateLabel(fr, $"AbilSubLbl_{f.Field}", $"<color={Theme.MutedBodyHex}>{f.Label}</color>", TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(lbl.GameObject, minWidth: 120, preferredWidth: 130, flexibleWidth: 0, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
            lbl.TextMesh.enableWordWrapping = false; lbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
            // #7: per-setting tooltip (sourced from Beelzebub's ABILITY_CONFIG.md). Attached to the whole row.
            if (BeelzAbilFieldTooltips.TryGetValue(f.Field, out var tip)) TooltipHover.Attach(fr, $"{f.Label}\n{tip}");

            string field = f.Field;
            if (f.Kind == "bool" || f.Kind == "onoff" || f.Kind == "tri")
            {
                string[] opts = f.Kind == "bool" ? new[] { "true", "false" } : f.Kind == "onoff" ? new[] { "on", "off" } : new[] { "on", "off", "auto" };
                int sel = Math.Max(0, Array.FindIndex(opts, o => o.Equals(orig, OIC)));
                var ddObj = UIFactory.CreateDropdown(fr, $"AbilSubDd_{f.Field}", out var dd, opts[sel], Theme.ScaledUI(11),
                    i => { if (i >= 0 && i < opts.Length) Mark(field, orig, opts[i]); }, opts);
                UIFactory.SetLayoutElement(ddObj, minWidth: 90, preferredWidth: 110, flexibleWidth: 0, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
                BeelzDropdownNoWrap(dd); dd.SetValueWithoutNotify(sel);
                Raphael.UI.Forms.FormDropdownRegistry.Register(dd);
                revert.Add(() => { dd.SetValueWithoutNotify(sel); changes.Remove(field); });
            }
            else
            {
                var inp = UIFactory.CreateInputField(fr, $"AbilSubIn_{f.Field}", f.Kind == "text" ? "(notes)" : "(baseline)");
                // Only the free-form notes field is wide; numeric baselines get a small fixed box.
                UIFactory.SetLayoutElement(inp.GameObject, minWidth: f.Kind == "text" ? 200 : 84, preferredWidth: f.Kind == "text" ? 300 : 96, flexibleWidth: f.Kind == "text" ? 1 : 0, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
                if (!string.IsNullOrEmpty(orig)) inp.Text = orig;
                inp.OnValueChanged += (string v) => Mark(field, orig, v);
                revert.Add(() => { inp.Text = orig ?? ""; changes.Remove(field); });
            }

            // #3: the current server value as a read-only reference column. The editor is pre-seeded with
            // it, but this keeps it visible while you type a new value AND — because Save re-scans this
            // ability and the table rebuilds — it updates to the new server value, confirming the change took.
            string curShown = string.IsNullOrEmpty(orig) ? "—" : orig;
            var nowLbl = UIFactory.CreateLabel(fr, $"AbilSubNow_{f.Field}", $"<color={Theme.MutedBodyHex}>now: {curShown}</color>",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(10));
            UIFactory.SetLayoutElement(nowLbl.GameObject, minWidth: 84, preferredWidth: 104, flexibleWidth: 0, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
            nowLbl.TextMesh.enableWordWrapping = false; nowLbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
            TooltipHover.Attach(nowLbl.GameObject, "The value currently on the server. After you Save, this ability re-scans and this updates to the new value — confirming the change applied.");
        }

        // #3: a status line under the buttons so a Save gives explicit feedback (and triggers a confirm re-scan).
        TextMeshProUGUI saveStatus = null;

        var btnRow = MakeBeelzRow(card, $"AbilSubBtns_{c.Name}");
        saveBtn = AddBeelzSmallButton(btnRow, $"AbilSubSave_{c.Name}", "Save changes",
            "Apply the changed fields. On api≥27 this is ONE bulk `.beelz admin ability-set` command; older " +
            "servers get one `.beelz admin ability <field> <value>` per change. Auto re-scans to confirm the new values.",
            () =>
            {
                int n = changes.Count;
                if (n == 0) return;
                ApplyBeelzAbilityChanges(name, changes);
                changes.Clear(); RefreshButtons();
                // Re-apply tuning live so the edit takes effect even if the server didn't apply it on write
                // (in-game `admin ability` edits normally apply live, but a lowered/cleared value can need this).
                BeelzClient.AdminReload();
                if (saveStatus != null)
                {
                    if (BeelzState.SupportsCatalogFilters)
                    {
                        // Re-pull JUST this ability AFTER a short delay so the read can't race the write/reload
                        // (firing it the same frame returned the pre-save value, so the table looked unchanged).
                        // On completion CatalogChanged → RebuildBeelzAbilityTable rebuilds this row with the
                        // server's post-save values, so the "now:" column reflects what actually took.
                        saveStatus.text = $"<color=#90EE90>Saved {n} change(s) — applying + re-scanning to confirm…</color>";
                        string abil = name;
                        RunBeelzDelayed(1.5f, () => { if (BeelzState.Present) BeelzProtocolService.ScanCatalogAll("search", abil); });
                    }
                    else
                    {
                        saveStatus.text = $"<color=#90EE90>Sent {n} change(s) + reload.</color> <color={Theme.MutedBodyHex}>Click \"Scan all abilities\" to refresh the shown values.</color>";
                    }
                }
            }, 110);
        // #6: Cancel — discard the typed-but-not-applied edits, resetting every control back to its
        // original value. Lights up only while there are unsaved changes. No server command is sent.
        cancelBtn = AddBeelzSmallButton(btnRow, $"AbilSubCancel_{c.Name}", "Cancel",
            "Revert this ability's fields to their current server values, discarding any unsaved edits. Sends nothing.",
            () => { foreach (var r in revert) { try { r(); } catch { } } changes.Clear(); RefreshButtons(); }, 70);
        RefreshButtons();   // both start disabled (no changes yet)
        AddBeelzConfirmButton(btnRow, $"AbilSubReset_{c.Name}", "Reset to defaults",
            "Reset ALL shaping for this ability to the shipped baseline (.beelz admin ability <name> defaults), re-apply tuning, and re-scan to refresh the shown values.",
            () =>
            {
                BeelzClient.SendUser($".beelz admin ability {name} defaults");
                BeelzClient.AdminReload();
                if (saveStatus != null) saveStatus.text = "<color=#90EE90>Reset to defaults — applying + re-scanning…</color>";
                string abil = name;
                if (BeelzState.SupportsCatalogFilters)
                    RunBeelzDelayed(1.5f, () => { if (BeelzState.Present) BeelzProtocolService.ScanCatalogAll("search", abil); });
            }, 120, new Color(0.5f, 0.35f, 0.15f));

        // Save-feedback line (empty until a Save). Captured by the Save handler above.
        saveStatus = AddInfoLabel(card, $"AbilSubStatus_{c.Name}", "", FontStyles.Italic, Theme.ScaledUI(10));
    }

    // Run `action` once after `seconds` of real time, via a self-removing per-frame ticker. Used to let a
    // server command commit before Raphael reads the result back (e.g. re-scan an ability AFTER its config save +
    // reload, so the confirm read doesn't race the write and show stale values).
    private static void RunBeelzDelayed(float seconds, Action action)
    {
        float fireAt = UnityEngine.Time.realtimeSinceStartup + seconds;
        Action ticker = null;
        ticker = () =>
        {
            if (UnityEngine.Time.realtimeSinceStartup < fireAt) return;
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(ticker);
            try { action(); }
            catch (Exception ex) { Utils.LogUtils.LogError($"RunBeelzDelayed action threw: {ex}"); }
        };
        Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(ticker);
    }

    // Apply a batch of per-ability field edits. api≥27 (Beelz v0.116): one bulk
    // `.beelz admin ability-set <id> "(f=v)…"` so a multi-field save is a single round-trip; older
    // servers fall back to one `.beelz admin ability <id> <f> <v>` per change (the universal path that
    // works on every Beelz that has the admin ability command).
    private void ApplyBeelzAbilityChanges(string idOrName, IReadOnlyDictionary<string, string> changes)
    {
        if (changes == null || changes.Count == 0) return;
        if (BeelzState.SupportsBulkAbilitySet)
            BeelzClient.AdminAbilitySet(idOrName, changes);
        else
            foreach (var kv in changes) BeelzClient.SendUser($".beelz admin ability {idOrName} {kv.Key} {kv.Value}");
    }

    // Weapon/form restriction lines. api29+ (Beelz v0.132): api info is authoritative — forms= lists ONLY
    // allowed forms and form_blocks=/weapon_blocks= carry the "!" blacklists. Older servers: fall back to the
    // catalog's api23 allow/"!"-block tokens (api info's forms= could hold INVERTED blocks there, so skip it).
    private static void AppendBeelzRestrictionLines(System.Text.StringBuilder sb, BeelzCatalogAbility c, BeelzAbilityInfo info)
    {
        IReadOnlyList<string> weapons, weaponBlocks, forms, formBlocks;
        if (info != null && BeelzState.SupportsFormBlocks)
        {
            weapons = info.Weapons; weaponBlocks = info.WeaponBlocks; forms = info.Forms; formBlocks = info.FormBlocks;
        }
        else if (c != null)
        {
            SplitBlocks(c.Weapons, out weapons, out weaponBlocks);
            SplitBlocks(c.Forms, out forms, out formBlocks);
        }
        else return;
        // weapons=Magic is the universal (non-weapon) bucket, not a restriction.
        bool wAllow = weapons != null && weapons.Count > 0 && !(weapons.Count == 1 && weapons[0].Equals("Magic", StringComparison.OrdinalIgnoreCase));
        if (wAllow) sb.Append("\nWeapons: ").Append(string.Join("/", weapons));
        if (weaponBlocks != null && weaponBlocks.Count > 0) sb.Append(wAllow ? "   " : "\n").Append("Not with: ").Append(string.Join("/", weaponBlocks));
        bool fAllow = forms != null && forms.Count > 0;
        bool fBlock = formBlocks != null && formBlocks.Count > 0;
        if (fAllow || fBlock)
        {
            sb.Append("\nUsable in: ").Append(fAllow ? string.Join("/", forms) : "any form");
            if (fBlock) sb.Append(" except ").Append(string.Join("/", formBlocks));
        }
    }

    private static void SplitBlocks(IReadOnlyList<string> tokens, out IReadOnlyList<string> allow, out IReadOnlyList<string> block)
    {
        var a = new List<string>(); var b = new List<string>();
        if (tokens != null)
            foreach (var t in tokens)
            {
                if (string.IsNullOrEmpty(t)) continue;
                if (t[0] == '!') { if (t.Length > 1) b.Add(t.Substring(1)); } else a.Add(t);
            }
        allow = a; block = b;
    }

    // Cooldown + damage summary for a hover card. api31+: the runtime cooldown precedence (override, else
    // shipped × scale, then the server floor) and no cooldown for charge-based abilities. api30+: the damage
    // factor a cast would snapshot right now (only when the server's damage mode is Scale).
    private static void AppendBeelzCooldownDamageLine(System.Text.StringBuilder sb, BeelzAbilityInfo info)
    {
        if (BeelzState.SupportsRuntimeCooldowns)
        {
            if (info.UsesCharges) sb.Append("\nUses charges");
            else if (info.CooldownSeconds > 0f || info.Shaping?.Cooldown != null) sb.Append($"\nCooldown {info.EffectiveCooldownSeconds:0.#}s");
        }
        else
        {
            if (info.CooldownSeconds > 0f) sb.Append($"\nCooldown {info.CooldownSeconds:0.#}s");
            if (info.CooldownScale != 1f) sb.Append($"   cd ×{info.CooldownScale:0.##}");
        }
        if (BeelzState.SupportsDamageMeta && info.DamageScaleEff.HasValue
            && info.DamageMode.Equals("Scale", StringComparison.OrdinalIgnoreCase))
        {
            if (Math.Abs(info.DamageScaleEff.Value - 1f) > 0.005f) sb.Append($"   dmg ×{info.DamageScaleEff.Value:0.##}");
        }
        else if (!BeelzState.SupportsDamageMeta && info.DamageScale != 1f) sb.Append($"   dmg ×{info.DamageScale:0.##}");
    }

    // Render the Beelz v0.107/v0.112/v0.113 (api24/25/26) informational metadata — activation condition,
    // curation state, source tier — as muted display lines, EACH gated on the connected server actually
    // emitting that token family. Shared by the F5 ability detail + the catalog/admin row tooltips so the
    // format stays consistent. Purely informational (none of this gates play; enabled= is the kill-switch).
    private static void AppendBeelzMetaLines(System.Text.StringBuilder sb, string condition,
        IReadOnlyList<string> conditionMods, string conditionSource, string reviewStatus, string reviewTag,
        int? sourceLevel, string sourceTier, bool isVBlood)
    {
        if (BeelzState.SupportsConditionMeta && !string.IsNullOrEmpty(condition))
        {
            sb.Append($"<color={Theme.MutedBodyHex}>Use: {condition}");
            if (conditionMods != null && conditionMods.Count > 0)
                sb.Append(" (").Append(string.Join(", ", conditionMods)).Append(')');
            if (!string.IsNullOrEmpty(conditionSource) && conditionSource.Equals("auto", StringComparison.OrdinalIgnoreCase))
                sb.Append(" · unconfirmed");   // 'auto' = classifier guess, not tester-confirmed
            sb.Append("</color>\n");
        }
        if (BeelzState.SupportsSourceTier && (!string.IsNullOrEmpty(sourceTier) || isVBlood || sourceLevel.HasValue))
        {
            sb.Append($"<color={Theme.MutedBodyHex}>Source:");
            if (sourceLevel.HasValue) sb.Append($" lvl {sourceLevel.Value}");
            if (!string.IsNullOrEmpty(sourceTier)) sb.Append($" · {sourceTier}");
            if (isVBlood) sb.Append(" · VBlood");
            sb.Append("</color>\n");
        }
        if (BeelzState.SupportsReviewMeta && !string.IsNullOrEmpty(reviewStatus))
        {
            sb.Append($"<color={BeelzReviewHex(reviewStatus)}>Review: {reviewStatus}");
            if (!string.IsNullOrEmpty(reviewTag)) sb.Append($" · {reviewTag}");
            sb.Append("</color>\n");
        }
    }

    // Display color for a review_status: red for the curation-gated states (Blocked/Hidden), green for
    // Approved, muted otherwise (Unreviewed/Reviewed/unknown). Shared by the meta tooltip + Review column.
    private static string BeelzReviewHex(string status)
    {
        if (string.IsNullOrEmpty(status)) return Theme.MutedBodyHex;
        if (status.Equals("Blocked", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Hidden", StringComparison.OrdinalIgnoreCase)) return "#FF8080";
        if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase)) return "#90EE90";
        return Theme.MutedBodyHex;
    }

    // #9: copy a text snapshot of the scanned ability config to the clipboard. Lists every ability that
    // has a non-baseline setting (enabled=false, any shaping override, or damage/cooldown scale ≠ 1),
    // plus a header with totals. Built from the in-memory catalog (run Scan all first).
    private void CopyBeelzAbilityConfigToClipboard()
    {
        try
        {
            if (!BeelzState.CatalogAllLoaded)
            {
                if (_beelzAbilExportStatus != null) _beelzAbilExportStatus.text = "<color=#FFD75A>Run Scan all first.</color>";
                return;
            }
            var sb = new System.Text.StringBuilder();
            int total = 0, customized = 0;
            sb.AppendLine($"# Beelzebub ability config snapshot — {BeelzState.CatalogAllAbilities.Count} abilities ({BeelzState.CatalogAllEnabledCount} enabled)");
            sb.AppendLine("# ability\tID(prefab)\tenabled\tdamage×\tcooldown×\toverrides");
            var ordered = new List<BeelzCatalogAbility>(BeelzState.CatalogAllAbilities.Values);
            ordered.Sort((a, b) => string.Compare(a?.Name, b?.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var c in ordered)
            {
                if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                total++;
                bool scaled = Math.Abs(c.DamageScale - 1f) > 0.0001f || Math.Abs(c.CooldownScale - 1f) > 0.0001f;
                bool shaped = c.Shaping != null && c.Shaping.HasAny;
                if (c.Enabled && !scaled && !shaped) continue;   // baseline → skip to keep the snapshot focused
                customized++;
                var overrides = new List<string>();
                foreach (var f in BeelzAbilFullFields)
                {
                    if (f.Field == "enabled" || f.Field == "damagescale" || f.Field == "cooldownscale" || f.Field == "notes") continue;
                    string v = BeelzAbilFieldValue(c, f.Field);
                    if (!string.IsNullOrEmpty(v) && !v.Equals("auto", StringComparison.OrdinalIgnoreCase)) overrides.Add($"{f.Field}={v}");
                }
                if (!string.IsNullOrEmpty(c.Notes)) overrides.Add($"notes=\"{c.Notes}\"");
                sb.AppendLine($"{BeelzNames.Ability(c.Name)}\t{c.Name}\t{(c.Enabled ? "true" : "false")}\t" +
                              $"{c.DamageScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\t" +
                              $"{c.CooldownScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\t" +
                              $"{string.Join(", ", overrides)}");
            }
            sb.Insert(0, $"# {customized} customized of {total} scanned\n");
            UnityEngine.GUIUtility.systemCopyBuffer = sb.ToString();
            if (_beelzAbilExportStatus != null) _beelzAbilExportStatus.text = $"<color=#90EE90>Copied {customized} customized / {total} abilities to clipboard.</color>";
        }
        catch (Exception ex)
        {
            if (_beelzAbilExportStatus != null) _beelzAbilExportStatus.text = "<color=#FF8080>Copy failed (see log).</color>";
            Utils.LogUtils.LogWarning($"CopyBeelzAbilityConfigToClipboard: {ex.Message}");
        }
    }

    // ============================ ADMIN: PLAYERS ============================

    private void BuildBeelzAdminPlayersTab(GameObject page)
    {
        EnsureBeelzSubscribed();
        RenderAdminInfoNote(page, "Beelzebub player");
        if (AddBeelzAbsentNote(page)) return;
        page = BeginAdminGate(page);   // gray out + disable everything below for non-admins

        var card = AddCard(page, "BeelzAdminPlayersCard");
        AddSectionHeading(card, "Player administration");
        AddBodyText(card,
            "Target a player by name. Unit / ability fields take integer PrefabGUIDs (the u= / a= values Beelzebub " +
            "reports). Destructive actions need a two-click confirm AND carry Beelzebub's literal confirm token, so " +
            "nothing clears by accident. The server enforces admin permission regardless.");

        _beelzAdminTargetBanners.Clear();   // F8: fresh banner set per (re)build

        // ---- target inputs (F6: searchable autocomplete; type to filter, click to fill) ----
        AddBodyText(card, $"<b>Player</b> <color={Theme.MutedBodyHex}>— type to search online players</color>");
        AddBeelzSearchableField(card, "BeelzAdminName", "Player name", _beelzAdminPlayerName,
            BeelzPlayerMatches, v => { _beelzAdminPlayerName = v ?? ""; RefreshAdminTargetBanner(); });
        AddBodyText(card, $"<b>Unit</b> <color={Theme.MutedBodyHex}>— PrefabGUID for give / devour / force-transform (type a unit name to find it)</color>");
        AddBeelzSearchableField(card, "BeelzAdminUnit", "Unit name or GUID", _beelzAdminUnitGuid,
            BeelzUnitMatches, v => { _beelzAdminUnitGuid = v ?? ""; RefreshAdminTargetBanner(); });
        AddBodyText(card, $"<b>Ability</b> <color={Theme.MutedBodyHex}>— ID for give/revoke; name or ID for shaping (type an ability name)</color>");
        AddBeelzSearchableField(card, "BeelzAdminAbil", "Ability name or ID", _beelzAdminAbilityGuid,
            BeelzAbilityMatches, v => { _beelzAdminAbilityGuid = v ?? ""; RefreshAdminTargetBanner(); });

        _beelzAdminStatusLabel = AddInfoLabel(card, "BeelzAdminStatus", "", FontStyles.Italic, Theme.ScaledUI(11));
        _beelzAdminStatusLabel.gameObject.SetActive(false);

        AddSpacer(page, 6);

        // ---- inspect ----
        var inspectCard = AddCard(page, "BeelzAdminInspectCard"); AddSectionHeading(inspectCard, "Inspect");
        var inspectRow = MakeBeelzRow(inspectCard, "BeelzAdminInspectRow");
        AddBeelzSmallButton(inspectRow, "BeelzAdmInspect", "Inspect", "View a player's full state (admin inspect).", () => { if (ReqName(out var n)) AdminSend($".beelz admin inspect {n}"); }, 70);
        AddBeelzSmallButton(inspectRow, "BeelzAdmProgress", "Progress", "A player's collection progress (admin progress).", () => { if (ReqName(out var n)) AdminSend($".beelz admin progress {n}"); }, 76);
        AddBeelzSmallButton(inspectRow, "BeelzAdmSnapshot", "Snapshot", "Server-wide summary (admin snapshot).", () => AdminSend(".beelz admin snapshot"), 80);
        AddBeelzSmallButton(inspectRow, "BeelzAdmBuffs", "Buffs", "Dump a player's live buffs/slots to the server log (admin buffs).", () => { if (ReqName(out var n)) AdminSend($".beelz admin buffs {n}"); }, 60);

        // ---- transforms ----
        var txCard = AddCard(page, "BeelzAdminTxCard"); AddSectionHeading(txCard, "Transforms");
        var txRow = MakeBeelzRow(txCard, "BeelzAdminTxRow");
        AddBeelzSmallButton(txRow, "BeelzAdmForceTx", "Force", "Force a transform, bypassing unlock+cooldown (admin force-transform <player> <unitGuid>). Renderable forms: Dracula, Morgana, Werewolf, Golem, Gargoyle (+ basic werewolf). Beelz v0.120: if the target is already transformed, click Clear first — a direct force is refused.", () => { if (ReqName(out var n) && ReqUnit(out var u)) AdminSend($".beelz admin force-transform {n} {u}"); }, 64);
        AddBeelzSmallButton(txRow, "BeelzAdmClearTx", "Clear", "End a player's active transform (admin clear-transform).", () => { if (ReqName(out var n)) AdminSend($".beelz admin clear-transform {n}"); }, 56);
        AddBeelzSmallButton(txRow, "BeelzAdmGiveTx", "Grant unlock", "Grant a transform unlock (admin give-transform <player> <unitGuid>). Forms: Dracula, Morgana, Werewolf, Golem, Gargoyle (+ basic werewolf).", () => { if (ReqName(out var n) && ReqUnit(out var u)) AdminSend($".beelz admin give-transform {n} {u}"); }, 96);
        AddBeelzConfirmButton(txRow, "BeelzAdmRevokeTx", "Revoke unlock", "Revoke a transform unlock (admin revoke-transform <player> <unitGuid>).", () => { if (ReqName(out var n) && ReqUnit(out var u)) AdminSend($".beelz admin revoke-transform {n} {u}"); }, 104, new Color(0.55f, 0.18f, 0.18f));

        // ---- grant / revoke / devour ----
        var grCard = AddCard(page, "BeelzAdminGrantCard"); AddSectionHeading(grCard, "Abilities");
        AddBeelzAdminTargetBanner(grCard);
        var grRow = MakeBeelzRow(grCard, "BeelzAdminGrantRow");
        AddBeelzSmallButton(grRow, "BeelzAdmDevour", "Devour", "Grant a player ALL of a unit's abilities at once (admin devour <player> <unitGuid>).", () => { if (ReqName(out var n) && ReqUnit(out var u)) AdminSend($".beelz admin devour {n} {u}"); }, 70);
        AddBeelzSmallButton(grRow, "BeelzAdmGive", "Give", "Grant one ability (admin give <player> <unitGuid> <abilityGuid>).", () => { if (ReqName(out var n) && ReqUnit(out var u) && ReqAbility(out var a)) AdminSend($".beelz admin give {n} {u} {a}"); }, 56);
        AddBeelzConfirmButton(grRow, "BeelzAdmRevoke", "Revoke", "Revoke one ability (admin revoke <player> <unitGuid> <abilityGuid>).", () => { if (ReqName(out var n) && ReqUnit(out var u) && ReqAbility(out var a)) AdminSend($".beelz admin revoke {n} {u} {a}"); }, 70, new Color(0.55f, 0.18f, 0.18f));

        // ---- per-ability shaping (server-wide; targets the Ability GUID above) ----
        var shapeCard = AddCard(page, "BeelzAdminShapeCard"); AddSectionHeading(shapeCard, "Ability shaping (server-wide)");
        AddBodyText(shapeCard,
            "Tune the ability whose GUID is in the <b>Ability GUID</b> box above (its a= value). Applies server-wide " +
            "when Abilities_ApplyConfig is on (the default) — it also changes the source NPC/boss cast. ONE field per " +
            "Set. Values are numbers, or on/off/auto for interruptible / interrupt-on-hit; type 'clear' to unset a field. " +
            "Click <b>Load current settings</b> to see the ability's live values before you change anything. " +
            "'Reset defaults' reverts ALL shaping for this ability to the shipped baseline.");
        var shapeRow = UIFactory.CreateHorizontalGroup(shapeCard, "BeelzAdminShapeRow",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 4, 0, 0));
        UIFactory.SetLayoutElement(shapeRow, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        int shapeSel = Math.Max(0, Array.IndexOf(BeelzShapeFields, _beelzAdminShapeField));
        var shapeDdObj = UIFactory.CreateDropdown(shapeRow, "BeelzShapeField", out var shapeDd,
            BeelzShapeFields[shapeSel], Theme.ScaledUI(12),
            i => { if (i >= 0 && i < BeelzShapeFields.Length) _beelzAdminShapeField = BeelzShapeFields[i]; }, BeelzShapeFields);
        UIFactory.SetLayoutElement(shapeDdObj, minWidth: 130, preferredWidth: 150, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        BeelzDropdownNoWrap(shapeDd); shapeDd.SetValueWithoutNotify(shapeSel);
        Raphael.UI.Forms.FormDropdownRegistry.Register(shapeDd);
        var shapeVal = UIFactory.CreateInputField(shapeRow, "BeelzShapeVal", "value (or 'clear')");
        UIFactory.SetLayoutElement(shapeVal.GameObject, minWidth: 96, preferredWidth: 120, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        shapeVal.OnValueChanged += (string v) => { _beelzAdminShapeValue = v ?? ""; };
        AddBeelzSmallButton(shapeRow, "BeelzShapeSet", "Set",
            "Set the chosen field on the ability (admin ability <id> <field> <value>).",
            () => { if (!ReqAbility(out var a)) return; var val = (_beelzAdminShapeValue ?? "").Trim();
                    if (val.Length == 0) { SetBeelzAdminStatus("<color=#FFB070>Enter a value (or 'clear').</color>"); return; }
                    AdminSend($".beelz admin ability {a} {_beelzAdminShapeField} {val}"); }, 48);
        AddBeelzConfirmButton(shapeRow, "BeelzShapeReset", "Reset defaults",
            "Reset ALL of this ability's shaping to the shipped baseline (admin ability <id> defaults). Curation (enabled/weapons/forms) is untouched.",
            () => { if (ReqAbility(out var a)) AdminSend($".beelz admin ability {a} defaults"); }, 110, new Color(0.5f, 0.35f, 0.15f));
        // F5: load + show the ability's CURRENT server settings (via api info-guid) so you can see what
        // you're changing before you set a value.
        var shapeRow2 = MakeBeelzRow(shapeCard, "BeelzAdminShapeRow2");
        AddBeelzSmallButton(shapeRow2, "BeelzShapeLoad", "Load current settings",
            "Fetch this ability's live server settings (cooldown/range/overrides) and show them below.",
            () => { if (!ReqAbility(out var a)) return; _beelzShapeInfoGuid = a;
                    if (BeelzState.TryGetAbilityInfo(a, out _)) RefreshShapeInfo();
                    else { if (_beelzShapeInfoLabel != null) _beelzShapeInfoLabel.text = "<color=#FFD75A>Loading…</color>"; BeelzClient.RequestInfoGuid(a); } }, 150);
        // Beelz v0.132: read-only dump of every tunable number in the ability's chain (incl. damage factors +
        // shared-prefab warnings). Plain-text chat reply — no wire tokens.
        if (BeelzState.SupportsAbilityInspect)
            AddBeelzSmallButton(shapeRow2, "BeelzShapeInspect", "Inspect",
                "Print every tunable number in this ability's chain (cooldown, cast time, charges, range, projectiles, lifetime, AoE, damage factors, stacks, knockback…) plus which prefabs it shares with other abilities (admin ability-inspect <id>). Read-only; the reply appears in chat.",
                () => { if (ReqAbility(out var a)) AdminSend($".beelz admin ability-inspect {a}"); }, 80);
        _beelzShapeInfoLabel = AddInfoLabel(shapeCard, "BeelzShapeInfo", "", FontStyles.Normal, Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(_beelzShapeInfoLabel.gameObject, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 34, flexibleHeight: 0);
        _beelzShapeInfoLabel.enableWordWrapping = true;

        // ---- incompatibility locks (Beelz v0.135 / api32) ----
        if (BeelzState.SupportsLocks)
        {
            var lockCard = AddCard(page, "BeelzAdminLockCard"); AddSectionHeading(lockCard, "Incompatibility locks");
            AddBodyText(lockCard,
                "A lock group caps how many of its abilities one player can have on their bar + hotkeys at once " +
                "(new groups start at max 1). Members are ability GUIDs or <b>cat:Category</b> (Travel, Aoe, Projectile, " +
                "Summon, Buff, WeaponSpell, Spell, Melee, Other), comma-separated. A locked bind is never deleted — it " +
                "shows the vanilla ability until the lock is lifted.");
            var lockRow = MakeBeelzRow(lockCard, "BeelzAdminLockRow");
            var lgIn = UIFactory.CreateInputField(lockRow, "BeelzLockGroup", "group name");
            UIFactory.SetLayoutElement(lgIn.GameObject, minWidth: 110, preferredWidth: 130, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
            lgIn.OnValueChanged += (string v) => { _beelzLockGroup = v ?? ""; };
            var lmIn = UIFactory.CreateInputField(lockRow, "BeelzLockMembers", "members: guid, cat:Travel, …");
            UIFactory.SetLayoutElement(lmIn.GameObject, minWidth: 180, preferredWidth: 240, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
            lmIn.OnValueChanged += (string v) => { _beelzLockMembers = v ?? ""; };
            var lxIn = UIFactory.CreateInputField(lockRow, "BeelzLockMax", "max");
            UIFactory.SetLayoutElement(lxIn.GameObject, minWidth: 44, preferredWidth: 50, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
            lxIn.OnValueChanged += (string v) => { _beelzLockMax = v ?? ""; };

            var lockRow2 = MakeBeelzRow(lockCard, "BeelzAdminLockRow2");
            AddBeelzSmallButton(lockRow2, "BeelzLockAdd", "Add / extend",
                "Create the group, or add these members to it (admin lock add <group> \"<members>\").",
                () => { if (!ReqLockGroup(out var g)) return; var m = (_beelzLockMembers ?? "").Replace("\"", "").Trim();
                        if (m.Length == 0) { SetBeelzAdminStatus("<color=#FFB070>Enter one or more members.</color>"); return; }
                        AdminSend($".beelz admin lock add {g} \"{m}\""); }, 96);
            AddBeelzSmallButton(lockRow2, "BeelzLockMaxSet", "Set max",
                "Set how many of the group's members may be active at once (admin lock max <group> <n>).",
                () => { if (!ReqLockGroup(out var g)) return;
                        if (!int.TryParse((_beelzLockMax ?? "").Trim(), out var n) || n < 1) { SetBeelzAdminStatus("<color=#FFB070>Max must be a whole number ≥ 1.</color>"); return; }
                        AdminSend($".beelz admin lock max {g} {n}"); }, 70);
            AddBeelzSmallButton(lockRow2, "BeelzLockRemoveMember", "Remove member",
                "Remove ONE member (the first entry in the members box) from the group (admin lock remove <group> <member>).",
                () => { if (!ReqLockGroup(out var g)) return; var m = (_beelzLockMembers ?? "").Split(',')[0].Replace("\"", "").Trim();
                        if (m.Length == 0) { SetBeelzAdminStatus("<color=#FFB070>Enter the member to remove.</color>"); return; }
                        AdminSend($".beelz admin lock remove {g} {m}"); }, 110);
            AddBeelzConfirmButton(lockRow2, "BeelzLockRemoveGroup", "Delete group",
                "Remove the whole lock group (admin lock remove <group>). Two-click confirm.",
                () => { if (ReqLockGroup(out var g)) AdminSend($".beelz admin lock remove {g}"); }, 96, new Color(0.55f, 0.18f, 0.18f));

            var lockRow3 = MakeBeelzRow(lockCard, "BeelzAdminLockRow3");
            AddBeelzSmallButton(lockRow3, "BeelzLockRefresh", "Refresh groups", "Re-read the lock groups (api locks) into the list below.", BeelzClient.RequestLocks, 110);
            AddBeelzSmallButton(lockRow3, "BeelzLockCheck", "Check player",
                "Show which of the target player's active abilities are locked right now, and why (admin lock check <player>). Uses the Player name box above.",
                () => { if (ReqName(out var n)) AdminSend($".beelz admin lock check {n}"); }, 100);
            _beelzLocksLabel = AddInfoLabel(lockCard, "BeelzLocksList", "", FontStyles.Normal, Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(_beelzLocksLabel.gameObject, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 20, flexibleHeight: 0);
            _beelzLocksLabel.enableWordWrapping = true;
            RefreshBeelzLocksLabel();
            if (!BeelzState.LocksLoaded) BeelzClient.RequestLocks();
        }

        // ---- reseed ability rules (Beelz v0.135) ----
        if (BeelzState.SupportsReseed)
        {
            var reseedCard = AddCard(page, "BeelzAdminReseedCard"); AddSectionHeading(reseedCard, "Reseed ability rules");
            AddBodyText(reseedCard,
                "Bring this server's ability_rules.json up to the installed Beelzebub's shipped defaults (new blocks, " +
                "label fixes, tuning). <b>Preview</b> shows what would change. <b>Merge</b> keeps your edits and takes " +
                "shipped changes you never touched. <b>Replace</b> overwrites with the shipped default — your curation is " +
                "lost. A backup is kept either way. Re-scan the ability catalog afterwards.");
            var reseedRow = MakeBeelzRow(reseedCard, "BeelzAdminReseedRow");
            AddBeelzSmallButton(reseedRow, "BeelzReseedPreview", "Preview", "Show what a reseed would change (admin reseed preview). Changes nothing.",
                () => AdminSend(".beelz admin reseed preview"), 76);
            AddBeelzConfirmButton(reseedRow, "BeelzReseedMerge", "Merge",
                "Take shipped changes for fields you never edited; keep your edits (admin reseed merge). Backup kept. Two-click confirm.",
                () => AdminSend(".beelz admin reseed merge"), 70, new Color(0.5f, 0.35f, 0.15f));
            AddBeelzConfirmButton(reseedRow, "BeelzReseedReplace", "Replace",
                "Overwrite ability_rules.json with the shipped default — ALL your curation is lost (admin reseed replace CONFIRM). Backup kept. Two-click confirm.",
                () => AdminSend(".beelz admin reseed replace CONFIRM"), 76, new Color(0.65f, 0.12f, 0.12f));
        }

        // ---- per-unit transform tuning (per-form scalars + duration/cooldown overrides) ----
        var txCfgCard = AddCard(page, "BeelzAdminTxSetCard"); AddSectionHeading(txCfgCard, "Transform tuning (per unit)");
        AddBodyText(txCfgCard,
            "Tune a transform unit by its prefab name (e.g. CHAR_Vampire_Dracula_VBlood). ONE field per Set. " +
            "Scalars (damage/cooldown/health/speed), plus per-form <b>duration</b> / <b>cooldown</b> overrides " +
            "(value in seconds, or <b>inherit</b> to clear an override and fall back to the category default). " +
            "Read current values from the Bestiary / api catalog units. (admin transform-set)");
        var txCfgRow = UIFactory.CreateHorizontalGroup(txCfgCard, "BeelzAdminTxSetRow",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 4, 0, 0));
        UIFactory.SetLayoutElement(txCfgRow, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var txUnitInput = UIFactory.CreateInputField(txCfgRow, "BeelzTxUnit", "CHAR_unit name");
        UIFactory.SetLayoutElement(txUnitInput.GameObject, minWidth: 120, preferredWidth: 150, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        txUnitInput.OnValueChanged += (string v) => { _beelzAdminTxUnit = v ?? ""; };
        int txSel = Math.Max(0, Array.IndexOf(BeelzTxSetFields, _beelzAdminTxField));
        var txDdObj = UIFactory.CreateDropdown(txCfgRow, "BeelzTxField", out var txDd,
            BeelzTxSetFields[txSel], Theme.ScaledUI(12),
            i => { if (i >= 0 && i < BeelzTxSetFields.Length) _beelzAdminTxField = BeelzTxSetFields[i]; }, BeelzTxSetFields);
        UIFactory.SetLayoutElement(txDdObj, minWidth: 120, preferredWidth: 140, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        BeelzDropdownNoWrap(txDd); txDd.SetValueWithoutNotify(txSel);
        Raphael.UI.Forms.FormDropdownRegistry.Register(txDd);
        var txValInput = UIFactory.CreateInputField(txCfgRow, "BeelzTxVal", "value");
        UIFactory.SetLayoutElement(txValInput.GameObject, minWidth: 90, preferredWidth: 110, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        txValInput.OnValueChanged += (string v) => { _beelzAdminTxValue = v ?? ""; };
        AddBeelzSmallButton(txCfgRow, "BeelzTxSetBtn", "Set",
            "Set the chosen field on the transform unit (admin transform-set <unit> <field> <value>).",
            () => { var u = (_beelzAdminTxUnit ?? "").Trim(); var val = (_beelzAdminTxValue ?? "").Trim();
                    if (u.Length == 0) { SetBeelzAdminStatus("<color=#FFB070>Enter a CHAR_ unit name first.</color>"); return; }
                    if (val.Length == 0) { SetBeelzAdminStatus("<color=#FFB070>Enter a value.</color>"); return; }
                    AdminSend($".beelz admin transform-set {u} {_beelzAdminTxField} {val}"); }, 48);

        // ---- recovery ----
        var recCard = AddCard(page, "BeelzAdminRecCard"); AddSectionHeading(recCard, "Recovery (non-destructive)");
        // Beelz v0.137 / api33: one layered reset behind reset-loadouts / purge, and a read-only `admin bar`
        // readout. rebuildslots / clearslotmods are LEGACY and rebuildbar is an alias of `bar`, so those buttons
        // go and the ladder becomes Diagnose bar -> Reset player's bar -> Purge bar -> Respawn.
        if (BeelzState.SupportsLayeredBarReset)
        {
            BuildBeelzAdminRecoveryLayered(recCard);
        }
        else
        {
            var recRow = MakeBeelzRow(recCard, "BeelzAdminRecRow");
            AddBeelzSmallButton(recRow, "BeelzAdmRespawn", "Respawn", "Respawn in place; keeps inventory / blood / captures / unlocks (admin respawn). Beelz v0.120: the GUARANTEED cure for an action bar stuck on a creature kit — even one that survived a relog (rebuilds a fresh character entity; NOT a character reset).", () => { if (ReqName(out var n)) AdminSend($".beelz admin respawn {n}"); }, 76);
            AddBeelzSmallButton(recRow, "BeelzAdmRebuild", "Rebuild slots", "Authoritatively clear the engine's cached slot values and re-apply the player's saved grants (admin rebuildslots) — Beelz v0.120's fix for a stuck bar. If a bar survives a relog, escalate to Respawn.", () => { if (ReqName(out var n)) AdminSend($".beelz admin rebuildslots {n}"); }, 100);
            AddBeelzSmallButton(recRow, "BeelzAdmClearMods", "Clear slot mods", "Clear orphaned ability-slot mods (admin clearslotmods).", () => { if (ReqName(out var n)) AdminSend($".beelz admin clearslotmods {n}"); }, 110);
            // Rebuild the player's action bar from their saved Beelzebub bindings (admin rebuildbar) — a bar-recovery
            // step alongside Rebuild slots. Two-click confirm because it rewrites the player's whole bar.
            AddBeelzConfirmButton(recRow, "BeelzAdmRebuildBar", "Rebuild bar",
                "Rebuild the player's action bar from their saved Beelzebub bindings (admin rebuildbar) — a bar-recovery " +
                "step alongside Rebuild slots. Two-click confirm. If the bar is still stuck after this, escalate to " +
                "Respawn, then Purge bar.",
                () => { if (ReqName(out var n)) AdminSend($".beelz admin rebuildbar {n}"); }, 100, new Color(0.5f, 0.35f, 0.15f));
            // Beelz v0.131: strip stuck STATE buffs (invisible / phased / immaterial) that survive respawn + relog.
            // A DIFFERENT problem from a stuck action bar — fixes a stuck PLAYER STATE, not slot bindings. Omitting
            // the buff arg removes the known culprits; non-destructive, so no confirm (like Respawn / Rebuild slots).
            var recRowCleanse = MakeBeelzRow(recCard, "BeelzAdminRecRowCleanse");
            AddBeelzSmallButton(recRowCleanse, "BeelzAdmCleanse", "Cleanse stuck buffs",
                "Strip stuck STATE buffs from a player — invisible / phased / immaterial buffs that survive respawn AND " +
                "relog (admin cleanse <player>). Removes the known culprits. Non-destructive. Use this for a stuck/invisible " +
                "PLAYER; for a stuck ACTION BAR use Rebuild slots → Respawn → Purge bar instead.",
                () => { if (ReqName(out var n)) AdminSend($".beelz admin cleanse {n}"); }, 130);
            var recRow2 = MakeBeelzRow(recCard, "BeelzAdminRecRow2");
            AddBeelzSmallButton(recRow2, "BeelzAdmCopy", "Copy collection", "Back up a player's captures+transforms to the admin clipboard (admin copy-collection).", () => { if (ReqName(out var n)) AdminSend($".beelz admin copy-collection {n}"); }, 116);
            AddBeelzSmallButton(recRow2, "BeelzAdmPaste", "Paste collection", "Paste the clipboard onto a player; additive, skips dupes (admin paste-collection).", () => { if (ReqName(out var n)) AdminSend($".beelz admin paste-collection {n}"); }, 120);
            // Beelz v0.100: clear a player's slot binds + custom (per-form/transform) loadouts + active transform,
            // while KEEPING their collection. Confirm-gated — it wipes all their binds, but is recoverable by rebinding.
            AddBeelzConfirmButton(recRow2, "BeelzAdmResetLoadouts", "Reset loadouts",
                "Clear a player's slot binds + custom loadouts + active transform (admin reset-loadouts <player> CONFIRM). Their COLLECTION is kept — they just have to rebind. Good for a player stuck with a broken bar.",
                // Server requires the literal CONFIRM token (AdminCommands.ResetLoadouts) — without it the server
                // only replies with a prompt and does nothing. Raphael's own two-click confirm IS the user gate.
                () => { if (ReqName(out var n)) AdminSend($".beelz admin reset-loadouts {n} CONFIRM"); }, 116, new Color(0.5f, 0.35f, 0.15f));
            // Beelz v0.121: LAST-RESORT stuck-bar fix. Wipes ALL Beelzebub bar integration to vanilla (ends/un-parks
            // any transform; clears every slot/form/weapon/hotkey bind) AND removes the leaked engine-level
            // AbilityGroupSlot modifications that survive relog/respawn/rebuildslots — while KEEPING the player's
            // captured abilities + transform unlocks. Player must be ONLINE; they re-slot afterward. The server needs
            // the literal CONFIRM token (sent for the user); Raphael's own two-click confirm is the user gate.
            AddBeelzConfirmButton(recRow2, "BeelzAdmPurge", "Purge bar",
                "LAST RESORT for a creature kit JAMMED on the bar that survives relog AND Respawn AND Rebuild slots / " +
                "Clear slot mods (an engine-level modification leak). Wipes ALL Beelzebub bar integration to vanilla and " +
                "removes the leaked engine slot modifications, KEEPING the player's captures + transform unlocks (they " +
                "re-slot afterward). Player must be ONLINE. Try Respawn first (it keeps bindings); use Purge only if the " +
                "stuck bar persists. (admin purge <player> CONFIRM)",
                () => { if (ReqName(out var n)) AdminSend($".beelz admin purge {n} CONFIRM"); }, 96, new Color(0.6f, 0.25f, 0.12f));
        }

        // ---- captures + summons ----
        var capCard = AddCard(page, "BeelzAdminCapCard"); AddSectionHeading(capCard, "Captures & summons");
        var capRow = MakeBeelzRow(capCard, "BeelzAdminCapRow");
        AddBeelzSmallButton(capRow, "BeelzAdmFreezeOn", "Freeze on", "Stop all capturing server-wide (admin freeze-captures on).", () => AdminSend(".beelz admin freeze-captures on"), 80);
        AddBeelzSmallButton(capRow, "BeelzAdmFreezeOff", "Freeze off", "Resume capturing (admin freeze-captures off).", () => AdminSend(".beelz admin freeze-captures off"), 80);
        AddBeelzSmallButton(capRow, "BeelzAdmDesummon", "Desummon", "Clean up a player's summons (admin desummon).", () => { if (ReqName(out var n)) AdminSend($".beelz admin desummon {n}"); }, 84);
        AddBeelzSmallButton(capRow, "BeelzAdmDesummonAll", "Desummon all", "Clean up ALL summons server-wide (admin desummon-all).", () => AdminSend(".beelz admin desummon-all"), 100);

        // ---- danger (confirmed) ----
        var dangerCard = AddCard(page, "BeelzAdminDangerCard"); AddSectionHeading(dangerCard, "Danger zone");
        AddBeelzAdminTargetBanner(dangerCard);
        AddBodyText(dangerCard, "<color=#FF8080>These delete progress or reset characters. Two-click confirm; the literal CONFIRM token is sent for you.</color>");
        var dangerRow = MakeBeelzRow(dangerCard, "BeelzAdminDangerRow");
        AddBeelzConfirmButton(dangerRow, "BeelzAdmRevertAll", "Revert all", "End EVERY player's active transform (admin revert-all). Non-destructive but server-wide.", () => AdminSend(".beelz admin revert-all"), 88, new Color(0.5f, 0.35f, 0.15f));
        AddBeelzConfirmButton(dangerRow, "BeelzAdmResetChar", "Reset character", "Unbind + kick the player so they create a fresh character next login; Beelzebub collection preserved (admin reset-character <player> CONFIRM-RESET).", () => { if (ReqName(out var n)) AdminSend($".beelz admin reset-character {n} CONFIRM-RESET"); }, 116, new Color(0.6f, 0.25f, 0.12f));
        AddBeelzConfirmButton(dangerRow, "BeelzAdmWipeAll", "WIPE ALL", "Wipe ALL Beelzebub data for ALL players (admin wipe-all CONFIRM-WIPE). Irreversible.", () => AdminSend(".beelz admin wipe-all CONFIRM-WIPE"), 80, new Color(0.65f, 0.12f, 0.12f));

        RefreshAdminTargetBanner();   // F8: set the initial banner text
    }

    // F8 (lighter): a live "current target" banner repeated near destructive actions, so an admin
    // always sees who/what they're acting on while scrolling (no nested-scroll freeze-pane).
    private void AddBeelzAdminTargetBanner(GameObject parent)
    {
        var l = AddInfoLabel(parent, $"BeelzAdmTarget_{parent.transform.childCount}", "", FontStyles.Bold, Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(l.gameObject, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 18, flexibleHeight: 0);
        l.enableWordWrapping = true;
        _beelzAdminTargetBanners.Add(l);
    }
    private void RefreshAdminTargetBanner()
    {
        string Cell(string v) => string.IsNullOrWhiteSpace(v) ? $"<color={Theme.MutedBodyHex}>—</color>" : $"<b>{v.Trim()}</b>";
        string txt = $"<color=#FFD75A>Target →</color> Player: {Cell(_beelzAdminPlayerName)}   ·   Unit: {Cell(_beelzAdminUnitGuid)}   ·   Ability: {Cell(_beelzAdminAbilityGuid)}";
        foreach (var l in _beelzAdminTargetBanners) if (l != null) l.text = txt;
    }

    // F6: a searchable text field with an inline autocomplete list. getMatches(typed) returns up to a
    // few (Display, Value) pairs; clicking one fills the field with Value (what the command consumes) and
    // onValue fires. The field holds Value; you can also type a value directly. Built into a VERTICAL
    // parent (the results list stacks under the input).
    private void AddBeelzSearchableField(GameObject parent, string name, string placeholder, string initial,
        Func<string, List<(string Display, string Value)>> getMatches, Action<string> onValue)
    {
        var input = UIFactory.CreateInputField(parent, name + "Input", placeholder);
        UIFactory.SetLayoutElement(input.GameObject, minWidth: 220, preferredWidth: 340, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        if (!string.IsNullOrEmpty(initial)) input.Text = initial;
        var results = UIFactory.CreateVerticalGroup(parent, name + "Results",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true, spacing: 1, padding: new Vector4(10, 1, 2, 3));
        UIFactory.SetLayoutElement(results, minWidth: 220, preferredWidth: 380, flexibleWidth: 1, minHeight: 0, flexibleHeight: 0);
        results.SetActive(false);
        bool suppress = false;
        void Rebuild(string typed)
        {
            if (suppress) return;
            onValue?.Invoke(typed ?? "");
            ClearChildren(results);
            string t = (typed ?? "").Trim();
            if (t.Length < 1) { results.SetActive(false); return; }
            var matches = getMatches(t) ?? new List<(string, string)>();
            if (matches.Count == 0) { results.SetActive(false); return; }
            results.SetActive(true);
            int cap = Math.Min(matches.Count, 8);
            for (int i = 0; i < cap; i++)
            {
                var (disp, val) = matches[i];
                var b = UIFactory.CreateButton(results, $"{name}Res{i}", disp);
                UIFactory.SetLayoutElement(b.GameObject, minWidth: 220, preferredWidth: 360, flexibleWidth: 1, minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
                var bt = b.Component.GetComponentInChildren<TextMeshProUGUI>();
                if (bt != null) { bt.fontSize = Theme.ScaledUI(11); bt.alignment = TextAlignmentOptions.MidlineLeft; bt.enableWordWrapping = false; bt.overflowMode = TextOverflowModes.Ellipsis; }
                string chosen = val;
                b.OnClick = () => { suppress = true; input.Text = chosen; suppress = false; onValue?.Invoke(chosen); ClearChildren(results); results.SetActive(false); };
            }
            if (matches.Count > cap) AddSimpleRow(results, $"<color={Theme.MutedBodyHex}>… {matches.Count - cap} more — keep typing</color>");
        }
        input.OnValueChanged += Rebuild;
    }

    private static bool BeelzContains(string haystack, string needle)
        => !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    // F6 match sources (name-based, off live data).
    private List<(string, string)> BeelzPlayerMatches(string typed)
    {
        var outl = new List<(string, string)>();
        try { foreach (var p in Services.PlayerRosterService.GetOnlinePlayers())
                if (BeelzContains(p.Name, typed)) outl.Add((p.Name, p.Name)); }
        catch { }
        return outl;
    }
    private List<(string, string)> BeelzUnitMatches(string typed)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outl = new List<(string, string)>();
        void Add(string guid, string rawName)
        {
            if (string.IsNullOrEmpty(guid) || !seen.Add(guid)) return;
            string disp = BeelzUnitName(guid, rawName);
            if (BeelzContains(disp, typed) || BeelzContains(rawName, typed) || BeelzContains(guid, typed))
                outl.Add(($"{disp}  ({guid})", guid));
        }
        foreach (var c in BeelzState.Captures) Add(c.UnitGuid, c.UnitName);
        foreach (var e in BeelzState.Bestiary) Add(e.UnitGuid, e.UnitName);
        foreach (var t in BeelzState.Transforms) Add(t.UnitGuid, t.UnitName);
        return outl;
    }
    private List<(string, string)> BeelzAbilityMatches(string typed)
    {
        var seenName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outl = new List<(string, string)>();
        // Captured abilities first (we know their GUID → works for give/revoke + admin ability).
        foreach (var c in BeelzState.Captures)
        {
            if (string.IsNullOrEmpty(c.AbilityName) || !seenName.Add(c.AbilityName)) continue;
            string disp = c.DisplayAbility;
            if (BeelzContains(disp, typed) || BeelzContains(c.AbilityName, typed) || BeelzContains(c.AbilityGuid, typed))
                outl.Add(($"{disp}  ({c.AbilityGuid})", c.AbilityGuid));
        }
        // Then catalog-only abilities (name only → admin ability accepts the name; give/revoke need a GUID).
        // Admin context: prefer the admin `abilities-all` scope (incl. disabled) when it's been scanned,
        // else fall back to the player collectible catalog.
        var catSource = BeelzState.CatalogAllLoaded ? BeelzState.CatalogAllAbilities : BeelzState.CatalogAbilities;
        foreach (var c in catSource.Values)
        {
            if (string.IsNullOrEmpty(c.Name) || !seenName.Add(c.Name)) continue;
            string disp = BeelzNames.Ability(c.Name);
            if (BeelzContains(disp, typed) || BeelzContains(c.Name, typed))
                outl.Add(($"{disp}  (name)", c.Name));
        }
        return outl;
    }

    // ---- admin helpers ----
    private void SetBeelzAdminStatus(string msg)
    {
        if (_beelzAdminStatusLabel == null) return;
        _beelzAdminStatusLabel.text = msg;
        _beelzAdminStatusLabel.gameObject.SetActive(true);
    }
    private void AdminSend(string cmd) { BeelzClient.SendUser(cmd); SetBeelzAdminStatus($"<color=#90EE90>Sent:</color> {cmd}"); }
    // Beelz v0.137+ (api33) admin Recovery card. Escalation ladder, weakest first; Cleanse is for a stuck player
    // state, not the bar. Reset/purge send the literal CONFIRM the server needs; Raphael's two-click is the gate.
    private void BuildBeelzAdminRecoveryLayered(GameObject recCard)
    {
        AddBodyText(recCard,
            "Stuck action bar? Work left to right: <b>Diagnose bar</b> → <b>Reset player's bar</b> → <b>Purge bar</b> → " +
            "<b>Respawn</b>. All of them keep the player's captures and unlocks.");
        var recRow = MakeBeelzRow(recCard, "BeelzAdminRecRow");
        AddBeelzSmallButton(recRow, "BeelzAdmDiagBar", "Diagnose bar",
            "Read-only per-slot readout of the player's bar: saved bind, Beelzebub equip row, override buffs, and gear- vs " +
            "other-sourced engine mods (admin bar <player>). Changes nothing; the reply is shown in chat.",
            () => { if (ReqName(out var n)) AdminSend($".beelz admin bar {n}"); }, 100);
        AddBeelzConfirmButton(recRow, "BeelzAdmResetLoadouts", "Reset player's bar",
            "Complete bar reset for another player, the same as their own Reset bar (admin reset-loadouts <player> CONFIRM): " +
            "clears saved binds + custom loadouts + active transform, and spells leave the live bar at once with no weapon " +
            "swap. Keeps captures, unlocks and spellbook (J) spells. Two-click confirm.",
            () => { if (ReqName(out var n)) AdminSend($".beelz admin reset-loadouts {n} CONFIRM"); }, 136, new Color(0.5f, 0.35f, 0.15f));
        AddBeelzConfirmButton(recRow, "BeelzAdmPurge", "Purge bar",
            "Same reset as Reset player's bar, PLUS clears the player's hotkeys (admin purge <player> CONFIRM). Use it for a " +
            "bar still stuck after Reset player's bar. Keeps captures + unlocks; player must be ONLINE. Two-click confirm.",
            () => { if (ReqName(out var n)) AdminSend($".beelz admin purge {n} CONFIRM"); }, 96, new Color(0.6f, 0.25f, 0.12f));
        AddBeelzSmallButton(recRow, "BeelzAdmRespawn", "Respawn",
            "Last resort: respawn in place, keeping inventory / blood / captures / unlocks AND binds (admin respawn <player>). " +
            "Rebuilds a fresh character entity; NOT a character reset.",
            () => { if (ReqName(out var n)) AdminSend($".beelz admin respawn {n}"); }, 76);

        var recRowCleanse = MakeBeelzRow(recCard, "BeelzAdminRecRowCleanse");
        AddBeelzSmallButton(recRowCleanse, "BeelzAdmCleanse", "Cleanse stuck buffs",
            "Strip stuck STATE buffs from a player — invisible / phased / immaterial buffs that survive respawn AND relog " +
            "(admin cleanse <player>). Removes the known culprits. Non-destructive. For a stuck PLAYER, not a stuck bar.",
            () => { if (ReqName(out var n)) AdminSend($".beelz admin cleanse {n}"); }, 130);

        var recRow2 = MakeBeelzRow(recCard, "BeelzAdminRecRow2");
        AddBeelzSmallButton(recRow2, "BeelzAdmCopy", "Copy collection", "Back up a player's captures+transforms to the admin clipboard (admin copy-collection).", () => { if (ReqName(out var n)) AdminSend($".beelz admin copy-collection {n}"); }, 116);
        AddBeelzSmallButton(recRow2, "BeelzAdmPaste", "Paste collection", "Paste the clipboard onto a player; additive, skips dupes (admin paste-collection).", () => { if (ReqName(out var n)) AdminSend($".beelz admin paste-collection {n}"); }, 120);
    }

    private bool ReqName(out string n)    { n = (_beelzAdminPlayerName ?? "").Trim();   if (n.Length == 0) { SetBeelzAdminStatus("<color=#FFB070>Enter a player name first.</color>"); return false; } return true; }
    private bool ReqUnit(out string g)    { g = (_beelzAdminUnitGuid ?? "").Trim();     if (g.Length == 0) { SetBeelzAdminStatus("<color=#FFB070>Enter a unit GUID first.</color>"); return false; } return true; }
    private bool ReqAbility(out string g) { g = (_beelzAdminAbilityGuid ?? "").Trim();  if (g.Length == 0) { SetBeelzAdminStatus("<color=#FFB070>Enter an ability GUID first.</color>"); return false; } return true; }
    private bool ReqPat(out string p)     { p = (_beelzAdminFilterPattern ?? "").Trim(); if (p.Length == 0) { SetBeelzAdminStatus("<color=#FFB070>Enter a name substring first.</color>"); return false; } return true; }
    private bool ReqGuid(out string g)    { g = (_beelzAdminFilterGuid ?? "").Trim();    if (g.Length == 0) { SetBeelzAdminStatus("<color=#FFB070>Enter a PrefabGUID first.</color>"); return false; } return true; }

    // Lock group names go out as one token; the server shows spaces as '_', so do the same here.
    private bool ReqLockGroup(out string g)
    {
        g = (_beelzLockGroup ?? "").Replace("\"", "").Trim().Replace(' ', '_');
        if (g.Length == 0) { SetBeelzAdminStatus("<color=#FFB070>Enter a lock group name first.</color>"); return false; }
        return true;
    }

    // api32: list the server's lock groups (from api locks) under the Incompatibility locks card.
    private void RefreshBeelzLocksLabel()
    {
        if (_beelzLocksLabel == null) return;
        if (!BeelzState.LocksLoaded) { _beelzLocksLabel.text = $"<color={Theme.MutedBodyHex}>Loading lock groups…</color>"; return; }
        if (BeelzState.Locks.Count == 0) { _beelzLocksLabel.text = $"<color={Theme.MutedBodyHex}>No lock groups defined.</color>"; return; }
        var sb = new System.Text.StringBuilder();
        foreach (var grp in BeelzState.Locks)
        {
            var names = new List<string>();
            foreach (var m in grp.Members)
            {
                if (m.StartsWith("cat:", StringComparison.OrdinalIgnoreCase)) { names.Add(m); continue; }
                names.Add(BeelzState.TryGetAbilityInfo(m, out var i) && !string.IsNullOrEmpty(i.Label) ? $"{i.Label} ({m})" : m);
            }
            sb.Append($"<b>{grp.Name}</b> <color={Theme.MutedBodyHex}>max {grp.Max}:</color> {string.Join(", ", names)}\n");
        }
        _beelzLocksLabel.text = sb.ToString().TrimEnd('\n');
    }

    // F5: render the selected ability's CURRENT server settings + shaping overrides from cached api info.
    // Re-runs on AbilityInfoChanged (cheap; only re-renders the ability currently loaded).
    private void RefreshShapeInfo()
    {
        if (_beelzShapeInfoLabel == null) return;
        if (string.IsNullOrEmpty(_beelzShapeInfoGuid)) { _beelzShapeInfoLabel.text = ""; return; }
        if (!BeelzState.TryGetAbilityInfo(_beelzShapeInfoGuid, out var info) || info == null)
        { _beelzShapeInfoLabel.text = $"<color={Theme.MutedBodyHex}>No data yet for {_beelzShapeInfoGuid} — click Load current settings.</color>"; return; }

        string nm = !string.IsNullOrEmpty(info.Label) ? info.Label : BeelzNames.Ability(info.AbilityName);
        var sb = new System.Text.StringBuilder();
        sb.Append($"<b>{nm}</b>  <color={Theme.MutedBodyHex}>[{_beelzShapeInfoGuid}]</color>\n");
        sb.Append($"<color={Theme.MutedBodyHex}>cooldown {info.CooldownSeconds:0.##}s · cast {info.CastTimeSeconds:0.##}s · range {info.Range:0.#}");
        if (!string.IsNullOrEmpty(info.School)) sb.Append($" · {info.School}");
        sb.Append("</color>\n");
        AppendBeelzMetaLines(sb, info.Condition, info.ConditionMods, info.ConditionSource,
            info.ReviewStatus, info.ReviewTag, info.SourceLevel, info.SourceTier, info.IsVBlood);

        var ov = new List<string>();
        var s = info.Shaping;
        if (s != null)
        {
            if (s.Cooldown.HasValue)      ov.Add($"cooldown={s.Cooldown.Value:0.##}");
            if (s.Range.HasValue)         ov.Add($"range={s.Range.Value:0.#}");
            if (s.Charges.HasValue)       ov.Add($"charges={s.Charges.Value}");
            if (s.ChargeTime.HasValue)    ov.Add($"chargetime={s.ChargeTime.Value:0.##}");
            if (s.Aoe.HasValue)           ov.Add($"aoe={s.Aoe.Value:0.##}");
            if (s.ProjSpeed.HasValue)     ov.Add($"projspeed={s.ProjSpeed.Value:0.##}");
            if (s.Duration.HasValue)      ov.Add($"duration={s.Duration.Value:0.##}");
            if (s.HealMult.HasValue)      ov.Add($"healing={s.HealMult.Value:0.##}");
            if (s.ForceTimeout.HasValue)  ov.Add($"forcetimeout={s.ForceTimeout.Value:0.##}");
            if (s.SummonCap.HasValue)     ov.Add($"summoncap={s.SummonCap.Value}");
            if (s.SummonTimeout.HasValue) ov.Add($"summontimeout={s.SummonTimeout.Value:0.##}");
            if (s.SummonUnits.HasValue)   ov.Add($"summonunits={s.SummonUnits.Value}");
            if (s.FreeMoveSecs.HasValue)  ov.Add($"freelymove={s.FreeMoveSecs.Value:0.##}");
            void Tri(string k, string v) { if (!string.IsNullOrEmpty(v) && !v.Equals("auto", StringComparison.OrdinalIgnoreCase)) ov.Add($"{k}={v}"); }
            Tri("interruptonhit", s.InterruptOnHit);
            Tri("interruptible",  s.Interruptible);
            Tri("freemove",       s.FreeMove);
            Tri("castspeed",      s.CastSpeed);
            // api31 (Beelz v0.134) tuning overrides.
            if (s.MaxStacks.HasValue)     ov.Add($"maxstacks={s.MaxStacks.Value}");
            if (s.ProjCount.HasValue)     ov.Add($"projcount={s.ProjCount.Value}");
            if (s.Knockback.HasValue)     ov.Add($"knockback={s.Knockback.Value:0.##}");
            if (s.Lifetime.HasValue)      ov.Add($"lifetime={s.Lifetime.Value:0.#}");
            if (s.CastTime.HasValue)      ov.Add($"casttime={s.CastTime.Value:0.##}");
        }
        // api29 restrictions · api30 damage · api31 cooldown mode / effective value.
        var rsb = new System.Text.StringBuilder();
        AppendBeelzRestrictionLines(rsb, null, info);
        if (rsb.Length > 0) sb.Append($"<color={Theme.MutedBodyHex}>{rsb.ToString().TrimStart('\n').Replace("\n", " · ")}</color>\n");
        if (BeelzState.SupportsRuntimeCooldowns)
            sb.Append($"<color={Theme.MutedBodyHex}>" + (info.UsesCharges ? "Cooldown mode: charges (cooldown rules skipped)"
                : $"Effective bar cooldown: {info.EffectiveCooldownSeconds:0.##}s (shipped {info.CooldownSeconds:0.##}s)") + "</color>\n");
        if (BeelzState.SupportsDamageMeta && !string.IsNullOrEmpty(info.DamageMode))
        {
            sb.Append($"<color={Theme.MutedBodyHex}>Damage: mode {info.DamageMode}");
            if (info.DamageScaleEff.HasValue) sb.Append($" · ×{info.DamageScaleEff.Value:0.##} now");
            if (info.SummonPower.HasValue && Math.Abs(info.SummonPower.Value - 1f) > 0.005f) sb.Append($" · summon power ×{info.SummonPower.Value:0.##}");
            sb.Append("</color>");
            if (info.Attribution.Equals("unreliable", StringComparison.OrdinalIgnoreCase))
                sb.Append(" <color=#FFB070>· hit attribution UNRELIABLE (damage scaling may misfire)</color>");
            else if (!string.IsNullOrEmpty(info.Attribution))
                sb.Append($"<color={Theme.MutedBodyHex}> · attribution {info.Attribution}</color>");
            sb.Append('\n');
        }
        sb.Append(ov.Count == 0 ? "<color=#90EE90>No shaping overrides set (running baseline).</color>" : $"Overrides set: {string.Join(", ", ov)}");
        _beelzShapeInfoLabel.text = sb.ToString();
    }

    // ============================ SETTINGS (client-side Beelzebub) ============================
    // Beelzebub-specific client settings: diagnostic detail (IDs + verbose log) for testers /
    // admins, the tab-group availability override, and the action-bar overlay. Unlike the data
    // tabs this does NOT call AddBeelzAbsentNote — it must stay usable when Beelzebub isn't
    // detected, so a tester can force the group On and re-probe.
    private TextMeshProUGUI _beelzDiagReadout;

    // ============================ CONNECTION (Settings and Help group) ============================
    // #7: always-reachable detection/state + re-detect for BOTH server mods. Lives in the always-
    // available Settings-and-Help group so you can re-detect even when a mod's own tab group is hidden
    // (it's hidden precisely when the mod isn't detected — which is when you'd want to re-detect).
    private void BuildConnectionTab(GameObject page)
    {
        var intro = AddCard(page, "ConnIntroCard");
        AddSectionHeading(intro, "Mod connection & detection");
        AddBodyText(intro,
            "Raphael auto-detects each server mod by a handshake on load-in; a mod's tab group only " +
            "appears once it answers. If a group is missing (e.g. after switching servers), use the matching " +
            "<b>Re-detect</b> below — this tab is always reachable, so you can recover detection from here.");

        // --- Bloodcraft ---
        var bc = AddCard(page, "ConnBloodcraftCard");
        AddSectionHeading(bc, "Bloodcraft");
        _connBloodcraftReadout = AddInfoLabel(bc, "ConnBcState", "", FontStyles.Normal, Theme.ScaledUI(12));
        _connBloodcraftReadout.overflowMode = TextOverflowModes.Overflow;
        var bcRow = MakeBeelzRow(bc, "ConnBcRow");
        AddBeelzPlainButton(bcRow, "ConnBcRedetect", "Re-detect Bloodcraft",
            "Restart Bloodcraft detection from scratch (Eclipse protocol) and re-send the registration handshake. Use after a server switch if the Bloodcraft group didn't appear.",
            // FIX (server-switch re-detect): Reset() FIRST — SendRegistration() early-returns once detection
            // has given up (RegistrationGaveUp), so without the reset this button did nothing in exactly the
            // state you'd click it. Reset clears the give-up + attempt count so the handshake actually re-fires.
            () => { try { Raphael.Services.EclipseProtocolService.Reset(); Raphael.Services.EclipseProtocolService.SendRegistration(); } catch { } RefreshConnectionReadout(); }, 170);

        // --- Beelzebub ---
        var bz = AddCard(page, "ConnBeelzCard");
        AddSectionHeading(bz, "Beelzebub");
        _connBeelzReadout = AddInfoLabel(bz, "ConnBzState", "", FontStyles.Normal, Theme.ScaledUI(12));
        _connBeelzReadout.overflowMode = TextOverflowModes.Overflow;
        var bzRow = MakeBeelzRow(bz, "ConnBzRow");
        AddBeelzPlainButton(bzRow, "ConnBzRedetect", "Re-detect Beelzebub",
            "Restart Beelzebub detection from scratch — re-anchors the handshake loop and re-probes (.beelz api version). Use after a server switch if the Beelzebub group didn't appear.",
            // FIX (server-switch re-detect): Reset() restarts the full probe loop. The old single RequestVersion
            // didn't help once detection had GIVEN UP (the Tick loop stops probing after the cap), so a lone
            // mistimed probe on a slow remote load couldn't recover. Reset re-anchors + the loop retries.
            () => { BeelzProtocolService.Reset(); BeelzClient.RequestVersion(); RefreshConnectionReadout(); }, 170);

        // --- Uriel (0.26) ---
        var ur = AddCard(page, "ConnUrielCard");
        AddSectionHeading(ur, "Uriel");
        _connUrielReadout = AddInfoLabel(ur, "ConnUrielState", "", FontStyles.Normal, Theme.ScaledUI(12));
        _connUrielReadout.overflowMode = TextOverflowModes.Overflow;
        var urRow = MakeBeelzRow(ur, "ConnUrielRow");
        AddBeelzPlainButton(urRow, "ConnUrielRedetect", "Re-detect Uriel",
            "Restart Uriel detection from scratch — re-anchors the handshake loop and re-probes (.uriel api version). Use after a server switch if the Uriel group didn't appear.",
            () => { Services.Uriel.UrielProtocolService.Reset(); Services.Uriel.UrielClient.RequestVersion(); RefreshConnectionReadout(); }, 170);

        // --- Faust (0.50) ---
        var fa = AddCard(page, "ConnFaustCard");
        AddSectionHeading(fa, "Faust");
        _connFaustReadout = AddInfoLabel(fa, "ConnFaustState", "", FontStyles.Normal, Theme.ScaledUI(12));
        _connFaustReadout.overflowMode = TextOverflowModes.Overflow;
        var faRow = MakeBeelzRow(fa, "ConnFaustRow");
        AddBeelzPlainButton(faRow, "ConnFaustRedetect", "Re-detect Faust",
            "Restart Faust detection from scratch — re-anchors the handshake loop and re-probes (.faust api version). Use after a server switch if the Faust group didn't appear. (Most servers don't run Faust.)",
            () => { Services.Faust.FaustProtocolService.Reset(); Services.Faust.FaustClient.RequestVersion(); RefreshConnectionReadout(); }, 170);

        // --- Nyarlathotep ---
        var ny = AddCard(page, "ConnNyarCard");
        AddSectionHeading(ny, "Nyarlathotep");
        _connNyarReadout = AddInfoLabel(ny, "ConnNyarState", "", FontStyles.Normal, Theme.ScaledUI(12));
        _connNyarReadout.overflowMode = TextOverflowModes.Overflow;
        var nyRow = MakeBeelzRow(ny, "ConnNyarRow");
        AddBeelzPlainButton(nyRow, "ConnNyarRedetect", "Re-detect Nyarlathotep",
            "Restart Nyarlathotep detection from scratch and re-probe (.nyar api version). Use after a server switch if the Nyarlathotep group didn't appear.",
            () => { Services.Nyar.NyarProtocolService.Reset(); Services.Nyar.NyarClient.RequestVersion(); RefreshConnectionReadout(); }, 190);

        // --- Refresh Raphael (0.65.6) ---
        // Players reported the game slowing over a long session until a relog. This clears the same
        // session caches a relog would (chat scrollback first) without leaving the world.
        var rf = AddCard(page, "ConnRefreshCard");
        AddSectionHeading(rf, "Refresh Raphael");
        AddBodyText(rf,
            "Game feeling sluggish after a long session? This clears Raphael's session caches " +
            "(including chat history) and frees memory — the same cleanup a relog does, without leaving " +
            "the world. Your settings, open windows, and server connections are kept.");
        _connRefreshReadout = AddInfoLabel(rf, "ConnRefreshState", "", FontStyles.Normal, Theme.ScaledUI(12));
        _connRefreshReadout.overflowMode = TextOverflowModes.Overflow;
        var rfRow = MakeBeelzRow(rf, "ConnRefreshRow");
        AddBeelzPlainButton(rfRow, "ConnRefreshBtn", "Refresh Raphael",
            "Clear chat history + session caches and free memory. Does not change any settings.",
            () => RunRaphaelRefresh(), 170);

        RefreshConnectionReadout();
    }

    /// <summary>0.65.6: shared by the Connection-tab button and the footer "Refresh" button. Runs
    /// BCHubUIManager.SoftRefresh and writes the result to the Connection-tab readout. Returns true on success.</summary>
    private bool RunRaphaelRefresh()
    {
        string msg; bool ok;
        var ui = Plugin.UIManager;
        if (ui == null) { msg = "<color=#FF8080>Refresh unavailable</color> — UI not ready."; ok = false; }
        else try { msg = $"<color=#90EE90>Done</color> ({DateTime.Now:HH:mm}) — {ui.SoftRefresh()}"; ok = true; }
        catch (Exception ex) { msg = "<color=#FF8080>Refresh failed</color> — see the BepInEx log."; ok = false; LogUtils.LogError($"[Refresh] {ex}"); }
        if (_connRefreshReadout != null) _connRefreshReadout.text = msg;
        return ok;
    }

    private void RefreshConnectionReadout()
    {
        if (_connBloodcraftReadout != null)
        {
            string s;
            if (Raphael.Services.EclipseProtocolService.StandDownForEclipse())
                s = "<color=#FFB070>Standing down — the standalone Eclipse mod is installed (Raphael defers its Bloodcraft data layer).</color>";
            else if (Raphael.Services.EclipseProtocolService.UserRegistered)
                s = "<color=#90EE90>Connected</color> — Bloodcraft detected and registered on this server.";
            else if (Raphael.Services.EclipseProtocolService.RegistrationGaveUp)
                s = "<color=#FF8080>Not detected</color> — no Bloodcraft response on this server. Re-detect to retry.";
            else if (Raphael.Services.EclipseProtocolService.RegistrationPending)
                s = "<color=#FFD75A>Detecting…</color> — handshake in progress.";
            else
                s = "<color=#A0A0A0>Idle</color> — not yet probed (re-detect to start).";
            _connBloodcraftReadout.text = s;
        }
        if (_connBeelzReadout != null)
        {
            string s;
            if (BeelzState.Present)
                s = $"<color=#90EE90>Connected</color> — api {BeelzState.ApiVersion}, plugin {BeelzState.PluginVersion}" +
                    (BeelzState.Subscribed ? ", event stream on." : ", subscribing…");
            else if (BeelzProtocolService.DetectionGaveUp)
                s = "<color=#FF8080>Not detected</color> — no Beelzebub response on this server. Re-detect to retry.";
            else
                s = "<color=#FFD75A>Detecting…</color> — handshake in progress.";
            _connBeelzReadout.text = s;
        }
        if (_connUrielReadout != null)
        {
            string s;
            if (Services.Uriel.UrielState.Present)
                s = $"<color=#90EE90>Connected</color> — api {Services.Uriel.UrielState.ApiVersion}, plugin {Services.Uriel.UrielState.PluginVersion}.";
            else if (Services.Uriel.UrielProtocolService.DetectionGaveUp)
                s = "<color=#FF8080>Not detected</color> — no Uriel response on this server. Re-detect to retry.";
            else
                s = "<color=#FFD75A>Detecting…</color> — handshake in progress.";
            _connUrielReadout.text = s;
        }
        if (_connFaustReadout != null)
        {
            string s;
            if (Services.Faust.FaustState.Present)
                s = $"<color=#90EE90>Connected</color> — api {Services.Faust.FaustState.ApiVersion}, plugin {Services.Faust.FaustState.PluginVersion}.";
            else if (Services.Faust.FaustProtocolService.DetectionGaveUp)
                s = "<color=#FF8080>Not detected</color> — no Faust response on this server (most servers don't run Faust). Re-detect to retry.";
            else
                s = "<color=#FFD75A>Detecting…</color> — handshake in progress.";
            _connFaustReadout.text = s;
        }
        RefreshNyarConnectionReadout();
    }

    // Small labeled bool toggle row (mirrors the diagnostics toggle) for the Beelz Settings tab.
    private void AddBeelzBoolToggle(GameObject parent, string name, string label, bool initial, Action<bool> onChanged, string tooltip)
    {
        var row = MakeBeelzRow(parent, name + "Row");
        var t = UIFactory.CreateToggle(row, name);
        UIFactory.SetLayoutElement(t.GameObject, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject, minWidth: 320, preferredWidth: 360, flexibleWidth: 1, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        t.Toggle.isOn = initial;
        if (!string.IsNullOrEmpty(tooltip)) TooltipHover.Attach(t.GameObject, tooltip);
        t.OnValueChanged += v => { try { onChanged?.Invoke(v); } catch (Exception ex) { LogUtils.LogError($"[Beelz] toggle '{name}' handler threw: {ex}"); } };
    }

    private void BuildBeelzSettingsTab(GameObject page)
    {
        EnsureBeelzSubscribed();

        // --- Diagnostics (the headline of this tab) ---
        var diagCard = AddCard(page, "BeelzSettingsDiagCard");
        AddSectionHeading(diagCard, "Diagnostics");
        AddBodyText(diagCard,
            "Turn this on when testing or reporting an issue. It reveals each ability's " +
            $"{Mono("ID")} (PrefabGUID) + raw prefab name in the Loadout and Hotkeys tables and on hover, and writes a " +
            "verbose trace of every Beelzebub command sent and reply received to the BepInEx log " +
            $"({Mono("LogOutput.log")}) — so you can tell the mod author exactly which abilities work or need tuning.");

        var diagRow = MakeBeelzRow(diagCard, "BeelzDiagToggleRow");
        var diagToggle = UIFactory.CreateToggle(diagRow, "BeelzDiagnosticsToggle");
        UIFactory.SetLayoutElement(diagToggle.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        diagToggle.Text.text = "Enable diagnostic details (show ability IDs + verbose logging)";
        diagToggle.Text.fontSize = Theme.ScaledUI(12);
        diagToggle.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(diagToggle.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        diagToggle.Toggle.isOn = Config.Settings.BeelzDiagnostics;
        TooltipHover.Attach(diagToggle.GameObject,
            "ON: ability IDs appear in the Loadout/Hotkeys tables + hover text, and Raphael logs a [Beelz][diag] command/reply " +
            "trace to LogOutput.log for copy/paste. OFF: clean tables, no extra logging. Takes effect immediately. Default OFF.");
        diagToggle.OnValueChanged += v =>
        {
            Config.Settings.SetBeelzDiagnostics(v);
            _beelzLoadoutAssignDirty = true; _beelzBestiaryDirty = true;   // ID column appears/disappears
            LogUtils.LogInfo($"[Beelz] diagnostic details -> {(v ? "ON" : "OFF")}.");
            // Keep the capture-list headers + rows aligned with the new column set. Headers aren't
            // visibility-gated; the row rebuilds early-return unless their tab is active and re-run
            // via RefreshBeelzTabOnShow when you switch to them.
            RebuildBeelzColumnHeaders();
            RebuildBeelzLoadoutRows();
            RebuildBeelzHotkeyBindList();
            RefreshBeelzDiagnosticsReadout();
        };

        AddBodyText(diagCard,
            $"<color={Theme.MutedBodyHex}>Also active automatically while Raphael's global Diagnostic mode is on " +
            "(Settings and Help → Settings). The log file is <b>BepInEx/LogOutput.log</b> in your V Rising profile " +
            "folder; filter for <b>[Beelz]</b> to find the Beelzebub lines.</color>");

        // --- Live connection / state readout ---
        AddSpacer(page, 6);
        var statusCard = AddCard(page, "BeelzSettingsStatusCard");
        AddSectionHeading(statusCard, "Connection & state");
        _beelzDiagReadout = AddInfoLabel(statusCard, "BeelzDiagReadout", "—", FontStyles.Normal, Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(_beelzDiagReadout.gameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 110, flexibleHeight: 0);
        _beelzDiagReadout.enableWordWrapping = true;

        AddBodyText(statusCard,
            $"<color={Theme.MutedBodyHex}>Re-detect / re-hydrate now lives at <b>Settings and Help → Connection</b> — " +
            "it's always reachable, so you can recover detection even when this Beelzebub group is hidden (it hides when " +
            "the server isn't running Beelzebub, which is exactly when you'd want to re-detect).</color>");

        // --- Availability (READ-ONLY here on purpose) ---
        // The mutator lives in the global Settings tab (Settings and Help → Settings → "Beelzebub
        // tab group"), NOT here: this tab is INSIDE the Beelzebub group, and switching the group to
        // Auto/Off while it's unavailable would collapse + disable the group's header — locking you
        // out of this very tab (see RefreshTabGroupAvailability). The global tab is always reachable.
        AddSpacer(page, 6);
        var availCard = AddCard(page, "BeelzSettingsAvailCard");
        AddSectionHeading(availCard, "Beelzebub tab group");
        AddBodyText(availCard,
            $"This tab group is currently set to <b>{BeelzAvailabilityWord()}</b>. <b>Auto</b> shows the Beelzebub tabs " +
            "only when the server answers the handshake (default; most servers don't have Beelzebub). To force them " +
            "visible on a server where they aren't auto-detected — e.g. to explore the UI before Beelzebub is installed " +
            "— set it to <b>On</b> from <b>Settings and Help → Settings → Beelzebub tab group</b> (or " +
            "BeelzebubAvailability = On in the .cfg). It's changed there, not here, so you can't accidentally hide the " +
            "group you're currently viewing.");

        // --- Action bar overlay ---
        AddSpacer(page, 6);
        var ovCard = AddCard(page, "BeelzSettingsOverlayCard");
        AddSectionHeading(ovCard, "Action bar overlay");
        AddBodyText(ovCard,
            "The Beelz Action Bar overlay shows a button per hotkey ability with a cooldown ring. Show/hide it here or " +
            "from the Hotkeys tab; drag it anywhere. Lock + transparency live in the overlay controls (Game UI / Settings).");
        var ovRow = MakeBeelzRow(ovCard, "BeelzOverlayRow");
        ButtonRef ovBtn = null;
        ovBtn = AddBeelzPlainButton(ovRow, "BeelzSettingsOverlayToggle", BeelzOverlayToggleLabel(),
            "Toggle the on-screen Beelz Action Bar overlay.",
            () => { Plugin.UIManager?.ToggleOverlay(PanelType.BeelzActionBarOverlay); SetBeelzButtonText(ovBtn, BeelzOverlayToggleLabel()); }, 130);

        // --- Devour messages (Beelz v0.83 `.beelz silent`) ---
        AddSpacer(page, 6);
        var extrasCard = AddCard(page, "BeelzSettingsExtrasCard");
        AddSectionHeading(extrasCard, "Devour messages");
        AddBodyText(extrasCard,
            "Mute the \"you already knew all of its abilities\" chat line when you devour a unit you've already fully " +
            "collected. (Leaderboard and drop-odds buttons are on the Bestiary tab.)");
        var exRow = MakeBeelzRow(extrasCard, "BeelzSettingsExtrasRow");
        AddBeelzPlainButton(exRow, "BeelzSilentOn", "Mute repeat-devour msg",
            "Hide the duplicate-devour message (.beelz silent on).", () => BeelzClient.Silent(true), 180);
        AddBeelzPlainButton(exRow, "BeelzSilentOff", "Unmute",
            "Show the duplicate-devour message again (.beelz silent off).", () => BeelzClient.Silent(false), 90);

        // --- Loadout display & behavior ---
        AddSpacer(page, 6);
        var loCard = AddCard(page, "BeelzSettingsLoadoutCard");
        AddSectionHeading(loCard, "Loadout");
        AddBeelzBoolToggle(loCard, "BeelzKeyLabelsToggle",
            "Label slot buttons with keys (LM / Q / Sp / Sh / E / R / C / T) instead of numbers",
            Config.Settings.BeelzSlotKeyLabels,
            v => { Config.Settings.SetBeelzSlotKeyLabels(v); _beelzLoadoutAssignDirty = true; RebuildBeelzColumnHeaders(); RebuildBeelzLoadoutRows(); },
            "ON: the Loadout slot buttons read as the in-game key each slot uses (Primary=Left-click, Travel=Space, etc.). OFF: numbers (P / 1-6 / U).");
        AddBeelzBoolToggle(loCard, "BeelzAutoRefreshToggle",
            "Auto-refresh the action bar after a grant (.beelz refresh)",
            Config.Settings.BeelzAutoRefreshBar,
            v => Config.Settings.SetBeelzAutoRefreshBar(v),
            "ON: after you grant/unslot an ability from Raphael, it re-applies your bar so the new ability shows immediately. OFF: rely on the server's own refresh. " +
            "Beelzebub 0.137.1+ shows grants at once by itself, so Raphael skips the extra refresh there.");

        RefreshBeelzDiagnosticsReadout();
    }

    private static string BeelzOverlayToggleLabel()
        => (Plugin.UIManager?.IsOverlayOpen(PanelType.BeelzActionBarOverlay) ?? false) ? "Hide overlay" : "Show overlay";

    private static string BeelzAvailabilityWord() => Config.Settings.BeelzebubAvailability switch
    {
        Config.Settings.ModAvailability.On  => "On",
        Config.Settings.ModAvailability.Off => "Off",
        _                                   => "Auto",
    };
    private static string FormatBeelzAvailability() => $"Beelzebub tabs: {BeelzAvailabilityWord()}";

    // Beelzebub tab-group availability cycle for the GLOBAL Settings tab (Settings and Help →
    // Settings). Safe to cycle Auto/On/Off here because that tab group is always available, so
    // hiding the Beelzebub group can never lock the user out of this control. (The in-group Beelz
    // Settings tab only SHOWS the state — see BuildBeelzSettingsTab.) Called from
    // BuildDisplaySettingsSection.
    internal void BuildBeelzAvailabilityGlobalSetting(GameObject page)
    {
        AddSpacer(page, 8);
        AddSectionHeading(page, "Beelzebub tab group");
        AddGuideSection(page, "",
            "Show or hide the BEELZEBUB tab group (client UI for the server-side ability-capture/transform mod). " +
            "Auto = visible only when the server answers Beelzebub's handshake (default). On = always visible — use " +
            "this to explore the UI or test a server before Beelzebub is installed. Off = always hidden.");
        var row = UIFactory.CreateHorizontalGroup(page, "BeelzAvailGlobalRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 8, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        ButtonRef btn = null;
        btn = AddBeelzPlainButton(row, "BeelzAvailGlobalCycle", FormatBeelzAvailability(),
            "Cycle the Beelzebub tab group: Auto → On → Off. Persists to the .cfg; takes effect immediately.",
            () =>
            {
                var next = Config.Settings.BeelzebubAvailability switch
                {
                    Config.Settings.ModAvailability.Auto => Config.Settings.ModAvailability.On,
                    Config.Settings.ModAvailability.On   => Config.Settings.ModAvailability.Off,
                    _                                    => Config.Settings.ModAvailability.Auto,
                };
                Config.Settings.SetBeelzebubAvailability(next);
                SetBeelzButtonText(btn, FormatBeelzAvailability());
                RefreshAllTabGroupAvailability();
            }, 170);
    }

    // Plain button (NOT gated on BeelzState.Present) for the Settings tab, which must work even
    // when Beelzebub isn't detected (forcing availability On, re-probing, toggling the overlay).
    private ButtonRef AddBeelzPlainButton(GameObject parent, string name, string label, string tooltip, Action onClick, int width = 110)
    {
        var btn = UIFactory.CreateButton(parent, name, label);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: width, preferredWidth: width, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(12); t.alignment = TextAlignmentOptions.Center; }
        if (!string.IsNullOrEmpty(tooltip)) TooltipHover.Attach(btn.GameObject, tooltip);
        btn.OnClick = onClick;
        return btn;
    }

    private void RefreshBeelzDiagnosticsReadout()
    {
        if (_beelzDiagReadout == null) return;
        var sb = new System.Text.StringBuilder();
        if (BeelzState.Present)
        {
            sb.Append($"<color=#90EE90>Beelzebub detected.</color>  API v{BeelzState.ApiVersion}");
            if (!string.IsNullOrEmpty(BeelzState.PluginVersion)) sb.Append($"  ·  plugin {BeelzState.PluginVersion}");
            sb.Append($"  ·  event stream {(BeelzState.Subscribed ? "on" : "off")}");
            sb.Append($"\nCaptures: {BeelzState.Captures.Count}   Slots set: {BeelzState.Slots.Count}   Hotkeys: {BeelzState.Hotkeys.Count}");
            sb.Append($"\nCatalog: {(BeelzState.CatalogLoaded ? $"{BeelzState.CatalogEnabledCount} collectible abilities scanned" : "not scanned (Bestiary → Scan all)")}");
            if (BeelzState.Active != null && !BeelzState.Active.None)
                sb.Append($"\nActive transform: {BeelzUnitName(BeelzState.Active.UnitGuid, BeelzState.Active.UnitName)}");
        }
        else
        {
            sb.Append(BeelzProtocolService.DetectionGaveUp
                ? "<color=#FFB070>Beelzebub not detected on this server.</color> If it IS installed, set the tab group to On above, then use Re-detect."
                : "<color=#FFD75A>Looking for Beelzebub…</color> If nothing appears shortly, the server doesn't have the mod.");
        }
        sb.Append($"\n\nDiagnostic details: <b>{(Config.Settings.BeelzDiagnostics ? "ON" : "off")}</b>");
        if (!Config.Settings.BeelzDiagnostics && Config.Settings.DiagnosticMode)
            sb.Append("  <color=#9FD0FF>(on via global Diagnostic mode)</color>");
        _beelzDiagReadout.text = sb.ToString();
    }

    // ============================ HELP: BEELZEBUB ============================
    // Documentation tabs in the "Settings and Help" group (always visible — they're docs,
    // so they render even when Beelzebub isn't on the server). Mirror the Bloodcraft help
    // tabs' style via AddGuideSection / AddCollapsibleHelpDetail (defined on MainPanel).

    private void BuildBeelzQuickStartTab(GameObject page)
    {
        AddGuideSection(page, "Beelzebub — what it is",
            "Beelzebub (\"Lord of Gluttony\") is a SERVER-SIDE mod, separate from Bloodcraft — a server can " +
            "run either, both, or neither. When it's present, defeating units lets you COLLECT their " +
            "abilities, swap those onto your spell bar (per-weapon if you like), cast extra abilities beyond " +
            "the 6 native slots from an on-screen action bar, and transform into Dracula or Morgana. " +
            "Raphael is the client UI for all of it — the Beelzebub tab group only appears when the " +
            "server actually has the mod.");

        AddGuideSection(page, "1. Collect abilities",
            "Kill units for a chance to capture one of their abilities (V-Blood sources are richer). A rare " +
            "\"devour\" grants a unit's WHOLE kit at once. Open <b>Beelzebub → Bestiary</b> and press " +
            "<b>Scan all abilities</b> to load the full collectible list — it becomes a checklist with a % " +
            "toward 100%, filterable by Captured / Missing and searchable by name, unit, weapon, or " +
            "category.\n\n" +
            "The full scan is heavy but <b>one-time</b>: Raphael caches it, so later logins load it instantly " +
            "(until the server updates Beelzebub). In a hurry? Use a <b>Quick scan</b> preset (Summons / " +
            "V-Bloods / Spells) to pull just that slice in seconds.");

        AddGuideSection(page, "2. Build ability sets (Loadout)",
            "Open Beelzebub → Loadout and pick a SET from the dropdown: \"Universal\" applies on any weapon, " +
            "or pick a specific weapon family. Click a slot number (1-6) next to a captured ability to bind " +
            "it. A per-weapon set overrides Universal on the slots you fill, and the server auto-switches to " +
            "it when you equip that weapon. Empty slots keep your normal in-game abilities. You can edit a " +
            "weapon's set even without holding it; use \"Copy from\" to clone one set onto another.");
        AddCollapsibleHelpDetail(page, "Slot meanings",
            "1 = Primary (Q / left-click) · 2 = Travel (Space) · 3 = Shift · 4 = Heavy (E) · 5 = Spell 1 (R) · " +
            "6 = Spell 2 (C). There's no separate ultimate slot — slot 1 is the primary attack.");

        AddGuideSection(page, "3. Extra abilities (Action Bar)",
            "Beyond the 6 slots, the Hotkeys tab lets you name extra abilities — each becomes a TILE on the " +
            "draggable \"Beelz Action Bar\" overlay. Click a tile (or the Cast button) to fire it; the ring " +
            "shows an estimated cooldown. These are on-screen BUTTONS, not native ability keys — but you can " +
            "assign an optional keyboard shortcut to each tile.");

        AddGuideSection(page, "4. Transform",
            "If you've unlocked them, the Transforms tab lets you become a boss form (Dracula, Morgana, Werewolf, " +
            "Golem, Gargoyle) — switch phase, fire the signature summon / detonation, then revert. Every other " +
            "unit is collected for its abilities (or devoured), not transformed into.");

        AddGuideSection(page, "Don't see the Beelzebub tabs?",
            "They only appear once Raphael confirms Beelzebub on your server — which can lag a few seconds after " +
            "switching servers. If the group is greyed or missing:\n" +
            "• Click the greyed <b>BEELZEBUB</b> header — it opens a small panel with <b>Re-check now</b> " +
            "(restarts detection) and <b>Force-enable tabs</b> (shows them anyway).\n" +
            "• Or open <b>Settings and Help → Connection</b> → <b>Re-detect Beelzebub</b>.\n" +
            "• To make the tabs always show, set BeelzebubAvailability = On in kdpen.Raphael.cfg.\n\n" +
            "See <b>Beelzebub Help</b> for the full command reference and mechanics.");
    }

    // The comprehensive in-app guide — purpose, mechanics, and the FULL player + admin command
    // reference (sourced from the Beelzebub repo's BCH_INTEGRATION_HANDOFF §10, ApiVersion 8).
    // Quick Start above is the short orientation; this is the deep reference. Argument
    // placeholders use (parentheses), not <angle brackets>, so TMP doesn't treat them as tags.
    private void BuildBeelzModHelpTab(GameObject page)
    {
        // ---- 1. What it is + the loop ----
        AddGuideSection(page, "Beelzebub — the complete guide",
            "Beelzebub (\"Lord of Gluttony\") is a SERVER-SIDE V Rising mod about DEVOURING your enemies' powers. " +
            "Defeat any unit — V-Blood or ordinary NPC — and you have a chance to CAPTURE one of its abilities into " +
            "your personal collection. Slot captured abilities onto your six-slot action bar, bind extras to named " +
            "hotkeys, transform into Dracula or Morgana, and complete the bestiary. Raphael is the client UI; " +
            "Beelzebub does the work server-side and the two talk over chat (just like Raphael ↔ Bloodcraft). Behaviour " +
            "and limits are set by the server admin, so exact numbers vary by server.");

        AddGuideSection(page, "The core loop",
            "Kill → (chance to) capture an ability → slot it / bind it / cast it → collect more. Rarely a kill hits " +
            "the DEVOUR jackpot and grants a unit's ENTIRE eligible kit at once. Abilities that summon minions make " +
            "those minions fight for you — whether you're transformed or just cast a captured summon ability.");

        AddCollapsibleHelpDetail(page, "Core concepts",
            "• Capture — a per-ability roll on each kill (rates set by the server). Captured abilities live in your " +
            "collection (Bestiary / Loadout).\n" +
            "• Devour — the rare jackpot: a unit's whole eligible kit granted at once.\n" +
            "• Slots & loadouts — bind a captured ability to one of 6 bar slots. Two bucket types: the UNIVERSAL set " +
            "(fires on any weapon) and PER-WEAPON sets (fire only with that weapon drawn; they override the universal " +
            "bind on their slots). The server auto-switches the active set when you swap weapons. Unarmed is its own family.\n" +
            "• Hotkeys — named bindings beyond the 6 slots, cast on demand from the on-screen action bar.\n" +
            "• Transform — become one of the renderable boss forms (Dracula, Morgana, Werewolf, Golem, Gargoyle): multi-phase kits, signature " +
            "summons, detonation AoEs. Every other unit is collected/devoured, not transformed into.\n" +
            "• Summons — summon abilities spawn player-allied minions (caps, leashing, stash-on-waygate, clean despawn).\n" +
            "• Untransformed casting — captured projectiles / AoEs / teleport-detonates fire correctly off your normal bar.");

        AddCollapsibleHelpDetail(page, "Kinds: Magic / Weapon / Form",
            "After a Bestiary \"Scan all\", abilities classify as Magic (castable on any weapon), Weapon (bound to a " +
            "weapon family), or Form (only while transformed into a specific form). Loadout + Bestiary can filter and " +
            "group by these; group-by-Weapon lists a multi-weapon ability under each of its families.");

        AddCollapsibleHelpDetail(page, "Weapon-locked abilities & a wrong-looking bar",
            "Some abilities only fire/animate correctly on a specific weapon family. Bind one into the wrong set and it " +
            "may not show on the live bar. The Loadout columns + row hover show each ability's weapon/form restriction, " +
            "and Beelzebub's chat reply gives a ✋ weapon hint. Use \"Fix bar\" (.beelz refresh) if the bar looks blank " +
            "or wrong after reverting a form, dismounting, or swapping weapons; \"Reset bar\" clears every binding back " +
            "to vanilla (keeps your captures).");

        // ---- 2. PLAYER command reference (handoff §10.3) ----
        AddGuideSection(page, "Player command reference",
            "Everything below is runnable by any player and targets YOU (no player argument). You don't need to type " +
            "these — the Beelzebub tabs send them for you — but they're listed so you know exactly what each button does, " +
            "and so you can use chat directly. Replies appear in chat as human-readable text.");

        AddCollapsibleHelpDetail(page, "Player — discover & collection",
            "• .beelz / .beelz help — overview.   • .beelz commands — full sectioned command list.\n" +
            "• .beelz list (vblood|shard|regular) (page) — your captured abilities.\n" +
            "• .beelz search (term) — find a captured ability by name.\n" +
            "• .beelz info (index|name) — one ability's details.\n" +
            "• .beelz bestiary (page)  ·  .beelz bestiary unit (name) — collection book, per-unit X/Y.\n" +
            "• .beelz progress — completion %.   • .beelz catalog (page) — curated reference.   • .beelz current — your active bar.");

        AddCollapsibleHelpDetail(page, "Player — loadouts (action bar)",
            "• .beelz grant (slot 1-6) (index) — bind a captured ability to a UNIVERSAL slot.\n" +
            "• .beelz unslot (slot) — clear a universal slot.\n" +
            "• .beelz weapon-grant (weapon|auto) (slot 1-6) (index) — bind to a PER-WEAPON set (auto = your current weapon).\n" +
            "• .beelz weapon-unslot (weapon|auto) (slot) — clear a per-weapon bind.\n" +
            "• .beelz form-grant (form|auto) (slot) (index|abilityID) · .beelz form-unslot (form|auto) (slot) — per-FORM sets for the shapeshift-wheel forms (Wolf/Bear/Rat/Spider/Toad). Werewolf/Golem/Gargoyle are boss transformations — manage them on the Transforms tab.\n" +
            "• .beelz loadouts — summary of the universal set + each per-weapon set + the active weapon.\n" +
            "• .beelz clearbar [all|universal|<weapon>|<form>] — clear a chosen set → vanilla (keeps captures).   • .beelz refresh — re-apply your bar if it looks wrong.\n" +
            "• .beelz resetbar CONFIRM — complete bar reset: clears every saved bind and puts your weapon's skills back at once; keeps captures + spellbook (J) spells (v0.137).\n" +
            "• .beelz preset save|load|list|delete (name) — save / restore loadout presets.\n" +
            "Grants and unslots show on the bar right away (v0.137.1); made while in a form, a transform or on a mount, they apply when that ends.\n" +
            "Weapon families: Sword, GreatSword, Axe, Mace, Spear, Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, " +
            "Claws, Pollaxe, Slashers, TwinBlades, Unarmed, FishingPole.");

        AddCollapsibleHelpDetail(page, "Player — extra hotkeys & casting",
            "• .beelz hotkey set (name) (index)  ·  .beelz hotkey clear (name)  ·  .beelz hotkey list — named bindings " +
            "beyond the 6 slots.\n" +
            "• .beelz cast (hotkey name|index) — force-cast any captured ability on demand, respecting its cooldown. " +
            "This is the mechanism behind each tile on Raphael's on-screen Beelz Action Bar overlay.");

        AddCollapsibleHelpDetail(page, "Player — transform",
            "• .beelz transforms (filter) — your unlocked transforms.\n" +
            "• .beelz transform (index|name) — transform.   • .beelz revert — end it.\n" +
            "• .beelz preview (index|name) — what you'd get per phase.   • .beelz phase (n) — switch phase.\n" +
            "• .beelz active — your active transform.   • .beelz detonate — fire its signature AoE.\n" +
            "• .beelz summon (n) — cast a transformed unit's signature add-summon.\n" +
            "Renderable forms: Dracula, Morgana, Werewolf, Golem, Gargoyle (+ a basic werewolf). Every OTHER unit is collected/devoured for its abilities rather than transformed into.");

        AddCollapsibleHelpDetail(page, "Player — summons & manage",
            "• .beelz summons stash|restore|clear|status — manage your minions (works untransformed too; stash before a waygate).\n" +
            "• .beelz tp — recall summons to you.\n" +
            "• .beelz forget (i)  ·  .beelz forget-transform (i) — delete a capture / unlock.\n" +
            "• .beelz clear CONFIRM — wipe ALL your captures + slots (the literal word CONFIRM is required).\n" +
            "• .beelz top — server collection leaderboard  ·  .beelz odds — your live drop / Devour / pity chances (Bestiary tab buttons).\n" +
            "• .beelz silent on|off — mute the \"you already knew that\" duplicate-devour message (Settings tab).\n" +
            "• .beelz verbosity silent|summary|verbose — your chat-notification level.");

        AddCollapsibleHelpDetail(page, "Machine API (what Raphael calls under the hood)",
            ".beelz api (version|list|slots|transforms|active|info|progress|rules|catalog|catalog units|catalog abilities|" +
            "hotkeys|transform-config|bestiary|config|cooldowns|verbosity|bch). These return compact [BEELZ:*] lines that " +
            "Raphael parses into the tabs — you never need them by hand. .beelz api bch on subscribes Raphael to the live event stream. " +
            "Turn on Beelzebub → Settings → Diagnostics to see this traffic in the log.");

        // ---- 3. ADMIN command reference (handoff §10.4 / §10.6) ----
        AddGuideSection(page, "Admin commands",
            "All .beelz admin … commands require V Rising admin status (the server enforces it — a non-admin just gets a " +
            "rejection reply). Admin commands operate on OTHER players (they take a player name, matched fuzzily) and on " +
            "server-wide rules/config, and every one is audited to the server log. Raphael surfaces these in the Beelzebub " +
            "Admin: Config and Admin: Players tabs (always visible; the server does the gating). unitGuid / abilityGuid are " +
            "the integer PrefabGUIDs shown in the lists when Diagnostics is on.");

        AddCollapsibleHelpDetail(page, "Admin — inspect & capture filters",
            "Read-only checks:\n" +
            "• .beelz admin help — list every admin command.\n" +
            "• .beelz admin rules — show the loaded capture/deny/allow rules + global scaling.\n" +
            "• .beelz admin inspect (player) — dump a player's full Beelzebub state.\n" +
            "• .beelz admin progress (player) — a player's collection % .\n" +
            "• .beelz admin snapshot — server-wide summary.\n" +
            "• .beelz admin buffs (player) — diagnostic: dump a player's live buffs + ability slots to the log.\n" +
            "• .beelz admin tune-list — list abilities with cast-tuning overrides.\n" +
            "• .beelz admin transform show — current transform mode / duration / cooldown.\n" +
            "\nControl what can be captured (then .beelz admin reload to apply file edits):\n" +
            "• .beelz admin deny (pattern)  /  undeny (pattern) — block / unblock abilities by name fragment.\n" +
            "• .beelz admin allow (pattern)  /  unallow (pattern) — whitelist abilities by name fragment.\n" +
            "• .beelz admin denyguid|allowguid (add|remove) (guid) — same, by exact ability GUID.\n" +
            "• .beelz admin transformonly (add|remove) (pattern|guid) — restrict an ability to transformed use only.\n" +
            "• .beelz admin reload — re-read ability_rules.json from disk (no restart).");

        AddCollapsibleHelpDetail(page, "Admin — live tuning (abilities / units / defaults)",
            "Change balance live, no file editing — persists to ability_rules.json:\n" +
            "• .beelz admin ability (name|ID) (field) (value) — set ONE per-ability rule (accepts the ability NAME or its " +
            "PrefabGUID). Curation: enabled, weapons, forms, transformonly, difficulty, phase, allowdenied, category, notes. " +
            "Server-wide shaping (Abilities_ApplyConfig, default on): cooldown, range, charges, chargetime, aoe, projspeed, " +
            "duration, healing, forcetimeout, freelymove, interruptonhit, interruptible, freemove, castspeed, summoncap, " +
            "summontimeout, summonunits, damagescale, cooldownscale. (Raphael's Admin: Players tab has a picker for these.)\n" +
            "• .beelz admin ability (id) defaults  ·  .beelz admin ability all defaults — reset shaping to the shipped baseline.\n" +
            "• .beelz admin tune (ability) (field) (value) — shortcut for one shaping field. .beelz admin tune-list lists tuned abilities.\n" +
            "• .beelz admin transform-set (CHAR_unit) (field) (value) — per-unit transform scalars: enabled, difficulty, " +
            "tier, damagescale, cooldownscale, healthscale, speedscale, fullreplace, powerscalingmode, notes.\n" +
            "• .beelz admin default (damagescale|cooldownscale) (value) — server-wide baseline for abilities with no override.\n" +
            "• .beelz admin set (key) (value) — set ANY config key live (persists to the .cfg).\n" +
            "• .beelz admin broadcast (status|test|leaderboard on|off|interval (min)|top (1-5)|complete on|off) — server announcements (Admin: Config tab).\n" +
            "• .beelz admin difficulty (basic|brutal) — server difficulty tier.\n" +
            "• .beelz admin freeze-captures (on|off|status) — pause / resume all ability capture server-wide.\n" +
            "• .beelz admin transform mode|duration|cooldown (regular|vblood) (value) — transform timing.");

        AddCollapsibleHelpDetail(page, "Admin — grant abilities & transforms",
            "Give or take powers on another player:\n" +
            "• .beelz admin give (player) (unitGuid) (abilityGuid) — grant one ability.\n" +
            "• .beelz admin revoke (player) (unitGuid) (abilityGuid) — remove one ability.\n" +
            "• .beelz admin devour (player) (unitGuid) — grant ALL of a unit's abilities at once.\n" +
            "• .beelz admin give-transform (player) (unitGuid) — unlock a transform (Dracula, Morgana, Werewolf, Golem, Gargoyle, basic werewolf).\n" +
            "• .beelz admin revoke-transform (player) (unitGuid) — remove a transform unlock.\n" +
            "• .beelz admin force-transform (player) (unitGuid) — force-activate a transform, bypassing unlock + cooldown.\n" +
            "• .beelz admin clear-transform (player) — end a player's active transform.\n" +
            "• .beelz admin revert-all — end every active transform on the server.\n" +
            "\nPut abilities on a player's bar directly (bypasses the normal guards; the reply notes a [WARNING]):\n" +
            "• .beelz admin set-slot|clear-slot (player) (slot 1-6) (abilityGuid) — universal-bar slot.\n" +
            "• .beelz admin set-weapon-slot|clear-weapon-slot (player) (weapon) (slot 1-6) (abilityGuid) — per-weapon slot.");

        AddCollapsibleHelpDetail(page, "Admin — locks, reseed & inspect",
            "• .beelz admin lock add (group) \"(member, member…)\" — create/extend a lock group; members are ability GUIDs or cat:Category (v0.135).\n" +
            "• .beelz admin lock max (group) (n)  /  lock remove (group) [member]  /  lock list  /  lock check (player).\n" +
            "• .beelz admin reseed preview | merge | replace CONFIRM — update ability_rules.json to the shipped defaults (backup kept).\n" +
            "• .beelz admin ability-inspect (ability|ID) [export] — read-only view of every tunable number in the ability's chain (v0.132).");

        AddCollapsibleHelpDetail(page, "Admin — recovery & destructive ops",
            "Fix a stuck action bar — escalation ladder (each step is stronger than the last; keeps the collection):\n" +
            "• .beelz admin bar (player) — read-only per-slot readout of the bar (saved bind, equip row, override buffs, engine mods). Changes nothing (v0.137).\n" +
            "• .beelz admin reset-loadouts (player) CONFIRM — the complete bar reset (same as the player's resetbar): binds + custom loadouts + transform cleared, spells off the bar at once, spellbook spells kept.\n" +
            "• .beelz admin purge (player) CONFIRM — the same reset PLUS hotkeys cleared; for a bar still stuck after reset-loadouts (player must be online).\n" +
            "• .beelz admin respawn (player) — respawn in place (keeps inventory, progress and binds); last resort.\n" +
            "Before v0.137, rebuildslots / clearslotmods / rebuildbar were the middle steps; they are LEGACY now (rebuildbar just prints the admin bar readout).\n" +
            "\nFix a stuck PLAYER STATE (not the bar):\n" +
            "• .beelz admin cleanse (player) [buffNameOrGuid] — strip stuck STATE buffs (invisible / phased / immaterial) that survive respawn AND relog (v0.131). Omit the buff to remove the known culprits, or name/GUID a specific buff. Non-destructive.\n" +
            "\nOther recovery:\n" +
            "• .beelz admin copy-collection (player)  /  paste-collection (player) — back up a player's captures + unlocks, " +
            "then paste onto another character (additive; skips dupes).\n" +
            "• .beelz admin reset-character (player) CONFIRM-RESET — player gets a fresh character next login; their " +
            "Beelzebub collection is preserved.\n" +
            "\nSummons:\n" +
            "• .beelz admin desummon (player)  /  desummon-all — clean up ally summons.\n" +
            "\nTest / DESTRUCTIVE (handle with care):\n" +
            "• .beelz admin testform (wolf|bear|off) — experimental native-form probe.\n" +
            "• .beelz admin scan-abilities — dump every discovered ability to discovered_abilities.json for auditing.\n" +
            "• .beelz admin wipe-all CONFIRM-WIPE — wipe ALL players' Beelzebub data (literal token required).");

        // ---- 4. Configuration (handoff §10.5) ----
        AddGuideSection(page, "Configuration (server admin)",
            "Beelzebub runs on defaults with zero setup; everything below is optional tuning, all on the SERVER. Three " +
            "config files live under the server's BepInEx/config/.");
        AddCollapsibleHelpDetail(page, "What lives where",
            "• kdpen.Beelzebub.cfg — global switches: capture on/off + drop rates, inclusive-capture mode, transform-only " +
            "enforcement, transform modes/durations/cooldowns, summon behaviour, power-scaling, server difficulty, hotkey " +
            "limits, logging. Change live with admin set (key) (value) (+ difficulty / freeze-captures / transform …); " +
            "hand-edits need a server restart.\n" +
            "• ability_rules.json — capture deny/allow lists + GUIDs, transform-only lists, global Defaults scaling, " +
            "per-ability AbilityMap, per-unit TransformMap, drop-rate overrides. Change live with admin ability / " +
            "transform-set / default / deny* / allow* / transformonly; hand-edits need admin reload (no restart).\n" +
            "• ability_metadata_overrides.json — optional display name/description/school/type/category overrides; " +
            "hand-edit + admin reload.\n" +
            "Raphael's Admin: Config tab builds a generic editor from the server's live config keys, so new settings just appear.");
    }

    // ---- small shared row helpers ----
    // (ClearChildren already exists on MainPanel — reused here.)
    private GameObject MakeBeelzRow(GameObject parent, string name)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, name,
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 8, padding: new Vector4(2, 2, 2, 2)); // 8 (was 4): breathing room between fields/buttons across admin rows
        // 0.50: scale row height with the UI font so buttons/toggles that grow via
        // Theme.ScaledHeight don't overflow a fixed-height row into the next at Large+ text.
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        return row;
    }

    private void AddBeelzRowLabel(GameObject row, string richText)
    {
        var lbl = UIFactory.CreateLabel(row, "Label", richText,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 120, preferredWidth: 180, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void AddSimpleRow(GameObject parent, string richText, bool italic = false)
    {
        var lbl = UIFactory.CreateLabel(parent, "BeelzRow", richText,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        if (italic) lbl.TextMesh.fontStyle = FontStyles.Italic;
    }
}
