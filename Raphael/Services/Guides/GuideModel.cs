using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Raphael.Services.Guides;

// 0.73.0: the in-game guides as plain data. Each mod's guide is built by a GuideBuilder in Services/Guides/*Guide.cs;
// MainPanel.GuideKit renders it, and the logic tests check it against the command / config manifests generated from
// the mods' own source (tools/guide-commands.py → tests/fixtures). No Unity types here, so the tests can link it.
//
// Text conventions (all prose fields):
//   * TMP rich text is allowed for emphasis only: <b>, <i>, <u>, <s>, <br>, <color=…>, <size=…>, <sup>, <sub>.
//   * An argument placeholder in prose is written with GuideText.Arg("name") (renders <name> safely).
//   * Command syntax (GuideCommand.Syntax) is plain text: ".bl set <player> <blood> <level>". <x> is a required
//     argument, [x] an optional one, a|b a choice. The renderer escapes it.
//   * No U+2212 minus, ◄ ► ↻ or emoji (the game font lacks them); no Markdown backticks.

internal enum GuideKind { FullMod, ShortMod, General }

internal enum CmdScope { Player, Admin }

internal enum CalloutKind { Note, AdminOnly, OffByDefault, Experimental, Planned, Warning }

internal sealed class Guide
{
    public string Id;
    public string Title;
    public string Purpose;
    /// <summary>"Nyarlathotep 0.13.1": the mod version the guide was checked against (shown muted in the header).</summary>
    public string CheckedAgainst;
    public GuideKind Kind;
    /// <summary>Manifest ids (tests/fixtures/&lt;id&gt;-commands.json) whose commands this guide must account for.</summary>
    public string[] ManifestMods = Array.Empty<string>();
    public readonly List<GuideSection> Sections = new();
}

internal sealed class GuideSection
{
    public string Key;
    public string Title;
    public bool Open;
    public readonly List<GuideBlock> Blocks = new();
}

internal abstract class GuideBlock { }

internal sealed class GPara : GuideBlock { public string Text; }

internal sealed class GBullets : GuideBlock { public string[] Items; }

internal sealed class GSteps : GuideBlock { public string[] Items; }

internal sealed class GCallout : GuideBlock { public CalloutKind Kind; public string Text; }

/// <summary>A closed-by-default block inside a section.</summary>
internal sealed class GDetail : GuideBlock { public string Title; public readonly List<GuideBlock> Blocks = new(); }

internal sealed class GCommands : GuideBlock { public string Title; public readonly List<GuideCommand> Rows = new(); }

internal sealed class GSettings : GuideBlock { public string Title; public string File; public readonly List<GuideSetting> Rows = new(); }

internal sealed class GTabMap : GuideBlock { public readonly List<GuideTab> Rows = new(); }

/// <summary>External links (label + URL), rendered as rows with an Open button (AddLinkRow).</summary>
internal sealed class GLinks : GuideBlock { public readonly List<(string Label, string Url)> Rows = new(); }

internal sealed class GuideCommand
{
    /// <summary>Manifest id the command belongs to ("bloodcraft").</summary>
    public string Mod;
    /// <summary>Manifest identity: "group name" ("blood set", "nyar api event"), or the bare name of an ungrouped
    /// command ("nyar").</summary>
    public string Key;
    public string Syntax;
    /// <summary>Other ways to type it ("same as .fam sl"), shown under the syntax. Optional.</summary>
    public string Aliases;
    public string Desc;
    public CmdScope Scope;
    public bool Destructive;
    /// <summary>Why the syntax's arguments deliberately differ from the method signature (verb-dispatch commands,
    /// or a usage the signature can't express). Null = the arguments must match.</summary>
    public string SyntaxOverride;
}

internal sealed class GuideSetting
{
    public string Section;
    public string Key;
    public string Default;
    public string Meaning;
    /// <summary>"restart" / "live" / null (unknown or not applicable).</summary>
    public string Applies;
}

internal sealed class GuideTab
{
    public string Tab;
    public string Does;
    /// <summary>The command(s) the tab sends, plain text like Syntax. Optional.</summary>
    public string Sends;
}

