using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Raphael.Utils;
using UnityEngine;

namespace Raphael.Services.Uriel;

// Castle Events (Uriel 0.21.0, alpha, off by default) — client-side tracker. Uriel has no [URIEL:*] wire
// shape for Castle Events yet (RAPHAEL_CASTLE_EVENTS_HANDOFF.md §7 is a proposal), so this reads the HUMAN
// text Uriel sends: the §5 run broadcasts (begun / clock running / group defeated / bosses down / complete /
// time warnings / failed / stopped) and the §3 reply starts that carry state (availability refusals, `event
// start`, `event status`, `event stop`). It also keeps a short transcript of the replies to the last command
// the Castle Events tabs sent, so the panel can echo `#id`s, floors and errors without the player reading chat.
//
// OBSERVE-ONLY: ClientChatPatch hands every non-protocol System line to Observe(); nothing here destroys an
// entity, so the lines still reach chat and any other parser. Observe is per-MESSAGE (not per-frame) and
// returns early on the cheap prefix checks.
internal static class UrielEvents
{
    public enum Availability { Unknown, Disabled, AdminOnly, Available }
    public enum RunPhase { None, Spawning, Running, Complete, Failed, Stopped }

    // The game cuts a chat line at ~120 chars and the cut-off tail silently falls back to defaults (handoff §2),
    // so every command the tabs build is checked against this before it is sent.
    public const int CHAT_LIMIT = 118;

    // ---- availability (from the refusal replies, or the planned `api version events=` field) ----
    public static Availability State { get; private set; } = Availability.Unknown;
    public static bool ProbeSent { get; private set; }

    // ---- the current / last run on the plot (from broadcasts + start/status replies) ----
    public static RunPhase Phase   { get; private set; } = RunPhase.None;
    public static string RunName   { get; private set; } = "";
    public static string RunOptions { get; private set; } = "";
    public static int Units        { get; private set; }
    public static int Bosses       { get; private set; }
    public static int BossesLeft   { get; private set; }
    public static int Groups       { get; private set; }
    public static int GroupsCleared { get; private set; }
    public static int Doors        { get; private set; }
    public static int LimitSeconds { get; private set; }
    public static string FinalTime { get; private set; } = "";   // "4m 12s" from complete / bosses-down
    private static float _clockStartAt = -1f;                     // realtime when "The clock is running"
    private static float _endAt = -1f;                            // realtime the run ended (freezes the clock)

    // ---- reply capture for the panel ----
    private const float REPLY_WINDOW = 3f;
    private const int MAX_REPLY_LINES = 14;
    private static float _replyWindowUntil = -1f;
    private static readonly List<string> _reply = new();
    public static string LastCommand { get; private set; } = "";
    public static IReadOnlyList<string> LastReply => _reply;

    // Rolling run log (broadcasts), newest last.
    private const int MAX_LOG = 8;
    private static readonly List<string> _log = new();
    public static IReadOnlyList<string> RunLog => _log;

    /// <summary>Fires on any state, run or reply change. UI handlers only.</summary>
    public static event Action Changed;

    // ---- outbound bookkeeping (called by UrielClient.SendEvent) ----
    internal static void NoteSent(string cmd)
    {
        LastCommand = cmd ?? "";
        _reply.Clear();
        _replyWindowUntil = Time.realtimeSinceStartup + REPLY_WINDOW;
        Fire();
    }

    internal static void NoteRefusedLocally(string cmd, string why)
    {
        LastCommand = cmd ?? "";
        _reply.Clear();
        _reply.Add(why);
        _replyWindowUntil = -1f;
        Fire();
    }

    internal static void NoteProbe() { ProbeSent = true; NoteSent(".uriel event list"); }

    /// <summary>Forward-compatible: the planned `api version` fields (handoff §7) set availability directly.</summary>
    internal static void SetFromVersion(bool events, bool playerEvents, bool isAdmin)
    {
        var next = !events ? Availability.Disabled : (playerEvents || isAdmin) ? Availability.Available : Availability.AdminOnly;
        if (next == State) return;
        State = next;
        Fire();
    }

