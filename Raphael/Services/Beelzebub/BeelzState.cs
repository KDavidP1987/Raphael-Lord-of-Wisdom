using System;
using System.Collections.Generic;
using Raphael.Utils;

namespace Raphael.Services.Beelzebub;

// ---- Typed records the UI binds to (one per api read shape) ----

// api list / [BEELZ:list]  +  the per-ability rows the bestiary cross-references.
// Label / UnitLabel are the server's friendly names (api list label=/ulabel=, Beelz v0.58/ApiVersion 10);
// empty when the server didn't supply them.
internal sealed record BeelzCapture(
    int Index, char Source, string UnitGuid, string UnitName,
    string AbilityGuid, string AbilityName, string Category, string UnitType,
    string Label = "", string UnitLabel = "")
{
    // Prefer the server's friendly ability name over Raphael's prefab humanizer when present.
    public string DisplayAbility => string.IsNullOrEmpty(Label) ? BeelzNames.Ability(AbilityName) : Label;
}

// api slots / [BEELZ:slot] (bucket = "any" or a weapon family). Label (Beelz v0.119/api28) = the server's
// CURATED friendly name for the slotted ability (better than humanizing the raw prefab name); "" on older
// servers. Prefer Label when present — it's what fixes "No Name"-style names for boss/NPC abilities.
internal sealed record BeelzSlot(string Bucket, int Slot, string AbilityGuid, string AbilityName, string Label = "");

// api slots / [BEELZ:form-slot] — a per-form loadout bind (Beelz v0.59+). A distinct line type so
// older parsers that read bucket= as a WeaponFamily ignore it. Form = a vanilla wheel form name.
internal sealed record BeelzFormSlot(string Form, int Slot, string AbilityGuid, string AbilityName, string Label = "");

// api transforms / [BEELZ:tx] (Dracula/Morgana only in v6 — 0..2 entries)
internal sealed record BeelzTransform(
    int Index, char Source, string UnitGuid, string UnitName, bool Enabled,
    string Difficulty, int Tier, float DamageScale, float CooldownScale,
    float HealthScale, float SpeedScale, string Type, bool FullReplace,
    string ScalingMode, bool Shard);

// api transform-config / [BEELZ:tx-config] — per-category transform mode/duration/cooldown.
// Src: 'R' = regular, 'V' = V-Blood, 'S' = shard boss. Live cooldown remaining is in api cooldowns.
internal sealed record BeelzTxConfig(char Src, string Mode, float Duration, float Cooldown);

// api active / [BEELZ:active]
internal sealed record BeelzActive(
    bool None, string UnitGuid, string UnitName, char Source,
    string Ttl, int Phase, string Phases);

// api bestiary / [BEELZ:bestiary] (per-unit collection progress)
internal sealed record BeelzBestiaryEntry(
    string UnitGuid, string UnitName, char Source, int Captured, int Total, bool Transform);

// api hotkeys / [BEELZ:hotkey] (named extra-ability bindings -> action-bar buttons)
internal sealed record BeelzHotkey(string Name, string AbilityGuid, string AbilityName);

// api config / [BEELZ:config] (generic admin settings panel)
internal sealed record BeelzConfigEntry(string Section, string Key, string Value, string Type, bool Editable);

// Per-ability shaping overrides — the admin-SET, server-wide tuning values (Beelz v0.65–v0.87),
// present on api info / info-guid / catalog-ability. Numeric overrides are null when unset (wire
// "-"); the tri-state strings are "on"/"off"/"auto" (or "" when absent). Backs the Phase-8 admin
// ability-config panel; tooltips can surface the few that affect play (cooldown/range/charges).
internal sealed record BeelzShaping(
    float? Cooldown, float? Range, int? Charges, float? ChargeTime, float? Aoe,
    float? ProjSpeed, float? Duration, float? HealMult, float? ForceTimeout,
    int? SummonCap, float? SummonTimeout, int? SummonUnits, float? FreeMoveSecs,
    string InterruptOnHit, string Interruptible, string FreeMove, string CastSpeed,
    // Beelz v0.134/api31 tuning overrides — emitted on api info / info-guid only (NOT catalog-ability),
    // so catalog rows always hold null here.
    int? MaxStacks = null, int? ProjCount = null, float? Knockback = null, float? Lifetime = null,
    float? CastTime = null)
{
    public static readonly BeelzShaping None = new(
        null, null, null, null, null, null, null, null, null,
        null, null, null, null, "", "", "", "");

    /// <summary>True when at least one override is set away from baseline — for an "edited" badge.</summary>
    public bool HasAny =>
        Cooldown.HasValue || Range.HasValue || Charges.HasValue || ChargeTime.HasValue || Aoe.HasValue ||
        ProjSpeed.HasValue || Duration.HasValue || HealMult.HasValue || ForceTimeout.HasValue ||
        SummonCap.HasValue || SummonTimeout.HasValue || SummonUnits.HasValue || FreeMoveSecs.HasValue ||
        MaxStacks.HasValue || ProjCount.HasValue || Knockback.HasValue || Lifetime.HasValue || CastTime.HasValue ||
        IsSet(InterruptOnHit) || IsSet(Interruptible) || IsSet(FreeMove) || IsSet(CastSpeed);

    private static bool IsSet(string s)
        => !string.IsNullOrEmpty(s) && !s.Equals("auto", StringComparison.OrdinalIgnoreCase);
}

