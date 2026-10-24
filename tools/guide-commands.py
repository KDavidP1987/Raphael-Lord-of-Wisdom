#!/usr/bin/env python3
"""0.73.0: generate the command and config manifests the in-game guides are tested against.

Reads each server mod's C# source READ-ONLY and writes, per mod:
  tests/fixtures/<mod>-commands.json   every VCF [Command] (group, name, shorthand, admin flag, usage, description,
                                       parameters from the method signature)
  tests/fixtures/<mod>-config.json     every BepInEx Bind / Bloodcraft ConfigEntryDefinition (section, key, default,
                                       description)

Output carries no timestamps, so regenerating unchanged sources gives byte-identical files.

  python -I tools/guide-commands.py            regenerate the fixtures
  python -I tools/guide-commands.py --check    regenerate in memory; exit 1 on any difference, 2 if a source is missing
  python -I tools/guide-commands.py --selftest run the parser self-tests only

Source roots are relative to the folder that holds the Raphael workspace ("…/Games/V Rising"); both of Kris's
machines keep the sibling repos there. Override with --root.
"""
import argparse
import json
import os
import re
import subprocess
import sys

GENERATOR_VERSION = 1

# mod id -> (source root relative to --root, display name)
MODS = {
    "bloodcraft": ("Raphael Lord of Wisdom/LearningMods/Bloodcraft-main/Bloodcraft-main", "Bloodcraft"),
    "kindredcommands": ("Raphael Lord of Wisdom/LearningMods/KindredCommands-main", "KindredCommands"),
    "kindredlogistics": ("Raphael Lord of Wisdom/LearningMods/KindredLogistics-main", "KindredLogistics"),
    "beelzebub": ("Beelzebub Lord of Gluttony/Beelzebub/Beelzebub", "Beelzebub"),
    "uriel": ("Uriel Lord of Oaths/Uriel/Uriel", "Uriel"),
    "faust": ("Faust Lord of Investigation/Faust/Faust", "Faust"),
    "nyarlathotep": ("Nyarlathotep Lord of Chaos/Nyarlathotep/Nyarlathotep", "Nyarlathotep"),
}

CONTEXT_TYPES = {"ChatCommandContext", "ICommandContext", "VCF.Core.Basics.ChatCommandContext",
                 "VampireCommandFramework.ChatCommandContext", "VampireCommandFramework.ICommandContext"}

SKIP_DIRS = {"bin", "obj", ".git", "tests", "Tests"}


class GenError(Exception):
    pass


# ----------------------------------------------------------------------------------------------- C# lexing helpers

def mask_code(src):
    """Return src with comments and string/char literal *contents* replaced by spaces (same length), so brace and
    paren matching can run on the result while offsets still index the original text. Interpolation holes inside
    $"..." strings stay as code (they can hold nested strings and braces)."""
    out = list(src)
    n = len(src)

    def blank(a, b):
        for k in range(a, b):
            if out[k] != "\n":
                out[k] = " "

    def code(i, stop_at_brace):
        """Scan code from i; with stop_at_brace, return the index of the '}' closing an interpolation hole."""
        depth = 0
        while i < n:
            c = src[i]
            if c == "/" and i + 1 < n and src[i + 1] == "/":
                j = src.find("\n", i)
                j = n if j < 0 else j
                blank(i, j)
                i = j
            elif c == "/" and i + 1 < n and src[i + 1] == "*":
                j = src.find("*/", i + 2)
                j = n if j < 0 else j + 2
                blank(i, j)
                i = j
            elif c == '"' or (c in "@$" and i + 1 < n and src[i + 1] in '"$@'):
                i = string(i)
            elif c == "'":
                k = i + 1
                while k < n and src[k] != "'":
                    k += 2 if src[k] == "\\" else 1
                blank(i + 1, k)
                i = k + 1
            else:
                if stop_at_brace:
                    if c == "{":
                        depth += 1
                    elif c == "}":
                        if depth == 0:
                            return i
                        depth -= 1
                i += 1
        return n

    def string(i):
        j = i
        verbatim = interp = False
        while j < n and src[j] in "@$":
            verbatim = verbatim or src[j] == "@"
            interp = interp or src[j] == "$"
            j += 1
        if j >= n or src[j] != '"':
            return i + 1
        if src.startswith('"""', j):
            end = src.find('"""', j + 3)
            end = n if end < 0 else end + 3
            blank(j + 1, end - 1)
            return end
        k = j + 1
        seg = k
        while k < n:
            ch = src[k]
            if verbatim and ch == '"' and k + 1 < n and src[k + 1] == '"':
                k += 2
                continue
            if not verbatim and ch == "\\":
                k += 2
                continue
            if interp and ch == "{":
                if k + 1 < n and src[k + 1] == "{":
                    k += 2
                    continue
                blank(seg, k)
                close = code(k + 1, True)
                k = close + 1
                seg = k
                continue
            if ch == '"':
                break
            k += 1
        blank(seg, k)
        return k + 1

    code(0, False)
    return "".join(out)


