using System;
using System.Collections.Generic;
using System.Globalization;

namespace Raphael.Resources;

// 0.74.0: the state behind both hosts of the unit and object reference (the Settings and Help tab and the floating
// overlay). Each host builds its own controls from this; nothing is reparented. PURE LOGIC (linked by the tests).

public enum CatalogTab { Units, Objects, All }

/// <summary>What a Browse… button writes into its input.</summary>
public enum BrowseFormat
{
    /// <summary>The internal prefab name, e.g. CHAR_Bandit_Hunter.</summary>
    Name,
    /// <summary>The internal name without CHAR_, e.g. Bandit_Hunter (Uriel Castle Events).</summary>
    ShortName,
    /// <summary>The signed PrefabGUID, e.g. -1301144178.</summary>
    Guid,
}

[Flags]
public enum BrowseKinds { Unit = 1, Object = 2, Both = Unit | Object }

public sealed class CatalogGroup
{
    public string Key;
    public string Title;
    public CatalogKind Kind;
    public readonly List<CatalogRow> Rows = new();
    public int Total => Rows.Count;
}

public sealed class GameCatalogView
{
    public const int PageSize = 60;
    public const int ShowMoreStep = 60;
    public const int MaxOpenRows = 240;

    private CatalogTab _tab = CatalogTab.Units;
    private string _text = "";
    private bool _vblood, _minion, _buildable, _container, _groupBy;
    private string _faction = "", _objGroup = "", _objSubGroup = "";
    private BrowseKinds _allowed = BrowseKinds.Both;

    private bool _dirty = true;
    private int _revision = int.MinValue;
    private List<CatalogRow> _results = new();
    private List<CatalogGroup> _groups = new();
    private int _page;
    private string _openGroup;
    private int _openShown = ShowMoreStep;

    /// <summary>Rows to search (tests pass their own; null = GameCatalog.Rows).</summary>
    public IReadOnlyList<CatalogRow> Source;

    // ---------------------------------------------------------------- state

    public CatalogTab Tab { get => _tab; set { value = Clamp(value); if (_tab != value) { _tab = value; Changed(); } } }
    public string Text { get => _text; set { value ??= ""; if (_text != value) { _text = value; Changed(); } } }
    public bool VBloodOnly { get => _vblood; set { if (_vblood != value) { _vblood = value; Changed(); } } }
    public bool MinionOnly { get => _minion; set { if (_minion != value) { _minion = value; Changed(); } } }
    public bool BuildableOnly { get => _buildable; set { if (_buildable != value) { _buildable = value; Changed(); } } }
    public bool ContainerOnly { get => _container; set { if (_container != value) { _container = value; Changed(); } } }
    /// <summary>Unit faction filter; empty = any.</summary>
    public string Faction { get => _faction; set { value ??= ""; if (_faction != value) { _faction = value; Changed(); } } }
    /// <summary>Object category filter (Castle, Resource, Tree, World); empty = any. Changing it clears the sub-group.</summary>
    public string ObjectGroup
    {
        get => _objGroup;
        set { value ??= ""; if (_objGroup != value) { _objGroup = value; _objSubGroup = ""; Changed(); } }
    }
    /// <summary>Object sub-group filter, only meaningful with a category; empty = any.</summary>
    public string ObjectSubGroup { get => _objSubGroup; set { value ??= ""; if (_objSubGroup != value) { _objSubGroup = value; Changed(); } } }
    public bool GroupBy { get => _groupBy; set { if (_groupBy != value) { _groupBy = value; _openGroup = null; Changed(); } } }

    /// <summary>The kinds a Browse target accepts. A single kind pins the tab to it.</summary>
    public BrowseKinds AllowedKinds
    {
        get => _allowed;
        set { _allowed = value == 0 ? BrowseKinds.Both : value; _tab = Clamp(_tab); Changed(); }
    }

    public bool TabAllowed(CatalogTab t) => t switch
    {
        CatalogTab.Units => (_allowed & BrowseKinds.Unit) != 0,
        CatalogTab.Objects => (_allowed & BrowseKinds.Object) != 0,
        _ => _allowed == BrowseKinds.Both,
    };

    private CatalogTab Clamp(CatalogTab t)
    {
        if (TabAllowed(t)) return t;
        return (_allowed & BrowseKinds.Unit) != 0 ? CatalogTab.Units : CatalogTab.Objects;
    }

