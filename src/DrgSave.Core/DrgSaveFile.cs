namespace DrgSave.Core;

/// <summary>A property found somewhere in the save, with the path that leads to it.</summary>
public sealed record FoundProperty(string Path, GvasProperty Property);

/// <summary>
/// A Deep Rock Galactic player save. Values are found by walking the GVAS property tree by name,
/// never by byte offset, so a save with a different layout cannot be written to the wrong place.
///
/// UNVERIFIED: the property names and GUIDs in <see cref="DrgKnown"/> come from public research
/// on the Steam save. They are checked against a real PS4 save before any page relies on them.
/// </summary>
public sealed class DrgSaveFile
{
    public string Path { get; private set; }
    public byte[] Original { get; private set; }
    public GvasFile File { get; private set; }

    private DrgSaveFile(string path, byte[] data)
    {
        Path = path;
        Original = data;
        File = GvasFile.Load(data);
    }

    public static DrgSaveFile Load(string path) => new(path, System.IO.File.ReadAllBytes(path));

    /// <summary>Loads a save from memory; <paramref name="name"/> is kept as its path (used by the web editor).</summary>
    public static DrgSaveFile Load(byte[] data, string name) => new(name, data);

    /// <summary>
    /// The bytes to write, checked to read back identically. Throws when the check fails, so a
    /// save the editor cannot read back is never written.
    /// </summary>
    public byte[] Export()
    {
        var bytes = File.ToBytes();
        if (!GvasFile.Load(bytes).ToBytes().AsSpan().SequenceEqual(bytes))
            throw new GvasFormatException("Self-check failed: the edited save does not read back the same. Nothing was saved.");
        return bytes;
    }

    /// <summary>True when writing the save unchanged gives back the exact bytes that were read.</summary>
    public bool RoundTrips() => File.ToBytes().AsSpan().SequenceEqual(Original);

    /// <summary>Writes the save. The file being replaced is kept next to it as a timestamped .bak.</summary>
    public string Save(string? path = null)
    {
        path ??= Path;
        string backup = "";
        if (System.IO.File.Exists(path))
        {
            backup = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            System.IO.File.Copy(path, backup, overwrite: false);
        }
        var bytes = Export();
        System.IO.File.WriteAllBytes(path, bytes);
        Path = path;
        Original = bytes;
        return backup;
    }

    // ---- finding things ---------------------------------------------------------

    /// <summary>Every property with this name anywhere in the save.</summary>
    public List<FoundProperty> FindAll(string name)
    {
        var found = new List<FoundProperty>();
        Walk(File.Properties, "", (path, p) => { if (p.Name == name) found.Add(new FoundProperty(path, p)); });
        return found;
    }

    /// <summary>
    /// The only property with this name, or null. When there is more than one, the first is
    /// returned and <paramref name="warning"/> says so, so the page can show it instead of
    /// silently editing the wrong one.
    /// </summary>
    public GvasProperty? FindOne(string name, out string? warning)
    {
        var all = FindAll(name);
        warning = all.Count > 1 ? $"{name} appears {all.Count} times: {string.Join(", ", all.Select(f => f.Path))}" : null;
        return all.FirstOrDefault()?.Property;
    }

    private static void Walk(List<GvasProperty> props, string prefix, Action<string, GvasProperty> visit)
    {
        foreach (var p in props)
        {
            string path = prefix.Length == 0 ? p.Name : $"{prefix}.{p.Name}";
            visit(path, p);
            WalkValue(p.Value, path, visit);
        }
    }

    private static void WalkValue(object? value, string path, Action<string, GvasProperty> visit)
    {
        switch (value)
        {
            case GvasStruct { Properties: not null } s:
                Walk(s.Properties, path, visit);
                break;
            case GvasArray a:
                for (int i = 0; i < a.Items.Count; i++) WalkValue(a.Items[i], $"{path}[{i}]", visit);
                break;
            case GvasSet set:
                for (int i = 0; i < set.Items.Count; i++) WalkValue(set.Items[i], $"{path}[{i}]", visit);
                break;
            case GvasMap m:
                for (int i = 0; i < m.Entries.Count; i++) WalkValue(m.Entries[i].Value, $"{path}{{{KeyText(m.Entries[i].Key)}}}", visit);
                break;
        }
    }

    /// <summary>Text for a map key: a Guid key as 32 hex digits in file byte order.</summary>
    public static string KeyText(object? key) => key switch
    {
        null => "",
        GvasStruct { Native: { Length: 16 } g } => Convert.ToHexString(g),
        GvasStruct s => s.StructName,
        GvasWideString w => w.Value,
        _ => key.ToString() ?? "",
    };

    // ---- simple values ------------------------------------------------------------

