using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class SaveSnapshotConcurrencyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(),
        "MhwSaveSnapshotConcurrency-" + Guid.NewGuid().ToString("N"));

    public SaveSnapshotConcurrencyTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task ActiveCaptureRejectsConcurrentPruningAndCaptureUntilIndexed()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(root, "snapshots.db"));
        await db.InitializeAsync(ct);
        var game = await CreateGameAsync(ct);
        var state = Path.Combine(root, "state");
        var owner = new SaveBackupService(db, state, game);
        var peer = new SaveBackupService(db, state, game);

        var first = await owner.CreateCoreAsync("first", async (directory, token) =>
        {
            var sentinel = Path.Combine(directory, "in-progress.txt");
            await File.WriteAllTextAsync(sentinel, "active capture", token);

            // The capture has created its directory but has no database row yet.
            // Prune must not treat that directory as an orphan.
            await Assert.ThrowsAsync<IOException>(() => peer.PruneAsync(30, token));
            await Assert.ThrowsAsync<IOException>(() => peer.CreateAsync("competing", token));
            Assert.Equal("active capture", await File.ReadAllTextAsync(sentinel, token));
            Assert.Single(Directory.EnumerateDirectories(owner.SnapshotRoot));
            Assert.Equal(0L, await CountSnapshotsAsync(db, token));
        }, ct);

        Assert.True(first.Success);
        Assert.True(File.Exists(Path.Combine(first.SnapshotRoot, "snapshot.json")));
        Assert.Equal(1L, await CountSnapshotsAsync(db, ct));

        // Once capture and indexing complete, the same root can be used again.
        var second = await peer.CreateAsync("second", ct);
        Assert.True(second.Success);
        Assert.NotEqual(first.SnapshotRoot, second.SnapshotRoot);
        await peer.PruneAsync(30, ct);
        Assert.Equal(2L, await CountSnapshotsAsync(db, ct));
        Assert.True(File.Exists(Path.Combine(first.SnapshotRoot, "snapshot.json")));
        Assert.True(File.Exists(Path.Combine(second.SnapshotRoot, "snapshot.json")));
    }

    [Fact]
    public async Task FailedCaptureReleasesLeaseWithoutPublishingPartialSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(root, "failed.db"));
        await db.InitializeAsync(ct);
        var game = await CreateGameAsync(ct);
        var state = Path.Combine(root, "failed-state");
        var owner = new SaveBackupService(db, state, game);
        var peer = new SaveBackupService(db, state, game);

        await Assert.ThrowsAsync<IOException>(() => owner.CreateCoreAsync(
            "injected failure", (_, _) => throw new IOException("capture failed"), ct));

        Assert.Empty(Directory.EnumerateDirectories(owner.SnapshotRoot));
        Assert.Equal(0L, await CountSnapshotsAsync(db, ct));

        var recovered = await peer.CreateAsync("recovered", ct);
        Assert.True(recovered.Success);
        Assert.Equal(1L, await CountSnapshotsAsync(db, ct));
    }

    [Fact]
    public async Task LinkedSnapshotRootCannotBeUsedForCaptureOrPrune()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(root, "linked.db"));
        await db.InitializeAsync(ct);
        var game = await CreateGameAsync(ct);
        var service = new SaveBackupService(db, Path.Combine(root, "linked-state"), game);
        var external = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(external, "unindexed")).FullName;
        var sentinel = Path.Combine(child, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "not manager-owned", ct);
        Directory.CreateDirectory(Path.GetDirectoryName(service.SnapshotRoot)!);
        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("cmd.exe",
                $"/d /c mklink /J \"{service.SnapshotRoot}\" \"{external}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Could not create fixture junction.");
        await process.WaitForExitAsync(ct);
        Assert.Equal(0, process.ExitCode);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.PruneAsync(30, ct));
            await Assert.ThrowsAsync<InvalidDataException>(() => service.CreateAsync("linked", ct));
            Assert.Equal("not manager-owned", await File.ReadAllTextAsync(sentinel, ct));
            Assert.False(File.Exists(Path.Combine(external, ".snapshot-operation.lock")));
            Assert.Equal(0L, await CountSnapshotsAsync(db, ct));
        }
        finally
        {
            Directory.Delete(service.SnapshotRoot, recursive: false);
        }
    }

    private async Task<GameProfile> CreateGameAsync(CancellationToken ct)
    {
        var save = Path.Combine(root, "fixture-save.bin");
        await File.WriteAllTextAsync(save, "isolated test save", ct);
        return GameProfile.Generic("snapshot-test", "Snapshot Test", Path.Combine(root, "game"),
            "game.exe", "") with { SavePath = save };
    }

    private static async Task<long> CountSnapshotsAsync(ManagerDatabase db, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM save_snapshots";
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
