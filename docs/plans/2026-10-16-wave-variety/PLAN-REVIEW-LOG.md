# Plan Review Log: Raphael 0.72.0 wave variety + rail accordion
Phases 0-1 complete. Interrogation: Kris gave the brief and asked to proceed (no open load-bearing decisions; defaults logged under Key decisions). MAX_ROUNDS=4.

## Round 1 — Codex

- Static rail memory is cleared only in a game-destroy prefix, but the plan does not prove that every disconnect, failed connection, return-to-menu, or server switch executes that path; stale choices could leak between servers. **Fix:** enumerate and test every session-transition path, key memory by connection/session identity, and clear it on both disconnect and bootstrap destruction.

- The rail policy is internally ambiguous: “user-chosen groups are never auto-closed” conflicts with accordion-driven collapse marking those groups “user-chosen-closed,” while “user-chosen” conflates durable intent with current expansion state. **Fix:** model expansion and user intent separately (`expanded`, `explicitlySet`) and specify a transition table for header clicks, tab clicks, accordion collapses, rebuilds, and detection changes.

- The policy considers whether a user-chosen group is open but says non-mod groups are never touched; unless all non-mod expansion states participate in that predicate, an auto-opened mod can coexist with Kindred/Game UI/Settings. **Fix:** make the “any user-selected group open” guard explicitly cover every rail group and add cross-category accordion tests.

- “First available mod group” hard-codes rail order rather than selecting the acknowledged/active integration, so Bloodcraft can always win even when the user is working with Nyarlathotep. **Fix:** define primary from active tab or newly detected service, with rail order only as a deterministic final fallback.

- A user who closes the current primary leaves it protected from reopening, but a later detection change may auto-open a different unchosen primary because no user-chosen group is open. **Fix:** persist an explicit session-level “user dismissed automatic expansion” state until the user opens a group or requests reset.

- Static dictionaries can retain obsolete keys across UI versions and make tests/order dependent. **Fix:** use a typed fixed-state record, define initialization/migration behavior, and reset it in test setup.

- Pool-count validation cannot be mirrored reliably because existing pools are learned only through a best-effort parse of human text; the client cannot know whether adding a pool exceeds eight. **Fix:** treat pool-count and reference checks as server-authoritative until structured readback exists, and phrase client validation accordingly.

- Deletion safety has the same problem: the UI cannot reliably determine whether any event field references a pool, yet the plan implies a friendly refusal before sending. **Fix:** either parse and visibly label the check as incomplete or always submit deletion and surface the authoritative server error.

- Replace-then-append pool saving is non-atomic and knowingly corrupts an existing valid pool when any append fails or times out. A summary does not restore the prior value. **Fix:** add server-side atomic replacement/chunk staging, or refuse oversized replacement in this release rather than destructively applying a partial pool.

- Batch matching is underspecified: repeated fields, truncated `value`, timeouts, late replies, and interleaved single commands can cause acknowledgements to be attributed to the wrong batch. **Fix:** define a serialized operation state machine keyed by a unique request identifier, or prove strict single-flight ordering and quarantine late replies before implementing batches.

- A timeout is not proof of failure; retrying or reporting a member as refused can be wrong if the server applied it and the reply was lost. **Fix:** distinguish `refused` from `unknown/timed out` and require authoritative readback before claiming the saved member count.

- The 450-byte chunk rule lacks an exact framing calculation and encoding definition; command prefix, event ID, field name, transport wrapper, and UTF-8 byte count can push it past the chat limit. **Fix:** centralize serialization first, measure the complete emitted twin in the actual encoding, and chunk against the verified protocol limit with margin.

- Rate-limit reasoning is unsafe: exactly 250 ms spacing is nominally 4/s but scheduler jitter, retries, unrelated Nyarlathotep commands, or server sliding-window accounting can exceed 5/s. **Fix:** use one global rate limiter with a conservative interval and test sliding-window behavior with retries and mixed commands.

- The unit-name grammar is contradictory: it says `[A-Za-z0-9_]`, `CHAR_` prefix, and a server maximum, but never gives the actual maximum or anchors the full expression. **Fix:** copy the exact server constant and anchored parser rule into the plan and boundary tests.

