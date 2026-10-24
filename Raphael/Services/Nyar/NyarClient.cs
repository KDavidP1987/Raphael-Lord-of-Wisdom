using System.Collections.Generic;
using System.Globalization;
using Raphael.Utils;
using UnityEngine;

namespace Raphael.Services.Nyar;

// The ONLY layer that talks to the Nyarlathotep server mod. Issues `.nyar …` chat commands via
// MessageService; no parsing here. Sibling of Services/Faust/FaustClient.
//
// Reads (`.nyar api …`) and, from api 4, the admin wire twins (`.nyar api event start …`) go out SILENT through
// a paced queue (handoff api 8 §2 "Budget"): one request in flight at a time, the next sent when the reply's
// terminator arrives ([NYAR:end] / [NYAR:ok] / [NYAR:err], NyarProtocolService calls Complete) or after
// REPLY_TIMEOUT, and never closer than SEND_GAP apart. A paged read asks for page cur+1 before anything else.
// Human commands (`.nyar announce`, `.nyar spawn`, `.nyar event info`, and every admin action against an
// api 1-3 server) go out VISIBLE so the user sees Nyarlathotep's reply in chat — Raphael never parses those
// (contract §5).
internal static class NyarClient
{
    public const string API_VERSION = ".nyar api version";

    public static readonly string[] Stats   = { "kills", "events", "waves", "defences", "bossadds", "deaths" };
    public static readonly string[] Windows = { "today", "week", "all" };
    public static readonly string[] PillarIds = { "empowerment", "spawns", "boss", "zones", "sieges" };

    private const float SEND_GAP = 0.25f;
    // 0.72.0: admin twins (event / template / pillar / purge) go at most one per 0.30 s, so every twin together (a
    // pool save, retries, anything else pressed) stays ≤ 3.4/s, under the server's 5 per sliding second (contract
    // §5a) even with arrival jitter. Reads keep 0.25 s: they are not counted (§3 / §5a).
    private const float TWIN_GAP = 0.30f;
    private const float REPLY_TIMEOUT = 5f;

    // 0.72.0: after a twin went out and was never answered, no admin twin is sent for 10 s from that moment, and every
    // twin ok / err that arrives meanwhile is stale by construction (NyarProtocolService discards it). Reads keep
    // flowing. Session-scoped: cleared only by expiry or ResetQueue (disconnect).
    private static readonly NyarQuarantine _quarantine = new();
    public static bool Quarantined => _quarantine.On(Time.realtimeSinceStartup);
    public static int QuarantineSecondsLeft => _quarantine.SecondsLeft(Time.realtimeSinceStartup);
    public static void StartQuarantine()
    {
        _quarantine.Start(Time.realtimeSinceStartup);
        NyarDiag.Log($"quarantine: no admin twin for {NyarQuarantine.Seconds:0}s (a sent command went unanswered)");
    }

    /// <summary>The command sent and not yet answered (null when idle).</summary>
    public static string InFlight => _inFlight;

    /// <summary>`.nyar api event|template|pillar|purge …`: an admin wire twin (answers ok / err, rate-limited).</summary>
    public static bool IsTwin(string cmd)
    {
        if (string.IsNullOrEmpty(cmd) || !cmd.StartsWith(".nyar api ", System.StringComparison.Ordinal)) return false;
        string rest = cmd.Substring(10);
        return rest.StartsWith("event ", System.StringComparison.Ordinal) || rest.StartsWith("template ", System.StringComparison.Ordinal)
            || rest.StartsWith("pillar ", System.StringComparison.Ordinal) || rest == "purge" || rest.StartsWith("purge ", System.StringComparison.Ordinal);
    }

    /// <summary>The `cmd=` of a twin's ok / err line (event, template, pillar, purge).</summary>
    public static bool IsTwinReply(string cmd)
        => cmd is "event" or "template" or "pillar" or "purge";

    private static readonly LinkedList<string> _queue = new();
    private static string _inFlight;
    private static float _inFlightAt, _lastSendAt;

    /// <summary>Queue a machine read / twin (silent). Duplicates already waiting are dropped.</summary>
    public static void Send(string cmd)
    {
        if (string.IsNullOrEmpty(cmd)) return;
        if (_queue.Contains(cmd)) return;
        _queue.AddLast(cmd);
    }

    /// <summary>Queue ahead of everything else (the next page of a paged read).</summary>
    public static void SendNext(string cmd)
    {
        if (string.IsNullOrEmpty(cmd)) return;
        _queue.Remove(cmd);
        _queue.AddFirst(cmd);
    }

    /// <summary>The handshake probe bypasses the queue: it must go out even while nothing else can.</summary>
    public static void SendProbe() { DiagLogOut(API_VERSION, silent: true); MessageService.EnqueueMessageSilent(API_VERSION); }

    /// <summary>Send a human command (reply visible in chat).</summary>
    public static void SendUser(string cmd) { DiagLogOut(cmd, silent: false); MessageService.EnqueueMessage(cmd); }

