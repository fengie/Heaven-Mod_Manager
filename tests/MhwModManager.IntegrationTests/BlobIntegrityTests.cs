using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using System.Security.Cryptography;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class BlobIntegrityTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-cas-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    private async Task<(ManagerDatabase db, BlobStore blobs, string source, string hash)> CreateAsync()
    {
        Directory.CreateDirectory(root);
        var db = new ManagerDatabase(Path.Combine(root, "state", "m.db"));
        await db.InitializeAsync(TestToken);
        var blobs = new BlobStore(Path.Combine(root, "state", "blobs"), db);
        var source = Path.Combine(root, "source");
        await File.WriteAllTextAsync(source, "CORRECT", TestToken);
        var hash = (await blobs.CaptureWithHashAsync(source, registerInDatabase: false, ct: TestToken)).Sha256;
        return (db, blobs, source, hash);
    }

    private static async Task<string> ScalarAsync(ManagerDatabase db, string sql)
    {
        await using var connection = await db.OpenAsync(TestToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(TestToken), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_rejects_corrupt_existing_object_without_registering_it(bool alreadyRegistered)
    {
        var (db, blobs, source, hash) = await CreateAsync();
        if (alreadyRegistered) await blobs.CaptureAsync(source, TestToken);
        var before = await ScalarAsync(db, "SELECT COALESCE(MAX(verified_at),'missing') FROM blobs");
        await File.WriteAllTextAsync(blobs.PathFor(hash), "CORRUPT", TestToken); // Same length as original.

        await Assert.ThrowsAsync<InvalidDataException>(() => blobs.CaptureAsync(source, TestToken));

        Assert.Equal(before, await ScalarAsync(db, "SELECT COALESCE(MAX(verified_at),'missing') FROM blobs"));
        Assert.Equal("CORRUPT", await File.ReadAllTextAsync(blobs.PathFor(hash), TestToken));
        Assert.Equal("CORRECT", await File.ReadAllTextAsync(source, TestToken));
        Assert.Empty(Directory.EnumerateFiles(blobs.Root, ".capture-*.tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_rejects_corrupt_object_before_publishing_destination(bool existing)
    {
        var (_, blobs, _, hash) = await CreateAsync();
        var destination = Path.Combine(root, "destination");
        if (existing) await File.WriteAllTextAsync(destination, "LIVE", TestToken);
        await File.WriteAllTextAsync(blobs.PathFor(hash), "CORRUPT", TestToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => blobs.RestoreAsync(hash, destination, TestToken));

        if (existing) Assert.Equal("LIVE", await File.ReadAllTextAsync(destination, TestToken));
        else Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFiles(root, "*.mhwmm.tmp"));
    }

    [Fact]
    public async Task Concurrent_valid_captures_converge_and_restore_the_expected_bytes()
    {
        var (db, blobs, source, hash) = await CreateAsync();
        var casPath = blobs.PathFor(hash);

        for (var repetition = 0; repetition < 6; repetition++)
        {
            File.Delete(casPath);
            var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => blobs.CaptureAsync(source, TestToken)));

            Assert.All(results, result => Assert.Equal(hash, result));
            Assert.Equal("1", await ScalarAsync(db, "SELECT COUNT(*) FROM blobs"));
            Assert.Equal([hash], Directory.EnumerateFiles(blobs.Root).Select(Path.GetFileName).ToArray());
            await using var cas = new FileStream(casPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Equal(hash, Convert.ToHexString(await SHA256.HashDataAsync(cas, TestToken)).ToLowerInvariant());
            Assert.Empty(Directory.EnumerateFiles(blobs.Root, ".capture-*.tmp"));
        }

        var destination = Path.Combine(root, "destination");
        await blobs.RestoreAsync(hash.ToUpperInvariant(), destination, TestToken);
        Assert.Equal("CORRECT", await File.ReadAllTextAsync(destination, TestToken));
    }

    [Fact]
    public async Task Canceled_capture_does_not_publish_or_leave_private_staging()
    {
        var (_, blobs, source, hash) = await CreateAsync();
        File.Delete(blobs.PathFor(hash));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blobs.CaptureAsync(source, canceled.Token));

        Assert.False(File.Exists(blobs.PathFor(hash)));
        Assert.Empty(Directory.EnumerateFiles(blobs.Root, ".capture-*.tmp"));
    }

    [Fact]
    public async Task Deployment_corrupt_after_blob_rolls_back_without_committing()
    {
        var (db, blobs, _, hash) = await CreateAsync();
        var game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(game, "nativePC"));
        var live = Path.Combine(game, "nativePC", "x.tex");
        await File.WriteAllTextAsync(live, "ORIGINAL", TestToken);
        await File.WriteAllTextAsync(blobs.PathFor(hash), "CORRUPT", TestToken);
        var plan = new DeploymentPlan("corrupt-after", DateTimeOffset.UtcNow,
            [new(1, ChangeKind.Add, @"nativePC\x.tex", null, hash, null, "m", null, null)], [], []);

        var result = await new DeploymentExecutor(db, blobs, new HashingService(), game).ApplyAsync(plan, "corrupt CAS", ct: TestToken);

        Assert.False(result.Success);
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(live, TestToken));
        Assert.Equal("RolledBack", await ScalarAsync(db, "SELECT state FROM operations WHERE id='corrupt-after'"));
        Assert.Equal("RolledBack", await ScalarAsync(db, "SELECT status FROM operation_journal WHERE operation_id='corrupt-after'"));
        Assert.Equal("0", await ScalarAsync(db, "SELECT COUNT(*) FROM deployment_manifest"));
        Assert.Equal("0", await ScalarAsync(db, "SELECT COUNT(*) FROM original_files"));
    }

    [Fact]
    public async Task Recovery_corrupt_before_blob_preserves_live_after_image_and_requires_recovery()
    {
        var (db, blobs, _, hash) = await CreateAsync();
        var game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(game, "nativePC"));
        var live = Path.Combine(game, "nativePC", "x.tex");
        await File.WriteAllTextAsync(live, "ORIGINAL", TestToken);
        var beforeHash = await blobs.CaptureAsync(live, TestToken);
        var plan = new DeploymentPlan("corrupt-before", DateTimeOffset.UtcNow,
            [new(1, ChangeKind.Add, @"nativePC\x.tex", null, hash, null, "m", null, null)], [], []);
        var crashing = new DeploymentExecutor(db, blobs, new HashingService(), game, (stage, _) =>
        {
            if (stage == "after-file-write") throw new SimulatedCrashException("before journal status update");
        });
        await Assert.ThrowsAsync<SimulatedCrashException>(() => crashing.ApplyAsync(plan, "crash", ct: TestToken));
        await File.WriteAllTextAsync(blobs.PathFor(beforeHash), "CORRUPT!", TestToken);

        var recovery = new DeploymentExecutor(db, blobs, new HashingService(), game);
        await Assert.ThrowsAsync<IOException>(() => recovery.RecoverIncompleteAsync(TestToken));

        Assert.Equal("CORRECT", await File.ReadAllTextAsync(live, TestToken));
        Assert.Equal("RecoveryRequired", await ScalarAsync(db, "SELECT state FROM operations WHERE id='corrupt-before'"));
        Assert.Equal("Writing", await ScalarAsync(db, "SELECT status FROM operation_journal WHERE operation_id='corrupt-before'"));
        Assert.Equal("0", await ScalarAsync(db, "SELECT COUNT(*) FROM deployment_manifest"));
        Assert.Equal(beforeHash, await ScalarAsync(db, "SELECT blob_sha256 FROM original_files"));

        // Restoring independently known-good recovery material permits a later retry.
        await File.WriteAllTextAsync(blobs.PathFor(beforeHash), "ORIGINAL", TestToken);
        await recovery.RecoverIncompleteAsync(TestToken);
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(live, TestToken));
        Assert.Equal("RolledBack", await ScalarAsync(db, "SELECT state FROM operations WHERE id='corrupt-before'"));
        Assert.Equal("RolledBack", await ScalarAsync(db, "SELECT status FROM operation_journal WHERE operation_id='corrupt-before'"));
        Assert.Equal("0", await ScalarAsync(db, "SELECT COUNT(*) FROM original_files"));
    }

    [Fact]
    public async Task Later_corrupt_blob_rolls_back_an_earlier_successful_write()
    {
        var (db, blobs, _, hash) = await CreateAsync();
        var game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(game, "nativePC"));
        var first = Path.Combine(game, "nativePC", "first.tex");
        var second = Path.Combine(game, "nativePC", "second.tex");
        await File.WriteAllTextAsync(first, "FIRST ORIGINAL", TestToken);
        await File.WriteAllTextAsync(second, "SECOND ORIGINAL", TestToken);
        var badSource = Path.Combine(root, "second-source");
        await File.WriteAllTextAsync(badSource, "SECOND MOD", TestToken);
        var badHash = await blobs.CaptureAsync(badSource, TestToken);
        await File.WriteAllTextAsync(blobs.PathFor(badHash), "CORRUPTION", TestToken);
        var plan = new DeploymentPlan("multi", DateTimeOffset.UtcNow,
            [new(1, ChangeKind.Add, @"nativePC\first.tex", null, hash, null, "m", null, null),
             new(2, ChangeKind.Add, @"nativePC\second.tex", null, badHash, null, "m", null, null)], [], []);
        var sawFirstWrite = false;
        var executor = new DeploymentExecutor(db, blobs, new HashingService(), game, (stage, sequence) =>
        {
            if (stage == "after-file-write" && sequence == 1) sawFirstWrite = true;
        });

        var result = await executor.ApplyAsync(plan, "multi-file corruption", ct: TestToken);

        Assert.True(sawFirstWrite);
        Assert.False(result.Success);
        Assert.True(result.RollbackCompleted);
        Assert.Equal(FailureCategory.DataIntegrityFailure, result.FailureCategory);
        Assert.Equal("FIRST ORIGINAL", await File.ReadAllTextAsync(first, TestToken));
        Assert.Equal("SECOND ORIGINAL", await File.ReadAllTextAsync(second, TestToken));
        Assert.Equal("RolledBack", await ScalarAsync(db, "SELECT state FROM operations WHERE id='multi'"));
        Assert.Equal("2", await ScalarAsync(db, "SELECT COUNT(*) FROM operation_journal WHERE status='RolledBack'"));
        Assert.Equal("0", await ScalarAsync(db, "SELECT COUNT(*) FROM deployment_manifest"));
        Assert.Equal("0", await ScalarAsync(db, "SELECT COUNT(*) FROM original_files"));
    }

    [Fact]
    public async Task Canceled_restore_does_not_replace_live_bytes_or_leave_staging()
    {
        var (_, blobs, _, hash) = await CreateAsync();
        var destination = Path.Combine(root, "destination");
        await File.WriteAllTextAsync(destination, "LIVE", TestToken);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blobs.RestoreAsync(hash, destination, canceled.Token));
        Assert.Equal("LIVE", await File.ReadAllTextAsync(destination, TestToken));
        Assert.Empty(Directory.EnumerateFiles(root, "*.mhwmm.tmp"));
    }
}
