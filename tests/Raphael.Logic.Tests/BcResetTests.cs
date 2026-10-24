using System.Text;
using Raphael.Services.Bloodcraft;

// 0.73.0: "Reset Bloodcraft progress" core (Services/Bloodcraft/BcReset.cs), PLAN.md §2 and its Tests list. Reply
// texts are Bloodcraft 1.13.26's, color tags included, exactly as the server sends them.
internal static partial class Program
{
    // VCF.Core/Common/Utility.cs GetParts, ported verbatim: how the server splits what Raphael sends.
    private static List<string> VcfParts(string input)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(input)) return parts;
        bool inQuotes = false;
        var sb = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            char ch = input[i];
            if (ch == '\\' && i + 1 < input.Length && input[i + 1] == '"') { sb.Append('"'); i++; continue; }
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ' ' && !inQuotes) { if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); } }
            else sb.Append(ch);
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts;
    }

    private static string PrestigeOk(string type, string name) => $"<color=#90EE90>{type}</color> prestige reset for <color=white>{name}</color>.";
    private static string WepOk(string w, string name, int lvl = 0) => $"<color=#c0c0c0>{w}</color> expertise set to [<color=white>{lvl}</color>] for <color=green>{name}</color>";
    private static string BlOk(string b, string name, int lvl = 0) => $"<color=red>{b}</color> legacy set to [<color=white>{lvl}</color>] for <color=green>{name}</color>";
    private static string ProfOk(string p, string name, int lvl = 0) => $"<color=#7E22CE>{p}</color> set to [<color=white>{lvl}</color>] for <color=green>{name}</color>";
    private static string LvlOk(int lvl, string name) => $"Level set to <color=white>{lvl}</color> for <color=green>{name}</color>!";
    private static string QuestOk(string name) => $"Quests for <color=green>{name}</color> have been refreshed.";
    private static string FamOk(string name, int lvl = 1) => $"Active familiar for <color=green>{name}</color> has been set to level <color=white>{lvl}</color>.";

    private static BcResetOptions Only(BcSystem s, int level = 10) => new()
    {
        Prestige = s == BcSystem.Prestige, Weapons = s == BcSystem.Weapons, Legacies = s == BcSystem.Legacies,
        Professions = s == BcSystem.Professions, Level = s == BcSystem.Level, LevelValue = level,
        Quests = s == BcSystem.Quests, Familiar = s == BcSystem.Familiar,
    };

    private static void ResetTests()
    {
        ResetNames();
        ResetPlans();
        ResetClassifier();
        ResetRunOkPath();
        ResetRunPauses();
        ResetRunLateAndRetry();
    }

    private static void ResetNames()
    {
        Bad(BcResetPlan.ValidateName("", out _), "name: empty");
        Bad(BcResetPlan.ValidateName("   ", out _), "name: whitespace only");
        Bad(BcResetPlan.ValidateName(null, out _), "name: null");
        Ok(BcResetPlan.ValidateName(new string('a', 20), out _), "name: 20 characters");
        Bad(BcResetPlan.ValidateName(new string('a', 21), out _), "name: 21 characters");
        foreach (var bad in new[] { "Bo\"b", "Bo\\b", "Bo<b", "Bo>b", "Bo\tb", "Bo\nb", "Bo\u0001b" })
            Bad(BcResetPlan.ValidateName(bad, out _), $"name: refuses {bad.Replace("\n", "\\n").Replace("\t", "\\t")}");
        Ok(BcResetPlan.ValidateName("  Bob  ", out var trimmed), "name: padded");
        Eq(trimmed, "Bob", "name: trimmed");
        Ok(BcResetPlan.ValidateName("Bob Smith", out var spaced), "name: internal space");

        Eq(BcResetPlan.Arg("Bob"), "Bob", "name: no space → bare");
        Eq(BcResetPlan.Arg(spaced), "\"Bob Smith\"", "name: space → quoted");

        // Through VCF's own splitter: the name lands as one argument in both forms.
        var bare = VcfParts(BcResetPlan.Build("Bob", new BcResetOptions())[0].Command);
        Eq(string.Join("|", bare), ".prestige|r|Bob|Experience", "vcf split: bare name");
        var quoted = VcfParts(BcResetPlan.Build("Bob Smith", new BcResetOptions())[28].Command);
        Eq(string.Join("|", quoted), ".wep|set|Bob Smith|Sword|0", "vcf split: quoted name");
        var lvl = VcfParts(BcResetPlan.Build("Ann  Lee", Only(BcSystem.Level, 7))[0].Command);
        Eq(string.Join("|", lvl), ".lvl|set|Ann  Lee|7", "vcf split: double space kept inside quotes");
    }

    private static void ResetPlans()
    {
        var all = BcResetPlan.Build("Bob", new BcResetOptions());
        Eq(all.Count, 63, "plan: default is 63 commands");
        Eq(all.Count(s => s.System == BcSystem.Prestige), 28, "plan: prestige 28");
        Eq(all.Count(s => s.System == BcSystem.Weapons), 16, "plan: weapons 16");
        Eq(all.Count(s => s.System == BcSystem.Legacies), 10, "plan: legacies 10");
        Eq(all.Count(s => s.System == BcSystem.Professions), 8, "plan: professions 8");
        Eq(all[0].Command, ".prestige r Bob Experience", "plan: first step");
        Eq(all[27].Command, ".prestige r Bob CorruptionLegacy", "plan: last prestige");
        Eq(all[28].Command, ".wep set Bob Sword 0", "plan: first weapon");
        Eq(all[44].Command, ".bl set Bob Worker 0", "plan: first legacy");
        Eq(all[54].Command, ".prof set Bob Enchanting 0", "plan: first profession");
        Eq(all[62].Command, ".lvl set Bob 10", "plan: level default 10");
        Check(all.Select((s, i) => s.Index == i).All(x => x), "plan: indexes in order");
        Check(all.All(s => s.State == BcStepState.Pending), "plan: all pending");
        Eq(BcResetPlan.Describe("Bob", all), "63 commands for Bob: prestige 28, weapons 16, legacies 10, professions 8, level 1", "plan: review line");

        var opts = new BcResetOptions { Quests = true, Familiar = true, LevelValue = 0 };
        var full = BcResetPlan.Build("Bob", opts);
        Eq(full.Count, 65, "plan: with both optional steps");
        Eq(full[62].Command, ".lvl set Bob 0", "plan: level 0 allowed");
        Eq(full[63].Command, ".quest rf Bob", "plan: quest refresh");
        Eq(full[64].Command, ".fam sl Bob 1", "plan: familiar to 1");
        Check(!full[63].Idempotent && full[64].Idempotent && full[0].Idempotent, "plan: only quest refresh is not idempotent");

        Eq(BcResetPlan.Build("Bob", Only(BcSystem.Weapons)).Count, 16, "plan: weapons only");
        var none = BcResetPlan.Build("Bob", new BcResetOptions { Prestige = false, Weapons = false, Legacies = false, Professions = false, Level = false });
        Eq(none.Count, 0, "plan: nothing ticked → empty");
        Eq(BcResetPlan.Describe("Bob", none), "Pick at least one thing to reset.", "plan: empty review line");
        bool threw = false;
        try { _ = new BcResetRun("Bob", none); } catch (ArgumentException) { threw = true; }
        Check(threw, "plan: an empty plan can't start");

        Bad(BcResetPlan.ValidateLevel("", null, out _), "level: empty");
        Bad(BcResetPlan.ValidateLevel("-1", null, out _), "level: negative");
        Bad(BcResetPlan.ValidateLevel("ten", null, out _), "level: non-numeric");
        Bad(BcResetPlan.ValidateLevel("1.5", null, out _), "level: decimal");
        Bad(BcResetPlan.ValidateLevel("91", 90, out _), "level: over server max");
        Ok(BcResetPlan.ValidateLevel("90", 90, out var l90), "level: at server max");
        Eq(l90, 90, "level: parsed");
        Ok(BcResetPlan.ValidateLevel("0", 90, out _), "level: 0");
        Ok(BcResetPlan.ValidateLevel("1000", null, out _), "level: 1000 with unknown max");
        Bad(BcResetPlan.ValidateLevel("1001", null, out _), "level: 1001 with unknown max");
    }

    private static void ResetClassifier()
    {
        var steps = BcResetPlan.Build("Bob", new BcResetOptions { Quests = true, Familiar = true });
        BcReply C(string line) => BcResetReplies.Classify(steps, "Bob", line);
        void IsOk(string line, int idx, string what) { var r = C(line); Check(r.Kind == BcReplyKind.Ok && r.StepIndex == idx, $"classify ok: {what} (got {r.Kind} #{r.StepIndex})"); }
        void Is(string line, BcReplyKind k, string what) => Eq(C(line).Kind, k, $"classify: {what}");

        IsOk(PrestigeOk("Experience", "Bob"), 0, "prestige Experience");
        IsOk(PrestigeOk("ClawsExpertise", "bob"), 17, "prestige, name case ignored");
        IsOk(WepOk("GreatSword", "Bob"), 33, "weapon GreatSword");
        IsOk(BlOk("Corruption", "Bob"), 53, "legacy Corruption");
        IsOk(ProfOk("Fishing", "Bob"), 61, "profession Fishing");
        IsOk(LvlOk(10, "Bob"), 62, "level");
        IsOk(QuestOk("Bob"), 63, "quests");
        IsOk(FamOk("Bob"), 64, "familiar");

        Is(WepOk("Sword", "Alice"), BcReplyKind.Unrelated, "same type, another player");
        Is(PrestigeOk("Experience", "Alice"), BcReplyKind.Unrelated, "prestige, another player");
        Is(WepOk("Sword", "Bob", 5), BcReplyKind.Unrelated, "same player, another value");
        Is(LvlOk(30, "Bob"), BcReplyKind.Unrelated, "level, another value");
        Is(FamOk("Bob", 50), BcReplyKind.Unrelated, "familiar, another level");
        var weaponsOnly = BcResetPlan.Build("Bob", Only(BcSystem.Weapons));
        Eq(BcResetReplies.Classify(weaponsOnly, "Bob", BlOk("Worker", "Bob")).Kind, BcReplyKind.Unrelated, "classify: system not in plan");
        Is("<color=#90EE90>Exo</color> prestiging is not enabled.", BcReplyKind.SystemOff, "colored Exo off");

        var spaced = BcResetPlan.Build("Bob Smith", new BcResetOptions());
        var sr = BcResetReplies.Classify(spaced, "Bob Smith", WepOk("Axe", "Bob Smith"));
        Check(sr.Kind == BcReplyKind.Ok && sr.StepIndex == 29, "classify: name with a space");

        void Off(string line, BcSystem sys, bool exo = false)
        {
            var r = C(line);
            Check(r.Kind == BcReplyKind.SystemOff && r.System == sys && r.ExoOnly == exo, $"classify system off: \"{line}\" (got {r.Kind} {r.System} exo={r.ExoOnly})");
        }
        Off("Prestiging is not enabled.", BcSystem.Prestige);
        Off("Exo prestiging is not enabled.", BcSystem.Prestige, exo: true);
        Off("Expertise is not enabled.", BcSystem.Weapons);
        Off("Blood Legacies are not enabled.", BcSystem.Legacies);
        Off("Professions are not enabled.", BcSystem.Professions);
        Off("Leveling is not enabled.", BcSystem.Level);
        Off("Quests are not enabled.", BcSystem.Quests);
        Off("Familiars are not enabled.", BcSystem.Familiar);

        Is("Couldn't find player.", BcReplyKind.PlayerNotFound, "player not found");
        Is("Couldn't find player...", BcReplyKind.PlayerNotFound, "player not found (prestige)");
        Is("<color=red>[denied]</color> <color=#FFD700>prestige reset</color>", BcReplyKind.Denied, "VCF denied");
        Is("Level must be between 0 and 100.", BcReplyKind.Invalid, "level range");
        Is("Level must be between <color=white>0</color> and <color=white>90</color>!", BcReplyKind.Invalid, "lvl range, colored");
        Is("Invalid weapon type.", BcReplyKind.Invalid, "invalid weapon");
        Is("Invalid blood legacy.", BcReplyKind.Invalid, "invalid blood");
        Is("Invalid profession.", BcReplyKind.Invalid, "invalid profession");
        Is("Invalid prestige, use <color=white>'.prestige l'</color> to see valid options.", BcReplyKind.Invalid, "invalid prestige");
        Is("Couldn't find active familiar...", BcReplyKind.PreconditionNotMet, "no familiar out");
        var noXp = C("Couldn't find experience data for Bob");
        Check(noXp.Kind == BcReplyKind.Invalid && noXp.Named && noXp.System == BcSystem.Level, "classify: no experience data names the player");
        Is("Couldn't find experience data for Alice", BcReplyKind.Unrelated, "no experience data, another player");
        Is("Welcome to the server!", BcReplyKind.Unrelated, "unrelated line");
        Is("", BcReplyKind.Unrelated, "empty line");
    }

    // Drive the run: tick at t and return what it sent.
    private static string T(BcResetRun run, ref double t, double dt = 0.3) { t += dt; return run.Tick(t); }

    private static void ResetRunOkPath()
    {
        var steps = BcResetPlan.Build("Bob", new BcResetOptions { Prestige = false, Legacies = false, Professions = false, Level = false });
        var run = new BcResetRun("Bob", steps);
        double t = 0;
        Eq(run.Tick(t), ".wep set Bob Sword 0", "run: first send at once");
        Eq(run.Tick(t + 0.1), null, "run: nothing more while waiting");
        Check(run.HandleLine(WepOk("Sword", "Bob"), t + 0.1), "run: reply consumed");
        Eq(run.Tick(t + 0.2), null, "run: min gap holds the next send");
        Eq(run.Tick(t + 0.26), ".wep set Bob Axe 0", "run: next after the gap");
        t += 0.26;
        for (int i = 1; i < 16; i++)
        {
            run.HandleLine(WepOk(BcResetPlan.Weapons[i], "Bob"), t);
            var sent = T(run, ref t);
            if (i < 15) Eq(sent, $".wep set Bob {BcResetPlan.Weapons[i + 1]} 0", $"run: weapon {i + 1}");
        }
        T(run, ref t);
        Eq(run.State, BcRunState.Finished, "run: finished");
        Eq(run.Count(BcStepState.Ok), 16, "run: all ok");
        Check(run.Summary().StartsWith("Finished Bob: 16 reset"), $"run: summary ({run.Summary()})");

        // Silent prestige → assumed nothing; no pause.
        var pr = new BcResetRun("Bob", BcResetPlan.Build("Bob", Only(BcSystem.Prestige)));
        t = 0;
        Eq(pr.Tick(t), ".prestige r Bob Experience", "prestige: first");
        Eq(pr.Tick(t + 1.4), null, "prestige: still waiting at 1.4 s");
        Eq(pr.Tick(t + 1.5), ".prestige r Bob Exo", "prestige: silence → next step, no pause");
        Eq(pr.Steps[0].State, BcStepState.AssumedEmpty, "prestige: silence → assumed nothing");

        // SystemOff on the step in flight skips the rest of that system, and only that system.
        var mix = new BcResetRun("Bob", BcResetPlan.Build("Bob", new BcResetOptions { Prestige = false, Weapons = false, Legacies = false }));
        t = 0;
        Eq(mix.Tick(t), ".prof set Bob Enchanting 0", "off: first profession");
        Check(mix.HandleLine("Professions are not enabled.", t + 0.1), "off: consumed");
        Check(mix.Steps.Take(8).All(s => s.State == BcStepState.SystemOff), "off: every profession skipped");
        Eq(T(mix, ref t), ".lvl set Bob 10", "off: next system goes on");
        mix.HandleLine(LvlOk(10, "Bob"), t);
        T(mix, ref t);
        Check(mix.Summary().Contains("8 skipped, system off (professions)"), $"off: summary ({mix.Summary()})");

        // "Exo prestiging is not enabled." skips only the Exo step.
        var exo = new BcResetRun("Bob", BcResetPlan.Build("Bob", Only(BcSystem.Prestige)));
        t = 0;
        exo.Tick(t);
        exo.HandleLine(PrestigeOk("Experience", "Bob"), t);
        Eq(T(exo, ref t), ".prestige r Bob Exo", "exo: sent");
        Check(exo.HandleLine("Exo prestiging is not enabled.", t), "exo: consumed");
        Eq(exo.Steps[1].State, BcStepState.SystemOff, "exo: exo step off");
        Eq(exo.Steps[2].State, BcStepState.Pending, "exo: other prestige steps still pending");
        Eq(T(exo, ref t), ".prestige r Bob SwordExpertise", "exo: prestige goes on");

        // Invalid on the step in flight → Failed, go on. Familiar not out → NotMet.
        var fam = new BcResetRun("Bob", BcResetPlan.Build("Bob", new BcResetOptions { Prestige = false, Weapons = false, Legacies = false, Professions = false, Familiar = true }));
        t = 0;
        fam.Tick(t);
        Check(fam.HandleLine("Level must be between <color=white>0</color> and <color=white>90</color>!", t), "invalid: consumed when in flight");
        Eq(fam.Steps[0].State, BcStepState.Failed, "invalid: level step failed");
        Eq(T(fam, ref t), ".fam sl Bob 1", "invalid: goes on");
        Check(fam.HandleLine("Couldn't find active familiar...", t), "not met: consumed");
        Eq(fam.Steps[1].State, BcStepState.NotMet, "not met: recorded");
        T(fam, ref t);
        Eq(fam.State, BcRunState.Finished, "not met: run finished");
        // A "no familiar" line is never pinned on a non-familiar step.
        var lv = new BcResetRun("Bob", BcResetPlan.Build("Bob", Only(BcSystem.Level)));
        lv.Tick(0);
        Check(!lv.HandleLine("Couldn't find active familiar...", 0.1), "not met: ignored on the level step");
        Eq(lv.Steps[0].State, BcStepState.Sent, "not met: level step untouched");
    }

    private static void ResetRunPauses()
    {
        double t = 0;
        // Silent set → pause; nothing sent while paused; Retry resends the same step.
        var run = new BcResetRun("Bob", BcResetPlan.Build("Bob", Only(BcSystem.Weapons)));
        Eq(run.Tick(t), ".wep set Bob Sword 0", "pause: sent");
        Eq(run.Tick(3.9), null, "pause: waiting at 3.9 s");
        Eq(run.Tick(4.0), null, "pause: timeout sends nothing");
        Check(run.State == BcRunState.Paused && run.Pause == BcPause.NoReply, "pause: no reply pauses");
        for (double s = 4.1; s < 20; s += 0.5) Check(run.Tick(s) == null, $"pause: nothing sent at {s}");
        Check(run.RetryStep(20), "pause: retry accepted");
        Eq(run.Tick(20.3), ".wep set Bob Sword 0", "pause: retry resends the same step");

        // Skip → Unknown, go on.
        run.Tick(25);
        Check(run.Skip(25), "pause: skip accepted");
        Eq(run.Steps[0].State, BcStepState.Unknown, "pause: skipped step is unknown");
        Eq(run.Tick(25.3), ".wep set Bob Axe 0", "pause: goes on after skip");

        // A nameless refusal during the pause is "possibly related" and changes nothing.
        run.Tick(30);
        Eq(run.Pause, BcPause.NoReply, "pause: second timeout");
        Check(!run.HandleLine("Invalid weapon type.", 30.1), "pause: nameless line not consumed");
        Eq(run.Steps[1].State, BcStepState.Sent, "pause: nameless line changes no record");
        Eq(run.PossiblyRelated.Count, 1, "pause: shown as possibly related");
        // A correlated success ends the pause by itself.
        Check(run.HandleLine(WepOk("Axe", "Bob"), 30.2), "pause: correlated success consumed");
        Eq(run.State, BcRunState.Running, "pause: correlated success resumes");
        Eq(run.Steps[1].State, BcStepState.Ok, "pause: step ok");
        Eq(run.Steps[1].Late, false, "pause: in-flight success isn't late");
        Eq(run.Tick(30.5), ".wep set Bob Mace 0", "pause: next step");

        // Stop: no sends after cancel; the step in flight is unknown.
        run.Stop(31, "Stopped by you.");
        Eq(run.State, BcRunState.Stopped, "stop: stopped");
        Eq(run.Steps[2].State, BcStepState.Unknown, "stop: in-flight step unknown");
        for (double s = 31; s < 40; s += 0.3) Check(run.Tick(s) == null, $"stop: nothing sent at {s:0.0}");
        Check(run.Summary().Contains("not sent") && run.Summary().Contains("Stopped by you."), $"stop: summary ({run.Summary()})");

        // Timed-out quest refresh: plain retry refused; the warned re-roll works.
        var q = new BcResetRun("Bob", BcResetPlan.Build("Bob", Only(BcSystem.Quests)));
        q.Tick(0);
        q.Tick(4);
        Check(!q.RetryStep(5), "quest: plain retry refused");
        Check(q.RetryStep(5, confirmReroll: true), "quest: confirmed re-roll accepted");
        Eq(q.Tick(5.3), ".quest rf Bob", "quest: re-roll sent");

        // PlayerNotFound / Denied pause; Stop and Continue.
        var nf = new BcResetRun("Bob", BcResetPlan.Build("Bob", Only(BcSystem.Weapons)));
        nf.Tick(0);
        Check(!nf.HandleLine("Couldn't find player.", 0.1), "not found: line stays in chat");
        Check(nf.State == BcRunState.Paused && nf.Pause == BcPause.PlayerNotFound, "not found: pauses");
        Eq(nf.PauseLine, "Couldn't find player.", "not found: line shown");
        Eq(nf.Tick(10), null, "not found: nothing sent while paused");
        Check(nf.Continue(10), "not found: continue");
        Eq(nf.State, BcRunState.WaitingReply, "not found: continue waits again for the step in flight");
        nf.HandleLine(WepOk("Sword", "Bob"), 10.1);
        Eq(nf.Tick(10.4), ".wep set Bob Axe 0", "not found: goes on");

        var dn = new BcResetRun("Bob", BcResetPlan.Build("Bob", Only(BcSystem.Weapons)));
        dn.Tick(0);
        dn.HandleLine("<color=red>[denied]</color> <color=#FFD700>weapon set</color>", 0.1);
        Eq(dn.Pause, BcPause.Denied, "denied: pauses");
        dn.Stop(1, null);
        Eq(dn.State, BcRunState.Stopped, "denied: stop");
        Eq(dn.Tick(2), null, "denied: nothing after stop");

        // Admin lost / disconnect (the runner calls Stop with its reason): no sends after.
        var lost = new BcResetRun("Bob", BcResetPlan.Build("Bob", Only(BcSystem.Weapons)));
        lost.Tick(0);
        lost.HandleLine(WepOk("Sword", "Bob"), 0.1);
        lost.Stop(0.2, "You are no longer admin.");
        Eq(lost.Tick(0.5), null, "admin lost: nothing sent");
        Eq(lost.Steps[1].State, BcStepState.Pending, "admin lost: unsent steps stay not sent");
        Check(lost.Summary().Contains("You are no longer admin."), "admin lost: reason in summary");
    }

    private static void ResetRunLateAndRetry()
    {
        double t = 0;
        // Late reply after a skip updates the earlier step and doesn't advance the current one.
        var run = new BcResetRun("Bob", BcResetPlan.Build("Bob", Only(BcSystem.Weapons)));
        run.Tick(0);
        run.Tick(4);
        run.Skip(4);
        Eq(run.Tick(4.3), ".wep set Bob Axe 0", "late: next step sent");
        Check(run.HandleLine(WepOk("Sword", "Bob"), 4.5), "late: earlier step's reply consumed");
        Eq(run.Steps[0].State, BcStepState.Ok, "late: earlier step now ok");
        Check(run.Steps[0].Late, "late: marked late");
        Eq(run.State, BcRunState.WaitingReply, "late: current step still waiting");
        Eq(run.Steps[1].State, BcStepState.Sent, "late: current step not advanced");
        // Duplicate reply.
        Check(run.HandleLine(WepOk("Sword", "Bob"), 4.6), "dup: consumed");
        Eq(run.Count(BcStepState.Ok), 1, "dup: counted once");
        // A matching-looking line for a step not sent yet (someone else's command) isn't ours.
        Check(!run.HandleLine(WepOk("Claws", "Bob"), 4.7), "not sent: not consumed");
        Eq(run.Steps[15].State, BcStepState.Pending, "not sent: record unchanged");

        // Retry list: failed + unknown, recomputed when asked; a late success drops out.
        var r2 = new BcResetRun("Bob", BcResetPlan.Build("Bob", new BcResetOptions { Prestige = false, Legacies = false, Professions = false, Level = false, Quests = true }));
        t = 0;
        r2.Tick(t);                                   // Sword
        r2.Tick(4); r2.Skip(4);                       // Sword unknown
        r2.Tick(4.3);                                 // Axe
        r2.HandleLine("Invalid weapon type.", 4.4);   // Axe failed
        t = 4.4;
        for (int i = 2; i < 16; i++) { T(r2, ref t); r2.HandleLine(WepOk(BcResetPlan.Weapons[i], "Bob"), t); }
        Eq(T(r2, ref t), ".quest rf Bob", "retry: quest refresh sent");
        r2.Tick(t + 4); r2.Skip(t + 4);               // quest unknown
        T(r2, ref t); t += 4; T(r2, ref t);
        Eq(r2.State, BcRunState.Finished, "retry: finished");
        var list = r2.RetryList(includeReroll: false);
        Eq(string.Join(",", list.Select(s => s.Command)), ".wep set Bob Sword 0,.wep set Bob Axe 0", "retry: failed + unknown, quest left out");
        Check(list.All(s => s.State == BcStepState.Pending) && list[1].Index == 1, "retry: fresh pending steps");
        Check(r2.HasUnknownReroll, "retry: unknown re-roll flagged");
        Eq(r2.RetryList(includeReroll: true).Count, 3, "retry: quest included when ticked");
        // Late confirmation within 30 s after finishing: record changes, run stays finished, note added.
        Check(r2.HandleLine(WepOk("Sword", "Bob"), t + 1), "late after finish: consumed");
        Eq(r2.State, BcRunState.Finished, "late after finish: still finished");
        Eq(r2.LateNotes.Count, 1, "late after finish: note");
        Eq(r2.RetryList(false).Count, 1, "late after finish: drops out of retry");
        // Sealed after 30 s.
        r2.Tick(t + 31);
        Check(r2.Sealed, "seal: sealed after 30 s");
        Check(!r2.HandleLine(WepOk("Axe", "Bob"), t + 32), "seal: late reply ignored");
        Eq(r2.Steps[1].State, BcStepState.Failed, "seal: record unchanged");

        // Post-build review: a late "not enabled" turns silence-assumed prestige steps into system off.
        var pl = new BcResetRun("Bob", BcResetPlan.Build("Bob", Only(BcSystem.Prestige)));
        pl.Tick(0); pl.Tick(1.5);                    // Experience assumed empty, Exo sent
        Check(pl.HandleLine("Prestiging is not enabled.", 1.6), "late off: consumed");
        Eq(pl.Steps[0].State, BcStepState.SystemOff, "late off: assumed step corrected");
        Eq(pl.Steps[5].State, BcStepState.SystemOff, "late off: pending steps skipped");

        // Post-build review: leaving the server keeps a frozen report and withholds Retry.
        var dt = new BcResetRun("Bob", BcResetPlan.Build("Bob", Only(BcSystem.Weapons)));
        dt.Tick(0);
        dt.Detach(1, "Stopped: you left the server.");
        Eq(dt.State, BcRunState.Stopped, "detach: stopped");
        Eq(dt.EndReason, "Stopped: you left the server.", "detach: reason kept");
        Check(dt.Sealed, "detach: sealed at once");
        Eq(dt.Steps[0].State, BcStepState.Unknown, "detach: in-flight step unknown");
        Eq(dt.RetryList(true).Count, 0, "detach: no retry");
        Check(!dt.HasUnknownReroll, "detach: no re-roll toggle");
        Check(!dt.HandleLine(WepOk("Sword", "Bob"), 2), "detach: later lines ignored");

        // Post-build review round 2: a command that couldn't be sent is failed (retryable), and the run stops.
        var sf = new BcResetRun("Bob", BcResetPlan.Build("Bob", Only(BcSystem.Weapons)));
        sf.Tick(0);
        sf.SendFailed(0);
        Eq(sf.State, BcRunState.Stopped, "send failed: stopped");
        Eq(sf.Steps[0].State, BcStepState.Failed, "send failed: step failed, not unknown");
        Eq(sf.RetryList(false).Count, 1, "send failed: offered for retry");
    }
}
