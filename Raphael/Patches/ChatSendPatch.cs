using System;
using System.Reflection;
using HarmonyLib;
using Raphael.Services;
using Raphael.Utils;

namespace Raphael.Patches;

// 0.67.2: notice chat commands the player types in the VANILLA chat box, so their replies/errors are attributed to
// the player (OutboundLedger) instead of being mistaken for Raphael's background noise. Hooks the game's one chat
// send helper, ProjectM.UI.ChatHelper.SendChatMessageOfType(EntityManager, string text, ChatMessageType, NetworkId):
// static and only called when a message is actually sent, so it's cheap. Raphael's own sends build the network
// event directly (MessageService) and never pass through here.
//
// Fail-soft: the target is resolved at runtime. If a game update renames it, Prepare() returns false, Harmony skips
// just this class, one warning is logged, and OutboundLedger.VanillaHookInstalled stays false — which turns OFF
// background VCF-error hiding for the session (vanilla-typed commands could otherwise lose their errors).
[HarmonyPatch]
internal static class ChatSendPatch
{
    private static MethodBase _target;

    private static bool Prepare()
    {
        try
        {
            // Exact overload (EntityManager, string text, ChatMessageType, NetworkId); fallback: the chat input's
            // end-edit handler (string text), which also sees the typed line.
            _target ??= AccessTools.Method(typeof(ProjectM.UI.ChatHelper), "SendChatMessageOfType", new[]
            {
                typeof(Unity.Entities.EntityManager), typeof(string),
                typeof(ProjectM.Network.ChatMessageType), typeof(ProjectM.Network.NetworkId),
            });
            _target ??= AccessTools.Method(typeof(ProjectM.UI.ClientChatSystem), "_OnInputEndEdit", new[] { typeof(string) });
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"ChatSendPatch lookup: {ex.Message}");
            _target = null;
        }
        if (_target == null)
        {
            LogUtils.LogWarning("[compat] Vanilla chat-send hook NOT applied (no chat send method found) — " +
                                "command errors from Raphael's background queries will show in chat this session.");
            return false;
        }
        return true;
    }

    private static MethodBase TargetMethod() => _target;

    // Harmony calls Cleanup after attaching each target (original != null) and once more at the end (original ==
    // null). Mark the hook live only on a successful attach; return null so an attach failure never propagates.
    private static Exception Cleanup(MethodBase original, Exception ex)
    {
        if (original == null) return null;
        if (ex == null)
        {
            OutboundLedger.VanillaHookInstalled = true;
            LogUtils.LogInfo($"[compat] Vanilla chat-send hook APPLIED on {original.DeclaringType?.Name}.{original.Name} (typed commands are attributed to you).");
        }
        else
        {
            LogUtils.LogWarning($"[compat] Vanilla chat-send hook failed to apply: {ex.Message}");
        }
        return null;
    }

    [HarmonyPrefix]
    private static void Prefix(string text)
    {
        try { OutboundLedger.RecordUser(text, "vanilla chat"); }
        catch (Exception ex) { LogUtils.LogDebug($"ChatSendPatch: {ex.Message}"); }
    }
}
