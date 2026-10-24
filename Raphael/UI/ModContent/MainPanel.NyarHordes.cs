using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Raphael.Services.Nyar;
using UnityEngine;

namespace Raphael.UI.ModContent;

// 0.73.0 (PLAN-NYAR014.md §3): the undead-hordes sections of the Nyarlathotep event editor (Nyarlathotep 0.14.0): an
// infestation, a horde march, and a spawn-waves event's destination. Built only from the editor's guarded controls
// (MainPanel.NyarControls.cs), so nothing here subscribes to a control: every value change goes through a helper that
// checks _nyarEdGen (a source test keeps it that way). Nothing here touches the left rail.
public partial class MainPanel
{
    private static readonly (string Label, string Value, string Tip)[] NyarKillDraws =
    {
        ("0", "0", "No new undead for this end of the draw."), ("1", "1", "One."), ("2", "2", "Two."), ("3", "3", "Three (the most)."),
    };

    // ---------------------------------------------------------------- infestation

    private void BuildNyarInfestationSection(GameObject ed)
    {
        AddSectionHeading(ed, "▸ Where the dead rise");
        NyarHowItWorks(ed, "infestation");
        NyarFieldStepper(ed, "action.origins.count", hint: "How many graveyards: hotspots of the faction's own NPCs.");
        NyarFieldStepper(ed, "action.origins.minNatives", hint: "A hotspot needs at least this many of the faction's NPCs.");
        NyarFieldStepper(ed, "action.origins.minSpacing", hint: "The least distance between two places.");
        NyarFieldStepper(ed, "action.origins.perOrigin", hint: "Undead that rise at each place when it starts.");
        BuildNyarCastleSwitch(ed);

        AddSectionHeading(ed, "▸ The living dead");
        NyarFieldStepper(ed, "action.hunters", hint: "The share of the undead that hunt the nearest player; the rest drift.");
        NyarFieldStepper(ed, "action.driftRadius", hint: "How far the others wander from their place.");
        NyarNote(ed, Muted("Hunters jump in straight lines, so a river or a cliff band holds many of them back. Unless castles may be " +
                           "entered (above), no hunter targets a player inside a castle territory."));

        AddSectionHeading(ed, "▸ Spread per kill");
        BuildNyarSpreadBlock(ed, "action.spread.", "Each undead killed may raise more.");
        NyarHordeNotes(ed, "The infestation's units, faction and pools live in events.json (edit it, then Reload from disk).");
    }

    // ---------------------------------------------------------------- horde march

