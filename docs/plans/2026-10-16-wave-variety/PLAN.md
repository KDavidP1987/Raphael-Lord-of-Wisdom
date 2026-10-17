# Plan: Raphael 0.72.0 — Nyarlathotep wave variety + left-rail accordion hardening
_Locked via claudex-loop — by Claude + Kris (Kris's brief: "integrate these updates, keep the form user-friendly, make sure
nothing in Nyarlathotep or any other section causes issues in the collapsible accordion menus; test pre-audit and
post-audit; use ClaudeX for plans and code")_

## Goal
Bring Raphael's Nyarlathotep admin UI up to Nyarlathotep 0.13.0-rc.3 ("wave variety", still api 8, no wire change):
editor support for the four new fields (pursuit, unit pools, pool entries in unit lists, fixed + rolled bosses incl.
per-wave bosses) with a friendly, validated form, grouped templates (Timed / Onslaught), and updated texts for the
server fixes since 0.71.0. Separately, fix every path that makes left-rail groups open by themselves, so the accordion
behaves predictably across panel rebuilds, detection changes and Nyarlathotep state changes.

Sources (read-only, sibling workspace): `Nyarlathotep Lord of Chaos/docs/RAPHAEL_HANDOFF_WAVE_VARIETY.md`,
`RAPHAEL_INTEGRATION_CONTRACT.md` §2-§5a (unchanged wire; one new paragraph in §3 "Wave variety adds no tag…"), and
the server parser `Logic/CommandArgs.cs` + `Logic/Validation.cs` for exact limits (excerpts in the Appendix).

## Approach

### A. Left-rail accordion (MainPanel.cs; the bug class Kris reported twice)
Root causes found in recon:
- **A-1** `RebuildMainPanelNow` destroys and re-creates `MainPanel` (e.g. after a UI text-size change). All rail state
  (`_groupExpanded`, `_userToggledGroups`, `_groupWasDiagnostic`) is instance state and is lost; the instance `Reset()`
  also clears it.
- **A-2** `BuildTabGroup` forces every group in diagnostic state open (`if (diagnostic) startExpanded = true;`). On a
  server without Bloodcraft/Beelzebub/Uriel/Faust that opens all four "not detected" groups on every build.
- **A-3** `ApplyDetectionDefaultExpansion` only ever collapses Bloodcraft and Beelzebub, so auto-opened Uriel / Faust /
  Nyarlathotep diagnostic groups are never closed again.
- **A-4** Auto-expansion ignores accordion mode: a late handshake can open a second group while the user works in
  another one.

Changes (revised after Codex round 1):
1. **One typed rail state, kept across rebuilds.** A `RailState` class (static instance) holds, per known group title,
   `Expanded` (bool) and `Intent` ∈ {`None`, `Open`, `Closed`} (what the user last asked for), plus one session flag
   `UserTookControl` (the user has clicked any group header or rail tab this session). Only titles in `TabGroups`
   are stored. The instance `Reset()` (which also runs on every rebuild) no longer clears it.
   `MainPanel.ClearRailMemory()` is called from `InitializationPatch.ClientBootstrapSystem_OnDestroy_Prefix` — the
   single teardown path that already resets every protocol service's detection (`EclipseProtocolService.Reset`,
   `BeelzProtocolService.Reset`, … `NyarProtocolService.Reset`). Rail memory therefore lives exactly as long as
   detection state: if detection is not reset, the rail has no reason to be either. Pure static clear, safe in teardown.
2. **BuildTabGroup** starts each group from `RailState.Expanded` if known, else `StartExpanded && headerInteractable`.
   It never force-opens a diagnostic group (removes `if (diagnostic) startExpanded = true;`).
