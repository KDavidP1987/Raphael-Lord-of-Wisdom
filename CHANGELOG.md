# Changelog — Raphael, Lord of Wisdom

## 0.72.0 — Nyarlathotep wave variety, a friendlier event editor, and a steadier left rail

Works with Nyarlathotep 0.13.0-rc.3 (api 8). Older 0.13 builds still work; the new fields need rc.3.

### New — Nyarlathotep event editor

- **The editor is now a form, not a list of text boxes.** Every field you can read is filled in from the server, and
  you pick values instead of typing them:
  - numbers have - / + buttons, the allowed range, and one-click presets; a value outside the range can't be sent;
  - choices (where, behaviour, level, wave start, yes / no) are buttons;
  - unit lists are rows with a count, a unit picker and Remove. "Edit as text" is still there.
- **Change, then save.** Changed fields turn gold, saved ones show ✓, and a refused one turns red with the server's
  reason. Each field keeps its own **Set**. The bar at the top shows **Save all changes (n)** and **Undo**.
  Saves are paced, one at a time, and stop at the first refusal.
- **Per-wave lists (new in rc.3).** An event can have a different unit list for each wave, its own bosses per wave,
  and a start rule per wave (when cleared, after a time, or whichever comes first). **Turn into a per-wave list…**
  converts a units-form event; you can add a wave or remove the last one.
- **Unit pools (new in rc.3).** Create, edit and delete named pools of units, and add a pool to any unit list as a
  rolled entry ("2-4 from pool X").
- **Bosses.** Fixed and rolled bosses for the final wave and for each wave.
- **Pursuit (new in rc.3)**: how far units chase before giving up, or no limit.
- **Where.** Whole region or around a player, with near / far distances and **Use where I stand**.
- Marks what it couldn't read from the server's text, instead of guessing.

### New — Nyarlathotep templates

- Templates are grouped by pillar, with the new **Timed waves** and **Onslaught** spawn templates in their own
  sections and a one-line description of each.
- If the Spawns pillar is off, the Templates tab says so, with a **Turn Spawns on** button.

### Fixed

- **Left-rail groups opening by themselves.** Changing UI text size, a mod being detected late, or a Nyarlathotep
  setting changing could open groups you had closed (and with accordion mode on, open a second group). The rail now
  remembers what you opened and closed for the whole session, and opens a group by itself only before you've touched
  the rail.
- Unread server text with a `<` in it no longer breaks the formatting of the editor.
- Texts updated for the Nyarlathotep fixes since rc.1 (the zombie apocalypse "admin location" refusal is fixed
  server-side in rc.2).

### Behind the scenes

- A logic test project (`tests/Raphael.Logic.Tests`, 430 checks) covers the server-rule mirror, batch saves, the
  rail policy, `event info` parsing and the form state. `preflight -Mode Release` now runs it.

## 0.71.0 — Nyarlathotep event editor, Admin: Units, and fixes from play-testing

### New — Nyarlathotep

- **Event editor.** **Edit…** on an event now opens its settings right under it. Before, it only copied the id into
  a box further down the page, so it looked like nothing happened.
  - The editor reads the event's current definition (`.nyar event info`), shows it, and fills in the fields it can read.
  - Each field has its own Set. Sections follow the event type: basics, conditions, where, spawn waves (waves,
    interval, radius, units, behaviour, level / health / power), empowerment stats, or apocalypse (goal, hordes,
    roamers, march speed, bosses). Any other field is still available from the full list.
  - **Goal here** sets an apocalypse's goal to where you stand.
- **Unit picker.** Every unit field has a type-to-search list of about 430 game units, showing faction, level and
  V Blood. "Add to list" builds the `CHAR_name:count` value for you.
  - The same list now powers Uriel's Castle Events and unit pickers, which used to offer about 90 units.
- **Admin: Units** (new tab):
  - scan the event units around you (5-100 m), shown as a table with event, level, health, time left and x / z position;
  - unit counts for each running event;
  - the test spawn, now with the unit picker.
  - Nyarlathotep can't list every unit on the map yet. Raphael has asked for that, and the tab will use it once the
    server adds it.

### Fixed

- **"[vcf] [denied] events / templates / pillar / killswitch" on login for non-admins.** Pre-building the admin tabs
  sent admin reads for everyone. They now go out only when the server says you're an admin. Each denied read also
  held up Live Events for 5 seconds.
- **Admin rights gained mid-session are picked up.** After `adminauth`, Raphael re-reads Nyarlathotep's version line,
  so the admin tabs load without a relog.
- **"Command not found: .beelz / .uriel / .faust api version" in chat.** These are Raphael's own detection probes on
  servers without those mods. They are now hidden even when you sent a command at the same moment.
- **Other mods' menu groups popping open.** Changing settings in a Nyarlathotep tab (for example, a pillar) re-opened
  the Bloodcraft, Beelzebub, Uriel and Faust "not detected" groups. They now open only once, when nothing else is
  detected, and never after you've closed them.
- **Event list stuck on "ending" after an event finished.** An ended event's units take about 30 seconds to leave,
  and the server sends nothing when they're gone. Raphael now re-reads the list when that countdown runs out.
- **Clearer start errors.**
  - "Pillar off" now says to turn the pillar on in Admin: Pillars.
  - The zombie apocalypse's "admin location" refusal now explains the server bug and how to work around it: stand at
    the goal, press **Goal here**, then Start.

### Notes

- The zombie apocalypse start failure is a Nyarlathotep bug, reported with the cause and fix in
  [`docs/NYAR_FEEDBACK_2026-10-15.md`](docs/NYAR_FEEDBACK_2026-10-15.md), along with requests for a map-wide units
  read and a field read.

## 0.70.0 — Uriel Castle Events (alpha)

Needs Uriel 0.21.0 with `CastleEvents.Enabled=true` on the server. Players also need
`CastleEvents.PlayerEvents=true`; otherwise the tabs are for admins only.

### New — Uriel

- **Castle Events** (new tab):
  - **Layout builder:** type a group name, then drop mob or boss markers at your feet. Bind or unbind the
    nearest door, set the group's squad, remove a marker by its #id or remove a whole group, and show the layout.
  - **Marker options:** unit (with a picker of common units), count, level and containment: server default,
    plot, room, storey, a 3-60 m leash, or room plus leash.
  - **Run panel:** a template field and Start / Stop / Status / History. Options are built from a difficulty
    preset, a level mode (set, shift, or auto +N), health / damage / speed / attack-speed multipliers, a time limit
    and a keep-factions switch. The panel shows the exact command before you send it.
  - **Live run readout:** the clock, groups cleared, bosses left and time left. Raphael reads these from Uriel's
    run messages, and Status re-syncs them.
  - **On-screen notices** when a run begins, its clock starts, a group falls, the bosses fall, it completes,
    time runs low, or it fails or is stopped. Switch them off on the tab.
- **Event Library** (new tab): create, show and delete squads, floor templates and templates. Add squad entries
  (role, units, count, any/distinct pick, level, set), extend an entry with more units, set a set's weight, and
  remove an entry or a set. Assign squads to groups and floor templates to floors, and set a template's default
  run options (or copy them from the run panel).
- **Admin: Units** (new tab): spawn units (at your feet or your aim point, with containment), kill nearby units
  (all / bosses / mobs), despawn the nearest, list, status, and clear the plot. Purge the whole server with an
  arm-then-confirm step. Also room / probe / door diagnostics.
- Each of these tabs shows the server's reply to your last button, so marker #ids, floors and errors appear
  without reading chat.
- Raphael won't send a command longer than chat allows (about 120 characters). The game would cut it, and the
  cut-off part would fall back to defaults.
- On first open the Castle Events tab checks whether Castle Events are on, admin-only, or off on the server.
- **Guides:** Uriel Help has a Castle Events section, and Uriel Quick Start adds a step 5 and lists Castle Events
  among Uriel's features.

### Notes

- Uriel doesn't send machine-readable Castle Events data yet. Raphael reads Uriel's chat replies and run messages,
  so the live readout depends on that wording. Raphael's answer to Uriel's proposed data format is in
  [`docs/URIEL_CASTLE_EVENTS_API_REPLY.md`](docs/URIEL_CASTLE_EVENTS_API_REPLY.md).
- Compatibility table updated: Uriel v0.21.0 (API 1).

## 0.69.0 — Chat fixes (#1, #2) and Nyarlathotep 0.13 support (API 8)

### Fixed

