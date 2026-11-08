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

    public static async Task<string> SaveAsync(
        string downloadsRoot,string sourceArchive,string modName,string expectedSha256,CancellationToken ct=default)
    {
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
        var destination=Path.Combine(root,$"{name}-{sha}{extension}");
        if(File.Exists(destination))
        {
            await VerifyExistingAsync(destination,sha,ct);
            return destination;
        }

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

    private static async Task VerifyExistingAsync(string destination,string expectedSha256,CancellationToken ct)
    {
        if((File.GetAttributes(destination)&FileAttributes.ReparsePoint)!=0)
            throw new InvalidDataException("Saved download destination is a reparse point.");
        await using var input=new FileStream(destination,FileMode.Open,FileAccess.Read,FileShare.Read,BufferSize,FileOptions.Asynchronous|FileOptions.SequentialScan);
        var hash=Convert.ToHexString(await SHA256.HashDataAsync(input,ct));
        if(!string.Equals(hash,expectedSha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved download exists but its contents do not match the recorded SHA-256.");
    }
}
