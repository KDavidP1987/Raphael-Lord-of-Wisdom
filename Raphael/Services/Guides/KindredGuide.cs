using System.Collections.Generic;

namespace Raphael.Services.Guides;

// 0.73.0: the Kindred guide (KindredCommands 2.5.8 + KindredLogistics 1.6.0). A ShortMod page: what / tabs / commands /
// trouble. Checked against LearningMods/KindredCommands-main and LearningMods/KindredLogistics-main (Commands/*.cs,
// Services/*, Models/Database.cs) and against what MainPanel.cs + MainPanel.KindredAdmin.cs actually send.
internal static partial class Guides
{
    static partial void AddKindred(List<Guide> list)
    {
        static string A(string name) => GuideText.Arg(name);
        static string C(string text) => GuideText.Cmd(text);

        const string KL = "kindredlogistics";

        var b = new GuideBuilder("kindred", "Kindred guide",
            "Every KindredCommands and KindredLogistics command, the Raphael tabs that send them, and the fixes for common problems.",
            "KindredCommands 2.5.8 and KindredLogistics 1.6.0", GuideKind.ShortMod, "kindredcommands", "kindredlogistics");

        // ------------------------------------------------------------------------------------------ what
        b.Section(GuideSchema.What, true, s =>
        {
            s.Bullets(
                "<b>KindredCommands</b> adds server-admin chat commands: god mode and boosts, spawning, teleporting, clans, " +
                "castles, regions, bosses, soul shards, dropped items, servants, prisoners and timed announcements. A few " +
                "commands are open to every player, such as <b>.afk</b>, <b>.ping</b>, <b>.time</b> and <b>.staff</b>.",
                "<b>KindredLogistics</b> adds castle storage tools for players: stash your bag into your chests, pull " +
                "ingredients when you craft, find items, chain stations with conveyor chests, and auto-fill braziers, " +
                "devourers and unit spawners from named chests.",
                "Both mods are <b>server-side</b>. They are made by Odjit (KindredLogistics with Zfolmt). Raphael does not " +
                "change what they do; it gives their commands buttons and forms in the <b>Kindred</b> tab group.",
                "Most of KindredCommands is <b>admin only</b>. You must be on the server's admin list and authed " +
                "(type <b>adminauth</b> in the F1 console) before the server accepts those commands.",
                "Neither mod has a BepInEx .cfg file. They save their state as JSON and text files under " +
                "<b>BepInEx/config/KindredCommands/</b> and <b>BepInEx/config/KindredLogistics/</b> on the server. The " +
                "commands below edit those files for you.");
            s.Detail("What the server files hold", d =>
            {
                d.Bullets(
                    "<b>KindredCommands/settings.json</b>: server-wide options set by commands. Blood-bound items, headgear " +
                    "loss, soul shard limits, flight and durability, dropped-item lifetimes, everyone-daywalker, global bat " +
                    "vision, reveal-map-for-all, gruel and prisoner feed changes.",
                    "<b>KindredCommands/staff.json</b>: the staff list shown by <b>.staff</b> (set with <b>.setstaff</b>).",
                    "<b>KindredCommands/autoadmin.txt</b>: SteamIDs that are adminauthed automatically on login " +
                    "(set with <b>.autoadminauth</b>).",
                    "<b>KindredCommands/nospawn.json</b>: units banned from spawning (<b>.spawnban</b>). Delete a line here " +
                    "to lift a ban; there is no unban command.",
                    "<b>KindredCommands/announcements.json</b>, <b>boss.json</b>, <b>regions.json</b> and " +
                    "<b>maxLevels.json</b>: announcements, locked bosses, locked/gated regions with bans and allow-lists, " +
                    "and each player's highest level (used by region gates).",
                    "<b>KindredCommands/audit-" + C("<date>") + ".csv</b>: a log of every chat command used, one file per " +
                    "server start.",
                    "<b>KindredLogistics/playerSettings.json</b>: each player's personal toggles plus the server-wide " +
                    "toggles.");
                d.P("Edit a file by hand only while the server is stopped, or the mod may overwrite your change. " +
                    "<b>.reloadstaff</b> re-reads staff.json and autoadmin.txt without a restart.");
            });
        });

        // ------------------------------------------------------------------------------------------ tabs
        b.Section(GuideSchema.Tabs, false, s =>
        {
            s.P("The <b>Kindred</b> group has six tabs. Every control sends a chat command; the reply appears in chat. " +
                "Neither mod sends structured data back, so Raphael shows no live state for them.");
            s.Tabs(t => t
                .Row("Logistics",
                    "Your personal Logistics toggles (<b>Sort Stash</b>, <b>Craft Pull</b>, <b>Don't Pull Last</b>, " +
                    "<b>Servant Stash</b>, <b>Conveyor</b>, <b>Salvage</b>, <b>Unit Spawner</b>, <b>Brazier</b>, " +
                    "<b>Silent Pull</b>, <b>Silent Stash</b>), <b>Show Settings</b>, <b>Stash All</b>, and the " +
                    "<b>Pull item</b>, <b>Find item</b> and <b>Find chest</b> forms.",
                    ".l ss / cr / dpl / asm / co / sal / us / bz / sp / ssh / s, .stash, .pull <item> <quantity>, .fi <item>, .fc <name>")
                .Row("Logistics: Admin",
                    "Server-wide Logistics toggles, <b>Show Settings</b>, <b>Empty Trash</b> and the <b>Admin stash spawn</b> " +
                    "form. Greyed out until Raphael sees you are authed as admin; press <b>Re-check admin</b> after adminauth.",
                    ".lg ss / p / cr / asm / co / sal / us / bz / nam / trash / s, .emptytrash, .adminstash <item> <quantity>")
                .Row("Commands",
                    "The KindredCommands that any player can use: <b>AFK</b>, <b>Ping</b>, <b>Pace</b>, <b>Server Time</b>, " +
                    "<b>Online Staff</b>, <b>Open Plots</b>, <b>Soulshards</b>, <b>Boss List</b>, <b>Region List</b>, the " +
                    "clan list pager, and the <b>Check player level</b> and <b>List clan members</b> forms.",
                    ".afk, .ping, .pace, .time, .staff, .openplots, .gear soulshardstatus, .boss list, .region list, .clan list <page>, .checklevel <player>, .clan members <clanName>")
                .Row("Admin: Players",
                    "Commands aimed at a player (type the player's name; your own name to target yourself): revive, kill, stay down, " +
                    "heart count, teleport, reveal map, unlock, god and mortal, cooldowns, buffs, spectate, rename, unbind and " +
                    "swap, fly, give, blood potions, all 22 boosts, gear repair and break, clan add/kick/role, player info and " +
                    "SteamID tools. Greyed out until you are authed as admin.",
                    ".revivetarget, .revive, .killplayer, .staydown, .playerheartcount, .teleport, .revealmap, .unlock, .god, .mortal, .resetcooldown, .listbuffs, .spectate, .rename, .unbindplayer, .swapplayers, .fly, .flyup, .flydown, .flylevel, .flyheight, .flyobstacleheight, .buff, .debuff, .give, .bloodpotion, .bloodpotionmix, .bst ..., .gear repair / break / soulsharddurability, .clan add / kick / changerole, .playerinfo, .idcheck, .assignsteamID")
                .Row("Admin: Server",
                    "Server-wide controls: <b>Reveal map (all)</b>, <b>Clean lost shards</b>, <b>Daywalker (all)</b>, " +
                    "<b>Bat vision (all)</b>, set time, force respawn, announcements, dropped items, regions, boss locks, " +
                    "soul shard rules, clan rename, prisoners (feed and gruel settings too) and staff. Greyed out until you are authed as admin.",
                    ".revealmapforallplayers, .cleancontainerlessshards, .everyonedaywalker, .globalbatvision, .settime, .forcerespawn, .announce ..., .dropitems ..., .region ..., .boss lock / unlock / lockprimal / unlockprimal, .gear headgear / soulshardflight / togglesoulsharddropmanagement / destroyallshards / soulshardlimit / soulsharddurabilitytime, .clan rename, .prisoner gruel / grueltransform / feed / feeddefault / gruelsettings / feedsettings, .reloadstaff, .reloadadmin, .autoadminauth, .setstaff, .removestaff, .toggleadmin")
                .Row("Admin: World",
                    "<b>Where am I</b>, teleport horses, item and NPC search, spawning, boss level and teleport, castle " +
                    "tools (<b>Relocate Reset</b>, claim), servants, gear repair/break in a radius, <b>Longest offline</b>, " +
                    "clan castles and blood-bound items, plus heart tools (incoming decay, freeze and thaw) and plot lookups. Greyed out until you are authed as admin.",
                    ".whereami, .teleporthorse, .search item / npc, .spawnnpc, .customspawn, .customspawnat, .despawnnpc, .spawnhorse, .spawnban, .boss modify / modifyprimal / teleportto, .relocatereset, .claim, .servant ..., .gear repairall / breakall, .longestofflinecastles, .clan castles, .bloodbound add / remove, .incomingdecay, .freezeheart, .thawheart, .plotsowned, .frozenhearts, .clanplotsowned, .teleporttoplot, .plotinfo"));
            s.Callout(CalloutKind.Note,
                "Every admin form needs a player name where it asks for one. To target yourself, type your own name.<br>" +
                "• KindredCommands 2.5.8 itself disables " + C(".showhair") + ", " + C(".unbindall") + ", " + C(".wipe") +
                ", " + C(".commencewipe") + ", " + C(".cancelwipe") + " and " + C(".clan fix") + " (they are commented out " +
                "in its source), so Raphael has no buttons for them and typing them in chat does nothing.<br>" +
                "• Names with spaces, such as an announcement message or a clan name, are sent in quotes for you. A value " +
                "with a double quote or a backslash is refused.");
            s.P("<b>Boss List</b> (on <b>Commands</b>) and <b>List Locked Bosses</b> (on <b>Admin: World</b>) list only " +
                "the bosses an admin has locked, not every boss. Use <b>.search npc</b> to find boss prefab names.");
        });

        // ------------------------------------------------------------------------------------------ commands
        b.Section(GuideSchema.Commands, false, s =>
        {
            s.P("How to read a row: " + A("x") + " is required, " + C("[x]") + " is optional. A " + A("player") +
                " argument takes a character name (not case-sensitive) or a SteamID64. Where a player argument is optional, " +
                "leaving it out targets you. Put names with spaces in straight double quotes; quotes pasted from outside " +
                "the game are not recognised.");
            s.Callout(CalloutKind.AdminOnly,
                "Admin rows need you on the server's admin list and authed with <b>adminauth</b> in the F1 console. " +
                "Otherwise the server replies " + C("[denied]") + ".");

            // ---------------------------------------------------------------- KindredLogistics
            s.Detail("Logistics: stash and find (players)", d =>
            {
                d.P("These work only inside a castle territory you belong to. You can also stash by " +
                    "double-tapping R with your inventory open, or double-clicking the sort button when Sort Stash is on.");
                d.Commands("Stash and find", KL, c => c
                    .Player("stash", ".stash",
                        "Sends every item in your inventory except the hotbar to the chests in your territory.")
                    .Player("pull", ".pull <item> [quantity]",
                        "Takes an item out of your territory's chests into your inventory. " + A("quantity") + " defaults " +
                        "to 1. Fails with \"Pulling is globally disabled\" if an admin turned pull off.")
                    .Player("finditem", ".fi <item>",
                        "Lists the chests in your territory that hold the item.").Also(".finditem <item>")
                    .Player("findchest", ".fc <name>",
                        "Lists the chests whose name contains the text, for example " + C(".fc salvage") + ".")
                    .Also(".findchest <name>"));
            });

            s.Detail("Logistics: personal toggles (.logistics, short .l)", d =>
            {
                d.P("Each toggle flips your own setting and replies with its new state. A feature works only when both your " +
                    "toggle and the server-wide toggle are on. New players start with everything off except " +
                    "<b>Don't Pull Last</b>. Check yours with <b>.l s</b>.");
                d.Commands("Personal toggles", KL, c => c
                    .Player("logistics sortstash", ".l ss",
                        "Sort Stash: double-clicking the inventory sort button stashes your items.").Also(".logistics sortstash")
                    .Player("logistics craftpull", ".l cr",
                        "Craft Pull: right-click a recipe at a station to pull its missing ingredients from your chests.")
                    .Also(".logistics craftpull")
                    .Player("logistics dontpulllast", ".l dpl",
                        "Don't Pull Last: pulling leaves one of each item in the chest, so labelled chests keep their slot. " +
                        "On by default.").Also(".logistics dontpulllast")
                    .Player("logistics autostashmissions", ".l asm",
                        "Servant Stash: servants back from missions stash their loot into your chests, or into chests " +
                        "named \"spoils\".").Also(".logistics autostashmissions")
                    .Player("logistics conveyor", ".l co",
                        "Conveyor: moves items from sender chests and stations (name contains s1, s2…) to receivers with " +
                        "the same number (r1, r2…). Leftovers go to chests named \"overflow\".").Also(".logistics conveyor")
                    .Player("logistics salvage", ".l sal",
                        "Salvage: items placed in a chest named \"salvage\" are sent to a devourer.").Also(".logistics salvage")
                    .Player("logistics unitspawner", ".l us",
                        "Unit Spawner: a chest named \"spawner\" refills tombs, vermin nests and stygian spawners. The " +
                        "mod's own usage text says .l sp, but that is Silent Pull.").Also(".logistics unitspawner")
                    .Player("logistics brazier", ".l bz",
                        "Brazier: a chest named \"brazier\" refuels your braziers.").Also(".logistics brazier")
                    .Player("logistics silentpull", ".l sp",
                        "Silent Pull: stops the chat messages saying where pulled items came from.").Also(".logistics silentpull")
                    .Player("logistics silentstash", ".l ssh",
                        "Silent Stash: stops the chat messages saying where stashed items went.").Also(".logistics silentstash")
                    .Player("logistics settings", ".l s",
                        "Shows your toggles. \"Server Off\" means an admin has the feature off for everyone.")
                    .Also(".logistics settings"));
            });

            s.Detail("Logistics: server-wide toggles (admin, .logisticsglobal, short .lg)", d =>
            {
                d.P("Each toggle flips the setting for the whole server and replies with its new state. Defaults: Sort " +
                    "Stash, Pull, Craft Pull, Servant Stash, Conveyor, Salvage and Trash on; Unit Spawner, Brazier and " +
                    "Named off.");
                d.Commands("Server-wide toggles", KL, c => c
                    .Admin("logisticsglobal sortstash", ".lg ss", "Allows or blocks Sort Stash for everyone.")
                    .Also(".logisticsglobal sortstash")
                    .Admin("logisticsglobal pull", ".lg p", "Allows or blocks .pull for everyone. There is no personal toggle for pull.")
                    .Also(".logisticsglobal pull")
                    .Admin("logisticsglobal craftpull", ".lg cr", "Allows or blocks Craft Pull for everyone.")
                    .Also(".logisticsglobal craftpull")
                    .Admin("logisticsglobal autostashmissions", ".lg asm", "Allows or blocks servant mission auto-stash.")
                    .Also(".logisticsglobal autostashmissions")
                    .Admin("logisticsglobal conveyor", ".lg co", "Allows or blocks conveyor chests and stations.")
                    .Also(".logisticsglobal conveyor")
                    .Admin("logisticsglobal salvage", ".lg sal", "Allows or blocks \"salvage\" chests.")
                    .Also(".logisticsglobal salvage")
                    .Admin("logisticsglobal unitspawner", ".lg us", "Allows or blocks \"spawner\" chests. Off by default.")
                    .Also(".logisticsglobal unitspawner")
                    .Admin("logisticsglobal brazier", ".lg bz", "Allows or blocks \"brazier\" chests. Off by default.")
                    .Also(".logisticsglobal brazier")
                    .Admin("logisticsglobal named", ".lg nam",
                        "Allows named braziers: a brazier named \"prox\" burns only while an allied player is within 20 m, " +
                        "and \"night\" keeps it lit at night too. Off by default.").Also(".logisticsglobal named")
                    .Admin("logisticsglobal trash", ".lg trash", "Allows or blocks emptying chests named \"trash\".")
                    .Also(".logisticsglobal trash")
                    .Admin("logisticsglobal settings", ".lg s", "Shows every server-wide toggle.")
                    .Also(".logisticsglobal settings"));
            });

            s.Detail("Logistics: admin tools", d =>
            {
                d.Commands("Admin tools", KL, c => c
                    .Admin("adminstash", ".adminstash <item> [quantity]",
                        "Creates items and stashes them into the chests of the territory you stand in. " + A("quantity") +
                        " defaults to 1.")
                    .Admin("emptytrash", ".emptytrash",
                        "Deletes everything in every chest named \"trash\" in the territory you stand in. You must be allied " +
                        "with its heart, alive, out of PvP combat, and the castle must not be under attack. The items cannot " +
                        "be recovered.").Destructive());
            });

            // ---------------------------------------------------------------- KindredCommands: players
            s.Detail("KindredCommands: player commands", d =>
            {
                d.Commands("Anyone can use these", c => c
                    .Player("afk", ".afk", "Shows a sleeping animation and locks WASD movement. Use again to stop.")
                    .Player("ping", ".ping", "Shows your latency in milliseconds.").Also(".p")
                    .Override("the source's optional mode argument is never read (PingCommands.cs)")
                    .Player("pace", ".pace",
                        "Matches your walking speed to the nearest NPC so you can walk beside it. Use again to go back to " +
                        "normal. Fails if the NPC is too fast.")
                    .Player("time", ".time", "Shows the server's clock time.")
                    .Player("staff", ".staff", "Lists staff members who are online, with their rank.")
                    .Player("checklevel", ".cl <player>", "Shows the highest level the player has reached.")
                    .Also(".checklevel <player>")
                    .Player("openplots", ".op", "Counts open and decaying castle plots in each region.").Also(".openplots")
                    .Player("clan list", ".clan list [page]", "Lists clans on the server, newest first. " + A("page") +
                        " defaults to 1.").Also(".c l [page]")
                    .Player("clan members", ".clan members <clanName>", "Lists a clan's members and their roles.")
                    .Also(".c m <clanName>")
                    .Player("boss list", ".boss list", "Lists the bosses an admin has locked.").Also(".boss ls")
                    .Player("region list", ".region list",
                        "Lists locked and gated regions with their level gates.")
                    .Player("gear soulshardstatus", ".gear sss",
                        "Shows each soul shard type: how many exist, the drop limit and whether one can drop.")
                    .Also(".gear soulshardstatus"));
            });

            // ---------------------------------------------------------------- KindredCommands: admin
            s.Detail("Admin access and staff", d =>
            {
                d.Commands("Admin access and staff", c => c
                    .Admin("toggleadmin", ".toggleadmin <player>",
                        "Adds the player to the admin list and auths them, or removes and de-auths them.")
                    .Admin("reloadadmin", ".reloadadmin", "Saves and re-reads the server's admin list.")
                    .Admin("autoadminauth", ".autoadminauth",
                        "Adds or removes you from the auto-auth list, so you are adminauthed on every login.")
                    .Admin("setstaff", ".setstaff <player> <rank>",
                        "Lists the player as staff with a rank shown by .staff. Grants no powers.")
                    .Admin("removestaff", ".removestaff <player>", "Removes the player from the staff list.")
                    .Admin("reloadstaff", ".reloadstaff", "Re-reads staff.json and the other KindredCommands lists from disk."));
            });

            s.Detail("Player accounts and progress", d =>
            {
                d.Commands("Accounts and progress", c => c
                    .Admin("playerinfo", ".pinfo <player>",
                        "Shows SteamID, online status, last seen, highest level, clan, position and every castle with its " +
                        "territory index and heart time.").Also(".playerinfo <player>")
                    .Admin("idcheck", ".idcheck <steamid>", "Shows which character a SteamID is bound to.")
                    .Admin("assignsteamID", ".asid <player> <steamid>",
                        "Binds a SteamID to a character. The player who owns that SteamID logs into this character.")
                    .Also(".assignsteamID <player> <steamid>")
                    .Admin("unbindplayer", ".unbindplayer <player>",
                        "Detaches the player's SteamID from the character and kicks them; they make a new character on next " +
                        "login. The old body, name and castles stay. Undo with .swapplayers or .assignsteamID.").Destructive()
                    .Admin("swapplayers", ".swapplayers <player1> <player2>",
                        "Swaps the SteamIDs of two characters and kicks both. Run it again to swap back.")
                    .Admin("rename", ".rename <newName>",
                        "Renames you. Letters and digits only, up to 20 bytes, and not already taken.")
                    .Admin("rename", ".rename <player> <newName>",
                        "Renames another player. Clanmates may still see the old name on the map.")
                    .Admin("unlock", ".unlock <player>",
                        "Completes the player's journal and unlocks V Blood abilities, waypoints and the map. " +
                        "Does not unlock DLC. Cannot be undone.").Destructive()
                    .Admin("revealmap", ".revealmap [player]", "Reveals the whole map for the player. They must relog to see it.")
                    .Admin("revealmapforallplayers", ".revealmapforallplayers",
                        "Reveals the map for every player, now and for new players. Online players must relog. Saved in " +
                        "settings.json (RevealMapToAll)."));
            });

            s.Detail("Player state: god, buffs, revive, teleport", d =>
            {
                d.Commands("Player state", c => c
                    .Admin("god", ".god [player]",
                        "Gives every boost at once: attack speed 10, damage 10000, health 100000, speed 15, yield 10, bat " +
                        "vision, no aggro, no blood drain, no cooldowns, no durability loss, immaterial, invincible and " +
                        "shrouded.")
                    .Admin("mortal", ".mortal [player]", "Removes god mode and every boost.")
                    .Admin("spectate", ".spectate [player] [returnToStart]",
                        "Toggles invisible observer mode. When turned off with " + A("returnToStart") + " true (the " +
                        "default) the player goes back to where they started.")
                    .Admin("buff", ".buff <buff> [player] [duration] [immortal]",
                        "Applies a buff by name or PrefabGUID. " + A("duration") + ": 0 = the buff's normal length (default), " +
                        "-1 = permanent, otherwise seconds. " + A("immortal") + " true keeps it through death. Some buffs " +
                        "break things; test first.")
                    .Admin("debuff", ".debuff <buff> [player]", "Removes a buff by name or PrefabGUID.")
                    .Admin("listbuffs", ".listbuffs [player]", "Lists every buff on the player.")
                    .Admin("resetcooldown", ".cd [player]", "Resets all ability and skill cooldowns.").Also(".resetcooldown [player]")
                    .Admin("revive", ".revive [player]",
                        "Picks a downed player up. A fully dead player is sent to their coffin.")
                    .Admin("revivetarget", ".revivetarget", "Revives the downed player under your cursor.")
                    .Admin("killplayer", ".killplayer <player>", "Kills the player at once.")
                    .Destructive()
                    .Admin("staydown", ".staydown <player>",
                        "Downs the player; respawning does not get them up until someone revives them.")
                    .Admin("playerheartcount", ".playerheartcount <amount> <player>",
                        "Sets how many castle hearts the game thinks the player has. It corrects itself when they place a " +
                        "heart.")
                    .Admin("teleport", ".teleport <x> <y> <z> [player]",
                        "Teleports to world coordinates (" + A("y") + " is height). Get coordinates from .whereami."));
            });

            s.Detail("Flying", d =>
            {
                d.Commands("Flying", c => c
                    .Admin("fly", ".fly [player]", "Toggles flight.")
                    .Admin("flyup", ".f^ [player]", "Raises the flyer one floor.").Also(".flyup [player]")
                    .Admin("flydown", ".fv [player]", "Lowers the flyer one floor.").Also(".flydown [player]")
                    .Admin("flylevel", ".flylevel <floor> [player]", "Sets the flyer to a floor number.")
                    .Admin("flyheight", ".flyheight [height]", "Sets your own floor height. Default 30.")
                    .Admin("flyobstacleheight", ".flyobstacleheight [height]",
                        "Sets how far you rise above obstacles you fly into. Default 7."));
            });

            s.Detail("Boosts (.boost, short .bst)", d =>
            {
                d.P("Value boosts set a number until removed. Toggle boosts flip on or off each time. <b>.mortal</b> removes " +
                    "them all.");
                d.Commands("Value boosts", c => c
                    .Admin("boost attackspeed", ".bst attackspeed [speed] [player]",
                        "Sets an attack speed multiplier. Default 10.").Also(".bst as")
                    .Admin("boost removeattackspeed", ".bst removeattackspeed [player]", "Removes the attack speed boost.")
                    .Also(".bst ras")
                    .Admin("boost damage", ".bst damage [damage] [player]", "Sets a damage boost. Default 10000.").Also(".bst d")
                    .Admin("boost removedamage", ".bst removedamage [player]", "Removes the damage boost.").Also(".bst rd")
                    .Admin("boost health", ".bst health [health] [player]", "Sets a health boost. Default 100000.").Also(".bst h")
                    .Admin("boost removehealth", ".bst removehealth [player]", "Removes the health boost.").Also(".bst rh")
                    .Admin("boost speed", ".bst speed [speed] [player]", "Sets movement speed. Default 15.").Also(".bst s")
                    .Admin("boost removespeed", ".bst removespeed [player]", "Removes the speed boost.").Also(".bst rs")
                    .Admin("boost yield", ".bst yield [yield] [player]", "Sets a resource yield multiplier. Default 10.")
                    .Also(".bst y")
                    .Admin("boost removeyield", ".bst removeyield [player]", "Removes the yield boost.").Also(".bst ry"));
                d.Commands("Toggle boosts", c => c
                    .Admin("boost batvision", ".bst batvision [player]", "See creatures while in bat form.").Also(".bst bv")
                    .Admin("boost fly", ".bst fly [player]", "Flight.").Also(".bst f")
                    .Admin("boost noaggro", ".bst noaggro [player]", "Enemies ignore the player.").Also(".bst na")
                    .Admin("boost noblooddrain", ".bst noblooddrain [player]", "Blood does not drain.").Also(".bst nb")
                    .Admin("boost nocooldown", ".bst nocooldown [player]", "No ability cooldowns.").Also(".bst nc")
                    .Admin("boost nodurability", ".bst nodurability [player]", "Gear does not lose durability.").Also(".bst nd")
                    .Admin("boost immaterial", ".bst immaterial [player]", "Turns the immaterial effect on or off.").Also(".bst i")
                    .Admin("boost invincible", ".bst invincible [player]", "Takes no damage. Also gives sun immunity.")
                    .Also(".bst inv")
                    .Admin("boost shrouded", ".bst shrouded [player]", "Turns the shrouded effect on or off.").Also(".bst sh")
                    .Admin("boost suninvulnerable", ".bst suninvulnerable [player]", "Immune to sunlight.").Also(".bst suninv"));
                d.Commands("Boost info", c => c
                    .Admin("boost state", ".bst state [player]", "Lists the player's active boosts and values.")
                    .Admin("boost players", ".bst players", "Lists every boosted player."));
            });

            s.Detail("Items, potions and search", d =>
            {
                d.Commands("Items and blood potions", c => c
                    .Admin("give", ".give <item> [quantity]",
                        "Gives you an item by prefab name or PrefabGUID. " + A("quantity") + " defaults to 1.")
                    .Also(".g <item> [quantity]")
                    .Admin("bloodpotion", ".bp [type] [quality] [quantity]",
                        "Gives Blood Merlot. " + A("type") + " is a blood type (Frailed, Creature, Warrior, Rogue, Brute, " +
                        "Scholar, Worker, Mutant, Draculin, Corrupted…; default Frailed), " + A("quality") + " 0-100 " +
                        "(default 100).").Also(".bloodpotion [type] [quality] [quantity]")
                    .Admin("bloodpotionmix", ".bpm [primaryType] [primaryQuality] [secondaryType] [secondaryQuality] [secondaryTrait] [quantity]",
                        "Gives Blood Merlot with a secondary blood type. " + A("secondaryTrait") + " picks trait option 1-3.")
                    .Also(".bloodpotionmix ..."));
                d.Commands("Search (.search)", c => c
                    .Admin("search item", ".search item <search> [page]",
                        "Finds item prefab names containing the text, for .give and .bloodbound.").Also(".search i")
                    .Admin("search npc", ".search npc <search> [page]",
                        "Finds unit prefab names containing the text, for spawning and servants.").Also(".search n"));
                d.Commands("Blood-bound items (.bloodbound, short .blb)", c => c
                    .Admin("bloodbound add", ".blb add <descriptor>",
                        "Makes an item blood-bound (kept on death) by prefab name or PrefabGUID. Players' tooltips still " +
                        "show the old state.").Also(".bloodbound a <descriptor>")
                    .Admin("bloodbound remove", ".blb remove <descriptor>", "Makes an item drop on death again.")
                    .Also(".bloodbound r <descriptor>"));
            });

            s.Detail("Gear and soul shards (.gear)", d =>
            {
                d.P("Shard types for " + A("shardType") + ": TheMonster (or Adam), Solarus, WingedHorror, Megara, Dracula, " +
                    "or All.");
                d.Commands("Gear", c => c
                    .Admin("gear repair", ".gear repair [player]", "Fully repairs the player's gear.").Also(".gear r")
                    .Admin("gear break", ".gear break [player]", "Breaks all of the player's gear.").Also(".gear b")
                    .Admin("gear repairall", ".gear repairall [range]", "Repairs gear for every player within range. Default 10.")
                    .Also(".gear ra")
                    .Admin("gear breakall", ".gear breakall [range]", "Breaks gear for every player within range. Default 10.")
                    .Also(".gear ba")
                    .Admin("gear headgear", ".gear headgear", "Toggles whether headgear is lost on death.").Also(".gear hg"));
                d.Commands("Soul shards", c => c
                    .Admin("gear togglesoulsharddropmanagement", ".gear tssdm",
                        "Toggles KindredCommands' soul shard drop management (the limits below).")
                    .Also(".gear togglesoulsharddropmanagement")
                    .Admin("gear soulshardlimit", ".gear ssl <limit> [shardType]",
                        "Sets how many of a shard may exist before its boss stops dropping new ones (when relics are " +
                        "unique). Leave " + A("shardType") + " out to set all.").Also(".gear soulshardlimit")
                    .Admin("gear soulshardflight", ".gear ssf",
                        "Toggles whether carrying a soul shard blocks bat flight.").Also(".gear soulshardflight")
                    .Admin("gear soulsharddurability", ".gear ssd <durability> [player]",
                        "Sets the durability of every shard in the player's inventory.").Also(".gear soulsharddurability")
                    .Admin("gear soulsharddurabilitytime", ".gear ssdt [seconds]",
                        "Sets how many seconds shards last before breaking. Leave " + A("seconds") + " out to restore the " +
                        "game default.").Also(".gear soulsharddurabilitytime")
                    .Admin("gear destroyallshards", ".gear destroyallshards",
                        "Destroys every soul shard: equipped, in inventories and chests, on the ground and on pedestals. " +
                        "Cannot be undone.").Destructive());
            });

            s.Detail("Dropped items (.dropitems)", d =>
            {
                d.P("This group has no short form; type <b>.dropitems</b>. (The source declares its description in the " +
                    "short-form slot, so the only working short form is that whole sentence.) Lifetimes are saved in " +
                    "settings.json; item lifetimes do not apply to shards or player containers.");
                d.Commands("Dropped items", c => c
                    .Admin("dropitems lifetime", ".dropitems lifetime <seconds>",
                        "Sets how long dropped items last while players are nearby. The game's default is 300.")
                    .Also(".dropitems lt <seconds>")
                    .Admin("dropitems removelifetime", ".dropitems removelifetime", "Returns dropped items to the game's lifetime.")
                    .Also(".dropitems rlt")
                    .Admin("dropitems lifetimewhendisabled", ".dropitems lifetimewhendisabled [seconds]",
                        "Sets how long dropped items last when no one is nearby. Default 300.").Also(".dropitems ltwd")
                    .Admin("dropitems shardlifetime", ".dropitems shardlifetime [seconds]",
                        "Sets how long dropped soul shards last. Default 3600.").Also(".dropitems slt")
                    .Admin("dropitems clear", ".dropitems clear <radius>",
                        "Deletes dropped items within the radius around you. Cannot be undone.").Also(".dropitems c <radius>")
                    .Destructive()
                    .Admin("dropitems clearall", ".dropitems clearall", "Deletes every dropped item in the world. Cannot be undone.")
                    .Also(".dropitems ca").Destructive()
                    .Admin("dropitems clearshards", ".dropitems clearshards <radius>",
                        "Deletes dropped soul shards within the radius. Cannot be undone.").Also(".dropitems cs <radius>")
                    .Destructive()
                    .Admin("dropitems clearallshards", ".dropitems clearallshards",
                        "Deletes every dropped soul shard in the world. Cannot be undone.").Also(".dropitems cas")
                    .Destructive());
            });

            s.Detail("Spawning NPCs and horses", d =>
            {
                d.P("Find unit names with <b>.search npc</b>. Units on the spawn-ban list cannot be spawned.");
                d.Commands("Spawning", c => c
                    .Admin("spawnnpc", ".spwn <character> [count] [level]",
                        "Spawns units at your position. " + A("count") + " defaults to 1; " + A("level") + " -1 keeps the " +
                        "unit's own level.").Also(".spawnnpc <character> [count] [level]")
                    .Admin("customspawn", ".cspwn <unit> [type] [quality] [consumable] [duration] [level]",
                        "Spawns a unit at your position with a set blood type, quality 0-100 (default 0), whether it can be " +
                        "fed on (default true), lifetime in seconds (-1, the default, lasts until killed) and level.")
                    .Also(".customspawn ...")
                    .Admin("customspawnat", ".cspwnat <unit> <x> <y> <z> [type] [quality] [consumable] [duration] [level]",
                        "Same as .customspawn at world coordinates; " + A("y") + " is height.").Also(".customspawnat ...")
                    .Admin("despawnnpc", ".dspwn <character> [radius]",
                        "Kills every unit of that type within the radius (default 25). Expensive; use sparingly.")
                    .Also(".despawnnpc <character> [radius]").Destructive()
                    .Admin("spawnban", ".spawnban <character> <reason>",
                        "Bans a unit from .spawnnpc and .customspawn. Remove the ban by deleting its line in nospawn.json.")
                    .Admin("spawnhorse", ".sh <speed> <acceleration> <rotation> [num]",
                        "Spawns horses near you with the given stats. " + A("num") + " defaults to 1.")
                    .Also(".spawnhorse <speed> <acceleration> <rotation> [num]")
                    .Admin("teleporthorse", ".teleporthorse [radius]",
                        "Pulls tamed horses within the radius (default 5) to you, for stuck or desynced horses."));
            });

            s.Detail("Bosses (.boss)", d =>
            {
                d.P("Primal names: Lidia, Rufus, Errol, Quincey, Keely, Angram, Domina, Vincent, Octavian, Poloma, Ungora, " +
                    "Goreswine, Leandra, Bane, Kriig, Foulrot. <b>.boss list</b> is in the player commands.");
                d.Commands("Bosses", c => c
                    .Admin("boss lock", ".boss lock <boss>", "Stops a V Blood boss from spawning; tracking it says it is locked.")
                    .Also(".boss l <boss>")
                    .Admin("boss unlock", ".boss unlock <boss>", "Lets a locked boss spawn again.").Also(".boss u <boss>")
                    .Admin("boss lockprimal", ".boss lockprimal <primalBoss>", "Stops a primal boss from spawning.")
                    .Also(".boss lp <primalBoss>")
                    .Admin("boss unlockprimal", ".boss unlockprimal <primalBoss>", "Lets a locked primal boss spawn again.")
                    .Also(".boss up <primalBoss>")
                    .Admin("boss modify", ".boss modify <boss> <level>",
                        "Changes the level of the nearest copy of the boss until it respawns. Works during its blood walk.")
                    .Also(".boss m <boss> <level>")
                    .Admin("boss modifyprimal", ".boss modifyprimal <boss> <level>",
                        "Changes the level of the nearest copy of a primal boss until it respawns.").Also(".boss mp <boss> <level>")
                    .Admin("boss teleportto", ".boss teleportto <boss> [whichOne]",
                        "Teleports you to the boss. If several are up, add the number from the reply.")
                    .Also(".boss tt <boss> [whichOne]"));
            });

            s.Detail("Regions (.region)", d =>
            {
                d.P("Region names: start, farbane, dunley, cursed, hallowed, silverlight, gloomrot south, gloomrot north, " +
                    "mortium. Players already inside a region are not removed, but cannot come back once they leave. " +
                    "<b>lock</b> and <b>list</b> share the short form <b>l</b>: " + C(".region l") + " alone lists, " +
                    C(".region l dunley") + " locks. <b>.region list</b> is in the player commands.");
                d.Commands("Regions", c => c
                    .Admin("region lock", ".region lock <region>", "Stops anyone entering the region.")
                    .Admin("region unlock", ".region unlock <region>", "Opens a locked region.").Also(".region ul <region>")
                    .Admin("region gate", ".region gate <region> <level>",
                        "Stops players whose highest-ever level is below " + A("level") + " from entering.")
                    .Also(".region g <region> <level>")
                    .Admin("region ungate", ".region ungate <region>", "Removes a level gate.").Also(".region ug <region>")
                    .Admin("region allow", ".region allow <player>", "Lets the player enter every locked or gated region.")
                    .Also(".region a <player>")
                    .Admin("region remove", ".region remove <player>", "Takes the player off the allow list.")
                    .Also(".region r <player>")
                    .Admin("region listplayers", ".region listplayers", "Lists players on the allow list.").Also(".region lp")
                    .Admin("region ban", ".region ban <player> <region>", "Bans the player from one region.")
                    .Also(".region b <player> <region>")
                    .Admin("region unban", ".region unban <player> <region>", "Lifts a region ban.")
                    .Also(".region ub <player> <region>")
                    .Admin("region listbans", ".region listbans <region>", "Lists players banned from the region.")
                    .Also(".region lb <region>"));
            });

            s.Detail("Castles and plots", d =>
            {
                d.P("<b>.claim</b>, <b>.freezeheart</b>, <b>.thawheart</b> and <b>.relocatereset</b> act on the castle heart " +
                    "within 5 m of you. Territory indexes come from <b>.whereami</b> or <b>.playerinfo</b>.");
                d.Commands("Castles", c => c
                    .Admin("claim", ".claim [player]", "Gives the nearby castle heart to the player (you if left out).")
                    .Admin("freezeheart", ".freezeheart",
                        "Stops the nearby heart's timer so the castle never decays. Has no short form (a source quirk puts " +
                        "its description in that slot).")
                    .Admin("thawheart", ".thawheart", "Restarts the timer of a frozen heart.")
                    .Admin("frozenhearts", ".frozenhearts [page]", "Lists frozen hearts.")
                    .Admin("relocatereset", ".relocatereset", "Clears the relocation cooldown on the nearby heart.")
                    .Admin("incomingdecay", ".incd", "Lists the territories closest to decaying.").Also(".incomingdecay")
                    .Admin("longestofflinecastles", ".loc [page]",
                        "Lists castle owners who have been offline the longest.").Also(".longestofflinecastles [page]")
                    .Admin("plotsowned", ".po [page]", "Counts plots owned per player, most first.").Also(".plotsowned [page]")
                    .Admin("clanplotsowned", ".cpo [page]", "Counts plots owned per clan, most first.").Also(".clanplotsowned [page]")
                    .Admin("plotinfo", ".plotinfo <territoryIndex>", "Shows the owner and details of a territory.")
                    .Admin("teleporttoplot", ".tpp <territoryIndex>", "Teleports you to the territory's castle heart.")
                    .Also(".teleporttoplot <territoryIndex>"));
            });

            s.Detail("Clans (.clan, short .c)", d =>
            {
                d.P("<b>.clan list</b> and <b>.clan members</b> are in the player commands.");
                d.Commands("Clans", c => c
                    .Admin("clan add", ".clan add <playerToAdd> <clanName>",
                        "Puts a player who has no clan into an existing clan, even past the size limit.").Also(".c a")
                    .Admin("clan kick", ".clan kick <playerToRemove>",
                        "Removes a player from their clan. A leader cannot be kicked; change their role first.").Also(".c k")
                    .Admin("clan changerole", ".clan changerole <player> <newRole>",
                        "Sets a member's role: Member, Officer or Leader.").Also(".c cr")
                    .Admin("clan castles", ".clan castles <clanName>", "Lists castles owned by the clan.").Also(".c c")
                    .Admin("clan rename", ".clan rename <oldClanName> <newClanName> [leaderName]",
                        "Renames a clan. If several clans share the name, add the leader's name.").Also(".c rn"));
            });

            s.Detail("Servants (.servant)", d =>
            {
                d.P("Look at a servant coffin before using these; otherwise the reply is \"Not pointing at a servant coffin.\"");
                d.Commands("Servants", c => c
                    .Admin("servant convert", ".servant convert", "Finishes the conversion in progress.").Also(".servant c")
                    .Admin("servant perfect", ".servant perfect", "Gives the servant perfect expertise.").Also(".servant p")
                    .Admin("servant heal", ".servant heal", "Cures the servant's injuries.")
                    .Admin("servant revive", ".servant revive", "Revives a dead servant.").Also(".servant r")
                    .Admin("servant completemission", ".servant completemission", "Completes the servant's mission now.")
                    .Also(".servant cm")
                    .Admin("servant add", ".servant add <character>", "Puts a new servant of that type in an empty coffin.")
                    .Also(".servant a <character>")
                    .Admin("servant change", ".servant change <character>", "Replaces the coffin's servant with another type.")
                    .Also(".servant ch <character>"));
            });

            s.Detail("Prisoners (.prisoner)", d =>
            {
                d.P("Feed names for " + A("feed") + ": Blood Snapper, Corrupted Fish, Fat Goby, Fierce Stinger, Golden River " +
                    "Bass, Rainbow Trout, Rat, Sage Fish, Swamp Dweller, Twilight Snapper (part of a name is enough). " +
                    "Changes are saved in settings.json.");
                d.Commands("Prisoners", c => c
                    .Admin("prisoner gruel", ".prisoner gruel <chance> <min> <max>",
                        "Sets gruel's mutation chance and blood quality gain range, all in percent. Example: " +
                        C(".prisoner gruel 50 10 30") + ".")
                    .Admin("prisoner grueltransform", ".prisoner grueltransform <prefab>", "Sets the unit a mutated prisoner turns into.")
                    .Admin("prisoner gruelsettings", ".prisoner gruelsettings", "Shows the current gruel settings.")
                    .Admin("prisoner feed", ".prisoner feed <feed> <healthChangeMin> <healthChangeMax> <miseryChangeMin> <miseryChangeMax> <bloodQualityChangeMin> <bloodQualityChangeMax>",
                        "Sets how a feed item changes a prisoner's health, misery and blood quality.")
                    .Admin("prisoner feeddefault", ".prisoner feeddefault <feed>",
                        "Restores a feed item to the game's values. Your custom values are lost.").Destructive()
                    .Admin("prisoner feedsettings", ".prisoner feedsettings <feed>", "Shows a feed item's current values."));
            });

            s.Detail("Announcements (.announce)", d =>
            {
                d.Commands("Announcements", c => c
                    .Admin("announce add", ".announce add <name> <message> <time> [oneTime]",
                        "Broadcasts " + A("message") + " at a server time such as 12:00AM. Repeats daily unless " +
                        A("oneTime") + " is true; one-time messages can also name a date. Quote multi-word messages.")
                    .Also(".announce a")
                    .Admin("announce change", ".announce change <name> <message> <time> [oneTime]",
                        "Replaces an existing announcement.").Also(".announce c")
                    .Admin("announce list", ".announce list", "Lists announcements, soonest first.").Also(".announce l")
                    .Admin("announce remove", ".announce remove <name>", "Deletes an announcement.").Also(".announce r")
                    .Destructive());
            });

            s.Detail("World and server", d =>
            {
                d.Commands("World and server", c => c
                    .Admin("whereami", ".wai", "Shows your coordinates and the territory index you stand on (-1 off-plot).")
                    .Also(".whereami")
                    .Admin("settime", ".st <day> <hour>",
                        "Sets the game day and hour. Affects sunlight, castle hearts and horses.").Also(".settime <day> <hour>")
                    .Admin("forcerespawn", ".fr [range]",
                        "Makes spawn chains within range (default 10) respawn now, such as plants and nodes.")
                    .Also(".forcerespawn [range]")
                    .Admin("everyonedaywalker", ".ed", "Toggles sun immunity for every player, including new logins.")
                    .Also(".everyonedaywalker")
                    .Admin("globalbatvision", ".gbv", "Toggles letting every player see creatures in bat form.")
                    .Also(".globalbatvision")
                    .Admin("cleancontainerlessshards", ".cleancontainerlessshards",
                        "Destroys soul shards that are not in a container or equipped, with their map icons. A clean-up " +
                        "after old mods. Cannot be undone.").Destructive());
            });
        });

        // ------------------------------------------------------------------------------------------ trouble
        b.Section(GuideSchema.Trouble, false, s =>
        {
            s.Detail("The Kindred tabs are greyed out", d =>
            {
                d.P("Raphael cannot detect these mods, so it assumes they are present. The group greys out only when " +
                    "<b>KindredAvailability</b> is set to Off in <b>BepInEx/config/kdpen.Raphael.cfg</b> under " +
                    "<b>[GeneralOptions]</b>. Set it to Auto or On and reopen the panel.");
                d.P("<b>Logistics: Admin</b> and <b>Admin: Players</b> also grey their controls until you are authed as " +
                    "admin. Type <b>adminauth</b> in the F1 console, then press <b>Re-check admin</b> at the top of the tab.");
            });
            s.Detail("Commands are refused with [denied]", d =>
            {
                d.Bullets(
                    "Admin commands need you on the server's admin list and authed this session. Type <b>adminauth</b> in " +
                    "the F1 console after every login, or run <b>.autoadminauth</b> once to be authed automatically.",
                    "If you were just added to the list, an admin can run <b>.reloadadmin</b> so the server re-reads it.",
                    "<b>Admin: Server</b> and <b>Admin: World</b> stay clickable for everyone; the server still refuses " +
                    "non-admins.");
            });
            s.Detail("Nothing happens when I press a button", d =>
            {
                d.Bullets(
                    "The server may not run that mod. Typing the command yourself gives a clearer answer: an unknown " +
                    "command means the mod is missing.",
                    "Some Raphael controls send commands that do not exist in KindredCommands 2.5.8. See the warning under " +
                    "<b>Raphael's tabs</b> for the working commands.",
                    "Commands that act on something near you need you there: within 5 m of a castle heart, looking at a " +
                    "servant coffin, or with a player under your cursor for <b>.revivetarget</b>.");
            });
            s.Detail("A Logistics feature does not work", d =>
            {
                d.Bullets(
                    "Run <b>.l s</b>. \"Off\" means turn your own toggle on; \"Server Off\" means an admin must turn it on " +
                    "with <b>.lg</b>. Unit Spawner, Brazier and Named braziers start off server-wide.",
                    "Logistics works only inside a castle territory where you are allied with the heart, and not while " +
                    "downed, dead or in PvP combat.",
                    "Chest names must contain the keyword: salvage, spawner, brazier, trash, overflow or spoils. Conveyor " +
                    "groups use s1/r1, s2/r2 and so on in the chest or station name.",
                    "Pull leaves the last item in each chest while <b>Don't Pull Last</b> is on. Turn it off with " +
                    "<b>.l dpl</b> to take everything.");
            });
            s.Detail("A player or item name is not found", d =>
            {
                d.Bullets(
                    "Player arguments match the character name without case, or a SteamID64. Use quotes around names " +
                    "with spaces.",
                    "Item and unit arguments want prefab names. Find them with <b>.search item</b> and " +
                    "<b>.search npc</b>.",
                    "A blocked spawn says the unit is banned; the ban list is nospawn.json.");
            });
            s.Detail("Multiple commands match this input", d =>
            {
                d.P("Another server mod uses the same command or short form. The reply lists the matches; type the number " +
                    "it shows, such as " + C(".1") + ", or use the full command name.");
            });
        });

        list.Add(b.Build());
    }
}