    private void BuildNyarHordeMarchSection(GameObject ed, string id)
    {
        var load = _nyarForm.Load;
        AddSectionHeading(ed, "▸ Start");
        NyarHowItWorks(ed, "hordemarch");
        string now = load.StartKind switch
        {
            "point" => "a set spot on the map",
            "admin" => "where the admin who starts it stands",
            "hotspot" => $"the natives' hotspot nearest the destination, at least {NyarText(_nyarForm.Loaded("action.start.minDistance"))} m away",
            _ => "couldn't be read",
        };
        var sr = NyarRow(ed, "NyarEdStartRow");
        NyarCell(sr, "Starts at", 150, tip: "Where the horde rises (action.start / action.start.minDistance).");
        var hereBtn = AddUrielButton(sr, "NyarEdStartHere", "Start here",
            $"The horde rises at the spot you stand on (event set {id} action.start here). Sent at once.",
            () => NyarSendOne("action.start", "here", "start", reread: true), 100);
        hereBtn.SetEnabled(NyarCanSend);
        NyarCell(sr, Muted($"Now: {now}."), 200, flex: true);
        NyarFieldStepper(ed, "action.start.minDistance",
            hint: load.StartKind == "hotspot"
                ? "How far the starting hotspot is from the destination, at least."
                : "Setting a distance makes it start at the natives' hotspot nearest the destination, at least this far away — it replaces the start above.");

        AddSectionHeading(ed, "▸ Destination");
        BuildNyarDestinationRows(ed, id, allowNone: false);
        BuildNyarCastleSwitch(ed);

        AddSectionHeading(ed, "▸ Growth on the way");
        BuildNyarSpreadBlock(ed, "action.growth.", "Each kill on the march may add undead to the horde.",
            maxAdvice: () =>
            {
                int size = load.HordeSize;
                return size > 0 && int.TryParse(_nyarForm.Get("action.growth.max"), NumberStyles.None, CultureInfo.InvariantCulture, out var max) && max <= size
                    ? $"most alive ({max}) is at or below the horde's size ({size}), so it never grows."
                    : null;
            },
            maxHint: load.HordeSize > 0 ? $"Caps the horde's living count. The horde starts with {load.HordeSize}." : "Caps the horde's living count.");

        AddSectionHeading(ed, "▸ When it arrives (infestation)");
        NyarNote(ed, Muted("On arrival, after 120 s stuck, or once players have watched it for 30 s, the horde infests where it stands."));
        NyarFieldStepper(ed, "action.infest.hunters", hint: "The share that then hunts the nearest player; the rest drift.");
        NyarFieldStepper(ed, "action.infest.driftRadius", hint: "How far the others wander.");
        BuildNyarSpreadBlock(ed, "action.infest.spread.", "Each undead killed after it infests may raise more.");

        AddSectionHeading(ed, "▸ Bosses with the horde");
        NyarListEditor(ed, "action.bosses", bosses: true, waveList: false, "Bosses",
            "Up to 3 entries: a boss (1-3 of it) or a roll from a pool (1-5). With a roll, 5 bosses at most in all.");
        string size = load.HordeSize > 0 ? $"a horde of {load.HordeSize}" : "the horde";
        NyarHordeNotes(ed, $"The horde's units ({size}, {NyarLimits.HordeMarchMin}-{NyarLimits.HordeMarchMax} in all; a pool range counts at its min for the " +
                           "least and at its max for the most), faction and pools live in events.json (edit it, then Reload from disk).");
    }

    // ---------------------------------------------------------------- destination (spawn waves and horde march)

    /// <summary>A spawn-waves event's destination card: Destination here / No destination, then its radius, name and
    /// march speed (refused by the server until a destination is set).</summary>
    private void BuildNyarDestinationCard(GameObject ed, string id)
    {
        AddSectionHeading(ed, "▸ Destination (march)");
        NyarNote(ed, Muted("Each wave marches as one front from where it spawns to the destination, then does its behaviour there. " +
                           "A wave players have watched for 30 s stops and fights where it stands."));
        BuildNyarDestinationRows(ed, id, allowNone: true);
    }

    private void BuildNyarDestinationRows(GameObject ed, string id, bool allowNone)
    {
        var load = _nyarForm.Load;
        bool has = _nyarForm.Get("action.destination") == "here";
        // A conflict the reply shows for certain blocks Destination here; the server refuses it anyway.
        string blocker = allowNone ? NyarDestinationBlocker(load.DestinationConflicts) : null;

        var dr = NyarRow(ed, "NyarEdDestRow");
        NyarCell(dr, "Destination", 150, tip: "Where the units march to (action.destination).");
        var here = AddUrielButton(dr, "NyarEdDestHere", has ? "Move it here" : "Destination here",
            $"Make the spot you stand on the destination (event set {id} action.destination here). Sent at once.",
            () => NyarSendOne("action.destination", "here", "destination", reread: true), 130);
        here.SetEnabled(NyarCanSend && blocker == null);
        if (allowNone && has)
        {
            var none = AddUrielConfirmButton(dr, "NyarEdDestNone", "No destination",
                $"Remove the destination and the march speed (event set {id} action.destination none).",
                () => NyarSendOne("action.destination", "none", "no destination", reread: true), 120, new Color(0.55f, 0.15f, 0.15f, 1f));
            none.SetEnabled(NyarCanSend);
        }
        NyarCell(dr, Muted(has ? "Now: set." : "Now: none."), 90, flex: true);
        // 0.74.0 (friend test): a greyed button always says why, and what to do about it.
        string wait = NyarSendBlockedText();
        if (wait != null) NyarNote(ed, $"<color={NyarChangedHex}>Destination here is greyed out for now: {wait}</color>");
        if (blocker != null) NyarNote(ed, $"<color={NyarChangedHex}>{blocker}</color>");
        else if (allowNone)
            NyarNote(ed, Muted("A destination needs units that spawn at a set spot (Location: Use where I stand, or where the admin " +
                               "starts it), and no pursuit, region lock, arena or fan-out. Stand at the destination, then press. Sent at once."));
        else NyarNote(ed, Muted("Stand at the destination, then press. Sent at once."));

        if (!has && allowNone)
        {
            NyarNote(ed, Muted("Arrival radius, name and march speed appear here once the event has a destination: " +
                               "the server refuses them without one."));
            return;
        }
        NyarFieldStepper(ed, "action.destination.radius", hint: "Units within this distance of the destination have arrived.");
        NyarTextField(ed, "action.destination.name", 220, "e.g. the old bridge", "What the arrival banners call it (1-40 characters).");
        NyarFieldStepper(ed, "action.marchSpeed", hint: "Metres a second. Players walk at about 5.");
    }

