# Rename record: BloodCraftHub → Raphael, Lord of Wisdom

**Status: complete.** Raphael's first public release was v0.30.0. This file records what was renamed, what was
deliberately left alone, and why. It replaces the original pre-rename plan.

---

## What changed

| Thing | Before | After |
|---|---|---|
| Display name | BloodCraftHub | **Raphael, Lord of Wisdom** ("Raphael" for short) |
| Thunderstore package | `kdpen/BloodCraftHub` | **`kdpen/Raphael`**, a new listing |
| GitHub repo | `KDavidP1987/BloodCraftHub` | **`KDavidP1987/Raphael-Lord-of-Wisdom`** |
| BepInEx plugin GUID | `kdpen.BloodCraftHub` | **`kdpen.Raphael`** |
| Assembly / DLL / root namespace | `BloodCraftHub` | **`Raphael`** (`Raphael.dll`) |
| Project and solution | `BloodCraftHub/BloodCraftHub.csproj` | `Raphael/Raphael.csproj`, `Raphael.sln` |
| Config file | `kdpen.BloodCraftHub.cfg` | `kdpen.Raphael.cfg` |
| Launcher button | "BCH" | "RUI" (the full name doesn't fit the 40 px button) |
| Release zip | `BloodCraftHub-X.Y.Z.zip` | `Raphael-X.Y.Z.zip` |

The GUID comes from the csproj: `<PackageId>` is `$(Authors).$(AssemblyName)`, so `AssemblyName` is the one
source of truth.

## Settings don't carry over

The new GUID means BepInEx writes a fresh `kdpen.Raphael.cfg`. There is **no importer**: overlay positions,
colors and hotkeys start at their defaults. The README's "Migrating from BloodCraftHub" section tells users
this and asks them to uninstall BloodCraftHub first. The two mods have different GUIDs, so both would load.

## Deliberately not renamed

These are wire-protocol tokens. Other mods recognise Raphael by them, so changing one would break detection
until that server mod changes in step.

- **`bch` in `.beelz api bch on|off`** (`Services/Beelzebub/BeelzClient.cs`). This is how Beelzebub knows
  Raphael is subscribing. Keep it, unless Beelzebub adds a new token and keeps accepting `bch` for a while.
- **The Eclipse protocol sub-type ids** and the `.beelz` / `.uriel` / `.faust` / `.nyar` command verbs.
- **Eclipse detection** keys off Eclipse's own GUID, `io.zfolmt.Eclipse`.
- **`secrets.json`** is the shared HMAC key for the signed Bloodcraft / Eclipse protocol and isn't tied to
  the mod's name. The repo keeps an empty placeholder. Never commit a real key.

## Internal names kept

`BCHubUIManager` (`UI/BCHubUIManager.cs`) and a few `BCH`-prefixed constants keep their old names. Players
never see them, and renaming them would only churn the diff.

## Embedded resources

Embedded resources are named `<RootNamespace>.Resources.*`. The HMAC key loads by the literal name
`Raphael.Resources.secrets.json` (`Resources/SecretManager.cs`). If the root namespace ever changes again,
update that string or the key silently stops loading. `English.json` is embedded too but isn't loaded yet
(see the localization notes in `docs/ROADMAP.md`).

## Legacy BloodCraftHub

BloodCraftHub lives in its own workspace and repo and is not developed here. Never push this repo to the
BloodCraftHub GitHub repo.
