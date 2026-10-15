using System;
using System.Collections.Generic;
using Raphael.Utils;
using UnityEngine;

namespace Raphael.Services.Nyar;

// ---- Typed records the UI binds to (one per wire read shape; contract §2-§3) ----

// api status / [NYAR:event] — one active event. Left is seconds remaining when the row arrived;
// ReceivedAt (realtime) lets the UI count down without re-querying. Units is -1 when hidden (non-admin).
// Region (api 5) is the event's action scope as the raw comma list, "" for a global event.
internal sealed record NyarEvent(string Id, string Kind, string Name, string State, string Faction,
    int Left, string Wave, int Units, float ReceivedAt, string Region = "")
{
    public int SecondsLeftNow => Left < 0 ? -1 : Math.Max(0, Left - (int)(Time.realtimeSinceStartup - ReceivedAt));
    public string[] Regions => NyarState.SplitList(Region);
}

// api regions / [NYAR:region] (api 5) — one map region and how many active events name it.
internal sealed record NyarRegion(string Id, int Events);

// api templates / [NYAR:tpl] (api 4) — one built-in template.
internal sealed record NyarTemplate(string Id, string Pillar, string Trigger, int Duration, string Summary);

// api pillar list / [NYAR:pillar] (api 4) — one pillar switch.
internal sealed record NyarPillar(string Id, bool On);

// api me / [NYAR:me] — one window (today | week | all) of the requester's own stats. None = no activity.
internal sealed record NyarMeRow(string Window, string Name, int Kills, int Events, int Waves, int Defences,
    int BossAdds, int Deaths, bool Hidden, bool None);

// api top / [NYAR:top] — one leaderboard row. Self marks the requester's own row.
internal sealed record NyarTopRow(int Rank, string Name, long Value, bool Self);

// api events / [NYAR:def] — one event definition (admin).
internal sealed record NyarDef(string Id, string Name, bool Enabled, string Trigger, string Action,
    int Duration, string State, string Reason, string Region = "")
{
    public string[] Regions => NyarState.SplitList(Region);
}

// api zones / [NYAR:zone] — one defended zone (admin; coordinates only reach admins).
internal sealed record NyarZone(string Name, string X, string Z, string Radius, int Threshold, int Cooldown, string State);

// Cached client model for the Nyarlathotep integration. Static read-only data + a change event per slice;
// NyarProtocolService updates it, the UI binds to it and never parses raw wire lines.
internal static class NyarState
{
    // ---- handshake (contract §2) ----
    public static bool   Present { get; private set; }     // answered `api version` with ready=1
    public static int    ApiVersion { get; private set; }
    public static string PluginVersion { get; private set; } = "";
    public static bool   Ready { get; private set; }
    public static bool   ServerAdmin { get; private set; }  // admin=1 — UI gating only; the server re-checks
    public static bool   Enabled { get; private set; }
    public static bool   Killswitch { get; private set; }
    public static bool   Empower { get; private set; }
    public static bool   Waves { get; private set; }
    public static bool   Boss { get; private set; }
    public static bool   Zones { get; private set; }
    public static bool   Sieges { get; private set; }
    public static bool   StatsOn { get; private set; }
    public static bool   AnnWarn { get; private set; }
    public static bool   AnnBanner { get; private set; }
    public static bool   AnnDaily { get; private set; }
    public static bool   AnnLogin { get; private set; }
    public static bool   AnnShare { get; private set; }
    public static bool   Subscribed { get; private set; }

    /// <summary>api 2 carries every read below (status/me/top/events/zones/sub).</summary>
    public static bool SupportsReads => ApiVersion >= 2;
    /// <summary>api 4: admin actions have wire twins (one ok/err line each); templates, pillar list, killswitch reads.</summary>
    public static bool SupportsTwins => ApiVersion >= 4;
    /// <summary>api 5: the regions read and region= on status/def rows and start/end pushes.</summary>
    public static bool SupportsRegions => ApiVersion >= 5;
    /// <summary>api 8: the `event unstick` twin.</summary>
    public static bool SupportsUnstick => ApiVersion >= 8;
    /// <summary>The `zones` read is PLANNED in the contract (no api yet): an unanswered read would stall the queue
    /// for its 5 s timeout. Change to an api gate when Nyarlathotep ships it. `zone add/remove` work today.</summary>
    public static bool SupportsZoneReads => false;

