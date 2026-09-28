using System;
using System.Collections.Generic;
using Raphael.Config;
using Raphael.UI.ModContent.Data;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.Utils;
using Raphael.UI.Framework.CustomLib.Util;
using TMPro;
using UnityEngine;

namespace Raphael.UI.ModContent;

// 0.67.2: Settings and Help → Chat & Diagnostics. One place for everything that decides what Raphael hides from
// chat and what it logs, for Raphael/Bloodcraft and each server mod — previously spread over the Settings tab, each
// mod's Settings tab and a .cfg-only key. The same settings stay editable where they were (per-mod Settings tabs);
// this tab re-reads them when shown and marks those pages stale when it changes them.
//
// Every row is a semantic on/off over one or more legacy config keys, so presets and the "Current:" label work on
// what the player sees, not on raw keys.
public partial class MainPanel
{
    private sealed class ChatDiagRow
    {
        public string Key;
        public Func<bool> Get;
        public Action<bool> Set;
        public ToggleRef Toggle;
    }

    private enum ChatPreset { Quiet, Normal, Everything }

    private readonly List<ChatDiagRow> _chatDiagRows = new();
    private TextMeshProUGUI _chatDiagPresetLabel;
    private TextMeshProUGUI _chatDiagTraceNote;
    private bool _chatDiagApplying;

    // Settings on other tabs that this tab also edits — rebuilt lazily after a change here.
    private static readonly PanelType[] ChatDiagMirrorTabs =
    {
        PanelType.SettingsTab, PanelType.BeelzSettingsTab, PanelType.UrielSettingsTab,
        PanelType.FaustSettingsTab, PanelType.NyarSettingsTab,
    };

    // Row keys, in preset-table order.
    private const string RowVcf = "vcf", RowBgReplies = "bg", RowCopies = "copies", RowFamChatter = "fam";

    private static readonly (string Key, string Name)[] ChatDiagMods =
    {
        ("beelz", "Beelzebub"), ("uriel", "Uriel"), ("faust", "Faust"), ("nyar", "Nyarlathotep"),
    };

