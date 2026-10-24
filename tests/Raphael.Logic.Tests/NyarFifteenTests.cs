using Raphael.Services.Nyar;

// 0.74.0: Nyarlathotep 0.15.0 "horde pools" (PLAN 2026-10-24 Part A). Server shapes from Nyarlathotep 0.15.0:
// CommandArgs.PoolEntry (a pool range, both 1-500, min <= max), Validation.cs (HordeMarch totals, the load reasons),
// EventAdmin.EntryText ("pool X ×min-max" on wave / family lines), EventAdmin.cs:967 (a library pool's head line),
// EventAdmin.cs:988 ("claimed territory allowed") and ApocalypseHost (a castle start refused as state/condition, arg id).
internal static partial class Program
{
    private static void NyarFifteenTests()
    {
        PoolRanges();
        HordeMarchTotals();
        RangeRoundTrip();
        RangeReadBack();
        CastleEntry();
        VersionGate();
        ReasonTexts();
    }

    private static NyarEntry PoolE(string pool, int count) => new() { Pool = pool, Count = count, Min = count, Max = count };
    private static NyarEntry RangeE(string pool, int min, int max) => new() { Pool = pool, Count = max, Min = min, Max = max, Ranged = true };
    private static NyarEntry UnitE(string unit, int count) => new() { Unit = unit, Count = count };

    // ------------------------------------------------------------------ CheckPoolRange / CheckUnitList
    private static void PoolRanges()
    {
        Ok(NyarFieldRules.CheckPoolRange("5-20", out var a, out var b), "range 5-20");
        Check(a == 5 && b == 20, "range 5-20 parsed");
        Ok(NyarFieldRules.CheckPoolRange("1-500", out _, out _), "range 1-500");
        Ok(NyarFieldRules.CheckPoolRange("7-7", out _, out _), "range 7-7 (min = max)");
        foreach (var bad in new[] { "0-5", "1-0", "20-5", "5-501", "501-501", "99999999999-5", "5-99999999999", "5-", "-20", "5-10-20", "6", "a-b", "" })
            Bad(NyarFieldRules.CheckPoolRange(bad, out _, out _), $"range '{bad}'");
        Check(NyarFieldRules.CheckPoolRange("0-5", out _, out _).StartsWith("min must be 1-500"), "min message");
        Check(NyarFieldRules.CheckPoolRange("20-5", out _, out _).StartsWith("max must be min-500"), "max message");
        Check(NyarFieldRules.CheckPoolRange("5-", out _, out _).Contains("not both"), "half range message");

        Ok(NyarFieldRules.CheckUnitList("pool:troops:5-20", false), "units list with a range");
        Ok(NyarFieldRules.CheckUnitList("CHAR_Bandit_Thug:4,pool:troops:5-20", true), "wave list with a range");
        Bad(NyarFieldRules.CheckUnitList("pool:troops:0-20", false), "range min 0 in a list");
        Bad(NyarFieldRules.CheckUnitList("pool:troops:5-501", false), "range max 501 in a list");
        Bad(NyarFieldRules.CheckUnitList("pool:troops:20-5", false), "reversed range in a list");
        Bad(NyarFieldRules.CheckUnitList("pool:troops:5-", false), "half range in a list");
        Bad(NyarFieldRules.CheckUnitList("CHAR_Bandit_Thug:5-20", false), "a fixed unit can't take a range");
        // A wave's total counts a range at its max (server PastWaveSize): 300 + 250 > 500.
        Bad(NyarFieldRules.CheckUnitList("pool:troops:1-300,pool:elite:1-250", false), "two ranges past 500 at their max");
        Ok(NyarFieldRules.CheckUnitList("pool:troops:1-300,pool:elite:1-200", false), "two ranges at 500 at their max");
        Eq(NyarFieldRules.TotalUnits(new[] { RangeE("a", 5, 20), PoolE("b", 3), UnitE("CHAR_A", 2) }), 25, "total counts a range at its max");
    }

