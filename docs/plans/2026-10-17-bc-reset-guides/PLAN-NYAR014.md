# Raphael 0.73.0 — Nyarlathotep 0.14.0 (undead hordes) editor support

## Context
Nyarlathotep shipped 0.13.1 (a server-only login notice) and 0.14.0 ("undead hordes"). Kris asked for them to be
integrated into Raphael cleanly and with full pre- and post-audit. Raphael already speaks api 8 and has had wave
variety since 0.72.0. The wire has not changed: no new tag, key, push, command or reason code (contract lines
181-182). Everything new is optional admin editor and template support.

Kris's requirements:
- the forms are easy to fill in, and the admin never has to know the accepted values;
- the main menu (left rail) is not disturbed;
- the work is clean and well formatted.

**Decisions (Kris, plan mode, 2026-10-18):**
- **Fold into 0.73.0**, which is still uncommitted. One release and one in-game test session.
- **The unit limit mirrors the server's 500.** A hint explains that the server cfg's `MaxUnitsPerWave` (default 20)
  trims a start, which the server reports as "capped", and that a cfg change needs a server restart.

## Pre-audit (2 read-only agents; server source at f340f75, Raphael working tree)

**How the editor picks a section today.** It chooses by `NyarForm.Read(event info)`, not by the def row.
- The def row can't tell the types apart: SpawnWaves, Apocalypse, Infestation and HordeMarch all send `action=waves`.
- The only type signal is the human info line, `action: infestation of the …` or `action: horde march of the …, march
  N m/s` (EventAdmin.cs:991, 1004).
- Both new types fall into `NyarEventKind.Other`, so they get "no form fields here", and "Any other field" doesn't
  list their fields.
- They are not mis-shown as spawn waves. The gap is that they have no form at all.

**Server rules to mirror.** Each is copied from CommandArgs, Validation, EventAdmin or Limits; the excerpts are in
the review log.
- **Destination** (SpawnWaves and HordeMarch only):
  - `action.destination` takes `here` or `none`. `none` also removes marchSpeed and is refused on a HordeMarch.
  - `.radius` takes an integer, 10-100. `.name` takes plain text, 1-40 characters, with no `<`, `>` or control
    characters.
  - `.radius`, `.name`, and marchSpeed on a SpawnWaves event are each refused until a destination exists. After `here`,
    the event stays disabled until both radius and name are set.
  - Conflicts are refused: fanOut, regionLock, arena, pursuit, and a location other than Point or Admin.
- **marchSpeed** is a Dec2 value, 0.5-5.0, with no `none`. It applies to Apocalypse, SpawnWaves (only with a
  destination) and HordeMarch, and is refused on an Infestation.
- **Infestation only:**
  - `origins.count` 1-10, `.minNatives` 1-20, `.minSpacing` 100-2000, `.perOrigin` 1-100;
  - `hunters` 0-100 (%), `driftRadius` 0-2000;
  - `spread.perKillMin` / `.perKillMax` 0-3. max ≥ min is checked on reload, which disables the event rather than
    refusing the set.
  - `spread.max` 1-500, `spread.perMinute` 1-30;
  - `spread.pool` takes a pool name or `none`. Whether the pool exists is checked on reload.
- **HordeMarch only:**
  - `start` takes `here` (a Point).
  - `start.minDistance` 100-3000. Setting it **replaces** the start with a Hotspot start.
  - `growth.*` and `infest.spread.*` take the five spread keys. `infest.hunters` 0-100, `infest.driftRadius` 0-2000.
  - The destination fields, marchSpeed, and `action.bosses` with pool rolls allowed.
  - There is no size field: the horde is the sum of the units, 10-500, checked on reload.
- **Not settable on Apocalypse, Infestation or HordeMarch:** units, pools, waves, scoreboard. The handoff's "pools work
  as in wave variety" is an overstatement; on these types, pools are events.json only. `action.type` is not settable.
  A new event of these types comes only from a template or a copy.
