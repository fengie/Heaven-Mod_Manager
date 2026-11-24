using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Updater;

/// <summary>
/// Records a local discovery hint for the last successfully started packaged
/// installation. It is never a release attestation or a runtime-health claim.
/// </summary>
public static class UpdateInstallLocationReceipt
{
    private const string Exe = "MHW Mod Manager.exe";
    private const string Dll = "MHW Mod Manager.dll";
    private const string Locator = "install-location.json";

    public static async Task<bool> TryRecordAsync(
        string installRoot, string localAppDataRoot,
        UpdateBuildIdentity runningBuild, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        try
        {
            if (string.IsNullOrWhiteSpace(installRoot) || string.IsNullOrWhiteSpace(localAppDataRoot)
                || !Path.IsPathFullyQualified(installRoot) || !Path.IsPathFullyQualified(localAppDataRoot))
                return false;
            var install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
            var local = Path.TrimEndingDirectorySeparator(Path.GetFullPath(localAppDataRoot));
            if (!string.Equals(install, installRoot, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(local, localAppDataRoot, StringComparison.OrdinalIgnoreCase)
                || !RegularDirectoryChain(install) || !RegularDirectoryChain(local)
                || !UpdateClientService.CanSelfUpdate(install))
                return false;

            var updater = Path.Combine(local, "MhwModManager", "Updater");
            var relative = Path.GetRelativePath(updater, install);
            // Never publish a location inside updater-owned staging or backup storage.
            if (!Path.IsPathRooted(relative) && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return false;

            var names = new[]
            {
                Exe, Dll, UpdateProtocol.BuildIdentityFileName,
                UpdateProtocol.ProductManifestFileName, UpdateProtocol.InstallMarkerFileName
            };
            foreach (var name in names)
            {
                var file = UpdatePathSafety.CombineUnderRoot(install, name);
                UpdatePathSafety.EnsureExistingComponentsNotReparse(install, file);
                var info = new FileInfo(file);
                var limit = name == UpdateProtocol.ProductManifestFileName ? 4L * 1024 * 1024
                    : name == Exe || name == Dll ? 64L * 1024 * 1024 : 64L * 1024;
                if (!info.Exists || info.Length <= 0 || info.Length > limit
                    || (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    return false;
            }

            var installed = await UpdateBuildIdentity.LoadRequiredAsync(install, ct);
            var marker = await ReleaseInstallMarker.LoadAsync(install, ct);
            if (installed != runningBuild || marker.Build != installed
                || marker.ExecutableRelativePath != Exe || installed.SourceSha.Length != 40
                || installed.SourceSha.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')))
                return false;

            var manifestPath = Path.Combine(install, UpdateProtocol.ProductManifestFileName);
            var manifestHash = await UpdatePackageVerifier.HashFileAsync(manifestPath, ct);
            if (!string.Equals(manifestHash, marker.ProductManifestSha256, StringComparison.OrdinalIgnoreCase))
                return false;
            var manifest = JsonSerializer.Deserialize<ProductFileManifest>(
                await File.ReadAllTextAsync(manifestPath, ct), UpdateProtocol.Json);
            if (manifest is null || manifest.Files.Count is < 3 or > 20000) return false;
            manifest.Validate();
            foreach (var name in new[] { Exe, Dll, UpdateProtocol.BuildIdentityFileName })
            {
                var entry = manifest.Files.SingleOrDefault(x =>
                    string.Equals(x.Path, name, StringComparison.OrdinalIgnoreCase));
                var file = Path.Combine(install, name);
                if (entry is null || new FileInfo(file).Length != entry.Size
                    || !string.Equals(await UpdatePackageVerifier.HashFileAsync(file, ct),
                        entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            ct.ThrowIfCancellationRequested();
            UpdatePathSafety.CreateDirectorySafely(local, updater);
            var locator = Path.Combine(updater, Locator);
            UpdatePathSafety.EnsureExistingComponentsNotReparse(local, locator);
            var value = new
            {
                SchemaVersion = 1,
                ProductId = UpdateProtocol.ProductId,
                InstallRoot = install
            };

            // Compare only the bounded canonical receipt bytes. Do not parse an
            // arbitrary previous file or rewrite an already valid identical hint.
            var expected = JsonSerializer.SerializeToUtf8Bytes(value, UpdateProtocol.Json);
            if (File.Exists(locator))
            {
                await using var current = new FileStream(locator, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (current.Length == expected.Length)
                {
                    var previous = new byte[expected.Length];
                    await current.ReadExactlyAsync(previous, ct);
                    ct.ThrowIfCancellationRequested();
                    if (current.Length == expected.Length && previous.SequenceEqual(expected))
                        return true;
                }
            }

            ct.ThrowIfCancellationRequested();
            UpdatePathSafety.EnsureExistingComponentsNotReparse(local, locator);
            await UpdatePackageStager.WriteJsonAtomicallyAsync(locator, value, ct);
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
            || ex is InvalidDataException || ex is JsonException || ex is ArgumentException
            || ex is NotSupportedException)
        {
            return false;
        }
    }

    private static bool RegularDirectoryChain(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // Match the external observer: only canonical directories on local
        // fixed or removable volumes, never UNC, device aliases or ADS paths.
        if (path.Length > 1024 || Path.GetDirectoryName(path) is null)
            return false;
        if (OperatingSystem.IsWindows())
        {
            var drive = Path.GetPathRoot(path);
            if (drive is null || drive.Length != 3 || !char.IsAsciiLetter(drive[0])
                || drive[1] != ':' || drive[2] != '\\' || path[2..].Contains(':')
                || new DriveInfo(drive).DriveType is not (DriveType.Fixed or DriveType.Removable))
                return false;
        }
        for (DirectoryInfo? current = new(path); current is not null; current = current.Parent)
        {
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;
        }
        return true;
    }
}
