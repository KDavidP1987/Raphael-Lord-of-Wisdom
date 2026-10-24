# Raphael 0.73.0 — Bloodcraft 1.13.26, admin progress reset, complete in-game guides
_Locked via claudex-loop — by Claude + Kris (decisions taken in plan mode, 2026-10-17). Revised after Codex round 1._

## Context
Kris asked for three things:
1. Review the new Bloodcraft release and update Raphael where needed.
2. A server admin asked for a "complete reset" of a player's Bloodcraft progress. Bloodcraft has none, so Raphael
   should send the needed admin commands one after another so it acts as one reset.
3. Every in-game mod guide should be complete, clean and well organised. The Nyarlathotep guide ("N8R") comes
   first; it is the weakest today. A player or admin should never need GitHub or Thunderstore.
   Kris asked for `/impeccable` or an equivalent level of design care.

Process: the claudex-loop with pre-audit, a ClaudeX plan review, a post-build Codex review and the test gates.
Push only after Kris has validated the build.

Recon done (Phase 0). Sources: the cloned Bloodcraft 1.13.26, Eclipse 1.3.17 and Emberglass, two research
agents, and the Raphael code.

## Decisions locked with Kris (plan mode)
| Decision | Answer |
|---|---|
| Reset level | Editable field, **default 10** (Bloodcraft's default StartingLevel), with a note; 0 allowed |
| Guide tabs | **One guide tab per mod** (Quick start open at the top); old QS/Help tab ids kept as aliases |
| Scope | **Everything in 0.73.0** |
| Kindred | **Short Kindred guide** tab |
| Optional reset steps | Quest refresh and "active familiar to level 1": **both unticked** |
| Confirm | **Review list + two-step confirm button** (AddUrielConfirmButton) |

## Pre-audit findings (what is real, not a false positive)
- **Bloodcraft 1.13.22-1.13.26:** no chat-command or protocol change. Against the 1.13.24 reference copy, only
  `RconCommands.cs` changed (diagnostics.primal.start), and the 1.13.25 / 1.13.26 changes are server-internal.
  → The README compatibility line goes from 1.13.21 to 1.13.26; no code change for commands.
- **Real gap: the Emberglass bridge (1.13.22, opt-in `UseEmberglassEclipseBridge`).**
  - The server sends progress / configs as Emberglass packets whenever the target client finished an Emberglass
    handshake. It falls back to chat only when that send throws.
  - A Raphael user who also has the Emberglass client installed, on an opted-in server, therefore gets no
    Bloodcraft stats. Users without Emberglass are unaffected: the send throws, so they get chat
    (`PacketRelay.cs:1461`).
  - Emberglass keeps **one handler per packet type** (`Registry.cs:54`, last wins). Raphael already stands down
    entirely when Eclipse is loaded (`EclipseProtocolService.cs:658`).
- **Reset** (from the Bloodcraft 1.13.26 source):
  - 28 × `.prestige r`, 16 × `.wep set … 0`, 10 × `.bl set … 0`, 8 × `.prof set … 0`, then `.lvl set … N`.
    That is **63 commands**.
  - Optional: `.quest rf` and `.fam sl … 1`.
  - Names match exactly, ignoring case, offline players included.
  - `.prestige r` gives **no reply** when the player never had that type.
  - Gate refusals: "X is not enabled."
  - Not resettable by an admin: class and class spell, stat choices, familiars, toggles.
  - Raphael has no paced multi-command sender; the VBlood scanner state machine is the model.
  - VCF has no throttle; it delivers one reply per user per server frame, in order.
- **Guides:** see the audit.
  - Nyarlathotep has no command syntax.
  - 7 × U+2212 (a glyph the font lacks).
  - Wrong commands: `.quest d/w`, `.fam pr <Stat>`, the exoform wording, Faust `admin set`.
  - The general pages leave out Uriel, Faust and Nyarlathotep.
  - V Rising Basics stacks double headings.
  - Only Vanilla Admin uses the command table helper.

## Approach

### 1. Emberglass receiver (Bloodcraft 1.13.22+)
- New `Services/EmberglassBloodcraftBridge.cs`, mirroring Eclipse 1.3.17 `Services/EmberglassEclipseBridge.cs`. It is
  reflection only, so Emberglass stays an optional dependency.
  - Resolve `Emberglass.API.Shared.VNetwork`.
  - Call `RegisterClientbound<EclipseServerMessagePacket>(Action<User, T>)`.
  - Raphael's packet class is `internal sealed class BloodcraftEclipseBridge.Messages.EclipseServerMessagePacket { public string
    Message { get; set; } }` with a public parameterless ctor. That is the same FullName as Bloodcraft's: Emberglass type ids are
    `Hash32(type.FullName)` (`Registry.cs:49`).
  - The payload is System.Text.Json with `CamelCase` (`Serialization.cs:29-32,61,93`), so `{"message":"…"}` binds to `Message`.
- The handler passes `Message` to the existing `EclipseProtocolService.TryHandleServerMessage(raw)`. That keeps MAC checking and
  routing in one place, and a bad MAC is counted and dropped.
- **Exact reflection call** (as Eclipse does):
  - `vNetwork.GetMethods(Public|Static).Single(m => m.Name=="RegisterClientbound" && m.IsGenericMethodDefinition &&
    m.GetParameters().Length==1).MakeGenericMethod(typeof(EclipseServerMessagePacket)).Invoke(null, new object[]{ new
    Action<ProjectM.Network.User, EclipseServerMessagePacket>(OnPacket) })`.
  - `User` is the **game's** `ProjectM.Network.User`, which Raphael already references, not an Emberglass type. No Emberglass type
    is referenced at compile time.
  - A failure in lookup, binding or Invoke disables the bridge for the session with one logged reason.
- **Opt-in (revised after round 2):** Raphael setting `[Bloodcraft] UseEmberglassBridge = false`, the same opt-in model Bloodcraft
  and Eclipse use.
  - Off: nothing changes for anyone.
  - On: the bridge registers.
  - **The setting is read once, at `Plugin.Load`, and a change takes effect after a game restart.** Emberglass has no way to
    unregister a handler, so the bridge is never registered or removed live.
    - The setting's description and the Settings row both say "(restart the game to apply)".
    - When the stored value differs from the value loaded this session, the Connection tab shows "Restart the game to apply the
      Emberglass setting".
  - When the Emberglass client is loaded, the bridge is off and no Bloodcraft data has arrived 60 s after Bloodcraft registration
    on chat, the Connection tab shows a hint: "Your client has Emberglass: if this server uses Bloodcraft's Emberglass bridge,
    turn on Use Emberglass bridge in Settings and restart the game."
- When it registers: **only** when the setting is on, the Emberglass assembly is loaded, AND Eclipse is not.
  - Raphael already stands down from the Bloodcraft layer when Eclipse is loaded (`EclipseProtocolService.cs:658`).
  - This also avoids Emberglass's one-handler-per-type rule (`Registry.cs:54`, last registration wins).
- Registration with Bloodcraft stays on chat; the server's chat path accepts it either way.
- `Plugin.cs` gets `[BepInDependency("io.zfolmt.Emberglass", SoftDependency)]`, so Emberglass loads first when present.
- **Threading:** Emberglass dispatches clientbound packets from its chat-system patch on the Unity main thread, and Raphael's
  chat patch runs there too. The bridge checks the managed thread id once against the main thread recorded at `Plugin.Load` and logs
  a warning if they differ. In that case it marshals onto the `CoreUpdateBehavior` queue instead of parsing in place.
- **Honest diagnostics** (Connection tab and the Chat & Diagnostics log). State moves
  `not installed → installed (Eclipse handles it) → registered, no data yet → receiving`, with the last valid packet time and a count
  of MAC failures.
  - "Registered, no data yet" is shown neutrally, with the possible reasons listed: the server's bridge is off, data is still
    coming over chat, or another mod owns the packet type. The tab never names one cause it can't know.
  - The tab shows separately whether Bloodcraft data is arriving over chat.
  - Registration alone never claims "receiving".
- **Release:**
  - Opt-in means players who don't turn it on take exactly the old path.
  - The Emberglass-on-both-sides in-game check stays on Kris's test list.
  - **Unless that check passes, the bridge is "experimental" everywhere:** the setting description, the changelog, the README and
    the Bloodcraft guide. It is also left out of the README's compatibility claim ("Bloodcraft 1.13.26 over chat; the Emberglass
    bridge is experimental").

### 2. Admin "Reset Bloodcraft progress" (Bloodcraft → Admin tab, new card)
Name and wording: "Reset Bloodcraft progress", with a plain list of what it can't reset, shown both in the card and in the summary.
"Full reset" is avoided because class, stat choices and familiars stay.

**Pure, unit-tested core: `Services/Bloodcraft/BcReset.cs` (no Unity types; linked into the test project).**
- **Player name.** Validated before any command is built:
  - trimmed, 1-20 characters;
  - refused only for characters that change VCF parsing or can't be in a name: `"`, `\`, control characters, newline, and
    `<` / `>` (rich text);
  - a name without spaces is sent bare;
  - a name **with spaces** is sent in double quotes, which VCF treats as one argument (`VCF.Core/Common/Utility.cs:18-65`);
  - both forms are unit-tested against a port of VCF's splitter.
