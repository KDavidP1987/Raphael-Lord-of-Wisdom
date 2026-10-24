using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Raphael.Services.Bloodcraft;

// 0.73.0: "Reset Bloodcraft progress" (Bloodcraft → Admin). Bloodcraft has no single reset command, so this sends its
// admin commands one after another and reads each reply. Pure: no Unity, no game types (time is passed in), so the
// logic tests link it. BcResetRunner drives it from CoreUpdateBehavior. PLAN.md §2 is the spec; reply texts and enum
// lists are Bloodcraft 1.13.26's (Commands/*Commands.cs, Interfaces/*Interface.cs).

internal enum BcSystem { Prestige, Weapons, Legacies, Professions, Level, Quests, Familiar }

internal enum BcStepState
{
    Pending,
    Sent,
    Ok,
    /// <summary>Silent prestige reset: Bloodcraft says nothing when the player never had that prestige.</summary>
    AssumedEmpty,
    SystemOff,
    Failed,
    Unknown,
    /// <summary>Familiar step: the player is offline or has no familiar out.</summary>
    NotMet,
}

internal sealed class BcResetStep
{
    public int Index;
    public BcSystem System;
    /// <summary>The Bloodcraft enum name the step targets ("SwordExpertise", "Sword", "Worker", "Enchanting"); null
    /// for level / quests / familiar.</summary>
    public string Type;
    public int Value;
    public string Command;
    public BcStepState State;
    /// <summary>Why it ended the way it did (a refusal line, "no reply", …). Shown in the step log.</summary>
    public string Note;
    /// <summary>A reply that arrived after the step had been given up on (late confirmation).</summary>
    public bool Late;

    /// <summary>Safe to send twice: set to 0, reset to 0, familiar to level 1. Quest refresh re-rolls again.</summary>
    public bool Idempotent => System != BcSystem.Quests;

    public BcResetStep Clone() => new()
    {
        Index = Index, System = System, Type = Type, Value = Value, Command = Command, State = BcStepState.Pending,
    };
}

internal sealed class BcResetOptions
{
    public bool Prestige = true;
    public bool Weapons = true;
    public bool Legacies = true;
    public bool Professions = true;
    public bool Level = true;
    public int LevelValue = 10;
    public bool Quests;
    public bool Familiar;
}

internal static class BcResetPlan
{
    public static readonly string[] PrestigeTypes =
    {
        "Experience", "Exo",
        "SwordExpertise", "AxeExpertise", "MaceExpertise", "SpearExpertise", "CrossbowExpertise", "GreatSwordExpertise",
        "SlashersExpertise", "PistolsExpertise", "ReaperExpertise", "LongbowExpertise", "WhipExpertise", "UnarmedExpertise",
        "FishingPoleExpertise", "TwinBladesExpertise", "DaggersExpertise", "ClawsExpertise",
        "WorkerLegacy", "WarriorLegacy", "ScholarLegacy", "RogueLegacy", "MutantLegacy", "DraculinLegacy", "ImmortalLegacy",
        "CreatureLegacy", "BruteLegacy", "CorruptionLegacy",
    };

    public static readonly string[] Weapons =
    {
        "Sword", "Axe", "Mace", "Spear", "Crossbow", "GreatSword", "Slashers", "Pistols", "Reaper", "Longbow", "Whip",
        "Unarmed", "FishingPole", "TwinBlades", "Daggers", "Claws",
    };

    /// <summary>The blood types that carry a legacy (BloodType minus VBlood / None / GateBoss).</summary>
    public static readonly string[] Bloods =
    {
        "Worker", "Warrior", "Scholar", "Rogue", "Mutant", "Draculin", "Immortal", "Creature", "Brute", "Corruption",
    };

    public static readonly string[] Professions =
    {
        "Enchanting", "Alchemy", "Harvesting", "Blacksmithing", "Tailoring", "Woodcutting", "Mining", "Fishing",
    };

    public const int MaxNameLength = 20;

