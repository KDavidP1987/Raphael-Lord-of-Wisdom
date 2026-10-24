using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Raphael.Services.Uriel;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using TMPro;
using UnityEngine;

namespace Raphael.UI.ModContent;

// 0.70: Uriel Castle Events (Uriel 0.21.0, alpha, off by default) — Uriel/Uriel/docs/RAPHAEL_CASTLE_EVENTS_HANDOFF.md.
// Three tabs: Castle Events (layout builder §6A + run panel §6C + live run HUD), Event Library (squads / floor
// templates / templates §6B) and Admin: Units (§6D). Uriel has no [URIEL:*] shape for any of this yet (§7 is a
// proposal), so every button sends the human command through UrielClient.SendEvent and UrielEvents reads the
// replies and run broadcasts back out of chat. Commands are checked against the ~120-char chat cut-off first.
public partial class MainPanel
{
    private bool _urielEvSubscribed;
    private Action _urielEvTicker;
    private float _urielEvNextTick;

    // Live labels (several tabs can show the reply echo at once).
    private TextMeshProUGUI _urielEvAvailLabel;
    private TextMeshProUGUI _urielEvHudLabel;
    private TextMeshProUGUI _urielEvLogLabel;
    private readonly List<TextMeshProUGUI> _urielEvReplyLabels = new();

    private static readonly Regex UrielEvNameRx = new(@"^[A-Za-z0-9_-]{1,24}$", RegexOptions.Compiled);

    private void EnsureUrielEventsSubscribed()
    {
        EnsureUrielSubscribed();
        if (_urielEvSubscribed) return;
        UrielEvents.Changed += RefreshUrielEventsLabels;
        // The HUD clock and time-left count down between broadcasts, so tick the label twice a second.
        _urielEvTicker = () =>
        {
            if (Time.realtimeSinceStartup < _urielEvNextTick) return;
            _urielEvNextTick = Time.realtimeSinceStartup + 0.5f;
            if (_urielEvHudLabel != null && UrielEvents.RunActive) _urielEvHudLabel.text = UrielEvents.HudText();
        };
        Behaviors.CoreUpdateBehavior.Actions.Add(_urielEvTicker);
        _urielEvSubscribed = true;
    }

    private void UnsubscribeUrielEvents()
    {
        if (!_urielEvSubscribed) return;
        UrielEvents.Changed -= RefreshUrielEventsLabels;
        if (_urielEvTicker != null) Behaviors.CoreUpdateBehavior.Actions.Remove(_urielEvTicker);
        _urielEvTicker = null;
        _urielEvReplyLabels.Clear();
        _urielEvSubscribed = false;
    }

    private void RefreshUrielEventsLabels()
    {
        if (_urielEvAvailLabel != null) _urielEvAvailLabel.text = UrielEventsAvailText();
        if (_urielEvHudLabel != null) _urielEvHudLabel.text = UrielEvents.HudText();
        if (_urielEvLogLabel != null)
            _urielEvLogLabel.text = UrielEvents.RunLog.Count == 0
                ? $"<color={Theme.MutedBodyHex}>Run messages appear here as they arrive.</color>"
                : string.Join("\n", UrielEvents.RunLog);
        string reply = UrielEventsReplyText();
        for (int i = _urielEvReplyLabels.Count - 1; i >= 0; i--)
        {
            if (_urielEvReplyLabels[i] == null) { _urielEvReplyLabels.RemoveAt(i); continue; }
            _urielEvReplyLabels[i].text = reply;
        }
    }

    private static string UrielEventsAvailText() => UrielEvents.State switch
    {
        UrielEvents.Availability.Disabled  => "<color=#FF8080>Castle Events are turned off on this server</color> (CastleEvents.Enabled). The buttons below will be refused.",
        UrielEvents.Availability.AdminOnly => "<color=#FFB070>Castle Events are admin-only on this server</color> (CastleEvents.PlayerEvents). Players can't use these tabs.",
        UrielEvents.Availability.Available => "<color=#90EE90>Castle Events are available.</color>",
        _ => UrielEvents.ProbeSent
            ? "<color=#FFD75A>Checking whether Castle Events are on…</color>"
            : "<color=#A0A0A0>Not checked yet.</color> Press <b>Check</b>.",
    };

    private static string UrielEventsReplyText()
    {
        if (string.IsNullOrEmpty(UrielEvents.LastCommand) && UrielEvents.LastReply.Count == 0)
            return $"<color={Theme.MutedBodyHex}>The server's answer to your last Castle Events button shows here.</color>";
        var sb = new System.Text.StringBuilder();
        sb.Append(UrielEvents.LastCommand.Length > 0
            ? $"<color={Theme.MutedBodyHex}>&gt; {Esc(UrielEvents.LastCommand)}</color>"
            : $"<color={Theme.MutedBodyHex}>(not sent)</color>");
        if (UrielEvents.LastReply.Count == 0) sb.Append("\n<color=#A0A0A0>(waiting for a reply…)</color>");
        foreach (var l in UrielEvents.LastReply) sb.Append('\n').Append(Esc(l));
        return sb.ToString();
    }

    // Replies quote user text ('<group>') — keep TMP from reading angle brackets in them as tags.
    private static string Esc(string s) => (s ?? "").Replace("<", "&lt;").Replace(">", "&gt;");

    private void AddUrielEventsReplyCard(GameObject page, string name)
    {
        var card = AddCard(page, name);
        AddSectionHeading(card, "Server reply");
        var lbl = AddBodyText(card, UrielEventsReplyText(), Theme.ScaledUI(12));
        _urielEvReplyLabels.Add(lbl);
    }

    // ---- small helpers ----

    private static string EvText(InputFieldRef f) => (f?.Text ?? "").Trim();

    private static bool EvName(InputFieldRef f, string what, out string name)
    {
        name = EvText(f);
        if (UrielEvNameRx.IsMatch(name)) return true;
        UrielEvents.NoteRefusedLocally("", $"Enter a {what} name: 1-24 letters, digits, '-' or '_'.");
        return false;
    }

    private static bool EvInt(InputFieldRef f, int min, int max, out int n)
    {
        n = 0;
        var t = EvText(f);
        return t.Length > 0 && int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= min && n <= max;
    }

    // Comma list of words (unit names / squad names): spaces removed so it stays one chat argument.
    private static string EvList(InputFieldRef f) => Regex.Replace(EvText(f), @"\s+", "");

    private static void EvSend(string cmd) => UrielClient.SendEvent(cmd);

