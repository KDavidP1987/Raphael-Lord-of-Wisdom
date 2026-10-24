using System;
using System.Collections.Generic;

namespace Raphael.Resources;

// 0.71.0: a searchable list of the game's NPC unit prefabs for every field that takes a unit: Nyarlathotep's event
// editor and test spawn, Uriel's Castle Events markers, squads and unit tools.
// 0.74.0: a view over GameCatalog (the generated unit and object reference) instead of its own generated table. The
// tuple shape, the filter and the order are unchanged; tests/Raphael.Logic.Tests pins the 426 names it returned
// through 0.73.0 (Fixtures/UnitCatalogSnapshot.cs). Pure logic: the logic tests link this file.
internal static class UnitCatalog
{
    // Left out, as through 0.73.0: player, critter, "ignored" and faction-less units, and test / dummy units.
    private static readonly HashSet<string> SkipFactions = new(StringComparer.Ordinal) { "Players", "Ignored", "Critters", "None" };
    private static readonly string[] SkipNameParts = { "_Dummy", "_Test", "Tutorial", "_Illusion" };

    private static (string Name, string Faction, int Level, bool VBlood, bool Minion)[] _all;

    /// <summary>(prefab name, faction without the Faction_ prefix, level, V Blood, minion), sorted by name.</summary>
    internal static (string Name, string Faction, int Level, bool VBlood, bool Minion)[] All => _all ??= Build();

    private static (string Name, string Faction, int Level, bool VBlood, bool Minion)[] Build()
    {
        var list = new List<(string Name, string Faction, int Level, bool VBlood, bool Minion)>();
        foreach (var r in GameCatalog.Rows)
        {
            if (r.Kind != CatalogKind.Unit || !r.InternalName.StartsWith("CHAR_", StringComparison.Ordinal)) continue;
            if (SkipFactions.Contains(r.Group)) continue;
            bool skip = false;
            foreach (var k in SkipNameParts) if (r.InternalName.Contains(k, StringComparison.Ordinal)) { skip = true; break; }
            if (skip) continue;
            list.Add((r.InternalName, r.Group, r.Level, r.Has(CatalogFlags.VBlood), r.Has(CatalogFlags.Minion)));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list.ToArray();
    }

    /// <summary>Tests: forget the cached view (after GameCatalog.UseTable).</summary>
    internal static void ResetCache() => _all = null;

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

    internal static string Label((string Name, string Faction, int Level, bool VBlood, bool Minion) u)
    {
        string shortName = u.Name.StartsWith("CHAR_", StringComparison.Ordinal) ? u.Name.Substring(5) : u.Name;
        string tags = u.Faction + (u.Level > 0 ? $" · L{u.Level}" : "") + (u.VBlood ? " · V Blood" : "") + (u.Minion ? " · minion" : "");
        return $"{shortName.Replace('_', ' ')}  <color=#888888>({tags})</color>";
    }
}
