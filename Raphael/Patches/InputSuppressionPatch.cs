using Raphael.Services;
using Raphael.Utils;
using HarmonyLib;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.UI;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Raphael.Patches;

// 0.16: suppress the local character's GAMEPLAY input (movement / aim /
// ability + hotkey casts) while the Raphael main panel is open, so the character
// doesn't act in the background while the user clicks buttons or types into
// forms. Chief friend-test complaint — especially bad for admins who fire
// hotkeyed abilities by accident.
//
// APPROACH HISTORY (see docs/LESSONS_LEARNED.md + memory):
//   - 0.1.x: PREFIX on InputActionSystem.OnUpdate returning false → FROZE the
//     whole game. CAUSE: InputActionSystem ALSO drives Unity UI input, so
//     skipping it starved the UI. Removed.
//   - 0.16 #1: POSTFIX on InputActionSystem zeroing EntityInput.Movement/.State.
//     Safe but INEFFECTIVE — the write persisted on read-back yet the character
//     still moved. CAUSE (found this session): GameplayInputSystem runs AFTER
//     InputActionSystem and re-populates EntityInput from live hardware,
//     overwriting our zero the same frame.
//   - 0.16 #2: adding ProjectM.UI.ChatInputFocused tag to LocalCharacter — no
//     effect; the engine's chat-focus gate isn't keyed off the character entity.
//   - 0.16 #3 (THIS): skip the actual CLIENT GAMEPLAY PRODUCER systems while the
//     panel is open:
//        * ProjectM.GameplayInputSystem.OnUpdate  -> movement + aim
//        * ProjectM.AbilityInputSystem.OnUpdate   -> ability / hotkey casts
//     These are SEPARATE systems from InputActionSystem, which keeps running —
//     so Unity UI input, the cursor, and Raphael form typing all stay alive
//     (structurally NO freeze, unlike 0.1.x). The character stops moving,
//     aiming, and casting. We also zero EntityInput once while blocking so a key
//     held at open-time doesn't drift — and this time the zero STICKS, because
//     the system that used to overwrite it (GameplayInputSystem) is skipped.
//
// Gated behind Settings.SuppressGameInputWhileUIOpen (kill-switch, default OFF).
// Every callback is wrapped so any exception falls through to running the
// original system — a bug in here can NEVER freeze the game.
internal static class InputSuppression
{
    private static double _lastDiagAt;

    // 0.17: true while a Raphael text field has keyboard focus, so the game keyboard is
    // LOCKED while you type — movement, abilities/ability-keybinds, menu hotkeys, and
    // Raphael's own toggle hotkeys are all suppressed. Independent of the
    // SuppressGameInputWhileUIOpen setting (you never want to act mid-type).
    // 0.18.2: now covers the chat window AND every Raphael form field (search/name/admin
    // boxes), so a keystroke meant for a text box can never fire a game action — e.g. an
    // admin's destructive ability keybind. See InputFieldRef.AnyFocused for the (reliable)
    // focus source and the lessons behind it.
    internal static bool ChatInputActive;

    // 0.18.2: falling-edge grace. When focus drops we hold the lock for a few frames so a
    // 1-frame focus blip (clicking field-A → field-B, or the chat-exit transition) can't
    // flicker ChatInputActive — flicker on the ability path is exactly what caused the
    // 0.17.2 "character stuck looping actions" regression. Escape clears it immediately.
    private static int _typingReleaseGrace;
    private const int TYPING_RELEASE_GRACE_FRAMES = 6; // ~100ms @60fps — imperceptible
    private static double _focusDiagAt;

