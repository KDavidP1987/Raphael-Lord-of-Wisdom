# Plan addendum: 0.73.0 pre-existing UI fixes
_Locked via claudex-loop — by Claude + Kris (plan mode, 2026-10-17). Extends PLAN.md; same release._


## Context
0.73.0 (guides, Bloodcraft reset, Emberglass bridge) is built, has had 2 post-build Codex rounds, and is deployed
locally. It is not committed yet. While writing the guides, the agents found pre-existing UI bugs. Kris decided they
ship in 0.73.0, with a pre-audit first to rule out false positives. Kris also asked for:
- the V Rising Basics and Game Guide pages converted to the guide kit;
- a fix for the Nyarlathotep event-editor background, which flips between purple and gray (keep the gray).

Rigor is unchanged: pre-audit (done; below), ClaudeX review of this addendum, post-build Codex review, tests, and
clean formatting when anything is added or removed. The Emberglass bridge ships as experimental (decided). The
untracked `docs/plans/2026-10-16-wave-variety/resp1-4.md` will be deleted (decided).

## Pre-audit results (3 read-only agents, every item checked against mod source)
- **Real:** all Kindred, Uriel, Beelzebub and Bloodcraft items below, and the Nyarlathotep background.
- **False positive:** "Stygian Coin" (no such text in Raphael).
- **Partly confirmed:** Kindred "Unlock with a blank player". A blank is never sent (`FormField.IsValid` rejects
  it), but every "blank for self" hint is false.

## Decisions (Kris, plan mode)
| Item | Decision |
|---|---|
| Kindred buttons for commented-out commands (`.showhair`, `.unbindall`, `.wipe` / `.commencewipe` / `.cancelwipe`, `.clan fix`) | **Remove** them and the whole wipe section; the guide notes they are disabled upstream |
| Beelzebub Recall buttons (`.beelz tp` = stash) | **Remove** them (Summons tab + summons overlay) |
| Beelzebub Phase button | **Cycle to the next phase** (`.beelz phase <next>`, like the transform overlay) |
| Uriel Force-purge plot (now the same as Purge plot) | **Remove the duplicate button** |

## Changes

### 1. Kindred Admin (`UI/ModContent/MainPanel.KindredAdmin.cs`, strings in `Services/MessageService_Processing.cs`)
- **Wrong group names:** fix `.staff X` → `.reloadstaff`, `.reloadadmin`, `.autoadminauth`, `.setstaff <player> <rank>`,
  `.removestaff <player>`, `.toggleadmin <player>`. Fix `.castle X` → `.incomingdecay`, `.freezeheart`, `.thawheart`,
  `.plotsowned [page]`, `.frozenhearts [page]`, `.clanplotsowned [page]`, `.teleporttoplot <i>`, `.plotinfo <i>`.
- **Prisoner commands:** Gruel Settings → `.prisoner gruelsettings`. Feed Settings becomes a one-field form,
  `.prisoner feedsettings {feed}`.
- **Dead commands:** remove the 7 buttons and the wipe section (KA:791-836), including its spacer and heading pair,
  `wipeNote` and `wipeCancelRow`.
- **Bosses:** "List All Bosses" → **"List Locked Bosses"** (`.boss list`), with a corrected tooltip.
- **Player hints:** replace every "Player (blank for self)" hint with "Player name"; the field is required.
- **Clean formatting:**
  - `PrisonerDiag`, `LookupsRow` and `CastleClanRow` move to `AddKLRow` (`MainPanel.cs:6008`), whose height scales
    with text size (`CastleClanRow` uses a fixed 32px);
  - rows close up after removals; no empty rows or orphaned headings.
- **Admin gate:** add `BeginAdminGate` to the Server and World tabs, matching the Players and KL tabs.
  - It is UI-only by design. The server enforces admin regardless (comment at `MainPanel.cs:9142-9148`), so this is
    no security claim.
  - It is self-scoped: it returns a CanvasGroup-wrapped content root for the tab, with no matching end call, so
    nothing can leak into other tabs.
