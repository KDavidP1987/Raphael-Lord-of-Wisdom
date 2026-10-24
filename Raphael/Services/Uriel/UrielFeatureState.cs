using System;
using System.Text.RegularExpressions;
using Raphael.Utils;
using UnityEngine;

namespace Raphael.Services.Uriel;

// 0.73.0: Uriel 0.22.0 feature switches + the Server controls state machine (PLAN-URIEL022.md §2). Thin wrapper over
// the pure UrielFeatureModel / UrielControlFlow (UrielFeatures.cs).
//
// OBSERVE-ONLY: ClientChatPatch hands it every non-protocol System line (after the MessageType == System filter, so a
// player can't imitate one). Nothing here destroys an entity: every Uriel line still reaches chat.
// RAIL-SAFE: it has its own Changed event and never raises UrielProtocolService.AvailabilityChanged or
// UrielState.PresenceChanged, so a feature change can't move the left rail.
internal static class UrielFeatureState
{
    public static readonly UrielFeatureModel Model = new();
    public static readonly UrielControlFlow Flow = new();

    /// <summary>The version probe got `[URIEL:err] code=disabled` (object spawning or the whole mod is off).</summary>
    public static bool ReportedDisabled { get; private set; }
    /// <summary>When Raphael last sent `.uriel features` (realtime), for the panel's "asked" note.</summary>
    public static float FeaturesAskedAt { get; private set; } = -1f;

    /// <summary>Fires on any switch, Server controls slot or reply change. UI handlers only.</summary>
    public static event Action Changed;

    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled);

    public static double Now => Time.realtimeSinceStartup;

    public static void Observe(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return;
        string text = (raw.IndexOf('<') >= 0 ? Tags.Replace(raw, "") : raw).Trim();
        if (text.Length == 0) return;

        bool changed = false;
        var snap = UrielFeatures.Parse(text);
        if (snap != null)
        {
            changed |= Model.Apply(snap);
            // Keep the Castle Events availability line in step (the features line only reaches admins).
            UrielEvents.SetFromVersion(Model.Effective(UrielFeature.Events) == UrielSwitch.On, !snap.EventsAdminOnly, true);
        }
        else
        {
            var refusal = UrielFeatures.ClassifyRefusal(text);
            if (refusal.HasValue) changed |= Model.Apply(refusal.Value);
            changed |= Flow.OnLine(text, Now);
        }
        if (changed) Fire();
    }

    /// <summary>Ask the server which features are on (admins only; the reply shows in chat).</summary>
    public static void RequestFeatures()
    {
        FeaturesAskedAt = Time.realtimeSinceStartup;
        UrielClient.Send(".uriel features");
        Fire();
    }

    /// <summary>The handshake said object spawning is off (`objectspawn=0`, or the `code=disabled` reply).</summary>
    internal static void NoteObjectsOff()
    {
        if (Model.SetOff(UrielFeature.Objects)) Fire();
    }

    internal static void NoteReportedDisabled()
    {
        ReportedDisabled = true;
        Model.SetOff(UrielFeature.Objects);
        Fire();
    }

    /// <summary>Tick from the panel's ticker. Fires Changed when a slot moved.</summary>
    public static void Tick()
    {
        if (Flow.Tick(Now).Count > 0) Fire();
    }

    /// <summary>Logout / server switch (UrielProtocolService.Reset): pure field resets, no event.</summary>
    internal static void Reset()
    {
        Model.Clear();
        Flow.Reset();
        ReportedDisabled = false;
        FeaturesAskedAt = -1f;
    }

    /// <summary>Admin rights went away: the kill-switch slots go back to Idle.</summary>
    internal static void ResetFlow()
    {
        Flow.Reset();
        Fire();
    }

    private static void Fire()
    {
        if (Changed == null) return;
        try { Changed.Invoke(); }
        catch (Exception ex) { LogUtils.LogError($"[Uriel] UrielFeatureState handler threw: {ex}"); }
    }
}
