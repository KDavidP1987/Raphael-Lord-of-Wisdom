using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Raphael.Services.Nyar;

// 0.72.0: client-side mirror of Nyarlathotep's `event set` value rules (0.13.0-rc.3, still api 8), copied from the
// server's Logic/CommandArgs.cs (SettableValue, SpawnKeyValue, WaveListValue, Units, WaveUnits, BossList, PoolValue,
// PursuitValue, ApocalypseValue, Stat, Decimal2) at commit 4f059f5. It is the same per-value parse, no stricter and no
// looser, and the editor only uses it to say exactly what to fix before anything is sent; the server still checks
// every value and its answer wins. Cross-field rules the client can't see (does a pool exist, the allied-group mix,
// pursuit without Hunt, wave gaps) stay the server's. Pure: no Unity types, unit-tested in tests/Raphael.Logic.Tests.

/// <summary>How a field's value is entered in the form.</summary>
internal enum NyarKind { Int, Dec2, Text }

/// <summary>One field's range and presentation: min / max / step for the stepper, a unit word, whether "none" (remove
/// the key) is allowed, and a few quick-pick presets.</summary>
internal sealed class NyarSpec
{
    public string Field, Label, Unit = "";
    public NyarKind Kind;
    public decimal Min, Max, Step = 1;
    public bool NoneAllowed, Signed;
    public decimal[] Presets = Array.Empty<decimal>();
    public int MaxLength;            // Text only
    public string NoneLabel = "none";

    /// <summary>"10-600 s", "0.5-3.0 ×", "-5 to +5".</summary>
    public string RangeText => Kind == NyarKind.Text
        ? $"1-{MaxLength} characters"
        : Signed ? $"{NyarFieldRules.Fmt(this, Min)} to {NyarFieldRules.Fmt(this, Max)}{UnitSuffix}"
        : $"{NyarFieldRules.Fmt(this, Min)}-{NyarFieldRules.Fmt(this, Max)}{UnitSuffix}";
    private string UnitSuffix => Unit.Length == 0 ? "" : (Unit == "×" ? " ×" : " " + Unit);
}

/// <summary>One entry of a units or bosses list: a fixed unit with a count, a pool entry (units) or a roll (bosses).</summary>
internal sealed class NyarEntry
{
    public string Unit = "";     // CHAR_… (fixed)
    public string Pool = "";     // pool name (pool entry / roll)
    public int Count = 1;        // fixed count, or a units pool entry's count
    public int Min = 1, Max = 1; // a boss roll's range
    public bool IsPool => Pool.Length > 0;

    public NyarEntry Clone() => (NyarEntry)MemberwiseClone();
}

internal static class NyarFieldRules
{
    public const int MinPursuit = 15, MaxPursuit = 600;
    public const int MaxPools = 8, MaxPoolMembers = 30, MaxPoolName = 24;
    public const int MaxUnitEntries = 10, MaxUnitCount = 50, MaxWaveUnits = 50;
    public const int MaxBossEntries = 3, MaxBossCount = 3, MaxRollBosses = 5, MaxWaveBosses = 5;
    public const int MaxNameLength = 96, MaxListChars = 4000;
    public const int MaxWave = 10, MaxFactions = 5;
    public const int MinHuntRange = 10, MaxHuntRange = 60;
    public const int MinNear = 10, MaxNear = 60, MinFar = 15, MaxFar = 80;
    /// <summary>Longest whole `.nyar api event set …` command Raphael sends (chat lines are cut near 512 bytes).</summary>
    public const int MaxCommandBytes = 450;

    // ================================================================== field specs

    private static NyarSpec I(string f, string label, int min, int max, int step = 1, string unit = "", bool none = false, params decimal[] presets)
        => new() { Field = f, Label = label, Kind = NyarKind.Int, Min = min, Max = max, Step = step, Unit = unit, NoneAllowed = none, Presets = presets };
    private static NyarSpec D(string f, string label, decimal min, decimal max, bool none, params decimal[] presets)
        => new() { Field = f, Label = label, Kind = NyarKind.Dec2, Min = min, Max = max, Step = 0.1m, Unit = "×", NoneAllowed = none, Presets = presets };
    private static NyarSpec T(string f, string label, int maxLen) => new() { Field = f, Label = label, Kind = NyarKind.Text, MaxLength = maxLen };

