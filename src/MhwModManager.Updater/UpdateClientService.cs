using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Updater;

public sealed record PreparedUpdateHandoff(
    UpdateApplyRequest Request,
    string RequestPath,
    string HelperExecutablePath);

public sealed class UpdateClientService : IDisposable
{
    private readonly HttpClient http;
    private readonly bool ownsHttpClient;
    private readonly Action<string> writeLog;
    private readonly GitHubUpdateSource source;
    private readonly UpdatePackageStager stager;
    private bool disposed;

    public UpdateClientService(HttpClient? httpClient = null, Action<string>? log = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        http = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        ownsHttpClient = httpClient is null;
        writeLog = log ?? (_ => { });
        source = new GitHubUpdateSource(http, writeLog);
        stager = new UpdatePackageStager(source, writeLog);
    }

    public static string GetInstallRoot()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Path.GetFullPath(
            AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
    }

    public static bool CanSelfUpdate(string installRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"install={installRoot}");
        if (UpdatePathSafety.IsDevelopmentLayout(installRoot)) return false;
        return File.Exists(Path.Combine(installRoot, UpdateProtocol.InstallMarkerFileName))
            && File.Exists(Path.Combine(installRoot, UpdateProtocol.BuildIdentityFileName))
            && File.Exists(Path.Combine(installRoot, UpdateProtocol.ProductManifestFileName));
    }

    public async Task<StagedUpdate?> CheckAndStageAsync(
        UpdateBuildIdentity current,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={current.BuildNumber}");
        ObjectDisposedException.ThrowIf(disposed, this);

        var pending = await TryLoadPendingAsync(current, ct);
        if (pending is not null)
        {
            writeLog($"update reused verified staged build={pending.Manifest.BuildNumber}");
            return pending;
        }

        UpdateCandidate? candidate = null;
        string? downloadToken = null;
        HttpRequestException? publicFeedFailure = null;
        try
        {
            candidate = await source.FindLatestAsync(
                current,
                token: null,
                ct: ct,
                repository: UpdateProtocol.PublicReleaseRepository);
            if (candidate is not null)
                writeLog($"update public feed selected build={candidate.Manifest.BuildNumber}");
        }
        catch (HttpRequestException ex)
        {
            publicFeedFailure = ex;
            writeLog(
                $"public update feed unavailable ({ex.StatusCode?.ToString() ?? ex.GetType().Name}); trying private fallback if configured");
        }

        if (candidate is null)
        {
            var token = WindowsCredentialStore.ReadGitHubToken();
            if (!string.IsNullOrWhiteSpace(token))
            {
                candidate = await source.FindLatestAsync(
                    current,
                    token,
                    ct,
                    UpdateProtocol.Repository);
                downloadToken = token;
                if (candidate is not null)
                    writeLog($"update private fallback selected build={candidate.Manifest.BuildNumber}");
            }
            else if (publicFeedFailure is not null)
            {
                throw new HttpRequestException(
                    $"Public updater feed '{UpdateProtocol.PublicReleaseRepository}' is unavailable and no private fallback credential is configured.",
                    publicFeedFailure,
                    publicFeedFailure.StatusCode);
            }
        }

        if (candidate is null) return null;
        return await stager.StageAsync(candidate, downloadToken, ct);
    }

    public async Task<PreparedUpdateHandoff> PrepareHandoffAsync(
        StagedUpdate staged,
        string installRoot,
        IReadOnlyList<string> currentArguments,
        int currentProcessId,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"build={staged.Manifest.BuildNumber}; install={installRoot}");
        ObjectDisposedException.ThrowIf(disposed, this);
        staged.Manifest.Validate();

        var fullInstallRoot = Path.GetFullPath(installRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!CanSelfUpdate(fullInstallRoot))
            throw new InvalidOperationException(
                "This application layout is not an updater-managed packaged installation.");

        await UpdatePackageVerifier.VerifyAsync(
            staged.StagingRoot,
            staged.Manifest.ProductManifestSha256,
            ct);

        var installedMarker = await ReleaseInstallMarker.LoadAsync(fullInstallRoot, ct);
        var installedProductPath = Path.Combine(
            fullInstallRoot,
            UpdateProtocol.ProductManifestFileName);
        var installedProductHash = await UpdatePackageVerifier.HashFileAsync(
            installedProductPath,
            ct);
        if (!string.Equals(
                installedProductHash,
                installedMarker.ProductManifestSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Installed product manifest does not match release-install.json before updater helper handoff.");

        var installedManifest = JsonSerializer.Deserialize<ProductFileManifest>(
                                    await File.ReadAllTextAsync(installedProductPath, ct),
                                    UpdateProtocol.Json)
                                ?? throw new InvalidDataException(
                                    "Installed product manifest is empty.");
        installedManifest.Validate();

        var helperRelative = UpdatePathSafety.NormalizeRelativeFilePath(
            UpdateProtocol.HelperRelativePath);
        var helperPrefix =
            UpdateProtocol.HelperDirectoryRelativePath.TrimEnd('/') + "/";
        var helperFiles = installedManifest.Files
            .Select(entry => (
                Entry: entry,
                Relative: UpdatePathSafety.NormalizeRelativeFilePath(entry.Path)))
            .Where(x => x.Relative.StartsWith(
                helperPrefix,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Relative, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!helperFiles.Any(x => string.Equals(
                x.Relative,
                helperRelative,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                $"Installed product manifest does not own updater helper '{helperRelative}'.");

        foreach (var helperFile in helperFiles)
        {
            var source = UpdatePathSafety.CombineUnderRoot(
                fullInstallRoot,
                helperFile.Relative);
            UpdatePathSafety.EnsureExistingComponentsNotReparse(
                fullInstallRoot,
                source);
            var info = new FileInfo(source);
            if (!info.Exists || info.Length != helperFile.Entry.Size)
                throw new InvalidDataException(
                    $"Installed updater helper file size does not match product ownership metadata: {helperFile.Relative}");
            var sourceHash = await UpdatePackageVerifier.HashFileAsync(
                source,
                ct);
            if (!string.Equals(
                    sourceHash,
                    helperFile.Entry.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Installed updater helper file hash does not match product ownership metadata: {helperFile.Relative}");
        }

        var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
        UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);
        var transactionId =
            $"{staged.Manifest.BuildNumber}-{Guid.NewGuid():N}";
        var transactionRoot = Path.Combine(
            updaterRoot,
            "transactions",
            transactionId);
        UpdatePathSafety.CreateDirectorySafely(
            updaterRoot,
            transactionRoot);

        var helperExecutionRoot = Path.Combine(
            transactionRoot,
            "helper");
        UpdatePathSafety.CreateDirectorySafely(
            transactionRoot,
            helperExecutionRoot);
        string? helperDestination = null;
        foreach (var helperFile in helperFiles)
        {
            var source = UpdatePathSafety.CombineUnderRoot(
                fullInstallRoot,
                helperFile.Relative);
            var relativeWithinHelper =
                helperFile.Relative[helperPrefix.Length..];
            var destination = UpdatePathSafety.CombineUnderRoot(
                helperExecutionRoot,
                relativeWithinHelper);
            var destinationParent = Path.GetDirectoryName(destination)
                ?? throw new InvalidOperationException(
                    $"Updater helper destination has no parent: {relativeWithinHelper}");
            UpdatePathSafety.CreateDirectorySafely(
                helperExecutionRoot,
                destinationParent);
            await CopyVerifiedAsync(
                source,
                destination,
                helperFile.Entry.Size,
                helperFile.Entry.Sha256,
                ct);
            if (string.Equals(
                    helperFile.Relative,
                    helperRelative,
                    StringComparison.OrdinalIgnoreCase))
                helperDestination = destination;
        }
        if (helperDestination is null)
            throw new InvalidDataException(
                $"Updater helper '{helperRelative}' was not copied.");

        var backupRoot = Path.Combine(
            transactionRoot,
            "backup");
        var journalPath = Path.Combine(
            transactionRoot,
            "journal.json");
        var healthFile = Path.Combine(
            transactionRoot,
            "health.json");
        var requestPath = Path.Combine(
            transactionRoot,
            "apply-request.json");
        var pendingPath = Path.Combine(
            updaterRoot,
            UpdateProtocol.PendingFileName);
        var healthToken = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));

        var request = new UpdateApplyRequest(
            staged.Manifest,
            fullInstallRoot,
            staged.StagingRoot,
            backupRoot,
            journalPath,
            pendingPath,
            healthFile,
            healthToken,
            currentProcessId,
            UpdateArgumentSanitizer.RemoveHealthArguments(
                currentArguments));

        await UpdateRequestStore.WriteAsync(
            requestPath,
            request,
            ct);
        writeLog(
            $"update handoff prepared build={staged.Manifest.BuildNumber} request={requestPath}");
        return new PreparedUpdateHandoff(
            request,
            requestPath,
            helperDestination);
    }

    public Process LaunchHelper(PreparedUpdateHandoff handoff)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"request={handoff.RequestPath}");
        ObjectDisposedException.ThrowIf(disposed, this);
        var helper = Path.GetFullPath(
            handoff.HelperExecutablePath);
        if (!File.Exists(helper))
            throw new FileNotFoundException(
                "Prepared updater helper is missing.",
                helper);
        var start = new ProcessStartInfo(helper)
        {
            UseShellExecute = false,
            WorkingDirectory =
                Path.GetDirectoryName(helper)
                ?? UpdatePackageStager.GetUpdaterRoot()
        };
        start.ArgumentList.Add("--request");
        start.ArgumentList.Add(
            Path.GetFullPath(handoff.RequestPath));
        return ProcessDebug.Start(
            start,
            "handoff MHW Manual Mod Manager self-update");
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (disposed) return;
        disposed = true;
        if (ownsHttpClient) http.Dispose();
        GC.SuppressFinalize(this);
    }

    private static async Task CopyVerifiedAsync(
        string sourcePath,
        string destinationPath,
        long expectedSize,
        string expectedSha256,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"source={sourcePath}; destination={destinationPath}");
        await using (var input = new FileStream(
                         sourcePath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         128 * 1024,
                         FileOptions.Asynchronous
                         | FileOptions.SequentialScan))
        await using (var output = new FileStream(
                         destinationPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         128 * 1024,
                         FileOptions.Asynchronous
                         | FileOptions.SequentialScan))
        {
            await input.CopyToAsync(
                output,
                128 * 1024,
                ct);
            await output.FlushAsync(ct);
            output.Flush(true);
        }

        var info = new FileInfo(destinationPath);
        if (info.Length != expectedSize)
            throw new InvalidDataException(
                "Copied updater helper size does not match the verified source.");
        var actual = await UpdatePackageVerifier.HashFileAsync(
            destinationPath,
            ct);
        if (!string.Equals(
                actual,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Copied updater helper hash does not match the verified source.");
    }

    private static async Task<StagedUpdate?> TryLoadPendingAsync(
        UpdateBuildIdentity current,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"current={current.BuildNumber}");
        var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
        UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);
        var pendingPath = Path.Combine(
            updaterRoot,
            UpdateProtocol.PendingFileName);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(
            updaterRoot,
            pendingPath);
        if (!File.Exists(pendingPath)) return null;

        var pending = JsonSerializer.Deserialize<StagedUpdate>(
                          await File.ReadAllTextAsync(
                              pendingPath,
                              ct),
                          UpdateProtocol.Json)
                      ?? throw new InvalidDataException(
                          "Pending update state is empty.");
        pending.Manifest.Validate();
        if (pending.Manifest.BuildNumber <= current.BuildNumber)
        {
            File.Delete(pendingPath);
            return null;
        }

        var stagingRoot = Path.GetFullPath(
            pending.StagingRoot);
        var relative = Path.GetRelativePath(
            updaterRoot,
            stagingRoot);
        if (relative.StartsWith(
                "..",
                StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw new InvalidDataException(
                "Pending update staging root is outside the updater root.");
        UpdatePathSafety.EnsureExistingComponentsNotReparse(
            updaterRoot,
            stagingRoot);
        await UpdatePackageVerifier.VerifyAsync(
            stagingRoot,
            pending.Manifest.ProductManifestSha256,
            ct);
        return pending;
    }
}

public static class UpdateArgumentSanitizer
{
    public static IReadOnlyList<string> RemoveHealthArguments(
        IReadOnlyList<string> args)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"count={args.Count}");
        var output = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            if (IsHealthArgument(args[i]))
            {
                if (i + 1 >= args.Count || IsHealthArgument(args[i + 1]))
                    throw new InvalidDataException(
                        $"Updater health argument '{args[i]}' is missing a value.");
                i++;
                continue;
            }
            output.Add(args[i]);
        }
        return output;
    }

    private static bool IsHealthArgument(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        return string.Equals(
                   value,
                   UpdateHealthProtocol.TokenArgument,
                   StringComparison.Ordinal)
               || string.Equals(
                   value,
                   UpdateHealthProtocol.FileArgument,
                   StringComparison.Ordinal)
               || string.Equals(
                   value,
                   UpdateHealthProtocol.AttemptArgument,
                   StringComparison.Ordinal);
    }
}
