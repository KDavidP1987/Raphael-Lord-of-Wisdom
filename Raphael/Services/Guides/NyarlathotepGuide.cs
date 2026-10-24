using System.Collections.Generic;

namespace Raphael.Services.Guides;

// 0.73.0: the Nyarlathotep guide, checked against the released Nyarlathotep 0.14.0 source (Commands/*.cs,
// Config/Settings.cs, Logic/Limits.cs, Logic/CommandArgs.cs, Logic validation and the 0.14.0 README: undead hordes).
// 0.74.0: checked against 0.15.0 (horde pools: pool ranges, the pool library, castle entry for the horde families, the
// new MaxTrackedUnits default), with the guide-commands fixtures regenerated from the 0.15.0 source.
internal static partial class Guides
{
    static partial void AddNyarlathotep(List<Guide> list)
    {
        static string A(string name) => GuideText.Arg(name);
        static string C(string text) => GuideText.Cmd(text);

        const string Cfg = "BepInEx/config/kdpen.Nyarlathotep.cfg";
        const string EvVerbs = "verbs from EventCommands.cs switch (VCF passes verb, id, field, value as optional strings)";

        var b = new GuideBuilder("nyarlathotep", "Nyarlathotep guide",
            "Server events for V Rising: empowered factions, spawn waves, revenge squads and undead hordes, for players and admins.",
            "Nyarlathotep 0.15.0", GuideKind.FullMod, "nyarlathotep");

        // ------------------------------------------------------------------------------------------ 1. what
        b.Section(GuideSchema.What, true, s => s
            .Bullets(
                "Nyarlathotep runs <b>world events</b>. A trigger starts an event, the event runs for a set time, and everything it spawned is removed after it ends.",
                "Events can <b>empower a faction</b> (stronger NPCs for a while), send <b>waves of enemies</b> at a spot, a region or a player, start a <b>revenge</b> hunt on one player, or unleash <b>undead hordes</b>: an apocalypse, an infestation that spreads with every kill, or a horde march on a destination.",
                "It is a <b>server-side</b> mod. Raphael is the client panel: live event board, notifications, and the admin tools.",
                "The server's admin decides everything. Every event type starts <b>off</b>, and no event runs until an admin enables it.",
                "Players need no commands. Fight what appears. Type " + C(".nyar status") + " to see what is running."));

        // ------------------------------------------------------------------------------------ 2. quickstart
        b.Section(GuideSchema.QuickStart, true, s => s
            .Steps(
                "Open Raphael's panel and pick the <b>Nyarlathotep</b> group. If it is greyed, see Troubleshooting.",
                "Open <b>Live Events</b> and press <b>Refresh</b>. It lists the running events, their time left and wave countdowns. The <b>Regions</b> card shows which map regions have an event.",
                "Open <b>Settings</b> (in the Nyarlathotep group) and choose your on-screen notices: <b>Event begins / ends</b>, <b>Wave warnings</b>, <b>Wave arrived</b>, <b>Kill switch</b> and <b>Login summary</b>. Press <b>Test</b> to see one.",
                "In chat, type " + C(".nyar status") + " at any time. It lists each running event and its minutes left, or \"No active events.\"",
                "When an event starts, its enemies appear near players or in its regions. Many hunt the nearest player. Kill them. Some events end early when every wave is defeated.",
                "When an event ends, its leftover enemies disappear after 30 seconds by default. A scoreboard line in chat may name the top players.")
            .Callout(CalloutKind.Note, "Admins: read the <b>Admin guide</b> section. Nothing runs until you switch a pillar on and enable an event."));

        // -------------------------------------------------------------------------------------- 3. concepts
        b.Section(GuideSchema.Concepts, false, s => s
            .Detail("Events, triggers and pillars", d => d
                .P("Every feature is an <b>event</b>. An event has a trigger (what starts it), optional conditions (what can hold it back), an action (what it does), a duration, and optional announcement text.")
                .P("Each event belongs to one <b>pillar</b>: empowerment, spawns, boss, zones or sieges. An event runs only while its pillar is switched on, and while the master switch [General] Enabled is true.")
                .Bullets(
                    "<b>Empowerment</b>: buffs every NPC of chosen factions for the event's time.",
                    "<b>Spawns</b>: spawn waves, revenge squads and the undead hordes (apocalypse, infestation, horde march).",
                    "<b>Boss</b>, <b>zones</b> and <b>sieges</b>: switches exist, but these pillars have no event types yet. See the Planned callouts in the Admin guide.")
                .P("At most 3 events run at once by default ([Limits] MaxConcurrentEvents). A start past that cap is skipped.")
                .P("Event definitions live in BepInEx/config/Nyarlathotep/events.json on the server. Admins edit them in chat, in Raphael, or in the file."))
            .Detail("Triggers: what starts an event", d => d
                .Bullets(
                    "<b>Manual</b>: only an admin's " + C(".nyar event start <id>") + ".",
                    "<b>Schedule</b>: chosen days (Sun to Sat) and times (HH:mm), server-local time.",
                    "<b>GameTime</b>: at nightfall or at daybreak.",
                    "<b>VBloodKilled</b>: when any V Blood dies, or one of up to 20 named ones.",
                    "<b>Interval</b>: again a random 5-1440 minutes after its last run ended (minMinutes to maxMinutes). The next start survives a restart; downtime is not replayed.",
                    "<b>RegionEntered</b>: when a player walks into one of its regions. The same player can start it again after playerCooldownMinutes (default 30).",
                    "<b>FactionKills</b>: when one player kills a number of NPCs (3-500) of up to 5 factions within a window (10-3600 s). With shared on, all players' kills count together.",
                    "<b>Revenge</b>: a faction hunts one player who killed too many of its NPCs. See Revenge below.")
                .P("An event started by a player's action (region, kills, revenge) is aimed at that player: its waves around a player spawn near them first."))
            .Detail("Conditions", d => d
                .P("Conditions hold back <b>automatic</b> starts only. An admin's start ignores them.")
                .Bullets(
                    "minPlayers: players online, 0-100.",
                    "cooldownMinutes: minutes after the last run, 0-10080.",
                    "chancePercent: the chance it starts when triggered, 1-100.",
                    "window: a from-to time of day (HH:mm). It may cross midnight; from equal to to means all day.",
                    "mode: any, pve or pvp server."))
            .Detail("Faction empowerment", d => d
                .P("Every NPC of up to 5 factions gets a buff for the event's time: a Blood Moon for the NPCs. Physical power, spell power, max health, attack speed and move speed each take a multiplier from 1.0 to 3.0.")
                .Bullets(
                    "The buff is a timed effect. It expires on its own, even if the mod is removed mid-event.",
                    "NPCs that respawn or load in during the event are buffed within about 15 seconds.",
                    "Players' servants, traders, Bloodcraft familiars and the mod's own units are never buffed. V Bloods only when includeVBloods is true.",
                    "One empowerment per faction runs at a time.",
                    "Player, trader, critter, prisoner and ignored factions cannot be empowered."))
            .Detail("Spawn waves", d => d
                .P("Waves of chosen units appear at a spot: up to 10 unit types, up to 10 waves, a set interval between waves.")
                .Bullets(
                    "<b>Location</b>: a Point on the map, around the admin (Admin, manual starts only), around a random player (AroundPlayer, 10-60 m minimum, 15-80 m maximum), or spread over the event's regions (Region).",
                    "Units land on walkable ground. A spawn point in water or against a wall moves to the nearest walkable spot.",
                    "<b>No wave spawns in claimed castle territory</b> unless the event sets allowTerritory. Since 0.15.0 an apocalypse, infestation or horde march takes allowTerritory too: off, nothing rises, marches or hunts on castle land and a start there is refused; on, castles are ground like any other.",
                    "A player in castle territory or in PvP combat is never picked by AroundPlayer and never hunted.",
                    "<b>Modifiers</b>: a fixed level (1-120) or a level shift (-5 to +5), and multipliers for max health, power, move speed and attack speed (0.5-3.0).",
                    "<b>Hunt</b>: every 5 seconds, each unit is set on the nearest players (up to 5) within 10-60 m.",
                    "<b>Loot</b>: units drop loot only when the event sets it.",
                    "<b>Chance</b>: each copy of a unit joins its wave with a chance from 0.05 to 1.0.",
                    "<b>Fan-out</b>: one group near each of up to 2-10 players standing 50-500 m apart. All groups together still obey the caps.",
                    "A warned wave can still be skipped (no eligible player, a castle at the centre, or every chance rolled no unit).")
                .P("After the event ends, its units stay for [Limits] GraceSeconds (30 s), then are removed a few at a time. Each unit also carries a timer, so none is left behind, even across a restart."))
            .Detail("Wave lists, swarms and the scoreboard", d => d
                .Bullets(
                    "<b>Wave list</b>: up to 10 waves, each with its own units. A later wave starts afterSeconds (10-3600) after the one before, or when the one before is cleared (whenCleared), whichever comes first. A wave with neither waits for a clear.",
                    "When the last wave is cleared and every wave was fought, the event ends at once (\"all waves defeated\").",
                    "<b>Scoreboard</b>: counts each kill of the event's units by a player (a familiar's or summon's kill counts for its player) and each player they kill. At the natural end, after all waves are defeated, or on " + C(".nyar event stop") + ", chat shows the top 3 by kills (ties go to fewer deaths) and the totals. A purge, a restart, a fault or a pillar switched off shows nothing.",
                    "Admins are left off the scoreboard unless [Scoreboard] IncludeAdmins is true. Nothing is saved.",
                    "<b>Arena</b> (15-60 m): every 5 seconds, units more than 10 m past the edge are brought back near the centre.",
                    "<b>Growth</b>: wave n adds (n-1) times addUnits (0-10) to each count and (n-1) times addLevels (0-5) to each level.",
                    "<b>Winner</b>: after the scoreboard, one line names the player with the best kills per death (a player needs one kill)."))
            .Detail("Regions and hunting grounds", d => d
                .P("Any event can be limited to named map regions: StartCave, FarbaneWoods, DunleyFarmlands, CursedForest, HallowedMountains, SilverlightHills, Gloomrot_South, Gloomrot_North, RuinsOfMortium and Strongblade. Global (the default) means the whole map.")
                .Bullets(
                    "<b>Trigger scope</b>: a V Blood trigger fires only for a kill inside the regions. A scheduled, nightfall or manual start needs a player standing in one of them; a scheduled one is skipped, not retried.",
                    "<b>Action scope</b>: empowerment buffs only NPCs in the regions. Waves spawn only there, and an admin start from outside is refused.",
                    "<b>Region lock</b>: every 5 seconds, a unit that left the regions is brought back.",
                    "<b>Region hunt</b> (needs the lock): each group goes after the nearest player inside the regions. A unit far away closes in with hidden jumps, only while no player is within 100 m to see it.",
                    "If the map cannot be read, regional events are disabled and the rest run."))
            .Detail("Revenge", d => d
                .P("A spawn event with a Revenge trigger lets a faction take revenge on one player.")
                .Bullets(
                    "Each kill of the trigger's factions within windowSeconds (60-3600) adds 1 heat to the killer. A V Blood kill adds vbloodHeat (default 10). Kills of event units add none.",
                    "At each warnAt step (up to 3) the player gets a private warning.",
                    "At heat (3-500) the revenge starts once the player is online, alive, out of PvP combat and outside castle territory.",
                    "A server alert names no one. The player gets a private line. The waves spawn around that player and hunt only that player. Killing every wave ends it with a private end line.",
                    "Then the player's cooldown runs (playerCooldownMinutes, default 30).",
                    "Heat lives in memory only. A restart or a purge forgets it.",
                    "The event must use an AroundPlayer location and Hunt, with no fan-out and no castle territory."))
            .Detail("Bosses, pools and pursuit", d => d
                .Bullets(
                    "<b>Bosses</b>: up to 3 boss entries (1-3 each) join the last wave. They must die for \"all waves defeated\". Players feed on their V Blood as usual.",
                    "Any wave can also bring its own bosses, fixed or rolled from a pool: at most 5 per wave.",
                    "A V Blood spawned by an event or by " + C(".nyar spawn") + " never starts a V Blood event.",
                    "<b>Pools</b>: 1-8 named lists of 1-30 units. A unit entry like pool:troops:9 draws 9 at random. Each wave is rolled when it comes.",
                    "Since 0.15.0 a spawn-waves pool entry can take a range, like pool:troops:5-20 (both 1-500): each wave draws a random count between the two. A wave's total counts each range at its max. In an apocalypse or infestation a pool entry's count is a weight, so a range is refused there.",
                    "A pool name that isn't one of the event's own pools is looked up in the server's pool library (see Pool library).",
                    "<b>Pursuit</b> (15-600 s, needs Hunt): once no player has stood in the arena or hunt range that long, the units chase the nearest player anywhere until the event ends. A castle and PvP combat stay a refuge."))
            .Detail("The apocalypse", d => d
                .P("An Apocalypse action (spawns pillar) turns one faction into an outbreak that lasts 10 minutes to 7 days (durationSeconds 600-604800).")
                .Bullets(
                    "<b>Hotspots</b> (1-5) are found at the start, where enough of the faction's own NPCs stand together.",
                    "<b>How many rise</b>: at the start, roamers.perHotspot (1-10) roamers rise at each hotspot; both numbers are set in events.json only. After that, kills add roamers up to roamers.max, and each horde brings hordes.size, the later ones more by hordes.growth (events.json). The event editor's How many rise card shows the event's own numbers.",
                    "<b>Roamers</b> hunt the nearest player anywhere on the map, with hidden jumps, never into a castle. Each kill a roamer or horde makes adds more roamers where the victim fell, up to a limit.",
                    "<b>Hordes</b> (1-10) rise at the hotspots on a timer and march on the <b>goal</b>. The last horde brings the bosses.",
                    "A horde stops and fights while a player stands within 60 m of its front, except close to the goal.",
                    "A horde reaching the goal loses the event (\"the goal has fallen\"). Destroying every horde wins it. Running out the clock survives it. Then the scoreboard.",
                    "Chat announces each horde rising (with its region), each quarter of its march, and each horde destroyed. " + C(".nyar status") + " shows the horde, the march and the roamers.",
                    "It is the only event that carries on after a restart. Progress is saved in state.json every minute (30 s while a horde marches).",
                    "Since 0.15.0 a horde is held only by [Limits] MaxTrackedUnits (default 500); MaxUnitsPerWave applies to spawn waves only. Before 0.15.0 a horde never passed MaxUnitsPerWave (20). A start the cfg trims says \"capped\"."))
            .Detail("Infestation", d => d
                .P("An Infestation action (spawns pillar, 10 minutes to 7 days) raises the dead where a faction's own NPCs gather, and the outbreak grows with every kill.")
                .Bullets(
                    "<b>Origins</b> (1-10) are found at the start, each where at least minNatives (1-20) of the faction stand, at least minSpacing (100-2000 m) apart. Each origin raises perOrigin (1-100) units, so it starts with origins × perOrigin; everything after that comes from the spread.",
                    "<b>Hunters</b> (0-100 %) is the share of units that hunt the nearest player. They move in straight lines, and a castle territory is a refuge unless the event sets allowTerritory. The rest drift within driftRadius (0-2000 m) of their origin.",
                    "<b>Spread</b>: each kill the dead make raises perKillMin to perKillMax (0-3) new units where the victim fell, drawn from a pool or from the event's units, up to max alive (1-500), at most perMinute (1-30) a minute.",
                    "Won when every unit is dead with no spread pending; survived when the time runs out.",
                    "Kills, spread and growth happen only within 80 m of a player: the game stops its own NPCs farther from everyone."))
            .Detail("Horde march", d => d
                .P("A HordeMarch action (spawns pillar, 10 minutes to 7 days) sends one horde from a start to a destination.")
                .Bullets(
                    "<b>Start</b>: a point (where the admin stood), the starting admin, or the natives' hotspot nearest the destination at least minDistance (100-3000 m) away.",
                    "<b>Destination</b>: a point with a name (1-40 characters) and a radius (10-100 m). The horde marches at marchSpeed (0.5-5.0 m/s).",
                    "The horde is the sum of its units, 10-500: with pool ranges, the mins must add up to at least 10 and the maxes to at most 500. <b>Growth</b> on the way works like spread: kills add units up to growth.max. A growth.max at or below the horde size never grows it.",
                    "<b>The starting size</b> is set only in events.json, by the counts of the event's units (then Reload from disk). No chat command changes it, so Raphael's editor shows it but can't set it.",
                    "A horde players have watched for 30 s stops and fights where it stands.",
                    "<b>On arrival</b> (or where it stands after 120 s stuck, or once watched) it infests there, with its own hunters, drift radius and spread.",
                    "Bosses join the horde, fixed or rolled from a pool. Won when the horde is destroyed."))
            .Detail("Destination waves", d => d
                .Bullets(
                    "A spawn-waves event can march on a destination too: action.destination here, then a radius (10-100) and a name. The event stays disabled until both are set.",
                    "Each wave marches as one front and takes its behaviour on arrival. A wave players have watched for 30 s fights where it stands.",
                    "It needs a Point or Admin location, and can't be combined with fan-out, region lock, an arena or pursuit.",
                    "Only one apocalypse-type event (apocalypse, infestation, horde march) runs at a time."))
            .Detail("Pool library", d => d
                .P("Since 0.15.0 the server keeps a pool library in pools.json: named pools any event can draw from without defining them. Some come seeded with the mod.")
                .Bullets(
                    "An event's pool entries may name a library pool, like pool:undead-units:6. An event's own pool of the same name wins.",
                    "The server checks every name when it loads the events. An event that names a pool that exists nowhere is held disabled with the reason.",
                    "pools.json is edited on the server only. Raphael's Admin: Events tab has a Pools library card with the same three commands.")
                .Commands("Pool library commands", c => c
                    .Admin("nyar pool", ".nyar pool list [page]",
                        "The library's pools, ten a page: each pool's name, how many units it holds, and whether it came seeded with the mod.").Override("verbs from PoolCommands.cs switch (verb, a, b are optional strings)")
                    .Admin("nyar pool", ".nyar pool info <pool> [page]",
                        "The units in one library pool, with their counts.").Override("verbs from PoolCommands.cs switch (verb, a, b are optional strings)")
                    .Admin("nyar pool", ".nyar pool reload",
                        "Re-reads pools.json. The reply names any event that has become invalid.").Override("verbs from PoolCommands.cs switch (verb, a, b are optional strings)")))
            .Detail("Unit watch: stuck units", d => d
                .P("On by default for every event unit (never a " + C(".nyar spawn") + " unit). A wave never hangs on one unit nobody can reach.")
                .Bullets(
                    "A unit is stuck when, without fighting, it stood within 5 m for StuckSeconds (90) while trying to reach a player; or it is more than 10 m under the ground; or it has not moved SpawnStillSeconds (10) after being spawned, while targeting a player.",
                    "A stuck unit is moved, only while no player is within 100 m, to walkable ground at least 50 m from every player and outside castles.",
                    "After two failed tries it is removed. It counts as gone for its wave, so the next wave comes, but it gives no kill credit.",
                    "A wave the event waits on with no death and no fight for StallMinutes (10) sends its leftovers through the same tries.",
                    "Admins can force it at once with " + C(".nyar event unstick <id>") + " or the <b>Unstick</b> button."))
            .Detail("Announcements and notifications", d => d
                .Bullets(
                    "<b>Login notice</b> (on by default): a player who logs in while an event runs gets a private line 10 seconds later, such as \"Events in progress: Bandit raid: 5 min left. Type .nyar status for details.\"",
                    "<b>Wave warnings</b> (off by default): server-wide lines before each wave, 300, 60 and 10 seconds ahead by default. Only for events whose announce.warnings is true.",
                    "<b>Event banners</b> (off by default): server-wide lines when an event starts and ends, with the event's own text or a stock line.",
                    "<b>Daily banner</b> (off by default): once a day at 20:00, naming the scheduled events still due that day.",
                    "Admins can broadcast any line with " + C(".nyar announce <text>") + ".",
                    "Raphael shows short on-screen notices copied from the server's live updates. Choose them in the Nyarlathotep <b>Settings</b> tab. Scoreboards, revenge warnings and admin broadcasts show in chat only.")));

        // ------------------------------------------------------------------------------------------ 4. tabs
        b.Section(GuideSchema.Tabs, false, s => s
            .P("The Nyarlathotep tab group appears when the server answers Raphael's handshake. Admin tabs are greyed until the server says you are an admin.")
            .Tabs(t => t
                .Row("Live Events", "Running events, time left, wave countdowns and the Regions card. Updates live. <b>Refresh</b> re-reads.", ".nyar api status, .nyar api regions")
                .Row("Leaderboards", "Planned. Shows a note until the server has player stats.", null)
                .Row("My Stats", "Planned. Shows a note until the server has player stats.", null)
                .Row("Settings", "Notification toggles, notice length and <b>Test</b>; the <b>Enable diagnostic details</b> toggle; a read-only <b>Server switches</b> readout.", null)
                .Row("Admin: Events", "Every definition with its state. Per event: <b>Start</b>, <b>Stop</b>, <b>Unstick</b>, <b>Enable</b>, <b>Disable</b>, <b>Edit…</b> (a form editor), <b>Info</b>, <b>Copy…</b> and <b>Delete</b> (press twice). <b>Refresh list</b>, <b>Reload from disk</b>, and <b>New</b> / <b>Copy</b> boxes.", ".nyar api events, .nyar api event start|stop|unstick|enable|disable|set|reload|new|copy|delete, .nyar event info")
                .Row("Admin: Units", "<b>Scan</b> or <b>Scan 100 m</b> lists the mod's units near you. <b>Refresh</b> shows units per running event. <b>Test spawn</b> spawns units beside you.", ".nyar debug here [radius], .nyar api status, .nyar spawn")
                .Row("Admin: Templates", "The built-in templates, filtered by pillar. <b>Create</b> copies one into events.json, disabled, under an optional new id. <b>Turn Spawns on</b> appears when the Spawns pillar is off.", ".nyar api templates, .nyar api template use")
                .Row("Admin: Pillars", "Each pillar's switch. <b>Switch on</b>, <b>Switch off</b> (asks to confirm; ends that pillar's events), <b>Refresh</b>.", ".nyar api pillar list, .nyar api pillar <name> on|off")
                .Row("Admin: Zones", "Planned: defended zones. Shows a note for now.", null)
                .Row("Admin: Announce", "Type 1-200 characters and press <b>Announce</b> to broadcast. Shows the server's announcement switches, read-only.", ".nyar announce")
                .Row("Admin: Kill Switch", "<b>Purge…</b> (asks to confirm) ends every event and removes every unit. <b>Refresh</b> shows whether the purge cooldown is active.", ".nyar api purge, .nyar api purge confirm, .nyar api killswitch")
                .Row("Nyarlathotep Guide", "This page.", null))
            .P("Refresh buttons read data quietly. Action buttons on servers with api 4 or later (Nyarlathotep 0.5.2+) use the machine twins and show the result on the tab. On older servers they send the chat command and the reply shows in chat.")
            .P("The <b>Edit…</b> form reads " + C(".nyar event info <id>") + " and sends one " + C(".nyar event set") + " per changed field. Buttons like <b>Use where I stand</b> and <b>Goal here</b> send location here and action.goal here."));

        // ---------------------------------------------------------------------------------------- 5. player
        b.Section(GuideSchema.Player, false, s => s
            .P("Players can type these. Every Nyarlathotep command replies \"still loading\" until the server's world is ready.")
            .Commands("Everyone", c => c
                .Player("nyar", ".nyar",
                    "Overview: \"Nyarlathotep, Lord of Chaos (v...) - server events...\", then a line listing the commands you may run. Admins see every command.")
                .Player("nyar status", ".nyar status",
                    "One line per running event: \"Name: N min left\", or \"No active events.\" An apocalypse shows its horde, march progress, roamers and time left. Admins also see tracked unit counts and any degraded hook."))
            .P("Raphael also sends four machine commands for you: .nyar api version, .nyar api status, .nyar api regions and .nyar api sub. They are listed under Machine API in the Admin guide. They answer in [NYAR:...] lines meant for Raphael, not for reading.")
            .Callout(CalloutKind.Planned, "Player stats, leaderboards, Share my line and Hide me / Show me are planned. The server does not have these commands yet, so the buttons get no answer."));

        // ----------------------------------------------------------------------------------------- 6. admin
        b.Section(GuideSchema.Admin, false, s => s
            .Callout(CalloutKind.AdminOnly, "Every command in this section needs server admin rights. Open the F1 console and type adminauth. Then press <b>Re-detect Nyarlathotep</b> in <b>Settings and Help → Connection</b> (or click the greyed header and press <b>Re-check now</b>) so the admin tabs light up.")
            .Callout(CalloutKind.OffByDefault, "Everything starts off. All five pillars are false. Seeded examples and copied templates are disabled. Wave warnings, event banners and the daily banner are off; only the login notice is on. The master switch [General] Enabled is true.")
            .Detail("First event in five commands", d => d
                .Steps(
                    C(".nyar template list") + ": see the 36 ready-made events.",
                    C(".nyar template use undead-nightfall") + ": copy one into events.json, disabled.",
                    C(".nyar pillar empowerment on") + ": switch its pillar on (saved to the cfg).",
                    C(".nyar event enable undead-nightfall") + ": arm it.",
                    C(".nyar status") + ": at the next nightfall the undead are empowered for 20 minutes, and this shows it running.")
                .P("To test at once: " + C(".nyar event start undead-nightfall") + ". If anything goes wrong, " + C(".nyar purge") + " then " + C(".nyar purge confirm") + " ends everything."))
            .Detail("Event commands", d => d
                .P("A value with spaces must be quoted, for example " + C(".nyar event set raid name \"Night raid\"") + ". Every change is saved to events.json; one events.json.bak copy of the previous file is kept.")
                .Commands("Events", c => c
                    .Admin("nyar event", ".nyar event list [page]",
                        "Every definition, 10 per page: id, readiness, pillar, trigger, regions and RUNNING. Readiness is ready, off (event), off (pillar), off (mod), off (purge), full (cap) or invalid: reason. \"page must be 1-N\" if the page is out of range.").Override(EvVerbs)
                    .Admin("nyar event", ".nyar event info <id>",
                        "One definition in full: name, enabled, pillar, trigger, duration, conditions, action, scope, waves, the run state, an Interval's next start, revenge heat counts and unit watch counts.").Override(EvVerbs)
                    .Admin("nyar event", ".nyar event start <id>",
                        "Starts it now: \"event id started\". Conditions are skipped, but the pillar, master switch, purge cooldown, concurrent cap and regions still apply; a refusal names the rule. An Admin location or goal uses where you stand. A disabled event is refused.").Override(EvVerbs)
                    .Admin("nyar event", ".nyar event stop <id>",
                        "Ends it now: \"event id stopped\" (or \"not active\"). Its units leave after the grace time. A scoreboard event shows its scoreboard.").Override(EvVerbs)
                    .Admin("nyar event", ".nyar event enable <id>",
                        "Arms it so its trigger can start it. Saved to events.json.").Override(EvVerbs)
                    .Admin("nyar event", ".nyar event disable <id>",
                        "Stops its trigger from starting it. Saved to events.json. A running event keeps running.").Override(EvVerbs)
                    .Admin("nyar event", ".nyar event set <id> <field> <value>",
                        "Changes one field. See \"Fields you can set in chat\". A refusal names the rule. An unknown field replies \"field X is not settable; edit events.json and reload\".").Override(EvVerbs)
                    .Admin("nyar event", ".nyar event reload",
                        "Re-reads events.json after a hand edit. A bad entry is disabled with a log line naming the reason. A file that does not parse leaves the last good set running.").Override(EvVerbs)
                    .Admin("nyar event", ".nyar event new <id> <pillar>",
                        "Writes a disabled skeleton with a Manual trigger and 600 s. Pillar is empowerment, spawns, boss, zones, sieges, or revenge (a ready Revenge spawn event). The id is 1-32 characters of a-z, 0-9 and -. At most 200 events.").Override(EvVerbs)
                    .Admin("nyar event", ".nyar event copy <id> <newId>",
                        "Copies an event under a new id, disabled: \"event X copied to Y (disabled)\".").Override(EvVerbs)
                    .Admin("nyar event", ".nyar event delete <id> [confirm]",
                        "Run without confirm first; it asks you to run it again with confirm within 30 s. Deletes the definition from events.json. A running event is refused (stop it first). Only the previous file survives, in events.json.bak.").Override(EvVerbs).Destructive()
                    .Admin("nyar event", ".nyar event unstick <id>",
                        "Sends every unit of a running event that is not fighting through the unit-watch tries now: \"unstick id: m units marked, f fighting left alone\". Needs [UnitWatch] Enabled. Units that fail twice are removed without kill credit.").Override(EvVerbs)))
            .Detail("Fields you can set in chat", d => d
                .P("Use " + C(".nyar event set <id> <field> <value>") + ". Any field not listed here: edit events.json, then " + C(".nyar event reload") + ". Most fields take none to remove them.")
                .P("<b>Basics</b>")
                .Bullets(
                    "name: 1-40 characters, no angle brackets.",
                    "durationSeconds: 30-7200 (an apocalypse, infestation or horde march 600-604800).",
                    "conditions.minPlayers 0-100; conditions.cooldownMinutes 0-10080; conditions.chancePercent 1-100.",
                    "location: here (stores your position as a Point), \"aroundplayer " + A("min") + " " + A("max") + "\" (10-60 and 15-80, min below max), or region.")
                .P("<b>Trigger</b>")
                .Bullets(
                    "trigger.type: Manual, Schedule, GameTime, VBloodKilled, Interval, RegionEntered, FactionKills or Revenge. Changing it writes defaults (Schedule Sat 20:00, GameTime night, VBloodKilled any, Interval 60-90, FactionKills Faction_Bandits 20 kills in 300 s).",
                    "trigger.days: 1-7 of Sun, Mon, Tue, Wed, Thu, Fri, Sat, comma separated. trigger.times: 1-12 HH:mm values.",
                    "trigger.phase: day or night. trigger.bosses: any, or 1-20 CHAR_ names.",
                    "trigger.minMinutes, trigger.maxMinutes: 5-1440. trigger.playerCooldownMinutes: 0-1440.",
                    "trigger.factions: 1-5 Faction_ names. trigger.kills: 3-500. trigger.windowSeconds: 10-3600 (Revenge 60-3600). trigger.shared: true or false.",
                    "trigger.heat: 3-500. trigger.warnAt: up to 3 rising values below heat, or none. trigger.vbloodHeat: 0-100.",
                    "trigger.scope, action.scope: Global, or region names comma separated.")
                .P("<b>Empowerment</b>")
                .Bullets(
                    "action.stats.physicalPower, spellPower, maxHealth, attackSpeed, moveSpeed: 1.0-3.0 (2 decimals).",
                    "action.factions: 1-5 Faction_ names.")
                .P("<b>Waves</b>")
                .Bullets(
                    "action.waves 1-10; action.intervalSeconds 10-600; action.radius 2-30.",
                    "action.units: 1-10 entries, comma separated, each CHAR_name or CHAR_name:count (count 1-500), or pool:name:count, or (0.15.0) pool:name:min-max. A list with pools holds at most 500 units, each range counted at its max.",
                    "action.units." + A("n") + ".chance: 0.05-1.0.",
                    "action.modifiers.level 1-120; levelDelta -5 to 5; maxHealth, power, moveSpeed, attackSpeed 0.5-3.0.",
                    "action.loot, action.allowTerritory, action.scoreboard: true or false. Since 0.15.0 action.allowTerritory is also settable on an apocalypse, infestation or horde march.",
                    "action.behaviour: none, \"hunt " + A("10-60") + "\", or regionhunt.",
                    "action.fanOut: none, or \"" + A("2-10") + " " + A("50-500") + "\" (groups and spacing in metres).",
                    "action.arena: 15-60 or none. action.winner: killdeath or none. action.regionLock: true, false or none. action.pursuit: 15-600 or none.",
                    "action.growth: none. action.growth.addUnits 0-10; addLevels 0-5; whenCleared true or false.",
                    "action.bosses: CHAR_name:1-3, up to 3 entries, or pool:name:min-max (at most 5), or none.",
                    "action.pools." + A("name") + ": a list of 1-30 units, +unit to add one, -unit to remove one, or none.")
                .P("<b>Wave lists</b>")
                .Bullets(
                    "action.waveList." + A("n") + ".units: as action.units. n may be one past the last wave to add a wave. waveList.1.units turns a units-form event into a list.",
                    "action.waveList." + A("n") + ".afterSeconds: 10-3600 or none. whenCleared: true or false.",
                    "action.waveList." + A("n") + ".units." + A("m") + "." + A("modifier") + ": a modifier for one entry.",
                    "action.waveList." + A("n") + ".bosses: unit:count or pool:name:min-max, or none.",
                    "action.waveList." + A("n") + " none: removes that wave.")
                .P("<b>Apocalypse</b>")
                .Bullets(
                    "action.goal here: the goal is where you stand. action.goal.radius 10-100; action.goal.name 1-32 characters.",
                    "action.hordes.count 1-10; action.hordes.everyMinutes 5-1440; action.hordes.size 5-500.",
                    "action.roamers.max 1-500; action.marchSpeed 0.5-5.0 (m/s).")
                .P("<b>Destination</b> (spawn waves and horde march)")
                .Bullets(
                    "action.destination: here, or none (spawn waves only; it also removes marchSpeed).",
                    "action.destination.radius 10-100; action.destination.name 1-40 characters. Both are refused until a destination exists.",
                    "action.marchSpeed 0.5-5.0: on spawn waves only with a destination; refused on an infestation.")
                .P("<b>Infestation</b>")
                .Bullets(
                    "action.origins.count 1-10; .minNatives 1-20; .minSpacing 100-2000; .perOrigin 1-100.",
                    "action.hunters 0-100 (%); action.driftRadius 0-2000.",
                    "action.spread.perKillMin and perKillMax 0-3 (max at least min, checked on reload); spread.max 1-500; spread.perMinute 1-30; spread.pool: a pool name or none.")
                .P("<b>Horde march</b>")
                .Bullets(
                    "action.start here: the start is where you stand. action.start.minDistance 100-3000 replaces it with the natives' hotspot.",
                    "action.growth.* and action.infest.spread.*: the five spread keys. action.infest.hunters 0-100; action.infest.driftRadius 0-2000.",
                    "action.bosses as on spawn waves, with pool rolls.",
                    "Units, pools, waves and the scoreboard of an apocalypse, infestation or horde march are set in events.json only. A new one comes from a template or a copy."))
            .Detail("Templates (36 ready-made events)", d => d
                .Commands("Template commands", c => c
                    .Admin("nyar template", ".nyar template list [pillar] [page]",
                        "The built-in templates, 10 per page, optionally for one pillar.").Override("verbs from TemplateCommands.cs switch (verb, a, b, c are optional strings)")
                    .Admin("nyar template", ".nyar template info <template>",
                        "What a template contains and how it starts.").Override("verbs from TemplateCommands.cs switch (verb, a, b, c are optional strings)")
                    .Admin("nyar template", ".nyar template use <template> [as <id>]",
                        "Copies it into events.json, disabled: \"template T added as ID (disabled)\". If the id exists, add as and a new id.").Override("verbs from TemplateCommands.cs switch (verb, a, b, c are optional strings)"))
                .Bullets(
                    "<b>Empowerment</b>: legion-weekend-surge (Legion, Saturday 20:00, 30 min), bandit-vengeance (after a bandit V Blood dies), undead-nightfall (at nightfall, 20 min), militia-crackdown (after a Militia or Church V Blood dies).",
                    "<b>By command</b>: bandit-ambush, undead-rising, graveyard-waves (3 growing waves with a Guardian and the scoreboard), spider-swarm (5 growing waves in a 40 m arena, ending on Ungoro), spider-hunting-grounds (Dunley Farmlands), realm-gauntlet (5 factions, ending on the Overseer).",
                    "<b>Automatic</b>: roaming-hunters (every 60-90 min), border-watch (a player enters Dunley Farmlands), bandit-reprisal (20 bandit kills in 5 min), bandit-revenge (Revenge).",
                    "<b>Faction arenas</b>: militia-muster, silverlight-crusade, legion-incursion, gloomrot-assault, cursed-night. 4-5 growing waves in a 40 m arena around the admin, ending on a V Blood.",
                    "<b>Timed</b>: spider-swarm-timed, graveyard-waves-timed, militia-muster-timed, silverlight-crusade-timed, legion-incursion-timed, gloomrot-assault-timed, cursed-night-timed. Fixed 75 s beat, pursuit after 60 s.",
                    "<b>Onslaught</b>: spider-onslaught, undead-onslaught, militia-onslaught, silverlight-onslaught, legion-onslaught, gloomrot-onslaught, cursed-onslaught. Waves rolled from pools, V Bloods mid-run and at the end.",
                    "<b>Undead hordes</b> (Raphael groups these together): zombie-apocalypse (hordes march on where the admin stood), undead-infestation (the dead rise at the natives' origins and spread with every kill) and undead-horde-march (one horde marches from the natives' hotspot on where the admin stood, then infests it). Only one runs at a time, and the cfg caps their size.")
                .P("Templates that spawn around the admin or use an arena are started where you stand with " + C(".nyar event start <id>") + "."))
            .Commands("Pillars", c => c
                .Admin("nyar pillar", ".nyar pillar list",
                    "Each pillar on or off, with its cfg key.").Override("verbs from PillarCommands.cs switch (name, state are optional strings)")
                .Admin("nyar pillar", ".nyar pillar <name> <state>",
                    "Name is empowerment, spawns, boss, zones or sieges. State is on or off. Saved to the cfg at once. Switching off ends that pillar's running events.").Override("verbs from PillarCommands.cs switch (name, state are optional strings)"))
            .Commands("Kill switch", c => c
                .Admin("nyar purge", ".nyar purge [confirm]",
                    "Run without confirm first: it says how many events and units it will end. Then run with confirm within 30 s. Ends every event, removes every mod unit and every empowerment buff, forgets revenge heat, and blocks new events and spawns for PurgeCooldownSeconds (60). Cannot be undone; definitions stay.").Destructive())
            .Commands("Testing and units", c => c
                .Admin("nyar spawn", ".nyar spawn <unit> [count] [level] [hp] [power]",
                    "Spawns units beside you: \"spawned N name\". Unit is a CHAR_ name. Count 1 to MaxUnitsPerWave (default 1). Level 1-120, or +n / -n (0-30) on the unit's own. Hp and power are multipliers 0.1-10.0. Removed after ManualSpawnLifetimeSeconds (300). Unit watch skips them.")
                .Admin("nyar debug", ".nyar debug here [radius]",
                    "Lists up to 20 mod units within the radius (5-100, default 30) with prefab, event, lifetime, level and stats, then up to 10 native NPCs with their empowerment buff.").Override("verbs from SpawnCommands.cs Debug switch (where, radius, extra are optional strings; where must be here)")
                .Admin("nyar region", ".nyar region list",
                    "Each region with how many events use it, and the global count.").Override("verbs from RegionCommands.cs switch (verb is list or here)")
                .Admin("nyar region", ".nyar region here",
                    "Names the region you stand in, or \"you are outside every region\".").Override("verbs from RegionCommands.cs switch (verb is list or here)"))
            .Commands("Announce", c => c
                .Admin("nyar announce", ".nyar announce <text>",
                    "Broadcasts a line to every connected player. 1-200 characters, no angle brackets. Up to 16 words work without quotes; quote longer text.").Override("VCF 0.10 has no remainder argument: up to 16 words arrive as w1..w16 and are joined"))
            .Detail("Machine API (Raphael uses these)", d => d
                .P("Raphael sends these behind the scenes. Each answers in [NYAR:...] lines of at most 480 bytes: one [NYAR:ok] or [NYAR:err] line for an action. The admin action twins are limited to 5 per second.")
                .Commands("Machine API", c => c
                    .Player("nyar api version", ".nyar api version",
                        "The handshake: api level (8 in 0.14.0 and 0.15.0), plugin version, ready, admin, enabled, kill switch, each pillar and each announcement switch.")
                    .Player("nyar api status", ".nyar api status",
                        "Running events as lines, then an end line. Unit counts go to admins only.")
                    .Player("nyar api regions", ".nyar api regions [page]",
                        "The map's regions and their running events.")
                    .Player("nyar api sub", ".nyar api sub [state]",
                        "State is on or off. Turns live pushes on or off for you: event start and end, waves, wave warnings, kill switch, config changed.")
                    .Admin("nyar api events", ".nyar api events [page]",
                        "Event definitions as lines.")
                    .Admin("nyar api event", ".nyar api event <verb> <id> [field] [value]",
                        "The event actions: start, stop, enable, disable, set, reload, new, copy, delete (then confirm) and unstick (api 8).").Override("verbs from ApiAdminCommands.cs / AdminFlows (verb, id, field, value, extra are optional strings)")
                    .Admin("nyar api template", ".nyar api template use <template> [as <id>]",
                        "Template use, or info with a template name.").Override("verbs from AdminFlows template switch (use or info; verb, template, asWord, asId, extra are optional strings)")
                    .Admin("nyar api templates", ".nyar api templates [pillar] [page]",
                        "The template catalogue.").Override("pillar and page both optional; the method takes pillar, page, extra as optional strings")
                    .Admin("nyar api pillar", ".nyar api pillar list",
                        "Pillar switches, or name on|off to switch one.").Override("verbs from AdminFlows pillar switch (name, state, extra are optional strings)")
                    .Admin("nyar api purge", ".nyar api purge [confirm]",
                        "The kill switch, in two steps like .nyar purge.").Override("confirm is optional; extra only catches surplus words").Destructive()
                    .Admin("nyar api killswitch", ".nyar api killswitch",
                        "Whether the purge cooldown is active, its seconds left, and event and unit counts.").Override("the optional extra parameter only catches surplus words; any value is refused")))
            .Detail("The events.json file", d => d
                .P("BepInEx/config/Nyarlathotep/events.json holds every definition: SchemaVersion 1 and an events list, at most 200 events and 1 MB. Edit it, then " + C(".nyar event reload") + ". A bad entry is disabled with a log line naming the event and the reason.")
                .Bullets(
                    "<b>Event keys</b>: id, name, enabled, pillar, trigger, conditions, durationSeconds, action, announce.",
                    "<b>announce</b>: start and end hold 0-5 lines of 1-200 characters. Placeholders: {faction}, {minutes}, {event}, {zone}, {wave}, {waves}, {region}. warnings is true or false.",
                    "<b>Empower action</b>: factions 1-5, includeUnits and excludeUnits (up to 20 each), includeVBloods, stats 1.0-3.0 (at least one above 1.0), scope.",
                    "<b>SpawnWaves action</b>: units, waves, intervalSeconds, radius, location, unitLifetimeSeconds (30-7200), scope, modifiers, loot, behaviour, allowTerritory, fanOut, waveList, scoreboard, arena, winner, growth, regionLock, bosses, pools, pursuit, destination, marchSpeed.",
                    "<b>Apocalypse action</b>: faction, units (1-10, count is a weight), hotspots (count 1-5, minNatives 3-50, minSpacing 100-3000), roamers (perHotspot 1-10, perKillMin and perKillMax 0-3, pool, max 1-500, perMinute 1-30, huntRange 20-60), pools, hordes (count 1-10, firstAfterMinutes 0-1440, everyMinutes 5-1440, size 5-500, growth 0-100), goal (Point x and z, or Admin for manual starts; radius 10-100; name 1-32), marchSpeed 0.5-5.0, bosses, scope, scoreboard (default true).",
                    "<b>Infestation action</b>: faction, units, pools, origins (count, minNatives, minSpacing, perOrigin), hunters, driftRadius, spread (perKillMin, perKillMax, max, perMinute, pool), scope, scoreboard.",
                    "<b>HordeMarch action</b>: faction, units (10-500 in all), pools, bosses, start (a Point, Admin, or minDistance for the natives' hotspot), destination, marchSpeed, growth (the spread keys), infest (hunters, driftRadius, spread), scope, scoreboard.",
                    "<b>Rules</b>: an Admin location needs a Manual trigger. Hunt range must be at least the AroundPlayer maximum. fanOut needs AroundPlayer. arena needs a Point or Admin location. winner needs scoreboard. growth needs the units form and 2-10 waves. regionLock needs a regional scope and no arena; regionhunt needs regionLock. pursuit needs Hunt, with no fanOut and no regionLock. The last apocalypse horde must rise before the duration ends. A destination needs a Point or Admin location, a radius and a name, and no fanOut, regionLock, arena or pursuit. A horde march needs 10-500 units in all. A spread pool must exist.",
                    "Horse mounts and some prison and point-of-interest units cannot be spawned."))
            .Callout(CalloutKind.Planned, "<b>Boss reinforcements</b>: adds join a V Blood fight when it starts or drops below a health threshold. The boss pillar switch exists but does nothing yet.")
            .Callout(CalloutKind.Planned, "<b>Defended zones</b>: admin-defined areas summon reinforcements when vampire activity inside crosses a threshold. The Admin: Zones tab is a placeholder.")
            .Callout(CalloutKind.Planned, "<b>Sieges</b>: NPC war parties harass an eligible castle's perimeter, and only the owning clan is warned.")
            .Callout(CalloutKind.Planned, "<b>Leaderboards and stats</b>: per-player kills, events, waves and deaths for today, this week and all time, with an opt-out. LoginStats, PlayerShare and the share limits are reserved for this.")
            .P("To remove the mod: stop the server and delete Nyarlathotep.dll. Its units and buffs carry timers and expire on their own. To remove its data too, delete " + Cfg + " and the BepInEx/config/Nyarlathotep folder."));

        // ---------------------------------------------------------------------------------------- 7. config
        b.Section(GuideSchema.Config, false, s => s
            .P("Settings live in " + Cfg + " on the server. Event definitions live in BepInEx/config/Nyarlathotep/events.json, with an events.json.bak copy and a state.json for timers and the apocalypse.")
            .P("<b>Live vs restart.</b> Pillars change live with " + C(".nyar pillar") + " or the Admin: Pillars tab. Limits, WarningOffsets and DailyBannerTime are read once at startup. The server does not watch the cfg file, so other hand edits take effect at the next restart. A " + C(".nyar pillar") + " command also re-reads the whole file.")
            .P("Out-of-range limits are clamped at load, with a log line.")
            .Settings("General and pillars", Cfg, t => t
                .Row("General", "Enabled", "true", "Master switch. When false, no events run and no units are spawned.", "restart")
                .Row("Pillars", "FactionEmpowerment", "false", "Allows empowerment events.", "live")
                .Row("Pillars", "EventSpawns", "false", "Allows spawn events: waves, revenge and the apocalypse.", "live")
                .Row("Pillars", "BossReinforcements", "false", "Planned pillar. No event type uses it yet.", "live")
                .Row("Pillars", "DefendedZones", "false", "Planned pillar. No event type uses it yet.", "live")
                .Row("Pillars", "SiegeWaves", "false", "Planned pillar. No event type uses it yet.", "live"))
            .Detail("[Limits]", d => d
                .Settings("Limits", Cfg, t => t
                    .Row("Limits", "MaxTrackedUnits", "500", "Most mod units alive at once across all events, 1-500. New spawns past it are skipped. It is also the only limit on a horde's size (0.15.0; it was 150 before, and a cfg that already holds a value keeps it).", "restart")
                    .Row("Limits", "MaxUnitsPerWave", "20", "Most units in one spawn wave, 1-500. Before 0.15.0 it also capped a horde. Also caps the count of " + C(".nyar spawn") + ".", "restart")
                    .Row("Limits", "MaxConcurrentEvents", "3", "Events running at once, 1-10.", "restart")
                    .Row("Limits", "MaxSpawnsPerTick", "10", "Units spawned per server tick, 1-40. A bigger wave spreads over the next ticks.", "restart")
                    .Row("Limits", "MaxDespawnsPerTick", "5", "Units removed per server tick, 1-40.", "restart")
                    .Row("Limits", "EmpowerBatchPerTick", "200", "Empowerment buffs applied or removed per server tick, 50-1000.", "restart")
                    .Row("Limits", "GraceSeconds", "30", "Seconds an event's units outlive its end, 0-600.", "restart")
                    .Row("Limits", "PurgeCooldownSeconds", "60", "Seconds after a purge during which no event starts and nothing spawns, 0-3600.", "restart")
                    .Row("Limits", "ManualSpawnLifetimeSeconds", "300", "Lifetime of a " + C(".nyar spawn") + " unit, 30-3600.", "restart")))
            .Detail("[Announcements]", d => d
                .Settings("Announcements", Cfg, t => t
                    .Row("Announcements", "LoginEventNotice", "true", "A private line 10 s after login naming the running events and their time left. Raphael's own Login summary notice may show as well.", "restart")
                    .Row("Announcements", "WaveWarnings", "false", "Server-wide warnings before each wave, for events with announce.warnings true.", "restart")
                    .Row("Announcements", "WarningOffsets", "300,60,10", "Seconds before a wave when warnings fire: 1-5 comma-separated values, each 5-3600. Bad values are dropped; none left means 300,60,10.", "restart")
                    .Row("Announcements", "EventBanners", "false", "Server-wide lines when an event starts and ends.", "restart")
                    .Row("Announcements", "DailyBanner", "false", "A server-wide line once a day naming the scheduled events still due.", "restart")
                    .Row("Announcements", "DailyBannerTime", "20:00", "Server-local time of the daily banner, HH:mm. An invalid value becomes 20:00.", "restart")
                    .Row("Announcements", "LoginStats", "false", "Reserved for player stats (planned). No effect in 0.14.0.", null)
                    .Row("Announcements", "PlayerShare", "false", "Reserved for player stats (planned): lets players share a leaderboard line. No effect in 0.14.0.", null)
                    .Row("Announcements", "ShareCooldownSeconds", "300", "Reserved: seconds between two shares by one player, 10-86400.", null)
                    .Row("Announcements", "ShareMaxPerMinute", "3", "Reserved: most shares the server passes per minute, 1-20.", null)))
            .Detail("[UnitWatch] and [Scoreboard]", d => d
                .Settings("Unit watch and scoreboard", Cfg, t => t
                    .Row("UnitWatch", "Enabled", "true", "Stuck event units are moved back unseen, then removed after two failed tries. False also refuses " + C(".nyar event unstick") + ".", "restart")
                    .Row("UnitWatch", "StuckSeconds", "90", "Seconds a unit trying to reach a player may stand still before it is stuck, 30-600.", "restart")
                    .Row("UnitWatch", "StallMinutes", "10", "Minutes with no death and no fight before a waited-on wave's leftovers count as stuck, 0-60. 0 turns it off.", "restart")
                    .Row("UnitWatch", "SpawnStillSeconds", "10", "Seconds after a spawn or landing within which a unit targeting a player must move, 5-60.", "restart")
                    .Row("Scoreboard", "IncludeAdmins", "false", "Count admins on event scoreboards. Off leaves their kills and deaths out.", "restart")))
            .Detail("[Debug]", d => d
                .Settings("Debug", Cfg, t => t
                    .Row("Debug", "VerboseLogging", "false", "Logs each unit spawned and removed, and each announcement sent.", "restart")
                    .Row("Debug", "TimingLog", "false", "Logs the scheduler tick's average and maximum time once a minute.", "restart")
                    .Row("Debug", "FaultInjection", "(empty)", "Debug builds of Nyarlathotep only; a release build has no such key. For the mod's own testing.", "restart")))
            .P("[General] AnnounceEvents from 0.1.0 is retired and ignored. Use the [Announcements] switches."));

        // --------------------------------------------------------------------------------------- 8. trouble
        b.Section(GuideSchema.Trouble, false, s => s
            .Detail("The Nyarlathotep tabs are greyed", d => d
                .Bullets(
                    "Raphael has not had an answer to its handshake (.nyar api version). The server may not run Nyarlathotep, or it was still loading.",
                    "Fix: press <b>Re-detect Nyarlathotep</b> in <b>Settings and Help → Connection</b>, or click the greyed header and press <b>Re-check now</b>.",
                    "<b>Force-enable tabs</b> shows them anyway. NyarAvailability (Auto, On, Off) in kdpen.Raphael.cfg forces the group on or off."))
            .Detail("Admin tabs greyed but I am an admin", d => d
                .Bullets(
                    "The handshake ran before you were admin.",
                    "Fix: type adminauth in the F1 console, then press <b>Re-detect Nyarlathotep</b> or <b>Re-check now</b>."))
            .Detail("Lists are empty or a tab says it is too old", d => d
                .Bullets(
                    "A tab note says the server is too old: Live Events needs api 2, the admin tabs need api 4 (Nyarlathotep 0.5.2 or later), the Regions card needs api 5.",
                    "\"General.Enabled = false\" means the master switch is off, so nothing runs.",
                    "Fix: ask the server admin to update Nyarlathotep. The Settings tab's Server switches readout shows the plugin version and api level."))
            .Detail("Unstick does nothing or is missing", d => d
                .Bullets(
                    "<b>Unstick</b> needs Nyarlathotep 0.13.0 or later (api 8).",
                    "\"unit watch is off\" means [UnitWatch] Enabled is false.",
                    "\"not running\" or \"no live unit\": the event has no units to move."))
            .Detail("Test builds (rc)", d => d
                .P("A version like 0.13.0-rc.1 is a test build, not a release. Expect rough edges and report problems with diagnostics on."))
            .Detail("An event never starts", d => d
                .Bullets(
                    "Run " + C(".nyar event list") + ". The readiness column says why: off (event) means disabled, off (pillar) means its pillar is off, off (mod) means General.Enabled is false, off (purge) means the purge cooldown is running, full (cap) means MaxConcurrentEvents is reached, invalid gives the reason.",
                    "Conditions (players online, cooldown, chance, time window) can hold back automatic starts. A regional trigger needs a player in its regions.",
                    "Waves never spawn in castle territory unless allowTerritory is set.",
                    "An apocalypse, infestation or horde march refused with \"inside a castle's territory\" (0.15.0) needs another start spot, or allowTerritory set to true."))
            .Detail("\"command not found\" or no reply", d => d
                .Bullets(
                    "Replies to sent commands show in chat. \"command not found\" means the server lacks that command: an older Nyarlathotep, a planned feature (stats, top, zone), or a typo. Type " + C(".nyar") + " to list the real commands.",
                    "Admin commands answer only admins. Run adminauth first.",
                    "\"still loading\" means the server's world is not ready yet. Wait and retry.",
                    "No reply at all: Raphael may hide the line. Turn on <b>Settings and Help → Chat & Diagnostics → Show everything</b>."))
            .Detail("Reporting a bug", d => d
                .Steps(
                    "Turn on <b>Enable diagnostic details (verbose Nyarlathotep logging)</b> in the Nyarlathotep <b>Settings</b> tab.",
                    "Repeat the problem.",
                    "Send BepInEx/LogOutput.log, filtered for [Nyar]. Admins can add the server log with [Debug] VerboseLogging true.")));

        list.Add(b.DefaultsNote("Event lengths, wave sizes and templates are also set per event in events.json.").Build());
    }
}
