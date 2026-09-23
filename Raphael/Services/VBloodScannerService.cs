using System;
using System.Collections.Generic;
using Raphael.Resources;
using Raphael.Utils;

namespace Raphael.Services;

// 0.10.9: V-Blood scanner rewritten from the .fam s search-based approach
// (0.10.0..0.10.8) to a per-box sweep using `.fam boxes` + `.fam cb` + `.fam l`.
//
// Why the rewrite:
//   - The .fam s reply is lossy: it returns boxes (not entries) and only a
//     per-box "at least one shiny" bit. A box containing BOTH a basic and
//     a shiny of the same V-Blood comes back as one star-marked row, so the
//     basic was invisible to the chip view.
//   - 64 names × 2 forms (basic + Primal) × ~2s/query = ~4 minutes per scan.
//     During that long window any other chat command could clobber our
//     intercept state, producing flaky / partial results — which is what
//     friend-testing reported in 0.10.8 ("some V-Bloods summon, some don't").
//   - `.fam l` per box gives us per-entry index + name + level + prestige +
//     IsShiny + ShinyColorHex. That's strictly richer data than .fam s and
//     resolves the variant-disambiguation problem natively. Each Bloodcraft
//     box holds ~10 familiars; a typical user has ~5-15 boxes; so the new
//     scan completes in ~30-60s and is more accurate.
//
// Sweep state machine:
//   Idle              → StartScan stores original active box, sends .fam boxes
//   AwaitingBoxList   → BoxListChanged fires → populate _boxQueue, advance
//   SweepingBoxes     → for each box: SetActiveBox + .fam cb + .fam l → wait
//                        for BoxContentsChanged → process entries → next box
//   Restoring         → final .fam cb back to the user's original box (if
//                        different from where we ended)
//   Idle              → scan complete
//
// Mutex against user commands during sweep:
//   - We watch ActiveBoxChanged. If the user navigates mid-sweep, we let
//     the in-flight pair finish, then re-issue cb+l for the box we were on
//     before resuming the queue. Best-effort — the user can also Cancel.
//   - We deliberately use Silent enqueue so chat copies are destroyed; only
//     visible if the user opted into ShowChatBchAuto.
public static class VBloodScannerService
{
    private enum State { Idle, AwaitingBoxList, SweepingBoxes, Restoring }

    private static State _state = State.Idle;
    private static readonly Queue<string> _boxQueue = new();
    private static int    _totalBoxes;
    private static int    _completedBoxes;
    private static bool   _subscribed;
    private static double _lastSendAt;
    private static string _currentBox;
    private static string _originalActiveBox;
    private static double _currentBoxDeadline;

    // Aggregation buffer — per-name temporary lists while the sweep runs.
    // Promoted to PlayerStateService.VBloodCollection in batches as each
    // box's contents arrive, so the V-Bloods tab progresses visibly as
    // boxes are read (instead of flashing all-empty until the scan ends).
    private static readonly Dictionary<string, List<PlayerStateService.VBloodInstance>> _accumulated =
        new(StringComparer.OrdinalIgnoreCase);

    private const double SCAN_SEND_INTERVAL_SECONDS  = 1.5; // pause between (cb,l) pairs so the server has room to reply
    private const double SCAN_BOX_TIMEOUT_SECONDS    = 4.5; // a single box that doesn't reply this long gets skipped

    /// <summary>True while a sweep is mid-flight. UI surfaces should
    /// disable Summon while this is true (or queue clicks). The summon
    /// service refuses with a status message when this is true.</summary>
    public static bool Scanning => _state != State.Idle;
    public static int  TotalForCurrentScan => _totalBoxes;
    public static int  CompletedForCurrentScan => _completedBoxes;
    public static string CurrentBoxBeingScanned => _currentBox ?? "";
    public static event Action ScanStateChanged;

    /// <summary>Subscribe to the state events the scanner needs. Called
    /// once from Plugin.Load after MessageService is fully initialized.
    /// Idempotent.</summary>
    public static void Initialize()
    {
        if (_subscribed) return;
        PlayerStateService.BoxListChanged     += OnBoxListChanged;
        PlayerStateService.BoxContentsChanged += OnBoxContentsChanged;
        _subscribed = true;
    }

