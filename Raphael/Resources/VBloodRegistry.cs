using System.Collections.Generic;

namespace Raphael.Resources;

// 0.10.0: canonical list of V-Blood familiar names, mirrored from Bloodcraft
// v1.13.x — Utilities/Familiars.cs::VBloodNamePrefabGuidMap. 65 entries; if
// Bloodcraft adds or removes V-Bloods this list must stay in sync (otherwise
// the V-Blood tab either misses new ones or shows phantom "not captured"
// rows for names that no longer exist server-side).
//
// We do NOT include "Primal" variants here. The scanner derives those at
// runtime by searching ".fam s "Primal <basename>"" alongside the base
// search and tracking the two results separately (basic + primal flags
// per slot). A V-Blood that has no primal form just returns empty for the
// primal search; no harm done.
//
// PORT REFERENCE: LearningMods/Bloodcraft-main/Utilities/Familiars.cs:169-235
public static class VBloodRegistry
{
    public static readonly string[] All = new[]
    {
        "Adam the Firstborn",
        "Albert the Duke of Balaton",
        "Alpha the White Wolf",
        "Angram the Purifier",
        "Azariel the Sunbringer",
        "Bane the Shadowblade",
        "Baron du Bouchon the Sommelier",
        "Beatrice the Tailor",
        "Ben the Old Wanderer",
        "Christina the Sun Priestess",
        "Clive the Firestarter",
        "Cyril the Cursed Smith",
        "Dantos the Forgebinder",
        "Domina the Blade Dancer",
        "Dracula the Immortal King",
        "Errol the Stonebreaker",
        "Finn the Fisherman",
        "Foulrot the Soultaker",
        "Frostmaw the Mountain Terror",
        "Gaius the Cursed Champion",
        "General Cassius the Betrayer",
        "General Elena the Hollow",
        "General Valencia the Depraved",
        "Gorecrusher the Behemoth",
        "Goreswine the Ravager",
        "Grayson the Armourer",
        "Grethel the Glassblower",
        "Henry Blackbrew the Doctor",
        "Jade the Vampire Hunter",
        "Jakira the Shadow Huntress",
        "Keely the Frost Archer",
        "Kodia the Ferocious Bear",
        "Kriig the Undead General",
        "Leandra the Shadow Priestess",
        "Lidia the Chaos Archer",
        "Lord Styx the Night Champion",
        "Lucile the Venom Alchemist",
        "Mairwyn the Elementalist",
        "Maja the Dark Savant",
        "Matka the Curse Weaver",
        "Megara the Serpent Queen",
        "Meredith the Bright Archer",
        "Morian the Stormwing Matriarch",
        "Nicholaus the Fallen",
        "Octavian the Militia Captain",
        "Polora the Feywalker",
        "Putrid Rat",
        "Quincey the Bandit King",
        "Raziel the Shepherd",
        "Rufus the Foreman",
        "Simon Belmont the Vampire Hunter",
        "Sir Erwin the Gallant Cavalier",
        "Sir Magnus the Overseer",
        "Solarus the Immaculate",
        "Stavros the Carver",
        "Talzur the Winged Horror",
        "Terah the Geomancer",
        "Terrorclaw the Ogre",
        "Tristan the Vampire Hunter",
        "Ungora the Spider Queen",
        "Vincent the Frostbringer",
        "Voltatia the Power Master",
        "Willfred the Village Elder",
        "Ziva the Engineer",
    };

    private static readonly HashSet<string> _set = new(All, System.StringComparer.OrdinalIgnoreCase);

    // 0.11.0: precomputed "primal stem" → canonical-name map. Bloodcraft
    // server-side names primal V-Bloods as "Primal <stem>" where <stem> is
    // the registry name with " the X" stripped (per Bloodcraft CHANGELOG
    // examples: '.fam sb "Primal Frostmaw"', '.fam sb "Primal Polora"').
    // For entries without " the " in the name (currently only "Putrid Rat"),
    // the stem is the full name. Used by the scanner to route the
    // "Primal Adam" reply back to the "Adam the Firstborn" registry entry.
    private static readonly Dictionary<string, string> _stemToCanonical = BuildStemMap();

