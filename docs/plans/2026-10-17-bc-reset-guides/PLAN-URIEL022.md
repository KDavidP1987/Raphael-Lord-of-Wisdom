# Plan: Raphael 0.73.0 — Uriel 0.22.0 admin controls and kill switches
_Draft by Claude for Kris (2026-10-18). Reviewed by Codex in 3 rounds (PLAN-REVIEW-LOG.md); awaiting Kris's sign-off._

**Decisions (Kris, 2026-10-18):**
- fold into 0.73.0;
- a `code=disabled` version reply counts as present with objects off;
- rework Admin: Config into Admin: Server controls, with no new rail tab;
- off features are greyed with the reason, never hidden.

## Context
Uriel 0.22.0 (commit 0bad5fe) adds:
- a master switch, `[General] Enabled`;
- two feature switches: event doors (`[CastleEvents] DoorGates`) and paid shares (`[PublicStorage] CostsEnabled`);
- a one-shot reset at boot, `[KillSwitch] RestoreVanillaOnBoot`;
- admin commands: `.uriel features`, `.uriel killswitch [confirm]`, `.uriel purgeall [confirm]` and `.uriel event
  stopall`.

ApiVersion stays 1 and there is no new `[URIEL:*]` line, so everything new arrives as human text in system chat.
Handoff: `Uriel/Uriel/docs/RAPHAEL_ADMIN_CONTROLS_HANDOFF.md`. Raphael works with 0.22.0 today, except in the case
described under "Blocker" below.

Kris's standing asks:
- clean and well-formatted work, with pre- and post-audits;
- forms that are easy to use;
- **the main menu (left rail) must not be disturbed.**

## Pre-audit (2 read-only agents, 2026-10-18)

### Uriel source vs the handoff: 12 differences, and the code wins
1. **The priced-chest refusal** is `[Uriel] This container has a price, and paid shares are switched off on this
   server — it's closed until its owner removes the price.` It has a `[Uriel] ` prefix, and the dash is an em dash.
2. **Not every refusal contains "disabled by the server admin".** These do not:
   - `Object spawning is admin-only on this server (ObjectSpawn.AdminOnly).`
   - `Castle events are admin-only on this server (CastleEvents.PlayerEvents).`
   - the priced-chest line above.
3. **The features values are effective states.** `(admins only)` appears only while that feature is on.
   - With chests off, prison cells and paid shares read OFF.
   - With events off, event doors reads OFF.
   - The default config has `[CastleEvents] Enabled=false`, so it reads "NPC units and castle events OFF, event doors
     OFF".
4. **Kill switch confirm** sends each line as its own system message:
   - the "Kill switch done: …" line;
   - optional `Events|Units|Objects|Storage: failed (…).` lines;
   - an optional line for objects not found yet;
   - an optional line for doors not found yet.

   Purgeall confirm and stopall each send one message.
5. **Any argument other than `confirm` re-arms** killswitch and purgeall. A confirm sent after 30 s gets `Not armed.
   Run '.uriel killswitch' first.` (or `… purgeall …`), and nothing runs. Each command has one static arm slot,
   shared by every admin.
6. **`event door` and `unit door` check the events switch first.** While an event is running on the plot, `event
   door` gives "An event is running on this plot…" before it checks DoorGates.
7. **Unit purge replies:**
   - arm: `This removes every Uriel unit on the server (<n> tracked, <m> pending). Run '.uriel unit purge confirm'
     within 30 s.`
   - confirm: `Purging <q> unit(s); cancelled <d> pending spawn(s).`
8. **`share … cost <id> <n>` with paid shares off** sends two messages: the share reply, then the cost refusal.
9. **`[URIEL:version] objectspawn=` now follows the master switch.** `adminonly=` is still the raw config value.
10. **`.uriel features` is one plain `ctx.Reply` line**, with no colour markup:
    `Uriel features:[ Master switch [General] Enabled is OFF: everything below is off.] Stair swap on|OFF. Object
    spawning on|OFF[ (admins only)]. NPC units and castle events on|OFF[ (admins only)], event doors on|OFF. Public
    chests on|OFF, prison cells on|OFF, paid shares on|OFF. Changes to the config take effect at the next server
    start.`
