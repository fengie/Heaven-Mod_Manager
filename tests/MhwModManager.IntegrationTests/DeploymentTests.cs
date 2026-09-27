using MhwModManager.Core;
using MhwModManager.Storage;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class DeploymentTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-v8-test-" + Guid.NewGuid().ToString("N"));
    public DeploymentTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    private async Task<(string game, ManagerDatabase db, HashingService hashing, BlobStore blobs)> CreateAsync(string name)
    {
        var game = Path.Combine(root, name, "game");
        var state = Path.Combine(root, name, "state");
        Directory.CreateDirectory(Path.Combine(game, "nativePC"));
        var db = new ManagerDatabase(Path.Combine(state, "m.db"));
        await db.InitializeAsync(TestToken);
        var hashing = new HashingService();
        var blobs = new BlobStore(Path.Combine(state, "blobs"), db);
        return (game, db, hashing, blobs);
    }

    private async Task<string> BlobAsync(BlobStore blobs, string name, string contents)
    {
        var file = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(file, contents, TestToken);
        return await blobs.CaptureAsync(file, TestToken);
    }

    [Fact]
    public async Task First_takeover_restore_and_undo_preserve_ownership_semantics()
    {
        var (game, db, hashing, blobs) = await CreateAsync("ownership");
        var live = Path.Combine(game, "nativePC", "same.tex");
        await File.WriteAllTextAsync(live, "ORIGINAL", TestToken);
        var modHash = await BlobAsync(blobs, "mod", "MOD");
        var executor = new DeploymentExecutor(db, blobs, hashing, game);

        var add = new DeploymentPlan("add", DateTimeOffset.UtcNow,
            [new(1, ChangeKind.Add, @"nativePC\same.tex", null, modHash, null, "m", null, null)], [], []);
        var result = await executor.ApplyAsync(add, "take over", ct: TestToken);
        Assert.True(result.Success);
        Assert.Equal("MOD", await File.ReadAllTextAsync(live, TestToken));
        var snap = await db.LoadPlannerSnapshotAsync(TestToken);
        Assert.Contains(@"nativePC\same.tex", snap.Originals.Keys);
        var original = snap.Originals[@"nativePC\same.tex"];

        var remove = new DeploymentPlan("restore", DateTimeOffset.UtcNow,
            [new(1, ChangeKind.RestoreOriginal, @"nativePC\same.tex", modHash, original, "m", null, modHash, null)], [], []);
        var r2 = await executor.ApplyAsync(remove, "restore original", ct: TestToken);
        Assert.True(r2.Success);
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(live, TestToken));
        snap = await db.LoadPlannerSnapshotAsync(TestToken);
        Assert.DoesNotContain(@"nativePC\same.tex", snap.Originals.Keys); // ownership released
        Assert.DoesNotContain(@"nativePC\same.tex", snap.CurrentManifest.Keys);

        var undo = await executor.UndoLastAsync(TestToken);
        Assert.True(undo.Success);
        Assert.Equal("MOD", await File.ReadAllTextAsync(live, TestToken));
        snap = await db.LoadPlannerSnapshotAsync(TestToken);
        Assert.Contains(@"nativePC\same.tex", snap.Originals.Keys); // baseline re-registered
        Assert.Equal("m", snap.CurrentManifest[@"nativePC\same.tex"].ProviderModId);
    }

    [Fact]
    public async Task Startup_recovery_handles_crash_after_bytes_change_before_status_update()
    {
        var (game, db, hashing, blobs) = await CreateAsync("crash-after-write");
        var live = Path.Combine(game, "nativePC", "x.tex");
        await File.WriteAllTextAsync(live, "ORIGINAL", TestToken);
        var modHash = await BlobAsync(blobs, "crash-mod", "MOD");
        var plan = new DeploymentPlan("crash-op", DateTimeOffset.UtcNow,
            [new(1, ChangeKind.Add, @"nativePC\x.tex", null, modHash, null, "m", null, null)], [], []);

        var crashing = new DeploymentExecutor(db, blobs, hashing, game, (stage, seq) =>
        {
            if (stage == "after-file-write" && seq == 1) throw new SimulatedCrashException("boom");
        });
        await Assert.ThrowsAsync<SimulatedCrashException>(() => crashing.ApplyAsync(plan, "crash fixture", ct: TestToken));
        Assert.Equal("MOD", await File.ReadAllTextAsync(live, TestToken));

        var recovered = new DeploymentExecutor(db, blobs, hashing, game);
        await recovered.RecoverIncompleteAsync(TestToken);
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(live, TestToken));
        var snap = await db.LoadPlannerSnapshotAsync(TestToken);
        Assert.DoesNotContain(@"nativePC\x.tex", snap.CurrentManifest.Keys);
        Assert.DoesNotContain(@"nativePC\x.tex", snap.Originals.Keys);
    }

    [Fact]
    public async Task Startup_recovery_handles_crash_after_journal_before_any_write()
    {
        var (game, db, hashing, blobs) = await CreateAsync("crash-before-write");
        var live = Path.Combine(game, "nativePC", "x.tex");
        await File.WriteAllTextAsync(live, "ORIGINAL", TestToken);
        var modHash = await BlobAsync(blobs, "before-mod", "MOD");
        var plan = new DeploymentPlan("before-op", DateTimeOffset.UtcNow,
            [new(1, ChangeKind.Add, @"nativePC\x.tex", null, modHash, null, "m", null, null)], [], []);

        var crashing = new DeploymentExecutor(db, blobs, hashing, game, (stage, _) =>
        {
            if (stage == "after-journal") throw new SimulatedCrashException("boom");
        });
        await Assert.ThrowsAsync<SimulatedCrashException>(() => crashing.ApplyAsync(plan, "crash fixture", ct: TestToken));
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(live, TestToken));

        var recovered = new DeploymentExecutor(db, blobs, hashing, game);
        await recovered.RecoverIncompleteAsync(TestToken);
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(live, TestToken));
        var snap = await db.LoadPlannerSnapshotAsync(TestToken);
        Assert.DoesNotContain(@"nativePC\x.tex", snap.Originals.Keys);
    }

    [Fact]
    public async Task Recovery_refuses_to_destroy_external_change()
    {
        var (game, db, hashing, blobs) = await CreateAsync("external-change");
        var live = Path.Combine(game, "nativePC", "x.tex");
        await File.WriteAllTextAsync(live, "ORIGINAL", TestToken);
        var modHash = await BlobAsync(blobs, "external-mod", "MOD");
        var plan = new DeploymentPlan("external-op", DateTimeOffset.UtcNow,
            [new(1, ChangeKind.Add, @"nativePC\x.tex", null, modHash, null, "m", null, null)], [], []);

        var crashing = new DeploymentExecutor(db, blobs, hashing, game, (stage, seq) =>
        {
            if (stage == "after-file-write" && seq == 1) throw new SimulatedCrashException("boom");
        });
        await Assert.ThrowsAsync<SimulatedCrashException>(() => crashing.ApplyAsync(plan, "crash fixture", ct: TestToken));
        await File.WriteAllTextAsync(live, "USER-EDIT-AFTER-CRASH", TestToken);

        var recovered = new DeploymentExecutor(db, blobs, hashing, game);
        var ex = await Assert.ThrowsAsync<IOException>(() => recovered.RecoverIncompleteAsync(TestToken));
        Assert.Contains("matches neither", ex.Message);
        var inner = Assert.IsType<IOException>(ex.InnerException);
        Assert.Contains("changed externally", inner.Message);
        Assert.Equal("USER-EDIT-AFTER-CRASH", await File.ReadAllTextAsync(live, TestToken));
    }

    [Fact]
    public async Task Empty_directory_pruning_never_deletes_nativepc_or_game_root()
    {
        var (game, db, hashing, blobs) = await CreateAsync("prune");
        var modHash = await BlobAsync(blobs, "prune-mod", "MOD");
        var executor = new DeploymentExecutor(db, blobs, hashing, game);
        var path = @"nativePC\nested\deeper\x.tex";
        var add = new DeploymentPlan("p-add", DateTimeOffset.UtcNow,
            [new(1, ChangeKind.Add, path, null, modHash, null, "m", null, null)], [], []);
        Assert.True((await executor.ApplyAsync(add, "add", ct: TestToken)).Success);
        var remove = new DeploymentPlan("p-remove", DateTimeOffset.UtcNow,
            [new(1, ChangeKind.Remove, path, modHash, null, "m", null, modHash, null)], [], []);
        Assert.True((await executor.ApplyAsync(remove, "remove", ct: TestToken)).Success);
        Assert.True(Directory.Exists(game));
        Assert.True(Directory.Exists(Path.Combine(game, "nativePC")));
        Assert.False(Directory.Exists(Path.Combine(game, "nativePC", "nested")));
    }

    [Fact]
    public void Archive_path_normalization_rejects_traversal() =>
        Assert.Throws<ArgumentException>(() => PathRules.Normalize(@"nativePC\..\evil.dll"));
}
