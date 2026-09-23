using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Unity.IL2CPP;
using Raphael.Resources;
using Raphael.Utils;
using ProjectM.Network;

namespace Raphael.Services;

// INBOUND + OUTBOUND structured protocol shared with Bloodcraft (server) and Eclipse (client).
//
// Wire format (both directions):
//   <intermediate-message>;mac<base64-hmac-sha256>
// where intermediate-message is:
//   client -> server : "[ECLIPSE][<subType>]:<payload>"
//   server -> client : "[<subType>]:<payload>"   (server omits the [ECLIPSE] tag in responses)
//
// Sub-types (mirrors Eclipse-main/Patches/ClientChatSystemPatch.NetworkEventSubType):
//   0 = RegisterUser       client sends "VERSION;PLATFORM_ID" once after entering world
//   1 = ProgressToClient   server pushes player progress (XP, legacy, expertise, ...) periodically
//   2 = ConfigsToClient    server pushes config snapshot (max levels, multipliers) once on register
//
// MAC = HMAC-SHA256(UTF8(intermediate-message), shared-key). Shared key is loaded from
// embedded Resources/secrets.json (see SecretManager) - same pre-shared default that
// Bloodcraft and Eclipse ship.
//
// Lifecycle: Patches/ClientChatPatch drives this; this service is otherwise stateless.
public static class EclipseProtocolService
{
    public enum NetworkEventSubType
    {
        RegisterUser     = 0,
        ProgressToClient = 1,
        ConfigsToClient  = 2,
    }

    // Mirrors Eclipse-main's protocol version negotiation. Bloodcraft only
    // signs responses for clients that announce "1.3.x" today.
    public const string PROTOCOL_VERSION = "1.3.13";

    // BepInEx plugin GUID of the standalone Eclipse client mod (Eclipse-main).
    // Used to detect coexistence so we don't destroy the chat entity before
    // Eclipse's own ClientChatSystem prefix can read it. See IsEclipseModLoaded.
    public const string ECLIPSE_PLUGIN_GUID = "io.zfolmt.Eclipse";

    private static readonly Regex _regexEventPrefix = new(@"^\[(\d+)\]:", RegexOptions.Compiled);
    private static readonly Regex _regexMacSuffix   = new(@";mac([^;]+)$", RegexOptions.Compiled);

    // NOTE: do NOT add static ComponentType[] / NetworkEventType fields here.
    // Plugin.Load triggers this class's cctor (via Initialize -> set_SharedKey),
    // which runs BEFORE V Rising creates its ECS World. At that point
    // Unity.Entities.TypeManager isn't initialized, so any static-field
    // initializer that calls ComponentType.ReadOnly(...) NREs inside
    // TypeManager.FindTypeIndex and aborts plugin load entirely.
    //
    // Outbound protocol messages reuse MessageService.SendRaw, whose ECS-
    // related fields are only touched at first invocation (per-frame loop,
    // after the world exists), so they're safe there.

    public static bool   UserRegistered { get; private set; }
    public static bool   RegistrationPending { get; private set; }
    public static byte[] SharedKey { get; private set; }

    // 0.12.1: retry state for the registration handshake. v0.12.0 and prior
    // gated SendRegistration on `!Pending` — the very first call set Pending=
    // true and the gate never reopened. If the server's first ACK got lost
    // (or a slow server hadn't loaded Bloodcraft yet at our handshake time),
    // we'd stay stuck in "Detecting…" forever and the Bloodcraft tab group
    // would render as Unavailable even on a Bloodcraft server. Now we retry
    // up to REGISTRATION_MAX_ATTEMPTS times with REGISTRATION_RETRY_AFTER_SECONDS
    // between attempts. After the cap is hit RegistrationGaveUp flips true
    // and the tab group renders as Unavailable for real.
    public static bool   RegistrationGaveUp { get; private set; }
    private static float _registrationSentAt;
    private static int   _registrationAttemptCount;
    private const float  REGISTRATION_RETRY_AFTER_SECONDS = 5f;
    // 0.18.x server-switch follow-up: widened from 3 (~15s) → 6 (~30s) so high-latency / slow-loading
    // joins get more runway before the handshake gives up. After give-up the user recovers in one click
    // via the inline Bloodcraft diagnostic's "Re-check" (which Resets + re-registers).
    private const int    REGISTRATION_MAX_ATTEMPTS = 6;

    /// <summary>0.12.1: fired when UserRegistered transitions to true OR when
    /// RegistrationGaveUp flips true after the retry cap. UI subscribers
    /// (MainPanel.BuildTabStrip) refresh tab-group availability in place so
    /// the user sees Bloodcraft come online without re-opening the panel.</summary>
    public static event System.Action AvailabilityChanged;

