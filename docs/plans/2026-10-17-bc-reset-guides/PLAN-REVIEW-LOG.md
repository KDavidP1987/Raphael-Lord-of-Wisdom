# Plan Review Log: Raphael 0.73.0 (Bloodcraft 1.13.26, admin full reset, complete guides)
Phases 0-1 (recon + interrogation) complete — plan locked with Kris in plan mode (2026-10-17). MAX_ROUNDS=5.

## Round 1 — Codex

Material problems remain:

1. **Command-count arithmetic is wrong:** the default reset is 28 + 16 + 10 + 8 + 1 = **63 commands**, not the UI’s stated 54; this undermines progress and ETA calculations.  
   **Fix:** Derive totals and progress exclusively from the built step list, never hard-code them in UI copy.

2. **Typed player names create parsing/injection risk:** the plan does not define quoting, escaping, normalization, or rejection of quotes, newlines, rich-text tags, and command delimiters.  
   **Fix:** Use Bloodcraft’s exact quoted-name grammar and centralize strict validation/escaping before constructing any command.

3. **Reply correlation is unsafe:** reply text has no request/run identifier, so matching output from another admin or command can advance the state machine incorrectly.  
   **Fix:** Match player, operation, and expected values wherever present, serialize Raphael captures through an exclusive lease, and surface ambiguous matches instead of consuming them.

4. **Timeouts permit late-reply corruption:** after a timeout, a delayed reply can match a later step—especially repeated prestige/set responses—and be attributed to the wrong command.  
   **Fix:** Add a late-reply quarantine/grace state or require uniquely attributable replies before advancing; mark non-correlatable timeouts as indeterminate.

5. **“Silence means nothing to reset” is not reliable:** silence can also mean packet loss, disconnect, throttling, insufficient processing time, or a changed server response.  
   **Fix:** Report silent prestige steps as “no reply/assumed empty,” track them separately, and abort on connection loss rather than counting them as confirmed no-ops.

6. **Capture exclusion has a race:** checking “another capture is armed” only at start does not prevent another subsystem from arming one during a long reset.  
   **Fix:** Introduce one centralized, disposable capture lease held for the entire run, with guaranteed release on stop, error, disconnect, and plugin shutdown.

7. **Lifecycle behavior is unspecified:** UI closure, scene changes, reconnects, target-server changes, plugin unloading, and exceptions could leave the runner active or chat interception installed.  
   **Fix:** Define cancellation and cleanup for every lifecycle transition and test that no command is sent after cancellation.

8. **Admin authorization is only described at UI entry:** admin status can change between opening the card, confirming, and later queued sends.  
   **Fix:** Revalidate connection and `IsLocalAdmin` at confirmation and before every send, aborting visibly if either changes.

9. **The operation is misleadingly called a “full reset”:** the plan admits classes, choices, familiars, and toggles cannot be reset, while `.wep rst`/`.bl rst` are delegated to the player.  
   **Fix:** Name it “Admin-resettable Bloodcraft progress” and show unresolved categories prominently in both confirmation and completion summaries.

10. **Partial failure semantics are incomplete:** generic `Error`, per-step timeout, optional-step failure, and changing system availability have no defined stop/continue policy.  
    **Fix:** Specify a policy for every classifier outcome and include succeeded, assumed, skipped, failed, and unknown steps in a retryable final report.

11. **No safe retry design exists:** rerunning after a partial reset may repeat prestige operations or other non-idempotent side effects.  
    **Fix:** Document idempotency per command and support retrying only failed/unknown steps after presenting the exact remaining plan.

12. **Level validation is underspecified:** “0 allowed” says nothing about negatives, overflow, server maximum, locale parsing, or empty input.  
    **Fix:** Parse invariant integer input, enforce Bloodcraft’s actual accepted range, and block confirmation with an inline validation error.

13. **Online-only familiar behavior can go stale:** picker-time online state and “familiar out” status may change before that late command executes.  
    **Fix:** Treat the familiar step as conditional at execution time and report “precondition not met” distinctly rather than generic failure.

14. **The guide accuracy assertion checks only a subset:** `guide commands ⊆ manifest` catches invented commands but allows the “complete” guide to omit any number of real commands.  
    **Fix:** Assert manifest-to-guide completeness too, with an explicit reviewed allowlist for intentionally hidden/internal commands.

15. **Argument accuracy remains a manual check:** command existence, aliases, and admin flags do not verify argument order, optionality, accepted values, or examples.  
    **Fix:** Extract usage/signature metadata into the manifest and validate structured guide arguments and examples automatically.

16. **Committed generated fixtures can silently become stale:** tests may pass forever against old manifests even when sibling source or target versions change.  
    **Fix:** Record source version/commit and generator version in each fixture, then make preflight regenerate and fail on any diff.

17. **The Emberglass design relies on an unproven wire-identity assumption:** equal `FullName` hashes do not alone prove compatible registration, serializer construction, field layout, or reflection delegate binding.  
    **Fix:** Add a real Emberglass integration test using its runtime API and serialized packet bytes, not merely a pure routing-seam test.

18. **Handler ownership remains order-dependent:** registering only when Eclipse is absent does not prevent another client plugin from registering the same packet type later and replacing Raphael’s handler.  
    **Fix:** Detect handler replacement or use an Emberglass-supported multiplexing/forwarding mechanism; otherwise expose a hard diagnostic that reception is unverified.

19. **Bridge status diagnostics overclaim success:** “Bloodcraft data: Emberglass” appears based on registration/loading rather than successful packet receipt.  
    **Fix:** Report separate states for detected, registered, first packet received, last valid packet time, MAC failures, and handler loss.

20. **The principal new transport path is explicitly not tested in game:** testing only “stats still flow over chat” does not validate the feature being added.  
    **Fix:** Require an Emberglass-on-both-sides integration test before release or defer the bridge from 0.73.0.

21. **Thread-safety is not addressed:** inbound chat interception and `CoreUpdateBehavior` may mutate the same run state from different callbacks.  
    **Fix:** Prove both execute on the Unity thread or marshal inbound events to a main-thread queue with synchronized ownership.

22. **Positive-match hiding still risks concealing important messages:** broad strings such as denial, “not enabled,” or player-not-found may belong to another operation.  
    **Fix:** Hide only fully correlated replies; display or duplicate ambiguous/error messages rather than consuming them.

23. **The test matrix misses critical races and lifecycle cases:** there are no tests for late replies, duplicate replies, unrelated matching replies, capture contention, disconnect, UI teardown, or admin revocation.  
    **Fix:** Add deterministic state-machine tests for each case before implementation approval.

24. **The universal nine-section rule conflicts with the locked “short Kindred guide” and general pages:** this will either pad pages with empty boilerplate or fail the proposed invariant.  
    **Fix:** Define explicit page schemas by guide type and test required sections per type rather than imposing one universal structure.

25. **“Every command” is not reconciled with aliases and dangerous/internal commands:** rendering all aliases as separate commands can create clutter, while exposing destructive/internal commands may be unsafe.  
    **Fix:** Establish a documented inclusion policy that groups aliases, flags destructive commands, and explicitly accounts for every manifest entry.

VERDICT: REVISE

### Claude's response (round 1)

Accepted (22): 1-16, 19, 21-25. Partly accepted (2): 17, 18. Rejected as stated (1): 20, with a reason. PLAN.md is revised.

