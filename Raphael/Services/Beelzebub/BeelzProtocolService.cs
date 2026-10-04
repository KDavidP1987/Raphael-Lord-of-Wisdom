using System;
using System.Collections.Generic;
using Raphael.Utils;
using UnityEngine;

namespace Raphael.Services.Beelzebub;

// Entry point + router for the Beelzebub integration. Modeled on
// EclipseProtocolService (handshake + availability gate), but the wire protocol
// is plaintext [BEELZ:*] System-chat lines (no MAC), routed here from
// ClientChatPatch's [BEELZ: branch — it never touches the regex intercept pipeline.
//
// Responsibilities:
//   • Detect (handshake `.beelz api version`, retry/back-off, gate on ready=1)
//   • Subscribe (`.beelz api bch on`) + Hydrate the initial snapshot
//   • Parse each line and commit multi-record streams on [BEELZ:end]
//   • Apply the [BEELZ:event] push stream (debounced targeted re-fetch)
//   • Expose IsPresent + AvailabilityChanged so MainPanel can gate the tab group
internal static class BeelzProtocolService
{
    // ---- availability gate (read by MainPanel.IsTabGroupAvailable) ----
    public static bool IsPresent => BeelzState.Present;
    public static bool DetectionGaveUp { get; private set; }

    /// <summary>Fired when presence is resolved (handshake ACK or give-up) so the
    /// UI re-checks the Beelzebub tab-group availability in place.</summary>
    public static event Action AvailabilityChanged;

    /// <summary>Fired when the player force-casts an ability (event type=cast) so the
    /// action-bar overlay can start a client-side cooldown ring.</summary>
    public static event Action<string> AbilityCast;

    /// <summary>Fired once when the player collects the whole capturable catalog (Beelz v0.84
    /// `type=collection-complete`): (count, total) = collected / full capturable universe (~1400).
    /// The server also posts a chat milestone; this lets a Raphael panel react (e.g. a 100% badge).</summary>
    public static event Action<int, int> CollectionComplete;

    /// <summary>Fired once per catalog page during a "Scan all" so the UI can update a
    /// lightweight progress label (NOT a list rebuild). The final page fires CatalogChanged.</summary>
    public static event Action ScanProgress;

    /// <summary>Same as ScanProgress but for the ADMIN `abilities-all` scan (Admin: Abilities table).
    /// The final page fires CatalogAllChanged.</summary>
    public static event Action ScanProgressAll;

    // ---- handshake state (mirrors EclipseProtocolService's retry/give-up) ----
    // 0.18.3: hardened for server-switching WITHOUT a full restart (Moonie's repro: leave game →
    // join another server → Beelz not re-detected). On a fresh relog the login window is heavy
    // (thousands of prefabs streaming) and chat/MessageService settle late; the old 4-probe/20s budget
    // could be burned before the server was ready to answer `.beelz api version`. Now we (1) wait a few
    // seconds after detection (re)starts before the first probe, and (2) probe more times over a longer
    // window. _detectStartAt is re-anchored every Reset() so each server-join gets the full budget.
    private const float DETECTION_START_DELAY_SECONDS = 4f;  // let the relog/chat window settle first
    private const float HANDSHAKE_RETRY_AFTER_SECONDS = 4f;
    // 0.18.x server-switch follow-up: widened 8 (~32s) → 12 (~48s) so high-latency / slow-loading joins get
    // more runway before give-up. After give-up the user recovers in one click via the inline Beelzebub
    // diagnostic's "Re-check" (Reset + re-probe) or Settings & Help → Connection → Re-detect.
    private const int   HANDSHAKE_MAX_ATTEMPTS = 12;
    private static float _detectStartAt;                     // when the detection phase first ran (post-reset)
    private static float _handshakeSentAt;
    private static int   _handshakeAttempts;

    // ---- event-driven re-fetch debounce ----
    private const float REFETCH_DEBOUNCE_SECONDS = 0.75f;
    private static float _lastRefetchAt;
    private static bool _needList, _needSlots, _needTransforms, _needActive,
                        _needProgress, _needHotkeys, _needConfig, _needBestiary, _needLocks;
    private static bool _needBarRefresh;   // 0.19: a grant landed → nudge `.beelz refresh` (debounced) if the setting's on

    // On-demand `api info` enrichment — fetches the real DESCRIPTION + cooldown-seconds that
    // the catalog doesn't carry, for the abilities the user is actually looking at. Unlike
    // the removed bulk pass (which fetched EVERY capture at 4/sec and rebuilt the whole list
    // on each reply → 1 FPS), this is fed ONLY by what's currently visible (capped per call),
    // rate-limited, dedup'd, and does NOT trigger a list rebuild — the rows use a DYNAMIC
    // tooltip that reads BeelzState.AbilityInfo live, so descriptions just appear on next hover.
    private static readonly Queue<int>  _infoQueue = new();   // capture indices pending an api info fetch
    private static readonly HashSet<int> _infoSeen = new();   // indices already queued this session
    private const float INFO_FETCH_INTERVAL = 0.2f;           // ~5 reads/sec, drained in Tick
    private static float _lastInfoAt;

    // Parallel queue for abilities we only know by GUID (slotted / hotkey / active-bar abilities that
    // may have no capture index) — drained via `api info-guid` (Beelz v0.84). Same throttle/dedup as the
    // index queue; skips guids already cached. Fixes "No name"/missing-tooltip on non-captured abilities.
    private static readonly Queue<string>  _infoGuidQueue = new();
    private static readonly HashSet<string> _infoGuidSeen = new(StringComparer.Ordinal);

    /// <summary>Queue `api info-guid` fetches for ability GUIDs the UI is showing (slots, hotkeys,
    /// overlay tiles). Bounded + dedup'd; skips guids already cached. Drained rate-limited in Tick.</summary>
    public static void EnrichAbilityInfoByGuid(IEnumerable<string> abilityGuids, int maxToQueue = 24)
    {
        if (abilityGuids == null) return;
        int added = 0;
        foreach (var g in abilityGuids)
        {
            if (added >= maxToQueue) break;
            if (string.IsNullOrEmpty(g) || g == "0") continue;
            if (BeelzState.TryGetAbilityInfo(g, out _)) continue;   // already known
            if (!_infoGuidSeen.Add(g)) continue;
            _infoGuidQueue.Enqueue(g);
            added++;
        }
    }

