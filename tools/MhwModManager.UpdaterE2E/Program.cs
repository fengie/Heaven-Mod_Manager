using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MhwModManager.Updater;

internal static class Program
{
    private static readonly List<string> Evidence = [];
    private static readonly CancellationTokenSource Timeout = new(TimeSpan.FromMinutes(18));

    private static async Task<int> Main(string[] args)
    {
        var options = ParseArgs(args);
        var oldZip = Require(options, "old-zip");
        var evidencePath = Require(options, "evidence");
        var expectedOldBuild = long.Parse(Require(options, "old-build"));
        var expectedTargetBuild = long.Parse(Require(options, "target-build"));
        var expectedTargetSha = Require(options, "target-sha");
        var expectedOldZipSha = Require(options, "old-zip-sha");
        var token = Environment.GetEnvironmentVariable("MHW_E2E_GITHUB_TOKEN")
                    ?? throw new InvalidOperationException("MHW_E2E_GITHUB_TOKEN is required.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(evidencePath))!);
        var root = Path.Combine(Path.GetTempPath(), "mhwmm-installed-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Record($"root={root}");
        Record($"updater_root={UpdatePackageStager.GetUpdaterRoot()}");
        Record($"old_zip={oldZip}");
        Record($"expected_old_build={expectedOldBuild}");
        Record($"expected_target_build={expectedTargetBuild}");
        Record($"expected_target_sha={expectedTargetSha}");

        try
        {
            var oldZipHash = await HashAsync(oldZip);
            Check(oldZipHash.Equals(expectedOldZipSha, StringComparison.OrdinalIgnoreCase),
                $"old ZIP hash mismatch expected={expectedOldZipSha} actual={oldZipHash}");
            Record($"old_zip_sha256={oldZipHash}");

            var scenarioA = Path.Combine(root, "scenario-a");
            var installA = Path.Combine(scenarioA, "install");
            var oldA = await ExtractAndValidateOldInstallAsync(oldZip, installA, expectedOldBuild);
            var sentinelsA = await SeedSentinelsAsync(installA);
            RecordSentinels("scenario_a.before", sentinelsA);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var source = new GitHubUpdateSource(http, m => Record("source: " + m));
            var candidate = await source.FindLatestAsync(oldA.Identity, token, Timeout.Token)
                            ?? throw new InvalidOperationException("No update candidate was discovered from the live private GitHub release feed.");
            Check(candidate.Manifest.BuildNumber == expectedTargetBuild,
                $"live discovery selected build {candidate.Manifest.BuildNumber}, expected {expectedTargetBuild}");
            Check(candidate.Manifest.SourceSha.Equals(expectedTargetSha, StringComparison.OrdinalIgnoreCase),
                $"live discovery selected source {candidate.Manifest.SourceSha}, expected {expectedTargetSha}");
            Record($"live_candidate_build={candidate.Manifest.BuildNumber}");
            Record($"live_candidate_source={candidate.Manifest.SourceSha}");
            Record($"live_candidate_artifact={candidate.Manifest.ArtifactName}");
            Record($"live_candidate_artifact_sha256={candidate.Manifest.Sha256}");

            var stager = new UpdatePackageStager(source, m => Record("stager: " + m));
            var staged = await stager.StageAsync(candidate, token, Timeout.Token);
            var stagedIdentity = await UpdateBuildIdentity.LoadRequiredAsync(staged.StagingRoot, Timeout.Token);
            Check(stagedIdentity.BuildNumber == expectedTargetBuild, "staged build number does not match target");
            Check(stagedIdentity.SourceSha.Equals(expectedTargetSha, StringComparison.OrdinalIgnoreCase),
                "staged source SHA does not match target");
            var downloadedArchive = Path.Combine(
                Directory.GetParent(staged.StagingRoot)!.FullName,
                candidate.Manifest.ArtifactName);
            var downloadedSha = await HashAsync(downloadedArchive);
            Check(downloadedSha.Equals(candidate.Manifest.Sha256, StringComparison.OrdinalIgnoreCase),
                "production stager download hash does not match update manifest");
            Record($"staged_root={staged.StagingRoot}");
            Record($"production_download_sha256={downloadedSha}");

            using var client = new UpdateClientService(http, m => Record("client: " + m));
            await RunScenarioAAsync(
                client, staged, installA, oldA, sentinelsA, scenarioA,
                expectedTargetBuild, expectedTargetSha);
            await RunScenarioBAsync(
                client, staged, oldZip, expectedOldBuild, root,
                expectedTargetBuild, expectedTargetSha);

            Record("OVERALL=PASS");
            await FlushEvidenceAsync(evidencePath);
            return 0;
        }
        catch (Exception ex)
        {
            Record("OVERALL=FAIL");
            Record($"failure_type={ex.GetType().FullName}");
            Record($"failure_message={ex.Message}");
            Record(ex.ToString());
            await FlushEvidenceAsync(evidencePath);
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("MHW_E2E_GITHUB_TOKEN", null);
        }
    }

    private static async Task RunScenarioAAsync(
        UpdateClientService client,
        StagedUpdate staged,
        string installRoot,
        InstallSnapshot oldSnapshot,
        IReadOnlyDictionary<string, string> sentinels,
        string scenarioRoot,
        long expectedTargetBuild,
        string expectedTargetSha)
    {
        Record("SCENARIO_A=START");
        var gatePid = await CreateExitedGateProcessAsync();
        var handoff = await client.PrepareHandoffAsync(
            staged, installRoot, [], gatePid, Timeout.Token);
        Record($"scenario_a.request={handoff.RequestPath}");
        Record($"scenario_a.helper={handoff.HelperExecutablePath}");

        ConfigureDisposableAppEnvironment(scenarioRoot);
        using var helper = client.LaunchHelper(handoff);
        Record($"scenario_a.helper_pid={helper.Id}");

        using var helperTimeout = CancellationTokenSource.CreateLinkedTokenSource(Timeout.Token);
        helperTimeout.CancelAfter(TimeSpan.FromMinutes(6));
        await helper.WaitForExitAsync(helperTimeout.Token);
        Record($"scenario_a.helper_exit={helper.ExitCode}");
        Check(helper.ExitCode == 0,
            $"real packaged updater helper failed/recovered instead of confirming target; exit={helper.ExitCode}");

        var healthPath = handoff.Request.HealthFile;
        Check(File.Exists(healthPath), $"health acknowledgement file missing: {healthPath}");
        var health = JsonSerializer.Deserialize<UpdateStartupHealth>(
                         await File.ReadAllTextAsync(healthPath, Timeout.Token), UpdateProtocol.Json)
                     ?? throw new InvalidDataException("health acknowledgement JSON is empty");
        Check(health.BuildNumber == expectedTargetBuild,
            $"health acknowledgement build {health.BuildNumber} != {expectedTargetBuild}");
        Check(health.SourceSha.Equals(expectedTargetSha, StringComparison.OrdinalIgnoreCase),
            $"health acknowledgement source {health.SourceSha} != {expectedTargetSha}");
        Check(health.Token == handoff.Request.HealthToken, "health token mismatch");
        Record($"scenario_a.health_pid={health.ProcessId}");
        Record($"scenario_a.health_build={health.BuildNumber}");
        Record($"scenario_a.health_source={health.SourceSha}");
        Record($"scenario_a.health_attempt={health.AttemptId}");

        var targetIdentity = await UpdateBuildIdentity.LoadRequiredAsync(installRoot, Timeout.Token);
        Check(targetIdentity.BuildNumber == expectedTargetBuild,
            $"installed build after update {targetIdentity.BuildNumber} != {expectedTargetBuild}");
        Check(targetIdentity.SourceSha.Equals(expectedTargetSha, StringComparison.OrdinalIgnoreCase),
            "installed source after update does not match target");
        Record($"scenario_a.installed_identity={targetIdentity.DisplayId}");

        var journal = await ReadJournalAsync(handoff.Request.JournalPath);
        Check(journal.Phase == UpdateJournalPhase.Confirmed,
            $"scenario A journal phase {journal.Phase} != Confirmed");
        Check(!Directory.Exists(handoff.Request.BackupRoot),
            "scenario A confirmed update retained backup unexpectedly");
        Record($"scenario_a.journal_phase={journal.Phase}");
        Record($"scenario_a.backup_exists={Directory.Exists(handoff.Request.BackupRoot)}");

        await AssertSentinelsUnchangedAsync("scenario_a.after", sentinels);
        await AssertOldToNewOwnedChangeAsync(oldSnapshot, installRoot);

        await StopExactProcessAsync(health.ProcessId, installRoot, staged.Manifest.ExecutableRelativePath);
        Record("SCENARIO_A=PASS");
    }

    private static async Task RunScenarioBAsync(
        UpdateClientService client,
        StagedUpdate staged,
        string oldZip,
        long expectedOldBuild,
        string root,
        long expectedTargetBuild,
        string expectedTargetSha)
    {
        Record("SCENARIO_B=START");
        var scenarioRoot = Path.Combine(root, "scenario-b");
        var installRoot = Path.Combine(scenarioRoot, "install");
        var oldSnapshot = await ExtractAndValidateOldInstallAsync(oldZip, installRoot, expectedOldBuild);
        var sentinels = await SeedSentinelsAsync(installRoot);
        RecordSentinels("scenario_b.before", sentinels);

        var gatePid = await CreateExitedGateProcessAsync();
        var handoff = await client.PrepareHandoffAsync(
            staged, installRoot, [], gatePid, Timeout.Token);

        var injected = false;
        var installer = new UpdateInstaller(
            faultInjector: (point, path) =>
            {
                if (!injected && point == UpdateApplyFaultPoint.BeforeInstalledVerification)
                {
                    injected = true;
                    throw new InjectedE2EException(
                        $"deterministic E2E fault at {point} path={path ?? "<none>"}");
                }
            },
            log: m => Record("rollback-installer: " + m));

        try
        {
            await installer.ApplyAsync(handoff.Request, Timeout.Token);
            throw new InvalidOperationException(
                "Scenario B unexpectedly completed apply without triggering deterministic fault.");
        }
        catch (InjectedE2EException ex)
        {
            Record($"scenario_b.injected_fault={ex.Message}");
        }

        Check(injected, "Scenario B deterministic fault seam was not reached.");
        var journal = await ReadJournalAsync(handoff.Request.JournalPath);
        Check(journal.Phase == UpdateJournalPhase.RolledBack,
            $"scenario B journal phase {journal.Phase} != RolledBack");
        Record($"scenario_b.journal_phase={journal.Phase}");

        var restoredIdentity = await UpdateBuildIdentity.LoadRequiredAsync(installRoot, Timeout.Token);
        Check(restoredIdentity.BuildNumber == expectedOldBuild,
            $"rollback restored build {restoredIdentity.BuildNumber}, expected {expectedOldBuild}");
        Check(!restoredIdentity.SourceSha.Equals(expectedTargetSha, StringComparison.OrdinalIgnoreCase),
            "rollback left target source identity installed");
        Record($"scenario_b.restored_identity={restoredIdentity.DisplayId}");

        await AssertOwnedSnapshotRestoredAsync(oldSnapshot, installRoot);
        await AssertSentinelsUnchangedAsync("scenario_b.after", sentinels);

        var newManifest = await LoadProductManifestAsync(staged.StagingRoot);
        var oldOwned = oldSnapshot.Manifest.Files
            .Select(x => Normalize(x.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newOnly = newManifest.Files
            .Select(x => Normalize(x.Path))
            .Where(x => !oldOwned.Contains(x))
            .ToArray();
        foreach (var relative in newOnly)
        {
            Check(!File.Exists(Path.Combine(installRoot, ToNative(relative))),
                $"rollback left new-only product file present: {relative}");
        }
        Record($"scenario_b.new_only_files_checked={newOnly.Length}");
        Record("SCENARIO_B=PASS");
    }

    private static async Task<InstallSnapshot> ExtractAndValidateOldInstallAsync(
        string zipPath, string installRoot, long expectedBuild)
    {
        Directory.CreateDirectory(installRoot);
        ZipFile.ExtractToDirectory(zipPath, installRoot);
        var identity = await UpdateBuildIdentity.LoadRequiredAsync(installRoot, Timeout.Token);
        var marker = await ReleaseInstallMarker.LoadAsync(installRoot, Timeout.Token);
        Check(identity.BuildNumber == expectedBuild,
            $"old package identity build {identity.BuildNumber} != expected {expectedBuild}");
        Check(marker.Build.BuildNumber == expectedBuild,
            $"old package marker build {marker.Build.BuildNumber} != expected {expectedBuild}");
        var manifest = await UpdatePackageVerifier.VerifyAsync(
            installRoot, marker.ProductManifestSha256, Timeout.Token);
        var hashes = await CaptureOwnedHashesAsync(installRoot, manifest);
        hashes[UpdateProtocol.ProductManifestFileName] =
            await HashAsync(Path.Combine(installRoot, UpdateProtocol.ProductManifestFileName));
        hashes[UpdateProtocol.InstallMarkerFileName] =
            await HashAsync(Path.Combine(installRoot, UpdateProtocol.InstallMarkerFileName));
        Record($"validated_old_install={installRoot}");
        Record($"validated_old_identity={identity.DisplayId}");
        return new InstallSnapshot(identity, marker, manifest, hashes);
    }

    private static async Task<IReadOnlyDictionary<string, string>> SeedSentinelsAsync(string installRoot)
    {
        var paths = new[]
        {
            Path.Combine(installRoot, "Mods", "e2e-user.mod"),
            Path.Combine(installRoot, "State", "e2e-user-state.json"),
            Path.Combine(installRoot, "e2e-unknown-user-file.bin")
        };
        Directory.CreateDirectory(Path.GetDirectoryName(paths[0])!);
        Directory.CreateDirectory(Path.GetDirectoryName(paths[1])!);
        await File.WriteAllTextAsync(paths[0], "E2E MOD SENTINEL " + Guid.NewGuid(), Timeout.Token);
        await File.WriteAllTextAsync(paths[1], "E2E STATE SENTINEL " + Guid.NewGuid(), Timeout.Token);
        await File.WriteAllBytesAsync(paths[2], RandomNumberGenerator.GetBytes(97), Timeout.Token);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths) result[path] = await HashAsync(path);
        return result;
    }

    private static void ConfigureDisposableAppEnvironment(string scenarioRoot)
    {
        var appHome = Path.Combine(scenarioRoot, "app-home");
        var fakeGame = Path.Combine(scenarioRoot, "fake-game");
        var debug = Path.Combine(scenarioRoot, "debug");
        Directory.CreateDirectory(appHome);
        Directory.CreateDirectory(fakeGame);
        Directory.CreateDirectory(debug);
        File.Copy(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Path.Combine(fakeGame, "MonsterHunterWorld.exe"),
            overwrite: true);
        Environment.SetEnvironmentVariable("MOD_MANAGER_HOME", appHome);
        Environment.SetEnvironmentVariable("MHW_MANAGER_HOME", appHome);
        Environment.SetEnvironmentVariable("MOD_MANAGER_GAME_ROOT", fakeGame);
        Environment.SetEnvironmentVariable("MHW_GAME_ROOT", fakeGame);
        Environment.SetEnvironmentVariable("MOD_MANAGER_DEBUG_ROOT", debug);
        Environment.SetEnvironmentVariable("MHW_MASTER_DEBUG_ROOT", debug);
        Record($"scenario_a.app_home={appHome}");
        Record($"scenario_a.fake_game_root={fakeGame}");
    }

    private static async Task<int> CreateExitedGateProcessAsync()
    {
        using var gate = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("could not create updater gate process");
        var pid = gate.Id;
        await gate.WaitForExitAsync(Timeout.Token);
        Check(gate.ExitCode == 0, "gate process did not exit cleanly");
        return pid;
    }

    private static async Task StopExactProcessAsync(
        int pid, string installRoot, string executableRelativePath)
    {
        Process? process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        using (process)
        {
            if (process.HasExited) return;
            var expected = Path.GetFullPath(Path.Combine(installRoot, ToNative(Normalize(executableRelativePath))));
            string? actual = null;
            try { actual = process.MainModule?.FileName; } catch { }
            Check(actual is not null && Path.GetFullPath(actual).Equals(expected, StringComparison.OrdinalIgnoreCase),
                $"refusing to stop health PID {pid}: executable identity could not be proven");
            process.Kill(entireProcessTree: true);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(Timeout.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(cts.Token);
            Record($"scenario_a.target_process_stopped_pid={pid}");
        }
    }

    private static async Task AssertOldToNewOwnedChangeAsync(
        InstallSnapshot oldSnapshot, string installRoot)
    {
        var changed = 0;
        foreach (var pair in oldSnapshot.OwnedHashes)
        {
            var path = Path.Combine(installRoot, ToNative(pair.Key));
            if (!File.Exists(path))
            {
                changed++;
                continue;
            }
            var now = await HashAsync(path);
            if (!now.Equals(pair.Value, StringComparison.OrdinalIgnoreCase)) changed++;
        }
        Check(changed > 0, "old-to-new scenario did not change any previously-owned payload/metadata bytes");
        Record($"scenario_a.old_owned_entries_changed={changed}");
    }

    private static async Task AssertOwnedSnapshotRestoredAsync(
        InstallSnapshot snapshot, string installRoot)
    {
        foreach (var pair in snapshot.OwnedHashes)
        {
            var path = Path.Combine(installRoot, ToNative(pair.Key));
            Check(File.Exists(path), $"rollback missing old owned file: {pair.Key}");
            var actual = await HashAsync(path);
            Check(actual.Equals(pair.Value, StringComparison.OrdinalIgnoreCase),
                $"rollback hash mismatch for old owned file: {pair.Key}");
        }
        Record($"scenario_b.old_owned_hashes_restored={snapshot.OwnedHashes.Count}");
    }

    private static async Task<Dictionary<string, string>> CaptureOwnedHashesAsync(
        string root, ProductFileManifest manifest)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Files)
        {
            var rel = Normalize(entry.Path);
            result[rel] = await HashAsync(Path.Combine(root, ToNative(rel)));
        }
        return result;
    }

    private static async Task<ProductFileManifest> LoadProductManifestAsync(string root)
    {
        var value = JsonSerializer.Deserialize<ProductFileManifest>(
                        await File.ReadAllTextAsync(
                            Path.Combine(root, UpdateProtocol.ProductManifestFileName), Timeout.Token),
                        UpdateProtocol.Json)
                    ?? throw new InvalidDataException("product manifest is empty");
        value.Validate();
        return value;
    }

    private static async Task<UpdateJournal> ReadJournalAsync(string path) =>
        JsonSerializer.Deserialize<UpdateJournal>(
            await File.ReadAllTextAsync(path, Timeout.Token), UpdateProtocol.Json)
        ?? throw new InvalidDataException($"journal is empty: {path}");

    private static async Task AssertSentinelsUnchangedAsync(
        string label, IReadOnlyDictionary<string, string> sentinels)
    {
        foreach (var pair in sentinels)
        {
            Check(File.Exists(pair.Key), $"{label} sentinel missing: {pair.Key}");
            var actual = await HashAsync(pair.Key);
            Check(actual.Equals(pair.Value, StringComparison.OrdinalIgnoreCase),
                $"{label} sentinel changed: {pair.Key}");
            Record($"{label}.sha256.{Path.GetFileName(pair.Key)}={actual}");
        }
    }

    private static void RecordSentinels(
        string label, IReadOnlyDictionary<string, string> sentinels)
    {
        foreach (var pair in sentinels)
            Record($"{label}.sha256.{Path.GetFileName(pair.Key)}={pair.Value}");
    }

    private static async Task<string> HashAsync(string path) =>
        await UpdatePackageVerifier.HashFileAsync(path, Timeout.Token);

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string ToNative(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Record(string line)
    {
        var value = $"{DateTimeOffset.UtcNow:O} {line}";
        Evidence.Add(value);
        Console.WriteLine(value);
    }

    private static async Task FlushEvidenceAsync(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, Evidence) + Environment.NewLine);
        await File.WriteAllBytesAsync(path, bytes);
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Arguments must be --name value pairs.");
            result[args[i][2..]] = args[i + 1];
        }
        return result;
    }

    private static string Require(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing --{key}.");

    private sealed record InstallSnapshot(
        UpdateBuildIdentity Identity,
        ReleaseInstallMarker Marker,
        ProductFileManifest Manifest,
        Dictionary<string, string> OwnedHashes);

    private sealed class InjectedE2EException(string message) : Exception(message);
}