    // Kill-switch read (api 4): events still active / units still tracked at the last read.
    public static int KillswitchEvents { get; private set; }
    public static int KillswitchUnits { get; private set; }

    public static IReadOnlyList<NyarRegion> RegionList { get; private set; } = Array.Empty<NyarRegion>();
    public static bool RegionsLoaded { get; private set; }
    public static IReadOnlyList<NyarTemplate> Templates { get; private set; } = Array.Empty<NyarTemplate>();
    public static string TemplatesPillar { get; private set; } = "";
    public static bool TemplatesLoaded { get; private set; }
    public static IReadOnlyList<NyarPillar> Pillars { get; private set; } = Array.Empty<NyarPillar>();
    public static bool PillarsLoaded { get; private set; }

    /// <summary>The last admin-action result as a friendly sentence (twin ok/err), "" when none.</summary>
    public static string LastResult { get; private set; } = "";
    public static bool LastResultOk { get; private set; }

    internal static string[] SplitList(string v)
        => string.IsNullOrEmpty(v) || v == "-" ? Array.Empty<string>() : v.Split(',', StringSplitOptions.RemoveEmptyEntries);

    // Kill-switch countdown (from `err code=cooldown secs=` or `ev type=killswitch secs=`). 0 = unknown.
    private static float _killswitchUntil;
    public static int KillswitchSecondsLeft => _killswitchUntil <= 0f ? 0 : Math.Max(0, (int)(_killswitchUntil - Time.realtimeSinceStartup));

    // ---- reads ----
    public static IReadOnlyList<NyarEvent> ActiveEvents { get; private set; } = Array.Empty<NyarEvent>();
    public static bool StatusLoaded { get; private set; }
    // Upcoming-wave warnings from `ev type=wave-warn`, keyed by event id: (wave, realtime the wave starts).
    private static readonly Dictionary<string, (string Wave, float At)> _waveWarn = new(StringComparer.OrdinalIgnoreCase);
    public static bool TryGetWaveWarn(string id, out string wave, out int secondsLeft)
    {
        wave = ""; secondsLeft = 0;
        if (string.IsNullOrEmpty(id) || !_waveWarn.TryGetValue(id, out var w)) return false;
        secondsLeft = (int)(w.At - Time.realtimeSinceStartup);
        if (secondsLeft < 0) return false;
        wave = w.Wave; return true;
    }

    public static IReadOnlyList<NyarMeRow> Me { get; private set; } = Array.Empty<NyarMeRow>();
    public static bool MeLoaded { get; private set; }

    public static IReadOnlyList<NyarTopRow> Top { get; private set; } = Array.Empty<NyarTopRow>();
    public static string TopStat { get; private set; } = "";
    public static string TopWindow { get; private set; } = "";
    public static int    TopPage { get; private set; } = 1;
    public static int    TopPages { get; private set; } = 1;
    public static int    TopCount { get; private set; }
    public static bool   TopLoaded { get; private set; }

    public static IReadOnlyList<NyarDef> Defs { get; private set; } = Array.Empty<NyarDef>();
    public static bool DefsLoaded { get; private set; }
    public static IReadOnlyList<NyarZone> ZoneList { get; private set; } = Array.Empty<NyarZone>();
    public static bool ZonesLoaded { get; private set; }

    /// <summary>Last `[NYAR:err]` as a friendly sentence (with the cmd), "" when none since the last read.</summary>
    public static string LastError { get; private set; } = "";
    public static string LastErrorCmd { get; private set; } = "";

    // ---- change events ----
    public static event Action PresenceChanged;   // handshake fields / subscription / kill switch
    public static event Action StatusChanged;     // active events + wave warnings
    public static event Action MeChanged;
    public static event Action TopChanged;
    public static event Action DefsChanged;
    public static event Action ZonesChanged;
    public static event Action ErrorChanged;
    public static event Action RegionsChanged;
    public static event Action TemplatesChanged;
    public static event Action PillarsChanged;
    public static event Action ResultChanged;     // LastResult (admin action outcome)

