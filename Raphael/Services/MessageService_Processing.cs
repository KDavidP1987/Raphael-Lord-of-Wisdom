using System.Collections.Generic;
using System.Text.RegularExpressions;
using Raphael.Utils;

namespace Raphael.Services;

// Companion partial to MessageService.cs.
//
// Holds:
//   1. BCCOM_* command-string constants (single source of truth - Bloodcraft
//      renames are a single-file edit).
//   2. The "legacy" inbound regex pipeline (Phase 3b): for chat replies
//      Bloodcraft doesn't ship via the structured Eclipse protocol -
//      .fam boxes lists, .fam l box-content lists, etc.
//      Eclipse-protocol messages are intercepted earlier by
//      EclipseProtocolService.TryHandleServerMessage. Anything that gets
//      here is plain colored chat from Bloodcraft.
//
// PORT REFERENCE for the regex code:
//   LearningMods/BloodCraftUI-master/BloodCraftUI/Services/MessageService_Processing.cs
public static partial class MessageService
{
    // =========================================================================
    // Command constants
    // =========================================================================

    // ---------- Familiar (.fam) ----------
    public const string BCCOM_FAM_BOXES                  = ".fam boxes";
    public const string BCCOM_FAM_LIST_CURRENT_BOX       = ".fam l";         // lists familiars in the currently-selected box
    public const string BCCOM_FAM_SWITCH_BOX_FORMAT      = ".fam cb {0}";    // {0} = box name
    public const string BCCOM_FAM_BIND_BY_INDEX_FORMAT   = ".fam b {0}";     // {0} = index within current box
    public const string BCCOM_FAM_ADDBOX_FORMAT          = ".fam ab {0}";    // {0} = new box name
    public const string BCCOM_FAM_DELETEBOX_FORMAT       = ".fam db {0}";    // {0} = box name (must be empty)
    public const string BCCOM_FAM_RENAMEBOX_FORMAT       = ".fam rb {0} {1}"; // {0} = current name, {1} = new name
    public const string BCCOM_FAM_MOVEBOX_FORMAT         = ".fam mb {0}";    // {0} = destination box; acts on currently bound familiar
    public const string BCCOM_FAM_REMOVE_FORMAT          = ".fam r {0}";     // {0} = index in current box; permanently deletes from collection
    public const string BCCOM_FAM_UNBIND                 = ".fam ub";       // Releases the active familiar entity. Box record + unlock data preserved — re-bind any time with .fam b N.
    public const string BCCOM_FAM_TOGGLE                 = ".fam t";        // calls/dismisses (recallable)
    public const string BCCOM_FAM_COMBAT                 = ".fam c";        // toggle combat on/off
    public const string BCCOM_FAM_TOGGLE_EMOTES          = ".fam e";        // enable/disable emote-action bindings (e.g. clap = open inventory)
    public const string BCCOM_FAM_LIST_EMOTES            = ".fam actions";  // list current emote-action bindings
    public const string BCCOM_FAM_PRESTIGE               = ".fam pr";
    public const string BCCOM_FAM_GET_LEVEL              = ".fam gl";
    public const string BCCOM_FAM_ENABLE_EQUIP           = ".fam smartbind"; // legacy constant; smartbind takes a name (see BCCOM_FAM_SMARTBIND_FORMAT)

    // ---- 0.6.0 .fam audit additions ----
    // 0.10.7: name-arg commands MUST quote the arg so VCF parses multi-word
    // familiar names correctly. Pre-0.10.7 sent `.fam s Alpha the White Wolf`
    // → VCF only consumed "Alpha" and printed the usage echo `.fam s [Name]`
    // (the `usage:` value from Bloodcraft's [Command(usage: ...)] attribute),
    // which broke the V-Blood scanner entirely and leaked usage echoes to
    // chat regardless of suppression. The intercept-arming Substring parse
    // strips the wrapping quotes so scanner correlation still works.
    public const string BCCOM_FAM_SEARCH_FORMAT          = ".fam s \"{0}\"";       // search boxes by name (quoted)
    public const string BCCOM_FAM_SMARTBIND_FORMAT       = ".fam sb \"{0}\"";      // search and bind in one step (quoted)
    public const string BCCOM_FAM_SHINY_FORMAT           = ".fam shiny {0}";       // make active familiar shiny ([SpellSchool] = blood/storm/unholy/chaos/frost/illusion)
    public const string BCCOM_FAM_OPTION_FORMAT          = ".fam option {0}";      // toggle a per-player familiar setting (e.g. shiny, vbloodemotes)
    public const string BCCOM_FAM_ECHOES_FORMAT          = ".fam echoes \"{0}\"";  // purchase exo reward via VBlood essence (quoted)
    public const string BCCOM_FAM_RESET                  = ".fam reset";       // Force-cleanup: destroys any leftover entities in the FollowerBuffer and clears the active-familiar record so a stuck familiar can be re-bound. Box records and unlocks are NOT touched — familiars can be re-summoned via .fam b N afterwards. Server-side handler also refuses to run if the active familiar is still alive — user must .fam ub first in that case.

    // 0.14.0: battle-group / challenge BCCOM_* constants removed.
    // Bloodcraft v1.1+ never wired the underlying server features — the
    // .fam bgs / .fam bg / .fam abg / .fam cbg / .fam sbg / .fam dbg /
    // .fam challenge commands appear in the Bloodcraft README but are
    // no-ops on the server (confirmed in chat by a Bloodcraft server
    // admin). Backing forms removed from the Familiars tab in the same
    // release; the intercept startsWith branches below were also removed.

    // ---------- Leveling (.lvl) ----------
    public const string BCCOM_LVL_GET            = ".lvl get";
    public const string BCCOM_LVL_LOG_TOGGLE     = ".lvl log";                  // toggle in-chat XP gain logging
    public const string BCCOM_LVL_IGNORE_FORMAT  = ".lvl ignore {0}";           // admin: add/remove player from shared-XP exclusion

    // ---------- Prestige (player-facing) ----------
    public const string BCCOM_PRESTIGE_LIST                = ".prestige l";
    public const string BCCOM_PRESTIGE_SYNC_BUFFS          = ".prestige sb";
    public const string BCCOM_PRESTIGE_TOGGLE_EXOFORM      = ".prestige exoform";
    public const string BCCOM_PRESTIGE_TOGGLE_SHROUD       = ".prestige shroud";
    // .prestige me/get/lb take a PrestigeType; .prestige sf takes an ExoformVariant.
    public const string BCCOM_PRESTIGE_ME_FORMAT           = ".prestige me {0}";
    public const string BCCOM_PRESTIGE_GET_FORMAT          = ".prestige get {0}";
    public const string BCCOM_PRESTIGE_LEADERBOARD_FORMAT  = ".prestige lb {0}";
    public const string BCCOM_PRESTIGE_SELECT_FORM_FORMAT  = ".prestige sf {0}";
    // ---- 0.7.0 prestige audit additions (admin-only) ----
    public const string BCCOM_PRESTIGE_IGNORE_LEADERBOARD_FORMAT = ".prestige ignore {0}"; // admin: toggle leaderboard exclusion for player
    // The "iacknowledge..." nuke is intentionally spelled out in full as a guard
    // against accidental copy/paste use. Only fires from the form with a confirm.
    public const string BCCOM_PRESTIGE_GLOBAL_BUFF_PURGE
        = ".prestige iacknowledgethiswillremoveallprestigebuffsfromplayersandwantthattohappen";

    // ---- 0.7.0 quest audit addition ----
    public const string BCCOM_QUEST_COMPLETE_FORMAT = ".quest c {0} {1}";  // admin: force-complete a quest for a player. {0}=player, {1}=Daily/Weekly

    // ---------- Blood legacy (.bl) ----------
    public const string BCCOM_BL_GET                 = ".bl get";
    public const string BCCOM_BL_GET_FORMAT          = ".bl get {0}";       // {0} = blood type name (queries any blood)
    public const string BCCOM_BL_LIST                = ".bl l";             // list legacy types
    public const string BCCOM_BL_LIST_STATS          = ".bl lst";           // list selectable bonus stats with indices
    public const string BCCOM_BL_RESET_STATS         = ".bl rst";           // reset chosen stats for current blood
    public const string BCCOM_BL_CHOOSE_STAT_FORMAT  = ".bl cst {0} {1}";   // {0} = blood type name, {1} = 1-based stat index

    // ---------- Profession (.prof) ----------
    // Player-facing 'log' / 'get' / 'list', admin 'set'. Use BloodcraftProfession
    // enum for the type argument so the dropdown emits valid names.
    public const string BCCOM_PROF_LOG_TOGGLE   = ".prof log";
    public const string BCCOM_PROF_GET_FORMAT   = ".prof get {0}";       // {0} = profession name (or blank)
    public const string BCCOM_PROF_LIST         = ".prof l";
    public const string BCCOM_PROF_SET_FORMAT   = ".prof set {0} {1} {2}"; // admin: {0}=player, {1}=profession, {2}=level

    // ---------- Misc (.misc) ----------
    public const string BCCOM_MISC_HEALTH         = ".misc health";
    // ---- 0.6.0 .misc audit additions (player-facing) ----
    public const string BCCOM_MISC_REMINDERS      = ".misc remindme";          // toggle general feature reminders
    public const string BCCOM_MISC_SCT_FORMAT     = ".misc sct {0}";           // toggle SCT element [Type]
    public const string BCCOM_MISC_KIT_ME         = ".misc kitme";             // claim starter kit
    public const string BCCOM_MISC_PREPARE        = ".misc prepare";           // complete GettingReadyForTheHunt
    public const string BCCOM_MISC_USER_STATS     = ".misc userstats";         // print neat player info
    public const string BCCOM_MISC_SILENCE        = ".misc silence";           // reset stuck combat music

    // ---------- Quests (.quest) ----------
    public const string BCCOM_QUEST_PROGRESS_DAILY  = ".quest p d";       // print daily quest objective
    public const string BCCOM_QUEST_PROGRESS_WEEKLY = ".quest p w";       // print weekly quest objective
    public const string BCCOM_QUEST_TRACK_DAILY     = ".quest t d";       // print location/direction to daily target
    public const string BCCOM_QUEST_TRACK_WEEKLY    = ".quest t w";       // print location/direction to weekly target
    public const string BCCOM_QUEST_REROLL_DAILY    = ".quest r d";       // reroll daily (costs configured item)
    public const string BCCOM_QUEST_REROLL_WEEKLY   = ".quest r w";       // reroll weekly (costs configured item)
    public const string BCCOM_QUEST_LOG_TOGGLE      = ".quest log";       // toggle in-chat progress logging

    // ---------- Class (.class) ----------
    public const string BCCOM_CLASS_LIST          = ".class l";
    public const string BCCOM_CLASS_LIST_SPELLS   = ".class lsp";
    public const string BCCOM_CLASS_LIST_STATS    = ".class lst";
    public const string BCCOM_CLASS_TOGGLE_SHIFT  = ".class shift";
    public const string BCCOM_CLASS_SELECT_FORMAT = ".class s {0}";   // {0} = enum name (BloodKnight, DemonHunter, ...)
    public const string BCCOM_CLASS_CHANGE_FORMAT = ".class c {0}";   // change an EXISTING class (.class s only works with no class); costs 750× Greater Stygian Shards by default
    public const string BCCOM_CLASS_CHOOSE_SHIFT_FORMAT = ".class csp {0}"; // {0}: 0 = default spell (Veil of Shadow); 1..N = class spells as numbered by List Spells. Needs Toggle Shift on.

    // ---------- Weapon expertise (.wep) ----------
    public const string BCCOM_WEP_GET                = ".wep get";
    public const string BCCOM_WEP_LIST               = ".wep l";
    public const string BCCOM_WEP_LIST_STATS         = ".wep lst";
    public const string BCCOM_WEP_RESET_STATS        = ".wep rst";
    public const string BCCOM_WEP_LOCK_SPELLS        = ".wep locksp";
    public const string BCCOM_WEP_CHOOSE_STAT_FORMAT = ".wep cst {0} {1}";  // {0} = weapon type name, {1} = 1-based stat index

    // =========================================================================
    // KindredLogistics commands (separate server mod, used in the KINDRED tab)
    // =========================================================================

    // ---------- Personal toggles (.l <flag>) — each toggles a per-player flag.
    public const string BCCOM_KL_SORT_STASH        = ".l ss";
    public const string BCCOM_KL_CRAFT_PULL        = ".l cr";
    public const string BCCOM_KL_DONT_PULL_LAST    = ".l dpl";
    public const string BCCOM_KL_AUTOSTASH_MISSION = ".l asm";
    public const string BCCOM_KL_CONVEYOR          = ".l co";
    public const string BCCOM_KL_SALVAGE           = ".l sal";
    public const string BCCOM_KL_UNIT_SPAWNER      = ".l us";
    public const string BCCOM_KL_BRAZIER           = ".l bz";
    public const string BCCOM_KL_SILENT_PULL       = ".l sp";
    public const string BCCOM_KL_SILENT_STASH      = ".l ssh";
    public const string BCCOM_KL_SETTINGS          = ".l s";