11. **Admin-only:**
    - features, killswitch, purgeall, event stopall, unit *, purgeplot, forcedespawn and purgeorphans;
    - sharedall, unshareplayer and unshareall.

    Player commands: event door, undoor and stop; share, paychest and takeprisoner.
12. **The cleanup commands now work with their feature off:** purgeplot, forcepurgeplot, forcedespawn, purgeorphans,
    and unit list, kill, despawn, clear and purge. Purgeall also needs Uriel to have finished initialising.

### Raphael today
- **Rail.** The Uriel group is gated as a whole by `IsTabGroupAvailable("Uriel")`, which is `UrielState.Present`
  (MainPanel.cs:1557).
  - Per-tab hiding is deliberately a no-op (`ApplyTabAvailability`, :1229).
  - Admin pages are always listed and greyed by `BeginAdminGate`.
  - Rail state changes only through `RefreshAllTabGroupAvailability`, `ReconcileRailExpansion`, `ApplyRailChanges`,
    header or tab clicks, and the `UrielProtocolService.AvailabilityChanged` / `UrielState.PresenceChanged` events,
    which are wired to the rail refresh (:996-997).
- **Blocker.** `.uriel api version` answers `[URIEL:err] code=disabled` whenever object spawning is off, which
  includes master off (ApiCommands.cs:26).
  - `OnErr` doesn't set Present, so the Uriel group never becomes available on such a server.
  - That leaves the admin no way to reach any server controls exactly when they matter most.
- **Hiding controls.**
  - Only two refusals are noticed today: castle events, which set a label (UrielEvents.cs:133), and `code=disabled`
    during a scan.
  - Admin: Config (MainPanel.Uriel.cs:1432) and the guide callout (UrielGuide.cs:158) both claim that Raphael "hides
    or disables the matching controls". **It doesn't yet.**
- **Confirm.** `AddUrielConfirmButton` is press-twice within 3 s, with one shared pending slot; the label isn't reset
  when the time runs out.
  - Unit purge is two such buttons (Arm, Confirm), sent through `EvSend`, so the reply shows in the Admin: Units
    "Server reply" card.
- **Chat.** Uriel human lines go through observe-only hooks (`UrielEvents.Observe`, ClientChatPatch.cs:293).
  - `MessageService.HandleInboundChat` destroys a line only on a positive, correlated match (the BcResetRunner
    rule).
  - No `.uriel` command arms the generic "Last server response" capture.
- **Guide.** `UrielGuide.cs` says 0.21.0.
  - The fixtures (`tests/fixtures/uriel-commands.json` / `uriel-config.json`) are at 0.21.0 / 5a3d4c8.
  - Preflight's `guide-commands.py --check` will fail until they are regenerated.
  - GuideTests then require rows for the 4 new commands and 4 new keys, with `.Destructive()` on purgeall and
    killswitch.

## Changes

### 1. Pure model: `Services/Uriel/UrielFeatures.cs` (new, Unity-free, linked into the tests)
- `enum UrielFeature { Master, Stairs, Objects, Events, Doors, Chests, Cells, Costs }`, with a tri-state of Unknown,
  On or Off per feature. **Unknown is treated as on (shown)**, as today.
- `Parse(string line)`:
  - Strips tags, requires `^Uriel features:` and the closing "Changes to the config…" sentence, and reads each of the
    7 `<name> (on|OFF)` tokens plus `(admins only)` for objects and events.
  - If any token is missing, it returns null (no guess).
  - The master is Off when the "Master switch … is OFF" clause is present.
