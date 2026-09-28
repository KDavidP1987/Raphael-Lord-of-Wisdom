using System;
using Raphael.Services;
using Raphael.Utils;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using ProjectM.UI;
using Unity.Collections;
using Unity.Entities;

namespace Raphael.Patches;

// Fires UI bring-up once the player's character HUD is alive, and feeds
// LocalCharacter / LocalUser to the rest of the mod as soon as the client
// data system surfaces them.
//
// PORT REFERENCE: LearningMods/BloodCraftUI-master/BloodCraftUI/Patches/InitializationPatch.cs
[HarmonyPatch]
public static class InitializationPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharacterHUDEntry), nameof(CharacterHUDEntry.Awake))]
    private static void CharacterHUDEntry_Awake_Postfix()
    {
        try
        {
            // 0.18.1: do NOT early-out on IsInitialized here. On a relog the UI was already built
            // in a prior session (UIManager persists, DontDestroyOnLoad) but was hidden on logout —
            // UIOnInitialize now routes that case to RestoreAfterRelogIfNeeded to re-show it. Only
            // log the "creating" line for the genuine first-build.
            if (Plugin.UIManager == null) return;
            if (!Plugin.UIManager.IsInitialized)
                LogUtils.LogInfo("Creating Raphael UI...");
            Plugin.UIOnInitialize();
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"InitializationPatch failed: {ex}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CommonClientDataSystem), nameof(CommonClientDataSystem.OnUpdate))]
    private static void CommonClientDataSystem_OnUpdate_Postfix(CommonClientDataSystem __instance)
    {
        // LOGOUT CRASH FIX (0.18.1): this per-frame postfix on a HOT client system used to have NO
        // top-level try/catch. On logout the client world is disposed but this system can still tick
        // against it; a throw from `__instance.World` / the entity queries (`ToEntityArray`) then
        // escaped into the IL2CPP `CommonClientDataSystem.OnUpdate` caller (which can't unwind managed
        // exceptions) and crashed the client to desktop instead of returning to the main menu
        // (Raphael-only; same interop-fault class as the 0.16 crash). The TRY/CATCH below IS the fix — it
        // keeps any teardown-time throw from reaching the engine.
        //
        // DO NOT add a `_client` / IsClientNull early-return here: this postfix is the ONLY caller of
        // GameDataOnInitialize, which is the ONE place `_client` is first set (Plugin.cs). Gating on
        // it would stop init from ever running on login — a deadlock that blanks every data-driven
        // overlay (the 0.18.1-internal regression this corrects). `_client` is also never reset to
        // null on disconnect, so such a guard wouldn't have helped the logout case anyway.
        try
        {
            if (Plugin.UIManager == null || !Plugin.UIManager.IsInitialized) return;
            // LOGOUT CRASH FIX: skip while the client world is torn down on logout — the
            // ToEntityArray calls below on a disposing world native-crash (uncatchable). Mirrors
            // Eclipse's GameDataManager guard. Safe to gate on World.IsCreated here (unlike a
            // _client/IsClientNull guard, which would deadlock — this is where _client first gets set):
            // on a live login tick World.IsCreated is true, so init still runs and binds the player.
            if (__instance.World == null || !__instance.World.IsCreated) return;
            Plugin.GameDataOnInitialize(__instance.World);

            // Try to capture LocalUser entity from the first matching query.
            var entities = __instance.__query_1840110770_0.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var e in entities)
                {
                    if (e.Has<LocalUser>())
                    {
                        MessageService.SetUser(e);
                        break;
                    }
                }
            }
            finally { entities.Dispose(); }

            // Try to capture LocalCharacter entity from the second matching query.
            entities = __instance.__query_1840110770_1.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var e in entities)
                {
                    if (e.Has<LocalCharacter>())
                    {
                        Plugin.LocalCharacter = e;
                        MessageService.SetCharacter(e);
                        break;
                    }
                }
            }
            finally { entities.Dispose(); }
        }
        catch (Exception ex)
        {
            // Swallow — a throw here used to crash the client to desktop on logout. Logged (rate
            // is fine: this only fires if something's actually wrong, e.g. mid-disconnect).
            LogUtils.LogDebug($"CommonClientDataSystem postfix skipped (likely disconnect/teardown): {ex.Message}");
        }
    }

    // THE logout crash fix. When the player leaves the game, ProjectM destroys the
    // ClientBootstrapSystem — the SAFE teardown signal Eclipse itself patches (its
    // InitializationPatches hooks this exact method), and the Player.log shows it firing as a
    // clean step. The crash was NOT any one patch: removing the IsClientNull deadlock left
    // MessageService initialized, which woke up ALL of Raphael's per-frame ECS code (services on
    // CoreUpdateBehavior + the chat/data patches); during teardown that code reads the disposed
    // world and NATIVE-crashes (uncatchable; no managed trace in the log). Fix: the instant the
    // client bootstrap is torn down, reset Raphael's session state so every per-frame gate fails and
    // the whole mod goes dormant for the rest of teardown — exactly the (accidental) dormant state
    // that made the earlier build not crash, but triggered cleanly here. PURE field resets: no UI,
    // no ECS, no GameObject work, so this prefix itself can't crash. Re-inits on the next login.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ClientBootstrapSystem), nameof(ClientBootstrapSystem.OnDestroy))]
    private static void ClientBootstrapSystem_OnDestroy_Prefix()
    {
        try
        {
            LogUtils.LogInfo("[Raphael] Left the game (ClientBootstrapSystem destroyed) — resetting session state.");
            MessageService.Destroy();        // _isInitialized = false → per-frame services gating on it go dormant
            EclipseProtocolService.Reset();  // clear registration so we re-register on relog
            Services.Beelzebub.BeelzProtocolService.Reset(); // clear Beelz detection (DetectionGaveUp) + state so a server-switch re-detects
            Services.Uriel.UrielProtocolService.Reset(); // 0.26: same, for Uriel — re-detect on relog/server-switch
            Services.Faust.FaustProtocolService.Reset(); // same, for Faust — re-detect + drop query state on relog/server-switch
            Services.Nyar.NyarProtocolService.Reset();   // same, for Nyarlathotep — re-detect + re-subscribe on relog/server-switch
            Services.Uriel.UrielBuildMode.Reset(); // 0.26: build-mode hotkeys always start OFF on a new session
            Services.ChatRelayService.Clear(); // 0.18.3: drop the previous server's chat scrollback/compose state (PURE static-collection clear — safe in teardown; the chat WINDOW is repainted on relog via ResetForServerSwitch)
            // 0.18.4 CRASH FIX (server-switch): drop cached ECS EntityQueries created in THIS (now-disposing)
            // client world. The world is recreated on the next join; reusing an old-world query native-crashes
            // (ToEntityArray/DestroyEntity) — the confirmed cause of "open Raphael after a server-switch → crash".
            // PURE field resets (just clear the "ready" flags); the queries rebuild against the new world on use.
            InputSuppression.OnWorldTeardown();
            TypingInputLock.OnWorldTeardown();   // 0.25.0: drop the input-context refs (the dying world's InputActionSystem takes the registration with it)
            Services.PlayerRosterService.OnWorldTeardown();
            Services.Uriel.SharedContainerDetector.OnWorldTeardown(); // 0.26: drop the shared-container query for the new world
            Core.Reset();                    // release Core's client world
            Plugin.ResetGameData();          // null Plugin._client (IsClientNull()=true) + allow re-bind on relog
            // 0.18.1: queue the UI hide so overlays/launcher don't linger over the main menu. PURE
            // flag set — the actual SetActive(false) runs on the next CoreUpdateBehavior tick (in the
            // main menu, after teardown), never inside this teardown hook. Re-shown on relog.
            Plugin.UIManager?.RequestHideForLogout();
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"ClientBootstrapSystem OnDestroy reset failed: {ex}");
        }
    }
}
