using System.Globalization;
using System.Text.Json;
using Raphael.Services.Guides;

// 0.73.0: the in-game guides checked against the manifests generated from each mod's source
// (tools/guide-commands.py → tests/fixtures/<mod>-commands.json / -config.json). PLAN.md §3 "Accuracy gate":
//   (a) every guide command exists in its manifest, with the same admin flag and a real way to type it;
//   (b) every manifest command appears in a guide or in the guide's allowlist (tests/fixtures/guide-allowlist/<id>.json);
//   (c) a row's <required> / [optional] arguments match the method signature, unless it carries a SyntaxOverride;
//   (d) every setting appears in a settings table with the manifest default, or is allowlisted;
// plus the page schema per guide kind and the text lint (escaped placeholders, no missing glyphs, no backticks).
internal static partial class Program
{
    private sealed class ManifestCmd
    {
        public string Key, Group, GroupShort, Name, ShortHand, Usage;
        public bool Admin, VerbDispatch;
        public List<(string Name, bool Optional)> Params = new();
    }

    private sealed class ManifestSetting { public string Section, Key; public JsonElement Default; }

    private static string FixturesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures"))) dir = dir.Parent;
        return dir == null ? null : Path.Combine(dir.FullName, "tests", "fixtures");
    }

    private static string Key(string group, string name) => string.IsNullOrEmpty(group) ? name : group + " " + name;

    private static List<ManifestCmd> LoadCommands(string fixtures, string mod)
    {
        var list = new List<ManifestCmd>();
        var path = Path.Combine(fixtures, mod + "-commands.json");
        if (!File.Exists(path)) { Check(false, $"manifest {mod}-commands.json exists"); return list; }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var c in doc.RootElement.GetProperty("commands").EnumerateArray())
        {
            var m = new ManifestCmd
            {
                Group = Str(c, "group"), GroupShort = Str(c, "groupShort"), Name = Str(c, "name"),
                ShortHand = Str(c, "shortHand"), Usage = Str(c, "usage"),
                Admin = c.GetProperty("adminOnly").GetBoolean(), VerbDispatch = c.GetProperty("verbDispatch").GetBoolean(),
            };
            m.Key = Key(m.Group, m.Name);
            foreach (var p in c.GetProperty("params").EnumerateArray())
                m.Params.Add((p.GetProperty("name").GetString(), p.GetProperty("optional").GetBoolean()));
            list.Add(m);
        }
        return list;
    }

    private static (List<ManifestSetting> settings, List<string> unresolved) LoadSettings(string fixtures, string mod)
    {
        var list = new List<ManifestSetting>();
        var unresolved = new List<string>();
        var path = Path.Combine(fixtures, mod + "-config.json");
        if (!File.Exists(path)) { Check(false, $"manifest {mod}-config.json exists"); return (list, unresolved); }
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var s in doc.RootElement.GetProperty("settings").EnumerateArray())
            list.Add(new ManifestSetting { Section = Str(s, "section"), Key = Str(s, "key"), Default = s.GetProperty("default").Clone() });
        if (doc.RootElement.TryGetProperty("unresolvedBinds", out var u))
            foreach (var x in u.EnumerateArray()) unresolved.Add(x.GetString());
        return (list, unresolved);
    }

    private static string Str(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private sealed class Allowlist
    {
        public readonly List<(string Mod, string Key, string Reason)> Commands = new();
        public readonly List<(string Mod, string Section, string Key, string Reason)> Settings = new();
        public readonly List<(string Mod, string Match, string Reason)> Binds = new();
    }

    private static Allowlist LoadAllowlist(string fixtures, string guideId)
    {
        var a = new Allowlist();
        var path = Path.Combine(fixtures, "guide-allowlist", guideId + ".json");
        if (!File.Exists(path)) return a;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.TryGetProperty("commands", out var cs))
            foreach (var c in cs.EnumerateArray())
            {
                var reason = Str(c, "reason");
                Check(!string.IsNullOrWhiteSpace(reason), $"allowlist {guideId}: command {Str(c, "key")} has a reason");
                a.Commands.Add((Str(c, "mod"), Str(c, "key"), reason));
            }
        if (root.TryGetProperty("settings", out var ss))
            foreach (var s in ss.EnumerateArray())
            {
                var reason = Str(s, "reason");
                Check(!string.IsNullOrWhiteSpace(reason), $"allowlist {guideId}: setting {Str(s, "key")} has a reason");
                a.Settings.Add((Str(s, "mod"), Str(s, "section"), Str(s, "key"), reason));
            }
        if (root.TryGetProperty("unresolvedBinds", out var bs))
            foreach (var b in bs.EnumerateArray())
                a.Binds.Add((Str(b, "mod"), Str(b, "match"), Str(b, "reason")));
        return a;
    }

    private static void GuideChecks()
    {
        var fixtures = FixturesDir();
        Check(fixtures != null, "tests/fixtures found");
        if (fixtures == null) return;

        IReadOnlyList<Guide> all;
        try { all = Guides.All; }
        catch (Exception ex) { Check(false, $"guides build without throwing: {ex}"); return; }

        // GUIDE_ONLY=<id> checks one guide (and only the manifests it claims): for writing a guide while others are
        // still in progress. Release preflight never sets it.
        var only = Environment.GetEnvironmentVariable("GUIDE_ONLY");
        if (!string.IsNullOrEmpty(only))
        {
            all = all.Where(g => g.Id == only).ToList();
            Check(all.Count == 1, $"GUIDE_ONLY={only} names a guide");
        }

        var ids = new HashSet<string>();
        foreach (var g in all)
        {
            Check(ids.Add(g.Id), $"guide id {g.Id} is unique");
            GuideSchemaCheck(g);
            GuideLint(g);
        }
        GuideMigrationChecks(fixtures, all);

        // Commands + settings per manifest mod, across every guide that claims it.
        var mods = all.SelectMany(g => g.ManifestMods).Distinct().ToList();
        foreach (var mod in mods)
        {
            var manifest = LoadCommands(fixtures, mod);
            var byKey = manifest.GroupBy(m => m.Key).ToDictionary(x => x.Key, x => x.ToList());
            var guides = all.Where(g => g.ManifestMods.Contains(mod)).ToList();
            var rows = guides.SelectMany(g => Guides.AllBlocks(g).OfType<GCommands>().SelectMany(c => c.Rows).Select(r => (g, r)))
                             .Where(x => x.r.Mod == mod).ToList();
            var allow = guides.SelectMany(g => LoadAllowlist(fixtures, g.Id).Commands).Where(a => a.Mod == mod).ToList();

            // (a) + (c)
            foreach (var (g, r) in rows)
            {
                string where = $"{g.Id}: {r.Syntax}";
                if (!byKey.TryGetValue(r.Key ?? "", out var matches)) { Check(false, $"{where}: key '{r.Key}' is in {mod}'s manifest"); continue; }
                var m = matches[0];
                Check((r.Scope == CmdScope.Admin) == m.Admin, $"{where}: scope {r.Scope} matches adminOnly={m.Admin}");
                Check(InvocationOk(r.Syntax, matches), $"{where}: starts with a real invocation ({string.Join(" / ", Invocations(m))})");
                if (m.VerbDispatch && r.SyntaxOverride == null)
                    Check(false, $"{where}: verb-dispatch command needs a SyntaxOverride naming its verbs' source");
                if (r.SyntaxOverride == null && !m.VerbDispatch)
                {
                    var got = GuideSyntax.Args(r.Syntax);
                    bool any = matches.Any(mm => ArgsMatch(got, mm.Params));
                    Check(any, $"{where}: arguments {string.Join(" ", got)} match the signature " +
                               $"{string.Join(" ", m.Params.Select(p => p.Optional ? $"[{p.Name}]" : $"<{p.Name}>"))}");
                }
            }
            // (e) 0.73.0 post-build review: a command whose verb wipes or removes data shows the destructive warning.
            foreach (var (g, r) in rows)
            {
                var verbs = (r.Syntax ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(2)
                    .Where(t => !t.StartsWith("<") && !t.StartsWith("[")).Select(t => t.ToLowerInvariant());
                if (verbs.Any(v => DestructiveVerbs.Contains(v)) && !NotDestructive.Contains((r.Syntax ?? "").Trim()))
                    Check(r.Destructive, $"{g.Id}: {r.Syntax}: a wiping verb is marked Destructive");
            }
            // (b)
            var documented = new HashSet<string>(rows.Select(x => x.r.Key));
            foreach (var key in byKey.Keys)
                if (!documented.Contains(key) && !allow.Any(a => a.Key == key))
                    Check(false, $"{mod}: command '{key}' is in a guide or the allowlist");
            foreach (var a in allow)
            {
                Check(byKey.ContainsKey(a.Key ?? ""), $"{mod}: allowlisted command '{a.Key}' exists in the manifest");
                Check(!documented.Contains(a.Key), $"{mod}: allowlisted command '{a.Key}' isn't also documented");
            }

            // (d)
            var (settings, unresolved) = LoadSettings(fixtures, mod);
            var setRows = guides.SelectMany(g => Guides.AllBlocks(g).OfType<GSettings>().SelectMany(s => s.Rows)).ToList();
            var allowSet = guides.SelectMany(g => LoadAllowlist(fixtures, g.Id).Settings).Where(a => a.Mod == mod).ToList();
            var allowBinds = guides.SelectMany(g => LoadAllowlist(fixtures, g.Id).Binds).Where(a => a.Mod == mod).ToList();
            foreach (var s in settings)
            {
                var row = setRows.FirstOrDefault(r => r.Section == s.Section && r.Key == s.Key);
                if (row == null)
                {
                    if (!allowSet.Any(a => a.Section == s.Section && a.Key == s.Key))
                        Check(false, $"{mod}: setting [{s.Section}] {s.Key} is in a guide's settings or the allowlist");
                    continue;
                }
                Check(DefaultMatches(row.Default, s.Default),
                    $"{mod}: [{s.Section}] {s.Key} default '{row.Default}' matches the source ({s.Default})");
            }
            foreach (var r in setRows.Where(r => !r.Section.Contains('<')))
                if (settings.Count > 0 && !settings.Any(s => s.Section == r.Section && s.Key == r.Key))
                    Check(false, $"{mod}: guide setting [{r.Section}] {r.Key} exists in the source");
            foreach (var u in unresolved)
                Check(allowBinds.Any(b => b.Match != null && u.Contains(b.Match) && !string.IsNullOrWhiteSpace(b.Reason)),
                    $"{mod}: loop-built setting '{u}' is explained in the allowlist");
        }
    }

    private static readonly HashSet<string> DestructiveVerbs = new(StringComparer.OrdinalIgnoreCase)
        { "purge", "delete", "del", "wipe", "reset", "kill", "destroy", "forcepurge" };
    // Commands whose verb reads as a wipe but which delete nothing (the guide says so on the row).
    private static readonly HashSet<string> NotDestructive = new(StringComparer.OrdinalIgnoreCase) { ".fam reset" };

    private static IEnumerable<string> Invocations(ManifestCmd m)
    {
        var groups = new[] { m.Group, m.GroupShort }.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var names = new[] { m.Name, m.ShortHand }.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        if (groups.Count == 0) foreach (var n in names) yield return "." + n;
        foreach (var gname in groups) foreach (var n in names) yield return "." + gname + " " + n;
    }

    private static bool InvocationOk(string syntax, List<ManifestCmd> matches)
    {
        if (string.IsNullOrEmpty(syntax)) return false;
        foreach (var m in matches)
            foreach (var inv in Invocations(m))
                if (syntax.Equals(inv, StringComparison.OrdinalIgnoreCase) ||
                    syntax.StartsWith(inv + " ", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool ArgsMatch(List<GuideSyntax.Arg> got, List<(string Name, bool Optional)> want)
    {
        if (got.Count != want.Count) return false;
        for (int i = 0; i < got.Count; i++)
        {
            if (got[i].Optional != want[i].Optional) return false;
            if (GuideSyntax.Norm(got[i].Name) != GuideSyntax.Norm(want[i].Name)) return false;
        }
        return true;
    }

    private static bool DefaultMatches(string shown, JsonElement source)
    {
        shown = (shown ?? "").Trim();
        switch (source.ValueKind)
        {
            case JsonValueKind.True: return shown.Equals("true", StringComparison.OrdinalIgnoreCase);
            case JsonValueKind.False: return shown.Equals("false", StringComparison.OrdinalIgnoreCase);
            case JsonValueKind.Number:
                return double.TryParse(shown, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                       && Math.Abs(d - source.GetDouble()) < 1e-9;
            case JsonValueKind.String:
                var s = source.GetString() ?? "";
                if (s.StartsWith("expr:")) return shown.Length > 0;   // computed default: any stated value
                if (s.Length == 0) return shown.Length == 0 || shown == "(empty)";
                return shown == s;
            default: return false;
        }
    }

    private static void GuideSchemaCheck(Guide g)
    {
        Check(!string.IsNullOrWhiteSpace(g.Title) && !string.IsNullOrWhiteSpace(g.Purpose), $"{g.Id}: has a title and purpose");
        Check(g.Sections.Count > 0, $"{g.Id}: has sections");
        var want = GuideSchema.For(g.Kind);
        if (want == null) return;
        var got = g.Sections.Select(s => s.Key).ToArray();
        Check(got.SequenceEqual(want), $"{g.Id}: sections {string.Join(",", got)} follow the {g.Kind} schema {string.Join(",", want)}");
        Check(g.Sections.Count > 0 && g.Sections[0].Open, $"{g.Id}: the first section is open");
        if (g.Kind == GuideKind.FullMod)
            Check(g.Sections.FirstOrDefault(s => s.Key == GuideSchema.QuickStart)?.Open == true, $"{g.Id}: Quick start is open");
        Check(g.ManifestMods.Length > 0, $"{g.Id}: names its manifest(s)");
        if (g.Kind == GuideKind.FullMod) GuideServerSideCheck(g);
    }

    // 0.74.0 (owner's request): every full mod guide says which settings only the server's files change. Its Server
    // settings section opens with the ServerSide callout, and every settings row says where it is changed (In game, or
    // Server file only); a row without that is retired or reserved and must say so.
    private static void GuideServerSideCheck(Guide g)
    {
        var config = g.Sections.FirstOrDefault(s => s.Key == GuideSchema.Config);
        Check(config != null && config.Blocks.Count > 1 && config.Blocks[0] is GCallout { Kind: CalloutKind.ServerSide }
              && config.Blocks[1] is GBullets,
            $"{g.Id}: Server settings opens with the server-side callout (Blocks.ServerSide)");
        foreach (var row in Guides.AllBlocks(g).OfType<GSettings>().SelectMany(s => s.Rows))
        {
            if (row.Applies is "live" or "restart" or "reload") continue;
            Check(row.Applies == null && System.Text.RegularExpressions.Regex.IsMatch(row.Meaning ?? "", @"\b(Retired|Reserved|Renamed|Replaced|Debug builds)\b"),
                $"{g.Id}: [{row.Section}] {row.Key} says where it is changed (live / restart / reload) or that it is retired or reserved");
        }
    }

    private static void GuideLint(Guide g)
    {
        void Prose(string text, string where)
        {
            var problem = GuideText.Lint(text);
            Check(problem == null, $"{g.Id}: {where}: {problem} in \"{Trunc(text)}\"");
        }
        void Plain(string text, string where)
        {
            var problem = GuideText.LintPlain(text);
            Check(problem == null, $"{g.Id}: {where}: {problem} in \"{Trunc(text)}\"");
        }
        Prose(g.Title, "title");
        Prose(g.Purpose, "purpose");
        foreach (var s in g.Sections) Prose(s.Title, "section title");
        foreach (var b in Guides.AllBlocks(g))
        {
            switch (b)
            {
                case GPara p: Prose(p.Text, "paragraph"); break;
                case GBullets bl: foreach (var i in bl.Items) Prose(i, "bullet"); break;
                case GSteps st: foreach (var i in st.Items) Prose(i, "step"); break;
                case GCallout c: Prose(c.Text, "callout"); break;
                case GDetail d: Prose(d.Title, "detail title"); break;
                case GCommands cs:
                    Prose(cs.Title, "command table title");
                    foreach (var r in cs.Rows)
                    {
                        Plain(r.Syntax, "syntax");
                        Plain(r.Aliases, "aliases");
                        Prose(r.Desc, $"description of {r.Syntax}");
                        Check(!string.IsNullOrWhiteSpace(r.Desc), $"{g.Id}: {r.Syntax} has a description");
                        Check(r.Syntax != null && r.Syntax.StartsWith("."), $"{g.Id}: syntax '{r.Syntax}' starts with '.'");
                    }
                    break;
                case GSettings se:
                    Prose(se.Title, "settings title");
                    Plain(se.File, "settings file");
                    foreach (var r in se.Rows)
                    {
                        Plain(r.Section, "setting section"); Plain(r.Key, "setting key"); Plain(r.Default, "setting default");
                        Prose(r.Meaning, $"meaning of {r.Key}");
                        Check(!string.IsNullOrWhiteSpace(r.Meaning), $"{g.Id}: setting {r.Key} has a meaning");
                    }
                    break;
                case GTabMap t:
                    foreach (var r in t.Rows) { Prose(r.Tab, "tab name"); Prose(r.Does, "tab text"); Plain(r.Sends, "tab sends"); }
                    break;
                case GLinks lk:
                    // 0.73.0 post-plan review: link rows open a browser, so each URL must be a plain absolute https link.
                    foreach (var (label, url) in lk.Rows)
                    {
                        Check(!string.IsNullOrWhiteSpace(label), $"{g.Id}: link to {url} has a label");
                        Prose(label, "link label");
                        Check(LinkUrlOk(url), $"{g.Id}: link '{label}' URL '{url}' is an absolute https URL without user info or spaces");
                    }
                    break;
            }
        }
    }

    private static bool LinkUrlOk(string url) =>
        !string.IsNullOrEmpty(url) && !url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
        && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(u.UserInfo);

    // 0.73.0: a page moved from a bespoke builder onto the kit keeps every heading, paragraph, bullet, link label and
    // URL the builder had (tests/fixtures/guide-migration/<id>.json, extracted before the builder was deleted).
    private static void GuideMigrationChecks(string fixtures, IReadOnlyList<Guide> all)
    {
        var dir = Path.Combine(fixtures, "guide-migration");
        if (!Directory.Exists(dir)) { Check(false, "tests/fixtures/guide-migration exists"); return; }
        static string Norm(string t) =>
            System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(t ?? "", "<[^>]+>", ""), @"\s+", " ").Trim().ToLowerInvariant();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var id = doc.RootElement.GetProperty("guide").GetString();
            var g = all.FirstOrDefault(x => x.Id == id);
            if (g == null) continue; // GUIDE_ONLY filtered it out
            var text = new System.Text.StringBuilder();
            void Add(string t) => text.Append(Norm(t)).Append('\n');
            Add(g.Title); Add(g.Purpose);
            foreach (var s in g.Sections) Add(s.Title);
            foreach (var b in Guides.AllBlocks(g))
                switch (b)
                {
                    case GPara p: Add(p.Text); break;
                    case GBullets bl: foreach (var i in bl.Items) Add(i); break;
                    case GSteps st: foreach (var i in st.Items) Add(i); break;
                    case GCallout c: Add(c.Text); break;
                    case GDetail d: Add(d.Title); break;
                    case GLinks lk: foreach (var (l, u) in lk.Rows) { Add(l); Add(u); } break;
                }
            var haystack = text.ToString();
            var rewords = doc.RootElement.GetProperty("rewordings").EnumerateArray()
                .Select(r => (r.GetProperty("original").GetString(), r.GetProperty("replacement").GetString())).ToList();
            int n = 0;
            foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                var entry = e.GetString();
                foreach (var (a, rb) in rewords) entry = entry.Replace(a, rb);
                n++;
                Check(haystack.Contains(Norm(entry)), $"{id}: migrated text kept: \"{Trunc(entry)}\"");
            }
            Check(n > 0, $"{id}: migration inventory has entries");
        }
    }

    private static string Trunc(string s) => s == null ? "" : s.Length > 70 ? s.Substring(0, 70) + "…" : s;

    // The kit's own rules (pure parts).
    private static void GuideKitRules()
    {
        Check(GuideText.Lint("Use <b>bold</b> and " + GuideText.Arg("player")) == null, "lint: allowed tag + Arg ok");
        Check(GuideText.Lint("Type .wep set <player>") != null, "lint: raw <player> refused");
        Check(GuideText.Lint("a − b") != null, "lint: U+2212 refused");
        Check(GuideText.Lint("press `.nyar`") != null, "lint: backtick refused");
        Check(GuideText.Lint("weapon hint ✋") != null, "lint: missing symbol refused");
        Check(GuideText.Lint("star ★ and swords ⚔ ok") == null, "lint: the font's known-good symbols pass");
        Check(GuideText.LintPlain(".bl set <player> <blood> <level>") == null, "lint plain: angle brackets fine in syntax");
        var args = GuideSyntax.Args(".prestige r <name> <prestigeType> [page]");
        Eq(string.Join(" ", args), "<name> <prestigeType> [page]", "syntax args parsed in order");
        Eq(GuideSyntax.Norm("Prestige_Type"), "prestigetype", "names compare case/separator-insensitively");
    }
}
