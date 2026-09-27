using MhwModManager.Core;
using System.Collections.Concurrent;

namespace MhwModManager.Filesystem;

public sealed record FileChangeHint(IReadOnlyList<string> Paths,bool WatcherOverflowed,DateTimeOffset ObservedAt);

/// <summary>
/// FileSystemWatcher-backed cache invalidation hints. These events are explicitly non-authoritative;
/// callers must reconcile against the filesystem before any destructive decision.
/// </summary>
public sealed class FileChangeHintService : IDisposable
{
    private readonly List<FileSystemWatcher> watchers=[];
    private readonly ConcurrentDictionary<string,byte> dirty=new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer timer;
    private int overflowed;

    public event Action<FileChangeHint>? HintsAvailable;

    public FileChangeHintService(params string[] roots)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach(var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var watcher=new FileSystemWatcher(root)
            {
                IncludeSubdirectories=true,
                NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite|NotifyFilters.Size,
                InternalBufferSize=64*1024,
                EnableRaisingEvents=true
            };
            watcher.Changed+=OnChanged;
            watcher.Created+=OnChanged;
            watcher.Deleted+=OnChanged;
            watcher.Renamed+=OnRenamed;
            watcher.Error+=OnError;
            watchers.Add(watcher);
        }
        // Idle polling must not open/flush two method-log records every 750 ms.
        // Flush still owns the traced boundary whenever there is actual work.
        timer=new Timer(_=>
        {
            if(!dirty.IsEmpty||Volatile.Read(ref overflowed)!=0)Flush();
        },null,750,750);
    }

    private void OnChanged(object sender,FileSystemEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"changeType={e.ChangeType}; path={e.FullPath}");
        MasterDebugLog.Write("FS-WATCH", $"{e.ChangeType}: {e.FullPath}");
        dirty[e.FullPath]=0;
    }
    private void OnRenamed(object sender,RenamedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"old={e.OldFullPath}; new={e.FullPath}");
        MasterDebugLog.Write("FS-WATCH", $"RENAMED: {e.OldFullPath} -> {e.FullPath}");
        dirty[e.OldFullPath]=0;
        dirty[e.FullPath]=0;
    }
    private void OnError(object sender,ErrorEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        MasterDebugLog.Write("FS-WATCH", "FileSystemWatcher error/overflow", e.GetException());
        Interlocked.Exchange(ref overflowed,1);
    }

    private void Flush()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(dirty.IsEmpty&&Volatile.Read(ref overflowed)==0)return;
        var paths=dirty.Keys.ToArray();
        foreach(var p in paths)dirty.TryRemove(p,out _);
        var overflow=Interlocked.Exchange(ref overflowed,0)!=0;
        try{HintsAvailable?.Invoke(new(paths,overflow,DateTimeOffset.UtcNow));}catch{}
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        timer.Dispose();
        foreach(var w in watchers)w.Dispose();
        watchers.Clear();
        GC.SuppressFinalize(this);
    }
}
