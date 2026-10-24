using System.Collections.Generic;
using System.Globalization;
using Raphael.Utils;

namespace Raphael.Services.Beelzebub;

// The ONLY layer that talks to the Beelzebub server mod. Issues `.beelz …`
// chat commands via MessageService. No parsing happens here.
//
// Reads (.beelz api …) and auto-refreshes go out SILENT (EnqueueMessageSilent):
// their replies are [BEELZ:*] lines that ClientChatPatch intercepts + destroys
// before they reach the chat window anyway, and the command echo shouldn't
// clutter chat. Player-initiated MUTATIONS go out VISIBLE (EnqueueMessage) so
// the user sees Beelzebub's human-text confirmation ("Slot 1 assigned to …").
internal static class BeelzClient
{
    // The weapon families Beelzebub's `weapon-grant`/`weapon-unslot` accept as an
    // EXPLICIT bucket name (case-insensitive). Mirrors Services/WeaponFamily.cs minus
    // None/Magic (both rejected by the command). Order = the group dropdown order;
    // "Unarmed" is the spellcasting/fists set. The universal ("any") bucket is NOT in
    // this list — it's a separate selector entry handled by the loadout UI.
    // NOTE: DualHammers was REMOVED in Beelzebub v0.49.0 — it was unobtainable cut content
    // (no craftable weapon in V Rising) and weapon-grant/weapon-slot no longer accept it. The
    // current valid set is these 17 families (handoff §"v0.49.0").
    public static readonly string[] WeaponFamilies =
    {
        "Sword", "GreatSword", "Axe", "Mace", "Spear", "Daggers", "Crossbow",
        "Longbow", "Pistols", "Reaper", "Whip", "Claws", "Pollaxe", "Slashers", "TwinBlades",
        "Unarmed", "FishingPole",
    };

    // The vanilla shapeshift-wheel forms that take a per-form loadout (Beelz v0.59+). `.beelz form-grant`/
    // `form-unslot` accept these or "auto". These are the forms a player can TRADITIONALLY switch into via
    // the shapeshift wheel. Werewolf / Gargoyle (and Golem) are deliberately EXCLUDED: in Beelzebub those
    // are boss TRANSFORMATIONS entered via `.beelz transform` (managed on the Transforms tab + its own
    // `.beelz tform` loadout editor), NOT shapeshift forms — so they don't belong in the per-form picker.
    // NOTE: "Mounted" (riding a horse) is a loadout form too (Beelz v0.101/api23), gated server-side by
    // Forms_CustomAbilities_Enabled. Unlike the shapeshift-wheel forms it only accepts three slots: 5/6/7
    // (R / C / ultimate) on Beelz v0.137.3+ (api34), 3/6/7 before that — the horse owns the riding controls —
    // so the loadout UI restricts its slot rows (see FormAllowedSlots). form-grant/form-unslot accept "mounted" like any other form.
    public static readonly string[] Forms =
    {
        "Wolf", "Bear", "Rat", "Spider", "Toad", "Mounted",
    };

    // Slots a given form's loadout accepts. Most forms take all 8 (primary/1-6/ultimate); "Mounted" only
    // takes 5/6/7 on api34+ (Beelz v0.137.3 moved the saddle off slot 3, which the client never draws) and
    // 3/6/7 on older servers. Returns null = no restriction (all slots allowed).
    public static int[] FormAllowedSlots(string form)
        => (!string.IsNullOrEmpty(form) && form.Equals("Mounted", System.StringComparison.OrdinalIgnoreCase))
            ? (BeelzState.SupportsSaddleRCT ? new[] { 5, 6, 7 } : new[] { 3, 6, 7 })
            : null;

    // ---- read API (machine-readable replies) ----
    public const string API_VERSION   = ".beelz api version";
    public const string API_BCH_ON     = ".beelz api bch on";
    public const string API_BCH_OFF    = ".beelz api bch off";
    public const string API_LIST       = ".beelz api list";
    public const string API_SLOTS      = ".beelz api slots";
    public const string API_TRANSFORMS = ".beelz api transforms";
    public const string API_ACTIVE     = ".beelz api active";
    public const string API_PROGRESS   = ".beelz api progress";
    public const string API_HOTKEYS    = ".beelz api hotkeys";
    public const string API_RULES      = ".beelz api rules";
    public const string API_CONFIG     = ".beelz api config";
    public const string API_COOLDOWNS  = ".beelz api cooldowns";
    public const string API_CATALOG    = ".beelz api catalog";
    public const string API_INFO_GUID  = ".beelz api info-guid";

