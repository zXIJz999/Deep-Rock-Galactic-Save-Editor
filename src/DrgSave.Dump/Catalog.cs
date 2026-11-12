namespace DrgSave.Dump;

/// <summary>
/// Builds the item catalogue: every object in the game that has a save ID, with its English
/// name, the class that owns it and, for schematics, the item the schematic unlocks.
/// Output columns: Id, Kind, Asset, Name, Owner, Target, Category, Path.
/// </summary>
public static class Catalog
{
    private sealed record Rec(string Id, string Kind, string Asset, string Path, Dictionary<string, string> Props, UAsset Package);

    private static readonly string[] NameKeys =
        { "Name", "ItemName", "Title", "SkinName", "CategoryName", "SeasonName", "DrinkableName", "AssetName", "ChallengeTitle", "MissionName", "Description" };

    private static readonly string[] OwnerKeys = { "OwningCharacter", "UsedByCharacter", "ItemOwner", "Character", "CharacterID" };

    public static int Build(string pakPath, string dir, string outPath)
    {
        using var pak = new Pak(pakPath);
        var byPath = pak.Entries.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);

        var recs = new List<Rec>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.uasset", SearchOption.AllDirectories))
        {
            var head = File.ReadAllBytes(file);
            if (head.AsSpan().IndexOf("SaveGameID"u8) < 0 && head.AsSpan().IndexOf("SavegameID"u8) < 0) continue;
            var a = UAsset.Load(file);
            string rel = System.IO.Path.GetRelativePath(dir, file).Replace('\\', '/');
            foreach (var e in a.Exports)
            {
                var props = new Dictionary<string, string>();
                foreach (var kv in e.Props) props.TryAdd(kv.Key, kv.Value);
                if (!props.TryGetValue("SaveGameID", out var id) && !props.TryGetValue("SavegameID", out id)) continue;
                recs.Add(new Rec(id, e.Class, e.Name, rel, props, a));
            }
        }

        // String tables referenced by any text, loaded from disk or pulled from the pak.
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        string Resolve(string v)
        {
            if (!v.StartsWith(UAsset.StringTableMark, StringComparison.Ordinal)) return v;
            var parts = v[UAsset.StringTableMark.Length..].Split('|', 2);
            if (!tables.TryGetValue(parts[0], out var t))
            {
                t = LoadTable(parts[0], dir, pak, byPath);
                tables[parts[0]] = t;
            }
            return t.TryGetValue(parts[1], out var s) ? s : "";
        }

