using System.Globalization;
using Raphael.Logic.Tests;
using Raphael.Resources;

// 0.74.0: the unit and object reference (PLAN 2026-10-24 Part C): the committed table (strict parse, hash, counts,
// uniqueness), the lenient runtime parse, normalisation, GUID reading, ranking, filters, flat and grouped paging, a
// late rename, the Browse formats and the UnitCatalog snapshot.
internal static partial class Program
{
    private static void GameCatalogTests()
    {
        CatalogCommitted();
        CatalogMalformed();
        CatalogText();
        CatalogQuery();
        CatalogRename();
        CatalogFlatPaging();
        CatalogGrouped();
        CatalogTabs();
        CatalogBrowseFormats();
        GameCatalog.UseTable(null);
        UnitCatalog.ResetCache();
        CatalogUnitSnapshot();
    }

    // ------------------------------------------------------------------ the committed table
    private static void CatalogCommitted()
    {
        GameCatalog.UseTable(null);
        var strict = GameCatalog.Parse(GameCatalogData.Table, strict: true);
        Eq(strict.Errors.Count, 0, "committed table parses strictly" + (strict.Errors.Count > 0 ? " (" + strict.Errors[0] + ")" : ""));
        Eq(GameCatalog.TableHash(GameCatalogData.Table), GameCatalogData.TableSha256, "table sha256 matches the header (no hand edits)");
        Eq(strict.Rows.Count, GameCatalogData.UnitRows + GameCatalogData.ObjectRows, "row count = units + objects in the header");
        Eq(strict.Rows.Count(r => r.IsUnit), GameCatalogData.UnitRows, "unit rows match the header");
        Eq(strict.Rows.Count(r => !r.IsUnit), GameCatalogData.ObjectRows, "object rows match the header");
        Eq(strict.Rows.Select(r => r.Guid).Distinct().Count(), strict.Rows.Count, "every GUID appears exactly once");
        Eq(strict.Rows.Select(r => r.InternalName).Distinct(StringComparer.Ordinal).Count(), strict.Rows.Count, "every internal name appears once");
        Check(strict.Rows.All(r => r.IsUnit == r.InternalName.StartsWith("CHAR_", StringComparison.Ordinal)), "units are exactly the CHAR_ rows");
        Check(strict.Rows.Where(r => !r.IsUnit).All(r => r.Group is "Castle" or "Resource" or "Tree" or "World"), "object groups are Castle/Resource/Tree/World");
        Check(strict.Rows.Where(r => !r.IsUnit).All(r => r.LocKey.Length == 0), "objects carry no name key (resolved in-game)");
        Check(strict.Rows.All(r => r.NameSource == NameSource.PrefabName && r.DisplayName.Length > 0), "every row starts with a readable fallback name");

        // per group and sub-group counts against the header's Counts table
        var expected = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in GameCatalogData.Counts.Replace("\r", "").Split('\n'))
        {
            if (line.Length == 0) continue;
            var f = line.Split('\t');
            Check(f.Length == 4 && int.TryParse(f[3], out _), $"Counts line '{line}' has 4 columns");
            if (f.Length == 4) expected[f[0] + "|" + f[1] + "|" + f[2]] = int.Parse(f[3], CultureInfo.InvariantCulture);
        }
        var actual = strict.Rows.GroupBy(r => (r.IsUnit ? "U" : "O") + "|" + r.Group + "|" + r.SubGroup)
                                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        Eq(actual.Count, expected.Count, "number of (kind, group, sub-group) buckets matches the header");
        foreach (var kv in expected)
            Eq(actual.TryGetValue(kv.Key, out var n) ? n : -1, kv.Value, $"count for {kv.Key}");
        Eq(expected.Values.Sum(), strict.Rows.Count, "header bucket counts add up to the row count");

