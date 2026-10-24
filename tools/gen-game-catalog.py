#!/usr/bin/env python3
"""0.74.0: generate the unit and object reference (Raphael/Resources/GameCatalog.Data.cs) and its exclusions report
(tools/catalog-exclusions.md) from the game's prefab dump. Replaces tools/gen-unit-catalog.py.

Inputs, all read-only, relative to --root (the folder that holds the V Rising repos, "…/Games/V Rising"):
  Beelzebub Lord of Gluttony/Reference Data/Prefabs/          one "<Name> PrefabGuid(<n>).txt" component dump per prefab
  Beelzebub Lord of Gluttony/Beelzebub/Beelzebub/Resources/prefab_names.tsv    guid<TAB>name, no header
  Raphael Lord of Wisdom/LearningMods/BloodCraftUI-master/BloodCraftUI/Services/PrefabNames.cs
                                                              PrefabGUID -> localization key (MIT, BloodCraftUI)
Cross-check only (not hashed, reported in the summary when present):
  Nyarlathotep Lord of Chaos/Reference Data/unit_index.tsv    the same unit metadata made by Nyarlathotep's method

No KindredCommands data is read: it is AGPL and Raphael is MIT.

Output carries no timestamps or machine paths, so regenerating unchanged sources gives byte-identical files.

  python -I tools/gen-game-catalog.py            regenerate both artifacts
  python -I tools/gen-game-catalog.py --check    regenerate in memory and compare; exit 1 on any difference or error,
                                                 2 if a source is missing
  python -I tools/gen-game-catalog.py --root "C:/dev/projects/Games/V Rising"   (default: two folders above the repo)
"""
import argparse
import collections
import hashlib
import os
import re
import sys

GENERATOR_VERSION = 1
GAME_DATA_VERSION = "1.1.12"  # the dump's game data (Nyarlathotep docs/GAME_ASSETS.md, VampireReferenceAssemblies 1.1.12)

PREFABS_DIR = "Beelzebub Lord of Gluttony/Reference Data/Prefabs"
PREFAB_NAMES = "Beelzebub Lord of Gluttony/Beelzebub/Beelzebub/Resources/prefab_names.tsv"
NAME_KEYS = "Raphael Lord of Wisdom/LearningMods/BloodCraftUI-master/BloodCraftUI/Services/PrefabNames.cs"
UNIT_INDEX = "Nyarlathotep Lord of Chaos/Reference Data/unit_index.tsv"

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
DATA_OUT = os.path.join(REPO, "Raphael", "Resources", "GameCatalog.Data.cs")
REPORT_OUT = os.path.join(REPO, "tools", "catalog-exclusions.md")

UNIT_PREFIX = "CHAR_"
OBJECT_PREFIXES = ("TM_", "Dynamic_", "Chain_", "Resource_", "Trees_")
BLUEPRINT = "ProjectM.BlueprintData"

# Components that make an excluded prefab look like a placeable world object. Finalised against the 1.1.12 dump
# (component survey, 2026-10-24): Health, the tile model pair, the four inventory components, Interactable (only
# when the prefab does not also carry DestroyOnSpawn, which marks spawn-time helpers), the castle heart link and
# BlueprintData (always included, so it never flags an excluded prefab; listed for completeness).
OBJECT_LIKE = (
    "ProjectM.Health",
    "ProjectM.Tiles.TileModel",
    "ProjectM.TilePosition",
    "ProjectM.InventoryInstanceElement",
    "ProjectM.InventoryOwner",
    "ProjectM.RestrictedInventory",
    "ProjectM.InventoryBuffer",
    "ProjectM.Interactable",
    "ProjectM.CastleBuilding.CastleHeartConnection",
    BLUEPRINT,
)
DESTROY_ON_SPAWN = "ProjectM.DestroyOnSpawn"

FLAG_VBLOOD = "ProjectM.VBloodUnit"
FLAG_MINION = "ProjectM.Minion"
FLAG_CONTAINER = "ProjectM.InventoryInstanceElement"

