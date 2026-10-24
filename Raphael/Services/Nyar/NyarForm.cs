using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Raphael.Services.Nyar;

// 0.72.0: the event editor's state, kept apart from the Unity controls so it survives every rebuild of the event list
// (defs are re-read after each saved field) and is unit-tested. Read() turns a captured `.nyar event info` reply into
// the values the server holds (PLAN.md §B2 change 7); NyarForm tracks what the admin changed since, what the server
// confirmed (green "saved") and what it refused (change 8). Values are always in `event set` form. A field Read()
// could not take from the reply is in Unread: the form shows an empty control with a "couldn't read" note, never a guess.
// 0.73.0 (PLAN-NYAR014.md): the undead-hordes types (Infestation, HordeMarch) and a destination wave's march line. The
// server describes an invalid definition with one line ("<id> disabled: <reason>", EventAdmin.cs:843): Read marks that
// reply ReasonOnly and Apply keeps the last full load (plus what the server acknowledged since) with NotReady set.

internal enum NyarEventKind { Unknown, UnitsForm, WaveList, Empower, Apocalypse, Infestation, HordeMarch, Other }

/// <summary>What one `event info` reply says about an event.</summary>
internal sealed class NyarFormLoad
{
    public NyarEventKind Kind = NyarEventKind.Unknown;
    public readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);
    public readonly HashSet<string> Unread = new(StringComparer.Ordinal);
    public List<NyarWaveInfo> Waves = new();
    public List<KeyValuePair<string, List<string>>> Pools = new();
    public bool PoolsComplete = true;
    /// <summary>"aroundplayer", "region", "admin", "point" or "" (unread).</summary>
    public string LocationKind = "";
    /// <summary>A growth event shows its modifiers on each generated wave, not in the spawn line.</summary>
    public bool Growth;
    /// <summary>The reply was only "&lt;id&gt; disabled: &lt;reason&gt;" (the server describes an event once it is valid).</summary>
    public bool ReasonOnly;
    /// <summary>The server's reason while the definition is invalid: from a ReasonOnly reply kept over the last full load.</summary>
    public string NotReady = "";
    /// <summary>A horde march's start: "point", "admin", "hotspot" or "" (unread).</summary>
    public string StartKind = "";
    /// <summary>A horde march's size, the sum of its units ("horde of N"); 0 when unread.</summary>
    public int HordeSize;
    /// <summary>What the reply shows that a destination can't be combined with ("pursuit", "fan-out" …); empty when none.</summary>
    public readonly List<string> DestinationConflicts = new();
}

internal sealed class NyarForm
{
    /// <summary>A wave's start rule, one form key for the two fields that make it ("cleared", "after 30",
    /// "afterOrCleared 30"); saved through <see cref="NyarFieldRules.StartRuleSets"/>.</summary>
    public static string StartKey(int wave) => $"action.waveList.{wave}.start";

    public static readonly string[] StatKeys = { "physicalPower", "spellPower", "maxHealth", "attackSpeed", "moveSpeed" };
    public static readonly string[] ModifierKeys = { "maxHealth", "power", "moveSpeed", "attackSpeed" };
    public static readonly string[] UnitsFormKeys = { "action.units", "action.waves", "action.intervalSeconds",
        "action.modifiers.level", "action.modifiers.levelDelta", "action.modifiers.maxHealth", "action.modifiers.power",
        "action.modifiers.moveSpeed", "action.modifiers.attackSpeed" };

    private static readonly Regex Head = new(@"^\S+ ""(.*)"" (?:enabled|disabled) pillar ", RegexOptions.Compiled);
    private static readonly Regex ReasonLine = new(@"^(\S+) disabled: (.+)$", RegexOptions.Compiled);
    private static readonly Regex MarchSpeedRx = new(@", march (\d+(?:\.\d{1,2})?) m/s$", RegexOptions.Compiled);
    private static readonly Regex FanOutRx = new(@"(?:^spawn: |; )fanOut \d+ players ", RegexOptions.Compiled);
    private const string RegionLockLine = "region lock: on";   // EventAdmin.RegionLockLine