    // 0.17.0 (fix): poll the typing-focus EVERY frame from CoreUpdateBehavior, not from
    // ClientChatSystem.OnUpdate (which doesn't tick reliably → the flag went stale and
    // menu hotkeys leaked mid-typing). The Escape hatch lives here too so it fires on a
    // guaranteed cadence. Registered once in Plugin.Load.
    internal static void TickChatFocus()
    {
        try
        {
            // Chat window focus is always honored (proven since 0.17.0). Form-field focus
            // is gated by a default-ON kill-switch so the (newer) form coverage can be
            // disabled instantly without losing chat suppression if it ever misbehaves.
            // 0.18.2: (reverted) EnsureSelectedFocused() re-activated the chat field after ESC →
            // trapped the user in chat. It also didn't help forms. The real form fix is to engage the
            // game's native chat-open input gate while a form is focused (see ClientChatPatch), not to
            // fiddle with field activation.

            bool chatFocused = Plugin.UIManager?.IsChatInputFocused() ?? false;
            bool formFocused = Config.Settings.LockKeyboardInFormFields
                               && UI.Framework.UniverseLib.UI.Models.InputFieldRef.AnyFocused();
            bool typingNow = chatFocused || formFocused;

            // 0.18.2 TEMP diagnostic (LogInfo so it's visible WITHOUT enabling Diagnostic mode):
            // once/sec while a UI field is selected or we're locking, surface the focus signals so a
            // form that isn't locking can be pinpointed. Removed once forms lock reliably.
            try
            {
                UnityEngine.GameObject selGo = null;
                try { var es = UnityEngine.EventSystems.EventSystem.current; selGo = es != null ? es.currentSelectedGameObject : null; } catch { }
                if ((selGo != null || typingNow) && System.Math.Abs(UnityEngine.Time.realtimeSinceStartupAsDouble - _focusDiagAt) >= 1.0)
                {
                    _focusDiagAt = UnityEngine.Time.realtimeSinceStartupAsDouble;
                    bool selFocused = false;
                    try { var f = selGo != null ? selGo.GetComponent<TMPro.TMP_InputField>() : null; if (f != null) selFocused = f.isFocused; } catch { }
                    // LogDiagnostic (not LogInfo) so this is silent unless Diagnostic mode is on — no console spam.
                    LogUtils.LogDiagnostic($"[FocusDiag] chat={chatFocused} form={formFocused} lockForms={Config.Settings.LockKeyboardInFormFields} typing={typingNow} selected={(selGo != null ? selGo.name : "<none>")} selFocused={selFocused}");
                }
            }
            catch { }

            if (typingNow) _typingReleaseGrace = TYPING_RELEASE_GRACE_FRAMES;
            else if (_typingReleaseGrace > 0) _typingReleaseGrace--;

            ChatInputActive = typingNow || _typingReleaseGrace > 0;

            // Escape ALWAYS frees the keyboard immediately (overrides the grace) so the
            // user can never be trapped suppressed — releases the chat input AND any
            // focused form field (+ clears the EventSystem selection, the anti-freeze).
            if (ChatInputActive && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
            {
                _typingReleaseGrace = 0;
                ChatInputActive = false;
                Plugin.UIManager?.ReleaseChatInput();
                UI.Framework.UniverseLib.UI.Models.InputFieldRef.ReleaseAllFocused();
            }

            // 0.25.0: CONSOLE keybindings (`keybinding create ...` — the admin-assigned
            // hotkeys; they only execute for console-enabled/admin sessions) are read RAW
            // by Stunlock.Console, completely OUTSIDE the IInputContext pipeline — they
            // fire even while the NATIVE chat has focus (observed back in 0.18.2), so
            // neither the typing-lock context nor any menu patch can ever stop them.
            // The game's own countermeasure is the DisableConsoleKeybindingsOnFocus
            // component: while its text field is focused it sets
            // StunConsole.UI.EnableKeybindingUpdates = false every frame, and
            // ResetConsoleKeybindingsSystem re-enables it every frame. We follow the
            // exact same contract — keep writing false while Raphael should block input;
            // the game re-arms the flag the frame we stop, so admin binds can never be
            // left permanently dead even if Raphael crashes mid-block.
            if (Config.Settings.SuppressConsoleKeybindsWhileTyping && ShouldBlockMenus())
            {
                try
                {
                    var consoleUi = Engine.Console.StunConsole.UI;
                    if (consoleUi != null) consoleUi.EnableKeybindingUpdates = false;
                }
                catch { /* console not initialized yet — nothing to suppress */ }
            }
        }
        catch { ChatInputActive = false; _typingReleaseGrace = 0; } // never pin suppression on
    }

    internal static bool ShouldBlock()
    {
        try
        {
            if (ChatInputActive) return true;
            if (!Config.Settings.SuppressGameInputWhileUIOpen) return false;
            return Plugin.UIManager?.IsMainPanelOpen ?? false;
        }
        catch
        {
            return false;
        }
    }

    // 0.17.0: menu-hotkey gate for MenuInputSystem + OpenHUDMenuSystem. While
    // typing in chat we DO block the menu hotkeys (map = M, build = B, inventory,
    // …) so keystrokes don't open menus behind the chat. That can't trap the
    // player: ChatInputActive only stays true while our input is actually focused
    // (polled from isFocused), and pressing Escape force-releases the chat input
    // (ClientChatPatch.OnUpdate_Prefix) regardless of which systems are blocked,
    // since it reads the raw Escape key. So this is just ShouldBlock.
    internal static bool ShouldBlockMenus() => ShouldBlock();

    // 0.17.3: ability/attack suppression ALSO engages while the cursor is over the chat
    // window, so a left-click on the chat (its tabs or input box) never leaks into the
    // world as a primary attack — which was getting the character STUCK repeating a
    // basic attack (the click's button-release went to the UI, so the game never saw
    // it and kept "attacking"). Pointer-over is a targeted rect test — true only when
    // the cursor is literally over the chat window, never during combat — and it feeds
    // ONLY ability suppression (not movement, not ChatInputActive), so it can't cause
    // the movement action-loop the 0.17.2 revert fixed.
    internal static bool ShouldBlockAbilities()
    {
        if (ShouldBlock()) return true;
        try
        {
            if (Plugin.UIManager?.IsPointerOverChatWindow() ?? false) return true;
            // 0.19: ALWAYS suppress while the cursor is over the open main panel — clicking buttons/forms
            // there must never fire a world attack/cast. Same proven ability-only path as the chat window.
            if (Plugin.UIManager?.IsPointerOverMainPanel() ?? false) return true;
            // B3 (0.19): optional guarded opt-in — extend the over-chat ability/primary-attack
            // suppression to ANY Raphael panel/overlay so a click on a panel can't leak into the world as
            // a cast or a stuck attack. Default OFF (some players WANT to cast with the cursor over an
            // overlay). Feeds ONLY this ability path — not movement, not the menu patches — so it
            // carries none of the movement-loop / menu-patch crash risk.
            if (Config.Settings.BlockInputWhenPointerOverUI && (Plugin.UIManager?.IsPointerOverAnyUI() ?? false))
                return true;
            return false;
        }
        catch { return false; }
    }

    // STUCK-ATTACK-ON-CLOSE fix. Opening the panel already cancels the in-flight attack (see
    // AbilityInputSuppressionPatch). The mirror case is CLOSING: closing the panel (clicking its X)
    // re-enables gameplay cursor/input, and the closing click latches a HELD primary attack that loops
    // until you click again ("character keeps attacking after I close the UI").
    //
    // 0.26.5 tried gating a release-hold on the mouse button still being DOWN at the unblock frame —
    // but a quick X-click RELEASES the button a frame or two before suppression falls off, so the gate
    // never engaged and the attack still latched. 0.26.6: after ability suppression falls on→off we run
    // an UNCONDITIONAL short release WINDOW (default ~0.4s) during which the ability producer stays
    // skipped and the cast is interrupted every frame — so whatever the closing click left half-started
    // (and any attack the cursor re-enable kicks off a frame later) is cancelled regardless of button
    // state. If the button is still physically held we extend the window until release (1s cap). The
    // window only opens on the close edge, so normal play is unaffected aside from a sub-half-second
    // dead-zone for an attack issued in the instant after closing the panel.
    // Called once per frame from AbilityInputSuppressionPatch.Prefix.
    private static bool _abilityWasBlocking;
    private static bool _mainPanelWasOpen;
    private static double _abilityUnblockAt;
    private static double _postCloseLogUntil;   // 0.29.1: stuck-attack diag window after a panel close (DiagnosticMode only)
    private static double _lastPostCloseLogAt;
    private static bool   _phantomGuardArmed;   // 0.29.2: post-close phantom-attack guard (latched Primary while LMB up)
    private static bool   _phantomActive;       // 0.29.3: latched once a phantom Primary is seen; suppress until a real click
    private static double _phantomGuardArmedAt;
    private const double  PHANTOM_GUARD_MAX_SECONDS = 120.0;   // safety: never stay armed forever
    private const double ABILITY_RELEASE_WINDOW_SECONDS = 0.4;   // unconditional cancel window after close
    private const double ABILITY_RELEASE_HOLD_MAX_SECONDS = 1.0; // cap if the mouse button stays held

    internal static bool ShouldBlockAbilitiesWithReleaseGrace()
    {
        bool raw = ShouldBlockAbilities();
        bool effective = raw;
        try
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;

            // 0.29: ALSO open the release window on the MAIN PANEL's open→closed edge — independent of the
            // ability-block falling edge below. The falling edge only fires when ShouldBlockAbilities() was
            // true the prior frame, i.e. the cursor was over the panel (pointer-over rect test). That MISSES
            // closes where the cursor wasn't over the panel — closing via the toggle HOTKEY, or any frame
            // ordering where the panel disables before the pointer-over test sees it — and in those cases the
            // held closing click could still latch a stuck auto-attack (the regression testers still hit).
            // Keying off IsMainPanelOpen catches every close path. (Strictly widens WHEN we cancel; can only
            // suppress an attack for ≤0.4s right after a close.)
            bool panelOpen = Plugin.UIManager?.IsMainPanelOpen ?? false;
            bool panelJustClosed = _mainPanelWasOpen && !panelOpen;
            _mainPanelWasOpen = panelOpen;
            if (panelJustClosed)
            {
                _postCloseLogUntil = now + 1.5;   // 0.29.1: arm the stuck-attack diagnostic
                _phantomGuardArmed = true;        // 0.29.2: arm the phantom-attack guard
                _phantomActive = false;           // 0.29.3: not yet latched for THIS close
                _phantomGuardArmedAt = now;
            }

            if (raw)
            {
                _abilityUnblockAt = 0; // (re)blocking — clear any pending release window
            }
            else
            {
                if (_abilityWasBlocking || panelJustClosed) _abilityUnblockAt = now;   // falling edge → open the window
                if (_abilityUnblockAt > 0)
                {
                    double since = now - _abilityUnblockAt;
                    bool lmbHeld = UnityEngine.Input.GetMouseButton(0);
                    if (since < ABILITY_RELEASE_WINDOW_SECONDS ||
                        (lmbHeld && since < ABILITY_RELEASE_HOLD_MAX_SECONDS))
                        effective = true;     // keep cancelling the attack this frame
                    else
                        _abilityUnblockAt = 0; // window elapsed → resume normal attacks
                }
            }
            _abilityWasBlocking = raw;
        }
        catch { _abilityUnblockAt = 0; }
        return effective;
    }