FILE_RX = re.compile(r"^(.*) PrefabGuid\((-?\d+)\)\.txt$")
FACTION_RX = re.compile(r"FactionReference\s+FactionGuid: ModifiablePrefabGUID PrefabGuid\((-?\d+)\)")
LEVEL_RX = re.compile(r"ProjectM\.UnitLevel\s+Level: (\d+)")
KEY_RX = re.compile(r'\{\s*new\((-?\d+)\)\s*,\s*"([0-9a-fA-F-]{36})"\s*\}')
DIGITS_RX = re.compile(r"\d+$")


class GenError(Exception):
    pass


class MissingSource(GenError):
    pass


def sha256_bytes(b):
    return hashlib.sha256(b).hexdigest()


def read_bytes(path):
    with open(path, "rb") as f:
        return f.read()


def components(text):
    """Component type names: the lines indented exactly two spaces under 'Components'."""
    out = set()
    for line in text.splitlines():
        if line.startswith("  ") and not line.startswith("   ") and line.strip():
            out.add(line.strip())
    return out


def prefix_of(name):
    i = name.find("_")
    return name[: i + 1] if i > 0 else name


def segment(name, index):
    parts = [p for p in name.split("_") if p]
    return parts[index] if len(parts) > index else ""


def strip_digits(s):
    t = DIGITS_RX.sub("", s)
    return t if t else s


def object_group(name, comps):
    """(group, raw sub-group) for an object row: Castle, Resource, Tree or World, then the next name segment."""
    if name.startswith("TM_Castle_"):
        return "Castle", segment(name, 2)
    if name.startswith("Resource_"):
        return "Resource", segment(name, 1)
    if name.startswith("Trees_"):
        return "Tree", segment(name, 1)
    if name.startswith(("Dynamic_", "Chain_")):
        return "World", segment(name, 1)
    if BLUEPRINT in comps:  # TM_ buildables outside TM_Castle_, and the BP_ blueprints (BP_Castle_<Sub>_…)
        return "Castle", segment(name, 2) if segment(name, 1) == "Castle" else segment(name, 1)
    return "World", segment(name, 1)


def check_field(value, what):
    for ch in ("\t", "\n", "\r", '"'):
        if ch in value:
            raise GenError(f"{what} contains a forbidden character {ch!r}: {value!r}")
    return value


def canonical_subgroups(rows):
    """Sub-groups differ only by trailing digits or case in the dump (Cherry01/Cherry02, Gloomrot/GloomRot). Merge
    them per (kind, group): strip digits, then pick the commonest spelling of each case-folded key (ties: ordinal)."""
    spellings = collections.defaultdict(collections.Counter)
    for r in rows:
        r["sub"] = strip_digits(r["sub"])
        spellings[(r["kind"], r["group"], r["sub"].lower())][r["sub"]] += 1
    pick = {k: sorted(c.items(), key=lambda kv: (-kv[1], kv[0]))[0][0] for k, c in spellings.items()}
    for r in rows:
        r["sub"] = pick[(r["kind"], r["group"], r["sub"].lower())]