    // ---- inbound ----
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex ReBegun   = new(@"^A castle event has begun: (\d+) foes(?: \((\d+) bosses?\))? in (\d+) group\(s\)(.*)$", RegexOptions.Compiled);
    private static readonly Regex ReDoors   = new(@"(\d+) door\(s\) are sealed", RegexOptions.Compiled);
    private static readonly Regex ReLimit   = new(@"time limit ([0-9hms ]+)", RegexOptions.Compiled);
    private static readonly Regex ReInPlace = new(@"^All (\d+) foes are in place\. The clock is running(?:: ([0-9hms ]+))?", RegexOptions.Compiled);
    private static readonly Regex ReGroup   = new(@"^Group '([^']+)' is defeated(; its door opens)? \(([0-9hms ]+)\)", RegexOptions.Compiled);
    private static readonly Regex ReBosses  = new(@"^All (\d+) boss(?:es are| is) defeated: ([0-9hms ]+)", RegexOptions.Compiled);
    private static readonly Regex ReDone    = new(@"^The castle event is complete: ([0-9hms ]+)", RegexOptions.Compiled);
    private static readonly Regex ReWarn    = new(@"^(60|10) seconds left\.", RegexOptions.Compiled);
    private static readonly Regex ReStarted = new(@"^Event started from '([^']*)': (\d+) unit\(s\), (\d+) boss\(es\), (\d+) group\(s\), (\d+) door\(s\) held(.*)$", RegexOptions.Compiled);
    private static readonly Regex ReStatus  = new(@"^Event running (\(spawning\)|[0-9hms ]+), '([^']*)'(?: \[([^\]]*)\])?: (\d+)/(\d+) group\(s\) cleared(?:, bosses (\d+)/(\d+) left|, bosses down at ([0-9hms ]+))?(?:, ([0-9hms ]+) left)?", RegexOptions.Compiled);

    public static void Observe(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return;
        string text = raw.IndexOf('<') >= 0 ? Tags.Replace(raw, "") : raw;
        text = text.Trim();
        if (text.Length == 0 || text[0] == '[') return;   // protocol frames are handled elsewhere

        bool changed = false;
        try { changed = ObserveState(text); }
        catch (Exception ex) { LogUtils.LogDebug($"[Uriel] events observe: {ex.Message}"); }

        // Capture every human line inside the reply window that follows a Castle Events command.
        if (Time.realtimeSinceStartup <= _replyWindowUntil)
        {
            if (_reply.Count >= MAX_REPLY_LINES) _reply.RemoveAt(0);
            _reply.Add(text);
            _replyWindowUntil = Time.realtimeSinceStartup + 1f;   // multi-line replies arrive back to back
            changed = true;
        }

        if (changed) Fire();
    }

