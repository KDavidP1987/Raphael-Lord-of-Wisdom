# Raphael — Internal Roadmap

The engineering roadmap. The short player-facing version is the **Roadmap** section of
[`README.md`](../README.md#roadmap). This file adds the detail needed to start an item.

Last reviewed: **v0.74.0** (2026-10-24).

Status: **Planned** (agreed, not started) · **Spike** (needs a prototype first) · **Waiting** (needs a
server-mod release) · **Deferred** · **Done**.

---

## Raphael core

| Item | Status | Notes |
|---|---|---|
| UI translations (German, Spanish first) | Planned | Dedicated track. See [Localization](#localization) below. |
| Hide Bloodcraft tabs for disabled systems | Planned | Tabs for systems the server turned off still show, empty. README "Known issues". |
| Name autocomplete in forms; paging on long lists | Planned | The name cache fills from chat, but forms have no dropdown yet. Only `.clan list` has prev/next. |
| Eclipse coexistence | Planned | Re-test against the latest Eclipse. Both mods installed used to crash the client on load. |
| Controller / gamepad edge cases | In progress | First fix shipped. Report remaining cases on Discord with repro steps. |
| Castle hearts on the game map | Deferred | Plot size, decay and open plots now come from Faust (Castle Info, Open Plots, Decay Watch, World Map). Drawing heart timers on the game's own map is still an open question. |
| Tabbed chat window | Done | Shipped as the standalone chat window, set up from **Game UI → Overview**. |
| Client-only tab group | Done | Shipped as the **Game UI** group, shown on every server. |
| Rename from BloodCraftHub | Done | See [`RENAME_TO_RAPHAEL.md`](../RENAME_TO_RAPHAEL.md). |
| Complete in-game guides (one per mod) | Done | 0.73.0. Data in `Services/Guides/`, checked against generated manifests (`tools/guide-commands.py`, `tests/fixtures/`). Regenerate the manifests when a mod ships a release; preflight -Mode Release fails while they are stale. |
| Bloodcraft: Reset Bloodcraft progress | Done | 0.73.0. Admin card; sends Bloodcraft's admin commands one at a time and correlates each reply (`Services/Bloodcraft/BcReset.cs`). |
| Bloodcraft: Emberglass bridge | Spike | 0.73.0 ships it opt-in and experimental. Promote once tested with Emberglass on client and server (`UseEmberglassEclipseBridge=true`). |
| Kindred Admin buttons that send commands KindredCommands 2.5.8 lacks | Done (0.73.0) | Real commands now; dead buttons removed; every template checked against the KindredCommands manifest by the release tests. |

## Server-mod integrations

Each item waits on the server mod named. The source docs live in that mod's repo.

| Mod | Item | Status | Source |
|---|---|---|---|
| Beelzebub | Action-bar hover card with real ability names (fixes "No Name") | Planned | CHANGELOG 0.68.0, "Not in this release" |
| Beelzebub | Ability library: names, descriptions, when-to-use hints, damage multiplier | Planned | Beelzebub `docs/BCH_INTEGRATION_HANDOFF.md` |
| Beelzebub | Buttons for `unmount`, `bar-raw`, `damage-stats`, `ability-set` | Planned | Commands exist server-side; no UI yet |
| Nyarlathotep | Live operations (api 9): field reads, live event view, unit lookup, wave-cleared pushes | Waiting | `RAPHAEL_HANDOFF_API8.md` §5 |
| Nyarlathotep | Map-wide unit list with go-to / despawn on Admin: Units | Waiting | Needs `.nyar api units` + `api unit goto|despawn`. 0.71.0 parses `debug here` (100 m max). Asked in `docs/NYAR_FEEDBACK_2026-10-15.md` §4. |
| Nyarlathotep | Exact pre-fill in the event editor | Waiting | Needs `.nyar api fields event <id>` (contract §10.5). 0.72.0 parses far more of the `event info` text (waves, bosses, pools, pursuit, modifiers) and marks what it couldn't read. Feedback §5. |
| Nyarlathotep | Apocalypse "goal: starting admin" can't start | Done | Fixed server-side in 0.13.0-rc.2 (`d492bd5`). Raphael keeps the Goal here button; the workaround text shows only on rc.1. |
| Nyarlathotep | Wave variety: per-wave lists, pools, bosses, pursuit; grouped templates | Done | 0.72.0, for Nyarlathotep 0.13.0-rc.3 (`RAPHAEL_HANDOFF_WAVE_VARIETY.md`). |
| Nyarlathotep | Undead hordes: Infestation and Horde march forms, destination waves, 500-unit limits, Undead hordes template group | Done | 0.73.0, for Nyarlathotep 0.14.0 (`RAPHAEL_HANDOFF_UNDEAD_HORDES.md`). |
| Nyarlathotep | Horde pools: pool min-max ranges, library pools and a Pools library card, castle entry on horde events; tooltips and folded guides on every setting | Done | 0.74.0, for Nyarlathotep 0.15.0 (`RAPHAEL_HANDOFF_HORDE_POOLS.md`). |
| All | Units & Objects reference: Help tab + floating panel, Browse buttons in Nyar, Beelzebub and Uriel forms | Done | 0.74.0 (`docs/plans/2026-10-24-nyar015-help-catalog/PLAN.md`). Object names from game text need an in-game check. |
| Nyarlathotep | Communications (api 10): notice push, catch-up read, scheduled messages | Waiting | `RAPHAEL_HANDOFF_API8.md` §6 |
| Nyarlathotep | Stats: `me` / `top` reads, hide / show, share | Waiting | Contract: PLANNED. Raphael tabs are built and gated. |
| Nyarlathotep | Defended zones: `zones` read, add / remove | Waiting | Contract: PLANNED. Raphael tab is built and gated (`NyarState.SupportsZoneReads`). |
| Faust | Region for players in the open world | Waiting | `docs/FAUST_API_REQUESTS.md` §3 |
| Faust | Resource stats view (`stats resources`) | Waiting | Faust `BCH_INTEGRATION_CONTRACT.md` |
| Uriel | Rules panel for the aimed container, "My Shares", owned-style stair picker | Waiting | Needs Uriel `api info` / `api shares` / `api stairinfo` (Uriel is on API 1). Uriel handoff §6. |
| Uriel | Coffin sharing, paid or timed doors, share-all-storage, castle rentals | Waiting | Uriel `docs/ROADMAP.md` |
| Uriel | Castle Events: layout, run panel, live readout, Event Library, Admin: Units | Done | Shipped in 0.70.0 on Uriel 0.21.0's chat replies (`Services/Uriel/UrielEvents.cs`). Not yet tested in game: run handoff §9. |
| Uriel | Castle Events wire API (`[URIEL:layout]`, `evrun` pushes, `err cmd=`) | Waiting | Raphael confirmed handoff §7 with six amendments: `docs/URIEL_CASTLE_EVENTS_API_REPLY.md`. Gate on Uriel's ApiVersion bump; keep the text parsing as the fallback. |
| Uriel | Castle Events run overlay (separate from the tab) | Planned | The readout lives on the Castle Events tab and in toasts today. |
| Uriel | Admin: Controls (feature switches, four cleanup buttons) and greyed off features | Done | 0.73.0 on Uriel 0.22.0's chat text (`Services/Uriel/UrielFeatures.cs`). |
| Uriel | `.uriel api features` machine line, and a per-admin arm token for killswitch / purgeall / unit purge | Waiting | Uriel keeps one shared arm per command and reads switches from human text today; ask for both with the next ApiVersion bump. |

---

## Localization

**Goal:** translate the UI, German and Spanish first, then French and Swedish, then Russian once Cyrillic is
confirmed. There is a real German-speaking user base.

**Why it's a separate track:** most of the cost is moving hardcoded strings into a lookup table, not the
translation itself.

### Current state (v0.70.0)

- `Services/LocalizationService.cs` is a stub. Its constructor is commented out.
- `Resources/Localization/English.json` is embedded but empty (`"strings": {}`).
- There is no `GetString` layer. Every user-facing string is a C# literal. Approximate counts:

  | Site | Count |
  |---|---|
  | `.text = "…"` | 155 |
  | `tooltip: "…"` | 206 |
  | `AddSectionHeading(…)` | 281 |
  | `AddBodyText(…)` | 244 |
  | `AddUrielButton(…)` | 162 |
  | Guide prose (`AddGuideSection`, `AddCollapsibleHelpDetail`) | 141 |

### Fonts

- German, Spanish, French and Swedish use Latin script. Their accented letters are very likely already
  covered. Bloodcraft ships Spanish, French and Turkish in the same game.
- The known missing glyphs (`◄ ► ↻`) are symbols, not letters. See `docs/LESSONS_LEARNED.md`.
- **Russian is the only real risk.** Cyrillic may render as boxes in Raphael's font and need one of the
  game's own multilingual `TMP_FontAsset`s.

### Approach

- Raphael owns its labels, so it needs only a key → string dictionary and a `GetString(key)` that reads
  the active language's JSON. No `Stunlock.Localization` registration.
- Language picker in Settings: Auto (game locale) or a manual choice, saved to the `.cfg`.
- Translate interactive text first: tabs, buttons, settings, status lines, tooltips. Leave the long guide
  prose for later; it's the most text for the least value.
- German runs about 30% longer. Give cramped buttons and labels room (auto-size or wrapping) as they move.

### Order of work

1. **Font test (about half a day).** Put German, Spanish and a Cyrillic sample on a Raphael label.
2. **Reviewers.** Find one or two German / Spanish speakers on Discord for mod terms (familiar, expertise,
   legacy, prestige, stash).
3. **Lookup layer.** Finish `LocalizationService`, add `GetString`, fill `English.json` as the key list.
4. **Move strings file by file**, interactive text first.
5. **Ship German and Spanish**, then French and Swedish.
6. **Russian last**, after the font test.
