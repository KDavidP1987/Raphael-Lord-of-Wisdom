using System;
using System.Collections.Generic;
using System.Linq;
using Raphael.Config;
using Raphael.Utils;
using Il2CppInterop.Runtime;
using ProjectM.Network;
using Unity.Entities;
using DateTime = System.DateTime;

namespace Raphael.Services;

// OUTBOUND chat-injection pipeline.
//
// Public API:
//   - EnqueueMessage(string) - queue a chat command to send.
//   - ProcessAllMessages()    - called every frame from Behaviors/CoreUpdateBehavior;
//                               throttled at Settings.GlobalQueryIntervalInSeconds
//                               (default 2s) so we don't spam the server.
//   - SetCharacter / SetUser  - called from InitializationPatch once the player's
//                               LocalCharacter / LocalUser entities are captured;
//                               messages are only sent when BOTH are set.
//
// Internals:
//   SendMessage builds a one-off ECS entity carrying the four components V Rising
//   uses to deliver a chat-typed-by-player event. The server treats it identically
//   to text the player typed in their chat box.
//
// Ported from LearningMods/BloodCraftUI-master/BloodCraftUI/Services/MessageService.cs.
// Split into partials so MessageService_Processing.cs can own the BCCOM_* command
// strings and the inbound regex/Eclipse-protocol decode (Phase 3b/3c).
public static partial class MessageService
{
    static EntityManager EntityManager => Plugin.EntityManager;

    // 0.10.3 critical fix: this used to be a static-field initializer that
    // ran inside MessageService's cctor. Every ComponentType.ReadOnly call
    // routes into Unity.Entities.TypeManager.FindTypeIndex, which NREs when
    // V Rising's ECS World hasn't been created yet. Pre-0.10.0 the cctor
    // happened to fire late enough that the World existed by then, but 0.10.0
    // added VBloodScannerService.Initialize() at Plugin.Load time which
    // touches MessageService.FamSearchCompleted — the first MessageService
    // static-field access ANY code path makes triggers the cctor, and at
    // Plugin.Load the World is definitely not up. Lazy-init the field so the
    // ComponentType.ReadOnly calls happen at SendMessage-time (gameplay,
    // World is up) rather than cctor time (plugin load, World is not).
    //
    // Same lesson is documented in EclipseProtocolService.cs's NOTE block —
    // copy/pasting it here so the next contributor sees the rationale right
    // next to the lazy pattern.
    private static ComponentType[] _networkEventComponents;
    private static ComponentType[] NetworkEventComponents =>
        _networkEventComponents ??= new[]
        {
            ComponentType.ReadOnly(Il2CppType.Of<FromCharacter>()),
            ComponentType.ReadOnly(Il2CppType.Of<NetworkEventType>()),
            ComponentType.ReadOnly(Il2CppType.Of<SendNetworkEventTag>()),
            ComponentType.ReadOnly(Il2CppType.Of<ChatMessageEvent>()),
        };

    private static readonly Queue<string> OutputMessages = new();
    private static Entity _localCharacter = Entity.Null;
    private static Entity _localUser      = Entity.Null;
    private static bool   _isInitialized;
    private static DateTime _lastAction = DateTime.MinValue;
    private static int _timeoutSeconds;

    private static readonly NetworkEventType NetworkEventType = new()
    {
        IsAdminEvent = false,
        EventId      = NetworkEvents.EventId_ChatMessageEvent,
        IsDebugEvent = false,
    };

    public static bool IsInitialized => _isInitialized;

    /// <summary>Local user entity once <see cref="SetUser"/> has been called; <see cref="Entity.Null"/> otherwise.</summary>
    public static Entity LocalUser => _localUser;

    /// <summary>Local character entity once <see cref="SetCharacter"/> has been called; <see cref="Entity.Null"/> otherwise.</summary>
    public static Entity LocalCharacter => _localCharacter;

    /// <summary>True if the local player is CURRENTLY authed as a server admin. Reads the replicated
    /// <c>User.IsAdmin</c> bool off the local user entity (snapshot-synced to the client). NOTE: reflects
    /// the AUTHED state — false at login until the player runs <c>adminauth</c> (F1 console), true after,
    /// and flips live as the server pushes the snapshot. Used only for UI gating; the server enforces
    /// admin permission on every command regardless. Best-effort: never throws.</summary>
    public static bool IsLocalAdmin()
    {
        try
        {
            var u = _localUser;
            if (u != Entity.Null && u.Has<User>())
                return u.Read<User>().IsAdmin;
        }
        catch { /* not ready / type unavailable → treat as non-admin */ }
        return false;
    }

