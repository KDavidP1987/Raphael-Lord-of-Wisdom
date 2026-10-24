using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Raphael.Services.Nyar;

// 0.74.0 (PLAN 2026-10-24 §B1): the plain-words help of the Nyarlathotep windows, written from Nyarlathotep's README
// (Setting up events, Unit pools; 0.15.0). PROSE ONLY: what a setting does and what it means in play. Defaults, ranges,
// units and option values are never written here; Tooltip() takes them from NyarSpec / NyarFieldRules / NyarLimits, so
// the help can't drift from the rules. Keyed by the generic field id (N / M / NAME, as NyarFieldRules.SettableFields);
// Inputs holds the boxes that aren't event fields, Sections the folded "How this works" guides. Pure, unit-tested
// (coverage both ways, no numbers beside "default").

internal static class NyarHelp
{
    // ================================================================== event fields

    public static readonly Dictionary<string, string> Fields = new(StringComparer.Ordinal)
    {
        // ---- basics and conditions
        ["name"] = "The event's name. Players see it in banners, in the status and in the event list.",
        ["durationSeconds"] = "How long the event runs once it starts. When the time is up it ends, and its units are removed a little later.",
        ["conditions.minPlayers"] = "How many players must be online before it starts on its own. An admin's Start ignores this.",
        ["conditions.cooldownMinutes"] = "After it starts, it can't start on its own again until this much time has passed. An admin's Start ignores this.",
        ["conditions.chancePercent"] = "Each time its trigger fires, it only starts with this chance. Lower it to make the event rarer.",

        // ---- triggers (set under Any other field)
        ["trigger.type"] = "What starts the event: Manual (only an admin), Schedule (days and times), GameTime (day or night), VBloodKilled, Interval (a random wait), RegionEntered, FactionKills or Revenge.",
        ["trigger.days"] = "Schedule trigger: the days it runs on, comma separated, like Sat,Sun.",
        ["trigger.times"] = "Schedule trigger: the server-local times it starts at, comma separated, like 20:00,22:30.",
        ["trigger.phase"] = "GameTime trigger: day starts it when day breaks, night when night falls.",
        ["trigger.bosses"] = "VBloodKilled trigger: the V Bloods whose kill starts it, or any. A V Blood spawned by an event never counts.",
        ["trigger.minMinutes"] = "Interval trigger: the shortest wait after the last run before it starts again.",
        ["trigger.maxMinutes"] = "Interval trigger: the longest wait. The real wait is picked at random between the two.",
        ["trigger.playerCooldownMinutes"] = "RegionEntered and Revenge triggers: how long before the same player can set it off again.",
        ["trigger.factions"] = "FactionKills and Revenge triggers: the factions whose kills count.",
        ["trigger.kills"] = "FactionKills trigger: how many kills it takes, inside the time window.",
        ["trigger.windowSeconds"] = "FactionKills and Revenge triggers: the time window the kills must fall in.",
        ["trigger.shared"] = "FactionKills trigger: whether every player's kills add up together, or one player has to make them all.",
        ["trigger.heat"] = "Revenge trigger: the heat a player must reach. Each kill of the factions adds heat; at this much, waves come for that player alone.",
        ["trigger.warnAt"] = "Revenge trigger: up to three heat levels below the top at which the player gets a private warning.",
        ["trigger.vbloodHeat"] = "Revenge trigger: the heat one V Blood kill adds.",
        ["trigger.scope"] = "Where the trigger listens: Global, or a list of regions. Outside them it doesn't fire.",

        // ---- empowerment
        ["action.scope"] = "Where the action works: Global, or a list of regions. Units, hotspots and buffs stay inside them, and an admin start from outside is refused.",
        ["action.factions"] = "The factions whose NPCs get stronger while the event runs. Players' units, servants, traders and the mod's own units are never buffed.",
        ["action.stats.physicalPower"] = "Multiplies the empowered NPCs' physical power. At x1.0 it stays as it is.",
        ["action.stats.spellPower"] = "Multiplies the empowered NPCs' spell power. At x1.0 it stays as it is.",
        ["action.stats.maxHealth"] = "Multiplies the empowered NPCs' health. At x1.0 it stays as it is.",
        ["action.stats.attackSpeed"] = "Multiplies how fast the empowered NPCs attack and use abilities.",
        ["action.stats.moveSpeed"] = "Multiplies how fast the empowered NPCs move.",

        // ---- spawn waves
        ["location"] = "Where each wave appears: spread over the event's regions, near a random player, or on a fixed spot you set with Use where I stand.",
        ["location.near"] = "Around a player: the closest a wave may appear to that player.",
        ["location.far"] = "Around a player: the farthest a wave may appear. With Hunt players it can't be past the hunt range.",
        ["action.radius"] = "How far from the wave's centre its units spread out when they appear.",
        ["action.behaviour"] = "What the units do once they appear: stay where they are, hunt players nearby, or roam the regions after the nearest player.",
        ["behaviour.range"] = "Hunt players: how far from the wave the units look for players to chase.",
        ["action.pursuit"] = "If no player has come near the units for this long, they go after the nearest player anywhere until the event ends. Needs Hunt players.",
        ["action.loot"] = "Whether the event's units drop loot when they die.",
        ["action.allowTerritory"] = "Whether the event may reach claimed castle land. When it's off, nothing spawns, marches or hunts there, and a start on castle land is refused. When it's on, castles are ground like any other.",
        ["action.waves"] = "How many waves come. Every wave brings the same units.",
        ["action.intervalSeconds"] = "The time from one wave to the next, whether the last one was cleared or not.",
        ["action.units"] = "The units every wave brings. Each entry is a unit with a count, or a draw from a pool (a fixed count, or a random count between a min and a max).",
        ["action.units.N.chance"] = "The chance each copy of this entry joins the wave. Lower it for a wave that's less predictable.",
        ["action.modifiers.level"] = "Gives every unit the same level. Keep units' own to leave each unit at its normal level.",
        ["action.modifiers.levelDelta"] = "Raises or lowers each unit's own level by a few steps. It can't be used together with a fixed level.",
        ["action.modifiers.maxHealth"] = "Multiplies every unit's health. Below x1 makes them weaker.",
        ["action.modifiers.power"] = "Multiplies every unit's physical and spell power. Below x1 makes them weaker.",
        ["action.modifiers.moveSpeed"] = "Multiplies how fast every unit moves.",
        ["action.modifiers.attackSpeed"] = "Multiplies how fast every unit attacks and uses abilities.",
        ["action.fanOut"] = "Around a player only: one group near each of several players at once, set apart from each other. All the groups share one wave's unit limit, and it can't have bosses.",
        ["action.scoreboard"] = "Counts kills of the event's units and deaths to them, and shows the top three in chat when it ends.",
        ["action.winner"] = "Needs the scoreboard: names the player with the best kills against deaths when it ends.",
        ["action.arena"] = "Keeps the fight in a circle: a unit that wanders out is brought back near the centre. Needs a fixed spot or the starting admin.",
        ["action.regionLock"] = "Keeps the units inside the scope regions: one that leaves is brought back.",
        ["action.growth"] = "Makes each wave bigger or stronger than the one before (same units every wave). none removes it.",
        ["action.growth.addUnits"] = "Each new wave adds this many to every entry's count.",
        ["action.growth.addLevels"] = "Each new wave adds this many levels to the units.",
        ["action.growth.whenCleared"] = "Whether the next wave also starts as soon as the last one is cleared, instead of only on its timer.",
        ["action.bosses"] = "Bosses that come with the last wave (or rise with the horde). They must die before the event counts as won. An entry is one boss, or a roll of a few from a pool.",
        ["action.pools.NAME"] = "One of this event's own pools: a named mix of units that list entries and boss rolls draw from at random.",
        ["action.destination"] = "A place the units march to as one body, then do their behaviour there. Stand on it and press Destination here.",
        ["action.destination.radius"] = "Units this close to the destination have arrived and start their behaviour.",
        ["action.destination.name"] = "What the arrival banners call the destination.",
        ["action.marchSpeed"] = "How fast marching units walk, in metres a second. Players walk at about five.",

        // ---- wave list
        ["action.waveList.N"] = "One wave of a per-wave list. Here it only takes none, which removes that wave.",
        ["action.waveList.N.units"] = "The units this wave brings. A unit may appear more than once, each entry with its own count.",
        ["action.waveList.N.start"] = "When this wave starts: as soon as the wave before is cleared, a set time after it started, or whichever comes first.",
        ["action.waveList.N.afterSeconds"] = "How long after the wave before started this wave starts.",
        ["action.waveList.N.whenCleared"] = "Whether this wave starts as soon as every unit of the wave before is gone.",
        ["action.waveList.N.bosses"] = "This wave's own bosses. Don't also give the last wave bosses through the final-wave list.",
        ["action.waveList.N.units.M.level"] = "Gives this one entry's units a fixed level.",
        ["action.waveList.N.units.M.levelDelta"] = "Raises or lowers this one entry's units' own level by a few steps.",
        ["action.waveList.N.units.M.maxHealth"] = "Multiplies this one entry's units' health.",
        ["action.waveList.N.units.M.power"] = "Multiplies this one entry's units' physical and spell power.",
        ["action.waveList.N.units.M.moveSpeed"] = "Multiplies how fast this one entry's units move.",
        ["action.waveList.N.units.M.attackSpeed"] = "Multiplies how fast this one entry's units attack.",

        // ---- apocalypse
        ["action.goal"] = "Apocalypse: the place the hordes march on. Goal here takes the spot you stand on.",
        ["action.goal.radius"] = "Apocalypse: a horde that gets this close to the goal takes it, and the event is lost.",
        ["action.goal.name"] = "Apocalypse: what the banners call the goal.",
        ["action.hordes.count"] = "Apocalypse: how many hordes rise during the event. Destroy them all to win.",
        ["action.hordes.everyMinutes"] = "Apocalypse: the time between one horde and the next.",
        ["action.hordes.size"] = "Apocalypse: how many units the first horde brings. Each later horde brings more by the event's growth (events.json).",
        ["action.roamers.max"] = "Apocalypse: the most roamers alive at once. Roamers rise at each hotspot when it starts (roamers.perHotspot in events.json), " +
                                 "hunt players across the map, and each kill they make raises more where the victim fell, up to this.",

        // ---- infestation
        ["action.origins.count"] = "Infestation: how many places the dead rise at. Each place is a gathering of the faction's own NPCs.",
        ["action.origins.minNatives"] = "Infestation: how many of the faction's NPCs a place needs before it counts.",
        ["action.origins.minSpacing"] = "Infestation: the least distance between two places.",
        ["action.origins.perOrigin"] = "Infestation: how many undead rise at each place when it starts. The event starts with places × this; the rest come from the spread.",
        ["action.hunters"] = "Infestation: the share of the undead that hunt the nearest player anywhere. The rest drift around their place.",
        ["action.driftRadius"] = "Infestation: how far the drifting undead wander from their place.",

        // ---- horde march
        ["action.start"] = "Horde march: where the horde rises. Start here takes the spot you stand on.",
        ["action.start.minDistance"] = "Horde march: rise at the faction's gathering nearest the destination, but at least this far from it.",
        ["action.infest.hunters"] = "Horde march, once it infests: the share that hunts the nearest player. The rest drift.",
        ["action.infest.driftRadius"] = "Horde march, once it infests: how far the drifting undead wander.",
    };

