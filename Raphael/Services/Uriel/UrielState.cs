using System;
using System.Collections.Generic;
using Raphael.Utils;

namespace Raphael.Services.Uriel;

// ---- Typed records the UI binds to (one per api read shape) ----

// [URIEL:object] — one spawnable world-object prefab. Used by BOTH the full catalog
// ("everything that exists to collect") and the per-player unlocked list.
//   Guid  = the spawn id (Raphael fires `.uriel spawn <guid>`); also the PrefabGUID for client-side
//           icon / true-name resolution.
//   Discoverable = 1 if discoverable-by-destruction.
//   Label = a humanized display name (wire-safe; underscores already restored to spaces here).
//   Category = coarse grouping + fallback-icon hint:
//              container|plant|ore|breakable|light|furniture|resource|buildable|decor|other
//              (treat unknown as "decor").
internal sealed record UrielObject(int Guid, bool Discoverable, string Label, string Category)
{
    public string DisplayName => string.IsNullOrEmpty(Label) ? Guid.ToString() : Label;
}

// Cached client model for the Uriel integration. Mirrors BeelzState: static read-only data + a
// change event per slice; UrielProtocolService updates it, the UI binds to it and never parses raw
// wire lines.
internal static class UrielState
{
    // ---- handshake / presence ([URIEL:version]) ----
    public static bool   Present       { get; private set; }   // server has Uriel and ACK'd ready=1
    public static int    ApiVersion    { get; private set; }
    public static string PluginVersion { get; private set; } = "";
    public static bool   Ready         { get; private set; }

    // ---- object-spawn feature meta (from [URIEL:version]) ----
    public static bool   ObjectSpawnEnabled  { get; private set; }   // objectspawn=1
    public static bool   CollectionEnabled   { get; private set; }   // collection=1 master switch
    public static bool   ObjectSpawnAdminOnly { get; private set; }  // adminonly=1
    public static string DiscoveryMode       { get; private set; } = "";  // Discovery|Full
    public static int    DiscoveryChance     { get; private set; }   // 0-100
    public static int    TotalPrefabs        { get; private set; }   // total spawnable prefabs in-game
    public static int    DiscoverablePrefabs { get; private set; }   // size of the discoverable set
    public static int    BlockedPrefabs      { get; private set; }   // blocklist size

    // ---- full catalog ([URIEL:catalog]) — the total prefab list; cache-and-browse only ----
    public static IReadOnlyList<UrielObject> Catalog { get; private set; } = Array.Empty<UrielObject>();
    public static bool CatalogLoaded   { get; private set; }
    public static bool CatalogComplete { get; private set; }   // true only after a full unfiltered paged scan
    /// <summary>Non-empty (the plugin version) when the catalog was warmed from the disk cache rather than
    /// a live scan — drives a "cached — re-scan to refresh" hint. Cleared when a live scan commits.</summary>
    public static string CatalogCacheInfo { get; private set; } = "";

    // ---- the calling player's unlocked prefabs ([URIEL:unlocked]) ----
    public static IReadOnlyList<UrielObject> Unlocked { get; private set; } = Array.Empty<UrielObject>();
    public static bool   UnlockedLoaded { get; private set; }
    public static int    UnlockedCount  { get; private set; }   // n= from the page header
    public static float  UnlockedPct    { get; private set; }   // % of the discoverable set
    public static string UnlockedSteam  { get; private set; } = "";

    // ---- capability gates (per the handoff ApiVersion timeline) ----
    /// <summary>api1 (Uriel): object-spawn wire API (version/catalog/unlocked) present.</summary>
    public static bool SupportsObjectSpawnApi => ApiVersion >= 1;
    /// <summary>Planned: `api info` / `api shares` (single + paged share rows). Not yet shipped
    /// server-side — until then the share UI uses Raphael's `.uriel info` / `.uriel shared` text parse.</summary>
    public static bool SupportsShareApi => false;
    /// <summary>Planned: `api stairinfo`. Until then the stair picker parses `.uriel stairstyles` text.</summary>
    public static bool SupportsStairApi => false;

