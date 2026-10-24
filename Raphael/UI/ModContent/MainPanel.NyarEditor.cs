using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Raphael.Resources;
using Raphael.Services.Nyar;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using TMPro;
using UnityEngine;

namespace Raphael.UI.ModContent;

// The Nyarlathotep event editor (opened by an event's Edit… button, inline under that event) and the Admin: Units tab.
// Api 8 has no field read, so the editor sends `.nyar event info <id>`, reads the reply (NyarForm.Read) and fills the
// form from it. 0.72.0 (PLAN.md §B / §B2): every field is a control that knows the server's range (steppers, choice
// buttons, list rows; MainPanel.NyarControls.cs); the form shows only the fields of the event's shape (units form or
// wave list); changed fields are marked and go out one by one or with "Save all changes" through a NyarBatch (paced,
// stops at the first refusal); pools are edited as rows and saved through the same batch.
// 0.73.0: the undead-hordes types (Infestation, HordeMarch) and a destination on spawn waves (MainPanel.NyarHordes.cs);
// a definition the server holds invalid keeps its last values (NyarForm ReasonOnly) instead of losing the form.
public partial class MainPanel
{
    private string _nyarEditId = "";
    private readonly NyarForm _nyarForm = new();
    private GameObject _nyarEdHost;
    private TextMeshProUGUI _nyarEdBarLabel;
    private ButtonRef _nyarEdSaveAllBtn, _nyarEdUndoBtn;
    private bool _nyarEdShowInfo;
    private int _nyarEdWave = 1;
    private NyarBatch _nyarEdBatch;          // this editor's running save (fields or pool)
    private bool _nyarEdBatchReread;         // re-read `event info` when it ends (waves added / removed / converted)
    private bool _nyarEdBatchSubscribed;
    private bool _nyarEdWasQuarantined;
    private string _nyarEditOtherField = "", _nyarOtherN = "", _nyarOtherM = "", _nyarOtherName = "";

    // pool card
    private string _nyarPoolSel = "";        // pool being edited; "" with _nyarPoolNew = a new one
    private bool _nyarPoolNew;
    private string _nyarPoolNewName = "";
    private List<string> _nyarPoolMembers = new();
    private List<string> _nyarPoolLoaded = new();
    private readonly Dictionary<string, List<string>> _nyarSessionPools = new(StringComparer.Ordinal);   // null = deleted
    private string _nyarPoolBatchName = "";
    private List<string> _nyarPoolBatchMembers;

    private void OpenNyarEditor(string id)
    {
        bool closing = _nyarEditId == id;
        CancelNyarEditorBatch();
        _nyarEditId = closing ? "" : id;
        _nyarForm.Clear();
        _nyarEdTextMode.Clear(); _nyarUnitPicks.Clear(); _nyarSessionPools.Clear();
        _nyarEditOtherField = _nyarOtherN = _nyarOtherM = _nyarOtherName = "";
        _nyarPoolSel = ""; _nyarPoolNew = false; _nyarPoolNewName = ""; _nyarPoolMembers = new(); _nyarPoolLoaded = new();
        _nyarEdWave = 1; _nyarEdShowInfo = false;
        if (!closing)
        {
            EnsureNyarBatchSubscribed();
            NyarClient.EventInfo(id);   // NyarCapture fills the reply; OnNyarInfoCaptured loads the form
        }
        RebuildNyarDefs();
    }

    private void EnsureNyarBatchSubscribed()
    {
        if (_nyarEdBatchSubscribed) return;
        NyarBatchRunner.Changed += OnNyarBatchChanged;
        NyarProtocolService.CountdownTick += OnNyarEdTick;
        _nyarEdBatchSubscribed = true;
    }

    // A save still running when the editor closes is cancelled (a queued step is dropped; a sent one can't be recalled).
    private void CancelNyarEditorBatch()
    {
        if (_nyarEdBatch != null && NyarBatchRunner.Current == _nyarEdBatch && NyarBatchRunner.Busy) NyarBatchRunner.Cancel();
        _nyarEdBatch = null;
    }

    private void OnNyarInfoCaptured()
    {
        if (_nyarEditId.Length == 0 || !string.Equals(NyarCapture.InfoId, _nyarEditId, StringComparison.OrdinalIgnoreCase)) return;
        _nyarForm.Apply(NyarForm.Read(NyarCapture.InfoLines));
        int waves = _nyarForm.Load.Waves.Count;
        if (_nyarEdWave > waves + 1 || _nyarEdWave < 1) _nyarEdWave = Math.Max(1, waves);
        if (_nyarPoolSel.Length > 0 && !_nyarPoolNew) _nyarPoolLoaded = NyarPoolMembers(_nyarPoolSel) ?? new List<string>();
        RebuildNyarDefs();
    }

    private void OnNyarEdTick()
    {
        bool q = NyarClient.Quarantined;
        if (q || q != _nyarEdWasQuarantined) NyarRefreshAll();
        _nyarEdWasQuarantined = q;
    }

