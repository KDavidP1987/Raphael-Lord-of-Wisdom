using System.Collections.Generic;

namespace Raphael.Services.Guides;

// 0.73.0: the Uriel guide (replaces the old "Uriel Quick Start" and "Uriel Help" pages). Checked against Uriel 0.22.0
// (Commands/*.cs, Config/Settings.cs, README, docs/features/*) and Raphael's own Uriel tabs (MainPanel.Uriel*.cs,
// Services/Uriel/UrielClient.cs).
internal static partial class Guides
{
    static partial void AddUriel(List<Guide> list)
    {
        static string Arg(string n) => GuideText.Arg(n);
        static string Cmd(string t) => GuideText.Cmd(t);
        const string Cfg = "BepInEx/config/kdpen.Uriel.cfg";

        var b = new GuideBuilder("uriel", "Uriel guide",
            "Shared chests and prison cells, stair restyling, world-object spawning and Castle Events, driven from Raphael's Uriel tabs.",
            "Uriel 0.22.0", GuideKind.FullMod, "uriel");

        // ------------------------------------------------------------------ 1. What it is
        b.Section(GuideSchema.What, true, s =>
        {
            s.Bullets(
                "Uriel (\"Lord of Hosts\") is a <b>server-side</b> mod. It is separate from Bloodcraft and Beelzebub, and a server can run any mix of them. Players install nothing for it; Raphael is the optional point-and-click UI.",
                "It adds five features: <b>public storage</b> (share one chest with everyone, with optional permissions, limits and an access cost), <b>public prison cells</b>, <b>stair editing</b> (restyle or cleanly remove a placed staircase), <b>object spawning</b> (collect world objects and place them in your castle) and <b>Castle Events</b> (spawn mobs and bosses into a castle behind locked doors and run a timed clear).",
                "Every feature has its own on/off switch in the server config, plus one master switch for the whole mod, and sharing or editing is always per object: nothing is changed server-wide for you.",
                "Feature status in 0.22.0: object spawning, public storage and stair editing are <b>beta</b>; Castle Events is <b>alpha</b> and <b>off by default</b>. Object spawning is <b>admin-only</b> until the admin opens it to players.",
                "The Uriel tab group only lights up when Raphael confirms Uriel on the server. Every button sends a normal <b>.uriel</b> chat command, so everything here also works by typing.");
        });

        // ------------------------------------------------------------------ 2. Quick start
        b.Section(GuideSchema.QuickStart, true, s =>
        {
            s.Steps(
                "<b>Share a chest.</b> Stand next to one of your castle chests, open <b>Uriel → Storage Sharing</b> and press <b>Share: take-only</b>, <b>Share: give-only</b> or <b>Share: give + take</b>. The chest is now public. <b>Unshare nearest</b> makes it private again.",
                "<b>Share a prison cell.</b> On <b>Prisons</b>, stand by the cell and press <b>Share cell</b>. Anyone can now feed the prisoner and extract blood through the normal cell menu. To take a shared prisoner home, ready <b>Dominating Presence</b> and press <b>Take prisoner (nearest cell)</b>.",
                "<b>Restyle a staircase.</b> On <b>Stairs</b>, stand by a staircase you own and press a style (<b>stone1</b> … <b>strongblade</b>) or <b>Next style</b>. The change shows for everyone at once. DLC styles need that DLC.",
                "<b>Spawn objects you have unlocked</b> (when the admin allows players). Destroy world objects such as crates, trees and ore veins for a chance to unlock them. On <b>Object Spawning</b>, press <b>Refresh my unlocked list</b>, stand inside your plot and press <b>Spawn</b> next to an object. <b>Rotate nearest</b>, <b>Raise nearest</b>, <b>Lower nearest</b> and <b>Despawn nearest</b> adjust it.",
                "<b>Run a castle event</b> (only if the admin turned Castle Events on). On <b>Castle Events</b>, type a group name, stand where foes should appear and press <b>Mob marker here</b> or <b>Boss marker here</b>, then stand by a door and press <b>Bind nearest door</b>. Press <b>Start</b>. The doors stay shut until each group is beaten.",
                "If the tabs are greyed, click the greyed <b>Uriel</b> rail header and press <b>Re-check now</b>. Everything else is in the sections below.");
        });

        // ------------------------------------------------------------------ 3. How it works
        b.Section(GuideSchema.Concepts, false, s =>
        {
            s.Detail("Targeting: aim, nearest and here", d =>
            {
                d.P("Most Uriel commands act on one object. Typed in chat, they use what you are <b>aiming at</b>. Clicking a Raphael panel leaves your aim pointing anywhere, so Raphael adds a token that targets by position instead:");
                d.Bullets(
                    "<b>nearest</b>: the closest qualifying container, cell or staircase to you. Raphael adds it to every share, info, pay chest, prisoner and stair button. The chat reply names the object it hit.",
                    "<b>here</b>: place a spawned object at your own feet instead of your aim point. Raphael's <b>Spawn</b> buttons always send it.",
                    "Placed-object commands (move, rotate, raise, lower, despawn, spawninfo) pick the Uriel object nearest your aim point or you, within 8 m by default.",
                    "Containers must be within 5 m and staircases within 6 m by default. The admin can change all three distances.");
            });
            s.Detail("Public storage rules", d =>
            {
                d.Bullets(
                    "Only the chest's owner (or clan) can share it, and only that one container is affected. Admins can share, unshare or change any container.",
                    "<b>Permission</b>: take-only, give-only (a donation box) or give + take.",
                    "<b>Withdrawal limit</b>: N stacks per player in a rolling window of H hours. Setting only one of the two defaults the other to 1 stack or 24 hours. Each drag counts as one stack, and \"take all\" is blocked on a limited chest.",
                    "<b>Access cost</b>: an item and amount charged per stack withdrawn, taken from the taker's inventory. Payment goes to your <b>pay chest</b> (a private general chest you choose), else into the shared chest, else the nearest general chest on the same castle. If none has room, the withdrawal is refused and the taker keeps the payment.",
                    "Owners and clan members ignore all rules on their own chests. Shares survive restarts.",
                    "When a chest or cell is first made public, another player may need to <b>log out and back in once</b> before their game lets them open it.");
            });
            s.Detail("Public prison cells", d =>
            {
                d.Bullets(
                    "Sharing a cell lets anyone feed the prisoner and extract blood through the normal cell menu.",
                    "The game only shows the subdue button to the cell's own clan, so <b>Take prisoner</b> (" + Cmd(".uriel takeprisoner") + ") does it for everyone else: the prisoner is released to you, subdued. Have <b>Dominating Presence</b> ready to escort it.",
                    "Owners and admins can take from any cell, shared or not. Cell sharing has its own switch, separate from chest sharing.");
            });
            s.Detail("Stair editing", d =>
            {
                d.Bullets(
                    "A restyle rebuilds the staircase on the spot in the new style. Position, rotation and ownership are kept, it is free, and everyone sees it at once with no restart.",
                    "Only styles of the same shape are offered (straight, left curve, right curve, wide). The six styles are stone1, stone2, stone3, gloomrot, projectk and strongblade. The last three need their DLC, the same rule as the build menu.",
                    "<b>Remove stairs</b> deletes the whole staircase without breaking the floors and walls it joins. You must own it. No materials are refunded.");
            });
            s.Detail("Object spawning: what you can place", d =>
            {
                d.Bullets(
                    "Uriel places <b>world objects the build menu never offers</b>: resource nodes, world chests, crates and urns, and props from across the map. Normal build pieces are left out unless the admin turns them on.",
                    "Players place objects only inside <b>their own</b> castle plot; admins can place on any plot. Objects join the castle you stand in, persist across restarts, and are placed and moved with Uriel commands, not the build menu.",
                    "By default an object is <b>indestructible and immune to decay</b>. Flags change that per spawn: <b>breakable</b> (raids and decay can destroy it), <b>smashable</b> (breakable, and you can break it yourself; it is not castle-owned, decays, and anyone in the territory can damage it), <b>respawn</b> (it comes back after being destroyed until the castle is gone or you despawn it).",
                    "Uriel refuses a spot that would put an object inside a wall, station, prop or another spawned object (floors are fine), and keeps 0.5 m between objects by default.",
                    "When a castle is destroyed or abandoned, its spawned objects are removed automatically within about two minutes, and again at the next boot.",
                    "Some of the game's thousands of objects have not been tested as decor. If something spawns but you can't see it, it is there but invisible: <b>despawn</b> it and tell the admin so it can be blocked.");
            });
            s.Detail("Object spawning: collecting (Discovery mode)", d =>
            {
                d.Bullets(
                    "The server picks how players get objects. <b>Discovery</b> (default): you unlock an object by destroying one in the world, with a 25% chance by default. <b>Full</b>: every object in the catalog is available.",
                    "About half of all objects can't be destroyed, so discovery can't reach them. Depending on the server they unlock when you collect 100% of the discoverable set, when you defeat Dracula, when you defeat every main V Blood, through a per-boss unlock map, or only by admin grant (the default).",
                    "<b>Unlocks</b> shows your collection and percentage; the <b>Object Catalog</b> tab lists everything that exists. Turn unlock messages on or off with <b>Notify on</b> / <b>Notify off</b>.",
                    "The admin can set a build cost, a per-plot limit per object, whether players may place indestructible or respawning copies, and a height correction for objects that float or sink.");
            });
            s.Detail("Castle Events: the model", d =>
            {
                d.P("A castle event turns a castle into a run: foes spawn at markers, doors stay locked until their group falls, and a clock times the clear. It is built in layers:");
                d.Bullets(
                    "<b>Layout</b> (one per castle plot): named <b>groups</b>. A group is created by its first <b>marker</b> and sits on the floor you stood on. Each marker is a spot with a slot (<b>mob</b> or <b>boss</b>); a group can hold one <b>door</b>.",
                    "<b>Squad</b>: what a group spawns. It has one or more <b>sets</b> (alternative combinations); one set is rolled per run, by weight. Each set has entries: a role, a unit list, a count, a pick (<b>any</b> = random with repeats, <b>distinct</b> = that many different units) and a level.",
                    "<b>Floor template</b>: which squad each group on a floor gets. Several squads = one rolled per run.",
                    "<b>Template</b>: which floor template each floor gets, plus default run options. Several floor templates = one rolled per floor per run, so a floor can have a different theme each time.",
                    "Squads, floor templates and templates share one set of names (1-24 letters, digits, - or _). An admin's are server-wide; a player's are their own.");
            });
            s.Detail("Castle Events: a run", d =>
            {
                d.Bullets(
                    "<b>Start</b> takes a template, a floor template (used on every floor) or a squad (every group gets it). With none, each group uses its own squad. A marker that names its own unit keeps it.",
                    "Everything must resolve and fit the unit caps before anything spawns or is charged. Each bound door is shut and locked; even a forced-open door is shut again.",
                    "The clock starts when every foe is in place. Clearing a group unlocks and opens its door. The last boss falling and the run finishing are timed and announced to everyone on the plot.",
                    "A time limit warns at 60 and 10 seconds, then fails the run. <b>Stop</b> removes the units and puts every door back as it was. A server restart also ends a run and restores the doors.",
                    "Event units never leave the castle plot. By default they also stay on the floor they spawned on; a marker can keep them to a room, the storey or a leash radius instead. A unit that strays is returned to its spawn spot.",
                    "Units are never saved and drop no loot. By default the whole run fights as one faction, so mixed units don't kill each other.",
                    "Layout edits are refused while a run is going. <b>History</b> keeps the last 100 runs and shows the last 5 and the best times.");
            });
            s.Detail("Castle Events: run options", d =>
            {
                d.P("Options are one comma-separated word, such as " + Cmd("hard,auto+2,limit=15m") + ". A template's own options apply first; the ones you add at start add to or override them.");
                d.Bullets(
                    "<b>easy</b>: health and damage ×0.75 · <b>normal</b>: no change · <b>hard</b>: health ×1.5, damage ×1.25 · <b>brutal</b>: health ×2.5, damage ×1.5, move speed ×1.1, attack speed ×1.15.",
                    "<b>level=N</b> sets every unit's level (1-200); <b>level+N</b> / <b>level-N</b> shifts each unit's own level; <b>auto</b> or <b>auto+N</b> uses the highest player level on the plot at start.",
                    "<b>hp*X</b>, <b>dmg*X</b> (physical and spell power), <b>pp*X</b> (physical only), <b>sp*X</b> (spell only), <b>speed*X</b>, <b>aspd*X</b>: multipliers from 0.1 to 10.",
                    "<b>limit=15m</b>, <b>limit=90s</b>, <b>limit=1h</b> (6 hours at most), or <b>limit=0</b> for none.",
                    "<b>faction=keep</b>: units keep their own factions. <b>faction=one</b>: force one faction (overrides a template's keep).",
                    "The game cuts chat lines at about 120 characters, and Raphael refuses to send a longer event command. Use short unit names (Foreman_VBlood works for CHAR_Bandit_Foreman_VBlood) or GUIDs, and add long unit lists in parts with <b>Extend</b>.");
            });
        });

        // ------------------------------------------------------------------ 4. Raphael's tabs
        b.Section(GuideSchema.Tabs, false, s =>
        {
            s.P("The Uriel group has eight player tabs and four admin tabs. Admin tabs show a <b>Re-check admin</b> gate; the server checks admin either way.");
            s.Tabs(t => t
                .Row("Storage Sharing", "Quick share buttons for the nearest chest, <b>Share with custom rules</b> (permission, withdrawal limit, limit window, cost item and amount, <b>Find item id</b>), <b>Set pay chest</b>, <b>Unshare nearest</b>, <b>List my shares</b>, <b>Unshare ALL mine</b>, <b>Server rules (nearest)</b>, <b>Check nearby</b> and <b>Show nearby overlay</b> (a draggable list of public containers around you, read on your client).",
                    ".uriel share nearest permission … [limitwithdrawal n] [limithours h] [cost item amount] · .uriel info nearest · .uriel paychest nearest · .uriel unshare nearest · .uriel shared · .uriel unsharemine · .uriel finditem <name>")
                .Row("Prisons", "<b>Share cell</b>, <b>Unshare cell</b>, <b>Show rules</b> and <b>Take prisoner (nearest cell)</b>.",
                    ".uriel share nearest · .uriel unshare nearest · .uriel info nearest · .uriel takeprisoner nearest")
                .Row("Stairs", "One button per style, <b>Next style</b>, <b>Show styles</b>, <b>Remove stairs</b>. An admin box adds <b>Stair purge (5m)</b> and <b>Refresh (diag)</b>.",
                    ".uriel stairswap <style> nearest · .uriel stairswap next nearest · .uriel stairstyles nearest · .uriel removestairs nearest · .uriel stairpurge · .uriel stairrefresh")
                .Row("Object Spawning", "<b>Building hotkeys</b> (a <b>Build Mode</b> toggle that is off at every login, plus keys for Move, Rotate and Remove), collection progress, <b>Refresh my unlocked list</b>, <b>Notify on</b> / <b>Notify off</b>, <b>Spawn as</b> (<b>Durability</b> and <b>Respawn</b>), <b>Despawn nearest</b>, <b>Rotate nearest</b>, <b>Raise nearest</b>, <b>Lower nearest</b>, <b>Show spawn overlay</b> (a draggable palette for building) and your unlocked list with a <b>Spawn</b> button per object.",
                    ".uriel api unlocked [page] · .uriel spawn <guid> 0 [breakable|smashable] [respawn] here · .uriel move · .uriel rotate · .uriel raise · .uriel lower · .uriel despawn · .uriel notify on|off")
                .Row("Object Catalog", "Every spawnable object, by category and ID. <b>Load full catalog</b> (cached after the first load), <b>Re-scan</b>. Unlocked rows have <b>Spawn</b>, using the <b>Spawn as</b> choice from Object Spawning.",
                    ".uriel api catalog [page] · .uriel spawn <guid> 0 … here")
                .Row("Castle Events", "<b>Check</b> (is it on for you), run notices toggle, <b>Start</b> / <b>Stop</b> / <b>Status</b> / <b>History</b> with a template field and run options, and the layout builder: <b>Mob marker here</b>, <b>Boss marker here</b>, <b>Bind nearest door</b>, <b>Unbind door</b>, <b>Show layout</b>, <b>Remove group</b>, <b>Set group squad</b>, <b>Clear group squad</b>, <b>Remove marker</b>.",
                    ".uriel event list · start · stop · status · history · marker · door · undoor · squad · remove")
                .Row("Event Library", "Build squads (<b>Create</b>, <b>Show</b>, <b>Delete</b>, <b>Add entry</b>, <b>Extend entry</b>, <b>Set weight</b>, <b>Remove</b>), floor templates (<b>Assign</b>, <b>Unassign group</b>) and templates (<b>Set floor</b>, <b>Clear floor</b>, <b>Set default options</b>, <b>Use run panel's</b>, <b>Clear options</b>).",
                    ".uriel squad … · .uriel floor … · .uriel template …")
                .Row("Settings", "Raphael-side only: the <b>Diagnostics</b> toggle (logs every Uriel command and reply), a connection readout, and the tab-group availability.", null)
                .Row("Admin: Sharing", "<b>All shares</b>, <b>Debug (aimed)</b>, <b>Player's shares</b>, <b>Unshare player</b>, <b>Unshare ALL</b>.",
                    ".uriel sharedall [player] · .uriel sharedebug · .uriel unshareplayer <player> · .uriel unshareall")
                .Row("Admin: Objects", "<b>Blocklist</b>, <b>Boss map</b>, <b>Block</b> / <b>Unblock</b>, <b>View unlocks</b>, <b>Grant</b>, <b>Revoke</b>, <b>Grant ALL</b> with a subset, spawn conditions (per object and global), plot tools (<b>Purge plot</b>, <b>Arm force-despawn</b> + <b>Confirm</b>, <b>Purge orphans (server-wide)</b>) and a note on the overlap settings.",
                    ".uriel blocklist · bossmap list · block · unblock · unlocks <player> · grant · revoke · grantall · objcfglist · objcfg · objcfgglobal · purgeplot · forcepurgeplot · forcedespawn [confirm] · purgeorphans")
                .Row("Admin: Controls", "<b>What's switched on</b>: every Uriel switch the server reports, with <b>Refresh</b>. <b>Undo what Uriel did</b>: <b>Stop all events</b>, <b>Remove all units</b>, <b>Remove all objects</b> and <b>Restore vanilla</b>, each an <b>Arm</b> + <b>Confirm</b> pair with the server's reply under it. Below them, the object-spawning handshake (collection, discovery mode and chance, admin-only, prefab totals).",
                    ".uriel features · .uriel event stopall · .uriel unit purge [confirm] · .uriel purgeall [confirm] · .uriel killswitch [confirm] · .uriel api version")
                .Row("Admin: Units", "Castle Events unit tools: <b>Spawn</b>, <b>Kill nearby</b>, <b>Despawn nearest</b>, <b>List</b>, <b>Status</b>, <b>Clear plot</b>, <b>Arm purge</b> + <b>Confirm purge</b>, <b>Rooms</b>, <b>Probe</b>, <b>Probe map</b>, and <b>Door</b> open / close / lock / unlock / show.",
                    ".uriel unit spawn · kill · despawn · list · status · clear · purge [confirm] · rooms · probe · probemap · door <action>"));
            s.Callout(CalloutKind.Note, "A feature the server has switched off stays visible: Raphael greys out its buttons and says which switch is off. Admins see every switch on <b>Admin: Controls</b>. Raphael sends reads (" + Cmd(".uriel api …") + ") silently; actions show their reply in chat.");
        });

        // ------------------------------------------------------------------ 5. Player commands
        b.Section(GuideSchema.Player, false, s =>
        {
            s.P("Every command starts with <b>.uriel</b>. Where a command takes " + Arg("mode") + ", the only value is <b>nearest</b> (target the closest object to you instead of your aim).");

            s.Commands("Help", c => c
                .Player("uriel", ".uriel", "A short overview of Uriel and its version.")
                .Player("uriel help", ".uriel help [topic]", "The command menu. Topics: objects, storage, stairs, events, admin."));

            s.Detail("Public storage and prison cells", d =>
            {
                d.Callout(CalloutKind.Note, "Needs PublicStorage.Enabled (on by default). Prison cells also need PublicStorage.PrisonEnabled (on by default).");
                d.Commands("Sharing a container", c => c
                    .Player("uriel share", ".uriel share", "Make the aimed container (chest or prison cell) public. Rules can be added in the same command, in any order; nothing is applied if one is wrong.").Override("verbs from ShareCommands.cs switch")
                    .Player("uriel share", ".uriel share permission <take|give|givetake>", "Who may use it: take-only, give-only (donation box) or both.").Override("verbs from ShareCommands.cs switch")
                    .Player("uriel share", ".uriel share limithours <hours>", "The window for the withdrawal limit, in hours. Alone, the limit is 1 stack per window.").Override("verbs from ShareCommands.cs switch")
                    .Player("uriel share", ".uriel share limitwithdrawal <stacks>", "Most stacks one player may take per window. Alone, the window is 24 hours.").Override("verbs from ShareCommands.cs switch")
                    .Player("uriel share", ".uriel share cost <itemId> <amount>", "Charge an item per stack withdrawn. Find the item id with .uriel finditem.").Override("verbs from ShareCommands.cs switch")
                    .Player("uriel share", ".uriel share nearest [rules]", "Act on the nearest container instead of the aimed one. Example: .uriel share nearest permission take limithours 24 limitwithdrawal 1.").Override("verbs from ShareCommands.cs switch")
                    .Player("uriel unshare", ".uriel unshare [mode]", "Make the aimed public container private again.")
                    .Player("uriel unsharemine", ".uriel unsharemine", "Make every container you shared (or control) private again. All their rules are lost; share again to restore.").Destructive()
                    .Player("uriel info", ".uriel info [mode]", "Show a container's sharing rules. Works for anyone.")
                    .Player("uriel shared", ".uriel shared", "List every container you have made public, with its rules.")
                    .Player("uriel paychest", ".uriel paychest [mode]", "Choose the aimed private general chest to receive the access costs of your shares.")
                    .Player("uriel finditem", ".uriel finditem <fragment>", "Search item ids by name (up to 8 results), for the cost rule.")
                    .Player("uriel takeprisoner", ".uriel takeprisoner [mode]", "Take the prisoner from a shared cell: it is subdued and released to you. Ready Dominating Presence first."));
            });

            s.Detail("Stairs", d =>
            {
                d.Callout(CalloutKind.Note, "Needs StairSwap.Enabled (on by default). Aim at, or stand by, a staircase you own.");
                d.Commands("Stair editing", c => c
                    .Player("uriel stairswap", ".uriel stairswap <style> [mode]", "Restyle the staircase live. Style: stone1, stone2, stone3, gloomrot, projectk, strongblade (the last three need their DLC), or next to cycle to the next style you own.")
                    .Player("uriel stairstyles", ".uriel stairstyles [mode]", "Show the staircase's shape, current style and the styles you can use.")
                    .Player("uriel removestairs", ".uriel removestairs [mode]", "Delete the whole staircase and leave the floors and walls it joins intact. No materials are refunded, and it can't be undone.").Destructive());
            });

            s.Detail("Object spawning", d =>
            {
                d.Callout(CalloutKind.OffByDefault, "Needs ObjectSpawn.Enabled (on). While ObjectSpawn.AdminOnly is true (the default), every command here except the machine API replies \"admin-only on this server\" for players.");
                d.Commands("Placing objects", c => c
                    .Player("uriel spawn", ".uriel spawn <prefab> [rotation] [flags]", "Place an object (name or GUID) at your aim point in the castle plot you stand in. Rotation 0-3 and the flags breakable, smashable, respawn, indestructible and here (at your feet) may come in any order. Admins also have force, for non-networked objects that may be invisible.").Also("Raphael sends: .uriel spawn <guid> 0 [breakable|smashable] [respawn] here").Override("the six optional tokens a1-a6 take the rotation and flags in any order")
                    .Player("uriel move", ".uriel move [flag]", "Move the nearest object you spawned to your aim point, or to your feet with here.")
                    .Player("uriel rotate", ".uriel rotate [rotation]", "Turn the nearest object you spawned 90 degrees, or set rotation 0-3.")
                    .Player("uriel raise", ".uriel raise [amount]", "Raise the nearest object you spawned by the amount in metres (default 0.25), so it sits flush.")
                    .Player("uriel lower", ".uriel lower [amount]", "Lower the nearest object you spawned by the amount in metres (default 0.25), so it stops floating.")
                    .Player("uriel despawn", ".uriel despawn", "Remove the nearest object you spawned. Players can only remove objects in their own plot. Refunds the build cost only when the server has RefundOnRemove on.").Destructive()
                    .Player("uriel spawninfo", ".uriel spawninfo", "Show the nearest spawned object's prefab, owner, durability and decay flags, and plot.")
                    .Player("uriel spawnlist", ".uriel spawnlist", "List the Uriel objects on the plot you stand in."));
                d.Commands("Collecting", c => c
                    .Player("uriel unlocks", ".uriel unlocks [player]", "Your unlocked objects and collection percentage. Admins can name a player to see theirs.")
                    .Player("uriel catalog", ".uriel catalog [page]", "Browse every placeable object, with totals (all and discoverable).")
                    .Player("uriel findprefab", ".uriel findprefab <text> [page]", "Search placeable objects by name.")
                    .Player("uriel notify", ".uriel notify <state>", "Turn your unlock messages on or off. State: on or off."));
            });

            s.Detail("Castle Events", d =>
            {
                d.Callout(CalloutKind.OffByDefault, "Needs CastleEvents.Enabled (off by default). Admins can use these on any plot. Castle owners and their clan can use them on their own plot only when CastleEvents.PlayerEvents is on (off by default); otherwise players get \"admin-only on this server\".");
                d.P("Layout commands act on the plot you stand in; door commands use the castle door within 6 m. Setup steps and rules are in <b>Admin guide → Castle Events</b>.");
                d.Commands("Layout", c => c
                    .Player("uriel event marker", ".uriel event marker <group> [slot] [unit] [count] [level] [contain]", "Place a marker at your feet in the group (its first marker creates it, on your floor). Slot: mob (default) or boss. Unit: a name or GUID for a fixed spawn, or - for the group's squad. Count and level apply to a fixed unit (0 = default). Contain: plot, room, storey, a leash in metres (3-60), a mix like room,15, or - for the server default. May cost an item.")
                    .Player("uriel event door", ".uriel event door <group>", "Bind the castle door nearest you (within 6 m) to the group. During a run it stays shut and locked until the group is defeated. One door per group.")
                    .Player("uriel event undoor", ".uriel event undoor <group>", "Unbind the group's door.")
                    .Player("uriel event squad", ".uriel event squad <group> <squad>", "Give the group its own squad, used when the run's template doesn't cover it. Use - for none.")
                    .Player("uriel event list", ".uriel event list", "Show this plot's layout: groups by floor, their doors and markers (with #numbers).")
                    .Player("uriel event remove", ".uriel event remove <what>", "Remove a marker (#number) or a whole group (name). Marker costs are refunded. Can't be undone; place the markers again.").Destructive());
                d.Commands("Runs", c => c
                    .Player("uriel event start", ".uriel event start", "Start the run with each group's own squad. May cost an item, not refunded.").Override("verbs from EventCommands.cs switch")
                    .Player("uriel event start", ".uriel event start <template>", "Start with a template, a floor template (every floor) or a squad (every group).").Override("verbs from EventCommands.cs switch")
                    .Player("uriel event start", ".uriel event start <template> <options>", "Start with run options, comma-separated, such as hard,auto+2,limit=15m. Use - as the template for each group's own squad.").Override("verbs from EventCommands.cs switch")
                    .Player("uriel event status", ".uriel event status", "The running event: clock, time left, bosses left, groups cleared, units left, doors.")
                    .Player("uriel event stop", ".uriel event stop", "End the run: its units are removed and every door goes back as it was.")
                    .Player("uriel event history", ".uriel event history", "The last 5 runs on this plot (result, time, time to the last boss) and the best times per template."));
                d.Detail("Library: squads", dd => dd.Commands("Squads", c => c
                    .Player("uriel squad create", ".uriel squad create <name>", "Create a squad (an admin's is server-wide, a player's is their own).")
                    .Player("uriel squad add", ".uriel squad add <name> <role> <units> [count] [pick] [level] [set]", "Add an entry. Role: mob or boss. Units: comma-separated names or GUIDs. Count 1-50 (default 1). Pick: any (default) or distinct. Level 0-200 (0 = the unit's own). Set: 1 by default; a new number starts another combination.")
                    .Player("uriel squad extend", ".uriel squad extend <name> <set> <entry> <units>", "Add more units to an entry's list, for lists too long for one chat line.")
                    .Player("uriel squad remove", ".uriel squad remove <name> <set> [entry]", "Remove one entry, or the whole set when the entry is 0 or left out.").Destructive()
                    .Player("uriel squad weight", ".uriel squad weight <name> <set> <weight>", "How often the set is rolled compared with the squad's other sets, 1-100 (default 1).")
                    .Player("uriel squad show", ".uriel squad show <name>", "Show the squad's sets and entries.")
                    .Player("uriel squad list", ".uriel squad list", "List the squads you can use.")
                    .Player("uriel squad delete", ".uriel squad delete <name>", "Delete a squad (yours, or any as admin). Can't be undone.").Destructive()));
                d.Detail("Library: floor templates and templates", dd =>
                {
                    dd.Commands("Floor templates", c => c
                        .Player("uriel floor create", ".uriel floor create <name>", "Create a floor template.")
                        .Player("uriel floor assign", ".uriel floor assign <name> <group> <squads>", "Give a group (or * for every group on the floor) a squad. Several, comma-separated, = one rolled per run. Use - to unassign.")
                        .Player("uriel floor show", ".uriel floor show <name>", "Show the floor template's groups and squads.")
                        .Player("uriel floor list", ".uriel floor list", "List the floor templates you can use.")
                        .Player("uriel floor delete", ".uriel floor delete <name>", "Delete a floor template (yours, or any as admin). Can't be undone.").Destructive());
                    dd.Commands("Templates", c => c
                        .Player("uriel template create", ".uriel template create <name>", "Create a castle-wide template.")
                        .Player("uriel template floor", ".uriel template floor <name> <floor> <plans>", "Give a floor (1, 2, … or * for every floor) a floor template. Several, comma-separated, = one rolled per floor per run. Use - to clear.")
                        .Player("uriel template options", ".uriel template options <name> <options>", "Default run options for the template, or - for none. Options typed at start add to them.")
                        .Player("uriel template show", ".uriel template show <name>", "Show the template's floors and options.")
                        .Player("uriel template list", ".uriel template list", "List the templates you can use.")
                        .Player("uriel template delete", ".uriel template delete <name>", "Delete a template (yours, or any as admin). Can't be undone.").Destructive());
                });
            });
        });

        // ------------------------------------------------------------------ 6. Admin guide
        b.Section(GuideSchema.Admin, false, s =>
        {
            s.Detail("Getting admin", d =>
            {
                d.Steps(
                    "Your account must be in the server's admin list.",
                    "Open the F1 console and run <b>adminauth</b>.",
                    "On any Uriel admin tab, press <b>Re-check admin</b>. The admin controls unlock.");
                d.P("The server checks admin on every admin command, whatever Raphael shows. Admins are never limited by spawn conditions, marker caps or costs, and can place objects and run events on any plot.");
            });
            s.Detail("What's off or limited by default", d =>
            {
                d.Bullets(
                    "<b>Castle Events</b> are off (CastleEvents.Enabled = false). Even when on, only admins can use them until CastleEvents.PlayerEvents is true.",
                    "<b>Object spawning</b> is on but admin-only (ObjectSpawn.AdminOnly = true). Set it false to open the player path (Discovery mode by default).",
                    "Non-destructible objects never unlock for players by play (ObjectSpawn.NonDestructibleUnlock = Off); only grants give them. The per-boss unlock map is off (BossUnlocksEnabled = false) and empty.",
                    "Public storage, prison cells, paid shares (PublicStorage.CostsEnabled), event doors (CastleEvents.DoorGates) and stair editing are on.",
                    "The whole mod is on (General.Enabled = true). The boot-time reset (KillSwitch.RestoreVanillaOnBoot) is off.",
                    "The experimental live stair refresh is off (StairSwap.ExperimentalLiveRefresh = false). Leave it off.");
            });

            s.Detail("Switches and the kill switch", d =>
            {
                d.P("Raphael's <b>Admin: Controls</b> tab shows these switches and sends these commands. Every cleanup command works even with its feature, or the whole mod, switched off.");
                d.Commands("Switches and cleanup", c => c
                    .Admin("uriel features", ".uriel features", "Show which Uriel features are switched on, read from " + Cfg + ".")
                    .Admin("uriel event stopall", ".uriel event stopall", "Stop every castle event on the server: their units are removed and their doors put back as they were.").Destructive()
                    .Admin("uriel purgeall", ".uriel purgeall [confirm]", "Remove every Uriel-spawned object on every plot. Native objects are never touched. Run once to arm, then add confirm within 30 seconds.").Destructive()
                    .Admin("uriel killswitch", ".uriel killswitch [confirm]", "Restore vanilla server-wide: stop every castle event (doors put back), remove every Uriel unit and Uriel-spawned object, and make every shared chest and prison cell private. Run once to arm, then add confirm within 30 seconds.").Destructive());
                d.Bullets(
                    "Swapped stairs stay as they are, and saved settings (layouts, the library, unlocks, the blocklist) are kept.",
                    "To turn a feature off, set its Enabled switch to false and restart. To turn the whole mod off, set General.Enabled = false; its cleanup commands keep working.",
                    "If the server can't be reached in game, set KillSwitch.RestoreVanillaOnBoot = true: the next start does the same reset, then Uriel sets the switch back to false so it runs once.");
            });

            s.Detail("Object spawning: setup", d =>
            {
                d.Steps(
                    "Try it as admin first: " + Cmd(".uriel findprefab chest") + ", then " + Cmd(".uriel spawn <name> here") + " inside a castle plot.",
                    "Block anything that misbehaves: " + Cmd(".uriel block <guid>") + ".",
                    "Pick the player model in the config: PlayerAccessMode (Discovery or Full), DiscoveryChancePercent, NonDestructibleUnlock, and an optional build cost (PrefabCostItem + PrefabCostStack).",
                    "Set player rules with " + Cmd(".uriel objcfgglobal") + " (every object) and " + Cmd(".uriel objcfg") + " (one object): per-plot limit, cost, and whether indestructible or respawning copies are allowed.",
                    "Set ObjectSpawn.AdminOnly = false and restart. Grant objects players can't discover with " + Cmd(".uriel grant") + " or " + Cmd(".uriel grantall") + ".");
                d.P("Spawn conditions (the object-spawn defaults) live in BepInEx/config/Uriel/object_conditions.json. A per-object value overrides the global one. Fields:");
                d.Bullets(
                    "<b>max</b> " + Arg("n") + ": most copies per plot; 0 = unlimited. Players only.",
                    "<b>cost</b> " + Arg("amount") + " " + Arg("itemGuid") + ": item cost to place one; amount 0 = free. Players only.",
                    "<b>indestructible</b> true|false|clear: whether players may place an indestructible copy; false forces breakable. Players only.",
                    "<b>respawn</b> true|false|clear: whether players may use the respawn flag. Players only.",
                    "<b>yoffset</b> " + Arg("metres") + ": a height correction added on every spawn and move so the object sits flush (0.4 raises, -0.3 lowers, 0 or clear removes). Applies to everyone, admins too.",
                    "<b>clear</b>: remove all of that layer's conditions. <b>show</b> (or no field): show them.");
            });
            s.Detail("Object spawning: admin commands", d =>
            {
                d.Commands("Unlocks and catalog control", c => c
                    .Admin("uriel grant", ".uriel grant <player> <prefab>", "Unlock an object (name or GUID) for a player, including non-destructible ones discovery can't reach.")
                    .Admin("uriel revoke", ".uriel revoke <player> <prefab>", "Remove an object from a player's unlocks.").Destructive()
                    .Admin("uriel grantall", ".uriel grantall <player> [mode]", "Unlock every placeable object for a player. Mode: all (default), destructible or indestructible.")
                    .Admin("uriel block", ".uriel block <prefab>", "Block an object (GUID or name) so it can't be spawned, discovered or granted. It leaves the catalog.")
                    .Admin("uriel unblock", ".uriel unblock <prefab>", "Allow a blocked object again. Use its GUID from the blocklist.")
                    .Admin("uriel blocklist", ".uriel blocklist", "List the blocked objects.")
                    .Admin("uriel bossmap", ".uriel bossmap <action> [vblood] [obj]", "Edit the boss unlock map (needs BossUnlocksEnabled). Action: add or remove with a V Blood and an object (defeating that V Blood unlocks the object), or list."));
                d.Commands("Spawn conditions", c => c
                    .Admin("uriel objcfg", ".uriel objcfg <prefab> [field] [v1] [v2]", "Set one object's spawn condition. Field: max, cost, indestructible, respawn, yoffset, clear or show (default). Example: " + Cmd(".uriel objcfg <name> cost 100 <itemGuid>") + ".")
                    .Admin("uriel objcfgglobal", ".uriel objcfgglobal show", "Show the global defaults every object uses unless it overrides them.").Override("verbs from ObjectSpawnService.cs ApplyCondition switch")
                    .Admin("uriel objcfgglobal", ".uriel objcfgglobal max <n>", "Global per-plot limit per object; 0 = unlimited.").Override("verbs from ObjectSpawnService.cs ApplyCondition switch")
                    .Admin("uriel objcfgglobal", ".uriel objcfgglobal cost <amount> <itemGuid>", "Global placement cost; amount 0 = free.").Override("verbs from ObjectSpawnService.cs ApplyCondition switch")
                    .Admin("uriel objcfgglobal", ".uriel objcfgglobal indestructible <true|false|clear>", "Whether players may place indestructible objects; false forces breakable.").Override("verbs from ObjectSpawnService.cs ApplyCondition switch")
                    .Admin("uriel objcfgglobal", ".uriel objcfgglobal respawn <true|false|clear>", "Whether players may use the respawn flag.").Override("verbs from ObjectSpawnService.cs ApplyCondition switch")
                    .Admin("uriel objcfgglobal", ".uriel objcfgglobal yoffset <metres>", "A height correction for every object (everyone, admins too).").Override("verbs from ObjectSpawnService.cs ApplyCondition switch")
                    .Admin("uriel objcfgglobal", ".uriel objcfgglobal clear", "Remove every global condition.").Override("verbs from ObjectSpawnService.cs ApplyCondition switch").Destructive()
                    .Admin("uriel objcfglist", ".uriel objcfglist", "List the global and per-object conditions."));
                d.Commands("Cleanup", c => c
                    .Admin("uriel purgeplot", ".uriel purgeplot", "Remove every Uriel object on the plot you stand in (live objects and records). Native objects are never touched. Can't be undone.").Destructive()
                    .Admin("uriel forcepurgeplot", ".uriel forcepurgeplot", "The same as purgeplot, kept as a second name.").Destructive()
                    .Admin("uriel forcedespawn", ".uriel forcedespawn [confirm]", "Remove the object you aim at even if Uriel has no record of it (for objects from older builds). Run once (it names the object), then add confirm within 30 seconds.").Destructive()
                    .Admin("uriel purgeorphans", ".uriel purgeorphans", "Scan the whole map and remove Uriel objects whose castle heart is gone. Only Uriel's objects are touched. The same cleanup also runs at boot and every 2 minutes.").Destructive());
            });

            s.Detail("Public storage: admin commands", d =>
            {
                d.P("Admins can also aim at any container and use " + Cmd(".uriel share") + " or " + Cmd(".uriel unshare") + " to change it directly.");
                d.Commands("Sharing control", c => c
                    .Admin("uriel sharedall", ".uriel sharedall [playerFilter]", "List every public container, or one player's (name or Steam ID).")
                    .Admin("uriel unshareplayer", ".uriel unshareplayer <nameOrId>", "Make all of one player's public containers private. Their rules are lost.").Destructive()
                    .Admin("uriel unshareall", ".uriel unshareall", "Make every public container private and clear the share list. Every rule is lost.").Destructive()
                    .Admin("uriel sharedebug", ".uriel sharedebug", "Show the aimed container's live sharing state (team, castle link, prisoner) for diagnosis."));
            });

            s.Detail("Stairs: admin commands", d =>
            {
                d.Commands("Stair cleanup", c => c
                    .Admin("uriel stairpurge", ".uriel stairpurge", "Destroy every stair entity within 5 m of you, to clear invisible \"ghost\" stairs from older builds. Restart the server afterwards. Can't be undone.").Destructive()
                    .Admin("uriel stairrefresh", ".uriel stairrefresh", "Experimental: write the aimed staircase's render state to the server log. It tries a live visual refresh only when StairSwap.ExperimentalLiveRefresh is on. The swap itself is not affected."));
            });

            s.Detail("Castle Events: setup", d =>
            {
                d.Callout(CalloutKind.Experimental, "Alpha. Tested by the developer on a development server with one player. Several players in one event, and players running events on their own castles, are not tested yet.");
                d.Steps(
                    "Set CastleEvents.Enabled = true (and PlayerEvents = true if castle owners should run their own) and restart.",
                    "Walk the castle. For each group, stand where foes should appear: " + Cmd(".uriel event marker g1 mob") + ", or " + Cmd(".uriel event marker g2 boss") + ".",
                    "Stand by the door the group guards: " + Cmd(".uriel event door g1") + ".",
                    "Make squads: " + Cmd(".uriel squad create militia5") + ", then " + Cmd(".uriel squad add militia5 mob CHAR_Militia_Guard,CHAR_Militia_Light 5 any") + ". For a boss, one of three: " + Cmd(".uriel squad add boss1of3 boss Foreman_VBlood,StoneBreaker_VBlood,Stalker_VBlood 1 distinct") + " (after creating boss1of3).",
                    "Floor template: " + Cmd(".uriel floor create f1") + ", " + Cmd(".uriel floor assign f1 g1 militia5") + ", " + Cmd(".uriel floor assign f1 g2 boss1of3") + ".",
                    "Template: " + Cmd(".uriel template create castle1") + ", " + Cmd(".uriel template floor castle1 1 f1") + ".",
                    "Run it: " + Cmd(".uriel event start castle1 hard,auto+2") + ". Check with " + Cmd(".uriel event status") + "; end it with " + Cmd(".uriel event stop") + ".");
                d.P("Raphael's <b>Castle Events</b> and <b>Event Library</b> tabs send these same commands.");
            });
            s.Detail("Castle Events: rules and limits", d =>
            {
                d.Bullets(
                    "Who: admins on any plot; castle owners (and their clan) on their own claimed, living plot when PlayerEvents is on. A player can't set up an event in someone else's castle or an abandoned one.",
                    "Players may place up to 40 markers per castle by default (MaxMarkersPerPlot). Admins have no limit.",
                    "Costs (players only, free by default): MarkerCostItem × MarkerCostAmount per mob marker and × BossMarkerCostAmount per boss marker, refunded when the marker is removed; RunCostItem × RunCostAmount per start, not refunded.",
                    "Caps: 60 Uriel units per plot, 300 on the server, 10 per unit-spawn command. A start that doesn't fit is refused before anything spawns.",
                    "Units are removed by a stop, a restart, the kill switch, or the game itself after UnitLifetimeSeconds (an hour by default).",
                    "Layouts, the library and run history are saved in BepInEx/config/Uriel/castle_events.json.");
            });
            s.Detail("Castle Events: unit tools (admin)", d =>
            {
                d.Callout(CalloutKind.Note, "Needs CastleEvents.Enabled, except the purge kill switch, which always works. Status, rooms, probe and probe map write their details to the server log.");
                d.Commands("Units", c => c
                    .Admin("uriel unit spawn", ".uriel unit spawn <prefab> [count] [level] [where] [contain]", "Spawn units into the plot you stand in, 2.5 m in front of you. Count default 1; level 0 = the unit's own; where: here (default) or aim; contain: plot (default), room, storey, a leash in metres (3-60) or a mix like room,15.")
                    .Admin("uriel unit list", ".uriel unit list", "Count the Uriel units on the server, per plot.")
                    .Admin("uriel unit kill", ".uriel unit kill [radius] [which]", "Kill Uriel units within the radius (default 15 m). Which: all (default), boss or mob. A real death: groups clear and doors open.").Destructive()
                    .Admin("uriel unit despawn", ".uriel unit despawn [radius]", "Remove the nearest Uriel unit within the radius (default 10 m). Not a defeat.")
                    .Admin("uriel unit clear", ".uriel unit clear", "Remove every Uriel unit on the plot you stand in.").Destructive()
                    .Admin("uriel unit purge", ".uriel unit purge [confirm]", "Kill switch: remove every Uriel unit on the server. Run once to arm, then add confirm within 30 seconds.").Destructive()
                    .Admin("uriel unit status", ".uriel unit status [radius]", "Log each Uriel unit within the radius (default 30 m): position, level, health, behaviour, target.")
                    .Admin("uriel unit rooms", ".uriel unit rooms", "Log the castle's rooms, storeys and doors, with a map per storey.")
                    .Admin("uriel unit probe", ".uriel unit probe [label]", "Read the tile map where you stand: height level, free or blocked, ground or air. The label tags the log line.").Override("verbs from UnitCommands.cs switch")
                    .Admin("uriel unit probe", ".uriel unit probe [label] aim", "The same reading at your aim point.").Override("verbs from UnitCommands.cs switch")
                    .Admin("uriel unit probemap", ".uriel unit probemap [label] [radius]", "Log an ASCII map of the tile map around you (radius 2-20, default 10).")
                    .Admin("uriel unit door", ".uriel unit door [action]", "Drive the castle door within 6 m on this plot: open, close, lock (no one can open it), unlock, or show (default)."));
            });

            s.Detail("Machine API (Raphael uses these)", d =>
            {
                d.P("Read-only commands Raphael sends silently. They answer with " + Cmd("[URIEL:…]") + " lines that Raphael reads and hides from chat. They need only ObjectSpawn.Enabled, so they work even while spawning is admin-only.");
                d.Commands("API", c => c
                    .Player("uriel api version", ".uriel api version", "The handshake: API version, plugin version, readiness, feature switches and catalog totals.")
                    .Player("uriel api catalog", ".uriel api catalog [page]", "One page of the full object catalog.")
                    .Player("uriel api unlocked", ".uriel api unlocked [page]", "One page of your unlocked objects and your collection percentage."));
            });
        });

        // ------------------------------------------------------------------ 7. Server configuration
        b.Section(GuideSchema.Config, false, s =>
        {
            s.P("File: <b>" + Cfg + "</b> on the server, created on first start. Uriel reads it at start-up, so <b>restart the server</b> after any change. Data lives next to it in <b>BepInEx/config/Uriel/</b>: spawned_objects.json, player_unlocks.json, blocked_prefabs.json, boss_unlocks.json, object_conditions.json, public_containers.json and castle_events.json.");
            s.Detail("[General] and [KillSwitch]", d => d.Settings("Master switch and reset", Cfg, r => r
                .Row("General", "Enabled", "true", "Master switch. False turns every Uriel feature off for everyone, admins included. The admin cleanup commands (killswitch, purgeall, unshareall, unit purge, event stopall) still work.", "restart")
                .Row("KillSwitch", "RestoreVanillaOnBoot", "false", "Emergency reset: the next start removes every Uriel-spawned object, makes every shared chest and cell private and puts back every door an event held. Uriel then sets it back to false itself, so it runs once. Saved settings are kept; swapped stairs stay.", "restart")));
            s.Detail("[ObjectSpawn]", d => d.Settings("Object spawning", Cfg, r => r
                .Row("ObjectSpawn", "Enabled", "true", "Master switch for object spawning and its API. False disables every object command.", "restart")
                .Row("ObjectSpawn", "AdminOnly", "true", "Only admins may use the object commands. Set false to open spawning and collecting to players.", "restart")
                .Row("ObjectSpawn", "PlayerAccessMode", "Discovery", "How players get objects when AdminOnly is false. Discovery: only objects they unlocked or were granted. Full: the whole catalog. Admins always have everything.", "restart")
                .Row("ObjectSpawn", "CollectionEnabled", "true", "Master switch for unlocking by destroying objects. False: no discovery and no unlock messages; grants and Full mode still work.", "restart")
                .Row("ObjectSpawn", "DiscoveryChancePercent", "25", "Chance (0-100) that destroying an eligible world object unlocks it for that player. 0 turns discovery off.", "restart")
                .Row("ObjectSpawn", "DiscoveryNotify", "true", "Tell a player in chat when they unlock an object. Each player can still turn it off with .uriel notify off.", "restart")
                .Row("ObjectSpawn", "NonDestructibleUnlock", "Off", "How players earn objects that can't be destroyed. Off: grants only. Collection: at 100% of the discoverable set. FinalBoss: on defeating Dracula. AllBosses: on defeating every main V Blood.", "restart")
                .Row("ObjectSpawn", "BossUnlocksEnabled", "false", "Defeating a V Blood listed in boss_unlocks.json grants its mapped objects. Build the map with .uriel bossmap; empty by default.", "restart")
                .Row("ObjectSpawn", "IncludeCastleBuildables", "false", "Also put the normal build-menu pieces (about 1200) in the catalog. Off keeps it to world objects. Admins can spawn any GUID either way.", "restart")
                .Row("ObjectSpawn", "PrefabCostItem", "0", "Item GUID a player pays to place any object. 0 = free. Find ids with .uriel finditem. Paid from the player's inventory.", "restart")
                .Row("ObjectSpawn", "PrefabCostStack", "0", "How many of PrefabCostItem one object costs. Ignored when PrefabCostItem is 0.", "restart")
                .Row("ObjectSpawn", "RefundOnRemove", "false", "Refund the paid cost when a player despawns their own object. Admin and free spawns refund nothing.", "restart")
                .Row("ObjectSpawn", "Indestructible", "true", "New objects are indestructible and decay-proof unless the spawn says breakable or smashable. Turn off on PvP servers so objects follow raid rules.", "restart")
                .Row("ObjectSpawn", "MaxTargetDistance", "8", "How close (metres) an object must be to your aim point or you for despawn, move, rotate, raise, lower and spawninfo.", "restart")
                .Row("ObjectSpawn", "PreventOverlap", "true", "Refuse to place or move an object onto a wall, station, prop or another spawned object (floors are fine), within about one storey. False allows free stacking, at your own risk: pile-ups can destabilise a server.", "restart")
                .Row("ObjectSpawn", "OverlapMinDistance", "0.5", "With PreventOverlap on, the minimum centre-to-centre spacing (metres) from nearby non-wall objects. Lower it toward 0 to place decor closer; 0 keeps only the same-tile check. Doesn't affect walls.", "restart")
                .Row("ObjectSpawn", "RespawnEnabled", "true", "Master switch for the respawn flag. False: destroyed respawn objects stay gone.", "restart")
                .Row("ObjectSpawn", "RespawnPollSeconds", "30", "How often (seconds, at least 5) destroyed respawn objects are brought back. Lower is snappier but scans more.", "restart")
                .Row("ObjectSpawn", "PurgeOrphansOnBoot", "true", "At start-up, remove Uriel objects whose castle heart is gone. False keeps them until an admin purges them.", "restart")
                .Row("ObjectSpawn", "AutoPurgeOrphans", "true", "Also remove orphaned Uriel objects during play, not only at boot. Only Uriel's own objects are touched.", "restart")
                .Row("ObjectSpawn", "OrphanPollSeconds", "120", "How often (seconds, at least 30) the in-play orphan sweep runs. It scans the whole map, so keep it infrequent.", "restart")));
            s.Detail("[PublicStorage]", d => d.Settings("Public storage", Cfg, r => r
                .Row("PublicStorage", "Enabled", "true", "Master switch for sharing containers. False disables share, unshare, info, pay chest and prisoner commands for players.", "restart")
                .Row("PublicStorage", "PrisonEnabled", "true", "Allow sharing prison cells and .uriel takeprisoner. Needs Enabled too.", "restart")
                .Row("PublicStorage", "CostsEnabled", "true", "Allow paid shares (the cost rule). False: no new price can be set, and a chest that already has a price stays closed until its owner shares it again without one.", "restart")
                .Row("PublicStorage", "MaxTargetDistance", "5", "How close (metres) your aim point must be to a container for the share commands.", "restart")));
            s.Detail("[StairSwap]", d => d.Settings("Stair editing", Cfg, r => r
                .Row("StairSwap", "Enabled", "true", "Allow stair restyling, .uriel stairstyles and .uriel removestairs.", "restart")
                .Row("StairSwap", "MaxTargetDistance", "6", "How close (metres) your aim point must be to a staircase for the stair commands.", "restart")
                .Row("StairSwap", "ExperimentalLiveRefresh", "false", "Experimental, admin testing only: lets .uriel stairrefresh attempt a live visual refresh. It can leave a staircase invisible until the next restart. Leave false.", "restart")
                .Row("StairSwap", "RespawnGapFrames", "5", "Frames between removing the old staircase and placing the new one in an earlier experimental restyle path. Uriel 0.22.0 doesn't read it; the live restyle ignores it.", "restart")));
            s.Detail("[CastleEvents]", d => d.Settings("Castle Events", Cfg, r => r
                .Row("CastleEvents", "Enabled", "false", "Experimental master switch for Castle Events and the .uriel unit tools. Spawned units are never saved.", "restart")
                .Row("CastleEvents", "PlayerEvents", "false", "Let castle owners set up and run events on their own plot. Admins can always, on any plot.", "restart")
                .Row("CastleEvents", "DoorGates", "true", "Let castle events take over castle doors. False: no door can be bound and runs never touch a door.", "restart")
                .Row("CastleEvents", "DefaultContain", "storey", "Containment for a marker that doesn't name one: plot, room, storey, a leash in metres, or a mix such as room,15. Units never leave the plot.", "restart")
                .Row("CastleEvents", "MaxMarkersPerPlot", "40", "Most markers a player's castle layout may have. Admins aren't limited.", "restart")
                .Row("CastleEvents", "MarkerCostItem", "0", "Item GUID a player pays per marker. 0 = free. Refunded when the marker is removed.", "restart")
                .Row("CastleEvents", "MarkerCostAmount", "0", "How many of MarkerCostItem a mob marker costs.", "restart")
                .Row("CastleEvents", "BossMarkerCostAmount", "0", "How many of MarkerCostItem a boss marker costs.", "restart")
                .Row("CastleEvents", "RunCostItem", "0", "Item GUID a player pays each time they start an event. 0 = free. Not refunded.", "restart")
                .Row("CastleEvents", "RunCostAmount", "0", "How many of RunCostItem one start costs.", "restart")
                .Row("CastleEvents", "MaxUnitsPerCommand", "10", "Most units one .uriel unit spawn may queue; larger counts are cut down.", "restart")
                .Row("CastleEvents", "MaxUnitsPerPlot", "60", "Most Uriel units alive on one castle plot at once.", "restart")
                .Row("CastleEvents", "MaxTrackedUnits", "300", "Most Uriel units alive on the whole server at once.", "restart")
                .Row("CastleEvents", "MaxSpawnsPerTick", "5", "Units created per spawn tick (about 6 ticks a second). Spreads big spawns over frames.", "restart")
                .Row("CastleEvents", "MaxDespawnsPerTick", "5", "Units removed per tick. Spreads big removals over frames.", "restart")
                .Row("CastleEvents", "UnitLifetimeSeconds", "3600", "Safety net: the game destroys a spawned unit after this many seconds, even if Uriel is removed.", "restart")
                .Row("CastleEvents", "ClearDrops", "true", "Spawned units drop no loot.", "restart")
                .Row("CastleEvents", "ContainCheckSeconds", "1", "How often units are checked against their containment. A unit outside is returned to its spawn spot.", "restart")));
            s.Detail("[Diagnostics]", d => d.Settings("Diagnostics", Cfg, r => r
                .Row("Diagnostics", "VerboseLogging", "false", "Write detailed per-action lines to the server log. Useful when testing; noisy in production.", "restart")));
        });

        // ------------------------------------------------------------------ 8. Troubleshooting
        b.Section(GuideSchema.Trouble, false, s =>
        {
            s.Detail("The Uriel tabs are greyed or missing", d =>
            {
                d.P("Raphael shows the group only after the server answers the " + Cmd(".uriel api version") + " handshake, which can lag a few seconds after you switch servers.");
                d.Bullets(
                    "Click the greyed <b>Uriel</b> rail header, then <b>Re-check now</b> (restarts detection) or <b>Force-enable tabs</b> (shows them for this session).",
                    "Or open <b>Settings and Help → Connection</b> and press <b>Re-detect Uriel</b>.",
                    "To always show them, set UrielAvailability = On in kdpen.Raphael.cfg.",
                    "If nothing helps, the server isn't running Uriel. When object spawning or the whole mod is off, the tabs still light up, with their buttons greyed.");
            });
            s.Detail("A button does nothing, or there is no reply", d =>
            {
                d.Bullets(
                    "Watch chat: Uriel answers every action there. No reply at all usually means the tab group is force-enabled on a server without Uriel.",
                    "\"Uriel is not yet initialized\": the server is still starting. Try again in a moment.",
                    "\"disabled by the server admin\" or a greyed button: that feature's switch is off (Raphael names it). Only the admin can change it.",
                    "Turn on <b>Uriel → Settings → Diagnostics</b> to log every command and reply to LogOutput.log (filter for " + Cmd("[Uriel]") + ") and send that to the admin or the mod author.");
            });
            s.Detail("Turning Uriel off, or restoring vanilla", d =>
                d.Steps(
                    "As admin, open <b>Admin: Controls</b> and press <b>Restore vanilla</b>, then <b>Confirm</b> within the countdown. The reply lists what was undone.",
                    "Then set General.Enabled = false (or the single feature's Enabled) and restart.",
                    "Can't get in game? Set KillSwitch.RestoreVanillaOnBoot = true and restart; it runs once."));
            s.Detail("\"Object spawning is admin-only on this server\"", d =>
                d.P("ObjectSpawn.AdminOnly is true (the default). Only the admin can open it to players. The catalog tabs can still load, because the machine API doesn't need it."));
            s.Detail("A spawn is refused", d =>
            {
                d.Bullets(
                    "\"can only be placed in a castle plot\": stand inside your own plot. From a Raphael button the object goes at your feet (here), so stand where you want it.",
                    "\"would overlap …\": something already occupies that tile. Step aside, or ask the admin to lower OverlapMinDistance.",
                    "Not unlocked, blocked, over the per-plot limit or missing the cost: check " + Cmd(".uriel unlocks") + " and the reply text.");
            });
            s.Detail("An object spawned but is invisible, floating or sunk", d =>
                d.Bullets(
                    "Invisible: it is there but doesn't render. Use <b>Despawn nearest</b> and report it so the admin can block it.",
                    "Floating or sunk: <b>Raise nearest</b> / <b>Lower nearest</b> move it 0.25 m per press. An admin can fix it for everyone with " + Cmd(".uriel objcfg <object> yoffset <metres>") + "."));
            s.Detail("Players can't open a newly shared chest or cell", d =>
                d.P("Their game hasn't picked up the change yet. They log out and back in once; after that it opens like any container."));
            s.Detail("Take prisoner fails", d =>
                d.Bullets(
                    "The cell must be shared (owners and admins excepted) and PublicStorage.PrisonEnabled must be on.",
                    "Stand next to the cell: the button targets the nearest cell.",
                    "Have Dominating Presence ready so the subdued prisoner follows you."));
            s.Detail("A stair restyle is refused or a ghost stair remains", d =>
                d.Bullets(
                    "You must own the staircase, and DLC styles need that DLC: the reply names the DLC. " + Cmd(".uriel stairstyles") + " shows what you can use.",
                    "A stray invisible stair from an older build: an admin runs " + Cmd(".uriel stairpurge") + " next to it, then restarts the server."));
            s.Detail("Castle Events problems", d =>
                d.Bullets(
                    "\"Castle events are disabled\": CastleEvents.Enabled is off. \"admin-only on this server\": PlayerEvents is off.",
                    "\"An event is running on this plot\": layout edits wait until " + Cmd(".uriel event stop") + ".",
                    "\"No castle door within 6 m\": stand right next to the door before binding it.",
                    "A long command was refused by Raphael (\"Not sent\"): the game cuts chat at about 120 characters. Shorten unit names, use GUIDs, or add units with Extend.",
                    "Units fighting each other: the run was started with faction=keep. Leave it out to put the run on one side.",
                    "Stray units after testing: " + Cmd(".uriel unit clear") + " for this plot, or " + Cmd(".uriel unit purge") + " then " + Cmd(".uriel unit purge confirm") + " for the whole server. A restart also removes them."));
        });

        b.DefaultsNote("Uriel is pre-1.0, so commands and config keys may still change between versions.");

        list.Add(b.Build());
    }
}
