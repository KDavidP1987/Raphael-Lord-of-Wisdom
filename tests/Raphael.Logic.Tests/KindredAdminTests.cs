using System.Text.Json;
using System.Text.RegularExpressions;
using Raphael.Services;
using Raphael.Services.Kindred;

// 0.73.0: PLAN-ADDENDUM §1 "Quoting" + "Accuracy gate" + "Soulshard durability".
//   VcfArg.TryEncode / TryDecimal (pure; linked from Raphael/Services/VcfArg.cs), and every template in
//   KindredAdminCommands (linked) checked against tests/fixtures/kindredcommands-commands.json: a real invocation, no
//   stray literal tokens, {placeholders} = the source parameters by name and order (trailing optional ones may be left
//   out), and quoting: every `string` parameter quoted, no number / bool / enum quoted.
internal static partial class Program
{
    private static void KindredAdminTests()
    {
        VcfArgEncodeTests();
        VcfArgDecimalTests();
        KindredAdminTemplateTests();
    }

    // ------------------------------------------------------------------ VcfArg.TryEncode
    private static void VcfArgEncodeTests()
    {
        string Enc(string raw) => VcfArg.TryEncode(raw, out var t, out _) ? t : null;
        Eq(Enc("Bandits"), "Bandits", "plain value sent as is");
        Eq(Enc("  Bandits  "), "Bandits", "value trimmed");
        Eq(Enc("The Night Watch"), "\"The Night Watch\"", "value with a space is quoted");
        Eq(Enc("8:00 PM"), "\"8:00 PM\"", "announcement time is quoted");
        Eq(Enc(""), null, "empty refused");
        Eq(Enc(null), null, "null refused");
        Eq(Enc("   "), null, "whitespace-only refused");
        Eq(Enc("say \"hi\""), null, "double quote refused");
        Eq(Enc("a\\b"), null, "backslash refused");
        Eq(Enc("trailing\\"), null, "trailing backslash refused (VCF reads \\\" as an escaped quote)");
        Eq(Enc("line1\nline2"), null, "newline refused");
        Eq(Enc("line1\r\nline2"), null, "CRLF refused");
        Eq(Enc("a\tb"), null, "tab refused");
        Eq(Enc("name\n"), null, "trailing newline refused, not trimmed");
        Eq(Enc("\tname"), null, "leading tab refused, not trimmed");
        Eq(Enc("a\u2028b"), null, "Unicode line separator refused");
        // VCF's Utility.GetParts splits only on ASCII ' ' (VCF.Core/Common/Utility.cs:48), so U+00A0 stays one token.
        Eq(Enc("Night\u00A0Watch"), "Night\u00A0Watch", "non-breaking space left unquoted");
        Check(!VcfArg.TryEncode("x\"", out var tok, out var err) && tok == null && !string.IsNullOrEmpty(err),
            "a refusal has no token and says why");
    }