    // ------------------------------------------------------------------ HordeMarch totals (Validation.cs:714-716)
    private static void HordeMarchTotals()
    {
        Ok(NyarFieldRules.CheckHordeMarchTotals(new[] { UnitE("CHAR_A", 10) }), "horde of exactly 10");
        Ok(NyarFieldRules.CheckHordeMarchTotals(new[] { UnitE("CHAR_A", 500) }), "horde of exactly 500");
        Bad(NyarFieldRules.CheckHordeMarchTotals(new[] { UnitE("CHAR_A", 9) }), "horde of 9");
        Bad(NyarFieldRules.CheckHordeMarchTotals(new[] { UnitE("CHAR_A", 501) }), "horde of 501");
        // Least uses each range's min; most its max.
        Bad(NyarFieldRules.CheckHordeMarchTotals(new[] { RangeE("a", 5, 400), PoolE("b", 4) }), "mins total 9");
        Ok(NyarFieldRules.CheckHordeMarchTotals(new[] { RangeE("a", 6, 400), PoolE("b", 4) }), "mins total 10, maxes 404");
        Bad(NyarFieldRules.CheckHordeMarchTotals(new[] { RangeE("a", 10, 300), RangeE("b", 10, 201) }), "maxes total 501");
        Check(NyarFieldRules.CheckHordeMarchTotals(new[] { RangeE("a", 5, 400), PoolE("b", 4) }).Contains("fewest"), "least message names the fewest");
        Check(NyarFieldRules.CheckHordeMarchTotals(new[] { RangeE("a", 10, 300), RangeE("b", 10, 201) }).Contains("most"), "most message names the most");
    }

    // ------------------------------------------------------------------ ParseEntries / BuildEntries / SetRanged / AddPool
    private static void RangeRoundTrip()
    {
        var rows = NyarFieldRules.ParseEntries("CHAR_Bandit_Thug:4,pool:troops:5-20,pool:elite:3", false);
        Check(rows != null && rows.Count == 3, "three entries read");
        if (rows == null || rows.Count != 3) return;
        Check(rows[1].IsPool && rows[1].Ranged && rows[1].Min == 5 && rows[1].Max == 20, "range entry read");
        Eq(rows[1].Least, 5, "range least"); Eq(rows[1].Most, 20, "range most");
        Check(rows[2].IsPool && !rows[2].Ranged && rows[2].Count == 3, "count pool entry read");
        Eq(NyarFieldRules.BuildEntries(rows, false), "CHAR_Bandit_Thug:4,pool:troops:5-20,pool:elite:3", "round trip");

        NyarFieldRules.SetRanged(rows[2], true);
        Check(rows[2].Ranged && rows[2].Min == 1 && rows[2].Max == 3, "count → range keeps the count as the max");
        Eq(NyarFieldRules.BuildEntries(rows, false), "CHAR_Bandit_Thug:4,pool:troops:5-20,pool:elite:1-3", "after switch to range");
        NyarFieldRules.SetRanged(rows[1], false);
        Check(!rows[1].Ranged && rows[1].Count == 20, "range → count takes the max");
        NyarFieldRules.SetRanged(rows[0], true);
        Check(!rows[0].Ranged, "a fixed unit never becomes a range");

        // Adding the same pool again merges into a count entry, never into a range.
        var l = NyarFieldRules.ParseEntries("pool:troops:5-20", false);
        NyarFieldRules.AddPool(l, "troops", 1, 1, 1, false, mergeRepeats: true);
        Eq(NyarFieldRules.BuildEntries(l, false), "pool:troops:5-20,pool:troops:1", "add next to a range adds an entry");
        NyarFieldRules.AddPool(l, "troops", 2, 1, 1, false, mergeRepeats: true);
        Eq(NyarFieldRules.BuildEntries(l, false), "pool:troops:5-20,pool:troops:3", "add merges into the count entry");
        // Bosses keep their min-max roll form.
        var bo = NyarFieldRules.ParseEntries("pool:elite:1-2", true);
        Eq(NyarFieldRules.BuildEntries(bo, true), "pool:elite:1-2", "boss roll round trip");
    }