    // ---- change events ----
    public static event Action PresenceChanged;     // Present / Ready / ApiVersion + version meta
    public static event Action CatalogChanged;      // full catalog scan committed
    public static event Action UnlockedChanged;     // unlocked list committed

    // ---- mutators (called only by UrielProtocolService) ----
    internal static void SetVersion(int api, string plugin, bool ready,
        bool objectSpawn, bool collection, bool adminOnly, string mode, int chance,
        int total, int discoverable, int blocked)
    {
        ApiVersion = api; PluginVersion = plugin ?? ""; Ready = ready;
        if (ready) Present = true;
        ObjectSpawnEnabled = objectSpawn; CollectionEnabled = collection; ObjectSpawnAdminOnly = adminOnly;
        DiscoveryMode = mode ?? ""; DiscoveryChance = chance;
        TotalPrefabs = total; DiscoverablePrefabs = discoverable; BlockedPrefabs = blocked;
        Fire(PresenceChanged);
    }

    /// <summary>0.73.0: the version probe got `[URIEL:err] code=disabled` (Uriel 0.22.0: object spawning or the
    /// whole mod is off). Uriel is there, but its object-spawn API isn't answering.</summary>
    internal static void SetPresentDisabled()
    {
        ApiVersion = 0; PluginVersion = ""; Ready = true; Present = true;
        ObjectSpawnEnabled = false; CollectionEnabled = false;
        Fire(PresenceChanged);
    }

    internal static void SetCatalog(IReadOnlyList<UrielObject> all, int total, int discoverable, string fromCacheVersion = "")
    {
        Catalog = all ?? Array.Empty<UrielObject>();
        CatalogLoaded = Catalog.Count > 0;
        CatalogComplete = true;
        CatalogCacheInfo = fromCacheVersion ?? "";
        if (total > 0) TotalPrefabs = total;
        if (discoverable > 0) DiscoverablePrefabs = discoverable;
        Fire(CatalogChanged);
    }

    internal static void SetUnlocked(IReadOnlyList<UrielObject> all, int count, float pct, string steam, int discoverable)
    {
        Unlocked = all ?? Array.Empty<UrielObject>();
        UnlockedLoaded = true;
        UnlockedCount = count > 0 ? count : Unlocked.Count;
        UnlockedPct = pct;
        UnlockedSteam = steam ?? "";
        if (discoverable > 0) DiscoverablePrefabs = discoverable;
        Fire(UnlockedChanged);
    }

    /// <summary>Clear ALL cached Uriel state. Called on logout (UrielProtocolService.Reset via the
    /// ClientBootstrapSystem.OnDestroy teardown hook) so a relog into a DIFFERENT server starts clean.
    /// PURE field resets — does NOT fire change events (the teardown hook does no UI work; the UI
    /// re-gates on relog when detection re-runs and fires AvailabilityChanged/PresenceChanged).</summary>
    internal static void Reset()
    {
        Present = false; ApiVersion = 0; PluginVersion = ""; Ready = false;
        ObjectSpawnEnabled = false; CollectionEnabled = false; ObjectSpawnAdminOnly = false;
        DiscoveryMode = ""; DiscoveryChance = 0;
        TotalPrefabs = 0; DiscoverablePrefabs = 0; BlockedPrefabs = 0;
        Catalog = Array.Empty<UrielObject>(); CatalogLoaded = false; CatalogComplete = false; CatalogCacheInfo = "";
        Unlocked = Array.Empty<UrielObject>(); UnlockedLoaded = false; UnlockedCount = 0; UnlockedPct = 0f; UnlockedSteam = "";
    }

    private static void Fire(Action evt)
    {
        if (evt == null) return;
        try { evt.Invoke(); }
        catch (Exception ex) { LogUtils.LogError($"UrielState event handler threw: {ex}"); }
    }
}
