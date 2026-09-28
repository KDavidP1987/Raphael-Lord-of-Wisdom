using System;
using System.Collections.Generic;
using Raphael.Utils;
using UnityEngine;

namespace Raphael.Services.Nyar;

// Entry point + router for the Nyarlathotep integration (server-side chaos/event mod). Modeled on
// UrielProtocolService / FaustProtocolService: plaintext [NYAR:*] System-chat lines routed here from
// ClientChatPatch's [NYAR: branch.
//
// Responsibilities:
//   • Detect (handshake `.nyar api version`, retry/back-off, gate on ready=1)
//   • On detection: `api sub on` (subscriptions are memory-only on the server, so every handshake re-subs)
//     and hydrate status / me / (admin) events + zones
//   • Parse rows and commit on [NYAR:end]; auto-page `events` / `zones` (cur+1 until cur == total)
//   • Apply the [NYAR:ev] push stream (debounced re-reads)
//   • Fire a once-a-second CountdownTick while there's something to count down (events / kill switch)
//
// Built against the contract's PLANNED api 1/2 shapes; the mod was v0.1.0 (no api) when this was written.
internal static class NyarProtocolService
{
    public static bool IsPresent => NyarState.Present;
    public static bool DetectionGaveUp { get; private set; }

    /// <summary>Fired when presence is resolved (handshake ACK or give-up).</summary>
    public static event Action AvailabilityChanged;
    /// <summary>Fired once a second while an event or the kill switch has a live countdown.</summary>
    public static event Action CountdownTick;

    // ---- handshake (mirrors Uriel/Faust) ----
    private const float DETECTION_START_DELAY_SECONDS = 4f;
    private const float HANDSHAKE_RETRY_AFTER_SECONDS = 4f;
    private const int   HANDSHAKE_MAX_ATTEMPTS = 12;
    private static float _detectStartAt, _handshakeSentAt;
    private static int   _handshakeAttempts;

    // ---- event-driven re-read debounce ----
    private const float REFETCH_DEBOUNCE_SECONDS = 0.75f;
    private static float _lastRefetchAt;
    private static bool _needStatus, _needVersion, _needDefs, _needMe;

    private static float _lastCountdownAt;

    // ---- accumulators (committed on the matching [NYAR:end]) ----
    private static readonly List<NyarEvent>  _accStatus = new();
    private static readonly List<NyarMeRow>  _accMe = new();
    private static readonly List<NyarTopRow> _accTop = new();
    private static readonly List<NyarDef>    _accDefs = new();
    private static readonly List<NyarZone>   _accZones = new();

    public static void Tick()
    {
        if (!MessageService.IsInitialized) return;

        if (!NyarState.Present)
        {
            if (DetectionGaveUp) return;
            float now = Time.realtimeSinceStartup;
            if (_detectStartAt <= 0f) { _detectStartAt = now; NyarDiag.Log($"detection started (settle {DETECTION_START_DELAY_SECONDS}s)"); }
            if (now - _detectStartAt < DETECTION_START_DELAY_SECONDS) return;
            bool first = _handshakeAttempts == 0;
            if (!first && now - _handshakeSentAt < HANDSHAKE_RETRY_AFTER_SECONDS) return;
            if (_handshakeAttempts >= HANDSHAKE_MAX_ATTEMPTS)
            {
                DetectionGaveUp = true;
                LogUtils.LogInfo($"[Nyar] No response to `.nyar api version` after {_handshakeAttempts} probes — Nyarlathotep not present on this server.");
                FireAvailability();
                return;
            }
            _handshakeAttempts++;
            _handshakeSentAt = now;
            NyarClient.RequestVersion();
            NyarDiag.Log($"`.nyar api version` probe {_handshakeAttempts}/{HANDSHAKE_MAX_ATTEMPTS} sent");
            return;
        }

        float t = Time.realtimeSinceStartup;
        if ((_needStatus || _needVersion || _needDefs || _needMe) && t - _lastRefetchAt >= REFETCH_DEBOUNCE_SECONDS)
            FlushRefetch();

        if (t - _lastCountdownAt >= 1f && (NyarState.ActiveEvents.Count > 0 || NyarState.KillswitchSecondsLeft > 0))
        {
            _lastCountdownAt = t;
            try { CountdownTick?.Invoke(); } catch (Exception ex) { LogUtils.LogError($"[Nyar] CountdownTick handler threw: {ex}"); }
        }
    }

    /// <summary>Reset detection + all streaming state on logout / server switch (pure field resets).</summary>
    public static void Reset()
    {
        DetectionGaveUp = false;
        _detectStartAt = _handshakeSentAt = 0f;
        _handshakeAttempts = 0;
        _lastRefetchAt = _lastCountdownAt = 0f;
        _needStatus = _needVersion = _needDefs = _needMe = false;
        _accStatus.Clear(); _accMe.Clear(); _accTop.Clear(); _accDefs.Clear(); _accZones.Clear();
        NyarState.Reset();
    }

