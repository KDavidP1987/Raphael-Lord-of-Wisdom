using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raphael.Services;
using Raphael.Services.Bloodcraft;
using Raphael.UI.Forms;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.Utils;
using TMPro;
using UnityEngine;

namespace Raphael.UI.ModContent;

// 0.73.0: Bloodcraft → Admin → "Reset Bloodcraft progress" card (PLAN.md §2 "Card UI"). The run itself lives in
// BcResetRunner, so this card only reads it: rebuilding or closing the panel never stops a run, and a rebuilt card
// re-attaches to the live one.
public partial class MainPanel
{
    // Form state, kept across panel rebuilds.
    private string _bcResetPlayer = "";
    private readonly BcResetOptions _bcResetOpts = new();
    private string _bcResetLevelText = "10";
    private bool _bcResetIncludeReroll;

    private TextMeshProUGUI _bcResetReview, _bcResetStatus, _bcResetPauseText, _bcResetLog, _bcResetRetryNote;
    private GameObject _bcResetFormBlock, _bcResetRunBlock, _bcResetPauseNoReply, _bcResetPauseRefusal, _bcResetRetryRow;
    private ButtonRef _bcResetRetryStepBtn, _bcResetRerollBtn;
    private GameObject _bcResetRerollToggleRow;
    private CanvasGroup _bcResetFormsLock;
    private TextMeshProUGUI _bcResetFormsLockNote;
    private Action _bcResetTicker;
    private int _bcResetSeenRevision = -1;

    private const string BC_RESET_CANT =
        "Not reset by this tool: class and class spell, weapon and blood stat choices (their bonuses drop with the " +
        "levels), familiars, quest history and the player's own toggles. The player resets those themselves with " +
        "<b>.class change</b>, <b>.wep rst</b>, <b>.bl rst</b> and <b>.fam reset</b>.";

    private void BuildBcResetCard(GameObject page)
    {
        CollapsibleSection.Build(page,
            title: "Reset Bloodcraft progress",
            startExpanded: BcResetRunner.IsRunning,
            tooltip: "Put a player's Bloodcraft progress back to the start by sending Bloodcraft's admin commands one after another. Admin only.",
            buildContent: content =>
            {
                var card = AddCard(content, "BcResetCard");
                AddBodyText(card,
                    $"Sends the admin commands you would otherwise type one by one ({Mono(".lvl set")}, {Mono(".bl set")}, " +
                    $"{Mono(".prestige r")}, {Mono(".wep set")}, {Mono(".prof set")}) for every type, and reads each reply, so it " +
                    "works as one reset. The player keeps their familiars. " +
                    $"<color={Theme.MutedBodyHex}>Works for offline players too. Commands already sent can't be undone.</color>");

                _bcResetFormBlock = UIFactory.CreateVerticalGroup(card, "BcResetForm",
                    forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
                    spacing: 4, padding: new Vector4(0, 4, 0, 0));
                BuildBcResetForm(_bcResetFormBlock);

                _bcResetRunBlock = UIFactory.CreateVerticalGroup(card, "BcResetRun",
                    forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
                    spacing: 4, padding: new Vector4(0, 4, 0, 0));
                BuildBcResetRunView(_bcResetRunBlock);

                _bcResetSeenRevision = -1;
                if (_bcResetTicker != null) Behaviors.CoreUpdateBehavior.Actions.Remove(_bcResetTicker);
                _bcResetTicker = BcResetTick;
                Behaviors.CoreUpdateBehavior.Actions.Add(_bcResetTicker);
                RefreshBcResetReview();
                RefreshBcResetRun();
            });
    }

