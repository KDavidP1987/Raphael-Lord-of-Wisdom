using System.Collections.Generic;

namespace Raphael.Services.Guides;

// 0.73.0: the Faust guide, checked against Faust 0.16.5 (Commands/*.cs, Config/Settings.cs, Config/ConfigEditor.cs,
// Services/FaustAccessGate.cs) and Raphael's Faust tabs (UI/ModContent/MainPanel.Faust.cs).
internal static partial class Guides
{
    static partial void AddFaust(List<Guide> list)
    {
        static string A(string n) => GuideText.Arg(n);
        static string C(string t) => GuideText.Cmd(t);

        const string Cfg = "BepInEx/config/kdpen.Faust.cfg";

        var b = new GuideBuilder("faust", "Faust guide",
            "Server intel on castles, plots, players, bosses and activity, gated feature by feature by the admin.",
            "Faust 0.16.5", GuideKind.FullMod, "faust");

        // ------------------------------------------------------------------ 1. What it is
        b.Section(GuideSchema.What, true, s => s.Bullets(
            "<b>Faust, Lord of Investigation</b> answers questions the game client cannot: who owns a castle, which plots are free, " +
            "when a player was last online, where the V Bloods are, who kills the most, and how busy the server is.",
            "It is a <b>server-side</b> mod. Raphael is the UI: the <b>Faust</b> tab group sends Faust's queries and draws the answers as " +
            "tables, charts and maps.",
            "It is an admin oversight tool first. <b>Every feature starts admin-only.</b> The server's admin decides, feature by feature, " +
            "what players may use and whether it costs an item, has a cooldown, or needs an unlock.",
            "Admins also get runtime blocks and schedules, a live config editor, player unlock grants and data management, all from the " +
            "<b>Admin:</b> tabs."));

        // ------------------------------------------------------------------ 2. Quick start
        b.Section(GuideSchema.QuickStart, true, s =>
        {
            s.Steps(
                "Join a server that runs Faust. The <b>Faust</b> tab group unlocks once Raphael's handshake gets a reply. If it stays grey, " +
                "press <b>Re-check now</b> in the Faust rail.",
                "Open any Faust tab and read the grey line under its intro. It says <b>admin-only</b> or <b>open to players</b>, plus any cost " +
                "and cooldown. <b>(unavailable on this server)</b> means the admin turned that feature off.",
                "Open <b>Player Info</b>, pick yourself in <b>Online player</b> and press <b>Look up</b>. Your own play history is always " +
                "allowed, even when the feature is admin-only.",
                "If the admin opened castle features to players, open <b>Castle Info</b> and press <b>Here</b> to see who owns the territory " +
                "you stand in. Try <b>Open Plots</b> → <b>Find open plots</b> to find a free building spot.",
                "Explore the other tabs the grey line marks as open: <b>Server Stats</b>, <b>Clans</b>, <b>Boss Status</b>, <b>Leaderboards</b>.",
                "If a query is refused, the tab's result line says why (admin-only, cost, cooldown, blocked, locked). See <b>Troubleshooting</b>.");
            s.Callout(CalloutKind.Note,
                "Some queries cost an item, the \"Faustian toll\". You are only charged when the query returns a real answer, never for " +
                "an empty or not-found result.");
        });

        // ------------------------------------------------------------------ 3. How it works
        b.Section(GuideSchema.Concepts, false, s =>
        {
            s.Detail("The thirteen features and what each reveals", d =>
            {
                d.P("Faust gates 13 features. Each has a lowercase key used in admin commands and in the cfg section name " +
                    "(" + C("[Faust.<key>]") + ").");
                d.Bullets(
                    "<b>castleinfo</b>: one territory: owner, clan, region, size, heart level, floors, decay state and time left, owner " +
                    "online and last online, total item count. Tab: Castle Info.",
                    "<b>plotavailability</b>: free territories without a heart, largest first, with region and map position. Tab: Open Plots.",
                    "<b>allcastles</b>: every territory on the map, claimed and open, in one table. Tab: All Plots.",
                    "<b>decaywatch</b>: claimed castles ordered by soonest decay. Sealed castles never decay and sort last. Tab: Decay Watch.",
                    "<b>castleresources</b>: the summed contents of every container in a castle, plus its prisoners (name, blood type, " +
                    "quality). Raid intel. Tab: Castle Resources.",
                    "<b>playerinfo</b>: a player's online state, last online, first seen, sessions, total playtime, logins per week, busiest " +
                    "hour and days idle. Your own info skips the access level and the unlock check (a cost, cooldown or block still applies). Tab: Player Info.",
                    "<b>playerpositions</b>: coordinates, territory and region of every online player. Tab: Player Positions.",
                    "<b>heatmap</b>: a density grid of where players spend time, server-wide or for one player. Needs collection turned on " +
                    "(" + C("[Faust.Heatmap] Enabled") + "). Tab: Player Positions.",
                    "<b>stats</b>: server statistics and charts (playtime, population, activity by hour and day, retention, regions, rosters). " +
                    "It also gates the admin oversight reports. Tabs: Server Stats, Player Info charts, Admin: Oversight.",
                    "<b>clans</b>: clanned versus solo players and each clan's roster: members, who is online, castles held, leader. Tab: Clans.",
                    "<b>bosses</b>: every V Blood: live in the world (position, region, HP, level), not spawned, or defeated. Tab: Boss Status.",
                    "<b>kills</b>: top killers (with their PvP kill count) and the most-defeated V Bloods, for today, this week or all time. " +
                    "Tab: Leaderboards.",
                    "<b>worldscan</b>: NPC units (blood type and quality, HP) and resource nodes across the whole map, filtered. Only " +
                    "whitelisted prefabs, and never V Bloods. Tab: World Map.");
            });

            s.Detail("Access level: Off, AdminOnly, Players", d =>
            {
                d.Bullets(
                    "<b>Off</b>: the feature is switched off for everyone, admins included. Queries reply " + C("code=disabled") + ".",
                    "<b>AdminOnly</b> (the default for all 13): only admins get answers. Players get " + C("code=noaccess") + ".",
                    "<b>Players</b>: anyone may query it, subject to the other gates below.");
                d.P("Change it with " + C(".faust admin set <feature> access=Players") + " or the <b>Admin: Control</b> → <b>Adjust settings</b> " +
                    "buttons <b>Off</b> / <b>Admins</b> / <b>Players</b>. It applies at once.");
            });

            s.Detail("The gate: the order Faust checks things", d =>
            {
                d.P("Every query passes one gate before Faust gathers anything. The first check that fails refuses the query with a code:");
                d.Steps(
                    "Faust ready (" + C("notready") + ") and the master switch " + C("[Faust] Enabled") + " (" + C("disabled") + ").",
                    "An admin block or schedule (" + C("blocked") + ", " + C("schedule") + ").",
                    "The feature's access is not Off (" + C("disabled") + ").",
                    "The per-player anti-spam floor " + C("RateLimitSeconds") + " (" + C("ratelimit") + ").",
                    "The unlock requirement (" + C("locked") + ").",
                    "The access level (" + C("noaccess") + ").",
                    "The PvP / PvE availability (" + C("pvp") + ").",
                    "The proximity requirement (" + C("notnear") + ").",
                    "The cooldown and the window / period allowance (" + C("cooldown") + ", " + C("window") + ").",
                    "The item cost: the requester must carry it (" + C("cost") + ").");
                d.P("<b>Admin exemption.</b> With the feature's <b>AdminsExempt</b> on (default true), admins skip the unlock, access, PvP, " +
                    "proximity, cooldown, window and cost checks. With <b>RateLimitAdminsExempt</b> on (default true) they also skip the rate " +
                    "limit. Admins are <b>never</b> exempt from the master switch, an Off feature, or a block or schedule.");
            });

            s.Detail("Costs: the Faustian toll", d =>
            {
                d.P("A feature can charge an item per query: <b>CostItemGuid</b> (the item's PrefabGUID) times <b>CostQuantity</b>. " +
                    "Both default to 0, which means free. The cost applies only when both are set.");
                d.P("The gate first checks that you carry enough. The item is removed only after the query produced a real result, so " +
                    "a not-found lookup or a refused query costs nothing. Raphael shows the price in each tab's grey line.");
                d.P("Use " + C(".faust admin prefab <nameFragment>") + " to find an item's PrefabGUID.");
            });

            s.Detail("Cooldowns, windows and periods", d =>
            {
                d.Bullets(
                    "<b>CooldownSeconds</b>: a per-player lockout after each successful use. 0 = none. Example: pay a cost, then wait 60 seconds.",
                    "<b>PeriodSeconds</b>: the allowance cycle (86400 = daily). 0 = no period; only the cooldown applies.",
                    "<b>MaxUsesPerPeriod</b>: how many uses (or window openings) a period allows. 0 = unlimited.",
                    "<b>WindowSeconds</b>: once used, the feature stays open this long and repeat uses inside the window are free and do not " +
                    "count. 0 = each use counts on its own.");
                d.P("Example: WindowSeconds 600, PeriodSeconds 86400, MaxUsesPerPeriod 1 gives one 10-minute window per day. Outside it the " +
                    "query replies " + C("code=window") + " with the seconds until the period rolls over.");
                d.P("Usage is tracked per player and feature and survives a restart (" + C("BepInEx/config/Faust/feature_usage.json") + "). " +
                    C(".faust admin data wipe usage confirm") + " clears every lock.");
            });

            s.Detail("PvP availability, unlocks and proximity", d =>
            {
                d.Bullets(
                    "<b>Availability</b>: <b>Always</b> (default), <b>PvEOnly</b> or <b>PvPOnly</b>, checked against the server's game mode. " +
                    "For example, make enemy-resource intel PvPOnly.",
                    "<b>Unlock</b>: a progression gate. <b>None</b> (default), <b>FinalBoss</b> (after defeating Dracula), or " +
                    C("BossKill:<vbloodGuid>") + " (after defeating that V Blood). <b>AllBosses</b> and <b>AllQuests</b> are accepted but not " +
                    "detected yet, so they act as grant-only. An admin grant (" + C(".faust admin grant <player> <feature>") + ") always unlocks.",
                    "<b>RequireNearPrefab</b> / <b>RequireNearDistance</b>: the player must stand within that many metres (default 5) of a " +
                    "placed or world object with that PrefabGUID, such as an altar in a castle. 0 = no proximity requirement.");
                d.Callout(CalloutKind.Note,
                    "A grant overrides the unlock requirement only. It does not open an AdminOnly feature to a player. Set access to Players for that.");
            });

            s.Detail("Delivery: ServerMediated or Free", d =>
            {
                d.P("Each feature has a <b>Delivery</b> setting. <b>ServerMediated</b> (default) routes the query through the gate so it can be " +
                    "limited, charged and audited. <b>Free</b> is meant for near-player data a client could read locally.");
                d.P("In Faust 0.16.5 every query goes through the server gate whichever value is set; nothing reads Delivery yet. " +
                    "Raphael's <b>Server</b> / <b>Free</b> buttons only change the stored value.");
            });

            s.Detail("Blocks, countdowns and schedules", d =>
            {
                d.P("These are admin switches layered on top of the config. They apply to everyone, admins included, and survive a restart " +
                    "(" + C("BepInEx/config/Faust/feature_control.json") + ").");
                d.Bullets(
                    "<b>Block</b>: " + C(".faust admin block <feature> [minutes]") + " closes a feature now. With minutes it reopens by itself " +
                    "(a countdown); without, it stays closed until unblocked.",
                    "<b>Schedule</b>: " + C(".faust admin schedule <feature> 18:00-22:00") + " allows use only in that daily window, in " +
                    "<b>server local time</b>. A window may wrap past midnight (22:00-02:00).",
                    "Use <b>all</b> as the feature to block or schedule every feature at once. The <b>all</b> rule and each feature's own rule " +
                    "are separate: " + C(".faust admin unblock all") + " clears only the <b>all</b> block, not a block set on one feature.");
            });

            s.Detail("What Faust collects and where it is stored", d =>
            {
                d.P("Most features read the live world when asked and cost nothing while idle. A few collectors build history:");
                d.Bullets(
                    "<b>Sessions</b> (connect / disconnect) power playtime, frequency, busiest hour and most stats. Switch: " +
                    "<b>SessionTracking</b> (on).",
                    "<b>Concurrency samples</b> (online count at each connect / disconnect) power population graphs. Switch: " +
                    "<b>ConcurrencySampling</b> (on).",
                    "<b>Kills</b> are tallied on each death and power the leaderboards. Switch: <b>KillTracking</b> (on).",
                    "<b>Heat map</b> samples every online player's position on a timer. Switch: " + C("[Faust.Heatmap] Enabled") + " (<b>off</b>).",
                    "<b>Unlock progress</b> (V Blood defeats and admin grants) and <b>usage</b> locks (cooldowns, windows) are kept for the gate.");
                d.P("Data lives in " + C("BepInEx/config/Faust/") + ", not in the world save, so it <b>survives a world wipe</b>. The same players " +
                    "return, so their history stays useful. Reset it on purpose with " + C(".faust admin data wipe") + ". History starts when " +
                    "Faust was installed, so \"new players\" means first seen by Faust.");
            });

            s.Detail("Server stats views", d =>
            {
                d.P("All charts use <b>UTC</b> hours and days. Unless a label says avg, numbers are totals across all players.");
                d.Bullets(
                    "<b>playtime</b>: leaderboard of total minutes per player.",
                    "<b>concurrency</b>: the stored online-count history.",
                    "<b>hours</b> / <b>weekdays</b> / <b>sessions</b>: playtime per hour of day, per weekday, and session-length spread, " +
                    "server-wide or for one player.",
                    "<b>daily</b>: distinct players and play-minutes per day (default 14 days, range 1-90).",
                    "<b>newplayers</b>: first-seen players per day (default 30 days), plus a roster of who joined.",
                    "<b>pdaily</b>: one player's playtime per day (default 90 days).",
                    "<b>population</b>: DAU / WAU / MAU, new versus returning, stickiness, and D1 / D7 / D30 retention.",
                    "<b>recency</b>: players active in 24 hours, 7 days, 30 days, or dormant.",
                    "<b>peak</b>: peak and 95th-percentile online count over a window (default 30 days).",
                    "<b>regions</b> / <b>regiondaily</b>: players and claimed castles per region, and castle fill per day. Faust samples regions " +
                    "once per UTC day from install, so the series grows forward.",
                    "<b>players</b> / <b>activegrid</b>: every tracked player with last seen, sessions, playtime and days idle; and the days each " +
                    "player was active.",
                    "<b>Session timeline</b>: when each player was actually online (default 14 days).");
            });

            s.Detail("Bosses: live, not spawned, defeated", d =>
            {
                d.P("Faust sees the whole map, so it reports bosses your client is too far away to load. <b>Live</b> means Faust found a placed " +
                    "boss within " + C("[Faust.Bosses] MapLimit") + " (default 9000 world units on both axes) and sends its position, region, HP " +
                    "and level. <b>Not spawned</b> means it found none (defeated, pooled or awaiting respawn).");
                d.P("If a boss you know is alive reads Not spawned, raise MapLimit a little (keep it under about 10000) or run " +
                    C(".faust admin bossdiag <name>") + ".");
            });

            s.Detail("World scan: whitelist and filters", d =>
            {
                d.P("The world scan returns only prefabs on an admin-curated whitelist. On first start Faust seeds it from the game's unit and " +
                    "resource-node catalog. Manage it with " + C(".faust admin worldscan list|add|remove|seed|clear") + ".");
                d.P("Scanning the map is heavy, so Faust caches the snapshot for " + C("ScanIntervalSeconds") + " (default 10, minimum 5) and " +
                    "rescans only when someone asks after that. A broad result is capped at " + C("MaxResults") + " (default 10000); the reply " +
                    "then says it was truncated.");
                d.P("The filter is one token with no spaces, comma-separated: " + C("type=units|nodes|all") + ", " + C("id=<prefabGuid>") + ", " +
                    C("bloodtype=<guid>") + ", " + C("bloodqmin=<0-100>") + ", " + C("unittype=<n>") + ". Example: " +
                    C(".faust api worldscan type=units,bloodqmin=90") + ".");
            });

            s.Detail("Heat map", d =>
            {
                d.P("With collection on, Faust records every online player's position every " + C("SampleSeconds") + " (default 60, 30-300) " +
                    "into " + C("CellSize") + " squares (default 25 world units). The all-time map is never pruned; per-day history for the " +
                    "Today / This week / This month windows is kept for " + C("RetentionDays") + " (default 30).");
                d.P("Raphael's <b>Detail</b> button can only group cells coarser. For a finer map an admin lowers CellSize, after wiping the " +
                    "old heat data so resolutions don't mix.");
            });
        });

        // ------------------------------------------------------------------ 4. Raphael's tabs
        b.Section(GuideSchema.Tabs, false, s =>
        {
            s.P("The Faust group appears only after the server answers " + C(".faust api version") + ". The Boss Status, Leaderboards, World " +
                "Map and Admin tabs are greyed for non-admins (press <b>Re-check admin</b> after adminauth); the server still decides who " +
                "gets answers.");
            s.Tabs(t => t
                .Row("Castle Info", "Owner, clan, region, size, decay and more for one territory. Press <b>Here</b>, <b>Nearest</b> (nearest " +
                     "castle heart), pick from the list, or type an index and press <b>Look up index</b>.",
                     ".faust api castleinfo here|nearest|<index>")
                .Row("Open Plots", "Free territories. Press <b>Find open plots</b>; cycle <b>Sort</b> and <b>Region</b> to filter.",
                     ".faust api plots")
                .Row("All Plots", "Every territory, claimed and open. Press <b>Load all plots</b>; <b>Claimed castles only</b> hides open plots.",
                     ".faust api castles")
                .Row("Decay Watch", "Claimed castles by soonest decay. Press <b>Refresh decay list</b>.", ".faust api decay")
                .Row("Castle Resources", "Summed container contents and prisoners of a castle. <b>Here</b>, <b>Nearest</b>, or <b>Scan index</b>.",
                     ".faust api resources here|nearest|<index>")
                .Row("Player Info", "A player's play history. Pick an online player or type a name or SteamID and press <b>Look up</b>. " +
                     "Chart buttons: <b>Activity by hour</b>, <b>Session lengths</b>, <b>By day of week</b>, <b>Daily / weekly trend</b>.",
                     ".faust api pinfo <player>; .faust api stats hours|sessions|weekdays|pdaily <player>")
                .Row("Clans", "Clan versus solo split and per-clan rosters. Press <b>Refresh clans</b>; click a clan name (or type it) and " +
                     "press <b>View members</b>.", ".faust api clans; .faust api clanmembers <clan>")
                .Row("Player Positions", "Online players' positions (<b>Refresh positions</b>), the heat map (<b>Server heat map</b>, " +
                     "<b>This player's heat map</b>, <b>When:</b> window, <b>View:</b> heat map or players now), and the collapsed admin " +
                     "<b>Show players on map</b> controls.",
                     ".faust api positions; .faust api heatmap all|<player> <days>; .faust admin showpositions on|off|status")
                .Row("Server Stats", "Server-wide charts: <b>Playtime leaderboard</b>, <b>Concurrency</b>, <b>By hour of day</b>, " +
                     "<b>Session lengths</b>, <b>New players</b>, <b>New vs returning</b>, <b>Daily activity</b>, <b>By day of week</b>, " +
                     "<b>By week</b>, <b>Population health</b>, <b>Player recency</b>, <b>Peak concurrency</b>, <b>By region</b>, " +
                     "<b>Region over time</b>, <b>Player roster</b>, <b>Active-days grid</b>, <b>Session timeline</b>. <b>Days window</b> " +
                     "(1-90) bounds the time views; <b>Refresh data</b> re-runs the current one.",
                     ".faust api stats <kind> ...; .faust api newplayers roster; .faust api sessions timeline all")
                .Row("Boss Status", "The V Blood board. <b>Refresh boss board</b>, <b>Show:</b> filter, <b>Look up boss</b>, and " +
                     "<b>+ Track</b> on a row to pin it to the boss tracker overlay (<b>Show / hide overlay</b>). Admin buttons: " +
                     "<b>Diagnose detection (admin)</b>, <b>Set limit (admin)</b>.",
                     ".faust api bosses; .faust api boss <name|guid>; .faust admin bossdiag; .faust admin setglobal bossmaplimit=<n>")
                .Row("Leaderboards", "Top killers and most-defeated V Bloods. Cycle the window (today / this week / all time), then " +
                     "<b>Refresh killers</b> or <b>Refresh boss defeats</b>.", ".faust api kills <days>; .faust api bosskills <days>")
                .Row("World Map", "Units and resource nodes on an X / Z map. Choose <b>Type</b>, <b>Category</b> and <b>Type (sub)</b>, then " +
                     "<b>Scan world</b>. The map underlay can be captured from the game and calibrated.", ".faust api worldscan <spec>")
                .Row("Settings", "Raphael-only display options: <b>Diagnostics</b> logging, decay <b>Format</b>, chart width and color, heat-map " +
                     "colors and detail, and the Faust tab group's availability (Auto / On / Off). Sends nothing.")
                .Row("Admin: Control", "Pick a feature, then <b>Block</b> / <b>Unblock</b>, <b>Set schedule</b> / <b>Clear schedule</b>, " +
                     "<b>Status</b>; <b>Adjust settings</b> quick buttons (access, PvP, delivery, admins exempt, cost, cooldown, limit, " +
                     "proximity, unlock); the live config editor and <b>Global settings</b>; the world-scan whitelist; and data " +
                     "management (<b>Data status</b>, <b>Prune activity</b>, <b>Preview wipe</b>, <b>Wipe - CONFIRM</b>).",
                     ".faust admin block|unblock|schedule|status|set|get|resetcfg|setglobal|getglobal|worldscan|prefab; .faust admin data ...")
                .Row("Admin: Access", "Pick a player and a feature, then <b>Grant</b>, <b>Revoke</b> or <b>Show unlocks</b>.",
                     ".faust admin grant|revoke <player> <feature>; .faust admin unlocks <player>")
                .Row("Admin: Oversight", "Per-feature access, price, grants and qualifiers (<b>Refresh access</b>), and usage, payers, items " +
                     "spent and cooldown denials over a window (<b>Refresh usage</b>, default 7 days). Gated by the <b>stats</b> feature.",
                     ".faust api access; .faust api usage <days>"));
            s.P("Admin tab buttons send visible chat commands, so Faust's reply appears in chat. Player tabs send silent queries and show " +
                "the answer in the tab.");
        });

        // ------------------------------------------------------------------ 5. Player commands
        b.Section(GuideSchema.Player, false, s =>
        {
            s.Commands("Help", c => c
                .Player("faust", ".faust", "Prints a short overview of Faust and the plugin version.")
                .Player("faust help", ".faust help [topic]",
                    "The help menu. Topic is one of: players, castles, server, admin. With no topic it lists the topics."));
            s.P("Every intel query is a " + C(".faust api ...") + " command. Raphael's tabs send them for you and turn the replies into tables. " +
                "You can type them in chat, but the replies are raw machine lines (" + C("[FAUST:castle] ...") + "). The full list is in " +
                "<b>Admin guide</b> → <b>Machine API (Raphael uses these)</b>.");
            s.Callout(CalloutKind.Note,
                "Whether a query answers you depends on the server's settings for that feature: by default every feature is admin-only.");
        });

        // ------------------------------------------------------------------ 6. Admin guide
        b.Section(GuideSchema.Admin, false, s =>
        {
            s.P("<b>Getting admin.</b> Faust's admin commands need V Rising admin. Your SteamID must be in the server's adminlist; then " +
                "open the console (F1) and type <b>adminauth</b>. In Raphael, press <b>Re-check admin</b> on any greyed admin tab.");
            s.Callout(CalloutKind.OffByDefault,
                "Out of the box: all 13 features are <b>AdminOnly</b>, free, with no cooldown, and admins are exempt. Heat-map collection " +
                "(" + C("[Faust.Heatmap] Enabled") + ") and native map markers (" + C("[Faust.MapMarkers] Enabled") + ") are off. Sessions, " +
                "concurrency and kill tracking are on.");
            s.P("Feature keys for " + A("feature") + ": playerpositions, castleinfo, playerinfo, plotavailability, castleresources, stats, " +
                "allcastles, decaywatch, clans, heatmap, bosses, kills, worldscan. Block, unblock, schedule and status also accept <b>all</b>.");

            s.Detail("Runtime control: blocks, countdowns, schedules", d =>
            {
                d.Commands("Runtime control", c => c
                    .Admin("faust admin block", ".faust admin block <feature> [minutes]",
                        "Closes a feature (or all) now for everyone, admins included. With minutes it reopens by itself; 0 or none = until unblocked.")
                    .Admin("faust admin unblock", ".faust admin unblock <feature>",
                        "Clears a block or countdown on that key. unblock all clears only the all block, not per-feature blocks.")
                    .Admin("faust admin schedule", ".faust admin schedule <feature> <window>",
                        "Window is HH:MM-HH:MM in 24-hour server local time (for example 18:00-22:00), or clear to remove it. Outside the " +
                        "window queries reply code=schedule.")
                    .Admin("faust admin status", ".faust admin status [feature]",
                        "Shows the effective state (open, BLOCKED with minutes left, schedule) for one feature, or for all and every feature."));
            });

            s.Detail("Live config editor", d =>
            {
                d.P("Change any Faust setting in game. A change applies at once and is saved to " + C(Cfg) + "; no restart.");
                d.Commands("Config editor", c => c
                    .Admin("faust admin set", ".faust admin set <feature> <spec>",
                        "Sets one or more of a feature's settings. Spec is setting=value pairs joined by commas with <b>no spaces</b>, for " +
                        "example access=Players,cooldown=60. Each pair is applied on its own; a bad pair is listed as Rejected and the rest still apply.")
                    .Also("e.g. .faust admin set castleinfo access=Players,costitem=<prefabGuid>,costqty=5")
                    .Admin("faust admin get", ".faust admin get <feature> [setting]",
                        "Lists a feature's current settings, or one setting with its valid values.")
                    .Admin("faust admin setglobal", ".faust admin setglobal <spec>",
                        "Sets one or more global settings, same pair format, for example ratelimit=5,heatmapenabled=on.")
                    .Admin("faust admin getglobal", ".faust admin getglobal [setting]",
                        "Lists the global settings, or one with its valid values.")
                    .Admin("faust admin resetcfg", ".faust admin resetcfg <target> [setting]",
                        "Target is a feature key or global. Resets that one setting, or <b>every</b> setting of the target, to the default. " +
                        "Your custom values are lost; re-enter them to undo.")
                    .Destructive());
                d.Detail("Setting names for set (per feature)", e => e.Bullets(
                    "<b>access</b>: Off | AdminOnly | Players. (cfg Access)",
                    "<b>delivery</b>: ServerMediated | Free. (cfg Delivery)",
                    "<b>costitem</b> (also costguid, costitemguid): item PrefabGUID, 0 = free. (cfg CostItemGuid)",
                    "<b>costqty</b> (also costquantity): 0 or more. (cfg CostQuantity)",
                    "<b>cooldown</b> (also cooldownseconds, cd): seconds, 0 or more. (cfg CooldownSeconds)",
                    "<b>adminsexempt</b> (also exempt): on | off. (cfg AdminsExempt)",
                    "<b>availability</b> (also pvp): Always | PvEOnly | PvPOnly. (cfg Availability)",
                    "<b>window</b> (also windowseconds): seconds. (cfg WindowSeconds)",
                    "<b>period</b> (also periodseconds): seconds, 86400 = daily. (cfg PeriodSeconds)",
                    "<b>maxuses</b> (also maxusesperperiod, max): 0 = unlimited. (cfg MaxUsesPerPeriod)",
                    "<b>unlock</b>: None | FinalBoss | " + C("BossKill:<guid>") + " | AllBosses | AllQuests. (cfg Unlock)",
                    "<b>nearprefab</b> (also requirenearprefab, near): PrefabGUID, 0 = off. (cfg RequireNearPrefab)",
                    "<b>neardist</b> (also requireneardistance, dist): metres. (cfg RequireNearDistance)"));
                d.Detail("Setting names for setglobal", e =>
                {
                    e.Bullets(
                        "<b>enabled</b>: [Faust] Enabled.  <b>audit</b>: AuditQueries.  <b>verbose</b>: [Diagnostics] VerboseLogging.",
                        "<b>ratelimit</b>: RateLimitSeconds.  <b>ratelimitexempt</b>: RateLimitAdminsExempt.",
                        "<b>resetsteamids</b>: [Faust.Data] ResetSteamIds (text; empty, \"\" or - clears it).",
                        "<b>sessiontracking</b>, <b>concurrencysampling</b>, <b>maxconcurrencypoints</b>, <b>sessionretentiondays</b>, " +
                        "<b>datanamespace</b>: the [Faust.Collection] settings of the same names.",
                        "<b>heatmapenabled</b>, <b>heatmapsample</b> (30-300), <b>heatmapcellsize</b> (1 or more), <b>heatmapmaxcells</b>, " +
                        "<b>heatmapretentiondays</b>: [Faust.Heatmap].",
                        "<b>mapmarkersenabled</b>, <b>mapmarkerprefab</b>: [Faust.MapMarkers].",
                        "<b>worldscaninterval</b> (5 or more), <b>worldscanmaxresults</b>: [Faust.WorldScan].",
                        "<b>bossmaplimit</b> (100-20000): [Faust.Bosses] MapLimit.");
                    e.P("On / off values accept on, off, true, false, 1, 0, yes, no. Enum values ignore case. " +
                        "KillTracking is not in the editor: change it in the cfg file and restart.");
                    e.Callout(CalloutKind.Warning,
                        "Because commas separate pairs, setglobal can store only <b>one</b> SteamID in resetsteamids. For several, edit " +
                        C("ResetSteamIds") + " in the cfg file and restart.");
                });
            });

            s.Detail("Player unlocks and grants", d =>
            {
                d.Commands("Unlocks", c => c
                    .Admin("faust admin grant", ".faust admin grant <player> <feature>",
                        "Unlocks a feature for one player, overriding its Unlock requirement. It does not bypass the access level.")
                    .Admin("faust admin revoke", ".faust admin revoke <player> <feature>", "Removes that grant.")
                    .Admin("faust admin unlocks", ".faust admin unlocks <player>",
                        "Shows the player's recorded V Blood defeats and granted features."));
            });

            s.Detail("Collected data: status, prune, wipe", d =>
            {
                d.P("These touch only Faust's own records. The world, castles, characters, inventories, levels and in-game V Blood progress " +
                    "are never changed.");
                d.Commands("Data", c => c
                    .Admin("faust admin data status", ".faust admin data status",
                        "Footprint: namespace, size on disk, retention, record counts and oldest record, collection switches, unlocks, " +
                        "usage locks, kills, world-scan whitelist and heat-map cells.")
                    .Admin("faust admin data clear", ".faust admin data clear <days>",
                        "Deletes activity (sessions and concurrency samples) older than that many days. Playtime and charts shrink to " +
                        "the kept window. Cannot be undone.")
                    .Destructive()
                    .Admin("faust admin data wipe", ".faust admin data wipe <store> [confirm]",
                        "Store is activity, unlocks, usage, heatmap, kills or all. Without confirm it only previews what would be erased. " +
                        "With confirm it erases the store permanently.")
                    .Destructive());
                d.Bullets(
                    "<b>activity</b>: sessions, concurrency points, region samples and names (playtime and charts reset).",
                    "<b>unlocks</b>: every player's recorded V Blood defeats and grants, so progression gates start over.",
                    "<b>usage</b>: every cooldown and window lock, plus the usage tally behind Admin: Oversight.",
                    "<b>heatmap</b>: all heat-map cells.  <b>kills</b>: the kill and boss-defeat tallies.  <b>all</b>: every store above.");
                d.Callout(CalloutKind.Warning,
                    "Clear and wipe cannot be undone. On a fresh world most admins wipe <b>unlocks</b> and keep <b>activity</b>.");
                d.Callout(CalloutKind.AdminOnly,
                    "<b>ResetSteamIds lock.</b> If " + C("[Faust.Data] ResetSteamIds") + " lists SteamIDs, only those admins may run clear and " +
                    "wipe. Other admins keep every other command, including data status. Empty (default) = any admin.");
            });

            s.Detail("Native map markers (experimental)", d =>
            {
                d.Callout(CalloutKind.Experimental,
                    "This makes the server attach real map icons to players. A wrong icon prefab can crash the server in a way Faust cannot " +
                    "catch. Test on a test server first. It needs " + C("[Faust.MapMarkers] Enabled") + " = true.");
                d.Commands("Map markers", c => c
                    .Admin("faust admin showpositions", ".faust admin showpositions [mode] [minutes]",
                        "Mode is on, off or status (default status). On pins an icon on every online player, and on players who join while " +
                        "it is on; minutes sets an auto-off, checked when a player connects."));
                d.P("The default icon (" + C("MarkerPrefabGuid") + " -892362184, MapIcon_Player) is visible to allies, not admin-only.");
            });

            s.Detail("World scan whitelist and diagnostics", d =>
            {
                d.Commands("World scan and lookup", c => c
                    .Admin("faust admin worldscan", ".faust admin worldscan <mode> [arg]",
                        "Mode: list [page], add " + A("prefabGuid") + ", remove " + A("prefabGuid") + ", seed (refill from the catalog), clear (empties it; the " +
                        "world map then shows nothing until you add or seed).")
                    .Admin("faust admin worldscandiag", ".faust admin worldscandiag <fragment>",
                        "Shows how prefabs whose name contains the fragment are classified (unit or node, entity category).")
                    .Admin("faust admin prefab", ".faust admin prefab <query> [page]",
                        "Looks up a PrefabGUID by number, or lists prefabs whose name contains the text, 20 per page. Use it for costs, " +
                        "unlocks and proximity objects.")
                    .Admin("faust admin bossdiag", ".faust admin bossdiag [filter]",
                        "Dumps V Blood entity positions and state, for one boss by name or GUID or for all. Helps when a live boss reads Not spawned."));
            });

            s.Detail("Machine API (Raphael uses these)", d =>
            {
                d.P("Raphael's tabs send these silently and parse the " + C("[FAUST:...]") + " replies. Each is gated by the feature in " +
                    "brackets. " + A("page") + " pages a long list; " + A("days") + " 0 means all time where noted.");
                d.Commands("Machine API", c => c
                    .Player("faust api version", ".faust api version",
                        "Handshake: API version, plugin version, ready flag, and each feature's access and cost as seen by you. Not gated.")
                    .Player("faust api ping", ".faust api ping", "Round-trip probe. Not gated.")
                    .Player("faust api castleinfo", ".faust api castleinfo [token]",
                        "One territory. Token is here (default), nearest (nearest castle heart) or a territory index. (castleinfo)")
                    .Player("faust api plots", ".faust api plots [page]", "Free plots, largest first. (plotavailability)")
                    .Player("faust api castles", ".faust api castles [page]", "Every territory, claimed and open. (allcastles)")
                    .Player("faust api decay", ".faust api decay [page]", "Claimed castles by soonest decay. (decaywatch)")
                    .Player("faust api resources", ".faust api resources [token] [page]",
                        "Summed container contents and prisoners of a castle; token as for castleinfo. (castleresources)")
                    .Player("faust api pinfo", ".faust api pinfo <nameOrId>",
                        "One player's info by name or SteamID. Your own skips access and unlock. (playerinfo)")
                    .Player("faust api positions", ".faust api positions [page]", "Online players' positions and region. (playerpositions)")
                    .Player("faust api heatmap", ".faust api heatmap [target] [days] [page]",
                        "Density grid. Target all (default) or a name or SteamID; days 0 = all time. (heatmap)")
                    .Player("faust api stats", ".faust api stats <kind> [arg] [arg2]",
                        "Kind: playtime, concurrency, players, regions [page]; hours, sessions, weekdays [player]; daily, newplayers, peak " +
                        "[days]; pdaily " + A("player") + " [days]; regiondaily, activegrid [days] [page]; population, recency. (stats)")
                    .Player("faust api newplayers", ".faust api newplayers [sub] [days] [page]",
                        "Sub must be roster: who first joined in the window (default 30, 1-90 days). (stats)")
                    .Player("faust api sessions", ".faust api sessions [sub] [target] [days] [page]",
                        "Sub must be timeline: session intervals for all or one player (default 14, 1-90 days). (stats)")
                    .Player("faust api access", ".faust api access [page]", "Per-feature access, gates, price and grants. (stats)")
                    .Player("faust api usage", ".faust api usage [days] [page]",
                        "Per-feature uses, payers, items spent and cooldown denials (default 7 days). (stats)")
                    .Player("faust api clans", ".faust api clans [page]", "Clan composition and rosters. (clans)")
                    .Player("faust api clanmembers", ".faust api clanmembers <clanName> [page]",
                        "One clan's members. Spaces in clan names arrive as underscores; type it exactly. (clans)")
                    .Player("faust api bosses", ".faust api bosses [page]", "The V Blood status board. (bosses)")
                    .Player("faust api boss", ".faust api boss <nameOrGuid>", "One V Blood by name or PrefabGUID. (bosses)")
                    .Player("faust api kills", ".faust api kills [days] [page]", "Top killers; days 0 = all time. (kills)")
                    .Player("faust api bosskills", ".faust api bosskills [days] [page]", "V Blood defeat counts; days 0 = all time. (kills)")
                    .Player("faust api worldscan", ".faust api worldscan [spec] [page]",
                        "Filtered units and resource nodes; spec as in How it works → World scan, default all. (worldscan)"));
            });
        });

        // ------------------------------------------------------------------ 7. Server configuration
        b.Section(GuideSchema.Config, false, s =>
        {
            s.P("Faust's settings live in <b>" + Cfg + "</b> on the server. Its data files live in " + C("BepInEx/config/Faust/") + ".");
            s.P("<b>live</b> = changes made in game with " + C(".faust admin setglobal") + " or " + C(".faust admin set") + " apply at once and are " +
                "saved to the file. A hand edit of the cfg file always needs a server restart.");

            s.Settings("Master switch and anti-spam", Cfg, t => t
                .Row("Faust", "Enabled", "true",
                    "Master switch. False refuses every query with code=disabled, for admins too. (in game: enabled)", "live")
                .Row("Faust", "AuditQueries", "true",
                    "Logs who ran which query and whether they were charged, in the server log. (audit)", "live")
                .Row("Faust", "RateLimitSeconds", "0",
                    "Minimum seconds a player must wait between any two Faust queries, across all features. 0 = off. Also throttles " +
                    "paged reads, so keep admins exempt. Refusals reply code=ratelimit. (ratelimit)", "live")
                .Row("Faust", "RateLimitAdminsExempt", "true",
                    "Admins skip RateLimitSeconds, so Raphael's admin tabs are not throttled. (ratelimitexempt)", "live")
                .Row("Faust.Data", "ResetSteamIds", "(empty)",
                    "Comma-separated SteamIDs of the admins allowed to run data clear and data wipe. Empty = any admin. (resetsteamids; " +
                    "setglobal can store only one ID)", "live")
                .Row("Diagnostics", "VerboseLogging", "false",
                    "Detailed per-query log lines. Useful when testing, noisy in production. (verbose)", "live"));

            s.Detail("[Faust.Collection]: background history", d => d.Settings("Collection", Cfg, t => t
                .Row("Faust.Collection", "SessionTracking", "true",
                    "Records connects and disconnects. Off: playtime, sessions, frequency, busiest hour and the playtime leaderboard " +
                    "report -1 (not tracked) and nothing is written. (sessiontracking)", "live")
                .Row("Faust.Collection", "ConcurrencySampling", "true",
                    "Records the online count at each connect and disconnect, for population graphs. Independent of SessionTracking. " +
                    "(concurrencysampling)", "live")
                .Row("Faust.Collection", "KillTracking", "true",
                    "Tallies kills and V Blood defeats per UTC day for the Leaderboards tab. Off: nothing recorded and the leaderboards " +
                    "are empty. Not in the in-game editor.", "restart")
                .Row("Faust.Collection", "MaxConcurrencyPoints", "4000",
                    "Most concurrency samples kept; the oldest are trimmed. 0 disables sampling. (maxconcurrencypoints)", "live")
                .Row("Faust.Collection", "SessionRetentionDays", "0",
                    "Deletes session records older than this many days, checked at load and on each connect. 0 = keep forever. Also " +
                    "bounds the kill tally. For a one-off trim use data clear. (sessionretentiondays)", "live")
                .Row("Faust.Collection", "DataNamespace", "(empty)",
                    "Stores data in " + C("BepInEx/config/Faust/<name>/") + " instead of the shared folder. Empty = one dataset that " +
                    "survives world wipes. A new name starts a fresh dataset; the old one stays on disk. Set it before first start, or " +
                    "restart after changing it. (datanamespace)", "restart")));

            s.Detail("[Faust.Heatmap]: position heat map", d => d.Settings("Heat map", Cfg, t => t
                .Row("Faust.Heatmap", "Enabled", "false",
                    "Turns on heat-map collection: every online player's position is sampled on a timer. The only timer-driven collector, " +
                    "so it is opt-in. (heatmapenabled)", "live")
                .Row("Faust.Heatmap", "SampleSeconds", "60",
                    "Seconds between samples, clamped to 30-300. Lower = finer timing, more disk. (heatmapsample)", "live")
                .Row("Faust.Heatmap", "CellSize", "25",
                    "Grid cell size in world units. Smaller = sharper, heavier. Fixed once data exists: wipe the heat map before changing " +
                    "it. (heatmapcellsize)", "live")
                .Row("Faust.Heatmap", "MaxCells", "250000",
                    "Most (player, cell) entries kept, for the all-time and per-day grids each. When full, existing cells keep counting " +
                    "but no new ones are added. 0 = unlimited (not advised). (heatmapmaxcells)", "live")
                .Row("Faust.Heatmap", "RetentionDays", "30",
                    "UTC days of per-day history kept for the windowed views. The all-time map is never pruned. 0 = keep all days. " +
                    "(heatmapretentiondays)", "live")));

            s.Detail("[Faust.WorldScan], [Faust.Bosses], [Faust.MapMarkers]", d => d.Settings("Scans and markers", Cfg, t => t
                .Row("Faust.WorldScan", "ScanIntervalSeconds", "10",
                    "How long a world-scan snapshot is reused before the next query rescans, minimum 5. Raise it on a busy server. " +
                    "(worldscaninterval)", "live")
                .Row("Faust.WorldScan", "MaxResults", "10000",
                    "Most assets one world-scan query returns after filtering; a capped reply says truncated. 0 = unlimited, which also " +
                    "lifts the internal snapshot cap. (worldscanmaxresults)", "live")
                .Row("Faust.Bosses", "MapLimit", "9000",
                    "World units on both axes within which a placed V Blood counts as Live. Raise it only if a live boss reads Not spawned, " +
                    "and keep it under about 10000. (bossmaplimit, 100-20000)", "live")
                .Row("Faust.MapMarkers", "Enabled", "false",
                    "Experimental. Allows .faust admin showpositions to attach native map icons to players. Can crash the server; test " +
                    "first. (mapmarkersenabled)", "live")
                .Row("Faust.MapMarkers", "MarkerPrefabGuid", "-892362184",
                    "The map-icon prefab attached per player (default MapIcon_Player, visible to allies). Ignored unless Enabled. " +
                    "(mapmarkerprefab)", "live")));

            s.Detail(C("[Faust.<feature>]") + ": the per-feature block", d =>
            {
                d.P("Faust writes one block per feature, named " + C("[Faust.<feature>]") + " with the lowercase key, for example " +
                    C("[Faust.castleinfo]") + ". Every block has the same settings and the same defaults:");
                d.Bullets(
                    "playerpositions, castleinfo, playerinfo, plotavailability, castleresources, stats, allcastles, decaywatch, clans, " +
                    "heatmap, bosses, kills, worldscan: all default to Access <b>AdminOnly</b> and Delivery <b>ServerMediated</b>.");
                d.Settings("Per-feature settings", Cfg, t => t
                    .Row("Faust.<Feature>", "Access", "AdminOnly",
                        "Off | AdminOnly | Players. Who may run this feature's queries. Off blocks admins too. (set: access)", "live")
                    .Row("Faust.<Feature>", "Delivery", "ServerMediated",
                        "ServerMediated | Free. Stored but not acted on in Faust 0.16.5; every query is server-gated. (delivery)", "live")
                    .Row("Faust.<Feature>", "CostItemGuid", "0",
                        "Item PrefabGUID charged per query. 0 = free. (costitem)", "live")
                    .Row("Faust.<Feature>", "CostQuantity", "0",
                        "How many of that item per query. Ignored when CostItemGuid is 0. (costqty)", "live")
                    .Row("Faust.<Feature>", "CooldownSeconds", "0",
                        "Per-player lockout after each successful use. 0 = none. (cooldown)", "live")
                    .Row("Faust.<Feature>", "AdminsExempt", "true",
                        "Admins skip this feature's unlock, access, PvP, proximity, cooldown, window and cost checks. (adminsexempt)", "live")
                    .Row("Faust.<Feature>", "Availability", "Always",
                        "Always | PvEOnly | PvPOnly, against the server's game mode. (availability)", "live")
                    .Row("Faust.<Feature>", "WindowSeconds", "0",
                        "Once used, the feature stays open this long and repeat uses are free. 0 = no window. (window)", "live")
                    .Row("Faust.<Feature>", "PeriodSeconds", "0",
                        "Allowance cycle in seconds, for example 86400 = daily. 0 = no period. (period)", "live")
                    .Row("Faust.<Feature>", "MaxUsesPerPeriod", "0",
                        "Uses or window openings per period; then locked until the period rolls over. 0 = unlimited. (maxuses)", "live")
                    .Row("Faust.<Feature>", "Unlock", "None",
                        "None | FinalBoss | " + C("BossKill:<vbloodGuid>") + " | AllBosses | AllQuests (the last two are grant-only for now). " +
                        "(unlock)", "live")
                    .Row("Faust.<Feature>", "RequireNearPrefab", "0",
                        "PrefabGUID of a placed or world object the player must stand near. 0 = no requirement. (nearprefab)", "live")
                    .Row("Faust.<Feature>", "RequireNearDistance", "5",
                        "Metres from that object. Ignored when RequireNearPrefab is 0. (neardist)", "live"));
            });
        });

        // ------------------------------------------------------------------ 8. Troubleshooting
        b.Section(GuideSchema.Trouble, false, s =>
        {
            s.Detail("The Faust tabs are greyed or missing", d => d.Bullets(
                "Raphael shows the group only after " + C(".faust api version") + " gets a reply. Press <b>Re-check now</b> in the Faust rail, " +
                "for example after switching servers.",
                "If the server has no Faust, the rail says <b>Faust not detected</b>. <b>Force-enable tabs</b> shows them for this session, " +
                "but they can only load data from a real Faust.",
                "Faust → <b>Settings</b> → <b>Faust tab group</b> sets Auto / On / Off; FaustAvailability in kdpen.Raphael.cfg keeps it."));

            s.Detail("The admin tabs are greyed", d => d.P(
                "You are not authed as admin. Run <b>adminauth</b> in the F1 console, then press <b>Re-check admin</b> on the tab."));

            s.Detail("\"This feature is turned off on this server\" (code=disabled)", d => d.P(
                "The master switch is off or the feature's access is Off. Admin fix: " + C(".faust admin setglobal enabled=on") + " or " +
                C(".faust admin set <feature> access=AdminOnly") + "."));

            s.Detail("\"Access denied - this query is admin-only\" (code=noaccess)", d => d.P(
                "The feature is AdminOnly, the default for all features. Admin fix: " + C(".faust admin set <feature> access=Players") +
                ". A grant does not open an AdminOnly feature."));

            s.Detail("Rate limit, cooldown or allowance spent", d => d.Bullets(
                "<b>code=ratelimit</b>: you queried again within RateLimitSeconds. Wait the seconds shown.",
                "<b>code=cooldown</b>: this feature's cooldown is running. <b>code=window</b>: the period's uses are spent; it resets when " +
                "the period rolls over.",
                "Admin: " + C(".faust admin data wipe usage confirm") + " clears every player's locks."));

            s.Detail("Blocked or outside its hours", d => d.P(
                "<b>code=blocked</b> or <b>code=schedule</b> come from an admin block or schedule and apply to admins too. Check with " +
                C(".faust admin status") + ", then " + C(".faust admin unblock <feature>") + " or " + C(".faust admin schedule <feature> clear") +
                ". Unblocking all does not clear a block set on one feature."));

            s.Detail("Locked, cost, PvP or proximity refusals", d => d.Bullets(
                "<b>code=locked</b>: defeat the required V Blood or Dracula, or ask an admin to " + C(".faust admin grant <player> <feature>") + ".",
                "<b>code=cost</b>: carry the item and quantity shown. You are charged only for a real answer.",
                "<b>code=pvp</b>: the feature is set PvEOnly or PvPOnly and this server is the other mode.",
                "<b>code=notnear</b>: stand within the distance shown of the required object."));

            s.Detail("No reply, or \"Faust is still initializing\"", d => d.Bullets(
                "<b>code=notready</b>: the server is still loading. Try again shortly.",
                "A tab times out with no reply: the server's Faust may be older than the endpoint (the tab says which version it needs). " +
                "Check the plugin version in Faust → Settings → <b>Connection & state</b>.",
                "Turn on Faust → Settings → <b>Diagnostics</b> and read LogOutput.log for the [Faust] trace."));

            s.Detail("Empty heat map, leaderboards or stats", d => d.Bullets(
                "<b>Heat map</b>: collection is off by default. Admin: " + C(".faust admin setglobal heatmapenabled=on") + ", then wait for samples.",
                "<b>Leaderboards</b>: need KillTracking on, and count only kills since it was on.",
                "<b>Playtime shows -1</b>: SessionTracking is off. History starts when Faust was installed."));

            s.Detail("World map or boss board looks wrong", d => d.Bullets(
                "<b>No resource nodes</b>: they are not whitelisted. Admin: <b>Seed defaults</b> on Admin: Control, or " +
                C(".faust admin worldscan seed") + ".",
                "<b>Results cut short</b>: the reply was truncated at MaxResults. Narrow the filter or raise worldscanmaxresults.",
                "<b>A live boss reads Not spawned</b>: raise bossmaplimit a little (under about 10000) or run " +
                C(".faust admin bossdiag <name>") + "."));

            s.Detail("Admin commands refused or rejected", d => d.Bullets(
                "<b>\"Rejected: ... use setting=value\"</b>: set and setglobal take pairs with no spaces, comma-separated: " +
                C(".faust admin set stats access=Players,cooldown=30") + ".",
                "<b>\"not authorized to clear/reset Faust data\"</b>: [Faust.Data] ResetSteamIds limits clear and wipe to listed SteamIDs.",
                "<b>Show on map says disabled</b>: " + C("[Faust.MapMarkers] Enabled") + " is false. Read the experimental warning first.",
                "<b>Clan members not found</b>: type the clan name exactly, with underscores for spaces, or click it in the roster."));
        });

        b.DefaultsNote();
        list.Add(b.Build());
    }
}
