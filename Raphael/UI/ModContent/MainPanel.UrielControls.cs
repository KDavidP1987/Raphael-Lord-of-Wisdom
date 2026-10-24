using System;
using System.Collections.Generic;
using System.Text;
using Raphael.Services.Uriel;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.UI.ModContent.Data;
using Raphael.UI.Framework.CustomLib.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Raphael.UI.ModContent;

// 0.73.0: Uriel 0.22.0 admin controls (PLAN-URIEL022.md §4-5).
//   • Admin: Controls (the old Admin: Config, same PanelType): which features are on, and the four buttons that undo
//     what Uriel did (Stop all events, Remove all units, Remove all objects, Restore vanilla).
//   • Feature gates: a control whose feature is off is greyed in place, with the reason. Nothing is hidden.
// RAIL-SAFE: nothing here calls a rail function or raises a presence event; it listens to UrielFeatureState.Changed
// only. Every command goes to chat visibly, and its reply stays in chat.
public partial class MainPanel
{
    // ---------------------------------------------------------------------------------------------- feature gates
    // Per page (keyed by the tab), so a page's rebuild replaces only its own entries.
    private readonly Dictionary<PanelType, List<(Selectable Sel, UrielFeature Feature)>> _urielGateTargets = new();
    private readonly Dictionary<PanelType, List<(TextMeshProUGUI Label, UrielFeature[] Features)>> _urielGateNotes = new();
    private bool _urielObjectsGateOn = true;

    /// <summary>Call at the top of a Uriel page build: forgets that page's previous gate targets.</summary>
    private void UrielGatesBegin(PanelType tab)
    {
        _urielGateTargets.Remove(tab);
        _urielGateNotes.Remove(tab);
    }

    private void UrielGate(PanelType tab, UrielFeature feature, params ButtonRef[] buttons)
    {
        foreach (var b in buttons) if (b?.Component != null) AddUrielGateTarget(tab, b.Component, feature);
    }

    private void UrielGate(PanelType tab, UrielFeature feature, params InputFieldRef[] inputs)
    {
        foreach (var i in inputs) if (i?.Component != null) AddUrielGateTarget(tab, i.Component, feature);
    }

    private void AddUrielGateTarget(PanelType tab, Selectable sel, UrielFeature feature)
    {
        if (!_urielGateTargets.TryGetValue(tab, out var list)) _urielGateTargets[tab] = list = new();
        list.Add((sel, feature));
        sel.interactable = UrielFeatureState.Model.Gate(feature).Enabled;
    }

    /// <summary>A muted line on a card that gives the reason when one of <paramref name="features"/> is off (empty
    /// otherwise).</summary>
    private void AddUrielGateNote(PanelType tab, GameObject card, params UrielFeature[] features)
    {
        var lbl = AddBodyText(card, "", Theme.ScaledUI(12));
        if (!_urielGateNotes.TryGetValue(tab, out var list)) _urielGateNotes[tab] = list = new();
        list.Add((lbl, features));
        lbl.text = UrielGateNoteText(features);
    }

