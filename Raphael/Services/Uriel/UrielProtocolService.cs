using System;
using System.Collections.Generic;
using Raphael.Utils;
using UnityEngine;

namespace Raphael.Services.Uriel;

// Entry point + router for the Uriel integration. Modeled on BeelzProtocolService (handshake +
// availability gate); the wire protocol is plaintext [URIEL:*] System-chat lines (no MAC), routed
// here from ClientChatPatch's [URIEL: branch — it never touches the regex intercept pipeline.
//
// Responsibilities:
//   • Detect (handshake `.uriel api version`, retry/back-off, gate on ready=1)
//   • Hydrate the player's unlocked-prefab list once detected (small, grows with play)
//   • Parse each line and commit paged object streams on [URIEL:end]
//   • Expose IsPresent + AvailabilityChanged so MainPanel can gate the tab group
//
// NOTE: Uriel is a MIXED-channel mod — only the object-spawn collection (version/catalog/unlocked)
// is [URIEL:*] wire today. Shares/stairs replies are still human text, handled by the MessageService
// regex pipeline; promote them to wire per docs/V0.26_URIEL_NOTES.md as the server adds endpoints.
internal static class UrielProtocolService
{
    // ---- availability gate (read by MainPanel.IsTabGroupAvailable) ----
    public static bool IsPresent => UrielState.Present;
    public static bool DetectionGaveUp { get; private set; }

    /// <summary>Fired when presence is resolved (handshake ACK or give-up) so the UI re-checks the
    /// Uriel tab-group availability in place.</summary>
    public static event Action AvailabilityChanged;

    // ---- handshake state (mirrors BeelzProtocolService's retry/give-up) ----
    private const float DETECTION_START_DELAY_SECONDS = 4f;   // let the relog/chat window settle first
    private const float HANDSHAKE_RETRY_AFTER_SECONDS = 4f;
    private const int   HANDSHAKE_MAX_ATTEMPTS = 12;          // ~48s budget; recover via Re-check after give-up
    private static float _detectStartAt;
    private static float _handshakeSentAt;
    private static int   _handshakeAttempts;

    // ---- paged object-stream accumulators (committed on the matching [URIEL:end]) ----
    private enum Scope { None, Catalog, Unlocked }
    private static Scope _activeScope = Scope.None;
    private static bool  _catalogScanning, _unlockedScanning;

    private static readonly List<UrielObject> _accCatalog = new();
    private static int _catalogTotal, _catalogDiscoverable;

    private static readonly List<UrielObject> _accUnlocked = new();
    private static int    _unlDiscoverable, _unlCount;
    private static float  _unlPct;
    private static string _unlSteam = "";

    public static bool CatalogScanInProgress  => _catalogScanning;
    public static bool UnlockedScanInProgress  => _unlockedScanning;
    public static int  CatalogScanLoaded       => _accCatalog.Count;

    // Diagnostic: a human note about the in-flight / last object-spawn read, surfaced in the Object
    // Spawning tab so a user can SEE why the list is empty (waiting / no reply / disabled / done).
    private const float SCAN_TIMEOUT_SECONDS = 6f;
    private static float _scanRequestAt;            // realtime of the last api catalog/unlocked request
    public static string ScanNote { get; private set; } = "";
    public static event Action ScanNoteChanged;
    private static void SetScanNote(string note)
    {
        ScanNote = note ?? "";
        try { ScanNoteChanged?.Invoke(); } catch (Exception ex) { LogUtils.LogError($"[Uriel] ScanNoteChanged handler threw: {ex}"); }
    }

    /// <summary>Begin (or restart) a FULL catalog scan — the optional "everything that exists to
    /// collect" browse. Heavy (hundreds of pages); the UI should cache it and pull once per session.</summary>
    public static void StartCatalogScan()
    {
        if (!UrielState.Present) return;
        _unlockedScanning = false; _accUnlocked.Clear();   // mutually exclusive with the unlocked scan
        _accCatalog.Clear(); _catalogTotal = _catalogDiscoverable = 0;
        _catalogScanning = true; _activeScope = Scope.Catalog;
        _scanRequestAt = Time.realtimeSinceStartup;
        SetScanNote("Requested full catalog — awaiting server reply…");
        UrielClient.RequestCatalog(1);
    }

