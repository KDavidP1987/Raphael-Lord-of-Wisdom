using System;
using System.Collections.Generic;
using System.Globalization;
using Raphael.Services.Nyar;
using Raphael.Utils;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using TMPro;
using UnityEngine;

namespace Raphael.UI.ModContent;

// Nyarlathotep tab group (client UI for the admin-run world-event server mod). Built against the PLANNED
// api 1/2 shapes in the Nyarlathotep repo's docs/RAPHAEL_INTEGRATION_CONTRACT.md.
//
// Players: Live Events (api status + push stream, per-wave countdown), Leaderboards (api top + Share),
// My Stats (api me + hide/show). Admins: Events (defs + actions + field editor + test spawn), Zones,
// Announce, Kill Switch. Admin tabs are inline-gated (BeginAdminGate); the server re-checks every command.
// Actions send the HUMAN `.nyar …` commands (reply shows in chat) and re-read state — Raphael never parses
// those replies (contract §5).
public partial class MainPanel
{
    private bool _nyarSubscribed;
    private GameObject _nyarDiagnosticGo;          // inline "not detected" panel under the rail
    private TextMeshProUGUI _connNyarReadout;      // Connection tab — Nyarlathotep state line

    // Live UI handles (reassigned on every tab build; Unity-null once the page is torn down).
    private GameObject _nyarEventsListGo;
    private TextMeshProUGUI _nyarEventsStatus;
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
    private InputFieldRef _nyarSpawnUnitInput, _nyarSpawnCountInput, _nyarSpawnLevelInput, _nyarSpawnHpInput, _nyarSpawnPowerInput;
    private GameObject _nyarZonesListGo;
    private InputFieldRef _nyarZoneNameInput, _nyarZoneRadiusInput;
    private InputFieldRef _nyarAnnounceInput;
    private TextMeshProUGUI _nyarAnnSwitchesLabel;
    private TextMeshProUGUI _nyarKillLabel;
    private InputFieldRef _nyarResetTargetInput;
    private readonly List<TextMeshProUGUI> _nyarErrorLabels = new();

    private void EnsureNyarSubscribed()
    {
        if (_nyarSubscribed) return;
        NyarState.PresenceChanged += OnNyarPresenceChanged;
        NyarState.StatusChanged   += RebuildNyarEvents;
        NyarState.TopChanged      += RebuildNyarTop;
        NyarState.MeChanged       += RebuildNyarMe;
        NyarState.DefsChanged     += RebuildNyarDefs;
        NyarState.ZonesChanged    += RebuildNyarZones;
        NyarState.ErrorChanged    += RefreshNyarErrors;
        NyarProtocolService.AvailabilityChanged += OnNyarPresenceChanged;
        NyarProtocolService.CountdownTick += RefreshNyarCountdowns;
        _nyarSubscribed = true;
    }

    private void UnsubscribeNyar()
    {
        if (!_nyarSubscribed) return;
        NyarState.PresenceChanged -= OnNyarPresenceChanged;
        NyarState.StatusChanged   -= RebuildNyarEvents;
        NyarState.TopChanged      -= RebuildNyarTop;
        NyarState.MeChanged       -= RebuildNyarMe;
        NyarState.DefsChanged     -= RebuildNyarDefs;
        NyarState.ZonesChanged    -= RebuildNyarZones;
        NyarState.ErrorChanged    -= RefreshNyarErrors;
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
    // when the tab can't show live data (callers still build the action controls — they're human commands).
    private bool AddNyarGateNotes(GameObject parent, bool needStats = false, bool needZones = false)
    {
        string note = null;
        if (!NyarState.Present)
            note = "Nyarlathotep hasn't answered the handshake on this server yet.";
        else if (!NyarState.Enabled)
            note = "Nyarlathotep is switched off on this server (General.Enabled = false).";
        else if (!NyarState.SupportsReads)
            note = $"This server's Nyarlathotep (api {NyarState.ApiVersion}) doesn't send live data yet — it needs api 2. Buttons still send their commands.";
        else if (needStats && !NyarState.StatsOn)
            note = "Stats are switched off on this server, so there are no leaderboards or personal stats.";
        else if (needZones && !NyarState.Zones)
            note = "The defended-zones pillar is switched off on this server.";
        if (note == null) return false;
        var card = AddCard(parent, "NyarGateNote");
        AddBodyText(card, $"<color=#FFD75A>{note}</color>");
        return true;
    }

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

    private static string NyarWord(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);
    private static string NyarText(string s) => (s ?? "").Replace('_', ' ');
    private static string Tok(InputFieldRef f) => (f?.Text ?? "").Trim();

    // ---------------------------------------------------------------- PLAYER: LIVE EVENTS

    private void BuildNyarEventsTab(GameObject page)
    {
        EnsureNyarSubscribed();
        var intro = AddCard(page, "NyarEventsIntro");
        AddSectionHeading(intro, "Live events");
        AddBodyText(intro,
            "World events Nyarlathotep is running right now — raids, empowered enemies, boss reinforcements, " +
            "zone alerts and sieges. Updates live; wave countdowns tick down on their own.");
        AddNyarGateNotes(page);

        var card = AddCard(page, "NyarEventsCard");
        var row = MakeUrielRow(card, "NyarEventsRow");
        AddUrielButton(row, "NyarEventsRefresh", "Refresh", "Re-read the active events (.nyar api status).",
            () => { NyarState.ClearError(); NyarProtocolService.RefreshStatus(); });
        _nyarEventsStatus = AddNyarRowText(row, "");
        _nyarEventsListGo = UIFactory.CreateVerticalGroup(card, "NyarEventsList", forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 2, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_nyarEventsListGo, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 20, flexibleHeight: 0);
        AddNyarErrorLine(card);

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
            AddInfoLabel(box, "NyarEvTitle",
                $"<b>{NyarText(e.Name)}</b>  <color=#888888>{NyarWord(e.Kind)} · {e.State}{faction}</color>",
                FontStyles.Normal, Theme.ScaledUI(13));
            var cd = AddInfoLabel(box, "NyarEvCountdown", "", FontStyles.Normal, Theme.ScaledUI(12));
            _nyarCountdownLabels.Add((cd, e));
        }
        RefreshNyarCountdowns();
    }

