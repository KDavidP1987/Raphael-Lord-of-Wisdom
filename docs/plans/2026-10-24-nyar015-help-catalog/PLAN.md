# Raphael 0.74.0: Nyarlathotep 0.15.0, built-in Nyar help, and a unit and object reference
_Round 1 revision. The four decisions below are locked with Kris; Codex reviews how, not whether._

## Context
Three things, released together as **0.74.0**, which also carries the reset-report fix written as "0.73.1":

1. **Nyarlathotep 0.15.0 (horde pools).** Testers find the Nyar windows confusing and don't open the guide or README.
   0.15.0 also changed things Raphael doesn't support yet. See
   `Nyarlathotep Lord of Chaos/docs/RAPHAEL_HANDOFF_HORDE_POOLS.md` and `RAPHAEL_INTEGRATION_CONTRACT.md:182`.
   The wire is unchanged: still api 8, with no new tag, key or command.
2. **Explanations inside the Nyar windows.** Every setting gets a tooltip covering what it does and means, its
   default, and its range or options. Each window gets a folded "How this works" section.
3. **A unit and object reference.** Admins currently leave the game to look up prefab IDs. The reference lists every
   unit and every world object, whether a mod uses it or not, and can be searched and filtered. It comes in two forms:
   a page under Settings and Help, and a floating panel that can be dragged and fills form fields.

| Locked decision (Kris, plan mode, 2026-10-24) | |
|---|---|
| Release | One 0.74.0. The "0.73.1" changelog heading becomes 0.74.0. |
| Objects | All world objects (precise rule in C1), with the 532 units. |
| Browser | Both: an in-interface page and a floating overlay that can be dragged, groups and filters by type, and fills fields. |
| Nyar help | Tooltips plus collapsed guides, with no "?" buttons. |

## Step 0: commit boundary (before any 0.74.0 code)
- Today's tree holds 0.73.0's uncommitted source, which is what was published, plus the reset-report fix in
  `MainPanel.BcReset.cs` and both changelogs.
- The fix touches exactly three files: `Raphael/UI/ModContent/MainPanel.BcReset.cs` (untracked), `CHANGELOG.md` and
  `CHANGELOG.thunderstore.md`. Its edits were made this session, so they are known exactly.
- State of each path in 0.73.0:
  - `MainPanel.BcReset.cs` **existed in 0.73.0.** It is the 0.73.0 reset card, published in the 0.73.0 DLL. Its
    pre-fix state is "restore prior content", not absence.
  - Both changelogs also existed then. Their pre-fix state is the 0.73.0 text, without the "0.73.1" section.