    private static string UrielGateNoteText(UrielFeature[] features)
    {
        var sb = new StringBuilder();
        var seen = new HashSet<string>();
        foreach (var f in features)
        {
            var (enabled, reason) = UrielFeatureState.Model.Gate(f);
            if (enabled || !seen.Add(reason)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<color=#FFB070>").Append(reason).Append("</color> Its buttons are greyed.");
        }
        // Players can't run `.uriel features`: after a code=disabled handshake, say what that may mean.
        if (sb.Length == 0 && UrielFeatureState.ReportedDisabled && !UrielFeatureState.Model.HasSnapshot
            && Array.IndexOf(features, UrielFeature.Objects) < 0)
            sb.Append($"<color={Theme.MutedBodyHex}>Uriel reports object spawning off; other features may be off too. " +
                      "A refused action greys its buttons.</color>");
        return sb.ToString();
    }

    private void ApplyUrielGates()
    {
        foreach (var list in _urielGateTargets.Values)
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Sel == null) { list.RemoveAt(i); continue; }
                list[i].Sel.interactable = UrielFeatureState.Model.Gate(list[i].Feature).Enabled;
            }
        foreach (var list in _urielGateNotes.Values)
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Label == null) { list.RemoveAt(i); continue; }
                list[i].Label.text = UrielGateNoteText(list[i].Features);
            }
    }

    // Subscribed once from EnsureUrielSubscribed (unsubscribed in UnsubscribeUriel, called from MainPanel.Reset).
    private void OnUrielFeaturesChanged()
    {
        if (_disposed) return;
        ApplyUrielGates();
        // The per-row Spawn buttons follow object spawning; rebuild the lists only when that gate flips.
        bool objectsOn = UrielFeatureState.Model.Gate(UrielFeature.Objects).Enabled;
        if (objectsOn != _urielObjectsGateOn)
        {
            _urielObjectsGateOn = objectsOn;
            RebuildUrielUnlockedRows();
            RebuildUrielCatalogRows();
        }
        NoteUrielControlOutcomes();
        RefreshUrielControls();
        if (UrielFeatureState.Flow.AnyActive) StartUrielControlsTicker();
    }

    // ---------------------------------------------------------------------------------------------- Admin: Controls
    private TextMeshProUGUI _urielFeatLabel;
    private TextMeshProUGUI _urielCtlReplyLabel;
    private readonly Dictionary<UrielAction, (ButtonRef Arm, ButtonRef Confirm)> _urielCtlButtons = new();
    private readonly Dictionary<UrielAction, UrielStep> _urielCtlSeen = new();
    private Action _urielCtlTicker;
    private float _urielCtlNextTick;

    private void BuildUrielAdminConfigTab(GameObject page)
    {
        EnsureUrielSubscribed();
        // Results from an earlier admin session don't carry over once admin is gone (pure reset; the page is rebuilding).
        if (!Services.MessageService.IsLocalAdmin()) UrielFeatureState.Flow.Reset();
        var content = BeginAdminGate(page);

        // --- What's switched on ---
        var feat = AddCard(content, "UrielCtlFeatures");
        AddSectionHeading(feat, "What's switched on");
        _urielFeatLabel = AddBodyText(feat, UrielFeaturesText(), Theme.ScaledUI(12));
        var fRow = MakeUrielRow(feat, "UrielCtlFeatRow");
        AddUrielButton(fRow, "UrielCtlFeatRefresh", "Refresh",
            "Ask the server which Uriel features are on (.uriel features, admins only). The reply also shows in chat.",
            UrielFeatureState.RequestFeatures, 100);
        AddBodyText(feat,
            $"<color={Theme.MutedBodyHex}>The switches live in BepInEx/config/kdpen.Uriel.cfg on the server. A change takes " +
            "effect at the next server start. A feature that is off greys its buttons on the other Uriel tabs.</color>",
            Theme.ScaledUI(11));

        // --- Undo what Uriel did ---
        var undo = AddCard(content, "UrielCtlUndo");
        AddSectionHeading(undo, "Undo what Uriel did");
        AddBodyText(undo,
            "These work even with their feature, or the whole mod, switched off. <b>Arm</b> asks the server what it " +
            "would remove; <b>Confirm</b> opens once the server answers, for 25 seconds. Each needs a second press.");
        _urielCtlButtons.Clear();
        AddUrielControlRow(undo, UrielAction.StopEvents, "Stop all events",
            "Ends every running castle event; their doors go back as they were.");
        AddUrielControlRow(undo, UrielAction.PurgeUnits, "Remove all units",
            "Removes every Uriel NPC unit on the server and cancels pending spawns.");
        AddUrielControlRow(undo, UrielAction.PurgeObjects, "Remove all objects",
            "Removes every Uriel-spawned object on every plot. Native objects are never touched.");
        AddUrielControlRow(undo, UrielAction.KillSwitch, "Restore vanilla",
            "Everything above, and every shared chest and prison cell becomes private again.");
        AddBodyText(undo,
            $"<color={Theme.MutedBodyHex}><b>Restore vanilla</b> keeps saved settings (layouts, squads, templates, unlocks) " +
            "and swapped stairs. To keep Uriel off afterwards, set [General] Enabled = false and restart the server.</color>",
            Theme.ScaledUI(11));

        AddSectionHeading(undo, "Server reply");
        _urielCtlReplyLabel = AddBodyText(undo, UrielControlsReplyText(), Theme.ScaledUI(12));
        AddBodyText(undo,
            $"<color={Theme.MutedBodyHex}>Uriel keeps one arm per command for all admins, and another admin's replies don't " +
            "reach you. If someone else arms or confirms in between, your next reply (such as \"Not armed\") says so.</color>",
            Theme.ScaledUI(11));

        // --- The object-spawning handshake (as before) ---
        var snap = AddCard(content, "UrielAdminConfigCard");
        AddSectionHeading(snap, "Object spawning (from the handshake)");
        AddBodyText(snap, UrielState.Present && UrielState.SupportsObjectSpawnApi
            ? $"Object spawning: <b>{(UrielState.ObjectSpawnEnabled ? "ENABLED" : "disabled")}</b>\n" +
              $"Collection master: <b>{(UrielState.CollectionEnabled ? "ENABLED" : "disabled")}</b>\n" +
              $"Discovery mode: <b>{(string.IsNullOrEmpty(UrielState.DiscoveryMode) ? "—" : UrielState.DiscoveryMode)}</b>" +
              (UrielState.DiscoveryChance > 0 ? $"  ·  chance {UrielState.DiscoveryChance}%" : "") + "\n" +
              $"Admin-only spawning: <b>{(UrielState.ObjectSpawnAdminOnly ? "yes" : "no")}</b>\n" +
              $"Prefabs: total {UrielState.TotalPrefabs} · discoverable {UrielState.DiscoverablePrefabs} · blocked {UrielState.BlockedPrefabs}"
            : UrielState.Present
                ? "The server reports object spawning off, so it sends no object-spawning details."
                : "Uriel not detected on this server.");

        RefreshUrielControls();
        // Opening the page reads the switches (admins only; the server refuses anyone else).
        if (UrielState.Present && Services.MessageService.IsLocalAdmin()) UrielFeatureState.RequestFeatures();
    }

    // One action: name + what it does, then its buttons (Stop all events has one; the others Arm + Confirm).
    private void AddUrielControlRow(GameObject card, UrielAction a, string title, string what)
    {
        AddBodyText(card, $"<b>{title}</b>  <color={Theme.MutedBodyHex}>{what}</color>", Theme.ScaledUI(12));
        var row = MakeUrielRow(card, $"UrielCtlRow_{a}");
        var flow = UrielFeatureState.Flow;
        bool danger = a == UrielAction.KillSwitch;
        ButtonRef arm = null;
        if (UrielControlFlow.HasArm(a))
        {
            arm = AddUrielButton(row, $"UrielCtlArm_{a}", "Arm",
                $"Ask the server what this removes ({UrielControlFlow.Command(a, false)}). Harmless: nothing is removed yet.",
                () =>
                {
                    if (!flow.CanArm(a)) return;
                    UrielClient.SendUser(UrielControlFlow.Command(a, false));
                    flow.ArmSent(a, UrielFeatureState.Now);
                    OnUrielFeaturesChanged();
                }, 100);
        }
        string confirmTip = UrielControlFlow.HasArm(a)
            ? $"Opens after the server answers Arm. Press twice to send {UrielControlFlow.Command(a, true)}."
            : $"Press twice to send {UrielControlFlow.Command(a, true)}.";
        var confirm = UIFactory.CreateButton(row, $"UrielCtlConfirm_{a}", "Confirm",
            danger ? UrielWipe : UrielDanger);
        int w = Theme.ScaledWidth(UrielControlFlow.HasArm(a) ? 150 : 170);
        UIFactory.SetLayoutElement(confirm.GameObject, minWidth: w, preferredWidth: w, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var t = confirm.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(12); t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow; }
        TooltipHover.Attach(confirm.GameObject, confirmTip);
        confirm.OnClick = () =>
        {
            double now = UrielFeatureState.Now;
            if (flow.Press(a, now))
            {
                UrielClient.SendUser(UrielControlFlow.Command(a, true));
                flow.ConfirmSent(a, now);
            }
            OnUrielFeaturesChanged();
        };
        _urielCtlButtons[a] = (arm, confirm);
    }

    private void RefreshUrielControls()
    {
        if (_urielFeatLabel != null) _urielFeatLabel.text = UrielFeaturesText();
        var flow = UrielFeatureState.Flow;
        double now = UrielFeatureState.Now;
        foreach (var kv in _urielCtlButtons)
        {
            var a = kv.Key;
            var (arm, confirm) = kv.Value;
            var st = flow.Step(a);
            if (arm?.Component != null)
            {
                arm.SetEnabled(flow.CanArm(a));
                SetUrielButtonText(arm, st switch
                {
                    UrielStep.ArmSent => "Arming…",
                    UrielStep.Expired or UrielStep.NoReply or UrielStep.Refused => "Arm again",
                    _ => "Arm",
                });
            }
            if (confirm?.Component == null) continue;
            confirm.SetEnabled(flow.CanConfirm(a));
            string idle = a == UrielAction.StopEvents ? "Stop all events" : "Confirm";
            SetUrielButtonText(confirm, st switch
            {
                UrielStep.ConfirmPending => "Press again",
                UrielStep.ConfirmSent    => "Sending…",
                UrielStep.Armed          => $"Confirm ({Math.Ceiling(flow.ArmSecondsLeft(a, now)):0} s)",
                _ => idle,
            });
        }
        if (_urielCtlReplyLabel != null) _urielCtlReplyLabel.text = UrielControlsReplyText();
    }

    private static string UrielFeaturesText()
    {
        var m = UrielFeatureState.Model;
        var sb = new StringBuilder();
        if (UrielFeatureState.ReportedDisabled && !m.HasSnapshot)
            sb.Append("<color=#FFB070>The server reports object spawning off.</color> Other features may be off too.\n");
        if (m.Raw(UrielFeature.Master) == UrielSwitch.Off)
            sb.Append("<color=#FF8080><b>The master switch [General] Enabled is OFF: every feature below is off.</b></color>\n");
        if (!m.HasSnapshot)
            sb.Append($"<color={Theme.MutedBodyHex}>").Append(UrielFeatureState.FeaturesAskedAt >= 0
                ? "Asked the server. If nothing shows, this Uriel may be older than 0.22.0 (no features command), or you aren't authed as admin."
                : "Not read yet. Press Refresh.").Append("</color>\n");
        foreach (var f in UrielFeatures.All)
        {
            var raw = m.Raw(f);
            var eff = m.Effective(f);
            string word = eff switch
            {
                UrielSwitch.On  => "<color=#90EE90>on</color>",
                UrielSwitch.Off => "<color=#FF8080>OFF</color>",
                _ => $"<color={Theme.MutedBodyHex}>unknown</color>",
            };
            string extra = "";
            if (eff == UrielSwitch.On && f == UrielFeature.Objects && m.ObjectsAdminOnly) extra = " (admins only)";
            if (eff == UrielSwitch.On && f == UrielFeature.Events && m.EventsAdminOnly) extra = " (admins only)";
            if (eff == UrielSwitch.Off && raw != UrielSwitch.Off) extra = $" <color={Theme.MutedBodyHex}>(needs {UrielFeatureNeeds(f)})</color>";
            sb.Append("• ").Append(UrielFeatures.Name(f)).Append(": ").Append(word).Append(extra)
              .Append($"  <color={Theme.MutedBodyHex}>{UrielFeatures.ConfigKey(f)}</color>\n");
        }
        return sb.ToString().TrimEnd('\n');
    }

    private static string UrielFeatureNeeds(UrielFeature f)
    {
        if (UrielFeatureState.Model.Raw(UrielFeature.Master) == UrielSwitch.Off) return "the master switch";
        var p = UrielFeatures.Parent(f);
        return p.HasValue ? UrielFeatures.Name(p.Value).ToLowerInvariant() : "the master switch";
    }

    private static string UrielControlsReplyText()
    {
        var flow = UrielFeatureState.Flow;
        double now = UrielFeatureState.Now;
        var sb = new StringBuilder();
        foreach (UrielAction a in Enum.GetValues(typeof(UrielAction)))
        {
            var st = flow.Step(a);
            if (st == UrielStep.Idle) continue;
            string head = st switch
            {
                UrielStep.ArmSent        => "<color=#E6B85C>Waiting for the server…</color>",
                UrielStep.Armed          => $"<color=#E6B85C>Armed: confirm within {Math.Ceiling(flow.ArmSecondsLeft(a, now)):0} s.</color>",
                UrielStep.ConfirmPending => "<color=#E6B85C>Press again to send.</color>",
                UrielStep.ConfirmSent    => "<color=#E6B85C>Sent. Waiting for the result…</color>",
                UrielStep.Done           => "<color=#90EE90>Done.</color>",
                UrielStep.Partial        => $"<color=#FF8080>Done, with failures: {string.Join(", ", flow.FailedDomains(a))}.</color>",
                UrielStep.NoReply        => "<color=#FF8080>No server reply. Check chat, then try again.</color>",
                UrielStep.Expired        => "<color=#FFB070>Not confirmed in time. Press Arm again.</color>",
                UrielStep.Refused        => "<color=#FFB070>Uriel is still starting. Try again in a moment.</color>",
                _ => "",
            };
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append("<b>").Append(UrielControlTitle(a)).Append("</b>: ").Append(head);
            foreach (var line in flow.Lines(a)) sb.Append("\n").Append(line);
        }
        if (flow.Unsolicited.Count > 0)
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append($"<color={Theme.MutedBodyHex}>Seen in chat (not from these buttons):</color>");
            foreach (var line in flow.Unsolicited) sb.Append("\n").Append(line);
        }
        return sb.Length == 0 ? $"<color={Theme.MutedBodyHex}>Replies to these buttons show here and in chat.</color>" : sb.ToString();
    }

    private static string UrielControlTitle(UrielAction a) => a switch
    {
        UrielAction.StopEvents   => "Stop all events",
        UrielAction.PurgeUnits   => "Remove all units",
        UrielAction.PurgeObjects => "Remove all objects",
        UrielAction.KillSwitch   => "Restore vanilla",
        _ => a.ToString(),
    };

    // Once per finished action: the tabs whose lists it changed re-read when next opened. Nothing touches the rail.
    private void NoteUrielControlOutcomes()
    {
        var flow = UrielFeatureState.Flow;
        foreach (UrielAction a in Enum.GetValues(typeof(UrielAction)))
        {
            var st = flow.Step(a);
            _urielCtlSeen.TryGetValue(a, out var before);
            _urielCtlSeen[a] = st;
            bool finished = st is UrielStep.Done or UrielStep.Partial;
            bool wasFinished = before is UrielStep.Done or UrielStep.Partial;
            if (!finished || wasFinished) continue;
            foreach (var tab in UrielStaleTabsAfter(a)) MarkTabStale(tab);
        }
    }

    private static PanelType[] UrielStaleTabsAfter(UrielAction a) => a switch
    {
        UrielAction.PurgeObjects => new[] { PanelType.UrielObjectsTab, PanelType.UrielObjectCatalogTab, PanelType.UrielAdminObjectsTab },
        UrielAction.KillSwitch => new[]
        {
            PanelType.UrielStorageTab, PanelType.UrielPrisonTab, PanelType.UrielAdminSharingTab,
            PanelType.UrielObjectsTab, PanelType.UrielObjectCatalogTab, PanelType.UrielAdminObjectsTab,
            PanelType.UrielEventsTab, PanelType.UrielAdminUnitsTab,
        },
        _ => new[] { PanelType.UrielEventsTab, PanelType.UrielAdminUnitsTab },   // units, stop all events
    };

    // While a slot has a deadline, tick about once a second: countdowns, expiry, reply waits, the gather window.
    // Self-unregistering (and removed in StopUrielControlsTicker from UnsubscribeUriel).
    private void StartUrielControlsTicker()
    {
        if (_urielCtlTicker != null) return;
        _urielCtlNextTick = 0f;
        _urielCtlTicker = () =>
        {
            if (Time.realtimeSinceStartup < _urielCtlNextTick) return;
            _urielCtlNextTick = Time.realtimeSinceStartup + 1f;
            if (!Services.MessageService.IsLocalAdmin()) UrielFeatureState.ResetFlow();
            else UrielFeatureState.Tick();
            if (_disposed || !UrielFeatureState.Flow.AnyActive) { StopUrielControlsTicker(); return; }
            RefreshUrielControls();
        };
        Behaviors.CoreUpdateBehavior.Actions.Add(_urielCtlTicker);
    }

    private void StopUrielControlsTicker()
    {
        if (_urielCtlTicker == null) return;
        Behaviors.CoreUpdateBehavior.Actions.Remove(_urielCtlTicker);
        _urielCtlTicker = null;
        if (!_disposed) RefreshUrielControls();
    }
}
