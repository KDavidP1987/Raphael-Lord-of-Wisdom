# In-game guide spec (Raphael 0.73.0)

The written page spec the guides are built against (PLAN.md §3). It applies Impeccable's **Read mode**:
the reader is here to understand something, so structure serves comprehension and the page should stay
pleasant to scan for a long time.

## Who reads these pages
- **A player** on a modded server, mid-session, with the panel open beside the game. They want "how do I…" in
  seconds and don't know the mod's vocabulary yet.
- **A server admin** setting the mod up or fixing something. They want every command, every setting and its
  default, and what happens when they change it, without opening GitHub or Thunderstore.

Every fact either of them needs must be on the page. Nothing should send them to an external site to finish a
task. A link to a source is fine as an extra.

## Page shape
Each guide is data (`Services/Guides/<Mod>Guide.cs`) built with `GuideBuilder`. `MainPanel.GuideKit` renders it.

**Full mod guide** (Bloodcraft, Beelzebub, Uriel, Faust, Nyarlathotep): these nine sections, in this order. The
logic tests enforce it.

| # | Key | Default title | Open? | What goes in it |
|---|---|---|---|---|
| 1 | what | What it is | open | 3-5 bullets: what the mod adds, who it's for, that it's server-side and Raphael is the UI, that features are server-configured |
| 2 | quickstart | Quick start | **open** | A numbered first-session path for a *player*: the 4-7 steps that get them using the core loop through Raphael's tabs |
| 3 | concepts | How it works | closed | One `Detail` per concept (mechanic, rule, formula, edge case). This is where depth lives |
| 4 | tabs | Raphael's tabs | closed | Tab map: every Raphael tab for the mod → what it does → the command it sends |
| 5 | player | Player commands | closed | Every player command, grouped by topic (`Commands` tables, in `Detail`s when long) |
| 6 | admin | Admin guide | closed | Getting admin (adminauth + Re-check), what's off by default, then every admin command, grouped |
| 7 | config | Server configuration | closed | File path(s), every setting with its default and meaning, grouped by cfg section, plus live vs restart |
| 8 | trouble | Troubleshooting | closed | Symptom → cause → fix, as `Detail`s or bullets. Include "tabs greyed" and "no reply" |
| 9 | defaults | About the numbers | closed | `DefaultsNote()`, the standard "values are defaults" note |

**Short mod guide** (Kindred): `what` (open), `tabs`, `commands`, `trouble`.

**Open vs closed:** only *What it is* and *Quick start* are open. Everything else starts collapsed, so the page
opens as a short orientation and the reference sits one click away.

## Copy rules
- **Plain words, short sentences.** One idea per sentence. Lead with the action or the answer.
- **Name controls exactly** as they appear in Raphael ("press **Scan all abilities**"), in `<b>bold</b>`.
- **Say what something does, then any limit.** For example: "Re-rolls your daily and weekly quests. Costs the
  server's reroll item."
- **No hedging filler** ("simply", "just", "easily", "note that"). No exclamation marks. No emoji.
- **Numbers are defaults.** State the default value and say an admin can change it, rather than presenting it
  as fixed.
- **Destructive actions:** say exactly what is lost and whether it can be undone. Mark the row `.Destructive()`.
- **Planned features:** use a `Callout(CalloutKind.Planned, …)`, never present-tense claims.
- No headings that restate the page title. No kicker labels above headings. No numbered section titles.

## Command rows
`b.Commands("Title", c => c.Player(key, syntax, desc) / c.Admin(key, syntax, desc) …)`
- **key** = the manifest identity: `"<group> <name>"` exactly as in `tests/fixtures/<mod>-commands.json`
  (`group` + space + `name`), or just `name` when `group` is null. For example: `"blood set"`, `"nyar api event"`,
  `"beelz admin lock add"`.
- **syntax** = how to type it, plain text, starting with a real invocation: `.` + (group or groupShort) + space +
  (name or shortHand). Prefer the short forms players actually type (`.bl set`, `.fam sl`), and add the long
  form with `.Also(".blood set …")` when it helps.