// api info / [BEELZ:info] (tooltip data + the cooldown the overlay ring needs)
internal sealed record BeelzAbilityInfo(
    int Index, char Source, string UnitGuid, string UnitName, string AbilityGuid,
    string AbilityName, string Label, string Desc, IReadOnlyList<string> Weapons,
    string WeaponAnim, string School, float CooldownSeconds, IReadOnlyList<string> Forms,
    bool TransformOnly, bool Enabled, string Difficulty, float DamageScale, float CooldownScale,
    string Category, string CategoryOverride, float CastTimeSeconds, float Range, string Behavior,
    int Phase, bool AllowDenied, BeelzShaping Shaping,
    // Beelz v0.107/api24 activation-condition · v0.112/api25 curation · v0.113/api26 source-tier — the
    // SAME informational tokens `catalog-ability` carries (api info/info-guid emit them too). Mirrored
    // here so a tooltip opened via api info shows "Use:"/tier/VBlood without a full catalog scan.
    string Condition = "", IReadOnlyList<string> ConditionMods = null, string ConditionSource = "",
    string ReviewStatus = "", string ReviewTag = "",
    int? SourceLevel = null, string SourceTier = "", bool IsVBlood = false,
    // Beelz v0.132/api29: form/weapon BLOCK lists (the "!" blacklists). On api29+ Forms holds ONLY the
    // allowed forms; before that it could hold inverted blocks, so don't badge from it on older servers.
    IReadOnlyList<string> FormBlocks = null, IReadOnlyList<string> WeaponBlocks = null,
    // Beelz v0.133/api30 per-hit damage: effective server mode, the factor a cast would snapshot now,
    // per-hit attribution health (reliable|unreliable|unknown), per-ability summon power.
    string DamageMode = "", float? DamageScaleEff = null, string Attribution = "", float? SummonPower = null,
    // Beelz v0.134/api31: "charges" = the ability uses charges, so cooldown rules are skipped for it.
    string CooldownMode = "")
{
    public bool UsesCharges => CooldownMode.Equals("charges", StringComparison.OrdinalIgnoreCase);

    // api31+ (Beelz v0.134) runtime precedence: cooldown_override if set, else the SHIPPED cooldown_seconds
    // × cooldown_scale, then the server floor Grant_MinimumCooldownSeconds (when api config has been read).
    // Older servers: cooldown_seconds × cooldown_scale (handoff §8). Floor 1s either way for the ring.
    public float EffectiveCooldownSeconds
    {
        get
        {
            float scale = CooldownScale <= 0f ? 1f : CooldownScale;
            if (!BeelzState.SupportsRuntimeCooldowns) return Math.Max(1f, CooldownSeconds * scale);
            float cd = Shaping?.Cooldown ?? CooldownSeconds * scale;
            float floor = BeelzState.ConfigFloat("Grant_MinimumCooldownSeconds");
            if (floor > 0f && cd < floor) cd = floor;
            return Math.Max(1f, cd);
        }
    }
}

// api locks / [BEELZ:lock] (Beelz v0.135/api32) — one admin-defined incompatibility group: at most Max of
// its Members may be on one player's live bar + hotkeys at once. A member is an ability GUID or "cat:<Category>".
internal sealed record BeelzLockGroup(string Name, int Max, IReadOnlyList<string> Members)
{
    public bool Covers(string abilityGuid, string category)
    {
        foreach (var m in Members)
        {
            if (m.StartsWith("cat:", StringComparison.OrdinalIgnoreCase))
            { if (!string.IsNullOrEmpty(category) && m.Substring(4).Equals(category, StringComparison.OrdinalIgnoreCase)) return true; }
            else if (!string.IsNullOrEmpty(abilityGuid) && m == abilityGuid) return true;
        }
        return false;
    }
}

// api progress / [BEELZ:progress]
internal sealed record BeelzProgress(
    int AbilitiesCaptured, int AbilitiesTotal, float AbilitiesPct,
    int TransformsUnlocked, int TransformsTotal, float TransformsPct);

