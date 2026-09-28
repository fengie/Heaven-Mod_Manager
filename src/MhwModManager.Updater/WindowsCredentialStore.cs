using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using MhwModManager.Core;

namespace MhwModManager.Updater;

public static class WindowsCredentialStore
{
    private const uint CredTypeGeneric = 1;

    public static string? ReadGitHubToken()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var environment = Environment.GetEnvironmentVariable("MHW_MOD_MANAGER_GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(environment)) return environment.Trim();
        if (!OperatingSystem.IsWindows()) return null;
        if (!CredRead(UpdateProtocol.CredentialTarget, CredTypeGeneric, 0, out var credentialPtr))
            return null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return null;
            var token = Marshal.PtrToStringUni(
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize / sizeof(char)));
            return string.IsNullOrWhiteSpace(token) ? null : token.TrimEnd('\0').Trim();
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr credential);
}