    /// <summary>Send a machine read / silent auto-refresh.</summary>
    public static void Send(string cmd) { DiagLogOut(cmd, silent: true); MessageService.EnqueueMessageSilent(cmd); }

    /// <summary>Send a player-initiated mutation (reply visible in chat).</summary>
    public static void SendUser(string cmd) { DiagLogOut(cmd, silent: false); MessageService.EnqueueMessage(cmd); }

    // Diagnostic trail (off by default): logs every outbound `.beelz …` command to the
    // BepInEx LogOutput.log so a tester can copy/paste the exact command stream to the
    // mod author. Fires when EITHER the Beelzebub diagnostics toggle OR the global
    // DiagnosticMode is on; one bool check + early return when both are off.
    internal static void DiagLogOut(string cmd, bool silent)
    {
        if (!BeelzDiag.Enabled) return;
        LogUtils.LogInfo($"[Beelz][diag] >> {(silent ? "(read) " : "(cmd)  ")}{cmd}");
    }

    // ---- read helpers ----
    public static void RequestVersion()    => Send(API_VERSION);
    public static void Subscribe()         => Send(API_BCH_ON);
    public static void RequestList()       => Send(API_LIST);
    public static void RequestSlots()      => Send(API_SLOTS);
    public static void RequestTransforms() => Send(API_TRANSFORMS);
    public static void RequestActive()     => Send(API_ACTIVE);
    public static void RequestProgress()   => Send(API_PROGRESS);
    public static void RequestHotkeys()    => Send(API_HOTKEYS);
    public static void RequestConfig()     => Send(API_CONFIG);
    public static void RequestCooldowns()  => Send(API_COOLDOWNS);
    public static void RequestTransformConfig() => Send(".beelz api transform-config");
    // NOTE: Beelzebub's bestiary pages are 0-based (default page=0). Passing page 1
    // skips the first 40 units — which read as an empty collection on small servers.
    public static void RequestBestiary(int page = 0) => Send($".beelz api bestiary {page.ToString(CultureInfo.InvariantCulture)}");
    public static void RequestInfo(int index)        => Send($".beelz api info {index.ToString(CultureInfo.InvariantCulture)}");
    // Beelz v0.84: full tooltip data keyed by a=<abilityGuid> (chunked, same as `api info`). The fix
    // for "No name"/generic tooltips on active-bar abilities that have no capture index.
    public static void RequestInfoGuid(string abilityGuid) => Send($"{API_INFO_GUID} {abilityGuid}");
    // Full ability matrix (paginated 40/page, 0-based). Powers the collector checklist +
    // the loadout metadata columns/Kind filter — one scan replaces per-ability `api info`.
    public static void RequestCatalogSummary()       => Send(API_CATALOG);
    // Beelz v0.101/api23 + v0.116/api27: optional server-side load filter — `<page> <filter> <value>`.
    // filter ∈ weapon|cat|unit|form|search (api23) plus tag|reviewstatus|tier|vblood (api27). Filtering
    // happens BEFORE pagination, so total=/pages= on [BEELZ:end] reflect the filtered subset. Omit the
    // filter (null/empty) → the full list, exactly as before (back-compatible with old servers).
    public static void RequestCatalogAbilities(int page = 0, string filter = null, string value = null)
        => Send(BuildCatalogCmd("abilities", page, filter, value));
    // Beelz v0.100 (ApiVersion 21): ADMIN-only catalog scope — EVERY real ability group regardless of
    // enable/deny/difficulty (for the Admin: Abilities config table), each row tagged enabled=. Distinct
    // end marker cmd=catalog-abilities-all so it doesn't mix with the player `catalog abilities` scope.
    // Same [BEELZ:catalog-ability] line format + chunking; paginated 40/page, 0-based. Same filter support.
    public static void RequestCatalogAbilitiesAll(int page = 0, string filter = null, string value = null)
        => Send(BuildCatalogCmd("abilities-all", page, filter, value));

