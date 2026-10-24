using System.Text.RegularExpressions;
using Raphael.Services.Nyar;

// 0.73.0: Nyarlathotep 0.14.0 "undead hordes" (PLAN-NYAR014.md). Server shapes are copied from Nyarlathotep f340f75:
// CommandArgs.HordeValue / DestinationValue (ranges), EventAdmin.InfestationLines / HordeMarchLines / MarchLine /
// SpreadText / AtText (the `event info` lines) and EventAdmin.Info line 843 (an invalid definition's one-line reply).
internal static partial class Program
{
    private static void UndeadHordesTests()
    {
        HordeSpecs();
        HordeInfoParse();
        HordeForm();
        HordeSaveOrder();
        HordeFieldsFor();
        HordeTemplates();
        HordeSourceGuards();
    }

    private static string V(NyarFormLoad l, string f) => l.Values.TryGetValue(f, out var x) ? x : "<absent>";

    // ------------------------------------------------------------------ ranges (CommandArgs.HordeValue / DestinationValue)
    private static void HordeSpecs()
    {
        void Range(string field, string below, string min, string max, string above)
        {
            var s = NyarFieldRules.Spec(field);
            Check(s != null, $"spec exists for {field}");
            if (s == null) return;
            Bad(NyarFieldRules.CheckValue(s, below), $"{field} {below}");
            Ok(NyarFieldRules.CheckValue(s, min), $"{field} {min}");
            Ok(NyarFieldRules.CheckValue(s, max), $"{field} {max}");
            Bad(NyarFieldRules.CheckValue(s, above), $"{field} {above}");
        }
        Range("action.destination.radius", "9", "10", "100", "101");
        Range("action.origins.count", "0", "1", "10", "11");
        Range("action.origins.minNatives", "0", "1", "20", "21");
        Range("action.origins.minSpacing", "99", "100", "2000", "2001");
        Range("action.origins.perOrigin", "0", "1", "100", "101");
        Range("action.hunters", "-1", "0", "100", "101");
        Range("action.infest.hunters", "-1", "0", "100", "101");
        Range("action.driftRadius", "-1", "0", "2000", "2001");
        Range("action.infest.driftRadius", "-1", "0", "2000", "2001");
        Range("action.start.minDistance", "99", "100", "3000", "3001");
        foreach (var b in NyarFieldRules.SpreadBlocks)
        {
            Range(b + "perKillMin", "-1", "0", "3", "4");
            Range(b + "perKillMax", "-1", "0", "3", "4");
            Range(b + "max", "0", "1", "500", "501");
            Range(b + "perMinute", "0", "1", "30", "31");
        }
        // marchSpeed is the server's Decimal2: at most 6 characters, 2 decimals, no leading / trailing dot, 0.5-5.0.
        var ms = NyarFieldRules.Spec("action.marchSpeed");
        Bad(NyarFieldRules.CheckValue(ms, "0.49"), "marchSpeed 0.49");
        Ok(NyarFieldRules.CheckValue(ms, "0.5"), "marchSpeed 0.5");
        Ok(NyarFieldRules.CheckValue(ms, "5.0"), "marchSpeed 5.0");
        Bad(NyarFieldRules.CheckValue(ms, "5.01"), "marchSpeed 5.01");
        Bad(NyarFieldRules.CheckValue(ms, "1.234"), "marchSpeed 1.234 (three decimals)");
        Bad(NyarFieldRules.CheckValue(ms, ".5"), "marchSpeed .5 (leading dot)");
        Bad(NyarFieldRules.CheckValue(ms, "none"), "marchSpeed none");
        // Durations: the apocalypse family (Apocalypse, Infestation, HordeMarch) runs 600-604800 s.
        Check(NyarForm.IsApocalypseFamily(NyarEventKind.Infestation) && NyarForm.IsApocalypseFamily(NyarEventKind.HordeMarch)
              && NyarForm.IsApocalypseFamily(NyarEventKind.Apocalypse) && !NyarForm.IsApocalypseFamily(NyarEventKind.UnitsForm), "apocalypse family");
        var dur = NyarFieldRules.Spec("durationSeconds", NyarForm.IsApocalypseFamily(NyarEventKind.HordeMarch));
        Ok(NyarFieldRules.CheckValue(dur, "604800"), "horde march duration 604800");
        Bad(NyarFieldRules.CheckValue(dur, "599"), "horde march duration 599");
        // Destination name: the server's IsPlainText(v, 40).
        var nm = NyarFieldRules.Spec("action.destination.name");
        Ok(NyarFieldRules.CheckValue(nm, "a"), "name 1 char");
        Ok(NyarFieldRules.CheckValue(nm, new string('x', 40)), "name 40 chars");
        Bad(NyarFieldRules.CheckValue(nm, new string('x', 41)), "name 41 chars");
        Bad(NyarFieldRules.CheckValue(nm, ""), "name empty");
        foreach (var bad in new[] { "a<b", "a>b", "a\nb", "a\tb", "a\u0085b" }) Bad(NyarFieldRules.CheckValue(nm, bad), $"name with {(int)bad[1]:X}");
        Ok(NyarFieldRules.CheckValue(nm, "the bridge, north"), "name with a comma");
        // Destination and spread pool values.
        Ok(NyarFieldRules.CheckDestination("here", true), "destination here");
        Ok(NyarFieldRules.CheckDestination("none", true), "destination none on spawn waves");
        Bad(NyarFieldRules.CheckDestination("none", false), "destination none on a horde march");
        Bad(NyarFieldRules.CheckDestination("there", true), "destination there");
        Ok(NyarFieldRules.CheckSpreadPool("none"), "spread pool none");
        Ok(NyarFieldRules.CheckSpreadPool(new string('a', 24)), "spread pool 24 chars");
        foreach (var bad in new[] { new string('a', 25), "Troops", "troop_s", "two words", "" }) Bad(NyarFieldRules.CheckSpreadPool(bad), $"spread pool '{bad}'");
        // Readable distances.
        Eq(NyarFieldRules.Echo(NyarFieldRules.Spec("action.origins.minSpacing"), "1500"), "= 1.5 km", "1500 m echoes 1.5 km");
        Eq(NyarFieldRules.Echo(NyarFieldRules.Spec("action.origins.minSpacing"), "300"), "", "300 m has no echo");
        // A swarm's growth (spawn waves) is not a spread block.
        Eq(NyarFieldRules.SpreadBlockOf("action.growth.addUnits"), null, "action.growth.addUnits is not a spread key");
        Eq(NyarFieldRules.SpreadBlockOf("action.infest.spread.max"), "action.infest.spread.", "infest spread block");
        Eq(NyarFieldRules.SpreadBlockOf("action.spread.pool"), "action.spread.", "spread block");
    }