// api catalog abilities / [BEELZ:catalog-ability] — the FULL ability matrix (every
// capturable ability in the game), keyed by ability NAME (the catalog carries no guid).
// One paginated scan replaces hundreds of per-ability `api info` calls: it already has
// category / weapons / forms / transform-only / enabled, which is everything the
// collector checklist, the loadout metadata columns, and the Magic/Weapon/Form filter
// need. (school/desc are NOT in the catalog — Kind is derived from weapons/forms.)
internal sealed record BeelzCatalogAbility(
    string Name, string Category, IReadOnlyList<string> Weapons, IReadOnlyList<string> Forms,
    bool TransformOnly, bool Enabled, string Difficulty, float DamageScale, float CooldownScale,
    bool Curated, string School, string Desc, int Phase, bool AllowDenied,
    string CategoryOverride, BeelzShaping Shaping, string Notes,
    // Coordinated wire addition: per-ability identity + source unit on the `catalog-ability` scan line,
    // so the FULL ability list (captured OR not) can show its GUID + owning unit in one scan. All
    // optional — empty until Beelzebub emits a=/unit=/unitguid=. AbilityGuid powers the ID column +
    // search-by-ID; Unit/UnitGuid fill the Unit column and the Bestiary's unit grouping of uncaptured.
    string AbilityGuid = "", string Unit = "", string UnitGuid = "",
    // Beelz v0.107/api24 activation-condition · v0.112/api25 curation · v0.113/api26 source-tier.
    // ALL INFORMATIONAL — none disables an ability (enabled= stays the kill-switch). Sentinels fold to
    // ""/empty/null/false, so on older servers (no emit) these read as "unknown". Condition is HOW the
    // ability is used; ReviewStatus/ReviewTag are our curation state; Source* describe the origin unit.
    string Condition = "", IReadOnlyList<string> ConditionMods = null, string ConditionSource = "",
    string ReviewStatus = "", string ReviewTag = "",
    int? SourceLevel = null, string SourceTier = "", bool IsVBlood = false)
{
    /// <summary>api25: true when the server's review gate would curate this row out of the player set
    /// (admin `abilities-all` still lists it). Use this — not Enabled — to detect curation-gated rows.</summary>
    public bool IsCurationBlocked =>
        ReviewStatus.Equals("Blocked", StringComparison.OrdinalIgnoreCase) ||
        ReviewStatus.Equals("Hidden", StringComparison.OrdinalIgnoreCase);
}

// Beelz v0.100 / api22 structured transform-loadout + broadcast reads (replace human-text parsing).
// api tform-kit / [BEELZ:tform-ability] — one transform's full eligible kit. AbilityName is the RAW
// prefab name (humanize client-side, like the slot lines).
internal sealed record BeelzTformAbility(int Index, string AbilityGuid, string AbilityName);
// api tform-binds / [BEELZ:tform-slot] — the player's current CUSTOM binds for a transform (per phase/slot).
internal sealed record BeelzTformBind(int Phase, int Slot, string AbilityGuid, string AbilityName);
// api broadcast-msgs / [BEELZ:broadcast-msg] — a broadcast pool's messages (idx is 1-based, matching
// admin broadcast-msg edit/remove <n>).
internal sealed record BeelzBroadcastMsg(int Index, string Text);

// Cached client model for the Beelzebub integration. Mirrors PlayerStateService:
// static read-only data + a change event per slice; the protocol service updates it,
// the UI binds to it and never parses raw wire lines.
internal static class BeelzState
{
    // ---- handshake / presence ----
    public static bool   Present { get; private set; }   // server has Beelzebub and ACK'd ready=1
    public static int    ApiVersion { get; private set; }
    public static string PluginVersion { get; private set; } = "";
    public static bool   Ready { get; private set; }
    public static bool   Subscribed { get; private set; } // api bch on ACK'd (event stream live)

    // ---- collection / loadout ----
    public static IReadOnlyList<BeelzCapture>       Captures   { get; private set; } = Array.Empty<BeelzCapture>();
    public static IReadOnlyList<BeelzSlot>          Slots      { get; private set; } = Array.Empty<BeelzSlot>();
    public static string                            CurrentWeapon { get; private set; } = "";
    public static IReadOnlyList<BeelzFormSlot>      FormSlots  { get; private set; } = Array.Empty<BeelzFormSlot>();
    public static string                            CurrentForm { get; private set; } = "";   // active shapeshift form ("" / "None" when not in one)
    public static IReadOnlyList<BeelzTransform>     Transforms { get; private set; } = Array.Empty<BeelzTransform>();
    public static BeelzActive                       Active     { get; private set; }
    public static IReadOnlyList<BeelzBestiaryEntry> Bestiary   { get; private set; } = Array.Empty<BeelzBestiaryEntry>();
    public static IReadOnlyList<BeelzHotkey>        Hotkeys    { get; private set; } = Array.Empty<BeelzHotkey>();
    public static bool                              HotkeysEnabled { get; private set; }
    public static int                               HotkeysMax { get; private set; }
    public static IReadOnlyList<BeelzConfigEntry>   Config     { get; private set; } = Array.Empty<BeelzConfigEntry>();
    public static BeelzProgress                     Progress   { get; private set; }
    public static IReadOnlyDictionary<string, float> Cooldowns { get; private set; } = new Dictionary<string, float>();
    public static IReadOnlyList<BeelzTxConfig>      TxConfigs  { get; private set; } = Array.Empty<BeelzTxConfig>();