    private static string BuildCatalogCmd(string scope, int page, string filter, string value)
    {
        string p = page.ToString(CultureInfo.InvariantCulture);
        return (string.IsNullOrEmpty(filter) || string.IsNullOrEmpty(value))
            ? $".beelz api catalog {scope} {p}"
            : $".beelz api catalog {scope} {p} {filter} {value}";
    }
    // Beelz v0.100 / api22: structured transform-loadout reads (replace the human-text `tform abilities`
    // parse + give the player's CURRENT binds). <unit> = the transform's index, name, or GUID (server
    // resolves all three); Raphael sends the GUID so the reply's unit= matches. Replies are [BEELZ:*] (silent).
    public static void RequestTformKit(string unit)      => Send($".beelz api tform-kit {unit}");
    public static void RequestTformBinds(string unit)    => Send($".beelz api tform-binds {unit}");
    // Beelz v0.100 / api22: structured admin read of a broadcast message pool (replaces parsing the
    // human-text `admin broadcast-msg <pool> list` reply).
    public static void RequestBroadcastMsgs(string pool) => Send($".beelz api broadcast-msgs {pool}");
    // Beelz v0.135 / api32: incompatibility lock groups ([BEELZ:lock] … [BEELZ:end] cmd=locks). Not adminOnly.
    public static void RequestLocks() => Send(".beelz api locks");

    /// <summary>Initial state snapshot after a successful handshake (handoff §0.5.2 Hydrate).</summary>
    public static void HydrateCore()
    {
        RequestList();
        RequestSlots();
        RequestTransforms();
        RequestActive();
        RequestProgress();
        RequestHotkeys();
        RequestBestiary();
    }

    // ---- player mutations (visible replies) ----
    public static void Cast(string hotkeyNameOrIndex) => SendUser($".beelz cast {hotkeyNameOrIndex}");
    public static void Grant(int slot, int index)     => SendUser($".beelz grant {SlotToken(slot)} {Num(index)}");
    public static void Unslot(int slot)               => SendUser($".beelz unslot {SlotToken(slot)}");
    public static void WeaponGrant(string weapon, int slot, int index) => SendUser($".beelz weapon-grant {weapon} {SlotToken(slot)} {Num(index)}");
    public static void WeaponUnslot(string weapon, int slot)           => SendUser($".beelz weapon-unslot {weapon} {SlotToken(slot)}");
    // Beelz v0.59+ per-form loadouts; v0.78 form-grant also accepts an ability ID. form = a vanilla
    // wheel form (see Forms[]) or "auto" = the form the player is currently in.
    public static void FormGrant(string form, int slot, int index) => SendUser($".beelz form-grant {form} {SlotToken(slot)} {Num(index)}");
    public static void FormUnslot(string form, int slot)           => SendUser($".beelz form-unslot {form} {SlotToken(slot)}");

    // Server-side named loadout presets (human-text replies).
    public static void PresetSave(string name)   => SendUser($".beelz preset save {name}");
    public static void PresetLoad(string name)   => SendUser($".beelz preset load {name}");
    public static void PresetDelete(string name) => SendUser($".beelz preset delete {name}");
    public static void PresetList()              => SendUser(".beelz preset list");

    // Beelz v0.83 player extras (human-text replies the player reads in chat).
    public static void Top()            => SendUser(".beelz top");
    public static void Odds()           => SendUser(".beelz odds");
    public static void Silent(bool on)  => SendUser($".beelz silent {(on ? "on" : "off")}");
    // Beelz v0.94: per-bucket clear-bar (NO confirmation). This is what a "clear bar" button must
    // call — bare `.beelz resetbar` NO-OPS since Beelz v0.76 added a required CONFIRM token. bucket
    // = null/"all" = every set; "universal" = the any-weapon set; a weapon family OR a form name =
    // that set. Keeps captures/unlocks; emits a slot-cleared event so the loadout view refreshes.
    // Beelz v0.137.4 (api35): clearbar re-resolves the whole live bar (may dismount / end a transform) even when
    // nothing was bound, and then no slot-cleared event fires — so re-read `api slots` once ourselves.
    public static void ClearBar(string bucket = null)
    {
        SendUser(string.IsNullOrEmpty(bucket) ? ".beelz clearbar" : $".beelz clearbar {bucket}");
        if (BeelzState.SupportsLayeredClearBar) RequestSlots();
    }