    private static string NyarCountdownText(NyarEvent e)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(e.Wave)) parts.Add($"Wave {e.Wave}");
        if (NyarState.TryGetWaveWarn(e.Id, out var nextWave, out var warnSecs))
            parts.Add($"<color=#FFD75A>wave {nextWave} in {NyarProtocolService.FormatSecs(warnSecs)}</color>");
        int left = e.SecondsLeftNow;
        if (left >= 0) parts.Add($"{NyarProtocolService.FormatSecs(left)} left");
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
            _nyarKillLabel.text = !NyarState.Killswitch
                ? "Kill switch: <color=#90EE90>off</color> — scheduled events can start."
                : ks > 0 ? $"Kill switch: <color=#FF8080>ON</color> — scheduled starts suppressed for {NyarProtocolService.FormatSecs(ks)}."
                         : "Kill switch: <color=#FF8080>ON</color> — scheduled starts suppressed.";
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
        AddNyarGateNotes(page, needStats: true);

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
        AddNyarGateNotes(page, needStats: true);

        var card = AddCard(page, "NyarMeCard");
        var row = MakeUrielRow(card, "NyarMeRow");
        AddUrielButton(row, "NyarMeRefresh", "Refresh", "Re-read your stats (.nyar api me).",
            () => { NyarState.ClearError(); NyarProtocolService.RefreshMe(); }, 100);
        AddUrielButton(row, "NyarMeHide", "Hide me", "Leave the public leaderboards (.nyar stats hide).",
            () => { NyarClient.StatsHide(); NyarProtocolService.RefreshMe(); }, 100);
        AddUrielButton(row, "NyarMeShow", "Show me", "Appear on the leaderboards again (.nyar stats show).",
            () => { NyarClient.StatsShow(); NyarProtocolService.RefreshMe(); }, 100);
        _nyarMeListGo = UIFactory.CreateVerticalGroup(card, "NyarMeList", forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 2, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_nyarMeListGo, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 20, flexibleHeight: 0);
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
        AddUrielButton(top, "NyarDefsReload", "Reload from disk", "Re-read the event JSON files on the server (.nyar event reload).",
            () => { NyarClient.EventReload(); NyarProtocolService.RefreshDefs(); }, 140);
        _nyarDefsListGo = UIFactory.CreateVerticalGroup(list, "NyarDefsList", forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 2, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_nyarDefsListGo, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 20, flexibleHeight: 0);
        AddNyarErrorLine(list);

        AddSpacer(page, 6);
        var edit = AddCard(page, "NyarDefEditCard");
        AddSectionHeading(edit, "Edit one field");
        AddBodyText(edit, $"Sends {Mono(".nyar event set <id> <field> <value>")}. The server validates the value; a bad one disables the event and shows the reason in the list.");
        var er = MakeUrielRow(edit, "NyarDefEditRow");
        _nyarDefIdInput    = AddNyarInput(er, "NyarDefId", "event id", 120);
        _nyarDefFieldInput = AddNyarInput(er, "NyarDefField", "field", 120);
        _nyarDefValueInput = AddNyarInput(er, "NyarDefValue", "value", 140);
        AddUrielButton(er, "NyarDefSet", "Set", "Apply this field change.", () =>
        {
            string id = Tok(_nyarDefIdInput), field = Tok(_nyarDefFieldInput), val = Tok(_nyarDefValueInput);
            if (id.Length == 0 || field.Length == 0 || val.Length == 0) { NyarState.SetError("event set", "Fill in the event id, field and value."); return; }
            NyarClient.EventSet(id, field, val);
            NyarProtocolService.RefreshDefs();
        }, 70);

        AddSpacer(page, 6);
        var spawn = AddCard(page, "NyarSpawnCard");
        AddSectionHeading(spawn, "Test spawn");
        AddBodyText(spawn, $"Spawns next to you: {Mono(".nyar spawn <unit> [count] [level] [hp×] [power×]")}. Leave optional fields blank for defaults.");
        var sr = MakeUrielRow(spawn, "NyarSpawnRow");
        _nyarSpawnUnitInput  = AddNyarInput(sr, "NyarSpawnUnit", "unit (name or GUID)", 170);
        _nyarSpawnCountInput = AddNyarInput(sr, "NyarSpawnCount", "count", 60);
        _nyarSpawnLevelInput = AddNyarInput(sr, "NyarSpawnLevel", "level", 60);
        _nyarSpawnHpInput    = AddNyarInput(sr, "NyarSpawnHp", "hp×", 55);
        _nyarSpawnPowerInput = AddNyarInput(sr, "NyarSpawnPower", "power×", 60);
        var sr2 = MakeUrielRow(spawn, "NyarSpawnRow2");
        AddUrielButton(sr2, "NyarSpawnGo", "Spawn", "Send the test spawn.", () =>
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
        }, 100);

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
                $"<b>{NyarText(d.Name)}</b> <color=#888888>({d.Id})</color>  <color={stateCol}>{d.State}</color>  <color=#888888>{d.Trigger} → {d.Action}{dur}</color>",
                FontStyles.Normal, Theme.ScaledUI(12));
            if (d.State == "disabled" && !string.IsNullOrEmpty(d.Reason))
                AddBodyText(box, $"<color=#FF8080>Disabled: {NyarText(d.Reason)}</color>", Theme.ScaledUI(11));
            var row = MakeUrielRow(box, "NyarDefBtns");
            string id = d.Id;
            AddUrielButton(row, "NyarDefStart", "Start", $"Start {id} now (.nyar event start {id}).",
                () => { NyarClient.EventStart(id); NyarProtocolService.RefreshStatus(); NyarProtocolService.RefreshDefs(); }, 70);
            AddUrielButton(row, "NyarDefStop", "Stop", $"Stop {id} (.nyar event stop {id}).",
                () => { NyarClient.EventStop(id); NyarProtocolService.RefreshStatus(); NyarProtocolService.RefreshDefs(); }, 70);
            if (d.Enabled)
                AddUrielButton(row, "NyarDefDisable", "Disable", $"Stop {id} from being scheduled (.nyar event disable {id}).",
                    () => { NyarClient.EventDisable(id); NyarProtocolService.RefreshDefs(); }, 80);
            else
                AddUrielButton(row, "NyarDefEnable", "Enable", $"Allow {id} to be scheduled (.nyar event enable {id}).",
                    () => { NyarClient.EventEnable(id); NyarProtocolService.RefreshDefs(); }, 80);
            AddUrielButton(row, "NyarDefEdit", "Edit…", "Put this id in the field editor below.",
                () => { if (_nyarDefIdInput != null) _nyarDefIdInput.Text = id; }, 70);
        }
    }

    // ---------------------------------------------------------------- ADMIN: ZONES

    private void BuildNyarAdminZonesTab(GameObject page)
    {
        EnsureNyarSubscribed();
        page = BeginAdminGate(page);
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
        _nyarZonesListGo = UIFactory.CreateVerticalGroup(list, "NyarZonesList", forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 2, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_nyarZonesListGo, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 20, flexibleHeight: 0);
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
        AddBodyText(card, "Send a server-wide message (1–200 characters, no < or >), or the stats digest.");
        var row = MakeUrielRow(card, "NyarAnnounceRow");
        _nyarAnnounceInput = AddNyarInput(row, "NyarAnnounceText", "message…", 320);
        UIFactory.SetLayoutElement(_nyarAnnounceInput.GameObject, flexibleWidth: 1);
        var row2 = MakeUrielRow(card, "NyarAnnounceRow2");
        AddUrielButton(row2, "NyarAnnounceSend", "Announce", "Broadcast this message (.nyar announce).", () =>
        {
            string t = (_nyarAnnounceInput?.Text ?? "").Trim();
            if (t.Length == 0 || t.Length > 200 || t.IndexOf('<') >= 0 || t.IndexOf('>') >= 0)
            { NyarState.SetError("announce", "Message must be 1–200 characters with no < or >."); return; }
            NyarClient.Announce(t);
            _nyarAnnounceInput.Text = "";
        }, 110);
        AddUrielButton(row2, "NyarAnnounceDigest", "Send digest", "Broadcast the stats digest now (.nyar announce digest).",
            NyarClient.AnnounceDigest, 120);
        AddNyarErrorLine(card);

        AddSpacer(page, 6);
        var sw = AddCard(page, "NyarAnnSwitchesCard");
        AddSectionHeading(sw, "Announcement switches (read-only)");
        _nyarAnnSwitchesLabel = AddBodyText(sw, "", Theme.ScaledUI(12));
        AddBodyText(sw, "Change these in the server's kdpen.Nyarlathotep.cfg.");
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
            $"cooldown. Step 1 sends {Mono(".nyar purge")} (the server explains what will happen); step 2 sends " +
            $"{Mono(".nyar purge confirm")}.");
        _nyarKillLabel = AddInfoLabel(card, "NyarKillState", "", FontStyles.Normal, Theme.ScaledUI(12));
        var row = MakeUrielRow(card, "NyarKillRow");
        AddUrielButton(row, "NyarPurge", "1. Purge…", "Ask the server to purge (.nyar purge). Nothing happens until step 2.", NyarClient.Purge, 110);
        AddUrielConfirmButton(row, "NyarPurgeConfirm", "2. Confirm purge", "Purge now (.nyar purge confirm).",
            () => { NyarClient.PurgeConfirm(); NyarProtocolService.RefreshStatus(); }, 150, new Color(0.55f, 0.15f, 0.15f, 1f));
        AddNyarErrorLine(card);

        AddSpacer(page, 6);
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

        RefreshNyarCountdowns();
    }

    // ---------------------------------------------------------------- GUIDE

    private void BuildNyarGuideTab(GameObject page)
    {
        AddGuideSection(page, "Nyarlathotep, Lord of Chaos",
            "A server mod that runs world events: raids that arrive in waves, empowered enemies, boss " +
            "reinforcements, defended zones and clan sieges. The server admin decides which of these are on. " +
            "The Nyarlathotep tabs appear only on servers that run it.");
        AddCollapsibleHelpDetail(page, "Players",
            "• Live Events — what's running now, with a countdown to the next wave.\n" +
            "• Leaderboards — pick a stat (kills, events, waves, defences, boss adds, deaths) and a window " +
            "(today, week, all time). Your row is highlighted. Share my line posts it to chat if the server allows.\n" +
            "• My Stats — your numbers. Hide me takes you off the boards; Show me puts you back.");
        AddCollapsibleHelpDetail(page, "Not seeing a reply?",
            "Nyarlathotep's replies to commands you send always show in chat, including errors like \"command not " +
            "found\" (`.nyar` alone lists the real commands). The tabs' Refresh buttons read data quietly and update " +
            "the tab instead of chat. To see everything while testing, open Settings and Help → Chat & Diagnostics " +
            "and pick Show everything.");
        AddCollapsibleHelpDetail(page, "Admins",
            "Admin tabs are greyed until you run adminauth (F1 console) and press Re-check.\n" +
            "• Events — start, stop, enable or disable each definition, reload them from disk, edit one field, " +
            "and test-spawn units next to you.\n" +
            "• Zones — add a defended zone where you stand, or remove one.\n" +
            "• Announce — broadcast a message or the stats digest. The announcement switches are read-only.\n" +
            "• Kill Switch — two steps: Purge, then Confirm. Scheduled events pause for the cooldown shown. " +
            "Stats reset works the same way.");
        AddCollapsibleHelpDetail(page, "Troubleshooting",
            "Tabs greyed out: the server didn't answer the handshake. Use Settings and Help → Connection → " +
            "Re-detect Nyarlathotep. Lists empty: the server's Nyarlathotep may be too old for live data (see " +
            "Nyarlathotep → Settings). Reporting a bug: turn on diagnostic details in Nyarlathotep → Settings.");
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
            () => { try { NyarProtocolService.Reset(); NyarClient.RequestVersion(); } catch { } RefreshAllTabGroupAvailability(); });
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