    private static readonly string[] ModifierKeys = { "maxHealth", "power", "moveSpeed", "attackSpeed" };
    private static readonly string[] StatKeys = { "physicalPower", "spellPower", "maxHealth", "attackSpeed", "moveSpeed" };

    /// <summary>The spec of <paramref name="field"/> (a wave / entry number in it may be a real number or N / M), or
    /// null for a field that isn't a single number or text (lists, choices, toggles).</summary>
    public static NyarSpec Spec(string field, bool apocalypse = false)
    {
        string f = Generic(field ?? "");
        switch (f)
        {
            case "name": return T(field, "Name", 40);
            case "durationSeconds":
                return apocalypse
                    ? I(field, "Duration", 600, 604800, 600, "s", false, 3600, 21600, 86400, 604800)
                    : I(field, "Duration", 30, 7200, 30, "s", false, 300, 600, 900, 1800);
            case "conditions.minPlayers": return I(field, "Min players online", 0, 100, 1, "", false, 0, 1, 2, 5);
            case "conditions.cooldownMinutes": return I(field, "Cooldown", 0, 10080, 5, "min", false, 0, 30, 60, 1440);
            case "conditions.chancePercent": return I(field, "Chance", 1, 100, 5, "%", false, 25, 50, 100);
            case "action.waves": return I(field, "Waves", 1, 10);
            case "action.intervalSeconds": return I(field, "Seconds between waves", 10, 600, 5, "s", false, 30, 60, 120);
            case "action.radius": return I(field, "Spawn radius", 2, 30, 1, "m", false, 5, 10, 20);
            case "action.pursuit": return Named(I(field, "Pursuit", MinPursuit, MaxPursuit, 15, "s", true, 30, 60, 120, 300), "no pursuit");
            case "action.modifiers.level": case "action.waveList.N.units.M.level":
                return Named(I(field, "Level", 1, 120, 1, "", true, 30, 60, 80), "units' own");
            case "action.modifiers.levelDelta": case "action.waveList.N.units.M.levelDelta":
                var ld = I(field, "Level shift", -5, 5, 1, "", true, -2, 2, 5); ld.Signed = true; return Named(ld, "no shift");
            case "action.waveList.N.afterSeconds": return Named(I(field, "Starts after", 10, 3600, 5, "s", true, 30, 60, 120), "no timer");
            case "action.goal.radius": return I(field, "Goal radius", 10, 100, 5, "m", false, 20, 50);
            case "action.goal.name": return T(field, "Goal name", 32);
            case "action.hordes.count": return I(field, "Hordes", 1, 10);
            case "action.hordes.everyMinutes": return I(field, "Horde every", 5, 1440, 5, "min", false, 15, 30, 60);
            case "action.hordes.size": return I(field, "Horde size", 5, 50, 1, "", false, 10, 20, 30);
            case "action.roamers.max": return I(field, "Max roamers", 1, 100, 1, "", false, 10, 25, 50);
            case "action.marchSpeed":
                var ms = D(field, "March speed", 0.5m, 5.0m, false, 1, 2, 3); ms.Unit = "m/s"; return ms;
            case "behaviour.range": return I(field, "Hunt range", MinHuntRange, MaxHuntRange, 5, "m", false, 20, 30, 60);
            case "location.near": return I(field, "Nearest", MinNear, MaxNear, 5, "m", false, 20, 30);
            case "location.far": return I(field, "Farthest", MinFar, MaxFar, 5, "m", false, 30, 40, 60);
        }
        foreach (var k in ModifierKeys)
            if (f == "action.modifiers." + k || f == "action.waveList.N.units.M." + k)
                return Named(D(field, ModifierLabel(k), 0.5m, 3.0m, true, 1, 1.5m, 2), "×1 (off)");
        foreach (var k in StatKeys)
            if (f == "action.stats." + k)
                return D(field, StatLabel(k), 1.0m, 3.0m, false, 1.25m, 1.5m, 2);
        return null;
    }