- `ClassifyRefusal(string line) → (UrielFeature, Off) | AdminOnly(feature) | null`, matching the exact verbatim
  strings from the audit:
  - stairs, objects, events, doors, chests, both prison-cell forms, paid shares, and the `[Uriel] ` priced-chest line
    (which means paid shares are off);
  - the two "admin-only on this server" lines are classified as admin-only, not off.
- `Effective(feature)` applies the dependencies: cells and costs need chests, doors need events, and everything needs
  the master.
- `Gate(feature) → (enabled, reason)`, for example "Paid shares are off on this server (PublicStorage.CostsEnabled)."
- `ClassifyControlReply(string line)`, which recognises the arm, done, not-armed, stopall and unit-purge lines for the
  in-panel reply card.

### 2. State and wiring: `UrielFeatureState` (in the mod, thin wrapper)
- The state is set by a features line (admins) or a refusal (everyone).
- A refusal only turns a feature **off**. A later features line overrides it either way.
- **One new event, `UrielFeatureState.Changed`. It never fires `AvailabilityChanged` or `PresenceChanged`, and never
  calls any rail function.**
- It is cleared in `UrielProtocolService.Reset` (logout), as pure field resets.
- `[URIEL:version] objectspawn=0` sets Objects to Off. That uses the existing field, with no new parse.
- **Provenance:** the observe call sits next to `UrielEvents.Observe` (ClientChatPatch.cs:293), after the existing
  `ev.MessageType != ServerChatMessageType.System` filter (:226). Player chat is never System, so a player can't
  imitate a features line or a refusal. The parse is also exact (anchored text, every token required).
- **No destroy (round 1).** Every Uriel line stays in chat, including the features reply to Raphael's own probe. The
  features line is one short line, and dropping the destroy removes the correlation risk of eating a reply the admin
  typed. The panel reads the line wherever it came from.
- **Who probes `.uriel features`:**
  - opening Admin: Server controls, and its Refresh button;
  - once, automatically, right after a `code=disabled` version reply, when `MessageService.IsLocalAdmin()` is true
    (the check `BeginAdminGate` uses). The admin then sees in chat what is off. Not repeated on retries.
- **Non-admins** can't run `.uriel features`. After a `code=disabled` reply, Uriel's pages show one muted line:
  "Uriel reports object spawning off; other features may be off too. A refused action greys its controls." Each
  refusal then greys its own feature.
- **Unknown stays usable.** A feature with no reading yet is shown as "unknown" and its controls stay enabled. That
  is today's behaviour, and it is needed for 0.21.0 servers, which have no features command. Nothing destructive
  depends on feature state: the Server controls buttons work with every feature off.

### 3. Fix the blocker (rail-safe)
- On the version probe only: `[URIEL:err] code=disabled` in reply to `.uriel api version` marks Uriel **present,
  with object spawning off**.
  - `ApiVersion` stays 0, so no catalog or unlocked scans run.
  - Objects is set to Off.
- This is the same single detection transition, through the same path that a `ready=1` version uses today. It is a
  presence change, not a new rail mechanism.
- Probing, retries and give-up behaviour are otherwise unchanged.
- **Test:** a server answering `code=disabled` shows the Uriel group, with the object tabs greyed and their reason
  shown.

### 4. Admin: Config becomes "Admin: Server controls" (same PanelType, so the rail order and stored values don't change)
Only the tab's label and page content change. No new tab and no new rail entry.

**Card "What's switched on"** (refreshed on open and by a Refresh button):
- One row per feature: "on", "OFF" or "unknown", plus "(admins only)" where it applies.
- A master-off banner when the master switch is off.
- The line "Config changes take effect at the next server start."
- The existing object-spawning snapshot (discovery mode, totals) is kept below.

**Card "Undo what Uriel did"** (admin-gated as today). Each action shows the server's reply lines verbatim in a reply
box under the card.

