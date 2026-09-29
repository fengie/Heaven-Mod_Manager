using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using MhwModManager.Core;

namespace MhwModManager.Filesystem;

/// <summary>
/// Stores catalog provider secrets in Windows Credential Manager. Environment variables and the
/// historical Nexus key file are read-only development/compatibility fallbacks; secrets entered
/// through the catalog UI are never written to the repository, database, logs, or plaintext state.
/// </summary>
public sealed class CatalogCredentialStore
{
    private readonly string nextStateRoot;

    public CatalogCredentialStore(string nextStateRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.nextStateRoot = nextStateRoot;
    }

    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    public string? ReadSecret(string providerId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}");
        var normalized = NormalizeProviderId(providerId);
        if (normalized.Equals("nexus", StringComparison.Ordinal))
        {
            var environment = Environment.GetEnvironmentVariable("NEXUS_API_KEY");
            if (!string.IsNullOrWhiteSpace(environment)) return environment.Trim();
        }

        if (OperatingSystem.IsWindows() &&
            CredRead(BuildTarget(normalized), CredTypeGeneric, 0, out var credentialPtr))
        {
            try
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
                if (credential.CredentialBlob != IntPtr.Zero && credential.CredentialBlobSize > 0)
                {
                    var secret = Marshal.PtrToStringUni(
                        credential.CredentialBlob,
                        checked((int)credential.CredentialBlobSize / sizeof(char)));
                    if (!string.IsNullOrWhiteSpace(secret)) return secret.TrimEnd('\0').Trim();
                }
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }

        if (normalized.Equals("nexus", StringComparison.Ordinal))
        {
            var legacyPath = Path.Combine(nextStateRoot, "nexus-api-key.txt");
            if (File.Exists(legacyPath))
            {
                var legacy = File.ReadAllText(legacyPath).Trim();
                if (legacy.Length > 0) return legacy;
            }
        }

        return null;
    }

    public void WriteSecret(string providerId, string secret)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}; secret=<redacted>");
        if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("A non-empty provider secret is required.", nameof(secret));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Catalog credential storage requires Windows Credential Manager.");

        var normalized = NormalizeProviderId(providerId);
        var value = secret.Trim();
        var byteCount = checked(value.Length * sizeof(char));
        var blob = Marshal.StringToCoTaskMemUni(value);
        try
        {
            var credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = BuildTarget(normalized),
                CredentialBlobSize = checked((uint)byteCount),
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager rejected the catalog credential.");
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    private static string NormalizeProviderId(string providerId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("Provider id is required.", nameof(providerId));
        var normalized = new string(providerId.Trim().ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
        if (normalized.Length == 0) throw new ArgumentException("Provider id contains no safe characters.", nameof(providerId));
        return normalized;
    }

    private static string BuildTarget(string providerId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return $"MHW-Mod-Manager/Catalog/{providerId}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr credential);
}
