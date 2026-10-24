using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Raphael.Services.Nyar;

// One tracked unit from `.nyar debug here` (Nyarlathotep AdminLines.DebugUnit):
//   "<prefab> <eventId|manual> left <n>s|NONE lvl <L> hp <h>/<max> pp <p> sp <s> ms <m> as <a> at <x>,<z> recipe ok[ UNMARKED]"
internal sealed record NyarUnitRow(string Prefab, string Event, string Left, int Level, int Hp, int MaxHp, int Power,
    bool HasPos, int X, int Z, bool Unmarked, string Raw);

// 0.71.0: OBSERVE-ONLY capture of two human replies that have no wire twin on api 8, so the admin tabs can show
// them instead of sending the admin to chat:
//   • `.nyar event info <id>` — the event's full definition (the editor shows it and pre-fills fields from it);
//   • `.nyar debug here [r]`  — tracked units near the admin with level, health and x/z (the Units tab's table).
// A capture opens when Raphael sends the command and closes 1.5 s after the last matching line. Lines are never
// destroyed: they still reach chat. ClientChatPatch only passes System messages here.
internal static class NyarCapture
{
    public static event Action InfoChanged;
    public static event Action UnitsChanged;

    private static readonly Regex Tags = new("<[^>]*>", RegexOptions.Compiled);

    // ---- event info ----
    private static string _infoId = "";
    private static float _infoUntil;
    private static bool _infoStarted;
    private static readonly List<string> _info = new();
    public static string InfoId { get; private set; } = "";
    public static IReadOnlyList<string> InfoLines => _info;

    public static void BeginInfo(string id)
    {
        _infoId = (id ?? "").Trim();
        _infoStarted = false;
        _infoUntil = Time.realtimeSinceStartup + 4f;
    }

    // ---- debug here ----
    private static float _unitsUntil;
    private static bool _unitsStarted;
    private static readonly List<NyarUnitRow> _units = new();
    private static readonly List<string> _unitsOther = new();
    public static IReadOnlyList<NyarUnitRow> Units => _units;
    public static IReadOnlyList<string> UnitsOther => _unitsOther;   // "+k more", empowered natives, "no tracked units…"
    public static int UnitsRadius { get; private set; }
    public static DateTime UnitsReadAt { get; private set; }
    public static bool UnitsPending => Time.realtimeSinceStartup <= _unitsUntil && !_unitsStarted;

    public static void BeginUnits(int radius)
    {
        UnitsRadius = radius > 0 ? radius : 30;
        _unitsStarted = false;
        _unitsUntil = Time.realtimeSinceStartup + 4f;
        Fire(UnitsChanged);
    }

    // ---- pool library (0.74.0, Nyarlathotep 0.15.0 `.nyar pool list|info|reload`) ----
    // The reply lines are shown verbatim in the Pools library card and never parsed for meaning (handoff §3b: they are
    // not a contract). Only the first line is recognised, by the shapes PoolLines prints, so an unrelated System message
    // in the window isn't taken for the reply; once started, every line within 1.5 s of the last is kept (up to 30).
    public static event Action PoolChanged;
    private static float _poolUntil;
    private static bool _poolStarted;
    private static readonly List<string> _pool = new();
    public static IReadOnlyList<string> PoolLines => _pool;
    public static string PoolAsked { get; private set; } = "";
    public static bool PoolPending => Time.realtimeSinceStartup <= _poolUntil && !_poolStarted;

    public static void BeginPool(string asked)
    {
        PoolAsked = asked ?? "";
        _poolStarted = false;
        _poolUntil = Time.realtimeSinceStartup + 4f;
        Fire(PoolChanged);
    }

    private static readonly Regex PoolFirst = new(
        @"^(?:[a-z0-9-]{1,24}: \d+ units?, (?:seeded|library)|pool [a-z0-9-]{1,24}: \d+ units?|pools(?: unknown)?:|pools\.json |" +
        @"no pools$|unknown pool|no page |page must be|argument must be list|named by: )", RegexOptions.Compiled);

    private static void ObservePool(string text, float now)
    {
        if (!_poolStarted)
        {
            if (!PoolFirst.IsMatch(text)) return;
            _poolStarted = true;
            _pool.Clear();
        }
        foreach (var l in text.Split('\n'))
            if (l.TrimEnd().Length > 0 && _pool.Count < 30) _pool.Add(l.TrimEnd());
        _poolUntil = now + 1.5f;
        Fire(PoolChanged);
    }

    private static readonly Regex UnitRx = new(
        @"^(?<prefab>CHAR_\S+) (?<ev>\S+) left (?<left>\S+) lvl (?<lvl>-?\d+) hp (?<hp>-?\d+)/(?<max>-?\d+) pp (?<pp>-?\d+)" +
        @"(?:.*? at (?<x>-?\d+),(?<z>-?\d+))?(?<rest>.*)$", RegexOptions.Compiled);

    public static void Observe(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return;
        float now = Time.realtimeSinceStartup;
        if (now > _infoUntil && now > _unitsUntil && now > _poolUntil) return;
        string text = (raw.IndexOf('<') >= 0 ? Tags.Replace(raw, "") : raw).Trim();
        if (text.Length == 0 || text[0] == '[') return;

        if (now <= _infoUntil) ObserveInfo(text, now);
        if (now <= _unitsUntil) ObserveUnits(text, now);
        if (now <= _poolUntil) ObservePool(text, now);
    }

