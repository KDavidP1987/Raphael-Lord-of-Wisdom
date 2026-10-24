# Plan Review Log: Raphael 0.74.0: Nyarlathotep 0.15.0 support (handoff at C:/dev/projects/Games/V Rising/Nyarlathotep Lord of Chaos/docs/RAPHAEL_HANDOFF_HORDE_POOLS.md and RAPHAEL_INTEGRATION_CONTRACT.md), Nyar help tooltips + collapsed guides, and a generated unit/object reference catalogue (page + floating overlay that fills fields). The four decisions in the table are locked by the owner; critique how, not whether.
Started 2026-10-24T16:19:06.643Z. MAX_ROUNDS=5.

## Round 1 — Codex

1. **The catalog scope is not deterministic.** “TM_*, Resource_*, Trees_* and other world props” leaves “other” undefined, so neither generation nor completeness tests can prove that all world objects are included.  
   **Fix:** Define inclusion/exclusion rules from prefab components and entity categories, then commit expected counts by category—not an approximate total.

2. **The generator inputs are neither pinned nor reproducible.** The plan depends on sibling repositories and 23,535 loose files without recording source revisions, hashes, required paths, or missing-input behavior.  
   **Fix:** Pin each source repository revision and localization version in generator metadata, fail clearly on missing/mismatched inputs, and emit provenance into the generated file.

3. **A single generated C# array may exceed CLR/compiler method-size limits.** Thousands of constructed rows and roughly 350 KB of literals can produce an oversized static initializer even if source generation succeeds.  
   **Fix:** Generate chunked initialization methods or compact parallel constant tables, and add a Release-build test against the full dataset.

4. **Prefab identity normalization is unspecified.** V Rising prefab GUIDs may appear in signed, unsigned, decimal, or wrapper forms; inconsistent normalization will break joins and searches.  
   **Fix:** Specify one canonical GUID representation and test equivalent signed/unsigned input, duplicates, collisions, and unresolved joins.

5. **Localization joins are underdefined.** The plan does not explain how prefab names map to localization keys, how duplicate keys are resolved, or what happens when live localization returns blank or malformed text.  
   **Fix:** Define deterministic join precedence and fallback order, preserve source provenance, and test missing, duplicate, and blank localization values.

6. **“Official name” can become false after live localization.** The flag appears derived from English source coverage, while display text may later come from an unrelated live localization lookup.  
   **Fix:** Track name source independently from localized display resolution and only mark official names when the prefab-to-key mapping itself is authoritative.

7. **The pool-list protocol lacks a paging state machine.** “Add a paged reader” does not address page ordering, completion detection, timeouts, duplicate pages, unrelated console replies, or two requests in flight.  
   **Fix:** Specify request correlation, page accumulation, terminal conditions, timeout/cancellation, deduplication, and stale-response handling before implementing UI consumers.

8. **Library pool mutability is unclear.** Showing library pools in the same chips and picker risks presenting read-only pools as locally editable or deletable.  
   **Fix:** Define library-pool permissions explicitly and visually distinguish read-only selection from event-owned pool editing.

9. **Pool identity and token escaping are unspecified.** `pool:<name>:<min>-<max>` is ambiguous if names may contain spaces, colons, hyphens, or case variants.  
   **Fix:** Adopt the server contract’s exact quoting/escaping and case rules, and add round-trip tests for every permitted boundary character.

10. **Range validation is incomplete.** Extending `Min/Max` does not define positivity, `min <= max`, server maximums, overflow, omitted bounds, or legacy fixed-count compatibility.  
    **Fix:** Add one canonical validator with explicit bounds and normalization, then test zero, negative, reversed, excessive, overflow, fixed-count, and range forms.

11. **The 500-unit limit is treated as presentation text only.** Updating a hint does not ensure client validation and aggregate estimates match Nyarlathotep 0.15.0 behavior.  
    **Fix:** Centralize the limit/default in versioned field metadata and validate worst-case expanded pool counts consistently.