    /// <summary>Queue api-info fetches for the given capture indices (the rows currently in
    /// view). Bounded per call + dedup'd; drained rate-limited in Tick. Safe to call on every
    /// list rebuild — already-seen indices are skipped.</summary>
    public static void EnrichAbilityInfo(IEnumerable<int> captureIndices, int maxToQueue = 80)
    {
        if (captureIndices == null) return;
        int added = 0;
        foreach (var idx in captureIndices)
        {
            if (added >= maxToQueue) break;
            if (idx < 0 || !_infoSeen.Add(idx)) continue;
            _infoQueue.Enqueue(idx);
            added++;
        }
    }

    // ---- catalog scan ("Scan all" button): the FULL ability matrix, paginated 40/page.

    // ---- catalog scan ("Scan all" button): the FULL ability matrix, paginated 40/page.
    // One scan (~total/40 chat round-trips) gives category/weapons/forms/enabled for every
    // ability — replacing the old per-ability `api info` enrichment that pinned the client
    // at 1 FPS. Self-driving: each page's [BEELZ:end] requests the next; commit on the last.
    private static readonly List<BeelzCatalogAbility> _accCatalog = new();
    // Parallel to _accCatalog: the raw wire BODY of each row, captured during the scan so a completed FULL
    // scan can be persisted to the disk cache (BeelzCatalogCache) and re-parsed verbatim next session.
    private static readonly List<string> _accCatalogRaw = new();
    private static bool _catalogScanning;
    private static int  _catalogTotalExpected;
    // api23+ load filter held across the scan's pages so every paginated re-request stays on the same
    // subset. Null when the scan is unfiltered (the canonical full-matrix scan). See ScanCatalog.
    private static string _catalogFilterKey, _catalogFilterVal;
    public static bool CatalogScanInProgress => _catalogScanning;
    public static int  CatalogScanLoaded => _accCatalog.Count;
    public static int  CatalogScanTotal  => _catalogTotalExpected;
    /// <summary>The active player-scan filter as "key=value", or "" when the scan is the full matrix.
    /// A filtered scan commits a SUBSET to BeelzState.Catalog — callers driving the canonical collection
    /// book / Kind classification should run the unfiltered ScanCatalog().</summary>
    public static string CatalogScanFilter =>
        string.IsNullOrEmpty(_catalogFilterKey) ? "" : $"{_catalogFilterKey}={_catalogFilterVal}";

    // Beelz v0.100: the ADMIN `abilities-all` scope runs through a PARALLEL accumulator + flags so it
    // doesn't clobber the player-collectible catalog (the Bestiary's). The two scans are mutually
    // exclusive — starting one cancels the other — so streamed catalog-ability lines (which carry no
    // scope tag) always belong to exactly one in-flight buffer.
    private static readonly List<BeelzCatalogAbility> _accCatalogAll = new();
    private static readonly List<string> _accCatalogAllRaw = new();
    private static bool _catalogAllScanning;
    private static int  _catalogAllTotalExpected;
    private static string _catalogAllFilterKey, _catalogAllFilterVal;
    public static bool CatalogAllScanInProgress => _catalogAllScanning;
    public static int  CatalogAllScanLoaded => _accCatalogAll.Count;
    public static int  CatalogAllScanTotal  => _catalogAllTotalExpected;
    /// <summary>The active admin-scan filter as "key=value", or "" when unfiltered.</summary>
    public static string CatalogAllScanFilter =>
        string.IsNullOrEmpty(_catalogAllFilterKey) ? "" : $"{_catalogAllFilterKey}={_catalogAllFilterVal}";

    /// <summary>Begin (or restart) a catalog scan. Idempotent restart so a stalled scan can be retried by
    /// clicking again. Commits to BeelzState on the final page. With a filter (api≥27 keys
    /// weapon/cat/unit/form/search/tag/reviewstatus/tier/vblood) the scan pulls just that subset
    /// server-side — much faster than the ~1,700-row full matrix — but commits a SUBSET to
    /// BeelzState.Catalog. Pass no filter for the canonical full scan the collection book relies on.</summary>
    public static void ScanCatalog(string filterKey = null, string filterVal = null)
    {
        if (!BeelzState.Present) return;
        _catalogAllScanning = false; _accCatalogAll.Clear(); _accCatalogAllRaw.Clear();   // cancel any in-flight admin scan
        _accCatalog.Clear(); _accCatalogRaw.Clear();
        _catalogTotalExpected = 0;
        _catalogFilterKey = filterKey; _catalogFilterVal = filterVal;
        _catalogScanning = true;
        BeelzClient.RequestCatalogAbilities(0, _catalogFilterKey, _catalogFilterVal);
    }

    /// <summary>Begin (or restart) the ADMIN `abilities-all` scan (every ability for the config table).
    /// Mutually exclusive with ScanCatalog; commits to BeelzState.SetCatalogAll on the last page. Optional
    /// same filter set as ScanCatalog (e.g. reviewstatus Blocked to show only curated-out rows).</summary>
    public static void ScanCatalogAll(string filterKey = null, string filterVal = null)
    {
        if (!BeelzState.Present) return;
        _catalogScanning = false; _accCatalog.Clear(); _accCatalogRaw.Clear();         // cancel any in-flight player scan
        _accCatalogAll.Clear(); _accCatalogAllRaw.Clear();
        _catalogAllTotalExpected = 0;
        _catalogAllFilterKey = filterKey; _catalogAllFilterVal = filterVal;
        _catalogAllScanning = true;
        BeelzClient.RequestCatalogAbilitiesAll(0, _catalogAllFilterKey, _catalogAllFilterVal);
    }

