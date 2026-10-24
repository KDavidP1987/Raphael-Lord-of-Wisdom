using System.Text;
using Raphael.Services.Nyar;
using Raphael.UI.ModContent;

// 0.72.0: logic tests for Raphael's pure parts. Exit code 0 = all passed. Each check prints only on failure; a summary
// line ends the run. Server rules quoted here come from Nyarlathotep 4f059f5 (PLAN.md Appendix).
internal static partial class Program
{
    private static int _pass, _fail;

    private static void Check(bool ok, string what)
    {
        if (ok) _pass++;
        else { _fail++; Console.WriteLine("FAIL  " + what); }
    }

    private static void Eq<T>(T actual, T expected, string what)
        => Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{what}: expected <{expected}>, got <{actual}>");

    private static void Ok(string message, string what) => Check(message == null, $"{what}: expected valid, got \"{message}\"");
    private static void Bad(string message, string what) => Check(message != null, $"{what}: expected a refusal, got valid");

    private static int Main()
    {
        Specs();
        UnitLists();
        BossLists();
        PoolsAndNames();
        ListRows();
        Chunking();
        SendOrder();
        Batch();
        Quarantine();
        Rail();
        Version();
        Messages();
        InfoParse();
        Form();
        Templates();
        GuideKitRules();
        GuideChecks();
        ResetTests();
        EmberglassTests();
        KindredAdminTests();
        UndeadHordesTests();
        UrielFeaturesTests();
        NyarFifteenTests();
        NyarHelpTests();
        GameCatalogTests();
        Console.WriteLine($"Raphael.Logic.Tests: {_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ field specs (every range edge)
    private static void Specs()
    {
        void Range(string field, string below, string min, string max, string above, bool apo = false)
        {
            var s = NyarFieldRules.Spec(field, apo);
            Check(s != null, $"spec exists for {field}");
            if (s == null) return;
            Bad(NyarFieldRules.CheckValue(s, below), $"{field} {below}");
            Ok(NyarFieldRules.CheckValue(s, min), $"{field} {min}");
            Ok(NyarFieldRules.CheckValue(s, max), $"{field} {max}");
            Bad(NyarFieldRules.CheckValue(s, above), $"{field} {above}");
        }
        Range("durationSeconds", "29", "30", "7200", "7201");
        Range("durationSeconds", "599", "600", "604800", "604801", apo: true);
        Range("conditions.minPlayers", "-1", "0", "100", "101");
        Range("conditions.cooldownMinutes", "-1", "0", "10080", "10081");
        Range("conditions.chancePercent", "0", "1", "100", "101");
        Range("action.waves", "0", "1", "10", "11");
        Range("action.intervalSeconds", "9", "10", "600", "601");
        Range("action.radius", "1", "2", "30", "31");
        Range("action.pursuit", "14", "15", "600", "601");
        Range("action.modifiers.level", "0", "1", "120", "121");
        Range("action.modifiers.levelDelta", "-6", "-5", "+5", "6");
        Range("action.modifiers.maxHealth", "0.49", "0.5", "3.0", "3.01");
        Range("action.waveList.3.units.2.power", "0.4", "0.50", "3", "3.1");
        Range("action.waveList.2.afterSeconds", "9", "10", "3600", "3601");
        Range("action.stats.physicalPower", "0.99", "1.0", "3.00", "3.01");
        Range("action.goal.radius", "9", "10", "100", "101");
        Range("action.hordes.count", "0", "1", "10", "11");
        Range("action.hordes.everyMinutes", "4", "5", "1440", "1441");
        Range("action.hordes.size", "4", "5", "500", "501");      // 0.73.0: Nyarlathotep 0.14.0 raised both
        Range("action.roamers.max", "0", "1", "500", "501");
        Range("action.marchSpeed", "0.4", "0.5", "5.0", "5.01");
        Range("behaviour.range", "9", "10", "60", "61");
        Range("location.near", "9", "10", "60", "61");
        Range("location.far", "14", "15", "80", "81");

        var pursuit = NyarFieldRules.Spec("action.pursuit");
        Ok(NyarFieldRules.CheckValue(pursuit, "none"), "pursuit none");
        Ok(NyarFieldRules.CheckValue(pursuit, "015"), "pursuit 015 (leading zero, as NumberStyles.None)");
        Bad(NyarFieldRules.CheckValue(pursuit, "-5"), "pursuit -5");
        Bad(NyarFieldRules.CheckValue(pursuit, "1 5"), "pursuit '1 5'");
        Bad(NyarFieldRules.CheckValue(pursuit, ""), "pursuit empty");
        Bad(NyarFieldRules.CheckValue(NyarFieldRules.Spec("action.waves"), "none"), "waves takes no none");
        var hp = NyarFieldRules.Spec("action.modifiers.maxHealth");
        Bad(NyarFieldRules.CheckValue(hp, "1.234"), "hp 1.234 (three decimals)");
        Bad(NyarFieldRules.CheckValue(hp, ".5"), "hp .5");
        Bad(NyarFieldRules.CheckValue(hp, "1."), "hp 1.");
        Ok(NyarFieldRules.CheckValue(hp, "none"), "hp none");
        var stat = NyarFieldRules.Spec("action.stats.maxHealth");
        Bad(NyarFieldRules.CheckValue(stat, "1.500"), "stat 1.500 (Stat: 1-2 digits after the dot)");
        Ok(NyarFieldRules.CheckValue(stat, "2"), "stat 2");
        var ld = NyarFieldRules.Spec("action.modifiers.levelDelta");
        Ok(NyarFieldRules.CheckValue(ld, "3"), "levelDelta 3");
        Bad(NyarFieldRules.CheckValue(ld, "+10"), "levelDelta +10");
        var name = NyarFieldRules.Spec("name");
        Ok(NyarFieldRules.CheckValue(name, new string('a', 40)), "name 40 chars");
        Bad(NyarFieldRules.CheckValue(name, new string('a', 41)), "name 41 chars");
        Bad(NyarFieldRules.CheckValue(name, "a<b"), "name with <");
        Bad(NyarFieldRules.CheckValue(NyarFieldRules.Spec("action.goal.name"), new string('g', 33)), "goal name 33");
        Eq(NyarFieldRules.Spec("action.units"), null, "lists have no number spec");

        // steppers: clamp, snap to step, start sensibly from empty
        var waves = NyarFieldRules.Spec("action.waves");
        Eq(NyarFieldRules.Step(waves, "10", +1), "10", "waves + at max stays 10");
        Eq(NyarFieldRules.Step(waves, "1", -1), "1", "waves − at min stays 1");
        Eq(NyarFieldRules.Step(waves, "", +1), "1", "waves + from empty → 1");
        var interval = NyarFieldRules.Spec("action.intervalSeconds");
        Eq(NyarFieldRules.Step(interval, "33", +1), "35", "interval 33 + → 35 (snap)");
        Eq(NyarFieldRules.Step(interval, "33", -1), "30", "interval 33 − → 30 (snap)");
        Eq(NyarFieldRules.Step(hp, "", +1), "1.1", "hp + from empty starts at ×1");
        Eq(NyarFieldRules.Step(hp, "2.95", +1), "3.0", "hp clamps at 3.0");
        Eq(NyarFieldRules.Step(ld, "0", +1), "+1", "levelDelta shows a sign");
        Eq(NyarFieldRules.Step(ld, "-5", -1), "-5", "levelDelta clamps at -5");
        Ok(NyarFieldRules.CheckValue(ld, NyarFieldRules.Step(ld, "4", +1)), "stepper output is always valid (levelDelta)");
        Ok(NyarFieldRules.CheckValue(hp, NyarFieldRules.Step(hp, "1.0", +1)), "stepper output is always valid (hp)");
        Eq(NyarFieldRules.Echo(NyarFieldRules.Spec("durationSeconds"), "900"), "= 15 min", "duration echo");
        Eq(NyarFieldRules.Echo(NyarFieldRules.Spec("conditions.cooldownMinutes"), "1440"), "= 1 day", "cooldown echo");
        Eq(NyarFieldRules.Echo(NyarFieldRules.Spec("action.radius"), "20"), "", "no echo for metres");
        Eq(NyarFieldRules.Spec("action.intervalSeconds").RangeText, "10-600 s", "range text");
        Eq(NyarFieldRules.Spec("action.modifiers.levelDelta").RangeText, "-5 to +5", "signed range text");
        Eq(NyarFieldRules.Generic("action.waveList.3.units.12.level"), "action.waveList.N.units.M.level", "generic field");
    }

    // ------------------------------------------------------------------ units lists
    private static void UnitLists()
    {
        Ok(NyarFieldRules.CheckUnitList("CHAR_Bandit_Thug", false), "count optional (default 1)");
        Ok(NyarFieldRules.CheckUnitList("CHAR_Bandit_Thug:499,pool:troops:1", false), "count 499 and pool entry (500 in all)");
        Ok(NyarFieldRules.CheckUnitList("CHAR_Bandit_Thug:500", false), "count 500 (0.73.0)");
        Bad(NyarFieldRules.CheckUnitList("CHAR_Bandit_Thug:0", false), "count 0");
        Bad(NyarFieldRules.CheckUnitList("CHAR_Bandit_Thug:501", false), "count 501");
        Bad(NyarFieldRules.CheckUnitList("CHAR_A:1,CHAR_A:2", false), "action.units refuses a repeat");
        Ok(NyarFieldRules.CheckUnitList("CHAR_A:1,CHAR_A:2", true), "a wave list allows a repeat");
        Bad(NyarFieldRules.CheckUnitList("pool:troops:2,pool:troops:3", false), "action.units refuses a repeated pool");
        string ten = string.Join(",", Enumerable.Range(1, 10).Select(i => $"CHAR_U{i}:1"));
        Ok(NyarFieldRules.CheckUnitList(ten, false), "10 entries");
        Bad(NyarFieldRules.CheckUnitList(ten + ",CHAR_U11:1", false), "11 entries");
        Ok(NyarFieldRules.CheckUnitList("pool:troops:250,CHAR_A:250", true), "pool list with 500 units");
        Bad(NyarFieldRules.CheckUnitList("pool:troops:251,CHAR_A:250", true), "pool list with 501 units");
        Ok(NyarFieldRules.CheckUnitList("CHAR_A:500,CHAR_B:500", false), "no pool → no 500-unit total");
        Bad(NyarFieldRules.CheckUnitList("pool:Troops:2", false), "upper-case pool name");
        Bad(NyarFieldRules.CheckUnitList("pool:troops", false), "pool token without count");
        Bad(NyarFieldRules.CheckUnitList("pool:troops:2:3", false), "pool token with 4 parts");
        Bad(NyarFieldRules.CheckUnitList("CHAR_A:1,,CHAR_B:1", false), "empty part");
        Bad(NyarFieldRules.CheckUnitList("CHAR_A:1,", false), "trailing comma");
        Bad(NyarFieldRules.CheckUnitList("CHAR_A:1, CHAR_B:1", false), "space");
        Bad(NyarFieldRules.CheckUnitList("CHAR_A:'1'", false), "quote");
        Bad(NyarFieldRules.CheckUnitList("Bandit_Thug:1", false), "no CHAR_ prefix");
        Bad(NyarFieldRules.CheckUnitList("CHAR_" + new string('x', 92), false), "unit name 97 chars");
        Ok(NyarFieldRules.CheckUnitList("CHAR_" + new string('x', 91), false), "unit name 96 chars");
        Bad(NyarFieldRules.CheckUnitList("CHAR_A-B", false), "bad char in name");
        Bad(NyarFieldRules.CheckUnitList("", false), "empty list");
    }

    // ------------------------------------------------------------------ bosses lists
    private static void BossLists()
    {
        Ok(NyarFieldRules.CheckBossList("none", false), "bosses none");
        Ok(NyarFieldRules.CheckBossList("CHAR_V:3,CHAR_W:3,CHAR_X:3", false), "all-waves fixed 3×3 = 9 (no total rule there)");
        Bad(NyarFieldRules.CheckBossList("CHAR_V:3,CHAR_W:3", true), "wave list 6 bosses");
        Ok(NyarFieldRules.CheckBossList("CHAR_V:3,CHAR_W:2", true), "wave list 5 bosses");
        Bad(NyarFieldRules.CheckBossList("CHAR_V,CHAR_W,CHAR_X,CHAR_Y", false), "4 entries");
        Bad(NyarFieldRules.CheckBossList("CHAR_V:4", false), "boss count 4");
        Bad(NyarFieldRules.CheckBossList("CHAR_V:0", false), "boss count 0");
        Ok(NyarFieldRules.CheckBossList("pool:elite:1-5", false), "roll 1-5");
        Bad(NyarFieldRules.CheckBossList("pool:elite:0-1", false), "roll 0-1");
        Bad(NyarFieldRules.CheckBossList("pool:elite:3-2", false), "roll 3-2");
        Bad(NyarFieldRules.CheckBossList("pool:elite:1-6", false), "roll 1-6");
        Bad(NyarFieldRules.CheckBossList("pool:elite:3", false), "roll without range");
        Bad(NyarFieldRules.CheckBossList("pool:elite:a-b", false), "roll bad numbers");
        Ok(NyarFieldRules.CheckBossList("CHAR_V:2,pool:elite:1-3", false), "fixed + roll = 5");
        Bad(NyarFieldRules.CheckBossList("CHAR_V:3,pool:elite:1-3", false), "fixed + roll = 6");
        var rows = NyarFieldRules.ParseEntries("CHAR_V:2,pool:elite:1-3", bosses: true);
        Eq(NyarFieldRules.MostBosses(rows), 5, "most bosses");
        Check(NyarFieldRules.BossTotalApplies(rows, false), "a roll makes the total apply");
        Check(!NyarFieldRules.BossTotalApplies(NyarFieldRules.ParseEntries("CHAR_V:3", true), false), "fixed-only all-waves list: no total");
        // the server's check order: shape before pool name, min before max
        Check((NyarFieldRules.CheckBossList("pool:BAD:9", false) ?? "").Contains("should look like"), "shape is checked before the pool name");
        Check((NyarFieldRules.CheckBossList("pool:elite:0-9", false) ?? "").Contains("fewest"), "min is checked before max");
    }

    // ------------------------------------------------------------------ pools, factions
    private static void PoolsAndNames()
    {
        Check(NyarFieldRules.IsPoolName(new string('a', 24)), "pool name 24");
        Check(!NyarFieldRules.IsPoolName(new string('a', 25)), "pool name 25");
        Check(!NyarFieldRules.IsPoolName("Troops"), "pool name upper case");
        Check(!NyarFieldRules.IsPoolName("my_pool"), "pool name underscore");
        Eq(NyarFieldRules.NormalizePoolName("  Elite Guards_2 "), "elite-guards-2", "normalise pool name");
        Eq(NyarFieldRules.NormalizePoolName("Ünïcode!!"), "ncode", "normalise drops other characters");
        Eq(NyarFieldRules.NormalizePoolName(new string('b', 30)).Length, 24, "normalise cuts to 24");
        var thirty = Enumerable.Range(1, 30).Select(i => $"CHAR_U{i}").ToList();
        Ok(NyarFieldRules.CheckPoolMembers(thirty), "30 members");
        Bad(NyarFieldRules.CheckPoolMembers(thirty.Append("CHAR_U31").ToList()), "31 members");
        Bad(NyarFieldRules.CheckPoolMembers(new[] { "CHAR_A", "CHAR_A" }), "duplicate member");
        Bad(NyarFieldRules.CheckPoolMembers(Array.Empty<string>()), "no members");
        Ok(NyarFieldRules.CheckFactions("Faction_Bandits,Faction_Undead"), "factions");
        Bad(NyarFieldRules.CheckFactions("Faction_Bandits,Faction_Bandits"), "repeated faction");
        Bad(NyarFieldRules.CheckFactions("Bandits"), "faction without prefix");
        Bad(NyarFieldRules.CheckFactions("Faction_A,Faction_B,Faction_C,Faction_D,Faction_E,Faction_F"), "6 factions");
    }

    // ------------------------------------------------------------------ list rows ⇄ text
    private static void ListRows()
    {
        var rows = NyarFieldRules.ParseEntries("CHAR_A:4,pool:troops:2,CHAR_B", bosses: false);
        Eq(rows.Count, 3, "3 rows");
        Eq(rows[2].Count, 1, "missing count reads as 1");
        rows[0].Count = 7;
        rows.RemoveAt(1);
        Eq(NyarFieldRules.BuildEntries(rows, false), "CHAR_A:7,CHAR_B:1", "edit count + remove row → rebuilt text");
        NyarFieldRules.AddUnit(rows, "CHAR_A", 45, 50, mergeRepeats: true);
        Eq(rows[0].Count, 50, "a repeat on action.units raises the count, capped at 50");
        NyarFieldRules.AddUnit(rows, "CHAR_A", 2, 50, mergeRepeats: false);
        Eq(rows.Count, 3, "a wave list keeps a repeat as its own row");
        NyarFieldRules.AddPool(rows, "elite", 3, 0, 0, bosses: false, mergeRepeats: true);
        Eq(NyarFieldRules.BuildEntries(rows, false), "CHAR_A:50,CHAR_B:1,CHAR_A:2,pool:elite:3", "pool entry appended");
        var b = NyarFieldRules.ParseEntries("CHAR_V:1,pool:elite:1-2", bosses: true);
        Eq(NyarFieldRules.BuildEntries(b, true), "CHAR_V:1,pool:elite:1-2", "bosses round trip");
        Eq(NyarFieldRules.ParseEntries("none", true).Count, 0, "none = no rows");
        Eq(NyarFieldRules.ParseEntries("CHAR_A:x", false), null, "unreadable text → null (kept as raw text)");
        Eq(NyarFieldRules.ParseEntries("CHAR_A,,CHAR_B", false), null, "empty part → null");
        foreach (var v in new[] { "CHAR_A:3,pool:p:4", "CHAR_X:1" })
            Eq(NyarFieldRules.BuildEntries(NyarFieldRules.ParseEntries(v, false), false), v, $"round trip {v}");
    }

    // ------------------------------------------------------------------ pool save chunking
    private static void Chunking()
    {
        string id = new string('e', 32);
        // 30 long catalogue-like names (~60-70 chars each)
        var members = Enumerable.Range(1, 30).Select(i => $"CHAR_Gloomrot_Technician_Very_Long_Prefab_Name_Variant_{i:00}_Elite").ToList();
        Func<string, string> cmd = v => $".nyar api event set {id} action.pools.troops-elite {v}";
        var steps = NyarFieldRules.PoolSaveSteps(members, cmd);
        Check(steps.Count > 1, "long pool needs several commands");
        Check(steps.All(s => Encoding.UTF8.GetByteCount(cmd(s.Value)) <= NyarFieldRules.MaxCommandBytes), "every command ≤ 450 bytes");
        Check(!steps[0].Value.StartsWith("+") && steps.Skip(1).All(s => s.Value.StartsWith("+") && s.Covers.Length == 1), "first chunk, then +name");
        Eq(string.Join(",", steps.SelectMany(s => s.Covers)), string.Join(",", members), "union equals members, in order");
        Eq(string.Join(",", steps[0].Covers), steps[0].Value, "first chunk covers its names");
        var small = new[] { "CHAR_A", "CHAR_B", "CHAR_C" };
        var one = NyarFieldRules.PoolSaveSteps(small, cmd);
        Eq(one.Count, 1, "small pool → one atomic set");
        Eq(one[0].Value, "CHAR_A,CHAR_B,CHAR_C", "small pool value");
    }

    // ------------------------------------------------------------------ send order
    private static void SendOrder()
    {
        var kv = (string f, string v) => new KeyValuePair<string, string>(f, v);
        var order = NyarFieldRules.OrderForSave(new[]
        {
            kv("action.pursuit", "60"), kv("action.waveList.2.bosses", "CHAR_V:1"), kv("action.waveList.2.units", "CHAR_A:3"),
            kv("action.bosses", "none"), kv("action.modifiers.level", "40"), kv("action.modifiers.levelDelta", "none"),
            kv("name", "Raid"), kv("action.waveList.1.units", "CHAR_B:2"), kv("action.pools.troops", "CHAR_A,CHAR_B"),
        }).Select(x => x.Key).ToList();
        Check(order.IndexOf("name") == 0, "definition fields first");
        Check(order.IndexOf("action.modifiers.levelDelta") < order.IndexOf("action.modifiers.level"), "the other level field's none goes first");
        Check(order.IndexOf("action.pools.troops") < order.IndexOf("action.waveList.1.units"), "pools before the entries naming them");
        Check(order.IndexOf("action.waveList.1.units") < order.IndexOf("action.waveList.2.units"), "waves ascending");
        Check(order.IndexOf("action.waveList.2.units") < order.IndexOf("action.waveList.2.bosses"), "a wave's units before its bosses");
        Check(order.IndexOf("action.bosses") > order.IndexOf("action.waveList.2.bosses"), "event bosses after the waves");
        Eq(order.Last(), "action.pursuit", "pursuit last");

        var cleared = NyarFieldRules.StartRuleSets(3, "cleared", 0);
        Eq($"{cleared[0].Key}={cleared[0].Value};{cleared[1].Key}={cleared[1].Value}",
           "action.waveList.3.whenCleared=true;action.waveList.3.afterSeconds=none", "when cleared: whenCleared true first");
        var after = NyarFieldRules.StartRuleSets(2, "after", 45);
        Eq($"{after[0].Value};{after[1].Value}", "45;false", "after: seconds first, then whenCleared false");
        var both = NyarFieldRules.StartRuleSets(2, "afterOrCleared", 30);
        Eq($"{both[0].Value};{both[1].Value}", "30;true", "after or cleared");
    }

    // ------------------------------------------------------------------ batch
    private static NyarBatch MakeBatch(params string[] fields)
    {
        var steps = fields.Select((f, i) => new NyarBatchStep { Field = f, Value = "v" + i, Command = $".nyar api event set ev1 {f} v{i}", Label = f }).ToList();
        return new NyarBatch("ev1", "changes", false, steps);
    }

    private static NyarBatch MakePool()
    {
        var steps = new List<NyarBatchStep>
        {
            new() { Field = "action.pools.troops", Value = "CHAR_A,CHAR_B,CHAR_C", Command = "c0", Covers = new[] { "CHAR_A", "CHAR_B", "CHAR_C" }, Label = "the first 3 units" },
            new() { Field = "action.pools.troops", Value = "+CHAR_D", Command = "c1", Covers = new[] { "CHAR_D" }, Label = "CHAR_D" },
            new() { Field = "action.pools.troops", Value = "+CHAR_E", Command = "c2", Covers = new[] { "CHAR_E" }, Label = "CHAR_E" },
        };
        return new NyarBatch("ev1", "troops", true, steps);
    }

    private static void Batch()
    {
        // a pool delete reports a delete
        var del = new NyarBatch("ev1", "troops", true, new List<NyarBatchStep>
            { new() { Field = "action.pools.troops", Value = "none", Command = "d0", Label = "delete pool troops" } });
        Check(del.IsPoolDelete && !MakePool().IsPoolDelete, "pool delete detected");
        del.Tick(0); del.MarkOnWire(0); del.OnOk("event", "set", "ev1", "action.pools.troops");
        Eq(del.Summary(), "Pool troops deleted.", "pool delete summary");

        // all ok, paced
        var b = MakeBatch("a", "b", "c");
        var sends = new List<double>();
        double t = 0;
        for (int guard = 0; guard < 1000 && !b.Finished; guard++, t += 0.05)
        {
            var c = b.Tick(t);
            if (c != null) { sends.Add(t); b.MarkOnWire(t); }
            if (b.Outstanding && b.OnWire && t - sends.Last() >= 0.1) b.OnOk("event", "set", "EV1", b.Current.Field);
        }
        Eq(b.State, NyarBatchState.Done, "all ok → done");
        Eq(sends.Count, 3, "three sends");
        Check(sends.Zip(sends.Skip(1), (x, y) => y - x).All(d => d >= NyarBatch.Gap - 1e-9), "pacing never < 0.40 s");
        Eq(b.Summary(), "Saved 3 changes.", "done summary");

        // ok matching
        b = MakeBatch("a", "b");
        b.Tick(0); b.MarkOnWire(0);
        Check(!b.OnOk("event", "set", "ev1", "b"), "ok with the wrong field ignored");
        Check(!b.OnOk("event", "set", "ev2", "a"), "ok with the wrong id ignored");
        Check(!b.OnOk("pillar", "set", "ev1", "a"), "ok for another command ignored");
        Check(b.OnOk("event", "set", "ev1", "a"), "matching ok accepted");
        Check(!b.OnOk("event", "set", "ev1", "a"), "duplicate ok ignored");

        // err matching
        b = MakeBatch("a", "b", "c");
        b.Tick(0); b.MarkOnWire(0); b.OnOk("event", "set", "ev1", "a");
        Check(!b.OnErr("event", "set", "invalid", 0, "x", b.Steps[1].Command, 0.1), "err while no step outstanding ignored");
        b.Tick(0.5); b.MarkOnWire(0.5);
        Check(!b.OnErr("event", "set", "invalid", 0, "x", ".nyar api pillar spawns on", 0.6), "err for another in-flight command ignored");
        Check(!b.OnErr("pillar", "-", "invalid", 0, "x", b.Steps[1].Command, 0.6), "err for another cmd ignored");
        Check(b.OnErr("event", "set", "invalid", 0, "b is not valid.", b.Steps[1].Command, 0.6), "matching err accepted");
        Eq(b.State, NyarBatchState.Stopped, "err mid-way → stopped");
        Eq(b.Tick(5), null, "nothing sent after a stop (rest dropped)");
        Check(b.Summary().StartsWith("Saved 1 of 3 changes; b was refused"), "stop summary names the step: " + b.Summary());

        // ratelimit: one retry
        b = MakeBatch("a");
        b.Tick(0); b.MarkOnWire(0);
        b.OnErr("event", "set", "ratelimit", 2, "Too fast", b.Steps[0].Command, 0.2);
        Eq(b.State, NyarBatchState.Running, "ratelimit → still running");
        Eq(b.Tick(1.0), null, "waits secs before the retry");
        Eq(b.Tick(2.3), b.Steps[0].Command, "retries the same step");
        b.MarkOnWire(2.3);
        b.OnErr("event", "set", "ratelimit", 2, "Too fast", b.Steps[0].Command, 2.5);
        Eq(b.State, NyarBatchState.Stopped, "a second ratelimit stops");

        // timeout → unknown; late answers ignored
        b = MakeBatch("a", "b");
        b.Tick(0); b.MarkOnWire(1.0);
        b.Tick(6.99);
        Eq(b.State, NyarBatchState.Running, "no timeout before 6 s on the wire");
        b.Tick(7.0);
        Eq(b.State, NyarBatchState.Unknown, "6 s without an answer → unknown");
        Check(b.AbandonedInFlight, "timeout abandons the in-flight step");
        Check(!b.OnOk("event", "set", "ev1", "a"), "late ok after timeout ignored");
        Check(!b.OnErr("event", "set", "invalid", 0, "x", b.Steps[0].Command, 8), "late err after timeout ignored");
        Check(b.Summary().Contains("Reload values"), "unknown summary asks for Reload values");

        // queued but never sent (quarantine held it)
        b = MakeBatch("a");
        b.Tick(0);
        b.Tick(20.0);
        Eq(b.State, NyarBatchState.Stopped, "never sent within 20 s → stopped");
        Check(!b.AbandonedInFlight, "…without abandoning anything on the wire");

        // cancel
        b = MakeBatch("a", "b");
        b.Tick(0); b.MarkOnWire(0);
        Check(b.Cancel(), "cancel with a step in flight → abandoned");
        Check(b.Summary().Contains("may or may not"), "cancel summary says may or may not");
        b = MakeBatch("a", "b");
        b.Tick(0);
        Check(!b.Cancel(), "cancel before the step went out → nothing abandoned");

        // pool coverage: first chunk / middle / last step refused
        var p = MakePool();
        p.Tick(0); p.MarkOnWire(0);
        p.OnErr("event", "set", "invalid", 0, "CHAR_A is not a unit.", "c0", 0.1);
        Check(p.Summary().StartsWith("Pool troops was not saved"), "first chunk refused: " + p.Summary());
        p = MakePool();
        p.Tick(0); p.MarkOnWire(0); p.OnOk("event", "set", "ev1", "action.pools.troops");
        p.Tick(0.5); p.MarkOnWire(0.5);
        p.OnErr("event", "set", "invalid", 0, "CHAR_D is not a unit.", "c1", 0.6);
        Check(p.Summary().Contains("after 3 of 5 units: CHAR_D was refused"), "middle refused: " + p.Summary());
        p = MakePool();
        double tp = 0;
        for (int i = 0; i < 2; i++) { p.Tick(tp); p.MarkOnWire(tp); p.OnOk("event", "set", "ev1", "action.pools.troops"); tp += 0.5; }
        p.Tick(tp); p.MarkOnWire(tp);
        p.OnErr("event", "set", "invalid", 0, "no.", "c2", tp + 0.1);
        Check(p.Summary().Contains("after 4 of 5 units: CHAR_E"), "last refused: " + p.Summary());
        p = MakePool();
        for (int i = 0; i < 3; i++) { p.Tick(i); p.MarkOnWire(i); p.OnOk("event", "set", "ev1", "action.pools.troops"); }
        Eq(p.Summary(), "Pool troops saved (5 units).", "pool done summary");

        // stale replies: after batch 1 times out, a new batch 2's step on the same field is not advanced by batch 1's
        // late ok while it isn't on the wire yet; once on the wire its own ok advances it.
        var b1 = MakeBatch("a");
        b1.Tick(0); b1.MarkOnWire(0); b1.Tick(6);
        var b2 = MakeBatch("a");
        b2.Tick(16.1);
        Check(!b2.OnOk("event", "set", "ev1", "a"), "stale ok before the new step is on the wire ignored");
        b2.MarkOnWire(16.2);
        Check(b2.OnOk("event", "set", "ev1", "a"), "the new batch's own answer advances it");
    }

    private static void Quarantine()
    {
        var q = new NyarQuarantine();
        Check(!q.On(0), "off at first");
        q.Start(7.0);
        Check(q.On(16.9), "+9.9 s: still on (twins held, Save refused)");
        Check(!q.On(17.1), "+10.1 s: off (Save allowed)");
        Eq(q.SecondsLeft(8.2), 9, "seconds left rounds up");
        q.Start(8.0);
        Check(q.On(17.5), "a later abandonment extends it");
        q.Start(1.0);
        Check(q.On(17.5), "an earlier start never shortens it");
        q.Clear();
        Check(!q.On(9), "clear (disconnect)");
    }

    // ------------------------------------------------------------------ rail
    private sealed class RailSim
    {
        public readonly List<RailGroup> Groups = new();
        public bool Accordion = true, UserTookControl;
        public string Active = "Bloodcraft";

        public RailSim(params (string title, bool available, bool diagnostic)[] spec)
        {
            foreach (var (title, available, diagnostic) in spec)
                Groups.Add(new RailGroup { Title = title, IsMod = RailPolicy.IsModGroup(title), Available = available, Diagnostic = diagnostic });
        }

        public RailGroup G(string t) => Groups.First(g => g.Title == t);
        public void Apply(RailChanges ch)
        {
            if (ch.UserTookControl) UserTookControl = true;
            foreach (var kv in ch.Intent) G(kv.Key).Intent = kv.Value;
            foreach (var kv in ch.Expand) if (!kv.Value || G(kv.Key).Usable) G(kv.Key).Expanded = kv.Value;
        }
        /// <summary>BuildTabGroup: from memory, else StartExpanded (only Bloodcraft), only when usable.</summary>
        public void Build(bool firstTime)
        {
            foreach (var g in Groups)
                g.Expanded = (firstTime ? g.Title == "Bloodcraft" : g.Expanded) && g.Usable;
        }
        public void Reconcile() => Apply(RailPolicy.Reconcile(Groups, Active, Accordion, UserTookControl));
        public void Header(string t) => Apply(RailPolicy.HeaderClick(Groups, t, Accordion));
        public string Open => string.Join(",", Groups.Where(g => g.Expanded).Select(g => g.Title));
    }

    private static RailSim Server(bool bc, bool bz, bool ur, bool fa, bool ny, bool gaveUp = true)
        => new(("Bloodcraft", bc, !bc && gaveUp), ("Kindred", true, false), ("Beelzebub", bz, !bz && gaveUp),
               ("Uriel", ur, !ur && gaveUp), ("Faust", fa, !fa && gaveUp), ("Nyarlathotep", ny, !ny && gaveUp),
               ("Game UI", true, false), ("Settings and Help", true, false));

    private static void Rail()
    {
        // Nyarlathotep-only server: only Nyarlathotep opens; the "not detected" groups stay closed.
        var s = Server(false, false, false, false, true);
        s.Build(firstTime: true); s.Reconcile();
        Eq(s.Open, "Nyarlathotep", "Nyar-only server → only Nyarlathotep open");
        s.Reconcile(); s.Reconcile();
        Eq(s.Open, "Nyarlathotep", "repeated state changes open nothing else");

        // Nothing detected → the Bloodcraft diagnostic opens; Nyar detected later → it moves there.
        s = Server(false, false, false, false, false);
        s.Build(true); s.Reconcile();
        Eq(s.Open, "Bloodcraft", "nothing detected → Bloodcraft diagnostic only");
        s.G("Nyarlathotep").Available = true; s.G("Nyarlathotep").Diagnostic = false;
        s.Reconcile();
        Eq(s.Open, "Nyarlathotep", "Nyar detected → Bloodcraft closes, Nyarlathotep opens");

        // User opened Uriel, then a rebuild (text-size change): Uriel stays, nothing else opens.
        s = Server(true, false, true, false, true);
        s.Build(true); s.Reconcile();
        Eq(s.Open, "Bloodcraft", "full server → Bloodcraft (first available)");
        s.Header("Uriel");
        Eq(s.Open, "Uriel", "accordion: opening Uriel closes Bloodcraft");
        s.Build(false); s.Reconcile();
        Eq(s.Open, "Uriel", "rebuild keeps Uriel, nothing else opens");

        // Late Bloodcraft ACK while the user works in Nyarlathotep: nothing changes.
        s = Server(false, false, false, false, true, gaveUp: false);
        s.Build(true); s.Reconcile();
        s.Header("Nyarlathotep"); s.Header("Nyarlathotep");   // close, reopen: user in control
        s.G("Bloodcraft").Available = true;
        s.Reconcile();
        Eq(s.Open, "Nyarlathotep", "late Bloodcraft ACK opens nothing next to the user's group");

        // The user closed the primary: it is never reopened by itself.
        s = Server(false, false, false, false, true);
        s.Build(true); s.Reconcile();
        s.Header("Nyarlathotep");
        s.Reconcile(); s.Build(false); s.Reconcile();
        Eq(s.Open, "", "a closed primary stays closed");

        // First-run Quick Start opens Settings and Help: no mod group opens next to it.
        s = Server(false, false, false, false, true);
        s.Build(true);
        s.G("Settings and Help").Expanded = true; s.G("Settings and Help").Intent = RailIntent.Open;
        s.Reconcile();
        Eq(s.Open, "Settings and Help", "first-run: only Settings and Help");

        // The active tab's group wins over rail order.
        s = Server(true, true, false, false, false);
        s.Active = "Beelzebub";
        s.Build(true); s.Reconcile();
        Eq(s.Open, "Beelzebub", "active tab's group wins");

        // Accordion off: same rules, and the user may keep several open.
        s = Server(true, false, true, false, false);
        s.Accordion = false;
        s.Build(true); s.Reconcile();
        s.Header("Uriel"); s.Header("Settings and Help");
        s.Reconcile();
        Eq(s.Open, "Bloodcraft,Uriel,Settings and Help", "accordion off keeps what the user opened (and the earlier auto-open)");

        // An Off group (dead, no diagnostic) is closed even if memory said open.
        s = Server(true, false, false, false, false);
        s.G("Faust").Diagnostic = false; s.G("Faust").Expanded = true; s.G("Faust").Intent = RailIntent.Open;
        s.Reconcile();
        Check(!s.G("Faust").Expanded, "Off group closed");
        Eq(s.G("Faust").Intent, RailIntent.Open, "…its intent is kept");

        // A user-opened group goes dead → rebuild → usable again → reopened.
        s = Server(false, false, true, false, false);
        s.Build(true); s.Reconcile();
        s.Header("Uriel"); s.Header("Uriel");   // auto-opened → close → reopen
        Eq(s.Open, "Uriel", "user opened Uriel");
        s.G("Uriel").Available = false; s.G("Uriel").Diagnostic = false;
        s.Reconcile();
        Eq(s.Open, "", "Uriel dead → closed");
        s.Build(false); s.Reconcile();
        s.G("Uriel").Available = true;
        s.Reconcile();
        Eq(s.Open, "Uriel", "Uriel usable again → reopened");

        // …but with the accordion on and another user-opened group open, it stays closed (Intent=Closed).
        s.G("Uriel").Available = false;
        s.Reconcile();
        s.Header("Settings and Help");
        s.G("Uriel").Available = true;
        s.Reconcile();
        Eq(s.Open, "Settings and Help", "usable again but another user group is open → stays closed");
        Eq(s.G("Uriel").Intent, RailIntent.Closed, "…and becomes Intent=Closed");

        // A tab click counts as the user's choice.
        var tab = RailPolicy.TabClick("Nyarlathotep");
        Check(tab.UserTookControl && tab.Intent["Nyarlathotep"] == RailIntent.Open, "tab click → Intent=Open + user in control");
    }

    private static void Version()
    {
        Check(NyarVersion.HasAdminLocationBug("0.13.0-rc.1"), "rc.1 has the bug");
        Check(!NyarVersion.HasAdminLocationBug("0.13.0-rc.2"), "rc.2 fixed");
        Check(!NyarVersion.HasAdminLocationBug("0.13.0-rc.3"), "rc.3 fixed");
        Check(!NyarVersion.HasAdminLocationBug("0.13.0"), "0.13.0 release");
        Check(!NyarVersion.HasAdminLocationBug("0.12.5"), "0.12.5 (no apocalypse)");
        Check(!NyarVersion.HasAdminLocationBug("0.14.0"), "0.14.0");
        Check(!NyarVersion.HasAdminLocationBug("garbage"), "garbage");
        Check(!NyarVersion.HasAdminLocationBug(""), "empty");
        Check(!NyarVersion.HasAdminLocationBug("0.13.0-beta.1"), "unknown prerelease");
        var v = NyarVersion.Parse("0.13.0-rc.3");
        Check(v.Valid && v.Minor == 13 && v.Rc == 3, "parse rc.3");
    }

    private static void Messages()
    {
        string Pil(string pillar) => NyarMessages.ErrorText("disabled", "pillar_off", "", 0, pillar, "0.13.0-rc.3");
        Check(Pil("spawns").StartsWith("The Spawns pillar"), "pillar_off names a known pillar");
        Check(Pil("").StartsWith("This event's pillar"), "pillar_off without the key → today's text");
        Check(Pil("bogus").StartsWith("This event's pillar"), "unknown pillar → today's text");
        Check(Pil("SPAWNS").StartsWith("The Spawns pillar"), "pillar id case-insensitive");
        Check(NyarMessages.ErrorText("badarg", "admin_location", "", 0, "", "0.13.0-rc.1").Contains("fixed in 0.13.0-rc.2"), "rc.1 wording");
        Check(!NyarMessages.ErrorText("badarg", "admin_location", "", 0, "", "0.13.0-rc.3").Contains("bug"), "rc.3 generic wording");
        Eq(NyarMessages.FormatSecs(75), "1m 15s", "format secs");
    }

    // ------------------------------------------------------------------ event info fixtures (server formats)
    private static void InfoParse()
    {
        var unitsForm = new[]
        {
            "raid \"Bandit Raid\" disabled pillar spawns trigger manual duration 600s",
            "conditions: minPlayers 1, cooldown 30 min, chance 100%, window none, mode any",
            "action: 3 waves every 60s, radius 10, around a player 20-40 m, units 4 CHAR_Bandit_Thug, 2 from troops, unit lifetime 600s",
            "spawn: modifiers level 40, maxHealth x1.5; loot on; hunt 30 m; claimed territory allowed",
            "final wave adds: CHAR_Bandit_Chaosarrow_VBlood x1, 1-3 bosses from elite",
            "pools: troops (13 units): CHAR_A1, CHAR_A2, CHAR_A3, CHAR_A4, CHAR_A5, CHAR_A6, CHAR_A7",
            "pools: troops (13 units): CHAR_A8, CHAR_A9, CHAR_A10, CHAR_A11, CHAR_A12, CHAR_A13",
            "pools: elite (2 units): CHAR_E1, CHAR_E2",
            "pursuit: after 90 s with no player within 40 m",
            "trigger scope: Global",
        };
        Check(!NyarInfoParse.IsWaveList(unitsForm), "units form detected");
        Eq(NyarInfoParse.UnitsFormUnits(unitsForm), "CHAR_Bandit_Thug:4,pool:troops:2", "units form units incl. pool entry");
        Eq(NyarInfoParse.FinalBosses(unitsForm), "CHAR_Bandit_Chaosarrow_VBlood:1,pool:elite:1-3", "final bosses fixed + roll");
        var pools = NyarInfoParse.Pools(unitsForm, out var complete);
        Eq(pools.Count, 2, "two pools");
        Eq(pools[0].Key + ":" + pools[0].Value.Count, "troops:13", "wrapped pool read whole");
        Check(complete, "pools complete");
        Eq(NyarInfoParse.Pursuit(unitsForm), 90, "pursuit");
        Eq(NyarInfoParse.Location(unitsForm, out var near, out var far) + $" {near}-{far}", "aroundplayer 20-40", "location around a player");
        Eq(NyarInfoParse.Behaviour(unitsForm), "hunt 30", "behaviour hunt");
        var mods = NyarInfoParse.Modifiers(unitsForm);
        Eq(mods.TryGetValue("level", out var lv) ? lv : "", "40", "modifier level");
        Eq(mods.TryGetValue("maxHealth", out var mh) ? mh : "", "1.5", "modifier maxHealth");
        Check(NyarInfoParse.SpawnFlag(unitsForm, "loot on"), "loot flag");
        Check(NyarInfoParse.SpawnFlag(unitsForm, "claimed territory allowed"), "territory flag");

        var below = new[]
        {
            "big \"Big\" enabled pillar spawns trigger manual duration 900s",
            "action: 2 waves every 30s, radius 5, at the admin, units below",
            "units: 4 CHAR_Very_Long_A, 3 CHAR_Very_Long_B",
            "units: 2 from troops",
            "final wave adds: 4 bosses",
        };
        Eq(NyarInfoParse.UnitsFormUnits(below), "CHAR_Very_Long_A:4,CHAR_Very_Long_B:3,pool:troops:2", "units below, wrapped");
        Eq(NyarInfoParse.FinalBosses(below), null, "shortened bosses line → unreadable");
        Eq(NyarInfoParse.Location(below, out _, out _), "admin", "location admin");
        Eq(NyarInfoParse.Behaviour(below), "none", "no hunt → none");
        Eq(NyarInfoParse.Pursuit(below), 0, "no pursuit");

        var waveList = new[]
        {
            "wv \"Waves\" disabled pillar spawns trigger manual duration 1200s",
            "action: 3 waves, radius 10, in the scope regions",
            "wave 1: 6 units (CHAR_A ×4, 2 from troops), at start",
            "wave 2: 5 units (CHAR_B ×5 L30 hp×1.5), after 45 s or when cleared",
            "wave 3: 12 units, when cleared, units below",
            "wave 3 units: CHAR_C ×6, CHAR_C ×6",
            "scoreboard: off",
            "behaviour: hunts the nearest player in the regions, hidden jumps beyond 60 m",
            "wave 2 adds: CHAR_V_VBlood x1, 1-2 bosses from elite",
        };
        Check(NyarInfoParse.IsWaveList(waveList), "wave list detected");
        var waves = NyarInfoParse.Waves(waveList);
        Eq(waves.Count, 3, "three waves");
        Eq(waves[0].Units, "CHAR_A:4,pool:troops:2", "wave 1 units");
        Eq(waves[0].Rule, "at start", "wave 1 rule");
        Eq(waves[1].Units, "CHAR_B:5", "wave 2 units (extras stripped)");
        Check(waves[1].HasExtras, "wave 2 has per-unit extras");
        Check(waves[1].AfterSeconds == 45 && waves[1].WhenCleared, "wave 2 after 45 s or when cleared");
        Eq(waves[1].Bosses, "CHAR_V_VBlood:1,pool:elite:1-2", "wave 2 own bosses");
        Eq(waves[2].Units, "CHAR_C:6,CHAR_C:6", "wave 3 units below (repeat kept)");
        Check(waves[2].AfterSeconds == 0 && waves[2].WhenCleared, "wave 3 when cleared");
        Eq(waves[2].Bosses, "", "wave 3 no bosses");
        Eq(NyarInfoParse.Location(waveList, out _, out _), "region", "location region");
        Eq(NyarInfoParse.Behaviour(waveList), "regionhunt", "behaviour regionhunt");

        var empower = new[]
        {
            "emp \"Empower\" enabled pillar empowerment trigger gametime night duration 600s",
            "action: empower Bandits, Undead, also CHAR_X, V Bloods included: physicalPower x1.5, maxHealth x2",
        };
        Check(NyarInfoParse.Empower(empower, out var facs, out var stats), "empower line found");
        Eq(facs, "Faction_Bandits,Faction_Undead", "empower factions");
        Eq(stats.TryGetValue("maxHealth", out var st) ? st : "", "2", "empower stat");
        Eq(NyarInfoParse.Behaviour(empower), "", "empower has no behaviour");

        var badPool = new[] { "pools: troops (3 units): CHAR_A, CHAR_B" };
        NyarInfoParse.Pools(badPool, out var c2);
        Check(!c2, "pool count mismatch → incomplete");
    }

    // ------------------------------------------------------------------ editor form state (PLAN §B2 changes 7-8)
    private static void Form()
    {
        var unitsForm = new[]
        {
            "raid \"Bandit Raid\" disabled pillar spawns trigger manual duration 600s",
            "conditions: minPlayers 1, cooldown 30 min, chance 100%, window none, mode any",
            "action: 3 waves every 60s, radius 10, around a player 20-40 m, units 4 CHAR_Bandit_Thug, 2 from troops, unit lifetime 600s",
            "spawn: modifiers level 40, maxHealth x1.5; loot on; hunt 30 m",
            "final wave adds: 4 bosses",
            "pools: troops (2 units): CHAR_A1, CHAR_A2",
            "trigger scope: Global",
        };
        var r = NyarForm.Read(unitsForm);
        string V(NyarFormLoad l, string f) => l.Values.TryGetValue(f, out var x) ? x : "<absent>";
        Eq(r.Kind, NyarEventKind.UnitsForm, "form kind units");
        Eq(V(r, "name"), "Bandit Raid", "form name");
        Eq(V(r, "durationSeconds"), "600", "form duration");
        Eq(V(r, "conditions.minPlayers") + "/" + V(r, "conditions.cooldownMinutes") + "/" + V(r, "conditions.chancePercent"), "1/30/100", "form conditions");
        Eq(V(r, "action.waves") + "/" + V(r, "action.intervalSeconds") + "/" + V(r, "action.radius"), "3/60/10", "form waves/interval/radius");
        Eq(V(r, "location"), "aroundplayer 20 40", "form location");
        Eq(V(r, "action.behaviour"), "hunt 30", "form behaviour");
        Eq(V(r, "action.loot") + "/" + V(r, "action.allowTerritory"), "true/false", "form loot/territory");
        Eq(V(r, "action.pursuit"), "none", "form no pursuit -> none");
        Eq(V(r, "action.units"), "CHAR_Bandit_Thug:4,pool:troops:2", "form units");
        Eq(V(r, "action.modifiers.level") + "/" + V(r, "action.modifiers.levelDelta"), "40/none", "form level / no shift");
        Eq(V(r, "action.modifiers.maxHealth") + "/" + V(r, "action.modifiers.power"), "1.5/none", "form modifiers");
        Check(r.Unread.Contains("action.bosses") && !r.Values.ContainsKey("action.bosses"), "shortened bosses line -> unread, no guess");
        Eq(r.Pools.Count, 1, "form pools");

        var wl = new[]
        {
            "wv \"Waves\" disabled pillar spawns trigger manual duration 1200s",
            "conditions: minPlayers 0, cooldown 0 min, chance 50%, window none, mode any",
            "action: 3 waves, radius 10, in the scope regions",
            "wave 1: 6 units (CHAR_A ×4, 2 from troops), at start",
            "wave 2: 5 units (CHAR_B ×5), after 45 s or when cleared",
            "wave 3: 6 units (CHAR_C ×6), after 30 s",
            "scoreboard: off",
            "location: anywhere in the scope regions",
            "wave 2 adds: CHAR_V_VBlood x1",
            "pursuit: after 90 s with no player within 40 m",
        };
        var w = NyarForm.Read(wl);
        Eq(w.Kind, NyarEventKind.WaveList, "form kind wave list");
        Eq(V(w, "location"), "region", "wave list location region");
        Eq(V(w, "action.waveList.1.units"), "CHAR_A:4,pool:troops:2", "wave 1 units");
        Eq(V(w, NyarForm.StartKey(1)), "<absent>", "wave 1 has no start rule");
        Eq(V(w, NyarForm.StartKey(2)), "afterOrCleared 45", "wave 2 start");
        Eq(V(w, NyarForm.StartKey(3)), "after 30", "wave 3 start");
        Eq(V(w, "action.waveList.2.bosses"), "CHAR_V_VBlood:1", "wave 2 bosses");
        Eq(V(w, "action.waveList.3.bosses"), "none", "wave 3 no bosses -> none");
        Eq(V(w, "action.bosses"), "none", "no final bosses -> none");
        Eq(V(w, "action.pursuit"), "90", "wave list pursuit");
        Check(!w.Values.ContainsKey("action.waves") && !w.Values.ContainsKey("action.units"), "wave list has no units-form fields");

        var emp = NyarForm.Read(new[]
        {
            "emp \"Empower\" enabled pillar empowerment trigger gametime night duration 600s",
            "conditions: minPlayers 0, cooldown 0 min, chance 100%, window none, mode any",
            "action: empower Bandits, Undead: physicalPower x1.5",
        });
        Eq(emp.Kind, NyarEventKind.Empower, "form kind empower");
        Eq(V(emp, "action.factions"), "Faction_Bandits,Faction_Undead", "empower factions");
        Eq(V(emp, "action.stats.physicalPower") + "/" + V(emp, "action.stats.spellPower"), "1.5/1.0", "unlisted stat reads 1.0");
        Ok(NyarFieldRules.CheckValue(NyarFieldRules.Spec("action.stats.spellPower"), "1.0"), "1.0 is a valid stat");

        var apo = NyarForm.Read(new[]
        {
            "apo \"Siege\" disabled pillar sieges trigger manual duration 86400s",
            "conditions: minPlayers 0, cooldown 0 min, chance 100%, window none, mode any",
            "action: apocalypse of the Undead, march 1.5 m/s",
            "hotspots: 3, at least 5 natives each, 80 m apart",
            "roamers: 2 per hotspot, +1 per kill, max 40, 3 spread per minute, hunt range 30",
            "hordes: 4, first after 10 min, every 30 min, size 20 +5 each",
            "goal: the village, by the well, radius 25, at the starting admin",
            "last horde adds: CHAR_Undead_Boss_VBlood x1",
            "scoreboard: on",
        });
        Eq(apo.Kind, NyarEventKind.Apocalypse, "form kind apocalypse");
        Eq(V(apo, "action.marchSpeed"), "1.5", "apo march");
        Eq(V(apo, "action.goal.name") + "|" + V(apo, "action.goal.radius"), "the village, by the well|25", "apo goal name with comma");
        Eq(V(apo, "action.hordes.count") + "/" + V(apo, "action.hordes.everyMinutes") + "/" + V(apo, "action.hordes.size"), "4/30/20", "apo hordes");
        Eq(V(apo, "action.roamers.max"), "40", "apo roamers");
        Eq(V(apo, "action.bosses"), "CHAR_Undead_Boss_VBlood:1", "apo bosses");

        // editing state
        var f = new NyarForm();
        f.Apply(r);
        Check(f.HaveLoad, "form loaded");
        Eq(f.Get("action.waves"), "3", "get loaded");
        f.Set("action.waves", "5");
        Check(f.IsChanged("action.waves"), "changed after set");
        f.Set("action.waves", "3");
        Check(!f.IsChanged("action.waves"), "back to loaded -> not changed");
        Eq(f.ChangeCount(), 0, "no changes");
        f.Set("conditions.chancePercent", "50");
        f.Set("action.waves", "4");
        f.Set("action.bosses", "CHAR_X_VBlood:1");          // an unread field: any value counts as a change
        var ch = f.Changes();
        Eq(string.Join(",", ch.Select(c => c.Key)), "conditions.chancePercent,action.waves,action.bosses", "changes keep first-change order");
        f.MarkSaved("action.waves");
        Check(f.IsSaved("action.waves") && !f.IsChanged("action.waves"), "saved mark");
        Eq(f.Get("action.waves"), "4", "saved value is the new loaded value");
        f.MarkRefused("conditions.chancePercent", "nope");
        Eq(f.Refused("conditions.chancePercent"), "nope", "refused mark");
        Check(f.IsChanged("conditions.chancePercent"), "refused field stays changed");
        f.Set("conditions.chancePercent", "60");
        Eq(f.Refused("conditions.chancePercent"), "", "editing clears the refusal");
        f.Undo(k => k == "conditions.chancePercent");
        Eq(f.Get("conditions.chancePercent"), "100", "undo one field");
        Eq(f.ChangeCount(), 1, "the other change survives a partial undo");
        f.Set("name", "");
        Check(!f.IsChanged("name"), "an emptied box is not a change to send");

        // a fresh reply: a change the server now holds drops out; other changes stay
        var r2 = NyarForm.Read(unitsForm.Select(l => l.Replace("final wave adds: 4 bosses", "final wave adds: CHAR_X_VBlood x1")).ToArray());
        f.Set("action.radius", "12");
        f.Apply(r2);
        Check(!f.IsChanged("action.bosses"), "change now on the server drops out");
        Check(f.IsChanged("action.radius"), "pending change survives a reload");
        Check(!f.IsSaved("action.waves"), "reload clears saved marks");
        f.Undo();
        Eq(f.ChangeCount(), 0, "undo all");

        // converted to a wave list: unsaved units-form edits no longer apply and drop out; others stay
        f.Set("action.modifiers.maxHealth", "2.0");
        f.Set("action.waves", "5");
        f.Set("name", "Renamed");
        f.Apply(w);
        Check(!f.IsChanged("action.modifiers.maxHealth") && !f.IsChanged("action.waves"), "conversion drops units-form edits");
        Check(f.IsChanged("name"), "conversion keeps other edits");
        f.Undo();
        f.Apply(r);

        // start-rule key -> its sets
        var sets = NyarForm.SetsFor(NyarForm.StartKey(2), "after 30");
        Eq(string.Join(" ", sets.Select(x => x.Key + "=" + x.Value)), "action.waveList.2.afterSeconds=30 action.waveList.2.whenCleared=false", "start after -> sets");
        sets = NyarForm.SetsFor(NyarForm.StartKey(3), "cleared");
        Eq(string.Join(" ", sets.Select(x => x.Key + "=" + x.Value)), "action.waveList.3.whenCleared=true action.waveList.3.afterSeconds=none", "start cleared -> sets");
        Eq(NyarForm.SetsFor("action.radius", "5").Count, 1, "plain field -> one set");
        Ok(NyarForm.CheckStart("cleared"), "start cleared valid");
        Ok(NyarForm.CheckStart("afterOrCleared 3600"), "start 3600 valid");
        Bad(NyarForm.CheckStart("after 5"), "start 5 s refused");
        Bad(NyarForm.CheckStart("after"), "start without seconds refused");
    }

    // ------------------------------------------------------------------ templates panel groups (PLAN §C1)
    private static void Templates()
    {
        var w = NyarMessages.TemplateGroup("spawns", "bandit-raid");
        var t = NyarMessages.TemplateGroup("spawns", "bandit-raid-timed");
        var o = NyarMessages.TemplateGroup("Spawns", "undead-onslaught");
        var e = NyarMessages.TemplateGroup("empowerment", "night-bandits");
        var x = NyarMessages.TemplateGroup("weird", "x");
        var z = NyarMessages.TemplateGroup("spawns", "raid-timedout");
        Eq(w.Heading, "Spawns — Wave events", "plain spawns group");
        Eq(t.Heading, "Spawns — Timed waves", "-timed group");
        Eq(o.Heading, "Spawns — Onslaughts", "-onslaught group (pillar case-insensitive)");
        Eq(z.Heading, "Spawns — Wave events", "unknown suffix falls back");
        Check(e.Order < w.Order && w.Order < t.Order && t.Order < o.Order && o.Order < x.Order, "group order: pillars, then spawns sub-groups, unknown last");
        Eq(x.Heading, "Weird", "unknown pillar keeps its name");
        Eq(NyarMessages.TemplateGroup("", "a").Heading, "Other", "empty pillar → Other");
        Check(t.Blurb.Contains("75 s") && o.Blurb.Contains("boss"), "sub-group blurbs");
    }
}
