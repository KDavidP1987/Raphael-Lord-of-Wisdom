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

// 0.72.0: the Nyarlathotep editor's control kit (PLAN.md §B2): steppers with the server's range printed beside them,
// choice buttons instead of syntax, list rows instead of comma strings, and one "changed / saved / refused" mark plus
// Set per field. Every control reads and writes _nyarForm (NyarForm), never its own copy, so a rebuild of the event
// list (after each saved field) redraws the same state. Ranges and checks come from NyarFieldRules, written once.
public partial class MainPanel
{
    private const string NyarChangedHex = "#FFD75A", NyarSavedHex = "#90EE90", NyarBadHex = "#FF8080", NyarPickHex = "#E6B85C";

    /// <summary>One field row's live parts: re-evaluated after every keystroke without rebuilding.</summary>
    private sealed class NyarFieldUi
    {
        public string[] Keys;
        public string Label;
        public TextMeshProUGUI Mark, Msg;
        public ButtonRef Set;
        public string Hint = "";
        public Func<string> Advice;   // a soft, labelled hint that never blocks (server-only rules)
    }

    private readonly List<NyarFieldUi> _nyarEdUis = new();
    private int _nyarEdGen;           // bumped on every editor build; stale closures check it

    private bool NyarCanSend => !NyarBatchRunner.Busy && !NyarClient.Quarantined;

    // ---------------------------------------------------------------- small pieces

    private static TextMeshProUGUI NyarCell(GameObject row, string text, int width, bool flex = false, string tip = null)
    {
        var lbl = UIFactory.CreateLabel(row, "NyarCell", text, TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lbl.GameObject, minWidth: Theme.ScaledWidth(flex ? Math.Min(width, 40) : width), preferredWidth: Theme.ScaledWidth(width),
            flexibleWidth: flex ? 1 : 0, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        if (!string.IsNullOrEmpty(tip)) TooltipHover.Attach(lbl.GameObject, tip);
        return lbl.TextMesh;
    }

    // A wrapped note line that hides itself when empty (validation, refusal, "couldn't read", hints).
    private static TextMeshProUGUI NyarNote(GameObject parent, string text = "")
    {
        var t = AddBodyText(parent, "", Theme.ScaledUI(11));
        t.text = text;
        t.gameObject.SetActive(text.Length > 0);
        return t;
    }

    private static void NyarSetNote(TextMeshProUGUI t, string text)
    {
        if (t == null) return;
        t.text = text ?? "";
        t.gameObject.SetActive(t.text.Length > 0);
    }

    private static string Muted(string s) => $"<color={Theme.MutedBodyHex}>{s}</color>";

    // A row of the editor (indented a little so field groups read as one block).
    private GameObject NyarRow(GameObject parent, string name) => MakeUrielRow(parent, name);

    private ButtonRef NyarChip(GameObject row, string name, string label, bool picked, string tip, Action click, int width = 0)
    {
        int w = width > 0 ? width : Math.Max(44, 14 + label.Length * 7);
        var b = AddUrielButton(row, name, picked ? $"<color={NyarPickHex}><b>{label}</b></color>" : label, tip, click, w);
        return b;
    }

    /// <summary>Chips laid out <paramref name="perRow"/> to a row under one label, so a long set never runs past the
    /// panel's edge.</summary>
    private void NyarChipRows(GameObject parent, string name, string label, List<(string Label, bool Picked, string Tip, Action Click, int Width)> chips, int perRow)
    {
        for (int i = 0; i < Math.Max(1, chips.Count); i += perRow)
        {
            var row = NyarRow(parent, name + i);
            NyarCell(row, i == 0 ? label : "", 150);
            foreach (var c in chips.Skip(i).Take(perRow)) NyarChip(row, name + "Chip", c.Label, c.Picked, c.Tip, c.Click, c.Width);
            NyarCell(row, "", 10, flex: true);
        }
    }

    // Removes children at once from the layout (inactive) and destroys them next frame, so a redraw never shows the old
    // and new controls stacked for a frame.
    private static void NyarClear(GameObject parent)
    {
        if (parent == null) return;
        var t = parent.transform;
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            var go = t.GetChild(i).gameObject;
            go.SetActive(false);
            UnityEngine.Object.Destroy(go);
        }
    }

    // ---------------------------------------------------------------- mark + Set (one per field row)

