using System.Text.RegularExpressions;
using Raphael.Services.Nyar;

// 0.74.0 (PLAN 2026-10-24 Part B): the Nyarlathotep windows' plain-words help. Every settable field and every pseudo
// field has prose; every help key the UI names exists, and every key defined is used (a source scan of
// MainPanel.Nyar*.cs, comments stripped); prose never states a default (those come from the rules).
internal static partial class Program
{
    private static void NyarHelpTests()
    {
        HelpCoverage();
        HelpTooltip();
        HelpSourceKeys();
    }

    private static readonly string[] HelpPseudoFields = { "location.near", "location.far", "behaviour.range", "action.waveList.N.start" };

    private static readonly Regex DigitNearDefault = new(@"default\W{0,3}\d|\d\W{0,3}default", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void HelpCoverage()
    {
        foreach (var f in NyarFieldRules.SettableFields.Concat(HelpPseudoFields))
        {
            var p = NyarHelp.Prose(f);
            Check(!string.IsNullOrWhiteSpace(p), $"help prose for {f}");
        }
        foreach (var kv in NyarHelp.Fields)
            Check(NyarFieldRules.SettableFields.Contains(kv.Key) || HelpPseudoFields.Contains(kv.Key), $"help key {kv.Key} is a real field");

        // Real keys map onto their generic help key.
        Eq(NyarHelp.Key("action.waveList.3.units.2.level"), "action.waveList.N.units.M.level", "wave entry key");
        Eq(NyarHelp.Key("action.units.4.chance"), "action.units.N.chance", "units entry key");
        Eq(NyarHelp.Key("action.pools.troops"), "action.pools.NAME", "pool key");
        Eq(NyarHelp.Key("action.waveList.2.start"), "action.waveList.N.start", "wave start key");
        Eq(NyarHelp.Key("action.growth.max"), "action.growth.max", "growth key unchanged");

        // Option lines for the choice fields.
        foreach (var (f, opts) in new[]
                 {
                     ("location", new[] { "region", "aroundplayer" }), ("action.behaviour", new[] { "none", "hunt", "regionhunt" }),
                     ("action.modifiers.level", new[] { "own", "fixed", "shift" }), ("action.waveList.2.start", new[] { "cleared", "after", "afterOrCleared" }),
                     ("action.allowTerritory", new[] { "true", "false" }), ("action.loot", new[] { "true", "false" }),
                 })
            foreach (var o in opts) Check(!string.IsNullOrEmpty(NyarHelp.Option(f, o)), $"option line {f} = {o}");

        // No prose states a default number: those come from the rules.
        foreach (var kv in NyarHelp.Fields) Check(!DigitNearDefault.IsMatch(kv.Value), $"no number beside 'default' in {kv.Key}");
        foreach (var kv in NyarHelp.Inputs) Check(!DigitNearDefault.IsMatch(kv.Value), $"no number beside 'default' in input {kv.Key}");
        foreach (var kv in NyarHelp.Sections)
        {
            Check(kv.Value.Lines.Length is >= 3 and <= 8, $"section {kv.Key} has 3-8 lines ({kv.Value.Lines.Length})");
            foreach (var l in kv.Value.Lines) Check(!DigitNearDefault.IsMatch(l), $"no number beside 'default' in section {kv.Key}");
        }

        // No glyph TMP's fallback font lacks, and no markup in the prose (it is escaped where shown).
        var allText = NyarHelp.Fields.Values.Concat(NyarHelp.Inputs.Values).Concat(NyarHelp.Sections.Values.SelectMany(s => s.Lines.Append(s.Title)))
            .Concat(NyarHelp.Options.Values.SelectMany(d => d.Values));
        foreach (var t in allText)
        {
            foreach (var bad in new[] { "◄", "►", "↻", "−" }) Check(!t.Contains(bad), $"no missing glyph in: {t}");
            Check(!t.Contains('<') && !t.Contains('>') || t.Contains("No < or >"), $"no markup in: {t}");
        }
    }

    private static void HelpTooltip()
    {
        var spec = NyarFieldRules.Spec("conditions.chancePercent");
        var tip = NyarHelp.Tooltip("conditions.chancePercent", spec, null, NyarHelp.DefaultOf("conditions.chancePercent"));
        Check(tip.StartsWith(NyarHelp.Prose("conditions.chancePercent")), "tooltip starts with the prose");
        Check(tip.Contains("Default 100%"), $"tooltip default: {tip}");
        Check(tip.Contains("range " + spec.RangeText), "tooltip range");
        Check(tip.EndsWith("<color=#8E8E8E>conditions.chancePercent</color>"), "tooltip ends with the muted key");

        var opts = new[] { ("Yes", "true", "x"), ("No", "false", "y") };
        var yn = NyarHelp.Tooltip("action.allowTerritory", null, opts, NyarHelp.DefaultOf("action.allowTerritory"));
        Check(yn.Contains("• Yes: " + NyarHelp.Option("action.allowTerritory", "true")), "yes line");
        Check(yn.Contains("• No: " + NyarHelp.Option("action.allowTerritory", "false")), "no line");
        Check(yn.Contains("Default No") && !yn.Contains("range "), "bool: default, no range");

        var pursuit = NyarHelp.Tooltip("action.pursuit", NyarFieldRules.Spec("action.pursuit"), null, NyarHelp.DefaultOf("action.pursuit"));
        Check(pursuit.Contains("Default no pursuit") && pursuit.Contains(", or no pursuit"), $"none label as default and range: {pursuit}");
        Eq(NyarHelp.DefaultOf("action.scoreboard", family: true), "Yes", "family scoreboard default");
        Eq(NyarHelp.DefaultOf("action.scoreboard"), "No", "spawn waves scoreboard default");
        Eq(NyarHelp.DefaultOf("action.marchSpeed"), "2.0 m/s", "march speed default");

        var fb = NyarHelp.Tooltip("no.such.field", null, null, "", "Fallback.");
        Check(fb.StartsWith("Fallback."), "fallback used for a field without help");
        // Option tips of the control are kept for an option the help lacks.
        var mixed = NyarHelp.Tooltip("location", null, new[] { ("Odd", "odd", "its own tip") });
        Check(mixed.Contains("• Odd: its own tip"), "control tip fallback");
    }

    // The keys the UI names in source == the keys defined (both ways).
    private static void HelpSourceKeys()
    {
        var fixtures = FixturesDir();
        Check(fixtures != null, "help keys: repo found");
        if (fixtures == null) return;
        var ui = Path.Combine(Directory.GetParent(fixtures).Parent.FullName, "Raphael", "UI", "ModContent");
        var inputs = new HashSet<string>(StringComparer.Ordinal);
        var sections = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in Directory.GetFiles(ui, "MainPanel.Nyar*.cs"))
        {
            var text = Regex.Replace(File.ReadAllText(f), @"//[^\n]*", "");
            foreach (Match m in Regex.Matches(text, @"NyarHelpInput\(""([^""]+)""\)")) inputs.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"NyarHowItWorks\([^,()]+, ""([^""]+)""\)")) sections.Add(m.Groups[1].Value);
        }
        foreach (var k in inputs) Check(NyarHelp.Inputs.ContainsKey(k), $"input help key {k} is defined");
        foreach (var k in NyarHelp.Inputs.Keys) Check(inputs.Contains(k), $"input help key {k} is used");
        foreach (var k in sections) Check(NyarHelp.Sections.ContainsKey(k), $"section {k} is defined");
        foreach (var k in NyarHelp.Sections.Keys) Check(sections.Contains(k), $"section {k} is used");
    }
}
