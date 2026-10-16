using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Raphael.Resources;
using Raphael.Services.Nyar;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using TMPro;
using UnityEngine;

namespace Raphael.UI.ModContent;

// 0.71.0: the Nyarlathotep event editor (opened by an event's Edit… button, inline under that event) and the
// Admin: Units tab. Api 8 has no field read, so the editor sends `.nyar event info <id>`, shows the reply
// (NyarCapture) and pre-fills what it can parse. Every field has its own Set, which sends
// `event set <id> <field> <value>` (the api 4 wire twin; the result line shows the server's answer).
// Unit fields get a type-to-search picker over the game's unit list (Resources/UnitCatalog).
public partial class MainPanel
{
    private string _nyarEditId = "";
    private readonly Dictionary<string, string> _nyarEditVals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _nyarEditTouched = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InputFieldRef> _nyarEditInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nyarUnitPicks = new(StringComparer.Ordinal);
    private TextMeshProUGUI _nyarEditInfoLabel;
    private string _nyarEditOtherField = "";

    private static readonly Regex NyarUnitToken = new(@"^CHAR_[A-Za-z0-9_]{1,91}$", RegexOptions.Compiled);

    private void OpenNyarEditor(string id)
    {
        bool closing = _nyarEditId == id;
        _nyarEditId = closing ? "" : id;
        _nyarEditVals.Clear(); _nyarEditTouched.Clear(); _nyarEditInputs.Clear(); _nyarUnitPicks.Clear();
        _nyarEditOtherField = "";
        if (!closing) NyarClient.EventInfo(id);   // NyarCapture fills the "current definition" box + pre-fills
        RebuildNyarDefs();
    }

    private void OnNyarInfoCaptured()
    {
        if (_nyarEditId.Length == 0 || !string.Equals(NyarCapture.InfoId, _nyarEditId, StringComparison.OrdinalIgnoreCase)) return;
        PrefillNyarEditor();
        RebuildNyarDefs();
    }

    // ---------------------------------------------------------------- inline editor