- **Ranges raised:**
  - entry count and pool-wave total 50 → 500;
  - roamers.max 1-100 → 1-500;
  - hordes.size 5-50 → 5-500;
  - durationSeconds for Infestation and HordeMarch 600-604800, the same as Apocalypse.
- **Templates:** 36 now. The new ones are `undead-infestation` and `undead-horde-march`; `zombie-apocalypse` was
  retuned.

**Main menu risk.** The Nyar editor never touches rail state; `ReconcileRailExpansion` is reached only through
`RefreshAllTabGroupAvailability` and the protocol `Reset`. The risks are local:
- `NyarClear` wipes anything on `_nyarEdHost` (the source of the earlier purple/gray bug);
- every handler needs the `_nyarEdGen` guard;
- wide chip rows must use `NyarChipRows`;
- the other-field dropdown registers on every redraw.
The plan keeps every change inside the editor body and adds a test that guards the rail (Verification).

## Changes

### 1. Rules: `Services/Nyar/NyarFieldRules.cs` (pure, linked into the tests)
- `MaxUnitCount` and `MaxWaveUnits` 50 → 500; `hordes.size` 5-500; `roamers.max` 1-500. Raise the presets to match:
  size 10/30/100/200, roamers 25/100/250.
- New specs, each with its unit word and presets:
  - `action.destination.radius` (m), `action.destination.name` (T, 40);
  - `action.origins.count` / `.minNatives` / `.minSpacing` (m) / `.perOrigin`;
  - `action.hunters` and `action.infest.hunters` (%);
  - `action.driftRadius` and `action.infest.driftRadius` (m);
  - `action.start.minDistance` (m);
  - the spread keys under the prefixes `action.spread.`, `action.growth.` and `action.infest.spread.`: perKillMin and
    perKillMax 0-3, max 1-500, perMinute 1-30.
- `Spec(field, kind)`: the boolean `apocalypse` parameter becomes a family check, so Apocalypse, Infestation and
  HordeMarch all use the 600-604800 duration.
- `Echo` gains readable units: `m`, `m/s` and `%`, for example "300 m", "2 m/s", "50 %". Distances of 1000 m or more
  are echoed as km, for example "= 1.5 km".
- `CheckDestination` (here/none) and `CheckPool` (`IsPoolName` or none) are wired into `NyarCheckKey`.
- `OrderForSave`:
  - `action.destination` comes before `.radius`, `.name` and `action.marchSpeed`.
  - Within one spread block, perKillMax is sent before perKillMin when the new min is above the loaded max; otherwise
    min goes first. This avoids a transient disable.

