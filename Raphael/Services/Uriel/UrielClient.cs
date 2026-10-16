using System.Globalization;
using Raphael.Services;
using Raphael.Utils;

namespace Raphael.Services.Uriel;

// The ONLY layer that talks to the Uriel server mod. Issues `.uriel …` chat commands via
// MessageService. No parsing happens here. Sibling of Services/Beelzebub/BeelzClient.
//
// Reads (`.uriel api …`) go out SILENT (EnqueueMessageSilent): their replies are [URIEL:*] lines
// that ClientChatPatch intercepts + destroys before they reach chat anyway, and the echo shouldn't
// clutter chat. Player-initiated MUTATIONS go out VISIBLE (EnqueueMessage) so the user sees Uriel's
// human-text confirmation ("<chest>: PUBLIC …", cost receipts, etc.).
//
// ⚠ TARGETING (handoff §3): every relay that acts on a WORLD object MUST append the `nearest` token
// so Uriel targets the closest object to the PLAYER, not wherever the aim ray happens to point after
// a UI click. This layer bakes that in so individual buttons can't forget it. `nearest` is a
// stackable token anywhere in `share`'s modifier list, and a trailing arg for the others; after a
// `stairswap <style>` it comes last.
internal static class UrielClient
{
    // The six same-shape stair styles `.uriel stairswap` accepts (others rejected with a hint).
    public static readonly string[] StairStyles =
    {
        "stone1", "stone2", "stone3", "gloomrot", "projectk", "strongblade",
    };

    // ---- read API (machine-readable [URIEL:*] replies) ----
    public const string API_VERSION = ".uriel api version";

    /// <summary>Send a machine read / silent auto-refresh.</summary>
    public static void Send(string cmd) { DiagLogOut(cmd, silent: true); MessageService.EnqueueMessageSilent(cmd); }

    /// <summary>Send a player-initiated mutation (reply visible in chat).</summary>
    public static void SendUser(string cmd) { DiagLogOut(cmd, silent: false); MessageService.EnqueueMessage(cmd); }

    internal static void DiagLogOut(string cmd, bool silent)
    {
        if (!UrielDiag.Enabled) return;
        LogUtils.LogInfo($"[Uriel][diag] >> {(silent ? "(read) " : "(cmd)  ")}{cmd}");
    }

    // ---- read helpers ----
    public static void RequestVersion()             => Send(API_VERSION);
    // Object-spawn collection (ApiVersion 1), paginated (1-based pages, ≤20 objects/page since Uriel's
    // 2026-06-08 one-reply-per-line fix; Raphael follows page/total from [URIEL:end] and is page-size-agnostic).
    public static void RequestCatalog(int page = 1)  => Send($".uriel api catalog {Num(page)}");
    public static void RequestUnlocked(int page = 1) => Send($".uriel api unlocked {Num(page)}");

    // ---- object spawning (player) ----
    // Handoff §6: `.uriel spawn <guid> [rot] [breakable] [here]` places at the aim point by default,
    // or at the PLAYER'S location with the `here` token. From a Raphael UI button the cursor is on the
    // panel, so the aim ray points outside the plot and aim-placement fails ("…can only be placed in a
    // castle plot") — so Raphael MUST append `here`. The rotation int has to come BEFORE the token (a bare
    // `here` would bind to the rotation slot and fail), so we send rotation 0 explicitly. The player
    // then nudges it into place with the Move build-hotkey (bare `.uriel move` = aim/cursor).
    // Spawn at the player's location (the `here` token — required from a UI button; see above), with a
    // durability flag + optional respawn. Handoff §6 lifecycle flags (order-independent AFTER the rotation
    // int, so we always send rotation 0 first):
    //   • indestructible (default) — no flag.
    //   • breakable  — raid/decay can destroy it (still castle-owned; owner can't weapon-smash it).
    //   • smashable  — breakable AND the owner can destroy it (object skips castle adoption; decays;
    //                  anyone in the territory can damage it).
    //   • respawn    — auto-respawns after destruction until the castle is gone or it's .uriel despawn'd
    //                  (only meaningful for breakable/smashable; harmless on indestructible).
    public const string DUR_INDESTRUCTIBLE = "indestructible";
    public const string DUR_BREAKABLE      = "breakable";
    public const string DUR_SMASHABLE      = "smashable";

