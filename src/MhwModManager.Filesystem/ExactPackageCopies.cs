using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using MhwModManager.Core;
using MhwModManager.Storage;
using System.Text.Json;

namespace MhwModManager.Filesystem;

// Source trees include metadata, documentation and empty folders; deployed-file indexes alone
// cannot prove that two editable packages are interchangeable.
public sealed class ExactPackageCopies(ManagerDatabase? database = null)
{
    private sealed record Source(string Shape, FileInfo[] Files);
    private readonly record struct Stamp(ulong Volume,ulong Low,ulong High,long Changed,long Length);
    private sealed record CachedDigest(Stamp Stamp,byte[] Digest);
    private readonly Dictionary<string,CachedDigest> cache = new(PathRules.Comparer);
    private const int CacheLimit = 8192;
    private const int CharacterLimit = 4 * 1024 * 1024;
    private const string CacheSetting = "catalog:exact-source-sha256:ntfs-v1";
    private bool cacheLoaded, cacheChanged;
    public long LastBytesHashed { get; private set; }

    public async Task<IReadOnlyList<string[]>> FindAsync(IReadOnlyList<ModDescriptor> mods,CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        LastBytesHashed = 0;
        if (database != null && !cacheLoaded)
        {
            var saved=await database.GetSettingAsync(CacheSetting,ct);
            if (saved is {Length: >0 and <= CharacterLimit})
            {
                try
                {
                    var entries=JsonSerializer.Deserialize<Dictionary<string,CachedDigest?>>(saved);
                    if (entries is {Count: <= CacheLimit})
                        foreach(var (path,entry) in entries)
                            if (entry is {Digest.Length:32} && Path.IsPathFullyQualified(path)) cache[path]=entry;
                }
                catch (JsonException) { cacheChanged = true; }
            }
            cacheLoaded = true;
        }
        var paths = mods.Where(m=>Path.IsPathFullyQualified(m.SourcePath)).ToLookup(m=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(m.SourcePath)),PathRules.Comparer);
        var overlaps = new HashSet<string>(PathRules.Comparer);
        foreach (var group in paths)
        {
            if (group.Count()>1) overlaps.Add(group.Key);
            for (var parent=Path.GetDirectoryName(group.Key); parent!=null; parent=Path.GetDirectoryName(parent))
                if (paths.Contains(parent)) {overlaps.Add(parent); overlaps.Add(group.Key);}
        }
        var sources = new List<(string Id,Source Source)>();
        foreach (var group in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (overlaps.Contains(group.Key) || PathRules.Comparer.Equals(Path.GetPathRoot(group.Key),group.Key)) continue;
            try
            {
                var source = Inspect(group.Key,ct);
                if (source != null) sources.Add((group.First().Id,source));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var result = new List<string[]>();
        foreach (var group in sources.GroupBy(item=>item.Source.Shape).Where(items=>items.Count()>1))
        {
            var matches = new List<(string Id,string Hash)>();
            foreach (var item in group)
            {
                try { matches.Add((item.Id,await ContentAsync(item.Source,ct))); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            result.AddRange(matches.GroupBy(item=>item.Hash).Where(items=>items.Count()>1)
                .Select(items=>items.Select(item=>item.Id).ToArray()));
        }
        if (database != null && cacheChanged)
        {
            var saved = JsonSerializer.Serialize(cache);
            await database.SetSettingAsync(CacheSetting,saved.Length<=CharacterLimit ? saved : "{}",ct);
            cacheChanged = false;
        }
        return result;
    }

    public static async Task<bool> SameContentsAsync(string source,string keeper,CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        try
        {
            var left=Inspect(source,ct);
            var right=Inspect(keeper,ct);
            if(left==null || right==null || left.Shape!=right.Shape) return false;
            var fresh=new ExactPackageCopies();
            return await fresh.ContentAsync(left,ct)==await fresh.ContentAsync(right,ct);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {return false;}
    }

    private static Source? Inspect(string root,CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        if (!Directory.Exists(root)) return null;
        for (var parent=new DirectoryInfo(root); parent!=null; parent=parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint)!=0) return null;
        var tree = SafeRecursiveTraversal.Snapshot(root,ct);
        if (tree.Files.Count==0) return null;
        var files = tree.Files.Order(StringComparer.Ordinal).Select(file=>new FileInfo(file)).ToArray();
        using var shape = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var dir in tree.Directories.Order(StringComparer.Ordinal))
            Append(shape,"D"+Path.GetRelativePath(root,dir));
        Span<byte> size = stackalloc byte[8];
        foreach (var file in files)
        {
            Append(shape,"F"+Path.GetRelativePath(root,file.FullName));
            BinaryPrimitives.WriteInt64LittleEndian(size,file.Length);
            shape.AppendData(size);
        }
        return new(Convert.ToHexString(shape.GetHashAndReset()),files);
    }

    private static void Append(IncrementalHash hash,string name)
    {
        using var trace = MasterDebugLog.BeginMethod();
        var value=Encoding.UTF8.GetBytes(name);
        Span<byte> size=stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size,value.Length);
        hash.AppendData(size);
        hash.AppendData(value);
    }

    private async Task<string> ContentAsync(Source source,CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        using var content=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in source.Files)
        {
            ct.ThrowIfCancellationRequested();
            using var stream=File.Open(file.FullName,FileMode.Open,FileAccess.Read,FileShare.Read);
            if (stream.Length!=file.Length) throw new IOException("Package source changed during comparison.");
            var stamp=ReadStamp(stream);
            if (stamp!=null && cache.TryGetValue(file.FullName,out var saved) && saved.Stamp==stamp.Value)
                content.AppendData(saved.Digest);
            else
            {
                var digest=await SHA256.HashDataAsync(stream,ct);
                LastBytesHashed+=stream.Length;
                if (stamp!=null && stamp!=ReadStamp(stream)) throw new IOException("Package source changed during hashing.");
                if (stamp!=null)
                {
                    if (cache.Count>=CacheLimit) cache.Clear();
                    cache[file.FullName]=new(stamp.Value,digest);
                    cacheChanged=true;
                }
                content.AppendData(digest);
            }
        }
        return Convert.ToHexString(content.GetHashAndReset());
    }

    private static Stamp? ReadStamp(FileStream file)
    {
        using var trace = MasterDebugLog.BeginMethod();
        // Avoid trusting file size and LastWriteTime: both can survive an in-place edit.
        // Only NTFS's change time paired with the volume and file ID qualifies for reuse.
        if (!OperatingSystem.IsWindows() || new DriveInfo(Path.GetPathRoot(file.Name)!).DriveFormat!="NTFS") return null;
        if (!GetBasic(file.SafeFileHandle,0,out var basic,40) || basic.Changed<=0 ||
            !GetIdentity(file.SafeFileHandle,18,out var id,24) || (id.Low==0 && id.High==0)) return null;
        return new(id.Volume,id.Low,id.High,basic.Changed,file.Length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Basic { public long Creation,Access,Write,Changed; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Identity { public ulong Volume,Low,High; }
    [DllImport("kernel32.dll",EntryPoint="GetFileInformationByHandleEx",ExactSpelling=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetBasic(SafeFileHandle handle,int info,out Basic value,uint size);
    [DllImport("kernel32.dll",EntryPoint="GetFileInformationByHandleEx",ExactSpelling=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIdentity(SafeFileHandle handle,int info,out Identity value,uint size);
}