    // The three spread-shaped blocks share their prose; the block says when it happens.
    private static readonly (string Block, string When)[] SpreadWhen =
    {
        ("action.spread.", "Infestation spread"), ("action.growth.", "Horde march, on the way"), ("action.infest.spread.", "Horde march, once it infests"),
    };
    private static readonly (string Key, string Text)[] SpreadText =
    {
        ("perKillMin", "the fewest new undead one kill raises. A kill here is one the undead make, of a player or another NPC; the new " +
                       "undead rise where the victim fell. A kill raises a random number between the two."),
        ("perKillMax", "the most new undead one kill by the undead raises."),
        ("max", "the most of these undead alive at once. On the march this caps the horde's size, so set it above the horde's start."),
        ("perMinute", "the most that rise in one minute."),
        ("pool", "the pool the new undead are drawn from. No pool draws them from the event's units."),
    };

    static NyarHelp()
    {
        foreach (var (b, when) in SpreadWhen)
            foreach (var (k, t) in SpreadText)
                Fields[b + k] = $"{when}: {t}";
    }

    // ================================================================== choice options (one line per option)

    public static readonly Dictionary<string, Dictionary<string, string>> Options = new(StringComparer.Ordinal)
    {
        ["location"] = new(StringComparer.Ordinal)
        {
            ["region"] = "groups of units appear spread over the event's scope regions.",
            ["aroundplayer"] = "near a random online player, between a nearest and a farthest distance. A player in a castle or in PvP combat is never picked.",
        },
        ["action.behaviour"] = new(StringComparer.Ordinal)
        {
            ["none"] = "the units stay where they appear and fight whoever comes.",
            ["hunt"] = "every few seconds the units go after the nearest players within the hunt range.",
            ["regionhunt"] = "each group goes after the nearest player inside the regions (needs the region lock).",
        },
        ["action.modifiers.level"] = new(StringComparer.Ordinal)
        {
            ["own"] = "each unit keeps its normal level.",
            ["fixed"] = "every unit gets the same level.",
            ["shift"] = "each unit's own level, raised or lowered by a few steps.",
        },
        ["action.waveList.N.start"] = new(StringComparer.Ordinal)
        {
            ["cleared"] = "starts as soon as every unit of the wave before is dead.",
            ["after"] = "starts a set time after the wave before started, cleared or not.",
            ["afterOrCleared"] = "starts after the time, or sooner if the wave before is cleared.",
        },
        ["action.allowTerritory"] = new(StringComparer.Ordinal)
        {
            ["true"] = "castles are ground like any other: units may rise, march and fight on claimed land.",
            ["false"] = "claimed castle land is off limits, and a start on it is refused.",
        },
        ["action.loot"] = new(StringComparer.Ordinal)
        {
            ["true"] = "killed event units drop loot as normal.",
            ["false"] = "killed event units drop nothing.",
        },
    };