- **Quoting (revised after review round 1): per-field and opt-in, never global.**
  - `TextField` gets a `Quote` flag, off by default, so every existing form keeps sending exactly what it sends today.
  - The flag is set only on Kindred free-text fields whose source parameter is a `string`. KindredCommands and
    KindredLogistics use no `[Remainder]` parameters (grep: none), so each string parameter is one VCF token.
  - One pure encoder, `Services/VcfArg.cs` (linked into the tests), does the work. It trims the value, refuses an
    empty or whitespace-only one, refuses `"`, backslash and every control character (CR, LF and tab included: VCF's
    `GetParts` treats backslash-quote as an escape, so a trailing backslash could swallow the closing quote), and
    wraps the value in quotes only if it contains a space.
  - Validation runs on the raw value first; the value is encoded exactly once at token substitution. A refused value
    shows "Invalid: <label>", the same path as today.
  - Tests cover plain, spaced, empty, whitespace-only, quote, backslash, trailing backslash and newline values.
  - The quote decision follows VCF exactly: `Utility.GetParts` splits only on ASCII `' '` outside quotes
    (`VCF.Core/Common/Utility.cs:48`). A test pins that a non-breaking space is left unquoted, because VCF doesn't
    split on it.
  - **Which fields quote is declared next to the tested templates, not in UI code.** `KindredAdminCommands` lists, for
    each template, which placeholders are quoted. The forms build their fields with
    `quote: KindredAdminCommands.IsQuoted(template, field)`. A test asserts that every placeholder whose manifest
    parameter is a `string` is quoted, and that no int, float, bool or enum placeholder is.
- **Accuracy gate (new):**
  - Every Kindred admin command string moves into a pure `Services/Kindred/KindredAdminCommands.cs` (linked into the
    tests). The UI and `MessageService_Processing` reference those constants, so the strings the tests check are the
    strings sent.
  - A test checks each template against `tests/fixtures/kindredcommands-commands.json` /
    `kindredlogistics-commands.json`, reusing GuideTests' `InvocationOk` / `ArgsMatch`. The checks are: the real
    invocation and literal tokens, the order and count of `{placeholders}` against the source parameters, and
    whether each parameter is required or optional.
  - The manifests already record source path and version, and Release preflight 10b fails when they are stale, so
    provenance is already covered.
- **Guide:** update `KindredGuide.cs:98-114` (the "known issues" callout) to the fixed state.
- **Soulshard durability:** add a small `DecimalField : TextField`, used for durability 0.0-1.0. No such field exists
  today.
  - Its parse and format logic is pure (`VcfArg.TryDecimal`): invariant culture, accepts `.` or `,` as the decimal
    separator, trims, inclusive min / max, at most 3 decimal places, emits invariant text.
  - Tests cover both bounds, out of range, comma and dot input, malformed input, whitespace, precision, and the
    emitted text.

### 2. Uriel (`MainPanel.Uriel.cs:1369`)
- Remove the Force-purge plot button, and let the row close up.
- The guide keeps `forcepurgeplot` listed as a synonym (already correct).

### 3. Beelzebub (`MainPanel.Beelzebub.cs`, `BeelzSummonsOverlayPanel.cs`)
- **Danger zone (:1242, :1256):** the text says `.beelz clear CONFIRM` wipes captured abilities, transform unlocks,
  slot binds, hotkeys and presets, and points to Reset bar to keep the collection.
- **Slot list (:1594):** the 8-slot list (Primary 0 … Ultimate 7), matching `BeelzSlotLabel`.
- **Recall:**
  - remove the Recall button (:2408) and the overlay Recall button (:84);
  - fix the :2397 text;
  - change the `BeelzebubGuide.cs:375` pointer from "Hotkeys → Recall" to Stash.
- **Phase button (:2814):** cycles to the next phase the way `BeelzTransformOverlayPanel.cs:127` does
  (`BeelzClient.Phase(n)`), with a corrected tooltip.
  - Beelzebub has no atomic "advance" command. One shared in-flight lock in `BeelzClient` is used by both the panel
    button and the transform overlay. It is set on send and cleared when the observed phase changes, or after a 3 s
    timeout. While it is set, both surfaces ignore Phase clicks.

### 4. Bloodcraft (`MainPanel.cs`)
- "Shattered Bone" → "Greater Stygian Shards" (:3560, :4477, :4653).
- Experience prestige resets level to **0** (:5073). Weapon and blood prestige reset to **0** (:5090).
- **Class:**
  - retitle the form "Select first class (.class s)";
  - add a "Change class (.class c)" form with the 750-shard cost in its tooltip, using the existing unused
    `BCCOM_CLASS_CHANGE_FORMAT` (fix its comment);
  - Spell # gets `min: 0`, with the tooltip "0 = default spell (Veil of Shadow); 1..N as listed by List Spells;
    needs Toggle Shift on".
- The stale "spell picker arrives in a later phase" line (:4898) now points to the Class tab form.

### 5. Nyarlathotep editor background (`MainPanel.NyarEditor.cs:276`)
- **Cause:** a full rebuild creates the card with a purple "Tint" child, and a partial redraw (`NyarClear`) destroys
  every child, the tint included, which leaves gray.
- **Fix:** an untinted `AddCard(parent, "NyarEditor", padding: 8, innerSpacing: 3)`, so it is always
  `Theme.CardBackground`.
- (Review round 1: the name-based `NyarClear` guard is dropped. With no tint there is nothing to preserve, and
  preserving by name could keep stale objects.)