    // ------------------------------------------------------------------ info read-back (EntryText, library heads)
    private static void RangeReadBack()
    {
        Eq(NyarInfoParse.EntrySetForm("pool rabble ×5-20", out var ex), "pool:rabble:5-20", "range entry as printed");
        Check(!ex, "no extras");
        Eq(NyarInfoParse.EntrySetForm("pool rabble ×5-20 L30", out ex), "pool:rabble:5-20", "range entry with a level");
        Check(ex, "level is an extra");
        Eq(NyarInfoParse.EntrySetForm("6 from troops", out _), "pool:troops:6", "count entry as printed");
        Eq(NyarInfoParse.EntrySetForm("CHAR_A ×4", out _), "CHAR_A:4", "fixed entry");
        Eq(NyarInfoParse.EntrySetForm("pool Rabble ×5-20", out _), null, "a pool name the server never prints is unread");

        var waveList = new[]
        {
            "wv \"Waves\" disabled pillar spawns trigger manual duration 1200s",
            "action: 2 waves, radius 10, in the scope regions",
            "wave 1: 24 units (CHAR_A ×4, pool rabble ×5-20), at start",
            "wave 2: 6 units (6 from troops), when cleared",
            "pools: troops (2 units): CHAR_Undead_SkeletonSoldier_Base, CHAR_Undead_ZealousCultist_Ghost",
            "pools: rabble (library, 12 units): CHAR_Undead_Ghoul, CHAR_Undead_SkeletonSoldier_Base",
            "scoreboard: off",
        };
        var misses = new List<string>();
        NyarInfoParse.OnMiss = s => misses.Add(s);
        var waves = NyarInfoParse.Waves(waveList);
        NyarInfoParse.OnMiss = null;
        Eq(waves.Count, 2, "two waves");
        if (waves.Count == 2)
        {
            Eq(waves[0].Units, "CHAR_A:4,pool:rabble:5-20", "wave 1 reads its range");
            Eq(waves[1].Units, "pool:troops:6", "wave 2 count entry");
        }
        Eq(misses.Count, 0, "nothing missed on a readable reply");

        var pools = NyarInfoParse.Pools(waveList, out _);
        Check(pools.Count == 1 && pools[0].Key == "troops", "event pools skip the library pool");
        Eq(string.Join(",", NyarInfoParse.LibraryPools(waveList)), "rabble", "library pools listed");
        var load = NyarForm.Read(waveList);
        Eq(string.Join(",", load.LibraryPools), "rabble", "form load carries the library pools");

        // A wave that can't be read is reported through OnMiss.
        misses.Clear();
        NyarInfoParse.OnMiss = s => misses.Add(s);
        NyarInfoParse.Waves(new[] { "wave 1: 4 units (something odd), at start" });
        NyarInfoParse.OnMiss = null;
        Check(misses.Count > 0, "an unreadable entry is reported");
    }

    // ------------------------------------------------------------------ allowTerritory for every action type
    private static readonly string[] InfestationOn =
    {
        "inf \"Undead Infestation\" disabled pillar spawns trigger manual duration 14400s",
        "action: infestation of the Undead",
        "origins: 5, at least 5 natives each, 300 m apart, 10 units each",
        "hunters: 50 %, drift radius 400 m",
        "spread: +1-3 per kill from troops, max 300, 20 per minute",
        "units: 8 from troops",
        "pools: troops (2 units): CHAR_Undead_SkeletonSoldier_Base, CHAR_Undead_ZealousCultist_Ghost",
        "pools: undead-units (library, 20 units): CHAR_Undead_Ghoul",
        "claimed territory allowed",
        "scoreboard: on",
        "not running",
    };

