# Raphael, Lord of Wisdom

A **client-side** V Rising UI that turns the chat commands of popular **server-side** mods into buttons, forms, and on-screen overlays. Each mod's tabs appear only when that mod is detected on your server — so it works whether your server runs one, several, or none.

**Works with:** Bloodcraft · Beelzebub · Uriel · Faust · Nyarlathotep · KindredCommands · KindredLogistics — plus a standalone tabbed chat window that works on **any** server.

> 🟣 **Formerly BloodCraftHub** — same mod, renamed. Switching over? See **[Migrating from BloodCraftHub](#migrating-from-bloodcrafthub)** and **don't run both at once.**
>
> ⚠️ **Pre-1.0 beta.** It's daily-driven on a live server, but you may hit rough edges. The fastest way to get a fix is the **[Shadow Realm Discord](https://discord.gg/c4YU2HbHFA)**.

---

## What you get

- **One panel for your server's mods** — left-rail tab groups for Bloodcraft, Kindred, Beelzebub, Uriel, Faust, and Nyarlathotep; each shows only when detected.
- **Commands as forms** — player pickers, dropdowns, and number fields instead of typing `.lvl set <Player> <Level>` into chat.
- **Live HUD overlays** — XP, familiar, weapon/blood, professions, quests, shift-spell cooldown, and more. Each is draggable, resizable, and individually toggleable.
- **Standalone chat window** — tabbed per-channel chat (Global / Local / Clan / Whispers / System), whisper anyone online, custom colors, and per-channel history controls (keep conversation, drop noisy System floods). Needs no server mods.
- **Chat & Diagnostics in one place** — choose what Raphael hides from chat and what it logs, for itself and every server mod. Presets: Quiet, Normal, Show everything. Replies to commands you send always show.
- **In-built game guide** — a "V Rising Basics" tab explaining the base game (blood, Gear Level, V Bloods, castles, hazards) and how each mod changes it, so new players have a one-stop primer.
- **Admin tooling** — surfaced wherever the backing mod provides it: Kindred admin, Beelzebub ability shaping and lock groups, Faust server analytics, Uriel sharing/spawning and Castle Events units, Nyarlathotep event control.

<details>
<summary><b>Full feature list</b></summary>

- **Floating Raphael + OV buttons** (top-right) — open the panel; OV is a master overlay show/hide (with optional timed auto-reappear and launcher hiding).
- **Tab groups** (each appears only when its server mod is detected):
  - **Bloodcraft** — Familiars, Boxes, V-Bloods, All Familiars, Class, Weapon Expertise, Blood Legacy, Unarmed + Shift, Prestige, Levels, Daily Quests, Admin.
  - **Kindred** — Logistics (+ admin), Commands, Admin: Players / Server / World.
  - **Beelzebub** — Bestiary, Loadout (universal + per-weapon + per-form ability sets), Hotkeys, Transforms, Settings, admin Config / Players.
  - **Faust** — Castle Info, Open/All Plots, Decay Watch, Castle Resources, Player Info, Clans, Player Positions, Server Stats, **Boss Status**, **Leaderboards**, **World Map**, admin Control / Oversight.
  - **Uriel** — Storage Sharing, Prisons, Stairs, Object Spawning, Object Catalog, **Castle Events** (alpha: drop mob and boss markers, bind doors, start and time runs with a live readout and on-screen notices), **Event Library** (squads, floor templates, templates), Settings, admin Sharing / Objects / Config / **Units**.
  - **Nyarlathotep** — Live Events (regions, wave countdowns, on-screen notices), Settings, admin Events (with an inline editor per event and a searchable unit list) / **Units** (event units near you with positions) / Templates / Pillars / Announce / Kill Switch. Leaderboards, My Stats and Zones are ready for when Nyarlathotep adds them.
  - **Settings & Help** — Quick Start, per-mod Help guides, **V Rising Basics** (in-built base-game guide), Game Guide (external links), Settings, **Chat & Diagnostics**, About.
- **Secondary overlays** (toggle from the footer) — XP, Familiar, Familiar Browser, Daily Quest, Professions, Shift Spell, a Combined info overlay, Quick Actions, a Familiar Quick Spawn overlay, and a Faust Boss Tracker. Each is independently draggable/resizable with its own transparency; visibility persists across sessions.
- **Freeze actions while the UI is open** (opt-in) — stop moving/attacking/casting and block menu hotkeys while you click around the panel.
- **"Last server response" panel** — replies to read-data commands (`.wep get`, `.class l`, …) show in-UI, not just in chat.
- **Live data** via Bloodcraft's signed broadcast protocol, with chat-regex fallback for replies it doesn't cover.
- **Fast opening** — the panel opens right away; tabs fill in without freezing the game.
- **Refresh button** (panel footer, bottom-right; also Settings → Connection) — clears chat history and stale UI caches without touching settings. Use it if the game feels slower after a long session.
- **Quality of life** — hover tooltips on every control, auto-resizing panel, scrollable tabs + left rail, a two-zone color theme, a 50–400% text-size slider, and opt-in keyboard hotkeys.

</details>

---

## Install

Client-side only — install on the **player's** V Rising client, not the server.

**Mod manager (recommended):** in Thunderstore Mod Manager or r2modman, open your V Rising profile → search **Raphael** → Install. Make sure **BepInExPack_V_Rising** is in the same profile, then launch from the manager.

**Manual:** install BepInEx for V Rising, drop `Raphael.dll` into `BepInEx/plugins/`, launch.

On your first connect to a modded server, a floating **Raphael** button appears top-right. Hover any control to see what it does (the description shows in the footer). Non-admin commands work for everyone; admin tabs need permissions.

---

## ⚠️ Before you install

<details>
<summary><b>Running Eclipse? (compatibility needs re-testing)</b></summary>

Historically, Raphael + [Eclipse](https://thunderstore.io/c/v-rising/p/zfolmt/Eclipse/) crashed the client on load (the fault is in Eclipse's HUD code, not Raphael). When Raphael detects Eclipse it stands down from its own passive Bloodcraft layer to avoid the crash: **Eclipse** drives the live HUD, while Raphael keeps its command buttons and chat window. Eclipse has since updated and this hasn't been re-verified — **disable Eclipse while running Raphael to be safe.**

</details>

<details>
<summary><b>Raphael looks empty on a Bloodcraft server?</b></summary>

Raphael's live HUD and Bloodcraft tabs are driven by Bloodcraft's broadcast, which only runs when the server enables at least one of: **Leveling**, **Legacy**, **Expertise**, **Class**, or **Familiar** systems. A Quests-only / Professions-only server won't engage it — click the **Bloodcraft** header for an in-panel diagnostic with a one-click **Force-enable tabs** button (chat commands still work; live overlays stay empty until the server broadcasts).

</details>

<details>
<summary><b>Controller / gamepad</b></summary>

There are known edge cases when V Rising's controller input meets the Raphael UI (e.g. a face button re-opening the panel after a waypoint teleport). A first-pass fix is in; more testing is ongoing. If something opens/closes/focuses on the wrong button, please report it on Discord with repro steps.

</details>

<details>
<summary><b>Rolling back a version</b></summary>

- **Mod manager:** select Raphael → version dropdown → pick an earlier build (quit the game first).
- **Crash on load after an update:** close the game and delete `BepInEx/interop` and `BepInEx/cache` in your profile — they rebuild on next launch.

</details>

---

## Migrating from BloodCraftHub

Raphael **replaces** BloodCraftHub — don't run both. They have separate plugin IDs, so installing Raphael without removing BloodCraftHub loads **both** (duplicate buttons, panels, overlays).

1. In your mod manager, **uninstall or disable BloodCraftHub** (`kdpen/BloodCraftHub`).
2. **Install Raphael** (`kdpen/Raphael`).
3. **Fully quit and relaunch V Rising.**

Settings don't transfer — Raphael writes a new config, so overlay positions, colors, and hotkeys start at defaults (your old `kdpen.BloodCraftHub.cfg` is left untouched in case you roll back).

---

<details>
<summary><b>Compatibility (tested versions)</b></summary>

| Mod | Tested | Role |
|---|---|---|
| **BepInExPack_V_Rising** | 1.733.2 | Loader (hard dependency) |
| **Bloodcraft** (server) | v1.13.21 | Primary integration |
| **KindredCommands** (server) | v2.5.8 | Admin/player commands → Kindred |
| **KindredLogistics** (server) | v1.6.0 | Logistics toggles → Kindred |
| **Beelzebub** (server, optional) | v0.137.x (API 35) | Abilities / transforms → Beelzebub |
| **Faust** (server, optional) | v0.16.5 (API 19) | Server investigation & analytics → Faust |
| **Uriel** (server, optional) | v0.21.0 (API 1) | Storage / prisons / stairs / spawning / Castle Events (alpha) → Uriel |
| **Nyarlathotep** (server, optional) | v0.12.0 (API 7); v0.13.0-rc.1 (API 8) | World events / leaderboards → Nyarlathotep |
| **Eclipse** (client) | v1.3.13 | ⚠ Compatibility needs re-testing — disable while running Raphael |

Newer Bloodcraft builds generally still work — the chat-command grammar is stable. If a tab does nothing on click, the server likely renamed a command.

</details>

<details>
<summary><b>Known issues</b></summary>

- **Eclipse** compatibility needs re-testing (see above) — disable it to be safe.
- **Controller** edge cases under investigation (see above).
- **Conservative per-system detection** — Raphael may show Bloodcraft tabs for systems the server has disabled (they'll just be empty); per-system hiding is on the [Roadmap](#roadmap).
- **Limited autocomplete / pagination** — the name cache fills from chat but forms don't yet show a dropdown; only `.clan list` has prev/next widgets so far.

</details>

<details>
<summary><b>Screenshots</b></summary>

*Captured on v0.65.6. Click a group to expand it.*

<details>
<summary><b>Main panel &amp; settings</b></summary>

![Main panel](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-MainUI.png)
*Main panel: tab groups on the left rail, overlay toggles in the footer, and the small **Refresh** button bottom-right.*

![Quick Start](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-MainUI-Settings-QuickStart.png)
*Quick Start: a one-minute tour shown on first open.*

![Settings](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-MainUI-Settings-Settings.png)
*Settings: text-size sliders, launcher size, and per-overlay transparency.*

</details>

<details>
<summary><b>Overlays &amp; chat window</b></summary>

![Overlays in game](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-UI-Overlays-Settings.png)
*Overlays in play: XP / class / weapon / legacy, professions, daily quest, and the Familiar Browser, next to HUD settings.*

![Tabbed chat window](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-MainUI-Settings-GameChatUI.png)
*Standalone tabbed chat window (All / Global / Local / Clan / System / Whispers) and its settings. Works on any server.*

</details>

<details>
<summary><b>Bloodcraft</b></summary>

![Familiar overlays](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-BloodCraft-FamiliarManagement.png)
*Familiar overlays: active familiar stats and the Familiar Browser box list.*

![V-Bloods tab](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-Bosses.png)
*V-Bloods: captured-familiar collection with filters and one-click Summon.*

![Class tab](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-Bloodcraft_Classes.png)
*Class tab.*

![Weapon Expertise tab](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-Bloodcraft_WeaponExp.png)
*Weapon Expertise tab.*

![Blood Legacy tab](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-Bloodcraft_BloodLegacy.png)
*Blood Legacy tab.*

![Levels tab](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-Bloodcraft_Leveling.png)
*Levels tab.*

![Prestige tab](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-Bloodcraft_Prestige.png)
*Prestige tab.*

</details>

<details>
<summary><b>Kindred</b></summary>

![Kindred Logistics tab](https://raw.githubusercontent.com/KDavidP1987/Raphael-Lord-of-Wisdom/main/docs/screenshots/v0.65.6%20Screenshots/Raphael-v0.65.6-KindredCommands.png)
*KindredLogistics: personal toggles and item/chest finders as buttons.*

</details>

</details>

---

## Roadmap

What's planned next. These are plans, not promises. Server-mod features show up in Raphael once that mod ships them.

**Raphael**

- Hide Bloodcraft tabs for systems your server has turned off.
- Name autocomplete in forms, and page buttons on more long lists.
- Re-test with the latest Eclipse so both can run together.
- More controller / gamepad fixes.
- UI translations: German and Spanish first, then French and Swedish.

**Beelzebub**

- An action-bar hover card that shows each captured ability's real name instead of "No Name".
- An ability library with names, descriptions and when-to-use hints.
- Each ability's effective damage multiplier in its tooltip.
- Buttons for the remaining admin tools (unmount, raw bar view, damage stats).
- Server side: mastery levels and set rewards, smarter summons, and more ability tuning.

**Nyarlathotep**

- Live operations: every event unit on the map with go-to and despawn (Admin: Units shows the ones near you today), exact current settings in the event editor, a live event view with map pins, and wave-cleared updates.
- Communications: a catch-up list of what was announced while you were offline, start warnings, and scheduled or repeating messages.
- Stats: leaderboards, personal stats, hide / show and sharing. The tabs are ready; the server part is planned.
- Defended zones: list, add and remove zones. The tab is ready; the server part is planned.

**Faust**

- A region shown for players in the open world, not only on territories.
- A resource stats view.

**Uriel**

- A "current rules" panel for the container you're aiming at.
- A "My Shares" list.
- A stair-style picker that greys out styles you don't own.
- Castle Events: a separate run overlay, and a readout that no longer depends on Uriel's chat wording once Uriel sends machine-readable event data.
- Later, once Uriel adds them: coffin sharing, paid or timed door access, sharing all of a castle's storage at once, and castle rentals.

---

## Support & credits

- **Bug reports / feature ideas:** [Shadow Realm Discord](https://discord.gg/c4YU2HbHFA) (fastest) or [GitHub issues](https://github.com/KDavidP1987/Raphael-Lord-of-Wisdom/issues). A `BepInEx/LogOutput.log` snippet or clear repro helps a lot.
- **Upstream mods** Raphael wraps — please show them love too: **Bloodcraft** by zfolmt, **KindredCommands / KindredLogistics** by odjit. Raphael's UI framework derives from **BloodCraftUI** (panthernet) and the signed protocol from **Eclipse** (zfolmt).
- **Built on** the V Rising server *The Shadow Realm* (Brutal PvE). Support development: [PayPal](https://www.paypal.com/paypalme/KrisPenland).
- **Special thanks** to the testers who shaped Raphael through countless reports: Moonie, Bradley, Xavarie, Exotic Mystique, Shiyrva, Imperivm Draconis.

<details>
<summary><b>For developers</b></summary>

Open source (MIT). Build:

```powershell
cd Raphael
dotnet restore Raphael.sln
dotnet build Raphael.sln -c Release
# Deploy to your local mod-manager profile:
dotnet build Raphael\Raphael.csproj -c Release -p:DeployToClient=true
```

Fully quit V Rising before redeploying (the running game file-locks the DLL). Key docs: [`CHANGELOG.md`](CHANGELOG.md) (full history), [`CONTRIBUTING.md`](CONTRIBUTING.md), [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md), [`docs/THUNDERSTORE.md`](docs/THUNDERSTORE.md).

</details>

## License

[MIT](LICENSE.txt) — includes third-party attribution for ported code from BloodCraftUI (panthernet), Eclipse (zfolmt), and the mods Raphael integrates with.