    // ---- streaming accumulators (committed on the matching [BEELZ:end]) ----
    private static readonly List<BeelzCapture>       _accCaptures   = new();
    private static readonly List<BeelzSlot>          _accSlots      = new();
    private static string                            _accCurrentWeapon = "";
    private static readonly List<BeelzFormSlot>      _accFormSlots  = new();
    private static string                            _accCurrentForm = "";
    private static readonly List<BeelzTransform>     _accTransforms = new();
    private static readonly List<BeelzBestiaryEntry> _accBestiary   = new();
    private static readonly List<BeelzHotkey>        _accHotkeys    = new();
    private static bool _accHotkeysEnabled; private static int _accHotkeysMax;
    private static readonly List<BeelzConfigEntry>   _accConfig     = new();
    private static readonly Dictionary<string, float> _accCooldowns = new();
    private static readonly List<BeelzTxConfig>      _accTxConfig   = new();
    // Beelz v0.100 / api22 structured reads (separate tags → safe to interleave; each commits on its end).
    private static readonly List<BeelzTformAbility>  _accTformKit   = new();
    private static readonly List<BeelzTformBind>     _accTformBinds = new();
    private static readonly List<BeelzBroadcastMsg>  _accBroadcastMsgs = new();
    private static readonly List<BeelzLockGroup>     _accLocks = new();   // Beelz v0.135 / api32

    // ---- chunked-line reassembly (Beelz v0.76 / ApiVersion 15) ----
    // `api info`, `api info-guid`, and `catalog-ability` outgrew VCF's 512-byte reply cap and are
    // now emitted ACROSS MULTIPLE replies: each part repeats the line's id field and carries
    // `part=k/n`. We concatenate the key=value tokens of parts 1..n (keyed by tag+id) and only
    // hand the merged line to the reader once all n parts have arrived. Lines with no `part=` (the
    // common short-ability case, and any pre-v0.76 server) are single and pass straight through.
    private sealed class ChunkBuffer
    {
        public int Total = 1;
        public int Seen;
        public readonly Dictionary<string, string> Fields = new(StringComparer.OrdinalIgnoreCase);
    }
    private static readonly Dictionary<string, ChunkBuffer> _chunks = new(StringComparer.Ordinal);

    // ---- per-frame driver (registered on CoreUpdateBehavior.Actions in Plugin.Load) ----
    public static void Tick()
    {
        if (!MessageService.IsInitialized) return;

        // Detection phase: keep probing until the server ACKs ready=1 or we give up.
        if (!BeelzState.Present)
        {
            if (DetectionGaveUp) return;
            float now = Time.realtimeSinceStartup;
            // 0.18.3: anchor the settle delay on the first ready tick after a (re)start, and hold off
            // the first probe so a heavy relog (scene streaming) + late chat readiness don't waste it.
            if (_detectStartAt <= 0f) { _detectStartAt = now; BeelzDiag.Log($"detection started (settle {DETECTION_START_DELAY_SECONDS}s)"); }
            if (now - _detectStartAt < DETECTION_START_DELAY_SECONDS) return;
            bool first = _handshakeAttempts == 0;
            if (first || now - _handshakeSentAt >= HANDSHAKE_RETRY_AFTER_SECONDS)
            {
                if (_handshakeAttempts >= HANDSHAKE_MAX_ATTEMPTS)
                {
                    DetectionGaveUp = true;
                    LogUtils.LogInfo($"[Beelz] No response to `.beelz api version` after {_handshakeAttempts} probes — Beelzebub not present on this server.");
                    FireAvailability();
                    return;
                }
                _handshakeAttempts++;
                _handshakeSentAt = now;
                BeelzClient.RequestVersion();
                BeelzDiag.Log($"`.beelz api version` probe {_handshakeAttempts}/{HANDSHAKE_MAX_ATTEMPTS} sent");
            }
            return;
        }

        // Live phase: flush any event-driven re-fetches, debounced.
        if (HasPendingRefetch() && Time.realtimeSinceStartup - _lastRefetchAt >= REFETCH_DEBOUNCE_SECONDS)
            FlushRefetch();

        // Drain the on-demand api-info queue, rate-limited. No list rebuild — the dynamic
        // tooltip picks up the description on the next hover. Skips abilities already known.
        if (_infoQueue.Count > 0 && Time.realtimeSinceStartup - _lastInfoAt >= INFO_FETCH_INTERVAL)
        {
            _lastInfoAt = Time.realtimeSinceStartup;
            int idx = _infoQueue.Dequeue();
            foreach (var c in BeelzState.Captures)
                if (c.Index == idx)
                {
                    if (!BeelzState.TryGetAbilityInfo(c.AbilityGuid, out _)) BeelzClient.RequestInfo(idx);
                    break;
                }
        }

        // Drain the guid-keyed queue (api info-guid) — same throttle, for non-captured abilities.
        else if (_infoGuidQueue.Count > 0 && Time.realtimeSinceStartup - _lastInfoAt >= INFO_FETCH_INTERVAL)
        {
            _lastInfoAt = Time.realtimeSinceStartup;
            string guid = _infoGuidQueue.Dequeue();
            if (!BeelzState.TryGetAbilityInfo(guid, out _)) BeelzClient.RequestInfoGuid(guid);
        }
    }

    // Per-frame: fire `.beelz cast <name>` for any LIVE hotkey whose optional keyboard
    // shortcut was pressed this frame. Keybinds are client-side (V Rising won't let a mod
    // bind extra ability keys) and cast via chat, so they're convenient but not frame-perfect.
    // Skipped while typing in chat so a keypress in a text field can't cast. Registered on
    // CoreUpdateBehavior in Plugin.Load. Cheap when no keybinds exist (one count check).
    public static void TickKeybinds()
    {
        if (!MessageService.IsInitialized || !BeelzState.Present || !BeelzState.HotkeysEnabled) return;
        // 0.25.0: ShouldBlock (not just ChatInputActive) — also pauses these binds while the
        // main panel is open with SuppressGameInputWhileUIOpen, matching the game-keybind
        // suppression (movement/abilities) that's already active in that state. Previously a
        // bound key pressed with the panel open fired `.beelz cast` even though every native
        // ability keybind was suppressed.
        if (Raphael.Patches.InputSuppression.ShouldBlock()) return;
        var binds = Raphael.Config.Settings.BeelzKeybinds;
        if (binds.Count == 0) return;
        foreach (var hk in BeelzState.Hotkeys)
        {
            if (string.IsNullOrEmpty(hk.Name)) continue;
            if (binds.TryGetValue(hk.Name, out var key) && key.IsDown())
            {
                try { BeelzClient.Cast(hk.Name); }
                catch (Exception ex) { LogUtils.LogError($"[Beelz] keybind cast '{hk.Name}' failed: {ex}"); }
            }
        }
    }