- **`BcResetPlan.Build(player, options)`** returns an ordered step list:
  - prestige `r` × 28 → `wep set … 0` × 16 → `bl set … 0` × 10 → `prof set … 0` × 8 → `lvl set … N`;
  - then the optional `quest rf` and `fam sl … 1`;
  - **63 commands by default (+2 optional).** Every count and progress figure the UI shows is derived from this list.
  - Enum lists are copied from Bloodcraft 1.13.26 (`PrestigeInterface.cs:4-34`, Weapon / Blood / Profession enums).
  - Level: invariant integer, 0 to the server's MaxLevel when Raphael knows it (from `ConfigsToClient`), else 0-1000 with the server
    as the final check. An empty or non-numeric value blocks Start.
- **`BcResetReplies.Classify(step, line)`**: each reply is **correlated to its step**.
  - Color tags are stripped first.
  - A success must name the **same player and the same type**, and for `set` the same value:
    - prestige: `"{type} prestige reset for {name}."`
    - weapons: `"{Weapon} expertise set to [0] for {name}"`
    - blood: `"{Blood} legacy set to [0] for {name}"`
    - professions: `"{Prof} set to [0] for {name}"`
    - level: `"Level set to {N} for {name}!"`
    - quests: `"Quests for {name} have been refreshed."`
    - familiar: `"Active familiar for {name} has been set to level 1."`
  - "X is not enabled." is **correlated by its text to a system**, not to a step:
    - "Prestiging is not enabled." / "Exo prestiging is not enabled." → prestige;
    - "Expertise …" → weapons;
    - "Blood Legacies …" → legacies;
    - "Professions …" → professions;
    - "Leveling …" → level;
    - "Quests …" → quests;
    - "Familiars …" → familiar.
  - Some refusals can't be tied to a step: "Couldn't find player.…", `[denied]`, "Level must be between…", "Invalid …" and
    "Couldn't find active familiar". They are handled like this:
    - Only this client's own commands can produce these lines, because VCF replies go only to the sender (`ctx.Reply`). Another
      admin's replies never reach this chat.
    - "Couldn't find player" and `[denied]` **pause** the run instead of stopping it. They could come from something else this
      client sent, such as another Raphael tab or the admin typing. The card shows the line and offers **Stop** (the default) or
      **Continue**.
    - "Level must be between…", "Invalid …" and "Couldn't find active familiar" are attributed only to the step in flight, inside
      its wait window.
    - **A non-prestige step that times out pauses the run.** The card shows "No reply to `<command>`" with **Retry this step** /
      **Skip** / **Stop**. Nothing else is sent until the admin chooses, so a slow reply can never be pinned on a later step.
      - While paused, only **positively correlated** lines change the step: a success naming the same player and type, or a
        system-off text. Either one ends the pause by itself.
      - A nameless line ("Invalid …", "Level must be between…", "Couldn't find active familiar") arriving during the pause is
        **not** attributed. The card shows it under the pause as "Possibly related: …", and the admin still chooses.
      - **For a timed-out `quest rf`, "Retry this step" is replaced by "Re-roll quests again".** It sits behind the same warning
        and two-step confirm as the Retry list, because the first re-roll may already have happened.
    - Silent prestige steps don't pause: silence is their normal "nothing to reset", and their only possible refusal is the
      system-level one, which is correlated by its text.
  - Returns Ok / SystemOff / PlayerNotFound / Denied / Invalid / PreconditionNotMet / Unrelated.
  - A correlated success for an **earlier** step (a late reply) updates that step's record and never advances the current one.
