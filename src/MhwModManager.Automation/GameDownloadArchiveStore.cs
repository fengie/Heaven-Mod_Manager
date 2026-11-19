using System.Security.Cryptography;
using System.Text;
using MhwModManager.Core;

namespace MhwModManager.Automation;

/// <summary>
/// Preserves a successfully acquired, hash-verified archive under the owning game's Downloads
/// folder. The catalog's separate lease-owned scratch archive remains temporary and is cleaned
/// by its existing lifecycle; this is durable user data and is never auto-reaped.
/// </summary>
public static class GameDownloadArchiveStore
{
    private const int BufferSize=128*1024;

    // Only identical content within one game's Downloads root is serialized. The
    // reference count keeps completed/cancelled acquisitions from retaining locks.
    private static readonly object saveGateSync = new();
    private static readonly Dictionary<string, InFlightSave> activeSaves =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class InFlightSave
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users;
    }

    private static InFlightSave RentGate(string key)
    {
        lock (saveGateSync)
        {
            if (!activeSaves.TryGetValue(key, out var gate))
            {
                gate = new InFlightSave();
                activeSaves.Add(key, gate);
            }
            gate.Users++;
            return gate;
        }
    }

    private static void ReturnGate(string key, InFlightSave gate)
    {
        lock (saveGateSync)
        {
            gate.Users--;
            if (gate.Users == 0)
            {
                activeSaves.Remove(key);
                gate.Semaphore.Dispose();
            }
        }
    }


    public static async Task<string> SaveAsync(
        string downloadsRoot,string sourceArchive,string modName,string expectedSha256,CancellationToken ct=default)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceArchive);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        if(expectedSha256.Length!=64||expectedSha256.Any(ch=>!Uri.IsHexDigit(ch)))
            throw new InvalidDataException("Saved archive requires a full verified SHA-256 digest.");
        ct.ThrowIfCancellationRequested();

        var root=Path.GetFullPath(downloadsRoot);
        // GameModStorage resolves the owning game root. Refuse a swapped linked directory
        // instead of following a symlink/junction into another game's files.
        var parent=Path.GetDirectoryName(root)
            ?? throw new InvalidDataException("Download library has no owning game directory.");
        foreach(var path in new[]{parent,root})
        {
            if(Directory.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
                throw new IOException($"Download library cannot use a reparse-point directory: {path}");
        }
        Directory.CreateDirectory(root);
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Download library became a reparse-point directory.");

        var slug=new StringBuilder();
        var previousSeparator=false;
        foreach(var ch in modName)
        {
            if(slug.Length>=60)break;
            if(char.IsLetterOrDigit(ch))
            {
                slug.Append(ch);
                previousSeparator=false;
            }
            else if(!previousSeparator&&slug.Length>0)
            {
                slug.Append('-');
                previousSeparator=true;
            }
        }
        var name=slug.ToString().TrimEnd('-');
        if(string.IsNullOrWhiteSpace(name))name="Mod";
        var extension=Path.GetExtension(sourceArchive).ToLowerInvariant();
        if(extension is not ".zip" and not ".7z" and not ".rar")extension=".archive";
        var sha=expectedSha256.ToLowerInvariant();

        // Two aliases can otherwise both pass the final directory scan before
        // either atomically publishes a differently named destination.
        var key=root+"|"+sha+"|"+extension;
        var gate=RentGate(key);
        var acquired=false;
        try
        {
            await gate.Semaphore.WaitAsync(ct);
            acquired=true;
            // Revalidate after waiting: another actor may have swapped the root.
            foreach (var path in new[]{parent,root})
            {
                if (Directory.Exists(path) &&
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Download library became a reparse-point directory.");
            }
            return await SaveVerifiedUnderGateAsync(
                root, sourceArchive, name, sha, extension, ct);
        }
        finally
        {
            if (acquired) gate.Semaphore.Release();
            ReturnGate(key, gate);
        }
    }

    private static async Task<string> SaveVerifiedUnderGateAsync(
        string root, string sourceArchive, string name,
        string sha, string extension, CancellationToken ct)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        var destination=Path.Combine(root,$"{name}-{sha}{extension}");
        // The display name is not part of content identity. Reuse a previously
        // verified archive within this game's Downloads root instead of storing
        // identical bytes again under a different mod title.
        // The common same-title retry remains an O(1) file lookup.
        if(File.Exists(destination))
        {
            await VerifyExistingAsync(destination,sha,ct);
            return destination;
        }
        var retained = await FindVerifiedExistingAsync(root,sha,extension,ct);
        if(retained is not null) return retained;

        var temporary=Path.Combine(root,$".umm-owned-{Guid.NewGuid():N}.tmp");
        try
        {
            using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using(var input=new FileStream(sourceArchive,FileMode.Open,FileAccess.Read,FileShare.Read,BufferSize,FileOptions.Asynchronous|FileOptions.SequentialScan))
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,BufferSize,FileOptions.Asynchronous|FileOptions.WriteThrough))
            {
                var buffer=new byte[BufferSize];
                int read;
                while((read=await input.ReadAsync(buffer,ct))>0)
                {
                    digest.AppendData(buffer,0,read);
                    await output.WriteAsync(buffer.AsMemory(0,read),ct);
                }
                await output.FlushAsync(ct);
                output.Flush(flushToDisk:true);
            }
            if(!string.Equals(Convert.ToHexString(digest.GetHashAndReset()),sha,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Downloaded archive bytes changed before durable retention.");

            ct.ThrowIfCancellationRequested();
            // Another acquisition can finish while we are copying. Recheck
            // before publication, and let finally delete this owned temporary.
            retained = await FindVerifiedExistingAsync(root,sha,extension,ct);
            if(retained is not null) return retained;

            try{File.Move(temporary,destination);}
            catch(IOException) when(File.Exists(destination))
            {
                await VerifyExistingAsync(destination,sha,ct);
            }
            return destination;
        }
        finally
        {
            // Only a file with this invocation's unique name can be removed.
            if(File.Exists(temporary))File.Delete(temporary);
        }
    }

    /// <summary>
    /// Find an existing verified artifact with the same digest and archive type.
    /// Never trust a digest merely because it appears in a user-editable filename.
    /// Only the game's own top-level Downloads root is inspected; other games,
    /// unknown extensions and user Mods/State are untouched.
    /// </summary>
    private static async Task<string?> FindVerifiedExistingAsync(
        string downloadsRoot,string digest,string extension,CancellationToken ct)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        var suffix="-"+digest+extension;
        // Directory enumeration can take time in a large user library. Keep
        // this optional alias lookup off the WPF dispatcher and cancellable.
        var matches=await Task.Run(
            () => Directory.EnumerateFiles(
                    downloadsRoot,"*"+suffix,SearchOption.TopDirectoryOnly)
                .Where(file =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Path.GetFileName(file) is string name &&
                        name.EndsWith(suffix,StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(file=>file,StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ct);
        foreach(var match in matches)
        {
            ct.ThrowIfCancellationRequested();
            await VerifyExistingAsync(match,digest,ct);
            return match;
        }
        return null;
    }

    private static async Task VerifyExistingAsync(string destination,string expectedSha256,CancellationToken ct)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        if((File.GetAttributes(destination)&FileAttributes.ReparsePoint)!=0)
            throw new InvalidDataException("Saved download destination is a reparse point.");
        await using var input=new FileStream(destination,FileMode.Open,FileAccess.Read,FileShare.Read,BufferSize,FileOptions.Asynchronous|FileOptions.SequentialScan);
        var hash=Convert.ToHexString(await SHA256.HashDataAsync(input,ct));
        if(!string.Equals(hash,expectedSha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved download exists but its contents do not match the recorded SHA-256.");
    }
}