    /// <summary>Per-frame tick registered with CoreUpdateBehavior in
    /// Plugin.Load. Drives box-pair dispatch from the queue.</summary>
    public static void Tick()
    {
        switch (_state)
        {
            case State.Idle:
                return;

            case State.AwaitingBoxList:
                // BoxListChanged handler advances us. If the .fam boxes reply
                // never arrived (server hiccup), abort after a generous
                // timeout so the user can re-trigger.
                if (UnityEngine.Time.realtimeSinceStartupAsDouble - _lastSendAt > 6.0)
                {
                    LogUtils.LogWarning("VBloodScanner: .fam boxes reply timed out — aborting scan.");
                    AbortInternal();
                }
                return;

            case State.SweepingBoxes:
                TickSweep();
                return;

            case State.Restoring:
                // Nothing to wait on — the scan reply for .fam cb to restore
                // doesn't need parsing. Just transition out after a short
                // grace period so we don't race a user click that ran
                // SetActiveBox manually right after.
                if (UnityEngine.Time.realtimeSinceStartupAsDouble - _lastSendAt > 0.5)
                {
                    _state = State.Idle;
                    LogUtils.LogInfo($"VBloodScanner: scan complete ({_completedBoxes}/{_totalBoxes} boxes; restored active box to '{_originalActiveBox}').");
                    Fire(ScanStateChanged);
                }
                return;
        }
    }

