using System.Collections.Generic;
using System.Text;
using Raphael.Config;
using Raphael.Services;
using Raphael.UI.Framework.CustomLib.Panel;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Panels;
using Raphael.UI.ModContent.Data;
using TMPro;
using UnityEngine;
using UIBase = Raphael.UI.Framework.UniverseLib.UI.UIBase;

namespace Raphael.UI.ModContent;

// 0.24: a SECONDARY, VIEW-ONLY chat window. A second draggable overlay that mirrors a user-chosen
// SUBSET of chat channels (no input box, no tabs) so an admin can watch two chat streams at once —
// e.g. keep System/Clan here while the main window stays on Global. It reads the SAME message source
// as the main chat window (ChatRelayService.Buffer + LineCaptured) and reuses the main window's
// per-channel tag/color formatting, so the two look consistent. Which channels appear is configured
// from Game UI → chat settings (the SecondaryChatShow* flags); nothing here can send.
public class SecondaryChatOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "SecondaryChatOverlay";
    public override PanelType PanelType => PanelType.SecondaryChatOverlay;

    public override int MinWidth  => 280;
    public override int MinHeight => 140;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Default to the right of centre so it doesn't land on top of the main chat window (lower-left).
    public override Vector2 DefaultPosition  => new(
        Owner.Scaler.m_ReferenceResolution.x * 0.5f - 230f,
        -Owner.Scaler.m_ReferenceResolution.y * 0.5f + 240f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    // Shares the chat window's transparency + background theme so the two windows match.
    public override float Opacity => Settings.TransparencyToAlpha(Settings.ChatWindowOverlayTransparency);
    public override bool UsesCustomBackgroundColor => true;

    public override void RefreshBackgroundColor()
    {
        var rgb = Settings.ChatWindowBackgroundColor;
        UIFactory.ApplyBackgroundColorRgbToPanel(uiRoot, rgb);
        try
        {
            var content = uiRoot != null ? uiRoot.transform.Find("Content") : null;
            var img = content != null ? content.GetComponent<UnityEngine.UI.Image>() : null;
            if (img != null) { var c = img.color; img.color = new Color(rgb.r, rgb.g, rgb.b, c.a); }
        }
        catch { }
    }

    private TextMeshProUGUI _log;
    private UnityEngine.UI.ScrollRect _scrollRect;
    private RectTransform _scrollContentRect;
    private bool _subscribed;
    // 0.65.6 PERF: coalesced repaint, same as ChatWindowOverlayPanel (see TickRender there).
    private System.Action _renderTicker;
    private bool _renderDirty;
    private float _nextRenderAt;
    private bool _renderRetried;

    public SecondaryChatOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Chat — view only");

        // Only the scrollable log should absorb resize slack (matches the main chat window).
        var contentVlg = ContentRoot.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (contentVlg != null) { contentVlg.childForceExpandHeight = false; contentVlg.childControlHeight = true; }

        var scroll = UIFactory.CreateScrollView(ContentRoot, "SecondaryChatLogScroll",
            out var scrollContent, out _, color: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(scroll,
            minWidth: 260, preferredWidth: 320, flexibleWidth: 1,
            minHeight: 100, preferredHeight: 180, flexibleHeight: 1);
        var scrollBg = scroll.GetComponent<UnityEngine.UI.Image>();
        if (scrollBg != null) scrollBg.color = new Color(0f, 0f, 0f, 0f);
        _scrollRect = scroll.GetComponent<UnityEngine.UI.ScrollRect>();
        _scrollContentRect = scrollContent.GetComponent<RectTransform>();

        var lbl = UIFactory.CreateLabel(scrollContent, "SecondaryChatLog", string.Empty, TextAlignmentOptions.TopLeft);
        _log = lbl.TextMesh;
        _log.richText = true;
        _log.enableWordWrapping = true;
        _log.fontSize = Mathf.Max(8f, Mathf.RoundToInt(12 * Settings.ChatTextScale));
        _log.lineSpacing = 4f;
        _log.margin = new Vector4(6, 4, 6, 4);
        _log.color = Theme.DefaultText;
        UIFactory.SetLayoutElement(_log.gameObject, flexibleWidth: 1, flexibleHeight: 1);

        if (!_subscribed) { ChatRelayService.LineCaptured += OnLineCaptured; _subscribed = true; }
        if (_renderTicker == null)
        {
            _renderTicker = TickRender;
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_renderTicker);
        }
        Render();
    }

    private void OnLineCaptured(ChatRelayService.ChatLine line) => _renderDirty = true;

    private void TickRender()
    {
        if (!_renderDirty || _log == null || !Enabled) return;
        float now = Time.unscaledTime;
        if (now < _nextRenderAt) return;
        _nextRenderAt = now + ChatWindowOverlayPanel.RenderIntervalSeconds;
        try { Render(); _renderRetried = false; }
        catch (System.Exception ex)
        {
            // Same policy as ChatWindowOverlayPanel.TickRender: one retry, then wait for the next line.
            if (!_renderRetried) { _renderRetried = true; _renderDirty = true; }
            else { _renderRetried = false; Raphael.Utils.LogUtils.LogWarning($"[Chat2] render failed twice; waiting for the next message: {ex.Message}"); }
        }
    }

    /// <summary>Re-render (e.g. the Game UI settings changed which channels show, or the text scale).</summary>
    public void Refresh()
    {
        if (_log == null) return;
        _log.fontSize = Mathf.Max(8f, Mathf.RoundToInt(12 * Settings.ChatTextScale));
        try { Render(); } catch { }
    }

    // Which channels this window shows, from the user's Game UI selection.
    private static bool ChannelEnabled(ChatRelayService.Channel ch) => ch switch
    {
        ChatRelayService.Channel.Global  => Settings.SecondaryChatShowGlobal,
        ChatRelayService.Channel.Local   => Settings.SecondaryChatShowLocal,
        ChatRelayService.Channel.Clan    => Settings.SecondaryChatShowClan,
        ChatRelayService.Channel.System  => Settings.SecondaryChatShowSystem,
        ChatRelayService.Channel.Whisper => Settings.SecondaryChatShowWhisper,
        _                                => false,   // "Other" never shown
    };

    // True if this line should appear: its channel is enabled, OR it's a note-to-self and the dedicated
    // "Notes to self" toggle is on (so you can mirror just your self-notes without enabling all whispers).
    private static bool LineEnabled(ChatRelayService.ChatLine ln)
        => ChannelEnabled(ln.Channel)
           || (Settings.SecondaryChatShowNotesToSelf && ChatRelayService.IsNoteToSelf(ln));

    private void Render()
    {
        if (_log == null) return;
        _renderDirty = false;
        bool showTime = Settings.ChatShowTimestamps;
        bool showTag  = Settings.ChatShowChannelTags;
        bool newestAtBottom = Settings.ChatNewestAtBottom;

        var lines = new List<string>();
        var line = new StringBuilder(160);
        // 0.65.6 PERF: only the newest lines that can be drawn (see ChatWindowOverlayPanel.NewestVisible).
        var buf = ChatWindowOverlayPanel.NewestVisible(ChatRelayService.Buffer, LineEnabled);
        for (int i = 0; i < buf.Count; i++)
        {
            var ln = buf[i];
            line.Clear();
            if (showTime) line.Append("<color=#808080>").Append(ln.Received.ToString("HH:mm")).Append("</color> ");
            if (showTag)  line.Append(ChatWindowOverlayPanel.ChannelTag(ln.Channel)).Append(' ');
            // View-only: plain sender name (no click-to-whisper link — this window can't send).
            // 0.65.3: close any tag the name leaves open so it can't bleed formatting downward.
            if (!string.IsNullOrEmpty(ln.Sender))
                line.Append(ChatWindowOverlayPanel.CloseDanglingTags(ln.Sender)).Append(": ");
            line.Append(ChatWindowOverlayPanel.BodyText(ln));
            lines.Add(line.ToString());
        }

        // 0.65.0 BLANK-CHAT FIX: bound the newest lines handed to the single TMP _log so the
        // mesh can't exceed TextMeshPro's 65,535-vertex ceiling and render blank. Same defect
        // and fix as ChatWindowOverlayPanel.Render — the retention buffer is untouched.
        // 0.65.1: bound by a visible-glyph budget (shared helper) so long lines can't overflow.
        int start = ChatWindowOverlayPanel.ComputeRenderStart(lines);
        var sb = new StringBuilder(2048);
        if (newestAtBottom)
            for (int i = start; i < lines.Count; i++) sb.Append(lines[i]).Append('\n');
        else
            for (int i = lines.Count - 1; i >= start; i--) sb.Append(lines[i]).Append('\n');
        _log.text = sb.ToString();

        if (Settings.ChatAutoScroll) ScrollToNewest(newestAtBottom);
    }

    private void ScrollToNewest(bool newestAtBottom)
    {
        if (_scrollRect == null) return;
        try
        {
            Canvas.ForceUpdateCanvases();
            if (_scrollContentRect != null)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContentRect);
            _scrollRect.verticalNormalizedPosition = newestAtBottom ? 0f : 1f;
        }
        catch { /* scroll is best-effort */ }
    }

    internal override void Reset()
    {
        if (_subscribed) { ChatRelayService.LineCaptured -= OnLineCaptured; _subscribed = false; }
        if (_renderTicker != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_renderTicker);
            _renderTicker = null;
        }
        _renderDirty = false;
        _log = null;
        _scrollRect = null;
        _scrollContentRect = null;
    }
}