    // ---------- Utility commands (no group) — player-facing.
    public const string BCCOM_KL_STASH_ALL              = ".stash";
    public const string BCCOM_KL_PULL_ITEM_FORMAT       = ".pull {0} {1}";   // {0} = item name, {1} = qty
    public const string BCCOM_KL_FIND_ITEM_FORMAT       = ".fi {0}";         // {0} = item name
    public const string BCCOM_KL_FIND_CHEST_FORMAT      = ".fc {0}";         // {0} = chest name

    // ---------- Admin globals (.lg <flag>) — server-wide.
    public const string BCCOM_KL_ADMIN_SORT_STASH        = ".lg ss";
    public const string BCCOM_KL_ADMIN_PULL              = ".lg p";
    public const string BCCOM_KL_ADMIN_CRAFT_PULL        = ".lg cr";
    public const string BCCOM_KL_ADMIN_AUTOSTASH_MISSION = ".lg asm";
    public const string BCCOM_KL_ADMIN_CONVEYOR          = ".lg co";
    public const string BCCOM_KL_ADMIN_SALVAGE           = ".lg sal";
    public const string BCCOM_KL_ADMIN_UNIT_SPAWNER      = ".lg us";
    public const string BCCOM_KL_ADMIN_BRAZIER           = ".lg bz";
    public const string BCCOM_KL_ADMIN_NAMED_BRAZIER     = ".lg nam";
    public const string BCCOM_KL_ADMIN_TRASH             = ".lg trash";
    public const string BCCOM_KL_ADMIN_SETTINGS          = ".lg s";

    // ---------- Admin utility (no group) — admin-only.
    public const string BCCOM_KL_ADMIN_EMPTY_TRASH       = ".emptytrash";
    public const string BCCOM_KL_ADMIN_STASH_SPAWN_FORMAT = ".adminstash {0} {1}";

    // =========================================================================
    // KindredCommands - player-facing commands (Phase 5h)
    //
    // The admin command surface is huge (~120 commands) and lands in Phase 5i
    // under separate sub-tabs (Players / Server / World). This block covers
    // only the 13 commands a non-admin player will actually use.
    // =========================================================================

    // ---------- Self (no group) ----------
    public const string BCCOM_KC_AFK             = ".afk";
    public const string BCCOM_KC_PING            = ".ping";
    public const string BCCOM_KC_PACE            = ".pace";

    // ---------- Server info (zero-arg listings) ----------
    public const string BCCOM_KC_TIME            = ".time";
    public const string BCCOM_KC_STAFF           = ".staff";
    public const string BCCOM_KC_BOSS_LIST       = Kindred.KindredAdminCommands.BossList;
    public const string BCCOM_KC_REGION_LIST     = ".region list";
    public const string BCCOM_KC_CASTLE_OPEN_PLOTS = ".openplots";  // not in castle group; top-level command (alias .op)
    public const string BCCOM_KC_GEAR_SOULSHARD_STATUS = ".gear soulshardstatus";
    public const string BCCOM_KC_CLAN_LIST       = ".clan list";

    // ---------- Lookups (forms) ----------
    public const string BCCOM_KC_CHECK_LEVEL_FORMAT   = ".checklevel {0}"; // {0} = player name
    public const string BCCOM_KC_CLAN_MEMBERS_FORMAT  = ".clan members {0}"; // {0} = clan name

    // =========================================================================
    // KindredCommands - admin commands (Phase 5i)
    //
    // ~146 admin commands surveyed and split across 3 sub-tabs (Players / Server
    // / World). Constants below are organized in the same shape so UI code can
    // walk them top-to-bottom.
    //
    // Many admin commands take an optional `player:OnlinePlayer=null` final arg
    // that defaults to self when omitted; those keep a trailing `{0}` slot so
    // the form can leave it blank for self-target.
    // =========================================================================

    // -------------------------------------------------------------------------
    // ADMIN: PLAYERS sub-tab
    // -------------------------------------------------------------------------

    // ---- General player commands (no group) ----
    public const string BCCOM_KCA_REVIVE_TARGET           = Kindred.KindredAdminCommands.ReviveTarget;
    public const string BCCOM_KCA_RENAME_SELF_FORMAT      = ".rename {0}";                    // newName
    public const string BCCOM_KCA_RENAME_PLAYER_FORMAT    = ".rename {0} {1}";                // player, newName
    public const string BCCOM_KCA_UNBIND_PLAYER_FORMAT    = ".unbindplayer {0}";              // player
    public const string BCCOM_KCA_SWAP_PLAYERS_FORMAT     = ".swapplayers {0} {1}";           // player1, player2
    public const string BCCOM_KCA_UNLOCK_FORMAT           = ".unlock {0}";                    // player (optional)
    public const string BCCOM_KCA_REVEALMAP_FORMAT        = ".revealmap {0}";                 // player (optional)
    public const string BCCOM_KCA_TELEPORT_FORMAT         = ".teleport {0} {1} {2} {3}";      // x y z player
    public const string BCCOM_KCA_FLY_FORMAT              = ".fly {0}";                       // player (optional)
    public const string BCCOM_KCA_FLYUP_FORMAT            = ".flyup {0}";                     // player
    public const string BCCOM_KCA_FLYDOWN_FORMAT          = ".flydown {0}";                   // player
    public const string BCCOM_KCA_FLYLEVEL_FORMAT         = ".flylevel {0} {1}";              // floor, player
    public const string BCCOM_KCA_FLYHEIGHT_FORMAT        = ".flyheight {0}";                 // height (default 30)
    public const string BCCOM_KCA_FLY_OBSTACLE_HEIGHT_FORMAT = ".flyobstacleheight {0}";      // height (default 7)
    public const string BCCOM_KCA_KILL_PLAYER_FORMAT      = ".killplayer {0}";                // player
    public const string BCCOM_KCA_STAY_DOWN_FORMAT        = ".staydown {0}";                  // player
    public const string BCCOM_KCA_HEART_COUNT_FORMAT      = ".playerheartcount {0} {1}";      // amount, player
    public const string BCCOM_KCA_REVIVE_FORMAT           = ".revive {0}";                    // player (optional)
    public const string BCCOM_KCA_BUFF_FORMAT             = ".buff {0} {1} {2} {3}";          // buff, player, duration, immortal
    public const string BCCOM_KCA_DEBUFF_FORMAT           = ".debuff {0} {1}";                // buff, player
    public const string BCCOM_KCA_LISTBUFFS_FORMAT        = ".listbuffs {0}";                 // player (optional)
    public const string BCCOM_KCA_GIVE_FORMAT             = ".give {0} {1}";                  // item, quantity
    public const string BCCOM_KCA_BLOODPOTION_FORMAT      = ".bloodpotion {0} {1} {2}";       // type, quality, quantity
    public const string BCCOM_KCA_BLOODPOTION_MIX_FORMAT  = ".bloodpotionmix {0} {1} {2} {3} {4} {5}"; // pType,pQual,sType,sQual,sTrait,qty
    public const string BCCOM_KCA_GOD_FORMAT              = ".god {0}";                       // player
    public const string BCCOM_KCA_MORTAL_FORMAT           = ".mortal {0}";                    // player
    public const string BCCOM_KCA_SPECTATE_FORMAT         = ".spectate {0} {1}";              // player, returnToStart
    public const string BCCOM_KCA_RESET_COOLDOWN_FORMAT   = ".resetcooldown {0}";             // player (optional)

    // ---- Boost (bst) group ----
    public const string BCCOM_KCA_BST_PLAYERS                = Kindred.KindredAdminCommands.BstPlayers;
    public const string BCCOM_KCA_BST_STATE_FORMAT           = ".bst state {0}";
    public const string BCCOM_KCA_BST_ATTACK_SPEED_FORMAT    = ".bst attackspeed {0} {1}";    // speed, player
    public const string BCCOM_KCA_BST_REMOVE_ATTACK_SPEED_FORMAT = ".bst removeattackspeed {0}";
    public const string BCCOM_KCA_BST_DAMAGE_FORMAT          = ".bst damage {0} {1}";
    public const string BCCOM_KCA_BST_REMOVE_DAMAGE_FORMAT   = ".bst removedamage {0}";
    public const string BCCOM_KCA_BST_HEALTH_FORMAT          = ".bst health {0} {1}";
    public const string BCCOM_KCA_BST_REMOVE_HEALTH_FORMAT   = ".bst removehealth {0}";
    public const string BCCOM_KCA_BST_SPEED_FORMAT           = ".bst speed {0} {1}";
    public const string BCCOM_KCA_BST_REMOVE_SPEED_FORMAT    = ".bst removespeed {0}";
    public const string BCCOM_KCA_BST_YIELD_FORMAT           = ".bst yield {0} {1}";
    public const string BCCOM_KCA_BST_REMOVE_YIELD_FORMAT    = ".bst removeyield {0}";
    public const string BCCOM_KCA_BST_BAT_VISION_FORMAT      = ".bst batvision {0}";
    public const string BCCOM_KCA_BST_FLY_FORMAT             = ".bst fly {0}";
    public const string BCCOM_KCA_BST_NO_AGGRO_FORMAT        = ".bst noaggro {0}";
    public const string BCCOM_KCA_BST_NO_BLOOD_DRAIN_FORMAT  = ".bst noblooddrain {0}";
    public const string BCCOM_KCA_BST_NO_COOLDOWN_FORMAT     = ".bst nocooldown {0}";
    public const string BCCOM_KCA_BST_NO_DURABILITY_FORMAT   = ".bst nodurability {0}";
    public const string BCCOM_KCA_BST_IMMATERIAL_FORMAT      = ".bst immaterial {0}";
    public const string BCCOM_KCA_BST_INVINCIBLE_FORMAT      = ".bst invincible {0}";
    public const string BCCOM_KCA_BST_SHROUDED_FORMAT        = ".bst shrouded {0}";
    public const string BCCOM_KCA_BST_SUN_INVULNERABLE_FORMAT = ".bst suninvulnerable {0}";

    // ---- Gear group (player-targeting subset) ----
    public const string BCCOM_KCA_GEAR_REPAIR_FORMAT          = ".gear repair {0}";           // player (optional)
    public const string BCCOM_KCA_GEAR_BREAK_FORMAT           = ".gear break {0}";            // player (optional)
    public const string BCCOM_KCA_GEAR_SS_DURABILITY_FORMAT   = ".gear soulsharddurability {0} {1}"; // durability, player

    // ---- Clan group (player-targeting subset) ----
    public const string BCCOM_KCA_CLAN_ADD_FORMAT             = ".clan add {0} {1}";          // player, clanName
    public const string BCCOM_KCA_CLAN_KICK_FORMAT            = ".clan kick {0}";             // player
    public const string BCCOM_KCA_CLAN_CHANGE_ROLE_FORMAT     = ".clan changerole {0} {1}";   // player, role

    // -------------------------------------------------------------------------
    // ADMIN: SERVER sub-tab
    // -------------------------------------------------------------------------

    // ---- General server commands (no group) ----
    public const string BCCOM_KCA_REVEALMAP_ALL              = Kindred.KindredAdminCommands.RevealMapAll;
    public const string BCCOM_KCA_CLEAN_CONTAINERLESS_SHARDS = Kindred.KindredAdminCommands.CleanContainerlessShards;
    public const string BCCOM_KCA_EVERYONE_DAYWALKER         = Kindred.KindredAdminCommands.EveryoneDaywalker;
    public const string BCCOM_KCA_GLOBAL_BAT_VISION          = Kindred.KindredAdminCommands.GlobalBatVision;
    public const string BCCOM_KCA_SETTIME_FORMAT             = ".settime {0} {1}";            // day, hour
    public const string BCCOM_KCA_FORCE_RESPAWN_FORMAT       = ".forcerespawn {0}";           // range (default 10)

    // ---- Announce group ----
    public const string BCCOM_KCA_ANNOUNCE_LIST              = Kindred.KindredAdminCommands.AnnounceList;
    public const string BCCOM_KCA_ANNOUNCE_ADD_FORMAT        = ".announce add {0} {1} {2} {3}"; // name, message, time, oneTime
    public const string BCCOM_KCA_ANNOUNCE_CHANGE_FORMAT     = ".announce change {0} {1} {2} {3}";
    public const string BCCOM_KCA_ANNOUNCE_REMOVE_FORMAT     = ".announce remove {0}";        // name