    /// <summary>Reset every filter (used when a Browse target opens the view on its own kind and filter).</summary>
    public void ResetFilters(string text = "")
    {
        _text = text ?? "";
        _vblood = _minion = _buildable = _container = false;
        _faction = _objGroup = _objSubGroup = "";
        _openGroup = null;
        Changed();
    }

    private void Changed()
    {
        _dirty = true;
        _page = 0;
        _openShown = ShowMoreStep;
    }

    /// <summary>True when a query input changed or names resolved since the last <see cref="Refresh"/>.</summary>
    public bool NeedsRefresh => _dirty || _revision != GameCatalog.Revision;

    /// <summary>Re-run the query and the grouping when needed. Returns true when the results were rebuilt.</summary>
    public bool Refresh()
    {
        if (!NeedsRefresh) return false;
        bool namesOnly = !_dirty;
        _dirty = false;
        _revision = GameCatalog.Revision;

        CatalogKind? kind = _tab == CatalogTab.Units ? CatalogKind.Unit : _tab == CatalogTab.Objects ? CatalogKind.Object : null;
        string group = null, sub = null;
        var flags = CatalogFlags.None;
        if (_tab == CatalogTab.Units)
        {
            group = _faction;
            if (_vblood) flags |= CatalogFlags.VBlood;
            if (_minion) flags |= CatalogFlags.Minion;
        }
        else if (_tab == CatalogTab.Objects)
        {
            group = _objGroup;
            sub = string.IsNullOrEmpty(_objGroup) ? null : _objSubGroup;
            if (_buildable) flags |= CatalogFlags.Buildable;
            if (_container) flags |= CatalogFlags.Container;
        }
        _results = GameCatalog.Query(Source ?? GameCatalog.Rows, _text, kind, group, sub, flags);
        BuildGroups();
        if (_openGroup != null && FindGroup(_openGroup) == null) _openGroup = null;
        if (!namesOnly) _page = 0;
        int pages = PageCount;
        if (_page >= pages) _page = Math.Max(0, pages - 1);
        return true;
    }

    private void BuildGroups()
    {
        var map = new Dictionary<string, CatalogGroup>(StringComparer.Ordinal);
        foreach (var r in _results)
        {
            string key = (r.IsUnit ? "U|" : "O|") + r.Group;
            if (!map.TryGetValue(key, out var g))
            {
                string name = string.IsNullOrEmpty(r.Group) ? "(none)" : r.Group;
                g = new CatalogGroup
                {
                    Key = key, Kind = r.Kind,
                    Title = _tab == CatalogTab.All ? (r.IsUnit ? "Units · " : "Objects · ") + name : name,
                };
                map[key] = g;
            }
            g.Rows.Add(r);   // results are already sorted, so each group's rows are too
        }
        _groups = new List<CatalogGroup>(map.Values);
        _groups.Sort((a, b) =>
        {
            int c = a.Kind.CompareTo(b.Kind);  // Units before Objects (All tab)
            return c != 0 ? c : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        });
    }

    // ---------------------------------------------------------------- results

    public IReadOnlyList<CatalogRow> Results { get { Refresh(); return _results; } }
    public int Total { get { Refresh(); return _results.Count; } }

    // Flat mode: global pages of PageSize.
    public int Page { get { Refresh(); return _page; } }
    public int PageCount { get { int n = _results.Count; return n == 0 ? 1 : (n + PageSize - 1) / PageSize; } }

    public IReadOnlyList<CatalogRow> PageRows()
    {
        Refresh();
        int start = _page * PageSize;
        int count = Math.Max(0, Math.Min(PageSize, _results.Count - start));
        return _results.GetRange(start, count);
    }

    public bool CanPrev => Page > 0;
    public bool CanNext => Page < PageCount - 1;
    public void NextPage() { Refresh(); if (_page < PageCount - 1) _page++; }
    public void PrevPage() { Refresh(); if (_page > 0) _page--; }

    // Grouped mode: groups by name, one open at a time, the open one shows 60 then "Show 60 more" up to 240.
    public IReadOnlyList<CatalogGroup> Groups { get { Refresh(); return _groups; } }
    public string OpenGroup { get { Refresh(); return _openGroup; } }