    // api info results, keyed by ability guid (string). Used for tooltips + cooldown rings.
    private static readonly Dictionary<string, BeelzAbilityInfo> _abilityInfo = new();
    public static bool TryGetAbilityInfo(string abilityGuid, out BeelzAbilityInfo info) => _abilityInfo.TryGetValue(abilityGuid, out info);

    // ---- catalog (full ability matrix, populated by the user-triggered Scan all) ----
    // Keyed by lowercased ability NAME (catalog carries no guid); cross-ref captures by name.
    private static readonly Dictionary<string, BeelzCatalogAbility> _catalog =
        new(StringComparer.OrdinalIgnoreCase);
    public static bool CatalogLoaded { get; private set; }
    /// <summary>True when the catalog holds the FULL collectible set (a complete unfiltered scan or a cache
    /// warm). False if it currently holds only a filtered subset (a preset/filtered scan with no full
    /// backing) — the % denominator + "Scan all" prompts should caveat that. Set true only by SetCatalog;
    /// MergeCatalog (filtered refresh) preserves it, so a slice refresh of a complete set stays complete.</summary>
    public static bool CatalogComplete { get; private set; }
    /// <summary>Non-empty when the catalog was warmed from the disk cache (e.g. "v0.20.0") — for a "cached —
    /// Re-scan to refresh" hint. Cleared the moment a live scan commits.</summary>
    public static string CatalogCacheInfo { get; private set; } = "";
    public static IReadOnlyDictionary<string, BeelzCatalogAbility> CatalogAbilities => _catalog;
    public static int CatalogEnabledCount { get; private set; }
    public static bool TryGetCatalog(string abilityName, out BeelzCatalogAbility c)
        => _catalog.TryGetValue(abilityName ?? "", out c);

    // ---- admin catalog (Beelz v0.100 `api catalog abilities-all`): EVERY ability group regardless
    // of enable/deny/difficulty, for the Admin: Abilities config table. Separate from the player
    // collectible catalog above (the Bestiary's). Same record type; each carries Enabled. ----
    private static readonly Dictionary<string, BeelzCatalogAbility> _catalogAll =
        new(StringComparer.OrdinalIgnoreCase);
    public static bool CatalogAllLoaded { get; private set; }
    /// <summary>Admin-scope analogue of CatalogComplete.</summary>
    public static bool CatalogAllComplete { get; private set; }
    public static string CatalogAllCacheInfo { get; private set; } = "";
    public static IReadOnlyDictionary<string, BeelzCatalogAbility> CatalogAllAbilities => _catalogAll;
    public static int CatalogAllEnabledCount { get; private set; }
    public static bool TryGetCatalogAll(string abilityName, out BeelzCatalogAbility c)
        => _catalogAll.TryGetValue(abilityName ?? "", out c);

    // ---- structured transform-loadout reads (Beelz v0.100 / api22), keyed by unit GUID string ----
    private static readonly Dictionary<string, IReadOnlyList<BeelzTformAbility>> _tformKit = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IReadOnlyList<BeelzTformBind>> _tformBinds = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> _tformPhases = new(StringComparer.Ordinal); // unit guid -> phase count
    public static bool TryGetTformKit(string unitGuid, out IReadOnlyList<BeelzTformAbility> kit)
        => _tformKit.TryGetValue(unitGuid ?? "", out kit);
    public static bool TryGetTformBinds(string unitGuid, out IReadOnlyList<BeelzTformBind> binds)
        => _tformBinds.TryGetValue(unitGuid ?? "", out binds);
    public static int TformPhases(string unitGuid)
        => (!string.IsNullOrEmpty(unitGuid) && _tformPhases.TryGetValue(unitGuid, out var n) && n > 0) ? n : 0;

    // ---- structured broadcast-pool read (Beelz v0.100 / api22), keyed by pool name ----
    private static readonly Dictionary<string, IReadOnlyList<BeelzBroadcastMsg>> _broadcastMsgs = new(StringComparer.OrdinalIgnoreCase);
    public static bool TryGetBroadcastMsgs(string pool, out IReadOnlyList<BeelzBroadcastMsg> msgs)
        => _broadcastMsgs.TryGetValue(pool ?? "", out msgs);

    /// <summary>True once the handshake reports a Beelzebub new enough for the structured transform-loadout
    /// + broadcast-pool reads (ApiVersion 22+, Beelz v0.100). Older servers use Raphael's human-text fallback.</summary>
    public static bool SupportsStructuredReads => ApiVersion >= 22;