    /// <summary>0.18.1: reset detection + all streaming/queue state on logout so a relog re-runs the
    /// `.beelz api version` handshake from scratch — CRITICAL when switching servers WITHOUT a full
    /// game restart (the bug this fixes). The static DetectionGaveUp flag from a non-Beelzebub server
    /// otherwise sticks: Tick hits `if (DetectionGaveUp) return;` and never re-probes, so a Beelzebub
    /// server reached via server-switch shows the tab group permanently Unavailable
    /// (IsTabGroupAvailable returns IsPresent || !DetectionGaveUp). Mirrors EclipseProtocolService.Reset.
    /// Called from the ClientBootstrapSystem.OnDestroy teardown hook — PURE field resets, no UI/ECS work
    /// and no event fire (the UI re-gates on relog when detection ACKs and fires AvailabilityChanged).</summary>
    public static void Reset()
    {
        DetectionGaveUp = false;
        _detectStartAt = 0f;     // 0.18.3: re-anchor the settle delay so each server-join gets the full probe budget
        _handshakeSentAt = 0f;
        _handshakeAttempts = 0;

        _lastRefetchAt = 0f;
        _needList = _needSlots = _needTransforms = _needActive =
            _needProgress = _needHotkeys = _needConfig = _needBestiary = _needBarRefresh = _needLocks = false;

        _infoQueue.Clear();
        _infoSeen.Clear();
        _infoGuidQueue.Clear();
        _infoGuidSeen.Clear();
        _lastInfoAt = 0f;

        _accCatalog.Clear(); _accCatalogRaw.Clear();
        _catalogScanning = false;
        _catalogTotalExpected = 0;
        _catalogFilterKey = _catalogFilterVal = null;
        // Admin `abilities-all` scope + structured-read accumulators: also reset on server-switch so a
        // stale subset can't bleed into a new server's scan (the player scan was already reset above).
        _accCatalogAll.Clear(); _accCatalogAllRaw.Clear();
        _catalogAllScanning = false;
        _catalogAllTotalExpected = 0;
        _catalogAllFilterKey = _catalogAllFilterVal = null;
        _accTformKit.Clear();
        _accTformBinds.Clear();
        _accBroadcastMsgs.Clear();
        _accLocks.Clear();

        _accCaptures.Clear();
        _accSlots.Clear();
        _accCurrentWeapon = "";
        _accFormSlots.Clear();
        _accCurrentForm = "";
        _accTransforms.Clear();
        _accBestiary.Clear();
        _accHotkeys.Clear();
        _accHotkeysEnabled = false; _accHotkeysMax = 0;
        _accConfig.Clear();
        _accCooldowns.Clear();
        _accTxConfig.Clear();
        _chunks.Clear();

        BeelzState.Reset();
    }

    // ---- inbound line router (called from ClientChatPatch for every [BEELZ:*] line) ----
    public static void HandleLine(string raw)
    {
        BeelzDiag.LogIn(raw);   // verbose wire trace (gated; off by default)
        var line = BeelzWireParser.Parse(raw);
        if (line == null) return;

        switch (line.Tag.ToLowerInvariant())
        {
            case "version":       OnVersion(line); break;
            case "bch":           OnBch(line); break;

            // streamed record rows -> accumulate, commit on "end"
            case "list":          _accCaptures.Add(ReadCapture(line)); break;
            case "slot":          _accSlots.Add(ReadSlot(line)); break;
            case "form-slot":     _accFormSlots.Add(ReadFormSlot(line)); break;   // Beelz v0.59 per-form bucket
            case "slot-current":  _accCurrentWeapon = line.Get("weapon"); _accCurrentForm = line.Get("form"); break;
            case "tx":            _accTransforms.Add(ReadTransform(line)); break;
            case "bestiary":      _accBestiary.Add(ReadBestiary(line)); break;
            case "hotkeys-config": _accHotkeysEnabled = line.GetBool("enabled"); _accHotkeysMax = line.GetInt("max"); break;
            case "hotkey":        _accHotkeys.Add(ReadHotkey(line)); break;
            case "config":        _accConfig.Add(ReadConfig(line)); break;
            case "cooldown":      _accCooldowns[line.Get("category")] = line.GetFloat("remaining"); break;
            case "tx-config":     _accTxConfig.Add(ReadTxConfig(line)); break;
            case "catalog-ability":
                // Lines carry no scope tag; the in-flight scan (mutually exclusive) decides the buffer.
                if (_catalogAllScanning)
                {
                    var merged = Reassemble(line, "an");          // chunk id = ability name
                    if (merged != null) { _accCatalogAll.Add(ReadCatalogAbility(merged)); _accCatalogAllRaw.Add(merged.ToWireBody()); }
                }
                else if (_catalogScanning)
                {
                    var merged = Reassemble(line, "an");          // chunk id = ability name
                    if (merged != null) { _accCatalog.Add(ReadCatalogAbility(merged)); _accCatalogRaw.Add(merged.ToWireBody()); }
                }
                break;
            case "catalog-summary": break; // counts only; the scan paginates off the [BEELZ:end] footer

            // Beelz v0.100 / api22 structured transform-loadout + broadcast reads (commit on their [BEELZ:end]).
            case "tform-ability":
                _accTformKit.Add(new BeelzTformAbility(line.GetInt("idx"), line.Get("a"), line.Get("an")));
                break;
            case "tform-slot":
                _accTformBinds.Add(new BeelzTformBind(line.GetInt("phase"), line.GetInt("slot"), line.Get("a"), line.Get("an")));
                break;
            case "broadcast-msg":
                _accBroadcastMsgs.Add(new BeelzBroadcastMsg(line.GetInt("idx"), line.GetCleanText("text")));
                break;
            // Beelz v0.135 / api32 incompatibility lock group. g=/m= are SafeToken-encoded (spaces→_), so
            // GetText restores the name; members are guids or cat:<Category> (no spaces to restore).
            case "lock":
                _accLocks.Add(new BeelzLockGroup(line.GetText("g"), line.GetInt("max", 1), line.GetList("m")));
                break;

            // single-line results -> apply immediately
            case "active":        BeelzState.SetActive(ReadActive(line)); break;
            case "progress":      BeelzState.SetProgress(ReadProgress(line)); break;
            case "info":
            {
                // `api info` repeats i=<index>; `api info-guid` has no i= and repeats a=<guid>.
                var merged = Reassemble(line, line.Has("i") ? "i" : "a");
                if (merged != null) BeelzState.SetAbilityInfo(ReadInfo(merged));
                break;
            }

            case "end":           OnEnd(line); break;
            case "err":           OnErr(line); break;
            case "event":         OnEvent(line); break;

            // tags we don't yet bind (rules, verbosity)
            default:              LogUtils.LogDiagnostic($"[Beelz] unhandled tag '{line.Tag}': {line.Raw}"); break;
        }
    }