def build(root):
    prefabs = os.path.join(root, PREFABS_DIR)
    names_path = os.path.join(root, PREFAB_NAMES)
    keys_path = os.path.join(root, NAME_KEYS)
    for p in (prefabs, names_path, keys_path):
        if not os.path.exists(p):
            raise MissingSource(f"MISSING source: {p}")

    # prefab_names.tsv
    names_bytes = read_bytes(names_path)
    tsv = {}
    for n, line in enumerate(names_bytes.decode("utf-8").splitlines(), 1):
        if not line.strip():
            continue
        parts = line.split("\t")
        if len(parts) != 2:
            raise GenError(f"prefab_names.tsv:{n}: expected 2 columns")
        g = int(parts[0])
        if g in tsv:
            raise GenError(f"prefab_names.tsv:{n}: duplicate GUID {g}")
        tsv[g] = parts[1]

    # BloodCraftUI name keys (MIT)
    keys_bytes = read_bytes(keys_path)
    keys = {}
    for m in KEY_RX.finditer(keys_bytes.decode("utf-8")):
        g = int(m.group(1))
        if g in keys and keys[g] != m.group(2).lower():
            raise GenError(f"PrefabNames.cs: GUID {g} has two keys")
        keys[g] = m.group(2).lower()
    if not keys:
        raise GenError("PrefabNames.cs: no localization keys found")

    # the dump
    files = sorted(os.listdir(prefabs))
    manifest = []
    ignored = []
    prefabs_by_guid = {}
    seen_names = {}
    for fname in files:
        full = os.path.join(prefabs, fname)
        if not os.path.isfile(full):
            continue
        m = FILE_RX.match(fname)
        if not m:
            ignored.append(fname)
            continue
        name, guid = m.group(1), int(m.group(2))
        raw = read_bytes(full)
        manifest.append((fname, sha256_bytes(raw)))
        if guid in prefabs_by_guid:
            raise GenError(f"duplicate GUID {guid} in the dump: {name} and {prefabs_by_guid[guid]['name']}")
        # A few excluded prefabs (abilities, buffs) share a name with another GUID in the dump. Names must be unique
        # only among catalogue rows (checked after the inclusion rule); the rest are reported.
        seen_names.setdefault(name, []).append(guid)
        if guid not in tsv:
            raise GenError(f"GUID {guid} ({name}) is in the dump but not in prefab_names.tsv")
        if tsv[guid] != name:
            raise GenError(f"GUID {guid}: the dump says {name!r}, prefab_names.tsv says {tsv[guid]!r}")
        text = raw.decode("utf-8", errors="replace")
        fm = FACTION_RX.search(text) if name.startswith(UNIT_PREFIX) else None
        lm = LEVEL_RX.search(text) if name.startswith(UNIT_PREFIX) else None
        prefabs_by_guid[guid] = {"name": name, "guid": guid, "comps": components(text),
                                 "faction": int(fm.group(1)) if fm else None,
                                 "level": int(lm.group(1)) if lm else 0}
    if not prefabs_by_guid:
        raise GenError("the prefab dump is empty")
    manifest.sort()
    manifest_sha = sha256_bytes("".join(f"{f}\t{h}\n" for f, h in manifest).encode("utf-8"))

    factions = {g: p["name"] for g, p in prefabs_by_guid.items() if p["name"].startswith("Faction_")}

    rows = []
    rule_counts = collections.OrderedDict((p, 0) for p in OBJECT_PREFIXES)
    rule_counts["BlueprintData"] = 0
    unresolved_factions = collections.Counter()
    excluded = collections.Counter()
    flagged = []
    for guid in sorted(prefabs_by_guid, key=lambda g: prefabs_by_guid[g]["name"]):
        p = prefabs_by_guid[guid]
        name, comps = p["name"], p["comps"]
        has_bp = BLUEPRINT in comps
        for pre in OBJECT_PREFIXES:
            if name.startswith(pre):
                rule_counts[pre] += 1
        if has_bp:
            rule_counts["BlueprintData"] += 1
        flags = ""
        if FLAG_VBLOOD in comps:
            flags += "v"
        if FLAG_MINION in comps:
            flags += "m"
        if has_bp:
            flags += "b"
        if FLAG_CONTAINER in comps:
            flags += "c"
        if name.startswith(UNIT_PREFIX):
            fg = p["faction"]
            if fg is not None:
                if fg in factions:
                    group = factions[fg][len("Faction_"):]
                else:
                    group = str(fg)
                    unresolved_factions[group] += 1
            else:
                group = "None"
            rows.append({"kind": "U", "guid": guid, "name": name, "group": group, "sub": segment(name, 1),
                         "level": p["level"], "flags": flags, "key": keys.get(guid, "")})
        elif name.startswith(OBJECT_PREFIXES) or has_bp:
            group, sub = object_group(name, comps)
            rows.append({"kind": "O", "guid": guid, "name": name, "group": group, "sub": sub,
                         "level": 0, "flags": flags, "key": ""})
        else:
            excluded[prefix_of(name)] += 1
            hit = [c for c in OBJECT_LIKE if c in comps]
            if "ProjectM.Interactable" in hit and DESTROY_ON_SPAWN in comps:
                hit.remove("ProjectM.Interactable")
            if hit:
                flagged.append((name, guid, hit))

    canonical_subgroups(rows)
    for r in rows:
        for k in ("name", "group", "sub", "flags", "key"):
            check_field(r[k], f"{r['name']} {k}")
        if not r["name"]:
            raise GenError(f"GUID {r['guid']} has an empty name")
    guids = [r["guid"] for r in rows]
    if len(guids) != len(set(guids)):
        raise GenError("a GUID appears in more than one row")
    row_names = collections.Counter(r["name"] for r in rows)
    dup_rows = sorted(n for n, c in row_names.items() if c > 1)
    if dup_rows:
        raise GenError("duplicate names among catalogue rows: " + ", ".join(dup_rows))
    dup_excluded = sorted(n for n, gs in seen_names.items() if len(gs) > 1)
    rows.sort(key=lambda r: (0 if r["kind"] == "U" else 1, r["name"]))

    return {
        "rows": rows, "manifest_count": len(manifest), "manifest_sha": manifest_sha,
        "names_sha": sha256_bytes(names_bytes), "keys_sha": sha256_bytes(keys_bytes), "key_count": len(keys),
        "rule_counts": rule_counts, "excluded": excluded, "flagged": flagged, "ignored": ignored,
        "unresolved_factions": unresolved_factions, "dup_excluded": dup_excluded,
    }


