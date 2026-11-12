using System.IO.Compression;
using System.Text;

namespace DrgSave.Dump;

/// <summary>
/// Reader for UE 4.25+ pak files (version 10/11: path-hash index, full directory index and
/// bit-packed encoded entries). Unencrypted index and data only, which is what the DRG PS4
/// pak uses. Used only to pull item IDs and names out of a local copy of the game.
/// </summary>
public sealed class Pak : IDisposable
{
    public sealed class Entry
    {
        public string Path = "";
        public long Offset, Size, UncompressedSize;
        public int CompressionIndex;
        public bool Encrypted;
        public uint BlockSize;
        public (long start, long end)[] Blocks = Array.Empty<(long, long)>();
    }

    private readonly FileStream _fs;
    public int Version;
    public string MountPoint = "";
    public string[] CompressionMethods = Array.Empty<string>();
    public List<Entry> Entries = new();

    public Pak(string path)
    {
        _fs = File.OpenRead(path);

        // Footer: guid(16) encryptedIndex(1) magic(4) version(4) offset(8) size(8) hash(20) methods(5*32).
        const int footer = 16 + 1 + 4 + 4 + 8 + 8 + 20 + 5 * 32;
        var f = new byte[footer];
        _fs.Seek(-footer, SeekOrigin.End);
        _fs.ReadExactly(f);
        if (BitConverter.ToUInt32(f, 17) != 0x5A6F12E1) throw new InvalidDataException("pak magic not found");
        if (f[16] != 0) throw new InvalidDataException("pak index is encrypted; not supported");
        Version = BitConverter.ToInt32(f, 21);
        if (Version < 10) throw new InvalidDataException($"pak version {Version}; this reader handles 10 and 11");
        long indexOffset = BitConverter.ToInt64(f, 25), indexSize = BitConverter.ToInt64(f, 33);
        CompressionMethods = new[] { "None" }.Concat(Enumerable.Range(0, 5)
            .Select(i => Encoding.ASCII.GetString(f, 61 + i * 32, 32).TrimEnd('\0'))).ToArray();

        var index = ReadAt(indexOffset, indexSize);
        int p = 0;
        MountPoint = Str(index, ref p);
        int count = I32(index, ref p);
        p += 8; // path hash seed
        if (I32(index, ref p) != 0) p += 8 + 8 + 20; // path hash index: not needed
        bool hasDirIndex = I32(index, ref p) != 0;
        long dirOffset = 0, dirSize = 0;
        if (hasDirIndex) { dirOffset = I64(index, ref p); dirSize = I64(index, ref p); p += 20; }
        int encodedSize = I32(index, ref p);
        var encoded = index.AsSpan(p, encodedSize).ToArray(); p += encodedSize;
        int unencodedCount = I32(index, ref p);
        var unencoded = new List<Entry>();
        for (int i = 0; i < unencodedCount; i++) unencoded.Add(ReadEntryHeader(index, ref p));
        if (!hasDirIndex) throw new InvalidDataException("pak has no full directory index");

        var dirs = ReadAt(dirOffset, dirSize);
        int q = 0;
        int dirCount = I32(dirs, ref q);
        for (int d = 0; d < dirCount; d++)
        {
            string dir = Str(dirs, ref q);
            int files = I32(dirs, ref q);
            for (int k = 0; k < files; k++)
            {
                string name = Str(dirs, ref q);
                int at = I32(dirs, ref q);
                Entry e;
                if (at >= 0) { int ep = at; e = DecodeEntry(encoded, ref ep); }
                else e = unencoded[-at - 1];
                e.Path = (MountPoint + dir + name).Replace("../../../", "");
                Entries.Add(e);
            }
        }
        _ = count;
    }

