using DrgSave.Core;

// Round-trips every save it is given and reports what the DRG layer can find in it.
// Nothing is written to the input files.
if (args.Length == 0) { Console.Error.WriteLine("usage: DrgSave.Tests <save file or folder> | --dump <save file>"); return 2; }

if (args[0] == "--edit")
{
    // Edits a copy: credits, perk points (created), every resource (list created), class XP,
    // promotions, season XP. Then reloads the copy and checks every value came back.
    string copy = Path.Combine(Path.GetTempPath(), "drg-edit-test.sav");
    File.Copy(args[1], copy, overwrite: true);
    var s = DrgSaveFile.Load(copy);
    s.SetInt(DrgKnown.Credits, 123456);
    s.EnsureRootInt(DrgKnown.PerkPoints).Value = 77;
    foreach (var r in DrgKnown.Resources) s.SetResource(r.Guid, 500);
    var gunner = s.Characters().First(c => c.id == "AE56E180FEC0C44D96FA29C28366B97B").record;
    DrgSaveFile.Field(gunner, "XP")!.Value = 315000;
    DrgSaveFile.Field(gunner, "TimesRetired")!.Value = 3;
    DrgSaveFile.Field(s.SeasonRecords().Last().record, "XP")!.Value = 250000;
    var schematics = DrgItems.All.Where(i => i.Kind == "Schematic" && !i.Dlc).Select(i => i.Id).ToList();
    int addedSchematics = s.AddOwnedSchematics(schematics);
    var vanity = DrgItems.All.Where(i => i.Kind.EndsWith("VanityItem") && !i.Dlc).Select(i => i.Id).ToList();
    var poses = DrgItems.All.Where(i => i.Kind == "VictoryPose" && !i.Dlc).Select(i => i.Id).ToList();
    int addedVanity = DrgSaveFile.AddIds(DrgSaveFile.ClassArray(gunner, "Vanity.UnLockedVanityItemIDs")!, vanity);
    int addedPoses = DrgSaveFile.AddIds(DrgSaveFile.ClassArray(gunner, "VictoryPose.UnlockedVictoryPoses")!, poses);
    var items = DrgItems.All;
    var weapons = items.Where(i => i.Kind == "ItemID" && !i.Dlc).Select(i => i.Id).ToList();
    var upgrades = items.Where(i => i.IsWeaponUpgrade && !i.Dlc).Select(i => i.Id).ToList();
    var skinPairs = items.Where(i => i.Kind == "ItemSkin" && !i.Dlc && i.Item.Length > 0).Select(i => (i.Item, i.Id)).ToList();
    var pickaxe = items.Where(i => i.Kind == "PickaxePart" && !i.Dlc).Select(i => i.Id).ToList();
    var drinks = items.Where(i => i.Kind == "DrinkableDataAsset" && !i.Dlc).Select(i => i.Id).ToList();
    int addedWeapons = DrgSaveFile.AddIds(s.RootGuidArray("UnlockedItems"), weapons);
    DrgSaveFile.AddIds(s.RootGuidArray("OwnedItems"), weapons);
    int addedUpgrades = DrgSaveFile.AddIds(s.RootGuidArray("PurchasedItemUpgrades"), upgrades);
    int addedSkins = s.AddItemSkins(skinPairs);
    int addedPickaxe = DrgSaveFile.AddIds(s.RootGuidSet("UnlockedPickaxeParts"), pickaxe);
    int addedDrinks = DrgSaveFile.AddIds(s.DrinkSet(), drinks);
    int addedRootVanity = DrgSaveFile.AddIds(s.RootGuidArray("UnLockedVanityItemIDs"), vanity);
    var bytes = s.File.ToBytes();
    File.WriteAllBytes(copy, bytes);

    var back = DrgSaveFile.Load(copy);
    var g2 = back.Characters().First(c => c.id == "AE56E180FEC0C44D96FA29C28366B97B").record;
    var checks = new (string what, bool ok)[]
    {
        ("re-read copy round-trips", back.RoundTrips()),
        ("credits", back.GetInt(DrgKnown.Credits, out _) == 123456),
        ("perk points", back.GetInt(DrgKnown.PerkPoints, out _) == 77),
        ("resources map read as Guid map", back.GuidMap(DrgKnown.OwnedResources)?.Entries.Count == DrgKnown.Resources.Length),
        ("every resource = 500", DrgKnown.Resources.All(r => back.GetResource(r.Guid) == 500)),
        ("gunner XP", DrgSaveFile.Field(g2, "XP")?.Value is 315000),
        ("gunner promotions", DrgSaveFile.Field(g2, "TimesRetired")?.Value is 3),
        ("season XP", DrgSaveFile.Field(back.SeasonRecords().Last().record, "XP")?.Value is 250000),
        ($"owned schematics ({addedSchematics})", DrgSaveFile.Ids(back.SchematicList("OwnedSchematics", false)).Count == addedSchematics && addedSchematics > 0),
        ($"gunner cosmetics ({addedVanity})", DrgSaveFile.ClassArray(g2, "Vanity.UnLockedVanityItemIDs")?.Items.Count == addedVanity && addedVanity > 0),
        ($"gunner victory poses ({addedPoses})", DrgSaveFile.ClassArray(g2, "VictoryPose.UnlockedVictoryPoses")?.Items.Count == addedPoses && addedPoses > 0),
        ($"weapons ({addedWeapons})", back.RootGuidArray("UnlockedItems").Items.Count == addedWeapons && addedWeapons > 0),
        ($"weapon upgrades ({addedUpgrades})", back.RootGuidArray("PurchasedItemUpgrades").Items.Count == addedUpgrades && addedUpgrades > 0),
        ($"weapon skins ({addedSkins})", back.File.Properties.First(p => p.Name == "UnlockedItemSkins").Value is GvasMap { KeyStruct: "Guid" } sm
            && sm.Entries.Sum(e => DrgSaveFile.Ids(DrgSaveFile.Field((GvasStruct)e.Value!, "Skins")?.Value as GvasSet).Count) == addedSkins && addedSkins > 0),
        ($"pickaxe parts ({addedPickaxe})", back.RootGuidSet("UnlockedPickaxeParts").Items.Count == addedPickaxe && addedPickaxe > 0),
        ($"drinks ({addedDrinks})", back.DrinkSet().Items.Count == addedDrinks && addedDrinks > 0),
        ($"account cosmetics ({addedRootVanity})", back.RootGuidArray("UnLockedVanityItemIDs").Items.Count == addedRootVanity),
        ("resources inside Resources struct", back.File.Properties.Any(p => p.Name == "Resources")),
        ("nothing else became raw", CountRawQuiet(back.File.Properties) == CountRawQuiet(DrgSaveFile.Load(args[1]).File.Properties)),
    };
    foreach (var (what, ok) in checks) Console.WriteLine($"  {(ok ? "PASS" : "FAIL")} {what}");
    File.Delete(copy);
    return checks.All(c => c.ok) ? 0 : 1;
}