    /// <summary>Null when <paramref name="raw"/> is a usable player name (trimmed into <paramref name="name"/>);
    /// otherwise the reason. Only characters that change how VCF splits the command, or that can't be in a name,
    /// are refused. A name with spaces is allowed and sent quoted (see <see cref="Arg"/>).</summary>
    public static string ValidateName(string raw, out string name)
    {
        name = (raw ?? "").Trim();
        if (name.Length == 0) return "Enter a player name.";
        if (name.Length > MaxNameLength) return $"Names are at most {MaxNameLength} characters.";
        foreach (var ch in name)
        {
            if (ch == '"' || ch == '\\') return "Names can't contain quotes or backslashes.";
            if (ch == '<' || ch == '>') return "Names can't contain < or >.";
            if (char.IsControl(ch)) return "Names can't contain tabs, line breaks or control characters.";
        }
        return null;
    }

    /// <summary>The name as a VCF argument: bare, or in double quotes when it has a space (VCF reads a quoted run as
    /// one argument).</summary>
    public static string Arg(string name) => name.IndexOf(' ') >= 0 ? $"\"{name}\"" : name;

    /// <summary>Null when <paramref name="raw"/> is a valid level (into <paramref name="level"/>); otherwise the reason.
    /// <paramref name="maxLevel"/> is the server's MaxLevel when known (ConfigsToClient), else 1000 is the ceiling and
    /// the server has the final say.</summary>
    public static string ValidateLevel(string raw, int? maxLevel, out int level)
    {
        level = 0;
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return "Enter a level.";
        if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out level))
            return s.StartsWith("-") ? "The level can't be negative." : "The level must be a whole number.";
        int max = maxLevel is > 0 ? maxLevel.Value : 1000;
        if (level > max) return maxLevel is > 0 ? $"This server's max level is {max}." : $"The level must be {max} or less.";
        return null;
    }

    public static List<BcResetStep> Build(string name, BcResetOptions o)
    {
        var steps = new List<BcResetStep>();
        string p = Arg(name);
        void Add(BcSystem system, string type, int value, string command) =>
            steps.Add(new BcResetStep { Index = steps.Count, System = system, Type = type, Value = value, Command = command });

        if (o.Prestige)
            foreach (var t in PrestigeTypes) Add(BcSystem.Prestige, t, 0, $".prestige r {p} {t}");
        if (o.Weapons)
            foreach (var w in Weapons) Add(BcSystem.Weapons, w, 0, $".wep set {p} {w} 0");
        if (o.Legacies)
            foreach (var b in Bloods) Add(BcSystem.Legacies, b, 0, $".bl set {p} {b} 0");
        if (o.Professions)
            foreach (var pr in Professions) Add(BcSystem.Professions, pr, 0, $".prof set {p} {pr} 0");
        if (o.Level)
            Add(BcSystem.Level, null, o.LevelValue, $".lvl set {p} {o.LevelValue.ToString(CultureInfo.InvariantCulture)}");
        if (o.Quests)
            Add(BcSystem.Quests, null, 0, $".quest rf {p}");
        if (o.Familiar)
            Add(BcSystem.Familiar, null, 1, $".fam sl {p} 1");
        return steps;
    }

    /// <summary>"63 commands for Bob: prestige 28, weapons 16, legacies 10, professions 8, level 1".</summary>
    public static string Describe(string name, IReadOnlyList<BcResetStep> steps)
    {
        if (steps.Count == 0) return "Pick at least one thing to reset.";
        var parts = steps.GroupBy(s => s.System).Select(g => $"{Label(g.Key)} {g.Count()}");
        return $"{steps.Count} command{(steps.Count == 1 ? "" : "s")} for {name}: {string.Join(", ", parts)}";
    }

    public static string Label(BcSystem s) => s switch
    {
        BcSystem.Prestige => "prestige",
        BcSystem.Weapons => "weapons",
        BcSystem.Legacies => "legacies",
        BcSystem.Professions => "professions",
        BcSystem.Level => "level",
        BcSystem.Quests => "quests",
        _ => "familiar",
    };
}

