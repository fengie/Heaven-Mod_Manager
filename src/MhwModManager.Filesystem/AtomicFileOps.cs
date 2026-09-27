using MhwModManager.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MhwModManager.Filesystem;

public static class AtomicFileOps
{
    private const int BufferSize = 256 * 1024;

    [DllImport("kernel32.dll", EntryPoint="ReplaceFileW", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReplaceFile(string replaced, string replacement, string? backup, uint flags, nint exclude, nint reserved);

    public static async Task ReplaceFromAsync(string source, string destination, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"source={source}; destination={destination}");
        var dir = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException($"Destination has no parent directory: {destination}");
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".mhwmm.tmp");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, BufferSize, ct);
                await output.FlushAsync(ct);
                output.Flush(true);
            }

            ct.ThrowIfCancellationRequested();
            if (File.Exists(destination))
            {
                if (OperatingSystem.IsWindows())
                {
                    if (!ReplaceFile(destination,temp,null,0,0,0))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), $"Atomic replacement failed for '{destination}'.");
                }
                else
                {
                    File.Move(temp,destination,true);
                }
            }
            else
            {
                // Same-directory rename is atomic on the supported local filesystems.
                File.Move(temp,destination,false);
            }
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}