    // ------------------------------------------------------------------ info lines
    private static void HordeInfoParse()
    {
        var sp = NyarInfoParse.Spread("+2 per kill, max 300, 20 per minute");
        Eq(sp == null ? "null" : $"{sp["perKillMin"]}/{sp["perKillMax"]}/{sp["max"]}/{sp["perMinute"]}/{sp["pool"]}", "2/2/300/20/none", "spread +2, no pool");
        sp = NyarInfoParse.Spread("+1-3 per kill from troops, max 400, 20 per minute");
        Eq(sp == null ? "null" : $"{sp["perKillMin"]}/{sp["perKillMax"]}/{sp["max"]}/{sp["perMinute"]}/{sp["pool"]}", "1/3/400/20/troops", "spread +1-3 from troops");
        foreach (var bad in new[] { "+1-3 per kill, max 400", "+1-3 per kill from troops, max 400, 20 per minute extra", "1-3 per kill, max 4, 2 per minute", "+12 per kill, max 4, 2 per minute" })
            Check(NyarInfoParse.Spread(bad) == null, $"spread unread: {bad}");

        Eq(string.Join("/", NyarInfoParse.Origins("origins: 5, at least 5 natives each, 300 m apart, 10 units each") ?? new[] { "null" }), "5/5/300/10", "origins");
        Check(NyarInfoParse.Origins("origins: 5, at least 5 natives each, 300 m apart") == null, "origins truncated → unread");
        Eq(string.Join("/", NyarInfoParse.Hunters("hunters: 50 %, drift radius 400 m") ?? new[] { "null" }), "50/400", "hunters");
        var inf = NyarInfoParse.Infest("infest: hunters 60 %, drift radius 500 m, spread +1-3 per kill from troops, max 400, 20 per minute");
        Eq(inf == null ? "null" : $"{inf[0]}/{inf[1]}/{inf[2]}", "60/500/+1-3 per kill from troops, max 400, 20 per minute", "infest");

        Eq(NyarInfoParse.Start("start: at 123.4 -56 height 7.5", out var md), "point", "start at a point with height");
        Eq(NyarInfoParse.Start("start: at -1 2", out md), "point", "start at a point");
        Eq(NyarInfoParse.Start("start: at the starting admin", out md), "admin", "start at the admin");
        Eq(NyarInfoParse.Start("start: the natives' hotspot nearest the destination, at least 300 m away", out md) + " " + md, "hotspot 300", "hotspot start");
        foreach (var bad in new[] { "start: at 1garbage", "start: at 1.25 2", "start: at 1 2 height", "start: somewhere" })
            Eq(NyarInfoParse.Start(bad, out _), null, $"start unread: {bad}");

        Eq(string.Join("|", NyarInfoParse.Destination("destination: the starting admin, radius 30, at the starting admin") ?? new[] { "null" }),
            "the starting admin|30", "destination at the admin");
        Eq(string.Join("|", NyarInfoParse.Destination("destination: the bridge, radius 5, at 1 2, radius 20, at 10.5 -3 height 2") ?? new[] { "null" }),
            "the bridge, radius 5, at 1 2|20", "destination name with commas and 'radius 5, at' takes the last tail");
        Check(NyarInfoParse.Destination("destination: x, radius 20, at 1.25 2") == null, "destination with 2-decimal coordinate → unread");

        // SpawnWaves MarchLine (EventAdmin.cs:953-958): an Admin start has no parentheses; the destination always has them.
        var cases = new[]
        {
            ("march: from (1.5, -2) to the bridge (10, 20) at 2 m/s", "the bridge|2"),
            ("march: from the starting admin to the bridge (10, 20) at 1.75 m/s", "the bridge|1.75"),
            ("march: from (1, 2) to camp (the starting admin) at 0.5 m/s", "camp|0.5"),
            ("march: from the starting admin to camp (the starting admin) at 5 m/s", "camp|5"),
            ("march: from (1, 2) to a (b), (c) (3, 4) at 2 m/s", "a (b), (c)|2"),
        };
        foreach (var (line, want) in cases) Eq(string.Join("|", NyarInfoParse.March(line) ?? new[] { "null" }), want, $"march line: {line}");
        foreach (var bad in new[] { "march: from (1, 2) to x (3, 4) at 2", "march: from (1, 2) to x (3, 4) at 2.123 m/s", "march: from 1, 2 to x (3, 4) at 2 m/s" })
            Check(NyarInfoParse.March(bad) == null, $"march unread: {bad}");

        Eq(NyarInfoParse.HordeSize(new[] { "action: horde march of the Undead, march 2 m/s", "horde of 200: 160 from troops, 32 from elite, 8 from strong" }), 200, "horde size");
        Eq(NyarInfoParse.FinalBosses(new[] { "horde adds: CHAR_Undead_BishopOfDeath_VBlood x1, 1-2 bosses from elite" }),
            "CHAR_Undead_BishopOfDeath_VBlood:1,pool:elite:1-2", "horde adds as bosses");
        Eq(NyarInfoParse.FinalBosses(new[] { "horde adds: 4 bosses at most" }), null, "shortened horde adds → unread");
    }