    // The nuke-ALL variant now requires the literal CONFIRM token (Beelz v0.76); bare `resetbar`
    // does nothing. Prefer ClearBar for a normal clear button; keep this for an explicit
    // "reset every bind to vanilla" affordance.
    public static void ResetBar()                     => SendUser(".beelz resetbar CONFIRM");
    public static void RefreshBar()                   => SendUser(".beelz refresh");
    // Silent variant for the auto-refresh-after-grant nudge (no human-text reply cluttering chat).
    public static void RefreshBarSilent()             => Send(".beelz refresh");
    public static void Transform(string indexOrName)  => SendUser($".beelz transform {indexOrName}");
    public static void Revert()                       => SendUser(".beelz revert");

    // 0.22: switching DIRECTLY from one transformation into another corrupts the player's action bar
    // server-side (and a relog doesn't fix it). Mirror the familiar "revert-then-bind" guard: if the
    // player is already transformed, queue a Revert() BEFORE the new Transform() so the server returns
    // them to vampire form first. Both go through the per-frame message queue, so they're sent in order
    // on consecutive frames — exactly like Familiar unbind-then-bind. Safe no-op when not transformed.
    // This is a client-side mitigation; the authoritative fix lives server-side in Beelzebub.
    public static void TransformSafe(string indexOrName)
    {
        var active = BeelzState.Active;
        if (active != null && !active.None) Revert();
        Transform(indexOrName);
    }
    public static void Phase(int n)                   => SendUser($".beelz phase {Num(n)}");

    // 0.73.0: phase switching through ONE shared in-flight lock, used by both the Transforms tab's Phase
    // button (TryCyclePhase) and the transform overlay's Phase 1 / Phase 2 buttons (TryPhase). Beelzebub has
    // no atomic "advance" command — bare `.beelz phase` only SHOWS the phases — so a cycle has to send an
    // explicit `.beelz phase <n>` computed from the last observed phase. A second click before that switch
    // lands would compute the same <n> again (or race the reply), so clicks are ignored while a switch is in
    // flight. The lock is cleared when the observed phase changes (the server's transform-phase-shift event
    // re-pulls `api active` → BeelzState.Active), when the transform ends / changes unit, or after 3 s.
    private const float PHASE_LOCK_SECONDS = 3f;
    private static bool   _phaseLock;
    private static float  _phaseLockAt;
    private static string _phaseLockUnit;
    private static int    _phaseLockFrom;