internal enum BcReplyKind { Unrelated, Ok, SystemOff, PlayerNotFound, Denied, Invalid, PreconditionNotMet }

internal readonly struct BcReply
{
    public readonly BcReplyKind Kind;
    /// <summary>Ok: the step it confirms. Otherwise -1.</summary>
    public readonly int StepIndex;
    /// <summary>SystemOff: the system it names.</summary>
    public readonly BcSystem System;
    /// <summary>SystemOff for Exo only ("Exo prestiging is not enabled.").</summary>
    public readonly bool ExoOnly;
    /// <summary>Invalid replies that name the player (level step's "Couldn't find experience data for X").</summary>
    public readonly bool Named;

    public BcReply(BcReplyKind kind, int stepIndex = -1, BcSystem system = default, bool exoOnly = false, bool named = false)
    { Kind = kind; StepIndex = stepIndex; System = system; ExoOnly = exoOnly; Named = named; }
}

internal static class BcResetReplies
{
    private static readonly Regex Tag = new(@"<[^<>]*>");
    private static readonly Regex Prestige = new(@"^(\w+) prestige reset for (.+)\.$");
    private static readonly Regex Weapon = new(@"^(\w+) expertise set to \[(\d+)\] for (.+)$");
    private static readonly Regex Blood = new(@"^(\w+) legacy set to \[(\d+)\] for (.+)$");
    private static readonly Regex Profession = new(@"^(\w+) set to \[(\d+)\] for (.+)$");
    private static readonly Regex Level = new(@"^Level set to (\d+) for (.+)!$");
    private static readonly Regex Quests = new(@"^Quests for (.+) have been refreshed\.$");
    private static readonly Regex Familiar = new(@"^Active familiar for (.+) has been set to level (\d+)\.$");
    private static readonly Regex NoExperience = new(@"^Couldn't find experience data for (.+)$");

    public static string Strip(string line) => Tag.Replace(line ?? "", "").Trim();