    // ------------------------------------------------------------------ VcfArg.TryDecimal
    private static void VcfArgDecimalTests()
    {
        string Dec(string raw) => VcfArg.TryDecimal(raw, 0, 1, 3, out var t) ? t : null;
        Eq(Dec("0"), "0", "lower bound 0");
        Eq(Dec("1"), "1", "upper bound 1");
        Eq(Dec("0.5"), "0.5", "dot decimal");
        Eq(Dec("0,5"), "0.5", "comma decimal → invariant dot");
        Eq(Dec(" 0.25 "), "0.25", "whitespace trimmed");
        Eq(Dec("0.125"), "0.125", "3 decimals ok");
        Eq(Dec("1.000"), "1", "trailing zeros within 3 decimals ok, emitted short");
        Eq(Dec("1.0001"), null, "4 decimals refused (precision)");
        Eq(Dec("-0.1"), null, "below min refused");
        Eq(Dec("1.1"), null, "above max refused");
        Eq(Dec("-0"), "0", "negative zero emitted as 0");
        Eq(Dec(".5"), "0.5", "leading-dot decimal accepted");
        Eq(Dec("abc"), null, "malformed refused");
        Eq(Dec(""), null, "empty refused");
        Eq(Dec("   "), null, "whitespace-only refused");
        Eq(Dec("1e-1"), null, "exponent refused (players type plain decimals; keeps the precision rule honest)");
        Eq(Dec("0.5.1"), null, "two separators refused");
        Eq(Dec("0,5.1"), null, "mixed separators refused");
        Eq(Dec("NaN"), null, "NaN refused");
        Eq(Dec("Infinity"), null, "infinity refused");
        Eq(Dec("1 000"), null, "inner space refused");
        Check(VcfArg.TryDecimal("2.5", 0, 10, 1, out var t1) && t1 == "2.5", "other range / precision: 2.5 in 0-10, 1 dp");
        Check(!VcfArg.TryDecimal("2.55", 0, 10, 1, out _), "other precision: 2.55 refused at 1 dp");
        Check(VcfArg.TryDecimal("7", 0, 10, 0, out var t2) && t2 == "7", "0 dp emits an integer");
    }

    // ------------------------------------------------------------------ KindredAdminCommands vs the manifest
    private sealed class KcCmd
    {
        public ManifestCmd M;
        public List<string> Types = new();
    }

    private static List<KcCmd> LoadKcWithTypes(string fixtures, string mod)
    {
        var cmds = LoadCommands(fixtures, mod);   // GuideTests' loader: names, groups, optional flags
        var path = Path.Combine(fixtures, mod + "-commands.json");
        var list = new List<KcCmd>();
        if (!File.Exists(path)) return list;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        int i = 0;
        foreach (var c in doc.RootElement.GetProperty("commands").EnumerateArray())
        {
            var k = new KcCmd { M = cmds[i++] };
            foreach (var p in c.GetProperty("params").EnumerateArray())
                k.Types.Add(p.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null);
            list.Add(k);
        }
        return list;
    }

    // Parameter types that are one plain token: never quoted. (Enums: VCF / KindredCommands parse them by name.)
    private static readonly HashSet<string> ScalarTypes = new(StringComparer.Ordinal)
        { "int", "int?", "float", "float?", "double", "long", "ulong", "bool", "BloodType", "ClanRoleEnum", "RelicType" };
    // Converters that match a multi-word name after stripping spaces ("Alpha Wolf", "Blood Snapper"): quoting allowed.
    private static readonly HashSet<string> MultiWordConverters = new(StringComparer.Ordinal)
        { "FoundVBlood", "FoundPrimal", "FoundPrisonerFeed" };

    private static string NormName(string s) => (s ?? "").Replace("_", "").Replace("-", "").ToLowerInvariant();

