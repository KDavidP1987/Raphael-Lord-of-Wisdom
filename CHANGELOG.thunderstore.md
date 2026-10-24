# Changelog — Raphael, Lord of Wisdom

*Player-facing highlights, newest first. Full per-patch detail lives in
[CHANGELOG.md](https://github.com/KDavidP1987/Raphael-Lord-of-Wisdom/blob/main/CHANGELOG.md).
Raphael was formerly **BloodCraftHub** (v0.1–v0.30).*

**Current: v0.73.0** (pre-1.0 beta).

---

## 0.74.0 — In progress (not released)

Works with Nyarlathotep 0.15.0 (0.14.0 still works), Bloodcraft 1.13.26 and Uriel 0.22.0.

- Nyarlathotep 0.15.0 horde pools: pool entries with a min-max count, the server's library pools in the pool picker
  and a **Pools library** card, and **Castles may be entered** on apocalypse, infestation and horde march events.
- Every Nyarlathotep setting explains itself: hover for what it does, its default and its range, and open the folded
  **How this works** section on each window.
- New **Reference: Units & Objects** (Settings and Help): every unit and world object in the game, searchable and
  filterable, also as a floating panel. **Browse…** beside unit and object fields in the Nyarlathotep, Beelzebub and
  Uriel forms fills the field for you.
- Admins: the **Reset Bloodcraft progress** report no longer overlaps itself. The summary, buttons, step log and notes
  each get their own space, and the log sits in its own box.
- **Start a new reset** (formerly New reset) sits under the summary and returns to the form with the player name
  cleared, ready for the next player.

## 0.73.0 — Complete in-game guides and a Bloodcraft progress reset

Works with Bloodcraft 1.13.26, Nyarlathotep 0.14.0 and Uriel 0.22.0.

- Every mod now has one complete guide under Settings and Help (Bloodcraft, Beelzebub, Uriel, Faust, Nyarlathotep,
  Kindred): a quick start, how it works, every player and admin command, server settings with defaults, and
  troubleshooting. Wrong commands in the old guides are fixed.
- Admins: **Reset Bloodcraft progress** (Bloodcraft → Admin) resets a player's prestige, weapon expertise, blood
  legacies, professions and level, one checked command at a time, with pause, retry, skip and stop. Works for offline
  players.
- Nyarlathotep 0.14.0 undead hordes: the event editor has forms for **Infestation** and **Horde march** events, and
  a Destination card for spawn waves. Unit counts go up to 500, and templates get an Undead hordes group.
- Uriel 0.22.0: **Admin: Controls** (formerly Admin: Config) shows which Uriel features are switched on and has
  **Stop all events**, **Remove all units**, **Remove all objects** and **Restore vanilla**, each with Arm + Confirm.
  Features the server has switched off are greyed with the reason on every Uriel tab, never hidden.
- Experimental, off by default: an **Emberglass bridge** for servers that send Bloodcraft data over Emberglass, for
  players who also have the Emberglass client installed.
- V Rising Basics tidied up, now covering Nyarlathotep and Uriel's Castle Events.
- Fixed: Kindred admin buttons that sent commands KindredCommands doesn't have (staff, castle hearts, plots, prisoner
  settings); buttons for commands KindredCommands disables are removed. Beelzebub Phase now cycles, and the wipe warning
  and slot list are corrected. Bloodcraft Change class form, the right shard name and prestige reset values. The
  Nyarlathotep editor background stays gray.

## 0.72.0 — Nyarlathotep wave variety and a friendlier event editor

Works with Nyarlathotep 0.13.0-rc.3.

- The event editor is now a form: values are filled in from the server; you pick numbers with - / + and presets within
  the allowed range, and choices with buttons. Changed fields turn gold, saved ones get a ✓, and refused ones show
  why. **Save all changes** and **Undo** sit at the top.
- New in rc.3: a different unit list, bosses and start rule for each wave; named unit pools; fixed and rolled bosses;
  pursuit.
- Templates are grouped, with the new Timed waves and Onslaught sections, and a **Turn Spawns on** button when the
  Spawns pillar is off.
- Fixed: left-rail groups opening by themselves after a text-size change, a late mod detection or a Nyarlathotep
  setting change.

## 0.71.0 — Nyarlathotep event editor and Admin: Units

