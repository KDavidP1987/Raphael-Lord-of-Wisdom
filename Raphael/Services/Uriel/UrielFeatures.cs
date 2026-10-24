using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Raphael.Services.Uriel;

// 0.73.0: Uriel 0.22.0 feature switches and admin kill switches (PLAN-URIEL022.md). PURE (no Unity, no game) so the
// logic tests link it. Uriel has no machine line for its switches yet (ApiVersion stays 1), so everything here reads
// the human text verbatim from Uriel 0bad5fe: KillSwitchCommands.cs (features / killswitch / purgeall),
// KillSwitch.RestoreVanilla (the result lines), EventCommands.cs:93 (stopall), UnitSpawnService.cs:650-655 (unit
// purge) and the refusal lines in Share/Stair/Unit/Event commands and PublicStorageService.cs:1537.
// Callers pass text that is already tag-stripped and trimmed (UrielFeatureState.Observe does that).

public enum UrielFeature { Master, Stairs, Objects, Events, Doors, Chests, Cells, Costs }

/// <summary>Unknown = no reading yet. Unknown is treated as usable: 0.21.0 servers have no features command.</summary>
public enum UrielSwitch { Unknown, On, Off }

/// <summary>One parsed `.uriel features` reply. The values are Uriel's effective states.</summary>
public sealed class UrielFeatureSnapshot
{
    public readonly Dictionary<UrielFeature, UrielSwitch> Values = new();
    public bool ObjectsAdminOnly;
    public bool EventsAdminOnly;
}

/// <summary>A refusal line: the feature is off, or (for players) admin-only.</summary>
public readonly struct UrielRefusal
{
    public readonly UrielFeature Feature;
    public readonly bool AdminOnly;
    public UrielRefusal(UrielFeature feature, bool adminOnly) { Feature = feature; AdminOnly = adminOnly; }
}

