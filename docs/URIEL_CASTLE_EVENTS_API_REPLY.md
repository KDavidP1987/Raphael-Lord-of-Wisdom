# Raphael → Uriel: reply on the Castle Events wire API (handoff §7)

**From:** Raphael 0.70.0 (2026-10-15), which ships the Castle Events UI on today's human-text replies.
**About:** `Uriel/Uriel/docs/RAPHAEL_CASTLE_EVENTS_HANDOFF.md` §7, "Raphael: confirm or amend this before Uriel
builds it."

**Verdict: confirmed, with six amendments.** The shape (header line, `key=value` rows, `[URIEL:end]`) matches
what `UrielWireParser` already reads, and none of the amendments needs a parser change.

## What Raphael 0.70.0 does today

- Sends every command as visible chat through `UrielClient.SendEvent`. It refuses lines over 118 characters.
- Reads replies and broadcasts in `Services/Uriel/UrielEvents.cs`. It never destroys a line. Its regexes are
  based on the reply starts in handoff §3 and the broadcasts in §5. **Please treat those strings as frozen**
  until the wire API lands, or tell Raphael when one changes.
  - Availability comes from the `disabled by the server admin` and `admin-only` refusals. The tab sends
    `.uriel event list` silently to check.
  - The run readout comes from the §5 broadcasts, and from the `Event started from` and `Event running` replies.
- Already parses `events=` and `playerevents=` on `[URIEL:version]` when they appear. Adding them needs no
  Raphael release.

## Amendments

1. **Split `cleared=<c>/<G>`** into `cleared=<c> groups=<G>`. Every other field holds one number, and this keeps
   `GetInt` working.
2. **Add the caller's rights to `[URIEL:layout]`:** `canedit=0|1`, `floor=<the floor the caller stands on>`,
   and `markers=<M> maxmarkers=<max>`. Raphael can then grey out the layout buttons on someone else's castle and
   show the marker cap.
3. **Add the costs to the version line, or to one `[URIEL:evconfig]` line:** `markercost=<item>:<n>`,
   `bossmarkercost=<item>:<n>`, `runcost=<item>:<n>` (`0` = free), and `defaultcontain=<c>`. Handoff §8 says
   Raphael should show these, but they aren't on the wire.
4. **Cap the length of `[URIEL:sqentry] units=`.** Fifty GUIDs make a line of about 600 characters. Above about
   400, split the list across several rows with `part=<k>`. Raphael joins rows that share the same
   `squad/set/entry`.
5. **Add a push line for each §5 broadcast.** Send a `[URIEL:evrun]` line alongside each human message, with
   `event=begun|inplace|group|bosses|complete|warn|timeup|stopped territory=<t> seconds=<s>`, plus `group=`,
   `left=` or `limit=` where they apply. Raphael's HUD would then stop depending on English text. Keep the
   human line for players without Raphael.
6. **Allow a `cmd=` echo on the `[URIEL:err]` rows** for event commands, e.g. `cmd=event.marker
   reason=nodoor|notplot|notowner|running|cap|cost|badname|nounit|ambiguous|toomany`. Raphael can then show the
   refusal next to the button that caused it.

Not needed: run history over the wire. The human `history` reply is enough for now.

## When Uriel ships it

Bump `ApiVersion` to 2 (or name the version). Raphael will gate on `SupportsEventsApi => ApiVersion >= N`. It
will keep the text parsing as the fallback for 0.21.x servers.