    private static void TickSweep()
    {
        var now = UnityEngine.Time.realtimeSinceStartupAsDouble;

        // Mid-box: waiting for BoxContents to arrive (or timeout).
        if (!string.IsNullOrEmpty(_currentBox))
        {
            if (now > _currentBoxDeadline)
            {
                LogUtils.LogWarning($"VBloodScanner: box '{_currentBox}' didn't return contents in {SCAN_BOX_TIMEOUT_SECONDS:0.0}s — skipping.");
                _completedBoxes++;
                _currentBox = null;
                Fire(ScanStateChanged);
            }
            return;
        }

        // Drained — advance to Restoring (or Idle if the user was already on
        // the box we ended on).
        if (_boxQueue.Count == 0)
        {
            FinalizeAggregation();
            BeginRestore();
            return;
        }

        if (now - _lastSendAt < SCAN_SEND_INTERVAL_SECONDS) return;

        var box = _boxQueue.Dequeue();
        _currentBox         = box;
        _currentBoxDeadline = now + SCAN_BOX_TIMEOUT_SECONDS;
        _lastSendAt         = now;
        try
        {
            // Align PlayerStateService.ActiveBox so the upcoming `.fam l`
            // FlushBoxContent keys correctly. Same pattern the summon path
            // uses.
            PlayerStateService.SetActiveBox(box);
            MessageService.EnqueueMessageSilent(string.Format(MessageService.BCCOM_FAM_SWITCH_BOX_FORMAT, box));
            MessageService.EnqueueMessageSilent(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
        }
        catch (Exception ex)
        {
            LogUtils.LogWarning($"VBloodScanner: failed to enqueue cb/l for '{box}': {ex.Message}");
            _completedBoxes++;
            _currentBox = null;
        }
    }

    /// <summary>Begin a fresh sweep. No-op while another scan is running.
    /// Wipes the existing VBloodCollection and reseeds from scratch so a
    /// .fam r deletion (which never sends an event we could subscribe to)
    /// is naturally subtracted on the next scan.</summary>
    public static void StartScan()
    {
        if (Scanning) return;
        _boxQueue.Clear();
        _accumulated.Clear();
        PlayerStateService.ResetVBloodCollection();

        _originalActiveBox = PlayerStateService.ActiveBox ?? "";
        _totalBoxes        = 0;
        _completedBoxes    = 0;
        _currentBox        = null;
        _lastSendAt        = UnityEngine.Time.realtimeSinceStartupAsDouble;
        _state             = State.AwaitingBoxList;

        try
        {
            MessageService.EnqueueMessageSilent(MessageService.BCCOM_FAM_BOXES);
        }
        catch (Exception ex)
        {
            LogUtils.LogWarning($"VBloodScanner: failed to send .fam boxes: {ex.Message}");
            AbortInternal();
            return;
        }

        LogUtils.LogInfo($"VBloodScanner: scan armed — waiting for .fam boxes reply (original active box='{_originalActiveBox}').");
        Fire(ScanStateChanged);
    }

    /// <summary>Stop any in-progress scan. Already-aggregated entries stay
    /// in VBloodCollection; the queue is dropped and the user's active box
    /// is restored on the next user navigation (we don't force a cb here
    /// because the user may have moved on already).</summary>
    public static void CancelScan()
    {
        if (!Scanning) return;
        _boxQueue.Clear();
        AbortInternal();
        LogUtils.LogInfo("VBloodScanner: scan cancelled by user.");
    }

    private static void AbortInternal()
    {
        FinalizeAggregation();
        _state       = State.Idle;
        _currentBox  = null;
        Fire(ScanStateChanged);
    }

    private static void OnBoxListChanged()
    {
        if (_state != State.AwaitingBoxList) return;
        var boxes = PlayerStateService.BoxList;
        if (boxes == null || boxes.Count == 0)
        {
            LogUtils.LogInfo("VBloodScanner: .fam boxes returned 0 boxes — nothing to scan.");
            AbortInternal();
            return;
        }
        foreach (var b in boxes)
            if (!string.IsNullOrWhiteSpace(b)) _boxQueue.Enqueue(b);
        _totalBoxes = _boxQueue.Count;
        _state = State.SweepingBoxes;
        _lastSendAt = 0; // dispatch first pair immediately
        LogUtils.LogInfo($"VBloodScanner: sweeping {_totalBoxes} box(es).");
        Fire(ScanStateChanged);
    }

    private static void OnBoxContentsChanged()
    {
        // 0.17.1: PASSIVE scan. Outside a full sweep, whenever a box's contents
        // arrive because the user navigated to it (Familiar Browser / Boxes tab),
        // fold that box's V-Bloods into the collection so the V-Bloods tab fills in
        // as the player moves through boxes — no full sweep, no Eclipse conflict
        // (this is just reading box contents the user already requested). A "Scan
        // all" still walks EVERY box and reconciles deletions.
        if (_state != State.SweepingBoxes)
        {
            var box = PlayerStateService.ActiveBox;
            if (!string.IsNullOrEmpty(box)
                && PlayerStateService.BoxContents.TryGetValue(box, out var navEntries) && navEntries != null)
            {
                ProcessBoxEntries(box, navEntries);
            }
            return;
        }
        if (string.IsNullOrEmpty(_currentBox)) return;

        // The flush may belong to a different box if the user navigated
        // manually between our cb send and the reply. PlayerStateService.
        // ActiveBox is whatever FlushBoxContent keyed it under — we read
        // back the entries for _currentBox specifically.
        if (!PlayerStateService.BoxContents.TryGetValue(_currentBox, out var entries) || entries == null)
        {
            return;
        }
        ProcessBoxEntries(_currentBox, entries);
        _completedBoxes++;
        _currentBox = null;
        Fire(ScanStateChanged);
    }

    /// <summary>Walk a box's parsed familiar entries; for each entry whose
    /// name matches the V-Blood registry (basic or "Primal "-prefixed),
    /// stash a VBloodInstance under the canonical basename. We commit each
    /// box's contribution into VBloodCollection eagerly so the UI redraws
    /// as the scan progresses (vs. a single bulk commit at the end).</summary>
    private static void ProcessBoxEntries(string box, List<PlayerStateService.FamiliarBoxEntry> entries)
    {
        // Track which basenames had a new instance added this pass, so the
        // event fires only for actually-changed slots.
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;

            string baseName;
            bool   isPrimal;
            if (TryClassifyName(entry.Name, out baseName, out isPrimal))
            {
                if (!_accumulated.TryGetValue(baseName, out var list))
                {
                    list = new List<PlayerStateService.VBloodInstance>(1);
                    _accumulated[baseName] = list;
                }
                var inst = new PlayerStateService.VBloodInstance
                {
                    Box           = box,
                    Index         = entry.Index,
                    Level         = entry.Level,
                    Prestige      = entry.Prestige,
                    IsShiny       = entry.IsShiny,
                    ShinySchool   = entry.ShinySchool,
                    IsPrimal      = isPrimal,
                };
                // 0.17.1: update-in-place by (Box, Index) instead of always appending,
                // so re-processing a box (passive scan re-visit, or a familiar that
                // leveled up) refreshes its entry rather than duplicating it.
                int existing = list.FindIndex(x => x.Box == box && x.Index == entry.Index);
                if (existing >= 0) list[existing] = inst; else list.Add(inst);
                touched.Add(baseName);
            }
        }

        // Commit touched names to the public collection.
        foreach (var name in touched)
        {
            var snapshot = new PlayerStateService.VBloodCaptureStatus
            {
                Name       = name,
                Instances  = new List<PlayerStateService.VBloodInstance>(_accumulated[name]),
                LastScanAt = DateTime.UtcNow,
            };
            PlayerStateService.UpdateVBloodSlot(in snapshot);
        }
    }