12. **Castle support does not cover the write path.** Adding the field to `FieldsFor` and read-back handling does not prove that form serialization emits it for all three event types.  
    **Fix:** Identify and test the complete create/edit/read-back command path for Apocalypse, Infestation, and HordeMarch.

13. **The refusal-message requirement has no concrete integration point.** “Show that refusal together with the switch” does not say how an asynchronous start failure finds the correct editor, event, or control.  
    **Fix:** Define failure correlation and UI state behavior, including closed editors, multiple events, stale replies, and unsupported server versions.

14. **Help coverage tests are materially too weak.** Testing only `NyarFieldRules.Spec()` misses configuration keys, new/copy inputs, template/zone/reset fields, dynamic options, and guide sections.  
    **Fix:** Enumerate every rendered Nyar control key from authoritative registries and assert non-placeholder help with default/range consistency.

15. **Help duplication invites drift.** Defaults and ranges would exist in field specs, settings declarations, and `NyarHelp`; an existence test will not catch contradictory values.  
    **Fix:** Generate tooltip defaults/options from typed field metadata and keep only explanatory prose in `NyarHelp`.

16. **The numerous collapsible sections have no identity or lifecycle design.** Repeated builders can collide on saved state, duplicate event handlers during rerenders, or create deeply nested unusable layouts.  
    **Fix:** Assign stable unique section IDs, dispose handlers on rebuild, and test repeated navigation and rendering.

17. **“One shared builder” is too vague for two different hosts.** A settings page and draggable overlay have different ownership, sizing, navigation, and callback lifetimes; sharing live controls can cause reparenting and stale subscriptions.  
    **Fix:** Share immutable query/view-model logic while constructing separate host-specific view trees with explicit disposal.

18. **Overlay persistence is incomplete.** Remembering position without defining storage, monitor changes, resolution/UI-scale changes, or off-screen recovery can permanently lose the panel.  
    **Fix:** Specify persistence location and clamp position and size to the current viewport on every open.

19. **Field-fill callbacks can target stale controls.** A form may close, rebuild, change row, or change event while the overlay remains open.  
    **Fix:** Use a cancellable destination token tied to the form/control lifetime and revalidate the target before applying a selection.

20. **Selection output rules are under-specified.** “Internal name, or GUID where needed” ignores list delimiters, pool-row structure, quoting, duplicate handling, and raw-field replacement versus insertion.  
    **Fix:** Define a typed browse-target contract for each destination and add integration tests for its exact serialized result.

21. **The Browse-button inventory is not actionable.** Phrases such as “any-other-field” provide no exhaustive mapping and make omissions inevitable.  
    **Fix:** Add a table of concrete control factories, accepted catalog kinds, emitted value format, and expected insertion behavior, then test the inventory.

22. **Grouping and pagination semantics conflict.** Paging 60 flat results and then grouping produces partial groups and misleading counts; grouping first can make pages unpredictably large.  
    **Fix:** Define filtering, sorting, grouping, and pagination order, and distinguish total group counts from rows visible on the current page.

23. **Search behavior is underspecified.** There are no rules for casing, punctuation, underscores, token order, numeric GUIDs, generated-name markers, ranking, or stable ordering.  
    **Fix:** Specify normalization and deterministic sort/ranking rules and cover them with edge-case tests.

24. **The query plan risks UI stalls and allocation churn.** Re-tokenizing and scanning all fields on every keystroke while rebuilding grouped controls can hitch the game UI.  
    **Fix:** Precompute normalized search text, debounce input, cap rendered controls, and add a performance acceptance threshold using the full generated catalog.

25. **`UnitCatalog` compatibility is asserted, not demonstrated.** Existing pickers may depend on ordering, concrete collection types, display-name semantics, or uniqueness that the new view changes.  
    **Fix:** Inventory its public consumers and add compatibility tests for API shape, ordering, row count, lookup behavior, and duplicate handling.

