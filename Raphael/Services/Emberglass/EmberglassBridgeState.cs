using System;

namespace Raphael.Services.Emberglass;

// 0.73.0: what the Emberglass bridge is doing, for the Connection tab and the log (PLAN.md §1 "Honest diagnostics").
// Pure: no Unity, no reflection, time passed in, so the logic tests drive it. EmberglassBloodcraftBridge owns the
// one live instance.

internal enum EmberglassBridgeStatus
{
    /// <summary>The Raphael setting is off (the default): the bridge never registers.</summary>
    Off,
    /// <summary>On, but the Emberglass client mod isn't loaded.</summary>
    NotInstalled,
    /// <summary>On and Emberglass is loaded, but the Eclipse client mod is too; Raphael stands down for it.</summary>
    EclipseHandles,
    /// <summary>Registration with Emberglass failed; disabled for this session.</summary>
    Failed,
    /// <summary>Registered with Emberglass; no valid packet yet.</summary>
    RegisteredNoData,
    /// <summary>At least one MAC-valid packet arrived.</summary>
    Receiving,
}

internal sealed class EmberglassBridgeState
{
    public EmberglassBridgeStatus Status { get; private set; } = EmberglassBridgeStatus.Off;
    public int Packets { get; private set; }
    public int MacFailures { get; private set; }
    public int Empty { get; private set; }
    public double LastPacketAt { get; private set; } = double.NaN;
    public string FailReason { get; private set; }

    /// <summary>Whether to register at all: only with the setting on, Emberglass loaded and Eclipse not loaded.</summary>
    public static EmberglassBridgeStatus Decide(bool settingOn, bool emberglassLoaded, bool eclipseLoaded)
    {
        if (!settingOn) return EmberglassBridgeStatus.Off;
        if (!emberglassLoaded) return EmberglassBridgeStatus.NotInstalled;
        if (eclipseLoaded) return EmberglassBridgeStatus.EclipseHandles;
        return EmberglassBridgeStatus.RegisteredNoData; // the caller registers, then calls Registered() or Fail()
    }

    public void SetDecided(EmberglassBridgeStatus s) => Status = s;
    public void Registered() => Status = EmberglassBridgeStatus.RegisteredNoData;
    public void Fail(string reason) { Status = EmberglassBridgeStatus.Failed; FailReason = reason; }

    /// <summary>One packet's Message: passed to the same handler the chat path uses (MAC check and routing).
    /// Registration alone never counts as receiving; only a packet the handler accepted does.</summary>
    public bool Deliver(string message, double now, Func<string, bool> handle)
    {
        if (string.IsNullOrWhiteSpace(message)) { Empty++; return false; }
        bool ok;
        try { ok = handle(message); }
        catch { ok = false; }
        if (!ok) { MacFailures++; return false; }
        Packets++;
        LastPacketAt = now;
        if (Status == EmberglassBridgeStatus.RegisteredNoData) Status = EmberglassBridgeStatus.Receiving;
        return true;
    }

    /// <summary>The Connection-tab line. Never names a cause it can't know.</summary>
    public string Describe(double now)
    {
        switch (Status)
        {
            case EmberglassBridgeStatus.Off: return "Emberglass bridge: off (Bloodcraft data comes over chat).";
            case EmberglassBridgeStatus.NotInstalled: return "Emberglass bridge: on, but the Emberglass client mod isn't installed. Data comes over chat.";
            case EmberglassBridgeStatus.EclipseHandles: return "Emberglass bridge: standing down; the Eclipse client mod handles Bloodcraft's Emberglass data.";
            case EmberglassBridgeStatus.Failed: return $"Emberglass bridge: couldn't register ({FailReason}). Data comes over chat.";
            case EmberglassBridgeStatus.RegisteredNoData:
                return "Emberglass bridge: registered, no data yet. Possible reasons: the server's bridge is off, data is still coming over chat, or another mod owns the packet." +
                       (MacFailures > 0 ? $" {MacFailures} packet(s) failed the signature check." : "");
            default:
                double ago = double.IsNaN(LastPacketAt) ? 0 : Math.Max(0, now - LastPacketAt);
                return $"Emberglass bridge: receiving ({Packets} packet(s), last {Ago(ago)} ago" +
                       (MacFailures > 0 ? $", {MacFailures} failed the signature check" : "") + ").";
        }
    }

    private static string Ago(double s) => s < 90 ? $"{(int)s} s" : s < 5400 ? $"{(int)(s / 60)} min" : $"{(int)(s / 3600)} h";
}