- **Edit…** on a Nyarlathotep event opens its settings under it, filled in from the server, with a Set per field.
- Every unit field has a searchable list of about 430 game units (also used by Uriel's pickers).
- New **Admin: Units** tab: the event units around you with level, health and position, unit counts per event, and
  test spawn.
- Fixed: "[denied]" and "Command not found" lines on login, other mods' menu groups popping open, and the event list
  staying on "ending" after an event finished.

## 0.70.0 — Uriel Castle Events (alpha)

Needs Uriel 0.21.0 with Castle Events turned on by the server admin.

- **Castle Events** tab: drop mob and boss markers as you walk your castle, bind doors to groups, and start,
  stop and time runs. A live readout shows the clock, groups cleared, bosses left and time left, with on-screen
  notices as the run goes.
- **Event Library** tab: build squads, floor templates and templates without typing commands.
- **Admin: Units** tab: spawn, kill, despawn, clear and purge event units.

## 0.69.0 — Chat fixes and Nyarlathotep 0.13 support

**Fixed**

- Copied chat keeps text in angle brackets. Roleplay lines like `<telepathy>` are no longer cut.
- One message's italics no longer carry into the next.
- `/w Name` always whispers the player you typed, whatever the dropdown shows.

**New (Nyarlathotep 0.13)**

- Live Events shows which regions each event is in, plus a Regions list.
- On-screen notices when events begin or end, when a wave is coming, and when you log in. Switch them off in
  Nyarlathotep → Settings.
- Admins see each action's result on the tab, and get Unstick, New / Copy / Delete, a field picker and Units near me.
- New admin tabs: **Templates** and **Pillars**.
- Purge is one confirmed click.

## 0.68.1 — Beelzebub 0.137.3-0.137.5 support

- Mounted loadout now binds R / C / Ultimate (slots 5, 6, 7) on Beelzebub 0.137.3+, matching the server.
  Older servers keep Shift / C / Ultimate.
- Clear bar and Clear set refresh your slot list afterwards, since on Beelzebub 0.137.4+ they can dismount you or end
  a transform.

## 0.68.0 — Beelzebub 0.137 support

- **Reset bar** (Loadout tab) replaces "Unstick bar" with one confirmed click. Spells leave the bar immediately, with
  no weapon swap, and spellbook (J) spells are kept. Older Beelzebub servers keep "Unstick bar".
- Admin Recovery buttons now go in order: **Diagnose bar** (read-only) -> **Reset player's bar** -> **Purge bar** ->
  **Respawn**. The old Rebuild slots / Clear slot mods / Rebuild bar buttons are hidden on Beelzebub 0.137+.
- Granted abilities appear on the bar immediately on Beelzebub 0.137.1+, so Raphael no longer sends an extra refresh.
- Your ability slots refresh when a transform ends.
- The config view now describes the server setting `Forms_AutoFillFromCaptures`.

## 0.67.4 — Nyarlathotep beta banner

- Nyarlathotep tabs now carry the beta banner with a **Give feedback** link to The Shadow Realm Discord.

## 0.67.3 — Chat window spacing

- Chat messages no longer overlap the tab buttons at the top of the chat window.

## 0.67.2 — Chat & Diagnostics

- Replies and errors for commands you send always show in chat now (e.g. "command not found"). Only Raphael's own
  background traffic is hidden.
- New **Settings and Help → Chat & Diagnostics** tab: one place for what Raphael hides from chat and what it logs, for
  Raphael, Bloodcraft, Beelzebub, Uriel, Faust and Nyarlathotep. Presets: Quiet, Normal, Show everything.
- Each server mod can now show its machine-readable lines in chat, useful when testing.

## 0.67.1 — Faster opening

- The main panel now opens right away. Tabs show "Loading..." and fill in without freezing the game.
- Mod groups that are still connecting show "(connecting...)" instead of "(unavailable)".
- A mod connecting mid-session only refreshes its own tabs, not the whole panel.
- The world map image loads when you first view a map, and a smaller cached copy makes later loads faster.

## 0.67.0 — Nyarlathotep support

- **New:** Nyarlathotep tabs (shown only on servers running it). Players: live events with wave countdowns,
  leaderboards with Share, and your own stats with hide/show.
- **Admin:** event control (start/stop/enable/disable/reload, field editor, test spawn), defended zones,
  announcements, and the kill switch.
- Beelzebub, Uriel and Faust tabs now carry a small beta banner with a **Give feedback** link to The Shadow
  Realm Discord.

## 0.66.0 — Beelzebub v0.132–v0.135 support

- **New:** incompatibility locks. Abilities that can't share your bar right now get a **[Locked]** badge. Admins
  get a lock editor.
- **New:** cooldowns shown on ability cards now match what the server applies. Charge-based abilities show
  "Uses charges".
- **New:** ability cards show where an ability can be used ("Usable in … except …", "Not with …") and its
  current damage multiplier.
- **Admin:** new tuning fields (max stacks, projectile count, knockback, lifetime, cast time), an Inspect
  button, and Reseed ability rules.

## 0.65.6 — Long-session slowdown fixed, plus a Refresh button

- **Fixed:** the game getting slower the longer you played, until you relogged.
- **New: Refresh** button (bottom-right of the main panel, also on the Connection tab). Clears chat history and
  cached UI leftovers without leaving the world. Settings are kept.
- **Fixed:** a flood of messages containing `<` (like `<3`) could blank the chat box.
- **Fixed:** several memory leaks, including in the Faust world map.
- **Docs:** new screenshots on this page.

## 0.65.5 — Safer chat length cap, plus Uriel v0.20.0 support

- **Fixed:** very long messages (480+ characters) could flood chat and freeze it for players without Raphael.
  Chat input is now capped at **470 characters**. *(Thanks to Howl.)*
- **New (Uriel v0.20.0):** **Raise / Lower nearest** buttons on Object Spawning to fix floating or sunken
  objects, plus an admin **Height offset** field.

## 0.65.4 — Chat fixes

- **Fixed:** chat input stayed off-center after pasting a long message. *(Thanks to Kruczarz.)*
- **Fixed:** one long message could appear many times. *(Thanks to Howl and Strie.)*

## 0.65.3 — Chat polish, plus optional auto-hide

- **Fixed:** chat tabs randomly turning italic. *(Thanks to Howl.)*
- **Changed:** typed chat text is vertically centered. *(Thanks to Kruczarz.)*
- **New:** optional chat auto-hide (off by default). It fades out when idle and returns on new messages or
  hover. Turn it on under Game UI. *(Requested by Kruczarz.)*

---

## 0.65.2 — Fixed the last localized familiars with wrong HP/quests

- **Fixed: a few specific familiars still showed HP as 0 and garbled quests on non-English clients.**
  A follow-up to the 0.65.1 fix: the field re-alignment used the quest "is V-Blood?" flags as landmarks
  but only recognized the capitalized `True`/`False`; Bloodcraft sends a lowercase `false` for non-kill
  (gather/craft) quests, so the re-alignment gave up whenever such a quest was active. It now reads either
  casing, so every comma-named familiar (e.g. Polish "Domina, Tancerka Ostrzy") displays correctly
  regardless of quest type. *(Thanks to Kruczarz and Luxanna.)*

---

## 0.65.1 — Fixed quests and familiar stats on non-English clients

- **Fixed: on localized (non-English) clients, quests showed only numbers and a summoned familiar's
  HP/PP/SP read 0.** Both traced to one cause — in some languages a familiar or quest-target name contains
  a comma (e.g. Polish "Alfa, Biały Wilk"), which shifted the live data fields Bloodcraft streams to
  Raphael. Raphael now re-aligns the fields, so quests and familiar stats read correctly in any client
  language. English clients were never affected and are unchanged. The fix is language-agnostic — it covers
  every localized client (Polish, Latin American Spanish, German, Russian, Turkish, …), not just Polish.
  *(Thanks to Kruczarz and Chronost.)*
- **Fixed: 9 more V-Bloods now show on the V-Blood tab on non-English clients** (Elena, Willfred, Sir Erwin,
  Gaius, Stavros, Dantos, Lucile, Jakira, Megara) — they classified in English but were dropped in other
  languages. The name table is now 64 entries, all cross-verified.
- **Fixed: a chat channel (Local first) could still go blank after heavy use.** The newest-lines cap is now
  by visible-glyph budget instead of line count, so long lines can't push the text box past TextMeshPro's
  mesh limit. Full history is kept for scrollback and Copy. *(Thanks to Howl.)*
- **New: a "Refresh" button on the tabbed chat window** — forces the current tab to redraw on demand.
- **New (experimental, opt-in): "Stronger hide if the game chat still flickers"** chat option — hides the
  entire native chat window every frame if you still see it flicker behind Raphael's chat. Off by default.

## 0.65.0 — In-built game guide + extended-chat controls + user-reported bug fixes

- **New "V Rising Basics" guide tab** (Settings and Help): a plain-language guide to the base game — blood,
  Gear Level, V Bloods, castles, hazards — plus a **"How the mods change the game"** rundown that links to each
  mod's own Help tab. A one-stop primer for new players, beside the external-links Game Guide.
- **Extended chat retention controls** (Game UI settings): a master switch + **per-channel** toggles (Global /
  Local / Clan / System / Whispers / Other) for what the chat window keeps — turn off noisy **System** floods
  while keeping conversation. All default ON (no change unless you opt in).
- **Fix — chat going blank after heavy use:** the chat (All tab first) no longer stops showing new messages on
  busy servers; full history is still kept for scrollback and Copy. *(Howl, Chronost)*
- **Fix — native chat flicker:** the game's native chat no longer flickers behind the Raphael chat while you
  type. *(Howl)*
- **Fix — casting breaking after UI toggle:** opening+closing the UI with the on-screen button no longer locks
  out spells / power / emote wheels until you swing a weapon. *(Mardina)*
- **Fix — V-Blood tab on non-English clients:** regular (non-Primal) V-Bloods now show up regardless of your
  game language; also corrected one mislabeled Primal. *(Kruczarz)*
- **Fix — Eclipse chat spam on relog:** the background handshake no longer leaks into public chat / over players'
  heads on servers without Bloodcraft. *(Howl)*
- **Fix — Exo prestige wording:** clarified that each Exo prestige resets your *character* level to 0 (only the
  prestige tier carries over), and dropped the server-specific shard reward from the description.
- **Fix — familiar Quick Spawn overlay:** button text no longer disappears at larger overlay font scales (the
  row now grows and the label auto-shrinks to fit).

## 0.64.0 — Live “Players now” map + redesigned heat palettes

- **Player Positions** map gained a **View** toggle: **Heat map** ↔ **Players now (live)** — plots every online
  player on the calibrated map with their name (and hover detail).
- **Redesigned heat palettes** with strong cold→hot variation that reads on the map: **Magma**, **Ice→Fire**,
  **Viridis** (cycle with **Colors**).

## 0.63.1 — Dropdown fixes + Faust-tab cleanup

- **Online-player / castle dropdowns now populate for admins** (the auto-load was wrongly blocked); every picker
  also got an inline **Load** button.
- **Boss tracker** no longer leaves a tracked boss stuck under “Look up one boss”.
- **World Map:** fixed help text overflowing; tidier filters (advanced GUID filters + map-underlay setup are now
  collapsible, so the table sits right under the map).
- **Player Positions:** “Show players on map” moved to the bottom + collapsed; removed the no-op **Scale** button.

## 0.63.0 — Pick from dropdowns instead of typing

- **Online-player dropdowns** anywhere you enter a player (Player Info, heat map, Admin → Player access).
- **Boss lookup** and **castle index** are now dropdowns too (castles show `#index · region · (x,z) · owner`);
  the **boss tracker overlay** now shows live **coordinates**.
- **World Map** Category / Type are dropdowns and the Category **pre-filters the scan server-side** (units vs
  nodes) so it pulls less.
- **Heat map** activity dots are **brighter** on the map, plus a **Map image opacity** slider on Player Positions.
- **Boss tracker:** removed the redundant management card — track/untrack from the board's **★** buttons.

## 0.62.0 — Heat map: time windows

- The player-position **heat map** now has a **When** toggle (All-time / Today / This week / This month). Needs
  Faust 0.16.4+; older servers show the all-time map as before.

## 0.61.0 — Catalog filter, map-friendly heat colors, lighter boss tracker

- **World Map filter** is now driven by an **editable catalog** (`config/Raphael/worldscan_categories.txt`), so you
  can pick a Category / Type **before** scanning, with clean NPC factions + resource types.
- **Heat map** gained **Magenta / Cyan / White-hot** color schemes that read on top of the world-map image (opacity
  now scales with intensity so the map shows through).
- **Boss tracker** auto-refresh now updates **only your tracked bosses** (per-boss lookups), not the whole board.

## 0.60.1 — World Map filter fixes + admin tools moved

- Rebuilt the **Category / Type** filter against real resource-node names, so it now groups cleanly (Plant Fiber,
  Copper, Pine, Stone, Gloomrot Tech…) and actually narrows the map/table.
- Moved the world-scan **admin** tools (whitelist, prefab lookup) to **Faust → Admin: Control**.
- “Truncated” notice now explains the **server's** result cap and how to raise it (`worldscanmaxresults`).

## 0.60.0 — World Map: search & filter by category / type

- **New cascading Category → Type filter** on the World Map: after a scan, narrow by category (NPC factions, or
  resource families like Ore / Stone / Wood / Plants / Gems) and then a specific type (e.g. Ore → Copper). Built
  automatically from the scan results — no PrefabGUIDs to type — and filters the table + map instantly.
- *(Resource nodes only appear if whitelisted on the server — use Seed defaults.)*

## 0.59.2 — Pre-calibrated map defaults

- The **World Map / Heat Map** underlay now ships pre-aligned to the Vardoran map, so it's close to correct out of
  the box (fine-tune with the calibration tool). Faust 0.16.3's boss + world-scan fixes are server-side — no
  Raphael change.

## 0.59.1 — Faust 0.16.2: bigger world scans

- **World Map** scans can return far more rows (Faust raised its cap to 10000 / unlimited). Added the
  `worldscanmaxresults` setting to the config editor + a client safety cap so huge scans stay responsive.
- **Boss board** roaming-boss locations: no Raphael change needed (server-side fix in Faust 0.16.2).

## 0.59.0 — World map fills the panel + live overlay calibration

- The Faust **World Map / Heat Map** now fills the panel width, and the map image and coordinate dots are decoupled.
- New **calibration tool** (World Map → Map underlay): move and stretch the dot overlay over the map until it lines
  up, then **Log calibration** to print the values to the console. Adjusts the coordinate overlay only, not the map.

## 0.58.6 — Readability overhaul

- Rewrote this page and the README to be shorter and easier to scan, with collapsible sections. No code changes.

## 0.54–0.58 — Faust 0.16: boss board, leaderboards & world map

The Faust 0.16 (API 18) integration, refined across several patches. Needs **Faust 0.16+**; older Faust degrades gracefully.

- **Boss Status** — a server-wide V Blood board (live location / region / HP / level, plus defeated), friendly names, single-boss lookup, and a movable **Boss Tracker overlay** (up to 3 bosses, optional ~5s refresh).
- **Leaderboards** — top killers and most-defeated V Bloods, over today / this week / all-time.
- **World Map** — a filterable, server-scanned map of NPC units (with blood type / quality) and resource nodes, shown as a table **and** an X/Z map you can lay over the **actual V Rising world map** (capture it from your own game, then calibrate).
- **Live config editor** — change a feature's cost / cooldown / use-limit / proximity / access / PvP / unlock at runtime, no `.cfg` edit or restart.
- Plus the matching fixes along the way (boss coordinates, map rendering, config-set command syntax).

## 0.51–0.53 — Familiar Quick Spawn overlay + compliance

- **Quick Spawn overlay** — up to 5 one-click familiar summons (by name, regardless of box) with Dismiss / Recall / Unbind; full overlay controls (opacity, text-size, transparency, lock).
- **0.51.0** — a compliance pass aligning Raphael with the V Rising modding guidelines (no feature changes).

## 0.47–0.50 — Faust analytics, charts & chat scrolling

- **Faust through API 17** — a player-position **heat map**, region fill-% over time, new-vs-returning bars, a session timeline, an active-days grid, and castle world coordinates.
- More readable charts (text that scales with UI size, selectable color themes), a **50–400% text-size slider**, settings grouped into titled cards, and clickable chat scroll arrows.

## 0.31–0.46 — Faust integration (server investigation & analytics)

- New **FAUST** tab group: Castle Info / Open Plots / All Plots / Decay Watch / Castle Resources, Player Info, Player Positions, Clans, and **Server Stats** dashboards (playtime, concurrency, DAU/WAU/MAU + retention, population by region). All version-gated to your server's Faust.

## 0.30.0 — Renamed: BloodCraftHub → Raphael, Lord of Wisdom

- Rebranded to reflect that it serves Bloodcraft, Beelzebub, Uriel, and the Kindred mods (plus a standalone chat window) — and to end the mix-up with the separate *Bloodcraft* **server** mod. New package + plugin ID; no feature changes.

<details>
<summary><b>Earlier history (v0.1–v0.29, as BloodCraftHub)</b></summary>

- **0.26–0.29** — Uriel integration (storage sharing, public storage, object spawning, prisons & stairs, admin); overlay-visibility controls; a whisper-anyone overhaul; server-switch re-detection fixes.
- **0.18–0.25** — Beelzebub integration (abilities, loadouts, transforms, hotkeys, admin); per-channel chat colors; a secondary view-only chat window.
- **0.15–0.17** — First public release: Bloodcraft / Kindred command UI + live HUD overlays + Eclipse coexistence; the standalone tabbed chat window.
- **0.1–0.14** — Initial build-out: UI stack ported from BloodCraftUI (panthernet) + the signed Eclipse protocol (zfolmt).

</details>