    private static readonly string[] InfestationInfo =
    {
        "ui-test \"Undead Infestation\" disabled pillar spawns trigger manual duration 14400s",
        "conditions: minPlayers 0, cooldown 0 min, chance 100%, window none, mode any",
        "action: infestation of the Undead",
        "origins: 5, at least 5 natives each, 300 m apart, 10 units each",
        "hunters: 50 %, drift radius 400 m",
        "spread: +1-3 per kill from troops, max 300, 20 per minute",
        "units: 8 from troops, 2 from elite, 1 from strong",
        "pools: troops (2 units): CHAR_Undead_SkeletonSoldier_Base, CHAR_Undead_ZealousCultist_Ghost",
        "pools: elite (1 units): CHAR_Undead_Assassin",
        "scoreboard: off",
        "trigger scope: Global",
        "action scope: Global",
        "not running",
    };

    private static string[] HordeMarchInfo(string start) => new[]
    {
        "hm-test \"Horde March\" disabled pillar spawns trigger manual duration 3600s",
        "conditions: minPlayers 0, cooldown 0 min, chance 100%, window none, mode any",
        "action: horde march of the Undead, march 2 m/s",
        start,
        "destination: the starting admin, radius 30, at the starting admin",
        "growth: +1-3 per kill from troops, max 400, 20 per minute",
        "infest: hunters 60 %, drift radius 500 m, spread +1-3 per kill from troops, max 400, 20 per minute",
        "horde of 200: 160 from troops, 32 from elite, 8 from strong",
        "pools: troops (1 units): CHAR_Undead_SkeletonSoldier_Base",
        "horde adds: CHAR_Undead_BishopOfDeath_VBlood x1",
        "scoreboard: off",
        "trigger scope: Global",
        "action scope: Global",
        "not running",
    };