    /// <summary>Classify one inbound system line against the plan for <paramref name="player"/>. A success counts only
    /// when it names the same player and the same type (and, for a set, the same value) as a step.</summary>
    public static BcReply Classify(IReadOnlyList<BcResetStep> steps, string player, string line)
    {
        string t = Strip(line);
        if (t.Length == 0) return default;

        int Find(BcSystem system, string type, int? value, string name)
        {
            if (!SameName(name, player)) return -1;
            foreach (var s in steps)
                if (s.System == system
                    && (type == null || string.Equals(s.Type, type, StringComparison.OrdinalIgnoreCase))
                    && (value == null || s.Value == value.Value))
                    return s.Index;
            return -1;
        }

        Match m;
        int idx;
        if ((m = Prestige.Match(t)).Success && (idx = Find(BcSystem.Prestige, m.Groups[1].Value, null, m.Groups[2].Value)) >= 0)
            return new BcReply(BcReplyKind.Ok, idx);
        if ((m = Weapon.Match(t)).Success && (idx = Find(BcSystem.Weapons, m.Groups[1].Value, Int(m.Groups[2].Value), m.Groups[3].Value)) >= 0)
            return new BcReply(BcReplyKind.Ok, idx);
        if ((m = Blood.Match(t)).Success && (idx = Find(BcSystem.Legacies, m.Groups[1].Value, Int(m.Groups[2].Value), m.Groups[3].Value)) >= 0)
            return new BcReply(BcReplyKind.Ok, idx);
        if ((m = Profession.Match(t)).Success && (idx = Find(BcSystem.Professions, m.Groups[1].Value, Int(m.Groups[2].Value), m.Groups[3].Value)) >= 0)
            return new BcReply(BcReplyKind.Ok, idx);
        if ((m = Level.Match(t)).Success && (idx = Find(BcSystem.Level, null, Int(m.Groups[1].Value), m.Groups[2].Value)) >= 0)
            return new BcReply(BcReplyKind.Ok, idx);
        if ((m = Quests.Match(t)).Success && (idx = Find(BcSystem.Quests, null, null, m.Groups[1].Value)) >= 0)
            return new BcReply(BcReplyKind.Ok, idx);
        if ((m = Familiar.Match(t)).Success && (idx = Find(BcSystem.Familiar, null, Int(m.Groups[2].Value), m.Groups[1].Value)) >= 0)
            return new BcReply(BcReplyKind.Ok, idx);
        if ((m = NoExperience.Match(t)).Success && SameName(m.Groups[1].Value, player))
            return new BcReply(BcReplyKind.Invalid, named: true, system: BcSystem.Level);

        switch (t)
        {
            case "Prestiging is not enabled.": return new BcReply(BcReplyKind.SystemOff, system: BcSystem.Prestige);
            case "Exo prestiging is not enabled.": return new BcReply(BcReplyKind.SystemOff, system: BcSystem.Prestige, exoOnly: true);
            case "Expertise is not enabled.": return new BcReply(BcReplyKind.SystemOff, system: BcSystem.Weapons);
            case "Blood Legacies are not enabled.": return new BcReply(BcReplyKind.SystemOff, system: BcSystem.Legacies);
            case "Professions are not enabled.": return new BcReply(BcReplyKind.SystemOff, system: BcSystem.Professions);
            case "Leveling is not enabled.": return new BcReply(BcReplyKind.SystemOff, system: BcSystem.Level);
            case "Quests are not enabled.": return new BcReply(BcReplyKind.SystemOff, system: BcSystem.Quests);
            case "Familiars are not enabled.": return new BcReply(BcReplyKind.SystemOff, system: BcSystem.Familiar);
        }

        if (t.StartsWith("Couldn't find player", StringComparison.Ordinal)) return new BcReply(BcReplyKind.PlayerNotFound);
        if (t.IndexOf("[denied]", StringComparison.OrdinalIgnoreCase) >= 0) return new BcReply(BcReplyKind.Denied);
        if (t.StartsWith("Couldn't find active familiar", StringComparison.Ordinal)) return new BcReply(BcReplyKind.PreconditionNotMet);
        if (t.StartsWith("Level must be between", StringComparison.Ordinal)
            || t.StartsWith("Invalid prestige", StringComparison.Ordinal)
            || t.StartsWith("Invalid blood legacy", StringComparison.Ordinal)
            || t.StartsWith("Invalid weapon type", StringComparison.Ordinal)
            || t.StartsWith("Invalid profession", StringComparison.Ordinal)
            || t.IndexOf("[error]", StringComparison.OrdinalIgnoreCase) >= 0
            || t.IndexOf("parameter conversion", StringComparison.OrdinalIgnoreCase) >= 0)
            return new BcReply(BcReplyKind.Invalid);
        return default;
    }

    private static int Int(string s) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : -1;

    private static bool SameName(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
}

internal enum BcRunState { Running, WaitingReply, Paused, Finished, Stopped }

internal enum BcPause { None, NoReply, PlayerNotFound, Denied }

/// <summary>One reset run: one step on the wire at a time. Time comes from the caller (seconds, monotonic).</summary>
internal sealed class BcResetRun
{
    public const double MinGap = 0.25;
    public const double PrestigeWait = 1.5;
    public const double OtherWait = 4.0;
    /// <summary>Late replies still update a finished run for this long; then it is sealed.</summary>
    public const double SealAfter = 30.0;

    public readonly string Player;
    public readonly List<BcResetStep> Steps;
    public BcRunState State { get; private set; } = BcRunState.Running;
    public BcPause Pause { get; private set; }
    /// <summary>The refusal line that caused a PlayerNotFound / Denied pause.</summary>
    public string PauseLine { get; private set; }
    /// <summary>Nameless replies seen during a no-reply pause; shown, never attributed.</summary>
    public readonly List<string> PossiblyRelated = new();
    /// <summary>"late confirmation: …" lines recorded after the run finished.</summary>
    public readonly List<string> LateNotes = new();
    public int Current { get; private set; } = -1;
    public bool Sealed { get; private set; }
    public string EndReason { get; private set; }
    /// <summary>False after the admin left the server the run was on: the report stays, but retrying it could
    /// reach a same-named player on another server.</summary>
    public bool RetryAllowed { get; private set; } = true;