    private NyarFieldUi NyarTail(GameObject row, string label, string hint, params string[] keys)
    {
        var ui = new NyarFieldUi { Keys = keys, Label = label, Hint = hint ?? "" };
        ui.Mark = NyarCell(row, "", 64);
        ui.Set = AddUrielButton(row, "NyarSet_" + keys[0], "Set",
            $"Save this change now (event set {_nyarEditId} {string.Join(" / ", keys)} …). The server checks it and the result shows at the top.",
            () => NyarSaveKeys(keys, label), 52);
        _nyarEdUis.Add(ui);
        return ui;
    }

    private void NyarRefreshUi(NyarFieldUi ui)
    {
        if (ui == null || ui.Mark == null) return;
        bool changed = false, saved = false; string problem = null, refused = "", unread = null;
        foreach (var k in ui.Keys)
        {
            if (_nyarForm.IsChanged(k)) { changed = true; problem ??= NyarCheckKey(k, _nyarForm.Get(k)); }
            if (_nyarForm.IsSaved(k)) saved = true;
            if (_nyarForm.Refused(k).Length > 0) refused = _nyarForm.Refused(k);
            if (_nyarForm.IsUnread(k)) unread = k;
        }
        ui.Mark.text = refused.Length > 0 ? $"<color={NyarBadHex}>• refused</color>"
            : changed ? $"<color={NyarChangedHex}>• changed</color>"
            : saved ? $"<color={NyarSavedHex}>✓ saved</color>" : "";
        string advice = ui.Advice?.Invoke();
        string msg = problem != null ? $"<color={NyarBadHex}>{problem}</color>"
            : refused.Length > 0 ? $"<color={NyarBadHex}>The server refused it: {NyarEscape(refused)}</color>"
            : unread != null ? $"<color={NyarChangedHex}>Couldn't read this from the server's reply — pick a value (or press Reload values).</color>"
            : !string.IsNullOrEmpty(advice) ? $"<color={NyarChangedHex}>Check: {advice}</color>"
            : ui.Hint.Length > 0 ? Muted(ui.Hint) : "";
        NyarSetNote(ui.Msg, msg);
        if (ui.Set != null)
        {
            bool can = changed && problem == null && NyarCanSend;
            ui.Set.SetEnabled(can);
            if (ui.Set.ButtonText != null) ui.Set.ButtonText.text = can ? "<b>Set</b>" : Muted("Set");
        }
    }

    private void NyarRefreshAll()
    {
        _nyarEdUis.RemoveAll(u => u.Mark == null);
        foreach (var u in _nyarEdUis) NyarRefreshUi(u);
        RefreshNyarChangesBar();
    }

    // ---------------------------------------------------------------- stepper

