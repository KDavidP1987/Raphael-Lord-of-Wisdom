using System;
using System.Collections.Generic;
using System.Text;
using Raphael.Config;
using Raphael.Services;
using Raphael.Utils;
using Raphael.UI.Framework.CustomLib.Panel;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.UI.Framework.UniverseLib.UI.Panels;
using Raphael.UI.ModContent.Data;
using ProjectM.Network;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UIBase = Raphael.UI.Framework.UniverseLib.UI.UIBase;

namespace Raphael.UI.ModContent;

// 0.17 (feature/v0.17-standalone-ui): standalone tabbed chat window.
//
// INCREMENT 1 = read-only display. Mirrors every inbound chat message into a
// persistent, channel-tabbed, scrollable view; native chat left untouched.
// Drag/resize follow the standard overlay rules — subject to the "Lock overlays"
// toggle like every other overlay (turn it off to move/resize). Formatting +
// per-window customization (timestamps, channel labels, transparency) are wired
// through Settings so the primary UI can drive them.
//
// Later increments add input/send, sender names, per-person whisper tabs, and
// the option to REPLACE the native chat (hide it) for a single powerful window.
public class ChatWindowOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "ChatWindowOverlay";
    public override PanelType PanelType => PanelType.ChatWindowOverlay;

    public override int MinWidth  => 320;
    public override int MinHeight => 180;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Lower-left by default — where the native chat lives.
    public override Vector2 DefaultPosition  => new(
        -Owner.Scaler.m_ReferenceResolution.x * 0.5f + 30f,
        -Owner.Scaler.m_ReferenceResolution.y * 0.5f + 220f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.ChatWindowOverlayTransparency);
    public override bool UsesCustomBackgroundColor => true;

    // 0.17.0: the chat window themes off its OWN color (Settings.ChatWindowBackgroundColor),
    // independent of the main panel's color. Overriding means BOTH construct-time and the
    // shared RefreshAllPanelBackgrounds walk paint the chat window with its own color, so
    // changing the main panel color never bleeds into the chat window.
    public override void RefreshBackgroundColor()
    {
        var rgb = Settings.ChatWindowBackgroundColor;
        UIFactory.ApplyBackgroundColorRgbToPanel(uiRoot, rgb);
        // ALSO recolor the main "Content" background image directly. The shared
        // walker skips "card-ish" neutral greys (its heuristic), and the chat
        // window's only opaque surface IS this base image (its rows are transparent)
        // — so without this a neutral color pick wouldn't visibly apply. Alpha is
        // left to the transparency control (ApplyOpacityToPanel owns it). NOTE: the
        // transparency floor is 1.0, so the 100% slider drives alpha to 0 — the
        // background goes FULLY transparent (text/buttons stay opaque), and at that
        // point the chosen background color is invisible by design.
        try
        {
            var content = uiRoot != null ? uiRoot.transform.Find("Content") : null;
            var img = content != null ? content.GetComponent<UnityEngine.UI.Image>() : null;
            if (img != null) { var c = img.color; img.color = new Color(rgb.r, rgb.g, rgb.b, c.a); }
        }
        catch { }
    }

    private static readonly (ChatRelayService.Channel? Filter, string Label)[] TabDefs =
    {
        (null,                             "All"),
        (ChatRelayService.Channel.Global,  "Global"),
        (ChatRelayService.Channel.Local,   "Local"),
        (ChatRelayService.Channel.Clan,    "Clan"),
        (ChatRelayService.Channel.System,  "System"),
        (ChatRelayService.Channel.Whisper, "Whispers"),
    };

    private int _activeTab;
    private TextMeshProUGUI _log;
    private bool _subscribed;
    private readonly List<ButtonRef> _tabButtons = new();
    private InputFieldRef _input;
    private GameObject _inputRow;       // 0.17.0: kept so the row can grow with wrapped input
    // 0.17.0: the scroll view + its content rect, kept so we can auto-scroll the
    // log to the newest message (bottom or top, per ChatNewestAtBottom).
    private UnityEngine.UI.ScrollRect _scrollRect;
    private RectTransform _scrollContentRect;
    // 0.17.0: input grows vertically (word-wrap) from one line up to this many px.
    private const float InputBaseHeight = 24f;
    private const float InputMaxHeight  = 96f;

    // 0.17.0 whisper sub-tabs. When the Whispers top-tab is active, a second row
    // appears: "All" (every whisper) + one sub-tab per conversation partner.
    private GameObject _whisperSubRow;
    private readonly List<ButtonRef> _whisperSubButtons = new();
    private readonly List<string> _whisperSubPartners = new(); // parallel to _whisperSubButtons; null = All
    private string _activeWhisperPartner; // null = All Whispers — the VIEW filter (top sub-tabs)
    // B4 (0.29.9): the message-bar recipient box's selection — who a typed message goes to on the Whispers
    // tab. Kept SEPARATE from _activeWhisperPartner (the view) so you can stay on the "All Whispers" view and
    // still pick/switch who you're replying to. Clicking a person's sub-tab sets BOTH (view + target); the box
    // and Tab-cycling set only this. null = no target picked / no conversations.
    private string _whisperSendTo;
    // Players chosen from the picker but not yet messaged — kept so their sub-tab
    // shows immediately (before any whisper line exists for them).
    private readonly HashSet<string> _initiatedPartners = new();
    // Partners the user explicitly CLOSED (× on the sub-tab). Suppresses their tab
    // even if old whisper lines remain in the buffer; cleared (re-opened) when a
    // new whisper from them arrives.
    private readonly HashSet<string> _closedPartners = new();
    private TMPro.TMP_Dropdown _whisperPicker;
    private List<PlayerRosterService.PlayerRef> _pickerRoster;
    private static readonly int WhispersTabIndex = System.Array.FindIndex(TabDefs, t => t.Filter == ChatRelayService.Channel.Whisper);
    // 0.17.0: per-tab unread counts. A message for a channel you're NOT currently
    // viewing bumps that tab's count; selecting the tab resets it. The All tab
    // (index 0) shows everything, so it never carries a badge.
    private readonly int[] _unread = new int[TabDefs.Length];
    // Max chars per chat message. The hard wire cap is FixedString512Bytes, but the
    // real limit is lower: tester reports (Howl, 0.65.x) showed that messages of 480+
    // chars make the SERVER re-broadcast the line repeatedly, spam-flooding chat and
    // freezing the chat box for NON-Raphael clients (teleporting to reload fixed it).
    // 479 and below were clean; 480+ repeated. We cap at 470 for margin so a Raphael
    // user can't emit a message that breaks vanilla clients around them.
    private const int MaxChatChars = 470;
    // 0.65.0: hard cap on how many newest lines we hand to the single TMP _log component
    // per render. A TextMeshPro mesh tops out at 65,535 vertices (~16k glyphs); above that
    // it silently renders blank. Bounds the rendered window WITHOUT shrinking the retention
    // buffer (scrollback/Copy keep the full 2000 lines). See the note in Render().
    // internal so SecondaryChatOverlayPanel.Render shares the identical cap.
    //
    // 0.65.1: a flat LINE cap wasn't enough — 300 long proximity lines on a busy server
    // still crossed the glyph ceiling and blanked (Howl: "Local went blank again"). The mesh
    // limit is about VISIBLE GLYPHS, not lines, so we now bound the newest lines by a glyph
    // budget (color-tag markup adds no vertices and is not counted). MaxRenderedLines stays as
    // a cheap secondary guard against pathological cases. See ComputeRenderStart.
    internal const int MaxRenderedLines  = 600;
    // Conservative visible-glyph budget per TMP draw. TMP blanks at ~16,383 glyphs; 12,000
    // leaves headroom for quad/sprite overhead and rounding in the tag-stripping estimate.
    internal const int MaxRenderedGlyphs = 12000;

    // 0.17.0: All-tab compose target — mirrors the native chat's Enter+Tab flow.
    // On the All tab a "Send to:" dropdown picks the outgoing channel/whisper, and
    // Tab cycles through the available targets (Global, Local, Clan if in a clan,
    // then each active whisper partner). Other tabs send on their own channel.
    private readonly struct SendTarget
    {
        public readonly ChatMessageType? Channel; // null => whisper to Whisper
        public readonly string Whisper;            // partner name when Channel is null
        public readonly string Label;              // dropdown text
        private SendTarget(ChatMessageType? channel, string whisper, string label)
        { Channel = channel; Whisper = whisper; Label = label; }
        public static SendTarget Chan(ChatMessageType c, string label) => new(c, null, label);
        public static SendTarget Whis(string partner) => new(null, partner, $"@{partner}");
        public bool IsWhisper => Channel == null;
    }
    private GameObject _composeDropdownObj; // compact target dropdown INSIDE the input row (All tab)
    private TMP_Dropdown _composeDropdown;
    private readonly List<SendTarget> _composeTargets = new();
    private int _composeIndex;
    private string _composeSignature = ""; // rebuild guard (clan state + whisper partners)
    private Action _composeKeyTicker;
    private Action _tabHotkeyTicker; // 0.17.3: <Modifier>+1..6 tab switch (chat open, not typing)
    private Action _nameClickTicker; // 0.17.3: double-click a name in the log to whisper
    private Action _scrollKeyTicker; // 0.50 r8: Up/Down/PageUp/PageDown scroll the chat history
    // 0.65.3: idle auto-hide (opt-in). A CanvasGroup on the window root is faded 1→0 after
    // Settings.ChatAutoHideSeconds of no activity and back on activity — see TickAutoHide. It's a
    // visual fade only; the persisted ShowChatWindowOverlay visibility is never touched.
    private Action _autoHideTicker;
    // 0.65.6 PERF: incoming lines mark the log dirty; TickRender repaints at most every
    // RenderIntervalSeconds, and only while the window is open. Pre-0.65.6 every line did a full
    // TMP rebuild + Canvas.ForceUpdateCanvases immediately — a burst of N lines = N rebuilds, even
    // with the window closed.
    private Action _renderTicker;
    private bool _renderDirty;
    private float _nextRenderAt;
    private bool _renderRetried; // one retry after a failed render, then give up until the next line
    internal const float RenderIntervalSeconds = 0.15f;
    private UnityEngine.CanvasGroup _rootCg;
    private float _lastActivityUnscaled;
    private bool _wasEnabledForAutoHide;
    private float _lastNameClickTime;
    private string _lastNameClickId;
    private float _nextComposeRefresh; // 0.17.3: throttle clan-availability re-check (All tab)
    // Frames of "still counts as typing" grace after the input reports unfocused.
    // The field's focus flag blips for a frame around a Tab press, which made every
    // OTHER Tab a no-op (friend-test: Tab needed pressing twice to switch). The
    // grace bridges that blip so each Tab press cycles exactly once.
    private int _inputFocusGrace;

    public ChatWindowOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Chat");

        // 0.17.0: only the message log should grow/shrink when the window is
        // resized — not the tab row or the input row. The ContentRoot's vertical
        // group force-expands children, which distributed extra height to ALL rows
        // and left blank space in the header/footer (friend-test report). Turn it
        // off so extra space goes only to the flexibleHeight log below.
        var contentVlg = ContentRoot.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (contentVlg != null) { contentVlg.childForceExpandHeight = false; contentVlg.childControlHeight = true; }

        // Tab button row.
        var tabRow = UIFactory.CreateHorizontalGroup(ContentRoot, "ChatTabs",
            true, false, true, true, 2, new Vector4(2, 2, 2, 2), bgColor: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(tabRow, minHeight: 24, preferredHeight: 24, flexibleWidth: 1);

        _tabButtons.Clear();
        for (int i = 0; i < TabDefs.Length; i++)
        {
            int idx = i;
            var btn = UIFactory.CreateButton(tabRow, $"ChatTab_{TabDefs[i].Label}", TabDefs[i].Label);
            UIFactory.SetLayoutElement(btn.GameObject,
                minWidth: 36, preferredWidth: 60, flexibleWidth: 1,
                minHeight: 22, preferredHeight: 22, flexibleHeight: 0);
            // 0.17.3: keep the label (incl. the unread badge "Clan (3)") on ONE line.
            // At larger chat text it used to word-wrap to two lines and overlap the
            // rows below; ellipsis truncates horizontally instead of spilling.
            if (btn.ButtonText != null)
            {
                btn.ButtonText.enableWordWrapping = false;
                btn.ButtonText.overflowMode = TextOverflowModes.Ellipsis;
            }
            btn.OnClick = () => SwitchToTab(idx);
            _tabButtons.Add(btn);
        }

        // Copy the messages currently shown (this tab's filter) to the OS clipboard — for bug reports
        // / sharing a conversation. Plain text (rich-text tags stripped).
        var copyBtn = UIFactory.CreateButton(tabRow, "ChatCopyBtn", "Copy");
        UIFactory.SetLayoutElement(copyBtn.GameObject,
            minWidth: 42, preferredWidth: 46, flexibleWidth: 0, minHeight: 22, preferredHeight: 22, flexibleHeight: 0);
        if (copyBtn.ButtonText != null) copyBtn.ButtonText.enableWordWrapping = false;
        copyBtn.OnClick = CopyConversationToClipboard;

        // 0.65.1: manual re-render. Requested by Howl — if a channel ever renders stale/blank
        // (e.g. after a burst of traffic, or a TMP hiccup), this forces a fresh draw from the
        // retained history without waiting for the next incoming line or a tab swap. The
        // glyph-budget cap (0.65.1) should keep the box from blanking in the first place, but
        // this stays as a zero-cost escape hatch.
        var refreshBtn = UIFactory.CreateButton(tabRow, "ChatRefreshBtn", "Refresh");
        UIFactory.SetLayoutElement(refreshBtn.GameObject,
            minWidth: 54, preferredWidth: 58, flexibleWidth: 0, minHeight: 22, preferredHeight: 22, flexibleHeight: 0);
        if (refreshBtn.ButtonText != null) refreshBtn.ButtonText.enableWordWrapping = false;
        refreshBtn.OnClick = () => { try { Render(); } catch { } };

        // 0.17.0: whisper sub-tab row — hidden unless the Whispers tab is active.
        _whisperSubRow = UIFactory.CreateHorizontalGroup(ContentRoot, "ChatWhisperSubTabs",
            false, false, true, true, 2, new Vector4(2, 0, 2, 0), bgColor: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(_whisperSubRow, minHeight: 22, preferredHeight: 22, flexibleWidth: 1);
        _whisperSubRow.SetActive(false);

        // (0.29.10: the old top "whisper a player by name" input row was removed — it confused testers as a
        // second message box. Starting a new whisper now lives at the BOTTOM input line via the "+ Whisper…"
        // picker built by UpdateWhisperPicker, beside the recipient box. The top is just the sub-tabs now.)
        // (0.17.0: the All-tab compose-target control is a compact dropdown placed
        // INSIDE the input row — built by RebuildComposeRow below, beside the input.)

        // Scrollable message log: one multi-line label rebuilt on update.
        var scroll = UIFactory.CreateScrollView(ContentRoot, "ChatLogScroll",
            out var scrollContent, out _, color: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(scroll,
            minWidth: 300, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 120, preferredHeight: 220, flexibleHeight: 1);
        // CreateScrollView paints its background Theme.Level1 (red) when the passed
        // color == default; force it transparent so the panel's themed background
        // shows through, matching the other overlays.
        var scrollBg = scroll.GetComponent<UnityEngine.UI.Image>();
        if (scrollBg != null) scrollBg.color = new Color(0f, 0f, 0f, 0f);
        // Cache the ScrollRect + content rect for auto-scroll-to-newest.
        _scrollRect = scroll.GetComponent<UnityEngine.UI.ScrollRect>();
        _scrollContentRect = scrollContent.GetComponent<RectTransform>();

        // 0.50 r11: clickable scroll arrows pinned to the TOP and BOTTOM of the scrollbar column (right edge).
        // Each click nudges the view a few lines — the scrollbar handle shrinks to an unusable sliver on a big
        // buffer. Manual anchors + ignoreLayout so they sit over the scrollbar ends and don't scroll with content.
        // Added LAST (after the viewport + scrollbar) so they draw on top. Toggle via ChatScrollArrowButtons.
        if (Config.Settings.ChatScrollArrowButtons)
        {
            void AddScrollArrow(string nm, bool up)
            {
                var b = UIFactory.CreateButton(scroll, nm, up ? "↑" : "↓");
                var rt = b.GameObject.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, up ? 1f : 0f);
                rt.anchorMax = new Vector2(1f, up ? 1f : 0f);
                rt.pivot     = new Vector2(1f, up ? 1f : 0f);
                rt.sizeDelta = new Vector2(24f, 18f);
                rt.anchoredPosition = new Vector2(-1f, up ? -1f : 1f);
                UIFactory.SetLayoutElement(b.GameObject, ignoreLayout: true);
                if (b.ButtonText != null) { b.ButtonText.fontSize = Theme.ScaledUI(13); b.ButtonText.fontStyle = FontStyles.Bold; }
                b.OnClick = () => ScrollByStep(up ? +1f : -1f);
                TooltipHover.Attach(b.GameObject, up ? "Scroll the chat up (older messages)." : "Scroll the chat down (newer messages).");
            }
            AddScrollArrow("ChatScrollUpBtn", true);
            AddScrollArrow("ChatScrollDownBtn", false);
        }

        var lbl = UIFactory.CreateLabel(scrollContent, "ChatLog", string.Empty, TextAlignmentOptions.TopLeft);
        _log = lbl.TextMesh;
        _log.richText = true;
        _log.enableWordWrapping = true;
        // 0.17.0: chat-only size — NOT Theme.ScaledOverlay, which is the shared
        // overlay multiplier (enlarging it grew every other overlay too).
        _log.fontSize = ChatFontSize();
        _log.lineSpacing = 4f;
        _log.margin = new Vector4(6, 4, 6, 4);
        _log.color = Theme.DefaultText;
        UIFactory.SetLayoutElement(_log.gameObject, flexibleWidth: 1, flexibleHeight: 1);

        // Input row — type + Enter or the Send button to send on the active tab's
        // channel. While the field is focused, gameplay input is suppressed (you
        // don't move/attack while typing) via InputSuppression.ChatInputActive.
        // forceExpandWidth=false: the input field (flexibleWidth=1) takes the slack
        // and the Send button keeps its fixed width, instead of both flexing as you
        // type (friend-test: the Send button grew/shrank while typing).
        var inputRow = UIFactory.CreateHorizontalGroup(ContentRoot, "ChatInputRow",
            false, false, true, true, 2, new Vector4(2, 2, 2, 2), bgColor: new Color(0f, 0f, 0f, 0f));
        // childAlignment Upper so the (fixed-height) Send button stays top-aligned
        // beside the input as the input grows downward with wrapped text.
        var inputRowHlg = inputRow.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        if (inputRowHlg != null) { inputRowHlg.childAlignment = TextAnchor.UpperCenter; inputRowHlg.childForceExpandHeight = false; }
        _inputRow = inputRow;
        UIFactory.SetLayoutElement(inputRow, minHeight: 26, preferredHeight: 26, flexibleWidth: 1);
        _input = UIFactory.CreateInputField(inputRow, "ChatInput", "Type a message…");
        UIFactory.SetLayoutElement(_input.GameObject,
            minWidth: 160, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        // 0.17.0: word-wrap. SingleLine never wraps (it scrolls horizontally);
        // MultiLineSubmit wraps long messages onto multiple lines while Enter
        // STILL submits (send). The text component already has word-wrapping on.
        _input.Component.lineType = TMPro.TMP_InputField.LineType.MultiLineSubmit;
        // 0.65.3: vertically CENTER the typed text while it's a single line (messaging-app feel —
        // tester request). UpdateInputHeight flips it to TopLeft once the message wraps to multiple
        // lines so every line stays visible as the field grows. Left == middle-vertical, left-horizontal.
        if (_input.Component.textComponent != null)
            _input.Component.textComponent.alignment = TextAlignmentOptions.Left;
        if (_input.PlaceholderText != null)
            _input.PlaceholderText.alignment = TextAlignmentOptions.Left; // placeholder is always one line
        // 0.17.0: readable typing area. The framework default is grey-on-grey
        // (light grey box + grey placeholder), which the user found hard to read.
        // Give it a dark, near-opaque box (independent of the window's transparency —
        // ApplyOpacityToPanel only touches the main Content image, not this) with
        // bright typed text and a lighter placeholder. ApplyBackgroundColorRgbToPanel
        // skips this Image too (no LayoutGroup), so the theme color won't override it.
        try
        {
            // Transition None so the image shows our exact color (ColorTint would
            // multiply it by the theme's normal/hover tint and shift the result).
            _input.Component.transition = UnityEngine.UI.Selectable.Transition.None;
            var inBg = _input.GameObject.GetComponent<UnityEngine.UI.Image>();
            if (inBg != null) inBg.color = new Color(0.10f, 0.10f, 0.13f, 0.95f);
            if (_input.Component.textComponent != null)
                _input.Component.textComponent.color = new Color(0.95f, 0.95f, 0.97f, 1f);
            if (_input.PlaceholderText != null)
                _input.PlaceholderText.color = new Color(0.62f, 0.62f, 0.68f, 1f);
        }
        catch { }
        // Cap input at MaxChatChars (470). Not just for the FixedString512Bytes wire
        // limit — 480+-char messages make the server re-broadcast/flood and freeze the
        // chat box for non-Raphael clients (see MaxChatChars note). SubmitText also
        // truncates as a backstop.
        _input.Component.characterLimit = MaxChatChars;
        _input.Component.onSubmit.AddListener(OnChatSubmit);
        _input.Component.onSelect.AddListener(OnChatSelect);
        _input.Component.onDeselect.AddListener(OnChatDeselect);
        // Grow the input row vertically as wrapped text adds lines (once-per-frame
        // via InputFieldRef.OnValueChanged), shrinking back when it's cleared. Also
        // strip any Tab char that slipped in — Tab is the compose-cycle key, not text.
        _input.OnValueChanged += OnInputValueChanged;
        // Explicit Send button — reliable even if Enter is intercepted by the
        // still-visible native chat (until the native-takeover increment).
        var sendBtn = UIFactory.CreateButton(inputRow, "ChatSendButton", "Send");
        UIFactory.SetLayoutElement(sendBtn.GameObject,
            minWidth: 64, preferredWidth: 64, flexibleWidth: 0,  // fixed width — never flex while typing
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        sendBtn.OnClick = () => SubmitText(keepFocus: true);

        if (!_subscribed)
        {
            ChatRelayService.LineCaptured += OnLineCaptured;
            _subscribed = true;
        }
        // 0.17.0: Tab-to-cycle the All-tab compose target. Registered once; polls
        // each frame but no-ops unless our input is focused on the All tab.
        _composeKeyTicker = TickComposeKeys;
        Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_composeKeyTicker);
        // 0.17.3: chat-tab switch hotkeys (Modifier + 1..6). No-ops unless the chat
        // window is open, the feature is on, and the input isn't focused.
        _tabHotkeyTicker = TickTabHotkeys;
        Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_tabHotkeyTicker);
        // 0.17.3: double-click a player's name in the log to start a whisper. No-ops
        // unless the feature is on, the window's open, and the click hits a name link.
        _nameClickTicker = TickNameDoubleClick;
        Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_nameClickTicker);
        // 0.50 r8: Up/Down (+ PageUp/PageDown) scroll the chat history — the scrollbar
        // shrinks to an unusable size once the buffer grows. No-ops unless the window's open.
        _scrollKeyTicker = TickScrollKeys;
        Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_scrollKeyTicker);
        // 0.65.3: idle auto-hide fade (opt-in; no-op unless Settings.ChatAutoHideEnabled).
        _autoHideTicker = TickAutoHide;
        Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_autoHideTicker);
        _renderTicker = TickRender;
        Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_renderTicker);
        NoteChatActivity(); // start fully visible; the idle timer counts from now

        ApplyChatTextScale(); // size the input field to match the chat scale
        UpdateComposeRow();   // build + show the compose dropdown if All tab is active
        UpdateTabHighlight();
        Render();
    }

    private void OnLineCaptured(ChatRelayService.ChatLine line)
    {
        try
        {
            // 0.65.3: a qualifying line fades the window back in (System pings gated by their toggle).
            if (Settings.ChatAutoHideEnabled && WakesAutoHide(line.Channel)) NoteChatActivity();
            // Bump the unread badge for the message's channel tab — unless you're
            // already on that tab, or on All (which shows everything).
            int t = TabIndexForChannel(line.Channel);
            if (t >= 1 && _activeTab != 0 && t != _activeTab)
            {
                _unread[t]++;
                UpdateTabHighlight(); // refresh the (N) labels
            }
            // A new whisper (received or sent) re-opens a previously-closed tab.
            if (line.Channel == ChatRelayService.Channel.Whisper && !string.IsNullOrEmpty(line.Partner))
                _closedPartners.Remove(line.Partner);
            // A whisper from a new partner adds a sub-tab while the Whispers tab is open.
            if (line.Channel == ChatRelayService.Channel.Whisper
                && WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex)
                RebuildWhisperSubTabs();
            // …and adds a target to the All-tab compose dropdown/cycle (rebuilds only
            // when the target set actually changed).
            if (line.Channel == ChatRelayService.Channel.Whisper && _activeTab == 0)
                UpdateComposeRow();
            _renderDirty = true; // painted by TickRender (coalesced, skipped while closed)
        }
        catch { /* never let chat rendering throw into the inbound pump */ }
    }

    private void TickRender()
    {
        if (!_renderDirty || _log == null || !Enabled) return;
        float now = UnityEngine.Time.unscaledTime;
        if (now < _nextRenderAt) return;
        _nextRenderAt = now + RenderIntervalSeconds;
        try { Render(); _renderRetried = false; }
        catch (Exception ex)
        {
            // Render() cleared the flag before it threw. Retry once (a transient TMP/layout hiccup), but don't
            // spin on a persistent fault — that would recreate the very slowdown this throttle exists to prevent.
            if (!_renderRetried) { _renderRetried = true; _renderDirty = true; }
            else { _renderRetried = false; Utils.LogUtils.LogWarning($"[Chat] render failed twice; waiting for the next message: {ex.Message}"); }
        }
    }

    // 0.17.3: whether a channel is shown in the consolidated All tab (per settings).
    private static bool AllTabIncludes(ChatRelayService.Channel c) => c switch
    {
        ChatRelayService.Channel.Global  => Settings.AllTabShowGlobal,
        ChatRelayService.Channel.Local   => Settings.AllTabShowLocal,
        ChatRelayService.Channel.Clan    => Settings.AllTabShowClan,
        ChatRelayService.Channel.System  => Settings.AllTabShowSystem,
        ChatRelayService.Channel.Whisper => Settings.AllTabShowWhisper,
        _ => true,
    };

    // The channel tab index for a captured line's channel, or -1 (All / unmapped).
    private static int TabIndexForChannel(ChatRelayService.Channel c)
    {
        for (int i = 1; i < TabDefs.Length; i++)
            if (TabDefs[i].Filter == c) return i;
        return -1;
    }

    // Re-render with current Settings (called by the Game UI customization toggles).
    // Also re-applies the chat-only text scale so a size change takes effect
    // immediately without closing + reopening the window.
    internal void Refresh()
    {
        try { ApplyChatTextScale(); UpdateTabHighlight(); Render(); } catch { }
    }

    // #12: copy the currently-shown conversation (same filter as Render — active tab + whisper
    // partner) to the OS clipboard as plain text (timestamp · [channel] · sender: text), rich-text
    // tags stripped, oldest→newest, for pasting into a bug report / Discord.
    private void CopyConversationToClipboard()
    {
        try
        {
            var filter = TabDefs[_activeTab].Filter;
            bool whisperPartnerFilter = WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex && _activeWhisperPartner != null;
            bool Visible(ChatRelayService.ChatLine ln)
                => (!filter.HasValue || ln.Channel == filter.Value)
                   && (filter.HasValue || AllTabIncludes(ln.Channel))
                   && !(!filter.HasValue && Settings.AllTabExcludeNotesToSelf && ChatRelayService.IsNoteToSelf(ln))
                   && (!whisperPartnerFilter || ln.Partner == _activeWhisperPartner);

            var sb = new StringBuilder(4096);
            var buf = ChatRelayService.Buffer;
            int n = 0;
            for (int i = 0; i < buf.Count; i++)
            {
                var ln = buf[i];
                if (!Visible(ln)) continue;
                sb.Append(ln.Received.ToString("HH:mm")).Append("  [").Append(ln.Channel).Append("] ");
                string sender = StripRichTags(ln.Sender ?? string.Empty).Trim();
                if (sender.Length > 0) sb.Append(sender).Append(": ");
                sb.Append(StripRichTags(ln.Text ?? string.Empty).Trim()).Append('\n');
                n++;
            }
            UnityEngine.GUIUtility.systemCopyBuffer = sb.ToString();
            Utils.LogUtils.LogInfo($"[Chat] Copied {n} message(s) from the current view to the clipboard.");
        }
        catch (System.Exception ex) { Utils.LogUtils.LogError($"[Chat] copy-to-clipboard failed: {ex}"); }
    }

    // 0.17.0: chat-window font size, independent of the shared overlay text
    // scale. 12px baseline × the chat-only multiplier.
    private static float ChatFontSize() =>
        UnityEngine.Mathf.Max(8f, UnityEngine.Mathf.RoundToInt(12 * Settings.ChatTextScale));

    // Push the current chat text scale onto the log + input (+ placeholder) so a
    // live size change is reflected without a rebuild.
    private void ApplyChatTextScale()
    {
        float size = ChatFontSize();
        try { if (_log != null) _log.fontSize = size; } catch { }
        try
        {
            if (_input?.Component?.textComponent != null) _input.Component.textComponent.fontSize = size;
            if (_input?.PlaceholderText != null) _input.PlaceholderText.fontSize = size;
        }
        catch { }
        UpdateInputHeight();
    }

    private void OnInputValueChanged(string v)
    {
        // Strip control chars that the field shouldn't hold: Tab (our compose-cycle
        // key) and newlines/carriage returns. The Enter that opens/sends chat can
        // leave a stray newline in the field (the "extra blank line" friend-test
        // report) — chat is single-line + auto-wraps, so no real newline is wanted.
        // Re-setting Text re-fires this handler (now clean), which then sizes the row.
        if (!string.IsNullOrEmpty(v) && (v.IndexOf('\t') >= 0 || v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0))
        {
            _input.Text = v.Replace("\t", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
            return;
        }
        // 0.17.3: native-style channel/whisper commands — "\g " / "/global " switch the
        // send channel; "\w Name " / "\whisper Name " start a whisper. The command is
        // consumed (removed) and the channel/whisper changes live, so an All-tab user
        // can switch targets without leaving All. Re-sets Text (re-fires this clean).
        if (TryApplyChatCommand(v, out var remainder))
        {
            _input.Text = remainder ?? string.Empty;
            return;
        }
        UpdateInputHeight();
    }

    // ---- 0.17.3: in-chat channel/whisper commands (\g \global /l \whisper Name …) ----

    // Parses a leading "\cmd " / "/cmd " (or "\whisper Name "). On a recognized,
    // COMPLETED command, applies it and returns true with `remainder` = the text to
    // keep in the input (the command itself is stripped). Accepts both \ and / so
    // it's seamless for players used to the native chat.
    private bool TryApplyChatCommand(string text, out string remainder)
    {
        remainder = text;
        if (string.IsNullOrEmpty(text) || (text[0] != '\\' && text[0] != '/')) return false;
        int sp = text.IndexOf(' ');
        if (sp < 1) return false; // need a completed "<cmd> " token (cmd + trailing space)
        string cmd = text.Substring(1, sp - 1).ToLowerInvariant();
        string after = text.Substring(sp + 1);

        ChatMessageType? chan = cmd switch
        {
            "g" or "global"         => ChatMessageType.Global,
            "l" or "local"          => ChatMessageType.Local,
            "c" or "clan" or "team" => ChatMessageType.Team,
            _                       => (ChatMessageType?)null,
        };
        if (chan.HasValue)
        {
            SetComposeChannel(chan.Value);
            remainder = after;            // keep whatever message they'd started typing
            return true;
        }

        if (cmd is "w" or "whisper" or "wisp" or "wispr")
        {
            int sp2 = after.IndexOf(' ');
            if (sp2 < 1) return false;    // name still being typed (no trailing space yet)
            string name = after.Substring(0, sp2);
            string rest = after.Substring(sp2 + 1);
            if (TryStartWhisperByName(name)) { remainder = rest; return true; }
            // Unresolved: tell the user (with who IS whisperable) and consume the
            // command so it doesn't re-fire each keystroke.
            ReportWhisperResolveFailure(name);
            remainder = string.Empty;
            return true;
        }
        return false;
    }

    // Point the All-tab compose target at a channel (switching to the All tab first,
    // since that's where free channel-switching lives).
    private void SetComposeChannel(ChatMessageType type)
    {
        if (_activeTab != 0)
        {
            _activeTab = 0;
            UpdateWhisperSubRow();
            UpdateTabHighlight();
        }
        UpdateComposeRow();               // ensures targets + the dropdown exist
        int idx = _composeTargets.FindIndex(t => !t.IsWhisper && t.Channel == type);
        if (idx < 0) idx = DefaultComposeIndex(); // e.g. Clan requested while not in a clan
        _composeIndex = Mathf.Clamp(idx, 0, Mathf.Max(0, _composeTargets.Count - 1));
        if (_composeDropdown != null) _composeDropdown.SetValueWithoutNotify(_composeIndex);
        Render();
    }

    // Resolve a typed name to a known whisper target and make it the active All-tab
    // compose target (stays on All). Returns false if the name can't be resolved.
    private bool TryStartWhisperByName(string typed)
    {
        if (!ResolvePlayer(typed, out var name, out var id)) return false;
        BeginWhisperTo(name, id);
        return true;
    }

    // 0.17.3 (#38): begin a whisper to an ALREADY-resolved target (name + NetworkId) and
    // make it the active All-tab compose target. Shared by TryStartWhisperByName and the
    // social-menu redirect (BCHubUIManager.BeginExternalWhisper); the latter has the id
    // straight from the game so it needs no name lookup. Stays on the All tab, where
    // free channel/whisper switching lives.
    public void BeginWhisperTo(string name, NetworkId id)
    {
        if (string.IsNullOrEmpty(name)) return;
        ChatRelayService.RememberWhisperTarget(name, id);
        _closedPartners.Remove(name);
        _initiatedPartners.Add(name);
        // B4: if the whisper was started FROM the Whispers tab (e.g. /whisper Name typed in its message bar),
        // open that conversation IN PLACE instead of jumping to the All tab.
        if (WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex)
        {
            _activeWhisperPartner = name; // view
            _whisperSendTo = name;        // and target them
            RebuildWhisperSubTabs(); // adds the partner's sub-tab + syncs the message-bar recipient box
            Render();
            return;
        }
        if (_activeTab != 0)
        {
            _activeTab = 0;
            UpdateWhisperSubRow();
            UpdateTabHighlight();
        }
        UpdateComposeRow();               // rebuilds the dropdown to include the new partner
        int idx = _composeTargets.FindIndex(t => t.IsWhisper && string.Equals(t.Whisper, name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            _composeIndex = idx;
            if (_composeDropdown != null) _composeDropdown.SetValueWithoutNotify(_composeIndex);
        }
        Render();
    }

    // 0.17.3: resolve a typed (possibly partial) player name to a whisper target.
    // Pool = players seen in chat (have a NetworkId) + the nearby/online roster.
    // Exact case-insensitive match wins; otherwise the first unique startswith match.
    // Truly-remote silent players can't be resolved client-side (no NetworkId yet).
    private static bool ResolvePlayer(string typed, out string name, out NetworkId id)
    {
        name = null; id = default;
        if (string.IsNullOrWhiteSpace(typed)) return false;
        typed = typed.Trim();
        var pool = ChatRelayService.GetKnownPlayers();
        try { pool.AddRange(PlayerRosterService.GetOnlinePlayers()); } catch { }
        try { if (PlayerRosterService.TryGetSelfPlayer(out var me)) pool.Add(me); } catch { }   // B1: allow whispering yourself (note to self)
        foreach (var p in pool)
            if (string.Equals(p.Name, typed, StringComparison.OrdinalIgnoreCase)) { name = p.Name; id = p.Id; return true; }
        foreach (var p in pool)
            if (!string.IsNullOrEmpty(p.Name) && p.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) { name = p.Name; id = p.Id; return true; }
        return false;
    }

    // 0.17.3: the distinct set of players the client can currently whisper (have a
    // NetworkId): seen in chat + the nearby/online roster. Used for the helpful
    // "who CAN I whisper" feedback when a typed name doesn't resolve.
    private static List<string> ResolvablePlayerNames()
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(IEnumerable<PlayerRosterService.PlayerRef> src)
        {
            foreach (var p in src) if (!string.IsNullOrEmpty(p.Name) && seen.Add(p.Name)) names.Add(p.Name);
        }
        try { Add(ChatRelayService.GetKnownPlayers()); } catch { }
        try { Add(PlayerRosterService.GetOnlinePlayers()); } catch { }
        try { if (PlayerRosterService.TryGetSelfPlayer(out var me)) Add(new[] { me }); } catch { }   // B1: you can whisper yourself too
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    // 0.17.3: explain a failed whisper resolution AND list who's actually whisperable,
    // so the user (and we) can see whether the target is simply outside the client's
    // known set. A whisper needs the target's NetworkId, which the client only has for
    // nearby players + anyone who's spoken — a truly-remote silent player can't be
    // resolved client-side (V Rising has no server-side whisper-by-name).
    private static void ReportWhisperResolveFailure(string typed)
    {
        var names = ResolvablePlayerNames();
        string list = names.Count == 0
            ? "(none known yet — no one nearby has spoken)"
            : (names.Count <= 15 ? string.Join(", ", names)
                                 : string.Join(", ", names.GetRange(0, 15)) + $", +{names.Count - 15} more");
        ChatRelayService.CaptureLocalEcho(ChatRelayService.Channel.System,
            $"No whisper match for \"{typed}\". Players I can whisper now: {list}. (You can whisper any player connected to the server, or anyone who's spoken in chat.)");
        Utils.LogUtils.LogInfo($"[Whisper] resolve failed for '{typed}'. Whisperable ({names.Count}): {string.Join(", ", names)}");
    }

    // 0.17.0: size the input row to fit the wrapped text — one line at rest,
    // growing up to InputMaxHeight, then shrinking back when the message is sent.
    private void UpdateInputHeight()
    {
        try
        {
            var txt = _input?.Component?.textComponent;
            if (txt == null) return;
            // Empty field → snap straight back to one line. Reading preferredHeight
            // alone didn't shrink reliably after a send (TMP keeps the prior multi-
            // line geometry until the mesh is rebuilt), so force the rebuild first.
            string cur = _input.Text;
            float pref; int lineCount;
            if (string.IsNullOrEmpty(cur)) { pref = 0f; lineCount = 0; }
            else { txt.ForceMeshUpdate(); pref = txt.preferredHeight; lineCount = txt.textInfo.lineCount; }
            // 0.65.4: drive single-vs-multi off the ACTUAL wrapped line count, not a clamped pixel
            // height. The old `h > InputBaseHeight+2` test was fragile: once the field had grown to
            // multiple lines (e.g. pasting a long message), TMP's preferredHeight for the SAME text
            // reduced back to one line reads ~a line taller than it did before the multi-line episode,
            // so h stayed over the threshold and the input never re-centered — the "center alignment
            // stops working after pasting long text" report (Kruczarz). lineCount is drift-proof.
            bool multiLine = lineCount > 1;
            // Single line (or empty) → always the base height; multi-line → grow with the content.
            int h = multiLine
                ? (int)UnityEngine.Mathf.Clamp(pref + 6f, InputBaseHeight, InputMaxHeight)
                : (int)InputBaseHeight;
            UIFactory.SetLayoutElement(_input.GameObject, minHeight: h, preferredHeight: h);
            if (_inputRow != null)
                UIFactory.SetLayoutElement(_inputRow, minHeight: h + 2, preferredHeight: h + 2);
            // Single line → vertically centered (Left); wrapped → top-aligned (TopLeft) so all lines
            // stay visible as the field grows.
            var want = multiLine
                ? TextAlignmentOptions.TopLeft
                : TextAlignmentOptions.Left;
            if (txt.alignment != want) txt.alignment = want;
        }
        catch { }
    }

    // ---- input / send (increment 2) ----

    // 0.17 (2c): focus the input — the divert target when the takeover intercepts
    // the game's chat-open key (Enter). ActivateInputField fires onSelect, which
    // sets ChatInputActive so gameplay input is suppressed while you type.
    internal void FocusInput()
    {
        NoteChatActivity(); // 0.65.3: opening chat wakes the window even if it had faded out
        try
        {
            // 0.17.0: don't re-activate an already-focused field — re-activating
            // every frame is what pinned ChatInputActive on and trapped the player.
            if (_input?.Component != null && !_input.Component.isFocused)
                _input.Component.ActivateInputField();
        }
        catch { }
    }

    // 0.17.0 escape hatch / canonical defocus. Deactivates the field, clears the
    // EventSystem selection, and clears ChatInputActive. The EventSystem clear is
    // load-bearing: V Rising suppresses gameplay input while a UI text field is the
    // EventSystem's selected object, and DeactivateInputField alone does NOT clear
    // that selection — so without this the game keeps gameplay frozen even after we
    // "defocus" (the post-chat freeze).
    internal void ReleaseInput()
    {
        try { _input?.Component?.DeactivateInputField(); } catch { }
        try
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && _input?.Component != null
                && es.currentSelectedGameObject == _input.Component.gameObject)
                es.SetSelectedGameObject(null);
        }
        catch { }
        Patches.InputSuppression.ChatInputActive = false;
    }

    // 0.18.3: full reset for a server switch. Called from the relog path
    // (BCHubUIManager.RestoreAfterRelogIfNeeded) AFTER ChatRelayService.Clear() has emptied the
    // shared scrollback buffer in the teardown hook. Clears any half-typed message, releases focus,
    // and repaints — so the new server's chat window starts empty instead of inheriting the previous
    // server's messages + compose state. Safe to call when the window isn't built (null-guarded).
    internal void ResetForServerSwitch()
    {
        try { if (_input != null) _input.Text = string.Empty; } catch { }
        try { ReleaseInput(); } catch { }
        try { Refresh(); } catch { }   // Render() repaints from the now-cleared buffer
    }

    // 0.17.0: authoritative focus state, polled each frame to drive ChatInputActive.
    // onSelect/onDeselect aren't reliable — DeactivateInputField doesn't always raise
    // onDeselect — so the event-set flag could stick true and leave gameplay input
    // suppressed after you finished chatting (the post-chat freeze). Reading the
    // field's real focus each frame means suppression can never stick on.
    internal bool IsInputFocused()
    {
        try { return _input?.Component != null && _input.Component.isFocused; }
        catch { return false; }
    }

    // 0.17.3: is the mouse cursor currently over the chat window? Used to suppress the
    // game's primary ATTACK while you click the window (tabs / input), so the click
    // doesn't leak into the world as an attack and get the character stuck repeating
    // it. Targeted rect test (not the whole EventSystem), so it's only ever true when
    // the cursor is literally over this window — never during combat.
    internal bool IsPointerOverWindow()
    {
        try
        {
            if (!Enabled) return false;
            var rt = uiRoot != null ? uiRoot.GetComponent<RectTransform>() : null;
            if (rt == null) return false;
            // ScreenSpaceOverlay canvas → null camera.
            return UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(rt, UnityEngine.Input.mousePosition, null);
        }
        catch { return false; }
    }

    // ---- 0.65.3: idle auto-hide fade ----

    // Record "the window is being used right now" so the idle timer restarts (and the window fades
    // back in on the next tick). Called on construct, tab switch, send, and qualifying messages.
    private void NoteChatActivity()
    {
        try { _lastActivityUnscaled = UnityEngine.Time.unscaledTime; } catch { }
    }

    // Does a captured line count as "activity" that fades the window back in? System-channel lines
    // (Raphael's regular BC pings / server broadcasts) are gated behind their own toggle so they can't
    // blink the window; everything else follows the message toggle.
    private static bool WakesAutoHide(ChatRelayService.Channel ch)
        => ch == ChatRelayService.Channel.System
            ? Settings.ChatAutoHideWakeOnSystem
            : Settings.ChatAutoHideWakeOnMessage;

    private UnityEngine.CanvasGroup EnsureRootCanvasGroup()
    {
        if (_rootCg != null) return _rootCg;
        try
        {
            if (uiRoot == null) return null;
            _rootCg = uiRoot.GetComponent<UnityEngine.CanvasGroup>()
                      ?? uiRoot.AddComponent<UnityEngine.CanvasGroup>();
        }
        catch { }
        return _rootCg;
    }

    // Per-frame fade driver. No-ops (holds full alpha) unless auto-hide is enabled AND the window is
    // open. Fades toward 0 after the idle timeout, toward 1 whenever the input is focused, the cursor
    // is over the window, or a qualifying message reset the activity clock. When essentially invisible,
    // blocksRaycasts drops so the faded window doesn't swallow clicks meant for the world/other UI.
    private void TickAutoHide()
    {
        try
        {
            var cg = EnsureRootCanvasGroup();
            if (cg == null) return;

            // Not actually on screen (window closed, or master "hide all overlays" active): keep the
            // idle clock fresh and don't touch the fade, so it appears fully visible when shown again.
            bool visibleInHierarchy = uiRoot != null && uiRoot.activeInHierarchy;
            if (!visibleInHierarchy || !_wasEnabledForAutoHide)
            {
                NoteChatActivity();
                if (cg.alpha != 1f) { cg.alpha = 1f; cg.blocksRaycasts = true; }
            }
            _wasEnabledForAutoHide = visibleInHierarchy;
            if (!visibleInHierarchy) return;

            float target;
            if (!Settings.ChatAutoHideEnabled)
            {
                target = 1f; // feature off → never leave it dimmed
            }
            else if (IsInputFocused() || IsPointerOverWindow())
            {
                NoteChatActivity();
                target = 1f;
            }
            else
            {
                float idle = UnityEngine.Time.unscaledTime - _lastActivityUnscaled;
                target = idle >= Settings.ChatAutoHideSeconds ? 0f : 1f;
            }

            if (!UnityEngine.Mathf.Approximately(cg.alpha, target))
            {
                // ~0.3s full fade, frame-rate independent.
                cg.alpha = UnityEngine.Mathf.MoveTowards(cg.alpha, target, 3.5f * UnityEngine.Time.unscaledDeltaTime);
            }
            bool interactive = cg.alpha > 0.05f;
            if (cg.blocksRaycasts != interactive) cg.blocksRaycasts = interactive;
        }
        catch { /* fade is best-effort; never throw from the update loop */ }
    }

    private void OnChatSelect(string _)   => Patches.InputSuppression.ChatInputActive = true;
    private void OnChatDeselect(string _) => Patches.InputSuppression.ChatInputActive = false;
    private void OnChatSubmit(string _)   => SubmitText(keepFocus: false); // Enter

    // Sends the current input on the active tab's channel, then clears. keepFocus
    // (Send button) re-activates the field so you can keep chatting; Enter
    // releases focus so movement resumes (game-like).
    private void SubmitText(bool keepFocus)
    {
        if (_input == null) return;
        try
        {
            var msg = _input.Text?.Trim();
            if (msg != null && msg.Length > MaxChatChars) msg = msg.Substring(0, MaxChatChars); // backstop
            if (!string.IsNullOrEmpty(msg))
            {
                bool onWhispersTab = WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex;
                if (onWhispersTab)
                {
                    // 0.29.7 PRIVACY FIX: the Whispers tab ONLY ever sends as a whisper to the active
                    // partner — it must NEVER fall back to a channel. Previously, with no active/resolvable
                    // partner (the "All" whisper view, or a partner who went offline), the message dropped
                    // into the channel `else` below → ActiveSendChannel() → LOCAL, leaking the private
                    // message into Local chat where everyone nearby could read it (tester report). Now: send
                    // only if the partner resolves to a target; otherwise tell the user and send NOTHING.
                    // Send to the recipient box's target (_whisperSendTo) — independent of which conversation
                    // is being VIEWED, so you can sit on "All Whispers" and still reply to a chosen person.
                    if (_whisperSendTo != null && ChatRelayService.TryGetWhisperTarget(_whisperSendTo, out var target))
                    {
                        MessageService.SendWhisper(msg, target);
                        ChatRelayService.CaptureLocalEcho(ChatRelayService.Channel.Whisper, msg, _whisperSendTo);
                    }
                    else
                    {
                        ChatRelayService.CaptureLocalEcho(ChatRelayService.Channel.System,
                            _whisperSendTo == null
                                ? "Pick a whisper recipient first (the box left of the input, a sub-tab, or “+ Whisper…”) before sending — your message was NOT sent to any channel."
                                : $"Can't whisper \"{_whisperSendTo}\" right now (they may have gone offline). Your message was NOT sent.");
                    }
                }
                else if (_activeTab == 0)
                {
                    // All tab: send to the chosen compose target (Global/Local/Clan
                    // or a whisper partner) — set via the "Send to:" dropdown or Tab.
                    EnsureComposeTargets();
                    if (_composeTargets.Count > 0 && _composeIndex >= 0 && _composeIndex < _composeTargets.Count
                        && _composeTargets[_composeIndex].IsWhisper)
                    {
                        var wp = _composeTargets[_composeIndex].Whisper;
                        if (ChatRelayService.TryGetWhisperTarget(wp, out var nid))
                        {
                            MessageService.SendWhisper(msg, nid);
                            ChatRelayService.CaptureLocalEcho(ChatRelayService.Channel.Whisper, msg, wp);
                        }
                        else
                        {
                            // 0.29.7: a whisper compose-target that no longer resolves must NOT leak to a
                            // channel either — tell the user, send nothing.
                            ChatRelayService.CaptureLocalEcho(ChatRelayService.Channel.System,
                                $"Can't whisper \"{wp}\" right now (they may have gone offline). Your message was NOT sent.");
                        }
                    }
                    else
                    {
                        var type = (_composeTargets.Count > 0 && _composeIndex >= 0 && _composeIndex < _composeTargets.Count
                                    && _composeTargets[_composeIndex].Channel.HasValue)
                            ? _composeTargets[_composeIndex].Channel.Value
                            : ActiveSendChannel();
                        MessageService.SendChat(msg, type);
                        ChatRelayService.CaptureLocalEcho(EchoChannelFor(type), msg);
                    }
                }
                else
                {
                    var type = ActiveSendChannel();
                    MessageService.SendChat(msg, type);
                    // The server broadcasts our message to others but doesn't echo it
                    // back to us, so add a local echo so we see our own message here.
                    ChatRelayService.CaptureLocalEcho(EchoChannelFor(type), msg);
                }
            }
        }
        catch (System.Exception ex)
        {
            Utils.LogUtils.LogError($"Chat send failed: {ex}");
        }
        finally
        {
            _input.Text = string.Empty;
            UpdateInputHeight(); // contract the (possibly grown) input back to one line
            if (keepFocus)
            {
                _input.Component.ActivateInputField();
            }
            else
            {
                ReleaseInput(); // deactivate + clear EventSystem selection + flag
            }
        }
    }

    // The active tab decides the outgoing channel. All / System / Whispers fall
    // back to Local (System isn't player-sendable; whisper send needs a target,
    // which comes with the per-person whisper tabs in a later increment).
    private ChatMessageType ActiveSendChannel()
    {
        var f = TabDefs[_activeTab].Filter;
        // All tab: send on the user's chosen default (Global or Local).
        if (f == null)
            return Settings.ChatAllTabDefaultGlobal ? ChatMessageType.Global : ChatMessageType.Local;
        return f switch
        {
            ChatRelayService.Channel.Global => ChatMessageType.Global,
            ChatRelayService.Channel.Clan   => ChatMessageType.Team,
            ChatRelayService.Channel.Local  => ChatMessageType.Local,
            _                               => ChatMessageType.Local, // System not player-sendable
        };
    }

    // Map an outgoing ChatMessageType to the buffer channel for the local echo.
    private static ChatRelayService.Channel EchoChannelFor(ChatMessageType t) => t switch
    {
        ChatMessageType.Global => ChatRelayService.Channel.Global,
        ChatMessageType.Team   => ChatRelayService.Channel.Clan,
        _                      => ChatRelayService.Channel.Local,
    };

    // 0.17.3: select a tab (shared by tab-button clicks and the tab hotkeys).
    private void SwitchToTab(int idx)
    {
        if (idx < 0 || idx >= TabDefs.Length) return;
        NoteChatActivity();                                            // 0.65.3: interacting keeps it visible
        _activeTab = idx;
        _unread[idx] = 0;                                              // viewing clears its badge
        if (idx == 0) for (int k = 0; k < _unread.Length; k++) _unread[k] = 0; // All sees everything
        UpdateWhisperSubRow();
        UpdateComposeRow();
        UpdateTabHighlight();
        Render();
    }

    // 0.17.3: <Modifier>+1..6 switches tabs while the chat window is open and the
    // input is NOT focused (so the keys still type normally while composing). The
    // key isn't consumed — the game still sees it — so pick a non-conflicting
    // modifier (default Shift). Registered on CoreUpdateBehavior.
    private void TickTabHotkeys()
    {
        try
        {
            if (!Enabled || !Settings.ChatTabHotkeysEnabled) return;
            if (IsInputFocused()) return;                 // don't hijack keys while typing
            if (!ModifierHeld(Settings.ChatTabHotkeyModifier)) return;
            int count = System.Math.Min(TabDefs.Length, 9);
            for (int i = 0; i < count; i++)
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1 + i)) { SwitchToTab(i); break; }
            }
        }
        catch { }
    }

    private static bool ModifierHeld(string mod)
    {
        switch ((mod ?? "Alt").Trim().ToLowerInvariant())
        {
            case "none":                 return true;
            case "ctrl": case "control": return UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
            case "shift":                return UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            default:                     return UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt); // Alt (default)
        }
    }

    private void UpdateTabHighlight()
    {
        var activeColor = Theme.SliderFill;
        var inactiveColor = activeColor * 0.5f; inactiveColor.a = activeColor.a;
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            var btn = _tabButtons[i];
            if (btn?.Component == null) continue;
            // Unread badge in the label, e.g. "Clan (3)". All tab never badges.
            if (btn.ButtonText != null)
            {
                btn.ButtonText.text = _unread[i] > 0 ? $"{TabDefs[i].Label} ({_unread[i]})" : TabDefs[i].Label;
                // 0.17.0: tint the tab label in its channel's color (All stays neutral).
                btn.ButtonText.color = (Settings.ChatColorTabs && TabDefs[i].Filter.HasValue)
                    ? ChannelColor(TabDefs[i].Filter) : Theme.DefaultText;
            }
            var baseC = (i == _activeTab) ? activeColor : inactiveColor;
            var cb = btn.Component.colors;
            cb.normalColor      = baseC;
            cb.highlightedColor = baseC * 1.2f;
            cb.selectedColor    = baseC * 1.1f;
            cb.pressedColor     = baseC * 0.7f;
            btn.Component.colors = cb;
        }
    }

    // Show/hide the whisper sub-tab row and rebuild it when the Whispers tab is active.
    private void UpdateWhisperSubRow()
    {
        if (_whisperSubRow == null) return;
        bool show = WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex;
        _whisperSubRow.SetActive(show);
        if (show) RebuildWhisperSubTabs();
        else UpdateWhisperPicker(false); // hide the bottom "+ Whisper…" picker off the Whispers tab
    }

    // Rebuild the whisper sub-tab buttons: "All" + one per distinct partner.
    private void RebuildWhisperSubTabs()
    {
        if (_whisperSubRow == null) return;
        // Destroy ALL existing sub-tab children (the "All"/partner buttons + close ×s). The "+ Whisper…"
        // picker no longer lives here — it's at the bottom input line (UpdateWhisperPicker).
        var t = _whisperSubRow.transform;
        for (int i = t.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        _whisperSubButtons.Clear();
        _whisperSubPartners.Clear();

        var partners = WhisperPartners();
        if (_activeWhisperPartner != null && !partners.Contains(_activeWhisperPartner))
            _activeWhisperPartner = null; // partner gone — fall back to All

        AddWhisperSubButton(null, "All");
        foreach (var p in partners) { AddWhisperSubButton(p, p); AddWhisperCloseButton(p); }
        HighlightWhisperSubTabs();
        UpdateComposeRow();        // recipient box (active conversations), kept in sync with the partner set
        UpdateWhisperPicker(true); // "+ Whisper…" new-conversation picker on the bottom input line
    }

    // Small "x" beside each partner sub-tab to close that conversation. Lets a long
    // session's whisper tabs be cleared. Closing suppresses the partner (even if old
    // whisper lines remain) until they whisper again.
    private void AddWhisperCloseButton(string partner)
    {
        var btn = UIFactory.CreateButton(_whisperSubRow, $"WhisperClose_{partner}", "x");
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 18, preferredWidth: 18, flexibleWidth: 0, minHeight: 20, preferredHeight: 20, flexibleHeight: 0);
        btn.OnClick = () => CloseWhisperPartner(partner);
        TooltipHover.Attach(btn.GameObject, $"Close the whisper tab with {partner}. It reappears if they whisper you again.");
    }

    private void CloseWhisperPartner(string partner)
    {
        if (string.IsNullOrEmpty(partner)) return;
        _initiatedPartners.Remove(partner);
        _closedPartners.Add(partner);
        if (_activeWhisperPartner == partner) _activeWhisperPartner = null;
        RebuildWhisperSubTabs();
        Render();
    }

    // Dropdown of players you can start a whisper with. 0.17.3 (#38): sourced from the
    // FULL connected-player roster (PlayerRosterService — the non-culled UserInfoElement
    // buffer, the same data the P-key social page shows), MERGED with anyone seen in
    // chat (covers a partner who just disconnected). Deduped by name, online first.
    // Selecting a name opens that conversation immediately.
    // 0.29.10: the bottom-of-the-Whispers-tab "+ Whisper…" picker — pick any connected player (or yourself)
    // to START a new conversation. Lives on the input line, immediately left of the message box (beside the
    // recipient box). Rebuilt when the partner set / roster changes (RebuildWhisperSubTabs); hidden off the
    // Whispers tab. (Replaces the old confusing top "whisper a player by name" text-input row.)
    private void UpdateWhisperPicker(bool onWhispers)
    {
        if (_whisperPicker != null) { UnityEngine.Object.Destroy(_whisperPicker.gameObject); _whisperPicker = null; }
        if (!onWhispers || _inputRow == null) return;
        _pickerRoster = BuildWhisperCandidates();
        var options = new List<string> { "+ Whisper…" };
        foreach (var p in _pickerRoster) options.Add(p.Name);
        var ddObj = UIFactory.CreateDropdown(_inputRow, "WhisperPicker", out _whisperPicker,
            "+ Whisper…", 12, OnWhisperPickerChanged, options.ToArray());
        UIFactory.SetLayoutElement(ddObj, minWidth: 104, preferredWidth: 130, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        ApplyDropdownNoWrap(_whisperPicker); // long names stay one line
        // Place it immediately LEFT of the message input (so order is: [recipient box][+ Whisper…][input][Send]).
        int inputIdx = _input != null ? _input.GameObject.transform.GetSiblingIndex() : 0;
        ddObj.transform.SetSiblingIndex(Mathf.Max(0, inputIdx));
        // Without this the dropdown never closes — TMP's own blocker doesn't fire in our canvas, so the
        // registry's per-frame outside-click check dismisses it.
        Raphael.UI.Forms.FormDropdownRegistry.Register(_whisperPicker);
        TooltipHover.Attach(ddObj, "Whisper someone new — pick any connected player (or yourself) to start a conversation. They get a tab above; switch between active conversations with those tabs.");
    }

    // 0.17.3 (#38): the whisper picker's candidate list — every connected player
    // (full non-culled roster) plus anyone seen in chat, deduped by name (online
    // roster wins, so it carries a current NetworkId), sorted A-Z.
    private static List<PlayerRosterService.PlayerRef> BuildWhisperCandidates()
    {
        var list = new List<PlayerRosterService.PlayerRef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(IEnumerable<PlayerRosterService.PlayerRef> src)
        {
            if (src == null) return;
            foreach (var p in src)
                if (!string.IsNullOrEmpty(p.Name) && seen.Add(p.Name)) list.Add(p);
        }
        try { Add(PlayerRosterService.GetOnlinePlayers()); } catch { }
        try { Add(ChatRelayService.GetKnownPlayers()); } catch { }
        try { if (PlayerRosterService.TryGetSelfPlayer(out var me)) Add(new[] { me }); } catch { }   // B1: include yourself (note to self)
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    private void OnWhisperPickerChanged(int index)
    {
        try
        {
            if (index <= 0 || _pickerRoster == null || index - 1 >= _pickerRoster.Count) return;
            var pr = _pickerRoster[index - 1];
            ChatRelayService.RememberWhisperTarget(pr.Name, pr.Id); // reply works before they message us
            _closedPartners.Remove(pr.Name); // explicit re-initiation un-closes
            _initiatedPartners.Add(pr.Name);
            _activeWhisperPartner = pr.Name; // open their conversation (view)
            _whisperSendTo = pr.Name;        // and target them so your next message goes to them
            RebuildWhisperSubTabs(); // adds their sub-tab + refreshes the bottom picker (resets its caption)
            Render();
        }
        catch (System.Exception ex) { Utils.LogUtils.LogError($"WhisperPicker: {ex}"); }
    }

    private void AddWhisperSubButton(string partner, string label)
    {
        var btn = UIFactory.CreateButton(_whisperSubRow, $"WhisperSub_{label}", label);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 40, preferredWidth: 90, flexibleWidth: 1, minHeight: 20, preferredHeight: 20, flexibleHeight: 0);
        // 0.17.3: long partner names used to word-wrap inside the sub-tab cell and
        // look odd — keep them on one line, truncating with an ellipsis instead.
        if (btn.ButtonText != null)
        {
            btn.ButtonText.enableWordWrapping = false;
            btn.ButtonText.overflowMode = TextOverflowModes.Ellipsis;
        }
        btn.OnClick = () =>
        {
            _activeWhisperPartner = partner;                 // switch the VIEW
            if (partner != null) _whisperSendTo = partner;   // opening a person also targets them; "All" keeps the current send target
            HighlightWhisperSubTabs(); UpdateComposeRow(); Render();
        };
        _whisperSubButtons.Add(btn);
        _whisperSubPartners.Add(partner);
    }

    // Distinct whisper conversation partners: everyone with a whisper line (the
    // Partner field), plus players picked from the dropdown to initiate — MINUS any
    // the user has explicitly closed (so merely-chatted players never appear here;
    // only actual whispers, received or initiated, create a tab).
    private List<string> WhisperPartners()
    {
        var list = new List<string>();
        var buf = ChatRelayService.Buffer;
        for (int i = 0; i < buf.Count; i++)
        {
            var ln = buf[i];
            if (ln.Channel != ChatRelayService.Channel.Whisper || string.IsNullOrEmpty(ln.Partner)) continue;
            if (_closedPartners.Contains(ln.Partner)) continue;
            if (!list.Contains(ln.Partner)) list.Add(ln.Partner);
        }
        foreach (var p in _initiatedPartners)
            if (!_closedPartners.Contains(p) && !list.Contains(p)) list.Add(p);
        return list;
    }

    private void HighlightWhisperSubTabs()
    {
        var activeColor = Theme.SliderFill;
        var inactiveColor = activeColor * 0.5f; inactiveColor.a = activeColor.a;
        for (int i = 0; i < _whisperSubButtons.Count; i++)
        {
            var btn = _whisperSubButtons[i];
            if (btn?.Component == null) continue;
            bool isActive = i < _whisperSubPartners.Count && _whisperSubPartners[i] == _activeWhisperPartner;
            var baseC = isActive ? activeColor : inactiveColor;
            var cb = btn.Component.colors;
            cb.normalColor = baseC; cb.highlightedColor = baseC * 1.2f;
            cb.selectedColor = baseC * 1.1f; cb.pressedColor = baseC * 0.7f;
            btn.Component.colors = cb;
            // 0.17.0: tint whisper sub-tab labels in the Whisper color when colored
            // tabs are on (these are all whisper conversations).
            if (btn.ButtonText != null)
                btn.ButtonText.color = Settings.ChatColorTabs
                    ? ChannelColor(ChatRelayService.Channel.Whisper) : Theme.DefaultText;
        }
    }

    // ---- All-tab compose target (Send-to dropdown + Tab cycle) ----

    // Show/hide the compact compose dropdown beside the input, and rebuild it when the available targets
    // change (clan join/leave, new partner). Shown on the All tab (channel/whisper picker) AND, B4 (0.29.8),
    // on the Whispers tab (a recipient quick-switch: your active conversations, mirroring the top sub-tabs).
    private void UpdateComposeRow()
    {
        bool onWhispers = WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex;
        bool show = _activeTab == 0 || onWhispers;
        if (!show)
        {
            if (_composeDropdownObj != null) _composeDropdownObj.SetActive(false);
            return;
        }
        // On the Whispers tab with no conversations, there's nothing to switch between — hide the box AND
        // clear stale targets so Tab can't cycle ghost recipients (B7) and no send target lingers. (Start your
        // first/next whisper with the name field above, or /whisper Name.)
        if (onWhispers && WhisperPartners().Count == 0)
        {
            if (_composeDropdownObj != null) _composeDropdownObj.SetActive(false);
            _composeTargets.Clear();
            _composeSignature = "";
            _whisperSendTo = null;
            return;
        }
        bool changed = EnsureComposeTargets();
        if (changed || _composeDropdown == null || _composeDropdownObj == null) RebuildComposeRow();
        else
        {
            _composeDropdownObj.SetActive(true);
            // B-fix (0.50 r8): the SEND target (_whisperSendTo) can change via a whisper sub-tab or the
            // "+ Whisper…" picker WITHOUT the target LIST changing (e.g. re-picking someone who already has a
            // tab). EnsureComposeTargets returns false in that case, so re-sync the selected index to the
            // current recipient here — otherwise the "@name" box would keep showing the previous person.
            if (onWhispers && _whisperSendTo != null)
            {
                int wi = _composeTargets.FindIndex(t => t.IsWhisper && string.Equals(t.Whisper, _whisperSendTo, StringComparison.OrdinalIgnoreCase));
                if (wi >= 0) _composeIndex = wi;
            }
            _composeDropdown.SetValueWithoutNotify(Mathf.Clamp(_composeIndex, 0, _composeTargets.Count - 1));
        }
    }

    // Rebuild _composeTargets = Global, Local, [Clan if in a clan], then each active
    // whisper partner. Returns true if the set changed (so the dropdown is rebuilt).
    // Preserves the current selection by label where possible; else falls to default.
    private bool EnsureComposeTargets()
    {
        bool onWhispers = WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex;
        var partners = WhisperPartners();
        // Signature gates LIST rebuilds (tab + partner set). Selection changes (_whisperSendTo / _composeIndex)
        // are applied by the handlers directly, so they don't need to be in the signature.
        string sig = (onWhispers ? "W|" : (IsInClan() ? "C|" : "-|")) + string.Join("|", partners);
        if (sig == _composeSignature && _composeTargets.Count > 0) return false;
        _composeSignature = sig;

        string prevLabel = (_composeIndex >= 0 && _composeIndex < _composeTargets.Count) ? _composeTargets[_composeIndex].Label : null;
        _composeTargets.Clear();

        if (onWhispers)
        {
            // B4: the Whispers-tab box is a SEND-RECIPIENT picker (one entry per active conversation),
            // DECOUPLED from the view — so you can sit on "All Whispers" and still pick who to reply to. Keep
            // _whisperSendTo on a real partner so sending always has a valid, visible target; fall back to the
            // viewed partner, else the first conversation.
            foreach (var p in partners) _composeTargets.Add(SendTarget.Whis(p));
            if (_composeTargets.Count == 0) { _whisperSendTo = null; _composeIndex = 0; return true; }
            if (_whisperSendTo == null || _composeTargets.FindIndex(t => string.Equals(t.Whisper, _whisperSendTo, StringComparison.OrdinalIgnoreCase)) < 0)
                _whisperSendTo = (_activeWhisperPartner != null && partners.Contains(_activeWhisperPartner)) ? _activeWhisperPartner : partners[0];
            int wi = _composeTargets.FindIndex(t => string.Equals(t.Whisper, _whisperSendTo, StringComparison.OrdinalIgnoreCase));
            _composeIndex = Mathf.Clamp(wi < 0 ? 0 : wi, 0, _composeTargets.Count - 1);
            return true;
        }

        _composeTargets.Add(SendTarget.Chan(ChatMessageType.Global, "Global"));
        _composeTargets.Add(SendTarget.Chan(ChatMessageType.Local, "Local"));
        if (IsInClan()) _composeTargets.Add(SendTarget.Chan(ChatMessageType.Team, "Clan"));
        foreach (var p in partners) _composeTargets.Add(SendTarget.Whis(p));

        int idx = (prevLabel != null) ? _composeTargets.FindIndex(t => t.Label == prevLabel) : -1;
        if (idx < 0) idx = DefaultComposeIndex();
        _composeIndex = Mathf.Clamp(idx, 0, _composeTargets.Count - 1);
        return true;
    }

    // Default All-tab send channel: Global or Local per the user's setting.
    private static int DefaultComposeIndex() => Settings.ChatAllTabDefaultGlobal ? 0 : 1;

    // Build the compact compose dropdown as the FIRST child of the input row, so it
    // sits as a small selectable button left of the text input on the All tab.
    private void RebuildComposeRow()
    {
        if (_inputRow == null) return;
        if (_composeDropdownObj != null) UnityEngine.Object.Destroy(_composeDropdownObj);
        _composeDropdownObj = null;
        _composeDropdown = null;
        EnsureComposeTargets();
        if (_composeTargets.Count == 0) return;

        var options = new string[_composeTargets.Count];
        for (int i = 0; i < _composeTargets.Count; i++) options[i] = _composeTargets[i].Label;
        int sel = Mathf.Clamp(_composeIndex, 0, options.Length - 1);
        var ddObj = UIFactory.CreateDropdown(_inputRow, "ComposeDropdown", out _composeDropdown,
            options[sel], 11, OnComposeChanged, options);
        // 0.17.3: wider so a whisper target "@LongName" fits, and never word-wrap the
        // caption or list items (long names used to wrap inside the cell and look odd).
        UIFactory.SetLayoutElement(ddObj,
            minWidth: 80, preferredWidth: 116, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        ApplyDropdownNoWrap(_composeDropdown);
        ddObj.transform.SetSiblingIndex(0); // leftmost — before the input field + Send
        _composeDropdownObj = ddObj;
        _composeDropdown.SetValueWithoutNotify(sel);
        // Outside-click close (TMP's own blocker doesn't fire in our canvas).
        Raphael.UI.Forms.FormDropdownRegistry.Register(_composeDropdown);
        bool onWhispers = WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex;
        TooltipHover.Attach(ddObj, onWhispers
            ? "Whisper recipient — who your message sends to. Pick anyone here (or press Tab while typing to cycle) WITHOUT changing the view, so you can stay on All Whispers and still reply to a chosen person. Start a new conversation with the name field above, or /whisper Name."
            : "Channel this message sends to. Click to pick, or press Tab while typing to cycle (Global / Local / Clan / active whispers).");
    }

    // Apply a compose-dropdown selection. On the Whispers tab the box drives the active conversation (and
    // therefore who your message goes to), mirroring the top sub-tabs; on the All tab it just sets the index.
    private void OnComposeChanged(int i)
    {
        if (i < 0 || i >= _composeTargets.Count) return;
        _composeIndex = i;
        if (WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex)
        {
            // The box drives only the SEND target — it deliberately leaves the VIEW (_activeWhisperPartner)
            // alone, so you can stay on "All Whispers" while picking who to message.
            var t = _composeTargets[i];
            if (t.IsWhisper && t.Whisper != null) _whisperSendTo = t.Whisper;
        }
    }

    // 0.17.3: keep a dropdown's caption + list items on ONE line (ellipsis), so a long
    // whisper target / player name doesn't word-wrap inside the cell and look odd.
    private static void ApplyDropdownNoWrap(TMP_Dropdown dd)
    {
        try
        {
            if (dd == null) return;
            if (dd.captionText != null) { dd.captionText.enableWordWrapping = false; dd.captionText.overflowMode = TextOverflowModes.Ellipsis; }
            if (dd.itemText != null)    { dd.itemText.enableWordWrapping    = false; dd.itemText.overflowMode    = TextOverflowModes.Ellipsis; }
        }
        catch { }
    }

    // Tab cycles the compose target (Global → Local → [Clan] → whisper partners → …).
    // Does NOT re-resolve the target list here — that's done when the row is built —
    // so a mid-cycle rebuild can't reset the index and eat a press.
    private void CycleCompose(int dir)
    {
        int n = _composeTargets.Count;
        if (n == 0) { EnsureComposeTargets(); n = _composeTargets.Count; if (n == 0) return; }
        _composeIndex = ((_composeIndex + dir) % n + n) % n;
        if (_composeDropdown != null) _composeDropdown.SetValueWithoutNotify(_composeIndex);
    }

    // Per-frame: on the All tab while typing, Tab cycles the send target. Uses a
    // short focus grace so the one-frame focus blip around a Tab press doesn't make
    // every other press a no-op.
    private void TickComposeKeys()
    {
        try
        {
            // B4: run on the All tab AND the Whispers tab (the compose box exists on both now).
            bool onWhispers = WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex;
            if (!Enabled || (_activeTab != 0 && !onWhispers)) { _inputFocusGrace = 0; return; }
            if (IsInputFocused()) _inputFocusGrace = 6;
            else if (_inputFocusGrace > 0) _inputFocusGrace--;
            if (_inputFocusGrace > 0 && UnityEngine.Input.GetKeyDown(KeyCode.Tab))
            {
                CycleCompose(+1);
                // On the Whispers tab, Tab cycles the SEND target (the box), NOT the view; a programmatic
                // cycle doesn't fire OnComposeChanged, so sync _whisperSendTo here.
                if (onWhispers && _composeIndex >= 0 && _composeIndex < _composeTargets.Count)
                {
                    var t = _composeTargets[_composeIndex];
                    if (t.IsWhisper && t.Whisper != null) _whisperSendTo = t.Whisper;
                }
            }

            // 0.17.3: re-evaluate the available compose targets a few times a minute so
            // the All-tab "Send to:" dropdown reflects clan membership that resolved AFTER
            // login (the clan entity replicates a few seconds late — the reported "Clan
            // option missing until I re-log") and mid-game clan join/leave. Cheap:
            // EnsureComposeTargets guards on a signature, so this only rebuilds the dropdown
            // when the set actually changes.
            if (UnityEngine.Time.unscaledTime >= _nextComposeRefresh)
            {
                _nextComposeRefresh = UnityEngine.Time.unscaledTime + 2f;
                UpdateComposeRow();
            }
        }
        catch { }
    }

    // 0.17.3: double-click a player's name in the chat log to start a whisper to them.
    // Polls left-clicks while the pointer is over the log; if the click lands on a sender
    // <link> and a second click on the SAME name lands within the double-click window,
    // open a whisper to them. Single clicks (and scroll drags) are unaffected.
    private void TickNameDoubleClick()
    {
        try
        {
            if (!Enabled || _log == null || !Settings.ChatDoubleClickNameWhisper) return;
            if (!UnityEngine.Input.GetMouseButtonDown(0)) return;
            var rt = _log.rectTransform;
            var mp = UnityEngine.Input.mousePosition;
            if (rt == null || !UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(rt, mp, null)) return;
            int li = TMPro.TMP_TextUtilities.FindIntersectingLink(_log, mp, null);
            if (li < 0) return;
            string id = _log.textInfo.linkInfo[li].GetLinkID();
            if (string.IsNullOrEmpty(id)) return;
            float now = UnityEngine.Time.unscaledTime;
            if (id == _lastNameClickId && (now - _lastNameClickTime) <= 0.40f)
            {
                _lastNameClickId = null; _lastNameClickTime = 0f; // consume
                StartWhisperFromClickedName(id);
            }
            else { _lastNameClickId = id; _lastNameClickTime = now; }
        }
        catch { }
    }

    // 0.50 r8/r11: OPT-IN keyboard scrolling for the chat history (the scrollbar becomes a tiny sliver once the
    // buffer fills). Up = toward older (top), Down = toward newer (bottom). Gated by TWO settings so the keys
    // can't steal gameplay binds: PageUp/PageDown (ChatScrollPageKeys, default ON) and Up/Down arrows
    // (ChatScrollArrowKeys, default OFF — they commonly clash with movement). Arrows are also ignored while the
    // message box is focused unless the pointer is over the log. No-ops when closed or both settings off.
    private void TickScrollKeys()
    {
        try
        {
            if (!Enabled || _scrollRect == null) return;
            bool pageKeys = Config.Settings.ChatScrollPageKeys;
            bool arrowKeys = Config.Settings.ChatScrollArrowKeys;
            if (!pageKeys && !arrowKeys) return;

            bool pageUp = pageKeys && UnityEngine.Input.GetKeyDown(KeyCode.PageUp);
            bool pageDown = pageKeys && UnityEngine.Input.GetKeyDown(KeyCode.PageDown);

            bool arrowsAllowed = false;
            if (arrowKeys)
            {
                // Only steal the arrows from the caret when the pointer is hovering the log area.
                if (IsInputFocused())
                {
                    var lrt = _log != null ? _log.rectTransform : null;
                    arrowsAllowed = lrt != null &&
                        UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(lrt, UnityEngine.Input.mousePosition, null);
                }
                else arrowsAllowed = true;
            }

            float dir = 0f;     // +1 = older (up), -1 = newer (down)
            bool page = false;
            if (pageUp) { dir = +1f; page = true; }
            else if (pageDown) { dir = -1f; page = true; }
            else if (arrowsAllowed)
            {
                if (UnityEngine.Input.GetKey(KeyCode.UpArrow)) dir = +1f;
                else if (UnityEngine.Input.GetKey(KeyCode.DownArrow)) dir = -1f;
            }
            if (dir == 0f) return;

            float content = _scrollContentRect != null ? _scrollContentRect.rect.height : 0f;
            float view = _scrollRect.viewport != null ? _scrollRect.viewport.rect.height : 0f;
            float scrollable = Mathf.Max(1f, content - view);
            float deltaPx = page ? Mathf.Max(40f, view * 0.9f) : 700f * UnityEngine.Time.unscaledDeltaTime;
            float deltaNorm = deltaPx / scrollable;
            _scrollRect.verticalNormalizedPosition =
                Mathf.Clamp01(_scrollRect.verticalNormalizedPosition + dir * deltaNorm);
        }
        catch { }
    }

    // 0.50 r11: one click of a scroll-arrow button — nudge the history a few lines. dir +1 = up (older), -1 = down.
    private void ScrollByStep(float dir)
    {
        if (_scrollRect == null) return;
        float content = _scrollContentRect != null ? _scrollContentRect.rect.height : 0f;
        float view = _scrollRect.viewport != null ? _scrollRect.viewport.rect.height : 0f;
        float scrollable = Mathf.Max(1f, content - view);
        const float stepPx = 52f;   // ~3 lines per click
        _scrollRect.verticalNormalizedPosition =
            Mathf.Clamp01(_scrollRect.verticalNormalizedPosition + dir * (stepPx / scrollable));
    }

    // Resolve a double-clicked name to a whisper target and open the conversation (All
    // tab, that whisper selected as the compose target, input focused). If the name
    // can't be resolved (e.g. they're offline now), report who IS whisperable.
    private void StartWhisperFromClickedName(string plainName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(plainName)) return;
            if (ResolvePlayer(plainName, out var name, out var id))
            {
                BeginWhisperTo(name, id);
                FocusInput();
            }
            else ReportWhisperResolveFailure(plainName);
        }
        catch (System.Exception ex) { Utils.LogUtils.LogError($"StartWhisperFromClickedName: {ex}"); }
    }

    private static bool IsInClan()
    {
        try
        {
            var u = MessageService.LocalUser;
            if (u == Entity.Null) return false;
            // User.ClanEntity is a ProjectM.NetworkedEntity; its ._Entity resolves to
            // Entity.Null when the player isn't in a clan. (If a clan member's clan
            // isn't resolved client-side they just won't see Clan in the All-tab
            // cycle — the dedicated Clan tab still sends clan messages either way.)
            return u.Read<ProjectM.Network.User>().ClanEntity._Entity != Entity.Null;
        }
        catch { return false; }
    }

    private void Render()
    {
        if (_log == null) return;
        _renderDirty = false;
        bool showTime = Settings.ChatShowTimestamps;
        bool showTag  = Settings.ChatShowChannelTags;
        bool newestAtBottom = Settings.ChatNewestAtBottom;
        bool tabular  = Settings.ChatTabularLayout;
        var filter = TabDefs[_activeTab].Filter;
        // On the Whispers tab, an active partner sub-tab narrows to that conversation.
        bool whisperPartnerFilter = WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex && _activeWhisperPartner != null;

        // Build the matching lines in chronological (oldest→newest) order first,
        // then emit in the user's chosen direction.
        var lines = new List<string>();
        var line = new StringBuilder(160);
        bool Visible(ChatRelayService.ChatLine ln)
            => (!filter.HasValue || ln.Channel == filter.Value)
               && (filter.HasValue || AllTabIncludes(ln.Channel))
               && !(!filter.HasValue && Settings.AllTabExcludeNotesToSelf && ChatRelayService.IsNoteToSelf(ln))
               && (!whisperPartnerFilter || ln.Partner == _activeWhisperPartner);
        // 0.65.6 PERF: only format the newest lines that could possibly be drawn (MaxRenderedLines),
        // not the whole 2000-line retention buffer. Pre-0.65.6 every incoming message re-formatted
        // the ENTIRE buffer, so per-message cost climbed all session until the buffer filled —
        // the "chat gets laggy over time, relog fixes it" report.
        var buf = NewestVisible(ChatRelayService.Buffer, Visible);
        // 0.17.3: pre-compute tabular column geometry once per render. Fixed-pixel
        // columns (auto-fit) give all extra width to the message column; otherwise the
        // legacy %-of-width columns scale everything with the window.
        var cols = tabular ? ComputeTabCols(buf, Visible, showTime, showTag) : default;
        for (int i = 0; i < buf.Count; i++)
        {
            var ln = buf[i];
            line.Clear();
            if (tabular)
            {
                AppendTabularLine(line, ln, showTime, showTag, Settings.ChatTabularSeparateChannelName, cols);
            }
            else
            {
                if (showTime) line.Append("<color=#808080>").Append(ln.Received.ToString("HH:mm")).Append("</color> ");
                if (showTag)  line.Append(WhisperAwareTag(ln)).Append(' ');
                // Game-resolved sender name (empty for system messages). Wrapped in a
                // click-to-whisper link when that feature is on; the native userName may
                // already carry color tags — kept as the visible link text. For a whisper
                // YOU sent, this slot can show the recipient / "Note to self" (B3/B5).
                if (!string.IsNullOrEmpty(ln.Sender))
                {
                    AppendWhisperAwareSender(line, ln);
                    line.Append(": ");
                }
                line.Append(BodyText(ln));
            }
            lines.Add(line.ToString());
        }

        // 0.65.0 BLANK-CHAT FIX: a single TextMeshPro object cannot exceed Unity's 16-bit
        // mesh index buffer — 65,535 vertices ≈ 16,383 visible glyphs (4 verts/glyph). Feeding
        // the whole retention buffer (up to ChatRelayService.MaxLines = 2000 lines) into one
        // _log component eventually crosses that ceiling; TMP then fails to build the mesh and
        // renders the box BLANK while the buffer stays intact — which is exactly why Copy still
        // works and only a relog (which clears the buffer) restored it. The "All" tab hits it
        // first because it aggregates every channel. 0.65.1: bound by a VISIBLE-GLYPH budget
        // (not a line count) so long lines can't sneak the mesh over the ceiling. The 2000-line
        // buffer is untouched so scrollback and Copy keep the full history.
        int start = ComputeRenderStart(lines);
        var sb = new StringBuilder(2048);
        if (newestAtBottom)
            for (int i = start; i < lines.Count; i++) sb.Append(lines[i]).Append('\n');
        else
            for (int i = lines.Count - 1; i >= start; i--) sb.Append(lines[i]).Append('\n');
        _log.text = sb.ToString();

        // 0.17.0: keep the newest message in view as lines arrive (bottom or top
        // per the setting). Off lets the user scroll back through history freely.
        if (Settings.ChatAutoScroll) ScrollToNewest(newestAtBottom);
    }

    /// <summary>
    /// 0.65.6: the newest (up to <see cref="MaxRenderedLines"/>) lines passing <paramref name="visible"/>,
    /// oldest→newest. ComputeRenderStart can never keep more than that, so formatting anything older
    /// was wasted work that grew with the retention buffer. Shared by both chat panels.
    /// </summary>
    internal static List<ChatRelayService.ChatLine> NewestVisible(
        IReadOnlyList<ChatRelayService.ChatLine> buf, System.Func<ChatRelayService.ChatLine, bool> visible)
    {
        var picked = new List<ChatRelayService.ChatLine>(System.Math.Min(buf.Count, MaxRenderedLines));
        for (int i = buf.Count - 1; i >= 0 && picked.Count < MaxRenderedLines; i--)
            if (visible(buf[i])) picked.Add(buf[i]);
        picked.Reverse();
        return picked;
    }

    /// <summary>
    /// 0.65.1: pick the earliest line index whose NEWEST-first run of rendered lines stays
    /// under <see cref="MaxRenderedGlyphs"/> visible glyphs (and <see cref="MaxRenderedLines"/>
    /// lines). Keeps the mesh well under TextMeshPro's ~16k-glyph ceiling so the box can't blank
    /// on a busy channel. Shared by both chat panels so their caps stay identical.
    /// Always returns at least the single newest line, even if that line alone is over budget.
    /// </summary>
    internal static int ComputeRenderStart(System.Collections.Generic.List<string> lines)
    {
        int used = 0;
        int start = lines.Count;                 // nothing kept yet
        for (int j = lines.Count - 1; j >= 0; j--)
        {
            int g = VisibleGlyphCount(lines[j]) + 1; // +1 for the joining newline
            // Stop once we'd exceed the glyph budget or the secondary line guard — but only
            // after we've committed at least the newest line.
            if (start != lines.Count &&
                (used + g > MaxRenderedGlyphs || (lines.Count - j) > MaxRenderedLines))
                break;
            used += g;
            start = j;
        }
        return start == lines.Count ? 0 : start;
    }

    /// <summary>
    /// Count visible glyphs, ignoring well-formed TMP rich-text tags, which draw no vertices.
    /// Anything that isn't clearly a tag is counted as visible, so the estimate errs high
    /// (trims a little history) rather than low (could overflow the mesh).
    /// </summary>
    internal static int VisibleGlyphCount(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        // 0.65.6: only skip a KNOWN TMP tag — '<' + a letter, '/' or '#', closed by '>' within MaxTagChars with no
        // '<' inside, whose name is in ZeroGlyphTags (sprite/quad cost 1). Pre-0.65.6 any '<' hid everything up to the next '>' (or the
        // rest of the line), so player text like "<3 <3 …" counted as ~0 glyphs and a flood of it could push the
        // mesh past TMP's ceiling and blank the chat again. Over-counting only trims history; never overflows.
        const int MaxTagChars = 64;
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '<' && i + 1 < s.Length && (char.IsLetter(s[i + 1]) || s[i + 1] == '/' || s[i + 1] == '#'))
            {
                int close = -1;
                for (int j = i + 1; j < s.Length && j - i <= MaxTagChars; j++)
                {
                    if (s[j] == '<') break;
                    if (s[j] == '>') { close = j; break; }
                }
                if (close > 0)
                {
                    int cost = TagGlyphCost(s, i + 1, close);
                    if (cost >= 0) { n += cost; i = close; continue; } // known tag: 0 (formatting) or 1 (sprite/quad)
                }
            }
            n++;
        }
        return n;
    }

    // Known TMP tags that draw no geometry. Anything NOT listed is counted as literal text (TMP prints unknown
    // tags verbatim), so the estimate only ever errs high.
    private static readonly System.Collections.Generic.HashSet<string> ZeroGlyphTags =
        new(System.StringComparer.OrdinalIgnoreCase)
    {
        "b", "i", "u", "s", "sub", "sup", "color", "size", "font", "mark", "cspace", "mspace", "style", "gradient",
        "rotate", "voffset", "indent", "align", "nobr", "noparse", "link", "lowercase", "uppercase", "smallcaps",
        "allcaps", "alpha", "pos", "space", "br", "nbsp", "zwsp", "line-height", "line-indent", "width", "margin",
        "margin-left", "margin-right", "page", "font-weight", "strikethrough", "underline",
    };

    // Glyph cost of the tag s[from..closeIdx) (text between '<' and '>'): 0 for a known formatting tag,
    // 1 for sprite/quad (they draw a quad), -1 when unknown (caller counts it literally).
    private static int TagGlyphCost(string s, int from, int closeIdx)
    {
        if (s[from] == '/') from++;
        if (from >= closeIdx) return -1;
        if (s[from] == '#') return 0; // <#RRGGBB> colour shorthand
        int end = from;
        while (end < closeIdx && s[end] != '=' && s[end] != ' ') end++;
        var name = s.Substring(from, end - from);
        if (name.Equals("sprite", System.StringComparison.OrdinalIgnoreCase)
            || name.Equals("quad", System.StringComparison.OrdinalIgnoreCase)) return 1;
        return ZeroGlyphTags.Contains(name) ? 0 : -1;
    }

    // 0.17.3: build one chat line in aligned columns using TMP <pos>/<indent>:
    //   [time]      [tag] Sender:      message (wrapped lines hang-indent under the
    //   message column). Columns are % of the log width so they scale with the window.
    // If a sender name is long enough to pass the message column, <pos> can't move
    // backwards so the text just flows inline — a graceful fallback, not a break.
    // <indent> is closed at the end so the next line (joined by \n in the same TMP
    // text block) doesn't inherit it.
    // Tabular column geometry for one render. When UsePx, the offsets are fixed pixels
    // (time/channel/name columns are snug and the message column gets all remaining
    // width — widening the window grows the message first). Otherwise the legacy
    // %-of-width offsets are used (every column scales with the window).
    private readonly struct TabCols
    {
        public readonly bool UsePx;
        public readonly int ChanStart;  // separate: channel col; combined: meta (tag+name) start
        public readonly int NameStart;  // separate: name col
        public readonly int MsgStart;   // message col (both modes)
        public TabCols(bool usePx, int chanStart, int nameStart, int msgStart)
        { UsePx = usePx; ChanStart = chanStart; NameStart = nameStart; MsgStart = msgStart; }
    }

    // Compute fixed-pixel tabular columns: snug time/channel/name, message absorbs the
    // rest. The name column auto-fits the longest VISIBLE sender so there's no dead gap.
    // Pixels scale with the log's font size so columns track the chat text-size setting.
    private TabCols ComputeTabCols(System.Collections.Generic.IReadOnlyList<ChatRelayService.ChatLine> buf,
                                   System.Func<ChatRelayService.ChatLine, bool> visible, bool showTime, bool showTag)
    {
        float fs = (_log != null && _log.fontSize > 0.1f) ? _log.fontSize : 14f;

        // Name-column width: auto-fit (flex) to the longest VISIBLE sender, or a fixed
        // ~10-char width when auto-fit is off (locked/static). Either way the columns are
        // fixed pixels and the MESSAGE column absorbs all remaining width — so widening
        // the window grows the message first, never every column proportionally.
        float nameChars = 10f;
        // Channel column: standard label width ("[W]" / "[Whisper]"), grown to fit a whisper recipient when
        // it's shown in the channel slot ("[→Name]"). Only auto-fit mode varies it; locked mode stays fixed.
        float chanUnits = Settings.ChatChannelLabelsSpelledOut ? 6.2f : 3.4f;
        if (Settings.ChatTabularAutoFitColumns)
        {
            int maxName = 0, maxChan = 0;
            for (int i = 0; i < buf.Count; i++)
            {
                var ln = buf[i];
                if (!visible(ln)) continue;
                // Measure the WHISPER-AWARE slots — "Note to self" / "→ Recipient" / "[→Name]" can be wider
                // than the raw sender/tag, so the columns fit what's actually drawn and don't overlap (B3/B5).
                if (!string.IsNullOrEmpty(ln.Sender))
                {
                    int n = WhisperAwareSenderPlain(ln).Length;
                    if (n > maxName) maxName = n;
                }
                if (showTag)
                {
                    int c = WhisperAwareTagPlain(ln).Length;
                    if (c > maxChan) maxChan = c;
                }
            }
            nameChars = Mathf.Clamp(maxName, 4, 18);
            // ~0.6 fs-units/char for the channel slot; never below the standard label width, capped so a long
            // recipient name can't swallow the whole row.
            chanUnits = Mathf.Clamp(Mathf.Max(chanUnits, maxChan * 0.6f), chanUnits, 12f);
        }

        float gap   = 0.8f * fs;
        float timeW = showTime ? 3.8f * fs : 0f;
        float chanW = showTag ? chanUnits * fs : 0f;
        float nameW = nameChars * 0.55f * fs;

        int chanStart = (int)(timeW + (timeW > 0f ? gap : 0f));
        bool separate = Settings.ChatTabularSeparateChannelName && showTag;
        int nameStart = (int)(chanStart + chanW + gap);
        int msgStart  = separate
            ? (int)(nameStart + nameW + gap)
            : (int)(chanStart + chanW + nameW + gap); // combined: tag + name share before message
        return new TabCols(true, chanStart, nameStart, msgStart);
    }

    // 0.21: the message BODY, optionally tinted. Precedence: your OWN messages (if "highlight my own
    // messages" is on) use the own-color so they stand out on every tab; else, if "color text by channel"
    // is on, the body takes the channel color; else it's left as-is. The &lt;color&gt; wrapper is TMP-stack
    // safe even if the source text carries its own tags.
    internal static string BodyText(ChatRelayService.ChatLine ln)
    {
        // 0.65.3: neutralize any rich-text tag the message leaves OPEN before it joins the shared
        // TMP block — otherwise an unclosed <i>/<b>/<color>/… bleeds into every line after it (the
        // "chat randomly turns italic and won't stop" report). See CloseDanglingTags.
        string body = CloseDanglingTags(ln.Text);
        string hex = null;
        if (Settings.ChatColorOwnMessages && ChatRelayService.IsOwnSender(ln.Sender))
            hex = Settings.ChatOwnMessageColorHex;
        else if (Settings.ChatColorMessageByChannel)
            hex = ChannelColorHex(ln.Channel);
        return string.IsNullOrEmpty(hex) ? body : $"<color={hex}>{body}</color>";
    }

    // Scope/style rich-text tags that persist until explicitly closed. If a message opens one of
    // these and never closes it, the open state carries forward through the '\n'-joined TMP text
    // into every following line. (Positional / self-contained tags like <pos>, <space>, <br>, <nbsp>
    // don't leak, so they're deliberately absent.)
    private static readonly System.Collections.Generic.HashSet<string> ScopedRichTags =
        new(System.StringComparer.OrdinalIgnoreCase)
    {
        "b", "i", "u", "s", "sub", "sup", "color", "size", "font", "mark",
        "cspace", "mspace", "style", "gradient", "rotate", "voffset", "indent",
        "align", "nobr", "noparse", "link", "lowercase", "uppercase", "smallcaps", "allcaps",
    };

    // 0.65.3 ITALICS-BLEED FIX. Every visible chat line is concatenated into ONE TextMeshPro
    // component (joined by '\n'), so a style/scope tag a SERVER or other-mod message leaves OPEN
    // — e.g. "<i>" with no matching "</i>", or an unclosed <b>/<color>/<size> — stays active and
    // bleeds into EVERY line rendered after it (Howl: two back-to-back system messages, one with
    // a dangling <i>, turned the whole All tab italic with no way to stop it). We can't trust
    // arbitrary inbound text to balance its own markup, so append the closers for whatever the
    // message leaves open, making each line self-contained. Markup the text DID close is untouched,
    // so legitimate in-message formatting still renders.
    internal static string CloseDanglingTags(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return text;
        var open = new System.Collections.Generic.List<string>(); // open scoped-tag names, in order
        int i = 0, n = text.Length;
        while (i < n)
        {
            int lt = text.IndexOf('<', i);
            if (lt < 0) break;
            int gt = text.IndexOf('>', lt + 1);
            if (gt < 0) break;                 // a bare '<' with no '>' isn't a tag — stop scanning
            string inner = text.Substring(lt + 1, gt - lt - 1);
            i = gt + 1;
            if (inner.Length == 0) continue;
            bool closing = inner[0] == '/';
            string bodyTag = closing ? inner.Substring(1) : inner;
            int cut = bodyTag.Length;          // tag name ends at the first '=' or ' '
            for (int k = 0; k < bodyTag.Length; k++) { char c = bodyTag[k]; if (c == '=' || c == ' ') { cut = k; break; } }
            string name = bodyTag.Substring(0, cut).Trim();
            if (name.Length == 0 || !ScopedRichTags.Contains(name)) continue;
            if (closing)
            {
                // Pop the nearest matching open tag (lenient about interleaving/over-closing).
                for (int k = open.Count - 1; k >= 0; k--)
                    if (string.Equals(open[k], name, System.StringComparison.OrdinalIgnoreCase)) { open.RemoveAt(k); break; }
            }
            else open.Add(name);
        }
        if (open.Count == 0) return text;
        var sb = new StringBuilder(text.Length + open.Count * 6);
        sb.Append(text);
        for (int k = open.Count - 1; k >= 0; k--) sb.Append("</").Append(open[k]).Append('>');
        return sb.ToString();
    }

    private static void AppendTabularLine(StringBuilder line, ChatRelayService.ChatLine ln, bool showTime, bool showTag, bool separate, TabCols cols)
    {
        bool hasSender = !string.IsNullOrEmpty(ln.Sender);

        // ---- Fixed-pixel columns (message column gets all remaining width) ----
        if (cols.UsePx)
        {
            if (showTime)
                line.Append("<color=#808080>").Append(ln.Received.ToString("HH:mm")).Append("</color>");
            if (separate && showTag)
            {
                line.Append("<pos=").Append(cols.ChanStart).Append('>');
                var tag = WhisperAwareTag(ln);
                if (!string.IsNullOrEmpty(tag)) line.Append(tag);
                line.Append("<pos=").Append(cols.NameStart).Append('>');
                if (hasSender) AppendWhisperAwareSender(line, ln);
            }
            else
            {
                line.Append("<pos=").Append(cols.ChanStart).Append('>');
                if (showTag)
                {
                    var tag = WhisperAwareTag(ln);
                    if (!string.IsNullOrEmpty(tag)) line.Append(tag).Append(' ');
                }
                if (hasSender) { AppendWhisperAwareSender(line, ln); line.Append(':'); }
            }
            line.Append("<indent=").Append(cols.MsgStart).Append("><pos=").Append(cols.MsgStart).Append('>')
                .Append(BodyText(ln)).Append("</indent>");
            return;
        }

        // ---- Legacy %-of-width columns (everything scales with the window) ----
        // Separate channel + name into their OWN columns: [time] [channel] [name] message.
        if (separate && showTag)
        {
            int chanPct = showTime ? 11 : 0;
            int namePct = chanPct + 10;
            int msgPct  = showTime ? 42 : 31;
            if (showTime)
                line.Append("<color=#808080>").Append(ln.Received.ToString("HH:mm")).Append("</color>");
            line.Append("<pos=").Append(chanPct).Append("%>");
            var tag = WhisperAwareTag(ln);
            if (!string.IsNullOrEmpty(tag)) line.Append(tag);
            line.Append("<pos=").Append(namePct).Append("%>");
            if (hasSender) AppendWhisperAwareSender(line, ln);
            line.Append("<indent=").Append(msgPct).Append("%><pos=").Append(msgPct).Append("%>")
                .Append(BodyText(ln)).Append("</indent>");
            return;
        }

        // Combined: [time]   [channel] name:   message  (the original layout).
        int metaPct  = showTime ? 12 : 0;
        int msgPctC  = showTime ? 34 : 24;
        if (showTime)
            line.Append("<color=#808080>").Append(ln.Received.ToString("HH:mm")).Append("</color>");
        if (metaPct > 0) line.Append("<pos=").Append(metaPct).Append("%>");
        if (showTag)
        {
            var tag = WhisperAwareTag(ln);
            if (!string.IsNullOrEmpty(tag)) line.Append(tag).Append(' ');
        }
        if (hasSender) { AppendWhisperAwareSender(line, ln); line.Append(':'); }
        line.Append("<indent=").Append(msgPctC).Append("%><pos=").Append(msgPctC).Append("%>")
            .Append(BodyText(ln)).Append("</indent>");
    }

    // 0.17.3: append a sender name, wrapped in a TMP <link> when click-to-whisper is on
    // so a double-click on it can be detected (TickNameDoubleClick). The link ID is the
    // PLAIN name (rich-text stripped, quotes removed) so it resolves against the roster /
    // chat-seen players; the visible link text keeps any color tags the game applied.
    private static void AppendSenderLinked(StringBuilder line, string sender)
    {
        if (string.IsNullOrEmpty(sender)) return;
        // 0.65.3: close any tag the game-resolved name leaves open too, so a name (or its color tag)
        // can't bleed formatting into the rest of the line / following lines. See CloseDanglingTags.
        if (!Settings.ChatDoubleClickNameWhisper) { line.Append(CloseDanglingTags(sender)); return; }
        string plain = StripRichTags(sender).Replace("\"", string.Empty).Trim();
        if (string.IsNullOrEmpty(plain)) { line.Append(CloseDanglingTags(sender)); return; }
        // Style clickable names like a hyperlink — underlined + "link blue" — so it's
        // obvious a double-click whispers them. We render the PLAIN name (the game's own
        // color is dropped here on purpose) so our color actually takes effect rather
        // than being overridden by a nested color tag. Link ID is the plain name.
        line.Append("<link=\"").Append(plain).Append("\"><u><color=").Append(WhisperLinkColorHex).Append(">")
            .Append(plain).Append("</color></u></link>");
    }

    // "Link blue" for clickable (double-click-to-whisper) names in the chat log.
    private const string WhisperLinkColorHex = "#7FB6FF";

    // Strip TMP rich-text tags (<...>) to recover a plain name for matching/link IDs.
    private static string StripRichTags(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s;
        var sb = new StringBuilder(s.Length);
        bool inTag = false;
        foreach (char c in s)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }

    // Snap the scroll view to the newest message's edge. ScrollRect convention:
    // verticalNormalizedPosition 1 = top, 0 = bottom (independent of pivot). A
    // forced layout rebuild first is load-bearing — the ContentSizeFitter hasn't
    // recomputed the new text's height yet when we set the position, so without it
    // the scroll lands on the PREVIOUS content size and the newest line is clipped.
    private void ScrollToNewest(bool newestAtBottom)
    {
        if (_scrollRect == null) return;
        try
        {
            UnityEngine.Canvas.ForceUpdateCanvases();
            if (_scrollContentRect != null)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContentRect);
            _scrollRect.verticalNormalizedPosition = newestAtBottom ? 0f : 1f;
        }
        catch { /* scroll is best-effort; never throw into the inbound pump */ }
    }

    // 0.17.0: single source of truth for per-channel color, shared by the inline
    // label tags AND the colored tab labels. Global is user-configurable; the rest
    // are fixed, distinct defaults (Local blue / Clan green / System gold / Whisper pink).
    // Per-channel colors — all five are user-settable (Settings → Game UI chat colors), persisted, and
    // drive BOTH the message-text label tag (ChannelTag) AND the colored tab (ChannelColor). 0.21.
    internal static string ChannelColorHex(ChatRelayService.Channel? ch) => ch switch
    {
        ChatRelayService.Channel.Global  => Settings.ChatGlobalColorHex,
        ChatRelayService.Channel.Local   => Settings.ChatLocalColorHex,
        ChatRelayService.Channel.Clan    => Settings.ChatClanColorHex,
        ChatRelayService.Channel.System  => Settings.ChatSystemColorHex,
        ChatRelayService.Channel.Whisper => Settings.ChatWhisperColorHex,
        _                                => "#FFFFFF",
    };

    private static Color ChannelColor(ChatRelayService.Channel? ch) =>
        UnityEngine.ColorUtility.TryParseHtmlString(ChannelColorHex(ch), out var c) ? c : Theme.DefaultText;

    internal static string ChannelTag(ChatRelayService.Channel ch)
    {
        string label = Settings.ChatChannelLabelsSpelledOut ? SpelledLabel(ch) : ShortLabel(ch);
        return string.IsNullOrEmpty(label) ? string.Empty : $"<color={ChannelColorHex(ch)}>{label}</color>";
    }

    // B3/B5 (0.29.8): a whisper YOU sent stamps Sender = your own name and Partner = the recipient (see
    // ChatRelayService.CaptureLocalEcho), so the line otherwise shows your name with no hint of who you sent
    // to. These helpers let a SENT whisper surface the recipient (or "Note to self") per the user's settings.
    // Received whispers are untouched — their sender already names the other person.
    private static bool IsSentWhisper(ChatRelayService.ChatLine ln)
        => ln.Channel == ChatRelayService.Channel.Whisper
           && !string.IsNullOrEmpty(ln.Sender) && ChatRelayService.IsOwnSender(ln.Sender);

    private static bool IsSelfWhisper(ChatRelayService.ChatLine ln)
        => IsSentWhisper(ln) && !string.IsNullOrEmpty(ln.Partner) && ChatRelayService.IsOwnSender(ln.Partner);

    // True when this line's channel slot should show the whisper RECIPIENT in place of the channel tag.
    private static bool WhisperRecipientInChannel(ChatRelayService.ChatLine ln)
        => IsSentWhisper(ln) && !string.IsNullOrEmpty(ln.Partner)
           && !(IsSelfWhisper(ln) && Settings.ChatSelfWhisperAsNoteToSelf)
           && Settings.ChatWhisperRecipientMode == Settings.WhisperRecipientDisplay.Channel;

    // Channel tag, augmented for sent whispers in channel-column mode. Per tester feedback, the recipient
    // REPLACES the "Whisper" label ("[→Name]") rather than appending to it ("[Whisper → Name]") — the latter
    // grew long enough to overlap the next column.
    private static string WhisperAwareTag(ChatRelayService.ChatLine ln)
    {
        if (WhisperRecipientInChannel(ln))
            return $"<color={ChannelColorHex(ChatRelayService.Channel.Whisper)}>[→{StripRichTags(ln.Partner).Trim()}]</color>";
        return ChannelTag(ln.Channel);
    }

    // Plain (rich-text-stripped) text the channel slot will render — used by ComputeTabCols so the column
    // auto-sizes to whisper-aware content (a recipient name shown in place of "[Whisper]").
    private static string WhisperAwareTagPlain(ChatRelayService.ChatLine ln)
        => WhisperRecipientInChannel(ln)
            ? "[→" + StripRichTags(ln.Partner).Trim() + "]"
            : StripRichTags(ChannelTag(ln.Channel));

    // Sender slot, whisper-aware: "Note to self" for a self-whisper; "→ Recipient" when the user chose the
    // name-column style for sent whispers; otherwise the normal (linked) sender name.
    private static void AppendWhisperAwareSender(StringBuilder line, ChatRelayService.ChatLine ln)
    {
        if (IsSelfWhisper(ln) && Settings.ChatSelfWhisperAsNoteToSelf)
        {
            line.Append("<color=").Append(ChannelColorHex(ChatRelayService.Channel.Whisper)).Append("><i>Note to self</i></color>");
            return;
        }
        if (IsSentWhisper(ln) && !string.IsNullOrEmpty(ln.Partner)
            && Settings.ChatWhisperRecipientMode == Settings.WhisperRecipientDisplay.Sender)
        {
            line.Append("→ ");
            AppendSenderLinked(line, ln.Partner);   // recipient — still a click-to-whisper link
            return;
        }
        AppendSenderLinked(line, ln.Sender);
    }

    // Plain (rich-text-stripped) text the NAME slot will render — used by ComputeTabCols so the name column
    // (and therefore the message column after it) fits "Note to self" / "→ Recipient", not just the raw sender.
    private static string WhisperAwareSenderPlain(ChatRelayService.ChatLine ln)
    {
        if (IsSelfWhisper(ln) && Settings.ChatSelfWhisperAsNoteToSelf) return "Note to self";
        if (IsSentWhisper(ln) && !string.IsNullOrEmpty(ln.Partner)
            && Settings.ChatWhisperRecipientMode == Settings.WhisperRecipientDisplay.Sender)
            return "→ " + StripRichTags(ln.Partner).Trim();
        return StripRichTags(ln.Sender ?? string.Empty).Trim();
    }

    private static string ShortLabel(ChatRelayService.Channel ch) => ch switch
    {
        ChatRelayService.Channel.Global  => "[G]",
        ChatRelayService.Channel.Local   => "[L]",
        ChatRelayService.Channel.Clan    => "[Clan]",
        ChatRelayService.Channel.System  => "[Sys]",
        ChatRelayService.Channel.Whisper => "[W]",
        _                                => string.Empty,
    };

    private static string SpelledLabel(ChatRelayService.Channel ch) => ch switch
    {
        ChatRelayService.Channel.Global  => "[Global]",
        ChatRelayService.Channel.Local   => "[Local]",
        ChatRelayService.Channel.Clan    => "[Clan]",
        ChatRelayService.Channel.System  => "[System]",
        ChatRelayService.Channel.Whisper => "[Whisper]",
        _                                => string.Empty,
    };

    internal override void Reset()
    {
        if (_subscribed) { ChatRelayService.LineCaptured -= OnLineCaptured; _subscribed = false; }
        Patches.InputSuppression.ChatInputActive = false;
        _tabButtons.Clear();
        _whisperSubButtons.Clear();
        _whisperSubPartners.Clear();
        _initiatedPartners.Clear();
        _closedPartners.Clear();
        _whisperSendTo = null;
        _whisperPicker = null;
        _whisperSubRow = null;
        _input = null;
        _inputRow = null;
        _log = null;
        _scrollRect = null;
        _scrollContentRect = null;
        if (_composeKeyTicker != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_composeKeyTicker);
            _composeKeyTicker = null;
        }
        if (_tabHotkeyTicker != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_tabHotkeyTicker);
            _tabHotkeyTicker = null;
        }
        if (_nameClickTicker != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_nameClickTicker);
            _nameClickTicker = null;
        }
        if (_scrollKeyTicker != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_scrollKeyTicker);
            _scrollKeyTicker = null;
        }
        if (_autoHideTicker != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_autoHideTicker);
            _autoHideTicker = null;
        }
        if (_renderTicker != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_renderTicker);
            _renderTicker = null;
        }
        _renderDirty = false;
        // Leave the (harmless) CanvasGroup on the root but restore full visibility so a rebuilt
        // window never inherits a mid-fade alpha.
        try { if (_rootCg != null) { _rootCg.alpha = 1f; _rootCg.blocksRaycasts = true; } } catch { }
        _rootCg = null;
        _wasEnabledForAutoHide = false;
        _composeDropdownObj = null;
        _composeDropdown = null;
        _composeTargets.Clear();
        _composeIndex = 0;
        _composeSignature = " ";
    }
}