    /// <summary>The reply to the in-flight request has ended (end/ok/err line). Returns the command that was in flight
    /// (0.72.0: a pool save matches an err line to its step by it — err lines carry no id / field on api 8).</summary>
    public static string Complete()
    {
        string was = _inFlight;
        _inFlight = null;
        return was;
    }

    /// <summary>`[NYAR:version]` is a one-line answer with no [NYAR:end]; it ends a queued version re-read.</summary>
    public static void CompleteVersion() { if (_inFlight == API_VERSION) _inFlight = null; }

    /// <summary>Per-frame pump (called from NyarProtocolService.Tick). <paramref name="onSend"/> runs just before a
    /// queued command goes out (so a page-1 read can start a fresh accumulation then, not when it was queued);
    /// <paramref name="onTimeout"/> runs when a request got no terminator (its half-read rows must be dropped).</summary>
    public static void Pump(System.Action<string> onSend, System.Action<string> onTimeout)
    {
        float now = Time.realtimeSinceStartup;
        if (_inFlight != null)
        {
            if (now - _inFlightAt < REPLY_TIMEOUT) return;
            NyarDiag.Log($"no terminator for `{_inFlight}` after {REPLY_TIMEOUT}s — moving on");
            string lost = _inFlight;
            _inFlight = null;
            if (IsTwin(lost)) StartQuarantine();   // its late answer must not be taken for the next twin's
            onTimeout?.Invoke(lost);
        }
        if (_queue.Count == 0) return;
        // During a quarantine twins wait in the queue; the first read behind them may go.
        bool hold = _quarantine.On(now);
        LinkedListNode<string> node = _queue.First;
        while (node != null && hold && IsTwin(node.Value)) node = node.Next;
        if (node == null) return;
        string cmd = node.Value;
        if (now - _lastSendAt < (IsTwin(cmd) ? TWIN_GAP : SEND_GAP)) return;
        _queue.Remove(node);
        _inFlight = cmd; _inFlightAt = _lastSendAt = now;
        onSend?.Invoke(cmd);
        DiagLogOut(cmd, silent: true);
        MessageService.EnqueueMessageSilent(cmd);
    }

    /// <summary>Drop every queued (not yet sent) command that starts with <paramref name="prefix"/>.</summary>
    public static void DropQueued(string prefix)
    {
        for (var n = _queue.First; n != null;)
        {
            var next = n.Next;
            if (n.Value.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) _queue.Remove(n);
            n = next;
        }
    }

    public static void ResetQueue() { _queue.Clear(); _inFlight = null; _inFlightAt = _lastSendAt = 0f; _quarantine.Clear(); }

    /// <summary>Drop one exact queued (not yet sent) command; true when it was still waiting.</summary>
    public static bool DropExact(string cmd) => _queue.Remove(cmd);

    /// <summary>The exact `event set` command string that <see cref="EventSet"/> sends (a pool save chunks against its
    /// byte length).</summary>
    public static string EventSetCommand(string id, string field, string value)
        => NyarFieldRules.EventSetText(NyarState.SupportsTwins, id, field, value);

    internal static void DiagLogOut(string cmd, bool silent)
    {
        if (!NyarDiag.Enabled) return;
        LogUtils.LogInfo($"[Nyar][diag] >> {(silent ? "(read)  " : "(action)")}{cmd}");
    }

    // ---- reads (silent, queued) ----
    public static void Subscribe(bool on)              => Send($".nyar api sub {(on ? "on" : "off")}");
    public static void RequestStatus()                 => Send(".nyar api status");
    public static void RequestMe()                     => Send(".nyar api me");
    public static void RequestTop(string stat, string window, int page = 1)
        => Send($".nyar api top {Tok(stat)} {Tok(window)} {Num(page)}");
    public static void RequestEvents(int page = 1)     => Send($".nyar api events {Num(page)}");
    public static void RequestZones(int page = 1)      => Send($".nyar api zones {Num(page)}");
    public static void RequestRegions(int page = 1)    => Send($".nyar api regions {Num(page)}");
    public static void RequestTemplates(string pillar, int page = 1)
        => Send(string.IsNullOrEmpty(pillar) ? $".nyar api templates {Num(page)}" : $".nyar api templates {Tok(pillar)} {Num(page)}");
    public static void RequestPillars()                => Send(".nyar api pillar list");
    public static void RequestKillswitch()             => Send(".nyar api killswitch");
    public static void RequestVersion()                => Send(API_VERSION);

    // ---- player actions (visible) ----
    public static void StatsHide()                     => SendUser(".nyar stats hide");
    public static void StatsShow()                     => SendUser(".nyar stats show");
    public static void Share(string stat, string window) => SendUser($".nyar top {Tok(stat)} {Tok(window)} share");

    // ---- admin actions: the api 4 wire twin when the server has it, else the human command ----
    // `Admin("event start x")` sends `.nyar api event start x` (silent, one ok/err line) on api >= 4, or the
    // visible `.nyar event start x` on an older server.
    private static void Admin(string rest)
    {
        if (NyarState.SupportsTwins) Send($".nyar api {rest}");
        else SendUser($".nyar {rest}");
    }

