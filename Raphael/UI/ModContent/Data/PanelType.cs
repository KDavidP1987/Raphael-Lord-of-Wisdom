namespace Raphael.UI.ModContent.Data;

// Identity tag for each kind of panel/overlay Raphael can host.
//
// The copied UI framework (UniverseLib panels) references PanelType for panel
// identity + persistent config keys, so we keep the enum in the same namespace
// path that the upstream BloodCraftUI used. New tabs/overlays add a value here.
public enum PanelType
{
    Base,                   // root content / floating button host

    // Primary-overlay tabs (the main UI):
    FamiliarsTab,
    BoxesTab,
    VBloodsTab,             // 0.10.0: collection tracker for V-Blood familiars across all boxes
    AllFamiliarsTab,        // 0.11.0: cross-box list of every captured familiar with filter/sort/delete
    ClassTab,
    ExpertiseTab,
    BloodLegacyTab,
    UnarmedShiftTab,
    PrestigeTab,
    LevelsTab,
    AdminTab,

    // Kindred-suite tabs (companion server mods to Bloodcraft):
    KindredLogisticsTab,
    KindredLogisticsAdminTab,
    KindredCommandsPlayerTab,
    KindredAdminPlayersTab,
    KindredAdminServerTab,
    KindredAdminWorldTab,

    // Bloodcraft daily quest tab + overlay:
    DailyQuestTab,
    DailyQuestOverlay,

    // Help / Reference tabs:
    QuickStartTab,          // 0.19: now a GENERAL Raphael getting-started (mod-agnostic)
    BloodcraftQuickStartTab,// 0.19: Bloodcraft-specific getting-started (parallel to the Beelzebub one)
    ModHelpTab,             // 0.13.0: deeper Bloodcraft mechanics reference
    VRisingBasicsTab,       // in-built base-game guide: vanilla V Rising mechanics + how each mod changes them
    GameGuideTab,           // 0.12.1: V Rising game guide / external links
    VanillaAdminTab,
    AboutTab,
    SettingsTab,
    ConnectionTab,          // 0.19: always-available mod connection/detection status + re-detect (Bloodcraft + Beelzebub)

    // Standalone enhancement tabs (client-side; work on any server, no
    // Bloodcraft / Kindred required). 0.17+.
    GameUITab,              // 0.17: home for standalone V Rising UI enhancements

    // Beelzebub-suite tabs (0.18: client UI for the server-side ability-capture
    // /transform mod; whole group gated on the `.beelz api version` handshake):
    BeelzBestiaryTab,       // collection tracker (captured vs available abilities)
    BeelzLoadoutTab,        // 6-slot ability-exchange editor (universal + per-weapon)
    BeelzHotkeysTab,        // named extra-ability bindings -> action-bar overlay
    BeelzTransformsTab,     // Dracula / Morgana transform panel
    BeelzSettingsTab,       // client-side Beelzebub settings: diagnostics, availability, overlay
    BeelzAdminConfigTab,    // admin: generic settings from api config
    BeelzAdminPlayersTab,   // admin: grant / devour / inspect / recovery
    BeelzAdminAbilityTableTab, // 0.19 (F13): mass per-ability config — tabular editor over the scanned catalog
    BeelzQuickStartTab,     // help: Beelzebub getting-started (lives in the Settings & Help group)
    BeelzModHelpTab,        // help: Beelzebub mechanics deep-dive (lives in the Settings & Help group)

    // Uriel-suite tabs (0.26: client UI for the server-side storage-share /
    // public-prison / stair-swap / object-spawning mod; whole group gated on
    // the `.uriel api version` handshake). One player tab per sub-feature +
    // admin tabs (inline, server-enforced like the Beelzebub/Kindred admin tabs).
    UrielStorageTab,        // player: share/unshare chests + permissions/limits/cost/paychest
    UrielPrisonTab,         // player: shared prison cells + Take Prisoner (pass 2)
    UrielStairsTab,         // player: live stair restyle (six same-shape styles)
    UrielObjectsTab,        // player: object-spawn palette (unlocked list) + collection progress
    UrielObjectCatalogTab,  // player: full spawnable-object catalog browse, columnar (name/category/id)
    UrielSettingsTab,       // client-side Uriel settings: diagnostics, availability readout
    UrielAdminSharingTab,   // admin: sharedall / unshareplayer / unshareall / sharedebug
    UrielAdminObjectsTab,   // admin: block/grant/blocklist/bossmap/purgeplot/forcedespawn
    UrielAdminConfigTab,    // admin: kill-switch status (storage / prison / stairswap / collection)
    UrielQuickStartTab,     // help: Uriel getting-started (lives in the Settings & Help group)
    UrielModHelpTab,        // help: Uriel mechanics + command reference (Settings & Help group)