    private static void OnVersion(BeelzLine line)
    {
        bool wasPresent = BeelzState.Present;
        int api = line.GetInt("api");
        bool ready = line.GetBool("ready");
        BeelzState.SetVersion(api, line.Get("plugin"), ready);

        if (!ready)
        {
            // Plugin still initializing — the Tick handshake retry will probe again.
            LogUtils.LogDiagnostic("[Beelz] api version ready=0 — will retry.");
            return;
        }
        if (!wasPresent)
        {
            LogUtils.LogInfo($"[Beelz] Beelzebub detected (api={api}, plugin={line.Get("plugin")}). Subscribing + hydrating.");
            FireAvailability();
            BeelzClient.Subscribe();
            BeelzClient.HydrateCore();
            if (BeelzState.SupportsLocks) BeelzClient.RequestLocks();   // api32: badge lock-suppressed abilities
            TryWarmCatalogFromCache();   // instant catalog from a prior scan (same plugin version), no re-scan
        }
    }

    // Warm the catalog(s) from the disk cache on handshake so a returning user gets the (otherwise slow,
    // chat-line-bound) full catalog instantly — no scan. Only loads a cache whose plugin version matches
    // THIS server's, and only when the scope isn't already loaded. Bodies re-parse through the SAME
    // BeelzWireParser + ReadCatalogAbility a live scan uses, so cache == live. Any miss is silent (the user
    // can still Scan all); the per-server dynamic fields are "last known" until a manual Re-scan.
    private static void TryWarmCatalogFromCache()
    {
        try
        {
            string plugin = BeelzState.PluginVersion;
            if (string.IsNullOrEmpty(plugin)) return;
            if (!BeelzState.CatalogLoaded
                && BeelzCatalogCache.TryLoad(BeelzCatalogCache.PlayerScope, plugin, out var pBodies))
            {
                var recs = ParseCatalogBodies(pBodies);
                if (recs.Count > 0)
                {
                    BeelzState.SetCatalog(recs, plugin);   // fromCacheVersion → drives the "cached" hint
                    LogUtils.LogInfo($"[Beelz] catalog warmed from cache ({recs.Count} rows, plugin={plugin}).");
                }
            }
            if (!BeelzState.CatalogAllLoaded
                && BeelzCatalogCache.TryLoad(BeelzCatalogCache.AdminScope, plugin, out var aBodies))
            {
                var recs = ParseCatalogBodies(aBodies);
                if (recs.Count > 0) BeelzState.SetCatalogAll(recs, plugin);
            }
        }
        catch (Exception ex) { LogUtils.LogDebug($"[Beelz] cache warm failed: {ex.Message}"); }
    }

    // Re-parse cached wire bodies into records via the live parser path (single source of truth).
    private static List<BeelzCatalogAbility> ParseCatalogBodies(List<string> bodies)
    {
        var recs = new List<BeelzCatalogAbility>(bodies?.Count ?? 0);
        if (bodies == null) return recs;
        foreach (var body in bodies)
        {
            var line = BeelzWireParser.Parse("[BEELZ:catalog-ability] " + body);
            if (line != null) recs.Add(ReadCatalogAbility(line));
        }
        return recs;
    }

    private static void OnBch(BeelzLine line)
        => BeelzState.SetSubscribed(line.Get("state").Equals("on", StringComparison.OrdinalIgnoreCase));

    private static void OnEnd(BeelzLine line)
    {
        string cmd = line.Get("cmd").ToLowerInvariant();
        switch (cmd)
        {
            case "list":
                BeelzState.SetCaptures(_accCaptures.ToArray()); _accCaptures.Clear();
                break;
            case "slots":
                BeelzState.SetSlots(_accSlots.ToArray(), _accCurrentWeapon, _accFormSlots.ToArray(), _accCurrentForm);
                _accSlots.Clear(); _accCurrentWeapon = ""; _accFormSlots.Clear(); _accCurrentForm = ""; break;
            case "transforms":
                BeelzState.SetTransforms(_accTransforms.ToArray()); _accTransforms.Clear(); break;
            case "hotkeys":
                BeelzState.SetHotkeys(_accHotkeys.ToArray(), _accHotkeysEnabled, _accHotkeysMax); _accHotkeys.Clear(); break;
            case "config":
                BeelzState.SetConfig(_accConfig.ToArray()); _accConfig.Clear(); break;
            case "cooldowns":
                BeelzState.SetCooldowns(new Dictionary<string, float>(_accCooldowns)); _accCooldowns.Clear(); break;
            case "transform-config":
                BeelzState.SetTxConfigs(_accTxConfig.ToArray()); _accTxConfig.Clear(); break;
            case "bestiary":
                // Paginated 40/page, page is 0-based, pages is the total page COUNT.
                // Keep accumulating until the last page (index pages-1), then commit.
                int page = line.GetInt("page", 0), pages = line.GetInt("pages", 1);
                if (page < pages - 1) { BeelzClient.RequestBestiary(page + 1); }
                else
                {
                    // Dedupe by unit guid defensively (overlapping fetches can't double-list).
                    var byUnit = new Dictionary<string, BeelzBestiaryEntry>();
                    foreach (var e in _accBestiary) byUnit[e.UnitGuid] = e;
                    BeelzState.SetBestiary(byUnit.Values.ToArray());
                    _accBestiary.Clear();
                }
                break;
            case "catalog-abilities":
            case "catalog-ability":
                OnCatalogPageEnd(line); break;
            case "catalog-abilities-all":
                OnCatalogAllPageEnd(line); break;
            // Beelz v0.100 / api22 structured reads (single-burst, keyed by the end marker's unit=/pool=).
            case "tform-kit":
                BeelzState.SetTformKit(line.Get("unit"), _accTformKit.ToArray()); _accTformKit.Clear(); break;
            case "tform-binds":
                BeelzState.SetTformBinds(line.Get("unit"), _accTformBinds.ToArray(), line.GetInt("phases", 0)); _accTformBinds.Clear(); break;
            case "broadcast-msgs":
                BeelzState.SetBroadcastMsgs(line.Get("pool"), _accBroadcastMsgs.ToArray()); _accBroadcastMsgs.Clear(); break;
            case "locks":
                BeelzState.SetLocks(_accLocks.ToArray()); _accLocks.Clear(); break;
            default:
                LogUtils.LogDiagnostic($"[Beelz] end for unhandled cmd '{cmd}'."); break;
        }
    }