/// <summary>Section keys and the order each guide kind must follow (enforced by the logic tests).</summary>
internal static class GuideSchema
{
    public const string What = "what";
    public const string QuickStart = "quickstart";
    public const string Concepts = "concepts";
    public const string Tabs = "tabs";
    public const string Player = "player";
    public const string Admin = "admin";
    public const string Config = "config";
    public const string Trouble = "trouble";
    public const string Defaults = "defaults";
    public const string Commands = "commands";

    public static readonly string[] FullMod = { What, QuickStart, Concepts, Tabs, Player, Admin, Config, Trouble, Defaults };
    public static readonly string[] ShortMod = { What, Tabs, Commands, Trouble };

    public static string[] For(GuideKind kind) => kind switch
    {
        GuideKind.FullMod => FullMod,
        GuideKind.ShortMod => ShortMod,
        _ => null,
    };

    public static string DefaultTitle(string key) => key switch
    {
        What => "What it is",
        QuickStart => "Quick start",
        Concepts => "How it works",
        Tabs => "Raphael's tabs",
        Player => "Player commands",
        Admin => "Admin guide",
        Config => "Server configuration",
        Trouble => "Troubleshooting",
        Defaults => "About the numbers",
        Commands => "Commands",
        _ => key,
    };
}

internal static class GuideText
{
    /// <summary>An argument placeholder for prose: renders "&lt;name&gt;" without TMP reading it as a tag.</summary>
    public static string Arg(string name) => $"<noparse><{name}></noparse>";

    /// <summary>A command or other literal for prose: rendered as-is, escaped.</summary>
    public static string Cmd(string text) => $"<noparse>{text}</noparse>";

    private static readonly Regex AllowedTag = new(
        @"^</?(b|i|u|s|br|sup|sub|noparse|color(=[^>]+)?|size(=[^>]+)?|mark(=[^>]+)?)>$", RegexOptions.IgnoreCase);

    private static readonly Regex AnyTag = new(@"<[^<>]*>");

    /// <summary>Problems with a prose string (null = fine): unescaped angle-bracket text that isn't an allowed tag,
    /// forbidden glyphs, Markdown backticks.</summary>
    public static string Lint(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var forbidden = ForbiddenGlyph(text);
        if (forbidden != null) return forbidden;
        if (text.IndexOf('`') >= 0) return "contains a Markdown backtick";
        // Walk the text outside <noparse> blocks; every <…> there must be an allowed tag.
        int i = 0;
        while (i < text.Length)
        {
            int np = text.IndexOf("<noparse>", i, StringComparison.OrdinalIgnoreCase);
            string chunk = np < 0 ? text.Substring(i) : text.Substring(i, np - i);
            foreach (Match m in AnyTag.Matches(chunk))
                if (!AllowedTag.IsMatch(m.Value)) return $"unescaped <…> text \"{m.Value}\" (use GuideText.Arg / Cmd)";
            if (chunk.Contains('<') && AnyTag.Matches(chunk).Count != CountChar(chunk, '<'))
                return "a stray '<' outside <noparse>";
            if (np < 0) break;
            int end = text.IndexOf("</noparse>", np, StringComparison.OrdinalIgnoreCase);
            if (end < 0) return "unclosed <noparse>";
            i = end + "</noparse>".Length;
        }
        return null;
    }

    /// <summary>Problems with a plain-text field (syntax, setting values): forbidden glyphs, backticks. Angle
    /// brackets are fine there because the renderer escapes the whole field.</summary>
    public static string LintPlain(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var forbidden = ForbiddenGlyph(text);
        if (forbidden != null) return forbidden;
        if (text.IndexOf('`') >= 0) return "contains a Markdown backtick";
        if (text.IndexOf("noparse", StringComparison.OrdinalIgnoreCase) >= 0) return "plain field must not carry <noparse>";
        return null;
    }

    private static int CountChar(string s, char c) { int n = 0; foreach (var ch in s) if (ch == c) n++; return n; }