3. **Transition table** (the only writers of expansion; all go through one `ApplyExpansion(title, bool)` that sets
   content `SetActive`, the header arrow and `RailState`):

   | Event | Effect |
   |---|---|
   | Header click opens G | G: Expanded, Intent=Open; UserTookControl. Accordion on: every other open group → collapsed, Intent=Closed. |
   | Header click closes G | G: collapsed, Intent=Closed; UserTookControl. |
   | Rail tab click in G | G: Intent=Open (already expanded); UserTookControl. |
   | First-run Quick Start opens "Settings and Help" | Expanded, Intent=Open (counts as the user's place); does not set UserTookControl. |
   | Header becomes dead (Off / unavailable, no diagnostic) | collapsed (display only); Intent unchanged. |
   | Header becomes usable again | a group with Intent=Open is expanded again, unless accordion is on and another group is open with Intent=Open (then it stays collapsed and its Intent becomes Closed, as an accordion collapse would). |
   | Rebuild | every group restored from RailState; then the policy runs. |
   | Detection / availability change | the policy runs. |

4. **The policy `ReconcileRailExpansion()`** (replaces `ApplyDetectionDefaultExpansion` and the `enteredDiagnostic`
   auto-expand in `RefreshTabGroupAvailability`). Implemented as a pure function in `UI/ModContent/RailPolicy.cs`
   (input: per-group {title, isMod, available, diagnostic, expanded, intent}, the active tab's group, accordion flag,
   UserTookControl; output: the set of groups to open and to close) so it is unit-tested; MainPanel only applies it.
   - First, on every run (including right after a rebuild), restores every usable group with Intent=Open that is
     collapsed (e.g. it was dead and is usable again): it is expanded, except when accordion is on and another group is
     open with Intent=Open — then it stays collapsed and its Intent becomes Closed.
   - Otherwise never changes a group whose Intent is Open or Closed.
   - Only mod groups (Bloodcraft, Beelzebub, Uriel, Faust, Nyarlathotep) are ever auto-opened or auto-closed.
   - `primary`: the active tab's group if it is an available mod group; else the first available mod group in rail
     order; else (nothing available) the first mod group in diagnostic state; else none.
   - Auto-open the primary only when ALL of: UserTookControl is false; no group of ANY kind (mod or not) is open with
     Intent=Open; the primary's header is usable. So once the user has touched the rail, nothing opens by itself again
     this session (covers "user dismissed the auto-open"), and a late handshake never opens a second group next to one
     the user opened.
   - Auto-close every other mod group with Intent=None that is open (undoes earlier auto-opens, e.g. the diagnostic
     fallback when a real mod is later detected). This never closes a group the user opened.
5. Server-derived text shown in the rail diagnostics is unchanged (static strings).

### B. Nyarlathotep event editor (MainPanel.NyarEditor.cs + small service additions)
Server rules, copied from the server source at Nyarlathotep commit `4f059f5` (excerpts in the Appendix), mirrored in a
new pure static class `Services/Nyar/NyarFieldRules.cs` (no Unity types). It reproduces the server's per-value parse
exactly — no stricter, no looser — and the UI labels it a pre-check: the server's answer is authoritative.
- Unit name: `IsNameValue(name, "CHAR_")` — longer than `CHAR_`, at most 96 characters (`MaxNameLength = 96`), starts
  with `CHAR_`, every character in `[A-Za-z0-9_]`. Case-sensitive.
- List split (`CommaList`): non-empty, ≤ 4000 chars, every char in `!`..`~` except the quote, apostrophe, braces,
  brackets, angle brackets and backslash; split on `,`; no empty part (no leading/trailing/double comma); part count ≤
  the field's max. Numbers parse with `NumberStyles.None` (digits only; leading zeros accepted, as on the server).
- `action.pursuit`: 15-600 or `none`.
- Pool name `^[a-z0-9-]{1,24}$`. Pool value: `none`, `+CHAR_x`, `-CHAR_x`, or 1-30 distinct unit names.
- Units list: ≤ 10 parts; each `pool:<name>:<count>` (exactly 3 `:` parts, valid pool name, count 1-50) or
  `CHAR_name` / `CHAR_name:count` (count optional, default 1, 1-50). `action.units` refuses a repeated name or pool
  (`Units` → `Twice`); a wave's own list (`WaveUnits`) allows repeats. In both, a list holding any pool entry may bring
  at most 50 units in all (`PastWaveSize`, `Limits.MaxUnitsPerWave.Max` = 50). (Corrected by the form audit: the
  round-4 text said "count required" and "duplicates allowed" everywhere.)
- Every other field the form shows, with the server's own range (`CommandArgs.SettableValue` / `SpawnKeyValue` /
  `WaveListValue` / `Stat`): name 1-40 plain characters; durationSeconds 30-7200 (600-604800 for an apocalypse; the
  set takes 30-604800 and the file check holds it to the kind, so the form uses the kind's range); minPlayers 0-100;
  cooldownMinutes 0-10080; chancePercent 1-100; waves 1-10; intervalSeconds 10-600; radius 2-30; behaviour none /
  `hunt R` (R 10-60) / `regionhunt`; location here / region / `aroundplayer MIN MAX` (MIN 10-60, MAX 15-80, MIN < MAX;
  with Hunt, MAX ≤ R); level 1-120; levelDelta -5..+5 (not both: the server refuses the second until the first is
  none); modifiers 0.5-3.0, two decimals; empower stats 1.0-3.0, two decimals; factions 1-5 distinct `Faction_` names;
  waveList.N.afterSeconds 10-3600 or none; waveList.N.whenCleared true/false; loot / allowTerritory true/false;
  apocalypse goal radius 10-100, goal name 1-32, hordes 1-10, every 5-1440 min, size 5-50, roamers max 1-100, march
  speed 0.5-5.0.
- **Event shape (found by the form audit, `EventAdmin.cs:410-431`):** an event is either *units form*
  (`action.units` + `waves` + `intervalSeconds` + `modifiers`) or *wave-list form* (`action.waveList`). A units-form
  event accepts only `action.waveList.1.units` as its first wave-list set, and that set **converts** it, removing
  `units`, `waves`, `intervalSeconds` and `modifiers`. A wave-list event takes wave N+1's **units** first (not its
  bosses), and the units-form keys no longer apply to it.