def render(data):
    rows = data["rows"]
    table = "".join(
        f"{r['kind']}\t{r['guid']}\t{r['name']}\t{r['group']}\t{r['sub']}\t{r['level']}\t{r['flags']}\t{r['key']}\n"
        for r in rows)
    table_sha = sha256_bytes(table.encode("utf-8"))
    units = sum(1 for r in rows if r["kind"] == "U")
    objects = len(rows) - units
    counts = collections.Counter((r["kind"], r["group"], r["sub"]) for r in rows)
    counts_text = "".join(f"{k}\t{g}\t{s}\t{n}\n" for (k, g, s), n in sorted(counts.items()))
    rules = ";".join(f"{k}={v}" for k, v in data["rule_counts"].items())
    keyed = sum(1 for r in rows if r["kind"] == "U" and r["key"])

    out = [
        "// <auto-generated> by tools/gen-game-catalog.py (generator v%d). Do not edit by hand: the logic tests" % GENERATOR_VERSION,
        "// recompute TableSha256 and the counts, and release preflight runs `gen-game-catalog.py --check`. </auto-generated>",
        "//",
        "// Sources (relative to the folder that holds the V Rising repos):",
        f"//   prefab dump   {PREFABS_DIR}/",
        f"//                 {data['manifest_count']} prefab files read; manifest sha256 {data['manifest_sha']}",
        "//                 (sha256 over the sorted \"<file name>\\t<file sha256>\\n\" list)",
        f"//   prefab names  {PREFAB_NAMES}",
        f"//                 sha256 {data['names_sha']}",
        f"//   name keys     {NAME_KEYS}",
        f"//                 sha256 {data['keys_sha']} (BloodCraftUI by panthernet, MIT; {data['key_count']} keys)",
        f"// Game data {GAME_DATA_VERSION}. Rows: {units} units + {objects} objects = {len(rows)}; {keyed} units carry a name key.",
        f"// Object rule, raw count per rule: {rules.replace(';', ', ')}. Deduplicated objects: {objects}.",
        "//",
        "// Table columns (tab-separated, one row per line, no escaping): kind (U unit, O object), PrefabGUID (signed int32),",
        "// internal prefab name, group (unit faction or object category), sub-group, level, flags (v V Blood, m minion,",
        "// b buildable, c container), localization key or empty. TableSha256 is over the table with any CR removed.",
        "namespace Raphael.Resources;",
        "",
        "internal static class GameCatalogData",
        "{",
        f"    public const int GeneratorVersion = {GENERATOR_VERSION};",
        f"    public const string GameDataVersion = \"{GAME_DATA_VERSION}\";",
        f"    public const string PrefabManifestSha256 = \"{data['manifest_sha']}\";",
        f"    public const int PrefabFilesRead = {data['manifest_count']};",
        f"    public const string PrefabNamesSha256 = \"{data['names_sha']}\";",
        f"    public const string NameKeysSha256 = \"{data['keys_sha']}\";",
        f"    public const int UnitRows = {units};",
        f"    public const int ObjectRows = {objects};",
        f"    public const string RuleCounts = \"{rules}\";",
        f"    public const string TableSha256 = \"{table_sha}\";",
        "",
        "    // kind, group, sub-group, row count",
        "    public const string Counts = @\"" + counts_text + "\";",
        "",
        "    public const string Table = @\"" + table + "\";",
        "}",
        "",
    ]
    data_cs = "\n".join(out)

    rep = [
        "# Unit and object reference: what the generator left out",
        "",
        "Generated by `tools/gen-game-catalog.py`; do not edit by hand (release preflight runs `--check`).",
        f"Source: the {GAME_DATA_VERSION} prefab dump, {data['manifest_count']} prefab files, manifest sha256",
        f"`{data['manifest_sha']}`.",
        "",
        "## Inclusion rule",
        "",
        f"- **Units:** every `{UNIT_PREFIX}*` prefab ({units}).",
        "- **Objects:** every prefab whose name starts with " + ", ".join(f"`{p}`" for p in OBJECT_PREFIXES)
        + f", plus every other prefab that has `{BLUEPRINT}`; each GUID once ({objects}).",
        "- Raw count per rule: " + ", ".join(f"{k} {v}" for k, v in data["rule_counts"].items()) + ".",
        "",
        "## Object-like components",
        "",
        "An excluded prefab is listed in full below when it has any of these (finalised against the dump):",
        "",
    ]
    rep += [f"- `{c}`" + (" (only without `ProjectM.DestroyOnSpawn`)" if c == "ProjectM.Interactable" else "")
            + (" (always included, so it never flags a row here)" if c == BLUEPRINT else "") for c in OBJECT_LIKE]
    rep += [
        "",
        f"## Excluded prefabs by prefix ({sum(data['excluded'].values())} prefabs)",
        "",
        "| Prefix | Count |",
        "|---|---|",
    ]
    rep += [f"| `{p}` | {n} |" for p, n in sorted(data["excluded"].items(), key=lambda kv: (-kv[1], kv[0]))]
    rep += [
        "",
        f"## Excluded prefabs with an object-like component ({len(data['flagged'])})",
        "",
        "Move a prefix into the inclusion rule if a placeable prop turns up here.",
        "",
        "| Prefab | PrefabGUID | Flagged by |",
        "|---|---|---|",
    ]
    rep += [f"| `{n}` | {g} | " + ", ".join(c.replace("ProjectM.", "") for c in hit) + " |"
            for n, g, hit in sorted(data["flagged"], key=lambda t: t[0])]
    if data["dup_excluded"]:
        rep += ["", "## Excluded prefab names that more than one GUID uses", ""]
        rep += [f"- `{n}`" for n in data["dup_excluded"]]
    if data["ignored"]:
        rep += ["", "## Files in the dump folder that are not prefab dumps (not read)", ""]
        rep += [f"- `{f.encode('ascii', 'backslashreplace').decode('ascii')}`" for f in data["ignored"]]
    rep.append("")
    report = "\n".join(rep)
    return data_cs, report, table_sha