    /// <summary>Begin (or restart) the player's unlocked-prefab scan (small — what they can build).</summary>
    public static void StartUnlockedScan()
    {
        if (!UrielState.Present) return;
        _catalogScanning = false; _accCatalog.Clear();
        _accUnlocked.Clear(); _unlDiscoverable = _unlCount = 0; _unlPct = 0f; _unlSteam = "";
        _unlockedScanning = true; _activeScope = Scope.Unlocked;
        _scanRequestAt = Time.realtimeSinceStartup;
        SetScanNote("Requested unlocked list — awaiting server reply…");
        UrielClient.RequestUnlocked(1);
    }

    // ---- per-frame driver (registered on CoreUpdateBehavior.Actions in Plugin.Load) ----
    public static void Tick()
    {
        if (!MessageService.IsInitialized) return;

        // Scan-reply timeout — runs whether or not detection is resolved. If a catalog/unlocked request
        // gets NO reply (no header, object, end, or err) within the window, tell the user (the usual
        // cause is the object-spawn API not being enabled / answering on this server). Each page request
        // re-anchors _scanRequestAt, so a legitimately long multi-page scan never false-trips this.
        if ((_unlockedScanning || _catalogScanning) && _scanRequestAt > 0f
            && Time.realtimeSinceStartup - _scanRequestAt > SCAN_TIMEOUT_SECONDS)
        {
            bool wasCatalog = _catalogScanning;
            _catalogScanning = _unlockedScanning = false; _activeScope = Scope.None;
            _accCatalog.Clear(); _accUnlocked.Clear();
            SetScanNote($"No reply from the server to '.uriel api {(wasCatalog ? "catalog" : "unlocked")}'. " +
                "Object spawning answered the version handshake but isn't returning rows — check that its " +
                "catalog/unlocked API is enabled on this server (turn on Uriel → Settings → Diagnostics to log the wire trace).");
        }

        if (UrielState.Present || DetectionGaveUp) return;

        float now = Time.realtimeSinceStartup;
        if (_detectStartAt <= 0f) { _detectStartAt = now; UrielDiag.Log($"detection started (settle {DETECTION_START_DELAY_SECONDS}s)"); }
        if (now - _detectStartAt < DETECTION_START_DELAY_SECONDS) return;

        bool first = _handshakeAttempts == 0;
        if (!first && now - _handshakeSentAt < HANDSHAKE_RETRY_AFTER_SECONDS) return;

        if (_handshakeAttempts >= HANDSHAKE_MAX_ATTEMPTS)
        {
            DetectionGaveUp = true;
            LogUtils.LogInfo($"[Uriel] No response to `.uriel api version` after {_handshakeAttempts} probes — Uriel not present on this server.");
            FireAvailability();
            return;
        }
        _handshakeAttempts++;
        _handshakeSentAt = now;
        UrielClient.RequestVersion();
        UrielDiag.Log($"`.uriel api version` probe {_handshakeAttempts}/{HANDSHAKE_MAX_ATTEMPTS} sent");
    }

    /// <summary>Reset detection + all streaming state on logout so a relog re-runs the handshake from
    /// scratch — CRITICAL when switching servers without a full restart (mirrors BeelzProtocolService.Reset).
    /// Called from the ClientBootstrapSystem.OnDestroy teardown hook — PURE field resets, no UI/ECS work
    /// and no event fire (the UI re-gates on relog when detection ACKs and fires AvailabilityChanged).</summary>
    public static void Reset()
    {
        DetectionGaveUp = false;
        _detectStartAt = 0f;
        _handshakeSentAt = 0f;
        _handshakeAttempts = 0;

        _activeScope = Scope.None;
        _catalogScanning = _unlockedScanning = false;
        _accCatalog.Clear(); _catalogTotal = _catalogDiscoverable = 0;
        _accUnlocked.Clear(); _unlDiscoverable = _unlCount = 0; _unlPct = 0f; _unlSteam = "";
        _scanRequestAt = 0f; ScanNote = "";

        UrielState.Reset();
        UrielEvents.Reset();
    }

