using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Raphael.Services.Nyar;

// 0.72.0: reads the human `.nyar event info <id>` reply (Nyarlathotep Logic/EventAdmin.cs Describe / WaveLines /
// SpawnKeys / BossesLine / WaveVarietyLines at 4f059f5) into editor pre-fills. Best effort and never used for a
// decision: anything not read leaves its field empty with a "couldn't read" note, never a guess. A line the server
// shortened to fit chat ("final wave adds: 4 bosses") is reported as unreadable. Pure; unit-tested on server-format
// fixtures.
// 0.73.0: the undead-hordes lines (Nyarlathotep 0.14.0, EventAdmin.cs InfestationLines / HordeMarchLines / MarchLine /
// SpreadText / AtText at f340f75): origins, hunters and drift, the spread / growth / infest blocks, a horde march's start
// and destination, and a destination wave's march line. Every pattern is anchored at both ends; coordinates use the
// server's invariant "0.#" shape, so a malformed or shortened line reads as unread.

/// <summary>One wave of a wave-list event as `event info` shows it.</summary>
internal sealed class NyarWaveInfo
{
    public int N, Total;
    public string Units;          // set form ("CHAR_a:4,pool:troops:2"), or null when unreadable
    public bool HasExtras;        // an entry carries a level / multiplier / chance (a units set replaces those)
    public string Rule = "";      // "at start", "after 30 s", "after 30 s or when cleared", "when cleared"
    public int AfterSeconds;      // 0 = none
    public bool WhenCleared;
    public string Bosses;         // set form of the wave's own bosses, "" for none, null when unreadable
}

internal static class NyarInfoParse
{
    private static readonly Regex WaveOne = new(@"^wave (\d+): (\d+) units \((.*)\), (.+)$", RegexOptions.Compiled);
    private static readonly Regex WaveBelow = new(@"^wave (\d+): (\d+) units, (.+), units below$", RegexOptions.Compiled);
    private static readonly Regex PoolHead = new(@"^pools: ([a-z0-9-]{1,24}) \((\d+) units?\): (.*)$", RegexOptions.Compiled);
    private static readonly Regex FixedEntry = new(@"^(CHAR_[A-Za-z0-9_]+) ×(\d+)(.*)$", RegexOptions.Compiled);
    private static readonly Regex PoolEntry = new(@"^(\d+) from ([a-z0-9-]{1,24})(.*)$", RegexOptions.Compiled);
    private static readonly Regex UnitsFormEntry = new(@"^(\d+) (CHAR_[A-Za-z0-9_]+)$", RegexOptions.Compiled);
    private static readonly Regex FixedBoss = new(@"^(CHAR_[A-Za-z0-9_]+) x(\d+)$", RegexOptions.Compiled);
    private static readonly Regex RollBoss = new(@"^(\d+)-(\d+) bosses from ([a-z0-9-]{1,24})$", RegexOptions.Compiled);

    // A coordinate as the server prints it (FormattableString.Invariant "{x:0.#}").
    private const string C = @"-?\d+(?:\.\d)?";
    private static readonly Regex SpreadRx = new(@"^\+(\d)(?:-(\d))? per kill(?: from ([a-z0-9-]{1,24}))?, max (\d+), (\d+) per minute$", RegexOptions.Compiled);
    private static readonly Regex OriginsRx = new(@"^origins: (\d+), at least (\d+) natives each, (\d+) m apart, (\d+) units each$", RegexOptions.Compiled);
    private static readonly Regex HuntersRx = new(@"^hunters: (\d+) %, drift radius (\d+) m$", RegexOptions.Compiled);
    private static readonly Regex InfestRx = new(@"^infest: hunters (\d+) %, drift radius (\d+) m, spread (.+)$", RegexOptions.Compiled);
    private static readonly Regex StartPointRx = new($@"^start: at (?:(the starting admin)|{C} {C}(?: height {C})?)$", RegexOptions.Compiled);
    private static readonly Regex StartHotspotRx = new(@"^start: the natives' hotspot nearest the destination, at least (\d+) m away$", RegexOptions.Compiled);
    private static readonly Regex DestinationRx = new($@"^destination: (.+), radius (\d+), at (?:the starting admin|{C} {C}(?: height {C})?)$", RegexOptions.Compiled);
    private static readonly Regex MarchRx = new($@"^march: from (?:the starting admin|\({C}, {C}\)) to (.+) \((?:the starting admin|{C}, {C})\) at (\d+(?:\.\d{{1,2}})?) m/s$", RegexOptions.Compiled);
    private static readonly Regex HordeOfRx = new(@"^horde of (\d+): ", RegexOptions.Compiled);

