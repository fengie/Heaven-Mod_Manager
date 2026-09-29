using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UpdaterInstalledClientE2ETests
{
    private const string OldTag = "updater-main-60";
    private const long OldBuild = 60;
    private const string OldSource = "ffd218b6ad4e9f4fea4b143d266712a6fa17a285";
    private const long TargetBuild = 61;
    private const string TargetSource = "5abe40304dfcb48f96e750bd7da3d0075315625b";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "UpdaterInstalledClientE2E")]
    public async Task Published_build_60_to_61_and_fault_rollback_are_safe()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MHW_RUN_UPDATER_INSTALLED_E2E"),
                "1",
                StringComparison.Ordinal))
            return;

        Assert.True(OperatingSystem.IsWindows(), "Installed-client updater E2E requires Windows.");

        var token = Environment.GetEnvironmentVariable("MHW_MOD_MANAGER_GITHUB_TOKEN");
        Assert.False(string.IsNullOrWhiteSpace(token),
            "Hosted updater E2E requires MHW_MOD_MANAGER_GITHUB_TOKEN.");

        var root = Path.Combine(Path.GetTempPath(), "mhw-updater-installed-e2e-" + Guid.NewGuid().ToString("N"));
        var downloads = Path.Combine(root, "downloads");
        var successInstall = Path.Combine(root, "success-install");
        var rollbackInstall = Path.Combine(root, "rollback-install");
        var managerHome = Path.Combine(root, "manager-home");
        var fakeGameRoot = Path.Combine(root, "fake-game");
        var evidencePath = Environment.GetEnvironmentVariable("MHW_E2E_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(evidencePath))
            evidencePath = Path.Combine(root, "evidence.json");

        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(successInstall);
        Directory.CreateDirectory(rollbackInstall);
        Directory.CreateDirectory(managerHome);
        Directory.CreateDirectory(fakeGameRoot);

        var previousManagerHome = Environment.GetEnvironmentVariable("MOD_MANAGER_HOME");
        var log = new List<string>();
        var evidence = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["oldTag"] = OldTag,
            ["oldBuild"] = OldBuild,
            ["oldSource"] = OldSource,
            ["targetBuild"] = TargetBuild,
            ["targetSource"] = TargetSource,
            ["startedUtc"] = DateTimeOffset.UtcNow
        };

        try
        {
            PrepareFakeGame(managerHome, fakeGameRoot);
            Environment.SetEnvironmentVariable("MOD_MANAGER_HOME", managerHome);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var oldRelease = await DownloadExactReleaseAsync(
                http, token!, OldTag, OldBuild, OldSource, downloads, TestToken);

            ExtractPackage(oldRelease.ArchivePath, successInstall);
            var successOldManifest = await VerifyPublishedInstallAsync(
                successInstall, oldRelease.Manifest, OldBuild, OldSource, TestToken);
            SeedSentinels(successInstall);
            var successSentinelsBefore = await SnapshotSentinelsAsync(successInstall, TestToken);

            using var client = new UpdateClientService(
                log: message =>
                {
                    log.Add(message);
                    Console.WriteLine("[updater-e2e] " + message);
                });

            var oldIdentity = await UpdateBuildIdentity.LoadRequiredAsync(successInstall, TestToken);
            var staged = await client.CheckAndStageAsync(oldIdentity, TestToken);
            Assert.NotNull(staged);
            Assert.Equal(TargetBuild, staged!.Manifest.BuildNumber);
            Assert.Equal(TargetSource, staged.Manifest.SourceSha, ignoreCase: true);

            var stagedIdentity = await UpdateBuildIdentity.LoadRequiredAsync(staged.StagingRoot, TestToken);
            Assert.Equal(TargetBuild, stagedIdentity.BuildNumber);
            Assert.Equal(TargetSource, stagedIdentity.SourceSha, ignoreCase: true);
            var targetManifest = await UpdatePackageVerifier.VerifyAsync(
                staged.StagingRoot, staged.Manifest.ProductManifestSha256, TestToken);

            using var simulatedOldProcess = StartShortLivedProcess();
            var handoff = await client.PrepareHandoffAsync(
                staged,
                successInstall,
                [],
                simulatedOldProcess.Id,
                TestToken);

            using var helper = client.LaunchHelper(handoff);
            using (var helperTimeout = CancellationTokenSource.CreateLinkedTokenSource(TestToken))
            {
                helperTimeout.CancelAfter(TimeSpan.FromMinutes(3));
                await helper.WaitForExitAsync(helperTimeout.Token);
            }

            Assert.Equal(0, helper.ExitCode);

            var installedIdentity = await UpdateBuildIdentity.LoadRequiredAsync(successInstall, TestToken);
            Assert.Equal(TargetBuild, installedIdentity.BuildNumber);
            Assert.Equal(TargetSource, installedIdentity.SourceSha, ignoreCase: true);

            var health = JsonSerializer.Deserialize<UpdateStartupHealth>(
                await File.ReadAllTextAsync(handoff.Request.HealthFile, TestToken),
                UpdateProtocol.Json);
            Assert.NotNull(health);
            Assert.Equal(TargetBuild, health!.BuildNumber);
            Assert.Equal(TargetSource, health.SourceSha, ignoreCase: true);
            Assert.Equal(handoff.Request.HealthToken, health.Token);

            var successJournal = await ReadJournalAsync(handoff.Request.JournalPath, TestToken);
            Assert.Equal(UpdateJournalPhase.Confirmed, successJournal.Phase);

            var successSentinelsAfter = await SnapshotSentinelsAsync(successInstall, TestToken);
            AssertSnapshotsEqual(successSentinelsBefore, successSentinelsAfter);

            evidence["scenarioA"] = new
            {
                status = "PASS",
                installRoot = successInstall,
                staged.Manifest.BuildNumber,
                staged.Manifest.SourceSha,
                helperExitCode = helper.ExitCode,
                health.ProcessId,
                health.AttemptId,
                health.BuildNumber,
                health.SourceSha,
                journalPhase = successJournal.Phase.ToString(),
                sentinelSha256 = successSentinelsAfter
            };

            StopTrackedProcessBestEffort(health.ProcessId);

            ExtractPackage(oldRelease.ArchivePath, rollbackInstall);
            var rollbackOldManifest = await VerifyPublishedInstallAsync(
                rollbackInstall, oldRelease.Manifest, OldBuild, OldSource, TestToken);
            SeedSentinels(rollbackInstall);
            var rollbackSentinelsBefore = await SnapshotSentinelsAsync(rollbackInstall, TestToken);
            var oldOwnedBefore = await SnapshotOwnedAsync(
                rollbackInstall, rollbackOldManifest, TestToken);

            var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
            UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);
            var rollbackRoot = Path.Combine(
                updaterRoot, "e2e-rollback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rollbackRoot);
            var rollbackRequest = new UpdateApplyRequest(
                staged.Manifest,
                rollbackInstall,
                staged.StagingRoot,
                Path.Combine(rollbackRoot, "backup"),
                Path.Combine(rollbackRoot, "journal.json"),
                Path.Combine(rollbackRoot, "pending.json"),
                Path.Combine(rollbackRoot, "health.json"),
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                Environment.ProcessId,
                []);

            var injected = false;
            var rollbackInstaller = new UpdateInstaller(
                (point, _) =>
                {
                    if (point != UpdateApplyFaultPoint.AfterFileApply || injected) return;
                    injected = true;
                    throw new InvalidOperationException("installed-e2e-injected-after-first-file");
                },
                message =>
                {
                    log.Add(message);
                    Console.WriteLine("[updater-e2e-rollback] " + message);
                });

            var injectedError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => rollbackInstaller.ApplyAsync(rollbackRequest, TestToken));
            Assert.Equal("installed-e2e-injected-after-first-file", injectedError.Message);
            Assert.True(injected);

            var rollbackJournal = await ReadJournalAsync(rollbackRequest.JournalPath, TestToken);
            Assert.Equal(UpdateJournalPhase.RolledBack, rollbackJournal.Phase);

            var oldOwnedAfter = await SnapshotOwnedAsync(
                rollbackInstall, rollbackOldManifest, TestToken);
            AssertSnapshotsEqual(oldOwnedBefore, oldOwnedAfter);

            var rollbackSentinelsAfter = await SnapshotSentinelsAsync(rollbackInstall, TestToken);
            AssertSnapshotsEqual(rollbackSentinelsBefore, rollbackSentinelsAfter);

            var oldPaths = rollbackOldManifest.Files
                .Select(x => Normalize(x.Path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newOnlyPaths = targetManifest.Files
                .Select(x => Normalize(x.Path))
                .Where(x => !oldPaths.Contains(x))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var relative in newOnlyPaths)
                Assert.False(
                    File.Exists(Path.Combine(rollbackInstall, relative.Replace('/', Path.DirectorySeparatorChar))),
                    $"Rollback left target-only product file '{relative}'.");

            var restoredIdentity = await UpdateBuildIdentity.LoadRequiredAsync(rollbackInstall, TestToken);
            Assert.Equal(OldBuild, restoredIdentity.BuildNumber);
            Assert.Equal(OldSource, restoredIdentity.SourceSha, ignoreCase: true);

            evidence["scenarioB"] = new
            {
                status = "PASS",
                installRoot = rollbackInstall,
                journalPhase = rollbackJournal.Phase.ToString(),
                restoredBuild = restoredIdentity.BuildNumber,
                restoredSource = restoredIdentity.SourceSha,
                oldOwnedFileCount = oldOwnedAfter.Count,
                targetOnlyFileCount = newOnlyPaths.Length,
                targetOnlyPaths = newOnlyPaths,
                sentinelSha256 = rollbackSentinelsAfter
            };
            evidence["productLog"] = log;
            evidence["completedUtc"] = DateTimeOffset.UtcNow;

            await WriteEvidenceAsync(evidencePath!, evidence, TestToken);
        }
        catch (Exception ex)
        {
            evidence["status"] = "FAIL";
            evidence["failureType"] = ex.GetType().FullName;
            evidence["failureMessage"] = ex.Message;
            evidence["productLog"] = log;
            evidence["completedUtc"] = DateTimeOffset.UtcNow;
            try { await WriteEvidenceAsync(evidencePath!, evidence, CancellationToken.None); } catch { }
            throw;
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOD_MANAGER_HOME", previousManagerHome);
            StopProcessesFromInstallRootBestEffort(successInstall);
            StopProcessesFromInstallRootBestEffort(rollbackInstall);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static Process StartShortLivedProcess()
    {
        return Process.Start(new ProcessStartInfo(
                   "powershell.exe",
                   "-NoProfile -NonInteractive -Command Start-Sleep -Milliseconds 1200")
               {
                   UseShellExecute = false,
                   CreateNoWindow = true
               })
               ?? throw new InvalidOperationException("Could not start simulated previous-client process.");
    }

    private static void PrepareFakeGame(string managerHome, string fakeGameRoot)
    {
        Directory.CreateDirectory(fakeGameRoot);
        Directory.CreateDirectory(Path.Combine(fakeGameRoot, "Mods"));
        var sourceExe = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
        var fakeExe = Path.Combine(fakeGameRoot, "FakeGame.exe");
        File.Copy(sourceExe, fakeExe, overwrite: true);

        var stateRoot = Path.Combine(managerHome, "State");
        var registry = new GameProfileRegistry(stateRoot);
        var profile = GameProfile.Generic(
            "updater-e2e-game",
            "Updater E2E Fake Game",
            fakeGameRoot,
            "FakeGame.exe",
            "Mods");
        registry.Upsert(profile);
        registry.SetActive(profile.Id);
    }

    private static void SeedSentinels(string installRoot)
    {
        Directory.CreateDirectory(Path.Combine(installRoot, "Mods"));
        Directory.CreateDirectory(Path.Combine(installRoot, "State"));
        File.WriteAllText(Path.Combine(installRoot, "Mods", "e2e-user.mod"), "E2E-USER-MOD");
        File.WriteAllText(Path.Combine(installRoot, "State", "e2e-state.json"), "{\"e2e\":true}");
        File.WriteAllText(Path.Combine(installRoot, "e2e-unknown-user-file.txt"), "E2E-UNKNOWN");
    }

    private static async Task<Dictionary<string, string>> SnapshotSentinelsAsync(
        string installRoot,
        CancellationToken ct)
    {
        var paths = new[]
        {
            "Mods/e2e-user.mod",
            "State/e2e-state.json",
            "e2e-unknown-user-file.txt"
        };
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in paths)
        {
            var full = Path.Combine(
                installRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), $"Sentinel is missing: {relative}");
            result[relative] = await UpdatePackageVerifier.HashFileAsync(full, ct);
        }
        return result;
    }

    private static async Task<Dictionary<string, string>> SnapshotOwnedAsync(
        string installRoot,
        ProductFileManifest manifest,
        CancellationToken ct)
    {
        var paths = manifest.Files.Select(x => Normalize(x.Path))
            .Append(UpdateProtocol.ProductManifestFileName)
            .Append(UpdateProtocol.InstallMarkerFileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in paths)
        {
            var full = Path.Combine(
                installRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), $"Owned file is missing: {relative}");
            result[relative] = await UpdatePackageVerifier.HashFileAsync(full, ct);
        }
        return result;
    }

    private static void AssertSnapshotsEqual(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected)
        {
            Assert.True(actual.TryGetValue(pair.Key, out var value),
                $"Snapshot path disappeared: {pair.Key}");
            Assert.Equal(pair.Value, value, ignoreCase: true);
        }
    }

    private static async Task<ProductFileManifest> VerifyPublishedInstallAsync(
        string installRoot,
        UpdateManifest releaseManifest,
        long expectedBuild,
        string expectedSource,
        CancellationToken ct)
    {
        var product = await UpdatePackageVerifier.VerifyAsync(
            installRoot, releaseManifest.ProductManifestSha256, ct);
        var identity = await UpdateBuildIdentity.LoadRequiredAsync(installRoot, ct);
        Assert.Equal(expectedBuild, identity.BuildNumber);
        Assert.Equal(expectedSource, identity.SourceSha, ignoreCase: true);
        var marker = await ReleaseInstallMarker.LoadAsync(installRoot, ct);
        Assert.Equal(expectedBuild, marker.Build.BuildNumber);
        Assert.Equal(expectedSource, marker.Build.SourceSha, ignoreCase: true);
        return product;
    }

    private static void ExtractPackage(string archivePath, string destination)
    {
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
        ZipFile.ExtractToDirectory(archivePath, destination);
    }

    private static async Task<DownloadedRelease> DownloadExactReleaseAsync(
        HttpClient http,
        string token,
        string tag,
        long expectedBuild,
        string expectedSource,
        string downloadRoot,
        CancellationToken ct)
    {
        using var releaseRequest = CreateGitHubRequest(
            HttpMethod.Get,
            $"https://api.github.com/repos/{UpdateProtocol.Repository}/releases/tags/{tag}",
            token,
            "application/vnd.github+json");
        using var releaseResponse = await http.SendAsync(
            releaseRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        releaseResponse.EnsureSuccessStatusCode();
        using var releaseDoc = JsonDocument.Parse(
            await releaseResponse.Content.ReadAsStringAsync(ct));
        var root = releaseDoc.RootElement;
        Assert.False(root.GetProperty("draft").GetBoolean());
        Assert.False(root.GetProperty("prerelease").GetBoolean());
        if (root.TryGetProperty("immutable", out var immutable))
            Assert.True(immutable.GetBoolean(), $"Release {tag} is not immutable.");

        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(x => new ReleaseAsset(
                x.GetProperty("name").GetString() ?? string.Empty,
                x.GetProperty("url").GetString() ?? string.Empty,
                x.GetProperty("size").GetInt64(),
                x.TryGetProperty("digest", out var digest) ? digest.GetString() : null))
            .ToArray();

        var manifestAsset = Assert.Single(
            assets.Where(x => string.Equals(
                x.Name, "update-manifest.json", StringComparison.OrdinalIgnoreCase)));
        var manifestBytes = await DownloadAssetBytesAsync(
            http, token, manifestAsset.ApiUrl, 64 * 1024, ct);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(
                           manifestBytes, UpdateProtocol.Json)
                       ?? throw new InvalidDataException($"Release {tag} manifest is empty.");
        manifest.Validate();
        Assert.Equal(expectedBuild, manifest.BuildNumber);
        Assert.Equal(expectedSource, manifest.SourceSha, ignoreCase: true);

        var packageAsset = Assert.Single(
            assets.Where(x => string.Equals(
                x.Name, manifest.ArtifactName, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(manifest.ArtifactSize, packageAsset.Size);
        if (!string.IsNullOrWhiteSpace(packageAsset.Digest))
            Assert.Equal(
                "sha256:" + manifest.Sha256.ToLowerInvariant(),
                packageAsset.Digest!.ToLowerInvariant());

        var archivePath = Path.Combine(downloadRoot, $"{tag}-{manifest.ArtifactName}");
        await DownloadAssetToFileAsync(
            http, token, packageAsset.ApiUrl, archivePath, manifest.ArtifactSize, ct);
        var archiveHash = await UpdatePackageVerifier.HashFileAsync(archivePath, ct);
        Assert.Equal(manifest.Sha256, archiveHash, ignoreCase: true);

        return new DownloadedRelease(manifest, archivePath);
    }

    private static HttpRequestMessage CreateGitHubRequest(
        HttpMethod method,
        string uri,
        string token,
        string accept)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.UserAgent.ParseAdd("MHW-Manual-Mod-Manager-Updater-E2E/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static async Task<byte[]> DownloadAssetBytesAsync(
        HttpClient http,
        string token,
        string apiUrl,
        int maxBytes,
        CancellationToken ct)
    {
        using var request = CreateGitHubRequest(
            HttpMethod.Get, apiUrl, token, "application/octet-stream");
        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0) break;
            Assert.True(memory.Length + read <= maxBytes, "Release metadata exceeded size budget.");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return memory.ToArray();
    }

    private static async Task DownloadAssetToFileAsync(
        HttpClient http,
        string token,
        string apiUrl,
        string destination,
        long expectedBytes,
        CancellationToken ct)
    {
        using var request = CreateGitHubRequest(
            HttpMethod.Get, apiUrl, token, "application/octet-stream");
        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
        await input.CopyToAsync(output, 128 * 1024, ct);
        await output.FlushAsync(ct);
        Assert.Equal(expectedBytes, output.Length);
    }

    private static async Task<UpdateJournal> ReadJournalAsync(
        string path,
        CancellationToken ct)
    {
        Assert.True(File.Exists(path), $"Updater journal is missing: {path}");
        return JsonSerializer.Deserialize<UpdateJournal>(
                   await File.ReadAllTextAsync(path, ct),
                   UpdateProtocol.Json)
               ?? throw new InvalidDataException("Updater journal is empty.");
    }

    private static async Task WriteEvidenceAsync(
        string path,
        IReadOnlyDictionary<string, object?> evidence,
        CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path))
                     ?? throw new InvalidOperationException("Evidence path has no parent.");
        Directory.CreateDirectory(parent);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions(UpdateProtocol.Json)
            {
                WriteIndented = true
            }),
            ct);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    private static void StopTrackedProcessBestEffort(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(15000);
            }
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
        }
    }

    private static void StopProcessesFromInstallRootBestEffort(string installRoot)
    {
        var fullRoot = Path.GetFullPath(installRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (!Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                        continue;
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(10000);
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException
                        or System.ComponentModel.Win32Exception
                        or NotSupportedException)
                {
                }
            }
        }
    }

    private sealed record DownloadedRelease(UpdateManifest Manifest, string ArchivePath);
    private sealed record ReleaseAsset(string Name, string ApiUrl, long Size, string? Digest);
}