    private CatalogGroup FindGroup(string key)
    {
        foreach (var g in _groups) if (g.Key == key) return g;
        return null;
    }

    /// <summary>Open a group (closing the one that was open), or close it if it is the open one.</summary>
    public void ToggleGroup(string key)
    {
        Refresh();
        _openShown = ShowMoreStep;
        _openGroup = _openGroup == key || FindGroup(key) == null ? null : key;
    }

    public IReadOnlyList<CatalogRow> OpenRows()
    {
        Refresh();
        var g = _openGroup == null ? null : FindGroup(_openGroup);
        if (g == null) return Array.Empty<CatalogRow>();
        return g.Rows.GetRange(0, Math.Min(Shown(g), g.Rows.Count));
    }

    private int Shown(CatalogGroup g) => Math.Min(Math.Min(_openShown, MaxOpenRows), g.Total);

    public bool CanShowMore
    {
        get
        {
            Refresh();
            var g = _openGroup == null ? null : FindGroup(_openGroup);
            return g != null && Shown(g) < g.Total && Shown(g) < MaxOpenRows;
        }
    }

    /// <summary>The open group has more matches than the 240 rows a group may show.</summary>
    public bool MustRefine
    {
        get
        {
            Refresh();
            var g = _openGroup == null ? null : FindGroup(_openGroup);
            return g != null && g.Total > MaxOpenRows && Shown(g) >= MaxOpenRows;
        }
    }

    public void ShowMore() { if (CanShowMore) _openShown = Math.Min(_openShown + ShowMoreStep, MaxOpenRows); }

    // ---------------------------------------------------------------- filter options

    public static List<string> Factions(IReadOnlyList<CatalogRow> rows = null) => Distinct(rows, CatalogKind.Unit, null);
    public static List<string> ObjectGroups(IReadOnlyList<CatalogRow> rows = null) => Distinct(rows, CatalogKind.Object, null);
    public static List<string> SubGroupsFor(string objectGroup, IReadOnlyList<CatalogRow> rows = null)
        => string.IsNullOrEmpty(objectGroup) ? new List<string>() : Distinct(rows, CatalogKind.Object, objectGroup);

    private static List<string> Distinct(IReadOnlyList<CatalogRow> rows, CatalogKind kind, string groupForSub)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows ?? GameCatalog.Rows)
        {
            if (r.Kind != kind) continue;
            if (groupForSub == null) { if (!string.IsNullOrEmpty(r.Group)) set.Add(r.Group); }
            else if (r.Group == groupForSub && !string.IsNullOrEmpty(r.SubGroup)) set.Add(r.SubGroup);
        }
        return new List<string>(set);
    }

    // ---------------------------------------------------------------- row text

    /// <summary>The muted detail under a row's name: internal name and GUID, plus faction and level for units.</summary>
    public static string Detail(CatalogRow r)
    {
        string s = r.InternalName + " · " + r.Guid.ToString(CultureInfo.InvariantCulture);
        if (r.IsUnit)
        {
            s += " · " + r.Group;
            if (r.Level > 0) s += " · L" + r.Level.ToString(CultureInfo.InvariantCulture);
            if (r.Has(CatalogFlags.VBlood)) s += " · V Blood";
            if (r.Has(CatalogFlags.Minion)) s += " · minion";
        }
        else
        {
            s += " · " + r.Group + (string.IsNullOrEmpty(r.SubGroup) ? "" : " / " + r.SubGroup);
            if (r.Has(CatalogFlags.Buildable)) s += " · buildable";
            if (r.Has(CatalogFlags.Container)) s += " · container";
        }
        return s;
    }
}

/// <summary>Filling forms (PLAN C6): what a Browse… button writes, and which kinds it accepts.</summary>
public static class CatalogBrowse
{
    public static string Value(CatalogRow r, BrowseFormat format)
    {
        if (r == null) return "";
        return format switch
        {
            BrowseFormat.Guid => r.Guid.ToString(CultureInfo.InvariantCulture),
            BrowseFormat.ShortName => r.InternalName.StartsWith("CHAR_", StringComparison.Ordinal) ? r.InternalName.Substring(5) : r.InternalName,
            _ => r.InternalName,
        };
    }

    public static bool Allows(BrowseKinds kinds, CatalogKind kind)
        => (kinds & (kind == CatalogKind.Unit ? BrowseKinds.Unit : BrowseKinds.Object)) != 0;