    private static string[] SpawnInfo(params string[] extra)
    {
        var l = new List<string>
        {
            "dw \"Destination waves\" disabled pillar spawns trigger manual duration 600s",
            "conditions: minPlayers 0, cooldown 0 min, chance 100%, window none, mode any",
            "action: 3 waves every 60s, radius 10, at a set spot, units 4 CHAR_Bandit_Thug",
            "spawn: hunt 30 m",
        };
        l.AddRange(extra);
        l.Add("trigger scope: Global");
        return l.ToArray();
    }

    // ------------------------------------------------------------------ NyarForm
    private static void HordeForm()
    {
        var inf = NyarForm.Read(InfestationInfo);
        Eq(inf.Kind, NyarEventKind.Infestation, "kind infestation");
        Eq(V(inf, "durationSeconds"), "14400", "infestation duration");
        Eq($"{V(inf, "action.origins.count")}/{V(inf, "action.origins.minNatives")}/{V(inf, "action.origins.minSpacing")}/{V(inf, "action.origins.perOrigin")}", "5/5/300/10", "infestation origins");
        Eq($"{V(inf, "action.hunters")}/{V(inf, "action.driftRadius")}", "50/400", "infestation hunters");
        Eq($"{V(inf, "action.spread.perKillMin")}/{V(inf, "action.spread.perKillMax")}/{V(inf, "action.spread.max")}/{V(inf, "action.spread.perMinute")}/{V(inf, "action.spread.pool")}",
            "1/3/300/20/troops", "infestation spread");
        Eq(string.Join(",", inf.Pools.Select(p => p.Key)), "troops,elite", "infestation pools are read");
        Eq(inf.Unread.Count, 0, "infestation: nothing unread");
        var noHunters = NyarForm.Read(InfestationInfo.Where(l => !l.StartsWith("hunters: ")).ToArray());
        Check(noHunters.Unread.Contains("action.hunters") && noHunters.Unread.Contains("action.driftRadius"), "missing hunters line → unread");

        foreach (var (line, kind, dist) in new[]
        {
            ("start: at 10 20", "point", "<absent>"),
            ("start: at the starting admin", "admin", "<absent>"),
            ("start: the natives' hotspot nearest the destination, at least 300 m away", "hotspot", "300"),
        })
        {
            var hm = NyarForm.Read(HordeMarchInfo(line));
            Eq(hm.Kind, NyarEventKind.HordeMarch, $"kind horde march ({kind})");
            Eq(hm.StartKind + " " + V(hm, "action.start.minDistance"), kind + " " + dist, $"horde march start {kind}");
        }
        var h = NyarForm.Read(HordeMarchInfo("start: at the starting admin"));
        Eq(V(h, "action.marchSpeed"), "2", "horde march speed");
        Eq($"{V(h, "action.destination")}|{V(h, "action.destination.name")}|{V(h, "action.destination.radius")}", "here|the starting admin|30", "horde march destination");
        Eq($"{V(h, "action.growth.perKillMin")}-{V(h, "action.growth.perKillMax")} max {V(h, "action.growth.max")}", "1-3 max 400", "horde march growth");
        Eq($"{V(h, "action.infest.hunters")}/{V(h, "action.infest.driftRadius")}/{V(h, "action.infest.spread.max")}", "60/500/400", "horde march infest");
        Eq(V(h, "action.bosses"), "CHAR_Undead_BishopOfDeath_VBlood:1", "horde march bosses");
        Eq(h.HordeSize, 200, "horde march size");
        Eq(h.Unread.Count, 0, "horde march: nothing unread");
        var bare = NyarForm.Read(new[] { HordeMarchInfo("x")[0], "action: horde march of the Undead, march 2 m/s" });
        Check(bare.Kind == NyarEventKind.HordeMarch && bare.Unread.Contains("action.destination.name") && bare.Unread.Contains("action.growth.max")
              && bare.StartKind == "", "horde march without its block → unread, no guess");

        // Kind detection: unknown or near-miss action lines stay Other and never fall into spawn waves.
        Eq(NyarForm.Read(new[] { InfestationInfo[0], "action: something new" }).Kind, NyarEventKind.Other, "unknown action → Other");
        Eq(NyarForm.Read(new[] { InfestationInfo[0], "action: infestation" }).Kind, NyarEventKind.Other, "'action: infestation' without 'of the' → Other");

        // Destination waves.
        var none = NyarForm.Read(SpawnInfo());
        Eq(V(none, "action.destination"), "none", "spawn waves without a march line → no destination");
        Eq(none.DestinationConflicts.Count, 0, "set spot, no conflicts");
        var dw = NyarForm.Read(SpawnInfo("march: from (1, 2) to the old bridge (10, 20) at 2.5 m/s"));
        Eq($"{V(dw, "action.destination")}|{V(dw, "action.destination.name")}|{V(dw, "action.marchSpeed")}", "here|the old bridge|2.5", "destination wave");
        Check(dw.Unread.Contains("action.destination.radius"), "destination radius isn't printed → unread");
        var f = new NyarForm();
        f.Apply(dw);
        Eq(f.ChangeCount(), 0, "R1: an unread radius is never a change");
        f.Set("action.destination.name", "the new bridge");
        Eq(string.Join(",", f.Changes().Select(c => c.Key)), "action.destination.name", "R1: editing the name sends only the name");

        foreach (var (extra, conflict) in new[]
        {
            ("pursuit: after 90 s with no player within 40 m", "pursuit"),
            ("region lock: on", "region lock"),
            ("arena: 40 m around the spot", "arena"),
        })
            Eq(string.Join(",", NyarForm.Read(SpawnInfo(extra)).DestinationConflicts), conflict, $"conflict {conflict}");
        var fan = NyarForm.Read(SpawnInfo().Select(l => l == "spawn: hunt 30 m" ? "spawn: hunt 30 m; fanOut 3 players 200 m apart" : l).ToArray());
        Eq(string.Join(",", fan.DestinationConflicts), "fan-out", "conflict fan-out");
        var around = NyarForm.Read(SpawnInfo().Select(l => l.Replace("at a set spot", "around a player 20-40 m")).ToArray());
        Eq(string.Join(",", around.DestinationConflicts), "location", "conflict location around a player");

        // An invalid definition: one line, the reason.
        var ro = NyarForm.Read(new[] { "a.b+c disabled: action.destination.radius is missing" });
        Check(ro.ReasonOnly && ro.NotReady == "action.destination.radius is missing" && ro.Kind == NyarEventKind.Unknown, "R3-4: reason-only reply (regex characters in the id)");
        Check(!NyarForm.Read(new[] { "dw \"x\" disabled pillar spawns trigger manual duration 600s" }).ReasonOnly, "a head line is not a reason");
        Check(!NyarForm.Read(new[] { "a disabled: x", "more" }).ReasonOnly, "two lines are not a reason-only reply");

        // R2-1 / R3-2: Destination here acknowledged → reason-only reply keeps the form → full reply replaces it.
        var g = new NyarForm();
        g.Apply(none);
        g.Set("name", "Pending name");
        g.Acknowledge("action.destination", "here");
        g.Apply(ro);
        Check(g.HaveLoad && g.Load.Kind == NyarEventKind.UnitsForm, "reason-only keeps the last kind");
        Eq(g.Load.NotReady, "action.destination.radius is missing", "reason-only sets NotReady");
        Eq(g.Get("action.destination"), "here", "acknowledged destination survives the reason-only reply");
        Check(g.IsChanged("name"), "a pending edit survives the reason-only reply");
        g.Apply(dw);
        Eq(g.Load.NotReady, "", "a full reply clears NotReady");
        Check(g.IsChanged("name") && g.Get("action.destination.name") == "the old bridge", "a pending edit survives the full reply too");
        var fresh = new NyarForm();
        fresh.Apply(ro);
        Check(fresh.HaveLoad && fresh.Load.Kind == NyarEventKind.Unknown && fresh.Load.ReasonOnly, "R3-1: reason-only with no load → reason alone");

        // Side effects of confirmed sets.
        var s = new NyarForm();
        s.Apply(dw);
        s.Acknowledge("action.destination", "none");
        Check(s.Loaded("action.destination.name") == "" && s.Loaded("action.marchSpeed") == "" && !s.IsUnread("action.destination.radius"),
            "R13: no destination drops its name, radius and march speed");
        var t = new NyarForm();
        t.Apply(NyarForm.Read(HordeMarchInfo("start: at 10 20")));
        t.Set("action.start.minDistance", "500");
        t.MarkSaved("action.start.minDistance");
        Eq(t.Load.StartKind, "hotspot", "R2-6: point → hotspot");
        t.Acknowledge("action.start", "here");
        Check(t.Load.StartKind == "point" && t.Loaded("action.start.minDistance") == "", "R2-6: hotspot → point drops the distance");
        var u = new NyarForm();
        u.Apply(NyarForm.Read(HordeMarchInfo("start: at the starting admin")));
        u.Acknowledge("action.start", "here");
        Eq(u.Load.StartKind, "point", "R2-6: admin → point");

        // R10: untouched round trip, per kind.
        foreach (var (name, load) in new[] { ("infestation", inf), ("horde march", h), ("destination wave", dw) })
        {
            var rt = new NyarForm();
            rt.Apply(load);
            Eq(rt.ChangeCount(), 0, $"R10: {name} read untouched → no changes");
        }
    }

