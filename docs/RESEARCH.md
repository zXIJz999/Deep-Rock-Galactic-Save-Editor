# Deep Rock Galactic save: research notes

Status: **unverified**. Everything here comes from public research on the Steam save. Each item
must be confirmed against a real PS4 save before the editor relies on it.

Source: [DRG-Save-Editor](https://github.com/AnthonyMichaelTDM/DRG-Save-Editor) (Python). That
editor does not parse the file. It searches for byte patterns and reads at fixed offsets. The
offsets it uses match a normal UE4 GVAS property layout, which is what this editor parses instead.

## File

- Steam: `<steam install>\steamapps\common\Deep Rock Galactic\FSD\Saved\SaveGames\<steamid>_Player.sav`
- PS4: unknown. File name and count to be confirmed from a decrypted save.
- Format: UE4 GVAS (engine newer than 4.16). This editor keeps the header bytes verbatim.

## Confirmed on a PS4 save (game 1.40, 2026-11-12)

File `ue4savegame.ps4.sav` (plus `sce_sys`), GVAS v2, UE 4.27.2, class `/Script/FSD.FSDSaveGame`.
Reads and writes back byte for byte. Guid structs are named `GUID` (capitals) in this game.

| Field | Where |
|---|---|
| Credits | root `Credits` IntProperty |
| Classes | root `CharacterSaves` array; each item: `SavegameID` (class GUID), `XP`, `TimesRetired` (promotions), `RetiredCharacterLevels`, vanity, loadouts, weapon upgrade loadouts. A 5th item `6E68D5D66FE647458ECEC5462768B128` is unknown |
| Seasons | `SeasonSave.Seasons` Map&lt;Guid, struct&gt;: `XP`, `Tokens` (scrip), challenges, rewards |
| Weapon XP | `WeaponMaintenance.Entries`: `WeaponID`, `XP`, `Level` |
| Enemy kills | root `EnemiesKilled` Map&lt;Guid, Int&gt; |
| Resources, perk points, overclocks | **not present** in a new save. The editor creates `OwnedResources` and `PerkPoints` at the root after `Credits`. Whether the game accepts them there is not tested yet |

## From the game's own data (PS4 pak v11, unencrypted)

`src/DrgSave.Core/items.tsv` is built from the game files with
`DrgSave.Dump --extract <pak> work\pak\out ...` then `--catalog <pak> work\pak\out <out.tsv>`:
2,022 ownable items (schematics, cosmetics, poses, skins, perks, resources, classes, seasons)
with ID, English name, class, Forge category and a DLC flag. All research GUIDs match it.

Field placement, from the field-name lists the game registers (strings in the executable):

| Struct | Fields, in order |
|---|---|
| `ResourcesSave` (root field `Resources`) | `OwnedResources` |
| `SchematicSave` | `bFirstSchematicMessageShown`, `ForgedSchematics`, `OwnedSchematics`, `SkinFixupCounter` |
| `FSDSaveGame` (root) | ... `UnLockedMissions`, `UnLockedMissionParameters`, `UnLockedPlanetZones`, `CampaignSave`, `DeepDiveSave`, `CharacterSaves`, `LastBoughtDailyDealSeed`, `Forging`, `Drinks`, `ItemUpgradeLoadouts`, `bIgnoreRandomLoadout`, `PurchasedItemUpgrades`, `UnlockedItems`, `OwnedItems`, `EnemiesKilled`, `ItemsWithNotification`, `UnlockedItemSkins`, `UnlockedPickaxeParts`, `UnLockedVanityItems`, `UnLockedVanityItemIDs` ... |

Types confirmed from the community-generated headers (DRG-Modding/FSD-Template): `UnlockedItems`,
`OwnedItems`, `PurchasedItemUpgrades`, `UnLockedVanityItemIDs`: TArray<FGuid>. `UnlockedPickaxeParts`,
`Drinks.UnlockedDrinks`: TSet<FGuid>. `UnlockedItemSkins`: TMap<FGuid, FSkinList { TSet<FGuid> Skins }>.
`OwnedPerks.PerkEntries`: TArray<{PerkID, currentRank}> (not written: rank numbering unconfirmed).

## Research on the Steam save (before the PS4 save)

## Fields

| Field | How it is stored (inferred from the byte offsets) |
|---|---|
| Credits | `Credits` IntProperty |
| Perk points | `PerkPoints` IntProperty (missing until the player has some) |
| Resources | `OwnedResources` MapProperty: StructProperty (Guid) key, FloatProperty value |
| Class XP | A Guid-keyed map whose value struct holds `XP`; key = class GUID below |
| Promotions | In the same class record, 108 bytes after XP in the Steam save. Field name unknown |
| Overclocks | `ForgedSchematics` / `OwnedSchematics` arrays of Guid. Not implemented yet |
| Season XP / scrip | Per-season GUID records with `XP` and `Tokens`. Not implemented yet |

GUIDs are in file byte order. The resource and class GUIDs are listed in
`src/DrgSave.Core/DrgKnown.cs`.

## To confirm with the PS4 save

1. The file round-trips byte for byte (`DrgSave.Tests <save>`).
2. `OwnedResources` is read as a Guid map and the GUIDs match the resource names.
3. The class GUIDs find the four class records; the promotion field's name.
4. One edit (credits) shows up in game.
