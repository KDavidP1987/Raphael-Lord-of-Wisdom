using System;
using System.Collections.Generic;

namespace Raphael.UI.ModContent;

// 0.72.0: the left rail's expand / collapse rules as pure functions, so they are unit-tested outside the game
// (tests/Raphael.Logic.Tests). MainPanel only feeds in what it sees and applies the answer. The bug class this closes:
// groups opening by themselves after a panel rebuild (text-size change), on every "not detected" mod group, or next to
// the group the user is working in when a late handshake lands.

/// <summary>What the user last asked of a group: nothing yet, open, or closed.</summary>
internal enum RailIntent { None, Open, Closed }

/// <summary>One group as the policy sees it, in rail order.</summary>
internal sealed class RailGroup
{
    public string Title;
    public bool IsMod;          // Bloodcraft, Beelzebub, Uriel, Faust, Nyarlathotep: the only groups ever auto-changed
    public bool Available;      // the mod answered (or the group isn't a mod group)
    public bool Diagnostic;     // a "not detected / handshake failed" panel is shown; the header still opens
    public bool Expanded;
    public RailIntent Intent;
    public bool Usable => Available || Diagnostic;
}

/// <summary>Rail memory that outlives a panel rebuild: per group title, whether it is open and what the user last
/// asked for, plus whether the user has clicked the rail at all this session. Cleared only by
/// <see cref="Clear"/> (client teardown, the same path that resets every mod's detection).</summary>
internal static class RailMemory
{
    public static readonly Dictionary<string, bool> Expanded = new(StringComparer.Ordinal);
    public static readonly Dictionary<string, RailIntent> Intent = new(StringComparer.Ordinal);
    public static bool UserTookControl;

    public static void Clear()
    {
        Expanded.Clear();
        Intent.Clear();
        UserTookControl = false;
    }
}

/// <summary>The changes to apply: groups to open or close, and new intents.</summary>
internal sealed class RailChanges
{
    public readonly Dictionary<string, bool> Expand = new(StringComparer.Ordinal);
    public readonly Dictionary<string, RailIntent> Intent = new(StringComparer.Ordinal);
    public bool UserTookControl;
}

internal static class RailPolicy
{
    public static readonly string[] ModGroups = { "Bloodcraft", "Beelzebub", "Uriel", "Faust", "Nyarlathotep" };

    public static bool IsModGroup(string title) => Array.IndexOf(ModGroups, title) >= 0;

    /// <summary>A header click on <paramref name="title"/>: it toggles; opening it with the accordion on closes every
    /// other open group (each becomes Intent=Closed, as if the user had closed it). Always marks the user in control.</summary>
    public static RailChanges HeaderClick(IReadOnlyList<RailGroup> groups, string title, bool accordion)
    {
        var ch = new RailChanges { UserTookControl = true };
        var g = Find(groups, title);
        if (g == null || !g.Usable) return ch;
        bool open = !g.Expanded;
        ch.Expand[title] = open;
        ch.Intent[title] = open ? RailIntent.Open : RailIntent.Closed;
        if (open && accordion)
            foreach (var o in groups)
                if (!ReferenceEquals(o, g) && o.Expanded)
                {
                    ch.Expand[o.Title] = false;
                    ch.Intent[o.Title] = RailIntent.Closed;
                }
        return ch;
    }

    /// <summary>A rail tab click inside <paramref name="title"/>: that group is where the user is (Intent=Open).</summary>
    public static RailChanges TabClick(string title)
    {
        var ch = new RailChanges { UserTookControl = true };
        if (!string.IsNullOrEmpty(title)) ch.Intent[title] = RailIntent.Open;
        return ch;
    }

    /// <summary>Runs after a rebuild and after every detection / availability change.
    /// 1. A dead header (not usable) is closed; its intent is kept.
    /// 2. A usable group the user opened (Intent=Open) that is closed is opened again — unless the accordion is on and
    ///    another group the user opened is already open; then it stays closed and becomes Intent=Closed.
    /// 3. Groups with Intent Open or Closed are otherwise never changed, and only mod groups are auto-changed.
    /// 4. Before the user's first rail click, and only while no group the user opened is open, the primary mod group
    ///    (the active tab's, else the first available, else the first diagnostic) is opened.
    /// 5. Every other mod group open with Intent=None (an earlier auto-open) is closed.</summary>
    public static RailChanges Reconcile(IReadOnlyList<RailGroup> groups, string activeGroup, bool accordion, bool userTookControl)
    {
        var ch = new RailChanges { UserTookControl = userTookControl };
        var open = new Dictionary<string, bool>(StringComparer.Ordinal);
        var intent = new Dictionary<string, RailIntent>(StringComparer.Ordinal);
        foreach (var g in groups) { open[g.Title] = g.Expanded; intent[g.Title] = g.Intent; }

        void Set(string t, bool v) { if (open[t] != v) { open[t] = v; ch.Expand[t] = v; } }
        void SetIntent(string t, RailIntent v) { if (intent[t] != v) { intent[t] = v; ch.Intent[t] = v; } }
        bool UserOpenElsewhere(string t)
        {
            foreach (var o in groups)
                if (o.Title != t && open[o.Title] && intent[o.Title] == RailIntent.Open) return true;
            return false;
        }

        // 1. dead headers close (display only)
        foreach (var g in groups)
            if (!g.Usable && open[g.Title]) Set(g.Title, false);

        // 2. restore what the user opened
        foreach (var g in groups)
        {
            if (!g.Usable || intent[g.Title] != RailIntent.Open || open[g.Title]) continue;
            if (accordion && UserOpenElsewhere(g.Title)) SetIntent(g.Title, RailIntent.Closed);
            else Set(g.Title, true);
        }

        // 4. primary
        RailGroup primary = null;
        var active = Find(groups, activeGroup);
        if (active != null && active.IsMod && active.Available) primary = active;
        if (primary == null) foreach (var g in groups) if (g.IsMod && g.Available) { primary = g; break; }
        if (primary == null) foreach (var g in groups) if (g.IsMod && g.Diagnostic) { primary = g; break; }

        bool anyUserOpen = false;
        foreach (var g in groups) if (open[g.Title] && intent[g.Title] == RailIntent.Open) { anyUserOpen = true; break; }
        if (primary != null && !userTookControl && !anyUserOpen && primary.Usable && intent[primary.Title] == RailIntent.None)
            Set(primary.Title, true);

        // 5. undo earlier auto-opens elsewhere
        foreach (var g in groups)
            if (g.IsMod && g != primary && open[g.Title] && intent[g.Title] == RailIntent.None) Set(g.Title, false);

        return ch;
    }

    private static RailGroup Find(IReadOnlyList<RailGroup> groups, string title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        foreach (var g in groups) if (g.Title == title) return g;
        return null;
    }
}