    // ------------------------------------------------------------------ save order
    private static void HordeSaveOrder()
    {
        string Order(Func<string, string> loaded, params (string, string)[] changes)
            => string.Join(" ", NyarFieldRules.OrderForSave(changes.Select(c => new KeyValuePair<string, string>(c.Item1, c.Item2)), loaded).Select(c => c.Key));
        Eq(Order(null, ("action.marchSpeed", "3"), ("action.destination.name", "x"), ("action.destination.radius", "20"), ("action.destination", "here")),
            "action.destination action.marchSpeed action.destination.name action.destination.radius", "destination first");
        Func<string, string> loaded = f => f == "action.spread.perKillMax" ? "1" : f == "action.spread.perKillMin" ? "1" : "";
        Eq(Order(loaded, ("action.spread.perKillMin", "2"), ("action.spread.perKillMax", "3")),
            "action.spread.perKillMax action.spread.perKillMin", "R7: new min above the loaded max → max first");
        Func<string, string> loaded2 = f => f == "action.spread.perKillMax" ? "3" : f == "action.spread.perKillMin" ? "2" : "";
        Eq(Order(loaded2, ("action.spread.perKillMax", "1"), ("action.spread.perKillMin", "0")),
            "action.spread.perKillMin action.spread.perKillMax", "R7: lowering both → min first");
        Eq(Order(loaded2, ("action.spread.perKillMax", "2"), ("action.spread.perKillMin", "2")),
            "action.spread.perKillMin action.spread.perKillMax", "R7: equal values → min first");
        Eq(Order(loaded, ("action.growth.perKillMax", "3")), "action.growth.perKillMax", "R7: one side alone");
        Eq(Order(loaded, ("action.growth.perKillMin", "2"), ("action.spread.perKillMax", "3"), ("action.spread.perKillMin", "1")),
            "action.growth.perKillMin action.spread.perKillMin action.spread.perKillMax", "R7: each block on its own");
        Eq(Order(null, ("action.start.minDistance", "300"), ("action.start", "here")), "action.start action.start.minDistance", "start before its distance");
    }