    // ================================================================== boxes that aren't event fields

    public static readonly Dictionary<string, string> Inputs = new(StringComparer.Ordinal)
    {
        ["new.id"] = "The new event's id: up to 32 of a-z, 0-9 and -. Every command and the event list use it.",
        ["new.pillar"] = "The pillar the new event runs under: empowerment, spawns, boss, zones or sieges. Admin: Pillars turns a whole pillar on or off.",
        ["copy.from"] = "The id of the event to copy. Copy… on an event fills it in.",
        ["copy.to"] = "The id the copy gets. The copy starts disabled, so it never runs before you've checked it.",
        ["template.as"] = "Optional: the id the created event gets. Leave it empty to use the template's own id (refused if that id is taken).",
        ["zone.name"] = "The defended zone's name.",
        ["zone.radius"] = "How far, in metres, the zone reaches from where you stand.",
        ["announce.text"] = "The message every player sees. No < or > characters.",
        ["reset.target"] = "The player whose stats to wipe, or all for everyone. Press 1 to ask, then 2 to confirm.",
        ["units.radius"] = "How far around you to look for tracked units, in metres. Empty uses the server's own distance.",
        ["spawn.count"] = "How many to spawn. Leave it empty for one.",
        ["spawn.level"] = "The level they spawn at. Empty keeps the unit's own level (and leaves the boxes after it unused).",
        ["spawn.hp"] = "A health multiplier, like 1.5. Empty keeps normal health.",
        ["spawn.power"] = "A power multiplier, like 1.5. Empty keeps normal power.",
        ["pool.name"] = "The new pool's name: a-z, 0-9 and -. Spaces and capitals are turned into that form for you.",
        ["other.poolname"] = "The pool this field is about, if it isn't one of the chips.",
        ["other.value"] = "The value as the server takes it. The server checks it, and its answer shows at the top.",
        ["library.poolname"] = "The name of a pool from the server's pool library (pools.json), like undead-units. The server checks that it exists when it loads the event.",
        ["library.info"] = "A library pool's name, to list its units and the events that use it.",
    };