- List validation specifies forbidden characters but not empty tokens, leading/trailing commas, repeated entries, duplicate bosses, integer formatting, case sensitivity, or overflow. **Fix:** define a canonical parser shared by builders and free-text validation, with tests for all tokenization and numeric edge cases.

- Boss validation is deliberately weakened to a note for the mutually exclusive `action.bosses`/last-wave rule, contradicting the goal of a validated form and guaranteeing server-disabled definitions. **Fix:** block the conflicting Set, or present an explicit replace-the-other-value transaction.

- The total-boss calculation is unclear for multiple roll entries and mixed fixed/rolled entries, and “when the list holds a roll, or it is a wave’s own list” may omit server constraints on an all-waves fixed list. **Fix:** reproduce the exact server aggregation algorithm and test every mixed-entry shape against server fixtures.

- “Wave 1…10” assumes ten valid wave slots regardless of the event’s actual wave count and does not define behavior for missing waves or malformed/stale definitions. **Fix:** derive selectable waves from structured definition state, or document and validate the server’s exact implicit-wave behavior.

- Pursuit compatibility is inferred from editable behavior and human info text, so stale text or alternate formatting can produce false assurance. **Fix:** label compatibility checks advisory and rely on structured local fields or authoritative server errors; do not claim full client validation.

- Pool and pursuit prefill parsing has no versioned grammar, escaping rules, or failure visibility; silent parse errors can leave a form targeting the wrong value. **Fix:** isolate parsers, show “could not read current value,” never silently default, and add captured-output fixtures for every supported server version.

- Server-supplied reasons and human-info fragments may contain rich-text markup that Unity renders, enabling UI spoofing or broken layout. **Fix:** escape or disable rich-text rendering for every server-derived string before display.

- Version-aware wording based on `plugin starting 0.13.0-rc.1` is underspecified and prone to incorrect lexical prerelease comparisons or unexpected version suffixes. **Fix:** use a semantic-version parser with explicit fallback behavior and tests for rc.1, rc.3, release, older, and malformed values.

- `pillar_off` support references a “planned” field rather than an implemented contract guarantee, but no parser-compatibility test covers absent, duplicate, malformed, or unknown pillar IDs. **Fix:** specify tolerant parsing and add protocol fixtures before changing user-facing text.

- Scratch, uncommitted unit tests provide no durable regression protection for the most failure-prone parsing, chunking, batching, and rail-policy logic. **Fix:** add a committed test project to the solution and run it in preflight/CI.

- The proposed external test compiles source files into a scratch .NET 6 project, which may not reproduce the production target framework, conditional symbols, nullable configuration, or actual serializer behavior. **Fix:** test the production assemblies under their real target/configuration and reserve scratch harnesses for exploratory checks only.

- Tests omit batch correlation, lost/late replies, server refusal mid-save, disconnect during a batch, duplicate acknowledgements, queue cancellation, rebuild during a request, and stale info parsing. **Fix:** add deterministic state-machine tests for all of these failure sequences.

- The deployment and commit steps are bundled into implementation despite requiring mutable external state and user authorization, while only push is gated on Kris’s approval. **Fix:** separate build/package from deploy/commit and require explicit approval for each external mutation.

- The cited sibling handoff, contract, parser, and implementation files were not available inside the provided read-only workspace, so the asserted exact server limits and root causes remain unverified. **Fix:** make those sources available to the reviewer or embed immutable excerpts/commit hashes before approving implementation.

VERDICT: REVISE

### Claude's response (round 1)