if (args[0] == "--dump")
{
    var dumped = DrgSaveFile.Load(args[1]);
    foreach (var p in dumped.File.Properties) Dump(p, 0);
    return 0;
}

var files = Directory.Exists(args[0])
    ? Directory.GetFiles(args[0], "*.sav", SearchOption.AllDirectories)
    : new[] { args[0] };
if (files.Length == 0) { Console.Error.WriteLine("No .sav files found."); return 2; }

int failed = 0;
foreach (var path in files)
{
    Console.WriteLine($"== {path}");
    DrgSaveFile save;
    try { save = DrgSaveFile.Load(path); }
    catch (Exception ex) { Console.WriteLine($"  FAIL load: {ex.Message}"); failed++; continue; }

    var f = save.File;
    Console.WriteLine($"  GVAS v{f.SaveGameVersion}, package {f.PackageVersion}, engine {f.EngineMajor}.{f.EngineMinor}.{f.EnginePatch}, class {f.SaveGameClass}");

    bool ok = save.RoundTrips();
    Console.WriteLine(ok ? "  PASS round-trip (byte-identical)" : "  FAIL round-trip: output differs from input");
    if (!ok) failed++;

    int raw = 0;
    CountRaw(f.Properties, ref raw);
    Console.WriteLine($"  properties kept as raw bytes: {raw}");

    Report("Credits", save.GetInt(DrgKnown.Credits, out var w1), w1);
    Report("Perk points", save.GetInt(DrgKnown.PerkPoints, out var w2), w2);

    var res = save.GuidMap(DrgKnown.OwnedResources);
    Console.WriteLine(res == null ? "  OwnedResources: not found as a Guid map" : $"  OwnedResources: {res.Entries.Count} entries");
    if (res != null)
        foreach (var e in res.Entries)
        {
            string key = DrgSaveFile.KeyText(e.Key);
            Console.WriteLine($"    {key}  {DrgKnown.ResourceName(key) ?? "(unknown)",-18} {e.Value}");
        }

    foreach (var (id, rec) in save.Characters())
    {
        string name = DrgKnown.ResourceName(id) ?? $"unknown {id}";
        Console.WriteLine($"  {name}: XP={DrgSaveFile.Field(rec, "XP")?.Value}, promotions={DrgSaveFile.Field(rec, "TimesRetired")?.Value}");
    }
    foreach (var (id, rec) in save.SeasonRecords())
        Console.WriteLine($"  {DrgKnown.ResourceName(id) ?? id}: XP={DrgSaveFile.Field(rec, "XP")?.Value}, scrip={DrgSaveFile.Field(rec, "Tokens")?.Value}");
}
Console.WriteLine(failed == 0 ? "All files passed." : $"{failed} file(s) failed.");
return failed == 0 ? 0 : 1;