    /// <summary>Glyphs the game's TMP fallback font renders as squares (docs/LESSONS_LEARNED.md), plus emoji.</summary>
    public static string ForbiddenGlyph(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            int v = rune.Value;
            if (v == 0x2212) return "contains U+2212 minus (use '-')";
            if (v == 0x25C4 || v == 0x25BA || v == 0x21BB) return $"contains U+{v:X4} (missing from the game font)";
            if (v >= 0x1F000) return $"contains emoji U+{v:X} (missing from the game font)";
            if (v >= 0x2600 && v <= 0x27BF && v != 0x2605 && v != 0x2694) return $"contains symbol U+{v:X4} (missing from the game font)";
        }
        return null;
    }

    /// <summary>Escape a plain-text field for TMP.</summary>
    public static string Escape(string plain) => string.IsNullOrEmpty(plain) ? plain : $"<noparse>{plain}</noparse>";
}

/// <summary>Reads the arguments out of a syntax string, for comparison with the manifest's method signature.</summary>
internal static class GuideSyntax
{
    internal readonly struct Arg
    {
        public readonly string Name;
        public readonly bool Optional;
        public Arg(string name, bool optional) { Name = name; Optional = optional; }
        public override string ToString() => Optional ? $"[{Name}]" : $"<{Name}>";
    }

    private static readonly Regex Token = new(@"<([^<>]+)>|\[([^\[\]]+)\]");

    /// <summary>Every &lt;x&gt; / [x] in order. A choice "a|b" keeps its first word as the name; a trailing "…" is
    /// dropped.</summary>
    public static List<Arg> Args(string syntax)
    {
        var list = new List<Arg>();
        if (string.IsNullOrEmpty(syntax)) return list;
        foreach (Match m in Token.Matches(syntax))
        {
            bool optional = m.Groups[2].Success;
            string raw = (optional ? m.Groups[2].Value : m.Groups[1].Value).Trim().TrimEnd('.', '…').Trim();
            list.Add(new Arg(raw, optional));
        }
        return list;
    }

    /// <summary>Normalize a parameter name for comparison: case- and separator-insensitive.</summary>
    public static string Norm(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name ?? "") if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }
}

// ------------------------------------------------------------------------------------------------- builder

internal sealed class GuideBuilder
{
    private readonly Guide _g;

    public GuideBuilder(string id, string title, string purpose, string checkedAgainst, GuideKind kind, params string[] manifestMods)
    {
        _g = new Guide { Id = id, Title = title, Purpose = purpose, CheckedAgainst = checkedAgainst, Kind = kind,
            ManifestMods = manifestMods ?? Array.Empty<string>() };
    }

    public GuideBuilder Section(string key, bool open, Action<Blocks> fill) => Section(key, GuideSchema.DefaultTitle(key), open, fill);

    public GuideBuilder Section(string key, string title, bool open, Action<Blocks> fill)
    {
        var s = new GuideSection { Key = key, Title = title, Open = open };
        fill?.Invoke(new Blocks(s.Blocks, _g.ManifestMods.Length > 0 ? _g.ManifestMods[0] : null));
        _g.Sections.Add(s);
        return this;
    }

    /// <summary>The standard closing note every full mod guide carries.</summary>
    public GuideBuilder DefaultsNote(string extra = null) => Section(GuideSchema.Defaults, false, b =>
    {
        b.P("Numbers on this page are the mod's defaults. Your server's admin can change them, so the values you see in " +
            "game may differ." + (extra != null ? " " + extra : ""));
    });

    public Guide Build() => _g;

    internal sealed class Blocks
    {
        private readonly List<GuideBlock> _list;
        private readonly string _mod;
        internal Blocks(List<GuideBlock> list, string mod) { _list = list; _mod = mod; }

        public Blocks P(string text) { _list.Add(new GPara { Text = text }); return this; }
        public Blocks Bullets(params string[] items) { _list.Add(new GBullets { Items = items }); return this; }
        public Blocks Steps(params string[] items) { _list.Add(new GSteps { Items = items }); return this; }
        public Blocks Callout(CalloutKind kind, string text) { _list.Add(new GCallout { Kind = kind, Text = text }); return this; }