26. **Generator verification is not wired into the stated commands.** A Python generator test will not automatically run under the listed `.NET` test invocation, and exact counts are not meaningful until inputs are pinned.  
    **Fix:** Add an explicit reproducibility command that regenerates to a temporary path and byte-compares output, invoked by preflight/CI.

27. **Generated-data review and licensing are omitted.** Checking third-party-derived names into a release artifact requires provenance and redistribution review.  
    **Fix:** Record licenses and attribution for each input source and confirm redistribution eligibility before committing generated data.

28. **Version compatibility is not addressed.** Raphael can apparently connect to older Nyarlathotep servers, where new commands, reply formats, and fields may be absent.  
    **Fix:** Add capability/version detection with graceful UI fallback and tests for both 0.14.x and 0.15.x responses.

29. **Failure observability is inadequate.** The plan defines happy-path UI but no diagnostics for malformed server replies, unresolved catalog joins, duplicate GUIDs, localization failures, or timed-out requests.  
    **Fix:** Add bounded diagnostic logging with request context and generator summary/error reports, without flooding chat or logs.

30. **The release sequence relies on an unverified dirty-worktree assumption.** Committing “0.73.0’s uncommitted source” first may accidentally include unrelated user changes and does not reconcile the stated 0.73.1 reset-report fix.  
    **Fix:** Inventory and attribute every pending change, define the exact commit boundary and changelog history, and require a clean verified tree before 0.74.0 work and packaging.

VERDICT: REVISE

### Claude's response (round 1)

Accepted, and folded into the plan:

- **#1 Inclusion rule.** C1 now gives an exact prefix and component rule and lists what is excluded; per-group counts are pinned in the generated header.
- **#2 and #26 Provenance and check.** C3 adds source hashes, the game data version, failing on missing or mismatched inputs, and a `--check` byte-compare that preflight runs.
- **#3 Static initializer size.** C3 replaces the big array initializer with one const TSV string parsed lazily.
- **#4 GUID form.** C3 uses the dump's signed int32 form and fails the run on a duplicate GUID or name.
- **#5, #6 and #27 Names and licensing.**
  - This was a real problem: KindredCommands is AGPL and Raphael is MIT, so C2 drops KindredCommands entirely.
  - Unit names come from BloodCraftUI's MIT PrefabGUID-to-localization-key table, which covers 515 of 532 units (measured), resolved at runtime from the game's own strings.
  - Object names come from the game's blueprint data at runtime, best-effort, and are verified in V3.
  - Every row falls back to a readable version of its prefab name.
  - `NameSource` records how each name was actually obtained.