    private void BuildChatDiagTab(GameObject page)
    {
        _chatDiagRows.Clear();

        AddGuideSection(page, "Chat & Diagnostics",
            "Controls what Raphael removes from chat and what it writes to LogOutput.log, for Raphael and every " +
            "server mod it talks to. Raphael never hides other players' chat. Replies and errors for commands " +
            "<b>you</b> send (buttons, Raphael chat, the game's chat box) always show; only Raphael's own background " +
            "traffic is hidden. Testing a mod and missing a reply? Pick <b>Show everything</b>.");

        // ── Presets ──────────────────────────────────────────────────────
        var presetCard = AddCard(page, "ChatDiagPresetCard");
        AddSectionHeading(presetCard, "Preset");
        var presetRow = MakeUrielRow(presetCard, "ChatDiagPresetRow");
        AddUrielButton(presetRow, "ChatPresetQuiet", "Quiet",
            "Hide everything Raphael can: background replies and errors, chat copies of replies Raphael already " +
            "shows, and familiar bind/switch chatter. Protocol lines hidden, wire traces off.",
            () => ApplyChatPreset(ChatPreset.Quiet), 110);
        AddUrielButton(presetRow, "ChatPresetNormal", "Normal",
            "The defaults: background replies and errors hidden, chat copies of replies Raphael already shows " +
            "hidden, familiar chatter shown. Protocol lines hidden, wire traces off.",
            () => ApplyChatPreset(ChatPreset.Normal), 110);
        AddUrielButton(presetRow, "ChatPresetEverything", "Show everything",
            "For testing: nothing hidden — background replies and errors, chat copies, familiar chatter and every " +
            "mod's protocol lines all show in chat, and every mod's wire trace is logged. Diagnostic mode (below) " +
            "is separate and not changed.",
            () => ApplyChatPreset(ChatPreset.Everything), 150);
        _chatDiagPresetLabel = AddNyarRowText(presetRow, "");

        // ── Raphael & Bloodcraft ─────────────────────────────────────────
        AddSpacer(page, 6);
        var rCard = AddCard(page, "ChatDiagRaphaelCard");
        AddSectionHeading(rCard, "Raphael & Bloodcraft");
        AddChatDiagRow(rCard, RowVcf, "Hide command errors caused by Raphael's background queries",
            "Command-framework '[error]', '[denied]' and 'command not found' lines. Raphael probes the server for " +
            "mods it may not have, which produces these. Errors for commands you send yourself always show.",
            () => Settings.SuppressCommandFrameworkErrors,
            v => Settings.SetSuppressCommandFrameworkErrors(v));
        AddChatDiagRow(rCard, RowBgReplies, "Hide replies to Raphael's background queries",
            "Replies to the queries Raphael sends on its own to keep panels and overlays current.",
            () => !Settings.ShowChatBchAuto,
            v => Settings.SetShowChatBchAuto(!v));
        AddChatDiagRow(rCard, RowCopies, "Hide chat copies of replies Raphael already shows",
            "Bloodcraft/Kindred replies Raphael reads into its own panels (box list, prestige, blood, expertise…). " +
            "Off shows them in chat too. Action confirmations and replies Raphael doesn't read always show.",
            () => Settings.ClearServerMessages || !Settings.ShowChatBloodcraft || !Settings.ShowChatKindred,
            v =>
            {
                if (v) { Settings.SetClearServerMessages(true); return; }
                Settings.SetClearServerMessages(false);
                Settings.SetShowChatBloodcraft(true);
                Settings.SetShowChatKindred(true);
            });
        AddChatDiagRow(rCard, RowFamChatter, "Hide familiar-action chatter (bind / unbind / switch box …)",
            "Bloodcraft's confirmation lines for .fam b / ub / t / cb / mb / sb / r. The UI updates either way.",
            () => Settings.SuppressFamiliarActionChatter,
            v => Settings.SetSuppressFamiliarActionChatter(v));
        AddSpacer(rCard, 4);
        AddDiagnosticModeToggle(rCard);
        AddBodyText(rCard,
            "<i>Diagnostic mode logs Raphael's own UI, input and Bloodcraft protocol events, and turns on every " +
            "mod's wire trace below. Presets don't change it.</i>", Theme.ScaledUI(11));

        // ── Server mods ──────────────────────────────────────────────────
        AddSpacer(page, 6);
        var mCard = AddCard(page, "ChatDiagModsCard");
        AddSectionHeading(mCard, "Server mods");
        AddBodyText(mCard,
            "<b>Show protocol lines in chat</b>: keep the mod's machine-readable lines (e.g. [NYAR:ok …]) in chat " +
            "instead of removing them after Raphael reads them. Replies to a command you send to that mod yourself " +
            "show for 10 seconds even when this is off. <b>Log wire trace</b>: write every command and reply to " +
            "LogOutput.log (filter for [Nyar], [Faust], [Beelz], [Uriel]).", Theme.ScaledUI(11));
        foreach (var (key, name) in ChatDiagMods)
        {
            AddSectionHeading(mCard, name);
            AddChatDiagRow(mCard, "proto_" + key, $"Show {name} protocol lines in chat",
                $"Keep {name}'s machine-readable lines in chat. Raphael still reads them.",
                () => GetModProtocol(key), v => SetModProtocol(key, v));
            AddChatDiagRow(mCard, "trace_" + key, $"Log {name} wire trace",
                $"Write every {name} command sent and reply received to LogOutput.log.",
                () => GetModTrace(key), v => SetModTrace(key, v));
        }
        _chatDiagTraceNote = AddBodyText(mCard, "", Theme.ScaledUI(11));

        AddSpacer(page, 6);
        var noteCard = AddCard(page, "ChatDiagNotesCard");
        AddSectionHeading(noteCard, "Good to know");
        AddBodyText(noteCard,
            "• Nyarlathotep tab <b>Refresh</b> buttons read data quietly; their replies go to the tab, not chat.\n" +
            "• A mod's reply only appears if the command exists: `.nyar` alone lists Nyarlathotep's commands.\n" +
            "• Raphael's chat window drops an identical long line repeated within a minute (flood guard); the game's " +
            "own chat still shows it.", Theme.ScaledUI(11));

        RefreshChatDiagRows();
    }

    private void AddChatDiagRow(GameObject parent, string key, string label, string tooltip,
        Func<bool> get, Action<bool> set)
    {
        var row = new ChatDiagRow { Key = key, Get = get, Set = set };
        row.Toggle = AddChatLoggingToggle(parent, label, tooltip, get, v =>
        {
            if (_chatDiagApplying) return;
            try { set(v); }
            catch (Exception ex) { LogUtils.LogError($"[ChatDiag] row '{key}' failed: {ex}"); }
            AfterChatDiagChange();
        });
        _chatDiagRows.Add(row);
    }