Accepted and changed in PLAN.md:
- Intent separated from expansion (Expanded + Intent None/Open/Closed + UserTookControl) with a transition table; the open-group guard covers every group kind; primary prefers the active tab's group; once the user touches the rail nothing auto-opens again this session; typed RailState limited to TabGroups titles; the policy is a pure, tested function.
- Pool count, pool references, allied-group mix, bosses-both-last, pursuit compatibility, wave gaps: server-authoritative and labelled so in the UI; the client mirrors only the per-value parse, exactly as the server does (excerpts in a new Appendix).
- Delete pool is always sent; the server refusal is shown.
- Pool save: one command when it fits; otherwise a pre-send warning and a NyarBatch state machine (stop on first err, one retry on ratelimit, timeout = unknown + Reload, late/duplicate/unrelated answers ignored, cancel on close/disconnect), 0.40 s pacing, correlation through NyarClient's strict single-flight returning the in-flight command; byte count measured on the exact emitted string from one builder.
- Exact unit-name grammar and CommaList rules copied; numeric parsing mirrors NumberStyles.None.
- Prefill never guesses; a "couldn't read" line; escaping of all server-derived text including NyarText.
- Version parser with tests; tolerant pillar= parsing with fixtures.
- Committed logic test project (net6.0, same language settings, not in the sln) run by preflight Release; batch, rail and version cases listed.
- Commit/push gated on Kris's explicit OK; deploy is the documented local dev step, game closed.
Rejected, with reason:
- Keying rail memory by connection identity: the one teardown prefix already resets every mod's detection; rail memory now lives exactly as long as detection state, which is the property that matters.
- Server-side atomic pool replace: outside this repo (Raphael never edits Nyarlathotep); the handoff prescribes appends. Mitigated as above.
- Blocking action.bosses vs last-wave bosses client-side: the client cannot reliably know the other value on api 8 (no field read); the server saves and disables with a reason the editor now shows prominently.
- Deriving wave slots from structured state: api 8 has no field read; the server's own N range is 1-10 and it refuses gaps with a clear reason, which is shown.
- Server sources not visible: excerpts with the commit hash are in the Appendix.

## Round 2 — Codex

Most prior findings are addressed, but these material issues remain:

- A dead header is collapsed while retaining `Intent=Open`; when it becomes usable again, the policy refuses to change it because its intent is non-`None`, leaving a group the user explicitly opened permanently collapsed. **Fix:** on reactivation, restore `Expanded=true` for `Intent=Open`, or model desired expansion separately from temporary availability-forced visibility.

- Command-string correlation does not correlate replies: `Complete()` merely returns whichever command is currently in flight, so a late reply from a timed-out command can complete a newer unrelated command if it arrives after that command starts. **Fix:** match replies using echoed protocol fields/request IDs, or prevent all new sends during a conservative late-reply quarantine and force authoritative reload before releasing the client.

- Cancellation drops queued work but cannot cancel an already transmitted command; closing the editor or disconnecting can therefore still produce a late mutation/reply that contaminates the next session or operation. **Fix:** explicitly track abandoned in-flight commands, quarantine their replies, and do not permit another operation until reset or the late-reply window expires.

- Batch pacing is not demonstrably global: disabling only Save does not prevent other editor actions or already queued client commands from sharing the server’s per-admin sliding rate limit. **Fix:** make the batch hold an exclusive client lease and apply one global rate limiter to every Nyarlathotep command, including retries and non-batch operations.

- “Completed by its ok/err/end line” is too broad: an `[NYAR:end]` for a list/read operation must not complete an event-set command, and an unrelated error must not advance the batch. **Fix:** define expected response types and identifying fields per command and accept only a matching terminal response.

- The claim that Wave 1–10 is “the server’s own N range” is not supported by the supplied parser excerpts or assumptions. **Fix:** cite the exact server constant/check establishing 10, and add boundary tests for wave 0, 1, 10, and 11.

- The pool retry is convergent only if the first replacement command succeeds, but the summary design does not specify how many names were established by that first multi-name chunk versus later appends. **Fix:** have each batch step carry its member coverage and compute saved/stopped counts from acknowledged steps, with tests where the first chunk and each subsequent append fail.

- The committed tests link production source files into a separate project, which can still drift from real compilation symbols and does not test the crucial `NyarClient` integration where reply attribution, queue ownership, reset, and UI cancellation occur. **Fix:** add integration tests against the production client/service boundary with scripted protocol lines, especially timeout → new command → late old reply.

- `NyarVersion` treats 0.12.5 differently from an “older 0.13.0 prerelease” despite the fix only being verified as entering rc.2; the chosen fallback behavior is not justified by ancestry or compatibility policy. **Fix:** define behavior from the actual fixed-version boundary—normally every version older than 0.13.0-rc.2 gets the bug wording—or document why unsupported older versions intentionally use the generic text.

VERDICT: REVISE

### Claude's response (round 2)