    // ---- inbound line router (called from ClientChatPatch for every [URIEL:*] line) ----
    public static void HandleLine(string raw)
    {
        UrielDiag.LogIn(raw);
        var line = UrielWireParser.Parse(raw);
        if (line == null) return;

        switch (line.Tag.ToLowerInvariant())
        {
            case "version":  OnVersion(line); break;

            // Page headers select which accumulator the following [URIEL:object] rows fill.
            case "catalog":
                _activeScope = Scope.Catalog;
                if (line.Has("total"))        _catalogTotal = line.GetInt("total");
                if (line.Has("discoverable")) _catalogDiscoverable = line.GetInt("discoverable");
                break;
            case "unlocked":
                _activeScope = Scope.Unlocked;
                _unlSteam = line.Get("steam");
                if (line.Has("n"))            _unlCount = line.GetInt("n");
                if (line.Has("discoverable")) _unlDiscoverable = line.GetInt("discoverable");
                if (line.Has("pct"))          _unlPct = line.GetFloat("pct");
                break;

            case "object":   OnObject(line); break;
            case "end":      OnEnd(line); break;
            case "err":      OnErr(line); break;

            default:         LogUtils.LogDiagnostic($"[Uriel] unhandled tag '{line.Tag}': {line.Raw}"); break;
        }
    }

    private static void OnVersion(UrielLine line)
    {
        bool wasPresent = UrielState.Present;
        int api = line.GetInt("api");
        bool ready = line.GetBool("ready");
        UrielState.SetVersion(
            api, line.Get("plugin"), ready,
            objectSpawn: line.GetBool("objectspawn"),
            collection:  line.GetBool("collection"),
            adminOnly:   line.GetBool("adminonly"),
            mode:        line.Get("mode"),
            chance:      line.GetInt("chance"),
            total:       line.GetInt("total"),
            discoverable:line.GetInt("discoverable"),
            blocked:     line.GetInt("blocked"));

        // Castle Events (handoff §7, planned): `events=` / `playerevents=` on the version line. Absent on
        // Uriel 0.21.0 — the Castle Events tab probes with `.uriel event list` until then.
        if (line.Has("events"))
            UrielEvents.SetFromVersion(line.GetBool("events"), line.GetBool("playerevents"), Raphael.Services.MessageService.IsLocalAdmin());

        if (!ready)
        {
            LogUtils.LogDiagnostic("[Uriel] api version ready=0 — will retry.");
            return;
        }
        if (!wasPresent)
        {
            LogUtils.LogInfo($"[Uriel] Uriel detected (api={api}, plugin={line.Get("plugin")}). Hydrating unlocked list.");
            FireAvailability();
            // Hydrate the player's spawn menu — small and grows with play. Do NOT auto-scan the full
            // catalog (can be hundreds of pages; the UI pulls + caches it on demand).
            if (UrielState.ObjectSpawnEnabled) StartUnlockedScan();
            TryWarmCatalogFromCache();   // instant full-catalog browse from a prior scan (same plugin version)
        }
    }

    // Warm the full catalog from the disk cache on handshake so a returning user gets the (otherwise
    // slow, chat-line-bound) full browse instantly — no scan. Only loads a cache whose plugin version
    // matches THIS server's, and only when the catalog isn't already loaded. Any miss is silent (the
    // user can still load it on demand).
    private static void TryWarmCatalogFromCache()
    {
        try
        {
            if (UrielState.CatalogLoaded) return;
            string plugin = UrielState.PluginVersion;
            if (string.IsNullOrEmpty(plugin)) return;
            if (UrielCatalogCache.TryLoad(plugin, out var cached) && cached.Count > 0)
            {
                UrielState.SetCatalog(cached, cached.Count, 0, plugin);
                LogUtils.LogInfo($"[Uriel] catalog warmed from cache ({cached.Count} rows, plugin={plugin}).");
            }
        }
        catch (Exception ex) { LogUtils.LogDebug($"[Uriel] cache warm failed: {ex.Message}"); }
    }

