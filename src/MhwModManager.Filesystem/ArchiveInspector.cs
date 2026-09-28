using System.Diagnostics.CodeAnalysis;
using SharpCompress.Archives;
using SharpCompress.Common;
using MhwModManager.Core;

namespace MhwModManager.Filesystem;

public sealed record ArchiveEntryInfo(string Key,long Size,bool IsDirectory);
public sealed record ArchiveInspection(IReadOnlyList<ArchiveEntryInfo> Entries,bool HasNativePc,bool HasGameRoot,bool HasSuspiciousPaths,long ExpandedBytes,string? CommonWrapper);

public sealed class ArchiveInspector
{
    public const int MaxEntries = 200_000;
    public const long MaxExpandedBytes = 200L * 1024 * 1024 * 1024;

    public Task<ArchiveInspection> InspectAsync(string archivePath, CancellationToken ct=default) =>
        Task.Run(() => Inspect(archivePath,ct), ct);

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "ArchiveInspector is intentionally an injectable instance service used by the application and integration tests.")]
    public ArchiveInspection Inspect(string archivePath, CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"archive={archivePath}");
        using var archive=ArchiveFactory.OpenArchive(archivePath);
        var entries=new List<ArchiveEntryInfo>();
        long total=0;
        bool bad=false;
        var top=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var e in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var key=(e.Key??"").Replace('/','\\');
            bad |= !PathRules.IsSafeArchiveRelativePath(key);
            if(!e.IsDirectory) total=checked(total+e.Size);
            if (total > MaxExpandedBytes) throw new InvalidDataException("Archive expanded-size safety limit exceeded.");
            var first=key.Split('\\',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if(first is not null)top.Add(first);
            entries.Add(new(key,e.Size,e.IsDirectory));
            if(entries.Count>MaxEntries)throw new InvalidDataException("Archive contains an excessive number of entries.");
        }
        var hasNative=entries.Any(x=>x.Key.Split('\\').Any(s=>s.Equals("nativePC",StringComparison.OrdinalIgnoreCase)));
        var hasRoot=entries.Any(x=>x.Key.Split('\\').Any(s=>s.Equals("GameRoot",StringComparison.OrdinalIgnoreCase)));
        return new(entries,hasNative,hasRoot,bad,total,top.Count==1?top.First():null);
    }

    public Task ExtractSafelyAsync(string archivePath,string destination,string trustedRoot,CancellationToken ct=default) =>
        Task.Run(() => ExtractSafely(archivePath,destination,trustedRoot,ct), ct);

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "ArchiveInspector is intentionally an injectable instance service used by the application and integration tests.")]
    public void ExtractSafely(string archivePath,string destination,string trustedRoot,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"archive={archivePath}; destination={destination}; trustedRoot={trustedRoot}");
        var trusted=Path.GetFullPath(trustedRoot).TrimEnd(Path.DirectorySeparatorChar);
        var destinationFull=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        var trustedPrefix=trusted+Path.DirectorySeparatorChar;
        if(!destinationFull.Equals(trusted,StringComparison.OrdinalIgnoreCase) && !destinationFull.StartsWith(trustedPrefix,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Extraction destination is outside the trusted root.");
        if(!Directory.Exists(trusted))throw new InvalidDataException("Trusted extraction root does not exist.");
        EnsureNoReparsePoint(trusted,Path.GetDirectoryName(destinationFull)??trusted);
        if (Directory.Exists(destinationFull) && IsReparsePoint(destinationFull))
            throw new InvalidDataException("Extraction destination is a reparse point.");
        Directory.CreateDirectory(destinationFull);
        var root=destinationFull+Path.DirectorySeparatorChar;
        using var archive=ArchiveFactory.OpenArchive(archivePath);
        long total=0;
        int count=0;
        foreach(var e in archive.Entries.Where(x=>!x.IsDirectory))
        {
            ct.ThrowIfCancellationRequested();
            count++;
            total=checked(total+e.Size);
            if(count>MaxEntries||total>MaxExpandedBytes)throw new InvalidDataException("Archive expansion safety limit exceeded.");
            var key=(e.Key??"").Replace('/',Path.DirectorySeparatorChar).Replace('\\',Path.DirectorySeparatorChar);
            if (!PathRules.IsSafeArchiveRelativePath(key)) throw new InvalidDataException($"Unsafe archive path: {e.Key}");
            var dest=Path.GetFullPath(Path.Combine(destinationFull,key));
            if(!dest.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Archive path traversal detected.");
            var parent = Path.GetDirectoryName(dest)!;
            Directory.CreateDirectory(parent);
            EnsureNoReparsePoint(trusted,parent);
            e.WriteToFile(dest,new ExtractionOptions{ExtractFullPath=false,Overwrite=false});
        }
    }

    private static void EnsureNoReparsePoint(string root,string directory)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var current=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        var stop=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var stopPrefix=stop+Path.DirectorySeparatorChar;
        while(current.Equals(stop,StringComparison.OrdinalIgnoreCase) || current.StartsWith(stopPrefix,StringComparison.OrdinalIgnoreCase))
        {
            if(Directory.Exists(current) && IsReparsePoint(current))throw new InvalidDataException($"Extraction path traverses a reparse point: {current}");
            if(current.Equals(stop,StringComparison.OrdinalIgnoreCase))break;
            var parent=Directory.GetParent(current)?.FullName;
            if(parent is null)break;
            current=parent.TrimEnd(Path.DirectorySeparatorChar);
        }
    }

    private static bool IsReparsePoint(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }
}