    // 0.29.1: stuck-attack diagnostics. The close-time attack-cancel has been tried several ways
    // (0.26.5 / 0.26.6 / 0.29.0) and a tester still sees the character keep auto-attacking after the
    // panel closes until they swing once — which points to a HELD/latched attack ACTION upstream of our
    // ability skip (the closing click's button-UP was eaten by the UI, so the game's input action never
    // saw the release; our 0.4s cancel can't clear that). To fix it precisely we need to SEE what's
    // latched. With global Diagnostic mode ON, for ~1.5s after every main-panel close this logs the local
    // character's input + ability-cast state each ~0.1s to LogOutput.log (filter `[CloseAttackDiag]`).
    // No effect when Diagnostic mode is off.
    internal static bool WantPostCloseDiag()
    {
        try { return Config.Settings.DiagnosticMode && UnityEngine.Time.realtimeSinceStartupAsDouble < _postCloseLogUntil; }
        catch { return false; }
    }

    internal static void LogPostCloseAttackDiag(EntityManager em, Entity character)
    {
        try
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (now - _lastPostCloseLogAt < 0.1) return;
            _lastPostCloseLogAt = now;
            string s = "";
            if (em.HasComponent<EntityInput>(character))
            {
                var ei = em.GetComponentData<EntityInput>(character);
                s += $"inState={ei.State} mov={ei.Movement}";
            }
            if (em.HasComponent<EntityAbilityInput>(character))
            {
                var ai = em.GetComponentData<EntityAbilityInput>(character);
                s += $" castInput={ai.CastInput} queued={(ai.QueuedCastGroup != Entity.Null)} prepare={(ai.PrepareCastGroup != Entity.Null)} interrupt={ai.Interrupt}";
            }
            LogUtils.LogInfo($"[CloseAttackDiag] {s}");
        }
        catch { }
    }

    internal static bool PhantomGuardArmed => _phantomGuardArmed;

    // 0.29.2: phantom-attack guard. CONFIRMED via [CloseAttackDiag]: after a panel close the game's Primary
    // attack input ACTION stays latched (the closing UI click's button-UP was eaten by the Close button, so
    // the game's input system never saw the release). GameplayInputSystem then re-feeds CastInput=Primary
    // every frame even though the physical mouse button is UP, and the character auto-attacks until a real
    // swing supplies the missing release. Our older fixed-duration cancel windows (0.26.5 / 0.26.6 / 0.29.0)
    // couldn't beat a permanent latch. This guard does: while armed (set on panel close), a Primary cast with
    // the LMB physically UP is the phantom → suppress it (the ability prefix interrupts + clears it). A REAL
    // attack HOLDS the LMB, so it passes through and its button release clears the latch — at which point we
    // disarm. Distinguishing on the physical mouse button is what separates the phantom from a wanted attack.
    internal static bool ShouldSuppressPhantomAttack(bool castIsPrimary)
    {
        if (!_phantomGuardArmed) return false;
        try
        {
            // Disarm the instant the user physically presses the mouse button — a real attack/cast holds the
            // button, takes over, and its release clears the latched input. Also disarm after a safety cap so
            // a stuck state can never pin the guard on indefinitely.
            if (UnityEngine.Input.GetMouseButton(0)
                || UnityEngine.Time.realtimeSinceStartupAsDouble - _phantomGuardArmedAt > PHANTOM_GUARD_MAX_SECONDS)
            {
                _phantomGuardArmed = false;
                _phantomActive = false;
                return false;
            }
        }
        catch { _phantomGuardArmed = false; _phantomActive = false; return false; }

        // 0.29.3 LATCH. Our prefix reads EntityAbilityInput.CastInput, which is AbilityInputSystem's output
        // from the PREVIOUS frame and is zeroed by our own suppression — so a per-frame "is it Primary right
        // now?" test alternates None/Primary and lets every OTHER phantom attack through (the flickering
        // ~0.5s cooldown the tester saw). Fix: once we observe the phantom Primary even ONCE, latch it and
        // keep suppressing EVERY frame until the user physically clicks (handled above). _phantomActive only
        // latches after a real phantom observation, so a clean close (no phantom — e.g. hotkey close) never
        // over-suppresses: nothing latches and the guard disarms on the next click or the safety cap.
        if (castIsPrimary) _phantomActive = true;
        return _phantomActive;
    }

    // 0.17.2 CRASH FIX — safe replacement for the three menu-suppression Harmony
    // patches (MenuInputSystem / OpenHUDMenuSystem / ActionWheelSystem prefixes).
    // The 0.16.x crash bisect pinned those three as the trigger: detouring those hot
    // menu ECS systems tipped a latent Il2CppInterop GC-finalizer bug during the
    // entity/HUD churn of opening the map to track a V-Blood (and on login while
    // already tracking) — a native crash that also corrupts the BepInEx interop cache
    // so the client then fails to load. Movement/ability suppression
    // (GameplayInputSystem / AbilityInputSystem) was proven safe and stays patched.
    //
    // Instead of detouring the menu systems, we DRAIN the menu-open REQUEST entities
    // (OpenMenuEvent / GoToHUDMenu — the same ones OpenHUDMenuSuppressionPatch used to
    // destroy) from our own per-frame MonoBehaviour tick while menus should be blocked
    // (typing in the Raphael chat, or the Raphael panel open with SuppressGameInputWhileUIOpen).
    // No detour on the menu systems => the crash can't happen. It also only does any
    // work WHILE blocking, which is never while tracking, so it can't coincide with the
    // tracking GC. M/B/I/etc. all create one of these request components, so destroying
    // them before OpenHUDMenuSystem consumes them keeps the menu from opening.
    private static EntityQuery _drainOpenMenuQuery;
    private static EntityQuery _drainGoToHudQuery;
    private static bool _drainQueriesReady;

    // 0.18.4: called from the ClientBootstrapSystem.OnDestroy teardown hook (leave-game / server-switch).
    // Drops the cached drain queries so DrainMenuOpenRequests rebuilds them against the NEW client world
    // on the next use — using a query created in the now-disposed old world is a native crash (the
    // server-switch "open Raphael → crash"). PURE field reset; safe in the teardown hook.
    internal static void OnWorldTeardown() => _drainQueriesReady = false;

    internal static void DrainMenuOpenRequests()
    {
        try
        {
            if (!Config.Settings.EnableInputSuppressionPatches) return; // master kill-switch
            // Drain while menus should be blocked. ShouldBlockMenus() keys off ChatInputActive,
            // which (0.18.2) is true while typing in the chat window OR any Raphael form field — so
            // this now covers form-field typing too, via the reliable per-field focus poll
            // (replacing the 0.17.3 EventSystem-selection probe that could blip).
            if (!ShouldBlockMenus()) return;
            if (Plugin.IsClientNull()) return;

            var em = Plugin.EntityManager;
            // 0.18.4 CRASH FIX (server-switch): NEVER use a cached EntityQuery from a DISPOSED world.
            // The client world is torn down + recreated on a server-switch; a query created in the old
            // world NATIVE-crashes when used against the new one (ToEntityArray/DestroyEntity). The
            // OnDestroy teardown hook calls OnWorldTeardown() to drop the cached queries so they're
            // rebuilt against the new world; this World.IsCreated check is a belt-and-braces guard for
            // any transient bad-world frame. (This is THE root cause of "open Raphael after a server-switch
            // → crash to desktop" — the drain runs on panel-open when SuppressGameInputWhileUIOpen is
            // on, and on typing — and was using the stale query.)
            var world = em.World;
            if (world == null || !world.IsCreated) { _drainQueriesReady = false; return; }

            if (!_drainQueriesReady)
            {
                _drainOpenMenuQuery = em.CreateEntityQuery(ComponentType.ReadOnly(Il2CppType.Of<OpenMenuEvent>()));
                _drainGoToHudQuery  = em.CreateEntityQuery(ComponentType.ReadOnly(Il2CppType.Of<GoToHUDMenu>()));
                _drainQueriesReady  = true;
            }
            DrainNonNetworked(em, _drainOpenMenuQuery);
            DrainNonNetworked(em, _drainGoToHudQuery);
        }
        catch
        {
            _drainQueriesReady = false; // rebuild next frame (e.g. after a world reload)
        }
    }

    // 0.18.4 CRITICAL CRASH FIX: destroy only NON-networked menu-open requests. A blanket
    // em.DestroyEntity(query) here destroyed a NETWORKED entity (one with a ProjectM.Network.NetworkId)
    // when the user interacted with a networked object — e.g. opening / RENAMING a storage box while the
    // Raphael chat window had focus (pressing Enter in the rename box stole focus into Raphael chat → armed this
    // drain). Destroying a networked entity client-side corrupts ReceivePacketSystem's
    // NetworkedIdToEntityMap ("NetworkedIdToEntityMap contained a destroyed entity") and the Burst job in
    // ReceivePacketSystem.OnUpdate then ABORTS THE APPLICATION (hard crash to desktop). Stray menu-open
    // keypresses (M/B/K/…) create LOCAL request entities with no NetworkId, so skipping networked ones
    // still suppresses the menus we mean to — and never touches a network-managed entity again.
    private static void DrainNonNetworked(EntityManager em, EntityQuery q)
    {
        var arr = q.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var e in arr)
            {
                bool networked = false;
                try { networked = em.HasComponent<ProjectM.Network.NetworkId>(e); } catch { networked = true; } // unsure → don't destroy
                if (networked) continue;
                em.DestroyEntity(e);
            }
        }
        finally { arr.Dispose(); }
    }

    // -------------------------------------------------------------------------
    // 0.25.0: the menu/hotkey lock while typing now lives in Patches/TypingInputLock.cs —
    // a Raphael IInputContext registered in the game's own input-consumer stack (the exact
    // mechanism the NATIVE chat uses), consuming every ButtonInputAction at the source
    // while ShouldBlockMenus() is true. The 0.18.2 BlockInputState-COMPONENT attempt
    // that used to live here was structurally wrong (BlockInputState is the by-ref
    // accumulator struct of that consumer pipeline, not entity data — hence the
    // "Unknown Type" failures) and has been removed along with its dead helpers.
    // -------------------------------------------------------------------------

    internal static void Diag(string msg)
    {
        double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
        if (now - _lastDiagAt < 1.0) return;
        _lastDiagAt = now;
        // 0.18: demoted to diagnostic — this routine "panel open" notice was cluttering
        // normal logs and got mistaken for an error. Enable Diagnostic mode to see it.
        LogUtils.LogDiagnostic($"[InputSuppress] {msg}");
    }
}