    private static void OnObject(UrielLine line)
    {
        var obj = new UrielObject(
            line.GetInt("guid"), line.GetBool("disc"),
            line.GetText("label"), NormalizeCategory(line.GetClean("cat")));
        if (_activeScope == Scope.Catalog)      _accCatalog.Add(obj);
        else if (_activeScope == Scope.Unlocked) _accUnlocked.Add(obj);
    }

    private static void OnEnd(UrielLine line)
    {
        string cmd = line.Get("cmd").ToLowerInvariant();
        ParsePage(line.Get("page"), out int page, out int pages);
        switch (cmd)
        {
            case "catalog":
                if (!_catalogScanning) break;
                if (page < pages)
                {
                    _scanRequestAt = Time.realtimeSinceStartup;   // re-anchor the reply timeout per page
                    SetScanNote($"Loading catalog… page {page}/{pages} ({_accCatalog.Count} so far)");
                    UrielClient.RequestCatalog(page + 1);
                }
                else
                {
                    _catalogScanning = false; _activeScope = Scope.None;
                    var rows = _accCatalog.ToArray();
                    UrielState.SetCatalog(rows, _catalogTotal, _catalogDiscoverable);
                    UrielCatalogCache.Save(UrielState.PluginVersion, rows);   // one-time per plugin version
                    _accCatalog.Clear();
                    SetScanNote("");
                }
                break;
            case "unlocked":
                if (!_unlockedScanning) break;
                if (page < pages)
                {
                    _scanRequestAt = Time.realtimeSinceStartup;
                    UrielClient.RequestUnlocked(page + 1);
                }
                else
                {
                    _unlockedScanning = false; _activeScope = Scope.None;
                    UrielState.SetUnlocked(_accUnlocked.ToArray(), _unlCount, _unlPct, _unlSteam, _unlDiscoverable);
                    _accUnlocked.Clear();
                    SetScanNote("");
                }
                break;
            default:
                LogUtils.LogDiagnostic($"[Uriel] end for unhandled cmd '{cmd}'."); break;
        }
    }

    private static void OnErr(UrielLine line)
    {
        string code = line.Get("code");
        string cmd = line.Get("cmd");
        // Surface the error against any in-flight catalog/unlocked scan so the Object Spawning tab can
        // explain the empty list instead of just spinning.
        bool wasScanning = _catalogScanning || _unlockedScanning;
        _catalogScanning = _unlockedScanning = false; _activeScope = Scope.None;
        _accCatalog.Clear(); _accUnlocked.Clear();

        if (code.Equals("notready", StringComparison.OrdinalIgnoreCase))
        {
            LogUtils.LogDiagnostic($"[Uriel] err notready for cmd={cmd}.");
            if (wasScanning) SetScanNote("Server replied 'not ready' — Uriel is still initializing. Try again in a moment.");
            return;
        }
        if (code.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            LogUtils.LogInfo($"[Uriel] err disabled for cmd={cmd} — object spawning is off on this server.");
            if (wasScanning) SetScanNote("Object spawning is disabled by the server admin.");
            return;
        }
        LogUtils.LogDebug($"[Uriel] err cmd={cmd} code={code}");
        if (wasScanning) SetScanNote($"Server returned an error (code={code}) for '.uriel api {cmd}'.");
    }

    // Parse a "cur/total" page token (e.g. "1/7"). Pages are 1-based in Uriel's wire API.
    private static void ParsePage(string token, out int page, out int pages)
    {
        page = 1; pages = 1;
        if (string.IsNullOrEmpty(token)) return;
        int slash = token.IndexOf('/');
        if (slash <= 0) { int.TryParse(token, out page); return; }
        int.TryParse(token.Substring(0, slash), out page);
        int.TryParse(token.Substring(slash + 1), out pages);
        if (page < 1) page = 1;
        if (pages < 1) pages = 1;
    }

    // Fold an unknown / empty category to the handoff's documented fallback ("decor").
    private static string NormalizeCategory(string cat)
        => string.IsNullOrEmpty(cat) ? "decor" : cat;

    private static void FireAvailability()
    {
        try { AvailabilityChanged?.Invoke(); }
        catch (Exception ex) { LogUtils.LogError($"[Uriel] AvailabilityChanged handler threw: {ex}"); }
    }
}