    // Cached Eclipse-mod-coexistence flag. Resolved lazily on first inbound
    // chat tick — Plugin.Load runs before BepInEx finishes loading the other
    // plugins (alphabetical: Raphael < Eclipse), so the chainloader's Plugins
    // dictionary may not contain Eclipse yet at Plugin.Load time. By the time
    // the player is in-world and chat starts flowing, all plugins are loaded.
    private static bool _eclipseModChecked;
    private static bool _eclipseModLoaded;

    /// <summary>Load the shared HMAC key once at plugin startup. Returns true if a usable key was loaded.</summary>
    public static bool Initialize()
    {
        SharedKey = SecretManager.GetSharedKey();
        if (SharedKey == null || SharedKey.Length == 0)
        {
            LogUtils.LogWarning("Eclipse protocol: no shared key loaded — structured server data will not be processed.");
            return false;
        }
        LogUtils.LogInfo($"Eclipse protocol initialized with {SharedKey.Length}-byte shared key.");
        return true;
    }

    public static void Reset()
    {
        UserRegistered = false;
        RegistrationPending = false;
        RegistrationGaveUp = false;
        _registrationSentAt = 0f;
        _registrationAttemptCount = 0;
        _standDownLogged = false; // 0.17.1: re-log the stand-down on the next session
        // 0.15.1: also clear the one-shot probe flag so the next session's
        // ACK re-fires `.fam boxes` to redetect FamiliarSystem availability
        // on the new server.
        _familiarProbeScheduled = false;
        // 0.65.6: unregister a still-pending probe. Its closure removes whatever _familiarProbeAction
        // points at, so if a relog re-scheduled before it fired, the old closure was orphaned in
        // CoreUpdateBehavior.Actions and enqueued `.fam boxes` every frame. (Pure list op — teardown-safe.)
        if (_familiarProbeAction != null)
        {
            Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_familiarProbeAction);
            _familiarProbeAction = null;
        }
        // 0.15.0: per-feature detection is per-session. Resetting registration
        // (e.g. world exit / re-login) also clears the sticky-Enabled flags
        // and the settling timer so the next session starts fresh.
        PlayerStateService.ResetFeatureFlags();
    }

    // 0.15.1 friend-test follow-up: one-shot `.fam boxes` probe scheduled
    // after registration ACK. The probe runs silently (BchAuto category,
    // not shown in chat) and the existing chat-regex pipeline parses the
    // reply: a "Familiar Boxes" header lands → PlayerStateService.
    // UpdateBoxList fires → _everReceivedFamiliarBoxList flips true →
    // RecomputeFeatureFlagsFromLatest marks Familiar as enabled, which
    // un-flags the false-positive disabled detection that fires when
    // the user hasn't summoned a familiar in the 30 s settling window.
    private static bool _familiarProbeScheduled;
    private static System.Action _familiarProbeAction;
    // 0.17.1: hold the probe this long after the registration ACK. It used to fire
    // the very NEXT frame — right inside the busy login window where the Eclipse
    // config/progress flood is processed (twice, if the Eclipse MOD is also
    // installed and Raphael is in coexistence mode). That window is what tips the
    // latent Il2CppInterop GC-finalizer crash (the 0.16 crash; the Raphael+Eclipse
    // crash log ends EXACTLY at this probe's dispatch). Holding Raphael's own probe a
    // few seconds moves it off the GC peak. See [[project_v016_crash_investigation]].
    private const float FAMILIAR_PROBE_DELAY_SECONDS = 8f;
    private static float _familiarProbeFireAt;
    private static void ScheduleFamiliarSystemProbe()
    {
        if (_familiarProbeScheduled) return;
        _familiarProbeScheduled = true;
        _familiarProbeFireAt = UnityEngine.Time.realtimeSinceStartup + FAMILIAR_PROBE_DELAY_SECONDS;
        _familiarProbeAction = () =>
        {
            try
            {
                // Hold until the post-login flood has settled (and the UI is up).
                if (UnityEngine.Time.realtimeSinceStartup < _familiarProbeFireAt) return;
                if (!MessageService.IsInitialized) return; // wait until next frame
                MessageService.EnqueueMessageSilent(MessageService.BCCOM_FAM_BOXES);
                LogUtils.LogDiagnostic("Eclipse: dispatched deferred `.fam boxes` familiar-system probe (post-ACK quiet frame).");
                Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_familiarProbeAction);
                _familiarProbeAction = null;
            }
            catch (Exception ex)
            {
                LogUtils.LogError($"Eclipse: familiar-system probe failed: {ex}");
                // Unregister even on error so we don't spin every frame.
                Raphael.Behaviors.CoreUpdateBehavior.Actions.Remove(_familiarProbeAction);
                _familiarProbeAction = null;
            }
        };
        Raphael.Behaviors.CoreUpdateBehavior.Actions.Add(_familiarProbeAction);
    }

    /// <summary>
    /// True when the standalone Eclipse client mod (io.zfolmt.Eclipse) is also
    /// installed in this BepInEx profile. Result is cached after the first call.
    /// </summary>
    /// <remarks>
    /// Bloodcraft's server-side mod broadcasts a single MAC-signed
    /// ProgressToClient/ConfigsToClient stream per player. Both Raphael and Eclipse
    /// independently Harmony-prefix ClientChatSystem.OnUpdate and consume that
    /// stream. If Raphael destroys the chat entity after processing (the default
    /// when Eclipse isn't around — keeps chat noise out of the player's
    /// chat window), Eclipse's prefix sees a destroyed entity and renders
    /// zeroed-out bars. Callers should leave the entity intact when this
    /// returns true; Eclipse's own prefix will destroy it after parsing.
    /// </remarks>
    public static bool IsEclipseModLoaded()
    {
        if (_eclipseModChecked) return _eclipseModLoaded;
        try
        {
            var plugins = IL2CPPChainloader.Instance?.Plugins;
            _eclipseModLoaded = plugins != null && plugins.ContainsKey(ECLIPSE_PLUGIN_GUID);
            if (_eclipseModLoaded)
                LogUtils.LogInfo($"Eclipse mod ({ECLIPSE_PLUGIN_GUID}) detected alongside Raphael — leaving MAC-signed chat entities intact so Eclipse can also process them.");
            else
                LogUtils.LogDebug($"Eclipse mod ({ECLIPSE_PLUGIN_GUID}) not installed; Raphael will destroy MAC-signed chat entities after processing.");
        }
        catch (Exception ex)
        {
            LogUtils.LogWarning($"Eclipse mod detection failed; assuming not present: {ex.Message}");
            _eclipseModLoaded = false;
        }
        _eclipseModChecked = true;
        return _eclipseModLoaded;
    }

    // 0.17.1 EXPERIMENT (Eclipse coexistence): when the Eclipse mod is installed,
    // Raphael "stands down" from the PASSIVE Bloodcraft layer — it does NOT register
    // for the Eclipse-protocol data stream, does NOT decode protocol broadcasts,
    // and does NOT fire background auto-queries. This keeps Raphael from doing ANY
    // Bloodcraft ECS work in the volatile login window, which is what perturbs
    // Eclipse's fragile HUD BufferLookup and crashes the client. Raphael still works as
    // a command console (user-clicked Bloodcraft/Kindred commands + their chat-reply
    // parsing) and its standalone chat window; Eclipse provides the live passive
    // HUD. See [[project_v016_crash_investigation]].
    internal static bool StandDownForEclipse() => IsEclipseModLoaded();
    private static bool _standDownLogged;

    // ---------- Inbound (server -> client) ----------

    /// <summary>
    /// Try to interpret a chat-message string as a signed Eclipse-protocol message.
    /// On match (MAC valid + recognized event id) routes the payload to PlayerStateService
    /// and returns true; the caller should then suppress the chat entity so it doesn't
    /// land in the player's chat window.
    /// </summary>
    public static bool TryHandleServerMessage(string raw)
    {
        if (SharedKey == null || string.IsNullOrEmpty(raw)) return false;

        var macMatch = _regexMacSuffix.Match(raw);
        if (!macMatch.Success) return false;

        string receivedMac = macMatch.Groups[1].Value;
        string intermediate = _regexMacSuffix.Replace(raw, string.Empty);

        if (!VerifyMac(intermediate, receivedMac, SharedKey))
        {
            // Could be a normal player message containing literal ";mac..." — quiet at info level.
            LogUtils.LogDebug($"Eclipse: MAC mismatch on candidate message — ignoring. ({raw.Length} chars)");
            return false;
        }

        var idMatch = _regexEventPrefix.Match(intermediate);
        if (!idMatch.Success ||
            !int.TryParse(idMatch.Groups[1].Value, out int subTypeId))
        {
            LogUtils.LogWarning($"Eclipse: MAC-verified message has no [<int>]: prefix — {intermediate}");
            return true; // we consumed it; just unable to route
        }

        string payload = _regexEventPrefix.Replace(intermediate, string.Empty);

        try
        {
            switch ((NetworkEventSubType)subTypeId)
            {
                case NetworkEventSubType.ProgressToClient:
                    HandleProgressMessage(payload);
                    break;
                case NetworkEventSubType.ConfigsToClient:
                    HandleConfigMessage(payload);
                    bool wasRegistered = UserRegistered;
                    UserRegistered = true; // server has accepted us
                    RegistrationPending = false;
                    RegistrationGaveUp = false; // ACK arrived even if late; clear the give-up flag
                    if (!wasRegistered)
                    {
                        LogUtils.LogInfo($"Eclipse: server acknowledged registration on attempt #{_registrationAttemptCount}; structured data flowing.");
                        LogUtils.LogDiagnostic($"Eclipse state transition: UserRegistered false -> true (attempt #{_registrationAttemptCount}). Config payload length={payload?.Length ?? 0}.");
                        try { AvailabilityChanged?.Invoke(); }
                        catch (Exception ex) { LogUtils.LogWarning($"Eclipse: AvailabilityChanged subscriber threw: {ex.Message}"); }
                        // 0.15.1 friend-test follow-up: schedule a silent
                        // `.fam boxes` probe so PlayerStateService can detect
                        // FamiliarSystem-enabled even when the user hasn't
                        // summoned a familiar yet. See PlayerStateService.
                        // _everReceivedFamiliarBoxList for the rationale.
                        ScheduleFamiliarSystemProbe();
                    }
                    break;
                default:
                    LogUtils.LogWarning($"Eclipse: unknown event id {subTypeId}");
                    break;
            }
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"Eclipse: failed handling subType={subTypeId}: {ex}");
        }
        return true;
    }

    private static void HandleProgressMessage(string csv)
    {
        // Field layout per Eclipse-main DataService.ParsePlayerData (Bloodcraft v1.3.x):
        //   [0..3]   Experience: progressPercent, level, prestige, classId          (4)
        //   [4..8]   Legacy:     progressPercent, level, prestige, type, bonusStats  (5)
        //   [9..13]  Expertise:  progressPercent, level, prestige, type, bonusStats  (5)
        //   [14..18] Familiar:   progressPercent, level, prestige, name, packedStats (5)
        //   [19..34] Profession: 8 professions x (progressPercent, level)           (16)
        //   [35..39] Daily quest: type, progress, goal, target, isVBlood             (5)
        //   [40..44] Weekly quest: type, progress, goal, target, isVBlood            (5)
        //   [45]     Shift spell PrefabGUID                                          (1)
        //   Total: 46 fields.
        //
        // Defensive parsing: each subsection only fires if its fields are present.
        // Bloodcraft may add fields in future versions - we won't crash on a longer
        // payload, just on a shorter one.
        //
        // 0.65.1: the payload is a comma-joined CSV, but Bloodcraft inserts three
        // FREE-TEXT fields into it un-escaped - the familiar name (p[17]) and the
        // daily/weekly quest target names (p[38]/p[43]) - via `GetLocalizedName()`.
        // On a localized client those names can CONTAIN a comma (e.g. Polish
        // "Alfa, Biały Wilk" = "Alpha the White Wolf", or a gather target like
        // "Klejnot krwi, poziom 1"). Every such comma shifts all downstream fields
        // by one, so the familiar stats field becomes a name fragment (HP/PP/SP read
        // as 0) and the quest target reads a numeric column (shows "only numbers").
        // English names have no commas, which is why only localized clients saw it
        // (Discord reports from Kruczarz on a Polish client). NormalizeProgressFields
        // re-aligns the fields when the split yields more than the expected count;
        // it's a no-op (returns the same array) for the normal 46-field case, so
        // every currently-working client is unaffected.
        var p = NormalizeProgressFields(csv.Split(','));
        int n = p.Length;

        if (n < 4)
        {
            LogUtils.LogWarning($"Eclipse: ProgressToClient payload too short ({n} fields).");
            return;
        }

        // [0..3] Experience
        PlayerStateService.UpdateExperience(new PlayerStateService.ExperienceState
        {
            Progress = PlayerStateService.ParseProgress(p[0]),
            Level    = PlayerStateService.ParseInt(p[1]),
            Prestige = PlayerStateService.ParseInt(p[2]),
            Class    = (PlayerStateService.PlayerClass)PlayerStateService.ParseInt(p[3]),
        });

        // [4..8] Legacy
        if (n >= 9)
        {
            PlayerStateService.UpdateLegacy(new PlayerStateService.LegacyState
            {
                Progress      = PlayerStateService.ParseProgress(p[4]),
                Level         = PlayerStateService.ParseInt(p[5]),
                Prestige      = PlayerStateService.ParseInt(p[6]),
                Type          = (PlayerStateService.BloodType)PlayerStateService.ParseInt(p[7]),
                BonusStatsRaw = p[8],
            });
        }

        // [9..13] Expertise
        if (n >= 14)
        {
            PlayerStateService.UpdateExpertise(new PlayerStateService.ExpertiseState
            {
                Progress      = PlayerStateService.ParseProgress(p[9]),
                Level         = PlayerStateService.ParseInt(p[10]),
                Prestige      = PlayerStateService.ParseInt(p[11]),
                Type          = (PlayerStateService.WeaponType)PlayerStateService.ParseInt(p[12]),
                BonusStatsRaw = p[13],
            });
        }

        // [14..18] Familiar
        if (n >= 19)
        {
            // 0.10.8: HasActive is the raw "name field is non-empty" signal —
            // before the empty-string mask gets replaced with the "Familiar"
            // placeholder for display. The Summon flow on the V-Bloods tab
            // uses this to decide whether to pre-issue `.fam ub`.
            string famNameRaw = p[17];
            bool   famActive  = !string.IsNullOrEmpty(famNameRaw);
            PlayerStateService.UpdateFamiliar(new PlayerStateService.FamiliarState
            {
                Progress  = PlayerStateService.ParseProgress(p[14]),
                Level     = Math.Max(1, PlayerStateService.ParseInt(p[15])),
                Prestige  = PlayerStateService.ParseInt(p[16]),
                Name      = famActive ? famNameRaw : "Familiar",
                RawStats  = p[18] ?? string.Empty,
                HasActive = famActive,
            });
        }

        // [19..34] Profession (8 x 2)
        if (n >= 35)
        {
            PlayerStateService.UpdateProfession(new PlayerStateService.ProfessionState
            {
                EnchantingProgress    = PlayerStateService.ParseProgress(p[19]),
                EnchantingLevel       = PlayerStateService.ParseInt(p[20]),
                AlchemyProgress       = PlayerStateService.ParseProgress(p[21]),
                AlchemyLevel          = PlayerStateService.ParseInt(p[22]),
                HarvestingProgress    = PlayerStateService.ParseProgress(p[23]),
                HarvestingLevel       = PlayerStateService.ParseInt(p[24]),
                BlacksmithingProgress = PlayerStateService.ParseProgress(p[25]),
                BlacksmithingLevel    = PlayerStateService.ParseInt(p[26]),
                TailoringProgress     = PlayerStateService.ParseProgress(p[27]),
                TailoringLevel        = PlayerStateService.ParseInt(p[28]),
                WoodcuttingProgress   = PlayerStateService.ParseProgress(p[29]),
                WoodcuttingLevel      = PlayerStateService.ParseInt(p[30]),
                MiningProgress        = PlayerStateService.ParseProgress(p[31]),
                MiningLevel           = PlayerStateService.ParseInt(p[32]),
                FishingProgress       = PlayerStateService.ParseProgress(p[33]),
                FishingLevel          = PlayerStateService.ParseInt(p[34]),
            });
        }

        // [35..39] Daily quest
        if (n >= 40)
        {
            PlayerStateService.UpdateDailyQuest(new PlayerStateService.QuestState
            {
                Target     = (PlayerStateService.TargetType)PlayerStateService.ParseInt(p[35]),
                Progress   = PlayerStateService.ParseInt(p[36]),
                Goal       = PlayerStateService.ParseInt(p[37]),
                TargetName = p[38] ?? string.Empty,
                IsVBlood   = PlayerStateService.ParseBool(p[39]),
            });
        }

        // [40..44] Weekly quest
        if (n >= 45)
        {
            PlayerStateService.UpdateWeeklyQuest(new PlayerStateService.QuestState
            {
                Target     = (PlayerStateService.TargetType)PlayerStateService.ParseInt(p[40]),
                Progress   = PlayerStateService.ParseInt(p[41]),
                Goal       = PlayerStateService.ParseInt(p[42]),
                TargetName = p[43] ?? string.Empty,
                IsVBlood   = PlayerStateService.ParseBool(p[44]),
            });
        }

        // [45] Shift spell PrefabGUID
        if (n >= 46)
        {
            PlayerStateService.UpdateShiftSpell(new PlayerStateService.ShiftSpellState
            {
                SpellIndex = PlayerStateService.ParseInt(p[45]),
            });
        }

        // 0.15.0: refresh per-system availability flags. The individual
        // Update* mutators above wrote the latest data into the state
        // structs; this call walks them and updates sticky-Enabled flags
        // + the settling timer. Detection is best-effort heuristic — see
        // PlayerStateService.RecomputeFeatureFlagsFromLatest. Friend-test
        // 0.14.0 root cause: Bloodcraft servers with only QuestSystem +
        // ProfessionSystem enabled (plus at least one of the five "core"
        // systems so Core.Eclipsed=true and broadcasts happen) were
        // sending zeroed-out CSV fields for the disabled systems. Raphael
        // rendered the empty data without a hint that the feature was
        // disabled, leaving users staring at "Level 0 / 0%" forever.
        PlayerStateService.RecomputeFeatureFlagsFromLatest();
    }

    // Expected ProgressToClient field count for the v1.3.x layout documented in
    // HandleProgressMessage (indices 0..45).
    private const int EXPECTED_PROGRESS_FIELDS = 46;

    /// <summary>
    /// 0.65.1: recover the fixed 46-field ProgressToClient layout when an
    /// un-escaped comma inside one of the three free-text fields (familiar name,
    /// daily target, weekly target) has split the payload into MORE tokens than
    /// expected. See the root-cause note at the call site.
    ///
    /// Fast path: when the token count is already the expected value (or SHORTER —
    /// an older/leaner Bloodcraft the existing `if (n >= X)` guards handle), the
    /// input array is returned unchanged so no currently-working client is touched.
    ///
    /// Recovery relies on the layout's rigid anchors, none of which a localized
    /// name can impersonate:
    ///   • indices 0..16 are always numeric/structured (no free text before p[17]);
    ///   • the familiar-stats field (p[18]) is empty OR a run of ≥10 digits
    ///     (server packs MaxHealth D4 + PhysicalPower D3 + SpellPower D3);
    ///   • the two quest isVBlood fields are the only bool tokens (matched
    ///     case-insensitively — the server sends lowercase "false" for non-kill
    ///     quests, capitalized "True"/"False" only for kill quests), and the
    ///     weekly one is always second-to-last (only the numeric shift-spell
    ///     field follows it).
    /// If any anchor fails to line up we return the original tokens untouched and
    /// let the naive index parse run — recovery never makes things worse than today.
    /// </summary>
    private static string[] NormalizeProgressFields(string[] tokens)
    {
        try
        {
            int n = tokens.Length;
            if (n <= EXPECTED_PROGRESS_FIELDS) return tokens; // fast path / short payload

            // 0.65.2: MUST be case-insensitive. Bloodcraft's EclipseService.GetQuestData
            // emits capitalized "True"/"False" ONLY for kill quests (type 0) whose target
            // is a known prefab; for every non-kill quest (gather/craft/etc.) and as its
            // default it emits LOWERCASE "false". If we only matched the capitalized forms,
            // a player whose daily/weekly quest was a non-kill quest had NO recognizable
            // isVBlood anchor, so this whole recovery bailed to the naive parse — which is
            // wrong for a comma-shifted payload. That surfaced as familiar-specific HP=0 +
            // garbled quests on localized clients (Kruczarz, Polish): only familiars whose
            // localized name contains a comma trip the recovery path, so only they broke,
            // and only while a non-kill quest was active. bool.TryParse on the read side is
            // already case-insensitive, so accepting either casing here loses nothing.
            static bool IsBool(string s) =>
                string.Equals(s, "True", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "False", StringComparison.OrdinalIgnoreCase);
            static bool IsPackedStats(string s)
            {
                if (string.IsNullOrEmpty(s) || s.Length < 10) return false;
                foreach (var c in s) if (c < '0' || c > '9') return false;
                return true;
            }

            var r = new string[EXPECTED_PROGRESS_FIELDS];

            // [0..16] fixed numeric/structured block — nothing free-text precedes p[17].
            for (int i = 0; i <= 16; i++) r[i] = tokens[i];

            // Rigid tail: [45] shift spell, [44] weekly isVBlood.
            if (!IsBool(tokens[n - 2])) return tokens; // anchor missing → bail to naive
            r[45] = tokens[n - 1];
            r[44] = tokens[n - 2];

            // Daily isVBlood = the last "True"/"False" token strictly before the weekly one.
            int bi1 = -1;
            for (int i = n - 3; i >= 17; i--) { if (IsBool(tokens[i])) { bi1 = i; break; } }
            if (bi1 < 0) return tokens;
            r[39] = tokens[bi1];

            // Weekly section sits between daily isVBlood (bi1) and weekly isVBlood (n-2):
            //   [type][progress][goal][target(≥1 token, may hold commas)].
            if (bi1 + 4 > n - 2) return tokens; // no room for weekly target
            r[40] = tokens[bi1 + 1];
            r[41] = tokens[bi1 + 2];
            r[42] = tokens[bi1 + 3];
            r[43] = string.Join(",", tokens, bi1 + 4, (n - 2) - (bi1 + 4));

            // Familiar name (free text from p[17]) / familiar stats boundary.
            int statsIdx;
            if (string.IsNullOrEmpty(tokens[17]))
            {
                // No familiar bound: name and stats are both empty and un-shifted.
                statsIdx = 18;
                r[17] = tokens[17];
                r[18] = tokens[18];
            }
            else
            {
                // Familiar active: name may span several tokens; the first ≥10-digit
                // run after it is the packed-stats field.
                statsIdx = -1;
                for (int j = 18; j < bi1; j++) { if (IsPackedStats(tokens[j])) { statsIdx = j; break; } }
                if (statsIdx < 0) return tokens;
                r[17] = string.Join(",", tokens, 17, statsIdx - 17);
                r[18] = tokens[statsIdx];
            }

            // [19..34] professions (16 numeric fields) immediately follow the stats field.
            int profStart = statsIdx + 1;
            if (profStart + 16 + 3 > bi1) return tokens; // no room for prof + daily type/prog/goal
            for (int i = 0; i < 16; i++) r[19 + i] = tokens[profStart + i];

            // Daily quest: [type][progress][goal] then the free-text target up to bi1-1.
            int dType = profStart + 16;
            r[35] = tokens[dType];
            r[36] = tokens[dType + 1];
            r[37] = tokens[dType + 2];
            if (dType + 3 > bi1) return tokens; // no room for daily target
            r[38] = string.Join(",", tokens, dType + 3, bi1 - (dType + 3));

            LogUtils.LogDebug($"Eclipse: recovered comma-corrupted progress payload ({n} → {EXPECTED_PROGRESS_FIELDS} fields).");
            return r;
        }
        catch (Exception ex)
        {
            // Any surprise in the anchoring math falls back to the naive tokens so we
            // degrade to today's behavior rather than dropping the update.
            LogUtils.LogDebug($"Eclipse: NormalizeProgressFields fell back to naive parse: {ex.Message}");
            return tokens;
        }
    }

    private static void HandleConfigMessage(string csv)
    {
        var parts = csv.Split(',');
        // Eclipse-main ConfigDataV1_3 takes the first 9 fields as scalars:
        //   prestigeMult, classMult, maxPlayer, maxLegacy, maxExpertise, maxFamiliar, maxProf(unused), extraRecipes, primalCost
        // followed by 12 weaponStats + 12 bloodStats + classSynergies. We only need the max-level scalars right now.
        if (parts.Length < 7)
        {
            LogUtils.LogWarning($"Eclipse: ConfigsToClient payload too short ({parts.Length} fields).");
            return;
        }

        var cfg = new PlayerStateService.ServerConfig
        {
            Loaded            = true,
            MaxPlayerLevel    = PlayerStateService.ParseInt(parts[2]),
            MaxLegacyLevel    = PlayerStateService.ParseInt(parts[3]),
            MaxExpertiseLevel = PlayerStateService.ParseInt(parts[4]),
            MaxFamiliarLevel  = PlayerStateService.ParseInt(parts[5]),
        };
        PlayerStateService.UpdateConfig(cfg);

        // 0.16: custom recipes. Bloodcraft broadcasts only a boolean flag (field
        // 7) + a primal-cost item GUID (field 8); the recipe TABLE itself is
        // hard-coded in both Eclipse and Raphael and applied client-side so the
        // recipes show in the crafting stations. Skip when the Eclipse mod is
        // installed (it applies these same mutations itself — applying twice
        // would duplicate them) or when the user disabled the feature.
        if (parts.Length > 7
            && Config.Settings.EnableCustomRecipes
            && !IsEclipseModLoaded()
            && bool.TryParse(parts[7], out bool extraRecipes) && extraRecipes)
        {
            int primalHash = parts.Length > 8 ? PlayerStateService.ParseInt(parts[8]) : 0;
            // 0.16.1: defer off this (login-critical) frame — see RecipeService.ScheduleApply.
            RecipeService.ScheduleApply(new Stunlock.Core.PrefabGUID(primalHash));
        }
    }

    // ---------- Outbound (client -> server) ----------

    /// <summary>Send the RegisterUser handshake. Called every frame from the
    /// chat patch once the player is in-world; the early-out gates below
    /// keep that to one attempt per REGISTRATION_RETRY_AFTER_SECONDS until
    /// either the server ACKs (UserRegistered=true) or we give up after
    /// REGISTRATION_MAX_ATTEMPTS (RegistrationGaveUp=true).</summary>
    public static void SendRegistration()
    {
        if (SharedKey == null) return;
        if (UserRegistered) return;
        if (RegistrationGaveUp) return;

        // 0.65.0 RELOG-SPAM FIX (defense-in-depth): if the admin has explicitly declared this
        // server has no Bloodcraft (BloodcraftAvailability=Off), never emit the handshake at all.
        // Nothing would consume it, so on a non-Bloodcraft server it'd only be chat noise. Auto/On
        // still register (via the self-whisper below, which keeps even those from leaking).
        if (Config.Settings.BloodcraftAvailability == Config.Settings.ModAvailability.Off) return;

        // 0.17.1 EXPERIMENT: stand down from the passive Bloodcraft layer when the
        // Eclipse mod is present — never register, so Raphael receives no structured
        // stream and does no protocol ECS work in the login window. The UI still
        // sends user-clicked commands (command-console mode); Eclipse owns the HUD.
        if (StandDownForEclipse())
        {
            if (!_standDownLogged)
            {
                _standDownLogged = true;
                LogUtils.LogInfo("Eclipse present: Raphael standing down from the passive Bloodcraft layer (no protocol registration). Use the UI buttons to send commands; Eclipse provides the live HUD.");
            }
            return;
        }

        // 0.12.1: retry on pending. v0.11.x and earlier had a single-attempt
        // gate (`!UserRegistered && !RegistrationPending`) — once Pending flipped
        // true, no further sends EVER. If the server's ACK was dropped or the
        // server hadn't loaded Bloodcraft yet at our first handshake, the user
        // would see "Bloodcraft Unavailable" for the rest of the session.
        if (RegistrationPending)
        {
            float elapsed = UnityEngine.Time.realtimeSinceStartup - _registrationSentAt;
            if (elapsed < REGISTRATION_RETRY_AFTER_SECONDS) return;
            // Window elapsed without ACK — check cap, then retry.
            if (_registrationAttemptCount >= REGISTRATION_MAX_ATTEMPTS)
            {
                RegistrationGaveUp = true;
                RegistrationPending = false;
                LogUtils.LogInfo($"Eclipse: gave up after {_registrationAttemptCount} registration attempts without ACK. Marking Bloodcraft as unavailable (use BloodcraftAvailability=On in .cfg to override).");
                LogUtils.LogDiagnostic($"Eclipse state transition: RegistrationGaveUp false -> true after {_registrationAttemptCount} attempts. The diagnostic panel should be visible in the BLOODCRAFT rail.");
                try { AvailabilityChanged?.Invoke(); }
                catch (Exception ex) { LogUtils.LogWarning($"Eclipse: AvailabilityChanged subscriber threw: {ex.Message}"); }
                return;
            }
            // Fall through to send another attempt.
        }

        try
        {
            if (!MessageService.IsInitialized)
            {
                LogUtils.LogDebug("Eclipse: registration deferred — MessageService not yet bound to character/user.");
                return;
            }

            // Build the payload Eclipse uses: "<PROTOCOL_VERSION>;<PlatformId>".
            ulong platformId = MessageService.LocalUser.Read<User>().PlatformId;
            string innerPayload = $"{PROTOCOL_VERSION};{platformId}";
            string intermediate = $"[ECLIPSE][{(int)NetworkEventSubType.RegisterUser}]:{innerPayload}";
            string mac = GenerateMac(intermediate, SharedKey);
            string signed = $"{intermediate};mac{mac}";

            _registrationSentAt = UnityEngine.Time.realtimeSinceStartup;
            _registrationAttemptCount++;
            RegistrationPending = true;
            // 0.65.0: send as a self-whisper, not Local — a real Bloodcraft server still consumes it
            // by content, but a non-Bloodcraft server can't leak it to nearby players. See
            // MessageService.SendRawSelfWhisper.
            MessageService.SendRawSelfWhisper(signed);
            LogUtils.LogInfo($"Eclipse: registration attempt #{_registrationAttemptCount}/{REGISTRATION_MAX_ATTEMPTS} sent (platformId={platformId}).");
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"Eclipse: SendRegistration failed: {ex}");
        }
    }

    // ---------- HMAC plumbing ----------

    private static bool VerifyMac(string message, string receivedMacBase64, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        byte[] computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        string expected = Convert.ToBase64String(computed);

        // Constant-time compare to avoid timing leaks (consistent with upstream).
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(receivedMacBase64));
    }

    private static string GenerateMac(string message, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        byte[] computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToBase64String(computed);
    }
}