    // ---- UI-facing read helpers (start a fresh accumulation) ----
    public static void RefreshStatus() { if (!NyarState.SupportsReads) return; _accStatus.Clear(); NyarClient.RequestStatus(); }
    public static void RefreshMe()     { if (!NyarState.SupportsReads || !NyarState.StatsOn) return; _accMe.Clear(); NyarClient.RequestMe(); }
    public static void RefreshTop(string stat, string window, int page)
    {
        if (!NyarState.SupportsReads || !NyarState.StatsOn) return;
        _accTop.Clear(); NyarClient.RequestTop(stat, window, Math.Max(1, page));
    }
    public static void RefreshDefs()  { if (!NyarState.SupportsReads) return; _accDefs.Clear(); NyarClient.RequestEvents(1); }
    public static void RefreshZones() { if (!NyarState.SupportsReads) return; _accZones.Clear(); NyarClient.RequestZones(1); }

    // ---- inbound router (called from ClientChatPatch for every [NYAR:*] line) ----
    public static void HandleLine(string raw)
    {
        NyarDiag.LogIn(raw);
        var line = NyarWireParser.Parse(raw);
        if (line == null) return;

        switch (line.Tag.ToLowerInvariant())
        {
            case "version": OnVersion(line); break;
            case "event":   _accStatus.Add(ReadEvent(line)); break;
            case "me":      _accMe.Add(ReadMe(line)); break;
            case "top":     _accTop.Add(new NyarTopRow(line.GetInt("rank"), line.GetText("name"), line.GetLong("value"), line.GetBool("self"))); break;
            case "def":     _accDefs.Add(ReadDef(line)); break;
            case "zone":    _accZones.Add(ReadZone(line)); break;
            case "end":     OnEnd(line); break;
            case "ok":      OnOk(line); break;
            case "err":     OnErr(line); break;
            case "ev":      OnEv(line); break;
            default:        LogUtils.LogDiagnostic($"[Nyar] unhandled tag '{line.Tag}': {line.Raw}"); break;
        }
    }

    private static void OnVersion(NyarLine line)
    {
        bool wasPresent = NyarState.Present;
        NyarState.SetVersion(line);
        if (!NyarState.Ready) { LogUtils.LogDiagnostic("[Nyar] api version ready=0 — will retry."); return; }
        if (wasPresent) return;

        LogUtils.LogInfo($"[Nyar] Nyarlathotep detected (api={NyarState.ApiVersion}, plugin={NyarState.PluginVersion}, admin={(NyarState.ServerAdmin ? 1 : 0)}).");
        FireAvailability();
        if (!NyarState.SupportsReads) return;   // api 1 = handshake only
        NyarClient.Subscribe(true);
        RefreshStatus();
        RefreshMe();
        if (NyarState.ServerAdmin)
        {
            RefreshDefs();
            if (NyarState.Zones) RefreshZones();
        }
    }

    private static void OnEnd(NyarLine line)
    {
        string cmd = line.Get("cmd").ToLowerInvariant();
        ParsePage(line.Get("page"), out int cur, out int total);
        switch (cmd)
        {
            case "status": NyarState.SetStatus(_accStatus.ToArray()); _accStatus.Clear(); NyarState.ClearError(); break;
            case "me":     NyarState.SetMe(_accMe.ToArray()); _accMe.Clear(); break;
            case "top":
                NyarState.SetTop(line.Get("stat"), line.Get("window"), cur, total, line.GetInt("count"), _accTop.ToArray());
                _accTop.Clear(); break;
            case "events":
                if (cur < total) NyarClient.RequestEvents(cur + 1);
                else { NyarState.SetDefs(_accDefs.ToArray()); _accDefs.Clear(); }
                break;
            case "zones":
                if (cur < total) NyarClient.RequestZones(cur + 1);
                else { NyarState.SetZones(_accZones.ToArray()); _accZones.Clear(); }
                break;
            default: LogUtils.LogDiagnostic($"[Nyar] end for unhandled cmd '{cmd}'."); break;
        }
    }

    private static void OnOk(NyarLine line)
    {
        if (line.Get("cmd").Equals("sub", StringComparison.OrdinalIgnoreCase))
            NyarState.SetSubscribed(line.GetBool("on"));
    }