    /// <summary>"Label [-] [value] [+] = 15 min · mark Set" over "10-600 s · [30 s] [60 s] [No pursuit]", then the note.
    /// Bound to <paramref name="get"/> / <paramref name="put"/> so a pseudo field (a hunt range inside a behaviour value)
    /// uses the same control; <paramref name="tailKeys"/> null = no mark / Set on this row.</summary>
    private void NyarStepper(GameObject parent, NyarSpec spec, Func<string> get, Action<string> put, string[] tailKeys,
        string hint = null, Func<string> advice = null, string label = null)
    {
        int gen = _nyarEdGen;
        label ??= spec.Label;
        var row = NyarRow(parent, "NyarStep_" + spec.Field);
        NyarCell(row, label, 150, tip: NyarTooltip(spec.Field, spec, fallback: $"{label}."));
        InputFieldRef input = null;
        TextMeshProUGUI echo = null;
        NyarFieldUi ui = null;
        void Changed(string v)
        {
            if (gen != _nyarEdGen) return;
            put(v);
            if (echo != null) echo.text = NyarEcho(spec, v);
            if (ui != null) NyarRefreshUi(ui); else NyarRefreshAll();
            RefreshNyarChangesBar();
        }
        void Push(string v) { if (input == null) return; input.Text = v; Changed(v); }
        AddUrielButton(row, "NyarStepDn", "-", $"Lower by {NyarFieldRules.Fmt(spec, spec.Step)} (never below {NyarFieldRules.Fmt(spec, spec.Min)}).",
            () => Push(NyarFieldRules.Step(spec, input.Text, -1)), 30);
        input = AddNyarInput(row, "NyarStepIn", spec.NoneAllowed ? spec.NoneLabel : NyarFieldRules.Fmt(spec, spec.Min), 76);
        input.Text = get() ?? "";
        input.OnValueChanged += t => Changed((t ?? "").Trim());
        AddUrielButton(row, "NyarStepUp", "+", $"Raise by {NyarFieldRules.Fmt(spec, spec.Step)} (never above {NyarFieldRules.Fmt(spec, spec.Max)}).",
            () => Push(NyarFieldRules.Step(spec, input.Text, +1)), 30);
        echo = NyarCell(row, NyarEcho(spec, input.Text), 110, flex: true);
        if (tailKeys != null) ui = NyarTail(row, label, hint, tailKeys);

        var quick = NyarRow(parent, "NyarStepQuick_" + spec.Field);
        NyarCell(quick, "", 150);
        NyarCell(quick, Muted(spec.RangeText), 96);
        foreach (var p in spec.Presets)
        {
            string pv = NyarFieldRules.Fmt(spec, p);
            NyarChip(quick, "NyarPreset", NyarPresetLabel(spec, p), false, $"Set {label} to {pv}.", () => Push(pv));
        }
        if (spec.NoneAllowed)
            NyarChip(quick, "NyarNone", NyarWordCap(spec.NoneLabel), false, $"{NyarWordCap(spec.NoneLabel)} (sends none).", () => Push("none"));
        var msg = NyarNote(parent);
        if (ui != null) { ui.Msg = msg; ui.Advice = advice; NyarRefreshUi(ui); }
        else if (hint != null) NyarSetNote(msg, Muted(hint));
    }

    private static string NyarEcho(NyarSpec s, string v)
    {
        if ((v ?? "").Trim() == "none") return Muted($"= {s.NoneLabel}");
        var e = NyarFieldRules.Echo(s, v);
        return e.Length > 0 ? Muted(e) : "";
    }

    private static string NyarPresetLabel(NyarSpec s, decimal p)
    {
        if (s.Unit == "s" && p >= 60) return NyarFieldRules.Human((long)p);
        if (s.Unit == "min" && p >= 60) return NyarFieldRules.Human((long)p * 60);
        if (s.Unit == "min" && p == 0) return "none";
        if (s.Unit == "×") return "×" + NyarFieldRules.Fmt(s, p);
        return NyarFieldRules.Fmt(s, p) + (s.Unit.Length > 0 && s.Unit != "×" ? " " + s.Unit : "");
    }

    private static string NyarWordCap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    /// <summary>A stepper bound straight to a form field.</summary>
    private void NyarFieldStepper(GameObject parent, string field, bool apocalypse = false, string hint = null, Func<string> advice = null)
    {
        var spec = NyarFieldRules.Spec(field, apocalypse);
        if (spec == null) return;
        NyarStepper(parent, spec, () => _nyarForm.Get(field), v => _nyarForm.Set(field, v), new[] { field }, hint, advice);
    }

    // ---------------------------------------------------------------- text field

    private void NyarTextField(GameObject parent, string field, int width, string placeholder, string hint = null)
    {
        int gen = _nyarEdGen;
        var spec = NyarFieldRules.Spec(field);
        string label = spec?.Label ?? field;
        var row = NyarRow(parent, "NyarText_" + field);
        NyarCell(row, label, 150, tip: NyarTooltip(field, spec, fallback: $"{label}. No < or > characters."));
        var input = AddNyarInput(row, "NyarTextIn", placeholder, width);
        input.Text = _nyarForm.Get(field);
        NyarCell(row, "", 10, flex: true);
        var ui = NyarTail(row, label, hint, field);
        input.OnValueChanged += t => { if (gen != _nyarEdGen) return; _nyarForm.Set(field, (t ?? "").Trim()); NyarRefreshUi(ui); RefreshNyarChangesBar(); };
        ui.Msg = NyarNote(parent);
        NyarRefreshUi(ui);
    }

    // ---------------------------------------------------------------- choice