public static class UrielFeatures
{
    // ---------------------------------------------------------------- `.uriel features` (KillSwitchCommands.Features)
    private static readonly Regex FeaturesRx = new(
        @"^Uriel features:(?<master> Master switch \[General\] Enabled is OFF: everything below is off\.)?" +
        @" Stair swap (?<stairs>on|OFF)\. Object spawning (?<objects>on|OFF)(?<objAdmin> \(admins only\))?\." +
        @" NPC units and castle events (?<events>on|OFF)(?<evAdmin> \(admins only\))?, event doors (?<doors>on|OFF)\." +
        @" Public chests (?<chests>on|OFF), prison cells (?<cells>on|OFF), paid shares (?<costs>on|OFF)\." +
        @" Changes to the config take effect at the next server start\.$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Reads a features reply. Null unless every part is there (no guessing).</summary>
    public static UrielFeatureSnapshot Parse(string line)
    {
        if (string.IsNullOrEmpty(line) || !line.StartsWith("Uriel features:", StringComparison.Ordinal)) return null;
        var m = FeaturesRx.Match(line);
        if (!m.Success) return null;
        var s = new UrielFeatureSnapshot();
        UrielSwitch V(string g) => m.Groups[g].Value == "on" ? UrielSwitch.On : UrielSwitch.Off;
        s.Values[UrielFeature.Master]  = m.Groups["master"].Success ? UrielSwitch.Off : UrielSwitch.On;
        s.Values[UrielFeature.Stairs]  = V("stairs");
        s.Values[UrielFeature.Objects] = V("objects");
        s.Values[UrielFeature.Events]  = V("events");
        s.Values[UrielFeature.Doors]   = V("doors");
        s.Values[UrielFeature.Chests]  = V("chests");
        s.Values[UrielFeature.Cells]   = V("cells");
        s.Values[UrielFeature.Costs]   = V("costs");
        s.ObjectsAdminOnly = m.Groups["objAdmin"].Success;
        s.EventsAdminOnly  = m.Groups["evAdmin"].Success;
        return s;
    }

    // ---------------------------------------------------------------- refusals
    private static readonly (string Text, UrielFeature Feature, bool AdminOnly)[] Refusals =
    {
        ("Stair swapping is disabled by the server admin.",                              UrielFeature.Stairs,  false),
        ("Object spawning is disabled by the server admin (ObjectSpawn.Enabled).",        UrielFeature.Objects, false),
        ("Castle events are disabled by the server admin (CastleEvents.Enabled).",        UrielFeature.Events,  false),
        ("Event doors are disabled by the server admin (CastleEvents.DoorGates).",        UrielFeature.Doors,   false),
        ("Public storage is disabled by the server admin.",                               UrielFeature.Chests,  false),
        ("Prison-cell sharing is disabled by the server admin (PublicStorage.PrisonEnabled).", UrielFeature.Cells, false),
        ("Prison-cell sharing is disabled by the server admin.",                          UrielFeature.Cells,   false),
        ("Paid shares are disabled by the server admin (PublicStorage.CostsEnabled).",    UrielFeature.Costs,   false),
        // PublicStorageService.cs:1537, sent with Uriel's "[Uriel] " prefix (the dash is an em dash).
        ("[Uriel] This container has a price, and paid shares are switched off on this server — it's closed until its owner removes the price.", UrielFeature.Costs, false),
        ("Object spawning is admin-only on this server (ObjectSpawn.AdminOnly).",         UrielFeature.Objects, true),
        ("Castle events are admin-only on this server (CastleEvents.PlayerEvents).",      UrielFeature.Events,  true),
    };

    /// <summary>An exact refusal line, or null. The two "admin-only" lines mean admin-only, not off.</summary>
    public static UrielRefusal? ClassifyRefusal(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        foreach (var r in Refusals)
            if (string.Equals(line, r.Text, StringComparison.Ordinal)) return new UrielRefusal(r.Feature, r.AdminOnly);
        return null;
    }

    // ---------------------------------------------------------------- names, keys, dependencies
    public static readonly UrielFeature[] All =
        { UrielFeature.Master, UrielFeature.Stairs, UrielFeature.Objects, UrielFeature.Events, UrielFeature.Doors,
          UrielFeature.Chests, UrielFeature.Cells, UrielFeature.Costs };

    public static string Name(UrielFeature f) => f switch
    {
        UrielFeature.Master  => "Uriel (master switch)",
        UrielFeature.Stairs  => "Stair swap",
        UrielFeature.Objects => "Object spawning",
        UrielFeature.Events  => "NPC units and castle events",
        UrielFeature.Doors   => "Event doors",
        UrielFeature.Chests  => "Public chests",
        UrielFeature.Cells   => "Prison cells",
        UrielFeature.Costs   => "Paid shares",
        _ => f.ToString(),
    };

    public static string ConfigKey(UrielFeature f) => f switch
    {
        UrielFeature.Master  => "[General] Enabled",
        UrielFeature.Stairs  => "[StairSwap] Enabled",
        UrielFeature.Objects => "[ObjectSpawn] Enabled",
        UrielFeature.Events  => "[CastleEvents] Enabled",
        UrielFeature.Doors   => "[CastleEvents] DoorGates",
        UrielFeature.Chests  => "[PublicStorage] Enabled",
        UrielFeature.Cells   => "[PublicStorage] PrisonEnabled",
        UrielFeature.Costs   => "[PublicStorage] CostsEnabled",
        _ => "",
    };

    /// <summary>The feature a feature needs (cells and costs need chests, doors need events), or null.</summary>
    public static UrielFeature? Parent(UrielFeature f) => f switch
    {
        UrielFeature.Cells or UrielFeature.Costs => UrielFeature.Chests,
        UrielFeature.Doors => UrielFeature.Events,
        _ => null,
    };
}

/// <summary>What Raphael knows about the server's switches. Pure; UrielFeatureState wraps it with an event.</summary>
public sealed class UrielFeatureModel
{
    private readonly Dictionary<UrielFeature, UrielSwitch> _raw = new();
    public bool ObjectsAdminOnly { get; private set; }
    public bool EventsAdminOnly { get; private set; }
    /// <summary>A player was told "admin-only" (only players get that line).</summary>
    public bool ObjectsRefusedAdminOnly { get; private set; }
    public bool HasSnapshot { get; private set; }

    public UrielSwitch Raw(UrielFeature f) => _raw.TryGetValue(f, out var v) ? v : UrielSwitch.Unknown;

    /// <summary>A features reply replaces everything, either way. Returns true if anything changed.</summary>
    public bool Apply(UrielFeatureSnapshot s)
    {
        if (s == null) return false;
        bool changed = !HasSnapshot || ObjectsAdminOnly != s.ObjectsAdminOnly || EventsAdminOnly != s.EventsAdminOnly;
        foreach (var kv in s.Values)
        {
            if (Raw(kv.Key) != kv.Value) changed = true;
            _raw[kv.Key] = kv.Value;
        }
        ObjectsAdminOnly = s.ObjectsAdminOnly; EventsAdminOnly = s.EventsAdminOnly;
        ObjectsRefusedAdminOnly = false;
        HasSnapshot = true;
        return changed;
    }