Accepted: (1) usable-again restores Intent=Open groups (accordion-aware); (2)+(5) correlation now by protocol fields: batch takes only cmd=event verb=set lines, ok must match id+field, err only while its step is outstanding and is the in-flight string; end lines and other commands ignored; one outstanding step and no sends after timeout/cancel/err; (3) cancel wording for an already-sent step; (4) global twin gap raised to 0.30 s in NyarClient for every admin twin, plus 0.40 s batch pacing; (6) cited WaveListPath 1..10; (7) per-step member coverage and counts from acknowledged steps, tested; (9) version boundary = any 0.13.0 prerelease below rc.2, with the reason pre-0.13.0 gets generic text (no apocalypse exists there).
Rejected (8) integration tests against NyarClient: it depends on Unity Time and MessageService (IL2CPP game types) and cannot be compiled outside the game; the correlation logic was moved into the pure NyarBatch, which is tested with scripted line sequences incl. timeout -> late reply; NyarClient's change is two lines, covered in the in-game list.

## Round 3 — Codex

Most round-2 findings are addressed, but reply correlation still has a material race:

- A late `err` from a timed-out or cancelled command can arrive while a new batch command is outstanding; because errors carry no `id`/`field`, the new batch will accept the old error merely because its own command is currently in flight. **Fix:** after timeout/cancel, quarantine the client from all new sends until reset or a bounded drain period expires; alternatively require a wire-level request identifier.

- The same race affects `ok` when the old and new commands target the same event and field—for example, retrying Save after a timeout—so matching only `id` and `field` can falsely acknowledge the new step. **Fix:** prohibit a new operation during the quarantine window or add a unique echoed correlation token server-side.

- “Starting a new batch cancels it” is unsafe when the old batch has a transmitted step: that step cannot be recalled, yet the plan does not say the new batch is delayed until the abandoned reply is drained. **Fix:** distinguish cancellation of queued versus in-flight work and refuse a replacement batch while an abandoned command remains unresolved/quarantined.

- The rail reactivation rule is not fully represented by the stated policy: after a rebuild, a usable group can have `Intent=Open` and `Expanded=false`, but the policy “never changes” non-`None` intent groups, and no availability transition necessarily fires to reopen it. **Fix:** make reconciliation explicitly restore usable `Intent=Open` groups before applying accordion conflict resolution, including on rebuild, and test dead → rebuild → usable.

- The global-rate claim says reads are excluded, but the supplied contract excerpt only says “at most 5 twins per admin per second”; it does not substantiate that reads are uncounted. **Fix:** cite the exact server limiter classification or pace every `.nyar api` command through the same global sliding-window limiter.

- The scripted tests still omit the decisive sequences: timeout/cancel → start a new command targeting the same field → receive the old `ok`/`err`. **Fix:** add both sequences and assert that the new operation cannot advance or fail from the stale response.

VERDICT: REVISE

### Claude's response (round 3)

Accepted all six: (1-3) a 10 s quarantine after any batch that ended with an unanswered sent step — no new batch can start in that window and the ended batch has no outstanding step, so late ok/err cannot touch a batch; a new batch can't start while one runs (no replacement cancel); (4) the policy restores usable Intent=Open groups on every reconcile including after rebuild, accordion-aware, tested dead -> rebuild -> usable; (5) cited contract §3/§5a that reads are not rate-limited; (6) the stale-ok and stale-err sequences after timeout are now explicit tests.

## Round 4 — Codex

Round-3 addresses the rail restoration, wave-bound evidence, read-rate classification, and batch test gaps. One material race remains:

- The quarantine blocks only a new batch, not other Nyarlathotep commands. During the window, the admin can apparently send pursuit, Add/Remove/Delete pool, pillar, template, or other event actions; a stale unidentifiable `err` can then complete or visually overwrite that newer operation through the normal client path. **Fix:** make quarantine global to `NyarClient`: block every command that expects a correlated reply, disable affected controls, and allow only explicitly safe/untracked reads until quarantine expires or the protocol resets.

- The plan says a late reply after batch termination is “handled as today by the normal result line,” which can overwrite the batch’s important unknown-state warning and mislead the admin into believing the stale response belongs to their latest action. **Fix:** tag abandoned replies during quarantine and discard them from both batch handling and the normal result line, optionally logging them diagnostically.

- The 10-second quarantine is measured from send time, so after a 6-second batch timeout it protects only four additional seconds; the rationale cites a 5-second client timeout but the batch itself uses six seconds. **Fix:** define quarantine as a duration after abandonment/timeout, not after send, and test the exact boundary.