    // ------------------------------------------------------------------ "Any other field" per kind (R3)
    private static void HordeFieldsFor()
    {
        var all = new[]
        {
            "name", "durationSeconds", "location", "action.scope", "action.units", "action.pursuit", "action.growth.addUnits",
            "action.goal.name", "action.hordes.size", "action.roamers.max", "action.marchSpeed", "action.bosses",
            "action.destination", "action.destination.radius", "action.origins.count", "action.hunters", "action.spread.max",
            "action.start", "action.start.minDistance", "action.growth.max", "action.infest.hunters", "action.infest.spread.pool",
            "action.stats.maxHealth", "action.factions",
        };
        string For(bool sw, bool apo, bool inf, bool hm, bool emp) => string.Join(" ", NyarFieldRules.FieldsFor(all, sw, apo, inf, hm, emp));
        Eq(For(false, false, true, false, false),
            "name durationSeconds action.scope action.origins.count action.hunters action.spread.max", "infestation fields");
        Eq(For(false, false, false, true, false),
            "name durationSeconds action.scope action.marchSpeed action.bosses action.destination action.destination.radius action.start action.start.minDistance action.growth.max action.infest.hunters action.infest.spread.pool",
            "horde march fields");
        Eq(For(true, false, false, false, false),
            "name durationSeconds location action.scope action.units action.pursuit action.growth.addUnits action.marchSpeed action.bosses action.destination action.destination.radius",
            "spawn waves fields (a swarm's growth.addUnits stays)");
        Eq(For(false, true, false, false, false),
            "name durationSeconds action.scope action.goal.name action.hordes.size action.roamers.max action.marchSpeed action.bosses", "apocalypse fields");
        Eq(For(false, false, false, false, false).Split(' ').Length, all.Length, "kind unknown → every field");
    }