    // ================================================================== folded guides ("How this works")

    public sealed class Section
    {
        public string Title;
        public string[] Lines;
    }

    private static Section S(string title, params string[] lines) => new() { Title = title, Lines = lines };

    public static readonly Dictionary<string, Section> Sections = new(StringComparer.Ordinal)
    {
        ["editor"] = S("How this editor works",
            "Every box here is one setting of the event, filled in from what the server says now.",
            "Change what you like: a changed setting is marked, and nothing is sent until you press its Set or Save all changes.",
            "The server checks every value. If it refuses one, the reason shows on that setting and nothing after it is sent.",
            "Example: raise Duration, press Save all changes, and the event list shows the new length.",
            "Commonest mistake: closing the editor with unsaved changes. They are dropped."),
        ["basics"] = S("Basics",
            "The name players see and how long the event runs.",
            "Example: a raid that should last a quarter of an hour gets a duration of 900 s.",
            "Apocalypse, infestation and horde march events run much longer than wave events, so their duration range is wider."),
        ["conditions"] = S("Conditions",
            "Conditions only hold back a start the server makes on its own (a schedule, a kill, a timer).",
            "An admin's Start always goes ahead, whatever they say.",
            "Example: Min players 3 and Chance 50% means the event only starts with three players on, and then only half the time.",
            "Commonest mistake: testing with Start and wondering why the conditions did nothing."),
        ["empower"] = S("Empowerment",
            "While it runs, every NPC of the chosen factions gets stronger. The buff wears off by itself when the event ends.",
            "NPCs that load in during the event are buffed within a few seconds.",
            "Example: Faction_Bandits with Physical power x1.5 makes bandit camps hit half again as hard.",
            "Commonest mistake: leaving every stat at x1.0. At least one must be above it."),
        ["spawnwaves"] = S("Spawn waves",
            "Waves of units appear at a place, do their behaviour, and the event ends when the time runs out or every wave is defeated.",
            "Pick where they appear (Location), how far they spread (Spawn radius) and what they do (Behaviour).",
            "Example: Around a player, Hunt players at 40 m: each wave appears near someone and goes straight for them.",
            "Commonest mistake: Pursuit without Hunt players. The server holds the event disabled until it's fixed."),
        ["location"] = S("Location and castles",
            "Where the event happens, and whether it may reach claimed castle land.",
            "\"Castles may be entered\" off: no unit spawns, marches, drifts or hunts on castle land, and a start on it is refused.",
            "\"Castles may be entered\" on: castles are ground like any other.",
            "Example: an apocalypse that refuses to start \"inside a castle's territory\" starts once this is on, or from another spot.",
            "Even when it's off, the game's own AI may chase a player into a castle once a fight has started."),
        ["units"] = S("Units",
            "Each entry is a unit and how many of it, or a draw from a pool.",
            "A pool entry can have a fixed count, or a Range: a random count between a min and a max, rolled again for every wave.",
            "Example: From pool undead-units, Range 5-20, gives each wave between 5 and 20 random undead.",
            "A list with a pool entry can bring at most 500 units in all, counting each range at its max.",
            "Commonest mistake: naming a pool that doesn't exist. Make it under Unit pools, or type a library pool's exact name."),
        ["waves"] = S("Waves",
            "Each wave has its own units, bosses and start rule. Wave 1 starts with the event.",
            "A later wave starts when the one before is cleared, a set time after it, or whichever comes first.",
            "Example: wave 2 After time 60 s starts a minute after wave 1, even if wave 1 is still alive.",
            "Commonest mistake: giving the last wave its own bosses and final-wave bosses too. The server refuses that."),
        ["pools"] = S("Unit pools",
            "A pool is a named mix of units. An entry that names a pool draws its units from it at random, so no two runs look the same.",
            "Pools here belong to this event alone. The server's pool library (pools.json) has more that any event can name.",
            "Save a pool before a list names it. An event's own pool wins over a library pool of the same name.",
            "Example: a pool bandits with three bandit units, then an entry From pool bandits x6.",
            "Commonest mistake: mixing units of rival groups in one pool. They attack each other when they spawn."),
        ["apocalypse"] = S("Apocalypse",
            "One faction rises at its hotspots. Roamers hunt players across the map, and hordes march on the goal.",
            "Destroy every horde to win. If a horde reaches the goal, the goal falls and the event is lost.",
            "Example: three hordes, one every 30 min, against a goal set on a village.",
            "Its units, hotspots and pools live in events.json; only the settings shown here can be changed from Raphael."),
        ["infestation"] = S("Infestation",
            "The dead rise at several places where the faction's own NPCs gather, then spread with every kill they make.",
            "Some hunt the nearest player; the rest drift around their place.",
            "Example: five places, half hunters, each kill raising one or two more.",
            "Commonest mistake: a spread max that is too low, so the infestation stops growing early."),
        ["hordemarch"] = S("Horde march",
            "One great horde rises, marches on its destination and grows with every kill on the way.",
            "On arrival, after two minutes stuck, or once players have watched it for 30 s, it becomes an infestation where it stands.",
            "Example: start at a gathering at least 1000 m from the destination, set on a town.",
            "Commonest mistake: a growth max at or below the horde's size, so it never grows.",
            "Its units must total 10-500: the mins of every range add up to at least 10 and the maxes to at most 500."),
        ["newcopy"] = S("New or copy",
            "New writes an empty event of a pillar; Copy duplicates an existing one under a new id.",
            "Both start disabled. Open the new event with Edit…, set it up, then press Enable.",
            "Example: copy bandit-raid to bandit-raid-hard, then raise its levels.",
            "Commonest mistake: an id with capitals or spaces. Use a-z, 0-9 and - only."),
        ["templates"] = S("Templates",
            "Templates are ready-made events. Create copies one into events.json, disabled.",
            "Type an id first to keep several copies of one template.",
            "Example: Create a horde march template, then Edit… it on Admin: Events and press Enable.",
            "Most templates start where the admin stands: go where the fight should be before pressing Start."),
        ["pillars"] = S("Pillars",
            "A pillar is a family of events with one on / off switch.",
            "Switching a pillar off ends its running events at once and stops new ones. The change is saved to the server's cfg.",
            "Example: turn Spawns off for a quiet evening; every wave event pauses until it's back on.",
            "Commonest mistake: enabling an event whose pillar is off. It never starts."),
        ["zones"] = S("Defended zones",
            "A defended zone is a circle around a spot you stand on. Its state shows whether it is calm, on alert or cooling down.",
            "Example: stand in the middle of the village, type a name and a radius, then press Add here.",
            "Removing a zone deletes it at once."),
        ["unitstab"] = S("Units",
            "Scan lists the units Nyarlathotep tracks around you, with level, health, time left and position.",
            "Test spawn puts units next to you to try them out. They are tracked like event units and show in the scan as manual.",
            "Example: spawn three of a unit at level 60 to see how hard it hits before putting it in an event.",
            "Only the area around you can be listed; go where an event runs and scan."),
        ["poolslibrary"] = S("Pool library",
            "The pool library is the server's pools.json: pools any event can name without defining them.",
            "List shows the pools ten a page, Info shows one pool's units and the events that use it, Reload re-reads the file.",
            "Library pools are edited only in pools.json on the server. Raphael shows them but never changes them.",
            "Example: type undead-units in a pool row's library box to draw from the seeded undead pool.",
            "After editing pools.json, press Reload: the reply names any event that has become invalid."),
    };

