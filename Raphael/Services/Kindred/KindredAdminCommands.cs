using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Raphael.Services.Kindred;

// 0.73.0: every command the three Kindred admin tabs send (PLAN-ADDENDUM §1 "Accuracy gate"). Pure (no Unity), linked
// into tests/Raphael.Logic.Tests, where KindredAdminTests checks each template against the manifest generated from
// KindredCommands 2.5.8 (tests/fixtures/kindredcommands-commands.json): the invocation, the {placeholders} against the
// source parameters (names, order, count), and which placeholders are quoted. MainPanel.KindredAdmin.cs and
// MessageService_Processing.cs reference these constants, so the strings the tests check are the strings sent.
//
// Templates: {name} = a form field of that name; names are the source parameter names. A template may leave out
// trailing optional parameters. Zero-argument commands are plain strings (buttons).
public static class KindredAdminCommands
{
    // ------------------------------------------------------------------ Admin: Players
    public const string ReviveTarget        = ".revivetarget";
    public const string Revive              = ".revive {player}";
    public const string KillPlayer          = ".killplayer {player}";
    public const string StayDown            = ".staydown {player}";
    public const string PlayerHeartCount    = ".playerheartcount {amount} {player}";
    public const string Teleport            = ".teleport {x} {y} {z} {player}";
    public const string RevealMap           = ".revealmap {player}";
    public const string Unlock              = ".unlock {player}";
    public const string God                 = ".god {player}";
    public const string Mortal              = ".mortal {player}";
    public const string ResetCooldown       = ".resetcooldown {player}";
    public const string ListBuffs           = ".listbuffs {player}";
    public const string Spectate            = ".spectate {player} {returnToStart}";
    public const string RenameSelf          = ".rename {newName}";
    public const string RenamePlayer        = ".rename {player} {newName}";
    public const string UnbindPlayer        = ".unbindplayer {player}";
    public const string SwapPlayers         = ".swapplayers {player1} {player2}";
    public const string Fly                 = ".fly {player}";
    public const string FlyUp               = ".flyup {player}";
    public const string FlyDown             = ".flydown {player}";
    public const string FlyLevel            = ".flylevel {floor} {player}";
    public const string FlyHeight           = ".flyheight {height}";
    public const string FlyObstacleHeight   = ".flyobstacleheight {height}";
    public const string Buff                = ".buff {buff} {player} {duration} {immortal}";
    public const string Debuff              = ".debuff {buff} {player}";
    public const string Give                = ".give {item} {quantity}";
    public const string BloodPotion         = ".bloodpotion {type} {quality} {quantity}";
    public const string BloodPotionMix      = ".bloodpotionmix {primaryType} {primaryQuality} {secondaryType} {secondaryQuality} {secondaryTrait} {quantity}";

    // Boost group (.boost, short .bst)
    public const string BstPlayers           = ".bst players";
    public const string BstAttackSpeed       = ".bst attackspeed {speed} {player}";
    public const string BstRemoveAttackSpeed = ".bst removeattackspeed {player}";
    public const string BstDamage            = ".bst damage {damage} {player}";
    public const string BstRemoveDamage      = ".bst removedamage {player}";
    public const string BstHealth            = ".bst health {health} {player}";
    public const string BstRemoveHealth      = ".bst removehealth {player}";
    public const string BstSpeed             = ".bst speed {speed} {player}";
    public const string BstRemoveSpeed       = ".bst removespeed {player}";
    public const string BstYield             = ".bst yield {yield} {player}";
    public const string BstRemoveYield       = ".bst removeyield {player}";
    public const string BstBatVision         = ".bst batvision {player}";
    public const string BstFly               = ".bst fly {player}";
    public const string BstNoAggro           = ".bst noaggro {player}";
    public const string BstNoBloodDrain      = ".bst noblooddrain {player}";
    public const string BstNoCooldown        = ".bst nocooldown {player}";
    public const string BstNoDurability      = ".bst nodurability {player}";
    public const string BstImmaterial        = ".bst immaterial {player}";
    public const string BstInvincible        = ".bst invincible {player}";
    public const string BstShrouded          = ".bst shrouded {player}";
    public const string BstSunInvulnerable   = ".bst suninvulnerable {player}";
    public const string BstState             = ".bst state {player}";

    public const string GearRepair              = ".gear repair {player}";
    public const string GearBreak               = ".gear break {player}";
    public const string GearSoulshardDurability = ".gear soulsharddurability {durability} {player}";
    public const string ClanAdd                 = ".clan add {playerToAdd} {clanName}";
    public const string ClanKick                = ".clan kick {playerToRemove}";
    public const string ClanChangeRole          = ".clan changerole {player} {newRole}";
    public const string PlayerInfo              = ".playerinfo {player}";
    public const string IdCheck                 = ".idcheck {steamid}";
    public const string AssignSteamId           = ".assignsteamID {player} {steamid}";
    public const string PrisonerGruelSettings   = ".prisoner gruelsettings";
    public const string PrisonerFeedSettings    = ".prisoner feedsettings {feed}";