    // Paginate the catalog scan: each page's [BEELZ:end] either fetches the next page or
    // (on the last) commits the accumulated full matrix to BeelzState.
    private static void OnCatalogPageEnd(BeelzLine line)
    {
        if (!_catalogScanning) return;
        int page = line.GetInt("page", 0), pages = line.GetInt("pages", 1);
        int total = line.GetInt("total", 0);
        if (total > 0) _catalogTotalExpected = total;
        if (page < pages - 1)
        {
            try { ScanProgress?.Invoke(); } catch (Exception ex) { LogUtils.LogError($"[Beelz] ScanProgress handler threw: {ex}"); }
            BeelzClient.RequestCatalogAbilities(page + 1, _catalogFilterKey, _catalogFilterVal);
        }
        else
        {
            _catalogScanning = false;
            if (string.IsNullOrEmpty(_catalogFilterKey))
            {
                BeelzState.SetCatalog(_accCatalog.ToArray());                 // full set: replace + mark complete
                BeelzCatalogCache.Save(BeelzCatalogCache.PlayerScope, BeelzState.PluginVersion, BeelzState.ApiVersion, _accCatalogRaw);
            }
            else
            {
                BeelzState.MergeCatalog(_accCatalog.ToArray());               // filtered: refresh just this slice
            }
            _accCatalog.Clear(); _accCatalogRaw.Clear();
        }
    }

    // Beelz v0.100: same pagination loop for the ADMIN `abilities-all` scope; commits to SetCatalogAll.
    private static void OnCatalogAllPageEnd(BeelzLine line)
    {
        if (!_catalogAllScanning) return;
        int page = line.GetInt("page", 0), pages = line.GetInt("pages", 1);
        int total = line.GetInt("total", 0);
        if (total > 0) _catalogAllTotalExpected = total;
        if (page < pages - 1)
        {
            try { ScanProgressAll?.Invoke(); } catch (Exception ex) { LogUtils.LogError($"[Beelz] ScanProgressAll handler threw: {ex}"); }
            BeelzClient.RequestCatalogAbilitiesAll(page + 1, _catalogAllFilterKey, _catalogAllFilterVal);
        }
        else
        {
            _catalogAllScanning = false;
            if (string.IsNullOrEmpty(_catalogAllFilterKey))
            {
                BeelzState.SetCatalogAll(_accCatalogAll.ToArray());
                BeelzCatalogCache.Save(BeelzCatalogCache.AdminScope, BeelzState.PluginVersion, BeelzState.ApiVersion, _accCatalogAllRaw);
            }
            else
            {
                BeelzState.MergeCatalogAll(_accCatalogAll.ToArray());
            }
            _accCatalogAll.Clear(); _accCatalogAllRaw.Clear();
        }
    }

    private static void OnErr(BeelzLine line)
    {
        string code = line.Get("code");
        if (code.Equals("not_ready", StringComparison.OrdinalIgnoreCase))
        {
            // Pre-init read; the handshake/refetch will retry. Nothing to do.
            LogUtils.LogDiagnostic($"[Beelz] err not_ready for cmd={line.Get("cmd")}.");
            return;
        }
        LogUtils.LogDebug($"[Beelz] err cmd={line.Get("cmd")} code={code} msg={line.GetText("msg")}");
    }

    // Map each push event to the affected read(s); applied debounced on Tick.
    private static void OnEvent(BeelzLine line)
    {
        switch (line.Get("type").ToLowerInvariant())
        {
            case "capture":
            case "devour":
            case "forget":
            case "cleared":
                _needList = _needBestiary = _needProgress = true; break;
            case "collection-complete":
                _needList = _needBestiary = _needProgress = true;
                FireCollectionComplete(line.GetInt("count"), line.GetInt("total")); break;
            case "forget-transform":
            case "transform-unlock":
                _needTransforms = _needProgress = true; break;
            case "slot-granted":
            case "weapon-slot-granted":
            case "form-slot-granted":
                _needSlots = true; _needBarRefresh = true; break;   // a new ability landed → re-apply the bar
            case "slot-cleared":
            case "weapon-slot-cleared":
            case "form-slot-cleared":
                _needSlots = true; break;
            case "hotkey-set":
            case "hotkey-cleared":
                _needHotkeys = true; break;
            case "transform-activated":
            case "transform-phase-shift":
                _needActive = _needTransforms = true; break;
            // Beelz v0.137.2: bar edits made while transformed apply when it ends -> re-read the slots too.
            case "transform-ended":
                _needActive = _needTransforms = _needSlots = true; break;
            case "config-changed":
                _needConfig = true; _needLocks = BeelzState.SupportsLocks; break;
            // Beelz v0.135 / api32: a bar resolve kept an ability OFF the live bar (locked=1) or let it back
            // (locked=0); src=cast when `.beelz cast` refused it. Only changes are sent. Track the live set
            // for badges and re-read the groups (the handoff's "after ability-locked events").
            case "ability-locked":
                BeelzState.SetAbilityLocked(line.Get("a"), line.GetText("group"), line.GetBool("locked"));
                _needLocks = true; break;
            case "cast":
                FireAbilityCast(line.Get("a")); break;
            case "summon":
            case "detonate":
                break; // no cached state to refresh today
            default:
                LogUtils.LogDiagnostic($"[Beelz] unhandled event type '{line.Get("type")}'."); break;
        }
    }

    private static bool HasPendingRefetch()
        => _needList || _needSlots || _needTransforms || _needActive
        || _needProgress || _needHotkeys || _needConfig || _needBestiary || _needBarRefresh || _needLocks;