- **`BcResetRun`**: a state machine with one step on the wire at a time. Minimum gap 0.25 s; wait 1.5 s for prestige `r`, 4 s for
  everything else.

| Outcome on the current step | Effect |
|---|---|
| Ok | Mark done, go on |
| Silent prestige `r` | **"No reply — assumed nothing to reset"** (counted separately, never "confirmed") |
| Silent other step | **Pause**: Retry this step / Skip (it is recorded as Unknown) / Stop |
| SystemOff | Skip the rest of that system |
| Invalid | Failed, go on |
| PreconditionNotMet (familiar) | "Not met: player offline or no familiar out" |
| PlayerNotFound (any step) | **Pause**: the line is shown; Stop (default) / Continue |
| Denied | **Pause**: the line is shown; Stop (default) / Continue |
| Lost admin / disconnect / server switch | **Stop** |

- **Empty plan:** if every box is unticked, the plan is empty and Start is disabled ("Pick at least one thing to reset"). This is
  tested.
- **Retry:**
  - `set … 0`, prestige `r` and `fam sl 1` are idempotent; **`quest rf` is not**, because it re-rolls quests again.
  - "Retry failed and unknown steps" recomputes the remaining list **from the step records at the moment it is pressed**, so a step
    a late reply has since confirmed drops out.
  - It shows that exact list before running.
  - An unknown `quest rf` is left out unless the admin ticks "also re-roll quests again", with a warning.