    private double _sentAt = double.NegativeInfinity;
    private double _lastSendAt = double.NegativeInfinity;
    private double _endedAt;
    private readonly HashSet<BcSystem> _off = new();
    private bool _exoOff;

    public BcResetRun(string player, List<BcResetStep> steps)
    {
        if (steps == null || steps.Count == 0) throw new ArgumentException("an empty plan can't run", nameof(steps));
        Player = player;
        Steps = steps;
    }

    public bool Active => State == BcRunState.Running || State == BcRunState.WaitingReply || State == BcRunState.Paused;
    public bool Ended => State == BcRunState.Finished || State == BcRunState.Stopped;
    public BcResetStep CurrentStep => Current >= 0 && Current < Steps.Count ? Steps[Current] : null;
    public int Done => Steps.Count(s => s.State != BcStepState.Pending && s.State != BcStepState.Sent);

    /// <summary>Per frame. Returns the command to send now, or null.</summary>
    public string Tick(double now)
    {
        if (Ended)
        {
            if (!Sealed && now - _endedAt >= SealAfter) Sealed = true;
            return null;
        }
        if (State == BcRunState.Paused) return null;

        if (State == BcRunState.WaitingReply)
        {
            var step = CurrentStep;
            if (now - _sentAt < Wait(step)) return null;
            if (step.System == BcSystem.Prestige)
            {
                step.State = BcStepState.AssumedEmpty;
                step.Note = "No reply: assumed nothing to reset.";
                State = BcRunState.Running;
            }
            else
            {
                Pause = BcPause.NoReply;
                PossiblyRelated.Clear();
                State = BcRunState.Paused;
                return null;
            }
        }

        // Running: next pending step.
        int next = Steps.FindIndex(s => s.State == BcStepState.Pending);
        if (next < 0) { End(BcRunState.Finished, now, null); return null; }
        if (now - _lastSendAt < MinGap) return null;
        var n = Steps[next];
        if (IsOff(n))
        {
            n.State = BcStepState.SystemOff;
            n.Note = "Skipped: the system is off on this server.";
            return null;
        }
        Current = next;
        n.State = BcStepState.Sent;
        _sentAt = _lastSendAt = now;
        State = BcRunState.WaitingReply;
        return n.Command;
    }

    private static double Wait(BcResetStep s) => s.System == BcSystem.Prestige ? PrestigeWait : OtherWait;

    private bool IsOff(BcResetStep s) => _off.Contains(s.System) || (_exoOff && s.System == BcSystem.Prestige && s.Type == "Exo");

    /// <summary>An inbound system line. True when the line was correlated to this run and should be hidden from chat.</summary>
    public bool HandleLine(string line, double now)
    {
        if (Sealed) return false;
        var r = BcResetReplies.Classify(Steps, Player, line);
        switch (r.Kind)
        {
            case BcReplyKind.Ok: return OnOk(Steps[r.StepIndex], line, now);
            case BcReplyKind.SystemOff: return OnSystemOff(r, line, now);
            case BcReplyKind.PlayerNotFound:
            case BcReplyKind.Denied:
                if (!Active) return false;
                if (State == BcRunState.Paused && Pause != BcPause.NoReply) return false; // already waiting on the admin
                Pause = r.Kind == BcReplyKind.Denied ? BcPause.Denied : BcPause.PlayerNotFound;
                PauseLine = BcResetReplies.Strip(line);
                State = BcRunState.Paused;
                return false; // not correlated to a step: it stays in chat too
            case BcReplyKind.Invalid:
            case BcReplyKind.PreconditionNotMet:
            {
                var cur = CurrentStep;
                if (State == BcRunState.WaitingReply && cur != null && (!r.Named || cur.System == r.System))
                {
                    if (r.Kind == BcReplyKind.PreconditionNotMet && cur.System != BcSystem.Familiar) return false;
                    cur.State = r.Kind == BcReplyKind.PreconditionNotMet ? BcStepState.NotMet : BcStepState.Failed;
                    cur.Note = r.Kind == BcReplyKind.PreconditionNotMet
                        ? "Not met: the player is offline or has no familiar out."
                        : BcResetReplies.Strip(line);
                    State = BcRunState.Running;
                    return true;
                }
                if (State == BcRunState.Paused && Pause == BcPause.NoReply)
                    PossiblyRelated.Add(BcResetReplies.Strip(line));
                return false;
            }
            default:
                return false;
        }
    }

