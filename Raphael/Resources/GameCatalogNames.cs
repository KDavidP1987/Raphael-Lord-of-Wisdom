using System;
using ProjectM;
using Raphael.Utils;
using Stunlock.Core;          // AssetGuid, PrefabGUID
using Stunlock.Localization;  // Localization.LocalizedStrings
using Unity.Entities;

namespace Raphael.Resources;

// 0.74.0: resolves the unit and object reference's display names in the player's own game language (PLAN C2).
// RUNTIME ONLY (not linked by the logic tests): it touches the game's localization table and ECS.
//
//   units    the row's BloodCraftUI localization key → Localization.LocalizedStrings (as VBloodLocalization does)
//   objects  PrefabCollectionSystem._PrefabGuidToEntityMap → the prefab's BlueprintData.Guid →
//            ManagedDataSystem.ManagedDataRegistry.GetOrDefault<ManagedBlueprintData>(guid).Name.Key →
//            Localization.LocalizedStrings. Eclipse (a client mod) reads the registry the same way
//            (LearningMods/Eclipse-main VExtensions.GetExistingDataManaged). The plan names
//            GameDataManager.ManagedDataRegistry; ManagedDataSystem holds the same registry and needs no
//            GameDataManager handle. UNVERIFIED IN-GAME (plan V3): every step is wrapped, a failure keeps the
//            readable fallback name and adds to the one counted debug line.
//
// Main thread only: a ticker on CoreUpdateBehavior.Actions resolves 200 rows per frame, bumps
// GameCatalog.Revision after each batch (open views re-query on their next refresh) and removes itself when done.
// Results are cached for the session (a resolved row keeps its name; a later Start() resumes where it left off).
public static class GameCatalogNames
{
    private const int BatchSize = 200;
    private const float WaitRetrySeconds = 2f;

    private static bool _running;
    private static bool _done;
    private static int _next;
    private static float _waitUntil;
    private static bool _loggedSummary;
    private const float BumpIntervalSeconds = 1f;
    private const int MaxBatchFaults = 5;
    private static bool _pendingBump;
    private static float _lastBump;
    private static int _batchFaults;
    private static Action _ticker;

    // counted diagnostics (one debug line per session)
    private static int _unitsResolved, _unitsMissing, _objectsResolved, _objNoEntity, _objNoBlueprint, _objNoData, _objNoText, _faults;
    private static string _firstFault;

    /// <summary>True once every row has been tried this session.</summary>
    public static bool Done => _done;

    /// <summary>Start (or resume) the background resolution. Safe to call on every panel open.</summary>
    public static void Start()
    {
        if (_done || _running) return;
        GameCatalog.Warn ??= m => LogUtils.LogWarning(m);
        _ticker ??= Tick;
        _running = true;
        Behaviors.CoreUpdateBehavior.Actions.Add(_ticker);
    }

    private static void Stop()
    {
        _running = false;
        if (_ticker != null) Behaviors.CoreUpdateBehavior.Actions.Remove(_ticker);
    }

    private static void Tick()
    {
        try
        {
            if (UnityEngine.Time.realtimeSinceStartup < _waitUntil) return;
            if (Plugin.IsClientNull() || !LocalizationReady())
            {
                _waitUntil = UnityEngine.Time.realtimeSinceStartup + WaitRetrySeconds;   // not in a world yet
                return;
            }
            var rows = GameCatalog.Rows;
            var ctx = new ObjectContext();
            int end = Math.Min(rows.Count, _next + BatchSize);
            for (; _next < end; _next++)
            {
                var r = rows[_next];
                if (r.NameSource == NameSource.GameText) continue;   // cached from an earlier pass
                if (!r.IsUnit && !Prepare(ref ctx))
                {
                    // The world's systems are not there (yet, or any more): wait and retry this row, don't skip it.
                    _waitUntil = UnityEngine.Time.realtimeSinceStartup + WaitRetrySeconds;
                    break;
                }
                string name = r.IsUnit ? UnitName(r) : ObjectName(r, ref ctx);
                if (name != null && GameCatalog.SetResolvedName(r, name)) _pendingBump = true;
            }
            _batchFaults = 0;
            if (_next >= rows.Count) { Finish(); return; }
            // Coalesce: the views rebuild their rows on a revision change, so bump at most once a second.
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (_pendingBump && now - _lastBump >= BumpIntervalSeconds)
            {
                _pendingBump = false;
                _lastBump = now;
                GameCatalog.BumpRevision();
            }
        }
        catch (Exception ex)
        {
            // A fault outside a single row (e.g. the world went away mid-batch): wait and retry; give up on
            // this session only after several faults in a row.
            Note(ex);
            if (++_batchFaults >= MaxBatchFaults) { Finish(); return; }
            _waitUntil = UnityEngine.Time.realtimeSinceStartup + WaitRetrySeconds;
        }
    }

