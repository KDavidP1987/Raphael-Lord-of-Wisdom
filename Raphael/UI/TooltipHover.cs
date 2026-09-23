using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Raphael.UI;

// Static hover-tooltip service. Each attached binding is a (RectTransform, string)
// pair. While the MainPanel is open, TickAll checks the mouse position against each
// rect; the first match writes its tooltip text into the shared Sink (a
// TextMeshProUGUI at the bottom of the MainPanel).
//
// Why not use IPointerEnterHandler/IPointerExitHandler on a subclassed
// MonoBehaviour: under Il2CppInterop those interfaces are exposed as wrapped
// classes, not interfaces, so a managed class can't implement them - the C#
// compiler reports CS1721 "multiple base classes". Polling avoids the issue.
//
// 0.18 PERF: this was written for "~30 tooltips" but the UI grew to THOUSANDS of
// bindings (4000+ in a full session). Ticking every frame iterated all of them with
// per-element IL2CPP interop (activeInHierarchy + GetComponentInParent<Canvas>) — a
// large always-on main-thread cost even with the panel CLOSED, felt worst on a
// self-hosted server where the client shares the CPU with the server process. Three
// fixes: (1) the Sink only renders inside the MainPanel, so skip the whole scan when
// the panel is closed; (2) cache each binding's canvas camera (it never changes)
// instead of walking the hierarchy every frame; (3) stop at the first hit.
public static class TooltipHover
{
    private struct Binding
    {
        public RectTransform Rt;
        public string Text;
        public Func<string> TextFunc; // dynamic text (evaluated on hover) — lets a tooltip reflect
                                      // live state (e.g. an ability description that streams in later)
                                      // WITHOUT rebuilding the list. Wins over Text when set.
        public Camera Cam;        // cached canvas camera (null for Screen Space Overlay)
        public bool CamResolved;  // whether Cam has been looked up yet
    }

    private static readonly List<Binding> _bindings = new();

    /// <summary>Shared display target. <see cref="ModContent.MainPanel"/> sets this on construction.</summary>
    public static TextMeshProUGUI Sink;

    public static string IdlePlaceholder = "Hint: hover any control for help.";

    private static bool _firstHitLogged;

    // PERF throttle: the MainPanel pre-builds EVERY tab's content (BuildContentArea)
    // and just toggles visibility, so the binding list is large (thousands). Running
    // the hit-test at full frame rate while the panel is open added cursor/input lag.
    // 30 Hz keeps tooltips responsive while cutting the cost ~5x at high frame rates.
    private const float TickIntervalSeconds = 1f / 30f;
    private static float _lastTickAt;

    public static int BindingCount => _bindings.Count;

    // 0.65.6 LEAK FIX: stale bindings were only pruned inside the hit-test loop, which runs only
    // while the MainPanel is open AND stops at the first hovered control. Overlays that rebuild
    // their rows on every state broadcast (Familiar Quick Spawn, Beelz action bar / transforms)
    // attach a fresh tooltip per row each time, so with the panel closed the list grew all session.
    // Now swept on a slow cadence regardless of panel state (and on "Refresh Raphael").
    private const float PruneIntervalSeconds = 10f;
    private static float _lastPruneAt;

    /// <summary>Drop bindings whose control has been destroyed. Returns how many were removed.</summary>
    public static int PruneDestroyed()
    {
        int before = _bindings.Count;
        _bindings.RemoveAll(b => b.Rt == null);
        return before - _bindings.Count;
    }

    /// <summary>Attach a tooltip to a UI GameObject. No-op if target lacks a RectTransform.</summary>
    public static void Attach(GameObject target, string text)
    {
        if (target == null || string.IsNullOrEmpty(text)) return;
        var rt = target.GetComponent<RectTransform>();
        if (rt == null) return;
        _bindings.Add(new Binding { Rt = rt, Text = text });
    }