// 0.18.2 (REVERTED): a postfix on ClientChatSystem.IsChatOpen getter that forced it true while a Raphael
// field was focused. It did NOT lock forms — V Rising's input/menu/debug systems read the chat-open
// state from an INTERNAL field, not this property, so overriding the getter's return value changed
// nothing (the log even showed StunConsole debug keybinds firing while CHAT was focused). It also
// appeared to cause NREs by confusing the chat system during text edits. Removed.

// Movement + aim producer. Skipping its OnUpdate stops the local character from
// moving or aiming while the Raphael panel is open.
[HarmonyPatch(typeof(GameplayInputSystem), nameof(GameplayInputSystem.OnUpdate))]
public static class GameplayInputSuppressionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameplayInputSystem __instance)
    {
        try
        {
            // 0.25.0: hand TypingInputLock the live InputActionSystem via this system's
            // injected _InputActionSystem reference (right instance, right world). One
            // null check per frame once captured; the registration work itself stays on
            // the CoreUpdateBehavior tick. This replaces the 0.25.0 wrong-world
            // GetExistingSystemManaged lookup that silently never found the system.
            TypingInputLock.CaptureFrom(__instance);

            // 0.17.2: drain queued menu-open requests from HERE. GameplayInputSystem
            // runs early in the input phase — before OpenHUDMenuSystem consumes the
            // requests — so this beats the menu open, unlike the CoreUpdateBehavior
            // tick which fired too late (menus still popped while typing). Self-gates
            // on ShouldBlockMenus, so it only acts while typing / panel-open, NEVER
            // during tracking — no crash risk (this system is proven safe to patch).
            InputSuppression.DrainMenuOpenRequests();
            // (0.25.0: the direct-keybind lock that used to be attempted here is now
            // TypingInputLock — a context in the game's own input stack; see that file.)

            if (!InputSuppression.ShouldBlock()) return true; // run normally

            // Clear any held movement so the character doesn't drift while the
            // producer is skipped. This STICKS now, because the only system that
            // repopulates EntityInput from live hardware (this one) is about to
            // be skipped — which is exactly why attempt #1's zeroing failed.
            try
            {
                var character = Plugin.LocalCharacter;
                if (!Plugin.IsClientNull() && character != Entity.Null)
                {
                    var em = Plugin.EntityManager;
                    if (em.HasComponent<EntityInput>(character))
                    {
                        var ei = em.GetComponentData<EntityInput>(character);
                        ei.Movement = default;
                        ei.State = default;
                        em.SetComponentData(character, ei);
                    }
                }
            }
            catch
            {
                // best-effort; never let this stop the skip below
            }

            InputSuppression.Diag("blocking GameplayInputSystem.OnUpdate (panel open).");
            return false; // skip movement/aim production this frame
        }
        catch
        {
            return true; // any failure → never freeze; let the game run
        }
    }
}