    // ------------------------------------------------------------------ Admin: Server
    public const string RevealMapAll             = ".revealmapforallplayers";
    public const string CleanContainerlessShards = ".cleancontainerlessshards";
    public const string EveryoneDaywalker        = ".everyonedaywalker";
    public const string GlobalBatVision          = ".globalbatvision";
    public const string SetTime                  = ".settime {day} {hour}";
    public const string ForceRespawn             = ".forcerespawn {range}";
    public const string AnnounceList             = ".announce list";
    public const string AnnounceAdd              = ".announce add {name} {message} {time} {oneTime}";
    public const string AnnounceChange           = ".announce change {name} {message} {time} {oneTime}";
    public const string AnnounceRemove           = ".announce remove {name}";
    public const string DropClearAll             = ".dropitems clearall";
    public const string DropClearAllShards       = ".dropitems clearallshards";
    public const string DropRemoveLifetime       = ".dropitems removelifetime";
    public const string DropLifetime             = ".dropitems lifetime {seconds}";
    public const string DropLifetimeWhenDisabled = ".dropitems lifetimewhendisabled {seconds}";
    public const string DropShardLifetime        = ".dropitems shardlifetime {seconds}";
    public const string DropClear                = ".dropitems clear {radius}";
    public const string DropClearShards          = ".dropitems clearshards {radius}";
    public const string RegionListPlayers        = ".region listplayers";
    public const string RegionLock               = ".region lock {region}";
    public const string RegionUnlock             = ".region unlock {region}";
    public const string RegionGate               = ".region gate {region} {level}";
    public const string RegionUngate             = ".region ungate {region}";
    public const string RegionAllow              = ".region allow {player}";
    public const string RegionBan                = ".region ban {player} {region}";
    public const string RegionUnban              = ".region unban {player} {region}";
    public const string RegionListBans           = ".region listbans {region}";
    public const string RegionRemove             = ".region remove {player}";
    public const string BossLock                 = ".boss lock {boss}";
    public const string BossUnlock               = ".boss unlock {boss}";
    public const string BossLockPrimal           = ".boss lockprimal {primalBoss}";
    public const string BossUnlockPrimal         = ".boss unlockprimal {primalBoss}";
    public const string GearHeadgear             = ".gear headgear";
    public const string GearSoulshardFlight      = ".gear soulshardflight";
    public const string GearSoulshardDropMgmt    = ".gear togglesoulsharddropmanagement";
    public const string GearDestroyAllShards     = ".gear destroyallshards";
    public const string GearSoulshardLimit       = ".gear soulshardlimit {limit} {shardType}";
    public const string GearSoulshardDurabilityTime = ".gear soulsharddurabilitytime {seconds}";
    public const string ClanRename               = ".clan rename {oldClanName} {newClanName} {leaderName}";
    public const string PrisonerGruel            = ".prisoner gruel {chance} {min} {max}";
    public const string PrisonerGruelTransform   = ".prisoner grueltransform {prefab}";
    public const string PrisonerFeed             = ".prisoner feed {feed} {healthChangeMin} {healthChangeMax} {miseryChangeMin} {miseryChangeMax} {bloodQualityChangeMin} {bloodQualityChangeMax}";
    public const string PrisonerFeedDefault      = ".prisoner feeddefault {feed}";
    // Staff: top-level commands (KindredCommands Commands/StaffCommands.cs), not a ".staff" group.
    public const string ReloadStaff              = ".reloadstaff";
    public const string ReloadAdmin              = ".reloadadmin";
    public const string AutoAdminAuth            = ".autoadminauth";
    public const string SetStaff                 = ".setstaff {player} {rank}";
    public const string RemoveStaff              = ".removestaff {player}";
    public const string ToggleAdmin              = ".toggleadmin {player}";

