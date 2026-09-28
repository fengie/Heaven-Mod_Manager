using System.Text.Json;
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
        Assert.Equal("USER-INBOX", await File.ReadAllTextAsync(Path.Combine(installRoot, "Inbox", "drop.zip"), TestToken));
        Assert.Equal("USER-ARCHIVE", await File.ReadAllTextAsync(Path.Combine(installRoot, "Mods Archive", "old.zip"), TestToken));
        Assert.Equal("USER-GAME", await File.ReadAllTextAsync(Path.Combine(installRoot, "Games", "mhw", "workspace.dat"), TestToken));
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

    private async Task<UpdateFixture> CreateFixtureAsync()
    {
        UpdatePackageStager.EnsureUpdaterRoot(UpdatePackageStager.GetUpdaterRoot());
        Directory.CreateDirectory(updaterTestRoot);
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "Mods"));
        Directory.CreateDirectory(Path.Combine(installRoot, "State"));
        Directory.CreateDirectory(Path.Combine(installRoot, "Inbox"));
        Directory.CreateDirectory(Path.Combine(installRoot, "Mods Archive"));
        Directory.CreateDirectory(Path.Combine(installRoot, "Games", "mhw"));

        await File.WriteAllTextAsync(Path.Combine(installRoot, "app.exe"), "OLD-APP", TestToken);
        await File.WriteAllTextAsync(Path.Combine(installRoot, "stale.dll"), "STALE", TestToken);
        await File.WriteAllTextAsync(Path.Combine(installRoot, "Mods", "mine.mod"), "USER-MOD", TestToken);
        await File.WriteAllTextAsync(Path.Combine(installRoot, "State", "user.dat"), "USER-STATE", TestToken);
        await File.WriteAllTextAsync(Path.Combine(installRoot, "Inbox", "drop.zip"), "USER-INBOX", TestToken);
        await File.WriteAllTextAsync(Path.Combine(installRoot, "Mods Archive", "old.zip"), "USER-ARCHIVE", TestToken);
        await File.WriteAllTextAsync(Path.Combine(installRoot, "Games", "mhw", "workspace.dat"), "USER-GAME", TestToken);
        await File.WriteAllTextAsync(Path.Combine(installRoot, "notes.txt"), "UNKNOWN", TestToken);

        var oldManifest = new ProductFileManifest(1,
        [
            await EntryAsync(installRoot, "app.exe"),
            await EntryAsync(installRoot, "stale.dll")
        ]);
        var oldManifestPath = Path.Combine(installRoot, UpdateProtocol.ProductManifestFileName);
        await WriteJsonAsync(oldManifestPath, oldManifest);
        var oldProductHash = await UpdatePackageVerifier.HashFileAsync(oldManifestPath, TestToken);
        var oldIdentity = new UpdateBuildIdentity(1, UpdateProtocol.Channel, "8.8.0",
            "sha-old-abcdef", 1, DateTimeOffset.UtcNow.AddMinutes(-5));
        var oldMarker = new ReleaseInstallMarker(1, UpdateProtocol.ProductId, UpdateProtocol.Channel,
            oldIdentity, oldProductHash);
        await WriteJsonAsync(Path.Combine(installRoot, UpdateProtocol.InstallMarkerFileName), oldMarker);

        var stage = Path.Combine(updaterTestRoot, "stage");
        Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(Path.Combine(stage, "app.exe"), "NEW-APP", TestToken);
        await File.WriteAllTextAsync(Path.Combine(stage, "new.dll"), "NEW-LIB", TestToken);
        var newManifest = new ProductFileManifest(1,
        [
            await EntryAsync(stage, "app.exe"),
            await EntryAsync(stage, "new.dll")
        ]);
        var newManifestPath = Path.Combine(stage, UpdateProtocol.ProductManifestFileName);
        await WriteJsonAsync(newManifestPath, newManifest);
        var newProductHash = await UpdatePackageVerifier.HashFileAsync(newManifestPath, TestToken);

        var newIdentity = new UpdateBuildIdentity(1, UpdateProtocol.Channel, "8.8.0",
            "sha-new-abcdef", 2, DateTimeOffset.UtcNow);
        var newMarker = new ReleaseInstallMarker(1, UpdateProtocol.ProductId, UpdateProtocol.Channel,
            newIdentity, newProductHash);
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
            "app.exe",
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
        Assert.Equal("USER-INBOX", await File.ReadAllTextAsync(Path.Combine(installRoot, "Inbox", "drop.zip"), TestToken));
        Assert.Equal("USER-ARCHIVE", await File.ReadAllTextAsync(Path.Combine(installRoot, "Mods Archive", "old.zip"), TestToken));
        Assert.Equal("USER-GAME", await File.ReadAllTextAsync(Path.Combine(installRoot, "Games", "mhw", "workspace.dat"), TestToken));
        Assert.Equal("UNKNOWN", await File.ReadAllTextAsync(Path.Combine(installRoot, "notes.txt"), TestToken));
        var marker = await ReadMarkerAsync(Path.Combine(installRoot, UpdateProtocol.InstallMarkerFileName));
        Assert.Equal(1, marker.Build.BuildNumber);
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