    /// <summary>Apocalypse, Infestation and HordeMarch: one runs at a time, and their duration is 600-604800 s.</summary>
    public static bool IsApocalypseFamily(NyarEventKind k) => k is NyarEventKind.Apocalypse or NyarEventKind.Infestation or NyarEventKind.HordeMarch;
    public static bool IsSpawnWaves(NyarEventKind k) => k is NyarEventKind.UnitsForm or NyarEventKind.WaveList;

    public NyarFormLoad Load { get; private set; } = new();
    public bool HaveLoad { get; private set; }
    private readonly Dictionary<string, string> _cur = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();                    // first-change order (Save all keeps form order on ties)
    private readonly HashSet<string> _saved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _refused = new(StringComparer.Ordinal);

    // ================================================================== reading `event info`

    public static NyarFormLoad Read(IReadOnlyList<string> lines)
    {
        var r = new NyarFormLoad();
        if (lines == null || lines.Count == 0) return r;
        var v = r.Values;
        void Put(string field, string value) { if (value == null) r.Unread.Add(field); else v[field] = value; }
        string Find(string prefix) { foreach (var l in lines) if (l.StartsWith(prefix, StringComparison.Ordinal)) return l; return null; }
        string M(string text, string rx) { if (text == null) return null; var m = Regex.Match(text, rx); return m.Success ? m.Groups[1].Value : null; }
        void PutSpread(string block, Dictionary<string, string> spread)
        {
            foreach (var k in new[] { "perKillMin", "perKillMax", "max", "perMinute", "pool" })
                Put(block + k, spread != null && spread.TryGetValue(k, out var s) ? s : null);
        }

        // An invalid definition: the server sends only its reason (an id has no spaces; never a head line).
        if (lines.Count == 1 && !Head.IsMatch(lines[0]) && ReasonLine.Match(lines[0]) is { Success: true } why)
        {
            r.ReasonOnly = true;
            r.NotReady = why.Groups[2].Value;
            return r;
        }

        var h = Head.Match(lines[0]);
        Put("name", h.Success ? h.Groups[1].Value : null);
        Put("durationSeconds", M(lines[0], @" duration (\d+)s$"));
        var cond = Find("conditions: ");
        Put("conditions.minPlayers", M(cond, @"minPlayers (\d+)"));
        Put("conditions.cooldownMinutes", M(cond, @"cooldown (\d+) min"));
        Put("conditions.chancePercent", M(cond, @"chance (\d+)%"));

        var apo = Find("action: apocalypse");
        if (apo != null)
        {
            r.Kind = NyarEventKind.Apocalypse;
            Put("action.marchSpeed", M(apo, @", march (\d+(?:\.\d{1,2})?) m/s$"));
            var goal = Find("goal: ");
            Put("action.goal.name", M(goal, @"^goal: (.+), radius \d+, at "));
            Put("action.goal.radius", M(goal, @", radius (\d+), at "));
            var hordes = Find("hordes: ");
            Put("action.hordes.count", M(hordes, @"^hordes: (\d+),"));
            Put("action.hordes.everyMinutes", M(hordes, @", every (\d+) min,"));
            Put("action.hordes.size", M(hordes, @", size (\d+) "));
            Put("action.roamers.max", M(Find("roamers: "), @", max (\d+),"));
            Put("action.bosses", Bosses(NyarInfoParse.FinalBosses(lines)));
            r.Pools = NyarInfoParse.Pools(lines, out r.PoolsComplete);
            return r;
        }

        if (Find("action: infestation of the ") != null)
        {
            r.Kind = NyarEventKind.Infestation;
            var o = NyarInfoParse.Origins(Find("origins: "));
            Put("action.origins.count", o?[0]); Put("action.origins.minNatives", o?[1]);
            Put("action.origins.minSpacing", o?[2]); Put("action.origins.perOrigin", o?[3]);
            var hu = NyarInfoParse.Hunters(Find("hunters: "));
            Put("action.hunters", hu?[0]); Put("action.driftRadius", hu?[1]);
            PutSpread("action.spread.", NyarInfoParse.Spread(After(Find("spread: "), "spread: ")));
            r.Pools = NyarInfoParse.Pools(lines, out r.PoolsComplete);
            return r;
        }

        var march = Find("action: horde march of the ");
        if (march != null)
        {
            r.Kind = NyarEventKind.HordeMarch;
            var sp = MarchSpeedRx.Match(march);
            Put("action.marchSpeed", sp.Success ? sp.Groups[1].Value : null);
            r.StartKind = NyarInfoParse.Start(Find("start: "), out var minDistance) ?? "";
            if (r.StartKind == "hotspot") v["action.start.minDistance"] = minDistance;
            else if (r.StartKind == "") r.Unread.Add("action.start.minDistance");
            v["action.destination"] = "here";   // a horde march always has one
            var dest = NyarInfoParse.Destination(Find("destination: "));
            Put("action.destination.name", dest?[0]); Put("action.destination.radius", dest?[1]);
            PutSpread("action.growth.", NyarInfoParse.Spread(After(Find("growth: "), "growth: ")));
            var inf = NyarInfoParse.Infest(Find("infest: "));
            Put("action.infest.hunters", inf?[0]); Put("action.infest.driftRadius", inf?[1]);
            PutSpread("action.infest.spread.", NyarInfoParse.Spread(inf?[2]));
            Put("action.bosses", Bosses(NyarInfoParse.FinalBosses(lines)));
            r.HordeSize = NyarInfoParse.HordeSize(lines);
            r.Pools = NyarInfoParse.Pools(lines, out r.PoolsComplete);
            return r;
        }

        if (NyarInfoParse.Empower(lines, out var factions, out var stats))
        {
            r.Kind = NyarEventKind.Empower;
            Put("action.factions", factions.Length > 0 ? factions : null);
            foreach (var k in StatKeys) v["action.stats." + k] = stats.TryGetValue(k, out var s) ? s : "1.0";   // only raised stats are listed
            return r;
        }

        bool waveList = NyarInfoParse.IsWaveList(lines);
        string action = null;
        foreach (var l in lines)
            if (l.StartsWith("action: ", StringComparison.Ordinal) && Regex.IsMatch(l, @"^action: \d+ waves")) { action = l; break; }
        if (action == null && !waveList) { r.Kind = NyarEventKind.Other; return r; }
        r.Kind = waveList ? NyarEventKind.WaveList : NyarEventKind.UnitsForm;
        r.Growth = Find("growth: ") != null;

        Put("action.radius", M(action, @", radius (\d+), "));
        r.LocationKind = NyarInfoParse.Location(lines, out var near, out var far);
        if (r.LocationKind == "aroundplayer") v["location"] = $"aroundplayer {near} {far}";
        else if (r.LocationKind == "region") v["location"] = "region";
        else if (r.LocationKind == "") r.Unread.Add("location");
        Put("action.behaviour", NyarInfoParse.Behaviour(lines) is { Length: > 0 } b ? b : null);
        v["action.loot"] = NyarInfoParse.SpawnFlag(lines, "loot on") ? "true" : "false";
        v["action.allowTerritory"] = NyarInfoParse.SpawnFlag(lines, "claimed territory allowed") ? "true" : "false";
        int pursuit = NyarInfoParse.Pursuit(lines);
        v["action.pursuit"] = pursuit > 0 ? Inv(pursuit) : "none";
        Put("action.bosses", Bosses(NyarInfoParse.FinalBosses(lines)));
        r.Pools = NyarInfoParse.Pools(lines, out r.PoolsComplete);

        // A destination wave (0.14.0): "march: from … to <name> (…) at <speed> m/s". Its radius isn't printed.
        var marchLine = Find("march: ");
        if (marchLine == null) v["action.destination"] = "none";
        else
        {
            v["action.destination"] = "here";
            var mv = NyarInfoParse.March(marchLine);
            Put("action.destination.name", mv?[0]); Put("action.marchSpeed", mv?[1]);
            r.Unread.Add("action.destination.radius");
        }
        // What a destination can't be combined with, as far as the reply shows it.
        if (r.LocationKind is "aroundplayer" or "region") r.DestinationConflicts.Add("location");
        if (pursuit > 0) r.DestinationConflicts.Add("pursuit");
        if (Find(RegionLockLine) != null) r.DestinationConflicts.Add("region lock");
        if (Find("arena: ") != null) r.DestinationConflicts.Add("arena");
        foreach (var l in lines) if (FanOutRx.IsMatch(l)) { r.DestinationConflicts.Add("fan-out"); break; }

        if (waveList)
        {
            r.Waves = NyarInfoParse.Waves(lines);
            foreach (var w in r.Waves)
            {
                Put($"action.waveList.{w.N}.units", w.Units);
                Put($"action.waveList.{w.N}.bosses", Bosses(w.Bosses));
                if (w.N > 1) v[StartKey(w.N)] = w.AfterSeconds > 0 ? $"{(w.WhenCleared ? "afterOrCleared" : "after")} {w.AfterSeconds}" : "cleared";
            }
        }
        else
        {
            Put("action.waves", M(action, @"^action: (\d+) waves? every "));
            Put("action.intervalSeconds", M(action, @" every (\d+)s, "));
            var units = NyarInfoParse.UnitsFormUnits(lines);
            Put("action.units", string.IsNullOrEmpty(units) ? null : units);
            if (r.Growth)
                foreach (var k in UnitsFormKeys) { if (k.StartsWith("action.modifiers.", StringComparison.Ordinal)) r.Unread.Add(k); }
            else
            {
                var mods = NyarInfoParse.Modifiers(lines);
                v["action.modifiers.level"] = mods.TryGetValue("level", out var lv) ? lv : "none";
                v["action.modifiers.levelDelta"] = mods.TryGetValue("levelDelta", out var ld) ? ld : "none";
                foreach (var k in ModifierKeys) v["action.modifiers." + k] = mods.TryGetValue(k, out var m) ? m : "none";
            }
        }
        return r;
    }