        public Blocks Detail(string title, Action<Blocks> fill)
        {
            var d = new GDetail { Title = title };
            fill?.Invoke(new Blocks(d.Blocks, _mod));
            _list.Add(d);
            return this;
        }

        public Blocks Commands(string title, Action<Cmds> fill) => Commands(title, _mod, fill);

        public Blocks Commands(string title, string mod, Action<Cmds> fill)
        {
            var c = new GCommands { Title = title };
            fill?.Invoke(new Cmds(c.Rows, mod));
            _list.Add(c);
            return this;
        }

        public Blocks Settings(string title, string file, Action<Sets> fill)
        {
            var s = new GSettings { Title = title, File = file };
            fill?.Invoke(new Sets(s.Rows));
            _list.Add(s);
            return this;
        }

        public Blocks Links(params (string Label, string Url)[] rows)
        {
            var l = new GLinks();
            l.Rows.AddRange(rows);
            _list.Add(l);
            return this;
        }

        public Blocks Tabs(Action<TabRows> fill)
        {
            var t = new GTabMap();
            fill?.Invoke(new TabRows(t.Rows));
            _list.Add(t);
            return this;
        }
    }

    internal sealed class Cmds
    {
        private readonly List<GuideCommand> _rows;
        private string _mod;
        internal Cmds(List<GuideCommand> rows, string mod) { _rows = rows; _mod = mod; }

        /// <summary>Switch the manifest the following rows belong to.</summary>
        public Cmds Mod(string mod) { _mod = mod; return this; }

        public Cmds Player(string key, string syntax, string desc) => Add(key, syntax, desc, CmdScope.Player);
        public Cmds Admin(string key, string syntax, string desc) => Add(key, syntax, desc, CmdScope.Admin);

        /// <summary>Flag the previous row as destructive (deletes, wipes or resets data).</summary>
        public Cmds Destructive() { Last().Destructive = true; return this; }

        /// <summary>Other ways to type the previous row.</summary>
        public Cmds Also(string aliases) { Last().Aliases = aliases; return this; }

        /// <summary>The previous row's arguments deliberately differ from the method signature; say why.</summary>
        public Cmds Override(string reason) { Last().SyntaxOverride = reason; return this; }

        private GuideCommand Last()
        {
            if (_rows.Count == 0) throw new InvalidOperationException("no command row to modify");
            return _rows[_rows.Count - 1];
        }

        private Cmds Add(string key, string syntax, string desc, CmdScope scope)
        {
            _rows.Add(new GuideCommand { Mod = _mod, Key = key, Syntax = syntax, Desc = desc, Scope = scope });
            return this;
        }
    }

    internal sealed class Sets
    {
        private readonly List<GuideSetting> _rows;
        internal Sets(List<GuideSetting> rows) { _rows = rows; }

        public Sets Row(string section, string key, string def, string meaning, string applies = null)
        {
            _rows.Add(new GuideSetting { Section = section, Key = key, Default = def, Meaning = meaning, Applies = applies });
            return this;
        }
    }

    internal sealed class TabRows
    {
        private readonly List<GuideTab> _rows;
        internal TabRows(List<GuideTab> rows) { _rows = rows; }

        public TabRows Row(string tab, string does, string sends = null)
        {
            _rows.Add(new GuideTab { Tab = tab, Does = does, Sends = sends });
            return this;
        }
    }
}

/// <summary>Every guide, in tab order. MainPanel and the tests both read this list.</summary>
internal static partial class Guides
{
    private static Guide[] _all;

    public static IReadOnlyList<Guide> All => _all ??= BuildAll();

    public static Guide Get(string id)
    {
        foreach (var g in All) if (g.Id == id) return g;
        return null;
    }

    /// <summary>Walk every block, including those inside closed details.</summary>
    public static IEnumerable<GuideBlock> AllBlocks(Guide g)
    {
        foreach (var s in g.Sections)
            foreach (var b in Walk(s.Blocks)) yield return b;
    }

    private static IEnumerable<GuideBlock> Walk(List<GuideBlock> blocks)
    {
        foreach (var b in blocks)
        {
            yield return b;
            if (b is GDetail d)
                foreach (var inner in Walk(d.Blocks)) yield return inner;
        }
    }
}