    private static void ObserveInfo(string text, float now)
    {
        // The reply starts `<id> "<name>" enabled|disabled …`; continuation messages follow back to back.
        if (!_infoStarted)
        {
            if (!text.StartsWith(_infoId + " \"", StringComparison.OrdinalIgnoreCase)) return;
            _infoStarted = true;
            _info.Clear();
            InfoId = _infoId;
        }
        foreach (var l in text.Split('\n'))
            if (l.Trim().Length > 0 && _info.Count < 40) _info.Add(l.Trim());
        _infoUntil = now + 1.5f;
        Fire(InfoChanged);
    }

    private static void ObserveUnits(string text, float now)
    {
        var lines = text.Split('\n');
        bool any = false;
        foreach (var l0 in lines)
        {
            string l = l0.Trim();
            if (l.Length == 0) continue;
            var m = UnitRx.Match(l);
            bool unitLine = m.Success;
            bool tail = l.StartsWith("no tracked units within", StringComparison.Ordinal)
                        || (l.StartsWith("+", StringComparison.Ordinal) && l.EndsWith(" more", StringComparison.Ordinal));
            // Empowered native lines follow the unit lines; accept them only once the reply has started.
            if (!unitLine && !tail && !_unitsStarted) continue;
            if (!_unitsStarted)
            {
                _unitsStarted = true;
                _units.Clear(); _unitsOther.Clear();
                UnitsReadAt = DateTime.Now;
            }
            any = true;
            if (unitLine)
            {
                bool hasPos = m.Groups["x"].Success;
                _units.Add(new NyarUnitRow(m.Groups["prefab"].Value, m.Groups["ev"].Value, m.Groups["left"].Value,
                    Int(m.Groups["lvl"].Value), Int(m.Groups["hp"].Value), Int(m.Groups["max"].Value), Int(m.Groups["pp"].Value),
                    hasPos, hasPos ? Int(m.Groups["x"].Value) : 0, hasPos ? Int(m.Groups["z"].Value) : 0,
                    m.Groups["rest"].Value.Contains("UNMARKED"), l));
            }
            else if (_unitsOther.Count < 20) _unitsOther.Add(l);
        }
        if (!any) return;
        _unitsUntil = now + 1.5f;
        Fire(UnitsChanged);
    }

    public static void Reset()
    {
        _infoId = InfoId = ""; _info.Clear(); _infoUntil = 0f; _infoStarted = false;
        _units.Clear(); _unitsOther.Clear(); _unitsUntil = 0f; _unitsStarted = false; UnitsRadius = 0;
        _pool.Clear(); _poolUntil = 0f; _poolStarted = false; PoolAsked = "";
    }

    // ---- parsing the info reply into editor pre-fills (best effort; never required) ----

    /// <summary>The value after "<paramref name="key"/> " in the info text, up to the next comma or line end.</summary>
    public static string InfoValue(string key)
    {
        foreach (var l in _info)
        {
            int i = IndexOfWord(l, key);
            if (i < 0) continue;
            string rest = l.Substring(i + key.Length).TrimStart();
            int end = rest.IndexOfAny(new[] { ',', '\n' });
            return (end >= 0 ? rest.Substring(0, end) : rest).Trim();
        }
        return "";
    }

    /// <summary>The event's display name: the quoted part of the first line.</summary>
    public static string InfoName()
    {
        if (_info.Count == 0) return "";
        var m = Regex.Match(_info[0], "\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>"units 4 CHAR_Bandit_Thug, 2 CHAR_Bandit_Hunter" → "CHAR_Bandit_Thug:4,CHAR_Bandit_Hunter:2" (the set form).</summary>
    public static string InfoUnitsSetForm()
    {
        foreach (var l in _info)
        {
            int i = IndexOfWord(l, "units");
            if (i < 0 || l.StartsWith("units:", StringComparison.Ordinal)) continue;
            var parts = new List<string>();
            foreach (Match m in Regex.Matches(l.Substring(i + 5), @"(\d+)\s*[x×]?\s*(CHAR_[A-Za-z0-9_]+)"))
                parts.Add($"{m.Groups[2].Value}:{m.Groups[1].Value}");
            if (parts.Count > 0) return string.Join(",", parts);
        }
        return "";
    }

    public static bool InfoMentions(string word)
    {
        foreach (var l in _info) if (l.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static int IndexOfWord(string line, string word)
    {
        int i = 0;
        while ((i = line.IndexOf(word, i, StringComparison.Ordinal)) >= 0)
        {
            bool startOk = i == 0 || !char.IsLetterOrDigit(line[i - 1]);
            int after = i + word.Length;
            bool endOk = after < line.Length && line[after] == ' ';
            if (startOk && endOk) return i;
            i = after;
        }
        return -1;
    }

    private static int Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static void Fire(Action a)
    {
        try { a?.Invoke(); } catch (Exception ex) { Utils.LogUtils.LogError($"[Nyar] capture handler threw: {ex}"); }
    }
}
