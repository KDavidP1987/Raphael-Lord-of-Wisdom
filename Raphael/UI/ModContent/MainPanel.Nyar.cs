using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Raphael.Services.Nyar;
using Raphael.Utils;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using TMPro;
using UnityEngine;

namespace Raphael.UI.ModContent;

// Nyarlathotep tab group (client UI for the admin-run world-event server mod). Built against the Nyarlathotep
// repo's docs/RAPHAEL_INTEGRATION_CONTRACT.md (api 8) and RAPHAEL_HANDOFF_API8.md.
//
// Players: Live Events (api status + push stream, region chips, per-wave countdown, the regions read),
// Leaderboards (api top + Share), My Stats (api me + hide/show), Settings (diagnostics + notification switches).
// Admins: Events (defs + twin actions + unstick + new/copy/delete + field editor + units/test spawn),
// Templates, Pillars, Zones, Announce, Kill Switch. Admin tabs are inline-gated (BeginAdminGate); the server
// re-checks every command. From api 4 admin actions go out as wire twins (one ok/err line, shown as the
// result line); against an older server they're the human commands and the reply shows in chat.
public partial class MainPanel
{
    private bool _nyarSubscribed;
    private GameObject _nyarDiagnosticGo;          // inline "not detected" panel under the rail
    private TextMeshProUGUI _connNyarReadout;      // Connection tab — Nyarlathotep state line

    // Live UI handles (reassigned on every tab build; Unity-null once the page is torn down).
    private GameObject _nyarEventsListGo;
    private TextMeshProUGUI _nyarEventsStatus;
    private GameObject _nyarRegionsListGo;
    private readonly List<(TextMeshProUGUI Label, NyarEvent Ev)> _nyarCountdownLabels = new();
    private GameObject _nyarTopListGo;
    private TextMeshProUGUI _nyarTopStatus;
    private readonly List<(ButtonRef Btn, string Value, string Label)> _nyarStatBtns = new();
    private readonly List<(ButtonRef Btn, string Value, string Label)> _nyarWindowBtns = new();
    private string _nyarTopStat = "kills";
    private string _nyarTopWindow = "week";
    private int _nyarTopPage = 1;
    private GameObject _nyarMeListGo;
    private TextMeshProUGUI _nyarStateReadout;
    private GameObject _nyarDefsListGo;
    private InputFieldRef _nyarDefIdInput, _nyarDefFieldInput, _nyarDefValueInput;
    private InputFieldRef _nyarNewIdInput, _nyarNewPillarInput, _nyarCopyFromInput, _nyarCopyToInput;
    private InputFieldRef _nyarSpawnUnitInput, _nyarSpawnCountInput, _nyarSpawnLevelInput, _nyarSpawnHpInput, _nyarSpawnPowerInput;
    private InputFieldRef _nyarNearRadiusInput;
    private GameObject _nyarTplListGo;
    private TextMeshProUGUI _nyarTplStatus;
    private InputFieldRef _nyarTplAsIdInput;
    private readonly List<(ButtonRef Btn, string Value, string Label)> _nyarTplPillarBtns = new();
    private string _nyarTplPillar = "";
    private GameObject _nyarPillarsListGo;
    private GameObject _nyarZonesListGo;
    private InputFieldRef _nyarZoneNameInput, _nyarZoneRadiusInput;
    private InputFieldRef _nyarAnnounceInput;
    private TextMeshProUGUI _nyarAnnSwitchesLabel;
    private TextMeshProUGUI _nyarKillLabel;
    private TextMeshProUGUI _nyarToastSecsLabel;
    private InputFieldRef _nyarResetTargetInput;
    private readonly List<TextMeshProUGUI> _nyarErrorLabels = new();
    private readonly List<TextMeshProUGUI> _nyarResultLabels = new();

    private void EnsureNyarSubscribed()
    {
        if (_nyarSubscribed) return;
        NyarState.PresenceChanged  += OnNyarPresenceChanged;
        NyarState.StatusChanged    += RebuildNyarEvents;
        NyarState.TopChanged       += RebuildNyarTop;
        NyarState.MeChanged        += RebuildNyarMe;
        NyarState.DefsChanged      += RebuildNyarDefs;
        NyarState.ZonesChanged     += RebuildNyarZones;
        NyarState.ErrorChanged     += RefreshNyarErrors;
        NyarState.RegionsChanged   += RebuildNyarRegions;
        NyarState.TemplatesChanged += RebuildNyarTemplates;
        NyarState.PillarsChanged   += RebuildNyarPillars;
        NyarState.ResultChanged    += RefreshNyarResults;
        NyarProtocolService.AvailabilityChanged += OnNyarPresenceChanged;
        NyarProtocolService.CountdownTick += RefreshNyarCountdowns;
        _nyarSubscribed = true;
    }

    private void UnsubscribeNyar()
    {
        if (!_nyarSubscribed) return;
        NyarState.PresenceChanged  -= OnNyarPresenceChanged;
        NyarState.StatusChanged    -= RebuildNyarEvents;
        NyarState.TopChanged       -= RebuildNyarTop;
        NyarState.MeChanged        -= RebuildNyarMe;
        NyarState.DefsChanged      -= RebuildNyarDefs;
        NyarState.ZonesChanged     -= RebuildNyarZones;
        NyarState.ErrorChanged     -= RefreshNyarErrors;
        NyarState.RegionsChanged   -= RebuildNyarRegions;
        NyarState.TemplatesChanged -= RebuildNyarTemplates;
        NyarState.PillarsChanged   -= RebuildNyarPillars;
        NyarState.ResultChanged    -= RefreshNyarResults;
        NyarProtocolService.AvailabilityChanged -= OnNyarPresenceChanged;
        NyarProtocolService.CountdownTick -= RefreshNyarCountdowns;
        _nyarSubscribed = false;
    }

    private void OnNyarPresenceChanged()
    {
        RefreshNyarStateReadout();
        RefreshNyarAnnSwitches();
        RefreshNyarCountdowns();
        RefreshNyarTopButtons();
        RefreshConnectionReadout();
    }

    // ---------------------------------------------------------------- shared helpers

    // Notes shown at the top of a tab when the mod / a pillar / the reads are unavailable. Returns true
    // when the tab can't show live data (callers still build the action controls).
    private bool AddNyarGateNotes(GameObject parent, bool needStats = false, bool needZones = false, bool needTwins = false)
    {
        string note = null;
        if (!NyarState.Present)
            note = "Nyarlathotep hasn't answered the handshake on this server yet.";
        else if (!NyarState.Enabled)
            note = "Nyarlathotep is switched off on this server (General.Enabled = false).";
        else if (!NyarState.SupportsReads)
            note = $"This server's Nyarlathotep (api {NyarState.ApiVersion}) doesn't send live data yet — it needs api 2. Buttons still send their commands.";
        else if (needTwins && !NyarState.SupportsTwins)
            note = $"This server's Nyarlathotep (api {NyarState.ApiVersion}) is too old for this tab — it needs api 4 (Nyarlathotep 0.5.2 or later).";
        else if (needStats && !NyarState.StatsOn)
            note = "Stats aren't available on this server, so there are no leaderboards or personal stats. (Nyarlathotep hasn't added them yet; they're planned.)";
        else if (needZones && !NyarState.Zones)
            note = "The defended-zones pillar is switched off on this server.";
        if (note == null) return false;
        var card = AddCard(page: parent, name: "NyarGateNote");
        AddBodyText(card, $"<color=#FFD75A>{note}</color>");
        return true;
    }

    private GameObject AddCard(GameObject page, string name) => AddCard(page, name, null);

    private void AddNyarErrorLine(GameObject parent)
    {
        var lbl = AddBodyText(parent, "", Theme.ScaledUI(12));
        _nyarErrorLabels.Add(lbl);
        RefreshNyarErrors();
    }

    private void RefreshNyarErrors()
    {
        _nyarErrorLabels.RemoveAll(l => l == null);
        string text = NyarState.LastError.Length == 0 ? ""
            : $"<color=#FF8080>{NyarState.LastError}</color>" +
              (NyarState.LastErrorCmd.Length > 0 ? $" <color={Theme.MutedBodyHex}>({NyarState.LastErrorCmd})</color>" : "");
        foreach (var l in _nyarErrorLabels) l.text = text;
    }

    // The last admin twin's outcome (api 4+): green on ok, red on a refusal.
    private void AddNyarResultLine(GameObject parent)
    {
        var lbl = AddBodyText(parent, "", Theme.ScaledUI(12));
        _nyarResultLabels.Add(lbl);
        RefreshNyarResults();
    }

    private void RefreshNyarResults()
    {
        _nyarResultLabels.RemoveAll(l => l == null);
        string text = NyarState.LastResult.Length == 0 ? ""
            : NyarState.LastResultOk ? $"<color=#90EE90>✓ {NyarState.LastResult}</color>" : $"<color=#FF8080>{NyarState.LastResult}</color>";
        foreach (var l in _nyarResultLabels) l.text = text;
    }

