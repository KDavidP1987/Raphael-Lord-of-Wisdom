using System;
using System.Collections.Generic;
using Raphael.Config;
using Raphael.Resources;
using Raphael.Services;
using Raphael.UI.Forms;
using Raphael.UI.Framework.CustomLib.Panel;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.UI.Framework.UniverseLib.UI.Panels;
using Raphael.UI.ModContent.Data;
using Raphael.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UIBase = Raphael.UI.Framework.UniverseLib.UI.UIBase;
using Raphael.UI.Framework.CustomLib.Util;

namespace Raphael.UI.ModContent;

// The primary tabbed UI. See the matching ASCII diagram in docs/MOD_DESIGN.md
// for the layout. Each tab's body is built by a dedicated BuildXxxTab method
// dispatched in BuildContentArea; tabs that need live data subscribe to
// PlayerStateService events and unsubscribe in Reset.
public partial class MainPanel : ResizeablePanelBase
{
    public override string PanelId => "MainPanel";
    public override PanelType PanelType => PanelType.Base;

    // 0.14.0 friend-test v3: another bump — 760×560 was "a little better
    // but still needs work" per the user. Settling at 960×700, which gives
    // long help-tab content (Mod Help / Quick Start) ~10–12 lines without
    // scrolling, comfortably fits the 7-toggle footer at Standard text
    // scale, and stays inside the 1366×768 minimum supported V Rising
    // resolution. Existing users with a saved custom size aren't affected
    // — this only moves the "Default" baseline + new-install size.
    public override int MinWidth  => 960;
    public override int MinHeight => 700;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    public override Vector2 DefaultPosition  => Vector2.zero;

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.UITransparency;
    public override bool ResizeWholePanel => false;
    // 0.12.0: opt the main panel into both color pickers. Construct-time
    // application happens in PanelBase.ConstructUI; live picks flow in via
    // BCHubUIManager.RefreshAllPanelBackgrounds (outer) and
    // RefreshScopedInnerBackgrounds (inner — main + familiar browser only).
    public override bool UsesCustomBackgroundColor      => true;
    public override bool UsesCustomInnerBackgroundColor => true;
    // 0.10.14: the "Lock overlays" toggle pins the five overlays; the
    // main panel is NOT an overlay and must stay drag/resize-enabled.
    protected override bool RespectsLockOverlays => false;

    public PanelType ActiveTab { get; private set; } = PanelType.FamiliarsTab;

    private readonly Dictionary<PanelType, GameObject> _tabContent = new();
    // Inner-content GameObject *inside* the ScrollView wrapper for each tab.
    // AutoResize walks this to compute the actual children-sum height; _tabContent
    // points at the scroll wrapper whose own height tracks the viewport, not
    // its overflow.
    private readonly Dictionary<PanelType, GameObject> _tabInnerContent = new();
    private readonly Dictionary<PanelType, ButtonRef> _tabButtons = new();
    private Toggle _xpOverlayToggle;
    private Toggle _famOverlayToggle;
    private Toggle _famBrowserToggle;
    private Toggle _quickSpawnOverlayToggle; // 0.53: footer quick-toggle for the Familiar Quick Spawn overlay
    private Toggle _bossTrackerOverlayToggle; // 0.54: footer quick-toggle for the Faust boss-tracker overlay
    private Toggle _dqOverlayToggle;
    private Toggle _profOverlayToggle;
    private Toggle _shiftOverlayToggle;
    private Toggle _quickActionsOverlayToggle;
    private Toggle _beelzOverlayToggle; // 0.18.3: footer quick-toggle for the Beelz action-bar overlay
    private Toggle _beelzSummonsOverlayToggle; // 0.19: footer quick-toggle for the Beelz summons overlay
    private Toggle _beelzTransformOverlayToggle; // 0.20: footer quick-toggle for the Beelz transforms overlay
    // 0.14.0: combined overlay toggle + per-PanelType GameObject tracker so
    // ApplyCombinedFooterVisibility can hide the 4 info toggles when combined
    // mode is on (and restore them when it flips off).
    private Toggle _combinedOverlayToggle;
    private readonly System.Collections.Generic.Dictionary<PanelType, GameObject> _overlayToggleGOs = new();
    // 0.14.0 friend-test fix: track the Settings-tab master combined toggle
    // so the footer click handler can sync it via SetIsOnWithoutNotify
    // (preventing the double-toggle feedback loop that surfaced as
    // "toggles show checked but individual overlays are showing").
    private Toggle _combinedMasterToggle;

    // Familiars-tab live labels
    private TextMeshProUGUI _famNameLabel;
    private TextMeshProUGUI _famProgressLabel;
    private TextMeshProUGUI _famStatsLabel;
    private bool _famSubscribed;
    // 0.10.11: in-panel search-result display. The .fam s reply was
    // previously visible only in chat — invisible when the user had Raphael
    // chat-suppression on. These fields hold the result-panel labels +
    // dynamic list container that subscribes to FamSearchCompleted.
    private TextMeshProUGUI _famSearchResultHeader;
    private GameObject      _famSearchResultList;
    private bool            _famSearchSubscribed;

    // Class-tab live labels
    private TextMeshProUGUI _classNameLabel;
    private TextMeshProUGUI _classLevelLabel;
    // 0.17.3: "you're missing this element (free power)" nudge labels, one per page.
    // Updated in RenderClass / RenderExpertise / RenderBloodLegacy via ProgressionHints.
    private TextMeshProUGUI _classHintLabel;
    private TextMeshProUGUI _wepHintLabel;
    private TextMeshProUGUI _blHintLabel;
    // 0.13.0: class-details body in the Active Class card. Re-rendered on
    // class change via RenderClass → FormatClassDetailsBlock.
    private TextMeshProUGUI _classDetailsLabel;
    // 0.13.0: class-synergy hints surfaced on the Expertise + Blood Legacy
    // tabs. Same RenderClass call updates all three so the user's context
    // changes everywhere when they swap class.
    private TextMeshProUGUI _wepClassSynergyLabel;
    private TextMeshProUGUI _blClassSynergyLabel;
    private bool _classSubscribed;

    // Expertise-tab live labels
    private TextMeshProUGUI _wepTypeLabel;
    private TextMeshProUGUI _wepProgressLabel;
    private TextMeshProUGUI _wepBonusLabel;
    // 0.9.6: stat-values line populated from the most recent .wep get reply
    // (cached via LastResponseChanged). Renders below the existing bonus-name
    // line so the header shows both "which stats are chosen" and "what their
    // current values are at this level".
    private TextMeshProUGUI _wepStatsValuesLabel;
    private bool _wepSubscribed;
    private bool _wepLastResponseSubscribed;
    private System.Collections.Generic.List<string> _cachedWepGetLines;

    // 0.10.0: V-Bloods tab — collection tracker fed by VBloodScannerService.
    // Each registry entry gets one row built once; refresh re-binds labels in
    // place rather than tearing down + rebuilding, so the list stays scrolled
    // and responsive while the scanner streams in results across ~4 minutes.
    // 0.10.9: chip view rebuilt around the box-sweep scanner's per-variant
    // VBloodInstance records. One row per captured variant (basic / shiny /
    // primal / primal-shiny) — the user picks which variant to summon
    // explicitly. "Missing" filter renders un-captured V-Blood names as
    // their own placeholder rows. The 0.10.7 dual-view toggle (Chips /
    // Instances) is retired because the new single view does both jobs.
    private TextMeshProUGUI _vbProgressLabel;          // "23 / 65 captured + 4 primals"
    private ButtonRef       _vbScanButton;             // "Scan all" / "Cancel" toggle
    private TextMeshProUGUI _vbScanStatusLabel;        // "Scanning… box 3 / 14: BoxName"
    private ButtonRef       _vbSortButton;             // 0.10.1: cycle button, shows current sort mode
    private GameObject      _vbRowContainer;           // VerticalLayoutGroup holding all variant + missing rows
    private bool            _vbSubscribed;
    private enum VBloodFilter { All, Captured, Missing, ShinyOnly }
    private VBloodFilter    _vbFilter = VBloodFilter.All;

    // Blood-Legacy-tab live labels
    private TextMeshProUGUI _blTypeLabel;
    private TextMeshProUGUI _blProgressLabel;
    private TextMeshProUGUI _blBonusLabel;
    // 0.9.6: stat-values line populated from BloodInfoLatest. Equivalent
    // to _wepStatsValuesLabel above.
    private TextMeshProUGUI _blStatsValuesLabel;
    private bool _blSubscribed;

    // In-UI Blood Info display (parsed from `.bl get [Type]` reply).
    private TextMeshProUGUI _blInfoTitleLabel;
    private TextMeshProUGUI _blInfoLevelLabel;
    private TextMeshProUGUI _blInfoStatsLabel;
    private bool _blInfoSubscribed;

    // Unarmed+Shift-tab live labels
    private TextMeshProUGUI _shiftSpellLabel;
    private TextMeshProUGUI _unarmedStatusLabel;
    private TextMeshProUGUI _unarmedBonusLabel;
    private bool _shiftSubscribed;

    // Prestige-tab live labels (current-prestige across systems)
    private TextMeshProUGUI _prestigeXpLabel;
    private TextMeshProUGUI _prestigeLegacyLabel;
    private TextMeshProUGUI _prestigeExpertiseLabel;
    private TextMeshProUGUI _prestigeFamLabel;
    private TextMeshProUGUI _prestigeExoLabel;        // 0.16: Exo prestige (from .prestige get Exo chat reply)
    private int  _prestigeExoLevel;
    private int  _prestigeExoMaxLevel;
    private bool _prestigeExoReceived;
    private bool _prestigeExoFetchScheduled;
    private System.Action _prestigeExoTicker;
    private bool _prestigeSubscribed;

    // In-UI Prestige info display (parsed from `.prestige get` reply).
    private GameObject       _prestigeInfoSection;
    private TextMeshProUGUI  _prestigeInfoTitleLabel;
    private TextMeshProUGUI  _prestigeInfoLevelLabel;
    private TextMeshProUGUI  _prestigeInfoEffectsLabel;
    private GameObject       _prestigeBar;        // 0.9.2: optional progress bar
    private RectTransform    _prestigeBarFill;
    private bool _prestigeInfoSubscribed;

    // Levels-tab live labels (full overview)
    private TextMeshProUGUI _lvlXpLabel;
    private TextMeshProUGUI _lvlLegacyLabel;
    private TextMeshProUGUI _lvlExpertiseLabel;
    private TextMeshProUGUI _lvlExpertiseBonusLabel;
    private TextMeshProUGUI _lvlFamLabel;
    private TextMeshProUGUI _lvlFamStatsLabel;
    private TextMeshProUGUI _lvlProfessions1Label;
    private TextMeshProUGUI _lvlProfessions2Label;
    private TextMeshProUGUI _lvlProfessions3Label;
    private TextMeshProUGUI _lvlProfessions4Label;
    private bool _lvlSubscribed;

    // Kindred Commands tab - stateful pager for .clan list. Page is 1-based;
    // server defaults to page 1 when no arg is given. The label TMP is updated
    // each time the user clicks Prev/Next so the row reflects the page number
    // currently being requested.
    private int _clanListPage = 1;
    private TextMeshProUGUI _clanListPageLabel;

    // Boxes-tab live state
    private TextMeshProUGUI _boxesActiveBoxLabel;
    private TextMeshProUGUI _boxesContentHeading;
    private TextMeshProUGUI _boxesStatusLabel;
    private LabelRef        _boxesSwapWarning;     // hidden by default; warns of pending destructive swap
    private GameObject _boxesPickerSection;       // parent wrapping picker heading + list
    private GameObject _boxesContentSection;      // parent wrapping content heading + list
    private GameObject _boxesListContainer;       // box-name buttons go here
    private GameObject _boxesContentContainer;    // familiar-name buttons go here
    private bool _boxesShowingContents;
    private bool _boxesSubscribed;

    // Two-click destruction-confirm state for switching active familiar.
    // Bloodcraft's `.fam b` errors with "You already have an active familiar!
    // Unbind that one first." when called with one bound, and `.fam t` (toggle/
    // dismiss) does NOT free the bind slot - HasActiveFamiliar() only returns
    // false once the entity is destroyed. So switching requires `.fam ub`
    // (DESTROY current) → `.fam b N`. We never fire that silently: first click
    // sets the pending-swap state and shows a warning banner; a second click
    // on the SAME familiar within SWAP_CONFIRM_WINDOW seconds executes it.
    private int   _pendingSwapIndex = -1;
    private float _pendingSwapDeadline = -1f;
    private const float SWAP_CONFIRM_WINDOW_SECONDS = 5f;

    // Per-row edit mode for the box contents view. When ON, each familiar row
    // gets a destructive Delete button next to the bind button. Two-click
    // confirm on the Delete; first click changes its label to "Confirm?".
    private bool _boxesEditMode = false;
    private int  _pendingDeleteIndex = -1;
    private float _pendingDeleteDeadline = -1f;
    private const float DELETE_CONFIRM_WINDOW_SECONDS = 3f;
    private Toggle _boxesEditModeToggle;

    // -------- Tab grouping (Phase 5c) ------------------------------------
    // The left rail is split into 3 collapsible groups. Each group has a
    // header button and a list of sub-tabs. KINDRED/HELP start empty (their
    // tabs land in Phases 5d-5i).
    private sealed class TabGroupDef
    {
        public string Title;
        public bool   StartExpanded;
        public (PanelType Tab, string Label)[] Tabs;
    }

    private static readonly TabGroupDef[] TabGroups = new[]
    {
        new TabGroupDef
        {
            Title = "Bloodcraft",
            StartExpanded = true,
            Tabs = new[]
            {
                (PanelType.FamiliarsTab,    "Familiars"),
                (PanelType.BoxesTab,        "Boxes"),
                (PanelType.VBloodsTab,      "V-Bloods"),
                (PanelType.AllFamiliarsTab, "All Familiars"),
                (PanelType.ClassTab,        "Class"),
                (PanelType.ExpertiseTab,    "Weapon Expertise"),
                (PanelType.BloodLegacyTab,  "Blood Legacy"),
                (PanelType.UnarmedShiftTab, "Unarmed + Shift"),
                (PanelType.PrestigeTab,     "Prestige"),
                (PanelType.LevelsTab,       "Levels"),
                (PanelType.DailyQuestTab,   "Daily Quests"),
                (PanelType.AdminTab,        "Admin"),
            },
        },
        new TabGroupDef
        {
            Title = "Kindred",
            StartExpanded = false,
            Tabs = new[]
            {
                (PanelType.KindredLogisticsTab,      "Logistics"),
                (PanelType.KindredLogisticsAdminTab, "Logistics: Admin"),
                (PanelType.KindredCommandsPlayerTab, "Commands"),
                (PanelType.KindredAdminPlayersTab,   "Admin: Players"),
                (PanelType.KindredAdminServerTab,    "Admin: Server"),
                (PanelType.KindredAdminWorldTab,     "Admin: World"),
            },
        },
        new TabGroupDef
        {
            // 0.18: client UI for the server-side Beelzebub mod (ability capture
            // /transform). The whole group is gated on the `.beelz api version`
            // handshake (see IsTabGroupAvailable("Beelzebub")) — most servers
            // won't have Beelzebub, so it stays unavailable until detected. Admin
            // tabs are inline (always visible with a "requires admin" note; the
            // server enforces permissions — same model as the Kindred admin tabs).
            Title = "Beelzebub",
            StartExpanded = false,
            Tabs = new[]
            {
                // 0.24.8: Loadout first — it's the tab players actually live in;
                // the Bestiary is the browse/collection view.
                (PanelType.BeelzLoadoutTab,      "Loadout"),
                (PanelType.BeelzBestiaryTab,     "Bestiary"),
                (PanelType.BeelzHotkeysTab,      "Hotkeys"),
                (PanelType.BeelzTransformsTab,   "Transforms"),
                (PanelType.BeelzSettingsTab,     "Settings"),
                (PanelType.BeelzAdminConfigTab,  "Admin: Config"),
                (PanelType.BeelzAdminPlayersTab, "Admin: Players"),
                (PanelType.BeelzAdminAbilityTableTab, "Admin: Abilities"),
            },
        },
        new TabGroupDef
        {
            // 0.26: client UI for the server-side Uriel mod (storage sharing,
            // public prisons, stair swap, object spawning). The whole group is
            // gated on the `.uriel api version` handshake (see
            // IsTabGroupAvailable("Uriel")) — most servers won't have Uriel, so
            // it stays unavailable until detected. One player tab per sub-feature;
            // admin tabs are inline (always visible with a "requires admin" gate;
            // the server enforces permissions — same model as the other admin tabs).
            Title = "Uriel",
            StartExpanded = false,
            Tabs = new[]
            {
                (PanelType.UrielStorageTab,      "Storage Sharing"),
                (PanelType.UrielPrisonTab,       "Prisons"),
                (PanelType.UrielStairsTab,       "Stairs"),
                (PanelType.UrielObjectsTab,      "Object Spawning"),
                (PanelType.UrielObjectCatalogTab,"Object Catalog"),
                (PanelType.UrielEventsTab,       "Castle Events"),
                (PanelType.UrielEventLibraryTab, "Event Library"),
                (PanelType.UrielSettingsTab,     "Settings"),
                (PanelType.UrielAdminSharingTab, "Admin: Sharing"),
                (PanelType.UrielAdminObjectsTab, "Admin: Objects"),
                (PanelType.UrielAdminConfigTab,  "Admin: Controls"),   // 0.73.0: was Admin: Config (same PanelType)
                (PanelType.UrielAdminUnitsTab,   "Admin: Units"),
            },
        },
        new TabGroupDef
        {
            // Client UI for the server-side Faust mod (investigation / information: castle, plot,
            // player, and server-stat queries). The whole group is gated on the `.faust api version`
            // handshake (see IsTabGroupAvailable("Faust")) — most servers won't have Faust, so it stays
            // unavailable until detected. Player read tabs first; admin tabs are inline (always visible
            // with a "requires admin" gate; the server enforces permissions — same model as the other
            // admin tabs). Positions / Resources / Admin tabs render a Phase-2 "coming soon" stub today.
            Title = "Faust",
            StartExpanded = false,
            Tabs = new[]
            {
                (PanelType.FaustCastleInfoTab,   "Castle Info"),
                (PanelType.FaustPlotsTab,        "Open Plots"),
                (PanelType.FaustAllPlotsTab,     "All Plots"),
                (PanelType.FaustDecayWatchTab,   "Decay Watch"),
                (PanelType.FaustResourcesTab,    "Castle Resources"),
                (PanelType.FaustPlayerInfoTab,   "Player Info"),
                (PanelType.FaustClansTab,        "Clans"),
                (PanelType.FaustPositionsTab,    "Player Positions"),
                (PanelType.FaustStatsTab,        "Server Stats"),
                (PanelType.FaustBossesTab,       "Boss Status"),
                (PanelType.FaustKillsTab,        "Leaderboards"),
                (PanelType.FaustWorldScanTab,    "World Map"),
                (PanelType.FaustSettingsTab,     "Settings"),
                (PanelType.FaustAdminControlTab, "Admin: Control"),
                (PanelType.FaustAdminAccessTab,  "Admin: Access"),
                (PanelType.FaustAdminOversightTab, "Admin: Oversight"),
            },
        },
        new TabGroupDef
        {
            // Client UI for the server-side Nyarlathotep mod (admin-run world events: waves, empowered NPCs,
            // boss adds, defended zones, sieges + per-player stats). Gated on the `.nyar api version`
            // handshake (IsTabGroupAvailable("Nyarlathotep")). Players get the event board, leaderboards
            // and their own stats; admin tabs are inline-gated (the server enforces permissions).
            Title = "Nyarlathotep",
            StartExpanded = false,
            Tabs = new[]
            {
                (PanelType.NyarEventsTab,          "Live Events"),
                (PanelType.NyarLeaderboardsTab,    "Leaderboards"),
                (PanelType.NyarMyStatsTab,         "My Stats"),
                (PanelType.NyarSettingsTab,        "Settings"),
                (PanelType.NyarAdminEventsTab,     "Admin: Events"),
                (PanelType.NyarAdminUnitsTab,      "Admin: Units"),
                (PanelType.NyarAdminTemplatesTab,  "Admin: Templates"),
                (PanelType.NyarAdminPillarsTab,    "Admin: Pillars"),
                (PanelType.NyarAdminZonesTab,      "Admin: Zones"),
                (PanelType.NyarAdminAnnounceTab,   "Admin: Announce"),
                (PanelType.NyarAdminKillSwitchTab, "Admin: Kill Switch"),
            },
        },
        new TabGroupDef
        {
            // 0.17: standalone client-side UI enhancements that work on ANY
            // server, with no Bloodcraft/Kindred dependency. Always available
            // (see IsTabGroupAvailable) so it shows even on vanilla servers.
            Title = "Game UI",
            StartExpanded = false,
            Tabs = new[]
            {
                (PanelType.GameUITab, "Overview"),
            },
        },
        new TabGroupDef
        {
            // 0.9.8: was "Help"; renamed because friend-testing surfaced that
            // users didn't notice there was a Settings page under what looked
            // like a documentation-only group. The Settings tab is the more
            // actionable child here — putting it in the group name makes the
            // group worth expanding.
            Title = "Settings and Help",
            StartExpanded = false,
            Tabs = new[]
            {
                (PanelType.QuickStartTab,    "Quick Start"),
                // 0.73.0: one complete guide per mod (Services/Guides, rendered by MainPanel.GuideKit). The old
                // per-mod "Help" tab ids (ModHelpTab, BeelzModHelpTab, UrielModHelpTab, FaustModHelpTab) are no longer
                // listed but still route to the same merged guide in the dispatch below.
                (PanelType.BloodcraftQuickStartTab, "Bloodcraft Guide"),
                (PanelType.BeelzQuickStartTab, "Beelzebub Guide"),
                (PanelType.UrielQuickStartTab, "Uriel Guide"),
                (PanelType.FaustQuickStartTab, "Faust Guide"),
                (PanelType.NyarGuideTab,       "Nyarlathotep Guide"),
                (PanelType.KindredGuideTab,    "Kindred Guide"),
                // In-built base-game guide: explains vanilla V Rising mechanics (leveling, blood, combat,
                // castles, V Bloods…) and how each server mod changes them — so Raphael works as a
                // one-stop reference even for players new to the game. Sits beside the external Game Guide.
                (PanelType.VRisingBasicsTab, "V Rising Basics"),
                // 0.12.1: V Rising game guide + community-resource links.
                (PanelType.GameGuideTab,     "Game Guide"),
                // 0.74.0: every unit and world object with its PrefabGUID, searchable (MainPanel.GameCatalog.cs).
                (PanelType.GameCatalogTab,   "Reference: Units & Objects"),
                (PanelType.SettingsTab,      "Settings"),
                // 0.67.2: every chat-visibility + diagnostics control in one place (presets + per-mod rows).
                (PanelType.ChatDiagTab,      "Chat & Diagnostics"),
                // 0.19: always-reachable connection/detection status + re-detect for both server mods.
                // Lives here (not inside a mod's own group) so you can re-detect even when that group
                // is hidden because the mod wasn't detected.
                (PanelType.ConnectionTab,    "Connection"),
                (PanelType.VanillaAdminTab,  "Vanilla Admin"),
                (PanelType.AboutTab,         "About"),
            },
        },
    };

    // Flattened view: every (Tab, Label) across all groups. Useful when other
    // code (BuildContentArea, etc.) needs to iterate all tabs without caring
    // which group they belong to.
    private static System.Collections.Generic.IEnumerable<(PanelType Tab, string Label)> AllTabs()
    {
        foreach (var g in TabGroups)
            foreach (var t in g.Tabs)
                yield return t;
    }

    private readonly System.Collections.Generic.Dictionary<string, bool>             _groupExpanded   = new();
    private readonly System.Collections.Generic.Dictionary<string, GameObject>       _groupContent    = new();
    // 0.72.0: what the user asked of each group and whether they have touched the rail live in RailMemory (static, kept
    // across a panel rebuild, cleared at client teardown); the expand / collapse rules are RailPolicy's.
    private readonly System.Collections.Generic.Dictionary<string, TextMeshProUGUI>  _groupHeaderText = new();
    // 0.12.1: keep header ButtonRef around so the Bloodcraft handshake retry
    // (in EclipseProtocolService) can flip the group from tentative-available
    // → confirmed-available (or → unavailable on give-up) in place when the
    // AvailabilityChanged event fires.
    private readonly System.Collections.Generic.Dictionary<string, Raphael.UI.Framework.UniverseLib.UI.Models.ButtonRef> _groupHeaderButton = new();
    // 0.15.0: Bloodcraft "registration failed — here's why + force-enable"
    // diagnostic. When Settings.BloodcraftAvailability == Auto AND the Eclipse
    // protocol handshake gave up after 3 retries (15s), we render a small
    // explanatory panel inside the Bloodcraft group's content area instead
    // of the usual sub-tab button list. The panel calls out the three likely
    // root causes (no Bloodcraft / Quests-Professions only / older Bloodcraft
    // with hard Eclipsed=false) and offers a one-click "Force-enable" that
    // flips BloodcraftAvailability=On for the rest of the session so the
    // user can navigate into the tabs and drive whatever the chat-regex
    // pipeline supports (.fam boxes / .quest p / .bl get / etc.). _groupTabListGo
    // tracks the sub-tab button list separately from _groupContent so the
    // diagnostic and sub-tabs can be hidden/shown independently within the
    // same expanded group.
    private readonly System.Collections.Generic.Dictionary<string, GameObject>       _groupTabListGo  = new();
    private GameObject _bloodcraftDiagnosticGo;
    private GameObject _beelzDiagnosticGo;   // inline "not detected — enable / re-check" panel for the Beelzebub rail
    private GameObject _urielDiagnosticGo;   // 0.26: same inline diagnostic for the Uriel rail
    private bool _availabilitySubscribed;
    private GameObject _tabStripGo;

    public MainPanel(UIBase owner) : base(owner) { }

    // The "—" button in the title-bar's top-right used to be inert because we
    // skip ResizeablePanelBase.ConstructPanelContent (which would hide the
    // title bar entirely) — and PanelBase wires it to OnClosePanelClicked,
    // which the base class no-ops. Hide the panel here so the button does
    // what users expect: close the UI. The floating button stays visible so
    // the user can re-open.
    protected override void OnClosePanelClicked() => SetActive(false);

    // 0.16.x: closing the panel while fullscreen must first exit fullscreen so
    // the floating launcher (hidden during fullscreen) is restored and the next
    // open is windowed. Covers every deactivation path (title-bar X, hotkey
    // toggle, escape-menu hide) since they all funnel through SetActive. The
    // launcher's actual visibility is governed by
    // BCHubUIManager.RefreshFloatingButtonVisibility, so exiting fullscreen here
    // during an escape-menu hide does not wrongly pop the launcher back up.
    public override void SetActive(bool active)
    {
        if (!active && _isFullscreen)
            SetFullscreen(false);
        // 0.67.1: reopening onto a page that was invalidated (mod detection changed / relog) or never built
        // must not flash the stale page — show "Loading…" and rebuild it next frame via the pending slot.
        if (active && !_disposed && !IsTabBuilt(ActiveTab))
            ArmPendingTab(ActiveTab);
        base.SetActive(active);
        // PERF: Beelzebub tabs skip their state-change rebuilds while the panel is hidden
        // (so a heavily-collected server's api-info enrichment can't thrash the UI off-
        // screen). When the panel reopens, resync whatever Beelz tab is active so it
        // reflects state that arrived while it was closed.
        if (active && IsTabBuilt(ActiveTab)) RefreshBeelzTabOnShow(ActiveTab);
    }

    // 0.9.7: fullscreen toggle state. Snapshot of pre-fullscreen Rect data so
    // we can restore exactly what the user had after toggling off. NOT
    // persisted across sessions — fullscreen is treated as transient.
    private bool _isFullscreen;
    private UnityEngine.Vector2 _preFullscreenSizeDelta;
    private UnityEngine.Vector2 _preFullscreenAnchoredPos;
    private UnityEngine.Vector2 _preFullscreenAnchorMin;
    private UnityEngine.Vector2 _preFullscreenAnchorMax;
    private UnityEngine.Vector2 _preFullscreenPivot;
    // 0.11.2: store pre-fullscreen pin state so exit restores it. We force
    // IsPinned=true during fullscreen to block PanelDragger drag/resize —
    // stretched anchors break sizeDelta arithmetic, so the safest UX is
    // "only the maximize button works while fullscreen."
    private bool _preFullscreenPinned;
    private Raphael.UI.Framework.UniverseLib.UI.Models.ButtonRef _maximizeBtn;
    // 0.65.6: small always-visible "Refresh" at the bottom-right of the panel (tooltip footer), so players don't
    // have to find the Connection tab. Label flips to "Done"/"Failed" briefly after a click (reverted in
    // TickTabAutoRefresh).
    private Raphael.UI.Framework.UniverseLib.UI.Models.ButtonRef _footerRefreshBtn;
    private float _footerRefreshRevertAt = -1f;
    private const string FooterRefreshLabel = "Refresh";

    public bool IsFullscreen => _isFullscreen;

    /// <summary>0.9.7: toggle the main panel between its current size+pos and
    /// a stretched fullscreen layout (a small inset preserves border-grab
    /// for the resize handle in case the user wants to exit by dragging).
    /// Snapshots the prior layout so a second toggle restores it pixel-for-
    /// pixel. Only applies to the Primary UI — overlays are size-only.</summary>
    public void ToggleFullscreen() => SetFullscreen(!_isFullscreen);

    public void SetFullscreen(bool fullscreen)
    {
        if (Rect == null) return;
        if (fullscreen == _isFullscreen) return;

        if (fullscreen)
        {
            // Snapshot every Rect field that's about to change. anchorMin/Max
            // pivot tend to be (0.5, 0.5) by default for this panel, but we
            // don't assume — restore exactly what was there.
            _preFullscreenSizeDelta   = Rect.sizeDelta;
            _preFullscreenAnchoredPos = Rect.anchoredPosition;
            _preFullscreenAnchorMin   = Rect.anchorMin;
            _preFullscreenAnchorMax   = Rect.anchorMax;
            _preFullscreenPivot       = Rect.pivot;
            _preFullscreenPinned      = IsPinned;

            // Stretch to fill the canvas. With anchorMin=(0,0)/anchorMax=(1,1)
            // sizeDelta becomes the margin (offset from each edge), so setting
            // it to zero makes the panel exactly canvas-sized; the small inset
            // applied via offsetMin/offsetMax leaves room for resize-by-edge
            // gestures so the user can manually shrink back if needed.
            Rect.anchorMin = UnityEngine.Vector2.zero;
            Rect.anchorMax = UnityEngine.Vector2.one;
            Rect.pivot     = new UnityEngine.Vector2(0.5f, 0.5f);
            Rect.offsetMin = new UnityEngine.Vector2(20f, 20f);
            Rect.offsetMax = new UnityEngine.Vector2(-20f, -20f);

            // 0.11.2 critical fix: force IsPinned=true while fullscreen.
            // Friend-test surfaced two bugs that both came from the same
            // root cause — stretched anchors invert sizeDelta semantics:
            //   (a) AutoResizeIfEnabled assigns sizeDelta.y = desired
            //       height; with stretched anchors that means "make me
            //       desired px LARGER than the parent" = panel becomes
            //       screen.height + desired tall.
            //   (b) PanelDragger's resize-drag does the same assignment,
            //       so grabbing the edge to resize ALSO blows the panel
            //       up the moment the mouse moves.
            // Both vectors are eliminated by blocking PanelDragger
            // entirely while fullscreen — IsPinned makes Update() early-
            // return. The only safe interaction in fullscreen mode is
            // the maximize button itself, which calls back into this
            // method to exit fullscreen and restore IsPinned.
            IsPinned = true;
            _isFullscreen = true;
        }
        else
        {
            Rect.anchorMin       = _preFullscreenAnchorMin;
            Rect.anchorMax       = _preFullscreenAnchorMax;
            Rect.pivot           = _preFullscreenPivot;
            Rect.sizeDelta       = _preFullscreenSizeDelta;
            Rect.anchoredPosition= _preFullscreenAnchoredPos;
            IsPinned             = _preFullscreenPinned;
            _isFullscreen = false;
        }

        Dragger?.OnEndResize();
        UpdateMaximizeBtnVisuals();

        // 0.16: hide the always-on-top floating launcher while fullscreen so it
        // can't sit over (and intercept clicks meant for) the panel's own
        // close/restore controls on smaller monitors. Restored on exit.
        Plugin.UIManager?.OnMainPanelFullscreenChanged(_isFullscreen);
        // 0.11.2 IMPORTANT: do NOT call OnFinishResize() here. The
        // pre-0.11.2 code did, which persisted the fullscreen-mode
        // sizeDelta to config — directly contradicting the comment at
        // line 295 saying "fullscreen is treated as transient." If the
        // user closed the game in fullscreen, next session restored the
        // stretched anchors and oversized sizeDelta, immediately re-
        // entering the broken state. Normal drag/resize still saves
        // through PanelBase.OnFinishDrag/OnFinishResize when the user
        // is NOT in fullscreen; the pre-fullscreen save is preserved.
    }

    private void UpdateMaximizeBtnVisuals()
    {
        if (_maximizeBtn == null) return;
        // Plain text glyphs known to render in V Rising's TMPro fallback set
        // (see docs/LESSONS_LEARNED.md). "[ ]" reads as "make window full" and
        // "[X]" as "restore" without needing icon glyph support.
        _maximizeBtn.ButtonText.text = _isFullscreen ? "[X]" : "[ ]";
    }

    /// <summary>0.9.7: insert a maximize/restore button to the LEFT of the close
    /// button in the PanelBase-built title bar. Called once at end of
    /// ConstructPanelContent — by then the base class has built TitleBar and
    /// CloseButton (see PanelBase.ConstructUI which builds the title bar
    /// before invoking the subclass ConstructPanelContent).</summary>
    private void BuildMaximizeButton()
    {
        if (CloseButton == null) return; // title bar was hidden by subclass override

        // The CloseButton GameObject is actually the right-aligned HOLDER
        // containing the actual close button. Add a sibling button inside
        // the same holder so both share the right-aligned cluster — and put
        // our button at sibling index 0 so it renders LEFT of the close.
        _maximizeBtn = Raphael.UI.Framework.UniverseLib.UI.UIFactory.CreateButton(
            CloseButton, "MaximizeButton", _isFullscreen ? "[X]" : "[ ]");
        UnityEngine.Object.Destroy(_maximizeBtn.Component.gameObject.GetComponent<UnityEngine.UI.Outline>());
        Raphael.UI.Framework.UniverseLib.UI.UIFactory.SetLayoutElement(
            _maximizeBtn.Component.gameObject,
            minHeight: 25, minWidth: 36, flexibleWidth: 0);
        _maximizeBtn.Component.colors = new UnityEngine.UI.ColorBlock()
        {
            normalColor    = Raphael.UI.Framework.CustomLib.Util.Theme.SliderHandle,
            colorMultiplier = 1,
        };
        _maximizeBtn.OnClick += ToggleFullscreen;
        // Move to the leftmost position inside CloseHolder so it appears
        // before the existing "—" close button. CreateButton appended at end;
        // SetSiblingIndex(0) pulls it to the front.
        _maximizeBtn.Component.gameObject.transform.SetSiblingIndex(0);
    }

    protected override void ConstructPanelContent()
    {
        // 0.10.13 fix (vertical analog of the 0.9.8 horizontal fix below):
        // ContentRoot's VLG was created with childForceExpandHeight=true in
        // PanelBase.CreatePanel. Unity's vertical layout distributes any
        // extra space EQUALLY among all children when forceExpand is true,
        // regardless of per-child flexibleHeight — so the LastResponse
        // panel + OverlayFooter + TooltipFooter all grew alongside the
        // tab content area when the user dragged the panel taller, even
        // though only `body` (flex=1) was supposed to absorb extra space.
        // Friend-test: "the tooltip area grows unnecessarily when the
        // settings/text pane is what should grow." Force-expand=false
        // makes only flex>0 children absorb extra space — `body` gets it
        // all, and the footers stay at their preferred sizes.
        var rootVlg = ContentRoot.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (rootVlg != null) rootVlg.childForceExpandHeight = false;

        // 0.9.8 fix: forceExpandWidth was true through 0.9.7. Unity's
        // HorizontalLayoutGroup ignores per-child flexibleWidth=0 when the
        // parent has childForceExpandWidth=true — every child gets an equal
        // share of extra space regardless. That's why the 0.9.7 attempt to
        // cap the tab strip via flexibleWidth=0 + preferredWidth=180 didn't
        // hold: as the main panel widened, the strip kept getting half the
        // extra width even though it was supposed to stay at 180. The right
        // content area still expands cleanly without forceExpandWidth because
        // BuildContentArea sets flexibleWidth=1 on the content's LayoutElement.
        var body = UIFactory.CreateHorizontalGroup(ContentRoot, "Body",
            forceExpandWidth: false, forceExpandHeight: true,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(6, 6, 6, 6));
        UIFactory.SetLayoutElement(body, flexibleHeight: 1, flexibleWidth: 1);

        // 0.67.1: the shell (rail + footers + title bar button) registers its tooltips as PRIORITY so the
        // lazily-built pages, which now register later, can't steal the footer's hover (see TooltipHover).
        var shellSw = System.Diagnostics.Stopwatch.StartNew();
        TooltipHover.PriorityScope = true;
        try
        {
            BuildTabStrip(body);
            BuildContentArea(body);
            BuildLastResponsePanel(ContentRoot);
            BuildOverlayFooter(ContentRoot);
            BuildTooltipFooter(ContentRoot);
            // B4: a clearer, branded title (default was the raw PanelId "MainPanel"). Bold + accent so it
            // stands out; the title bar is also the drag handle.
            SetTitle("<b><color=#C41E3A>Raphael</color> Main Panel</b>");   // vampiric blood-red accent

            // 0.9.7: maximize/restore button in the title bar, left of "—".
            BuildMaximizeButton();
        }
        finally { TooltipHover.PriorityScope = false; }

        // 0.67.1: group-wide state subscriptions used to be wired as a side effect of building the mod tabs.
        // With lazy pages, wire them with the shell so Beelzebub's reply caching (broadcast pools / kits) and
        // the Connection readout stay live before any mod tab is opened. Every handler null-checks its page
        // fields (audited), so an unbuilt page is simply skipped.
        EnsureBeelzSubscribed();
        EnsureUrielSubscribed();
        EnsureFaustSubscribed();
        EnsureNyarSubscribed();
        if (!_mapImageSubscribed)
        {
            FaustMapBackdrop.ImageLoaded += OnFaustMapImageLoaded;
            _mapImageSubscribed = true;
        }

        // 0.67.1: frame 1 is shell-only — the first page is built next frame behind a "Loading…" placeholder,
        // then the rest are prewarmed one per frame while the panel is open.
        ShowTabDeferred(InitialTabForFirstShow());
        if (_prewarmTicker == null)
        {
            _prewarmTicker = TickLazyPages;
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_prewarmTicker);
        }
        shellSw.Stop();
        LogUtils.LogInfo($"MainPanel: shell built in {shellSw.Elapsed.TotalMilliseconds:0} ms (tab pages build on demand).");

        // 0.9.6: per-frame ticker that auto-refreshes the wep / blood-legacy
        // stat-values on a 10s cadence while their tab is the active page.
        // Cheap when no relevant tab is active (one ActiveTab compare + one
        // time check). Registered once; Reset() unregisters.
        if (_tabAutoRefreshTicker == null)
        {
            _tabAutoRefreshTicker = TickTabAutoRefresh;
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_tabAutoRefreshTicker);
        }
    }

    private void BuildTooltipFooter(GameObject parent)
    {
        // 0.9.3: footer height bumped 22 → 56 and label set to word-wrap.
        // Pre-0.9.3 long tooltips (notably the new OV master-overlay button's
        // multi-sentence description) overflowed the panel's right edge as
        // a single un-wrapped line. The new height holds ~3 lines of 12pt
        // italic text; longer tooltips get ellipsized instead of overflowing.
        // 0.65.6: forceExpandWidth false (was true) so the new fixed-width Refresh button stays small — a
        // force-expanding HLG ignores flexibleWidth=0 (see the 0.9.8 note in ConstructPanelContent); the
        // tooltip label keeps flexibleWidth=1 and still takes all the slack. MiddleLeft centers the button
        // vertically against the 56px footer.
        var footer = UIFactory.CreateHorizontalGroup(parent, "TooltipFooter",
            forceExpandWidth: false, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(8, 8, 4, 4));
        UIFactory.SetLayoutElement(footer, minHeight: 56, preferredHeight: 56, flexibleHeight: 0, flexibleWidth: 1);
        var footerHlg = footer.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        if (footerHlg != null) footerHlg.childAlignment = TextAnchor.MiddleLeft;

        // 0.10.13: dropped italic and bumped font size 12 → 13 for legibility.
        // Friend-test: italic at standard text scale is hard to read.
        var lbl = UIFactory.CreateLabel(footer, "TooltipText",
            TooltipHover.IdlePlaceholder,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 400, preferredWidth: 600, flexibleWidth: 1,
            minHeight: 48, preferredHeight: 52, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Normal;
        lbl.TextMesh.enableWordWrapping = true;
        lbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        // Apply muted color so the tooltip text reads as secondary/contextual.
        lbl.TextMesh.color = Theme.MutedBody;

        // 0.65.6: bottom-right "Refresh" — same action as Connection → Refresh Raphael.
        _footerRefreshBtn = UIFactory.CreateButton(footer, "FooterRefreshBtn", FooterRefreshLabel);
        UIFactory.SetLayoutElement(_footerRefreshBtn.Component.gameObject,
            minWidth: Theme.ScaledWidth(64), preferredWidth: Theme.ScaledWidth(68), flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        if (_footerRefreshBtn.ButtonText != null)
        {
            _footerRefreshBtn.ButtonText.enableWordWrapping = false;
            _footerRefreshBtn.ButtonText.fontSize = Theme.ScaledUI(12);
        }
        TooltipHover.Attach(_footerRefreshBtn.Component.gameObject,
            "Refresh Raphael: clears chat history and cached UI leftovers and frees memory — the same cleanup a " +
            "relog does, without leaving the world. Settings and open windows are kept. (Also on the Connection tab.)");
        _footerRefreshBtn.OnClick += () =>
        {
            bool ok = RunRaphaelRefresh();
            if (_footerRefreshBtn?.ButtonText != null)
                _footerRefreshBtn.ButtonText.text = ok ? "<color=#90EE90>Done</color>" : "<color=#FF8080>Failed</color>";
            _footerRefreshRevertAt = UnityEngine.Time.unscaledTime + 2f;
        };

        // Wire the static Sink so the per-frame TooltipHover.TickAll updates
        // this label. The TickAll action itself is registered in Plugin.Load,
        // so it's already running by the time we get here.
        TooltipHover.Sink = lbl.TextMesh;
        LogUtils.LogInfo($"TooltipHover sink wired ({TooltipHover.BindingCount} bindings tracked).");

        // Subscribe AutoResizeIfEnabled to CollapsibleSection toggles so the
        // panel grows/shrinks when the user expands an admin/prestige form.
        // Static event, single subscription per panel construction; Reset
        // unsubscribes.
        if (!_collapsibleSubscribed)
        {
            CollapsibleSection.Toggled += AutoResizeIfEnabled;
            _collapsibleSubscribed = true;
        }
    }

    private bool _collapsibleSubscribed;

    // -----------------------------------------------------------------------
    // Last server response panel (0.8.3)
    //
    // Always docked above the overlay footer. When the user clicks a read-data
    // command (.wep get / .class l / .misc userstats / etc.), the response is
    // captured by MessageService_Processing.AwaitingGenericResponse and routed
    // here so it lands in the UI instead of only in chat. Friend-testing of
    // v0.8.1 surfaced "it would load into the chat window rather than loading
    // into the UI informational box" — this is the structural fix.
    //
    // Hidden until the first response arrives. Click the header to collapse
    // the body so the panel doesn't crowd the active tab on narrow screens.
    // -----------------------------------------------------------------------

    private GameObject _lastResponseRoot;
    private GameObject _lastResponseBodyWrap;
    private TextMeshProUGUI _lastResponseHeader;
    private TextMeshProUGUI _lastResponseBody;
    private bool _lastResponseCollapsed;
    private bool _lastResponseSubscribed;

    private void BuildLastResponsePanel(GameObject parent)
    {
        _lastResponseRoot = UIFactory.CreateVerticalGroup(parent, "LastResponsePanel",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(8, 8, 4, 4));
        UIFactory.SetLayoutElement(_lastResponseRoot,
            minHeight: 0, flexibleHeight: 0, flexibleWidth: 1);
        _lastResponseRoot.SetActive(false); // shown when first response arrives

        // Header button — click to collapse/expand the body.
        var headerBtn = UIFactory.CreateButton(_lastResponseRoot, "LastResponseHeaderBtn", "");
        UIFactory.SetLayoutElement(headerBtn.GameObject,
            minWidth: 360, preferredWidth: 600, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var headerText = headerBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (headerText != null)
        {
            headerText.alignment = TextAlignmentOptions.MidlineLeft;
            headerText.fontSize = Theme.ScaledUI(12);
            headerText.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _lastResponseHeader = headerText;
        }
        headerBtn.OnClick = () =>
        {
            _lastResponseCollapsed = !_lastResponseCollapsed;
            if (_lastResponseBodyWrap != null)
                _lastResponseBodyWrap.SetActive(!_lastResponseCollapsed);
            UpdateLastResponseHeaderText();
            AutoResizeIfEnabled();
        };
        TooltipHover.Attach(headerBtn.GameObject,
            "Click to collapse/expand. Updates whenever you click a read-data command in any tab (.wep get, .class l, .misc userstats, .clan list, .boss list, etc.). Replies still also appear in chat unless you've enabled Clear server messages.");

        _lastResponseBodyWrap = UIFactory.CreateVerticalGroup(_lastResponseRoot, "LastResponseBody",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 0, padding: new Vector4(6, 6, 4, 4));
        UIFactory.SetLayoutElement(_lastResponseBodyWrap,
            minHeight: 0, flexibleHeight: 0, flexibleWidth: 1);

        // Multi-line label with ContentSizeFitter so the panel sizes to fit
        // whatever the server returned without truncation.
        var bodyLbl = UIFactory.CreateLabel(_lastResponseBodyWrap, "LastResponseText",
            "", TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(bodyLbl.GameObject,
            minWidth: 360, preferredWidth: 600, flexibleWidth: 1,
            minHeight: 0, flexibleHeight: 0);
        bodyLbl.TextMesh.enableWordWrapping = true;
        bodyLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        bodyLbl.TextMesh.richText = true; // keep server-sent <color=...> tags
        // 0.9.1: server color tags (e.g. <color=red> blood headings) get a
        // wider outline so they stay legible when the panel sits over a red
        // in-game background.
        ApplyStrongAccentOutline(bodyLbl.TextMesh);
        var fitter = bodyLbl.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        _lastResponseBody = bodyLbl.TextMesh;

        if (!_lastResponseSubscribed)
        {
            PlayerStateService.LastResponseChanged += OnLastResponseChanged;
            _lastResponseSubscribed = true;
        }
    }

    private void OnLastResponseChanged()
    {
        if (_lastResponseRoot == null) return;
        var r = PlayerStateService.LastResponse;

        _lastResponseRoot.SetActive(true);
        UpdateLastResponseHeaderText();

        if (_lastResponseBody != null)
        {
            // Concatenate lines with newlines. TMP renders the inline color
            // tags, so the response looks like the server-side chat output.
            _lastResponseBody.text = r.Lines != null
                ? string.Join("\n", r.Lines)
                : "";
        }

        // Whenever a new response arrives, auto-expand so the user notices.
        _lastResponseCollapsed = false;
        if (_lastResponseBodyWrap != null) _lastResponseBodyWrap.SetActive(true);

        AutoResizeIfEnabled();
    }

    private void UpdateLastResponseHeaderText()
    {
        if (_lastResponseHeader == null) return;
        var r = PlayerStateService.LastResponse;
        var arrow = _lastResponseCollapsed ? "▶" : "▼";
        var cmd   = string.IsNullOrEmpty(r.Command) ? "(no response yet)" : r.Command;
        var count = r.Lines?.Count ?? 0;
        _lastResponseHeader.text = $"{arrow}  Last server response — <color=#9ECCFF>{cmd}</color>  ({count} line{(count == 1 ? "" : "s")})";
    }

    // -----------------------------------------------------------------------
    // Tab strip (left rail)
    // -----------------------------------------------------------------------

    // 0.9.7: max width applied to the tab strip so it doesn't grow
    // proportionally when the main panel widens (or when UI text scales up).
    // 0.10.2 bump: 180 → 220. The v0.9.8 "Help" → "SETTINGS AND HELP" rename
    // pushed the longest group header to ~17 chars ("SETTINGS AND HELP") plus
    // the ▶/▼ prefix, which overlapped the rail edge at Standard scale and
    // truncated at Large scale. 220 covers Large scale with margin.
    private const float TAB_STRIP_MAX_WIDTH = 220f;

    private void BuildTabStrip(GameObject parent)
    {
        // 0.12.1: subscribe to Bloodcraft availability transitions so a late
        // handshake ACK flips the group from tentative-available to confirmed
        // (or to unavailable on give-up) without rebuilding the panel. Idempotent
        // — Reset() unsubscribes, and the flag prevents double-subscribe if
        // BuildTabStrip is called again on rebuild.
        if (!_availabilitySubscribed)
        {
            Services.EclipseProtocolService.AvailabilityChanged += OnBloodcraftAvailabilityChanged;
            // 0.15.0: per-feature flag transitions also drive UI refresh.
            PlayerStateService.FeatureFlagsChanged += OnFeatureFlagsChanged;
            // 0.18: Beelzebub presence transitions flip the Beelzebub group from
            // tentative -> available (or -> unavailable on give-up). Reuses the same
            // refresh-all handler (it re-evaluates every group's availability).
            Services.Beelzebub.BeelzProtocolService.AvailabilityChanged += OnBloodcraftAvailabilityChanged;
            // 0.24.7: ALSO reconcile the group off BeelzState.PresenceChanged — not just the single
            // AvailabilityChanged fire. AvailabilityChanged fires exactly once (in OnVersion when presence
            // first resolves); if that one deferred refresh doesn't land on the live panel (build/subscribe
            // timing during the handshake-settle window), the BEELZEBUB group stays "(unavailable)" forever
            // while the Connection readout — which DOES listen to PresenceChanged — shows Connected. That
            // exact contradiction was the tester report. PresenceChanged also fires on SetSubscribed
            // (`[BEELZ:bch] state=on`, which arrives right after detection), so binding the group refresh to
            // it guarantees a second reconciliation pass. The handler dedupes (deferred-action guard) and
            // RefreshAllTabGroupAvailability is idempotent, so the extra fires are cheap.
            Services.Beelzebub.BeelzState.PresenceChanged += OnBloodcraftAvailabilityChanged;
            // 0.26: Uriel presence transitions flip the Uriel group available/unavailable. Same dual
            // subscription as Beelzebub (one-shot AvailabilityChanged + the repeated PresenceChanged) so
            // a late ACK that misses the single deferred refresh still reconciles the group header.
            Services.Uriel.UrielProtocolService.AvailabilityChanged += OnBloodcraftAvailabilityChanged;
            Services.Uriel.UrielState.PresenceChanged += OnBloodcraftAvailabilityChanged;
            // Faust presence transitions flip the Faust group available/unavailable. Same dual
            // subscription as Beelzebub/Uriel (one-shot AvailabilityChanged + the repeated PresenceChanged)
            // so a late ACK that misses the single deferred refresh still reconciles the group header.
            Services.Faust.FaustProtocolService.AvailabilityChanged += OnBloodcraftAvailabilityChanged;
            Services.Faust.FaustState.PresenceChanged += OnBloodcraftAvailabilityChanged;
            // Nyarlathotep: same dual subscription.
            Services.Nyar.NyarProtocolService.AvailabilityChanged += OnBloodcraftAvailabilityChanged;
            Services.Nyar.NyarState.PresenceChanged += OnBloodcraftAvailabilityChanged;
            _availabilitySubscribed = true;
        }

        // 0.15.0: wrap the tab strip in a ScrollRect so it scrolls when the
        // expanded sum of group content heights exceeds the panel's left-rail
        // height. Pre-0.15.0: with all four groups (BLOODCRAFT 12 / KINDRED 6
        // / SETTINGS+HELP 5+) expanded simultaneously, the VLG ran out of
        // vertical space and collapsed the lowest-priority group's content
        // to minHeight=0 — Bloodcraft sub-tab buttons rendered at zero height
        // visually overlapping the KINDRED header. The ScrollRect makes that
        // overflow scroll instead of clipping. Combined with the minHeight ==
        // preferredHeight clamp in BuildTabGroup, content can no longer
        // collapse but DOES scroll cleanly when there's too much of it.
        var scrollWrap = UIFactory.CreateScrollView(parent, "TabStripScroll",
            out var stripContent, out _, color: Color.clear);
        // 0.49: the rail width tracks the UI font multiplier (Theme.ScaledWidth). The left pane is
        // normally a fixed width, but at large font-size-% settings the longest tab captions outgrew
        // the fixed 220px strip and the centered button text overflowed past the button's right edge
        // (tester report). Scaling the strip (and the button/container widths below) in lockstep with
        // the font keeps captions inside their buttons; the content area (flexibleWidth:1) absorbs the
        // difference, so the pane only grows as much as the text requires.
        UIFactory.SetLayoutElement(scrollWrap,
            minWidth: Theme.ScaledWidth((int)TAB_STRIP_MAX_WIDTH),
            preferredWidth: Theme.ScaledWidth((int)TAB_STRIP_MAX_WIDTH),
            flexibleWidth: 0,
            flexibleHeight: 1);

        // childControlHeight: true is required - the strip stacks group headers
        // and group-content blocks of varying heights, and without it the layout
        // group leaves children at default sizeDelta (~0px) so KINDRED/HELP
        // headers overlap the BLOODCRAFT sub-tab list.
        // 0.15.0: CreateScrollView already added a VerticalLayoutGroup +
        // ContentSizeFitter to the scroll content; we just tune its padding
        // and spacing to match the previous TabStrip styling.
        var stripVlg = stripContent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (stripVlg != null)
        {
            stripVlg.spacing = 2;
            var pad = stripVlg.padding;
            pad.left = 2; pad.right = 2; pad.top = 2; pad.bottom = 2;
            stripVlg.padding = pad;
            // childControlHeight defaults to true on CreateScrollView, which is
            // what we need so the group headers/content size correctly.
        }
        _tabStripGo = stripContent;

        foreach (var group in TabGroups)
            BuildTabGroup(stripContent, group);

        // Reconcile the restored expansion with what's detected RIGHT NOW (a rebuild after detection settled, or an
        // already-connected reopen). The event path (RefreshAllTabGroupAvailability) re-runs it as detection resolves.
        ReconcileRailExpansion();
    }

    private void BuildTabGroup(GameObject parent, TabGroupDef group)
    {
        // Resolve per-group availability. There are now THREE states for the
        // Bloodcraft group (Item 1, 0.15.0):
        //   available           — render normally with sub-tabs.
        //   diagnostic pending  — render with the "registration failed" panel
        //                         instead of sub-tabs; header still expandable.
        //   disabled             — user explicitly set Off in .cfg; grayed.
        bool available  = IsTabGroupAvailable(group.Title);
        bool diagnostic = IsModDiagnosticState(group.Title);
        // Header is interactable when group is usable OR when we have a
        // diagnostic to show. Only the explicit-Off case fully disables it.
        bool headerInteractable = available || diagnostic;
        // 0.72.0: start from the rail memory (a rebuild keeps what was open); a diagnostic group is no longer forced
        // open: RailPolicy opens at most one mod group, and only before the user has touched the rail.
        bool startExpanded = (RailMemory.Expanded.TryGetValue(group.Title, out var remembered) ? remembered : group.StartExpanded)
                             && headerInteractable;
        _groupExpanded[group.Title] = startExpanded;
        RailMemory.Expanded[group.Title] = startExpanded;

        // Header button - clicking toggles the group's content visibility (when interactable).
        var header = UIFactory.CreateButton(parent, $"GroupHeader_{group.Title}",
            FormatGroupHeader(group.Title, startExpanded, available || diagnostic));
        UIFactory.SetLayoutElement(header.GameObject,
            minWidth: Theme.ScaledWidth(140), preferredWidth: Theme.ScaledWidth(144), flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var headerText = header.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (headerText != null)
        {
            headerText.alignment = TextAlignmentOptions.MidlineLeft;
            headerText.enableWordWrapping = false;
            headerText.overflowMode = TextOverflowModes.Overflow;
            headerText.fontStyle = FontStyles.Bold;
            headerText.fontSize = Theme.ScaledUI(12);
            // In diagnostic state we keep the text bright so the user notices
            // and clicks in — the grayed treatment is reserved for explicit Off.
            if (!available && !diagnostic) headerText.color = new Color(0.55f, 0.55f, 0.55f);
            _groupHeaderText[group.Title] = headerText;
        }
        header.Component.interactable = headerInteractable;
        _groupHeaderButton[group.Title] = header;
        TooltipHover.Attach(header.GameObject,
            diagnostic
                ? "Bloodcraft handshake didn't complete — click to see why and force-enable the tabs anyway."
                : (available
                    ? $"Show / hide the {group.Title} tab list."
                    : $"{group.Title} isn't available yet: Raphael is still connecting to it, or it wasn't detected on this server. Adjust via .cfg: BloodcraftAvailability / KindredAvailability = On to force-enable."));

        // Outer container for everything inside the group (sub-tabs OR diagnostic).
        var content = UIFactory.CreateVerticalGroup(parent, $"GroupContent_{group.Title}",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(6, 2, 2, 2));
        // 0.15.0: clamp minHeight to preferredHeight so the parent VLG can't
        // collapse this content down to zero when the strip's total content
        // exceeds the available height. Pre-0.15.0 minHeight was 0 — with all
        // groups expanded, the BLOODCRAFT group (12 sub-tabs * 30 px = ~360 px
        // tall preferred) would collapse to 0 px and its Admin button
        // overlapped the KINDRED header beneath it. Combined with the
        // ScrollRect added to BuildTabStrip, content now keeps its full
        // height AND the strip scrolls when overflow happens.
        int groupContentHeight = Mathf.Max(28, group.Tabs.Length * 30 + 4);
        UIFactory.SetLayoutElement(content,
            minWidth: Theme.ScaledWidth(140), preferredWidth: Theme.ScaledWidth(144), flexibleWidth: 1,
            minHeight: groupContentHeight, preferredHeight: groupContentHeight, flexibleHeight: 0);
        _groupContent[group.Title] = content;

        // Sub-tab button list lives inside a nested container so we can hide
        // the buttons without hiding the diagnostic that sits next to them.
        var tabListGo = UIFactory.CreateVerticalGroup(content, $"GroupTabList_{group.Title}",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(tabListGo,
            minWidth: Theme.ScaledWidth(130), preferredWidth: Theme.ScaledWidth(140), flexibleWidth: 1,
            minHeight: 0, flexibleHeight: 0);
        _groupTabListGo[group.Title] = tabListGo;

        if (group.Tabs.Length == 0)
        {
            var placeholder = UIFactory.CreateLabel(tabListGo, "Empty",
                "(coming soon)",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(placeholder.GameObject,
                minWidth: Theme.ScaledWidth(130), preferredWidth: Theme.ScaledWidth(140), flexibleWidth: 1,
                minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
            placeholder.TextMesh.fontStyle = FontStyles.Italic;
            placeholder.TextMesh.enableWordWrapping = false;
        }
        else
        {
            foreach (var (tab, label) in group.Tabs)
            {
                var b = UIFactory.CreateButton(tabListGo, $"TabBtn_{tab}", label);
                UIFactory.SetLayoutElement(b.GameObject,
                    minWidth: Theme.ScaledWidth(130), preferredWidth: Theme.ScaledWidth(138), flexibleWidth: 1,
                    minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
                var t = b.Component.GetComponentInChildren<TextMeshProUGUI>();
                if (t != null)
                {
                    t.enableWordWrapping = false;
                    t.overflowMode = TextOverflowModes.Overflow;
                    t.alignment = TextAlignmentOptions.Center;
                    t.fontSize = Theme.ScaledUI(13);
                }
                var captured = tab;
                // 0.67.1: deferred — shows "Loading…" this frame and builds the page next frame if it
                // isn't built yet, so a click never freezes the UI mid-press.
                var groupTitle = group.Title;
                b.OnClick = () => { ApplyRailChanges(RailPolicy.TabClick(groupTitle)); ShowTabDeferred(captured); };
                _tabButtons[tab] = b;
                // 0.15.0: apply initial per-tab dimming based on the system
                // availability detected from ProgressToClient broadcasts.
                ApplyTabAvailability(captured);
            }
        }

        // 0.15.0: build the inline diagnostic panel alongside the sub-tab list. Hidden whenever the group
        // is normal-available; shown when the mod's Auto-mode handshake gave up. Both mod rails get one.
        if (group.Title == "Bloodcraft")
            BuildBloodcraftDiagnosticPanel(content);
        else if (group.Title == "Beelzebub")
            BuildBeelzDiagnosticPanel(content);
        else if (group.Title == "Uriel")
            BuildUrielDiagnosticPanel(content);
        else if (group.Title == "Faust")
            BuildFaustDiagnosticPanel(content);
        else if (group.Title == "Nyarlathotep")
            BuildNyarDiagnosticPanel(content);

        // Apply initial visibility — diagnostic OR sub-tab list, never both.
        ApplyModGroupVisibility(group.Title, diagnostic);

        content.SetActive(startExpanded);
        if (headerInteractable) header.OnClick = () => ToggleGroup(group.Title);
    }

    // 0.15.0: helper called from both BuildTabGroup (initial render) and
    // RefreshTabGroupAvailability (when the handshake completes or gives up
    // mid-session) so the diagnostic + sub-tab visibility stays consistent
    // with the current Bloodcraft availability state.
    // Swap the sub-tab list ↔ the inline diagnostic panel for a mod group (Bloodcraft OR Beelzebub).
    private void ApplyModGroupVisibility(string groupTitle, bool diagnostic)
    {
        if (_groupTabListGo.TryGetValue(groupTitle, out var tabListGo) && tabListGo != null)
            tabListGo.SetActive(!diagnostic);
        GameObject diagGo = groupTitle == "Bloodcraft" ? _bloodcraftDiagnosticGo
                          : groupTitle == "Beelzebub"  ? _beelzDiagnosticGo
                          : groupTitle == "Uriel"      ? _urielDiagnosticGo
                          : groupTitle == "Faust"      ? _faustDiagnosticGo
                          : groupTitle == "Nyarlathotep" ? _nyarDiagnosticGo
                          : null;
        if (diagGo != null) diagGo.SetActive(diagnostic);
    }

    // 0.15.0 (reverted): per-tab visibility/dimming based on detected system
    // availability. Friend-test on a fully-enabled Bloodcraft server showed
    // false positives — the Familiar/Shift signals (HasActive / SpellIndex)
    // only fire when the user is actively engaged with those systems at
    // broadcast time. A logged-in player who hasn't summoned a familiar
    // OR cast their shift spell in 30s falsely tripped the Disabled state
    // even though the server fully supports those systems. ConfigsToClient
    // max-level fields don't help either (they're config defaults that
    // come through regardless of whether the system is enabled). Restoring
    // unconditional tab rendering until a more reliable probe (chat-regex
    // "Familiars are not enabled." reply detection, manual user-set
    // per-system visibility toggles, or a Bloodcraft-side protocol
    // extension) lands in a follow-up release. The FeatureFlags
    // infrastructure still tracks detection results for diagnostic mode.
    private void ApplyTabAvailability(PanelType tab)
    {
        // Intentionally a no-op for 0.15.0 — see comment above.
        _ = tab;
    }

    private static string LookupTabLabel(PanelType tab)
    {
        foreach (var g in TabGroups)
            foreach (var (t, l) in g.Tabs)
                if (t == tab) return l;
        return null;
    }

    // 0.15.0: refresh per-tab dimming on every tab when FeatureFlagsChanged
    // fires. Called from the deferred FeatureFlagsChanged handler so the
    // tab strip stays in sync with the latest detection results.
    private void RefreshAllTabAvailability()
    {
        foreach (var key in new List<PanelType>(_tabButtons.Keys))
            ApplyTabAvailability(key);
    }

    // 0.15.0: in-rail diagnostic shown when Bloodcraft handshake gave up while
    // the user is still on the Auto setting. Friend-test 0.14.0 surfaced two
    // failure modes that produced the same "empty Raphael" symptom:
    //   • server runs ONLY QuestSystem or ProfessionSystem — Bloodcraft's
    //     Core.Eclipsed gate (ChatMessageSystemPatch.cs:24) requires at least
    //     one of {Leveling, Legacy, Expertise, Class, Familiar} to even
    //     PROCESS our RegisterUser handshake;
    //   • older Bloodcraft + the misleadingly-named Eclipsed=false config
    //     value, which on some pre-1.13 builds was a hard kill rather than
    //     the modern "broadcast frequency" knob.
    // Both cases leave the user unable to navigate into the tabs at all,
    // so the chat-regex pipeline (which DOES work without Core.Eclipsed —
    // .quest p, .fam boxes, .bl get, .wep get, etc.) is unreachable. The
    // "Force-enable" button below flips BloodcraftAvailability=On for the
    // rest of the session so the user can drive whatever the regex pipe
    // supports. Doc context lives in README.md's compatibility section.
    private void BuildBloodcraftDiagnosticPanel(GameObject parent)
    {
        var card = UIFactory.CreateVerticalGroup(parent, "BloodcraftDiagnostic",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(6, 6, 6, 6),
            bgColor: Theme.CardBackground);
        UIFactory.SetLayoutElement(card,
            minWidth: 130, preferredWidth: 140, flexibleWidth: 1,
            minHeight: 0, flexibleHeight: 0);
        _bloodcraftDiagnosticGo = card;

        // Yellow attention-grabbing heading.
        var heading = UIFactory.CreateLabel(card, "DiagHeading",
            "Handshake failed",
            TextAlignmentOptions.MidlineLeft, color: Color.yellow, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(heading.GameObject,
            minWidth: 120, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        heading.TextMesh.fontStyle = FontStyles.Bold;
        heading.TextMesh.enableWordWrapping = true;

        // Body — wrapped to ~140 px. Concise on purpose; the README
        // carries the full explanation.
        var body = UIFactory.CreateLabel(card, "DiagBody",
            "Server didn't ACK our Bloodcraft handshake after 15s.\n\n" +
            "Likely causes:\n" +
            "• Bloodcraft not installed on the server.\n" +
            "• Bloodcraft installed but only Quests / Professions enabled (needs at least one of Leveling / Legacy / Expertise / Class / Familiar to broadcast).\n" +
            "• Older Bloodcraft with server config Eclipsed=false.",
            TextAlignmentOptions.TopLeft, color: Theme.MutedBody, fontSize: Theme.ScaledUI(10));
        UIFactory.SetLayoutElement(body.GameObject,
            minWidth: 120, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 110, flexibleHeight: 1);
        body.TextMesh.enableWordWrapping = true;
        body.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // Force-enable button — flips BloodcraftAvailability to On so the
        // sub-tabs become accessible. Doesn't make the structured pipeline
        // start working (server still won't ACK), but lets the user drive
        // the chat-regex pipeline (.quest p / .fam boxes / etc.) via the
        // tab forms.
        var btn = UIFactory.CreateButton(card, "DiagForceEnableBtn", "Force-enable tabs");
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 120, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        var btnTxt = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (btnTxt != null)
        {
            btnTxt.fontSize = Theme.ScaledUI(11);
            btnTxt.fontStyle = FontStyles.Bold;
            btnTxt.enableWordWrapping = true;
        }
        btn.OnClick = OnBloodcraftForceEnableClicked;
        TooltipHover.Attach(btn.GameObject,
            "Flip BloodcraftAvailability to On for the rest of the session. The Bloodcraft tabs become navigable so you can manually issue commands (.quest p / .fam boxes / .bl get / etc.) and read replies in the Last server response panel. Live overlay updates still won't work because the server's structured broadcast remains disabled — see the README's Server compatibility section for the full picture.");

        // Re-check — restart detection from scratch (fixes the server-switch case: the old re-detect
        // no-op'd once registration had given up). The most common one-click recovery after a server switch.
        AddDiagnosticActionButton(card, "DiagRecheckBtn", "Re-check now",
            "Restart Bloodcraft detection and re-send the registration handshake. Use after switching servers if the tabs didn't light up on their own.",
            () => { try { Services.EclipseProtocolService.Reset(); Services.EclipseProtocolService.SendRegistration(); } catch { } RefreshAllTabGroupAvailability(); });

        // Footnote — points to the .cfg setting for permanence.
        var footnote = UIFactory.CreateLabel(card, "DiagFootnote",
            "To persist across sessions, set BloodcraftAvailability=On in kdpen.Raphael.cfg.",
            TextAlignmentOptions.TopLeft, color: Theme.MutedBody, fontSize: Theme.ScaledUI(9));
        UIFactory.SetLayoutElement(footnote.GameObject,
            minWidth: 120, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 30, flexibleHeight: 0);
        footnote.TextMesh.fontStyle = FontStyles.Italic;
        footnote.TextMesh.enableWordWrapping = true;
    }

    // Shared styled action button for the inline diagnostic panels (Force-enable / Re-check), matching
    // the diagnostic card's button look so Bloodcraft + Beelzebub panels stay consistent.
    private ButtonRef AddDiagnosticActionButton(GameObject card, string name, string label, string tooltip, System.Action onClick)
    {
        var btn = UIFactory.CreateButton(card, name, label);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 120, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(11); t.fontStyle = FontStyles.Bold; t.enableWordWrapping = true; }
        btn.OnClick = onClick;
        TooltipHover.Attach(btn.GameObject, tooltip);
        return btn;
    }

    // Inline "Beelzebub not detected" panel for the Beelzebub rail — mirrors the Bloodcraft diagnostic so a
    // user can recover WITHOUT leaving the game (the long-standing ask). Shown when the `.beelz api version`
    // handshake gave up in Auto mode (IsBeelzDiagnosticState). Two actions: Re-check (restart detection) and
    // Force-enable (flip the tab group On for the session so the command/loadout tabs are reachable anyway).
    private void BuildBeelzDiagnosticPanel(GameObject parent)
    {
        var card = UIFactory.CreateVerticalGroup(parent, "BeelzDiagnostic",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(6, 6, 6, 6),
            bgColor: Theme.CardBackground);
        UIFactory.SetLayoutElement(card,
            minWidth: 130, preferredWidth: 140, flexibleWidth: 1,
            minHeight: 0, flexibleHeight: 0);
        _beelzDiagnosticGo = card;

        var heading = UIFactory.CreateLabel(card, "BeelzDiagHeading", "Beelzebub not detected",
            TextAlignmentOptions.MidlineLeft, color: Color.yellow, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(heading.GameObject,
            minWidth: 120, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        heading.TextMesh.fontStyle = FontStyles.Bold;
        heading.TextMesh.enableWordWrapping = true;

        var body = UIFactory.CreateLabel(card, "BeelzDiagBody",
            "No reply to the Beelzebub handshake on this server.\n\n" +
            "Likely causes:\n" +
            "• Beelzebub isn't installed on this server.\n" +
            "• You just switched servers and it's still loading — click Re-check.\n" +
            "• A slow connection let the first probes time out.",
            TextAlignmentOptions.TopLeft, color: Theme.MutedBody, fontSize: Theme.ScaledUI(10));
        UIFactory.SetLayoutElement(body.GameObject,
            minWidth: 120, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 96, flexibleHeight: 1);
        body.TextMesh.enableWordWrapping = true;
        body.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // Re-check — the primary recovery: restart the handshake loop from scratch (Reset re-anchors +
        // re-probes). This is the in-tab equivalent of Settings & Help → Connection → Re-detect.
        AddDiagnosticActionButton(card, "BeelzDiagRecheckBtn", "Re-check now",
            "Restart Beelzebub detection and re-probe (.beelz api version). Use after switching servers if the tabs didn't light up on their own.",
            () => { try { Services.Beelzebub.BeelzProtocolService.Reset(); Services.Beelzebub.BeelzClient.RequestVersion(); } catch { } RefreshAllTabGroupAvailability(); });

        // Force-enable — make the tabs reachable even with no handshake, so loadout/command tabs work as a
        // console (live overlays stay empty until a real handshake). Session-only; .cfg makes it permanent.
        AddDiagnosticActionButton(card, "BeelzDiagForceEnableBtn", "Force-enable tabs",
            "Flip BeelzebubAvailability to On for the rest of the session so the Beelzebub tabs are navigable. Live data needs a real handshake; set BeelzebubAvailability=On in kdpen.Raphael.cfg to persist.",
            OnBeelzForceEnableClicked);

        var footnote = UIFactory.CreateLabel(card, "BeelzDiagFootnote",
            "To persist across sessions, set BeelzebubAvailability=On in kdpen.Raphael.cfg.",
            TextAlignmentOptions.TopLeft, color: Theme.MutedBody, fontSize: Theme.ScaledUI(9));
        UIFactory.SetLayoutElement(footnote.GameObject,
            minWidth: 120, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 30, flexibleHeight: 0);
        footnote.TextMesh.fontStyle = FontStyles.Italic;
        footnote.TextMesh.enableWordWrapping = true;
    }

    private void OnBeelzForceEnableClicked()
    {
        try
        {
            Settings.SetBeelzebubAvailability(Settings.ModAvailability.On);
            LogUtils.LogInfo("Beelzebub availability force-enabled by user via diagnostic panel. Session-only — edit kdpen.Raphael.cfg to make it permanent.");
        }
        catch (System.Exception ex) { LogUtils.LogError($"OnBeelzForceEnableClicked failed: {ex}"); }
        RefreshAllTabGroupAvailability();
    }

    // 0.26: inline "Uriel not detected" panel for the Uriel rail — mirrors the Beelzebub diagnostic so a
    // user can recover WITHOUT leaving the game. Shown when the `.uriel api version` handshake gave up in
    // Auto mode (IsUrielDiagnosticState). Two actions: Re-check (restart detection) and Force-enable.
    private void BuildUrielDiagnosticPanel(GameObject parent)
    {
        var card = UIFactory.CreateVerticalGroup(parent, "UrielDiagnostic",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(6, 6, 6, 6),
            bgColor: Theme.CardBackground);
        UIFactory.SetLayoutElement(card,
            minWidth: 130, preferredWidth: 140, flexibleWidth: 1,
            minHeight: 0, flexibleHeight: 0);
        _urielDiagnosticGo = card;

        var heading = UIFactory.CreateLabel(card, "UrielDiagHeading", "Uriel not detected",
            TextAlignmentOptions.MidlineLeft, color: Color.yellow, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(heading.GameObject,
            minWidth: 120, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        heading.TextMesh.fontStyle = FontStyles.Bold;
        heading.TextMesh.enableWordWrapping = true;

        var body = UIFactory.CreateLabel(card, "UrielDiagBody",
            "No reply to the Uriel handshake on this server.\n\n" +
            "Likely causes:\n" +
            "• Uriel isn't installed on this server.\n" +
            "• You just switched servers and it's still loading — click Re-check.\n" +
            "• A slow connection let the first probes time out.",
            TextAlignmentOptions.TopLeft, color: Theme.MutedBody, fontSize: Theme.ScaledUI(10));
        UIFactory.SetLayoutElement(body.GameObject,
            minWidth: 120, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 96, flexibleHeight: 1);
        body.TextMesh.enableWordWrapping = true;
        body.TextMesh.overflowMode = TextOverflowModes.Overflow;

        AddDiagnosticActionButton(card, "UrielDiagRecheckBtn", "Re-check now",
            "Restart Uriel detection and re-probe (.uriel api version). Use after switching servers if the tabs didn't light up on their own.",
            () => { try { Services.Uriel.UrielProtocolService.Reset(); Services.Uriel.UrielClient.RequestVersion(); } catch { } RefreshAllTabGroupAvailability(); });

        AddDiagnosticActionButton(card, "UrielDiagForceEnableBtn", "Force-enable tabs",
            "Flip UrielAvailability to On for the rest of the session so the Uriel tabs are navigable. Live data needs a real handshake; set UrielAvailability=On in kdpen.Raphael.cfg to persist.",
            OnUrielForceEnableClicked);

        var footnote = UIFactory.CreateLabel(card, "UrielDiagFootnote",
            "To persist across sessions, set UrielAvailability=On in kdpen.Raphael.cfg.",
            TextAlignmentOptions.TopLeft, color: Theme.MutedBody, fontSize: Theme.ScaledUI(9));
        UIFactory.SetLayoutElement(footnote.GameObject,
            minWidth: 120, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 30, flexibleHeight: 0);
        footnote.TextMesh.fontStyle = FontStyles.Italic;
        footnote.TextMesh.enableWordWrapping = true;
    }

    private void OnUrielForceEnableClicked()
    {
        try
        {
            Settings.SetUrielAvailability(Settings.ModAvailability.On);
            LogUtils.LogInfo("Uriel availability force-enabled by user via diagnostic panel. Session-only — edit kdpen.Raphael.cfg to make it permanent.");
        }
        catch (System.Exception ex) { LogUtils.LogError($"OnUrielForceEnableClicked failed: {ex}"); }
        RefreshAllTabGroupAvailability();
    }

    // 0.15.0: click handler for the diagnostic's Force-enable button. Flips
    // BloodcraftAvailability=On (session-only, not persisted to disk) and
    // refreshes the tab strip so the sub-tab buttons replace the diagnostic.
    private void OnBloodcraftForceEnableClicked()
    {
        try
        {
            Settings.SetBloodcraftAvailability(Settings.ModAvailability.On);
            LogUtils.LogInfo("Bloodcraft availability force-enabled by user via diagnostic panel. Setting persists for the session only — edit kdpen.Raphael.cfg to make it permanent.");
        }
        catch (System.Exception ex)
        {
            LogUtils.LogError($"OnBloodcraftForceEnableClicked failed: {ex}");
        }
        // Refresh the tab strip availability so the sub-tabs appear immediately.
        RefreshAllTabGroupAvailability();
    }

    /// <summary>
    /// Resolves availability for a tab group ("Bloodcraft" / "Kindred" / "Help").
    /// Auto = Bloodcraft uses the Eclipse handshake ACK, Kindred is currently
    /// always-on (no probe wired). Help is always available.
    /// </summary>
    private static bool IsTabGroupAvailable(string title)
    {
        switch (title)
        {
            case "Bloodcraft":
                return Settings.BloodcraftAvailability switch
                {
                    Settings.ModAvailability.On  => true,
                    Settings.ModAvailability.Off => false,
                    // 0.18.3: "hidden until confirmed". Available only once the server has
                    // ACKed our registration (UserRegistered) — NOT optimistically during the
                    // probe window. Rationale (server-switch follow-up): the old
                    // `UserRegistered || !RegistrationGaveUp` showed the tab as available the
                    // whole time, so on a server WITHOUT Bloodcraft it stayed enabled showing the
                    // previous server's stale data until give-up (and a stuck-Pending bug meant
                    // give-up never fired — see ClientChatPatch). Now it greys out the instant we
                    // relog and only lights up when THIS server confirms Bloodcraft. Under Eclipse
                    // stand-down we never register (Eclipse owns the HUD) but Raphael still works as a
                    // command console, so keep the tab available there.
                    _ => Services.EclipseProtocolService.StandDownForEclipse()
                      || Services.EclipseProtocolService.UserRegistered,
                };
            case "Kindred":
                return Settings.KindredAvailability switch
                {
                    Settings.ModAvailability.On   => true,
                    Settings.ModAvailability.Off  => false,
                    _ => true, // no probe wired - assume present
                };
            case "Beelzebub":
                return Settings.BeelzebubAvailability switch
                {
                    Settings.ModAvailability.On  => true,
                    Settings.ModAvailability.Off => false,
                    // 0.18.3: "hidden until confirmed" (was `IsPresent || !DetectionGaveUp`).
                    // Available ONLY once the `.beelz api version` handshake ACKs ready=1. We no
                    // longer show the tab as tentatively-available during the probe window — testers
                    // saw it sit enabled through 5-7 probes and then suddenly grey out (jarring if
                    // you'd opened the tab). Now it starts greyed on every relog and lights up the
                    // moment THIS server confirms Beelzebub; if a later probe ACKs it's restored.
                    _ => Services.Beelzebub.BeelzProtocolService.IsPresent,
                };
            case "Uriel":
                return Settings.UrielAvailability switch
                {
                    Settings.ModAvailability.On  => true,
                    Settings.ModAvailability.Off => false,
                    // "hidden until confirmed" — available ONLY once the `.uriel api version` handshake
                    // ACKs ready=1 (same model as Beelzebub). Greyed on relog; lights up when THIS server
                    // confirms Uriel; recover via the inline diagnostic Re-check or Connection → Re-detect.
                    _ => Services.Uriel.UrielProtocolService.IsPresent,
                };
            case "Faust":
                return Settings.FaustAvailability switch
                {
                    Settings.ModAvailability.On  => true,
                    Settings.ModAvailability.Off => false,
                    // "hidden until confirmed" — available ONLY once the `.faust api version` handshake
                    // ACKs ready=1 (same model as Beelzebub/Uriel). Greyed on relog; lights up when THIS
                    // server confirms Faust; recover via the inline Re-check or Connection → Re-detect.
                    _ => Services.Faust.FaustProtocolService.IsPresent,
                };
            case "Nyarlathotep":
                return Settings.NyarAvailability switch
                {
                    Settings.ModAvailability.On  => true,
                    Settings.ModAvailability.Off => false,
                    // "hidden until confirmed" — same model as Faust/Uriel.
                    _ => Services.Nyar.NyarProtocolService.IsPresent,
                };
            case "Game UI":
                return true; // standalone client-side enhancements; no server probe
            default:
                return true; // Help, future groups
        }
    }

    /// <summary>0.15.0: true when the Bloodcraft group should render the
    /// diagnostic panel (handshake failed + user is on the default Auto
    /// setting). When the user has explicitly set BloodcraftAvailability=On
    /// or =Off, we honor that choice and never show the diagnostic. The
    /// diagnostic state is distinct from "available" — header is still
    /// interactable so the user can expand to read the message.</summary>
    private static bool IsBloodcraftDiagnosticState(string title)
    {
        if (title != "Bloodcraft") return false;
        if (Settings.BloodcraftAvailability != Settings.ModAvailability.Auto) return false;
        return Services.EclipseProtocolService.RegistrationGaveUp
            && !Services.EclipseProtocolService.UserRegistered;
    }

    /// <summary>Beelzebub analogue: in Auto mode, the handshake (`.beelz api version`) gave up without a
    /// ready ACK. Shows the inline "not detected — enable / re-check" panel under the Beelzebub rail,
    /// mirroring Bloodcraft's. Distinct from "available" — the header stays interactable so the user can
    /// expand to read it and recover.</summary>
    private static bool IsBeelzDiagnosticState(string title)
    {
        if (title != "Beelzebub") return false;
        if (Settings.BeelzebubAvailability != Settings.ModAvailability.Auto) return false;
        return Services.Beelzebub.BeelzProtocolService.DetectionGaveUp
            && !Services.Beelzebub.BeelzProtocolService.IsPresent;
    }

    /// <summary>Uriel analogue: in Auto mode, the `.uriel api version` handshake gave up without a
    /// ready ACK. Shows the inline "not detected — enable / re-check" panel under the Uriel rail.</summary>
    private static bool IsUrielDiagnosticState(string title)
    {
        if (title != "Uriel") return false;
        if (Settings.UrielAvailability != Settings.ModAvailability.Auto) return false;
        return Services.Uriel.UrielProtocolService.DetectionGaveUp
            && !Services.Uriel.UrielProtocolService.IsPresent;
    }

    /// <summary>Faust analogue: in Auto mode, the `.faust api version` handshake gave up without a
    /// ready ACK. Shows the inline "not detected — enable / re-check" panel under the Faust rail.</summary>
    private static bool IsFaustDiagnosticState(string title)
    {
        if (title != "Faust") return false;
        if (Settings.FaustAvailability != Settings.ModAvailability.Auto) return false;
        return Services.Faust.FaustProtocolService.DetectionGaveUp
            && !Services.Faust.FaustProtocolService.IsPresent;
    }

    /// <summary>Nyarlathotep analogue: in Auto mode, the `.nyar api version` handshake gave up without a
    /// ready ACK. Shows the inline "not detected — enable / re-check" panel under the Nyarlathotep rail.</summary>
    private static bool IsNyarDiagnosticState(string title)
    {
        if (title != "Nyarlathotep") return false;
        if (Settings.NyarAvailability != Settings.ModAvailability.Auto) return false;
        return Services.Nyar.NyarProtocolService.DetectionGaveUp
            && !Services.Nyar.NyarProtocolService.IsPresent;
    }

    /// <summary>Any mod group can show an inline diagnostic when its Auto-mode handshake fails.</summary>
    private static bool IsModDiagnosticState(string title)
        => IsBloodcraftDiagnosticState(title) || IsBeelzDiagnosticState(title) || IsUrielDiagnosticState(title)
        || IsFaustDiagnosticState(title) || IsNyarDiagnosticState(title);

    /// <summary>0.15.0: map each Bloodcraft tab to the Bloodcraft system that
    /// backs it. Returns null when the tab isn't tied to a single system
    /// (Admin/Prestige cross-cut multiple systems and aren't gated). UI
    /// uses this to dim/hide tabs when the corresponding system shows as
    /// Disabled after the feature-detection settling window.</summary>
    private static PlayerStateService.SystemKind? TabBackingSystem(PanelType tab)
    {
        switch (tab)
        {
            case PanelType.FamiliarsTab:
            case PanelType.BoxesTab:
            case PanelType.VBloodsTab:
            case PanelType.AllFamiliarsTab:
                return PlayerStateService.SystemKind.Familiar;
            case PanelType.ClassTab:
                return PlayerStateService.SystemKind.Class;
            case PanelType.ExpertiseTab:
                return PlayerStateService.SystemKind.Expertise;
            case PanelType.BloodLegacyTab:
                return PlayerStateService.SystemKind.Legacy;
            case PanelType.LevelsTab:
                return PlayerStateService.SystemKind.Leveling;
            case PanelType.DailyQuestTab:
                return PlayerStateService.SystemKind.Quest;
            case PanelType.UnarmedShiftTab:
                return PlayerStateService.SystemKind.ShiftSlot;
            // PrestigeTab + AdminTab cross-cut multiple systems / pure admin
            // tools — both always render.
            default:
                return null;
        }
    }

    private void ToggleGroup(string title)
    {
        if (!_groupExpanded.ContainsKey(title)) return;
        // Accordion (default ON): opening a group collapses every other group, so the left rail stays short
        // on small screens. Players who prefer several sections open at once disable it in Display settings.
        ApplyRailChanges(RailPolicy.HeaderClick(RailSnapshot(), title, Config.Settings.LeftRailAccordion));
        AutoResizeIfEnabled();
    }


    private static string FormatGroupHeader(string title, bool expanded, bool available = true)
    {
        var t = title.ToUpper();
        // 0.67.1: a mod whose handshake is still in flight reads "(connecting…)" rather than "(unavailable)",
        // so the rail shows Raphael is still working instead of looking broken. "(unavailable)" only after
        // detection gives up (or the group is forced Off).
        if (!available) return IsTabGroupProbing(title) ? $"–  {t}  (connecting…)" : $"–  {t}  (unavailable)";
        return expanded ? $"▼  {t}" : $"▶  {t}";
    }

    /// <summary>0.67.1: true while a mod group's Auto-mode handshake is still in progress (not confirmed, not
    /// given up). Drives the "(connecting…)" header state. Always false for non-mod groups and forced On/Off.</summary>
    private static bool IsTabGroupProbing(string title)
    {
        switch (title)
        {
            case "Bloodcraft":
                return Settings.BloodcraftAvailability == Settings.ModAvailability.Auto
                    && !Services.EclipseProtocolService.StandDownForEclipse()
                    && !Services.EclipseProtocolService.UserRegistered
                    && !Services.EclipseProtocolService.RegistrationGaveUp;
            case "Beelzebub":
                return Settings.BeelzebubAvailability == Settings.ModAvailability.Auto
                    && !Services.Beelzebub.BeelzProtocolService.IsPresent
                    && !Services.Beelzebub.BeelzProtocolService.DetectionGaveUp;
            case "Uriel":
                return Settings.UrielAvailability == Settings.ModAvailability.Auto
                    && !Services.Uriel.UrielProtocolService.IsPresent
                    && !Services.Uriel.UrielProtocolService.DetectionGaveUp;
            case "Faust":
                return Settings.FaustAvailability == Settings.ModAvailability.Auto
                    && !Services.Faust.FaustProtocolService.IsPresent
                    && !Services.Faust.FaustProtocolService.DetectionGaveUp;
            case "Nyarlathotep":
                return Settings.NyarAvailability == Settings.ModAvailability.Auto
                    && !Services.Nyar.NyarProtocolService.IsPresent
                    && !Services.Nyar.NyarProtocolService.DetectionGaveUp;
            default:
                return false;
        }
    }

    // 0.12.1: AvailabilityChanged subscriber. Defers to the next CoreUpdateBehavior
    // tick because the event fires from inside ClientChatPatch.OnUpdate_Prefix
    // (mid-iteration of the chat entity array) — running UI mutations there is
    // legal but the deferred-frame timing keeps it consistent with the rest of
    // our event handlers (RequestRebuildMainPanel, etc.).
    private System.Action _deferredAvailabilityRefresh;

    private void OnBloodcraftAvailabilityChanged()
    {
        if (_deferredAvailabilityRefresh != null) return; // already queued for this frame
        _deferredAvailabilityRefresh = () =>
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_deferredAvailabilityRefresh);
            _deferredAvailabilityRefresh = null;
            RefreshAllTabGroupAvailability();
            // 0.18.3: also hide/show overlays to match the mod that just resolved (BC stream overlays
            // when Bloodcraft isn't present; the Beelz action-bar overlay when Beelzebub isn't present).
            try { Plugin.UIManager?.ApplyAvailabilityToOverlays(); }
            catch (System.Exception ex) { LogUtils.LogError($"ApplyAvailabilityToOverlays failed: {ex}"); }
        };
        Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_deferredAvailabilityRefresh);
    }

    // 0.15.0: per-feature flag transition handler. Same deferred-frame
    // pattern as OnBloodcraftAvailabilityChanged (this fires from inside
    // ClientChatPatch.OnUpdate_Prefix mid-iteration; mutating UI mid-
    // iteration is technically legal but the deferred pattern is what
    // every other event subscriber uses).
    private System.Action _deferredFeatureFlagsRefresh;
    private void OnFeatureFlagsChanged()
    {
        if (_deferredFeatureFlagsRefresh != null) return;
        _deferredFeatureFlagsRefresh = () =>
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_deferredFeatureFlagsRefresh);
            _deferredFeatureFlagsRefresh = null;
            RefreshAllTabAvailability();
            // Push the same flags out to BCHubUIManager so it can hide
            // overlays whose backing system was just detected disabled.
            try { Plugin.UIManager?.ApplyServerFeatureFlagsToOverlays(); }
            catch (System.Exception ex) { LogUtils.LogError($"ApplyServerFeatureFlagsToOverlays failed: {ex}"); }
        };
        Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_deferredFeatureFlagsRefresh);
    }

    private void RefreshAllTabGroupAvailability()
    {
        foreach (var title in new System.Collections.Generic.List<string>(_groupHeaderText.Keys))
            RefreshTabGroupAvailability(title);
        ReconcileRailExpansion();
        AutoResizeIfEnabled();
    }

    /// <summary>0.18.3: public entry so the relog path (BCHubUIManager.RestoreAfterRelogIfNeeded)
    /// can re-evaluate every tab group's availability the instant we re-enter a world. After a
    /// server-switch the protocol services have been Reset (UserRegistered=false / IsPresent=false),
    /// so this greys the Bloodcraft + Beelzebub groups back out — they only re-light when THIS
    /// server's handshake ACKs (which fires AvailabilityChanged → RefreshAllTabGroupAvailability
    /// again). Without this the headers kept the previous server's "available" state until the
    /// (slow) give-up or ACK resolved.</summary>
    public void RefreshTabGroupAvailabilityNow()
    {
        try { RefreshAllTabGroupAvailability(); }
        catch (System.Exception ex) { LogUtils.LogError($"RefreshTabGroupAvailabilityNow failed: {ex}"); }
    }

    // 0.72.0: the rail's current state as RailPolicy sees it, in rail order.
    private System.Collections.Generic.List<RailGroup> RailSnapshot()
    {
        var list = new System.Collections.Generic.List<RailGroup>(TabGroups.Length);
        foreach (var g in TabGroups)
        {
            if (!_groupExpanded.TryGetValue(g.Title, out var open)) continue;
            bool mod = RailPolicy.IsModGroup(g.Title);
            list.Add(new RailGroup
            {
                Title = g.Title,
                IsMod = mod,
                Available = IsTabGroupAvailable(g.Title),
                Diagnostic = mod && IsModDiagnosticState(g.Title),
                Expanded = open,
                Intent = RailMemory.Intent.TryGetValue(g.Title, out var it) ? it : RailIntent.None,
            });
        }
        return list;
    }

    // 0.72.0: the group holding the active tab (null when the active tab isn't on the rail).
    private string ActiveTabGroupTitle()
    {
        foreach (var g in TabGroups)
            foreach (var (t, _) in g.Tabs)
                if (t == ActiveTab) return g.Title;
        return null;
    }

    // 0.72.0: replaces ApplyDetectionDefaultExpansion and the diagnostic auto-expand. At most one mod group opens by
    // itself (the active mod's), only before the user's first rail click, and never next to a group the user opened;
    // groups the user opened or closed keep that across rebuilds and detection changes. Rules + tests: RailPolicy.
    private void ReconcileRailExpansion()
    {
        ApplyRailChanges(RailPolicy.Reconcile(RailSnapshot(), ActiveTabGroupTitle(), Config.Settings.LeftRailAccordion,
            RailMemory.UserTookControl));
        AutoResizeIfEnabled();
    }

    // 0.72.0: the single writer of group expansion: content visibility, header arrow, and the rail memory.
    private void ApplyRailChanges(RailChanges ch)
    {
        if (ch == null) return;
        if (ch.UserTookControl) RailMemory.UserTookControl = true;
        foreach (var kv in ch.Intent) RailMemory.Intent[kv.Key] = kv.Value;
        foreach (var kv in ch.Expand) ApplyExpansion(kv.Key, kv.Value);
    }

    private void ApplyExpansion(string title, bool expanded)
    {
        if (!_groupExpanded.ContainsKey(title)) return;
        bool usable = IsTabGroupAvailable(title) || IsModDiagnosticState(title);
        if (expanded && !usable) return;   // never expand a dead header
        _groupExpanded[title] = expanded;
        RailMemory.Expanded[title] = expanded;
        if (_groupContent.TryGetValue(title, out var go) && go != null) go.SetActive(expanded);
        if (_groupHeaderText.TryGetValue(title, out var txt) && txt != null) txt.text = FormatGroupHeader(title, expanded, usable);
    }

    // 0.22 first-run onboarding + open-to-active-mod. The very first time the panel ever opens we route the
    // user to the Quick Start tab (a one-time, mod-agnostic welcome) instead of dropping them into a feature
    // tab, and flip the persisted HasSeenWelcome flag so it happens exactly once. On every later open we pick
    // the active mod's primary tab when detection already resolved (a reopen / rebuild); during the first
    // login's handshake window neither mod is confirmed yet, so we keep the existing default and let the
    // rail's group expansion follow detection as it settles.
    private PanelType InitialTabForFirstShow()
    {
        if (!Config.Settings.HasSeenWelcome)
        {
            Config.Settings.SetHasSeenWelcome(true);
            // Reveal the Quick Start rail entry; it counts as the user's place, so no mod group opens next to it.
            var firstRun = new RailChanges();
            firstRun.Expand["Settings and Help"] = true;
            firstRun.Intent["Settings and Help"] = RailIntent.Open;
            ApplyRailChanges(firstRun);
            return PanelType.QuickStartTab;
        }
        if (IsTabGroupAvailable("Bloodcraft")) return PanelType.FamiliarsTab;
        if (IsTabGroupAvailable("Beelzebub"))  return PanelType.BeelzLoadoutTab;   // 0.24.8: Loadout is now the group's primary tab
        return ActiveTab;   // default (FamiliarsTab) until a handshake confirms a mod
    }

    private void RefreshTabGroupAvailability(string title)
    {
        bool available  = IsTabGroupAvailable(title);
        bool diagnostic = IsModDiagnosticState(title);
        bool headerInteractable = available || diagnostic;
        bool expanded = _groupExpanded.TryGetValue(title, out var e) && e;

        // 0.72.0: no expansion here. ReconcileRailExpansion (run right after by RefreshAllTabGroupAvailability) decides
        // what opens or closes, so a "not detected" group no longer pops open on every Nyarlathotep state change.
        if (_groupHeaderText.TryGetValue(title, out var headerText))
        {
            headerText.text = FormatGroupHeader(title, expanded && headerInteractable, available || diagnostic);
            // Diagnostic state stays bright (uses default text color) so it
            // catches the user's attention; explicit-Off is the only grayed state.
            headerText.color = headerInteractable ? Theme.DefaultText : new Color(0.55f, 0.55f, 0.55f);
        }
        if (_groupHeaderButton.TryGetValue(title, out var btn))
        {
            btn.Component.interactable = headerInteractable;
            // Re-wire the OnClick: a previously-unavailable header had its OnClick
            // skipped during BuildTabGroup. Assign now if it's become available.
            btn.OnClick = headerInteractable ? () => ToggleGroup(title) : null;
        }

        // 0.15.0: swap sub-tab list ↔ diagnostic panel based on current state.
        ApplyModGroupVisibility(title, diagnostic);
    }

    // -----------------------------------------------------------------------
    // Tab content area (right side) - dispatches per-tab builders
    // -----------------------------------------------------------------------

    // 0.67: Beelzebub, Uriel and Faust are in beta and had downloads but no feedback. One slim line at the top
    // of each of their tabs asks for issues / ideas and links The Shadow Realm Discord (permanent invite).
    // 0.67.4: Nyarlathotep added.
    private const string SHADOW_REALM_INVITE = "https://discord.gg/c4YU2HbHFA";
    private static readonly string[] BetaFeedbackGroups = { "Beelzebub", "Uriel", "Faust", "Nyarlathotep" };

    private void MaybeAddBetaFeedbackBanner(GameObject page, PanelType tab)
    {
        string mod = null;
        foreach (var g in TabGroups)
        {
            if (System.Array.IndexOf(BetaFeedbackGroups, g.Title) < 0) continue;
            foreach (var (t, _) in g.Tabs) if (t == tab) { mod = g.Title; break; }
            if (mod != null) break;
        }
        if (mod == null) return;

        var row = UIFactory.CreateHorizontalGroup(page, "BetaFeedbackBanner",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 6, 4), bgColor: Theme.CardBackground);
        UIFactory.SetLayoutElement(row, minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);

        var lbl = UIFactory.CreateLabel(row, "BetaFeedbackText",
            $"<color=#E6B85C><b>{mod} is in beta.</b></color> <color={Theme.MutedBodyHex}>Found a bug or have an idea? Tell us on The Shadow Realm Discord.</color>",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(lbl.GameObject, minWidth: 240, preferredWidth: 320, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;

        var btn = UIFactory.CreateButton(row, "BetaFeedbackBtn", "Give feedback");
        UIFactory.SetLayoutElement(btn.GameObject, minWidth: Theme.ScaledWidth(104), preferredWidth: Theme.ScaledWidth(110), flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
        var bt = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (bt != null) { bt.fontSize = Theme.ScaledUI(11); bt.enableWordWrapping = false; bt.overflowMode = TextOverflowModes.Overflow; }
        btn.OnClick = () =>
        {
            try { UnityEngine.Application.OpenURL(SHADOW_REALM_INVITE); }
            catch (System.Exception ex) { LogUtils.LogWarning($"OpenURL('{SHADOW_REALM_INVITE}') threw: {ex.Message}"); }
        };
        TooltipHover.Attach(btn.GameObject, $"Open The Shadow Realm Discord ({SHADOW_REALM_INVITE}) in your browser. Post {mod} issues or suggestions in the Raphael channel.");
    }

    // 0.17.1: when Raphael is standing down from the passive Bloodcraft layer because
    // Eclipse is installed (see EclipseProtocolService.StandDownForEclipse), put a
    // clear notice at the top of each affected Bloodcraft-data tab — what's off,
    // why, and what still works. Kindred / Game-UI / Settings tabs are unaffected,
    // so they get no banner.
    private void MaybeAddEclipseStandDownBanner(GameObject page, PanelType tab)
    {
        if (!Services.EclipseProtocolService.StandDownForEclipse()) return;
        if (!IsBloodcraftDataTab(tab)) return;

        var card = AddCard(page, "EclipseStandDownNotice");
        AddSectionHeading(card, "Eclipse detected — Bloodcraft readouts off here");
        AddBodyText(card,
            "Eclipse is installed, so Raphael turns OFF its own live Bloodcraft " +
            "data to stay compatible (the two can't run together otherwise — it's a " +
            "known Eclipse-side crash). Eclipse's HUD shows your live XP / legacy / " +
            "expertise / familiar / professions / quest data instead.");
        AddBodyText(card,
            "Still works here: every command button on this tab, plus Kindred / " +
            "KindredLogistics commands and the tabbed chat window. Info panels are " +
            "blank until you press their Refresh / query button (live auto-updates " +
            "are disabled in this mode).");
        AddBodyText(card,
            "Want Raphael's own live overlays back? Disable Eclipse in your mod " +
            "manager — Raphael covers the same readouts on its own.");
    }

    // The Bloodcraft-data tabs whose passive readouts go dark under Eclipse
    // stand-down. Kindred*, GameUI, Settings, About, Help, Admin tabs are unaffected.
    private static bool IsBloodcraftDataTab(PanelType t) => t switch
    {
        PanelType.FamiliarsTab or PanelType.BoxesTab or PanelType.VBloodsTab
        or PanelType.AllFamiliarsTab or PanelType.ClassTab or PanelType.ExpertiseTab
        or PanelType.BloodLegacyTab or PanelType.UnarmedShiftTab or PanelType.PrestigeTab
        or PanelType.LevelsTab or PanelType.DailyQuestTab => true,
        _ => false,
    };

    private void BuildContentArea(GameObject parent)
    {
        var content = UIFactory.CreateVerticalGroup(parent, "TabContent",
            forceWidth: true, forceHeight: true,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(6, 6, 6, 6));
        UIFactory.SetLayoutElement(content,
            minWidth: 380, preferredWidth: 420, flexibleWidth: 1,
            minHeight: 280, preferredHeight: 320, flexibleHeight: 1);

        // 0.67.1 PERF: pages are no longer built here. Building all ~60 tab pages in one frame froze the game
        // for seconds on first open (and again on every whole-panel rebuild). The shell now holds only the
        // container + a "Loading…" placeholder; each page is built on demand / prewarmed one per frame by
        // EnsureTabBuilt → BuildTabPage (see the "Lazy tab pages" section near ShowTab).
        _tabContentRoot = content;
        BuildPlaceholderPage(content);
    }

    // 0.67.1: builds ONE tab page — exactly what BuildContentArea used to do per tab inside its loop. Registers
    // the page in _tabContent/_tabInnerContent (inactive); EnsureTabBuilt owns ordering, teardown and failure.
    private GameObject BuildTabPage(PanelType tab, string label)
    {
        var pageWrapper = CreateTabPage(_tabContentRoot, out var page);
        _buildingWrapper = pageWrapper;   // 0.67.1: lets EnsureTabBuilt destroy a half-built page if a builder throws
        AddTabHeading(page, label);
        MaybeAddEclipseStandDownBanner(page, tab); // 0.17.1: command-console-mode notice
        MaybeAddBetaFeedbackBanner(page, tab);     // 0.67: beta feedback ask on Beelzebub / Uriel / Faust / Nyarlathotep tabs

        switch (tab)
        {
            case PanelType.FamiliarsTab:
                BuildFamiliarsTab(page);
                break;
            case PanelType.BoxesTab:
                BuildBoxesTab(page);
                break;
            case PanelType.VBloodsTab:
                BuildVBloodsTab(page);
                break;
            case PanelType.AllFamiliarsTab:
                BuildAllFamiliarsTab(page);
                break;
            case PanelType.ClassTab:
                BuildClassTab(page);
                break;
            case PanelType.ExpertiseTab:
                BuildExpertiseTab(page);
                break;
            case PanelType.BloodLegacyTab:
                BuildBloodLegacyTab(page);
                break;
            case PanelType.UnarmedShiftTab:
                BuildUnarmedShiftTab(page);
                break;
            case PanelType.PrestigeTab:
                BuildPrestigeTab(page);
                break;
            case PanelType.LevelsTab:
                BuildLevelsTab(page);
                break;
            case PanelType.AdminTab:
                BuildAdminTab(page);
                break;
            case PanelType.KindredLogisticsTab:
                BuildKindredLogisticsTab(page);
                break;
            case PanelType.KindredLogisticsAdminTab:
                BuildKindredLogisticsAdminTab(page);
                break;
            case PanelType.DailyQuestTab:
                BuildDailyQuestTab(page);
                break;
            case PanelType.KindredCommandsPlayerTab:
                BuildKindredCommandsPlayerTab(page);
                break;
            case PanelType.KindredAdminPlayersTab:
                BuildKindredAdminPlayersTab(page);
                break;
            case PanelType.KindredAdminServerTab:
                BuildKindredAdminServerTab(page);
                break;
            case PanelType.KindredAdminWorldTab:
                BuildKindredAdminWorldTab(page);
                break;
            // 0.73.0: every guide tab renders its Services/Guides page; old Help ids are aliases.
            case PanelType.QuickStartTab:           RenderGuide(page, Raphael.Services.Guides.Guides.Get("quickstart")); break;
            case PanelType.BloodcraftQuickStartTab:
            case PanelType.ModHelpTab:              RenderGuide(page, Raphael.Services.Guides.Guides.Get("bloodcraft")); break;
            case PanelType.BeelzQuickStartTab:
            case PanelType.BeelzModHelpTab:         RenderGuide(page, Raphael.Services.Guides.Guides.Get("beelzebub")); break;
            case PanelType.KindredGuideTab:         RenderGuide(page, Raphael.Services.Guides.Guides.Get("kindred")); break;
            case PanelType.VRisingBasicsTab:        RenderGuide(page, Raphael.Services.Guides.Guides.Get("vrisingbasics")); break;
            case PanelType.GameGuideTab:            RenderGuide(page, Raphael.Services.Guides.Guides.Get("gameguide")); break;
            case PanelType.SettingsTab:
                BuildSettingsTab(page);
                break;
            case PanelType.ChatDiagTab:
                BuildChatDiagTab(page);
                break;
            case PanelType.GameCatalogTab:
                BuildGameCatalogTab(page);
                break;
            case PanelType.ConnectionTab:
                BuildConnectionTab(page);
                break;
            case PanelType.AboutTab:
                BuildAboutTab(page);
                break;
            case PanelType.VanillaAdminTab:
                BuildVanillaAdminTab(page);
                break;
            case PanelType.GameUITab:
                BuildGameUITab(page);
                break;
            case PanelType.BeelzBestiaryTab:
                BuildBeelzBestiaryTab(page);
                break;
            case PanelType.BeelzLoadoutTab:
                BuildBeelzLoadoutTab(page);
                break;
            case PanelType.BeelzHotkeysTab:
                BuildBeelzHotkeysTab(page);
                break;
            case PanelType.BeelzTransformsTab:
                BuildBeelzTransformsTab(page);
                break;
            case PanelType.BeelzSettingsTab:
                BuildBeelzSettingsTab(page);
                break;
            case PanelType.BeelzAdminConfigTab:
                BuildBeelzAdminConfigTab(page);
                break;
            case PanelType.BeelzAdminPlayersTab:
                BuildBeelzAdminPlayersTab(page);
                break;
            case PanelType.BeelzAdminAbilityTableTab:
                BuildBeelzAdminAbilityTableTab(page);
                break;
            case PanelType.UrielStorageTab:
                BuildUrielStorageTab(page);
                break;
            case PanelType.UrielPrisonTab:
                BuildUrielPrisonTab(page);
                break;
            case PanelType.UrielStairsTab:
                BuildUrielStairsTab(page);
                break;
            case PanelType.UrielObjectsTab:
                BuildUrielObjectsTab(page);
                break;
            case PanelType.UrielObjectCatalogTab:
                BuildUrielObjectCatalogTab(page);
                break;
            case PanelType.UrielEventsTab:
                BuildUrielEventsTab(page);
                break;
            case PanelType.UrielEventLibraryTab:
                BuildUrielEventLibraryTab(page);
                break;
            case PanelType.UrielAdminUnitsTab:
                BuildUrielAdminUnitsTab(page);
                break;
            case PanelType.UrielSettingsTab:
                BuildUrielSettingsTab(page);
                break;
            case PanelType.UrielAdminSharingTab:
                BuildUrielAdminSharingTab(page);
                break;
            case PanelType.UrielAdminObjectsTab:
                BuildUrielAdminObjectsTab(page);
                break;
            case PanelType.UrielAdminConfigTab:
                BuildUrielAdminConfigTab(page);
                break;
            case PanelType.UrielQuickStartTab:
            case PanelType.UrielModHelpTab:         RenderGuide(page, Raphael.Services.Guides.Guides.Get("uriel")); break;
            case PanelType.FaustCastleInfoTab:
                BuildFaustCastleInfoTab(page);
                break;
            case PanelType.FaustPlotsTab:
                BuildFaustPlotsTab(page);
                break;
            case PanelType.FaustAllPlotsTab:
                BuildFaustAllPlotsTab(page);
                break;
            case PanelType.FaustDecayWatchTab:
                BuildFaustDecayWatchTab(page);
                break;
            case PanelType.FaustResourcesTab:
                BuildFaustResourcesTab(page);
                break;
            case PanelType.FaustPlayerInfoTab:
                BuildFaustPlayerInfoTab(page);
                break;
            case PanelType.FaustClansTab:
                BuildFaustClansTab(page);
                break;
            case PanelType.FaustPositionsTab:
                BuildFaustPositionsTab(page);
                break;
            case PanelType.FaustStatsTab:
                BuildFaustStatsTab(page);
                break;
            case PanelType.FaustBossesTab:
                BuildFaustBossesTab(page);
                break;
            case PanelType.FaustKillsTab:
                BuildFaustKillsTab(page);
                break;
            case PanelType.FaustWorldScanTab:
                BuildFaustWorldScanTab(page);
                break;
            case PanelType.FaustSettingsTab:
                BuildFaustSettingsTab(page);
                break;
            case PanelType.FaustAdminControlTab:
                BuildFaustAdminControlTab(page);
                break;
            case PanelType.FaustAdminAccessTab:
                BuildFaustAdminAccessTab(page);
                break;
            case PanelType.FaustAdminOversightTab:
                BuildFaustAdminOversightTab(page);
                break;
            case PanelType.NyarEventsTab:          BuildNyarEventsTab(page); break;
            case PanelType.NyarLeaderboardsTab:    BuildNyarLeaderboardsTab(page); break;
            case PanelType.NyarMyStatsTab:         BuildNyarMyStatsTab(page); break;
            case PanelType.NyarSettingsTab:        BuildNyarSettingsTab(page); break;
            case PanelType.NyarAdminEventsTab:     BuildNyarAdminEventsTab(page); break;
            case PanelType.NyarAdminUnitsTab:      BuildNyarAdminUnitsTab(page); break;
            case PanelType.NyarAdminTemplatesTab:  BuildNyarAdminTemplatesTab(page); break;
            case PanelType.NyarAdminPillarsTab:    BuildNyarAdminPillarsTab(page); break;
            case PanelType.NyarAdminZonesTab:      BuildNyarAdminZonesTab(page); break;
            case PanelType.NyarAdminAnnounceTab:   BuildNyarAdminAnnounceTab(page); break;
            case PanelType.NyarAdminKillSwitchTab: BuildNyarAdminKillSwitchTab(page); break;
            case PanelType.NyarGuideTab:           RenderGuide(page, Raphael.Services.Guides.Guides.Get("nyarlathotep")); break;
            case PanelType.FaustQuickStartTab:
            case PanelType.FaustModHelpTab:        RenderGuide(page, Raphael.Services.Guides.Guides.Get("faust")); break;
            default:
                AddComingSoonBody(page, label);
                break;
        }

        pageWrapper.SetActive(false);
        _tabContent[tab] = pageWrapper;
        _tabInnerContent[tab] = page;
        return pageWrapper;
    }

    // Returns the ScrollView wrapper (used for SetActive + as the visible tab
    // GameObject); out-param yields the inner content GameObject where the
    // BuildXxxTab methods append children. AutoResize walks the inner so it
    // sees the true children-sum height, not the viewport.
    private GameObject CreateTabPage(GameObject parent, out GameObject content)
    {
        var wrapper = UIFactory.CreateScrollView(parent, "TabPage",
            out content, out _, color: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(wrapper,
            minWidth: 380, preferredWidth: 420, flexibleWidth: 1,
            minHeight: 280, flexibleHeight: 1);

        // Re-style the auto-created content VerticalLayoutGroup to match the
        // old CreateTabPage layout (spacing 6, padding 8/8/8/8).
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
        {
            vlg.spacing = 6;
            vlg.padding.left  = 8;
            vlg.padding.right = 8;
            vlg.padding.top   = 8;
            vlg.padding.bottom = 8;
            vlg.childControlWidth  = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;
        }
        return wrapper;
    }

    // 0.17: home tab for the standalone "Game UI" enhancement group. Client-side
    // features that work on any server (no Bloodcraft/Kindred). Content fills in
    // as each enhancement lands; today it introduces the section.
    private void BuildGameUITab(GameObject page)
    {
        var card = AddCard(page, "GameUIIntroCard");
        AddSectionHeading(card, "Standalone UI enhancements");
        AddBodyText(card,
            "Client-side improvements to the V Rising interface that work on any " +
            "server — no Bloodcraft, KindredCommands, or KindredLogistics required. " +
            "They live here so the hub stays useful even without the server mods.");
        AddBodyText(card,
            "Planned for this section:\n" +
            "• Tabbed chat — split Global / Local / Clan / System / whisper into " +
            "separate channels in the in-game chat.\n" +
            "• Map info — castle-heart timers and plot size / availability on the map.");

        // 0.17 increment 1: early preview of the tabbed chat window (read-only).
        var chatCard = AddCard(page, "GameUIChatCard");
        AddSectionHeading(chatCard, "Tabbed chat (preview)");
        AddBodyText(chatCard,
            "Mirrors the in-game chat into a movable, persistent window with " +
            "per-channel tabs (All / Global / Local / Clan / System / Whispers). " +
            "Early preview — read-only for now; typing and sending come next.");
        var chatBtn = UIFactory.CreateButton(chatCard, "ToggleTabbedChatBtn", "Open / close tabbed chat window");
        UIFactory.SetLayoutElement(chatBtn.GameObject,
            minWidth: 200, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 30, flexibleHeight: 0);
        chatBtn.OnClick = () =>
        {
            try { Plugin.UIManager?.ToggleOverlay(PanelType.ChatWindowOverlay); }
            catch (System.Exception ex) { Utils.LogUtils.LogError($"Toggle tabbed chat failed: {ex}"); }
        };
        TooltipHover.Attach(chatBtn.GameObject,
            "Show or hide the standalone tabbed chat window. Early preview (read-only); input + sending arrive in a later update.");
        AddBodyText(chatCard,
            "Tip: if the window won't move or resize, turn off \"Lock overlays\" " +
            "(main panel footer, beside Auto-resize) — it locks every overlay.");

        // Per-window customization (re-renders the live window immediately).
        AddChatOptionToggle(chatCard, "Show timestamps",
            Config.Settings.ChatShowTimestamps,
            v => Config.Settings.SetChatShowTimestamps(v));
        AddChatOptionToggle(chatCard, "Show channel labels",
            Config.Settings.ChatShowChannelTags,
            v => Config.Settings.SetChatShowChannelTags(v));
        AddChatOptionToggle(chatCard, "Replace the game's chat (hide it; use this window)",
            Config.Settings.HideNativeChat,
            v => { Config.Settings.SetHideNativeChat(v); Plugin.UIManager?.ApplyNativeChatVisibility(); });
        AddChatOptionToggle(chatCard, "Stronger hide if the game chat still flickers (experimental)",
            Config.Settings.AggressiveNativeChatHide,
            v => { Config.Settings.SetAggressiveNativeChatHide(v); Plugin.UIManager?.ApplyNativeChatVisibility(); });
        AddChatOptionToggle(chatCard, "On the All tab, send to Global by default (off = Local)",
            Config.Settings.ChatAllTabDefaultGlobal,
            v => Config.Settings.SetChatAllTabDefaultGlobal(v));

        // 0.65.3: idle auto-hide — fade the chat window out after inactivity, like the native chat.
        // Off by default; the wake triggers below are configurable so it can't blink from Raphael's
        // own background Bloodcraft pings (System channel is opt-in as a wake source).
        AddChatOptionToggle(chatCard, "Auto-hide the chat window when idle (fades out; opens on activity)",
            Config.Settings.ChatAutoHideEnabled,
            v => Config.Settings.SetChatAutoHideEnabled(v));
        AddChatSecondsRow(chatCard, "    └ Hide after",
            () => Config.Settings.ChatAutoHideSeconds,
            v => Config.Settings.SetChatAutoHideSeconds(v), min: 3, max: 120);
        AddChatOptionToggle(chatCard, "    └ A new player message wakes it (Global / Local / Clan / Whisper)",
            Config.Settings.ChatAutoHideWakeOnMessage,
            v => Config.Settings.SetChatAutoHideWakeOnMessage(v));
        AddChatOptionToggle(chatCard, "    └ A System line wakes it (server broadcasts + Bloodcraft pings — may blink)",
            Config.Settings.ChatAutoHideWakeOnSystem,
            v => Config.Settings.SetChatAutoHideWakeOnSystem(v));

        // 0.17.0: chat-only text size — independent of the "Overlay text size"
        // row in Display settings (which previously also resized this window).
        // Reuses the segmented Small/Standard/Large/X-Large control; refreshes
        // the live chat window so the size change shows immediately.
        AddTextScaleRow(chatCard, "Chat text size",
            currentScaleSetting: () => Config.Settings.ChatTextScale,
            setScale: v => Config.Settings.SetChatTextScale(v),
            rebuild: () => Plugin.UIManager?.RefreshChatWindowOverlay());
        // ── Message display & format (own card) ────────────────────────────
        var chatFmtCard = AddCard(page, "GameUIChatFmtCard");
        AddSectionHeading(chatFmtCard, "Message display & format");
        AddChatOptionToggle(chatFmtCard, "Newest message at the bottom (off = top)",
            Config.Settings.ChatNewestAtBottom,
            v => Config.Settings.SetChatNewestAtBottom(v));
        AddChatOptionToggle(chatFmtCard, "Auto-scroll to the newest message",
            Config.Settings.ChatAutoScroll,
            v => Config.Settings.SetChatAutoScroll(v));
        // 0.50 r11: scrolling aids — clickable arrows (default on) + opt-in keyboard scrolling. Arrow keys are
        // OFF by default because they commonly clash with gameplay binds; PageUp/PageDown are on by default.
        AddChatOptionToggle(chatFmtCard, "Scroll arrows (↑ / ↓) on the chat scrollbar",
            Config.Settings.ChatScrollArrowButtons,
            v => Config.Settings.SetChatScrollArrowButtons(v));
        AddChatOptionToggle(chatFmtCard, "Scroll the chat with PageUp / PageDown keys",
            Config.Settings.ChatScrollPageKeys,
            v => Config.Settings.SetChatScrollPageKeys(v));
        AddChatOptionToggle(chatFmtCard, "Scroll the chat with Up / Down arrow keys (may clash with gameplay keys)",
            Config.Settings.ChatScrollArrowKeys,
            v => Config.Settings.SetChatScrollArrowKeys(v));
        AddChatOptionToggle(chatFmtCard, "Spell out channel labels ([Global] instead of [G])",
            Config.Settings.ChatChannelLabelsSpelledOut,
            v => Config.Settings.SetChatChannelLabelsSpelledOut(v));
        AddChatOptionToggle(chatFmtCard, "Color tabs by channel",
            Config.Settings.ChatColorTabs,
            v => Config.Settings.SetChatColorTabs(v));
        AddChatOptionToggle(chatFmtCard, "Tabular layout (align time / channel+sender / message in columns)",
            Config.Settings.ChatTabularLayout,
            v => Config.Settings.SetChatTabularLayout(v));
        AddChatOptionToggle(chatFmtCard, "    └ Separate channel and name into their own columns",
            Config.Settings.ChatTabularSeparateChannelName,
            v => Config.Settings.SetChatTabularSeparateChannelName(v));
        AddChatOptionToggle(chatFmtCard, "    └ Auto-fit name column (off = fixed width; message column always grows first)",
            Config.Settings.ChatTabularAutoFitColumns,
            v => Config.Settings.SetChatTabularAutoFitColumns(v));
        AddChatOptionToggle(chatFmtCard, "Double-click a name in chat to whisper them",
            Config.Settings.ChatDoubleClickNameWhisper,
            v => Config.Settings.SetChatDoubleClickNameWhisper(v));
        AddChatOptionToggle(chatFmtCard, "On whispers you send, show who you sent it to",
            Config.Settings.ChatShowWhisperRecipient,
            v => Config.Settings.SetChatShowWhisperRecipient(v));
        AddChatOptionToggle(chatFmtCard, "    └ Recipient in the channel column ([Whisper → Name]); off = name column (→ Name)",
            Config.Settings.ChatWhisperRecipientInChannelColumn,
            v => Config.Settings.SetChatWhisperRecipientInChannelColumn(v));
        AddChatOptionToggle(chatFmtCard, "Show a whisper to yourself as \"Note to self\" (off = whisper to your own name)",
            Config.Settings.ChatSelfWhisperAsNoteToSelf,
            v => Config.Settings.SetChatSelfWhisperAsNoteToSelf(v));
        AddChatOptionToggle(chatFmtCard, "Show \"missing class / expertise / legacy — free power\" hints (Class, Weapon, Blood pages + overlays)",
            Config.Settings.ShowMissingElementHints,
            v => Config.Settings.SetShowMissingElementHints(v));

        // ── Extended chat retention (own card) ─────────────────────────────
        // The standalone window is the only DURABLE chat history (the server drops old lines fast). On
        // servers whose mods flood System chat, retaining everything has a cost — so this lets the player
        // turn the durable buffer off entirely, or keep only the channels they care about. All default ON,
        // so the out-of-box behavior is unchanged. The gate lives in ChatRelayService.ShouldCapture.
        var retentionCard = AddCard(page, "GameUIChatRetentionCard");
        AddSectionHeading(retentionCard, "Extended chat retention (performance)");
        AddBodyText(retentionCard,
            "This window keeps chat after the game would normally drop it. Retaining a lot of chat — " +
            "especially on servers whose mods flood SYSTEM messages — has a small cost, so use these to keep " +
            "only what you want. A channel that's off still shows transiently in the game's own chat; it just " +
            "isn't retained here. (Takes effect for new messages; already-kept lines stay until they age out.)");
        AddChatOptionToggle(retentionCard, "Keep an extended chat history (master switch — off = retain nothing)",
            Config.Settings.ExtendedChatCaptureEnabled,
            v => Config.Settings.SetExtendedChatCaptureEnabled(v));
        AddBodyText(retentionCard, "<color=#9FD0FF>Channels to keep in the extended history:</color>");
        AddChatOptionToggle(retentionCard, "    └ Global",
            Config.Settings.ExtendedCaptureGlobal,  v => Config.Settings.SetExtendedCaptureGlobal(v));
        AddChatOptionToggle(retentionCard, "    └ Local",
            Config.Settings.ExtendedCaptureLocal,   v => Config.Settings.SetExtendedCaptureLocal(v));
        AddChatOptionToggle(retentionCard, "    └ Clan",
            Config.Settings.ExtendedCaptureClan,    v => Config.Settings.SetExtendedCaptureClan(v));
        AddChatOptionToggle(retentionCard, "    └ System / server",
            Config.Settings.ExtendedCaptureSystem,  v => Config.Settings.SetExtendedCaptureSystem(v));
        AddChatOptionToggle(retentionCard, "    └ Whispers (includes notes to self)",
            Config.Settings.ExtendedCaptureWhisper, v => Config.Settings.SetExtendedCaptureWhisper(v));
        AddChatOptionToggle(retentionCard, "    └ Other / uncategorized",
            Config.Settings.ExtendedCaptureOther,   v => Config.Settings.SetExtendedCaptureOther(v));

        // ── Channels shown in the All tab (own card) ───────────────────────
        // Unchecking a channel hides it from the All tab only — its own dedicated tab still shows it.
        var allTabCard = AddCard(page, "GameUIChatAllTabCard");
        AddSectionHeading(allTabCard, "Channels shown in the All tab");
        AddChatOptionToggle(allTabCard, "Global",   Config.Settings.AllTabShowGlobal,  v => Config.Settings.SetAllTabShowGlobal(v));
        AddChatOptionToggle(allTabCard, "Local",    Config.Settings.AllTabShowLocal,   v => Config.Settings.SetAllTabShowLocal(v));
        AddChatOptionToggle(allTabCard, "Clan",     Config.Settings.AllTabShowClan,    v => Config.Settings.SetAllTabShowClan(v));
        AddChatOptionToggle(allTabCard, "System",   Config.Settings.AllTabShowSystem,  v => Config.Settings.SetAllTabShowSystem(v));
        AddChatOptionToggle(allTabCard, "Whispers", Config.Settings.AllTabShowWhisper, v => Config.Settings.SetAllTabShowWhisper(v));
        AddChatOptionToggle(allTabCard, "Exclude notes to self from All (keep them only in the secondary window)",
            Config.Settings.AllTabExcludeNotesToSelf, v => Config.Settings.SetAllTabExcludeNotesToSelf(v));

        // ── Secondary chat window (own card) ───────────────────────────────
        // A SECOND, view-only window that mirrors only the channels ticked below — watch two streams at once.
        var secondaryCard = AddCard(page, "GameUIChatSecondaryCard");
        AddSectionHeading(secondaryCard, "Secondary chat window (view-only)");
        AddBodyText(secondaryCard,
            "Open a second, draggable DISPLAY-ONLY window (no input box) that shows ONLY the channels you tick " +
            "below — handy for admins watching System / Clan apart from Global. Move/resize it like any overlay " +
            "(turn off \"Lock overlays\" first). It shares the chat window's text size, colors, and transparency.");
        var secondaryChatBtn = UIFactory.CreateButton(secondaryCard, "ToggleSecondaryChatBtn", "Open / close secondary chat window");
        UIFactory.SetLayoutElement(secondaryChatBtn.GameObject,
            minWidth: 200, preferredWidth: 280, flexibleWidth: 1, minHeight: 30, preferredHeight: 30, flexibleHeight: 0);
        secondaryChatBtn.OnClick = () =>
        {
            try { Plugin.UIManager?.ToggleOverlay(PanelType.SecondaryChatOverlay); }
            catch (System.Exception ex) { Utils.LogUtils.LogError($"Toggle secondary chat failed: {ex}"); }
        };
        TooltipHover.Attach(secondaryChatBtn.GameObject, "Show or hide the view-only secondary chat window.");
        AddBodyText(secondaryCard, "<color=#9FD0FF>Channels shown in the secondary window:</color>");
        void SecondaryChan(string label, bool val, System.Action<bool> set) =>
            AddChatOptionToggle(secondaryCard, label, val, v => { set(v); Plugin.UIManager?.RefreshSecondaryChatOverlay(); });
        SecondaryChan("Global",   Config.Settings.SecondaryChatShowGlobal,  Config.Settings.SetSecondaryChatShowGlobal);
        SecondaryChan("Local",    Config.Settings.SecondaryChatShowLocal,   Config.Settings.SetSecondaryChatShowLocal);
        SecondaryChan("Clan",     Config.Settings.SecondaryChatShowClan,    Config.Settings.SetSecondaryChatShowClan);
        SecondaryChan("System",   Config.Settings.SecondaryChatShowSystem,  Config.Settings.SetSecondaryChatShowSystem);
        SecondaryChan("Whispers", Config.Settings.SecondaryChatShowWhisper, Config.Settings.SetSecondaryChatShowWhisper);
        SecondaryChan("Notes to self (only — whispers to your own character, separate from all whispers)",
            Config.Settings.SecondaryChatShowNotesToSelf, Config.Settings.SetSecondaryChatShowNotesToSelf);

        // ── Tab-switch hotkeys (own card) ──────────────────────────────────
        // <Modifier>+1..6 selects a tab while the chat window is open and you're not typing (1=All … 6=Whispers).
        var hotkeyCard = AddCard(page, "GameUIChatHotkeyCard");
        AddSectionHeading(hotkeyCard, "Tab-switch hotkeys");
        AddChatOptionToggle(hotkeyCard, "Switch chat tabs with hotkeys (Modifier + 1-6, while chat is open & not typing)",
            Config.Settings.ChatTabHotkeysEnabled,
            v => Config.Settings.SetChatTabHotkeysEnabled(v));
        var modRow = UIFactory.CreateHorizontalGroup(hotkeyCard, "ChatTabHotkeyModRow",
            true, false, true, true, 6, new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(modRow,
            minWidth: 200, preferredWidth: 280, flexibleWidth: 1, minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        var modLabel = UIFactory.CreateLabel(modRow, "ChatTabModLabel",
            $"Modifier: {Config.Settings.ChatTabHotkeyModifier}", TextAlignmentOptions.MidlineLeft,
            color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(modLabel.GameObject,
            minWidth: 96, preferredWidth: 110, flexibleWidth: 0, minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        foreach (var m in new[] { "Shift", "Ctrl", "Alt", "None" })
        {
            var mm = m;
            var b = UIFactory.CreateButton(modRow, $"ChatTabMod_{mm}", mm);
            UIFactory.SetLayoutElement(b.GameObject,
                minWidth: 44, preferredWidth: 52, flexibleWidth: 1, minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
            var tt = b.Component.GetComponentInChildren<TextMeshProUGUI>(); if (tt != null) tt.fontSize = Theme.ScaledUI(11);
            b.OnClick = () =>
            {
                Config.Settings.SetChatTabHotkeyModifier(mm);
                if (modLabel?.TextMesh != null) modLabel.TextMesh.text = $"Modifier: {mm}";
            };
            TooltipHover.Attach(b.GameObject, $"Use {mm} + number keys 1-6 to switch chat tabs (1=All … 6=Whispers). The key isn't consumed, so pick a modifier that doesn't clash.");
        }

        // ── Channel colors (own card) ──────────────────────────────────────
        // Each picker sets the color used for BOTH that channel's message text AND its tab (when "Color tabs by
        // channel" is on). All five are user-settable + persisted.
        var colorCard = AddCard(page, "GameUIChatColorCard");
        AddSectionHeading(colorCard, "Channel colors");
        AddChatChannelColorRow(colorCard, "Global channel",  "Global",  Config.Settings.DEFAULT_CHAT_GLOBAL_HEX,  ApplyChatGlobalColorHex);
        AddChatChannelColorRow(colorCard, "Local channel",   "Local",   Config.Settings.DEFAULT_CHAT_LOCAL_HEX,   ApplyChatLocalColorHex);
        AddChatChannelColorRow(colorCard, "Clan channel",    "Clan",    Config.Settings.DEFAULT_CHAT_CLAN_HEX,    ApplyChatClanColorHex);
        AddChatChannelColorRow(colorCard, "System messages", "System",  Config.Settings.DEFAULT_CHAT_SYSTEM_HEX,  ApplyChatSystemColorHex);
        AddChatChannelColorRow(colorCard, "Whispers",        "Whisper", Config.Settings.DEFAULT_CHAT_WHISPER_HEX, ApplyChatWhisperColorHex);
        // Extend the channel color to the message BODY (not just the [tag]/tab), and a distinct color for YOUR OWN messages.
        AddChatOptionToggle(colorCard, "Color message text by channel color",
            Config.Settings.ChatColorMessageByChannel, Config.Settings.SetChatColorMessageByChannel);
        AddChatOptionToggle(colorCard, "Highlight my own messages in a custom color",
            Config.Settings.ChatColorOwnMessages, Config.Settings.SetChatColorOwnMessages);
        AddChatChannelColorRow(colorCard, "My messages", "Own", Config.Settings.DEFAULT_CHAT_OWN_HEX, ApplyChatOwnColorHex);

        // ── Chat window background (own card) ──────────────────────────────
        // Transparency + theme color, independent of the other overlays / the main panel.
        var bgCard = AddCard(page, "GameUIChatBgCard");
        AddSectionHeading(bgCard, "Chat window background");
        AddTransparencyRow(bgCard, "Transparency",
            () => Config.Settings.ChatWindowOverlayTransparency,
            v => Config.Settings.SetChatWindowOverlayTransparency(v));
        AddPanelColorPresetRow(bgCard, "ChatBgPresetRow", ApplyChatWindowBgHex);
    }

    // 0.17.0: persist the chat window's own background color + live-refresh it.
    private void ApplyChatWindowBgHex(string hex)
    {
        Config.Settings.SetChatWindowBackgroundColorHex(hex);
        Plugin.UIManager?.RefreshChatWindowBackground();
    }

    // 0.21: one labeled row of color-preset swatches for a chat channel (heading + swatches). The shared
    // palette leads with the channel's own default so "reset to default" is one click. Each swatch persists
    // via `apply` and live-refreshes the chat window (tabs + message text recolor together).
    private void AddChatChannelColorRow(GameObject parent, string heading, string rowKey, string defaultHex, System.Action<string> apply)
    {
        AddSectionHeading(parent, heading);
        var row = UIFactory.CreateHorizontalGroup(parent, $"Chat{rowKey}ColorRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 200, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        foreach (var preset in new[] {
            ("Default", defaultHex), ("White", "#FFFFFF"), ("Coral", "#FF8A5B"), ("Amber", "#FFD479"),
            ("Green", "#90EE90"), ("Cyan", "#66CCFF"), ("Blue", "#B0E0FF"), ("Pink", "#FF9CEF"), ("Violet", "#C9A0FF") })
            AddPanelBgPresetButton(row, preset.Item1, preset.Item2, apply);
    }

    // 0.17.0 / 0.21: persist a channel color + live-refresh the chat window so the label tags and colored
    // tab update immediately.
    private void ApplyChatGlobalColorHex(string hex)  { Config.Settings.SetChatGlobalColorHex(hex);  Plugin.UIManager?.RefreshChatWindowOverlay(); }
    private void ApplyChatLocalColorHex(string hex)   { Config.Settings.SetChatLocalColorHex(hex);   Plugin.UIManager?.RefreshChatWindowOverlay(); }
    private void ApplyChatClanColorHex(string hex)    { Config.Settings.SetChatClanColorHex(hex);    Plugin.UIManager?.RefreshChatWindowOverlay(); }
    private void ApplyChatSystemColorHex(string hex)  { Config.Settings.SetChatSystemColorHex(hex);  Plugin.UIManager?.RefreshChatWindowOverlay(); }
    private void ApplyChatWhisperColorHex(string hex) { Config.Settings.SetChatWhisperColorHex(hex); Plugin.UIManager?.RefreshChatWindowOverlay(); }
    private void ApplyChatOwnColorHex(string hex)     { Config.Settings.SetChatOwnMessageColorHex(hex); Plugin.UIManager?.RefreshChatWindowOverlay(); }

    // 0.17: small labeled toggle for the Game UI chat-window options. Persists
    // via the supplied setter, then re-renders the live chat overlay so the
    // change is visible immediately.
    private void AddChatOptionToggle(GameObject parent, string label, bool initial, System.Action<bool> setter)
    {
        var t = UIFactory.CreateToggle(parent, label + "Toggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 200, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(13);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        t.Toggle.isOn = initial;
        t.OnValueChanged += v =>
        {
            setter(v);
            Plugin.UIManager?.RefreshChatWindowOverlay();
        };
    }

    // 0.65.3: a labeled whole-seconds slider + typed box (min..max), for the chat auto-hide delay.
    // Modeled on AddTransparencyRow's slider/box mirroring; applies immediately (no live refresh needed
    // — the value is read each frame by the auto-hide ticker).
    private void AddChatSecondsRow(GameObject parent, string label,
                                   System.Func<float> currentValue,
                                   System.Action<float> applyValue, int min, int max)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"ChatSecRow_{label}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var lbl = UIFactory.CreateLabel(row, $"ChatSecLbl_{label}", $"{label}:",
            TMPro.TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: Theme.ScaledWidth(150), preferredWidth: Theme.ScaledWidth(170), flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        int start = Mathf.Clamp(Mathf.RoundToInt(currentValue()), min, max);

        var sliderGo = UIFactory.CreateSlider(row, $"ChatSecSlider_{label}", out var slider);
        UIFactory.SetLayoutElement(sliderGo,
            minWidth: 100, preferredWidth: 160, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = true; slider.value = start;

        var gap = UIFactory.CreateUIObject($"ChatSecGap_{label}", row);
        UIFactory.SetLayoutElement(gap,
            minWidth: 12, preferredWidth: 12, flexibleWidth: 0, minHeight: 10, preferredHeight: 10, flexibleHeight: 0);

        var box = UIFactory.CreateInputField(row, $"ChatSecInput_{label}", start.ToString());
        UIFactory.SetLayoutElement(box.GameObject,
            minWidth: 44, preferredWidth: 48, flexibleWidth: 0, minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        box.Component.contentType = TMPro.TMP_InputField.ContentType.IntegerNumber;
        box.Component.characterLimit = 3;
        box.Text = start.ToString();

        var unit = UIFactory.CreateLabel(row, $"ChatSecUnit_{label}", "s",
            TMPro.TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(unit.GameObject,
            minWidth: Theme.ScaledWidth(14), preferredWidth: Theme.ScaledWidth(16), flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        bool syncing = false;
        slider.onValueChanged.AddListener((System.Action<float>)(v =>
        {
            if (syncing) return; syncing = true;
            int s = Mathf.Clamp(Mathf.RoundToInt(v), min, max);
            box.Text = s.ToString(); applyValue(s);
            syncing = false;
        }));
        box.Component.onEndEdit.AddListener((System.Action<string>)(str =>
        {
            if (syncing) return; syncing = true;
            if (!int.TryParse(str, out int s)) s = Mathf.RoundToInt(currentValue());
            s = Mathf.Clamp(s, min, max);
            box.Text = s.ToString(); slider.value = s; applyValue(s);
            syncing = false;
        }));
    }

    private static void AddTabHeading(GameObject page, string text)
    {
        var heading = UIFactory.CreateLabel(page, "TabHeading", text,
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(20));
        UIFactory.SetLayoutElement(heading.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        heading.TextMesh.fontStyle = FontStyles.Bold;
        heading.TextMesh.enableWordWrapping = false;
        heading.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    private static void AddComingSoonBody(GameObject page, string label)
    {
        var placeholder = UIFactory.CreateLabel(page, "Placeholder",
            "Coming soon — this tab will surface the matching Bloodcraft commands.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(14));
        UIFactory.SetLayoutElement(placeholder.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 80, flexibleHeight: 0);
        placeholder.TextMesh.enableWordWrapping = true;
        placeholder.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    // -----------------------------------------------------------------------
    // Familiars tab
    // -----------------------------------------------------------------------

    private void BuildFamiliarsTab(GameObject page)
    {
        // 0.10.9: card-wrapped sections. Pre-0.10.9 this was the densest
        // tab — 8 action buttons, an emote-binding wall, and 6 collapsible
        // forms all stacked on the same panel background. Cards give the
        // four conceptual zones (current state / quick actions / emotes
        // reference / more actions / battle groups) explicit grouping.

        // ── Active Familiar ─────────────────────────────────────────────
        var activeCard = AddCard(page, "FamActiveCard", Theme.SystemTintFamiliar);
        AddSectionHeading(activeCard, "★  Active Familiar");
        _famNameLabel     = AddInfoLabel(activeCard, "FamName",     "—", FontStyles.Bold,   fontSize: Theme.ScaledUI(18));
        _famProgressLabel = AddInfoLabel(activeCard, "FamProgress", "Level — ", FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _famStatsLabel    = AddInfoLabel(activeCard, "FamStats",    "HP —  PP —  SP —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        AddSpacer(page, 6);

        // ── Quick actions ───────────────────────────────────────────────
        var actionsCard = AddCard(page, "FamActionsCard");
        AddSectionHeading(actionsCard, "Actions");
        var row1 = UIFactory.CreateHorizontalGroup(actionsCard, "FamActionsRow1",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row1,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(row1, "Recall / Dismiss", MessageService.BCCOM_FAM_TOGGLE,
            "Calls or dismisses your active familiar (.fam t). Recallable — does NOT destroy the familiar.");
        AddCommandButton(row1, "Toggle Combat", MessageService.BCCOM_FAM_COMBAT,
            "Toggle combat mode for the active familiar (.fam c). Off = passive, won't engage enemies.");
        AddCommandButton(row1, "Toggle Emotes", MessageService.BCCOM_FAM_TOGGLE_EMOTES,
            "Enable/disable emote-action bindings (.fam e). When OFF, emoting (e.g. clap) won't open the familiar's inventory or trigger other actions.");
        AddCommandButton(row1, "List Emotes", MessageService.BCCOM_FAM_LIST_EMOTES,
            "List the current emote→action bindings (.fam actions). Tells you which emote does what (e.g. clap = open inventory).");

        var row2 = UIFactory.CreateHorizontalGroup(actionsCard, "FamActionsRow2",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row2,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(row2, "★  Prestige", MessageService.BCCOM_FAM_PRESTIGE,
            "Prestige the active familiar (.fam pr). Requires max level; resets level and grants permanent bonuses.");
        AddCommandButton(row2, "Unbind", MessageService.BCCOM_FAM_UNBIND,
            "Unbind the active familiar (.fam ub). The in-world entity is released but the familiar STAYS in your box — you can re-bind it from the Boxes tab any time. Use this to free the bind slot so you can summon a different familiar. To permanently delete a familiar from your collection, use the Boxes tab → Permanently Delete form.");

        AddDivider(actionsCard);
        AddBodyText(actionsCard, "Switch to the Boxes tab to browse your familiar boxes and click-to-bind.");

        AddSpacer(page, 6);

        // ── Emote bindings reference ────────────────────────────────────
        var emoteCard = AddCard(page, "FamEmoteCard");
        AddSectionHeading(emoteCard, "Emote Bindings (perform these in-world)");
        AddBodyText(emoteCard,
            "Bloodcraft binds these emotes to familiar actions. Trigger by performing the emote in-world (e.g. /clap), NOT via this UI — there's no chat command to invoke an emote programmatically. Toggle Emotes (above) enables/disables the whole system.");
        var emoteRef = UIFactory.CreateLabel(emoteCard, "FamEmoteRef",
            "  • Wave   →  Recall / Dismiss\n" +
            "  • Salute →  Toggle Combat Mode\n" +
            "  • Clap   →  Bind / Unbind active familiar\n" +
            "  • Beckon →  Interact (opens familiar's inventory, equipment, name & settings)",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(emoteRef.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 80, preferredHeight: 96, flexibleHeight: 0);
        emoteRef.TextMesh.enableWordWrapping = true;
        emoteRef.TextMesh.overflowMode = TextOverflowModes.Overflow;

        AddSpacer(page, 6);

        // ── More familiar actions (collapsibles) ────────────────────────
        var moreActionsCard = AddCard(page, "FamMoreActionsCard");
        AddSectionHeading(moreActionsCard, "More Familiar Actions");

        CollapsibleSection.Build(moreActionsCard,
            title: "Search boxes by name (.fam s)",
            startExpanded: false,
            tooltip: "Search across ALL your boxes for familiars whose name matches the text. Reply appears in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Search familiars",
                commandTemplate: ".fam s \"{name}\"",
                new TextField("name", "Name (substring)", placeholder: "Wolf",
                    tooltip: "Substring of the familiar's display name. Bloodcraft does case-insensitive matching across boxes.")));

        CollapsibleSection.Build(moreActionsCard,
            title: "Smart bind by name (.fam sb)",
            startExpanded: false,
            tooltip: "Search + bind in one step. If multiple matches are found Bloodcraft returns the list for clarification (no destructive action). Will fail if you already have a familiar bound.",
            buildContent: c => FormBuilder.Build(c,
                title: "Smart bind",
                commandTemplate: ".fam sb \"{name}\"",
                new TextField("name", "Name (substring)", placeholder: "Wolf",
                    tooltip: "Substring of the familiar's display name to bind.")));

        CollapsibleSection.Build(moreActionsCard,
            title: "Make active familiar shiny (.fam shiny)",
            startExpanded: false,
            tooltip: "Spends vampiric dust to permanently mark your CURRENT active familiar with a shiny buff of the chosen school. Requires an active familiar bound first.",
            buildContent: c => FormBuilder.Build(c,
                title: "Apply shiny",
                commandTemplate: ".fam shiny {school}",
                new EnumField<PlayerStateService.FamiliarShinySchoolChoice>("school", "Spell school",
                    defaultValue: PlayerStateService.FamiliarShinySchoolChoice.Storm,
                    tooltip: "The shiny element to apply. Each school has a flavour (Storm = stun, Blood = leech, etc.).")));

        CollapsibleSection.Build(moreActionsCard,
            title: "Toggle a familiar setting (.fam option)",
            startExpanded: false,
            tooltip: "Flips one of Bloodcraft's per-player familiar settings. Common settings: 'shiny' (apply shiny visuals), 'vbloodemotes' (familiar plays VBlood emotes). Bloodcraft's reply tells you what's now on/off.",
            buildContent: c => FormBuilder.Build(c,
                title: "Toggle option",
                commandTemplate: ".fam option {setting}",
                new TextField("setting", "Setting name", placeholder: "shiny",
                    tooltip: "Name of the setting to toggle. Server replies with the new state in chat.")));

        CollapsibleSection.Build(moreActionsCard,
            title: "Buy V-Blood echoes (.fam echoes)",
            startExpanded: false,
            tooltip: "Spend V-Blood essence to purchase the exo reward tied to the named V-Blood unit. Cost scales with unit tier.",
            buildContent: c => FormBuilder.Build(c,
                title: "Buy echoes",
                commandTemplate: ".fam echoes \"{vblood}\"",
                new TextField("vblood", "V-Blood name", placeholder: "Quincey the Bandit King",
                    tooltip: "Exact display name of the V-Blood whose echo reward you want.")));

        CollapsibleSection.Build(moreActionsCard,
            title: "Force-unbind stuck familiar (.fam reset)",
            startExpanded: false,
            tooltip: "Cleanup utility for a familiar that won't unbind normally. Clears leftover follower entities and the active-familiar record so you can re-bind from your box. Box records and familiar unlocks are PRESERVED — you can re-summon any familiar with .fam b N after running this. Use only when .fam ub (Unbind) doesn't work; the server-side handler refuses to run if the active familiar entity is still alive in-world.",
            buildContent: c => FormBuilder.Build(c,
                title: "Force-unbind",
                commandTemplate: ".fam reset",
                new BoolField("confirm", "Yes, clear stuck familiar bind",
                    tooltip: "Required. Same effect as Unbind (.fam ub) for handling stuck/bugged familiars. Box records and unlocks are NOT touched — re-bind any familiar from its box after running this.",
                    requireTrue: true)));

        // 0.10.11: in-panel results display for the .fam s search above.
        // Pre-0.10.11 the only place results showed up was chat — when
        // the user had Raphael chat-suppression on (Settings → Chat Logging
        // → Bloodcraft toggle off, or ClearServerMessages), the reply
        // landed nowhere visible. The parser already fires
        // MessageService.FamSearchCompleted for every .fam s reply, so
        // we just need a subscriber that surfaces the result in the UI.
        BuildFamSearchResultPanel(moreActionsCard);

        // 0.14.0: Battle Groups card removed. Bloodcraft v1.1+ never
        // implemented the feature set behind .fam bgs / .fam bg / .fam abg
        // / .fam cbg / .fam sbg / .fam dbg / .fam challenge — the README
        // still documents them but they are no-ops on the server (a
        // Bloodcraft server admin confirmed in chat). Surfacing them in
        // the UI just produced silent failures + log clutter, so the
        // entire BG card is removed. Backing constants in
        // MessageService_Processing.cs and the intercept startsWith
        // branches are removed in the same commit so the dead-feature
        // surface area shrinks to zero.

        RenderFamiliar(PlayerStateService.Familiar);
        if (!_famSubscribed)
        {
            PlayerStateService.FamiliarChanged += OnFamiliarChanged;
            _famSubscribed = true;
        }
        // 0.10.11: subscribe to the parser's FamSearchCompleted event so
        // the in-panel result list updates whenever a .fam s reply lands.
        if (!_famSearchSubscribed)
        {
            MessageService.FamSearchCompleted += OnFamSearchCompletedForFamTab;
            _famSearchSubscribed = true;
        }
    }

    private void OnFamiliarChanged() => RenderFamiliar(PlayerStateService.Familiar);

    /// <summary>0.10.11: build the in-panel result display for the .fam s
    /// search form above. Mounted at the bottom of the More Actions card
    /// so it's visually adjacent to the form that produces its data.</summary>
    private void BuildFamSearchResultPanel(GameObject parent)
    {
        AddDivider(parent);
        AddSectionHeading(parent, "Last search result");

        _famSearchResultHeader = AddInfoLabel(parent, "FamSearchResultHeader",
            "(submit a search above to populate)", FontStyles.Italic, fontSize: Theme.ScaledUI(12));
        _famSearchResultHeader.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f);

        _famSearchResultList = UIFactory.CreateVerticalGroup(parent, "FamSearchResultList",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 6, 4));
        UIFactory.SetLayoutElement(_famSearchResultList,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 0, flexibleHeight: 0);
    }

    /// <summary>0.10.11: render a FamSearchCompleted payload into the
    /// Last-search-result panel. Each matching box renders as its own
    /// row with a shiny indicator when the server attached the pink-star
    /// marker. No-match results show a single italic "no matches" line.
    /// Cheap rebuild: typical reply has 0-5 boxes.</summary>
    private void OnFamSearchCompletedForFamTab(MessageService.FamSearchResult r)
    {
        if (_famSearchResultHeader == null || _famSearchResultList == null) return;

        // Header: "Search: 'name' → N match(es)" or "no matches".
        if (string.IsNullOrEmpty(r.Query))
        {
            _famSearchResultHeader.text = "(submit a search above to populate)";
        }
        else if (!r.HadAnyMatch || r.Boxes == null || r.Boxes.Count == 0)
        {
            _famSearchResultHeader.text = $"Search: \"{r.Query}\"  →  no matches.";
        }
        else
        {
            _famSearchResultHeader.text = $"Search: \"{r.Query}\"  →  {r.Boxes.Count} box{(r.Boxes.Count == 1 ? "" : "es")}.";
        }
        _famSearchResultHeader.color = new UnityEngine.Color(0.9f, 0.9f, 0.9f);

        // Wipe and rebuild the box list.
        for (int i = _famSearchResultList.transform.childCount - 1; i >= 0; --i)
            UnityEngine.Object.Destroy(_famSearchResultList.transform.GetChild(i).gameObject);

        if (r.HadAnyMatch && r.Boxes != null)
        {
            foreach (var b in r.Boxes)
            {
                string suffix = b.HasShiny
                    ? $"   <color=#FFA0F0>★ shiny</color>"
                    : "";
                var line = UIFactory.CreateLabel(_famSearchResultList, "ResultRow",
                    $"<color=#9AC8D9>•</color>  {b.Box}{suffix}",
                    TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
                UIFactory.SetLayoutElement(line.GameObject,
                    minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
                    minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
                line.TextMesh.enableWordWrapping = false;
                line.TextMesh.overflowMode = TextOverflowModes.Overflow;
            }
        }
        AutoResizeIfEnabled();
    }

    // -----------------------------------------------------------------------
    // Boxes tab
    // -----------------------------------------------------------------------

    private void BuildBoxesTab(GameObject page)
    {
        // 0.10.11: wrap the active-box header + tip in a card so the
        // bold "Active Box: …" label isn't flush with the panel border,
        // and the italic Tip body has muted styling that recedes
        // visually next to the bold header.
        var headerCard = AddCard(page, "BoxesHeaderCard", Theme.SystemTintFamiliar);
        _boxesActiveBoxLabel = AddInfoLabel(headerCard, "ActiveBox",
            "Active Box: (none selected)",
            FontStyles.Bold, fontSize: Theme.ScaledUI(14));
        AddBodyText(headerCard,
            "Tip: click Refresh to pull your box list, click a box to see its familiars, click a familiar to bind it. Use ← Back to return.");

        AddSpacer(page, 6);

        // ---------------- Picker section (visible when no box selected) ----------------
        // childControlHeight: true is crucial here - without it the layout group
        // does NOT enforce children's heights, so the action row, section heading,
        // and list container all draw at their default (0) sizeDelta and overlap.
        // flexibleHeight: 0 (was 1) so the section takes only its natural height.
        // The tab-page ScrollView (added in Phase 5j) handles overflow when the
        // box list is long, and AutoResize grows the panel before that point.
        _boxesPickerSection = UIFactory.CreateVerticalGroup(page, "BoxPickerSection",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_boxesPickerSection,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 80, flexibleHeight: 0);

        var pickerActions = UIFactory.CreateHorizontalGroup(_boxesPickerSection, "PickerActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(pickerActions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        var refreshBtn = UIFactory.CreateButton(pickerActions, "Cmd_RefreshBoxes", "Refresh");
        UIFactory.SetLayoutElement(refreshBtn.GameObject,
            minWidth: 70, preferredWidth: 110, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        var refreshText = refreshBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (refreshText != null)
        {
            refreshText.enableWordWrapping = false;
            refreshText.overflowMode = TextOverflowModes.Overflow;
            refreshText.alignment = TextAlignmentOptions.Center;
            refreshText.fontSize = Theme.ScaledUI(13);
        }
        TooltipHover.Attach(refreshBtn.GameObject,
            "Re-fetch your familiar boxes from the server (.fam boxes). The server reply can take a few seconds.");
        refreshBtn.OnClick = () =>
        {
            if (_boxesStatusLabel != null) _boxesStatusLabel.text = "Loading boxes from the server…";
            EnqueueOrWarn(MessageService.BCCOM_FAM_BOXES);
        };

        _boxesStatusLabel = AddInfoLabel(_boxesPickerSection, "BoxesStatus", "",
            FontStyles.Italic, fontSize: Theme.ScaledUI(11));

        AddSectionHeading(_boxesPickerSection, "Available Boxes");
        // 0.9.2: bumped top padding 2→6 so the first row of box buttons has
        // breathing room from the "Available Boxes" heading above it. The
        // heading itself now reserves a larger height (AddSectionHeading
        // 0.9.2 fix) but the row gap was tight too.
        _boxesListContainer = UIFactory.CreateVerticalGroup(_boxesPickerSection, "BoxListContainer",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(2, 2, 6, 2));
        // No fixed preferredHeight - the VerticalLayoutGroup computes from
        // its dynamic children (box buttons), so auto-resize picks up the
        // actual list height after .fam boxes returns.
        UIFactory.SetLayoutElement(_boxesListContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 60, flexibleHeight: 0);

        // ---- Box management (collapsed by default) ----
        AddSpacer(_boxesPickerSection, 4);
        AddSectionHeading(_boxesPickerSection, "Manage Boxes");

        // Each of the box-management forms re-pulls the box list after submit
        // so the picker reflects the new state without a manual Refresh click.
        Action refreshBoxes = () => EnqueueOrWarn(MessageService.BCCOM_FAM_BOXES);

        CollapsibleSection.Build(_boxesPickerSection,
            title: "Create new box (.fam ab)",
            startExpanded: false,
            tooltip: "Adds an empty box you can move familiars into.",
            buildContent: content => FormBuilder.Build(content,
                title: "Create box",
                commandTemplate: ".fam ab {boxName}",
                onSubmitted: refreshBoxes,
                new TextField("boxName", "Box name", placeholder: "MyBox",
                    tooltip: "Name for the new box. Avoid spaces if possible — Bloodcraft can be picky.")));

        CollapsibleSection.Build(_boxesPickerSection,
            title: "Delete empty box (.fam db)",
            startExpanded: false,
            tooltip: "Removes a box. Bloodcraft only allows deleting boxes that are already empty — move familiars out first.",
            buildContent: content => FormBuilder.Build(content,
                title: "Delete empty box",
                commandTemplate: ".fam db {boxName}",
                onSubmitted: refreshBoxes,
                new BoxNameDropdownField("boxName", "Box name",
                    tooltip: "Pick the box to delete. Server will reject if it isn't empty — move familiars out first.")));

        CollapsibleSection.Build(_boxesPickerSection,
            title: "Rename box (.fam rb)",
            startExpanded: false,
            tooltip: "Renames an existing box.",
            buildContent: content => FormBuilder.Build(content,
                title: "Rename box",
                commandTemplate: ".fam rb {current} {newName}",
                onSubmitted: refreshBoxes,
                new BoxNameDropdownField("current", "Current name",
                    tooltip: "Pick the box to rename."),
                new TextField("newName", "New name", placeholder: "NewName",
                    tooltip: "What to rename it to.")));

        // ---------------- Content section (visible when a box is selected) ----------------
        // flexibleHeight: 0 - same reasoning as the picker section above.
        _boxesContentSection = UIFactory.CreateVerticalGroup(page, "BoxContentSection",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_boxesContentSection,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 80, flexibleHeight: 0);

        var contentActions = UIFactory.CreateHorizontalGroup(_boxesContentSection, "ContentActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(contentActions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);

        // "Back" doesn't send a command — wire manually instead of via AddCommandButton.
        var backBtn = UIFactory.CreateButton(contentActions, "BackToBoxes", "← Back");
        UIFactory.SetLayoutElement(backBtn.GameObject,
            minWidth: 70, preferredWidth: 110, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        var backText = backBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (backText != null)
        {
            backText.enableWordWrapping = false;
            backText.overflowMode = TextOverflowModes.Overflow;
            backText.alignment = TextAlignmentOptions.Center;
            backText.fontSize = Theme.ScaledUI(13);
        }
        backBtn.OnClick = OnBackToBoxesClicked;
        TooltipHover.Attach(backBtn.GameObject, "Return to the box list without changing your active box.");

        AddCommandButton(contentActions, "Reload", MessageService.BCCOM_FAM_LIST_CURRENT_BOX,
            "Re-fetch the familiars in the currently-active box (sends .fam l).");

        // Edit mode toggle. When ON, each familiar row sprouts a destructive
        // Delete button (two-click confirm). Off by default so accidental
        // clicks can't trigger destruction.
        var editToggle = UIFactory.CreateToggle(contentActions, "BoxEditMode");
        UIFactory.SetLayoutElement(editToggle.GameObject,
            minWidth: 110, preferredWidth: 120, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        editToggle.Text.text = "Edit mode";
        editToggle.Text.fontSize = Theme.ScaledUI(12);
        editToggle.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(editToggle.Text.gameObject,
            minWidth: 80, preferredWidth: 90, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        editToggle.Toggle.isOn = _boxesEditMode;
        TooltipHover.Attach(editToggle.GameObject,
            "When ON, each familiar row gets a Delete button (red, two-click confirm) for permanent removal. Move stays as the form below — Bloodcraft's .fam mb only acts on the bound familiar, so the Move workflow is multi-step.");
        editToggle.OnValueChanged += value =>
        {
            _boxesEditMode = value;
            ClearPendingDelete();
            RenderBoxContents();
            AutoResizeIfEnabled();
        };
        _boxesEditModeToggle = editToggle.Toggle;

        _boxesContentHeading = AddInfoLabel(_boxesContentSection, "ContentHeading",
            "Familiars in (none)", FontStyles.Italic, fontSize: Theme.ScaledUI(13));

        // Stays in the layout always (text empty when idle) so showing/hiding
        // the swap-confirm warning doesn't shift the familiar list and yank
        // your click target out from under your cursor. ~38px reserves room
        // for ~2 lines of wrapped text at fontSize 12.
        _boxesSwapWarning = UIFactory.CreateLabel(_boxesContentSection, "SwapWarning",
            "", TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(_boxesSwapWarning.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(38), preferredHeight: Theme.ScaledHeight(38), flexibleHeight: 0);
        _boxesSwapWarning.TextMesh.color = new Color(1f, 0.65f, 0.45f); // warm orange
        _boxesSwapWarning.TextMesh.enableWordWrapping = true;
        _boxesSwapWarning.TextMesh.overflowMode = TextOverflowModes.Overflow;

        _boxesContentContainer = UIFactory.CreateVerticalGroup(_boxesContentSection, "BoxContentContainer",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(2, 2, 2, 2));
        // No fixed preferredHeight - auto-derives from the dynamic familiar
        // button rows so auto-resize grows the panel for tall content.
        UIFactory.SetLayoutElement(_boxesContentContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(60), flexibleHeight: 0);

        // ---- Edit-familiar forms (visible only when viewing a box's contents) ----
        AddSpacer(_boxesContentSection, 6);
        AddSectionHeading(_boxesContentSection, "Edit Familiar (advanced)");

        CollapsibleSection.Build(_boxesContentSection,
            title: "Move active familiar to box (.fam mb)",
            startExpanded: false,
            tooltip: "Moves your CURRENTLY-BOUND familiar to the named box. You must bind a familiar first (click it in the list above). The active familiar stays bound after the move; use Unbind on the Familiars tab to release it.",
            buildContent: c => FormBuilder.Build(c,
                title: "Move active familiar",
                commandTemplate: ".fam mb {boxName}",
                onSubmitted: () =>
                {
                    // Re-pull the source box's familiar list so the moved
                    // familiar disappears from the visible list immediately.
                    EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
                },
                new BoxNameDropdownField("boxName", "Destination box",
                    tooltip: "Pick the destination box. Must already exist; create one with the box management form on the picker view.")));

        CollapsibleSection.Build(_boxesContentSection,
            title: "Permanently delete familiar from box (.fam r)",
            startExpanded: false,
            tooltip: "DESTRUCTIVE — permanently removes a familiar from your collection. The level/prestige/shiny are gone forever; only re-unlockable via gameplay drop. Two-click the Submit button (and check the box) so you don't fire it by accident.",
            buildContent: c => FormBuilder.Build(c,
                title: "Permanently delete familiar",
                commandTemplate: ".fam r {index}",
                onSubmitted: () =>
                {
                    // Re-pull the box's familiar list after a delete so the UI
                    // reflects the new contents without the user having to
                    // hit Reload manually. The intercept timeout (~600ms)
                    // covers any server-side delay between commands.
                    EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
                },
                new IntField("index", "Familiar index", min: 1, max: 999,
                    tooltip: "The 1-based index from the list above (the leading '01', '02', etc.)."),
                new BoolField("confirm", "Yes, permanently delete",
                    tooltip: "Required. Check this box to confirm the deletion is intentional.",
                    requireTrue: true)));

        UpdateBoxesSectionVisibility();
        RenderBoxList();
        RenderBoxContents();

        if (!_boxesSubscribed)
        {
            PlayerStateService.BoxListChanged     += OnBoxListChanged;
            PlayerStateService.BoxContentsChanged += OnBoxContentsChanged;
            PlayerStateService.ActiveBoxChanged   += OnActiveBoxChanged;
            _boxesSubscribed = true;
        }
    }

    private void UpdateBoxesSectionVisibility()
    {
        if (_boxesPickerSection != null)  _boxesPickerSection.SetActive(!_boxesShowingContents);
        if (_boxesContentSection != null) _boxesContentSection.SetActive(_boxesShowingContents);
    }

    private void OnBackToBoxesClicked()
    {
        ClearPendingSwap();
        ClearPendingDelete();
        _boxesShowingContents = false;
        PlayerStateService.SetActiveBox(null);
        UpdateBoxesSectionVisibility();
    }

    private void OnBoxListChanged()
    {
        if (_boxesStatusLabel != null) _boxesStatusLabel.text = "";
        RenderBoxList();
        AutoResizeIfEnabled();
    }
    private void OnBoxContentsChanged() { RenderBoxContents(); AutoResizeIfEnabled(); }
    private void OnActiveBoxChanged()
    {
        var name = PlayerStateService.ActiveBox;
        if (_boxesActiveBoxLabel != null)
            _boxesActiveBoxLabel.text = string.IsNullOrEmpty(name)
                ? "Active Box: (none selected)" : $"Active Box: {name}";
        if (_boxesContentHeading != null)
            _boxesContentHeading.text = $"Familiars in {(string.IsNullOrEmpty(name) ? "(none)" : name)}";
        RenderBoxContents();
    }

    private void RenderBoxList()
    {
        if (_boxesListContainer == null) return;
        ClearChildren(_boxesListContainer);

        var boxes = PlayerStateService.BoxList;
        if (boxes.Count == 0)
        {
            var empty = UIFactory.CreateLabel(_boxesListContainer, "BoxesEmpty",
                "(no boxes loaded yet — click Refresh Boxes)",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
            UIFactory.SetLayoutElement(empty.GameObject,
                minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
            empty.TextMesh.fontStyle = FontStyles.Italic;
            return;
        }

        foreach (var name in boxes)
        {
            var captured = name;
            var b = UIFactory.CreateButton(_boxesListContainer, $"BoxBtn_{name}", name);
            UIFactory.SetLayoutElement(b.GameObject,
                minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
            b.OnClick = () => OnBoxClicked(captured);
        }
    }

    private void RenderBoxContents()
    {
        if (_boxesContentContainer == null) return;
        ClearChildren(_boxesContentContainer);

        var active = PlayerStateService.ActiveBox;
        if (string.IsNullOrEmpty(active))
        {
            var empty = UIFactory.CreateLabel(_boxesContentContainer, "ContentEmpty",
                "(click a box above to load its familiars)",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
            UIFactory.SetLayoutElement(empty.GameObject,
                minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
            empty.TextMesh.fontStyle = FontStyles.Italic;
            return;
        }

        if (!PlayerStateService.BoxContents.TryGetValue(active, out var entries) || entries.Count == 0)
        {
            var pending = UIFactory.CreateLabel(_boxesContentContainer, "ContentPending",
                $"Loading familiars for {active}…",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
            UIFactory.SetLayoutElement(pending.GameObject,
                minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
            pending.TextMesh.fontStyle = FontStyles.Italic;
            return;
        }

        foreach (var entry in entries)
        {
            var idx = entry.Index;

            // Compose the label with level / prestige / shiny inline so the
            // user can see at a glance which familiar to bind. Format:
            //   "01  —  RoyalRavager  Lv 12  P3  ★ Storm"
            string label = $"{entry.Index:00}  —  {entry.Name}";
            if (entry.Level > 0) label += $"   Lv {entry.Level}";
            if (entry.Prestige > 0) label += $"  P{entry.Prestige}";
            if (entry.IsShiny)
            {
                label += "  ★";
                var school = entry.ShinySchool;
                if (!string.IsNullOrEmpty(school)) label += $" {school}";
            }

            if (_boxesEditMode)
            {
                // Edit-mode row: bind button + red two-click Delete button.
                var row = UIFactory.CreateHorizontalGroup(_boxesContentContainer, $"FamRow_{entry.Index}",
                    forceExpandWidth: true, forceExpandHeight: false,
                    childControlWidth: true, childControlHeight: true,
                    spacing: 4, padding: new Vector4(0, 0, 0, 0));
                UIFactory.SetLayoutElement(row,
                    minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                    minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);

                var bindBtn = UIFactory.CreateButton(row, $"FamBtn_{entry.Index}", label);
                UIFactory.SetLayoutElement(bindBtn.GameObject,
                    minWidth: 240, preferredWidth: 280, flexibleWidth: 1,
                    minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
                var bindBtnTxt = bindBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
                if (bindBtnTxt != null) bindBtnTxt.fontSize = Theme.ScaledUI(12);
                bindBtn.OnClick = () => OnFamiliarClicked(idx);

                var delBtn = UIFactory.CreateButton(row, $"FamDel_{entry.Index}", "Delete",
                    new Color(0.55f, 0.18f, 0.18f));
                UIFactory.SetLayoutElement(delBtn.GameObject,
                    minWidth: 70, preferredWidth: 80, flexibleWidth: 0,
                    minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
                var delText = delBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
                if (delText != null) delText.fontSize = Theme.ScaledUI(12);
                TooltipHover.Attach(delBtn.GameObject,
                    "PERMANENTLY delete this familiar (.fam r). Two-click confirm — first click changes the label to 'Confirm?' and waits 3 seconds. Box record is gone forever.");
                int capturedIdx = idx;
                delBtn.OnClick = () => OnDeleteClicked(capturedIdx, delBtn, "Delete");
            }
            else
            {
                var b = UIFactory.CreateButton(_boxesContentContainer, $"FamBtn_{entry.Index}", label);
                UIFactory.SetLayoutElement(b.GameObject,
                    minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                    minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
                var bTxt = b.Component.GetComponentInChildren<TextMeshProUGUI>();
                if (bTxt != null) bTxt.fontSize = Theme.ScaledUI(12);
                b.OnClick = () => OnFamiliarClicked(idx);
            }
        }
    }

    private void OnDeleteClicked(int index, Raphael.UI.Framework.UniverseLib.UI.Models.ButtonRef btn, string originalLabel)
    {
        float now = Time.realtimeSinceStartup;
        bool armed = _pendingDeleteIndex == index && now <= _pendingDeleteDeadline;
        if (armed)
        {
            ClearPendingDelete();
            var label = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = originalLabel;
            EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_REMOVE_FORMAT, index));
            // Refresh after deletion so the row vanishes without a manual reload.
            EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
            return;
        }

        _pendingDeleteIndex = index;
        _pendingDeleteDeadline = now + DELETE_CONFIRM_WINDOW_SECONDS;
        var lbl = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl != null) lbl.text = "Confirm?";
    }

    private void ClearPendingDelete()
    {
        _pendingDeleteIndex = -1;
        _pendingDeleteDeadline = -1f;
    }

    private void OnBoxClicked(string boxName)
    {
        ClearPendingSwap();
        ClearPendingDelete();
        PlayerStateService.SetActiveBox(boxName);
        // .fam cb selects the box server-side, .fam l lists its contents.
        // Both fire immediately (no queue throttle) so the user sees the
        // contents view populate within ~1s of the click.
        EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_SWITCH_BOX_FORMAT, boxName));
        EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);

        _boxesShowingContents = true;
        UpdateBoxesSectionVisibility();
    }

    private void OnFamiliarClicked(int index)
    {
        // 0.17.1: under Eclipse stand-down Raphael has no active familiar from the
        // stream, so the arm-to-swap UX can't fire. Switch directly: unbind active
        // (server no-op if none) then bind the clicked one. Mirrors the Familiar
        // Browser. Pure unbind (no rebind) is the "Unbind" command button.
        if (Services.EclipseProtocolService.StandDownForEclipse())
        {
            ClearPendingSwap();
            EnqueueOrWarn(MessageService.BCCOM_FAM_UNBIND);
            EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, index));
            return;
        }

        // No active familiar → straight bind. (Familiar.Name is empty when
        // PlayerStateService has no active familiar yet.)
        bool hasActive = !string.IsNullOrEmpty(PlayerStateService.Familiar.Name);
        if (!hasActive)
        {
            ClearPendingSwap();
            EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, index));
            return;
        }

        // Active familiar: Bloodcraft requires destruction to switch. Two-click
        // confirm — first click arms, second click on the SAME index within
        // the window executes destroy → bind.
        float now = Time.realtimeSinceStartup;
        bool armed = _pendingSwapIndex == index && now <= _pendingSwapDeadline;
        if (armed)
        {
            ClearPendingSwap();
            // Send destroy first, then the new bind. Both are immediate
            // (MessageService.EnqueueMessage is throttle-free), so the server
            // sees them in order on the next frame's chat-message processing.
            EnqueueOrWarn(MessageService.BCCOM_FAM_UNBIND);
            EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, index));
            return;
        }

        // Arm pending swap. Find the entry to put its name in the warning.
        string targetName = $"#{index}";
        var active = PlayerStateService.ActiveBox;
        if (!string.IsNullOrEmpty(active)
            && PlayerStateService.BoxContents.TryGetValue(active, out var entries))
        {
            foreach (var e in entries) if (e.Index == index) { targetName = e.Name; break; }
        }

        _pendingSwapIndex = index;
        _pendingSwapDeadline = now + SWAP_CONFIRM_WINDOW_SECONDS;
        ShowSwapWarning($"Active: {PlayerStateService.Familiar.Name}. Click {targetName} again within {(int)SWAP_CONFIRM_WINDOW_SECONDS}s to unbind current and bind it. The current familiar returns to its box (level/prestige preserved); use Permanently Delete below to actually remove it from your collection.");
    }

    private void ClearPendingSwap()
    {
        _pendingSwapIndex = -1;
        _pendingSwapDeadline = -1f;
        // Empty text rather than SetActive(false) — the label stays in the
        // layout so showing/hiding the warning doesn't shift the box list.
        if (_boxesSwapWarning != null) _boxesSwapWarning.TextMesh.text = "";
    }

    private void ShowSwapWarning(string msg)
    {
        if (_boxesSwapWarning == null) return;
        _boxesSwapWarning.TextMesh.text = msg;
    }

    private static void ClearChildren(GameObject parent)
    {
        if (parent == null) return;
        var t = parent.transform;
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }

    // -----------------------------------------------------------------------
    // Class tab
    // -----------------------------------------------------------------------

    private void BuildClassTab(GameObject page)
    {
        // 0.10.11: card-wrap the three sections so the tab reads as
        // grouped content instead of one stacked column.
        var currentCard = AddCard(page, "ClassCurrentCard", Theme.SystemTintExpertise);
        AddSectionHeading(currentCard, "Active Class");
        _classNameLabel  = AddInfoLabel(currentCard, "ClassName",  "—",       FontStyles.Bold,   fontSize: Theme.ScaledUI(18));
        _classLevelLabel = AddInfoLabel(currentCard, "ClassLevel", "Level —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        // 0.17.3: missing-class nudge (hidden unless no class is selected + the hint
        // setting is on + the server has the Class system enabled). Set in RenderClass.
        _classHintLabel  = AddInfoLabel(currentCard, "ClassHint", string.Empty, FontStyles.Italic, fontSize: Theme.ScaledUI(13));
        _classHintLabel.gameObject.SetActive(false);

        // 0.13.0: live class-details block — archetype + tagline + weapon
        // synergies + blood synergies + on-hit debuff. Replaces nothing —
        // augments the existing class name + level by showing WHAT picking
        // this class actually gets you. RenderClass updates this on
        // class-change so the user always sees their current loadout's
        // capabilities without leaving the tab.
        AddDivider(currentCard);
        _classDetailsLabel = AddContextBodyLabel(currentCard, "ClassDetails",
            FormatClassDetailsBlock(PlayerStateService.PlayerClass.None), fontSize: 13);
        AddServerDisclaimer(currentCard);

        AddSpacer(page, 6);

        var actionsCard = AddCard(page, "ClassActionsCard");
        AddSectionHeading(actionsCard, "Actions");
        var actions = UIFactory.CreateHorizontalGroup(actionsCard, "ClassActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "List Classes",  MessageService.BCCOM_CLASS_LIST,
            "List the classes available on this server (.class l). Response appears in chat.");
        AddCommandButton(actions, "List Spells",   MessageService.BCCOM_CLASS_LIST_SPELLS,
            "List the spells granted by your current class (.class lsp). Response in chat.");
        AddCommandButton(actions, "List Stats",    MessageService.BCCOM_CLASS_LIST_STATS,
            "List the weapon/blood stat synergies for your current class (.class lst).");
        AddCommandButton(actions, "Toggle Shift",  MessageService.BCCOM_CLASS_TOGGLE_SHIFT,
            "Toggle whether the class spell occupies your shift-slot (.class shift).");

        AddSpacer(page, 6);

        var changeCard = AddCard(page, "ClassChangeCard");
        AddSectionHeading(changeCard, "Change Class");
        // 0.73.0: Bloodcraft's `.class s` only works when you have NO class yet (ClassCommands.SelectClass);
        // changing an existing class is `.class c`, which costs ChangeClassItem × ChangeClassQuantity
        // (default 750× Greater Stygian Shards). Two forms, one per command.
        CollapsibleSection.Build(changeCard,
            title: "Select first class (.class s)",
            startExpanded: false,
            tooltip: "Pick your FIRST class from the dropdown and Submit. Only works while you have no class yet — to switch later, use Change class (.class c) below. Bloodcraft replies in chat with success or the rejection reason.",
            buildContent: c => FormBuilder.Build(c,
                title: "Select first class",
                commandTemplate: ".class s {class}",
                new EnumField<PlayerStateService.BloodcraftClassChoice>("class", "Class",
                    defaultValue: PlayerStateService.BloodcraftClassChoice.BloodKnight,
                    tooltip: "The class you want active. Bloodcraft's six built-in classes are listed.")));

        CollapsibleSection.Build(changeCard,
            title: "Change class (.class c)",
            startExpanded: false,
            tooltip: "Switch from your current class to another. Costs 750× Greater Stygian Shards by default (the server can change the item and amount). Bloodcraft replies in chat with success or the rejection reason.",
            buildContent: c => FormBuilder.Build(c,
                title: "Change class",
                commandTemplate: MessageService.BCCOM_CLASS_CHANGE_FORMAT.Replace("{0}", "{class}"),
                new EnumField<PlayerStateService.BloodcraftClassChoice>("class", "Class",
                    defaultValue: PlayerStateService.BloodcraftClassChoice.BloodKnight,
                    tooltip: "The class to change to. Costs 750× Greater Stygian Shards by default.")));

        CollapsibleSection.Build(changeCard,
            title: "Choose class shift spell (.class csp)",
            startExpanded: false,
            tooltip: "Set which spell occupies your shift slot. 0 = default spell (Veil of Shadow); 1..N = class spells as numbered by List Spells. Needs Toggle Shift on.",
            buildContent: c => FormBuilder.Build(c,
                title: "Choose class shift spell",
                commandTemplate: ".class csp {index}",
                // 0.73.0: min 0 — Bloodcraft's `.class csp 0` picks the default class spell (ClassCommands.ChooseClassSpell).
                new IntField("index", "Spell #", min: 0, max: 32,
                    tooltip: "0 = default spell (Veil of Shadow); 1..N = class spells as numbered by List Spells. Needs Toggle Shift on.")));

        AddDivider(changeCard);
        AddBodyText(changeCard,
            $"Tip: {Mono("List Spells")} / {Mono("List Stats")} above describe what each class grants before you commit.");

        // 0.13.0: comparison collapsible — all six classes side-by-side
        // inline so the user picking a new class doesn't have to switch
        // to Mod Help to compare options. Each block reuses the same
        // FormatClassDetailsBlock the Active Class card displays, so the
        // information stays consistent across surfaces.
        CollapsibleSection.Build(changeCard,
            title: "Compare all classes",
            startExpanded: false,
            tooltip: "Expand to see each of the six Bloodcraft classes side-by-side — their weapon and blood synergies, archetype, and on-hit debuff school. Helpful before committing to a class change.",
            buildContent: c =>
            {
                AddContextBodyLabel(c, "ClassCompareIntro",
                    "<i>Each class grants a 1.5× cap multiplier on its synergized weapon + blood stats, an on-hit debuff at 7.5% proc chance (default), and a class-specific spell school. Class change costs 750× Greater Stygian Shards by default.</i>",
                    fontSize: 12);
                AddDivider(c);
                AddClassComparisonBlock(c, PlayerStateService.PlayerClass.BloodKnight);
                AddClassComparisonBlock(c, PlayerStateService.PlayerClass.VampireLord);
                AddClassComparisonBlock(c, PlayerStateService.PlayerClass.DemonHunter);
                AddClassComparisonBlock(c, PlayerStateService.PlayerClass.ShadowBlade);
                AddClassComparisonBlock(c, PlayerStateService.PlayerClass.ArcaneSorcerer);
                AddClassComparisonBlock(c, PlayerStateService.PlayerClass.DeathMage);
                AddServerDisclaimer(c);
            });

        RenderClass(PlayerStateService.Experience);
        if (!_classSubscribed)
        {
            PlayerStateService.ExperienceChanged += OnExperienceChangedForClass;
            _classSubscribed = true;
        }
    }

    private void OnExperienceChangedForClass() => RenderClass(PlayerStateService.Experience);

    private void RenderClass(PlayerStateService.ExperienceState s)
    {
        if (_classNameLabel == null) return;
        _classNameLabel.text = s.Class == PlayerStateService.PlayerClass.None
            ? "(no class selected)"
            : s.Class.ToString();
        _classLevelLabel.text = s.Class == PlayerStateService.PlayerClass.None
            ? "Use .class s <Class> in chat to choose one."
            : $"Player Level {s.Level}   Prestige {s.Prestige}";

        // 0.13.0: refresh the per-tab class-synergy hint cards on the Class /
        // Expertise / Blood Legacy tabs so the live PlayerClass.Class drives
        // which weapon + blood synergies are shown. Each label is built once
        // at tab construct time; this just updates its text.
        if (_classDetailsLabel != null)
            _classDetailsLabel.text = FormatClassDetailsBlock(s.Class);
        if (_wepClassSynergyLabel != null)
            _wepClassSynergyLabel.text = FormatClassWeaponSynergyHint(s.Class);
        if (_blClassSynergyLabel != null)
            _blClassSynergyLabel.text = FormatClassBloodSynergyHint(s.Class);

        UpdateMissingHint(_classHintLabel, Services.ProgressionHints.ClassHint());
    }

    // 0.17.3: show/hide a "missing element — free power" nudge label. Hidden entirely
    // (no empty row) when there's nothing to show or the hint setting is off.
    private static void UpdateMissingHint(TextMeshProUGUI label, string hintMarkup)
    {
        if (label == null) return;
        bool show = !string.IsNullOrEmpty(hintMarkup);
        if (show) label.text = hintMarkup;
        if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
    }

    // 0.13.0: shared Bloodcraft class data — single source of truth used by
    // the Class / Expertise / Blood Legacy context cards AND the Mod Help
    // tab's class section (Mod Help still hard-codes the same data inline for
    // readability — if you change a synergy here, update BuildModHelpTab too).
    private struct ClassInfo
    {
        public string DisplayName;
        public string Archetype;          // Warrior / Rogue / Caster
        public string Tagline;
        public string[] WeaponSynergies;  // stat names
        public string[] BloodSynergies;
        public string OnHitDebuff;
        public string OnHitSecondary;
    }

    private static readonly System.Collections.Generic.Dictionary<PlayerStateService.PlayerClass, ClassInfo> ClassInfoByClass = new()
    {
        [PlayerStateService.PlayerClass.BloodKnight] = new ClassInfo
        {
            DisplayName = "Blood Knight",
            Archetype = "Warrior",
            Tagline = "Tank-leaning vampire warrior; sword + life-leech identity.",
            WeaponSynergies = new[] { "Max Health", "Primary Attack Speed", "Primary Life Leech", "Physical Power" },
            BloodSynergies  = new[] { "Damage Reduction", "Reduced Blood Drain", "Weapon Cooldown Recovery", "Ability Attack Speed" },
            OnHitDebuff     = "Leech",
            OnHitSecondary  = "Lesser Bloodrage self-buff",
        },
        [PlayerStateService.PlayerClass.VampireLord] = new ClassInfo
        {
            DisplayName = "Vampire Lord",
            Archetype = "Warrior",
            Tagline = "AOE / sustain warrior; mace + scholar blood scales spell power.",
            WeaponSynergies = new[] { "Max Health", "Spell Life Leech", "Physical Power", "Spell Power" },
            BloodSynergies  = new[] { "Damage Reduction", "Spell Resistance", "Ultimate Cooldown Recovery", "Corruption Damage Reduction" },
            OnHitDebuff     = "Chill",
            OnHitSecondary  = "Lesser Frozen Weapon self-buff",
        },
        [PlayerStateService.PlayerClass.DemonHunter] = new ClassInfo
        {
            DisplayName = "Demon Hunter",
            Archetype = "Rogue",
            Tagline = "Ranged / holy crit-driven physical damage.",
            WeaponSynergies = new[] { "Movement Speed", "Primary Attack Speed", "Physical Crit Chance", "Physical Crit Damage" },
            BloodSynergies  = new[] { "Physical Resistance", "Reduced Blood Drain", "Weapon Cooldown Recovery", "Minion Damage" },
            OnHitDebuff     = "Static",
            OnHitSecondary  = "Lesser Stormshield self-buff",
        },
        [PlayerStateService.PlayerClass.ShadowBlade] = new ClassInfo
        {
            DisplayName = "Shadow Blade",
            Archetype = "Rogue",
            Tagline = "Dagger / shadow rogue; movement + crit-chance leaning.",
            WeaponSynergies = new[] { "Movement Speed", "Primary Attack Speed", "Physical Power", "Physical Crit Damage" },
            BloodSynergies  = new[] { "Spell Resistance", "Reduced Blood Drain", "Weapon Cooldown Recovery", "Ability Attack Speed" },
            OnHitDebuff     = "Ignite",
            OnHitSecondary  = "Lesser Powersurge self-buff",
        },
        [PlayerStateService.PlayerClass.ArcaneSorcerer] = new ClassInfo
        {
            DisplayName = "Arcane Sorcerer",
            Archetype = "Caster",
            Tagline = "Pure spell-power caster; scholar blood is the natural pair.",
            WeaponSynergies = new[] { "Spell Life Leech", "Spell Power", "Spell Crit Chance", "Spell Crit Damage" },
            BloodSynergies  = new[] { "Healing Received", "Spell Cooldown Recovery", "Ultimate Cooldown Recovery", "Ability Attack Speed" },
            OnHitDebuff     = "Weaken",
            OnHitSecondary  = "Lesser Aegis self-buff",
        },
        [PlayerStateService.PlayerClass.DeathMage] = new ClassInfo
        {
            DisplayName = "Death Mage",
            Archetype = "Caster",
            Tagline = "Necromancy-themed caster; shadow / corruption synergies.",
            WeaponSynergies = new[] { "Max Health", "Spell Life Leech", "Spell Power", "Spell Crit Damage" },
            BloodSynergies  = new[] { "Physical Resistance", "Spell Resistance", "Spell Cooldown Recovery", "Minion Damage" },
            OnHitDebuff     = "Condemn",
            OnHitSecondary  = "Guardian Block self-buff",
        },
    };

    private static string FormatSynergyList(string[] stats)
        => stats == null || stats.Length == 0 ? "—" : string.Join("  •  ", stats);

    /// <summary>0.13.0: multi-line block describing one class. Used in the
    /// Class tab's per-class comparison + the "Active class details" card.</summary>
    private static string FormatClassDetailsBlock(PlayerStateService.PlayerClass cls)
    {
        if (cls == PlayerStateService.PlayerClass.None || !ClassInfoByClass.TryGetValue(cls, out var info))
            return "<i>No class selected — pick one in the Change Class section below to see its synergies and on-hit effect.</i>";
        return
            $"<b>{info.DisplayName}</b>  ({info.Archetype})\n" +
            $"<i>{info.Tagline}</i>\n" +
            $"  Weapon synergies (1.5× cap):  {FormatSynergyList(info.WeaponSynergies)}\n" +
            $"  Blood synergies (1.5× cap):   {FormatSynergyList(info.BloodSynergies)}\n" +
            $"  On-hit debuff:  {info.OnHitDebuff}  (secondary: {info.OnHitSecondary})";
    }

    /// <summary>0.13.0: short hint shown on the Weapon Expertise tab — tells
    /// the player which stats their class amplifies so the bonus-stat picker
    /// is informed by class context, no tab-switch required.</summary>
    private static string FormatClassWeaponSynergyHint(PlayerStateService.PlayerClass cls)
    {
        if (cls == PlayerStateService.PlayerClass.None || !ClassInfoByClass.TryGetValue(cls, out var info))
            return "<i>No class selected — every weapon stat scales at its baseline cap until you pick a class (Class tab).</i>";
        return
            $"Your class (<b>{info.DisplayName}</b>) amplifies these weapon stats with a <b>1.5× cap</b>:\n" +
            $"   {FormatSynergyList(info.WeaponSynergies)}\n" +
            $"<i>Picking one of these for the weapon you fight with gets you the most out of every expertise level.</i>";
    }

    /// <summary>0.13.0: companion hint for the Blood Legacy tab.</summary>
    private static string FormatClassBloodSynergyHint(PlayerStateService.PlayerClass cls)
    {
        if (cls == PlayerStateService.PlayerClass.None || !ClassInfoByClass.TryGetValue(cls, out var info))
            return "<i>No class selected — every blood stat scales at its baseline cap until you pick a class (Class tab).</i>";
        return
            $"Your class (<b>{info.DisplayName}</b>) amplifies these blood stats with a <b>1.5× cap</b>:\n" +
            $"   {FormatSynergyList(info.BloodSynergies)}\n" +
            $"<i>Stacking blood + weapon picks toward the same role compounds the bonuses.</i>";
    }

    /// <summary>0.13.0: word-wrapped body label for use inside context cards
    /// on the action tabs. Returns the TMP_Text so callers that need live
    /// updates (Class / Expertise / Legacy synergy hints) can stash a
    /// reference and rewrite .text on class changes.</summary>
    private static TextMeshProUGUI AddContextBodyLabel(GameObject parent, string name, string initialText, int fontSize = 13)
    {
        var lbl = UIFactory.CreateLabel(parent, name, initialText,
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(fontSize));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
            minHeight: 22, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = true;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        lbl.TextMesh.fontStyle = FontStyles.Normal;
        var fitter = lbl.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        return lbl.TextMesh;
    }

    /// <summary>0.13.0: italic muted-grey disclaimer line shared by every
    /// context card. Reinforces that the numbers come from Bloodcraft's
    /// shipped defaults and that the server admin can override them.</summary>
    private static void AddServerDisclaimer(GameObject parent, string name = "ServerDisclaimer")
    {
        var lbl = UIFactory.CreateLabel(parent, name,
            "<i>Defaults shown — your server's admin may have overridden any of these values in Bloodcraft.cfg.</i>",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 22, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = true;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    /// <summary>0.13.0: build a per-class block inside the Compare-All-Classes
    /// collapsible on the Class tab. Re-uses FormatClassDetailsBlock so the
    /// visual data exactly matches what the Active Class card shows.</summary>
    private static void AddClassComparisonBlock(GameObject parent, PlayerStateService.PlayerClass cls)
    {
        AddContextBodyLabel(parent, $"ClassCompare_{cls}", FormatClassDetailsBlock(cls), fontSize: 13);
        AddDivider(parent);
    }

    // -----------------------------------------------------------------------
    // Weapon Expertise tab
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // 0.10.0: V-Bloods tab. Collection tracker for the 65 named V-Bloods
    // listed in VBloodRegistry. Rows are built once at tab-construct time,
    // refreshed in place when PlayerStateService.VBloodCollection changes or
    // when the scanner ticks. Filter buttons re-show/hide the existing row
    // GameObjects rather than rebuilding — keeps scroll position stable
    // across filter switches.
    //
    // Status chips per row:
    //   B  — basic variant captured
    //   S  — basic + shiny captured
    //   P  — Primal variant captured
    //   PS — Primal + shiny captured
    //
    // Each chip is one of:
    //   green "[B]" = captured     gray "[B]" = not captured (yet)
    // We pack all four chips into a single TMP label using <color> tags so
    // each row only needs one label instead of four, keeping layout cheap.
    // -----------------------------------------------------------------------
    // 0.10.9: V-Bloods tab rebuilt around the box-sweep scanner. One row
    // per CAPTURED VARIANT (basic / shiny / primal / primal-shiny) with
    // explicit per-variant Summon. Pre-0.10.9 the chip view rendered a
    // single row per V-Blood name with 4 status chips and a single
    // ambiguous Summon button — users had no way to choose which variant
    // to bind. The 0.10.7 "Instances" view sourced data from BoxContents
    // (required manual box navigation); the new scanner populates
    // BoxContents AND a precise per-variant index, so the two views are
    // collapsed back into one.
    private void BuildVBloodsTab(GameObject page)
    {
        // 0.10.10: every section wrapped in a card so progress / filter
        // controls aren't flush with the panel edge anymore (friend-test
        // feedback: "X / Y captured is flush left against the border").

        // Header card — Section heading + help text inside.
        var headerCard = AddCard(page, "VBloodsHeaderCard");
        AddSectionHeading(headerCard, "V-Blood Collection");
        AddBodyText(headerCard,
            "One row per captured V-Blood variant (basic / shiny / primal / primal shiny). " +
            "This list fills in <b>passively</b> as you browse your familiar boxes (via the " +
            "Familiar Browser or Boxes tab) — every box you open is recorded automatically. " +
            $"<b>Scan all</b> walks <i>every</i> box once via {Mono(".fam boxes")} + {Mono(".fam l")} (restoring your active box afterward) " +
            "to capture boxes you haven't visited and to reconcile any you've cleared out. " +
            "Filter shows All / Captured / Missing / Shiny only.");

        AddSpacer(page, 6);

        // Progress + Scan card.
        var progressCard = AddCard(page, "VBloodsProgressCard");
        var headerRow = UIFactory.CreateHorizontalGroup(progressCard, "VBloodsHeader",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(headerRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        _vbProgressLabel = AddInfoLabel(headerRow, "VBProgress", "0 / 65 captured",
            FontStyles.Bold, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(_vbProgressLabel.gameObject,
            minWidth: 180, preferredWidth: 240, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 28, flexibleHeight: 0);

        _vbScanButton = UIFactory.CreateButton(headerRow, "VBScanBtn", "Scan all");
        UIFactory.SetLayoutElement(_vbScanButton.GameObject,
            minWidth: 90, preferredWidth: 110, flexibleWidth: 0,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        var scanBtnText = _vbScanButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (scanBtnText != null) { scanBtnText.fontSize = Theme.ScaledUI(12); scanBtnText.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(_vbScanButton.GameObject,
            "Sweep every familiar box via .fam boxes + .fam cb + .fam l — populates the V-Blood collection AND per-box contents in one pass. ~30–60s. Your active box is restored when the sweep finishes. Cancel halts the sweep mid-flight.");
        _vbScanButton.OnClick = () =>
        {
            if (VBloodScannerService.Scanning) VBloodScannerService.CancelScan();
            else                                VBloodScannerService.StartScan();
            RefreshVBScanButton();
        };

        _vbScanStatusLabel = AddInfoLabel(progressCard, "VBScanStatus", "",
            FontStyles.Italic, fontSize: Theme.ScaledUI(11));
        _vbScanStatusLabel.gameObject.SetActive(false);

        AddSpacer(page, 6);

        // Filter + Sort card.
        var filterCard = AddCard(page, "VBloodsFilterCard");
        var filterRow = UIFactory.CreateHorizontalGroup(filterCard, "VBloodsFilters",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(filterRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        AddVBFilterButton(filterRow, "All",      VBloodFilter.All);
        AddVBFilterButton(filterRow, "Captured", VBloodFilter.Captured);
        AddVBFilterButton(filterRow, "Missing",  VBloodFilter.Missing);
        AddVBFilterButton(filterRow, "Shiny",    VBloodFilter.ShinyOnly);

        _vbSortButton = UIFactory.CreateButton(filterRow, "VBSortBtn", FormatVBSortButtonText());
        UIFactory.SetLayoutElement(_vbSortButton.GameObject,
            minWidth: 100, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var sortTxt = _vbSortButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (sortTxt != null) { sortTxt.fontSize = Theme.ScaledUI(12); sortTxt.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(_vbSortButton.GameObject,
            "Cycle sort order: Default (alpha by name) → Alphabetical → By level (descending across all captured instances) → By region.");
        _vbSortButton.OnClick = () =>
        {
            var current = Config.Settings.FamiliarSortOrderSetting;
            var next = current switch
            {
                Config.Settings.FamiliarSortOrder.Default      => Config.Settings.FamiliarSortOrder.Alphabetical,
                Config.Settings.FamiliarSortOrder.Alphabetical => Config.Settings.FamiliarSortOrder.Level,
                Config.Settings.FamiliarSortOrder.Level        => Config.Settings.FamiliarSortOrder.Location,
                Config.Settings.FamiliarSortOrder.Location     => Config.Settings.FamiliarSortOrder.Default,
                _                                               => Config.Settings.FamiliarSortOrder.Default,
            };
            Config.Settings.SetFamiliarSortOrder(next);
            RefreshVBSortButtonText();
            RebuildVBRows();
            // The overlay also reads this setting; ping it so its list re-orders too.
            try { Plugin.UIManager?.FamiliarBrowserOverlay?.NotifySortOrderChanged(); }
            catch { /* overlay may not be open */ }
        };

        AddSpacer(page, 6);

        // Rows card — section heading + column-header row + the dynamic
        // rows themselves. Pre-0.10.10 the rows lived flush against the
        // panel edge and shifted column widths because the shiny-school
        // chip was conditionally rendered.
        var rowsCard = AddCard(page, "VBloodsRowsCard", padding: 4, innerSpacing: 2);
        AddSectionHeading(rowsCard, "V-Bloods");
        BuildVBColumnHeader(rowsCard); // 0.10.10: column labels above the rows for clarity

        // Rows are dynamic — rebuilt each time the collection / filter / sort
        // changes. Cheap; the registry caps at ~65 names plus 0..N variants
        // each (typical owned ~10-30 instances, so list is small).
        _vbRowContainer = UIFactory.CreateVerticalGroup(rowsCard, "VBloodRows",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(_vbRowContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 60, flexibleHeight: 0);

        if (!_vbSubscribed)
        {
            PlayerStateService.VBloodCollectionChanged += OnVBloodCollectionChanged;
            VBloodScannerService.ScanStateChanged      += OnVBloodScanStateChanged;
            _vbSubscribed = true;
        }

        // 0.10.10: auto-scan is now opt-in. Friend-testing 0.10.9: the scan
        // walks the box list and the `.fam cb`/`.fam l` confirmations leaked
        // into chat (suppression-flag gap fixed below, but the unannounced
        // box-switching was still surprising). Pre-0.10.10 we triggered
        // StartScan unconditionally when VBloodCollection was empty; now
        // the user clicks "Scan all" (or can opt back in via Display
        // Settings → "Auto-scan on tab open").
        if (Config.Settings.AutoScanVBloodsOnTabOpen
            && PlayerStateService.VBloodCollection.Count == 0
            && !VBloodScannerService.Scanning)
        {
            System.Action deferStart = null;
            deferStart = () =>
            {
                Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(deferStart);
                if (MessageService.IsInitialized && PlayerStateService.VBloodCollection.Count == 0)
                    VBloodScannerService.StartScan();
                RefreshVBScanButton();
            };
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(deferStart);
        }

        RebuildVBRows();
        RefreshVBHeader();
        RefreshVBScanButton();
    }

    /// <summary>0.10.9: subscribed to VBloodCollectionChanged. Cheap, but
    /// rebuilds the entire row list — fine because typical scenarios have
    /// at most a few dozen instances.</summary>
    private void OnVBloodCollectionChanged()
    {
        RebuildVBRows();
        RefreshVBHeader();
    }

    private void OnVBloodScanStateChanged()
    {
        RefreshVBScanButton();
        RefreshVBHeader();
    }

    private void RefreshVBScanButton()
    {
        if (_vbScanButton == null) return;
        var t = _vbScanButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = VBloodScannerService.Scanning ? "Cancel" : "Scan all";
    }

    private void RefreshVBHeader()
    {
        if (_vbProgressLabel == null) return;
        int total = Resources.VBloodRegistry.All.Length;
        int capturedNames = 0, primalNames = 0, shinyInstances = 0, totalInstances = 0;
        foreach (var slot in PlayerStateService.VBloodCollection.Values)
        {
            if (slot.Instances == null || slot.Instances.Count == 0) continue;
            capturedNames++;
            if (slot.HasPrimal || slot.HasPrimalShiny) primalNames++;
            foreach (var i in slot.Instances)
            {
                totalInstances++;
                if (i.IsShiny) shinyInstances++;
            }
        }
        string text = $"{capturedNames} / {total} captured  ·  {totalInstances} instance{(totalInstances == 1 ? "" : "s")}";
        if (primalNames    > 0) text += $"  ·  {primalNames} primal";
        if (shinyInstances > 0) text += $"  ·  {shinyInstances} shiny";
        _vbProgressLabel.text = text;

        if (_vbScanStatusLabel != null)
        {
            if (VBloodScannerService.Scanning)
            {
                string box = VBloodScannerService.CurrentBoxBeingScanned;
                string suffix = string.IsNullOrEmpty(box) ? "" : $" — {box}";
                _vbScanStatusLabel.text = $"Scanning… box {VBloodScannerService.CompletedForCurrentScan + 1} / {VBloodScannerService.TotalForCurrentScan}{suffix}";
                if (!_vbScanStatusLabel.gameObject.activeSelf) _vbScanStatusLabel.gameObject.SetActive(true);
            }
            else if (_vbScanStatusLabel.text != null && _vbScanStatusLabel.text.StartsWith("Scanning"))
            {
                _vbScanStatusLabel.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>0.10.9: full teardown + rebuild. Sorts rows per the
    /// FamiliarSortOrder setting and applies the current filter. Captured
    /// variants render with full detail + Summon; un-captured V-Bloods (in
    /// All / Missing filter) render as a muted placeholder row with no
    /// Summon button.</summary>
    private void RebuildVBRows()
    {
        if (_vbRowContainer == null) return;

        // Tear down all existing rows.
        for (int i = _vbRowContainer.transform.childCount - 1; i >= 0; --i)
        {
            var child = _vbRowContainer.transform.GetChild(i);
            if (child == null) continue;
            UnityEngine.Object.Destroy(child.gameObject);
        }

        // Build the row list. Each row is either a captured variant
        // (VBRow with Name + variant tag + stats + box + Summon) or a
        // missing-name placeholder (VBRowMissing — name only, dim).
        var rows = new List<VBRowSpec>();
        bool includeMissing = _vbFilter == VBloodFilter.All || _vbFilter == VBloodFilter.Missing;
        bool includeCaptured = _vbFilter != VBloodFilter.Missing;
        bool shinyOnly = _vbFilter == VBloodFilter.ShinyOnly;

        if (includeCaptured)
        {
            foreach (var kv in PlayerStateService.VBloodCollection)
            {
                var slot = kv.Value;
                if (slot.Instances == null) continue;
                foreach (var inst in slot.Instances)
                {
                    if (shinyOnly && !inst.IsShiny) continue;
                    if (_vbFilter == VBloodFilter.Captured && shinyOnly) continue; // already shiny path
                    rows.Add(new VBRowSpec { Name = slot.Name, Instance = inst, IsMissing = false });
                }
            }
        }
        if (includeMissing)
        {
            foreach (var name in Resources.VBloodRegistry.All)
            {
                if (PlayerStateService.VBloodCollection.TryGetValue(name, out var slot)
                    && slot.Instances != null && slot.Instances.Count > 0) continue;
                rows.Add(new VBRowSpec { Name = name, IsMissing = true });
            }
        }

        SortVBRows(rows);

        if (rows.Count == 0)
        {
            var empty = UIFactory.CreateLabel(_vbRowContainer, "VBEmpty",
                _vbFilter == VBloodFilter.Missing
                    ? "Nothing missing — every registered V-Blood has at least one capture. Nice work."
                    : "No captures match the current filter. Try Scan or switch filter to All.",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(empty.GameObject,
                minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
                minHeight: 28, preferredHeight: 32, flexibleHeight: 0);
            empty.TextMesh.fontStyle = FontStyles.Italic;
            return;
        }

        foreach (var spec in rows)
        {
            if (spec.IsMissing) BuildVBMissingRow(_vbRowContainer, spec.Name);
            else                BuildVBVariantRow(_vbRowContainer, spec.Name, spec.Instance);
        }
    }

    private struct VBRowSpec
    {
        public string Name;
        public PlayerStateService.VBloodInstance Instance;
        public bool   IsMissing;
    }

    private static void SortVBRows(List<VBRowSpec> rows)
    {
        var mode = Config.Settings.FamiliarSortOrderSetting;
        // Always: captured before missing in the All filter so the user
        // sees their actual collection at the top.
        rows.Sort((a, b) =>
        {
            int missCmp = a.IsMissing.CompareTo(b.IsMissing);
            if (missCmp != 0) return missCmp; // false < true → captured first
            switch (mode)
            {
                case Config.Settings.FamiliarSortOrder.Alphabetical:
                {
                    int c = string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return CompareVariantOrder(a, b);
                }
                case Config.Settings.FamiliarSortOrder.Level:
                {
                    if (a.IsMissing || b.IsMissing)
                        return string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                    int c = b.Instance.Level.CompareTo(a.Instance.Level);
                    if (c != 0) return c;
                    c = b.Instance.Prestige.CompareTo(a.Instance.Prestige);
                    if (c != 0) return c;
                    return string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                }
                case Config.Settings.FamiliarSortOrder.Location:
                {
                    int ra = Resources.VBloodRegistry.RegionOrderFor(a.Name);
                    int rb = Resources.VBloodRegistry.RegionOrderFor(b.Name);
                    if (ra != rb) return ra.CompareTo(rb);
                    int c = string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return CompareVariantOrder(a, b);
                }
                case Config.Settings.FamiliarSortOrder.Default:
                default:
                {
                    int c = string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return CompareVariantOrder(a, b);
                }
            }
        });
    }

    /// <summary>Stable variant ordering within one V-Blood name: basic →
    /// shiny → primal → primal-shiny. Keeps the chip view readable when
    /// the player has 2+ variants of the same name back-to-back.</summary>
    private static int CompareVariantOrder(VBRowSpec a, VBRowSpec b)
    {
        if (a.IsMissing || b.IsMissing) return 0;
        int va = VariantOrder(a.Instance);
        int vb = VariantOrder(b.Instance);
        return va.CompareTo(vb);
    }
    private static int VariantOrder(PlayerStateService.VBloodInstance i)
        => (i.IsPrimal ? 2 : 0) + (i.IsShiny ? 1 : 0); // basic 0, shiny 1, primal 2, primal-shiny 3

    private static string FormatVBSortButtonText()
    {
        var mode = Config.Settings.FamiliarSortOrderSetting;
        return mode switch
        {
            Config.Settings.FamiliarSortOrder.Alphabetical => "Sort: Alpha",
            Config.Settings.FamiliarSortOrder.Level        => "Sort: Level",
            Config.Settings.FamiliarSortOrder.Location     => "Sort: Region",
            _                                              => "Sort: Default",
        };
    }

    private void RefreshVBSortButtonText()
    {
        if (_vbSortButton == null) return;
        var t = _vbSortButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = FormatVBSortButtonText();
    }

    private void AddVBFilterButton(GameObject parent, string label, VBloodFilter mode)
    {
        var btn = UIFactory.CreateButton(parent, $"VBFilter_{label}", label);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 70, preferredWidth: 90, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(12); t.alignment = TextAlignmentOptions.Center; }
        btn.OnClick = () =>
        {
            _vbFilter = mode;
            RebuildVBRows();
        };
    }

    // Variant-tag color palette. Bright captured colors stand out against
    // the muted "missing" rows; primal gets a warmer gold-orange and shiny
    // / primal-shiny inherit cyan to echo the school-color convention used
    // in the Boxes tab. Picked for contrast against the panel background,
    // not lifted from Theme.Level* (those are for system-progress tinting).
    private const string VB_VARIANT_BASIC_HEX        = "#7CDA7C"; // green
    private const string VB_VARIANT_SHINY_HEX        = "#9AE0FF"; // light cyan
    private const string VB_VARIANT_PRIMAL_HEX       = "#FFC066"; // gold-orange
    private const string VB_VARIANT_PRIMAL_SHINY_HEX = "#FFA0F0"; // pink — Bloodcraft's own shiny-marker hue
    private const string VB_MISSING_HEX              = "#888888"; // mid-grey

    private static string VariantTag(PlayerStateService.VBloodInstance i)
    {
        string label = i.IsPrimal
            ? (i.IsShiny ? "PS" : "P")
            : (i.IsShiny ? "S"  : "B");
        string hex = i.IsPrimal
            ? (i.IsShiny ? VB_VARIANT_PRIMAL_SHINY_HEX : VB_VARIANT_PRIMAL_HEX)
            : (i.IsShiny ? VB_VARIANT_SHINY_HEX        : VB_VARIANT_BASIC_HEX);
        return $"<color={hex}><b>[{label}]</b></color>";
    }

    // 0.10.10: strict column widths so the rows form a real table. Pre-
    // 0.10.10 the shiny-school chip was OMITTED for non-shiny rows, which
    // collapsed that column — name and box shifted left and the layout
    // looked ragged. Every row now reserves all columns even when blank;
    // BuildVBColumnHeader uses these exact constants too.
    private const int VB_COL_TAG_W     = 36;
    private const int VB_COL_LV_W      = 70;
    private const int VB_COL_SHINY_W   = 96;
    private const int VB_COL_BOX_W     = 100;
    private const int VB_COL_SUMMON_W  = 78;
    private const int VB_ROW_SPACING   = 6;

    /// <summary>0.10.10: tabular column-header row that lives just above
    /// the rows. Uses the same widths as BuildVBVariantRow /
    /// BuildVBMissingRow so the visual columns line up.</summary>
    private static void BuildVBColumnHeader(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "VBColHeader",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: VB_ROW_SPACING, padding: new Vector4(2, 2, 1, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);

        void Col(string label, int w, TextAlignmentOptions align)
        {
            var l = UIFactory.CreateLabel(row, $"H_{label}",
                $"<color={Theme.MutedBodyHex}>{label}</color>",
                align, color: null, fontSize: Theme.ScaledUI(10));
            UIFactory.SetLayoutElement(l.GameObject,
                minWidth: w, preferredWidth: w, flexibleWidth: 0,
                minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
            l.TextMesh.enableWordWrapping = false;
            l.TextMesh.overflowMode = TextOverflowModes.Overflow;
            l.TextMesh.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
        }

        Col("Type", VB_COL_TAG_W, TextAlignmentOptions.Midline);

        // Name column is flex so it absorbs the leftover space.
        var nameHdr = UIFactory.CreateLabel(row, "H_Name",
            $"<color={Theme.MutedBodyHex}>Name</color>",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(10));
        UIFactory.SetLayoutElement(nameHdr.GameObject,
            minWidth: 100, preferredWidth: 140, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        nameHdr.TextMesh.enableWordWrapping = false;
        nameHdr.TextMesh.overflowMode = TextOverflowModes.Overflow;
        nameHdr.TextMesh.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;

        Col("Lv",     VB_COL_LV_W,     TextAlignmentOptions.Midline);
        Col("Shiny",  VB_COL_SHINY_W,  TextAlignmentOptions.Midline);
        Col("Box",    VB_COL_BOX_W,    TextAlignmentOptions.MidlineLeft);
        Col("",       VB_COL_SUMMON_W, TextAlignmentOptions.Midline); // summon column has no header label
    }

    private void BuildVBVariantRow(GameObject parent, string name, PlayerStateService.VBloodInstance instance)
    {
        var row = UIFactory.CreateHorizontalGroup(parent,
            $"VBRow_{name}_{(instance.IsPrimal ? 'P' : 'B')}{(instance.IsShiny ? 'S' : '_')}_{instance.Box}_{instance.Index}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: VB_ROW_SPACING, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        // Type column — variant tag.
        var tag = UIFactory.CreateLabel(row, "Tag", VariantTag(instance),
            TextAlignmentOptions.Midline, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(tag.GameObject,
            minWidth: VB_COL_TAG_W, preferredWidth: VB_COL_TAG_W, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        tag.TextMesh.enableWordWrapping = false;
        tag.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // Name column — flex.
        var nameLbl = AddInfoLabel(row, "Name", name,
            FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        nameLbl.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(nameLbl.gameObject,
            minWidth: 100, preferredWidth: 140, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        nameLbl.enableWordWrapping = false;
        nameLbl.overflowMode = TextOverflowModes.Ellipsis;

        // Level column — fixed width, center-aligned.
        string statsTxt = instance.Prestige > 0
            ? $"Lv {instance.Level}  Pr {instance.Prestige}"
            : (instance.Level > 0 ? $"Lv {instance.Level}" : "—");
        var statsLbl = AddInfoLabel(row, "Stats", statsTxt,
            FontStyles.Normal, fontSize: Theme.ScaledUI(11));
        statsLbl.alignment = TextAlignmentOptions.Midline;
        UIFactory.SetLayoutElement(statsLbl.gameObject,
            minWidth: VB_COL_LV_W, preferredWidth: VB_COL_LV_W, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        statsLbl.enableWordWrapping = false;
        statsLbl.overflowMode = TextOverflowModes.Overflow;

        // Shiny column — ALWAYS rendered (with "—" placeholder when not
        // shiny) so the box and Summon columns stay column-aligned across
        // rows. Pre-0.10.10 the column was omitted entirely when not
        // shiny, which collapsed the layout for that row only.
        string schoolTxt = instance.IsShiny
            ? (string.IsNullOrEmpty(instance.ShinySchool) ? "★" : $"★ {instance.ShinySchool}")
            : $"<color={Theme.MutedBodyHex}>—</color>";
        var schoolLbl = UIFactory.CreateLabel(row, "School", schoolTxt,
            TextAlignmentOptions.Midline, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(schoolLbl.GameObject,
            minWidth: VB_COL_SHINY_W, preferredWidth: VB_COL_SHINY_W, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        schoolLbl.TextMesh.fontStyle = FontStyles.Italic;
        schoolLbl.TextMesh.enableWordWrapping = false;
        schoolLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // Box column.
        var boxLbl = AddInfoLabel(row, "Box", instance.Box,
            FontStyles.Italic, fontSize: Theme.ScaledUI(11));
        boxLbl.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(boxLbl.gameObject,
            minWidth: VB_COL_BOX_W, preferredWidth: VB_COL_BOX_W, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        boxLbl.enableWordWrapping = false;
        boxLbl.overflowMode = TextOverflowModes.Ellipsis;

        // Summon button — fixed column.
        var summonBtn = UIFactory.CreateButton(row, "Summon", "Summon");
        UIFactory.SetLayoutElement(summonBtn.GameObject,
            minWidth: VB_COL_SUMMON_W, preferredWidth: VB_COL_SUMMON_W, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var sbt = summonBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (sbt != null) { sbt.fontSize = Theme.ScaledUI(11); sbt.alignment = TextAlignmentOptions.Center; }
        string capturedName    = name;
        bool   capturedShiny   = instance.IsShiny;
        bool   capturedPrimal  = instance.IsPrimal;
        summonBtn.OnClick = () => OnVBSummonVariantClicked(capturedName, capturedShiny, capturedPrimal);
    }

    private void BuildVBMissingRow(GameObject parent, string name)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"VBMissingRow_{name}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: VB_ROW_SPACING, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        // Type column.
        var tag = UIFactory.CreateLabel(row, "Tag", $"<color={VB_MISSING_HEX}>—</color>",
            TextAlignmentOptions.Midline, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(tag.GameObject,
            minWidth: VB_COL_TAG_W, preferredWidth: VB_COL_TAG_W, flexibleWidth: 0,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);

        // Name column.
        var nameLbl = UIFactory.CreateLabel(row, "Name",
            $"<color={VB_MISSING_HEX}>{name}</color>",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(nameLbl.GameObject,
            minWidth: 100, preferredWidth: 140, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        nameLbl.TextMesh.fontStyle = FontStyles.Italic;
        nameLbl.TextMesh.enableWordWrapping = false;
        nameLbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;

        // Empty Lv / Shiny / Box columns reserved so the row column-aligns
        // with captured rows even when there's no data to display.
        void EmptyCol(string label, int w)
        {
            var l = UIFactory.CreateLabel(row, label,
                $"<color={VB_MISSING_HEX}>—</color>",
                TextAlignmentOptions.Midline, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(l.GameObject,
                minWidth: w, preferredWidth: w, flexibleWidth: 0,
                minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
            l.TextMesh.fontStyle = FontStyles.Italic;
        }
        EmptyCol("LvEmpty",    VB_COL_LV_W);
        EmptyCol("ShinyEmpty", VB_COL_SHINY_W);
        EmptyCol("BoxEmpty",   VB_COL_BOX_W);

        // Status label in the Summon-button column slot.
        var statusLbl = UIFactory.CreateLabel(row, "Status",
            $"<color={VB_MISSING_HEX}>not captured</color>",
            TextAlignmentOptions.Midline, color: null, fontSize: Theme.ScaledUI(10));
        UIFactory.SetLayoutElement(statusLbl.GameObject,
            minWidth: VB_COL_SUMMON_W, preferredWidth: VB_COL_SUMMON_W, flexibleWidth: 0,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        statusLbl.TextMesh.fontStyle = FontStyles.Italic;
        statusLbl.TextMesh.enableWordWrapping = false;
        statusLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    private void OnVBSummonVariantClicked(string name, bool isShiny, bool isPrimal)
    {
        if (!_vbSummonStatusSubscribed)
        {
            Services.VBloodSummonService.StatusChanged += OnVBSummonStatusChanged;
            _vbSummonStatusSubscribed = true;
        }
        Services.VBloodSummonService.SummonVariant(name, isShiny, isPrimal);
    }

    private bool _vbSummonStatusSubscribed;

    private void OnVBSummonStatusChanged(string status)
    {
        if (_vbScanStatusLabel == null) return;
        _vbScanStatusLabel.text = status;
        if (!_vbScanStatusLabel.gameObject.activeSelf) _vbScanStatusLabel.gameObject.SetActive(true);
    }

    private void BuildExpertiseTab(GameObject page)
    {
        // 0.10.9: card-wrapped current state for visual breathing room.
        var currentCard = AddCard(page, "WepCurrentCard", Theme.SystemTintExpertise);
        AddSectionHeading(currentCard, "Current Weapon Expertise");

        _wepTypeLabel     = AddInfoLabel(currentCard, "WepType",     "—",                  FontStyles.Bold,   fontSize: Theme.ScaledUI(18));
        _wepProgressLabel = AddInfoLabel(currentCard, "WepProgress", "Level —",            FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _wepBonusLabel    = AddInfoLabel(currentCard, "WepBonus",    "Bonus Stats: —",     FontStyles.Normal, fontSize: Theme.ScaledUI(13));
        // 0.17.3: missing-expertise-stats nudge (set in RenderExpertise; hidden otherwise).
        _wepHintLabel     = AddInfoLabel(currentCard, "WepHint", string.Empty, FontStyles.Italic, fontSize: Theme.ScaledUI(13));
        _wepHintLabel.gameObject.SetActive(false);
        _wepStatsValuesLabel = AddInfoLabel(currentCard, "WepStatsValues", "", FontStyles.Italic, fontSize: Theme.ScaledUI(12));
        _wepStatsValuesLabel.gameObject.SetActive(false);
        _wepStatsValuesLabel.enableWordWrapping = true;
        _wepStatsValuesLabel.overflowMode = TextOverflowModes.Overflow;

        AddSpacer(page, 6);

        // 0.13.0: class-synergy hint card. Tells the user which weapon stats
        // their CURRENT class amplifies (1.5× cap) so the bonus-stat picker
        // below is informed by class context — no tab swap needed. Live-
        // updated by RenderClass when the player changes class.
        var classHintCard = AddCard(page, "WepClassHintCard");
        AddSectionHeading(classHintCard, "Class synergies");
        _wepClassSynergyLabel = AddContextBodyLabel(classHintCard, "WepClassSynergy",
            FormatClassWeaponSynergyHint(PlayerStateService.Experience.Class), fontSize: 13);
        CollapsibleSection.Build(classHintCard,
            title: "All stat caps + per-stat details",
            startExpanded: false,
            tooltip: "Reference list of every weapon expertise stat and its baseline cap at L100. Class synergy multiplies the cap by 1.5× for the four stats listed above.",
            buildContent: c =>
            {
                AddContextBodyLabel(c, "WepStatCapsBody",
                    "<b>Bonus stat caps</b> (baseline at expertise L100, before any prestige boost):\n" +
                    "  • Physical Power: <b>+20</b>\n" +
                    "  • Spell Power: <b>+10</b>\n" +
                    "  • Max Health: <b>+250</b>\n" +
                    "  • Movement Speed: <b>+25%</b>\n" +
                    "  • Primary Attack Speed: <b>+10%</b>\n" +
                    "  • Physical / Spell Crit Chance: <b>+10%</b>\n" +
                    "  • Physical / Spell Crit Damage: <b>+50%</b>\n" +
                    "  • Physical / Spell Life Leech: <b>+10%</b>\n" +
                    "  • Primary Life Leech: <b>+15%</b>\n\n" +
                    "Each weapon expertise prestige tier (max 10): <b>-10%</b> XP rate, <b>+10%</b> stat-cap boost. " +
                    "Reset a weapon's chosen stats with <b>.wep rst</b> (default cost: 500× Greater Stygian Shards).",
                    fontSize: 12);
            });
        AddServerDisclaimer(classHintCard);

        AddSpacer(page, 6);

        var actionsCard = AddCard(page, "WepActionsCard");
        AddSectionHeading(actionsCard, "Actions");
        var actions = UIFactory.CreateHorizontalGroup(actionsCard, "WepActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "Refresh",     MessageService.BCCOM_WEP_GET,
            "Display your current weapon expertise details in chat (.wep get).");
        AddCommandButton(actions, "List Weps",   MessageService.BCCOM_WEP_LIST,
            "List all weapon expertise types tracked by Bloodcraft (.wep l).");
        AddCommandButton(actions, "List Stats",  MessageService.BCCOM_WEP_LIST_STATS,
            "List the weapon-stat bonuses you can choose between (.wep lst).");
        AddCommandButton(actions, "Reset Stats", MessageService.BCCOM_WEP_RESET_STATS,
            "Reset your chosen bonus stats for the current weapon (.wep rst).");
        AddCommandButton(actions, "Lock Spells", MessageService.BCCOM_WEP_LOCK_SPELLS,
            "Lock in the next spells you equip to use as your unarmed slot spells (.wep locksp).");

        AddSpacer(page, 6);

        var statCard = AddCard(page, "WepStatPickerCard");
        AddSectionHeading(statCard, "Choose Bonus Stat");
        CollapsibleSection.Build(statCard,
            title: "Set bonus stat for a weapon (.wep cst)",
            startExpanded: false,
            tooltip: "Pick the weapon type AND the bonus stat you want to lock in for it. Bloodcraft applies the chosen stat scaled by your expertise level for that weapon. Each weapon tracks up to 3 chosen stats; submit again to add more (or use Reset Stats to clear).",
            buildContent: c => FormBuilder.Build(c,
                title: "Set bonus stat",
                commandTemplate: ".wep cst {weapon} {stat}",
                onSubmitted: () => EnqueueOrWarn(MessageService.BCCOM_WEP_GET),
                new EnumField<PlayerStateService.WeaponType>("weapon", "Weapon",
                    defaultValue: PlayerStateService.WeaponType.Sword,
                    tooltip: "Which weapon type the chosen bonus stat applies to."),
                new EnumIndexField<PlayerStateService.WeaponBonusStat>("stat", "Bonus stat",
                    defaultValue: PlayerStateService.WeaponBonusStat.PhysicalPower,
                    tooltip: "The stat to enhance. Bloodcraft expects a 1-12 index; the dropdown sends it for you.")));

        AddDivider(statCard);
        AddBodyText(statCard,
            $"Bloodcraft only streams the EQUIPPED weapon's expertise. Switch weapons to see each one's level + chosen stats above. The {Mono(".wep l")} button lists every weapon type you can level.");

        RenderExpertise(PlayerStateService.Expertise);
        if (!_wepSubscribed)
        {
            PlayerStateService.ExpertiseChanged += OnExpertiseChanged;
            _wepSubscribed = true;
        }
        if (!_wepLastResponseSubscribed)
        {
            PlayerStateService.LastResponseChanged += OnLastResponseChangedForWep;
            _wepLastResponseSubscribed = true;
        }
        // Seed from any LastResponse already on file in case the user opens
        // this tab AFTER a .wep get fired (e.g. the overlay's bonus-stats
        // ticker has been running, or they clicked Refresh and switched away
        // before the reply landed).
        var seed = PlayerStateService.LastResponse;
        if (seed.Command == ".wep get" && seed.Lines != null && seed.Lines.Count > 0)
        {
            _cachedWepGetLines = new System.Collections.Generic.List<string>(seed.Lines);
            RenderWepStatsValues();
        }
    }

    private void OnLastResponseChangedForWep()
    {
        var r = PlayerStateService.LastResponse;
        if (r.Command != ".wep get" || r.Lines == null) return;
        _cachedWepGetLines = new System.Collections.Generic.List<string>(r.Lines);
        RenderWepStatsValues();
        AutoResizeIfEnabled();
    }

    // 0.9.6: render the cached .wep get reply (raw color-tagged lines) into
    // the stats-values label. Hidden while we have no data so the tab stays
    // visually tidy on cold opens.
    private void RenderWepStatsValues()
    {
        if (_wepStatsValuesLabel == null) return;
        if (_cachedWepGetLines == null || _cachedWepGetLines.Count == 0)
        {
            if (_wepStatsValuesLabel.gameObject.activeSelf) _wepStatsValuesLabel.gameObject.SetActive(false);
            return;
        }
        _wepStatsValuesLabel.text = "• " + string.Join("\n• ", _cachedWepGetLines);
        if (!_wepStatsValuesLabel.gameObject.activeSelf) _wepStatsValuesLabel.gameObject.SetActive(true);
    }

    private void OnExpertiseChanged()
    {
        var e = PlayerStateService.Expertise;
        // 0.10.2: weapon-swap detection — fast-refresh the bonus-stat values
        // by zeroing the auto-fetch cooldown so the next TickTabAutoRefresh
        // sends .wep get immediately, instead of waiting up to 10s.
        if (_wepTabTypeBaseline && e.Type != _wepTabLastType)
        {
            _lastWepAutoFetchAt = 0;
            _cachedWepGetLines = null; // hide stale values while reply is in-flight
            if (_wepStatsValuesLabel != null) RenderWepStatsValues();
        }
        _wepTabLastType = e.Type;
        _wepTabTypeBaseline = true;
        RenderExpertise(e);
    }

    // -----------------------------------------------------------------------
    // Blood Legacy tab
    //
    // Mirrors the Weapon Expertise tab structure: a live "current blood" panel
    // up top fed by PlayerStateService.Legacy (already streamed via Eclipse),
    // chat-action buttons in the middle, and forms at the bottom for setting
    // the bonus stat per blood type and querying any blood's current state.
    //
    // Unlike .wep get (equipped-weapon-only), .bl get [BloodType] accepts an
    // explicit type argument server-side, so we expose a "Show info for blood"
    // form that lets you query a non-current blood's level + chosen stats.
    // -----------------------------------------------------------------------

    private void BuildBloodLegacyTab(GameObject page)
    {
        // 0.10.9: tinted card for current legacy state.
        var currentCard = AddCard(page, "BlCurrentCard", Theme.SystemTintLegacy);
        AddSectionHeading(currentCard, "Current Blood Legacy");

        _blTypeLabel     = AddInfoLabel(currentCard, "BlType",     "—",                  FontStyles.Bold,   fontSize: Theme.ScaledUI(18));
        _blTypeLabel.color = new Color(1f, 0.4f, 0.4f); // Bloodcraft uses red for blood headings
        ApplyStrongAccentOutline(_blTypeLabel);
        _blProgressLabel = AddInfoLabel(currentCard, "BlProgress", "Level —",            FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _blBonusLabel    = AddInfoLabel(currentCard, "BlBonus",    "Bonus Stats: —",     FontStyles.Normal, fontSize: Theme.ScaledUI(13));
        // 0.17.3: missing-legacy-stats nudge (set in RenderBloodLegacy; hidden otherwise).
        _blHintLabel     = AddInfoLabel(currentCard, "BlHint", string.Empty, FontStyles.Italic, fontSize: Theme.ScaledUI(13));
        _blHintLabel.gameObject.SetActive(false);
        _blStatsValuesLabel = AddInfoLabel(currentCard, "BlStatsValues", "", FontStyles.Italic, fontSize: Theme.ScaledUI(12));
        _blStatsValuesLabel.gameObject.SetActive(false);
        ApplyStrongAccentOutline(_blStatsValuesLabel);
        _blStatsValuesLabel.enableWordWrapping = true;
        _blStatsValuesLabel.overflowMode = TextOverflowModes.Overflow;

        AddSpacer(page, 6);

        // 0.13.0: class-synergy hint card — companion to the Weapon Expertise
        // hint. Shows which BLOOD stats the current class amplifies (1.5×
        // cap). Same RenderClass call updates this and the Expertise version
        // when the user changes class.
        var classHintCard = AddCard(page, "BlClassHintCard");
        AddSectionHeading(classHintCard, "Class synergies");
        _blClassSynergyLabel = AddContextBodyLabel(classHintCard, "BlClassSynergy",
            FormatClassBloodSynergyHint(PlayerStateService.Experience.Class), fontSize: 13);
        CollapsibleSection.Build(classHintCard,
            title: "All stat caps + per-stat details",
            startExpanded: false,
            tooltip: "Reference list of every blood legacy stat and its baseline cap at L100. Class synergy multiplies the cap by 1.5× for the four stats listed above.",
            buildContent: c =>
            {
                AddContextBodyLabel(c, "BlStatCapsBody",
                    "<b>Bonus stat caps</b> (baseline at legacy L100, before any prestige boost):\n" +
                    "  • Healing Received: <b>+15%</b>\n" +
                    "  • Damage Reduction: <b>+5%</b>\n" +
                    "  • Physical / Spell Resistance: <b>+10%</b>\n" +
                    "  • Resource Yield: <b>+25%</b>\n" +
                    "  • Reduced Blood Drain: <b>+50%</b>\n" +
                    "  • Weapon / Spell Cooldown Recovery: <b>+10%</b>\n" +
                    "  • Ultimate Cooldown Recovery: <b>+20%</b>\n" +
                    "  • Minion Damage: <b>+25%</b>\n" +
                    "  • Ability Attack Speed: <b>+10%</b>\n" +
                    "  • Corruption Damage Reduction: <b>+10%</b>\n\n" +
                    "Each blood legacy prestige tier (max 10): <b>-10%</b> gain rate, <b>+10%</b> stat-cap boost. " +
                    "Reset a blood's chosen stats with <b>.bl rst</b> (default cost: 500× Greater Stygian Shards — same item as expertise reset).",
                    fontSize: 12);
            });
        AddServerDisclaimer(classHintCard);

        AddSpacer(page, 6);

        var actionsCard = AddCard(page, "BlActionsCard");
        AddSectionHeading(actionsCard, "Actions");
        var actions = UIFactory.CreateHorizontalGroup(actionsCard, "BlActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "Refresh",     MessageService.BCCOM_BL_GET,
            "Display your current blood legacy details in chat (.bl get).");
        AddCommandButton(actions, "List Bloods", MessageService.BCCOM_BL_LIST,
            "List all blood legacy types tracked by Bloodcraft (.bl l).");
        AddCommandButton(actions, "List Stats",  MessageService.BCCOM_BL_LIST_STATS,
            "List the blood-stat bonuses you can choose between (.bl lst).");
        AddCommandButton(actions, "Reset Stats", MessageService.BCCOM_BL_RESET_STATS,
            "Reset your chosen bonus stats for the current blood (.bl rst).");

        AddSpacer(page, 6);

        var statCard = AddCard(page, "BlStatPickerCard");
        AddSectionHeading(statCard, "Choose Bonus Stat");
        CollapsibleSection.Build(statCard,
            title: "Set bonus stat for a blood type (.bl cst)",
            startExpanded: false,
            tooltip: "Pick a blood type AND the bonus stat you want to lock in for it. Bloodcraft applies the chosen stat scaled by your legacy level for that blood. Each blood tracks up to 3 chosen stats; submit again to add more (or use Reset Stats to clear the current blood).",
            buildContent: c => FormBuilder.Build(c,
                title: "Set bonus stat",
                commandTemplate: ".bl cst {blood} {stat}",
                onSubmitted: () => EnqueueOrWarn(MessageService.BCCOM_BL_GET),
                new EnumField<PlayerStateService.BloodTypeChoice>("blood", "Blood",
                    defaultValue: PlayerStateService.BloodTypeChoice.Warrior,
                    tooltip: "Which blood type the chosen bonus stat applies to."),
                new EnumIndexField<PlayerStateService.BloodBonusStat>("stat", "Bonus stat",
                    defaultValue: PlayerStateService.BloodBonusStat.PhysicalResistance,
                    tooltip: "The stat to enhance. Bloodcraft expects a 1-12 index; the dropdown sends it for you.")));

        CollapsibleSection.Build(statCard,
            title: "Show info for a specific blood (.bl get [Blood])",
            startExpanded: false,
            tooltip: "Query any blood type's level + chosen stats — not just the one you currently have. Result is parsed and shown in the panel below; chat is also updated unless you've enabled 'Clear server messages'.",
            buildContent: c => FormBuilder.Build(c,
                title: "Show blood info",
                commandTemplate: ".bl get {blood}",
                new EnumField<PlayerStateService.BloodTypeChoice>("blood", "Blood",
                    defaultValue: PlayerStateService.BloodTypeChoice.Warrior,
                    tooltip: "Which blood type to inspect.")));

        AddDivider(statCard);
        AddBodyText(statCard,
            $"Unlike weapon expertise, {Mono(".bl get")} accepts a blood-type argument — so the 'Show info' form above can inspect ANY blood you've leveled, not just your current one.");

        AddSpacer(page, 6);
        BuildBloodInfoDisplay(page);

        RenderBloodLegacy(PlayerStateService.Legacy);
        if (!_blSubscribed)
        {
            PlayerStateService.LegacyChanged += OnLegacyChanged;
            _blSubscribed = true;
        }
        if (!_blInfoSubscribed)
        {
            PlayerStateService.BloodInfoChanged += OnBloodInfoChanged;
            _blInfoSubscribed = true;
        }
        RenderBloodInfo();
    }

    private void BuildBloodInfoDisplay(GameObject page)
    {
        var section = UIFactory.CreateVerticalGroup(page, "BloodInfoDisplay",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(6, 6, 6, 6));
        UIFactory.SetLayoutElement(section,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 80, flexibleHeight: 0);

        // The accent labels in this section render in Bloodcraft red over a
        // dark panel — fine on the panel background, but at lower opacity the
        // in-game world (often red-tinted in vampire areas) bleeds through
        // and the red-on-red kills contrast. ApplyStrongAccentOutline bumps
        // the per-character outline so the text stays legible. Friend-testing
        // feedback (v0.9.0): "pink or red text on the red background".
        _blInfoTitleLabel = AddInfoLabel(section, "BloodInfoTitle",
            "Blood Info", FontStyles.Bold | FontStyles.Italic, fontSize: Theme.ScaledUI(14));
        _blInfoTitleLabel.color = new Color(1f, 0.4f, 0.4f); // Bloodcraft red
        ApplyStrongAccentOutline(_blInfoTitleLabel);

        _blInfoLevelLabel = AddInfoLabel(section, "BloodInfoLevel",
            "(submit Show info above to populate)", FontStyles.Italic, fontSize: Theme.ScaledUI(12));

        // Stat lines preserve server <color=red> markup; the outline applies
        // globally to the label so the red/cyan/white tokens all get the
        // dark border.
        _blInfoStatsLabel = AddInfoLabel(section, "BloodInfoStats",
            "", FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        ApplyStrongAccentOutline(_blInfoStatsLabel);
        var fitter = _blInfoStatsLabel.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        _blInfoStatsLabel.enableWordWrapping = true;
        _blInfoStatsLabel.overflowMode = TextOverflowModes.Overflow;
    }

    private void OnBloodInfoChanged()
    {
        RenderBloodInfo();
        RenderBlStatsValues();
        AutoResizeIfEnabled();
    }

    // 0.9.6: render the structured stat-values line in the Blood Legacy tab
    // HEADER (separate from the full Blood Info display further down the
    // page, which renders any blood the user queries via the form). Only
    // shows when BloodInfoLatest matches the currently-equipped blood — if
    // the user just queried "Worker" but is currently using "Warrior", the
    // header keeps showing nothing rather than misleading values.
    private void RenderBlStatsValues()
    {
        if (_blStatsValuesLabel == null) return;
        var info = PlayerStateService.BloodInfoLatest;
        var leg = PlayerStateService.Legacy;
        bool currentMatches = !string.IsNullOrEmpty(info.BloodType)
                           && string.Equals(info.BloodType, leg.Type.ToString(), System.StringComparison.OrdinalIgnoreCase);
        bool haveLines = info.StatLines != null && info.StatLines.Count > 0;
        if (!currentMatches || !haveLines)
        {
            if (_blStatsValuesLabel.gameObject.activeSelf) _blStatsValuesLabel.gameObject.SetActive(false);
            return;
        }
        _blStatsValuesLabel.text = "• " + string.Join("\n• ", info.StatLines);
        if (!_blStatsValuesLabel.gameObject.activeSelf) _blStatsValuesLabel.gameObject.SetActive(true);
    }

    private void RenderBloodInfo()
    {
        if (_blInfoTitleLabel == null) return;
        var info = PlayerStateService.BloodInfoLatest;
        if (string.IsNullOrEmpty(info.BloodType))
        {
            _blInfoTitleLabel.text  = "Blood Info";
            _blInfoLevelLabel.text  = "(submit Show info above to populate)";
            _blInfoStatsLabel.text  = "";
            return;
        }
        _blInfoTitleLabel.text = $"{info.BloodType} Blood Info";
        _blInfoLevelLabel.text = info.Prestige > 0
            ? $"Level {info.Level}  Prestige {info.Prestige}   Essence {info.Essence}  ({info.ProgressPct}%)"
            : $"Level {info.Level}   Essence {info.Essence}  ({info.ProgressPct}%)";
        if (info.StatLines != null && info.StatLines.Count > 0)
            _blInfoStatsLabel.text = "• " + string.Join("\n• ", info.StatLines);
        else
            _blInfoStatsLabel.text = "(no stat lines parsed — Bloodcraft may not have sent any)";
    }

    private void OnLegacyChanged()
    {
        var l = PlayerStateService.Legacy;
        // 0.10.2: blood-swap fast refresh — same pattern as weapon.
        if (_blTabTypeBaseline && l.Type != _blTabLastType)
        {
            _lastBlAutoFetchAt = 0;
            // BloodInfoLatest survives — that's a different blood now, so
            // RenderBlStatsValues will hide the old values via its
            // "currentMatches" gate until the new .bl get reply lands.
        }
        _blTabLastType = l.Type;
        _blTabTypeBaseline = true;
        RenderBloodLegacy(l);
        // The current blood may have changed (player switched bloods); refresh
        // the stats-values header which gates on Legacy.Type matching
        // BloodInfoLatest.BloodType.
        RenderBlStatsValues();
    }

    private void RenderBloodLegacy(PlayerStateService.LegacyState s)
    {
        if (_blTypeLabel == null) return;
        _blTypeLabel.text = s.Type.ToString();
        _blProgressLabel.text = s.Prestige > 0
            ? $"Level {s.Level}  ({s.Progress * 100f:0.#}%)   Prestige {s.Prestige}"
            : $"Level {s.Level}  ({s.Progress * 100f:0.#}%)";

        var stats = PlayerStateService.DecodeBloodBonusStats(s.BonusStatsRaw);
        var named = new System.Collections.Generic.List<string>();
        foreach (var st in stats)
            if (st != PlayerStateService.BloodStatType.None) named.Add(st.ToString());
        _blBonusLabel.text = named.Count > 0
            ? $"Bonus Stats: {string.Join(", ", named)}"
            : "Bonus Stats: (none yet — use the form below to choose)";

        UpdateMissingHint(_blHintLabel, Services.ProgressionHints.LegacyHint());
    }

    // -----------------------------------------------------------------------
    // Unarmed + Shift Skill tab
    // -----------------------------------------------------------------------

    private void BuildUnarmedShiftTab(GameObject page)
    {
        // 0.10.11: card-wrap shift spell + unarmed expertise + actions.
        var shiftCard = AddCard(page, "ShiftSpellCard", Theme.SystemTintExpertise);
        AddSectionHeading(shiftCard, "Shift Spell");
        _shiftSpellLabel = AddInfoLabel(shiftCard, "ShiftSpell",
            "Equipped: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        AddSpacer(page, 6);

        var unarmedCard = AddCard(page, "UnarmedCard", Theme.SystemTintExpertise);
        AddSectionHeading(unarmedCard, "Unarmed Expertise");
        _unarmedStatusLabel = AddInfoLabel(unarmedCard, "UnarmedStatus",
            "Equip your fists (no weapon) to inspect unarmed expertise.",
            FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _unarmedBonusLabel = AddInfoLabel(unarmedCard, "UnarmedBonus",
            "Bonus Stats: —", FontStyles.Normal, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 6);

        var actionsCard = AddCard(page, "ShiftActionsCard");
        AddSectionHeading(actionsCard, "Actions");
        var actions = UIFactory.CreateHorizontalGroup(actionsCard, "UnarmedShiftActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "Toggle Shift",  MessageService.BCCOM_CLASS_TOGGLE_SHIFT,
            "Toggle whether your class spell is locked into the shift slot (.class shift).");
        AddCommandButton(actions, "Lock Spells",   MessageService.BCCOM_WEP_LOCK_SPELLS,
            "Lock in next-equipped spells for use in your unarmed slots (.wep locksp).");
        AddCommandButton(actions, "Refresh",       MessageService.BCCOM_WEP_GET,
            "Refresh weapon expertise details (.wep get). Chat receives the response.");

        AddDivider(actionsCard);
        AddBodyText(actionsCard,
            $"Choosing which class spell goes in the shift slot takes a number ({Mono(".class csp <#>")}). Use the " +
            "<b>Class</b> tab → <b>Choose class shift spell (.class csp)</b> form (0 = default spell, 1..N as listed by List Spells).");

        RenderUnarmedShift(PlayerStateService.Expertise, PlayerStateService.ShiftSpell);
        if (!_shiftSubscribed)
        {
            PlayerStateService.ExpertiseChanged  += OnExpertiseChangedForUnarmed;
            PlayerStateService.ShiftSpellChanged += OnShiftSpellChanged;
            _shiftSubscribed = true;
        }
    }

    private void OnExpertiseChangedForUnarmed()
        => RenderUnarmedShift(PlayerStateService.Expertise, PlayerStateService.ShiftSpell);
    private void OnShiftSpellChanged()
        => RenderUnarmedShift(PlayerStateService.Expertise, PlayerStateService.ShiftSpell);

    private void RenderUnarmedShift(
        PlayerStateService.ExpertiseState exp,
        PlayerStateService.ShiftSpellState shift)
    {
        if (_shiftSpellLabel == null) return;

        _shiftSpellLabel.text = shift.SpellIndex == 0
            ? "Equipped: (none)"
            : PrefabNameResolver.TryGet(shift.SpellIndex, out var spellName)
                ? $"Equipped: {spellName}"
                : $"Equipped: PrefabGUID {shift.SpellIndex}";

        bool unarmedEquipped = exp.Type == PlayerStateService.WeaponType.Unarmed;
        if (unarmedEquipped)
        {
            _unarmedStatusLabel.text = exp.Prestige > 0
                ? $"Level {exp.Level}   ({exp.Progress * 100f:0.#}%)   Prestige {exp.Prestige}"
                : $"Level {exp.Level}   ({exp.Progress * 100f:0.#}%)";

            var stats = PlayerStateService.DecodeWeaponBonusStats(exp.BonusStatsRaw);
            var named = new System.Collections.Generic.List<string>();
            foreach (var st in stats)
                if (st != PlayerStateService.WeaponStatType.None) named.Add(st.ToString());
            _unarmedBonusLabel.text = named.Count > 0
                ? $"Bonus Stats: {string.Join(", ", named)}"
                : "Bonus Stats: (none yet — pick one via .wep cst)";
        }
        else
        {
            _unarmedStatusLabel.text =
                $"Currently equipped: {exp.Type}. Equip your fists (unarm) to inspect unarmed expertise.";
            _unarmedBonusLabel.text = "Bonus Stats: —";
        }
    }

    // -----------------------------------------------------------------------
    // Prestige tab
    // -----------------------------------------------------------------------

    private void BuildPrestigeTab(GameObject page)
    {
        // 0.10.9: each system's prestige sits in its own tinted card so the
        // 4-system breakdown reads at a glance. Pre-0.10.9 the four lines
        // sat in a single padded VLG, distinguished only by their prose
        // text — visually mushy.
        AddSectionHeading(page, "Current Prestige");

        var xpCard = AddCard(page, "PrestigeXpCard", Theme.SystemTintXP, padding: 6, innerSpacing: 2);
        _prestigeXpLabel = AddInfoLabel(xpCard, "PrestigeXp",
            "Experience prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        var legacyCard = AddCard(page, "PrestigeLegacyCard", Theme.SystemTintLegacy, padding: 6, innerSpacing: 2);
        _prestigeLegacyLabel = AddInfoLabel(legacyCard, "PrestigeLegacy",
            "Blood legacy prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        var expertiseCard = AddCard(page, "PrestigeExpertiseCard", Theme.SystemTintExpertise, padding: 6, innerSpacing: 2);
        _prestigeExpertiseLabel = AddInfoLabel(expertiseCard, "PrestigeExpertise",
            "Weapon expertise prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        var famCard = AddCard(page, "PrestigeFamCard", Theme.SystemTintFamiliar, padding: 6, innerSpacing: 2);
        _prestigeFamLabel = AddInfoLabel(famCard, "PrestigeFam",
            "Familiar prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        // 0.16: Exo prestige card. Exo is NOT carried by the structured Eclipse
        // protocol like the four systems above — it comes from the parsed
        // ".prestige get Exo" chat reply (PlayerStateService.PrestigeInfoLatest),
        // auto-fetched once below so the card fills without the user running the
        // command. The Experience overlay has its own EXO line; this is the
        // in-panel counterpart, NOT a duplicate of it.
        var exoCard = AddCard(page, "PrestigeExoCard", Theme.SystemTintXP, padding: 6, innerSpacing: 2);
        _prestigeExoLabel = AddInfoLabel(exoCard, "PrestigeExo",
            "Exo prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        AddSpacer(page, 6);
        AddSectionHeading(page, "Quick actions");

        var actions = UIFactory.CreateHorizontalGroup(page, "PrestigeActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "List",      MessageService.BCCOM_PRESTIGE_LIST,
            "List the prestige systems available on this server (.prestige l). Response in chat.");
        AddCommandButton(actions, "Sync Buffs",MessageService.BCCOM_PRESTIGE_SYNC_BUFFS,
            "Re-apply your prestige buffs if any have dropped (.prestige sb).");
        AddCommandButton(actions, "Exoform",   MessageService.BCCOM_PRESTIGE_TOGGLE_EXOFORM,
            "Toggle taunting to enter exoform shapeshift (.prestige exoform). Requires Exo prestige.");
        AddCommandButton(actions, "Shroud",    MessageService.BCCOM_PRESTIGE_TOGGLE_SHROUD,
            "Toggle permashroud if you qualify for it (.prestige shroud).");

        AddSpacer(page, 4);
        AddSectionHeading(page, "Prestige actions (forms)");

        CollapsibleSection.Build(page,
            title: "Prestige in a system (.prestige me)",
            startExpanded: false,
            tooltip: "Expand to prestige in a specific system — Experience, a weapon expertise, a blood legacy, or Exo.",
            buildContent: c => FormBuilder.Build(c,
                title: "Prestige in a system",
                commandTemplate: ".prestige me {type}",
                new EnumField<PlayerStateService.PrestigeType>("type", "Prestige type",
                    defaultValue: PlayerStateService.PrestigeType.Experience,
                    tooltip: "Which leveling system to prestige in. You must be at max level in that system.")));

        CollapsibleSection.Build(page,
            title: "Show prestige info (.prestige get)",
            startExpanded: false,
            tooltip: "Expand, pick a system, Submit. The reply is parsed and rendered in-panel below — chat is also updated unless you've enabled 'Clear server messages' in settings.",
            buildContent: c => FormBuilder.Build(c,
                title: "Show prestige info",
                commandTemplate: ".prestige get {type}",
                new EnumField<PlayerStateService.PrestigeType>("type", "Prestige type",
                    defaultValue: PlayerStateService.PrestigeType.Experience)));

        AddSpacer(page, 4);
        BuildPrestigeInfoDisplay(page);

        CollapsibleSection.Build(page,
            title: "Leaderboard (.prestige lb)",
            startExpanded: false,
            tooltip: "Expand to view the prestige leaderboard for a system.",
            buildContent: c => FormBuilder.Build(c,
                title: "Prestige leaderboard",
                commandTemplate: ".prestige lb {type}",
                new EnumField<PlayerStateService.PrestigeType>("type", "Prestige type",
                    defaultValue: PlayerStateService.PrestigeType.Experience)));

        CollapsibleSection.Build(page,
            title: "Select exoform variant (.prestige sf)",
            startExpanded: false,
            tooltip: "Expand to switch between the Evolved Vampire and Corrupted Serpent exoform shapeshifts.",
            buildContent: c => FormBuilder.Build(c,
                title: "Select exoform",
                commandTemplate: ".prestige sf {form}",
                new EnumField<PlayerStateService.ExoformVariant>("form", "Exoform",
                    defaultValue: PlayerStateService.ExoformVariant.EvolvedVampire,
                    tooltip: "EvolvedVampire or CorruptedSerpent.")));

        // 0.13.0: prestige progression reference card. Surfaces what each
        // prestige tier gives the player AND what to look forward to as
        // they continue prestiging. Always present beneath the prestige
        // forms so the user doesn't need to swap to Mod Help to remember
        // the per-tier math while filling in a .prestige me form.
        AddSpacer(page, 6);
        var progressionCard = AddCard(page, "PrestigeProgressionCard");
        AddSectionHeading(progressionCard, "What each prestige tier gives you");
        AddContextBodyLabel(progressionCard, "PrestigeProgressionIntro",
            "Bloodcraft has up to <b>10 tiers</b> per system (Experience / each weapon / each blood) — plus <b>100 Exo tiers</b> on top of leveling-prestige.",
            fontSize: 13);
        CollapsibleSection.Build(progressionCard,
            title: "Leveling (Experience) prestige — per tier",
            startExpanded: false,
            tooltip: "What you get every time you complete .prestige me Experience.",
            buildContent: c =>
            {
                AddContextBodyLabel(c, "LevelingPrestigeBody",
                    "Per tier (max 10):\n" +
                    "  • Resets character level back to <b>0</b>.\n" +
                    "  • Permanent leveling buff (server-tunable; varies by Bloodcraft version).\n" +
                    "  • <b>-5%</b> XP gain from kills (LevelingPrestigeReducer = 0.05).\n" +
                    "  • <b>+10%</b> rate boost to weapon expertise + blood legacy XP (PrestigeRateMultiplier = 0.10).\n" +
                    "  • Unlocks one additional class-spell slot (PrestigeLevelsToUnlockClassSpells = 0..5, one per tier).\n\n" +
                    "Net: each tier slows your raw level XP slightly but accelerates expertise / legacy gain. " +
                    "By tier 10 you've traded <b>-50%</b> XP for <b>+100%</b> expertise + legacy gain rate.",
                    fontSize: 12);
            });
        CollapsibleSection.Build(progressionCard,
            title: "Weapon Expertise / Blood Legacy prestige — per tier",
            startExpanded: false,
            tooltip: "Per-system prestige using .prestige me <Weapon> or .prestige me <Blood>.",
            buildContent: c =>
            {
                AddContextBodyLabel(c, "ExpLegPrestigeBody",
                    "Per tier (max 10 per weapon, 10 per blood):\n" +
                    "  • Resets that specific system's level back to <b>0</b>.\n" +
                    "  • <b>-10%</b> XP rate for THAT weapon / blood (PrestigeRatesReducer = 0.10).\n" +
                    "  • <b>+10%</b> stat-bonus cap boost for THAT weapon's / blood's chosen stats (PrestigeStatMultiplier = 0.10).\n\n" +
                    "By tier 10: stat caps are <b>×2.0</b> their baseline for that weapon / blood, but levelling that weapon / blood takes about twice as long.",
                    fontSize: 12);
            });
        CollapsibleSection.Build(progressionCard,
            title: "Exo Prestige — endgame tier (after max Experience prestige)",
            startExpanded: false,
            tooltip: "Unlocks at maxed Experience prestige + level 90. The highest tier of Bloodcraft progression.",
            buildContent: c =>
            {
                AddContextBodyLabel(c, "ExoPrestigeBody",
                    "Up to <b>100 Exo prestige tiers</b> available once you've maxed leveling-prestige.\n" +
                    "  • Each Exo prestige resets your <b>character level back to 0</b> — you re-level to max " +
                    "every time (and feel the level-difference penalty again until you do). It's the Exo " +
                    "<i>prestige</i> tier that climbs and stays; your character level does NOT stay at max.\n" +
                    "  • Unlocks <b>Exoforms</b> — shapeshift between Evolved Vampire and Corrupted Serpent. " +
                    "Pick which form with <b>.prestige sf</b> (above); <b>.prestige exoform</b> triggers the " +
                    "transformation (taunt emote by default).\n" +
                    "    Form duration grows from <b>15s</b> at Exo 1 to roughly <b>180s</b> at Exo 100 " +
                    "    (formula: 15 + (165 ÷ 100) × exoLevel).\n" +
                    "  • Servers usually grant a reward at each Exo tier, but <b>what you get varies by server</b> " +
                    "— check with your server admin for details.",
                    fontSize: 12);
            });
        AddServerDisclaimer(progressionCard);

        RenderPrestige();
        RenderExoCardFromState();
        SchedulePrestigeExoFetch();
        if (!_prestigeSubscribed)
        {
            PlayerStateService.ExperienceChanged += OnAnyForPrestige;
            PlayerStateService.LegacyChanged     += OnAnyForPrestige;
            PlayerStateService.ExpertiseChanged  += OnAnyForPrestige;
            PlayerStateService.FamiliarChanged   += OnAnyForPrestige;
            _prestigeSubscribed = true;
        }

        if (!_prestigeInfoSubscribed)
        {
            PlayerStateService.PrestigeInfoChanged += OnPrestigeInfoChanged;
            _prestigeInfoSubscribed = true;
        }
        RenderPrestigeInfo();
    }

    private void BuildPrestigeInfoDisplay(GameObject page)
    {
        // 0.8.2: bumped spacing 2→8 and padding 6→12 so the parsed prestige
        // info ("Prestige Info" title, level line, effect lines) doesn't crowd
        // together at the top of the box. Friend-testing called this out as
        // "crammed" — the issue is more visible when the effect lines wrap.
        _prestigeInfoSection = UIFactory.CreateVerticalGroup(page, "PrestigeInfoDisplay",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 8, padding: new Vector4(12, 12, 10, 10));
        UIFactory.SetLayoutElement(_prestigeInfoSection,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 120, flexibleHeight: 0);

        _prestigeInfoTitleLabel = AddInfoLabel(_prestigeInfoSection, "PrestigeInfoTitle",
            "Prestige Info", FontStyles.Bold | FontStyles.Italic, fontSize: Theme.ScaledUI(16));
        _prestigeInfoTitleLabel.color = new Color(0.6f, 0.95f, 0.6f); // Bloodcraft #90EE90

        _prestigeInfoLevelLabel = AddInfoLabel(_prestigeInfoSection, "PrestigeInfoLevel",
            "(submit Show prestige info above to populate)", FontStyles.Italic, fontSize: Theme.ScaledUI(13));
        // 0.9.2: optional progress bar showing level / maxLevel. Visibility +
        // fill are pushed during RenderPrestigeInfo, which re-reads
        // Settings.ShowProgressBars so the toggle takes effect live.
        _prestigeBar = UI.Framework.CustomLib.Controls.MiniBar.Create(
            _prestigeInfoSection, "PrestigeBar", out _prestigeBarFill,
            fillColor: new Color(0.6f, 0.95f, 0.6f, 0.95f)); // matches the Bloodcraft #90EE90 title accent
        _prestigeBar.SetActive(false);

        // Multi-line "effects" label. Using ContentSizeFitter so however many
        // lines the server sends back render flush together — the parser emits
        // one effect per inbound chat line (color tags stripped).
        _prestigeInfoEffectsLabel = AddInfoLabel(_prestigeInfoSection, "PrestigeInfoEffects",
            "", FontStyles.Normal, fontSize: Theme.ScaledUI(13));
        var fitter = _prestigeInfoEffectsLabel.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        _prestigeInfoEffectsLabel.enableWordWrapping = true;
        _prestigeInfoEffectsLabel.overflowMode = TextOverflowModes.Overflow;
    }

    private void OnPrestigeInfoChanged()
    {
        RenderPrestigeInfo();
        RenderExoCardFromState();
        AutoResizeIfEnabled();
    }

    private void RenderPrestigeInfo()
    {
        if (_prestigeInfoTitleLabel == null) return;
        var info = PlayerStateService.PrestigeInfoLatest;
        if (string.IsNullOrEmpty(info.TypeName))
        {
            _prestigeInfoTitleLabel.text  = "Prestige Info";
            _prestigeInfoLevelLabel.text  = "(submit Show prestige info above to populate)";
            _prestigeInfoEffectsLabel.text = "";
            if (_prestigeBar != null && _prestigeBar.activeSelf) _prestigeBar.SetActive(false);
            return;
        }

        _prestigeInfoTitleLabel.text = $"{info.TypeName} Prestige Info";
        _prestigeInfoLevelLabel.text = info.MaxLevel > 0
            ? $"Current Prestige Level: {info.Level} / {info.MaxLevel}"
            : $"Current Prestige Level: {info.Level}";

        if (info.EffectLines != null && info.EffectLines.Count > 0)
            _prestigeInfoEffectsLabel.text = "• " + string.Join("\n• ", info.EffectLines);
        else
            _prestigeInfoEffectsLabel.text = "(no additional effect lines parsed)";

        // 0.9.2: progress bar. Only meaningful when MaxLevel > 0 (i.e. the
        // server reported a cap); otherwise hide so we don't show a bar
        // that never fills.
        bool showBar = Config.Settings.ShowProgressBars && info.MaxLevel > 0;
        if (_prestigeBar != null && _prestigeBar.activeSelf != showBar) _prestigeBar.SetActive(showBar);
        if (showBar)
            UI.Framework.CustomLib.Controls.MiniBar.SetProgress(_prestigeBarFill, info.Level / (float)info.MaxLevel);
    }

    private void OnAnyForPrestige() => RenderPrestige();

    private void RenderPrestige()
    {
        if (_prestigeXpLabel == null) return;
        _prestigeXpLabel.text        = $"Experience prestige: {PlayerStateService.Experience.Prestige}";
        _prestigeLegacyLabel.text    = $"Blood legacy prestige ({PlayerStateService.Legacy.Type}): {PlayerStateService.Legacy.Prestige}";
        _prestigeExpertiseLabel.text = $"Weapon expertise prestige ({PlayerStateService.Expertise.Type}): {PlayerStateService.Expertise.Prestige}";
        _prestigeFamLabel.text       = $"Familiar prestige ({(string.IsNullOrEmpty(PlayerStateService.Familiar.Name) ? "no familiar" : PlayerStateService.Familiar.Name)}): {PlayerStateService.Familiar.Prestige}";
    }

    // 0.16: Exo prestige isn't in the structured protocol — fill the card from
    // the parsed ".prestige get Exo" reply. PrestigeInfoLatest is shared across
    // all .prestige get queries, so only adopt it when it's actually the Exo
    // type; otherwise keep the last known exo values.
    private void RenderExoCardFromState()
    {
        var p = PlayerStateService.PrestigeInfoLatest;
        if (p.TypeName != null && string.Equals(p.TypeName, "Exo", System.StringComparison.OrdinalIgnoreCase))
        {
            _prestigeExoLevel    = p.Level;
            _prestigeExoMaxLevel = p.MaxLevel;
            _prestigeExoReceived = true;
        }
        if (_prestigeExoLabel == null) return;
        if (!_prestigeExoReceived)
            _prestigeExoLabel.text = "Exo prestige: —";
        else if (_prestigeExoLevel <= 0)
            _prestigeExoLabel.text = "Exo prestige: none yet";
        else if (_prestigeExoMaxLevel > 0)
            _prestigeExoLabel.text = $"Exo prestige: {_prestigeExoLevel} / {_prestigeExoMaxLevel}";
        else
            _prestigeExoLabel.text = $"Exo prestige: {_prestigeExoLevel}";
    }

    // 0.16: one-shot auto-fetch of ".prestige get Exo" when the Prestige tab is
    // built, deferred until MessageService has bound to the local character/user.
    // Mirrors ExperienceOverlayPanel.ScheduleExoFetch so the card populates with
    // no user action. (At most two fetches if the XP overlay is also open — both
    // are one-shot and harmless.)
    private void SchedulePrestigeExoFetch()
    {
        if (_prestigeExoFetchScheduled) return;
        _prestigeExoFetchScheduled = true;
        System.Action ticker = null;
        ticker = () =>
        {
            if (!MessageService.IsInitialized) return;
            Behaviors.CoreUpdateBehavior.Actions.Remove(ticker);
            if (_prestigeExoTicker == ticker) _prestigeExoTicker = null;
            try { MessageService.EnqueueMessage(".prestige get Exo"); }
            catch (System.Exception ex) { Utils.LogUtils.LogWarning($"PrestigeTab: auto .prestige get Exo failed — {ex.Message}"); }
        };
        _prestigeExoTicker = ticker;   // 0.67.1: tracked so Reset() can remove it if it never fired
        Behaviors.CoreUpdateBehavior.Actions.Add(ticker);
    }

    // -----------------------------------------------------------------------
    // Experience Levels overview tab (read-only summary across systems)
    // -----------------------------------------------------------------------

    private void BuildLevelsTab(GameObject page)
    {
        // 0.10.9: each progression system gets a tinted card so the four
        // streams (XP / Legacy / Expertise / Familiar) read as visually
        // distinct rather than four indistinguishable label stacks. Tints
        // are 6%-alpha washes over the card; text remains full-contrast.

        // ── Player Experience ───────────────────────────────────────────
        var xpCard = AddCard(page, "LvlXpCard", Theme.SystemTintXP);
        AddSectionHeading(xpCard, "Player Experience");
        _lvlXpLabel = AddInfoLabel(xpCard, "LvlXp", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 6);

        // ── Blood Legacy ────────────────────────────────────────────────
        var legacyCard = AddCard(page, "LvlLegacyCard", Theme.SystemTintLegacy);
        AddSectionHeading(legacyCard, "Blood Legacy");
        _lvlLegacyLabel = AddInfoLabel(legacyCard, "LvlLegacy", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 6);

        // ── Weapon Expertise ────────────────────────────────────────────
        var expertiseCard = AddCard(page, "LvlExpertiseCard", Theme.SystemTintExpertise);
        AddSectionHeading(expertiseCard, "Weapon Expertise (active weapon)");
        _lvlExpertiseLabel      = AddInfoLabel(expertiseCard, "LvlExpertise",      "—", FontStyles.Normal, fontSize: Theme.ScaledUI(13));
        _lvlExpertiseBonusLabel = AddInfoLabel(expertiseCard, "LvlExpertiseBonus", "Bonus stats: —", FontStyles.Italic, fontSize: Theme.ScaledUI(12));

        // Bloodcraft's Eclipse protocol only streams the currently-equipped
        // weapon's expertise level — no per-weapon snapshot. Surface a
        // "List Weapons" button + a muted explanation so the user isn't
        // left wondering why only one weapon shows.
        AddBodyText(expertiseCard,
            $"Bloodcraft streams only the currently-equipped weapon. Swap weapons to update the row above, or use {Mono(".wep l")} for the full list (reply lands in chat).");
        var allWepRow = UIFactory.CreateHorizontalGroup(expertiseCard, "AllWepRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(allWepRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        AddCommandButton(allWepRow, "List Weapon Types", MessageService.BCCOM_WEP_LIST,
            "Sends .wep l. Bloodcraft replies in chat with the list of weapon types you can level.");

        AddSpacer(page, 6);

        // ── Familiar (active) ───────────────────────────────────────────
        var famCard = AddCard(page, "LvlFamCard", Theme.SystemTintFamiliar);
        AddSectionHeading(famCard, "★  Familiar (active)");
        _lvlFamLabel      = AddInfoLabel(famCard, "LvlFam",      "—", FontStyles.Normal, fontSize: Theme.ScaledUI(13));
        _lvlFamStatsLabel = AddInfoLabel(famCard, "LvlFamStats", "HP —   PP —   SP —", FontStyles.Italic, fontSize: Theme.ScaledUI(12));

        AddSpacer(page, 6);

        // ── Professions ─────────────────────────────────────────────────
        var profCard = AddCard(page, "LvlProfCard", Theme.SystemTintProfession);
        AddSectionHeading(profCard, "Professions");
        _lvlProfessions1Label = AddInfoLabel(profCard, "LvlProf1", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        _lvlProfessions2Label = AddInfoLabel(profCard, "LvlProf2", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        _lvlProfessions3Label = AddInfoLabel(profCard, "LvlProf3", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        _lvlProfessions4Label = AddInfoLabel(profCard, "LvlProf4", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(12));

        AddSpacer(page, 6);

        // ── Profession Tools ────────────────────────────────────────────
        var profToolsCard = AddCard(page, "LvlProfToolsCard");
        AddSectionHeading(profToolsCard, "Profession Tools");
        var profRow = UIFactory.CreateHorizontalGroup(profToolsCard, "ProfRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(profRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(profRow, "List",          MessageService.BCCOM_PROF_LIST,
            "List the professions Bloodcraft tracks (.prof l). Reply in chat.");
        AddCommandButton(profRow, "Toggle Log",    MessageService.BCCOM_PROF_LOG_TOGGLE,
            "Toggle in-chat profession-progress logging (.prof log). SERVER-side toggle.");

        CollapsibleSection.Build(profToolsCard,
            title: "Show profession progress (.prof get)",
            startExpanded: false,
            tooltip: "Displays your current level + progress for the chosen profession in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Show profession",
                commandTemplate: ".prof get {profession}",
                new EnumField<PlayerStateService.BloodcraftProfession>("profession", "Profession",
                    defaultValue: PlayerStateService.BloodcraftProfession.Enchanting,
                    tooltip: "Which profession to inspect.")));

        AddSpacer(page, 6);

        // ── Player Tools ────────────────────────────────────────────────
        var playerToolsCard = AddCard(page, "LvlPlayerToolsCard");
        AddSectionHeading(playerToolsCard, "Player Tools");
        var toolsRow1 = UIFactory.CreateHorizontalGroup(playerToolsCard, "PlayerToolsRow1",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(toolsRow1,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(toolsRow1, "User Stats",   MessageService.BCCOM_MISC_USER_STATS,
            "Print a summary of player stats in chat (.misc userstats).");
        AddCommandButton(toolsRow1, "Toggle XP Log", MessageService.BCCOM_LVL_LOG_TOGGLE,
            "Toggle in-chat logging of leveling-progress messages (.lvl log). SERVER-side toggle — Bloodcraft replies with the new state in chat.");
        AddCommandButton(toolsRow1, "Reminders",    MessageService.BCCOM_MISC_REMINDERS,
            "Toggle general feature reminders (.misc remindme). SERVER-side toggle.");
        AddCommandButton(toolsRow1, "Silence",      MessageService.BCCOM_MISC_SILENCE,
            "Reset stuck combat music if it won't stop (.misc silence).");

        var toolsRow2 = UIFactory.CreateHorizontalGroup(playerToolsCard, "PlayerToolsRow2",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(toolsRow2,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(toolsRow2, "Starter Kit",  MessageService.BCCOM_MISC_KIT_ME,
            "Claim the server's starter kit (.misc kitme). One-time on most servers.");
        AddCommandButton(toolsRow2, "Prepare Hunt",  MessageService.BCCOM_MISC_PREPARE,
            "Auto-complete the GettingReadyForTheHunt quest if it's stuck (.misc prepare).");

        CollapsibleSection.Build(playerToolsCard,
            title: "Toggle scrolling combat text (.misc sct)",
            startExpanded: false,
            tooltip: "Enable or disable a specific scrolling-combat-text element. Bloodcraft replies with the new state in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Toggle SCT",
                commandTemplate: ".misc sct {type}",
                new TextField("type", "SCT element type",
                    tooltip: "Element name (e.g. 'damage', 'heal'). Bloodcraft's reply tells you the new state.")));

        AddDivider(playerToolsCard);
        AddBodyText(playerToolsCard,
            $"Heads up: most of these are server-side TOGGLES — Bloodcraft flips a flag and reports the new state in chat. The client can't 'remember' the new state across sessions because the server is the source of truth (same with {Mono(".fam t")} / {Mono(".lvl log")} elsewhere).");

        RenderLevels();
        if (!_lvlSubscribed)
        {
            PlayerStateService.ExperienceChanged += OnAnyForLevels;
            PlayerStateService.LegacyChanged     += OnAnyForLevels;
            PlayerStateService.ExpertiseChanged  += OnAnyForLevels;
            PlayerStateService.FamiliarChanged   += OnAnyForLevels;
            PlayerStateService.ProfessionChanged += OnAnyForLevels;
            _lvlSubscribed = true;
        }
    }

    private void OnAnyForLevels() => RenderLevels();

    private void RenderLevels()
    {
        if (_lvlXpLabel == null) return;
        var exp = PlayerStateService.Experience;
        var leg = PlayerStateService.Legacy;
        var wep = PlayerStateService.Expertise;
        var fam = PlayerStateService.Familiar;
        var pro = PlayerStateService.Profession;

        _lvlXpLabel.text = exp.Prestige > 0
            ? $"Level {exp.Level} ({exp.Progress * 100f:0.#}%)   Prestige {exp.Prestige}   Class: {exp.Class}"
            : $"Level {exp.Level} ({exp.Progress * 100f:0.#}%)   Class: {exp.Class}";

        _lvlLegacyLabel.text = leg.Prestige > 0
            ? $"{leg.Type}   Level {leg.Level} ({leg.Progress * 100f:0.#}%)   Prestige {leg.Prestige}"
            : $"{leg.Type}   Level {leg.Level} ({leg.Progress * 100f:0.#}%)";

        _lvlExpertiseLabel.text = wep.Prestige > 0
            ? $"{wep.Type}   Level {wep.Level} ({wep.Progress * 100f:0.#}%)   Prestige {wep.Prestige}"
            : $"{wep.Type}   Level {wep.Level} ({wep.Progress * 100f:0.#}%)";

        var wepStats = PlayerStateService.DecodeWeaponBonusStats(wep.BonusStatsRaw);
        var wepNamed = new System.Collections.Generic.List<string>();
        foreach (var s in wepStats)
            if (s != PlayerStateService.WeaponStatType.None) wepNamed.Add(s.ToString());
        _lvlExpertiseBonusLabel.text = wepNamed.Count > 0
            ? $"Bonus stats: {string.Join(", ", wepNamed)}"
            : "Bonus stats: (none yet — choose via .wep cst)";

        // 0.10.8: HasActive is sourced from the raw Eclipse protocol name
        // field. Pre-0.10.8 the Level > 0 || !empty(Name) check was always
        // true because EclipseProtocolService defaults Name to "Familiar"
        // and floors Level to 1 — so the "(no familiar bound)" branch was
        // unreachable and the Levels tab always rendered "Familiar Lv 1".
        bool famActive = fam.HasActive;
        _lvlFamLabel.text = famActive
            ? (fam.Prestige > 0
                ? $"{fam.Name}   Level {fam.Level} ({fam.Progress * 100f:0.#}%)   Prestige {fam.Prestige}"
                : $"{fam.Name}   Level {fam.Level} ({fam.Progress * 100f:0.#}%)")
            : "(no familiar bound)";
        _lvlFamStatsLabel.text = famActive
            ? $"HP {fam.MaxHealth}   PP {fam.PhysicalPower}   SP {fam.SpellPower}"
            : "HP —   PP —   SP —";

        _lvlProfessions1Label.text = $"Enchanting    Lv {pro.EnchantingLevel:00} ({pro.EnchantingProgress * 100f:0.#}%)        Alchemy        Lv {pro.AlchemyLevel:00} ({pro.AlchemyProgress * 100f:0.#}%)";
        _lvlProfessions2Label.text = $"Harvesting    Lv {pro.HarvestingLevel:00} ({pro.HarvestingProgress * 100f:0.#}%)        Blacksmithing  Lv {pro.BlacksmithingLevel:00} ({pro.BlacksmithingProgress * 100f:0.#}%)";
        _lvlProfessions3Label.text = $"Tailoring     Lv {pro.TailoringLevel:00} ({pro.TailoringProgress * 100f:0.#}%)        Woodcutting    Lv {pro.WoodcuttingLevel:00} ({pro.WoodcuttingProgress * 100f:0.#}%)";
        _lvlProfessions4Label.text = $"Mining        Lv {pro.MiningLevel:00} ({pro.MiningProgress * 100f:0.#}%)        Fishing        Lv {pro.FishingLevel:00} ({pro.FishingProgress * 100f:0.#}%)";
    }

    // -----------------------------------------------------------------------
    // Daily Quest tab
    // -----------------------------------------------------------------------

    private TextMeshProUGUI _dqDailyTargetLabel;
    private TextMeshProUGUI _dqDailyProgressLabel;
    private TextMeshProUGUI _dqWeeklyTargetLabel;
    private TextMeshProUGUI _dqWeeklyProgressLabel;
    private bool _dqSubscribed;

    private void BuildDailyQuestTab(GameObject page)
    {
        // 0.10.11: card-wrap daily / weekly / settings sections.
        var dailyCard = AddCard(page, "DQDailyCard", Theme.SystemTintQuest);
        AddSectionHeading(dailyCard, "Daily Quest");
        _dqDailyTargetLabel   = AddInfoLabel(dailyCard, "DQDailyTarget",   "—", FontStyles.Bold,   fontSize: Theme.ScaledUI(15));
        _dqDailyTargetLabel.color = new Color(0f, 1f, 1f); // Bloodcraft cyan #00FFFF
        ApplyStrongAccentOutline(_dqDailyTargetLabel);
        _dqDailyProgressLabel = AddInfoLabel(dailyCard, "DQDailyProgress", "—", FontStyles.Italic, fontSize: Theme.ScaledUI(13));
        var dailyRow = UIFactory.CreateHorizontalGroup(dailyCard, "DQDailyActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(dailyRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(dailyRow, "Refresh",  MessageService.BCCOM_QUEST_PROGRESS_DAILY,
            "Print the daily quest objective into chat (.quest p d). The UI updates automatically when Bloodcraft pushes new state via the Eclipse protocol; this is for an explicit poll.");
        AddCommandButton(dailyRow, "Track",    MessageService.BCCOM_QUEST_TRACK_DAILY,
            "Print the location/direction to your daily target (.quest t d). Reply appears in chat.");
        AddCommandButton(dailyRow, "Reroll",   MessageService.BCCOM_QUEST_REROLL_DAILY,
            "Reroll the daily quest (.quest r d). Costs the server-configured reroll item; only works once the daily is complete OR if the server allows mid-quest rerolls.");

        AddSpacer(page, 6);

        var weeklyCard = AddCard(page, "DQWeeklyCard", Theme.SystemTintQuest);
        AddSectionHeading(weeklyCard, "Weekly Quest");
        _dqWeeklyTargetLabel   = AddInfoLabel(weeklyCard, "DQWeeklyTarget",   "—", FontStyles.Bold,   fontSize: Theme.ScaledUI(15));
        _dqWeeklyTargetLabel.color = new Color(1f, 0.85f, 0.3f);
        ApplyStrongAccentOutline(_dqWeeklyTargetLabel);
        _dqWeeklyProgressLabel = AddInfoLabel(weeklyCard, "DQWeeklyProgress", "—", FontStyles.Italic, fontSize: Theme.ScaledUI(13));
        var weeklyRow = UIFactory.CreateHorizontalGroup(weeklyCard, "DQWeeklyActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(weeklyRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(weeklyRow, "Refresh", MessageService.BCCOM_QUEST_PROGRESS_WEEKLY,
            "Print the weekly quest objective into chat (.quest p w).");
        AddCommandButton(weeklyRow, "Track",   MessageService.BCCOM_QUEST_TRACK_WEEKLY,
            "Print the location/direction to your weekly target (.quest t w).");
        AddCommandButton(weeklyRow, "Reroll",  MessageService.BCCOM_QUEST_REROLL_WEEKLY,
            "Reroll the weekly quest (.quest r w). Costs the server-configured reroll item.");

        AddSpacer(page, 6);

        var settingsCard = AddCard(page, "DQSettingsCard");
        AddSectionHeading(settingsCard, "Settings");
        var setRow = UIFactory.CreateHorizontalGroup(settingsCard, "DQSettings",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(setRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(setRow, "Toggle Quest Log", MessageService.BCCOM_QUEST_LOG_TOGGLE,
            "Toggle in-chat progress logging (.quest log). When on, Bloodcraft prints a message each time you progress an objective.");

        AddDivider(settingsCard);
        AddBodyText(settingsCard,
            "Toggle the Daily Quest overlay from the panel footer to track progress in a small movable HUD.");

        RenderDailyQuestTab();
        if (!_dqSubscribed)
        {
            PlayerStateService.QuestChanged += OnQuestChangedForTab;
            _dqSubscribed = true;
        }
    }

    private void OnQuestChangedForTab() => RenderDailyQuestTab();

    private void RenderDailyQuestTab()
    {
        if (_dqDailyTargetLabel == null) return;
        var d = PlayerStateService.DailyQuest;
        var w = PlayerStateService.WeeklyQuest;
        // 0.15.1: when Quest is reliably detected as disabled server-side
        // (other Bloodcraft systems are flowing data but Quest stays
        // empty across the settling window), show a clearer hint than
        // "no quest yet — check back". Friend-test 0.15.0 surfaced users
        // staring at the placeholder indefinitely on a Quests-disabled
        // server with no signal that the feature was actually off.
        bool questDisabled = PlayerStateService.IsSystemReliablyDisabled(PlayerStateService.SystemKind.Quest);
        string dailyEmpty  = questDisabled
            ? "(Quests disabled on this server — the admin has Bloodcraft's QuestSystem turned off)"
            : "(no daily quest yet — check back after the next refresh)";
        string weeklyEmpty = questDisabled
            ? "(Quests disabled on this server — see daily row above)"
            : "(no weekly quest yet — check back after the next refresh)";
        FormatQuestRow(_dqDailyTargetLabel,  _dqDailyProgressLabel,  d, dailyEmpty);
        FormatQuestRow(_dqWeeklyTargetLabel, _dqWeeklyProgressLabel, w, weeklyEmpty);
    }

    private static void FormatQuestRow(TextMeshProUGUI target, TextMeshProUGUI progress,
        PlayerStateService.QuestState s, string emptyHint)
    {
        bool hasQuest = !string.IsNullOrEmpty(s.TargetName) || s.Goal > 0;
        if (!hasQuest)
        {
            target.text   = emptyHint;
            progress.text = "";
            return;
        }
        target.text = s.IsVBlood
            ? $"{s.TargetName}  (V Blood)"
            : $"{s.TargetName}";
        if (s.Goal > 0 && s.Progress >= s.Goal)
        {
            progress.text  = "Complete!  Reroll for the next one.";
            progress.color = new Color(0.6f, 1f, 0.6f);
        }
        else
        {
            progress.text  = $"Progress: {s.Progress} / {s.Goal}";
            progress.color = Color.white;
        }
    }

    // -----------------------------------------------------------------------
    // Admin tab
    // -----------------------------------------------------------------------

    /// <summary>
    /// Renders an info note at the top of every admin tab explaining that the
    /// commands here require server-admin permission. 0.8.2: replaced the
    /// previous self-asserted "Is admin" gate (which needed a full game
    /// restart to actually surface the commands — `Settings.SetIsAdmin` flipped
    /// a bool but `ShowTab(ActiveTab)` didn't always rebuild the page). The
    /// gate added no security since the server enforces permissions anyway;
    /// non-admins clicking commands just get rejection messages.
    /// </summary>
    private void RenderAdminInfoNote(GameObject page, string contextLabel)
    {
        var msg = UIFactory.CreateLabel(page, "AdminInfoNote",
            $"<b>Admin only.</b> {contextLabel} commands require server-admin permission. " +
            "Non-admins can click these buttons, but the server will reject them with a " +
            "permission error. Nothing here can damage your client.",
            TextAlignmentOptions.TopLeft, color: new Color(1f, 0.85f, 0.5f), fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(msg.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 48, flexibleHeight: 0);
        msg.TextMesh.enableWordWrapping = true;
        msg.TextMesh.overflowMode = TextOverflowModes.Overflow;
        msg.TextMesh.richText = true;

        AddSpacer(page, 4);
    }

    private void BuildAdminTab(GameObject page)
    {
        page = BeginAdminGate(page);   // gray out + disable the admin controls below for non-admins
        // 0.10.12: wrap the admin note + diagnostics row in cards. The
        // forms below are collapsibles which are already self-contained
        // visual units; an outer card around the long form stack would
        // double-nest without adding value.
        var noteCard = AddCard(page, "AdminNoteCard");
        RenderAdminInfoNote(noteCard, "Bloodcraft admin");

        AddSpacer(page, 6);

        var diagCard = AddCard(page, "AdminDiagCard");
        AddSectionHeading(diagCard, "Server Diagnostics");
        var diagRow = UIFactory.CreateHorizontalGroup(diagCard, "AdminDiag",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(diagRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(diagRow, "Server Health", MessageService.BCCOM_MISC_HEALTH,
            "Show the Bloodcraft server's startup readiness summary in chat (.misc health). Admin only.");

        AddSpacer(page, 6);
        BuildBcResetCard(page);   // 0.73.0: Reset Bloodcraft progress

        AddSpacer(page, 6);
        AddSectionHeading(page, "Admin forms");
        page = BeginBcResetFormsLock(page);   // 0.73.0: the forms below are disabled while a reset runs

        // Collapsible form: header click toggles the form's content visibility.
        // Many forms collapsed by default keeps the Admin tab compact even after
        // Phase 5e migrates the remaining 8 reference lines.
        CollapsibleSection.Build(page,
            title: "Set player level (.lvl set)",
            startExpanded: false,
            tooltip: "Expand to set a player's character level. Admin only.",
            buildContent: content => FormBuilder.Build(content,
                title: "Set player level",
                commandTemplate: ".lvl set {player} {level}",
                new PlayerNameField("player", "Player",
                    tooltip: "Target player's character name (must match exactly)."),
                new IntField("level", "Level", min: 1, max: 200,
                    tooltip: "Target character level. Bloodcraft default cap is 90.")));

        CollapsibleSection.Build(page,
            title: "Toggle shared-XP exclusion (.lvl ignore)",
            startExpanded: false,
            tooltip: "Adds (or removes) a player from the list of those NOT eligible to receive shared experience. Toggle — Bloodcraft replies with the new state in chat.",
            buildContent: content => FormBuilder.Build(content,
                title: "Toggle shared-XP ignore",
                commandTemplate: ".lvl ignore {player}",
                new PlayerNameField("player", "Player",
                    tooltip: "Target player. The flip is reversible — call again to remove from the ignore list.")));

        // ---- 0.7.0 admin profession setter ----
        CollapsibleSection.Build(page,
            title: "Set player profession level (.prof set)",
            startExpanded: false,
            tooltip: "Set a player's profession level for the named profession.",
            buildContent: content => FormBuilder.Build(content,
                title: "Set profession",
                commandTemplate: ".prof set {player} {profession} {level}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.BloodcraftProfession>("profession", "Profession",
                    defaultValue: PlayerStateService.BloodcraftProfession.Enchanting),
                new IntField("level", "Level", min: 0, max: 100,
                    tooltip: "Target level. 0 resets.")));

        // Set prestige
        CollapsibleSection.Build(page,
            title: "Set player prestige (.prestige set)",
            startExpanded: false,
            tooltip: "Expand to set a player's prestige level in a specific system.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set player prestige",
                commandTemplate: ".prestige set {player} {type} {level}",
                new PlayerNameField("player", "Player",
                    tooltip: "Target player's character name."),
                new EnumField<PlayerStateService.PrestigeType>("type", "Prestige type",
                    defaultValue: PlayerStateService.PrestigeType.Experience,
                    tooltip: "Which prestige system to set."),
                new IntField("level", "Level", min: 0, max: 100,
                    tooltip: "Prestige level (0 to reset).")));

        // Reset prestige
        CollapsibleSection.Build(page,
            title: "Reset player prestige (.prestige r)",
            startExpanded: false,
            tooltip: "Expand to reset a player's prestige in a specific system.",
            buildContent: c => FormBuilder.Build(c,
                title: "Reset player prestige",
                commandTemplate: ".prestige r {player} {type}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.PrestigeType>("type", "Prestige type",
                    defaultValue: PlayerStateService.PrestigeType.Experience,
                    tooltip: "Which prestige system to reset.")));

        // ---- 0.7.0 prestige audit additions ----
        CollapsibleSection.Build(page,
            title: "Toggle prestige-leaderboard exclusion (.prestige ignore)",
            startExpanded: false,
            tooltip: "Adds (or removes) the player from the list of those who are HIDDEN from prestige leaderboards. Intended for admin/staff accounts.",
            buildContent: c => FormBuilder.Build(c,
                title: "Toggle leaderboard exclusion",
                commandTemplate: ".prestige ignore {player}",
                new PlayerNameField("player", "Player",
                    tooltip: "Toggle — calling again removes them from the exclusion list.")));

        CollapsibleSection.Build(page,
            title: "GLOBAL prestige-buff purge — DESTRUCTIVE",
            startExpanded: false,
            tooltip: "Removes prestige buffs from EVERY player on the server, so config-changed buffs can be re-applied cleanly. Cannot be undone in one click — every player would need to re-apply via .prestige sb. Required confirm.",
            buildContent: c => FormBuilder.Build(c,
                title: "Global prestige-buff purge",
                commandTemplate: MessageService.BCCOM_PRESTIGE_GLOBAL_BUFF_PURGE,
                new BoolField("confirm", "Yes, purge prestige buffs from EVERY player",
                    tooltip: "Required. Affects every player on the server — they each need to .prestige sb to re-apply.",
                    requireTrue: true)));

        // ---- 0.7.0 quest audit addition ----
        CollapsibleSection.Build(page,
            title: "Force-complete a player's quest (.quest c)",
            startExpanded: false,
            tooltip: "Marks a Daily or Weekly quest as complete for the named player without them having to fulfil the objective.",
            buildContent: c => FormBuilder.Build(c,
                title: "Force-complete quest",
                commandTemplate: ".quest c {player} {type}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.BloodcraftQuestType>("type", "Quest type",
                    defaultValue: PlayerStateService.BloodcraftQuestType.Daily,
                    tooltip: "Daily or Weekly.")));

        // Set blood legacy
        CollapsibleSection.Build(page,
            title: "Set blood legacy (.bl set)",
            startExpanded: false,
            tooltip: "Expand to set a player's blood legacy level.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set blood legacy",
                commandTemplate: ".bl set {player} {blood} {level}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.BloodType>("blood", "Blood",
                    defaultValue: PlayerStateService.BloodType.Warrior,
                    tooltip: "Worker / Warrior / Scholar / Rogue / Mutant / Draculin / Immortal / Creature / Brute / Corruption."),
                new IntField("level", "Level", min: 0, max: 100)));

        // Set weapon expertise
        CollapsibleSection.Build(page,
            title: "Set weapon expertise (.wep set)",
            startExpanded: false,
            tooltip: "Expand to set a player's weapon expertise level for a specific weapon.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set weapon expertise",
                commandTemplate: ".wep set {player} {weapon} {level}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.WeaponType>("weapon", "Weapon",
                    defaultValue: PlayerStateService.WeaponType.Sword),
                new IntField("level", "Level", min: 0, max: 100)));

        // Set profession
        CollapsibleSection.Build(page,
            title: "Set profession (.prof set)",
            startExpanded: false,
            tooltip: "Expand to set a player's level in a specific profession.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set profession",
                commandTemplate: ".prof set {player} {profession} {level}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.ProfessionType>("profession", "Profession",
                    defaultValue: PlayerStateService.ProfessionType.Mining),
                new IntField("level", "Level", min: 0, max: 100)));

        // Set familiar level
        CollapsibleSection.Build(page,
            title: "Set familiar level (.fam sl)",
            startExpanded: false,
            tooltip: "Expand to set a player's currently-bound familiar to a specific level.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set familiar level",
                commandTemplate: ".fam sl {player} {level}",
                new PlayerNameField("player", "Player",
                    tooltip: "Target player. Their currently-bound familiar is affected."),
                new IntField("level", "Level", min: 1, max: 100)));

        // Refresh quests
        CollapsibleSection.Build(page,
            title: "Refresh quests (.quest rf)",
            startExpanded: false,
            tooltip: "Expand to force-refresh a player's daily and weekly quests.",
            buildContent: c => FormBuilder.Build(c,
                title: "Refresh quests",
                commandTemplate: ".quest rf {player}",
                new PlayerNameField("player", "Player")));

        // Complete a quest
        CollapsibleSection.Build(page,
            title: "Complete quest (.quest c)",
            startExpanded: false,
            tooltip: "Expand to forcibly complete a player's daily or weekly quest.",
            buildContent: c => FormBuilder.Build(c,
                title: "Complete quest",
                commandTemplate: ".quest c {player} {schedule}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.QuestSchedule>("schedule", "Schedule",
                    defaultValue: PlayerStateService.QuestSchedule.Daily,
                    tooltip: "Daily or Weekly.")));

        AddSpacer(page, 6);
        AddBodyText(page,
            "All admin commands now have forms. If you aren't an admin on this server, commands return a permission error.");
    }

    // -----------------------------------------------------------------------
    // KindredLogistics tab (Kindred group)
    //
    // Surfaces the KindredLogistics server mod's 28 chat commands as buttons +
    // forms. KindredLogistics returns no structured data, so every control here
    // just fires-and-forgets - the server echoes confirmation/state into chat
    // (which the player can read with `.l s` / `.lg s` settings buttons).
    // -----------------------------------------------------------------------

    private void BuildKindredLogisticsTab(GameObject page)
    {
        // 0.10.12: card-wrap the intro + personal-toggles + utility sections.
        var introCard = AddCard(page, "KLIntroCard");
        AddBodyText(introCard,
            $"Requires the KindredLogistics server mod. Personal toggles affect only your character; admin globals affect the whole server (admin only). Personal toggles use {Mono(".l ...")}; admin globals use {Mono(".lg ...")}.");

        AddSpacer(page, 6);

        var personalCard = AddCard(page, "KLPersonalCard");
        AddSectionHeading(personalCard, "Personal Toggles (.l)");

        var pr1 = AddKLRow(personalCard, "KLPersonal1");
        AddCommandButton(pr1, "Sort Stash",     MessageService.BCCOM_KL_SORT_STASH,
            "Toggle auto-stash on double-click of the sort button (.l ss).");
        AddCommandButton(pr1, "Craft Pull",     MessageService.BCCOM_KL_CRAFT_PULL,
            "Toggle right-click on a recipe pulling missing ingredients (.l cr).");
        AddCommandButton(pr1, "Don't Pull Last", MessageService.BCCOM_KL_DONT_PULL_LAST,
            "Toggle never pulling the last item from a container (.l dpl).");
        AddCommandButton(pr1, "Servant Stash",  MessageService.BCCOM_KL_AUTOSTASH_MISSION,
            "Toggle auto-stash of servant mission rewards (.l asm).");

        var pr2 = AddKLRow(personalCard, "KLPersonal2");
        AddCommandButton(pr2, "Conveyor",      MessageService.BCCOM_KL_CONVEYOR,
            "Toggle named sender/receiver chests routing items between them (.l co).");
        AddCommandButton(pr2, "Salvage",       MessageService.BCCOM_KL_SALVAGE,
            "Toggle chests named 'salvage' auto-salvaging their contents (.l sal).");
        AddCommandButton(pr2, "Unit Spawner",  MessageService.BCCOM_KL_UNIT_SPAWNER,
            "Toggle chests named 'spawner' auto-filling unit stations (.l us).");
        AddCommandButton(pr2, "Brazier",       MessageService.BCCOM_KL_BRAZIER,
            "Toggle chests named 'brazier' auto-fueling braziers (.l bz).");

        var pr3 = AddKLRow(personalCard, "KLPersonal3");
        AddCommandButton(pr3, "Silent Pull",   MessageService.BCCOM_KL_SILENT_PULL,
            "Toggle suppressing chat messages when pulling items (.l sp).");
        AddCommandButton(pr3, "Silent Stash",  MessageService.BCCOM_KL_SILENT_STASH,
            "Toggle suppressing chat messages when stashing items (.l ssh).");
        AddCommandButton(pr3, "Show Settings", MessageService.BCCOM_KL_SETTINGS,
            "Print your current personal Logistics settings into chat (.l s).");

        AddSpacer(page, 6);

        var utilCard = AddCard(page, "KLUtilityCard");
        AddSectionHeading(utilCard, "Utility");
        var util = AddKLRow(utilCard, "KLUtility");
        AddCommandButton(util, "Stash All",    MessageService.BCCOM_KL_STASH_ALL,
            "Stash all items in your inventory into nearby chests (.stash).");

        CollapsibleSection.Build(utilCard,
            title: "Pull item from containers (.pull)",
            startExpanded: false,
            tooltip: "Pulls a specific item (and quantity) from nearby chests into your inventory.",
            buildContent: c => FormBuilder.Build(c,
                title: "Pull item",
                commandTemplate: ".pull {item} {quantity}",
                new TextField("item", "Item name",
                    tooltip: "Item to pull. Exact match against the item's prefab name (e.g. 'Iron Ingot')."),
                new IntField("quantity", "Quantity", min: 1, max: 9999,
                    tooltip: "How many to pull. KindredLogistics caps at what's available across all reachable chests.")));

        CollapsibleSection.Build(utilCard,
            title: "Find item (.fi)",
            startExpanded: false,
            tooltip: "Locates the specified item in nearby chests and prints which chest holds it.",
            buildContent: c => FormBuilder.Build(c,
                title: "Find item",
                commandTemplate: ".fi {item}",
                new TextField("item", "Item name",
                    tooltip: "Item to search for. Exact match against the item's prefab name.")));

        CollapsibleSection.Build(utilCard,
            title: "Find chest by name (.fc)",
            startExpanded: false,
            tooltip: "Locates chests with the specified custom name.",
            buildContent: c => FormBuilder.Build(c,
                title: "Find chest",
                commandTemplate: ".fc {name}",
                new TextField("name", "Chest name",
                    tooltip: "The custom name written on the chest's sign (e.g. 'salvage', 'spawner', 'brazier').")));

        // Admin globals (.lg ...) live on the dedicated KindredLogisticsAdminTab.
    }

    private void BuildKindredLogisticsAdminTab(GameObject page)
    {
        page = BeginAdminGate(page);   // gray out + disable the admin controls below for non-admins
        // 0.10.12: card-wrap the admin info + admin-globals + spawn-form
        // sections.
        var noteCard = AddCard(page, "KLAdminNoteCard");
        RenderAdminInfoNote(noteCard, "Kindred Logistics admin");
        AddBodyText(noteCard,
            "Server-wide toggles for the KindredLogistics features. These affect every player on the server. Requires admin permission server-side.");

        AddSpacer(page, 6);

        var globalsCard = AddCard(page, "KLAdminGlobalsCard");
        AddSectionHeading(globalsCard, "Admin Globals (.lg)");

        var ar1 = AddKLRow(globalsCard, "KLAdmin1");
        AddCommandButton(ar1, "Sort Stash",      MessageService.BCCOM_KL_ADMIN_SORT_STASH,
            "Server-wide: enable auto-stash on sort double-click (.lg ss).");
        AddCommandButton(ar1, "Pull",            MessageService.BCCOM_KL_ADMIN_PULL,
            "Server-wide: enable the .pull command for all players (.lg p).");
        AddCommandButton(ar1, "Craft Pull",      MessageService.BCCOM_KL_ADMIN_CRAFT_PULL,
            "Server-wide: enable right-click-recipe ingredient pulling (.lg cr).");
        AddCommandButton(ar1, "Servant Stash",   MessageService.BCCOM_KL_ADMIN_AUTOSTASH_MISSION,
            "Server-wide: enable auto-stash for servant mission rewards (.lg asm).");

        var ar2 = AddKLRow(globalsCard, "KLAdmin2");
        AddCommandButton(ar2, "Conveyor",        MessageService.BCCOM_KL_ADMIN_CONVEYOR,
            "Server-wide: enable sender/receiver conveyor chests (.lg co).");
        AddCommandButton(ar2, "Salvage",         MessageService.BCCOM_KL_ADMIN_SALVAGE,
            "Server-wide: enable 'salvage' chests (.lg sal).");
        AddCommandButton(ar2, "Unit Spawner",    MessageService.BCCOM_KL_ADMIN_UNIT_SPAWNER,
            "Server-wide: enable 'spawner' chests filling unit stations (.lg us).");
        AddCommandButton(ar2, "Brazier",         MessageService.BCCOM_KL_ADMIN_BRAZIER,
            "Server-wide: enable 'brazier' chests auto-fueling braziers (.lg bz).");

        var ar3 = AddKLRow(globalsCard, "KLAdmin3");
        AddCommandButton(ar3, "Named Brazier",   MessageService.BCCOM_KL_ADMIN_NAMED_BRAZIER,
            "Server-wide: enable night/proximity-controlled named braziers (.lg nam).");
        AddCommandButton(ar3, "Trash",           MessageService.BCCOM_KL_ADMIN_TRASH,
            "Server-wide: allow 'trash' chests to delete their contents (.lg trash).");
        AddCommandButton(ar3, "Show Settings",   MessageService.BCCOM_KL_ADMIN_SETTINGS,
            "Print the current server-wide Logistics settings into chat (.lg s).");
        AddCommandButton(ar3, "Empty Trash",     MessageService.BCCOM_KL_ADMIN_EMPTY_TRASH,
            "Empty all trash containers in your current territory (.emptytrash).");

        AddSpacer(page, 6);

        var spawnCard = AddCard(page, "KLAdminSpawnCard");
        AddSectionHeading(spawnCard, "Admin Item Spawn");
        CollapsibleSection.Build(spawnCard,
            title: "Spawn item to territory stash (.adminstash)",
            startExpanded: false,
            tooltip: "Spawns a quantity of an item directly into the current territory's stash containers.",
            buildContent: c => FormBuilder.Build(c,
                title: "Admin stash spawn",
                commandTemplate: ".adminstash {item} {quantity}",
                new TextField("item", "Item name",
                    tooltip: "Item prefab name to spawn (e.g. 'Iron Ingot', 'Blood Essence')."),
                new IntField("quantity", "Quantity", min: 1, max: 9999,
                    tooltip: "How many to spawn.")));
    }

    // Action row matching the look used by other tabs (Familiars, Class, etc.):
    // horizontal group, child controls expand, single-row layout.
    private static GameObject AddKLRow(GameObject parent, string id)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, id,
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        // 0.50: scale the row height with the UI font multiplier. AddCommandButton's
        // buttons already grow via Theme.ScaledHeight(30); without scaling the row too,
        // childControlHeight clamps the buttons back to a fixed 32px at Large+ text and
        // the captions overflow into the row below (overlap on every Kindred tab).
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(32), preferredHeight: Theme.ScaledHeight(32), flexibleHeight: 0);
        return row;
    }

    // -----------------------------------------------------------------------
    // KindredCommands - Player tab (Kindred group)
    //
    // Surfaces the 13 player-facing (non-admin) KindredCommands. The much
    // larger admin surface (~120 commands) lands in Phase 5i as its own
    // sub-tabs - keeping the player view minimal here.
    // -----------------------------------------------------------------------

    private void BuildKindredCommandsPlayerTab(GameObject page)
    {
        // 0.10.12: card-wrap Intro / Self / Server info / Lookups.
        var introCard = AddCard(page, "KCPlayerIntroCard");
        AddBodyText(introCard,
            "Requires the KindredCommands server mod. Player-facing commands only — admin commands land in their own tab.");

        AddSpacer(page, 6);

        var selfCard = AddCard(page, "KCSelfCard");
        AddSectionHeading(selfCard, "Self");
        var selfRow = AddKLRow(selfCard, "KCSelf");
        AddCommandButton(selfRow, "AFK",   MessageService.BCCOM_KC_AFK,
            "Toggle AFK animation - locks WASD movement until you run .afk again (.afk).");
        AddCommandButton(selfRow, "Ping",  MessageService.BCCOM_KC_PING,
            "Show your latency in chat (.ping).");
        AddCommandButton(selfRow, "Pace",  MessageService.BCCOM_KC_PACE,
            "Pace at the closest NPC near you - a cosmetic walk loop (.pace).");

        AddSpacer(page, 6);

        var infoCard = AddCard(page, "KCInfoCard");
        AddSectionHeading(infoCard, "Server info");
        var infoRow1 = AddKLRow(infoCard, "KCInfo1");
        AddCommandButton(infoRow1, "Server Time", MessageService.BCCOM_KC_TIME,
            "Print the current server time into chat (.time).");
        AddCommandButton(infoRow1, "Online Staff", MessageService.BCCOM_KC_STAFF,
            "List staff members currently online (.staff).");
        AddCommandButton(infoRow1, "Open Plots",   MessageService.BCCOM_KC_CASTLE_OPEN_PLOTS,
            "Report territories with open or decaying castle plots (.openplots — alias .op). Reply appears in chat.");
        AddCommandButton(infoRow1, "Soulshards",   MessageService.BCCOM_KC_GEAR_SOULSHARD_STATUS,
            "Print the status of soulshards on the server (.gear soulshardstatus).");

        var infoRow2 = AddKLRow(infoCard, "KCInfo2");
        AddCommandButton(infoRow2, "Boss List",   MessageService.BCCOM_KC_BOSS_LIST,
            "List all locked bosses on the server (.boss list).");
        AddCommandButton(infoRow2, "Region List", MessageService.BCCOM_KC_REGION_LIST,
            "List all locked and gated regions on the server (.region list).");

        BuildClanListPager(infoRow2);

        AddSpacer(page, 6);

        var lookupsCard = AddCard(page, "KCLookupsCard");
        AddSectionHeading(lookupsCard, "Lookups");

        CollapsibleSection.Build(lookupsCard,
            title: "Check player level (.checklevel)",
            startExpanded: false,
            tooltip: "Print a player's current level into chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Check player level",
                commandTemplate: ".checklevel {player}",
                new PlayerNameField("player", "Player",
                    tooltip: "Player whose level you want to look up. Exact character-name match.")));

        CollapsibleSection.Build(lookupsCard,
            title: "List clan members (.clan members)",
            startExpanded: false,
            tooltip: "List the members of a specific clan.",
            buildContent: c => FormBuilder.Build(c,
                title: "List clan members",
                commandTemplate: ".clan members {clan}",
                new TextField("clan", "Clan name",
                    tooltip: "Exact clan name. Use the Clan List pager above to find it.")));
    }

    // Stateful pager for `.clan list <page>` - three buttons in a row plus a
    // current-page label between Prev and Next. Page-1 is fired on click of
    // any button; the label reflects which page the next press will request.
    private void BuildClanListPager(GameObject parent)
    {
        var prev = UIFactory.CreateButton(parent, "ClanListPrev", "<");
        UIFactory.SetLayoutElement(prev.GameObject,
            minWidth: 32, preferredWidth: 36, flexibleWidth: 0,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        TooltipHover.Attach(prev.GameObject, "Previous page of clans (.clan list <page-1>).");
        prev.OnClick = () =>
        {
            if (_clanListPage > 1) _clanListPage--;
            RefreshClanListPage(send: true);
        };

        _clanListPageLabel = UIFactory.CreateLabel(parent, "ClanListPage",
            $"Clan List p{_clanListPage}",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledUI(12)).TextMesh;
        UIFactory.SetLayoutElement(_clanListPageLabel.gameObject,
            minWidth: 90, preferredWidth: 100, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        _clanListPageLabel.enableWordWrapping = false;
        _clanListPageLabel.overflowMode = TextOverflowModes.Overflow;

        var next = UIFactory.CreateButton(parent, "ClanListNext", ">");
        UIFactory.SetLayoutElement(next.GameObject,
            minWidth: 32, preferredWidth: 36, flexibleWidth: 0,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        TooltipHover.Attach(next.GameObject, "Next page of clans (.clan list <page+1>).");
        next.OnClick = () =>
        {
            _clanListPage++;
            RefreshClanListPage(send: true);
        };
    }

    private void RefreshClanListPage(bool send)
    {
        if (_clanListPageLabel != null)
            _clanListPageLabel.text = $"Clan List p{_clanListPage}";
        if (send)
            MessageService.EnqueueMessage($"{MessageService.BCCOM_KC_CLAN_LIST} {_clanListPage}");
    }

    // -----------------------------------------------------------------------
    // Quick Start Guide tab (Help group)
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // Vanilla Admin Reference tab (Help group) — documents the in-game console
    // commands Raphael cannot trigger directly, plus the chat-command equivalents
    // we DO surface elsewhere.
    // -----------------------------------------------------------------------

    private void BuildVanillaAdminTab(GameObject page)
    {
        AddGuideSection(page,
            "Why this is reference, not action",
            "V Rising's vanilla admin commands (the ones the game ships with — " +
            "no mods needed) are CONSOLE commands, typed into the in-game " +
            "console (default key F1) — NOT chat commands. They use a different " +
            "input pipeline than mod commands like .fam or .lvl. Raphael " +
            "is a CLIENT mod that sends CHAT messages, so it can't trigger " +
            "console commands directly.\n\n" +
            "The good news: KindredCommands re-implements most of the " +
            "common admin actions (kick / ban / give / spawn / teleport / etc.) " +
            "as CHAT commands — and Raphael wires every one of those into the " +
            "KINDRED → Admin: Players / Server / World tabs. So the actions " +
            "you want to take are already covered there. This tab is a " +
            "reference for the underlying vanilla console commands in case " +
            "you need to type them yourself (open the console with F1).");

        // 0.10.7: switched from monolithic AddGuideSection bodies (manually
        // tab-aligned, proportional font misaligned the columns) to the new
        // AddCommandTable helper for crisp 2-column layout.
        AddCommandTable(page, "Authentication",
            ("adminauth",          "Grant yourself admin powers (needed before any other vanilla admin command)."),
            ("adminderegister",    "Drop admin powers for the current session."));

        AddCommandTable(page, "Player management",
            ("Kick <CharacterName>",       "Kick a player by name."),
            ("BanUser <SteamID>",          "Ban a player by SteamID."),
            ("Banhammer <SteamID>",        "Ban + delete the player's characters."),
            ("Unban <UserIndex>",          "Unban (use BanList to find the index)."),
            ("BanList",                    "List current bans."),
            ("Mute <SteamID> <minutes>",   "Silence a player."),
            ("PlayerInfo <CharacterName>", "Print info about a specific player."),
            ("UserList",                   "List all users registered on the server."),
            ("WhoIsOnline",                "List currently-connected players."),
            ("Connectinfo",                "Print connection info for diagnostics."),
            ("ForceConnectInfo",           "Force-refresh connection info display."));
        AddGuideSection(page, "",
            "Chat-command equivalents already in Raphael (KINDRED → Admin: Players):  .kick, .ban (via Kindred or vanilla), .unban, etc.");

        AddCommandTable(page, "Character actions",
            ("Suicide",                    "Kill your own character (no penalty)."),
            ("KillPlayer <CharacterName>", "Kill a player."),
            ("RevivePlayer <Name>",        "Revive a downed player."),
            ("HealPlayer <Name>",          "Fully restore a player's HP."),
            ("DamagePlayer <Name> <amt>",  "Apply damage to a player."),
            ("ResetCharacter <Name>",      "Fully reset a player's character (DESTRUCTIVE)."),
            ("KillUnit",                   "Kill the unit your reticle is targeting."),
            ("HealUnit",                   "Heal the targeted unit to full."),
            ("DamageUnit <amount>",        "Apply damage to the targeted unit."),
            ("Despawn",                    "Despawn the targeted unit."));

        AddCommandTable(page, "Item / character spawning",
            ("give <PrefabName>",          "Give yourself an item by prefab name."),
            ("giveset",                    "Open the giveset menu (sets of armor/weapons)."),
            ("SpawnUnit <PrefabName>",     "Spawn an NPC at your position."),
            ("SpawnCastle <PrefabName>",   "Spawn a castle structure."),
            ("FillStorage",                "Fill the targeted storage container."),
            ("ClearAllInventories",        "Wipe every inventory on the server (DESTRUCTIVE)."),
            ("DespawnAll",                 "Despawn all units in the world (DESTRUCTIVE)."));
        AddGuideSection(page, "",
            "Chat-command equivalents already in Raphael (KINDRED → Admin: World):  .give {item} {qty},  .spawnnpc / .customspawn / .customspawnat.  Use the Lookups section on the same tab to find prefab names.");

        AddCommandTable(page, "Teleportation",
            ("teleporttowaypoint <name>",  "Teleport to a named waypoint."),
            ("TeleportToPlayer <Name>",    "Teleport to a specific player."),
            ("TeleportToBoss <Boss>",      "Teleport to a boss's spawn location."),
            ("TeleportToHorse",            "Teleport to your horse (if any)."),
            ("TeleportToOwner",            "Teleport to the targeted creature's owner."),
            ("TeleportToWorld <x> <z>",    "Teleport to absolute world coordinates."),
            ("UnlockAllPlayerWaypoints",   "Unlock every waypoint for a player."),
            ("MapMarker <args>",           "Add / manage map markers."));
        AddGuideSection(page, "",
            "Chat-command equivalents:  .teleport {x} {y} {z} {player},  .tpb {boss} (Kindred teleport-to-boss).");

        AddCommandTable(page, "Time, world & difficulty",
            ("Time",                       "Print current server time."),
            ("ChangeMapTime <hh:mm>",      "Set the in-world time of day."),
            ("SetTimeOfDay <hh:mm>",       "Alias of ChangeMapTime on some versions."),
            ("weather <type>",             "Change weather (clear / rain / mist / storm)."),
            ("GameDifficulty <level>",     "Adjust the server's difficulty."),
            ("Lockdown",                   "Toggle PvP / siege lockdown."),
            ("alllockdown",                "Server-wide lockdown."));

        AddCommandTable(page, "Server administration",
            ("Save",                       "Force a server save."),
            ("AutoSave",                   "Enable auto-save."),
            ("StopAutoSave",               "Disable auto-save."),
            ("ReloadServerSettings",       "Re-apply ServerHostSettings.json without restart."),
            ("Restart",                    "Restart the server."),
            ("Disconnect",                 "Disconnect yourself from the server."),
            ("Quit",                       "Close the V Rising client."),
            ("List",                       "List every available console command (live reference)."),
            ("Help <command>",             "Detailed help for a specific command."),
            ("ShowVersion",                "Display the V Rising client/server version."),
            ("ShowAdminCommands",          "List admin-only commands (live filtered List)."));

        AddCommandTable(page, "Debugging / display",
            ("DebugHud",                   "Toggle the debug heads-up display."),
            ("ShowDebugUI",                "Toggle extended debug UI."),
            ("ShowFPS",                    "Show frame-rate counter."),
            ("ShowInputBindings",          "List current input bindings."),
            ("BlockUserInput",             "Block all input (anti-stuck recovery)."),
            ("Console.SetCheats",          "Enable cheat-level commands (requires extra setup)."));

        AddGuideSection(page,
            "Authoritative list",
            "V Rising occasionally adds/removes commands between patches. The " +
            "in-game console's `List` command always shows the live set the " +
            "current build accepts — use it as the authoritative reference if " +
            "any command listed here is rejected. `Help <command>` prints " +
            "usage details for a specific entry.");

        AddGuideSection(page,
            "How to use the in-game console",
            "  1. Press F1 while in-game (or whatever your bound console key is)\n" +
            "  2. Type the command exactly as written (case can matter for some)\n" +
            "  3. Press Enter\n\n" +
            "If F1 doesn't open the console, check your keybindings in the V " +
            "Rising settings or run with `-console` on the command line.\n\n" +
            "If a command says 'access denied' you haven't run adminauth yet, " +
            "OR your SteamID isn't in the server's adminlist (server-side config).");
    }

    // -----------------------------------------------------------------------
    // About tab (Help group) — credits + community links
    // -----------------------------------------------------------------------

    private void BuildAboutTab(GameObject page)
    {
        // 0.10.8: reworked from a stack of dense AddGuideSection calls into a
        // four-region layout with explicit spacers between regions, so the
        // page reads as discrete cards instead of one wall of text:
        //   1. Header band — version + one-line description
        //   2. Acknowledgements — Bloodcraft + KindredCommands credits
        //   3. About me / community — author info + support links
        //   4. Project — GitHub / Thunderstore / license footer
        // Each region opens with a section heading; AddSpacer separates them.

        // ── Region 1 ─────────────────────────────────────────────────────
        AddGuideSection(page,
            $"Raphael  v{MyPluginInfo.PLUGIN_VERSION}{(Raphael.Config.BuildVariant.IsTestVariant ? $"   [{Raphael.Config.BuildVariant.Tag}]" : string.Empty)}",
            "A unified CLIENT UI for the Bloodcraft suite of V Rising " +
            "server mods. Surfaces every Bloodcraft, KindredCommands, and " +
            "KindredLogistics chat command as buttons and forms — no more " +
            "typing in chat to manage your familiars, run a class change, " +
            "or fire an admin command. Live progress overlays for XP, " +
            "weapon expertise, blood legacy, familiars, professions, and " +
            "daily quests stream in at ~1 Hz over the signed [ECLIPSE] " +
            "protocol Bloodcraft already speaks, so the cost on the " +
            "server is the same as if you had Eclipse installed.");
        AddSpacer(page, 12);

        // ── Region 2 ─────────────────────────────────────────────────────
        AddSectionHeading(page, "Mods this UI is built on");
        AddGuideSection(page, "",
            "Raphael is purely a client-side overlay — it doesn't " +
            "modify the server or add new gameplay systems. Every feature " +
            "you see is wrapping the chat-command surface of these " +
            "server-side mods by other developers:");

        AddSpacer(page, 4);
        AddGuideSection(page,
            "Bloodcraft  —  by zfolmt",
            "Leveling, weapon expertise, blood legacies, professions, " +
            "familiars, classes, quests, and prestige. The bulk of what " +
            "Raphael surfaces (every tab in the BLOODCRAFT group) " +
            "would not exist without zfolmt's mod.");
        AddLinkRow(page, "Bloodcraft on Thunderstore",
            "https://thunderstore.io/c/v-rising/p/zfolmt/Bloodcraft/");

        AddSpacer(page, 6);
        AddGuideSection(page,
            "KindredCommands  —  by odjit",
            "Commands to expand server administration and add quality-" +
            "of-life affordances for players. The KINDRED admin tabs " +
            "(Players, Server, World) and the entire Logistics section " +
            "call into odjit's mods.");
        AddLinkRow(page, "KindredCommands on Thunderstore",
            "https://thunderstore.io/c/v-rising/p/odjit/KindredCommands/");

        // 0.50: also credit the companion server mods Raphael integrates beyond the two external
        // mods above. Each of these powers its own tab group, which appears only when the mod is
        // detected on your server.
        AddSpacer(page, 10);
        AddGuideSection(page, "",
            "Raphael also integrates these companion server mods — each tab group lights up only when " +
            "the matching mod is detected on your server:");

        AddSpacer(page, 4);
        AddGuideSection(page,
            "Beelzebub  —  ability capture, loadouts & transforms",
            "Powers the BEELZEBUB group: the bestiary of captured abilities, loadouts per weapon and per " +
            "form (Wolf, Bear, Rat, Spider, Toad, Mounted), hotkeys, creature transforms, the bar-repair " +
            "tools (Fix bar, Clear bar, Reset bar), and admin ability shaping and player recovery.");

        AddSpacer(page, 6);
        AddGuideSection(page,
            "Uriel  —  storage sharing, prisons, stairs & object spawning",
            "Powers the URIEL group: client-side detection of nearby shared storage, public prisons, " +
            "stair restyling, and the object-spawn palette plus admin spawn-condition config.");

        AddSpacer(page, 6);
        AddGuideSection(page,
            "Faust  —  server investigation & analytics",
            "Powers the FAUST group: castle / plot / decay reporting, player positions, server-stats " +
            "charts, clan rosters, and activity heat maps.");

        AddSpacer(page, 6);
        AddGuideSection(page,
            "Nyarlathotep  —  world events",
            "Powers the NYARLATHOTEP group. The server stages NPC world events: empowered factions, hunting " +
            "waves, arena swarms, region hunting grounds, faction revenge and a map-wide apocalypse. Raphael " +
            "shows what's running, where, and when the next wave comes, and gives admins the event controls.");
        AddSpacer(page, 12);

        // ── Region 3 ─────────────────────────────────────────────────────
        AddSectionHeading(page, "About the author");
        AddGuideSection(page, "",
            "Maintained by Chaos. I play on The Shadow " +
            "Realm — a Brutal, PvE community server — and built this mod " +
            "to give that community a click-driven alternative to typing " +
            "every Bloodcraft command. Feedback, bug reports, and pull " +
            "requests are very welcome through any of the links below.");
        AddSpacer(page, 4);

        AddLinkRow(page, "Server Discord  (The Shadow Realm)",
            "https://discord.gg/usC9QgBrXK");
        // 0.9.8: direct-message link to the author. Friend-testing: users
        // wanted a way to reach me one-on-one for mod feedback / bug reports
        // without joining the server Discord first.
        AddLinkRow(page, "DM me on Discord  (PerpetualChaos)",
            "https://discord.com/users/PerpetualChaos");
        AddLinkRow(page, "Support development  (PayPal)",
            "https://www.paypal.com/paypalme/KrisPenland");
        AddLinkRow(page, "SkillEra.IO  (other projects)",
            "https://SkillEra.IO");
        AddSpacer(page, 12);

        // ── Region 4 ─────────────────────────────────────────────────────
        AddSectionHeading(page, "Project");
        AddGuideSection(page, "",
            "Raphael is open source under the MIT license. The " +
            "repository, the release feed, and every prior version's " +
            "CHANGELOG entry are public:");
        AddSpacer(page, 4);

        AddLinkRow(page, "GitHub repository",
            "https://github.com/KDavidP1987/Raphael");
        AddLinkRow(page, "Thunderstore listing",
            "https://thunderstore.io/c/v-rising/p/kdpen/Raphael/");
        AddSpacer(page, 6);

        AddGuideSection(page, "",
            "Bloodcraft compatibility: v1.13.x   •   License: MIT   •   " +
            "Plugin GUID: kdpen.Raphael");
    }

    // -----------------------------------------------------------------------
    // Settings tab (Help group) — 0.9.0 sections, 0.9.2 promoted to its own tab.
    //
    // Three segmented controls (text scale UI, text scale overlay, plus a
    // grid of per-overlay transparency selectors) and the chat-noise toggle.
    // 0.9.2 hooks rebuild + opacity-refresh so changes take effect live.
    // -----------------------------------------------------------------------

    private void BuildSettingsTab(GameObject page)
    {
        BuildDisplaySettingsSection(page);
    }

    private void BuildDisplaySettingsSection(GameObject page)
    {
        AddGuideSection(page,
            "Display settings",
            "Adjust text size and overlay transparency. " +
            "<b>Text size is now a slider</b> (50–400%, where 100% = the old \"Standard\") — drag it or type an " +
            "exact value, then click <b>Apply</b> (it also takes effect when the panel/overlay is reopened). " +
            "This replaces the old Small/Standard/Large/X-Large steps so you can dial in a size that fits your " +
            "monitor or TV exactly, including much larger than the old X-Large. Transparency " +
            "changes apply immediately — drag the slider or type a 0–100 value. " +
            "0% transparency = solid background; 100% = fully invisible " +
            "background (the panel is still draggable and its text stays visible).");

        // ── Text & button size ─────────────────────────────────────────────
        var sizeCard = AddCard(page, "SetCardTextSize");
        AddSectionHeading(sizeCard, "Text & button size");
        AddTextScaleRow(sizeCard, "UI text size",
            currentScaleSetting: () => Config.Settings.UITextScale,
            setScale: v => {
                Config.Settings.SetUITextScale(v);
                UI.Framework.CustomLib.Util.Theme.UIFontMultiplier = v;
            },
            // 0.9.2: rebuild the main panel so labels pick up the new multiplier. Deferred to next frame so
            // the click handler completes before the panel hosting it is destroyed.
            rebuild: () => Plugin.UIManager.RequestRebuildMainPanel());
        AddTextScaleRow(sizeCard, "Overlay text size",
            currentScaleSetting: () => Config.Settings.OverlayTextScale,
            setScale: v => {
                Config.Settings.SetOverlayTextScale(v);
                UI.Framework.CustomLib.Util.Theme.OverlayFontMultiplier = v;
            },
            // Rebuild each enabled overlay so its labels pick up the new multiplier.
            rebuild: () => Plugin.UIManager.RequestRebuildAllOverlays());
        // 0.18.4: launcher (Raphael/OV) button size — some displays render them large.
        AddLauncherButtonSizeRow(sizeCard);

        // ── Overlay transparency ───────────────────────────────────────────
        var transCard = AddCard(page, "SetCardTransparency");
        AddSectionHeading(transCard, "Overlay transparency");
        AddTransparencyRow(transCard, "XP overlay",
            () => Config.Settings.XPOverlayTransparency,
            v => Config.Settings.SetXPOverlayTransparency(v));
        AddTransparencyRow(transCard, "Familiar overlay",
            () => Config.Settings.FamiliarOverlayTransparency,
            v => Config.Settings.SetFamiliarOverlayTransparency(v));
        AddTransparencyRow(transCard, "Familiar Browser",
            () => Config.Settings.FamiliarBrowserTransparency,
            v => Config.Settings.SetFamiliarBrowserTransparency(v));
        // 0.53: Familiar Quick Spawn overlay transparency.
        AddTransparencyRow(transCard, "Quick Spawn",
            () => Config.Settings.FamiliarQuickSpawnOverlayTransparency,
            v => Config.Settings.SetFamiliarQuickSpawnOverlayTransparency(v));
        // 0.54: Faust boss-tracker overlay transparency.
        AddTransparencyRow(transCard, "Boss Tracker",
            () => Config.Settings.FaustBossTrackerOverlayTransparency,
            v => Config.Settings.SetFaustBossTrackerOverlayTransparency(v));
        AddTransparencyRow(transCard, "Daily quest",
            () => Config.Settings.DailyQuestTransparency,
            v => Config.Settings.SetDailyQuestTransparency(v));
        AddTransparencyRow(transCard, "Professions",
            () => Config.Settings.ProfessionOverlayTransparency,
            v => Config.Settings.SetProfessionOverlayTransparency(v));
        // 0.14.0: combined overlay transparency slider — same as the
        // standalone-overlay sliders above.
        AddTransparencyRow(transCard, "Combined overlay",
            () => Config.Settings.CombinedOverlayTransparency,
            v => Config.Settings.SetCombinedOverlayTransparency(v));
        // 0.19: Beelz summons overlay transparency.
        AddTransparencyRow(transCard, "Beelz summons",
            () => Config.Settings.BeelzSummonsOverlayTransparency,
            v => Config.Settings.SetBeelzSummonsOverlayTransparency(v));
        // 0.20: Beelz transforms overlay transparency.
        AddTransparencyRow(transCard, "Beelz transforms",
            () => Config.Settings.BeelzTransformOverlayTransparency,
            v => Config.Settings.SetBeelzTransformOverlayTransparency(v));

        // ── Colors & combined overlay (each its own titled card) ───────────
        BuildCombinedOverlaySection(AddCard(page, "SetCardCombinedOverlay"));
        BuildPanelBackgroundColorSection(AddCard(page, "SetCardPanelBg"));
        BuildButtonColorSection(AddCard(page, "SetCardButtonColor"));

        // ── HUD extras ─────────────────────────────────────────────────────
        var hudCard = AddCard(page, "SetCardHUDExtras");
        AddSectionHeading(hudCard, "HUD extras");
        AddShowProgressBarsToggle(hudCard);
        AddShowOverlayBonusStatsToggle(hudCard);
        AddShowOverlayStatAcronymsToggle(hudCard);
        AddShowOverlayXpCounterToggle(hudCard);
        AddProgressBarHeightControls(hudCard);
        AddOverlayEdgePaddingControls(hudCard);
        AddShowPrestigeSubLineToggle(hudCard);
        AddOverlaysBehindMenusToggle(hudCard);
        AddSuppressInputToggle(hudCard);
        AddBlockInputOverUiToggle(hudCard);
        AddOverlayAlignmentToggle(hudCard);
        AddAutoScanVBloodsToggle(hudCard);

        // ── Remaining sections — each wrapped in its own card so the page reads
        //    as discrete grouped boxes instead of one long flat scroll of toggles.
        BuildProfessionTrackedSection(AddCard(page, "SetCardProfessions"));
        // Chat-suppression controls (consolidated "Chat noise" section).
        BuildChatNoiseSection(AddCard(page, "SetCardChatNoise"));
        // Per-component size adjustment + reset-to-default controls.
        BuildSizePositioningSection(AddCard(page, "SetCardSizePos"));
        // Beelzebub tab-group availability (Auto/On/Off) — lives on the always-reachable Settings tab.
        BuildBeelzAvailabilityGlobalSetting(AddCard(page, "SetCardBeelzAvail"));
        // Master overlay-hide options (Toggle vs Timed, hide launcher, chat-with-OV) — pairs with the Hotkeys bind.
        BuildOverlayVisibilitySection(AddCard(page, "SetCardOverlayVis"));
        // Optional keyboard hotkeys for the floating Raphael / OV buttons + diagnostic mode.
        BuildHotkeysSection(AddCard(page, "SetCardHotkeys"));
    }

    // 0.28: "Overlay Visibility" — shapes what the OV button / the "Toggle all overlays" hotkey do.
    // Toggle vs Timed auto-reappear, optionally hide the launcher cluster too (gated on a guaranteed
    // way back), and keep the game's native chat hidden during a hide instead of letting it pop back.
    private void BuildOverlayVisibilitySection(GameObject page)
    {
        AddSectionHeading(page, "Overlay Visibility");

        AddGuideSection(page, "",
            "Controls the upper-right 'OV' button and the 'Toggle all overlays' hotkey — the master " +
            "show/hide for every Raphael overlay you've enabled. (It never reveals overlays you've turned " +
            "off per-overlay.) Choose whether a hide stays until you toggle back, or auto-reappears " +
            "after a countdown; optionally hide the Raphael/OV launcher buttons too for a fully clean screen.");

        // ── Hide mode: Toggle vs Timed ──────────────────────────────────────
        var modeRow = UIFactory.CreateHorizontalGroup(page, "DisplayRow_OVHideMode",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(modeRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        var modeLbl = UIFactory.CreateLabel(modeRow, "Lbl_OVHideMode", "Hide mode:",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(modeLbl.GameObject,
            minWidth: Theme.ScaledWidth(90), preferredWidth: Theme.ScaledWidth(100), flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        var modeHint = UIFactory.CreateLabel(modeRow, "Hint_OVHideMode",
            FormatOVHideModeHint(),
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(modeHint.GameObject,
            minWidth: 150, preferredWidth: 180, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        modeHint.TextMesh.fontStyle = FontStyles.Italic;

        // Forward-declared so the mode buttons can refresh the gate warning after flipping Timed mode
        // (toggling Timed on can satisfy the launcher-hide safety gate).
        System.Action refreshGateWarning = null;

        void PickMode(bool timed)
        {
            Config.Settings.SetOverlayTimedHide(timed);
            modeHint.TextMesh.text = FormatOVHideModeHint();
            refreshGateWarning?.Invoke();
        }
        AddScaleButton(modeRow, "Toggle", () => PickMode(false));
        AddScaleButton(modeRow, "Timed",  () => PickMode(true));

        // ── Timed duration presets ─────────────────────────────────────────
        var durRow = UIFactory.CreateHorizontalGroup(page, "DisplayRow_OVHideDuration",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(durRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        var durLbl = UIFactory.CreateLabel(durRow, "Lbl_OVHideDuration", "Timed duration:",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(durLbl.GameObject,
            minWidth: Theme.ScaledWidth(100), preferredWidth: Theme.ScaledWidth(110), flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        var durHint = UIFactory.CreateLabel(durRow, "Hint_OVHideDuration",
            FormatOVHideDurationHint(),
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(durHint.GameObject,
            minWidth: Theme.ScaledWidth(70), preferredWidth: Theme.ScaledWidth(80), flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        durHint.TextMesh.fontStyle = FontStyles.Italic;

        void PickDuration(int seconds)
        {
            Config.Settings.SetOverlayHideDurationSeconds(seconds);
            durHint.TextMesh.text = FormatOVHideDurationHint();
        }
        // Spread from quick screenshots to long timed video captures (up to the 10-minute cap).
        AddScaleButton(durRow, "10s", () => PickDuration(10));
        AddScaleButton(durRow, "30s", () => PickDuration(30));
        AddScaleButton(durRow, "1m",  () => PickDuration(60));
        AddScaleButton(durRow, "2m",  () => PickDuration(120));
        AddScaleButton(durRow, "5m",  () => PickDuration(300));
        AddScaleButton(durRow, "10m", () => PickDuration(600));

        // ── Hide launcher buttons too (gated) ───────────────────────────────
        AddOverlayVisToggle(page, "Hide Raphael/OV buttons too", Config.Settings.HideLauncherButtonsWithOverlays,
            "When ON, hiding overlays also hides the always-on Raphael and OV launcher buttons for a fully clean screen. " +
            "Only takes effect when there's a guaranteed way back — Timed hide on, or a 'Toggle all overlays' hotkey bound below.",
            v => { Config.Settings.SetHideLauncherButtonsWithOverlays(v); refreshGateWarning?.Invoke(); });

        // Live gate warning — shown only when the user has asked to hide the buttons but neither escape
        // route (timed mode / bound hotkey) exists, so the setting is currently being ignored for safety.
        var gateWarn = UIFactory.CreateLabel(page, "OVHideGateWarning", "",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(gateWarn.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 34, flexibleHeight: 0);
        gateWarn.TextMesh.enableWordWrapping = true;
        gateWarn.TextMesh.overflowMode = TextOverflowModes.Overflow;
        gateWarn.TextMesh.fontStyle = FontStyles.Italic;
        refreshGateWarning = () =>
        {
            bool wantsButtonHide = Config.Settings.HideLauncherButtonsWithOverlays;
            bool safe = Config.Settings.CanHideLauncherButtons;
            if (wantsButtonHide && !safe)
            {
                gateWarn.TextMesh.text = "Note: the launcher buttons won't hide yet — turn on Timed hide above, " +
                    "or bind the 'Toggle all overlays' hotkey below, so you have a way to bring the UI back.";
                gateWarn.TextMesh.color = WARNING_TINT;
            }
            else
            {
                gateWarn.TextMesh.text = string.Empty;
            }
        };
        refreshGateWarning();

        // ── Chat ↔ master overlay-hide (OV) ─────────────────────────────────
        // Master switch (same setting as the "Hide chat with OV" footer toggle) — surfaced here too so it's
        // findable in Settings. When OFF (default), the OV hide never touches chat (game chat OR Raphael chat).
        AddOverlayVisToggle(page, "Hide chat when hiding overlays (OV)",
            Config.Settings.HideChatWithOverlaysToggle,
            "When ON, the master 'hide all overlays' (OV button / hotkey) ALSO hides the chat — both Raphael's " +
            "chat window and the game's native chat. Default OFF, so chat stays visible while the other overlays " +
            "hide. (Same setting as 'Hide chat with OV' on the main panel footer.) The toggle below then decides " +
            "whether the GAME chat comes back or stays hidden during the hide.",
            v => { Config.Settings.SetHideChatWithOverlaysToggle(v); Plugin.UIManager?.ApplyNativeChatVisibility(); });
        AddOverlayVisToggle(page, "↳ Keep GAME chat hidden too (else it returns)",
            Config.Settings.KeepNativeChatHiddenWhileOverlaysHidden,
            "Only applies when 'Hide chat when hiding overlays' (above) is on. ON (default) keeps the game's " +
            "native chat hidden during a master hide for a clean screen. Turn OFF if you'd rather the game chat " +
            "stay/return visible while Raphael overlays are hidden — e.g. if you use the game's own chat and not " +
            "Raphael's chat window.",
            v => { Config.Settings.SetKeepNativeChatHiddenWhileOverlaysHidden(v); Plugin.UIManager?.ApplyNativeChatVisibility(); });
    }

    private static string FormatOVHideModeHint()
        => Config.Settings.OverlayTimedHide
            ? "(timed — auto-reappears)"
            : "(toggle — stays hidden until pressed again)";

    private static string FormatOVHideDurationHint()
        => $"(current: {FormatHideDuration(Config.Settings.OverlayHideDurationSeconds)})";

    // Compact duration label: "30s", "1m", "2m 30s" — keeps the hint readable now that the range
    // extends to 10 minutes.
    private static string FormatHideDuration(int seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        int m = seconds / 60, s = seconds % 60;
        return s == 0 ? $"{m}m" : $"{m}m {s}s";
    }

    // 0.28: simple full-width labeled toggle for the Overlay Visibility section. Distinct from
    // AddChatOptionToggle (which refreshes the chat overlay) — this just persists the bool + runs an
    // optional follow-up so the gate warning can update live.
    private void AddOverlayVisToggle(GameObject parent, string label, bool initial, string tooltip,
        System.Action<bool> setter)
    {
        var t = UIFactory.CreateToggle(parent, label + "Toggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 280, preferredWidth: 380, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(13);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        t.Toggle.isOn = initial;
        if (!string.IsNullOrEmpty(tooltip)) TooltipHover.Attach(t.GameObject, tooltip);
        t.OnValueChanged += v => setter(v);
    }

    // 0.15.0: configurable hotkeys + diagnostic mode toggle, all under one
    // collapsible section so they don't visually compete with the existing
    // Display Settings controls.
    private void BuildHotkeysSection(GameObject page)
    {
        AddSectionHeading(page, "Hotkeys & diagnostics");

        AddGuideSection(page, "",
            "Optional keyboard shortcuts for the Raphael and OV floating buttons. " +
            "Both are unbound by default — click \"Set...\" then press the key " +
            "(or modifier+key combo) you want. Click \"Clear\" to remove the binding. " +
            "Diagnostic mode emits [DIAG]-tagged trace logs to BepInEx for UI clicks, " +
            "overlay toggles, protocol state changes, and hotkey fires — toggle it on " +
            "when reproducing an issue, then share LogOutput.log to help debug.");

        AddHotkeyRow(page, "Open main panel",
            () => Config.Settings.HotkeyToggleMainPanel,
            v  => Config.Settings.SetHotkeyToggleMainPanel(v));
        AddHotkeyRow(page, "Toggle all overlays",
            () => Config.Settings.HotkeyToggleAllOverlays,
            v  => Config.Settings.SetHotkeyToggleAllOverlays(v));

        AddSpacer(page, 4);
        AddDiagnosticModeToggle(page);
    }

    // 0.15.0: a hotkey rebind row — label + current-binding text + Set... +
    // Clear buttons. Click "Set..." to enter listening mode; the next non-
    // modifier key pressed becomes the binding, with all currently-held
    // modifier keys (Ctrl/Alt/Shift/Win) captured as the combo.
    private void AddHotkeyRow(GameObject parent, string label,
        System.Func<Config.BCHotkey> get, System.Action<Config.BCHotkey> set)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"HotkeyRow_{label}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var labelLbl = UIFactory.CreateLabel(row, $"HotkeyLabel_{label}", label + ":",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(labelLbl.GameObject,
            minWidth: 140, preferredWidth: 150, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        labelLbl.TextMesh.enableWordWrapping = false;
        labelLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // Current-binding display. Shows the key combo as text. When binding
        // is empty, shows "(unbound)" in muted color.
        var bindingLbl = UIFactory.CreateLabel(row, $"HotkeyBinding_{label}", "",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(bindingLbl.GameObject,
            minWidth: 110, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        bindingLbl.TextMesh.enableWordWrapping = false;
        bindingLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        bindingLbl.TextMesh.fontStyle = FontStyles.Bold;

        var setBtn = UIFactory.CreateButton(row, $"HotkeySet_{label}", "Set...");
        UIFactory.SetLayoutElement(setBtn.GameObject,
            minWidth: 60, preferredWidth: 64, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var setTxt = setBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (setTxt != null) setTxt.fontSize = Theme.ScaledUI(11);
        TooltipHover.Attach(setBtn.GameObject,
            $"Click then press the key (or modifier+key combo) you want to bind to '{label}'. Modifiers (Ctrl / Alt / Shift / Win) are captured along with the key. Press Escape to cancel.");

        var clearBtn = UIFactory.CreateButton(row, $"HotkeyClear_{label}", "Clear");
        UIFactory.SetLayoutElement(clearBtn.GameObject,
            minWidth: 56, preferredWidth: 60, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var clearTxt = clearBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (clearTxt != null) clearTxt.fontSize = Theme.ScaledUI(11);
        TooltipHover.Attach(clearBtn.GameObject, $"Remove the '{label}' hotkey binding.");

        // Refresh the binding label to reflect current setting.
        System.Action refreshLabel = () =>
        {
            var current = get();
            if (current.IsEmpty)
            {
                bindingLbl.TextMesh.text = "(unbound)";
                bindingLbl.TextMesh.color = Theme.MutedBody;
            }
            else
            {
                bindingLbl.TextMesh.text = current.ToString();
                bindingLbl.TextMesh.color = Theme.DefaultText;
            }
        };
        refreshLabel();

        // Listening state — only one row can be listening at a time, but we
        // don't enforce that across rows; the per-row ticker self-unregisters
        // on first key press or Escape.
        System.Action listener = null;
        setBtn.OnClick = () =>
        {
            // If we're already listening, cancel that listener first.
            if (listener != null)
            {
                Behaviors.CoreUpdateBehavior.Actions.Remove(listener);
                listener = null;
            }
            bindingLbl.TextMesh.text = "press a key...";
            bindingLbl.TextMesh.color = Color.yellow;

            listener = () =>
            {
                try
                {
                    // Escape cancels — restore prior binding.
                    if (Input.GetKeyDown(KeyCode.Escape))
                    {
                        Behaviors.CoreUpdateBehavior.Actions.Remove(listener);
                        listener = null;
                        refreshLabel();
                        return;
                    }
                    // Find the first non-modifier key pressed THIS FRAME.
                    KeyCode pressed = KeyCode.None;
                    for (int k = (int)KeyCode.Backspace; k < (int)KeyCode.JoystickButton0; k++)
                    {
                        var kc = (KeyCode)k;
                        if (IsModifierKey(kc)) continue;
                        if (Input.GetKeyDown(kc)) { pressed = kc; break; }
                    }
                    if (pressed == KeyCode.None) return;
                    // Capture currently-held modifiers as the combo.
                    var mods = new List<KeyCode>();
                    if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) mods.Add(KeyCode.LeftControl);
                    if (Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt))     mods.Add(KeyCode.LeftAlt);
                    if (Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift))   mods.Add(KeyCode.LeftShift);
                    if (Input.GetKey(KeyCode.LeftWindows) || Input.GetKey(KeyCode.RightWindows)) mods.Add(KeyCode.LeftWindows);

                    var hotkey = new Config.BCHotkey
                    {
                        MainKey = pressed,
                        Modifiers = mods.Count > 0 ? mods.ToArray() : null,
                    };
                    set(hotkey);
                    Behaviors.CoreUpdateBehavior.Actions.Remove(listener);
                    listener = null;
                    refreshLabel();
                    LogUtils.LogInfo($"Hotkey '{label}' bound to {hotkey}.");
                }
                catch (System.Exception ex)
                {
                    LogUtils.LogError($"Hotkey bind failed: {ex}");
                    Behaviors.CoreUpdateBehavior.Actions.Remove(listener);
                    listener = null;
                    refreshLabel();
                }
            };
            Behaviors.CoreUpdateBehavior.Actions.Add(listener);
        };

        clearBtn.OnClick = () =>
        {
            // Cancel any pending listener.
            if (listener != null)
            {
                Behaviors.CoreUpdateBehavior.Actions.Remove(listener);
                listener = null;
            }
            set(Config.BCHotkey.Empty);
            refreshLabel();
            LogUtils.LogInfo($"Hotkey '{label}' cleared.");
        };
    }

    private static bool IsModifierKey(KeyCode k) =>
        k == KeyCode.LeftControl  || k == KeyCode.RightControl
     || k == KeyCode.LeftAlt      || k == KeyCode.RightAlt
     || k == KeyCode.LeftShift    || k == KeyCode.RightShift
     || k == KeyCode.LeftWindows  || k == KeyCode.RightWindows
     || k == KeyCode.LeftCommand  || k == KeyCode.RightCommand
     || k == KeyCode.AltGr;

    private void AddDiagnosticModeToggle(GameObject parent)
    {
        // 0.15.0 friend-test v3: three radio-style buttons instead of a
        // single bool. Off / Session / Always — Session is a runtime-only
        // override that resets on game restart so users who flip diagnostic
        // on to reproduce a bug don't accidentally leave it on forever.

        var labelLbl = UIFactory.CreateLabel(parent, "DiagnosticModeLabel",
            "Diagnostic mode:",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(labelLbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        labelLbl.TextMesh.fontStyle = FontStyles.Bold;
        TooltipHover.Attach(labelLbl.GameObject,
            "Verbose logging to BepInEx for UI clicks, overlay toggles, protocol state changes, feature-flag transitions, and hotkey fires. Off by default. Use Session when reproducing a single-bug repro so logging silently shuts off on next restart; use Always to keep logging on across sessions. Share LogOutput.log with the maintainer for debugging.");

        var row = UIFactory.CreateHorizontalGroup(parent, "DiagnosticModeRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 8, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        // We keep the three toggle refs so we can re-sync isOn after any
        // click — the click on one toggle must clear the other two to
        // preserve mutual exclusion.
        ToggleRef offT     = null;
        ToggleRef sessionT = null;
        ToggleRef alwaysT  = null;

        // 0.67.2: returns false once this row's toggles are destroyed (page retired) so it can be dropped from
        // _diagModeRefreshers — the same radios exist on Settings and on Chat & Diagnostics and must agree.
        System.Func<bool> refreshOne = () =>
        {
            if (offT != null && offT.Toggle == null) return false;
            var current = Config.Settings.DiagnosticModeSetting;
            if (offT     != null) offT.Toggle.SetIsOnWithoutNotify(current == Config.Settings.DiagnosticModeChoice.Off);
            if (sessionT != null) sessionT.Toggle.SetIsOnWithoutNotify(current == Config.Settings.DiagnosticModeChoice.Session);
            if (alwaysT  != null) alwaysT.Toggle.SetIsOnWithoutNotify(current == Config.Settings.DiagnosticModeChoice.Always);
            return true;
        };
        _diagModeRefreshers.Add(refreshOne);
        System.Action refresh = RefreshAllDiagModeRadios;

        offT = AddDiagnosticRadio(row, "Off",
            "No diagnostic logging. Default state.",
            () => Config.Settings.DiagnosticModeSetting == Config.Settings.DiagnosticModeChoice.Off,
            () => { Config.Settings.SetDiagnosticMode(Config.Settings.DiagnosticModeChoice.Off); refresh(); LogUtils.LogInfo("DiagnosticMode -> Off."); });

        sessionT = AddDiagnosticRadio(row, "This session",
            "Diagnostic logging enabled until the game restarts. The .cfg stays at Off so a forgotten Session-only mode silently clears on next launch.",
            () => Config.Settings.DiagnosticModeSetting == Config.Settings.DiagnosticModeChoice.Session,
            () => { Config.Settings.SetDiagnosticMode(Config.Settings.DiagnosticModeChoice.Session); refresh(); LogUtils.LogInfo("DiagnosticMode -> Session (resets on game restart)."); });

        alwaysT = AddDiagnosticRadio(row, "Always",
            "Diagnostic logging enabled every session until the user turns it off. Persists to .cfg.",
            () => Config.Settings.DiagnosticModeSetting == Config.Settings.DiagnosticModeChoice.Always,
            () => { Config.Settings.SetDiagnosticMode(Config.Settings.DiagnosticModeChoice.Always); refresh(); LogUtils.LogInfo("DiagnosticMode -> Always (persists across game restarts)."); });

        // Sync initial state.
        refresh();
    }

    // 0.67.2: every live Diagnostic-mode radio row (Settings tab + Chat & Diagnostics tab).
    private readonly List<System.Func<bool>> _diagModeRefreshers = new();

    private void RefreshAllDiagModeRadios()
    {
        for (int i = _diagModeRefreshers.Count - 1; i >= 0; i--)
        {
            bool alive;
            try { alive = _diagModeRefreshers[i](); }
            catch { alive = false; }
            if (!alive) _diagModeRefreshers.RemoveAt(i);
        }
        RefreshChatDiagRows();   // its "Diagnostic mode is on" note depends on this
    }

    /// <summary>0.15.0: one mutually-exclusive radio button for the
    /// DiagnosticMode three-state. Uses the standard CreateToggle factory
    /// (so it inherits the same Frame-border styling as every other
    /// toggle in Raphael) — mutual exclusion is enforced by the OnClick
    /// callback re-syncing the sibling toggles via the outer `refresh`
    /// closure.</summary>
    private static ToggleRef AddDiagnosticRadio(GameObject parent, string label, string tooltip,
        System.Func<bool> isSelected, System.Action onSelect)
    {
        var t = UIFactory.CreateToggle(parent, $"DiagnosticRadio_{label}");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 90, preferredWidth: 110, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(11);
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 70, preferredWidth: 90, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.SetIsOnWithoutNotify(isSelected());
        TooltipHover.Attach(t.GameObject, tooltip);
        t.OnValueChanged += v =>
        {
            // Radio behavior: only fire onSelect when the user CHECKS the
            // button. A user who manually un-checks gets re-checked by the
            // refresh() call inside onSelect — but if nothing else became
            // selected, we have to re-check ourselves.
            if (v) onSelect();
            else
            {
                // Re-check ourselves if we're still the "selected" choice
                // (mutual exclusion didn't transfer to another button).
                if (isSelected()) t.Toggle.SetIsOnWithoutNotify(true);
            }
        };
        return t;
    }

    // -----------------------------------------------------------------------
    // 0.10.6/0.18: per-category chat-visibility toggle helper. The old standalone
    // "Chat Logging" section (3 Show* toggles + Show/Hide-All buttons) was merged
    // into the consolidated "Chat noise" section (BuildChatNoiseSection); this
    // helper is still used there to build the inverted "Hide …" rows. Suppression
    // is purely cosmetic — data extraction parses BEFORE the destroy decision, so
    // hiding never breaks a feature.
    // -----------------------------------------------------------------------
    private UI.Framework.UniverseLib.UI.Models.ToggleRef AddChatLoggingToggle(
        GameObject parent, string label, string tooltip,
        System.Func<bool> get, System.Action<bool> set)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"ChatLogRow_{label}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, $"ChatLogTog_{label}");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = get();
        TooltipHover.Attach(t.GameObject, tooltip);
        t.OnValueChanged += v => set(v);
        return t;
    }

    // -----------------------------------------------------------------------
    // 0.9.7: Size & Positioning section (Settings tab)
    //
    // Per-component subsections (Primary UI + each of the 5 overlays). Each
    // exposes [-]/[+] for width and height (20 px step; Shift+click = 100 px)
    // plus a [Default] button that calls SetDefaultSizeAndPosition(). Primary
    // UI additionally has [Auto-size] (mirrors footer toggle) and [Fullscreen]
    // (mirrors the title-bar maximize button). Manual drag-from-edge resize
    // remains unaffected — these controls just provide a click-driven
    // alternative for users who didn't realize the panels were resizable.
    // -----------------------------------------------------------------------

    private const int SIZE_STEP_NORMAL = 20;
    private const int SIZE_STEP_LARGE  = 100;

    // 0.9.8: list of size-readout refreshers. Each AddSizePosStepRow registers
    // its own refresh action here; the per-frame ticker (TickSizePosReadouts)
    // walks them while Settings is the active tab so dragging a panel by its
    // edge updates the readout immediately — pre-0.9.8 the readout only
    // refreshed inside the +/- click handlers, leaving manual drag-resize
    // out of sync. Cleared on Reset to avoid leaking references.
    private readonly System.Collections.Generic.List<System.Action> _sizePosRefreshers = new();
    private System.Action _sizePosReadoutTicker;

    private void BuildSizePositioningSection(GameObject page)
    {
        AddSectionHeading(page, "Size & Positioning");

        // 0.9.8: per-frame readout refresher. Self-gates on ActiveTab so it's
        // a no-op except when the Settings tab is open. Registered once when
        // the section builds.
        if (_sizePosReadoutTicker == null)
        {
            _sizePosReadoutTicker = TickSizePosReadouts;
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_sizePosReadoutTicker);
        }

        // 0.10.13: dropped italic + bumped 11 → 13 + muted color for legibility.
        var help = UIFactory.CreateLabel(page, "SizePosHelp",
            $"<color={Theme.MutedBodyHex}>Click +/- to adjust the width/height of the main panel or any overlay " +
            "(hold Shift while clicking for 100 px steps). Default returns it to its " +
            "factory size and position. Drag-to-resize from any edge still works.</color>",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(help.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 36, preferredHeight: 52, flexibleHeight: 0);
        help.TextMesh.fontStyle = FontStyles.Normal;
        help.TextMesh.enableWordWrapping = true;
        help.TextMesh.overflowMode = TextOverflowModes.Overflow;

        AddSpacer(page, 4);
        BuildPrimaryUISizeControls(page);
        AddSpacer(page, 4);
        BuildOverlaySizeControls(page, "XP overlay",       () => Plugin.UIManager?.ExperienceOverlay);
        BuildOverlaySizeControls(page, "Familiar overlay", () => Plugin.UIManager?.FamiliarOverlay);
        BuildOverlaySizeControls(page, "Familiar Browser", () => Plugin.UIManager?.FamiliarBrowserOverlay);
        BuildOverlaySizeControls(page, "Daily Quest",      () => Plugin.UIManager?.DailyQuestOverlay);
        BuildOverlaySizeControls(page, "Professions",      () => Plugin.UIManager?.ProfessionOverlay);
        // 0.14.0: combined overlay size controls — same +/-/Default treatment
        // as the standalone overlays. Particularly useful while iterating on
        // the new panel before the auto-fit defaults settle.
        BuildOverlaySizeControls(page, "Combined overlay", () => Plugin.UIManager?.CombinedOverlay);
    }

    private void BuildPrimaryUISizeControls(GameObject page)
    {
        AddSizePosSubHeading(page, "Primary UI");

        // Row: [Auto-size] [Fullscreen] [Default]
        var btnRow = UIFactory.CreateHorizontalGroup(page, "PrimaryUISizeBtns",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(btnRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        AddSizePosButton(btnRow, "Auto-size",
            "Auto-resize the main panel vertically to fit the active tab's content. Mirrors the footer toggle.",
            () => {
                Config.Settings.SetIsPanelAutoResizeEnabled(!Config.Settings.IsPanelAutoResizeEnabled);
                AutoResizeIfEnabled();
            });
        AddSizePosButton(btnRow, "Fullscreen",
            "Toggle the main panel between its current size+position and a fullscreen stretch (with a small inset so the edges stay grabbable). Mirrors the maximize button on the title bar.",
            ToggleFullscreen);
        AddSizePosButton(btnRow, "Default",
            "Reset the main panel to its default size + unpin and re-center it. Recovers from any 'panel feels stuck' state.",
            () => {
                SetFullscreen(false);
                // 0.15.0: defensive unpin. The main panel never opts into
                // the lock-overlays system, but a stale IsPinned=true
                // from save data could leave it locked against drag/
                // resize. Clearing here ensures the user always has a
                // single-click recovery path even before they restart
                // the game (the load-time fix in ApplySaveData stops
                // the persistence; this button restores movement in the
                // current session).
                IsPinned = false;
                SetDefaultSize();
                EnsureValidPosition();
                SaveInternalData();
            });

        // Left-rail accordion toggle — collapse other groups when one opens (keeps the rail short on small
        // screens). Lives here under Primary UI sizing because it's a "the rail is too tall" remedy.
        var accToggle = UIFactory.CreateToggle(page, "LeftRailAccordionToggle");
        UIFactory.SetLayoutElement(accToggle.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        accToggle.Text.text = "Accordion left rail (collapse other groups when one opens)";
        accToggle.Text.fontSize = Theme.ScaledUI(13);
        accToggle.Text.alignment = TextAlignmentOptions.MidlineLeft;
        accToggle.Text.enableWordWrapping = true;
        accToggle.Toggle.isOn = Config.Settings.LeftRailAccordion;
        accToggle.OnValueChanged += v => Config.Settings.SetLeftRailAccordion(v);
        TooltipHover.Attach(accToggle.GameObject,
            "ON (default): expanding one left-rail group (Bloodcraft / Beelzebub / Kindred / Uriel / Faust / " +
            "Settings & Help) collapses the others, so the rail stays short on small screens. OFF: keep several " +
            "groups expanded at once.");

        // Width + Height step rows
        AddSizePosStepRow(page, "Width",
            () => Rect != null ? (int)Rect.sizeDelta.x : 0,
            d => { SetFullscreen(false); AdjustSize(d, 0); });
        AddSizePosStepRow(page, "Height",
            () => Rect != null ? (int)Rect.sizeDelta.y : 0,
            d => { SetFullscreen(false); AdjustSize(0, d); });
    }

    private void BuildOverlaySizeControls(GameObject page, string label,
        System.Func<Raphael.UI.Framework.CustomLib.Panel.ResizeablePanelBase> getter)
    {
        AddSizePosSubHeading(page, label);

        var btnRow = UIFactory.CreateHorizontalGroup(page, $"{label}_SizeBtns",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(btnRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        AddSizePosButton(btnRow, "Default",
            $"Reset the {label}'s size to its default. Does NOT move it — drag the overlay if you also want to reset position. Only affects this overlay if it's currently open.",
            () => {
                var p = getter();
                if (p != null) p.SetDefaultSize();
            });

        AddSizePosStepRow(page, "Width",
            () => { var p = getter(); return p?.Rect != null ? (int)p.Rect.sizeDelta.x : 0; },
            d => { var p = getter(); if (p != null) p.AdjustSize(d, 0); });
        AddSizePosStepRow(page, "Height",
            () => { var p = getter(); return p?.Rect != null ? (int)p.Rect.sizeDelta.y : 0; },
            d => { var p = getter(); if (p != null) p.AdjustSize(0, d); });

        AddSpacer(page, 2);
    }

    private void AddSizePosSubHeading(GameObject parent, string text)
    {
        var lbl = UIFactory.CreateLabel(parent, $"SizePosSub_{text}", text,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Bold;
    }

    private void AddSizePosButton(GameObject parent, string label, string tooltip, System.Action onClick)
    {
        var btn = UIFactory.CreateButton(parent, $"SizePosBtn_{label}", label);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 90, preferredWidth: 110, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(12); t.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(btn.GameObject, tooltip);
        btn.OnClick = onClick;
    }

    /// <summary>0.9.7: builds a "Width [-] 720 px [+]" row. The label text
    /// re-reads the current value from the supplied getter after each click
    /// so the user sees the immediate effect. Shift-click on +/- jumps by
    /// SIZE_STEP_LARGE instead of SIZE_STEP_NORMAL.</summary>
    private void AddSizePosStepRow(GameObject parent, string label,
        System.Func<int> getCurrent, System.Action<int> applyDelta)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"SizePosRow_{label}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);

        var lblText = UIFactory.CreateLabel(row, $"SizePosRowLabel_{label}", $"{label}:",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lblText.GameObject,
            minWidth: 60, preferredWidth: 70, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var minusBtn = UIFactory.CreateButton(row, $"SizePosMinus_{label}", "-");
        UIFactory.SetLayoutElement(minusBtn.GameObject,
            minWidth: 32, preferredWidth: 32, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var minusT = minusBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (minusT != null) { minusT.fontSize = Theme.ScaledUI(14); minusT.alignment = TextAlignmentOptions.Center; }

        var valLabel = UIFactory.CreateLabel(row, $"SizePosVal_{label}", $"{getCurrent()} px",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(valLabel.GameObject,
            minWidth: 60, preferredWidth: 80, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var plusBtn = UIFactory.CreateButton(row, $"SizePosPlus_{label}", "+");
        UIFactory.SetLayoutElement(plusBtn.GameObject,
            minWidth: 32, preferredWidth: 32, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var plusT = plusBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (plusT != null) { plusT.fontSize = Theme.ScaledUI(14); plusT.alignment = TextAlignmentOptions.Center; }

        TooltipHover.Attach(minusBtn.GameObject, $"Shrink {label.ToLowerInvariant()} by {SIZE_STEP_NORMAL} px (Shift+click: {SIZE_STEP_LARGE} px).");
        TooltipHover.Attach(plusBtn.GameObject,  $"Grow {label.ToLowerInvariant()} by {SIZE_STEP_NORMAL} px (Shift+click: {SIZE_STEP_LARGE} px).");

        // Capture the val label so click handlers can refresh it after applying.
        System.Action refresh = () => {
            if (valLabel?.TextMesh == null) return;
            var text = $"{getCurrent()} px";
            if (valLabel.TextMesh.text != text)
                valLabel.TextMesh.text = text;
        };
        minusBtn.OnClick = () => { applyDelta(-CurrentStep()); refresh(); };
        plusBtn.OnClick  = () => { applyDelta( CurrentStep()); refresh(); };
        // 0.9.8: per-frame refresh path — picks up changes from manual edge-
        // drag resize so the readout stays in sync with the live panel size.
        _sizePosRefreshers.Add(refresh);
    }

    private void TickSizePosReadouts()
    {
        if (ActiveTab != PanelType.SettingsTab) return;
        if (!Enabled) return;
        // Iterate via index — refresh actions don't mutate the list at runtime,
        // but a foreach over a possibly-extended list would also be cheap.
        for (int i = 0; i < _sizePosRefreshers.Count; i++)
        {
            try { _sizePosRefreshers[i]?.Invoke(); }
            catch (System.Exception ex)
            {
                Utils.LogUtils.LogWarning($"Size-pos readout refresher #{i} threw: {ex.Message}");
            }
        }
    }

    /// <summary>0.9.7: detect Shift modifier at click time so users can hold
    /// Shift to jump by SIZE_STEP_LARGE instead of SIZE_STEP_NORMAL. Uses the
    /// legacy UnityEngine.Input API which is what V Rising's IL2CPP wrap
    /// exposes; reading is allocation-free.</summary>
    private static int CurrentStep()
    {
        bool shift = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift)
                  || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
        return shift ? SIZE_STEP_LARGE : SIZE_STEP_NORMAL;
    }

    /// <summary>0.9.2: toggle XP and prestige progress visualization as
    /// horizontal bars (alongside the existing % numeric value). Off by
    /// default. Applies immediately because each render re-reads the
    /// setting and toggles the bar GameObject's SetActive.
    /// 0.14.0 friend-test v2: replaced the single global toggle with 5
    /// per-system toggles. Each controls the bar visibility in BOTH the
    /// standalone overlay AND the combined overlay so the two views stay
    /// consistent. Old Settings.ShowProgressBars is kept solely for the
    /// Prestige Info display in the Prestige tab (separate concern).
    /// </summary>
    private void AddShowProgressBarsToggle(GameObject parent)
    {
        AddPanelColorSubHeading(parent, "ShowBarsHeader", "Show progress bars for");

        var row = UIFactory.CreateHorizontalGroup(parent, "ShowProgressBarsRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);

        AddProgressBarSystemToggle(row, "XP",          () => Config.Settings.ShowProgressBarXP,          Config.Settings.SetShowProgressBarXP);
        AddProgressBarSystemToggle(row, "Familiar",    () => Config.Settings.ShowProgressBarFamiliar,    Config.Settings.SetShowProgressBarFamiliar);
        AddProgressBarSystemToggle(row, "Weapon",      () => Config.Settings.ShowProgressBarExpertise,   Config.Settings.SetShowProgressBarExpertise);
        AddProgressBarSystemToggle(row, "Blood",       () => Config.Settings.ShowProgressBarLegacy,      Config.Settings.SetShowProgressBarLegacy);
        AddProgressBarSystemToggle(row, "Professions", () => Config.Settings.ShowProgressBarProfessions, Config.Settings.SetShowProgressBarProfessions);
    }

    private static void AddProgressBarSystemToggle(GameObject row, string label,
        System.Func<bool> get, System.Action<bool> set)
    {
        var t = UIFactory.CreateToggle(row, $"BarSys_{label}");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 70, preferredWidth: 80, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(11);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 50, preferredWidth: 65, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        t.Toggle.isOn = get();
        TooltipHover.Attach(t.GameObject,
            $"Show the progress bar for {label} in BOTH the standalone overlay and the combined overlay. Applies wherever the system renders.");
        t.OnValueChanged += v =>
        {
            set(v);
            // Push to combined overlay (its render reads these flags directly).
            Plugin.UIManager?.RefreshCombinedOverlaySections();
            // Prestige info display has its own separate ShowProgressBars
            // setting (legacy global, kept for that one use); no refresh
            // needed here.
        };
    }

    /// <summary>0.9.6: toggle the XP overlay's per-row bonus-stat detail line
    /// (chosen stat names + current numeric values from .wep get / .bl get).
    /// Off by default. When toggled on, the overlay's always-on ticker starts
    /// auto-fetching .wep get and .bl get &lt;CurrentBlood&gt; every 10s while
    /// the overlay is visible. The render methods self-gate on the setting,
    /// so toggling off immediately hides the rows on the next frame.</summary>
    private void AddShowOverlayBonusStatsToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "ShowOverlayBonusStatsRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "ShowOverlayBonusStatsToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        t.Text.text = "Show weapon expertise & blood legacy bonus stats on the XP overlay";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.ShowOverlayBonusStats;
        TooltipHover.Attach(t.GameObject,
            "When on, the XP overlay shows the chosen bonus-stat names AND their current numeric values under the Weapon and Legacy rows (e.g. '+12.5% PhysicalPower'). Auto-fetches .wep get and .bl get every 10s while the overlay is visible. Off by default for a minimal HUD.");
        t.OnValueChanged += value =>
        {
            Config.Settings.SetShowOverlayBonusStats(value);
            // XP overlay re-renders on its next frame tick (always-on
            // BonusStatsTick). Combined overlay is event-driven; nudge it
            // to re-render now so the sub-rows show/hide without waiting
            // for the next data event (could be ~10s).
            Plugin.UIManager?.RefreshCombinedOverlaySections();
        };
    }

    // B5 (0.19): abbreviate bonus-stat names on the overlays so each stays on one line and the
    // wrapped sub-row can't overlap the bar/row around it at large text. Off by default (full names).
    private void AddShowOverlayStatAcronymsToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "ShowOverlayStatAcronymsRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "ShowOverlayStatAcronymsToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Abbreviate bonus-stat names on overlays (PhysicalPower → PhysPwr)";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.ShowOverlayStatAcronyms;
        TooltipHover.Attach(t.GameObject,
            "Shortens the bonus-stat names shown on the XP and Combined overlays (Eclipse-style: PhysicalPower → PhysPwr, SpellCriticalStrikeChance → SpellCritCh, etc.) so each stat stays on one line. Helps the bonus-stats sub-row from overlapping the progress bar / next row at Large or X-Large overlay text. Off = full names.");
        t.OnValueChanged += value =>
        {
            Config.Settings.SetShowOverlayStatAcronyms(value);
            // XP overlay re-renders on its next BonusStatsTick frame; nudge the combined overlay
            // (event-driven) to re-render immediately so the names switch without a data-event wait.
            Plugin.UIManager?.RefreshCombinedOverlaySections();
        };
    }

    // 0.10.7: Show numerical Exp/Ess counter under the Weapon and Legacy
    // rows on the XP overlay. Values come from parsing .wep get / .bl get
    // chat replies; off by default to match the rest of the HUD-extra toggles.
    private void AddShowOverlayXpCounterToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "ShowOverlayXpCounterRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "ShowOverlayXpCounterToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Show numerical Exp / Ess counter on the XP overlay";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.ShowOverlayXpCounter;
        TooltipHover.Attach(t.GameObject,
            "Adds a sub-row under Weapon and Legacy showing 'Exp: 123 / 4500 (2.7%)' — current expertise / essence and the threshold to the next level. Derives the threshold from the percentage the server prints, so it's accurate to within ±1 of the true value.");
        t.OnValueChanged += value =>
        {
            Config.Settings.SetShowOverlayXpCounter(value);
            // Same rationale as the bonus-stats toggle above — push combined
            // to re-render immediately so the counter sub-rows show/hide.
            Plugin.UIManager?.RefreshCombinedOverlaySections();
        };
    }

    // 0.10.7: Progress-bar height: relative-vs-absolute toggle + slider for
    // the absolute mode. Default is absolute (8 px) because user feedback was
    // that pre-0.10.7 bars grew aggressively when the overlay was enlarged
    // for additional info rows.
    private void AddProgressBarHeightControls(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "ProgressBarHeightRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var label = UIFactory.CreateLabel(row, "ProgressBarHeightLabel",
            "Progress bar height:", TextAlignmentOptions.MidlineLeft,
            color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(label.GameObject,
            minWidth: 160, preferredWidth: 180, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        // Decrement
        var minus = UIFactory.CreateButton(row, "ProgressBarHeightMinus", "-");
        UIFactory.SetLayoutElement(minus.Component.gameObject,
            minWidth: 30, preferredWidth: 30, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var valueLbl = UIFactory.CreateLabel(row, "ProgressBarHeightValue",
            $"{Config.Settings.ProgressBarHeight} px",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(valueLbl.GameObject,
            minWidth: 60, preferredWidth: 70, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var plus = UIFactory.CreateButton(row, "ProgressBarHeightPlus", "+");
        UIFactory.SetLayoutElement(plus.Component.gameObject,
            minWidth: 30, preferredWidth: 30, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        minus.OnClick = () =>
        {
            Config.Settings.SetProgressBarHeight(Config.Settings.ProgressBarHeight - 1);
            valueLbl.TextMesh.text = $"{Config.Settings.ProgressBarHeight} px";
        };
        plus.OnClick = () =>
        {
            Config.Settings.SetProgressBarHeight(Config.Settings.ProgressBarHeight + 1);
            valueLbl.TextMesh.text = $"{Config.Settings.ProgressBarHeight} px";
        };

        TooltipHover.Attach(row,
            $"Absolute pixel height for the XP/Weapon/Legacy progress bars when 'Scale bar with overlay' is off. Clamped {Config.Settings.PROGRESS_BAR_HEIGHT_MIN}..{Config.Settings.PROGRESS_BAR_HEIGHT_MAX}. Default 8.");

        // Companion toggle on the next line so the row doesn't get crowded.
        var relRow = UIFactory.CreateHorizontalGroup(parent, "ProgressBarRelativeRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(relRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var relToggle = UIFactory.CreateToggle(relRow, "ProgressBarRelativeToggle");
        UIFactory.SetLayoutElement(relToggle.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        relToggle.Text.text = "Scale bar height with overlay (pre-0.10.7 behavior)";
        relToggle.Text.fontSize = Theme.ScaledUI(12);
        relToggle.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(relToggle.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        relToggle.Toggle.isOn = Config.Settings.ProgressBarHeightRelative;
        TooltipHover.Attach(relToggle.GameObject,
            "When on, the bars stretch vertically as you grow the overlay. When off (default), the bars stay at the fixed pixel height above regardless of overlay size.");
        relToggle.OnValueChanged += v => Config.Settings.SetProgressBarHeightRelative(v);
    }

    // 0.10.8: per-overlay left/right edge padding. Friend-testing feedback
    // surfaced text sitting flush with overlay borders — especially the
    // Familiar Browser's row labels brushing the scrollbar gutter. One
    // setting, applied to every overlay's content area at construct time;
    // rebuild via overlay toggle to pick up changes live.
    private void AddOverlayEdgePaddingControls(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "OverlayEdgePadRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var label = UIFactory.CreateLabel(row, "OverlayEdgePadLabel",
            "Overlay edge padding:", TextAlignmentOptions.MidlineLeft,
            color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(label.GameObject,
            minWidth: 160, preferredWidth: 180, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var minus = UIFactory.CreateButton(row, "OverlayEdgePadMinus", "-");
        UIFactory.SetLayoutElement(minus.Component.gameObject,
            minWidth: 30, preferredWidth: 30, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var valueLbl = UIFactory.CreateLabel(row, "OverlayEdgePadValue",
            $"{Config.Settings.OverlayEdgePadding} px",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(valueLbl.GameObject,
            minWidth: 60, preferredWidth: 70, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var plus = UIFactory.CreateButton(row, "OverlayEdgePadPlus", "+");
        UIFactory.SetLayoutElement(plus.Component.gameObject,
            minWidth: 30, preferredWidth: 30, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        minus.OnClick = () =>
        {
            Config.Settings.SetOverlayEdgePadding(Config.Settings.OverlayEdgePadding - 1);
            valueLbl.TextMesh.text = $"{Config.Settings.OverlayEdgePadding} px";
            // Rebuild any currently-shown overlay so the new padding
            // takes effect live (same lifecycle as text-scale changes).
            Plugin.UIManager.RequestRebuildAllOverlays();
        };
        plus.OnClick = () =>
        {
            Config.Settings.SetOverlayEdgePadding(Config.Settings.OverlayEdgePadding + 1);
            valueLbl.TextMesh.text = $"{Config.Settings.OverlayEdgePadding} px";
            Plugin.UIManager.RequestRebuildAllOverlays();
        };

        TooltipHover.Attach(row,
            $"Inner left/right padding applied to every overlay (XP, Familiar, Familiar Browser, Daily Quest, Professions). Higher = more breathing room between text and panel edge / scrollbar. Clamped {Config.Settings.OVERLAY_EDGE_PADDING_MIN}..{Config.Settings.OVERLAY_EDGE_PADDING_MAX}. Default 6.");
    }

    // 0.10.7: Prestige sub-line on bars — Eclipse-style inset strip showing
    // progress toward next prestige tier inside the main bar.
    // 0.10.10: opt-in toggle for the V-Bloods tab's auto-scan-on-open
    // behavior. Pre-0.10.10 the scan fired unconditionally the first time
    // the user opened the tab; now it's manual by default.
    private void AddAutoScanVBloodsToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "AutoScanVBloodsRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "AutoScanVBloodsToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Auto-scan V-Bloods when the V-Bloods tab opens";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.AutoScanVBloodsOnTabOpen;
        TooltipHover.Attach(t.GameObject,
            "When on, opening the V-Bloods tab with an empty collection automatically triggers a box-sweep scan (.fam boxes + .fam cb + .fam l for every box). Off by default — the scanner switches your active box ~10-15 times, so most users prefer the explicit Scan all button.");
        t.OnValueChanged += v => Config.Settings.SetAutoScanVBloodsOnTabOpen(v);
    }

    // 0.13.0: per-profession visibility section for the Professions overlay.
    // Eight toggles in two compact rows of four. Each flip writes its
    // Settings.ShowProfession* flag and calls RefreshProfessionOverlay so
    // the overlay re-renders immediately. Forward-compatible with the
    // v0.14.0 combined overlay (same flags will gate the per-profession
    // sub-rows inside the combined component).
    // 0.14.0: combined overlay settings section. Master toggle + 6 per-section
    // checkboxes. Master toggle flips Settings.ShowCombinedOverlay and calls
    // ApplyCombinedOverlayMutualExclusion + ApplyCombinedFooterVisibility so
    // the change cascades through the panel + footer + overlay set in one go.
    private void BuildCombinedOverlaySection(GameObject page)
    {
        AddSectionHeading(page, "Combined overlay");

        var help = UIFactory.CreateLabel(page, "CombinedOverlayHelp",
            "Single draggable overlay containing XP / Familiar / Weapon / Blood / Professions / Quests sections — replaces the four standalone info overlays when on. " +
            "Per-section checkboxes below pick which slices show inside the combined panel; the per-profession checkboxes from 'Professions tracked' still filter the profession rows.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(14));
        UIFactory.SetLayoutElement(help.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(32), flexibleHeight: 0);
        help.TextMesh.enableWordWrapping = true;
        help.TextMesh.overflowMode = TextOverflowModes.Overflow;
        help.TextMesh.fontStyle = FontStyles.Italic;
        // 0.50: ContentSizeFitter so this wrapped subtext grows with the font scale
        // instead of clipping under the "Use combined overlay" toggle at Large+ text.
        var helpFitter = help.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        helpFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        helpFitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        // Master toggle.
        var masterRow = UIFactory.CreateHorizontalGroup(page, "CombinedMasterRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(masterRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);

        var masterT = UIFactory.CreateToggle(masterRow, "CombinedMasterToggle");
        UIFactory.SetLayoutElement(masterT.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        masterT.Text.text = "Use combined overlay (hides individual info overlays)";
        masterT.Text.fontSize = Theme.ScaledUI(13);
        masterT.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(masterT.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        masterT.Toggle.isOn = Config.Settings.ShowCombinedOverlay;
        _combinedMasterToggle = masterT.Toggle; // track for footer-click sync
        TooltipHover.Attach(masterT.GameObject,
            "When on, Raphael replaces the standalone XP / Familiar / Daily Quest / Professions overlays with a single combined panel. Familiar Browser and Shift Spell overlays stay independent.");
        masterT.OnValueChanged += v =>
        {
            Config.Settings.SetShowCombinedOverlay(v);
            Plugin.UIManager?.ApplyCombinedOverlayMutualExclusion();
            ApplyCombinedFooterVisibility();
            Plugin.UIManager?.RefreshCombinedOverlaySections();
            // 0.14.0 friend-test v2: full toggle sync via the shared helper.
            // The earlier SetIsOnWithoutNotify on just _combinedOverlayToggle
            // wasn't enough — the 4 individual footer toggles (XP / Familiar /
            // Daily quest / Professions) also need their isOn refreshed
            // because mutual-exclusion may have just changed which overlays
            // are actually enabled.
            RefreshAllOverlayToggleStates();
        };

        // Per-section checkboxes — 2 rows × 3 toggles each so they fit
        // alongside the existing per-profession toggle pattern.
        AddSpacer(page, 4);
        var row1 = UIFactory.CreateHorizontalGroup(page, "CombinedSectionsRow1",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row1,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        // 0.14.0 friend-test v2: per-component checkboxes now write the SAME
        // Settings.Show*Overlay flags that the footer toggles use. Both UI
        // surfaces stay in sync because both read/write the same setting —
        // RefreshAllOverlayToggleStates pushes the value to whichever UI
        // surface didn't initiate the change.
        AddCombinedSectionToggleUnified(row1, "XP",       () => Config.Settings.ShowExperienceOverlay, Config.Settings.SetShowExperienceOverlay);
        AddCombinedSectionToggleUnified(row1, "Familiar", () => Config.Settings.ShowFamiliarOverlay,   Config.Settings.SetShowFamiliarOverlay);
        // Weapon / Blood have no standalone equivalent — they're sub-rows
        // of the standalone XP overlay. The Combined-only flags stay.
        AddCombinedSectionToggle(row1, "Weapon",     () => Config.Settings.CombinedOverlayShowExpertise, Config.Settings.SetCombinedOverlayShowExpertise);

        var row2 = UIFactory.CreateHorizontalGroup(page, "CombinedSectionsRow2",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row2,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        AddCombinedSectionToggle(row2, "Blood",        () => Config.Settings.CombinedOverlayShowLegacy, Config.Settings.SetCombinedOverlayShowLegacy);
        AddCombinedSectionToggleUnified(row2, "Professions", () => Config.Settings.ShowProfessionOverlay, Config.Settings.SetShowProfessionOverlay);
        AddCombinedSectionToggleUnified(row2, "Quests",      () => Config.Settings.ShowDailyQuestOverlay, Config.Settings.SetShowDailyQuestOverlay);

        // Per-section bar toggles moved to HUD extras → "Show progress bars
        // for" — they apply to both standalone and combined overlays so
        // they belong in the shared section, not nested under Combined.
    }

    /// <summary>0.14.0 friend-test v2: variant of AddCombinedSectionToggle
    /// for the systems that share visibility between standalone overlay and
    /// combined section (XP / Familiar / Professions / Quests). Writes the
    /// unified Setting AND fires the manager's mutual-exclusion sync so
    /// standalone overlays appear / disappear immediately when toggled.</summary>
    private void AddCombinedSectionToggleUnified(GameObject row, string label,
        System.Func<bool> get, System.Action<bool> set)
    {
        var t = UIFactory.CreateToggle(row, $"CombinedSec_{label}");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 110, preferredWidth: 130, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 80, preferredWidth: 100, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        t.Toggle.isOn = get();
        TooltipHover.Attach(t.GameObject,
            $"Show the {label} system — affects BOTH the standalone {label} overlay AND the corresponding section in the combined overlay. One flag controls both views.");
        t.OnValueChanged += v =>
        {
            set(v);
            Plugin.UIManager?.RefreshCombinedOverlaySections();
            Plugin.UIManager?.ApplyCombinedOverlayMutualExclusion();
            RefreshAllOverlayToggleStates();
        };
    }

    /// <summary>0.14.0 friend-test v2: push the current Settings.Show*Overlay
    /// values to every footer overlay toggle's UI via SetIsOnWithoutNotify.
    /// Resolves the desync where toggling combined-mode (which hides /
    /// re-shows individual overlays per their flags) left the footer
    /// toggle checkboxes showing stale construct-time state.</summary>
    public void RefreshAllOverlayToggleStates()
    {
        if (_xpOverlayToggle       != null) _xpOverlayToggle.SetIsOnWithoutNotify(Plugin.UIManager?.IsOverlayOpen(PanelType.ExperienceOverlay) ?? false);
        if (_famOverlayToggle      != null) _famOverlayToggle.SetIsOnWithoutNotify(Plugin.UIManager?.IsOverlayOpen(PanelType.FamiliarOverlay) ?? false);
        if (_famBrowserToggle      != null) _famBrowserToggle.SetIsOnWithoutNotify(Plugin.UIManager?.IsOverlayOpen(PanelType.FamiliarBrowserOverlay) ?? false);
        if (_quickSpawnOverlayToggle != null) _quickSpawnOverlayToggle.SetIsOnWithoutNotify(Plugin.UIManager?.IsOverlayOpen(PanelType.FamiliarQuickSpawnOverlay) ?? false);
        if (_bossTrackerOverlayToggle != null) _bossTrackerOverlayToggle.SetIsOnWithoutNotify(Plugin.UIManager?.IsOverlayOpen(PanelType.FaustBossTrackerOverlay) ?? false);
        if (_dqOverlayToggle       != null) _dqOverlayToggle.SetIsOnWithoutNotify(Plugin.UIManager?.IsOverlayOpen(PanelType.DailyQuestOverlay) ?? false);
        if (_profOverlayToggle     != null) _profOverlayToggle.SetIsOnWithoutNotify(Plugin.UIManager?.IsOverlayOpen(PanelType.ProfessionOverlay) ?? false);
        if (_shiftOverlayToggle    != null) _shiftOverlayToggle.SetIsOnWithoutNotify(Plugin.UIManager?.IsOverlayOpen(PanelType.ShiftSpellOverlay) ?? false);
        if (_quickActionsOverlayToggle != null) _quickActionsOverlayToggle.SetIsOnWithoutNotify(Plugin.UIManager?.IsOverlayOpen(PanelType.QuickActionsOverlay) ?? false);
        if (_beelzOverlayToggle    != null) _beelzOverlayToggle.SetIsOnWithoutNotify(Plugin.UIManager?.IsOverlayOpen(PanelType.BeelzActionBarOverlay) ?? false);
        if (_beelzSummonsOverlayToggle != null) _beelzSummonsOverlayToggle.SetIsOnWithoutNotify(Plugin.UIManager?.IsOverlayOpen(PanelType.BeelzSummonsOverlay) ?? false);
        if (_beelzTransformOverlayToggle != null) _beelzTransformOverlayToggle.SetIsOnWithoutNotify(Plugin.UIManager?.IsOverlayOpen(PanelType.BeelzTransformOverlay) ?? false);
        if (_combinedOverlayToggle != null) _combinedOverlayToggle.SetIsOnWithoutNotify(Config.Settings.ShowCombinedOverlay);
        if (_combinedMasterToggle  != null) _combinedMasterToggle.SetIsOnWithoutNotify(Config.Settings.ShowCombinedOverlay);
    }

    private static void AddCombinedSectionToggle(GameObject row, string label,
        System.Func<bool> get, System.Action<bool> set)
    {
        var t = UIFactory.CreateToggle(row, $"CombinedSec_{label}");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 110, preferredWidth: 130, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 80, preferredWidth: 100, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        t.Toggle.isOn = get();
        TooltipHover.Attach(t.GameObject,
            $"Show the {label} section inside the combined overlay. No effect when combined-mode is off.");
        t.OnValueChanged += v =>
        {
            set(v);
            Plugin.UIManager?.RefreshCombinedOverlaySections();
        };
    }

    private void BuildProfessionTrackedSection(GameObject page)
    {
        AddSectionHeading(page, "Professions tracked");

        var help = UIFactory.CreateLabel(page, "ProfTrackedHelp",
            "Choose which of the eight Bloodcraft professions appear on the Professions overlay. " +
            "Defaults show all eight (preserves pre-0.13.0 behavior). Each row hides immediately when its checkbox is cleared.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(help.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 32, flexibleHeight: 0);
        help.TextMesh.enableWordWrapping = true;
        help.TextMesh.overflowMode = TextOverflowModes.Overflow;
        help.TextMesh.fontStyle = FontStyles.Italic;

        var row1 = UIFactory.CreateHorizontalGroup(page, "ProfTogglesRow1",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row1,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        AddProfessionTrackedToggle(row1, "Enchanting",    () => Config.Settings.ShowProfessionEnchanting,    Config.Settings.SetShowProfessionEnchanting);
        AddProfessionTrackedToggle(row1, "Alchemy",       () => Config.Settings.ShowProfessionAlchemy,       Config.Settings.SetShowProfessionAlchemy);
        AddProfessionTrackedToggle(row1, "Harvesting",    () => Config.Settings.ShowProfessionHarvesting,    Config.Settings.SetShowProfessionHarvesting);
        AddProfessionTrackedToggle(row1, "Blacksmithing", () => Config.Settings.ShowProfessionBlacksmithing, Config.Settings.SetShowProfessionBlacksmithing);

        var row2 = UIFactory.CreateHorizontalGroup(page, "ProfTogglesRow2",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row2,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        AddProfessionTrackedToggle(row2, "Tailoring",   () => Config.Settings.ShowProfessionTailoring,   Config.Settings.SetShowProfessionTailoring);
        AddProfessionTrackedToggle(row2, "Woodcutting", () => Config.Settings.ShowProfessionWoodcutting, Config.Settings.SetShowProfessionWoodcutting);
        AddProfessionTrackedToggle(row2, "Mining",      () => Config.Settings.ShowProfessionMining,      Config.Settings.SetShowProfessionMining);
        AddProfessionTrackedToggle(row2, "Fishing",     () => Config.Settings.ShowProfessionFishing,     Config.Settings.SetShowProfessionFishing);
    }

    private static void AddProfessionTrackedToggle(GameObject row, string label,
        System.Func<bool> get, System.Action<bool> set)
    {
        var t = UIFactory.CreateToggle(row, $"ProfTog_{label}");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 80, preferredWidth: 95, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(11);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 60, preferredWidth: 75, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = get();
        TooltipHover.Attach(t.GameObject,
            $"Show the {label} row on the Professions overlay. Default: on.");
        t.OnValueChanged += value =>
        {
            set(value);
            Plugin.UIManager?.RefreshProfessionOverlay();
        };
    }

    private void AddShowPrestigeSubLineToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "ShowPrestigeSubLineRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "ShowPrestigeSubLineToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Show prestige-progress sub-line in progress bars (Eclipse-style)";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.ShowPrestigeSubLine;
        TooltipHover.Attach(t.GameObject,
            "Adds a slim inset fill at the bottom of each progress bar reflecting how close you are to the next prestige tier (Level / MaxLevel for that system). Mirrors Eclipse's overlay style. Requires Show Progress Bars to be on.");
        t.OnValueChanged += v => Config.Settings.SetShowPrestigeSubLine(v);
    }

    // 0.16.x: Settings-page toggle for the overlays-vs-game-menus z-order
    // (config key OverlaysBehindGameMenus). Friend-test feedback asked for a
    // UI control instead of editing the .cfg. The UICanvasSystemPatch reads the
    // setting every frame, so flipping it takes effect immediately.
    private void AddOverlaysBehindMenusToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "OverlaysBehindMenusRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "OverlaysBehindMenusToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Send overlays behind in-game menus (inventory, character, map…)";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.OverlaysBehindGameMenus;
        TooltipHover.Attach(t.GameObject,
            "ON: while a game menu (inventory, character sheet, map, etc.) is open, Raphael's overlays drop behind it, then return on top when you close it. OFF: overlays always stay on top of game menus (the pre-0.16 behavior). Takes effect immediately.");
        t.OnValueChanged += v => Config.Settings.SetOverlaysBehindGameMenus(v);
    }

    // 0.16.x: Settings-page toggle for the "freeze character actions while the
    // main panel is open" input-suppression feature (config key
    // SuppressGameInputWhileUIOpen). Default off; the InputSuppressionPatch reads
    // the setting every frame so flipping it takes effect immediately.
    private void AddSuppressInputToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "SuppressInputRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "SuppressInputToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Freeze character actions while the main panel is open";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.SuppressGameInputWhileUIOpen;
        TooltipHover.Attach(t.GameObject,
            "When ON, your character won't move, attack, or cast (including hotkeyed commands) while the Raphael main panel is open — so background actions don't fire while you click buttons or type into forms. Aim/camera still works. Takes effect immediately. Default OFF.");
        t.OnValueChanged += v => Config.Settings.SetSuppressGameInputWhileUIOpen(v);
    }

    // B3 (0.19): toggle for suppressing the primary attack / cast when left-clicking over ANY Raphael
    // panel/overlay (the chat window is always covered). Default OFF; read every frame by
    // InputSuppression.ShouldBlockAbilities so flipping it takes effect immediately.
    private void AddBlockInputOverUiToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "BlockInputOverUiRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "BlockInputOverUiToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Don't attack/cast when clicking over a Raphael panel or overlay";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.BlockInputWhenPointerOverUI;
        TooltipHover.Attach(t.GameObject,
            "When ON, a LEFT-CLICK while the cursor is over any Raphael panel or overlay won't fire your primary attack or spell cast — so clicking a button or an overlay can't leak into the world as an attack / stuck cast (the chat window is always protected regardless). Only the attack/cast is suppressed — movement, camera, and menus are untouched, so it can't freeze the game. Default OFF.");
        t.OnValueChanged += v => Config.Settings.SetBlockInputWhenPointerOverUI(v);
    }

    /// <summary>0.10.2: cycle button — text alignment for overlay rows
    /// (Left = default; Right = useful when the overlay is pinned to the
    /// right edge of the screen). Toggling rebuilds open overlays so the
    /// new alignment takes effect immediately — labels capture alignment
    /// at construct time, so a flag flip without rebuild wouldn't visually
    /// update existing labels.</summary>
    private void AddOverlayAlignmentToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "OverlayAlignRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var label = UIFactory.CreateLabel(row, "OverlayAlignLabel",
            "Overlay text alignment:", TextAlignmentOptions.MidlineLeft,
            color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(label.GameObject,
            minWidth: 200, preferredWidth: 240, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var btn = UIFactory.CreateButton(row, "OverlayAlignBtn", FormatOverlayAlignText());
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 80, preferredWidth: 100, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(12); t.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(btn.GameObject,
            "Cycle between Left and Right text alignment for ALL overlays (XP, Familiar, Familiar Browser, Daily Quest, Professions). Right is handy when you've pinned an overlay to the right edge of the screen and want the values closer to the panel border.");
        btn.OnClick = () =>
        {
            var next = Config.Settings.OverlayTextAlignmentSetting == Config.Settings.OverlayAlignment.Left
                ? Config.Settings.OverlayAlignment.Right
                : Config.Settings.OverlayAlignment.Left;
            Config.Settings.SetOverlayTextAlignment(next);
            // Refresh the button label, then trigger overlay rebuilds so the
            // alignment baked into existing labels gets re-applied.
            var newTxt = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (newTxt != null) newTxt.text = FormatOverlayAlignText();
            Plugin.UIManager?.RequestRebuildAllOverlays();
        };
    }

    private static string FormatOverlayAlignText()
        => Config.Settings.OverlayTextAlignmentSetting == Config.Settings.OverlayAlignment.Right ? "Right" : "Left";

    /// <summary>0.67.2: the "Chat noise" controls moved to Settings and Help → Chat & Diagnostics (one place for
    /// every chat-visibility and diagnostics setting). This card just points there.</summary>
    private void BuildChatNoiseSection(GameObject page)
    {
        AddSectionHeading(page, "Chat noise");
        AddBodyText(page,
            "What Raphael hides from chat, plus diagnostics for every mod, now lives in one place.");
        var row = MakeUrielRow(page, "ChatNoiseGoRow");
        AddUrielButton(row, "ChatNoiseGo", "Open Chat & Diagnostics", "Go to Settings and Help → Chat & Diagnostics.",
            () => ShowTabDeferred(PanelType.ChatDiagTab), 220);
    }

    /// <summary>Font-size control: a slider (50–400%) + a typed value box + Apply, replacing the old
    /// Small/Standard/Large/X-Large buttons so users can dial in an exact size for their monitor/TV (where the
    /// old fixed steps looked drastically off). Dragging the slider or typing a value stores it live; Apply (or
    /// reopening the panel) rebuilds so existing labels pick up the new size. 100% = the old "Standard".</summary>
    private void AddTextScaleRow(GameObject parent, string label,
                                 System.Func<float> currentScaleSetting,
                                 System.Action<float> setScale,
                                 System.Action rebuild)
    {
        const int MINPCT = 50, MAXPCT = 400;

        var row = UIFactory.CreateHorizontalGroup(parent, $"FontScaleRow_{label}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var lbl = UIFactory.CreateLabel(row, $"FsLbl_{label}", $"{label}:",
            TMPro.TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: Theme.ScaledWidth(120), preferredWidth: Theme.ScaledWidth(140), flexibleWidth: 0, minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        int startPct = Mathf.Clamp(Mathf.RoundToInt(currentScaleSetting() * 100f), MINPCT, MAXPCT);

        var sliderGo = UIFactory.CreateSlider(row, $"FsSlider_{label}", out var slider);
        UIFactory.SetLayoutElement(sliderGo,
            minWidth: 110, preferredWidth: 160, flexibleWidth: 1, minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        slider.minValue = MINPCT; slider.maxValue = MAXPCT; slider.wholeNumbers = true; slider.value = startPct;

        var gap = UIFactory.CreateUIObject($"FsGap_{label}", row);
        UIFactory.SetLayoutElement(gap, minWidth: 10, preferredWidth: 10, flexibleWidth: 0, minHeight: 10, preferredHeight: 10, flexibleHeight: 0);

        var box = UIFactory.CreateInputField(row, $"FsInput_{label}", "100");
        UIFactory.SetLayoutElement(box.GameObject,
            minWidth: 48, preferredWidth: 52, flexibleWidth: 0, minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        box.Component.contentType = TMPro.TMP_InputField.ContentType.IntegerNumber;
        box.Component.characterLimit = 3;
        box.Text = startPct.ToString();

        var pctLbl = UIFactory.CreateLabel(row, $"FsPct_{label}", "%",
            TMPro.TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(pctLbl.GameObject,
            minWidth: Theme.ScaledWidth(14), preferredWidth: Theme.ScaledWidth(16), flexibleWidth: 0, minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        var applyBtn = UIFactory.CreateButton(row, $"FsApply_{label}", "Apply");
        UIFactory.SetLayoutElement(applyBtn.GameObject,
            minWidth: Theme.ScaledWidth(60), preferredWidth: Theme.ScaledWidth(64), flexibleWidth: 0, minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        { var t = applyBtn.Component.GetComponentInChildren<TextMeshProUGUI>(); if (t != null) t.fontSize = Theme.ScaledUI(11); }

        bool syncing = false;
        void StoreLive(int pct) => setScale(Mathf.Clamp(pct, MINPCT, MAXPCT) / 100f);

        // Slider drag previews the value + stores it live, but does NOT rebuild on every tick (that would
        // destroy the slider mid-drag). The user commits with Apply (or by typing + Enter), or it takes effect
        // next time the panel/overlay is (re)opened.
        System.Action<float> onSlider = v =>
        {
            if (syncing) return; syncing = true;
            int pct = Mathf.Clamp(Mathf.RoundToInt(v), MINPCT, MAXPCT);
            box.Text = pct.ToString();
            StoreLive(pct);
            syncing = false;
        };
        System.Action<string> onBox = s =>
        {
            if (syncing) return; syncing = true;
            if (!int.TryParse(s, out int pct)) pct = Mathf.RoundToInt(currentScaleSetting() * 100f);
            pct = Mathf.Clamp(pct, MINPCT, MAXPCT);
            box.Text = pct.ToString();
            slider.value = pct;
            StoreLive(pct);
            syncing = false;
            rebuild();   // committing a typed value applies it immediately
        };
        slider.onValueChanged.AddListener(onSlider);
        box.Component.onEndEdit.AddListener(onBox);
        applyBtn.OnClick = () => { StoreLive(Mathf.Clamp(Mathf.RoundToInt(slider.value), MINPCT, MAXPCT)); rebuild(); };
    }

    // Small segmented-control button used by various labeled choice rows. (Formerly also drove the text-size
    // row, which is now a slider; kept here because other rows still use it.)
    private static void AddScaleButton(GameObject row, string text, System.Action onClick)
    {
        var btn = UIFactory.CreateButton(row, $"Btn_{text}", text);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: Theme.ScaledWidth(64), preferredWidth: Theme.ScaledWidth(72), flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.fontSize = Theme.ScaledUI(11);
        btn.OnClick = () => onClick();
    }

    /// <summary>B6 (0.19): renders a labeled row with a live SLIDER (0–100%) plus a typed numeric box
    /// for a single overlay's background transparency. 0% = solid, 100% = fully invisible. Replaces the
    /// old five-button presets — testers wanted fine control and a typed value, and true 100% (the floor
    /// was raised to 1.0 in Settings). Slider and box stay in sync; both apply immediately + refresh live
    /// opacities so the change is visible without toggling the overlay off and on.</summary>
    private void AddTransparencyRow(GameObject parent, string overlayLabel,
                                    System.Func<float> currentValue,
                                    System.Action<float> applyValue)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"OpacityRow_{overlayLabel}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var lbl = UIFactory.CreateLabel(row, $"Lbl_{overlayLabel}",
            $"{overlayLabel}:",
            TMPro.TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: Theme.ScaledWidth(130), preferredWidth: Theme.ScaledWidth(150), flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        int startPct = Mathf.Clamp(Mathf.RoundToInt(currentValue() * 100f), 0, 100);

        var sliderGo = UIFactory.CreateSlider(row, $"OpacitySlider_{overlayLabel}", out var slider);
        UIFactory.SetLayoutElement(sliderGo,
            minWidth: 120, preferredWidth: 180, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.wholeNumbers = true;
        slider.value = startPct;

        // Breathing room so the slider handle (at 100%) doesn't butt up against the numeric box.
        var gap = UIFactory.CreateUIObject($"OpacityGap_{overlayLabel}", row);
        UIFactory.SetLayoutElement(gap,
            minWidth: 12, preferredWidth: 12, flexibleWidth: 0,
            minHeight: 10, preferredHeight: 10, flexibleHeight: 0);

        var box = UIFactory.CreateInputField(row, $"OpacityInput_{overlayLabel}", "0");
        UIFactory.SetLayoutElement(box.GameObject,
            minWidth: 44, preferredWidth: 48, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        box.Component.contentType = TMPro.TMP_InputField.ContentType.IntegerNumber;
        box.Component.characterLimit = 3;
        box.Text = startPct.ToString();

        var pctLbl = UIFactory.CreateLabel(row, $"OpacityPct_{overlayLabel}", "%",
            TMPro.TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(pctLbl.GameObject,
            minWidth: Theme.ScaledWidth(14), preferredWidth: Theme.ScaledWidth(16), flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        // Re-entry guard so slider→box and box→slider mirroring doesn't recurse.
        bool syncing = false;
        void Apply(int pct)
        {
            pct = Mathf.Clamp(pct, 0, 100);
            applyValue(pct / 100f);
            Plugin.UIManager.RefreshAllOpacities();
        }
        System.Action<float> onSlider = v =>
        {
            if (syncing) return;
            syncing = true;
            int pct = Mathf.Clamp(Mathf.RoundToInt(v), 0, 100);
            box.Text = pct.ToString();
            Apply(pct);
            syncing = false;
        };
        System.Action<string> onBox = s =>
        {
            if (syncing) return;
            syncing = true;
            if (!int.TryParse(s, out int pct)) pct = Mathf.RoundToInt(currentValue() * 100f);
            pct = Mathf.Clamp(pct, 0, 100);
            box.Text = pct.ToString();
            slider.value = pct;
            Apply(pct);
            syncing = false;
        };
        slider.onValueChanged.AddListener(onSlider);
        box.Component.onEndEdit.AddListener(onBox);
    }

    // 0.12.0: panel color preset sections. Two zones — "Panel background"
    // (outer chrome of every panel) and "Interior background" (scroll-view
    // wrappers + viewports inside the main panel and Familiar Browser).
    // Hex strings stored in Settings.{Panel,Inner}PanelBackgroundColorHex;
    // power users can hand-edit the .cfg for any specific color, this UI
    // surfaces a row of seven curated presets per zone.
    private TextMeshProUGUI _panelBgCurrentLabel;
    private TextMeshProUGUI _innerBgCurrentLabel;
    private TextMeshProUGUI _buttonBgCurrentLabel; // 0.18.4: button color "Current:" label

    private void BuildPanelBackgroundColorSection(GameObject page)
    {
        // ── Outer (panel background, all panels) ────────────────────────
        AddSectionHeading(page, "Panel background color");

        AddPanelColorHelp(page, "PanelBgHelp",
            "Sets the OUTER background color of every panel Raphael builds — the main panel, the Familiar Browser, and all five info overlays (XP, Familiar, Daily Quest, Profession, Shift Spell). " +
            "Light colors may reduce text legibility — labels assume a dark background. Transparency per panel is configured by the sliders above; this picker controls hue only.");

        AddPanelColorPresetRow(page, "PanelBgPresetRow", ApplyOuterPanelBgHex);

        _panelBgCurrentLabel = AddPanelColorInfoRow(page, "PanelBgInfoRow",
            FormatOuterBgCurrentText,
            resetHex: Config.Settings.DEFAULT_PANEL_BG_HEX,
            resetTooltip: "Restore the default panel background color (#121212 — near-black, the pre-0.12.0 look).",
            applyAction: ApplyOuterPanelBgHex);

        AddSpacer(page, 6);

        // ── Inner (scroll-view interior, main + familiar browser) ───────
        AddSectionHeading(page, "Interior background color");

        AddPanelColorHelp(page, "InnerBgHelp",
            "Sets the INTERIOR background — the scroll-view area where tab content shows in the main panel and where familiar rows show in the Familiar Browser. Pre-0.12.0 this was bright red by framework default (UIFactory.CreateScrollView used Theme.Level1). " +
            "Two palettes are offered — the dark row keeps the muted modern look, the bright row restores the saturation similar to that original red (Crimson Bright = #A30000 = the pre-0.12.0 default exactly). " +
            "Independent of the outer color above so you can build a two-tone theme. Smaller info overlays don't host scroll views so this picker doesn't affect them.");

        AddPanelColorSubHeading(page, "InnerBgDarkLabel", "Dark variants");
        AddPanelColorPresetRow(page, "InnerBgPresetDarkRow", ApplyInnerPanelBgHex, DefaultDarkPresets);

        AddPanelColorSubHeading(page, "InnerBgBrightLabel", "Bright variants");
        AddPanelColorPresetRow(page, "InnerBgPresetBrightRow", ApplyInnerPanelBgHex, DefaultBrightPresets);

        _innerBgCurrentLabel = AddPanelColorInfoRow(page, "InnerBgInfoRow",
            FormatInnerBgCurrentText,
            resetHex: Config.Settings.DEFAULT_INNER_BG_HEX,
            resetTooltip: "Restore the default interior background (#121212 near-black — masks the framework-default red.)",
            applyAction: ApplyInnerPanelBgHex);
    }

    // 0.12.1: thin italic sub-label for inside a color-picker section.
    // Smaller and quieter than AddSectionHeading so two preset rows visually
    // group together under "Interior background color" without looking like
    // a wall of headings. Uses DefaultText (white) rather than MutedBodyHex
    // for the same readability reasons documented on AddPanelColorHelp —
    // mid-luminance bright presets erase grey-on-grey contrast.
    private static void AddPanelColorSubHeading(GameObject page, string name, string text)
    {
        var lbl = UIFactory.CreateLabel(page, name, text,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Italic;
    }

    private static void AddPanelColorHelp(GameObject page, string name, string body)
    {
        // 0.12.1: render help paragraphs in italic + DefaultText (white).
        // Pre-fix this used <color=Theme.MutedBodyHex> (#8E8E8E mid-grey),
        // which lost contrast against the brighter interior presets
        // (Default Bright #666666, Forest Bright #2A6E2E, Crimson Bright
        // #A30000) — muted-on-mid-grey is unreadable. White italic stays
        // hierarchically distinct from the bold section heading above
        // while reading cleanly on every preset, dark and bright.
        var help = UIFactory.CreateLabel(page, name, body,
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(help.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 36, preferredHeight: 52, flexibleHeight: 0);
        help.TextMesh.enableWordWrapping = true;
        help.TextMesh.overflowMode = TextOverflowModes.Overflow;
        help.TextMesh.fontStyle = FontStyles.Italic;
    }

    // Seven-button preset row, parameterized over which Apply action it
    // calls AND which preset list it shows. Same row layout drives every
    // color picker — outer (one dark row) and inner (one dark + one bright).
    private void AddPanelColorPresetRow(GameObject page, string name, System.Action<string> applyAction,
        (string Label, string Hex)[] presets = null)
    {
        presets ??= DefaultDarkPresets;

        var row = UIFactory.CreateHorizontalGroup(page, name,
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        foreach (var p in presets)
            AddPanelBgPresetButton(row, p.Label, p.Hex, applyAction);
    }

    // 0.12.1: paired dark / bright preset palettes. The dark row is the
    // default (used by every color picker); the bright row is offered
    // additionally for the interior picker after user feedback that some
    // players prefer the pre-0.12.0 saturated look (the framework default
    // Theme.Level1 = (0.64, 0, 0) — actual bright red). Each bright entry
    // shares the hue of its dark sibling but lifts the max channel to
    // roughly 0.5–0.65 — Crimson Bright (#A30000) matches Theme.Level1
    // RGB exactly so the original framework red is one click away.
    private static readonly (string Label, string Hex)[] DefaultDarkPresets = new[]
    {
        ("Default", Config.Settings.DEFAULT_PANEL_BG_HEX),
        ("Black",   "#000000"),
        ("Slate",   "#1A1B25"),
        ("Wine",    "#1F0A10"),
        ("Forest",  "#0A1A0B"),
        ("Indigo",  "#0E0A1F"),
        ("Crimson", "#3B0B0F"),
        // 0.17.3: approximates V Rising's native tooltip/quest-box blue (the dark
        // navy Eclipse's quest panel shows — Eclipse reuses the game's FakeTooltip
        // prefab, so there's no literal value to copy; tune via the hex field).
        ("Eclipse", "#16243F"),
    };
    private static readonly (string Label, string Hex)[] DefaultBrightPresets = new[]
    {
        ("Default", "#666666"),  // medium neutral grey
        ("Black",   "#404040"),  // dark grey — brighter neutral twin of #000
        ("Slate",   "#4A5070"),  // brighter blue-grey
        ("Wine",    "#8B1A2E"),  // burgundy
        ("Forest",  "#2A6E2E"),  // moss green
        ("Indigo",  "#3D2D80"),  // royal indigo
        ("Crimson", "#A30000"),  // exactly Theme.Level1 — the pre-0.12.0 framework red
        ("Eclipse", "#2B4A7A"),  // brighter twin of the quest-box navy
    };

    // "Current: #hex" label + Reset button row. Returns the TMP_Text so the
    // caller can keep a reference and refresh it on each pick.
    private TextMeshProUGUI AddPanelColorInfoRow(GameObject page, string name,
        System.Func<string> currentText,
        string resetHex,
        string resetTooltip,
        System.Action<string> applyAction)
    {
        var info = UIFactory.CreateHorizontalGroup(page, name,
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(info,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var current = UIFactory.CreateLabel(info, $"{name}_Current",
            currentText(),
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(current.GameObject,
            minWidth: 200, preferredWidth: 260, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        current.TextMesh.fontStyle = FontStyles.Italic;

        var reset = UIFactory.CreateButton(info, $"{name}_Reset", "Reset");
        UIFactory.SetLayoutElement(reset.GameObject,
            minWidth: 54, preferredWidth: 60, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var resetText = reset.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (resetText != null) resetText.fontSize = Theme.ScaledUI(11);
        reset.OnClick = () => applyAction(resetHex);
        TooltipHover.Attach(reset.GameObject, resetTooltip);

        return current.TextMesh;
    }

    private void AddPanelBgPresetButton(GameObject row, string label, string hex, System.Action<string> applyAction)
    {
        var btn = UIFactory.CreateButton(row, $"PanelBg_{row.name}_{label}", label);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 50, preferredWidth: 60, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.fontSize = Theme.ScaledUI(11);
        btn.OnClick = () => applyAction(hex);
        TooltipHover.Attach(btn.GameObject, $"Apply preset {label} ({hex}).");
    }

    private void ApplyOuterPanelBgHex(string hex)
    {
        Config.Settings.SetPanelBackgroundColorHex(hex);
        Plugin.UIManager?.RefreshAllPanelBackgrounds();
        if (_panelBgCurrentLabel != null) _panelBgCurrentLabel.text = FormatOuterBgCurrentText();
    }

    private void ApplyInnerPanelBgHex(string hex)
    {
        Config.Settings.SetInnerPanelBackgroundColorHex(hex);
        Plugin.UIManager?.RefreshScopedInnerBackgrounds();
        if (_innerBgCurrentLabel != null) _innerBgCurrentLabel.text = FormatInnerBgCurrentText();
    }

    private static string FormatOuterBgCurrentText()
        => $"Current: {Config.Settings.PanelBackgroundColorHex}";
    private static string FormatInnerBgCurrentText()
        => $"Current: {Config.Settings.InnerPanelBackgroundColorHex}";

    // 0.18.4: button background-color picker. Reuses the same preset-row + info-row infrastructure as
    // the panel color pickers; recolors every themed button live (BCHubUIManager.RefreshAllButtonColors).
    private static readonly (string Label, string Hex)[] ButtonColorPresets = new[]
    {
        ("Default", Config.Settings.DEFAULT_BUTTON_BG_HEX), // #4D4D4D neutral grey
        ("Dark",    "#2A2A2A"),
        ("Slate",   "#3A4256"),
        ("Steel",   "#46505C"),
        ("Wine",    "#5A2230"),
        ("Forest",  "#2A4A2E"),
        ("Indigo",  "#352B5E"),
        ("Crimson", "#6E1F26"),
    };

    private void BuildButtonColorSection(GameObject page)
    {
        AddSectionHeading(page, "Button color");
        AddPanelColorHelp(page, "ButtonColorHelp",
            "Sets the background color of the buttons Raphael builds — the Raphael / OV launcher buttons, Stash All, the Familiar Browser buttons, and most others. " +
            "Buttons with a deliberate color (the red Danger / WIPE buttons) keep their own color. Changes apply immediately.");
        AddPanelColorPresetRow(page, "ButtonColorPresetRow", ApplyButtonBgHex, ButtonColorPresets);
        _buttonBgCurrentLabel = AddPanelColorInfoRow(page, "ButtonColorInfoRow",
            FormatButtonBgCurrentText,
            resetHex: Config.Settings.DEFAULT_BUTTON_BG_HEX,
            resetTooltip: "Restore the default button color (#4D4D4D neutral grey — the original look).",
            applyAction: ApplyButtonBgHex);
    }

    private void ApplyButtonBgHex(string hex)
    {
        Config.Settings.SetButtonBackgroundColorHex(hex);
        Plugin.UIManager?.RefreshAllButtonColors();
        if (_buttonBgCurrentLabel != null) _buttonBgCurrentLabel.text = FormatButtonBgCurrentText();
    }

    private static string FormatButtonBgCurrentText()
        => $"Current: {Config.Settings.ButtonBackgroundColorHex}";

    // 0.18.4: launcher (Raphael/OV) button size — a segmented control 60%–120%, applied live via the
    // floating panel's localScale (Settings.FloatingButtonScale). Mirrors AddTextScaleRow's layout.
    private void AddLauncherButtonSizeRow(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "DisplayRow_LauncherSize",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        var lbl = UIFactory.CreateLabel(row, "Lbl_LauncherSize", "Launcher button size:",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: Theme.ScaledWidth(140), preferredWidth: Theme.ScaledWidth(160), flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        var hint = UIFactory.CreateLabel(row, "Hint_LauncherSize",
            FormatLauncherSizeHint(Config.Settings.FloatingButtonScale),
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(hint.GameObject,
            minWidth: Theme.ScaledWidth(80), preferredWidth: Theme.ScaledWidth(90), flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        hint.TextMesh.fontStyle = FontStyles.Italic;

        void Pick(float v)
        {
            Config.Settings.SetFloatingButtonScale(v);
            Plugin.UIManager?.RefreshFloatingButtonScale();
            hint.TextMesh.text = FormatLauncherSizeHint(v);
        }
        AddScaleButton(row, "60%",  () => Pick(0.6f));
        AddScaleButton(row, "75%",  () => Pick(0.75f));
        AddScaleButton(row, "85%",  () => Pick(0.85f));
        AddScaleButton(row, "100%", () => Pick(1.0f));
        AddScaleButton(row, "120%", () => Pick(1.2f));
    }

    private static string FormatLauncherSizeHint(float v) => $"(current: {v * 100f:0}%)";

    /// <summary>One row showing a label, the URL, and an "Open" button that
    /// hands the URL to <see cref="UnityEngine.Application.OpenURL"/> so the
    /// system browser opens it. Cleaner than wiring TMPro &lt;link&gt; click
    /// handlers under IL2CPP.</summary>
    private static void AddLinkRow(GameObject parent, string label, string url)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"LinkRow_{label}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 1, 1));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var lbl = UIFactory.CreateLabel(row, "LinkLbl",
            $"{label}:  {url}",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 240, preferredWidth: 320, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;

        var btn = UIFactory.CreateButton(row, "OpenBtn", "Open");
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 56, preferredWidth: 64, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var btnText = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null) btnText.fontSize = Theme.ScaledUI(12);
        btn.OnClick = () =>
        {
            try { UnityEngine.Application.OpenURL(url); }
            catch (System.Exception ex) { LogUtils.LogWarning($"OpenURL('{url}') threw: {ex.Message}"); }
        };
        TooltipHover.Attach(btn.GameObject, $"Open {url} in your default browser.");
    }

    private static void AddGuideSection(GameObject parent, string title, string body)
    {
        AddSectionHeading(parent, title);

        // 0.13.0: bumped body fontSize 12 → 14 for the prose-heavy help-group
        // tabs (Quick Start / Mod Help / Game Guide / Settings descriptions).
        // Friend-test: "these pages contain a lot of information, harder to
        // read than other pages because the text is small." TMP wraps on
        // preferredWidth = 400 so the wider glyphs still fit cleanly.
        var lbl = UIFactory.CreateLabel(parent, $"Guide_{title}", body,
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(14));
        // Guide body sized by TMP's actual rendered preferredHeight via a
        // ContentSizeFitter. Earlier versions estimated lines × 16 which
        // consistently over-shot, producing visible empty gaps between
        // sections. preferredWidth is fixed (panel content area) so wrap
        // is deterministic.
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 20, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = true;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        lbl.TextMesh.fontStyle = FontStyles.Normal;
        var fitter = lbl.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
    }

    private static void AddAdminRefLine(GameObject parent, string command, string summary)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"AdminRef_{command}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 8, padding: new Vector4(2, 2, 0, 0));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);

        var cmd = UIFactory.CreateLabel(row, "Cmd", command,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(cmd.GameObject,
            minWidth: 200, preferredWidth: 220, flexibleWidth: 0,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        cmd.TextMesh.fontStyle = FontStyles.Bold;
        cmd.TextMesh.enableWordWrapping = false;
        cmd.TextMesh.overflowMode = TextOverflowModes.Overflow;

        var desc = UIFactory.CreateLabel(row, "Desc", summary,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(desc.GameObject,
            minWidth: 160, preferredWidth: 180, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        desc.TextMesh.enableWordWrapping = false;
        desc.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    // 0.10.7: tabular two-column row for the Vanilla Admin reference. Pre-0.10.7
    // each section packed all entries into a single multi-line label with
    // hand-tabbed alignment ("  • cmd — desc"); the tab spacing inside a
    // proportional font produced inconsistent column edges depending on the
    // longest command in each section. This helper renders bold command in
    // a fixed-width column + wrapped description in the flex column, so
    // every section ends up with the same crisp alignment regardless of
    // entry length.
    //
    // 0.10.8: the row now grows its height with the wrapped description.
    // Pre-0.10.8 the row had a fixed preferredHeight=22; the desc label
    // would wrap (correctly) but its rendered height stayed at the row's
    // fixed value, so the second/third wrapped line drew on top of the
    // next row's text. Fix: ContentSizeFitter on the row itself
    // (verticalFit=PreferredSize), and clear the row's preferredHeight so
    // the fitter uses the children's preferredHeight (HorizontalLayoutGroup
    // reports the max of its child heights, and desc has its own fitter).
    private static void AddCommandTableRow(GameObject parent, string command, string description)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"CmdRow_{command}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 10, padding: new Vector4(2, 2, 2, 2));
        // 0.10.8: minHeight floor only; flexibleHeight=0 + ContentSizeFitter
        // below grows the row when the description wraps. Setting
        // preferredHeight=-1 means "no opinion" — Unity uses the next
        // priority (child max from HorizontalLayoutGroup) which itself
        // comes from desc's ContentSizeFitter.
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 22, preferredHeight: -1, flexibleHeight: 0);
        var rowFitter = row.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        rowFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        rowFitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        var cmd = UIFactory.CreateLabel(row, "Cmd", command,
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(cmd.GameObject,
            minWidth: 220, preferredWidth: 220, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 22, flexibleHeight: 0);
        cmd.TextMesh.fontStyle = FontStyles.Bold;
        cmd.TextMesh.enableWordWrapping = false;
        cmd.TextMesh.overflowMode = TextOverflowModes.Overflow;

        var desc = UIFactory.CreateLabel(row, "Desc", description,
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        // 0.10.8: preferredHeight=-1 so the LayoutElement doesn't pin a
        // floor; the ContentSizeFitter pulls TMP's computed preferredHeight
        // (which is the actual wrapped height for this width).
        UIFactory.SetLayoutElement(desc.GameObject,
            minWidth: 160, preferredWidth: 200, flexibleWidth: 1,
            minHeight: 22, preferredHeight: -1, flexibleHeight: 0);
        desc.TextMesh.enableWordWrapping = true;
        desc.TextMesh.overflowMode = TextOverflowModes.Overflow;
        var fitter = desc.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
    }

    // 0.10.7: convenience — title heading + tabular row entries.
    private static void AddCommandTable(GameObject parent, string title, params (string command, string description)[] entries)
    {
        AddSectionHeading(parent, title);
        foreach (var (cmd, desc) in entries) AddCommandTableRow(parent, cmd, desc);
    }

    private void RenderExpertise(PlayerStateService.ExpertiseState s)
    {
        if (_wepTypeLabel == null) return;

        _wepTypeLabel.text = s.Type.ToString();
        _wepProgressLabel.text = s.Prestige > 0
            ? $"Level {s.Level}   ({s.Progress * 100f:0.#}%)   Prestige {s.Prestige}"
            : $"Level {s.Level}   ({s.Progress * 100f:0.#}%)";

        var stats = PlayerStateService.DecodeWeaponBonusStats(s.BonusStatsRaw);
        if (stats.Count == 0)
        {
            _wepBonusLabel.text = "Bonus Stats: (none yet — pick one via .wep cst)";
        }
        else
        {
            var named = new System.Collections.Generic.List<string>();
            foreach (var st in stats)
                if (st != PlayerStateService.WeaponStatType.None) named.Add(st.ToString());
            _wepBonusLabel.text = named.Count > 0
                ? $"Bonus Stats: {string.Join(", ", named)}"
                : "Bonus Stats: (none yet)";
        }

        UpdateMissingHint(_wepHintLabel, Services.ProgressionHints.ExpertiseHint());
    }

    private void RenderFamiliar(PlayerStateService.FamiliarState s)
    {
        if (_famNameLabel == null) return;
        // 0.10.8: HasActive is the authoritative "is there a familiar bound"
        // signal sourced from the raw Eclipse protocol name field. The Name
        // field is masked to "Familiar" for display when no familiar is
        // bound (preserving pre-0.10.8 visual placeholder), so we can't
        // rely on string.IsNullOrEmpty(Name) anymore.
        _famNameLabel.text = s.HasActive ? s.Name : "(no familiar bound)";

        bool active = s.HasActive;
        _famProgressLabel.text = active
            ? (s.Prestige > 0
                ? $"Level {s.Level}   ({s.Progress * 100f:0.#}%)   Prestige {s.Prestige}"
                : $"Level {s.Level}   ({s.Progress * 100f:0.#}%)")
            : "—";

        _famStatsLabel.text = active
            ? $"HP {s.MaxHealth}   PP {s.PhysicalPower}   SP {s.SpellPower}"
            : "HP —   PP —   SP —";
    }

    // -----------------------------------------------------------------------
    // Small UI helpers (label rows, command buttons, section headings)
    // -----------------------------------------------------------------------

    /// <summary>
    /// 0.9.1: bump the per-character outline so an accent-colored label stays
    /// legible against bright / red in-game backdrops bleeding through a low-
    /// opacity panel. UIFactory.CreateLabel already applies a 0.15-width
    /// black outline by default; this helper widens it to 0.25 for labels
    /// the user has explicitly recolored (Bloodcraft red, weekly quest
    /// magenta, daily cyan, etc.). Friend-testing surfaced this as "pink or
    /// red text on the red background of the UI". Cheap — just a property
    /// set, no extra GameObjects.
    /// </summary>
    private static void ApplyStrongAccentOutline(TextMeshProUGUI t)
    {
        if (t == null) return;
        try
        {
            t.outlineColor = Color.black;
            t.outlineWidth = 0.25f;
        }
        catch { /* TMP can throw during application teardown */ }
    }

    private static TextMeshProUGUI AddInfoLabel(GameObject parent, string name, string initialText, FontStyles style, int fontSize)
    {
        var lbl = UIFactory.CreateLabel(parent, name, initialText,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: fontSize);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        lbl.TextMesh.fontStyle = style;
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        return lbl.TextMesh;
    }

    // 0.13.0 visual refresh: section headings now get a thin 2-pixel gold
    // divider band above them PLUS a gold + bold + larger heading text.
    // Friend-test motivation: the help-group tabs (Quick Start, Mod Help,
    // Game Guide, Settings) carry long prose / detail blocks; with the
    // pre-0.13 plain-white italic heading it was hard to spot where one
    // article section ended and the next began while scrolling. The
    // colored band acts as a scannable section marker. Used for every
    // section across the panel (not just article tabs); data-display
    // tabs benefit from the clearer section break too.
    private static readonly UnityEngine.Color ARTICLE_HEADING_ACCENT =
        new UnityEngine.Color(0.90f, 0.72f, 0.36f, 1f); // warm gold

    // 0.28: amber caution tint for inline "this setting is being ignored / needs attention" warnings
    // (e.g. the launcher-hide safety gate in the Overlay Visibility section). Distinct from the gold
    // heading accent — more orange so it reads as a caution, not a section marker.
    private static readonly UnityEngine.Color WARNING_TINT =
        new UnityEngine.Color(0.95f, 0.58f, 0.20f, 1f);

    private static void AddSectionHeading(GameObject parent, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            // Empty title — historical callers (Vanilla Admin) use this to
            // emit a body paragraph without a heading. Skip the divider too
            // so we don't render a free-floating gold stripe.
            return;
        }

        // Gold divider band — full-width 2px Image. Visual marker for
        // "section starts here" that survives any panel-background color
        // pick (gold contrasts on every dark + bright preset).
        var divider = UIFactory.CreateUIObject($"SectionDivider_{text}", parent);
        divider.AddComponent<UnityEngine.UI.Image>().color = ARTICLE_HEADING_ACCENT;
        UIFactory.SetLayoutElement(divider,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 2, preferredHeight: 2, flexibleHeight: 0);

        // Heading text — gold, bold + italic, bumped to 16pt for readability
        // on the dense help-group tabs. The Theme.ScaledHeight helpers track
        // UI font scale so Small / Large / X-Large users still see a heading
        // proportional to their body text.
        var lbl = UIFactory.CreateLabel(parent, $"Section_{text}", text,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(16));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(30), preferredHeight: Theme.ScaledHeight(34), flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Bold | FontStyles.Italic;
        lbl.TextMesh.color = ARTICLE_HEADING_ACCENT;
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    private static void AddSpacer(GameObject parent, int height)
    {
        var spacer = UIFactory.CreateUIObject("Spacer", parent);
        UIFactory.SetLayoutElement(spacer, minHeight: height, preferredHeight: height, flexibleHeight: 0, flexibleWidth: 1);
    }

    // 0.10.9: VISUAL-POLISH HELPERS
    //
    // These power the cross-cutting design improvements from the v0.10.9
    // visual audit. Each is a small composable building block — pass an
    // arbitrary action that adds your section contents and the helper
    // wraps them in the polished shell. Used by Levels, Familiars,
    // V-Bloods, Prestige, Expertise, Legacy across the panel.

    /// <summary>0.10.9: wrap arbitrary content in an inset card with a
    /// subtle 13%-grey background. Replaces "naked text on the panel"
    /// pattern with discrete cards that read as grouped content. The
    /// optional <paramref name="tint"/> washes the card behind the
    /// content — pass one of <c>Theme.SystemTint*</c> for the XP /
    /// Legacy / Expertise / Familiar / Profession / Quest colors, or
    /// null for the neutral card background.
    ///
    /// 0.10.11: bumped default padding 6 → 10 so text labels inside the
    /// card never sit flush with the inset border. Friend-test 0.10.10
    /// surfaced this: "ensure that within containers ... there is at
    /// least a little left padding." Pre-0.10.11 a 6 px pad was tight
    /// at smaller font scales and several labels visually butted up
    /// against the card's left edge. Callers can still override.</summary>
    private static GameObject AddCard(GameObject parent, string name, Color? tint = null,
                                       int padding = 10, int innerSpacing = 4)
    {
        var card = UIFactory.CreateVerticalGroup(parent, name,
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: innerSpacing,
            padding: new Vector4(padding, padding, padding, padding),
            bgColor: Theme.CardBackground);
        UIFactory.SetLayoutElement(card,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        // Optional system tint overlay — draws ON TOP of the card
        // background image so the wash modulates it down into the
        // theme-tinted hue. Uses a child Image with stretched anchors
        // so it tracks any future resize.
        if (tint.HasValue && tint.Value.a > 0.0001f)
        {
            var washObj = UIFactory.CreateUIObject("Tint", card);
            var washRt  = washObj.GetComponent<UnityEngine.RectTransform>();
            washRt.anchorMin = Vector2.zero;
            washRt.anchorMax = Vector2.one;
            washRt.offsetMin = Vector2.zero;
            washRt.offsetMax = Vector2.zero;
            var img = washObj.AddComponent<UnityEngine.UI.Image>();
            img.color = tint.Value;
            img.raycastTarget = false;
            // Force the wash to draw BEHIND siblings (which are added
            // AFTER it because parent is its first child). Setting
            // sibling index 0 keeps it behind anything appended later.
            washObj.transform.SetSiblingIndex(0);
            // Layout-element opt-out so the wash doesn't consume layout
            // space — it's pure decoration.
            UIFactory.SetLayoutElement(washObj, ignoreLayout: true);
        }
        return card;
    }

    // Admin gating. Call at the top of an admin tab build as `page = BeginAdminGate(page);` — it appends
    // an always-interactive status banner + "Re-check admin" button to the real page, then returns a
    // CanvasGroup-wrapped content root that the rest of the tab builds into. When the local player is NOT
    // authed as a server admin, that content is grayed out + non-interactive (you can SEE the layout but
    // can't use the table/buttons). Re-evaluated whenever the tab is (re)built (i.e. switching to it) AND
    // on demand via the Re-check button — so authing via `adminauth` while already on the page is picked
    // up without leaving. The server enforces admin permission regardless; this is purely UI gating.
    private GameObject BeginAdminGate(GameObject page)
    {
        var header = UIFactory.CreateHorizontalGroup(page, "AdminGateHeader",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 8, padding: new Vector4(2, 2, 2, 4));
        UIFactory.SetLayoutElement(header, minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 30, flexibleHeight: 0);

        var statusLbl = AddInfoLabel(header, "AdminGateStatus", "", FontStyles.Bold, Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(statusLbl.gameObject, minWidth: 230, preferredWidth: 300, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        statusLbl.enableWordWrapping = true;

        var btn = UIFactory.CreateButton(header, "AdminGateRecheck", "Re-check admin");
        UIFactory.SetLayoutElement(btn.GameObject, minWidth: 112, preferredWidth: 124, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var bt = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (bt != null) bt.fontSize = Theme.ScaledUI(11);
        TooltipHover.Attach(btn.GameObject,
            "Re-check whether you're authed as a server admin. If you just ran adminauth in the F1 console, click this to enable the admin controls without leaving the page.");

        var content = UIFactory.CreateVerticalGroup(page, "AdminGateContent",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(content, minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 60, flexibleHeight: 0);
        var cg = content.AddComponent<UnityEngine.CanvasGroup>();

        void Apply()
        {
            bool admin = Services.MessageService.IsLocalAdmin();
            cg.interactable = admin;
            cg.blocksRaycasts = admin;
            cg.alpha = admin ? 1f : 0.45f;
            statusLbl.text = admin
                ? "<color=#90EE90><b>✓ Admin</b></color>  <color=#888888>— admin controls enabled.</color>"
                : "<color=#FFD75A><b>Not authed as admin.</b></color> <color=#888888>Run <b>adminauth</b> in the F1 console, then click Re-check →</color>";
        }
        btn.OnClick = Apply;
        Apply();
        return content;
    }

    /// <summary>0.10.9: a left-aligned label paired with a right-aligned
    /// value. Replaces the common pattern of stacking 4 separate
    /// AddInfoLabel calls with the data flowing left-to-right ("Type:
    /// Sword" / "Level: 42" / etc.) — instead, present as a compact
    /// table-style row inside a card. Value defaults to right-aligned so
    /// numeric values line up across stacked rows.</summary>
    private static (TextMeshProUGUI labelTmp, TextMeshProUGUI valueTmp) AddStatRow(
        GameObject parent, string label, string value, int fontSize = -1,
        FontStyles valueStyle = FontStyles.Bold)
    {
        int fs = fontSize > 0 ? fontSize : Theme.ScaledUI(12);
        var row = UIFactory.CreateHorizontalGroup(parent, $"StatRow_{label}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 8, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row,
            minWidth: 320, preferredWidth: 380, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);

        var lbl = UIFactory.CreateLabel(row, "Label",
            $"<color={Theme.MutedBodyHex}>{label}</color>",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: fs);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 140, preferredWidth: 170, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;

        var val = UIFactory.CreateLabel(row, "Value", value,
            TextAlignmentOptions.MidlineRight, color: null, fontSize: fs);
        UIFactory.SetLayoutElement(val.GameObject,
            minWidth: 120, preferredWidth: 200, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
        val.TextMesh.fontStyle = valueStyle;
        val.TextMesh.enableWordWrapping = false;
        val.TextMesh.overflowMode = TextOverflowModes.Overflow;

        return (lbl.TextMesh, val.TextMesh);
    }

    /// <summary>0.10.9: a 1-px hairline used to separate logical groups
    /// inside a card / between cards. The color comes from Theme.DividerLine
    /// so it obeys the global opacity curve.
    ///
    /// 0.10.11: rewritten to compose spacer + line + spacer at the
    /// PARENT level instead of using an HLG wrap. The 0.10.9 implementation
    /// had the Vector4 padding axes swapped (this codebase's Vector4 is
    /// (top, bottom, left, right) — easy to get wrong, see UIFactory.cs:254),
    /// which put 12px of top/bottom padding inside a 7px wrap. The line
    /// rendered at an unexpected vertical position and visually overlapped
    /// the body text that followed the divider. The new approach has no
    /// such trap and produces a cleaner result.</summary>
    private static void AddDivider(GameObject parent, int verticalGap = 6)
    {
        int halfGap = UnityEngine.Mathf.Max(2, verticalGap / 2);
        AddSpacer(parent, halfGap);
        var line = UIFactory.CreateUIObject("DividerLine", parent);
        UIFactory.SetLayoutElement(line,
            minWidth: 100, preferredWidth: 320, flexibleWidth: 1,
            minHeight: 1, preferredHeight: 1, flexibleHeight: 0);
        var img = line.AddComponent<UnityEngine.UI.Image>();
        img.color = Theme.DividerLine;
        img.raycastTarget = false;
        AddSpacer(parent, halfGap);
    }

    /// <summary>0.10.9: muted prose label for "what this section does"
    /// explanatory text. Reads as secondary content vs. the bright
    /// section headings + primary data labels.
    ///
    /// 0.10.13: dropped italic styling and bumped default font size
    /// 11 → 13. Friend-test 0.10.12: "italic at standard text size is
    /// difficult to read." The MutedBodyHex color already does the
    /// "secondary content" job — italic was redundant emphasis that
    /// hurt legibility without adding meaning. The slight size bump
    /// brings prose hints in line with the 12-13 pt body text used
    /// for primary data labels.</summary>
    private static TextMeshProUGUI AddBodyText(GameObject parent, string text, int fontSize = -1)
    {
        int fs = fontSize > 0 ? fontSize : Theme.ScaledUI(13);
        var lbl = UIFactory.CreateLabel(parent, "BodyText",
            $"<color={Theme.MutedBodyHex}>{text}</color>",
            TextAlignmentOptions.TopLeft, color: null, fontSize: fs);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 320, preferredWidth: 380, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Normal;
        lbl.TextMesh.enableWordWrapping = true;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        var fitter = lbl.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        return lbl.TextMesh;
    }

    /// <summary>0.10.9: format a `.command` literal for inline display
    /// inside a body label. Wraps in AccentMono color so it visually
    /// pops as "this is a literal chat command." Use within string
    /// composition: <c>$"Use {Mono(".wep cst")} to pick a stat."</c>
    /// </summary>
    private static string Mono(string s)
        => string.IsNullOrEmpty(s) ? s : $"<color={Theme.AccentMonoHex}><b>{s}</b></color>";

    private static void AddCommandButton(GameObject parent, string label, string command,
        string tooltip = null, Color? color = null, bool confirm = false)
    {
        var b = UIFactory.CreateButton(parent, $"Cmd_{label}", label, color);
        UIFactory.SetLayoutElement(b.GameObject,
            minWidth: 70, preferredWidth: 110, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        // Action rows use childForceExpandWidth so buttons share the row; tell
        // their inner TMP text not to wrap, so labels render on one line and
        // overflow visually (which is fine - shorter than wrap) instead of
        // stacking one character per line.
        var t = b.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null)
        {
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.Center;
            t.fontSize = Theme.ScaledUI(13);
        }

        if (confirm && t != null)
        {
            // Two-click confirm: first click arms the button for 3s and changes
            // its text to "Confirm?". A second click within the window sends.
            // Outside the window, it reverts to the original label silently.
            // This avoids a modal dialog system for the one or two destructive
            // commands we expose (e.g. .fam ub permanently destroys familiar).
            float armUntil = -1f;
            string originalLabel = label;
            b.OnClick = () =>
            {
                float now = Time.realtimeSinceStartup;
                if (armUntil > 0 && now <= armUntil)
                {
                    armUntil = -1f;
                    t.text = originalLabel;
                    EnqueueOrWarn(command);
                }
                else
                {
                    armUntil = now + 3f;
                    t.text = "Confirm?";
                }
            };
        }
        else
        {
            b.OnClick = () => EnqueueOrWarn(command);
        }

        // Hover tooltip: default to the literal command text so even un-described
        // buttons surface what they'll send; per-call site can pass a richer
        // explanation if it's worth the words.
        TooltipHover.Attach(b.GameObject, tooltip ?? $"Sends '{command}' to the server.");
    }

    private static void EnqueueOrWarn(string command)
    {
        if (!MessageService.IsInitialized)
        {
            LogUtils.LogWarning($"Cannot send '{command}' — MessageService not yet bound to character/user.");
            return;
        }
        MessageService.EnqueueMessage(command);
        LogUtils.LogInfo($"Enqueued outbound: {command}");
    }

    // -----------------------------------------------------------------------
    // Overlay-toggle footer
    // -----------------------------------------------------------------------

    private void BuildOverlayFooter(GameObject parent)
    {
        // 0.10.13: reformatted as a labeled card-style container so the
        // user reads it as "Overlay visibility" rather than a loose row
        // of toggles. Single-column layout: a label prefix on the left
        // of the toggle row visually frames the group. The auto-resize
        // toggle stays on its own line below — it's not an overlay
        // toggle and shouldn't read as one.
        var footerWrap = UIFactory.CreateVerticalGroup(parent, "OverlayFooterWrap",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(10, 10, 6, 6),
            bgColor: Theme.CardBackground);
        UIFactory.SetLayoutElement(footerWrap, minHeight: 62, flexibleHeight: 0, flexibleWidth: 1);

        // "Show overlays:" header on its OWN line — keeping it inline with the first toggle made the
        // leftmost (Combined) toggle overlap the label. (0.20.1)
        var visLabel = UIFactory.CreateLabel(footerWrap, "OverlayVisLabel",
            "Show overlays:",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(visLabel.GameObject,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0, flexibleWidth: 1);
        visLabel.TextMesh.fontStyle = FontStyles.Bold;
        visLabel.TextMesh.enableWordWrapping = false;
        visLabel.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // The overlay toggles flow through a WRAPPING grid (0.20.1). There are 10+ overlays now; a single
        // fixed HorizontalLayoutGroup squished them and overran the panel border when the window was narrow.
        // GridLayoutGroup.Constraint.Flexible fits as many columns as the current width allows (1 when very
        // narrow, more as you widen the panel) and wraps the rest onto new rows; the parent VLG
        // (childControlHeight) reads the grid's reported preferred height so the card grows with the rows.
        var toggleGrid = UIFactory.CreateUIObject("OverlayToggleGrid", footerWrap);
        var grid = toggleGrid.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        grid.cellSize = new Vector2(Theme.ScaledUI(170), Theme.ScaledUI(26));   // wide enough for "Familiar Browser"
        grid.spacing = new Vector2(6, 4);
        grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.Flexible;
        grid.childAlignment = TextAnchor.UpperLeft;

        // 0.14.0: Combined toggle first — when it's checked, the 4 info
        // overlay toggles below hide (mutual exclusion). Familiar Browser
        // and Shift spell stay visible either way.
        _combinedOverlayToggle = AddOverlayToggle(toggleGrid, "Combined",       PanelType.CombinedOverlay);
        // 0.14.0 friend-test v5: tooltip on the footer Combined toggle —
        // its label is less self-explanatory than the per-system labels.
        if (_overlayToggleGOs.TryGetValue(PanelType.CombinedOverlay, out var combinedToggleGO))
        {
            TooltipHover.Attach(combinedToggleGO,
                "Toggle the combined info overlay — one panel with XP / Familiar / Weapon / Blood / Professions / Quests sections in a single container. When on, the standalone XP / Familiar / Daily Quest / Professions overlays auto-hide; their per-section visibility is controlled in Settings → Display → Combined overlay.");
        }
        _xpOverlayToggle    = AddOverlayToggle(toggleGrid, "XP",                PanelType.ExperienceOverlay);
        _famOverlayToggle   = AddOverlayToggle(toggleGrid, "Familiar",          PanelType.FamiliarOverlay);
        _famBrowserToggle   = AddOverlayToggle(toggleGrid, "Familiar Browser",  PanelType.FamiliarBrowserOverlay);
        // 0.53: Familiar Quick Spawn overlay quick-toggle. Independent of Combined mode (like
        // Familiar Browser / Shift spell), so it always stays visible in the footer.
        _quickSpawnOverlayToggle = AddOverlayToggle(toggleGrid, "Quick Spawn",  PanelType.FamiliarQuickSpawnOverlay);
        if (_overlayToggleGOs.TryGetValue(PanelType.FamiliarQuickSpawnOverlay, out var quickSpawnToggleGO))
        {
            TooltipHover.Attach(quickSpawnToggleGO,
                "Toggle the Familiar Quick Spawn overlay — a small movable HUD with up to 5 one-click familiar summons (assigned from the All Familiars tab). Honors the overlay opacity, text-size, transparency and lock controls like every other overlay.");
        }
        _bossTrackerOverlayToggle = AddOverlayToggle(toggleGrid, "Boss Tracker", PanelType.FaustBossTrackerOverlay);
        if (_overlayToggleGOs.TryGetValue(PanelType.FaustBossTrackerOverlay, out var bossTrackerToggleGO))
        {
            TooltipHover.Attach(bossTrackerToggleGO,
                "Toggle the Faust Boss Tracker overlay — a small movable HUD tracking up to 3 V Blood statuses (live / defeated, with HP). Assign bosses and enable ~5s auto-refresh on the Faust → Boss Status tab. Needs a server running Faust 0.16+.");
        }
        _dqOverlayToggle    = AddOverlayToggle(toggleGrid, "Daily quest",       PanelType.DailyQuestOverlay);
        _profOverlayToggle  = AddOverlayToggle(toggleGrid, "Professions",       PanelType.ProfessionOverlay);
        _shiftOverlayToggle = AddOverlayToggle(toggleGrid, "Shift spell",       PanelType.ShiftSpellOverlay);
        _quickActionsOverlayToggle = AddOverlayToggle(toggleGrid, "Quick Actions",   PanelType.QuickActionsOverlay);
        // 0.18.3: Beelz action-bar overlay quick-toggle. Lets you hide the Beelz hotkey bar even on
        // servers WITHOUT Beelzebub (where the Beelzebub tab — the only other place to toggle it — is
        // greyed out). Always present; the overlay itself also auto-hides when Beelz isn't detected.
        _beelzOverlayToggle = AddOverlayToggle(toggleGrid, "Beelz hotkeys",     PanelType.BeelzActionBarOverlay);
        // 0.19: Beelz summons overlay quick-toggle (one-click stash/restore). Always present; the overlay
        // itself auto-hides when Beelzebub isn't detected, same as the Beelz hotkeys bar.
        _beelzSummonsOverlayToggle = AddOverlayToggle(toggleGrid, "Beelz summons",    PanelType.BeelzSummonsOverlay);
        // 0.20: Beelz transforms overlay quick-toggle (double-click to transform + phase/revert).
        _beelzTransformOverlayToggle = AddOverlayToggle(toggleGrid, "Beelz transforms",  PanelType.BeelzTransformOverlay);

        // Initial visibility — reflects whichever mode was active at last
        // logout (Combined sticks across sessions via Settings).
        ApplyCombinedFooterVisibility();

        // Row 2: panel behavior — visually separated by the spacing in
        // the parent VLG, so it doesn't get confused with the visibility
        // toggles above.
        var row2 = UIFactory.CreateHorizontalGroup(footerWrap, "OverlayFooterRow2",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: false, childControlHeight: false,
            spacing: 12, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row2, minHeight: 26, flexibleHeight: 0, flexibleWidth: 1);

        AddAutoResizeToggle(row2);
        AddLockOverlaysToggle(row2);
        AddHideChatWithOverlaysToggle(row2);
    }

    /// <summary>0.18.3: "Hide chat with OV" toggle beside Lock overlays. When on, the upper-right
    /// "hide all overlays" button (OV) also hides the chat window; default off (chat stays visible).</summary>
    private void AddHideChatWithOverlaysToggle(GameObject parent)
    {
        var t = UIFactory.CreateToggle(parent, "HideChatWithOverlaysToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 180, preferredWidth: 200, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        // 0.18.3 follow-up: clearer label (tester found "Hide chat too" ambiguous). "OV" = the
        // upper-right overlay master toggle this rides along with.
        t.Text.text = "Hide chat with OV";
        t.Text.fontSize = Theme.ScaledUI(13);
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 150, preferredWidth: 170, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);

        t.Toggle.isOn = Config.Settings.HideChatWithOverlaysToggle;
        TooltipHover.Attach(t.GameObject,
            "When ON, the upper-right 'hide all overlays' button (and the overlay hotkey) also hides the chat window. Default OFF — chat stays visible while the other overlays hide. If overlays are currently hidden, re-apply by toggling them off and on.");
        t.OnValueChanged += value => Config.Settings.SetHideChatWithOverlaysToggle(value);
    }

    /// <summary>0.10.14: "Lock overlays" toggle beside Auto-resize.
    /// When on, every overlay's IsPinned flag is set true, which makes
    /// PanelDragger ignore mouse interactions (no drag, no resize).
    /// Programmatic resize when settings or content change is unaffected
    /// — IsPinned only blocks the dragger, not direct Rect.sizeDelta
    /// mutations. Friend-test: "lock overlays so I don't accidentally
    /// drag them around while playing."</summary>
    private void AddLockOverlaysToggle(GameObject parent)
    {
        var t = UIFactory.CreateToggle(parent, "LockOverlaysToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 160, preferredWidth: 180, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        t.Text.text = "Lock overlays";
        t.Text.fontSize = Theme.ScaledUI(13);
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 130, preferredWidth: 150, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);

        t.Toggle.isOn = Config.Settings.LockOverlays;
        TooltipHover.Attach(t.GameObject,
            "Lock the position and size of every overlay so they can't be dragged or resized by accident during play. Settings-driven resize (e.g. enabling progress bars on the XP overlay, or a V-Blood scan growing the list) still works.");
        t.OnValueChanged += value =>
        {
            Config.Settings.SetLockOverlays(value);
            // Apply to every live overlay immediately. Overlays not yet
            // constructed will read the setting in
            // ResizeablePanelBase.LateConstructUI when they're built.
            Plugin.UIManager?.ApplyOverlayLockState();
        };
    }

    private void AddAutoResizeToggle(GameObject parent)
    {
        var t = UIFactory.CreateToggle(parent, "AutoResizeToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 160, preferredWidth: 180, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        t.Text.text = "Auto-resize panel";
        t.Text.fontSize = Theme.ScaledUI(13);
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 130, preferredWidth: 150, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);

        t.Toggle.isOn = Settings.IsPanelAutoResizeEnabled;
        TooltipHover.Attach(t.GameObject,
            "When on, the main panel grows to fit the active tab's content (capped at 90% of screen height).");
        t.OnValueChanged += value =>
        {
            Plugin.Instance.Config.Bind(Settings.UI_SETTINGS_GROUP, nameof(Settings.IsPanelAutoResizeEnabled), true, "").Value = value;
            AutoResizeIfEnabled();
        };
    }

    private void AutoResizeIfEnabled()
    {
        if (!Settings.IsPanelAutoResizeEnabled) return;
        // 0.11.2: bail in fullscreen mode. With stretched anchors (set by
        // SetFullscreen), assigning sizeDelta.y makes the panel that-many
        // pixels TALLER than the canvas — which is exactly the friend-
        // test bug ("UI scaled up larger than the screen"). Auto-resize
        // doesn't make sense when the panel is already canvas-sized;
        // skip it cleanly.
        if (_isFullscreen) return;
        if (!_tabContent.TryGetValue(ActiveTab, out var pageGo) || pageGo == null) return;

        // The visible tab GameObject is the ScrollView wrapper, but the actual
        // children live in the inner content. Walk the inner so AutoResize sees
        // the true height; if missing (legacy path), fall back to the wrapper.
        _tabInnerContent.TryGetValue(ActiveTab, out var innerGo);
        var measureGo = innerGo != null ? innerGo : pageGo;

        try
        {
            var pageRt = pageGo.GetComponent<RectTransform>();
            if (pageRt == null) return;

            // Force the layout to recalculate so preferredHeight is up to date.
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(pageRt);

            // Take the taller of the active page and the left tab strip - the
            // tab strip can be taller than the active page when many tabs are
            // expanded (BLOODCRAFT alone has 8 sub-tabs), and we don't want
            // sub-tabs hidden below the panel border.
            float pageHeight  = ComputeChildrenSumHeight(measureGo);
            float stripHeight = _tabStripGo != null ? ComputeChildrenSumHeight(_tabStripGo) : 0f;
            float contentHeight = Math.Max(pageHeight, stripHeight);
            // Chrome budget: panel title bar (~24) + OverlayFooter (32) + TooltipFooter (22) +
            // spacing/margins/safety buffer (~32) ≈ 110px. The earlier 76 was missing the title-
            // bar height, which manifested as the panel coming up short by a row or two when
            // switching to a long box-content view.
            float chrome = 110f;
            float desired = contentHeight + chrome;
            float screenCap = UnityEngine.Screen.height * 0.9f;
            float clamped = Math.Min(Math.Max(desired, MinHeight), screenCap);

            var size = Rect.sizeDelta;
            if (Math.Abs(size.y - clamped) > 1f)
            {
                Rect.sizeDelta = new Vector2(size.x, clamped);
                EnsureValidPosition();
                // 0.10.14: refresh the dragger's cached resize hit-area
                // after a programmatic resize. PanelDragger caches the
                // 10-px border mask at construction and ONLY refreshes it
                // when OnEndResize fires (manual drag-resize). Pre-0.10.14
                // an auto-resize would leave the cache pointing at the
                // OLD panel size — the user couldn't hit the new bottom
                // border because the mask still expected the panel's
                // initial bottom. Friend-test 0.10.13: "can't click and
                // expand the UI." Root cause was this cache staleness;
                // 0.10.13's layout changes made the auto-resize delta
                // bigger, which made the stale-cache offset large enough
                // for the user to notice.
                Dragger?.OnEndResize();
            }
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"AutoResizeIfEnabled failed: {ex}");
        }
    }

    /// <summary>
    /// Sum the visible direct children's preferred heights + spacing + padding.
    /// Honors VerticalLayoutGroup's spacing/padding when present. Uses
    /// LayoutUtility.GetPreferredHeight per child so children with their own
    /// VerticalLayoutGroup (e.g. CollapsibleSections) report the right value
    /// when expanded vs collapsed.
    /// </summary>
    private static float ComputeChildrenSumHeight(GameObject parent)
    {
        var rt = parent.GetComponent<RectTransform>();
        if (rt == null) return 0f;

        var vlg = parent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        float spacing = vlg != null ? vlg.spacing : 0f;
        float padTop = vlg != null ? vlg.padding.top : 0f;
        float padBot = vlg != null ? vlg.padding.bottom : 0f;

        float total = padTop + padBot;
        int visible = 0;
        for (int i = 0; i < rt.childCount; i++)
        {
            var child = rt.GetChild(i);
            if (!child.gameObject.activeInHierarchy) continue;
            visible++;

            var crt = child.GetComponent<RectTransform>();
            if (crt == null) continue;

            float ph = UnityEngine.UI.LayoutUtility.GetPreferredHeight(crt);
            if (ph < 0) ph = crt.rect.height;
            total += ph;
        }
        if (visible > 1) total += spacing * (visible - 1);
        return total;
    }

    private Toggle AddOverlayToggle(GameObject parent, string label, PanelType overlay)
    {
        var t = UIFactory.CreateToggle(parent, $"OverlayToggle_{overlay}");
        // Compact widths so 4 toggles fit in row 1 of the footer at the panel's
        // MinWidth=600 without spilling off the right edge.
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 130, preferredWidth: 150, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);

        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(13);
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 100, preferredWidth: 120, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);

        t.Toggle.isOn = Plugin.UIManager.IsOverlayOpen(overlay);
        t.OnValueChanged += _ =>
        {
            Plugin.UIManager.ToggleOverlay(overlay);
            // 0.14.0 friend-test v2: any footer toggle change cascades into
            // a full sync — fixes the desync where toggling combined-mode
            // left the footer XP/Familiar/etc. toggles showing stale state
            // because their isOn was last set at construct time.
            if (overlay == PanelType.CombinedOverlay)
            {
                ApplyCombinedFooterVisibility();
                Plugin.UIManager?.RefreshCombinedOverlaySections();
            }
            RefreshAllOverlayToggleStates();
        };
        // 0.14.0: remember the toggle's GameObject so the footer can hide/show
        // it in response to Combined-mode flips without rebuilding the row.
        _overlayToggleGOs[overlay] = t.GameObject;
        return t.Toggle;
    }

    /// <summary>0.14.0: drive the footer toggle visibility for the
    /// Combined-vs-individuals mutual exclusion. The Combined toggle stays
    /// visible in both modes; the 4 info toggles it replaces (XP / Familiar /
    /// Daily quest / Professions) hide when Combined is on. Familiar Browser
    /// and Shift spell are independent overlays so they always stay visible.</summary>
    public void ApplyCombinedFooterVisibility()
    {
        bool combined = Config.Settings.ShowCombinedOverlay;
        SetToggleVisible(PanelType.ExperienceOverlay,  !combined);
        SetToggleVisible(PanelType.FamiliarOverlay,    !combined);
        SetToggleVisible(PanelType.DailyQuestOverlay,  !combined);
        SetToggleVisible(PanelType.ProfessionOverlay,  !combined);
    }

    private void SetToggleVisible(PanelType overlay, bool visible)
    {
        if (_overlayToggleGOs.TryGetValue(overlay, out var go) && go != null
            && go.activeSelf != visible)
        {
            go.SetActive(visible);
        }
    }

    // -----------------------------------------------------------------------
    // Tab switching
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // Lazy tab pages (0.67.1 PERF)
    //
    // Pre-0.67.1 the constructor built all ~60 pages in one frame (a 2-10 s freeze on first open, repeated on
    // every whole-panel rebuild). Now:
    //   • the shell (rail + footers) builds on open, with a "Loading…" placeholder in the content area;
    //   • a clicked / initial tab goes through a single PENDING slot and builds next frame (placeholder shows
    //     first); programmatic ShowTab builds synchronously because external callers may touch the page;
    //   • TickLazyPages prewarms the rest ONE page per frame while the panel is open (never while closed);
    //   • a mod's detection change marks only that mod's pages stale (InvalidateModPages) — no whole rebuild.
    // Page lifecycle: EnsureTabBuilt retires the old page FIRST (its per-page teardown runs, then it is hidden
    // and destroyed) and builds the replacement in the same synchronous frame. Builders assign shared panel
    // fields, so "keep the old page if the rebuild fails" isn't safe — a failed build shows an error page.
    // -----------------------------------------------------------------------

    public enum ModGroup { Bloodcraft, Beelzebub, Uriel, Faust, Nyar }

    private static string ModGroupTitle(ModGroup g) => g switch
    {
        ModGroup.Bloodcraft => "Bloodcraft",
        ModGroup.Beelzebub  => "Beelzebub",
        ModGroup.Uriel      => "Uriel",
        ModGroup.Faust      => "Faust",
        ModGroup.Nyar       => "Nyarlathotep",
        _ => null,
    };

    private GameObject _tabContentRoot;
    private GameObject _placeholderPage;
    private TextMeshProUGUI _placeholderLabel;
    private GameObject _buildingWrapper;
    private readonly Dictionary<PanelType, GameObject> _errorPages = new();
    private readonly HashSet<PanelType> _staleTabs = new();
    private readonly HashSet<PanelType> _failedTabs = new();
    private readonly Dictionary<PanelType, List<System.Action>> _pageTeardown = new();
    private List<System.Action> _buildingTeardown;
    private bool _isBuildingPage;
    private PanelType? _pendingTab;
    private int _pendingArmedFrame;
    private int _navGen;
    private bool _disposed;
    private System.Action _prewarmTicker;
    private int _prewarmPauseUntilFrame;
    private int _prewarmBuilt;
    private double _prewarmTotalMs;
    private double _prewarmSlowestMs;
    private PanelType _prewarmSlowestTab;
    private bool _prewarmSummaryLogged;
    private bool _mapImageSubscribed;
    private const double PREWARM_SLOW_PAGE_MS = 16.0;
    private const int PREWARM_SLOW_PAUSE_FRAMES = 10;

    private bool IsTabBuilt(PanelType tab)
        => _tabContent.TryGetValue(tab, out var go) && go != null && !_staleTabs.Contains(tab);

    private void BuildPlaceholderPage(GameObject parent)
    {
        _placeholderPage = UIFactory.CreateVerticalGroup(parent, "TabLoadingPlaceholder",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(14, 14, 14, 14), childAlignment: TextAnchor.UpperLeft);
        UIFactory.SetLayoutElement(_placeholderPage,
            minWidth: 380, preferredWidth: 420, flexibleWidth: 1,
            minHeight: 280, flexibleHeight: 1);
        var lbl = UIFactory.CreateLabel(_placeholderPage, "TabLoadingText", "Loading…",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(14));
        UIFactory.SetLayoutElement(lbl.GameObject, minWidth: 300, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        lbl.TextMesh.color = Theme.MutedBody;
        _placeholderLabel = lbl.TextMesh;
        _placeholderPage.SetActive(false);
    }

    // Show exactly one content-area child: a built page, an error page, or (null) the placeholder.
    private void ShowOnlyPage(GameObject visible)
    {
        foreach (var kv in _tabContent)
            if (kv.Value != null && kv.Value.activeSelf != (kv.Value == visible)) kv.Value.SetActive(kv.Value == visible);
        foreach (var kv in _errorPages)
            if (kv.Value != null && kv.Value.activeSelf != (kv.Value == visible)) kv.Value.SetActive(kv.Value == visible);
        if (_placeholderPage != null)
        {
            bool showPlaceholder = visible == null;
            if (_placeholderPage.activeSelf != showPlaceholder) _placeholderPage.SetActive(showPlaceholder);
        }
    }

    // Put `tab` in the pending slot (latest request wins) and show the placeholder until it's built.
    private void ArmPendingTab(PanelType tab)
    {
        ActiveTab = tab;
        _pendingTab = tab;
        _pendingArmedFrame = UnityEngine.Time.frameCount;
        _navGen++;
        if (_placeholderLabel != null)
            _placeholderLabel.text = $"Loading {LookupTabLabel(tab) ?? tab.ToString()}…";
        ShowOnlyPage(null);
    }

    /// <summary>0.67.1: tab-button / initial-open navigation. Built pages switch instantly; an unbuilt or stale
    /// page shows "Loading…" this frame and is built next frame (TickLazyPages), so a click never freezes.</summary>
    private void ShowTabDeferred(PanelType tab)
    {
        if (_disposed || LookupTabLabel(tab) == null) return;
        if (IsTabBuilt(tab))
        {
            ShowTab(tab);
            return;
        }
        _failedTabs.Remove(tab);   // an explicit click retries a failed page
        ArmPendingTab(tab);
    }

    // Retire a page: run its teardown (static-event unhooks), hide it immediately (Destroy is end-of-frame), destroy.
    private void RetirePage(PanelType tab)
    {
        if (_pageTeardown.TryGetValue(tab, out var td))
        {
            _pageTeardown.Remove(tab);
            PageBuildContext.Drain(td);
        }
        if (_tabContent.TryGetValue(tab, out var old))
        {
            _tabContent.Remove(tab);
            _tabInnerContent.Remove(tab);
            if (old != null)
            {
                old.SetActive(false);
                UnityEngine.Object.Destroy(old);
            }
        }
        RetireErrorPage(tab);
    }

    private void RetireErrorPage(PanelType tab)
    {
        if (!_errorPages.TryGetValue(tab, out var err)) return;
        _errorPages.Remove(tab);
        if (err != null)
        {
            err.SetActive(false);
            UnityEngine.Object.Destroy(err);
        }
    }

    private void CreateErrorPage(PanelType tab, string label)
    {
        if (_tabContentRoot == null) return;
        var err = UIFactory.CreateVerticalGroup(_tabContentRoot, $"TabError_{tab}",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(14, 14, 14, 14), childAlignment: TextAnchor.UpperLeft);
        UIFactory.SetLayoutElement(err, minWidth: 380, preferredWidth: 420, flexibleWidth: 1, minHeight: 280, flexibleHeight: 1);
        var lbl = UIFactory.CreateLabel(err, "TabErrorText",
            $"<color=#FF8080>Couldn't build the {label} tab.</color>\nThe error is in the BepInEx log. Click the tab again to retry.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(lbl.GameObject, minWidth: 300, flexibleWidth: 1, minHeight: 40, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = true;
        err.SetActive(false);
        _errorPages[tab] = err;
    }

    /// <summary>0.67.1: build `tab`'s page if it's missing or stale. Returns true when a usable page exists.
    /// `explicitRetry` (clicks / programmatic ShowTab) clears a previous failure; prewarm skips failed tabs.</summary>
    private bool EnsureTabBuilt(PanelType tab, bool explicitRetry)
    {
        if (_disposed || _tabContentRoot == null) return false;
        if (IsTabBuilt(tab)) return true;
        var label = LookupTabLabel(tab);
        if (label == null) return false;   // not in any group (same as the old `_tabContent.ContainsKey` guard)
        if (_failedTabs.Contains(tab))
        {
            if (!explicitRetry) return false;
            _failedTabs.Remove(tab);
        }
        if (_isBuildingPage)
        {
            // A builder navigated mid-build — never observed; park it in the pending slot for the caller to drain.
            // Only ShowTab reaches here reentrantly, so this is a navigation: it becomes the target (latest wins),
            // and bumping _navGen stops a deferred commit of the outer tab.
            LogUtils.LogWarning($"MainPanel: nested page build requested for {tab} while another page was building; deferred.");
            _pendingTab = tab;
            _pendingArmedFrame = UnityEngine.Time.frameCount - 1;
            ActiveTab = tab;
            _navGen++;
            return false;
        }

        RetirePage(tab);

        var teardown = new List<System.Action>();
        var prevScope = PageBuildContext.Begin(teardown);
        _isBuildingPage = true;
        _buildingTeardown = teardown;
        _buildingWrapper = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var wrapper = BuildTabPage(tab, label);
            // PanelBase paints the user's panel colours once, after construction (PanelBase.cs ConstructUI);
            // a page built later missed that pass, so paint just this page's subtree the same way.
            if (UsesCustomBackgroundColor)
                UIFactory.ApplyBackgroundColorRgbToPanel(wrapper, Settings.PanelBackgroundColor);
            if (UsesCustomInnerBackgroundColor)
                UIFactory.ApplyInnerBackgroundColorToPanel(wrapper, Settings.InnerPanelBackgroundColor);
            _pageTeardown[tab] = teardown;
            _staleTabs.Remove(tab);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            _prewarmBuilt++;
            _prewarmTotalMs += ms;
            if (ms > _prewarmSlowestMs) { _prewarmSlowestMs = ms; _prewarmSlowestTab = tab; }
            LogUtils.LogDiagnostic($"MainPanel: built tab {tab} in {ms:0.0} ms");
            return true;
        }
        catch (System.Exception ex)
        {
            LogUtils.LogError($"MainPanel: building tab {tab} failed: {ex}");
            PageBuildContext.Drain(teardown);
            _tabContent.Remove(tab);
            _tabInnerContent.Remove(tab);
            if (_buildingWrapper != null)
            {
                _buildingWrapper.SetActive(false);
                UnityEngine.Object.Destroy(_buildingWrapper);
            }
            _staleTabs.Remove(tab);
            _failedTabs.Add(tab);
            CreateErrorPage(tab, label);
            return false;
        }
        finally
        {
            PageBuildContext.End(prevScope);
            _isBuildingPage = false;
            _buildingTeardown = null;
            _buildingWrapper = null;
        }
    }

    // Per-frame: (1) build a pending navigation target once its placeholder has rendered, then (2) prewarm one
    // more page. Only while the panel is open; nothing is built while it's closed.
    private void TickLazyPages()
    {
        if (_disposed || !Enabled || _tabContentRoot == null) return;
        int frame = UnityEngine.Time.frameCount;

        if (_pendingTab is PanelType pending)
        {
            if (frame <= _pendingArmedFrame) return;   // let "Loading…" render for at least one frame
            int gen = _navGen;
            _pendingTab = null;
            EnsureTabBuilt(pending, explicitRetry: true);
            // Commit only if nothing re-targeted navigation while we built (latest click wins).
            if (gen == _navGen && ActiveTab == pending && !_pendingTab.HasValue)
                ShowTabTail(pending);
            return;   // one build per frame
        }

        if (frame < _prewarmPauseUntilFrame) return;
        var next = NextTabToPrewarm();
        if (next is not PanelType t)
        {
            if (!_prewarmSummaryLogged && _prewarmBuilt > 0)
            {
                _prewarmSummaryLogged = true;
                int teardowns = 0;
                foreach (var kv in _pageTeardown) teardowns += kv.Value.Count;
                LogUtils.LogInfo($"MainPanel: prewarm done — {_prewarmBuilt} page build(s), {_prewarmTotalMs:0} ms total, " +
                    $"slowest {_prewarmSlowestTab} {_prewarmSlowestMs:0} ms; page teardowns={teardowns}, " +
                    $"box-name fields live={Raphael.UI.Forms.BoxNameDropdownField.LiveCount}.");
            }
            return;
        }
        _prewarmSummaryLogged = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        EnsureTabBuilt(t, explicitRetry: false);
        if (sw.Elapsed.TotalMilliseconds > PREWARM_SLOW_PAGE_MS)
            _prewarmPauseUntilFrame = frame + PREWARM_SLOW_PAUSE_FRAMES;
    }

    // Recomputed every tick from current state (no persistent queue, so nothing carries across a server switch):
    // exact ActiveTab → rest of its group → other available groups (rail order; Game UI + Settings and Help last
    // by position). Unavailable mod groups are skipped — built on click only — and failed tabs are skipped.
    private PanelType? NextTabToPrewarm()
    {
        bool Wants(PanelType t) => !IsTabBuilt(t) && !_failedTabs.Contains(t);
        if (Wants(ActiveTab) && LookupTabLabel(ActiveTab) != null) return ActiveTab;
        TabGroupDef activeGroup = null;
        foreach (var g in TabGroups)
            foreach (var (t, _) in g.Tabs)
                if (t == ActiveTab) activeGroup = g;
        if (activeGroup != null && IsTabGroupAvailable(activeGroup.Title))
            foreach (var (t, _) in activeGroup.Tabs)
                if (Wants(t)) return t;
        foreach (var g in TabGroups)
        {
            if (g == activeGroup || !IsTabGroupAvailable(g.Title)) continue;
            foreach (var (t, _) in g.Tabs)
                if (Wants(t)) return t;
        }
        return null;
    }

    /// <summary>0.67.1: a mod's detection state changed (present ↔ absent, or gave up). Mark only that mod's
    /// built pages stale — they're rebuilt lazily (active one next frame, rest by prewarm / on click) instead
    /// of destroying and rebuilding the whole panel. Safe while closed.</summary>
    public void InvalidateModPages(ModGroup group)
    {
        if (_disposed) return;
        var title = ModGroupTitle(group);
        foreach (var g in TabGroups)
        {
            if (g.Title != title) continue;
            foreach (var (t, _) in g.Tabs)
            {
                _failedTabs.Remove(t);
                if (_tabContent.ContainsKey(t) || _errorPages.ContainsKey(t)) _staleTabs.Add(t);
            }
            // The active page is showing now: swap to "Loading…" so the stale content isn't interactable.
            foreach (var (t, _) in g.Tabs)
                if (t == ActiveTab && _staleTabs.Contains(t)) ArmPendingTab(t);
        }
        _prewarmSummaryLogged = false;
        LogUtils.LogDiagnostic($"MainPanel: {title} detection changed — its tab pages will rebuild on demand.");
    }

    /// <summary>0.67.2: a setting shown on <paramref name="tab"/> changed elsewhere — rebuild that page when next
    /// needed (prewarm / on click) so it doesn't show a stale value. No-op if it was never built.</summary>
    private void MarkTabStale(PanelType tab)
    {
        if (_disposed || tab == ActiveTab) return;
        if (_tabContent.ContainsKey(tab) || _errorPages.ContainsKey(tab)) { _staleTabs.Add(tab); _prewarmSummaryLogged = false; }
    }

    /// <summary>0.67.1: relog / server switch — every mod group's pages may describe the previous server.</summary>
    public void InvalidateAllModPages()
    {
        foreach (ModGroup g in System.Enum.GetValues(typeof(ModGroup))) InvalidateModPages(g);
    }

    private void OnFaustMapImageLoaded()
    {
        if (_disposed) return;
        try
        {
            if (_faustWsMapImgStatus != null) _faustWsMapImgStatus.text = FaustMapBackdrop.StatusLine();
            RefreshFaustMapUnderlays();
        }
        catch (System.Exception ex) { LogUtils.LogDebug($"[Faust] map refresh after image load failed: {ex.Message}"); }
    }

    public void ShowTab(PanelType tab)
    {
        if (_disposed) return;
        if (_isBuildingPage)
        {
            // A page builder navigated (never observed). Park it as the newest target — even if that tab is
            // already built — and let the outermost ShowTab / the tick settle it after the current build.
            LogUtils.LogWarning($"MainPanel: ShowTab({tab}) called while a page was building; deferred.");
            _pendingTab = tab;
            _pendingArmedFrame = UnityEngine.Time.frameCount - 1;
            ActiveTab = tab;
            _navGen++;
            return;
        }
        // 0.67.1: programmatic navigation is synchronous (callers may touch the page right after), and it
        // supersedes any pending deferred navigation so that can't take over next frame.
        _pendingTab = null;
        _navGen++;
        bool built = EnsureTabBuilt(tab, explicitRetry: true);
        // A builder that navigated mid-build parked its target in the pending slot — that newer target wins. Build
        // it now (bounded, in case of a cycle) so nothing is left half-requested when this synchronous call returns.
        var target = tab;
        for (int i = 0; i < 3 && _pendingTab is PanelType nested; i++)
        {
            _pendingTab = null;
            target = nested;
            built = EnsureTabBuilt(nested, explicitRetry: true);
        }
        if (_pendingTab is PanelType stuck)
        {
            LogUtils.LogError($"MainPanel: nested tab navigation did not settle (still pending {stuck}); deferring it.");
            ArmPendingTab(stuck);   // placeholder + next-frame build
            return;
        }
        if (!built)
        {
            if (_errorPages.TryGetValue(target, out var errPage))
            {
                ActiveTab = target;
                ShowOnlyPage(errPage);
            }
            return;
        }
        ShowTabTail(target);
    }

    // Everything ShowTab did once the page existed (unchanged pre-0.67.1 behaviour).
    private void ShowTabTail(PanelType tab)
    {
        if (!_tabContent.TryGetValue(tab, out var pageGo) || pageGo == null)
        {
            if (_errorPages.TryGetValue(tab, out var errPage)) { ActiveTab = tab; ShowOnlyPage(errPage); }
            return;
        }
        ShowOnlyPage(pageGo);
        ActiveTab = tab;

        // PERF: Beelz tabs ignore their state-change events while hidden; resync the
        // one being shown so it reflects current BeelzState (no-op for non-Beelz tabs).
        RefreshBeelzTabOnShow(tab);
        // 0.67.2: the same settings are also editable on other tabs — re-read them when shown.
        if (tab == PanelType.ChatDiagTab) RefreshChatDiagRows();
        if (tab == PanelType.GameCatalogTab) OnGameCatalogTabShown();

        // First-open auto-pull for tabs whose body depends on a server reply.
        // Boxes tab: send .fam boxes if we don't have any yet, so the user
        // doesn't have to click Refresh on every cold open. Cheap and idempotent
        // (the existing intercept-flag logic handles re-arming if pressed twice).
        if (tab == PanelType.BoxesTab
            && PlayerStateService.BoxList != null
            && PlayerStateService.BoxList.Count == 0
            && MessageService.IsInitialized)
        {
            if (_boxesStatusLabel != null) _boxesStatusLabel.text = "Loading boxes from the server…";
            EnqueueOrWarn(MessageService.BCCOM_FAM_BOXES);
        }

        // 0.9.6: auto-fire the structured-info fetch on Wep/Blood tab open
        // so the new stat-values header populates immediately. Subsequent
        // refreshes are driven by TickTabAutoRefresh (per-frame ticker).
        if (MessageService.IsInitialized)
        {
            if (tab == PanelType.ExpertiseTab)
            {
                FireWepInfoFetch();
            }
            else if (tab == PanelType.BloodLegacyTab)
            {
                FireBlInfoFetch();
            }
        }

        AutoResizeIfEnabled();
    }

    // 0.9.6: per-tab auto-refresh of the live "current X" info displays so
    // the stat-values stay current without the user needing to click Refresh.
    // Wired once in ConstructPanelContent as a CoreUpdateBehavior tick;
    // self-gates on ActiveTab + interval so it's a no-op for any other tab.
    private double _lastWepAutoFetchAt;
    private double _lastBlAutoFetchAt;
    // 0.10.2: type-change tracking. When the user equips a new weapon or
    // switches blood type, the bonus stats need to refresh ASAP rather than
    // waiting for the next 10s tick. Subscribe to ExpertiseChanged /
    // LegacyChanged in BuildExpertiseTab / BuildBloodLegacyTab; when a Type
    // delta is detected, reset the per-tab fetch timer so the ticker fires
    // next frame.
    private PlayerStateService.WeaponType _wepTabLastType;
    private PlayerStateService.BloodType  _blTabLastType;
    private bool _wepTabTypeBaseline;
    private bool _blTabTypeBaseline;
    private System.Action _tabAutoRefreshTicker;
    private const double TAB_AUTO_REFRESH_SECONDS = 10.0;

    private void TickTabAutoRefresh()
    {
        // 0.65.6: put the footer Refresh label back after its brief "Done" confirmation.
        if (_footerRefreshRevertAt > 0f && UnityEngine.Time.unscaledTime >= _footerRefreshRevertAt)
        {
            _footerRefreshRevertAt = -1f;
            try { if (_footerRefreshBtn?.ButtonText != null) _footerRefreshBtn.ButtonText.text = FooterRefreshLabel; } catch { }
        }
        if (!MessageService.IsInitialized) return;
        // Only the panel-is-open path matters; tab tickers shouldn't run
        // while the main panel is hidden.
        if (!Enabled) return;
        // 0.17.3: under Eclipse stand-down the weapon/blood data is Eclipse's domain —
        // skip the passive `.wep get` / `.bl get` auto-refresh (no stream → stale blood
        // type → misfiring queries whose replies spam chat). The Refresh buttons on
        // those tabs still work for an explicit one-off pull.
        if (Services.EclipseProtocolService.StandDownForEclipse()) return;
        var now = UnityEngine.Time.realtimeSinceStartupAsDouble;
        if (ActiveTab == PanelType.ExpertiseTab
            && now - _lastWepAutoFetchAt >= TAB_AUTO_REFRESH_SECONDS)
        {
            FireWepInfoFetch();
        }
        else if (ActiveTab == PanelType.BloodLegacyTab
              && now - _lastBlAutoFetchAt >= TAB_AUTO_REFRESH_SECONDS)
        {
            FireBlInfoFetch();
        }
    }

    private void FireWepInfoFetch()
    {
        _lastWepAutoFetchAt = UnityEngine.Time.realtimeSinceStartupAsDouble;
        // 0.10.2: silent enqueue — the wep tab already renders the reply in
        // the structured display, so the chat copy is redundant noise.
        if (MessageService.IsInitialized)
            MessageService.EnqueueMessageSilent(MessageService.BCCOM_WEP_GET);
        else
            EnqueueOrWarn(MessageService.BCCOM_WEP_GET);
    }

    private void FireBlInfoFetch()
    {
        _lastBlAutoFetchAt = UnityEngine.Time.realtimeSinceStartupAsDouble;
        var leg = PlayerStateService.Legacy;
        // 0.13.1: only fire `.bl get <Type>` when Type is a bondable blood
        // Bloodcraft will reply to. Skips Frailed / VBlood / GateBoss —
        // those produce no reply, and arming AwaitingBloodInfo for them
        // produces the timeout-spam logs the user reported when their blood
        // drained mid-session.
        if (!PlayerStateService.IsBondableBloodType(leg.Type)) return;
        if (MessageService.IsInitialized)
            MessageService.EnqueueMessageSilent(string.Format(MessageService.BCCOM_BL_GET_FORMAT, leg.Type));
        else
            EnqueueOrWarn(string.Format(MessageService.BCCOM_BL_GET_FORMAT, leg.Type));
    }

    internal override void Reset()
    {
        // 0.67.1 lazy-page teardown, in order: stop every deferred/ticker path first so nothing builds or
        // refreshes into a panel that's being destroyed, then drain the per-page teardowns, then the
        // panel-wide unsubscribes below (RunFaustPickerUnsubs / DetachAll stay as safety drains).
        _disposed = true;
        _pendingTab = null;
        if (_prewarmTicker != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_prewarmTicker);
            _prewarmTicker = null;
        }
        if (_deferredFeatureFlagsRefresh != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_deferredFeatureFlagsRefresh);
            _deferredFeatureFlagsRefresh = null;
        }
        if (_prestigeExoTicker != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_prestigeExoTicker);
            _prestigeExoTicker = null;
        }
        if (_mapImageSubscribed)
        {
            FaustMapBackdrop.ImageLoaded -= OnFaustMapImageLoaded;
            _mapImageSubscribed = false;
        }
        PageBuildContext.Drain(_buildingTeardown);
        foreach (var td in new List<List<System.Action>>(_pageTeardown.Values)) PageBuildContext.Drain(td);
        _pageTeardown.Clear();

        if (_availabilitySubscribed)
        {
            Services.EclipseProtocolService.AvailabilityChanged -= OnBloodcraftAvailabilityChanged;
            PlayerStateService.FeatureFlagsChanged -= OnFeatureFlagsChanged;
            Services.Beelzebub.BeelzProtocolService.AvailabilityChanged -= OnBloodcraftAvailabilityChanged;
            Services.Beelzebub.BeelzState.PresenceChanged -= OnBloodcraftAvailabilityChanged;   // 0.24.7: matches the subscribe above
            Services.Uriel.UrielProtocolService.AvailabilityChanged -= OnBloodcraftAvailabilityChanged;   // 0.26: matches the subscribe above
            Services.Uriel.UrielState.PresenceChanged -= OnBloodcraftAvailabilityChanged;
            Services.Faust.FaustProtocolService.AvailabilityChanged -= OnBloodcraftAvailabilityChanged;   // matches the subscribe above
            Services.Faust.FaustState.PresenceChanged -= OnBloodcraftAvailabilityChanged;
            Services.Nyar.NyarProtocolService.AvailabilityChanged -= OnBloodcraftAvailabilityChanged;
            Services.Nyar.NyarState.PresenceChanged -= OnBloodcraftAvailabilityChanged;
            _availabilitySubscribed = false;
        }
        UnsubscribeBeelz(); // 0.18: drop BeelzState handlers so they don't leak across rebuilds
        UnsubscribeUriel(); // 0.26: same, for UrielState handlers
        UnsubscribeFaust(); // drop FaustState handlers so they don't leak across rebuilds
        UnsubscribeNyar();  // same, for NyarState handlers + the countdown tick
        RunFaustPickerUnsubs(); // 0.65.6: + the Faust picker repop closures (see MainPanel.Faust.cs)
        BoxNameDropdownField.DetachAll(); // 0.65.6: + the box-name form fields' BoxListChanged handlers
        if (_allFamSubscribed) // 0.65.6: All Familiars tab (subscribed in BuildAllFamiliarsTab) was never detached
        {
            PlayerStateService.BoxContentsChanged -= OnAllFamBoxContentsChanged;
            VBloodScannerService.ScanStateChanged -= OnAllFamScanStateChanged;
            _allFamSubscribed = false;
        }
        if (_deferredAvailabilityRefresh != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_deferredAvailabilityRefresh);
            _deferredAvailabilityRefresh = null;
        }
        if (_tabAutoRefreshTicker != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_tabAutoRefreshTicker);
            _tabAutoRefreshTicker = null;
        }
        if (_sizePosReadoutTicker != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_sizePosReadoutTicker);
            _sizePosReadoutTicker = null;
        }
        _sizePosRefreshers.Clear();
        if (_wepLastResponseSubscribed)
        {
            PlayerStateService.LastResponseChanged -= OnLastResponseChangedForWep;
            _wepLastResponseSubscribed = false;
        }
        if (_vbSubscribed)
        {
            PlayerStateService.VBloodCollectionChanged -= OnVBloodCollectionChanged;
            Services.VBloodScannerService.ScanStateChanged -= OnVBloodScanStateChanged;
            _vbSubscribed = false;
        }
        if (_vbSummonStatusSubscribed)
        {
            Services.VBloodSummonService.StatusChanged -= OnVBSummonStatusChanged;
            _vbSummonStatusSubscribed = false;
        }
        if (_famSubscribed)
        {
            PlayerStateService.FamiliarChanged -= OnFamiliarChanged;
            _famSubscribed = false;
        }
        if (_famSearchSubscribed)
        {
            MessageService.FamSearchCompleted -= OnFamSearchCompletedForFamTab;
            _famSearchSubscribed = false;
        }
        if (_classSubscribed)
        {
            PlayerStateService.ExperienceChanged -= OnExperienceChangedForClass;
            _classSubscribed = false;
        }
        if (_wepSubscribed)
        {
            PlayerStateService.ExpertiseChanged -= OnExpertiseChanged;
            _wepSubscribed = false;
        }
        if (_shiftSubscribed)
        {
            PlayerStateService.ExpertiseChanged  -= OnExpertiseChangedForUnarmed;
            PlayerStateService.ShiftSpellChanged -= OnShiftSpellChanged;
            _shiftSubscribed = false;
        }
        if (_boxesSubscribed)
        {
            PlayerStateService.BoxListChanged     -= OnBoxListChanged;
            PlayerStateService.BoxContentsChanged -= OnBoxContentsChanged;
            PlayerStateService.ActiveBoxChanged   -= OnActiveBoxChanged;
            _boxesSubscribed = false;
        }
        if (_prestigeSubscribed)
        {
            PlayerStateService.ExperienceChanged -= OnAnyForPrestige;
            PlayerStateService.LegacyChanged     -= OnAnyForPrestige;
            PlayerStateService.ExpertiseChanged  -= OnAnyForPrestige;
            PlayerStateService.FamiliarChanged   -= OnAnyForPrestige;
            _prestigeSubscribed = false;
        }
        if (_lvlSubscribed)
        {
            PlayerStateService.ExperienceChanged -= OnAnyForLevels;
            PlayerStateService.LegacyChanged     -= OnAnyForLevels;
            PlayerStateService.ExpertiseChanged  -= OnAnyForLevels;
            PlayerStateService.FamiliarChanged   -= OnAnyForLevels;
            PlayerStateService.ProfessionChanged -= OnAnyForLevels;
            _lvlSubscribed = false;
        }
        if (_dqSubscribed)
        {
            PlayerStateService.QuestChanged -= OnQuestChangedForTab;
            _dqSubscribed = false;
        }
        if (_blSubscribed)
        {
            PlayerStateService.LegacyChanged -= OnLegacyChanged;
            _blSubscribed = false;
        }
        if (_blInfoSubscribed)
        {
            PlayerStateService.BloodInfoChanged -= OnBloodInfoChanged;
            _blInfoSubscribed = false;
        }
        if (_prestigeInfoSubscribed)
        {
            PlayerStateService.PrestigeInfoChanged -= OnPrestigeInfoChanged;
            _prestigeInfoSubscribed = false;
        }
        if (_collapsibleSubscribed)
        {
            CollapsibleSection.Toggled -= AutoResizeIfEnabled;
            _collapsibleSubscribed = false;
        }
        if (_lastResponseSubscribed)
        {
            PlayerStateService.LastResponseChanged -= OnLastResponseChanged;
            _lastResponseSubscribed = false;
        }
    }
}