    private static void EvRefuse(string why) => UrielEvents.NoteRefusedLocally("", why);

    private ButtonRef AddUrielCycleButton(GameObject row, string name, string[] values, string[] labels, Func<int> get, Action<int> set, string tooltip, int width)
    {
        ButtonRef btn = null;
        btn = AddUrielButton(row, name, labels[get()], tooltip, () =>
        {
            int next = (get() + 1) % values.Length;
            set(next);
            SetUrielButtonText(btn, labels[next]);
        }, width);
        return btn;
    }

    // Unit picker (Uriel's object catalog has no units — handoff §6B). 0.71.0: searches the shared game unit list
    // (Resources/UnitCatalog). Sent without the CHAR_ prefix: any unique ending works, and an ambiguous one is
    // refused with the matches, never guessed.
    private static string ShortUnit(string full) => full.StartsWith("CHAR_", StringComparison.Ordinal) ? full.Substring(5) : full;

    private List<(string Display, string Value)> UrielEvUnitMatches(string typed)
    {
        var outl = new List<(string, string)>();
        foreach (var (disp, full) in Raphael.Resources.UnitCatalog.Search(typed, 30)) outl.Add((disp, ShortUnit(full)));
        return outl;
    }

    // Containment picker (handoff §2): server default / plot / room / storey / leash N m / room + leash.
    private static readonly string[] UrielEvContainValues = { "", "plot", "room", "storey", "leash", "roomleash" };
    private static readonly string[] UrielEvContainLabels = { "Contain: server default", "Contain: plot", "Contain: room", "Contain: storey", "Contain: leash (m)", "Contain: room + leash" };

    private static bool EvContain(int mode, InputFieldRef metres, out string contain)
    {
        contain = "";
        string v = UrielEvContainValues[mode];
        if (v is "leash" or "roomleash")
        {
            if (!EvInt(metres, 3, 60, out int m)) { EvRefuse("A leash is 3-60 metres: fill in the Leash field."); return false; }
            contain = v == "leash" ? m.ToString(CultureInfo.InvariantCulture) : $"room,{m.ToString(CultureInfo.InvariantCulture)}";
            return true;
        }
        contain = v;
        return true;
    }

