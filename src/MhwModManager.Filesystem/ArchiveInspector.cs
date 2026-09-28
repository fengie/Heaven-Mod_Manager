using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using SharpCompress.Archives;
using SharpCompress.Common;
using MhwModManager.Core;

namespace MhwModManager.Filesystem;

public sealed record ArchiveEntryInfo(string Key,long Size,bool IsDirectory);
public sealed record ArchiveInspection(IReadOnlyList<ArchiveEntryInfo> Entries,bool HasNativePc,bool HasGameRoot,bool HasSuspiciousPaths,long ExpandedBytes,string? CommonWrapper);
public sealed record ArchiveExtractionLimits(long MaxDeclaredExpandedBytes,long MaxActualOutputBytes);

public enum ArchiveExtractionFaultPoint
{
    BeforePayloadWrite,
    BeforeOwnedOutputCleanup
}

public sealed class ArchiveInspector
{
    private readonly Action<ArchiveExtractionFaultPoint,string> injectFault;

    public ArchiveInspector(Action<ArchiveExtractionFaultPoint,string>? faultInjector=null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        injectFault=faultInjector??((_,_)=>{});
    }

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

    public async Task ExtractSafelyAsync(string archivePath,string destination,string trustedRoot,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"archive={archivePath}; destination={destination}; trustedRoot={trustedRoot}");
        await ExtractSafelyAsync(
            archivePath,
            destination,
            trustedRoot,
            new ArchiveExtractionLimits(MaxExpandedBytes,MaxExpandedBytes),
            ct).ConfigureAwait(false);
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "ArchiveInspector is intentionally an injectable instance service used by the application and integration tests.")]
    public async Task ExtractSafelyAsync(
        string archivePath,
        string destination,
        string trustedRoot,
        ArchiveExtractionLimits limits,
        CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"archive={archivePath}; destination={destination}; trustedRoot={trustedRoot}");
        ArgumentNullException.ThrowIfNull(limits);
        if(limits.MaxDeclaredExpandedBytes<=0)throw new ArgumentOutOfRangeException(nameof(limits),"Declared expansion budget must be positive.");
        if(limits.MaxActualOutputBytes<=0)throw new ArgumentOutOfRangeException(nameof(limits),"Actual output budget must be positive.");

        var trusted=Path.GetFullPath(trustedRoot).TrimEnd(Path.DirectorySeparatorChar);
        var destinationFull=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        var trustedPrefix=trusted+Path.DirectorySeparatorChar;
        if(!destinationFull.Equals(trusted,StringComparison.OrdinalIgnoreCase) && !destinationFull.StartsWith(trustedPrefix,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Extraction destination is outside the trusted root.");
        if(!Directory.Exists(trusted))throw new InvalidDataException("Trusted extraction root does not exist.");
        EnsureSafeDirectoryPath(trusted,destinationFull);
        var root=destinationFull+Path.DirectorySeparatorChar;
        using var archive=ArchiveFactory.OpenArchive(archivePath);
        long declaredTotal=0;
        long actualTotal=0;
        int count=0;
        foreach(var e in archive.Entries.Where(x=>!x.IsDirectory))
        {
            ct.ThrowIfCancellationRequested();
            count++;
            declaredTotal=checked(declaredTotal+e.Size);
            if(count>MaxEntries||declaredTotal>limits.MaxDeclaredExpandedBytes)
                throw new InvalidDataException("Archive expansion safety limit exceeded.");
            var key=(e.Key??"").Replace('/',Path.DirectorySeparatorChar).Replace('\\',Path.DirectorySeparatorChar);
            if(!PathRules.IsSafeArchiveRelativePath(key))throw new InvalidDataException($"Unsafe archive path: {e.Key}");
            var dest=Path.GetFullPath(Path.Combine(destinationFull,key));
            if(!dest.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Archive path traversal detected.");
            var parent=Path.GetDirectoryName(dest)!;
            EnsureSafeDirectoryPath(trusted,parent);

            var createdOutput=false;
            try
            {
                using var source=await e.OpenEntryStreamAsync(ct).ConfigureAwait(false);
                await using var output=new FileStream(
                    dest,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128*1024,
                    FileOptions.Asynchronous|FileOptions.SequentialScan);
                createdOutput=true;
                var buffer=ArrayPool<byte>.Shared.Rent(128*1024);
                try
                {
                    while(true)
                    {
                        ct.ThrowIfCancellationRequested();
                        var read=await source.ReadAsync(buffer.AsMemory(0,buffer.Length),ct).ConfigureAwait(false);
                        if(read==0)break;
                        if(read>limits.MaxActualOutputBytes-actualTotal)
                            throw new InvalidDataException("Archive actual-output safety limit exceeded.");
                        injectFault(ArchiveExtractionFaultPoint.BeforePayloadWrite,dest);
                        await output.WriteAsync(buffer.AsMemory(0,read),ct).ConfigureAwait(false);
                        actualTotal+=read;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch(Exception primaryFailure) when(createdOutput)
            {
                TryDeleteOwnedOutput(dest,primaryFailure);
                throw;
            }
        }
    }

    private void TryDeleteOwnedOutput(string path,Exception primaryFailure)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}; primary={primaryFailure.GetType().Name}");
        try
        {
            injectFault(ArchiveExtractionFaultPoint.BeforeOwnedOutputCleanup,path);
            File.Delete(path);
        }
        catch(Exception cleanupFailure)
        {
            MasterDebugLog.Write(
                "ARCHIVE-CLEANUP",
                $"Failed to remove owned partial archive output '{path}'. The primary {primaryFailure.GetType().Name} remains authoritative.",
                cleanupFailure);
        }
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "ArchiveInspector is intentionally an injectable instance service used by the application and integration tests.")]
    public void ExtractSafely(string archivePath,string destination,string trustedRoot,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"archive={archivePath}; destination={destination}; trustedRoot={trustedRoot}");
        ExtractSafelyAsync(archivePath,destination,trustedRoot,ct).GetAwaiter().GetResult();
    }
    private static void EnsureSafeDirectoryPath(string root,string directory)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var stop=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var target=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        var stopPrefix=stop+Path.DirectorySeparatorChar;
        if(!target.Equals(stop,StringComparison.OrdinalIgnoreCase) && !target.StartsWith(stopPrefix,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Extraction directory is outside the trusted root.");

        if(IsReparsePoint(stop))throw new InvalidDataException($"Extraction path traverses a reparse point: {stop}");
        if(target.Equals(stop,StringComparison.OrdinalIgnoreCase))return;

        var relative=Path.GetRelativePath(stop,target);
        var current=stop;
        foreach(var segment in relative.Split(Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries))
        {
            current=Path.Combine(current,segment);
            if(Directory.Exists(current))
            {
                if(IsReparsePoint(current))throw new InvalidDataException($"Extraction path traverses a reparse point: {current}");
                continue;
            }

            Directory.CreateDirectory(current);
            if(IsReparsePoint(current))throw new InvalidDataException($"Extraction path traverses a reparse point: {current}");
        }
    }

    private static bool IsReparsePoint(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }
}