        var byAsset = recs.GroupBy(r => r.Asset).ToDictionary(g => g.Key, g => g.First());
        // Skins take their name from a separate SkinSet asset; load those on demand from the pak.
        var byFileName = pak.Entries.Where(e => e.Path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => System.IO.Path.GetFileNameWithoutExtension(e.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Path, StringComparer.OrdinalIgnoreCase);
        var externalNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string ExternalName(string assetName)
        {
            if (externalNames.TryGetValue(assetName, out var cached)) return cached;
            string name = "";
            if (byFileName.TryGetValue(assetName, out var rel))
            {
                string local = System.IO.Path.Combine(dir, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
                foreach (var ext in new[] { ".uasset", ".uexp" })
                {
                    string src = rel[..^".uasset".Length] + ext, dst = local[..^".uasset".Length] + ext;
                    if (!File.Exists(dst) && byPath.TryGetValue(src, out var e)) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst)!); File.WriteAllBytes(dst, pak.Read(e)); }
                }
                if (File.Exists(local))
                    foreach (var ex in UAsset.Load(local).Exports)
                        foreach (var kv in ex.Props)
                            if (name.Length == 0 && NameKeys.Contains(kv.Key) && kv.Key != "Description") name = Resolve(kv.Value).Trim();
            }
            return externalNames[assetName] = name;
        }

        string NameOf(Rec r)
        {
            foreach (var k in NameKeys)
                if (r.Props.TryGetValue(k, out var v)) { var s = Resolve(v).Trim(); if (s.Length > 0 && k != "Description") return s; }
            if (r.Props.TryGetValue("SkinSet", out var set) && set.StartsWith("import:", StringComparison.Ordinal))
            {
                var s = ExternalName(set["import:".Length..]);
                if (s.Length > 0) return s;
            }
            return "";
        }

        string Owner(Rec r)
        {
            foreach (var k in OwnerKeys)
                if (r.Props.TryGetValue(k, out var v) && v.StartsWith("import:", StringComparison.Ordinal))
                    return v["import:".Length..].Replace("ID", "");
            return "";
        }

        // A schematic's Item points at an inner export whose first object reference is the unlocked item.
        Rec? Target(Rec r)
        {
            if (!r.Props.TryGetValue("Item", out var item) || !item.StartsWith("export:", StringComparison.Ordinal)) return null;
            int n = int.Parse(item["export:".Length..]);
            if (n < 1 || n > r.Package.Exports.Count) return null;
            foreach (var kv in r.Package.Exports[n - 1].Props)
            {
                // OwningItem is the weapon the schematic belongs to, not what it unlocks.
                if (kv.Key == "OwningItem") continue;
                if (kv.Value.StartsWith("import:", StringComparison.Ordinal) && byAsset.TryGetValue(StripClass(kv.Value["import:".Length..]), out var t)) return t;
                if (kv.Value.StartsWith("export:", StringComparison.Ordinal))
                {
                    int m = int.Parse(kv.Value["export:".Length..]);
                    if (m >= 1 && m <= r.Package.Exports.Count && byAsset.TryGetValue(r.Package.Exports[m - 1].Name, out var t2)) return t2;
                }
            }
            return null;
        }

        string Category(Rec r) =>
            r.Props.TryGetValue("Category", out var c) && c.StartsWith("import:", StringComparison.Ordinal) ? c["import:".Length..]
            : r.Props.TryGetValue("SchematicCategory", out var sc) && sc.StartsWith("import:", StringComparison.Ordinal) ? sc["import:".Length..] : "";

        // How an item is obtained: the class of its Aquisition export (DLC, schematic, season...),
        // plus "+DLC" when any field names a DLC. Used to keep paid DLC out of unlock-all.
        string Acquisition(Rec r)
        {
            string acq = "";
            if (r.Props.TryGetValue("Aquisition", out var a) && a.StartsWith("export:", StringComparison.Ordinal))
            {
                int n = int.Parse(a["export:".Length..]);
                if (n >= 1 && n <= r.Package.Exports.Count)
                {
                    var ex = r.Package.Exports[n - 1];
                    acq = ex.Class;
                    if (ex.Props.Any(kv => kv.Value.Contains("DLC", StringComparison.OrdinalIgnoreCase))) acq += "+DLC";
                }
            }
            if (r.Props.Any(kv => kv.Key.Contains("DLC", StringComparison.OrdinalIgnoreCase) && kv.Value.StartsWith("import:", StringComparison.Ordinal)))
                acq += "+DLC";
            return acq;
        }

        // The weapon or tool an item belongs to (skins, upgrades): OwningItem / ItemOwner import -> its ID.
        string Item(Rec r)
        {
            foreach (var k in new[] { "OwningItem", "Item" })
                if (r.Props.TryGetValue(k, out var v) && v.StartsWith("import:", StringComparison.Ordinal)
                    && byAsset.TryGetValue(StripClass(v["import:".Length..]), out var t) && t.Kind == "ItemID")
                    return t.Id;
            return "";
        }

        // Upgrade tier for weapon upgrades, rank count for perks.
        string Extra(Rec r)
        {
            if (r.Props.TryGetValue("UpgradeTier", out var tier)) return tier.Split("::").Last();
            int ranks = r.Props.Keys.Count(k => k.StartsWith("Ranks[", StringComparison.Ordinal) && k.EndsWith("].Tier", StringComparison.Ordinal));
            return ranks > 0 ? $"Ranks{ranks}" : "";
        }

        var lines = new List<string> { "Id\tKind\tAsset\tName\tOwner\tTarget\tCategory\tAcquisition\tItem\tExtra\tPath" };
        foreach (var r in recs.OrderBy(r => r.Kind).ThenBy(r => r.Path))
        {
            var target = r.Kind == "Schematic" ? Target(r) : null;
            string name = NameOf(r);
            if (target != null && (name.Length == 0 || r.Kind == "Schematic")) name = NameOf(target);
            if (name.Length == 0) name = Prettify(target?.Asset ?? r.Asset);
            string owner = Owner(r);
            if (owner.Length == 0 && target != null) owner = Owner(target);
            string acquisition = Acquisition(r);
            if (acquisition.Length == 0 && target != null) acquisition = Acquisition(target);
            lines.Add(string.Join('\t', r.Id, r.Kind, r.Asset, Clean(name), owner, target?.Id ?? "", Category(r), acquisition, Item(r), Extra(r), r.Path));
        }
        File.WriteAllLines(outPath, lines);
        Console.WriteLine($"catalogue: {recs.Count} items, {tables.Count} string tables");
        return 0;
    }

    private static Dictionary<string, string> LoadTable(string tableId, string dir, Pak pak, Dictionary<string, Pak.Entry> byPath)
    {
        // "/Game/Character/Vanity2/_StringTables/Strings_DLC01.Strings_DLC01" -> FSD/Content/.../Strings_DLC01.uasset
        string pkg = tableId.Split('.')[0];
        if (!pkg.StartsWith("/Game/", StringComparison.Ordinal)) return new();
        string rel = "FSD/Content/" + pkg["/Game/".Length..];
        string local = System.IO.Path.Combine(dir, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
        foreach (var ext in new[] { ".uasset", ".uexp" })
        {
            if (File.Exists(local + ext) || !byPath.TryGetValue(rel + ext, out var e)) continue;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(local)!);
            File.WriteAllBytes(local + ext, pak.Read(e));
        }
        return File.Exists(local + ".uasset") ? UAsset.Load(local + ".uasset").StringTable : new();
    }

    /// <summary>"OC_AssaultRifle_ClipSize" -> "Assault Rifle Clip Size" (only when the game gives no name).</summary>
    private static string Prettify(string asset)
    {
        var parts = asset.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 1 && parts[0].Length <= 4 && parts[0].All(char.IsUpper)) parts.RemoveAt(0);
        var text = string.Join(' ', parts);
        return System.Text.RegularExpressions.Regex.Replace(text, "(?<=[a-z])(?=[A-Z])", " ").Trim() + " *";
    }

    /// <summary>A blueprint class import ("OC_X_C") names the asset "OC_X".</summary>
    private static string StripClass(string name) => name.EndsWith("_C", StringComparison.Ordinal) ? name[..^2] : name;

    private static string Clean(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
}