    private static void OnErr(NyarLine line)
    {
        string cmd = line.Get("cmd"), code = line.Get("code").ToLowerInvariant();
        int secs = line.GetInt("secs");
        if (code == "notready") { LogUtils.LogDiagnostic($"[Nyar] err notready for cmd={cmd}."); return; }
        if (code == "cooldown") NyarState.SetKillswitchSeconds(secs);
        // A failed paged read must not leave a half-filled accumulator behind.
        switch (cmd.ToLowerInvariant())
        {
            case "status": _accStatus.Clear(); break;
            case "me":     _accMe.Clear(); break;
            case "top":    _accTop.Clear(); break;
            case "events": _accDefs.Clear(); break;
            case "zones":  _accZones.Clear(); break;
        }
        NyarState.SetError(cmd, code switch
        {
            "disabled"  => "That feature is switched off on this server.",
            "notfound"  => "Not found (unknown event, zone or stat).",
            "badarg"    => $"Bad argument{(line.Has("arg") ? $": {line.GetText("arg")}" : "")}.",
            "ratelimit" => $"Share limit reached — try again in {FormatSecs(secs)}.",
            "cooldown"  => $"Kill-switch cooldown active — {FormatSecs(secs)} left.",
            "noaccess"  => "Admin only.",
            _           => $"Error '{code}'.",
        });
    }

    // Push stream (after `api sub on`). Map each type to the read(s) it invalidates.
    private static void OnEv(NyarLine line)
    {
        string id = line.Get("id");
        switch (line.Get("type").ToLowerInvariant())
        {
            case "wave-warn":
                NyarState.SetWaveWarn(id, line.Get("wave"), line.GetInt("secs"));
                break;
            case "wave":
                NyarState.ClearWaveWarn(id); _needStatus = true; break;
            case "event-start":
                _needStatus = true; break;
            case "event-end":
                NyarState.ClearWaveWarn(id); _needStatus = _needMe = true; break;
            case "killswitch":
                NyarState.SetKillswitchSeconds(line.GetInt("secs")); _needStatus = _needVersion = true; break;
            case "config-changed":
                _needVersion = true; _needDefs = NyarState.ServerAdmin; break;
            default:
                LogUtils.LogDiagnostic($"[Nyar] unhandled ev type '{line.Get("type")}'."); break;
        }
    }

    private static void FlushRefetch()
    {
        _lastRefetchAt = Time.realtimeSinceStartup;
        if (_needVersion) { NyarClient.RequestVersion(); _needVersion = false; }
        if (_needStatus)  { RefreshStatus(); _needStatus = false; }
        if (_needMe)      { RefreshMe(); _needMe = false; }
        if (_needDefs)    { RefreshDefs(); _needDefs = false; }
    }

    // ---- record readers ----
    private static NyarEvent ReadEvent(NyarLine l) => new(
        l.Get("id"), l.Get("kind"), l.GetText("name"), l.Get("state"), l.GetClean("faction").Replace('_', ' '),
        l.Get("left") == "-" ? -1 : l.GetInt("left", -1), l.GetClean("wave"), l.Get("units") == "-" ? -1 : l.GetInt("units", -1),
        Time.realtimeSinceStartup);

    private static NyarMeRow ReadMe(NyarLine l) => new(
        l.Get("window"), l.GetText("name"), l.GetInt("kills"), l.GetInt("events"), l.GetInt("waves"),
        l.GetInt("defences"), l.GetInt("bossadds"), l.GetInt("deaths"), l.GetBool("hidden"), l.GetBool("none"));

    private static NyarDef ReadDef(NyarLine l) => new(
        l.Get("id"), l.GetText("name"), l.GetBool("enabled"), l.Get("trigger"), l.Get("action"),
        l.GetInt("duration"), l.Get("state"), l.GetClean("reason").Replace('_', ' '));

    private static NyarZone ReadZone(NyarLine l) => new(
        l.GetText("name"), l.GetClean("x"), l.GetClean("z"), l.GetClean("r"), l.GetInt("threshold"), l.GetInt("cooldown"), l.Get("state"));

    // "cur/total" → ints; absent (unpaged) → 1/1.
    private static void ParsePage(string v, out int cur, out int total)
    {
        cur = 1; total = 1;
        if (string.IsNullOrEmpty(v)) return;
        int slash = v.IndexOf('/');
        if (slash <= 0) { int.TryParse(v, out cur); return; }
        int.TryParse(v.Substring(0, slash), out cur);
        int.TryParse(v.Substring(slash + 1), out total);
        if (cur < 1) cur = 1; if (total < 1) total = 1;
    }

    internal static string FormatSecs(int secs)
    {
        if (secs <= 0) return "0s";
        if (secs < 60) return $"{secs}s";
        if (secs < 3600) return $"{secs / 60}m {secs % 60:00}s";
        return $"{secs / 3600}h {(secs % 3600) / 60:00}m";
    }

    private static void FireAvailability()
    {
        try { AvailabilityChanged?.Invoke(); }
        catch (Exception ex) { LogUtils.LogError($"[Nyar] AvailabilityChanged handler threw: {ex}"); }
    }
}