def cross_check(root, rows):
    """Compare unit faction / level / V Blood / minion with Nyarlathotep's unit_index.tsv. Report only."""
    path = os.path.join(root, UNIT_INDEX)
    if not os.path.exists(path):
        return ["unit_index.tsv not found; cross-check skipped"]
    idx = {}
    with open(path, encoding="utf-8") as f:
        head = f.readline().rstrip("\n").split("\t")
        for line in f:
            x = dict(zip(head, line.rstrip("\n").split("\t")))
            idx[x["name"]] = x
    out = []
    for r in rows:
        if r["kind"] != "U":
            continue
        x = idx.get(r["name"])
        if x is None:
            out.append(f"{r['name']}: not in unit_index.tsv")
            continue
        fac = x["faction"].replace("Faction_", "") or "None"
        mine = (r["group"], r["level"], "v" in r["flags"], "m" in r["flags"])
        theirs = (fac, int(x["level"] or 0), x["vblood"] == "Y", x["minion"] == "Y")
        if mine != theirs:
            out.append(f"{r['name']}: catalogue {mine} vs unit_index {theirs}")
    names = {r["name"] for r in rows if r["kind"] == "U"}
    out += [f"{n}: in unit_index.tsv only (not a {UNIT_PREFIX} prefab)" for n in sorted(set(idx) - names)]
    return out