def match_close(masked, open_idx, open_ch="(", close_ch=")"):
    depth = 0
    for k in range(open_idx, len(masked)):
        ch = masked[k]
        if ch == open_ch:
            depth += 1
        elif ch == close_ch:
            depth -= 1
            if depth == 0:
                return k
    raise GenError(f"unbalanced {open_ch} at offset {open_idx}")


def split_top(text, masked, sep=","):
    """Split text at top-level separators (outside (), [], {}, <> generics, and literals)."""
    parts, depth, start = [], 0, 0
    for k, ch in enumerate(masked):
        if ch in "([{<":
            depth += 1
        elif ch in ")]}>":
            depth -= 1
        elif ch == sep and depth == 0:
            parts.append(text[start:k])
            start = k + 1
    tail = text[start:]
    if tail.strip():
        parts.append(tail)
    return [p.strip() for p in parts]


def unquote(expr):
    """Evaluate a C# string-literal expression (incl. "a" + "b" concatenation). None when it isn't one."""
    expr = expr.strip()
    if expr == "null":
        return None
    pieces = []
    masked = mask_code(expr)
    for part in split_top(expr, masked, "+"):
        part = part.strip()
        m = re.fullmatch(r'(@?)"(.*)"', part, re.S)
        if not m:
            return None
        body = m.group(2)
        if m.group(1):
            body = body.replace('""', '"')
        else:
            body = (body.replace('\\"', '"').replace("\\n", "\n").replace("\\t", "\t").replace("\\\\", "\\"))
        pieces.append(body)
    return "".join(pieces)


def bind_args(text, order):
    """Map an argument list onto parameter names in `order`. C# lets a named argument sit in its own position and be
    followed by positional ones (`name: "blood", "bl"`), so positions count every argument, named or not."""
    masked = mask_code(text)
    vals = {}
    for idx, raw in enumerate(split_top(text, masked)):
        m = re.match(r"^([A-Za-z_]\w*)\s*(:|=(?!=))\s*(.*)$", raw, re.S)
        if m and not raw.lstrip().startswith('"'):
            vals[m.group(1)[0].lower() + m.group(1)[1:]] = m.group(3).strip()
        elif idx < len(order):
            vals.setdefault(order[idx], raw)
    return vals


def parse_args(text):
    """Attribute / call argument list -> (positional[], named{}). Named covers both `name:` and `Name =` forms."""
    masked = mask_code(text)
    pos, named = [], {}
    for raw in split_top(text, masked):
        m = re.match(r"^([A-Za-z_]\w*)\s*(:|=(?!=))\s*(.*)$", raw, re.S)
        if m and not raw.lstrip().startswith('"'):
            named[m.group(1)[0].lower() + m.group(1)[1:]] = m.group(3).strip()
        else:
            pos.append(raw)
    return pos, named