    // ---- mutators (called only by NyarProtocolService) ----
    internal static void SetVersion(NyarLine l)
    {
        // Handoff api 8 §2: when admin turns to 0 on a re-handshake, drop every cached admin row.
        if (ServerAdmin && !l.GetBool("admin")) DropAdminCaches();
        ApiVersion = l.GetInt("api"); PluginVersion = l.Get("plugin"); Ready = l.GetBool("ready");
        ServerAdmin = l.GetBool("admin"); Enabled = l.GetBool("enabled"); Killswitch = l.GetBool("killswitch");
        Empower = l.GetBool("empower"); Waves = l.GetBool("waves"); Boss = l.GetBool("boss");
        Zones = l.GetBool("zones"); Sieges = l.GetBool("sieges"); StatsOn = l.GetBool("stats");
        AnnWarn = l.GetBool("annwarn"); AnnBanner = l.GetBool("annbanner"); AnnDaily = l.GetBool("anndaily");
        AnnLogin = l.GetBool("annlogin"); AnnShare = l.GetBool("annshare");
        if (!Killswitch) _killswitchUntil = 0f;
        if (Ready) Present = true;
        Fire(PresenceChanged);
    }
    internal static void SetSubscribed(bool on) { Subscribed = on; Fire(PresenceChanged); }
    /// <summary>The server sends no push when the kill-switch cooldown ends; clear it locally when the timer runs out.
    /// (An admin's `api killswitch` poll corrects it either way.)</summary>
    internal static void ExpireKillswitch()
    {
        if (!Killswitch || _killswitchUntil <= 0f || Time.realtimeSinceStartup < _killswitchUntil) return;
        Killswitch = false; _killswitchUntil = 0f;
        Fire(PresenceChanged);
    }

    internal static void SetKillswitchSeconds(int secs)
    {
        if (secs <= 0) return;
        Killswitch = true; _killswitchUntil = Time.realtimeSinceStartup + secs;
        Fire(PresenceChanged);
    }
    // `api killswitch` row (api 4): on/secs plus the active events and tracked units.
    internal static void SetKillswitchRead(bool on, int secs, int events, int units)
    {
        KillswitchEvents = events; KillswitchUnits = units;
        Killswitch = on;
        _killswitchUntil = on && secs > 0 ? Time.realtimeSinceStartup + secs : 0f;
        Fire(PresenceChanged);
    }

    internal static void SetRegions(IReadOnlyList<NyarRegion> rows) { RegionList = rows ?? Array.Empty<NyarRegion>(); RegionsLoaded = true; Fire(RegionsChanged); }
    internal static void SetTemplates(string pillar, IReadOnlyList<NyarTemplate> rows)
    {
        TemplatesPillar = pillar ?? ""; Templates = rows ?? Array.Empty<NyarTemplate>(); TemplatesLoaded = true;
        Fire(TemplatesChanged);
    }
    internal static void SetPillars(IReadOnlyList<NyarPillar> rows) { Pillars = rows ?? Array.Empty<NyarPillar>(); PillarsLoaded = true; Fire(PillarsChanged); }

    internal static void SetResult(bool ok, string message)
    {
        LastResultOk = ok; LastResult = message ?? "";
        Fire(ResultChanged);
    }