    private static bool ObserveState(string t)
    {
        // Availability.
        if (t.StartsWith("Castle events are disabled by the server admin", StringComparison.Ordinal))
            return SetState(Availability.Disabled);
        if (t.StartsWith("Castle events are admin-only on this server", StringComparison.Ordinal))
            return SetState(Availability.AdminOnly);
        if (t.StartsWith("Event layout of plot", StringComparison.Ordinal)
            || t.StartsWith("Stand inside a castle plot to set up its event", StringComparison.Ordinal)
            || t.StartsWith("You can only set up events in your own castle", StringComparison.Ordinal))
            SetState(Availability.Available);

        Match m;
        if ((m = ReBegun.Match(t)).Success)
        {
            ResetRun();
            Phase = RunPhase.Spawning;
            Units = Int(m.Groups[1]); Bosses = BossesLeft = Int(m.Groups[2]); Groups = Int(m.Groups[3]);
            var tail = m.Groups[4].Value;
            var d = ReDoors.Match(tail); if (d.Success) Doors = Int(d.Groups[1]);
            var l = ReLimit.Match(tail); if (l.Success) LimitSeconds = ParseDuration(l.Groups[1].Value);
            return Log(t, "#E6B85C");
        }
        if ((m = ReInPlace.Match(t)).Success)
        {
            Phase = RunPhase.Running;
            Units = Int(m.Groups[1]);
            if (m.Groups[2].Success) LimitSeconds = ParseDuration(m.Groups[2].Value);
            _clockStartAt = Time.realtimeSinceStartup; _endAt = -1f;
            return Log(t, "#E6B85C");
        }
        if ((m = ReGroup.Match(t)).Success)
        {
            if (Groups > 0) GroupsCleared = Math.Min(Groups, GroupsCleared + 1); else GroupsCleared++;
            return Log(t, "#90EE90");
        }
        if ((m = ReBosses.Match(t)).Success)
        {
            BossesLeft = 0; if (Bosses == 0) Bosses = Int(m.Groups[1]);
            return Log(t, "#FF9F6B");
        }
        if ((m = ReDone.Match(t)).Success)
        {
            Phase = RunPhase.Complete; FinalTime = m.Groups[1].Value.Trim(); GroupsCleared = Math.Max(GroupsCleared, Groups);
            _endAt = Time.realtimeSinceStartup;
            return Log(t, "#90EE90");
        }
        if (ReWarn.IsMatch(t)) return Log(t, "#FF8080");
        if (t.StartsWith("Time is up: the castle event has failed", StringComparison.Ordinal))
        {
            Phase = RunPhase.Failed; _endAt = Time.realtimeSinceStartup;
            return Log(t, "#FF8080");
        }
        if (t.StartsWith("The castle event was stopped", StringComparison.Ordinal)
            || t.StartsWith("Event stopped:", StringComparison.Ordinal))
        {
            if (Phase is RunPhase.Spawning or RunPhase.Running) { Phase = RunPhase.Stopped; _endAt = Time.realtimeSinceStartup; }
            return t.StartsWith("The castle", StringComparison.Ordinal) ? Log(t, "#A0A0A0") : true;
        }

        // Replies that resync the run model.
        if ((m = ReStarted.Match(t)).Success)
        {
            if (Phase is not (RunPhase.Spawning or RunPhase.Running)) { ResetRun(); Phase = RunPhase.Spawning; }
            RunName = m.Groups[1].Value; Units = Int(m.Groups[2]); Bosses = BossesLeft = Int(m.Groups[3]);
            Groups = Int(m.Groups[4]); Doors = Int(m.Groups[5]);
            var l = ReLimit.Match(m.Groups[6].Value.Replace("limit ", "time limit "));
            if (l.Success) LimitSeconds = ParseDuration(l.Groups[1].Value);
            SetState(Availability.Available);
            return true;
        }
        if ((m = ReStatus.Match(t)).Success)
        {
            bool spawning = m.Groups[1].Value.StartsWith("(", StringComparison.Ordinal);
            Phase = spawning ? RunPhase.Spawning : RunPhase.Running;
            RunName = m.Groups[2].Value; RunOptions = m.Groups[3].Success ? m.Groups[3].Value : "";
            GroupsCleared = Int(m.Groups[4]); Groups = Int(m.Groups[5]);
            if (m.Groups[6].Success) { BossesLeft = Int(m.Groups[6]); Bosses = Int(m.Groups[7]); }
            else if (m.Groups[8].Success) BossesLeft = 0;
            if (!spawning)
            {
                // Re-anchor the local clock on the server's elapsed time (also recovers after a relog).
                _clockStartAt = Time.realtimeSinceStartup - ParseDuration(m.Groups[1].Value); _endAt = -1f;
                if (m.Groups[9].Success) LimitSeconds = ParseDuration(m.Groups[1].Value) + ParseDuration(m.Groups[9].Value);
            }
            SetState(Availability.Available);
            return true;
        }
        if (t.StartsWith("No event is running on this plot", StringComparison.Ordinal))
        {
            if (Phase is RunPhase.Spawning or RunPhase.Running) { Phase = RunPhase.None; _clockStartAt = _endAt = -1f; }
            SetState(Availability.Available);
            return true;
        }
        return false;
    }

    // ---- HUD helpers ----

    /// <summary>Elapsed run time in seconds, or -1 before the clock starts.</summary>
    public static int ElapsedSeconds
    {
        get
        {
            if (_clockStartAt < 0f) return -1;
            float end = _endAt >= 0f ? _endAt : Time.realtimeSinceStartup;
            return Math.Max(0, (int)(end - _clockStartAt));
        }
    }