    /// <summary>A refusal only ever turns a feature off (or marks it admin-only). True if anything changed.</summary>
    public bool Apply(UrielRefusal r)
    {
        if (r.AdminOnly)
        {
            if (r.Feature != UrielFeature.Objects || ObjectsRefusedAdminOnly) return false;
            ObjectsRefusedAdminOnly = true;   // castle events' admin-only is already handled by UrielEvents
            return true;
        }
        return SetOff(r.Feature);
    }

    /// <summary>`[URIEL:version] objectspawn=0` (it follows the master switch since 0.22.0).</summary>
    public bool SetOff(UrielFeature f)
    {
        if (Raw(f) == UrielSwitch.Off) return false;
        _raw[f] = UrielSwitch.Off;
        return true;
    }

    /// <summary>The state after the dependencies: everything needs the master, cells and costs need chests, doors
    /// need events. Unknown stays Unknown.</summary>
    public UrielSwitch Effective(UrielFeature f)
    {
        if (f != UrielFeature.Master && Raw(UrielFeature.Master) == UrielSwitch.Off) return UrielSwitch.Off;
        var parent = UrielFeatures.Parent(f);
        if (parent.HasValue && Raw(parent.Value) == UrielSwitch.Off) return UrielSwitch.Off;
        return Raw(f);
    }

    /// <summary>Whether the controls for <paramref name="f"/> are usable, and why not. The reason names the switch
    /// that is actually off (the master or the parent before the feature itself).</summary>
    public (bool Enabled, string Reason) Gate(UrielFeature f)
    {
        if (f != UrielFeature.Master && Raw(UrielFeature.Master) == UrielSwitch.Off)
            return (false, "Uriel is switched off on this server ([General] Enabled).");
        var parent = UrielFeatures.Parent(f);
        if (parent.HasValue && Raw(parent.Value) == UrielSwitch.Off) return (false, OffReason(parent.Value));
        if (Raw(f) == UrielSwitch.Off) return (false, OffReason(f));
        if (f == UrielFeature.Objects && ObjectsRefusedAdminOnly)
            return (false, "Object spawning is admin-only on this server (ObjectSpawn.AdminOnly).");
        return (true, "");
    }

    private static string OffReason(UrielFeature f)
        => $"{UrielFeatures.Name(f)} {(f == UrielFeature.Costs || f == UrielFeature.Chests || f == UrielFeature.Cells || f == UrielFeature.Doors || f == UrielFeature.Events ? "are" : "is")} off on this server ({UrielFeatures.ConfigKey(f)}).";

    public void Clear()
    {
        _raw.Clear();
        ObjectsAdminOnly = EventsAdminOnly = ObjectsRefusedAdminOnly = HasSnapshot = false;
    }
}

// ==================================================================================== admin kill switches
public enum UrielAction { StopEvents, PurgeUnits, PurgeObjects, KillSwitch }

public enum UrielStep
{
    Idle,
    ArmSent,        // waiting for the arm line (10 s)
    Armed,          // the server armed it; Confirm usable until arm-send + 25 s
    ConfirmPending, // first press of Confirm (3 s for the second)
    ConfirmSent,    // waiting for the result (10 s)
    Done,
    Partial,        // kill switch done, with a "… failed (…)" line
    NoReply,
    Expired,        // the window passed, or the server said "Not armed"
    Refused,        // "Uriel is not yet initialized."
}

public enum UrielReplyKind { Arm, Done, NotArmed, Failure, Note, NotReady }

/// <summary>A recognised control reply. <see cref="Action"/> is null only for NotReady (it names no command).</summary>
public readonly struct UrielControlReply
{
    public readonly UrielAction? Action;
    public readonly UrielReplyKind Kind;
    public readonly string Domain;   // Failure: Events | Units | Objects | Storage
    public UrielControlReply(UrielAction? action, UrielReplyKind kind, string domain = "")
    { Action = action; Kind = kind; Domain = domain ?? ""; }
}

/// <summary>The four Server controls buttons as a state machine (PLAN-URIEL022.md §4). Time is passed in (seconds),
/// so it is testable and needs no Unity timers; the panel's ticker calls <see cref="Tick"/> while <see cref="AnyActive"/>.
/// Only replies this client asked for move a slot: a slot accepts a reply only while it is waiting for one.</summary>
public sealed class UrielControlFlow
{
    public const double ReplyWait = 10, ArmWindow = 25, PressWindow = 3, GatherWindow = 3;
    public const int MaxUnsolicited = 4;