    // "" (no bosses) → "none", the value that clears them; null stays unread.
    private static string Bosses(string setForm) => setForm == null ? null : setForm.Length == 0 ? "none" : setForm;

    // The text after a line's head, or null.
    private static string After(string line, string head) => line == null ? null : line.Substring(head.Length);

    // ================================================================== editing state

    /// <summary>Take a fresh reply. A field the admin changed keeps its change unless the server now holds that very
    /// value; an unchanged one follows the server. Saved / refused marks are cleared (the reply is the new truth).
    /// A ReasonOnly reply keeps the last full load (with every value acknowledged since) and only sets NotReady; with no
    /// full load yet, the form has the reason alone.</summary>
    public void Apply(NyarFormLoad load)
    {
        if (load != null && load.ReasonOnly && Load.Kind != NyarEventKind.Unknown)
        {
            Load.NotReady = load.NotReady;
            _saved.Clear(); _refused.Clear();
            return;
        }
        var old = Load;
        Load = load ?? new NyarFormLoad();
        HaveLoad = load != null && (load.Kind != NyarEventKind.Unknown || load.ReasonOnly);
        foreach (var k in new List<string>(_cur.Keys))
        {
            string was = old.Values.TryGetValue(k, out var o) ? o : "";
            string now = Load.Values.TryGetValue(k, out var n) ? n : "";
            if (_cur[k] == now || (_cur[k] == was && !Load.Unread.Contains(k))) { _cur.Remove(k); _order.Remove(k); }
            // Converted to a per-wave list: the shared units / waves / interval / modifiers are gone from the event.
            else if (Load.Kind == NyarEventKind.WaveList && Array.IndexOf(UnitsFormKeys, k) >= 0) { _cur.Remove(k); _order.Remove(k); }
        }
        _saved.Clear(); _refused.Clear();
    }