    /// <summary>Decodes one bit-packed FPakEntry (FPakFile::EncodePakEntry).</summary>
    private static Entry DecodeEntry(byte[] d, ref int p)
    {
        uint v = BitConverter.ToUInt32(d, p); p += 4;
        var e = new Entry();
        uint blockSize = (v & 0x3f) << 11;
        if ((v & 0x3f) == 0x3f) { blockSize = BitConverter.ToUInt32(d, p); p += 4; }
        e.BlockSize = blockSize;
        e.CompressionIndex = (int)((v >> 23) & 0x3f);
        e.Encrypted = (v & (1 << 22)) != 0;
        int blockCount = (int)((v >> 6) & 0xffff);
        bool offset32 = (v & (1u << 31)) != 0, usize32 = (v & (1 << 30)) != 0, size32 = (v & (1 << 29)) != 0;
        e.Offset = offset32 ? BitConverter.ToUInt32(d, p) : BitConverter.ToInt64(d, p); p += offset32 ? 4 : 8;
        e.UncompressedSize = usize32 ? BitConverter.ToUInt32(d, p) : BitConverter.ToInt64(d, p); p += usize32 ? 4 : 8;
        if (e.CompressionIndex != 0) { e.Size = size32 ? BitConverter.ToUInt32(d, p) : BitConverter.ToInt64(d, p); p += size32 ? 4 : 8; }
        else e.Size = e.UncompressedSize;

        // Block offsets are relative to the entry; the data starts after the serialized header.
        long header = 8 + 8 + 8 + 4 + 20 + (e.CompressionIndex != 0 ? 4 + blockCount * 16 : 0) + 1 + 4;
        if (blockCount > 0)
        {
            e.Blocks = new (long, long)[blockCount];
            if (blockCount == 1 && !e.Encrypted)
                e.Blocks[0] = (header, header + e.Size);
            else
            {
                long start = header;
                for (int b = 0; b < blockCount; b++)
                {
                    uint len = BitConverter.ToUInt32(d, p); p += 4;
                    e.Blocks[b] = (start, start + len);
                    start += e.Encrypted ? (len + 15) & ~15u : len;
                }
            }
        }
        return e;
    }

    private static Entry ReadEntryHeader(byte[] d, ref int p)
    {
        var e = new Entry();
        e.Offset = I64(d, ref p);
        e.Size = I64(d, ref p);
        e.UncompressedSize = I64(d, ref p);
        e.CompressionIndex = I32(d, ref p);
        p += 20;
        if (e.CompressionIndex != 0)
        {
            int n = I32(d, ref p);
            e.Blocks = new (long, long)[n];
            for (int b = 0; b < n; b++) e.Blocks[b] = (I64(d, ref p), I64(d, ref p));
        }
        e.Encrypted = d[p++] != 0;
        e.BlockSize = (uint)I32(d, ref p);
        return e;
    }

    public byte[] Read(Entry e)
    {
        if (e.Encrypted) throw new InvalidDataException($"{e.Path} is encrypted");
        if (e.CompressionIndex == 0)
        {
            // Uncompressed data sits after the entry's own serialized header.
            long header = 8 + 8 + 8 + 4 + 20 + 1 + 4;
            return ReadAt(e.Offset + header, e.Size);
        }
        string method = CompressionMethods[e.CompressionIndex];
        if (!method.Equals("Zlib", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"{e.Path}: compression '{method}' not supported");
        var output = new MemoryStream((int)e.UncompressedSize);
        foreach (var (start, end) in e.Blocks)
        {
            var block = ReadAt(e.Offset + start, end - start);
            using var z = new ZLibStream(new MemoryStream(block), CompressionMode.Decompress);
            z.CopyTo(output);
        }
        return output.ToArray();
    }

    private byte[] ReadAt(long offset, long size)
    {
        var b = new byte[size];
        _fs.Seek(offset, SeekOrigin.Begin);
        _fs.ReadExactly(b);
        return b;
    }

    private static int I32(byte[] d, ref int p) { int v = BitConverter.ToInt32(d, p); p += 4; return v; }
    private static long I64(byte[] d, ref int p) { long v = BitConverter.ToInt64(d, p); p += 8; return v; }

    private static string Str(byte[] d, ref int p)
    {
        int len = I32(d, ref p);
        if (len == 0) return "";
        if (len > 0) { var s = Encoding.Latin1.GetString(d, p, len - 1); p += len; return s; }
        var w = Encoding.Unicode.GetString(d, p, (-len - 1) * 2); p += -len * 2; return w;
    }

    public void Dispose() => _fs.Dispose();
}
