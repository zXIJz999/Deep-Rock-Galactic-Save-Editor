using System.Reflection;

namespace DrgSave.Core;

/// <summary>One thing in the game that has a save ID.</summary>
/// <param name="Id">The 16-byte ID as hex, in save byte order.</param>
/// <param name="Kind">The game's class: Schematic, HeadVanityItem, VictoryPose...</param>
/// <param name="Owner">Class it belongs to (Driller, Engineer, Gunner, Scout, Bosco), or "".</param>
/// <param name="Category">For schematics: SCAT_OC_Clean, SCAT_Vanity_Beard...</param>
/// <param name="Dlc">True when the item comes from paid DLC. These are never unlocked by the editor.</param>
/// <param name="Item">For skins and upgrades: the ID of the weapon or tool it belongs to.</param>
/// <param name="Extra">Upgrade tier ("Tier_3") for weapon upgrades, "RanksN" for perks.</param>
public sealed record DrgItem(string Id, string Kind, string Name, string Owner, string Category, bool Dlc, string Item = "", string Extra = "")
{
    public bool IsWeaponUpgrade => Extra.StartsWith("Tier", StringComparison.Ordinal);
}

/// <summary>
/// The item list built from the game's own data (items.tsv, made with DrgSave.Dump --catalog).
/// </summary>
public static class DrgItems
{
    private static List<DrgItem>? _all;
    private static Dictionary<string, DrgItem>? _byId;

    public static IReadOnlyList<DrgItem> All => _all ??= Load();

    public static DrgItem? Find(string idHex)
    {
        _byId ??= All.GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return _byId.TryGetValue(idHex, out var i) ? i : null;
    }

    public static string? NameOf(string idHex) => Find(idHex)?.Name;

    /// <summary>Schematic groups, as the game's Forge shows them.</summary>
    public static readonly (string category, string label)[] SchematicGroups =
    {
        ("SCAT_OC_Clean", "Overclocks: Clean"),
        ("SCAT_OC_Balanced", "Overclocks: Balanced"),
        ("SCAT_OC_Unstable", "Overclocks: Unstable"),
        ("SCAT_Vanity_WeaponSkin", "Weapon paint jobs"),
        ("SCAT_Vanity_Headwear", "Headwear"),
        ("SCAT_Vanity_Beard", "Beards"),
        ("SCAT_Vanity_Moustache", "Moustaches"),
        ("SCAT_Vanity_Sideburn", "Sideburns"),
        ("SCAT_Vanity_VictoryPose", "Victory poses"),
        ("SCAT_Resources", "Resource schematics"),
    };

    private static List<DrgItem> Load()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("DrgSave.Core.items.tsv")
            ?? throw new InvalidOperationException("items.tsv is not embedded");
        using var r = new StreamReader(s);
        var list = new List<DrgItem>();
        r.ReadLine(); // header
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            var f = line.Split('\t');
            if (f.Length < 6) continue;
            list.Add(new DrgItem(f[0], f[1], f[2], f[3], f[4], f[5] == "1", f.Length > 6 ? f[6] : "", f.Length > 7 ? f[7] : ""));
        }
        return list;
    }
}