def literal_value(expr):
    """A config default -> JSON-friendly value (bool / number / string), else the C# expression text."""
    e = expr.strip()
    s = unquote(e)
    if s is not None:
        return s
    if e in ("true", "false"):
        return e == "true"
    m = re.fullmatch(r"(-?\d+(?:\.\d+)?)[fFdDmMuUlL]*", e)
    if m:
        v = m.group(1)
        return float(v) if "." in v else int(v)
    return "expr:" + re.sub(r"\s+", " ", e)


# ----------------------------------------------------------------------------------------------- commands

ATTR_RE = re.compile(r"\[\s*(CommandGroup|Command)\s*\(")
CLASS_RE = re.compile(r"\b(class|struct|record)\s+([A-Za-z_]\w*)")
METHOD_RE = re.compile(r"\b([A-Za-z_][\w<>,\[\]\.\? ]*?)\s+([A-Za-z_]\w*)\s*\(")


def parse_params(text):
    masked = mask_code(text)
    params = []
    for raw in split_top(text, masked):
        p = re.sub(r"\[[^\]]*\]\s*", "", raw).strip()  # parameter attributes
        p = re.sub(r"^(this|params|ref|out|in)\s+", "", p)
        default = None
        if "=" in p:
            p, default = p.split("=", 1)
            p, default = p.strip(), default.strip()
        m = re.match(r"^(.*\S)\s+([A-Za-z_]\w*)$", p, re.S)
        if not m:
            raise GenError(f"can't read parameter '{raw}'")
        params.append({"name": m.group(2), "type": re.sub(r"\s+", "", m.group(1)), "optional": default is not None,
                       **({"default": literal_value(default)} if default is not None else {})})
    return params


def scan_commands(path, rel):
    src = open(path, encoding="utf-8-sig").read()
    masked = mask_code(src)
    # class extents: (start, end, groupName, groupShort)
    class_spans = []
    pending_group = None
    commands = []
    pos = 0
    events = []
    for m in ATTR_RE.finditer(masked):
        events.append(("attr", m.start(), m))
    for m in CLASS_RE.finditer(masked):
        events.append(("class", m.start(), m))
    events.sort(key=lambda e: e[1])
    for kind, start, m in events:
        if kind == "attr":
            open_idx = masked.index("(", m.start())
            close_idx = match_close(masked, open_idx)
            args = src[open_idx + 1:close_idx]
            if m.group(1) == "CommandGroup":
                g = bind_args(args, ["name", "shortHand"])
                pending_group = (unquote(g.get("name", "null")), unquote(g.get("shortHand", "null")))
            else:
                commands.append((close_idx, args))
        else:
            brace = masked.find("{", m.end())
            if brace < 0:
                continue
            end = match_close(masked, brace, "{", "}")
            class_spans.append((start, end, pending_group, m.group(2)))
            pending_group = None

    out = []
    for close_idx, args in commands:
        order = ["name", "shortHand", "usage", "description", "id", "adminOnly"]
        vals = {k: None for k in order}
        vals.update({k: v for k, v in bind_args(args, order).items() if k in vals})
        name = unquote(vals["name"] or "null")
        if name is None:
            raise GenError(f"{rel}: [Command] without a literal name")
        # enclosing group = innermost class span with a group that contains the attribute
        group, cls = (None, None), None
        for s, e, g, name_ in class_spans:
            if s <= close_idx <= e:
                cls = name_
                if g is not None:
                    group = g
        mm = METHOD_RE.search(masked, close_idx + 1)
        if not mm:
            raise GenError(f"{rel}: no method after [Command(\"{name}\")]")
        popen = masked.index("(", mm.end() - 1)
        pclose = match_close(masked, popen)
        params = parse_params(src[popen + 1:pclose]) if src[popen + 1:pclose].strip() else []
        if not params or params[0]["type"].split(".")[-1] not in {t.split(".")[-1] for t in CONTEXT_TYPES}:
            raise GenError(f"{rel}: [Command(\"{name}\")] method {mm.group(2)} has no leading command-context "
                           f"parameter (unknown command framework?)")
        params = params[1:]
        verb_dispatch = len(params) >= 2 and all(p["optional"] and p["type"] == "string" for p in params)
        line = src.count("\n", 0, close_idx) + 1
        out.append({
            "group": group[0], "groupShort": group[1],
            "name": name, "shortHand": unquote(vals["shortHand"] or "null"),
            "adminOnly": (vals["adminOnly"] or "false").strip() == "true",
            "usage": unquote(vals["usage"] or "null"),
            "description": unquote(vals["description"] or "null"),
            "params": params,
            "verbDispatch": verb_dispatch,
            "method": mm.group(2), "_class": cls, "_groupFromAttr": group[0] is not None,
            "file": rel.replace("\\", "/"), "line": line,
        })
    return out