    // Granular capability gates for the v0.101→v0.118 (api23→27) additive token/filter set. Each UI
    // feature lights up only when the connected server actually emits the data — older servers degrade
    // gracefully (the tokens are absent → the records hold the "unknown" sentinels). See the
    // Beelz→Raphael integration handoff §1 for the ApiVersion timeline.
    /// <summary>api24 (Beelz v0.107): condition / condition_mods / condition_source tokens present.</summary>
    public static bool SupportsConditionMeta  => ApiVersion >= 24;
    /// <summary>api25 (Beelz v0.112): review_status / review_tag curation tokens present.</summary>
    public static bool SupportsReviewMeta     => ApiVersion >= 25;
    /// <summary>api26 (Beelz v0.113): source_level / source_tier / is_vblood tokens present.</summary>
    public static bool SupportsSourceTier     => ApiVersion >= 26;
    /// <summary>api27 (Beelz v0.116): full catalog load-filter set (weapon/cat/unit/form/search PLUS
    /// tag/reviewstatus/tier/vblood). Lets a scan pull one group server-side instead of ~1,700 rows.</summary>
    public static bool SupportsCatalogFilters => ApiVersion >= 27;
    /// <summary>api27 (Beelz v0.116): `.beelz admin ability-set <id> "(f=v)…"` bulk multi-field set in one
    /// command. When false, fall back to one `.beelz admin ability <id> <f> <v>` per changed field.</summary>
    public static bool SupportsBulkAbilitySet => ApiVersion >= 27;
    /// <summary>api28 (Beelz v0.119): `api slots` / `[BEELZ:form-slot]` carry `label=` (the curated friendly
    /// ability name per slot). When false, Raphael humanizes the raw prefab name instead.</summary>
    public static bool SupportsSlotLabels => ApiVersion >= 28;
    /// <summary>Beelz v0.120 (ApiVersion stayed 28, so this gates on PLUGIN version, not api): `.beelz resetbar`
    /// (and admin `rebuildslots`) now authoritatively clear the engine's cached slot values AND re-apply the
    /// player's saved grants — the real cure for a bar stuck on a creature kit (a plain `.beelz refresh`
    /// can't clear a deeply-cached slot). When false, Raphael's "Unstick bar" uses the gentler revert→refresh.</summary>
    public static bool SupportsAuthoritativeBarReset => PluginVersionAtLeast(0, 120, 0);
    /// <summary>api29 (Beelz v0.132): api info `forms=` lists ONLY allowed forms + `form_blocks=`/`weapon_blocks=`.</summary>
    public static bool SupportsFormBlocks => ApiVersion >= 29;
    /// <summary>api30 (Beelz v0.133): per-hit damage tokens (damage_mode/damage_scale_eff/attribution/summon_power).</summary>
    public static bool SupportsDamageMeta => ApiVersion >= 30;
    /// <summary>api31 (Beelz v0.134): runtime cooldown precedence + maxstacks/projcount/knockback/lifetime/casttime
    /// overrides + cooldown_mode on api info.</summary>
    public static bool SupportsRuntimeCooldowns => ApiVersion >= 31;
    /// <summary>api32 (Beelz v0.135): `api locks` + `type=ability-locked` event (incompatibility locks).</summary>
    public static bool SupportsLocks => ApiVersion >= 32;
    /// <summary>Beelz v0.132 admin `ability-inspect` + v0.135 admin `lock`/`reseed` chat commands (no wire).</summary>
    public static bool SupportsAbilityInspect => PluginVersionAtLeast(0, 132, 0);
    public static bool SupportsReseed         => PluginVersionAtLeast(0, 135, 0);