    private sealed class Slot
    {
        public UrielStep Step;
        public double SentAt, ArmUntil = -1, PressUntil = -1, GatherUntil = -1;
        public UrielStep BeforePress;
        public readonly List<string> Lines = new();
        public readonly List<string> Failed = new();
    }

    private readonly Dictionary<UrielAction, Slot> _slots = new();
    private readonly List<string> _unsolicited = new();

    public UrielControlFlow()
    {
        foreach (UrielAction a in Enum.GetValues(typeof(UrielAction))) _slots[a] = new Slot();
    }

    public UrielStep Step(UrielAction a) => _slots[a].Step;
    public IReadOnlyList<string> Lines(UrielAction a) => _slots[a].Lines;
    public IReadOnlyList<string> FailedDomains(UrielAction a) => _slots[a].Failed;
    /// <summary>Control replies seen in chat that these buttons didn't ask for (an admin typed the command).</summary>
    public IReadOnlyList<string> Unsolicited => _unsolicited;
    public bool Gathering(UrielAction a, double now) => _slots[a].GatherUntil > now;

    public static bool HasArm(UrielAction a) => a != UrielAction.StopEvents;

    public static string Command(UrielAction a, bool confirm) => a switch
    {
        UrielAction.StopEvents   => ".uriel event stopall",
        UrielAction.PurgeUnits   => confirm ? ".uriel unit purge confirm" : ".uriel unit purge",
        UrielAction.PurgeObjects => confirm ? ".uriel purgeall confirm" : ".uriel purgeall",
        UrielAction.KillSwitch   => confirm ? ".uriel killswitch confirm" : ".uriel killswitch",
        _ => "",
    };

    /// <summary>Seconds left to confirm (0 when not armed).</summary>
    public double ArmSecondsLeft(UrielAction a, double now)
    {
        var s = _slots[a];
        return s.Step is UrielStep.Armed or UrielStep.ConfirmPending && s.ArmUntil > now ? s.ArmUntil - now : 0;
    }

    /// <summary>Confirm (or Stop all events) is pressable.</summary>
    public bool CanConfirm(UrielAction a)
    {
        var st = _slots[a].Step;
        if (!HasArm(a)) return st is not (UrielStep.ConfirmSent);
        return st is UrielStep.Armed or UrielStep.ConfirmPending;
    }

    /// <summary>Arm is pressable unless a reply is awaited.</summary>
    public bool CanArm(UrielAction a) => HasArm(a) && _slots[a].Step is not (UrielStep.ArmSent or UrielStep.ConfirmSent);

    public bool AnyActive
    {
        get
        {
            foreach (var s in _slots.Values)
                if (s.Step is UrielStep.ArmSent or UrielStep.Armed or UrielStep.ConfirmPending or UrielStep.ConfirmSent
                    || s.GatherUntil >= 0) return true;
            return false;
        }
    }

    /// <summary>Call after sending the arm command. A new arm restarts the slot.</summary>
    public void ArmSent(UrielAction a, double now)
    {
        if (!HasArm(a)) return;
        var s = Fresh(a);
        s.Step = UrielStep.ArmSent; s.SentAt = now; s.ArmUntil = now + ArmWindow;
    }

    /// <summary>A press of Confirm (or Stop all events). True on the second press within 3 s: the caller sends the
    /// command and calls <see cref="ConfirmSent"/>.</summary>
    public bool Press(UrielAction a, double now)
    {
        var s = _slots[a];
        if (!CanConfirm(a)) return false;
        if (HasArm(a) && now >= s.ArmUntil) return false;   // the window is over; the next Tick shows Expired
        if (s.Step == UrielStep.ConfirmPending && now <= s.PressUntil) return true;
        s.BeforePress = HasArm(a) ? UrielStep.Armed : (s.Step == UrielStep.ConfirmPending ? s.BeforePress : s.Step);
        s.Step = UrielStep.ConfirmPending;
        s.PressUntil = HasArm(a) ? Math.Min(now + PressWindow, s.ArmUntil) : now + PressWindow;
        return false;
    }

    public void ConfirmSent(UrielAction a, double now)
    {
        var s = _slots[a];
        s.Lines.Clear(); s.Failed.Clear(); s.GatherUntil = -1; s.PressUntil = -1;
        s.Step = UrielStep.ConfirmSent; s.SentAt = now;
    }

