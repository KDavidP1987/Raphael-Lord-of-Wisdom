using System.Globalization;
using Raphael.Utils;

namespace Raphael.Services.Nyar;

// The ONLY layer that talks to the Nyarlathotep server mod. Issues `.nyar …` chat commands via
// MessageService; no parsing here. Sibling of Services/Faust/FaustClient.
//
// Reads (`.nyar api …`) go out SILENT: their replies are [NYAR:*] lines ClientChatPatch intercepts and
// destroys anyway. Actions (`.nyar event start`, `.nyar announce`, `.nyar stats hide`, …) go out VISIBLE so
// the user sees Nyarlathotep's human confirmation in chat — Raphael never parses those (contract §5).
//
// Built against the contract's PLANNED api 1/2 shapes (docs/RAPHAEL_INTEGRATION_CONTRACT.md in the
// Nyarlathotep repo). If a shape changes before it ships, adjust here + NyarProtocolService.
internal static class NyarClient
{
    public const string API_VERSION = ".nyar api version";

    public static readonly string[] Stats   = { "kills", "events", "waves", "defences", "bossadds", "deaths" };
    public static readonly string[] Windows = { "today", "week", "all" };

    /// <summary>Send a machine read / silent probe.</summary>
    public static void Send(string cmd) { DiagLogOut(cmd, silent: true); MessageService.EnqueueMessageSilent(cmd); }

    /// <summary>Send a human command (reply visible in chat).</summary>
    public static void SendUser(string cmd) { DiagLogOut(cmd, silent: false); MessageService.EnqueueMessage(cmd); }

    internal static void DiagLogOut(string cmd, bool silent)
    {
        if (!NyarDiag.Enabled) return;
        LogUtils.LogInfo($"[Nyar][diag] >> {(silent ? "(read)  " : "(action)")}{cmd}");
    }

    // ---- reads (silent) ----
    public static void RequestVersion()                => Send(API_VERSION);
    public static void Subscribe(bool on)              => Send($".nyar api sub {(on ? "on" : "off")}");
    public static void RequestStatus()                 => Send(".nyar api status");
    public static void RequestMe()                     => Send(".nyar api me");
    public static void RequestTop(string stat, string window, int page = 1)
        => Send($".nyar api top {Tok(stat)} {Tok(window)} {Num(page)}");
    public static void RequestEvents(int page = 1)     => Send($".nyar api events {Num(page)}");
    public static void RequestZones(int page = 1)      => Send($".nyar api zones {Num(page)}");

    // ---- player actions (visible) ----
    public static void StatsHide()                     => SendUser(".nyar stats hide");
    public static void StatsShow()                     => SendUser(".nyar stats show");
    public static void Share(string stat, string window) => SendUser($".nyar top {Tok(stat)} {Tok(window)} share");

    // ---- admin actions (visible; the server re-checks admin on every one) ----
    public static void EventStart(string id)           => SendUser($".nyar event start {Tok(id)}");
    public static void EventStop(string id)            => SendUser($".nyar event stop {Tok(id)}");
    public static void EventEnable(string id)          => SendUser($".nyar event enable {Tok(id)}");
    public static void EventDisable(string id)         => SendUser($".nyar event disable {Tok(id)}");
    public static void EventSet(string id, string field, string value)
        => SendUser($".nyar event set {Tok(id)} {Tok(field)} {Quote(value)}");
    public static void EventReload()                   => SendUser(".nyar event reload");
    public static void Spawn(string args)              => SendUser($".nyar spawn {args.Trim()}");
    public static void Purge()                         => SendUser(".nyar purge");
    public static void PurgeConfirm()                  => SendUser(".nyar purge confirm");
    public static void Announce(string text)           => SendUser($".nyar announce {Quote(text)}");
    public static void AnnounceDigest()                => SendUser(".nyar announce digest");
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