    private void BuildNyarInlineEditor(GameObject parent, NyarDef d)
    {
        _nyarEditInputs.Clear();
        string id = d.Id;
        var ed = AddCard(parent, "NyarEditor", new Color(0.16f, 0.12f, 0.22f, 0.9f), padding: 8, innerSpacing: 3);

        var head = MakeUrielRow(ed, "NyarEdHead");
        AddNyarRowText(head, $"<b>Editing {NyarText(d.Name)}</b> <color=#888888>({id})</color>");
        AddUrielButton(head, "NyarEdReload", "Reload values", $"Read {id}'s current definition again (.nyar event info {id}).",
            () => NyarClient.EventInfo(id), 120);
        AddUrielButton(head, "NyarEdClose", "Close", "Close the editor.", () => OpenNyarEditor(id), 70);
        AddNyarResultLine(ed);
        AddNyarErrorLine(ed);

        AddSectionHeading(ed, "▸ Current definition");
        bool haveInfo = string.Equals(NyarCapture.InfoId, id, StringComparison.OrdinalIgnoreCase) && NyarCapture.InfoLines.Count > 0;
        _nyarEditInfoLabel = AddBodyText(ed, haveInfo
            ? $"<color=#CCCCCC>{string.Join("\n", NyarCapture.InfoLines.Select(NyarEscape))}</color>"
            : "<color=#888888>Reading the definition from the server…  (it also shows in chat)</color>", Theme.ScaledUI(11));
        AddBodyText(ed, "Fields below are filled from this where Raphael can read them. Change one, then press its <b>Set</b>. " +
            "The server checks each value and the result shows above. New and copied events start disabled — Enable them from the list.",
            Theme.ScaledUI(11));

        bool apocalypse = haveInfo && NyarCapture.InfoMentions("action: apocalypse");
        bool empower = string.Equals(d.Action, "empower", StringComparison.OrdinalIgnoreCase);

        AddSectionHeading(ed, "▸ Basics");
        NyarEditField(ed, id, "Name", "name", "display name", "The name players see in banners (name).");
        NyarEditField(ed, id, "Duration (seconds)", "durationSeconds", "e.g. 600",
            apocalypse ? "How long the event runs, 600-604800 s (durationSeconds)." : "How long the event runs, in seconds (durationSeconds).");

        AddSectionHeading(ed, "▸ Conditions");
        NyarEditField(ed, id, "Min players online", "conditions.minPlayers", "0 = any", "Players that must be online for it to start (conditions.minPlayers).");
        NyarEditField(ed, id, "Cooldown (minutes)", "conditions.cooldownMinutes", "0 = none", "Minutes before it can run again (conditions.cooldownMinutes).");
        NyarEditField(ed, id, "Chance (%)", "conditions.chancePercent", "1-100", "Chance it starts when triggered (conditions.chancePercent).");

        if (empower)
        {
            AddSectionHeading(ed, "▸ Empowerment");
            NyarEditField(ed, id, "Factions", "action.factions", "Faction_Bandits,…", "Factions to empower, comma-separated (action.factions).", 220);
            NyarEditField(ed, id, "Physical power ×", "action.stats.physicalPower", "e.g. 1.5", "action.stats.physicalPower");
            NyarEditField(ed, id, "Spell power ×", "action.stats.spellPower", "e.g. 1.5", "action.stats.spellPower");
            NyarEditField(ed, id, "Max health ×", "action.stats.maxHealth", "e.g. 2", "action.stats.maxHealth");
            NyarEditField(ed, id, "Attack speed ×", "action.stats.attackSpeed", "e.g. 1.2", "action.stats.attackSpeed");
            NyarEditField(ed, id, "Move speed ×", "action.stats.moveSpeed", "e.g. 1.2", "action.stats.moveSpeed");
        }
        else if (apocalypse)
        {
            AddSectionHeading(ed, "▸ Apocalypse");
            var gr = MakeUrielRow(ed, "NyarEdGoalRow");
            AddUrielButton(gr, "NyarEdGoalHere", "Goal here",
                $"Make the spot you stand on the goal the horde marches to (event set {id} action.goal here). " +
                "Needed before Start while the goal is \"the starting admin\" — Nyarlathotep can't start that yet.",
                () => { NyarBeginAction(); NyarClient.EventSetGoalHere(id); }, 110);
            AddNyarRowText(gr, "<color=#888888>Stand where the horde should march, then press.</color>");
            NyarEditField(ed, id, "Goal radius (m)", "action.goal.radius", "10-100", "action.goal.radius");
            NyarEditField(ed, id, "Goal name", "action.goal.name", "e.g. the village", "What the banners call the goal (action.goal.name).");
            NyarEditField(ed, id, "Hordes", "action.hordes.count", "1-10", "action.hordes.count");
            NyarEditField(ed, id, "Horde every (min)", "action.hordes.everyMinutes", "5-1440", "action.hordes.everyMinutes");
            NyarEditField(ed, id, "Horde size", "action.hordes.size", "5-50", "action.hordes.size");
            NyarEditField(ed, id, "Max roamers", "action.roamers.max", "1-100", "action.roamers.max");
            NyarEditField(ed, id, "March speed (m/s)", "action.marchSpeed", "0.5-5", "action.marchSpeed");
            AddSectionHeading(ed, "Bosses (last horde)");
            NyarEditField(ed, id, "Bosses", "action.bosses", "CHAR_X_VBlood:1 or none", "Up to 3 bosses, CHAR_name[:1-3] comma-separated, or none (action.bosses).", 260);
            NyarUnitListBuilder(ed, "bosses", "action.bosses", 3, vbloodFirst: true);
            AddBodyText(ed, "<color=#888888>The apocalypse's units, faction and hotspots aren't settable by command — they live in events.json (edit it, then Reload from disk).</color>",
                Theme.ScaledUI(11));
        }
        else
        {
            AddSectionHeading(ed, "▸ Where");
            var lr = MakeUrielRow(ed, "NyarEdLocRow");
            AddUrielButton(lr, "NyarEdLocHere", "Location here", $"Spawn at the spot you stand on (event set {id} location here).",
                () => { NyarBeginAction(); NyarClient.EventSetLocationHere(id); }, 120);
            AddUrielButton(lr, "NyarEdLocRegion", "Whole region", $"Spawn anywhere in the event's regions (event set {id} location region).",
                () => { NyarBeginAction(); NyarClient.EventSet(id, "location", "region"); }, 120);
            NyarEditField(ed, id, "Around a player (m)", "location", "aroundplayer 20 40",
                "Spawn around a random player, MIN to MAX metres away: type aroundplayer MIN MAX (location).", 180);

            AddSectionHeading(ed, "▸ Spawn waves");
            NyarEditField(ed, id, "Waves", "action.waves", "e.g. 3", "Number of waves (action.waves).");
            NyarEditField(ed, id, "Seconds between waves", "action.intervalSeconds", "e.g. 60", "action.intervalSeconds");
            NyarEditField(ed, id, "Spawn radius (m)", "action.radius", "e.g. 10", "How far from the spot units appear (action.radius).");
            NyarEditField(ed, id, "Units per wave", "action.units", "CHAR_Bandit_Thug:4,…",
                "1-10 entries of CHAR_name:count (count 1-50), comma-separated. Build it with the picker below (action.units).", 260);
            NyarUnitListBuilder(ed, "units", "action.units", 50, vbloodFirst: false);
            NyarEditField(ed, id, "Behaviour", "action.behaviour", "none / hunt 30 / regionhunt",
                "none = stay put; hunt R = chase players within R m; regionhunt = roam the region hunting (action.behaviour).", 180);
            NyarEditField(ed, id, "Level", "action.modifiers.level", "e.g. 40", "Fixed unit level (action.modifiers.level).");
            NyarEditField(ed, id, "Level shift", "action.modifiers.levelDelta", "e.g. +5", "Added to each unit's own level (action.modifiers.levelDelta).");
            NyarEditField(ed, id, "Health ×", "action.modifiers.maxHealth", "e.g. 1.5", "action.modifiers.maxHealth");
            NyarEditField(ed, id, "Power ×", "action.modifiers.power", "e.g. 1.2", "action.modifiers.power");
            NyarEditField(ed, id, "Bosses", "action.bosses", "CHAR_X_VBlood:1 or none", "Up to 3 bosses with the last wave (action.bosses).", 260);
            NyarUnitListBuilder(ed, "bosses", "action.bosses", 3, vbloodFirst: true);
        }

        // Anything else from the full list.
        AddSectionHeading(ed, "▸ Any other field");
        var fr = MakeUrielRow(ed, "NyarEdOtherPick");
        var fieldOpts = new[] { "Pick a field…" }.Concat(NyarSettableFields).ToArray();
        var ddObj = UIFactory.CreateDropdown(fr, "NyarEdFieldPicker", out var fieldDd, fieldOpts[0], 11, i =>
        {
            if (i <= 0 || i >= fieldOpts.Length) return;
            _nyarEditOtherField = fieldOpts[i];
            if (_nyarEditInputs.TryGetValue("__otherField", out var f) && f != null) f.Text = fieldOpts[i];
        }, fieldOpts);
        UIFactory.SetLayoutElement(ddObj, minWidth: Theme.ScaledWidth(260), preferredWidth: Theme.ScaledWidth(260), flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        try { Raphael.UI.Forms.FormDropdownRegistry.Register(fieldDd); } catch { }
        var or = MakeUrielRow(ed, "NyarEdOtherRow");
        var fieldIn = AddNyarInput(or, "NyarEdOtherField", "field (replace N / M)", 200);
        fieldIn.Text = _nyarEditOtherField;
        fieldIn.OnValueChanged += t => _nyarEditOtherField = t ?? "";
        _nyarEditInputs["__otherField"] = fieldIn;
        var valIn = AddNyarInput(or, "NyarEdOtherValue", "value", 200);
        if (_nyarEditVals.TryGetValue("__other", out var ov)) valIn.Text = ov;
        valIn.OnValueChanged += t => _nyarEditVals["__other"] = t ?? "";
        _nyarEditInputs["__other"] = valIn;
        AddUrielButton(or, "NyarEdOtherSet", "Set", "Send event set <id> <field> <value>.", () =>
        {
            string field = (_nyarEditOtherField ?? "").Trim();
            string val = _nyarEditVals.TryGetValue("__other", out var v) ? v.Trim() : "";
            if (field.Length == 0 || val.Length == 0) { NyarState.SetError("event set", "Pick a field and type a value."); return; }
            if (field.Contains(".N") || field.Contains(".M")) { NyarState.SetError("event set", "Replace N / M in the field with a number."); return; }
            NyarBeginAction(); NyarClient.EventSet(id, field, val);
            if (!NyarState.SupportsTwins) NyarProtocolService.RefreshDefs();
        }, 56);
        AddBodyText(ed, "<color=#888888>For a unit field (action.waveList.N.units, trigger.bosses) add units to the value with this picker:</color>", Theme.ScaledUI(11));
        NyarUnitListBuilder(ed, "other", "__other", 50, vbloodFirst: false);
    }

    // One "label  [value]  Set" row bound to an event field.
    private void NyarEditField(GameObject parent, string id, string label, string field, string placeholder, string tip, int width = 150)
    {
        var row = MakeUrielRow(parent, "NyarEd_" + field);
        var lbl = UIFactory.CreateLabel(row, "NyarEdLbl", label, TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lbl.GameObject, minWidth: Theme.ScaledWidth(160), preferredWidth: Theme.ScaledWidth(160), flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        TooltipHover.Attach(lbl.GameObject, tip);
        var input = AddNyarInput(row, "NyarEdIn_" + field, placeholder, width);
        if (_nyarEditVals.TryGetValue(field, out var v)) input.Text = v;
        input.OnValueChanged += t => { _nyarEditVals[field] = t ?? ""; _nyarEditTouched.Add(field); };
        _nyarEditInputs[field] = input;
        AddUrielButton(row, "NyarEdSet_" + field, "Set", $"{tip}\nSends: event set {id} {field} <value>", () =>
        {
            string val = _nyarEditVals.TryGetValue(field, out var x) ? x.Trim() : "";
            if (val.Length == 0) { NyarState.SetError("event set", $"Type a value for {label} first."); return; }
            NyarBeginAction(); NyarClient.EventSet(id, field, val);
            if (!NyarState.SupportsTwins) NyarProtocolService.RefreshDefs();
        }, 56);
    }

    // "Find a unit…" picker + count + Add: appends CHAR_name:count to the bound value (comma list).
    private void NyarUnitListBuilder(GameObject parent, string key, string valueField, int maxCount, bool vbloodFirst)
    {
        AddBeelzSearchableField(parent, "NyarEdPick_" + key, vbloodFirst ? "Find a boss (type vblood to list them)…" : "Find a unit to add (name or faction)…",
            "", t => UnitCatalog.Search(t, 30), v => _nyarUnitPicks[key] = v ?? "");
        var r = MakeUrielRow(parent, "NyarEdPickRow_" + key);
        var cnt = AddNyarInput(r, "NyarEdPickCount_" + key, $"count (1-{maxCount})", 100);
        AddUrielButton(r, "NyarEdPickAdd_" + key, "Add to list", "Append the picked unit with this count to the value above.", () =>
        {
            string unit = _nyarUnitPicks.TryGetValue(key, out var u) ? u.Trim() : "";
            if (!unit.StartsWith("CHAR_", StringComparison.OrdinalIgnoreCase) && unit.Length > 0) unit = "CHAR_" + unit;
            if (!NyarUnitToken.IsMatch(unit)) { NyarState.SetError("unit", "Pick a unit from the list (CHAR_…)."); return; }
            int n = 1;
            string c = Tok(cnt);
            if (c.Length > 0 && (!int.TryParse(c, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1 || n > maxCount))
            { NyarState.SetError("unit", $"Count must be 1-{maxCount}."); return; }
            string cur = _nyarEditVals.TryGetValue(valueField, out var x) ? x.Trim() : "";
            if (cur.Equals("none", StringComparison.OrdinalIgnoreCase)) cur = "";
            string next = (cur.Length > 0 ? cur.TrimEnd(',') + "," : "") + $"{unit}:{n}";
            _nyarEditVals[valueField] = next; _nyarEditTouched.Add(valueField);
            if (_nyarEditInputs.TryGetValue(valueField, out var f) && f != null) f.Text = next;
            NyarState.ClearError();
        }, 100);
        AddUrielButton(r, "NyarEdPickClear_" + key, "Clear list", "Empty the value above.", () =>
        {
            _nyarEditVals[valueField] = ""; _nyarEditTouched.Add(valueField);
            if (_nyarEditInputs.TryGetValue(valueField, out var f) && f != null) f.Text = "";
        }, 90);
    }

    // Fill untouched fields from the captured `event info` reply (best effort — the reply is human text).
    private void PrefillNyarEditor()
    {
        var lines = NyarCapture.InfoLines;
        if (lines.Count == 0) return;
        string all = string.Join("\n", lines);
        string Line(string prefix) => lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal)) ?? "";
        string M(string text, string rx) { var m = Regex.Match(text, rx); return m.Success ? m.Groups[1].Value : ""; }
        void Put(string field, string value)
        {
            if (string.IsNullOrEmpty(value) || _nyarEditTouched.Contains(field)) return;
            _nyarEditVals[field] = value;
        }

        Put("name", NyarCapture.InfoName());
        Put("durationSeconds", M(lines[0], @"\bduration (\d+)s"));
        string cond = Line("conditions:");
        Put("conditions.minPlayers", M(cond, @"minPlayers (\d+)"));
        Put("conditions.cooldownMinutes", M(cond, @"cooldown (\d+) min"));
        Put("conditions.chancePercent", M(cond, @"chance (\d+)%"));

        string act = Line("action:");
        Put("action.waves", M(act, @"(\d+) waves?"));
        Put("action.intervalSeconds", M(act, @"every (\d+)s"));
        Put("action.radius", M(act, @"radius (\d+)"));
        Put("action.units", NyarCapture.InfoUnitsSetForm());
        Put("action.marchSpeed", M(act, @"march ([\d.]+)"));

        string goal = Line("goal:");
        Put("action.goal.radius", M(goal, @"radius (\d+)"));
        string hordes = Line("hordes:");
        Put("action.hordes.count", M(hordes, @"hordes: (\d+)"));
        Put("action.hordes.everyMinutes", M(hordes, @"every (\d+) min"));
        Put("action.hordes.size", M(hordes, @"size (\d+)"));
        Put("action.roamers.max", M(Line("roamers:"), @"max (\d+)"));
        string boss = M(all, @"last horde adds: (CHAR_[A-Za-z0-9_]+) x(\d)");
        if (boss.Length > 0) Put("action.bosses", $"{boss}:{M(all, @"last horde adds: CHAR_[A-Za-z0-9_]+ x(\d)")}");
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
