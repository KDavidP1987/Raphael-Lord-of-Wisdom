using Raphael.Services;
using Raphael.UI.Forms;
using Raphael.UI.Framework.UniverseLib.UI;
using TMPro;
using UnityEngine;
using Raphael.UI.Framework.CustomLib.Util;
using KAC = Raphael.Services.Kindred.KindredAdminCommands;

namespace Raphael.UI.ModContent;

// Phase 5i — KindredCommands admin command surface (146 commands), split into
// three sub-tabs (Players / Server / World). Each Build method on the
// MainPanel partial below adds an intro warning, then walks the commands
// organized by command group, surfacing zero-arg commands as
// AddCommandButton rows and arg-taking commands as collapsible FormBuilder
// forms.
//
// 0.73.0: every command string comes from Services/Kindred/KindredAdminCommands
// (KAC), which the logic tests check against the KindredCommands 2.5.8 manifest.
// Form field names are the source parameter names, so they match the template
// placeholders. Free-text fields are built with KText so their quoting comes
// from KAC.IsQuoted. Player fields are required (FormField.IsValid rejects a
// blank), so they are PlayerNameFields with the "Player name" hint — the old
// "blank for self" hints were never true.
public partial class MainPanel
{
    // -----------------------------------------------------------------------
    // Admin: Players sub-tab
    // -----------------------------------------------------------------------

    private void BuildKindredAdminPlayersTab(GameObject page)
    {
        page = BeginAdminGate(page);   // gray out + disable the admin controls below for non-admins
        RenderAdminInfoNote(page, "Kindred admin (Players)");
        AddAdminWarningIntro(page,
            "Player-targeting admin commands. Most take a player name; type " +
            "your own name to target yourself. Requires KindredCommands + " +
            "server admin permission.");

        // ---- General (no group) ------------------------------------------
        AddSpacer(page, 4);
        AddSectionHeading(page, "General");

        var genRow = AddKLRow(page, "KCAGenRow1");
        AddCommandButton(genRow, "Revive Target", KAC.ReviveTarget,
            "Revive the dead player you are currently targeting (.revivetarget).");

        AddPlayerCollapse(page, "Revive (.revive)",
            "Revive a player from death.",
            KAC.Revive);
        AddPlayerCollapse(page, "Kill player (.killplayer)",
            "Instantly kill the specified player.",
            KAC.KillPlayer);
        AddPlayerCollapse(page, "Stay down (.staydown)",
            "Down the target player until revived.",
            KAC.StayDown);

        CollapsibleSection.Build(page,
            title: "Set heart count (.playerheartcount)",
            startExpanded: false,
            tooltip: "Set how many soul-shard 'hearts' a player counts as on the server.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set heart count",
                commandTemplate: KAC.PlayerHeartCount,
                new IntField("amount", "Heart count", min: 0, max: 99,
                    tooltip: "Number of soul-shard hearts."),
                new PlayerNameField("player", "Player",
                    tooltip: "Player to apply to. Required for this command.")));

        CollapsibleSection.Build(page,
            title: "Teleport player to coords (.teleport)",
            startExpanded: false,
            tooltip: "Teleport a player to specific X/Y/Z world coordinates.",
            buildContent: c => FormBuilder.Build(c,
                title: "Teleport to coords",
                commandTemplate: KAC.Teleport,
                KText(KAC.Teleport, "x", "X", tooltip: "World X coordinate."),
                KText(KAC.Teleport, "y", "Y", tooltip: "World Y coordinate."),
                KText(KAC.Teleport, "z", "Z", tooltip: "World Z coordinate."),
                new PlayerNameField("player", "Player",
                    tooltip: "Player to teleport. Type your own name to teleport yourself.")));

        AddPlayerCollapse(page, "Reveal map (.revealmap)",
            "Reveal the entire map for a player.",
            KAC.RevealMap);
        AddPlayerCollapse(page, "Unlock skills/journal (.unlock)",
            "Unlock all skills, journal entries, and abilities for a player.",
            KAC.Unlock);
        AddPlayerCollapse(page, "God mode on (.god)",
            "Enable full god mode for the target.",
            KAC.God);
        AddPlayerCollapse(page, "Mortal (god mode off) (.mortal)",
            "Remove god mode and admin boosts from the target.",
            KAC.Mortal);
        AddPlayerCollapse(page, "Reset cooldowns (.resetcooldown)",
            "Instantly reset all ability cooldowns for the target.",
            KAC.ResetCooldown);
        AddPlayerCollapse(page, "List active buffs (.listbuffs)",
            "List the buffs currently applied to the target.",
            KAC.ListBuffs);

