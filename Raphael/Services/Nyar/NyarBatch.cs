using System;
using System.Collections.Generic;

namespace Raphael.Services.Nyar;

// 0.72.0: a run of `event set` twins sent one at a time (a pool larger than one chat line, "Save all changes", a
// wave's start rule). Pure: the time is passed in and the caller does the sending, so the whole sequence is
// unit-tested (tests/Raphael.Logic.Tests). Rules (PLAN.md §B6):
//   • one step outstanding at a time; the next is queued only after the previous one's answer AND ≥ 0.40 s after it
//     went on the wire;
//   • an `ok` counts only if it is `cmd=event verb=set` with this batch's id and the step's field;
//   • an `err` carries no id / field on api 8, so it counts only while the step is outstanding AND NyarClient reports
//     this step's exact command as the one that was in flight;
//   • err → stop (remaining steps dropped); ratelimit → wait `secs` and retry that step once; no answer 6 s after the
//     step went on the wire → stop as "unknown" (the server may have applied it);
//   • cancel drops queued work; a step already sent can't be recalled, so the batch reports "may or may not".
// When a batch ends with a sent-but-unanswered step, AbandonedInFlight is set and the caller starts the client's
// quarantine (NyarQuarantine), so a late reply can never be taken for a later command.

internal enum NyarBatchState { Running, Done, Stopped, Unknown, Cancelled }

internal sealed class NyarBatchStep
{
    public string Field, Value, Command, Label;
    public string FormKey = "";                        // the editor field this step saves (a start rule takes two steps)
    public string[] Covers = Array.Empty<string>();   // pool member names this step adds (pool saves)
}

internal sealed class NyarBatch
{
    public const double Gap = 0.40, ReplyTimeout = 6.0, QueueTimeout = 20.0;

    public readonly string Id, Title;
    public readonly bool IsPool;
    public readonly IReadOnlyList<NyarBatchStep> Steps;
    public NyarBatchState State { get; private set; } = NyarBatchState.Running;
    public int Index { get; private set; }               // the step being worked on
    public bool Outstanding { get; private set; }         // Index's command was handed to the client
    public bool OnWire { get; private set; }              // … and the client sent it
    public bool AbandonedInFlight { get; private set; }   // ended with a sent, unanswered step
    public string RefusedMessage { get; private set; } = "";
    public int Acknowledged => Index;                     // steps before Index were all answered ok

    private double _queuedAt, _onWireAt = double.NegativeInfinity, _retryAt;
    private bool _retried;

    public NyarBatch(string id, string title, bool isPool, IReadOnlyList<NyarBatchStep> steps)
    {
        Id = id ?? ""; Title = title ?? ""; IsPool = isPool; Steps = steps ?? Array.Empty<NyarBatchStep>();
        if (Steps.Count == 0) State = NyarBatchState.Done;
    }

    public bool Finished => State != NyarBatchState.Running;
    public NyarBatchStep Current => Index < Steps.Count ? Steps[Index] : null;

    /// <summary>Called every frame. Returns the command to queue now, or null. Also ends the batch on a timeout.</summary>
    public string Tick(double now)
    {
        if (Finished) return null;
        if (Outstanding)
        {
            if (OnWire && now - _onWireAt >= ReplyTimeout) { State = NyarBatchState.Unknown; AbandonedInFlight = true; }
            else if (!OnWire && now - _queuedAt >= QueueTimeout) { State = NyarBatchState.Stopped; RefusedMessage = "it couldn't be sent (the queue was busy)"; }
            return null;
        }
        if (now < _retryAt || now - _onWireAt < Gap) return null;
        Outstanding = true; OnWire = false; _queuedAt = now;
        return Steps[Index].Command;
    }

    /// <summary>The client reports the current step's command went out.</summary>
    public void MarkOnWire(double now)
    {
        if (!Outstanding || OnWire) return;
        OnWire = true; _onWireAt = now;
    }

    /// <summary>An `[NYAR:ok]` line. True when it was this step's answer (the batch advanced).</summary>
    public bool OnOk(string cmd, string verb, string id, string field)
    {
        if (Finished || !Outstanding || !OnWire) return false;
        if (!Eq(cmd, "event") || !Eq(verb, "set") || !Eq(id, Id) || !string.Equals(field, Steps[Index].Field, StringComparison.Ordinal)) return false;
        Outstanding = OnWire = false; _retried = false;
        Index++;
        if (Index >= Steps.Count) State = NyarBatchState.Done;
        return true;
    }

