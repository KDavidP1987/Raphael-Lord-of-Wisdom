# Changelog — Raphael, Lord of Wisdom

*Player-facing highlights, newest first. Full per-patch detail lives in
[CHANGELOG.md](https://github.com/KDavidP1987/Raphael-Lord-of-Wisdom/blob/main/CHANGELOG.md).
Raphael was formerly **BloodCraftHub** (v0.1–v0.30).*

**Current: v0.65.6** (pre-1.0 beta).

---

## 0.65.6 — Long-session slowdown fixed, plus a "Refresh Raphael" button

- **Fixed: the game slowing down over a long session** (until you relogged). The chat window was redrawing its
  whole history on every message, even when closed, and stale tooltips piled up. Both are fixed; chat looks the
  same.
- **New: Refresh** button at the bottom-right of the main panel (also on the Connection tab) clears chat history
  and cached UI leftovers without leaving the world. Your settings and windows are kept.
- **Fixed:** a flood of messages containing `<` (like `<3`) could still blank the chat box.
- **Fixed:** several memory leaks when panels rebuild, and when reloading or capturing the Faust world-map image.
- **Docs:** new screenshots on this page, grouped into collapsible sections.

## 0.65.5 — Chat length cap lowered to protect non-Raphael players, plus Uriel v0.20.0 support

- **Fixed: a very long chat message could flood chat and freeze the chat box for players who don't run
  Raphael.** Messages around 480+ characters made the server re-broadcast the line repeatedly and left
  non-Raphael players unable to open chat until they teleported. Raphael now caps chat input at **470
  characters** (down from 500), safely under the threshold. Normal messages are unaffected. *(Thanks to Howl.)*
- **New: support for Uriel v0.20.0's flush-to-ground placement.** **Raise nearest / Lower nearest** buttons on
  the Object Spawning tab nudge the object you placed nearest you up/down so it stops floating or sinking, and
  admins get a **Height offset** field (per-object or global) under Uriel → Admin → Spawn conditions. The Uriel
  guide pages document all three new commands.

## 0.65.4 — Chat fixes: typed text re-centers after long pastes, and long messages stop flooding

- **Fixed: the chat input stopped centering your text after you'd pasted a long message.** It now re-centers
  reliably as soon as your message is back to a single line. *(Thanks to Kruczarz.)*
- **Fixed: a very long message could show up duplicated many times from a single send.** Sending a message
  that hit the character cap could spam receivers with copies of it; the chat window now recognizes and drops
  those duplicate copies. Short messages are untouched. *(Thanks to Howl and Strie.)*

## 0.65.3 — Chat polish: no more random italics, centered typing, optional idle auto-hide

- **Fixed: a chat tab could randomly turn italic and stay that way.** A message that left a rich-text tag
  open (e.g. a system message with an unclosed `<i>`) leaked its formatting into every line after it. Each
  chat line is now self-contained, so formatting can't bleed between messages. *(Thanks to Howl.)*
- **Changed: text you type in the tabbed chat is now vertically centered** on a single line (top-aligned once
  it wraps to multiple lines). *(Thanks to Kruczarz.)*
- **New: optional chat auto-hide (off by default).** Under Game UI → chat, the window can fade out after an
  idle delay (3–120s, default 12) and fade back in on activity — like the native chat. Configurable wake
  triggers keep it from blinking on busy servers: player messages wake it, but System lines (incl. Raphael's
  background pings) don't unless you opt in; hovering or opening chat always wakes it. *(Requested by Kruczarz.)*

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