// Ability / hotkey cast producer. Skipping its OnUpdate stops the character from
// casting spells or using hotkeyed abilities while the Raphael panel is open.
//
// IMPORTANT (stuck-attack fix): the primary-mouse click that OPENS the Raphael panel
// is also seen by the game world as a primary-attack press on the frame before
// blocking engages. If we merely skip this system, that half-started attack
// never sees its button-release and the character fires continuously until the
// panel closes. So while blocking we ALSO actively cancel the in-flight cast on
// the local character — set EntityAbilityInput.Interrupt and clear the held /
// queued cast — so the attack is released instead of frozen ON.
[HarmonyPatch(typeof(AbilityInputSystem), nameof(AbilityInputSystem.OnUpdate))]
public static class AbilityInputSuppressionPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        try
        {
            // 0.17.3: ShouldBlockAbilities (not ShouldBlock) — also blocks while the
            // cursor is over the chat window, so clicking the chat never fires/sticks
            // a primary attack.
            // 0.26.5: ...WithReleaseGrace — also keeps releasing the attack for a moment AFTER the
            // panel closes while the LMB is still held (the closing X-click), so the held button can't
            // latch a stuck auto-attack. Ends the instant the button is released.
            bool block = InputSuppression.ShouldBlockAbilitiesWithReleaseGrace();

            // 0.29.1: post-close stuck-attack diagnostic (Diagnostic mode only; ~1.5s after a panel close).
            if (InputSuppression.WantPostCloseDiag())
            {
                try
                {
                    var dch = Plugin.LocalCharacter;
                    if (!Plugin.IsClientNull() && dch != Entity.Null)
                        InputSuppression.LogPostCloseAttackDiag(Plugin.EntityManager, dch);
                }
                catch { }
            }

            // Panel-open / release-grace suppression: the UI owns input, so skip the WHOLE ability
            // system this frame (unchanged behavior — a click on the open UI must never leak a cast).
            if (block)
            {
                try
                {
                    var character = Plugin.LocalCharacter;
                    if (!Plugin.IsClientNull() && character != Entity.Null)
                    {
                        var em = Plugin.EntityManager;
                        if (em.HasComponent<EntityAbilityInput>(character))
                        {
                            var ai = em.GetComponentData<EntityAbilityInput>(character);
                            ai.Interrupt = true;        // cancel the active cast (kills the stuck primary attack)
                            ai.CastInput = default;     // treat the cast button as released
                            ai.QueuedCastGroup = Entity.Null;
                            ai.PrepareCastGroup = Entity.Null;
                            em.SetComponentData(character, ai);
                        }
                    }
                }
                catch { /* best-effort; never let this stop the skip below */ }

                InputSuppression.Diag("blocking AbilityInputSystem.OnUpdate (panel open).");
                return false; // skip ability/hotkey cast production this frame
            }

            // 0.65.0 CAST-LOCK FIX: post-close phantom-attack guard. This USED to fold into `block`
            // and skip the ENTIRE AbilityInputSystem every frame until the user physically pressed the
            // LMB — which swallowed every spell (Q/E/right-click/number-key casts never press LMB) and
            // every power/emote-wheel selection after merely opening+closing the UI, clearing only when
            // the user did a real left-click attack. Now we cancel ONLY the phantom Primary and let the
            // system keep running, so legitimate casts and wheel selections pass through untouched. A
            // spurious latch (the leftover CastInput=Primary from the closing UI click) is now harmless:
            // we only ever zero a Primary that is actually present THIS frame — there's nothing to
            // cancel on a spell-cast frame, so casting is never blocked.
            if (InputSuppression.PhantomGuardArmed)
            {
                try
                {
                    var pch = Plugin.LocalCharacter;
                    if (!Plugin.IsClientNull() && pch != Entity.Null)
                    {
                        var pem = Plugin.EntityManager;
                        bool castPrimary = pem.HasComponent<EntityAbilityInput>(pch)
                            && pem.GetComponentData<EntityAbilityInput>(pch).CastInput.ToString() == "Primary";
                        // Always tick the guard (drives its disarm-on-physical-LMB / safety-cap logic),
                        // but only zero a Primary that's present this frame — the phantom auto-attack.
                        bool suppress = InputSuppression.ShouldSuppressPhantomAttack(castPrimary);
                        if (castPrimary && suppress && pem.HasComponent<EntityAbilityInput>(pch))
                        {
                            var ai = pem.GetComponentData<EntityAbilityInput>(pch);
                            ai.Interrupt = true;
                            ai.CastInput = default;     // cancel just the phantom Primary
                            ai.QueuedCastGroup = Entity.Null;
                            ai.PrepareCastGroup = Entity.Null;
                            pem.SetComponentData(pch, ai);
                            InputSuppression.Diag("cancelled phantom Primary (post-close); ability system still running.");
                        }
                    }
                }
                catch { }
            }

            return true; // run normally — the phantom guard never skips the ability system
        }
        catch
        {
            return true;
        }
    }
}

