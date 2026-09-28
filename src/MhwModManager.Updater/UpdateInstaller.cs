using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.Updater;

public sealed class UpdateInstaller(
    Action<UpdateApplyFaultPoint, string?>? faultInjector = null,
    Action<string>? log = null)
{
    private readonly Action<UpdateApplyFaultPoint, string?> injectFault = faultInjector ?? ((_, _) => { });
    private readonly Action<string> writeLog = log ?? (_ => { });

    public async Task ApplyAsync(UpdateApplyRequest request, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        var context = await ValidateAndLoadAsync(request, ct);
        var journal = await ReadJournalAsync(request.JournalPath, ct);
        if (journal is not null && journal.TargetBuildNumber == request.Manifest.BuildNumber
            && journal.Phase is UpdateJournalPhase.BackupCreated
                or UpdateJournalPhase.Applying
                or UpdateJournalPhase.RollbackRequired)
        {
            writeLog($"update recovery rollback resumed phase={journal.Phase}");
            await RollbackFromBackupAsync(request, context.NewManifest, ct);
        }

        injectFault(UpdateApplyFaultPoint.BeforeBackup, null);
        var backupManifest = await CreateBackupAsync(request, context.OldManifest, ct);
        await WriteJournalAsync(request, UpdateJournalPhase.BackupCreated, null, ct);
        injectFault(UpdateApplyFaultPoint.AfterBackup, null);
        try
        {
            await WriteJournalAsync(request, UpdateJournalPhase.Applying, null, ct);
            foreach (var entry in context.NewManifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                injectFault(UpdateApplyFaultPoint.BeforeFileApply, entry.Path);
                await ApplyOwnedFileAsync(request, entry, ct);
                injectFault(UpdateApplyFaultPoint.AfterFileApply, entry.Path);
            }

            var oldByPath = context.OldManifest.Files.ToDictionary(
                x => UpdatePathSafety.NormalizeRelativeFilePath(x.Path),
                StringComparer.OrdinalIgnoreCase);
            var newPaths = context.NewManifest.Files
                .Select(x => UpdatePathSafety.NormalizeRelativeFilePath(x.Path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in oldByPath)
            {
                if (newPaths.Contains(pair.Key)) continue;
                injectFault(UpdateApplyFaultPoint.BeforeStaleOwnedRemoval, pair.Key);
                await RemoveStaleOwnedFileAsync(request.InstallRoot, pair.Value, ct);
                injectFault(UpdateApplyFaultPoint.AfterStaleOwnedRemoval, pair.Key);
            }

            await PublishProductManifestAsync(request, ct);
            await PublishInstallMarkerAsync(request, ct);
            injectFault(UpdateApplyFaultPoint.BeforeInstalledVerification, null);
            await VerifyInstalledOwnedFilesAsync(request, context.NewManifest, ct);
            await WriteJournalAsync(request, UpdateJournalPhase.AppliedAwaitingHealth, null, ct);
            writeLog($"update apply completed build={request.Manifest.BuildNumber}; awaiting startup health");
        }
        catch (Exception applyError)
        {
            await WriteJournalBestEffortAsync(request, UpdateJournalPhase.RollbackRequired, applyError.Message);
            try
            {
                // User cancellation stops forward work, never the recovery it requires.
                using var recovery = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                await RollbackFromBackupAsync(request, context.NewManifest, recovery.Token);
            }
            catch (Exception rollbackError)
            {
                await WriteJournalBestEffortAsync(request, UpdateJournalPhase.RollbackRequired,
                    $"apply={applyError.Message}; rollback={rollbackError.Message}");
                throw new AggregateException("Update apply failed and rollback also failed.", applyError, rollbackError);
            }
            throw;
        }
    }

    public async Task ConfirmAsync(UpdateApplyRequest request, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        await VerifyInstalledOwnedFilesAsync(
            request,
            await LoadProductManifestAsync(
                Path.Combine(request.InstallRoot, UpdateProtocol.ProductManifestFileName), ct),
            ct);
        await WriteJournalAsync(request, UpdateJournalPhase.Confirmed, null, ct);
        try { if (File.Exists(request.PendingPath)) File.Delete(request.PendingPath); } catch { }
        try { if (Directory.Exists(request.BackupRoot)) Directory.Delete(request.BackupRoot, true); } catch (Exception ex) { writeLog($"update backup cleanup deferred: {ex.Message}"); }
    }

    public async Task RollbackAsync(UpdateApplyRequest request, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        var stagedManifest = await LoadProductManifestAsync(request.StagingRoot, ct, isRoot: true);
        await RollbackFromBackupAsync(request, stagedManifest, ct);
    }

    private static async Task<ApplyContext> ValidateAndLoadAsync(UpdateApplyRequest request, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"install={request.InstallRoot}");
        request.Manifest.Validate();
        if (!Directory.Exists(request.InstallRoot))
            throw new DirectoryNotFoundException($"Install root is missing: {request.InstallRoot}");
        if ((File.GetAttributes(request.InstallRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Install root may not be a reparse point: {request.InstallRoot}");
        if (UpdatePathSafety.IsDevelopmentLayout(request.InstallRoot))
            throw new InvalidOperationException("Self-update is disabled for a repository/development release layout.");

        var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
        UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, request.StagingRoot);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, request.BackupRoot);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, request.JournalPath);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, request.PendingPath);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, request.HealthFile);

        var markerPath = Path.Combine(request.InstallRoot, UpdateProtocol.InstallMarkerFileName);
        if (!File.Exists(markerPath))
            throw new InvalidOperationException("This installation has no updater install marker; destructive self-apply is disabled.");
        var marker = JsonSerializer.Deserialize<ReleaseInstallMarker>(
                         await File.ReadAllTextAsync(markerPath, ct), UpdateProtocol.Json)
                     ?? throw new InvalidDataException("Installed updater marker is empty.");
        marker.Validate();
        if (request.Manifest.BuildNumber <= marker.Build.BuildNumber)
            throw new InvalidOperationException(
                $"Refusing update build {request.Manifest.BuildNumber}; installed build is {marker.Build.BuildNumber}.");

        var oldProductPath = Path.Combine(request.InstallRoot, UpdateProtocol.ProductManifestFileName);
        var oldProductHash = await UpdatePackageVerifier.HashFileAsync(oldProductPath, ct);
        if (!string.Equals(oldProductHash, marker.ProductManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installed product manifest no longer matches the packaged install marker.");
        var oldManifest = await LoadProductManifestAsync(oldProductPath, ct);

        var newManifest = await UpdatePackageVerifier.VerifyAsync(
            request.StagingRoot, request.Manifest.ProductManifestSha256, ct);
        var stagedMarkerPath = Path.Combine(request.StagingRoot, UpdateProtocol.InstallMarkerFileName);
        var stagedMarker = JsonSerializer.Deserialize<ReleaseInstallMarker>(
                               await File.ReadAllTextAsync(stagedMarkerPath, ct), UpdateProtocol.Json)
                           ?? throw new InvalidDataException("Staged updater install marker is empty.");
        stagedMarker.Validate();
        if (stagedMarker.Build.BuildNumber != request.Manifest.BuildNumber
            || !string.Equals(stagedMarker.Build.SourceSha, request.Manifest.SourceSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(stagedMarker.ProductManifestSha256, request.Manifest.ProductManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Staged install marker does not match the target update manifest.");

        var exe = UpdatePathSafety.NormalizeRelativeFilePath(request.Manifest.ExecutableRelativePath);
        if (!newManifest.Files.Any(x =>
                string.Equals(UpdatePathSafety.NormalizeRelativeFilePath(x.Path), exe, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Staged product manifest does not own the declared restart executable.");

        var ownedPaths = oldManifest.Files.Select(x => UpdatePathSafety.NormalizeRelativeFilePath(x.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in newManifest.Files)
        {
            var relative = UpdatePathSafety.NormalizeRelativeFilePath(entry.Path);
            var destination = UpdatePathSafety.CombineUnderRoot(request.InstallRoot, relative);
            UpdatePathSafety.EnsureExistingComponentsNotReparse(request.InstallRoot, destination);
            if (Directory.Exists(destination) || (!ownedPaths.Contains(relative) && File.Exists(destination)))
                throw new IOException($"Update would overwrite an unowned path: {entry.Path}");
        }

        return new ApplyContext(marker, oldManifest, newManifest);
    }

    private async Task<ProductFileManifest> CreateBackupAsync(
        UpdateApplyRequest request,
        ProductFileManifest oldManifest,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"backup={request.BackupRoot}");
        if (Directory.Exists(request.BackupRoot))
            Directory.Delete(request.BackupRoot, true);
        var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
        UpdatePathSafety.CreateDirectorySafely(updaterRoot, request.BackupRoot);

        var paths = oldManifest.Files
            .Select(x => UpdatePathSafety.NormalizeRelativeFilePath(x.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        paths.Add(UpdateProtocol.ProductManifestFileName);
        paths.Add(UpdateProtocol.InstallMarkerFileName);
        var backedUp = new List<ProductFileEntry>();
        foreach (var relative in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var source = UpdatePathSafety.CombineUnderRoot(request.InstallRoot, relative);
            if (!File.Exists(source)) continue;
            UpdatePathSafety.EnsureExistingComponentsNotReparse(request.InstallRoot, source);
            var destination = UpdatePathSafety.CombineUnderRoot(request.BackupRoot, relative);
            var parent = Path.GetDirectoryName(destination)
                         ?? throw new InvalidOperationException($"Backup path has no parent: {relative}");
            UpdatePathSafety.CreateDirectorySafely(request.BackupRoot, parent);
            await CopyFileDurablyAsync(source, destination, ct);
            var info = new FileInfo(destination);
            backedUp.Add(new(relative, info.Length, await UpdatePackageVerifier.HashFileAsync(destination, ct)));
        }

        var manifest = new ProductFileManifest(UpdateProtocol.ProductManifestSchemaVersion, backedUp);
        var manifestPath = Path.Combine(request.BackupRoot, "rollback-files.json");
        await UpdatePackageStager.WriteJsonAtomicallyAsync(manifestPath, manifest, ct);
        writeLog($"update backup created files={backedUp.Count}");
        return manifest;
    }

    private static async Task ApplyOwnedFileAsync(UpdateApplyRequest request, ProductFileEntry entry, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={entry.Path}");
        var relative = UpdatePathSafety.NormalizeRelativeFilePath(entry.Path);
        var source = UpdatePathSafety.CombineUnderRoot(request.StagingRoot, relative);
        var destination = UpdatePathSafety.CombineUnderRoot(request.InstallRoot, relative);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(request.InstallRoot,
            Path.GetDirectoryName(destination) ?? request.InstallRoot);
        await AtomicFileOps.ReplaceFromAsync(source, destination, expectedSha256: entry.Sha256, ct: ct);
    }

    private async Task RemoveStaleOwnedFileAsync(
        string installRoot,
        ProductFileEntry oldEntry,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={oldEntry.Path}");
        var destination = UpdatePathSafety.CombineUnderRoot(installRoot, oldEntry.Path);
        if (!File.Exists(destination)) return;
        UpdatePathSafety.EnsureExistingComponentsNotReparse(installRoot, destination);
        var actual = await UpdatePackageVerifier.HashFileAsync(destination, ct);
        if (!string.Equals(actual, oldEntry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            writeLog($"update preserved modified stale owned file path={oldEntry.Path}");
            return;
        }
        File.Delete(destination);
    }

    private static async Task PublishProductManifestAsync(UpdateApplyRequest request, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var source = Path.Combine(request.StagingRoot, UpdateProtocol.ProductManifestFileName);
        var destination = Path.Combine(request.InstallRoot, UpdateProtocol.ProductManifestFileName);
        await AtomicFileOps.ReplaceFromAsync(
            source, destination, expectedSha256: request.Manifest.ProductManifestSha256, ct: ct);
    }

    private static async Task PublishInstallMarkerAsync(UpdateApplyRequest request, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var source = Path.Combine(request.StagingRoot, UpdateProtocol.InstallMarkerFileName);
        var destination = Path.Combine(request.InstallRoot, UpdateProtocol.InstallMarkerFileName);
        var expected = await UpdatePackageVerifier.HashFileAsync(source, ct);
        await AtomicFileOps.ReplaceFromAsync(source, destination, expectedSha256: expected, ct: ct);
    }

    private static async Task VerifyInstalledOwnedFilesAsync(
        UpdateApplyRequest request,
        ProductFileManifest manifest,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"files={manifest.Files.Count}");
        manifest.Validate();
        foreach (var entry in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            var path = UpdatePathSafety.CombineUnderRoot(request.InstallRoot, entry.Path);
            UpdatePathSafety.EnsureExistingComponentsNotReparse(request.InstallRoot, path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != entry.Size)
                throw new InvalidDataException($"Installed product file size mismatch: {entry.Path}");
            var actual = await UpdatePackageVerifier.HashFileAsync(path, ct);
            if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Installed product file SHA-256 mismatch: {entry.Path}");
        }

        var productPath = Path.Combine(request.InstallRoot, UpdateProtocol.ProductManifestFileName);
        var productHash = await UpdatePackageVerifier.HashFileAsync(productPath, ct);
        if (!string.Equals(productHash, request.Manifest.ProductManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installed product manifest SHA-256 does not match the update manifest.");
        var markerPath = Path.Combine(request.InstallRoot, UpdateProtocol.InstallMarkerFileName);
        var marker = JsonSerializer.Deserialize<ReleaseInstallMarker>(
                         await File.ReadAllTextAsync(markerPath, ct), UpdateProtocol.Json)
                     ?? throw new InvalidDataException("Installed release marker is empty after apply.");
        marker.Validate();
        if (marker.Build.BuildNumber != request.Manifest.BuildNumber
            || !string.Equals(marker.Build.SourceSha, request.Manifest.SourceSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installed release marker does not describe the target update build.");
    }

    private async Task RollbackFromBackupAsync(
        UpdateApplyRequest request,
        ProductFileManifest newManifest,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"backup={request.BackupRoot}");
        var rollbackPath = Path.Combine(request.BackupRoot, "rollback-files.json");
        if (!File.Exists(rollbackPath))
            throw new InvalidDataException("Rollback manifest is missing.");
        var rollbackManifest = JsonSerializer.Deserialize<ProductFileManifest>(
                                   await File.ReadAllTextAsync(rollbackPath, ct), UpdateProtocol.Json)
                               ?? throw new InvalidDataException("Rollback manifest is empty.");
        rollbackManifest.Validate();

        var backupPaths = rollbackManifest.Files
            .Select(x => UpdatePathSafety.NormalizeRelativeFilePath(x.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in newManifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            var relative = UpdatePathSafety.NormalizeRelativeFilePath(entry.Path);
            if (backupPaths.Contains(relative)) continue;
            var destination = UpdatePathSafety.CombineUnderRoot(request.InstallRoot, relative);
            if (!File.Exists(destination)) continue;
            UpdatePathSafety.EnsureExistingComponentsNotReparse(request.InstallRoot, destination);
            var actual = await UpdatePackageVerifier.HashFileAsync(destination, ct);

            if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Rollback refused to delete unexpectedly modified new file: {entry.Path}");
            File.Delete(destination);
        }

        foreach (var entry in rollbackManifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            var source = UpdatePathSafety.CombineUnderRoot(request.BackupRoot, entry.Path);
            if (!File.Exists(source))
                throw new InvalidDataException($"Rollback backup file is missing: {entry.Path}");
            var actualBackup = await UpdatePackageVerifier.HashFileAsync(source, ct);
            if (!string.Equals(actualBackup, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Rollback backup file was modified: {entry.Path}");
            var destination = UpdatePathSafety.CombineUnderRoot(request.InstallRoot, entry.Path);
            UpdatePathSafety.EnsureExistingComponentsNotReparse(
                request.InstallRoot, Path.GetDirectoryName(destination) ?? request.InstallRoot);
            await AtomicFileOps.ReplaceFromAsync(source, destination, expectedSha256: entry.Sha256, ct: ct);
        }

        await VerifyRollbackBytesAsync(request.InstallRoot, rollbackManifest, ct);
        await WriteJournalAsync(request, UpdateJournalPhase.RolledBack, null, ct);
        writeLog($"update rollback restored files={rollbackManifest.Files.Count}");
    }

    private static async Task VerifyRollbackBytesAsync(
        string installRoot,
        ProductFileManifest rollbackManifest,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"files={rollbackManifest.Files.Count}");
        foreach (var entry in rollbackManifest.Files)
        {
            var destination = UpdatePathSafety.CombineUnderRoot(installRoot, entry.Path);
            if (!File.Exists(destination))
                throw new IOException($"Rollback verification is missing file: {entry.Path}");
            var actual = await UpdatePackageVerifier.HashFileAsync(destination, ct);
            if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Rollback verification failed for: {entry.Path}");
        }
    }

    private static async Task CopyFileDurablyAsync(string source, string destination, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"source={source}; destination={destination}");
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, 128 * 1024, ct);
        await output.FlushAsync(ct);
        output.Flush(true);
    }

    private static async Task<ProductFileManifest> LoadProductManifestAsync(
        string pathOrRoot,
        CancellationToken ct,
        bool isRoot = false)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={pathOrRoot}");
        var path = isRoot ? Path.Combine(pathOrRoot, UpdateProtocol.ProductManifestFileName) : pathOrRoot;
        if (!File.Exists(path)) throw new InvalidDataException($"Product manifest is missing: {path}");
        var value = JsonSerializer.Deserialize<ProductFileManifest>(
                        await File.ReadAllTextAsync(path, ct), UpdateProtocol.Json)
                    ?? throw new InvalidDataException("Product manifest is empty.");
        value.Validate();
        return value;
    }

    private static async Task<UpdateJournal?> ReadJournalAsync(string path, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}");
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<UpdateJournal>(
            await File.ReadAllTextAsync(path, ct), UpdateProtocol.Json);
    }

    private static Task WriteJournalAsync(
        UpdateApplyRequest request,
        UpdateJournalPhase phase,
        string? detail,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"phase={phase}");
        return UpdatePackageStager.WriteJsonAtomicallyAsync(
            request.JournalPath,
            new UpdateJournal(phase, request.Manifest.BuildNumber, request.Manifest.SourceSha, DateTimeOffset.UtcNow, detail),
            ct);
    }

    private async Task WriteJournalBestEffortAsync(
        UpdateApplyRequest request,
        UpdateJournalPhase phase,
        string? detail)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"phase={phase}");
        try
        {
            await WriteJournalAsync(request, phase, detail, CancellationToken.None);
        }
        catch (Exception ex)
        {
            writeLog($"update journal best-effort write failed phase={phase}: {ex.Message}");
        }
    }

    private sealed record ApplyContext(
        ReleaseInstallMarker Marker,
        ProductFileManifest OldManifest,
        ProductFileManifest NewManifest);
}