- Bosses list (`BossList`): `none`, or ≤ 3 parts; roll `pool:<name>:<min>-<max>` (shape first, then pool name, then
  1 ≤ min ≤ 5, then min ≤ max ≤ 5); fixed `CHAR_name` or `CHAR_name:<1-3>`. If the list holds any roll, or it is a
  wave's own list, the sum over entries of (fixed count, or roll max) ≤ 5. Same check order as the server, so the
  first message matches what the server would refuse.
- **Server-authoritative only (no client check; the UI says so):** the 8-pools limit, whether a pool exists or is big
  enough for a roll, whether a pool is still referenced (delete), the allied-group mix, `action.bosses` together with
  the last wave's own list, pursuit without Hunt / with fan-out or region lock, and wave gaps ("waveList has N waves;
  the next one is N+1"). These either refuse the set (`[NYAR:err]`, shown as is) or save it and disable the event with
  a reason (shown in the "Not ready yet" box, B7).

Editor (Spawn waves section of the inline editor, i.e. non-empower, non-apocalypse events):
1. **"Which wave?" selector** (All waves / Wave 1…10 — the server's own N range: `CommandArgs.WaveListPath` accepts
   N only as `i is >= 1 and <= 10`, one or two digits, no leading zero; Appendix). "All waves" edits `action.units` /
   `action.bosses`; "Wave N" edits `action.waveList.N.units` / `action.waveList.N.bosses`. Hint: "Waves are added in
   order — set Wave N's units before Wave N+1." The server refuses a gap; Raphael shows that refusal.
2. **Units builder** gains "Add from pool" (pool name + count 1-50 → appends `pool:<name>:<count>`) beside the unit
   picker. The value box stays editable; Set runs `CheckUnitList` first and shows its sentence on failure.
3. **Bosses builder**: "Add boss" (unit picker, count 1-3) and "Add roll from pool" (pool, min, max); a live line
   "bosses in all: n / 5" when a roll or a wave list is in play; Set runs `CheckBossList`. A note: `action.bosses` and
   the last wave's own bosses can't both be set — the server disables the event if they are.
4. **Pursuit row**: number box, Set (runs `CheckPursuit`), "No pursuit" (`none`). Advisory hint (labelled as a check):
   needs Behaviour `hunt R`; yellow when the Behaviour box holds something other than `hunt …`.
5. **Unit pools card**: pool name box (normalised to lower case, validated), unit picker + "Add to pool" building a
   member list shown as numbered rows with a remove button per row, "Save pool", "Add one" (`+x`), "Remove one"
   (`-x`), "Delete pool" (`none`, confirm button; always sent — the server refuses it while an entry names the pool and
   Raphael shows that). Soft warning when members come from different UnitCatalog factions.