    /// <summary>Best display name for an event id: the status row, else the definition, else the id.</summary>
    public static string NameOf(string id)
    {
        if (string.IsNullOrEmpty(id) || id == "-") return "An event";
        foreach (var e in ActiveEvents) if (string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase) && e.Name.Length > 0) return e.Name;
        foreach (var d in Defs) if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase) && d.Name.Length > 0) return d.Name;
        return id.Replace('_', ' ').Replace('-', ' ');
    }

    public static bool IsKnownEvent(string id)
    {
        foreach (var e in ActiveEvents) if (string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void DropAdminCaches()
    {
        Defs = Array.Empty<NyarDef>(); DefsLoaded = false;
        ZoneList = Array.Empty<NyarZone>(); ZonesLoaded = false;
        Templates = Array.Empty<NyarTemplate>(); TemplatesPillar = ""; TemplatesLoaded = false;
        Pillars = Array.Empty<NyarPillar>(); PillarsLoaded = false;
        KillswitchEvents = KillswitchUnits = 0;
        LastResult = "";
        Fire(DefsChanged); Fire(ZonesChanged); Fire(TemplatesChanged); Fire(PillarsChanged); Fire(ResultChanged);
    }

    internal static void SetStatus(IReadOnlyList<NyarEvent> rows)
    {
        ActiveEvents = rows ?? Array.Empty<NyarEvent>(); StatusLoaded = true;
        Fire(StatusChanged);
    }
    internal static void SetWaveWarn(string id, string wave, int secs)
    {
        if (string.IsNullOrEmpty(id)) return;
        _waveWarn[id] = (wave ?? "", Time.realtimeSinceStartup + Math.Max(0, secs));
        Fire(StatusChanged);
    }
    internal static void ClearWaveWarn(string id) { if (!string.IsNullOrEmpty(id)) _waveWarn.Remove(id); }

    internal static void SetMe(IReadOnlyList<NyarMeRow> rows) { Me = rows ?? Array.Empty<NyarMeRow>(); MeLoaded = true; Fire(MeChanged); }

    internal static void SetTop(string stat, string window, int page, int pages, int count, IReadOnlyList<NyarTopRow> rows)
    {
        TopStat = stat ?? ""; TopWindow = window ?? ""; TopPage = Math.Max(1, page); TopPages = Math.Max(1, pages);
        TopCount = count; Top = rows ?? Array.Empty<NyarTopRow>(); TopLoaded = true;
        Fire(TopChanged);
    }

    internal static void SetDefs(IReadOnlyList<NyarDef> rows) { Defs = rows ?? Array.Empty<NyarDef>(); DefsLoaded = true; Fire(DefsChanged); }
    internal static void SetZones(IReadOnlyList<NyarZone> rows) { ZoneList = rows ?? Array.Empty<NyarZone>(); ZonesLoaded = true; Fire(ZonesChanged); }

    internal static void SetError(string cmd, string message)
    {
        LastErrorCmd = cmd ?? ""; LastError = message ?? "";
        Fire(ErrorChanged);
    }
    internal static void ClearError() { if (LastError.Length == 0) return; LastError = ""; LastErrorCmd = ""; Fire(ErrorChanged); }

    /// <summary>Clear ALL cached state on logout / server switch. PURE field resets — fires no events
    /// (the teardown hook must do no UI work; the UI re-gates when detection re-runs).</summary>
    internal static void Reset()
    {
        Present = false; ApiVersion = 0; PluginVersion = ""; Ready = false; ServerAdmin = false;
        Enabled = Killswitch = Empower = Waves = Boss = Zones = Sieges = StatsOn = false;
        AnnWarn = AnnBanner = AnnDaily = AnnLogin = AnnShare = false; Subscribed = false;
        _killswitchUntil = 0f;
        ActiveEvents = Array.Empty<NyarEvent>(); StatusLoaded = false; _waveWarn.Clear();
        Me = Array.Empty<NyarMeRow>(); MeLoaded = false;
        Top = Array.Empty<NyarTopRow>(); TopStat = TopWindow = ""; TopPage = TopPages = 1; TopCount = 0; TopLoaded = false;
        Defs = Array.Empty<NyarDef>(); DefsLoaded = false;
        ZoneList = Array.Empty<NyarZone>(); ZonesLoaded = false;
        RegionList = Array.Empty<NyarRegion>(); RegionsLoaded = false;
        Templates = Array.Empty<NyarTemplate>(); TemplatesPillar = ""; TemplatesLoaded = false;
        Pillars = Array.Empty<NyarPillar>(); PillarsLoaded = false;
        KillswitchEvents = KillswitchUnits = 0;
        LastError = ""; LastErrorCmd = ""; LastResult = ""; LastResultOk = false;
    }

    private static void Fire(Action evt)
    {
        if (evt == null) return;
        try { evt.Invoke(); }
        catch (Exception ex) { LogUtils.LogError($"NyarState event handler threw: {ex}"); }
    }
}
