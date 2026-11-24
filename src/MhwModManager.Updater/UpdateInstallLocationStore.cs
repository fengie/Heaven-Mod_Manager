using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Updater;

/// <summary>Records a local discovery hint, not an installed-health or release-trust attestation.</summary>
public static class UpdateInstallLocationStore
{
    private const string FileName = "install-location.json";
    private const string ExecutableName = "MHW Mod Manager.exe";
    private const string AssemblyName = "MHW Mod Manager.dll";
    private sealed record Location(int SchemaVersion, string ProductId, string InstallRoot);

    public static Task<bool> RecordCurrentAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return RecordAsync(UpdateClientService.GetInstallRoot(),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ct);
    }

    public static async Task<bool> RecordAsync(string installRoot, string localAppData, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        var install = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!UpdateClientService.CanSelfUpdate(install)) return false;
        RequireLocalDirectory(install);
        var local = Path.GetFullPath(localAppData).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(localAppData)) throw new InvalidDataException("Local application data is unavailable.");
        RequireLocalDirectory(local);
        var updater = Path.Combine(local, "MhwModManager", "Updater");
        if (install.Equals(updater, StringComparison.OrdinalIgnoreCase)
            || install.StartsWith(updater + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Updater work directories cannot become an installation location.");

        // Validate the same fixed release metadata and selected product bytes
        // that the external observer will verify again. Never scan user files.
        var build = await UpdateBuildIdentity.LoadRequiredAsync(install, ct);
        var marker = await ReleaseInstallMarker.LoadAsync(install, ct);
        if (build != marker.Build || !string.Equals(marker.ExecutableRelativePath, ExecutableName, StringComparison.Ordinal)
            || build.SourceSha.Length != 40 || !build.SourceSha.All(Uri.IsHexDigit))
            throw new InvalidDataException("Installed build and release marker disagree.");
        var manifestPath = UpdatePathSafety.CombineUnderRoot(install, UpdateProtocol.ProductManifestFileName);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(install, manifestPath);
        if (!string.Equals(await UpdatePackageVerifier.HashFileAsync(manifestPath, ct), marker.ProductManifestSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installed product manifest hash mismatch.");
        var manifest = JsonSerializer.Deserialize<ProductFileManifest>(await File.ReadAllTextAsync(manifestPath, ct), UpdateProtocol.Json)
            ?? throw new InvalidDataException("Installed product manifest is empty.");
        manifest.Validate();
        foreach (var name in new[] { UpdateProtocol.BuildIdentityFileName, ExecutableName, AssemblyName })
        {
            var entry = manifest.Files.SingleOrDefault(x => string.Equals(x.Path, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("Required installed product file is not in the manifest.");
            var path = UpdatePathSafety.CombineUnderRoot(install, name);
            UpdatePathSafety.EnsureExistingComponentsNotReparse(install, path);
            if (!File.Exists(path) || new FileInfo(path).Length != entry.Size
                || !string.Equals(await UpdatePackageVerifier.HashFileAsync(path, ct), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Required installed product file does not match the manifest.");
        }

        ct.ThrowIfCancellationRequested();
        UpdatePathSafety.CreateDirectorySafely(local, updater);
        var locator = UpdatePathSafety.CombineUnderRoot(updater, FileName);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(local, locator);
        var value = new Location(1, UpdateProtocol.ProductId, install);
        if (File.Exists(locator))
        {
            try
            {
                var previous = JsonSerializer.Deserialize<Location>(await File.ReadAllTextAsync(locator, ct), UpdateProtocol.Json);
                if (previous == value) return true;
            }
            catch (JsonException)
            {
                // The fixed product-owned location file is replaceable after
                // validating the new packaged installation.
            }
        }

        ct.ThrowIfCancellationRequested();
        UpdatePathSafety.EnsureExistingComponentsNotReparse(local, locator);
        await UpdatePackageStager.WriteJsonAtomicallyAsync(locator, value, ct);
        return true;
    }

    private static void RequireLocalDirectory(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !Directory.Exists(path))
            throw new InvalidDataException("Install location requires an existing local directory.");
        if (OperatingSystem.IsWindows())
        {
            var drive = Path.GetPathRoot(path);
            if (drive is null || drive.Length != 3 || drive[1] != ':' || path[2..].Contains(':')
                || new DriveInfo(drive).DriveType is not (DriveType.Fixed or DriveType.Removable))
                throw new InvalidDataException("Install location requires a fixed or removable local drive.");
        }
        for (DirectoryInfo? cursor = new(path); cursor is not null; cursor = cursor.Parent)
        {
            if ((cursor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Install location cannot traverse reparse points.");
        }
    }
}
