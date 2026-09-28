using System;
using Raphael.Utils;

namespace Raphael.Services;

// 0.67.2: who sent the most recent chat command — Raphael in the background, or the player?
// Used to decide whether a reply is chat noise Raphael caused (hide) or something the player asked for (show):
//   • VCF error / "command not found" lines are hidden only when a Background command went out recently and the
//     player sent nothing recently (MessageService_Processing.ShouldHideCommandFrameworkNoise).
//   • [NYAR:*] / [FAUST:*] / [BEELZ:*] / [URIEL:*] replies pass through to chat for a short window after the player
//     sent a command to that mod (ClientChatPatch.ShouldShowProtocolLine).
// Only timestamps are kept, tracked independently, so a background send never overwrites the player's activity.
// Background = MessageService.EnqueueMessageSilent (every mod client's api reads + Raphael auto-refreshes).
// User = MessageService.EnqueueMessage (buttons), SendChat dot-commands (Raphael chat input), and the vanilla chat
// send hook (Patches/ChatSendPatch). Eclipse handshake frames (SendRaw*) are not commands and aren't recorded.
internal static class OutboundLedger
{
    public enum Mod { Other, Beelz, Uriel, Faust, Nyar }

    public const float VcfWindowSeconds = 5f;
    public const float UserProtocolWindowSeconds = 10f;

    private static float _lastBackground = float.NegativeInfinity;
    private static float _lastUser = float.NegativeInfinity;
    private static readonly float[] _lastUserByMod =
    {
        float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity,
    };

    /// <summary>True once the vanilla chat send hook installed. While false, vanilla-typed commands can't be
    /// attributed, so background VCF suppression fails open (see <see cref="VcfSuppressionAllowed"/>).</summary>
    public static bool VanillaHookInstalled;

    private static float Now => UnityEngine.Time.realtimeSinceStartup;

    public static Mod ModOf(string text)
    {
        if (string.IsNullOrEmpty(text)) return Mod.Other;
        var t = text.TrimStart();
        if (StartsCmd(t, ".nyar"))  return Mod.Nyar;
        if (StartsCmd(t, ".faust")) return Mod.Faust;
        if (StartsCmd(t, ".beelz")) return Mod.Beelz;
        if (StartsCmd(t, ".uriel")) return Mod.Uriel;
        return Mod.Other;
    }

    private static bool StartsCmd(string t, string root)
        => t.StartsWith(root, StringComparison.OrdinalIgnoreCase)
           && (t.Length == root.Length || char.IsWhiteSpace(t[root.Length]));

    public static void RecordBackground(string text)
    {
        if (!IsCommand(text)) return;
        _lastBackground = Now;
    }

    public static void RecordUser(string text, string source)
    {
        if (!IsCommand(text)) return;
        float now = Now;
        _lastUser = now;
        var mod = ModOf(text);
        _lastUserByMod[(int)mod] = now;
        if (mod != Mod.Other && AnyDiagnosticsOn())
            LogUtils.LogInfo($"[Chat] you sent ({source}): {text.Trim()}");
    }

    public static bool UserSentRecently(float seconds) => Now - _lastUser < seconds;
    public static bool BackgroundSentRecently(float seconds) => Now - _lastBackground < seconds;
    public static bool UserSentToModRecently(Mod mod, float seconds) => Now - _lastUserByMod[(int)mod] < seconds;

    /// <summary>Background VCF suppression only works when every player-typed command can be attributed.</summary>
    public static bool VcfSuppressionAllowed => VanillaHookInstalled;

    public static void Reset()
    {
        _lastBackground = float.NegativeInfinity;
        _lastUser = float.NegativeInfinity;
        for (int i = 0; i < _lastUserByMod.Length; i++) _lastUserByMod[i] = float.NegativeInfinity;
    }

    private static bool IsCommand(string text)
        => !string.IsNullOrEmpty(text) && text.TrimStart().StartsWith(".", StringComparison.Ordinal);

    private static bool AnyDiagnosticsOn()
        => Config.Settings.DiagnosticMode || Config.Settings.NyarDiagnostics || Config.Settings.FaustDiagnostics
           || Config.Settings.BeelzDiagnostics || Config.Settings.UrielDiagnostics;
}
