using Microsoft.Data.Sqlite;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class ProfileRepositoryAtomicityTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "MhwProfileAtomicityTests-" + Guid.NewGuid().ToString("N"));

    public ProfileRepositoryAtomicityTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task SaveCurrentRollsBackExistingProfileWhenReplacementInsertFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(root, "profiles.db"));
        await db.InitializeAsync(ct);
        var repository = new ProfileRepository(db);

        await db.UpsertModAsync(Mod("a", enabled: true, priority: 10), ct);
        await repository.SaveCurrentAsync("Stable", ct);

        var beforeSummary = Assert.Single(await repository.ListAsync(ct));
        var beforeState = await repository.LoadAsync(beforeSummary.Id, ct);
        Assert.Equal((true, 10), beforeState["a"]);

        await db.UpsertModAsync(Mod("a", enabled: false, priority: 99), ct);
        await db.UpsertModAsync(Mod("b", enabled: true, priority: 20), ct);
        await db.ExecuteAsync(
            """
            CREATE TRIGGER fail_profile_b
            BEFORE INSERT ON profile_mods
            WHEN NEW.mod_id = 'b'
            BEGIN
                SELECT RAISE(ABORT, 'injected profile replacement failure');
            END;
            """,
            new Dictionary<string, object?>(),
            ct);

        await Assert.ThrowsAsync<SqliteException>(
            () => repository.SaveCurrentAsync("Stable", ct));

        var afterSummary = Assert.Single(await repository.ListAsync(ct));
        var afterState = await repository.LoadAsync(afterSummary.Id, ct);

        Assert.Equal(beforeSummary.Id, afterSummary.Id);
        Assert.Equal(beforeSummary.UpdatedAt, afterSummary.UpdatedAt);
        Assert.Single(afterState);
        Assert.Equal((true, 10), afterState["a"]);
        Assert.DoesNotContain("b", afterState.Keys);
    }

    private ModDescriptor Mod(string id, bool enabled, int priority) =>
        new(id, id.ToUpperInvariant(), id.ToUpperInvariant(), Path.Combine(root, id), enabled, priority);

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        GC.SuppressFinalize(this);
    }
}