    private bool OnOk(BcResetStep step, string line, double now)
    {
        if (step.State == BcStepState.Ok) return true; // duplicate
        if (step.State == BcStepState.Pending) return false; // not sent yet, so this reply isn't ours
        bool inFlight = step.Index == Current && step.State == BcStepState.Sent;
        step.State = BcStepState.Ok;
        step.Note = null;
        if (!inFlight || Ended)
        {
            // An earlier step given up on (assumed, unknown, failed) or a reply after the run ended: update the record
            // only, never advance the current step.
            step.Late = true;
            if (Ended) LateNotes.Add($"late confirmation: {step.Command}");
            return true;
        }
        if (State == BcRunState.WaitingReply) State = BcRunState.Running;
        else if (State == BcRunState.Paused && Pause == BcPause.NoReply) { Pause = BcPause.None; PossiblyRelated.Clear(); State = BcRunState.Running; }
        // A PlayerNotFound / Denied pause stays: the admin still chooses Stop or Continue.
        return true;
    }

    private bool OnSystemOff(BcReply r, string line, double now)
    {
        var cur = CurrentStep;
        bool touched = false;
        foreach (var s in Steps)
        {
            if (s.System != r.System) continue;
            if (r.ExoOnly && s.Type != "Exo") continue;
            // AssumedEmpty too: a late "not enabled" means the silence wasn't "nothing to reset".
            if (s.State == BcStepState.Pending || s.State == BcStepState.Sent || s.State == BcStepState.Unknown
                || s.State == BcStepState.AssumedEmpty)
            {
                if (Ended && s.State == BcStepState.Pending) continue; // never sent: stays "not sent"
                s.State = BcStepState.SystemOff;
                s.Note = BcResetReplies.Strip(line);
                if (Ended) LateNotes.Add($"late reply: {s.Command} — {s.Note}");
                touched = true;
            }
        }
        if (!touched) return false;
        if (r.ExoOnly) _exoOff = true; else _off.Add(r.System);
        if (cur != null && cur.State == BcStepState.SystemOff && Active)
        {
            if (State == BcRunState.WaitingReply) State = BcRunState.Running;
            else if (State == BcRunState.Paused && Pause == BcPause.NoReply) { Pause = BcPause.None; State = BcRunState.Running; }
        }
        return true;
    }

    // ---------------------------------------------------------------- admin choices

    /// <summary>No-reply pause → send the same step again. A quest re-roll needs <paramref name="confirmReroll"/>.</summary>
    public bool RetryStep(double now, bool confirmReroll = false)
    {
        if (State != BcRunState.Paused || Pause != BcPause.NoReply) return false;
        var s = CurrentStep;
        if (s == null) return false;
        if (!s.Idempotent && !confirmReroll) return false;
        s.State = BcStepState.Pending;
        Pause = BcPause.None;
        PossiblyRelated.Clear();
        State = BcRunState.Running;
        return true;
    }

    /// <summary>No-reply pause → record the step as Unknown and go on.</summary>
    public bool Skip(double now)
    {
        if (State != BcRunState.Paused || Pause != BcPause.NoReply) return false;
        var s = CurrentStep;
        s.State = BcStepState.Unknown;
        s.Note = "No reply.";
        Pause = BcPause.None;
        PossiblyRelated.Clear();
        State = BcRunState.Running;
        return true;
    }