    /// <summary>"Label [A] [B] [C] · mark Set": the picked option is gold. Picking redraws the editor (the controls under a
    /// choice depend on it).</summary>
    /// <remarks>0.74.0: the label's tooltip is the field's plain-words help (NyarHelp) with one line per option, keyed by
    /// <paramref name="helpKey"/> (default: the first tail key, else <paramref name="name"/>); <paramref name="tip"/> is
    /// the fallback for a field without help.</remarks>
    private NyarFieldUi NyarChoice(GameObject parent, string name, string label, string tip, (string Label, string Value, string Tip)[] options,
        string picked, Action<string> onPick, string[] tailKeys, string hint = null, string helpKey = null)
    {
        var row = NyarRow(parent, "NyarChoice_" + name);
        string key = helpKey ?? (tailKeys != null && tailKeys.Length > 0 ? tailKeys[0] : name);
        NyarCell(row, label, 150, tip: NyarHelp.Prose(key) != null ? NyarTooltip(key, null, options) : tip);
        foreach (var o in options)
        {
            string v = o.Value;
            NyarChip(row, "NyarOpt", o.Label, v == picked, o.Tip, () => { onPick(v); RedrawNyarEditor(); });
        }
        NyarCell(row, "", 10, flex: true);
        NyarFieldUi ui = null;
        if (tailKeys != null)
        {
            ui = NyarTail(row, label, hint, tailKeys);
            ui.Msg = NyarNote(parent);
            NyarRefreshUi(ui);
        }
        else if (hint != null) NyarNote(parent, Muted(hint));
        return ui;
    }

    /// <summary>A true / false field as [Yes] [No]. <paramref name="disabledReason"/> set: the server can't take the field
    /// yet, so the row shows the label and that reason (muted) instead of the chips.</summary>
    private void NyarYesNo(GameObject parent, string field, string label, string tip, string hint = null, string disabledReason = null)
    {
        if (!string.IsNullOrEmpty(disabledReason))
        {
            var row = NyarRow(parent, "NyarChoiceOff_" + field);
            NyarCell(row, label, 150, tip: NyarTooltip(field, null, fallback: tip));
            NyarCell(row, Muted(disabledReason), 200, flex: true);
            return;
        }
        string cur = _nyarForm.Get(field);
        NyarChoice(parent, field, label, $"{tip} ({field})",
            new[] { ("Yes", "true", $"{label}: yes"), ("No", "false", $"{label}: no") },
            cur, v => _nyarForm.Set(field, v), new[] { field }, hint);
    }

    /// <summary>0.74.0 (PLAN §B2): a field label's tooltip: its plain-words help (NyarHelp), one line per option, the
    /// default and the range, then the raw key muted. A field without help gets <paramref name="fallback"/>.</summary>
    private string NyarTooltip(string field, NyarSpec spec, IEnumerable<(string Label, string Value, string Tip)> options = null, string fallback = null)
        => NyarHelp.Tooltip(field, spec, options, NyarHelp.DefaultOf(field, NyarForm.IsApocalypseFamily(_nyarForm.Load.Kind)), fallback, Theme.MutedBodyHex);

    // ---------------------------------------------------------------- list rows (units, bosses)

    private readonly HashSet<string> _nyarEdTextMode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nyarUnitPicks = new(StringComparer.Ordinal);