    /// <summary>An `[NYAR:err]` line; <paramref name="inFlight"/> is the command the client had in flight when it arrived.
    /// True when it was this step's answer.</summary>
    public bool OnErr(string cmd, string verb, string code, int secs, string message, string inFlight, double now)
    {
        if (Finished || !Outstanding || !OnWire) return false;
        if (!Eq(cmd, "event") || !(string.IsNullOrEmpty(verb) || verb == "-" || Eq(verb, "set"))) return false;
        if (!string.Equals(inFlight, Steps[Index].Command, StringComparison.Ordinal)) return false;
        Outstanding = OnWire = false;
        if (Eq(code, "ratelimit") && !_retried)
        {
            _retried = true;
            _retryAt = now + Math.Max(1, secs);
            return true;
        }
        State = NyarBatchState.Stopped;
        RefusedMessage = message ?? "";
        return true;
    }

    /// <summary>Editor closed / disconnect. Returns true when a sent step was left unanswered (start the quarantine).</summary>
    public bool Cancel()
    {
        if (Finished) return false;
        State = NyarBatchState.Cancelled;
        AbandonedInFlight = Outstanding && OnWire;
        return AbandonedInFlight;
    }

    /// <summary>Member names confirmed saved (pool saves): those of every acknowledged step.</summary>
    public int ConfirmedNames
    {
        get { int n = 0; for (int i = 0; i < Index && i < Steps.Count; i++) n += Steps[i].Covers.Length; return n; }
    }

    public int TotalNames
    {
        get { int n = 0; foreach (var s in Steps) n += s.Covers.Length; return n; }
    }

    /// <summary>"Saving pool troops… 4 of 13" while running.</summary>
    /// <summary>A pool batch that is one "none" step: the pool's deletion.</summary>
    public bool IsPoolDelete => IsPool && Steps.Count == 1 && Steps[0].Value == "none";

    public string Progress => IsPool
        ? $"Saving pool {Title}… {ConfirmedNames} of {TotalNames} units"
        : $"Saving {Title}… {Index} of {Steps.Count}";

    /// <summary>The one result line for the whole batch.</summary>
    public string Summary()
    {
        var cur = Current;
        string label = cur?.Label ?? "";
        switch (State)
        {
            case NyarBatchState.Done:
                if (IsPoolDelete) return $"Pool {Title} deleted.";
                if (IsPool) return $"Pool {Title} saved ({TotalNames} unit{(TotalNames == 1 ? "" : "s")}).";
                return Steps.Count == 1 && !string.IsNullOrEmpty(Steps[0].Label) ? $"Saved {Steps[0].Label}." : $"Saved {Steps.Count} changes.";
            case NyarBatchState.Stopped:
                if (IsPoolDelete) return $"Pool {Title} was not deleted: {RefusedMessage}";
                if (IsPool)
                    return Index == 0
                        ? $"Pool {Title} was not saved: {RefusedMessage} Fix it and Save again."
                        : $"Pool {Title} stopped after {ConfirmedNames} of {TotalNames} units: {label} was refused ({Trim(RefusedMessage)}). Fix it and Save again (Save replaces the whole pool).";
                return Index == 0
                    ? $"{label} was refused: {RefusedMessage}"
                    : $"Saved {Index} of {Steps.Count} changes; {label} was refused: {RefusedMessage} Fix it and save again.";
            case NyarBatchState.Unknown:
                return IsPool
                    ? $"Pool {Title}: no answer for {label}. Press Reload values to see what was saved."
                    : $"No answer for {label}. Press Reload values to see what was saved.";
            case NyarBatchState.Cancelled:
                return AbandonedInFlight
                    ? $"{(IsPool ? "Pool " + Title : "Saving")} stopped: {label} may or may not have been saved. Press Reload values."
                    : $"{(IsPool ? "Pool " + Title : "Saving")} stopped before {label}.";
            default:
                return Progress;
        }
    }

    private static bool Eq(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    private static string Trim(string s) => (s ?? "").TrimEnd('.', ' ');
}

/// <summary>After a twin was sent and never answered, no admin twin goes out for 10 s from that moment, and every twin
/// ok / err arriving meanwhile is stale by construction (logged and discarded). Reads keep flowing.</summary>
internal sealed class NyarQuarantine
{
    public const double Seconds = 10.0;
    private double _until = double.NegativeInfinity;

    public void Start(double now) { if (now + Seconds > _until) _until = now + Seconds; }
    public bool On(double now) => now < _until;
    public int SecondsLeft(double now) => On(now) ? (int)Math.Ceiling(_until - now) : 0;
    public void Clear() => _until = double.NegativeInfinity;
}
