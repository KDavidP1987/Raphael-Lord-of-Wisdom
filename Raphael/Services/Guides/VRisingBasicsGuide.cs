using System.Collections.Generic;

namespace Raphael.Services.Guides;

// 0.73.0: V Rising Basics (Settings and Help) on the guide kit. A General guide: the base game in plain language,
// then what each server mod changes. Text carried over from the old bespoke builder; the migration inventory
// (tests/fixtures/guide-migration/vrisingbasics.json) pins that nothing was dropped.
internal static partial class Guides
{
    static partial void AddVRisingBasics(List<Guide> list)
    {
        var b = new GuideBuilder("vrisingbasics", "V Rising Basics",
            "The base game in plain language, and what each server mod changes.",
            null, GuideKind.General);

        b.Section("v-rising-how-game-works", "V Rising — how the game works", true, s =>
        {
            s.P("New to V Rising? This is a plain-language guide to the base game — what blood, gear level, V Bloods " +
                "and castles actually do — followed by a summary of how the server mods Raphael supports change the " +
                "experience. Raphael itself is a UI mod: it does not change the game, it just gives you a nicer " +
                "interface for it and the mods.");
            s.Callout(CalloutKind.Note, "Heads-up: Stunlock patch the game over time and most servers tweak their settings, so treat the " +
                "numbers below as defaults/typical values. When something seems off for your server, ask your admin " +
                "or check the official wiki on the Game Guide tab.");
        });

        b.Section("blood-sunlight-staying", "Blood, sunlight, and staying alive", true, s =>
        {
            s.P("You are a vampire. Two things matter from minute one: blood keeps you powered, and the sun kills " +
                "you. Daylight burns you fast in the open — travel in shade, under cover, or via the world's shadow " +
                "paths, and build a roof over your base early. Garlic exposure, holy ground, and silver all hurt " +
                "vampires too (see Map, travel & vampire weaknesses, below).");
            s.Detail("Details — Blood types & quality", d =>
            {
                d.Bullets(
                    "Feeding on a creature gives you its BLOOD TYPE — Warrior, Rogue, Brute, Scholar, Worker, Creature, " +
                    "Draculin, Mutant, Corruption and more — each granting a different set of combat bonuses while you " +
                    "carry it.",
                    "Blood has a QUALITY percentage (0–100%). Higher quality = stronger bonuses, and at high quality you " +
                    "unlock extra bonus tiers for that blood type. Feeding from a higher-quality victim raises your " +
                    "blood; taking hits and time lower it.",
                    "Your BLOOD POOL is also your resource bar for some abilities and your sustain — it drains in " +
                    "sunlight and over time, so keep topped up by feeding.",
                    "Match your blood type to your playstyle: e.g. Warrior for melee damage, Rogue for crit/move speed, " +
                    "Brute for sustain, Scholar for spell power. The right blood at high quality is a real power spike, " +
                    "separate from your gear.");
            });
            s.Detail("With the mods — Blood", d =>
            {
                d.Bullets(
                    "BLOODCRAFT adds Blood Legacies: each blood type levels up the more you use it, granting permanent " +
                    "stat bonuses on top of vanilla blood bonuses, plus prestige on each legacy. See the Blood Legacy tab " +
                    "and the Bloodcraft Guide for the full breakdown.");
            });
        });

        b.Section("gear-level-level", "Gear Level is your \"level\"", true, s =>
        {
            s.P("Vanilla V Rising has NO experience bar or character levels. Your power is your GEAR LEVEL — a single " +
                "number computed from the tier of your equipped weapon, armor, and the spells/jewels you have " +
                "slotted. Better gear = higher gear level = you hit harder and take less from same-level enemies.");
            s.P("Every enemy and boss has a level too. Fighting something far above your gear level means your hits " +
                "are heavily reduced and theirs are amplified — so the loop is: beat content near your level, craft " +
                "the better gear it unlocks, raise your gear level, move to the next tier.");
            s.Detail("Details — Weapons, spells & vampire powers", d =>
            {
                d.Bullets(
                    "WEAPONS: each weapon type (mace, sword, spear, crossbow, slashers, greatsword, pistols, longbow, " +
                    "twinblades, whip, axes, …) has its own feel plus two special attacks. Swapping weapons to suit the " +
                    "fight is normal play.",
                    "SPELLS come in schools — Blood, Unholy, Chaos, Frost, Illusion, Storm. You slot abilities into your " +
                    "spell slots and an ultimate; JEWELS modify and upgrade individual spells.",
                    "VAMPIRE POWERS: dodge/travel abilities, the bat form for fast travel (unlocked later), and utility " +
                    "powers all come from progression.",
                    "Most new abilities, spells, and recipes are unlocked by defeating V Blood bosses (next section) — " +
                    "that's the spine of vanilla progression.");
            });
            s.Detail("With the mods — Combat & progression", d =>
            {
                d.Bullets(
                    "BLOODCRAFT bolts a full RPG layer onto vanilla's gear-only power: character XP and levels, Classes, " +
                    "Weapon Expertise (weapons level with use and grant stats), and Prestige. Your effective power " +
                    "becomes gear + level + expertise + legacy + class, not just gear. See the Class, Weapon (Expertise), " +
                    "Levels and Prestige tabs, and the Bloodcraft Guide.",
                    "BEELZEBUB lets you capture enemy abilities and equip them in custom loadouts, and unlocks transform " +
                    "forms (Dracula / Morgana). See the Beelzebub Guide.");
            });
        });

        b.Section("v-bloods-backbone-of", "V Bloods: the backbone of progress", true, s =>
        {
            s.P("V Bloods are special named bosses scattered across the world, each at a set level. Defeating one " +
                "lets you consume its powerful blood, which permanently UNLOCKS rewards: new spells, weapon " +
                "abilities, crafting recipes, structures, and travel powers. You hunt them roughly in level order.");
            s.P("Use the in-game Blood Altar to track and locate the next V Bloods available to you — it's your to-do " +
                "list for the whole game. Clearing V Bloods is what raises your crafting options and, with it, your " +
                "gear level.");
            s.Detail("Details — Crafting, refinement & servants", d =>
            {
                d.Bullets(
                    "RESOURCES flow through refinement chains: raw materials → refined goods (planks, ingots, scrolls, " +
                    "etc.) at the right station → crafted gear/structures. Higher tiers need stations and recipes " +
                    "unlocked from V Bloods.",
                    "SERVANTS: later you can convert defeated humans into castle servants, sending them on expeditions " +
                    "for loot/resources. Servant power scales with the blood you converted them with.",
                    "HORSES can be tamed for fast overland travel; the bat form (late unlock) gives flight between " +
                    "discovered points.",
                    "Inventory/crafting management is a big time sink in vanilla — which is exactly what some mods " +
                    "streamline (see below).");
            });
            s.Detail("With the mods — Progression & logistics", d =>
            {
                d.Bullets(
                    "BLOODCRAFT adds Familiars (defeated enemies can become summonable combat companions, stored in " +
                    "boxes), Professions (gathering/crafting skills that level up), and Daily Quests. See the Familiars / " +
                    "Boxes / All Familiars / Daily Quest tabs and the Bloodcraft Guide.",
                    "KINDREDLOGISTICS automates inventory: auto-stashing, pulling from network storage, and sorting — " +
                    "removing much of the manual hauling. See the Kindred Logistics tab.",
                    "URIEL adds shared storage between players, public prisons, live stair restyling, and spawning " +
                    "decorative/utility objects, plus Castle Events (defend your castle against waves). See the Uriel " +
                    "Guide.");
            });
        });

        b.Section("castle-heart-territory", "Castle Heart, territory & upkeep", true, s =>
        {
            s.P("Your base is built around a CASTLE HEART placed on a territory plot. The Heart claims the " +
                "surrounding territory, powers your crafting stations and defenses, and must be FUELED with blood " +
                "essence (gathered from kills / greater blood essence). Let it run dry and your castle enters DECAY — " +
                "structures degrade and eventually can be lost.");
            s.P("Upgrading the Heart raises your castle level, which increases your servant cap and territory size. " +
                "Plots are limited, so good early spots get claimed fast on busy servers.");
            s.Detail("Details — Raiding, decay & offline protection", d =>
            {
                d.Bullets(
                    "On PvP servers, castles can be raided during set windows; on PvE servers they can't. Which applies, " +
                    "the raid windows, decay rates, and offline-protection rules are all SERVER SETTINGS — ask your admin " +
                    "or check the server's rules.",
                    "Keep the Heart fueled and your blood-essence stock up, especially before logging off, so you don't " +
                    "return to a decaying castle.",
                    "Territory and plot availability change as players come and go — a freed plot becomes claimable.");
            });
            s.Detail("With the mods — Castles & server info", d =>
            {
                d.Bullets(
                    "FAUST is an information/investigation mod: it surfaces castle ownership, plot size/availability, " +
                    "decay timers, player playtime, V Blood boss status, leaderboards, and a world map — extremely handy " +
                    "for finding open plots or planning. See the Faust Guide tab.",
                    "KINDREDCOMMANDS gives admins the command tools to manage players, castles, and the server.");
            });
        });

        b.Section("map-travel-vampire", "Map, travel & vampire weaknesses", true, s =>
        {
            s.P("The world is divided into regions that rise in danger and level as you go outward. Discover WAYGATES " +
                "to fast-travel between them (you can't carry most resources through waygates by default). World " +
                "bosses, resource nodes, and points of interest dot each region.");
            s.P("Vampire hazards to respect: SUNLIGHT burns you in the open during the day; GARLIC exposure stacks a " +
                "debuff indoors in human settlements; HOLY ground and holy enemies deal bonus damage; and SILVER " +
                "(including silver coins in your inventory) burns you until you have silver resistance. Resistances " +
                "are improved through gear, consumables, and progression.");
            s.Detail("Details — Difficulty & PvP vs PvE", d =>
            {
                d.Bullets(
                    "Servers run at different difficulties (e.g. relaxed → standard → brutal), which scale enemy strength " +
                    "and your durability.",
                    "PvE servers focus on co-op/solo progression with no castle raiding or player-vs-player combat; PvP " +
                    "servers add player combat and (windowed) raiding. The exact ruleset is server-defined.",
                    "Clan size, drop rates, blood quality, and many other knobs vary per server — your experience can " +
                    "differ a lot from another server's.");
            });
        });

        b.Section("what-each-server-mod", "What each server mod adds", true, s =>
        {
            s.P("Raphael is a unified UI for several server-side mods. WHICH of these you actually have depends on " +
                "your server — Raphael auto-detects them and shows the matching tab groups. Here's the short version " +
                "of what each one does; open its guide in Settings and Help for everything else.");
            s.Detail("Bloodcraft — RPG progression", d =>
            {
                d.P("Turns gear-only vanilla into a full RPG: character XP & levels, Classes, Weapon Expertise, Blood " +
                    "Legacies, Prestige (incl. endgame Exo prestige + Exoform shapeshifts), Familiars, Professions, and " +
                    "Daily Quests. The biggest gameplay change of the set. → Bloodcraft Guide.");
            });
            s.Detail("Beelzebub — abilities & transforms", d =>
            {
                d.P("Capture abilities from enemies and slot them into custom loadouts (universal + per-weapon), bind " +
                    "extra abilities to hotkeys, and unlock transform forms (Dracula / Morgana). → Beelzebub Guide.");
            });
            s.Detail("Uriel — sharing, prisons & building", d =>
            {
                d.P("Share storage chests with other players (with permissions/limits), run public prisons, restyle " +
                    "stairs live, spawn an unlocked palette of decorative/utility objects, and run Castle Events: waves " +
                    "of enemies that attack a castle, for rewards. → Uriel Guide.");
            });
            s.Detail("Faust — server information", d =>
            {
                d.P("A read-only intel layer: castle/plot info, decay watch, player playtime & frequency, V Blood boss " +
                    "status, kill/playtime leaderboards, and a filterable world map. Great for finding plots, planning " +
                    "hunts, and admin oversight. → Faust Guide.");
            });
            s.Detail("Nyarlathotep — world events", d =>
            {
                d.P("Admin-run world events: spawn waves, empowered enemies, bosses that pursue players, regional rules " +
                    "and announcements, started by pillars, timers or triggers. Players see what is running and where. → " +
                    "Nyarlathotep Guide.");
            });
            s.Detail("Kindred suite — logistics & admin", d =>
            {
                d.P("KindredLogistics automates inventory (auto-stash, network pull, sorting). KindredCommands is an " +
                    "admin/player command toolkit. → Kindred Guide.");
            });
        });

        b.Section("want-more-detail", "Want more detail?", true, s =>
        {
            s.P("This guide is a primer. For authoritative base-game detail (exact numbers, maps, boss locations) use " +
                "the official wiki and community links on the Game Guide tab. For each mod's full mechanics and " +
                "command list, use its guide in this same Settings and Help group. Anything server-specific — drop " +
                "rates, raid windows, rewards — is best confirmed with your server admin.");
        });

        list.Add(b.Build());
    }
}