    /// <summary>Match a Bloodcraft-localized name against the canonical
    /// V-Blood registry. Returns true and populates baseName / isPrimal
    /// when the name is recognized. Match is case-insensitive and tries
    /// both the bare name and the "Primal " prefix.</summary>
    private static bool TryClassifyName(string localizedName, out string baseName, out bool isPrimal)
    {
        const string primalPrefix = "Primal ";
        baseName = null;
        isPrimal = false;
        if (string.IsNullOrEmpty(localizedName)) return false;

        // Primal first — its base name is the suffix after the prefix.
        // 0.11.0: Bloodcraft server-side strips " the X" from the registry
        // name (e.g., "Adam the Firstborn" → "Primal Adam"; "Frostmaw the
        // Mountain Terror" → "Primal Frostmaw"). The previous "full-name
        // match only" logic missed every primal except for entries with
        // no " the " in the name. Now we try full-name first (defensive,
        // in case Bloodcraft changes its mind) then fall back to the
        // stem lookup that handles the dominant case.
        if (localizedName.StartsWith(primalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string suffix = localizedName.Substring(primalPrefix.Length).Trim();
            // 0.17.3: ResolveCanonical accepts exact registry names AND aliases
            // (full in-game names like "Nibbles the Putrid Rat" → "Putrid Rat").
            var resolvedPrimal = VBloodRegistry.ResolveCanonical(suffix);
            if (resolvedPrimal != null)
            {
                baseName = resolvedPrimal;
                isPrimal = true;
                return true;
            }
            if (VBloodRegistry.TryResolvePrimalStem(suffix, out var canonical))
            {
                baseName = canonical;
                isPrimal = true;
                return true;
            }
            return false;
        }
        // 0.17.3: alias-aware match fixes the Putrid Rat miscapture — the box list
        // carries "Nibbles the Putrid Rat" but the registry key is "Putrid Rat".
        var resolved = VBloodRegistry.ResolveCanonical(localizedName);
        if (resolved != null)
        {
            baseName = resolved;
            isPrimal = false;
            return true;
        }
        // 0.65.0: language-proof fallback. On a non-English client, Bloodcraft sends regular
        // V-Blood names in the server's language (e.g. Polish), which aren't in the English
        // registry — so the tab used to drop every regular V-Blood while primals (hardcoded
        // English "Primal X" by Bloodcraft) still matched. Resolve the localized name via the
        // client's own live localization table. See Resources/VBloodLocalization.cs.
        if (Resources.VBloodLocalization.TryResolveLocalized(localizedName, out var localizedCanonical))
        {
            baseName = localizedCanonical;
            isPrimal = false;
            return true;
        }
        return false;
    }

    private static void FinalizeAggregation()
    {
        // No-op today — promotion already happens incrementally in
        // ProcessBoxEntries. Reserved as a hook for future deduping /
        // sort-stability passes.
    }

    private static void BeginRestore()
    {
        // Skip the restore .fam cb if we're already on the original box
        // (happens naturally when the user only had one box, or the queue
        // ended on the same box they started on).
        if (!string.IsNullOrEmpty(_originalActiveBox)
            && !string.Equals(_originalActiveBox, PlayerStateService.ActiveBox, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                PlayerStateService.SetActiveBox(_originalActiveBox);
                MessageService.EnqueueMessageSilent(string.Format(MessageService.BCCOM_FAM_SWITCH_BOX_FORMAT, _originalActiveBox));
                MessageService.EnqueueMessageSilent(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
            }
            catch (Exception ex)
            {
                LogUtils.LogWarning($"VBloodScanner: failed to restore active box '{_originalActiveBox}': {ex.Message}");
            }
        }
        _lastSendAt = UnityEngine.Time.realtimeSinceStartupAsDouble;
        _state      = State.Restoring;
        Fire(ScanStateChanged);
    }

    private static void Fire(Action a)
    {
        if (a == null) return;
        try { a(); }
        catch (Exception ex) { LogUtils.LogError($"VBloodScanner event subscriber threw: {ex}"); }
    }
}
