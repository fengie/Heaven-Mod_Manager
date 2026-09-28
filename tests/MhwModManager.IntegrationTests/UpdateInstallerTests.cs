using System.Text.Json;
using System.Diagnostics;
using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UpdateInstallerTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string id = Guid.NewGuid().ToString("N");
    private readonly string installRoot;
    private readonly string updaterTestRoot;

    public UpdateInstallerTests()
    {
        installRoot = Path.Combine(Path.GetTempPath(), "mhwmm-update-install-" + id);
        updaterTestRoot = Path.Combine(UpdatePackageStager.GetUpdaterRoot(), "tests", id);
    }

    public void Dispose()
    {
        try { Directory.Delete(installRoot, true); } catch { }
        try { Directory.Delete(updaterTestRoot, true); } catch { }
    }

    [Fact]
    public async Task Apply_preserves_user_and_unknown_files_and_retires_only_stale_owned_file()
    {
        var fixture = await CreateFixtureAsync();
        var installer = new UpdateInstaller();

        await installer.ApplyAsync(fixture.Request, TestToken);

        Assert.Equal("NEW-APP", await File.ReadAllTextAsync(Path.Combine(installRoot, "app.exe"), TestToken));
        Assert.Equal("NEW-LIB", await File.ReadAllTextAsync(Path.Combine(installRoot, "new.dll"), TestToken));
        Assert.False(File.Exists(Path.Combine(installRoot, "stale.dll")));
        Assert.Equal("USER-MOD", await File.ReadAllTextAsync(Path.Combine(installRoot, "Mods", "mine.mod"), TestToken));
        Assert.Equal("USER-STATE", await File.ReadAllTextAsync(Path.Combine(installRoot, "State", "user.dat"), TestToken));
        Assert.Equal("UNKNOWN", await File.ReadAllTextAsync(Path.Combine(installRoot, "notes.txt"), TestToken));

        var marker = await ReadMarkerAsync(Path.Combine(installRoot, UpdateProtocol.InstallMarkerFileName));
        Assert.Equal(2, marker.Build.BuildNumber);
        var journal = await ReadJournalAsync(fixture.Request.JournalPath);
        Assert.Equal(UpdateJournalPhase.AppliedAwaitingHealth, journal.Phase);
        Assert.True(Directory.Exists(fixture.Request.BackupRoot));

        await installer.ConfirmAsync(fixture.Request, TestToken);

        journal = await ReadJournalAsync(fixture.Request.JournalPath);
        Assert.Equal(UpdateJournalPhase.Confirmed, journal.Phase);
        Assert.False(Directory.Exists(fixture.Request.BackupRoot));
    }

    [Fact]
    public async Task Cancellation_after_first_replacement_restores_exact_previous_payload()
    {
        var fixture = await CreateFixtureAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        var installer = new UpdateInstaller((point, _) =>
        {
            if (point == UpdateApplyFaultPoint.AfterFileApply) cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => installer.ApplyAsync(fixture.Request, cancellation.Token));

        await AssertOldInstallRestoredAsync();
        Assert.False(File.Exists(Path.Combine(installRoot, "new.dll")));
        Assert.Equal(UpdateJournalPhase.RolledBack, (await ReadJournalAsync(fixture.Request.JournalPath)).Phase);
    }

    [Fact]
    public async Task Incoming_file_collision_with_unknown_user_file_fails_before_mutation()
    {
        var fixture = await CreateFixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(installRoot, "new.dll"), "USER-OWNED", TestToken);

        await Assert.ThrowsAsync<IOException>(() => new UpdateInstaller().ApplyAsync(fixture.Request, TestToken));

        await AssertOldInstallRestoredAsync();
        Assert.Equal("USER-OWNED", await File.ReadAllTextAsync(Path.Combine(installRoot, "new.dll"), TestToken));
        Assert.False(Directory.Exists(fixture.Request.BackupRoot));
    }

    [Fact]
    public async Task Recovery_rejects_journal_for_a_different_update_before_mutation()
    {
        var fixture = await CreateFixtureAsync();
        await WriteJsonAsync(fixture.Request.JournalPath,
            new UpdateJournal(UpdateJournalPhase.Applying, 999, "other-source", DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateInstaller().ApplyAsync(fixture.Request, TestToken));
        await AssertOldInstallRestoredAsync();
        Assert.False(Directory.Exists(fixture.Request.BackupRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_metadata_publication_recovers_before_install_validation(bool markerWasPublished)
    {
        var fixture = await CreateFixtureAsync();
        var interrupted = new UpdateInstaller((point, _) =>
        {
            if (point == UpdateApplyFaultPoint.AfterBackup)
                throw new InjectedUpdateFailureException("simulate process interruption after backup");
        });
        await Assert.ThrowsAsync<InjectedUpdateFailureException>(() => interrupted.ApplyAsync(fixture.Request, TestToken));
        File.Copy(Path.Combine(fixture.Request.StagingRoot, "app.exe"), Path.Combine(installRoot, "app.exe"), true);
        File.Copy(Path.Combine(fixture.Request.StagingRoot, UpdateProtocol.ProductManifestFileName),
            Path.Combine(installRoot, UpdateProtocol.ProductManifestFileName), true);
        if (markerWasPublished)
            File.Copy(Path.Combine(fixture.Request.StagingRoot, UpdateProtocol.InstallMarkerFileName),
                Path.Combine(installRoot, UpdateProtocol.InstallMarkerFileName), true);
        await WriteJsonAsync(fixture.Request.JournalPath,
            new UpdateJournal(UpdateJournalPhase.Applying, fixture.Request.Manifest.BuildNumber,
                fixture.Request.Manifest.SourceSha, DateTimeOffset.UtcNow));

        var resumed = new UpdateInstaller();
        await resumed.ApplyAsync(fixture.Request, TestToken);
        Assert.Equal(UpdateJournalPhase.AppliedAwaitingHealth, (await ReadJournalAsync(fixture.Request.JournalPath)).Phase);
        await resumed.RollbackAsync(fixture.Request, TestToken);
        await AssertOldInstallRestoredAsync();
    }

    [Fact]
    public async Task Corrupt_backup_blocks_rollback_before_any_live_file_changes()
    {
        var fixture = await CreateFixtureAsync();
        var installer = new UpdateInstaller();
        await installer.ApplyAsync(fixture.Request, TestToken);
        await File.WriteAllTextAsync(Path.Combine(fixture.Request.BackupRoot, "stale.dll"), "CORRUPT", TestToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => installer.RollbackAsync(fixture.Request, TestToken));

        Assert.Equal("NEW-APP", await File.ReadAllTextAsync(Path.Combine(installRoot, "app.exe"), TestToken));
        Assert.Equal("NEW-LIB", await File.ReadAllTextAsync(Path.Combine(installRoot, "new.dll"), TestToken));
        Assert.Equal(UpdateJournalPhase.AppliedAwaitingHealth, (await ReadJournalAsync(fixture.Request.JournalPath)).Phase);
    }

    [Fact]
    public async Task Restart_failure_restores_previous_payload_and_attempts_previous_launch()
    {
        var fixture = await CreateFixtureAsync();
        var installer = new UpdateInstaller();
        await installer.ApplyAsync(fixture.Request, TestToken);
        var previousLaunched = false;
        string? previousExecutable = null;
        var coordinator = new UpdateRestartCoordinator(
            _ => throw new System.ComponentModel.Win32Exception("injected launch failure"),
            executable =>
            {
                previousLaunched = true;
                previousExecutable = executable;
                return Process.GetCurrentProcess();
            },
            _ => throw new InvalidOperationException("No target process was started."));

        Assert.Equal(5, await coordinator.RunAsync(installer, fixture.Request, TimeSpan.FromMilliseconds(100)));
        Assert.True(previousLaunched);
        Assert.Equal("app.exe", previousExecutable);
        await AssertOldInstallRestoredAsync();
    }

    [Fact]
    public async Task Rollback_restart_uses_restored_previous_executable_when_target_name_changed()
    {
        var fixture = await CreateFixtureAsync("old-manager.exe", "new-manager.exe");
        var installer = new UpdateInstaller();
        await installer.ApplyAsync(fixture.Request, TestToken);
        string? restartedExecutable = null;
        var coordinator = new UpdateRestartCoordinator(
            _ => throw new System.ComponentModel.Win32Exception("injected target launch failure"),
            executable =>
            {
                restartedExecutable = executable;
                return Process.GetCurrentProcess();
            },
            _ => throw new InvalidOperationException("No target process was started."));

        Assert.Equal(5, await coordinator.RunAsync(
            installer, fixture.Request, TimeSpan.FromMilliseconds(100)));

        Assert.Equal("old-manager.exe", restartedExecutable);
        Assert.Equal(
            "OLD-APP",
            await File.ReadAllTextAsync(Path.Combine(installRoot, "old-manager.exe"), TestToken));
        Assert.False(File.Exists(Path.Combine(installRoot, "new-manager.exe")));
        var restoredMarker = await ReleaseInstallMarker.LoadAsync(installRoot, TestToken);
        Assert.Equal("old-manager.exe", restoredMarker.ExecutableRelativePath);
        Assert.Equal(1, restoredMarker.Build.BuildNumber);
    }

    [Fact]
    public async Task Unproven_target_exit_blocks_rollback_and_preserves_recovery_material()
    {
        var fixture = await CreateFixtureAsync();
        var installer = new UpdateInstaller();
        await installer.ApplyAsync(fixture.Request, TestToken);
        var coordinator = new UpdateRestartCoordinator(
            _ => Process.GetCurrentProcess(),
            _ => throw new InvalidOperationException("Previous application must not launch while target exit is unproven."),
            _ => Task.FromResult(false));

        Assert.Equal(4, await coordinator.RunAsync(installer, fixture.Request, TimeSpan.FromMilliseconds(30)));
        Assert.Equal("NEW-APP", await File.ReadAllTextAsync(Path.Combine(installRoot, "app.exe"), TestToken));
        Assert.Equal(UpdateJournalPhase.AppliedAwaitingHealth, (await ReadJournalAsync(fixture.Request.JournalPath)).Phase);
        Assert.True(Directory.Exists(fixture.Request.BackupRoot));
    }

    [Fact]
    public async Task Resume_ambiguous_launch_with_matching_health_confirms_without_duplicate_start()
    {
        var fixture = await CreateFixtureAsync();
        var installer = new UpdateInstaller();
        await installer.ApplyAsync(fixture.Request, TestToken);
        var launch = await UpdateLaunchStateStore.BeginAsync(fixture.Request, TestToken);
        await WriteJsonAsync(
            fixture.Request.HealthFile,
            new UpdateStartupHealth(
                fixture.Request.HealthToken,
                launch.AttemptId,
                Environment.ProcessId,
                fixture.Request.Manifest.BuildNumber,
                fixture.Request.Manifest.SourceSha,
                DateTimeOffset.UtcNow));

        var targetLaunches = 0;
        var previousLaunches = 0;
        var coordinator = new UpdateRestartCoordinator(
            _ =>
            {
                targetLaunches++;
                return Process.GetCurrentProcess();
            },
            _ =>
            {
                previousLaunches++;
                return Process.GetCurrentProcess();
            },
            _ => Task.FromResult(true));

        Assert.Equal(
            0,
            await coordinator.ResumeAsync(installer, fixture.Request, TimeSpan.FromMilliseconds(250)));
        Assert.Equal(0, targetLaunches);
        Assert.Equal(0, previousLaunches);
        Assert.Equal(
            UpdateJournalPhase.Confirmed,
            (await ReadJournalAsync(fixture.Request.JournalPath)).Phase);
        Assert.False(Directory.Exists(fixture.Request.BackupRoot));
        Assert.False(File.Exists(UpdateLaunchStateStore.GetPath(fixture.Request)));
    }

    [Fact]
    public async Task Helper_process_resume_confirms_existing_launch_without_duplicate_target()
    {
        var fixture = await CreateFixtureAsync();
        var installer = new UpdateInstaller();
        var helperRequest = fixture.Request with { CurrentProcessId = 0 };
        await installer.ApplyAsync(helperRequest, TestToken);
        var launch = await UpdateLaunchStateStore.BeginAsync(helperRequest, TestToken);
        await WriteJsonAsync(
            helperRequest.HealthFile,
            new UpdateStartupHealth(
                helperRequest.HealthToken,
                launch.AttemptId,
                Environment.ProcessId,
                helperRequest.Manifest.BuildNumber,
                helperRequest.Manifest.SourceSha,
                DateTimeOffset.UtcNow));
        var requestPath = Path.Combine(updaterTestRoot, "helper-request.json");
        await UpdateRequestStore.WriteAsync(requestPath, helperRequest, TestToken);

        var repositoryRoot = FindRepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Release";
        var helperDll = Path.Combine(
            repositoryRoot,
            "src",
            "MhwModManager.Updater.Helper",
            "bin",
            configuration,
            "net10.0-windows10.0.19041.0",
            "MHW Mod Manager Updater.dll");
        Assert.True(File.Exists(helperDll), $"Updater helper fixture is missing: {helperDll}");

        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(helperDll);
        start.ArgumentList.Add("--request");
        start.ArgumentList.Add(requestPath);

        using var helper = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start updater helper fixture.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await helper.WaitForExitAsync(timeout.Token);
        var stdout = await helper.StandardOutput.ReadToEndAsync(TestToken);
        var stderr = await helper.StandardError.ReadToEndAsync(TestToken);

        Assert.True(
            helper.ExitCode == 0,
            $"Updater helper exit={helper.ExitCode}{Environment.NewLine}stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
        Assert.Equal(
            UpdateJournalPhase.Confirmed,
            (await ReadJournalAsync(helperRequest.JournalPath)).Phase);
        Assert.False(Directory.Exists(helperRequest.BackupRoot));
        Assert.False(File.Exists(UpdateLaunchStateStore.GetPath(helperRequest)));
        Assert.Equal("NEW-APP", await File.ReadAllTextAsync(Path.Combine(installRoot, "app.exe"), TestToken));
    }

    [Fact]
    public async Task Resume_ambiguous_launch_without_health_never_starts_duplicate_or_rolls_back()
    {
        var fixture = await CreateFixtureAsync();
        var installer = new UpdateInstaller();
        await installer.ApplyAsync(fixture.Request, TestToken);
        await UpdateLaunchStateStore.BeginAsync(fixture.Request, TestToken);

        var targetLaunches = 0;
        var previousLaunches = 0;
        var coordinator = new UpdateRestartCoordinator(
            _ =>
            {
                targetLaunches++;
                return Process.GetCurrentProcess();
            },
            _ =>
            {
                previousLaunches++;
                return Process.GetCurrentProcess();
            },
            _ => Task.FromResult(true));

        Assert.Equal(
            4,
            await coordinator.ResumeAsync(installer, fixture.Request, TimeSpan.FromMilliseconds(40)));
        Assert.Equal(0, targetLaunches);
        Assert.Equal(0, previousLaunches);
        Assert.Equal("NEW-APP", await File.ReadAllTextAsync(Path.Combine(installRoot, "app.exe"), TestToken));
        Assert.Equal(
            UpdateJournalPhase.AppliedAwaitingHealth,
            (await ReadJournalAsync(fixture.Request.JournalPath)).Phase);
        Assert.True(Directory.Exists(fixture.Request.BackupRoot));
        Assert.True(File.Exists(UpdateLaunchStateStore.GetPath(fixture.Request)));
    }

    [Fact]
    public async Task Resume_tracked_live_process_uses_exact_attempt_without_relaunch()
    {
        var fixture = await CreateFixtureAsync();
        var installer = new UpdateInstaller();
        await installer.ApplyAsync(fixture.Request, TestToken);
        var launch = await UpdateLaunchStateStore.BeginAsync(fixture.Request, TestToken);
        using var target = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -Command Start-Sleep -Seconds 5")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start tracked target fixture.");
        launch = await UpdateLaunchStateStore.RecordStartedAsync(fixture.Request, launch, target, TestToken);
        await WriteJsonAsync(
            fixture.Request.HealthFile,
            new UpdateStartupHealth(
                fixture.Request.HealthToken,
                launch.AttemptId,
                target.Id,
                fixture.Request.Manifest.BuildNumber,
                fixture.Request.Manifest.SourceSha,
                DateTimeOffset.UtcNow));

        var targetLaunches = 0;
        var coordinator = new UpdateRestartCoordinator(
            _ =>
            {
                targetLaunches++;
                return Process.GetCurrentProcess();
            },
            _ => throw new InvalidOperationException("Previous app must not restart after successful health."),
            _ => Task.FromResult(true));

        Assert.Equal(
            0,
            await coordinator.ResumeAsync(installer, fixture.Request, TimeSpan.FromMilliseconds(250)));
        Assert.Equal(0, targetLaunches);
        Assert.Equal(
            UpdateJournalPhase.Confirmed,
            (await ReadJournalAsync(fixture.Request.JournalPath)).Phase);

        if (!target.HasExited) target.Kill(entireProcessTree: true);
    }

    [Fact]
    public async Task Modified_stale_owned_file_is_preserved_instead_of_deleted()
    {
        var fixture = await CreateFixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(installRoot, "stale.dll"), "USER-MODIFIED", TestToken);
        var installer = new UpdateInstaller();

        await installer.ApplyAsync(fixture.Request, TestToken);

        Assert.Equal("USER-MODIFIED",
            await File.ReadAllTextAsync(Path.Combine(installRoot, "stale.dll"), TestToken));
    }

    [Fact]
    public async Task Failure_after_first_replacement_restores_exact_previous_payload()
    {
        var fixture = await CreateFixtureAsync();
        var injected = false;
        var installer = new UpdateInstaller((point, _) =>
        {
            if (point != UpdateApplyFaultPoint.AfterFileApply || injected) return;
            injected = true;
            throw new InjectedUpdateFailureException("after-first-file");
        });

        await Assert.ThrowsAsync<InjectedUpdateFailureException>(
            () => installer.ApplyAsync(fixture.Request, TestToken));

        await AssertOldInstallRestoredAsync();
        var journal = await ReadJournalAsync(fixture.Request.JournalPath);
        Assert.Equal(UpdateJournalPhase.RolledBack, journal.Phase);
    }

    [Fact]
    public async Task Failure_after_stale_owned_deletion_restores_deleted_and_replaced_files()
    {
        var fixture = await CreateFixtureAsync();
        var installer = new UpdateInstaller((point, _) =>
        {
            if (point == UpdateApplyFaultPoint.AfterStaleOwnedRemoval)
                throw new InjectedUpdateFailureException("after-stale-delete");
        });

        await Assert.ThrowsAsync<InjectedUpdateFailureException>(
            () => installer.ApplyAsync(fixture.Request, TestToken));

        await AssertOldInstallRestoredAsync();
        Assert.Equal("STALE", await File.ReadAllTextAsync(Path.Combine(installRoot, "stale.dll"), TestToken));
        Assert.False(File.Exists(Path.Combine(installRoot, "new.dll")));
    }

    [Fact]
    public async Task Missing_packaged_install_marker_blocks_apply_before_live_mutation()
    {
        var fixture = await CreateFixtureAsync();
        File.Delete(Path.Combine(installRoot, UpdateProtocol.InstallMarkerFileName));
        var installer = new UpdateInstaller();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => installer.ApplyAsync(fixture.Request, TestToken));

        Assert.Equal("OLD-APP", await File.ReadAllTextAsync(Path.Combine(installRoot, "app.exe"), TestToken));
        Assert.False(Directory.Exists(fixture.Request.BackupRoot));
    }

    [Fact]
    public async Task Same_or_older_target_build_is_rejected_before_live_mutation()
    {
        var fixture = await CreateFixtureAsync();
        var request = fixture.Request with
        {
            Manifest = fixture.Request.Manifest with { BuildNumber = 1, SourceSha = "sha-old-abcdef" }
        };
        var installer = new UpdateInstaller();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => installer.ApplyAsync(request, TestToken));

        Assert.Equal("OLD-APP", await File.ReadAllTextAsync(Path.Combine(installRoot, "app.exe"), TestToken));
    }

    [Fact]
    public async Task Staged_install_marker_must_match_target_manifest_before_apply()
    {
        var fixture = await CreateFixtureAsync();
        var markerPath = Path.Combine(fixture.Request.StagingRoot, UpdateProtocol.InstallMarkerFileName);
        var marker = await ReadMarkerAsync(markerPath);
        await WriteJsonAsync(markerPath, marker with
        {
            Build = marker.Build with { SourceSha = "different-sha-abcdef" }
        });
        var installer = new UpdateInstaller();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.ApplyAsync(fixture.Request, TestToken));

        Assert.Equal("OLD-APP", await File.ReadAllTextAsync(Path.Combine(installRoot, "app.exe"), TestToken));
    }

    private async Task<UpdateFixture> CreateFixtureAsync(
        string oldExecutable = "app.exe",
        string newExecutable = "app.exe")
    {
        UpdatePackageStager.EnsureUpdaterRoot(UpdatePackageStager.GetUpdaterRoot());
        Directory.CreateDirectory(updaterTestRoot);
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "Mods"));
        Directory.CreateDirectory(Path.Combine(installRoot, "State"));

        await File.WriteAllTextAsync(Path.Combine(installRoot, oldExecutable), "OLD-APP", TestToken);
        await File.WriteAllTextAsync(Path.Combine(installRoot, "stale.dll"), "STALE", TestToken);
        await File.WriteAllTextAsync(Path.Combine(installRoot, "Mods", "mine.mod"), "USER-MOD", TestToken);
        await File.WriteAllTextAsync(Path.Combine(installRoot, "State", "user.dat"), "USER-STATE", TestToken);
        await File.WriteAllTextAsync(Path.Combine(installRoot, "notes.txt"), "UNKNOWN", TestToken);

        var oldManifest = new ProductFileManifest(1,
        [
            await EntryAsync(installRoot, oldExecutable),
            await EntryAsync(installRoot, "stale.dll")
        ]);
        var oldManifestPath = Path.Combine(installRoot, UpdateProtocol.ProductManifestFileName);
        await WriteJsonAsync(oldManifestPath, oldManifest);
        var oldProductHash = await UpdatePackageVerifier.HashFileAsync(oldManifestPath, TestToken);
        var oldIdentity = new UpdateBuildIdentity(1, UpdateProtocol.Channel, "8.8.0",
            "sha-old-abcdef", 1, DateTimeOffset.UtcNow.AddMinutes(-5));
        var oldMarker = new ReleaseInstallMarker(1, UpdateProtocol.ProductId, UpdateProtocol.Channel,
            oldIdentity, oldExecutable, oldProductHash);
        await WriteJsonAsync(Path.Combine(installRoot, UpdateProtocol.InstallMarkerFileName), oldMarker);

        var stage = Path.Combine(updaterTestRoot, "stage");
        Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(Path.Combine(stage, newExecutable), "NEW-APP", TestToken);
        await File.WriteAllTextAsync(Path.Combine(stage, "new.dll"), "NEW-LIB", TestToken);
        var newManifest = new ProductFileManifest(1,
        [
            await EntryAsync(stage, newExecutable),
            await EntryAsync(stage, "new.dll")
        ]);
        var newManifestPath = Path.Combine(stage, UpdateProtocol.ProductManifestFileName);
        await WriteJsonAsync(newManifestPath, newManifest);
        var newProductHash = await UpdatePackageVerifier.HashFileAsync(newManifestPath, TestToken);

        var newIdentity = new UpdateBuildIdentity(1, UpdateProtocol.Channel, "8.8.0",
            "sha-new-abcdef", 2, DateTimeOffset.UtcNow);
        var newMarker = new ReleaseInstallMarker(1, UpdateProtocol.ProductId, UpdateProtocol.Channel,
            newIdentity, newExecutable, newProductHash);
        await WriteJsonAsync(Path.Combine(stage, UpdateProtocol.InstallMarkerFileName), newMarker);

        var updateManifest = new UpdateManifest(
            1,
            UpdateProtocol.Channel,
            "8.8.0",
            newIdentity.SourceSha,
            newIdentity.BuildNumber,
            "update.zip",
            123,
            new string('A', 64),
            newProductHash,
            newExecutable,
            1,
            DateTimeOffset.UtcNow);

        var request = new UpdateApplyRequest(
            updateManifest,
            installRoot,
            stage,
            Path.Combine(updaterTestRoot, "backup"),
            Path.Combine(updaterTestRoot, "journal.json"),
            Path.Combine(updaterTestRoot, "pending.json"),
            Path.Combine(updaterTestRoot, "health.txt"),
            Guid.NewGuid().ToString("N"),
            Environment.ProcessId,
            []);

        return new UpdateFixture(request);
    }

    private async Task AssertOldInstallRestoredAsync()
    {
        Assert.Equal("OLD-APP", await File.ReadAllTextAsync(Path.Combine(installRoot, "app.exe"), TestToken));
        Assert.Equal("USER-MOD", await File.ReadAllTextAsync(Path.Combine(installRoot, "Mods", "mine.mod"), TestToken));
        Assert.Equal("USER-STATE", await File.ReadAllTextAsync(Path.Combine(installRoot, "State", "user.dat"), TestToken));
        Assert.Equal("UNKNOWN", await File.ReadAllTextAsync(Path.Combine(installRoot, "notes.txt"), TestToken));
        var marker = await ReadMarkerAsync(Path.Combine(installRoot, UpdateProtocol.InstallMarkerFileName));
        Assert.Equal(1, marker.Build.BuildNumber);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MhwModManager.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from integration-test output.");
    }

    private static async Task<ProductFileEntry> EntryAsync(string root, string relative)
    {
        var path = Path.Combine(root, relative);
        var info = new FileInfo(path);
        return new ProductFileEntry(
            relative.Replace('\\', '/'),
            info.Length,
            await UpdatePackageVerifier.HashFileAsync(path, TestToken));
    }

    private static Task WriteJsonAsync<T>(string path, T value) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, UpdateProtocol.Json), TestToken);

    private static async Task<ReleaseInstallMarker> ReadMarkerAsync(string path) =>
        JsonSerializer.Deserialize<ReleaseInstallMarker>(
            await File.ReadAllTextAsync(path, TestToken), UpdateProtocol.Json)
        ?? throw new InvalidDataException("Marker fixture is empty.");

    private static async Task<UpdateJournal> ReadJournalAsync(string path) =>
        JsonSerializer.Deserialize<UpdateJournal>(
            await File.ReadAllTextAsync(path, TestToken), UpdateProtocol.Json)
        ?? throw new InvalidDataException("Journal fixture is empty.");

    private sealed record UpdateFixture(UpdateApplyRequest Request);

    private sealed class InjectedUpdateFailureException(string message) : Exception(message);
}