    private static void FlushRefetch()
    {
        _lastRefetchAt = Time.realtimeSinceStartup;
        if (_needList)       { BeelzClient.RequestList();       _needList = false; }
        if (_needSlots)      { BeelzClient.RequestSlots();      _needSlots = false; }
        if (_needTransforms) { BeelzClient.RequestTransforms(); _needTransforms = false; }
        if (_needActive)     { BeelzClient.RequestActive();     _needActive = false; }
        if (_needProgress)   { BeelzClient.RequestProgress();   _needProgress = false; }
        if (_needHotkeys)    { BeelzClient.RequestHotkeys();    _needHotkeys = false; }
        if (_needConfig)     { BeelzClient.RequestConfig();     _needConfig = false; }
        if (_needBestiary)   { BeelzClient.RequestBestiary();   _needBestiary = false; }
        if (_needLocks)      { BeelzClient.RequestLocks();      _needLocks = false; }
        // 0.19: one debounced bar re-apply after grants landed this window (Settings-gated, silent).
        if (_needBarRefresh)
        {
            _needBarRefresh = false;
            // Beelz v0.137.1+ puts grants on the live bar itself; the extra refresh is only for older servers.
            if (Raphael.Config.Settings.BeelzAutoRefreshBar && !BeelzState.GrantsApplyImmediately) BeelzClient.RefreshBarSilent();
        }
    }

    // Reassemble a chunked `info`/`info-guid`/`catalog-ability` line (Beelz v0.76+). Returns the
    // fully-merged line once every part has arrived, or null while parts are still pending. A line
    // with no `part=` (or part=k/1) is single and returned as-is. idKey identifies which field is
    // the stable id repeated on every part (i / a / an), so concurrent chunked lines don't collide.
    private static BeelzLine Reassemble(BeelzLine line, string idKey)
    {
        if (!line.Has("part")) return line;                       // single-line (short ability / pre-v0.76)

        string part = line.Get("part");                           // "k/n"
        int slash = part.IndexOf('/');
        int n = 1;
        if (slash > 0) int.TryParse(part.Substring(slash + 1), out n);
        if (n <= 1) return line;                                  // part=1/1 -> single

        string bufKey = line.Tag + " " + idKey + " " + line.Get(idKey);
        if (!_chunks.TryGetValue(bufKey, out var buf)) { buf = new ChunkBuffer(); _chunks[bufKey] = buf; }
        buf.Total = n;
        buf.Seen++;
        foreach (var kv in line.Fields)
            if (!kv.Key.Equals("part", StringComparison.OrdinalIgnoreCase))
                buf.Fields[kv.Key] = kv.Value;                    // later parts overwrite; repeated id is harmless

        if (buf.Seen < buf.Total) return null;                    // wait for the rest

        _chunks.Remove(bufKey);
        return new BeelzLine(line.Tag,
            new Dictionary<string, string>(buf.Fields, StringComparer.OrdinalIgnoreCase), line.Raw);
    }

    // ---- record readers ----
    private static BeelzCapture ReadCapture(BeelzLine l) => new(
        l.GetInt("i"), l.GetChar("s"), l.Get("u"), l.Get("un"),
        l.Get("a"), l.Get("an"), l.Get("cat"), l.Get("type"),
        l.GetText("label"), l.GetText("ulabel"));   // friendly names (Api10); GetText restores spaces

    private static BeelzSlot ReadSlot(BeelzLine l) => new(
        l.Get("bucket"), l.GetInt("slot"), l.Get("a"), l.Get("an"), l.GetCleanText("label"));   // label= api28

    private static BeelzFormSlot ReadFormSlot(BeelzLine l) => new(
        l.Get("form"), l.GetInt("slot"), l.Get("a"), l.Get("an"), l.GetCleanText("label"));   // label= api28

    private static BeelzTransform ReadTransform(BeelzLine l) => new(
        l.GetInt("i"), l.GetChar("s"), l.Get("u"), l.Get("un"), l.GetBool("enabled"),
        l.Get("difficulty"), l.GetInt("tier"), l.GetFloat("damage_scale"), l.GetFloat("cooldown_scale"),
        l.GetFloat("health_scale"), l.GetFloat("speed_scale"), l.Get("type"), l.GetBool("full_replace"),
        l.Get("scaling_mode"), l.GetBool("shard"));

    private static BeelzTxConfig ReadTxConfig(BeelzLine l) => new(
        l.GetChar("src"), l.Get("mode"), l.GetFloat("duration"), l.GetFloat("cooldown"));

    private static BeelzActive ReadActive(BeelzLine l) => l.GetBool("none")
        ? new BeelzActive(true, "", "", '\0', "", 0, "")
        : new BeelzActive(false, l.Get("u"), l.Get("un"), l.GetChar("s"), l.Get("ttl"), l.GetInt("phase"), l.Get("phases"));

    private static BeelzBestiaryEntry ReadBestiary(BeelzLine l) => new(
        l.Get("u"), l.Get("un"), l.GetChar("s"), l.GetInt("captured"), l.GetInt("total"), l.GetBool("transform"));

    private static BeelzHotkey ReadHotkey(BeelzLine l) => new(
        l.Get("name"), l.Get("a"), l.Get("an"));

    private static BeelzProgress ReadProgress(BeelzLine l) => new(
        l.GetInt("abilities_captured"), l.GetInt("abilities_total"), l.GetFloat("abilities_pct"),
        l.GetInt("transforms_unlocked"), l.GetInt("transforms_total"), l.GetFloat("transforms_pct"));

    private static BeelzConfigEntry ReadConfig(BeelzLine l) => new(
        l.Get("section"), l.Get("key"), l.GetText("value"), l.Get("type"), l.GetBool("editable", true));

    private static BeelzCatalogAbility ReadCatalogAbility(BeelzLine l) => new(
        l.Get("an"), l.Get("cat"),
        StripAny(l.GetList("weapons")), StripAny(l.GetList("forms")),
        l.GetBool("transform_only"), l.GetBool("enabled", true), l.Get("difficulty"),
        l.GetFloat("damage_scale"), l.GetFloat("cooldown_scale"),
        // v9 curated flag · v10 school/desc · v8 phase/allow_denied/category_override · shaping (v0.65+)
        l.GetBool("curated"), l.GetClean("school"), l.GetCleanText("desc"),
        l.GetInt("phase"), l.GetBool("allow_denied"), l.GetClean("category_override"), ReadShaping(l),
        l.GetCleanText("notes"),
        // Coordinated wire addition (optional): a=ability PrefabGUID, unit=source-NPC friendly name,
        // unitguid=source-NPC PrefabGUID. Empty when the server doesn't emit them → views fall back.
        l.Get("a"), l.GetCleanText("unit"), l.Get("unitguid"),
        // api24 condition · api25 curation · api26 source-tier (all informational, absent on old servers).
        l.GetClean("condition"), ReadConditionMods(l), DashClean(l.Get("condition_source")),
        l.Get("review_status"), l.GetClean("review_tag"),
        l.GetIntOpt("source_level"), l.GetClean("source_tier"), l.GetBool("is_vblood"));