| Button | Sends | Flow |
|---|---|---|
| **Stop all events** | `.uriel event stopall` | press twice |
| **Remove all units** | `.uriel unit purge`, then `… confirm` | Arm, then Confirm |
| **Remove all objects** | `.uriel purgeall`, then `… confirm` | Arm, then Confirm |
| **Restore vanilla** (red) | `.uriel killswitch`, then `… confirm` | Arm, then Confirm |

- **Arm** is one click: it is harmless, and the server says what it would remove.
- **Confirm** stays disabled until the server's arm line is seen, and is then enabled for **25 s** (a 5 s margin
  under the server's 30 s), with a countdown in its label. A press within that window still asks for the
  press-twice confirmation.
- If the window passes, Confirm greys out again and the text says "Arm again".
- A `Not armed…` reply resets the Confirm button.
- A short note on Restore vanilla explains what it removes and what it keeps:
  - removes: every event, unit and object, and every share;
  - keeps: saved settings and swapped stairs;
  - the reminder "To keep it off afterwards, set [General] Enabled = false and restart."

**The control flow is a pure state machine** (`UrielControlFlow` in `UrielFeatures.cs`, driven by timestamps, with
no Unity timers), one slot per action:
- **States:** Idle → ArmSent → Armed (until **arm-send time** + 25 s) → ConfirmPending (first press, 3 s) →
  ConfirmSent → Done | Partial | NoReply | Expired. Stop all events has no arm: Idle → ConfirmPending → Sent → Done |
  NoReply.
- **The window counts from the send (round 3):** Uriel's 30 s start when the server handles the arm, which is after
  Raphael sends it. So the 25 s run from the send time, never from when the reply arrives. A late arm reply can't
  extend the window, and one arriving after the window is shown as Expired at once.
- **Terminal states are results, not activity:** Done, Partial, NoReply and Expired keep their text in the reply box
  and the button labels, but they have no deadline. `AnyActive` is true only while a slot has a pending deadline: the
  ArmSent/ConfirmSent wait, the Armed countdown, the 3 s press, or the 3 s aggregation window.
- **Press twice inside the slot:** the second-press step lives in each action's own slot, so these four buttons don't
  use the shared `AddUrielConfirmButton` pending slot and can't interfere with each other or with other Uriel
  buttons.
- **Deadlines:** ArmSent and ConfirmSent each wait 10 s for their reply. With no reply, the slot goes to NoReply and
  the reply box says "No server reply. Check chat, then try again." ConfirmPending falls back to Armed (or Idle) after
  3 s.
- **Ownership:** only locally correlated replies change state: a reply moves an action only while that action is
  ArmSent or ConfirmSent, inside its 10 s deadline. The arm, done and not-armed texts name their command, so a
  killswitch line can't move purgeall's slot. A matching line Raphael didn't ask for (the admin typed the command) is
  shown in the reply box but changes no button.
- **Result aggregation:** a correlated killswitch done line opens its own 3 s window, independent of the 10 s
  deadline. The follow-up lines (`… failed (…)`, objects not found yet, doors not found yet) arriving in that window
  join the same reply. A failure line makes the result **Partial**, shown in the reply's header ("Done, with failures:
  Storage"). Purgeall, stopall and unit purge reply in one line.
- **Reset:** every slot returns to Idle on `UrielProtocolService.Reset` (logout or disconnect), when
  `IsLocalAdmin()` turns false, and when a new arm is sent for the same action.
- **Time passing (the updater):** while `AnyActive` is true, one ticker on `CoreUpdateBehavior.Actions` calls
  `UrielControlFlow.Tick(now)` about once a second. Tick returns the slots that changed: expiry, a deadline, the
  aggregation window closing. The ticker refreshes the countdown labels and the buttons' `interactable`. It
  unregisters itself when `AnyActive` is false, on Reset, and when the page's objects are gone. This is the same
  self-unregistering ticker pattern as the MessageService init poll in CLAUDE.md.