- Mechanism:
  1. Copy those three files to `fix/` inside the session scratchpad
     (`%LOCALAPPDATA%\Temp\claude\...\scratchpad\fix\`), which is outside the worktree. `git status` must not show it.
  2. Write the pre-fix versions by reversing the known edits.
  3. `diff` the pre-fix copies against `fix/`. The only differences may be the known hunks; anything else stops the
     step.
- Commit with an explicit path allowlist:
  - every path in `git status` except `.claudex/` and `docs/plans/2026-10-24-*`
  - the list is printed and checked against the 0.73.0 changelog first
  - commit `chore(release): v0.73.0`, then tag `v0.73.0`
- Prove the 0.73.0 commit:
  - `dotnet build -c Release` and the logic tests pass on it
  - the built DLL's AssemblyVersion is 0.73.0
  - Byte identity with the published DLL is not claimed: builds aren't deterministic here.
- Restore the three files from `fix/`. `diff` must then show no difference from `fix/`.
- Build and run the tests, then commit `fix(bc-reset): readable report and Start a new reset`.
- No push yet: pushing waits for Kris's OK at release.

## Part A: Nyarlathotep 0.15.0 support
Base path for Raphael files: `Raphael/Raphael/`.

**A1. Pool entries with a min-max count**, the token `pool:<name>:<min>-<max>` (handoff L66).
- Pool names: the server's existing pool-name rule is reused, with no new quoting. Raphael already validates
  `pool:<name>:<count>` in `CheckUnitList` (`Services/Nyar/NyarFieldRules.cs:357-359`), and its name check is kept
  as is.
- One validator, `NyarFieldRules.CheckPoolRange`:
  - min must be 1-500, and max must be from min up to 500.
  - An entry holds either a count or a range, never both.
  - The parse is overflow-safe (int.TryParse).
  - The messages mirror the server's: `min must be 1-500`, `max must be min-500`.
- HordeMarch totals: the sum of mins must be at least 10, and the sum of maxes at most 500 (handoff L78). They are
  checked with the same arithmetic the server uses, so a refused save explains itself.
- Code changes:
  - `ParseEntries` (L485) and `BuildEntries` (L507) round-trip all three forms: `CHAR_x[:n]`, `pool:p:n`,
    `pool:p:a-b`.
  - The comment on `NyarEntry.Min/Max` (L48) changes from "boss roll" to "boss roll or pool range".
  - In the units form, a pool row gets a Count / Range switch.
- Read-back: parse the wave line `pool X ×5-20` (server `EventAdmin.cs:1114`) in `NyarInfoParse.cs:35-36`.
  - The units line `{Count} from {pool}` (`EventAdmin.cs:895`) prints no min, so a range entry reads back from the
    wave-line form.
  - If a line can't be parsed, that field shows "couldn't read" as today, and the raw line is logged at debug level.
- Where the cases come from: tests use the handoff's examples and the server's own test fixtures where they exist,
  in `Nyarlathotep/Tests`.

**A2. Library pools.**
- The handoff (L88-92) says the `.nyar pool list` lines are not a contract and must not be parsed. So:
  - **Pools library card** in Admin: Events: List, Next page, Info `<pool>` and Reload buttons. The server's reply
    lines show verbatim in that card's reply area, through the generic-response path Nyar admin cards already use.
    Nothing is parsed, so there is no paging state machine.
  - The Info name goes through the same pool-name validator as A1 before any command is built. A name that fails
    is shown as refused and never sent.
  - **Pool name entry:** the pool chips keep listing the event's own pools. A pool row also gets a free-text name
    box, "or a library pool name", which the server checks at load (handoff L90 recommends this). Library pools
    are read-only from Raphael: the server never writes pools.json, and Raphael offers no edit or delete for them.
  - The misleading "No pools yet, make one" note (`MainPanel.NyarControls.cs:372`) becomes: "This event has no
    pools of its own. Make one below, or type the name of a library pool (see Pools library)."

**A3. "Castles may be entered" (`action.allowTerritory`)** on Apocalypse, Infestation and HordeMarch.
- Offering it: `FieldsFor` (`NyarFieldRules.cs:684`) offers it for those three types as well as SpawnWaves.
- Writing it: the YesNo control already sends `event set <id> action.allowTerritory true|false`, as SpawnWaves does
  (`MainPanel.NyarEditor.cs:531`). A test asserts that the built command is identical for all four types.
- Reading it: `NyarForm.cs:181` is extended to the three types. The server prints "claimed territory allowed" for
  them too (`EventAdmin.cs:988`).
- Refused start: when a start fails with "inside a castle's territory", the reply already appears where it was
  started (the Nyar admin generic-response area). A readable explanation is added to the same mapping as A4. It
  names the setting: open the event, Location, "Castles may be entered". This is text only, with no linking across
  editors.

**A4. Limits and messages.**
- The hint at `NyarHordes.cs:224` explains two separate limits:
  - `[Limits] MaxTrackedUnits` is the server-wide setting that holds a horde. Its default is 500, range 1-500.
    `MaxUnitsPerWave` no longer applies to hordes.
  - Each event's own unit total is checked against a ceiling of 500 when it is saved, as in A1.
- The 500 ceilings live in one constants class (`NyarLimits`), which the steppers, validators and help all read.
- New load reasons (handoff L70-80) get readable explanations in the existing reason-to-text mapping, matched by
  pattern. Unknown reasons still show the raw text.
- The guide (`Services/Guides/NyarlathotepGuide.cs`) moves to "checked against 0.15.0" and gains a "Pool library"
  section.

**A5. Older servers.**
- The api level is 8 for both 0.14 and 0.15, so Raphael tells them apart by the plugin version.
- Where the version comes from: the handshake's `plugin=` field (`NyarState.cs:158`).
  - `NyarState.PluginVersion` is reset when the connection drops (`NyarState.cs:269`) and set again on the next
    handshake.
  - It is parsed by `NyarVersion.Parse` (`Services/Nyar/NyarVersion.cs:16`), which already handles `-rc.N`.
- New `NyarVersion.AtLeast(plugin, 0, 15, 0)`. **It fails closed:** an empty or unparseable version counts as older.
  An rc of 0.15.0 counts as 0.15.0, because the features shipped in its release candidates.
- On an older or unknown version:
  - the pool range switch and the `action.allowTerritory` switch on **Apocalypse, Infestation and HordeMarch** are
    disabled, with the reason "needs Nyarlathotep 0.15.0 (server reports <v>)"
  - The SpawnWaves castle switch, which existed before 0.15, stays available.
  - the Pools library card shows the same note
- Tests cover an empty version, a malformed one, 0.14.0, 0.15.0-rc.1, 0.15.0 and 0.16.0.

## Part B: Explanations in the Nyar windows
**B1. Help data without drift.**
- New `Services/Nyar/NyarHelp.cs` holds **prose only**, keyed by field: what it does, and what it means in play.
  Choice fields add one line per option.
- Defaults, ranges, units and option lists are not written in `NyarHelp`. The tooltip builds them from `NyarSpec`
  (`NyarFieldRules.cs:24-141`) and `NyarLimits`.
- **cfg keys are not controls in Raphael.** No Nyar window edits the server's .cfg. The keys are documented only in
  the guide's `GSettings` block (`NyarlathotepGuide.cs:390`), which the guide tests already cover. They get no
  tooltips because there is nothing to hover. The ~35 cfg keys were miscounted as tooltip work in round 0.
- The text is written from the Nyarlathotep README:
  - Configuration, L603-708
  - Setting up events, L711-1060: fields, actions, entries and modifiers
  - Unit pools, L1061-1169

**B2. Tooltips.**
- `NyarStepper`, `NyarTextField`, `NyarChoice`, `NyarYesNo` and `NyarListEditor` (`MainPanel.NyarControls.cs`) call
  one `NyarTooltip(field)`. It shows the meaning, then the options, then "Default x · range a-b", then the raw key in
  muted text.
- `AddNyarInput` (`MainPanel.Nyar.cs:186`) gains a `helpKey` parameter. The non-field inputs get keys in a small
  `NyarHelp.Inputs` table: new/copy id and pillar, template "create as", zone, announce and reset.
- Tooltips use `TooltipHover.Attach`, which every Nyar control already uses through `NyarCell`
  (`MainPanel.NyarControls.cs:48`).

**B3. Folded "How this works" sections.**
- They are built with `CollapsibleSection.Build` (`UI/Forms/CollapsibleSection.cs:34`) and closed by default.
- Its state lives in the rebuilt GameObject, with nothing saved and one header handler that is destroyed along with
  it. So rebuilds can't stack handlers or collide on saved state.
- The text comes from `NyarHelp.Sections`, one entry per section id:
  - the editor top
  - the cards: Basics, Conditions, Empower, SpawnWaves, Apocalypse, Infestation, Horde march, Location, Units,
    Waves and Pools
  - the windows: New or copy, Templates, Pillars, Zones, Units tab and Pools library
- Each section has 3-8 lines: what the window is for, one worked example, and the commonest mistake. The sections go
  at card level, never inside the dense form rows, so they never nest more than one level below a card.

**B4. Coverage tests (logic tests).**
- Every field id that `NyarFieldRules.Spec()` returns a spec for, and every id `FieldsFor()` can return for every
  action type, has non-empty prose. That includes the spread, modifier and stat keys.
- Inputs and sections are attached in only two ways: `NyarHelpInput("<key>")` and `NyarHowItWorks(parent, "<id>")`,
  each called with a string-literal key.
  - The test extracts those call sites with a regex anchored on the method names, after stripping `//` comments.
  - It asserts that the set of keys used equals the set of keys defined, in both directions.
  - The logic tests can't instantiate Unity UI, so this is the strongest check available without the game. V3
    covers the rendered result.
- No help text repeats a default or range number. A regex flags digits next to "default"; it does not apply to
  quoted examples.
- Preflight's glyph check covers `NyarHelp.cs`.

## Part C: Unit and object reference
**C1. What is included**, from the prefab dump at `Beelzebub Lord of Gluttony/Reference Data/Prefabs/`
(23,535 files, game data 1.1.12) and `prefab_names.tsv`:
- **Units:** every `CHAR_*` prefab: 532.
- **Objects:** the union, with each GUID counted once, of every prefab whose name starts with `TM_` (3,778),
  `Dynamic_` (520, world props such as barrels, crates, banners and bonfires), `Chain_` (1,456, breakable and staged
  props such as plants, tool stands and tree stumps), `Resource_` (139) or `Trees_` (385), plus every other prefab
  carrying `BlueprintData`. That is about 6,300 objects and about 6,800 rows in all, roughly 500 KB.
- **Left out on purpose:** abilities (`AB_`), buffs, items, effects, projectiles, sequences, menu groups and the other
  non-placeable prefixes.
  - The generator writes `tools/catalog-exclusions.md`, a committed report, so the cut can be reviewed and is never
    silent. It contains:
    1. A count for every excluded prefix.
    2. In full, **every excluded prefab that has an object-like component**: `Health`, `TileModel`/`TilePosition`,
       an inventory, `DestroyOnSpawn`-free `Interactable`, `CastleHeartConnection` or `BlueprintData`. Each has the
       component that flagged it, so a placeable prop hiding in an excluded prefix is visible and can be moved into
       the include rule.
    - The component list is finalised against the dump when the generator is written, and recorded in the report's
      header.
- **Counts:**
  - The header records both the raw count per prefix rule and the deduplicated total.
  - A test asserts that every GUID appears exactly once, and that the counts per group and per sub-group match the
    header.

**C2. Names and licensing.**
- Raphael is MIT. KindredCommands' name table is AGPL-3.0, so **no KindredCommands data is copied into Raphael**.
- **Units:** the PrefabGUID-to-localization-key table from BloodCraftUI. It is MIT, and is Raphael's own ancestor,
  already credited in LICENSE.txt. It covers 515 of 532 units, as measured. At runtime the key resolves through the
  game's own `Localization.LocalizedStrings`, as `Resources/VBloodLocalization.cs` already does, so names show in the
  player's language.
- **Objects:** at runtime, by a live lookup keyed on the row's GUID. The lookup:
  1. finds the prefab through `PrefabCollectionSystem`
  2. reads its blueprint's managed data from `GameDataManager.ManagedDataRegistry`
  3. resolves that data's name `LocalizationKey`
  - Each step is wrapped. A failure keeps the readable fallback name and adds to one counted debug line.
  - Nothing is stored in the generated rows for objects, because the dump holds no name key for them.
  - The exact API is confirmed in-game (V3) before the code relies on it. If no object names resolve, the object rows
    keep their readable names, and the release notes say so.
- **Fallback for every row:** a readable name made from the prefab name, `TM_Castle_Container_Bookshelf_Base_6x2`
  becoming "Castle Container Bookshelf Base 6x2". It is marked as "from prefab name" (`NameSource`).
- `NameSource` is one of `GameText` (resolved at runtime) or `PrefabName`. It is set by how the name was actually
  obtained, never assumed in advance.
- **When names resolve:**
  - The panel opens at once, showing fallback names.
  - A ticker on `CoreUpdateBehavior.Actions` then resolves names in batches of 200 rows per frame, about 35 frames
    for 6,800 rows. The ticker removes itself when done.
  - It runs on the Unity main thread only. All ECS, registry and localization access happens there, and nothing
    runs on a background thread.
  - When a row's name resolves, that row's normalised search text is rebuilt.
  - After each batch, a revision counter goes up. An open view re-runs its query and sort on its next refresh, as the
    reset card does with `BcResetRunner.Revision`.
  - Results are cached for the session.
- Offline logic tests use the fallback names, plus a test that a late rename changes the search hits and the sort.

**C3. Generated data format.**
- `tools/gen-game-catalog.py` writes `Resources/GameCatalog.Data.cs` holding **one `const string`**, a
  tab-separated table, parsed lazily on first use. It is not a big array initializer, so there is no giant static
  constructor.
  - Columns: kind, guid (signed int32, the dump's own form), internal name, group, sub-group, level, flags,
    localization key or empty.
- The file header records provenance:
  - each source path
  - a manifest sha256 over the sorted (file name, sha256) list of every prefab file actually read
  - the sha256 of `prefab_names.tsv` and of BloodCraftUI's `PrefabNames.cs`
  - the game data version and the counts
  - the sha256 of the table string itself
- If an input is missing, the generator stops with a clear error. So does a GUID in the dump that disagrees with
  `prefab_names.tsv`. Duplicate GUIDs or names fail the run.
- Two checks:
  - `--check` regenerates both artifacts, `GameCatalog.Data.cs` and `tools/catalog-exclusions.md`, into memory, and
    byte-compares each with the committed file. It runs where the sources exist, which is this PC, the only machine
    that builds Raphael releases. Every V Rising repo is pc-only.
  - A logic test, which always runs, recomputes the table's sha256 and compares it with the header. Hand edits fail
    on every machine.
  - Raphael has no CI. Release preflight on this PC runs `--check` and **fails**, not skips, when the sources are
    missing.
- Table format:
  - Tab-separated, with no escaping. The generator refuses any tab or newline inside a field.
  - Every row has exactly 8 columns, with integers parsed by TryParse.
  - **Strict at build time:** a logic test parses every committed row in strict mode, and any error fails the tests
    and so the release.
  - **Lenient at runtime**, as a defence only: a malformed row is skipped and counted, with one warning line, and the
    rest still loads.
  - A test feeds both modes malformed rows.
- Categories:
  - A unit's group is its faction: `FactionReference` mapped through the `Faction_*` prefabs, the
    `unit_index.tsv` method. Its level comes from `UnitLevel`.
  - The flags: VBlood (the `VBloodUnit` component), minion, buildable (`BlueprintData`), container (an inventory
    component).
  - An object's group is Castle, Resource, Tree or World, and its sub-group is the next name segment, for example
    `TM_Castle_<Floor>`.

**C4. Search.**
- `GameCatalog.Query(text, kind, group, flags)` is pure logic.
- Normalisation: lower-case, with `_` and `-` turned into spaces. It is precomputed once per row as a single search
  string covering the display name, the internal name, the GUID, the group and the sub-group.
- Matching: every typed word must match (AND), in any order.
- A typed number is read as a signed or unsigned 32-bit value and normalised to the signed form, so
  `4294967295` finds `-1`. If it equals a GUID, it matches that row exactly.
- Order:
  1. an exact GUID or internal-name match
  2. a display-name prefix match
  3. the rest
- Ties sort by display name, then internal name, so the order is stable.
- Filter first, then sort.
  - Flat mode: global pages of 60.
  - Grouped mode: groups in order by name. Each group holds its own sorted rows, with a per-group shown-limit.
  - The two modes are tested separately.

**C5. UI: two hosts, one view model.**
- `GameCatalogView` is plain logic. It holds the query state, filters, group-by, the expanded groups and the paging.
- `MainPanel.GameCatalog.cs` (the new **Reference: Units & Objects** tab in Settings and Help, with a new
  `PanelType`) and `GameCatalogOverlayPanel` (a `ResizeablePanelBase`, modelled on `FamiliarBrowserOverlayPanel.cs`)
  each build **their own** controls from that state. Nothing is reparented.
- The overlay's size and position use the existing panel config keys (`PanelType` and `ResizeablePanelBase`). It
  calls `EnsureValidSize` / `EnsureValidPosition` on every open, so a changed resolution can't lose it.
- Layout:
  - A search box, with input debounced by 150 ms.
  - Type tabs: Units, Objects, All.
  - Filter chips:
    - for units: V Blood, Minion, a faction drop-down
    - for objects: a group drop-down, a sub-group drop-down, Buildable, Container
  - A Group by switch: none, faction (units) or category (objects).
- The All tab:
  - Only the kind-neutral filters show: the search box and Group by.
  - Grouping is first by kind (Units, Objects), then by faction or category inside each kind.
- Grouping:
  - With grouping off, results show in flat pages of 60, with Next and Previous.
  - With grouping on, each group header shows the **total** matches in that group and is folded.
  - **One group is open at a time.** Opening a group closes the one that was open.
  - The open group shows 60 rows, then "Show 60 more", with up to 240 rows. Past that it shows "Refine your search
    to see more".
  - So at most 240 row controls are alive. A test covers the open-group switch and the "Show more" limit.
- Rows show the display name, the internal name and the GUID, plus faction and level for units. A muted mark shows
  when the name comes from the prefab name.

**C6. Filling forms (Browse…).**
- Each **Browse…** button targets **one text input**, the field's existing pick or search box, never a list or pool
  structure directly. So the user still presses that form's own Add or Save, and list syntax is never written by the
  catalogue.
- The button registers a `BrowseTarget`: input reference, owner GameObject, allowed kind, value format (`Name`,
  `ShortName` without `CHAR_`, or `Guid`) and a starting filter. The overlay opens set to that kind and filter.
- On a row click, the overlay first checks that the owner and the input are still alive and active, then **replaces**
  that input's text. If they're gone, the overlay shows "That form was closed; pick a field again" and drops the
  target.
- A newer Browse click replaces the target. Opened from Help with no target, rows show **Copy name** and **Copy GUID**
  instead.

| Where | Input (file:line today) | Kind | Writes |
|---|---|---|---|
| Nyar unit list picker | `MainPanel.NyarControls.cs:354` | Unit | Name |
| Nyar pool member picker | `MainPanel.NyarEditor.cs:833` | Unit | Name |
| Nyar test spawn | `MainPanel.NyarEditor.cs:1084` | Unit | Name |
| Beelz admin unit or GUID | `MainPanel.Beelzebub.cs:4040` | Unit | Name |
| Beelz PrefabGUID filter | `MainPanel.Beelzebub.cs:2920` | Unit or Object (button says "Browse units & objects…"; tooltip notes abilities and items aren't listed) | Guid |
| Beelz transform unit | `MainPanel.Beelzebub.cs:4206` | Unit | Name |
| Beelz lock-group member | `MainPanel.Beelzebub.cs:4136` | Unit | Guid |
| Uriel block prefab | `MainPanel.Uriel.cs:1259` | Object | Name |
| Uriel config prefab | `MainPanel.Uriel.cs:1308` | Object | Name |
| Uriel event marker unit | `MainPanel.UrielEvents.cs:323` | Unit | ShortName |
| Uriel squad unit | `MainPanel.UrielEvents.cs:521` | Unit | ShortName |
| Uriel unit name | `MainPanel.UrielEvents.cs:672` | Unit | ShortName |

- The Name, ShortName or Guid choice for each row is checked against what that field's command accepts, before it is
  built.
- Two fields are left out on purpose: the raw list text box and the "Any other field" box. They hold whole list
  syntax, so filling them would overwrite a list. Their tooltips point to the list picker.
- A logic test holds this table as data, and asserts each format's output for a sample row.

**C7. Keeping UnitCatalog's behaviour.**
- The consumers are `MainPanel.NyarControls.cs:355,440,465`, `MainPanel.NyarEditor.cs:833,1085` and
  `MainPanel.UrielEvents.cs:155`.
- `UnitCatalog.All` keeps its exact tuple shape `(Name, Faction, Level, VBlood, Minion)`, its filter (players,
  critters and ignored factions are skipped, as are Dummy, Test, Tutorial and Illusion units) and its sort by name.
  It becomes a view over the new data.
- `Search()` keeps its signature and labels.
- A test pins today's 426 names, taken as a snapshot from the current generated file before it is replaced, and
  asserts that the new view returns the same set and order. Any difference has to be accepted on purpose.

**C8. Diagnostics.**
- Each of these produces one debug log line, never chat:
  - a parse miss on a Nyar read-back line
  - a localization or blueprint name that could not be resolved (counted, logged once per session)
  - a stale Browse target
- The generator prints a summary: counts, unresolved factions, and rows that fell back to the prefab name.

## Out of scope
- A region for every unit: the data has one only for 22 V Bloods.
- Items, abilities and buffs.
- Any server change.
- Translating the help prose.
- Parsing `.nyar pool` replies.

## Critical files
- `Services/Nyar/`: `NyarFieldRules.cs`, `NyarInfoParse.cs`, `NyarForm.cs`, `NyarMessages.cs`, `NyarVersion.cs`,
  new `NyarHelp.cs` and `NyarLimits.cs`
- `UI/ModContent/MainPanel.Nyar*.cs`
- `Resources/UnitCatalog.cs`, new `GameCatalog.cs`, `GameCatalog.Data.cs` and `GameCatalogView.cs`,
  `tools/gen-game-catalog.py`
- New `UI/ModContent/GameCatalogOverlayPanel.cs` and `MainPanel.GameCatalog.cs`; `Data/PanelType.cs`; tab routing in
  `MainPanel.cs`
- Browse buttons: `MainPanel.Beelzebub.cs`, `MainPanel.Uriel.cs`, `MainPanel.UrielEvents.cs`
- `NyarlathotepGuide.cs`, `tools/preflight.ps1` (catalog `--check`), the changelogs, `README.md` and the
  `LICENSE.txt` credits

## Verification
- **V1.** `dotnet build -c Release` with 0 errors, and `dotnet run --project tests/Raphael.Logic.Tests`. New tests
  cover:
  - the pool range validator and token round trip: zero, negative, reversed, over 500, overflow, count and range
    together, the HordeMarch totals
  - `pool X ×5-20` read-back
  - the allowTerritory command for all four types
  - help coverage (B4)
  - catalogue counts against the header, normalisation, ranking, GUID match, filter, group and page order
  - the browse-target formats
  - the UnitCatalog snapshot
- **V2.** `tools/preflight.ps1`: the glyph check over all new text, and catalog `--check`.
- **V3.** On the dev server with Nyarlathotep 0.15.0. Kris is told before any client run, and the server is stopped
  afterwards.
  - Save a min-max pool entry and a library-pool name, and read both back.
  - Turn on castle entry for a horde march, and see a castle refusal explained.
  - Hover the tooltips and open each folded guide.
  - Check that unit names resolve in-game, and measure how many object names resolve.
  - Open the reference from Help and from each Browse button. Filter, group, fill a field, and spawn the unit picked.
  - Close a form while the overlay is open, and see the stale-target message.
- **V4.** Post-build Codex review. Then deploy to both Raphael.dll paths for Kris's own test.
- **V5.** Release, after Kris's OK:
  1. `bump-version.ps1 -To 0.74.0` and finish the changelog.
  2. Preflight in Release mode, then `package-release.ps1`.
  3. Push, and confirm `rev-parse HEAD` equals `ls-remote`.
  4. Kris uploads to Thunderstore.
