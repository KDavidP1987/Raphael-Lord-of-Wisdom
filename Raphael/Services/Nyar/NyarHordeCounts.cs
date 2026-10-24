using System;
using System.Collections.Generic;
using System.Globalization;

namespace Raphael.Services.Nyar;

// 0.74.0 (friend test: "what controls how many spawn initially versus over time?"): the "How many rise" card at the top
// of each horde editor. It separates the start from the growth and says where each number is set. From Nyarlathotep
// 0.15.0: EventAdmin.HordeMarchLines ("horde of N" = the units' counts, a range at its max), ApocalypseOnlyLines
// (hotspots, roamers per hotspot, hordes), Validation.ParseInfestation (origins × perOrigin), Apocalypse.SpreadOn (a kill
// BY an event unit raises more where the victim fell). CommandArgs.ApocalypseFields / HordeFields: the horde march's units,
// an apocalypse's hotspots, roamers.perHotspot, hordes.firstAfterMinutes and hordes.growth, and every pool of the three
// types, are events.json only (SetPool takes SpawnWaves only). Pure, unit-tested.
internal static class NyarHordeCounts
{
    /// <summary>The card's lines for an apocalypse-type event; empty for any other kind. <paramref name="get"/> reads the
    /// form's current value of a field ("" when unread), so an unsaved edit shows at once.</summary>
    public static List<string> Lines(NyarFormLoad load, Func<string, string> get)
    {
        var o = new List<string>();
        if (load == null || get == null) return o;
        switch (load.Kind)
        {
            case NyarEventKind.Infestation:
            {
                int places = Int(get("action.origins.count")), each = Int(get("action.origins.perOrigin"));
                o.Add("<b>At the start:</b> " + (places > 0 && each > 0
                    ? $"{places} places × {each} undead = {places * each}."
                    : "places × undead at each place.") +
                      " Set both under Where the dead rise.");
                o.Add("<b>Over time:</b> " + Spread(get, "action.spread.") + " Set under Spread per kill.");
                o.Add("<b>Which units:</b> drawn by weight from the event's units, or from the spread's pool. Those live in events.json.");
                break;
            }
            case NyarEventKind.HordeMarch:
                o.Add("<b>At the start:</b> " + (load.HordeSize > 0 ? $"a horde of {load.HordeSize}" : "one horde") +
                      ": the counts of the event's units added up (a pool range counts at its most), plus its bosses.");
                o.Add("<b>To change the starting size:</b> edit the counts in the event's <b>units</b> in events.json, then press " +
                      "Reload from disk. Nyarlathotep has no chat command for it, so Raphael can't set it here.");
                o.Add("<b>On the way:</b> " + Spread(get, "action.growth.") + " Set under Growth on the way.");
                o.Add("<b>Once it infests:</b> " + Spread(get, "action.infest.spread.") + " Set under When it arrives.");
                break;
            case NyarEventKind.Apocalypse:
            {
                int spots = load.ApoHotspots, per = load.ApoPerHotspot;
                o.Add("<b>At the start:</b> " + (spots > 0 && per > 0
                    ? $"{spots} hotspots × {per} roamers = {spots * per}."
                    : "hotspots × roamers at each hotspot.") +
                      " Both are set in events.json (hotspots.count, roamers.perHotspot), then Reload from disk.");
                string max = Text(get("action.roamers.max"));
                o.Add($"<b>Over time:</b> each kill a roamer or horde makes raises more roamers where the victim fell, up to {max} alive " +
                      "(Max roamers, below).");
                int count = Int(get("action.hordes.count")), every = Int(get("action.hordes.everyMinutes")), size = Int(get("action.hordes.size"));
                string first = load.ApoFirstAfter >= 0 ? $"the first after {load.ApoFirstAfter} min" : "the first at the time set in events.json";
                string grow = load.ApoHordeGrowth >= 0 ? $"each later one {load.ApoHordeGrowth} more" : "each later one more by its growth";
                o.Add($"<b>Hordes:</b> {(count > 0 ? count.ToString(CultureInfo.InvariantCulture) : "the")} hordes, {first}, then every " +
                      $"{(every > 0 ? every.ToString(CultureInfo.InvariantCulture) : "?")} min. The first brings {(size > 0 ? size.ToString(CultureInfo.InvariantCulture) : "?")}, " +
                      $"{grow}. Hordes, Horde every and Horde size are set below; the first horde's time and the growth are set in events.json.");
                break;
            }
        }
        return o;
    }

    // "+1-3 per kill the undead make, at most 20 a minute, up to 300 alive."
    private static string Spread(Func<string, string> get, string block)
    {
        string min = Text(get(block + "perKillMin")), max = Text(get(block + "perKillMax"));
        string draw = min == max ? $"+{min}" : $"+{min}-{max}";
        return $"{draw} per kill the undead make, at most {Text(get(block + "perMinute"))} a minute, up to {Text(get(block + "max"))} alive.";
    }

    private static int Int(string s) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
    private static string Text(string s) => string.IsNullOrEmpty(s) ? "?" : s;
}