- Cancellation by closing the editor needs to preserve the global quarantine somewhere outside the destroyed editor/batch instance; otherwise reopening the editor can lose the guard and immediately send a new command. **Fix:** store quarantine state in the session-scoped protocol/client service and clear it only on expiry or full protocol reset.

VERDICT: REVISE

### Claude's response (round 4)

Accepted all four (round cap reached; no open disagreement): quarantine is global in NyarClient (static, session-scoped, cleared on expiry or disconnect reset), measured 10 s from abandonment, holds every admin twin (reads continue), and stale twin ok/err lines received during it are logged and discarded from both the batch and the result line so the unknown-state warning stays. Boundary tests at 9.9 s / 10.1 s after abandonment.

## Form audit after round 4 (Kris's request, 2026-10-16)
Kris asked for an audit: everything in well, user-friendly, selections front-loaded, admins need not know the acceptable
values, editing easy. Claude re-read the editor (`MainPanel.NyarEditor.cs`) and the server parser at Nyarlathotep
`CommandArgs.cs` / `EventAdmin.cs` / `Validation.cs`. Findings F1-F6 and changes 1-11 are in PLAN.md §B2. Three rule
mirror errors were corrected in §B: unit count optional; repeats refused in `action.units`, allowed in a wave list; and
the 50-unit ceiling for a list with a pool entry. One behaviour was missing: setting Wave 1 converts a units-form event
and removes units/waves/intervalSeconds/modifiers. The round cap was reached, so these changes get no plan round. They
are covered by the committed tests (Testing → "Form audit") and the post-build Codex cross-inspection of the diff.

## Post-build inspection

Fresh read-only Codex (`codex exec -s read-only`), given PLAN.md and the full uncommitted diff against `f2a91db`.

### Inspection round 1 — Codex findings (verbatim)

- **High — `Raphael/UI/ModContent/MainPanel.NyarEditor.cs:659`** — Saving an existing pool may use a single `+unit` or `-unit` command instead of replacing the whole pool, so the operation is not safely retryable after an unknown result.  
  Failure scenario: pool `troops=[A,B]` → admin adds `C` → `+C` reaches the server but its reply is lost → retrying Save sends `+C` again → the server refuses the duplicate, and “Save again” cannot recover as promised.

- **High — `Raphael/UI/ModContent/MainPanel.NyarEditor.cs:569`** — An incompletely parsed pool remains editable and save-enabled despite `PoolsComplete == false`, allowing a partial human-readable reply to overwrite and silently remove members Raphael never read.  
  Failure scenario: server pool contains 30 members but chat capture exposes only 12 → warning appears, admin adds one member and presses Save → the first pool command replaces the server pool with those 13 visible members, deleting the other 18.

- **Medium — `Raphael/UI/ModContent/MainPanel.NyarEditor.cs:453`** — Converting a units-form event discards every unsaved units-form change before the conversion is acknowledged, while only the current units value is sent.  
  Failure scenario: admin changes level `40→60`, multiplier `1.0→1.5`, and units, then presses “Turn into a per-wave list” → `Undo(UnitsFormKeys)` immediately drops all those changes, the conversion sends only Wave 1 units, and the server converts using its old level/modifiers; refusal also leaves the edits lost.

- **Medium — `Raphael/Services/Nyar/NyarClient.cs:108` and `Raphael/Services/Nyar/NyarBatchRunner.cs:76`** — Cancelling an on-wire batch starts quarantine immediately but leaves the command in `NyarClient._inFlight`, whose five-second timeout starts quarantine again and extends the effective wait to roughly 15 seconds.  
  Failure scenario: close the editor immediately after a pool step goes out and receive no reply → quarantine starts at close, then restarts at the client timeout five seconds later → queued admin twins remain blocked about five seconds longer than the specified ten-second cancellation quarantine.

- **Medium — `Raphael/Services/Nyar/NyarForm.cs:91` and `Raphael/Services/Nyar/NyarInfoParse.cs:242`** — Loot and territory are inferred as `false` whenever their phrases are absent, rather than being marked unread when the `spawn:` text is missing or shortened.  
  Failure scenario: an event actually has loot enabled, but its long server description omits/truncates the relevant `spawn:` fragment → Raphael pre-fills “No” with no unread warning → changing another nearby setting and using Save all can send `action.loot=false`, unintentionally disabling loot.

