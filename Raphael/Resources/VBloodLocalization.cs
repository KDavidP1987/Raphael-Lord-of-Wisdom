using System;
using System.Collections.Generic;
using Raphael.Utils;
using Stunlock.Core;         // AssetGuid
using Stunlock.Localization; // Localization.LocalizedStrings

namespace Raphael.Resources;

// 0.65.0 - LANGUAGE-PROOF V-BLOOD CLASSIFICATION.
//
// THE BUG: the V-Blood familiar tab classified familiars by matching their display NAME against
// VBloodRegistry's English name list. Bloodcraft hardcodes *primal* names in English ("Primal X"),
// so primals always matched - but *regular* V-Bloods arrive from the server in the server's
// configured language (e.g. Polish), so they failed the English match and were silently dropped
// from the tab (while "All Familiars", which does no classification, still showed them). See the
// Discord report from Kruczarz on a Polish client.
//
// THE FIX (client-side, language-proof): V Rising's client keeps a live localization table,
// Stunlock.Localization.Localization.LocalizedStrings (AssetGuid -> text), populated in the
// PLAYER'S OWN game language. Because a localized community server is played by clients set to the
// same language, the client-resolved name equals what the server sends over `.fam l`. So we resolve
// each known regular-V-Blood localization key -> the client-language name once the game is up, and
// build a {localizedName -> canonicalEnglishName} lookup the scanner consults as a fallback.
//
// DATA PROVENANCE (all reference-only, mirrored the same way VBloodRegistry.All already was):
//   * regular-V-Blood name -> PrefabGUID: Bloodcraft VBloodNamePrefabGuidMap
//     (LearningMods/Bloodcraft-main/.../Utilities/Familiars.cs)
//   * PrefabGUID -> localization AssetGuid key: LocalizedNameKeys
//     (LearningMods/BloodCraftUI-master/.../Services/PrefabNames.cs)
//   joined offline to (AssetGuid key -> canonical English name) below. 64 REGULAR V-Bloods are here
//   (the original 55 + 9 added in 0.65.1 that Bloodcraft carried via symbolic PrefabGUIDs.CHAR_*
//   constants) - primals still classify via the English "Primal X" path in VBloodScannerService, which
//   already works because Bloodcraft sends those names in English regardless of server language.
//
// Runtime access mirrors BloodCraftUI-master's Extensions.GetLocalizedName pattern.
public static class VBloodLocalization
{
    // (AssetGuid localization key) -> canonical VBloodRegistry name. Keys are the game's localized-
    // string GUIDs; the canonical name is what the rest of the classifier keys off.
    private static readonly Dictionary<string, string> _keyToCanonical =
        new(StringComparer.OrdinalIgnoreCase)
        {
        { "590ff5f7-b82f-47cc-9545-57e2b33996c6", "Adam the Firstborn" },
        { "ba73c4c9-c890-4a32-a589-8e061b011773", "Albert the Duke of Balaton" },
        { "4ec97a89-7b65-4343-8036-a534785882f6", "Alpha the White Wolf" },
        { "59af5689-73bc-49cc-ae20-d893a65ecb2b", "Angram the Purifier" },
        { "de6bdc63-562d-4441-ac1a-9e5456ea4629", "Azariel the Sunbringer" },
        { "6d8b0943-47ab-49a7-8420-46a46fb4624b", "Bane the Shadowblade" },
        { "9c7c1ef0-cf92-412a-adde-053a6018de47", "Baron du Bouchon the Sommelier" },
        { "fb3e731a-933b-4ff8-b5ef-ae1aa49d711b", "Beatrice the Tailor" },
        { "4d645623-80e5-4d9e-b2a2-be0f7ca4b137", "Ben the Old Wanderer" },
        { "460f5eef-29f9-4bab-a9b8-6541b24c99aa", "Christina the Sun Priestess" },
        { "83409624-6b4e-4a09-97cb-5dd633d811e2", "Clive the Firestarter" },
        { "846da9a8-95af-458f-9e07-d504c69917b9", "Cyril the Cursed Smith" },
        { "c5c0bcf8-6a58-457e-ab06-e9e16035b9ad", "Domina the Blade Dancer" },
        { "d4c423dd-de78-4652-8e07-aea40e51f4ec", "Dracula the Immortal King" },
        { "9163ff1b-1784-461c-9867-a2eaecb9e9bb", "Errol the Stonebreaker" },
        { "9afaaaa5-61e2-4132-aae1-2af99c7be5bc", "Finn the Fisherman" },
        { "e3e5021f-a812-495c-83c4-2ead6fe8ddae", "Foulrot the Soultaker" },
        { "a9c38cae-4d12-44dd-b1d8-876b905baae2", "Frostmaw the Mountain Terror" },
        { "d7f5b785-a846-4f9e-be66-a20bf57db6c6", "General Cassius the Betrayer" },
        { "d553ccd2-fb35-49dc-aee6-0a8eb330f124", "General Valencia the Depraved" },
        { "15da3e2d-6b6b-49d4-b386-8e4fddd434b4", "Gorecrusher the Behemoth" },
        { "886eb898-df5a-460d-8525-04251a5ed23d", "Goreswine the Ravager" },
        { "98b27507-db6e-494b-9499-04a896c687da", "Grayson the Armourer" },
        { "3b5a9d1b-1f50-4d07-ad71-0204d54680ba", "Grethel the Glassblower" },
        { "bbcdac34-dbd1-4e96-86c7-02d685972271", "Henry Blackbrew the Doctor" },
        { "a174902c-6321-4470-ac8c-a0f430ad2ca0", "Jade the Vampire Hunter" },
        { "850d90b2-fcac-4988-bbeb-6b9e327ad036", "Keely the Frost Archer" },
        { "7b7b7478-a726-4b8e-a02a-59d839593d4d", "Kodia the Ferocious Bear" },
        { "fefef080-eb6a-49e1-bb2d-ab2b744751a0", "Kriig the Undead General" },
        { "b8977486-8c0f-48dc-b451-dbd0422dd81d", "Leandra the Shadow Priestess" },
        { "033181b8-c4ce-4ea8-bea4-45420555e4b4", "Lidia the Chaos Archer" },
        { "7fee9abd-d1cc-4a3b-b1a6-deb8d3fd3dac", "Lord Styx the Night Champion" },
        { "846528ce-861c-4516-8260-5d8147fd7c1a", "Mairwyn the Elementalist" },
        { "fec53478-9ae5-46d4-92c2-372a693d2e29", "Maja the Dark Savant" },
        { "a2fd6eb4-9553-4fa3-9553-b7e3e3ba06a7", "Matka the Curse Weaver" },
        { "e7644bef-0106-4ee5-9dec-e07daf4737a8", "Meredith the Bright Archer" },
        { "a52b8c47-9562-4119-b3c6-62213e43048b", "Morian the Stormwing Matriarch" },
        { "0404cf9d-9b94-43e2-afa1-c125ecdda2de", "Nicholaus the Fallen" },
        { "198d311b-92db-4b67-9c39-bfdec509b75b", "Octavian the Militia Captain" },
        { "cb492cc0-5523-4271-8c13-05ad5b97bb1c", "Polora the Feywalker" },
        { "3b0d06aa-66f3-4a99-a166-2a8349b14782", "Putrid Rat" },
        { "0d2e3f54-23b5-4ffd-b787-8c7a32235621", "Quincey the Bandit King" },
        { "2fc6d543-b4ef-46fc-acc8-a3cb3378e498", "Raziel the Shepherd" },
        { "69b296b9-b55b-4ef2-98b4-560fc7a32ea6", "Rufus the Foreman" },
        { "7e1a0761-1a1a-4766-9de1-4638156b5970", "Simon Belmont the Vampire Hunter" },
        { "9277b654-8567-49b5-8ec6-1e63aeb0184b", "Sir Magnus the Overseer" },
        { "3578461a-547d-4223-b4d6-1af8491dfdc8", "Solarus the Immaculate" },
        { "a7b2b314-aa8d-44a1-8ee4-c19115f30f05", "Talzur the Winged Horror" },
        { "10b52efc-9c24-4f0e-8dd1-0d83cfe46fc4", "Terah the Geomancer" },
        { "aa7475b0-6842-4c09-95d8-d9a07196510f", "Terrorclaw the Ogre" },
        { "bff27151-9d05-45a5-8438-eda9799414be", "Tristan the Vampire Hunter" },
        { "22ffd45f-259a-47a5-95fe-1d8d7849323d", "Ungora the Spider Queen" },
        { "df33d822-c589-4c09-a91e-ec3e0f28eafd", "Vincent the Frostbringer" },
        { "1f842f41-ebb2-4e69-b985-2f709c3bc9bf", "Voltatia the Power Master" },
        { "53d5aebb-d82b-4a94-a070-2d79d42a5760", "Ziva the Engineer" },
        // 0.65.1 - 9 regular V-Bloods that were in Bloodcraft's familiar name->GUID map (via symbolic
        // PrefabGUIDs.CHAR_* constants) but missing from the original 55 above, so they classified on
        // English clients yet dropped from the V-Blood tab on localized clients. Resolved through the
        // SAME name->PrefabGUID (Bloodcraft VBloodNamePrefabGuidMap) -> AssetGuid (BloodCraftUI
        // LocalizedNameKeys) chain as the 55, and cross-checked for collisions + exact registry names.
        { "c2517a6c-1fa5-4fb5-ace0-951589f1120e", "General Elena the Hollow" },
        { "7f5102ec-fa0d-449d-82c0-b1b29621109d", "Willfred the Village Elder" },
        { "65bd2953-493b-4699-94bd-a537768a2fc4", "Sir Erwin the Gallant Cavalier" },
        { "1d527ada-ec57-4fd8-8409-a857ff7286b0", "Gaius the Cursed Champion" },
        { "a7fabc15-6e38-440e-815c-8220cc5b05d5", "Stavros the Carver" },
        { "5111a5b4-42b2-4230-9018-94f9acf00a16", "Dantos the Forgebinder" },
        { "3e228167-88a2-410d-a13c-c4ca323ff414", "Lucile the Venom Alchemist" },
        { "e3a817b1-c662-4543-ba43-6688f75bcc7d", "Jakira the Shadow Huntress" },
        { "3f1dada3-dbf3-48fe-9bff-6b214a797a30", "Megara the Serpent Queen" },
        };