    /// <summary>True while a phase switch sent by Raphael hasn't been observed yet (max 3 s).</summary>
    public static bool PhaseSwitchInFlight
    {
        get
        {
            if (!_phaseLock) return false;
            var a = BeelzState.Active;
            if (a == null || a.None || a.UnitGuid != _phaseLockUnit || a.Phase != _phaseLockFrom
                || UnityEngine.Time.realtimeSinceStartup - _phaseLockAt >= PHASE_LOCK_SECONDS)
            {
                _phaseLock = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Switch the active transform to phase <paramref name="n"/> unless a switch is already in flight.
    /// Returns false (nothing sent) while locked or when not transformed. Asking for the phase you're
    /// already in is sent (the server replies "Already in phase n") but doesn't arm the lock, since the
    /// observed phase would never change.
    /// </summary>
    public static bool TryPhase(int n)
    {
        if (PhaseSwitchInFlight) return false;
        var a = BeelzState.Active;
        if (a == null || a.None) return false;
        if (a.Phase != n)
        {
            _phaseLock = true;
            _phaseLockAt = UnityEngine.Time.realtimeSinceStartup;
            _phaseLockUnit = a.UnitGuid;
            _phaseLockFrom = a.Phase;
        }
        Phase(n);
        return true;
    }

    /// <summary>
    /// Cycle the active transform to its next phase (wrapping back to the first). Returns false (nothing
    /// sent) while a switch is in flight or when not transformed; <paramref name="next"/> is the phase sent.
    /// </summary>
    public static bool TryCyclePhase(out int next)
    {
        next = 0;
        var a = BeelzState.Active;
        if (a == null || a.None || PhaseSwitchInFlight) return false;
        next = NextPhase(a);
        return TryPhase(next);
    }

    // The phase after the active one. `api active` carries phases=<csv of available phase numbers> (Beelzebub
    // ApiCommands.Active, e.g. "1,2" or "1,2,3"): take the smallest listed phase above the current one, else
    // wrap to the smallest. Old/odd replies with no list fall back to the tform-binds phase count (min 2).
    private static int NextPhase(BeelzActive a)
    {
        int cur = a.Phase;
        int lowest = int.MaxValue, above = int.MaxValue;
        if (!string.IsNullOrEmpty(a.Phases))
        {
            foreach (var part in a.Phases.Split(','))
            {
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) || p <= 0) continue;
                if (p < lowest) lowest = p;
                if (p > cur && p < above) above = p;
            }
        }
        if (above != int.MaxValue) return above;
        if (lowest != int.MaxValue) return lowest;
        int count = System.Math.Max(2, BeelzState.TformPhases(a.UnitGuid));
        return cur >= count || cur < 1 ? 1 : cur + 1;
    }

    public static void Summon()                     => SendUser(".beelz summon");
    public static void Detonate()                     => SendUser(".beelz detonate");
    public static void Forget(int index)              => SendUser($".beelz forget {Num(index)}");
    public static void ForgetTransform(int index)     => SendUser($".beelz forget-transform {Num(index)}");
    public static void ClearAll()                     => SendUser(".beelz clear CONFIRM");
    public static void HotkeySet(string name, int index) => SendUser($".beelz hotkey set {name} {Num(index)}");
    public static void HotkeyClear(string name)          => SendUser($".beelz hotkey clear {name}");

    // Beelz v0.117: forget + hotkey-set now resolve their numeric argument as the ability's STABLE
    // PrefabGUID when it's outside the `.beelz list` index range (server ResolveCapturedSelector), so
    // Raphael can pass the catalog/capture `a=` GUID instead of the index that shifts as you capture. The
    // GUID is itself an int (PrefabGUID), so these send it as the numeric argument.
    // NOTE (audit, api27): `.beelz cast` does NOT take a GUID — it resolves only a hotkey NAME or a
    // `.beelz list` index (Commands/BeelzCommands.Cast). So there is deliberately no CastGuid; cast stays
    // name/index-based via Cast(string). (The Beelz→Raphael handoff doc's "cast accepts a GUID" is inaccurate
    // vs the command source, which is the contract of record.)
    public static void ForgetGuid(string abilityGuid)               => SendUser($".beelz forget {abilityGuid}");
    public static void HotkeySetGuid(string name, string abilityGuid) => SendUser($".beelz hotkey set {name} {abilityGuid}");

    // Beelz v0.116/api27: bulk-set MANY ability config fields in ONE command, instead of N separate
    // `.beelz admin ability <id> <field> <value>` round-trips. Each pair is grouped as (field=value)
    // (the server splits on parens/';'), and the whole spec is quoted so values with spaces/commas
    // (notes, weapon lists) survive. <idOrName> is the ability NAME or its GUID. No-op on an empty set.
    public static void AdminAbilitySet(string idOrName, IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in pairs)
            if (!string.IsNullOrEmpty(kv.Key)) sb.Append('(').Append(kv.Key).Append('=').Append(kv.Value).Append(')');
        if (sb.Length == 0) return;
        SendUser($".beelz admin ability-set {idOrName} \"{sb}\"");
    }

    // Re-load ability_rules.json + re-apply all tuning live, no server restart (Beelz: `admin reload`). In-game
    // `admin ability` edits already apply live, but Raphael sends this after a config save as a belt-and-suspenders
    // so a lowered/cleared value (or a server that didn't apply on write) takes effect without a restart.
    public static void AdminReload() => SendUser(".beelz admin reload");

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    // Slot arg is a TOKEN since Beelz v0.91/0.94: 0 = primary (left-click attack, engine slot 0),
    // 7 = ultimate (the T key, engine slot 7), 1-6 = the spell slots. The grant family accepts the
    // word for 0/7 and the number for 1-6.
    private static string SlotToken(int slot) => slot switch
    {
        0 => "primary",
        7 => "ultimate",
        _ => Num(slot),
    };
}