6. **Pool save (non-atomic by necessity, made safe to retry).** The server has no atomic multi-chunk replace; the
   handoff prescribes "first names, then one `+name` per twin". Raphael:
   - builds every command with one function, `NyarClient.EventSetCommand(id, field, value)`, the exact string that is
     sent; `NyarFieldRules.PoolSaveValues` chunks against that string's UTF-8 byte count ≤ 450 (chat limit ~512);
   - if the whole pool fits one command (the common case, ~10 names), it is one atomic set;
   - otherwise shows, before sending, "This pool takes N commands; if one is refused the pool stops there — fix it and
     Save again (Save replaces the whole pool).";
   - runs the commands through a `NyarBatch` state machine (pure, time injected, unit-tested): one command at a time;
     the next is queued only after the previous one's answer AND ≥ 0.40 s after it was sent; and NyarClient's global
     pump gap for admin twins (every `.nyar api event|template|pillar|purge …`) rises from 0.25 s to 0.30 s, so all
     twins together — batch, retries and anything else the admin presses — stay ≤ 3.4/s on the client clock, under the
     server's 5 per sliding second even with arrival jitter (reads stay at 0.25 s: contract §3 "These four reads change
     nothing and are not rate-limited (§5a's rate counts twins only)" and §5a "The reads of §3 are not counted");
     on `err` the batch stops and drops its remaining commands; on `ratelimit` it
     waits `secs` and retries that command once; with no answer within 6 s the batch stops and marks the result
     **unknown** (the server may have applied it) and asks the admin to press Reload values;
   - correlation: the batch only ever has ONE command outstanding and never sends another after a timeout, cancel or
     error, so a late reply can never be taken for a later batch step. NyarProtocolService hands the batch every parsed
     `[NYAR:ok]` / `[NYAR:err]` line; the batch accepts only `cmd=event verb=set` lines, and for `ok` only those whose
     `id=` and `field=` equal its outstanding step (an `err` line carries no id/field on the wire, so it is accepted
     only while the step is outstanding AND NyarClient reports that step's exact command string as the one in flight —
     `NyarClient.Complete()` now returns it). `[NYAR:end]` lines and lines for other commands never touch the batch.
     A reply arriving after the batch ended is ignored by it (and handled as today by the normal result line);
   - quarantine (revised after round 4): when a batch ends with a step that was sent but not answered (timeout, or a
     cancel while in flight), `NyarClient` — static, session-scoped, cleared only on expiry or `ResetQueue` (disconnect)
     — enters a 10 s quarantine measured from that moment (not from the send). During it NyarClient sends NO admin twin
     (twins stay queued and go out after expiry; reads keep flowing — they are not counted and their replies are `end`
     lines), Save and the other editor Set buttons show "Waiting for the server's last answer — N s", and any twin
     `[NYAR:ok]` / `[NYAR:err]` (cmd event/template/pillar/purge) that arrives is stale by construction: it is logged
     (diag) and discarded from both the batch and the normal result line, so the batch's "unknown" warning stays. This
     closes the stale-reply race without a wire request id (api 8 has none);
   - cancel: closing the editor, a disconnect (`NyarProtocolService.Reset` also resets NyarClient's queue) drops the batch's
     queued step (a new batch cannot start while one runs). A step already sent cannot be recalled: the summary then says "stopped —
     the last name may or may not have been added; press Reload values". Save stays disabled while a batch runs;
   - coverage: each step carries the member names it adds (the first chunk carries several). The summary counts
     confirmed names from acknowledged steps only ("saved 9 of 13"), names the refused/unanswered step's names, and is
     unit-tested with the first chunk failing, a middle append failing, and the last failing;
   - one summary in the result line: "Pool troops saved (13 names)." / "Pool troops stopped after 9 of 13: CHAR_X was
     refused (…). Fix it and Save again." / "Pool troops: no answer for CHAR_X — press Reload values."
7. **Still-missing reason.** Under the editor header: when the event's `[NYAR:def]` row is `state=disabled` with a
   reason other than `disabled`, a yellow box "Not ready yet: <reason>. It clears once the definition is valid; then
   press Enable." (defs are re-read after every set `ok`).
8. **Pool names for pickers.** From (a) pools saved in this editor session and (b) best-effort `pools: <name> (` lines
   of the captured `event info` text (server format, Appendix). Convenience only: typing any name always works.
9. **Pre-fill** (best effort): pursuit from `pursuit: after <s> s`. A field not read shows an empty box with its
   placeholder — never a guessed default — and a line says "Some values couldn't be read from the server's reply —
   type them" when the info capture arrived but a shown field's parse found nothing.
10. **"Any other field"** list gains `action.pursuit`, `action.pools.NAME`, `action.waveList.N.bosses`; Set refuses an
    unreplaced `NAME` / `N` / `M` placeholder.
11. **Escaping.** Every server-derived string the Nyar tabs render (names, reasons, info lines, unit rows, template
    summaries, pool names) goes through one escape that neutralises TMP rich text (`<` → `<` + zero-width space),
    including the existing `NyarText` helper.

### B2. Form audit (Kris, after round 4: "everything in well; user-friendly; front-load selections; admins don't have to
know acceptable values, the form takes proper ranges, values and types; editing is easy")
Findings against today's editor and the round-4 plan:
- **F1** Almost every field is a free text box with an "e.g." placeholder; ranges are hidden in tooltips or absent
  (Waves "e.g. 3", Level "e.g. 40", Behaviour "none / hunt 30 / regionhunt", Location "aroundplayer 20 40").
- **F2** Lists (units, bosses, pool members) are comma strings: changing one count or removing one entry means
  editing the string by hand.
- **F3** Counts, pool names, roll min/max and pursuit were planned as typed boxes.
- **F4** The editor shows units-form fields on wave-list events, and nothing warns that setting Wave 1 converts the
  event and removes its units, waves, interval and modifiers.
- **F5** Values are checked only on Set; nothing shows which fields were changed and not yet sent.
- **F6** Three rule-mirror errors (count optional, repeats per field, 50-unit ceiling with pools); corrected above.

Changes (all driven by one pure spec table, so the ranges are written once and unit-tested):
1. **`NyarFieldSpec`** (in `NyarFieldRules.cs`): per field, its kind (whole number, two-decimal number, text, choice,
   toggle, list), min, max, step, unit word, "none allowed", and a few presets, copied from the server ranges above.
   Every control below reads it; `CheckValue(field, value, ctx)` gives the exact sentence.
2. **Numbers become steppers:** `[−] [value] [+]` with the range printed beside it ("10-600 s"). Buttons clamp to the
   range, and typing is still allowed. Preset chips where admins usually pick round values: duration 5/10/15/30 min
   (apocalypse 1 h/6 h/1 d/7 d), cooldown none/30 min/1 h/1 d, chance 25/50/100 %, pursuit 30/60/120/300 s,
   multipliers ×1/×1.5/×2. Seconds and minutes are shown with a readable echo ("900 s = 15 min").
3. **Choices become buttons, not syntax:**
   - Behaviour: [Stay put] [Hunt players] [Roam the region], plus a range stepper (10-60 m) shown only for Hunt.
   - Location: [Here] [Whole region] [Around a player], plus MIN (10-60) and MAX (15-80) steppers. MAX is kept above MIN,
     and with Hunt it is kept at or below the hunt range, with a one-line reason.
   - Level: [Units' own] [Fixed level 1-120] [Shift −5..+5]. Picking one sends `none` for the other first, so the
     server never refuses "level or levelDelta, not both".
   - Toggles for loot and claimed territory.
   - Empower factions: a faction picker that builds up to 5 distinct chips.
   - Boss count: [1] [2] [3]. Roll min/max: two steppers (1-5) that keep min ≤ max. A units count stepper runs 1-50,
     default 1.
4. **Lists become rows:** units, bosses and pool members are shown as rows parsed from the value: name, count stepper,
   remove ✕. A pool entry shows as "12 from troops". Add comes from the unit picker or a pool dropdown, and the value
   text is rebuilt from the rows. An "Edit as text" toggle (closed by default) keeps the raw value for experts, synced
   both ways. Live counters: "entries 3 / 10", "units 34 / 50" (when a pool entry is in the list) and "bosses 4 / 5".
   Add is disabled at the limit, with the reason. Adding a repeated name to `action.units` raises that row's count.
5. **Pools:** the name is a dropdown of the event's pools, read from `pools: <name> (k units): …` lines, which give the
   full member list across wrapped lines, plus this session's saves and a "New pool…" option. The new-name box shows the
   normalised name live ("Troops 1" → "troops-1"). Picking a pool loads its members into rows to edit, and Save
   replaces the pool. "Add from pool" and "Add roll from pool" use the same dropdown. Typing a name is still possible
   under "Edit as text".
6. **The event's shape decides the form:**
   - From the info lines (`wave N:` lines → wave-list form), the editor shows either "Units per wave / Waves / Seconds
     between waves / modifiers" or the wave list, never both.
   - Wave list: the wave selector lists the existing waves with their entry counts ("Wave 2 — 3 entries"), plus "Wave
     N+1 (new)", which offers units only until they are set.
   - Units-form event: the selector offers "Turn into a per-wave list…". It needs a second confirm press and names
     what is removed ("Units per wave, Waves, Seconds between waves and modifiers are replaced by Wave 1").
   - Per wave: "Starts after (s)" stepper (10-3600, or "with the previous wave" = none), a "Wait until cleared"
     toggle, and "Remove wave" (last wave only; a middle wave shows why it can't be removed).
7. **Pre-fill from what the server already said:**
   - from `event info`: location (around a player MIN-MAX, here/region); behaviour (`hunt R m`, region-hunt line);
     modifiers (`level`, `levelDelta`, `name xV`); loot and territory flags; bosses (`final wave adds:` and `wave N
     adds:`, fixed `CHAR_x xC` and roll `a-b bosses from p`); wave units and start rules; pools and pursuit.
   - If the server shortened a line ("final wave adds: 4 bosses", or a too-long units line), that field shows
     "couldn't read — type or rebuild it" instead of a guess.
8. **Editing state:**
   - Each field remembers its loaded value. A changed field shows a yellow "changed" mark and its own Set. After the
     server's ok it shows a green "saved"; an error stays on that field.
   - "Save all changes (n)" sends the changed fields in form order through the same `NyarBatch` (one at a time, paced,
     stop at the first refusal, with the same quarantine rules). "Undo changes" restores the loaded values.
   - Order rules the server needs are applied by the batch order: a wave's units before its bosses or start rule, and
     `none` for the other level field before the chosen one.
9. **Check while typing:** each row shows the `CheckValue` sentence under it as soon as the value is invalid, and its
   Set looks disabled. Server-only rules keep their labelled hint (B, "Server-authoritative only").
10. **"Any other field":** after a field is picked, the value control comes from its spec (stepper, toggle, choice or
    list rows). `N` / `M` are chosen from wave and entry dropdowns instead of typed over.
11. **Scope guard:** this is the existing editor reorganised around one control kit (`NyarStepper`, `NyarChoice`,
    `NyarToggle`, `NyarListRows`) built from existing UIFactory pieces inside `MainPanel.NyarEditor.cs` / a new
    `MainPanel.NyarControls.cs`. No new framework and no new settings. The apocalypse and empower sections get the same
    steppers.

### C. Templates panel (MainPanel.Nyar.cs)
1. Group cards under headings: per pillar when "All" is selected; inside Spawns, three sub-groups by id suffix:
   **Wave events**, **Timed waves** (`-timed`: "a new wave every 75 s, cleared or not"), **Onslaughts**
   (`-onslaught`: "rolled waves, a boss mid-run and one or two at the end"). Unknown suffixes fall back to the plain
   group (naming convention, not a wire field).
2. Reminder at the top: Spawns templates need the Spawns pillar on — live state from the handshake (`waves=` flag)
   with a "Turn Spawns on" button (existing pillar twin) when off; "templates start where the admin stands: press
   Start in game where the fight should happen".
3. Paging is already "until `[NYAR:end]`" — verify only (34 rows / 4 pages).

### D. Texts for server changes since 0.71.0
1. `admin_location`: a small version parser `NyarVersion` (`major.minor.patch[-rc.N]`; anything else = unknown). The
   bug wording (+ Goal here) for any 0.13.0 prerelease below rc.2 (the fix's boundary: `d492bd5` is in rc.2, not rc.1).
   Versions below 0.13.0 have no apocalypse at all (it arrived in 0.13.0), so their `admin_location` can only come from
   a spawn-waves start without a readable position — they get the generic text, as do 0.13.0-rc.2 and later, a
   release, and an unknown/malformed string: "The server couldn't read your position for this event's goal — try again in a
   moment, or press Goal here in the editor." Unit-tested (rc.1, rc.2, rc.3, 0.13.0, 0.12.5, 0.14.0, garbage, empty).
   The Goal-here tooltip drops "Nyarlathotep can't start that yet".
2. `pillar_off`: if the line has `pillar=<id>` and the id is one of the five known pillars, name it; absent, empty,
   unknown or malformed → today's text. Tested with fixtures.
3. `docs/NYAR_FEEDBACK_2026-10-15.md`: a status table (item 1 fixed in rc.2; item 2 handoff corrected; R1-R3 logged as
   planned in contract §8/§10.5/§10.7; items 4-5 planned live-ops).

### E. Release
Bump to 0.72.0 via `tools/bump-version.ps1`; CHANGELOG + CHANGELOG.thunderstore + README (Nyar compat 0.13.0-rc.3, tab
line) + ROADMAP. Preflight Release. Package the zip. Deploying to the local test profile is the project's documented dev
step and is done only with the game closed. **Commit and push only on Kris's explicit OK**, asked for at the end.

## Testing
- **Pre-audit (done before code):** `dotnet build -c Release` succeeded; preflight PASS (0.71.0).
- **Committed logic tests:** new `tests/Raphael.Logic.Tests` console project (net6.0, same LangVersion/ImplicitUsings as
  the mod, no packages, not in `Raphael.sln`, so the mod build and package are untouched). It compiles the pure files
  `NyarFieldRules.cs`, `NyarBatch.cs`, `RailPolicy.cs`, `NyarVersion.cs` by link and asserts; exit code ≠ 0 on any
  failure. `tools/preflight.ps1 -Mode Release` runs it.
  - Rules: pursuit 14/15/600/601/none/"015"/"-5"/"1 5"; pool names 24/25 chars, upper case, `_`; members 30/31 and
    duplicates; unit name 96/97 chars, bad chars; units list 10/11 parts, counts 0/1/50/51, pool-token shapes; bosses
    3/4 parts, roll 1-5 / 0-1 / 3-2 / 1-6 / bad shape, totals 5/6 with mixed fixed+roll and wave lists, an all-waves
    fixed list of 3×3 = 9 allowed (no total rule there, as on the server); empty parts, spaces, quotes.
  - Chunking: the 30 longest catalogue names on a 32-char id; every emitted command ≤ 450 UTF-8 bytes; first chunk then
    `+name`s; their union equals the member list in order.
  - Batch: all ok; err mid-way (stops, rest dropped, summary names it); ratelimit then ok (one retry); ratelimit twice
    (stops); timeout (unknown); late answer after timeout (ignored); duplicate ok (ignored); answer for a non-batch
    command (ignored); cancel (drops queue); pacing never < 0.40 s between sends.
  - Rail: Nyar-only server (others diagnostic) → only Nyarlathotep opens; nothing detected → Bloodcraft diagnostic
    opens, then Nyar detected → Bloodcraft closes, Nyarlathotep opens; user opened Uriel, then rebuild → Uriel stays,
    nothing else opens; late Bloodcraft ACK while the user is in Nyarlathotep → nothing changes; user closed the
    primary → never reopened; first-run "Settings and Help" open → primary not opened next to it; the active tab's
    group wins over rail order; accordion off → same rules; Off group → closed; a user-opened group goes dead → rebuild → usable again →
    expanded (and, with accordion on and another user-opened group open, stays closed with Intent=Closed).
  - Version: the D1 cases. pillar_off: the D2 fixtures.
  - Form audit (B2): every `NyarFieldSpec` range's edges (min−1, min, max, max+1, and the decimals 0.49/0.5/3.0/3.01/
    1.234); units without a count; a repeat in `action.units` (refused) vs a wave list (allowed); a pool entry with
    51 units (refused) and 50 (allowed); list rows ↔ text round trip (parse, edit a count, remove a row, rebuild =
    expected string); the info-line parsers on server-format fixtures (pools wrapped over two lines, `wave N:`
    lines with and without "units below", `final wave adds:` fixed + roll and its shortened form → "couldn't
    read", `hunt 30 m`, `around a player 20-40 m`, `modifiers level 40, maxHealth x1.5`); shape detection
    (units form vs wave list); "Save all changes" ordering (wave units before bosses; the other level field's
    `none` first).
  - Batch correlation, scripted as protocol-line sequences fed to the pure `NyarBatch`: timeout → (no new step) → late
    ok for the old step (ignored); ok with wrong id/field (ignored); err while no step outstanding (ignored);
    `[NYAR:end]` mid-step (ignored); cancel with a step in flight (summary "may or may not"); timeout on a `+name` step → quarantine starts at the timeout; Save at +9.9 s refused, at +10.1 s allowed (boundary
    measured from abandonment); within the window a stale `ok` for the same id+field and a stale `err`
    arrive → no batch changes state; after the new batch starts, its own answers advance it. NyarClient itself uses
    Unity `Time` and `MessageService` and cannot be compiled outside the game; its part is kept to two lines
    (return the in-flight string; the twin gap), covered by the in-game test list.
- **Post-audit:** build (no new warnings), preflight Release (runs the tests), a self-review of the diff, then the fresh
  read-only Codex cross-inspection of the diff; fix accepted findings; re-run build + tests.
- **In game (Kris, listed in the reply):** Nyar-only and full servers; text-size change (rebuild); pillar toggle; the
  handoff §4 steps on a `wv-test` copy of `undead-onslaught`; a 20-name pool save.

## Key decisions & tradeoffs
- Auto-open the active mod's group for Uriel/Faust/Nyarlathotep too (today only Bloodcraft/Beelzebub) — only before the
  user's first rail click in the session, and never next to a group the user opened.
- Rail memory lives exactly as long as detection state (same teardown path).
- Tab clicks count as a user choice.
- Best-effort parse of the human `event info` text only for convenience defaults, never for decisions.
- Pool save is replace-then-append (server limitation, the handoff's own recipe), made safe by stop-on-first-error,
  "unknown" on timeout, a pre-send warning, and Save being a full replace that can be repeated.
- Cross-field rules the client cannot see reliably stay server-authoritative (listed in B) instead of guessing.

## Assumptions
1. Wire unchanged at api 8; rc.3 sends `plugin=0.13.0-rc.3` — handoff §2.
2. `[NYAR:ok] … field=<field> value=<stored, ≤120 bytes>` on every set; Raphael re-reads defs after a set ok —
   `NyarProtocolService.OnOk` sets `_needDefs`.
3. NyarClient sends one command at a time (single in-flight), ≥ 0.25 s apart, 5 s reply timeout — `NyarClient.cs`.
4. Chat line limit ~512 bytes; the server refuses lists > 4000 chars — `CommandArgs.CommaList`.
5. `NyarState.Waves` mirrors the Spawns pillar from the handshake — `NyarState.cs`.
6. `RebuildMainPanelNow` destroys + re-creates MainPanel and calls its `Reset()` — `BCHubUIManager.cs:1646-1660`.
7. The apocalypse admin-goal fix (`d492bd5`) is an ancestor of tag `v0.13.0-rc.2` — verified with git.

## Risks / open questions
- A partial pool after a refused or unanswered append (reported precisely; Save again fixes it).
- UnitCatalog's faction ≠ the server's allied group; the mix warning is advisory only.

## Appendix — server excerpts (Nyarlathotep `4f059f5`, read-only)
- `Logic/Validation.cs`: `MaxBosses = 3; MaxBossCount = 3; MaxPools = 8; MaxPoolMembers = 30; MaxPoolName = 24;
  MaxWaveBosses = 5; MaxRollBosses = 5; MinPursuitSeconds = 15; MaxPursuitSeconds = 600;`
  `PoolNameRx = new("^[a-z0-9-]{1,24}$")`.
- `Logic/CommandArgs.cs`: `MaxNameLength = 96`. `IsNameValue(name, prefix)`: `name.Length > prefix.Length`,
  `name.Length <= 96`, `name.StartsWith(prefix)`, every char in `[A-Za-z0-9_]`.
  `CommaList(value, max)`: null if empty or longer than 4000 chars, or any char `<= ' '` or `> '~'` or one of the
  quote, apostrophe, braces, brackets, angle brackets, backslash; split `,`; null if more than `max` parts or any empty.
  `BossList`: `none` → removal; more than `MaxBosses` parts → refused; per part: `pool:` → `bits = Split(':')`,
  `range = bits.Length == 3 ? bits[2].Split('-') : []`, shape check, `IsPoolName(bits[1])`, min 1..5, max min..5; else
  `name[:count]`, count 1..3, `IsNameValue(name, "CHAR_")`; then refused if `(waveLevel || rolls) && sum(MostBosses) > 5`.
  `PoolEntry`: `pool:` with exactly 3 parts, pool name, count 1..50. `PursuitValue`: `none` or 15..600.
  `PoolValue`: pool name, then `none` | `+name` | `-name` | CommaList(≤ 30), no repeats, all `IsNameValue`.
- `Logic/CommandArgs.cs` `WaveListPath`: `static int? Index(string t) => t.Length is 1 or 2 && t[0] != '0' &&
  int.TryParse(t, NumberStyles.None, …, out var i) && i is >= 1 and <= 10 ? i : null;`
- `Logic/EventAdmin.cs:430`: "action.waveList has {n} waves; the next one is {n+1} (set its units first)".
- Handoff `RAPHAEL_HANDOFF_WAVE_VARIETY.md` §3a: "build big pools by appends: send the first few names, then one
  +<prefab> per twin (queue them, one every 250 ms)"; removing a pool an entry still names is refused.
- Contract §5a: at most 5 twins per admin per second, sliding; every line ≤ 480 bytes; `value` cut to 120 bytes.

## Out of scope
Map-wide units read / field read (not on api 8); Uriel / Faust feature work; a run HUD; automating Thunderstore.