    // condition_source uses "auto" as a REAL value (classifier guess), so GetClean — which folds "auto"
    // to "" — is wrong here. Fold only the "-"/empty sentinel. review_tag keeps literal underscores
    // (idle_flee, variant_hard), so it uses GetClean (no underscore→space restore), NOT GetCleanText.
    private static string DashClean(string v) => string.IsNullOrEmpty(v) || v == "-" ? "" : v;

    // condition_mods is a comma list ("Combo,Charged,Channel") or the "-" sentinel. GetList doesn't fold
    // "-", so parse explicitly; RemoveEmptyEntries avoids a Linq dependency.
    private static IReadOnlyList<string> ReadConditionMods(BeelzLine l)
    {
        var v = l.Get("condition_mods");
        if (string.IsNullOrEmpty(v) || v == "-") return System.Array.Empty<string>();
        return v.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
    }

    // Per-ability shaping overrides, present on both `api info`/`info-guid` and `catalog-ability`
    // (Beelz v0.65–v0.87). Numeric fields are null when unset ("-"); tri-states keep raw on/off/auto.
    private static BeelzShaping ReadShaping(BeelzLine l) => new(
        l.GetFloatOpt("cooldown_override"), l.GetFloatOpt("range_override"), l.GetIntOpt("charges_override"),
        l.GetFloatOpt("chargetime_override"), l.GetFloatOpt("aoe_override"), l.GetFloatOpt("projspeed_override"),
        l.GetFloatOpt("duration_override"), l.GetFloatOpt("heal_mult"), l.GetFloatOpt("force_timeout_override"),
        l.GetIntOpt("summon_cap_override"), l.GetFloatOpt("summon_timeout_override"), l.GetIntOpt("summon_units_override"),
        l.GetFloatOpt("free_move_secs"),
        l.Get("interrupt_on_hit"), l.Get("interruptible"), l.Get("free_move"), l.Get("cast_speed"),
        // api31 (Beelz v0.134) — api info only; absent on catalog rows → null.
        l.GetIntOpt("maxstacks_override"), l.GetIntOpt("projcount_override"), l.GetFloatOpt("knockback_override"),
        l.GetFloatOpt("lifetime_override"), l.GetFloatOpt("casttime_override"));

    // Beelzebub emits weapons=/forms= as "any" when unrestricted; normalize that to an
    // empty list so callers can treat Count>0 as "restricted".
    private static IReadOnlyList<string> StripAny(IReadOnlyList<string> list)
    {
        if (list == null || list.Count == 0) return Array.Empty<string>();
        if (list.Count == 1 && list[0].Equals("any", StringComparison.OrdinalIgnoreCase)) return Array.Empty<string>();
        return list;
    }

    private static BeelzAbilityInfo ReadInfo(BeelzLine l) => new(
        l.GetInt("i"), l.GetChar("s"), l.Get("u"), l.Get("un"), l.Get("a"), l.Get("an"),
        l.GetText("label"), l.GetCleanText("desc"), l.GetList("weapons"), l.Get("weapon_anim"), l.GetClean("school"),
        l.GetFloat("cooldown_seconds"), l.GetList("forms"), l.GetBool("transform_only"), l.GetBool("enabled"),
        l.Get("difficulty"), l.GetFloat("damage_scale"), l.GetFloat("cooldown_scale"),
        // v8 fields: cat / category_override / cast_time_seconds / range / behavior / phase / allow_denied + shaping
        l.Get("cat"), l.GetClean("category_override"), l.GetFloat("cast_time_seconds"), l.GetFloat("range"),
        l.Get("behavior"), l.GetInt("phase"), l.GetBool("allow_denied"), ReadShaping(l),
        // api24/25/26 informational tokens — same parse rules as ReadCatalogAbility (api info carries them too).
        l.GetClean("condition"), ReadConditionMods(l), DashClean(l.Get("condition_source")),
        l.Get("review_status"), l.GetClean("review_tag"),
        l.GetIntOpt("source_level"), l.GetClean("source_tier"), l.GetBool("is_vblood"),
        // api29 block lists · api30 per-hit damage · api31 cooldown mode (all additive; absent on old servers).
        ReadDashList(l, "form_blocks"), ReadDashList(l, "weapon_blocks"),
        DashClean(l.Get("damage_mode")), l.GetFloatOpt("damage_scale_eff"), DashClean(l.Get("attribution")),
        l.GetFloatOpt("summon_power"), DashClean(l.Get("cooldown_mode")));

    // Comma list with the "-" none-sentinel (form_blocks= / weapon_blocks=, api29).
    private static IReadOnlyList<string> ReadDashList(BeelzLine l, string key)
    {
        var v = l.Get(key);
        if (string.IsNullOrEmpty(v) || v == "-") return System.Array.Empty<string>();
        return v.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
    }

    private static void FireAvailability()
    {
        try { AvailabilityChanged?.Invoke(); }
        catch (Exception ex) { LogUtils.LogError($"[Beelz] AvailabilityChanged handler threw: {ex}"); }
    }

    private static void FireAbilityCast(string abilityGuid)
    {
        if (string.IsNullOrEmpty(abilityGuid)) return;
        try { AbilityCast?.Invoke(abilityGuid); }
        catch (Exception ex) { LogUtils.LogError($"[Beelz] AbilityCast handler threw: {ex}"); }
    }

    private static void FireCollectionComplete(int count, int total)
    {
        LogUtils.LogInfo($"[Beelz] collection complete — {count}/{total} abilities.");
        try { CollectionComplete?.Invoke(count, total); }
        catch (Exception ex) { LogUtils.LogError($"[Beelz] CollectionComplete handler threw: {ex}"); }
    }
}