    // Positional args with skippable middles: trailing defaults are dropped, inner ones stay as their default.
    private static string EvArgs(params (string Value, string Default)[] args)
    {
        int last = -1;
        for (int i = 0; i < args.Length; i++)
            if (!string.IsNullOrEmpty(args[i].Value) && args[i].Value != args[i].Default) last = i;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i <= last; i++)
            sb.Append(' ').Append(string.IsNullOrEmpty(args[i].Value) ? args[i].Default : args[i].Value);
        return sb.ToString();
    }

    // ============================ PLAYER: CASTLE EVENTS ============================

    // Layout builder state.
    private InputFieldRef _urielEvGroup;
    private GameObject _urielEvRecentRow;
    private readonly List<string> _urielEvRecentGroups = new();
    private string _urielEvMarkerUnit = "";
    private InputFieldRef _urielEvMarkerCount;
    private InputFieldRef _urielEvMarkerLevel;
    private InputFieldRef _urielEvMarkerLeash;
    private int _urielEvMarkerContain;
    private InputFieldRef _urielEvGroupSquad;
    private InputFieldRef _urielEvMarkerId;

    // Run panel state.
    private InputFieldRef _urielEvTemplate;
    private int _urielEvPreset;          // index into UrielEvPresets
    private int _urielEvLevelMode;       // own / set / shift / auto
    private InputFieldRef _urielEvLevel;
    private InputFieldRef _urielEvHp, _urielEvDmg, _urielEvSpeed, _urielEvAspd;
    private InputFieldRef _urielEvLimit;
    private bool _urielEvKeepFactions;
    private TextMeshProUGUI _urielEvOptionsPreview;

    private static readonly string[] UrielEvPresets = { "", "easy", "normal", "hard", "brutal" };
    private static readonly string[] UrielEvPresetLabels = { "Difficulty: unscaled", "Difficulty: easy", "Difficulty: normal", "Difficulty: hard", "Difficulty: brutal" };
    private static readonly string[] UrielEvLevelModes = { "own", "set", "shift", "auto" };
    private static readonly string[] UrielEvLevelLabels = { "Level: units' own", "Level: set to N", "Level: shift by ±N", "Level: auto (+N)" };

    private void BuildUrielEventsTab(GameObject page)
    {
        EnsureUrielEventsSubscribed();

        var intro = AddCard(page, "UrielEvIntro");
        AddSectionHeading(intro, "Castle Events (alpha)");
        AddBodyText(intro,
            "Turn a castle into an event space: drop <b>spawn markers</b> while you walk it, bind <b>doors</b> to " +
            "groups of markers, then <b>start a run</b>. Mobs and bosses spawn at the markers, each group's door " +
            "shuts until that group is defeated, and a clock times the run. Everything acts on the castle plot " +
            "you're <b>standing in</b> (your own castle; admins any). What spawns comes from the <b>Event Library</b> tab.");
        _urielEvAvailLabel = AddBodyText(intro, UrielEventsAvailText(), Theme.ScaledUI(12));
        var aRow = MakeUrielRow(intro, "UrielEvAvailRow");
        AddUrielButton(aRow, "UrielEvProbe", "Check",
            "Ask the server whether Castle Events are on for you (.uriel event list — the layout of the plot you're on also shows in chat).",
            () => UrielClient.ProbeEvents(), 90);
        AddUrielBoolToggle(intro, "UrielEvToasts", "On-screen notices during a run (begun, groups, bosses, complete, time)",
            Config.Settings.UrielEventToasts, v => Config.Settings.SetUrielEventToasts(v),
            "Short toasts from the run broadcasts. Default on.");

        // --- Run ---
        var run = AddCard(page, "UrielEvRun");
        AddSectionHeading(run, "Run");
        _urielEvHudLabel = AddBodyText(run, UrielEvents.HudText(), Theme.ScaledUI(13));
        var rRow = MakeUrielRow(run, "UrielEvRunRow");
        UrielGatesBegin(Data.PanelType.UrielEventsTab);
        UrielGate(Data.PanelType.UrielEventsTab, UrielFeature.Events,
            AddUrielButton(rRow, "UrielEvStart", "Start", "Start a run on this plot with the template and options below (.uriel event start …).",
                StartUrielEvent, 90));
        AddUrielConfirmButton(rRow, "UrielEvStop", "Stop",
            "Stop the run: its units are removed and every door goes back as it was (.uriel event stop).",
            () => EvSend(".uriel event stop"), 80, UrielDanger);
        AddUrielButton(rRow, "UrielEvStatus", "Status", "Show the run on this plot: time, groups cleared, bosses left (.uriel event status).",
            () => EvSend(".uriel event status"), 90);
        AddUrielButton(rRow, "UrielEvHistory", "History", "The last runs on this plot and the best times (.uriel event history).",
            () => EvSend(".uriel event history"), 90);

        _urielEvTemplate = AddUrielLabeledInput(run, "UrielEvTemplate", "Template, floor template or squad", "blank = each group's own");
        var lRow = MakeUrielRow(run, "UrielEvListRow");
        AddUrielButton(lRow, "UrielEvListTpl", "List templates", "List the templates you can use (.uriel template list).",
            () => EvSend(".uriel template list"), 130);
        AddUrielButton(lRow, "UrielEvListFloor", "Floor templates", "List the floor templates you can use (.uriel floor list).",
            () => EvSend(".uriel floor list"), 130);
        AddUrielButton(lRow, "UrielEvListSquad", "Squads", "List the squads you can use (.uriel squad list).",
            () => EvSend(".uriel squad list"), 90);

        // --- Run options ---
        AddSectionHeading(run, "▸ Run options");
        AddBodyText(run, "Leave anything blank to skip it. A template's own default options apply first, then these.");
        var pRow = MakeUrielRow(run, "UrielEvPresetRow");
        AddUrielCycleButton(pRow, "UrielEvPreset", UrielEvPresets, UrielEvPresetLabels, () => _urielEvPreset,
            i => { _urielEvPreset = i; RefreshUrielEvOptionsPreview(); },
            "easy: hp and damage ×0.75 · hard: hp ×1.5, damage ×1.25 · brutal: hp ×2.5, damage ×1.5, speed ×1.1, attack speed ×1.15.", 180);
        AddUrielCycleButton(pRow, "UrielEvLevelMode", UrielEvLevelModes, UrielEvLevelLabels, () => _urielEvLevelMode,
            i => { _urielEvLevelMode = i; RefreshUrielEvOptionsPreview(); },
            "Set every unit's level, shift each unit's own level, or use the highest player level on the plot at start (+N).", 170);
        _urielEvLevel = AddUrielLabeledInput(run, "UrielEvLevel", "Level N (set) / ±N (shift) / +N (auto)", "e.g. 40 or -5");
        _urielEvHp    = AddUrielLabeledInput(run, "UrielEvHp",    "Health ×",       "0.1-10");
        _urielEvDmg   = AddUrielLabeledInput(run, "UrielEvDmg",   "Damage ×",       "0.1-10");
        _urielEvSpeed = AddUrielLabeledInput(run, "UrielEvSpeed", "Move speed ×",   "0.1-10");
        _urielEvAspd  = AddUrielLabeledInput(run, "UrielEvAspd",  "Attack speed ×", "0.1-10");
        _urielEvLimit = AddUrielLabeledInput(run, "UrielEvLimit", "Time limit",     "e.g. 15m, 90s, 1h");
        AddUrielBoolToggle(run, "UrielEvKeepFactions", "Keep each unit's own faction (default: one faction for the run)",
            false, v => { _urielEvKeepFactions = v; RefreshUrielEvOptionsPreview(); },
            "Off: every unit is forced onto one faction so they don't fight each other (faction=one). On: faction=keep.");
        foreach (var f in new[] { _urielEvLevel, _urielEvHp, _urielEvDmg, _urielEvSpeed, _urielEvAspd, _urielEvLimit, _urielEvTemplate })
            f.OnValueChanged += _ => RefreshUrielEvOptionsPreview();
        _urielEvOptionsPreview = AddBodyText(run, "", Theme.ScaledUI(12));
        RefreshUrielEvOptionsPreview();

        AddUrielEventsReplyCard(page, "UrielEvReplyCard");

        // --- Layout builder ---
        var lay = AddCard(page, "UrielEvLayout");
        AddSectionHeading(lay, "Layout builder");
        AddBodyText(lay,
            "Walk the castle. Type a <b>group</b> name, stand where a unit should appear and press <b>Mob marker " +
            "here</b> or <b>Boss marker here</b> — the marker goes at your feet, and the group's floor is the floor " +
            "you're on. Stand next to the door that group should hold shut and press <b>Bind nearest door</b> " +
            "(within 6 m). Layout edits are refused while a run is going.");
        _urielEvGroup = AddUrielLabeledInput(lay, "UrielEvGroup", "Group", "e.g. g1, hall, boss_room");
        _urielEvRecentRow = MakeUrielRow(lay, "UrielEvRecentRow");
        RebuildUrielEvRecentGroups();

        var mRow = MakeUrielRow(lay, "UrielEvMarkerRow");
        AddUrielButton(mRow, "UrielEvMobMarker", "Mob marker here", "Drop a mob marker at your feet in this group (.uriel event marker <group> mob …).",
            () => SendUrielEvMarker("mob"), 150);
        AddUrielButton(mRow, "UrielEvBossMarker", "Boss marker here", "Drop a boss marker at your feet in this group (.uriel event marker <group> boss …).",
            () => SendUrielEvMarker("boss"), 150);
        var dRow = MakeUrielRow(lay, "UrielEvDoorRow");
        // 0.73.0: only binding is gated; Unbind stays usable as cleanup (Uriel 0.22.0 still runs undoor).
        UrielGate(Data.PanelType.UrielEventsTab, UrielFeature.Doors,
            AddUrielButton(dRow, "UrielEvDoor", "Bind nearest door", "This group holds the castle door nearest you (within 6 m): shut while its units live (.uriel event door <group>).",
                () => { if (TakeUrielEvGroup(out var g)) EvSend($".uriel event door {g}"); }, 150));
        AddUrielButton(dRow, "UrielEvUndoor", "Unbind door", "The group stops holding its door (.uriel event undoor <group>).",
            () => { if (TakeUrielEvGroup(out var g)) EvSend($".uriel event undoor {g}"); }, 120);
        var sRow = MakeUrielRow(lay, "UrielEvShowRow");
        AddUrielButton(sRow, "UrielEvList", "Show layout", "List this plot's groups, doors and markers (.uriel event list).",
            () => EvSend(".uriel event list"), 120);
        AddUrielConfirmButton(sRow, "UrielEvRemoveGroup", "Remove group",
            "Remove the group and all its markers; marker costs are refunded (.uriel event remove <group>).",
            () => { if (TakeUrielEvGroup(out var g)) EvSend($".uriel event remove {g}"); }, 130, UrielDanger);

        AddSectionHeading(lay, "▸ Marker options (optional)");
        AddBodyText(lay, "Blank unit = the unit comes from the group's squad. Count and level 0 = the default.");
        AddBeelzSearchableField(lay, "UrielEvMarkerUnit", "Unit name or GUID (blank = from squad)…", "", UrielEvUnitMatches, v => _urielEvMarkerUnit = v);
        _urielEvMarkerCount = AddUrielLabeledInput(lay, "UrielEvMarkerCount", "Count", "blank = 1");
        _urielEvMarkerLevel = AddUrielLabeledInput(lay, "UrielEvMarkerLevel", "Level", "blank = unit's own");
        var cRow = MakeUrielRow(lay, "UrielEvContainRow");
        AddUrielCycleButton(cRow, "UrielEvMarkerContain", UrielEvContainValues, UrielEvContainLabels, () => _urielEvMarkerContain,
            i => _urielEvMarkerContain = i,
            "Where this marker's units must stay: the plot always; also the room under the marker, the storey, or a leash radius. Server default is usually storey.", 200);
        _urielEvMarkerLeash = AddUrielLabeledInput(lay, "UrielEvMarkerLeash", "Leash (metres, 3-60)", "e.g. 15");

        AddSectionHeading(lay, "▸ Group squad & markers");
        _urielEvGroupSquad = AddUrielLabeledInput(lay, "UrielEvGroupSquad", "Squad for this group", "squad name");
        var gsRow = MakeUrielRow(lay, "UrielEvGroupSquadRow");
        AddUrielButton(gsRow, "UrielEvSetSquad", "Set group squad", "The group spawns this squad unless the run's template says otherwise (.uriel event squad <group> <squad>).",
            () => { if (TakeUrielEvGroup(out var g) && EvName(_urielEvGroupSquad, "squad", out var s)) EvSend($".uriel event squad {g} {s}"); }, 150);
        AddUrielButton(gsRow, "UrielEvClearSquad", "Clear group squad", "The group has no squad of its own (.uriel event squad <group> -).",
            () => { if (TakeUrielEvGroup(out var g)) EvSend($".uriel event squad {g} -"); }, 150);
        _urielEvMarkerId = AddUrielLabeledInput(lay, "UrielEvMarkerId", "Marker #", "id from the reply");
        var rmRow = MakeUrielRow(lay, "UrielEvRemoveMarkerRow");
        AddUrielButton(rmRow, "UrielEvRemoveMarker", "Remove marker", "Remove one marker by its #id; its cost is refunded (.uriel event remove #<id>).",
            () =>
            {
                var t = EvText(_urielEvMarkerId).TrimStart('#');
                if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id > 0) EvSend($".uriel event remove #{id}");
                else EvRefuse("Enter the marker's number (the #id from the marker reply or Show layout).");
            }, 140);

        // --- Run log ---
        var log = AddCard(page, "UrielEvLog");
        AddSectionHeading(log, "Run messages");
        _urielEvLogLabel = AddBodyText(log, "", Theme.ScaledUI(12));

        RefreshUrielEventsLabels();
        if (UrielEvents.State == UrielEvents.Availability.Unknown && !UrielEvents.ProbeSent && UrielState.Present)
            UrielClient.ProbeEvents();
    }

    private bool TakeUrielEvGroup(out string g)
    {
        if (!EvName(_urielEvGroup, "group", out g)) return false;
        string name = g;
        _urielEvRecentGroups.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        _urielEvRecentGroups.Insert(0, name);
        if (_urielEvRecentGroups.Count > 5) _urielEvRecentGroups.RemoveAt(5);
        RebuildUrielEvRecentGroups();
        return true;
    }

    private void RebuildUrielEvRecentGroups()
    {
        if (_urielEvRecentRow == null) return;
        ClearChildren(_urielEvRecentRow);
        if (_urielEvRecentGroups.Count == 0) { AddUrielRowLabel(_urielEvRecentRow, "Recent groups appear here."); return; }
        foreach (var g in _urielEvRecentGroups)
        {
            string name = g;
            AddUrielButton(_urielEvRecentRow, $"UrielEvRecent_{name}", name, $"Use group '{name}'.",
                () => { if (_urielEvGroup != null) _urielEvGroup.Text = name; }, 70);
        }
    }

    private void SendUrielEvMarker(string slot)
    {
        if (!TakeUrielEvGroup(out var g)) return;
        if (!EvContain(_urielEvMarkerContain, _urielEvMarkerLeash, out var contain)) return;
        string unit = Regex.Replace((_urielEvMarkerUnit ?? "").Trim(), @"\s+", "");
        string count = EvInt(_urielEvMarkerCount, 1, 50, out int c) ? c.ToString(CultureInfo.InvariantCulture) : "";
        string level = EvInt(_urielEvMarkerLevel, 1, 999, out int l) ? l.ToString(CultureInfo.InvariantCulture) : "";
        EvSend($".uriel event marker {g} {slot}" + EvArgs((unit, "-"), (count, "0"), (level, "0"), (contain, "-")));
    }

    // Assemble the one comma-separated options word (handoff §4). Returns false with a reason on a bad field.
    private bool BuildUrielEvOptions(out string opts, out string error)
    {
        var parts = new List<string>();
        string err = "";
        if (UrielEvPresets[_urielEvPreset].Length > 0) parts.Add(UrielEvPresets[_urielEvPreset]);

        string lv = EvText(_urielEvLevel);
        switch (UrielEvLevelModes[_urielEvLevelMode])
        {
            case "set":
                if (int.TryParse(lv, NumberStyles.Integer, CultureInfo.InvariantCulture, out int set) && set > 0) parts.Add($"level={set}");
                else err = "Level: enter the level to set (e.g. 40).";
                break;
            case "shift":
                if (int.TryParse(lv, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int sh) && sh != 0) parts.Add(sh > 0 ? $"level+{sh}" : $"level{sh}");
                else err = "Level: enter a shift like +5 or -5.";
                break;
            case "auto":
                if (lv.Length == 0) parts.Add("auto");
                else if (int.TryParse(lv, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int au) && au >= 0) parts.Add(au > 0 ? $"auto+{au}" : "auto");
                else err = "Level: auto takes +N or nothing.";
                break;
        }

        void Mult(InputFieldRef f, string token, string label)
        {
            var t = EvText(f);
            if (t.Length == 0) return;
            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) && x >= 0.1f && x <= 10f)
            { if (Math.Abs(x - 1f) > 0.0001f) parts.Add($"{token}*{x.ToString("0.##", CultureInfo.InvariantCulture)}"); }
            else if (err.Length == 0) err = $"{label}: a multiplier from 0.1 to 10.";
        }
        Mult(_urielEvHp, "hp", "Health");
        Mult(_urielEvDmg, "dmg", "Damage");
        Mult(_urielEvSpeed, "speed", "Move speed");
        Mult(_urielEvAspd, "aspd", "Attack speed");

        var lim = EvText(_urielEvLimit).Replace(" ", "").ToLowerInvariant();
        if (lim.Length > 0)
        {
            if (lim == "0" || Regex.IsMatch(lim, @"^\d+[smh]$")) parts.Add($"limit={lim}");
            else if (err.Length == 0) err = "Time limit: a number with s, m or h (e.g. 15m), at most 6h.";
        }
        if (_urielEvKeepFactions) parts.Add("faction=keep");

        opts = string.Join(",", parts);
        error = err;
        return err.Length == 0;
    }

    private string UrielEvStartCommand(string opts)
    {
        string tpl = EvText(_urielEvTemplate);
        if (tpl.Length == 0) tpl = "-";
        if (opts.Length == 0) return tpl == "-" ? ".uriel event start" : $".uriel event start {tpl}";
        return $".uriel event start {tpl} {opts}";
    }

    private void RefreshUrielEvOptionsPreview()
    {
        if (_urielEvOptionsPreview == null) return;
        bool ok = BuildUrielEvOptions(out var opts, out var err);
        string cmd = UrielEvStartCommand(opts);
        _urielEvOptionsPreview.text = ok
            ? $"<color={Theme.MutedBodyHex}>Start sends:</color> {Esc(cmd)}" +
              (cmd.Length > UrielEvents.CHAT_LIMIT ? $"\n<color=#FF8080>Too long for chat ({cmd.Length}/~120).</color>" : "")
            : $"<color=#FF8080>{err}</color>";
    }

    private void StartUrielEvent()
    {
        if (!BuildUrielEvOptions(out var opts, out var err)) { EvRefuse(err); return; }
        string tpl = EvText(_urielEvTemplate);
        if (tpl.Length > 0 && tpl != "-" && !UrielEvNameRx.IsMatch(tpl)) { EvRefuse("Template: 1-24 letters, digits, '-' or '_' (blank = each group's own squad)."); return; }
        EvSend(UrielEvStartCommand(opts));
    }

    // ============================ PLAYER: EVENT LIBRARY ============================

    private InputFieldRef _urielEvSqName, _urielEvSqUnits, _urielEvSqCount, _urielEvSqLevel, _urielEvSqSet, _urielEvSqEntry, _urielEvSqWeight;
    private string _urielEvSqPick = "";
    private int _urielEvSqRole;     // 0 mob, 1 boss
    private int _urielEvSqPickMode; // 0 any, 1 distinct
    private InputFieldRef _urielEvFlName, _urielEvFlGroup, _urielEvFlSquads;
    private InputFieldRef _urielEvTpName, _urielEvTpFloor, _urielEvTpFloors, _urielEvTpOptions;

    private void BuildUrielEventLibraryTab(GameObject page)
    {
        EnsureUrielEventsSubscribed();

        var intro = AddCard(page, "UrielEvLibIntro");
        AddSectionHeading(intro, "Event library");
        AddBodyText(intro,
            "What a run spawns is built in layers. A <b>squad</b> is one or more <b>sets</b> of entries (\"5 mobs, " +
            "any of these units\"); one set is rolled per run, by weight. A <b>floor template</b> gives each group " +
            "(or <b>*</b> for every group) one or more squads. A <b>template</b> gives each floor (or <b>*</b>) one " +
            "or more floor templates, plus default run options. Names share one namespace. An admin's entries are " +
            "server-wide; a player's are their own.");
        AddBodyText(intro,
            $"<color={Theme.MutedBodyHex}>Chat cuts a line at about 120 characters, so use short unit names (the " +
            "picker drops the CHAR_ prefix) or GUIDs, and add long unit lists in parts with <b>Extend</b>.</color>");
        var lRow = MakeUrielRow(intro, "UrielEvLibListRow");
        AddUrielButton(lRow, "UrielEvLibSquads", "List squads", "(.uriel squad list)", () => EvSend(".uriel squad list"), 110);
        AddUrielButton(lRow, "UrielEvLibFloors", "Floor templates", "(.uriel floor list)", () => EvSend(".uriel floor list"), 130);
        AddUrielButton(lRow, "UrielEvLibTpls", "List templates", "(.uriel template list)", () => EvSend(".uriel template list"), 130);

        AddUrielEventsReplyCard(page, "UrielEvLibReplyCard");

        // --- Squads ---
        var sq = AddCard(page, "UrielEvSquad");
        AddSectionHeading(sq, "Squad");
        _urielEvSqName = AddUrielLabeledInput(sq, "UrielEvSqName", "Squad name", "e.g. militia5");
        var sqRow = MakeUrielRow(sq, "UrielEvSqRow");
        AddUrielButton(sqRow, "UrielEvSqCreate", "Create", "(.uriel squad create <name>)",
            () => { if (EvName(_urielEvSqName, "squad", out var n)) EvSend($".uriel squad create {n}"); }, 90);
        AddUrielButton(sqRow, "UrielEvSqShow", "Show", "Show the squad's sets and entries (.uriel squad show <name>).",
            () => { if (EvName(_urielEvSqName, "squad", out var n)) EvSend($".uriel squad show {n}"); }, 80);
        AddUrielConfirmButton(sqRow, "UrielEvSqDelete", "Delete", "(.uriel squad delete <name>)",
            () => { if (EvName(_urielEvSqName, "squad", out var n)) EvSend($".uriel squad delete {n}"); }, 80, UrielDanger);

        AddSectionHeading(sq, "▸ Add an entry");
        var roleRow = MakeUrielRow(sq, "UrielEvSqRoleRow");
        AddUrielCycleButton(roleRow, "UrielEvSqRole", new[] { "mob", "boss" }, new[] { "Role: mob", "Role: boss" },
            () => _urielEvSqRole, i => _urielEvSqRole = i, "Mobs go to a group's mob markers, bosses to its boss markers.", 110);
        AddUrielCycleButton(roleRow, "UrielEvSqPick", new[] { "any", "distinct" }, new[] { "Pick: any", "Pick: distinct" },
            () => _urielEvSqPickMode, i => _urielEvSqPickMode = i,
            "any = random from the list, repeats allowed · distinct = that many different units.", 120);
        AddBeelzSearchableField(sq, "UrielEvSqPicker", "Find a unit…", "", UrielEvUnitMatches, v => _urielEvSqPick = v);
        var addURow = MakeUrielRow(sq, "UrielEvSqAddUnitRow");
        AddUrielButton(addURow, "UrielEvSqAddUnit", "Add to unit list", "Append the picked unit to the list below.",
            () =>
            {
                var u = Regex.Replace((_urielEvSqPick ?? "").Trim(), @"\s+", "");
                if (u.Length == 0 || _urielEvSqUnits == null) return;
                var cur = EvList(_urielEvSqUnits);
                _urielEvSqUnits.Text = cur.Length == 0 ? u : $"{cur},{u}";
            }, 150);
        AddUrielButton(addURow, "UrielEvSqClearUnits", "Clear list", "Empty the unit list.",
            () => { if (_urielEvSqUnits != null) _urielEvSqUnits.Text = ""; }, 100);
        _urielEvSqUnits = AddUrielLabeledInput(sq, "UrielEvSqUnits", "Units (comma list)", "Bandit_Thug,Bandit_Deadeye");
        _urielEvSqCount = AddUrielLabeledInput(sq, "UrielEvSqCount", "Count (1-50)", "blank = 1");
        _urielEvSqLevel = AddUrielLabeledInput(sq, "UrielEvSqLevel", "Level", "blank = unit's own");
        _urielEvSqSet   = AddUrielLabeledInput(sq, "UrielEvSqSet", "Set #", "blank = set 1; next # = new set");
        var addRow = MakeUrielRow(sq, "UrielEvSqAddRow");
        AddUrielButton(addRow, "UrielEvSqAdd", "Add entry",
            "(.uriel squad add <name> <mob|boss> <units> [count] [any|distinct] [level] [set])", SendUrielEvSquadAdd, 110);

        AddSectionHeading(sq, "▸ Edit a set");
        AddBodyText(sq, "Uses the Set # above. <b>Extend</b> adds the unit list above to an existing entry.");
        _urielEvSqEntry  = AddUrielLabeledInput(sq, "UrielEvSqEntry", "Entry #", "e.g. 1");
        _urielEvSqWeight = AddUrielLabeledInput(sq, "UrielEvSqWeight", "Weight (1-100)", "chance of this set");
        var edRow = MakeUrielRow(sq, "UrielEvSqEditRow");
        AddUrielButton(edRow, "UrielEvSqExtend", "Extend entry", "(.uriel squad extend <name> <set> <entry> <units>)",
            () =>
            {
                if (!EvName(_urielEvSqName, "squad", out var n)) return;
                if (!EvInt(_urielEvSqSet, 1, 99, out int s) || !EvInt(_urielEvSqEntry, 1, 99, out int e)) { EvRefuse("Extend needs a Set # and an Entry #."); return; }
                var units = EvList(_urielEvSqUnits);
                if (units.Length == 0) { EvRefuse("Put the units to add in the unit list."); return; }
                EvSend($".uriel squad extend {n} {s} {e} {units}");
            }, 120);
        AddUrielButton(edRow, "UrielEvSqWeightBtn", "Set weight", "(.uriel squad weight <name> <set> <1-100>)",
            () =>
            {
                if (!EvName(_urielEvSqName, "squad", out var n)) return;
                if (!EvInt(_urielEvSqSet, 1, 99, out int s) || !EvInt(_urielEvSqWeight, 1, 100, out int w)) { EvRefuse("Set weight needs a Set # and a weight from 1 to 100."); return; }
                EvSend($".uriel squad weight {n} {s} {w}");
            }, 110);
        AddUrielConfirmButton(edRow, "UrielEvSqRemove", "Remove", "Remove an entry, or the whole set when Entry # is blank (.uriel squad remove <name> <set> [entry]).",
            () =>
            {
                if (!EvName(_urielEvSqName, "squad", out var n)) return;
                if (!EvInt(_urielEvSqSet, 1, 99, out int s)) { EvRefuse("Remove needs a Set #."); return; }
                EvSend(EvInt(_urielEvSqEntry, 1, 99, out int e) ? $".uriel squad remove {n} {s} {e}" : $".uriel squad remove {n} {s}");
            }, 90, UrielDanger);

        // --- Floor templates ---
        var fl = AddCard(page, "UrielEvFloor");
        AddSectionHeading(fl, "Floor template");
        _urielEvFlName = AddUrielLabeledInput(fl, "UrielEvFlName", "Floor template name", "e.g. f1");
        var flRow = MakeUrielRow(fl, "UrielEvFlRow");
        AddUrielButton(flRow, "UrielEvFlCreate", "Create", "(.uriel floor create <name>)",
            () => { if (EvName(_urielEvFlName, "floor template", out var n)) EvSend($".uriel floor create {n}"); }, 90);
        AddUrielButton(flRow, "UrielEvFlShow", "Show", "(.uriel floor show <name>)",
            () => { if (EvName(_urielEvFlName, "floor template", out var n)) EvSend($".uriel floor show {n}"); }, 80);
        AddUrielConfirmButton(flRow, "UrielEvFlDelete", "Delete", "(.uriel floor delete <name>)",
            () => { if (EvName(_urielEvFlName, "floor template", out var n)) EvSend($".uriel floor delete {n}"); }, 80, UrielDanger);
        _urielEvFlGroup  = AddUrielLabeledInput(fl, "UrielEvFlGroup", "Group (* = every group)", "e.g. g1");
        _urielEvFlSquads = AddUrielLabeledInput(fl, "UrielEvFlSquads", "Squads (comma = one rolled)", "e.g. militia5,bandits5");
        var faRow = MakeUrielRow(fl, "UrielEvFlAssignRow");
        AddUrielButton(faRow, "UrielEvFlAssign", "Assign", "(.uriel floor assign <name> <group|*> <squads>)",
            () => SendUrielEvAssign(".uriel floor assign", _urielEvFlName, "floor template", _urielEvFlGroup, "group", _urielEvFlSquads, unassign: false), 90);
        AddUrielButton(faRow, "UrielEvFlUnassign", "Unassign group", "(.uriel floor assign <name> <group|*> -)",
            () => SendUrielEvAssign(".uriel floor assign", _urielEvFlName, "floor template", _urielEvFlGroup, "group", _urielEvFlSquads, unassign: true), 140);

        // --- Templates ---
        var tp = AddCard(page, "UrielEvTemplate");
        AddSectionHeading(tp, "Template");
        _urielEvTpName = AddUrielLabeledInput(tp, "UrielEvTpName", "Template name", "e.g. keep");
        var tpRow = MakeUrielRow(tp, "UrielEvTpRow");
        AddUrielButton(tpRow, "UrielEvTpCreate", "Create", "(.uriel template create <name>)",
            () => { if (EvName(_urielEvTpName, "template", out var n)) EvSend($".uriel template create {n}"); }, 90);
        AddUrielButton(tpRow, "UrielEvTpShow", "Show", "(.uriel template show <name>)",
            () => { if (EvName(_urielEvTpName, "template", out var n)) EvSend($".uriel template show {n}"); }, 80);
        AddUrielConfirmButton(tpRow, "UrielEvTpDelete", "Delete", "(.uriel template delete <name>)",
            () => { if (EvName(_urielEvTpName, "template", out var n)) EvSend($".uriel template delete {n}"); }, 80, UrielDanger);
        _urielEvTpFloor  = AddUrielLabeledInput(tp, "UrielEvTpFloor", "Floor (1, 2, … or *)", "1 = ground");
        _urielEvTpFloors = AddUrielLabeledInput(tp, "UrielEvTpFloors", "Floor templates (comma = one rolled)", "e.g. f2,f2b");
        var tfRow = MakeUrielRow(tp, "UrielEvTpFloorRow");
        AddUrielButton(tfRow, "UrielEvTpSetFloor", "Set floor", "(.uriel template floor <name> <floor|*> <floor templates>)",
            () => SendUrielEvAssign(".uriel template floor", _urielEvTpName, "template", _urielEvTpFloor, "floor", _urielEvTpFloors, unassign: false), 100);
        AddUrielButton(tfRow, "UrielEvTpClearFloor", "Clear floor", "(.uriel template floor <name> <floor|*> -)",
            () => SendUrielEvAssign(".uriel template floor", _urielEvTpName, "template", _urielEvTpFloor, "floor", _urielEvTpFloors, unassign: true), 110);
        _urielEvTpOptions = AddUrielLabeledInput(tp, "UrielEvTpOptions", "Default run options", "e.g. hard,auto+2,limit=20m");
        var toRow = MakeUrielRow(tp, "UrielEvTpOptRow");
        AddUrielButton(toRow, "UrielEvTpSetOpts", "Set default options", "Runs from this template start with these options (.uriel template options <name> <options>).",
            () =>
            {
                if (!EvName(_urielEvTpName, "template", out var n)) return;
                var o = Regex.Replace(EvText(_urielEvTpOptions), @"\s+", "");
                if (o.Length == 0) { EvRefuse("Type the options (one comma-separated word), or use Clear options."); return; }
                EvSend($".uriel template options {n} {o}");
            }, 170);
        toRow = MakeUrielRow(tp, "UrielEvTpOptRow2");
        AddUrielButton(toRow, "UrielEvTpCopyOpts", "Use run panel's", "Copy the options built on the Castle Events tab into the field above.",
            () => { if (_urielEvTpOptions != null && BuildUrielEvOptions(out var o, out _)) _urielEvTpOptions.Text = o; }, 140);
        AddUrielButton(toRow, "UrielEvTpClearOpts", "Clear options", "(.uriel template options <name> -)",
            () => { if (EvName(_urielEvTpName, "template", out var n)) EvSend($".uriel template options {n} -"); }, 120);

        RefreshUrielEventsLabels();
    }

    private void SendUrielEvSquadAdd()
    {
        if (!EvName(_urielEvSqName, "squad", out var n)) return;
        var units = EvList(_urielEvSqUnits);
        if (units.Length == 0) { EvRefuse("Add at least one unit to the unit list."); return; }
        string role = _urielEvSqRole == 1 ? "boss" : "mob";
        string count = EvInt(_urielEvSqCount, 1, 50, out int c) ? c.ToString(CultureInfo.InvariantCulture) : "";
        string pick = _urielEvSqPickMode == 1 ? "distinct" : "";
        string level = EvInt(_urielEvSqLevel, 1, 999, out int l) ? l.ToString(CultureInfo.InvariantCulture) : "";
        string set = EvInt(_urielEvSqSet, 1, 99, out int s) ? s.ToString(CultureInfo.InvariantCulture) : "";
        EvSend($".uriel squad add {n} {role} {units}" + EvArgs((count, "1"), (pick, "any"), (level, "0"), (set, "1")));
    }

    private static void SendUrielEvAssign(string verb, InputFieldRef nameField, string kind, InputFieldRef keyField, string keyKind,
        InputFieldRef listField, bool unassign)
    {
        if (!EvName(nameField, kind, out var n)) return;
        var key = EvText(keyField);
        bool keyOk = key == "*" || (keyKind == "floor" ? int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int f) && f >= 1 : UrielEvNameRx.IsMatch(key));
        if (!keyOk) { EvRefuse(keyKind == "floor" ? "Floor: 1 for the ground floor, 2 above it, … or * for every floor." : "Group: a group name or * for every group."); return; }
        if (unassign) { EvSend($"{verb} {n} {key} -"); return; }
        var list = EvList(listField);
        if (list.Length == 0) { EvRefuse("List at least one name (comma-separate several: one is rolled per run)."); return; }
        EvSend($"{verb} {n} {key} {list}");
    }

    // ============================ ADMIN: UNITS ============================

    private string _urielUnitName = "";
    private InputFieldRef _urielUnitCount, _urielUnitLevel, _urielUnitLeash, _urielUnitRadius;
    private int _urielUnitPlace;     // 0 here, 1 aim
    private int _urielUnitContain;
    private int _urielUnitKillFilter; // all / boss / mob

    private void BuildUrielAdminUnitsTab(GameObject page)
    {
        EnsureUrielEventsSubscribed();
        var content = BeginAdminGate(page);
        AddBodyText(content,
            "Admin unit tools (Castle Events). Uriel units are never saved — a restart removes them — and drop no loot. " +
            "The server enforces admin regardless of this gate.");

        AddUrielEventsReplyCard(content, "UrielUnitReplyCard");

        var sp = AddCard(content, "UrielUnitSpawn");
        AddSectionHeading(sp, "Spawn units");
        AddBeelzSearchableField(sp, "UrielUnitName", "Unit name or GUID…", "", UrielEvUnitMatches, v => _urielUnitName = v);
        _urielUnitCount = AddUrielLabeledInput(sp, "UrielUnitCount", "Count", "blank = 1");
        _urielUnitLevel = AddUrielLabeledInput(sp, "UrielUnitLevel", "Level", "blank = unit's own");
        var pRow = MakeUrielRow(sp, "UrielUnitPlaceRow");
        AddUrielCycleButton(pRow, "UrielUnitPlace", new[] { "here", "aim" }, new[] { "At: your feet", "At: aim point" },
            () => _urielUnitPlace, i => _urielUnitPlace = i,
            "Your feet is the safe choice from a UI click — the aim point is wherever the cursor ray lands.", 140);
        AddUrielCycleButton(pRow, "UrielUnitContain", UrielEvContainValues, UrielEvContainLabels, () => _urielUnitContain,
            i => _urielUnitContain = i, "Where the spawned units must stay.", 200);
        _urielUnitLeash = AddUrielLabeledInput(sp, "UrielUnitLeash", "Leash (metres, 3-60)", "e.g. 15");
        var sRow = MakeUrielRow(sp, "UrielUnitSpawnRow");
        AddUrielButton(sRow, "UrielUnitSpawnBtn", "Spawn", "(.uriel unit spawn <name|guid> [count] [level] [here|aim] [contain])",
            () =>
            {
                var u = Regex.Replace((_urielUnitName ?? "").Trim(), @"\s+", "");
                if (u.Length == 0) { EvRefuse("Pick or type a unit."); return; }
                if (!EvContain(_urielUnitContain, _urielUnitLeash, out var contain)) return;
                string count = EvInt(_urielUnitCount, 1, 50, out int c) ? c.ToString(CultureInfo.InvariantCulture) : "1";
                string level = EvInt(_urielUnitLevel, 1, 999, out int l) ? l.ToString(CultureInfo.InvariantCulture) : "0";
                string place = _urielUnitPlace == 1 ? "aim" : "here";
                EvSend($".uriel unit spawn {u} {count} {level} {place}" + (contain.Length > 0 ? $" {contain}" : ""));
            }, 100);

        var mg = AddCard(content, "UrielUnitManage");
        AddSectionHeading(mg, "Manage units");
        AddBodyText(mg, "<b>Kill</b> is a real death: groups clear and doors open, as if players had won. <b>Despawn</b> and <b>Clear</b> just remove.");
        _urielUnitRadius = AddUrielLabeledInput(mg, "UrielUnitRadius", "Radius (metres)", "blank = server default");
        var kRow = MakeUrielRow(mg, "UrielUnitKillRow");
        AddUrielCycleButton(kRow, "UrielUnitKillFilter", new[] { "all", "boss", "mob" }, new[] { "Kill: all", "Kill: bosses", "Kill: mobs" },
            () => _urielUnitKillFilter, i => _urielUnitKillFilter = i, "Which units Kill nearby hits.", 120);
        AddUrielButton(kRow, "UrielUnitKill", "Kill nearby", "(.uriel unit kill [radius] [all|boss|mob])",
            () =>
            {
                string r = EvInt(_urielUnitRadius, 1, 500, out int rad) ? rad.ToString(CultureInfo.InvariantCulture) : "";
                string f = new[] { "all", "boss", "mob" }[_urielUnitKillFilter];
                if (r.Length == 0 && f != "all") { EvRefuse("Killing only bosses or mobs needs a radius."); return; }
                EvSend(".uriel unit kill" + EvArgs((r, ""), (f, "all")));
            }, 110);
        AddUrielButton(kRow, "UrielUnitDespawn", "Despawn nearest", "Remove the nearest Uriel unit within the radius (.uriel unit despawn [radius]).",
            () => EvSend(".uriel unit despawn" + (EvInt(_urielUnitRadius, 1, 500, out int rad) ? $" {rad}" : "")), 140);
        var lRow = MakeUrielRow(mg, "UrielUnitListRow");
        AddUrielButton(lRow, "UrielUnitList", "List", "Counts alive / pending / being removed, per plot (.uriel unit list).",
            () => EvSend(".uriel unit list"), 80);
        AddUrielButton(lRow, "UrielUnitStatus", "Status", "(.uriel unit status [radius])",
            () => EvSend(".uriel unit status" + (EvInt(_urielUnitRadius, 1, 500, out int rad) ? $" {rad}" : "")), 80);
        AddUrielConfirmButton(lRow, "UrielUnitClear", "Clear plot", "Remove every Uriel unit on the plot you're standing in (.uriel unit clear).",
            () => EvSend(".uriel unit clear"), 110, UrielDanger);

        var pg = AddCard(content, "UrielUnitPurge");
        AddSectionHeading(pg, "Purge (server-wide)");
        AddBodyText(pg, "<color=#FF8080>Removes every Uriel unit on the server.</color> <b>Arm</b> first (the server says what it will remove), then <b>Confirm</b> within 30 s.");
        var puRow = MakeUrielRow(pg, "UrielUnitPurgeRow");
        AddUrielConfirmButton(puRow, "UrielUnitPurgeArm", "Arm purge", "(.uriel unit purge)",
            () => EvSend(".uriel unit purge"), 110, UrielDanger);
        AddUrielConfirmButton(puRow, "UrielUnitPurgeConfirm", "Confirm purge", "(.uriel unit purge confirm)",
            () => EvSend(".uriel unit purge confirm"), 130, UrielWipe);

        var dg = AddCard(content, "UrielUnitDiag");
        AddSectionHeading(dg, "Diagnostics");
        AddBodyText(dg, "Details go to the <b>server</b> log.");
        var dRow = MakeUrielRow(dg, "UrielUnitDiagRow");
        AddUrielButton(dRow, "UrielUnitRooms", "Rooms", "(.uriel unit rooms)", () => EvSend(".uriel unit rooms"), 80);
        AddUrielButton(dRow, "UrielUnitProbe", "Probe", "(.uriel unit probe)", () => EvSend(".uriel unit probe"), 80);
        AddUrielButton(dRow, "UrielUnitProbeMap", "Probe map", "(.uriel unit probemap)", () => EvSend(".uriel unit probemap"), 100);
        GameObject doRow = null;
        foreach (var verb in new[] { "show", "open", "close", "lock", "unlock" })
        {
            string v = verb;
            if (v is "show" or "lock") doRow = MakeUrielRow(dg, $"UrielUnitDoorRow_{v}");
            AddUrielButton(doRow, $"UrielUnitDoor_{v}", $"Door {v}", $"Nearest door (.uriel unit door {v}).", () => EvSend($".uriel unit door {v}"), 90);
        }

        RefreshUrielEventsLabels();
    }
}