    /// <summary>Reads an int property by name, or null when the save has none.</summary>
    public int? GetInt(string name, out string? warning)
    {
        var p = FindOne(name, out warning);
        return p?.Value is int i ? i : null;
    }

    public bool SetInt(string name, int value)
    {
        var p = FindOne(name, out _);
        if (p == null || p.Value is not int) return false;
        p.Value = value;
        return true;
    }

    // ---- maps keyed by Guid ---------------------------------------------------------

    /// <summary>The map with this name, when its keys were read as Guids.</summary>
    public GvasMap? GuidMap(string name)
    {
        var p = FindOne(name, out _);
        return p?.Value is GvasMap { KeyStruct: "Guid" } m ? m : null;
    }

    /// <summary>Index of the entry whose Guid key matches, or -1.</summary>
    public static int IndexOf(GvasMap map, string guidHex)
    {
        for (int i = 0; i < map.Entries.Count; i++)
            if (string.Equals(KeyText(map.Entries[i].Key), guidHex, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Every map in the save whose keys are Guids, with its path.</summary>
    public List<FoundProperty> GuidMaps()
    {
        var found = new List<FoundProperty>();
        Walk(File.Properties, "", (path, p) => { if (p.Value is GvasMap { KeyStruct: "Guid" }) found.Add(new FoundProperty(path, p)); });
        return found;
    }

    // ---- confirmed layout (PS4 save, game 1.40) ------------------------------------

    /// <summary>The class records in CharacterSaves, each with its SavegameID as hex.</summary>
    public List<(string id, GvasStruct record)> Characters()
    {
        var list = new List<(string, GvasStruct)>();
        if (FindOne(DrgKnown.CharacterSaves, out _)?.Value is not GvasArray a) return list;
        foreach (var item in a.Items)
        {
            if (item is not GvasStruct { Properties: not null } s) continue;
            var id = s.Properties.FirstOrDefault(p => p.Name == "SavegameID")?.Value;
            list.Add((KeyText(id), s));
        }
        return list;
    }

    /// <summary>A whole-number field of a struct, or null.</summary>
    public static GvasProperty? Field(GvasStruct s, string name) => s.Properties?.FirstOrDefault(p => p.Name == name);

    /// <summary>
    /// Returns the root int property with this name, creating it right after Credits when the
    /// save does not have it yet (a new save has no PerkPoints until the first one is earned).
    /// </summary>
    public GvasProperty EnsureRootInt(string name)
    {
        var p = File.Properties.FirstOrDefault(x => x.Name == name);
        if (p != null) return p;
        p = new GvasProperty { Name = name, Type = "IntProperty", Value = 0 };
        InsertAfterCredits(p);
        return p;
    }

    /// <summary>
    /// The OwnedResources map. The game keeps it in a root struct "Resources" (ResourcesSave),
    /// which a new save does not have yet; it is created after Credits when missing.
    /// </summary>
    public GvasMap EnsureResources()
    {
        if (GuidMap(DrgKnown.OwnedResources) is { } existing) return existing;
        var map = new GvasMap { KeyType = "StructProperty", ValueType = "FloatProperty", KeyStruct = "Guid" };
        var mapProp = new GvasProperty
        {
            Name = DrgKnown.OwnedResources, Type = "MapProperty", KeyType = "StructProperty", ValueType = "FloatProperty", Value = map,
        };
        var resources = File.Properties.FirstOrDefault(x => x.Name == "Resources" && x.Value is GvasStruct { Properties: not null });
        if (resources != null) ((GvasStruct)resources.Value!).Properties!.Insert(0, mapProp);
        else
            InsertAfterCredits(new GvasProperty
            {
                Name = "Resources", Type = "StructProperty", StructName = "ResourcesSave", StructGuid = new byte[16],
                Value = new GvasStruct { StructName = "ResourcesSave", Properties = new List<GvasProperty> { mapProp } },
            });
        return map;
    }

    // ---- schematics (overclocks, cosmetics found in the mines) ------------------------

    /// <summary>SchematicSave.ForgedSchematics or .OwnedSchematics, created when missing.</summary>
    public GvasArray? SchematicList(string name, bool create)
    {
        if (File.Properties.FirstOrDefault(x => x.Name == "SchematicSave")?.Value is not GvasStruct { Properties: not null } save) return null;
        if (save.Properties.FirstOrDefault(x => x.Name == name)?.Value is GvasArray a) return a;
        if (!create) return null;
        // Game order: bFirstSchematicMessageShown, ForgedSchematics, OwnedSchematics, SkinFixupCounter.
        var prop = NewGuidArray(name);
        var array = (GvasArray)prop.Value!;
        int at = save.Properties.FindIndex(x => x.Name == "SkinFixupCounter");
        if (name == "ForgedSchematics")
        {
            int owned = save.Properties.FindIndex(x => x.Name == "OwnedSchematics");
            if (owned >= 0) at = owned;
        }
        save.Properties.Insert(at >= 0 ? at : save.Properties.Count, prop);
        return array;
    }

    public static HashSet<string> Ids(GvasArray? a) =>
        a == null ? new(StringComparer.OrdinalIgnoreCase) : a.Items.Select(KeyText).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds schematics as owned but not forged, the state the game gives when one drops in a
    /// mission. The player then forges them at the Forge. Returns how many were added.
    /// </summary>
    public int AddOwnedSchematics(IEnumerable<string> ids)
    {
        var forged = Ids(SchematicList("ForgedSchematics", create: false));
        var ownedList = SchematicList("OwnedSchematics", create: true)!;
        var owned = Ids(ownedList);
        int added = 0;
        foreach (var id in ids)
        {
            if (forged.Contains(id) || !owned.Add(id)) continue;
            ownedList.Items.Add(NewGuid(id));
            added++;
        }
        return added;
    }

    // ---- per-class cosmetics ------------------------------------------------------------

    /// <summary>A class record's array at a dotted path (e.g. "Vanity.UnLockedVanityItemIDs").</summary>
    public static GvasArray? ClassArray(GvasStruct record, string path)
    {
        GvasStruct current = record;
        var parts = path.Split('.');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (Field(current, parts[i])?.Value is not GvasStruct { Properties: not null } next) return null;
            current = next;
        }
        return Field(current, parts[^1])?.Value as GvasArray;
    }

    /// <summary>Adds IDs to an existing Guid array, skipping ones already there. Returns how many.</summary>
    public static int AddIds(GvasArray array, IEnumerable<string> ids)
    {
        var have = Ids(array);
        int added = 0;
        foreach (var id in ids)
        {
            if (!have.Add(id)) continue;
            array.Items.Add(NewGuid(id, array.InnerTag?.StructName ?? "Guid"));
            added++;
        }
        return added;
    }

    // FNames are case-insensitive and this build registers the Guid struct as "GUID", which is
    // the spelling the game itself writes in this save. New tags use the same.
    private const string GuidStruct = "GUID";

    private static GvasStruct NewGuid(string hex, string structName = GuidStruct) =>
        new() { StructName = structName, Native = Convert.FromHexString(hex) };

    /// <summary>A new TArray&lt;FGuid&gt; property.</summary>
    private static GvasProperty NewGuidArray(string name) => new()
    {
        Name = name, Type = "ArrayProperty", InnerType = "StructProperty",
        Value = new GvasArray
        {
            InnerType = "StructProperty",
            InnerTag = new GvasProperty { Name = name, Type = "StructProperty", StructName = GuidStruct, StructGuid = new byte[16] },
        },
    };

    /// <summary>A new TSet&lt;FGuid&gt; property.</summary>
    private static GvasProperty NewGuidSet(string name) => new()
    {
        Name = name, Type = "SetProperty", InnerType = "StructProperty",
        Value = new GvasSet { InnerType = "StructProperty", ElementStruct = "Guid" },
    };

    // ---- root lists (types from the game's FSDSaveGame header) ----------------------------

    /// <summary>A root TArray&lt;FGuid&gt; (UnlockedItems, OwnedItems, PurchasedItemUpgrades, UnLockedVanityItemIDs), created after CharacterSaves when missing.</summary>
    public GvasArray RootGuidArray(string name)
    {
        if (File.Properties.FirstOrDefault(x => x.Name == name)?.Value is GvasArray a) return a;
        var prop = NewGuidArray(name);
        InsertRoot(prop);
        return (GvasArray)prop.Value!;
    }

    /// <summary>A root TSet&lt;FGuid&gt; (UnlockedPickaxeParts), created when missing.</summary>
    public GvasSet RootGuidSet(string name)
    {
        if (File.Properties.FirstOrDefault(x => x.Name == name)?.Value is GvasSet s) return s;
        var prop = NewGuidSet(name);
        InsertRoot(prop);
        return (GvasSet)prop.Value!;
    }

    /// <summary>Drinks.UnlockedDrinks (FDrinkSave), created when missing.</summary>
    public GvasSet DrinkSet()
    {
        var drinks = File.Properties.FirstOrDefault(x => x.Name == "Drinks");
        if (drinks == null)
        {
            drinks = new GvasProperty
            {
                Name = "Drinks", Type = "StructProperty", StructName = "DrinkSave", StructGuid = new byte[16],
                Value = new GvasStruct { StructName = "DrinkSave", Properties = new List<GvasProperty>() },
            };
            InsertRoot(drinks);
        }
        var body = ((GvasStruct)drinks.Value!).Properties!;
        if (body.FirstOrDefault(x => x.Name == "UnlockedDrinks")?.Value is GvasSet s) return s;
        var prop = NewGuidSet("UnlockedDrinks");
        body.Insert(0, prop);
        return (GvasSet)prop.Value!;
    }

    /// <summary>
    /// UnlockedItemSkins: TMap&lt;FGuid weapon, FSkinList { TSet&lt;FGuid&gt; Skins }&gt;.
    /// Adds each (weapon, skin) pair, creating the map and the weapon's entry when missing.
    /// </summary>
    public int AddItemSkins(IEnumerable<(string weapon, string skin)> pairs)
    {
        var prop = File.Properties.FirstOrDefault(x => x.Name == "UnlockedItemSkins");
        if (prop == null)
        {
            prop = new GvasProperty
            {
                Name = "UnlockedItemSkins", Type = "MapProperty", KeyType = "StructProperty", ValueType = "StructProperty",
                Value = new GvasMap { KeyType = "StructProperty", ValueType = "StructProperty", KeyStruct = "Guid" },
            };
            InsertRoot(prop);
        }
        if (prop.Value is not GvasMap { KeyStruct: "Guid" } map) return 0;
        int added = 0;
        foreach (var group in pairs.GroupBy(p => p.weapon, StringComparer.OrdinalIgnoreCase))
        {
            int i = IndexOf(map, group.Key);
            GvasStruct list;
            if (i >= 0 && map.Entries[i].Value is GvasStruct { Properties: not null } existing) list = existing;
            else
            {
                list = new GvasStruct { StructName = "SkinList", Properties = new List<GvasProperty> { NewGuidSet("Skins") } };
                map.Entries.Add(new KeyValuePair<object?, object?>(NewGuid(group.Key, "Guid"), list));
            }
            if (Field(list, "Skins")?.Value is not GvasSet skins) continue;
            added += AddIds(skins, group.Select(p => p.skin));
        }
        return added;
    }

    /// <summary>Adds IDs to a Guid set, skipping ones already there. Returns how many.</summary>
    public static int AddIds(GvasSet set, IEnumerable<string> ids)
    {
        var have = set.Items.Select(KeyText).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (var id in ids)
        {
            if (!have.Add(id)) continue;
            set.Items.Add(NewGuid(id, "Guid"));
            added++;
        }
        return added;
    }

    public static HashSet<string> Ids(GvasSet? s) =>
        s == null ? new(StringComparer.OrdinalIgnoreCase) : s.Items.Select(KeyText).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Root properties are matched by name, so position only matters for tidiness.</summary>
    private void InsertRoot(GvasProperty p)
    {
        int at = File.Properties.FindIndex(x => x.Name == DrgKnown.CharacterSaves);
        File.Properties.Insert(at >= 0 ? at + 1 : File.Properties.Count, p);
    }

    /// <summary>Sets a resource amount, adding the entry when it is missing.</summary>
    public void SetResource(string guidHex, float amount)
    {
        var map = EnsureResources();
        int i = IndexOf(map, guidHex);
        var key = i >= 0 ? map.Entries[i].Key : new GvasStruct { StructName = "Guid", Native = Convert.FromHexString(guidHex) };
        var entry = new KeyValuePair<object?, object?>(key, amount);
        if (i >= 0) map.Entries[i] = entry;
        else map.Entries.Add(entry);
    }

    /// <summary>Current amount of a resource, or 0 when the save has none.</summary>
    public float GetResource(string guidHex)
    {
        var map = GuidMap(DrgKnown.OwnedResources);
        if (map == null) return 0;
        int i = IndexOf(map, guidHex);
        return i >= 0 && map.Entries[i].Value is float f ? f : 0;
    }

    private void InsertAfterCredits(GvasProperty p)
    {
        int at = File.Properties.FindIndex(x => x.Name == DrgKnown.Credits);
        File.Properties.Insert(at >= 0 ? at + 1 : File.Properties.Count, p);
    }

    /// <summary>The season records in SeasonSave.Seasons, keyed by season GUID hex.</summary>
    public List<(string id, GvasStruct record)> SeasonRecords()
    {
        var list = new List<(string, GvasStruct)>();
        if (FindOne(DrgKnown.Seasons, out _)?.Value is not GvasMap m) return list;
        foreach (var e in m.Entries)
            if (e.Value is GvasStruct { Properties: not null } s) list.Add((KeyText(e.Key), s));
        return list;
    }

    /// <summary>
    /// Finds the struct stored under a Guid key in any Guid-keyed map. Returns the map path
    /// too, so the caller can show where it came from.
    /// </summary>
    public (string path, GvasStruct value)? StructForGuid(string guidHex)
    {
        foreach (var f in GuidMaps())
        {
            var m = (GvasMap)f.Property.Value!;
            int i = IndexOf(m, guidHex);
            if (i >= 0 && m.Entries[i].Value is GvasStruct { Properties: not null } s) return (f.Path, s);
        }
        return null;
    }
}