    // Before an admin action: clear the old outcome so the next one is clearly its answer.
    private static void NyarBeginAction() { NyarState.ClearError(); NyarState.SetResult(true, ""); }

    private InputFieldRef AddNyarInput(GameObject row, string name, string placeholder, int width)
    {
        var input = UIFactory.CreateInputField(row, name, placeholder);
        UIFactory.SetLayoutElement(input.GameObject, minWidth: Theme.ScaledWidth(width), preferredWidth: Theme.ScaledWidth(width),
            flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        return input;
    }

    private static TextMeshProUGUI AddNyarRowText(GameObject row, string text)
    {
        var lbl = UIFactory.CreateLabel(row, "NyarRowText", text, TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lbl.GameObject, minWidth: 200, preferredWidth: 320, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        return lbl.TextMesh;
    }

    private GameObject AddNyarList(GameObject parent, string name)
    {
        var go = UIFactory.CreateVerticalGroup(parent, name, forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 2, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(go, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 20, flexibleHeight: 0);
        return go;
    }

    private static string NyarWord(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);
    private static string NyarText(string s) => (s ?? "").Replace('_', ' ');
    private static string Tok(InputFieldRef f) => (f?.Text ?? "").Trim();

    // Region chips (api 5): "Cursed Forest · Farbane Woods", or "Global" for a server-wide event.
    private static string NyarRegionChips(string[] regions)
        => regions == null || regions.Length == 0
            ? "<color=#888888>Global</color>"
            : string.Join(" ", regions.Select(r => $"<color=#9FD0FF>[{NyarProtocolService.PrettyRegion(r)}]</color>"));

    // Trigger values through api 8, labelled; an unknown one shows raw (contract §1).
    private static string NyarTriggerLabel(string t) => (t ?? "").ToLowerInvariant() switch
    {
        "schedule"      => "Schedule",
        "ingame"        => "In-game time",
        "vbloodkilled"  => "V Blood killed",
        "interval"      => "Interval",
        "regionentered" => "Region entered",
        "factionkills"  => "Faction kills",
        "revenge"       => "Revenge",
        "bossengaged"   => "Boss engaged",
        "bosshealth"    => "Boss health",
        "zoneactivity"  => "Zone activity",
        "manual"        => "Manual",
        _ => string.IsNullOrEmpty(t) ? "-" : t,
    };

    // ---------------------------------------------------------------- PLAYER: LIVE EVENTS

    private void BuildNyarEventsTab(GameObject page)
    {
        EnsureNyarSubscribed();
        var intro = AddCard(page, "NyarEventsIntro");
        AddSectionHeading(intro, "Live events");
        AddBodyText(intro,
            "World events Nyarlathotep is running right now — raids, empowered enemies, boss reinforcements, " +
            "zone alerts and sieges. Updates live; wave countdowns tick down on their own. You also get a short " +
            "on-screen notice when an event begins or ends (Nyarlathotep → Settings).");
        AddNyarGateNotes(page);

        var card = AddCard(page, "NyarEventsCard");
        var row = MakeUrielRow(card, "NyarEventsRow");
        AddUrielButton(row, "NyarEventsRefresh", "Refresh", "Re-read the active events (.nyar api status).",
            () => { NyarState.ClearError(); NyarProtocolService.RefreshStatus(); NyarProtocolService.RefreshRegions(); });
        _nyarEventsStatus = AddNyarRowText(row, "");
        _nyarEventsListGo = AddNyarList(card, "NyarEventsList");
        AddNyarErrorLine(card);

        _nyarRegionsListGo = null;
        if (NyarState.SupportsRegions)
        {
            AddSpacer(page, 6);
            var rc = AddCard(page, "NyarRegionsCard");
            AddSectionHeading(rc, "Regions");
            AddBodyText(rc, "How many events are active in each region of the map right now.", Theme.ScaledUI(12));
            _nyarRegionsListGo = AddNyarList(rc, "NyarRegionsList");
            if (!NyarState.RegionsLoaded) NyarProtocolService.RefreshRegions();
            RebuildNyarRegions();
        }

        if (!NyarState.StatusLoaded) NyarProtocolService.RefreshStatus();
        RebuildNyarEvents();
    }

    private void RebuildNyarEvents()
    {
        if (_nyarEventsListGo == null) return;
        ClearChildren(_nyarEventsListGo);
        _nyarCountdownLabels.Clear();
        var evs = NyarState.ActiveEvents;
        if (_nyarEventsStatus != null)
            _nyarEventsStatus.text = !NyarState.StatusLoaded ? "<color=#888888>Loading…</color>"
                : $"<color=#888888>{evs.Count} active{(NyarState.Subscribed ? " · live" : "")}</color>";
        if (NyarState.StatusLoaded && evs.Count == 0)
        {
            AddBodyText(_nyarEventsListGo, "No events running right now.");
            return;
        }
        foreach (var e in evs)
        {
            var box = AddCard(_nyarEventsListGo, $"NyarEv_{e.Id}", padding: 6, innerSpacing: 2);
            string faction = string.IsNullOrEmpty(e.Faction) ? "" : $"  ·  {e.Faction}";
            string state = e.State == "ending" ? "<color=#FFD75A>ending — units leaving</color>" : e.State;
            AddInfoLabel(box, "NyarEvTitle",
                $"<b>{NyarText(e.Name)}</b>  <color=#888888>{NyarWord(e.Kind)} · {state}{faction}</color>",
                FontStyles.Normal, Theme.ScaledUI(13));
            if (NyarState.SupportsRegions)
                AddInfoLabel(box, "NyarEvRegions", NyarRegionChips(e.Regions), FontStyles.Normal, Theme.ScaledUI(11));
            var cd = AddInfoLabel(box, "NyarEvCountdown", "", FontStyles.Normal, Theme.ScaledUI(12));
            _nyarCountdownLabels.Add((cd, e));
        }
        RefreshNyarCountdowns();
    }

    private void RebuildNyarRegions()
    {
        if (_nyarRegionsListGo == null) return;
        ClearChildren(_nyarRegionsListGo);
        if (!NyarState.RegionsLoaded) { AddBodyText(_nyarRegionsListGo, "Loading…"); return; }
        if (NyarState.RegionList.Count == 0) { AddBodyText(_nyarRegionsListGo, "No regions reported."); return; }
        foreach (var r in NyarState.RegionList)
        {
            string count = r.Events > 0 ? $"<color=#E6B85C><b>{r.Events}</b> active</color>" : "<color=#888888>quiet</color>";
            AddInfoLabel(_nyarRegionsListGo, "NyarRegionRow", $"{NyarProtocolService.PrettyRegion(r.Id)}  —  {count}",
                FontStyles.Normal, Theme.ScaledUI(12));
        }
    }

    private static string NyarCountdownText(NyarEvent e)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(e.Wave)) parts.Add($"Wave {e.Wave}");
        if (NyarState.TryGetWaveWarn(e.Id, out var nextWave, out var warnSecs))
            parts.Add($"<color=#FFD75A>wave {nextWave} in {NyarProtocolService.FormatSecs(warnSecs)}</color>");
        int left = e.SecondsLeftNow;
        if (left >= 0) parts.Add(e.State == "ending" ? $"units gone in {NyarProtocolService.FormatSecs(left)}" : $"{NyarProtocolService.FormatSecs(left)} left");
        if (e.Units >= 0) parts.Add($"{e.Units} units");
        return parts.Count == 0 ? "" : string.Join("  ·  ", parts);
    }

    private void RefreshNyarCountdowns()
    {
        _nyarCountdownLabels.RemoveAll(x => x.Label == null);
        foreach (var (lbl, ev) in _nyarCountdownLabels) lbl.text = NyarCountdownText(ev);
        if (_nyarKillLabel != null)
        {
            int ks = NyarState.KillswitchSecondsLeft;
            string counts = NyarState.SupportsTwins && NyarState.ServerAdmin
                ? $"  <color=#888888>({NyarState.KillswitchEvents} active event(s), {NyarState.KillswitchUnits} tracked unit(s) at the last read)</color>"
                : "";
            _nyarKillLabel.text = (!NyarState.Killswitch
                ? "Kill switch: <color=#90EE90>off</color> — scheduled events can start."
                : ks > 0 ? $"Kill switch: <color=#FF8080>ON</color> — scheduled starts suppressed for {NyarProtocolService.FormatSecs(ks)}."
                         : "Kill switch: <color=#FF8080>ON</color> — scheduled starts suppressed.") + counts;
        }
    }

    // ---------------------------------------------------------------- PLAYER: LEADERBOARDS

    private static readonly string[] NyarStatLabels   = { "Kills", "Events", "Waves", "Defences", "Boss adds", "Deaths" };
    private static readonly string[] NyarWindowLabels = { "Today", "This week", "All time" };

    private void BuildNyarLeaderboardsTab(GameObject page)
    {
        EnsureNyarSubscribed();
        var intro = AddCard(page, "NyarTopIntro");
        AddSectionHeading(intro, "Leaderboards");
        AddBodyText(intro, "Pick a stat and a time window. Your own row is highlighted. Players who hid themselves don't appear.");
        if (AddNyarGateNotes(page, needStats: true) && NyarState.Present && !NyarState.StatsOn) return;   // stats are planned server-side: no dead buttons

        var picks = AddCard(page, "NyarTopPicks");
        _nyarStatBtns.Clear(); _nyarWindowBtns.Clear();
        var sRow1 = MakeUrielRow(picks, "NyarTopStats1");
        var sRow2 = MakeUrielRow(picks, "NyarTopStats2");
        for (int i = 0; i < NyarClient.Stats.Length; i++)
        {
            string v = NyarClient.Stats[i], lbl = NyarStatLabels[i];
            var b = AddUrielButton(i < 3 ? sRow1 : sRow2, $"NyarTopStat_{v}", lbl, $"Rank by {lbl.ToLowerInvariant()}.",
                () => { _nyarTopStat = v; _nyarTopPage = 1; RequestNyarTop(); }, 110);
            _nyarStatBtns.Add((b, v, lbl));
        }
        var wRow = MakeUrielRow(picks, "NyarTopWindows");
        for (int i = 0; i < NyarClient.Windows.Length; i++)
        {
            string v = NyarClient.Windows[i], lbl = NyarWindowLabels[i];
            var b = AddUrielButton(wRow, $"NyarTopWin_{v}", lbl, $"Count {lbl.ToLowerInvariant()}.",
                () => { _nyarTopWindow = v; _nyarTopPage = 1; RequestNyarTop(); }, 110);
            _nyarWindowBtns.Add((b, v, lbl));
        }

        var card = AddCard(page, "NyarTopCard");
        _nyarTopStatus = AddInfoLabel(card, "NyarTopStatus", "", FontStyles.Normal, Theme.ScaledUI(12));
        _nyarTopListGo = UIFactory.CreateVerticalGroup(card, "NyarTopList", forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 1, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_nyarTopListGo, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 20, flexibleHeight: 0);
        var pager = MakeUrielRow(card, "NyarTopPager");
        AddUrielButton(pager, "NyarTopPrev", "← Prev", "Previous page.",
            () => { if (_nyarTopPage > 1) { _nyarTopPage--; RequestNyarTop(); } }, 90);
        AddUrielButton(pager, "NyarTopNext", "Next →", "Next page.",
            () => { if (_nyarTopPage < NyarState.TopPages) { _nyarTopPage++; RequestNyarTop(); } }, 90);
        AddUrielButton(pager, "NyarTopRefresh", "Refresh", "Re-read this board.", RequestNyarTop, 90);
        AddUrielButton(pager, "NyarTopShare", "Share my line", "Post your line on this board to server chat (.nyar top <stat> <window> share). Only works when the server allows sharing; there's a cooldown between shares.",
            () => NyarClient.Share(_nyarTopStat, _nyarTopWindow), 130);
        AddNyarErrorLine(card);

        RefreshNyarTopButtons();
        if (!NyarState.TopLoaded || NyarState.TopStat != _nyarTopStat || NyarState.TopWindow != _nyarTopWindow) RequestNyarTop();
        else RebuildNyarTop();
    }

    private void RequestNyarTop()
    {
        NyarState.ClearError();
        RefreshNyarTopButtons();
        NyarProtocolService.RefreshTop(_nyarTopStat, _nyarTopWindow, _nyarTopPage);
        if (_nyarTopStatus != null && NyarState.SupportsReads && NyarState.StatsOn) _nyarTopStatus.text = "<color=#888888>Loading…</color>";
    }

    private void RefreshNyarTopButtons()
    {
        foreach (var (b, v, l) in _nyarStatBtns)
            if (b?.ButtonText != null) b.ButtonText.text = v == _nyarTopStat ? $"<color=#E6B85C><b>{l}</b></color>" : l;
        foreach (var (b, v, l) in _nyarWindowBtns)
            if (b?.ButtonText != null) b.ButtonText.text = v == _nyarTopWindow ? $"<color=#E6B85C><b>{l}</b></color>" : l;
    }

    private void RebuildNyarTop()
    {
        if (_nyarTopListGo == null) return;
        ClearChildren(_nyarTopListGo);
        if (!NyarState.TopLoaded) return;
        _nyarTopPage = NyarState.TopPage;
        int si = Array.IndexOf(NyarClient.Stats, NyarState.TopStat), wi = Array.IndexOf(NyarClient.Windows, NyarState.TopWindow);
        string statLbl = si >= 0 ? NyarStatLabels[si] : NyarState.TopStat;
        string winLbl  = wi >= 0 ? NyarWindowLabels[wi] : NyarState.TopWindow;
        if (_nyarTopStatus != null)
            _nyarTopStatus.text = $"<b>{statLbl}</b> · {winLbl}  <color=#888888>page {NyarState.TopPage}/{NyarState.TopPages} · {NyarState.TopCount} players</color>";
        if (NyarState.Top.Count == 0) { AddBodyText(_nyarTopListGo, "Nobody on this board yet."); return; }
        foreach (var r in NyarState.Top)
        {
            string line = $"#{r.Rank}  {NyarText(r.Name)}  —  <b>{r.Value.ToString("N0", CultureInfo.InvariantCulture)}</b>";
            AddInfoLabel(_nyarTopListGo, "NyarTopRow", r.Self ? $"<color=#E6B85C>{line}  (you)</color>" : line,
                FontStyles.Normal, Theme.ScaledUI(12));
        }
    }

    // ---------------------------------------------------------------- PLAYER: MY STATS

    private void BuildNyarMyStatsTab(GameObject page)
    {
        EnsureNyarSubscribed();
        var intro = AddCard(page, "NyarMeIntro");
        AddSectionHeading(intro, "My stats");
        AddBodyText(intro, "Your own Nyarlathotep numbers for today, this week and all time. Hiding yourself removes you from the leaderboards; you still see your own numbers.");
        if (AddNyarGateNotes(page, needStats: true) && NyarState.Present && !NyarState.StatsOn) return;   // stats are planned server-side: no dead buttons

        var card = AddCard(page, "NyarMeCard");
        var row = MakeUrielRow(card, "NyarMeRow");
        AddUrielButton(row, "NyarMeRefresh", "Refresh", "Re-read your stats (.nyar api me).",
            () => { NyarState.ClearError(); NyarProtocolService.RefreshMe(); }, 100);
        AddUrielButton(row, "NyarMeHide", "Hide me", "Leave the public leaderboards (.nyar stats hide).",
            () => { NyarClient.StatsHide(); NyarProtocolService.RefreshMe(); }, 100);
        AddUrielButton(row, "NyarMeShow", "Show me", "Appear on the leaderboards again (.nyar stats show).",
            () => { NyarClient.StatsShow(); NyarProtocolService.RefreshMe(); }, 100);
        _nyarMeListGo = AddNyarList(card, "NyarMeList");
        AddNyarErrorLine(card);

        if (!NyarState.MeLoaded) NyarProtocolService.RefreshMe();
        RebuildNyarMe();
    }

    private void RebuildNyarMe()
    {
        if (_nyarMeListGo == null) return;
        ClearChildren(_nyarMeListGo);
        if (!NyarState.MeLoaded) { AddBodyText(_nyarMeListGo, "Loading…"); return; }
        bool hidden = false, any = false;
        foreach (var m in NyarState.Me)
        {
            if (m.None) continue;
            any = true; hidden |= m.Hidden;
            int wi = Array.IndexOf(NyarClient.Windows, m.Window);
            var box = AddCard(_nyarMeListGo, $"NyarMe_{m.Window}", padding: 6, innerSpacing: 1);
            AddInfoLabel(box, "NyarMeTitle", $"<b>{(wi >= 0 ? NyarWindowLabels[wi] : m.Window)}</b>", FontStyles.Normal, Theme.ScaledUI(13));
            AddStatRow(box, "Kills", m.Kills.ToString(CultureInfo.InvariantCulture));
            AddStatRow(box, "Events", m.Events.ToString(CultureInfo.InvariantCulture));
            AddStatRow(box, "Waves", m.Waves.ToString(CultureInfo.InvariantCulture));
            AddStatRow(box, "Defences", m.Defences.ToString(CultureInfo.InvariantCulture));
            AddStatRow(box, "Boss adds", m.BossAdds.ToString(CultureInfo.InvariantCulture));
            AddStatRow(box, "Deaths", m.Deaths.ToString(CultureInfo.InvariantCulture));
        }
        if (!any) { AddBodyText(_nyarMeListGo, "No counted activity yet — take part in an event to get on the board."); return; }
        AddBodyText(_nyarMeListGo, hidden ? "<color=#FFD75A>You're hidden from the leaderboards.</color>" : "You're visible on the leaderboards.");
    }

    // ---------------------------------------------------------------- SETTINGS

    private void BuildNyarSettingsTab(GameObject page)
    {
        EnsureNyarSubscribed();
        var notif = AddCard(page, "NyarSettingsToastCard");
        AddSectionHeading(notif, "Notifications");
        AddBodyText(notif,
            "Short on-screen notices from Nyarlathotep's live updates, shown at the top of the screen. " +
            "They're a copy of what the server announces — chat still shows its own lines.");
        AddUrielBoolToggle(notif, "NyarToastsToggle", "Show Nyarlathotep notifications", Config.Settings.NyarToasts,
            Config.Settings.SetNyarToasts, "Master switch for every Nyarlathotep toast. Default on.");
        AddUrielBoolToggle(notif, "NyarToastStartEnd", "Event begins / ends", Config.Settings.NyarToastStartEnd,
            Config.Settings.SetNyarToastStartEnd, "\"Ashfall Raid has begun (Farbane Woods)\" and \"… is over\". Default on.");
        AddUrielBoolToggle(notif, "NyarToastWaveWarn", "Wave warnings", Config.Settings.NyarToastWaveWarn,
            Config.Settings.SetNyarToastWaveWarn, "\"Wave 2 in 60s\". Only sent when the server's wave warnings are on. Default on.");
        AddUrielBoolToggle(notif, "NyarToastWave", "Wave arrived", Config.Settings.NyarToastWave,
            Config.Settings.SetNyarToastWave, "A notice each time a wave spawns. Default off.");
        AddUrielBoolToggle(notif, "NyarToastKill", "Kill switch", Config.Settings.NyarToastKillswitch,
            Config.Settings.SetNyarToastKillswitch, "When an admin purges every event. Default on.");
        AddUrielBoolToggle(notif, "NyarToastLogin", "Login summary", Config.Settings.NyarToastLogin,
            Config.Settings.SetNyarToastLogin, "On joining, one notice listing the events already under way. Default on.");
        var dr = MakeUrielRow(notif, "NyarToastSecsRow");
        AddUrielButton(dr, "NyarToastSecsDown", "−", "Shorter.", () => { Config.Settings.SetNyarToastSeconds(Config.Settings.NyarToastSeconds - 1); RefreshNyarToastSecs(); }, 40);
        _nyarToastSecsLabel = AddNyarRowText(dr, "");
        AddUrielButton(dr, "NyarToastSecsUp", "+", "Longer.", () => { Config.Settings.SetNyarToastSeconds(Config.Settings.NyarToastSeconds + 1); RefreshNyarToastSecs(); }, 40);
        AddUrielButton(dr, "NyarToastTest", "Test", "Show a sample notification.",
            () => NyarToastHost.Show("<b>Ashfall Raid</b> has begun <color=#9FD0FF>(Farbane Woods)</color>"), 70);
        RefreshNyarToastSecs();

        AddSpacer(page, 6);
        var diagCard = AddCard(page, "NyarSettingsDiagCard");
        AddSectionHeading(diagCard, "Diagnostics");
        AddBodyText(diagCard,
            $"Writes every Nyarlathotep command sent and every {Mono("[NYAR:*]")} reply to {Mono("LogOutput.log")}. " +
            $"Turn on when reporting a problem; filter the log for {Mono("[Nyar]")}.");
        AddUrielBoolToggle(diagCard, "NyarDiagnosticsToggle", "Enable diagnostic details (verbose Nyarlathotep logging)",
            Config.Settings.NyarDiagnostics,
            v => { Config.Settings.SetNyarDiagnostics(v); LogUtils.LogInfo($"[Nyar] diagnostic details -> {(v ? "ON" : "OFF")}."); },
            "ON: log a [Nyar][diag] command/reply trace. OFF: no extra logging. Default OFF.");

        AddSpacer(page, 6);
        var statusCard = AddCard(page, "NyarSettingsStatusCard");
        AddSectionHeading(statusCard, "Server switches");
        _nyarStateReadout = AddBodyText(statusCard, "—", Theme.ScaledUI(12));
        AddBodyText(statusCard,
            "These are set in the server's kdpen.Nyarlathotep.cfg and are read-only here. Re-detect lives at " +
            "<b>Settings and Help → Connection</b>. To force this tab group on or off, set <b>NyarAvailability</b> " +
            "(Auto / On / Off) in kdpen.Raphael.cfg.");
        RefreshNyarStateReadout();
    }

    private void RefreshNyarToastSecs()
    {
        if (_nyarToastSecsLabel != null) _nyarToastSecsLabel.text = $"Each notice stays up <b>{Config.Settings.NyarToastSeconds}s</b>";
    }

    private static string NyarOnOff(bool b) => b ? "<color=#90EE90>on</color>" : "<color=#888888>off</color>";

    private void RefreshNyarStateReadout()
    {
        if (_nyarStateReadout == null) return;
        if (!NyarState.Present)
        {
            _nyarStateReadout.text = NyarProtocolService.DetectionGaveUp ? "Not detected on this server." : "Detecting…";
            return;
        }
        _nyarStateReadout.text =
            $"Plugin {NyarState.PluginVersion} · api {NyarState.ApiVersion} · you are {(NyarState.ServerAdmin ? "an admin" : "a player")} · live updates {NyarOnOff(NyarState.Subscribed)}\n" +
            $"Mod {NyarOnOff(NyarState.Enabled)} · kill switch {(NyarState.Killswitch ? "<color=#FF8080>ON</color>" : "off")} · stats {NyarOnOff(NyarState.StatsOn)}\n" +
            $"Pillars: empower {NyarOnOff(NyarState.Empower)} · waves {NyarOnOff(NyarState.Waves)} · boss {NyarOnOff(NyarState.Boss)} · zones {NyarOnOff(NyarState.Zones)} · sieges {NyarOnOff(NyarState.Sieges)}";
    }

    // ---------------------------------------------------------------- ADMIN: EVENTS

    // Design §6's settable fields (api 8). N and M are a wave / unit-entry number the admin types in.
    private static readonly string[] NyarSettableFields =
    {
        "name", "durationSeconds", "location", "conditions.minPlayers", "conditions.cooldownMinutes", "conditions.chancePercent",
        "trigger.type", "trigger.days", "trigger.times", "trigger.phase", "trigger.bosses", "trigger.minMinutes", "trigger.maxMinutes",
        "trigger.playerCooldownMinutes", "trigger.factions", "trigger.kills", "trigger.windowSeconds", "trigger.shared",
        "trigger.heat", "trigger.warnAt", "trigger.vbloodHeat", "trigger.scope",
        "action.scope", "action.stats.physicalPower", "action.stats.spellPower", "action.stats.maxHealth", "action.stats.attackSpeed",
        "action.stats.moveSpeed", "action.factions",
        "action.waves", "action.intervalSeconds", "action.radius", "action.units", "action.modifiers.level", "action.modifiers.levelDelta",
        "action.modifiers.maxHealth", "action.modifiers.power", "action.modifiers.moveSpeed", "action.modifiers.attackSpeed",
        "action.loot", "action.allowTerritory", "action.behaviour", "action.units.N.chance", "action.fanOut", "action.scoreboard",
        "action.waveList.N", "action.waveList.N.units", "action.waveList.N.afterSeconds", "action.waveList.N.whenCleared",
        "action.waveList.N.units.M.level", "action.waveList.N.units.M.levelDelta", "action.waveList.N.units.M.maxHealth",
        "action.waveList.N.units.M.power", "action.waveList.N.units.M.moveSpeed", "action.waveList.N.units.M.attackSpeed",
        "action.arena", "action.winner", "action.growth", "action.growth.addUnits", "action.growth.addLevels", "action.growth.whenCleared",
        "action.regionLock", "action.bosses",
        "action.goal", "action.goal.radius", "action.goal.name", "action.hordes.count", "action.hordes.everyMinutes", "action.hordes.size",
        "action.roamers.max", "action.marchSpeed",
    };

    private void BuildNyarAdminEventsTab(GameObject page)
    {
        EnsureNyarSubscribed();
        page = BeginAdminGate(page);
        AddNyarGateNotes(page);

        var list = AddCard(page, "NyarDefsCard");
        AddSectionHeading(list, "Event definitions");
        var top = MakeUrielRow(list, "NyarDefsTop");
        AddUrielButton(top, "NyarDefsRefresh", "Refresh list", "Re-read the definitions (.nyar api events).",
            () => { NyarState.ClearError(); NyarProtocolService.RefreshDefs(); }, 120);
        AddUrielButton(top, "NyarDefsReload", "Reload from disk", "Re-read the server's events.json (event reload).",
            () => { NyarBeginAction(); NyarClient.EventReload(); if (!NyarState.SupportsTwins) NyarProtocolService.RefreshDefs(); }, 140);
        AddNyarResultLine(list);
        AddNyarErrorLine(list);
        _nyarDefsListGo = AddNyarList(list, "NyarDefsList");

        // New / copy (api 4 twins; the human commands on an older server).
        AddSpacer(page, 6);
        var nc = AddCard(page, "NyarNewCopyCard");
        AddSectionHeading(nc, "New or copy");
        AddBodyText(nc, "New events and copies are created <b>disabled</b>; set their fields, then enable them.", Theme.ScaledUI(12));
        var nr = MakeUrielRow(nc, "NyarNewRow");
        _nyarNewIdInput     = AddNyarInput(nr, "NyarNewId", "new event id", 140);
        _nyarNewPillarInput = AddNyarInput(nr, "NyarNewPillar", "pillar (spawns…)", 130);
        AddUrielButton(nr, "NyarNewGo", "New", "Create a skeleton definition of that pillar: empowerment, spawns, boss, zones or sieges (event new <id> <pillar>).", () =>
        {
            string id = Tok(_nyarNewIdInput), pillar = Tok(_nyarNewPillarInput).ToLowerInvariant();
            if (id.Length == 0 || Array.IndexOf(NyarClient.PillarIds, pillar) < 0)
            { NyarState.SetError("event new", $"Enter an id and a pillar ({string.Join(", ", NyarClient.PillarIds)})."); return; }
            NyarBeginAction(); NyarClient.EventNew(id, pillar);
        }, 70);
        var cr = MakeUrielRow(nc, "NyarCopyRow");
        _nyarCopyFromInput = AddNyarInput(cr, "NyarCopyFrom", "copy from id", 140);
        _nyarCopyToInput   = AddNyarInput(cr, "NyarCopyTo", "new id", 130);
        AddUrielButton(cr, "NyarCopyGo", "Copy", "Copy a definition under a new id (event copy <id> <newId>).", () =>
        {
            string from = Tok(_nyarCopyFromInput), to = Tok(_nyarCopyToInput);
            if (from.Length == 0 || to.Length == 0) { NyarState.SetError("event copy", "Enter the id to copy and the new id."); return; }
            NyarBeginAction(); NyarClient.EventCopy(from, to);
        }, 70);

        // Field editor.
        AddSpacer(page, 6);
        var edit = AddCard(page, "NyarDefEditCard");
        AddSectionHeading(edit, "Edit one field");
        AddBodyText(edit,
            $"Sends {Mono("event set <id> <field> <value>")}. Pick a field from the list (replace {Mono("N")} / {Mono("M")} with a wave or unit number), " +
            "type the value, then Set. The server validates it and echoes the stored value. A trigger field needs its trigger type, an action field its action type. " +
            "Raphael can't read an event's current values yet — use <b>Show in chat</b> to see them.", Theme.ScaledUI(12));
        var fr = MakeUrielRow(edit, "NyarDefFieldPickRow");
        var fieldOpts = new[] { "Pick a field…" }.Concat(NyarSettableFields).ToArray();
        var ddObj = UIFactory.CreateDropdown(fr, "NyarFieldPicker", out var fieldDd, fieldOpts[0], 11, i =>
        {
            if (i > 0 && i < fieldOpts.Length && _nyarDefFieldInput != null) _nyarDefFieldInput.Text = fieldOpts[i];
        }, fieldOpts);
        UIFactory.SetLayoutElement(ddObj, minWidth: Theme.ScaledWidth(260), preferredWidth: Theme.ScaledWidth(260), flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        try { Raphael.UI.Forms.FormDropdownRegistry.Register(fieldDd); } catch { }
        var er = MakeUrielRow(edit, "NyarDefEditRow");
        _nyarDefIdInput    = AddNyarInput(er, "NyarDefId", "event id", 120);
        _nyarDefFieldInput = AddNyarInput(er, "NyarDefField", "field", 170);
        _nyarDefValueInput = AddNyarInput(er, "NyarDefValue", "value", 150);
        var er2 = MakeUrielRow(edit, "NyarDefEditRow2");
        AddUrielButton(er2, "NyarDefSet", "Set", "Apply this field change.", () =>
        {
            string id = Tok(_nyarDefIdInput), field = Tok(_nyarDefFieldInput), val = Tok(_nyarDefValueInput);
            if (id.Length == 0 || field.Length == 0 || val.Length == 0) { NyarState.SetError("event set", "Fill in the event id, field and value."); return; }
            if (field.Contains(".N") || field.Contains(".M")) { NyarState.SetError("event set", "Replace N / M in the field with a number."); return; }
            NyarBeginAction(); NyarClient.EventSet(id, field, val);
            if (!NyarState.SupportsTwins) NyarProtocolService.RefreshDefs();
        }, 70);
        AddUrielButton(er2, "NyarDefLocHere", "Location here", "Set the event's location to where you stand (event set <id> location here).", () =>
        {
            string id = Tok(_nyarDefIdInput);
            if (id.Length == 0) { NyarState.SetError("event set", "Enter the event id first."); return; }
            NyarBeginAction(); NyarClient.EventSetLocationHere(id);
        }, 120);
        AddUrielButton(er2, "NyarDefInfo", "Show in chat", "Print this event's full definition and run state in chat (.nyar event info <id>).", () =>
        {
            string id = Tok(_nyarDefIdInput);
            if (id.Length == 0) { NyarState.SetError("event info", "Enter the event id first."); return; }
            NyarClient.EventInfo(id);
        }, 120);

        // Units (no wire read on api 8: human commands, replies in chat).
        AddSpacer(page, 6);
        var units = AddCard(page, "NyarUnitsCard");
        AddSectionHeading(units, "Units");
        AddBodyText(units, $"<b>Units near me</b> lists the tracked units around you in chat ({Mono(".nyar debug here [radius]")}). " +
            $"<b>Test spawn</b> spawns next to you: {Mono(".nyar spawn <unit> [count] [level] [hp×] [power×]")}; leave optional fields blank for defaults.",
            Theme.ScaledUI(12));
        var ur = MakeUrielRow(units, "NyarNearRow");
        _nyarNearRadiusInput = AddNyarInput(ur, "NyarNearRadius", "radius (m, optional)", 140);
        AddUrielButton(ur, "NyarNearGo", "Units near me", "List tracked units around you in chat (.nyar debug here).", () =>
        {
            string r = Tok(_nyarNearRadiusInput);
            int radius = 0;
            if (r.Length > 0 && (!int.TryParse(r, NumberStyles.Integer, CultureInfo.InvariantCulture, out radius) || radius <= 0))
            { NyarState.SetError("debug here", "Radius must be a whole number of metres."); return; }
            NyarClient.DebugHere(radius);
        }, 120);
        var sr = MakeUrielRow(units, "NyarSpawnRow");
        _nyarSpawnUnitInput  = AddNyarInput(sr, "NyarSpawnUnit", "unit (name or GUID)", 170);
        _nyarSpawnCountInput = AddNyarInput(sr, "NyarSpawnCount", "count", 60);
        _nyarSpawnLevelInput = AddNyarInput(sr, "NyarSpawnLevel", "level", 60);
        _nyarSpawnHpInput    = AddNyarInput(sr, "NyarSpawnHp", "hp×", 55);
        _nyarSpawnPowerInput = AddNyarInput(sr, "NyarSpawnPower", "power×", 60);
        var sr2 = MakeUrielRow(units, "NyarSpawnRow2");
        AddUrielButton(sr2, "NyarSpawnGo", "Test spawn", "Send the test spawn.", () =>
        {
            string unit = NyarClient.Tok(Tok(_nyarSpawnUnitInput));
            if (unit.Length == 0) { NyarState.SetError("spawn", "Enter a unit to spawn."); return; }
            // Optional args are positional — stop at the first blank so later ones don't shift left.
            var args = new List<string> { unit };
            foreach (var f in new[] { _nyarSpawnCountInput, _nyarSpawnLevelInput, _nyarSpawnHpInput, _nyarSpawnPowerInput })
            {
                string t = Tok(f);
                if (t.Length == 0) break;
                if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) { NyarState.SetError("spawn", $"'{t}' isn't a number."); return; }
                args.Add(t);
            }
            NyarClient.Spawn(string.Join(" ", args));
        }, 110);

        if (!NyarState.DefsLoaded) NyarProtocolService.RefreshDefs();
        RebuildNyarDefs();
    }

    private void RebuildNyarDefs()
    {
        if (_nyarDefsListGo == null) return;
        ClearChildren(_nyarDefsListGo);
        if (!NyarState.DefsLoaded) { AddBodyText(_nyarDefsListGo, "Press Refresh list to load."); return; }
        if (NyarState.Defs.Count == 0) { AddBodyText(_nyarDefsListGo, "No event definitions on this server."); return; }
        foreach (var d in NyarState.Defs)
        {
            var box = AddCard(_nyarDefsListGo, $"NyarDef_{d.Id}", padding: 6, innerSpacing: 2);
            string stateCol = d.State switch { "active" => "#90EE90", "disabled" => "#FF8080", "scheduled" => "#FFD75A", _ => "#AAAAAA" };
            string dur = d.Duration > 0 ? $" · {NyarProtocolService.FormatSecs(d.Duration)}" : "";
            AddInfoLabel(box, "NyarDefTitle",
                $"<b>{NyarText(d.Name)}</b> <color=#888888>({d.Id})</color>  <color={stateCol}>{d.State}</color>  " +
                $"<color=#888888>{NyarTriggerLabel(d.Trigger)} → {NyarWord(d.Action)}{dur}</color>",
                FontStyles.Normal, Theme.ScaledUI(12));
            if (NyarState.SupportsRegions)
                AddInfoLabel(box, "NyarDefRegions", NyarRegionChips(d.Regions), FontStyles.Normal, Theme.ScaledUI(11));
            if (d.State == "disabled" && !string.IsNullOrEmpty(d.Reason))
                AddBodyText(box, $"<color=#FF8080>Disabled: {NyarText(d.Reason)}</color>", Theme.ScaledUI(11));
            var row = MakeUrielRow(box, "NyarDefBtns");
            string id = d.Id;
            bool running = d.State == "active";
            if (running)
            {
                AddUrielButton(row, "NyarDefStop", "Stop", $"End {id} now; its units despawn (event stop {id}).",
                    () => { NyarBeginAction(); NyarClient.EventStop(id); if (!NyarState.SupportsTwins) { NyarProtocolService.RefreshStatus(); NyarProtocolService.RefreshDefs(); } }, 64);
                if (NyarState.SupportsUnstick)
                    AddUrielButton(row, "NyarDefUnstick", "Unstick", $"Move {id}'s units that haven't fought lately back into play; after two failed tries they're removed with no kill credit (event unstick {id}).",
                        () => { NyarBeginAction(); NyarClient.EventUnstick(id); }, 74);
            }
            else
                AddUrielButton(row, "NyarDefStart", "Start", $"Start {id} now (event start {id}).",
                    () => { NyarBeginAction(); NyarClient.EventStart(id); if (!NyarState.SupportsTwins) { NyarProtocolService.RefreshStatus(); NyarProtocolService.RefreshDefs(); } }, 64);
            if (d.Enabled)
                AddUrielButton(row, "NyarDefDisable", "Disable", $"Stop {id} from being scheduled (event disable {id}).",
                    () => { NyarBeginAction(); NyarClient.EventDisable(id); if (!NyarState.SupportsTwins) NyarProtocolService.RefreshDefs(); }, 74);
            else
                AddUrielButton(row, "NyarDefEnable", "Enable", $"Allow {id} to be scheduled (event enable {id}).",
                    () => { NyarBeginAction(); NyarClient.EventEnable(id); if (!NyarState.SupportsTwins) NyarProtocolService.RefreshDefs(); }, 74);
            AddUrielButton(row, "NyarDefEdit", "Edit…", "Put this id in the field editor below.",
                () => { if (_nyarDefIdInput != null) _nyarDefIdInput.Text = id; }, 60);
            AddUrielButton(row, "NyarDefInfoBtn", "Info", $"Print {id}'s definition and run state in chat (.nyar event info {id}).",
                () => NyarClient.EventInfo(id), 54);
            if (NyarState.SupportsTwins)
            {
                AddUrielButton(row, "NyarDefCopy", "Copy…", "Put this id in the Copy box below.",
                    () => { if (_nyarCopyFromInput != null) _nyarCopyFromInput.Text = id; }, 64);
                if (!running)
                    AddUrielConfirmButton(row, $"NyarDefDelete_{id}", "Delete", $"Delete {id} from events.json (event delete {id}, then confirm).",
                        () => { NyarBeginAction(); NyarClient.EventDelete(id); }, 70, new Color(0.55f, 0.15f, 0.15f, 1f));
            }
        }
    }

    // ---------------------------------------------------------------- ADMIN: TEMPLATES (api 4)

    private static readonly string[] NyarPillarLabels = { "Empowerment", "Spawns", "Boss", "Zones", "Sieges" };

    private void BuildNyarAdminTemplatesTab(GameObject page)
    {
        EnsureNyarSubscribed();
        page = BeginAdminGate(page);
        AddNyarGateNotes(page, needTwins: true);

        var intro = AddCard(page, "NyarTplIntro");
        AddSectionHeading(intro, "Templates");
        AddBodyText(intro,
            "Nyarlathotep's built-in event templates. <b>Create</b> copies one into events.json, disabled, under its own " +
            "id — or under the id you type below. Then edit and enable it on <b>Admin: Events</b>.", Theme.ScaledUI(12));
        var pr = MakeUrielRow(intro, "NyarTplPillars");
        _nyarTplPillarBtns.Clear();
        var allBtn = AddUrielButton(pr, "NyarTplAll", "All", "Every template.", () => { _nyarTplPillar = ""; RequestNyarTemplates(); }, 60);
        _nyarTplPillarBtns.Add((allBtn, "", "All"));
        for (int i = 0; i < NyarClient.PillarIds.Length; i++)
        {
            string v = NyarClient.PillarIds[i], lbl = NyarPillarLabels[i];
            var b = AddUrielButton(pr, $"NyarTpl_{v}", lbl, $"Only {lbl.ToLowerInvariant()} templates.", () => { _nyarTplPillar = v; RequestNyarTemplates(); }, 96);
            _nyarTplPillarBtns.Add((b, v, lbl));
        }
        var ar = MakeUrielRow(intro, "NyarTplAsRow");
        _nyarTplAsIdInput = AddNyarInput(ar, "NyarTplAsId", "create as id (optional)", 180);
        _nyarTplStatus = AddNyarRowText(ar, "");

        var card = AddCard(page, "NyarTplCard");
        AddNyarResultLine(card);
        AddNyarErrorLine(card);
        _nyarTplListGo = AddNyarList(card, "NyarTplList");

        if (!NyarState.TemplatesLoaded || NyarState.TemplatesPillar != _nyarTplPillar) RequestNyarTemplates();
        else RebuildNyarTemplates();
    }

    private void RequestNyarTemplates()
    {
        NyarState.ClearError();
        foreach (var (b, v, l) in _nyarTplPillarBtns)
            if (b?.ButtonText != null) b.ButtonText.text = v == _nyarTplPillar ? $"<color=#E6B85C><b>{l}</b></color>" : l;
        NyarProtocolService.BeginTemplatesRead(_nyarTplPillar);
        if (_nyarTplStatus != null) _nyarTplStatus.text = "<color=#888888>Loading…</color>";
    }

    private void RebuildNyarTemplates()
    {
        foreach (var (b, v, l) in _nyarTplPillarBtns)
            if (b?.ButtonText != null) b.ButtonText.text = v == _nyarTplPillar ? $"<color=#E6B85C><b>{l}</b></color>" : l;
        if (_nyarTplListGo == null) return;
        ClearChildren(_nyarTplListGo);
        if (_nyarTplStatus != null) _nyarTplStatus.text = NyarState.TemplatesLoaded ? $"<color=#888888>{NyarState.Templates.Count} template(s)</color>" : "";
        if (!NyarState.TemplatesLoaded) { AddBodyText(_nyarTplListGo, "Loading…"); return; }
        if (NyarState.Templates.Count == 0) { AddBodyText(_nyarTplListGo, "No templates."); return; }
        foreach (var t in NyarState.Templates)
        {
            var box = AddCard(_nyarTplListGo, $"NyarTpl_{t.Id}", padding: 6, innerSpacing: 2);
            string dur = t.Duration > 0 ? $" · {NyarProtocolService.FormatSecs(t.Duration)}" : "";
            AddInfoLabel(box, "NyarTplTitle",
                $"<b>{NyarText(t.Summary)}</b> <color=#888888>({t.Id})</color>  <color=#888888>{NyarWord(t.Pillar)} · {NyarTriggerLabel(t.Trigger)}{dur}</color>",
                FontStyles.Normal, Theme.ScaledUI(12));
            var row = MakeUrielRow(box, "NyarTplBtns");
            string id = t.Id;
            AddUrielButton(row, "NyarTplUse", "Create", $"Copy {id} into events.json, disabled (template use {id} [as <id>]).",
                () => { NyarBeginAction(); NyarClient.TemplateUse(id, Tok(_nyarTplAsIdInput)); }, 80);
        }
    }

    // ---------------------------------------------------------------- ADMIN: PILLARS (api 4)

    private void BuildNyarAdminPillarsTab(GameObject page)
    {
        EnsureNyarSubscribed();
        page = BeginAdminGate(page);
        AddNyarGateNotes(page, needTwins: true);

        var card = AddCard(page, "NyarPillarsCard");
        AddSectionHeading(card, "Pillars");
        AddBodyText(card,
            "Each pillar is a family of events. Switching one <b>off</b> ends its running events at once and stops new ones; " +
            "the change is saved to the server's cfg.", Theme.ScaledUI(12));
        var top = MakeUrielRow(card, "NyarPillarsTop");
        AddUrielButton(top, "NyarPillarsRefresh", "Refresh", "Re-read the switches (.nyar api pillar list).",
            () => { NyarState.ClearError(); NyarProtocolService.RefreshPillars(); }, 100);
        AddNyarResultLine(card);
        AddNyarErrorLine(card);
        _nyarPillarsListGo = AddNyarList(card, "NyarPillarsList");

        if (!NyarState.PillarsLoaded) NyarProtocolService.RefreshPillars();
        RebuildNyarPillars();
    }

    private void RebuildNyarPillars()
    {
        if (_nyarPillarsListGo == null) return;
        ClearChildren(_nyarPillarsListGo);
        if (!NyarState.PillarsLoaded) { AddBodyText(_nyarPillarsListGo, "Loading…"); return; }
        foreach (var p in NyarState.Pillars)
        {
            var row = MakeUrielRow(_nyarPillarsListGo, $"NyarPillar_{p.Id}");
            int i = Array.IndexOf(NyarClient.PillarIds, p.Id);
            string lbl = i >= 0 ? NyarPillarLabels[i] : NyarWord(p.Id);
            AddNyarRowText(row, $"<b>{lbl}</b>  {NyarOnOff(p.On)}");
            string id = p.Id;
            if (p.On)
                AddUrielConfirmButton(row, $"NyarPillarOff_{id}", "Switch off", $"Turn {lbl} off and end its running events (pillar {id} off).",
                    () => { NyarBeginAction(); NyarClient.PillarSet(id, false); }, 110, new Color(0.55f, 0.15f, 0.15f, 1f));
            else
                AddUrielButton(row, $"NyarPillarOn_{id}", "Switch on", $"Turn {lbl} on (pillar {id} on).",
                    () => { NyarBeginAction(); NyarClient.PillarSet(id, true); }, 110);
        }
    }

    // ---------------------------------------------------------------- ADMIN: ZONES

    private void BuildNyarAdminZonesTab(GameObject page)
    {
        EnsureNyarSubscribed();
        page = BeginAdminGate(page);
        _nyarZonesListGo = null;
        if (!NyarState.SupportsZoneReads)
        {
            // Defended zones are still PLANNED in Nyarlathotep (no `zone` commands, no `zones` read on api 8),
            // so the tab explains that instead of offering buttons the server would reject.
            var note = AddCard(page, "NyarZonesPlanned");
            AddSectionHeading(note, "Defended zones (planned)");
            AddBodyText(note,
                "Defended zones aren't in Nyarlathotep yet. When the server adds them, this tab will list the zones " +
                "and let you add one where you stand or remove one.");
            return;
        }
        AddNyarGateNotes(page, needZones: true);

        var add = AddCard(page, "NyarZoneAddCard");
        AddSectionHeading(add, "Add a zone where you stand");
        AddBodyText(add, $"Stand at the centre of the area to defend, then add it: {Mono(".nyar zone add <name> <radius>")}.");
        var ar = MakeUrielRow(add, "NyarZoneAddRow");
        _nyarZoneNameInput   = AddNyarInput(ar, "NyarZoneName", "zone name", 160);
        _nyarZoneRadiusInput = AddNyarInput(ar, "NyarZoneRadius", "radius (m)", 90);
        AddUrielButton(ar, "NyarZoneAdd", "Add here", "Create the zone at your position.", () =>
        {
            string name = Tok(_nyarZoneNameInput);
            if (name.Length == 0 || !int.TryParse(Tok(_nyarZoneRadiusInput), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) || r <= 0)
            { NyarState.SetError("zone add", "Enter a name and a whole-number radius."); return; }
            NyarClient.ZoneAdd(name, r);
            NyarProtocolService.RefreshZones();
        }, 100);

        AddSpacer(page, 6);
        var list = AddCard(page, "NyarZonesCard");
        AddSectionHeading(list, "Zones");
        var top = MakeUrielRow(list, "NyarZonesTop");
        AddUrielButton(top, "NyarZonesRefresh", "Refresh list", "Re-read the zones (.nyar api zones).",
            () => { NyarState.ClearError(); NyarProtocolService.RefreshZones(); }, 120);
        _nyarZonesListGo = AddNyarList(list, "NyarZonesList");
        AddNyarErrorLine(list);

        if (!NyarState.ZonesLoaded) NyarProtocolService.RefreshZones();
        RebuildNyarZones();
    }

    private void RebuildNyarZones()
    {
        if (_nyarZonesListGo == null) return;
        ClearChildren(_nyarZonesListGo);
        if (!NyarState.ZonesLoaded) { AddBodyText(_nyarZonesListGo, "Press Refresh list to load."); return; }
        if (NyarState.ZoneList.Count == 0) { AddBodyText(_nyarZonesListGo, "No defended zones yet."); return; }
        foreach (var z in NyarState.ZoneList)
        {
            var row = MakeUrielRow(_nyarZonesListGo, $"NyarZone_{z.Name}");
            string stateCol = z.State switch { "alert" => "#FF8080", "cooldown" => "#FFD75A", _ => "#90EE90" };
            AddNyarRowText(row,
                $"<b>{NyarText(z.Name)}</b>  <color={stateCol}>{z.State}</color>  <color=#888888>({z.X}, {z.Z}) r{z.Radius} · alert at {z.Threshold} · cd {NyarProtocolService.FormatSecs(z.Cooldown)}</color>");
            string name = z.Name;
            AddUrielConfirmButton(row, $"NyarZoneRemove_{name}", "Remove", $"Delete zone {NyarText(name)} (.nyar zone remove).",
                () => { NyarClient.ZoneRemove(name); NyarProtocolService.RefreshZones(); }, 90);
        }
    }

    // ---------------------------------------------------------------- ADMIN: ANNOUNCE

    private void BuildNyarAdminAnnounceTab(GameObject page)
    {
        EnsureNyarSubscribed();
        page = BeginAdminGate(page);
        AddNyarGateNotes(page);

        var card = AddCard(page, "NyarAnnounceCard");
        AddSectionHeading(card, "Broadcast now");
        AddBodyText(card,
            "Send a server-wide message (1–200 characters, no < or >). The server's reply shows in chat. " +
            "Scheduled and repeating messages aren't on the server yet — Raphael doesn't schedule them itself, " +
            "because they'd stop whenever you log out.");
        var row = MakeUrielRow(card, "NyarAnnounceRow");
        _nyarAnnounceInput = AddNyarInput(row, "NyarAnnounceText", "message…", 320);
        UIFactory.SetLayoutElement(_nyarAnnounceInput.GameObject, flexibleWidth: 1);
        var row2 = MakeUrielRow(card, "NyarAnnounceRow2");
        AddUrielButton(row2, "NyarAnnounceSend", "Announce", "Broadcast this message (.nyar announce \"…\").", () =>
        {
            string t = (_nyarAnnounceInput?.Text ?? "").Trim();
            if (t.Length == 0 || t.Length > 200 || t.IndexOf('<') >= 0 || t.IndexOf('>') >= 0 || t.Any(char.IsControl))
            { NyarState.SetError("announce", "Message must be 1–200 characters with no <, > or control characters."); return; }
            NyarState.ClearError();
            NyarClient.Announce(t);
            _nyarAnnounceInput.Text = "";
        }, 110);
        AddNyarErrorLine(card);

        AddSpacer(page, 6);
        var sw = AddCard(page, "NyarAnnSwitchesCard");
        AddSectionHeading(sw, "Announcement switches (read-only)");
        _nyarAnnSwitchesLabel = AddBodyText(sw, "", Theme.ScaledUI(12));
        AddBodyText(sw, "Change these in [Announcements] of the server's kdpen.Nyarlathotep.cfg.");
        RefreshNyarAnnSwitches();
    }

    private void RefreshNyarAnnSwitches()
    {
        if (_nyarAnnSwitchesLabel == null) return;
        _nyarAnnSwitchesLabel.text =
            $"Wave warnings {NyarOnOff(NyarState.AnnWarn)} · Event banners {NyarOnOff(NyarState.AnnBanner)} · " +
            $"Daily banner {NyarOnOff(NyarState.AnnDaily)} · Login stats {NyarOnOff(NyarState.AnnLogin)} · Player share {NyarOnOff(NyarState.AnnShare)}";
    }

    // ---------------------------------------------------------------- ADMIN: KILL SWITCH

    private void BuildNyarAdminKillSwitchTab(GameObject page)
    {
        EnsureNyarSubscribed();
        page = BeginAdminGate(page);
        AddNyarGateNotes(page);

        var card = AddCard(page, "NyarKillCard");
        AddSectionHeading(card, "Kill switch");
        AddBodyText(card,
            "Ends every running event and removes every Nyarlathotep unit, then pauses scheduled events for the " +
            "cooldown. <b>Purge…</b> asks you to click again to confirm.");
        _nyarKillLabel = AddInfoLabel(card, "NyarKillState", "", FontStyles.Normal, Theme.ScaledUI(12));
        var row = MakeUrielRow(card, "NyarKillRow");
        AddUrielConfirmButton(row, "NyarPurge", "Purge…", "End every event and despawn every unit now (purge, then purge confirm).",
            () => { NyarBeginAction(); NyarClient.Purge(); if (!NyarState.SupportsTwins) NyarProtocolService.RefreshStatus(); },
            130, new Color(0.55f, 0.15f, 0.15f, 1f));
        if (NyarState.SupportsTwins)
            AddUrielButton(row, "NyarKillRefresh", "Refresh", "Re-read the kill switch (.nyar api killswitch).",
                () => { NyarState.ClearError(); NyarProtocolService.RefreshKillswitch(); }, 90);
        AddNyarResultLine(card);
        AddNyarErrorLine(card);

        AddSpacer(page, 6);
        if (!NyarState.StatsOn) { if (NyarState.SupportsTwins) NyarProtocolService.RefreshKillswitch(); RefreshNyarCountdowns(); return; }
        var rs = AddCard(page, "NyarStatsResetCard");
        AddSectionHeading(rs, "Reset stats");
        AddBodyText(rs, $"Wipe one player's stats, or type {Mono("all")} for everyone. Step 1 asks, step 2 confirms.");
        var rr = MakeUrielRow(rs, "NyarResetRow");
        _nyarResetTargetInput = AddNyarInput(rr, "NyarResetTarget", "player or all", 150);
        AddUrielButton(rr, "NyarReset", "1. Reset…", "Ask the server to reset (.nyar stats reset <target>).", () =>
        {
            string t = Tok(_nyarResetTargetInput);
            if (t.Length == 0) { NyarState.SetError("stats reset", "Enter a player name or all."); return; }
            NyarClient.StatsReset(t);
        }, 100);
        AddUrielConfirmButton(rr, "NyarResetConfirm", "2. Confirm", "Reset now (.nyar stats reset <target> confirm).", () =>
        {
            string t = Tok(_nyarResetTargetInput);
            if (t.Length == 0) { NyarState.SetError("stats reset", "Enter a player name or all."); return; }
            NyarClient.StatsResetConfirm(t);
        }, 110, new Color(0.55f, 0.15f, 0.15f, 1f));

        if (NyarState.SupportsTwins) NyarProtocolService.RefreshKillswitch();
        RefreshNyarCountdowns();
    }

    // ---------------------------------------------------------------- GUIDE

    private void BuildNyarGuideTab(GameObject page)
    {
        AddGuideSection(page, "Nyarlathotep, Lord of Chaos",
            "A server mod that stages NPC world events the base game doesn't have: empowered factions, waves " +
            "that hunt players, swarms in an arena, hunting grounds bound to a region, faction revenge and a " +
            "map-wide apocalypse. Every pillar and event starts off; the server admin turns them on. " +
            "The Nyarlathotep tabs appear only on servers that run it.");
        AddCollapsibleHelpDetail(page, "How events work",
            "• Triggers: a schedule, nightfall or daybreak, a V Blood kill, a random interval, a player entering " +
            "a region, players killing a faction, faction revenge, or an admin command.\n" +
            "• Actions: empower a faction with timed buffs, or spawn waves that hunt players, roam a region or " +
            "hold an arena. A last wave can bring a boss.\n" +
            "• Revenge: kill too many of one faction and it sends squads after you. You get private warnings in chat first.\n" +
            "• Cleanup: when an event ends, its units leave and its buffs expire. Stuck units are moved back " +
            "into play, or removed after two tries.\n" +
            "• Planned, not on the server yet: boss reinforcements, defended zones, sieges and player stats.");
        AddCollapsibleHelpDetail(page, "Players",
            "• Live Events: what's running now, which regions it's in, and a countdown to the next wave. The " +
            "Regions list shows how many events are active in each part of the map.\n" +
            "• Notifications: a short notice at the top of the screen when an event begins or ends, a wave is " +
            "coming, or an admin uses the kill switch. On joining, one notice lists the events already under way. " +
            "Switch each kind on or off in Nyarlathotep → Settings. Wave warnings come only if the server sends " +
            "them. Scoreboards, revenge warnings and admin broadcasts still show in chat only.\n" +
            "• Leaderboards and My Stats: planned. The server doesn't collect stats yet, so these tabs say " +
            "stats are off. They fill in when Nyarlathotep's stats update ships.");
        AddCollapsibleHelpDetail(page, "Not seeing a reply?",
            "Nyarlathotep's replies to commands you send always show in chat, including errors like \"command not " +
            "found\" (`.nyar` alone lists the real commands). The tabs' Refresh buttons read data quietly and update " +
            "the tab instead of chat. On newer servers, admin buttons show their result on the tab itself. To see " +
            "everything while testing, open Settings and Help → Chat & Diagnostics and pick Show everything.");
        AddCollapsibleHelpDetail(page, "Admins",
            "Admin tabs are greyed until you run adminauth (F1 console) and press Re-check.\n" +
            "• Events: start, stop, enable or disable each definition. Unstick moves a running event's idle " +
            "units back into play (the server's unit watch must be on). Create a new event or copy one; new ones " +
            "start disabled. Delete one (click twice). Edit one field (pick it from the list), or set the location " +
            "to where you stand. Raphael can't read a field's current value yet, so Show in chat prints the full " +
            "definition. Units near me and Test spawn answer in chat.\n" +
            "• Templates: browse the built-in templates by pillar and create an event from one. It starts disabled.\n" +
            "• Pillars: switch whole families of events on or off. Off ends their running events.\n" +
            "• Zones: planned. Defended zones aren't on the server yet, so Add and Remove do nothing for now. " +
            "Zones can't be listed here either; remove is by name.\n" +
            "• Announce: broadcast a message now (1 to 200 characters, no angle brackets). The announcement " +
            "switches are read-only.\n" +
            "• Kill Switch: Purge (click twice) ends every event and removes every Nyarlathotep unit. Scheduled " +
            "events pause for the cooldown shown. Stats reset is for the planned stats update and does nothing yet.");
        AddCollapsibleHelpDetail(page, "Troubleshooting",
            "Tabs greyed out: the server didn't answer the handshake. Use Settings and Help → Connection → " +
            "Re-detect Nyarlathotep. Lists empty: the server's Nyarlathotep may be too old for live data (see " +
            "Nyarlathotep → Settings). Unstick needs Nyarlathotep 0.13.0 (api 8). A version like 0.13.0-rc.1 is a " +
            "test build. Reporting a bug: turn on diagnostic details in Nyarlathotep → Settings.");
    }

    // ---------------------------------------------------------------- RAIL DIAGNOSTIC

    private void BuildNyarDiagnosticPanel(GameObject parent)
    {
        var card = UIFactory.CreateVerticalGroup(parent, "NyarDiagnostic",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(6, 6, 6, 6), bgColor: Theme.CardBackground);
        UIFactory.SetLayoutElement(card, minWidth: 130, preferredWidth: 140, flexibleWidth: 1, minHeight: 0, flexibleHeight: 0);
        _nyarDiagnosticGo = card;

        var heading = UIFactory.CreateLabel(card, "NyarDiagHeading", "Nyarlathotep not detected",
            TextAlignmentOptions.MidlineLeft, color: Color.yellow, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(heading.GameObject, minWidth: 120, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        heading.TextMesh.fontStyle = FontStyles.Bold;
        heading.TextMesh.enableWordWrapping = true;

        var body = UIFactory.CreateLabel(card, "NyarDiagBody",
            "No reply to the Nyarlathotep handshake on this server.\n\n" +
            "Likely causes:\n" +
            "• Nyarlathotep isn't installed on this server.\n" +
            "• You just switched servers and it's still loading — click Re-check.",
            TextAlignmentOptions.TopLeft, color: Theme.MutedBody, fontSize: Theme.ScaledUI(10));
        UIFactory.SetLayoutElement(body.GameObject, minWidth: 120, preferredWidth: 130, flexibleWidth: 1, minHeight: 80, flexibleHeight: 1);
        body.TextMesh.enableWordWrapping = true;
        body.TextMesh.overflowMode = TextOverflowModes.Overflow;

        AddDiagnosticActionButton(card, "NyarDiagRecheckBtn", "Re-check now",
            "Restart Nyarlathotep detection and re-probe (.nyar api version).",
            () => { try { NyarProtocolService.Reset(); NyarClient.SendProbe(); } catch { } RefreshAllTabGroupAvailability(); });
        AddDiagnosticActionButton(card, "NyarDiagForceEnableBtn", "Force-enable tabs",
            "Flip NyarAvailability to On for this session so the tabs are navigable. Live data needs a real handshake; set NyarAvailability=On in kdpen.Raphael.cfg to persist.",
            () =>
            {
                try { Config.Settings.SetNyarAvailability(Config.Settings.ModAvailability.On); }
                catch (Exception ex) { LogUtils.LogError($"Nyar force-enable failed: {ex}"); }
                RefreshAllTabGroupAvailability();
            });
    }

    // ---------------------------------------------------------------- CONNECTION TAB

    private void RefreshNyarConnectionReadout()
    {
        if (_connNyarReadout == null) return;
        string s;
        if (NyarState.Present)
            s = $"<color=#90EE90>Connected</color> — api {NyarState.ApiVersion}, plugin {NyarState.PluginVersion}.";
        else if (NyarProtocolService.DetectionGaveUp)
            s = "<color=#FF8080>Not detected</color> — no Nyarlathotep response on this server. Re-detect to retry.";
        else
            s = "<color=#FFD75A>Detecting…</color> — handshake in progress.";
        _connNyarReadout.text = s;
    }
}