    private void BuildBcResetForm(GameObject parent)
    {
        AddBodyText(parent, $"<b>Player</b> <color={Theme.MutedBodyHex}>— pick an online player, or type the exact name of an offline one</color>");
        AddBeelzSearchableField(parent, "BcResetPlayer", "Player name", _bcResetPlayer,
            BeelzPlayerMatches, v => { _bcResetPlayer = v ?? ""; RefreshBcResetReview(); });

        AddBodyText(parent, "<b>Reset</b>");
        AddUrielBoolToggle(parent, "BcResetPrestige", "Prestige (all 28 types, including Exo)", _bcResetOpts.Prestige,
            v => { _bcResetOpts.Prestige = v; RefreshBcResetReview(); }, "Sends .prestige r for every prestige type. A type the player never prestiged gives no reply; that counts as nothing to reset.");
        AddUrielBoolToggle(parent, "BcResetWeapons", "Weapon expertise (16 weapons) to 0", _bcResetOpts.Weapons,
            v => { _bcResetOpts.Weapons = v; RefreshBcResetReview(); }, "Sends .wep set <player> <weapon> 0 for every weapon.");
        AddUrielBoolToggle(parent, "BcResetLegacies", "Blood legacies (10 bloods) to 0", _bcResetOpts.Legacies,
            v => { _bcResetOpts.Legacies = v; RefreshBcResetReview(); }, "Sends .bl set <player> <blood> 0 for every blood type.");
        AddUrielBoolToggle(parent, "BcResetProfessions", "Professions (8) to 0", _bcResetOpts.Professions,
            v => { _bcResetOpts.Professions = v; RefreshBcResetReview(); }, "Sends .prof set <player> <profession> 0 for every profession.");
        AddUrielBoolToggle(parent, "BcResetLevel", "Character level", _bcResetOpts.Level,
            v => { _bcResetOpts.Level = v; RefreshBcResetReview(); }, "Sends .lvl set <player> <level> last.");

        var lvlRow = MakeUrielRow(parent, "BcResetLevelRow");
        var lvlLbl = AddInfoLabel(lvlRow, "BcResetLevelLbl", "Set level to", FontStyles.Normal, Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lvlLbl.gameObject, minWidth: 90, preferredWidth: 100, flexibleWidth: 0);
        var lvlInput = UIFactory.CreateInputField(lvlRow, "BcResetLevelInput", "10");
        UIFactory.SetLayoutElement(lvlInput.GameObject, minWidth: 60, preferredWidth: 70, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        lvlInput.Text = _bcResetLevelText;
        lvlInput.OnValueChanged += v => { _bcResetLevelText = v ?? ""; RefreshBcResetReview(); };
        var lvlNote = AddInfoLabel(lvlRow, "BcResetLevelNote",
            $"<color={Theme.MutedBodyHex}>Match your server's StartingLevel (Bloodcraft's default is 10).</color>", FontStyles.Normal, Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(lvlNote.gameObject, minWidth: 160, preferredWidth: 260, flexibleWidth: 1);
        lvlNote.enableWordWrapping = true;

        AddBodyText(parent, $"<b>Optional</b> <color={Theme.MutedBodyHex}>— off by default</color>");
        AddUrielBoolToggle(parent, "BcResetQuests", "Re-roll daily and weekly quests", _bcResetOpts.Quests,
            v => { _bcResetOpts.Quests = v; RefreshBcResetReview(); }, "Sends .quest rf. This re-rolls the player's current quests; it does not clear quest history.");
        AddUrielBoolToggle(parent, "BcResetFamiliar", "Active familiar to level 1", _bcResetOpts.Familiar,
            v => { _bcResetOpts.Familiar = v; RefreshBcResetReview(); }, "Sends .fam sl <player> 1. Only works while the player is online with a familiar out; otherwise it is recorded as not met.");

        _bcResetReview = AddBcResetText(parent, "BcResetReview", FontStyles.Bold, Theme.ScaledUI(12));
        AddBodyText(parent, $"<color={Theme.MutedBodyHex}>{BC_RESET_CANT}</color>", Theme.ScaledUI(11));

        var row = MakeUrielRow(parent, "BcResetStartRow");
        AddUrielConfirmButton(row, "BcResetStart", "Start reset",
            "Start sending the reset commands listed above.", StartBcReset, 130, WARNING_TINT);
    }

    // A wrapped label that grows with its text. AddInfoLabel pins a one-line height, so the multi-line status, step
    // log and notes spilled over the intro text and the buttons (friend test of 0.73.0).
    private static TextMeshProUGUI AddBcResetText(GameObject parent, string name, FontStyles style, int fontSize)
    {
        var t = AddInfoLabel(parent, name, "", style, fontSize);
        t.alignment = TextAlignmentOptions.TopLeft;
        t.enableWordWrapping = true;
        var le = t.GetComponent<UnityEngine.UI.LayoutElement>();
        if (le != null) { le.minHeight = Theme.ScaledHeight(18); le.preferredHeight = -1; }
        return t;
    }

    private void BuildBcResetRunView(GameObject parent)
    {
        _bcResetStatus = AddBcResetText(parent, "BcResetStatus", FontStyles.Bold, Theme.ScaledUI(13));

        // Stop while running, "Start a new reset" once it ends. Kept right under the status so it is never pushed
        // below a long log.
        var stopRow = MakeUrielRow(parent, "BcResetStopRow");
        AddUrielButton(stopRow, "BcResetStop", "Stop", "Stop the reset. A command already sent can't be recalled.", () => BcResetRunner.Stop("Stopped by you."), 80);
        AddUrielButton(stopRow, "BcResetNew", "Start a new reset",
            "Close this report and go back to an empty form to reset another player.", StartNewBcReset, 160);

        _bcResetPauseText = AddBcResetText(parent, "BcResetPauseText", FontStyles.Normal, Theme.ScaledUI(12));

        _bcResetPauseNoReply = MakeUrielRow(parent, "BcResetPauseNoReply");
        _bcResetRetryStepBtn = AddUrielButton(_bcResetPauseNoReply, "BcResetRetryStep", "Retry this step",
            "Send the same command again. Safe: setting to 0 twice gives the same result.", () => BcResetRunner.RetryStep(false), 130);
        _bcResetRerollBtn = AddUrielConfirmButton(_bcResetPauseNoReply, "BcResetReroll", "Re-roll quests again",
            "The first re-roll may already have happened. This re-rolls the player's quests a second time.",
            () => BcResetRunner.RetryStep(true), 160, WARNING_TINT);
        AddUrielButton(_bcResetPauseNoReply, "BcResetSkip", "Skip", "Record this step as unknown and go on.", () => BcResetRunner.Skip(), 80);
        AddUrielButton(_bcResetPauseNoReply, "BcResetStopA", "Stop", "Stop the reset. Nothing more is sent.", () => BcResetRunner.Stop("Stopped by you."), 80);

        _bcResetPauseRefusal = MakeUrielRow(parent, "BcResetPauseRefusal");
        AddUrielButton(_bcResetPauseRefusal, "BcResetStopB", "Stop", "Stop the reset. Nothing more is sent.", () => BcResetRunner.Stop("Stopped by you."), 80);
        AddUrielButton(_bcResetPauseRefusal, "BcResetContinue", "Continue",
            "Carry on. Choose this only if that line came from something else you sent.", () => BcResetRunner.Continue(), 100);

        // Step log in its own darker inset so it reads as a report, apart from the controls.
        var logCard = AddCard(parent, "BcResetLogCard", new Color(0f, 0f, 0f, 0.28f), padding: 8, innerSpacing: 0);
        _bcResetLog = AddBcResetText(logCard, "BcResetLog", FontStyles.Normal, Theme.ScaledUI(11));

        _bcResetEndNote = AddBcResetText(parent, "BcResetEndNote", FontStyles.Normal, Theme.ScaledUI(11));

        _bcResetRetryNote = AddBcResetText(parent, "BcResetRetryNote", FontStyles.Normal, Theme.ScaledUI(11));
        _bcResetRerollToggleRow = UIFactory.CreateVerticalGroup(parent, "BcResetRerollToggleHost",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true, spacing: 0, padding: Vector4.zero);
        AddUrielBoolToggle(_bcResetRerollToggleRow, "BcResetIncludeReroll", "Also re-roll quests again (the first re-roll may have happened)",
            _bcResetIncludeReroll, v => { _bcResetIncludeReroll = v; _bcResetSeenRevision = -1; }, "Adds the quest re-roll to the retry list.");
        _bcResetRetryRow = MakeUrielRow(parent, "BcResetRetryRow");
        AddUrielConfirmButton(_bcResetRetryRow, "BcResetRetry", "Retry failed and unknown",
            "Start a new run with only the steps that failed or got no reply, worked out again now.", () =>
            {
                var err = BcResetRunner.StartRetry(_bcResetIncludeReroll);
                if (err != null && _bcResetStatus != null) _bcResetStatus.text = $"<color=#{ColorUtility.ToHtmlStringRGB(WARNING_TINT)}>{err}</color>";
            }, 190);
    }

    /// <summary>The run whose report the admin dismissed with "New reset"; the form shows again until a new run starts.</summary>
    private BcResetRun _bcResetDismissed;

    private TextMeshProUGUI _bcResetEndNote;

    /// <summary>"Start a new reset": hide the finished report and give back the form with the player cleared, so the
    /// next player can't be reset by mistake under the last one's name. The chosen options stay.</summary>
    private void StartNewBcReset()
    {
        _bcResetDismissed = BcResetRunner.Current;
        _bcResetIncludeReroll = false;
        _bcResetPlayer = "";
        var input = _bcResetFormBlock ? _bcResetFormBlock.transform.Find("BcResetPlayerInput")?.GetComponent<TMP_InputField>() : null;
        if (input != null) input.text = "";
        RefreshBcResetReview();
        RefreshBcResetRun();
    }

    private void StartBcReset()
    {
        var steps = BcResetBuildPlan(out var name, out var error);
        if (error == null) error = BcResetRunner.Start(name, steps);
        if (error != null)
        {
            if (_bcResetReview != null) _bcResetReview.text = $"<color=#{ColorUtility.ToHtmlStringRGB(WARNING_TINT)}>{error}</color>";
            return;
        }
        _bcResetDismissed = null;
        RefreshBcResetRun();
    }

    private List<BcResetStep> BcResetBuildPlan(out string name, out string error)
    {
        error = BcResetPlan.ValidateName(_bcResetPlayer, out name);
        if (error != null) return null;
        if (_bcResetOpts.Level)
        {
            int max = PlayerStateService.Config.MaxPlayerLevel;
            error = BcResetPlan.ValidateLevel(_bcResetLevelText, max > 0 ? max : null, out var level);
            if (error != null) return null;
            _bcResetOpts.LevelValue = level;
        }
        var steps = BcResetPlan.Build(name, _bcResetOpts);
        if (steps.Count == 0) { error = "Pick at least one thing to reset."; return null; }
        return steps;
    }

    private void RefreshBcResetReview()
    {
        if (_bcResetReview == null) return;
        var steps = BcResetBuildPlan(out var name, out var error);
        _bcResetReview.text = error != null
            ? $"<color=#{ColorUtility.ToHtmlStringRGB(WARNING_TINT)}>{error}</color>"
            : BcResetPlan.Describe(name, steps);
    }

    private void BcResetTick()
    {
        // The card was destroyed (tab switch / rebuild): stop polling.
        if (_bcResetRunBlock == null || !_bcResetRunBlock)
        {
            Behaviors.CoreUpdateBehavior.Actions.Remove(_bcResetTicker);
            _bcResetTicker = null;
            return;
        }
        if (BcResetRunner.Revision == _bcResetSeenRevision) return;
        RefreshBcResetRun();
    }

    private void RefreshBcResetRun()
    {
        try
        {
            _bcResetSeenRevision = BcResetRunner.Revision;
            var run = BcResetRunner.Current;
            bool show = run != null && run != _bcResetDismissed;
            if (_bcResetFormBlock) _bcResetFormBlock.SetActive(!show);
            if (_bcResetRunBlock) _bcResetRunBlock.SetActive(show);
            ApplyBcResetFormsLock();
            if (!show || run == null) return;

            string warn = ColorUtility.ToHtmlStringRGB(WARNING_TINT);
            int total = run.Steps.Count;
            var cur = run.CurrentStep;
            if (run.Active)
                _bcResetStatus.text = cur != null
                    ? $"Resetting {run.Player}: step {cur.Index + 1} / {total}  <color={Theme.MutedBodyHex}>{cur.Command}</color>"
                    : $"Resetting {run.Player}: starting…";
            else
                _bcResetStatus.text = run.Summary();

            bool noReply = run.State == BcRunState.Paused && run.Pause == BcPause.NoReply;
            bool refusal = run.State == BcRunState.Paused && (run.Pause == BcPause.PlayerNotFound || run.Pause == BcPause.Denied);
            if (noReply)
            {
                var sb = new StringBuilder($"<color=#{warn}><b>Paused.</b></color> No reply to <b>{cur?.Command}</b>. Nothing more is sent until you choose.");
                if (cur != null && !cur.Idempotent)
                    sb.Append($" <color={Theme.MutedBodyHex}>The quest re-roll may already have happened.</color>");
                foreach (var line in run.PossiblyRelated.TakeLast(3))
                    sb.Append($"\n<color={Theme.MutedBodyHex}>Possibly related: {Services.Guides.GuideText.Escape(line)}</color>");
                _bcResetPauseText.text = sb.ToString();
            }
            else if (refusal)
            {
                string what = run.Pause == BcPause.Denied ? "The server refused a command" : "The server couldn't find a player";
                _bcResetPauseText.text = $"<color=#{warn}><b>Paused.</b></color> {what}: \"{Services.Guides.GuideText.Escape(run.PauseLine)}\". " +
                                         $"<color={Theme.MutedBodyHex}>Stop unless that line came from something else you sent.</color>";
            }
            _bcResetPauseText.gameObject.SetActive(noReply || refusal);
            _bcResetPauseNoReply.SetActive(noReply);
            if (noReply && cur != null)
            {
                _bcResetRetryStepBtn.GameObject.SetActive(cur.Idempotent);
                _bcResetRerollBtn.GameObject.SetActive(!cur.Idempotent);
            }
            _bcResetPauseRefusal.SetActive(refusal);
            var stopRow = _bcResetRunBlock.transform.Find("BcResetStopRow");
            if (stopRow != null)
            {
                stopRow.Find("BcResetStop")?.gameObject.SetActive(run.Active && !noReply && !refusal);
                stopRow.Find("BcResetNew")?.gameObject.SetActive(run.Ended);
                stopRow.gameObject.SetActive((run.Active && !noReply && !refusal) || run.Ended);
            }

            // Step log: everything that has an outcome, newest last; long runs show the latest 14.
            string muted = Theme.MutedBodyHex;
            var log = new StringBuilder();
            var settled = run.Steps.Where(s => s.State != BcStepState.Pending).ToList();
            if (settled.Count > 14) log.Append($"<color={muted}>… {settled.Count - 14} earlier steps not shown</color>\n");
            foreach (var s in settled.Skip(Math.Max(0, settled.Count - 14)))
                log.Append($"<color={muted}>{s.Index + 1}.</color>  {s.Command}  <color={muted}>→</color>  {BcResetOutcome(s)}\n");
            foreach (var note in run.LateNotes) log.Append($"<color={muted}>{Services.Guides.GuideText.Escape(note)}</color>\n");
            if (log.Length == 0) log.Append($"<color={muted}>Waiting for the first reply…</color>");
            _bcResetLog.text = log.ToString().TrimEnd('\n');

            _bcResetEndNote.gameObject.SetActive(run.Ended);
            if (run.Ended)
            {
                var end = new StringBuilder($"<color={muted}>A command already sent can't be recalled. {BC_RESET_CANT}");
                if (!run.Sealed) end.Append("\nLate replies in the next 30 s still update this report.");
                if (!run.RetryAllowed) end.Append("\nRetry isn't offered after leaving the server. Start a new reset if needed.");
                _bcResetEndNote.text = end.Append("</color>").ToString();
            }

            var retry = run.Ended ? run.RetryList(_bcResetIncludeReroll) : new List<BcResetStep>();
            bool canRetry = run.Ended && retry.Count > 0;
            _bcResetRetryRow.SetActive(canRetry);
            _bcResetRerollToggleRow.SetActive(run.Ended && run.HasUnknownReroll);
            _bcResetRetryNote.gameObject.SetActive(canRetry);
            if (canRetry)
                _bcResetRetryNote.text = $"Retry would send {retry.Count} command{(retry.Count == 1 ? "" : "s")}: " +
                                         string.Join(", ", retry.Select(s => s.Command));
        }
        catch (Exception ex)
        {
            LogUtils.LogWarning($"[Raphael] reset card refresh failed: {ex.Message}");
        }
        finally
        {
            // Re-flow now, not next frame, so a grown log or a form/report swap never draws over its neighbours.
            if (_bcResetRunBlock && _bcResetRunBlock.transform.root is RectTransform rootRt)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rootRt);
        }
    }

    private static string BcResetOutcome(BcResetStep s)
    {
        string muted = Theme.MutedBodyHex;
        string late = s.Late ? $" <color={muted}>(late)</color>" : "";
        return s.State switch
        {
            BcStepState.Sent => $"<color={muted}>waiting…</color>",
            BcStepState.Ok => "<color=#90EE90>reset</color>" + late,
            BcStepState.AssumedEmpty => $"<color={muted}>no reply: assumed nothing to reset</color>",
            BcStepState.SystemOff => $"<color={muted}>skipped: system off</color>",
            BcStepState.Failed => $"<color=#{ColorUtility.ToHtmlStringRGB(WARNING_TINT)}>failed: {Services.Guides.GuideText.Escape(s.Note)}</color>",
            BcStepState.Unknown => $"<color=#{ColorUtility.ToHtmlStringRGB(WARNING_TINT)}>unknown: {Services.Guides.GuideText.Escape(s.Note)}</color>",
            BcStepState.NotMet => $"<color={muted}>not met: player offline or no familiar out</color>",
            _ => "",
        };
    }

    /// <summary>Called right after the reset card in BuildAdminTab: the other Bloodcraft admin forms go inside the
    /// returned group, which is disabled while a reset runs.</summary>
    private GameObject BeginBcResetFormsLock(GameObject page)
    {
        _bcResetFormsLockNote = AddInfoLabel(page, "BcResetFormsLockNote",
            $"<color=#{ColorUtility.ToHtmlStringRGB(WARNING_TINT)}>A progress reset is running. These forms are paused until it ends.</color>",
            FontStyles.Italic, Theme.ScaledUI(11));
        var group = UIFactory.CreateVerticalGroup(page, "BcResetFormsLock",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: Vector4.zero);
        UIFactory.SetLayoutElement(group, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 0, flexibleHeight: 0);
        _bcResetFormsLock = group.AddComponent<CanvasGroup>();
        ApplyBcResetFormsLock();
        return group;
    }

    private void ApplyBcResetFormsLock()
    {
        bool locked = BcResetRunner.IsRunning;
        if (_bcResetFormsLock)
        {
            _bcResetFormsLock.interactable = !locked;
            _bcResetFormsLock.blocksRaycasts = !locked;
            _bcResetFormsLock.alpha = locked ? 0.45f : 1f;
        }
        if (_bcResetFormsLockNote) _bcResetFormsLockNote.gameObject.SetActive(locked);
    }
}