    private static void KindredAdminTemplateTests()
    {
        var fixtures = FixturesDir();
        Check(fixtures != null, "kindred admin: tests/fixtures found");
        if (fixtures == null) return;
        var kc = LoadKcWithTypes(fixtures, "kindredcommands");
        Check(kc.Count > 0, "kindred admin: kindredcommands manifest loaded");
        var all = KindredAdminCommands.All().ToList();
        Check(all.Count >= 140, $"kindred admin: all templates found by reflection ({all.Count})");
        Eq(all.Select(a => a.Template).Distinct().Count(), all.Count, "kindred admin: no two constants share a template");

        foreach (var (name, template) in all)
        {
            string where = $"KindredAdminCommands.{name} \"{template}\"";
            var matches = kc.Where(k => InvocationOk(template, new List<ManifestCmd> { k.M })).ToList();
            if (matches.Count == 0) { Check(false, $"{where}: starts with a real KindredCommands invocation"); continue; }

            // Pick the overload whose parameters fit (.rename has two).
            KcCmd hit = null;
            List<string> holders = null;
            string why = null;
            foreach (var k in matches)
            {
                var inv = Invocations(k.M).Where(i => template.Equals(i, StringComparison.OrdinalIgnoreCase) ||
                                                      template.StartsWith(i + " ", StringComparison.OrdinalIgnoreCase))
                                          .OrderByDescending(i => i.Length).First();
                var rest = template.Substring(inv.Length).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var literal = rest.FirstOrDefault(t => !Regex.IsMatch(t, @"^\{\w+\}$"));
                if (literal != null) { why = $"stray literal token '{literal}' after {inv}"; continue; }
                var ph = rest.Select(t => t.Substring(1, t.Length - 2)).ToList();
                int required = k.M.Params.Count(p => !p.Optional);
                if (ph.Count < required || ph.Count > k.M.Params.Count)
                { why = $"{ph.Count} placeholders for {required} required / {k.M.Params.Count} total parameters"; continue; }
                bool namesOk = true;
                for (int i = 0; i < ph.Count; i++)
                    if (NormName(ph[i]) != NormName(k.M.Params[i].Name))
                    { why = $"placeholder {{{ph[i]}}} at {i + 1} is '{k.M.Params[i].Name}' in the source"; namesOk = false; break; }
                if (!namesOk) continue;
                hit = k; holders = ph; break;
            }
            if (hit == null) { Check(false, $"{where}: placeholders match the source parameters ({why})"); continue; }
            Check(hit.M.Admin || template == KindredAdminCommands.BossList, $"{where}: is an admin command (adminOnly)");

            for (int i = 0; i < holders.Count; i++)
            {
                var type = hit.Types[i];
                bool quoted = KindredAdminCommands.IsQuoted(template, holders[i]);
                if (type == "string")
                    Check(quoted, $"{where}: string parameter {{{holders[i]}}} is quoted");
                else if (type != null && ScalarTypes.Contains(type))
                    Check(!quoted, $"{where}: {type} parameter {{{holders[i]}}} is not quoted");
                else if (quoted)
                    Check(type != null && MultiWordConverters.Contains(type),
                        $"{where}: quoted {{{holders[i]}}} is a string or a multi-word converter (is {type})");
                if (KindredAdminCommands.IsBlankable(template, holders[i]))
                {
                    Check(hit.M.Params[i].Optional, $"{where}: blankable {{{holders[i]}}} is optional in the source");
                    Check(holders.Skip(i).All(h => KindredAdminCommands.IsBlankable(template, h)),
                        $"{where}: blankable {{{holders[i]}}} is a trailing placeholder");
                }
            }
        }

        // The quoting / blank tables name only real templates and their own placeholders.
        foreach (var (table, template, field) in KindredAdminCommands.Declarations())
        {
            Check(all.Any(a => a.Template == template), $"kindred admin: {table} entry '{template}' is a template");
            Check(template.Contains("{" + field + "}"), $"kindred admin: {table} field {{{field}}} is in '{template}'");
        }

        // Commands KindredCommands 2.5.8 doesn't have (commented out, or wrong group) appear in no template.
        string[] gone = { ".showhair", ".unbindall", ".wipe", ".commencewipe", ".cancelwipe", ".clan fix",
                          ".staff ", ".castle ", ".gruelsettings", ".feedsettings" };
        foreach (var (name, template) in all)
            foreach (var g in gone)
                Check(!(template + " ").StartsWith(g.EndsWith(" ") ? g : g + " ", StringComparison.OrdinalIgnoreCase),
                    $"KindredAdminCommands.{name}: doesn't send the removed / wrong command {g.Trim()}");
        Eq(KindredAdminCommands.PrisonerGruelSettings, ".prisoner gruelsettings", "gruel settings is in the .prisoner group");
        Eq(KindredAdminCommands.PrisonerFeedSettings, ".prisoner feedsettings {feed}", "feed settings takes a feed");
    }
}