**Accepted:**
- **1:** 63 commands, not 54 (my arithmetic error). Every count now derives from the step list.
- **2:** strict name validation (1-20 characters, no whitespace / quotes / `<` `>` `\` / control characters), sent unquoted to match Bloodcraft's exact case-insensitive match.
- **3, 4, 22:** reply correlation. A success must name the same player and type (and the value for `set`). Refusals that carry no name are attributed only to the step in flight, within its window. A late correlated reply updates its own earlier step and never advances the current one. Only correlated lines are hidden.
- **5:** silent prestige `r` is reported as "No reply — assumed nothing to reset", counted separately. A silent `set` is Unknown.
- **6:** instead of a capture lease, the reset tap runs first in `HandleInboundChat`, ahead of InterceptFlag and generic capture. Because correlation is per player and type, it needs no exclusivity with other captures, and another capture can't swallow its replies.
- **7:** lifecycle defined. Teardown on disconnect / server switch through `ClientBootstrapSystem_OnDestroy`; a panel close or rebuild does not stop the run; Stop is tested to send nothing afterwards.
- **8:** connection and `IsLocalAdmin` are rechecked before every send.
- **9:** renamed "Reset Bloodcraft progress", with the can't-reset list shown in the card and the summary.
- **10:** policy table covering every outcome.
- **11:** all commands are idempotent; "Retry failed and unknown steps" added.
- **12:** level is an invariant integer from 0 to the server's MaxLevel (from `ConfigsToClient`) or 1000; empty input blocks Start.
- **13:** familiar step gets its own PreconditionNotMet outcome.
- **14, 15, 16:** two-way manifest checks with an allowlist, a usage-syntax check with reviewed overrides, and fixture metadata. Release preflight regenerates the manifests and fails on any diff.
- **19:** diagnostic states that report receipt, not just registration.
- **21:** threading stated, with a main-thread check and a marshal fallback.
- **23:** race and lifecycle tests added.
- **24:** page schemas per guide type (full / short / general).
- **25:** command inclusion policy (aliases grouped, destructive commands flagged, machine API in its own block, allowlist with reasons).

**Partly accepted:**
- **17:** I verified Emberglass's serializer: System.Text.Json with CamelCase (`Serialization.cs:29-32,61,93`), and type ids are `Hash32(FullName)` (`Registry.cs:49`). Added an offline wire test that serializes with Emberglass's exact options and deserializes into Raphael's packet type, plus a FullName check. A unit test against the real Emberglass runtime isn't possible (IL2CPP game runtime), so the in-game check becomes a release-gate item.
- **18:** Emberglass offers no multiplexing or replacement event. Detection is through diagnostics instead: if registered with no packet within 60 s while chat data is also absent, a warning names "another mod may own this packet type". No reflection into Emberglass internals, which would be fragile.

**Rejected as stated:**
- **20 (defer the bridge):** the receiver activates only for players with the Emberglass client, who get no Bloodcraft data at all on opted-in servers today. Shipping either fixes them or changes nothing, and everyone else keeps the old path. The both-sides in-game check is offered to Kris as a release gate; if it isn't run, the changelog says "untested in game".

## Round 2 — Codex

Most prior findings are addressed, but several material issues remain:

1. **Valid player names containing spaces are rejected:** the new validation prevents resets for any legitimate multi-word V Rising character name, despite Bloodcraft potentially accepting the remainder of the command as one name.  
   **Fix:** Derive validation and argument handling from Bloodcraft’s actual command parser; support every valid player name while rejecting only characters that genuinely alter parsing.

2. **Quest refresh is incorrectly declared idempotent:** retrying an `Unknown` `.quest rf` may refresh or reroll quests a second time after the first command actually succeeded.  
   **Fix:** Classify quest refresh as non-idempotent and never automatically retry an unknown quest step without a separate explicit warning and confirmation.

3. **Late nameless refusals can still corrupt the current step:** only late successes are correlated historically; a delayed `"X is not enabled"`, `[denied]`, invalid-value response, or familiar error is attributed to whichever step is currently in flight.  
   **Fix:** Add a reply-drain/quarantine period after every timeout or stop advancing when an uncorrelatable command times out.

4. **Capture exclusivity remains unresolved:** placing the reset tap before generic capture protects correlated reset replies, but reset commands and uncorrelated responses can still interfere with an already armed generic capture.  
   **Fix:** Acquire a centralized exclusive command/capture lease for the complete reset run and reject new captures until it releases.

5. **The reflection-only Emberglass call is underspecified:** `Action<User,T>` names an Emberglass-owned `User` type, which cannot be referenced statically while retaining a genuinely optional compile-time dependency.  
   **Fix:** Specify and test the exact runtime generic-method lookup and compatible delegate construction used without referencing any Emberglass type at compile time.

6. **The Emberglass tests still omit the riskiest operation:** JSON and FullName tests do not prove that reflection registration succeeds against the real Emberglass assembly or that its dispatcher invokes Raphael’s delegate.  
   **Fix:** Add an integration test loading the pinned Emberglass assembly, registering the reflected handler, and invoking its registry/dispatch path with serialized packet bytes.

7. **Last-handler replacement is only diagnosed after functionality is lost:** another registration can still overwrite Raphael, while a 60-second no-data warning cannot distinguish handler loss from an idle server or disabled bridge.  
   **Fix:** Inspect the registry to verify continuing ownership when possible, and diagnose bridge configuration, registration ownership, and traffic inactivity as separate states.

8. **The actual Emberglass path remains optional for release validation:** “Kris decides” still permits shipping the new transport feature with no end-to-end execution.  
   **Fix:** Make the Emberglass-on-both-sides test mandatory for shipping the bridge, or feature-gate/defer it when the environment is unavailable.

9. **Release preflight still accepts unverifiable stale manifests:** when sibling repositories are absent, it merely warns and then treats committed fixtures as authoritative.  
   **Fix:** In release mode, fail unless pinned source snapshots/repos are available and regeneration is clean; reserve warning-only behavior for local non-release runs.

10. **Usage normalization erases argument semantics:** treating `[x]`, `(x)`, and `<x>` alike can hide differences between optional, required, and convention-specific arguments.  
    **Fix:** Preserve requiredness and ordering in structured manifest fields and normalize only cosmetic syntax proven equivalent for that mod.

11. **“Complete guides” validates commands but not configuration:** there is no equivalent completeness check for configuration keys, defaults, restart requirements, or documented concepts.  
    **Fix:** Generate configuration manifests from each mod’s config definitions and require every public setting to appear or have a reasoned allowlist entry.

12. **Zero-step and degenerate plans are unspecified:** users can apparently disable every core system and optional step, producing an empty reset with confirm/progress/retry behavior undefined.  
    **Fix:** Block confirmation when the generated plan is empty and test that validation path.

13. **Historical late-success mutation can produce a misleading final report:** a step already reported or retried as unknown may later flip to success without clearly reconciling UI counts or suppressing a now-unnecessary retry.  
    **Fix:** Define immutable run-finalization boundaries and remove late-resolved steps atomically from the retry plan while visibly recording the reconciliation.

VERDICT: REVISE

### Claude's response (round 2)

Round 2 arbitration (Claude):

1. PARTIAL. Names with spaces are allowed and sent double-quoted (VCF's splitter treats a quoted run as one argument). Only `"`, `\`, control chars/newline and `<`/`>` are refused. Unit-tested against a port of VCF's splitter.
2. ACCEPT. quest rf marked non-idempotent; an unknown quest rf is excluded from Retry unless the admin ticks "re-roll again" (with a warning).
3. ACCEPT. "X is not enabled." is correlated by text to its system. "Couldn't find player" and [denied] stop the run regardless of attribution. Other nameless refusals get a 1.5 s drain after any non-prestige timeout and are attributed to the timed-out step, never a later one.
4. PARTIAL. Other Bloodcraft Admin forms are disabled during a run. No global capture lease: reset commands never arm capture, the reset tap runs first so correlated replies are never lost, and a lease would block every other mod's reads for a minute or more.
5. REJECT. The delegate's User is ProjectM.Network.User (a game type Raphael already references), not an Emberglass type; the exact reflection call (GetMethods/Single/MakeGenericMethod/Invoke) is in the plan.
6. REJECT. Emberglass is an IL2CPP BepInEx plugin; its runtime can't be loaded in the net6 test host. The wire format (FullName hash, camelCase JSON) is covered offline.
7. PARTIAL. Diagnostics are already neutral and split (registered / no data yet / chat data). Registry is internal; not inspected by reflection.
8. PARTIAL. The bridge is now opt-in (UseEmberglassBridge, default false), labelled experimental in the release notes unless Kris runs the both-sides test. Nobody gets the untested path without choosing it.
9. ACCEPT. Release preflight fails unless sibling repos are present and regeneration is clean; Commit mode warns.
10. ACCEPT. Manifest params come from method signatures (name, type, optional = has default); guide args checked for names, order, requiredness.
11. ACCEPT. Config manifests generated; every public key appears in the guide with its default or is allowlisted with a reason.
12. ACCEPT. Empty plan disables Start; tested.
13. ACCEPT. Late replies update records after finish with a visible "late confirmation" line; Retry list is recomputed at press time.

## Round 3 — Codex

Most prior findings are now addressed. Remaining material problems:

1. **Generic refusals can falsely stop the reset:** `[denied]` or `"Couldn't find player"` from another concurrently issued command can be mistaken for the reset’s response; attribution does matter.  
   **Fix:** Block all command-producing actions while a reset step is awaiting a nameless response, or treat uncorrelated generic refusals as ambiguous instead of stopping the run.

2. **The fixed 1.5-second drain does not guarantee isolation:** a response arriving after the drain can still be attributed to a later step.  
   **Fix:** After an unconfirmed non-prestige command, pause the run for explicit admin continuation or use a proven upper-bound timeout before sending another command that accepts indistinguishable replies.

3. **Completed runs accept late replies indefinitely:** a future unrelated command with matching player/type/value could mutate an old summary and retry list.  
   **Fix:** Close the reply-correlation window after a bounded reconciliation period and unregister completed runs from the inbound tap.

4. **The manifest’s raw C# parameter list may include framework-injected parameters:** VCF command methods commonly contain context/user parameters that are not command-line arguments.  
   **Fix:** Define extraction rules for framework-injected parameters and validate the generated manifest against representative commands from every supported command framework.

5. **The name-validation test specification contradicts the implementation:** the tests list “whitespace” as invalid while names containing spaces are explicitly supported.  
   **Fix:** Distinguish whitespace-only, leading/trailing normalization, forbidden whitespace characters, and valid internal ASCII spaces in the test cases.

6. **Emberglass remains releasable without exercising the reflection registration path:** format and routing tests do not catch lookup, delegate-binding, or invocation failures. Default-off limits impact but does not validate the advertised feature.  
   **Fix:** Make one manual Emberglass-on-both-sides registration-and-receipt test mandatory before advertising the bridge as supported; otherwise label it experimental everywhere and exclude it from compatibility claims.

7. **Generated config fixtures are omitted from the critical-file inventory:** the plan introduces `*-config.json` files but does not list them among new artifacts.  
   **Fix:** Add `tests/fixtures/*-config.json` to the critical files and package/review scope.

VERDICT: REVISE

### Claude's response (round 3)

Round 3 arbitration (Claude):

1. ACCEPT (reframed). Note: VCF replies go only to the sender (ctx.Reply), so another admin's lines never reach this client; the only source is this client's own sends. "Couldn't find player" / [denied] now PAUSE the run (line shown; Stop default / Continue) instead of stopping, so a stray line from another Raphael tab or typed command can't silently kill or mis-advance it.
2. ACCEPT. The fixed drain is replaced: any non-prestige timeout pauses the run (Retry this step / Skip→Unknown / Stop) with nothing sent until the admin chooses. Silent prestige steps don't pause (silence is their normal no-op; their only refusal is system-level, text-correlated).
3. ACCEPT. 30 s reconciliation window after finish; then the run is sealed and unregistered from the tap.
4. ACCEPT. Leading ChatCommandContext/ICommandContext parameter dropped; generator fails on a [Command] method without one. Verified all six sibling mods use VCF (Nyarlathotep via ChatCommandContext). Added: verb-dispatch commands (all-optional strings, e.g. Nyar api) are flagged verbDispatch and carry a SyntaxOverride; generator self-tests on representative commands per mod.
5. ACCEPT. Test cases split: whitespace-only refused, trim, internal ASCII space accepted+quoted, tab/control refused.
6. ACCEPT. Unless the both-sides check passes, "experimental" everywhere (setting, changelog, README, guide) and excluded from the compatibility claim.
7. ACCEPT. tests/fixtures/*-config.json added to critical files.

## Round 4 — Codex

The prior findings are substantially addressed, but two material gaps remain:

1. **The timeout UI bypasses the non-idempotent quest safeguard:** every non-prestige timeout offers “Retry this step,” so a timed-out `.quest rf` can be rerun immediately without the warning required by the later bulk-retry policy.  
   **Fix:** For an unknown `.quest rf`, replace “Retry this step” with “Re-roll quests again” behind the same explicit warning and confirmation used by bulk retry.

2. **Runtime behavior of the Emberglass setting is undefined:** the UI tells users to turn the setting on, but the plan does not say whether registration happens immediately, on reconnect, or only after restart; disabling is also problematic if Emberglass exposes no unregister API.  
   **Fix:** Make the setting explicitly restart/reconnect-required, or define tested live registration and state clearly that disabling cannot take effect until reconnect/restart.

3. **Paused runs still accept ambiguous replies from unrelated local commands:** while paused after a timeout, any matching nameless `"Invalid …"` or familiar error is assigned to the timed-out step, even though the user can issue commands elsewhere or manually in chat.  
   **Fix:** During a timeout pause, accept only positively correlated successes/system errors; display nameless replies as ambiguous and require the admin to choose Retry, Skip, or Stop.

VERDICT: REVISE

### Claude's response (round 4)

Round 4 arbitration (Claude):

1. ACCEPT. A timed-out quest rf offers "Re-roll quests again" behind the same warning + two-step confirm instead of plain "Retry this step".
2. ACCEPT. UseEmberglassBridge is read once at Plugin.Load; restart required to apply (Emberglass has no unregister). Description, Settings row and a Connection-tab "restart to apply" line when stored != loaded value; the hint now says "turn on … and restart the game".
3. ACCEPT. During a timeout pause only positively correlated lines (same-player/type success, system-off text) change the step and end the pause; nameless refusals are shown as "Possibly related: …" and attributed to nothing. Tests updated.

## Round 5 — Codex

The prior material findings are addressed. The remaining risks are explicitly bounded, surfaced to users, and covered by proportionate automated and manual release gates. The plan is sound enough to implement.

VERDICT: APPROVED

## Sign-off

The user signed off on 2026-10-17T20:59:59.072Z after round 5 (approved).

## Post-build inspection — round 1 (fresh read-only Codex session)

## Findings

- **High — `Services/MessageService_Processing.cs:1070`, `Services/Bloodcraft/BcResetRunner.cs:67`, `Services/Bloodcraft/BcReset.cs:375`** — The reply tap receives only message text and never verifies that the inbound line is a server/VCF reply rather than player-authored chat, so another player can spoof any positively correlated reset response.  
  **Scenario:** While Sword expertise is awaiting a reply, another player sends `Sword expertise set to [0] for Bob`; Raphael hides the chat line, marks the step successful, and advances even though Bloodcraft never confirmed it. The same attack can inject a system-off line or a nameless `Invalid...` response to skip/fail work.

- **High — `Services/Bloodcraft/BcResetRunner.cs:121-122`** — A step is marked sent and its timeout starts when its command is queued, not when `ProcessAllMessages` actually transmits it, and stopping the run does not remove commands already queued in `MessageService`.  
  **Scenario:** A message backlog delays `.quest rf Bob` for over four seconds, causing an Unknown pause before the first command is transmitted; the admin retries or stops, but the queued command later goes out anyway, potentially re-rolling quests after cancellation or producing a duplicate reroll. This violates both one-command-on-the-wire correlation and “no send after cancel.”

- **Medium — `Services/Emberglass/EmberglassBloodcraftBridge.cs:94-117`** — The off-thread fallback mutates `CoreUpdateBehavior.Actions` from the off-thread callback itself, so it is not actually a thread-safe marshal onto the Unity update loop.  
  **Scenario:** If Emberglass ever dispatches on a worker thread—as this safeguard anticipates—`EnsureDrain` concurrently adds to the action list while the main thread enumerates it, risking an enumeration exception, lost registration, or list corruption.

- **Medium — `Services/Emberglass/EmberglassBloodcraftBridge.cs:112-117`** — Once the off-thread drain is installed it is never removed, leaving a permanent per-frame action for the rest of the game session.  
  **Scenario:** A single off-thread packet installs `_drain`; after its queue empties, the delegate continues running every frame through server switches and subsequent sessions within the process.

- **Medium — `Services/Bloodcraft/BcReset.cs:435-459`** — System-off replies are applied to every pending/unknown step in that system regardless of whether that system currently has a command in flight, while the originating timed-out/assumed step is not updated.  
  **Scenario:** During a weapon step, the admin manually tries a prestige command and receives `Prestiging is not enabled.`; the reset silently marks all pending prestige work system-off even though the line did not answer its current command. Conversely, a late `Prestiging is not enabled.` after the final prestige step was assumed empty touches no step and leaves the report incorrectly saying “nothing to reset.”

- **Medium — `Patches/InitializationPatch.cs:133`, `Services/Bloodcraft/BcResetRunner.cs:86-90`** — Disconnect/server-switch teardown stops the run and then immediately discards `Current`, so the required visible stop reason and partial report cannot reattach after the panel/world rebuild.  
  **Scenario:** The connection drops at step 40; after reconnecting, the reset card shows a fresh form rather than “Stopped: you left the server” and the 39-step outcome report.

- **Low — `UI/ModContent/MainPanel.Beelzebub.cs:4689-4697`** — The Emberglass opt-in hint is keyed to `RegistrationGaveUp`, not “no Bloodcraft data 60 seconds after registration,” and the connection UI does not separately identify whether valid Bloodcraft data is arriving over chat as required by PLAN §1.  
  **Scenario:** The hint may appear only after registration retries give up—or not appear when registration succeeds but packets never arrive—while the admin cannot distinguish chat transport from Emberglass transport.

- **Low — `UI/ModContent/MainPanel.cs:8582-8787`** — V Rising Basics and Game Guide remain bespoke builders rather than using the new guide model/header/section kit, deviating from PLAN §3’s requirement that all general pages use the kit.  
  **Scenario:** Those pages retain a different rendering path and are excluded from the guide schema/text lint coverage that protects the converted pages.

- **Low — `tests/Raphael.Logic.Tests/GuideTests.cs:143-195`** — The guide accuracy gate never verifies that commands required to be marked destructive actually carry `Destructive = true`.  
  **Scenario:** A guide can document a purge, delete, wipe, reset, or acknowledgement command without its destructive warning and still pass Release preflight.

No additional material issue was apparent in name quoting/validation, level validation, Exo-only skipping, ordinary pause/retry transitions, late-success removal from retry lists, destroyed-card detection, or reset-card reattachment while remaining in the same world. Player-derived text rejects `<`/`>`, and server-derived lines shown in the card are generally wrapped with `GuideText.Escape`, so I found no separate rich-text-injection path beyond the more serious reply-origin spoofing above.

**Overall assessment:** The guide work is largely well-gated, but the reset feature should not ship until inbound reply provenance and queued-command cancellation/transmission timing are fixed.
### Claude's arbitration
1. **Reply spoofing by another player — rejected (false positive).** Only `ServerChatMessageType.System` lines reach `HandleInboundChat` (`Patches/ClientChatPatch.cs:226`, the `continue` for every other type). Player chat (Local/Global/Team/Whisper) never reaches the tap, and a player can't author a System message. Only a server-side mod could, which is the same trust level as the server running the commands.
2. **Timeout starts at queue time; Stop leaves queued commands — rejected (false positive).** `EnqueueMessageSilent` ends in `SendMessage`, which creates the `ChatMessageEvent` entity the same frame (`Services/MessageService.cs:138-166, 261`). `OutputMessages` is never enqueued anywhere (`ProcessAllMessages` documents it as a no-op hook), so there is no backlog and nothing left to cancel.
3. **Off-thread drain mutates `CoreUpdateBehavior.Actions` from the worker thread — accepted.** The drain is now installed once, on the main thread, right after registration. An off-thread packet only touches the `ConcurrentQueue` (plus a one-time warning guarded by `Interlocked.Exchange`).
4. **Drain never removed — accepted as resolved by 3.** It now exists exactly as long as the registration (the session, restart-to-apply by design) and costs one empty-queue check per frame.
5. **System-off applies regardless of the step in flight — split.** First half rejected: "X is not enabled." is a server-wide fact, so skipping the rest of that system is correct whatever command produced it. Second half accepted: `AssumedEmpty` steps are now corrected to `SystemOff` by a late "not enabled" (test "late off").
6. **Teardown discards the report — accepted.** New `BcResetRun.Detach`: it stops, seals at once (no line from the next server can touch it) and sets `RetryAllowed = false` (Retry could otherwise reach a same-named player on another server). `OnWorldTeardown` keeps `Current`; the card adds "Retry isn't offered after leaving the server." Tests "detach: *".
7. **Emberglass hint keyed to `RegistrationGaveUp` — rejected.** With the bridge off on an Emberglass server, the ack (first ProgressToClient) never arrives over chat, so registration giving up IS the "no Bloodcraft data" signal. When data arrives over Emberglass it goes through `TryHandleServerMessage`, which clears the give-up flag (`EclipseProtocolService.cs:278`). Transport is visible: the bridge line says "receiving (N packets…)" when Emberglass carries the data; otherwise it is chat.
8. **V Rising Basics / Game Guide not on the guide kit — deferred (logged deviation).** Fixed in place this release (stacked headings, duplicate Scholar, mod pointers, Nyarlathotep / Castle Events). Both are base-game reference pages with no mod commands, so the manifest gate has nothing to check. Converting them to the kit is a follow-up and will be raised with Kris.
9. **No test that destructive commands carry `Destructive` — accepted.** New GuideTests check (e): a row whose verb is purge / delete / wipe / reset / kill / destroy / forcepurge must be `Destructive`, with `.fam reset` exempt (the guide states it deletes nothing). Reversible admin edits (remove, ban, kick, clear) aren't covered. Found and fixed: `.uriel unit kill` and `.uriel objcfgglobal clear` are now marked Destructive.

Gates after fixes: logic tests 9258 passed / 0 failed; mod build 0 errors, 4 warnings (baseline).

## Post-build inspection — round 2 (re-inspection, fresh read-only session)

### Findings

- **Medium — `Services/Bloodcraft/BcResetRunner.cs:122-124`, `Services/MessageService.cs:261-280`** — The run marks a step `Sent` before `SendMessage` succeeds, while `SendMessage` swallows entity-creation exceptions; a failed send therefore becomes a misleading timeout/Unknown.  
  **Fix:** Make the immediate-send API return success and begin the reply window only after successful event creation.

- **Low — `Services/Emberglass/EmberglassBloodcraftBridge.cs:94-103`** — `packet?.Message` may be null, but `ConcurrentQueue<string>.Enqueue(null)` throws on the off-thread path; the main-thread path accepts the same input.  
  **Fix:** Ignore null packets/messages before either dispatch path.

### Accepted fixes

- **Off-thread drain:** Correct and complete. Worker callbacks now touch only the concurrent queue; registration of `_drain` occurs on the main thread.
- **Drain lifetime:** Acceptable by stated process-session registration design; the residual cost is one empty dequeue check per frame.
- **Late system-off correction:** Correct. `AssumedEmpty` steps are converted to `SystemOff`, including the originating prestige step, without advancing an unrelated current step.
- **Teardown/report retention:** Correct. `Detach` stops, seals, preserves the report and reason, and prevents cross-server retries. UI and tests cover the behavior.
- **Destructive guide gate:** Correct for the declared command grammar and exemption. The added verbs cover the stated destructive classes.

### Rejected/deferred findings

1. **Player reply spoofing — Agree with rejection.** Only `ServerChatMessageType.System` reaches the reset tap; ordinary player chat cannot produce that type.

2. **Queued-command backlog/cancellation — Agree with rejection.** `EnqueueMessageSilent` sends immediately and the legacy queue is not used. The separate send-failure issue above remains.

5. **System-off scope — Agree with rejection.** “System not enabled” is server-wide configuration evidence, so applying it across that system is reasonable.

7. **Emberglass hint/transport — Agree, with a wording caveat.** A valid config acknowledgement clears `RegistrationGaveUp`, and Emberglass packet counts identify that transport. The cited code clears the flag on `ConfigsToClient`, not merely the first `ProgressToClient`.

8. **Guide-kit conversion — The evidence still holds.** The two pages remain bespoke, so this is a genuine logged/deferred PLAN deviation; the “no mod commands” rationale reduces its practical severity but does not refute it.
### Claude's arbitration
- **Send failure reads as a timeout — accepted.** `SendMessage` and `EnqueueMessageSilent` now return whether the chat event was created; existing callers ignore it. On a failed send the runner calls the new `BcResetRun.SendFailed`: the step becomes Failed ("Couldn't be sent"), so Retry offers it, and the run stops with "Stopped: a command couldn't be sent." Test "send failed: *".
- **Null packet message throws on the off-thread queue — accepted.** A null message becomes "", which `Deliver` already counts as an empty packet.
- Wording caveat on 7 noted: the give-up flag clears on a valid ConfigsToClient. The conclusion is unchanged.
- Item 8 stays a logged deviation, to be raised with Kris.

Inspection closed at MAX_INSPECTION_ROUNDS=2. Gates: logic tests 9261 passed / 0 failed; mod build 0 errors, 4 warnings (baseline).

## Round 1 — Codex

Material issues found:

- **Soulshard durability has no committed behavior.** “Use a decimal helper if one exists; otherwise log it” permits knowingly shipping the broken 0/1 control.  
  Fix: Require implementation of a validated decimal field supporting `0.0–1.0`, or explicitly remove/defer the control.

- **The quoting design is incomplete and potentially injectable.** Rejecting only `"` does not address backslashes, CR/LF, tabs, or other characters with VCF/chat-parser semantics; a trailing backslash may escape the generated closing quote.  
  Fix: Define one tested VCF argument encoder that escapes every parser-significant character and rejects all command separators/control characters.

- **Blank values are underspecified after quoting.** The plan says free-text values containing spaces are quoted, but does not define trimming, whitespace-only input, or whether validation happens before or after encoding.  
  Fix: Normalize and validate the raw value first, reject empty/whitespace-only input, then encode exactly once.

- **Changing `FormBuilder.HandleSubmit` is a high-blast-radius global change.** Existing forms may depend on raw remainder-of-line arguments or already supply quoted values, so automatic quoting can silently change unrelated commands.  
  Fix: Make argument encoding an explicit per-field/per-command policy and regression-test every existing free-text form.

- **The command accuracy test can pass while the UI remains wrong.** Invocation plus argument-count checks do not prove that buttons/forms actually use the extracted templates, that argument order is correct, or that optional arguments and fixed literals match.  
  Fix: Test every rendered Kindred action against its template and manifest, including ordered argument types, optionality, and required literal tokens.

- **Fixture authority and versioning are absent.** Stale or manually edited JSON manifests would bless incorrect commands, particularly since this release changes many upstream integrations.  
  Fix: Record the upstream mod/version and fixture-generation source, regenerate fixtures reproducibly, and fail preflight when provenance or version differs.

- **Admin gating is treated as security without specifying enforcement.** `BeginAdminGate` appears to disable UI, but client-side greying cannot prevent direct command submission or stale callbacks.  
  Fix: State that server-side authorization remains authoritative and add tests proving non-admin submissions are rejected, not merely disabled visually.

- **Gate lifecycle is not covered.** Adding two `BeginAdminGate` calls without explicit matching scope/end behavior risks leaking the disabled state into later tabs or leaving descendants active.  
  Fix: Specify balanced gate boundaries and test both admin/non-admin render trees for enabled-state leakage.

- **Phase cycling is vulnerable to stale state and rapid clicks.** Computing `next` from cached client phase can submit the same phase repeatedly or overwrite a newer server state.  
  Fix: Prefer an atomic server-side “advance phase” command; otherwise update from acknowledged state and disable/debounce the button while a change is pending.

- **The Nyarlathotep changes are internally contradictory.** Making the editor permanently untinted removes its `Tint`, while changing `NyarClear` globally to preserve any child named `Tint` is unnecessary for that host and can preserve stale or unrelated objects elsewhere.  
  Fix: Remove the global name-based exception, or clear only a dedicated content container while leaving the host’s decoration outside it.

- **The Nyarlathotep guard relies on a non-unique object name.** `"Tint"` is a conventional name, not a stable ownership/type boundary; preserving by name can cause duplicate decorations or leaked children on rebuild.  
  Fix: Keep persistent decoration in a separate named container/reference and clear only explicitly owned dynamic content.

- **Guide “1:1” migration conflicts with editorial changes.** Adding purpose lines, folded details, new callout semantics, and “impeccable” restructuring is not a 1:1 conversion and makes omissions difficult to detect.  
  Fix: Define whether semantic equivalence or verbatim parity is required and add a content inventory test/checklist covering every source heading, paragraph, bullet, and link.

- **`GLinks` lint is too narrow.** Checking label glyphs and an `https://` prefix misses malformed URLs, unsafe schemes hidden by whitespace/case, duplicate labels, and empty targets.  
  Fix: Parse URLs with `Uri.TryCreate`, require absolute HTTPS, reject credentials/control characters, and test empty and malformed labels/targets.

- **Deletion and `.gitignore` verification are missing.** Adding `tools/__pycache__/` may only match the repository-root directory and not nested Python caches; the plan also does not verify that `resp1-4.md` is actually untracked before deletion.  
  Fix: Use `**/__pycache__/` or the repository’s established pattern, and verify the deletion target’s exact path and Git status before removing it.

- **The release sequence tests a materially different state after commit.** Preflight is rerun after push, but any failure then requires another commit and push, invalidating the already-verified remote equality and package assumptions.  
  Fix: Run the full commit-sensitive preflight locally on the final commit before pushing, then verify remote SHA and package that exact SHA.

- **No rollback or artifact identity check exists for dual-DLL deployment.** A partial copy can leave the two destinations on different builds, and “game closed” does not prove both files match the committed artifact.  
  Fix: Back up both DLLs, deploy atomically where possible, and verify hashes/version metadata for the built artifact and both destinations.

- **Several acceptance checks remain subjective.** “Rows have no gaps,” “danger-zone text is right,” and “render on the kit” are not reproducible assertions and could regress immediately.  
  Fix: Add concrete UI snapshot/layout assertions where feasible and exact text/action tests for every changed control.

VERDICT: REVISE

### Claude's response (round 1)

Round 1 arbitration (PLAN-ADDENDUM.md revised):

- **Accepted:**
  - Soulshard durability now has a `DecimalField` (0.0-1.0).
  - Quoting is now an opt-in `Quote` flag, set per field, with one encoder: trim, refuse empty, refuse `"`,
    backslash and control characters (the trailing-backslash case), quote only on whitespace, validate raw and encode
    once. This covers points 2, 3 and 4. FormBuilder's global behaviour is unchanged.
  - Accuracy gate: the UI references the tested constants; the test checks the order and count of placeholders and
    which arguments are required or optional, plus literal tokens.
  - Phase: about 1.5 s of debounce. There is no atomic advance command.
  - Nyar: the name-based guard is dropped (points 10 and 11).
  - Guides: semantic equivalence is defined; a content inventory checklist plus a URL and section-count test.
  - GLinks lint uses `Uri.TryCreate`: absolute https, no user info, no control characters, non-empty label.
  - `**/__pycache__/`; the resp files are verified untracked.
  - Release preflight runs on the release commit before pushing; the package is that SHA.
  - Back up the DLLs before deploying, then verify hash and version at both paths.
- **Rejected:**
  - Fixture provenance (6): the manifests already carry source path and version, and Release preflight 10b fails
    when they are stale (it caught a stale Nyarlathotep sourceCommit today).
  - Admin gate as security (7): the plan makes no security claim. The server enforces admin, as the code comment
    states. A non-admin rejection test would test the server mod, not Raphael.
  - Gate lifecycle (8): `BeginAdminGate` returns a self-contained CanvasGroup root for the tab, with no matching end
    call, so nothing can leak.
  - UI layout snapshots (17): Unity IL2CPP UI has no headless test harness here. Exact action strings are tested;
    layout goes to Kris's in-game checklist.

## Round 2 — Codex

Most prior findings are addressed. Remaining material gaps:

- **Quoting only detects ASCII space.** If VCF tokenization recognizes other Unicode whitespace, values containing non-breaking or other separator characters can still split into multiple arguments.  
  Fix: Base the quote decision on the exact `GetParts` separator rules, and test representative Unicode whitespace.

- **Tests do not prove that `Quote` is enabled on every intended field.** The encoder and command constants can pass independently while a Kindred form forgets the opt-in flag and still breaks player/clan names containing spaces.  
  Fix: Add form-schema tests asserting `Quote=true` for every string-token field that permits spaces and `false` for fields that must remain raw.

- **The phase debounce is local and time-based, not state-based.** Separate main-panel and overlay controls can bypass each other’s cooldown, while server synchronization taking longer than 1.5 seconds can still calculate the same next phase.  
  Fix: Use one shared in-flight phase controller across both surfaces and clear it only after observed phase change, failure, or a bounded timeout.

- **`DecimalField` has no specified tests despite introducing parsing and range logic.** Culture handling, inclusive limits, excessive precision, malformed input, and invariant serialization are easy regression points.  
  Fix: Add tests for both bounds, out-of-range values, comma/dot cultures, malformed input, whitespace, precision policy, and emitted invariant text.

- **The guide inventory remains mostly manual.** Testing only four URLs plus section/detail counts will not catch a missing or altered paragraph, heading, or bullet despite the semantic-equivalence requirement.  
  Fix: Check a normalized content inventory in tests or store and compare an approved migration manifest covering every heading, paragraph, bullet, and link.

- **Deployment backups are underspecified.** Fixed `.bak` files can overwrite the only known-good backup from an earlier deployment, and the plan does not verify backup hashes before replacement.  
  Fix: Create timestamped or SHA-named backups of each destination and verify them before copying the new DLLs.

VERDICT: REVISE

### Claude's response (round 2)

Round 2 arbitration:
- **Unicode whitespace: rejected, with a pinning test.** VCF `Utility.GetParts` splits only on ASCII ' ' outside
  quotes (`VCF.Core/Common/Utility.cs:48`), so other whitespace never splits a token. A test pins the rule.
- **Accepted:**
  - Quote opt-in is declared in `KindredAdminCommands` (`IsQuoted`) and checked against the manifest's parameter types
    (string means quoted; everything else raw). The forms read that declaration.
  - Phase: one shared in-flight lock in `BeelzClient`, used by both the panel and the overlay, cleared when the
    observed phase changes or after 3 s.
  - DecimalField: pure `VcfArg.TryDecimal`, with the listed tests.
  - Guide migration: an automated normalized inventory fixture, with every entry asserted; deliberate rewordings are
    listed with a reason.
  - Timestamped backups, verified against their source hash.

## Round 3 — Codex

The revised plan addresses the prior material findings: tokenizer-compatible quoting, declarative quote coverage, shared phase locking, decimal parsing tests, automated guide inventory, and verified timestamped backups.

One minor documentation omission: the critical-files list should include the file defining `BeelzClient` and `BeelzTransformOverlayPanel.cs`, since both must change for the shared phase lock. The implementation section already requires those changes, so this is not material enough to block implementation.

VERDICT: APPROVED

## Sign-off

The user signed off on 2026-10-17T22:15:53.555Z after round 3 (approved).

## Addendum post-build inspection — round 1 (fresh read-only Codex session)

- **High — `Raphael/Services/Kindred/KindredAdminCommands.cs:181`**  
  Scenario: `RenameSelf` and `RenamePlayer` have string `newName` parameters but are absent from `Quoted`; renaming to `The Count` sends `.rename The Count` as multiple VCF arguments, and the manifest test should fail its “every string parameter is quoted” assertion.  
  Fix: add `[RenameSelf] = new[] { "newName" }` and `[RenamePlayer] = new[] { "newName" }` to `Quoted`.

- **Medium — `Raphael/Services/VcfArg.cs:21`**  
  Scenario: forbidden control characters at either end are removed by `Trim()` before validation, so values such as `"name\n"` or `"\tname"` are accepted instead of producing `Invalid: <label>`, contrary to the raw-value-first validation requirement.  
  Fix: scan `raw ?? ""` for quotes, backslashes, controls, and Unicode line separators before trimming it.

- **Low — `Raphael/Services/Guides/BloodcraftGuide.cs:40`**  
  Scenario: the quick-start guide still says changing a class “is typed in chat,” even though this change adds a dedicated **Change class (.class c)** form. The same stale direction remains at line 654.  
  Fix: direct users to the Class tab’s Change class form at both locations.
### Claude's arbitration
- **Rename `newName` not quoted — rejected (false positive).** Its type is KindredCommands' `NewName` converter, not `string`. `NewNameConverter.Parse` refuses anything not alphanumeric (`Commands/PlayerCommands.cs:35-46`), spaces included, so a spaced name is refused by the server whether or not it is quoted. The test's rule ("every `string` parameter is quoted") is correct, and it passes.
- **Control characters at the ends were trimmed instead of refused — accepted.** `VcfArg.TryEncode` now scans the raw value before trimming. New tests: "name\n" and "\tname" are refused.
- **Bloodcraft guide still says class change and Shift spell are typed in chat — accepted.** Lines 40, 239 and 654 now point to **Change class** and **Choose class shift spell** on the Class tab, and the select form is named "Select first class".

Gates: logic tests 11278 passed / 0 failed; mod build 0 errors, 4 warnings (baseline).

## Addendum post-build inspection — round 2 (re-inspection)

- **Low — `BloodcraftGuide.cs:233`**  
  The summary still uses stale form names: **Select class** and **Choose shift spell**. Elsewhere, and per the arbitration, the actual names are **Select first class** and **Choose class shift spell**. Update this row for consistency and accurate navigation.

The accepted fixes are correct and complete:

- `VcfArg.TryEncode` now validates the untrimmed input, including control characters and Unicode line/paragraph separators.
- Lines 40, 239, and 654 correctly direct users to the Class-tab forms.

I agree with rejecting the rename finding. `newName` is a `NewName` converter parameter, not `string`, and `NewNameConverter` rejects non-alphanumeric names—including spaces—so quoting it would not enable `The Count`.

No high- or medium-severity issue remains in the shown code.
### Claude's arbitration
- **Stale form names in the Class tab row (BloodcraftGuide.cs:233) — accepted.** Now **Select first class** / **Choose class shift spell**. A grep confirms neither old name remains in the guides or the UI.

Inspection closed at MAX_INSPECTION_ROUNDS=2.

## Round 1 — Codex

1. **SpawnWaves radius is not observable:** the `march:` line omits radius, yet the plan offers a radius stepper without defining how an unread value is initialized or prevented from overwriting the server value during “Save all.”  
   **Fix:** represent radius as unknown, send it only after explicit user input, and test that untouched unknown fields are excluded from saves.

2. **Destination-name parsing is ambiguous:** `action.destination.name` permits commas, so parsing `destination: name, radius R, at …` from the first comma will corrupt valid names.  
   **Fix:** parse from the right-hand `, radius <integer>, at ` delimiter and add fixtures with commas and delimiter-like text in names.

3. **“Any other field” can expose invalid values for the current kind:** globally adding every path allows `action.destination=none` for HordeMarch and infestation-only fields on other kinds, contradicting the requirement that admins need not know accepted values.  
   **Fix:** make the field list and choice values kind-dependent, with dependency checks for destination/start fields.

4. **Pool discovery is incomplete:** only the Apocalypse branch is explicitly changed to read `pools:`, while HordeMarch growth/infestation pool pickers also claim to show the event’s pools.  
   **Fix:** document and test where pools are obtained for each new kind; parse `pools:` for every kind that emits it or omit chips when no authoritative list exists.

5. **Destination conflict handling is too weak:** pursuit gets a visible check, while fan-out, region lock, arena, and incompatible locations merely produce a server refusal after the user fills the card.  
   **Fix:** detect every known conflict, disable destination actions with a precise reason, and test each conflict independently.

6. **Immediate `Destination here` creates a knowingly invalid intermediate event:** it leaves required radius/name absent, but the plan does not specify refresh behavior, preservation of unsaved form state, or recovery if the disabled event’s info output changes.  
   **Fix:** define and test the full send/refresh sequence, preserving pending edits and clearly showing which required fields remain unset.

7. **Spread invariant recovery is underspecified:** client enforcement of `min ≤ max` can make an already-invalid loaded configuration impossible to edit, and ordering is only described relative to the loaded maximum.  
   **Fix:** define repair semantics for invalid loaded pairs and test both crossing directions, equality, partial edits, immediate sends, and Save-all sends.

8. **The parser plan lacks an explicit culture and anchoring contract:** several new lines contain decimals, optional clauses, apostrophes, coordinates, and free text, making permissive regex parsing liable to accept truncated or malformed lines.  
   **Fix:** require invariant-culture, fully anchored parsing with exact remainder consumption and negative fixtures for every format.

9. **Kind detection still depends on mutable human prose:** matching `action:` text is the sole discriminator, but tests cover only happy-path formats and not shortened, unknown, reordered, or future wording.  
   **Fix:** add conservative discriminator tests proving unknown action lines remain `Other` and cannot fall through to SpawnWaves.

10. **No round-trip/save-set tests are planned:** parser tests and `OrderForSave` tests do not establish that an untouched form emits no accidental setters or that edited forms emit exactly the intended keys.  
    **Fix:** add per-kind read–edit–save tests asserting the exact ordered command set, especially for unread fields and immediate-send controls.

11. **The rail source scan provides false confidence:** banning four direct method names cannot catch indirect rail mutation, aliases, event-driven rebuilds, or state loss caused by clearing/rebuilding `_nyarEdHost`.  
    **Fix:** add a behavioral test that snapshots rail expansion, redraws and edits every new form, then asserts identical rail state.

12. **The handler-generation requirement is not verified:** the audit calls `_nyarEdGen` guards mandatory, but verification contains no test or systematic check for newly added callbacks.  
    **Fix:** add stale-generation callback tests for every immediate button, stepper, text field, list editor, and pool picker.

13. **The plan does not test destructive setter side effects:** `destination none` removes `marchSpeed`, and `start.minDistance` replaces a Point/Admin start, yet only UI copy mentions these effects.  
    **Fix:** add confirmation/state-refresh tests proving dependent values are cleared or replaced in the model after successful server responses.

14. **Server refusal and partial-save behavior are unspecified:** multi-field Save all can succeed for early setters and fail later, leaving the UI stale or suggesting atomicity that does not exist.  
    **Fix:** define sequential partial-success reconciliation, refresh after the first refusal, and test recovery without resending already-applied destructive setters.

15. **The verification omits boundary tests for plain text and pool names:** name length, control characters, angle brackets, case, invalid punctuation, and pool-name length are security- and correctness-relevant inputs.  
    **Fix:** add exact validator fixtures for all forbidden characters, Unicode/control cases, 0/1/40/41 lengths, and the complete pool-name grammar.

16. **Deployment is mixed into an implementation plan without failure/rollback criteria:** two DLL replacements and `.bak` files do not specify exact targets, atomic replacement, hash expectations, or restoration on partial failure.  
    **Fix:** document resolved paths, expected hashes, ordered atomic replacement, and a tested rollback procedure before authorizing deployment.

VERDICT: REVISE

### Claude's response (round 1)

Accepted 1-10, 12-15 (see "Revisions after ClaudeX round 1" R1-R15 in the plan). Partially accepted 5: conflicts are shown as advisory checks, each detected from the info reply, not hard disables, because the info parse is best effort and the server's refusal is authoritative. Rejected 11 as a unit test: the logic test project has no Unity runtime, so the rail can't be exercised behaviourally there; the source scan is widened and the in-game check stays. Rejected 16: deployment follows the project's established, documented procedure (both DLL paths, game closed, .bak, hash checks) and is not part of this change. Added: -10/+10 buttons on list counts once the max is 500.

## Round 2 — Codex

Most prior findings are addressed, but several material gaps remain:

1. **SpawnWaves can still become uneditable after `Destination here`:** the plan re-reads info immediately, but the only documented SpawnWaves parser recognizes the complete `march:` line. If the server omits that line while radius/name are missing, `HasDestination` becomes false and the required controls disable again.  
   **Fix:** test the exact server response after `action.destination=here`; preserve an acknowledged-but-incomplete destination locally or infer it from the structured “Not ready” state until the complete march line appears.

2. **The destination regex is not actually strict:** `at (?:the starting admin|-?\d.*)` accepts malformed locations such as `1garbage`, contradicting the fully anchored/exact-consumption claim and the “never guess” convention.  
   **Fix:** encode the exact invariant coordinate grammar, including optional height, and add malformed-coordinate negative fixtures.

3. **SpawnWaves destination parsing remains ambiguous for Admin endpoints:** “take the name greedily before … `the starting admin`” needs an exact delimiter strategy because valid plain-text names may themselves contain that phrase.  
   **Fix:** specify a right-anchored grammar for both Point and Admin forms and test names containing “the starting admin,” parentheses, and coordinate-like text.

4. **Known destination conflicts remain actionable despite being certain:** when parsed info definitively reports pursuit, region lock, arena, fan-out, or an incompatible location, advisory text still lets the admin initiate a command guaranteed to be refused.  
   **Fix:** disable `Destination here` whenever a definite parsed conflict exists; retain advisory-only behavior only when relevant state is unread.

5. **The generation-guard source test is too syntactic and incomplete:** checking only the same or next line after `OnValueChanged +=` misses multiline handlers and other callback types such as clicks, selections, confirmations, and delayed callbacks.  
   **Fix:** rely on guarded helper APIs exclusively and add a structural/source check banning direct event subscriptions in the new editor code, or explicitly audit every callback type.

6. **Start replacement lacks the promised state-transition test:** the revisions test destination removal but do not test that `start.minDistance` replaces Point/Admin state, or that `start here` replaces Hotspot state after rereading.  
   **Fix:** add Apply/re-read tests for Point → Hotspot and Hotspot/Admin → Point, asserting stale start fields disappear.

7. **The template-group blurb appears false for infestation:** “they start where the admin stands” does not describe native-based infestation origins and may not describe hotspot-start HordeMarch templates.  
   **Fix:** use per-template descriptions or neutral group text that accurately covers all three templates.

VERDICT: REVISE

### Claude's response (round 2)

All seven accepted (plan "Revisions after ClaudeX round 2"). Finding 1 was sharper than stated: the server's EventAdmin.Info returns only "<id> disabled: <reason>" for any invalid definition, so the editor now keeps the last load plus acknowledged values on such a reply and shows the reason (this also fixes the same gap for wave-variety sets). Strict coordinate/name grammars with the listed negative fixtures; definite conflicts disable Destination here; new sections in a separate partial file with a no-subscription source test; start transition tests; neutral group blurb.

## Round 3 — Codex

The previous round’s findings are substantially addressed. Two material problems remain:

1. **A freshly opened disabled event exposes “all fields” despite having no known kind:** because every spawn type shares `action=waves`, this offers many guaranteed-invalid setters and directly contradicts the kind-filtering and “admin never has to know accepted values” requirements.  
   **Fix:** when a ReasonOnly reply has no cached kind, show only the reason and retry/copy/delete actions—or only fields proven universal—not every setter.

2. **ReasonOnly reconciliation is ambiguous about pending edits:** R2-1 says a later full reply “replaces everything,” while R6 says `Apply` preserves pending edits; replacing them would silently discard text entered while the event was disabled.  
   **Fix:** explicitly preserve unsaved changes across ReasonOnly and subsequent full replies, reconcile acknowledged values separately, and test pending-edit survival through `full → ReasonOnly → full`.

3. **SpawnWaves Admin endpoint syntax is still unverified:** the new regex assumes the destination is printed as `(the starting admin)`, but the appendix only says either endpoint may be “the starting admin,” and no explicit four-combination coverage is listed.  
   **Fix:** copy verbatim server output fixtures for Point→Point, Admin→Point, Point→Admin, and Admin→Admin marches and make the grammar match those exact strings.

4. **ReasonOnly matching must treat the event ID literally:** `^<id> disabled:` is unsafe or incorrect if the ID is inserted into a regex without escaping metacharacters.  
   **Fix:** use `Regex.Escape(id)` or parse and compare the prefix ordinally, with a test containing regex-significant ID characters.

VERDICT: REVISE

### Claude's response (round 3)

Round 3 (the cap): all four findings accepted and folded into the plan (R3-1..R3-4); none disputed. The Admin endpoint grammar was verified against the server's MarchLine: "from the starting admin" without parentheses, destination "<name> (the starting admin)". Loop closed at the cap with no open disagreement; implementation follows the revised plan, and the post-build inspection will check it.

## Sign-off

The user signed off on 2026-10-18T19:17:56.724Z after round 3 (deadlock).

## Round 1 — Codex

1. **Human-text spoofing is unaddressed:** `Parse` and `ClassifyRefusal` appear to trust message text alone, so player chat imitating Uriel could alter local feature state or satisfy the five-second capture.  
   **Fix:** Require verified system/server-message provenance before parsing, correlating, suppressing, or mutating state.

2. **The `.uriel features` correlation is not causally reliable:** a manually issued command or matching message arriving during Raphael’s five-second window can be mistaken for Raphael’s response and removed from chat.  
   **Fix:** Serialize probes, record send time/channel and expected server sender, consume at most one verified system reply, and explicitly test overlapping manual and UI-issued commands.

3. **Master-off discovery is incomplete:** `code=disabled` only establishes that object spawning is unavailable; until an admin opens Server controls and receives `.uriel features`, every other feature remains “unknown,” which the plan treats as enabled—even when the master switch is off.  
   **Fix:** Automatically request `.uriel features` immediately after disabled-version detection for admins, and keep dependent controls conservatively disabled pending that response.

4. **There is no non-admin solution for master-off state:** `.uriel features` is admin-only, so ordinary players can retain apparently enabled controls until each feature independently refuses an action.  
   **Fix:** Define conservative behavior for non-admins after `code=disabled`, or require a machine-readable server feature response before claiming comprehensive gating.

5. **The door gating requirements contradict themselves:** the table says event-door off greys both bind and unbind, while the next sentence says unbind stays enabled.  
   **Fix:** State and test one rule: disable bind only; leave `undoor` enabled because cleanup remains supported.

6. **Control-reply routing is underspecified:** classifying text does not establish which panel action owns a reply, and unrelated commands issued manually or by another UI can overwrite the reply card or unlock Confirm.  
   **Fix:** Maintain per-action pending state keyed by exact command, verified sender, send time, and expected reply class; ignore unmatched replies for UI state transitions.

7. **The shared server arm slot creates an explicit cross-admin race:** another admin can re-arm the same command between this client’s arm and confirm, changing what the local countdown actually represents.  
   **Fix:** Document the unavoidable server limitation in the UI, reset local armed state on any observed re-arm/not-armed reply, and request a server-side per-admin arm token as follow-up work.

8. **Multi-line kill-switch results lack an aggregation protocol:** “done,” failures, and missing-object/door notices arrive as separate messages, but the plan does not define how long the reply box collects them or prevents subsequent replies from replacing them.  
   **Fix:** Start a bounded aggregation window on the correlated done line, append all recognized follow-ups, and close it on timeout or a new action.

9. **Confirmation state has no complete lifecycle specification:** rebuilding/closing the panel, logout, reconnect, role loss, or issuing another arm can leave timers or enabled buttons associated with stale server state.  
   **Fix:** Cancel timers and clear all armed/pending state on panel disposal, protocol reset, disconnect, admin-state change, new arm, completion, and not-armed replies.

10. **Event subscription cleanup is missing:** pages subscribe to static/global `UrielFeatureState.Changed`, but no unsubscribe/disposal behavior is planned, risking duplicate updates and retained Unity objects after panel rebuilds.  
    **Fix:** Specify lifecycle-bound subscribe/unsubscribe behavior and add a rebuild test proving exactly one live handler per page.

11. **Failure handling is too optimistic:** cache invalidation is triggered by a kill-switch “done” line before subsequent per-domain failure lines are known, and the UI has no explicit partial-success state.  
    **Fix:** Aggregate the complete result, label partial failures prominently, and invalidate only affected domains—or conservatively invalidate all while clearly reporting partial failure.

12. **Purgeall cache behavior is semantically wrong:** Remove all objects is planned to clear the shared-container cache and stale the share tab even though `.uriel purgeall` removes objects, not shares.  
    **Fix:** For purgeall, invalidate only object/catalog state; reserve share-cache invalidation for killswitch or commands that actually alter shares.

13. **The test plan does not cover the dangerous asynchronous cases:** there are no tests for spoofed player messages, overlapping probes, late replies, panel rebuilds, disconnects, cross-action replies, or multi-line result interleaving.  
    **Fix:** Add deterministic state-machine tests for provenance, correlation, timeout boundaries, interleaving, lifecycle reset, and duplicate subscriptions.

14. **The rail guard is brittle and overclaims safety:** scanning selected files for four method names misses indirect calls, aliases, new helper methods, and rail mutations elsewhere, while disabled-version detection intentionally emits a presence change that does refresh the rail.  
    **Fix:** Replace or supplement source scanning with behavioral tests asserting expansion, selection, scroll position, and tab order remain unchanged after feature-state updates and expected presence transitions.

15. **“The left rail stays exactly as you left it” conflicts with first-time detection:** converting `code=disabled` into Present necessarily adds/enables the Uriel group and invokes the existing rail refresh path.  
    **Fix:** Narrow the invariant to preserving selection/expansion/scroll after initial presence discovery, and test that precise contract.

16. **Unknown-as-enabled is unsafe for destructive or invalid operations:** malformed or changed server wording makes parsing return null, after which controls remain actionable despite having no trustworthy state.  
    **Fix:** Show unknown controls but disable state-dependent actions with “feature state unavailable; refresh” until a valid snapshot or authoritative protocol field is received.

VERDICT: REVISE

### Claude's response (round 1)

### Claude's response (Uriel round 1)

Accepted:
- **2 and 6 (correlation, routing):** the destroy is dropped entirely; every Uriel line stays in chat. Button state
  now moves through a pure per-action state machine (`UrielControlFlow`). A reply moves a slot only when this client
  sent that action's command within 10 s, and the reply texts name their command. Replies Raphael didn't ask for are
  shown but change no button.
- **3 (master-off discovery):** one automatic `.uriel features` probe after a `code=disabled` version reply, for
  admins (`MessageService.IsLocalAdmin()`).
- **5 (doors):** bind only; `undoor` stays enabled.
- **7 (shared arm slot):** the server's reply is the truth and the slot follows it; a muted note says so; a ROADMAP
  request for a per-admin token.
- **8 and 11 (aggregation, partial):** follow-up lines join the killswitch result for 3 s; a failure line marks the
  result partial in its header. A killswitch invalidates all its domains, partial or not.
- **9 (lifecycle):** reset on logout or disconnect, admin loss and re-arm; the countdown comes from timestamps
  (no timers).
- **10 (subscriptions):** the existing `EnsureXSubscribed` / `UnsubscribeX` pattern, called from `MainPanel.Reset()`;
  the gate-target list is cleared on rebuild; a source guard checks for one `+=` with a matching `-=`.
- **12 (purgeall):** object tabs only. Units and stopall stale only the Castle Events tabs.
- **13 (tests):** state-machine tests for ownership, expiry boundaries, interleaving, aggregation, reset and re-arm.
- **15 (rail invariant):** narrowed. The Uriel group appears once on detection; after that, expansion, selection and
  scroll are unchanged.

Rejected, with reasons:
- **1 (spoofing):** already covered. The observer runs after the `MessageType != System` filter
  (ClientChatPatch.cs:226), and player chat is never System. Now stated in the plan.
- **4 (non-admins):** there is no non-admin source for the master state, and Uriel has no machine features line yet.
  Non-admins get a muted line after `code=disabled`, and each refusal greys its feature. A fuller answer waits for
  `.uriel api features` (ROADMAP).
- **14 (behavioural rail tests):** the test project is Unity-free by design, so it can't drive the rail. The source
  scan plus the in-game check stays, and the plan now says that is the guard.
- **16 (unknown disables):** that would break every 0.21.0 server, which has no features command, and it changes
  today's behaviour. No destructive control depends on feature state. Unknown stays usable and labelled "unknown".

## Round 2 — Codex

Most prior findings are addressed, including provenance, non-admin behavior, cache scope, subscriptions, rail invariants, reply aggregation, and action-specific correlation. Material issues remain:

1. **The countdown cannot update as designed:** Unity retained-mode pages are not continuously “drawn,” and no state-change event fires when 25 seconds elapse, so Confirm can remain visibly enabled with a stale label indefinitely.  
   **Fix:** Add a lifecycle-bound main-thread updater or scheduled callback that refreshes countdown labels and transitions Armed to Expired, with cleanup on rebuild/reset/logout.

2. **The press-twice confirmation is outside the new state machine:** the plan retains the existing shared `AddUrielConfirmButton` slot while claiming one isolated slot per action; Stop all events and armed confirmations can still interfere through that shared pending state.  
   **Fix:** Model the first/second press and three-second deadline inside each `UrielControlFlow` action slot, eliminating the shared confirmation slot for these four controls.

3. **The cross-admin behavior contradicts the ownership rule:** one section says unsolicited replies never change button state, while another says a new arm line from another admin makes the slot follow server truth. Both cannot hold, and replies to another admin may not be delivered to this client at all.  
   **Fix:** State that only locally correlated replies affect state; explain that remote re-arms are unobservable and that a subsequent local success/not-armed response is the reconciliation point.

4. **The ten-second ownership window conflicts with result aggregation near its boundary:** a done reply arriving at 9.9 seconds starts a three-second follow-up window, but failure lines after 10 seconds may fail the “command within the last 10 s” ownership test.  
   **Fix:** Once a correlated done line opens aggregation, accept recognized follow-ups until that aggregation deadline independently of the original command window.

5. **`ArmSent` has no timeout transition:** if the arm reply is lost or delayed beyond ten seconds, the state machine can remain stuck in `ArmSent`, and the reset rules do not mention request timeout. The same applies to `ConfirmSent`.  
   **Fix:** Define explicit response deadlines that transition unanswered `ArmSent` and `ConfirmSent` states to Failed/Idle with a visible “No server reply” result.

6. **The global gate-target clearing rule is ambiguous and potentially destructive:** “the list is cleared whenever a Uriel page is rebuilt” could remove registrations belonging to other live Uriel pages or stale only part of a rebuilt page.  
   **Fix:** Store gate registrations per page/build owner and unregister only that owner’s targets during its rebuild or destruction.

7. **The verification plan tests pure transitions but not the mechanism that makes timeout state visible:** boundary tests on `UrielControlFlow` would pass even if the actual button never reevaluates time.  
   **Fix:** Add a UI-level or source-backed integration test proving that the expiry updater disables Confirm, changes its label, and stops running after reset/rebuild.

VERDICT: REVISE

### Claude's response (round 2)

### Claude's response (Uriel round 2)

All 7 accepted:
1. **Updater:** one self-unregistering ticker on `CoreUpdateBehavior.Actions` while any slot is active. It calls
   `UrielControlFlow.Tick(now)`, which returns the slots that changed, and refreshes the labels and buttons. It is
   removed when every slot is Idle, on Reset, and when the page is gone.
2. **Press twice moved into the slot** (ConfirmPending, 3 s). These four buttons no longer use the shared
   `AddUrielConfirmButton` slot.
3. **Cross-admin:** only locally correlated replies change state. Remote re-arms can't be seen; the next local reply
   reconciles. The contradictory sentence is gone.
4. **Aggregation** has its own 3 s window from the correlated done line, independent of the 10 s deadline. A test
   covers the boundary (9.9 → 12 joins, 13.1 doesn't).
5. **Deadlines:** ArmSent and ConfirmSent go to NoReply after 10 s, with a visible "No server reply" message.
6. **Gate targets** are stored per page (PanelType); a rebuild replaces only that page's entry.
7. **Visible timeout:** Tick's boundary tests, plus a source guard that the ticker calls Tick and unregisters itself
   (on AnyActive false and on reset). A Unity-level UI test isn't possible in the Unity-free test project, so the
   rest is checked in game ("let a Confirm time out").

## Round 3 — Codex

The previous findings are mostly addressed, but two material timing flaws remain:

1. **The ticker never unregisters after normal completion:** `Done`, `Partial`, `NoReply`, and `Expired` are non-Idle terminal states, while the ticker runs whenever any slot is non-Idle and unregisters only when `AnyActive` is false. After any action, it can therefore remain registered forever.  
   **Fix:** Define `AnyActive` as “has a pending deadline/aggregation/countdown,” not “state is non-Idle,” or separate retained result display from transient execution state and return the execution slot to Idle.

2. **The 25-second confirmation deadline starts too late:** measuring from “arm-seen time” ignores command/reply latency, while Uriel’s 30-second window begins when the server processes the arm command. With more than five seconds of reply latency, Raphael can enable Confirm after the server deadline has already passed.  
   **Fix:** Set the local deadline conservatively from the arm-send timestamp (`sentAt + 25s`), never from receipt time; if the reply arrives after that deadline, immediately show the arm as expired.

Add tests proving terminal states do not keep the ticker registered and that delayed arm replies cannot extend the confirmation window.

VERDICT: REVISE

### Claude's response (round 3)

### Claude's response (Uriel round 3, the cap)

Both accepted, and the plan is revised:
1. **Ticker:** `AnyActive` now means "a slot has a pending deadline" (the reply wait, the Armed countdown, the 3 s
   press, the aggregation window). Done, Partial, NoReply and Expired are results with no deadline, so the ticker
   unregisters after any outcome. Tested.
2. **Window from the send:** the 25 s run from the arm's send time, not from the reply. A late reply can't extend it,
   and one after 25 s shows Expired at once. Tested (a reply at 6 s leaves 19 s).

The loop stops at MAX_ROUNDS=3 with these fixes in the plan and not re-reviewed. Nothing is left in dispute: every
finding from rounds 2 and 3 was accepted. The post-build Codex inspection will check the implementation.

## Sign-off

The user signed off on 2026-10-18T19:37:10.688Z after round 3 (deadlock).


## Post-build inspection (Uriel 0.22.0 + Nyarlathotep 0.14.0) — fresh read-only Codex, 2026-10-18
Thread 01a15092-d006-7072-b740-0a4f54c57d29. Bundle: both plans, the new files in full, diffs vs HEAD.

### Round 1 — Codex
1. **High — `MainPanel.UrielControls.cs`, `NoteUrielControlOutcomes` / `UrielStaleTabsAfter`**  
   A successful or partial Restore vanilla only marks tabs stale; it never clears the shared-container cache required by the plan. Reopening Storage/Prisons can therefore display containers that the kill switch already made private.  
   **Fix:** clear the shared-container cache when `KillSwitch` first enters `Done` or `Partial`, before marking the related tabs stale.

2. **Medium — `MainPanel.NyarHordes.cs`, `BuildNyarDestinationRows`**  
   For a SpawnWaves event without a destination, the method returns before constructing radius, name, and march-speed controls. The signed-off behavior was to keep those controls visible but disabled with “set a destination first”; the current UI hides them and changes layout.  
   **Fix:** always build the three controls and disable their selectables while `has == false`, with the reason note.

3. **Medium — `MainPanel.Uriel.cs`, `BuildUrielPrisonTab`**  
   “Show rules” is not registered under the Prison Cells gate. With `PublicStorage.PrisonEnabled=false`, this control remains clickable despite the plan explicitly requiring share, unshare, show-rules, and take-prisoner controls to be greyed.  
   **Fix:** include `UrielPrisonInfo` in the `UrielFeature.Cells` gate registration.

4. **Medium — `UrielFeatures.cs`, `UrielControlFlow.OnLine` (`NotReady` branch)**  
   The command-agnostic `Uriel is not yet initialized.` reply is assigned to the first waiting slot in fixed order—KillSwitch before PurgeObjects. If both commands are awaiting replies, a purgeall refusal can incorrectly terminate the kill-switch slot while purgeall later times out.  
   **Fix:** correlate this ambiguous reply only when exactly one eligible slot is waiting; otherwise leave both unchanged and show it as an unowned reply.

5. **Low — `MainPanel.UrielControls.cs`, `UrielFeaturesText`**  
   The Master feature never gets its required row. It appears only as a banner when off, so an enabled or unknown master switch is absent from the “one row per feature” readout.  
   **Fix:** render `UrielFeature.Master` like the other features, retaining the additional off banner.

6. **Low — `MainPanel.UrielControls.cs`, admin-loss handling in `StartUrielControlsTicker`**  
   Admin loss resets the flow only while `Flow.AnyActive` keeps the ticker registered. Terminal states such as Done, Partial, Expired, or NoReply survive an admin-loss/regain cycle, contrary to the required reset-on-admin-loss behavior, and can leave old results displayed.  
   **Fix:** reset the flow from the existing admin-status change/re-check path as well as from the active ticker.

### Claude's arbitration
- 1 rejected: Raphael has no shared-container cache. SharedContainerDetector re-scans live (ids are deliberately not cached); marking tabs stale covers the readouts.
- 2 rejected: the destination radius/name/speed are hidden behind a note until a destination exists; same outcome, no inert controls (steppers have no disabled mode).
- 3 accepted: Show rules gated by Cells.
- 4 accepted: "not initialized" matches only when exactly one slot waits; test added.
- 5 accepted: master switch gets its own row.
- 6 accepted: opening Admin: Controls without admin resets the flow.
- Also fixed before the inspection (found by a test): a pending confirm at exactly the 25 s mark now expires, and a press at/after the window is refused.

### Round 2 — Codex
No new material findings in these fixes.

Tests 11,920 passed, 0 failed. Build 0 errors, 4 baseline warnings.