    /// <summary>A units or bosses list as rows (name, count stepper, Remove), with live counters, Add from the unit
    /// picker or a pool chip, and "Edit as text" for experts. <paramref name="allowPools"/> false hides pool entries
    /// (an apocalypse takes fixed bosses only).</summary>
    private void NyarListEditor(GameObject parent, string field, bool bosses, bool waveList, string title, string hint, bool allowPools = true)
    {
        int gen = _nyarEdGen;
        string value = _nyarForm.Get(field);
        var rows = NyarFieldRules.ParseEntries(value, bosses);
        bool text = _nyarEdTextMode.Contains(field) || rows == null;
        int maxEntries = bosses ? NyarFieldRules.MaxBossEntries : NyarFieldRules.MaxUnitEntries;
        // 0.74.0: pool ranges and library pools need Nyarlathotep 0.15.0 (api 8 covers 0.14 too, so the plugin version gates).
        bool allowRanges = !bosses && allowPools;
        bool rangesOk = NyarVersion.HasHordePools(NyarState.PluginVersion);

        var head = NyarRow(parent, "NyarListHead_" + field);
        NyarCell(head, $"<b>{title}</b>", 150, tip: NyarTooltip(field, null, fallback: $"{title}."));
        var counter = NyarCell(head, "", 170, flex: true);
        NyarChip(head, "NyarAsText", text ? "Show as rows" : "Edit as text", false,
            text ? "Back to rows (needs a value Raphael can read)." : "Edit the raw value: CHAR_name:count and pool:name:count, comma-separated.",
            () => { if (!_nyarEdTextMode.Remove(field)) _nyarEdTextMode.Add(field); RedrawNyarEditor(); }, 100);
        var ui = NyarTail(head, title, hint, field);

        void Commit(List<NyarEntry> list)
        {
            _nyarForm.Set(field, list.Count == 0 ? (bosses ? "none" : "") : NyarFieldRules.BuildEntries(list, bosses));
            RedrawNyarEditor();
        }

        if (text)
        {
            var tr = NyarRow(parent, "NyarListText_" + field);
            var input = AddNyarInput(tr, "NyarListTextIn", bosses ? "CHAR_X_VBlood:1,pool:elite:1-2 or none" : "CHAR_Bandit_Thug:4,pool:troops:2", 420);
            input.Text = value;
            input.OnValueChanged += t => { if (gen != _nyarEdGen) return; _nyarForm.Set(field, (t ?? "").Trim()); NyarRefreshUi(ui); RefreshNyarChangesBar(); };
            if (rows == null && !_nyarEdTextMode.Contains(field))
                NyarNote(parent, $"<color={NyarChangedHex}>Raphael can't show this value as rows; edit it as text.</color>");
        }
        else
        {
            if (rows.Count == 0)
                NyarNote(parent, Muted(bosses ? "No bosses. Add one below." : "No units yet. Add one below."));
            for (int i = 0; i < rows.Count; i++)
            {
                int idx = i;
                var e = rows[i];
                var r = NyarRow(parent, $"NyarListRow_{field}_{i}");
                string name = e.IsPool
                    ? (bosses ? $"Roll from pool <b>{NyarEscape(e.Pool)}</b>" : $"From pool <b>{NyarEscape(e.Pool)}</b>")
                    : NyarUnitDisplay(e.Unit);
                NyarCell(r, $"{i + 1}. {name}", 200, flex: true, tip: e.IsPool ? $"pool:{e.Pool}" : e.Unit);
                if (bosses && e.IsPool)
                {
                    NyarMiniCount(r, "fewest", e.Min, 1, NyarFieldRules.MaxRollBosses, n => { var l = Clone(rows); l[idx].Min = n; if (l[idx].Max < n) l[idx].Max = n; Commit(l); });
                    NyarMiniCount(r, "most", e.Max, 1, NyarFieldRules.MaxRollBosses, n => { var l = Clone(rows); l[idx].Max = n; if (l[idx].Min > n) l[idx].Min = n; Commit(l); });
                }
                else if (!bosses && e.IsPool && e.Ranged)
                {
                    // 0.74.0: a pool range (Nyarlathotep 0.15.0): a random count between fewest and most, rolled per wave.
                    int cap = NyarFieldRules.MaxUnitCount;
                    NyarMiniCount(r, "fewest", e.Min, 1, cap, n => { var l = Clone(rows); l[idx].Min = n; if (l[idx].Max < n) l[idx].Max = n; l[idx].Count = l[idx].Max; Commit(l); });
                    NyarMiniCount(r, "most", e.Max, 1, cap, n => { var l = Clone(rows); l[idx].Max = n; if (l[idx].Min > n) l[idx].Min = n; l[idx].Count = n; Commit(l); });
                }
                else
                {
                    int max = bosses ? NyarFieldRules.MaxBossCount : NyarFieldRules.MaxUnitCount;
                    NyarMiniCount(r, "×", e.Count, 1, max, n => { var l = Clone(rows); l[idx].Count = n; Commit(l); });
                }
                if (!bosses && e.IsPool && allowRanges)
                {
                    bool on = e.Ranged;
                    if (rangesOk)
                        NyarChip(r, "NyarRangeSwitch", on ? "Count" : "Range", false,
                            on ? "Back to a fixed count (the range's most)." : "Draw a random count between a fewest and a most, rolled again each wave.",
                            () => { var l = Clone(rows); NyarFieldRules.SetRanged(l[idx], !on); Commit(l); }, 60);
                }
                AddUrielButton(r, "NyarListRemove", "Remove", "Take this entry out of the list.", () => { var l = Clone(rows); l.RemoveAt(idx); Commit(l); }, 70);
            }

            if (allowRanges && !rangesOk && rows.Any(x => x.IsPool))
                NyarNote(parent, Muted("A pool entry's min-max range " + NyarVersion.NeedsText(NyarState.PluginVersion) + "."));

            // Add: unit picker + Add, then pool chips.
            bool full = rows.Count >= maxEntries;
            string pickKey = field;
            if (!full)
            {
                AddBeelzSearchableField(parent, "NyarPick_" + field, bosses ? "Find a boss to add (type vblood to list them)…" : "Find a unit to add (name or faction)…",
                    "", t => UnitCatalog.Search(t, 30), v => _nyarUnitPicks[pickKey] = v ?? "",
                    // 0.74.0: browse the unit reference; the pick lands in this search box, then Add as usual.
                    (row, input) => AddCatalogBrowseButton(row, input, parent, Raphael.Resources.BrowseKinds.Unit,
                        Raphael.Resources.BrowseFormat.Name, startFilter: bosses ? "vblood" : null,
                        fieldLabel: bosses ? "Nyarlathotep boss list" : "Nyarlathotep unit list"));
                var ar = NyarRow(parent, "NyarListAdd_" + field);
                NyarCell(ar, "", 150);
                AddUrielButton(ar, "NyarListAddUnit", bosses ? "Add boss" : "Add unit", "Add the unit picked above (count 1; change it in its row).", () =>
                {
                    string unit = NyarResolveUnit(_nyarUnitPicks.TryGetValue(pickKey, out var u) ? u : "");
                    if (unit == null) { NyarState.SetError("unit", "Pick a unit from the list under the search box (or type its full CHAR_ name)."); return; }
                    var l = Clone(rows);
                    NyarFieldRules.AddUnit(l, unit, 1, bosses ? NyarFieldRules.MaxBossCount : NyarFieldRules.MaxUnitCount, mergeRepeats: bosses || !waveList);
                    _nyarUnitPicks.Remove(pickKey);
                    NyarState.ClearError();
                    Commit(l);
                }, 100);
                NyarCell(ar, "", 10, flex: true);
                if (allowPools)
                {
                    var pools = NyarPoolNames();
                    if (pools.Count == 0)
                        NyarNote(parent, Muted(rangesOk
                            ? "This event has no pools of its own. Make one below, or type the name of a library pool (see Pools library)."
                            : "No pools yet — make one under Unit pools to add a random mix."));
                    if (rangesOk && !bosses)
                    {
                        // A library pool (pools.json) by name: read on press, checked for shape only (the server checks it exists).
                        var lr = NyarRow(parent, "NyarListAddLib_" + field);
                        NyarCell(lr, Muted("or a library pool name"), 150);
                        var lin = AddNyarInput(lr, "NyarListLibIn", "e.g. undead-units", 160, NyarHelpInput("library.poolname"));
                        AddUrielButton(lr, "NyarListAddLib", bosses ? "Add roll" : "Add pool", "Add an entry drawn from that library pool (count 1; change it in its row).", () =>
                        {
                            string pn = NyarFieldRules.NormalizePoolName(lin?.Text ?? "");
                            if (!NyarFieldRules.IsPoolName(pn)) { NyarState.SetError("pool", "Type a pool name: a-z, 0-9 and -, up to 24 characters."); return; }
                            NyarState.ClearError();
                            var l = Clone(rows); NyarFieldRules.AddPool(l, pn, 1, 1, 1, bosses, mergeRepeats: !waveList); Commit(l);
                        }, 90);
                        NyarCell(lr, "", 10, flex: true);
                    }
                    if (pools.Count > 0)
                    {
                        var chips = new List<(string, bool, string, Action, int)>();
                        foreach (var p in pools)
                        {
                            string pn = p;
                            chips.Add((pn, false,
                                bosses ? $"Add a roll of 1-1 bosses from pool {pn} (raise it in its row)." : $"Add 1 unit drawn from pool {pn} (raise it in its row).",
                                () => { var l = Clone(rows); NyarFieldRules.AddPool(l, pn, 1, 1, 1, bosses, mergeRepeats: !waveList); Commit(l); }, 0));
                        }
                        NyarChipRows(parent, "NyarListAddPool_" + field, Muted(bosses ? "Add a roll from" : "Add from pool"), chips, 4);
                    }
                }
            }
            else NyarNote(parent, Muted($"The list is full ({maxEntries} entries) — remove one to add another."));
        }

        // counters
        if (rows != null && !text)
        {
            var parts = new List<string> { $"entries {rows.Count} / {maxEntries}" };
            if (bosses)
            {
                if (NyarFieldRules.BossTotalApplies(rows, waveList)) parts.Add($"bosses {NyarFieldRules.MostBosses(rows)} / {NyarFieldRules.MaxWaveBosses}");
            }
            else if (rows.Any(x => x.IsPool)) parts.Add($"units {NyarFieldRules.TotalUnits(rows)} / {NyarFieldRules.MaxWaveUnits}");
            else parts.Add($"{NyarFieldRules.TotalUnits(rows)} units");
            counter.text = Muted(string.Join(" · ", parts));
        }
        ui.Msg = NyarNote(parent);
        NyarRefreshUi(ui);
    }