    // ------------------------------------------------------------------ Admin: World
    public const string WhereAmI          = ".whereami";
    public const string TeleportHorse     = ".teleporthorse {radius}";
    public const string BossList          = ".boss list";      // lists the LOCKED bosses only (BossCommands.cs)
    public const string SearchItem        = ".search item {search} {page}";
    public const string SearchNpc         = ".search npc {search} {page}";
    public const string SpawnNpc          = ".spawnnpc {character} {count} {level}";
    public const string CustomSpawn       = ".customspawn {unit} {type} {quality} {consumable} {duration} {level}";
    public const string CustomSpawnAt     = ".customspawnat {unit} {x} {y} {z} {type} {quality} {consumable} {duration} {level}";
    public const string DespawnNpc        = ".despawnnpc {character} {radius}";
    public const string SpawnHorse        = ".spawnhorse {speed} {acceleration} {rotation} {num}";
    public const string SpawnBan          = ".spawnban {character} {reason}";
    public const string BossModify        = ".boss modify {boss} {level}";
    public const string BossModifyPrimal  = ".boss modifyprimal {boss} {level}";
    public const string BossTeleportTo    = ".boss teleportto {boss} {whichOne}";
    // Castle: top-level commands (KindredCommands Commands/CastleCommands.cs), not a ".castle" group.
    public const string RelocateReset     = ".relocatereset";
    public const string IncomingDecay     = ".incomingdecay";
    public const string FreezeHeart       = ".freezeheart";
    public const string ThawHeart         = ".thawheart";
    public const string Claim             = ".claim {player}";
    public const string PlotsOwned        = ".plotsowned {page}";
    public const string FrozenHearts      = ".frozenhearts {page}";
    public const string ClanPlotsOwned    = ".clanplotsowned {page}";
    public const string TeleportToPlot    = ".teleporttoplot {territoryIndex}";
    public const string PlotInfo          = ".plotinfo {territoryIndex}";
    public const string ServantConvert    = ".servant convert";
    public const string ServantPerfect    = ".servant perfect";
    public const string ServantHeal       = ".servant heal";
    public const string ServantRevive     = ".servant revive";
    public const string ServantCompleteMission = ".servant completemission";
    public const string ServantChange     = ".servant change {character}";
    public const string ServantAdd        = ".servant add {character}";
    public const string GearRepairAll     = ".gear repairall {range}";
    public const string GearBreakAll      = ".gear breakall {range}";
    public const string LongestOfflineCastles = ".longestofflinecastles";
    public const string ClanCastles       = ".clan castles {clanName}";
    public const string BloodboundAdd     = ".bloodbound add {descriptor}";
    public const string BloodboundRemove  = ".bloodbound remove {descriptor}";

    // ------------------------------------------------------------------ quoting (PLAN-ADDENDUM §1 "Quoting")
    // Placeholders sent through VcfArg.TryEncode (quoted when the value has a space). Every `string` source parameter is
    // listed (KindredCommands has no [Remainder] parameters, so each is one VCF token), plus the converters that match
    // multi-word names after stripping spaces (FoundVBlood, FoundPrimal, FoundPrisonerFeed: "Alpha Wolf",
    // "Blood Snapper"). Numbers, bools and enums are never quoted; KindredAdminTests pins both rules.
    private static readonly Dictionary<string, string[]> Quoted = new()
    {
        [AnnounceAdd]         = new[] { "name", "message", "time" },
        [AnnounceChange]      = new[] { "name", "message", "time" },
        [AnnounceRemove]      = new[] { "name" },
        [ClanAdd]             = new[] { "clanName" },
        [ClanCastles]         = new[] { "clanName" },
        [ClanRename]          = new[] { "oldClanName", "newClanName", "leaderName" },
        [SetStaff]            = new[] { "rank" },
        [SpawnBan]            = new[] { "reason" },
        [SearchItem]          = new[] { "search" },
        [SearchNpc]           = new[] { "search" },
        [BossLock]            = new[] { "boss" },
        [BossUnlock]          = new[] { "boss" },
        [BossLockPrimal]      = new[] { "primalBoss" },
        [BossUnlockPrimal]    = new[] { "primalBoss" },
        [BossModify]          = new[] { "boss" },
        [BossModifyPrimal]    = new[] { "boss" },
        [BossTeleportTo]      = new[] { "boss" },
        [PrisonerFeed]        = new[] { "feed" },
        [PrisonerFeedDefault] = new[] { "feed" },
        [PrisonerFeedSettings] = new[] { "feed" },
    };

    // Optional trailing parameters the form may leave blank (the token is then dropped). Only the last placeholder(s).
    private static readonly Dictionary<string, string[]> Blankable = new()
    {
        [ClanRename] = new[] { "leaderName" },
    };

    public static bool IsQuoted(string template, string field)
        => template != null && Quoted.TryGetValue(template, out var f) && Array.IndexOf(f, field) >= 0;

    public static bool IsBlankable(string template, string field)
        => template != null && Blankable.TryGetValue(template, out var f) && Array.IndexOf(f, field) >= 0;

    /// <summary>Every command constant, for the tests.</summary>
    public static IEnumerable<(string Name, string Template)> All()
        => typeof(KindredAdminCommands)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()));

    /// <summary>The templates named in the quoting / blank tables (the tests check each is a real template).</summary>
    public static IEnumerable<(string Table, string Template, string Field)> Declarations()
        => Quoted.SelectMany(kv => kv.Value.Select(f => ("quoted", kv.Key, f)))
           .Concat(Blankable.SelectMany(kv => kv.Value.Select(f => ("blankable", kv.Key, f))));
}
