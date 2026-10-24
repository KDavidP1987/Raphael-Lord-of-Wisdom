using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using BloodcraftEclipseBridge.Messages;
using ProjectM.Network;
using Raphael.Behaviors;
using Raphael.Utils;

namespace Raphael.Services.Emberglass;

// 0.73.0 (experimental, opt-in): receive Bloodcraft's data over Emberglass. Bloodcraft 1.13.22+ with its opt-in
// UseEmberglassEclipseBridge sends progress/configs as Emberglass packets to any client that finished an Emberglass
// handshake, and falls back to chat only when that send throws. A Raphael user who also has the Emberglass client
// on such a server would otherwise get no Bloodcraft stats. Mirrors Eclipse 1.3.17 Services/EmberglassEclipseBridge.cs,
// receive side only: registration with Bloodcraft stays on chat (the server accepts it either way).
//
// Reflection only, so Emberglass stays optional and no Emberglass type is referenced at compile time. The handler's
// User is the game's ProjectM.Network.User. Emberglass keeps one handler per packet type (last registration wins),
// so this registers only when Eclipse (which registers its own) is not loaded. Emberglass can't be asked to drop a
// handler mid-session in a way we rely on, so the setting is read once at Plugin.Load and applies after a restart.
internal static class EmberglassBloodcraftBridge
{
    private const string EMBERGLASS_ASSEMBLY = "Emberglass";
    private const string VNETWORK_TYPE = "Emberglass.API.Shared.VNetwork";

    public static readonly EmberglassBridgeState State = new();

    /// <summary>The setting's value when the game started; the bridge follows this one until a restart.</summary>
    public static bool SettingAtLoad { get; private set; }

    private static bool _scheduled, _initialized;
    private static int _mainThreadId, _warnedOffThread;
    private static Action _initOnce, _drain;
    private static readonly ConcurrentQueue<string> _offThread = new();

    private static double Now => UnityEngine.Time.realtimeSinceStartup;

    /// <summary>Plugin.Load: remember the setting and the main thread, and defer the decision to the first frame,
    /// when every plugin (Emberglass, Eclipse) has loaded.</summary>
    public static void Schedule(bool settingOn)
    {
        if (_scheduled) return;
        _scheduled = true;
        SettingAtLoad = settingOn;
        _mainThreadId = Environment.CurrentManagedThreadId;
        if (!settingOn) { State.SetDecided(EmberglassBridgeStatus.Off); return; }
        _initOnce = () =>
        {
            CoreUpdateBehavior.Actions.Remove(_initOnce);
            Initialize();
        };
        CoreUpdateBehavior.Actions.Add(_initOnce);
    }

    /// <summary>True when the Emberglass client assembly is loaded (for the Connection-tab hint).</summary>
    public static bool EmberglassLoaded() => ResolveVNetwork() != null;

    private static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var vNetwork = ResolveVNetwork();
            var decided = EmberglassBridgeState.Decide(SettingAtLoad, vNetwork != null, EclipseProtocolService.IsEclipseModLoaded());
            State.SetDecided(decided);
            if (decided != EmberglassBridgeStatus.RegisteredNoData)
            {
                LogUtils.LogInfo($"[Raphael] Emberglass bridge not registered: {decided}.");
                return;
            }

            var register = vNetwork.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "RegisterClientbound" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            register.MakeGenericMethod(typeof(EclipseServerMessagePacket))
                .Invoke(null, new object[] { new Action<User, EclipseServerMessagePacket>(OnPacket) });
            State.Registered();
            // Installed here, on the main thread, so an off-thread packet only touches the concurrent queue. It lives
            // as long as the registration (the whole session): one empty-queue check per frame.
            _drain = () => { while (_offThread.TryDequeue(out var m)) Handle(m); };
            CoreUpdateBehavior.Actions.Add(_drain);
            LogUtils.LogInfo("[Raphael] Emberglass bridge registered for Bloodcraft data (experimental).");
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            State.Fail($"{inner.GetType().Name}: {inner.Message}");
            LogUtils.LogWarning($"[Raphael] Emberglass bridge disabled for this session: {State.FailReason}");
        }
    }

    private static void OnPacket(User sender, EclipseServerMessagePacket packet)
    {
        var message = packet?.Message ?? ""; // ConcurrentQueue rejects null; Deliver counts "" as an empty packet
        // Emberglass dispatches clientbound packets on the main thread; if that ever changes, queue onto the
        // CoreUpdateBehavior frame instead of touching game state from another thread.
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            _offThread.Enqueue(message);
            if (Interlocked.Exchange(ref _warnedOffThread, 1) == 0)
                LogUtils.LogWarning("[Raphael] Emberglass bridge: packet arrived off the main thread; handling it on the next frame.");
            return;
        }
        Handle(message);
    }

    private static void Handle(string message)
    {
        bool ok = State.Deliver(message, Now, EclipseProtocolService.TryHandleServerMessage);
        if (!ok && !string.IsNullOrWhiteSpace(message))
            LogUtils.LogWarning("[Raphael] Emberglass bridge: a Bloodcraft packet failed the signature check (check secrets.json matches the server).");
        else if (ok && State.Packets == 1)
            LogUtils.LogInfo("[Raphael] Emberglass bridge: first Bloodcraft packet received.");
    }

    private static Type ResolveVNetwork()
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == EMBERGLASS_ASSEMBLY);
            return asm?.GetType(VNETWORK_TYPE, throwOnError: false);
        }
        catch { return null; }
    }
}