    /// <summary>Attach a DYNAMIC tooltip — the provider is evaluated when the control is
    /// hovered, so the text reflects current state (e.g. an ability description fetched
    /// after the row was built) without rebuilding the UI.</summary>
    public static void Attach(GameObject target, Func<string> textFunc)
    {
        if (target == null || textFunc == null) return;
        var rt = target.GetComponent<RectTransform>();
        if (rt == null) return;
        _bindings.Add(new Binding { Rt = rt, TextFunc = textFunc });
    }

    /// <summary>Per-frame: walks bindings, updates Sink to the topmost hovered tooltip text.</summary>
    public static void TickAll()
    {
        float nowPrune = Time.realtimeSinceStartup;
        if (nowPrune - _lastPruneAt >= PruneIntervalSeconds)
        {
            _lastPruneAt = nowPrune;
            try { PruneDestroyed(); } catch { }
        }

        if (Sink == null) return;

        // PERF: the Sink lives in the MainPanel footer, so the hover hit-test is only
        // meaningful while that panel is open. Skipping the scan when it's closed
        // removes the per-frame iteration over thousands of bindings during normal
        // gameplay (the common case) entirely. Stale-binding pruning still happens
        // whenever the panel is open, so the list can't grow unbounded.
        if (!(Plugin.UIManager?.IsMainPanelOpen ?? false)) return;

        // Throttle to a fixed cadence: the hit-test scans thousands of bindings, so
        // running it every frame added cursor/input lag while the panel was open.
        float now = Time.realtimeSinceStartup;
        if (now - _lastTickAt < TickIntervalSeconds) return;
        _lastTickAt = now;

        Vector2 mouse = Input.mousePosition;
        string hit = null;

        // Reverse iteration: removal of stale (destroyed) bindings is O(1)-cheap, AND
        // the most-recently-registered (typically deepest/child) binding has the highest
        // index so it's visited FIRST — meaning the first hit is the one that should
        // win, so we can stop scanning there.
        for (int i = _bindings.Count - 1; i >= 0; i--)
        {
            var b = _bindings[i];
            if (b.Rt == null)
            {
                _bindings.RemoveAt(i);
                continue;
            }
            if (!b.Rt.gameObject.activeInHierarchy) continue;

            // Resolve (and cache) the canvas camera once per binding. GetComponentInParent
            // walks the parent chain across the IL2CPP boundary; doing it per element per
            // frame was the bulk of the cost. A control's canvas doesn't change, so cache
            // the camera (null for Screen Space Overlay, which V Rising's UI uses).
            if (!b.CamResolved)
            {
                var canvas = b.Rt.GetComponentInParent<Canvas>();
                b.Cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    ? canvas.worldCamera : null;
                b.CamResolved = true;
                _bindings[i] = b; // write the cached camera back into the struct
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(b.Rt, mouse, b.Cam))
            {
                // Dynamic provider wins (evaluated only for the hovered binding, so it's one
                // call per tick — cheap). Falls back to the static text.
                if (b.TextFunc != null) { try { hit = b.TextFunc(); } catch { hit = null; } }
                hit ??= b.Text;
                break; // first hit in reverse order == most-recently-registered winner
            }
        }

        // Only write when the text actually changes — assigning every tick forces a
        // TMP mesh + layout rebuild of the footer, which isn't free at this UI size.
        string newText = hit ?? IdlePlaceholder;
        if (Sink.text != newText) Sink.text = newText;

        // One-shot diagnostic so we can confirm a hover ever registered. If
        // this never fires, either the bindings are mis-positioned or the
        // mouse-coords / canvas-camera mismatch is still defeating the hit
        // test - the user can then paste this absence into the log report.
        if (hit != null && !_firstHitLogged)
        {
            _firstHitLogged = true;
            try { Utils.LogUtils.LogInfo($"TooltipHover: first hover registered ({_bindings.Count} bindings tracked)."); }
            catch { /* harmless */ }
        }
    }
}
