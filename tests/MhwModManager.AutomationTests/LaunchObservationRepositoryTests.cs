using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class LaunchObservationRepositoryTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string root = Path.Combine(Path.GetTempPath(), "MhwLaunchObservationTests-" + Guid.NewGuid().ToString("N"));

    public LaunchObservationRepositoryTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task FailureBeforeFirstTrustDeltaRollsBackHistory()
    {
        var db = await CreateDbAsync("first-trust-failure.db");
        var repository = new LaunchObservationRepository(db);
        var observation = Observation("launch-first-failure", new Dictionary<string, ModState>(StringComparer.OrdinalIgnoreCase) { ["missing"] = new(true, 1) });

        await Assert.ThrowsAsync<SqliteException>(() => repository.PersistAsync(observation));

        Assert.Equal(0, await LaunchCountAsync(db, observation.Id));
    }

    [Fact]
    public async Task FailureAfterOneTrustDeltaRollsBackHistoryAndEarlierTrust()
    {
        var db = await CreateDbAsync("later-trust-failure.db");
        await SeedModAsync(db, "first", "First");
        await SeedModAsync(db, "second", "Second");
        await db.ExecuteAsync(
            """
            CREATE TRIGGER fail_second_trust
            BEFORE INSERT ON mod_trust
            WHEN NEW.mod_id='second'
            BEGIN
                SELECT RAISE(ABORT,'injected second trust write failure');
            END;
            """,
            ct:TestContext.Current.CancellationToken);

        var trust = new ModTrustService(db);
        var repository = new LaunchObservationRepository(db);
        var observation = Observation(
            "launch-later-failure",
            new Dictionary<string, ModState>(StringComparer.OrdinalIgnoreCase)
            {
                ["first"] = new(true, 1),
                ["second"] = new(true, 2)
            });

        await Assert.ThrowsAsync<SqliteException>(() => repository.PersistAsync(observation));

        Assert.Equal(0, await LaunchCountAsync(db, observation.Id));
        Assert.Null(await trust.GetAsync("first", TestContext.Current.CancellationToken));
        Assert.Null(await trust.GetAsync("second", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExactReplayIsIdempotentAndConflictingReplayFailsClosed()
    {
        var db = await CreateDbAsync("replay.db");
        await SeedModAsync(db, "a", "A");
        var trust = new ModTrustService(db);
        var repository = new LaunchObservationRepository(db);
        var observation = Observation("launch-replay", new Dictionary<string, ModState>(StringComparer.OrdinalIgnoreCase) { ["a"] = new(true, 1) });

        Assert.Equal(LaunchObservationPersistenceResult.Persisted, await repository.PersistAsync(observation));
        Assert.Equal(LaunchObservationPersistenceResult.AlreadyPersisted, await repository.PersistAsync(observation));

        var state = await trust.GetAsync("a", TestContext.Current.CancellationToken);
        Assert.NotNull(state);
        Assert.Equal(1, state!.SuccessfulLaunches);
        Assert.Equal(0, state.FailedLaunches);
        Assert.Equal(1, await LaunchCountAsync(db, observation.Id));

        var conflict = observation with { Success = false, StartupSurvived = false, ExitCode = 9 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.PersistAsync(conflict));

        state = await trust.GetAsync("a", TestContext.Current.CancellationToken);
        Assert.NotNull(state);
        Assert.Equal(1, state!.SuccessfulLaunches);
        Assert.Equal(0, state.FailedLaunches);
        Assert.True(await LaunchSuccessAsync(db, observation.Id));
    }

    [Fact]
    public async Task PersistedStateAndTrustUseTheSameImmutableSnapshot()
    {
        var db = await CreateDbAsync("snapshot.db");
        await SeedModAsync(db, "enabled", "Enabled");
        await SeedModAsync(db, "disabled", "Disabled");
        var trust = new ModTrustService(db);
        var repository = new LaunchObservationRepository(db);
        var expected = new Dictionary<string, ModState>(StringComparer.OrdinalIgnoreCase)
        {
            ["enabled"] = new(true, 7),
            ["disabled"] = new(false, 9)
        };

        await repository.PersistAsync(Observation("launch-snapshot", expected));

        var persisted = await LaunchStateAsync(db, "launch-snapshot");
        Assert.Equal(expected.Count, persisted.Count);
        foreach (var (id, modState) in expected) Assert.Equal(modState, persisted[id]);

        var enabledTrust = await trust.GetAsync("enabled", TestContext.Current.CancellationToken);
        Assert.NotNull(enabledTrust);
        Assert.Equal(1, enabledTrust!.SuccessfulLaunches);
        Assert.Null(await trust.GetAsync("disabled", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedLaunchDiagnosisResolvesTheExactPersistedLaunchId()
    {
        var db = await CreateDbAsync("diagnosis.db");
        await SeedModAsync(db, "base", "Base");
        await SeedModAsync(db, "plugin", "Plugin");
        await db.ReplaceModFilesAsync(
            "plugin",
            [new("plugin", @"nativePC\plugins\thing.dll", "p", null, 1, DateTimeOffset.UtcNow, FileClass.Plugin)],
            TestContext.Current.CancellationToken);

        var trust = new ModTrustService(db);
        var repository = new LaunchObservationRepository(db);
        await repository.PersistAsync(Observation(
            "good-launch",
            new Dictionary<string, ModState>(StringComparer.OrdinalIgnoreCase)
            {
                ["base"] = new(true, 1),
                ["plugin"] = new(false, 2)
            },
            success: true,
            startedAt: DateTimeOffset.Parse("2026-09-28T16:00:00Z", CultureInfo.InvariantCulture)));
        await repository.PersistAsync(Observation(
            "bad-launch",
            new Dictionary<string, ModState>(StringComparer.OrdinalIgnoreCase)
            {
                ["base"] = new(true, 1),
                ["plugin"] = new(true, 2)
            },
            success: false,
            startedAt: DateTimeOffset.Parse("2026-09-28T16:10:00Z", CultureInfo.InvariantCulture)));

        var issues = new ModIssueFallbackService(db, new ChangeTimelineService(db), trust);
        var result = await issues.RecordLaunchFailureAsync("bad-launch", ModIssueKind.StartupCrash, TestContext.Current.CancellationToken);

        Assert.Equal("bad-launch", result.LaunchId);
        Assert.Equal("plugin", Assert.Single(result.Suspects).ModId);
    }

    [Fact]
    public async Task AuthoritativePersistenceDoesNotAcceptUserCancellation()
    {
        var db = await CreateDbAsync("cancellation.db");
        await SeedModAsync(db, "a", "A");
        using var userCancellation = new CancellationTokenSource();
        userCancellation.Cancel();

        var repository = new LaunchObservationRepository(db);
        var observation = Observation("launch-after-user-cancel", new Dictionary<string, ModState>(StringComparer.OrdinalIgnoreCase) { ["a"] = new(true, 1) });

        Assert.True(userCancellation.IsCancellationRequested);
        Assert.Equal(LaunchObservationPersistenceResult.Persisted, await repository.PersistAsync(observation));
        Assert.Equal(1, await LaunchCountAsync(db, observation.Id));
    }

    private static LaunchObservationRecord Observation(string id, IReadOnlyDictionary<string, ModState> state, bool success = true, DateTimeOffset? startedAt = null)
    {
        var started = startedAt ?? DateTimeOffset.Parse("2026-09-28T17:00:00Z", CultureInfo.InvariantCulture);
        return new(id, started, started.AddSeconds(15), LaunchMode.Modded, "build-sha", success, success ? null : 7, success, state, "Observed for 15.0s");
    }

    private async Task<ManagerDatabase> CreateDbAsync(string name)
    {
        var db = new ManagerDatabase(Path.Combine(root, name));
        await db.InitializeAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private async Task SeedModAsync(ManagerDatabase db, string id, string name)
        => await db.UpsertModAsync(new(id, name, name, Path.Combine(root, id), true, 1), TestContext.Current.CancellationToken);

    private static async Task<int> LaunchCountAsync(ManagerDatabase db, string id)
    {
        await using var connection = await db.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM launch_history WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> LaunchSuccessAsync(ManagerDatabase db, string id)
    {
        await using var connection = await db.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT success FROM launch_history WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture) != 0;
    }

    private static async Task<Dictionary<string, ModState>> LaunchStateAsync(ManagerDatabase db, string id)
    {
        await using var connection = await db.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT state_json FROM launch_history WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        var json = Assert.IsType<string>(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        var parsed = JsonSerializer.Deserialize<Dictionary<string, ModState>>(json, JsonOptions);
        Assert.NotNull(parsed);
        return new Dictionary<string, ModState>(parsed!, StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