- **The shared arm slot (server limit):** Uriel keeps one arm per command for all admins, and another admin's
  replies don't reach this client. A remote re-arm or confirm can't be seen. This client finds out on its next own
  reply (for example "Not armed…" after a confirm), which resets the slot. A muted note under the card says so. A
  per-admin arm token goes into the ROADMAP row for Uriel.

**After a done line:**
- Restore vanilla marks the share, object and event tabs stale (`MarkTabStale`) and clears the shared-container cache,
  partial or not.
- Remove all objects marks only the object tabs stale (Objects, Object Catalog, Admin: Objects).
- Remove all units and Stop all events mark the Castle Events tabs stale.
- Nothing touches the rail.

**The existing Admin: Units Arm/Confirm purge** stays as it is. It gets the same arm-seen gating only if that is
cheap and the shared helper fits. Otherwise this plan leaves it alone.

### 5. Disable what's off (in place, never hidden from the rail)
Controls are greyed with the reason, using `interactable` plus a one-line note on the card. Nothing is hidden by
`SetActive`, so the layout doesn't jump.

**Updating in place:** the panel subscribes once to `UrielFeatureState.Changed`, through the existing
`EnsureXSubscribed` / `UnsubscribeX` pattern (MainPanel.Beelzebub.cs:913-955), which is called from
`MainPanel.Reset()`. The handler walks the registered gate targets (control, feature) and sets each one's
`interactable` and note. Targets are stored per page, keyed by the tab's PanelType. A page's rebuild replaces only
its own entry, and a destroyed control is skipped and dropped.

| Off | Greyed |
|---|---|
| Public chests | the Storage tab's action cards |
| Paid shares | the cost item and amount inputs, the cost part of a custom share, and Set pay chest |
| Prison cells | the share cell, unshare, show rules and take prisoner controls |
| Stair swap | the style, next, show styles and remove stairs controls (the admin stair purge stays, as cleanup) |
| Event doors | bind door only. Unbind (`undoor`) stays enabled: it still works server-side, as cleanup. |
| Castle events | the existing label, plus greyed Start and run controls (stop and stopall stay enabled) |
| Objects | the spawn controls on Objects and Object Catalog (Admin: Objects cleanup stays enabled) |

- Cleanup controls are never greyed, because they all work with their feature off.
- The two false "Raphael hides…" texts become true, and are reworded to "greys out, with the reason".

### 6. Guide, docs, fixtures
- **Regenerate the fixtures:** `python -I tools/guide-commands.py` (reads Uriel at 0bad5fe).
- **UrielGuide.cs → 0.22.0:**
  - new cfg rows: `[General] Enabled`, `[KillSwitch] RestoreVanillaOnBoot`, `[PublicStorage] CostsEnabled` and
    `[CastleEvents] DoorGates`;
  - new admin rows: features, killswitch (`.Destructive()`), purgeall (`.Destructive()`) and event stopall;
  - a "Turning Uriel off / restoring vanilla" troubleshooting entry;
  - the Server controls tab row;
  - corrected wording about greyed controls.