    private static void CastleEntry()
    {
        var all = NyarFieldRules.SettableFields;
        foreach (var (sw, apo, inf, hm, name) in new[]
                 { (true, false, false, false, "spawn waves"), (false, true, false, false, "apocalypse"), (false, false, true, false, "infestation"), (false, false, false, true, "horde march") })
            Check(NyarFieldRules.FieldsFor(all, sw, apo, inf, hm, false).Contains("action.allowTerritory"), $"{name} offers action.allowTerritory");
        Check(!NyarFieldRules.FieldsFor(all, false, false, false, false, true).Contains("action.allowTerritory"), "empowerment doesn't offer it");
        Eq(NyarFieldRules.EventSetText(true, "inf", "action.allowTerritory", "true"), ".nyar api event set inf action.allowTerritory true",
            "the same set command for every type (twin)");
        Eq(NyarFieldRules.EventSetText(false, "inf", "action.allowTerritory", "false"), ".nyar event set inf action.allowTerritory false",
            "the same set command for every type (human)");

        var on = NyarForm.Read(InfestationOn);
        Eq(on.Kind, NyarEventKind.Infestation, "infestation read");
        Eq(V(on, "action.allowTerritory"), "true", "castle entry on read from its line");
        Eq(string.Join(",", on.LibraryPools), "undead-units", "infestation library pools");
        Check(on.Pools.Count == 1, "infestation's own pool only");
        var off = NyarForm.Read(InfestationOn.Where(l => l != "claimed territory allowed").ToArray());
        Eq(V(off, "action.allowTerritory"), "false", "castle entry off when the line is missing");
    }

    // ------------------------------------------------------------------ NyarVersion.AtLeast (fail-closed)
    private static void VersionGate()
    {
        foreach (var (v, want) in new[]
                 {
                     ("", false), ("garbage", false), ("0.15", false), ("0.15.0-beta", false), ("0.14.0", false), ("0.14.9", false),
                     ("0.15.0-rc.1", true), ("0.15.0", true), ("0.15.3", true), ("0.16.0", true), ("1.0.0", true),
                 })
            Eq(NyarVersion.HasHordePools(v), want, $"HasHordePools({v})");
        Eq(NyarVersion.NeedsText("0.14.0"), "needs Nyarlathotep 0.15.0 (server reports 0.14.0)", "needs text");
        Eq(NyarVersion.NeedsText(""), "needs Nyarlathotep 0.15.0 (server reports no version)", "needs text, no version");
    }

    // ------------------------------------------------------------------ reasons in plain words
    private static void ReasonTexts()
    {
        Eq(NyarMessages.ErrorText("state", "condition", "id", 0, "", "0.15.0"), NyarMessages.CastleStartText, "castle start refusal");
        Check(NyarMessages.ErrorText("state", "condition", "", 0, "", "0.15.0").Contains("conditions"), "plain condition refusal");
        Check(NyarMessages.CastleStartText.Contains("Castles may be entered"), "castle text names the switch");

        void Has(string reason, string part)
            => Check(NyarMessages.ReasonText(reason).Contains(part), $"ReasonText({reason}) contains '{part}': {NyarMessages.ReasonText(reason)}");
        Has("action.units.1: takes count or min and max, not both", "count and a min-max");
        Has("action.units.1: min/max need a pool entry", "Only a pool entry");
        Has("action.units.2: min/max apply to counted entries", "weight");
        Has("action.waveList.2.units.1.min must be 1-500", "min must be 1-500");
        Has("action.waveList.2.units.1.min must be 1-500", "(action.waveList.2.units.1)");
        Has("action.units.1.max must be min-500", "at most 500");
        Has("action.units must total 10-500 units for a HordeMarch", "10-500");
        Has("action.allowTerritory must be true or false", "Castles may be entered");
        Has("action.units.1.pool: no pool rabble", "No pool named rabble");
        Has("action.units.1.pool: no pool rabble", "pools.json");
        Has("the start is inside a castle's territory", "Castles may be entered");
        Eq(NyarMessages.ReasonText("something new"), "something new", "unknown reason passes through");
        Eq(NyarMessages.ReasonText(""), "", "empty reason");
        Eq(NyarMessages.ReasonText(null), "", "null reason");
    }
}