### 6. V Rising Basics + Game Guide → guide kit
- New `Services/Guides/VRisingBasicsGuide.cs` and `GameGuideGuide.cs` (`GuideKind.General`), registered in `Guides.cs`
  via partial `Add…` methods.
- **Content carried over with semantic equivalence**, not verbatim layout: every heading, paragraph, bullet and
  link survives (the 0.73.0 fixes included). The only editorial changes are structural:
  - open sections with paragraphs;
  - `Detail` blocks for each "Details —" / "With the mods —" block;
  - `Bullets` in place of the hand-typed "• " lines;
  - the patch heads-up becomes a Note callout;
  - Game Guide's link rows become `Links(...)` blocks.
- `MainPanel.cs:2093-2098` dispatch moves to `RenderGuide(page, Guides.Get(...))`; delete `BuildVRisingBasicsTab` /
  `BuildGameGuideTab`.
- **Content inventory (automated):** before deleting the builders, a script extracts every heading, paragraph,
  bullet, detail title and URL from them into `tests/fixtures/guide-migration/{vrisingbasics,gameguide}.json`
  (normalized: tags and bullet marks stripped, whitespace collapsed).
  - A test asserts that each entry appears in the new guide's normalized text. An entry deliberately reworded is
    listed in the fixture with its replacement and a reason.
  - The 0.73.0 content fixes are applied to the fixture as such entries.
- **Test gap closed:** GuideLint also lints `GLinks`. Each label is non-empty and passes the glyph lint; each URL
  passes `Uri.TryCreate` as Absolute, scheme `https`, no user info, no whitespace or control characters. Fix the
  `GLinks` comment ("Open" rows).
- Apply /impeccable reading-mode principles as on the other guides: a short purpose line, scannable headings, details
  folded.

### 7. Housekeeping
- Delete `resp1-4.md`; `git status` shows them untracked (`??`), and the exact paths are checked before deletion.
  Add `**/__pycache__/` to `.gitignore`.
- Update CHANGELOG(s) for 0.73.0 (Fixed section) and ROADMAP (remove the "Kindred Admin wrong commands" Planned row
  → Done).

## Review & verification
1. Add this addendum to `docs/plans/2026-10-17-bc-reset-guides/PLAN.md`, then run a ClaudeX runner review (read-only,
   ≤3 rounds), logged. Any material change comes back to Kris.
2. Build, then run the logic tests (new: Kindred template ⊆ manifest, GLinks lint, quoting helper incl. a `"` refusal)
   and Release preflight (glyph lint, manifests).
3. A fresh read-only Codex post-build inspection of the addendum diff (≤2 rounds), logged.
4. Deploy with the game closed: back up both current DLLs to timestamped copies
   (`Raphael.dll.<yyyyMMdd-HHmmss>.bak`) and verify each backup's hash equals its source before copying, copy to both
   paths, then verify that the SHA-256
   and AssemblyVersion of both destinations equal the build output. **In-game list for Kris** (layout and look
   can't be unit-tested in Unity: no test harness; exact command strings are tested via KindredAdminCommands):
   - Kindred Admin: Server and World tabs grey out for non-admins; Reload staff, Incoming Decay, Plots owned and Feed
     Settings work; there is no wipe section; rows have no gaps.
   - Beelzebub: Phase cycles; no Recall button; the danger-zone text is right.
   - Uriel: a single Purge plot.
   - Bloodcraft: Change class form; Spell # accepts 0; reset card + guides as before.
   - Nyarlathotep editor: stays gray through Edit, save, Undo and adding a wave.
   - V Rising Basics + Game Guide render on the kit, details fold, links open.
5. After Kris validates: commit `chore(release): v0.73.0`, then run Release preflight **on that commit, before
   pushing**. Then push to origin, verify that `rev-parse HEAD` == `ls-remote`, and package that exact SHA. Kris
   uploads to Thunderstore.

## Critical files
`MainPanel.KindredAdmin.cs`, `MessageService_Processing.cs`, `UI/Forms/FormBuilder.cs`,
`Services/Kindred/KindredAdminCommands.cs` (new), `Services/VcfArg.cs` (new), `UI/Forms/FormField.cs`, `MainPanel.Uriel.cs`, `MainPanel.Beelzebub.cs`,
`BeelzSummonsOverlayPanel.cs`, `BeelzTransformOverlayPanel.cs`, `Services/Beelzebub/BeelzClient.cs`, `MainPanel.cs`, `MainPanel.NyarEditor.cs`, `MainPanel.NyarControls.cs`,
`Services/Guides/{VRisingBasics,GameGuide}Guide.cs` (new), `Guides.cs`, `GuideModel.cs`, `KindredGuide.cs`,
`BeelzebubGuide.cs`, `tests/Raphael.Logic.Tests/*`.
