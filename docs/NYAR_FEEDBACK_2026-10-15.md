# Raphael → Nyarlathotep: in-game feedback (2026-10-15)

**From:** Raphael 0.71.0, tested in game against Nyarlathotep `0.13.0-rc.1` (api 8) on the dev server.
**Source:** Kris's play session (client log), plus a read of the Nyarlathotep source. Raphael changed nothing in the
Nyarlathotep workspace.

Items 1-3 are bugs. Items 4-7 are requests, most important first.

## 1. Bug: an apocalypse whose goal is "the starting admin" can never start

- `zombie-apocalypse` (from the template, `goal: the living, radius 30, at the starting admin`) always answers
  `[NYAR:err] cmd=event verb=start code=badarg arg=location reason=admin_location`. Both `.nyar event start` and
  the twin fail the same way.
- **Cause:** `Logic/AdminFlows.cs:83-96` reads the admin's position only when
  `def.Action?.Location.Type == LocationType.Admin`. An apocalypse has `Action == null` (`Logic/Model.cs:209-221`),
  so `origin` stays null and `Logic/Engine.cs:391-394` always refuses it.
- **Fix:** also read the position when `def.Apocalypse?.Goal.Location.Type == LocationType.Admin`.
- The refusal text "start it with .nyar event start" is misleading, because that is the command that just failed.
- No scenario covers this. `tools/vrclient/scenarios/apocalypse.vrs` always sets a fixed-point goal first.
- **Raphael 0.71.0 workaround:** the error explains it, and the event editor has a **Goal here** button
  (`event set <id> action.goal here`).

## 2. Bug: the kill switch sends no `event-end` per event

`Engine.cs:566-576` sends only `[NYAR:ev] type=killswitch`, but handoff API8 §7 step 7 says it sends both. Raphael
re-reads status on the killswitch push, so it copes. Please either send the per-event `event-end` lines or correct
the handoff.

## 3. Please verify: `api status` reports `units=0` for running wave events

In the log, `bandit-ambush` showed `units=0` at every status read through waves 1-3 (`left=600` down to `58`),
while it was spawning 6 units a wave. `spider-swarm` showed 0-1. `undead-nightfall` (empower) showed 68-71 as
expected. Either the count misses spawned wave units, or the units died or despawned right away. Raphael's new
**Admin: Units** tab shows this count per event.

Related: `[NYAR:def]` for `zombie-apocalypse` says `action=waves`, so a client can't tell an apocalypse from spawn
waves. Please send `action=apocalypse`. Raphael now detects it from the `event info` text.

## 4. Request: a map-wide units read (contract §10, live ops)

Kris needs to see every live event unit on the map with coordinates, to find stray or stuck units. Today only the
human `.nyar debug here [5-100]` exists. Raphael 0.71.0 parses it into a table on the new **Admin: Units** tab, but
it only covers 100 m around the admin.

The planned shape from the contract would let Raphael list the whole map:

```
.nyar api units [event=<id>] [src=spawn|empower] [page]
[NYAR:unit] key=<stable> src=spawn prefab=CHAR_… event=<id|-> wave=<n|-> lvl= hp= maxhp= left= watch=… x= y= z= region=
[NYAR:end] cmd=units page=<cur>/<total> count=<n>
```

Also requested: `.nyar api unit goto|despawn|unstick <key>`, so the table can teleport the admin to a unit or remove
it. Raphael will gate on whatever api number this ships under.

## 5. Request: a field read for the event editor (contract §10.5)

Raphael 0.71.0 has an inline editor per event. Without a machine read, it sends `.nyar event info <id>`, shows the
reply and parses what it can from the English text. The parse breaks if that text changes. Please ship
`.nyar api fields event <id>`, returning `[NYAR:field] id= key= value=<set-form value>`, so every field pre-fills
exactly.

## 6. Request: tell clients when an ending event is gone

After `event-end`, status shows the event as `state=ending left=30` (units leaving), and no push follows when that
finishes. Raphael now re-reads status when the countdown runs out. A push or a `grace=<s>` field on `event-end`
would remove the guesswork.

## 7. Small: name the pillar in `pillar_off`

`code=disabled arg=pillar reason=pillar_off` doesn't say which pillar. Adding `id=spawns` (or `pillar=spawns`) would
let Raphael say "turn on the Spawns pillar". Today it says "this event's pillar is switched off".

## Not Nyarlathotep: fixed in Raphael 0.71.0

- The admin reads (`api events`, `templates`, `pillar list`, `killswitch`) went out for non-admins when the tabs were
  pre-built, which produced `[vcf] [denied] …` lines. Raphael now sends them only on `admin=1`.
- The "Command not found: .beelz/.uriel/.faust api version" lines from Raphael's own probes are now hidden.
- The event list now updates by itself after an event ends.