    // Why the editor's send-at-once buttons are greyed right now (a batch running, or the server's last answer awaited);
    // null when they aren't.
    private string NyarSendBlockedText()
    {
        if (NyarBatchRunner.Busy) return "your saved changes are still being sent. It turns back on when they finish.";
        if (NyarClient.Quarantined) return NyarBatchRunner.WaitText;
        return null;
    }

    // The reason Destination here would be refused, from what the reply shows for certain; null when nothing does.
    // 0.74.0 (friend test): one step per conflict, each naming where in the editor it is fixed.
    internal static string NyarDestinationBlocker(List<string> conflicts)
    {
        if (conflicts == null || conflicts.Count == 0) return null;
        var steps = new List<string>();
        foreach (var c in conflicts.Distinct())
            steps.Add(c switch
            {
                "location" => "• Location: the units spawn across a region or around a player, and a march needs a set starting spot. " +
                              "Under Where and how, stand where the units should spawn and press Use where I stand.",
                "pursuit" => "• Pursuit is on. Under Where and how, set Pursuit to no pursuit, then Save.",
                "region lock" => "• Region lock is on. Under Any other field, pick action.regionLock and set it to No.",
                "arena" => "• The event has an arena. Under Any other field, pick action.arena and type none.",
                _ => "• Fan-out is on. Under Any other field, pick action.fanOut and type none.",
            });
        string head = steps.Count == 1
            ? "Destination here is greyed out: the server refuses a destination on this event until this is changed:"
            : $"Destination here is greyed out: the server refuses a destination on this event until these {steps.Count} are changed:";
        return head + "\n" + string.Join("\n", steps) + "\nThe button turns on once the event reads back without them.";
    }

    // ---------------------------------------------------------------- spread block (spread, growth, infest spread)