// Menu input reader. May handle radial / gamepad menu navigation. Kept as a
// belt-and-suspenders skip; the actual open-the-menu chokepoint is
// OpenHUDMenuSystem below (in-game testing showed B/M still opened with only
// this skipped, so they don't route through here on KBM).
[HarmonyPatch(typeof(MenuInputSystem), nameof(MenuInputSystem.OnUpdate))]
public static class MenuInputSuppressionPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        try
        {
            // ShouldBlockMenus (NOT ShouldBlock): chat typing must not block menus/escape.
            if (!InputSuppression.ShouldBlockMenus()) return true;
            InputSuppression.Diag("blocking MenuInputSystem.OnUpdate (panel open).");
            return false;
        }
        catch
        {
            return true;
        }
    }
}

// THE HUD-menu chokepoint. Every game menu (map = M, build = B, inventory,
// spellbook, emotes, VBlood tracking, …) is a HUDMenuType opened via
// HUDMenuManager.ToggleMenu/GoToMenu, whose request this system processes in
// OnUpdate. Skipping it while the Raphael panel is open means no game menu opens
// behind the panel — one skip covers them all.
//
// BUT skipping alone only DEFERS: the input reader (a separate system we don't
// block) still creates the open-request component each frame the key is pressed,
// and because we skip the consumer those requests pile up and all fire the
// instant the panel closes (observed in-game as "menus open with a delayed
// reaction"). The request comes in two component shapes — ProjectM.UI.OpenMenuEvent
// and ProjectM.UI.GoToHUDMenu (the latter has Delay/IsHandled, i.e. an explicitly
// deferred request). So while blocking we DRAIN both every frame: destroy the
// pending request entities so nothing is queued when the panel closes. The
// reader runs before this consumer in system order, so same-frame requests are
// already present here and get drained.
//
// Menu-CLOSE / ESC live elsewhere, and the UI pump is untouched, so the user can
// still close the Raphael panel and there's no freeze risk.
[HarmonyPatch(typeof(OpenHUDMenuSystem), nameof(OpenHUDMenuSystem.OnUpdate))]
public static class OpenHUDMenuSuppressionPatch
{
    private static EntityQuery _openMenuEventQuery;
    private static EntityQuery _goToHudMenuQuery;
    private static bool _queriesReady;