    public static bool RunActive => Phase is RunPhase.Spawning or RunPhase.Running;

    /// <summary>One-line run summary for the Castle Events tab (rich text).</summary>
    public static string HudText()
    {
        if (Phase == RunPhase.None) return $"<color=#A0A0A0>No run tracked.</color> Press <b>Status</b> to check the plot you're standing on.";
        var sb = new System.Text.StringBuilder();
        sb.Append(Phase switch
        {
            RunPhase.Spawning => "<color=#FFD75A>Spawning…</color>",
            RunPhase.Running  => "<color=#E6B85C>Running</color>",
            RunPhase.Complete => "<color=#90EE90>Complete</color>",
            RunPhase.Failed   => "<color=#FF8080>Failed (time up)</color>",
            RunPhase.Stopped  => "<color=#A0A0A0>Stopped</color>",
            _ => "",
        });
        int el = ElapsedSeconds;
        if (Phase == RunPhase.Complete && FinalTime.Length > 0) sb.Append($"  <b>{FinalTime}</b>");
        else if (el >= 0) sb.Append($"  <b>{Clock(el)}</b>");
        if (RunName.Length > 0) sb.Append($"  ·  '{RunName}'");
        if (RunOptions.Length > 0) sb.Append($" [{RunOptions}]");
        if (Groups > 0) sb.Append($"\nGroups cleared <b>{GroupsCleared}/{Groups}</b>");
        if (Bosses > 0) sb.Append(BossesLeft > 0 ? $"  ·  bosses left <b>{BossesLeft}/{Bosses}</b>" : "  ·  <color=#90EE90>bosses down</color>");
        if (Units > 0) sb.Append($"  ·  {Units} foes");
        if (Doors > 0) sb.Append($"  ·  {Doors} door(s)");
        if (LimitSeconds > 0)
        {
            if (RunActive && el >= 0)
            {
                int left = Math.Max(0, LimitSeconds - el);
                sb.Append($"\nTime left <b>{(left <= 60 ? $"<color=#FF8080>{Clock(left)}</color>" : Clock(left))}</b> of {Clock(LimitSeconds)}");
            }
            else sb.Append($"\nTime limit {Clock(LimitSeconds)}");
        }
        return sb.ToString();
    }

    public static string Clock(int seconds)
    {
        if (seconds < 0) seconds = 0;
        int h = seconds / 3600, m = seconds / 60 % 60, s = seconds % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    /// <summary>"1h 2m 5s" / "4m 12s" / "45s" → seconds. Unknown text → 0.</summary>
    public static int ParseDuration(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        int total = 0;
        foreach (Match p in Regex.Matches(s, @"(\d+)\s*([hms])"))
        {
            int n = int.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture);
            total += p.Groups[2].Value switch { "h" => n * 3600, "m" => n * 60, _ => n };
        }
        return total;
    }

    /// <summary>Clear all state on logout (UrielProtocolService.Reset). Pure field resets, no event.</summary>
    internal static void Reset()
    {
        State = Availability.Unknown; ProbeSent = false;
        ResetRun();
        Phase = RunPhase.None;
        _reply.Clear(); _replyWindowUntil = -1f; LastCommand = "";
        _log.Clear();
    }

    private static void ResetRun()
    {
        RunName = ""; RunOptions = ""; FinalTime = "";
        Units = Bosses = BossesLeft = Groups = GroupsCleared = Doors = LimitSeconds = 0;
        _clockStartAt = _endAt = -1f;
    }

    private static bool SetState(Availability a)
    {
        if (State == a) return false;
        State = a;
        return true;
    }

    private static bool Log(string line, string accent)
    {
        if (_log.Count >= MAX_LOG) _log.RemoveAt(0);
        _log.Add(line);
        if (Config.Settings.UrielEventToasts) UI.ModContent.NyarToastHost.ShowUngated(line, accent);
        return true;
    }

    private static int Int(Group g)
        => g.Success && int.TryParse(g.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static void Fire()
    {
        if (Changed == null) return;
        try { Changed.Invoke(); }
        catch (Exception ex) { LogUtils.LogError($"UrielEvents handler threw: {ex}"); }
    }
}
