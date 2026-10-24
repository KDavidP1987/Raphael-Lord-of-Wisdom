using System.Collections.Generic;

namespace Raphael.Services.Guides;

// 0.73.0: the Bloodcraft guide. Replaces the old "Bloodcraft Quick Start" and "Bloodcraft Help" tabs. Checked against
// the Bloodcraft source (1.13.24 reference copy + the 1.13.26 clone: chat commands and config are identical) and
// Raphael's own Bloodcraft tabs in MainPanel. Every command in tests/fixtures/bloodcraft-commands.json and every
// setting in bloodcraft-config.json is documented below.
internal static partial class Guides
{
    static partial void AddBloodcraft(List<Guide> list)
    {
        static string C(string s) => GuideText.Cmd(s);
        static string A(string s) => GuideText.Arg(s);

        const string Cfg = "BepInEx/config/io.zfolmt.Bloodcraft.cfg";
        const string R = "restart";

        var b = new GuideBuilder("bloodcraft", "Bloodcraft guide",
            "Levels, classes, weapon expertise, blood legacies, prestige, familiars, professions and quests, and how Raphael drives them.",
            "Bloodcraft 1.13.26", GuideKind.FullMod, "bloodcraft");

        // ------------------------------------------------------------------------------------------- 1. what
        b.Section(GuideSchema.What, true, s =>
        {
            s.Bullets(
                "Bloodcraft adds an RPG layer to V Rising: character levels, weapon expertise, blood legacies, classes, prestige, familiars, professions and daily and weekly quests.",
                "It is a <b>server-side</b> mod. Raphael is the client UI for it: the <b>Bloodcraft</b> tab group shows your progress live and sends the chat commands for you.",
                "Every system is switched on separately by the server admin. Out of the box all of them are off, so your server may have only some of them.",
                "It is for players who want to use those systems without memorising commands, and for admins who set them up.",
                "Every command still works typed in chat. This page lists them all, with every server setting and its default.");
        });

        // ------------------------------------------------------------------------------------------- 2. quickstart
        b.Section(GuideSchema.QuickStart, true, s =>
        {
            s.Steps(
                "Open the <b>Bloodcraft</b> group in the left rail. It lights up once the server confirms Bloodcraft, usually a few seconds after you join. If it stays grey, see Troubleshooting.",
                "Open <b>Levels</b>. It shows your level, the blood legacy and weapon expertise you are using, your familiar and your professions. You earn experience by killing enemies.",
                "Open <b>Class</b>, pick a class in <b>Select first class</b> and press Submit. The first pick is free. Changing it later costs items: use <b>Change class</b> on the same tab (" + C(".class c <class>") + ").",
                "Open <b>Weapon Expertise</b> and <b>Blood Legacy</b>. Use <b>List Stats</b>, then <b>Set bonus stat</b> to choose up to 3 bonus stats per weapon and per blood. Bonuses grow as that weapon or blood levels up.",
                "Open <b>Boxes</b>, click a box, then click a familiar to summon it. Use <b>Familiars</b> to call or dismiss it, toggle combat and prestige it.",
                "Open <b>Daily Quests</b>. Press <b>Refresh</b> to see your daily and weekly quest and <b>Track</b> to get directions to the target.",
                "At the level cap, open <b>Prestige</b> and use <b>Prestige in a system</b> to reset that level for permanent bonuses.");
            s.Callout(CalloutKind.Note, "If a button replies \"... is not enabled\", your server has that system turned off. Only the admin can turn it on.");
        });

        // ------------------------------------------------------------------------------------------- 3. concepts
        b.Section(GuideSchema.Concepts, false, s =>
        {
            s.Detail("Which systems are on", d =>
            {
                d.P("Each system has its own switch in the server config, and every switch is off by default:");
                d.Bullets(
                    "Levels: <b>LevelingSystem</b>. Rested experience: <b>RestedXPSystem</b>.",
                    "Weapon expertise: <b>ExpertiseSystem</b>. Extra unarmed spells: <b>UnarmedSlots</b>. Class spell on Shift: <b>ShiftSlot</b>.",
                    "Blood legacies: <b>LegacySystem</b>. Classes: <b>ClassSystem</b>.",
                    "Prestige: <b>PrestigeSystem</b> (needs LevelingSystem too). Exo prestige: <b>ExoPrestiging</b>.",
                    "Familiars: <b>FamiliarSystem</b>. Familiar prestige: <b>FamiliarPrestige</b>. Battles: <b>FamiliarBattles</b>.",
                    "Professions: <b>ProfessionSystem</b>. Quests: <b>QuestSystem</b>. Starter kit: <b>StarterKit</b>.");
                d.P("Raphael's live readouts need at least one of levels, legacies, expertise, classes or familiars to be on. With only quests or professions on, the server never answers Raphael's handshake and the tabs stay grey.");
            });

            s.Detail("Levels and rested experience", d =>
            {
                d.Bullets(
                    "You gain experience from kills. The level cap is <b>90</b>, and new characters start at level <b>10</b>.",
                    "Experience multipliers: normal units ×7.5, V Bloods ×15, docile units ×0.15, war event spawns ×0.2. Unit spawners (vermin nests, tombs) give none by default.",
                    "Kills far below your level give less experience. LevelScalingMultiplier (default 0.05) sets how much less.",
                    "Shared experience: nearby players in combat share kill experience if they are within 25 units and 10 levels of each other. Players who have prestiged at least once ignore the level gap on PvE servers. On PvP servers only clan members share.",
                    "Rested experience (off by default): log out in a coffin to bank bonus experience. A stone coffin gives the full rate, a wooden one half. The bank holds up to 5 levels' worth and fills by 5% of that every 120 minutes, so about 20 offline hours fill it. Prestiging resets the bank.",
                    "Check progress with " + C(".lvl get") + ", or watch the <b>Levels</b> tab and the XP overlay.");
            });

            s.Detail("Weapon expertise", d =>
            {
                d.Bullets(
                    "Each of the 16 weapon types levels on its own: Sword, Axe, Mace, Spear, Crossbow, GreatSword, Slashers, Pistols, Reaper, Longbow, Whip, Unarmed, FishingPole, TwinBlades, Daggers and Claws.",
                    "A weapon gains expertise when it is equipped for the killing blow. Normal units ×2, V Bloods ×5. The cap is level <b>100</b>.",
                    "Pick up to <b>3 bonus stats</b> per weapon. Each grows from 0 to its full cap as that weapon reaches level 100.",
                    "Weapon stats and their base caps: MaxHealth 250, MovementSpeed 25%, PrimaryAttackSpeed 10%, PhysicalLifeLeech 10%, SpellLifeLeech 10%, PrimaryLifeLeech 15%, PhysicalPower 20, SpellPower 10, PhysicalCritChance 10%, PhysicalCritDamage 50%, SpellCritChance 10%, SpellCritDamage 50%.",
                    "A stat that matches your class's synergies gets a ×1.5 cap.",
                    "Clearing a weapon's stat choices costs 500 Greater Stygian Shards by default (" + C(".wep rst") + ", current weapon only).",
                    "Raphael only receives the equipped weapon's expertise. Swap weapons to see each one.");
            });

            s.Detail("Unarmed spells and the Shift slot", d =>
            {
                d.Bullets(
                    "With <b>UnarmedSlots</b> on, you can keep spells in your weapon-skill slots while unarmed. Run " + C(".wep locksp") + ", equip the spells you want, then run it again to lock them. It doesn't work in exoform.",
                    "<b>Duality</b> (default on) gives both unarmed slots; off gives one. <b>FishingSlots</b> does the same for the fishing pole.",
                    "With <b>ShiftSlot</b> and classes on, " + C(".class shift") + " puts a class spell on Shift in place of your travel form. Pick which spell with " + C(".class csp <choice>") + ": 0 is the shared default (Veil of Shadow), 1 to 5 are your class's spells.",
                    "Class spells unlock at Experience prestige levels 0, 1, 2, 3, 4 and 5 by default (one more per prestige). When prestige is off, all of them are open.",
                    "Both commands need one free inventory slot, because jewels are moved while switching.");
            });

            s.Detail("Blood legacies", d =>
            {
                d.Bullets(
                    "Each of the 10 blood types levels on its own: Worker, Warrior, Scholar, Rogue, Mutant, Draculin, Immortal, Creature, Brute and Corruption.",
                    "A blood gains legacy when you feed (executes and completed feeds). Normal units ×1, V Bloods ×5. The cap is level <b>100</b>.",
                    "Pick up to <b>3 bonus stats</b> per blood, scaling with that blood's level like weapon stats.",
                    "Blood stats and their base caps: HealingReceived 15%, DamageReduction 5%, PhysicalResistance 10%, SpellResistance 10%, ResourceYield 25%, ReducedBloodDrain 50%, SpellCooldownRecoveryRate 10%, WeaponCooldownRecoveryRate 10%, UltimateCooldownRecoveryRate 20%, MinionDamage 25%, AbilityAttackSpeed 10%, CorruptionDamageReduction 10%.",
                    "Clearing a blood's stat choices costs 500 Greater Stygian Shards by default (" + C(".bl rst") + ", the blood you are using).",
                    C(".bl get <blood>") + " can show any blood's progress, not only the one you are using.");
            });

            s.Detail("Classes", d =>
            {
                d.P("There are six classes. Your first pick (" + C(".class s <class>") + ") is free. Changing later (" + C(".class c <class>") + ") costs 750 Greater Stygian Shards by default. You can type the class name or its number from " + C(".class l") + ".");
                d.P("A class gives three things: a ×1.5 cap on its synergy stats, a 7.5% chance on hit to apply its spell school's debuff (if the debuff is already there, a second-tier effect applies instead), and spells for the Shift slot.");
                d.Bullets(
                    "<b>Blood Knight</b>: weapon MaxHealth, PrimaryAttackSpeed, PrimaryLifeLeech, PhysicalPower. Blood DamageReduction, ReducedBloodDrain, WeaponCooldownRecoveryRate, AbilityAttackSpeed. On hit: Leech.",
                    "<b>Demon Hunter</b>: weapon MovementSpeed, PrimaryAttackSpeed, PhysicalCritChance, PhysicalCritDamage. Blood PhysicalResistance, ReducedBloodDrain, WeaponCooldownRecoveryRate, MinionDamage. On hit: Static.",
                    "<b>Vampire Lord</b>: weapon MaxHealth, SpellLifeLeech, PhysicalPower, SpellPower. Blood DamageReduction, SpellResistance, UltimateCooldownRecoveryRate, CorruptionDamageReduction. On hit: Chill.",
                    "<b>Shadow Blade</b>: weapon MovementSpeed, PrimaryAttackSpeed, PhysicalPower, PhysicalCritDamage. Blood SpellResistance, ReducedBloodDrain, WeaponCooldownRecoveryRate, AbilityAttackSpeed. On hit: Ignite.",
                    "<b>Arcane Sorcerer</b>: weapon SpellLifeLeech, SpellPower, SpellCritChance, SpellCritDamage. Blood HealingReceived, SpellCooldownRecoveryRate, UltimateCooldownRecoveryRate, AbilityAttackSpeed. On hit: Weaken.",
                    "<b>Death Mage</b>: weapon MaxHealth, SpellLifeLeech, SpellPower, SpellCritDamage. Blood PhysicalResistance, SpellResistance, SpellCooldownRecoveryRate, MinionDamage. On hit: Condemn.");
                d.P("Synergies are server settings, so " + C(".class lst") + " shows your server's real list.");
            });

            s.Detail("Prestige", d =>
            {
                d.P("Prestige resets one system's level in exchange for permanent bonuses. There are 28 prestige types: Experience, Exo, one per weapon (SwordExpertise, AxeExpertise ...) and one per blood (WorkerLegacy, WarriorLegacy ...). " + C(".prestige l") + " lists them. A partial name works (sword finds SwordExpertise); the first match wins.");
                d.Bullets(
                    "<b>Experience</b>: needs max level (90). Your level goes back to 0. Each prestige cuts experience from kills by 5% and raises expertise and legacy gain by 10%. Up to 10 prestiges.",
                    "Experience prestige 1 also grants the permanent shroud buff by default. Toggle it with " + C(".prestige shroud") + ". If prestige buffs go missing, " + C(".prestige sb") + " reapplies them.",
                    "<b>Weapon or blood</b>: needs that weapon or blood at level 100. It goes back to 0. Each prestige lowers its gain rate by 10% and raises its stat bonuses by 10%. Up to 10 prestiges each.",
                    "The rates stack: a weapon's real gain rate is your Experience prestige bonus minus its own prestige penalty. " + C(".prestige get <prestigeType>") + " shows the totals.",
                    "Leaderboards (" + C(".prestige lb <prestigeType>") + ") show the top 10 for each type.");
            });

            s.Detail("Exo prestige and exoforms", d =>
            {
                d.P("Exo is the endgame prestige. It is off unless the admin turns on <b>ExoPrestiging</b>.");
                d.Bullets(
                    "To Exo prestige you need maxed Experience prestige (10 by default) and max level. Run " + C(".prestige me Exo") + ".",
                    "Each Exo prestige sets your level back to <b>0</b>, so you level to the cap again before the next one. Up to 100 Exo prestiges.",
                    "Each one also pays a reward: by default 500 Primal Stygian Shards. If your inventory is full, they drop on the ground. The admin can change or remove the reward.",
                    "Exo unlocks two shapeshift forms. Evolved Vampire needs Dracula's primordial essence consumed; Corrupted Serpent needs Megara's. Choose one with " + C(".prestige sf <shapeshift>") + ".",
                    C(".prestige exoform") + " turns the <b>taunt</b> emote into your exoform trigger (run it again to turn that off). Then taunt to enter the form, and taunt again to leave early.",
                    "Form time: 15 seconds at Exo 1, then 15 + 1.65 seconds per Exo level, up to 180 seconds at Exo 100. The charge refills from empty to full over 24 hours. A 5-second countdown warns you before the form ends.",
                    "With <b>TrueImmortal</b> on, your blood becomes Immortal while in the form.");
            });

            s.Detail("Familiars: unlocking and boxes", d =>
            {
                d.Bullets(
                    "Enemies you kill can unlock as familiars: 5% for normal units and 1% for V Bloods by default. V Bloods and minions are excluded unless the admin allows them.",
                    "Unlocks go into named <b>boxes</b> of up to 10 familiars each. You can have up to 50 boxes. You need at least one unlock before you can add a box.",
                    "To summon one: choose a box (" + C(".fam cb <name>") + "), list it (" + C(".fam l") + "), then bind by number (" + C(".fam b <boxIndex>") + "). " + C(".fam sb <name>") + " searches all boxes and binds in one step.",
                    "Only one familiar can be out. Unbind (" + C(".fam ub") + ") before binding another. Unbinding keeps the familiar in its box; " + C(".fam r <choice>") + " deletes it for good.",
                    "Familiars level from kills up to level 90 (normal units ×7.5, V Bloods ×15).",
                    "With <b>ShareUnlocks</b> on, clan or party members within shared-experience range can receive the same unlock.",
                    "With <b>FamiliarPvP</b> off, your familiar is unbound when you enter PvP combat.");
            });

            s.Detail("Familiars: shiny, prestige and echoes", d =>
            {
                d.Bullets(
                    "<b>Shiny</b>: a new unlock has a 20% chance to be shiny, and unlocking the same unit again always is. A shiny familiar has a visual effect and the same chance as class on-hit effects to apply its spell school's debuff.",
                    "Make the active familiar shiny with " + C(".fam shiny <spellSchool>") + " (blood, storm, unholy, chaos, frost or illusion). It costs 100 Vampiric Dust by default; changing an existing shiny costs 25% of that. Rebind to see it. " + C(".fam option shiny") + " hides shiny effects (and their on-hit debuff).",
                    "Vampiric Dust is refined at an Advanced Grinder when the admin turns on <b>ExtraRecipes</b>.",
                    "<b>Prestige</b> (off by default): " + C(".fam pr") + " prestiges the active familiar. At max level it goes back to level 1 with +10% stats per prestige, up to 10 prestiges. If you carry the schematic cost (1000 Schematics by default), that is used first and the familiar prestiges at once.",
                    "<b>Echoes</b>: with <b>PrimalEchoes</b> and <b>AllowVBloods</b> on, " + C(".fam echoes <vBlood>") + " buys a V Blood familiar with the Exo reward item. The cost is the Exo reward amount times 1 to 25 by the boss's level, times EchoesFactor. With defaults that is 500 to 12,500 Primal Stygian Shards.");
            });

            s.Detail("Familiars: emotes, equipment and battles", d =>
            {
                d.P("Turn emote actions on with " + C(".fam e") + ". Then:");
                d.Bullets(
                    "<b>Wave</b> calls or dismisses your familiar.",
                    "<b>Salute</b> toggles its combat mode.",
                    "<b>Clap</b> binds or unbinds it.",
                    "<b>Beckon</b> enters interact mode, so you can open the familiar like a servant and give it gear. With <b>EquipmentOnly</b> on it has equipment slots only, no inventory.",
                    "<b>Taunt</b> is the exoform trigger when you turned that on.");
                d.P("Familiar battles let players fight each other's familiar teams in an arena an admin sets with " + C(".fam sba") + ". Build a battle group of 3 familiars, then " + C(".fam challenge <name>") + "; the other player emotes <b>yes</b> to accept or <b>no</b> to decline.");
                d.Callout(CalloutKind.Experimental, "Bloodcraft's own config warns that familiar battles are most likely not working since V Rising 1.1. Use at your own risk.");
            });

            s.Detail("Professions", d =>
            {
                d.P("Eight professions level from gathering and crafting: Enchanting, Alchemy, Harvesting, Blacksmithing, Tailoring, Woodcutting, Mining and Fishing. The cap is level 100.");
                d.Bullets(
                    "Mining, Woodcutting and Harvesting: extra resources per node, rising with level, plus bonus drops (gold ore, saplings and seeds).",
                    "Fishing: an extra fish every 20 levels.",
                    "Alchemy: stronger and longer potions, up to double duration at max. Holy potions only get the longer duration.",
                    "Blacksmithing, Tailoring and Enchanting: crafted gear gets up to +10% base stats and double durability at max.",
                    "The admin can turn single professions off with <b>DisabledProfessions</b>.");
            });

            s.Detail("Quests", d =>
            {
                d.Bullets(
                    "You get one daily and one weekly quest: kill, gather or craft targets for experience and an item reward.",
                    "Rewards come from a pool (by default tier 3 and tier 2 Stygian shards and crystals). Weekly rewards are ×5 and V Blood kill quests ×3.",
                    "Each daily also has a 10% chance to give a random perfect gem, which steers the spell school of a Primal Jewel.",
                    "View a quest with " + C(".quest p <questType>") + " and get directions with " + C(".quest t <questType>") + ". Type daily or weekly (d or w also work).",
                    "Reroll with " + C(".quest r <questType>") + ". Each costs 50 Silver Coins by default. You can't reroll a quest you already completed this day or week.",
                    "With <b>InfiniteDailies</b> on, a new daily starts as soon as you finish one.");
            });

            s.Detail("Starter kit and server extras", d =>
            {
                d.Bullets(
                    "<b>Starter kit</b>: " + C(".misc kitme") + " gives a one-time kit (by default 500 Blood Essence, 1000 Stone, 1000 Wood and 250 Bone) and optionally a familiar.",
                    "<b>Extra recipes</b>: new salvage and refining recipes, including Vampiric Dust, Primal Stygian Shards and Primal Jewels (random tier 4 jewels refined from Demon Fragments at the gem cutter).",
                    "<b>Bleeding edge</b>: optional weapon changes for Slashers (bleed every 3rd chained hit), Crossbow (faster bolts), TwinBlades (shield on V Bloods), Pistols (25% more range) and Daggers (stacks persist).",
                    "<b>Elite shard bearers</b>, <b>elite primal rifts</b> and <b>Nightmare mode</b> make PvE harder. <b>Primal arsenal</b> adds experimental weapons.");
            });

            s.Detail("How Raphael gets your stats", d =>
            {
                d.P("When you join, Raphael registers with Bloodcraft. The server then sends your progress every 2.5 seconds (0.1 seconds with <b>Eclipsed</b> on) as signed chat messages that Raphael reads and hides. Replies to typed commands show in chat and in Raphael's <b>Last server response</b> panel.");
                d.P("Since Bloodcraft 1.13.22 a server can send these stats over the Emberglass mod instead (<b>UseEmberglassEclipseBridge</b>). See Troubleshooting if your stats never show up.");
                d.P("If the Eclipse client mod is installed, Raphael turns its live readouts off so the two don't clash. Eclipse's HUD shows your stats, and Raphael's buttons still send commands.");
            });
        });

        // ------------------------------------------------------------------------------------------- 4. tabs
        b.Section(GuideSchema.Tabs, false, s =>
        {
            s.P("All tabs are in the <b>Bloodcraft</b> group. Buttons send the command shown; replies appear in chat and the <b>Last server response</b> panel.");
            s.Tabs(t => t
                .Row("Familiars", "Your active familiar's level, prestige and stats. Buttons: <b>Recall / Dismiss</b>, <b>Toggle Combat</b>, <b>Toggle Emotes</b>, <b>List Emotes</b>, <b>Prestige</b>, <b>Unbind</b>. <b>More Familiar Actions</b> has search, smart bind, shiny, options, echoes and <b>Force-unbind</b>.",
                    ".fam t, .fam c, .fam e, .fam actions, .fam pr, .fam ub, .fam s, .fam sb, .fam shiny, .fam option, .fam echoes, .fam reset")
                .Row("Boxes", "Lists your boxes. Click one to open it, then click a familiar to bind it. If one is already out, click the same familiar again to confirm the swap. Also create, delete, rename, move and permanently delete.",
                    ".fam boxes, .fam cb <name> + .fam l, .fam ub + .fam b <#>, .fam ab, .fam db, .fam rb, .fam mb, .fam r")
                .Row("V-Bloods", "Your V Blood collection by variant (basic, shiny, primal). Fills in as you open boxes; <b>Scan all</b> walks every box (30 to 60 seconds) and restores your active box. Filters and sort; <b>Summon</b> binds one.",
                    ".fam boxes, .fam cb, .fam l, .fam b")
                .Row("All Familiars", "Every familiar from every box in one list, with search and sort. Click a row to bind it; <b>Delete</b> removes it for good (two clicks). <b>Quick Spawn slots</b> pins familiars to the Quick Spawn overlay.",
                    ".fam cb + .fam b, .fam r, .fam sb")
                .Row("Class", "Your class. <b>List Classes</b>, <b>List Spells</b>, <b>List Stats</b>, <b>Toggle Shift</b>; <b>Select first class</b>, <b>Change class</b> and <b>Choose class shift spell</b> forms; <b>Compare all classes</b>. Select first class makes the free first pick; Change class switches later and costs 750 Greater Stygian Shards by default.",
                    ".class l, .class lsp, .class lst, .class shift, .class s <class>, .class c <class>, .class csp <#>")
                .Row("Weapon Expertise", "The equipped weapon's level and bonus stats, with class synergy hints. <b>Refresh</b>, <b>List Weps</b>, <b>List Stats</b>, <b>Reset Stats</b>, <b>Lock Spells</b>, and <b>Set bonus stat</b>.",
                    ".wep get, .wep l, .wep lst, .wep rst, .wep locksp, .wep cst <weapon> <stat>")
                .Row("Blood Legacy", "Your current blood's level and bonus stats. <b>Refresh</b>, <b>List Bloods</b>, <b>List Stats</b>, <b>Reset Stats</b>, <b>Set bonus stat</b> and <b>Show blood info</b> for any blood.",
                    ".bl get, .bl l, .bl lst, .bl rst, .bl cst <blood> <stat>, .bl get <blood>")
                .Row("Unarmed + Shift", "Your Shift spell and unarmed expertise. <b>Toggle Shift</b>, <b>Lock Spells</b>, <b>Refresh</b>. Choose the Shift spell on the <b>Class</b> tab (<b>Choose class shift spell</b>).",
                    ".class shift, .wep locksp, .wep get")
                .Row("Prestige", "Your prestige levels and Exo charge (asks for .prestige get Exo when opened). <b>List</b>, <b>Sync Buffs</b>, <b>Exoform</b>, <b>Shroud</b>; forms to prestige, show info, view a leaderboard and select an exoform.",
                    ".prestige l, .prestige sb, .prestige exoform, .prestige shroud, .prestige me, .prestige get, .prestige lb, .prestige sf")
                .Row("Levels", "Everything at a glance: experience, legacy, expertise, familiar and professions. <b>Profession Tools</b> and <b>Player Tools</b> (user stats, logs, reminders, music fix, starter kit, prepare for the hunt, scrolling text).",
                    ".wep l, .prof l, .prof log, .prof get, .misc userstats, .lvl log, .misc remindme, .misc silence, .misc kitme, .misc prepare, .misc sct")
                .Row("Daily Quests", "Your daily and weekly quest. <b>Refresh</b>, <b>Track</b> and <b>Reroll</b> for each, and <b>Toggle Quest Log</b>.",
                    ".quest p d, .quest t d, .quest r d, .quest p w, .quest t w, .quest r w, .quest log")
                .Row("Admin", "Admin only. <b>Server Health</b>, forms for every set, reset and toggle command, and the <b>Reset Bloodcraft progress</b> card (see Admin guide).",
                    ".misc health, .lvl set, .lvl ignore, .prof set, .prestige set, .prestige r, .prestige ignore, .quest c, .quest rf, .bl set, .wep set, .fam sl"));
            s.P("Footer toggles open the draggable overlays: XP, Familiar, Familiar Browser, Daily Quest, Professions and Quick Spawn.");
        });

        // ------------------------------------------------------------------------------------------- 5. player
        b.Section(GuideSchema.Player, false, s =>
        {
            s.P("Arguments in " + A("angle brackets") + " are required, those in [square brackets] optional. Put names with spaces in double quotes. Names and types ignore case.");

            s.Detail("Short forms that look alike", d =>
            {
                d.P("The same short letter means different things in different groups. Check the group before you press Enter:");
                d.Bullets(
                    C(".fam c") + " toggles familiar combat. " + C(".class c") + " changes your class (costs items). " + C(".quest c") + " is the admin's force-complete.",
                    C(".fam r") + " <b>deletes</b> a familiar for good. " + C(".quest r") + " rerolls a quest. " + C(".prestige r") + " is the admin's prestige reset.",
                    C(".fam s") + " searches your boxes. " + C(".class s") + " selects your first class.",
                    C(".fam sb") + " is smart bind. " + C(".prestige sb") + " reapplies prestige buffs.",
                    C(".fam ub") + " unbinds and keeps the familiar. " + C(".fam reset") + " clears a stuck familiar. Neither deletes anything.",
                    C(".fam l") + " lists the familiars in your box; " + C(".fam boxes") + " lists the boxes; " + C(".fam bgs") + " lists battle groups.",
                    C(".wep l") + " / " + C(".bl l") + " list weapon or blood types; " + C(".wep lst") + " / " + C(".bl lst") + " list stats; " + C(".class lst") + " lists class synergies; " + C(".class lsp") + " lists class spells.",
                    C(".fam shiny") + " buys a shiny; " + C(".fam option shiny") + " only hides or shows shiny effects.",
                    C(".prestige sf") + " picks the exoform; " + C(".prestige exoform") + " turns the taunt trigger on or off.");
            });

            s.Detail("Levels and general", d => d.Commands("Levels and general", c => c
                .Player("level get", ".lvl get", "Shows your level, prestige and progress, and any rested experience left.")
                .Also(".level get")
                .Player("level log", ".lvl log", "Turns chat messages for experience gains on or off.")
                .Player("miscellaneous userstats", ".misc userstats", "Shows V Bloods slain, units killed, deaths, time online, distance travelled and blood consumed.")
                .Player("miscellaneous reminders", ".misc remindme", "Turns Bloodcraft's feature reminders on or off.")
                .Also(".misc reminders")
                .Player("miscellaneous sct", ".misc sct [input]", "Turns one kind of scrolling combat text on or off: PlayerXP, ExpertiseXP, LegacyXP, FamiliarXP, ProfessionXP or ProfessionYields (or 1 to 6). With no argument it lists them.")
                .Player("miscellaneous silence", ".misc silence", "Clears stuck combat music. Refused while you are in combat.")
                .Player("miscellaneous starterkit", ".misc kitme", "Gives the starter kit once per character. Needs StarterKit on.")
                .Also(".misc starterkit")
                .Player("miscellaneous prepareforthehunt", ".misc prepare", "Completes the Getting Ready for the Hunt journal step if you haven't. Needs leveling on.")
                .Also(".misc prepareforthehunt")));

            s.Detail("Weapon expertise", d => d.Commands("Weapon expertise", c => c
                .Player("weapon get", ".wep get", "Shows the equipped weapon's expertise level, prestige, progress and current bonus values.")
                .Player("weapon log", ".wep log", "Turns chat messages for expertise gains on or off.")
                .Player("weapon list", ".wep l", "Lists the weapon types.")
                .Player("weapon liststats", ".wep lst", "Lists the weapon stats with their numbers and base caps.")
                .Player("weapon choosestat", ".wep cst <weaponOrStat> [statType]", "Adds a bonus stat. Type a weapon and a stat number (" + C(".wep cst Sword 7") + "), or only a stat number for the equipped weapon. Up to 3 per weapon by default.")
                .Player("weapon resetstats", ".wep rst", "Clears the equipped weapon's stat choices. Costs 500 Greater Stygian Shards by default.")
                .Player("weapon lockspells", ".wep locksp", "Run once, equip the spells you want for your unarmed slots, then run again to lock them. Needs UnarmedSlots on.")));

            s.Detail("Blood legacies", d => d.Commands("Blood legacies", c => c
                .Player("blood get", ".bl get [blood]", "Shows a blood's legacy level, prestige, progress and bonus values. With no argument, the blood you are using.")
                .Player("blood log", ".bl log", "Turns chat messages for legacy gains on or off.")
                .Player("blood list", ".bl l", "Lists the blood legacies.")
                .Player("blood liststats", ".bl lst", "Lists the blood stats with their numbers and base caps.")
                .Player("blood choosestat", ".bl cst <bloodOrStat> [statType]", "Adds a bonus stat. Type a blood and a stat number (" + C(".bl cst Worker 3") + "), or only a stat number for your current blood. Up to 3 per blood by default.")
                .Player("blood resetstats", ".bl rst", "Clears your current blood's stat choices. Costs 500 Greater Stygian Shards by default.")));

            s.Detail("Classes", d => d.Commands("Classes", c => c
                .Player("class list", ".class l", "Lists the six classes with their numbers.")
                .Player("class select", ".class s <input>", "Picks your first class, by name or number. Free. If you already have a class it tells you to use .class c.")
                .Player("class change", ".class c <input>", "Changes to another class. Costs 750 Greater Stygian Shards by default.")
                .Player("class liststats", ".class lst [classType]", "Shows a class's weapon and blood synergies. With no argument, your class.")
                .Player("class listspells", ".class lsp [classType]", "Shows a class's Shift spells. With no argument, your class.")
                .Player("class lockshift", ".class shift", "Turns your class spell on Shift on or off. Needs ShiftSlot on and a free inventory slot.")
                .Player("class choosespell", ".class csp <choice>", "Puts a class spell on Shift: 0 for the default Veil of Shadow, 1 to 5 for your class's spells. Needs .class shift on and the prestige level for that spell.")));

            s.Detail("Prestige", d => d.Commands("Prestige", c => c
                .Player("prestige list", ".prestige l", "Lists the 28 prestige types.")
                .Player("prestige self", ".prestige me <prestigeType>", "Prestiges you in one type when you meet its level. " + C("<prestigeType>") + " is Experience, Exo, a weapon (SwordExpertise ...) or a blood (WorkerLegacy ...).")
                .Player("prestige get", ".prestige get <prestigeType>", "Shows your prestige level in a type and its effects. For Exo it shows the form time and stored charge.")
                .Player("prestige leaderboard", ".prestige lb <prestigeType>", "Shows the top 10 players for a type. Needs Leaderboard on.")
                .Player("prestige syncbuffs", ".prestige sb", "Reapplies your Experience prestige buffs if any are missing.")
                .Player("prestige permashroud", ".prestige shroud", "Keeps the shroud buff from Experience prestige on (or turns that off).")
                .Player("prestige exoform", ".prestige exoform", "Turns the taunt emote into your exoform trigger, or back. Needs Exo prestige 1 and Dracula's or Megara's essence consumed.")
                .Player("prestige selectform", ".prestige sf <shapeshift>", "Chooses your exoform: EvolvedVampire (needs Dracula's essence) or CorruptedSerpent (needs Megara's).")));

            s.Detail("Familiars: summon and control", d => d.Commands("Familiars: summon and control", c => c
                .Player("familiar list", ".fam l", "Lists the familiars in your active box, numbered.")
                .Player("familiar bind", ".fam b <boxIndex>", "Summons the familiar with that number from your active box.")
                .Player("familiar smartbind", ".fam sb <name>", "Finds a familiar by name in any box and binds it. If several match it asks you to be more specific.")
                .Player("familiar search", ".fam s <name>", "Lists which boxes hold familiars matching a name.")
                .Player("familiar unbind", ".fam ub", "Sends your familiar away. It stays in its box.")
                .Player("familiar toggle", ".fam t", "Calls or dismisses your familiar. Not while in bat form or using Dominating Presence.")
                .Player("familiar togglecombat", ".fam c", "Turns your familiar's combat on or off. Not in PvP or PvE combat.")
                .Player("familiar getlevel", ".fam gl", "Shows your familiar's level, prestige, progress and stats.")
                .Player("familiar emotes", ".fam e", "Turns emote actions (wave, salute, clap, beckon) on or off.")
                .Player("familiar emoteactions", ".fam actions", "Lists the emote actions.")
                .Player("familiar toggleoption", ".fam option <option>", "Turns a familiar setting on or off: Shiny (shiny effects) or VBloodEmotes.")
                .Player("familiar reset", ".fam reset", "Clears a stuck familiar so you can bind again. Refused if your familiar can still be found; unbind it normally instead. Keeps your boxes.")));

            s.Detail("Familiars: boxes", d => d.Commands("Familiars: boxes", c => c
                .Player("familiar listboxes", ".fam boxes", "Lists your boxes.")
                .Player("familiar choosebox", ".fam cb <name>", "Makes a box your active box.")
                .Player("familiar addbox", ".fam ab <name>", "Adds an empty box. Needs at least one unlock; up to 50 boxes.")
                .Player("familiar renamebox", ".fam rb <current> <name>", "Renames a box.")
                .Player("familiar movebox", ".fam mb <name>", "Moves your active familiar to another box. A box holds 10.")
                .Player("familiar deletebox", ".fam db <name>", "Deletes a box. Only empty boxes.")
                .Player("familiar remove", ".fam r <choice>", "Deletes the familiar with that number from your active box. This can't be undone; you would have to unlock it again.")
                .Destructive()));

            s.Detail("Familiars: shiny, prestige and echoes", d => d.Commands("Familiars: shiny, prestige and echoes", c => c
                .Player("familiar shinybuff", ".fam shiny [spellSchool]", "Makes the active familiar shiny with a spell school (blood, storm, unholy, chaos, frost, illusion). Costs 100 Vampiric Dust by default; changing costs 25% of that.")
                .Player("familiar prestige", ".fam pr", "Prestiges the active familiar: at max level, or at once if you carry the schematic cost. No stat argument.")
                .Player("familiar echoes", ".fam echoes <vBlood>", "Buys a V Blood familiar with the Exo reward item. Needs PrimalEchoes and AllowVBloods on.")));

            s.Detail("Familiars: battles", d =>
            {
                d.Callout(CalloutKind.Experimental, "Familiar battles are off by default and may not work since V Rising 1.1.");
                d.Commands("Familiars: battles", c => c
                    .Player("familiar listbattlegroups", ".fam bgs", "Lists your battle groups.")
                    .Player("familiar listbattlegroup", ".fam bg [groupName]", "Shows a battle group, or your active one.")
                    .Player("familiar addbattlegroup", ".fam abg <groupName>", "Creates a battle group.")
                    .Player("familiar choosebattlegroup", ".fam cbg <groupName>", "Makes a battle group active.")
                    .Player("familiar slotbattlegroup", ".fam sbg <groupOrSlot> [slotIndex]", "Puts your active familiar in slot 1, 2 or 3 of a group. With only a slot number, your active group.")
                    .Player("familiar deletebattlegroup", ".fam dbg <groupName>", "Deletes a battle group.")
                    .Destructive()
                    .Player("familiar challenge", ".fam challenge [name]", "Challenges a player. They emote yes or no. With no name, shows your queue."));
            });

            s.Detail("Professions", d => d.Commands("Professions", c => c
                .Player("profession list", ".prof l", "Lists the professions.")
                .Player("profession get", ".prof get <profession>", "Shows your level and progress in a profession.")
                .Player("profession log", ".prof log", "Turns chat messages for profession gains on or off.")));

            s.Detail("Quests", d => d.Commands("Quests", c => c
                .Player("quest progress", ".quest p <questType>", "Shows your daily or weekly quest. Type daily, weekly, d or w. (There is no .quest d or .quest w.)")
                .Player("quest track", ".quest t <questType>", "Gives directions to the nearest target of your daily or weekly quest.")
                .Player("quest reroll", ".quest r <questType>", "Rerolls your daily or weekly quest for the reroll cost (50 Silver Coins by default). Not after you completed it.")
                .Player("quest log", ".quest log", "Turns quest progress messages on or off.")));
        });

        // ------------------------------------------------------------------------------------------- 6. admin
        b.Section(GuideSchema.Admin, false, s =>
        {
            s.Callout(CalloutKind.AdminOnly, "You need to be authed as a server admin. Run <b>adminauth</b> in the F1 console, then press <b>Re-check admin</b> at the top of the Bloodcraft <b>Admin</b> tab. The server checks every admin command itself.");
            s.Callout(CalloutKind.OffByDefault, "Every Bloodcraft system is off until you turn it on in the config file, then restart the server. Commands for a system that is off reply \"... is not enabled\".");
            s.P("Player names in admin commands must match the character name, ignoring case. Offline players work.");

            s.Detail("Reset Bloodcraft progress (Admin tab card)", d =>
            {
                d.P("Bloodcraft has no single reset command. The <b>Reset Bloodcraft progress</b> card in the <b>Admin</b> tab sends the needed admin commands one after another so they act as one reset.");
                d.Steps(
                    "Pick an online player, or type an offline player's exact name.",
                    "Choose the systems: Prestige (28 types), Weapon expertise (16), Blood legacies (10), Professions (8) and Level. All are on by default. The level defaults to 10; match it to your server's StartingLevel.",
                    "Optional, off by default: <b>Re-roll daily and weekly quests</b> and <b>Active familiar to level 1</b> (the player must be online with a familiar out).",
                    "Check the review line, then press <b>Start reset</b> twice to confirm.");
                d.P("By default it sends 63 commands: " + C(".prestige r <player> <type>") + " for all 28 prestige types, " + C(".wep set <player> <weapon> 0") + " for 16 weapons, " + C(".bl set <player> <blood> 0") + " for 10 bloods, " + C(".prof set <player> <profession> 0") + " for 8 professions, then " + C(".lvl set <player> <level>") + ". The options add " + C(".quest rf <player>") + " and " + C(".fam sl <player> 1") + ".");
                d.Bullets(
                    "A prestige type the player never had gets no reply; that counts as nothing to reset.",
                    "If any other step gets no reply, the run pauses: <b>Retry this step</b>, <b>Skip</b> or <b>Stop</b>. Nothing else is sent until you choose. A quest re-roll that got no reply offers <b>Re-roll quests again</b> instead, because the first one may have happened.",
                    "If the server says \"Couldn't find player\" or denies a command, the run pauses with that line shown: <b>Stop</b>, or <b>Continue</b> if the line came from something else you sent.",
                    "Stop works at once, but a command already sent can't be recalled. At the end, <b>Retry failed and unknown</b> lists exactly what it will resend before you confirm. Replies that arrive up to 30 seconds late still update the report.",
                    "The other Admin forms are disabled while a run is going.");
                d.Callout(CalloutKind.Warning, "This can't be undone. It can't reset class or class spell, weapon and blood stat choices (their bonuses drop to 0 with the levels), familiars, quest history or player toggles. The player resets those: " + C(".class c <class>") + " (changes class, costs items), " + C(".wep rst") + " and " + C(".bl rst") + " (one weapon or blood at a time, cost items), and " + C(".fam reset") + " (only clears a stuck familiar).");
            });

            s.Detail("Levels and professions", d => d.Commands("Levels and professions", c => c
                .Admin("level set", ".lvl set <name> <level>", "Sets a player's level, 0 to MaxLevel.")
                .Admin("level ignoresharedexperience", ".lvl ignore <name>", "Adds a player to, or removes them from, the list that never receives shared experience.")
                .Also(".lvl ignoresharedexperience")
                .Admin("profession set", ".prof set <name> <profession> <level>", "Sets a player's profession level, 0 to 100.")));

            s.Detail("Weapons and blood", d => d.Commands("Weapons and blood", c => c
                .Admin("weapon set", ".wep set <name> <weapon> <level>", "Sets a player's expertise in one weapon, 0 to MaxExpertiseLevel. Stat choices are kept.")
                .Admin("blood set", ".bl set <name> <blood> <level>", "Sets a player's legacy in one blood, 0 to MaxBloodLevel. Stat choices are kept.")
                .Admin("weapon setspells", ".wep spell <name> <slot> <ability> [radius]", "Testing tool: puts an ability (prefab number) in unarmed slot 1 or 2. With a radius, applies to every player that close to the named one. Needs UnarmedSlots on.")));

            s.Detail("Prestige", d => d.Commands("Prestige", c => c
                .Admin("prestige set", ".prestige set <name> <prestigeType> <level>", "Sets a player's prestige in one type. Exo needs ExoPrestiging on and a level from 1 to 100.")
                .Admin("prestige reset", ".prestige r <name> <prestigeType>", "Sets one prestige type back to 0. For Experience it removes the prestige buffs; for Exo it clears the form charge. No reply if the player never had that type.")
                .Destructive()
                .Admin("prestige ignoreleaderboard", ".prestige ignore <name>", "Hides a player from the prestige leaderboards, or shows them again. Meant for admin-only accounts.")
                .Also(".prestige ignoreleaderboard")
                .Admin("prestige iacknowledgethiswillremoveallprestigebuffsfromplayersandwantthattohappen", ".prestige iacknowledgethiswillremoveallprestigebuffsfromplayersandwantthattohappen", "Removes the prestige buffs from every player, so you can change PrestigeBuffs in the config. Prestige levels stay; players can reapply buffs with .prestige sb.")
                .Destructive()));

            s.Detail("Familiars", d => d.Commands("Familiars", c => c
                .Admin("familiar setlevel", ".fam sl <name> <level>", "Sets the level of a player's active familiar, 1 to MaxFamiliarLevel. The player must have a familiar out.")
                .Admin("familiar add", ".fam a <name> <unit>", "Testing tool: adds a unit (prefab number or CHAR_ name) to a player's familiar boxes.")
                .Admin("familiar setbattlearena", ".fam sba", "Sets your position as the familiar battle arena centre and starts the battle service. Only one arena. Needs FamiliarBattles on.")));

            s.Detail("Quests and diagnostics", d => d.Commands("Quests and diagnostics", c => c
                .Admin("quest refresh", ".quest rf <name>", "Gives a player new daily and weekly quests. Running it again re-rolls them again.")
                .Admin("quest complete", ".quest c <name> <questTypeName>", "Completes a player's daily or weekly quest (daily, weekly, d or w).")
                .Admin("miscellaneous health", ".misc health", "Shows the server's Bloodcraft startup readiness summary.")));
        });

        // ------------------------------------------------------------------------------------------- 7. config
        b.Section(GuideSchema.Config, false, s =>
        {
            s.ServerSide(
                "<b>" + Cfg + "</b> (every setting below). Player progress is saved as JSON under BepInEx/config/Bloodcraft/; " +
                "don't edit those files while the server runs.",
                "stop the server, edit the file, then start it. Bloodcraft reads it once at startup and has no reload command.",
                "no setting. Bloodcraft has no command that changes its config. Admin commands change a player's progress instead " +
                "(" + C(".lvl set") + ", " + C(".bl set") + ", " + C(".prestige r") + ", " + C(".wep set") + ", " + C(".prof set") +
                ", Raphael's Bloodcraft: Admin tab and Reset Bloodcraft progress).");
            s.P("Settings live in <b>" + Cfg + "</b> on the server. Player data is saved under BepInEx/config/Bloodcraft/. Bloodcraft reads the file at startup, so every change needs a server restart.");
            s.P("Values ending in 0.x are fractions: 0.25 means 25%. Item and unit settings take PrefabGUID numbers; lists are comma-separated.");

            s.Detail("[General]", d => d.Settings("General", Cfg, t => t
                .Row("General", "LanguageLocalization", "English", "Language for item and unit names in replies. Options: Brazilian, English, French, German, Hungarian, Italian, Japanese, Koreana, Latam, Polish, Russian, SimplifiedChinese, Spanish, TraditionalChinese, Thai, Turkish, Vietnamese.", R)
                .Row("General", "Eclipsed", "false", "How often stats are sent to clients like Raphael: true every 0.1 s, false every 2.5 s. Sending itself is on whenever a synced system is on.", R)
                .Row("General", "UseEmberglassEclipseBridge", "false", "Send client stats over the Emberglass mod instead of chat when Emberglass is installed. Players who have the Emberglass client then need Raphael's <b>Use Emberglass bridge</b> setting (experimental) or they see no stats. Players without Emberglass still get chat.", R)
                .Row("General", "ElitePrimalRifts", "false", "Turns on elite primal rifts (stronger primal war events).", R)
                .Row("General", "RiftFrequency", "0", "Primal rifts started per day when they are on. Up to 24.", R)
                .Row("General", "NightmareMode", "false", "PvE-only extra health, power and movement for enemy units.", R)
                .Row("General", "EliteShardBearers", "false", "Makes shard bearers much harder.", R)
                .Row("General", "ShardBearerLevel", "0", "Sets every shard bearer's level when elite shard bearers are on. 0 leaves them unchanged.", R)
                .Row("General", "PotionStacking", "false", "Lets tier 1 and tier 2 potion effects stack.", R)
                .Row("General", "BearFormDash", "false", "Gives bear form a dash.", R)
                .Row("General", "BleedingEdge", "(empty)", "Weapons that get experimental changes, comma-separated: Slashers, Crossbow, Pistols, TwinBlades, Daggers.", R)
                .Row("General", "PrimalArsenal", "false", "Experimental weapons with different models and abilities.", R)
                .Row("General", "PrimalJewelCost", "-77477508", "Item refined at the gem cutter into a random enhanced tier 4 jewel when ExtraRecipes is on (default Demon Fragment).", R)));

            s.Detail("[StarterKit]", d => d.Settings("StarterKit", Cfg, t => t
                .Row("StarterKit", "StarterKit", "false", "Turns on .misc kitme.", R)
                .Row("StarterKit", "KitPrefabs", "862477668,-1531666018,-1593377811,1821405450", "Items in the kit (default Blood Essence, Stone, Wood, Bone).", R)
                .Row("StarterKit", "KitQuantities", "500,1000,1000,250", "Amount of each kit item, in the same order.", R)
                .Row("StarterKit", "KitFamiliar", "0", "Unit given as a familiar with the kit. 0 gives none.", R)));

            s.Detail("[Quests]", d => d.Settings("Quests", Cfg, t => t
                .Row("Quests", "QuestSystem", "false", "Turns on daily and weekly quests (kill, gather and craft).", R)
                .Row("Quests", "InfiniteDailies", "false", "Starts a new daily as soon as one is finished.", R)
                .Row("Quests", "DailyPerfectChance", "0.1", "Chance for a random perfect gem when a daily is completed.", R)
                .Row("Quests", "QuestRewards", "28358550,576389135,-257494203", "Reward item pool (default tier 3 and tier 2 Stygian shards, Crystal).", R)
                .Row("Quests", "QuestRewardAmounts", "50,250,50", "Amount of each reward, in the same order. ×5 for weeklies, ×3 for V Blood kill quests.", R)
                .Row("Quests", "RerollDailyPrefab", "-949672483", "Item paid to reroll a daily (default Silver Coin).", R)
                .Row("Quests", "RerollDailyAmount", "50", "How many to reroll a daily.", R)
                .Row("Quests", "RerollWeeklyPrefab", "-949672483", "Item paid to reroll a weekly (default Silver Coin).", R)
                .Row("Quests", "RerollWeeklyAmount", "50", "How many to reroll a weekly. Not possible once the weekly is done.", R)));

            s.Detail("[Leveling]", d => d.Settings("Leveling", Cfg, t => t
                .Row("Leveling", "LevelingSystem", "false", "Turns on character levels.", R)
                .Row("Leveling", "MaxLevel", "90", "Level cap.", R)
                .Row("Leveling", "StartingLevel", "10", "Level for a player with no saved data.", R)
                .Row("Leveling", "UnitLevelingMultiplier", "7.5", "Experience multiplier for normal units.", R)
                .Row("Leveling", "VBloodLevelingMultiplier", "15", "Experience multiplier for V Bloods.", R)
                .Row("Leveling", "DocileUnitMultiplier", "0.15", "Experience multiplier for docile units.", R)
                .Row("Leveling", "WarEventMultiplier", "0.2", "Experience multiplier for war event spawns.", R)
                .Row("Leveling", "UnitSpawnerMultiplier", "0", "Experience multiplier for unit spawners (vermin nests, tombs). Also applies to familiars.", R)
                .Row("Leveling", "GroupLevelingMultiplier", "1", "Experience multiplier for group kills.", R)
                .Row("Leveling", "LevelScalingMultiplier", "0.05", "How much a big level gap reduces kill experience. Higher is harsher; 0 turns it off.", R)
                .Row("Leveling", "ExpShare", "true", "Shares kill experience with nearby players in range and level range. Also shares familiar unlocks when ShareUnlocks is on. PvP servers: clan only.", R)
                .Row("Leveling", "ExpShareDistance", "25", "Shared experience range, about 5 floor tiles.", R)
                .Row("Leveling", "ExpShareLevelRange", "10", "Largest level gap for shared experience. 0 removes the limit. Prestiged players are exempt.", R)
                .Row("Leveling", "RestedXPSystem", "false", "Rested experience from logging out in a coffin (stone full, wooden half).", R)
                .Row("Leveling", "RestedXPMax", "5", "Most rested experience a player can bank, in levels.", R)
                .Row("Leveling", "RestedXPRate", "0.05", "Share of the maximum banked per tick.", R)
                .Row("Leveling", "RestedXPTickRate", "120", "Minutes per rested tick.", R)));

            s.Detail("[Prestige]", d => d.Settings("Prestige", Cfg, t => t
                .Row("Prestige", "PrestigeSystem", "false", "Turns on prestige. Needs LevelingSystem.", R)
                .Row("Prestige", "MaxLevelingPrestiges", "10", "Most Experience prestiges. Exo needs this many.", R)
                .Row("Prestige", "LevelingPrestigeReducer", "0.05", "Kill experience lost per Experience prestige.", R)
                .Row("Prestige", "PrestigeRateMultiplier", "0.1", "Expertise and legacy gain added per Experience prestige.", R)
                .Row("Prestige", "PrestigeRatesReducer", "0.1", "Gain lost per weapon or blood prestige, for that weapon or blood.", R)
                .Row("Prestige", "PrestigeStatMultiplier", "0.1", "Stat bonus added per weapon or blood prestige.", R)
                .Row("Prestige", "PrestigeBuffs", "1504279833,0,0,0,0,0,0,0,0,0", "Buff granted at each Experience prestige level, one per level; 0 skips. Default: the shroud buff at prestige 1. Run the global buff purge before changing it.", R)
                .Row("Prestige", "PrestigeLevelsToUnlockClassSpells", "0,1,2,3,4,5", "Experience prestige needed for each Shift spell: the default spell, then the class's five.", R)
                .Row("Prestige", "Leaderboard", "true", "Turns on prestige leaderboards.", R)
                .Row("Prestige", "ExoPrestiging", "false", "Turns on Exo prestige (100 levels) after maxed Experience prestige.", R)
                .Row("Prestige", "ExoPrestigeReward", "28358550", "Item paid per Exo prestige, also the currency for .fam echoes (default tier 3 Stygian shard). 0 pays nothing.", R)
                .Row("Prestige", "ExoPrestigeRewardQuantity", "500", "Amount paid per Exo prestige, and the base echoes cost.", R)
                .Row("Prestige", "TrueImmortal", "false", "Gives Immortal blood while in exoform.", R)));

            s.Detail("[Expertise]", d => d.Settings("Expertise", Cfg, t => t
                .Row("Expertise", "ExpertiseSystem", "false", "Turns on weapon expertise.", R)
                .Row("Expertise", "MaxExpertiseLevel", "100", "Expertise level cap.", R)
                .Row("Expertise", "MaxExpertisePrestiges", "10", "Most prestiges per weapon.", R)
                .Row("Expertise", "UnitExpertiseMultiplier", "2", "Expertise multiplier for normal units.", R)
                .Row("Expertise", "VBloodExpertiseMultiplier", "5", "Expertise multiplier for V Bloods.", R)
                .Row("Expertise", "UnitSpawnerExpertiseFactor", "1", "Expertise multiplier for unit spawners (vermin nests, tombs).", R)
                .Row("Expertise", "ExpertiseStatChoices", "3", "Bonus stats per weapon. Clients show at most 3.", R)
                .Row("Expertise", "ResetExpertiseItem", "576389135", "Item paid for .wep rst (default tier 2 Stygian shard). 0 makes it free.", R)
                .Row("Expertise", "ResetExpertiseItemQuantity", "500", "How many for .wep rst.", R)
                .Row("Expertise", "UnarmedSlots", "false", "Extra spells while unarmed (.wep locksp).", R)
                .Row("Expertise", "Duality", "true", "Both unarmed slots (true) or one (false). Needs UnarmedSlots; not for the fishing pole.", R)
                .Row("Expertise", "FishingSlots", "false", "Extra spells while holding the fishing pole.", R)
                .Row("Expertise", "ShiftSlot", "false", "Class spell on Shift (.class shift).", R)
                .Row("Expertise", "MaxHealth", "250", "Base cap for max health.", R)
                .Row("Expertise", "MovementSpeed", "0.25", "Base cap for movement speed.", R)
                .Row("Expertise", "PrimaryAttackSpeed", "0.1", "Base cap for primary attack speed.", R)
                .Row("Expertise", "PhysicalLifeLeech", "0.1", "Base cap for physical life leech.", R)
                .Row("Expertise", "SpellLifeLeech", "0.1", "Base cap for spell life leech.", R)
                .Row("Expertise", "PrimaryLifeLeech", "0.15", "Base cap for primary life leech.", R)
                .Row("Expertise", "PhysicalPower", "20", "Base cap for physical power.", R)
                .Row("Expertise", "SpellPower", "10", "Base cap for spell power.", R)
                .Row("Expertise", "PhysicalCritChance", "0.1", "Base cap for physical crit chance.", R)
                .Row("Expertise", "PhysicalCritDamage", "0.5", "Base cap for physical crit damage.", R)
                .Row("Expertise", "SpellCritChance", "0.1", "Base cap for spell crit chance.", R)
                .Row("Expertise", "SpellCritDamage", "0.5", "Base cap for spell crit damage.", R)));

            s.Detail("[Legacies]", d => d.Settings("Legacies", Cfg, t => t
                .Row("Legacies", "LegacySystem", "false", "Turns on blood legacies.", R)
                .Row("Legacies", "MaxBloodLevel", "100", "Legacy level cap.", R)
                .Row("Legacies", "MaxLegacyPrestiges", "10", "Most prestiges per blood.", R)
                .Row("Legacies", "UnitLegacyMultiplier", "1", "Legacy multiplier for normal units.", R)
                .Row("Legacies", "VBloodLegacyMultiplier", "5", "Legacy multiplier for V Bloods.", R)
                .Row("Legacies", "LegacyStatChoices", "3", "Bonus stats per blood. Clients show at most 3.", R)
                .Row("Legacies", "ResetLegacyItem", "576389135", "Item paid for .bl rst (default tier 2 Stygian shard). 0 makes it free.", R)
                .Row("Legacies", "ResetLegacyItemQuantity", "500", "How many for .bl rst.", R)
                .Row("Legacies", "HealingReceived", "0.15", "Base cap for healing received.", R)
                .Row("Legacies", "DamageReduction", "0.05", "Base cap for damage reduction.", R)
                .Row("Legacies", "PhysicalResistance", "0.1", "Base cap for physical resistance.", R)
                .Row("Legacies", "SpellResistance", "0.1", "Base cap for spell resistance.", R)
                .Row("Legacies", "ResourceYield", "0.25", "Base cap for resource yield.", R)
                .Row("Legacies", "ReducedBloodDrain", "0.5", "Base cap for reduced blood drain.", R)
                .Row("Legacies", "SpellCooldownRecoveryRate", "0.1", "Base cap for spell cooldown recovery.", R)
                .Row("Legacies", "WeaponCooldownRecoveryRate", "0.1", "Base cap for weapon cooldown recovery.", R)
                .Row("Legacies", "UltimateCooldownRecoveryRate", "0.2", "Base cap for ultimate cooldown recovery.", R)
                .Row("Legacies", "MinionDamage", "0.25", "Base cap for minion damage.", R)
                .Row("Legacies", "AbilityAttackSpeed", "0.1", "Base cap for ability attack speed.", R)
                .Row("Legacies", "CorruptionDamageReduction", "0.1", "Base cap for corruption damage reduction.", R)));

            s.Detail("[Professions]", d => d.Settings("Professions", Cfg, t => t
                .Row("Professions", "ProfessionSystem", "false", "Turns on professions.", R)
                .Row("Professions", "ProfessionFactor", "1", "Profession experience multiplier.", R)
                .Row("Professions", "DisabledProfessions", "(empty)", "Professions to turn off, comma-separated (for example Fishing,Alchemy).", R)
                .Row("Professions", "ExtraRecipes", "false", "Adds Bloodcraft's salvage and refining recipes, including Vampiric Dust for shinies. Without it players can't add or change shinies unless dust comes from elsewhere.", R)));

            s.Detail("[Familiars]", d => d.Settings("Familiars", Cfg, t => t
                .Row("Familiars", "FamiliarSystem", "false", "Turns on familiars.", R)
                .Row("Familiars", "MaxFamiliarLevel", "90", "Familiar level cap.", R)
                .Row("Familiars", "UnitFamiliarMultiplier", "7.5", "Familiar experience multiplier for normal units.", R)
                .Row("Familiars", "VBloodFamiliarMultiplier", "15", "Familiar experience multiplier for V Bloods.", R)
                .Row("Familiars", "UnitUnlockChance", "0.05", "Chance a normal unit unlocks as a familiar.", R)
                .Row("Familiars", "VBloodUnlockChance", "0.01", "Chance a V Blood unlocks as a familiar.", R)
                .Row("Familiars", "AllowVBloods", "false", "Lets V Bloods (shard bearers included) be familiars. Exclude single ones with BannedUnits.", R)
                .Row("Familiars", "AllowMinions", "false", "Lets minions be familiars. Some behave badly; at your own risk.", R)
                .Row("Familiars", "BannedUnits", "(empty)", "Units that can't be familiars, as PrefabGUID numbers, comma-separated.", R)
                .Row("Familiars", "BannedTypes", "(empty)", "Unit types that can't be familiars: Human, Undead, Demon, Mechanical, Beast.", R)
                .Row("Familiars", "ShareUnlocks", "false", "Shares unlocks with clan or party members in shared-experience range.", R)
                .Row("Familiars", "FamiliarCombat", "true", "Lets familiars fight.", R)
                .Row("Familiars", "FamiliarPvP", "true", "Lets familiars join PvP. When false they are unbound on entering PvP combat.", R)
                .Row("Familiars", "EquipmentOnly", "false", "Familiars get equipment slots only, without an inventory.", R)
                .Row("Familiars", "FamiliarPrestige", "false", "Turns on familiar prestige (.fam pr).", R)
                .Row("Familiars", "MaxFamiliarPrestiges", "10", "Most prestiges per familiar.", R)
                .Row("Familiars", "FamiliarPrestigeStatMultiplier", "0.1", "Stat gain per familiar prestige.", R)
                .Row("Familiars", "PrestigeCostItemQuantity", "1000", "Schematics that prestige a familiar at once. Clamped to 500-2000.", R)
                .Row("Familiars", "ShinyChance", "0.2", "Chance a new unlock is shiny. A repeat unlock always is.", R)
                .Row("Familiars", "ShinyCostItemQuantity", "100", "Vampiric Dust to make a familiar shiny; 25% of it to change the shiny. Clamped to 50-200.", R)
                .Row("Familiars", "PrimalEchoes", "false", "Turns on .fam echoes (buy V Blood familiars with the Exo reward). Needs Exo prestige and AllowVBloods; respects bans.", R)
                .Row("Familiars", "EchoesFactor", "1", "Multiplies echoes costs. Clamped to 1-4.", R)
                .Row("Familiars", "FamiliarBattles", "false", "Turns on familiar battles. Bloodcraft warns they are most likely broken since V Rising 1.1.", R)));

            s.Detail("[Classes]", d => d.Settings("Classes", Cfg, t => t
                .Row("Classes", "ClassSystem", "false", "Turns on classes.", R)
                .Row("Classes", "ChangeClassItem", "576389135", "Item paid for .class c (default tier 2 Stygian shard). 0 makes it free.", R)
                .Row("Classes", "ChangeClassQuantity", "750", "How many for .class c.", R)
                .Row("Classes", "SynergyMultiplier", "1.5", "Cap multiplier for a class's synergy stats.", R)
                .Row("Classes", "ClassOnHitEffects", "true", "Class spell-school debuffs on hit. If the first is already on the target, the second tier applies.", R)
                .Row("Classes", "OnHitProcChance", "0.075", "Chance per hit for the class effect (also used by shiny familiars).", R)
                .Row("Classes", "DefaultClassSpell", "-433204738", "Shift spell every class can pick as 0 (default Veil of Shadow).", R)
                .Row("Classes", "BloodKnightWeaponSynergies", "MaxHealth,PrimaryAttackSpeed,PrimaryLifeLeech,PhysicalPower", "Blood Knight weapon synergy stats.", R)
                .Row("Classes", "BloodKnightBloodSynergies", "DamageReduction,ReducedBloodDrain,WeaponCooldownRecoveryRate,AbilityAttackSpeed", "Blood Knight blood synergy stats.", R)
                .Row("Classes", "BloodKnightSpells", "-880131926,651613264,2067760264,189403977,375131842", "Blood Knight Shift spells, unlocked by prestige.", R)
                .Row("Classes", "DemonHunterWeaponSynergies", "MovementSpeed,PrimaryAttackSpeed,PhysicalCritChance,PhysicalCritDamage", "Demon Hunter weapon synergy stats.", R)
                .Row("Classes", "DemonHunterBloodSynergies", "PhysicalResistance,ReducedBloodDrain,WeaponCooldownRecoveryRate,MinionDamage", "Demon Hunter blood synergy stats.", R)
                .Row("Classes", "DemonHunterSpells", "-356990326,-987810170,1071205195,1249925269,-914344112", "Demon Hunter Shift spells, unlocked by prestige.", R)
                .Row("Classes", "VampireLordWeaponSynergies", "MaxHealth,SpellLifeLeech,PhysicalPower,SpellPower", "Vampire Lord weapon synergy stats.", R)
                .Row("Classes", "VampireLordBloodSynergies", "DamageReduction,SpellResistance,UltimateCooldownRecoveryRate,CorruptionDamageReduction", "Vampire Lord blood synergy stats.", R)
                .Row("Classes", "VampireLordSpells", "78384915,295045820,-1000260252,91249849,1966330719", "Vampire Lord Shift spells, unlocked by prestige.", R)
                .Row("Classes", "ShadowBladeWeaponSynergies", "MovementSpeed,PrimaryAttackSpeed,PhysicalPower,PhysicalCritDamage", "Shadow Blade weapon synergy stats.", R)
                .Row("Classes", "ShadowBladeBloodSynergies", "SpellResistance,ReducedBloodDrain,WeaponCooldownRecoveryRate,AbilityAttackSpeed", "Shadow Blade blood synergy stats.", R)
                .Row("Classes", "ShadowBladeSpells", "1019568127,1575317901,1112116762,-358319417,1174831223", "Shadow Blade Shift spells, unlocked by prestige.", R)
                .Row("Classes", "ArcaneSorcererWeaponSynergies", "SpellLifeLeech,SpellPower,SpellCritChance,SpellCritDamage", "Arcane Sorcerer weapon synergy stats.", R)
                .Row("Classes", "ArcaneSorcererBloodSynergies", "HealingReceived,SpellCooldownRecoveryRate,UltimateCooldownRecoveryRate,AbilityAttackSpeed", "Arcane Sorcerer blood synergy stats.", R)
                .Row("Classes", "ArcaneSorcererSpells", "247896794,268059675,-242769430,-2053450457,1650878435", "Arcane Sorcerer Shift spells, unlocked by prestige.", R)
                .Row("Classes", "DeathMageWeaponSynergies", "MaxHealth,SpellLifeLeech,SpellPower,SpellCritDamage", "Death Mage weapon synergy stats.", R)
                .Row("Classes", "DeathMageBloodSynergies", "PhysicalResistance,SpellResistance,SpellCooldownRecoveryRate,MinionDamage", "Death Mage blood synergy stats.", R)
                .Row("Classes", "DeathMageSpells", "-1204819086,481411985,1961570821,2138402840,-1781779733", "Death Mage Shift spells, unlocked by prestige.", R)));
        });

        // ------------------------------------------------------------------------------------------- 8. trouble
        b.Section(GuideSchema.Trouble, false, s =>
        {
            s.Detail("The Bloodcraft tabs are greyed out", d =>
            {
                d.P("Raphael lights the group only after the server answers its Bloodcraft handshake. Click the grey <b>Bloodcraft</b> header for the diagnostic panel.");
                d.Bullets(
                    "Press <b>Re-check now</b> after switching servers.",
                    "The server may not run Bloodcraft, or may have only quests or professions on. Those two don't answer the handshake.",
                    "Press <b>Force-enable tabs</b> to use the buttons anyway for this session. Commands work; live readouts stay empty. To keep it, set BloodcraftAvailability=On in Raphael's config.");
            });

            s.Detail("My stats never show up", d =>
            {
                d.Bullets(
                    "If the tabs are grey, see above.",
                    "If you have the <b>Emberglass</b> client mod and the server turned on Bloodcraft's UseEmberglassEclipseBridge, Bloodcraft sends your stats over Emberglass instead of chat. Turn on <b>Use Emberglass bridge</b> in <b>Settings and Help → Connection</b> (Bloodcraft card) and restart the game. The line under it shows whether packets are arriving. Players without Emberglass are unaffected.",
                    "If the Eclipse client mod is installed, Raphael leaves the live stats to Eclipse's HUD. Disable Eclipse to get Raphael's readouts back.",
                    "Weapon expertise only shows the equipped weapon. Swap weapons, or press <b>Refresh</b>.");
                d.Callout(CalloutKind.Experimental, "Raphael's Emberglass receiver is experimental. It is off by default and only takes effect after a game restart.");
            });

            s.Detail("A button gets no reply", d =>
            {
                d.Bullets(
                    "Check chat and the <b>Last server response</b> panel; most replies land there.",
                    "\"... is not enabled\" means the server has that system off.",
                    "Some admin commands reply nothing when there is nothing to change, such as " + C(".prestige r") + " for a type the player never had.",
                    "If nothing at all comes back, the group may be force-enabled on a server without Bloodcraft.");
            });

            s.Detail("A command says \"Invalid ...\" or does the wrong thing", d =>
            {
                d.Bullets(
                    "Check the short form: " + C(".fam c") + ", " + C(".class c") + " and " + C(".quest c") + " are three different commands. See <b>Short forms that look alike</b> under Player commands.",
                    "Quests are " + C(".quest p daily") + " and " + C(".quest p weekly") + "; there is no " + C(".quest d") + ".",
                    C(".fam pr") + " takes no stat argument.",
                    "Put names with spaces in double quotes.");
            });

            s.Detail("\"You've already selected ...\" when picking a class", d =>
                d.P("The <b>Select first class</b> form only makes the first, free pick. To switch, use <b>Change class</b> on the same tab (" + C(".class c <class>") + "); it costs 750 Greater Stygian Shards by default."));

            s.Detail("My familiar is stuck or won't bind", d =>
            {
                d.Bullets(
                    "Only one familiar can be out. Press <b>Unbind</b> (" + C(".fam ub") + ") first, or click the new familiar twice in <b>Boxes</b> to swap.",
                    "If it vanished but the server still thinks it is out, press <b>Force-unbind</b> (" + C(".fam reset") + "). It refuses while the familiar can still be found; call and unbind it normally then.",
                    "You can't call a familiar in bat form or while using Dominating Presence, or toggle its combat during a fight.");
            });

            s.Detail("How do I wipe a player's progress?", d =>
            {
                d.P("Use the <b>Reset Bloodcraft progress</b> card in the Bloodcraft <b>Admin</b> tab. It resets prestige, expertise, legacies, professions and level in one run. See <b>Admin guide</b> for what it can't reset.");
            });

            s.Detail("Admin buttons are disabled", d =>
                d.P("Run <b>adminauth</b> in the F1 console, then press <b>Re-check admin</b> at the top of the Admin tab. While a progress reset is running, the other Admin forms stay disabled until it finishes."));
        });

        b.DefaultsNote("Item names are the defaults' PrefabGUIDs; your server may use other items.");
        list.Add(b.Build());
    }
}