    private static List<NyarEntry> Clone(List<NyarEntry> rows) => rows.Select(r => r.Clone()).ToList();

    // "[-] n [+]" for a count inside a list row; every press commits (and redraws). A count that goes past 99 (units, up
    // to 500 since 0.73.0) also gets "-10" / "+10" and a wider cell.
    private void NyarMiniCount(GameObject row, string label, int value, int min, int max, Action<int> commit)
    {
        bool big = max >= 100;
        if (label.Length > 0) NyarCell(row, Muted(label), label.Length > 2 ? 44 : 14);
        if (big)
        {
            var dn10 = AddUrielButton(row, "NyarCntDn10", "-10", $"Ten less (at least {min}).", () => commit(Math.Max(min, value - 10)), 34);
            dn10.SetEnabled(value > min);
        }
        var dn = AddUrielButton(row, "NyarCntDn", "-", $"One less (at least {min}).", () => commit(Math.Max(min, value - 1)), 28);
        dn.SetEnabled(value > min);
        NyarCell(row, $"<b>{value}</b>", big ? 34 : 26);
        var up = AddUrielButton(row, "NyarCntUp", "+", $"One more (at most {max}).", () => commit(Math.Min(max, value + 1)), 28);
        up.SetEnabled(value < max);
        if (big)
        {
            var up10 = AddUrielButton(row, "NyarCntUp10", "+10", $"Ten more (at most {max}).", () => commit(Math.Min(max, value + 10)), 34);
            up10.SetEnabled(value < max);
        }
    }

