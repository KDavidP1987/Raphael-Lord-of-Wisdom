using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Raphael.Resources;

// 0.74.0: the unit and object reference (PLAN 2026-10-24 Part C). Every CHAR_ unit and every world object from the
// 1.1.12 prefab dump, parsed lazily from one generated const string (GameCatalog.Data.cs, tools/gen-game-catalog.py).
//
// PURE LOGIC: no Unity, no IL2CPP, no BepInEx. The logic tests link this file. Runtime name resolution (localization,
// blueprint names) lives in GameCatalogNames.cs, which is not linked, and reaches in only through SetResolvedName.

public enum CatalogKind { Unit, Object }

/// <summary>How a row's display name was obtained. Set by what actually happened, never assumed.</summary>
public enum NameSource { PrefabName, GameText }

[Flags]
public enum CatalogFlags { None = 0, VBlood = 1, Minion = 2, Buildable = 4, Container = 8 }

public sealed class CatalogRow
{
    public CatalogKind Kind { get; internal set; }
    /// <summary>PrefabGUID hash, signed (the dump's own form).</summary>
    public int Guid { get; internal set; }
    public string InternalName { get; internal set; }
    /// <summary>Units: the faction without "Faction_" ("None" when the prefab has no FactionReference).
    /// Objects: Castle, Resource, Tree or World.</summary>
    public string Group { get; internal set; }
    public string SubGroup { get; internal set; }
    public int Level { get; internal set; }
    public CatalogFlags Flags { get; internal set; }
    /// <summary>Localization AssetGuid string (units, from BloodCraftUI's MIT table) or empty.</summary>
    public string LocKey { get; internal set; }
    public string DisplayName { get; internal set; }
    public NameSource NameSource { get; internal set; }

    // Normalised search text and display name, rebuilt whenever the display name changes.
    internal string Search;
    internal string DisplayNorm;
    internal string GuidText;

    public bool Has(CatalogFlags f) => (Flags & f) == f;
    public bool IsUnit => Kind == CatalogKind.Unit;

    internal void Reindex()
    {
        DisplayNorm = GameCatalog.Normalize(DisplayName);
        GuidText = Guid.ToString(CultureInfo.InvariantCulture);
        Search = " " + DisplayNorm + " " + GameCatalog.Normalize(InternalName) + " " + GuidText.TrimStart('-') + " "
                 + GameCatalog.Normalize(Group) + " " + GameCatalog.Normalize(SubGroup) + " ";
    }
}

public sealed class CatalogParseResult
{
    public readonly List<CatalogRow> Rows = new();
    public readonly List<string> Errors = new();
    public int Skipped;
}

public static class GameCatalog
{
    public const int Columns = 8;
    public const string FlagLetters = "vmbc";

    private static string _table;
    private static List<CatalogRow> _rows;
    private static Dictionary<int, CatalogRow> _byGuid;
    private static readonly List<CatalogRow> _empty = new();

    /// <summary>Called once with the count of malformed rows skipped by the lenient runtime parse (runtime hooks a
    /// warning log here; the logic tests leave it null).</summary>
    public static Action<string> Warn;

    /// <summary>Goes up after every batch of resolved names. An open view re-runs its query when it changes.</summary>
    public static int Revision { get; private set; }

    public static int SkippedRows { get; private set; }

    /// <summary>The generated table, unless a test swapped in its own (<see cref="UseTable"/>).</summary>
    private static string Table => _table ?? GameCatalogData.Table;

    public static IReadOnlyList<CatalogRow> Rows => Ensure();

    public static int Count => Ensure().Count;

    /// <summary>Tests: replace the table (null = the generated one) and drop every cache and resolved name.</summary>
    public static void UseTable(string table)
    {
        _table = table;
        _rows = null;
        _byGuid = null;
        Revision++;
    }

    public static CatalogRow ByGuid(int guid)
    {
        Ensure();
        return _byGuid.TryGetValue(guid, out var r) ? r : null;
    }

    private static List<CatalogRow> Ensure()
    {
        if (_rows != null) return _rows;
        var res = Parse(Table, strict: false);
        _byGuid = new Dictionary<int, CatalogRow>(res.Rows.Count);
        foreach (var r in res.Rows) _byGuid[r.Guid] = r;
        SkippedRows = res.Skipped;
        _rows = res.Rows;
        if (res.Skipped > 0)
            Warn?.Invoke($"GameCatalog: skipped {res.Skipped} malformed row(s) of the generated table; first: {res.Errors[0]}");
        return _rows ?? _empty;
    }