        CollapsibleSection.Build(page,
            title: "Spectate (.spectate)",
            startExpanded: false,
            tooltip: "Toggle spectate mode following a player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Spectate",
                commandTemplate: KAC.Spectate,
                new PlayerNameField("player", "Player",
                    tooltip: "Player to spectate."),
                new BoolField("returnToStart", "Return to start when done",
                    defaultValue: true,
                    tooltip: "If true, snaps you back to your starting position when spectate ends.")));

        // ---- Rename / Unbind / Swap -----------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Identity");

        CollapsibleSection.Build(page,
            title: "Rename self (.rename)",
            startExpanded: false,
            tooltip: "Rename your own character.",
            buildContent: c => FormBuilder.Build(c,
                title: "Rename self",
                commandTemplate: KAC.RenameSelf,
                KText(KAC.RenameSelf, "newName", "New name",
                    tooltip: "Your new character name.")));
        CollapsibleSection.Build(page,
            title: "Rename player (.rename)",
            startExpanded: false,
            tooltip: "Rename another player's character.",
            buildContent: c => FormBuilder.Build(c,
                title: "Rename player",
                commandTemplate: KAC.RenamePlayer,
                new PlayerNameField("player", "Player"),
                KText(KAC.RenamePlayer, "newName", "New name")));
        CollapsibleSection.Build(page,
            title: "Unbind player save (.unbindplayer)",
            startExpanded: false,
            tooltip: "Unbinds a SteamID from a player save so the slot can be re-used.",
            buildContent: c => FormBuilder.Build(c,
                title: "Unbind player",
                commandTemplate: KAC.UnbindPlayer,
                new PlayerNameField("player", "Player")));
        CollapsibleSection.Build(page,
            title: "Swap two players' SteamIDs (.swapplayers)",
            startExpanded: false,
            tooltip: "Switches the SteamIDs of two players. Useful for recovering lost characters.",
            buildContent: c => FormBuilder.Build(c,
                title: "Swap players",
                commandTemplate: KAC.SwapPlayers,
                new PlayerNameField("player1", "Player 1"),
                new PlayerNameField("player2", "Player 2")));

        // ---- Fly group ----------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Fly");

        AddPlayerCollapse(page, "Toggle fly (.fly)",
            "Toggle fly mode for a player.",
            KAC.Fly);
        AddPlayerCollapse(page, "Fly up one floor (.flyup)",
            "Increase fly height by one floor.",
            KAC.FlyUp);
        AddPlayerCollapse(page, "Fly down one floor (.flydown)",
            "Decrease fly height by one floor.",
            KAC.FlyDown);

        CollapsibleSection.Build(page,
            title: "Fly level (.flylevel)",
            startExpanded: false,
            tooltip: "Set fly height to a specific floor level.",
            buildContent: c => FormBuilder.Build(c,
                title: "Fly to level",
                commandTemplate: KAC.FlyLevel,
                new IntField("floor", "Floor", min: 0, max: 99,
                    tooltip: "Floor number to fly to."),
                new PlayerNameField("player", "Player")));
        CollapsibleSection.Build(page,
            title: "Set fly height (.flyheight)",
            startExpanded: false,
            tooltip: "Set the absolute fly height for yourself (default 30).",
            buildContent: c => FormBuilder.Build(c,
                title: "Set fly height",
                commandTemplate: KAC.FlyHeight,
                new IntField("height", "Height", min: 1, max: 500,
                    tooltip: "Fly height in world units. Default 30.")));
        CollapsibleSection.Build(page,
            title: "Set fly obstacle height (.flyobstacleheight)",
            startExpanded: false,
            tooltip: "Set the height to fly above any obstacles (default 7).",
            buildContent: c => FormBuilder.Build(c,
                title: "Set obstacle height",
                commandTemplate: KAC.FlyObstacleHeight,
                new IntField("height", "Height", min: 0, max: 100,
                    tooltip: "Auto-rise height when an obstacle is below. Default 7.")));

        // ---- Buffs & items -----------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Buffs & items");

        CollapsibleSection.Build(page,
            title: "Apply buff (.buff)",
            startExpanded: false,
            tooltip: "Apply a named buff to a player. Buff names match the server's prefab lookup.",
            buildContent: c => FormBuilder.Build(c,
                title: "Apply buff",
                commandTemplate: KAC.Buff,
                KText(KAC.Buff, "buff", "Buff prefab",
                    tooltip: "Buff prefab name or ID. Use .search npc on the server for hints."),
                new PlayerNameField("player", "Player"),
                new IntField("duration", "Duration (seconds, 0=infinite)", min: 0, max: 86400),
                new BoolField("immortal", "Immortal flag",
                    tooltip: "If true the buff cannot be cleansed.")));

        CollapsibleSection.Build(page,
            title: "Remove buff (.debuff)",
            startExpanded: false,
            tooltip: "Remove a specific buff from a player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Remove buff",
                commandTemplate: KAC.Debuff,
                KText(KAC.Debuff, "buff", "Buff prefab"),
                new PlayerNameField("player", "Player")));

        CollapsibleSection.Build(page,
            title: "Give item (.give)",
            startExpanded: false,
            tooltip: "Spawn an item directly into your inventory.",
            buildContent: c => FormBuilder.Build(c,
                title: "Give item",
                commandTemplate: KAC.Give,
                KText(KAC.Give, "item", "Item",
                    tooltip: "Item prefab/name. Use .search item to look up names."),
                new IntField("quantity", "Quantity", min: 1, max: 9999)));

        CollapsibleSection.Build(page,
            title: "Create blood potion (.bloodpotion)",
            startExpanded: false,
            tooltip: "Spawn a blood potion of the given blood type and quality.",
            buildContent: c => FormBuilder.Build(c,
                title: "Create blood potion",
                commandTemplate: KAC.BloodPotion,
                new EnumField<PlayerStateService.BloodType>("type", "Blood type",
                    defaultValue: PlayerStateService.BloodType.Warrior),
                new IntField("quality", "Quality (1-100)", min: 1, max: 100),
                new IntField("quantity", "Quantity", min: 1, max: 99)));

        CollapsibleSection.Build(page,
            title: "Create mixed blood potion (.bloodpotionmix)",
            startExpanded: false,
            tooltip: "Spawn a mixed blood potion blending two blood types.",
            buildContent: c => FormBuilder.Build(c,
                title: "Create mixed potion",
                commandTemplate: KAC.BloodPotionMix,
                new EnumField<PlayerStateService.BloodType>("primaryType", "Primary type",
                    defaultValue: PlayerStateService.BloodType.Warrior),
                new IntField("primaryQuality", "Primary quality", min: 1, max: 100),
                new EnumField<PlayerStateService.BloodType>("secondaryType", "Secondary type",
                    defaultValue: PlayerStateService.BloodType.Scholar),
                new IntField("secondaryQuality", "Secondary quality", min: 1, max: 100),
                new IntField("secondaryTrait", "Secondary trait #", min: 1, max: 4),
                new IntField("quantity", "Quantity", min: 1, max: 99)));

        // ---- Boost group (.bst) -------------------------------------------
        AddSpacer(page, 6);
        CollapsibleSection.Build(page,
            title: "Boost (.bst) - 22 commands",
            startExpanded: false,
            tooltip: "Per-player stat boosts and admin toggles. Each takes a player name.",
            buildContent: BuildBoostGroup);

        // ---- Gear (player-targeting) --------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Gear");
        AddPlayerCollapse(page, "Repair all gear (.gear repair)",
            "Repair every piece of gear the target is wearing.",
            KAC.GearRepair);
        AddPlayerCollapse(page, "Break all gear (.gear break)",
            "Drop the target's gear durability to zero.",
            KAC.GearBreak);
        // 0.73.0: durability is a float 0-1 in KindredCommands; was an int field that could only send 0 or 1.
        CollapsibleSection.Build(page,
            title: "Set soulshard durability (.gear soulsharddurability)",
            startExpanded: false,
            tooltip: "Set the soulshard durability for a player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set soulshard durability",
                commandTemplate: KAC.GearSoulshardDurability,
                new DecimalField("durability", "Durability (0-1)", min: 0, max: 1, maxDecimals: 3,
                    tooltip: "A value from 0 to 1, up to 3 decimal places (for example 0.5 = half)."),
                new PlayerNameField("player", "Player")));

        // ---- Clan (player ops) --------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Clan");
        CollapsibleSection.Build(page,
            title: "Add player to clan (.clan add)",
            startExpanded: false,
            tooltip: "Force-add a player to a clan.",
            buildContent: c => FormBuilder.Build(c,
                title: "Add to clan",
                commandTemplate: KAC.ClanAdd,
                new PlayerNameField("playerToAdd", "Player"),
                KText(KAC.ClanAdd, "clanName", "Clan name")));
        CollapsibleSection.Build(page,
            title: "Kick player from clan (.clan kick)",
            startExpanded: false,
            tooltip: "Remove a player from their current clan.",
            buildContent: c => FormBuilder.Build(c,
                title: "Kick from clan",
                commandTemplate: KAC.ClanKick,
                new PlayerNameField("playerToRemove", "Player")));
        CollapsibleSection.Build(page,
            title: "Change clan role (.clan changerole)",
            startExpanded: false,
            tooltip: "Change a player's role within their clan (Member / Officer / Leader).",
            buildContent: c => FormBuilder.Build(c,
                title: "Change clan role",
                commandTemplate: KAC.ClanChangeRole,
                new PlayerNameField("player", "Player"),
                KText(KAC.ClanChangeRole, "newRole", "Role",
                    tooltip: "Role name (e.g. Member, Officer, Leader). Match what KindredCommands expects.")));

        // ---- Player info / lookup (0.5.0 audit gaps) ---------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Lookup");
        CollapsibleSection.Build(page,
            title: "Player info (.playerinfo)",
            startExpanded: false,
            tooltip: "Display detailed info about a specific player. Reply appears in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Player info",
                commandTemplate: KAC.PlayerInfo,
                new PlayerNameField("player", "Player")));
        CollapsibleSection.Build(page,
            title: "Search player by SteamID (.idcheck)",
            startExpanded: false,
            tooltip: "Look up which character is associated with a given SteamID.",
            buildContent: c => FormBuilder.Build(c,
                title: "ID check",
                commandTemplate: KAC.IdCheck,
                KText(KAC.IdCheck, "steamid", "SteamID", placeholder: "76561198…")));
        CollapsibleSection.Build(page,
            title: "Assign SteamID to player (.assignsteamID)",
            startExpanded: false,
            tooltip: "Link a player record to a different SteamID. Recovery use — most servers will never need this.",
            buildContent: c => FormBuilder.Build(c,
                title: "Assign SteamID",
                commandTemplate: KAC.AssignSteamId,
                new PlayerNameField("player", "Player"),
                KText(KAC.AssignSteamId, "steamid", "SteamID", placeholder: "76561198…")));

        // ---- Prisoner config readouts (paired with the .prisoner setters) -
        // 0.73.0: both live in the .prisoner group; .feedsettings needs a feed. The "Cosmetic" (.showhair) and
        // "Mass action" (.unbindall) sections were removed: KindredCommands 2.5.8 ships those commands commented out.
        AddSpacer(page, 6);
        AddSectionHeading(page, "Prisoner config (read)");
        var diagRow = AddKLRow(page, "PrisonerDiag");
        AddCommandButton(diagRow, "Gruel Settings", KAC.PrisonerGruelSettings,
            "Print current gruel-conversion config (.prisoner gruelsettings).");
        CollapsibleSection.Build(page,
            title: "Feed settings (.prisoner feedsettings)",
            startExpanded: false,
            tooltip: "Print the current config for one prisoner feed.",
            buildContent: c => FormBuilder.Build(c,
                title: "Feed settings",
                commandTemplate: KAC.PrisonerFeedSettings,
                KText(KAC.PrisonerFeedSettings, "feed", "Feed name",
                    tooltip: FeedNamesTooltip)));
    }

    // 0.73.0: KindredCommands' FoundPrisonerFeed names (Commands/Converters/FoundPrisonerFeed.cs). Part of a name is
    // enough when it matches only one.
    private const string FeedNamesTooltip =
        "Feed name: Blood Snapper, Corrupted Fish, Fat Goby, Fierce Stinger, Golden River Bass, Rainbow Trout, Rat, " +
        "Sage Fish, Swamp Dweller or Twilight Snapper. Part of a name works if it matches only one.";

    // 0.73.0: a free-text field for a Kindred template. Quoting and "may be blank" come from KindredAdminCommands
    // (checked by the logic tests against the source), never from UI code.
    private static TextField KText(string template, string name, string label, string placeholder = "", string tooltip = null)
        => new TextField(name, label, placeholder, tooltip)
        {
            Quote = KAC.IsQuoted(template, name),
            Optional = KAC.IsBlankable(template, name),
        };

    // Helper: build a CollapsibleSection wrapping a 1-field form for the very
    // common "player target" command pattern. 0.73.0: the player is always
    // required (the old optional "blank for self" variant was rejected on submit).
    private static void AddPlayerCollapse(GameObject page, string title, string tooltip, string template)
    {
        CollapsibleSection.Build(page,
            title: title,
            startExpanded: false,
            tooltip: tooltip,
            buildContent: c => FormBuilder.Build(c,
                title: title,
                commandTemplate: template,
                new PlayerNameField("player", "Player")));
    }

    // The boost group has 22 closely-related commands; rather than 22 top-level
    // CollapsibleSections, wrap them all in one outer collapsible and build a
    // tighter list inside.
    private void BuildBoostGroup(GameObject c)
    {
        var row = AddKLRow(c, "BstZeroArg");
        AddCommandButton(row, "List Boosted", KAC.BstPlayers,
            "List all currently boosted players (.bst players).");

        // 0.73.0: the value field is named after each command's own parameter (speed / damage / health / yield).
        AddBstValueForm(c, "Attack speed (.bst attackspeed)",
            KAC.BstAttackSpeed, "speed", "Attack speed multiplier (default 10).", 10);
        AddBstTogglePlayer(c, "Remove attack speed (.bst removeattackspeed)",
            KAC.BstRemoveAttackSpeed);
        AddBstValueForm(c, "Damage (.bst damage)",
            KAC.BstDamage, "damage", "Bonus damage value (default 10000).", 10000);
        AddBstTogglePlayer(c, "Remove damage (.bst removedamage)",
            KAC.BstRemoveDamage);
        AddBstValueForm(c, "Health (.bst health)",
            KAC.BstHealth, "health", "Bonus health value (default 100000).", 100000);
        AddBstTogglePlayer(c, "Remove health (.bst removehealth)",
            KAC.BstRemoveHealth);
        AddBstValueForm(c, "Movement speed (.bst speed)",
            KAC.BstSpeed, "speed", "Movement speed multiplier (default 15).", 15);
        AddBstTogglePlayer(c, "Remove speed (.bst removespeed)",
            KAC.BstRemoveSpeed);
        AddBstValueForm(c, "Yield (.bst yield)",
            KAC.BstYield, "yield", "Yield multiplier on harvest/loot (default 10).", 10);
        AddBstTogglePlayer(c, "Remove yield (.bst removeyield)",
            KAC.BstRemoveYield);

        AddBstTogglePlayer(c, "Bat vision (.bst batvision)",         KAC.BstBatVision);
        AddBstTogglePlayer(c, "Fly (.bst fly)",                       KAC.BstFly);
        AddBstTogglePlayer(c, "No aggro (.bst noaggro)",              KAC.BstNoAggro);
        AddBstTogglePlayer(c, "No blood drain (.bst noblooddrain)",   KAC.BstNoBloodDrain);
        AddBstTogglePlayer(c, "No cooldown (.bst nocooldown)",        KAC.BstNoCooldown);
        AddBstTogglePlayer(c, "No durability loss (.bst nodurability)", KAC.BstNoDurability);
        AddBstTogglePlayer(c, "Immaterial (.bst immaterial)",         KAC.BstImmaterial);
        AddBstTogglePlayer(c, "Invincible (.bst invincible)",         KAC.BstInvincible);
        AddBstTogglePlayer(c, "Shrouded (.bst shrouded)",             KAC.BstShrouded);
        AddBstTogglePlayer(c, "Sun invulnerable (.bst suninvulnerable)", KAC.BstSunInvulnerable);
        AddBstTogglePlayer(c, "Show state (.bst state)",              KAC.BstState);
    }

    private static void AddBstValueForm(GameObject c, string title, string template, string valueName,
                                        string valueTooltip, int defaultValue)
    {
        CollapsibleSection.Build(c,
            title: title, startExpanded: false, tooltip: valueTooltip,
            buildContent: cc => FormBuilder.Build(cc,
                title: title, commandTemplate: template,
                new IntField(valueName, "Value", min: 0, max: 1_000_000,
                    tooltip: valueTooltip + $" Try {defaultValue} for parity with the chat default."),
                new PlayerNameField("player", "Player")));
    }

    private static void AddBstTogglePlayer(GameObject c, string title, string template)
    {
        CollapsibleSection.Build(c,
            title: title, startExpanded: false, tooltip: "Toggle on the named player.",
            buildContent: cc => FormBuilder.Build(cc,
                title: title, commandTemplate: template,
                new PlayerNameField("player", "Player")));
    }

    // -----------------------------------------------------------------------
    // Admin: Server sub-tab
    // -----------------------------------------------------------------------

    private void BuildKindredAdminServerTab(GameObject page)
    {
        page = BeginAdminGate(page);   // 0.73.0: gray out + disable for non-admins, like the Players tab (UI-only; the server enforces admin)
        RenderAdminInfoNote(page, "Kindred admin (Server)");
        AddAdminWarningIntro(page,
            "Server-wide admin commands. These affect global config, all " +
            "players, or persistent server state. Requires KindredCommands + " +
            "server admin permission.");

        // ---- General ------------------------------------------------------
        AddSpacer(page, 4);
        AddSectionHeading(page, "Global");

        var gr1 = AddKLRow(page, "KCAServerGen1");
        AddCommandButton(gr1, "Reveal map (all)", KAC.RevealMapAll,
            "Reveal the map for every player on the server (.revealmapforallplayers).");
        AddCommandButton(gr1, "Clean lost shards",  KAC.CleanContainerlessShards,
            "Destroy all items that aren't currently in containers (.cleancontainerlessshards).");
        AddCommandButton(gr1, "Daywalker (all)",    KAC.EveryoneDaywalker,
            "Toggle daywalker (sun immunity) for every player (.everyonedaywalker).");
        AddCommandButton(gr1, "Bat vision (all)",   KAC.GlobalBatVision,
            "Toggle bat vision for every player (.globalbatvision).");

        CollapsibleSection.Build(page,
            title: "Set time of day (.settime)",
            startExpanded: false,
            tooltip: "Set the game world's time to a specific day and hour.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set time",
                commandTemplate: KAC.SetTime,
                new IntField("day",  "Day",  min: 0, max: 9999),
                new IntField("hour", "Hour", min: 0, max: 23)));
        CollapsibleSection.Build(page,
            title: "Force respawn (.forcerespawn)",
            startExpanded: false,
            tooltip: "Force spawn chains (NPCs, resources) to respawn in a radius around you.",
            buildContent: c => FormBuilder.Build(c,
                title: "Force respawn",
                commandTemplate: KAC.ForceRespawn,
                new IntField("range", "Range", min: 1, max: 200,
                    tooltip: "Radius in world units. Default 10.")));

        // ---- Announce -----------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Announcements");

        var anRow = AddKLRow(page, "KCAAnnounce");
        AddCommandButton(anRow, "List", KAC.AnnounceList,
            "List scheduled announcements (.announce list).");

        CollapsibleSection.Build(page,
            title: "Add announcement (.announce add)",
            startExpanded: false,
            tooltip: "Schedule a new chat announcement.",
            buildContent: c => FormBuilder.Build(c,
                title: "Add announcement",
                commandTemplate: KAC.AnnounceAdd,
                KText(KAC.AnnounceAdd, "name", "Name", tooltip: "Unique announcement key."),
                KText(KAC.AnnounceAdd, "message", "Message", tooltip: "Message text to post."),
                KText(KAC.AnnounceAdd, "time", "Time",
                    tooltip: "Server time, e.g. 8:00 PM for a daily message; a one-time message can name a date (KindredCommands parses it with DateTime.TryParse)."),
                new BoolField("oneTime", "One time only", defaultValue: false)));
        CollapsibleSection.Build(page,
            title: "Change announcement (.announce change)",
            startExpanded: false,
            tooltip: "Update an existing announcement by name.",
            buildContent: c => FormBuilder.Build(c,
                title: "Change announcement",
                commandTemplate: KAC.AnnounceChange,
                KText(KAC.AnnounceChange, "name", "Name"),
                KText(KAC.AnnounceChange, "message", "Message"),
                KText(KAC.AnnounceChange, "time", "Time",
                    tooltip: "Server time, e.g. 8:00 PM; a one-time message can name a date."),
                new BoolField("oneTime", "One time only", defaultValue: false)));
        CollapsibleSection.Build(page,
            title: "Remove announcement (.announce remove)",
            startExpanded: false,
            tooltip: "Remove an announcement by name.",
            buildContent: c => FormBuilder.Build(c,
                title: "Remove announcement",
                commandTemplate: KAC.AnnounceRemove,
                KText(KAC.AnnounceRemove, "name", "Name")));

        // ---- Drop items ---------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Dropped items");

        var drRow = AddKLRow(page, "KCADrops");
        AddCommandButton(drRow, "Clear All",          KAC.DropClearAll,
            "Clear every dropped item in the world (.dropitems clearall).");
        AddCommandButton(drRow, "Clear All Shards",   KAC.DropClearAllShards,
            "Clear every dropped soulshard in the world (.dropitems clearallshards).");
        AddCommandButton(drRow, "Remove Lifetime",    KAC.DropRemoveLifetime,
            "Remove the lifetime cap on dropped items (.dropitems removelifetime).");

        CollapsibleSection.Build(page,
            title: "Set drop lifetime (.dropitems lifetime)",
            startExpanded: false,
            tooltip: "Set how long dropped items persist (in seconds).",
            buildContent: c => FormBuilder.Build(c,
                title: "Set lifetime",
                commandTemplate: KAC.DropLifetime,
                new IntField("seconds", "Seconds", min: 0, max: 86400)));
        CollapsibleSection.Build(page,
            title: "Set drop lifetime when disabled (.dropitems lifetimewhendisabled)",
            startExpanded: false,
            tooltip: "Lifetime applied while the dropped-item system is disabled.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set fallback lifetime",
                commandTemplate: KAC.DropLifetimeWhenDisabled,
                new IntField("seconds", "Seconds", min: 0, max: 86400,
                    tooltip: "Default 300.")));
        CollapsibleSection.Build(page,
            title: "Set shard lifetime (.dropitems shardlifetime)",
            startExpanded: false,
            tooltip: "Set how long dropped soulshards persist (default 3600).",
            buildContent: c => FormBuilder.Build(c,
                title: "Set shard lifetime",
                commandTemplate: KAC.DropShardLifetime,
                new IntField("seconds", "Seconds", min: 0, max: 86400)));
        CollapsibleSection.Build(page,
            title: "Clear drops in radius (.dropitems clear)",
            startExpanded: false,
            tooltip: "Clear dropped items within a radius around you.",
            buildContent: c => FormBuilder.Build(c,
                title: "Clear radius",
                commandTemplate: KAC.DropClear,
                new IntField("radius", "Radius", min: 1, max: 500)));
        CollapsibleSection.Build(page,
            title: "Clear shards in radius (.dropitems clearshards)",
            startExpanded: false,
            tooltip: "Clear dropped soulshards within a radius around you.",
            buildContent: c => FormBuilder.Build(c,
                title: "Clear shards radius",
                commandTemplate: KAC.DropClearShards,
                new IntField("radius", "Radius", min: 1, max: 500)));

        // ---- Region -------------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Regions");

        var rgRow = AddKLRow(page, "KCARegion");
        AddCommandButton(rgRow, "List Players", KAC.RegionListPlayers,
            "List all players currently allowed to enter gated regions (.region listplayers).");

        AddSimpleTextCollapse(page, "Lock region (.region lock)",      KAC.RegionLock,   "region", "Region name");
        AddSimpleTextCollapse(page, "Unlock region (.region unlock)",  KAC.RegionUnlock, "region", "Region name");
        CollapsibleSection.Build(page,
            title: "Gate region (.region gate)",
            startExpanded: false,
            tooltip: "Gate a region behind a player level requirement.",
            buildContent: c => FormBuilder.Build(c,
                title: "Gate region",
                commandTemplate: KAC.RegionGate,
                KText(KAC.RegionGate, "region", "Region"),
                new IntField("level", "Level", min: 0, max: 100)));
        AddSimpleTextCollapse(page, "Ungate region (.region ungate)",  KAC.RegionUngate, "region", "Region name");
        AddSimpleTextCollapse(page, "Allow player (.region allow)",    KAC.RegionAllow,  "player", "Player name");
        CollapsibleSection.Build(page,
            title: "Ban player from region (.region ban)",
            startExpanded: false,
            tooltip: "Ban a player from a specific region.",
            buildContent: c => FormBuilder.Build(c,
                title: "Ban from region",
                commandTemplate: KAC.RegionBan,
                new PlayerNameField("player", "Player"),
                KText(KAC.RegionBan, "region", "Region")));
        CollapsibleSection.Build(page,
            title: "Unban player from region (.region unban)",
            startExpanded: false,
            tooltip: "Lift a region ban on a player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Unban from region",
                commandTemplate: KAC.RegionUnban,
                new PlayerNameField("player", "Player"),
                KText(KAC.RegionUnban, "region", "Region")));
        AddSimpleTextCollapse(page, "List region bans (.region listbans)", KAC.RegionListBans, "region", "Region name");
        AddSimpleTextCollapse(page, "Remove from allowlist (.region remove)", KAC.RegionRemove, "player", "Player name");

        // ---- Boss locks ---------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Bosses (lock)");
        AddSimpleTextCollapse(page, "Lock boss (.boss lock)",            KAC.BossLock,         "boss", "V Blood boss name");
        AddSimpleTextCollapse(page, "Unlock boss (.boss unlock)",        KAC.BossUnlock,       "boss", "V Blood boss name");
        AddSimpleTextCollapse(page, "Lock primal (.boss lockprimal)",    KAC.BossLockPrimal,   "primalBoss", "Primal boss name");
        AddSimpleTextCollapse(page, "Unlock primal (.boss unlockprimal)", KAC.BossUnlockPrimal, "primalBoss", "Primal boss name");

        // ---- Gear (server) ------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Gear (server)");

        var grRow1 = AddKLRow(page, "KCAGearServer1");
        AddCommandButton(grRow1, "Headgear loss",   KAC.GearHeadgear,
            "Toggle whether players lose headgear on death (.gear headgear).");
        AddCommandButton(grRow1, "Shard flight",    KAC.GearSoulshardFlight,
            "Toggle soulshard flight restrictions (.gear soulshardflight).");
        AddCommandButton(grRow1, "Shard drop mgmt", KAC.GearSoulshardDropMgmt,
            "Toggle KC-managed soulshard drops (.gear togglesoulsharddropmanagement).");
        AddCommandButton(grRow1, "Destroy shards",  KAC.GearDestroyAllShards,
            "Destroy every soulshard currently in the world (.gear destroyallshards).");

        CollapsibleSection.Build(page,
            title: "Shard drop limit (.gear soulshardlimit)",
            startExpanded: false,
            tooltip: "Set how many soulshards of a given type can exist before drops stop.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set shard limit",
                commandTemplate: KAC.GearSoulshardLimit,
                new IntField("limit", "Limit", min: 0, max: 99),
                KText(KAC.GearSoulshardLimit, "shardType", "Shard type",
                    tooltip: "TheMonster, Solarus, WingedHorror, Megara, Dracula, or All.")));
        CollapsibleSection.Build(page,
            title: "Shard durability time (.gear soulsharddurabilitytime)",
            startExpanded: false,
            tooltip: "Set how long a soulshard lasts before its durability ticks down.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set shard durability time",
                commandTemplate: KAC.GearSoulshardDurabilityTime,
                new IntField("seconds", "Seconds", min: 1, max: 86400)));

        // ---- Clan rename --------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Clans");
        CollapsibleSection.Build(page,
            title: "Rename clan (.clan rename)",
            startExpanded: false,
            tooltip: "Admin-rename a clan, optionally specifying the leader.",
            buildContent: c => FormBuilder.Build(c,
                title: "Rename clan",
                commandTemplate: KAC.ClanRename,
                KText(KAC.ClanRename, "oldClanName", "Old clan name"),
                KText(KAC.ClanRename, "newClanName", "New clan name"),
                KText(KAC.ClanRename, "leaderName", "Leader name (optional)",
                    tooltip: "Only needed when several clans match the old name: the one led by this player is renamed. Leave blank otherwise.")));

        // ---- Prisoner -----------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Prisoners");
        CollapsibleSection.Build(page,
            title: "Set gruel transform chance (.prisoner gruel)",
            startExpanded: false,
            tooltip: "Adjust the chance and rate window of gruel transformations.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set gruel",
                commandTemplate: KAC.PrisonerGruel,
                KText(KAC.PrisonerGruel, "chance", "Chance (0-1)"),
                KText(KAC.PrisonerGruel, "min", "Min value"),
                KText(KAC.PrisonerGruel, "max", "Max value")));
        CollapsibleSection.Build(page,
            title: "Set gruel transform prefab (.prisoner grueltransform)",
            startExpanded: false,
            tooltip: "Set the unit prefab a gruel-transformed prisoner becomes.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set gruel transform",
                commandTemplate: KAC.PrisonerGruelTransform,
                KText(KAC.PrisonerGruelTransform, "prefab", "Unit prefab")));
        CollapsibleSection.Build(page,
            title: "Set prisoner feed settings (.prisoner feed)",
            startExpanded: false,
            tooltip: "Adjust the per-feed ranges of health / misery / blood-quality changes.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set feed",
                commandTemplate: KAC.PrisonerFeed,
                KText(KAC.PrisonerFeed, "feed", "Feed name", tooltip: FeedNamesTooltip),
                KText(KAC.PrisonerFeed, "healthChangeMin", "Health min"),
                KText(KAC.PrisonerFeed, "healthChangeMax", "Health max"),
                KText(KAC.PrisonerFeed, "miseryChangeMin", "Misery min"),
                KText(KAC.PrisonerFeed, "miseryChangeMax", "Misery max"),
                KText(KAC.PrisonerFeed, "bloodQualityChangeMin", "Quality min"),
                KText(KAC.PrisonerFeed, "bloodQualityChangeMax", "Quality max")));
        CollapsibleSection.Build(page,
            title: "Restore feed defaults (.prisoner feeddefault)",
            startExpanded: false,
            tooltip: "Reset a feed to its default config.",
            buildContent: c => FormBuilder.Build(c,
                title: "Restore feed defaults",
                commandTemplate: KAC.PrisonerFeedDefault,
                KText(KAC.PrisonerFeedDefault, "feed", "Feed name", tooltip: FeedNamesTooltip)));

        // ---- Staff --------------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Staff");

        var stRow = AddKLRow(page, "KCAStaff");
        AddCommandButton(stRow, "Reload staff",      KAC.ReloadStaff,
            "Reload the staff config from disk (.reloadstaff).");
        AddCommandButton(stRow, "Reload admin list", KAC.ReloadAdmin,
            "Reload the admin list (.reloadadmin).");
        AddCommandButton(stRow, "Auto admin auth",   KAC.AutoAdminAuth,
            "Toggle whether you are on the auto-admin-auth list (.autoadminauth).");

        CollapsibleSection.Build(page,
            title: "Set staff rank (.setstaff)",
            startExpanded: false,
            tooltip: "Assign a staff rank to a player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set staff",
                commandTemplate: KAC.SetStaff,
                new PlayerNameField("player", "Player"),
                KText(KAC.SetStaff, "rank", "Rank",
                    tooltip: "Rank text; KindredCommands shows it in [brackets].")));
        CollapsibleSection.Build(page,
            title: "Remove staff rank (.removestaff)",
            startExpanded: false,
            tooltip: "Remove a player's staff rank.",
            buildContent: c => FormBuilder.Build(c,
                title: "Remove staff",
                commandTemplate: KAC.RemoveStaff,
                new PlayerNameField("player", "Player")));
        CollapsibleSection.Build(page,
            title: "Toggle admin (.toggleadmin)",
            startExpanded: false,
            tooltip: "Add or remove a player from the admin list.",
            buildContent: c => FormBuilder.Build(c,
                title: "Toggle admin",
                commandTemplate: KAC.ToggleAdmin,
                new PlayerNameField("player", "Player")));
        // 0.73.0: the "Server wipe" section (.wipe / .commencewipe / .cancelwipe) was removed: KindredCommands 2.5.8
        // ships those commands commented out, so the buttons did nothing.
    }

    // -----------------------------------------------------------------------
    // Admin: World sub-tab
    // -----------------------------------------------------------------------

    private void BuildKindredAdminWorldTab(GameObject page)
    {
        page = BeginAdminGate(page);   // 0.73.0: gray out + disable for non-admins, like the Players tab (UI-only; the server enforces admin)
        RenderAdminInfoNote(page, "Kindred admin (World)");
        AddAdminWarningIntro(page,
            "World manipulation commands - spawn units, teleport, search " +
            "prefabs, manage servants and castles. Acts on the world around " +
            "you (or supplied coordinates). Requires KindredCommands + server " +
            "admin permission.");

        AddSpacer(page, 4);
        AddSectionHeading(page, "Position");

        var posRow = AddKLRow(page, "KCAWorldPos");
        AddCommandButton(posRow, "Where am I",     KAC.WhereAmI,
            "Print your current position and territory (.whereami).");

        CollapsibleSection.Build(page,
            title: "Teleport horses to me (.teleporthorse)",
            startExpanded: false,
            tooltip: "Teleport all horses within radius to your position.",
            buildContent: c => FormBuilder.Build(c,
                title: "Teleport horses",
                commandTemplate: KAC.TeleportHorse,
                new IntField("radius", "Radius", min: 1, max: 500,
                    tooltip: "Default 5.")));

        // ---- Lookups (find prefab IDs for spawn/give commands) -----------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Lookups (find IDs for spawn / give)");
        var lookupsHint = UIFactory.CreateLabel(page, "LookupsHint",
            "Use these to find the prefab name / ID for an item, NPC, or boss before using the Spawn or Give forms below. Replies appear in chat — V Rising's prefab registry isn't available client-side, so the search runs on the server and KindredCommands lists matching results.",
            TMPro.TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(lookupsHint.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(40), flexibleHeight: 0);
        lookupsHint.TextMesh.fontStyle = TMPro.FontStyles.Italic;
        lookupsHint.TextMesh.enableWordWrapping = true;
        lookupsHint.TextMesh.overflowMode = TMPro.TextOverflowModes.Overflow;
        // 0.50: ContentSizeFitter so the wrapped hint grows with the font scale.
        var lookupsFitter = lookupsHint.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        lookupsFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        lookupsFitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        // Quick boss-list button (no args). Same command as the player-facing
        // Boss List on the Kindred Commands tab. 0.73.0: .boss list prints only
        // the LOCKED bosses (KindredCommands BossCommands.cs), so the label says so;
        // row via AddKLRow so its height scales with the text size.
        var lookupsRow = AddKLRow(page, "LookupsRow");
        AddCommandButton(lookupsRow, "List Locked Bosses", KAC.BossList,
            "List the bosses that are currently locked (.boss list).");

        CollapsibleSection.Build(page,
            title: "Search items (.search item)",
            startExpanded: false,
            tooltip: "Search items by name fragment. Reply lists every match with its prefab name — copy the name (e.g. 'Item_Ingredient_Iron') and paste into the .give form on the Players admin tab to spawn it. Page defaults to 1; KindredCommands paginates if there are many matches.",
            buildContent: c => FormBuilder.Build(c,
                title: "Search items",
                commandTemplate: KAC.SearchItem,
                KText(KAC.SearchItem, "search", "Search text", placeholder: "iron",
                    tooltip: "Substring of the item name. Case-insensitive."),
                new IntField("page", "Page", min: 1, max: 99,
                    tooltip: "Pagination — start at 1, increment if results say there's more.")));
        CollapsibleSection.Build(page,
            title: "Search NPCs / creatures (.search npc)",
            startExpanded: false,
            tooltip: "Search NPC + creature prefabs by name fragment. Reply lists every match — copy a 'CHAR_'-prefixed name and use it with .spawnnpc / .customspawn / .customspawnat / .servant change / .despawnnpc.",
            buildContent: c => FormBuilder.Build(c,
                title: "Search NPCs",
                commandTemplate: KAC.SearchNpc,
                KText(KAC.SearchNpc, "search", "Search text", placeholder: "wolf",
                    tooltip: "Substring of the unit name. Case-insensitive."),
                new IntField("page", "Page", min: 1, max: 99)));

        // ---- Spawn --------------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Spawn");
        CollapsibleSection.Build(page,
            title: "Spawn NPC (.spawnnpc)",
            startExpanded: false,
            tooltip: "Spawn a number of NPCs at your position.",
            buildContent: c => FormBuilder.Build(c,
                title: "Spawn NPC",
                commandTemplate: KAC.SpawnNpc,
                KText(KAC.SpawnNpc, "character", "Unit prefab",
                    tooltip: "CHAR_-prefixed prefab name. Use .search npc to find one."),
                new IntField("count", "Count", min: 1, max: 50),
                new IntField("level", "Level (-1 default)", min: -1, max: 200)));
        CollapsibleSection.Build(page,
            title: "Custom spawn at me (.customspawn)",
            startExpanded: false,
            tooltip: "Spawn an NPC at your position with full customization.",
            buildContent: c => FormBuilder.Build(c,
                title: "Custom spawn",
                commandTemplate: KAC.CustomSpawn,
                KText(KAC.CustomSpawn, "unit", "Unit prefab"),
                new EnumField<PlayerStateService.BloodType>("type", "Blood type",
                    defaultValue: PlayerStateService.BloodType.Warrior),
                new IntField("quality",  "Blood quality", min: 0, max: 100),
                new BoolField("consumable", "Consumable", defaultValue: true),
                new IntField("duration", "Duration (-1 infinite)", min: -1, max: 86400),
                new IntField("level",    "Level (0 default)", min: 0, max: 200)));
        CollapsibleSection.Build(page,
            title: "Custom spawn at coords (.customspawnat)",
            startExpanded: false,
            tooltip: "Spawn an NPC at specific world coordinates with full customization.",
            buildContent: c => FormBuilder.Build(c,
                title: "Custom spawn at coords",
                commandTemplate: KAC.CustomSpawnAt,
                KText(KAC.CustomSpawnAt, "unit", "Unit prefab"),
                KText(KAC.CustomSpawnAt, "x", "X"),
                KText(KAC.CustomSpawnAt, "y", "Y"),
                KText(KAC.CustomSpawnAt, "z", "Z"),
                new EnumField<PlayerStateService.BloodType>("type", "Blood type",
                    defaultValue: PlayerStateService.BloodType.Warrior),
                new IntField("quality",  "Blood quality", min: 0, max: 100),
                new BoolField("consumable", "Consumable", defaultValue: true),
                new IntField("duration", "Duration (-1 infinite)", min: -1, max: 86400),
                new IntField("level",    "Level",  min: 0, max: 200)));
        CollapsibleSection.Build(page,
            title: "Despawn NPCs (.despawnnpc)",
            startExpanded: false,
            tooltip: "Despawn NPCs of a given prefab within a radius.",
            buildContent: c => FormBuilder.Build(c,
                title: "Despawn NPC",
                commandTemplate: KAC.DespawnNpc,
                KText(KAC.DespawnNpc, "character", "Unit prefab"),
                new IntField("radius", "Radius", min: 1, max: 200,
                    tooltip: "Default 25.")));
        CollapsibleSection.Build(page,
            title: "Spawn horse (.spawnhorse)",
            startExpanded: false,
            tooltip: "Spawn one or more horses with custom stats.",
            buildContent: c => FormBuilder.Build(c,
                title: "Spawn horse",
                commandTemplate: KAC.SpawnHorse,
                KText(KAC.SpawnHorse, "speed", "Speed"),
                KText(KAC.SpawnHorse, "acceleration", "Acceleration"),
                KText(KAC.SpawnHorse, "rotation", "Rotation speed"),
                new IntField("num", "Count", min: 1, max: 20)));
        CollapsibleSection.Build(page,
            title: "Ban unit from spawning (.spawnban)",
            startExpanded: false,
            tooltip: "Prevent a unit prefab from spawning naturally, with a reason logged.",
            buildContent: c => FormBuilder.Build(c,
                title: "Spawn ban",
                commandTemplate: KAC.SpawnBan,
                KText(KAC.SpawnBan, "character", "Unit prefab"),
                KText(KAC.SpawnBan, "reason", "Reason")));

        // ---- Boss (world: modify+teleport) -------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Bosses (world)");
        CollapsibleSection.Build(page,
            title: "Modify boss level (.boss modify)",
            startExpanded: false,
            tooltip: "Set the level of the nearest V Blood boss with the given name.",
            buildContent: c => FormBuilder.Build(c,
                title: "Modify boss",
                commandTemplate: KAC.BossModify,
                KText(KAC.BossModify, "boss", "Boss name"),
                new IntField("level", "Level", min: 1, max: 200)));
        CollapsibleSection.Build(page,
            title: "Modify primal boss level (.boss modifyprimal)",
            startExpanded: false,
            tooltip: "Set the level of the nearest primal boss with the given name.",
            buildContent: c => FormBuilder.Build(c,
                title: "Modify primal boss",
                commandTemplate: KAC.BossModifyPrimal,
                KText(KAC.BossModifyPrimal, "boss", "Boss name"),
                new IntField("level", "Level", min: 1, max: 200)));
        CollapsibleSection.Build(page,
            title: "Teleport to boss (.boss teleportto)",
            startExpanded: false,
            tooltip: "Teleport yourself to a named boss.",
            buildContent: c => FormBuilder.Build(c,
                title: "Teleport to boss",
                commandTemplate: KAC.BossTeleportTo,
                KText(KAC.BossTeleportTo, "boss", "Boss name"),
                new IntField("whichOne", "Index (0 default)", min: 0, max: 50)));

        // ---- Castle -------------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Castles");

        var castleRow = AddKLRow(page, "KCACastle");
        AddCommandButton(castleRow, "Relocate Reset",   KAC.RelocateReset,
            "Clear the relocation cooldown on the current castle (.relocatereset).");
        AddCommandButton(castleRow, "Incoming Decay",   KAC.IncomingDecay,
            "List territories with the least castle time remaining (.incomingdecay).");
        AddCommandButton(castleRow, "Freeze Heart",     KAC.FreezeHeart,
            "Freeze the castle heart you are targeting (stops decay) (.freezeheart).");
        AddCommandButton(castleRow, "Thaw Heart",       KAC.ThawHeart,
            "Resume decay on the castle heart you are targeting (.thawheart).");

        CollapsibleSection.Build(page,
            title: "Claim castle (.claim)",
            startExpanded: false,
            tooltip: "Claim the castle heart you are targeting for the named player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Claim castle",
                commandTemplate: KAC.Claim,
                new PlayerNameField("player", "Player")));
        CollapsibleSection.Build(page,
            title: "List plots owned (.plotsowned)",
            startExpanded: false,
            tooltip: "List castle plots owned by each player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Plots owned",
                commandTemplate: KAC.PlotsOwned,
                new IntField("page", "Page", min: 1, max: 99)));
        CollapsibleSection.Build(page,
            title: "Frozen hearts (.frozenhearts)",
            startExpanded: false,
            tooltip: "List frozen (non-decaying) castle hearts.",
            buildContent: c => FormBuilder.Build(c,
                title: "Frozen hearts",
                commandTemplate: KAC.FrozenHearts,
                new IntField("page", "Page", min: 1, max: 99)));
        CollapsibleSection.Build(page,
            title: "Clan plots owned (.clanplotsowned)",
            startExpanded: false,
            tooltip: "List castle plots owned by each clan.",
            buildContent: c => FormBuilder.Build(c,
                title: "Clan plots owned",
                commandTemplate: KAC.ClanPlotsOwned,
                new IntField("page", "Page", min: 1, max: 99)));
        CollapsibleSection.Build(page,
            title: "Teleport to territory plot (.teleporttoplot)",
            startExpanded: false,
            tooltip: "Teleport to the castle heart of a specific territory.",
            buildContent: c => FormBuilder.Build(c,
                title: "Teleport to plot",
                commandTemplate: KAC.TeleportToPlot,
                new IntField("territoryIndex", "Territory index", min: 0, max: 9999)));
        CollapsibleSection.Build(page,
            title: "Plot info (.plotinfo)",
            startExpanded: false,
            tooltip: "Print detailed info about a territory's plot.",
            buildContent: c => FormBuilder.Build(c,
                title: "Plot info",
                commandTemplate: KAC.PlotInfo,
                new IntField("territoryIndex", "Territory index", min: 0, max: 9999)));

        // ---- Servant ------------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Servants");

        var srRow1 = AddKLRow(page, "KCAServant1");
        AddCommandButton(srRow1, "Convert",          KAC.ServantConvert,
            "Instantly convert the servant in the targeted coffin (.servant convert).");
        AddCommandButton(srRow1, "Perfect",          KAC.ServantPerfect,
            "Make the targeted servant perfect (max expertise) (.servant perfect).");
        AddCommandButton(srRow1, "Heal",             KAC.ServantHeal,
            "Cure the targeted servant of injuries (.servant heal).");
        AddCommandButton(srRow1, "Revive",           KAC.ServantRevive,
            "Revive the servant in the targeted coffin (.servant revive).");

        var srRow2 = AddKLRow(page, "KCAServant2");
        AddCommandButton(srRow2, "Complete Mission", KAC.ServantCompleteMission,
            "Complete the active mission on the targeted servant (.servant completemission).");

        CollapsibleSection.Build(page,
            title: "Change servant in coffin (.servant change)",
            startExpanded: false,
            tooltip: "Replace the servant in the targeted coffin with a different unit prefab.",
            buildContent: c => FormBuilder.Build(c,
                title: "Change servant",
                commandTemplate: KAC.ServantChange,
                KText(KAC.ServantChange, "character", "Unit prefab")));
        CollapsibleSection.Build(page,
            title: "Add servant to coffin (.servant add)",
            startExpanded: false,
            tooltip: "Add a servant to the targeted empty coffin.",
            buildContent: c => FormBuilder.Build(c,
                title: "Add servant",
                commandTemplate: KAC.ServantAdd,
                KText(KAC.ServantAdd, "character", "Unit prefab")));

        // ---- Gear (world: range-based) -----------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Gear (radius)");
        CollapsibleSection.Build(page,
            title: "Repair all gear in radius (.gear repairall)",
            startExpanded: false,
            tooltip: "Repair every piece of gear worn by any player in the radius.",
            buildContent: c => FormBuilder.Build(c,
                title: "Repair all in radius",
                commandTemplate: KAC.GearRepairAll,
                new IntField("range", "Range", min: 1, max: 200,
                    tooltip: "Default 10.")));
        CollapsibleSection.Build(page,
            title: "Break all gear in radius (.gear breakall)",
            startExpanded: false,
            tooltip: "Drop the gear durability of all players in the radius to zero.",
            buildContent: c => FormBuilder.Build(c,
                title: "Break all in radius",
                commandTemplate: KAC.GearBreakAll,
                new IntField("range", "Range", min: 1, max: 200,
                    tooltip: "Default 10.")));

        // ---- Castle / Clan world queries (0.5.0 audit gaps) -------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Castle & Clan");
        // 0.73.0: AddKLRow (was a fixed 32px row). "Repair clan data" (.clan fix) removed: commented out in
        // KindredCommands 2.5.8.
        var castleClanRow = AddKLRow(page, "CastleClanRow");
        AddCommandButton(castleClanRow, "Longest offline",  KAC.LongestOfflineCastles,
            "List castles ranked by owner-last-online (.longestofflinecastles). Helps clean up dead bases.");

        CollapsibleSection.Build(page,
            title: "List castles owned by a clan (.clan castles)",
            startExpanded: false,
            tooltip: "List the territories a specific clan owns. Reply appears in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Clan castles",
                commandTemplate: KAC.ClanCastles,
                KText(KAC.ClanCastles, "clanName", "Clan name")));

        // ---- Bloodbound items -------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Bloodbound items");
        CollapsibleSection.Build(page,
            title: "Add Bloodbound attribute (.bloodbound add)",
            startExpanded: false,
            tooltip: "Marks the named/prefab item as Bloodbound (drops only to its owner, etc.).",
            buildContent: c => FormBuilder.Build(c,
                title: "Add bloodbound",
                commandTemplate: KAC.BloodboundAdd,
                KText(KAC.BloodboundAdd, "descriptor", "Item prefab/name")));
        CollapsibleSection.Build(page,
            title: "Remove Bloodbound attribute (.bloodbound remove)",
            startExpanded: false,
            tooltip: "Removes the Bloodbound flag from the item.",
            buildContent: c => FormBuilder.Build(c,
                title: "Remove bloodbound",
                commandTemplate: KAC.BloodboundRemove,
                KText(KAC.BloodboundRemove, "descriptor", "Item prefab/name")));
    }

    // -----------------------------------------------------------------------
    // Shared helpers for this file
    // -----------------------------------------------------------------------

    private static void AddAdminWarningIntro(GameObject page, string body)
    {
        // 0.10.12: wrap the intro paragraph in a card so it doesn't sit
        // flush with the panel border. Italic-muted styling pulls it back
        // visually so the forms below are the primary focus.
        var card = UIFactory.CreateVerticalGroup(page, "AdminIntroCard",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(8, 8, 10, 10),
            bgColor: Theme.CardBackground);
        UIFactory.SetLayoutElement(card,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, flexibleHeight: 0);

        // 0.10.13: dropped italic and bumped font size 12 → 13 for legibility.
        var intro = UIFactory.CreateLabel(card, "AdminIntro",
            $"<color={Theme.MutedBodyHex}>{body}</color>",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(intro.GameObject,
            minWidth: 320, preferredWidth: 380, flexibleWidth: 1,
            minHeight: 36, preferredHeight: 44, flexibleHeight: 0);
        intro.TextMesh.enableWordWrapping = true;
        intro.TextMesh.overflowMode = TextOverflowModes.Overflow;
        intro.TextMesh.fontStyle = FontStyles.Normal;
    }

    // For "fill in one string, fire command" cases. 0.73.0: KText, so a quoted
    // placeholder (boss names like "Alpha Wolf") is encoded per KindredAdminCommands.
    private static void AddSimpleTextCollapse(GameObject page, string title, string template,
                                              string fieldName, string fieldLabel)
    {
        CollapsibleSection.Build(page,
            title: title, startExpanded: false, tooltip: title,
            buildContent: c => FormBuilder.Build(c,
                title: title,
                commandTemplate: template,
                KText(template, fieldName, fieldLabel)));
    }
}