    // Faust-suite tabs (client UI for the server-side investigation/information mod; whole group gated
    // on the `.faust api version` handshake). Player read tabs query castles/plots/players/server stats;
    // admin tabs drive the `.faust admin …` runtime control surface (server-enforced, inline-gated like
    // the Beelzebub/Uriel admin tabs). Positions/Resources/Admin land in Phase 2.
    FaustCastleInfoTab,     // player: castle owner/region/size/decay (here|nearest|tindex)
    FaustPlotsTab,          // player: open (heart-less) territories, largest first
    FaustAllPlotsTab,       // admin: every territory (claimed + open) with full castle detail — needs `.faust api castles`
    FaustDecayWatchTab,     // admin: claimed castles by soonest decay (housekeeping) — needs `.faust api decay` (api 9)
    FaustResourcesTab,      // player/admin: castle container totals (raid intel) — Phase 2
    FaustPlayerInfoTab,     // player: playtime/frequency/peak-hour (self always; others gated)
    FaustClansTab,          // admin: clan composition (clanned vs solo split + per-clan rosters) — needs `.faust api clans` (api 11)
    FaustPositionsTab,      // admin: online player coordinates (sortable list) — Phase 2
    FaustStatsTab,          // player: playtime leaderboard + concurrency series
    FaustBossesTab,         // admin: V Blood boss status board (live/defeated) — needs `.faust api bosses` (api 18, §B1)
    FaustKillsTab,          // admin: kill + boss-defeat leaderboards — needs `.faust api kills`/`bosskills` (api 18, §B2)
    FaustWorldScanTab,      // admin: filterable map of in-world units + resource nodes — needs `.faust api worldscan` (api 18, §C1)
    FaustSettingsTab,       // client-side Faust settings: diagnostics, availability readout
    FaustAdminControlTab,   // admin: block/unblock/schedule/status — Phase 2
    FaustAdminAccessTab,    // admin: grant/revoke/unlocks — Phase 2
    FaustAdminOversightTab, // admin: who-can-use-what (access) + usage/spend reporting (Faust 0.14 / api 13)
    FaustQuickStartTab,     // help: Faust getting-started (lives in the Settings & Help group)
    FaustModHelpTab,        // help: Faust command/feature reference (Settings & Help group)

    // Secondary overlays (independent draggable panels):
    ExperienceOverlay,
    FamiliarOverlay,
    FamiliarBrowserOverlay,
    FamiliarQuickSpawnOverlay,  // 0.52: up-to-5 user-assigned familiars, one-click summon by name (.fam sb)
    FaustBossTrackerOverlay,    // 0.54: up-to-3 tracked V Blood statuses (reads Faust boss board), optional ~5s auto-refresh
    ProfessionOverlay,
    ShiftSpellOverlay,      // 0.11.0: visual cooldown ring/bar for the shift slot (Eclipse-style)
    CombinedOverlay,        // 0.14.0: single combined info overlay (XP + Familiar + Wep + Blood + Prof + Quests)
    QuickActionsOverlay,    // 0.16: configurable one-click Kindred action buttons (Stash All)
    ChatWindowOverlay,      // 0.17: standalone tabbed chat window (Game UI group)
    SecondaryChatOverlay,   // 0.24: view-only second chat window mirroring a chosen subset of channels
    BeelzActionBarOverlay,  // 0.18: on-screen extra-ability buttons + cooldown rings
    BeelzSummonsOverlay,    // 0.19: one-click stash/restore (+ recall/clear) for Beelzebub summons
    BeelzTransformOverlay,  // 0.20: browser-style transform overlay (double-click to transform + phase/revert)
    UrielSharedOverlay,     // 0.26: nearby public-storage badges (client-side detection of Uriel-shared containers)
    UrielObjectSpawnerOverlay, // 0.29: draggable quick-build palette of your unlocked Uriel objects (category cycler + per-row Spawn)

    // Legacy panel identities from BloodCraftUI — kept so the existing
    // ResizeablePanelBase config keys (Panels/<PanelType>) survive a port.
    BoxList,
    BoxContent,
    FamStats,
    TestPanel,
}