# ----------------------------------------------------------------------------------------------- config

BIND_RE = re.compile(r"\.Bind\s*(?:<[^>]*>)?\s*\(")
CED_RE = re.compile(r"new\s+ConfigEntryDefinition\s*\(")
INTLIMIT_RE = re.compile(r"\bIntLimit\s+\w+\s*=\s*new(?:\s+IntLimit)?\s*\(")


def scan_config(path, rel, unresolved=None):
    src = open(path, encoding="utf-8-sig").read()
    masked = mask_code(src)
    consts = {m.group(1): m.group(2) for m in re.finditer(r'const\s+string\s+(\w+)\s*=\s*("[^"]*")', src)}

    def text_arg(expr):
        e = expr.strip()
        m = re.fullmatch(r'nameof\(\s*(?:[\w.]+\.)?(\w+)\s*\)', e)
        if m:
            return m.group(1)
        return unquote(consts.get(e.split('.')[-1], e))
    out = []
    # Nyarlathotep's range-checked ints: `IntLimit Name = new("Section", "Key", default, min, max)`, bound in a loop.
    for m in INTLIMIT_RE.finditer(masked):
        open_idx = m.end() - 1
        close_idx = match_close(masked, open_idx)
        a, _ = parse_args(src[open_idx + 1:close_idx])
        if len(a) == 5 and unquote(a[0]) is not None and unquote(a[1]) is not None:
            out.append({"section": unquote(a[0]), "key": unquote(a[1]), "default": literal_value(a[2]),
                        "min": literal_value(a[3]), "max": literal_value(a[4]), "description": None,
                        "kind": "IntLimit", "file": rel.replace("\\", "/"), "line": src.count(chr(10), 0, m.start()) + 1})
    for rx, kind in ((BIND_RE, "Bind"), (CED_RE, "ConfigEntryDefinition")):
        for m in rx.finditer(masked):
            open_idx = m.end() - 1
            close_idx = match_close(masked, open_idx)
            pos_args, named = parse_args(src[open_idx + 1:close_idx])
            if len(pos_args) == 2 and pos_args[0].startswith("new ConfigDefinition"):
                inner = pos_args[0][pos_args[0].index("(") + 1:pos_args[0].rindex(")")]
                ip, _ = parse_args(inner)
                pos_args = ip + pos_args[1:]
            if len(pos_args) < 3:
                continue
            section, key = text_arg(pos_args[0]), text_arg(pos_args[1])
            if section is None or key is None:
                # Not literal: Bloodcraft's generic helper Bind(section, key, defaultValue, description), or a bind
                # built in a loop. Reported so a real setting can't vanish silently.
                generic = pos_args[0].strip() == "section" and pos_args[1].strip() == "key"
                limit_loop = pos_args[0].strip().endswith(".Section")  # IntLimit table, extracted above
                if unresolved is not None and not generic and not limit_loop:
                    unresolved.append(f"{rel}:{src.count(chr(10), 0, m.start()) + 1}: Bind({pos_args[0].strip()}, {pos_args[1].strip()}, ...)")
                continue
            desc = None
            if len(pos_args) > 3:
                d = pos_args[3].strip()
                if d.startswith("new ConfigDescription"):
                    di, _ = parse_args(d[d.index("(") + 1:d.rindex(")")])
                    desc = unquote(di[0]) if di else None
                else:
                    desc = unquote(d)
            out.append({"section": section, "key": key, "default": literal_value(pos_args[2]),
                        "description": desc, "kind": kind, "file": rel.replace("\\", "/"),
                        "line": src.count("\n", 0, m.start()) + 1})
    return out