- **#7 Pool list paging.** The handoff (L88-92) says the `.nyar pool list` lines are not a contract and must not be parsed. A2 shows them verbatim, so there is no state machine.
- **#8 Library pools are read-only.** A2 states it, and Raphael offers no edit or delete for them.
- **#9 Pool name rules.** A1 reuses the existing server pool-name check already in CheckUnitList, with no new quoting.
- **#10 One range validator.** A1 adds `CheckPoolRange` with the server's bounds and the HordeMarch totals.
- **#11 500 limit as data.** A4 adds a `NyarLimits` constants class used by the steppers, validators and help.
- **#12 Castle switch write path.** A3 confirms it is the same `event set` command and adds a test for all four types.
- **#13 Castle refusal.** A3 scopes it to explanation text where the reply already shows. It is not linked across editors.
- **#14 Help coverage.** B4 widens it to `FieldsFor` for every action type, the input keys and the section ids.
- **#15 Help drift.** B1 keeps prose only in NyarHelp; defaults and ranges come from NyarSpec and NyarLimits, and a test flags numbers in the prose.
- **#16 Collapsible state.** B3 notes that the state lives in the rebuilt GameObject and nothing is saved.
- **#17 Two hosts.** C5 shares a view model only; each host builds its own controls.
- **#18 Overlay position.** C5 reuses the ResizeablePanelBase config keys and calls EnsureValidSize and EnsureValidPosition on each open.
- **#19 to #21 Browse targets.** C6 adds a BrowseTarget with a liveness check, has every Browse replace the text of one single input only, and gives a concrete table with each field's file:line, kind and format. The raw-list and any-field boxes are excluded on purpose.
- **#22 Grouping and paging.** C4 and C5 fix the order (filter, sort, group, page) and show total counts per group.
- **#23 and #24 Search rules and speed.** C4 adds normalisation and ranking with a precomputed search string; C5 adds a 150 ms debounce and a cap of 120 live rows.
- **#25 UnitCatalog behaviour.** C7 inventories the consumers, keeps the API shape, and pins a snapshot of the current 426 names.
- **#28 Older servers.** A5 tells 0.14 from 0.15 by the plugin version (NyarVersion), because the api level is 8 for both. Features are disabled with a reason on a known-older server.
- **#29 Diagnostics.** C8 adds one debug-log line for each failure kind, plus a generator summary.
- **#30 Commit boundary.** Step 0 reverts the known fix edits temporarily, commits and tags 0.73.0 with every path checked, re-applies the fix, and verifies by DLL hash.

Rejected or limited:
- Nothing was rejected outright. For #13, cross-editor correlation is deliberately not built: the refusal already shows where the start was issued, and readable text there is enough.

## Round 2 — Codex

The revision addresses most prior findings, but several material problems remain:

1. **Version gating fails open.** An unknown/unparseable version enables 0.15-only controls, which can send unsupported data to an older server.  
   **Fix:** Disable 0.15-only features unless a successfully parsed semantic version is `>= 0.15.0`, with an explicit override only if needed.

2. **Version discovery is underspecified.** `NyarVersion` cannot gate anything reliably unless the plan identifies where the remote plugin version is obtained, refreshed, and invalidated after reconnects.  
   **Fix:** Define the exact response/source, semver handling, connection lifecycle, and tests for missing, prerelease, malformed, 0.14.x, and 0.15.x versions.

3. **Configuration-help coverage remains incomplete.** B1 mentions approximately 35 cfg keys, but B4 tests only field specs, inputs, and sections; it never proves every rendered configuration control has prose or derived metadata.  
   **Fix:** Put cfg keys in an authoritative typed registry and assert every rendered cfg control has non-empty help and valid default/options metadata.

4. **The object-name design contradicts the generated schema.** C2 says runtime object names come from each blueprint’s managed name key, but C3 stores only a generic “localization key,” without explaining how blueprint keys are generated or found at runtime.  
   **Fix:** Add the blueprint localization key to generated rows or specify and test a GUID-to-live-prefab lookup that obtains it safely.

5. **“All world objects” is still not supported by the inclusion rule.** Excluding every other prefix by assertion does not prove that `Dynamic_*`, `Chain_*`, or another family contains no world props; `BlueprintData` only recovers buildables.  
   **Fix:** Audit excluded prefixes/components from the pinned dump and commit a report demonstrating that excluded prefabs are not world objects, or narrow the product claim.

6. **Overlapping object sets are ambiguous.** A `TM_*` prefab carrying `BlueprintData` belongs to two inclusion groups, yet “the sum of these groups is exact” does not state whether counts are disjoint or deduplicated.  
   **Fix:** Define set-union semantics, report both raw and deduplicated counts, and test that every GUID appears exactly once.

7. **Generated-data reproducibility still fails on machines without sibling repositories.** Preflight explicitly skips `--check`, allowing stale or manually altered generated data through CI and release packaging.  
   **Fix:** Make authoritative generator inputs available in CI/release validation or verify the committed file against a checked-in source manifest and digest without silently skipping.