    // ---- Drop items group ----
    public const string BCCOM_KCA_DROP_REMOVE_LIFETIME       = Kindred.KindredAdminCommands.DropRemoveLifetime;
    public const string BCCOM_KCA_DROP_CLEAR_ALL             = Kindred.KindredAdminCommands.DropClearAll;
    public const string BCCOM_KCA_DROP_CLEAR_ALL_SHARDS      = Kindred.KindredAdminCommands.DropClearAllShards;
    public const string BCCOM_KCA_DROP_LIFETIME_FORMAT       = ".dropitems lifetime {0}";      // seconds
    public const string BCCOM_KCA_DROP_LIFETIME_DISABLED_FORMAT = ".dropitems lifetimewhendisabled {0}";
    public const string BCCOM_KCA_DROP_SHARD_LIFETIME_FORMAT = ".dropitems shardlifetime {0}";
    public const string BCCOM_KCA_DROP_CLEAR_FORMAT          = ".dropitems clear {0}";        // radius
    public const string BCCOM_KCA_DROP_CLEAR_SHARDS_FORMAT   = ".dropitems clearshards {0}";  // radius

    // ---- Region group ----
    public const string BCCOM_KCA_REGION_LIST_PLAYERS        = Kindred.KindredAdminCommands.RegionListPlayers;
    public const string BCCOM_KCA_REGION_LOCK_FORMAT         = ".region lock {0}";            // region
    public const string BCCOM_KCA_REGION_UNLOCK_FORMAT       = ".region unlock {0}";
    public const string BCCOM_KCA_REGION_GATE_FORMAT         = ".region gate {0} {1}";        // region, level
    public const string BCCOM_KCA_REGION_UNGATE_FORMAT       = ".region ungate {0}";
    public const string BCCOM_KCA_REGION_ALLOW_FORMAT        = ".region allow {0}";           // player
    public const string BCCOM_KCA_REGION_BAN_FORMAT          = ".region ban {0} {1}";         // player, region
    public const string BCCOM_KCA_REGION_UNBAN_FORMAT        = ".region unban {0} {1}";
    public const string BCCOM_KCA_REGION_LIST_BANS_FORMAT    = ".region listbans {0}";        // region
    public const string BCCOM_KCA_REGION_REMOVE_FORMAT       = ".region remove {0}";          // player

    // ---- Boss group (server lock subset) ----
    public const string BCCOM_KCA_BOSS_LOCK_FORMAT           = ".boss lock {0}";              // boss
    public const string BCCOM_KCA_BOSS_UNLOCK_FORMAT         = ".boss unlock {0}";
    public const string BCCOM_KCA_BOSS_LOCK_PRIMAL_FORMAT    = ".boss lockprimal {0}";
    public const string BCCOM_KCA_BOSS_UNLOCK_PRIMAL_FORMAT  = ".boss unlockprimal {0}";

    // ---- Gear group (server subset) ----
    public const string BCCOM_KCA_GEAR_HEADGEAR              = Kindred.KindredAdminCommands.GearHeadgear;
    public const string BCCOM_KCA_GEAR_SS_FLIGHT             = Kindred.KindredAdminCommands.GearSoulshardFlight;
    public const string BCCOM_KCA_GEAR_SS_DROP_MGMT          = Kindred.KindredAdminCommands.GearSoulshardDropMgmt;
    public const string BCCOM_KCA_GEAR_DESTROY_ALL_SHARDS    = Kindred.KindredAdminCommands.GearDestroyAllShards;
    public const string BCCOM_KCA_GEAR_SS_LIMIT_FORMAT       = ".gear soulshardlimit {0} {1}"; // limit, shardType
    public const string BCCOM_KCA_GEAR_SS_DURATION_FORMAT    = ".gear soulsharddurabilitytime {0}"; // seconds

    // ---- Clan group (server rename) ----
    public const string BCCOM_KCA_CLAN_RENAME_FORMAT         = ".clan rename {0} {1} {2}";    // old, new, leader

    // ---- Prisoner group ----
    public const string BCCOM_KCA_PRISONER_GRUEL_FORMAT      = ".prisoner gruel {0} {1} {2}"; // chance, min, max
    public const string BCCOM_KCA_PRISONER_GRUEL_XFORM_FORMAT = ".prisoner grueltransform {0}";// prefab
    public const string BCCOM_KCA_PRISONER_FEED_FORMAT       = ".prisoner feed {0} {1} {2} {3} {4} {5} {6}"; // feed,hMin,hMax,mMin,mMax,qMin,qMax
    public const string BCCOM_KCA_PRISONER_FEED_DEFAULT_FORMAT = ".prisoner feeddefault {0}"; // feed

    // ---- Staff (0.73.0: top-level commands in KindredCommands StaffCommands.cs; there is no ".staff" group) ----
    public const string BCCOM_KCA_STAFF_RELOAD_STAFF         = Kindred.KindredAdminCommands.ReloadStaff;
    public const string BCCOM_KCA_STAFF_RELOAD_ADMIN         = Kindred.KindredAdminCommands.ReloadAdmin;
    public const string BCCOM_KCA_STAFF_AUTO_ADMIN_AUTH      = Kindred.KindredAdminCommands.AutoAdminAuth;
    public const string BCCOM_KCA_STAFF_SET_STAFF_FORMAT     = ".setstaff {0} {1}";           // player, rank
    public const string BCCOM_KCA_STAFF_REMOVE_STAFF_FORMAT  = ".removestaff {0}";            // player
    public const string BCCOM_KCA_STAFF_TOGGLE_ADMIN_FORMAT  = ".toggleadmin {0}";            // player

    // -------------------------------------------------------------------------
    // ADMIN: WORLD sub-tab
    // -------------------------------------------------------------------------

    // ---- General world commands (no group) ----
    public const string BCCOM_KCA_WHERE_AM_I                 = Kindred.KindredAdminCommands.WhereAmI;
    public const string BCCOM_KCA_SPAWN_NPC_FORMAT           = ".spawnnpc {0} {1} {2}";       // unit, count, level
    public const string BCCOM_KCA_CUSTOM_SPAWN_FORMAT        = ".customspawn {0} {1} {2} {3} {4} {5}"; // unit,type,qual,consumable,duration,level
    public const string BCCOM_KCA_CUSTOM_SPAWN_AT_FORMAT     = ".customspawnat {0} {1} {2} {3} {4} {5} {6} {7} {8}"; // unit,x,y,z,type,qual,consumable,duration,level
    public const string BCCOM_KCA_DESPAWN_NPC_FORMAT         = ".despawnnpc {0} {1}";         // unit, radius
    public const string BCCOM_KCA_SPAWN_HORSE_FORMAT         = ".spawnhorse {0} {1} {2} {3}"; // speed, accel, rotation, num
    public const string BCCOM_KCA_SPAWN_BAN_FORMAT           = ".spawnban {0} {1}";           // unit, reason
    public const string BCCOM_KCA_TELEPORT_HORSE_FORMAT      = ".teleporthorse {0}";          // radius

    // ---- Search group ----
    public const string BCCOM_KCA_SEARCH_ITEM_FORMAT         = ".search item {0} {1}";        // query, page
    public const string BCCOM_KCA_SEARCH_NPC_FORMAT          = ".search npc {0} {1}";

    // ---- Boss group (world: modify+teleport) ----
    public const string BCCOM_KCA_BOSS_MODIFY_FORMAT         = ".boss modify {0} {1}";
    public const string BCCOM_KCA_BOSS_MODIFY_PRIMAL_FORMAT  = ".boss modifyprimal {0} {1}";
    public const string BCCOM_KCA_BOSS_TELEPORT_TO_FORMAT    = ".boss teleportto {0} {1}";    // boss, whichOne

    // ---- Castle (0.73.0: top-level commands in KindredCommands CastleCommands.cs; there is no ".castle" group) ----
    public const string BCCOM_KCA_CASTLE_RELOCATE_RESET      = Kindred.KindredAdminCommands.RelocateReset;
    public const string BCCOM_KCA_CASTLE_INCOMING_DECAY      = Kindred.KindredAdminCommands.IncomingDecay;
    public const string BCCOM_KCA_CASTLE_FREEZE_HEART        = Kindred.KindredAdminCommands.FreezeHeart;
    public const string BCCOM_KCA_CASTLE_THAW_HEART          = Kindred.KindredAdminCommands.ThawHeart;
    public const string BCCOM_KCA_CASTLE_CLAIM_FORMAT        = ".claim {0}";                  // player (optional)
    public const string BCCOM_KCA_CASTLE_PLOTS_OWNED_FORMAT  = ".plotsowned {0}";             // page
    public const string BCCOM_KCA_CASTLE_FROZEN_HEARTS_FORMAT = ".frozenhearts {0}";          // page
    public const string BCCOM_KCA_CASTLE_CLAN_PLOTS_OWNED_FORMAT = ".clanplotsowned {0}";     // page
    public const string BCCOM_KCA_CASTLE_TELEPORT_PLOT_FORMAT = ".teleporttoplot {0}";        // territoryIndex
    public const string BCCOM_KCA_CASTLE_PLOT_INFO_FORMAT    = ".plotinfo {0}";               // territoryIndex

    // ---- Servant group ----
    public const string BCCOM_KCA_SERVANT_CONVERT            = Kindred.KindredAdminCommands.ServantConvert;
    public const string BCCOM_KCA_SERVANT_PERFECT            = Kindred.KindredAdminCommands.ServantPerfect;
    public const string BCCOM_KCA_SERVANT_HEAL               = Kindred.KindredAdminCommands.ServantHeal;
    public const string BCCOM_KCA_SERVANT_REVIVE             = Kindred.KindredAdminCommands.ServantRevive;
    public const string BCCOM_KCA_SERVANT_COMPLETE_MISSION   = Kindred.KindredAdminCommands.ServantCompleteMission;
    public const string BCCOM_KCA_SERVANT_CHANGE_FORMAT      = ".servant change {0}";         // character
    public const string BCCOM_KCA_SERVANT_ADD_FORMAT         = ".servant add {0}";

    // ---- Gear group (world: range-based) ----
    public const string BCCOM_KCA_GEAR_REPAIR_ALL_FORMAT     = ".gear repairall {0}";         // range
    public const string BCCOM_KCA_GEAR_BREAK_ALL_FORMAT      = ".gear breakall {0}";

    // ---------- 0.5.0 audit additions ----------
    // Player-info / lookup (all admin-only despite reading what feels like user info)
    public const string BCCOM_KCA_PLAYERINFO_FORMAT          = ".playerinfo {0}";             // player name
    public const string BCCOM_KCA_IDCHECK_FORMAT             = ".idcheck {0}";                // steamID
    public const string BCCOM_KCA_ASSIGN_STEAMID_FORMAT      = ".assignsteamID {0} {1}";      // player, steamID
    public const string BCCOM_KCA_LONGEST_OFFLINE_CASTLES    = Kindred.KindredAdminCommands.LongestOfflineCastles;
    // 0.73.0: .showhair, .unbindall, .wipe / .commencewipe / .cancelwipe and .clan fix constants removed: those
    // commands are commented out in KindredCommands 2.5.8, and their buttons were removed from the admin tabs.
    // Clan
    public const string BCCOM_KCA_CLAN_CASTLES_FORMAT        = ".clan castles {0}";           // clan name
    // Prisoner config readouts (paired with the existing .prisoner gruel/feed setters).
    // 0.73.0: both are in the .prisoner group, and feedsettings needs a feed (was ".gruelsettings" / ".feedsettings").
    public const string BCCOM_KCA_GRUEL_SETTINGS             = Kindred.KindredAdminCommands.PrisonerGruelSettings;
    public const string BCCOM_KCA_FEED_SETTINGS_FORMAT       = ".prisoner feedsettings {0}";  // feed
    // Bloodbound item-attribute management
    public const string BCCOM_KCA_BLOODBOUND_ADD_FORMAT      = ".bloodbound add {0}";         // item descriptor (prefab/name)
    public const string BCCOM_KCA_BLOODBOUND_REMOVE_FORMAT   = ".bloodbound remove {0}";

    // =========================================================================
    // Inbound regex pipeline (Phase 3b)
    // =========================================================================

