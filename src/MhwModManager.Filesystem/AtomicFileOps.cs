using MhwModManager.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MhwModManager.Filesystem;

public interface IAtomicReplaceBackend
{
    bool TryReplace(string replaced, string replacement, string? backup, out int errorCode);
}

public static class AtomicFileOps
{
    private const int BufferSize = 256 * 1024;
    private const int ErrorUnableToMoveReplacement = 1176;
    private const int ErrorUnableToMoveReplacement2 = 1177;
    private static readonly IAtomicReplaceBackend WindowsReplaceBackend = new Win32AtomicReplaceBackend();

    public static async Task ReplaceFromAsync(
        string source,
        string destination,
        IAtomicReplaceBackend? replaceBackend = null,
        string? expectedSha256 = null,
        bool requireDestinationAbsent = false,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"source={source}; destination={destination}");
        var dir = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException($"Destination has no parent directory: {destination}");
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".mhwmm.tmp");
        var preserveTemp = false;
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, BufferSize, ct);
                await output.FlushAsync(ct);
                output.Flush(true);
                if (expectedSha256 is not null)
                {
                    // Validate the private staged bytes, not a source path that could change
                    // between a separate validation and copy. Publication has not begun yet.
                    output.Position = 0;
                    var actual = Convert.ToHexString(await SHA256.HashDataAsync(output, ct));
                    if (!StringComparer.OrdinalIgnoreCase.Equals(actual, expectedSha256))
                        throw new InvalidDataException($"CAS integrity failure: staged replacement does not match SHA-256 {expectedSha256}.");
                }
            }

            ct.ThrowIfCancellationRequested();
            if (requireDestinationAbsent)
            {
                // Publish with a single no-overwrite rename. Do not pre-check
                // File.Exists here: another actor may create the destination
                // between a check and publication.
                File.Move(temp, destination, false);
            }
            else if (File.Exists(destination))
            {
                if (OperatingSystem.IsWindows())
                {
                    var backend = replaceBackend ?? WindowsReplaceBackend;
                    if (!backend.TryReplace(destination, temp, null, out var errorCode))
                    {
                        preserveTemp = errorCode is ErrorUnableToMoveReplacement or ErrorUnableToMoveReplacement2;
                        var recoveryNote = preserveTemp
                            ? $" Staged replacement retained at '{temp}' because this native failure can occur after pathname mutation."
                            : string.Empty;
                        throw new Win32Exception(errorCode, $"Atomic replacement failed for '{destination}'.{recoveryNote}");
                    }
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
            if (!preserveTemp)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }

    private sealed class Win32AtomicReplaceBackend : IAtomicReplaceBackend
    {
        [DllImport("kernel32.dll", EntryPoint="ReplaceFileW", CharSet=CharSet.Unicode, SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReplaceFile(string replaced, string replacement, string? backup, uint flags, nint exclude, nint reserved);

        public bool TryReplace(string replaced, string replacement, string? backup, out int errorCode)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            var success = ReplaceFile(replaced,replacement,backup,0,0,0);
            errorCode = success ? 0 : Marshal.GetLastWin32Error();
            return success;
        }
    }
}