### 2. Reading: `NyarForm.cs` and `NyarInfoParse.cs`
- `NyarEventKind` gains `Infestation` and `HordeMarch`, read from the `action:` line before the spawn path (so the
  `growth:` pitfall can't trigger).
- A shared `NyarInfoParse.Spread(text)` reads `+a[-b] per kill[ from pool], max M, P per minute`, where `+n` means
  min = max. It is used by `spread:`, `growth:` and `infest: … spread …`.
- Each new line has a parser:
  - `origins:` and `hunters: H %, drift radius D m`;
  - `start: at …` (Point), `start: at the starting admin` (Admin) and `start: the natives' hotspot … at least N m away`
    (Hotspot);
  - `destination: name, radius R, at …`;
  - `horde of N:` (shown, not edited);
  - `horde adds:` as bosses. The shortened "… bosses at most" form counts as Unread.
- SpawnWaves: the `march: from … to NAME (…) at S m/s` line gives HasDestination, the name and the speed. Radius isn't
  printed there, so it is marked Unread with the existing "couldn't read" note.
- The Apocalypse branch also reads `pools:`, so the spread and growth pool chips can list the event's pools.
- Unknown or shortened lines count as Unread, never as a guess (the existing convention).

### 3. Editor: `MainPanel.NyarEditor.cs`, built only from existing controls
Controls used: `NyarFieldStepper`, `NyarTextField`, `NyarChoice`, `NyarListEditor`, `NyarSendOne`,
`AddUrielConfirmButton`, `NyarChipRows`, `NyarNote`.

**Infestation section:**
- "▸ Where the dead rise": four steppers for origins.
- "▸ The living dead": hunters % and drift radius, with the hint "hunters jump in straight lines; a castle territory is
  a refuge".
- "▸ Spread per kill": the spread block (below).
- A closing note: units, pools and faction live in events.json.

**Horde march section:**
- "▸ Start":
  - a choice between **[Start here]** (sent at once, like Goal here) and **[Natives' hotspot]** (a minDistance
    stepper, 100-3000 m);
  - the current start, read from the info line, shown as "now: here / the starting admin / hotspot ≥ 300 m";
  - picking Hotspot says that it replaces a Point start.
- "▸ Destination": **[Destination here]** (sent at once), radius, name and march speed. There is no "none" button on
  a horde march, because the server refuses it.
- "▸ Growth on the way": a spread block, with the hint "growth.max caps the horde's living count; at or below the
  horde size (N) it never grows". N comes from `horde of N`.
- "▸ When it arrives (infestation)": infest hunters, infest drift and an infest spread block.
- "▸ Bosses": `NyarListEditor` with `allowPools: true`.
- The A8 hint: kills, spread and growth happen only within 80 m of a player.

**SpawnWaves (destination waves), a new "▸ Destination (march)" card in the spawn sections:**
- **[Destination here]** (sent at once) and **[No destination]** (press twice to confirm; it removes the march speed
  too).
- Radius, name and march speed are disabled with the reason "set a destination first" until one is read.
- An advisory line: a destination needs location Here (Point) or Admin, and can't be combined with fan-out, region
  lock, arena or pursuit. The server refuses those, and Raphael shows the refusal.
- If Pursuit is set, the card shows "Check: pursuit and a destination can't both be set".

**Spread block** (one helper, used 3 times):
- "Per kill: [min] to [max]" as two steppers that keep min ≤ max on the client (like roll min/max). The echo reads
  "+1-3 per kill".
- Then "Most alive" (1-500) and "Per minute" (1-30).
- A pool picker: chips for the event's pools as read from `pools:`, plus "no pool". Typing is still available under
  "Edit as text", as with the existing pool fields.

**Other updates:**
- **Unit limit hint** in the units list editors and counters (now "units n / 500"): "The server's cfg MaxUnitsPerWave
  (default 20) trims a start — it says 'capped' — and a cfg change needs a server restart."
- **Kind text and section dispatch** cover both new kinds. The four `apo` sites use the family check.
- **"Any other field"** (`MainPanel.Nyar.cs` `NyarSettableFields`) gains every new field path, each picked with its
  spec control.

### 4. Templates panel (`NyarMessages.TemplateGroup`)
- A new group, "Spawns — Undead hordes" (order + 3), for `zombie-apocalypse`, `undead-infestation` and
  `undead-horde-march`. It matches on the suffixes `-apocalypse`, `-infestation` and `-horde-march`; anything else
  falls back as today.
- Its blurb: "one at a time; they start where the admin stands; the cfg caps their size."
- Update the `Templates()` test.

### 5. Texts and docs
- **NyarlathotepGuide.cs** moves to 0.14.0:
  - Infestation and Horde march details, and the destination waves;
  - the new ranges (500, durations), the 36 templates with the Undead hordes group, and `MaxUnitsPerWave` 1-500;
  - the spawn and despawn per-tick ceilings at 40;
  - the `LoginEventNotice` cfg row (0.13.1): "players who log in during an event get a private line; Raphael's own login
    toast may show as well";
  - "one apocalypse-type event at a time".
  Then regenerate the Nyar guide manifests with `python -I tools/guide-commands.py` at f340f75.
- **README:** set the Nyarlathotep compatibility to 0.14.0 (API 8). The Nyar features line adds undead hordes.
- **CHANGELOG(s):** the 0.73.0 Nyarlathotep section. **ROADMAP:** add a row for undead hordes.

## Verification
1. **ClaudeX plan review** of this plan through the runner, read-only, at most 3 rounds, logged in
   `docs/plans/2026-10-17-bc-reset-guides/PLAN-REVIEW-LOG.md` (new "Nyar 0.14.0" section).
2. **Logic tests (new):**
   - every new spec's edges (min−1, min, max, max+1; the marchSpeed decimals 0.49, 0.5, 5.0, 5.01, 1.234, ".5");
   - the new 500 ceilings (500 / 501);
   - info parsing on fixtures copied verbatim from EventAdmin.cs's formats: Infestation, a HordeMarch with each start
     kind, a SpawnWaves march line, spread `+2 per kill` versus `+1-3 per kill from troops`, the shortened `horde adds`;
   - `NyarForm.Read` gives the right Kind for all 6 kinds;
   - OrderForSave: destination first, and the perKill ordering both ways;
   - the template group order;
   - a **rail guard**: a source check that no `MainPanel.Nyar*.cs` file calls `RefreshAllTabGroupAvailability`,
     `ReconcileRailExpansion`, `RequestRebuildMainPanel` or `ApplyRailChanges`, apart from the two existing diagnostic
     buttons.
3. **Build** with 0 errors and the 4 baseline warnings. Run the full tests, dev preflight, and Release preflight (the
   version-file check passes once committed).
4. **A fresh read-only Codex post-build inspection** of the diff, at most 2 rounds, logged.
5. **Deploy** with the game closed to both DLL paths, with .bak backups and verified hashes.
6. **In game (Kris), added to the 0.73.0 list:**
   - on a Nyarlathotep 0.14.0 server, `template use undead-infestation as ui-test`: Edit shows the Infestation form;
     change hunters and spread, Save all, and get oks;
   - `undead-horde-march as hm-test`: Start here, the hotspot distance, Destination here, growth max, bosses with a
     roll;
   - a copy of `undead-onslaught`: Destination here, then radius and name, and the "Not ready yet" reason clears;
   - Templates shows the Undead hordes group;
   - the left rail stays as you left it through all of the above, and the editor stays gray.
7. **After Kris validates:** the same release steps as 0.73.0 (commit, Release preflight on that commit, push, verify
   the remote, package).

## Critical files
`Services/Nyar/NyarFieldRules.cs`, `NyarForm.cs`, `NyarInfoParse.cs`, `NyarMessages.cs`,
`UI/ModContent/MainPanel.NyarEditor.cs`, `MainPanel.Nyar.cs`, `MainPanel.NyarControls.cs` (only if the spread helper
needs a small control), `Services/Guides/NyarlathotepGuide.cs`, `tests/Raphael.Logic.Tests/Program.cs`,
`tests/fixtures/nyarlathotep*`, README, CHANGELOG(s), ROADMAP.

## Appendix — server excerpts (Nyarlathotep f340f75, read-only; Logic/)
- Integers: `int.TryParse(v, NumberStyles.None, InvariantCulture)` (CommandArgs.cs:247-248). Empty → ValueRequired.
- marchSpeed `Decimal2` (CA:739-744): `if (string.IsNullOrEmpty(text) || text.Length > 6 || text[0] == '.' || text[^1] == '.') return null; decimal.TryParse(text, NumberStyles.AllowDecimalPoint, Invariant) ; m >= min && m <= max && decimal.Round(m, 2) == m`. 0.5-5.0; `none` refused.
- `action.destination`: exactly `here` | `none` ("action.destination takes here (your position) or none"); `.radius` 10-100; `.name` `IsPlainText(v, 40)` (length 1-40, no `<` `>` `char.IsControl`) (CA:274-297, VAL:242-248).
- Fit (EA:526-555): destination fields fit SpawnWaves || HordeMarch; marchSpeed fits Apocalypse, SpawnWaves, HordeMarch. HordeMarch `destination none` → refused. SpawnWaves `.radius`/`.name`/`marchSpeed` without destination → "<field> needs action.destination". `destination none` removes destination and marchSpeed. `here` writes location only.
- Conflicts refused when introduced (EA:665-672, VAL:994-1001): needs location Point/Admin; fanOut, regionLock, arena, pursuit take no destination.
- Infestation (EA:582-589): `marchKey = key.StartsWith("start") || key.StartsWith("growth.") || key.StartsWith("infest.")`; marchKey → HordeMarch only, else Infestation only. Ranges: origins.count 1-10, minNatives 1-20, minSpacing 100-2000, perOrigin 1-100, hunters 0-100, driftRadius 0-2000, spread.perKillMin/Max 0-3 (max ≥ min on reload), spread.max 1-500 (MaxTrackedUnits.Max), perMinute 1-30, pool `^[a-z0-9-]{1,24}$` or none (existence on reload).
- HordeMarch: `action.start` exactly `here` (Point); `action.start.minDistance` 100-3000 replaces start with `{type:Hotspot,minDistance}` (EA:590-594); no `admin`/`hotspot` literal; growth.* / infest.spread.* = spread ranges; infest.hunters 0-100, infest.driftRadius 0-2000; bosses rolls allowed (EA:194). Horde = sum of units 10-500 (reload only). units/pools/scoreboard/waves are SpawnWaves-only setters.
- Limits.cs: MaxUnitsPerWave ceiling 500 (default 20); entry counts 1-500 (CA:400,684,852); PastWaveSize > 500; roamers.max 1-500; hordes.size 5-500; durationSeconds Infestation/HordeMarch 600-604800 (VAL:609-611).
- `action.type` not settable (CA:717-720). Wire: `[NYAR:def] action=` is the pillar's action name (`waves` for all spawns types) (ApiLines.cs:21-28).
- Info lines (EA:982-1018): `action: infestation of the {F}`; `origins: {c}, at least {n} natives each, {s} m apart, {p} units each`; `hunters: {h} %, drift radius {d} m`; `spread: {KillDraw}, max {m}, {pm} per minute` with KillDraw `+{n} per kill` | `+{a}-{b} per kill` + ` from {pool}`; `action: horde march of the {F}, march {speed:0.##} m/s`; `start: at {x} {z}[ height {y}]` | `start: at the starting admin` | `start: the natives' hotspot nearest the destination, at least {n} m away`; `destination: {name}, radius {r}, at …` | `…, at the starting admin`; `growth: {SpreadText}`; `infest: hunters {h} %, drift radius {d} m, spread {SpreadText}`; `horde of {N}: …`; `horde adds: {BossText,…}` | `horde adds: {n} bosses at most`. SpawnWaves: `march: from ({x}, {z}) to {name} ({x}, {z}) at {speed:0.##} m/s` (either end may be "the starting admin").

## Revisions after ClaudeX round 1
- **R1 (unread radius):** a field marked Unread is shown empty and is never sent unless the admin types or picks a value (NyarForm.IsChanged needs a non-empty change). Test: Read(SpawnWaves with march) → Changes() is empty; setting only the name gives exactly one set.
- **R2 (destination name with commas):** parse right-anchored: `^destination: (.+), radius (\d+), at (?:the starting admin|-?\d.*)$` (greedy name, the last ", radius N, at " wins). The SpawnWaves `march:` line likewise takes the name greedily before the last ` (` coordinate group or ` the starting admin`. Fixtures: a name with commas, a name containing "radius".
- **R3 (Any other field per kind):** `NyarSettableFields` becomes kind-filtered: a pure `NyarFieldRules.FieldsFor(kind)` (SpawnWaves kinds: existing + destination fields + marchSpeed; Apocalypse: existing apocalypse fields; Infestation: origins/hunters/driftRadius/spread.*; HordeMarch: start/start.minDistance/destination fields (no `none` choice)/marchSpeed/growth.*/infest.*/bosses; Empower: existing). Unit-tested: no Infestation field offered on a HordeMarch and vice versa, `destination none` offered only for SpawnWaves.
- **R4 (pools for every kind):** `NyarInfoParse.Pools` runs for every kind (it is line-driven); the pool chips show the event's pools from `pools:` lines; with none read, only "no pool" plus "Edit as text". Tested on Infestation and HordeMarch fixtures.
- **R5 (destination conflicts):** advisory "Check:" lines (not hard disables, since the info parse is best effort and the server is authoritative) for every conflict the info reply reveals: location not Here/Admin (LocationKind aroundplayer/region), `pursuit:` line, `region lock: on`, `arena: …`, a `fanOut N players` part. Each is a pure function `NyarFieldRules.DestinationConflicts(load)` returning the list; tested one conflict at a time.
- **R6 (Destination here sequence):** sent with NyarSendOne(reread: true): the batch's ok re-reads defs and info; `NyarForm.Apply` keeps the admin's pending edits (existing rule, now tested for the destination case); the existing "Not ready yet: <reason>" box shows the server's missing-field reason (radius, then name) and clears when valid.
- **R7 (spread min/max):** the client never blocks editing a loaded invalid pair (it shows "Check: at most is below at least — raise it"). Stepping min above max moves max up with it, and stepping max below min moves min down (a repair, both marked changed). OrderForSave gains an overload with the loaded values: within a block, max goes first when the new min is above the loaded max, else min first, so every intermediate state is valid (no transient disable). Tests: both crossing directions, equality, only one side changed, loaded-invalid pair.
- **R8 (anchored parsing):** every new regex is fully anchored (`^…$`), numbers parsed with InvariantCulture / NumberStyles.None, decimals with the server's `0.##` shape; negative fixtures (truncated line, extra text, missing clause) give Unread.
- **R9 (kind detection):** tests: an unknown `action: something new` line stays Other; `action: infestation` without "of the" stays Other; neither falls through to spawn waves.
- **R10 (round trip):** per kind (Infestation, HordeMarch, SpawnWaves with destination): Read → untouched → zero changes; edit two fields → OrderForSave emits exactly those sets in the expected order.
- **R11 (rail):** a behavioural Unity test isn't possible in the logic harness (no Unity runtime); kept as the source scan, widened to `NyarProtocolService.Reset`, `RebuildMainPanel`, `ShowTab`, `RailMemory`, plus the in-game check. Rejected as a unit test, with that reason.
- **R12 (generation guard):** new controls use only the existing helpers (which already guard `_nyarEdGen`); a source test asserts every `OnValueChanged +=` in `MainPanel.Nyar*.cs` has `_nyarEdGen` in its handler (same or next line).
- **R13 (side effects):** `action.destination none` and `action.start.minDistance` (and `action.start here`) always re-read info after success (reread), so marchSpeed / the old start disappear from the form; `destination none` is a confirm (press twice) button. Test: Apply of a load without destination drops the old marchSpeed/destination values from the form.
- **R14 (partial saves):** existing NyarBatch semantics (stop at the first refusal, acked steps marked saved); new: when a batch stops after at least one acknowledged step, info is re-read so the form matches the server; already-applied steps are not resent (they are no longer changes after MarkSaved).
- **R15 (text and pool boundaries):** fixtures for destination name 0/1/40/41 characters, `<`, `>`, `\n`, `\t`, U+0085 (char.IsControl), and pool names (24/25, upper case, `_`, space).
- **R16 (deploy):** out of scope for this plan; the project's established deploy (CLAUDE.md dual-DLL paths, game closed, timestamped .bak, hash verification of both targets) is used unchanged. Rejected, logged.
- **Ease of entry (found while building):** with counts up to 500, list rows get `-10` / `+10` buttons beside `-` / `+` when the row's max is 500, and the count cell widens to fit three digits.

## Revisions after ClaudeX round 2
- **R2-1 (an invalid definition hides its lines):** verified in the server, `EventAdmin.Info` line 843: `if (d.DisabledReason is { } reason) return [$"{d.Id} disabled: {reason}"];`, so after `destination here` (and after any set that leaves the definition invalid, a pre-existing gap since 0.72.0) the info reply is that single line and today's editor falls back to "no form fields". Fix: `NyarForm.Read` recognises the single `^<id> disabled: <reason>$` reply as `ReasonOnly` (with the reason); `NyarForm.Apply` of a ReasonOnly load keeps the previous load's kind and values (plus every value the server acknowledged since: `MarkSaved` already folds them into `Load.Values`) and sets `Load.NotReady = reason`. The editor keeps every control, shows the existing "Not ready yet: <reason>" box, and a muted line "The server describes an event only once it is valid; these are the last values read plus your saved changes." An immediate set's ok (Destination here, Start here) records its value in the load (`NyarForm.Acknowledge(field, value)`), so `HasDestination` is `Get("action.destination") == "here"` and survives the ReasonOnly reply. With no previous load (the editor opened on a broken event), the form shows the reason and Any other field (all fields), as today. Tests: Read of the one-line reply; Apply keeps kind/values and adds NotReady; Acknowledge then ReasonOnly keeps HasDestination; a later full reply replaces everything.
- **R2-2/R2-3 (strict coordinate and name grammar):** C = `-?\d+(?:\.\d)?` (server `0.#`, invariant). HordeMarch: `^destination: (.+), radius (\d+), at (?:the starting admin|C C(?: height C)?)$`; `^start: at (?:the starting admin|C C(?: height C)?)$`. SpawnWaves: `^march: from (?:the starting admin|\(C, C\)) to (.+) \((?:the starting admin|C, C)\) at (\d+(?:\.\d{1,2})?) m/s$`. The greedy name with the right-anchored tail always takes the last ", radius N, at …" / last parenthesis, which is the server's (it prints the name first). Fixtures: names containing commas, "radius 5, at", "(the starting admin)", parentheses and coordinate-like text; negative: `at 1garbage`, `at 1.25 2` (two decimals), a missing `m/s`.
- **R2-4 (certain conflicts):** when the parse is definite (a `pursuit:` line, `region lock: on`, an `arena:` line, a `fanOut N players` part, or LocationKind aroundplayer / region), Destination here is disabled with the reason ("Turn pursuit off first" etc.); with the relevant state unread, the advisory text only.
- **R2-5 (callbacks):** the new sections live in a new partial file `MainPanel.NyarHordes.cs` built only from the guarded helpers; a source test asserts that file has no `+=` subscription and no `OnValueChanged` / `onClick` / `AddListener`.
- **R2-6 (start transitions):** Read records `action.start` as `point` / `admin` / `hotspot` (display) and `action.start.minDistance` only for a hotspot start; tests Apply Point → Hotspot (minDistance appears, point gone) and Hotspot → Point and Admin → Point (minDistance gone).
- **R2-7 (group blurb):** neutral: "Apocalypse-type events (apocalypse, infestation, horde march): one runs at a time, and the server cfg caps their size. Each template's summary says where it starts."

## Revisions after ClaudeX round 3 (cap reached; all four accepted, nothing left in dispute)
- **R3-1:** a ReasonOnly reply with no cached kind shows the reason, Reload values, and only the fields every type takes (name and conditions.*); no other setter is offered until a full reply names the kind.
- **R3-2:** pending (unsaved) edits survive every reply: `Apply` keeps an admin change unless the server now holds that very value, for ReasonOnly and full replies alike; acknowledged values are folded into the load separately (MarkSaved / Acknowledge). Test: edit → ReasonOnly → full keeps the edit.
- **R3-3:** verified against the server's `MarchLine` (EventAdmin.cs:953-958): an Admin start prints `from the starting admin` (no parentheses), a Point start `from (x, z)`, and the destination is always `<name> (<x>, <z>)` or `<name> (the starting admin)`. Fixtures for all four combinations, built from that format string.
- **R3-4:** the ReasonOnly reply is matched without inserting the id into a regex: one line, `^(\S+) disabled: (.+)$` (ids hold no spaces), and it is not a head line. Test with regex-significant characters in the id (`a.b+c`).