    /// <summary>
    /// What kind of inbound chat response we're currently expecting from a
    /// previously-sent command. Idle = no pending request.
    /// </summary>
    private enum InterceptFlag
    {
        Idle,
        AwaitingBoxList,
        ReceivingBoxList,
        AwaitingBoxContent,
        ReceivingBoxContent,
        AwaitingPrestigeInfo,
        ReceivingPrestigeInfo,
        AwaitingBloodInfo,
        ReceivingBloodInfo,
        // 0.8.3: generic capture for read-data commands that don't have a
        // dedicated structured parse. See PlayerStateService.LastResponse.
        AwaitingGenericResponse,
        ReceivingGenericResponse,
        // 0.10.0: structured parse for .fam s replies feeding the V-Blood
        // collection tracker. Server replies with either:
        //   "Matching familiar(s) found in: <color=white>box1</color>, <color=white>box3</color><color=#AA336A>*</color>"
        //   "VBlood familiar(s) found in: ..."  (when query == "vblood")
        //   "Couldn't find any matches..."
        //   "Couldn't find matching familiar in boxes."
        // The reply is a single line; we parse + emit + return to Idle immediately
        // so there's no need for a separate "Receiving" state.
        AwaitingFamSearch,
    }

    private static InterceptFlag _intercept = InterceptFlag.Idle;
    private static readonly List<string> _boxListBuffer = new();
    private static readonly List<PlayerStateService.FamiliarBoxEntry> _boxContentBuffer = new();
    // Per-query buffer for the prestige info display; reset on each
    // .prestige get send so a stale query never leaks into a fresh one.
    private static PlayerStateService.PrestigeInfo _prestigeInfoBuffer;
    private static PlayerStateService.BloodInfo    _bloodInfoBuffer;
    // 0.8.3: generic capture buffer + the originating command so subscribers
    // can show "last response from `.wep get`: ..." if useful.
    private static readonly List<string> _genericResponseBuffer = new();
    private static string _genericResponseCommand = "";
    // 0.10.0: the .fam s query currently armed. We need to remember it so
    // the FamSearchCompleted event tells the scanner which name resolved.
    private static string _famSearchQuery = "";

    // 0.10.2 / 0.10.6: chat-suppression decision system.
    //
    // The 0.10.2 design used a single bool (_suppressCurrentCaptureChat) set
    // by EnqueueMessageSilent before arming the intercept. 0.10.6 generalizes
    // this to a per-category model so the Settings → Chat Logging section can
    // expose user-controllable visibility per command-source group:
    //   BchAuto    — Raphael's own auto-fired traffic (V-Blood scanner, refresh
    //                tickers). EnqueueMessageSilent forces this category.
    //                Default suppressed.
    //   Bloodcraft — user-initiated Bloodcraft commands. HasBchUIDisplay=true
    //                for commands whose reply Raphael renders structurally (.bl
    //                get, .wep get, .prestige get, .fam boxes/l/s/gl). Other
    //                Bloodcraft commands (action confirmations) get
    //                HasBchUIDisplay=false and STAY VISIBLE regardless of
    //                the toggle — losing them would leave the user blind to
    //                server responses Raphael has no UI for.
    //   Kindred    — same as Bloodcraft but for KindredCommands /
    //                KindredLogistics. Today no Kindred replies are
    //                structurally parsed (HasBchUIDisplay=false for all), so
    //                the toggle is currently a no-op. Reserved for future
    //                structured parsing.
    //   Other      — unknown / unclassified. Always visible.
    //
    // Receive-side decision (in each intercept handler):
    //   destroy = (HasBchUIDisplay && !ShowChatForCategory(category))
    //          || Settings.ClearServerMessages;
    //
    // Safety vs Eclipse: this only affects plain colored chat entities that
    // Eclipse already ignores (Eclipse only consumes MAC-signed [ECLIPSE]
    // entities). Confirmed in Eclipse's ClientChatSystemPatch — its CheckMAC
    // gate rejects every line we'd ever suppress.
    internal static bool _nextCommandIsBchAuto; // set by MessageService.EnqueueMessageSilent
    private static CommandCategory _currentCaptureCategory = CommandCategory.Other;
    private static bool            _currentCaptureHasBchUI;

    /// <summary>0.10.6: returns true if the chat copy of the current intercept's
    /// reply should be destroyed based on Category + HasBchUIDisplay + the
    /// user's Chat Logging toggles. Does NOT take ClearServerMessages into
    /// account — callers OR that in separately to preserve legacy behavior.</summary>
    private static bool ShouldSuppressByCategory()
    {
        if (!_currentCaptureHasBchUI) return false; // Raphael doesn't display this — keep chat visible
        return _currentCaptureCategory switch
        {
            CommandCategory.BchAuto    => !Config.Settings.ShowChatBchAuto,
            CommandCategory.Bloodcraft => !Config.Settings.ShowChatBloodcraft,
            CommandCategory.Kindred    => !Config.Settings.ShowChatKindred,
            _ => false,
        };
    }

    // 0.9.1: action-confirmation suppress window. Independent of the intercept
    // state machine — action commands (.fam b / .fam ub / .fam t / .fam cb
    // etc.) don't yield structured data the UI needs to capture, but they
    // each produce 1-3 color-tagged confirmation lines that pile up in chat.
    // When the user opts in via SuppressFamiliarActionChatter, those lines
    // are eaten. The window arms for ~1.5s after each action send; we only
    // eat color-tagged lines while INTERCEPT is Idle, so we never clobber
    // a structured intercept that's still capturing.
    private static double _actionSuppressUntil;
    private const double ACTION_SUPPRESS_WINDOW_SECONDS = 1.5;

    private static bool IsActionSuppressActive() =>
        UnityEngine.Time.realtimeSinceStartupAsDouble < _actionSuppressUntil;

    // 0.10.10: parallel force-suppress window. Set by NoteOutboundForIntercept
    // when a familiar-action command is being sent via EnqueueMessageSilent
    // (i.e. the caller WANTS the reply hidden regardless of the user's
    // SuppressFamiliarActionChatter setting). Pre-0.10.10 the V-Blood
    // scanner's `.fam cb` confirmations leaked into chat whenever the
    // user had SuppressFamiliarActionChatter off — because the silent
    // flag only flowed into the BchAuto category for STRUCTURED intercepts,
    // not the action-confirmation suppress branch. This flag fills that
    // gap so scan-issued action confirmations are unconditionally eaten.
    private static double _actionForceSuppressUntil;

    private static bool IsActionForceSuppressActive() =>
        UnityEngine.Time.realtimeSinceStartupAsDouble < _actionForceSuppressUntil;

    /// <summary>0.9.3: pattern-match Bloodcraft's literal action-confirmation
    /// reply strings (from `LearningMods/Bloodcraft-main/Commands/FamiliarCommands.cs`
    /// and `.../Utilities/Familiars.cs`). Listed here so the suppress can fire
    /// even when a structured intercept is concurrently armed (e.g., the
    /// `.fam cb` + `.fam l` back-to-back workflow). Patterns chosen to be
    /// distinct from the structured intercept patterns so no legitimate
    /// list/info response gets eaten by accident.</summary>
    private static bool IsKnownFamiliarActionConfirmation(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        // .fam cb {name} → "Box Selected - <color=white>{name}</color>"
        if (text.StartsWith("Box Selected", System.StringComparison.Ordinal)) return true;
        // .fam ub → "<color=green>{name}</color> <color=#FFC0CB>unbound</color>!"
        if (text.Contains("unbound</color>!")) return true;
        if (text.Contains("</color> unbound!")) return true;
        // .fam mb {dest} → "<color=green>{name}</color> moved - <color=white>{dest}</color>"
        if (text.Contains("</color> moved -")) return true;
        // .fam r N → "<color=green>{name}</color> removed from <color=white>{box}</color>."
        if (text.Contains("</color> removed from ")) return true;
        // .fam t → "<color=yellow>Familiar</color> <color=green>enabled</color>!"
        //       or "<color=yellow>Familiar</color> <color=red>disabled</color>!"
        if (text.Contains("Familiar</color> <color=") &&
            (text.Contains("enabled</color>!") || text.Contains("disabled</color>!"))) return true;
        // .fam b (bind) async confirmation — Bloodcraft's bind flow prints
        // a generic "is now bound!" once the familiar entity instantiates.
        // Pattern is loose because Bloodcraft has multiple bind paths.
        if (text.Contains("now bound!")) return true;
        if (text.Contains("now active!")) return true;
        // 0.10.8: Bloodcraft's .fam ub negative-path replies. Action-chat
        // suppression now covers both the success message ("...unbound!")
        // and the failure messages users see when they try to unbind with
        // no active familiar. The V-Bloods Summon flow used to leak this
        // line into chat even with chat-suppression on, because the
        // upstream "always send unbind" call was reaching Bloodcraft when
        // no familiar was bound. 0.10.8 also stops sending unbind in that
        // case, but keeping the pattern recognized here is defense in
        // depth for any other code path that might unbind speculatively.
        if (text.Contains("Couldn't find familiar to unbind")) return true;
        if (text.Contains("Couldn't find familiar actives")) return true;
        // .fam reset hint that Bloodcraft appends to the active/unbind
        // error path. Suppress it as part of the same action-chat window
        // — it's the second sentence of the unbind-failure message.
        if (text.Contains("Active familiar doesn't exist")) return true;
        return false;
    }

