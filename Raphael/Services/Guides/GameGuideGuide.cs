using System.Collections.Generic;

namespace Raphael.Services.Guides;

// 0.73.0: Game Guide (Settings and Help) on the guide kit: links to base-game resources, each row with an Open
// button. Carried over from the old bespoke builder; tests/fixtures/guide-migration/gameguide.json pins the content.
internal static partial class Guides
{
    static partial void AddGameGuide(List<Guide> list)
    {
        var b = new GuideBuilder("gameguide", "Game Guide",
            "Official and community resources for V Rising itself.",
            null, GuideKind.General);

        b.Section("v-rising-quick-reference", "V Rising — quick reference", true, s => s
            .P("Links to resources for V Rising itself: the official homepage, the community wiki, fan-maintained " +
                "guides and the official Discord. Click 'Open' on any row to launch the URL in your default browser. " +
                "For the server mods, use their guides in Settings and Help."));

        b.Section("official", "Official", true, s => s
            .Links(
                ("Game homepage (Stunlock Studios)", "https://playvrising.com")));

        b.Section("community-resources", "Community resources", true, s => s
            .Links(
                ("V Rising Wiki (Fandom)", "https://vrising.fandom.com/wiki/V_Rising_Wiki"),
                ("CaDrift — community guides + tools", "https://www.cadrift.net/v-rising/")));

        b.Section("discord", "Discord", true, s => s
            .Links(
                ("V Rising official Discord", "https://discord.com/invite/vrising")));

        b.Section("suggest-resource", "Suggest a resource", true, s => s
            .P("Have another V Rising guide / map / Discord worth surfacing here? Open an issue on the Raphael " +
                "GitHub (link on the About tab) and a future version can include it. Resources listed here are " +
                "user-suggested — they are not maintained by the mod author and their content / availability may " +
                "change."));

        list.Add(b.Build());
    }
}
