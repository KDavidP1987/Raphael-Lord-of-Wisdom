using System;
using System.Collections.Generic;
using Raphael.Utils;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Raphael.Services;

// 0.17.0 / 0.17.3: enumerate online players for the whisper picker + whisper-by-name.
//
// PRIMARY source (0.17.3, #38): the client's `UserInfoElement` buffer, held on the
// `UserInfoBufferSingleton` entity. This is the same server-pushed list the in-game
// social/clan page reads — it is NOT spatially culled, so it contains EVERY connected
// player (matching "in the base game you can whisper anybody on the server"). Each
// element carries Name + NetworkId (the whisper target) + IsConnected + PlatformId
// (self-exclusion).
//
// FALLBACK (0.17.0): a query over `User` entities. The client only replicates User
// entities near the player (spatial culling), so this yields just nearby players —
// used only if the UserInfoElement buffer is unavailable/empty.
internal static class PlayerRosterService
{
    internal readonly struct PlayerRef
    {
        public readonly string Name;
        public readonly NetworkId Id;
        public PlayerRef(string name, NetworkId id) { Name = name; Id = id; }
    }

    private static EntityQuery _userQuery;
    private static bool _queryReady;

    // 0.18.4: drop the cached query on leave-game / server-switch (called from the
    // ClientBootstrapSystem.OnDestroy teardown hook). The client world is disposed + recreated on a
    // switch; reusing a query from the old world is a native crash. Pure field reset → rebuilt against
    // the new world on the next GetOnlinePlayers call. Mirrors InputSuppression.OnWorldTeardown.
    internal static void OnWorldTeardown() => _queryReady = false;

    // Online players (excluding self), sorted by name. Prefers the full (non-culled)
    // UserInfoElement roster; falls back to the nearby-only User query. Best-effort:
    // returns empty on failure rather than throwing into the UI.
    internal static List<PlayerRef> GetOnlinePlayers()
    {
        var full = GetConnectedUsers();
        if (full.Count > 0) return full;
        return GetNearbyPlayersFromUserQuery();
    }

    // B1 (note-to-self whisper): the LOCAL player as a whisper target. The rosters above deliberately
    // EXCLUDE self, but the base game lets you whisper your OWN character (it shows as a "note to self"),
    // so the whisper UI needs to resolve self too. Reads the local player's own UserInfoElement (matched by
    // PlatformId) for its current NetworkId. Best-effort; returns false if the roster/self isn't available.
    internal static bool TryGetSelfPlayer(out PlayerRef self)
    {
        self = default;
        try
        {
            if (Plugin.IsClientNull()) return false;
            var em = Plugin.EntityManager;
            ulong selfPlatform = 0;
            try { selfPlatform = MessageService.LocalUser.Read<User>().PlatformId; } catch { return false; }
            if (selfPlatform == 0) return false;
            if (!SingletonAccessor<UserInfoBufferSingleton>.TryGetSingletonEntityWasteful(em, out var singleton)) return false;
            if (singleton == Entity.Null || !em.HasBuffer<UserInfoElement>(singleton)) return false;
            var buf = em.GetBuffer<UserInfoElement>(singleton);
            for (int i = 0; i < buf.Length; i++)
            {
                var ui = buf[i];
                if (ui.PlatformId != selfPlatform) continue;
                var nm = ui.Name.ToString();
                if (string.IsNullOrEmpty(nm)) return false;
                self = new PlayerRef(nm, ui.NetworkId);
                return true;
            }
        }
        catch (Exception ex) { LogUtils.LogDebug($"TryGetSelfPlayer: {ex.Message}"); }
        return false;
    }