# ----------------------------------------------------------------------------------------------- driver

def source_files(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS and not d.endswith(".Tests"))
        for f in sorted(filenames):
            if f.endswith(".cs"):
                full = os.path.join(dirpath, f)
                yield full, os.path.relpath(full, root)


def mod_version(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for f in filenames:
            if f.endswith(".csproj"):
                txt = open(os.path.join(dirpath, f), encoding="utf-8-sig").read()
                m = re.search(r"<Version>([^<]+)</Version>", txt)
                if m:
                    return m.group(1).strip()
    return None


def git_commit(root, games_root):
    """The mod repo's commit, or None when the source isn't in a repo of its own (e.g. a reference copy that sits
    inside a bigger workspace repo)."""
    try:
        top = subprocess.run(["git", "-C", root, "rev-parse", "--show-toplevel"], capture_output=True, text=True,
                             timeout=20)
        if top.returncode != 0:
            return None
        top_path = os.path.normcase(os.path.abspath(top.stdout.strip()))
        games = os.path.normcase(os.path.abspath(games_root))
        if not top_path.startswith(games + os.sep):
            return None
        r = subprocess.run(["git", "-C", root, "rev-parse", "--short=10", "HEAD"], capture_output=True, text=True,
                           timeout=20)
        return (r.stdout.strip() or None) if r.returncode == 0 else None
    except (OSError, subprocess.SubprocessError):
        return None


def build(mod, root):
    rel_root, display = MODS[mod]
    src_root = os.path.join(root, rel_root)
    if not os.path.isdir(src_root):
        raise FileNotFoundError(src_root)
    commands, config, unresolved = [], [], []
    for full, rel in source_files(src_root):
        commands.extend(scan_commands(full, rel))
        config.extend(scan_config(full, rel, unresolved))
    # A partial class carries its [CommandGroup] on one declaration only; commands in its other parts take it too.
    class_groups = {}
    for c in commands:
        if c["_groupFromAttr"]:
            class_groups[c["_class"]] = (c["group"], c["groupShort"])
    for c in commands:
        if not c["_groupFromAttr"] and c["_class"] in class_groups:
            c["group"], c["groupShort"] = class_groups[c["_class"]]
        del c["_class"], c["_groupFromAttr"]
    commands.sort(key=lambda c: (c["group"] or "", c["name"], c["file"], c["line"]))
    config.sort(key=lambda c: (c["section"], c["key"]))
    head = {"mod": display, "source": rel_root, "sourceVersion": mod_version(src_root),
            "sourceCommit": git_commit(src_root, root), "generatorVersion": GENERATOR_VERSION}
    return ({**head, "commands": commands}, {**head, "settings": config, "unresolvedBinds": unresolved})


def dumps(obj):
    return json.dumps(obj, indent=2, ensure_ascii=False) + "\n"


def selftest():
    sample = '''
[CommandGroup(name: "blood", "bl")]
internal static class BloodCommands {
    // [Command("commented")] not a command
    [Command(name: "set", adminOnly: true, usage: ".bl set [Player] [Blood] [Level]", description: "Sets \\"x\\".")]
    public static void SetCmd(ChatCommandContext ctx, string name, string blood, int level) { var s = "}{"; }
    [Command("get")]
    public static void Get(ChatCommandContext ctx, string blood = "") { var t = $"a{(x ? " }<b>" : "")}c"; }
}
[CommandGroup("nyar api")]
internal static class Api {
    [Command("event", usage: "VERB [ID]", adminOnly: true)]
    public static void Event(ChatCommandContext ctx, string verb = "", string id = "", string field = "") { }
    [Command("version")]
    public static void Version(ICommandContext ctx) { }
}
internal class Cfg { void B(ConfigFile config) {
    X = config.Bind("General", "Enabled", true, "Master switch.");
    Y = config.Bind("Waves", "Max", 5, new ConfigDescription("Most waves" + " per event.", null));
    Z = config.Bind(section, key, defaultValue, description);
}}
'''
    import tempfile
    with tempfile.TemporaryDirectory() as d:
        p = os.path.join(d, "S.cs")
        open(p, "w", encoding="utf-8").write(sample)
        cmds = scan_commands(p, "S.cs")
        cfg = scan_config(p, "S.cs")
    by = {(c["group"], c["name"]): c for c in cmds}
    fails = []

    def check(cond, what):
        if not cond:
            fails.append(what)

    check(len(cmds) == 4, f"4 commands, got {len(cmds)}")
    s = by.get(("blood", "set"))
    check(s and s["groupShort"] == "bl" and s["adminOnly"], "blood set group/admin")
    check(s and [p["name"] for p in s["params"]] == ["name", "blood", "level"], "blood set params, ctx dropped")
    check(s and not any(p["optional"] for p in s["params"]), "blood set all required")
    check(s and s["description"] == 'Sets "x".', "escaped quote in description")
    g = by.get(("blood", "get"))
    check(g and g["params"][0]["optional"] and not g["verbDispatch"], "blood get optional, not verb dispatch")
    e = by.get(("nyar api", "event"))
    check(e and e["verbDispatch"], "nyar api event is verb dispatch")
    v = by.get(("nyar api", "version"))
    check(v and v["params"] == [], "ICommandContext dropped, no params")
    check(len(cfg) == 2, f"2 settings (generic helper skipped), got {len(cfg)}")
    check(any(c["key"] == "Max" and c["default"] == 5 and c["description"] == "Most waves per event." for c in cfg),
          "ConfigDescription + concatenation")
    try:
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "B.cs")
            open(p, "w", encoding="utf-8").write('class A { [Command("x")] public static void X(string a) {} }')
            scan_commands(p, "B.cs")
        fails.append("a command without a context parameter must fail")
    except GenError:
        pass
    for f in fails:
        print("SELFTEST FAIL  " + f)
    print(f"guide-commands selftest: {'ok' if not fails else str(len(fails)) + ' failed'}")
    return 0 if not fails else 1


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.dirname(here)
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=os.path.abspath(os.path.join(repo, "..", "..")))
    ap.add_argument("--out", default=os.path.join(repo, "tests", "fixtures"))
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args()
    if a.selftest:
        return selftest()
    if selftest() != 0:
        return 1
    os.makedirs(a.out, exist_ok=True)
    diffs, missing = [], []
    for mod in MODS:
        try:
            cmds, cfg = build(mod, a.root)
        except FileNotFoundError as ex:
            missing.append(f"{mod}: source not found at {ex}")
            continue
        except GenError as ex:
            print(f"ERROR {mod}: {ex}")
            return 1
        for suffix, obj in (("commands", cmds), ("config", cfg)):
            path = os.path.join(a.out, f"{mod}-{suffix}.json")
            text = dumps(obj)
            old = open(path, encoding="utf-8").read() if os.path.exists(path) else None
            if a.check:
                if old != text:
                    diffs.append(os.path.basename(path))
            elif old != text:
                with open(path, "w", encoding="utf-8", newline="\n") as fh:
                    fh.write(text)
                print(f"wrote {os.path.basename(path)}")
        print(f"{mod}: {len(cmds['commands'])} commands, {len(cfg['settings'])} settings "
              f"(source {cmds['sourceVersion']} @ {cmds['sourceCommit']})")
    for m in missing:
        print("MISSING " + m)
    if a.check:
        for d in diffs:
            print("STALE   " + d + " (run: python -I tools/guide-commands.py)")
        if missing:
            return 2
        return 1 if diffs else 0
    return 2 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