- **Arguments:** `<x>` = required, `[x]` = optional, **using the method's parameter names** from the manifest
  `params` (case and `_` don't matter). The logic test compares names, order and requiredness.
  - When the real input is better described by a choice, write it as the name and explain the choices in the
    description. For example, `<prestigeType>` with "one of: Experience, Exo, a weapon (Sword…) or a blood
    legacy (Worker…)".
  - **Verb-dispatch commands** (manifest `verbDispatch: true`: every parameter is an optional string and the
    method switches on a verb) need one row per verb, each with `.Override("verbs from <File.cs> switch")`.
    Their syntax is free-form but must still start with the invocation.
  - If a usage can't be expressed through the signature, use `.Override("why")`. Keep this rare and honest.
- **Scope:** `Admin` exactly when the manifest says `adminOnly: true`; otherwise `Player`. The test enforces this.
  If a player command is in practice gated by a server setting (Faust access levels, for example), say so in the
  description.
- **desc:** one or two sentences: what it does, important limits, the reply the player sees if that's useful.
- Group related commands under one titled table. Put long lists (more than about 8 rows) inside a `Detail`.
- **Machine API commands** (`… api …` groups, the ones Raphael sends behind the scenes) go in **one closed
  Detail** titled "Machine API (Raphael uses these)", in the admin section. Give each a one-line description.

## Settings rows
`b.Settings("Title", "BepInEx/config/<file>.cfg", s => s.Row(section, key, default, meaning, applies))`
- **section** and **key** exactly as in `tests/fixtures/<mod>-config.json`.
- **default:** the manifest default (true/false, number, text; "(empty)" for an empty string).
- **meaning:** what it controls and the practical effect of changing it, including the range when known.
- **applies:** `"restart"`, `"live"` or null when unknown. Only claim `"live"` when the source shows a reload or
  `SettingChanged` handler.
- Group rows by cfg section (the renderer prints `[Section]` once per run). Put large tables in `Detail`s per
  section.
- A setting built in a loop (the manifest's `unresolvedBinds`) is documented as a **template row** whose section
  contains a placeholder, such as `section: "Faust.<Feature>"`. Explain it in the allowlist's `unresolvedBinds`
  with `match` (a substring of the unresolved line) and a `reason`.

## Prose text
- TMP rich text for emphasis only: `<b>`, `<i>`, `<color=#hex>`.
- Write argument placeholders in prose with `GuideText.Arg("name")`, and commands or other literals that contain
  `<`, `>` or `[` with `GuideText.Cmd(".wep set <player> …")`. A raw `<player>` in prose fails the lint.
- **Forbidden glyphs** (the game font lacks them): U+2212 minus (use `-`), ◄ ► ↻, any emoji, most symbols in
  U+2600-U+27BF. ★ and ⚔ are fine; so are • → ← and the em dash.
- No Markdown: no backticks, no `**`.

## Allowlist
`tests/fixtures/guide-allowlist/<guideId>.json`, only for things deliberately left off the page:
```json
{
  "commands": [ { "mod": "kindredcommands", "key": "dev test", "reason": "debug-only command compiled out of release builds" } ],
  "settings": [ { "mod": "bloodcraft", "section": "…", "key": "…", "reason": "…" } ],
  "unresolvedBinds": [ { "mod": "faust", "match": "Bind(section, \"Access\"", "reason": "per-feature template documented as [Faust.<Feature>] Access" } ]
}
```
Default to documenting. A reason must be specific ("obsolete", "internal" and "not needed" are not enough).

## Checking your guide
From `tests/Raphael.Logic.Tests`, after another session has restored once:
```
GUIDE_ONLY=<id> dotnet build -c Release --no-restore -p:IntermediateOutputPath=obj/w-<id>/ -p:OutDir=bin/w-<id>/
GUIDE_ONLY=<id> dotnet bin/w-<id>/Raphael.Logic.Tests.dll
```
(In PowerShell: `$env:GUIDE_ONLY='<id>'`.) It must report 0 failures for your guide. If the build fails in a
file that isn't yours, another guide is mid-edit, so wait a minute and retry.