8. **The prefab input provenance is incomplete.** Hashing only `prefab_names.tsv` and `PrefabNames.cs` does not authenticate the 23,535 prefab files from which components, flags, levels, and categories are extracted.  
   **Fix:** Record and verify a deterministic manifest hash covering every consumed prefab input.

9. **Search still omits equivalent GUID forms.** Rows store signed `int32`, but users may paste an equivalent unsigned decimal prefab ID; “exact GUID” will then miss it.  
   **Fix:** Parse numeric queries as signed or unsigned 32-bit bit patterns and normalize both to the canonical stored value.

10. **Grouping order is internally inconsistent.** C4 says “group, then page,” while C5 specifies independent 60-row expansion inside each group; these are different pagination models.  
    **Fix:** State that flat mode uses global paging and grouped mode uses per-group incremental limits, then test them separately.

11. **The 120-control cap has no defined enforcement.** Multiple groups may remain expanded and each can “Show 60 more,” so the stated cap can be exceeded or rows must disappear unexpectedly.  
    **Fix:** Define eviction/collapse behavior or use virtualization, and test multiple expanded groups and repeated “Show more.”

12. **All-type filter and grouping semantics are missing.** With the `All` tab selected, “faction” applies only to units and “category” only to objects, but the plan does not define combined grouping or which filters remain active.  
    **Fix:** Specify All-mode filter visibility, predicate behavior, and grouping—preferably first by kind and then by faction/category.

13. **The Beelzebub `PrefabGUID` browser is misleadingly incomplete.** It accepts “Any,” but this catalog intentionally excludes items, abilities, buffs, and many other prefab kinds that a generic GUID filter may accept.  
    **Fix:** Label it “Unit/Object catalog” and document the restricted coverage, or omit Browse there unless the accepted domain is verified to match.

14. **Library-pool command input lacks explicit validation.** The free-text pool row reuses validation, but the Pools library `Info <pool>` input is not said to validate names before constructing a command.  
    **Fix:** Run every pool-name command argument through the same pool-name validator before sending it.

15. **The full-catalog TSV parser has no malformed-data contract.** Missing columns, bad escapes, invalid integers, duplicate rows, or embedded separators could fail during UI opening or silently corrupt rows.  
    **Fix:** Define escaping and strict column validation, report a bounded diagnostic, and fail initialization gracefully with tests for malformed generated data.

16. **The Step 0 hash assertion may be impossible or misleading.** DLL equality depends on deterministic builds, identical toolchain/reference inputs, and the deployed DLL actually corresponding to the reconstructed source.  
    **Fix:** First verify deterministic-build prerequisites and record toolchain inputs; if byte identity is unavailable, compare assembly version plus a documented source/dependency fingerprint.

17. **Temporarily reverting known fixes in a dirty tree remains risky.** The plan does not specify a recoverable mechanism or verify that reverse application touches only those known hunks.  
    **Fix:** Preserve the fix as an explicit patch, validate its paths and hunks, commit only an reviewed allowlist, and verify the patch reapplies cleanly before tagging.

18. **A source-text scan is not a reliable UI coverage test.** It can pass for dead references, comments, or help keys never attached to rendered controls.  
    **Fix:** Expose registered help keys from UI construction metadata and compare those registries directly instead of scanning source text.

VERDICT: REVISE

### Claude's response (round 2)

Accepted:
- **#1 and #2 Version gating.**
  - A5 now names where the version comes from: the handshake's `plugin=` field, set at `NyarState.cs:158` and reset on disconnect at `NyarState.cs:269`.
  - `NyarVersion.AtLeast` fails closed, and an rc of 0.15.0 counts as 0.15.0.
  - It is tested for an empty value, a malformed one, 0.14, a 0.15 rc, 0.15 and 0.16.