    [HarmonyPrefix]
    private static bool Prefix()
    {
        try
        {
            // ShouldBlockMenus (NOT ShouldBlock): chat typing must not block menus/escape.
            if (!InputSuppression.ShouldBlockMenus()) return true;

            // Drain pending menu-open requests so they don't fire on unblock.
            try
            {
                var em = Plugin.EntityManager;
                if (!_queriesReady)
                {
                    _openMenuEventQuery = em.CreateEntityQuery(ComponentType.ReadOnly(Il2CppType.Of<OpenMenuEvent>()));
                    _goToHudMenuQuery = em.CreateEntityQuery(ComponentType.ReadOnly(Il2CppType.Of<GoToHUDMenu>()));
                    _queriesReady = true;
                }
                // DestroyEntity(query) is a no-op when the query is empty.
                em.DestroyEntity(_openMenuEventQuery);
                em.DestroyEntity(_goToHudMenuQuery);
            }
            catch
            {
                _queriesReady = false; // rebuild next frame (e.g. after a world reload)
            }

            InputSuppression.Diag("blocking OpenHUDMenuSystem.OnUpdate + draining OpenMenuEvent/GoToHUDMenu (panel open).");
            return false;
        }
        catch
        {
            return true;
        }
    }
}

// 0.17.0: the radial Action Wheel is its OWN system, NOT routed through
// OpenHUDMenuSystem, so it slipped past the menu block and opened while typing in
// chat (friend-test: a modifier key — e.g. Ctrl, which the user wants free for
// Ctrl+A select-all in the field — popped this wheel). Skip its OnUpdate while the
// menu block is active so the wheel can't open mid-type. Safe: when not blocking it
// runs normally; any exception falls through to running the original.
[HarmonyPatch(typeof(ActionWheelSystem), nameof(ActionWheelSystem.OnUpdate))]
public static class ActionWheelSuppressionPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        try
        {
            if (!InputSuppression.ShouldBlockMenus()) return true;
            InputSuppression.Diag("blocking ActionWheelSystem.OnUpdate (typing/panel open).");
            return false;
        }
        catch
        {
            return true;
        }
    }
}