    /// <summary>One spread-shaped block: "per kill, at least / at most" (0-3, kept min ≤ max), most alive, per minute and
    /// the pool the new units are drawn from.</summary>
    private void BuildNyarSpreadBlock(GameObject ed, string block, string intro, Func<string> maxAdvice = null, string maxHint = null)
    {
        string minKey = block + "perKillMin", maxKey = block + "perKillMax";
        int Int(string k) => int.TryParse(_nyarForm.Get(k), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
        int min = Int(minKey), max = Int(maxKey);
        NyarNote(ed, Muted(intro + (min >= 0 && max >= 0 ? $" Now: {NyarKillDrawText(min, max)}." : "")));

        // Stepping one end past the other moves the other with it, so the pair is always valid.
        NyarChoice(ed, block + "min", "Per kill, at least", $"The fewest undead a kill adds ({minKey}).", NyarKillDraws,
            min >= 0 ? min.ToString(CultureInfo.InvariantCulture) : "",
            v => { _nyarForm.Set(minKey, v); if (Int(maxKey) >= 0 && Int(maxKey) < Int(minKey)) _nyarForm.Set(maxKey, v); },
            null, helpKey: minKey);
        var mostUi = NyarChoice(ed, block + "max", "Per kill, at most", $"The most undead a kill adds ({maxKey}).", NyarKillDraws,
            max >= 0 ? max.ToString(CultureInfo.InvariantCulture) : "",
            v => { _nyarForm.Set(maxKey, v); if (Int(minKey) > Int(maxKey)) _nyarForm.Set(minKey, v); },
            new[] { minKey, maxKey }, "A kill adds a random number between the two.", helpKey: maxKey);
        if (mostUi != null)
        {
            mostUi.Advice = () => Int(minKey) > Int(maxKey) && Int(maxKey) >= 0 ? "at most is below at least — raise it (the server disables the event otherwise)." : null;
            NyarRefreshUi(mostUi);
        }
        NyarFieldStepper(ed, block + "max", hint: maxHint ?? "The most of these undead alive at once.", advice: maxAdvice);
        NyarFieldStepper(ed, block + "perMinute", hint: "The most that rise in one minute.");
        BuildNyarSpreadPool(ed, block + "pool");
    }

    private static string NyarKillDrawText(int min, int max) => min == max ? $"+{min} per kill" : $"+{min}-{max} per kill";

    private void BuildNyarSpreadPool(GameObject ed, string field)
    {
        string cur = _nyarForm.Get(field);
        var head = NyarRow(ed, "NyarSpreadPool_" + field);
        NyarCell(head, "Drawn from", 150, tip: NyarTooltip(field, null, fallback: "The pool the new undead are drawn from, or no pool."));
        NyarCell(head, Muted(cur.Length == 0 ? "" : cur == "none" ? "the event's units" : $"pool <b>{NyarEscape(cur)}</b>"), 160, flex: true);
        var ui = NyarTail(head, "drawn from", "No pool: drawn from the event's units, by their weights.", field);
        var chips = new List<(string, bool, string, Action, int)>
        {
            ("No pool", cur == "none", "Draw from the event's units.", () => { _nyarForm.Set(field, "none"); RedrawNyarEditor(); }, 80),
        };
        var pools = NyarPoolNames();
        // 0.74.0: library pools the event already names (pools.json, Nyarlathotep 0.15.0) are offered too.
        foreach (var lp in _nyarForm.Load.LibraryPools) if (!pools.Contains(lp)) pools.Add(lp);
        if (cur.Length > 0 && cur != "none" && !pools.Contains(cur)) pools.Insert(0, cur);
        foreach (var p in pools)
        {
            string pn = p;
            chips.Add((pn, cur == pn, $"Draw from pool {pn}.", () => { _nyarForm.Set(field, pn); RedrawNyarEditor(); }, 0));
        }
        NyarChipRows(ed, "NyarSpreadPoolPick_" + field, "", chips, 4);
        ui.Msg = NyarNote(ed);
        NyarRefreshUi(ui);
    }

    // The notes every apocalypse-type section ends with.
    private void NyarHordeNotes(GameObject ed, string where)
    {
        NyarNote(ed, Muted(where));
        NyarNote(ed, Muted("Kills, spread and growth happen only near players: the game pauses its own NPCs 80 m or more from every player. " +
                           "One apocalypse-type event (apocalypse, infestation, horde march) runs at a time."));
        NyarNote(ed, Muted(NyarHordeCapHint));
    }

    /// <summary>0.74.0: the server's cfg trims a big wave; shown under a spawn-waves event's unit lists (NyarLimits).</summary>
    private static string NyarCapHint =>
        $"The server's cfg caps each wave at MaxUnitsPerWave (default {NyarLimits.PerWaveDefault}, up to {NyarLimits.PerWaveMax}; spawn waves only) " +
        $"and all of Nyarlathotep's units at MaxTrackedUnits (default {NyarLimits.TrackedDefault} on Nyarlathotep 0.15.0, 150 before; up to {NyarLimits.TrackedMax}). " +
        "A wave it trims says \"capped\". A cfg change needs a server restart.";

    /// <summary>0.74.0: the undead-hordes version: a horde is held by MaxTrackedUnits only.</summary>
    private static string NyarHordeCapHint =>
        $"A horde is held by MaxTrackedUnits in the server's cfg (default {NyarLimits.TrackedDefault} on Nyarlathotep 0.15.0, 150 before; up to {NyarLimits.TrackedMax}). " +
        $"MaxUnitsPerWave doesn't apply to it. The event's own units are checked against {NyarLimits.UnitCeiling} in all when it loads. " +
        "A start it trims says \"capped\". A cfg change needs a server restart.";
}