    /// <summary>A numeric `api config` value by key (0 when absent/unparseable — i.e. api config not read yet).</summary>
    public static float ConfigFloat(string key)
    {
        foreach (var e in Config)
            if (e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                && float.TryParse(e.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                return f;
        return 0f;
    }

    // ---- incompatibility locks (Beelz v0.135/api32) ----
    public static IReadOnlyList<BeelzLockGroup> Locks { get; private set; } = Array.Empty<BeelzLockGroup>();
    public static bool LocksLoaded { get; private set; }
    // ability guid -> lock group name, from `ability-locked locked=1` events (the server kept it OFF the bar).
    private static readonly Dictionary<string, string> _lockedNow = new(StringComparer.Ordinal);
    public static bool IsLockedNow(string abilityGuid, out string group)
    {
        group = "";
        return !string.IsNullOrEmpty(abilityGuid) && _lockedNow.TryGetValue(abilityGuid, out group);
    }

    /// <summary>The lock group that would keep this ability off the player's bar: one covering it whose
    /// member count ALREADY on the bar + hotkeys (excluding this ability) has reached max. "" when none.
    /// Live suppression reported by the server (ability-locked events) wins over this estimate.</summary>
    public static string LockConflict(string abilityGuid, string category)
    {
        if (!SupportsLocks) return "";
        if (IsLockedNow(abilityGuid, out var g)) return g;
        if (Locks.Count == 0) return "";
        var onBar = new Dictionary<string, string>(StringComparer.Ordinal);   // guid -> category
        void Add(string guid) { if (!string.IsNullOrEmpty(guid) && guid != "0" && guid != abilityGuid) onBar[guid] = CategoryOf(guid); }
        // Only what's LIVE: the universal bucket + the current weapon's bucket, and the current form's loadout.
        foreach (var s in Slots)
            if (s.Bucket.Equals("any", StringComparison.OrdinalIgnoreCase) || s.Bucket.Equals(CurrentWeapon, StringComparison.OrdinalIgnoreCase))
                Add(s.AbilityGuid);
        foreach (var s in FormSlots)
            if (!string.IsNullOrEmpty(CurrentForm) && s.Form.Equals(CurrentForm, StringComparison.OrdinalIgnoreCase))
                Add(s.AbilityGuid);
        foreach (var h in Hotkeys) Add(h.AbilityGuid);
        foreach (var grp in Locks)
        {
            if (grp.Max <= 0 || !grp.Covers(abilityGuid, category)) continue;
            int n = 0;
            foreach (var kv in onBar) if (grp.Covers(kv.Key, kv.Value)) n++;
            if (n >= grp.Max) return grp.Name;
        }
        return "";
    }

    /// <summary>Best-known category (`cat=`) for an ability guid — api info, else the capture list.</summary>
    public static string CategoryOf(string abilityGuid)
    {
        if (string.IsNullOrEmpty(abilityGuid)) return "";
        if (_abilityInfo.TryGetValue(abilityGuid, out var i) && !string.IsNullOrEmpty(i.Category)) return i.Category;
        foreach (var c in Captures) if (c.AbilityGuid == abilityGuid) return c.Category ?? "";
        return "";
    }

    /// <summary>Parse the dotted PluginVersion ("0.120.0") and test it is >= major.minor.patch. Lenient:
    /// returns false on an empty / unparseable version so callers degrade to the older code path.</summary>
    internal static bool PluginVersionAtLeast(int major, int minor, int patch)
    {
        if (string.IsNullOrEmpty(PluginVersion)) return false;
        if (!System.Version.TryParse(PluginVersion, out var v)) return false;
        int vMinor = v.Minor < 0 ? 0 : v.Minor, vPatch = v.Build < 0 ? 0 : v.Build;
        if (v.Major != major) return v.Major > major;
        if (vMinor  != minor) return vMinor  > minor;
        return vPatch >= patch;
    }

    /// <summary>Magic / Weapon / Form classification from catalog metadata. "" if unknown
    /// (not scanned yet). Weapon = bound to a weapon family; Form = form-restricted; else Magic.</summary>
    public static string AbilityKind(string abilityName)
    {
        if (!TryGetCatalog(abilityName, out var c)) return "";
        // Beelz v0.101/api23: forms/weapons tokens can be an ALLOW-list (plain name = usable ONLY there) or a
        // `!`-prefixed BLOCK-list (usable everywhere EXCEPT there). Only an allow-token is a genuine binding —
        // a list of pure `!`-blacklist tokens means the ability is broadly usable, so it classifies as Magic.
        if (HasAllowToken(c.Weapons)) return "Weapon";
        if (HasAllowToken(c.Forms))   return "Form";
        return "Magic";
    }

    /// <summary>True if a forms/weapons restriction list contains at least one ALLOW (non-`!`) token — i.e.
    /// the ability is genuinely restricted TO those, not merely blocked FROM some (`!`-prefixed).</summary>
    public static bool HasAllowToken(System.Collections.Generic.IReadOnlyList<string> tokens)
    {
        if (tokens == null) return false;
        for (int i = 0; i < tokens.Count; i++)
            if (!string.IsNullOrEmpty(tokens[i]) && tokens[i][0] != '!') return true;
        return false;
    }

    // unit guid -> the resolved (friendlier) unit name the bestiary reports. api list
    // emits raw prefab unit names, but api bestiary resolves them, so we reuse that.
    private static readonly Dictionary<string, string> _unitNameByGuid = new();
    /// <summary>Resolved unit name from the bestiary if known, else the supplied raw value.</summary>
    public static string ResolvedUnit(string unitGuid, string rawFallback)
        => (!string.IsNullOrEmpty(unitGuid) && _unitNameByGuid.TryGetValue(unitGuid, out var n) && !string.IsNullOrEmpty(n))
            ? n : rawFallback;

    // ---- change events ----
    public static event Action PresenceChanged;   // Present / Ready / ApiVersion / Subscribed
    public static event Action CapturesChanged;
    public static event Action SlotsChanged;
    public static event Action TransformsChanged;
    public static event Action ActiveChanged;
    public static event Action BestiaryChanged;
    public static event Action HotkeysChanged;
    public static event Action ConfigChanged;
    public static event Action ProgressChanged;
    public static event Action CooldownsChanged;
    public static event Action TxConfigChanged;
    public static event Action AbilityInfoChanged;
    public static event Action CatalogChanged;
    public static event Action CatalogAllChanged;
    public static event Action TformKitChanged;       // a tform-kit read committed
    public static event Action TformBindsChanged;     // a tform-binds read committed
    public static event Action BroadcastMsgsChanged;  // a broadcast-msgs read committed
    public static event Action LocksChanged;          // api locks committed, or an ability-locked event (api32)

    // ---- mutators (called only by BeelzProtocolService) ----
    internal static void SetVersion(int api, string plugin, bool ready)
    {
        ApiVersion = api; PluginVersion = plugin; Ready = ready;
        if (ready) Present = true;
        Fire(PresenceChanged);
    }
    internal static void SetSubscribed(bool on) { Subscribed = on; Fire(PresenceChanged); }

    internal static void SetCaptures(IReadOnlyList<BeelzCapture> v)   { Captures = v;   Fire(CapturesChanged); }
    internal static void SetSlots(IReadOnlyList<BeelzSlot> v, string currentWeapon,
        IReadOnlyList<BeelzFormSlot> formSlots, string currentForm)
    {
        Slots = v; CurrentWeapon = currentWeapon ?? "";
        FormSlots = formSlots ?? Array.Empty<BeelzFormSlot>(); CurrentForm = currentForm ?? "";
        Fire(SlotsChanged);
    }
    internal static void SetTransforms(IReadOnlyList<BeelzTransform> v) { Transforms = v; Fire(TransformsChanged); }
    internal static void SetActive(BeelzActive v)                    { Active = v;     Fire(ActiveChanged); }
    internal static void SetBestiary(IReadOnlyList<BeelzBestiaryEntry> v)
    {
        Bestiary = v;
        _unitNameByGuid.Clear();
        foreach (var e in v)
            if (!string.IsNullOrEmpty(e.UnitGuid) && !string.IsNullOrEmpty(e.UnitName))
                _unitNameByGuid[e.UnitGuid] = e.UnitName;
        Fire(BestiaryChanged);
    }
    internal static void SetHotkeys(IReadOnlyList<BeelzHotkey> v, bool enabled, int max) { Hotkeys = v; HotkeysEnabled = enabled; HotkeysMax = max; Fire(HotkeysChanged); }
    internal static void SetConfig(IReadOnlyList<BeelzConfigEntry> v) { Config = v;     Fire(ConfigChanged); }
    internal static void SetProgress(BeelzProgress v)                { Progress = v;   Fire(ProgressChanged); }
    internal static void SetCooldowns(IReadOnlyDictionary<string, float> v) { Cooldowns = v; Fire(CooldownsChanged); }
    internal static void SetTxConfigs(IReadOnlyList<BeelzTxConfig> v) { TxConfigs = v; Fire(TxConfigChanged); }
    internal static void SetAbilityInfo(BeelzAbilityInfo info)
    {
        if (info == null || string.IsNullOrEmpty(info.AbilityGuid)) return;
        _abilityInfo[info.AbilityGuid] = info;
        Fire(AbilityInfoChanged);
    }

    /// <summary>Commit a completed FULL (unfiltered) catalog scan — REPLACES the matrix and marks it
    /// complete. fromCacheVersion is non-empty only when warming from the disk cache (drives the
    /// "cached — Re-scan" hint); a live scan passes "".</summary>
    internal static void SetCatalog(IReadOnlyList<BeelzCatalogAbility> all, string fromCacheVersion = "")
    {
        _catalog.Clear();
        if (all != null)
            foreach (var c in all)
            {
                if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                _catalog[c.Name] = c;
            }
        CatalogCacheInfo = fromCacheVersion ?? "";
        CatalogComplete = true;            // a full set (scan or cache) is, by definition, complete
        RecomputeCatalogCounts();
        Fire(CatalogChanged);
    }

    /// <summary>Commit a completed ADMIN catalog scan (every ability group; `api catalog abilities-all`).</summary>
    internal static void SetCatalogAll(IReadOnlyList<BeelzCatalogAbility> all, string fromCacheVersion = "")
    {
        _catalogAll.Clear();
        if (all != null)
            foreach (var c in all)
            {
                if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                _catalogAll[c.Name] = c;
            }
        CatalogAllCacheInfo = fromCacheVersion ?? "";
        CatalogAllComplete = true;
        RecomputeCatalogAllCounts();
        Fire(CatalogAllChanged);
    }

    /// <summary>Filtered commit — UPSERTS a subset (refresh just those rows) without dropping the rest.
    /// Preserves CatalogComplete (a slice refresh of a complete set stays complete; a merge into nothing
    /// stays partial). Clears the cache hint — the in-memory set now differs from what's on disk.</summary>
    internal static void MergeCatalog(IReadOnlyList<BeelzCatalogAbility> subset)
    {
        if (subset != null)
            foreach (var c in subset)
            {
                if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                _catalog[c.Name] = c;
            }
        CatalogCacheInfo = "";
        RecomputeCatalogCounts();
        Fire(CatalogChanged);
    }
    internal static void MergeCatalogAll(IReadOnlyList<BeelzCatalogAbility> subset)
    {
        if (subset != null)
            foreach (var c in subset)
            {
                if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                _catalogAll[c.Name] = c;
            }
        CatalogAllCacheInfo = "";
        RecomputeCatalogAllCounts();
        Fire(CatalogAllChanged);
    }

    private static void RecomputeCatalogCounts()
    {
        int enabled = 0;
        foreach (var c in _catalog.Values) if (c.Enabled) enabled++;
        CatalogEnabledCount = enabled;
        CatalogLoaded = _catalog.Count > 0;
    }
    private static void RecomputeCatalogAllCounts()
    {
        int enabled = 0;
        foreach (var c in _catalogAll.Values) if (c.Enabled) enabled++;
        CatalogAllEnabledCount = enabled;
        CatalogAllLoaded = _catalogAll.Count > 0;
    }

    internal static void SetTformKit(string unitGuid, IReadOnlyList<BeelzTformAbility> kit)
    {
        if (string.IsNullOrEmpty(unitGuid)) return;
        _tformKit[unitGuid] = kit ?? Array.Empty<BeelzTformAbility>();
        Fire(TformKitChanged);
    }
    internal static void SetTformBinds(string unitGuid, IReadOnlyList<BeelzTformBind> binds, int phases)
    {
        if (string.IsNullOrEmpty(unitGuid)) return;
        _tformBinds[unitGuid] = binds ?? Array.Empty<BeelzTformBind>();
        if (phases > 0) _tformPhases[unitGuid] = phases;
        Fire(TformBindsChanged);
    }
    internal static void SetBroadcastMsgs(string pool, IReadOnlyList<BeelzBroadcastMsg> msgs)
    {
        if (string.IsNullOrEmpty(pool)) return;
        _broadcastMsgs[pool] = msgs ?? Array.Empty<BeelzBroadcastMsg>();
        Fire(BroadcastMsgsChanged);
    }

    internal static void SetLocks(IReadOnlyList<BeelzLockGroup> groups)
    {
        Locks = groups ?? Array.Empty<BeelzLockGroup>(); LocksLoaded = true;
        Fire(LocksChanged);
    }
    internal static void SetAbilityLocked(string abilityGuid, string group, bool locked)
    {
        if (string.IsNullOrEmpty(abilityGuid)) return;
        if (locked) _lockedNow[abilityGuid] = group ?? ""; else _lockedNow.Remove(abilityGuid);
        Fire(LocksChanged);
    }

    /// <summary>0.18.1: clear ALL cached Beelzebub state. Called on logout (via
    /// BeelzProtocolService.Reset from the ClientBootstrapSystem.OnDestroy teardown hook) so a relog
    /// into a DIFFERENT server starts clean. Without this, presence + catalog + captures + slots
    /// from the previous server leak across a server-switch that doesn't fully restart the game.
    /// PURE field resets — does NOT fire change events (the teardown hook must do no UI work; the
    /// UI re-gates on relog when detection re-runs and fires AvailabilityChanged/PresenceChanged).</summary>
    internal static void Reset()
    {
        Present = false; ApiVersion = 0; PluginVersion = ""; Ready = false; Subscribed = false;
        Captures = Array.Empty<BeelzCapture>();
        Slots = Array.Empty<BeelzSlot>(); CurrentWeapon = "";
        FormSlots = Array.Empty<BeelzFormSlot>(); CurrentForm = "";
        Transforms = Array.Empty<BeelzTransform>();
        Active = null;
        Bestiary = Array.Empty<BeelzBestiaryEntry>();
        Hotkeys = Array.Empty<BeelzHotkey>(); HotkeysEnabled = false; HotkeysMax = 0;
        Config = Array.Empty<BeelzConfigEntry>();
        Progress = null;
        Cooldowns = new Dictionary<string, float>();
        TxConfigs = Array.Empty<BeelzTxConfig>();
        _abilityInfo.Clear();
        _catalog.Clear(); CatalogLoaded = false; CatalogEnabledCount = 0; CatalogComplete = false; CatalogCacheInfo = "";
        _catalogAll.Clear(); CatalogAllLoaded = false; CatalogAllEnabledCount = 0; CatalogAllComplete = false; CatalogAllCacheInfo = "";
        _tformKit.Clear(); _tformBinds.Clear(); _tformPhases.Clear(); _broadcastMsgs.Clear();
        _unitNameByGuid.Clear();
        Locks = Array.Empty<BeelzLockGroup>(); LocksLoaded = false; _lockedNow.Clear();
    }

    private static void Fire(Action evt)
    {
        if (evt == null) return;
        try { evt.Invoke(); }
        catch (Exception ex) { LogUtils.LogError($"BeelzState event handler threw: {ex}"); }
    }
}