    // ================================================================== lookups

    private static readonly Regex UnitEntryN = new(@"^action\.units\.\d+\.", RegexOptions.Compiled);
    private static readonly Regex PoolName = new(@"^action\.pools\.[a-z0-9-]+$", RegexOptions.Compiled);
    private static readonly Regex WaveStart = new(@"^action\.waveList\.\d+\.start$", RegexOptions.Compiled);

    /// <summary>The help key of a field: its generic id (waves N, entries M, pools NAME).</summary>
    public static string Key(string field)
    {
        var f = NyarFieldRules.Generic(field ?? "");
        if (WaveStart.IsMatch(field ?? "")) return "action.waveList.N.start";
        f = UnitEntryN.Replace(f, "action.units.N.");
        if (PoolName.IsMatch(f) && f != "action.pools.NAME") f = "action.pools.NAME";
        return f;
    }

    /// <summary>The prose of <paramref name="field"/>, or null.</summary>
    public static string Prose(string field) => Fields.TryGetValue(Key(field), out var s) ? s : null;

    /// <summary>One option's line of a choice field, or null.</summary>
    public static string Option(string field, string value)
        => Options.TryGetValue(Key(field), out var d) && value != null && d.TryGetValue(value, out var s) ? s : null;

    /// <summary>The tooltip of a field: its meaning, one line per option, "Default x · range a-b", then the raw key in
    /// muted text. <paramref name="options"/> are the control's own (label, value, tip); a missing option line falls back
    /// to the control's tip. <paramref name="fallback"/> is used when the field has no prose.</summary>
    public static string Tooltip(string field, NyarSpec spec, IEnumerable<(string Label, string Value, string Tip)> options = null,
        string defaultText = "", string fallback = null, string mutedHex = "#8E8E8E")
    {
        var sb = new StringBuilder();
        string prose = Prose(field) ?? fallback ?? "";
        if (prose.Length > 0) sb.Append(prose);
        if (options != null)
            foreach (var o in options)
            {
                string line = Option(field, o.Value) ?? o.Tip ?? "";
                if (line.Length == 0) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("• ").Append(o.Label).Append(": ").Append(line);
            }
        var facts = new List<string>();
        if (!string.IsNullOrEmpty(defaultText)) facts.Add("Default " + defaultText);
        if (spec != null) facts.Add("range " + spec.RangeText + (spec.NoneAllowed ? $", or {spec.NoneLabel}" : ""));
        if (facts.Count > 0) { if (sb.Length > 0) sb.Append('\n'); sb.Append(string.Join(" · ", facts)); }
        if (!string.IsNullOrEmpty(field)) { if (sb.Length > 0) sb.Append('\n'); sb.Append($"<color={mutedHex}>{field}</color>"); }
        return sb.ToString();
    }

    /// <summary>The server's default of a field when events.json leaves it out (Nyarlathotep 0.15.0 Definitions.cs), as
    /// the tooltip shows it, or "" when there is none worth naming. <paramref name="family"/>: an apocalypse-type event.</summary>
    public static string DefaultOf(string field, bool family = false)
    {
        string k = Key(field);
        switch (k)
        {
            case "conditions.minPlayers": return "0";
            case "conditions.cooldownMinutes": return "0 min";
            case "conditions.chancePercent": return "100%";
            case "action.marchSpeed": return "2.0 m/s";
            case "action.loot": case "action.allowTerritory": case "action.regionLock": case "action.growth.whenCleared":
            case "trigger.shared": case "action.arena": case "action.winner": case "action.waveList.N.whenCleared":
                return "No";
            case "action.scoreboard": return family ? "Yes" : "No";
        }
        var s = NyarFieldRules.Spec(field ?? "", family);
        return s != null && s.NoneAllowed ? s.NoneLabel : "";
    }

    /// <summary>A non-field box's help, or null.</summary>
    public static string Input(string key) => Inputs.TryGetValue(key ?? "", out var s) ? s : null;
}
