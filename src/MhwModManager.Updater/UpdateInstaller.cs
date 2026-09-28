using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.Updater;

public sealed class UpdateInstaller(
    Action<UpdateApplyFaultPoint, string?>? faultInjector = null,
    Action<string>? log = null,
    IAtomicReplaceBackend? applyReplaceBackend = null,
    IAtomicReplaceBackend? metadataReplaceBackend = null,
    IAtomicReplaceBackend? rollbackReplaceBackend = null)
{
    private readonly Action<UpdateApplyFaultPoint, string?> injectFault = faultInjector ?? ((_, _) => { });
    private readonly Action<string> writeLog = log ?? (_ => { });
    private readonly IAtomicReplaceBackend? liveApplyReplaceBackend = applyReplaceBackend;
    private readonly IAtomicReplaceBackend? liveMetadataReplaceBackend = metadataReplaceBackend;
    private readonly IAtomicReplaceBackend? liveRollbackReplaceBackend = rollbackReplaceBackend;

    public async Task ApplyAsync(UpdateApplyRequest request, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        ValidateRequestPaths(request);
        var journal = await ReadJournalAsync(request.JournalPath, ct);
        if (journal is not null && (journal.TargetBuildNumber != request.Manifest.BuildNumber
            || !string.Equals(journal.TargetSourceSha, request.Manifest.SourceSha, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Recovery journal belongs to a different update; preserve its backup for recovery.");
        if (journal is not null && journal.Phase is UpdateJournalPhase.BackupCreated
                or UpdateJournalPhase.Applying
                or UpdateJournalPhase.RollbackRequired)
        {
            writeLog($"update recovery rollback resumed phase={journal.Phase}");
            // Installed metadata can be half-published. Recover from the validated
            // backup before attempting normal installed-version validation.
            using var recovery = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await RollbackAsync(request, recovery.Token);
        }
        var context = await ValidateAndLoadAsync(request, ct);

        injectFault(UpdateApplyFaultPoint.BeforeBackup, null);
        var backupManifest = await CreateBackupAsync(request, context.OldManifest, ct);
        await WriteJournalAsync(request, UpdateJournalPhase.BackupCreated, null, ct);
        injectFault(UpdateApplyFaultPoint.AfterBackup, null);
        var oldByPath = context.OldManifest.Files.ToDictionary(
            x => UpdatePathSafety.NormalizeRelativeFilePath(x.Path),
            StringComparer.OrdinalIgnoreCase);
        var publishedNewPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await WriteJournalAsync(request, UpdateJournalPhase.Applying, null, ct);
            foreach (var entry in context.NewManifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                var relative = UpdatePathSafety.NormalizeRelativeFilePath(entry.Path);
                var previouslyOwned = oldByPath.ContainsKey(relative);
                injectFault(UpdateApplyFaultPoint.BeforeFileApply, entry.Path);
                await ApplyOwnedFileAsync(request, entry, previouslyOwned, ct);
                if (!previouslyOwned) publishedNewPaths.Add(relative);
                injectFault(UpdateApplyFaultPoint.AfterFileApply, entry.Path);
            }

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

            await PublishProductManifestAsync(request, liveMetadataReplaceBackend, ct);
            await PublishInstallMarkerAsync(request, liveMetadataReplaceBackend, ct);
            injectFault(UpdateApplyFaultPoint.BeforeInstalledVerification, null);
            await VerifyInstalledOwnedFilesAsync(request, context.NewManifest, ct);
            await WriteJournalAsync(request, UpdateJournalPhase.AppliedAwaitingHealth, null, ct);
            writeLog($"update apply completed build={request.Manifest.BuildNumber}; awaiting startup health");
        }
        catch (Exception applyError)
        {
            await WriteJournalBestEffortAsync(request, UpdateJournalPhase.RollbackRequired, applyError.Message);
            if (IsAmbiguousNativeReplaceFailure(applyError))
            {
                writeLog(
                    $"update native replacement failed ambiguously error={((Win32Exception)applyError).NativeErrorCode}; " +
                    "preserving backup and replacement evidence for deterministic recovery");
                throw;
            }
            try
            {
                // User cancellation stops forward work, never the recovery it requires.
                using var recovery = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                await RollbackFromBackupAsync(
                    request, context.NewManifest, recovery.Token, publishedNewPaths);
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

    private static bool IsAmbiguousNativeReplaceFailure(Exception error)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"type={error.GetType().Name}");
        return error is Win32Exception { NativeErrorCode: 1176 or 1177 };
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
        ValidateRequestPaths(request);
        var stagedManifest = await UpdatePackageVerifier.VerifyAsync(
            request.StagingRoot, request.Manifest.ProductManifestSha256, ct);
        try
        {
            await RollbackFromBackupAsync(request, stagedManifest, ct);
        }
        catch (Exception rollbackError) when (IsAmbiguousNativeReplaceFailure(rollbackError))
        {
            await WriteJournalBestEffortAsync(
                request,
                UpdateJournalPhase.RollbackRequired,
                $"rollback={rollbackError.Message}");
            writeLog(
                $"update rollback replacement failed ambiguously error={((Win32Exception)rollbackError).NativeErrorCode}; " +
                "preserving backup and replacement evidence for deterministic recovery");
            throw;
        }
    }

    private static void ValidateRequestPaths(UpdateApplyRequest request)
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
    }

    private static async Task<ApplyContext> ValidateAndLoadAsync(UpdateApplyRequest request, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"install={request.InstallRoot}");
        ValidateRequestPaths(request);
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
        EnsureManifestOwnsExecutable(oldManifest, marker.ExecutableRelativePath, "Installed");
        EnsureManifestOwnsPath(oldManifest, UpdateProtocol.BuildIdentityFileName, "Installed");
        var installedIdentity = await UpdateBuildIdentity.LoadRequiredAsync(request.InstallRoot, ct);
        EnsureMarkerMatchesBuildIdentity(marker, installedIdentity, "Installed");

        var newManifest = await UpdatePackageVerifier.VerifyAsync(
            request.StagingRoot, request.Manifest.ProductManifestSha256, ct);
        EnsureManifestOwnsPath(newManifest, UpdateProtocol.BuildIdentityFileName, "Staged");
        var stagedIdentity = await UpdateBuildIdentity.LoadRequiredAsync(request.StagingRoot, ct);
        var stagedMarkerPath = Path.Combine(request.StagingRoot, UpdateProtocol.InstallMarkerFileName);
        var stagedMarker = JsonSerializer.Deserialize<ReleaseInstallMarker>(
                               await File.ReadAllTextAsync(stagedMarkerPath, ct), UpdateProtocol.Json)
                           ?? throw new InvalidDataException("Staged updater install marker is empty.");
        stagedMarker.Validate();
        EnsureMarkerMatchesBuildIdentity(stagedMarker, stagedIdentity, "Staged");
        EnsureTargetIdentityAgreement(request.Manifest, stagedMarker, stagedIdentity);
        if (!string.Equals(
                UpdatePathSafety.NormalizeRelativeFilePath(stagedMarker.ExecutableRelativePath),
                UpdatePathSafety.NormalizeRelativeFilePath(request.Manifest.ExecutableRelativePath),
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(stagedMarker.ProductManifestSha256, request.Manifest.ProductManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Staged install marker does not match the target update manifest.");

        EnsureManifestOwnsExecutable(newManifest, request.Manifest.ExecutableRelativePath, "Staged");

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

    private async Task ApplyOwnedFileAsync(
        UpdateApplyRequest request,
        ProductFileEntry entry,
        bool previouslyOwned,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"path={entry.Path}; previouslyOwned={previouslyOwned}");
        var relative = UpdatePathSafety.NormalizeRelativeFilePath(entry.Path);
        var source = UpdatePathSafety.CombineUnderRoot(request.StagingRoot, relative);
        var destination = UpdatePathSafety.CombineUnderRoot(request.InstallRoot, relative);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(request.InstallRoot,
            Path.GetDirectoryName(destination) ?? request.InstallRoot);
        await AtomicFileOps.ReplaceFromAsync(
            source,
            destination,
            replaceBackend: liveApplyReplaceBackend,
            expectedSha256: entry.Sha256,
            ct: ct,
            requireDestinationAbsent: !previouslyOwned);
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

    private static async Task PublishProductManifestAsync(
        UpdateApplyRequest request,
        IAtomicReplaceBackend? replaceBackend,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var source = Path.Combine(request.StagingRoot, UpdateProtocol.ProductManifestFileName);
        var destination = Path.Combine(request.InstallRoot, UpdateProtocol.ProductManifestFileName);
        await AtomicFileOps.ReplaceFromAsync(
            source,
            destination,
            replaceBackend: replaceBackend,
            expectedSha256: request.Manifest.ProductManifestSha256,
            ct: ct);
    }

    private static async Task PublishInstallMarkerAsync(
        UpdateApplyRequest request,
        IAtomicReplaceBackend? replaceBackend,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var source = Path.Combine(request.StagingRoot, UpdateProtocol.InstallMarkerFileName);
        var destination = Path.Combine(request.InstallRoot, UpdateProtocol.InstallMarkerFileName);
        var expected = await UpdatePackageVerifier.HashFileAsync(source, ct);
        await AtomicFileOps.ReplaceFromAsync(
            source,
            destination,
            replaceBackend: replaceBackend,
            expectedSha256: expected,
            ct: ct);
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
        var installedIdentity = await UpdateBuildIdentity.LoadRequiredAsync(request.InstallRoot, ct);
        EnsureMarkerMatchesBuildIdentity(marker, installedIdentity, "Installed target");
        EnsureTargetIdentityAgreement(request.Manifest, marker, installedIdentity);
        if (!string.Equals(
                UpdatePathSafety.NormalizeRelativeFilePath(marker.ExecutableRelativePath),
                UpdatePathSafety.NormalizeRelativeFilePath(request.Manifest.ExecutableRelativePath),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installed release marker does not describe the target update build.");
    }

    private async Task RollbackFromBackupAsync(
        UpdateApplyRequest request,
        ProductFileManifest newManifest,
        CancellationToken ct,
        HashSet<string>? publishedNewPaths = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"backup={request.BackupRoot}");
        var rollbackPath = Path.Combine(request.BackupRoot, "rollback-files.json");
        if (!File.Exists(rollbackPath))
            throw new InvalidDataException("Rollback manifest is missing.");
        var rollbackManifest = JsonSerializer.Deserialize<ProductFileManifest>(
                                   await File.ReadAllTextAsync(rollbackPath, ct), UpdateProtocol.Json)
                               ?? throw new InvalidDataException("Rollback manifest is empty.");
        rollbackManifest.Validate();

        // Prove the entire recovery set before deleting or restoring any live file.
        foreach (var entry in rollbackManifest.Files)
        {
            var source = UpdatePathSafety.CombineUnderRoot(request.BackupRoot, entry.Path);
            UpdatePathSafety.EnsureExistingComponentsNotReparse(request.BackupRoot, source);
            var destination = UpdatePathSafety.CombineUnderRoot(request.InstallRoot, entry.Path);
            UpdatePathSafety.EnsureExistingComponentsNotReparse(request.InstallRoot, destination);
            if (!File.Exists(source) || new FileInfo(source).Length != entry.Size
                || !string.Equals(await UpdatePackageVerifier.HashFileAsync(source, ct), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Rollback backup is incomplete or modified: {entry.Path}");
        }
        var backupMarkerPath = Path.Combine(request.BackupRoot, UpdateProtocol.InstallMarkerFileName);
        var backupMarker = JsonSerializer.Deserialize<ReleaseInstallMarker>(
            await File.ReadAllTextAsync(backupMarkerPath, ct), UpdateProtocol.Json)
            ?? throw new InvalidDataException("Rollback install marker is empty.");
        backupMarker.Validate();
        var backupProductPath = Path.Combine(request.BackupRoot, UpdateProtocol.ProductManifestFileName);
        if (backupMarker.Build.BuildNumber >= request.Manifest.BuildNumber
            || !string.Equals(await UpdatePackageVerifier.HashFileAsync(backupProductPath, ct),
                backupMarker.ProductManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Rollback metadata does not describe the previous installation.");
        var previousManifest = await LoadProductManifestAsync(backupProductPath, ct);
        EnsureManifestOwnsExecutable(previousManifest, backupMarker.ExecutableRelativePath, "Rollback");
        EnsureManifestOwnsPath(previousManifest, UpdateProtocol.BuildIdentityFileName, "Rollback");
        var previousIdentity = await UpdateBuildIdentity.LoadRequiredAsync(request.BackupRoot, ct);
        EnsureMarkerMatchesBuildIdentity(backupMarker, previousIdentity, "Rollback");
        var previousPaths = previousManifest.Files.Select(x => UpdatePathSafety.NormalizeRelativeFilePath(x.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        previousPaths.Add(UpdateProtocol.ProductManifestFileName);
        previousPaths.Add(UpdateProtocol.InstallMarkerFileName);
        if (rollbackManifest.Files.Any(x => !previousPaths.Contains(UpdatePathSafety.NormalizeRelativeFilePath(x.Path)))
            || !rollbackManifest.Files.Any(x => x.Path == UpdateProtocol.ProductManifestFileName)
            || !rollbackManifest.Files.Any(x => x.Path == UpdateProtocol.InstallMarkerFileName))
            throw new InvalidDataException("Rollback manifest contains files outside previous ownership or omits metadata.");

        var backupPaths = rollbackManifest.Files
            .Select(x => UpdatePathSafety.NormalizeRelativeFilePath(x.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in newManifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            var relative = UpdatePathSafety.NormalizeRelativeFilePath(entry.Path);
            if (backupPaths.Contains(relative)) continue;
            if (publishedNewPaths is not null && !publishedNewPaths.Contains(relative)) continue;
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
            await AtomicFileOps.ReplaceFromAsync(
                source,
                destination,
                replaceBackend: liveRollbackReplaceBackend,
                expectedSha256: entry.Sha256,
                ct: ct);
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

    private static void EnsureManifestOwnsExecutable(
        ProductFileManifest manifest,
        string executableRelativePath,
        string label)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"label={label}; executable={executableRelativePath}");
        EnsureManifestOwnsPath(manifest, executableRelativePath, $"{label} restart executable");
    }

    private static void EnsureManifestOwnsPath(
        ProductFileManifest manifest,
        string relativePath,
        string label)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"label={label}; path={relativePath}");
        var normalized = UpdatePathSafety.NormalizeRelativeFilePath(relativePath);
        if (!manifest.Files.Any(x =>
                string.Equals(
                    UpdatePathSafety.NormalizeRelativeFilePath(x.Path),
                    normalized,
                    StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                $"{label} is not owned by the product manifest: '{relativePath}'.");
    }

    private static void EnsureMarkerMatchesBuildIdentity(
        ReleaseInstallMarker marker,
        UpdateBuildIdentity identity,
        string label)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"label={label}");
        marker.Validate();
        identity.ValidatePublished();
        if (marker.Build.SchemaVersion != identity.SchemaVersion
            || !string.Equals(marker.Build.Channel, identity.Channel, StringComparison.Ordinal)
            || !string.Equals(marker.Build.ProductVersion, identity.ProductVersion, StringComparison.Ordinal)
            || !string.Equals(marker.Build.SourceSha, identity.SourceSha, StringComparison.OrdinalIgnoreCase)
            || marker.Build.BuildNumber != identity.BuildNumber
            || marker.Build.BuiltUtc != identity.BuiltUtc)
            throw new InvalidDataException(
                $"{label} install marker does not match build-identity.json.");
    }

    private static void EnsureTargetIdentityAgreement(
        UpdateManifest manifest,
        ReleaseInstallMarker marker,
        UpdateBuildIdentity identity)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={manifest.BuildNumber}");
        manifest.Validate();
        if (!string.Equals(manifest.Channel, marker.Channel, StringComparison.Ordinal)
            || !string.Equals(manifest.Channel, identity.Channel, StringComparison.Ordinal)
            || !string.Equals(manifest.ProductVersion, identity.ProductVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.SourceSha, identity.SourceSha, StringComparison.OrdinalIgnoreCase)
            || manifest.BuildNumber != identity.BuildNumber)
            throw new InvalidDataException(
                "Update manifest, install marker, and build-identity.json disagree on target build identity.");
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