- **Late replies after the run has finished:**
  - For **30 s** after the run finishes, they still update their step's record. The summary shows a "late confirmation: …" line
    and its counts change in place; the run itself stays finished.
  - After 30 s the run is sealed: it is taken off the reply tap and its records no longer change.
  - Retry (if pressed) starts a new run with its own window.
- **Final report:** reset / assumed-empty / skipped (system off) / failed / unknown / not met, plus the "can't be reset by an admin"
  list.

**Runner `Services/Bloodcraft/BcResetRunner.cs`** (service-level, so it doesn't depend on whether the panel is open).
- Ticked from `CoreUpdateBehavior.Actions`.
- **Before every send** it rechecks that the client is still connected and that `MessageService.IsLocalAdmin()` is still true; if
  either has changed it stops with a visible reason.
- Sends with `MessageService.EnqueueMessageSilent`.
- **Reply tap:** the first handler in `MessageService.HandleInboundChat`, ahead of InterceptFlag and generic capture, so another
  capture can't swallow a reset reply.
  - It consumes, and hides from chat, **only lines it correlated** (positive match, never a blanket destroy).
  - Uncorrelated or ambiguous lines pass through untouched.
  - Reset commands never arm InterceptFlag or generic capture, since they aren't in `ShouldArmGenericCapture`.
  - **While a run is active, the other Bloodcraft Admin-tab forms are disabled** ("A progress reset is running"). That stops the
    admin interleaving the same `set` / `r` commands and confusing correlation.
  - A read command clicked elsewhere can still arm its own capture. The reset tap runs first, so a correlated reset reply is never
    lost; at worst an uncorrelated nameless line appears in that capture's panel. A global capture lease is not added: it would block
    every other mod's reads for a minute or more.
- **Lifecycle:**
  - Stopped by the teardown in `InitializationPatch.ClientBootstrapSystem_OnDestroy_Prefix`, which covers disconnect and server
    switch.
  - Closing or rebuilding the panel does **not** stop it; the card re-attaches to the live run.
  - Stop cancels at once. A command already sent can't be recalled; the summary says so, and no send happens after cancel (tested).
- **Threading:** chat and `CoreUpdateBehavior` both run on the Unity main thread, so there is no cross-thread state.

**Card UI** (existing helpers: `MakeUrielRow`, `AddUrielBoolToggle`, `AddUrielConfirmButton`, `BeginAdminGate`):
- Player: a searchable picker over `PlayerRosterService.GetOnlinePlayers()`, and a typed name for offline players (validated live).
- Systems (all core on): Prestige (28 types) / Weapon expertise (16) / Blood legacies (10) / Professions (8) / Level (stepper,
  default 10, "match your server's StartingLevel").
- Optional (off): Re-roll quests / Active familiar to level 1 ("player must be online with a familiar out").
- Review line from the plan: "63 commands for Bob: prestige 28, weapons 16, legacies 10, professions 8, level 1".
- "Can't be reset by an admin": class and class spell, weapon / blood stat choices (their bonus drops to 0 with the level), familiars,
  quest history and player toggles. The player resets these themselves with `.class change`, `.wep rst`, `.bl rst` and `.fam reset`.
- **Start reset** (two-step confirm). Progress shows "step 12 / 63" with the current command, a scrolling step log, Stop, and then
  Retry failed/unknown.

### 3. Guide kit + complete guides
- **Load `/impeccable` at build start** for critique and layout principles (hierarchy, scannability, density, copy). They are applied
  to a written page spec before building. Live browser mode doesn't apply to Unity UI.
- **Kit: `UI/ModContent/MainPanel.GuideKit.cs`**, using `AddGuideSection`, `CollapsibleSection.Build`, `AddCommandTableRow`,
  `AddCard`, `AddLinkRow` and `WARNING_TINT`.
  - **Page header:** name, one-line purpose, "checked against <mod> vX" (muted).
  - **Sections:** open or closed, holding any children.
  - **Command table:** stacked rows, with the syntax in accent mono, aliases inline ("`.level` / `.lvl`"), a scope chip
    (Player / Admin) and a "Destructive" flag where needed. The description wraps underneath.
  - **Settings table:** key / default / meaning.
  - **Callouts:** Admin only / Alpha / Planned / Off by default.
  - **Tab map:** rail label → what it does → command it sends.
- **Content as pure data** (`Services/Guides/*.cs`): sections, command rows and settings rows per guide.
  - One `GuideText.Arg()` escapes `<arg>` via `<noparse>`, giving a single notation.
- **Page schemas by guide type**, enforced by tests:
  - **Full mod guide** (Bloodcraft, Beelzebub, Uriel, Faust, Nyarlathotep):
    1. What it is
    2. Quick start (open)
    3. Concepts
    4. Raphael tab map
    5. Player commands
    6. Admin guide
    7. Server configuration
    8. Troubleshooting
    9. Defaults note
  - **Short mod guide** (Kindred): what it is / what Raphael wires (tab map) / commands / troubleshooting.
  - **General pages** (Quick Start, V Rising Basics, Game Guide): free structure, but use the kit's header and sections.
- **Command inclusion policy**, every manifest entry accounted for:
  - player and admin commands are listed, aliases grouped onto one row;
  - destructive ones are flagged (purge, delete, wipe, reset, `iacknowledge…`);
  - machine `api` commands go in one closed "Machine API (Raphael uses these)" table;
  - test or diagnostic commands are listed in an "Admin diagnostics" closed block;
  - nothing is silently dropped. An explicit **allowlist with reasons** (`tests/fixtures/guide-allowlist.json`) covers anything
    intentionally omitted.
- **Order of work:**
  1. Nyarlathotep: all `.nyar` commands; events / actions / waves / pools / bosses / pursuit / locations / regions; pillars;
     templates; unit watch; kill switch; announcements; cfg sections; planned features.
  2. Bloodcraft: merges Quick Start + Help, the full command list, fixes the 4 wrong commands, explains the alias collisions, and
     points to the reset card.
  3. Beelzebub, then Uriel (including Castle Events and the object-spawn defaults), then Faust (48 commands, the access model,
     `admin set k=v`), then Kindred.
  4. General pages: Quick Start, V Rising Basics (stacked headings, duplicate "Scholar", plus Nyarlathotep and Castle Events) and the
     Game Guide intro.
- **Tabs:** Settings and Help becomes Quick Start, then the Bloodcraft / Beelzebub / Uriel / Faust / Nyarlathotep / Kindred Guides,
  then V Rising Basics, Game Guide and the rest. The old QS/Help `PanelType`s route to the merged guide; nothing else navigates to
  them (checked by grep). RailState is unaffected.
- **Accuracy gate (generated manifests):**
  - `tools/guide-commands.py` reads the sibling repos read-only. It extracts each `[Command]` / `[CommandGroup]` (name, shortHand,
    adminOnly, usage, description) into `tests/fixtures/<mod>-commands.json`. Each file records the source mod version, the git
    commit and the generator version.
  - Each command entry also records its **parameter list from the C# method signature**: name, type, and whether it is optional
    (it has a default value). This is used because usage strings use brackets inconsistently; Bloodcraft writes required arguments
    as `[Player]`.
    - **Injected parameters are dropped:** the first parameter of type `ChatCommandContext` / `ICommandContext` (VCF's context),
      because it isn't typed by the player.
    - All six sibling mods use VCF; the generator fails on any `[Command]` method whose first parameter isn't a context, so a new
      framework can't slip through unnoticed.
    - **Verb-dispatch commands:** some commands take every argument as an optional string and switch on a verb inside the method.
      Nyarlathotep's `api` commands are an example: `Event(ctx, string verb = "", …)`. The signature can't describe these, so the
      manifest flags them as `verbDispatch`. Their guide rows carry a `SyntaxOverride` naming the source switch, and they are
      checked by Kris's read-through, not by test (c).
    - Generator self-tests run on one representative command per mod (a no-argument command, one with required and optional
      arguments, an enum argument, and a command group), with the expected argument lists.
  - **Config manifests:** the generator also extracts each mod's settings (BepInEx `Bind` / Bloodcraft `ConfigEntryDefinition`:
    section, key, default, description) into `tests/fixtures/<mod>-config.json`.
  - Tests check, both ways:
    - (a) every guide command exists in its manifest, with the same admin flag;
    - (b) every manifest command appears in the guide or in the allowlist;
    - (c) each guide row's arguments match the signature's argument names, order and requiredness. The guide renders required
      arguments as `<x>` and optional ones as `[x]`, and a reviewed `SyntaxOverride` reason is needed to differ;
    - (d) every public config key appears in its guide's settings tables, with the manifest default, or is in the allowlist with a
      reason.
  - **Release preflight fails** unless the sibling mod repos are present and regenerating the manifests gives no diff. Commit mode
    only warns. Both of Kris's machines keep the sibling repos at `…/dev/projects/Games/V Rising/`.

## Tests (pre and post)
Logic tests (`tests/Raphael.Logic.Tests`, run by `preflight -Mode Release`) gain:
- **Reset plan:** step lists and counts for each option set; level validation (empty, negative, over max, non-numeric).
- **Name validation:**
  - refused: empty, whitespace only, 21 characters, `"`, `\`, `<` / `>`, tab, newline and other control characters;
  - accepted: leading and trailing spaces trimmed; an internal ASCII space is accepted and sent quoted; a name without spaces is
    sent bare.
- **Classifier:** every real reply text, including color tags; correlation (same type but another player → Unrelated; same player but
  another type → Unrelated); system-off texts mapped to their system; nameless refusals attributed to the in-flight step
  only; quoted names with spaces.
- **Run state machine:** Ok path; silent prestige → assumed; silent `set` → pause, then Retry / Skip (Unknown) / Stop with nothing
  sent while paused; SystemOff skip; PlayerNotFound and Denied pause, and Stop / Continue work; a late reply after the 30 s seal is
  ignored;
  a late reply after timeout updates the earlier step and doesn't advance; duplicate replies; an unrelated matching-looking line from
  another admin; Stop leaves no sends after cancel; admin lost mid-run stops; disconnect stops; Retry runs only failed/unknown steps;
a nameless refusal arriving during a timeout pause is shown as "possibly related" and changes no record; a correlated success
during the pause ends it; a timed-out `quest rf` offers only the warned "Re-roll quests again"; an empty plan blocks Start; a late success removes the
step from Retry; an unknown `quest rf` is excluded from Retry by default.
- **Emberglass:**
  - wire test — serialize `{ Message }` with Emberglass's exact options (CamelCase System.Text.Json) and deserialize into Raphael's
    packet type;
  - FullName equality;
  - routing seam into `TryHandleServerMessage`;
  - the diagnostics state transitions.
- **Guides:** manifest checks (a)-(d); schema per guide type; no forbidden glyph (U+2212 ◄ ► ↻); no raw backticks; every `<…>`
  escaped.

Also:
- A preflight glyph lint over the guide sources.
- Build with no new warnings, then Release preflight, then package.

**In-game list for Kris** (dev server, game closed → deploy both DLL paths):
- reset a test character with the options on and off;
- Stop mid-run, then Retry;
- a system switched off;
- an unknown name;
- reset an offline player;
- every guide page opens, reads cleanly and its collapsibles work;
- the rail is unaffected;
- Bloodcraft stats still flow over chat;
- **release-gate choice:** the Emberglass-on-both-sides check (Emberglass on client and server, `UseEmberglassEclipseBridge=true`).

## Review process
1. ClaudeX runner (`codex-round.mjs`, read-only, ≤5 rounds). Every finding is arbitrated and logged in PLAN-REVIEW-LOG.md.
2. Kris's sign-off on the converged plan **before any code**.
3. After the build: a fresh read-only Codex post-build inspection of the diff (≤2 rounds), logged.
4. Commit, push and package **only after Kris validates in game**. Release = `chore(release): v0.73.0`. Kris uploads to Thunderstore
   himself.

## Critical files
- New: `Services/EmberglassBloodcraftBridge.cs`, `Services/Bloodcraft/BcReset.cs`, `Services/Bloodcraft/BcResetRunner.cs`,
  `UI/ModContent/MainPanel.GuideKit.cs`, `Services/Guides/*.cs`, `tools/guide-commands.py`, `tests/fixtures/*-commands.json`,
  `tests/fixtures/guide-allowlist.json`, `tests/fixtures/*-config.json`.
- Changed:
  - `Plugin.cs`;
  - `Patches/InitializationPatch.cs` (runner teardown);
  - `Services/MessageService_Processing.cs` (reply tap);
  - `UI/ModContent/MainPanel.cs` (`TabGroups`, dispatch, `BuildAdminTab` card, Connection tab line, general guides);
  - `MainPanel.Nyar.cs`, `.Beelzebub.cs`, `.Uriel.cs`, `.Faust.cs` (guide builders → kit);
  - `tests/Raphael.Logic.Tests`;
  - `tools/preflight.ps1`;
  - README, CHANGELOG(s), ROADMAP.

## Risks
- **The Emberglass path can't be exercised in game unless Emberglass is installed on both sides.**
  - It is opt-in (default off), so nobody gets the untested path without choosing it.
  - The offline wire and seam tests cover the format, and the diagnostics never overclaim.
  - A unit test against the real Emberglass runtime isn't possible (IL2CPP); its registry is internal, so it isn't inspected by
    reflection.
- **A future Bloodcraft could change its reply texts.** Correlation would then fail, and the step reports Unknown (never a false
  success). The classifier tests pin the 1.13.26 texts.
- **Guide volume is large (~6 pages).** The data-driven content, the two-way manifest checks and the syntax check keep it accurate;
  Kris's read-through is the final gate.


## Addendum (2026-10-17)
Pre-existing UI fixes, the V Rising Basics / Game Guide kit conversion and the Nyarlathotep editor background were added to 0.73.0 by Kris. See PLAN-ADDENDUM.md.
