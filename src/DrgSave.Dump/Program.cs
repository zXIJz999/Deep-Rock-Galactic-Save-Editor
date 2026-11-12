using DrgSave.Dump;

// DrgSave.Dump --list <pak> <out.txt>                 list every file in the pak
// DrgSave.Dump --extract <pak> <outDir> <filter>...   extract files whose path contains any filter
if (args.Length < 2) { Console.Error.WriteLine("usage: --list <pak> <out.txt> | --extract <pak> <outDir> <filter>... | --scan <dir> <out.tsv>"); return 2; }

// --catalog <pak> <extractedDir> <out.tsv>: every item with a save ID, named.
if (args[0] == "--catalog") return Catalog.Build(args[1], args[2], args[3]);

// --find <dir> <hex>: files containing these bytes, and the property they sit in when parseable.
if (args[0] == "--find")
{
    var needle = Convert.FromHexString(args[2]);
    foreach (var file in Directory.EnumerateFiles(args[1], "*.*", SearchOption.AllDirectories))
    {
        var bytes = File.ReadAllBytes(file);
        if (bytes.AsSpan().IndexOf(needle) < 0) continue;
        Console.WriteLine(Path.GetRelativePath(args[1], file));
        if (!file.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase)) continue;
        var a = UAsset.Load(Path.ChangeExtension(file, ".uasset"));
        foreach (var e in a.Exports)
            foreach (var kv in e.Props.Where(kv => kv.Value.Equals(args[2], StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"   {e.Class} {e.Name}: {kv.Key}");
    }
    return 0;
}

// --show <uasset>: every export of one asset with all its fields.
if (args[0] == "--show")
{
    var a = UAsset.Load(args[1]);
    for (int i = 0; i < a.Exports.Count; i++)
    {
        Console.WriteLine($"export {i + 1}: {a.Exports[i].Class} {a.Exports[i].Name}");
        foreach (var kv in a.Exports[i].Props) Console.WriteLine($"    {kv.Key} = {kv.Value}");
    }
    return 0;
}

// --scan: every asset with a SaveGameID, one line per export that has one, with all its fields.
if (args[0] == "--scan")
{
    var lines = new List<string>();
    int seen = 0, bad = 0;
    foreach (var file in Directory.EnumerateFiles(args[1], "*.uasset", SearchOption.AllDirectories))
    {
        var head = File.ReadAllBytes(file);
        if (head.AsSpan().IndexOf("SaveGameID"u8) < 0 && head.AsSpan().IndexOf("SavegameID"u8) < 0) continue;
        seen++;
        UAsset a;
        try { a = UAsset.Load(file); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException) { bad++; continue; }
        string rel = Path.GetRelativePath(args[1], file).Replace('\\', '/');
        foreach (var e in a.Exports)
        {
            var id = e.Props.FirstOrDefault(kv => kv.Key is "SaveGameID" or "SavegameID").Value;
            if (id == null) continue;
            lines.Add($"{rel}\t{e.Class}\t{e.Name}\t{id}\t" + string.Join("\t", e.Props.Where(kv => kv.Key is not ("SaveGameID" or "SavegameID"))
                .Select(kv => $"{kv.Key}={kv.Value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ')}")));
        }
    }
    File.WriteAllLines(args[2], lines);
    Console.WriteLine($"assets naming SaveGameID: {seen}, unreadable: {bad}, exports with an ID: {lines.Count}");
    return 0;
}

using var pak = new Pak(args[1]);
Console.WriteLine($"pak v{pak.Version}, mount {pak.MountPoint}, {pak.Entries.Count} files, compression: {string.Join(",", pak.CompressionMethods.Where(m => m.Length > 0))}");

switch (args[0])
{
    case "--list":
        File.WriteAllLines(args[2], pak.Entries.Select(e => $"{e.Path}\t{e.UncompressedSize}\t{pak.CompressionMethods[e.CompressionIndex]}"));
        return 0;
    case "--extract":
    {
        var filters = args.Skip(3).ToArray();
        int n = 0, failed = 0;
        foreach (var e in pak.Entries)
        {
            if (filters.Length > 0 && !filters.Any(f => e.Path.Contains(f, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var outPath = Path.Combine(args[2], e.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllBytes(outPath, pak.Read(e));
                n++;
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException)
            {
                if (failed++ < 5) Console.WriteLine($"  skip {e.Path}: {ex.Message}");
            }
        }
        Console.WriteLine($"extracted {n}, failed {failed}");
        return 0;
    }
    default:
        Console.Error.WriteLine("unknown mode");
        return 2;
}
