using System;
using System.Collections.Generic;
using Raphael.Config;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Raphael.UI.ModContent;

// 0.69.0: short-lived on-screen notifications for the Nyarlathotep push stream (handoff api 8 §2/§4): "Ashfall
// Raid has begun", "Wave 2 in 60s", the kill-switch banner, the login summary. Not a ResizeablePanelBase —
// a click-through stack anchored top-centre on Raphael's canvas, built lazily on the first toast. Each toast
// fades out after Settings.NyarToastSeconds; at most MAX_VISIBLE show at once (the oldest is dropped).
// Tick() runs on CoreUpdateBehavior.Actions.
internal static class NyarToastHost
{
    private const int MAX_VISIBLE = 4;
    private const float FADE_SECONDS = 0.6f;

    private sealed class Toast
    {
        public GameObject Go;
        public CanvasGroup Cg;
        public float Until;
    }

    private static GameObject _stack;
    private static readonly List<Toast> _toasts = new();

    /// <summary>Show one toast. <paramref name="accentHex"/> tints the left bar (e.g. "#E6B85C").</summary>
    public static void Show(string text, string accentHex = "#B48CFF")
    {
        if (string.IsNullOrEmpty(text) || !Settings.NyarToasts) return;
        try
        {
            if (!EnsureStack()) return;
            while (_toasts.Count >= MAX_VISIBLE) Remove(_toasts[0]);

            var go = UIFactory.CreateHorizontalGroup(_stack, "NyarToast", forceExpandWidth: false, forceExpandHeight: true,
                childControlWidth: true, childControlHeight: true, spacing: 8, padding: new Vector4(8, 8, 6, 12),
                bgColor: new Color(0.08f, 0.06f, 0.12f, 0.92f));
            UIFactory.SetLayoutElement(go, minWidth: 260, preferredWidth: 420, flexibleWidth: 0, minHeight: 34, flexibleHeight: 0);

            var bar = UIFactory.CreateUIObject("NyarToastBar", go);
            var img = bar.AddComponent<Image>();
            img.color = ColorUtility.TryParseHtmlString(accentHex, out var c) ? c : new Color(0.7f, 0.55f, 1f, 1f);
            img.raycastTarget = false;
            UIFactory.SetLayoutElement(bar, minWidth: 4, preferredWidth: 4, flexibleWidth: 0, minHeight: 22, flexibleHeight: 1);

            var lbl = UIFactory.CreateLabel(go, "NyarToastText", text, TextAlignmentOptions.MidlineLeft, color: null,
                fontSize: Theme.ScaledOverlay(14));
            lbl.TextMesh.enableWordWrapping = true;
            lbl.TextMesh.raycastTarget = false;
            UIFactory.SetLayoutElement(lbl.GameObject, minWidth: 220, preferredWidth: 390, flexibleWidth: 0, minHeight: 22, flexibleHeight: 0);

            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false; cg.interactable = false;
            _toasts.Add(new Toast { Go = go, Cg = cg, Until = Time.realtimeSinceStartup + Settings.NyarToastSeconds });
        }
        catch (Exception ex) { LogUtils.LogError($"[Nyar] toast failed: {ex.Message}"); }
    }

    public static void Tick()
    {
        if (_toasts.Count == 0) return;
        float now = Time.realtimeSinceStartup;
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            var t = _toasts[i];
            if (t.Go == null) { _toasts.RemoveAt(i); continue; }
            float left = t.Until - now;
            if (left <= 0f) { Remove(t); continue; }
            if (t.Cg != null) t.Cg.alpha = left < FADE_SECONDS ? left / FADE_SECONDS : 1f;
        }
    }

    /// <summary>Follows the UI's hide/show (escape menu, logout teardown).</summary>
    public static void SetVisible(bool visible)
    {
        if (_stack != null) { try { _stack.SetActive(visible); } catch { } }
    }

    /// <summary>Drop every toast and the stack (logout / UI teardown).</summary>
    public static void Clear()
    {
        foreach (var t in _toasts.ToArray()) Remove(t);
        if (_stack != null) { try { UnityEngine.Object.Destroy(_stack); } catch { } }
        _stack = null;
    }

    private static void Remove(Toast t)
    {
        _toasts.Remove(t);
        if (t.Go != null) { try { UnityEngine.Object.Destroy(t.Go); } catch { } }
    }

    private static bool EnsureStack()
    {
        if (_stack != null) return true;
        var root = Plugin.UIManager?.UiRootObject;
        if (root == null) return false;
        _stack = UIFactory.CreateVerticalGroup(root, "NyarToastStack", forceWidth: false, forceHeight: false,
            childControlWidth: true, childControlHeight: true, spacing: 6, padding: new Vector4(0, 0, 0, 0),
            bgColor: new Color(0, 0, 0, 0), childAlignment: TextAnchor.UpperCenter);
        var rt = _stack.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -90f);
        var fitter = _stack.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var bg = _stack.GetComponent<Image>();
        if (bg != null) bg.raycastTarget = false;
        _stack.transform.SetAsLastSibling();
        return true;
    }
}