- **Low — `Raphael/UI/ModContent/MainPanel.NyarEditor.cs:700` and `Raphael/Services/Nyar/NyarBatch.cs:143`** — Successful pool deletion is represented as a pool save with zero covered members, producing “Pool X saved (0 units)” rather than confirming deletion.  
  Failure scenario: admin confirms Delete pool and the server accepts `none` → the result line says the pool was “saved,” making it unclear whether deletion actually occurred.

The rail policy and version-aware/pillar-specific error texts show no material defect from this review. The list validators, boss totals, wave-unit ordering, level-before-levelDelta ordering, dropdown callback guard, deferred-destroy handling, and stale-control generation guards also appear consistent with the locked plan.

### Inspection round 1 — dispositions

| # | Finding | Disposition | Change |
|---|---|---|---|
| 1 | Single `+x` / `-x` pool save not retry-safe | **Accepted** | Pool saves are always a full replace (chunked when long); the `+x` / `-x` shortcut is gone. |
| 2 | Partially read pool stays savable | **Accepted** | An existing server pool can't be saved while `PoolsComplete` is false (unless saved this session); the note says to press Reload values. New pools are unaffected. |
| 3 | Convert drops unsaved units-form edits | **Accepted** | No Undo before the send. Convert is refused while level / multiplier edits are unsaved. `NyarForm.Apply` drops units-form-key edits once the event reads back as a wave list (tested). |
| 4 | Cancel + client timeout quarantine twice (~15 s) | **Accepted** | The runner quarantines on cancel only if the step is no longer the client's in-flight command; otherwise the client's reply timeout does it. |
| 5 | Loot / territory default to false when absent | **Rejected** | The server's `spawn:` line is one unwrapped line, omitted when empty (`EventAdmin.cs:1003-1026`); only `pools:` lines wrap. Absent = off is the format. |
| 6 | Pool delete reported as "saved (0 units)" | **Accepted** | `NyarBatch.IsPoolDelete`: "Pool X deleted." / "Pool X was not deleted: …" (tested). |

Build: 0 errors, the 4 pre-existing warnings. Logic tests: 434 passed, 0 failed.

### Inspection round 2 — Codex on the fixes (verbatim)

- Finding 1 fix is sound: every save now begins with a full replacement, so replaying the complete chunked batch is idempotent.

- Finding 2 fix is sound: incomplete server pools are visibly blocked from saving until reloaded, while new and successfully session-saved pools remain usable.

- Finding 3 fix is sound: conversion retains unsaved units until acknowledgement, blocks unsaved level/modifier values, and removes obsolete units-form edits only after reload confirms the wave-list conversion.

- **Medium — `Raphael/Services/Nyar/NyarBatchRunner.cs:76`** — Deferring quarantine to the client timeout avoids restarting it but does not fix the reported delay: quarantine still begins approximately five seconds after cancellation and expires approximately fifteen seconds after cancellation.  
  Failure scenario: the editor closes immediately after a command is sent and no reply arrives → the client waits five seconds to time out, then starts the ten-second quarantine → queued admin commands remain blocked for roughly fifteen seconds rather than the specified ten-second cancellation quarantine.

- Finding 5’s rejection is sound: `EventAdmin` emits `spawn:` as one complete, unwrapped line and omits it only when no spawn options are enabled, so absence of `loot on` or territory text legitimately means `false`; the partial-reply hazard is specific to wrapped `pools:` output.

- Finding 6 fix is sound: the single `none` deletion batch now receives accurate success and refusal messages without changing ordinary pool-save reporting.

### Inspection round 2 — dispositions

- Fixes 1, 2, 3 and 6 confirmed sound; the rejection of 5 confirmed.
- **Finding 4 (follow-up) accepted.** The round-1 change only moved the start of the quarantine to the client's 5 s
  reply timeout, so the total wait was still about 15 s. Now cancelling releases the client's in-flight command
  (`NyarClient.Complete()`) and starts the 10 s quarantine at once. A late answer inside it is discarded as stale by
  `NyarProtocolService.StaleTwinReply`, the same as after a timeout.
- Round limit (2) reached; no open findings. Build: 0 errors, the 4 pre-existing warnings. Logic tests: 434 passed, 0 failed.


## Sign-off

The user signed off on 2026-10-17T02:27:12.164Z after round 4 (deadlock).