    /// <summary>Parse the tab-separated table. Strict (tests): every problem is an error. Lenient (runtime, a defence
    /// only): a malformed row is skipped and counted, the rest still loads. A duplicate GUID is an error in both and
    /// the later row is skipped.</summary>
    public static CatalogParseResult Parse(string table, bool strict)
    {
        var res = new CatalogParseResult();
        if (string.IsNullOrEmpty(table)) return res;
        var seen = new HashSet<int>();
        int lineNo = 0;
        foreach (var rawLine in table.Split('\n'))
        {
            lineNo++;
            string line = rawLine.EndsWith("\r", StringComparison.Ordinal) ? rawLine.Substring(0, rawLine.Length - 1) : rawLine;
            if (line.Length == 0) continue;
            string err = TryParseRow(line, out var row);
            if (err == null && !seen.Add(row.Guid)) err = $"duplicate GUID {row.Guid}";
            if (err != null)
            {
                res.Errors.Add($"line {lineNo}: {err}");
                res.Skipped++;
                continue;
            }
            res.Rows.Add(row);
        }
        if (strict && res.Errors.Count == 0 && res.Rows.Count == 0) res.Errors.Add("the table has no rows");
        return res;
    }

    private static string TryParseRow(string line, out CatalogRow row)
    {
        row = null;
        var f = line.Split('\t');
        if (f.Length != Columns) return $"expected {Columns} columns, got {f.Length}";
        CatalogKind kind;
        if (f[0] == "U") kind = CatalogKind.Unit;
        else if (f[0] == "O") kind = CatalogKind.Object;
        else return $"unknown kind '{f[0]}'";
        if (!int.TryParse(f[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int guid)) return $"bad GUID '{f[1]}'";
        if (f[2].Length == 0) return "empty internal name";
        if (!int.TryParse(f[5], NumberStyles.None, CultureInfo.InvariantCulture, out int level)) return $"bad level '{f[5]}'";
        var flags = CatalogFlags.None;
        foreach (char c in f[6])
        {
            int i = FlagLetters.IndexOf(c);
            if (i < 0) return $"unknown flag '{c}'";
            flags |= (CatalogFlags)(1 << i);
        }
        row = new CatalogRow
        {
            Kind = kind, Guid = guid, InternalName = f[2], Group = f[3], SubGroup = f[4], Level = level, Flags = flags,
            LocKey = f[7], DisplayName = ReadableName(f[2]), NameSource = NameSource.PrefabName,
        };
        row.Reindex();
        return null;
    }

    // ------------------------------------------------------------------ names

    private static readonly string[] _namePrefixes = { "CHAR_", "TM_", "Dynamic_", "Chain_", "Resource_", "Trees_", "BP_" };

    /// <summary>The fallback display name: the prefab name without its kind prefix, underscores as spaces.
    /// TM_Castle_Container_Bookshelf_Base_6x2 → "Castle Container Bookshelf Base 6x2".</summary>
    public static string ReadableName(string internalName)
    {
        if (string.IsNullOrEmpty(internalName)) return "";
        string s = internalName;
        foreach (var p in _namePrefixes)
            if (s.Length > p.Length && s.StartsWith(p, StringComparison.Ordinal)) { s = s.Substring(p.Length); break; }
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char c in s)
        {
            if (c == '_') { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Record a name obtained from the game. Rebuilds that row's search text. Does not bump
    /// <see cref="Revision"/>: the resolver bumps it at most once a second and when it finishes (<see cref="BumpRevision"/>).</summary>
    public static bool SetResolvedName(CatalogRow row, string name)
    {
        if (row == null) return false;
        name = Clean(name);
        if (name.Length == 0) return false;
        if (row.NameSource == NameSource.GameText && row.DisplayName == name) return false;
        row.DisplayName = name;
        row.NameSource = NameSource.GameText;
        row.Reindex();
        return true;
    }

    public static void BumpRevision() => Revision++;

    // Game text can carry rich-text tags or line breaks; a display name is one plain line.
    private static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool inTag = false;
        foreach (char c in s)
        {
            if (c == '<') { inTag = true; continue; }
            if (inTag) { if (c == '>') inTag = false; continue; }
            sb.Append(c == '\n' || c == '\r' || c == '\t' ? ' ' : c);
        }
        return sb.ToString().Trim();
    }

    // ------------------------------------------------------------------ search

    /// <summary>Lower case, '_' and '-' as spaces, runs of spaces collapsed, trimmed.</summary>
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char ch in s)
        {
            char c = ch == '_' || ch == '-' || char.IsWhiteSpace(ch) ? ' ' : char.ToLowerInvariant(ch);
            if (c == ' ') { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>A typed number read as a signed or unsigned 32-bit value, returned in signed form
    /// ("4294967295" → -1). False for anything else.</summary>
    public static bool TryParseGuid(string s, out int guid)
    {
        guid = 0;
        s = (s ?? "").Trim();
        if (s.Length == 0) return false;
        if (int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out guid)) return true;
        if (uint.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out uint u)) { guid = unchecked((int)u); return true; }
        return false;
    }

    /// <summary>Filter then sort. Every typed word must match (AND, any order). Order: an exact GUID or internal-name
    /// match, then a display-name prefix match, then the rest; ties by display name, then internal name.
    /// <paramref name="kind"/> null = both kinds; empty <paramref name="group"/> / <paramref name="subGroup"/> = any;
    /// <paramref name="flags"/> must all be set.</summary>
    public static List<CatalogRow> Query(string text, CatalogKind? kind = null, string group = null, string subGroup = null,
        CatalogFlags flags = CatalogFlags.None)
        => Query(Rows, text, kind, group, subGroup, flags);

    public static List<CatalogRow> Query(IReadOnlyList<CatalogRow> rows, string text, CatalogKind? kind, string group,
        string subGroup, CatalogFlags flags)
    {
        string raw = (text ?? "").Trim();
        string norm = Normalize(raw);
        var words = norm.Length == 0 ? Array.Empty<string>() : norm.Split(' ');
        // The whole query as one 32-bit number (signed or unsigned spelling) ranks that GUID first.
        var rawWords = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        int wholeGuid = 0;
        bool wholeIsGuid = rawWords.Length == 1 && TryParseGuid(raw, out wholeGuid);

        var hits = new List<(CatalogRow Row, int Rank)>();
        foreach (var r in rows)
        {
            if (kind.HasValue && r.Kind != kind.Value) continue;
            if (!string.IsNullOrEmpty(group) && !string.Equals(r.Group, group, StringComparison.Ordinal)) continue;
            if (!string.IsNullOrEmpty(subGroup) && !string.Equals(r.SubGroup, subGroup, StringComparison.Ordinal)) continue;
            if (flags != CatalogFlags.None && !r.Has(flags)) continue;
            if (!Matches(r, words)) continue;
            int rank = 2;
            if (raw.Length > 0)
            {
                if ((wholeIsGuid && r.Guid == wholeGuid) || string.Equals(r.InternalName, raw, StringComparison.OrdinalIgnoreCase)) rank = 0;
                else if (norm.Length > 0 && r.DisplayNorm.StartsWith(norm, StringComparison.Ordinal)) rank = 1;
            }
            hits.Add((r, rank));
        }
        hits.Sort((a, b) =>
        {
            int c = a.Rank.CompareTo(b.Rank);
            return c != 0 ? c : Compare(a.Row, b.Row);
        });
        var outl = new List<CatalogRow>(hits.Count);
        foreach (var h in hits) outl.Add(h.Row);
        return outl;
    }

    /// <summary>The stable tie order: display name (case-insensitive, then exact), then internal name.</summary>
    public static int Compare(CatalogRow a, CatalogRow b)
    {
        int c = string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.DisplayName, b.DisplayName);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.InternalName, b.InternalName);
        return c != 0 ? c : a.Guid.CompareTo(b.Guid);
    }

    private static bool Matches(CatalogRow r, string[] words)
    {
        foreach (var w in words)
        {
            if (r.Search.IndexOf(w, StringComparison.Ordinal) >= 0) continue;
            // The search text holds the GUID's digits; a number typed in its unsigned spelling still finds it.
            if (!IsNumberFor(w, r.Guid)) return false;
        }
        return true;
    }

    private static bool IsNumberFor(string w, int guid)
    {
        if (w.Length == 0 || !char.IsDigit(w[0])) return false;
        if (TryParseGuid(w, out int g) && g == guid) return true;     // e.g. 4294967295 for -1
        return TryParseGuid("-" + w, out g) && g == guid;            // "-123" arrives normalised as "123"
    }

    // ------------------------------------------------------------------ integrity (logic tests)

    /// <summary>sha256 (lower-case hex) of the table with any CR removed, as the generator computes it.</summary>
    public static string TableHash(string table)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes((table ?? "").Replace("\r", "")));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