    /// <summary>
    /// Send a player-initiated chat command. Bypasses the 2-second throttle
    /// queue so a button click reaches the server on the next frame instead of
    /// up to 2s later (live feedback during Phase 4 testing surfaced the delay
    /// as a real UX problem; rate-limiting human button-mashing is not worth
    /// the perceived input lag).
    ///
    /// Arms the regex intercept flag (NoteOutboundForIntercept) so the reply
    /// gets routed to the right state slot.
    /// </summary>
    public static void EnqueueMessage(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!_isInitialized)
        {
            LogUtils.LogWarning($"EnqueueMessage('{text}') ignored — character/user not bound yet.");
            return;
        }
        OutboundLedger.RecordUser(text, "button");   // 0.67.2: player-initiated → its replies/errors stay visible
        NoteOutboundForIntercept(text);
        SendMessage(text);
    }

    /// <summary>
    /// 0.10.2: same as EnqueueMessage but tells the chat-capture pipeline to
    /// destroy the server's reply lines so they don't clutter chat. Used by
    /// the overlay's bonus-stats ticker, the V-Bloods scanner, and the
    /// per-tab auto-refresh paths — their replies are already rendered in
    /// the Raphael UI, so the chat copy is pure noise. Manual user-triggered
    /// commands (Refresh button etc.) MUST use the regular EnqueueMessage
    /// so the user still sees their explicit query reply in chat.
    ///
    /// Mechanism: sets the static "next-capture-silent" flag on
    /// MessageService_Processing immediately before arming the intercept.
    /// NoteOutboundForIntercept consumes it during arming and stores its
    /// decision in a per-intercept slot, so subsequent regular EnqueueMessage
    /// calls aren't affected.
    /// </summary>
    public static void EnqueueMessageSilent(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!_isInitialized)
        {
            LogUtils.LogWarning($"EnqueueMessageSilent('{text}') ignored — character/user not bound yet.");
            return;
        }
        // 0.17.1: silent commands are NOT blocked under Eclipse. The crash fix is
        // skipping the passive PROTOCOL layer (registration + stream decode) — see
        // EclipseProtocolService — NOT suppressing chat commands. Familiar/box/V-Blood
        // queries (which Eclipse doesn't provide) and the user's own commands are
        // occasional gameplay-time sends, as safe as typing in chat. The stat-overlay
        // auto-refresh (.wep/.bl get) self-disables because those overlays are off
        // under Eclipse (see RestoreOverlaysFromSettings).
        // 0.10.6: _nextCommandIsBchAuto replaces the prior
        // _suppressNextCaptureChat flag. The intercept arming reads this
        // and classifies the command as BchAuto category (rather than
        // running the prefix classifier), so the Chat Logging BchAuto
        // toggle controls visibility. Same partial-class field — no prefix.
        _nextCommandIsBchAuto = true;
        OutboundLedger.RecordBackground(text);   // 0.67.2: Raphael-fired → its VCF errors may be hidden
        NoteOutboundForIntercept(text);
        // Defensive reset: NoteOutboundForIntercept clears the flag after
        // arming, but if the command didn't match any arming branch we
        // need to clear so the next call isn't accidentally tagged auto.
        _nextCommandIsBchAuto = false;
        SendMessage(text);
    }

    public static void SetCharacter(Entity entity)
    {
        _localCharacter = entity;
        UpdateInitializedFlag();
    }

    public static void SetUser(Entity entity)
    {
        _localUser = entity;
        UpdateInitializedFlag();
    }

    private static void UpdateInitializedFlag()
    {
        bool ready = _localCharacter.HasValue() && _localUser.HasValue();
        if (ready && !_isInitialized)
        {
            _isInitialized = true;
            LogUtils.LogInfo("MessageService initialized (character + user bound).");
        }
    }

    public static void Destroy()
    {
        _localCharacter = Entity.Null;
        _localUser      = Entity.Null;
        OutputMessages.Clear();
        _isInitialized  = false;
        OutboundLedger.Reset();   // 0.67.2: no attribution carries across logout / reconnect
    }

    /// <summary>
    /// Per-frame tick still registered with CoreUpdateBehavior. Currently a no-op
    /// because EnqueueMessage was switched to immediate send (Phase 5a). Kept as
    /// a hook so we can re-introduce defensive batched throttling later if some
    /// future code path enqueues many messages programmatically.
    /// </summary>
    public static void ProcessAllMessages()
    {
        if (!_isInitialized) return;
        if (OutputMessages.Count == 0) return;

        // Drain anything that snuck in via the legacy queue path. Cheap; no throttle.
        while (OutputMessages.Count > 0)
            SendMessage(OutputMessages.Dequeue());
    }

    /// <summary>
    /// Send a chat message immediately, bypassing the 2-second throttle queue.
    /// Use for one-off protocol messages that must land on the next frame
    /// (e.g. the Eclipse-protocol registration handshake); use
    /// <see cref="EnqueueMessage"/> for player-initiated commands so we don't
    /// flood the server parser.
    /// </summary>
    public static void SendRaw(string text)
    {
        if (!_isInitialized || string.IsNullOrEmpty(text)) return;
        SendMessage(text);
    }

    /// <summary>
    /// 0.65.0: send a protocol handshake as a WHISPER TO SELF rather than Local proximity chat.
    /// A real Bloodcraft/Eclipse server consumes the frame purely by its text content
    /// (Contains("[ECLIPSE]") + MAC) and destroys the entity before rebroadcast, so this behaves
    /// identically there. But on a server that does NOT run Bloodcraft, nothing consumes it — and a
    /// Local message would then be proximity-broadcast to nearby players (raw handshake in their
    /// chat AND a floating bubble over the sender's head; the "Eclipse relog spam" bug). A whisper
    /// addressed to yourself is never proximity-broadcast, so the leak can't reach bystanders.
    /// ReceiverEntity = our own NetworkId (same self-target the Local path already used).
    /// </summary>
    public static void SendRawSelfWhisper(string text)
    {
        if (!_isInitialized || string.IsNullOrEmpty(text)) return;
        try
        {
            var chatMessageEvent = new ChatMessageEvent
            {
                MessageText    = text,
                MessageType    = ChatMessageType.Whisper,
                ReceiverEntity = _localUser.Read<NetworkId>(),
            };

            Entity networkEntity = EntityManager.CreateEntity(NetworkEventComponents);
            networkEntity.Write(new FromCharacter { Character = _localCharacter, User = _localUser });
            networkEntity.Write(NetworkEventType);
            networkEntity.Write(chatMessageEvent);
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"MessageService.SendRawSelfWhisper failed: {ex}");
        }
    }

    private static void SendMessage(string text)
    {
        try
        {
            var chatMessageEvent = new ChatMessageEvent
            {
                MessageText    = text,
                MessageType    = ChatMessageType.Local,
                ReceiverEntity = _localUser.Read<NetworkId>(),
            };

            Entity networkEntity = EntityManager.CreateEntity(NetworkEventComponents);
            networkEntity.Write(new FromCharacter { Character = _localCharacter, User = _localUser });
            networkEntity.Write(NetworkEventType);
            networkEntity.Write(chatMessageEvent);
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"MessageService.SendMessage failed: {ex}");
        }
    }

    // 0.17: send a chat message on a specific channel (Global / Local / Team).
    // Used by the tabbed chat window's input box. No intercept arming — this is
    // real player chat, not a command awaiting a parsed reply.
    // 0.17: send chat on a channel (Global / Local / Team). Mirrors the proven
    // command-send path (self ReceiverEntity — same as Eclipse/FamBook). NOTE:
    // broadcast chat sent via this raw ChatMessageEvent injection is under
    // investigation — COMMANDS round-trip fine through it, but plain broadcast
    // chat hasn't surfaced yet; diagnosing whether it needs the native send path.
    public static void SendChat(string text, ChatMessageType type)
    {
        if (!_isInitialized || string.IsNullOrEmpty(text)) return;
        OutboundLedger.RecordUser(text, "Raphael chat");   // 0.67.2: no-op unless it's a dot-command
        try
        {
            var chatMessageEvent = new ChatMessageEvent
            {
                MessageText    = text,
                MessageType    = type,
                ReceiverEntity = _localUser.Read<NetworkId>(),
            };

            Entity networkEntity = EntityManager.CreateEntity(NetworkEventComponents);
            networkEntity.Write(new FromCharacter { Character = _localCharacter, User = _localUser });
            networkEntity.Write(NetworkEventType);
            networkEntity.Write(chatMessageEvent);
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"MessageService.SendChat failed: {ex}");
        }
    }

    // 0.17.0: send a WHISPER to a specific player. target is the recipient's
    // NetworkId (captured from an incoming whisper's FromUser). Same injection as
    // SendChat but MessageType=Whisper and ReceiverEntity=the target, not self.
    public static void SendWhisper(string text, NetworkId target)
    {
        if (!_isInitialized || string.IsNullOrEmpty(text)) return;
        try
        {
            var chatMessageEvent = new ChatMessageEvent
            {
                MessageText    = text,
                MessageType    = ChatMessageType.Whisper,
                ReceiverEntity = target,
            };

            Entity networkEntity = EntityManager.CreateEntity(NetworkEventComponents);
            networkEntity.Write(new FromCharacter { Character = _localCharacter, User = _localUser });
            networkEntity.Write(NetworkEventType);
            networkEntity.Write(chatMessageEvent);
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"MessageService.SendWhisper failed: {ex}");
        }
    }
}
