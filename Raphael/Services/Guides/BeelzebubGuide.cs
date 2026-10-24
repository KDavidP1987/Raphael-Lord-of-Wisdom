using System.Collections.Generic;

namespace Raphael.Services.Guides;

// 0.73.0: the Beelzebub guide (replaces the old "Beelzebub Quick Start" and "Beelzebub Help" pages). Checked against
// Beelzebub 0.137.7 (Commands/*.cs, Config/Settings.cs) and Raphael's Beelzebub tabs (MainPanel.Beelzebub.cs).
internal static partial class Guides
{
    static partial void AddBeelzebub(List<Guide> list)
    {
        static string A(string name) => GuideText.Arg(name);

        const string Cfg = "BepInEx/config/kdpen.Beelzebub.cfg";

        var g = new GuideBuilder("beelzebub", "Beelzebub guide",
            "Capture enemy abilities, build your own action bar and take the forms of the bosses you defeat.",
            "Beelzebub 0.137.7", GuideKind.FullMod, "beelzebub");

        // ------------------------------------------------------------------------------------------- 1. what
        g.Section(GuideSchema.What, true, b =>
        {
            b.Bullets(
                "Every kill can teach you one of the enemy's abilities. A rare <b>Devour</b> teaches a unit's whole kit at once.",
                "Put what you collect on your spell slots (for every weapon, one weapon, a shapeshift form or your horse), " +
                "bind extras to on-screen hotkey buttons, and transform into the bosses you defeat.",
                "Beelzebub (\"Lord of Gluttony\") runs on the <b>server</b>. Raphael is the client UI: the <b>Beelzebub</b> tab " +
                "group appears only when the server has the mod. It is separate from Bloodcraft; a server can run either, both or neither.",
                "Drop chances, limits, transform rules and which abilities can drop are set by the server admin, so your server's " +
                "numbers may differ from the defaults on this page.");
            b.Callout(CalloutKind.Experimental,
                "Beelzebub is a pre-1.0 alpha meant for test servers. Some captured abilities still misbehave when a player casts " +
                "them; the admin can switch any of them off.");
            b.Links(("Beelzebub source and issue tracker", "https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony"));
        });

        // ------------------------------------------------------------------------------------------- 2. quick start
        g.Section(GuideSchema.QuickStart, true, b =>
        {
            b.Steps(
                "Open the <b>Beelzebub</b> tab group. If it is greyed, click its header and press <b>Re-check now</b>.",
                "Kill anything. Each kill rolls for each of the unit's abilities (5% each by default). Chat tells you what you " +
                "captured. <b>Bestiary</b> → <b>My odds</b> shows your current chances.",
                "Open <b>Loadout</b>, pick a set in the <b>Editing</b> dropdown (<b>Universal</b> works on every weapon), then click a " +
                "slot button (LM, Q, Sp, Sh, E, R, C, T) next to a captured ability. It shows on your bar at once.",
                "Want more than the bar holds? Open <b>Hotkeys</b>, press <b>Bind</b> on an ability, then <b>Show Action Bar</b> and " +
                "click its tile to cast it.",
                "Unlocked a boss form? Open <b>Transforms</b> and press <b>Transform</b>. Use <b>Phase</b>, <b>Summon</b> and " +
                "<b>Detonate</b> while in it, and <b>Revert</b> to leave.",
                "Bar looks wrong? On <b>Loadout</b> press <b>Fix bar</b>. If that is not enough, press <b>Reset bar</b>: it puts your " +
                "normal skills back and keeps everything you collected.");
        });

        // ------------------------------------------------------------------------------------------- 3. concepts
        g.Section(GuideSchema.Concepts, false, b =>
        {
            b.Detail("Capturing abilities", d =>
            {
                d.P("When a unit dies, every eligible ability in its kit (all of its phases) rolls on its own. The default chance " +
                    "is 5% per ability for ordinary units and for V Bloods. An ability you already have can roll but adds nothing.");
                d.Bullets(
                    "<b>Who rolls:</b> by default only the killer. An admin can switch to Proximity, where every online player near the kill rolls separately.",
                    "<b>Unit level:</b> units are Low, Mid (level 30+) or High (level 60+) tier, and each tier can scale the chance. All three multipliers are 1.0 by default.",
                    "<b>Filler is skipped:</b> idle, spawn, death and similar stub abilities never drop. Abilities an admin disabled, Blocked or Hidden never drop either.",
                    "<b>Inclusive mode</b> (on by default) ignores the deny lists and the Basic/Brutal gate, so almost every ability in the game can drop. A curated server turns it off.",
                    "Admins can pause all captures server-wide (<b>Freeze on</b>).");
            });
            b.Detail("Bad-luck protection (pity)", d =>
            {
                d.P("Each kill whose rolls give you nothing raises your next chance by 0.25 percentage points. It resets to the base " +
                    "chance the moment something drops. The bonus can climb by up to +100% (so a long dry streak ends in a guaranteed drop).");
                d.Bullets(
                    "Ability captures, Devour and transformation unlocks each keep their own pity, tracked separately for ordinary units and V Bloods.",
                    "Pity is permanent across sessions by default. An admin can make it reset when you log out.",
                    "Press <b>My odds</b> on the Bestiary tab (.beelz odds) to see your current chances and pity bonus.");
            });
            b.Detail("Devour", d =>
            {
                d.P("Devour is the rare jackpot: one roll per kill that, when it hits, gives you every eligible ability of that unit at " +
                    "once. The default chance is 0.25% per kill. If you already knew all of them, chat says so; mute that line with " +
                    "<b>Mute repeat-devour msg</b> on the Settings tab (.beelz silent on).");
                d.P("Devouring a boss gives its abilities, never its form. The form is a separate roll.");
            });
            b.Detail("Transformation unlocks", d =>
            {
                d.P("Only units the game can draw as a player form can be transformed into: <b>Dracula</b>, <b>Morgana</b>, and the test " +
                    "forms <b>Werewolf</b> (Werewolf Chieftain or the common werewolf), <b>Golem</b> (Terah the Geomancer) and " +
                    "<b>Gargoyle</b> (the Tailor). Every other unit is collected for its abilities instead.");
                d.P("Killing one of these units rolls its own transformation chance, separate from captures and Devour. The shipped " +
                    "default is 100% (every kill unlocks the form) so testers get forms reliably; most servers lower it.");
            });
            b.Detail("The bestiary and your 100%", d =>
            {
                d.Bullets(
                    "Your collection counts toward 100% of the server's collectible catalog. <b>Bestiary</b> shows it as a checklist.",
                    "Abilities you captured that are outside the curated catalog show as <b>(off-catalog)</b>. They count as yours but not toward the 100% target. This is common while inclusive mode is on.",
                    "When an admin marks abilities Blocked or Hidden, they leave the catalog and the 100% target shrinks. A lower total after a server update is curation, not lost captures.",
                    "Collecting everything posts a server-wide announcement (on by default). .beelz top shows the leaderboard; admins are left out of it.");
            });
            b.Detail("Slots and keys", d =>
            {
                d.P("Beelzebub uses the eight slots of the V Rising bar. Raphael's slot buttons show the key; turn off <b>Label slot " +
                    "buttons with keys</b> on the Settings tab to see numbers (P, 1-6, U) instead.");
                d.Bullets(
                    "<b>Primary</b> (left mouse, button LM) = slot 0. In chat, type primary.",
                    "<b>Slot 1</b> (Q, button Q).",
                    "<b>Slot 2</b> = Travel (Space, button Sp).",
                    "<b>Slot 3</b> = Shift (button Sh).",
                    "<b>Slot 4</b> = Heavy (E, button E).",
                    "<b>Slot 5</b> = Spell 1 (R, button R).",
                    "<b>Slot 6</b> = Spell 2 (C, button C).",
                    "<b>Ultimate</b> (T, button T) = slot 7. In chat, type ultimate.");
                d.P("Player commands take 1-6, primary or ultimate. Admin slot commands and the transform editor use the numbers 0-7.");
            });
            b.Detail("Ability sets: universal, weapon, form and mounted", d =>
            {
                d.Bullets(
                    "<b>Universal</b> applies on every weapon. It is your baseline.",
                    "A <b>per-weapon</b> set applies only while you hold that weapon family and overrides Universal on the slots it fills. " +
                    "The server switches sets when you swap weapons. You can edit a weapon's set without holding it.",
                    "A <b>per-form</b> set loads when you enter Wolf, Bear, Rat, Spider or Toad from the shapeshift wheel, and your weapon bar " +
                    "returns when you leave. A form has only a few ability slots, so your form binds fill them in slot order. By default a form " +
                    "with no binds keeps its own kit.",
                    "The <b>Mounted</b> set loads while you ride and takes only R, C and T (slots 5, 6 and 7). The horse uses the other keys.",
                    "Empty slots keep your normal abilities. Picking a vanilla spell for a slot in the spellbook (J) hands that slot back to your spell.",
                    "Binds show on the bar at once. Made while you are in a form, transformed or riding, they apply when that ends.");
                d.P("Weapon families: Sword, GreatSword, Axe, Mace, Spear, Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, Claws, " +
                    "Pollaxe, Slashers, TwinBlades, Unarmed and FishingPole. Unarmed is its own family. In chat, auto means the weapon " +
                    "you are holding (or the form you are in).");
                d.P("Use <b>Copy from</b> on the Loadout tab to clone one set onto another, and <b>Clear set</b> to empty the set you are editing.");
            });
            b.Detail("Magic, Weapon and Form abilities", d =>
            {
                d.P("After a catalog scan, abilities are tagged <b>Magic</b> (works on any weapon), <b>Weapon</b> (tied to a weapon family's " +
                    "animation) or <b>Form</b> (only in a specific form). Loadout, Hotkeys and Bestiary can filter and group by these.");
                d.P("A weapon-tied ability shows on the live bar only while you hold its weapon and are not transformed. Chat names the " +
                    "weapon when you grant one. Bind it into that weapon's set, equip the weapon, then press <b>Fix bar</b>. An admin can " +
                    "make the server refuse such abilities in the Universal set.");
            });
            b.Detail("Hotkeys and the action bar overlay", d =>
            {
                d.P("Hotkeys are named extra abilities beyond your bar. Each one becomes a tile on the draggable <b>Beelz Action Bar</b> " +
                    "overlay. Click a tile (or <b>Cast</b> on the Hotkeys tab) to cast it. The ring on a tile is Raphael's estimate of the cooldown.");
                d.Bullets(
                    "They are on-screen buttons, not native ability keys. Press <b>+ key</b> on a hotkey to give it a keyboard shortcut; it casts through chat, so it is convenient rather than frame-perfect, and it does nothing while you type in a text field.",
                    "Cooldowns still apply. The default limit is 5 hotkeys per player.",
                    "Lock and transparency for the overlay are in Raphael's overlay controls.");
            });
            b.Detail("Presets", d =>
            {
                d.P("A preset saves your current slot assignments under a name, so you can swap whole loadouts later. Use the " +
                    "<b>Loadout presets</b> card on the Loadout tab (Save, Load, List) or .beelz preset.");
            });
            b.Detail("Transforms and phases", d =>
            {
                d.Bullets(
                    "Transforming replaces your bar with the boss's kit. A form takes a moment to appear; until it does, Phase and Fix bar reply \"Still transforming\". If a form never finishes, Revert clears it.",
                    "Every form has two or more phases (ability sets). By default you switch them yourself (.beelz phase " + A("n") + "). " +
                    "An admin can make phases advance as you lose health in combat and reset when combat ends.",
                    "Bosses have more abilities than slots. The <b>Customize transform loadout</b> card on the Transforms tab lets you pick which of its abilities sit on each slot, per phase. Reset form returns to the curated kit.",
                    "<b>Summon</b> force-casts the form's signature adds (12 s cooldown by default). <b>Detonate</b> fires its signature area attack (5 s cooldown by default). Not every form has them.",
                    "Default mode is Toggle: you stay transformed until you revert, with no cooldown. An admin can make forms Timed (60 s by default) or Disabled, and add cooldowns.",
                    "To switch forms, revert first. Raphael's Transform button does this for you.",
                    "If you disconnect while transformed, your form and summons are kept for 90 seconds by default and come back if you reconnect in time.");
            });
            b.Detail("Summons", d =>
            {
                d.Bullets(
                    "Captured summon abilities raise allies that fight for you, transformed or not. They match your level by default.",
                    "Each summon ability can have at most 3 live uses at once by default; more casts are refused until some die.",
                    "Summons despawn after 30 seconds by default and are pulled back if they wander more than 30 units away.",
                    "Stash them before a waygate or bat form (<b>Stash summons</b>, or .beelz tp). They come back at your destination, or with <b>Restore</b>.",
                    "Mounting a horse stashes them by default and dismounting restores them.",
                    "Unlocking a form also adds its signature summons to your captures, so you can use them without transforming.");
            });
            b.Detail("Fixing the bar", d =>
            {
                d.P("Three controls on the Loadout tab, from mild to strong:");
                d.Bullets(
                    "<b>Fix bar</b> re-applies your saved binds (.beelz refresh). Use it after reverting, leaving a travel form or a weapon quirk.",
                    "<b>Clear bar</b> (every set) or <b>Clear set</b> (the set you are editing) removes binds. Your captures are kept.",
                    "<b>Reset bar</b> clears every saved bind and every Beelzebub change on the live bar, so your weapon skills return without " +
                    "a weapon swap. It keeps your captures, unlocks, hotkeys, presets and spellbook (J) spells (.beelz resetbar CONFIRM).");
                d.P("Clearing or resetting while riding dismounts you. While transformed, it ends the transform. Still stuck? Ask an " +
                    "admin to press <b>Diagnose bar</b> on the Admin: Players tab.");
            });
            b.Detail("Scanning the catalog", d =>
            {
                d.P("<b>Scan all abilities</b> (Bestiary, Loadout, Admin: Abilities) loads the server's full ability list. It powers the " +
                    "checklist, the extra columns and the Magic/Weapon/Form filter. You do not need it to bind abilities.");
                d.Bullets(
                    "The full scan is heavy (around 1,700 abilities; up to a minute on a big server) but runs in the background and is one-time: Raphael caches it until the server updates Beelzebub.",
                    "In a hurry, pick a slice in <b>Quick scan</b> (Summons, Spells, Projectiles, AoE, Buffs, Travel, Weapon spells, V-Bloods, a form or a weapon) and press <b>Scan</b>. It merges that slice into what you have.",
                    "The Admin: Abilities scan loads every ability, including disabled ones, for configuration.");
            });
            b.Detail("Incompatibility locks", d =>
            {
                d.P("An admin can group abilities that should not be used together and cap how many of the group you may have on your bar " +
                    "and hotkeys at once. A locked bind is never deleted: the slot shows its normal ability until you unslot the other one. " +
                    "Chat names the lock when it blocks a grant. None ship by default.");
            });
            b.Detail("List numbers and ability IDs", d =>
            {
                d.P("Commands that take an ability accept its number in .beelz list (counting from 0) or its stable ability ID. When the " +
                    "number is larger than your list, it is read as an ID. IDs are safer: list numbers shift as you capture more. Turn on " +
                    "<b>Diagnostics</b> on the Settings tab to see IDs in Raphael's tables.");
            });
            b.Detail("Chat messages", d =>
            {
                d.Bullets(
                    "<b>Silent</b>: no capture messages.",
                    "<b>Summary</b> (the default): one line per kill that captured something.",
                    "<b>Verbose</b>: a line per captured ability and per cast confirmation.");
                d.P("Set yours with .beelz verbosity " + A("level") + ". The admin sets the default for new players.");
            });
        });

        // ------------------------------------------------------------------------------------------- 4. tabs
        g.Section(GuideSchema.Tabs, false, b =>
        {
            b.P("The Beelzebub group in the left rail. Admin tabs are visible to everyone, but their controls stay greyed until you are " +
                "authed as an admin, and the server checks permission on every command.");
            b.Tabs(t => t
                .Row("Loadout",
                    "Your ability sets. Pick a set in <b>Editing</b>, click a slot button on a captured ability to bind it, <b>Clear</b> a slot, " +
                    "<b>Clear set</b>, <b>Copy from</b> another set, and save <b>Loadout presets</b>. Top buttons: <b>Fix bar</b>, <b>Clear bar</b>, " +
                    "<b>Reset bar</b>. Search, group and filter the list; <b>Scan all abilities</b> fills in the extra columns.",
                    ".beelz grant / weapon-grant / form-grant <slot token> <index>, .beelz unslot, .beelz refresh, .beelz clearbar, .beelz resetbar CONFIRM, .beelz preset save|load|list")
                .Row("Bestiary",
                    "Your collector's checklist toward 100%. <b>Scan all abilities</b> or <b>Quick scan</b> loads the catalog; filter by " +
                    "Captured / Missing, category and kind, group, and <b>Copy</b> a name. <b>Leaderboard</b> and <b>My odds</b> reply in chat. " +
                    "The <b>Danger zone</b> card's <b>Wipe abilities</b> (type confirm first) deletes all your Beelzebub data.",
                    ".beelz api catalog abilities, .beelz top, .beelz odds, .beelz clear CONFIRM")
                .Row("Hotkeys",
                    "Bind extra abilities beyond the bar (<b>Bind</b>), <b>Cast</b> or <b>Clear</b> them, give each a <b>+ key</b> shortcut, and " +
                    "<b>Show Action Bar</b>. The <b>Summons</b> card: <b>Stash summons</b>, <b>Restore</b>, " +
                    "<b>Clear summons</b> and <b>Show Summons Overlay</b>.",
                    ".beelz hotkey set <name> <index>, .beelz hotkey clear <name>, .beelz cast <name>, .beelz summons stash|restore|clear")
                .Row("Transforms",
                    "Your unlocked forms with <b>Transform</b> and <b>Preview</b>. While transformed: <b>Revert</b>, <b>Phase</b> (switches to the " +
                    "next phase), <b>Summon</b>, <b>Detonate</b>. <b>Customize transform loadout</b> binds a boss ability per phase and slot " +
                    "(<b>Bind here</b>, <b>Clear slot</b>, <b>Reset form</b>). Also shows the server's transform settings and cooldowns, and " +
                    "<b>Show Transforms Overlay</b>.",
                    ".beelz transform <index>, .beelz revert, .beelz phase <n>, .beelz summon, .beelz detonate, .beelz preview <index>, .beelz tform <unit> set|clear|defaults")
                .Row("Settings",
                    "Raphael-side options: <b>Diagnostics</b> (show ability IDs and log every Beelzebub command and reply), connection " +
                    "state, the action bar overlay, <b>Mute repeat-devour msg</b> / <b>Unmute</b>, key labels on slot buttons, and " +
                    "auto-refresh after a grant.",
                    ".beelz silent on|off")
                .Row("Admin: Config",
                    "Every server setting, read live and editable (new settings appear on their own). <b>Announcements</b>: <b>Status</b>, " +
                    "<b>Test</b>, <b>Leaderboard on/off</b>. <b>Capture filters</b>: Deny / Allow by name, by GUID, transform-only, " +
                    "<b>Show rules</b>, <b>Reload rules</b>.",
                    ".beelz api config, .beelz admin set <key> <value>, .beelz admin broadcast ..., .beelz admin deny|allow|denyguid|allowguid|transformonly ..., .beelz admin rules, .beelz admin reload")
                .Row("Admin: Players",
                    "Type a player, unit and ability (searchable), then: <b>Inspect</b>, <b>Progress</b>, <b>Snapshot</b>, <b>Buffs</b>; " +
                    "transforms (<b>Force</b>, <b>Clear</b>, <b>Grant unlock</b>, <b>Revoke unlock</b>); <b>Devour</b>, <b>Give</b>, " +
                    "<b>Revoke</b>; ability shaping; incompatibility locks; reseed; per-unit transform tuning; the recovery ladder " +
                    "(<b>Diagnose bar</b>, <b>Reset player's bar</b>, <b>Purge bar</b>, <b>Respawn</b>, <b>Cleanse stuck buffs</b>, " +
                    "<b>Copy</b> / <b>Paste collection</b>); <b>Freeze on/off</b>, <b>Desummon</b>; and the <b>Danger zone</b>.",
                    ".beelz admin inspect|progress|give|devour|force-transform|bar|reset-loadouts|purge|respawn ... (see Admin guide)")
                .Row("Admin: Abilities",
                    "Mass ability config: <b>Scan all abilities</b> (every ability, enabled or not), filter by name, category, enabled, " +
                    "review status and tier, expand a row, edit, and <b>Save changes</b>. <b>Copy config → clipboard</b> exports every " +
                    "non-default setting.",
                    ".beelz api catalog abilities-all, .beelz admin ability-set <ability> \"(field=value)...\", .beelz admin reload")
                .Row("Overlays",
                    "<b>Beelz Action Bar</b> (a tile per hotkey with a cooldown ring), <b>Summons</b> (stash/restore toggle, recall, clear) " +
                    "and <b>Transforms</b> (double-click a form to transform; Phase 1, Phase 2, Revert). Drag them anywhere; quick toggles " +
                    "also sit in the panel footer.")
                .Row("Settings and Help → Connection",
                    "<b>Re-detect Beelzebub</b> restarts detection. Reachable even when the Beelzebub group is hidden.",
                    ".beelz api version"));
        });

        // ------------------------------------------------------------------------------------------- 5. player
        g.Section(GuideSchema.Player, false, b =>
        {
            b.P("Any player can use these. They act on you. Raphael's tabs send them for you, so you rarely need to type them. " +
                "Replies appear in chat.");

            b.Commands("Help", c => c
                .Player("beelz", ".beelz", "A short overview of the mod and how to start.")
                .Player("beelz help", ".beelz help", "A walkthrough of how Beelzebub works.")
                .Player("beelz commands", ".beelz commands",
                    "Lists every player command. For the other groups: .beelz admin help, .beelz api help, .beelz hotkey help."));

            b.Detail("Collection", d => d.Commands("Collection", c => c
                .Player("beelz list", ".beelz list [arg] [page]",
                    "Your captured abilities and where each is slotted, 15 per page. " + A("arg") + " filters by source: vblood, shard or regular.")
                .Player("beelz search", ".beelz search <term>", "Finds your captures whose ability or unit name contains the text. Up to 25 matches.")
                .Player("beelz info", ".beelz info <arg>",
                    "Full info for one ability: description, school, type, cooldown, cast time and the units it comes from. " +
                    A("arg") + " is a list number, or a name to search every known ability.")
                .Player("beelz bestiary", ".beelz bestiary [page]", "Your collection book: per unit, how many of its abilities you have (X/Y) and whether its form is unlocked.")
                .Player("beelz bestiary unit", ".beelz bestiary unit <name>", "Which of one unit's abilities you have and which you are missing.")
                .Player("beelz progress", ".beelz progress", "Your collection: % of the catalog captured and transforms unlocked.")
                .Player("beelz catalog", ".beelz catalog [page]", "The curated boss list with tier and difficulty, 10 per page, lowest tier first.")
                .Player("beelz top", ".beelz top", "The server leaderboard by abilities collected. Admins are left out. (Bestiary → Leaderboard.)")
                .Player("beelz odds", ".beelz odds", "Your current capture and Devour chances and your pity bonus. (Bestiary → My odds.)")
                .Player("beelz forget", ".beelz forget <index or id>",
                    "Deletes one captured ability and any hotkey bound to it. To get it back you must capture it again.").Destructive()
                .Player("beelz forget-transform", ".beelz forget-transform <index>",
                    "Deletes one transform unlock (number from .beelz transforms). To get it back you must unlock it again.").Destructive()
                .Player("beelz clear", ".beelz clear [confirm]",
                    "Wipes ALL your Beelzebub data: captures, transform unlocks, every bind, hotkey and preset. Cannot be undone. " +
                    "Type CONFIRM as " + A("confirm") + "; without it you only see what would be lost. To fix just the bar, use .beelz resetbar.").Destructive()));

            b.Detail("Action bar and loadouts", d =>
            {
                d.P(A("slot token") + " is 1-6, primary or ultimate. " + A("index or id") + " and " + A("index") + " take a .beelz list " +
                    "number or an ability ID. " + A("weaponStr") + " is a weapon family or auto. " + A("formStr") + " is wolf, bear, rat, " +
                    "spider, toad, mounted or auto.");
                d.Commands("Slots and sets", c => c
                    .Player("beelz grant", ".beelz grant <slot token> <index or id>",
                        "Binds a captured ability to a slot in your Universal set (every weapon). Weapon-tied abilities only appear on their weapon.")
                    .Player("beelz unslot", ".beelz unslot <slot token>", "Clears one slot of your Universal set.").Also(".beelz clear-slot <slot token>")
                    .Player("beelz weapon-grant", ".beelz weapon-grant <weaponStr> <slot token> <index>",
                        "Binds a captured ability to a slot for one weapon family. It overrides Universal on that slot while you hold the weapon.")
                    .Player("beelz weapon-unslot", ".beelz weapon-unslot <weaponStr> <slot token>", "Clears one slot of a weapon set.")
                        .Also(".beelz clear-weapon-slot <weaponStr> <slot token>")
                    .Player("beelz form-grant", ".beelz form-grant <formStr> <slot token> <index>",
                        "Binds a captured ability for a shapeshift form or for riding. It loads when you enter the form. Mounted takes only slots 5, 6 and ultimate.")
                    .Player("beelz form-unslot", ".beelz form-unslot <formStr> <slot token>", "Clears one slot of a form set.")
                    .Player("beelz loadouts", ".beelz loadouts", "Shows your Universal set, each weapon set, and which one is active for the weapon in your hands.")
                    .Player("beelz current", ".beelz current", "What is really on your bar right now, transform-aware. Useful when a bind does not show.")
                    .Player("beelz active", ".beelz active", "Your current bar with full info per slot (name, school, cooldown, description). Handy while transformed."));
                d.Commands("Fixing and resetting", c => c
                    .Player("beelz refresh", ".beelz refresh", "Re-applies your bar. Fixes a blank or wrong bar after reverting, leaving a travel form or a weapon quirk. (Loadout → Fix bar.)")
                    .Player("beelz clearbar", ".beelz clearbar [bucket]",
                        "Clears the binds of one set, no confirmation. " + A("bucket") + ": all (or nothing) = every set, universal, a weapon, or a form. " +
                        "Captures are kept; re-bind any time. (Loadout → Clear bar.)").Destructive()
                    .Player("beelz resetbar", ".beelz resetbar [confirm]",
                        "Puts your bar back to vanilla: ends any transform, removes every bind (universal, weapon and form) and every Beelzebub change on the live bar. " +
                        "Keeps captures, unlocks, hotkeys and presets. Type CONFIRM as " + A("confirm") + ". (Loadout → Reset bar.)").Destructive());
                d.Commands("Presets", c => c
                    .Player("beelz preset save", ".beelz preset save <name>", "Saves your current slot assignments under a name.")
                    .Player("beelz preset load", ".beelz preset load <name>", "Loads a saved preset onto your slots.")
                    .Player("beelz preset list", ".beelz preset list", "Lists your saved presets.")
                    .Player("beelz preset delete", ".beelz preset delete <name>", "Deletes a saved preset. It cannot be restored.").Destructive());
            });

            b.Commands("Hotkeys and casting", c => c
                .Player("beelz hotkey set", ".beelz hotkey set <name> <index or id>",
                    "Binds a captured ability to a named hotkey (a tile on the action bar overlay). Reusing a name replaces it. Limit: 5 per player by default.")
                .Player("beelz hotkey clear", ".beelz hotkey clear <name>", "Removes one hotkey.")
                .Player("beelz hotkey list", ".beelz hotkey list", "Lists your hotkeys.")
                .Player("beelz hotkey help", ".beelz hotkey help", "Lists the hotkey commands.")
                .Player("beelz cast", ".beelz cast <name or index>",
                    "Casts a captured ability now, beyond your bar, respecting its cooldown. Takes a hotkey name or a .beelz list number (not an ID). Raphael's tiles use this."));

            b.Detail("Transforms", d => d.Commands("Transforms", c => c
                .Player("beelz transforms", ".beelz transforms [filter]", "Lists your transform unlocks. " + A("filter") + ": vblood, shard or regular.")
                .Player("beelz transform", ".beelz transform <unit or index>",
                    "Transforms you. Refused if the form is not unlocked, on cooldown, disabled, Brutal-only on a Basic server, or if you are already transformed (revert first).")
                .Player("beelz revert", ".beelz revert", "Ends your transform. Your bar comes back at once.")
                .Player("beelz preview", ".beelz preview <unit or index>", "Shows the abilities a form would give you, per phase, without transforming.")
                .Player("beelz phase", ".beelz phase [n]", "With no number, shows your current and available phases. With a number, switches to that phase.")
                .Player("beelz tform", ".beelz tform <unit> abilities", "Lists the boss's full kit, numbered, for binding.")
                    .Override("action verbs from TransformCommands.cs tform switch")
                .Player("beelz tform", ".beelz tform <unit> set <phase> <slot> <index>",
                    "Puts one of the boss's abilities on a slot (0 = primary, 1-6, 7 = ultimate) for that phase. Updates live if you are in that form and phase.")
                    .Override("action verbs from TransformCommands.cs tform switch")
                .Player("beelz tform", ".beelz tform <unit> clear <phase> <slot>", "Returns one phase slot to the curated default.")
                    .Override("action verbs from TransformCommands.cs tform switch")
                .Player("beelz tform", ".beelz tform <unit> defaults", "Returns the whole form to its curated kit.")
                    .Override("action verbs from TransformCommands.cs tform switch")
                .Player("beelz detonate", ".beelz detonate", "Fires your form's signature area attack, if it has one. 5 s cooldown by default.")
                .Player("beelz summon", ".beelz summon [n]",
                    "Casts your form's signature adds (for example the Toad King's frogs). With no number it lists the options, or casts the only one. 12 s cooldown by default.")));

            b.Commands("Summons", c => c
                .Player("beelz summons", ".beelz summons [action]",
                    "Manages your summons, transformed or not. " + A("action") + ": status (default, shows counts), stash (stow them so you can use waygates; off does the same), " +
                    "restore (bring them back; on does the same), clear (despawn all).")
                .Player("beelz tp", ".beelz tp", "Stashes your summons before a waygate or bat form. They come back at your destination. (Hotkeys → Stash summons.)"));

            b.Commands("Messages", c => c
                .Player("beelz verbosity", ".beelz verbosity <level>", "Sets your chat messages: silent, summary or verbose.")
                .Player("beelz silent", ".beelz silent [state]", "on hides the \"you already knew all of its abilities\" Devour message; off shows it again."));
        });

        // ------------------------------------------------------------------------------------------- 6. admin
        g.Section(GuideSchema.Admin, false, b =>
        {
            b.Detail("Getting admin", d =>
            {
                d.Steps(
                    "The server owner adds your Steam ID to the server's adminlist.txt (vanilla V Rising; usually in VRisingServer_Data/StreamingAssets/Settings).",
                    "In game, press F1 to open the console and type adminauth.",
                    "On any Beelzebub admin tab, press <b>Re-check admin</b>. The controls light up.");
                d.P("Every .beelz admin command is checked by the server; a non-admin gets a rejection. Admin actions are written to the " +
                    "server log as [Beelz AUDIT] lines. Player names match loosely, so check the reply names the right person.");
            });

            b.Detail("Defaults to review on a new server", d =>
            {
                d.Callout(CalloutKind.Warning,
                    "The shipped defaults are tuned for testing: <b>Capture_InclusiveMode</b> is on (almost every ability can drop) and both " +
                    "<b>DropChance_TransformUnlock</b> settings are 1.0 (every transform-boss kill unlocks its form). Lower these for a balanced " +
                    "server. An existing .cfg keeps its values until you change them.");
                d.Callout(CalloutKind.OffByDefault,
                    "Off by default: per-hit damage scaling (Damage_Mode), the periodic leaderboard broadcast, transform-only enforcement " +
                    "(Grant_EnforceTransformOnly), blocking weapon-tied abilities in the Universal set (Grant_EnforceWeaponMatch), filling a " +
                    "form's bar from captures (Forms_AutoFillFromCaptures), the summon counter buff, and verbose logging.");
            });

            b.Detail("Where the server keeps its data", d =>
            {
                d.Bullets(
                    "<b>BepInEx/config/kdpen.Beelzebub.cfg</b>: global settings (see Server configuration).",
                    "<b>BepInEx/config/kdpen.Beelzebub/ability_rules.json</b>: deny/allow lists, transform-only lists, global scaling defaults, " +
                    "per-ability rules (AbilityMap), per-unit transform rules (TransformMap) and drop-rate overrides. Created on first start. " +
                    "The admin commands edit it live; after a hand edit run .beelz admin reload.",
                    "<b>BepInEx/config/kdpen.Beelzebub/ability_metadata_overrides.json</b>: optional display name, description, school, type and category fixes. Hand-edit, then reload.",
                    "<b>BepInEx/config/kdpen.Beelzebub/state.json</b>: every player's captures, unlocks, binds, hotkeys and presets, keyed by Steam ID. Back it up.",
                    "Diagnostic dumps (discovered_abilities.json, inspect_export.csv) are written to the same folder.");
            });

            b.Detail("Ability rules and shaping", d =>
            {
                d.P("Each ability has a rule you can read and change live with .beelz admin ability " + A("ability") + " (name or ID). " +
                    "Changes save to ability_rules.json. Raphael's <b>Ability shaping</b> card (Admin: Players) and the Admin: Abilities table " +
                    "send the same commands.");
                d.Bullets(
                    "<b>Curation:</b> enabled (the hard on/off), weapons and forms (a comma list to allow, !X to exclude, any to clear; for example forms !mounted), " +
                    "transformonly, difficulty, phase, allowdenied, category, notes.",
                    "<b>Review:</b> reviewstatus (Unreviewed, Reviewed, Approved, Blocked, Hidden; Blocked and Hidden stop it dropping and remove it from the catalog), " +
                    "reviewtag, condition (Aimed, CloseRange, Summon, SelfCast, Movement or clear).",
                    "<b>Runtime (player casts only, nothing baked):</b> cooldown, cooldownscale, damagescale, forcetimeout (makes otherwise endless effects expire), " +
                    "powerwindow, summoncap, summontimeout, summonunits, summonpower.",
                    "<b>Baked (edits the ability itself, so the boss that owns it changes too):</b> range, charges, chargetime, aoe, projspeed, leapheight, " +
                    "duration, healing, maxstacks, projcount, knockback, lifetime, casttime, interruptible, interruptonhit, freemove, freelymove, castspeed. " +
                    "Applied while Abilities_ApplyConfig is on (default). Skipped on parts shared with other abilities unless you set allowglobalsharededit.");
                d.P(".beelz admin ability-inspect " + A("ability") + " shows every number behind an ability before you change it. " +
                    ".beelz admin ability " + A("id") + " defaults (or all defaults) returns shaping to the shipped baseline. " +
                    ".beelz admin reseed merge brings the rules file up to a new Beelzebub version's defaults while keeping your edits.");
            });

            b.Detail("Capture filters", d =>
            {
                d.Bullets(
                    "<b>Deny</b> patterns and GUIDs block abilities from dropping. They are ignored while Capture_InclusiveMode is on.",
                    "<b>Allow</b> patterns and GUIDs: when any exist, only matching abilities can drop.",
                    "<b>Transform-only</b> reserves abilities for a real transform. Enforced only when Grant_EnforceTransformOnly is on.",
                    "Patterns match part of the ability's prefab name. All of these save to ability_rules.json.");
            });

            b.Detail("Incompatibility locks", d =>
            {
                d.P("A lock group caps how many of its members one player may have on their bar and hotkeys at once. New groups start at " +
                    "max 1. Members are ability names or IDs, or cat:" + A("Category") + " (Travel, Aoe, Projectile, Summon, Buff, WeaponSpell, " +
                    "Spell, Melee, Other), comma-separated and wrapped in quotes. A locked bind is kept but shows the normal ability until the " +
                    "lock is lifted. Raphael's <b>Incompatibility locks</b> card builds these commands.");
            });

            b.Detail("Grants", d =>
            {
                d.P("Grant commands take the player's name and integer PrefabGUIDs. Raphael's Admin: Players fields search units and abilities " +
                    "by name and fill in the GUID. give and revoke handle one ability; devour grants a unit's whole kit. Transform unlocks " +
                    "work only for the renderable forms. set-slot and set-weapon-slot put an ability straight on a player's bar and skip the " +
                    "normal checks; the reply carries a warning.");
            });

            b.Detail("Player diagnostics and the stuck-bar ladder", d =>
            {
                d.Steps(
                    "<b>Diagnose bar</b> (.beelz admin bar " + A("player") + "): read-only. Shows each slot's ability, saved bind, override buffs and slot changes. Start here.",
                    "<b>Reset player's bar</b> (.beelz admin reset-loadouts " + A("player") + " CONFIRM): the same reset as the player's own Reset bar. Keeps captures, unlocks and hotkeys.",
                    "<b>Purge bar</b> (.beelz admin purge " + A("player") + " CONFIRM): the same reset and clears hotkeys too.",
                    "<b>Respawn</b> (.beelz admin respawn " + A("player") + "): rebuilds the character in place, keeping inventory, progress and binds.");
                d.P("Reset and purge work on offline players: the saved binds clear now and the live bar resets at next login. Other " +
                    "fixes: cleanse for a player stuck invisible or phased, unmount for a player stuck on a summoned mount, desummon for " +
                    "stray summons. The old rebuildslots and clearslotmods are legacy; rebuildbar is now the same as bar.");
            });

            b.Detail("Running with Bloodcraft", d =>
            {
                d.P("Bloodcraft can write slot 3 (its class or shift spell) and slots 1 and 4 (unarmed spells). On a slot both mods claim, the " +
                    "winner depends on load order. Either set <b>Interop_SlotInjectionPriority</b> to 1 so Beelzebub always wins (negative " +
                    "makes Beelzebub yield), or set Bloodcraft's ShiftSlot and UnarmedSlots to false to hand those slots to Beelzebub. While " +
                    "a player is transformed, Beelzebub owns the bar and Bloodcraft's spells return on revert.");
            });

            b.P("Every admin command, grouped. All start with .beelz admin.");

            b.Detail("Help and inspection", d => d.Commands("Help and inspection", c => c
                .Admin("beelz admin help", ".beelz admin help", "Lists every admin command, grouped by purpose.")
                .Admin("beelz admin rules", ".beelz admin rules", "Shows the loaded filter rules and global scaling. (Admin: Config → Show rules.)")
                .Admin("beelz admin inspect", ".beelz admin inspect <player>", "Shows a player's full Beelzebub state.")
                .Admin("beelz admin progress", ".beelz admin progress <player>", "Shows a player's collection progress.")
                .Admin("beelz admin snapshot", ".beelz admin snapshot", "Server summary in chat: player counts, active transforms and more.")
                .Admin("beelz admin scan-abilities", ".beelz admin scan-abilities",
                    "Writes every ability's metadata (shipped, game-derived and your policy) to discovered_abilities.json, so you can find abilities with no curated description.")));

            b.Detail("Ability rules and tuning", d => d.Commands("Ability rules and tuning", c => c
                .Admin("beelz admin ability", ".beelz admin ability <ability> [field] [value] [field2] [value2] [field3] [value3] [field4] [value4] [field5] [value5]",
                    "Sets up to five rule fields of one ability (name or ID) live. Leave out the fields to read its current rule. See Ability rules and shaping for the fields.")
                .Admin("beelz admin ability", ".beelz admin ability <ability> defaults",
                    "Returns one ability's shaping to the shipped baseline. Use all as " + A("ability") + " to reset every ability. Curation (enabled, weapons, forms) is untouched.")
                    .Override("defaults is a keyword in the field position (AdminCommands.cs ability)")
                .Admin("beelz admin ability-set", ".beelz admin ability-set <ability> <configs>",
                    "Sets many fields in one command. Write " + A("configs") + " as \"(cooldown=30)(weapons=sword,axe)\" or \"cooldown=30; range=50\", in quotes when a value has spaces.")
                .Admin("beelz admin ability-inspect", ".beelz admin ability-inspect <ability>",
                    "Read-only: every tunable number in the ability's chain, including damage factors, and which parts it shares. Use export as " + A("ability") + " to write all abilities to inspect_export.csv.")
                .Admin("beelz admin tune", ".beelz admin tune <ability> <knob> <value>", "Sets one shaping field. Use clear as " + A("value") + " to remove it.")
                .Admin("beelz admin tune-list", ".beelz admin tune-list", "Lists every ability with any shaping or tuning set.")
                .Admin("beelz admin default", ".beelz admin default <field> <value>",
                    "Sets the server-wide damagescale or cooldownscale for abilities with no rule of their own.")
                .Admin("beelz admin transform-set", ".beelz admin transform-set <unit> <field> <value>",
                    "Sets a per-form rule live, by prefab name (for example CHAR_Vampire_Dracula_VBlood). Fields: enabled, difficulty, tier, damagescale, " +
                    "cooldownscale, healthscale, speedscale, fullreplace, powerscalingmode, notes, plus per-form duration and cooldown (seconds, or inherit).")
                .Admin("beelz admin reload", ".beelz admin reload", "Re-reads ability_rules.json from disk after a hand edit. No restart.")
                .Admin("beelz admin reseed", ".beelz admin reseed <mode> [confirm]",
                    "Updates ability_rules.json to this build's shipped defaults. " + A("mode") + ": preview (changes nothing), merge (keeps your edits, takes shipped " +
                    "changes you never touched) or replace with CONFIRM (overwrites; your curation is lost). A backup is kept either way.").Destructive()
                .Admin("beelz admin damage-stats", ".beelz admin damage-stats [ability]",
                    "Per-hit damage counters for Damage_Mode, overall or for one ability. Use reset as " + A("ability") + " to zero them.")
                .Admin("beelz admin damage-trace", ".beelz admin damage-trace <onOff>",
                    "Logs every attributed captured-ability hit and the target's health change. Not saved; off after a restart.")));

            b.Detail("Capture filters and gating", d => d.Commands("Capture filters and gating", c => c
                .Admin("beelz admin deny", ".beelz admin deny <pattern>", "Adds a name pattern to the deny list.")
                .Admin("beelz admin undeny", ".beelz admin undeny <pattern>", "Removes a name pattern from the deny list.")
                .Admin("beelz admin allow", ".beelz admin allow <pattern>", "Adds a name pattern to the allow list. While the list has entries, only matching abilities drop.")
                .Admin("beelz admin unallow", ".beelz admin unallow <pattern>", "Removes a name pattern from the allow list.")
                .Admin("beelz admin denyguid", ".beelz admin denyguid <action> <guid>", A("action") + ": add or remove an ability GUID on the deny list.")
                .Admin("beelz admin allowguid", ".beelz admin allowguid <action> <guid>", A("action") + ": add or remove an ability GUID on the allow list.")
                .Admin("beelz admin transformonly", ".beelz admin transformonly <action> <value>",
                    "Adds or removes a transform-only reservation. A number targets the GUID list, text the pattern list. Enforced only when Grant_EnforceTransformOnly is on.")
                .Admin("beelz admin freeze-captures", ".beelz admin freeze-captures [mode]",
                    A("mode") + ": on stops all captures server-wide, off resumes, status shows. Sets CaptureOnKill.")
                .Admin("beelz admin difficulty", ".beelz admin difficulty [mode]",
                    "Shows or sets the server difficulty used for gating: basic or brutal. Brutal-only abilities and forms are blocked on Basic.")));

            b.Commands("Incompatibility locks", c => c
                .Admin("beelz admin lock add", ".beelz admin lock add <group> <members>",
                    "Creates a lock group or adds members. Wrap " + A("members") + " in quotes, comma-separated (names, IDs or cat:Category).")
                .Admin("beelz admin lock max", ".beelz admin lock max <group> <max>", "Sets how many members may be active at once.")
                .Admin("beelz admin lock remove", ".beelz admin lock remove <group> [member]", "Removes one member, or the whole group when no member is given.")
                .Admin("beelz admin lock list", ".beelz admin lock list", "Shows every lock group and its members.")
                .Admin("beelz admin lock check", ".beelz admin lock check <player>", "Shows which of a player's active abilities are locked right now, and why."));

            b.Commands("Transform settings", c => c
                .Admin("beelz admin transform mode", ".beelz admin transform mode <source> <mode>",
                    A("source") + ": regular or vblood. " + A("mode") + ": toggle, timed or disabled.")
                .Admin("beelz admin transform duration", ".beelz admin transform duration <source> <seconds>", "Sets how long a Timed transform lasts.")
                .Admin("beelz admin transform cooldown", ".beelz admin transform cooldown <source> <seconds>", "Sets the cooldown after a transform ends.")
                .Admin("beelz admin transform show", ".beelz admin transform show", "Shows the current transform modes, durations and cooldowns."));

            b.Detail("Grants and bar edits", d => d.Commands("Grants and bar edits", c => c
                .Admin("beelz admin give", ".beelz admin give <player> <unit guid> <ability guid>", "Grants one captured ability. (Admin: Players → Give.)")
                .Admin("beelz admin revoke", ".beelz admin revoke <player> <unit guid> <ability guid> [reason]",
                    "Removes one captured ability. The reason goes to the audit log.").Destructive()
                .Admin("beelz admin devour", ".beelz admin devour <player> <unit guid>", "Grants all of a unit's eligible abilities at once.")
                .Admin("beelz admin give-transform", ".beelz admin give-transform <player> <unit guid>",
                    "Grants a transform unlock. Only renderable forms: Dracula, Morgana, Werewolf Chieftain or basic werewolf, Geomancer (Golem), Tailor (Gargoyle).")
                .Admin("beelz admin revoke-transform", ".beelz admin revoke-transform <player> <unit guid> [reason]", "Removes a transform unlock.").Destructive()
                .Admin("beelz admin force-transform", ".beelz admin force-transform <player> <unit guid>",
                    "Transforms a player now, ignoring unlock and cooldown. If they are already transformed, clear it first.")
                .Admin("beelz admin clear-transform", ".beelz admin clear-transform <player>", "Ends a player's transform.")
                .Admin("beelz admin revert-all", ".beelz admin revert-all", "Ends every active transform on the server.")
                .Admin("beelz admin set-slot", ".beelz admin set-slot <player> <slot> <ability guid>",
                    "Binds a player's Universal slot (0 = primary, 7 = ultimate). Skips the normal checks.")
                .Admin("beelz admin clear-slot", ".beelz admin clear-slot <player> <slot>", "Clears a player's Universal slot.").Also(".beelz admin unslot <player> <slot>")
                .Admin("beelz admin set-weapon-slot", ".beelz admin set-weapon-slot <player> <weaponStr> <slot> <ability guid>", "Binds a player's slot for one weapon family.")
                .Admin("beelz admin clear-weapon-slot", ".beelz admin clear-weapon-slot <player> <weaponStr> <slot>", "Clears a player's weapon slot.")
                    .Also(".beelz admin weapon-unslot <player> <weaponStr> <slot>")));

            b.Detail("Recovery and diagnostics", d => d.Commands("Recovery and diagnostics", c => c
                .Admin("beelz admin bar", ".beelz admin bar [player]",
                    "Read-only per-slot readout of a player's bar (you when no name). Offline: their saved sets, transform record and hotkeys. (Diagnose bar.)")
                .Admin("beelz admin rebuildbar", ".beelz admin rebuildbar [player]", "Same as .beelz admin bar. Kept for old habits.")
                .Admin("beelz admin bar-raw", ".beelz admin bar-raw [player]", "Read-only: logs each slot's raw engine data to LogOutput.log as [Beelz BARRAW] lines.")
                .Admin("beelz admin modleak", ".beelz admin modleak [player]",
                    "Read-only: lists leftover slot helpers for a player (or all) and whether any are stale. These cause the harmless startup \"Couldn't remap old Modification Id\" errors and are cleaned up on their own.")
                .Admin("beelz admin buffs", ".beelz admin buffs [player]", "Logs a player's buffs and which ones override slots, with a chat summary.")
                .Admin("beelz admin dump", ".beelz admin dump <target>",
                    "Logs diagnostics: an ability GUID's full chain, form (your current form buff) or forms (every form buff and how Beelzebub maps it).")
                .Admin("beelz admin reset-loadouts", ".beelz admin reset-loadouts <player> [confirm]",
                    "Resets a player's bar to vanilla: clears every bind and custom transform loadout and ends any transform. Keeps captures, unlocks, hotkeys and presets. Type CONFIRM. Works offline.").Destructive()
                .Admin("beelz admin purge", ".beelz admin purge <player> CONFIRM",
                    "The same reset as reset-loadouts, and clears all hotkeys. Keeps captures, unlocks and presets. Use you for yourself. Works offline.").Destructive()
                    .Override("verb-dispatch signature (optional player and confirm) in AdminCommands.cs Purge")
                .Admin("beelz admin respawn", ".beelz admin respawn [player]",
                    "Respawns a character where they stand. Inventory, equipment, blood, progress and binds are kept, as with dying.")
                .Admin("beelz admin unmount", ".beelz admin unmount [player]",
                    "Frees a player stuck on a summoned mount: strips mount buffs, removes the stray steed and re-applies their binds. Safe to repeat.")
                .Admin("beelz admin cleanse", ".beelz admin cleanse [player] [buff]",
                    "Strips stuck state buffs (invisible, phased, immaterial, corpse, stealth) that survive respawn and relog. Name a buff (part of its name or GUID) to strip only that one.")
                    .Override("verb-dispatch signature (optional player and buff) in AdminCommands.cs Cleanse")
                .Admin("beelz admin rebuildslots", ".beelz admin rebuildslots [player]", "Legacy: re-syncs a player's slots to their base abilities. Use purge instead.")
                .Admin("beelz admin clearslotmods", ".beelz admin clearslotmods [player]", "Legacy: clears orphaned slot changes and rebuilds the slots. Use purge instead.")
                .Admin("beelz admin desummon", ".beelz admin desummon <player>", "Removes every Beelzebub summon following a player, including leftovers from earlier sessions.")
                .Admin("beelz admin desummon-all", ".beelz admin desummon-all", "Removes every Beelzebub summon on the map, whoever owns it.")
                .Admin("beelz admin copy-collection", ".beelz admin copy-collection <player>", "Copies a player's captures and unlocks to the admin clipboard (one clipboard).")
                .Admin("beelz admin paste-collection", ".beelz admin paste-collection <player>",
                    "Adds the clipboard's captures and unlocks to a player. Keeps what they already have.")));

            b.Detail("Server config and broadcasts", d => d.Commands("Server config and broadcasts", c => c
                .Admin("beelz admin set", ".beelz admin set <key> <value>",
                    "Sets any setting from Server configuration by its key. Applies live and saves to the .cfg. (Admin: Config does this for you.)")
                .Admin("beelz admin broadcast", ".beelz admin broadcast status", "Shows the announcement settings.")
                    .Override("verbs from AdminCommands.cs Broadcast switch")
                .Admin("beelz admin broadcast", ".beelz admin broadcast leaderboard <on|off>", "Turns the periodic leaderboard on or off.")
                    .Override("verbs from AdminCommands.cs Broadcast switch")
                .Admin("beelz admin broadcast", ".beelz admin broadcast interval <minutes>", "Minutes between leaderboards (60 = hourly, 1440 = daily).")
                    .Override("verbs from AdminCommands.cs Broadcast switch")
                .Admin("beelz admin broadcast", ".beelz admin broadcast top <n>", "How many players the leaderboard lists, 1-5.")
                    .Override("verbs from AdminCommands.cs Broadcast switch")
                .Admin("beelz admin broadcast", ".beelz admin broadcast complete <on|off>", "Turns the 100%-collection announcement on or off.")
                    .Override("verbs from AdminCommands.cs Broadcast switch")
                .Admin("beelz admin broadcast", ".beelz admin broadcast test", "Sends one leaderboard broadcast now.")
                    .Override("verbs from AdminCommands.cs Broadcast switch")
                .Admin("beelz admin broadcast-msg", ".beelz admin broadcast-msg <pool> [action] [arg1] [arg2]",
                    "Edits a message pool. " + A("pool") + ": complete or leaderboard. " + A("action") + ": list, add \"text\", remove " + A("n") +
                    ", edit " + A("n") + " \"text\". Tokens: %player% (complete), %top% and %count% (leaderboard). A message cannot contain |.")));

            b.Detail("Tests and danger zone", d => d.Commands("Tests and danger zone", c => c
                .Admin("beelz admin testform", ".beelz admin testform <form>", "Test: drops you into wolf or bear form with your loadout's abilities; off leaves it.")
                .Admin("beelz admin testmount", ".beelz admin testmount [on|off] [player]",
                    "Test: spawns a rideable horse by a player to check that riding with Beelzebub abilities is stable. off removes it.")
                    .Override("verb-dispatch signature (mode then player) in AdminCommands.cs TestMount")
                .Admin("beelz admin reset-character", ".beelz admin reset-character <player> [confirm]",
                    "Unbinds and kicks the player so they make a new character next login. The old body stays, unplayable. Their Beelzebub collection is kept. Type CONFIRM-RESET.").Destructive()
                .Admin("beelz admin wipe-all", ".beelz admin wipe-all <confirm token>",
                    "Wipes ALL player data on the server: captures, transforms, binds, hotkeys, presets and cooldowns. Cannot be undone. Type CONFIRM-WIPE.").Destructive()));

            b.Detail("Machine API (Raphael uses these)", d =>
            {
                d.P("Raphael sends these behind the scenes and reads the [BEELZ:*] lines they return. You never need them by hand. Turn on " +
                    "<b>Diagnostics</b> on the Settings tab to see the traffic in the log.");
                d.Commands("Machine API", c => c
                    .Player("beelz api version", ".beelz api version", "The API and plugin version. Raphael's detection handshake.")
                    .Player("beelz api help", ".beelz api help", "Lists the API commands.")
                    .Player("beelz api bch", ".beelz api bch <mode>", "on, off or status: the live event stream for you. Raphael turns it on at load.")
                    .Player("beelz api list", ".beelz api list", "Your captured abilities.")
                    .Player("beelz api slots", ".beelz api slots", "Your slot assignments: universal, weapon and form sets.")
                    .Player("beelz api transforms", ".beelz api transforms", "Your transform unlocks.")
                    .Player("beelz api active", ".beelz api active", "Your active transform, if any.")
                    .Player("beelz api hotkeys", ".beelz api hotkeys", "Your hotkeys.")
                    .Player("beelz api info", ".beelz api info <index>", "Tooltip data for one captured ability by list number.")
                    .Player("beelz api info-guid", ".beelz api info-guid <ability guid>", "Tooltip data for any ability by GUID.")
                    .Player("beelz api progress", ".beelz api progress", "Your collection progress.")
                    .Player("beelz api bestiary", ".beelz api bestiary [page]", "Your collection book, 40 per page.")
                    .Player("beelz api catalog", ".beelz api catalog", "Counts of what is in the curated catalog.")
                    .Player("beelz api catalog units", ".beelz api catalog units [page]", "Every curated transform unit, 40 per page.")
                    .Player("beelz api catalog abilities", ".beelz api catalog abilities [page] [filter] [value]",
                        "The collectible ability catalog, 40 per page. Filters: weapon, cat, unit, form, search, tag, reviewstatus, tier, vblood. (Scan all and Quick scan.)")
                    .Admin("beelz api catalog abilities-all", ".beelz api catalog abilities-all [page] [filter] [value]",
                        "Every ability, enabled or not, for configuration. Same filters. (Admin: Abilities scan.)")
                    .Player("beelz api rules", ".beelz api rules", "The loaded ability rules.")
                    .Player("beelz api config", ".beelz api config", "Every Beelzebub setting. (Admin: Config.)")
                    .Player("beelz api cooldowns", ".beelz api cooldowns", "Your remaining transform cooldowns.")
                    .Player("beelz api transform-config", ".beelz api transform-config", "Transform mode, duration and cooldown per source.")
                    .Player("beelz api tform-kit", ".beelz api tform-kit <unit>", "A form's full bindable kit.")
                    .Player("beelz api tform-binds", ".beelz api tform-binds <unit>", "Your custom loadout for one form and its phase count.")
                    .Player("beelz api locks", ".beelz api locks", "The server's lock groups.")
                    .Player("beelz api verbosity", ".beelz api verbosity", "Your chat message level.")
                    .Admin("beelz api broadcast-msgs", ".beelz api broadcast-msgs <pool>", "A broadcast message pool's messages, numbered for edit and remove."));
            });
        });

        // ------------------------------------------------------------------------------------------- 7. config
        g.Section(GuideSchema.Config, false, b =>
        {
            b.P("All settings live in <b>" + Cfg + "</b> on the server. The file is created on first start.");
            b.Bullets(
                "<b>live</b>: change it with .beelz admin set " + A("key") + " " + A("value") + " or the Admin: Config tab and it applies at once and saves to the file.",
                "Hand edits to the file need a server restart (or a set command) to take effect.",
                "Rules for single abilities and forms are in ability_rules.json, not here (see Admin guide).");

            b.Detail("Capture", d =>
            {
                d.Settings("Capture", Cfg, s => s
                    .Row("Capture", "CaptureOnKill", "true", "Master switch. false = no ability captures from kills. Same as .beelz admin freeze-captures.", "live")
                    .Row("Capture", "Capture_InclusiveMode", "true",
                        "Testing mode: captures and Devour ignore the deny lists and the Basic/Brutal gate, so almost every ability can drop. " +
                        "The junk filter and each ability's enabled switch still apply. Set false for a curated server.", "live")
                    .Row("Capture", "Curation_EnforceReviewStatus", "true",
                        "Abilities marked Blocked or Hidden cannot drop or be granted and leave the catalog, even in inclusive mode. Set false to test a blocked ability.", "live")
                    .Row("Capture", "Grant_EnforceTransformOnly", "false",
                        "true = abilities reserved as transform-only can be used only in a real transform. false = every ability can be slotted.", "live")
                    .Row("Capture", "Capture_ShareCreditMode", "KillerOnly",
                        "Who rolls when a unit dies: KillerOnly, or Proximity (every online player within Capture_ShareCreditRadius rolls separately).", "live")
                    .Row("Capture", "Capture_ShareCreditRadius", "30", "Radius in world units (about metres) for Proximity mode.", "live")
                    .Row("Capture", "Capture_GrantSignatureSummons", "true",
                        "Unlocking a form also adds its signature summons to the player's captures, usable without transforming. Applied to existing unlocks at load. " +
                        "Cast outside the form, the animation may look off and a few scripted summons may do nothing.", null));
            });

            b.Detail("Drop chances", d =>
            {
                d.Settings("Capture.DropChance", Cfg, s => s
                    .Row("Capture.DropChance", "DropChance_Ability_Regular", "0.05", "Chance (0-1) per eligible ability on an ordinary kill. 1.0 = always.", "live")
                    .Row("Capture.DropChance", "DropChance_Ability_VBlood", "0.05", "Chance (0-1) per eligible ability on a V Blood kill.", "live")
                    .Row("Capture.DropChance", "DropChance_Devour_Regular", "0.0025",
                        "Devour chance (0-1) per ordinary kill: the whole kit at once. A fresh file takes its value from the retired DropChance_Transform_Regular.", "live")
                    .Row("Capture.DropChance", "DropChance_Devour_VBlood", "0.0025",
                        "Devour chance (0-1) per V Blood kill. Gives abilities, never the form. A fresh file takes its value from the retired DropChance_Transform_VBlood.", "live")
                    .Row("Capture.DropChance", "DropChance_TransformUnlock_Regular", "1.0",
                        "Chance (0-1) that killing a non-V Blood transform unit (the common werewolf) unlocks its form. Test-friendly default; lower it for a balanced server.", "live")
                    .Row("Capture.DropChance", "DropChance_TransformUnlock_VBlood", "1.0",
                        "Chance (0-1) that killing a transform boss (Dracula, Morgana, Werewolf Chieftain, Geomancer, Tailor) unlocks its form. Test-friendly default " +
                        "(it was 0.0015); lower it for a balanced server.", "live"));
                d.Settings("Capture.Tier", Cfg, s => s
                    .Row("Capture.Tier", "Capture_TierMidThreshold", "30", "Unit level at which a unit moves from Low to Mid tier.", "live")
                    .Row("Capture.Tier", "Capture_TierHighThreshold", "60", "Unit level at which a unit moves from Mid to High tier.", "live")
                    .Row("Capture.Tier", "Capture_TierMultiplier_Low", "1.0", "Drop-chance multiplier for Low-tier units. 1.0 = no change.", "live")
                    .Row("Capture.Tier", "Capture_TierMultiplier_Mid", "1.0", "Drop-chance multiplier for Mid-tier units.", "live")
                    .Row("Capture.Tier", "Capture_TierMultiplier_High", "1.0", "Drop-chance multiplier for High-tier units.", "live"));
            });

            b.Detail("Bad-luck protection", d =>
            {
                d.Settings("Capture.Pity", Cfg, s => s
                    .Row("Capture.Pity", "Capture_PityIncrementPerKill", "0.0025",
                        "Added to the ability-capture chance after each dry kill (0.0025 = +0.25 points), reset on a drop. Ordinary and V Blood tracked separately. 0 = off.", "live")
                    .Row("Capture.Pity", "Capture_PityMaxBonus", "1.0",
                        "Cap on the ability pity bonus. 1.0 = +100% (a long streak ends in a sure drop); 0.5 keeps rare drops rare. 0 = no cap.", "live")
                    .Row("Capture.Pity", "Capture_PityIncrement_Devour", "0.0025",
                        "Pity step for the Devour roll. A fresh file copies Capture_PityIncrementPerKill. 0 = off.", "live")
                    .Row("Capture.Pity", "Capture_PityMax_Devour", "1.0", "Cap on the Devour pity bonus. A fresh file copies Capture_PityMaxBonus. 0 = no cap.", "live")
                    .Row("Capture.Pity", "Capture_PityIncrement_Transform", "0.0025",
                        "Pity step for the transformation-unlock roll. A fresh file copies Capture_PityIncrementPerKill. 0 = off.", "live")
                    .Row("Capture.Pity", "Capture_PityMax_Transform", "1.0", "Cap on the transformation pity bonus. A fresh file copies Capture_PityMaxBonus. 0 = no cap.", "live")
                    .Row("Capture.Pity", "Capture_PitySessionBased", "false",
                        "true = a player's pity resets when they log out. false = pity carries across sessions.", "live"));
            });

            b.Detail("Abilities and damage", d =>
            {
                d.Settings("Abilities", Cfg, s => s
                    .Row("Abilities", "Abilities_ApplyConfig", "true",
                        "Master switch for baked ability edits (range, charges, interrupt, cast speed and so on). The edits are server-wide and change the boss's copy too. " +
                        "Set false only to turn every baked edit off. Applied at server start and on .beelz admin reload.", null)
                    .Row("Abilities", "Grant_EnforceWeaponMatch", "false",
                        "true = refuse a Universal grant of a weapon-tied ability and point the player to .beelz weapon-grant. The weapon hint shows either way.", "live")
                    .Row("Abilities", "Grant_MinimumCooldownSeconds", "0",
                        "Minimum cooldown in seconds for captured abilities cast from a player's bar. Stops spam. Applied after the ability's own cooldown rules; " +
                        "charge-based abilities are not affected. 0 = off.", "live")
                    .Row("Abilities", "Grant_PowerScalingMode", "PlayerScaled",
                        "PlayerScaled: captured abilities scale with the player's own power, as vanilla. Boosted: also multiply by Grant_PowerScalingFactor. Does not affect transforms.", "live")
                    .Row("Abilities", "Grant_PowerScalingFactor", "1.0",
                        "Multiplier used in Boosted mode (1.5 = +50%, 0.75 = -25%). Applied as a short power buff around the cast, so other actions in about 1.5 s are boosted too.", "live"));
                d.Settings("Damage", Cfg, s => s
                    .Row("Damage", "Damage_Mode", "Off",
                        "How damage scaling reaches captured abilities. Off: the short power buff. Telemetry: also count what per-hit scaling would do, changing nothing " +
                        "(see .beelz admin damage-stats). Scale: scale each hit from a captured cast; bosses stay vanilla. Falls back to Telemetry by itself if the game refuses.", "live")
                    .Row("Damage", "Damage_MinFactor", "0", "Floor on the final power factor in Scale mode. 0 = off. Must not exceed Damage_MaxFactor, or both are ignored.", "live")
                    .Row("Damage", "Damage_MaxFactor", "0", "Cap on the final power factor in Scale mode. Tames outlier boss abilities. 0 = off.", "live")
                    .Row("Damage", "Damage_FlatPolicy", "Unchanged", "The flat (non-power) part of a scaled hit: Unchanged or Scaled. Percent-of-health damage is never scaled.", "live")
                    .Row("Damage", "Damage_ProvenanceRetentionSeconds", "120",
                        "How long a cast can still be matched to new damage sources (late projectiles, zones). Minimum 30. Longer uses more memory.", "live")
                    .Row("Damage", "Summon_PowerMode", "OwnerRelative",
                        "OwnerRelative: a summon's power = your power × Transform_SummonPowerFactor × the ability's summonpower; health untouched. Legacy: the unit's own stats × " +
                        "the factor. A server whose .cfg predates this setting is switched to Legacy.", "live"));
            });

            b.Detail("Transformation", d =>
            {
                d.Settings("Transformation", Cfg, s => s
                    .Row("Transformation", "Transform_Enabled", "true", "Master switch for all transforms. false = unlocks still drop but .beelz transform is refused.", "live")
                    .Row("Transformation", "Transform_Mode_Regular", "Toggle",
                        "Mode for non-V Blood forms: Toggle (until revert), Timed (auto-revert after the duration) or Disabled.", "live")
                    .Row("Transformation", "Transform_Mode_VBlood", "Toggle", "Mode for V Blood forms: Toggle, Timed or Disabled.", "live")
                    .Row("Transformation", "Transform_Mode_ShardBoss", "Toggle", "Mode for shard-boss forms: Toggle, Timed or Disabled.", "live")
                    .Row("Transformation", "Transform_DurationSeconds_Regular", "60", "Seconds a Timed non-V Blood transform lasts.", "live")
                    .Row("Transformation", "Transform_DurationSeconds_VBlood", "60", "Seconds a Timed V Blood transform lasts.", "live")
                    .Row("Transformation", "Transform_DurationSeconds_ShardBoss", "60", "Seconds a Timed shard-boss transform lasts.", "live")
                    .Row("Transformation", "Transform_CooldownSeconds_Regular", "0", "Cooldown after a non-V Blood transform ends. 0 = none.", "live")
                    .Row("Transformation", "Transform_CooldownSeconds_VBlood", "0", "Cooldown after a V Blood transform ends. 0 = none.", "live")
                    .Row("Transformation", "Transform_CooldownSeconds_ShardBoss", "0", "Cooldown after a shard-boss transform ends, in its own bucket. 0 = none.", "live")
                    .Row("Transformation", "Transform_CooldownScope", "PerCategory",
                        "How cooldowns are shared: PerCategory (one bucket each for regular, V Blood and shard boss), PerTransformation (each form its own) or Global (one for all).", "live")
                    .Row("Transformation", "Transform_ShardBossNames", "Dracula,Adam,Solarus,Talzur,Winged Horror,Megara,Morgana,Gorecrusher",
                        "Name parts that mark a form as a shard boss (matched against display or prefab name, any case). Shard bosses use the ShardBoss settings.", "live")
                    .Row("Transformation", "Transform_PhaseMode", "Manual",
                        "Manual: players switch phases with .beelz phase. Auto: phases advance as the player loses health in combat (3 phases: at 66% and 33%) and reset when combat ends.", "live")
                    .Row("Transformation", "Transform_PowerScalingMode", "CuratedScales",
                        "Where transform power comes from: CuratedScales (each form's curated scales), PrefabAbsolute (full boss stats), PlayerScaled (the player's own stats, " +
                        "including Bloodcraft bonuses) or PlayerLeveled (boss stats scaled by player level). A form's own rule overrides this.", "live")
                    .Row("Transformation", "Transform_PlayerLeveled_MaxLevel", "90",
                        "Level at which PlayerLeveled gives full boss stats (factor = level / this, capped at 1). Raise it on servers with a higher level cap.", "live")
                    .Row("Transformation", "Transform_ManualDetonateCooldownSeconds", "5", "Cooldown between .beelz detonate uses per player. 0 = none.", "live")
                    .Row("Transformation", "Transform_SummonCooldownSeconds", "12", "Cooldown between .beelz summon uses per player. 0 = none.", "live")
                    .Row("Transformation", "Transform_ReconnectGraceSeconds", "90",
                        "Seconds a disconnected player's transform and summons are kept for a reconnect. 0 = revert at once. -1 = keep until a manual revert.", "live")
                    .Row("Transformation", "Transform_SummonsAreAllies", "true",
                        "Summons spawned by a player's abilities fight for that player and despawn on revert. false leaves spawning to the game; some chained summons then spawn nothing.", "live")
                    .Row("Transformation", "Transform_MaxStacksPerSummonAbility", "3",
                        "Most live uses one summon ability may have per player, transformed or not. Further casts are refused. An ability's own summoncap overrides it. 0 = no cap.", "live")
                    .Row("Transformation", "Transform_SummonLifetimeSeconds", "30",
                        "Seconds before a summon cast's minions despawn. An ability's own summontimeout overrides it. 0 = never.", "live")
                    .Row("Transformation", "Transform_SummonLeashRadius", "30",
                        "Distance (world units) a summon may wander before it is teleported back. 0 = no leash.", "live")
                    .Row("Transformation", "Transform_SummonMatchPlayerLevel", "true", "A summon takes its owner's level when it spawns, so it stays useful.", "live")
                    .Row("Transformation", "Transform_SummonPowerFactor", "1.0",
                        "Multiplier on a summon's power (and health in Legacy mode). 1.5 = +50%.", "live")
                    .Row("Transformation", "Transform_MountedSummonMode", "Stash",
                        "When a transformed player mounts: Stash (summons wait and return on dismount) or Follow (they keep up and keep fighting). Needs Transform_SummonsAreAllies.", "live")
                    .Row("Transformation", "Transform_SummonCounterBuffGuid", "0",
                        "GUID of a buff shown as a live summon counter (its icon and stack count; its effects are stripped). 0 = off.", "live")
                    .Row("Transformation", "Transform_DespawnSummonsOnDisconnect", "true",
                        "Despawn a player's summons when they disconnect. While transformed, Transform_ReconnectGraceSeconds decides instead.", "live")
                    .Row("Transformation", "Transform_DespawnBudgetPerFrame", "5",
                        "Most summons removed per frame during cleanup. The server can crash if about 36 or more are destroyed at once; lower is safer but slower.", "live")
                    .Row("Transformation", "Transform_RealFormWhenAvailable", "true",
                        "Retired: has no effect now. Dracula and Morgana always use their real form.", null)
                    .Row("Transformation", "Transform_NativeShapeshift_Enabled", "false",
                        "Retired: has no effect now. Only the renderable forms transform.", null));
            });

            b.Detail("Forms, hotkeys and other sections", d =>
            {
                d.Settings("Other sections", Cfg, s => s
                    .Row("Forms", "Forms_CustomAbilities_Enabled", "true",
                        "Per-form and mounted sets: entering Wolf or Bear (and the other wheel forms) loads the player's form binds and keeps the form while they cast. " +
                        "false restores stock vanilla forms for everyone.", "live")
                    .Row("Forms", "Forms_AutoFillFromCaptures", "false",
                        "true = a form with no form or universal binds gets the player's first six captures. With it on, captures return on the next form enter even after a bar reset.", "live")
                    .Row("Hotkeys", "Hotkeys_Enabled", "true", "Allows .beelz hotkey commands. false blocks creating hotkeys.", "live")
                    .Row("Hotkeys", "Hotkeys_MaxPerPlayer", "5", "Most hotkeys per player. 0 = none can be stored.", "live")
                    .Row("Interop", "Interop_SlotInjectionPriority", "0",
                        "Priority of Beelzebub's slot binds when another mod writes the same slot (Bloodcraft: slots 1, 3, 4). 0 = tie (load order decides), 1 = Beelzebub wins, negative = Beelzebub yields.", "live")
                    .Row("Notifications", "DefaultVerbosity", "Summary", "Chat level for new players: Silent, Summary or Verbose. Players change theirs with .beelz verbosity.", "live")
                    .Row("Server", "Server_DifficultyMode", "Basic",
                        "Basic or Brutal. On Basic, abilities and forms tagged Brutal (and abilities with _Hard_ in the name) are blocked unless inclusive mode is on. Match your server's difficulty.", "live")
                    .Row("Diagnostics", "VerboseLogging", "false", "Logs every capture and filter decision to BepInEx/LogOutput.log. Noisy; for debugging.", "live"));
            });

            b.Detail("Announcements", d =>
            {
                d.Settings("Announcements", Cfg, s => s
                    .Row("Announcements", "Broadcast_CollectionComplete_Enabled", "true", "Announce to the server when a player collects every ability.", "live")
                    .Row("Announcements", "Broadcast_Leaderboard_Enabled", "false", "Post the top collectors periodically.", "live")
                    .Row("Announcements", "Broadcast_Leaderboard_IntervalMinutes", "60", "Minutes between leaderboards. Minimum 1.", "live")
                    .Row("Announcements", "Broadcast_Leaderboard_TopN", "3", "How many players the leaderboard lists, 1-5.", "live"));
                d.P("<b>Broadcast_CollectionComplete_Messages</b> and <b>Broadcast_Leaderboard_Messages</b> are message pools: several messages " +
                    "separated by |, one picked at random each time. Tokens: %player% in the completion pool; %top% (the ranked list) and %count% " +
                    "(number of collectors) in the leaderboard pool. The shipped defaults are three themed lines each. Edit them one at a time with " +
                    ".beelz admin broadcast-msg (or the editor on the Admin: Config tab) rather than by hand.");
            });

            b.Detail("Retired keys", d =>
            {
                d.P("Beelzebub removes these from the file at startup. They are listed so an old .cfg makes sense.");
                d.Settings("Retired", Cfg, s => s
                    .Row("Capture.DropChance", "DropChance_Transform_Regular", "0.0025", "Renamed to DropChance_Devour_Regular; the value is copied over.", null)
                    .Row("Capture.DropChance", "DropChance_Transform_VBlood", "0.0025", "Renamed to DropChance_Devour_VBlood; the value is copied over.", null)
                    .Row("AbilityTuning", "AbilityTuning_Enabled", "false", "Replaced by Abilities_ApplyConfig, which is on for every server.", null));
            });
        });

        // ------------------------------------------------------------------------------------------- 8. trouble
        g.Section(GuideSchema.Trouble, false, b =>
        {
            b.Detail("The Beelzebub tabs are missing or greyed", d =>
            {
                d.P("They appear only after Raphael confirms Beelzebub on the server, which can lag a few seconds after you switch servers.");
                d.Bullets(
                    "Click the greyed <b>BEELZEBUB</b> header. Press <b>Re-check now</b> to restart detection, or <b>Force-enable tabs</b> to show them anyway for this session.",
                    "Or open <b>Settings and Help → Connection</b> and press <b>Re-detect Beelzebub</b>.",
                    "To always show them, set the Beelzebub tab group to On in <b>Settings and Help → Settings</b> (or BeelzebubAvailability = On in kdpen.Raphael.cfg).",
                    "If detection keeps failing, the server probably does not run Beelzebub.");
            });
            b.Detail("Admin tabs are greyed out", d =>
            {
                d.P("You are not authed as an admin. Press F1, type adminauth, then press <b>Re-check admin</b>. If adminauth fails, " +
                    "your Steam ID is not in the server's adminlist.txt.");
            });
            b.Detail("No reply in chat", d =>
            {
                d.Bullets(
                    "\"Beelzebub not yet initialized\": the server is still loading the world. Wait and try again.",
                    "No reply at all: check that the tab group is detected (above). Turn on <b>Diagnostics</b> on the Settings tab and look for [Beelz] lines in BepInEx/LogOutput.log.",
                    "Your chat level is Silent: capture messages are hidden. Use .beelz verbosity summary.");
            });
            b.Detail("I am not capturing anything", d =>
            {
                d.Bullets(
                    "The chance is per ability and low by default (5%). Check <b>My odds</b>; pity raises it with every dry kill.",
                    "An admin may have paused captures (freeze-captures) or set CaptureOnKill to false.",
                    "An allow list with entries lets only matching abilities drop. Ask the admin to check <b>Show rules</b>.",
                    "With inclusive mode off, the deny lists and the Basic/Brutal gate apply. Blocked and Hidden abilities never drop.",
                    "In Proximity share mode you must be near the kill; in KillerOnly mode only the killer rolls.",
                    "Admins can turn on VerboseLogging to see why each ability was skipped.");
            });
            b.Detail("A bound ability does not show on my bar", d =>
            {
                d.Bullets(
                    "It is weapon-tied: equip its weapon, then press <b>Fix bar</b>. Chat named the weapon when you granted it.",
                    "You were in a form, transformed or riding when you bound it: it applies when that ends.",
                    "You picked a vanilla spell for that slot in the spellbook (J), which took the slot back.",
                    "A lock blocks it: another ability in the same lock group is active. Unslot the other one.",
                    "Another mod (Bloodcraft) claims the slot. Ask the admin about Interop_SlotInjectionPriority.",
                    "Run .beelz current to see what the server thinks is on your bar.");
            });
            b.Detail("My bar is stuck on a creature's or form's abilities", d =>
            {
                d.Steps(
                    "Press <b>Fix bar</b> (.beelz refresh).",
                    "Press <b>Reset bar</b> (.beelz resetbar CONFIRM). It keeps your captures, unlocks and hotkeys.",
                    "Still stuck: ask an admin to run <b>Diagnose bar</b>, then <b>Purge bar</b> or <b>Respawn</b> (see the Admin guide).");
            });
            b.Detail("Transform refused or stuck", d =>
            {
                d.Bullets(
                    "The reply says why: not unlocked, on cooldown, disabled by the admin, Brutal-only on a Basic server, or transforms switched off.",
                    "You are already transformed: revert first. Raphael's <b>Transform</b> button does this for you.",
                    "\"Still transforming\": the form is still appearing. Wait a moment. If it never finishes, press <b>Revert</b>.",
                    "A Timed form reverts on its own after its duration.");
            });
            b.Detail("Summon problems", d =>
            {
                d.Bullets(
                    "\"Summon use limit reached\": the ability has its maximum live uses (3 by default). Wait for some minions to die.",
                    "Summons vanish after 30 seconds by default. That is the server's summon lifetime.",
                    "Cannot use a waygate: stash your summons first (<b>Stash summons</b> or .beelz tp).",
                    "\"You have no active summons\": cast a captured summon ability first.");
            });
            b.Detail("Scan all is slow", d =>
            {
                d.P("The full scan loads about 1,700 abilities and can take up to a minute on a big server. It runs in the background and is " +
                    "cached until the server updates Beelzebub. For a quick look, use <b>Quick scan</b> with one slice.");
            });
            b.Detail("Other messages", d =>
            {
                d.Bullets(
                    "\"Hotkey limit reached\": clear a hotkey, or ask the admin to raise Hotkeys_MaxPerPlayer.",
                    "\"Hotkey bindings are disabled by the server admin\": Hotkeys_Enabled is false.",
                    "Collection total dropped after a server update: the admin curated abilities out of the catalog. Your captures are not lost.",
                    "A player is stuck invisible or phased after an ability: an admin runs Cleanse stuck buffs.",
                    "A player is stuck on a summoned mount: an admin runs .beelz admin unmount.",
                    "\"Couldn't remap old Modification Id\" in the server log at startup: harmless leftovers, cleaned up on their own.");
            });
            b.Callout(CalloutKind.Warning,
                "<b>Wipe abilities</b> (Bestiary danger zone) and .beelz clear CONFIRM delete everything, transform unlocks included, and " +
                "cannot be undone. To fix the bar, use <b>Reset bar</b> instead.");
        });

        g.DefaultsNote();
        list.Add(g.Build());
    }
}