    /// <summary>Feed one system line. True if a slot or the unsolicited list changed.</summary>
    public bool OnLine(string line, double now)
    {
        var r = ClassifyReply(line);
        if (r == null) return false;
        var reply = r.Value;

        if (reply.Kind == UrielReplyKind.NotReady)
        {
            // The line names no command, so it is matched only when exactly one of the two is waiting.
            Slot only = null; int waiting = 0;
            foreach (var a in new[] { UrielAction.KillSwitch, UrielAction.PurgeObjects })
                if (Waiting(_slots[a], now)) { only = _slots[a]; waiting++; }
            if (waiting != 1) return false;   // common to many commands; never shown as unsolicited
            only.Step = UrielStep.Refused; only.Lines.Clear(); only.Lines.Add(line);
            return true;
        }

        var act = reply.Action.Value;
        var slot = _slots[act];
        switch (reply.Kind)
        {
            case UrielReplyKind.Arm when slot.Step == UrielStep.ArmSent && now - slot.SentAt < ReplyWait:
                slot.Lines.Clear(); slot.Lines.Add(line);
                // The window counts from the send, so a late reply can't extend it.
                slot.Step = now >= slot.ArmUntil ? UrielStep.Expired : UrielStep.Armed;
                return true;
            case UrielReplyKind.Done when slot.Step == UrielStep.ConfirmSent && now - slot.SentAt < ReplyWait:
                slot.Lines.Clear(); slot.Lines.Add(line);
                slot.Step = UrielStep.Done;
                if (act == UrielAction.KillSwitch) slot.GatherUntil = now + GatherWindow;
                return true;
            case UrielReplyKind.NotArmed when slot.Step == UrielStep.ConfirmSent && now - slot.SentAt < ReplyWait:
                slot.Lines.Clear(); slot.Lines.Add(line);
                slot.Step = UrielStep.Expired;
                return true;
            case UrielReplyKind.Failure or UrielReplyKind.Note when slot.GatherUntil > now:
                slot.Lines.Add(line);
                if (reply.Kind == UrielReplyKind.Failure)
                {
                    slot.Step = UrielStep.Partial;
                    if (!slot.Failed.Contains(reply.Domain)) slot.Failed.Add(reply.Domain);
                }
                return true;
        }

        // A control reply nobody here asked for: show it, change no button.
        if (_unsolicited.Count >= MaxUnsolicited) _unsolicited.RemoveAt(0);
        _unsolicited.Add(line);
        return true;
    }

    /// <summary>Advance time. Returns the actions whose slot changed (a deadline, the window, the gather closing).</summary>
    public List<UrielAction> Tick(double now)
    {
        var changed = new List<UrielAction>();
        foreach (var kv in _slots)
        {
            var s = kv.Value;
            var before = s.Step;
            bool gatherClosed = false;
            switch (s.Step)
            {
                case UrielStep.ArmSent when now - s.SentAt >= ReplyWait:
                case UrielStep.ConfirmSent when now - s.SentAt >= ReplyWait:
                    s.Step = UrielStep.NoReply; s.Lines.Clear();
                    break;
                case UrielStep.Armed when now >= s.ArmUntil:
                    s.Step = UrielStep.Expired;
                    break;
                case UrielStep.ConfirmPending when now > s.PressUntil || (HasArm(kv.Key) && now >= s.ArmUntil):
                    if (HasArm(kv.Key)) s.Step = now >= s.ArmUntil ? UrielStep.Expired : UrielStep.Armed;
                    else s.Step = s.BeforePress;
                    break;
            }
            if (s.GatherUntil >= 0 && now >= s.GatherUntil) { s.GatherUntil = -1; gatherClosed = true; }
            if (s.Step != before || gatherClosed) changed.Add(kv.Key);
        }
        return changed;
    }

    /// <summary>Logout, disconnect or admin loss: every slot back to Idle.</summary>
    public void Reset()
    {
        foreach (UrielAction a in Enum.GetValues(typeof(UrielAction))) Fresh(a);
        _unsolicited.Clear();
    }

    private Slot Fresh(UrielAction a)
    {
        var s = _slots[a];
        s.Step = UrielStep.Idle; s.SentAt = 0; s.ArmUntil = s.PressUntil = s.GatherUntil = -1;
        s.Lines.Clear(); s.Failed.Clear();
        return s;
    }