    private void OnNyarBatchChanged()
    {
        var b = NyarBatchRunner.Current;
        if (b == null || b != _nyarEdBatch) { NyarRefreshAll(); return; }
        if (!b.Finished) { NyarRefreshAll(); return; }
        _nyarEdBatch = null;
        if (b.IsPool)
        {
            if (b.State == NyarBatchState.Done) NyarPoolSaved(_nyarPoolBatchName, _nyarPoolBatchMembers);
        }
        else
        {
            // A form key is saved when every step it made was answered ok (a start rule makes two).
            var stepsOf = new Dictionary<string, int>(StringComparer.Ordinal);
            var okOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < b.Steps.Count; i++)
            {
                string k = b.Steps[i].FormKey;
                stepsOf[k] = stepsOf.TryGetValue(k, out var n) ? n + 1 : 1;
                if (i < b.Acknowledged) okOf[k] = okOf.TryGetValue(k, out var m) ? m + 1 : 1;
            }
            foreach (var kv in stepsOf)
                if (okOf.TryGetValue(kv.Key, out var ok) && ok == kv.Value) _nyarForm.MarkSaved(kv.Key);
            // An immediate set (Destination here, Start here) isn't a form change: record what the server took.
            for (int i = 0; i < b.Acknowledged && i < b.Steps.Count; i++)
                if (NyarImmediateKeys.Contains(b.Steps[i].Field)) _nyarForm.Acknowledge(b.Steps[i].Field, b.Steps[i].Value);
            if (b.State == NyarBatchState.Stopped && b.Current != null) _nyarForm.MarkRefused(b.Current.FormKey, b.RefusedMessage);
            // Stopped after some steps went through: re-read, so the form matches what the server now holds.
            if (b.State == NyarBatchState.Stopped && b.Acknowledged > 0) _nyarEdBatchReread = true;
        }
        if (_nyarEdBatchReread && _nyarEditId.Length > 0) NyarClient.EventInfo(_nyarEditId);
        _nyarEdBatchReread = false;
        RedrawNyarEditor();
    }

    // ---------------------------------------------------------------- saving

    /// <summary>Sets sent at once (not form values) whose confirmed value the form records.</summary>
    private static readonly HashSet<string> NyarImmediateKeys = new(StringComparer.Ordinal) { "action.destination", "action.start" };

    /// <summary>Sets that change other fields on the server: the editor re-reads `event info` after them.</summary>
    private static bool NyarRereadAfter(string key) => key is "action.destination" or "action.start" or "action.start.minDistance";

    private string NyarKeyLabel(string key)
    {
        var start = System.Text.RegularExpressions.Regex.Match(key, @"^action\.waveList\.(\d+)\.(start|units|bosses)$");
        if (start.Success)
            return $"wave {start.Groups[1].Value} {(start.Groups[2].Value == "start" ? "start" : start.Groups[2].Value)}";
        switch (key)
        {
            case "location": return "location";
            case "action.behaviour": return "behaviour";
            case "action.units": return "units per wave";
            case "action.bosses": return "final-wave bosses";
            case "action.factions": return "factions";
            case "action.loot": return "loot";
            case "action.allowTerritory": return "claimed territory";
            case "action.destination": return "destination";
            case "action.start": return "start";
        }
        // A spread block's key says which block: "growth: most alive", "spread: drawn from".
        string block = NyarFieldRules.SpreadBlockOf(key);
        string prefix = block switch
        {
            "action.growth." => "growth: ", "action.infest.spread." => "spread on arrival: ", "action.spread." => "spread: ", _ => "",
        };
        if (block == null && key.StartsWith("action.infest.", StringComparison.Ordinal)) prefix = "on arrival: ";
        if (block != null && key.EndsWith(".pool", StringComparison.Ordinal)) return prefix + "drawn from";
        var s = NyarFieldRules.Spec(key);
        return prefix + (s != null ? s.Label.TrimEnd(' ', '×').ToLowerInvariant() : key);
    }

    /// <summary>The client-side check of one form key (null = fine). Same per-value rules as the server.</summary>
    private string NyarCheckKey(string key, string value)
    {
        bool apo = NyarForm.IsApocalypseFamily(_nyarForm.Load.Kind);
        switch (key)
        {
            case "action.destination": return NyarFieldRules.CheckDestination(value, allowNone: _nyarForm.Load.Kind != NyarEventKind.HordeMarch);
            case "action.start": return value == "here" ? null : "Press Start here.";
        }
        if (NyarFieldRules.SpreadBlockOf(key) != null && key.EndsWith(".pool", StringComparison.Ordinal)) return NyarFieldRules.CheckSpreadPool(value);
        if (key.EndsWith(".start", StringComparison.Ordinal)) return NyarForm.CheckStart(value);
        switch (key)
        {
            case "location": return NyarCheckLocation(value);
            case "action.behaviour": return NyarCheckBehaviour(value);
            case "action.loot": case "action.allowTerritory": return value is "true" or "false" ? null : "Pick Yes or No.";
            case "action.units": return NyarFieldRules.CheckUnitList(value, false);
            case "action.bosses": return NyarFieldRules.CheckBossList(value, false);
            case "action.factions": return NyarFieldRules.CheckFactions(value);
        }
        if (key.StartsWith("action.waveList.", StringComparison.Ordinal))
        {
            if (key.EndsWith(".units", StringComparison.Ordinal) && key.Split('.').Length == 4) return NyarFieldRules.CheckUnitList(value, true);
            if (key.EndsWith(".bosses", StringComparison.Ordinal)) return NyarFieldRules.CheckBossList(value, true);
            if (key.EndsWith(".whenCleared", StringComparison.Ordinal)) return value is "true" or "false" ? null : "Pick Yes or No.";
        }
        if (key.StartsWith("action.pools.", StringComparison.Ordinal))
        {
            if (value is "none") return null;
            if ((value.StartsWith("+") || value.StartsWith("-")) && NyarFieldRules.IsUnitName(value.Substring(1))) return null;
            var parts = NyarFieldRules.SplitList(value, NyarFieldRules.MaxPoolMembers, out var why);
            return parts == null ? why : NyarFieldRules.CheckPoolMembers(parts);
        }
        var spec = NyarFieldRules.Spec(key, apo);
        if (spec != null) return NyarFieldRules.CheckValue(spec, value);
        return (value ?? "").Trim().Length == 0 ? "Type a value." : null;
    }

    private static int NyarHuntRange(string behaviour)
    {
        var p = (behaviour ?? "").Split(' ');
        return p.Length == 2 && p[0] == "hunt" && int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : 0;
    }

    private string NyarCheckBehaviour(string v)
    {
        if (v is "none" or "regionhunt") return null;
        var p = (v ?? "").Split(' ');
        if (p.Length == 2 && p[0] == "hunt")
            return NyarFieldRules.CheckValue(NyarFieldRules.Spec("behaviour.range"), p[1]);
        return "Pick Stay put, Hunt players or Roam the region.";
    }

    private string NyarCheckLocation(string v)
    {
        if (v is "region" or "here") return null;
        var p = (v ?? "").Split(' ');
        if (p.Length != 3 || p[0] != "aroundplayer") return "Pick Whole region or Around a player (or press Use where I stand).";
        var near = NyarFieldRules.CheckValue(NyarFieldRules.Spec("location.near"), p[1]);
        if (near != null) return near;
        var far = NyarFieldRules.CheckValue(NyarFieldRules.Spec("location.far"), p[2]);
        if (far != null) return far;
        int a = int.Parse(p[1], CultureInfo.InvariantCulture), b = int.Parse(p[2], CultureInfo.InvariantCulture);
        if (a >= b) return $"Nearest ({a} m) must be less than farthest ({b} m).";
        int hunt = NyarHuntRange(_nyarForm.Get("action.behaviour"));
        if (hunt > 0 && b > hunt) return $"With Hunt players at {hunt} m, farthest can be at most {hunt} m (or raise the hunt range).";
        return null;
    }

    /// <summary>Send the changed values of <paramref name="keys"/> (null = every change) as one paced batch, in an order
    /// the server accepts. Refuses up front, naming the field, when a value fails its check.</summary>
    private void NyarSaveKeys(IEnumerable<string> keys, string what, bool reread = false)
    {
        string id = _nyarEditId;
        if (id.Length == 0) return;
        if (NyarBatchRunner.Busy) { NyarState.SetError("event set", "Still saving — wait for it to finish."); return; }
        if (NyarClient.Quarantined) { NyarState.SetError("event set", NyarBatchRunner.WaitText); return; }
        var set = keys == null ? null : new HashSet<string>(keys, StringComparer.Ordinal);
        var changes = _nyarForm.Changes(set == null ? null : k => set.Contains(k));
        if (changes.Count == 0) { NyarState.SetError("event set", "Nothing changed to save."); return; }
        foreach (var c in changes)
        {
            var why = NyarCheckKey(c.Key, c.Value);
            if (why != null) { NyarState.SetError("event set", $"{NyarWordCap(NyarKeyLabel(c.Key))}: {why}"); NyarRefreshAll(); return; }
        }
        int waves = _nyarForm.Load.Waves.Count;
        var expanded = new List<KeyValuePair<string, string>>();
        var formKeyOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in changes)
        {
            if (c.Key == $"action.waveList.{waves + 1}.units") reread = true;     // a new wave: re-read the shape after
            if (NyarRereadAfter(c.Key)) reread = true;
            foreach (var s in NyarForm.SetsFor(c.Key, c.Value)) { expanded.Add(s); formKeyOf[s.Key] = c.Key; }
        }
        var ordered = NyarFieldRules.OrderForSave(expanded, _nyarForm.Loaded);
        NyarBeginAction();
        if (!NyarState.SupportsTwins)
        {
            foreach (var s in ordered) NyarClient.EventSet(id, s.Key, s.Value);
            NyarProtocolService.RefreshDefs();
            return;
        }
        var steps = ordered.Select(s => new NyarBatchStep
        {
            Field = s.Key, Value = s.Value, Command = NyarClient.EventSetCommand(id, s.Key, s.Value),
            Label = NyarKeyLabel(formKeyOf[s.Key]), FormKey = formKeyOf[s.Key],
        }).ToList();
        var batch = new NyarBatch(id, what, false, steps);
        _nyarEdBatch = batch;
        _nyarEdBatchReread = reread;
        if (!NyarBatchRunner.Start(batch)) { _nyarEdBatch = null; _nyarEdBatchReread = false; }
        NyarRefreshAll();
    }

    /// <summary>One immediate set that is not a form value (convert, remove a wave, delete a pool): a one-step batch.</summary>
    private void NyarSendOne(string field, string value, string label, bool reread)
    {
        string id = _nyarEditId;
        if (NyarBatchRunner.Busy) { NyarState.SetError("event set", "Still saving — wait for it to finish."); return; }
        if (NyarClient.Quarantined) { NyarState.SetError("event set", NyarBatchRunner.WaitText); return; }
        NyarBeginAction();
        if (!NyarState.SupportsTwins) { NyarClient.EventSet(id, field, value); NyarProtocolService.RefreshDefs(); return; }
        var batch = new NyarBatch(id, label, false, new List<NyarBatchStep>
        {
            new() { Field = field, Value = value, Command = NyarClient.EventSetCommand(id, field, value), Label = label, FormKey = field },
        });
        _nyarEdBatch = batch;
        _nyarEdBatchReread = reread;
        if (!NyarBatchRunner.Start(batch)) { _nyarEdBatch = null; _nyarEdBatchReread = false; }
        NyarRefreshAll();
    }

    // ---------------------------------------------------------------- inline editor

    private void BuildNyarInlineEditor(GameObject parent, NyarDef d)
    {
        // 0.73.0: untinted. A tint is a "Tint" child, which RedrawNyarEditor's NyarClear destroyed, so the card
        // flipped between purple (full rebuild) and gray (redraw). Theme.CardBackground on both paths now.
        var ed = AddCard(parent, "NyarEditor", padding: 8, innerSpacing: 3);
        _nyarEdHost = ed;
        BuildNyarEditorBody(ed, d);
    }

    private void RedrawNyarEditor()
    {
        if (_nyarEdHost == null || _nyarEditId.Length == 0) return;
        var d = NyarState.Defs.FirstOrDefault(x => x.Id == _nyarEditId);
        if (d == null) return;
        NyarClear(_nyarEdHost);
        BuildNyarEditorBody(_nyarEdHost, d);
    }

    private void BuildNyarEditorBody(GameObject ed, NyarDef d)
    {
        _nyarEdGen++;
        _nyarEdUis.Clear();
        string id = d.Id;

        var head = MakeUrielRow(ed, "NyarEdHead");
        AddNyarRowText(head, $"<b>Editing {NyarText(d.Name)}</b> <color=#888888>({NyarEscape(id)})</color>");
        AddUrielButton(head, "NyarEdReload", "Reload values", $"Read {id}'s current definition again (.nyar event info {id}). Your unsaved changes stay.",
            () => NyarClient.EventInfo(id), 120);
        AddUrielButton(head, "NyarEdClose", "Close", "Close the editor (unsaved changes are dropped).", () => OpenNyarEditor(id), 70);
        AddNyarResultLine(ed);
        AddNyarErrorLine(ed);

        // Still-missing reason (B7): the server saved the definition but holds it disabled until it is valid. The def row
        // has it after a re-read; an `event info` reply of only the reason has it first.
        string notReady = d.State == "disabled" && !string.IsNullOrEmpty(d.Reason) && d.Reason != "disabled" ? d.Reason
            : _nyarForm.Load.NotReady.Length > 0 ? _nyarForm.Load.NotReady : "";
        if (notReady.Length > 0)
        {
            var box = AddCard(ed, "NyarEdNotReady", new Color(0.35f, 0.28f, 0.05f, 0.9f), padding: 6, innerSpacing: 2);
            AddBodyText(box, $"<color={NyarChangedHex}><b>Not ready yet:</b> {NyarText(notReady)}. It clears once the definition is valid; then press Enable.</color>",
                Theme.ScaledUI(12));
        }

        bool haveInfo = string.Equals(NyarCapture.InfoId, id, StringComparison.OrdinalIgnoreCase) && NyarCapture.InfoLines.Count > 0;
        if (!haveInfo || !_nyarForm.HaveLoad)
        {
            AddBodyText(ed, "<color=#888888>Reading the definition from the server…  (it also shows in chat)</color>", Theme.ScaledUI(12));
            return;
        }

        // Changes bar: count, Save all, Undo.
        var bar = MakeUrielRow(ed, "NyarEdBar");
        _nyarEdBarLabel = AddNyarRowText(bar, "");
        _nyarEdSaveAllBtn = AddUrielButton(bar, "NyarEdSaveAll", "Save all changes",
            "Send every changed field, one at a time, in an order the server accepts. It stops at the first refusal and says which.",
            () => NyarSaveKeys(null, "all changes"), 150);
        _nyarEdUndoBtn = AddUrielConfirmButton(bar, "NyarEdUndo", "Undo changes", "Put every field back to the value the server has.",
            () => { _nyarForm.Undo(); RedrawNyarEditor(); }, 110);

        var load = _nyarForm.Load;
        if (load.NotReady.Length > 0 && load.Kind != NyarEventKind.Unknown)
            NyarNote(ed, Muted("The server describes an event only once it is valid, so these are the last values read plus the changes saved since."));
        var showRow = MakeUrielRow(ed, "NyarEdInfoToggle");
        AddUrielButton(showRow, "NyarEdInfoBtn", _nyarEdShowInfo ? "Hide server text" : "Show server text",
            "The definition exactly as the server describes it (.nyar event info).", () => { _nyarEdShowInfo = !_nyarEdShowInfo; RedrawNyarEditor(); }, 130);
        AddNyarRowText(showRow, Muted(NyarKindText(load.Kind)));
        if (_nyarEdShowInfo)
            AddBodyText(ed, $"<color=#CCCCCC>{string.Join("\n", NyarCapture.InfoLines.Select(NyarEscape))}</color>", Theme.ScaledUI(11));
        if (load.Unread.Count > 0)
            NyarNote(ed, $"<color={NyarChangedHex}>Some values couldn't be read from the server's reply (its line was shortened to fit chat). " +
                         "Those fields are empty and say so — pick a value, or leave them alone to keep the server's.</color>");
        NyarNote(ed, Muted("Each field shows the server's range; - / + and the quick picks stay inside it. A changed field is marked " +
                           "<color=#FFD75A>• changed</color>: press its <b>Set</b>, or <b>Save all changes</b>. The server checks every value."));

        bool apo = NyarForm.IsApocalypseFamily(load.Kind);
        AddSectionHeading(ed, "▸ Basics");
        NyarTextField(ed, "name", 240, "the name players see", "The name in banners and the event list.");
        // Opened on an invalid definition: the kind isn't known yet, so only the fields every type takes.
        bool kindKnown = load.Kind != NyarEventKind.Unknown;
        if (kindKnown) NyarFieldStepper(ed, "durationSeconds", apo, "How long the event runs.");

        AddSectionHeading(ed, "▸ Conditions");
        NyarFieldStepper(ed, "conditions.minPlayers", hint: "Players that must be online for it to start (0 = any).");
        NyarFieldStepper(ed, "conditions.cooldownMinutes", hint: "Minutes before it can run again (0 = no cooldown).");
        NyarFieldStepper(ed, "conditions.chancePercent", hint: "Chance it starts each time it's triggered.");

        if (!kindKnown)
        {
            NyarNote(ed, $"<color={NyarChangedHex}>The server sends only its reason while this definition is invalid, so Raphael can't tell its type. " +
                         "Fix what the reason names (Any other field isn't offered until then), or press Reload values.</color>");
            NyarRefreshAll();
            return;
        }

        switch (load.Kind)
        {
            case NyarEventKind.Empower: BuildNyarEmpowerSection(ed); break;
            case NyarEventKind.Apocalypse: BuildNyarApocalypseSection(ed, id); break;
            case NyarEventKind.Infestation: BuildNyarInfestationSection(ed); break;
            case NyarEventKind.HordeMarch: BuildNyarHordeMarchSection(ed, id); break;
            case NyarEventKind.UnitsForm:
            case NyarEventKind.WaveList: BuildNyarSpawnSections(ed, id); break;
            default:
                AddBodyText(ed, Muted("This event's action has no form fields here — use Any other field below."), Theme.ScaledUI(12));
                break;
        }

        BuildNyarOtherField(ed, id);
        NyarRefreshAll();
    }

    private static string NyarKindText(NyarEventKind k) => k switch
    {
        NyarEventKind.UnitsForm => "Spawn waves — the same units every wave",
        NyarEventKind.WaveList => "Spawn waves — each wave has its own units",
        NyarEventKind.Empower => "Empowerment",
        NyarEventKind.Apocalypse => "Apocalypse",
        NyarEventKind.Infestation => "Infestation — the dead rise at the faction's graveyards and spread",
        NyarEventKind.HordeMarch => "Horde march — one horde marches to a destination, then infests",
        _ => "",
    };

    private void RefreshNyarChangesBar()
    {
        if (_nyarEdBarLabel == null) return;
        int n = _nyarForm.ChangeCount();
        string state = NyarBatchRunner.Busy && NyarBatchRunner.Current == _nyarEdBatch && _nyarEdBatch != null ? $"<color={NyarChangedHex}>{_nyarEdBatch.Progress}</color>"
            : NyarClient.Quarantined ? $"<color={NyarChangedHex}>{NyarBatchRunner.WaitText}</color>"
            : n == 0 ? Muted("No unsaved changes.")
            : $"<color={NyarChangedHex}><b>{n}</b> unsaved change{(n == 1 ? "" : "s")}</color>";
        _nyarEdBarLabel.text = state;
        if (_nyarEdSaveAllBtn != null)
        {
            bool can = n > 0 && NyarCanSend;
            _nyarEdSaveAllBtn.SetEnabled(can);
            if (_nyarEdSaveAllBtn.ButtonText != null)
                _nyarEdSaveAllBtn.ButtonText.text = can ? $"<b>Save all changes ({n})</b>" : Muted(n > 0 ? $"Save all changes ({n})" : "Save all changes");
        }
        _nyarEdUndoBtn?.SetEnabled(n > 0);
    }

    // ---------------------------------------------------------------- empowerment

    private void BuildNyarEmpowerSection(GameObject ed)
    {
        AddSectionHeading(ed, "▸ Empowerment");
        const string field = "action.factions";
        var cur = _nyarForm.Get(field).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        var head = NyarRow(ed, "NyarFacHead");
        NyarCell(head, "Factions", 150, tip: "1-5 factions whose units get stronger (action.factions).");
        NyarCell(head, Muted($"{cur.Count} / {NyarFieldRules.MaxFactions} picked"), 120, flex: true);
        var ui = NyarTail(head, "factions", "Click a faction to add or remove it.", field);
        var known = NyarCatalogFactions();
        foreach (var f in cur.Select(c => c.StartsWith("Faction_", StringComparison.Ordinal) ? c.Substring(8) : c))
            if (!known.Contains(f)) known.Insert(0, f);
        var fchips = new List<(string, bool, string, Action, int)>();
        foreach (var f0 in known)
        {
            string f = f0;
            {
                string full = "Faction_" + f;
                bool on = cur.Contains(full);
                fchips.Add((f, on, on ? $"Remove {f}." : $"Add {f}.", () =>
                {
                    var l = _nyarForm.Get(field).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (!l.Remove(full))
                    {
                        if (l.Count >= NyarFieldRules.MaxFactions) { NyarState.SetError("factions", $"At most {NyarFieldRules.MaxFactions} factions — remove one first."); return; }
                        l.Add(full);
                    }
                    NyarState.ClearError();
                    _nyarForm.Set(field, string.Join(",", l));
                    RedrawNyarEditor();
                }, 0));
            }
        }
        NyarChipRows(ed, "NyarFacRow", "", fchips, 4);
        ui.Msg = NyarNote(ed);
        NyarRefreshUi(ui);
        foreach (var k in NyarForm.StatKeys)
            NyarFieldStepper(ed, "action.stats." + k, hint: k == "physicalPower" ? "×1.0 leaves a stat as it is." : null);
    }

    // ---------------------------------------------------------------- apocalypse

    private void BuildNyarApocalypseSection(GameObject ed, string id)
    {
        AddSectionHeading(ed, "▸ Apocalypse");
        var gr = MakeUrielRow(ed, "NyarEdGoalRow");
        NyarCell(gr, "Goal", 150);
        var goalBtn = AddUrielButton(gr, "NyarEdGoalHere", "Goal here",
            $"Make the spot you stand on the goal the horde marches to (event set {id} action.goal here). Sent at once.",
            () => NyarSendOne("action.goal", "here", "goal", reread: true), 110);
        goalBtn.SetEnabled(NyarCanSend);
        AddNyarRowText(gr, Muted("Stand where the horde should march, then press. Sent at once."));
        NyarTextField(ed, "action.goal.name", 220, "e.g. the village", "What the banners call the goal.");
        NyarFieldStepper(ed, "action.goal.radius");
        NyarFieldStepper(ed, "action.hordes.count");
        NyarFieldStepper(ed, "action.hordes.everyMinutes");
        NyarFieldStepper(ed, "action.hordes.size");
        NyarFieldStepper(ed, "action.roamers.max");
        NyarFieldStepper(ed, "action.marchSpeed");
        AddSectionHeading(ed, "▸ Bosses with the last horde");
        NyarListEditor(ed, "action.bosses", bosses: true, waveList: false, "Bosses", "Up to 3 bosses, 1-3 of each.", allowPools: false);
        NyarHordeNotes(ed, "The apocalypse's units, faction, hotspots and pools aren't settable by command — they live in events.json (edit it, then Reload from disk).");
    }

    // ---------------------------------------------------------------- spawn waves

    private void BuildNyarSpawnSections(GameObject ed, string id)
    {
        var load = _nyarForm.Load;
        AddSectionHeading(ed, "▸ Where and how");
        BuildNyarLocation(ed, id);
        NyarFieldStepper(ed, "action.radius", hint: "How far from the spot the units appear.");
        BuildNyarBehaviour(ed);
        NyarFieldStepper(ed, "action.pursuit",
            hint: "If no player is near the units for this long, they come after the nearest one. Needs Hunt players.",
            advice: () =>
            {
                string p = _nyarForm.Get("action.pursuit");
                if (p.Length > 0 && p != "none" && _nyarForm.Get("action.destination") == "here")
                    return "pursuit and a destination can't both be set — the server refuses it.";
                return p.Length > 0 && p != "none" && NyarHuntRange(_nyarForm.Get("action.behaviour")) == 0
                    ? "pursuit needs Behaviour: Hunt players — the server disables the event otherwise." : null;
            });
        NyarYesNo(ed, "action.loot", "Drops loot", "Whether event units drop loot.");
        NyarYesNo(ed, "action.allowTerritory", "Claimed territory", "Whether units may spawn on claimed castle territory.");

        BuildNyarDestinationCard(ed, id);

        if (load.Kind == NyarEventKind.UnitsForm) BuildNyarUnitsForm(ed);
        else BuildNyarWaveList(ed);

        AddSectionHeading(ed, "▸ Final-wave bosses");
        NyarListEditor(ed, "action.bosses", bosses: true, waveList: false, "Bosses",
            "Up to 3 entries: a boss (1-3 of it) or a roll from a pool (1-5). With a roll, 5 bosses at most in all.");
        if (load.Kind == NyarEventKind.WaveList)
            NyarNote(ed, Muted("These come with the last wave. Don't also give the last wave its own bosses — the server disables the event if both are set."));

        BuildNyarPoolsCard(ed);
    }

    private void BuildNyarLocation(GameObject ed, string id)
    {
        string loc = _nyarForm.Get("location");
        string kind = loc.StartsWith("aroundplayer", StringComparison.Ordinal) ? "aroundplayer" : loc == "region" ? "region" : "";
        string now = _nyarForm.Load.LocationKind switch { "admin" => "the spot where an admin starts it", "point" => "a set spot on the map", _ => "" };
        var ui = NyarChoice(ed, "location", "Location", "Where units spawn (location).",
            new[]
            {
                ("Whole region", "region", "Anywhere in the event's regions (location region)."),
                ("Around a player", "aroundplayer", "Around a random player in range, between a nearest and a farthest distance."),
            }, kind, v =>
            {
                if (v == "region") _nyarForm.Set("location", "region");
                else if (!_nyarForm.Get("location").StartsWith("aroundplayer", StringComparison.Ordinal)) _nyarForm.Set("location", "aroundplayer 20 40");
            }, new[] { "location" },
            kind.Length == 0 && now.Length > 0 ? $"Now: {now}." : null);
        var hr = NyarRow(ed, "NyarEdLocHere");
        NyarCell(hr, "", 150);
        var here = AddUrielButton(hr, "NyarEdLocHereBtn", "Use where I stand", $"Spawn at the spot you stand on (event set {id} location here). Sent at once.",
            () => NyarSendOne("location", "here", "location", reread: true), 140);
        here.SetEnabled(NyarCanSend);
        NyarCell(hr, Muted("sent at once"), 100, flex: true);
        if (kind == "aroundplayer")
        {
            var p = loc.Split(' ');
            string Near() => p.Length == 3 ? p[1] : "";
            string Far() => p.Length == 3 ? p[2] : "";
            void Put(string a, string b) { p = new[] { "aroundplayer", a, b }; _nyarForm.Set("location", $"aroundplayer {a} {b}"); }
            NyarStepper(ed, NyarFieldRules.Spec("location.near"), Near, v => Put(v, Far()), null, "Nearest distance from the player.");
            int hunt = NyarHuntRange(_nyarForm.Get("action.behaviour"));
            NyarStepper(ed, NyarFieldRules.Spec("location.far"), Far, v => Put(Near(), v), null,
                hunt > 0 ? $"Farthest distance — at most the hunt range ({hunt} m)." : "Farthest distance, more than the nearest.");
        }
    }

    private void BuildNyarBehaviour(GameObject ed)
    {
        string b = _nyarForm.Get("action.behaviour");
        string kind = b.StartsWith("hunt ", StringComparison.Ordinal) ? "hunt" : b;
        NyarChoice(ed, "behaviour", "Behaviour", "What the units do (action.behaviour).",
            new[]
            {
                ("Stay put", "none", "Units stay where they spawn."),
                ("Hunt players", "hunt", "Units chase players within a range."),
                ("Roam the region", "regionhunt", "Units hunt the nearest player anywhere in the regions."),
            }, kind, v => _nyarForm.Set("action.behaviour", v == "hunt" ? (b.StartsWith("hunt ", StringComparison.Ordinal) ? b : "hunt 30") : v),
            new[] { "action.behaviour" });
        if (kind == "hunt")
            NyarStepper(ed, NyarFieldRules.Spec("behaviour.range"), () => b.Length > 5 ? b.Substring(5) : "",
                v => { b = "hunt " + v; _nyarForm.Set("action.behaviour", b); }, null, "How far they look for players.");
    }

    private void BuildNyarUnitsForm(GameObject ed)
    {
        AddSectionHeading(ed, "▸ Waves (the same units every wave)");
        NyarFieldStepper(ed, "action.waves");
        NyarFieldStepper(ed, "action.intervalSeconds", hint: "Time from one wave to the next.");
        NyarListEditor(ed, "action.units", bosses: false, waveList: false, "Units per wave",
            "1-10 entries, 1-500 of each. A unit already in the list gets its count raised. " + NyarCapHint);
        BuildNyarLevel(ed);
        foreach (var k in NyarForm.ModifierKeys) NyarFieldStepper(ed, "action.modifiers." + k);

        AddSectionHeading(ed, "▸ Different units per wave");
        NyarNote(ed, Muted("Turn this event into a per-wave list to give each wave its own units, bosses and start time. " +
                           "Wave 1 gets the units above; Units per wave, Waves and Seconds between waves are removed; " +
                           "the level and multipliers above are copied onto wave 1's units."));
        var cr = NyarRow(ed, "NyarEdConvert");
        NyarCell(cr, "", 150);
        string units = _nyarForm.Get("action.units");
        var btn = AddUrielConfirmButton(cr, "NyarEdConvertBtn", "Turn into a per-wave list…",
            "Sends action.waveList.1.units with the units above; the server converts the event.", () =>
            {
                var why = NyarFieldRules.CheckUnitList(_nyarForm.Get("action.units"), true);
                if (why != null) { NyarState.SetError("convert", "Units per wave: " + why); return; }
                // The server copies its saved level / multipliers onto wave 1, so unsaved ones would be lost.
                var pendingMods = NyarForm.UnitsFormKeys.Where(k => k.StartsWith("action.modifiers.", StringComparison.Ordinal) && _nyarForm.IsChanged(k)).ToList();
                if (pendingMods.Count > 0)
                {
                    NyarState.SetError("convert", $"Save or undo your changes to {string.Join(", ", pendingMods.Select(NyarKeyLabel))} first — the server copies its saved values onto wave 1.");
                    return;
                }
                string v = _nyarForm.Get("action.units");
                _nyarEdWave = 1;
                NyarSendOne("action.waveList.1.units", v, "wave 1 units (converts the event)", reread: true);
            }, 200);
        btn.SetEnabled(NyarCanSend && units.Length > 0);
    }

    private void BuildNyarLevel(GameObject ed)
    {
        string lv = _nyarForm.Get("action.modifiers.level"), ld = _nyarForm.Get("action.modifiers.levelDelta");
        string mode = lv.Length > 0 && lv != "none" ? "fixed" : ld.Length > 0 && ld != "none" ? "shift" : "own";
        NyarChoice(ed, "level", "Level", "Units' level: their own, a fixed level, or their own shifted (action.modifiers.level / levelDelta — never both).",
            new[]
            {
                ("Units' own", "own", "Each unit keeps its own level."),
                ("Fixed level", "fixed", "Every unit gets the same level (1-120)."),
                ("Shift level", "shift", "Each unit's own level, raised or lowered by up to 5."),
            }, mode, v =>
            {
                if (v == "fixed") { _nyarForm.Set("action.modifiers.level", lv.Length > 0 && lv != "none" ? lv : "40"); _nyarForm.Set("action.modifiers.levelDelta", "none"); }
                else if (v == "shift") { _nyarForm.Set("action.modifiers.levelDelta", ld.Length > 0 && ld != "none" ? ld : "+2"); _nyarForm.Set("action.modifiers.level", "none"); }
                else { _nyarForm.Set("action.modifiers.level", "none"); _nyarForm.Set("action.modifiers.levelDelta", "none"); }
            }, new[] { "action.modifiers.level", "action.modifiers.levelDelta" });
        var lvSpec = NyarFieldRules.Spec("action.modifiers.level"); lvSpec.NoneAllowed = false;
        var ldSpec = NyarFieldRules.Spec("action.modifiers.levelDelta"); ldSpec.NoneAllowed = false;
        if (mode == "fixed")
            NyarStepper(ed, lvSpec, () => _nyarForm.Get("action.modifiers.level"),
                v => _nyarForm.Set("action.modifiers.level", v), null);
        else if (mode == "shift")
            NyarStepper(ed, ldSpec, () => _nyarForm.Get("action.modifiers.levelDelta"),
                v => _nyarForm.Set("action.modifiers.levelDelta", v), null);
    }

    private void BuildNyarWaveList(GameObject ed)
    {
        var waves = _nyarForm.Load.Waves;
        int count = waves.Count;
        AddSectionHeading(ed, "▸ Waves (each has its own units)");
        var chips = new List<(string, bool, string, Action, int)>();
        foreach (var w in waves)
        {
            int n = w.N;
            bool dirty = _nyarForm.IsChanged($"action.waveList.{n}.units") || _nyarForm.IsChanged($"action.waveList.{n}.bosses") || _nyarForm.IsChanged(NyarForm.StartKey(n));
            chips.Add(($"Wave {n}{(dirty ? " •" : "")}", n == _nyarEdWave, $"Wave {n}: {w.Total} units, {w.Rule}.", () => { _nyarEdWave = n; RedrawNyarEditor(); }, 74));
        }
        if (count < NyarFieldRules.MaxWave)
            chips.Add(($"+ Wave {count + 1}", _nyarEdWave == count + 1, $"Add wave {count + 1}: set its units first.", () => { _nyarEdWave = count + 1; RedrawNyarEditor(); }, 84));
        NyarChipRows(ed, "NyarEdWaveSel", "Edit wave", chips, 5);

        int sw = _nyarEdWave;
        if (sw == count + 1)
        {
            NyarNote(ed, Muted($"New wave {sw}: pick its units and press Set (or Save all). Its bosses and start time can be set once it exists. " +
                               "It starts when the wave before it is cleared until you change that."));
            NyarListEditor(ed, $"action.waveList.{sw}.units", bosses: false, waveList: true, $"Wave {sw} units", "1-10 entries, 1-500 of each; a unit may appear twice.");
            return;
        }
        var info = waves.FirstOrDefault(x => x.N == sw);
        if (info == null) return;
        if (sw > 1)
        {
            string key = NyarForm.StartKey(sw);
            string cur = _nyarForm.Get(key);
            var p = cur.Split(' ');
            string kind = p[0];
            string secs = p.Length > 1 ? p[1] : "";
            NyarChoice(ed, "start" + sw, "Starts", $"When wave {sw} starts (action.waveList.{sw}.afterSeconds / whenCleared).",
                new[]
                {
                    ("When cleared", "cleared", "As soon as every unit of the wave before is dead."),
                    ("After time", "after", "A set time after the wave before, cleared or not."),
                    ("Time or cleared", "afterOrCleared", "After the time, or earlier if the wave before is cleared."),
                }, kind, v => _nyarForm.Set(key, v == "cleared" ? "cleared" : $"{v} {(secs.Length > 0 ? secs : "60")}"), new[] { key },
                kind switch
                {
                    "cleared" => "Starts as soon as every unit of the wave before is dead.",
                    "after" => "Starts a set time after the wave before, cleared or not.",
                    "afterOrCleared" => "Starts after the time, or sooner if the wave before is cleared.",
                    _ => null,
                });
            var secSpec = NyarFieldRules.Spec($"action.waveList.{sw}.afterSeconds");
            secSpec.NoneAllowed = false;   // "no timer" is the When-cleared choice above
            if (kind is "after" or "afterOrCleared")
                NyarStepper(ed, secSpec, () => secs, v => { secs = v; _nyarForm.Set(key, $"{kind} {v}"); }, null,
                    "Seconds after the wave before starts.", label: "Seconds");
        }
        else NyarNote(ed, Muted("Wave 1 starts with the event."));
        if (info.HasExtras)
            NyarNote(ed, $"<color={NyarChangedHex}>Some units in this wave have their own level, multipliers or chance. Saving this wave's units replaces " +
                         "them — set those again under Any other field (action.waveList.N.units.M.…).</color>");
        NyarListEditor(ed, $"action.waveList.{sw}.units", bosses: false, waveList: true, $"Wave {sw} units",
            "1-10 entries, 1-500 of each; a unit may appear twice. " + NyarCapHint);
        NyarListEditor(ed, $"action.waveList.{sw}.bosses", bosses: true, waveList: true, $"Wave {sw} bosses",
            "This wave's own bosses: up to 3 entries, 5 bosses at most in all.");
        if (count > 1)
        {
            var rr = NyarRow(ed, "NyarEdWaveRemove");
            NyarCell(rr, "", 150);
            if (sw == count)
            {
                var rb = AddUrielConfirmButton(rr, "NyarEdWaveRemoveBtn", $"Remove wave {sw}", $"Delete wave {sw} (event set … action.waveList.{sw} none).",
                    () => { _nyarForm.Undo(k => k.StartsWith($"action.waveList.{sw}.", StringComparison.Ordinal)); _nyarEdWave = sw - 1;
                            NyarSendOne($"action.waveList.{sw}", "none", $"remove wave {sw}", reread: true); }, 140, new Color(0.55f, 0.15f, 0.15f, 1f));
                rb.SetEnabled(NyarCanSend);
            }
            else NyarCell(rr, Muted($"Only the last wave (wave {count}) can be removed here, so the others keep their numbers."), 300, flex: true);
        }
    }

    // ---------------------------------------------------------------- unit pools

    /// <summary>The event's pools: the server's (event info) with this session's saves on top; deleted ones dropped.</summary>
    private List<string> NyarPoolNames()
    {
        var names = new List<string>();
        foreach (var kv in _nyarForm.Load.Pools) if (!_nyarSessionPools.TryGetValue(kv.Key, out var s) || s != null) names.Add(kv.Key);
        foreach (var kv in _nyarSessionPools) if (kv.Value != null && !names.Contains(kv.Key)) names.Add(kv.Key);
        return names;
    }

    private List<string> NyarPoolMembers(string name)
    {
        if (_nyarSessionPools.TryGetValue(name, out var s)) return s == null ? null : new List<string>(s);
        foreach (var kv in _nyarForm.Load.Pools) if (kv.Key == name) return new List<string>(kv.Value);
        return null;
    }

    private void NyarPoolSaved(string name, List<string> members)
    {
        _nyarSessionPools[name] = members == null ? null : new List<string>(members);
        if (members == null) { _nyarPoolSel = ""; _nyarPoolMembers = new(); _nyarPoolLoaded = new(); }
        else { _nyarPoolSel = name; _nyarPoolNew = false; _nyarPoolNewName = ""; _nyarPoolLoaded = new List<string>(members); _nyarPoolMembers = new List<string>(members); }
    }

    private void BuildNyarPoolsCard(GameObject ed)
    {
        AddSectionHeading(ed, "▸ Unit pools");
        NyarNote(ed, Muted("A pool is a named mix of up to 30 units. A units entry \"from pool\" draws its units from it at random, and a boss roll " +
                           "picks 1-5 bosses from it. Save a pool before a list names it. The server allows 8 pools per event and refuses " +
                           "deleting one a list still uses."));
        if (!_nyarForm.Load.PoolsComplete)
            NyarNote(ed, $"<color={NyarChangedHex}>A pool's member list couldn't be read whole — press Reload values before editing it.</color>");
        var names = NyarPoolNames();
        var pchips = new List<(string, bool, string, Action, int)>();
        foreach (var n in names)
        {
            string pn = n;
            int k = NyarPoolMembers(pn)?.Count ?? 0;
            pchips.Add(($"{pn} ({k})", !_nyarPoolNew && _nyarPoolSel == pn, $"Edit pool {pn} ({k} units).", () =>
            {
                _nyarPoolSel = pn; _nyarPoolNew = false;
                _nyarPoolLoaded = NyarPoolMembers(pn) ?? new List<string>();
                _nyarPoolMembers = new List<string>(_nyarPoolLoaded);
                RedrawNyarEditor();
            }, 0));
        }
        if (names.Count < NyarFieldRules.MaxPools)
            pchips.Add(("New pool…", _nyarPoolNew, "Start a new pool.", () =>
            {
                _nyarPoolNew = true; _nyarPoolSel = ""; _nyarPoolMembers = new(); _nyarPoolLoaded = new(); RedrawNyarEditor();
            }, 90));
        NyarChipRows(ed, "NyarPoolSel", "Pool", pchips, 4);
        if (!_nyarPoolNew && _nyarPoolSel.Length == 0)
        {
            NyarNote(ed, Muted(names.Count == 0 ? "This event has no pools. Press New pool… to make one." : "Pick a pool to edit it, or press New pool…."));
            return;
        }

        int gen = _nyarEdGen;
        string name = _nyarPoolSel;
        var members = _nyarPoolMembers;
        TextMeshProUGUI note = null;           // set below; the name box's handler refreshes it while typing
        // A server pool read from an incomplete reply: saving would replace it with only the members Raphael saw.
        bool partial = !_nyarPoolNew && !_nyarForm.Load.PoolsComplete && !_nyarSessionPools.ContainsKey(_nyarPoolSel);
        if (_nyarPoolNew)
        {
            var nr = NyarRow(ed, "NyarPoolName");
            NyarCell(nr, "Name", 150, tip: "a-z, 0-9 and -, up to 24 characters.");
            var input = AddNyarInput(nr, "NyarPoolNameIn", "e.g. troops", 160);
            input.Text = _nyarPoolNewName;
            var echo = NyarCell(nr, "", 160, flex: true);
            void Echo(string t)
            {
                string norm = NyarFieldRules.NormalizePoolName(t);
                echo.text = norm.Length == 0 ? Muted("letters, digits and -") : norm == t ? $"<color={NyarSavedHex}>✓ {norm}</color>" : Muted($"saved as <b>{norm}</b>");
            }
            Echo(_nyarPoolNewName);
            input.OnValueChanged += t => { if (gen != _nyarEdGen) return; _nyarPoolNewName = t ?? ""; Echo(_nyarPoolNewName); RefreshPoolNote(); };
        }

        var head = NyarRow(ed, "NyarPoolHead");
        NyarCell(head, $"<b>Members</b>", 150);
        NyarCell(head, Muted($"units {members.Count} / {NyarFieldRules.MaxPoolMembers}"), 140, flex: true);
        bool dirty = !members.SequenceEqual(_nyarPoolLoaded);
        NyarCell(head, dirty ? $"<color={NyarChangedHex}>• changed</color>" : "", 64);
        for (int i = 0; i < members.Count; i++)
        {
            int idx = i;
            var r = NyarRow(ed, "NyarPoolRow" + i);
            NyarCell(r, $"{i + 1}. {NyarUnitDisplay(members[i])}", 260, flex: true, tip: members[i]);
            AddUrielButton(r, "NyarPoolRemove", "Remove", "Take this unit out of the pool (press Save pool to send).",
                () => { _nyarPoolMembers.RemoveAt(idx); RedrawNyarEditor(); }, 70);
        }
        if (members.Count < NyarFieldRules.MaxPoolMembers)
        {
            AddBeelzSearchableField(ed, "NyarPoolPick", "Find a unit to add to the pool…", "", t => UnitCatalog.Search(t, 30), v => _nyarUnitPicks["__pool"] = v ?? "");
            var ar = NyarRow(ed, "NyarPoolAdd");
            NyarCell(ar, "", 150);
            AddUrielButton(ar, "NyarPoolAddBtn", "Add to pool", "Add the unit picked above.", () =>
            {
                string unit = NyarResolveUnit(_nyarUnitPicks.TryGetValue("__pool", out var u) ? u : "");
                if (unit == null) { NyarState.SetError("pool", "Pick a unit from the list under the search box (or type its full CHAR_ name)."); return; }
                if (_nyarPoolMembers.Contains(unit)) { NyarState.SetError("pool", "That unit is already in the pool."); return; }
                _nyarPoolMembers.Add(unit); _nyarUnitPicks.Remove("__pool"); NyarState.ClearError(); RedrawNyarEditor();
            }, 110);
        }
        else NyarNote(ed, Muted($"The pool is full ({NyarFieldRules.MaxPoolMembers} units)."));

        var factions = members.Select(NyarFactionOf).Where(f => f.Length > 0).Distinct().ToList();
        if (factions.Count > 1)
            NyarNote(ed, $"<color={NyarChangedHex}>Check: these units come from {string.Join(", ", factions)}. Units of rival factions may fight each other; " +
                         "the server refuses some mixes.</color>");

        string PoolName() => _nyarPoolNew ? NyarFieldRules.NormalizePoolName(_nyarPoolNewName) : _nyarPoolSel;
        string Problem(string nm) => partial ? "This pool couldn't be read whole, so saving it could drop units. Press Reload values first."
            : !NyarFieldRules.IsPoolName(nm) ? "Give the pool a name (a-z, 0-9 and -)."
            : _nyarPoolNew && names.Contains(nm) ? $"There's already a pool called {nm} — pick it above to edit it."
            : NyarFieldRules.CheckPoolMembers(members);
        note = NyarNote(ed);
        void RefreshPoolNote()
        {
            string nm = PoolName(), problem = Problem(nm);
            if (problem != null) NyarSetNote(note, partial || members.Count > 0 || _nyarPoolNewName.Length > 0 ? $"<color={NyarBadHex}>{problem}</color>" : "");
            else
            {
                int n = NyarPoolSteps(nm, members).Count;
                NyarSetNote(note, n > 1 ? $"<color={NyarChangedHex}>This pool takes {n} commands; if one is refused the pool stops there — fix it and Save again (Save replaces the whole pool).</color>" : "");
            }
        }
        RefreshPoolNote();

        var br = NyarRow(ed, "NyarPoolBtns");
        NyarCell(br, "", 150);
        var save = AddUrielButton(br, "NyarPoolSave", "Save pool", "Send this pool to the server (it replaces the pool's members).", () =>
        {
            string nm = PoolName(), problem = Problem(nm);
            if (problem != null) { NyarState.SetError("pool", problem); return; }
            var steps = NyarPoolSteps(nm, members);
            if (!NyarCanSend) { NyarState.SetError("pool", NyarBatchRunner.Busy ? "Still saving — wait for it to finish." : NyarBatchRunner.WaitText); return; }
            NyarBeginAction();
            if (!NyarState.SupportsTwins) { NyarState.SetError("pool", "Pools need Nyarlathotep api 4 or later."); return; }
            var batch = new NyarBatch(_nyarEditId, nm, true, steps);
            _nyarPoolBatchName = nm; _nyarPoolBatchMembers = new List<string>(members);
            _nyarEdBatch = batch; _nyarEdBatchReread = false;
            if (!NyarBatchRunner.Start(batch)) _nyarEdBatch = null;
            NyarRefreshAll();
        }, 100);
        save.SetEnabled(!partial && members.Count > 0 && (dirty || _nyarPoolNew) && NyarCanSend);
        if (dirty) AddUrielButton(br, "NyarPoolUndo", "Undo", "Put the members back as the server has them.", () => { _nyarPoolMembers = new List<string>(_nyarPoolLoaded); RedrawNyarEditor(); }, 64);
        if (!_nyarPoolNew)
        {
            var del = AddUrielConfirmButton(br, "NyarPoolDelete", "Delete pool", $"Remove pool {name} (event set … action.pools.{name} none). The server refuses while a list uses it.",
                () =>
                {
                    _nyarPoolBatchName = name; _nyarPoolBatchMembers = null;
                    NyarSendPoolDelete(name);
                }, 110, new Color(0.55f, 0.15f, 0.15f, 1f));
            del.SetEnabled(NyarCanSend);
        }
    }

    /// <summary>The steps of a pool save: always the whole member list (chunked under the chat limit, then "+name" for the
    /// rest). A full replace is safe to send again after a lost answer; a lone "+x" / "-x" is not.</summary>
    private List<NyarBatchStep> NyarPoolSteps(string name, List<string> members)
    {
        string field = "action.pools." + name;
        string id = _nyarEditId;
        NyarBatchStep Step(string value, string[] covers, string label)
            => new() { Field = field, Value = value, Command = NyarClient.EventSetCommand(id, field, value), Label = label, FormKey = field, Covers = covers };
        return NyarFieldRules.PoolSaveSteps(members, v => NyarClient.EventSetCommand(id, field, v))
            .Select((s, i) => Step(s.Value, s.Covers, i == 0 ? (s.Covers.Length == 1 ? s.Covers[0] : $"the first {s.Covers.Length} units") : s.Covers[0])).ToList();
    }

    private void NyarSendPoolDelete(string name)
    {
        if (!NyarCanSend) { NyarState.SetError("pool", NyarBatchRunner.Busy ? "Still saving — wait for it to finish." : NyarBatchRunner.WaitText); return; }
        if (!NyarState.SupportsTwins) { NyarState.SetError("pool", "Pools need Nyarlathotep api 4 or later."); return; }
        NyarBeginAction();
        string field = "action.pools." + name;
        var batch = new NyarBatch(_nyarEditId, name, true, new List<NyarBatchStep>
        {
            new() { Field = field, Value = "none", Command = NyarClient.EventSetCommand(_nyarEditId, field, "none"), Label = $"delete pool {name}", FormKey = field },
        });
        _nyarEdBatch = batch; _nyarEdBatchReread = false;
        if (!NyarBatchRunner.Start(batch)) _nyarEdBatch = null;
        NyarRefreshAll();
    }

    // ---------------------------------------------------------------- any other field

    private static readonly System.Text.RegularExpressions.Regex NyarSeg = new(@"\.N(?=\.|$)");

    private static readonly HashSet<string> NyarBoolFields = new(StringComparer.Ordinal)
    {
        "action.loot", "action.allowTerritory", "action.waveList.N.whenCleared", "action.scoreboard", "action.regionLock",
        "action.growth.whenCleared", "action.fanOut", "trigger.shared",
    };

    private void BuildNyarOtherField(GameObject ed, string id)
    {
        AddSectionHeading(ed, "▸ Any other field");
        NyarNote(ed, Muted("Every field the server takes. Pick one; its value control follows the field's type and range."));
        var fr = MakeUrielRow(ed, "NyarEdOtherPick");
        NyarCell(fr, "Field", 150);
        var k = _nyarForm.Load.Kind;
        var offered = NyarFieldRules.FieldsFor(NyarSettableFields, NyarForm.IsSpawnWaves(k), k == NyarEventKind.Apocalypse,
            k == NyarEventKind.Infestation, k == NyarEventKind.HordeMarch, k == NyarEventKind.Empower);
        var fieldOpts = new[] { "Pick a field…" }.Concat(offered).ToArray();
        int picked = Math.Max(0, Array.IndexOf(fieldOpts, _nyarEditOtherField));
        int ddGen = _nyarEdGen;
        var ddObj = UIFactory.CreateDropdown(fr, "NyarEdFieldPicker", out var fieldDd, fieldOpts[picked], 11, i =>
        {
            // Setting the shown value below fires this too: only a real pick, on the live editor, redraws.
            if (ddGen != _nyarEdGen || i <= 0 || i >= fieldOpts.Length || fieldOpts[i] == _nyarEditOtherField) return;
            _nyarEditOtherField = fieldOpts[i];
            _nyarOtherN = _nyarOtherM = "";
            RedrawNyarEditor();
        }, fieldOpts);
        UIFactory.SetLayoutElement(ddObj, minWidth: Theme.ScaledWidth(300), preferredWidth: Theme.ScaledWidth(300), flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        try { fieldDd.value = picked; } catch { }
        try { Raphael.UI.Forms.FormDropdownRegistry.Register(fieldDd); } catch { }
        string g = _nyarEditOtherField;
        if (g.Length == 0) return;

        // N / M / NAME chosen from chips, never typed over.
        int waves = _nyarForm.Load.Waves.Count;
        bool hasN = NyarSeg.IsMatch(g.Replace(".M", "")), hasM = g.Contains(".M.") || g.EndsWith(".M", StringComparison.Ordinal);
        if (hasN)
        {
            var r = NyarRow(ed, "NyarOtherN");
            NyarCell(r, g.StartsWith("action.units.N", StringComparison.Ordinal) ? "Entry" : "Wave", 150);
            int top = g.StartsWith("action.units.N", StringComparison.Ordinal) ? NyarFieldRules.MaxUnitEntries : Math.Min(NyarFieldRules.MaxWave, Math.Max(1, waves + 1));
            for (int n = 1; n <= top; n++)
            {
                string s = n.ToString(CultureInfo.InvariantCulture);
                NyarChip(r, "NyarOtherNChip", s, _nyarOtherN == s, $"Use {s}.", () => { _nyarOtherN = s; RedrawNyarEditor(); }, 34);
            }
            NyarCell(r, "", 10, flex: true);
        }
        if (hasM)
        {
            var r = NyarRow(ed, "NyarOtherM");
            NyarCell(r, "Unit entry", 150);
            for (int n = 1; n <= NyarFieldRules.MaxUnitEntries; n++)
            {
                string s = n.ToString(CultureInfo.InvariantCulture);
                NyarChip(r, "NyarOtherMChip", s, _nyarOtherM == s, $"Entry {s} of the wave's units list.", () => { _nyarOtherM = s; RedrawNyarEditor(); }, 34);
            }
            NyarCell(r, "", 10, flex: true);
        }
        if (g.Contains("NAME"))
        {
            var r = NyarRow(ed, "NyarOtherName");
            NyarCell(r, "Pool", 150);
            foreach (var p in NyarPoolNames())
            {
                string pn = p;
                NyarChip(r, "NyarOtherNameChip", pn, _nyarOtherName == pn, $"Pool {pn}.", () => { _nyarOtherName = pn; RedrawNyarEditor(); });
            }
            var input = AddNyarInput(r, "NyarOtherNameIn", "or type a name", 130);
            input.Text = _nyarOtherName;
            int gen = _nyarEdGen;
            input.OnValueChanged += t => { if (gen == _nyarEdGen) _nyarOtherName = NyarFieldRules.NormalizePoolName(t); };
            AddUrielButton(r, "NyarOtherNameUse", "Use", "Use the typed name.", RedrawNyarEditor, 50);
        }
        string field = g;
        if (hasN) { if (_nyarOtherN.Length == 0) { NyarNote(ed, Muted("Pick the number above.")); return; } field = NyarSeg.Replace(field, "." + _nyarOtherN, 1); }
        if (hasM) { if (_nyarOtherM.Length == 0) { NyarNote(ed, Muted("Pick the unit entry above.")); return; } field = System.Text.RegularExpressions.Regex.Replace(field, @"\.M(?=\.|$)", "." + _nyarOtherM); }
        if (field.Contains("NAME")) { if (!NyarFieldRules.IsPoolName(_nyarOtherName)) { NyarNote(ed, Muted("Pick or type a pool name above.")); return; } field = field.Replace("NAME", _nyarOtherName); }

        if (field is "action.destination" or "action.start")
        {
            NyarNote(ed, Muted(field == "action.start" ? "Use Start here in the Start section above (sent at once)." : "Use Destination here in the Destination section above (sent at once)."));
            return;
        }
        bool family = NyarForm.IsApocalypseFamily(_nyarForm.Load.Kind);
        var spec = NyarFieldRules.Spec(field, family);
        if (spec != null && spec.Kind != NyarKind.Text) { NyarFieldStepper(ed, field, family); return; }
        if (NyarBoolFields.Contains(NyarFieldRules.Generic(field))) { NyarYesNo(ed, field, NyarText(field), field); return; }
        string gen2 = NyarFieldRules.Generic(field);
        if (gen2 == "action.units" || gen2 == "action.waveList.N.units")
        { NyarListEditor(ed, field, false, gen2 != "action.units", "Units", "1-10 entries, 1-500 of each."); return; }
        if (gen2 == "action.bosses" || gen2 == "action.waveList.N.bosses")
        { NyarListEditor(ed, field, true, gen2 != "action.bosses", "Bosses", "Up to 3 entries."); return; }
        if (gen2 == "action.waveList.N")
        {
            NyarNote(ed, Muted("This field only takes none (remove that wave). Use Remove wave in the Waves section."));
            return;
        }
        // Free text, checked by the server.
        int g3 = _nyarEdGen;
        var vr = NyarRow(ed, "NyarOtherValue");
        NyarCell(vr, "Value", 150, tip: field);
        var vin = AddNyarInput(vr, "NyarOtherValueIn", "value", 260);
        vin.Text = _nyarForm.Get(field);
        NyarCell(vr, "", 10, flex: true);
        var ui = NyarTail(vr, field, "The server checks this value; its answer shows at the top.", field);
        vin.OnValueChanged += t => { if (g3 != _nyarEdGen) return; _nyarForm.Set(field, (t ?? "").Trim()); NyarRefreshUi(ui); RefreshNyarChangesBar(); };
        ui.Msg = NyarNote(ed);
        NyarRefreshUi(ui);
    }

    private static string NyarEscape(string s) => (s ?? "").Replace("<", "<​");

    // ---------------------------------------------------------------- ADMIN: UNITS

    private GameObject _nyarUnitsListGo;
    private TextMeshProUGUI _nyarUnitsStatus;
    private GameObject _nyarUnitCountsGo;
    private string _nyarSpawnUnit = "";

    private void BuildNyarAdminUnitsTab(GameObject page)
    {
        EnsureNyarSubscribed();
        page = BeginAdminGate(page);
        AddNyarGateNotes(page);

        var scan = AddCard(page, "NyarUnitsCard");
        AddSectionHeading(scan, "Event units near you");
        AddBodyText(scan,
            "Lists the units Nyarlathotep is tracking around you — event spawns and test spawns — with level, health, time left " +
            $"and map position (x, z). Sends {Mono(".nyar debug here [radius]")}; the radius is 5-100 m. " +
            "<color=#888888>Nyarlathotep can't list every unit on the map yet (api 8). Raphael has asked for a map-wide units read; " +
            "this tab will use it when the server adds it. Until then, go to where an event is running and scan.</color>",
            Theme.ScaledUI(12));
        var r = MakeUrielRow(scan, "NyarUnitsScanRow");
        _nyarNearRadiusInput = AddNyarInput(r, "NyarNearRadius", "radius (5-100 m)", 130);
        AddUrielButton(r, "NyarNearGo", "Scan", "List tracked units around you (.nyar debug here <radius>).", () => NyarScanUnits(Tok(_nyarNearRadiusInput)), 80);
        AddUrielButton(r, "NyarNear100", "Scan 100 m", "The widest scan the server allows (.nyar debug here 100).", () => NyarScanUnits("100"), 110);
        AddNyarErrorLine(scan);
        _nyarUnitsStatus = AddBodyText(scan, "", Theme.ScaledUI(12));
        _nyarUnitsListGo = AddNyarList(scan, "NyarUnitsList");

        AddSpacer(page, 6);
        var counts = AddCard(page, "NyarUnitCountsCard");
        AddSectionHeading(counts, "Units per running event");
        var cr = MakeUrielRow(counts, "NyarUnitCountsTop");
        AddUrielButton(cr, "NyarUnitCountsRefresh", "Refresh", "Re-read the running events (.nyar api status).", () => NyarProtocolService.RefreshStatus(), 90);
        _nyarUnitCountsGo = AddNyarList(counts, "NyarUnitCountsList");

        AddSpacer(page, 6);
        var spawn = AddCard(page, "NyarSpawnCard");
        AddSectionHeading(spawn, "Test spawn");
        AddBodyText(spawn, $"Spawns next to you: {Mono(".nyar spawn <unit> [count] [level] [hp×] [power×]")}. Leave optional fields blank for defaults. " +
            "Test spawns are tracked and show in the scan as <i>manual</i>.", Theme.ScaledUI(12));
        AddBeelzSearchableField(spawn, "NyarSpawnUnit", "Find a unit (name, faction or vblood)…", _nyarSpawnUnit,
            t => UnitCatalog.Search(t, 30), v => _nyarSpawnUnit = v ?? "");
        var sr = MakeUrielRow(spawn, "NyarSpawnRow");
        _nyarSpawnCountInput = AddNyarInput(sr, "NyarSpawnCount", "count", 60);
        _nyarSpawnLevelInput = AddNyarInput(sr, "NyarSpawnLevel", "level", 60);
        _nyarSpawnHpInput    = AddNyarInput(sr, "NyarSpawnHp", "hp×", 55);
        _nyarSpawnPowerInput = AddNyarInput(sr, "NyarSpawnPower", "power×", 60);
        AddUrielButton(sr, "NyarSpawnGo", "Test spawn", "Send the test spawn.", () =>
        {
            string unit = NyarClient.Tok(_nyarSpawnUnit);
            if (unit.Length == 0) { NyarState.SetError("spawn", "Pick a unit to spawn."); return; }
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

        RebuildNyarUnits();
        RebuildNyarUnitCounts();
    }

    private void NyarScanUnits(string radiusText)
    {
        int radius = 0;
        if (radiusText.Length > 0 && (!int.TryParse(radiusText, NumberStyles.Integer, CultureInfo.InvariantCulture, out radius) || radius < 5 || radius > 100))
        { NyarState.SetError("debug here", "Radius must be a whole number from 5 to 100 metres."); return; }
        NyarState.ClearError();
        NyarClient.DebugHere(radius);
    }

    private void RebuildNyarUnits()
    {
        if (_nyarUnitsListGo == null) return;
        ClearChildren(_nyarUnitsListGo);
        if (NyarCapture.UnitsPending) { if (_nyarUnitsStatus != null) _nyarUnitsStatus.text = "<color=#FFD75A>Scanning…</color>"; return; }
        if (NyarCapture.UnitsRadius == 0)
        {
            if (_nyarUnitsStatus != null) _nyarUnitsStatus.text = "<color=#888888>Press Scan to list the units around you.</color>";
            return;
        }
        var units = NyarCapture.Units;
        if (_nyarUnitsStatus != null)
            _nyarUnitsStatus.text = $"<b>{units.Count}</b> tracked unit{(units.Count == 1 ? "" : "s")} within {NyarCapture.UnitsRadius} m, nearest first " +
                                    $"<color=#888888>· read {NyarCapture.UnitsReadAt:HH:mm:ss}</color>";
        if (units.Count > 0)
            AddInfoLabel(_nyarUnitsListGo, "NyarUnitsHead",
                "<color=#888888><b>Unit · event · level · health · time left · position (x, z)</b></color>", FontStyles.Normal, Theme.ScaledUI(11));
        foreach (var u in units)
        {
            string name = NyarText(u.Prefab.StartsWith("CHAR_", StringComparison.Ordinal) ? u.Prefab.Substring(5) : u.Prefab);
            string ev = u.Event == "manual" ? "<i>manual</i>" : NyarText(NyarState.NameOf(u.Event));
            float pct = u.MaxHp > 0 ? (float)u.Hp / u.MaxHp : 0f;
            string hpCol = pct > 0.6f ? "#90EE90" : pct > 0.3f ? "#FFD75A" : "#FF8080";
            string pos = u.HasPos ? $"{u.X}, {u.Z}" : "—";
            string flag = u.Unmarked ? " <color=#FF8080>UNMARKED</color>" : "";
            var l = AddInfoLabel(_nyarUnitsListGo, "NyarUnitRow",
                $"<b>{name}</b> · {ev} · L{u.Level} · <color={hpCol}>{u.Hp}/{u.MaxHp}</color> · {u.Left} · <color=#9CC8FF>{pos}</color>{flag}",
                FontStyles.Normal, Theme.ScaledUI(12));
            TooltipHover.Attach(l.gameObject, u.Raw);
        }
        foreach (var o in NyarCapture.UnitsOther)
            AddBodyText(_nyarUnitsListGo, $"<color=#888888>{NyarEscape(o)}</color>", Theme.ScaledUI(11));
    }

    private void RebuildNyarUnitCounts()
    {
        if (_nyarUnitCountsGo == null) return;
        ClearChildren(_nyarUnitCountsGo);
        if (!NyarState.StatusLoaded) { AddBodyText(_nyarUnitCountsGo, "Press Refresh to load."); return; }
        if (NyarState.ActiveEvents.Count == 0) { AddBodyText(_nyarUnitCountsGo, "No events are running."); return; }
        foreach (var e in NyarState.ActiveEvents)
        {
            string units = e.Units < 0 ? "hidden" : e.Units.ToString(CultureInfo.InvariantCulture);
            string wave = string.IsNullOrEmpty(e.Wave) || e.Wave == "-" ? "" : $" · wave {e.Wave}";
            AddInfoLabel(_nyarUnitCountsGo, "NyarUnitCount",
                $"<b>{NyarText(e.Name)}</b> <color=#888888>({e.Id})</color> · {e.State} · <b>{units}</b> units{wave}",
                FontStyles.Normal, Theme.ScaledUI(12));
        }
    }
}