    private static Dictionary<string, string> BuildStemMap()
    {
        var map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var name in All)
        {
            int theIdx = name.IndexOf(" the ", System.StringComparison.OrdinalIgnoreCase);
            var stem = theIdx > 0 ? name.Substring(0, theIdx) : name;
            // Last write wins on collision; with the current 65-entry list
            // every stem is unique (verified visually — see Familiars.cs
            // ref). If Bloodcraft adds a colliding pair the scanner will
            // map both occurrences to whichever the dictionary ends on,
            // which is wrong but rare; we'd add a disambiguation rule then.
            map[stem] = name;
        }
        // 0.65.0: Bloodcraft's primal-name override spells this one "Primal Poloma", but the
        // registry (and the game) call the base V-Blood "Polora the Feywalker" — so "Primal
        // Polora" classifies fine while "Primal Poloma" fell through and the primal was dropped.
        // Map the Bloodcraft spelling back to the canonical name. (Independent of localization —
        // this is an English-vs-English mismatch in Bloodcraft's own hardcoded primal table.)
        map["Poloma"] = "Polora the Feywalker";
        return map;
    }

    /// <summary>Case-insensitive membership test against the canonical list.</summary>
    public static bool Contains(string name)
        => !string.IsNullOrEmpty(name) && _set.Contains(name);

    /// <summary>0.11.0: resolve a "Primal &lt;suffix&gt;" reply back to the
    /// base V-Blood. Returns true and populates <paramref name="canonical"/>
    /// when <paramref name="suffix"/> is the part-before-" the " of a known
    /// registry name (e.g., "Frostmaw" → "Frostmaw the Mountain Terror").</summary>
    public static bool TryResolvePrimalStem(string suffix, out string canonical)
    {
        canonical = null;
        if (string.IsNullOrEmpty(suffix)) return false;
        return _stemToCanonical.TryGetValue(suffix.Trim(), out canonical);
    }

    // 0.10.2: region grouping mirrored from FamBook's vbloods.json page layout.
    // The seven pages correspond loosely to V Rising progression / region tiers:
    //   1 = Farbane Woods (early Farbane V-Bloods)
    //   2 = Dunley Farmlands
    //   3 = Silverlight Hills / Cursed Forest
    //   4 = Hallowed Mountains / mid-progression
    //   5 = Gloomrot / late progression
    //   6 = Ruins of Mortium / Oakveil / endgame
    //   7 = Endgame V-Bloods (Dracula, etc.)
    //
    // Used by the Location sort mode in the V-Bloods tab + Familiar Browser
    // overlay so the list orders by where the V-Blood was originally
    // encountered. The names are best-effort labels — V Rising's actual
    // region geography is more nuanced (some bosses cross zones).
    //
    // The PrimalEchoes purchase path (.fam echoes) uses substring matching
    // against Bloodcraft's VBloodNamePrefabGuidMap, so "Putrid Rat" here
    // matches in-game "Nibbles the Putrid Rat" via FamBook's page1 entry —
    // the registry name aligns with Bloodcraft's map keys, the region
    // mapping aligns with FamBook's pagination.
    private static readonly Dictionary<string, int> _pageByName =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            // page 1 — Farbane Woods
            { "Alpha the White Wolf", 1 },
            { "Keely the Frost Archer", 1 },
            { "Errol the Stonebreaker", 1 },
            { "Rufus the Foreman", 1 },
            { "Grayson the Armourer", 1 },
            { "Goreswine the Ravager", 1 },
            { "Lidia the Chaos Archer", 1 },
            { "Clive the Firestarter", 1 },
            { "Putrid Rat", 1 }, // FamBook lists as "Nibbles the Putrid Rat"; Bloodcraft's map uses the shorter form.
            { "Finn the Fisherman", 1 },
            // page 2 — Dunley Farmlands
            { "Polora the Feywalker", 2 },
            { "Kodia the Ferocious Bear", 2 },
            { "Nicholaus the Fallen", 2 },
            { "Quincey the Bandit King", 2 },
            { "Beatrice the Tailor", 2 },
            { "Vincent the Frostbringer", 2 },
            { "Christina the Sun Priestess", 2 },
            { "Tristan the Vampire Hunter", 2 },
            { "Sir Erwin the Gallant Cavalier", 2 },
            { "Kriig the Undead General", 2 },
            // page 3 — Silverlight Hills / Cursed Forest
            { "Leandra the Shadow Priestess", 3 },
            { "Maja the Dark Savant", 3 },
            { "Bane the Shadowblade", 3 },
            { "Grethel the Glassblower", 3 },
            { "Meredith the Bright Archer", 3 },
            { "Terah the Geomancer", 3 },
            { "Frostmaw the Mountain Terror", 3 },
            { "General Elena the Hollow", 3 },
            { "Gaius the Cursed Champion", 3 },
            { "General Cassius the Betrayer", 3 },
            // page 4 — Hallowed Mountains / mid-progression
            { "Jade the Vampire Hunter", 4 },
            { "Raziel the Shepherd", 4 },
            { "Octavian the Militia Captain", 4 },
            { "Ziva the Engineer", 4 },
            { "Domina the Blade Dancer", 4 },
            { "Angram the Purifier", 4 },
            { "Ungora the Spider Queen", 4 },
            { "Ben the Old Wanderer", 4 },
            { "Foulrot the Soultaker", 4 },
            { "Albert the Duke of Balaton", 4 },
            // page 5 — Gloomrot / late progression
            { "Willfred the Village Elder", 5 },
            { "Cyril the Cursed Smith", 5 },
            { "Sir Magnus the Overseer", 5 },
            { "Baron du Bouchon the Sommelier", 5 },
            { "Morian the Stormwing Matriarch", 5 },
            { "Mairwyn the Elementalist", 5 },
            { "Henry Blackbrew the Doctor", 5 },
            { "Jakira the Shadow Huntress", 5 },
            { "Stavros the Carver", 5 },
            { "Lucile the Venom Alchemist", 5 },
            // page 6 — Ruins of Mortium / Oakveil
            { "Matka the Curse Weaver", 6 },
            { "Terrorclaw the Ogre", 6 },
            { "Azariel the Sunbringer", 6 },
            { "Voltatia the Power Master", 6 },
            { "Simon Belmont the Vampire Hunter", 6 },
            { "Dantos the Forgebinder", 6 },
            { "Lord Styx the Night Champion", 6 },
            { "Gorecrusher the Behemoth", 6 },
            { "General Valencia the Depraved", 6 },
            { "Solarus the Immaculate", 6 },
            // page 7 — Endgame
            { "Talzur the Winged Horror", 7 },
            { "Adam the Firstborn", 7 },
            { "Megara the Serpent Queen", 7 },
            { "Dracula the Immortal King", 7 },
        };

    /// <summary>0.10.9: canonical-case lookup. Bloodcraft localized names
    /// are stable as written in <see cref="All"/>; this exists so a
    /// case-mismatched input (e.g., "alpha the white wolf") still maps
    /// to the same dictionary key used elsewhere in the codebase.
    /// Returns the input unchanged when it isn't a known V-Blood.</summary>
    public static string CanonicalNameOf(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        // _set is OrdinalIgnoreCase, so we iterate All once to find
        // the canonical-cased entry. Cheap — All is ~65 entries.
        foreach (var candidate in All)
            if (string.Equals(candidate, name, System.StringComparison.OrdinalIgnoreCase))
                return candidate;
        return name;
    }

    // 0.17.3: in-game display names that DON'T exactly equal a registry entry,
    // mapped to their canonical registry name. The registry mirrors Bloodcraft's
    // VBloodNamePrefabGuidMap keys (a short form for some units), but Bloodcraft's
    // localized capture/box-list replies carry the FULL in-game name — so those
    // captures silently failed to register. Reported case: the Putrid Rat is
    // "Nibbles the Putrid Rat" in game but "Putrid Rat" in the registry, so a
    // captured "Nibbles the Putrid Rat" never matched. Add aliases here as more
    // discrepancies surface.
    private static readonly Dictionary<string, string> _aliases =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            { "Nibbles the Putrid Rat", "Putrid Rat" },
        };

    /// <summary>0.17.3: resolve an in-game display name to its canonical registry
    /// name, accepting both exact registry names AND known aliases (full in-game
    /// names that differ from the registry form). Returns null when unrecognized.</summary>
    public static string ResolveCanonical(string ingameName)
    {
        if (string.IsNullOrEmpty(ingameName)) return null;
        var n = ingameName.Trim();
        if (_set.Contains(n)) return CanonicalNameOf(n);
        if (_aliases.TryGetValue(n, out var canonical)) return canonical;
        return null;
    }

    /// <summary>0.10.2: page/region order key for the Location sort. Returns 99
    /// (sinks to bottom) for any name not in the map — defensive, the static
    /// list should cover every entry in <see cref="All"/>.</summary>
    public static int RegionOrderFor(string name)
        => !string.IsNullOrEmpty(name) && _pageByName.TryGetValue(name, out var page) ? page : 99;

    /// <summary>Friendly region label for the page number, used in any UI
    /// surface that wants to show the grouping (currently sort-key only —
    /// rows render just the name, but future versions could add a region
    /// column).</summary>
    public static string RegionLabelFor(string name) => RegionOrderFor(name) switch
    {
        1 => "Farbane Woods",
        2 => "Dunley Farmlands",
        3 => "Silverlight Hills",
        4 => "Hallowed Mountains",
        5 => "Gloomrot",
        6 => "Ruins of Mortium",
        7 => "Endgame",
        _ => "Unknown",
    };
}
