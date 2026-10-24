using System.Collections.Generic;

namespace Raphael.Services.Guides;

// 0.73.0: the general Quick Start page (Settings and Help → Quick Start), the first page a new player sees. A General
// guide: free sections, no manifest. It orients and then points at each mod's own guide tab.
internal static partial class Guides
{
    static partial void AddQuickStart(List<Guide> list)
    {
        var b = new GuideBuilder("quickstart", "Welcome to Raphael",
            "A one-minute tour: what Raphael is, how to find your server's mods, and where each guide lives.",
            null, GuideKind.General);

        b.Section("what", "What Raphael is", true, s =>
        {
            s.Bullets(
                "Raphael is a <b>client-side</b> companion for several server-side V Rising mods. It turns each mod's chat " +
                "commands into buttons and forms and adds on-screen overlays, so you can play the mods without " +
                "memorizing commands.",
                "It works with <b>Bloodcraft</b> (leveling, familiars, classes, expertise, legacies, prestige, quests), " +
                "<b>Beelzebub</b> (ability capture and transforms), <b>Uriel</b> (castle storage sharing, prisons, stairs, " +
                "object spawning and Castle Events), <b>Faust</b> (server information and statistics), " +
                "<b>Nyarlathotep</b> (world events and spawn waves) and the <b>Kindred</b> mods (KindredCommands and " +
                "KindredLogistics).",
                "The <b>Game UI</b> tools and the chat window work on any server, even one with no mods.",
                "Raphael only sends the same chat commands you could type yourself. The server decides what is allowed.");
        });

        b.Section("first", "Your first minute", true, s =>
        {
            s.Steps(
                "Open this panel with the <b>Raphael</b> button at the top right of your screen, or with the keybind " +
                "shown in <b>Settings</b>.",
                "Look at the left rail. Raphael detects your server's mods and shows a group for each one it finds, plus " +
                "<b>Game UI</b> and <b>Settings and Help</b>, which are always there.",
                "Open the guide for a mod your server runs. Each guide starts with a short quick start, and everything " +
                "else is folded underneath.",
                "Use the mod's tabs. Hover any control to read what it does in the bar at the bottom of the panel.");
        });

        b.Section("guides", "Where the guides are", false, s =>
        {
            s.P("Every guide is under <b>Settings and Help</b>. Each covers what the mod does, a quick start, how it works, " +
                "Raphael's tabs, every player and admin command, the server settings and troubleshooting.");
            s.Bullets(
                "<b>Bloodcraft Guide</b>, <b>Beelzebub Guide</b>, <b>Uriel Guide</b>, <b>Faust Guide</b>, " +
                "<b>Nyarlathotep Guide</b> and <b>Kindred Guide</b>: one page per mod.",
                "<b>V Rising Basics</b>: the base game's systems and how each mod changes them.",
                "<b>Game Guide</b>: links to community resources for the base game.");
        });

        b.Section("missing", "A mod's tabs are missing or greyed", false, s =>
        {
            s.P("A group appears once Raphael confirms that mod on your server. Right after a server switch it can be greyed " +
                "or missing for a moment.");
            s.Bullets(
                "Click the greyed <b>group header</b>. It opens a small panel with <b>Re-check now</b> (restarts detection) " +
                "and <b>Force-enable</b> (shows the tabs anyway).",
                "Or open <b>Settings and Help → Connection</b> and press the mod's <b>Re-detect</b> button.",
                "Admin tabs stay greyed until you are authed as an admin: type <b>adminauth</b> in the F1 console, then press " +
                "<b>Re-check admin</b> on the tab.");
        });

        b.Section("layout", "Moving things around", false, s =>
        {
            s.Bullets(
                "<b>Left rail:</b> click a group header to fold or unfold it. Groups for detected mods open by themselves.",
                "<b>Top-right buttons:</b> <b>Raphael</b> opens this panel; <b>OV</b> shows or hides all overlays.",
                "<b>Drag and resize:</b> this panel and every overlay move by their title bar and resize from any edge or " +
                "corner. The maximize button toggles full screen.",
                "<b>Footer:</b> toggles the secondary overlays, auto-resize, and the option that stops your typing from " +
                "triggering game keys.",
                "<b>Settings and Help → Settings</b> holds the display, colour and size options. <b>Chat & Diagnostics</b> " +
                "controls what Raphael hides from chat.");
        });

        list.Add(b.Build());
    }
}