    public static void Spawn(int guid, string durability = DUR_INDESTRUCTIBLE, bool respawn = false)
    {
        string flags = durability switch
        {
            DUR_BREAKABLE => " breakable",
            DUR_SMASHABLE => " smashable",
            _             => "",          // indestructible = default, no flag
        };
        if (respawn) flags += " respawn";
        SendUser($".uriel spawn {Num(guid)} 0{flags} here");
    }
    public static void SpawnList()       => SendUser(".uriel spawnlist");

    // ---- placed-object management (player, own plot) — these re-resolve the target from Uriel's
    // spawned-object registry on demand (no `nearest` token; they act on the nearest/aimed placed
    // object). Used by the build-mode hotkeys. ----
    public static void Move()    => SendUser(".uriel move");
    public static void Rotate()  => SendUser(".uriel rotate");
    public static void Despawn() => SendUser(".uriel despawn");
    // 0.20.0 (Uriel): flush-to-ground nudges — each click shifts the nearest placed object
    // up/down by Uriel's default step (0.25m, sent argument-less so Uriel supplies it).
    public static void Raise()   => SendUser(".uriel raise");
    public static void Lower()   => SendUser(".uriel lower");
    public static void Notify(bool on)   => SendUser($".uriel notify {(on ? "on" : "off")}");
    // Item-name -> numeric id search (≤8 results), for the cost-item picker.
    public static void FindItem(string name) => SendUser($".uriel finditem {name}");

    // ---- storage / prison sharing (player) — all targeted via `nearest` ----
    // share accepts `nearest` as a stackable token anywhere in the modifier list; we put it first
    // and append the caller's modifier string (e.g. "permission take limithours 24").
    public static void Share(string modifiers = null)
        => SendUser(string.IsNullOrEmpty(modifiers) ? ".uriel share nearest" : $".uriel share nearest {modifiers}");
    public static void Unshare()      => SendUser(".uriel unshare nearest");
    public static void UnshareMine()  => SendUser(".uriel unsharemine");
    public static void Shared()       => SendUser(".uriel shared");
    public static void Info()         => SendUser(".uriel info nearest");
    public static void PayChest()     => SendUser(".uriel paychest nearest");
    public static void TakePrisoner() => SendUser(".uriel takeprisoner nearest");

    // ---- stair swap (player) — `nearest` is a trailing arg, after the style for stairswap ----
    public static void StairSwap(string style) => SendUser($".uriel stairswap {style} nearest");
    public static void StairNext()             => SendUser(".uriel stairswap next nearest");
    public static void RemoveStairs()          => SendUser(".uriel removestairs nearest");
    public static void ShowStairStyles()       => SendUser(".uriel stairstyles nearest");

    // ---- Castle Events (Uriel 0.21.0, alpha; RAPHAEL_CASTLE_EVENTS_HANDOFF.md) ----
    // Every `.uriel event|squad|floor|template|unit …` command goes through SendEvent: it refuses a line
    // the game would truncate (handoff §2 — the cut-off tail silently falls back to defaults, which could
    // spawn the wrong units) and opens UrielEvents' reply window so the tab can echo the answer.
    // Layout commands act on the plot the caller STANDS in and the door within 6 m — no `nearest` token.

    /// <summary>Send a Castle Events command (visible). Returns false, without sending, when it is too long.</summary>
    public static bool SendEvent(string cmd)
    {
        cmd = (cmd ?? "").Trim();
        if (cmd.Length == 0) return false;
        if (cmd.Length > UrielEvents.CHAT_LIMIT)
        {
            UrielEvents.NoteRefusedLocally(cmd,
                $"Not sent: {cmd.Length} characters, and the game cuts chat at about 120. Use shorter unit names " +
                "(drop the CHAR_ prefix), GUIDs, or add long unit lists in parts with Extend.");
            return false;
        }
        UrielEvents.NoteSent(cmd);
        SendUser(cmd);
        return true;
    }

    /// <summary>Availability probe (handoff §8): the disabled / admin-only refusals tell the tab what to show.</summary>
    public static void ProbeEvents()
    {
        UrielEvents.NoteProbe();
        Send(".uriel event list");
    }

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
}