def normalise(text):
    return text.replace("\r\n", "\n")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", default=os.path.normpath(os.path.join(REPO, "..", "..")),
                    help="folder that holds the V Rising repos (default: two folders above the Raphael repo)")
    ap.add_argument("--check", action="store_true", help="compare with the committed files instead of writing")
    args = ap.parse_args()
    try:
        data = build(args.root)
        data_cs, report, table_sha = render(data)
    except MissingSource as e:
        print(e)
        return 2
    except GenError as e:
        print(f"ERROR {e}")
        return 1

    rows = data["rows"]
    units = [r for r in rows if r["kind"] == "U"]
    print(f"gen-game-catalog: {len(units)} units + {len(rows) - len(units)} objects = {len(rows)} rows; "
          f"table sha256 {table_sha[:16]}...")
    for k, v in data["rule_counts"].items():
        print(f"  rule {k}: {v}")
    groups = collections.Counter((r["kind"], r["group"]) for r in rows if r["kind"] == "O")
    print("  objects by group: " + ", ".join(f"{g} {n}" for (_, g), n in sorted(groups.items())))
    print(f"  excluded prefabs: {sum(data['excluded'].values())}; with an object-like component: {len(data['flagged'])}")
    print(f"  unresolved factions: {dict(data['unresolved_factions']) or 'none'}")
    fallback = [r["name"] for r in units if not r["key"]]
    print(f"  name from prefab name: all {len(rows) - len(units)} objects (no name key in the dump; resolved in-game), "
          f"{len(fallback)} units without a BloodCraftUI key: {', '.join(fallback)}")
    for line in cross_check(args.root, rows):
        print(f"  cross-check: {line}")
    if data["ignored"]:
        print(f"  ignored {len(data['ignored'])} non-prefab file(s) in the dump folder")

    targets = ((DATA_OUT, data_cs), (REPORT_OUT, report))
    if args.check:
        stale = False
        for path, text in targets:
            if not os.path.exists(path):
                print(f"STALE {os.path.relpath(path, REPO)} is missing")
                stale = True
                continue
            with open(path, "rb") as f:
                current = normalise(f.read().decode("utf-8"))
            if current != text:
                print(f"STALE {os.path.relpath(path, REPO)} differs from a fresh generation")
                stale = True
        if stale:
            return 1
        print("OK both artifacts match the sources")
        return 0
    for path, text in targets:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        print(f"wrote {os.path.relpath(path, REPO)} ({len(text.encode('utf-8'))} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
