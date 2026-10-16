using System;
using System.Collections.Generic;

namespace Raphael.Resources;

// 0.71.0: a searchable list of the game's NPC unit prefabs (UnitCatalog.Data.cs, generated) for every field that
// takes a unit: Nyarlathotep's event editor and test spawn, Uriel's Castle Events markers, squads and unit tools.
internal static partial class UnitCatalog
{
    /// <summary>Up to <paramref name="max"/> units whose name, faction or "vblood" matches every word typed.
    /// Display is a readable label; Value is the full CHAR_ prefab name.</summary>
    public static List<(string Display, string Value)> Search(string typed, int max = 40)
    {
        var outl = new List<(string, string)>();
        var words = (typed ?? "").Trim().Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return outl;
        foreach (var u in All)
        {
            bool all = true;
            foreach (var w in words)
            {
                if (u.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (u.Faction.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (u.VBlood && "vblood".StartsWith(w.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) continue;
                all = false; break;
            }
            if (!all) continue;
            outl.Add((Label(u), u.Name));
            if (outl.Count >= max) break;
        }
        return outl;
    }

    private static string Label((string Name, string Faction, int Level, bool VBlood, bool Minion) u)
    {
        string shortName = u.Name.StartsWith("CHAR_", StringComparison.Ordinal) ? u.Name.Substring(5) : u.Name;
        string tags = u.Faction + (u.Level > 0 ? $" · L{u.Level}" : "") + (u.VBlood ? " · V Blood" : "") + (u.Minion ? " · minion" : "");
        return $"{shortName.Replace('_', ' ')}  <color=#888888>({tags})</color>";
    }
}