- **Copied chat keeps text in angle brackets** ([#1](https://github.com/KDavidP1987/Raphael-Lord-of-Wisdom/issues/1)).
  Copy used to delete everything from `<` to `>`, so roleplay text like `<telepathy>` was lost.
  - Copy now removes only real formatting tags.
  - In player chat, a `<` that isn't a style tag shows as plain text.
  - Unclosed tags are closed, so one message's italics no longer carry into the next.
- **Whispers go to the person you typed** ([#2](https://github.com/KDavidP1987/Raphael-Lord-of-Wisdom/issues/2)).
  Picking one player in the dropdown and typing `/w` to another used to whisper the dropdown's player.
  - Names now resolve against the list of online players first.
  - Message senders no longer get matched to the wrong player after a skipped duplicate.
  - `/w Name`, the dropdown and Tab cycling now share one whisper target.
  - The All tab remembers its own pick.

### New — Nyarlathotep

Needs Nyarlathotep 0.13.0-rc.1 (API 8) unless noted. Older servers keep the previous behaviour.

- **Live Events:** region tags on each event (API 5+), a Regions list, and an "ending — units leaving" state.
- **On-screen notices** for:
  - an event beginning or ending;
  - wave warnings;
  - a wave arriving (off by default);
  - the kill switch;
  - a summary when you log in.

  Turn each one on or off, and set how long notices stay up, in Nyarlathotep → Settings.
- **Admin: Events:**
  - Each action's result shows on the tab, with plain-language errors (API 4+).
  - New buttons: Unstick, New, Copy, Delete, Location here, Info, Units near me.
  - A field picker for editing one setting.
  - Rows show the trigger, the action, the duration, the regions, and why an event is disabled.
- **Admin: Templates** (new tab, API 4+): browse the built-in templates by pillar and create an event from one.
- **Admin: Pillars** (new tab, API 4+): switch each pillar on or off. Switching one off asks you to confirm first.
- **Kill Switch:** Purge is one confirmed click. The tab shows how many events and units are still active.

### Changed

- Background requests to Nyarlathotep now go out one at a time, at least 250 ms apart, as the server's contract asks.
- **Stats and defended zones** aren't in Nyarlathotep yet. Leaderboards, My Stats and Admin: Zones now say so
  instead of showing buttons the server would reject.
- **Admin: Announce:** the "digest" button is gone. The server broadcast the literal word "digest".
- Updated the Beelzebub and Nyarlathotep guide pages and the About tab.
- README: added a Roadmap section and updated the tested versions (Nyarlathotep 0.13.0-rc.1, Faust 0.16.5).

### Not in this release

- Nyarlathotep live-ops (API 9) and in-game communications (API 10). The server hasn't finalised them yet.

## 0.68.1 — Beelzebub 0.137.3-0.137.5 support (API 35)

Catches up to Beelzebub 0.137.3-0.137.5 (`ApiVersion 35`). No `[BEELZ:*]` line changed shape. New gates in
`BeelzState`: `SupportsSaddleRCT` (api>=34) and `SupportsLayeredClearBar` (api>=35). Older servers keep the old behavior.

- **Mounted loadout uses slots 5/6/7 (R / C / Ultimate)** on api>=34; `BeelzClient.FormAllowedSlots("Mounted")`
  returns {5, 6, 7} there (3/6/7 before). The slot rows, per-ability bind buttons and the "only binds..." note follow.
  The server moves saved slot-3 saddle binds to 5 and rejects `form-grant mounted 3`.
- **Clear bar / Clear set re-reads `api slots`** after `.beelz clearbar` on api>=35. Clearbar now re-resolves the whole
  live bar (and can dismount or end a transform) even when nothing was bound, which emits no slot-cleared event.
- No change needed for: the dismount / transform-ended sentences on reset and clearbar replies, the `Still transforming`
  reply to `.beelz phase` (both show in chat as-is), or the phase display (already driven by `transform-phase-shift`).
- README: Beelzebub row -> v0.137.x (API 35).

## 0.68.0 — Beelzebub 0.137 support (API 33): one-step bar reset

Catches up to Beelzebub 0.137.0-0.137.2 (`ApiVersion 33`, `docs/BCH_INTEGRATION_HANDOFF.md`). No `[BEELZ:*]` line
changed shape. New gates in `BeelzState`: `SupportsLayeredBarReset` (api>=33) and `GrantsApplyImmediately`
(plugin>=0.137.1). Older servers keep the old buttons and behavior.

- **Loadout tab:** on api>=33 "Unstick bar" (revert -> resetbar) becomes one confirm-gated **Reset bar**
  (`.beelz resetbar CONFIRM`). The server's layered reset takes spells off the bar at once with no weapon swap and
  keeps spellbook (J) spells.
- **Admin: Players -> Recovery** (api>=33): new ladder **Diagnose bar** (`admin bar <player>`, read-only chat
  readout) -> **Reset player's bar** (`reset-loadouts CONFIRM`) -> **Purge bar** (`purge CONFIRM`, also clears hotkeys) ->
  **Respawn**. The legacy Rebuild slots / Clear slot mods buttons and the Rebuild bar duplicate (now an alias of
  `admin bar`) are hidden. New builder `BuildBeelzAdminRecoveryLayered`.
- **Grants show at once (0.137.1):** the auto `.beelz refresh` after a grant (`BeelzAutoRefreshBar`) is skipped on
  plugin >= 0.137.1; the `api slots` re-read still runs.
- **`transform-ended` now also re-reads `api slots`** (0.137.2: bar edits made while transformed apply on exit).
- Config view: description for the new `Forms_AutoFillFromCaptures` key.
- Guide: `resetbar CONFIRM` and the "grants show right away" note in the player reference; admin recovery ladder
  rewritten (`admin bar`, reset-loadouts, purge, respawn; legacy commands noted).
- README: Beelzebub row -> v0.137.2 (API 33).
- Not in this release: the handoff's optional "No Name" action-bar hover card / ability library (separate feature).

## 0.67.4 — Nyarlathotep beta banner

- Nyarlathotep tabs now show the same beta feedback banner and **Give feedback** Discord button as Beelzebub, Uriel
  and Faust (`BetaFeedbackGroups`).

## 0.67.3 — Chat window spacing

- Chat window: message text no longer touches the tab buttons. Tab row bottom padding 2 -> 6 px (row 24 -> 30),
  log top margin 4 -> 6 px.

## 0.67.2 — Chat & Diagnostics tab; your command replies always show

Replies to Nyarlathotep commands looked "suppressed". Two causes: VCF error / "command not found" lines were hidden
whenever `SuppressCommandFrameworkErrors` was on (default), including errors for commands the player typed (`.nyar help`,
typos, admin-only commands); and every `[NYAR:*]` / `[FAUST:*]` / `[BEELZ:*]` / `[URIEL:*]` line was always removed,
even replies to `.nyar api …` typed by hand.

- **Attribution** (`Services/OutboundLedger.cs`): Raphael now records whether the latest command came from its own
  background queries (`EnqueueMessageSilent`) or from the player (buttons, Raphael chat, and the game's chat box via a
  new fail-soft hook on `ChatHelper.SendChatMessageOfType`, `Patches/ChatSendPatch.cs`). Cleared on logout.
- **VCF errors:** hidden only when a background command went out in the last 5 s and the player sent nothing in that
  time. If the game-chat hook can't install, hiding is off for the session.
- **Protocol lines:** still parsed; new per-mod "Show protocol lines in chat" settings (`BeelzShowProtocolInChat`,
  `UrielShowProtocolInChat`, `FaustShowProtocolInChat`, `NyarShowProtocolInChat`). Replies to a command the player
  sent to that mod pass through for 10 s even when off; push lines (`[NYAR:ev]`, `[BEELZ:event]`) never do.
- **New tab: Settings and Help → Chat & Diagnostics.** Presets (Quiet / Normal / Show everything, with a "Current"
  label), Raphael & Bloodcraft rows (background errors, background replies, chat copies of parsed replies (first UI
  for `ClearServerMessages`), familiar chatter, Diagnostic mode), and per-mod rows (show protocol lines, wire trace).
  The Settings tab's "Chat noise" card now links here. Per-mod Settings tabs keep their toggles; both stay in sync.
- Nyarlathotep Guide: "Not seeing a reply?" section.

## 0.67.1 — Main panel opens instantly

Opening Raphael could freeze the game for 2-10 s: every tab (~60 pages, ~1,300 tooltips) was built in one
frame, the 6080x6080 `worldmap.png` was decoded during that build, and each mod handshake rebuilt the whole panel.

- **Lazy pages:** the panel opens with its tab strip and a "Loading <tab>..." placeholder; the clicked tab builds
  next frame, other tabs build one per frame only while the panel is open (pauses after a slow page). Log shows
  shell ms, per-page ms and a prewarm summary. A tab that throws shows an error page and retries on next click.
- **Per-mod invalidation:** a mod connecting, disconnecting or giving up now rebuilds only that mod's tabs
  (relog/server switch invalidates all mod groups) instead of the whole panel.
- **"(connecting...)"** group headers while a mod's handshake is still running; "(unavailable)" only after it gives up.
- **World map:** decoded only when a map is drawn, one frame after "Loading map image..." shows. A <=2048px copy
  is cached to `worldmap.cache.png` (keyed on source size + time); falls back to the full image on any error.
- Per-page teardown for Faust pickers and box-name dropdowns; footer/last-response tooltips keep priority over
  late-built pages; late pages get the panel colour pass; prestige one-shot ticker is now removed on reset.


## 0.67.0 — Nyarlathotep integration (contract api 1/2)

New **Nyarlathotep** tab group for the admin-run world-event server mod (waves, empowered NPCs, boss adds,
defended zones, sieges, stats), built to Nyarlathotep's `docs/RAPHAEL_INTEGRATION_CONTRACT.md` (api 1/2).
The group stays hidden (Auto) until a server answers `.nyar api version` with `ready=1`. Checked in game against
Nyarlathotep 0.5.2 (api 4).

- **Services/Nyar/** (new): `NyarWireParser`, `NyarClient`, `NyarState`, `NyarProtocolService`, `NyarDiag`.
  Handshake mirrors Faust/Uriel (4s settle, 12 probes, give-up). On detection with api ≥2: `api sub on`
  (re-sent every handshake — server subscriptions are memory-only), then status + me, and for admins events +
  zones. `events`/`zones` auto-page; `[NYAR:ev]` push lines trigger debounced re-reads; a 1s countdown tick
  runs while events or the kill switch are live. `[NYAR:err]` codes map to friendly messages.
- **Wiring:** `[NYAR:` branch in `ClientChatPatch` (routed + destroyed), `Reset()` on logout, `Tick`
  registered in `Plugin`, `NyarAvailability` (Auto/On/Off) + `NyarDiagnostics` settings, rail diagnostic
  panel (Re-check / Force-enable), Connection-tab card with Re-detect.
- **Player tabs:** Live Events (per-event card, wave/next-wave/time-left countdowns), Leaderboards
  (6 stats × today/week/all, paging, own row highlighted, Share my line), My Stats (three windows +
  Hide me / Show me), Settings (diagnostics + read-only server switches).
- **Admin tabs** (inline admin gate): Events (definition list with Start/Stop/Enable/Disable/Edit, Reload
  from disk, `event set` field editor, test spawn), Zones (add at your position, list, two-click remove),
  Announce (text + digest; five announcement switches read-only), Kill Switch (purge → confirm with
  cooldown readout; stats reset → confirm).
- **Guide:** "Nyarlathotep Guide" in Settings and Help.
- Pillar/stats switches show an in-tab note rather than hiding tabs. Zone map pins (contract §6) not done yet.

Also in 0.67.0:

- **Beta feedback banner** on every Beelzebub, Uriel and Faust tab: one slim line ("<mod> is in beta. Found a bug
  or have an idea?…") plus a **Give feedback** button that opens The Shadow Realm Discord (permanent invite
  `discord.gg/c4YU2HbHFA`). Added via `MaybeAddBetaFeedbackBanner` beside the Eclipse stand-down banner hook.

## 0.66.0 — Beelzebub v0.132–v0.135 support (ApiVersion 29–32)

Every item below only turns on when the connected Beelzebub server reports that API version. On older servers
Raphael behaves as it did before.

- **Incompatibility locks (API 32 / Beelz v0.135).**
  - Raphael reads the server's lock groups (`api locks`) on connect, and again after `ability-locked` events and
    config changes.
  - In the loadout and hotkey lists, an ability gets a red **[Locked]** badge when its lock group is already full
    on your live bar (universal slots + current weapon + current form + hotkeys). Its tooltip explains why. When
    the server itself reports an ability as locked, that report takes precedence over Raphael's estimate.
  - The action-bar overlay warns when the server will refuse a force-cast of a locked ability.
  - New admin card **Incompatibility locks**: lists the groups, with Add / extend, Set max, Remove member,
    Delete group (two-click confirm) and Check player.
- **Runtime cooldowns + new tuning knobs (API 31 / Beelz v0.134).**
  - Displayed cooldowns (tooltips, copy text, the action-bar ring) now follow the server's order: the override if
    set, otherwise the shipped cooldown × scale, then the server's `Grant_MinimumCooldownSeconds` minimum. The
    minimum only applies once `api config` has been read.
  - Abilities that use charges show "Uses charges" instead of a cooldown.
  - The admin ability table and shaping dropdown gain **Max stacks**, **Projectile count**, **Knockback ×**,
    **Lifetime** and **Cast time ×**. The shaping dropdown also gains leap height and power window. The table
    scan doesn't report the new values, so they show blank there; **Load current settings** shows the live values.
- **Per-hit damage (API 30 / Beelz v0.133).**
  - Player tooltips show the damage multiplier a cast would get right now, but only when the server's damage
    mode is Scale.
  - The admin **Load current settings** readout shows damage mode, damage factor and summon power, and flags
    unreliable hit attribution in orange.
- **Form/weapon blocks (API 29 / Beelz v0.132).** Tooltips now read "Usable in: X except Y" and "Not with: …".
  On older servers they use the catalog's allow/`!`-block tokens instead of `api info`'s `forms=` (which could
  show a blocked form as allowed).
- **Admin chat commands:**
  - **Inspect** button (`admin ability-inspect`) on the shaping card.
  - New **Reseed ability rules** card: Preview / Merge / Replace. Merge and Replace need a two-click confirm.
  - Short guide section covering the lock, reseed and inspect commands.

## 0.65.6 — Long-session slowdown fixed, plus a "Refresh Raphael" button

- **Fixed: the game got slower the longer you played, until you logged out and back in.** Two causes:
  - **Chat window.** Every incoming message rebuilt the whole retained history (up to 2,000 lines) and redrew
    it immediately — even with the chat window closed — so each message cost a little more as the session went
    on. The tabbed and view-only chat windows now format only the lines that can actually be shown, merge bursts
    of messages into one redraw (at most every 0.15 s), and skip redrawing while closed. What you see is
    unchanged. A redraw that fails is retried once, then waits for the next message instead of retrying forever.
  - **Tooltips.** Overlays that rebuild their buttons on every server update (Familiar Quick Spawn, the
    Beelzebub action bar and transforms) left a stale tooltip entry behind each time. These were only cleaned up
    while the main panel was open, so the list grew all session. They're now swept every 10 seconds.
- **New: "Refresh Raphael" button** — a small **Refresh** at the bottom-right of the main panel (always visible;
  flashes "Done"), plus a fuller version on the Connection tab that shows what it cleared. Clears chat history
  and stale tooltips and starts a Unity unused-asset cleanup — the same cleanup a relog does, without leaving
  the world. Settings, open windows, server connections and whisper targets are kept.
- **Fixed: chat could still blank out from a flood of messages containing `<`.** The "blank chat" guard from
  0.65.1 counted everything after a stray `<` (e.g. `<3`) as invisible, so a flood of such messages could push
  the chat past TextMeshPro's size limit again. Only known formatting tags are skipped now; everything else
  counts.
- **Memory leaks fixed** (not tied to session length, but real):
  - Rebuilding the main panel (settings/scale changes) left the old panel in memory, held by the All Familiars
    tab, the Faust player/castle/boss pickers, and the box-name form dropdowns. All are now detached on rebuild.
  - Rebuilding the Familiar Browser overlay left the old one subscribed to V Blood summon status.
  - Faust world map: each **Reload image** leaked the old map texture, and each **Capture map from game** leaked
    a full-size copy (~64 MB for a 4K map). Both are now freed.
  - A familiar-system check scheduled shortly after login could get stuck re-sending `.fam boxes` every frame if
    you relogged within ~8 seconds of connecting.
- **Docs:** fresh v0.65.6 screenshots on the store page (grouped into collapsible sections), and the compatibility table now lists Faust 0.16.4 (API 19) and Uriel v0.20.0.

## 0.65.5 — Chat length cap lowered to stop breaking non-Raphael players, plus Uriel v0.20.0 support

- **Fixed: a very long chat message could flood chat and freeze the chat box for players who don't run
  Raphael.** Testers found that a message of roughly 480 characters or more made the server re-broadcast the
  line over and over — one send turning into a screenful of repeats — and, worse, non-Raphael players nearby
  couldn't open their chat window at all until they teleported to reload it. Messages of 479 characters and
  under were always clean. Raphael now caps chat input at **470 characters** (down from 500), which keeps you
  under the threshold with margin so nothing you type can break the chat of vampires around you. Normal-length
  messages are completely unaffected. *(Thanks to Howl for the careful character-by-character testing that
  pinned the threshold.)*

- **New: support for Uriel v0.20.0's flush-to-ground placement.** Uriel's latest update lets you correct
  objects that float above or sink into the ground. Raphael now surfaces it:
  - **Raise nearest / Lower nearest** buttons on the Object Spawning tab nudge the object you placed nearest
    you up or down by 0.25m per click (`.uriel raise` / `.uriel lower`) — no more objects hovering or buried.
  - Admins get a **Height offset** field under Uriel → Admin → Spawn conditions to set a permanent per-object
    (or global-default) height correction in metres (`.uriel objcfg <obj> yoffset <metres>`). Unlike the other
    spawn conditions, this is a placement fix and applies to everyone, admins included.
  - The in-app Uriel guide pages now document all three new commands.

  (Uriel v0.20.0 also blocks a batch of problem objects — warps, portals, mine gates, an item-farming pickup
  exploit — and removes empty world containers like chests, shelves, cabinets and drawers from spawning to cut
  server lag. Those are enforced server-side by Uriel itself, so they just quietly stop appearing in the
  catalog; no Raphael setting is involved.)

## 0.65.4 — Chat fixes: typed text re-centers after long pastes, and long messages stop flooding

- **Fixed: the tabbed chat input stopped centering your text once you'd pasted a long message.** The
  centered-while-single-line behavior added in 0.65.3 decided single-vs-multi-line from the input's pixel
  height. After the field had grown to several lines (e.g. pasting a long message), TextMeshPro reports the
  *same* one-line text as slightly taller than it did before, so the field stayed just over the threshold and
  never dropped back to centered. Raphael now uses the actual wrapped line count instead of a height guess, so
  it re-centers reliably as soon as your message is back to a single line. *(Thanks to Kruczarz.)*

- **Fixed: a very long message could show up duplicated/spammed in chat, from a single send.** When a Raphael
  player sent a message that hit the character cap, receivers running Raphael could see the same message
  repeated many times over — one send, a screenful of copies. The tabbed chat window now recognizes an
  identical long line already in recent history as a duplicate of that one message (nobody retypes an 80+
  character message word-for-word) and drops the extra copies, regardless of what produced them. Short
  messages are untouched, so genuinely repeating "lol" twice still shows both. *(Thanks to Howl and Strie for
  the report.)*

## 0.65.3 — Chat polish: no more random italics, centered typing, optional idle auto-hide

- **Fixed: a chat tab could randomly switch to italics and stay that way.** All visible chat lines share a
  single text component, so if one message left a rich-text tag *open* — e.g. a system/server message
  containing `<i>` with no matching `</i>` — that italic (or bold/color/size) carried forward into every
  line after it, with no way to turn it off short of a relog. Raphael now closes any tag a message leaves
  dangling, so each line is self-contained and formatting can't leak between messages. *(Thanks to Howl for
  spotting it — two back-to-back system messages was the trigger.)*

- **Changed: text you type in the tabbed chat is now vertically centered.** The message input centered the
  text on a single line (like a messaging app) instead of pinning it to the top; it switches to top-aligned
  only once your message wraps onto multiple lines, so long messages still show every line. *(Thanks to
  Kruczarz.)*

- **New: optional auto-hide for the tabbed chat window (off by default).** Turn it on under Game UI → chat
  and the window fades out after a configurable idle delay (3–120s, default 12) and fades back in on
  activity — like the game's native chat. Which activity wakes it is configurable so it won't blink on a
  busy server: a new **player** message (Global/Local/Clan/Whisper) wakes it by default, while **System**
  lines (server broadcasts and Raphael's own regular Bloodcraft pings) do **not** unless you opt in. Hovering
  the window or opening chat always wakes it. It's a visual fade only — it never closes the window or changes
  its saved visibility. *(Requested by Kruczarz.)*

## 0.65.2 — Fixed the last localized familiars that still showed wrong HP/quests

- **Fixed: some familiars *still* showed HP as 0 and garbled quests on non-English clients — but only
  certain familiars, and only some of the time.** 0.65.1 fixed the general non-English case, but a follow-up
  report (Kruczarz, Polish client) found ~15 specific familiars that stayed broken while others were fine.
  Root cause: the 0.65.1 re-alignment locks onto the two quest "is this a V-Blood?" flags as landmarks, but
  it only recognized the capitalized forms `True`/`False`. Bloodcraft actually sends a **lowercase `false`
  for any non-kill quest** (gather, craft, etc.) and as the default. So if your current quest happened to be
  a gather-type quest, the landmark went unrecognized and the re-alignment gave up — falling back to the old,
  wrong reading. Because the re-alignment only runs when the familiar's localized name contains a comma, this
  only hit familiars whose name has a comma in your language (e.g. Polish "Domina, Tancerka Ostrzy"), which is
  why it looked familiar-specific and why swapping familiars or waiting for the quest to rotate "fixed" it.
  Raphael now recognizes the flag in either casing, so these familiars display correct HP/PP/SP and quests
  regardless of your active quest type. *(Thanks to Kruczarz for the detailed Polish-client repro, and to
  Luxanna for confirming the English/other-client case was clean.)*

## 0.65.1 — Fixed quests and familiar stats on non-English clients

- **Fixed: daily/weekly quests showed only numbers, and the summoned familiar's HP/PP/SP read 0 — on
  localized (non-English) clients.** These looked like two separate bugs but had a single root cause. The
  live data Bloodcraft streams to Raphael is a comma-separated list of fields, and Bloodcraft drops the
  **familiar name** and **quest target name** into that list without escaping them. In your own game
  language some of those names *contain a comma* — Polish, for example, writes Alpha the White Wolf as
  "Alfa, Biały Wilk" and a gather target as "Klejnot krwi, poziom 1". Each stray comma shoved every
  following field one slot over, so the familiar's stat numbers landed where the name should be (reading as
  **0**) and the quest target landed on a numeric slot (reading as **bare numbers**). English names have no
  commas, which is why English players never saw it. Raphael now detects the shift and re-aligns the fields
  before reading them, so quests show their real target and familiars show real HP/PP/SP regardless of your
  client language. Clients that were already fine are untouched. *(Thanks to Kruczarz for the Polish-client
  report and screenshots, and to Chronost for confirming English was unaffected.)*

  The re-alignment is **language-agnostic** — it works off the data's field structure, not any
  per-language name list — so it covers every localized client whose names contain a comma (Polish, Latin
  American Spanish, German, Russian, Turkish, …), not just Polish. Languages that use a full-width comma
  (e.g. Chinese/Japanese) never triggered the bug and are likewise unaffected.

- **Fixed: 9 more V-Bloods now appear on the V-Blood tab on non-English clients.** The language-proof
  V-Blood name table added in 0.65.0 covered 55 regular V-Bloods; an audit found 9 more (General Elena the
  Hollow, Willfred the Village Elder, Sir Erwin the Gallant Cavalier, Gaius the Cursed Champion, Stavros the
  Carver, Dantos the Forgebinder, Lucile the Venom Alchemist, Jakira the Shadow Huntress, Megara the Serpent
  Queen) that classified fine in English but dropped from the tab in other languages. All 64 entries were
  cross-verified through the game's name→prefab→localization-key chain.

- **Fixed (harder): a chat channel could still go blank after heavy use.** 0.65.0 already stopped feeding
  the whole 2000-line history to the chat text box, but it capped by *line count* (300). On a busy server
  with long proximity lines, 300 lines still pushed TextMeshPro past its ~16,000-glyph mesh ceiling, so a
  flooded tab (the **Local** tab first) went blank again until you swapped tabs. The cap is now by **visible
  glyph budget** instead of line count, so long lines can't sneak the mesh over the limit. Full history is
  still retained for scrollback and Copy. *(Thanks to Howl.)*

- **New: a "Refresh" button on the tabbed chat window.** Forces the current tab to redraw from the retained
  history on demand — the same effect as swapping tabs and back, without the round-trip — for the rare case a
  channel ever shows stale or empty. *(Requested by Howl.)*

- **New (experimental): "Stronger hide if the game chat still flickers" chat option.** 0.65.0 zeroed the
  native chat's message-list alpha every frame, which reduced but didn't fully stop the native chat flickering
  through on some PCs — the game's chat has more than one visible layer, and the one that flickers isn't the
  message list. This opt-in toggle (Game UI settings, **off by default**) hides the **entire** native chat
  window every frame instead. It's alpha-only, so it keeps the same input/freeze-safety as the normal hide.
  Off by default because the flicker is frame-timing-dependent and most players never see it; enable it only if
  you do. *(Thanks to Howl — this one needs in-game confirmation, so it's opt-in for now.)*

## 0.65.0 — In-built game guide + extended-chat controls, plus a batch of user-reported bug fixes

- **New "V Rising Basics" guide tab (Settings and Help).** An in-built, plain-language guide to the *base*
  game — vampire survival & blood, Gear Level as your "level", combat/spells, V Bloods, crafting, castles &
  territory, and world hazards — followed by a **"How the mods change the game"** section. Each base section
  has a collapsible *"With the mods"* note that points to the relevant per-mod Help tab, so Raphael reads as a
  one-stop reference for new players without duplicating the deep per-mod command references. Sits beside the
  existing external-links **Game Guide** tab.
- **Extended chat retention is now controllable (performance).** The standalone tabbed/secondary chat windows
  keep a durable history because the server drops old lines quickly — but on servers whose mods flood **System**
  chat, retaining everything can add up. New **Extended chat retention** card (Game UI settings) adds a master
  on/off plus **per-channel** toggles (Global / Local / Clan / System / Whispers / Other). Turn off a channel to
  stop retaining it (it still shows transiently in the game's native chat). **All default ON**, so existing
  behavior is unchanged. The capture gate runs *after* the whisper-picker sender-ID pairing, so disabling a
  channel never desyncs the whisper picker. *(Note: "notes to self" are part of the Whispers channel.)*
- **Fixed: Exo prestige guide text was misleading about levels.** The description said your "level stays at
  max", which several players read as their *character* level — but each Exo prestige resets your character
  level to 0 (you re-level to max; only the Exo *prestige tier* carries over). Reworded in both the Prestige
  tab's progression card and the Bloodcraft Help tab to make this explicit. Also **removed the server-specific
  500× Primal Stygian Shard reward from the descriptions** (most servers change it, which was confusing) —
  the text now says rewards vary per server, check your admin. The "Default settings — Exo" reference still
  lists the Bloodcraft default, but framed as a default most servers override. *(Thanks to Kyoma, Chronost,
  Count Snackula, and Xavarie for the report + corrections.)*
- **Fixed: familiar Quick Spawn overlay buttons could lose their text.** With a larger overlay font scale the
  text outgrew the fixed-height slot buttons and was dropped entirely. The button height now scales with the
  overlay font, and the label auto-shrinks (down to a readable floor) to fit — so long familiar names stay
  visible instead of vanishing.
- **Fixed: chat could stop showing new messages after prolonged use.** On busy servers the chat (the **All**
  tab first) would eventually go blank — messages were still there (Copy chat still returned them) but the box
  rendered empty until you relogged. A single text element can only draw so many characters before its mesh
  overflows and renders nothing; we were feeding it the entire history. The rendered window is now capped to the
  newest lines while the full history is kept for scrollback and Copy. *(Thanks to Howl and Chronost.)*
- **Fixed: the game's native chat flickered behind the Raphael chat.** Even with native chat hidden, it could
  flicker through — worst while you had the chat bar open to type — because the game's own fade kept fighting our
  hide. We now re-assert the hide every frame, so it stays down. *(Frame-timing-dependent, which is why some PCs
  saw it and others didn't. Thanks to Howl.)*
- **Fixed: casting could break after opening then closing the UI with the on-screen button.** If you opened and
  closed the Raphael UI with its floating button and didn't left-click *attack* in between, spells — and power/
  emote-wheel selections — silently did nothing until you swapped weapon and swung. A guard meant to swallow a
  stuck auto-attack after closing a panel was over-reaching and blocking *all* casting; it now cancels only the
  stray attack and leaves everything else working. *(Thanks to Mardina.)*
- **Fixed: regular V-Bloods were missing from the V-Blood familiar tab on non-English clients.** The tab only
  showed Primal V-Bloods because it recognized bosses by their **English** names; on a localized client (e.g.
  Polish) the regular V-Bloods came through in that language and weren't matched. It now resolves V-Blood names
  in your **own game language**, so regular V-Bloods show up regardless of client language. Also fixed one
  Primal ("Primal Polora") that was mislabeled and dropped even in English. *(Thanks to Kruczarz.)*
- **Fixed: an Eclipse handshake could spam public chat when a Raphael user relogged.** On servers **without**
  Bloodcraft, Raphael's background registration message wasn't being consumed and got broadcast as normal
  proximity chat — so nearby players (even those without Raphael) saw a raw `[ECLIPSE]…` line and a floating
  text over the relogging player's head, repeated a few times. The handshake is now sent so it can never surface
  to bystanders, it's skipped entirely when you've set Bloodcraft to *Off*, and any stray copy is filtered from
  Raphael's chat. *(Thanks to Howl.)*

## 0.64.0 — Live “Players now” map view + redesigned heat palettes

- **See where online players are right now, on the map.** Faust → Player Positions → the map area now has a
  **View** toggle: **Heat map** (aggregated history, as before) ↔ **Players now (live)** — a snapshot that plots
  every online player as a dot on the same calibrated map, with their **name** beside each dot (toggle labels
  off if it gets crowded; names are always on hover, along with coords / region / territory). Hit **Refresh
  positions** to update it. Uses the same map underlay + coordinate calibration as the heat map.
- **Redesigned heat-map color palettes.** The map-friendly schemes had too little variation between low and high
  traffic. Replaced them with three proper, perceptually-graded ramps that sweep both hue *and* brightness (and
  ramp opacity from translucent “less” to opaque “more”), so density reads clearly on top of the world map:
  **Magma** (purple → magenta → orange → cream), **Ice→Fire** (blue → cyan → white → amber → red), and
  **Viridis** (purple → teal → green → yellow). Cycle them with **Colors** on Player Positions (or Settings →
  Heat map).

## 0.63.1 — Dropdown fixes + Faust-tab cleanup (follow-up to 0.63.0)

Testing-feedback fixes for the 0.63.0 dropdowns and some Faust-tab tidying.

- **Online-player & castle dropdowns now actually populate for admins.** The auto-load was wrongly gated on the
  feature's `admin` token — but admins *can* use admin-gated features, so the list stayed empty for the very
  people who could see it. Removed that gate, and every picker now has an inline **Load** button to (re)pull the
  list on demand (online players via `.faust api positions`, territories via `.faust api castles`).
- **Boss tracker no longer leaves a boss stuck under “Look up one boss”.** The tracker's ~5s auto-refresh was
  routing through the single-boss **lookup result** card, so a tracked boss showed there and never cleared (even
  after you untracked it). Auto-refresh now updates only the overlay's cache; the result card shows only your own
  manual lookups.
- **World Map: fixed text running off the container.** Hint lines (e.g. the “Pick a Category…” help) now wrap
  instead of overflowing. *(All in-game hint text benefits — the helper that builds them had word-wrap off.)*
- **World Map: tidier filters + collapsible setup.** The everyday **Type / Category / Type** dropdowns + **Scan**
  sit together; the GUID-level filters (prefab id / blood type / unit category / min blood quality) moved into a
  collapsed **Advanced filters** section. The **Map underlay & coordinate overlay** setup is now collapsed by
  default, so the **Assets** table sits right under the map (expand it only when aligning the map image).
- **Player Positions: cleanup.** **Show players on map** (admin · experimental) moved to the bottom and collapsed.
  Removed the **Scale** heat-map button — it did nothing once a map underlay was active (the underlay overrides
  it); the setting still lives under Faust → Settings → Heat map.

## 0.63.0 — Dropdowns everywhere: pick players, bosses & castles instead of typing

Testing-feedback pass focused on the Faust tabs — fewer things to type by hand, and a clearer boss tracker.

- **Active-player dropdowns.** Anywhere you enter a player name / SteamID — **Player Info**, the **heat-map**
  per-player field, and **Admin → Player access** — there's now a dropdown of currently-online players (from
  `.faust api positions`). Pick one and it fills the field + runs the lookup; the free-text box stays for anyone
  not online. The list auto-loads when you're allowed to see it, with a **Reload online list** button otherwise.
- **Boss lookup is a dropdown.** “Look up one boss” now has a **Pick a boss** dropdown built from the live board
  merged with every known V Blood, so it's usable even before the (admin-gated) board loads. Selecting one looks
  it up by GUID when it's on the board (most reliable), else by name. Typing still works for unlisted GUIDs.
- **Boss tracker overlay now shows coordinates** for live bosses (`(x, z)`) next to HP and region — so you can
  actually find them.
- **Castle index is a dropdown.** **Castle Info** and **Castle Resources** now have a **Castle (index)** dropdown
  listing every territory as `#index · region · (x, z) · owner` (from `.faust api castles`), so you never have to
  know an index by heart. Typing an index still works.
- **World Map filters are dropdowns and pre-filter the scan.** Category / Type / kind (Units/Nodes) are now proper
  dropdowns. Picking a **Category** narrows the scan **server-side** (NPC factions → units, resource families →
  nodes) so it pulls less data, then the client narrows the table/map to your exact Category / Type. The catalog
  file gained an optional 4th field (`Category | Type | keywords | unittype=N|restier=N`) for finer server-side
  filtering. *(A full per-unit checkbox picker and a prefab type-ahead were considered but deferred — Faust
  doesn't expose a player-facing prefab catalog over the wire.)*
- **Heat map: brighter, more visible dots + opacity control.** The map-friendly color ramps (Magenta / Cyan /
  White-hot) are now bright at both ends with a higher opacity floor, so activity cells read clearly on the map.
  Added a **Map image opacity** slider right on the Player Positions heat map (shares the World Map setting) so
  you can fade the map art down and let the dots pop.
- **Boss tracker: removed the redundant management card.** Tracking is now managed in one place — the board's
  **+ Track / ★ Tracked** buttons. The Boss Status tab keeps just the overlay **show/hide** + **auto-refresh**
  switches (no more separate name-list that duplicated the board).

## 0.62.0 — Heat map: time windows (Faust 0.16.4 / API 19)

- **The player-position heat map can now be filtered by time.** A new **When** toggle on Faust → Player Positions
  cycles **All-time → Today → This week → This month**, re-querying the current (server or player) heat map for
  that window. Needs **Faust 0.16.4+** (API 19); on older servers the toggle is hidden and the all-time map works
  as before.
- Added **`heatmapretentiondays`** (the server's per-day history cap, default 30) to the live config editor's
  global settings — windows longer than retention just sum what's kept.

## 0.61.0 — World Map catalog filter, map-friendly heat colors, lighter boss tracker

- **World Map filter is now catalog-driven and works before you scan.** The Category / Type pickers come from a
  built-in, **editable** catalog (`config/Raphael/worldscan_categories.txt`, format `Category | Type | keywords`),
  so you can pre-select what you're after instead of having to load everything first. NPCs are grouped into clean
  factions (Bandit, Undead, Gloomrot, Wildlife…) instead of raw prefab names, and resources into Ore / Wood /
  Stone / Plants / Gems / Tech with proper types (Copper, Pine, Plant Fiber…). Add your own lines to the file to
  extend it.
- **Heat map: new color schemes that read on top of the map.** Added **Magenta**, **Cyan**, and **White-hot**
  ramps designed to contrast with the world-map image, and heat-cell opacity now scales with intensity so the map
  shows through low-traffic areas. Cycle them on Faust → Player Positions (or Settings → Heat map).
- **Boss tracker auto-refresh now only refreshes your tracked bosses.** Instead of re-pulling the entire boss
  board every ~5s, it does one quick per-boss lookup (`.faust api boss …`) for each boss you're tracking — far
  lighter on the server. (Faust already has the per-boss query; Raphael now uses it.)
- **Map default coordinates** updated to the latest in-game-aligned calibration.

## 0.60.1 — World Map filter fixes; admin tools moved to Admin: Control

- **Fixed the Category / Type filter actually grouping things usefully.** The taxonomy was rebuilt against real
  V Rising resource-node names — plant-fiber bushes (which have “BushyTree” in their name) no longer read as Wood,
  and variants (`_01/_02/_Stage1/_Pickup`) now collapse into clean types like **Plant Fiber, Copper, Pine, Spruce,
  Rock, Emery, Gloomrot Tech**. So Category → Type now meaningfully narrows the table + map (previously most nodes
  fell into one “Other” bucket, so cycling looked like it did nothing).
- **Moved the world-scan admin tools** (the prefab **Whitelist** and the **Prefab lookup / diagnostics**) off the
  World Map tab and onto **Faust → Admin: Control**, leaving the World Map tab player-focused (scan / filter / map).
- **“Truncated” explained:** the notice now says the **server** hit its result cap and tells you to raise
  **`worldscanmaxresults`** (Admin: Control → global settings; `0` = unlimited). On a busy map the server may still
  cap at its configured `MaxResults` (e.g. 2000) until you raise it.
- **Boss board (no Raphael change):** re-verified against the raw wire — the “Not spawned” bosses arrive with
  **no coordinates at all** (`status=down`, no `x`/`z`), and Raphael's parser reads exactly what's sent and renders
  the whole board. If Faust's `bossdiag` shows those bosses *do* have positions server-side, then Faust's `bosses`
  endpoint isn't putting them on the wire — that's the server-side gap. Re-filed with the wire evidence.

## 0.60.0 — World Map: search & filter by category / type

- **New: cascading Category → Type filter on the World Map.** After a scan, narrow the results by a friendly
  **Category** (NPC factions like “NPC · Bandit”, or resource families like “Resource · Ore / Stone / Wood /
  Plants / Gems …”) and then by a specific **Type** within it (e.g. Ore → Copper). Both lists are built
  automatically from whatever the scan returned — no PrefabGUIDs to type — and filtering is **instant and
  client-side** (the table and the map both update, no re-query). The status line shows how many of the total
  are matched.
- A hint on the filter card explains that resource **nodes only appear if they're whitelisted on the server**
  (use **Seed defaults** / add PrefabGUIDs) and to scan with Type = All or Nodes.
- **Boss board (no Raphael change):** re-verified with the server now on **Faust 0.16.3** — the board is still
  ~half “Not spawned” (the same roaming bosses), so the 0.16.3 location “combine” still isn't resolving them
  server-side. Raphael fetches all pages and renders the whole board correctly; filed back to Faust with the
  evidence (run `.faust admin bossdiag` and check the `Map tokens: N resolved` line — if 0, the token sources
  aren't populated on this server).

## 0.59.2 — Calibrated map defaults; Faust 0.16.3 note

- **The world-map underlay now ships pre-calibrated.** The default coordinate bounds were updated to a set aligned
  in-game against the captured Vardoran map, so a fresh install lands close to correct out of the box (fine-tune
  with the calibration tool as needed). These bounds are shared by **both** the World Map and the player-position
  **Heat Map**.
- **Faust 0.16.3 — no Raphael change.** 0.16.3's fixes are server-side: roaming-boss locations (the status +
  map-token "combine") and a world-scan filter fix so a narrow filter no longer fills up on common entities before
  reaching rare matches. The wire is unchanged (ApiVersion 18); Raphael already handles both. Updated the Boss
  Status note to reference 0.16.3.

## 0.59.1 — Faust 0.16.2: bigger world scans; roaming-boss note

- **World Map: bigger scans.** Faust 0.16.1 raised its result cap (2000 → 10000, or `0` = unlimited) so a busy
  map's scan isn't cut short. Raphael now exposes **`worldscanmaxresults`** in the live config editor's global
  settings, and added a **client-side safety cap (5000 rows)** so an unlimited, unfiltered scan can't page
  hundreds of times or render tens of thousands of dots — when it's hit, you get the usual "narrow the filter"
  notice. (No wire change.)
- **Boss board:** Faust 0.16.2 changes how roaming-boss locations are found (it now combines the boss's status
  with the game's own map-token tracking). This needs **no Raphael change** — the wire is unchanged; Raphael just
  receives coordinates for more bosses once the **server** runs 0.16.2. Refreshed the Boss Status note to match.

## 0.59.0 — World map fills the panel + live coordinate-overlay calibration

- **The map now fills the panel width** (square, sized to the available width) instead of sitting as a small
  centered square. The map image and the dot/heat/grid overlays all reflow to the new size.
- **The map image and the coordinate overlay are now decoupled.** The image fills the board; the **dots** are
  positioned by the calibration bounds, which you adjust independently (X and Z) — so aligning the coordinates no
  longer distorts the map.
- **New live calibration tool** (Faust → World Map → Map underlay): after a scan, **Move** the dot overlay
  ◄ ► ▲ ▼ and **Stretch** it Wider / Narrower / Taller / Shorter (with a cycleable step size) until the
  coordinates line up with the map, then click **Log calibration** to print the values to the BepInEx console so
  they can be set as the baseline. Nudges the coordinate overlay only, never the map image.
- **Boss board (no Raphael change):** re-verified after the server updated to **Faust 0.16.1** — Raphael fetches
  all pages and accumulates the full list, but the board is unchanged (same roaming bosses still "Not spawned").
  The 0.16.1 fix didn't catch them server-side; filed back to Faust with the evidence.

## 0.58.6 — Docs: README + store-page readability overhaul

- **Rewrote the README and the Thunderstore changelog for readability** (acting on tester/admin feedback that the
  docs read as dense "AI slop"). The README dropped from ~456 lines to ~170: a tight intro + "What you get", a
  short install section, and everything secondary (Eclipse/beta/controller heads-ups, BloodCraftHub migration,
  full feature list, compatibility table, known issues, screenshots, dev notes) moved into **collapsible
  sections**. Stale version references were corrected to the current line.
- The Thunderstore changelog is now a **concise milestone history** (newest first, deep history collapsed).
- **Packaging fix:** `package-release.ps1` now bundles `CHANGELOG.thunderstore.md` (the concise one) to the store
  page instead of the full per-patch `CHANGELOG.md`; the full changelog remains the canonical archive on GitHub.
- No functional/code changes.

## 0.58.5 — Boss board note: reflect Faust 0.16.1's roaming-boss fix

- **No functional change** — Faust 0.16.1 fixed the real "only half the bosses" cause entirely **server-side** (the
  board read roaming V Bloods' position from a stale render matrix; it now reads the live sim position, so roamers
  report **Live** with coordinates). The wire shape is unchanged (still ApiVersion 18), so Raphael needs no code
  change — it simply receives coordinates for more bosses once the **server runs Faust 0.16.1**.
- Updated the **Boss Status** explanation to describe the roaming-boss fix (the old text blamed only the ±5000 /
  map-limit cutoff, which was a separate, secondary cause). Confirm your server's handshake reports `plugin=0.16.1`
  before re-testing.

## 0.58.4 — World map: fix the layout overlap (no more filters/caption colliding with the map)

- **Fixed the map underlay overlapping its neighbors** — the filters above and the caption below no longer collide
  with the map board. The square-aspect board is now driven from a **fixed height** (HeightControlsWidth), so the
  page layout reserves the correct vertical space for it instead of the board expanding over its siblings.
- *Still calibration, not a bug:* if markers line up horizontally but not vertically, the map image's **vertical**
  world coverage doesn't match the Z bounds yet — adjust **World Z at bottom/top edge** (shift both together to
  move the map up/down). Tell me one boss + where it should sit and I'll compute exact values.

## 0.58.3 — World map: actually stop the stretch (square board + normalized dots)

- **The map underlay no longer renders stretched.** The previous aspect fix corrected the world rectangle but the
  board's *pixels* were still force-stretched to the panel width by the layout, so the square map looked squished.
  The board now holds the image's aspect with an **AspectRatioFitter** (stays square no matter the panel width),
  and all dots / heat cells / grid lines are positioned by **normalized anchors** (0–1) instead of pixel offsets,
  so they fill the board correctly at any size. Applies to both the **World Map** and the **Heat Map**.
- Map *position* alignment is still set by the four calibration bounds (the captured texture's exact world
  coverage isn't documented) — calibrate with **Use heat-map bounds** + the bound fields.

## 0.58.2 — World map underlay: fix the skewed/stretched map image

- **Fixed the map image rendering stretched** on the World Map and Heat Map. The captured V Rising map is square,
  but the board was sized to the (non-square) calibration rectangle, so the image got vertically/horizontally
  stretched. The render rectangle now **matches the image's aspect ratio** automatically (center-preserving), so
  the map draws undistorted and the dots share its space. Calibrate position/scale with the four bound fields (or
  **Use heat-map bounds**) as before — they now stay square to the image.

## 0.58.1 — Boss coords: fully trust Faust (remove the client cutoff)

- Finished the §18 follow-up from Faust's updated contract: Raphael's boss **location guard is now removed entirely**
  (was raised to 9990 in 0.57.2). Faust decides a boss's live/down state server-side via its tunable
  `[Faust.Bosses] MapLimit` (now up to 20000) and only sends coordinates for bosses it classifies on-map, so
  Raphael displays whatever coordinates it receives — no client-side distance filter that could re-hide a far boss
  if an admin raises the limit. `FaustBoss.OnMap` is now simply "has a position."
- *Note:* this only governs how Raphael **displays** boss positions. A board still showing many "Not spawned" rows
  means the **server** isn't running the fixed Faust yet — the live handshake reports `plugin=0.16.0`, and the boss
  data is unchanged, so the 0.16.1 `MapLimit` fix needs to be built and deployed to the game server (then restart).

## 0.58.0 — Capture the world-map image from the running game (no external tools)

- **New: “Capture map from game” on Faust → World Map → Map underlay.** V Rising ships its art inside hashed
  Addressable archives (no loose image file to copy), so Raphael now grabs the map **texture from the running
  game** instead: open the in-game map once (press **M**) so it loads, then click **Capture** — Raphael scans the
  game's loaded textures, logs the candidates to the BepInEx log, and saves the best match to
  `config/Raphael/worldmap.png` (then **Reload image** / calibrate). If the auto-pick isn't the map, the log lists
  every candidate texture name — type part of the right one into the **filter** field and capture again.
  *Experimental:* the map texture's name is undocumented, so it may take a try or two with the filter to land the
  right one.
- This is the answer to “can't you pull the map from my game files?” — the files are archived, but the live
  texture can be captured. Still fully local and client-side.

## 0.57.3 — Map underlay: clearer image status

- The **Map underlay** card now shows a plain **image status line** — whether `worldmap.png` was loaded (with its
  dimensions), or, if not, exactly where to drop it (`BepInEx/config/Raphael/worldmap.png`) and to click **Reload
  image**. This makes it obvious that a black-background-with-grid map just means no image file is present yet
  (the grid is the always-on fallback; the world-map picture is a file you supply). No behavior change to the
  rendering itself.

## 0.57.2 — Boss coords fix: drop the old ±5000 guard (Faust 0.16.1, §18 resolved)

- **Live outer-region bosses now show their location instead of “—”.** `.faust admin bossdiag` proved the V Rising
  map extends well past ±5000 and that V Bloods keep their **real** positions (there's no ~10000 sentinel-parking,
  contrary to the earlier assumption). Faust raised its default live/down threshold to **9000**, and Raphael's
  leftover **±5000 client-side coord guard** (from the old §16 fix) — which would have re-hidden those now-correct
  coordinates — has been removed; Raphael now trusts Faust's positions (keeping only a defensive guard against the
  literal ~10000 off-map sentinel).
- Updated the **Boss map limit** control accordingly: default shown as **9000**, range widened to **20000** (matching
  Faust), with clearer guidance; the on-tab note and the config editor's `bossmaplimit` hint were refreshed.
- With this, **§18 (live bosses reading “Not spawned”) is resolved.**

## 0.57.1 — Boss board: tune the live/“Not spawned” threshold from the UI (Faust 0.16.1)

- **Faust 0.16.1 makes the boss live-vs-down threshold tunable**, and Raphael now surfaces it. If real,
  in-world V Bloods show as **“Not spawned”**, the **Boss Status** tab has a new **Boss map limit** field +
  **Set limit** button (admin) — raise it toward **6000–8000** (keep under ~10000) and Refresh. Drives Faust's
  new `.faust admin setglobal bossmaplimit=N`. The on-tab note now explains the fix, and `bossmaplimit` was also
  added to the live config editor's global settings.
- Background: V Rising parks not-currently-active V Bloods off-map (correctly “down”), but an outer-region boss
  whose real position sits just past the threshold was being mis-classed — that's the case this knob fixes.
  Genuinely parked bosses still read “down” (locating them needs spawn-zone data, a separate Faust effort).

## 0.57.0 — World map underlay (grid + drop-in map image), blood column split, boss-board note

Testing follow-ups for the Faust World Map / Boss Status.

- **World Map & Heat Map now have a frame of reference.** Both boards used to draw on a bare black background;
  they now show a **coordinate grid with corner X/Z labels** behind the dots, and can draw a **real world-map
  image** if you supply one. Drop V Rising's world-map texture (extract it from the game files, e.g. with
  AssetRipper) into `BepInEx/config/Raphael/worldmap.png`, then on **Faust → World Map → Map underlay** click
  **Reload image** and calibrate the four world-edge values (a **Use heat-map bounds** button pre-fills Faust's
  authoritative full-map bounds). When a backdrop is on, both boards render to that fixed world rectangle so dots
  land in their true map location. Grid/image toggles + image opacity are on the same card (and in Settings).
- **World Map table: blood is now two columns** — **Blood** (type) and **Q** (quality %) — instead of one
  combined cell.
- **Boss Status:** added an in-panel note explaining that some V Bloods alive in the world can still read
  **“Not spawned”** — V Rising lazy-spawns many V Bloods so Faust can't always resolve their position; it's a
  **server-side detection limit, not a Raphael paging issue** (verified from the log: Raphael loads the entire
  board — all pages). Added an admin **Diagnose detection** button (`.faust admin bossdiag`) to help the Faust
  side fix it, and corrected the Show-filter default label (it's **All**).

## 0.56.0 — World Map: unit categories, resource tiers + in-game prefab lookup (Faust 0.16.x)

Follow-up pass consuming the rest of Faust 0.16's API-18 batch on the **World Map** tab. Gated on Faust 0.16+.

- **World Map now shows unit categories & resource tiers.** Each scanned asset carries Faust's
  `EntityCategory` classification — NPC units show a **category** number and resource nodes a **tier** number
  (in the table's Kind column and the map-dot tooltips). A new **Unit category** filter narrows units to one
  category. (The numbers are the game's raw category ints; use the new audit button below to discover them.)
- **In-game prefab lookup (admin).** A new **Prefab lookup & diagnostics** card on the World Map tab: type a
  **PrefabGUID** to get its dev-name, or a **partial name** to search the catalog — so you can fill the
  whitelist / item-cost / proximity GUID fields without an external dump (`/.faust admin prefab`). An **Audit
  scan** button (`.faust admin worldscandiag`) dumps a prefab's category numbers + Faust's unit/node verdict so
  you can set the category filter. Replies appear in chat.
- Under the hood: Raphael now reads the `unittype`/`restier` tokens Faust adds to each `worldscan` asset row
  (additive — older Faust simply omits them).

## 0.55.0 — New: World Map tab + the config-editor "Set" fix (Faust 0.16.x)

Faust shipped the fixes plus a new server-side scan, all consumed here. Gated on Faust 0.16+ (API 18).

- **New: World Map tab (Faust → World Map).** A filterable map of in-world **NPC units** (with blood type and
  quality) and **resource nodes**, scanned **server-side by Faust** and rendered here. Filter by **type**
  (units / nodes / all), a specific **prefab ID**, a **blood type**, and a **minimum blood-quality slider**;
  results show as both a **table** and an **X/Z dot map** (units coloured by blood quality, resource nodes
  green — hover any dot for details). Honors Faust's "truncated — narrow the filter" notice. Includes admin
  **whitelist** controls (list / add / remove / seed / clear). Admin-default (it reveals the whole map).
  *(This is server-side scanning — Faust scans and Raphael only renders the data it sends — distinct from the
  removed client-side scan; V Bloods are excluded here, they live on the Boss Status tab.)*
- **Fixed: the config editor "Set" actions now work.** Faust 0.16 changed the command syntax (its chat-command
  framework can't take a multi-word value), so Raphael now sends settings as a single `setting=value,setting=
  value` token — e.g. `set castleinfo costitem=…,costqty=…`. The earlier *"Too many parameters"* error is gone.
- **Boss lookup** now sends the boss name/GUID as a single token (matching the new server parser), so the
  single-boss lookup no longer errors on multi-word input.

## 0.54.5 — Diagnosed two issues to the Faust server side (filed upstream) + in-panel notes

Investigated both via the BepInEx diagnostic log; **both are server-side (Faust) and Raphael is behaving
correctly** — filed precise fixes in `docs/FAUST_API_REQUESTS.md` (§17, §18).

- **Config editor “Set … Too many parameters” error = a Faust bug.** Raphael sends a correct command
  (verified in the log, e.g. `set castleinfo costitem 862477668 costqty 100`); Faust's `set`/`setglobal`
  commands are **missing the `[Remainder]` attribute** on their value parameter, so the chat-command framework
  rejects any value with *“Too many parameters.”* There is no client workaround — it needs a one-line Faust
  fix. The config editor now shows a note explaining this; **Get current values still works**.
- **Boss board “Not spawned” bosses = Faust's live-detection, not a Raphael paging bug.** The log confirms
  Raphael fetches the **whole** board (all pages, e.g. `count=52`) and shows exactly the `up`/`down` status
  Faust sends. Bosses appearing as **Not spawned** are reported that way by Faust (`status=down` on the wire).
  The boss tab now clarifies what Live / Not spawned mean; whether a specific boss is mis-classified is a
  Faust-side question (filed with detail for the Faust dev).

## 0.54.4 — Boss board shows all bosses; config-editor input hints + validation (tester feedback)

- **Boss board defaults to “All” again, so nothing is hidden.** The 0.54.2 default (“Live + defeated”) was
  hiding the pooled / not-spawned V Bloods that Faust returns — they’re back by default. Down bosses now read
  **“Not spawned”** (instead of bare dashes) so they’re clearly a known-but-inactive boss, not a blank row.
  The **Show** filter still lets you narrow to Live + defeated / Live only.
- **Config editor: every numeric field now has a description.** Each row (Cost, Cooldown, Limit, Proximity)
  has a one-line explanation under it — e.g. *“Period s = the period length in seconds (3600 = hour, 86400 =
  day); Window s = optional grace window”* — so the terse field labels are no longer ambiguous.
- **Config editor: clearer errors on bad input.** Entering something that isn’t a number (e.g. an item **name**
  instead of its PrefabGUID hash) now shows an immediate in-panel hint instead of bouncing a server reject —
  *“Item is the prefab GUID hash (e.g. 576389135), not the item name.”* (Cost/limit/proximity all validate.)
  Reminder: setting an item cost takes the item’s **numeric PrefabGUID**, not its display name.

## 0.54.3 — Fix: config editor "Set cost" (and limit / proximity) errored on multi-value rows

- **Fixed: setting a two-value gate from the config editor (e.g. cost = item GUID + quantity) errored.** The
  multi-value rows (Cost, Limit, Proximity) were sending one `.faust admin set` command **per field**, fired
  back-to-back in the same frame — the server dropped/errored on the second. They now send a **single**
  command with all the pairs at once (`set <feature> costitem <guid> costqty <n>`), which Faust applies in one
  go. (Matches why it worked when you set the values one at a time before.)

## 0.54.2 — Boss board filter + one-click tracking; config editor cleanup (tester feedback)

- **Boss board: a “Show” filter, so the board isn’t flooded with blank-looking rows.** Faust pre-instantiates
  much of the V Blood roster as “not spawned”, which filled the board with sparse rows. The board now defaults
  to **Live + defeated** (hiding bosses that are neither in the world nor ever killed); cycle the **Show**
  button for **All** or **Live only**. The status line shows “X shown / Y total”.
- **Boss board: one-click tracking.** Every board row now has a **+ Track / ★ Tracked** button that adds or
  removes that boss from the tracker overlay — no more typing names. (The typed-name field stays as a way to
  pre-add a boss that isn’t on the board yet.) Adding/removing anywhere keeps the board buttons, the tracker
  list, and the overlay in sync.
- **Config editor reorganized.** **Get current values** and **Reset feature** now sit directly under the
  feature selector, so you can read the live values *before* changing anything. The separate **Any setting**
  cycler is gone — the inline rows now cover **every** per-feature setting (access, PvP, delivery, admins-exempt,
  cost, cooldown, usage-limit, proximity, and the unlock criterion incl. a *BossKill:&lt;guid&gt;* input), each
  on its own row with its own control(s).
- *Note on boss names:* names already resolve to the in-game friendly form for the classic V Bloods; any newer
  boss not in the map falls back to a tidied prefab name. With the default filter most boards now show only
  named, meaningful rows.

## 0.54.1 — Boss board polish + boss-tracker overlay (tester feedback on 0.54.0)

- **Boss names are now the in-game player-friendly names.** The Boss Status board and the boss-defeat
  leaderboard show e.g. *Dracula the Immortal King* instead of the raw `CHAR_…_VBlood` prefab name (resolved
  from the V Blood's PrefabGUID; unknown / modded bosses fall back to a tidied dev-name).
- **No more bogus boss coordinates.** Bosses that exist as a pooled / staged entity reported a far-off limbo
  position (e.g. 10000,10000) and showed as "outside the map"; those are now treated as having no real map
  position (the Loc column shows "—") so only genuinely-placed bosses display coordinates.
- **New: Boss Tracker overlay.** A small movable HUD that tracks up to **3** chosen V Bloods' status (live /
  defeated, with HP) at a glance. Assign bosses on **Faust → Boss Status** (type a name + *Add to tracker*),
  toggle it from there or the main-panel **Show overlays** footer (**Boss Tracker**), and optionally enable
  **auto-refresh (~5s)**. It's a standard overlay — opacity, text-size, transparency slider, and **Lock
  overlays** all apply. (Auto-refresh is opt-in and bounded by the Faust query cooldown to limit server load.)
- **Config editor: common gates as straight-line rows.** The Live config editor now has a **Quick set** section
  with dedicated rows — Access, PvP, Cost (item + qty), Cooldown, Limit (uses / period / window), and Proximity
  (object + metres) — each with its own field(s) and Set button, so you no longer have to cycle the setting
  picker for the common ones. The generic per-setting cycler remains for everything else.

## 0.54.0 — Faust 0.16 (API 18): boss board, kill leaderboards, live config editor

Consumes the new **Faust, Lord of Investigation 0.16** server features (wire API 18). All of it is gated on
the Faust handshake, so it only appears on servers running Faust 0.16+; older Faust degrades gracefully with
a "needs Faust 0.16+" note.

- **New: Boss Status tab (Faust → Boss Status).** A server-wide **V Blood status board** — which bosses are
  **live in the world right now** (with map location, region, an HP bar and level) and which have been
  **defeated**. Faust sees the whole map, so it reports bosses your client can't. Includes a **single-boss
  lookup** (type a name — multi-word names work — or a PrefabGUID). Admin-default (boss locations are intel).
- **New: Leaderboards tab (Faust → Leaderboards).** Two server boards from Faust's kill tracking — the **top
  killers** (with how many of their kills were PvP) and the **most-defeated V Bloods** — with a
  **today / this week / all-time** window selector. Admin-default; needs the server's kill-tracking on.
- **New: Live config editor (Faust → Admin: Control).** Admins can now change a feature's **cost, cooldown,
  usage limit** (uses / period / window), **proximity** requirement, **access**, **PvP availability**, or
  **unlock** criterion — plus **global** settings (anti-spam, data collection, heat-map, map markers) —
  **at runtime**, with no config-file edit or server restart. Faust's confirmation appears in chat. (This
  resolves the earlier limitation where these gates were config-file-only with no way to set them from the UI.)
- **Admin: Oversight now shows each feature's gates.** The Access table gained a **Gates** column listing the
  configured cooldown / usage-limit / proximity for each feature (read-only), alongside the existing cost.
- **Fixed (server-side): the "Data status" button works again.** The Faust **Admin → Data status** button no
  longer errors on busy servers — Faust 0.16 fixed the oversized-reply bug that was throwing in the chat
  command framework. The data-wipe control also gained the new **heatmap** and **kills** stores.

## 0.53.0 — Quick Spawn overlay: footer toggle + transparency

- **Familiar Quick Spawn overlay now has a footer quick-toggle.** The overlay added in 0.52.0 could only be
  shown/hidden from the All Familiars tab; it now has a **Quick Spawn** toggle in the main panel's
  **Show overlays** footer alongside every other overlay, so you can stash/restore it with one click.
- **Quick Spawn transparency slider.** Added a **Quick Spawn** row to **Settings → Display → Overlay
  transparency**. The overlay already honored the overlay opacity, text-size and **Lock overlays** controls;
  it now sits with the other overlays under the transparency sliders too, so it's fully covered by the
  standard overlay controls.
- **Faust (server-side, filed upstream):** the **Faust → Admin "data status"** button can return a VCF error
  on busier servers — this is a reply-size bug **inside the Faust mod** (its status text overruns the chat
  command framework's 512-byte limit), so Raphael can't work around it; it's been filed for the Faust dev.
  Likewise, per-feature **cost / cooldown / usage-limit / proximity** gates are Faust **config-file** settings
  with no chat command behind them, so Raphael's admin tabs can't drive them yet — also filed upstream.

## 0.52.0 — New: Familiar Quick Spawn overlay

- **New: Familiar Quick Spawn overlay (Bloodcraft).** A small, draggable overlay with up to **5 one-click
  buttons**, each pinned to a specific familiar. Clicking a button summons that familiar **by name regardless
  of which box it's in** (Bloodcraft's smart-bind, `.fam sb`) — if a different familiar is active it switches
  automatically. Two footer buttons cover the two distinct "put away" actions:
  - **Dismiss / Recall** (`.fam t`) — toggles your *active* familiar offline/online without unbinding it. It
    stays your active familiar; it's just hidden / out of combat.
  - **Unbind** (`.fam ub`) — releases the binding so you can summon a different familiar (non-destructive; the
    familiar returns to its box). Clicking a slot whose familiar is *already* active also Dismisses/Recalls it.
- **Assign familiars from the All Familiars tab.** Each row gains a **+ QS** button to pin that familiar; a new
  **Quick Spawn slots** card at the top of the tab lists your assignments (with **Clear**) and a
  **Show/Hide Quick Spawn overlay** button. Assignments persist across sessions.
- The overlay is fully integrated with the overlay system — draggable/resizable, its own transparency, honors
  the **OV** hide-all toggle and the **Lock overlays** setting, and restores on login. It shows a hint when no
  familiars are assigned and disables itself if the server has Bloodcraft's Familiar system turned off.

## 0.51.1 — Fix: Combined overlay came back as individual overlays after "hide all"

- **Fix: the OV "hide all overlays" button dropped the Combined overlay.** For players using the single
  **Combined** info overlay (instead of the standalone XP / Familiar / Daily Quest / Professions overlays),
  pressing the **OV** button to hide all overlays and then un-hiding brought them back as the **individual**
  overlays rather than the Combined one. The un-hide path wasn't combined-mode-aware: it re-showed the four
  individual overlays from their (independently-persisted) Show* flags and never re-showed the Combined panel.
  Un-hide now restores the Combined overlay when combined mode is on, matching the login and availability
  restore paths. (Reported via tester feedback.)

## 0.51.0 — Compliance update

A compliance-only release that brings Raphael into line with the V Rising modding community's guidelines.
No new features, and no other behavior changes.

## 0.50.0 — Large-font overlap sweep + Faust visualization/usability fixes (tester feedback)

### Large-font overlap fixes (UI text size)
The v0.49 font pass scaled fonts and the shared helpers, but many hand-rolled rows in individual tabs still
pinned their container height to raw pixel constants — so at Large/X-Large text the captions grew while the
box didn't, and content spilled into the neighbouring row. These tab-local rows now scale their heights with
the UI font multiplier (`Theme.ScaledHeight`), matching the shared helpers:

- **Kindred (Commands / Logistics / both Admin tabs).** The shared `AddKLRow` action-row helper (and the
  inline button rows + wrapped warning/hint labels on the Kindred admin tabs) now scale — fixes the
  overlapping buttons reported in the Logistics tab and across the other Kindred tabs.
- **Settings → Combined overlay.** The descriptive "subtext" under *Use combined overlay* was a fixed-height
  label that slid under the toggle at larger text; it now uses a `ContentSizeFitter` so it grows with its
  wrapped content. The master toggle, per-section toggle rows, and the *Show progress bars* / bonus-stats
  rows scale too.
- **Familiar browser (box contents + All Familiars table).** The box-content list rows/buttons, the
  "Familiars in (none)" active heading area, and the All Familiars table (header, rows, cells, Bind/Delete
  buttons) all scale, so table data no longer slides under the row above it. The box-list buttons also now
  scale their own font (previously they ignored the UI text size).
- **Uriel** (storage sharing + every Uriel tab) and **Beelzebub** (all tabs): the shared row/label/toggle
  helpers and the per-tab control rows, table cells, and category grids now scale.

### Faust
- **Visualizations are more readable at larger text.** The bar-chart y-axis tick labels (previously a tiny
  base size 8) and x-axis labels are bigger, and the chart body height now scales with the UI font so the
  whole chart grows legibly instead of cramming taller text into a fixed 90px box.
- **Chart width control + left alignment.** Bars now anchor to the left. New **Faust → Settings → Charts**
  toggle *"Stretch charts to fit the panel width"* (default on = dynamic/stretch; off = a compact,
  left-anchored static width).
- **Server Stats: aggregate vs. average clarity.** Added a "Totals vs. averages" legend to the Server Stats
  intro spelling out that the numbers are aggregate totals across all players unless a row says *avg*, and
  the Daily view's hover now also shows the derived average play-minutes per active player. (A
  median/mean/aggregate view toggle isn't possible client-side — Faust sends these already summarised, not
  as a raw per-player spread.)
- **Clan members — click a clan to view its roster.** Root cause of the "no reply": clan names arrive with
  underscores for spaces (e.g. `Testing_Clan`), and typing `Testing Clan` splits the name into two tokens so
  the server never matches it. The clan-name cell in the **Clans** roster is now a button that queries that
  clan's members with its exact name (no retyping), the input prefills on click, and the hint explains the
  underscore convention. (The client command + parsing match the Faust contract exactly; the failure was the
  typed name, confirmed from the wire log.)

### 0.50.0 follow-up fixes (same version, tester round 2)
- **Faust charts: no more duplicate Y-axis labels.** On small-count charts (e.g. a DAU/online value of 2–3)
  the evenly-spaced ticks rounded to the same integer and the axis showed repeats (2, 2, 1, 0, 0); duplicate
  ticks are now blanked so the column stays distinct (top = max and bottom = 0 always show).
- **Connection tab now lists Faust.** Settings and Help → Connection had cards for Bloodcraft / Beelzebub /
  Uriel but not Faust; it now shows a Faust connection readout (api / plugin / detection state) with its own
  **Re-detect Faust** button.
- **About tab credits the companion mods.** The "Mods this UI is built on" section now also credits
  **Beelzebub**, **Uriel**, and **Faust** (alongside the external Bloodcraft / KindredCommands), describing
  what each tab group surfaces.
- **Faust chart drill-downs — now built (Faust 0.15 / api 14).** Faust shipped the per-player/per-event data
  (the §9 batch), so Server Stats gains three new views plus a chart toggle, all gated on api ≥ 14:
  - **New-player roster** — who joined recently, when (first-seen, UTC), and their clan (the names behind the
    New-players / New-vs-returning counts).
  - **Active-days grid** — per-player active-days over the Days window, with a ranked bar overview and a
    per-day playtime breakdown on row hover.
  - **Session timeline** — a Gantt-style per-player timeline of actual online intervals across the window
    (hover a bar for exact session times).
  - **By hour of day → Avg / Total toggle** — switch the hour chart between total play-minutes and the average
    per active player that hour (Faust now sends the per-hour player counts).
### 0.50.0 follow-up fixes (round 3 — Faust visualization polish)
- **Charts "right-aligned" SOLVED.** Root cause: the y-axis tick column and the plot were forced to *equal*
  width, so the axis ate the left half and the bars were crammed into the right half. The y-axis now stays at
  its narrow fixed width and the plot fills the rest — bars use the full chart width again.
- **Y-axis labels no longer over-wide** (same fix — the tick column is back to its intended ~40px).
- **X-axis date labels** added under the **Session timeline** and **Active-days grid** (evenly-spaced short
  dates across the window, so you can read which days a bar/square falls on).
- **Active-days grid is now a heatmap** — instead of a single bar, each player gets a row of day squares
  (oldest → newest), filled/brighter when they were online that day and dim when not, so you see *which* days
  they played. Hover a square for that day's playtime.
- **Peak concurrency now has a visual** — a Now / Avg / p95 / Peak comparison bar chart, so the live count's
  relationship to typical and peak reads at a glance.
- **New chart-color setting** — **Faust → Settings → Charts → "Bar color"** cycles Green / Teal / Blue / Red /
  Amber / Violet; every Faust chart, graph, timeline, and heatmap follows it.
- **New-player roster** is forward-compatible with **Playtime + Castles** columns — they appear automatically
  once the server's Faust build sends that data (filed as a Faust request, §10a).
- **By-region chart + roster extras filed (Faust §10).** A meaningful region "fill %" (claimed ÷ buildable
  plots) and a per-day per-region time-series need data Faust doesn't send yet; spec'd in
  `docs/FAUST_API_REQUESTS.md` §10b/§10c to build once Faust ships it. The current chart stays a raw castle
  count until then.

### 0.50.0 follow-up fixes (round 4 — Faust §10 region data + player-position heat map)
Faust shipped §10 (wire api 15) and a new player-position heat map (api 16), all still under Faust 0.15.0.
Raphael now consumes them (each gated on the handshake api, so older servers are unaffected):
- **By-region → castle fill %.** When the server sends the buildable-plot denominator, the By-region chart
  pivots from raw castle count to **fill % (claimed ÷ buildable plots)** — a fair "how popular is building
  here" measure across regions of different sizes; the table gains Plots + Fill % columns.
- **New "Region over time" view** (Server Stats) — per-region **fill-% sparklines** (one row per region, a bar
  per sampled day) plus a by-date table. Shows how building popularity in each area trends. (Faust samples the
  castle map once per UTC day from install, so the series is sparse.)
- **New-player roster Playtime + Castles** columns now populate from live server data.
- **NEW — Player activity heat map** (Faust → Player Positions). A density grid of where players spend their
  time on the map (**+X east → right, +Z north → up**), brighter = more time — **server-wide or per-player**.
  Great for spotting popular areas / dead zones and, for PvP, where to find people. Requires the server's
  `[Faust.Heatmap] Enabled = true` (admin opt-in); the UI clearly says when sampling is off. Hover a cell for
  its world coordinate + sample count, and the chosen **chart color** theme applies to the heat gradient.

### 0.50.0 follow-up fixes (round 5 — chart stretch, axes, region matrix, heat-map fix)
- **Heat map "nothing found" fixed.** The server heat-map query was sending `.faust api heatmap 1`, and Faust
  read the page number `1` as the *target* → "nothing found." It now sends `.faust api heatmap all 1`.
- **Horizontal bar charts now stretch to fill the panel.** Playtime leaderboard, session lengths, and the
  peak-concurrency comparison used a fixed ~200px max bar; they now use a flexible bar that fills whatever width
  the panel gives (bar length still = value ÷ max), so a small data set still spans the full width.
- **Concurrency chart gained axes.** It now renders through the same bar-chart path as the others, so it has a
  y-axis (online count) and a sparse time x-axis — and stretches + follows the chart color.
- **X value-axis added** under the horizontal bar charts (0 → max), and the concurrency/region charts get
  date/time x-labels. Every chart now carries axis context.
- **Region over time → matrix table (your format).** Below the per-region fill-% sparklines, the table is now a
  grid with **regions as columns (angled headers)** and **one row per sampled day**; each cell shows
  `castles/plots` shaded by fill %, with the exact %/counts on hover. (Capped at 9 region columns to fit.)

### 0.50.0 follow-up fixes (round 6 — heat-map colors/detail, castle coords, chart text)
- **Heat-map color ramp (new setting).** The heat map now uses a true cold→hot **gradient** instead of one flat
  hue. Pick the ramp in **Faust → Settings → Heat map** (or the live **Colors** cycler on the heat-map card):
  *Theme* (black→chart color), *Heat* (black→red→yellow→white, the new default), *Green*, or *Mono* (black↔white).
  A "less → more" legend is drawn under the grid.
- **Heat-map detail / granularity.** New **Detail** control (Native / Grouped 2× / Grouped 4×) merges cells into
  bigger blobs to smooth sparse data. The true resolution is the **server's `[Faust.Heatmap] CellSize`** — the
  card now explains that big blocks usually just mean sparse data, the map sharpens as more players roam, and the
  only way *finer* is lowering CellSize server-side. The grid also renders larger/scaled for a more map-like read.
- **Castle coordinates (Open plots / All plots / Decay watch).** These tables now auto-add a **"Loc (X,Z)"**
  column showing each castle's world position — the moment Faust sends it. Faust doesn't emit territory coords
  yet, so the column stays hidden until then (the tables look unchanged); the data ask is filed as Faust request
  §11a, and Raphael is already wired to display it.
- **Chart text scaling.** Confirmed every chart/axis label scales with the main UI text size; bumped the
  smallest X/Y tick labels a notch for legibility. (The bar charts already stretch to fill the panel — verified
  the layout end-to-end; the heat map / region matrix are fixed grids by nature and were widened.)

### 0.50.0 follow-up fixes (round 7 — Faust api 17: coords live, heat-map true scale)
- **Castle "Loc (X,Z)" goes live.** Faust 0.15.0 (ApiVersion 17) now emits `posx`/`posz` on every castle/plot
  row (territory centroid), so the **Open plots / All plots / Decay watch** tables now show each castle's world
  location automatically — no Raphael update beyond the forward-wiring shipped last round.
- **Heat map at true map scale (new "Scale" control).** Faust now sends the full buildable-map bounds
  (`mapbounds`), so the heat map can draw at real map scale — sparse data reads as a few dots on the actual map
  outline instead of a tiny zoomed board. New **Scale** cycler (Full map / Zoom to data) on the heat-map card and
  in Faust → Settings → Heat map; defaults to **Full map** when the server supports it, falls back to Zoom on
  older Faust. Coarsen (Detail) and the cold→hot color ramp still apply.
- **Clan members now respond.** The "server doesn't respond" on clan-member views was a Faust-side argument-binding
  bug (clan names with spaces); fixed server-side in 0.15.0 — works after the server restart, no Raphael change.

### 0.50.0 follow-up fixes (round 8 — New-players merge, New-vs-returning two-color, chart text, chat fixes)
- **New players + roster, one view.** The separate "New-player roster" button is gone; the **New players** view now
  shows the per-day count chart *and* the "who joined" roster (name · joined · clan, plus playtime/castles when
  Faust sends them) together.
- **New vs returning — two bars per day.** Instead of charting only new players, each UTC day now shows two
  colored bars side by side — <b>green = new</b>, <b>amber = returning</b> — with a legend, so admins can see at a
  glance whether growth or retention is trending. The table still lists active/new/returning.
- **Region-over-time header fixed + bigger chart text.** The 45°-angled region names no longer overflow the top of
  the card (taller header, bounded labels). Across all Faust charts, the in-visual text, axis tick labels, and
  caption/subtext were enlarged so they're legible at 100% UI text size (they were noticeably smaller than the
  surrounding UI text).
- **Chat: @recipient now follows the conversation.** Switching whisper tabs — or re-opening an existing
  conversation from the "+ Whisper…" picker — now updates the **@name** in the message bar to that person
  (previously it could keep showing the previous recipient).
- **Chat: keyboard scrolling.** <b>Up/Down</b> (and <b>PageUp/PageDown</b>) now scroll the chat history, so you're
  not stuck with the tiny scrollbar that shrinks as the buffer grows. Arrows yield to the text caret while you're
  typing (unless the pointer is over the log); PageUp/PageDown always scroll.

### 0.50.0 follow-up fixes (round 9 — OV chat toggle, region text, clan members)
- **Clan members now actually return.** Root cause (from the diag log): Raphael was sending the clan's *display*
  name with spaces (e.g. `clanmembers Testing Clan 1`), which split into separate args and never matched. It now
  sends the **wire-safe single token** (`Testing_Clan`) Faust stores, so the member roster comes back. (Works
  whether you click a clan row or type the name.)
- **"Hide chat with overlays" is now in Settings.** The master toggle lived only on the main-panel footer, so it
  was hard to find. It's now also under **Settings → Display → Overlay Visibility**, paired with a clear
  **"Keep GAME chat hidden too (else it returns)"** sub-toggle — so you control whether the OV "hide all overlays"
  button also hides the game's native chat, and whether it comes back. Both apply immediately. (If the OV button
  was hiding your game chat unexpectedly, turn the top toggle off — with it off, OV never touches chat.)
- **Region-over-time headers no longer cut off.** Last round's fix stopped the angled region names from
  overflowing the top but clipped them with an ellipsis; the header is now tall enough to show the **full** name.

### 0.50.0 follow-up fixes (round 10 — settings reformat, clan members = Faust bug)
- **Settings pages reorganized into cards.** The **Game UI / chat** settings (one giant block of ~30 toggles) and
  **Settings & Help → Display** (a long flat scroll) are now grouped into discrete, titled **cards** —
  *Text & button size*, *Overlay transparency*, *HUD extras*, *Channel colors*, *Tab-switch hotkeys*, etc. — so
  each topic reads as its own box instead of one wall of toggles. Same settings, much easier to scan.
- **Clan members = confirmed Faust-side bug.** Raphael now sends the correct wire-safe token
  (`clanmembers Testing_Clan`, verified in the log), and **every other Faust command replies while this one is
  totally silent** — no rows, no end marker, not even an error. The clan is clearly visible (the `clans` reply
  lists it), so it's not a name/gate problem. Filed for a Faust server-side fix (request §12); Raphael also now
  drops the trailing page number on page 1 to rule out a server-side parse throw. Nothing more Raphael can do here.

### 0.50.0 follow-up fixes (round 11 — chat scroll arrows + opt-in scroll keys)
- **Clickable scroll arrows on the chat scrollbar.** Small <b>↑ / ↓</b> buttons now sit at the top and bottom of
  the chat window's scrollbar; each click nudges the history a few lines — much easier than grabbing the tiny
  scrollbar handle on a big buffer. Toggle them off with **"Scroll arrows on the chat scrollbar."**
- **Keyboard scrolling is now opt-in (so it can't steal gameplay keys).** Two separate toggles under
  **Game UI → chat → Message display & format:** <b>PageUp / PageDown</b> (default <b>on</b> — rarely bound) and
  <b>Up / Down arrows</b> (default <b>off</b> — they commonly clash with movement/abilities). Previously both were
  always on; now you choose.

## 0.49.0 — Faust guide merge + font-size-% fixes (tester feedback)

- **Faust: one combined "Faust Guide" tab.** The separate *Faust Quick Start* and *Faust Help* tabs are
  merged into a single **Faust Guide** under Settings and Help — quick start on top, full command/feature
  reference below. Faust is informational-only, so two tabs was more than it needed (the Bloodcraft /
  Beelzebub / Uriel pairs stay split).
- **Font-size %: left rail now grows to fit the text.** At larger font-size-% settings the longest tab
  captions overflowed past the right edge of their buttons in the left pane. The rail (and its buttons /
  containers) now scale their width in lockstep with the font multiplier, so captions stay inside their
  buttons; the content area absorbs the difference, so the pane only widens as much as the text needs.
- **Font-size %: subtext now scales too.** A number of inline row labels and italic hint/“subtext” lines in
  Settings (overlay hide-mode, timed duration, transparency rows, launcher-button size, the font-size slider
  row itself, segmented buttons) used fixed point sizes and ignored the slider — only headers and primary
  body text resized. They now route through the same UI font multiplier, so the whole panel scales uniformly.
  (Takes effect on the next panel open, like the other font-scale changes.)

## 0.48.0 — Faust 0.14 (§8 batch): castle detail, prisoners, clan members, new-vs-returning, oversight

Consumes Faust **ApiVersion 13** (Faust 0.14.0) — the tester batch I filed as §8 is now fully delivered.
All api-gated, so it stays hidden on older servers.

- **Castle Info — more detail.** Now also shows **Floors** (storeys), the owning **Clan**, and the castle's
  **total item count** (the single high-level number — the per-item breakdown stays in Castle Resources, kept
  separate for PvP). Each appears only when the server resolves it. (Heart level / age are reserved in the
  wire but not yet emitted — the game exposes no reliable source; they'll light up if Faust finds one.)
- **Castle Resources — prisoners.** The header reports the **prisoner count**, and a **prisoner sub-table**
  lists each prisoner with their **blood type + quality** below the item list.
- **Clans — member rosters.** A new **"Clan members"** section (enter a clan name from the roster) lists that
  clan's members, leader first, with who's online — backed by `.faust api clanmembers`.
- **Server Stats — "New vs returning."** A new view splits each day's active players into **new** (first seen
  that day) vs **returning** — a growth-vs-retention chart + table, over the Days window.
- **New tab: Faust → Admin: Oversight** (admin). **Access** = each feature's scope, price, and how many
  players are granted / qualify ("who can use what"). **Usage** = per-feature uses, paying players, items
  spent, and cooldown denials over a window ("how it's being used / what it earns the server"). Pure
  server-side accounting — no client overhead. Backed by `.faust api access` / `.faust api usage`.
- Wire layer extended for all of the above; docs (`FAUST_API_REQUESTS.md` §8 + `FAUST_INTEGRATION_HANDOFF.md`)
  updated — only §3 (open-world region) and §5 (map-marker visibility) remain open on the Faust side.

## 0.47.0 — Custom font-size slider, chart axes, label reach & tester polish

- **Custom UI text size (slider + typed value).** The Small/Standard/Large/X-Large buttons are replaced by a
  **slider + a typed % box (50–400%)** for UI text, overlay text, and chat text — dial in an exact size for
  your monitor/TV (the old fixed steps looked drastically off on big screens), including **much larger than
  the old X-Large**. Drag or type, then **Apply** (or reopen the panel). 100% = the old "Standard."
- **Chart y-axis labels** (Server Stats + Player Info). Every bar chart now has a **left-side numeric scale**
  (max → 0) so you can read a bar's value at a glance without hovering, and the caption notes the top value.
- **Open Plots columns reordered** to **Territory · Region · Size** (text between the two numbers).
- **"By hour of day" clarified** — the caption/title now spell out it's **total playtime accumulated per UTC
  hour across all history** (the server's daily rhythm), **not** the last 24 hours.
- **Filed for Faust** (`docs/FAUST_API_REQUESTS.md` §8, for the data Raphael can't derive client-side): richer
  **Castle Info** (heart level / floors / age / clan / total-item count), **prisoners** in Castle Resources
  (count + names), **clan member** lists, a **new-vs-returning** split on the daily activity series, and
  **Faust access & usage reporting** (who can use each feature + how often / resources spent). When Faust adds
  these, Raphael will surface them.

## 0.46.2 — Uriel object-spacing reference (server-config note)

- **New: "Object spacing / overlap" reference** in Uriel → **Admin: Objects**. Documents the two values that
  control how close spawned objects may be placed — **`[ObjectSpawn] OverlapMinDistance`** (meters between
  objects, default 0.5; lower toward 0 to allow tighter placement, 0 disables the proximity check) and
  **`[ObjectSpawn] PreventOverlap`** (master switch, default on). Uriel exposes **no chat command** for these
  (they're server config in the Uriel `.cfg`), so Raphael can't change them live — the card explains exactly
  which keys to edit on the server and that a refused spawn replies "Can't place … it would overlap …". (If a
  live in-game control is wanted, it needs a new Uriel command — notes that it can be requested.)

## 0.46.1 — Fix: secondary chat window now hides with the OV "hide all" toggle

- **Fix:** with **"Hide chat with overlays"** on, the OV master hide-all button hid the main chat window but
  **left the secondary (view-only) chat window visible**. The secondary window now hides alongside the main
  one (and re-appears together with it), as expected.
- **Map markers — not a bug:** if "Show players on map" replies that it's *experimental and disabled in
  configuration*, that's the **server**: set **`[Faust.MapMarkers] Enabled = true`** in the Faust mod's config
  (`BepInEx/config/…Faust….cfg` on the server) and restart/reload, then click **Show on map: ON** again. Raphael
  is just relaying Faust's reply.

## 0.46.0 — Tester fixes: map markers, Uriel collection %, chat & admin clarity

- **Fix: Uriel collection progress showed >100%** (e.g. "2573 / 1687"). It was dividing your **total** unlocked
  objects (which includes non-discoverable / granted ones) by only the **discoverable** subset. Now it counts
  the **discoverable objects you actually have** as the numerator (always ≤ the total), shows that as the
  percentage, and lists your total-unlocked separately. In **Full mode** (or collection off) — where everyone
  has everything — it now reads “N objects available · Full mode” instead of a misleading fraction.
- **New: Show players on the in-game map** (Faust → Player Positions, admin · experimental). Wires Faust's
  server-side `.faust admin showpositions on|off|status` — the **server** pins native map icons on online
  players so they appear on the **M-key map**. Requires the server config **[Faust.MapMarkers] Enabled = true**
  (the button explains this if it's off). The old "Probe map icons" button is now clearly labelled a developer
  **diagnostic** that does NOT display icons — use the new ON button for actual markers.
- **Fix/clarify: Faust data reset** (Admin: Control → Reset / wipe). The text is now **red** and spells out
  that it **only clears Faust's own tracking data** (playtime log, charts, Faust feature-unlock records,
  cost/cooldown locks) and **does NOT touch the game** — your world, castles, characters, inventories, levels,
  blood, and V-Blood progression are all left untouched.
- **New: exclude Notes to Self from the main chat's All tab** (Game UI → chat settings). Mirrors the secondary
  window's "Notes to self" option — players who route self-notes to the secondary view-only window can now hide
  them from the main All tab. (The Whispers tab still shows them.)
- **Cleanup: Uriel → Admin: Objects “Spawn conditions”** reorganized for clarity — each input now sits directly
  above its button, with clear **▸ Per-object** and **▸ Global default** sections and yes/no permit toggles,
  instead of a wall of disconnected fields and buttons.

## 0.45.0 — Uriel admin: per-object spawn conditions + orphan purge

Catches Raphael up to Uriel's recent admin-config additions (Uriel 0.18+; no wire change — still ApiVersion 1).

- **New: Spawn conditions** in Uriel → **Admin: Objects**. Set limits on what **non-admin players** can
  spawn (admins bypass), per-object or as a global default:
  - **Max per plot** (0 = unlimited),
  - **Per-object item cost** (amount + item GUID; overrides the server's global spawn cost),
  - **Permit / forbid indestructible** and **permit / forbid respawn** flags.
  Plus **Show** / **Clear** for one object and **List conditions** for everything currently set. Backed by
  `.uriel objcfg` / `.uriel objcfgglobal` / `.uriel objcfglist`. Refused spawns (over-limit / can't-afford /
  not-permitted) reply in chat as before.
- **New: Purge orphans (server-wide)** button (Admin: Objects → Plot tools) — `.uriel purgeorphans` removes
  Uriel objects whose castle heart is gone (castle destroyed / decayed / open world); native objects are never
  touched. Two-click confirm, like the other destructive admin actions.
- Uriel command reference (Help) updated with the new admin commands.

## 0.44.0 — Faust 0.13: Player roster (active-today ✓ checkmarks) + rate-limit handling

Consumes Faust **ApiVersion 12** (Faust 0.13.0). API-gated, so it stays hidden on older servers.

- **New: Player roster** (Server Stats → **Player roster**, api 12) — the per-player table testers asked for:
  every tracked player with **✓ active today** (last 24h, UTC) and **✓ active this week** (last 7d) checkmarks,
  plus sessions, total playtime, and an **Idle** column (online / colour-coded days-since-seen). A summary
  line shows online / active-today / active-this-week / total counts. This is the “who is behind the
  DAU/recency numbers” view — backed by `.faust api stats players` (the §7 request, now delivered).
- **Rate-limit handling.** Faust 0.13 added a per-player anti-spam **`ratelimit`** deny code; Raphael now
  surfaces a friendly “the server is rate-limiting — try again in Ns” message instead of a generic refusal.
- **Note:** Faust 0.13 makes every feature **AdminOnly by default** server-side (admins grant pieces to
  players per server). No Raphael change — it already gates each feature on the handshake-advertised access,
  so non-admins simply see the relevant tabs/queries as admin-only until granted.
- Docs: `FAUST_API_REQUESTS.md` §7 marked delivered (only §3 open-world region and §5 map-marker visibility
  remain open); `FAUST_INTEGRATION_HANDOFF.md` mirror table + ApiVersion notes updated.

## 0.43.0 — Left-rail accordion (small-screen friendliness)

- **Accordion left rail (default ON).** Opening one tab group in the left rail — **Bloodcraft / Beelzebub /
  Kindred / Uriel / Faust / Settings & Help** — now automatically collapses the others, so the rail stays
  short and doesn't run off the bottom of the screen once a couple of groups are expanded. This was a
  request from players on smaller displays.
- **Override toggle.** Players who prefer several sections open at once can turn the accordion **off** —
  Settings → Size & Positioning → Primary UI → “Accordion left rail (collapse other groups when one opens).”
  With it off, the rail behaves as before (each group expands/collapses independently).

## 0.42.0 — Server Stats UX: filters, refresh, chart titles, tooltips + large-font fix

A polish pass over the Faust reporting UI from tester feedback, plus a couple of cross-cutting fixes.

- **Large-font layout fix.** At Large / X-Large UI text, button rows and table rows used **fixed** heights
  that the bigger buttons overflowed, so text overlapped the controls. The Faust row / cell / input / list
  helpers now scale their heights with the UI font size, so nothing collides at large text.
- **Server Stats — date-span filter + Refresh.** A new **“Days window”** field bounds the time-windowed
  views (Daily, By week, New players, Peak concurrency) from 1–90 days instead of fixed defaults, and a
  **Refresh data** button re-runs whichever view is showing with the current window (still cooldown-guarded).
  The metrics are no longer all-or-nothing.
- **Chart titles + spacing.** Every chart now has a **bold title above it** (so stacked charts aren't
  ambiguous), with leading space between charts so they don't sit flush against each other — especially the
  per-player charts in Player Info.
- **Acronym tooltips.** Hover any metric in **Population health / Peak concurrency** (DAU, WAU, MAU,
  stickiness, D1/D7/D30 retention, p95, …) for a plain-language explanation of what it means and how it's
  computed. The chart titles carry hover help too.
- **Secondary chat — “Notes to self” filter.** The view-only secondary chat window can now mirror **only
  your notes-to-self** (whispers to your own character), as a toggle independent of the Whisper channel — so
  you can keep a running self-note scratchpad in the second window without all the whisper traffic. (Game UI
  → chat settings → “Channels shown in the secondary window”.)
- **Filed for Faust** (`docs/FAUST_API_REQUESTS.md` §7): a server-wide **player-activity roster** endpoint
  (`stats players` — per-player active-today/active-this-week ✓, last-seen, sessions, playtime, days-idle).
  Once Faust ships it, Raphael will add a sortable player table with “active today” checkmarks beneath the
  Server Stats dashboards. Until then, per-player activity lives in the **Player Info** tab (one lookup at a
  time) and the aggregate views keep their per-bucket data tables.

## 0.41.0 — Faust 0.12 reporting: clans, population health, per-player trends

Consumes Faust **ApiVersion 11** (Faust 0.12.0) — a big reporting buildout for players (individually and
in aggregate) and clans. All new UI is api-gated, so it stays hidden with a "needs Faust 0.12+" note on
older servers.

- **New tab: Clans** (admin) — how the server splits between **clans and solo** players: a composition
  summary (clanned vs independent counts + %, online split, largest clan, average size) and a per-clan
  **roster table** (members, who's online, castles owned, leader). Backed by `.faust api clans`.
- **Server Stats — new aggregate "health" views** (api 11):
  - **Population health** — DAU / WAU / MAU, today's new vs returning, **stickiness** (DAU/MAU), and
    **D1 / D7 / D30 retention**.
  - **Player recency** — how many known players were seen in 24h / 7d / 30d vs **dormant** (>30d), as bars.
  - **Peak concurrency** — peak (and when), 95th-percentile, average, and the live online count, last 30d.
  - **By region** — online population + claimed-castle count per map region (bars + table).
  - **By day of week** now uses Faust's **authoritative** `weekdays` endpoint (true playtime-per-weekday)
    when available, instead of the client-derived approximation (which remains the fallback on Faust 0.10–0.11).
- **Player Info — richer individual reporting** (api 11):
  - **By day of week** — this player's playtime per UTC weekday (authoritative per-player histogram).
  - **Daily / weekly trend** — their playtime per day over the last 90 days, plus a re-bucketed weekly table.
  - **Days idle** — when a player is offline, how long since they were last seen, colour-coded as an
    at-risk cue (orange ≥14 days, red ≥30) so admins can spot who's drifting away.
- Wire layer: `FaustState` gained records/slots for clans, weekdays (server + per-player), pdaily,
  population, recency, peak, and regions; `FaustProtocolService` parses `[FAUST:clansummary|clan|weekdays|
  pdaily|population|recency|concsummary|region]` + the `clans`/`pdaily`/`regions` end trailers and reads
  `daysidle` off `[FAUST:player]`; `FaustClient` gained the request methods; the handshake now reads the
  `clans` feature token. Docs: `FAUST_API_REQUESTS.md` §6 marked delivered; `FAUST_INTEGRATION_HANDOFF.md`
  mirror table updated.

## 0.40.1 — Fix: map-probe crash + "needs Faust 0.10+" note

- **Crash fix (important):** the "Probe map icons" diagnostic added a UI sweep in 0.39.0
  (`Resources.FindObjectsOfTypeAll<RectTransform>()`) that iterated the entire loaded UI set on the main
  thread — on a busy scene this froze and **crashed the game**. Removed it entirely; the probe is back to
  the safe, fast entity-only dump. (The main-map overlay research is shelved; server-side markers remain the
  recommended path.)
- **Clarity:** Server Stats now shows an explicit note when the **server's** Faust is older than 0.10 —
  the activity charts (By hour / By day of week / By week / Daily / New players / Session lengths) require
  **Faust 0.10+ (ApiVersion 10)**, so on an older server they're hidden with an explanation (it shows the
  detected API version + plugin version) rather than silently missing. Playtime + Concurrency still work.
  - Note: if you don't see the activity/weekday/week views, update the **Faust plugin on your server** — the
    Raphael client is ready; it just gates those views on the server advertising API 10.

## 0.40.0 — Weekday & week-over-week activity views (Server Stats)

- **New: "By day of week"** — which weekday the server is busiest. Average players online and average
  playtime per Mon–Sun (UTC), with the busiest day called out and a table.
- **New: "By week"** — the week-over-week trend: average players/day and total playtime per ISO week
  (Monday-start, UTC), newest first in the table, with peak per week on hover.
- Both are derived **client-side** by re-bucketing Faust's existing `daily` series over a 90-day window —
  no Faust change needed. The Daily / By-day-of-week / By-week views now share one 90-day query, so
  switching between them is instant (and respects the anti-spam cooldown).
- **Filed for Faust** (`docs/FAUST_API_REQUESTS.md` §6): a direct `stats weekdays [<scope>]` histogram and a
  per-player daily series (`stats pdaily`/scoped `daily`) so the **individual-player** weekday + week
  trends can be charted in Player Info (server-wide already works via the derived views). Raphael will add
  the per-player charts and switch the server weekday view to the authoritative endpoint when Faust ships
  them.

## 0.39.0 — Query anti-spam cooldown

- **New: query cooldown (anti-spam).** A fast double-click or held Refresh no longer fires a second Faust
  server request — each query type has a minimum gap (default **5 s**, `FaustQueryCooldownSeconds`, 0 to
  disable) and only one query runs at a time. Blocked clicks show a transient "wait Ns" note at the top of
  the tab and send **nothing** to the server. Protects the server when many players query at once.
- **Positions region:** confirmed via the wire trace that Raphael displays region correctly — a player on a
  territory shows `region=Farbane_Woods`, but Faust sends `region=-` for a player in the **open world**
  (`tindex=-1`). Resolving the open-world world-map region is a **Faust-side** item (filed for circle-back).
- **Main-map marker research:** the `mapprobe` diagnostic now also sweeps the loaded UI for the full-map
  (M-key) container + transform — the data needed to evaluate a client-side marker overlay on the main map.
  (The clean path remains server-side, `FAUST_API_REQUESTS.md` §5.)

## 0.38.0 — Faust 0.11 admin data management + region sentinel fix

- **New: Data management** in the Faust → **Admin: Control** tab (Faust 0.11+). Inspect, prune, and reset
  Faust's server-scoped data (the session log behind playtime/charts, plus unlock-progress and usage state)
  — which lives in `BepInEx/config/Faust/` and **survives a V Rising world wipe**, so it's reset explicitly:
  - **Data status** — footprint readout (record counts, oldest record, on-disk size, namespace, retention).
  - **Prune activity** — drop sessions/concurrency older than N days (config retention untouched).
  - **Reset / wipe** — `activity` (playtime/charts), `unlocks` (V-blood progression — the usual fresh-world
    reset), `usage` (cost/cooldown locks), or `all`, behind a **Preview-then-Confirm** flow (Preview erases
    nothing; the separate "Wipe — CONFIRM" sends the `confirm` token). These are admin chat commands; Faust
    replies in chat. No ApiVersion change (stays 10).
- **Fix: region "no region" sentinel.** Faust canonicalized the empty-region token to `-` (0.10.0+); Raphael's
  All Plots / Open Plots / Decay Watch / Castle Info still ran castle & plot regions through `GetText`, so a
  `region=-` would have shown a literal "-". They now fold it via `CleanRegion` like positions already did,
  so no-region reads as the muted "(outside map)" placeholder again.
- Help reference + integration mirror doc updated.

## 0.37.0 — Per-player activity charts

- **Per-player activity charts** (Player Info, Faust 0.10+): after looking a player up, **Activity by hour**
  (their playtime per UTC hour — when they're active) and **Session lengths** (short bursts vs long
  sittings) draw charts for *that* player, using the steamId-scoped `stats hours/sessions`. Player Info also
  now derives **Avg session** (playtime ÷ sessions), and labels "busiest hour (most logins)" / "first seen
  (by Faust)" so the numbers aren't misread. Per-player charts use their own state slots so they never
  collide with the server-wide Server Stats charts.
- **Native map markers — re-scoped to the server (the safe path).** The full `MapIcon_Player` archetype dump
  confirmed it's a **server-authoritative networked entity** (`NetworkId` + `NetworkSnapshot` + …), so
  faking one client-side would risk crashing the client's networking. Filed `docs/FAUST_API_REQUESTS.md` §5:
  a server-side Faust admin toggle that attaches map icons to online players, which the native map then
  renders for admins with **zero** custom client map code. The client-side proxy approach is shelved as
  unsafe; the read-only `mapprobe` diagnostic stays (behind Diagnostics).

## 0.36.1 — Activity-chart labeling (per the Faust contract caveats)

The Faust 0.10 contract asks the client to label three things so admins don't misread the charts — now
surfaced in the Server Stats UI:
- **UTC:** every hour/day bucket is UTC (the hours chart, its peak-hour callout, and the daily chart now
  say so explicitly), so a "14:00 peak" isn't mistaken for local time.
- **"New players" = first seen by Faust:** the chart caption + button tooltip now make clear it counts
  *first-seen-by-Faust-since-install*, not account creation — returning veterans register as "new" for a
  while after install, and it's only reliable with session retention off.
- A short caveat note was added under the Server Stats heading covering both.
No wire/behavior change — labeling only.

## 0.36.0 — Faust 0.10 endpoints: Decay Watch + activity-analytics charts

Consumes Faust **ApiVersion 9 & 10** (verified against `BCH_INTEGRATION_CONTRACT.md`). All new UI is
ApiVersion-gated, so it stays hidden / shows a "needs newer Faust" note on older servers.

- **New tab: Decay Watch** (admin) — claimed castles ordered **soonest-to-decay first**, pairing the decay
  timer (color-coded: red < 1 day, orange < 3 days) with how long the owner's been offline. The housekeeping
  view for spotting abandoned/at-risk bases. Backed by Faust's `.faust api decay` (api ≥9).
- **New: activity-analytics charts in Server Stats** (api ≥10) — the controls gained four buttons, each
  drawing its own chart:
  - **By hour of day** — 24-bar histogram of accumulated playtime per UTC hour (when the server is busiest).
  - **Daily activity** — distinct players per day (last 14d) as bars; hover for that day's play-minutes; full table below.
  - **New players** — players first seen per day (last 30d) for growth/retention.
  - **Session lengths** — distribution across `<15m / 15–60m / 1–3h / 3h+` with percentages.
- The Server Stats card is now view-driven (Playtime · Concurrency · Hours · Daily · New players · Sessions),
  each with its matching graph + table. New generic vertical-bar-chart helper drives the time-series charts.
- Wire layer: `FaustState` gained slots/records for decay + the four analytics; `FaustProtocolService` parses
  `[FAUST:hours|daily|newplayers|sessions]` + `[FAUST:end] cmd=decay|daily|newplayers`; `FaustClient` gained
  the request methods; handshake now reads the `decaywatch` feature token. `docs/FAUST_API_REQUESTS.md` §4 is
  marked delivered; mirror table updated in `docs/FAUST_INTEGRATION_HANDOFF.md`.

## 0.35.0 — Playtime bar chart + map-probe groundwork

- **New: ranked playtime bar chart** (Server Stats). Run the **Playtime leaderboard** and the "Activity
  graph" card now draws a horizontal bar per player (length ∝ total minutes, top 15) for an at-a-glance
  collective view — the exact-numbers table still lists everyone. The card adapts: Playtime → bar chart,
  Concurrency → population graph.
- **Richer analytics are data-bound:** filed `docs/FAUST_API_REQUESTS.md` §4 — proposed Faust stats
  endpoints (hour-of-day histogram, daily-active-users series, new-players series, session-length
  distribution). Raphael will add a chart per shape as Faust ships them.
- **Map-marker groundwork:** the probe now also dumps the full component archetype of a real `MapIcon_Player`
  entity + the local character (`EntityManager.Debug.GetEntityInfo`) — the template needed to build native
  in-game player markers safely. Found: the icon system is **attach-based** (`MapIconData` has render
  settings but no position/sprite field; icons hang off a target via `AttachMapIconsToEntity`), which is why
  free-floating markers need this template before they can be created without risking the game's map job.
- The dev "Probe map icons" button now lives under **Faust → Settings → Diagnostics ON** (decluttered for
  the public build) and no longer claims you must open the map first.

## 0.34.0 — Standalone map overlay removed

- **Removed the standalone player-positions map overlay** (the draggable mini-map from 0.32.0). It wasn't
  the right shape for the feature — player positions belong on the **actual in-game map**. The
  query-based, map-rendered version is in progress (see the probe diagnostic, still present in Player
  Positions).
- **Probe correction:** the "Probe map icons (diag)" button does **not** need the in-game map open — the
  icon entities live in the world regardless. Just click it any time in-game and send the `[Faust][mapprobe]`
  log lines.

## 0.33.1 — Map-icon probe (groundwork for native-map player markers)

- **Dev diagnostic** (Faust → Player Positions → "Probe map icons (diag)"): read-only — logs the game's
  map-icon component setup (`MapIconData` / `PlayerMapIcon` / `AttachMapIconsToEntity` populations + a
  sample of their prefab GUIDs/names) to `LogOutput.log` under `[Faust][mapprobe]`. Creates/modifies
  nothing. This is step 1 of putting player markers on the **native in-game map** (proxy-entity approach):
  the log tells me which icon prefab + component template to replicate. Open the in-game map, click the
  button, and send back the `[Faust][mapprobe]` lines.

## 0.32.0 — Faust player-positions map overlay

- **New: Player map overlay** (Faust → Player Positions → "Show map overlay") — a draggable, resizable
  mini-map that plots every **online** player as a dot on a north-up top-down map. Other players come from
  the `positions` query (`FaustState.Positions`); **your own** dot is drawn from your live client position
  (gold) so it's always accurate, even before a query. Hover a dot for name, region, territory, and
  coordinates. Header has a **Refresh** (re-query) button and a **zoom cycler**: *Auto-fit* (frames all
  online players) or fixed origin-centred extents (±1500 / ±2560 / ±4000 m). Fully integrated with the
  overlay system (Lock-overlays, transparency, restore-on-login). It honors the same admin gating as the
  Positions tab — non-admins just won't get position data to plot.
  - Note: the game's true world bounds aren't exposed to the client, so the fixed zoom levels are
    best-effort — use whichever frames your server's map, or leave it on Auto-fit. Tell me the extent that
    lines up and I'll set it as the default.

## 0.31.0 — Faust testing feedback (display)

- **All Plots:** the open-plot owner now reads a plain **`(open)`** instead of the code-looking `<open>`.
- **Region display:** an empty region or Faust's literal `none` token (sent for out-of-bounds / unmapped
  territories like the admin island) now renders a muted **`(outside map)`** in Castle Info, Open Plots, and
  All Plots — no more bare "none". The real region *name* for those plots must come from Faust; filed as a
  server-side request (`docs/FAUST_API_REQUESTS.md` §3).
- **Decay & duration display option** (Faust → Settings): new cycler — **Auto** (default; the two largest
  units, e.g. `3w 2d` or `6h 30m`, so weeks-long timers don't show as a huge hour count), Hours & minutes
  (legacy), Days/hours/minutes, or Weeks/days/hours/minutes. Applies to castle decay timers.

Raphael, Lord of Wisdom began life as **BloodCraftHub** (v0.1 → v0.30). This is the **condensed** history of
that work, grouped by milestone. The full per-patch BloodCraftHub changelog (100+ entries) is preserved in
this repo's git history and in the legacy repository at https://github.com/KDavidP1987/BloodCraftHub.

## Unreleased — Faust integration
- New **FAUST** tab group (handshake-gated on `.faust api version`, hidden until a Faust server is
  detected — same model as Beelzebub/Uriel), client UI for the server-side investigation/information mod.
- **Player tabs:** **Castle Info** (owner/region/size/decay for here/nearest/index), **Open Plots** (free
  territories), **Castle Resources** (enemy-castle container totals — raid intel), **Player Info**
  (playtime/frequency/busiest-hour; self always, others gated), **Player Positions** (online players'
  coords + territory, sortable by name/territory), and **Server Stats** (playtime leaderboard + a
  concurrency **sparkline graph** with hover detail).
- **Admin tabs** (server-enforced, admin-gated UI): **Control** (block/unblock with auto-reopen, daily
  schedule, status) and **Access** (grant/revoke a feature, show a player's unlocks).
- **Open Plots**: sort (largest/smallest first, or region A–Z) and a region filter (cycles All → each
  region found in the results).
- **Player Positions**: added a **Region** column — live on Faust 0.8 (ApiVersion 8), which adds
  `region=` to the position wire (open world shows "—").
- **All Plots** tab (new): the full server castle map — every territory (claimed + open) with owner,
  region, size, state, and decay in one sortable (size/region/owner/state) and region-filterable table,
  with a "claimed only" toggle. Backed by Faust 0.8's `.faust api castles` endpoint (`allcastles`
  feature, AdminOnly); on an older Faust it shows a "needs 0.8+" note. Gated on
  `FaustState.SupportsAllCastles` (ApiVersion ≥ 8).
- All lists are proper **columnar tables**; single-record results use aligned label/value rows. Each
  query shows the server-resolved access + price from the handshake and surfaces deny reasons (cost /
  cooldown / admin-only / locked / schedule / …) on the result line.
- Plus a **Settings** tab (diagnostics toggle + connection readout) and **Faust Quick Start / Faust
  Help** guides.
- **Friendly item names:** raw item/prefab GUIDs are resolved to readable names (via the client
  `PrefabNameResolver`) wherever they appear — the Castle Resources table (e.g. `Iron Ingot` instead of
  `Item_Ingredient_Mineral_IronIngot`/a bare GUID, with the GUID kept in its own column), the per-feature
  cost hints, and the "you can't afford this" error (`costs Iron Ingot ×100`).
- New client plumbing mirroring the Uriel integration: `Services/Faust/` (`FaustProtocolService`,
  `FaustState`, `FaustClient`, `FaustWireParser`, `FaustDiag`, `FaustNames`), a `[FAUST:` branch in
  `ClientChatPatch`, a per-frame detection/timeout tick, relog reset, and `FaustAvailability` /
  `FaustDiagnostics` config keys. See `docs/FAUST_INTEGRATION_HANDOFF.md` (mirrors Faust's
  `BCH_INTEGRATION_CONTRACT.md`).

## 0.30.0 — Renamed: BloodCraftHub → Raphael, Lord of Wisdom
- Rebranded from **BloodCraftHub** to **Raphael, Lord of Wisdom** — the mod long outgrew "Bloodcraft
  companion" (it serves Bloodcraft, Beelzebub, Uriel, KindredCommands, KindredLogistics, and a standalone
  chat window), and the new name ends the constant mix-up with the separate *Bloodcraft* server mod. New
  Thunderstore package `kdpen/Raphael`, new plugin GUID `kdpen.Raphael`. **No feature changes** versus the
  final BloodCraftHub build — same UI, same behavior.

## 0.29.x — Whisper overhaul · Uriel object-spawner overlay · server-switch fixes
- **Whispers:** message anyone connected to the server (incl. a note to yourself); sent whispers show the
  recipient (channel-column "[→Name]" or name-column "→Name" toggle); "Note to self" display; a privacy fix
  so an unresolved recipient can't leak into Local chat; Whispers tab restructured (conversation sub-tabs on
  top, recipient picker + "+ Whisper…" + input on one bottom line).
- **Uriel:** draggable object-spawner overlay (category cycler, page selector, name/ID search, per-row
  Spawn / Despawn / Rotate).
- **Fixes:** server-switch re-detection of mod tab groups (incl. Beelzebub tab content); overlays-behind-menus
  extended to Social/Spellbook/Map; combined-overlay stat-name abbreviation; Shift recast/charge cooldown;
  and closing the main panel no longer leaves your character stuck auto-attacking.

## 0.28.x — Overlay-visibility controls
- The hide-all overlay toggle can auto-reappear on a timer, optionally hide the launcher buttons too, and
  keep the native chat hidden while overlays are hidden.

## 0.26 – 0.27 — Uriel integration
- **URIEL** tab group (handshake-gated): storage sharing, nearby public-storage detection (client-side) +
  overlay, object spawning with a full catalog browser, build-mode move/rotate/remove hotkeys,
  durability/respawn options, prisons & stairs, and built-out admin tools.

## 0.21 – 0.25 — Beelzebub catch-up · typing lock · chat & admin polish
- Per-channel chat colors with disk-cached presets; a keyboard/cursor lock while panels are open; a second
  view-only chat window; the loadout editor + an admin recovery toolkit; plus a long run of friend-test fixes.

## 0.18 – 0.20 — Beelzebub integration
- **BEELZEBUB** tab group: ability capture / Bestiary, loadouts, transforms (browser + overlay), hotkeys,
  and admin ability/config tools; protocol catch-up through Beelzebub's ApiVersion 22 era.

## 0.17.x — Chat overhaul
- Standalone tabbed chat window; whisper anyone via the full (non-culled) online roster; double-click a name
  to whisper; tabular columns; per-channel tab filters; in-chat `\`/`/` commands.

## 0.16.x — Input & overlays
- Input-suppression while typing; recipe browser; the SHIFT-spell icon; overlays-drop-behind-menus; panel
  resize; the Quick Actions overlay; and a fullscreen-mode fix.

## 0.15.x — First public release
- First Thunderstore + GitHub release: Bloodcraft / KindredCommands / KindredLogistics command UI, live HUD
  overlays (XP, legacy, expertise, familiar, professions, quests), and Eclipse-mod coexistence.

## 0.1 – 0.14 — Initial build-out
- Ported the UniverseLib + CustomLib + ModernLib UI stack from **BloodCraftUI** (panthernet) and the signed
  Eclipse protocol from **Eclipse** (zfolmt); built the main panel, forms, and overlays, and the Bloodcraft
  structured + chat-regex pipelines.