    // 0.69.0 (#2): the authoritative NetworkId for one CONNECTED player name (self included), straight from the
    // server-pushed UserInfoElement roster. Quiet (no log line) so it can run per chat message. Whisper sends and
    // the chat-sender pairing use it first, so a name can never resolve to another player's id.
    internal static bool TryFindOnline(string name, out PlayerRef player)
    {
        player = default;
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            if (Plugin.IsClientNull()) return false;
            var em = Plugin.EntityManager;
            if (!SingletonAccessor<UserInfoBufferSingleton>.TryGetSingletonEntityWasteful(em, out var singleton)) return false;
            if (singleton == Entity.Null || !em.HasBuffer<UserInfoElement>(singleton)) return false;
            var buf = em.GetBuffer<UserInfoElement>(singleton);
            // A game-resolved sender may carry a colour tag or quotes; match on the bare name.
            string want = System.Text.RegularExpressions.Regex.Replace(name, "<[^<>]*>", string.Empty).Replace("\"", string.Empty).Trim();
            if (want.Length == 0) return false;
            for (int i = 0; i < buf.Length; i++)
            {
                var ui = buf[i];
                if (!ui.IsConnected) continue;
                var nm = ui.Name.ToString();
                if (!string.Equals(nm, want, StringComparison.OrdinalIgnoreCase)) continue;
                player = new PlayerRef(nm, ui.NetworkId);
                return true;
            }
        }
        catch (Exception ex) { LogUtils.LogDebug($"TryFindOnline: {ex.Message}"); }
        return false;
    }

    // 0.17.3 (#38): the full connected-player roster from the UserInfoElement buffer.
    // The buffer lives on the UserInfoBufferSingleton entity, which V Rising parks on a
    // DISABLED entity — a plain CreateEntityQuery skips disabled entities (we saw
    // singletonEntities=0 in-game). The game's own Stunlock.Core.SingletonAccessor<T>
    // queries with the right options, so we use it (the same call Bloodcraft uses).
    internal static List<PlayerRef> GetConnectedUsers()
    {
        var result = new List<PlayerRef>();
        try
        {
            if (Plugin.IsClientNull()) return result;
            var em = Plugin.EntityManager;

            ulong selfPlatform = 0;
            try { selfPlatform = MessageService.LocalUser.Read<User>().PlatformId; } catch { /* leave 0 */ }

            Entity singleton = Entity.Null;
            bool found = false;
            try { found = SingletonAccessor<UserInfoBufferSingleton>.TryGetSingletonEntityWasteful(em, out singleton); }
            catch (Exception ex) { LogUtils.LogDebug($"GetConnectedUsers accessor: {ex.Message}"); }

            bool hasBuf = found && singleton != Entity.Null && em.HasBuffer<UserInfoElement>(singleton);
            int bufTotal = 0, connected = 0;
            var sample = new List<string>();
            if (hasBuf)
            {
                var buf = em.GetBuffer<UserInfoElement>(singleton);
                bufTotal = buf.Length;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < buf.Length; i++)
                {
                    var ui = buf[i];
                    var nm = ui.Name.ToString();
                    if (sample.Count < 16)
                        sample.Add($"{(string.IsNullOrEmpty(nm) ? "<noname>" : nm)}[conn={ui.IsConnected}]");
                    if (!ui.IsConnected) continue;
                    connected++;
                    if (selfPlatform != 0 && ui.PlatformId == selfPlatform) continue; // skip self
                    if (string.IsNullOrEmpty(nm) || !seen.Add(nm)) continue;
                    result.Add(new PlayerRef(nm, ui.NetworkId));
                }
            }

            // One-line diagnostic (called on whisper actions, not per-frame): confirms
            // whether the client carries the UserInfoElement roster via the accessor.
            LogUtils.LogWarning($"[ChatRoster] UserInfoElement (accessor): foundEntity={found}, hasBuffer={hasBuf}, " +
                $"bufferElems={bufTotal}, connected={connected} -> roster={result.Count}. sample: {string.Join(", ", sample)}");
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"PlayerRosterService.GetConnectedUsers: {ex.Message}");
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    // 0.17.0 fallback: nearby players from the (spatially-culled) User entity query.
    private static List<PlayerRef> GetNearbyPlayersFromUserQuery()
    {
        var result = new List<PlayerRef>();
        try
        {
            if (Plugin.IsClientNull()) return result;
            var em = Plugin.EntityManager;
            if (!_queryReady)
            {
                _userQuery = em.CreateEntityQuery(ComponentType.ReadOnly(Il2CppType.Of<User>()));
                _queryReady = true;
            }

            ulong selfPlatform = 0;
            try { selfPlatform = MessageService.LocalUser.Read<User>().PlatformId; } catch { /* leave 0 */ }

            NativeArray<Entity> users;
            try { users = _userQuery.ToEntityArray(Allocator.Temp); }
            catch { _queryReady = false; return result; } // rebuild query next time (e.g. after world reload)

            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in users)
                {
                    if (!e.Has<User>()) continue;
                    var u = e.Read<User>();
                    var nm = u.CharacterName.ToString();
                    if (!u.IsConnected) continue;
                    if (selfPlatform != 0 && u.PlatformId == selfPlatform) continue; // skip ourselves
                    if (string.IsNullOrEmpty(nm) || !seen.Add(nm)) continue;
                    if (!e.Has<NetworkId>()) continue; // need a whisper target
                    result.Add(new PlayerRef(nm, e.Read<NetworkId>()));
                }
            }
            finally { users.Dispose(); }

            LogUtils.LogDebug($"[ChatRoster] User-query fallback -> {result.Count} nearby player(s).");
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"PlayerRosterService.GetNearbyPlayersFromUserQuery: {ex.Message}");
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }
}
