using Raphael.Utils;

namespace Raphael.Services.Nyar;

// Central gate for Nyarlathotep diagnostic detail (sibling of Services/Faust/FaustDiag).
//
// ON when EITHER the Nyarlathotep → Settings "Enable diagnostic details" toggle is set, OR Raphael's
// global DiagnosticMode is active. Drives the verbose [Nyar][diag] wire trace in LogOutput.log.
// Per-MESSAGE only — do NOT call from per-frame code.
internal static class NyarDiag
{
    public static bool Enabled => Config.Settings.NyarDiagnostics || Config.Settings.DiagnosticMode;

    /// <summary>Log an inbound raw [NYAR:*] line (the server -> client trace).</summary>
    public static void LogIn(string raw)
    {
        if (!Enabled || string.IsNullOrEmpty(raw)) return;
        LogUtils.LogInfo($"[Nyar][diag] << {raw}");
    }

    /// <summary>Log a detection/handshake or outbound event.</summary>
    public static void Log(string msg)
    {
        if (!Enabled || string.IsNullOrEmpty(msg)) return;
        LogUtils.LogInfo($"[Nyar][diag] {msg}");
    }
}
