using Microsoft.Data.Sqlite;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ManagerDatabaseMigrationTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "mhwmm-db-migration-" + Guid.NewGuid().ToString("N"));

    public ManagerDatabaseMigrationTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task InitializeAsyncSerializesConcurrentFreshDatabaseMigrations()
    {
        var ct = TestContext.Current.CancellationToken;
        var databasePath = Path.Combine(root, "concurrent.db");
        var initializers = Enumerable.Range(0, 8)
            .Select(_ => new ManagerDatabase(databasePath).InitializeAsync(ct))
            .ToArray();

        await Task.WhenAll(initializers);

        Assert.Equal("6", await ReadSchemaVersionAsync(databasePath, ct));
        Assert.Equal(
            "ok",
            await new ManagerDatabase(databasePath).IntegrityCheckAsync(ct));
    }

    [Fact]
    public async Task InitializeAsyncRollsBackFreshSchemaWhenFailureOccursBeforeCommit()
    {
        var ct = TestContext.Current.CancellationToken;
        var databasePath = Path.Combine(root, "fresh-rollback.db");
        var failing = new ManagerDatabase(
            databasePath,
            (checkpoint, _) =>
            {
                if (checkpoint == ManagerDatabaseMigrationCheckpoint.BeforeCommit)
                    throw new InvalidOperationException("injected migration failure");
                return ValueTask.CompletedTask;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => failing.InitializeAsync(ct));

        Assert.False(await TableExistsAsync(databasePath, "schema_info", ct));

        var recovered = new ManagerDatabase(databasePath);
        await recovered.InitializeAsync(ct);

        Assert.Equal("6", await ReadSchemaVersionAsync(databasePath, ct));
        Assert.Equal("ok", await recovered.IntegrityCheckAsync(ct));
    }

    [Fact]
    public async Task InitializeAsyncRollsBackArmorRebuildAndVersionTogether()
    {
        var ct = TestContext.Current.CancellationToken;
        var databasePath = Path.Combine(root, "legacy-rollback.db");
        const string armorPath = @"nativePC\pl001_0000\helm\model.mod3";
        await SeedLegacyVersionOneAsync(databasePath, armorPath, ct);

        var failing = new ManagerDatabase(
            databasePath,
            (checkpoint, _) =>
            {
                if (checkpoint == ManagerDatabaseMigrationCheckpoint.BeforeCommit)
                    throw new InvalidOperationException("injected migration failure");
                return ValueTask.CompletedTask;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => failing.InitializeAsync(ct));

        Assert.Equal("1", await ReadSchemaVersionAsync(databasePath, ct));
        Assert.Equal(
            ("stale-model", "stale-component"),
            await ReadArmorIdentityAsync(databasePath, armorPath, ct));

        var recovered = new ManagerDatabase(databasePath);
        await recovered.InitializeAsync(ct);

        Assert.Equal("6", await ReadSchemaVersionAsync(databasePath, ct));
        Assert.Equal(
            ("pl001_0000", "head"),
            await ReadArmorIdentityAsync(databasePath, armorPath, ct));
        Assert.Equal("ok", await recovered.IntegrityCheckAsync(ct));
    }

    [Fact]
    public async Task InitializeAsyncCancellationAfterArmorRebuildRollsBackMigration()
    {
        var databasePath = Path.Combine(root, "legacy-cancel.db");
        const string armorPath = @"nativePC\pl002_0000\body\model.mod3";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await SeedLegacyVersionOneAsync(databasePath, armorPath, cts.Token);

        var failing = new ManagerDatabase(
            databasePath,
            (checkpoint, token) =>
            {
                if (checkpoint == ManagerDatabaseMigrationCheckpoint.AfterArmorIndexRebuild)
                {
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                }

                return ValueTask.CompletedTask;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => failing.InitializeAsync(cts.Token));

        Assert.Equal(
            "1",
            await ReadSchemaVersionAsync(
                databasePath,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            ("stale-model", "stale-component"),
            await ReadArmorIdentityAsync(
                databasePath,
                armorPath,
                TestContext.Current.CancellationToken));
    }

    private static async Task SeedLegacyVersionOneAsync(
        string databasePath,
        string armorPath,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());
        await connection.OpenAsync(ct);

        await ExecuteAsync(connection, Schema.Sql, ct);
        await ExecuteAsync(
            connection,
            "INSERT INTO schema_info(key,value) VALUES('version','1');",
            ct);
        await ExecuteAsync(
            connection,
            """
            INSERT INTO mods(
                id,name,display_name,source_path,enabled,priority,
                imported_at,updated_at)
            VALUES(
                'legacy-mod','Legacy','Legacy','C:\\mods\\legacy',
                1,0,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
            """,
            ct);

        await using (var files = connection.CreateCommand())
        {
            files.CommandText = """
                INSERT INTO mod_files(
                    mod_id,path,blob_sha256,fast_hash,length,last_write_utc,file_class)
                VALUES(
                    'legacy-mod',$path,'deadbeef',NULL,1,
                    '2026-01-01T00:00:00Z','Structural');
                """;
            files.Parameters.AddWithValue("$path", armorPath);
            await files.ExecuteNonQueryAsync(ct);
        }

        await using var armor = connection.CreateCommand();
        armor.CommandText = """
            INSERT INTO mod_file_armor(mod_id,path,model_id,component)
            VALUES('legacy-mod',$path,'stale-model','stale-component');
            """;
        armor.Parameters.AddWithValue("$path", armorPath);
        await armor.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> ReadSchemaVersionAsync(
        string databasePath,
        CancellationToken ct)
    {
        await using var connection = await OpenReadWriteAsync(databasePath, ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT value FROM schema_info WHERE key='version';";
        return (string?)await command.ExecuteScalarAsync(ct) ?? string.Empty;
    }

    private static async Task<(string ModelId, string Component)> ReadArmorIdentityAsync(
        string databasePath,
        string armorPath,
        CancellationToken ct)
    {
        await using var connection = await OpenReadWriteAsync(databasePath, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT model_id,component
            FROM mod_file_armor
            WHERE mod_id='legacy-mod' AND path=$path;
            """;
        command.Parameters.AddWithValue("$path", armorPath);
        await using var reader = await command.ExecuteReaderAsync(ct);
        Assert.True(await reader.ReadAsync(ct));
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task<bool> TableExistsAsync(
        string databasePath,
        string tableName,
        CancellationToken ct)
    {
        await using var connection = await OpenReadWriteAsync(databasePath, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table' AND name=$name;
            """;
        command.Parameters.AddWithValue("$name", tableName);
        var count = Convert.ToInt64(
            await command.ExecuteScalarAsync(ct),
            System.Globalization.CultureInfo.InvariantCulture);
        return count != 0;
    }

    private static async Task<SqliteConnection> OpenReadWriteAsync(
        string databasePath,
        CancellationToken ct)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite
            }.ToString());
        await connection.OpenAsync(ct);
        return connection;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }
}