    public void Clear() { Load = new NyarFormLoad(); HaveLoad = false; _cur.Clear(); _order.Clear(); _saved.Clear(); _refused.Clear(); }

    public string Loaded(string field) => Load.Values.TryGetValue(field, out var v) ? v : "";
    public bool IsUnread(string field) => Load.Unread.Contains(field) && !_cur.ContainsKey(field);

    /// <summary>The value the form shows: the admin's change, else the server's.</summary>
    public string Get(string field) => _cur.TryGetValue(field, out var c) ? c : Loaded(field);

    public void Set(string field, string value)
    {
        value ??= "";
        _saved.Remove(field); _refused.Remove(field);
        if (value == Loaded(field) && !Load.Unread.Contains(field)) { _cur.Remove(field); _order.Remove(field); return; }
        if (!_cur.ContainsKey(field)) _order.Add(field);
        _cur[field] = value;
    }

    public bool IsChanged(string field) => _cur.TryGetValue(field, out var c) && c.Trim().Length > 0 && c != Loaded(field);
    public bool IsSaved(string field) => _saved.Contains(field);
    public string Refused(string field) => _refused.TryGetValue(field, out var m) ? m : "";

    /// <summary>Changed fields in the order they were first changed (Save all re-orders them for the server).</summary>
    public List<KeyValuePair<string, string>> Changes(Func<string, bool> include = null)
    {
        var l = new List<KeyValuePair<string, string>>();
        foreach (var f in _order)
            if (IsChanged(f) && (include == null || include(f))) l.Add(new(f, _cur[f]));
        return l;
    }