    // Built lazily once the game's localization table is populated: client-language name -> canonical.
    // Null until a successful build so we keep retrying across the login window (the table isn't ready
    // at Plugin.Load). Case-insensitive so trivial casing differences don't drop a match.
    private static Dictionary<string, string> _localizedToCanonical;

    /// <summary>True once we've resolved at least one localized name from the live game table.</summary>
    public static bool IsReady => _localizedToCanonical != null;

    /// <summary>
    /// Resolve a Bloodcraft-localized in-game V-Blood name (any client language) to its canonical
    /// VBloodRegistry name. Returns false - never throws - when the localization table isn't up yet
    /// or the name isn't a known regular V-Blood. Builds the lookup on first successful call.
    /// </summary>
    public static bool TryResolveLocalized(string ingameName, out string canonical)
    {
        canonical = null;
        if (string.IsNullOrEmpty(ingameName)) return false;
        if (_localizedToCanonical == null && !TryBuild()) return false;
        return _localizedToCanonical.TryGetValue(ingameName.Trim(), out canonical)
               && !string.IsNullOrEmpty(canonical);
    }

    private static bool TryBuild()
    {
        try
        {
            // Live client-side localization table, in the player's own language. Same accessor
            // BloodCraftUI-master uses (Localization.LocalizedStrings). Not ready at Plugin.Load -
            // TypeManager/localization come up later - so a failed/empty build stays uncached and
            // we retry on the next scan.
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _keyToCanonical)
            {
                try
                {
                    if (Localization.LocalizedStrings.TryGetValue(AssetGuid.FromString(kvp.Key), out var localized)
                        && !string.IsNullOrEmpty(localized))
                        map[localized.Trim()] = kvp.Value;
                }
                catch { /* one bad key never aborts the whole build */ }
            }

            if (map.Count == 0) return false; // localization not populated yet - retry later
            _localizedToCanonical = map;
            LogUtils.LogInfo($"VBloodLocalization: resolved {map.Count}/{_keyToCanonical.Count} V-Blood name(s) in the client language.");
            return true;
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"VBloodLocalization.TryBuild: {ex.Message}");
            return false;
        }
    }
}