    public static CatalogTab StartTab(BrowseKinds kinds)
        => kinds == BrowseKinds.Object ? CatalogTab.Objects : kinds == BrowseKinds.Unit ? CatalogTab.Units : CatalogTab.All;

    public static string ButtonLabel(BrowseKinds kinds)
        => kinds == BrowseKinds.Both ? "Browse units & objects…" : "Browse…";

    /// <summary>One Browse… button in the C6 table. <see cref="Wired"/> false: deliberately not built (see Note).</summary>
    public sealed class Site
    {
        public string Where, Input, File;
        public BrowseKinds Kinds;
        public BrowseFormat Format;
        public bool Wired = true;
        public string Note = "";
    }

    /// <summary>The C6 table as data. Each format was checked against the command the field feeds.</summary>
    public static readonly Site[] Sites =
    {
        new() { Where = "Nyar unit list picker", Input = "NyarUnitPick", File = "MainPanel.NyarControls.cs", Kinds = BrowseKinds.Unit, Format = BrowseFormat.Name,
                Note = "Browse… beside the search box; the pick lands there, then Add as usual" },
        new() { Where = "Nyar pool member picker", Input = "NyarPoolPick", File = "MainPanel.NyarEditor.cs", Kinds = BrowseKinds.Unit, Format = BrowseFormat.Name,
                Note = "Browse… beside the search box; the pick lands there, then Add as usual" },
        new() { Where = "Nyar test spawn", Input = "NyarSpawnUnit", File = "MainPanel.NyarEditor.cs", Kinds = BrowseKinds.Unit, Format = BrowseFormat.Name,
                Note = "Browse… beside the search box; the pick lands there, then Add as usual" },
        new() { Where = "Beelz admin unit or GUID", Input = "BeelzAdminUnit", File = "MainPanel.Beelzebub.cs", Kinds = BrowseKinds.Unit, Format = BrowseFormat.Guid,
                Note = "give / devour / give-transform / force-transform take `int unitGuid` (Beelzebub AdminCommands.cs), so Guid, not Name" },
        new() { Where = "Beelz PrefabGUID filter", Input = "BeelzFiltGuid", File = "MainPanel.Beelzebub.cs", Kinds = BrowseKinds.Both, Format = BrowseFormat.Guid,
                Note = "denyguid / allowguid take `int guid`; abilities and items are not listed (tooltip says so)" },
        new() { Where = "Beelz transform unit", Input = "BeelzTxUnit", File = "MainPanel.Beelzebub.cs", Kinds = BrowseKinds.Unit, Format = BrowseFormat.Name,
                Note = "transform-set <CHAR_unit>" },
        new() { Where = "Beelz lock-group member", Input = "BeelzLockMembers", File = "MainPanel.Beelzebub.cs", Kinds = BrowseKinds.Unit, Format = BrowseFormat.Guid,
                Wired = false,
                Note = "not built: lock members are ABILITY names/IDs or cat:<Category> (AdminLockCommands.cs), and the box holds a comma list that a fill would overwrite" },
        new() { Where = "Uriel block prefab", Input = "UrielBlockPrefab", File = "MainPanel.Uriel.cs", Kinds = BrowseKinds.Object, Format = BrowseFormat.Name,
                Note = ".uriel block <guid|name>" },
        new() { Where = "Uriel config prefab", Input = "UrielCfgPrefab", File = "MainPanel.Uriel.cs", Kinds = BrowseKinds.Object, Format = BrowseFormat.Name,
                Note = ".uriel objcfg <prefab> …" },
        new() { Where = "Uriel event marker unit", Input = "UrielEvMarkerUnit", File = "MainPanel.UrielEvents.cs", Kinds = BrowseKinds.Unit, Format = BrowseFormat.ShortName,
                Note = "Castle Events take the unit without CHAR_ (the picker's own ShortUnit)" },
        new() { Where = "Uriel squad unit", Input = "UrielEvSqPicker", File = "MainPanel.UrielEvents.cs", Kinds = BrowseKinds.Unit, Format = BrowseFormat.ShortName },
        new() { Where = "Uriel unit name", Input = "UrielUnitName", File = "MainPanel.UrielEvents.cs", Kinds = BrowseKinds.Unit, Format = BrowseFormat.ShortName },
    };
}