    /// <summary>One spread block ("+1-3 per kill from troops, max 300, 20 per minute") as its five set values, or null
    /// when the text doesn't have that exact shape. "+2 per kill" is min = max = 2; no " from …" is pool none.</summary>
    public static Dictionary<string, string> Spread(string text)
    {
        var m = SpreadRx.Match(text ?? "");
        if (!m.Success) return null;
        string min = m.Groups[1].Value, max = m.Groups[2].Success ? m.Groups[2].Value : min;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["perKillMin"] = min, ["perKillMax"] = max, ["max"] = m.Groups[4].Value, ["perMinute"] = m.Groups[5].Value,
            ["pool"] = m.Groups[3].Success ? m.Groups[3].Value : "none",
        };
    }

    /// <summary>An infestation's "origins:" line → count, minNatives, minSpacing, perOrigin; null when unread.</summary>
    public static string[] Origins(string line)
    {
        var m = OriginsRx.Match(line ?? "");
        return m.Success ? new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value } : null;
    }

    /// <summary>"hunters: 50 %, drift radius 400 m" → { hunters, drift }; null when unread.</summary>
    public static string[] Hunters(string line)
    {
        var m = HuntersRx.Match(line ?? "");
        return m.Success ? new[] { m.Groups[1].Value, m.Groups[2].Value } : null;
    }

    /// <summary>"infest: hunters 60 %, drift radius 500 m, spread …" → hunters, drift and the spread text; null when unread.</summary>
    public static string[] Infest(string line)
    {
        var m = InfestRx.Match(line ?? "");
        return m.Success ? new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value } : null;
    }

    /// <summary>A horde march's start: "point", "admin" or "hotspot" (with <paramref name="minDistance"/>), or null.</summary>
    public static string Start(string line, out string minDistance)
    {
        minDistance = null;
        var h = StartHotspotRx.Match(line ?? "");
        if (h.Success) { minDistance = h.Groups[1].Value; return "hotspot"; }
        var p = StartPointRx.Match(line ?? "");
        return p.Success ? (p.Groups[1].Success ? "admin" : "point") : null;
    }

    /// <summary>A horde march's "destination: &lt;name&gt;, radius R, at …" → { name, radius }; null when unread. The name
    /// may hold commas: the last ", radius N, at &lt;place&gt;" is the server's.</summary>
    public static string[] Destination(string line)
    {
        var m = DestinationRx.Match(line ?? "");
        return m.Success ? new[] { m.Groups[1].Value, m.Groups[2].Value } : null;
    }

    /// <summary>A destination wave's "march: from … to &lt;name&gt; (…) at S m/s" → { name, speed }; null when unread.</summary>
    public static string[] March(string line)
    {
        var m = MarchRx.Match(line ?? "");
        return m.Success ? new[] { m.Groups[1].Value, m.Groups[2].Value } : null;
    }

    /// <summary>A horde march's size from "horde of N: …"; 0 when absent.</summary>
    public static int HordeSize(IReadOnlyList<string> lines)
    {
        foreach (var l in lines) { var m = HordeOfRx.Match(l); if (m.Success) return Int(m.Groups[1].Value); }
        return 0;
    }

    /// <summary>True when the event uses the wave-list form (`wave N:` lines).</summary>
    public static bool IsWaveList(IReadOnlyList<string> lines)
    {
        foreach (var l in lines) if (WaveOne.IsMatch(l) || WaveBelow.IsMatch(l)) return true;
        return false;
    }

    /// <summary>A units-form event's units in set form; "" when there is no units-form action line; null when the line
    /// is there but an entry can't be read.</summary>
    public static string UnitsFormUnits(IReadOnlyList<string> lines)
    {
        foreach (var l in lines)
        {
            if (!l.StartsWith("action: ", StringComparison.Ordinal) || l.IndexOf(" waves every ", StringComparison.Ordinal) < 0) continue;
            string items;
            if (l.EndsWith(", units below", StringComparison.Ordinal))
                items = Joined(lines, "units: ");
            else
            {
                int i = l.IndexOf(", units ", StringComparison.Ordinal);
                if (i < 0) return null;
                items = l.Substring(i + 8);
                items = Regex.Replace(items, @", unit lifetime \d+s$", "");
            }
            var outl = new List<string>();
            foreach (var it in Split(items))
            {
                var m = UnitsFormEntry.Match(it);
                if (m.Success) { outl.Add($"{m.Groups[2].Value}:{m.Groups[1].Value}"); continue; }
                var p = PoolEntry.Match(it);
                if (p.Success && p.Groups[3].Value.Length == 0) { outl.Add($"pool:{p.Groups[2].Value}:{p.Groups[1].Value}"); continue; }
                return null;
            }
            return outl.Count == 0 ? null : string.Join(",", outl);
        }
        return "";
    }

    /// <summary>The waves of a wave-list event, in order (with each wave's own bosses).</summary>
    public static List<NyarWaveInfo> Waves(IReadOnlyList<string> lines)
    {
        var waves = new List<NyarWaveInfo>();
        foreach (var l in lines)
        {
            NyarWaveInfo w = null; string entries = null;
            var m = WaveOne.Match(l);
            if (m.Success) { w = new NyarWaveInfo { N = Int(m.Groups[1].Value), Total = Int(m.Groups[2].Value) }; entries = m.Groups[3].Value; SetRule(w, m.Groups[4].Value); }
            else
            {
                var b = WaveBelow.Match(l);
                if (!b.Success) continue;
                w = new NyarWaveInfo { N = Int(b.Groups[1].Value), Total = Int(b.Groups[2].Value) };
                SetRule(w, b.Groups[3].Value);
                entries = Joined(lines, $"wave {w.N} units: ");
            }
            var outl = new List<string>();
            bool ok = entries.Length > 0;
            foreach (var it in Split(entries))
            {
                var f = FixedEntry.Match(it);
                if (f.Success) { outl.Add($"{f.Groups[1].Value}:{f.Groups[2].Value}"); if (f.Groups[3].Value.Length > 0) w.HasExtras = true; continue; }
                var p = PoolEntry.Match(it);
                if (p.Success) { outl.Add($"pool:{p.Groups[2].Value}:{p.Groups[1].Value}"); if (p.Groups[3].Value.Length > 0) w.HasExtras = true; continue; }
                ok = false;
            }
            w.Units = ok ? string.Join(",", outl) : null;
            w.Bosses = WaveBosses(lines, w.N);
            waves.Add(w);
        }
        return waves;
    }

    private static void SetRule(NyarWaveInfo w, string rule)
    {
        w.Rule = rule.Trim();
        var m = Regex.Match(w.Rule, @"^after (\d+) s( or when cleared)?$");
        if (m.Success) { w.AfterSeconds = Int(m.Groups[1].Value); w.WhenCleared = m.Groups[2].Success; }
        else w.WhenCleared = w.Rule == "when cleared";
    }

    /// <summary>The last wave's bosses (action.bosses) in set form: "" when there are none, null when the server
    /// shortened the line or it can't be read. Also reads an apocalypse's "last horde adds:" line and a horde march's
    /// "horde adds:" ("horde adds: 4 bosses at most", the shortened form, is unreadable).</summary>
    public static string FinalBosses(IReadOnlyList<string> lines)
    {
        foreach (var head in new[] { "final wave adds: ", "last horde adds: ", "horde adds: " })
        {
            var items = Joined(lines, head);
            if (items.Length > 0) return BossItems(items);
        }
        return "";
    }

    /// <summary>Wave <paramref name="n"/>'s own bosses in set form ("" none, null unreadable).</summary>
    public static string WaveBosses(IReadOnlyList<string> lines, int n)
    {
        var items = Joined(lines, $"wave {n} adds: ");
        return items.Length == 0 ? "" : BossItems(items);
    }

    private static string BossItems(string items)
    {
        var outl = new List<string>();
        foreach (var it in Split(items))
        {
            var f = FixedBoss.Match(it);
            if (f.Success) { outl.Add($"{f.Groups[1].Value}:{f.Groups[2].Value}"); continue; }
            var r = RollBoss.Match(it);
            if (r.Success) { outl.Add($"pool:{r.Groups[3].Value}:{r.Groups[1].Value}-{r.Groups[2].Value}"); continue; }
            return null;
        }
        return string.Join(",", outl);
    }

    /// <summary>Every pool of the event with its members, in server order (a long pool wraps over several lines with the
    /// same head). <paramref name="complete"/> is false when a pool's member count doesn't match its "(k units)".</summary>
    public static List<KeyValuePair<string, List<string>>> Pools(IReadOnlyList<string> lines, out bool complete)
    {
        complete = true;
        var order = new List<KeyValuePair<string, List<string>>>();
        var want = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var l in lines)
        {
            var m = PoolHead.Match(l);
            if (!m.Success) continue;
            string name = m.Groups[1].Value;
            var members = order.Find(kv => kv.Key == name).Value;
            if (members == null) { members = new List<string>(); order.Add(new KeyValuePair<string, List<string>>(name, members)); }
            want[name] = Int(m.Groups[2].Value);
            foreach (var it in Split(m.Groups[3].Value))
                if (NyarFieldRules.IsUnitName(it)) members.Add(it); else complete = false;
        }
        foreach (var kv in order) if (kv.Value.Count != want[kv.Key]) complete = false;
        return order;
    }

    /// <summary>Pursuit seconds ("pursuit: after 90 s with no player within 40 m"); 0 when the event has none.</summary>
    public static int Pursuit(IReadOnlyList<string> lines)
    {
        foreach (var l in lines)
        {
            var m = Regex.Match(l, @"^pursuit: after (\d+) s");
            if (m.Success) return Int(m.Groups[1].Value);
        }
        return 0;
    }

    /// <summary>The spawn location kind: "aroundplayer" (with near / far), "region", "admin", "point", or "" unread.</summary>
    public static string Location(IReadOnlyList<string> lines, out int near, out int far)
    {
        near = far = 0;
        foreach (var l in lines)
        {
            if (l == "location: anywhere in the scope regions") return "region";
            if (!l.StartsWith("action: ", StringComparison.Ordinal) || l.StartsWith("action: empower", StringComparison.Ordinal)) continue;
            var m = Regex.Match(l, @", around a player (\d+)-(\d+) m(,|$)");
            if (m.Success) { near = Int(m.Groups[1].Value); far = Int(m.Groups[2].Value); return "aroundplayer"; }
            if (l.Contains(", in the scope regions")) return "region";
            if (l.Contains(", at the admin")) return "admin";
            if (Regex.IsMatch(l, @", at -?\d+(\.\d)? -?\d+(\.\d)?")) return "point";
        }
        return "";
    }

    /// <summary>The behaviour as the set value: "hunt 30", "regionhunt" or "none"; "" when no spawn-waves action is shown.</summary>
    public static string Behaviour(IReadOnlyList<string> lines)
    {
        bool action = false;
        foreach (var l in lines)
        {
            if (l.StartsWith("behaviour: hunts the nearest player", StringComparison.Ordinal)) return "regionhunt";
            if (l.StartsWith("spawn: ", StringComparison.Ordinal))
            {
                var m = Regex.Match(l, @"(?:^spawn: |; )hunt (\d+) m(;|$)");
                if (m.Success) return "hunt " + m.Groups[1].Value;
            }
            if (l.StartsWith("action: ", StringComparison.Ordinal) && !l.StartsWith("action: empower", StringComparison.Ordinal)) action = true;
        }
        return action ? "none" : "";
    }

    /// <summary>The shared modifiers of a units-form event ("spawn: modifiers level 40, maxHealth x1.5; …"):
    /// keys level, levelDelta, maxHealth, power, moveSpeed, attackSpeed → set values ("40", "+3", "1.5").</summary>
    public static Dictionary<string, string> Modifiers(IReadOnlyList<string> lines)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in lines)
        {
            if (!l.StartsWith("spawn: ", StringComparison.Ordinal)) continue;
            foreach (var part in l.Substring(7).Split(new[] { "; " }, StringSplitOptions.None))
            {
                if (!part.StartsWith("modifiers ", StringComparison.Ordinal)) continue;
                foreach (var m0 in part.Substring(10).Split(new[] { ", " }, StringSplitOptions.None))
                {
                    var m = Regex.Match(m0, @"^(level|levelDelta) ([+-]?\d+)$");
                    if (m.Success) { d[m.Groups[1].Value] = m.Groups[2].Value; continue; }
                    m = Regex.Match(m0, @"^(maxHealth|power|moveSpeed|attackSpeed) x(\d+(?:\.\d{1,2})?)$");
                    if (m.Success) d[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }
        }
        return d;
    }

    /// <summary>"loot on" / "claimed territory allowed" in the spawn line.</summary>
    public static bool SpawnFlag(IReadOnlyList<string> lines, string flag)
    {
        foreach (var l in lines)
            if (l.StartsWith("spawn: ", StringComparison.Ordinal))
                foreach (var part in l.Substring(7).Split(new[] { "; " }, StringSplitOptions.None))
                    if (part == flag) return true;
        return false;
    }

    /// <summary>An empowerment's factions as set value ("Faction_Bandits,Faction_Undead") and raised stats
    /// ("physicalPower" → "1.5"); false when there is no empower line.</summary>
    public static bool Empower(IReadOnlyList<string> lines, out string factions, out Dictionary<string, string> stats)
    {
        factions = ""; stats = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in lines)
        {
            if (!l.StartsWith("action: empower ", StringComparison.Ordinal)) continue;
            string rest = l.Substring(16);
            int colon = rest.LastIndexOf(": ", StringComparison.Ordinal);
            string head = colon >= 0 ? rest.Substring(0, colon) : rest;
            string tail = colon >= 0 ? rest.Substring(colon + 2) : "";
            var names = new List<string>();
            foreach (var t in head.Split(new[] { ", " }, StringSplitOptions.None))
            {
                if (t.StartsWith("also ", StringComparison.Ordinal) || t.StartsWith("not ", StringComparison.Ordinal) || t == "V Bloods included") break;
                names.Add(t.StartsWith("Faction_", StringComparison.Ordinal) ? t : "Faction_" + t);
            }
            factions = string.Join(",", names);
            foreach (var s in tail.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = Regex.Match(s, @"^(physicalPower|spellPower|maxHealth|attackSpeed|moveSpeed) x(\d+(?:\.\d{1,2})?)$");
                if (m.Success) stats[m.Groups[1].Value] = m.Groups[2].Value;
            }
            return true;
        }
        return false;
    }

    // ---- helpers ----

    /// <summary>Every line starting with <paramref name="head"/>, the parts after it joined with ", " (PackList wraps).</summary>
    private static string Joined(IReadOnlyList<string> lines, string head)
    {
        var parts = new List<string>();
        foreach (var l in lines)
            if (l.StartsWith(head, StringComparison.Ordinal)) parts.Add(l.Substring(head.Length).Trim());
        return string.Join(", ", parts);
    }

    private static IEnumerable<string> Split(string items)
    {
        foreach (var s in (items ?? "").Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
            if (s.Trim().Length > 0) yield return s.Trim();
    }

    private static int Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
}
