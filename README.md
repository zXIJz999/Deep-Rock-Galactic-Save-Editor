# Deep Rock Galactic Save Editor

**Made by zXIJz**

A free, easy save editor for **Deep Rock Galactic** saves: decrypted PS4 saves, and PC saves.

> **This editor is free.** If you paid for it, or paid someone for an edited save, you were
> ripped off.

Tested in game on PS4: credits, resources, class level and cosmetic unlocks all show up.

## Warning

Deep Rock Galactic has online co-op. You use edited saves at your own risk. The editor only
changes your own save file. It does not decrypt or encrypt PS4 saves: use your own save tools
for that. **Paid DLC items are never unlocked.**

## How to use it

1. Export your save from the PS4 and **decrypt it** with your save tool. The file is
   `ue4savegame.ps4.sav`.
2. Run `DeepRockGalacticSaveEditor.exe`, click **Open a file...** and pick the save.
   On PC, **Open Steam save** finds it for you.
3. Change what you want.
4. Click **Save**. Your old save is kept next to the new one as `<name>.<date-time>.bak`.
5. On PS4, **re-encrypt** the save and import it back.

Big cosmetic lists (headwear) can take a while to appear in game the first time: the game draws
every icon.

## Use it in your browser (no download)

Open the **web editor** on this repository's GitHub Pages site: `https://<owner>.github.io/<repo>/`
(the link is in the repository's About box). It does everything the exe does and works on any
computer with a modern browser. It runs entirely on your own PC: your save is **never
uploaded**. **Save (download)** gives you the edited file with the same name; your original
file is not changed, so it is your backup.

## The pages

| Page | What you can change |
|---|---|
| **Resources** | Credits, perk points, minerals, brewing ingredients, Error Cubes, Blank Matrix Cores, Data Cells, Phazyonite. A **Max** button next to each |
| **Dwarves** | Each class's XP and promotions |
| **Seasons** | Season XP and scrip for every season in the save |
| **Unlocks** | Overclocks and Forge cosmetics (given as schematics, you forge them in game), armour, headwear, beards, paints, victory poses, weapons and tools, all weapon upgrades, weapon paint jobs and frameworks, pickaxe parts, Abyss Bar drinks |
| **Advanced** | Every field in the save as a tree, and the activity log |
| **About** | Credits and links |

Perks are not unlocked directly: give yourself perk points and claim them in game.

## Questions

**Windows says the file is unsafe.** The exe is not signed. Click "More info", then "Run
anyway", or build it yourself from this source.

**Something does not show up in game.** Tell me which unlock it was. Use the `.bak` file the
editor made to go back.

**Issues?** DM me. But it is open source for a reason, so download the source and patch or fix
stuff yourselves.

## Credits

| Who | For |
|---|---|
| **zXIJz** | Creating the editor |
| [DRG-Save-Editor](https://github.com/AnthonyMichaelTDM/DRG-Save-Editor) | Earlier public research on the save's fields |
| [DRG-Modding / FSD-Template](https://github.com/DRG-Modding/FSD-Template) | The community's generated game headers, used for the save's field types |
| [DevExpress WinForms](https://www.devexpress.com/products/net/controls/winforms/) | The user interface controls |
| [.NET 8](https://dotnet.microsoft.com/) | The runtime |
| Ghost Ship Games | The game. Item names in the editor are the game's own English text |

This is a fan-made tool. It is not made, approved or supported by Ghost Ship Games or Coffee
Stain.

## For developers

```
dotnet build DrgSave.sln -c Release
dotnet publish src\DrgSave.Editor -c Release -r win-x64 -o publish
src\DrgSave.Tests\bin\Release\net8.0\DrgSave.Tests.exe <save>            round-trip check
src\DrgSave.Tests\bin\Release\net8.0\DrgSave.Tests.exe --edit <save>     every edit, on a temp copy
src\DrgSave.Tests\bin\Release\net8.0\DrgSave.Tests.exe --dump <save>     whole save as text
```

You need the .NET SDK 8 or newer and DevExpress WinForms 25.2 (`DevExpress.Win`, paid). Without
DevExpress, `DrgSave.Core` has no UI and can be put behind any other GUI.

| Project | Purpose |
|---|---|
| `DrgSave.Core` | Save reader and writer (UE4 GVAS, byte-for-byte), item list, unlock helpers. No UI |
| `DrgSave.Editor` | The editor |
| `DrgSave.Dump` | Builds `items.tsv` from your own copy of the game: `--list`, `--extract`, `--catalog` |
| `DrgSave.Tests` | Console test runner |
| `DrgSave.Web` | The browser version (Blazor WebAssembly, no DevExpress needed). Published to GitHub Pages by `.github/workflows/pages.yml` |

Every write goes through `DrgSave.Core` (`DrgSaveFile`, `DrgUnlocks`), so the exe and the web
page produce byte-identical saves for the same edits. To run the web editor locally:
`dotnet run --project src\DrgSave.Web`. To publish it, set **Settings > Pages > Source: GitHub
Actions** in the repository; every push to `main` then updates the site.

`items.tsv` lists every ownable item (ID, English name, class, Forge category, DLC flag). Save
layout notes: [docs/RESEARCH.md](docs/RESEARCH.md). Game files and saves are not in this
repository and should not be added to it.
