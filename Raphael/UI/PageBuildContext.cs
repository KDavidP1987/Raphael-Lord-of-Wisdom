using System;
using System.Collections.Generic;

namespace Raphael.UI;

// 0.67.1: per-page teardown scope for MainPanel's lazy tab pages.
//
// MainPanel now builds each tab page on demand and can rebuild a single page when its mod's detection state
// changes. Anything a page builder subscribes to a STATIC event (picker repopulate closures, box-name dropdown
// fields) must be unhooked when THAT page is retired — not just on a full panel Reset — or each rebuild adds
// another handler that keeps the destroyed page rooted.
//
// While MainPanel builds a page it opens a scope; builders call Register(teardown). The scope's list becomes
// the page's teardown list on success, or is drained immediately if the build throws. Outside a scope,
// Register returns false and the caller falls back to its own panel-lifetime list.
internal static class PageBuildContext
{
    private static List<Action> _current;

    public static bool IsBuilding => _current != null;

    /// <summary>Open a scope. Returns the previous scope so the caller can restore it in a finally.</summary>
    public static List<Action> Begin(List<Action> sink)
    {
        var prev = _current;
        _current = sink;
        return prev;
    }

    public static void End(List<Action> previous) => _current = previous;

    /// <summary>Attach a teardown to the page being built. False when no page build is in progress.</summary>
    public static bool Register(Action teardown)
    {
        if (_current == null || teardown == null) return false;
        _current.Add(teardown);
        return true;
    }

    /// <summary>Run and clear a teardown list; one failing entry never stops the rest.</summary>
    public static void Drain(List<Action> list)
    {
        if (list == null || list.Count == 0) return;
        var snapshot = list.ToArray();
        list.Clear();
        foreach (var t in snapshot)
        {
            try { t(); }
            catch (Exception ex) { Utils.LogUtils.LogDebug($"page teardown threw: {ex.Message}"); }
        }
    }
}
