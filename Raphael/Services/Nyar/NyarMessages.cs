using System;

namespace Raphael.Services.Nyar;

// Contract §4 codes + §5a reasons → one short sentence. Moved out of NyarProtocolService in 0.72.0 so it is pure and
// unit-tested (version-aware admin_location wording, pillar_off naming the pillar when the server says which).
internal static class NyarMessages
{
    private static readonly string[] Pillars = { "empowerment", "spawns", "boss", "zones", "sieges" };

    public static string ErrorText(string code, string reason, string arg, int secs, string pillar, string plugin)
    {
        code ??= ""; reason ??= "";
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
            case "disabled":
                if (reason == "pillar_off")
                {
                    // Contract §10.7 (planned): pillar_off may name the pillar. Use it only when it's one of the five.
                    string p = (pillar ?? "").Trim().ToLowerInvariant();
                    return Array.IndexOf(Pillars, p) >= 0
                        ? $"The {Cap(p)} pillar is switched off on the server — turn it on in Nyarlathotep → Admin: Pillars."
                        : "This event's pillar is switched off on the server — turn it on in Nyarlathotep → Admin: Pillars.";
                }
                if (reason == "general") return "Nyarlathotep is switched off on this server (General.Enabled = false).";
                return "That feature is switched off on this server.";
            case "notfound":  return $"Not found{(argTxt.Length > 0 ? $" ({argTxt})" : "")}.";
            case "badarg":
                if (reason == "out_of_region") return "That location is outside the event's regions.";
                if (reason == "no_position") return "The server couldn't read your position — try again in a moment.";
                if (reason == "admin_location")
                    // 0.13.0-rc.1 could never start an apocalypse whose goal is "the starting admin" (fixed in rc.2, d492bd5).
                    return NyarVersion.HasAdminLocationBug(plugin)
                        ? "This Nyarlathotep version can't start an event that marches on the starting admin (fixed in 0.13.0-rc.2). " +
                          "Stand where the goal should be, press Goal here in the event editor, then Start."
                        : "The server couldn't read your position for this event's goal — try again in a moment, or press Goal here in the editor.";
                return $"Bad argument{(argTxt.Length > 0 ? $": {argTxt}" : "")}{(reason.Length > 0 ? $" ({reason.Replace('_', ' ')})" : "")}.";
            case "noaccess":  return "Admin only.";
            default:          return $"Error '{code}'{(reason.Length > 0 ? $" ({reason})" : "")}.";
        }
    }

    private static readonly string[] PillarOrder = { "empowerment", "spawns", "boss", "zones", "sieges" };
    private static readonly string[] PillarNames = { "Empowerment", "Spawns", "Boss", "Zones", "Sieges" };

    /// <summary>The Templates panel's group of a template (PLAN.md §C1): pillars in their usual order, and inside Spawns
    /// three groups by id suffix (a naming convention of Nyarlathotep's templates, not a wire field; an unknown suffix
    /// falls back to the plain group). Order sorts the groups; Blurb is "" when there is nothing to add.
    /// 0.73.0: the apocalypse-type templates (Nyarlathotep 0.14.0: -apocalypse, -infestation, -horde-march) get their own group.</summary>
    public static (int Order, string Heading, string Blurb) TemplateGroup(string pillar, string id)
    {
        string p = (pillar ?? "").Trim().ToLowerInvariant();
        int i = Array.IndexOf(PillarOrder, p);
        string name = i >= 0 ? PillarNames[i] : (p.Length == 0 ? "Other" : Cap(p));
        int order = (i >= 0 ? i : PillarOrder.Length) * 10;
        if (p != "spawns") return (order, name, "");
        string s = (id ?? "").ToLowerInvariant();
        if (s.EndsWith("-timed", StringComparison.Ordinal)) return (order + 1, "Spawns — Timed waves", "A new wave every 75 s, cleared or not.");
        if (s.EndsWith("-onslaught", StringComparison.Ordinal)) return (order + 2, "Spawns — Onslaughts", "Rolled waves, a boss mid-run and one or two at the end.");
        if (s.EndsWith("-apocalypse", StringComparison.Ordinal) || s.EndsWith("-infestation", StringComparison.Ordinal) || s.EndsWith("-horde-march", StringComparison.Ordinal))
            return (order + 3, "Spawns — Undead hordes",
                "Apocalypse-type events (apocalypse, infestation, horde march): one runs at a time, and the server cfg caps their size. Each template's summary says where it starts.");
        return (order, "Spawns — Wave events", "");
    }

    public static string FormatSecs(int secs)
    {
        if (secs <= 0) return "0s";
        if (secs < 60) return $"{secs}s";
        if (secs < 3600) return $"{secs / 60}m {secs % 60:00}s";
        return $"{secs / 3600}h {(secs % 3600) / 60:00}m";
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