    public static void EventStart(string id)           => Admin($"event start {Tok(id)}");
    public static void EventStop(string id)            => Admin($"event stop {Tok(id)}");
    public static void EventEnable(string id)          => Admin($"event enable {Tok(id)}");
    public static void EventDisable(string id)         => Admin($"event disable {Tok(id)}");
    public static void EventUnstick(string id)         => Admin($"event unstick {Tok(id)}");
    public static void EventSet(string id, string field, string value)
        => Admin($"event set {Tok(id)} {Tok(field)} {Quote(value)}");
    /// <summary>0.72.0: queue an exact twin built by <see cref="EventSetCommand"/> (NyarBatchRunner's steps).</summary>
    internal static void SendTwin(string cmd) => Send(cmd);
    public static void EventSetLocationHere(string id) => Admin($"event set {Tok(id)} location here");
    public static void EventReload()                   => Admin("event reload");
    public static void EventNew(string id, string pillar) => Admin($"event new {Tok(id)} {Tok(pillar)}");
    public static void EventCopy(string id, string newId) => Admin($"event copy {Tok(id)} {Tok(newId)}");
    // Two-step (Raphael's own confirm button is the user's gate). With twins the confirm is sent only after the
    // ask's ok (NyarProtocolService.OnOk), so a refused ask shows its real error instead of `code=confirm`.
    // An api 1-3 server's human replies aren't parsed, so there both go out back to back.
    public static void EventDelete(string id)
    {
        if (NyarState.SupportsTwins) Send($".nyar api event delete {Tok(id)}");
        else { SendUser($".nyar event delete {Tok(id)}"); SendUser($".nyar event delete {Tok(id)} confirm"); }
    }
    internal static void EventDeleteConfirm(string id) => SendNext($".nyar api event delete {Tok(id)} confirm");
    public static void TemplateUse(string tpl, string asId)
        => Admin(string.IsNullOrWhiteSpace(asId) ? $"template use {Tok(tpl)}" : $"template use {Tok(tpl)} as {Tok(asId)}");
    public static void PillarSet(string pillar, bool on) => Admin($"pillar {Tok(pillar)} {(on ? "on" : "off")}");
    public static void Purge()
    {
        if (NyarState.SupportsTwins) Send(".nyar api purge");
        else { SendUser(".nyar purge"); SendUser(".nyar purge confirm"); }
    }
    internal static void PurgeConfirm()                => SendNext(".nyar api purge confirm");

    // Human-only on api 8 (no twin): their replies show in chat for the admin to read.
    // 0.71.0: NyarCapture reads these two replies for the event editor and the Units tab (they still show in chat).
    public static void EventInfo(string id)            { NyarCapture.BeginInfo(Tok(id)); SendUser($".nyar event info {Tok(id)}"); }
    public static void DebugHere(int radius)
    {
        NyarCapture.BeginUnits(radius);
        SendUser(radius > 0 ? $".nyar debug here {Num(radius)}" : ".nyar debug here");
    }
    // 0.74.0: the pool library (Nyarlathotep 0.15.0, admin-only, human replies with no twin). The replies show verbatim
    // in the Pools library card (NyarCapture) and in chat; never parsed. A pool name must pass IsPoolName first.
    public static void PoolList(int page)
    {
        string cmd = page > 1 ? $".nyar pool list {Num(page)}" : ".nyar pool list";
        NyarCapture.BeginPool(cmd); SendUser(cmd);
    }
    public static bool PoolInfo(string pool)
    {
        if (!NyarFieldRules.IsPoolName(pool)) return false;
        string cmd = $".nyar pool info {pool}";
        NyarCapture.BeginPool(cmd); SendUser(cmd);
        return true;
    }
    public static void PoolReload() { NyarCapture.BeginPool(".nyar pool reload"); SendUser(".nyar pool reload"); }

    public static void EventSetGoalHere(string id)    => Admin($"event set {Tok(id)} action.goal here");
    public static void Spawn(string args)              => SendUser($".nyar spawn {args.Trim()}");
    public static void Announce(string text)           => SendUser($".nyar announce \"{(text ?? "").Replace('"', '\'').Trim()}\"");
    public static void ZoneAdd(string name, int radius) => SendUser($".nyar zone add {Tok(name)} {Num(radius)}");
    public static void ZoneRemove(string name)         => SendUser($".nyar zone remove {Tok(name)}");
    public static void StatsReset(string target)       => SendUser($".nyar stats reset {Tok(target)}");
    public static void StatsResetConfirm(string target) => SendUser($".nyar stats reset {Tok(target)} confirm");

    // Single-token args: the wire has no spaces, so a typed name uses '_' the same way the server does.
    internal static string Tok(string s) => (s ?? "").Trim().Replace('"', ' ').Trim().Replace(' ', '_');
    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
    // Free text goes quoted (VCF reads a quoted string as one argument).
    private static string Quote(string s)
    {
        var t = (s ?? "").Replace('"', '\'').Trim();
        return t.IndexOf(' ') >= 0 ? $"\"{t}\"" : t;
    }
}