    // ---------------------------------------------------------------- unit names

    private static Dictionary<string, string> _nyarUnitFaction;

    private static string NyarFactionOf(string unit)
    {
        if (_nyarUnitFaction == null)
        {
            _nyarUnitFaction = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var u in UnitCatalog.All) _nyarUnitFaction[u.Name] = u.Faction;
        }
        return _nyarUnitFaction.TryGetValue(unit ?? "", out var f) ? f : "";
    }

    /// <summary>The unit the picker holds: a catalog name (picked from the list, or typed in full), or any full CHAR_
    /// name typed by hand (a modded unit the catalog lacks). A half-typed search word is not a unit: null.</summary>
    private static string NyarResolveUnit(string picked)
    {
        string u = (picked ?? "").Trim();
        if (u.Length == 0) return null;
        if (NyarFactionOf(u).Length > 0) return u;
        if (NyarFactionOf("CHAR_" + u).Length > 0) return "CHAR_" + u;
        return u.StartsWith("CHAR_", StringComparison.Ordinal) && NyarFieldRules.IsUnitName(u) ? u : null;
    }

    private static string NyarUnitDisplay(string unit)
    {
        string s = (unit ?? "").StartsWith("CHAR_", StringComparison.Ordinal) ? unit.Substring(5) : unit ?? "";
        string f = NyarFactionOf(unit);
        return NyarEscape(s.Replace('_', ' ')) + (f.Length > 0 ? $" {Muted("(" + f + ")")}" : "");
    }

    /// <summary>The factions of the unit catalog, most units first (the empower picker's chips).</summary>
    private static List<string> NyarCatalogFactions()
        => UnitCatalog.All.GroupBy(u => u.Faction).Where(g => g.Count() >= 5 && !g.Key.Contains("_"))
            .OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
}
