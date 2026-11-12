namespace DrgSave.Core;

/// <summary>
/// One "unlock all" action. The desktop editor and the web editor both list
/// <see cref="DrgUnlocks.Groups"/>, so the same button writes the same data in either.
/// Paid DLC items are never included.
/// </summary>
/// <param name="Section">Heading the action sits under.</param>
/// <param name="Label">What it unlocks.</param>
/// <param name="Button">Button text.</param>
/// <param name="Status">Progress text for this save ("12 of 48 found or forged").</param>
/// <param name="Apply">Does it; returns how many entries were added.</param>
public sealed record UnlockGroup(string Section, string Label, string Button, Func<DrgSaveFile, string> Status, Func<DrgSaveFile, int> Apply);

public static class DrgUnlocks
{
    public const string SchematicsSection = "Forge schematics";
    public const string CosmeticsSection = "Cosmetics for every class";
    public const string GearSection = "Weapons and gear";

    public const string SchematicsNote =
        "Adds schematics as found but not forged, as if they dropped in a mission. Forge them at the Forge in the Space Rig (max your resources on the Resources page first).";

    public const string PerksNote = "Perks: give yourself perk points on the Resources page and claim the perks in game.";

    // Distinct: the game data lists a few IDs twice, which would throw the "x of y" counts off.
    private static List<string> Ids(Func<DrgItem, bool> where) =>
        DrgItems.All.Where(i => !i.Dlc && where(i)).Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static IReadOnlyList<UnlockGroup> Groups { get; } = Build();

    private static List<UnlockGroup> Build()
    {
        var list = new List<UnlockGroup>();

        foreach (var (category, label) in DrgItems.SchematicGroups)
        {
            var ids = Ids(i => i.Kind == "Schematic" && i.Category == category);
            if (ids.Count == 0) continue;
            list.Add(new UnlockGroup(SchematicsSection, label, "Give all",
                s =>
                {
                    var owned = DrgSaveFile.Ids(s.SchematicList("OwnedSchematics", false));
                    var forged = DrgSaveFile.Ids(s.SchematicList("ForgedSchematics", false));
                    return $"{ids.Count(id => owned.Contains(id) || forged.Contains(id))} of {ids.Count} found or forged";
                },
                s => s.AddOwnedSchematics(ids)));
        }

        var vanity = Ids(i => i.Kind.EndsWith("VanityItem", StringComparison.Ordinal));
        var poses = Ids(i => i.Kind == "VictoryPose");
        list.Add(PerClass("Armour, headwear, beards, paints", "Vanity.UnLockedVanityItemIDs", vanity,
            s => DrgSaveFile.AddIds(s.RootGuidArray("UnLockedVanityItemIDs"), vanity)));
        list.Add(PerClass("Victory poses", "VictoryPose.UnlockedVictoryPoses", poses, null));

        // Only class equipment: event pods and the like have no owning class.
        var weapons = Ids(i => i.Kind == "ItemID" && i.Owner is "Driller" or "Engineer" or "Gunner" or "Scout");
        var upgrades = Ids(i => i.IsWeaponUpgrade);
        var skins = DrgItems.All.Where(i => i.Kind == "ItemSkin" && !i.Dlc && i.Item.Length > 0).Select(i => (i.Item, i.Id)).ToList();
        var pickaxe = Ids(i => i.Kind == "PickaxePart");
        var drinks = Ids(i => i.Kind == "DrinkableDataAsset");
        list.Add(Root("Weapons and tools", weapons.Count, s =>
        {
            // The game keeps both lists: unlocked (can buy) and owned (bought).
            int added = DrgSaveFile.AddIds(s.RootGuidArray("UnlockedItems"), weapons);
            DrgSaveFile.AddIds(s.RootGuidArray("OwnedItems"), weapons);
            return added;
        }));
        list.Add(Root("Weapon upgrades (all tiers)", upgrades.Count, s => DrgSaveFile.AddIds(s.RootGuidArray("PurchasedItemUpgrades"), upgrades)));
        list.Add(Root("Weapon paint jobs and frameworks", skins.Count, s => s.AddItemSkins(skins)));
        list.Add(Root("Pickaxe parts", pickaxe.Count, s => DrgSaveFile.AddIds(s.RootGuidSet("UnlockedPickaxeParts"), pickaxe)));
        list.Add(Root("Abyss Bar drinks", drinks.Count, s => DrgSaveFile.AddIds(s.DrinkSet(), drinks)));
        return list;
    }

    private static List<(string id, GvasStruct record)> Dwarves(DrgSaveFile s) =>
        s.Characters().Where(c => DrgKnown.Dwarves.Any(d => string.Equals(d.Guid, c.id, StringComparison.OrdinalIgnoreCase))).ToList();

    private static UnlockGroup PerClass(string label, string path, List<string> ids, Func<DrgSaveFile, int>? alsoAdd) =>
        new(CosmeticsSection, label, "Unlock all",
            s =>
            {
                var dwarves = Dwarves(s);
                int have = dwarves.Sum(d => DrgSaveFile.Ids(DrgSaveFile.ClassArray(d.record, path)).Count(ids.Contains));
                return $"{have} of {ids.Count * dwarves.Count} unlocked";
            },
            s =>
            {
                int added = 0;
                foreach (var d in Dwarves(s))
                    if (DrgSaveFile.ClassArray(d.record, path) is { } array) added += DrgSaveFile.AddIds(array, ids);
                if (alsoAdd != null) added += alsoAdd(s);
                return added;
            });

    private static UnlockGroup Root(string label, int total, Func<DrgSaveFile, int> apply) =>
        new(GearSection, label, "Unlock all", _ => $"{total} available", apply);
}