    private static NyarSpec Named(NyarSpec s, string noneLabel) { s.NoneLabel = noneLabel; return s; }

    private static string ModifierLabel(string k) => k switch
    {
        "maxHealth" => "Health ×", "power" => "Power ×", "moveSpeed" => "Move speed ×", _ => "Attack speed ×",
    };
    private static string StatLabel(string k) => k switch
    {
        "physicalPower" => "Physical power ×", "spellPower" => "Spell power ×", "maxHealth" => "Max health ×",
        "attackSpeed" => "Attack speed ×", _ => "Move speed ×",
    };

    /// <summary>"action.waveList.3.units.2.level" → "action.waveList.N.units.M.level".</summary>
    public static string Generic(string field)
    {
        var p = field.Split('.');
        if (p.Length >= 3 && p[0] == "action" && p[1] == "waveList")
        {
            if (IsDigits(p[2])) p[2] = "N";
            if (p.Length >= 5 && p[3] == "units" && IsDigits(p[4])) p[4] = "M";
        }
        return string.Join(".", p);
    }

    // ================================================================== numbers and text

    /// <summary>The value as the server would read it, or null with <paramref name="why"/> set.</summary>
    public static string CheckValue(NyarSpec s, string value)
    {
        var v = (value ?? "").Trim();
        if (s == null) return null;
        if (v.Length == 0) return s.NoneAllowed ? $"Pick a value ({s.RangeText}) or press {Cap(s.NoneLabel)}." : $"Type a value: {s.RangeText}.";
        if (s.NoneAllowed && v == "none") return null;
        switch (s.Kind)
        {
            case NyarKind.Text:
                if (v.Length > s.MaxLength) return $"{s.Label} is {v.Length} characters; the most is {s.MaxLength}.";
                foreach (var ch in v)
                    if (ch is '<' or '>' || char.IsControl(ch)) return $"{s.Label} can't contain < or >.";
                return null;
            case NyarKind.Int:
                if (!TryInt(s, v, out var n)) return $"{s.Label} must be a whole number, {s.RangeText}.";
                return n < s.Min || n > s.Max ? $"{s.Label} must be {s.RangeText} ({v} is outside)." : null;
            default:
                if (!TryDec2(v, out var d, stat: s.Field.StartsWith("action.stats.", StringComparison.Ordinal)))
                    return $"{s.Label} must be a number like 1.5 (at most two decimals), {s.RangeText}.";
                return d < s.Min || d > s.Max ? $"{s.Label} must be {s.RangeText} ({v} is outside)." : null;
        }
    }