    /// <summary>True for commands whose chat confirmation the user can opt to
    /// hide (Settings.SuppressFamiliarActionChatter). Strictly the .fam action
    /// commands — bind, unbind, recall/dismiss, switch box, move box,
    /// smartbind, permanent remove. List queries (.fam boxes / .fam l) are
    /// NOT here because those have structured intercepts already.</summary>
    private static bool IsFamiliarActionCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return false;
        return command.StartsWith(".fam b ",   System.StringComparison.Ordinal) // bind by index
            || command.Equals(".fam ub",        System.StringComparison.Ordinal) // unbind
            || command.Equals(".fam t",         System.StringComparison.Ordinal) // toggle
            || command.StartsWith(".fam cb ",  System.StringComparison.Ordinal) // switch box
            || command.StartsWith(".fam mb ",  System.StringComparison.Ordinal) // move to box
            || command.StartsWith(".fam sb ",  System.StringComparison.Ordinal) // smartbind
            || command.StartsWith(".fam r ",   System.StringComparison.Ordinal); // permanent remove
    }
    // load-bearing: tracks last time a "useful" line for the current intercept
    // arrived. Per-frame TickInterceptTimeouts() flushes the buffered list when
    // this gets too stale - covers the case where Bloodcraft sends multiple
    // batches of box/familiar lines and then no further chat noise to act as
    // a terminator. Older code waited for "first non-color line"; that triggered
    // both too late (UI hangs forever if no other system message ever arrives)
    // and too early (any unrelated system announcement landing mid-list flushed
    // empty/partial state).
    private static double _interceptLastLineTime;
    private const double INTERCEPT_FLUSH_AFTER_SECONDS = 0.6;

    // 0.18.3: HARD, non-sliding cap on the STRUCTURED Receiving states (BoxList / BoxContent /
    // PrestigeInfo / BloodInfo). Bug (Moonie): other mods' system messages disappear on load-in until a
    // BC command is run. Cause — `.prestige get` / `.fam boxes` auto-fire at login and open a Receiving
    // state; ReceivingPrestigeInfo treats EVERY following line as an "effect" (and ReceivingBoxList eats
    // color-tagged lines), and each capture SLID _interceptLastLineTime — so a steady trickle of
    // unrelated colored system lines kept the state open and got eaten forever (exactly the trap the
    // 0.18 generic-catch-all fix solved, but the structured states were never bounded). This hard cap
    // force-flushes the Receiving state a fixed time after it STARTED, regardless of incoming lines, so
    // it can never swallow chat beyond the real reply burst. Anchored lazily in TickInterceptTimeouts.
    private static double _receivingArmTime;
    private const double RECEIVING_HARD_WINDOW_SECONDS = 1.5;

    // 0.18: HARD bound on the GENERIC catch-all capture (AwaitingGenericResponse /
    // ReceivingGenericResponse). Unlike the structured intercepts (which only match a
    // specific Bloodcraft regex), the generic capture grabs ANY colored line as "the
    // reply" — so it can't tell Raphael's reply from an unrelated mod's colored system
    // message. The 0.6s flush ABOVE is SLIDING (each captured line pushed it out), which
    // let a steady trickle of OTHER mods' system messages (KindredPonds, XP Rising, …)
    // latch the capture open indefinitely and get eaten — the "system messages vanish
    // until I run a BC command" bug. These cap the capture to a fixed window from when
    // the command was SENT + a small line count, so it always self-heals and can never
    // swallow a sustained stream of unrelated chat. A real reply arrives as a tight burst
    // right after the send, well inside this window.
    private static double _genericArmTime;
    private const double GENERIC_CAPTURE_WINDOW_SECONDS = 1.5;
    private const int    GENERIC_CAPTURE_MAX_LINES = 8;

    private const string BOX_LIST_HEADER          = "Familiar Boxes";
    private const string BOX_SELECTED_HEADER      = "Box Selected";
    private const string BOX_NAME_REGEX           = @"<color=[^>]+>(?<box>[^<]+)</color>";

    // Prestige info reply parsing. Bloodcraft sends 4-5 lines starting with a
    // "<TYPE> Prestige Info:" header. Keep regexes loose: capture what we can,
    // fall through to "raw text minus color tags" for everything else so the
    // user can read it in the UI even when the format drifts between versions.
    private static readonly Regex _prestigeHeaderRegex = new(
        @"<color=#90EE90>(?<type>[^<]+)</color>\s+Prestige Info:",
        RegexOptions.Compiled);
    private static readonly Regex _prestigeLevelRegex = new(
        @"Current Prestige Level:\s*<color=yellow>(?<level>\d+)</color>/(?<max>\d+)",
        RegexOptions.Compiled);
    // 0.16: Exo prestige replies with a DIFFERENT one-line format that has NO
    // "<Type> Prestige Info:" header — the type, level and cap are all on the
    // level line itself:
    //   "Current <color=#90EE90>Exo</color> Prestige Level: <color=yellow>{lvl}</color>/{max} | Max Form Duration: <color=green>{s}</color>s"
    // The generic header/level regexes above never match it, which is why exo
    // tracking silently did nothing before 0.16 (the state machine waited for a
    // "Prestige Info:" header that never arrived). See Bloodcraft
    // PrestigeCommands.GetPrestigeCommand (the Exo special-case branch).
    private static readonly Regex _prestigeExoLineRegex = new(
        @"Current\s+<color=#90EE90>(?<type>Exo)</color>\s+Prestige Level:\s*<color=yellow>(?<level>\d+)</color>/(?<max>\d+)",
        RegexOptions.Compiled);
    // "You have not prestiged in <color=#90EE90>Exo</color> yet."
    private static readonly Regex _prestigeExoNoneRegex = new(
        @"not prestiged in\s+<color=#90EE90>Exo</color>",
        RegexOptions.Compiled);
    // Strip any TMPro color/size/bold markup so the effect lines render cleanly
    // in the UI without inline tags.
    private static readonly Regex _stripTmpTagsRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    // Blood-info reply parsing. Bloodcraft sends 1 main info line + N stat lines:
    //   "You're level [<color=white>{lvl}</color>][<color=#90EE90>{prestige}</color>]
    //    with <color=yellow>{essence}</color> <color=#FFC0CB>essence</color>
    //    (<color=white>{pct}%</color>) in <color=red>{type}</color>!"
    //   "<color=red>{type}</color> Stats: <color=#00FFFF>{stat}</color>: <color=white>{val}</color>, ..."
    private static readonly Regex _bloodHeaderRegex = new(
        @"You're level \[<color=white>(?<level>\d+)</color>\]\[<color=#90EE90>(?<prestige>\d+)</color>\] with <color=yellow>(?<essence>[^<]+)</color> <color=#FFC0CB>essence</color> \(<color=white>(?<pct>[^<%]+)%?</color>\) in <color=red>(?<type>[^<]+)</color>",
        RegexOptions.Compiled);
    private static readonly Regex _bloodStatLineRegex = new(
        @"^<color=red>[^<]+</color>\s+Stats:",
        RegexOptions.Compiled);
    // Bloodcraft v1.13.x .fam l per-familiar line. Full format from FamiliarCommands.cs:
    //   <color=yellow>{idx}</color>| <color=green>{name}</color>[<color=#XYZ>*</color>] [<color=white>{level}</color>][<color=#90EE90>{prestige}</color>]
    // The shiny marker (<color=#XYZ>*</color>) and the prestige bracket are optional.
    // Capturing all of them so the UI can show level / prestige / shiny next to the name.
    private const string BOX_CONTENT_ENTRY_REGEX  =
        @"<color=yellow>(?<idx>\d+)</color>\|\s*" +
        @"<color=(?<color>[^>]+)>(?<name>[^<]+)</color>" +
        @"(?:<color=(?<shiny>[^>]+)>\*</color>)?" +
        @"\s*\[<color=[^>]+>(?<level>\d+)</color>\]" +
        @"(?:\[<color=[^>]+>(?<prestige>\d+)</color>\])?";

    private static readonly Regex _boxNameRegex         = new(BOX_NAME_REGEX,          RegexOptions.Compiled);
    private static readonly Regex _boxContentEntryRegex = new(BOX_CONTENT_ENTRY_REGEX, RegexOptions.Compiled);

    // 0.10.5: per-command plain-text leading-header recognition. Some
    // Bloodcraft commands reply with a first line that starts in plain
    // English even though the rest of the line contains <color=...> tags
    // for inline emphasis. The generic-capture .StartsWith("<color") filter
    // misses these, so before 0.10.5 the silent-mode suppression let those
    // first lines leak into chat (most-info-bearing line of every silent
    // refresh — exactly what the user noticed for repeated .wep get).
    //
    // Each entry is a list of safe prefixes to match against. Pattern is
    // intentionally string.StartsWith rather than regex so the check stays
    // O(prefix-length) per line. Keep entries narrow enough that an
    // unrelated server announcement wouldn't accidentally match — anything
    // less specific would risk eating legitimate other system chat that
    // happens to fall inside our 0.6s intercept window.
    //
    // To add a new command's plain-leading reply: confirm the EXACT prefix
    // from Bloodcraft's Commands/*.cs and add a line here. Avoid duplicates
    // with any structured intercept (.fam boxes, .bl get, etc. — those
    // route through their own intercept state, not AwaitingGenericResponse).
    private static readonly Dictionary<string, string[]> _genericReplyPlainHeaders =
        new(System.StringComparer.Ordinal)
        {
            // Bloodcraft v1.13.x WeaponCommands.cs:62 / 83 / 88
            [".wep get"] = new[]
            {
                "Your weapon expertise is",
                "No bonuses from currently equipped",
                "You haven't gained any expertise for",
            },
            // Future commands with similar patterns can be added here.
        };

    private static bool LooksLikePlainReplyHeaderForCommand(string text, string command)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(command)) return false;
        if (!_genericReplyPlainHeaders.TryGetValue(command, out var prefixes)) return false;
        foreach (var p in prefixes)
            if (text.StartsWith(p, System.StringComparison.Ordinal)) return true;
        return false;
    }

    // 0.10.0: .fam s reply parsing for the V-Blood collection scanner.
    // Server formats (Bloodcraft v1.13.x, Commands/FamiliarCommands.cs:1085 / 1049 / 1054 / 1090):
    //   "Matching familiar(s) found in: <color=white>boxN</color>[<color=#AA336A>*</color>], ..."
    //   "VBlood familiar(s) found in: ..."           (when query == "vblood")
    //   "Couldn't find any matches..."               (regular query, nothing found)
    //   "Couldn't find matching familiar in boxes." (vblood query, nothing found)
    // The list portion is comma-separated tokens; each token is a colored box-name
    // followed by an OPTIONAL pink-star shiny marker. The shiny marker means
    // "some familiar in that box matching the query is shiny" — we surface it
    // as a per-box bool in the parsed result.
    private static readonly Regex _famSearchSuccessRegex = new(
        @"^(?:Matching|VBlood) familiar\(s\) found in:\s*(?<list>.+)$",
        RegexOptions.Compiled);
    // 0.10.4: added the "no unlocks yet" path. Bloodcraft replies with
    // "You don't have any unlocked familiars yet." (FamiliarCommands.cs:1096)
    // when the player has zero captured familiars at all. Pre-0.10.4 the
    // scanner timed out 0.6s per search waiting for a match that would never
    // come — 130 names × ~0.6s = ~80s of wasted timeouts on a fresh
    // character. Catching this pattern lets the scanner finish each search
    // immediately and move on.
    private static readonly Regex _famSearchNoMatchRegex = new(
        @"^(Couldn't find (any matches|matching familiar)|You don't have any unlocked familiars)",
        RegexOptions.Compiled);
    private static readonly Regex _famSearchBoxTokenRegex = new(
        @"<color=white>(?<name>[^<]+)</color>(?<shiny><color=[^>]+>\*</color>)?",
        RegexOptions.Compiled);
    // 0.10.7: VCF prints a command's `usage:` template when an arg-required
    // command is dispatched with empty/malformed args. Pre-0.10.7 the
    // scanner sent unquoted ".fam s Alpha the White Wolf"; VCF only
    // consumed "Alpha" as the (positional) name, hit the next-arg
    // boundary, and printed Bloodcraft's `usage: .fam s [Name]` template
    // literally. Even with the 0.10.7 quoting fix this can still fire if
    // a user manually clears the textbox and submits, or types something
    // with a stray quote — so we explicitly recognize the echo here and
    // treat it as no-match (advances the queue, suppresses the line).
    private static readonly Regex _famSearchUsageEchoRegex = new(
        @"\.fam s \[Name\]|\.familiar search.*\.fam s \[Name\]",
        RegexOptions.Compiled);

    /// <summary>
    /// Called from EnqueueMessage / SendRaw when our UI dispatches a command we
    /// know triggers a parseable reply. Sets the intercept flag so the next
    /// matching inbound chat lines get routed to PlayerStateService.
    /// </summary>
    internal static void NoteOutboundForIntercept(string command)
    {
        if (string.IsNullOrEmpty(command)) return;

        // 0.9.1: arm the action-suppress window in parallel with any
        // structured intercept arming below. Independent flag — see
        // _actionSuppressUntil + IsActionSuppressActive.
        if (IsFamiliarActionCommand(command))
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            _actionSuppressUntil = now + ACTION_SUPPRESS_WINDOW_SECONDS;
            // 0.10.10: silent-enqueue path (V-Blood scanner, overlay
            // auto-fires) wants the reply hidden whether the user has
            // SuppressFamiliarActionChatter on or off. _nextCommandIsBchAuto
            // is the flag MessageService.EnqueueMessageSilent sets just
            // before calling us; arm a parallel force-suppress window so
            // HandleInboundChat eats the reply unconditionally for as long
            // as a normal action-suppress window lasts.
            if (_nextCommandIsBchAuto)
            {
                _actionForceSuppressUntil = now + ACTION_SUPPRESS_WINDOW_SECONDS;
            }
        }

        if (command.Equals(BCCOM_FAM_BOXES, System.StringComparison.Ordinal))
        {
            _intercept = InterceptFlag.AwaitingBoxList;
            _boxListBuffer.Clear();
            // 0.10.10: classify so the BoxList receive handler can ask
            // ShouldSuppressByCategory — needed for the V-Blood scanner's
            // initial `.fam boxes` to stay silent in chat.
            ClassifyAndStoreCategory(command);
            _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            LogUtils.LogInfo($"Intercept armed: AwaitingBoxList ({_currentCaptureCategory})");
        }
        else if (command.Equals(BCCOM_FAM_LIST_CURRENT_BOX, System.StringComparison.Ordinal))
        {
            _intercept = InterceptFlag.AwaitingBoxContent;
            _boxContentBuffer.Clear();
            // 0.10.10: classify so the per-entry `.fam l` rows can be
            // suppressed when the scanner fires them silently.
            ClassifyAndStoreCategory(command);
            _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            LogUtils.LogInfo($"Intercept armed: AwaitingBoxContent ({_currentCaptureCategory})");
        }
        else if (command.StartsWith(".prestige get ", System.StringComparison.Ordinal))
        {
            _intercept = InterceptFlag.AwaitingPrestigeInfo;
            _prestigeInfoBuffer = new PlayerStateService.PrestigeInfo
            {
                EffectLines = new System.Collections.Generic.List<string>(),
            };
            _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            LogUtils.LogInfo("Intercept armed: AwaitingPrestigeInfo");
        }
        // Intercept .bl get with an explicit type (e.g. ".bl get Warrior").
        // Skip the no-arg ".bl get" form because the live Eclipse stream
        // already keeps PlayerStateService.Legacy current for the equipped
        // blood — intercepting that would clobber the structured display
        // with a partial parse on every refresh button press.
        else if (command.Length > ".bl get ".Length
              && command.StartsWith(".bl get ", System.StringComparison.Ordinal))
        {
            _intercept = InterceptFlag.AwaitingBloodInfo;
            _bloodInfoBuffer = new PlayerStateService.BloodInfo
            {
                StatLines = new System.Collections.Generic.List<string>(),
            };
            ClassifyAndStoreCategory(command);
            _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            LogUtils.LogInfo($"Intercept armed: AwaitingBloodInfo ({_currentCaptureCategory}, hasUI={_currentCaptureHasBchUI})");
        }
        // 0.10.0: .fam s / .fam search → V-Blood scanner reply. Single-line
        // response so we parse + emit + return to Idle inside HandleInboundChat.
        // Manual user searches via the UI form pass through the same path; the
        // FamSearchCompleted event is fired regardless of caller. We strip
        // off the "/.fam s " prefix so the event payload carries just the
        // queried name (e.g. "Alpha the White Wolf" or "Primal Alpha the White Wolf").
        else if (command.StartsWith(".fam s ", System.StringComparison.Ordinal)
              || command.StartsWith(".fam search ", System.StringComparison.Ordinal))
        {
            _intercept = InterceptFlag.AwaitingFamSearch;
            // 0.10.7: BCCOM_FAM_SEARCH_FORMAT now wraps the query in quotes
            // (`.fam s "Alpha the White Wolf"`) so VCF parses multi-word
            // names. Strip the surrounding quotes when capturing the query
            // so scanner correlation (string.Equals with the unquoted form)
            // still matches.
            var rawArg = command.StartsWith(".fam search ", System.StringComparison.Ordinal)
                ? command.Substring(".fam search ".Length).Trim()
                : command.Substring(".fam s ".Length).Trim();
            if (rawArg.Length >= 2 && rawArg[0] == '"' && rawArg[rawArg.Length - 1] == '"')
                rawArg = rawArg.Substring(1, rawArg.Length - 2);
            _famSearchQuery = rawArg;
            // 0.10.4: honor the silent-enqueue flag so scanner-fired searches
            // don't dump 130 "Matching familiar(s)..." or "Couldn't find..."
            // lines into the player's chat box during a full V-Blood scan.
            // Manual user searches via the UI form continue to use the
            // regular EnqueueMessage path and stay visible in chat.
            ClassifyAndStoreCategory(command);
            _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            LogUtils.LogInfo($"Intercept armed: AwaitingFamSearch ('{_famSearchQuery}', {_currentCaptureCategory})");
        }
        // Fallback: arm the generic capture for known read-data commands so
        // their replies land in the UI's "Last server response" sections
        // instead of only the chat box. Added in 0.8.3 in response to friend-
        // testing feedback ("it would load into the chat window rather than
        // loading into the UI informational box"). Specific intercepts above
        // (.fam boxes / .fam l / .prestige get / .bl get) still take priority.
        else if (ShouldArmGenericCapture(command))
        {
            _intercept = InterceptFlag.AwaitingGenericResponse;
            _genericResponseBuffer.Clear();
            _genericResponseCommand = command;
            ClassifyAndStoreCategory(command);
            _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            _genericArmTime = _interceptLastLineTime; // hard window anchor (see GENERIC_CAPTURE_WINDOW_SECONDS)
            LogUtils.LogInfo($"Intercept armed: AwaitingGenericResponse ('{command}', {_currentCaptureCategory}, hasUI={_currentCaptureHasBchUI})");
        }
    }

    /// <summary>True for chat commands whose reply users expect to see in the
    /// UI panel rather than only in chat. Drives the generic capture fallback
    /// added in 0.8.3. Specific structured intercepts (.fam boxes / .fam l /
    /// .prestige get / .bl get) are handled separately above and shouldn't
    /// reach this list.</summary>
    private static bool ShouldArmGenericCapture(string command)
    {
        if (string.IsNullOrEmpty(command)) return false;

        // Read-data commands — explicit prefix list keeps this conservative.
        // Side-effect commands (.fam b N, .lvl set X Y, .giveset, etc.) should
        // NOT arm the capture because their server reply is a transient
        // confirmation, not info the user wants to study in a panel.
        return command.StartsWith(".fam pr",         System.StringComparison.Ordinal)
            || command.StartsWith(".fam actions",    System.StringComparison.Ordinal)
            // 0.14.0: .fam bgs / .fam bg branches removed — Bloodcraft never
            // implemented battle groups, so these never produce server replies
            // and the capture would only ever time out.
            // 0.10.12: .fam sb (smart-bind) reply varies — single match
            // produces a bind confirmation, multiple matches produce a
            // clarification list, no match produces "couldn't find...".
            // Capturing here surfaces the list/error in the global
            // LastResponse panel when chat suppression is on.
            || command.StartsWith(".fam sb ",        System.StringComparison.Ordinal)
            // 0.10.12: .class l / .class s / .class csp configuration
            // queries and the .class info queries already covered by the
            // ".class l" prefix below. Adding .class lst explicitly
            // (handled by ".class l" prefix) and .class lsp (also
            // covered). Leaving as-is — the prefix match is correct.
            // 0.10.12: .lvl log / .quest log / .prof log / .misc
            // remindme — TOGGLES that return the new state. User wants
            // to see the new state in the UI.
            || command.Equals(".lvl log",            System.StringComparison.Ordinal)
            || command.Equals(".quest log",          System.StringComparison.Ordinal)
            || command.Equals(".prof log",           System.StringComparison.Ordinal)
            || command.Equals(".misc silence",       System.StringComparison.Ordinal)
            || command.StartsWith(".misc sct ",      System.StringComparison.Ordinal)
            || command.StartsWith(".prestige l",     System.StringComparison.Ordinal)
            || command.StartsWith(".prestige lb ",   System.StringComparison.Ordinal)
            || command.StartsWith(".bl l",           System.StringComparison.Ordinal) // .bl l + .bl lst
            || command.StartsWith(".wep get",        System.StringComparison.Ordinal)
            || command.StartsWith(".wep l",          System.StringComparison.Ordinal) // .wep l + .wep lst
            || command.StartsWith(".lvl get",        System.StringComparison.Ordinal)
            || command.StartsWith(".class l",        System.StringComparison.Ordinal) // .class l + .class lsp + .class lst
            || command.StartsWith(".prof l",         System.StringComparison.Ordinal)
            || command.StartsWith(".prof get",       System.StringComparison.Ordinal)
            || command.StartsWith(".misc userstats", System.StringComparison.Ordinal)
            || command.StartsWith(".misc health",    System.StringComparison.Ordinal)
            || command.StartsWith(".misc remindme",  System.StringComparison.Ordinal)
            || command.StartsWith(".quest p ",       System.StringComparison.Ordinal)
            || command.StartsWith(".quest t ",       System.StringComparison.Ordinal)
            || command.StartsWith(".checklevel",     System.StringComparison.Ordinal)
            || command.StartsWith(".clan list",      System.StringComparison.Ordinal)
            || command.StartsWith(".clan members",   System.StringComparison.Ordinal)
            || command.StartsWith(".boss list",      System.StringComparison.Ordinal)
            || command.StartsWith(".region list",    System.StringComparison.Ordinal)
            || command.StartsWith(".openplots",      System.StringComparison.Ordinal)
            || command.StartsWith(".staff",          System.StringComparison.Ordinal)
            || command.StartsWith(".time",           System.StringComparison.Ordinal)
            || command.StartsWith(".gear soulshardstatus", System.StringComparison.Ordinal)
            || command.StartsWith(".fc ",            System.StringComparison.Ordinal)
            || command.StartsWith(".search item ",   System.StringComparison.Ordinal)
            || command.StartsWith(".search npc ",    System.StringComparison.Ordinal)
            // 0.20: Beelzebub plain-text READS whose reply Raphael parses (broadcast-msg list, tform abilities).
            // Their config/state isn't on the structured [BEELZ:*] wire, so the human-text reply is the read.
            || IsBeelzMultilineReadCommand(command);
    }

    /// <summary>True for the Beelzebub `.beelz admin broadcast-msg &lt;pool&gt; list` read command, whose
    /// reply is a plain numbered list Raphael's announcements editor parses (Beelz v0.100).</summary>
    internal static bool IsBeelzBroadcastListCommand(string command)
        => !string.IsNullOrEmpty(command)
           && command.StartsWith(".beelz admin broadcast-msg ", System.StringComparison.Ordinal)
           && command.EndsWith(" list", System.StringComparison.Ordinal);

    /// <summary>True for the Beelzebub `.beelz tform &lt;unit&gt; abilities` read command, whose reply is a
    /// plain numbered ability kit ("  [i] Name (id N)") Raphael's transform-loadout editor parses (Beelz v0.100).</summary>
    internal static bool IsBeelzTformAbilitiesCommand(string command)
        => !string.IsNullOrEmpty(command)
           && command.StartsWith(".beelz tform ", System.StringComparison.Ordinal)
           && command.EndsWith(" abilities", System.StringComparison.Ordinal);

    /// <summary>Beelzebub plain-text reads whose multi-line reply Raphael captures + parses. These get a larger
    /// capture window / line cap than the default generic capture (a kit/list can exceed 8 lines).</summary>
    internal static bool IsBeelzMultilineReadCommand(string command)
        => IsBeelzBroadcastListCommand(command) || IsBeelzTformAbilitiesCommand(command);

    /// <summary>
    /// True if <paramref name="text"/> is a VampireCommandFramework (VCF) system reply — an
    /// "[error]" / "[denied]" / "parameter conversion errors" line, or a continuation line of a
    /// multi-line VCF error ("  - .cmd (Assembly): reason"). VCF colour-wraps the tokens
    /// (e.g. <c>&lt;color=...&gt;[error]&lt;/color&gt;</c>) but the literal markers survive, so a
    /// substring test is enough. These appear when a command Raphael issues can't run on this server;
    /// they are never Raphael's own structured data (Beelzebub = [BEELZ:*]; Bloodcraft = colour-tagged
    /// human text). Gated by Settings.SuppressCommandFrameworkErrors at the call site.
    /// </summary>
    private static bool IsCommandFrameworkNoise(string text)
    {
        const System.StringComparison OIC = System.StringComparison.OrdinalIgnoreCase;
        if (text.IndexOf("[error]", OIC) >= 0) return true;
        if (text.IndexOf("[denied]", OIC) >= 0) return true;
        if (text.IndexOf("parameter conversion error", OIC) >= 0) return true;
        // B1 (0.19): "command not found" replies. During handshake/probing Raphael fires Bloodcraft/Beelzebub
        // commands (.fam boxes, .prestige get, .beelz …) that a server lacking that mod can't resolve;
        // the framework answers with a "command not found"/"could not be found"/"unknown command" SYSTEM
        // line that floods chat on every probe. These are never Raphael's structured data, so eating them is
        // safe (and the whole filter is gated by SuppressCommandFrameworkErrors so a user debugging their
        // OWN typed command can still see it).
        if (text.IndexOf("command not found", OIC) >= 0) return true;
        if (text.IndexOf("could not be found", OIC) >= 0) return true;
        if (text.IndexOf("unknown command", OIC) >= 0) return true;
        // Continuation line of a paginated multi-command VCF error: "  - .cmd (Assembly): reason".
        string trimmed = text.TrimStart();
        if (trimmed.StartsWith("- .", System.StringComparison.Ordinal) && trimmed.Contains("): ")) return true;
        return false;
    }

    /// <summary>
    /// 0.67.2: hide a VCF error line only when it's almost certainly caused by Raphael's own background traffic:
    /// setting on, a background (silent) command went out recently, and the player sent nothing recently. Errors
    /// for commands the player sent — buttons, Raphael chat, vanilla chat — always show (e.g. `.nyar help`, a typo,
    /// an admin-only command while not admin). Fails open when the vanilla send hook isn't installed, since
    /// vanilla-typed commands then can't be told apart.
    /// </summary>
    private static bool ShouldHideCommandFrameworkNoise(string text = null)
    {
        if (!Config.Settings.SuppressCommandFrameworkErrors) return false;
        if (!OutboundLedger.VcfSuppressionAllowed) return false;
        // 0.71.0: the error quotes one of Raphael's own silent probes (".beelz api version" on a server without
        // Beelzebub): hide it even if the player sent something else in the same few seconds.
        if (text != null && OutboundLedger.NamesRecentBackgroundCommand(text, 15f)) return true;
        if (OutboundLedger.UserSentRecently(OutboundLedger.VcfWindowSeconds)) return false;
        return OutboundLedger.BackgroundSentRecently(OutboundLedger.VcfWindowSeconds);
    }

    /// <summary>
    /// Process an inbound chat-server message text. Returns true if the message
    /// was consumed by the pipeline and the caller should destroy the entity
    /// (so it doesn't appear in the player's chat window).
    /// </summary>
    public static bool HandleInboundChat(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // 0.73.0: the Bloodcraft progress-reset tap goes first, ahead of VCF-noise suppression (which would eat its
        // "[denied]" pause signal) and every intercept, so no other capture can swallow a reset reply. It returns true
        // only for a line it positively correlated to one of its steps.
        if (Bloodcraft.BcResetRunner.TryHandleInbound(text)) return true;

        // 0.18: VampireCommandFramework (VCF) noise suppression. When Raphael sends a command the
        // server's command framework can't run — a Bloodcraft command on a non-Bloodcraft server,
        // an admin-only command from a non-admin (admin buttons are always visible), or a malformed
        // arg — VCF replies with "[error]" / "[denied]" / "parameter conversion errors" SYSTEM lines.
        // Those are never Raphael's structured data (Beelzebub uses [BEELZ:*]; Bloodcraft replies are
        // color-tagged human text handled by the intercepts below), so eating them is always safe.
        // Checked first + gated by the setting so a user debugging their OWN typed command can see it.
        if (IsCommandFrameworkNoise(text) && ShouldHideCommandFrameworkNoise(text))
            return true;

        // 0.9.1 / 0.9.2 / 0.9.3: action-confirmation suppress.
        //
        // 0.9.3 fix: the "intercept must be Idle" guard was too strict and
        // caused the suppress to silently fail in the most common workflow.
        // Clicking a box in the picker enqueues TWO commands back to back:
        //     .fam cb {name}  → arms _actionSuppressUntil (no structured intercept)
        //     .fam l           → arms InterceptFlag.AwaitingBoxContent
        // When Bloodcraft replies "Box Selected - <color=white>{name}</color>!",
        // the intercept state is already AwaitingBoxContent, so the old
        // Idle-guarded suppress check skipped and the chatter passed through.
        //
        // New approach: match Bloodcraft's exact action-confirmation patterns
        // (Box Selected, unbound!, moved -, removed from, Familiar enabled/
        // disabled). These are distinct enough from the structured intercept
        // patterns (box-content-entry regex requires <color=yellow>\d+</color>|
        // prefix; box-list header is the literal "Familiar Boxes" string; etc.)
        // that we can fire the suppress even when an intercept is armed
        // without risk of clobbering a legitimate structured response.
        // 0.10.10: suppress when the user has opted-in via the setting OR
        // when the action was force-flagged by a silent-enqueue (V-Blood
        // scanner, overlay auto-fires, etc.). Without the force branch the
        // scanner's `.fam cb`/`.fam l` confirmations leaked into chat for
        // any user who hadn't turned SuppressFamiliarActionChatter on.
        if (IsActionSuppressActive()
            && (Config.Settings.SuppressFamiliarActionChatter || IsActionForceSuppressActive())
            && IsKnownFamiliarActionConfirmation(text))
        {
            return true;
        }

        if (_intercept == InterceptFlag.Idle) return false;

        try
        {
            switch (_intercept)
            {
                case InterceptFlag.AwaitingBoxList:
                    if (text.StartsWith(BOX_LIST_HEADER, System.StringComparison.Ordinal))
                    {
                        _intercept = InterceptFlag.ReceivingBoxList;
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages; // consume header line if hiding chat
                    }
                    return false;

                case InterceptFlag.ReceivingBoxList:
                    if (text.StartsWith("<color", System.StringComparison.Ordinal))
                    {
                        foreach (Match m in _boxNameRegex.Matches(text))
                        {
                            var name = m.Groups["box"].Value;
                            if (!string.IsNullOrEmpty(name) && !_boxListBuffer.Contains(name))
                                _boxListBuffer.Add(name);
                        }
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        // 0.10.10: scanner-initiated `.fam boxes` is classified
                        // BchAuto and ShouldSuppressByCategory eats it.
                        return ShouldSuppressByCategory() || Config.Settings.ClearServerMessages;
                    }
                    // Non-color line in the middle: ignore (don't flush yet).
                    // Some other system announcement arriving between batches
                    // would otherwise truncate our list. The timeout in
                    // TickInterceptTimeouts() handles end-of-list.
                    return false;

                case InterceptFlag.AwaitingPrestigeInfo:
                {
                    var headerMatch = _prestigeHeaderRegex.Match(text);
                    if (headerMatch.Success)
                    {
                        _prestigeInfoBuffer.TypeName = headerMatch.Groups["type"].Value;
                        _intercept = InterceptFlag.ReceivingPrestigeInfo;
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages;
                    }

                    // 0.16: Exo's reply has no header — its level line carries the
                    // type + level + cap together. Match it directly so exo
                    // tracking populates (overlay EXO line + Prestige-tab EXO label).
                    var exoMatch = _prestigeExoLineRegex.Match(text);
                    if (exoMatch.Success)
                    {
                        _prestigeInfoBuffer.TypeName = exoMatch.Groups["type"].Value; // "Exo"
                        _prestigeInfoBuffer.Level    = PlayerStateService.ParseInt(exoMatch.Groups["level"].Value);
                        _prestigeInfoBuffer.MaxLevel = PlayerStateService.ParseInt(exoMatch.Groups["max"].Value);
                        // Move to Receiving so the follow-up "Max Form Duration /
                        // charge" line is captured as an effect line.
                        _intercept = InterceptFlag.ReceivingPrestigeInfo;
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages;
                    }

                    // 0.16: "You have not prestiged in Exo yet." — record an
                    // explicit empty state (level 0) so the EXO surfaces show a
                    // clear "not yet" instead of staying blank forever.
                    if (_prestigeExoNoneRegex.IsMatch(text))
                    {
                        _prestigeInfoBuffer.TypeName = "Exo";
                        _prestigeInfoBuffer.Level    = 0;
                        _intercept = InterceptFlag.ReceivingPrestigeInfo;
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages;
                    }
                    return false;
                }

                case InterceptFlag.ReceivingPrestigeInfo:
                {
                    var levelMatch = _prestigeLevelRegex.Match(text);
                    if (levelMatch.Success)
                    {
                        _prestigeInfoBuffer.Level    = PlayerStateService.ParseInt(levelMatch.Groups["level"].Value);
                        _prestigeInfoBuffer.MaxLevel = PlayerStateService.ParseInt(levelMatch.Groups["max"].Value);
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages;
                    }
                    // Any subsequent line until timeout is treated as an "effect"
                    // line (growth-rate change, stat bonus improvement, etc.).
                    // Strip color/markup so the UI label renders cleanly.
                    var clean = _stripTmpTagsRegex.Replace(text, "").Trim();
                    if (!string.IsNullOrEmpty(clean))
                    {
                        // 0.18.3 diag: surface what this greedy state captures, so a swallowed
                        // unrelated system message (e.g. wanted/heat) is visible in the log. The hard
                        // cap in TickInterceptTimeouts bounds how long this can keep capturing.
                        LogUtils.LogDiagnostic($"[Intercept] ReceivingPrestigeInfo captured: {clean}");
                        _prestigeInfoBuffer.EffectLines.Add(clean);
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages;
                    }
                    return false;
                }

                case InterceptFlag.AwaitingBloodInfo:
                {
                    var hm = _bloodHeaderRegex.Match(text);
                    if (hm.Success)
                    {
                        _bloodInfoBuffer.BloodType   = hm.Groups["type"].Value;
                        _bloodInfoBuffer.Level       = PlayerStateService.ParseInt(hm.Groups["level"].Value);
                        _bloodInfoBuffer.Prestige    = PlayerStateService.ParseInt(hm.Groups["prestige"].Value);
                        _bloodInfoBuffer.Essence     = hm.Groups["essence"].Value;
                        _bloodInfoBuffer.ProgressPct = hm.Groups["pct"].Value;
                        _intercept = InterceptFlag.ReceivingBloodInfo;
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        // 0.10.2: silent path destroys chat copy regardless of
                        // the global ClearServerMessages setting.
                        return ShouldSuppressByCategory() || Config.Settings.ClearServerMessages;
                    }
                    return false;
                }

                case InterceptFlag.ReceivingBloodInfo:
                {
                    if (_bloodStatLineRegex.IsMatch(text))
                    {
                        var clean = _stripTmpTagsRegex.Replace(text, "").Trim();
                        if (!string.IsNullOrEmpty(clean))
                            _bloodInfoBuffer.StatLines.Add(clean);
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return ShouldSuppressByCategory() || Config.Settings.ClearServerMessages;
                    }
                    return false;
                }

                case InterceptFlag.AwaitingGenericResponse:
                case InterceptFlag.ReceivingGenericResponse:
                {
                    // 0.18 HARD WINDOW: the generic capture only owns the tight burst right
                    // after the command was sent. Once that window (or the line cap) is
                    // exceeded, flush what we have and STOP — return false so the line is
                    // left in chat. This is the fix for "other mods' system messages vanish
                    // until I run a BC command": previously each captured line slid the 0.6s
                    // timer, so a steady stream of unrelated colored system lines kept this
                    // capture open forever and got eaten. Now it can never swallow more than
                    // GENERIC_CAPTURE_MAX_LINES within GENERIC_CAPTURE_WINDOW_SECONDS of the send.
                    double nowGen = UnityEngine.Time.realtimeSinceStartupAsDouble;
                    // Beelz multi-line reads (broadcast-msg list / tform abilities) can exceed the default
                    // 8-line / 1.5s budget — a transform kit is often 10-20 abilities — so widen it for them.
                    bool beelzMultiline = IsBeelzMultilineReadCommand(_genericResponseCommand);
                    double windowSecs = beelzMultiline ? 2.5 : GENERIC_CAPTURE_WINDOW_SECONDS;
                    int    maxLines   = beelzMultiline ? 64  : GENERIC_CAPTURE_MAX_LINES;
                    if (nowGen - _genericArmTime > windowSecs
                        || _genericResponseBuffer.Count >= maxLines)
                    {
                        // Dispatch a real capture to the panel; if nothing was captured (the
                        // reply never came and this is just a late, unrelated line) reset
                        // WITHOUT clobbering the panel's previous "last response".
                        if (_genericResponseBuffer.Count > 0) FlushGenericResponse();
                        else { _intercept = InterceptFlag.Idle; ResetCaptureCategory(); }
                        return false;           // this line is NOT our reply — never destroy it
                    }

                    // Capture any color-tagged server line. Bloodcraft / Kindred
                    // helpers always wrap their reply text in <color=...> tags;
                    // plain unstyled lines tend to be unrelated system chatter
                    // (player joins, broadcast etc.) and would just be noise.
                    // 0.10.5: ALSO match the per-command plain-leading header
                    // patterns (see _genericReplyPlainHeaders below) so that
                    // Bloodcraft replies whose first line is unstyled text
                    // (".wep get" starts with "Your weapon expertise is...")
                    // get destroyed by the silent flag too. Without this match
                    // the FIRST and most-informative line of every silent
                    // refresh still surfaces in chat.
                    // The Beelzebub broadcast-msg list / tform abilities replies are PLAIN (no color tags):
                    // a header + "  [n] …" lines. For those commands only, capture every non-blank line in
                    // the burst window so the announcements / transform-loadout editors can parse them.
                    bool isReplyLine =
                        text.StartsWith("<color", System.StringComparison.Ordinal)
                     || LooksLikePlainReplyHeaderForCommand(text, _genericResponseCommand)
                     || (beelzMultiline && !string.IsNullOrWhiteSpace(text));
                    if (isReplyLine)
                    {
                        _intercept = InterceptFlag.ReceivingGenericResponse;
                        _genericResponseBuffer.Add(text);
                        // Slide the 0.6s flush WITHIN the hard window so a normal burst still
                        // flushes promptly after its last line; the hard window above is the
                        // ceiling that stops a sustained unrelated stream from latching it.
                        _interceptLastLineTime = nowGen;
                        // 0.10.2: silent-mode auto-fires destroy the chat copy
                        // so overlay/tab refresh traffic doesn't spam chat. The
                        // 0.8.3 default (return false) is preserved for any
                        // capture armed without the silent flag, so manual user
                        // clicks (Refresh button etc.) still see their reply in
                        // chat as before.
                        return ShouldSuppressByCategory();
                    }
                    return false;
                }

                case InterceptFlag.AwaitingFamSearch:
                {
                    // 0.10.0: single-line reply, parse + emit + return to Idle.
                    // 0.10.4: honor the silent flag so scanner replies get
                    // destroyed and don't surface in the chat window.
                    bool suppress = ShouldSuppressByCategory();
                    var successMatch = _famSearchSuccessRegex.Match(text);
                    if (successMatch.Success)
                    {
                        var listPart = successMatch.Groups["list"].Value;
                        var boxes = new System.Collections.Generic.List<(string box, bool shiny)>();
                        foreach (Match tok in _famSearchBoxTokenRegex.Matches(listPart))
                        {
                            var name = tok.Groups["name"].Value;
                            if (string.IsNullOrEmpty(name)) continue;
                            bool shiny = tok.Groups["shiny"].Success;
                            boxes.Add((name, shiny));
                        }
                        FireFamSearchCompleted(_famSearchQuery, boxes, hadAnyMatch: true);
                        _famSearchQuery = "";
                        _intercept = InterceptFlag.Idle;
                        ResetCaptureCategory();
                        return suppress || Config.Settings.ClearServerMessages;
                    }
                    if (_famSearchNoMatchRegex.IsMatch(text))
                    {
                        FireFamSearchCompleted(_famSearchQuery, new System.Collections.Generic.List<(string, bool)>(), hadAnyMatch: false);
                        _famSearchQuery = "";
                        _intercept = InterceptFlag.Idle;
                        ResetCaptureCategory();
                        return suppress || Config.Settings.ClearServerMessages;
                    }
                    // 0.10.7: VCF usage echo (".fam s [Name]") — treat as
                    // no-match so the scanner advances. Always suppress
                    // this line regardless of category visibility because
                    // it's never user-actionable (it's our own bug
                    // surfacing if it fires at all).
                    if (_famSearchUsageEchoRegex.IsMatch(text))
                    {
                        LogUtils.LogWarning($"VCF usage echo while AwaitingFamSearch '{_famSearchQuery}' — advancing as no-match.");
                        FireFamSearchCompleted(_famSearchQuery, new System.Collections.Generic.List<(string, bool)>(), hadAnyMatch: false);
                        _famSearchQuery = "";
                        _intercept = InterceptFlag.Idle;
                        ResetCaptureCategory();
                        return true;
                    }
                    // Some other server message arrived while we were awaiting.
                    // Don't transition out — keep waiting for the actual reply or
                    // until TickInterceptTimeouts gives up.
                    return false;
                }

                case InterceptFlag.AwaitingBoxContent:
                case InterceptFlag.ReceivingBoxContent:
                    var match = _boxContentEntryRegex.Match(text);
                    if (match.Success)
                    {
                        _intercept = InterceptFlag.ReceivingBoxContent;
                        var shinyGroup = match.Groups["shiny"];
                        var prestigeGroup = match.Groups["prestige"];
                        var levelGroup = match.Groups["level"];
                        var entry = new PlayerStateService.FamiliarBoxEntry
                        {
                            Index         = PlayerStateService.ParseInt(match.Groups["idx"].Value),
                            ColorHex      = match.Groups["color"].Value,
                            Name          = match.Groups["name"].Value,
                            Level         = levelGroup.Success    ? PlayerStateService.ParseInt(levelGroup.Value)    : 0,
                            Prestige      = prestigeGroup.Success ? PlayerStateService.ParseInt(prestigeGroup.Value) : 0,
                            IsShiny       = shinyGroup.Success,
                            ShinyColorHex = shinyGroup.Success    ? shinyGroup.Value : null,
                        };
                        if (!_boxContentBuffer.Exists(e => e.Index == entry.Index))
                            _boxContentBuffer.Add(entry);
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        // 0.10.10: scanner-initiated `.fam l` is classified
                        // BchAuto so each per-entry row is suppressed via
                        // ShouldSuppressByCategory. Without this, the silent
                        // box-sweep leaked ~10 entry lines per box (×~15
                        // boxes) into chat for users who hadn't enabled
                        // ClearServerMessages.
                        return ShouldSuppressByCategory() || Config.Settings.ClearServerMessages;
                    }
                    // Non-entry line in the middle: ignore (don't flush yet).
                    // The timeout in TickInterceptTimeouts() handles end-of-list.
                    return false;

                default:
                    return false;
            }
        }
        catch (System.Exception ex)
        {
            LogUtils.LogError($"MessageService.HandleInboundChat parse failed: {ex}");
            _intercept = InterceptFlag.Idle;
            return false;
        }
    }

    /// <summary>
    /// Per-frame: flush any buffered intercept list whose last new line arrived
    /// more than INTERCEPT_FLUSH_AFTER_SECONDS ago. Registered with
    /// CoreUpdateBehavior in Plugin.Load alongside ProcessAllMessages.
    /// </summary>
    public static void TickInterceptTimeouts()
    {
        if (_intercept == InterceptFlag.Idle) { _receivingArmTime = 0; return; }
        var now = UnityEngine.Time.realtimeSinceStartupAsDouble;

        // 0.18.3: hard cap on the structured Receiving states. Anchor the moment we first observe a
        // Receiving state (≈ when the reply header arrived); once RECEIVING_HARD_WINDOW_SECONDS elapses
        // from that anchor, force a flush even if unrelated colored lines keep sliding the soft timeout —
        // this is what stops the load-in `.prestige get` / `.fam boxes` intercepts from eating other
        // mods' system messages indefinitely. (The generic catch-all has its own equivalent bound.)
        bool receiving = _intercept == InterceptFlag.ReceivingBoxList
                         || _intercept == InterceptFlag.ReceivingBoxContent
                         || _intercept == InterceptFlag.ReceivingPrestigeInfo
                         || _intercept == InterceptFlag.ReceivingBloodInfo;
        if (receiving && _receivingArmTime <= 0) _receivingArmTime = now;
        bool hardCapHit = receiving && _receivingArmTime > 0
                          && (now - _receivingArmTime) > RECEIVING_HARD_WINDOW_SECONDS;
        if (hardCapHit)
            LogUtils.LogDiagnostic($"[Intercept] hard-cap flush of {_intercept} after {now - _receivingArmTime:0.0}s (stops eating unrelated system messages).");

        if (!hardCapHit && now - _interceptLastLineTime < INTERCEPT_FLUSH_AFTER_SECONDS) return;
        _receivingArmTime = 0;

        switch (_intercept)
        {
            case InterceptFlag.ReceivingBoxList:
                FlushBoxList();
                break;
            case InterceptFlag.ReceivingBoxContent:
                FlushBoxContent();
                break;
            case InterceptFlag.ReceivingPrestigeInfo:
                FlushPrestigeInfo();
                break;
            case InterceptFlag.ReceivingBloodInfo:
                FlushBloodInfo();
                break;
            case InterceptFlag.ReceivingGenericResponse:
                FlushGenericResponse();
                break;
            case InterceptFlag.AwaitingBoxList:
            case InterceptFlag.AwaitingBoxContent:
            case InterceptFlag.AwaitingPrestigeInfo:
            case InterceptFlag.AwaitingBloodInfo:
            case InterceptFlag.AwaitingGenericResponse:
                // Server never replied (command rejected, comms hiccup, etc.).
                // Reset so the next user click re-arms cleanly. Don't dispatch
                // an empty list - that'd clobber any previously-loaded data.
                LogUtils.LogWarning($"Intercept '{_intercept}' timed out with no server reply; resetting.");
                _intercept = InterceptFlag.Idle;
                if (_intercept == InterceptFlag.AwaitingGenericResponse) _genericResponseBuffer.Clear();
                ResetCaptureCategory();
                break;
            case InterceptFlag.AwaitingFamSearch:
                // 0.10.0: emit a "no match" result so the scanner moves on instead
                // of hanging on this name forever. Treating timeout as a soft
                // "not captured" is safer than retrying — Bloodcraft replies
                // quickly when the query is well-formed.
                LogUtils.LogWarning($"Intercept 'AwaitingFamSearch' timed out for '{_famSearchQuery}'; treating as no-match.");
                FireFamSearchCompleted(_famSearchQuery, new System.Collections.Generic.List<(string, bool)>(), hadAnyMatch: false);
                _famSearchQuery = "";
                _intercept = InterceptFlag.Idle;
                ResetCaptureCategory();
                break;
        }
    }

    /// <summary>
    /// 0.10.0: lightweight event payload for a parsed .fam s reply. The scanner
    /// (or any other consumer) subscribes via PlayerStateService.FamSearchCompleted
    /// and gets the search name back plus the list of matching boxes — each box
    /// carries a flag for whether the server included the pink-star shiny marker
    /// (meaning "at least one familiar in this box matching the query is shiny").
    /// </summary>
    public readonly struct FamSearchResult
    {
        public readonly string Query;
        public readonly System.Collections.Generic.IReadOnlyList<(string Box, bool HasShiny)> Boxes;
        public readonly bool HadAnyMatch;
        public FamSearchResult(string query, System.Collections.Generic.IReadOnlyList<(string, bool)> boxes, bool hadAnyMatch)
        {
            Query = query; Boxes = boxes; HadAnyMatch = hadAnyMatch;
        }
    }

    public static event System.Action<FamSearchResult> FamSearchCompleted;

    private static void FireFamSearchCompleted(string query, System.Collections.Generic.List<(string, bool)> boxes, bool hadAnyMatch)
    {
        try { FamSearchCompleted?.Invoke(new FamSearchResult(query ?? "", boxes, hadAnyMatch)); }
        catch (System.Exception ex)
        {
            LogUtils.LogError($"FamSearchCompleted subscriber threw: {ex}");
        }
    }

    // 0.10.6: every flush path resets the category tracking so it doesn't
    // leak into the NEXT intercept arming. Called by every flush + the
    // timeout reset. Replaces the 0.10.2 ResetCaptureSuppressionFlag().
    private static void ResetCaptureCategory()
    {
        _currentCaptureCategory = CommandCategory.Other;
        _currentCaptureHasBchUI = false;
    }

    /// <summary>0.10.6: classify an outbound command and store the result in
    /// _currentCaptureCategory / _currentCaptureHasBchUI so the receive-side
    /// handlers can make a suppression decision via ShouldSuppressByCategory.
    /// Consumes _nextCommandIsBchAuto (set by EnqueueMessageSilent) before
    /// falling back to prefix-based user-fire classification.</summary>
    private static void ClassifyAndStoreCategory(string command)
    {
        CommandClassification cls;
        if (_nextCommandIsBchAuto)
        {
            cls = CommandClassifier.ForBchAuto();
            _nextCommandIsBchAuto = false;
        }
        else
        {
            cls = CommandClassifier.ForUserFire(command);
        }
        _currentCaptureCategory = cls.Category;
        _currentCaptureHasBchUI = cls.HasBchUIDisplay;
    }

    private static void FlushGenericResponse()
    {
        // Wrap into the public state slot so subscribed tabs can render. Strip
        // nothing — let the UI label keep TMP color tags so the response
        // visually matches what the user sees scrolling by in chat.
        var snapshot = new PlayerStateService.LastServerResponse
        {
            Command    = _genericResponseCommand,
            Lines      = new List<string>(_genericResponseBuffer),
            CapturedAt = System.DateTime.UtcNow,
        };
        _genericResponseBuffer.Clear();
        _genericResponseCommand = "";
        _intercept = InterceptFlag.Idle;
        ResetCaptureCategory();
        PlayerStateService.UpdateLastResponse(snapshot);
        LogUtils.LogInfo($"Captured {snapshot.Lines.Count} server response line(s) for '{snapshot.Command}'.");
    }

    private static void FlushPrestigeInfo()
    {
        var snapshot = _prestigeInfoBuffer;
        _prestigeInfoBuffer = new PlayerStateService.PrestigeInfo
        {
            EffectLines = new System.Collections.Generic.List<string>(),
        };
        _intercept = InterceptFlag.Idle;
        PlayerStateService.UpdatePrestigeInfo(snapshot);
        LogUtils.LogInfo($"Parsed prestige info for '{snapshot.TypeName}' (level {snapshot.Level}/{snapshot.MaxLevel}, {snapshot.EffectLines?.Count ?? 0} effect lines).");
    }

    private static void FlushBloodInfo()
    {
        var snapshot = _bloodInfoBuffer;
        _bloodInfoBuffer = new PlayerStateService.BloodInfo
        {
            StatLines = new System.Collections.Generic.List<string>(),
        };
        _intercept = InterceptFlag.Idle;
        ResetCaptureCategory();
        PlayerStateService.UpdateBloodInfo(snapshot);
        LogUtils.LogInfo($"Parsed blood info for '{snapshot.BloodType}' (level {snapshot.Level} prestige {snapshot.Prestige}, {snapshot.StatLines?.Count ?? 0} stat lines).");
    }

    private static void FlushBoxList()
    {
        var snapshot = new List<string>(_boxListBuffer);
        _boxListBuffer.Clear();
        _intercept = InterceptFlag.Idle;
        ResetCaptureCategory(); // 0.10.10: classify-on-arm needs reset-on-flush
        PlayerStateService.UpdateBoxList(snapshot);
        LogUtils.LogInfo($"Parsed {snapshot.Count} familiar box name(s).");
    }

    private static void FlushBoxContent()
    {
        var snapshot = new List<PlayerStateService.FamiliarBoxEntry>(_boxContentBuffer);
        _boxContentBuffer.Clear();
        _intercept = InterceptFlag.Idle;
        ResetCaptureCategory(); // 0.10.10: classify-on-arm needs reset-on-flush
        var box = PlayerStateService.ActiveBox;
        if (!string.IsNullOrEmpty(box))
            PlayerStateService.UpdateBoxContents(box, snapshot);
        LogUtils.LogInfo($"Parsed {snapshot.Count} familiar entries for box '{box}'.");
    }
}