    private static bool Waiting(Slot s, double now)
        => s.Step is UrielStep.ArmSent or UrielStep.ConfirmSent && now - s.SentAt < ReplyWait;

    // ---------------------------------------------------------------- reply texts
    private static readonly Regex KillDoneRx  = new(@"^Kill switch done: \d+ event\(s\) stopped, \d+ unit\(s\) being removed, \d+ object\(s\) removed, \d+ container\(s\) made private\.$", RegexOptions.Compiled);
    private static readonly Regex FailureRx   = new(@"^(Events|Units|Objects|Storage): failed \(.*\)\.$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex UnresolvedRx = new(@"^\d+ spawned object\(s\) couldn't be found right now and are still tracked; run it again once their area is loaded\.$", RegexOptions.Compiled);
    private static readonly Regex DoorsRx     = new(@"^\d+ event door\(s\) weren't found yet; Uriel keeps trying to put them back\.$", RegexOptions.Compiled);
    private static readonly Regex PurgeAllArmRx  = new(@"^This removes every Uriel-spawned object on the server \(\d+ tracked\)\. Run '\.uriel purgeall confirm' within 30 s\.$", RegexOptions.Compiled);
    private static readonly Regex PurgeAllDoneRx = new(@"^Removed \d+ Uriel-spawned object\(s\) server-wide\.( \d+ couldn't be found right now and are still tracked; run it again once their area is loaded\.)?$", RegexOptions.Compiled);
    private static readonly Regex UnitArmRx   = new(@"^This removes every Uriel unit on the server \(\d+ tracked, \d+ pending\)\. Run '\.uriel unit purge confirm' within 30 s\.$", RegexOptions.Compiled);
    private static readonly Regex UnitDoneRx  = new(@"^Purging \d+ unit\(s\); cancelled \d+ pending spawn\(s\)\.$", RegexOptions.Compiled);
    private static readonly Regex StopAllRx   = new(@"^(?:No castle event is running\.|Stopped \d+ castle event\(s\)\.( \d+ door\(s\) weren't found yet; Uriel keeps trying to put them back\.)?)$", RegexOptions.Compiled);

    /// <summary>One of the Server controls replies, or null.</summary>
    public static UrielControlReply? ClassifyReply(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        if (line.StartsWith("This removes everything Uriel put in the world: ", StringComparison.Ordinal)
            && line.EndsWith("To keep it off afterwards, set [General] Enabled = false in the config.", StringComparison.Ordinal))
            return new UrielControlReply(UrielAction.KillSwitch, UrielReplyKind.Arm);
        if (KillDoneRx.IsMatch(line)) return new UrielControlReply(UrielAction.KillSwitch, UrielReplyKind.Done);
        if (line == "Not armed. Run '.uriel killswitch' first.") return new UrielControlReply(UrielAction.KillSwitch, UrielReplyKind.NotArmed);
        var f = FailureRx.Match(line);
        if (f.Success) return new UrielControlReply(UrielAction.KillSwitch, UrielReplyKind.Failure, f.Groups[1].Value);
        if (UnresolvedRx.IsMatch(line) || DoorsRx.IsMatch(line)) return new UrielControlReply(UrielAction.KillSwitch, UrielReplyKind.Note);

        if (PurgeAllArmRx.IsMatch(line)) return new UrielControlReply(UrielAction.PurgeObjects, UrielReplyKind.Arm);
        if (PurgeAllDoneRx.IsMatch(line)) return new UrielControlReply(UrielAction.PurgeObjects, UrielReplyKind.Done);
        if (line == "Not armed. Run '.uriel purgeall' first.") return new UrielControlReply(UrielAction.PurgeObjects, UrielReplyKind.NotArmed);

        if (UnitArmRx.IsMatch(line)) return new UrielControlReply(UrielAction.PurgeUnits, UrielReplyKind.Arm);
        if (UnitDoneRx.IsMatch(line)) return new UrielControlReply(UrielAction.PurgeUnits, UrielReplyKind.Done);
        if (line == "Not armed. Run '.uriel unit purge' first.") return new UrielControlReply(UrielAction.PurgeUnits, UrielReplyKind.NotArmed);

        if (StopAllRx.IsMatch(line)) return new UrielControlReply(UrielAction.StopEvents, UrielReplyKind.Done);
        if (line == "Uriel is not yet initialized.") return new UrielControlReply(null, UrielReplyKind.NotReady);
        return null;
    }
}