- **#4 Object names.** C2 now specifies a runtime lookup by GUID: the prefab collection, then the managed blueprint data, then its LocalizationKey.
  - Nothing is stored in the rows, because the dump has no key for objects.
  - It is confirmed in-game before the code relies on it, with an explicit fallback.
- **#5 World objects.** This was a real gap. The dump shows that `Dynamic_*` and `Chain_*` are world props (barrels, crates, plants, stumps).
  - Both are now included, about 6,800 rows in all.
  - A committed report lists every remaining exclusion with its count and samples.
- **#6 Object counts.** Objects are taken as a union with each GUID counted once. Both the raw and the deduplicated counts are recorded, and a test checks that every GUID appears exactly once.
- **#7 Machines without the sources.** Raphael has no CI.
  - A logic test that always runs checks the table's sha256 against the header.
  - The full `--check` runs on this PC, the only machine that builds releases. Release preflight fails when the sources are missing.
- **#8 Provenance.** The header now carries a manifest hash over every prefab file the generator read.
- **#9 GUID forms.** Numbers are read as either signed or unsigned 32-bit values.
- **#10 and #11 Paging.** Flat mode pages globally. In grouped mode one group is open at a time, and its "Show more" stops at 240 rows, so the control cap holds.
- **#12 The All tab.** It shows only the kind-neutral controls and groups by kind first.
- **#13 The Beelzebub GUID field.** Its button now reads "Browse units & objects…", with a tooltip saying abilities and items aren't listed.
- **#14 Pool names.** The Info name is validated before any command is built.
- **#15 Malformed rows.** The table has no escaping, and the generator refuses tabs and newlines in fields. Every row must have exactly 8 columns; a bad row is skipped, counted and warned about once. A test covers this.
- **#16 and #17 Step 0.**
  - The DLL byte-identity claim is dropped, because builds aren't deterministic here. The commit instead has to build, pass the tests, and carry AssemblyVersion 0.73.0.
  - The revert works from copies saved under `fix/`. A diff must show only the known hunks, and the commit uses a printed allowlist of paths. After the restore, a diff against `fix/` must be empty.
- **#18 The coverage test.** It now checks two named helper call patterns with string-literal keys, in both directions, after stripping comments. The logic tests can't build Unity UI, so a registry of rendered controls isn't possible there; V3 covers the rendered result.

Rejected:
- **#3 Tooltips for cfg keys.** No Raphael window edits the server's .cfg, so there are no cfg controls to attach tooltips to. The keys are documented in the guide's GSettings block, which the guide tests already cover. My round-0 wording wrongly counted them as tooltip work, and B1 now corrects that.

## Round 3 — Codex

Most prior findings are addressed, but a few material issues remain:

1. **Step 0 may commit the scratchpad itself.** The `fix/` directory appears to be inside the repository, while the commit allowlist includes every changed path except only `.claudex/` and the plan directory.  
   **Fix:** Put backups in an OS temporary directory outside the worktree, or explicitly exclude and verify `fix/` before committing.

2. **The untracked reset file has no valid “pre-fix version.”** If `MainPanel.BcReset.cs` was created by the fix, reversing its edits still leaves a file that did not belong in 0.73.0; the plan does not say whether its pre-fix state is modification or absence.  
   **Fix:** Record each of the three paths as “restore prior content” or “remove entirely,” verify against the published source/package, and ensure the 0.73.0 commit contains the correct file state.

3. **Feature gating misses two castle switches.** A5 disables “the horde castle switch,” but A3 also adds the 0.15-only switch to Apocalypse and Infestation; those would remain usable against 0.14.x.  
   **Fix:** Gate `action.allowTerritory` for Apocalypse, Infestation, and HordeMarch, while leaving the pre-existing SpawnWaves control available.

4. **Resolving every object name on first open can freeze the game.** Approximately 6,800 main-thread prefab/managed-registry/localization lookups are performed before or during initial panel display, with no batching or latency limit.  
   **Fix:** Resolve only visible rows or process bounded batches per frame, cache results, and show fallback names immediately.