static void Dump(GvasProperty p, int depth)
{
    string pad = new(' ', depth * 2);
    string type = p.Type switch
    {
        "StructProperty" => $"Struct<{p.StructName}>",
        "ArrayProperty" => $"Array<{(p.Value as GvasArray)?.InnerTag?.StructName ?? p.InnerType}>",
        "MapProperty" => $"Map<{p.KeyType}{((p.Value as GvasMap)?.KeyStruct is { Length: > 0 } ks ? ":" + ks : "")},{p.ValueType}>",
        "SetProperty" => $"Set<{p.InnerType}>",
        _ => p.Type,
    };
    string idx = p.ArrayIndex != 0 ? $"[{p.ArrayIndex}]" : "";
    Console.WriteLine($"{pad}{p.Name}{idx} : {type} = {Short(p.Value)}  @0x{p.Offset:X}");
    DumpValue(p.Value, depth + 1);
}

static void DumpValue(object? v, int depth)
{
    string pad = new(' ', depth * 2);
    switch (v)
    {
        case GvasStruct { Properties: not null } s:
            foreach (var c in s.Properties) Dump(c, depth);
            break;
        case GvasArray a:
            for (int i = 0; i < a.Items.Count; i++)
            {
                Console.WriteLine($"{pad}[{i}] = {Short(a.Items[i])}");
                DumpValue(a.Items[i], depth + 1);
            }
            break;
        case GvasSet set:
            for (int i = 0; i < set.Items.Count; i++)
            {
                Console.WriteLine($"{pad}[{i}] = {Short(set.Items[i])}");
                DumpValue(set.Items[i], depth + 1);
            }
            break;
        case GvasMap m:
            foreach (var e in m.Entries)
            {
                string key = DrgSaveFile.KeyText(e.Key);
                string name = DrgKnown.ResourceName(key) is { } n ? $" ({n})" : "";
                Console.WriteLine($"{pad}{{{key}}}{name} = {Short(e.Value)}");
                DumpValue(e.Key is GvasStruct { Properties: not null } ? e.Key : null, depth + 1);
                DumpValue(e.Value, depth + 1);
            }
            break;
    }
}

static string Short(object? v) => v switch
{
    null => "null",
    GvasStruct { Native: not null } s => $"{s.StructName} {Convert.ToHexString(s.Native)}",
    GvasStruct s => $"{{{s.Properties?.Count} fields}}",
    GvasArray { Bytes: not null } a => $"{a.Bytes.Length} bytes {Convert.ToHexString(a.Bytes, 0, Math.Min(32, a.Bytes.Length))}",
    GvasArray a => $"{a.Items.Count} items",
    GvasSet s => $"{s.Items.Count} items",
    GvasMap m => $"{m.Entries.Count} entries",
    GvasRaw r => $"RAW {r.Data.Length} bytes ({r.Reason}) {Convert.ToHexString(r.Data, 0, Math.Min(48, r.Data.Length))}",
    float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
    _ => v.ToString() ?? "",
};

static void Report(string label, int? value, string? warning)
{
    Console.WriteLine($"  {label}: {(value?.ToString() ?? "not found")}");
    if (warning != null) Console.WriteLine($"    WARNING {warning}");
}

static int CountRawQuiet(List<GvasProperty> props)
{
    int n = 0;
    foreach (var p in props)
    {
        if (p.Value is GvasRaw) n++;
        else if (p.Value is GvasStruct { Properties: not null } s) n += CountRawQuiet(s.Properties);
    }
    return n;
}

static void CountRaw(List<GvasProperty> props, ref int raw)
{
    foreach (var p in props)
    {
        if (p.Value is GvasRaw) { raw++; Console.WriteLine($"    raw: {p.Name} ({p.Type}) {((GvasRaw)p.Value).Reason}"); }
        else if (p.Value is GvasStruct { Properties: not null } s) CountRaw(s.Properties, ref raw);
    }
}