    /// <summary>PlayerNotFound / Denied pause → carry on: the step in flight gets a fresh reply window.</summary>
    public bool Continue(double now)
    {
        if (State != BcRunState.Paused || (Pause != BcPause.PlayerNotFound && Pause != BcPause.Denied)) return false;
        Pause = BcPause.None;
        PauseLine = null;
        var s = CurrentStep;
        if (s != null && s.State == BcStepState.Sent) { _sentAt = now; State = BcRunState.WaitingReply; }
        else State = BcRunState.Running;
        return true;
    }

    /// <summary>Stop now. Nothing more is sent; a command already on the wire can't be recalled.</summary>
    public void Stop(double now, string reason)
    {
        if (Ended) return;
        var s = CurrentStep;
        if (s != null && s.State == BcStepState.Sent)
        {
            s.State = BcStepState.Unknown;
            s.Note = "Stopped while waiting: it may or may not have run.";
        }
        End(BcRunState.Stopped, now, reason);
    }

    /// <summary>The runner couldn't put the current command on the wire: record it as failed (never sent, so Retry
    /// offers it) and stop.</summary>
    public void SendFailed(double now)
    {
        if (Ended) return;
        var s = CurrentStep;
        if (s != null && s.State == BcStepState.Sent)
        {
            s.State = BcStepState.Failed;
            s.Note = "Couldn't be sent (see the BepInEx log).";
        }
        End(BcRunState.Stopped, now, "Stopped: a command couldn't be sent.");
    }

    /// <summary>Disconnect / server switch: stop if running, then freeze the report. No late reply from the next
    /// server can touch it, and Retry is no longer offered.</summary>
    public void Detach(double now, string reason)
    {
        Stop(now, reason);
        Sealed = true;
        RetryAllowed = false;
    }

    private void End(BcRunState state, double now, string reason)
    {
        State = state;
        Pause = BcPause.None;
        _endedAt = now;
        EndReason = reason;
    }

    // ---------------------------------------------------------------- report

    public int Count(BcStepState s) => Steps.Count(x => x.State == s);

    /// <summary>"Reset Bob: 41 reset, 12 assumed nothing, 1 system off (Professions), …".</summary>
    public string Summary()
    {
        var sb = new StringBuilder();
        sb.Append(Ended ? (State == BcRunState.Stopped ? "Stopped" : "Finished") : "Resetting").Append(' ').Append(Player).Append(": ");
        var parts = new List<string> { $"{Count(BcStepState.Ok)} reset" };
        void Part(int n, string text) { if (n > 0) parts.Add($"{n} {text}"); }
        Part(Count(BcStepState.AssumedEmpty), "assumed nothing to reset");
        var off = Steps.Where(x => x.State == BcStepState.SystemOff).Select(x => x.System).Distinct().ToList();
        if (off.Count > 0)
            parts.Add($"{Count(BcStepState.SystemOff)} skipped, system off ({string.Join(", ", off.Select(BcResetPlan.Label))})");
        Part(Count(BcStepState.Failed), "failed");
        Part(Count(BcStepState.Unknown), "unknown");
        Part(Count(BcStepState.NotMet), "not met");
        Part(Count(BcStepState.Pending), "not sent");
        sb.Append(string.Join(", ", parts)).Append('.');
        if (!string.IsNullOrEmpty(EndReason)) sb.Append(' ').Append(EndReason);
        return sb.ToString();
    }

    /// <summary>Failed and Unknown steps, recomputed from the records at the moment it is called, as fresh Pending
    /// steps for a new run. An unknown quest re-roll is left out unless <paramref name="includeReroll"/>.</summary>
    public List<BcResetStep> RetryList(bool includeReroll)
    {
        var list = new List<BcResetStep>();
        if (!RetryAllowed) return list;
        foreach (var s in Steps)
        {
            if (s.State != BcStepState.Failed && s.State != BcStepState.Unknown) continue;
            if (!s.Idempotent && !includeReroll) continue;
            var c = s.Clone();
            c.Index = list.Count;
            list.Add(c);
        }
        return list;
    }

    public bool HasUnknownReroll => RetryAllowed && Steps.Any(s => !s.Idempotent && (s.State == BcStepState.Unknown || s.State == BcStepState.Failed));
}