5. **The search index conflicts with runtime localization.** C4 precomputes search text once per row, but C2 replaces fallback display names with localized names after the panel opens; without reindexing, localized names cannot be searched and sorting may remain based on stale names.  
   **Fix:** Build or refresh each row’s normalized search text when its runtime name resolves, then invalidate the active query and stable sort.

6. **Live game-data access needs an explicit thread guarantee.** `PrefabCollectionSystem` and managed registry access generally must occur on the game’s main thread, while debouncing or asynchronous batching could accidentally move work off-thread.  
   **Fix:** Require all ECS/registry/localization access on the Unity game thread and keep background work limited to immutable string filtering.

7. **The exclusions report cannot substantiate “all world objects.”** Five samples per excluded prefix can conceal placeable props elsewhere in a large prefix, so the report does not actually validate the universal claim.  
   **Fix:** Have the report list every excluded prefab possessing object-like components or emit a complete machine-reviewable classification with explicit inclusion/exclusion reasons.

8. **`catalog-exclusions.md` is not included in reproducibility checks.** The generator writes this committed artifact, but `--check` is described as byte-comparing only `GameCatalog.Data.cs`; the report can therefore become stale.  
   **Fix:** Make `--check` regenerate and byte-compare both generated artifacts and include both in preflight.

9. **Malformed generated rows are tolerated too late.** The runtime parser skips bad rows, even though this is checked-in build-time data whose corruption should prevent release rather than silently ship an incomplete catalog.  
   **Fix:** Make logic tests and preflight parse every committed row strictly and fail on any error; retain runtime skipping only as a defensive fallback.

10. **The cap wording contradicts the event limits.** A4 calls `MaxTrackedUnits` the “only horde cap,” while A1 enforces a separate hard maximum of 500 expanded units per HordeMarch.  
    **Fix:** Describe `MaxTrackedUnits` as the global tracked-unit setting and separately explain the per-event 500-unit validation ceiling.

VERDICT: REVISE

### Claude's response (round 3)

I accepted all ten points:

- **#1 Where `fix/` lives.** It is in the session scratchpad, outside the worktree, and `git status` must not show it.
- **#2 What the reset file looked like in 0.73.0.** `MainPanel.BcReset.cs` was part of 0.73.0, as the reset card in the published DLL, so its pre-fix state is the earlier content, not absence. The changelogs are restored to their 0.73.0 text.
- **#3 Castle switches on older servers.** The gate now covers `allowTerritory` on Apocalypse, Infestation and HordeMarch. The SpawnWaves switch, which already existed, stays available.
- **#4 to #6 Name resolution.**
  - Names resolve in batches of 200 rows per frame on the Unity main thread, so no ECS or registry access happens off it.
  - The panel shows fallback names right away.
  - When a name resolves, that row's search text is rebuilt and a revision bump makes any open view run its query again.
  - A test checks that a late rename changes both the search hits and the sort.
- **#7 Exclusions report.** It now lists every excluded prefab that has an object-like component, in full, together with the component that flagged it. It no longer relies on samples.
- **#8 Reproducibility check.** `--check` now byte-compares both generated artifacts.
- **#9 Malformed rows.** Every committed row is parsed strictly in the logic tests, so a bad row blocks the release. At runtime they are still skipped, but only as a defensive fallback.
- **#10 Limit wording.** `MaxTrackedUnits` is described as the server-wide horde setting, and the 500 ceiling on each event as a separate check made at save time.

## Round 4 — Codex

The Round 3 plan addresses all prior material findings. The remaining implementation risks—batch-size tuning, live object-name API availability, and manual review of the exclusions report—have explicit fallbacks or verification gates and do not block implementation.

VERDICT: APPROVED

## Sign-off

The user signed off on 2026-10-24T16:30:47.789Z after round 4 (approved).
