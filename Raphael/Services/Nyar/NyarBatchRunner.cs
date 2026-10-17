using System;
using Raphael.Utils;
using UnityEngine;

namespace Raphael.Services.Nyar;

// 0.72.0: runs one NyarBatch (pool save, "Save all changes", a wave's start rule) against NyarClient: queues each step
// when the batch says so, notices when the client puts it on the wire, feeds it the matching ok / err lines, and
// writes one result line for the whole run. The rules themselves are NyarBatch's (pure, unit-tested).
internal static class NyarBatchRunner
{
    private static NyarBatch _batch;

    /// <summary>Fired when a batch starts, advances or ends (the editor re-renders its buttons).</summary>
    public static event Action Changed;

    public static NyarBatch Current => _batch;
    public static bool Busy => _batch != null && !_batch.Finished;

    /// <summary>Start <paramref name="batch"/>; false (with the reason in the error line) while another runs or the
    /// client is in quarantine.</summary>
    public static bool Start(NyarBatch batch)
    {
        if (batch == null) return false;
        if (Busy) { NyarState.SetError("event set", "Still saving — wait for it to finish."); return false; }
        if (NyarClient.Quarantined) { NyarState.SetError("event set", WaitText); return false; }
        if (!NyarState.SupportsTwins) { NyarState.SetError("event set", "This needs Nyarlathotep api 4 or later."); return false; }
        _batch = batch;
        NyarState.ClearError();
        NyarState.SetResult(true, batch.Progress);
        Fire();
        Tick();
        return true;
    }

    public static string WaitText => $"Waiting for the server's last answer — {Math.Max(1, NyarClient.QuarantineSecondsLeft)} s.";

    /// <summary>Per frame, right after NyarClient.Pump (so a step that just went out is marked on the wire at once).</summary>
    public static void Tick()
    {
        if (!Busy) return;
        double now = Time.realtimeSinceStartup;
        var b = _batch;
        if (b.Outstanding && !b.OnWire && NyarClient.InFlight == b.Current?.Command) b.MarkOnWire(now);
        var cmd = b.Tick(now);
        if (cmd != null) NyarClient.SendTwin(cmd);
        if (b.Finished) Finish();
    }

    /// <summary>An `[NYAR:ok]` line; true when it was the batch's (the caller then skips its own result line).</summary>
    public static bool OnOk(NyarLine line)
    {
        if (!Busy) return false;
        if (!_batch.OnOk(line.Get("cmd"), line.Get("verb"), line.Get("id"), line.Get("field"))) return false;
        if (_batch.Finished) Finish();
        else { NyarState.SetResult(true, _batch.Progress); Fire(); }
        return true;
    }

    /// <summary>An `[NYAR:err]` line with the command that was in flight when it arrived; true when it was the batch's.</summary>
    public static bool OnErr(NyarLine line, string inFlight, string message)
    {
        if (!Busy) return false;
        if (!_batch.OnErr(line.Get("cmd"), line.Get("verb"), line.Get("code"), line.GetInt("secs"), message, inFlight, Time.realtimeSinceStartup))
            return false;
        if (_batch.Finished) Finish();
        else { NyarState.SetResult(true, $"{_batch.Progress} (server busy, retrying)"); Fire(); }
        return true;
    }

    /// <summary>Editor closed, or the connection reset. A queued step is dropped; a sent one can't be recalled, so the
    /// client goes into quarantine and the summary says "may or may not".</summary>
    public static void Cancel()
    {
        if (!Busy) return;
        var cur = _batch.Current;
        if (!_batch.Cancel() && cur != null) NyarClient.DropExact(cur.Command);
        Finish();
    }

    /// <summary>Forget the batch without touching the client (the queue was reset: disconnect / server switch).</summary>
    public static void Reset() { _batch = null; }

    private static void Finish()
    {
        var b = _batch;
        if (b.AbandonedInFlight)
        {
            // Release the client's wait on it too, so the 10 s quarantine runs from now rather than after its reply
            // timeout (a late answer inside the quarantine is discarded as stale, as on a timeout).
            if (b.Current != null && NyarClient.InFlight == b.Current.Command) NyarClient.Complete();
            NyarClient.StartQuarantine();
        }
        if (b.State == NyarBatchState.Stopped && b.Outstanding && !b.OnWire && b.Current != null) NyarClient.DropExact(b.Current.Command);
        bool ok = b.State == NyarBatchState.Done;
        NyarState.SetResult(ok, b.Summary());
        if (!ok) NyarState.SetError("event set", b.Summary());
        NyarDiag.Log($"batch '{b.Title}' ended {b.State} after {b.Acknowledged}/{b.Steps.Count} steps");
        Fire();
    }

    private static void Fire()
    {
        try { Changed?.Invoke(); } catch (Exception ex) { LogUtils.LogError($"[Nyar] batch Changed handler threw: {ex}"); }
    }
}