    public int ChangeCount(Func<string, bool> include = null) => Changes(include).Count;

    /// <summary>The server confirmed <paramref name="field"/>: its value is now the loaded one.</summary>
    public void MarkSaved(string field)
    {
        if (_cur.TryGetValue(field, out var c)) { Load.Values[field] = c; Load.Unread.Remove(field); _cur.Remove(field); _order.Remove(field); SideEffects(field, c); }
        _refused.Remove(field);
        _saved.Add(field);
    }

    /// <summary>An immediate set (Destination here, Start here …) the server confirmed: its value is now the loaded one,
    /// so it survives a ReasonOnly reply.</summary>
    public void Acknowledge(string field, string value)
    {
        value ??= "";
        Load.Values[field] = value;
        Load.Unread.Remove(field);
        if (_cur.TryGetValue(field, out var c) && c == value) { _cur.Remove(field); _order.Remove(field); }
        SideEffects(field, value);
    }

    // What a confirmed set also changes on the server: no destination drops its name, radius and march speed; a start
    // here replaces a hotspot start, and a hotspot distance replaces a point start.
    private void SideEffects(string field, string value)
    {
        if (field == "action.destination" && value == "none")
        {
            foreach (var k in new[] { "action.destination.name", "action.destination.radius", "action.marchSpeed" })
            { Load.Values.Remove(k); Load.Unread.Remove(k); }
        }
        else if (field == "action.start" && value == "here")
        {
            Load.StartKind = "point";
            Load.Values.Remove("action.start.minDistance"); Load.Unread.Remove("action.start.minDistance");
        }
        else if (field == "action.start.minDistance") Load.StartKind = "hotspot";
    }

    public void MarkRefused(string field, string message) { _saved.Remove(field); _refused[field] = message ?? ""; }

    /// <summary>Undo every change (or those <paramref name="include"/> picks).</summary>
    public void Undo(Func<string, bool> include = null)
    {
        foreach (var f in new List<string>(_cur.Keys))
            if (include == null || include(f)) { _cur.Remove(f); _order.Remove(f); _refused.Remove(f); }
    }

    /// <summary>The `event set` steps for a form key: a start-rule key becomes its two sets, every other key itself.</summary>
    public static List<KeyValuePair<string, string>> SetsFor(string field, string value)
    {
        var m = Regex.Match(field, @"^action\.waveList\.(\d+)\.start$");
        if (!m.Success) return new List<KeyValuePair<string, string>> { new(field, value) };
        int wave = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var p = (value ?? "").Split(' ');
        int secs = p.Length > 1 && int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : 0;
        return NyarFieldRules.StartRuleSets(wave, p[0], secs);
    }

    /// <summary>A start-rule value's check: "after" kinds need 10-3600 s.</summary>
    public static string CheckStart(string value)
    {
        var p = (value ?? "").Split(' ');
        if (p[0] == "cleared" && p.Length == 1) return null;
        if ((p[0] == "after" || p[0] == "afterOrCleared") && p.Length == 2)
            return NyarFieldRules.CheckValue(NyarFieldRules.Spec("action.waveList.N.afterSeconds"), p[1] == "none" ? "" : p[1]);
        return "Pick when this wave starts.";
    }

    private static string Inv(int n) => n.ToString(CultureInfo.InvariantCulture);
}
