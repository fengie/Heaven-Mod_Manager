using System.Buffers.Binary;
using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.Automation;

/// <summary>Compares legacy IoStore export package IDs in captured TOC bytes, without decompressing game assets.</summary>
internal sealed record IoStoreExportInventory(IReadOnlyDictionary<string,string> SharedWith, IReadOnlySet<string> Unknown)
{
    public static IoStoreExportInventory Read(IReadOnlyCollection<string> enabled,
        IReadOnlyDictionary<string,IReadOnlyList<ModFileDescriptor>> files, BlobStore? store, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var firstProvider = new Dictionary<ulong,string>();
        var shared = new Dictionary<string,string>(PathRules.Comparer);
        var unknown = new HashSet<string>(PathRules.Comparer);
        foreach (var id in enabled.Order(PathRules.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            if (!files.TryGetValue(id,out var package)) continue;
            var mounts = package.Where(file => UnrealPakLayout.IsMember(file.Path))
                .GroupBy(file => Path.ChangeExtension(file.Path,null),PathRules.Comparer);
            foreach (var mount in mounts)
            {
                var toc = mount.FirstOrDefault(file => file.Path.EndsWith(".utoc",StringComparison.OrdinalIgnoreCase));
                if (toc is null || store is null || toc.BlobSha256.Length != 64 || !toc.BlobSha256.All(char.IsAsciiHexDigit) ||
                    ReadExports(store.PathFor(toc.BlobSha256), ct) is not { } exports)
                {
                    unknown.Add(id);
                    continue;
                }
                foreach (var export in exports)
                {
                    if (firstProvider.TryAdd(export,id)) continue;
                    var previous = firstProvider[export];
                    if (PathRules.Comparer.Equals(previous,id)) continue;
                    shared.TryAdd(previous,id);
                    shared.TryAdd(id,previous);
                }
            }
        }
        return new(shared,unknown);
    }

    // Narrow version 2 / legacy EIoChunkType layout. UE5 assigns different meanings to the same numeric tags.
    // Layout reference: CUE4Parse ecc4878950336126f125af0747190edf474b2a21, FIoChunkId and FIoStoreTocHeader.
    private static HashSet<ulong>? ReadExports(string path, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[144];
            if (stream.Length < header.Length) return null;
            stream.ReadExactly(header);
            if (!header[..16].SequenceEqual("-==--==--==--==-"u8) || header[16] != 2 ||
                BinaryPrimitives.ReadUInt32LittleEndian(header[20..]) != header.Length) return null;
            var count = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
            if (count > 65536 || stream.Length - header.Length < 12L * count) return null;
            var entries = new byte[12 * count];
            stream.ReadExactly(entries);
            var exports = new HashSet<ulong>();
            var containerHeader = false;
            for (var i = 0; i < entries.Length; i += 12)
            {
                ct.ThrowIfCancellationRequested();
                var type = entries[i + 11];
                if (type is 0 or 1 or > 10) return null;
                if (type == 10) containerHeader = true;
                if (type == 2) exports.Add(BinaryPrimitives.ReadUInt64LittleEndian(entries.AsSpan(i, 8)));
            }
            return containerHeader && exports.Count > 0 ? exports : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MasterDebugLog.Write("AUTO-POPULATE", "Cannot inspect captured IoStore export table.", ex);
            return null;
        }
    }
}
