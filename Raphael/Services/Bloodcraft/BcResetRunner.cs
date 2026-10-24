using System;
using System.Collections.Generic;
using Raphael.Behaviors;
using Raphael.Utils;

namespace Raphael.Services.Bloodcraft;

// 0.73.0: drives a BcResetRun in the game (PLAN.md §2 "Runner"). Service-level, so closing or rebuilding the panel
// doesn't stop a run; the card re-reads Current. Everything here runs on the Unity main thread (chat patch and
// CoreUpdateBehavior both do), so there is no locking.
internal static class BcResetRunner
{
    /// <summary>The latest run, kept after it ends so the card can show the summary. Null before the first run.</summary>
    public static BcResetRun Current { get; private set; }

    /// <summary>Bumped whenever the run's visible state may have changed; the card repaints when it differs.</summary>
    public static int Revision { get; private set; }

    /// <summary>True while a run is sending or waiting (including paused). The other Bloodcraft admin forms are
    /// disabled while this holds, so the admin can't interleave the same commands.</summary>
    public static bool IsRunning => Current != null && Current.Active;

    private static Action _ticker;

    private static double Now => UnityEngine.Time.realtimeSinceStartup;

    /// <summary>Null when the run started; otherwise why it didn't.</summary>
    public static string Start(string player, List<BcResetStep> steps)
    {
        if (IsRunning) return "A reset is already running.";
        if (steps == null || steps.Count == 0) return "Pick at least one thing to reset.";
        if (!MessageService.IsInitialized) return "Not connected to a server.";
        if (!MessageService.IsLocalAdmin()) return "You need admin. Run adminauth in the F1 console, then press Re-check admin.";
        Current = new BcResetRun(player, steps);
        Revision++;
        LogUtils.LogInfo($"[Raphael] Bloodcraft reset started for {player}: {steps.Count} commands.");
        EnsureTicker();
        return null;
    }

    /// <summary>Starts a new run over the last run's failed and unknown steps, recomputed now.</summary>
    public static string StartRetry(bool includeReroll)
    {
        if (Current == null) return "Nothing to retry.";
        if (IsRunning) return "A reset is already running.";
        var list = Current.RetryList(includeReroll);
        if (list.Count == 0) return "Nothing to retry.";
        return Start(Current.Player, list);
    }

    public static void Stop(string reason)
    {
        if (Current == null || !Current.Active) return;
        Current.Stop(Now, reason);
        Revision++;
        LogUtils.LogInfo($"[Raphael] Bloodcraft reset stopped: {Current.Summary()}");
    }

    public static bool RetryStep(bool confirmReroll) => Bump(Current != null && Current.RetryStep(Now, confirmReroll));
    public static bool Skip() => Bump(Current != null && Current.Skip(Now));
    public static bool Continue() => Bump(Current != null && Current.Continue(Now));

    private static bool Bump(bool changed) { if (changed) Revision++; return changed; }

    /// <summary>The reply tap: called first thing in MessageService.HandleInboundChat. True only for a line this run
    /// correlated, which the caller then hides from chat. Everything else passes through untouched.</summary>
    public static bool TryHandleInbound(string text)
    {
        var run = Current;
        if (run == null || run.Sealed) return false;
        try
        {
            var before = run.Pause;
            bool consumed = run.HandleLine(text, Now);
            if (consumed || run.Pause != before) Revision++;
            return consumed;
        }
        catch (Exception ex)
        {
            LogUtils.LogWarning($"[Raphael] Bloodcraft reset reply handling failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Disconnect / server switch (InitializationPatch teardown). A pure field update. The report is kept,
    /// frozen, so the card shows why the run stopped and how far it got after the world rebuilds.</summary>
    public static void OnWorldTeardown()
    {
        if (Current == null) return;
        Current.Detach(Now, "Stopped: you left the server.");
        Revision++;
    }

    private static void EnsureTicker()
    {
        if (_ticker != null) return;
        _ticker = Tick;
        CoreUpdateBehavior.Actions.Add(_ticker);
    }

    private static void Tick()
    {
        var run = Current;
        if (run == null || run.Sealed)
        {
            CoreUpdateBehavior.Actions.Remove(_ticker);
            _ticker = null;
            return;
        }
        try
        {
            if (run.Active && run.State != BcRunState.Paused)
            {
                // Rechecked every frame a send could happen: losing the connection or admin stops the run before
                // anything else goes out.
                if (!MessageService.IsInitialized) { Stop("Stopped: not connected."); return; }
                if (!MessageService.IsLocalAdmin()) { Stop("Stopped: you are no longer admin."); return; }
            }
            var state = run.State;
            var done = run.Done;
            bool wasSealed = run.Sealed;
            var command = run.Tick(Now);
            if (command != null && !MessageService.EnqueueMessageSilent(command))
            {
                run.SendFailed(Now);
                LogUtils.LogWarning($"[Raphael] Bloodcraft reset: couldn't send '{command}'. {run.Summary()}");
            }
            if (command != null || state != run.State || done != run.Done || wasSealed != run.Sealed) Revision++;
            if (state != run.State && run.Ended) LogUtils.LogInfo($"[Raphael] Bloodcraft reset: {run.Summary()}");
        }
        catch (Exception ex)
        {
            LogUtils.LogWarning($"[Raphael] Bloodcraft reset tick failed: {ex.Message}");
            Stop("Stopped: internal error (see the BepInEx log).");
        }
    }
}
