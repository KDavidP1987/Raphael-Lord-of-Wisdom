using System;
using System.Collections.Generic;
using System.Linq;
using Raphael.UI.ModContent;
using Raphael.Utils;
using UnityEngine;

namespace Raphael.Services.Nyar;

// Entry point + router for the Nyarlathotep integration (server-side chaos/event mod). Modeled on
// UrielProtocolService / FaustProtocolService: plaintext [NYAR:*] System-chat lines routed here from
// ClientChatPatch's [NYAR: branch.
//
// Responsibilities (Nyarlathotep repo docs/RAPHAEL_INTEGRATION_CONTRACT.md + RAPHAEL_HANDOFF_API8.md):
//   • Detect (handshake `.nyar api version`, retry/back-off, gate on ready=1)
//   • On detection, one read at a time through NyarClient's queue (handoff §2): sub on → status → regions,
//     then for admins events (all pages) → pillar list → killswitch → templates (all pages)
//   • Parse rows and commit on [NYAR:end]; auto-page (cur+1 until cur == total)
//   • Apply the [NYAR:ev] push stream (debounced re-reads) and raise toasts (NyarToastHost)
//   • Read the admin twins' one ok/err line into NyarState.LastResult / LastError
//   • Correct drift: status every 60 s while a Nyar tab is open (5 min otherwise), killswitch every 5 s while on
//   • Fire a once-a-second CountdownTick while there's something to count down (events / kill switch)
//
// Gates on `api` only, never on `plugin` (which may carry a pre-release suffix like 0.13.0-rc.1).
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
    private static bool _needStatus, _needVersion, _needDefs, _needMe, _needPillars, _needKillswitch, _needRegions;

    // ---- periodic drift correction (handoff §2) ----
    private const float STATUS_EVERY_VISIBLE = 60f, STATUS_EVERY_HIDDEN = 300f, KILLSWITCH_EVERY = 5f;
    private static float _lastStatusAt, _lastKillswitchAt;

    private static float _lastCountdownAt;
    private static bool _loginToastPending;

    // ---- accumulators (committed on the matching [NYAR:end]) ----
    private static readonly List<NyarEvent>    _accStatus = new();
    private static readonly List<NyarMeRow>    _accMe = new();
    private static readonly List<NyarTopRow>   _accTop = new();
    private static readonly List<NyarDef>      _accDefs = new();
    private static readonly List<NyarZone>     _accZones = new();
    private static readonly List<NyarRegion>   _accRegions = new();
    private static readonly List<NyarTemplate> _accTemplates = new();
    private static readonly List<NyarPillar>   _accPillars = new();
    private static string _templatesPillar = "";   // filter of the templates read actually on the wire
    private static string _templatesWanted = "";   // filter the UI last asked for

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
            NyarClient.SendProbe();
            NyarDiag.Log($"`.nyar api version` probe {_handshakeAttempts}/{HANDSHAKE_MAX_ATTEMPTS} sent");
            return;
        }

        NyarClient.Pump(OnSending, OnTimedOut);
        NyarState.ExpireKillswitch();

        float t = Time.realtimeSinceStartup;
        if ((_needStatus || _needVersion || _needDefs || _needMe || _needPillars || _needKillswitch || _needRegions)
            && t - _lastRefetchAt >= REFETCH_DEBOUNCE_SECONDS)
            FlushRefetch();

        if (NyarState.SupportsReads)
        {
            bool visible = false;
            try { visible = Plugin.UIManager?.IsNyarTabVisible ?? false; } catch { }
            if (t - _lastStatusAt >= (visible ? STATUS_EVERY_VISIBLE : STATUS_EVERY_HIDDEN)) RefreshStatus();
            if (NyarState.Killswitch && NyarState.ServerAdmin && NyarState.SupportsTwins && t - _lastKillswitchAt >= KILLSWITCH_EVERY)
                RefreshKillswitch();
        }

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
        _lastRefetchAt = _lastCountdownAt = _lastStatusAt = _lastKillswitchAt = 0f;
        _needStatus = _needVersion = _needDefs = _needMe = _needPillars = _needKillswitch = _needRegions = false;
        _loginToastPending = false;
        _accStatus.Clear(); _accMe.Clear(); _accTop.Clear(); _accDefs.Clear(); _accZones.Clear();
        _accRegions.Clear(); _accTemplates.Clear(); _accPillars.Clear(); _templatesPillar = _templatesWanted = "";
        NyarClient.ResetQueue();
        NyarState.Reset();
    }

    // ---- UI-facing read helpers (start a fresh accumulation) ----
    public static void RefreshStatus()
    {
        if (!NyarState.SupportsReads) return;
        _lastStatusAt = Time.realtimeSinceStartup;
        NyarClient.RequestStatus();
    }
    public static void RefreshMe()     { if (!NyarState.SupportsReads || !NyarState.StatsOn) return; NyarClient.RequestMe(); }
    public static void RefreshTop(string stat, string window, int page)
    {
        if (!NyarState.SupportsReads || !NyarState.StatsOn) return;
        NyarClient.RequestTop(stat, window, Math.Max(1, page));
    }
    public static void RefreshDefs()    { if (!NyarState.SupportsReads) return; NyarClient.RequestEvents(1); }
    public static void RefreshZones()   { if (!NyarState.SupportsZoneReads) return; NyarClient.RequestZones(1); }
    public static void RefreshRegions() { if (!NyarState.SupportsRegions) return; NyarClient.RequestRegions(1); }
    public static void RefreshPillars() { if (!NyarState.SupportsTwins) return; NyarClient.RequestPillars(); }
    public static void RefreshKillswitch()
    {
        if (!NyarState.SupportsTwins) return;
        _lastKillswitchAt = Time.realtimeSinceStartup;
        NyarClient.RequestKillswitch();
    }

    /// <summary>Re-read everything an admin console shows (after `io internal`, or a manual refresh).</summary>
    public static void RefreshAllAdmin()
    {
        RefreshStatus();
        if (!NyarState.ServerAdmin) return;
        RefreshDefs();
        RefreshPillars();
        RefreshKillswitch();
    }

    // ---- inbound router (called from ClientChatPatch for every [NYAR:*] line) ----
    public static void HandleLine(string raw)
    {
        NyarDiag.LogIn(raw);
        var line = NyarWireParser.Parse(raw);
        if (line == null) return;

        switch (line.Tag.ToLowerInvariant())
        {
            case "version": NyarClient.CompleteVersion(); OnVersion(line); break;
            case "event":   _accStatus.Add(ReadEvent(line)); break;
            case "me":      _accMe.Add(ReadMe(line)); break;
            case "top":     _accTop.Add(new NyarTopRow(line.GetInt("rank"), line.GetText("name"), line.GetLong("value"), line.GetBool("self"))); break;
            case "def":     _accDefs.Add(ReadDef(line)); break;
            case "zone":    _accZones.Add(ReadZone(line)); break;
            case "region":  _accRegions.Add(new NyarRegion(line.Get("id"), line.GetInt("events"))); break;
            case "tpl":     _accTemplates.Add(new NyarTemplate(line.Get("id"), line.Get("pillar"), line.Get("trigger"), line.GetInt("duration"), line.GetText("summary"))); break;
            case "pillar":  _accPillars.Add(new NyarPillar(line.Get("id"), line.GetBool("on"))); break;
            case "ks":      NyarState.SetKillswitchRead(line.GetBool("on"), line.GetInt("secs"), line.GetInt("events"), line.GetInt("units")); break;
            case "end":     NyarClient.Complete(); OnEnd(line); break;
            case "ok":      NyarClient.Complete(); OnOk(line); break;
            case "err":     NyarClient.Complete(); OnErr(line); break;
            case "ev":      OnEv(line); break;
            default:        LogUtils.LogDiagnostic($"[Nyar] unhandled tag '{line.Tag}': {line.Raw}"); break;
        }
    }

    private static void OnVersion(NyarLine line)
    {
        bool wasPresent = NyarState.Present;
        bool wasAdmin = NyarState.ServerAdmin;
        NyarState.SetVersion(line);
        if (!NyarState.Ready) { LogUtils.LogDiagnostic("[Nyar] api version ready=0 — will retry."); return; }
        if (wasPresent)
        {
            // A re-read (config-changed): an admin who just gained admin rights loads the admin reads.
            if (!wasAdmin && NyarState.ServerAdmin) LoadAdminReads();
            return;
        }

        LogUtils.LogInfo($"[Nyar] Nyarlathotep detected (api={NyarState.ApiVersion}, plugin={NyarState.PluginVersion}, admin={(NyarState.ServerAdmin ? 1 : 0)}).");
        FireAvailability();
        if (!NyarState.SupportsReads) return;   // api 1 = handshake only
        // Handoff §2 login order, one read at a time (the queue serialises them).
        NyarClient.Subscribe(true);
        _loginToastPending = true;
        RefreshStatus();
        RefreshRegions();
        RefreshMe();
        if (NyarState.ServerAdmin) LoadAdminReads();
    }

    private static void LoadAdminReads()
    {
        RefreshDefs();
        if (NyarState.SupportsTwins)
        {
            RefreshPillars();
            RefreshKillswitch();
            BeginTemplatesRead("");
        }
        if (NyarState.Zones) RefreshZones();
    }

    // `.nyar api <cmd> [args] [page]` -> (cmd, page); page 1 when there's no trailing number.
    private static bool ParseRead(string wire, out string cmd, out int page, out string[] parts)
    {
        cmd = ""; page = 1;
        parts = wire.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != ".nyar" || parts[1] != "api") return false;
        cmd = parts[2].ToLowerInvariant();
        if (parts.Length > 3 && int.TryParse(parts[parts.Length - 1], out int p)) page = p;
        return true;
    }

    private static void ClearAccumulator(string cmd)
    {
        switch (cmd)
        {
            case "status":    _accStatus.Clear(); break;
            case "me":        _accMe.Clear(); break;
            case "top":       _accTop.Clear(); break;
            case "events":    _accDefs.Clear(); break;
            case "zones":     _accZones.Clear(); break;
            case "regions":   _accRegions.Clear(); break;
            case "templates": _accTemplates.Clear(); break;
            case "pillar":    _accPillars.Clear(); break;
        }
    }

    // A read's first page starts a fresh accumulation the moment it goes out, so rows left by an earlier read that
    // lost its terminator can't merge into it. A templates read records the filter it actually carries.
    private static void OnSending(string wire)
    {
        if (!ParseRead(wire, out var cmd, out int page, out var parts)) return;
        if (cmd == "templates" && page == 1)
            _templatesPillar = parts.Length == 5 ? parts[3] : (parts.Length == 4 && !int.TryParse(parts[3], out _) ? parts[3] : "");
        if (page == 1) ClearAccumulator(cmd);
    }

    private static void OnTimedOut(string wire)
    {
        if (!ParseRead(wire, out var cmd, out _, out _)) return;
        ClearAccumulator(cmd);
        if (cmd == "status") _loginToastPending = false;
    }

    private static void OnEnd(NyarLine line)
    {
        string cmd = line.Get("cmd").ToLowerInvariant();
        ParsePage(line.Get("page"), out int cur, out int total);
        switch (cmd)
        {
            case "status":
                NyarState.SetStatus(_accStatus.ToArray()); _accStatus.Clear(); NyarState.ClearError();
                if (_loginToastPending) { _loginToastPending = false; LoginToast(); }
                break;
            case "me":     NyarState.SetMe(_accMe.ToArray()); _accMe.Clear(); break;
            case "top":
                NyarState.SetTop(line.Get("stat"), line.Get("window"), cur, total, line.GetInt("count"), _accTop.ToArray());
                _accTop.Clear(); break;
            case "events":
                if (cur < total) NyarClient.SendNext($".nyar api events {cur + 1}");
                else { NyarState.SetDefs(_accDefs.ToArray()); _accDefs.Clear(); }
                break;
            case "zones":
                if (cur < total) NyarClient.SendNext($".nyar api zones {cur + 1}");
                else { NyarState.SetZones(_accZones.ToArray()); _accZones.Clear(); }
                break;
            case "regions":
                if (cur < total) NyarClient.SendNext($".nyar api regions {cur + 1}");
                else { NyarState.SetRegions(_accRegions.ToArray()); _accRegions.Clear(); }
                break;
            case "templates":
                // The filter changed mid-read: a fresh page-1 read is queued, so stop paging this one.
                if (!string.Equals(_templatesPillar, _templatesWanted, StringComparison.OrdinalIgnoreCase)) { _accTemplates.Clear(); break; }
                if (cur < total)
                    NyarClient.SendNext(_templatesPillar.Length == 0 ? $".nyar api templates {cur + 1}" : $".nyar api templates {_templatesPillar} {cur + 1}");
                else { NyarState.SetTemplates(_templatesPillar, _accTemplates.ToArray()); _accTemplates.Clear(); }
                break;
            case "template": _accTemplates.Clear(); break;   // `template info` (unused here): never let its row leak into a list
            case "pillar":     NyarState.SetPillars(_accPillars.ToArray()); _accPillars.Clear(); break;
            case "killswitch": break;  // the [NYAR:ks] row was applied as it arrived
            default: LogUtils.LogDiagnostic($"[Nyar] end for unhandled cmd '{cmd}'."); break;
        }
    }

    /// <summary>Remember which pillar filter the next templates read uses (for paging + the commit).</summary>
    public static void BeginTemplatesRead(string pillar)
    {
        if (!NyarState.SupportsTwins) return;
        _templatesWanted = NyarClient.Tok(pillar);
        NyarClient.DropQueued(".nyar api templates");
        NyarClient.RequestTemplates(_templatesWanted, 1);
    }

    // Twin acknowledgements (api 4, contract §5a) and the subscription ack.
    private static void OnOk(NyarLine line)
    {
        string cmd = line.Get("cmd").ToLowerInvariant(), verb = line.Get("verb").ToLowerInvariant(), id = line.Get("id");
        if (cmd == "sub") { NyarState.SetSubscribed(line.GetBool("on")); return; }
        NyarState.ClearError();

        string name = NyarState.NameOf(id);
        string msg;
        switch (cmd)
        {
            case "event":
                switch (verb)
                {
                    case "start":   msg = $"Started {name}."; _needStatus = _needDefs = true; break;
                    case "stop":    msg = $"Stopped {name}."; _needStatus = _needDefs = true; break;
                    case "enable":  msg = line.GetInt("changed") == 0 ? $"{name} was already enabled." : $"Enabled {name}."; _needDefs = true; break;
                    case "disable": msg = line.GetInt("changed") == 0 ? $"{name} was already disabled." : $"Disabled {name}."; _needDefs = true; break;
                    case "set":     msg = $"{id}: {line.Get("field")} = {Decode(line.Get("value"))}"; _needDefs = true; break;
                    case "reload":  msg = $"Reloaded {line.GetInt("count")} definition(s) from disk."; _needDefs = true; break;
                    case "new":     msg = $"Created {id} ({line.Get("pillar")}), disabled."; _needDefs = true; break;
                    case "copy":    msg = $"Copied {line.Get("from")} to {id}, disabled."; _needDefs = true; break;
                    case "delete":
                        if (line.Has("done")) { msg = $"Deleted {id}."; _needDefs = true; }
                        else { msg = $"Deleting {id}…"; NyarClient.EventDeleteConfirm(id); }
                        break;
                    case "unstick":
                        msg = $"Unstick {name}: {line.GetInt("marked")} unit(s) marked stuck, {line.GetInt("fighting")} still fighting.";
                        _needStatus = true; break;
                    default: msg = $"event {verb} {id}: ok."; _needDefs = true; break;
                }
                break;
            case "template":
                msg = $"Created {id} from template {line.Get("tpl")}, disabled."; _needDefs = true; break;
            case "pillar":
                msg = $"Pillar {id} {(line.GetBool("on") ? "on" : "off")}"
                      + (line.GetInt("changed") == 0 ? " (no change)" : "")
                      + (line.GetInt("ended") > 0 ? $"; ended {line.GetInt("ended")} running event(s)" : "") + ".";
                _needPillars = _needVersion = true;
                if (line.GetInt("ended") > 0) _needStatus = true;
                break;
            case "purge":
                if (verb == "confirm")
                {
                    msg = $"Purged: {line.GetInt("events")} event(s) ended, {line.GetInt("units")} unit(s) despawning; cooldown {FormatSecs(line.GetInt("secs"))}.";
                    NyarState.SetKillswitchSeconds(line.GetInt("secs"));
                    _needStatus = _needKillswitch = true;
                }
                else { msg = "Purging…"; NyarClient.PurgeConfirm(); }
                break;
            default: msg = $"{cmd} {verb}: ok."; break;
        }
        NyarState.SetResult(true, msg);
    }

    private static void OnErr(NyarLine line)
    {
        string cmd = line.Get("cmd"), code = line.Get("code").ToLowerInvariant(), verb = line.Get("verb");
        string reason = line.Get("reason").ToLowerInvariant(), arg = line.Get("arg");
        int secs = line.GetInt("secs");
        if (code == "notready") { LogUtils.LogDiagnostic($"[Nyar] err notready for cmd={cmd}."); return; }
        if (code == "cooldown") NyarState.SetKillswitchSeconds(secs);
        // A failed paged read must not leave a half-filled accumulator behind.
        ClearAccumulator(cmd.ToLowerInvariant());
        if (cmd.Equals("status", StringComparison.OrdinalIgnoreCase)) _loginToastPending = false;
        // io internal (contract §5a): the action may be partly applied — re-read state before acting on it.
        if (code == "io" && reason == "internal") RefreshAllAdmin();

        string message = ErrorText(code, reason, arg, secs);
        string where = string.IsNullOrEmpty(verb) || verb == "-" ? cmd : $"{cmd} {verb}";
        NyarState.SetError(where, message);
        if (!string.IsNullOrEmpty(verb)) NyarState.SetResult(false, message);
    }

    // Contract §4 codes + §5a reasons → one short sentence.
    internal static string ErrorText(string code, string reason, string arg, int secs)
    {
        string argTxt = string.IsNullOrEmpty(arg) || arg == "-" ? "" : arg;
        switch (code)
        {
            case "state":
                return reason switch
                {
                    "already_active"      => "Already running.",
                    "not_active"          => "Not running.",
                    "running"             => "Can't delete a running event — stop it first.",
                    "disabled"            => "That event is disabled — enable it first.",
                    "empower_clash"       => "Clashes with an empowerment that's already running.",
                    "nothing_to_purge"    => "Nothing to purge.",
                    "no_player_in_region" => "No online player is in the event's trigger regions.",
                    "out_of_region"       => "Outside the event's trigger regions.",
                    // §5a: unstick answers a bare `code=state arg=id` when the event has no live unit.
                    "" when argTxt == "id" => "That event has no live units.",
                    _ => $"Wrong state{(reason.Length > 0 ? $" ({reason.Replace('_', ' ')})" : "")}.",
                };
            case "cooldown":  return $"Kill-switch cooldown: {FormatSecs(secs)} left.";
            case "ratelimit": return $"Too fast — try again in {FormatSecs(Math.Max(1, secs))}.";
            case "invalid":
                if (reason == "watch_off") return "Unit watch is off on this server ([UnitWatch] Enabled = false).";
                return $"{(argTxt.Length > 0 ? argTxt : "Value")} is not valid{(reason.Length > 0 ? $": {reason.Replace('_', ' ')}" : "")}.";
            case "exists":    return $"That {(argTxt.Length > 0 ? argTxt : "id")} is already taken.";
            case "full":      return reason == "size" ? "The definitions file would pass 1 MB." : "The definitions file already holds 200 events.";
            case "io":
                return reason switch
                {
                    "internal" => "Server error: re-reading state.",
                    "stale"    => "The file changed on disk — reload and try again.",
                    "read_only" => "The server's events file is read-only.",
                    _ => $"Server file error ({(reason.Length > 0 ? reason.Replace('_', ' ') : "io")}).",
                };
            case "confirm":   return "The confirmation window expired — try again.";
            case "limit":     return "Skipped: the server is already running its maximum number of events.";
            case "disabled":  return reason == "pillar_off" ? "That pillar is switched off." : "That feature is switched off on this server.";
            case "notfound":  return $"Not found{(argTxt.Length > 0 ? $" ({argTxt})" : "")}.";
            case "badarg":
                if (reason == "out_of_region") return "That location is outside the event's regions.";
                return $"Bad argument{(argTxt.Length > 0 ? $": {argTxt}" : "")}{(reason.Length > 0 ? $" ({reason.Replace('_', ' ')})" : "")}.";
            case "noaccess":  return "Admin only.";
            default:          return $"Error '{code}'{(reason.Length > 0 ? $" ({reason})" : "")}.";
        }
    }

    // Push stream (after `api sub on`). Map each type to the read(s) it invalidates, and toast.
    private static void OnEv(NyarLine line)
    {
        string id = line.Get("id");
        int secs = line.GetInt("secs");
        string type = line.Get("type").ToLowerInvariant();
        // A push naming an event we don't know means a line was dropped (the server queues 50 at most).
        if (id.Length > 0 && id != "-" && type != "event-start" && !NyarState.IsKnownEvent(id)) _needStatus = true;
        switch (type)
        {
            case "wave-warn":
                NyarState.SetWaveWarn(id, line.Get("wave"), secs);
                if (Config.Settings.NyarToastWaveWarn)
                    NyarToastHost.Show($"<b>{NyarState.NameOf(id)}</b>: wave {line.Get("wave")} in {FormatSecs(secs)}", "#FFD75A");
                break;
            case "wave":
                NyarState.ClearWaveWarn(id); _needStatus = true;
                if (Config.Settings.NyarToastWave)
                    NyarToastHost.Show($"<b>{NyarState.NameOf(id)}</b>: wave {line.Get("wave")} has arrived", "#E6B85C");
                break;
            case "event-start":
                _needStatus = _needRegions = true;
                if (Config.Settings.NyarToastStartEnd)
                    NyarToastHost.Show($"<b>{NyarState.NameOf(id)}</b> has begun{RegionSuffix(line.Get("region"))}", "#B48CFF");
                break;
            case "event-end":
                NyarState.ClearWaveWarn(id); _needStatus = _needMe = _needRegions = true;
                if (Config.Settings.NyarToastStartEnd)
                    NyarToastHost.Show($"<b>{NyarState.NameOf(id)}</b> is over", "#888888");
                break;
            case "killswitch":
                NyarState.SetKillswitchSeconds(secs); _needStatus = _needVersion = _needRegions = true;
                _needKillswitch = NyarState.ServerAdmin;
                if (Config.Settings.NyarToastKillswitch)
                    NyarToastHost.Show($"<color=#FF8080><b>Kill switch</b></color>: every event was ended. New events are paused for {FormatSecs(secs)}.", "#FF6B6B");
                break;
            case "config-changed":
                _needVersion = true; _needDefs = _needPillars = NyarState.ServerAdmin; break;
            default:
                LogUtils.LogDiagnostic($"[Nyar] unhandled ev type '{line.Get("type")}'."); break;
        }
    }

    // Handoff §2: one toast on login listing the events already under way.
    private static void LoginToast()
    {
        var evs = NyarState.ActiveEvents;
        if (evs.Count == 0 || !Config.Settings.NyarToastLogin) return;
        var parts = evs.Take(4).Select(e =>
        {
            int left = e.SecondsLeftNow;
            return left > 0 ? $"{e.Name} ({FormatMinutes(left)})" : e.Name;
        });
        string more = evs.Count > 4 ? $", +{evs.Count - 4} more" : "";
        NyarToastHost.Show($"<b>{evs.Count} event{(evs.Count == 1 ? "" : "s")} under way</b>: {string.Join(", ", parts)}{more}", "#B48CFF");
    }

    private static string RegionSuffix(string region)
    {
        var regions = NyarState.SplitList(region);
        return regions.Length == 0 ? "" : $" <color=#9FD0FF>({string.Join(", ", regions.Select(PrettyRegion))})</color>";
    }

    /// <summary>"CursedForest" → "Cursed Forest", "Gloomrot_North" → "Gloomrot North".</summary>
    internal static string PrettyRegion(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var sb = new System.Text.StringBuilder(id.Length + 4);
        for (int i = 0; i < id.Length; i++)
        {
            char c = id[i];
            if (c == '_') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && char.IsLower(id[i - 1])) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static void FlushRefetch()
    {
        _lastRefetchAt = Time.realtimeSinceStartup;
        if (_needVersion)    { NyarClient.RequestVersion(); _needVersion = false; }
        if (_needStatus)     { RefreshStatus(); _needStatus = false; }
        if (_needRegions)    { RefreshRegions(); _needRegions = false; }
        if (_needMe)         { RefreshMe(); _needMe = false; }
        if (_needDefs)       { if (NyarState.ServerAdmin) RefreshDefs(); _needDefs = false; }
        if (_needPillars)    { if (NyarState.ServerAdmin) RefreshPillars(); _needPillars = false; }
        if (_needKillswitch) { if (NyarState.ServerAdmin) RefreshKillswitch(); _needKillswitch = false; }
    }

    // ---- record readers ----
    private static NyarEvent ReadEvent(NyarLine l) => new(
        l.Get("id"), l.Get("kind"), l.GetText("name"), l.Get("state"), l.GetClean("faction").Replace('_', ' ').Replace(",", ", "),
        l.Get("left") == "-" ? -1 : l.GetInt("left", -1), l.GetClean("wave"), l.Get("units") == "-" ? -1 : l.GetInt("units", -1),
        Time.realtimeSinceStartup, l.GetClean("region"));

    private static NyarMeRow ReadMe(NyarLine l) => new(
        l.Get("window"), l.GetText("name"), l.GetInt("kills"), l.GetInt("events"), l.GetInt("waves"),
        l.GetInt("defences"), l.GetInt("bossadds"), l.GetInt("deaths"), l.GetBool("hidden"), l.GetBool("none"));

    private static NyarDef ReadDef(NyarLine l) => new(
        l.Get("id"), l.GetText("name"), l.GetBool("enabled"), l.Get("trigger"), l.Get("action"),
        l.GetInt("duration"), l.Get("state"), l.GetClean("reason").Replace('_', ' '), l.GetClean("region"));

    private static NyarZone ReadZone(NyarLine l) => new(
        l.GetText("name"), l.GetClean("x"), l.GetClean("z"), l.GetClean("r"), l.GetInt("threshold"), l.GetInt("cooldown"), l.Get("state"));

    // Wire values put '_' for spaces.
    private static string Decode(string v) => string.IsNullOrEmpty(v) ? v : v.Replace('_', ' ');

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

    private static string FormatMinutes(int secs)
        => secs < 60 ? $"{secs} s" : secs < 3600 ? $"{(secs + 59) / 60} min" : $"{secs / 3600} h {(secs % 3600) / 60:00} min";

    private static void FireAvailability()
    {
        try { AvailabilityChanged?.Invoke(); }
        catch (Exception ex) { LogUtils.LogError($"[Nyar] AvailabilityChanged handler threw: {ex}"); }
    }
}