- **README:** Uriel compatibility v0.22.0 (API 1), and the Uriel features line mentions Server controls.
- **CHANGELOG** (both files): a 0.73.0 Uriel section.
- **ROADMAP:** a row for adopting `.uriel api features` (Uriel's proposed machine line) once it ships, and a request
  for a per-admin arm token.

## Verification
1. **Codex plan review** through the runner (read-only, at most 3 rounds, logged in PLAN-REVIEW-LOG.md under a
   "Uriel 0.22.0" section), after the open Nyarlathotep loop is signed off.
2. **Logic tests** (UrielFeaturesTests.cs):
   - Parse:
     - all on;
     - the default config (events OFF);
     - master off;
     - admins-only on and off;
     - with markup;
     - a missing token returns null;
     - another mod's line returns null.
   - ClassifyRefusal: every verbatim string, the admin-only lines not counted as off, and the priced-chest line with
     its em dash.
   - Effective dependencies and Gate reasons.
   - ClassifyControlReply: each arm, done and not-armed form, the kill switch done line with failure lines and
     follow-up lines, and stopall with and without pending doors.
   - **UrielControlFlow:**
     - arm → armed → first press → second press → done;
     - expiry through `Tick` at 25 s (24.9 armed, 25.0 expired, reported as changed);
     - the first press falls back after 3 s;
     - ArmSent and ConfirmSent with no reply become NoReply at 10 s;
     - not-armed resets the slot;
     - a reply while the slot isn't waiting changes nothing, and a killswitch line doesn't move purgeall;
     - a done line at 9.9 s opens aggregation, and a failure line at 12 s still joins it (Partial); a line at 13.1 s
       doesn't;
     - a reset clears every slot, and a re-arm restarts the window;
     - `AnyActive` is false once every slot is Idle or terminal (Done, Partial, NoReply, Expired), which is what
       unregisters the ticker;
     - an arm reply arriving 6 s after the send leaves 19 s, not 25; one arriving after 25 s is Expired at once.
   - **Ticker guard (source scan):** the Server controls ticker removes itself from `CoreUpdateBehavior.Actions` when
     `AnyActive` is false and in the Uriel unsubscribe/reset path, and it calls `Tick`.
   - **Rail guard:** extend the existing source scan so no `MainPanel.Uriel*.cs` or `Services/Uriel/*.cs` file calls
     `RefreshAllTabGroupAvailability`, `ReconcileRailExpansion`, `RequestRebuildMainPanel` or `ApplyRailChanges`, and
     `UrielFeatureState` never raises `AvailabilityChanged` or `PresenceChanged`. The test project has no Unity, so
     this scan, plus the in-game check, is the guard. The only rail event in this change is the existing first
     detection.
   - **Subscription guard:** the source scan checks that `UrielFeatureState.Changed +=` appears exactly once, inside
     the Ensure method, and has a matching `-=` in the Unsubscribe method.
   - The guide tests pass against the regenerated fixtures.
3. **Build** with 0 errors and the 4 baseline warnings. Run the full tests, dev preflight and Release preflight.
4. **Codex post-build inspection** (fresh, read-only, at most 2 rounds), logged. It is shared with the Nyarlathotep
   build.
5. **Deploy** to both DLL paths with the game closed, with .bak backups and verified hashes.
6. **In game (Kris), added to the 0.73.0 list:**
   - On a 0.22.0 server with everything on: Admin: Server controls shows the rows. Stop all events. Arm and Confirm
     Remove all units, Remove all objects, then Restore vanilla. The replies show in the box, and the share and
     object lists re-read.
   - Let a Confirm time out: it greys out and says "Arm again".
   - With `[PublicStorage] CostsEnabled=false`: the cost fields and Set pay chest are greyed with the reason.
   - With `[General] Enabled=false`: the Uriel group still appears, Server controls works, and the feature controls
     are greyed.
   - **The left rail:** on a server answering `code=disabled`, the Uriel group appears once on detection, as it does
     on any Uriel server. After that, the expanded groups, the selected tab and the scroll position stay as you left
     them through everything above.

## Critical files
- `Services/Uriel/UrielFeatures.cs` (new), `UrielProtocolService.cs`, `UrielState.cs`, `UrielEvents.cs`
- `Patches/ClientChatPatch.cs`, `Services/MessageService_Processing.cs`
- `UI/ModContent/MainPanel.Uriel.cs`, `MainPanel.UrielEvents.cs`
- `MainPanel.cs`: only the Admin: Config tab label
- `Services/Guides/UrielGuide.cs`
- `tests/Raphael.Logic.Tests/UrielFeaturesTests.cs` (new), the test csproj link, `tests/fixtures/uriel-*.json`
- README, CHANGELOG(s), ROADMAP

## Out of scope
- Uriel's proposed `.uriel api features` line, which is not implemented server-side yet.
- Coffin, door-access and rental features.
- Any change to rail mechanics.