    private static bool TryInt(NyarSpec s, string v, out int n)
    {
        n = 0;
        if (s.Signed)   // levelDelta: AllowLeadingSign, at most 2 characters ("+5", "-5", "5")
            return v.Length <= 2 && int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n);
        return int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out n);
    }

    // CommandArgs.Decimal2 (modifiers, march speed) and CommandArgs.Stat (empower stats: one or two digits after a dot).
    private static bool TryDec2(string v, out decimal d, bool stat)
    {
        d = 0;
        if (v.Length == 0 || v.Length > 6 || v[0] == '.' || v[v.Length - 1] == '.') return false;
        int dot = v.IndexOf('.');
        if (stat && dot > 0 && v.Length - dot - 1 is < 1 or > 2) return false;
        if (!decimal.TryParse(v, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out d)) return false;
        return decimal.Round(d, 2) == d;
    }

    /// <summary>A number value read back for a stepper; false for none / empty / unreadable.</summary>
    public static bool TryRead(NyarSpec s, string value, out decimal d)
    {
        d = 0;
        var v = (value ?? "").Trim();
        if (s == null || v.Length == 0 || v == "none") return false;
        if (s.Kind == NyarKind.Int) { if (!TryInt(s, v, out var n)) return false; d = n; return true; }
        return decimal.TryParse(v, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out d);
    }

    /// <summary>One stepper press: the next value in <paramref name="dir"/> (+1 / -1), snapped to the step and kept in range.
    /// An empty or unreadable value starts from the range's low end (or 1.0 for a multiplier).</summary>
    public static string Step(NyarSpec s, string value, int dir)
    {
        if (s == null || s.Kind == NyarKind.Text) return value;
        decimal cur;
        if (!TryRead(s, value, out cur)) cur = s.Kind == NyarKind.Dec2 && s.Min <= 1 && s.Max >= 1 ? 1m : (dir > 0 ? s.Min - s.Step : s.Min);
        decimal next = dir > 0 ? Math.Floor(cur / s.Step) * s.Step + s.Step : Math.Ceiling(cur / s.Step) * s.Step - s.Step;
        next = Math.Max(s.Min, Math.Min(s.Max, next));
        return Fmt(s, next);
    }

    /// <summary>A number as the server takes it: "40", "1.5", "+3" for a level shift.</summary>
    public static string Fmt(NyarSpec s, decimal d)
    {
        if (s.Kind == NyarKind.Int)
        {
            var n = (int)decimal.Round(d);
            return s.Signed && n > 0 ? "+" + n.ToString(CultureInfo.InvariantCulture) : n.ToString(CultureInfo.InvariantCulture);
        }
        return d.ToString("0.0#", CultureInfo.InvariantCulture);
    }

    /// <summary>A readable echo for a time value: "900 s = 15 min", "1440 min = 1 day"; "" when not a time.</summary>
    public static string Echo(NyarSpec s, string value)
    {
        if (s == null || !TryRead(s, value, out var d)) return "";
        long n = (long)d;
        if (s.Unit == "s" && n >= 60) return $"= {Human(n)}";
        if (s.Unit == "min" && n >= 60) return $"= {Human(n * 60)}";
        return "";
    }

    public static string Human(long secs)
    {
        if (secs < 60) return $"{secs} s";
        if (secs < 3600) return secs % 60 == 0 ? $"{secs / 60} min" : $"{secs / 60} min {secs % 60} s";
        if (secs < 86400) return secs % 3600 == 0 ? $"{secs / 3600} h" : $"{secs / 3600} h {secs % 3600 / 60} min";
        long days = secs / 86400, rest = secs % 86400;
        return (days == 1 ? "1 day" : $"{days} days") + (rest >= 3600 ? $" {rest / 3600} h" : "");
    }

    // ================================================================== single tokens

    /// <summary>A unit prefab name the server takes: CHAR_ then letters, digits or '_', at most 96 characters.</summary>
    public static bool IsUnitName(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= 5 || s.Length > MaxNameLength || !s.StartsWith("CHAR_", StringComparison.Ordinal)) return false;
        foreach (var ch in s)
            if (!(ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) return false;
        return true;
    }

    /// <summary>A faction name the server takes: Faction_ then letters, digits or '_'.</summary>
    public static bool IsFactionName(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= 8 || s.Length > MaxNameLength || !s.StartsWith("Faction_", StringComparison.Ordinal)) return false;
        foreach (var ch in s)
            if (!(ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) return false;
        return true;
    }

    /// <summary>A pool name: 1-24 of a-z, 0-9 and '-'.</summary>
    public static bool IsPoolName(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > MaxPoolName) return false;
        foreach (var ch in s)
            if (!(ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) return false;
        return true;
    }

    /// <summary>A typed pool name made valid where that is obvious: trimmed, lower case, spaces / underscores → '-',
    /// other characters dropped, cut to 24.</summary>
    public static string NormalizePoolName(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch0 in (s ?? "").Trim().ToLowerInvariant())
        {
            char ch = ch0 is ' ' or '_' ? '-' : ch0;
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') sb.Append(ch);
        }
        var r = sb.ToString();
        return r.Length > MaxPoolName ? r.Substring(0, MaxPoolName) : r;
    }

    // CommandArgs.WaveListPath: one or two digits, no leading zero, 1-10.
    public static bool IsWave(int n) => n >= 1 && n <= MaxWave;

    // ================================================================== lists

    /// <summary>The server's CommaList: null (with a sentence) if empty, too long, a bad character, too many parts or an
    /// empty part.</summary>
    public static string[] SplitList(string value, int maxParts, out string why)
    {
        why = null;
        var v = (value ?? "").Trim();
        if (v.Length == 0) { why = "The list is empty: add an entry first."; return null; }
        if (v.Length > MaxListChars) { why = $"The list is {v.Length} characters; the most is {MaxListChars}."; return null; }
        foreach (var ch in v)
            if (ch <= ' ' || ch > '~' || ch is '"' or '\'' or '{' or '}' or '[' or ']' or '<' or '>' or '\\')
            {
                why = ch == ' ' ? "Remove the spaces: entries are separated by commas only." : $"'{ch}' isn't allowed in a list.";
                return null;
            }
        var parts = v.Split(',');
        if (parts.Length > maxParts) { why = $"At most {maxParts} entries here ({parts.Length} now)."; return null; }
        foreach (var p in parts) if (p.Length == 0) { why = "There's an empty entry (two commas, or a comma at an end)."; return null; }
        return parts;
    }

    /// <summary>A units list (action.units, or action.waveList.N.units when <paramref name="waveList"/>): 1-10 entries
    /// CHAR_name[:count] or pool:&lt;name&gt;:&lt;count&gt;, count 1-50 (default 1). action.units takes each name or pool
    /// once; a wave's own list allows repeats. With any pool entry, at most 50 units in all.</summary>
    public static string CheckUnitList(string value, bool waveList)
    {
        var parts = SplitList(value, MaxUnitEntries, out var why);
        if (parts == null) return why;
        var entries = new List<NyarEntry>();
        foreach (var p in parts)
        {
            if (p.StartsWith("pool:", StringComparison.Ordinal))
            {
                var bits = p.Split(':');
                if (bits.Length != 3) return $"'{p}' should look like pool:name:count.";
                if (!IsPoolName(bits[1])) return $"'{bits[1]}' isn't a pool name (a-z, 0-9 and -, up to 24).";
                if (!Count(bits[2], 1, MaxUnitCount, out var pc)) return $"'{p}': the count must be 1-{MaxUnitCount}.";
                entries.Add(new NyarEntry { Pool = bits[1], Count = pc });
                continue;
            }
            int colon = p.IndexOf(':');
            string name = colon < 0 ? p : p.Substring(0, colon);
            int count = 1;
            if (colon >= 0 && !Count(p.Substring(colon + 1), 1, MaxUnitCount, out count)) return $"'{p}': the count must be 1-{MaxUnitCount}.";
            if (!IsUnitName(name)) return $"'{name}' isn't a unit name (CHAR_…).";
            entries.Add(new NyarEntry { Unit = name, Count = count });
        }
        if (!waveList)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in entries)
                if (!seen.Add(e.IsPool ? "pool:" + e.Pool : e.Unit))
                    return $"{(e.IsPool ? "Pool " + e.Pool : e.Unit)} is in the list twice: raise its count instead.";
        }
        if (PastWaveSize(entries)) return $"A list with a pool entry brings at most {MaxWaveUnits} units ({TotalUnits(entries)} now).";
        return null;
    }

    public static bool PastWaveSize(IReadOnlyList<NyarEntry> entries)
    {
        bool pool = false;
        foreach (var e in entries) if (e.IsPool) pool = true;
        return pool && TotalUnits(entries) > MaxWaveUnits;
    }

    public static int TotalUnits(IReadOnlyList<NyarEntry> entries)
    {
        int n = 0;
        foreach (var e in entries) n += e.Count;
        return n;
    }

    /// <summary>A bosses list (action.bosses, or action.waveList.N.bosses when <paramref name="waveList"/>): "none", or
    /// 1-3 entries, fixed CHAR_name[:1-3] or roll pool:&lt;name&gt;:&lt;min&gt;-&lt;max&gt; (1 ≤ min ≤ max ≤ 5); with a
    /// roll, or on a wave's own list, at most 5 bosses in all (fixed counts plus roll maxes).</summary>
    public static string CheckBossList(string value, bool waveList)
    {
        if ((value ?? "").Trim() == "none") return null;
        var parts = SplitList(value, MaxBossEntries, out var why);
        if (parts == null) return why;
        int most = 0; bool roll = false;
        foreach (var p in parts)
        {
            if (p.StartsWith("pool:", StringComparison.Ordinal))
            {
                var bits = p.Split(':');
                var range = bits.Length == 3 ? bits[2].Split('-') : Array.Empty<string>();
                if (range.Length != 2 || !int.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out var min)
                    || !int.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out var max))
                    return $"'{p}' should look like pool:name:min-max.";
                if (!IsPoolName(bits[1])) return $"'{bits[1]}' isn't a pool name (a-z, 0-9 and -, up to 24).";
                if (min < 1 || min > MaxRollBosses) return $"'{p}': the fewest bosses must be 1-{MaxRollBosses}.";
                if (max < min || max > MaxRollBosses) return $"'{p}': the most bosses must be {min}-{MaxRollBosses}.";
                most += max; roll = true;
                continue;
            }
            int colon = p.IndexOf(':');
            string name = colon < 0 ? p : p.Substring(0, colon);
            int count = 1;
            if (colon >= 0 && !Count(p.Substring(colon + 1), 1, MaxBossCount, out count)) return $"'{p}': a boss count must be 1-{MaxBossCount}.";
            if (!IsUnitName(name)) return $"'{name}' isn't a unit name (CHAR_…).";
            most += count;
        }
        if ((roll || waveList) && most > MaxWaveBosses) return $"At most {MaxWaveBosses} bosses in all here ({most} now).";
        return null;
    }

    /// <summary>Whether the 5-bosses total applies to this list (a wave's own list, or one holding a roll).</summary>
    public static bool BossTotalApplies(IReadOnlyList<NyarEntry> bosses, bool waveList)
    {
        if (waveList) return true;
        foreach (var b in bosses) if (b.IsPool) return true;
        return false;
    }

    /// <summary>The most bosses a list can bring (fixed counts plus roll maxes).</summary>
    public static int MostBosses(IReadOnlyList<NyarEntry> bosses)
    {
        int n = 0;
        foreach (var b in bosses) n += b.IsPool ? b.Max : b.Count;
        return n;
    }

    /// <summary>Factions for an empowerment: 1-5 distinct Faction_ names.</summary>
    public static string CheckFactions(string value)
    {
        var parts = SplitList(value, MaxFactions, out var why);
        if (parts == null) return why;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in parts)
        {
            if (!IsFactionName(p)) return $"'{p}' isn't a faction name (Faction_…).";
            if (!seen.Add(p)) return $"{p} is in the list twice.";
        }
        return null;
    }

    // ---- list rows ⇄ value text (the form edits rows; the value box is rebuilt from them) ----

    /// <summary>Rows of a units or bosses value; null when the text can't be read as entries (then the form keeps the
    /// raw text and says so). "none" and "" are an empty list.</summary>
    public static List<NyarEntry> ParseEntries(string value, bool bosses)
    {
        var list = new List<NyarEntry>();
        var v = (value ?? "").Trim();
        if (v.Length == 0 || v == "none") return list;
        foreach (var p in v.Split(','))
        {
            if (p.Length == 0) return null;
            if (p.StartsWith("pool:", StringComparison.Ordinal))
            {
                var bits = p.Split(':');
                if (bits.Length != 3 || bits[1].Length == 0) return null;
                if (bosses)
                {
                    var r = bits[2].Split('-');
                    if (r.Length != 2 || !int.TryParse(r[0], NumberStyles.None, CultureInfo.InvariantCulture, out var mn)
                        || !int.TryParse(r[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mx)) return null;
                    list.Add(new NyarEntry { Pool = bits[1], Min = mn, Max = mx });
                }
                else
                {
                    if (!int.TryParse(bits[2], NumberStyles.None, CultureInfo.InvariantCulture, out var c)) return null;
                    list.Add(new NyarEntry { Pool = bits[1], Count = c });
                }
                continue;
            }
            int colon = p.IndexOf(':');
            string name = colon < 0 ? p : p.Substring(0, colon);
            int count = 1;
            if (colon >= 0 && !int.TryParse(p.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out count)) return null;
            if (name.Length == 0) return null;
            list.Add(new NyarEntry { Unit = name, Count = count });
        }
        return list;
    }

    /// <summary>The value text for rows: "CHAR_a:4,pool:troops:6" / "CHAR_b:1,pool:elite:1-2"; "" for no rows.</summary>
    public static string BuildEntries(IReadOnlyList<NyarEntry> rows, bool bosses)
    {
        var sb = new StringBuilder();
        foreach (var e in rows)
        {
            if (sb.Length > 0) sb.Append(',');
            if (e.IsPool) sb.Append("pool:").Append(e.Pool).Append(':').Append(bosses ? $"{e.Min}-{e.Max}" : Inv(e.Count));
            else sb.Append(e.Unit).Append(':').Append(Inv(e.Count));
        }
        return sb.ToString();
    }

    /// <summary>Adds a fixed unit to <paramref name="rows"/>: on a list that takes each name once (action.units, bosses),
    /// a name already there gets its count raised (kept within <paramref name="maxCount"/>) instead of a second row.</summary>
    public static void AddUnit(List<NyarEntry> rows, string unit, int count, int maxCount, bool mergeRepeats)
    {
        if (mergeRepeats)
            foreach (var e in rows)
                if (!e.IsPool && e.Unit == unit) { e.Count = Math.Min(maxCount, e.Count + count); return; }
        rows.Add(new NyarEntry { Unit = unit, Count = count });
    }

    /// <summary>Adds a pool entry (units: count; bosses: min-max roll); a repeated pool on action.units raises the count.</summary>
    public static void AddPool(List<NyarEntry> rows, string pool, int count, int min, int max, bool bosses, bool mergeRepeats)
    {
        if (!bosses && mergeRepeats)
            foreach (var e in rows)
                if (e.IsPool && e.Pool == pool) { e.Count = Math.Min(MaxUnitCount, e.Count + count); return; }
        rows.Add(bosses ? new NyarEntry { Pool = pool, Min = min, Max = max } : new NyarEntry { Pool = pool, Count = count });
    }

    // ================================================================== pool save

    /// <summary>One command of a pool save: the value sent and the member names it adds.</summary>
    internal sealed class PoolStep
    {
        public string Value;
        public string[] Covers;
    }

    /// <summary>The commands that replace pool members with <paramref name="members"/>: the first carries as many names as
    /// keep the whole command (built by <paramref name="commandFor"/>, the exact string sent) within
    /// <see cref="MaxCommandBytes"/> UTF-8 bytes, then one "+CHAR_x" per remaining name (the server's append form).</summary>
    public static List<PoolStep> PoolSaveSteps(IReadOnlyList<string> members, Func<string, string> commandFor)
    {
        var steps = new List<PoolStep>();
        if (members == null || members.Count == 0) return steps;
        var first = new List<string> { members[0] };
        int i = 1;
        for (; i < members.Count; i++)
        {
            var trial = string.Join(",", first) + "," + members[i];
            if (Encoding.UTF8.GetByteCount(commandFor(trial)) > MaxCommandBytes) break;
            first.Add(members[i]);
        }
        steps.Add(new PoolStep { Value = string.Join(",", first), Covers = first.ToArray() });
        for (; i < members.Count; i++) steps.Add(new PoolStep { Value = "+" + members[i], Covers = new[] { members[i] } });
        return steps;
    }

    /// <summary>A pool member list (the value of a "Save pool"): 1-30 distinct unit names.</summary>
    public static string CheckPoolMembers(IReadOnlyList<string> members)
    {
        if (members == null || members.Count == 0) return "Add at least one unit to the pool.";
        if (members.Count > MaxPoolMembers) return $"A pool holds at most {MaxPoolMembers} units ({members.Count} picked).";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in members)
        {
            if (!IsUnitName(m)) return $"'{m}' isn't a unit name (CHAR_…).";
            if (!seen.Add(m)) return $"{m} is in the pool twice.";
        }
        return null;
    }

    public static string CheckPursuit(string value) => CheckValue(Spec("action.pursuit"), value);

    // ================================================================== send order

    /// <summary>A wave's start rule as the sets that produce it, in an order the server accepts at every step
    /// (EventAdmin: wave 1 takes no start rule; whenCleared=false with no afterSeconds is refused).
    /// <paramref name="rule"/>: "cleared" | "after" | "afterOrCleared".</summary>
    public static List<KeyValuePair<string, string>> StartRuleSets(int wave, string rule, int seconds)
    {
        string a = $"action.waveList.{wave}.afterSeconds", w = $"action.waveList.{wave}.whenCleared";
        string s = seconds.ToString(CultureInfo.InvariantCulture);
        var l = new List<KeyValuePair<string, string>>();
        switch (rule)
        {
            case "cleared":
                l.Add(new(w, "true")); l.Add(new(a, "none")); break;
            case "after":
                l.Add(new(a, s)); l.Add(new(w, "false")); break;
            default:
                l.Add(new(a, s)); l.Add(new(w, "true")); break;
        }
        return l;
    }

    /// <summary>Changed fields in an order the server accepts: definition and condition fields first, then location /
    /// behaviour, then units-form fields, then waves in ascending order (each wave's units before its bosses and start
    /// rule), then the event's bosses, pools and pursuit last. A level / levelDelta pair: the "none" goes first, so the
    /// server never sees both. Stable inside a group.</summary>
    public static List<KeyValuePair<string, string>> OrderForSave(IEnumerable<KeyValuePair<string, string>> changes)
    {
        var list = new List<(int rank, int sub, int idx, KeyValuePair<string, string> kv)>();
        int i = 0;
        foreach (var kv in changes)
        {
            string f = kv.Key;
            int rank, sub = 0;
            if (f.StartsWith("action.pools.", StringComparison.Ordinal)) rank = 3;     // before any entry that names the pool
            else if (f == "name" || f == "durationSeconds" || f.StartsWith("conditions.", StringComparison.Ordinal)) rank = 0;
            else if (f == "location" || f == "action.behaviour" || f == "action.radius") rank = 1;
            else if (f.StartsWith("action.waveList.", StringComparison.Ordinal))
            {
                var p = f.Split('.');
                int.TryParse(p.Length > 2 ? p[2] : "0", NumberStyles.None, CultureInfo.InvariantCulture, out var n);
                rank = 4; sub = n * 10 + (p.Length == 4 && p[3] == "units" ? 0 : p.Length > 4 ? 1 : 2);
            }
            else if (f == "action.bosses") rank = 5;
            else if (f == "action.pursuit") rank = 6;
            else rank = 2;   // units form, modifiers, empower, apocalypse, loot / territory
            if ((f.EndsWith(".level", StringComparison.Ordinal) || f.EndsWith(".levelDelta", StringComparison.Ordinal)) && kv.Value != "none") sub += 5;
            list.Add((rank, sub, i++, kv));
        }
        list.Sort((x, y) => x.rank != y.rank ? x.rank.CompareTo(y.rank) : x.sub != y.sub ? x.sub.CompareTo(y.sub) : x.idx.CompareTo(y.idx));
        var outl = new List<KeyValuePair<string, string>>();
        foreach (var x in list) outl.Add(x.kv);
        return outl;
    }

    // ================================================================== helpers

    private static bool Count(string s, int min, int max, out int n)
        => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= min && n <= max;

    private static bool IsDigits(string s)
    {
        if (s.Length == 0) return false;
        foreach (var c in s) if (c < '0' || c > '9') return false;
        return true;
    }

    private static string Inv(int n) => n.ToString(CultureInfo.InvariantCulture);
    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