    /// <summary>Forget this session's progress (UI teardown, server change). Names already resolved stay cached
    /// on their rows and are skipped on the next pass.</summary>
    public static void Reset()
    {
        Stop();
        _done = false;
        _next = 0;
        _waitUntil = 0f;
        _batchFaults = 0;
        _pendingBump = false;
        _loggedSummary = false;
        _unitsResolved = _unitsMissing = _objectsResolved = _objNoEntity = _objNoBlueprint = _objNoData = _objNoText = _faults = 0;
        _firstFault = null;
    }

    private static void Finish()
    {
        _done = true;
        Stop();
        _pendingBump = false;
        GameCatalog.BumpRevision();
        if (_loggedSummary) return;
        _loggedSummary = true;
        LogUtils.LogDebug($"GameCatalog names: units {_unitsResolved} from game text, {_unitsMissing} kept the prefab name; " +
                          $"objects {_objectsResolved} from game text, kept the prefab name: {_objNoEntity} no prefab entity, " +
                          $"{_objNoBlueprint} no blueprint, {_objNoData} no blueprint data, {_objNoText} no text; " +
                          $"{_faults} fault(s){(_firstFault != null ? " (first: " + _firstFault + ")" : "")}.");
    }

    private static void Note(Exception ex)
    {
        _faults++;
        _firstFault ??= ex.GetType().Name + ": " + ex.Message;
    }

    private static bool LocalizationReady()
    {
        try { return Localization.LocalizedStrings != null && Localization.LocalizedStrings.Count > 0; }
        catch { return false; }
    }

    private static string Text(string assetGuid)
    {
        if (string.IsNullOrEmpty(assetGuid)) return null;
        try { return Text(AssetGuid.FromString(assetGuid)); }
        catch (Exception ex) { Note(ex); return null; }
    }

    private static string Text(AssetGuid key)
    {
        try { return Localization.LocalizedStrings.TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s) ? s : null; }
        catch (Exception ex) { Note(ex); return null; }
    }

    private static string UnitName(CatalogRow r)
    {
        string s = Text(r.LocKey);
        if (s != null) _unitsResolved++; else _unitsMissing++;
        return s;
    }

    // Per-batch handles, fetched once per frame.
    private struct ObjectContext
    {
        public bool Tried, Ok;
        public EntityManager Em;
        public PrefabCollectionSystem Prefabs;
        public ManagedDataRegistry Registry;
    }

    private static bool Prepare(ref ObjectContext ctx)
    {
        if (ctx.Tried) return ctx.Ok;
        ctx.Tried = true;
        try
        {
            ctx.Em = Plugin.EntityManager;
            var world = ctx.Em.World;
            if (world == null || !world.IsCreated) return false;
            ctx.Prefabs = world.GetExistingSystemManaged<PrefabCollectionSystem>();
            var managed = world.GetExistingSystemManaged<ManagedDataSystem>();
            ctx.Registry = managed?.ManagedDataRegistry;
            ctx.Ok = ctx.Prefabs != null && ctx.Registry != null;
        }
        catch (Exception ex) { Note(ex); ctx.Ok = false; }
        return ctx.Ok;
    }

    private static string ObjectName(CatalogRow r, ref ObjectContext ctx)
    {
        try
        {
            var guid = new PrefabGUID(r.Guid);
            if (!ctx.Prefabs._PrefabGuidToEntityMap.TryGetValue(guid, out var entity) || entity == Entity.Null || !ctx.Em.Exists(entity))
            { _objNoEntity++; return null; }

            // The blueprint GUID: the prefab's own BlueprintData.Guid when it has one (TM_ buildables), else the
            // row's own GUID, which the registry also answers for many world props (Dynamic_, Chain_, Resource_).
            var bpGuid = guid;
            bool hasBlueprint = ctx.Em.HasComponent<BlueprintData>(entity);
            if (hasBlueprint)
            {
                var bp = ctx.Em.GetComponentData<BlueprintData>(entity);
                if (bp.Guid.GuidHash != 0) bpGuid = bp.Guid;
            }

            var data = ctx.Registry.GetOrDefault<ManagedBlueprintData>(bpGuid);
            if (data == null) { if (hasBlueprint) _objNoData++; else _objNoBlueprint++; return null; }
            string s = Text(data.Name.Key);
            if (s == null) _objNoText++; else _objectsResolved++;
            return s;
        }
        catch (Exception ex) { Note(ex); return null; }
    }
}