        // runtime (lenient) view of the same table
        Eq(GameCatalog.Count, strict.Rows.Count, "runtime parse keeps every committed row");
        Eq(GameCatalog.SkippedRows, 0, "runtime parse skips nothing in the committed table");
        Check(GameCatalog.ByGuid(-1301144178)?.InternalName == "CHAR_Bandit_Hunter", "ByGuid finds CHAR_Bandit_Hunter");
        Check(GameCatalog.ByGuid(-1301144178)?.LocKey.Length == 36, "units carry their BloodCraftUI name key");
    }

    // ------------------------------------------------------------------ malformed rows, both modes
    private static void CatalogMalformed()
    {
        string good1 = "U\t1\tCHAR_A_One\tBandits\tA\t10\tv\t\n";
        string good2 = "O\t2\tTM_Castle_Floor_X\tCastle\tFloor\t0\tb\t\n";
        string[] bad =
        {
            "U\t3\tCHAR_Seven\tBandits\tA\t1\tv\n",              // 7 columns
            "U\t4\tCHAR_Nine\tBandits\tA\t1\tv\t\textra\n",     // 9 columns
            "X\t5\tCHAR_Kind\tBandits\tA\t1\t\t\n",             // unknown kind
            "U\t6x\tCHAR_Guid\tBandits\tA\t1\t\t\n",            // bad GUID
            "U\t99999999999\tCHAR_Big\tBandits\tA\t1\t\t\n",    // GUID overflow
            "U\t7\tCHAR_Level\tBandits\tA\t-1\t\t\n",           // negative level
            "U\t8\tCHAR_Flag\tBandits\tA\t1\tz\t\n",            // unknown flag
            "U\t9\t\tBandits\tA\t1\t\t\n",                      // empty name
            "U\t1\tCHAR_Dupe\tBandits\tA\t1\t\t\n",             // duplicate GUID
        };
        string table = good1 + string.Concat(bad) + good2;
        var strict = GameCatalog.Parse(table, strict: true);
        Eq(strict.Errors.Count, bad.Length, "strict parse reports every malformed row");
        var lenient = GameCatalog.Parse(table, strict: false);
        Eq(lenient.Skipped, bad.Length, "lenient parse skips and counts every malformed row");
        Eq(lenient.Rows.Count, 2, "lenient parse still loads the good rows");
        Eq(lenient.Rows[0].Guid, 1, "the first of two duplicate GUIDs is kept");
        var crlf = GameCatalog.Parse(good1.Replace("\n", "\r\n") + good2.Replace("\n", "\r\n"), strict: true);
        Eq(crlf.Errors.Count, 0, "CRLF line endings parse");
        Eq(GameCatalog.TableHash(good1 + good2), GameCatalog.TableHash((good1 + good2).Replace("\n", "\r\n")), "table hash ignores CR");
        Eq(GameCatalog.Parse("", strict: true).Errors.Count, 0, "empty input is not a crash");

        string warned = null;
        GameCatalog.Warn = m => warned = m;
        GameCatalog.UseTable(table);
        Eq(GameCatalog.Count, 2, "runtime catalogue loads past bad rows");
        Eq(GameCatalog.SkippedRows, bad.Length, "runtime catalogue counts the skipped rows");
        Check(warned != null && warned.Contains(bad.Length.ToString(CultureInfo.InvariantCulture)), "one warning names the skipped count");
        GameCatalog.Warn = null;
        GameCatalog.UseTable(null);
    }

    // ------------------------------------------------------------------ names, normalisation, GUIDs
    private static void CatalogText()
    {
        Eq(GameCatalog.ReadableName("TM_Castle_Container_Bookshelf_Base_6x2"), "Castle Container Bookshelf Base 6x2", "readable name (plan example)");
        Eq(GameCatalog.ReadableName("CHAR_Bandit_Hunter"), "Bandit Hunter", "readable unit name");
        Eq(GameCatalog.ReadableName("Trees__Pine__01"), "Pine 01", "readable name collapses double underscores");
        Eq(GameCatalog.ReadableName("NoPrefix"), "NoPrefix", "readable name without a known prefix");
        Eq(GameCatalog.Normalize("  CHAR_Bandit-Hunter   VBlood "), "char bandit hunter vblood", "normalise: lower, _ and - to space, collapse");

        Check(GameCatalog.TryParseGuid("4294967295", out int g) && g == -1, "4294967295 reads as -1");
        Check(GameCatalog.TryParseGuid("-1", out g) && g == -1, "-1 reads as -1");
        Check(GameCatalog.TryParseGuid("2147483648", out g) && g == int.MinValue, "2147483648 reads as int.MinValue");
        Check(GameCatalog.TryParseGuid(" 42 ", out g) && g == 42, "spaces around a number");
        Check(!GameCatalog.TryParseGuid("4294967296", out _), "4294967296 is not a 32-bit value");
        Check(!GameCatalog.TryParseGuid("-2147483649", out _), "-2147483649 is not a 32-bit value");
        Check(!GameCatalog.TryParseGuid("12a", out _), "12a is not a number");
        Check(!GameCatalog.TryParseGuid("", out _), "empty is not a number");
    }

    private static string SampleTable() =>
        "U\t-1\tCHAR_Wolf_Alpha_VBlood\tWolves\tWolf\t16\tv\t\n" +
        "U\t-1301144178\tCHAR_Bandit_Hunter\tBandits\tBandit\t16\t\t\n" +
        "U\t-301730941\tCHAR_Bandit_Thug\tBandits\tBandit\t14\t\t\n" +
        "U\t100\tCHAR_Bandit_Leader_Wolf_Summon\tBandits\tBandit\t4\tm\t\n" +
        "U\t200\tCHAR_Hunter_Bandit_Friend\tBandits\tHunter\t5\t\t\n" +
        "O\t300\tTM_Castle_Container_Bookshelf_Base_6x2\tCastle\tContainer\t0\tbc\t\n" +
        "O\t301\tTM_Castle_Floor_Stone\tCastle\tFloor\t0\tb\t\n" +
        "O\t302\tResource_Drop_Bandit\tResource\tDrop\t0\tc\t\n" +
        "O\t303\tTrees_Pine01\tTree\tPine\t0\t\t\n" +
        "O\t304\tChain_Barrel_Bandit\tWorld\tBarrel\t0\t\t\n";

    // ------------------------------------------------------------------ ranking and filters
    private static void CatalogQuery()
    {
        GameCatalog.UseTable(SampleTable());
        List<string> Q(string t, CatalogKind? k = null, string grp = null, string sub = null, CatalogFlags f = CatalogFlags.None)
            => GameCatalog.Query(t, k, grp, sub, f).Select(r => r.InternalName).ToList();

        Eq(string.Join(",", Q("hunter bandit")), "CHAR_Hunter_Bandit_Friend,CHAR_Bandit_Hunter", "AND words in any order; a display-name prefix ranks first");
        Eq(string.Join(",", Q("bandit hunter")), "CHAR_Bandit_Hunter,CHAR_Hunter_Bandit_Friend", "the other word order puts the other prefix first");
        Eq(string.Join(",", Q("hunter")), "CHAR_Hunter_Bandit_Friend,CHAR_Bandit_Hunter", "prefix first, then the rest by display name");
        Eq(Q("hunter bandit friend").Single(), "CHAR_Hunter_Bandit_Friend", "every word must match");
        Eq(Q("CHAR_Bandit_Thug").First(), "CHAR_Bandit_Thug", "exact internal name first");
        Eq(Q("char_bandit_thug").First(), "CHAR_Bandit_Thug", "exact internal name, any case");
        Eq(Q("-1301144178").First(), "CHAR_Bandit_Hunter", "exact signed GUID first");
        Eq(Q("2993823118").First(), "CHAR_Bandit_Hunter", "unsigned GUID finds the signed row");
        Eq(Q("4294967295").First(), "CHAR_Wolf_Alpha_VBlood", "4294967295 finds GUID -1");
        Eq(Q("-1").First(), "CHAR_Wolf_Alpha_VBlood", "-1 ranks GUID -1 first");
        Check(Q("130114").Contains("CHAR_Bandit_Hunter"), "partial GUID digits match");
        Eq(Q("bandits").Count, 4, "group text is searchable");
        Eq(Q("container").Count, 1, "sub-group text is searchable");

        Eq(Q("", CatalogKind.Unit).Count, 5, "kind filter: units");
        Eq(Q("", CatalogKind.Object).Count, 5, "kind filter: objects");
        Eq(Q("", null).Count, 10, "no filter: everything");
        Eq(Q("", CatalogKind.Unit, "Bandits").Count, 4, "faction filter");
        Eq(Q("", CatalogKind.Object, "Castle", "Floor").Single(), "TM_Castle_Floor_Stone", "group + sub-group filter");
        Eq(Q("", CatalogKind.Unit, f: CatalogFlags.VBlood).Single(), "CHAR_Wolf_Alpha_VBlood", "V Blood flag");
        Eq(Q("", CatalogKind.Unit, f: CatalogFlags.Minion).Single(), "CHAR_Bandit_Leader_Wolf_Summon", "minion flag");
        Eq(Q("", CatalogKind.Object, f: CatalogFlags.Buildable).Count, 2, "buildable flag");
        Eq(Q("", CatalogKind.Object, f: CatalogFlags.Container | CatalogFlags.Buildable).Single(), "TM_Castle_Container_Bookshelf_Base_6x2", "flags combine (all must be set)");
        Eq(string.Join(",", Q("", CatalogKind.Unit)),
           "CHAR_Bandit_Hunter,CHAR_Bandit_Leader_Wolf_Summon,CHAR_Bandit_Thug,CHAR_Hunter_Bandit_Friend,CHAR_Wolf_Alpha_VBlood",
           "empty query sorts by display name");

        // Stable ties: two rows with the same display name order by internal name.
        GameCatalog.UseTable("U\t1\tCHAR_B\tX\tB\t0\t\t\nU\t2\tCHAR_A\tX\tA\t0\t\t\n");
        GameCatalog.SetResolvedName(GameCatalog.ByGuid(1), "Same");
        GameCatalog.SetResolvedName(GameCatalog.ByGuid(2), "Same");
        Eq(string.Join(",", Q("same")), "CHAR_A,CHAR_B", "equal display names tie-break on internal name");
        GameCatalog.UseTable(null);
    }

    // ------------------------------------------------------------------ a late rename changes hits and order
    private static void CatalogRename()
    {
        GameCatalog.UseTable(SampleTable());
        var view = new GameCatalogView { Tab = CatalogTab.Units, Text = "alpha" };
        Eq(view.Total, 1, "before the rename 'alpha' finds the wolf by its prefab name");
        view.Text = "white";
        Eq(view.Total, 0, "before the rename 'white' finds nothing");
        Check(!view.NeedsRefresh, "view is up to date");
        var wolf = GameCatalog.ByGuid(-1);
        Check(GameCatalog.SetResolvedName(wolf, "Alpha the <b>White</b> Wolf"), "a resolved name is accepted");
        Eq(wolf.DisplayName, "Alpha the White Wolf", "rich text is stripped from a game name");
        Eq(wolf.NameSource, NameSource.GameText, "NameSource records the game text");
        Check(!GameCatalog.SetResolvedName(wolf, "  "), "an empty game name is refused");
        int rev = GameCatalog.Revision;
        GameCatalog.BumpRevision();
        Check(GameCatalog.Revision == rev + 1 && view.NeedsRefresh, "a batch bumps the revision and the open view notices");
        Eq(view.Total, 1, "after the rename 'white' finds the wolf");
        view.Text = "";
        Eq(view.Results[0].InternalName, "CHAR_Wolf_Alpha_VBlood", "the renamed wolf sorts first (Alpha… before Bandit…)");
        GameCatalog.SetResolvedName(wolf, "Zed the Wolf");
        GameCatalog.BumpRevision();
        Eq(view.Results[view.Results.Count - 1].InternalName, "CHAR_Wolf_Alpha_VBlood", "a second rename moves it to the end");
        GameCatalog.UseTable(null);
    }

    private static string ManyRows(int units, string faction = "Bandits", int startGuid = 1000)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < units; i++)
            sb.Append($"U\t{startGuid + i}\tCHAR_{faction}_{i:D4}\t{faction}\t{faction}\t1\t\t\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ flat paging
    private static void CatalogFlatPaging()
    {
        GameCatalog.UseTable(ManyRows(130));
        var v = new GameCatalogView { Tab = CatalogTab.Units };
        Eq(v.Total, 130, "130 rows");
        Eq(v.PageCount, 3, "pages of 60: 3 pages");
        Eq(v.PageRows().Count, 60, "page 1 holds 60");
        Eq(v.PageRows()[0].InternalName, "CHAR_Bandits_0000", "page 1 starts at the first row");
        Check(!v.CanPrev && v.CanNext, "page 1: Next only");
        v.NextPage(); v.NextPage();
        Eq(v.Page, 2, "page 3 reached");
        Eq(v.PageRows().Count, 10, "the last page holds the remaining 10");
        Eq(v.PageRows()[0].InternalName, "CHAR_Bandits_0120", "page 3 starts at row 121");
        v.NextPage();
        Eq(v.Page, 2, "Next on the last page stays");
        v.Text = "0005";
        Eq(v.Page, 0, "a new search goes back to page 1");
        Eq(v.Total, 1, "search narrows the pages");
        GameCatalog.BumpRevision();
        v.Text = "";
        v.NextPage();
        GameCatalog.BumpRevision();   // names resolved: the page is kept
        Eq(v.Page, 1, "a name batch keeps the current page");
        GameCatalog.UseTable(null);
    }

    // ------------------------------------------------------------------ grouped mode
    private static void CatalogGrouped()
    {
        GameCatalog.UseTable(ManyRows(300, "Wolves", 1000) + ManyRows(100, "Bandits", 5000) + ManyRows(5, "Undead", 9000));
        var v = new GameCatalogView { Tab = CatalogTab.Units, GroupBy = true };
        Eq(string.Join(",", v.Groups.Select(g => g.Title)), "Bandits,Undead,Wolves", "groups in order by name");
        Eq(string.Join(",", v.Groups.Select(g => g.Total)), "100,5,300", "each header shows the group's total");
        Eq(v.OpenGroup, null, "groups start folded");
        Eq(v.OpenRows().Count, 0, "no rows while every group is folded");

        v.ToggleGroup("U|Wolves");
        Eq(v.OpenGroup, "U|Wolves", "open Wolves");
        Eq(v.OpenRows().Count, 60, "an open group shows 60");
        Check(v.CanShowMore && !v.MustRefine, "Show 60 more is offered");
        v.ShowMore(); Eq(v.OpenRows().Count, 120, "show more: 120");
        v.ShowMore(); Eq(v.OpenRows().Count, 180, "show more: 180");
        v.ShowMore(); Eq(v.OpenRows().Count, 240, "show more: 240");
        Check(!v.CanShowMore, "no Show more past 240");
        Check(v.MustRefine, "300 matches past 240: refine your search");
        v.ShowMore(); Eq(v.OpenRows().Count, 240, "never more than 240 row controls");
        Eq(v.OpenRows()[0].InternalName, "CHAR_Wolves_0000", "the open group's rows are sorted");

        v.ToggleGroup("U|Bandits");
        Eq(v.OpenGroup, "U|Bandits", "opening Bandits closes Wolves (one open at a time)");
        Eq(v.OpenRows().Count, 60, "the newly opened group starts at 60");
        v.ShowMore();
        Eq(v.OpenRows().Count, 100, "show more stops at the group's total");
        Check(!v.CanShowMore && !v.MustRefine, "a 100-row group needs no refine");
        v.ToggleGroup("U|Bandits");
        Eq(v.OpenGroup, null, "clicking the open group folds it");

        v.ToggleGroup("U|Undead");
        v.Text = "wolves";
        Eq(v.OpenGroup, null, "the open group closes when the search drops it");
        Eq(v.Groups.Count, 1, "search filters before grouping");

        // All tab: by kind first, then category.
        GameCatalog.UseTable(SampleTable());
        var a = new GameCatalogView { Tab = CatalogTab.All, GroupBy = true };
        Eq(string.Join(",", a.Groups.Select(g => g.Title)),
           "Units · Bandits,Units · Wolves,Objects · Castle,Objects · Resource,Objects · Tree,Objects · World",
           "All tab groups by kind, then faction or category");
        GameCatalog.UseTable(null);
    }

    // ------------------------------------------------------------------ tabs and kind-neutral filters
    private static void CatalogTabs()
    {
        GameCatalog.UseTable(SampleTable());
        var v = new GameCatalogView { Tab = CatalogTab.Units, VBloodOnly = true };
        Eq(v.Total, 1, "Units tab with V Blood only");
        v.Tab = CatalogTab.All;
        Eq(v.Total, 10, "the All tab ignores the unit and object filters");
        v.Tab = CatalogTab.Objects;
        v.ObjectGroup = "Castle";
        v.ObjectSubGroup = "Floor";
        Eq(v.Total, 1, "object group + sub-group");
        v.ObjectGroup = "Resource";
        Eq(v.ObjectSubGroup, "", "changing the group clears the sub-group");
        Eq(string.Join(",", GameCatalogView.SubGroupsFor("Castle")), "Container,Floor", "sub-groups for a category");
        Eq(GameCatalogView.SubGroupsFor("").Count, 0, "no sub-groups without a category");
        Eq(string.Join(",", GameCatalogView.Factions()), "Bandits,Wolves", "faction drop-down options");
        Eq(string.Join(",", GameCatalogView.ObjectGroups()), "Castle,Resource,Tree,World", "category drop-down options");

        var b = new GameCatalogView { Tab = CatalogTab.All };
        b.AllowedKinds = BrowseKinds.Object;
        Eq(b.Tab, CatalogTab.Objects, "a Browse target for objects pins the Objects tab");
        b.Tab = CatalogTab.Units;
        Eq(b.Tab, CatalogTab.Objects, "a disallowed tab cannot be chosen");
        Check(!b.TabAllowed(CatalogTab.All), "All is offered only when both kinds are allowed");
        b.AllowedKinds = BrowseKinds.Both;
        b.Tab = CatalogTab.All;
        Eq(b.Tab, CatalogTab.All, "both kinds allow the All tab");
        b.ResetFilters("thug");
        Eq(b.Total, 1, "ResetFilters applies the starting filter");

        Check(GameCatalogView.Detail(GameCatalog.ByGuid(-1)).Contains("V Blood"), "unit detail shows V Blood");
        Check(GameCatalogView.Detail(GameCatalog.ByGuid(-1)).Contains("L16"), "unit detail shows the level");
        Check(GameCatalogView.Detail(GameCatalog.ByGuid(300)).Contains("Castle / Container"), "object detail shows category / sub-group");
        GameCatalog.UseTable(null);
    }

    // ------------------------------------------------------------------ Browse… formats (the C6 table as data)
    private static void CatalogBrowseFormats()
    {
        GameCatalog.UseTable(SampleTable());
        var unit = GameCatalog.ByGuid(-1301144178);
        var obj = GameCatalog.ByGuid(300);
        Eq(CatalogBrowse.Value(unit, BrowseFormat.Name), "CHAR_Bandit_Hunter", "Name writes the internal name");
        Eq(CatalogBrowse.Value(unit, BrowseFormat.ShortName), "Bandit_Hunter", "ShortName drops CHAR_");
        Eq(CatalogBrowse.Value(unit, BrowseFormat.Guid), "-1301144178", "Guid writes the signed GUID");
        Eq(CatalogBrowse.Value(obj, BrowseFormat.ShortName), "TM_Castle_Container_Bookshelf_Base_6x2", "ShortName leaves non-unit names alone");
        Eq(CatalogBrowse.Value(null, BrowseFormat.Name), "", "no row, no text");
        Check(CatalogBrowse.Allows(BrowseKinds.Unit, CatalogKind.Unit) && !CatalogBrowse.Allows(BrowseKinds.Unit, CatalogKind.Object), "unit targets take units only");
        Check(CatalogBrowse.Allows(BrowseKinds.Both, CatalogKind.Object), "both-kind targets take objects");
        Eq(CatalogBrowse.StartTab(BrowseKinds.Object), CatalogTab.Objects, "object target opens on Objects");
        Eq(CatalogBrowse.StartTab(BrowseKinds.Both), CatalogTab.All, "both-kind target opens on All");
        Eq(CatalogBrowse.ButtonLabel(BrowseKinds.Both), "Browse units & objects…", "both-kind button label");

        Eq(CatalogBrowse.Sites.Length, 12, "the C6 table has 12 rows");
        Eq(CatalogBrowse.Sites.Select(s => s.Input).Distinct().Count(), 12, "each site names its own input");
        foreach (var s in CatalogBrowse.Sites)
        {
            var sample = CatalogBrowse.Allows(s.Kinds, CatalogKind.Unit) ? unit : obj;
            string v = CatalogBrowse.Value(sample, s.Format);
            string want = s.Format switch
            {
                BrowseFormat.Guid => sample.Guid.ToString(CultureInfo.InvariantCulture),
                BrowseFormat.ShortName => "Bandit_Hunter",
                _ => sample.InternalName,
            };
            Eq(v, want, $"{s.Where} writes {s.Format}");
            Check(s.Wired || s.Note.Length > 0, $"{s.Where}: an unbuilt site says why");
        }
        Eq(CatalogBrowse.Sites.Single(s => s.Input == "BeelzAdminUnit").Format, BrowseFormat.Guid, "Beelz admin unit takes an int unitGuid");
        Check(CatalogBrowse.Sites.Where(s => s.File == "MainPanel.UrielEvents.cs").All(s => s.Format == BrowseFormat.ShortName), "Uriel Castle Events take ShortName");
        Check(CatalogBrowse.Sites.Where(s => s.File == "MainPanel.Uriel.cs").All(s => s.Kinds == BrowseKinds.Object), "Uriel prefab fields take objects");
        GameCatalog.UseTable(null);
    }

    // ------------------------------------------------------------------ UnitCatalog keeps its behaviour (C7)
    private static void CatalogUnitSnapshot()
    {
        var now = UnitCatalog.All;
        var snap = UnitCatalogSnapshot.Rows;
        Eq(now.Length, snap.Length, "UnitCatalog.All keeps the 426 units of 0.73.0");
        Eq(string.Join("\n", now.Select(u => u.Name)), string.Join("\n", snap.Select(u => u.Name)), "UnitCatalog.All: same names, same order");
        int diff = 0;
        string first = null;
        for (int i = 0; i < Math.Min(now.Length, snap.Length); i++)
            if (now[i] != snap[i]) { diff++; first ??= $"{now[i]} vs {snap[i]}"; }
        Eq(diff, 0, "UnitCatalog.All: same faction, level, V Blood and minion per unit" + (first != null ? " (first: " + first + ")" : ""));

        var hits = UnitCatalog.Search("bandit hunter", 40);
        Check(hits.Count > 0 && hits[0].Value == "CHAR_Bandit_Hunter", "Search('bandit hunter') finds CHAR_Bandit_Hunter first");
        Check(hits.Count > 0 && hits[0].Display == "Bandit Hunter  <color=#888888>(Bandits · L16)</color>", "Search keeps its labels");
        Check(UnitCatalog.Search("vblood", 500).All(h => h.Display.Contains("V Blood")), "'vblood' finds only V Bloods");
        Eq(UnitCatalog.Search("", 40).Count, 0, "empty search returns nothing");
        Eq(UnitCatalog.Search("bandit", 3).Count, 3, "Search honours max");
    }
}