    // ------------------------------------------------------------------ templates panel
    private static void HordeTemplates()
    {
        var o = NyarMessages.TemplateGroup("spawns", "undead-onslaught");
        foreach (var id in new[] { "zombie-apocalypse", "undead-infestation", "undead-horde-march" })
        {
            var g = NyarMessages.TemplateGroup("spawns", id);
            Eq(g.Heading, "Spawns — Undead hordes", $"{id} → Undead hordes group");
            Check(g.Order > o.Order && g.Order < NyarMessages.TemplateGroup("boss", "x").Order, $"{id}: after Onslaughts, before the next pillar");
            Check(g.Blurb.Contains("one runs at a time"), $"{id}: group blurb");
        }
        Eq(NyarMessages.TemplateGroup("spawns", "march-horde").Heading, "Spawns — Wave events", "a near-miss suffix falls back");
    }

    // ------------------------------------------------------------------ source guards (main menu, generation guard)
    private static void HordeSourceGuards()
    {
        var fixtures = FixturesDir();
        Check(fixtures != null, "source guards: repo found");
        if (fixtures == null) return;
        var ui = Path.Combine(Directory.GetParent(fixtures).Parent.FullName, "Raphael", "UI", "ModContent");
        var files = Directory.GetFiles(ui, "MainPanel.Nyar*.cs");
        Check(files.Length >= 4, $"source guards: Nyar editor files found ({files.Length})");
        string[] rail = { "RefreshAllTabGroupAvailability", "ReconcileRailExpansion", "RequestRebuildMainPanel", "ApplyRailChanges", "RailMemory", "RebuildMainPanel" };
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            string name = Path.GetFileName(f);
            foreach (var r in rail)
            {
                int n = Regex.Matches(text, $@"\b{r}\b").Count;
                // The two diagnostic buttons (Re-probe, Force-enable) in MainPanel.Nyar.cs are the only rail refreshes.
                int allowed = name == "MainPanel.Nyar.cs" && r == "RefreshAllTabGroupAvailability" ? 2 : 0;
                Eq(n, allowed, $"rail guard: {name} calls {r}");
            }
            // R12: every text box's handler checks the editor generation (directly, or through the stepper's Changed).
            foreach (Match m in Regex.Matches(text, @"OnValueChanged \+=[^\n]*"))
                Check(m.Value.Contains("_nyarEdGen") || m.Value.Contains("Changed("), $"generation guard: {name}: {m.Value.Trim()}");
        }
        var hordes = File.ReadAllText(Path.Combine(ui, "MainPanel.NyarHordes.cs"));
        foreach (var bad in new[] { "+=", "OnValueChanged", "onClick", "AddListener" })
            Check(!hordes.Contains(bad), $"R2-5: MainPanel.NyarHordes.cs has no {bad}");
    }
}