    private static bool GetModProtocol(string key) => key switch
    {
        "beelz" => Settings.BeelzShowProtocolInChat,
        "uriel" => Settings.UrielShowProtocolInChat,
        "faust" => Settings.FaustShowProtocolInChat,
        "nyar"  => Settings.NyarShowProtocolInChat,
        _ => false,
    };

    private static void SetModProtocol(string key, bool v)
    {
        switch (key)
        {
            case "beelz": Settings.SetBeelzShowProtocolInChat(v); break;
            case "uriel": Settings.SetUrielShowProtocolInChat(v); break;
            case "faust": Settings.SetFaustShowProtocolInChat(v); break;
            case "nyar":  Settings.SetNyarShowProtocolInChat(v); break;
        }
    }

    private static bool GetModTrace(string key) => key switch
    {
        "beelz" => Settings.BeelzDiagnostics,
        "uriel" => Settings.UrielDiagnostics,
        "faust" => Settings.FaustDiagnostics,
        "nyar"  => Settings.NyarDiagnostics,
        _ => false,
    };

    private static void SetModTrace(string key, bool v)
    {
        switch (key)
        {
            case "beelz": Settings.SetBeelzDiagnostics(v); break;
            case "uriel": Settings.SetUrielDiagnostics(v); break;
            case "faust": Settings.SetFaustDiagnostics(v); break;
            case "nyar":  Settings.SetNyarDiagnostics(v); break;
        }
    }

    // Semantic row values per preset. Rows not listed default to the "mods" rule (protocol/trace follow the flag).
    private static bool PresetValue(ChatPreset p, string rowKey)
    {
        if (rowKey.StartsWith("proto_", StringComparison.Ordinal) || rowKey.StartsWith("trace_", StringComparison.Ordinal))
            return p == ChatPreset.Everything;
        return rowKey switch
        {
            RowVcf        => p != ChatPreset.Everything,
            RowBgReplies  => p != ChatPreset.Everything,
            RowCopies     => p != ChatPreset.Everything,
            RowFamChatter => p == ChatPreset.Quiet,
            _ => false,
        };
    }

    private void ApplyChatPreset(ChatPreset preset)
    {
        _chatDiagApplying = true;
        try
        {
            foreach (var row in _chatDiagRows)
            {
                // Always write, even when the row already reads right: the combined "copies" row can read hidden
                // for a different underlying key, and a preset should leave the canonical keys set.
                bool want = PresetValue(preset, row.Key);
                try { row.Set(want); }
                catch (Exception ex) { LogUtils.LogError($"[ChatDiag] preset row '{row.Key}' failed: {ex}"); }
            }
        }
        finally { _chatDiagApplying = false; }
        LogUtils.LogInfo($"[ChatDiag] preset -> {PresetName(preset)}.");
        AfterChatDiagChange();
    }

    private void AfterChatDiagChange()
    {
        foreach (var t in ChatDiagMirrorTabs) MarkTabStale(t);
        RefreshChatDiagRows();
    }

    /// <summary>Re-read every row from Settings (they're also editable on other tabs) and update the labels.</summary>
    private void RefreshChatDiagRows()
    {
        foreach (var row in _chatDiagRows)
            if (row.Toggle?.Toggle != null) row.Toggle.Toggle.SetIsOnWithoutNotify(row.Get());

        if (_chatDiagPresetLabel != null)
            _chatDiagPresetLabel.text = $"Current: <b>{CurrentChatPresetName()}</b>";
        if (_chatDiagTraceNote != null)
            _chatDiagTraceNote.text = Settings.DiagnosticMode
                ? "<i>Diagnostic mode is on, so every mod's wire trace is being logged regardless of the rows above.</i>"
                : "";
    }

    private string CurrentChatPresetName()
    {
        if (_chatDiagRows.Count == 0) return "—";
        foreach (ChatPreset p in Enum.GetValues(typeof(ChatPreset)))
        {
            bool all = true;
            foreach (var row in _chatDiagRows)
                if (row.Get() != PresetValue(p, row.Key)) { all = false; break; }
            if (all) return PresetName(p);
        }
        return "Custom";
    }

    private static string PresetName(ChatPreset p) => p switch
    {
        ChatPreset.Quiet => "Quiet",
        ChatPreset.Normal => "Normal",
        _ => "Show everything",
    };
}
