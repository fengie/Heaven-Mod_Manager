using System.Globalization;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;

namespace MhwModManager.Storage;

internal enum ManagerDatabaseMigrationCheckpoint
{
    AfterVersionRead,
    AfterArmorIndexRebuild,
    BeforeVersionWrite,
    BeforeCommit
}

/// <summary>
/// Connection-per-operation SQLite facade. WAL is intentionally used without SQLite shared-cache;
/// shared-cache changes locking semantics and works against the WAL concurrency model.
/// </summary>
public sealed record ModCatalogSourceIdentity(string Id, string Name, string SourcePath, string? NexusModId, string? SourceUrl)
{
    public bool CanInspectCollection { get; init; }
    public bool HasSavedReferences { get; init; }
}
public sealed record ModArchiveDisplayBackfill(string Id, string Name, string SourcePath, string DisplayName);

public sealed class ManagerDatabase
{
    private const int BusyTimeoutSeconds = 10;
    private readonly Func<ManagerDatabaseMigrationCheckpoint, CancellationToken, ValueTask>? migrationCheckpoint;
    private readonly string connectionString;

    public ManagerDatabase(string databasePath)
        : this(databasePath, migrationCheckpoint: null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
    }

    internal ManagerDatabase(
        string databasePath,
        Func<ManagerDatabaseMigrationCheckpoint, CancellationToken, ValueTask>? migrationCheckpoint)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        DatabasePath = databasePath;
        this.migrationCheckpoint = migrationCheckpoint;
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
            DefaultTimeout = BusyTimeoutSeconds
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await using var c = await OpenRawAsync(ct);
        await ExecAsync(c, "PRAGMA journal_mode=WAL;", ct);
        await ExecAsync(c, "PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000; PRAGMA wal_autocheckpoint=1000; PRAGMA journal_size_limit=67108864;", ct);

        await using var tx = c.BeginTransaction(deferred: false);
        try
        {
            await ExecAsync(c, Schema.Sql, ct, tx);

            var version = await GetSchemaVersionAsync(c, tx, ct);
            await ReachMigrationCheckpointAsync(
                ManagerDatabaseMigrationCheckpoint.AfterVersionRead,
                ct);

            if (version < 2)
            {
                await RebuildArmorIndexAsync(c, tx, ct);
                await ReachMigrationCheckpointAsync(
                    ManagerDatabaseMigrationCheckpoint.AfterArmorIndexRebuild,
                    ct);
            }

            if (version < 6)
            {
                await ReachMigrationCheckpointAsync(
                    ManagerDatabaseMigrationCheckpoint.BeforeVersionWrite,
                    ct);
                await SetSchemaVersionAsync(c, tx, 6, ct);
            }
            else
            {
                await ExecAsync(
                    c,
                    "INSERT OR IGNORE INTO schema_info(key,value) VALUES('version','6');",
                    ct,
                    tx);
            }

            await ReachMigrationCheckpointAsync(
                ManagerDatabaseMigrationCheckpoint.BeforeCommit,
                ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            MasterDebugLog.Write(
                "DB-MIGRATION",
                "Rolling back schema migration after initialization failure.",
                ex);
            try
            {
                await tx.RollbackAsync(CancellationToken.None);
                MasterDebugLog.Write(
                    "DB-MIGRATION",
                    "Schema migration rollback completed.");
            }
            catch (Exception rollbackEx)
            {
                MasterDebugLog.Write(
                    "DB-MIGRATION",
                    "Schema migration rollback itself failed; preserving the original initialization failure.",
                    rollbackEx);
            }

            throw;
        }
    }

    private ValueTask ReachMigrationCheckpointAsync(
        ManagerDatabaseMigrationCheckpoint checkpoint,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"checkpoint={checkpoint}");
        return migrationCheckpoint?.Invoke(checkpoint, ct) ?? ValueTask.CompletedTask;
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var c = await OpenRawAsync(ct);
        try
        {
            await ExecAsync(c, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;", ct);
            return c;
        }
        catch
        {
            await c.DisposeAsync();
            throw;
        }
    }

    private async Task<SqliteConnection> OpenRawAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var c = new SqliteConnection(connectionString);
        await c.OpenAsync(ct);
        return c;
    }

    private static async Task ExecAsync(SqliteConnection c, string sql, CancellationToken ct, SqliteTransaction? tx = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> GetSchemaVersionAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT value FROM schema_info WHERE key='version'";
        var value = await cmd.ExecuteScalarAsync(ct) as string;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static async Task SetSchemaVersionAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        int version,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO schema_info(key,value) VALUES('version',$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("$v", version.ToString(CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task RebuildArmorIndexAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await ExecAsync(c, "DELETE FROM mod_file_armor;", ct, tx);

        var rows = new List<(string modId, string path)>();
        await using (var read = c.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT mod_id,path FROM mod_files";
            await using var r = await read.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) rows.Add((r.GetString(0), r.GetString(1)));
        }

        await using var insert = c.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT OR IGNORE INTO mod_file_armor(mod_id,path,model_id,component) VALUES($m,$p,$model,$component)";
        var pMod = insert.Parameters.Add("$m", SqliteType.Text);
        var pPath = insert.Parameters.Add("$p", SqliteType.Text);
        var pModel = insert.Parameters.Add("$model", SqliteType.Text);
        var pComponent = insert.Parameters.Add("$component", SqliteType.Text);
        insert.Prepare();

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (!PathRules.TryGetArmorComponent(row.path, out var modelId, out var component)) continue;
            pMod.Value = row.modId;
            pPath.Value = row.path;
            pModel.Value = modelId;
            pComponent.Value = component;
            await insert.ExecuteNonQueryAsync(ct);
        }

    }

    public async Task RebuildArmorIndexAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var tx = c.BeginTransaction(deferred: false);
        await RebuildArmorIndexAsync(c, tx, ct);
        await tx.CommitAsync(ct);
    }

    private string[] pendingModIds = [];
    public void SetPendingModIds(IEnumerable<string> ids)
    {
        using var trace = MasterDebugLog.BeginMethod();
        Volatile.Write(ref pendingModIds,ids.ToArray());
    }

    public async Task ReconcileExactCopiesAsync(IReadOnlyList<string[]> groups,CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        await using var connection=await OpenAsync(ct);
        using var tx=connection.BeginTransaction(deferred:false);
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText="SELECT mod_id,keeper_id FROM exact_package_copies";
        var previous=new Dictionary<string,string>(StringComparer.Ordinal);
        await using(var reader=await command.ExecuteReaderAsync(ct))
            while(await reader.ReadAsync(ct)) previous.Add(reader.GetString(0),reader.GetString(1));
        var oldKeepers=previous.Values.ToHashSet(StringComparer.Ordinal);
        var pending=Volatile.Read(ref pendingModIds).ToHashSet(StringComparer.Ordinal);
        command.CommandText="SELECT m.id,m.enabled,m.priority,("+ProtectedCopySql+") FROM mods m";
        var states=new Dictionary<string,(bool Enabled,int Priority,bool Protected)>(StringComparer.Ordinal);
        await using(var reader=await command.ExecuteReaderAsync(ct))
            while(await reader.ReadAsync(ct))
                states[reader.GetString(0)]=(reader.GetBoolean(1),reader.GetInt32(2),reader.GetBoolean(3)||pending.Contains(reader.GetString(0)));
        var copies=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var group in groups)
        {
            var ordered=group.Where(states.ContainsKey).OrderByDescending(id=>states[id].Enabled)
                .ThenByDescending(id=>states[id].Protected).ThenByDescending(oldKeepers.Contains)
                .ThenBy(id=>states[id].Priority).ThenBy(id=>id,StringComparer.Ordinal).ToArray();
            foreach(var id in ordered.Skip(1))
                if(!states[id].Enabled && !states[id].Protected) copies[id]=ordered[0];
        }
        foreach(var id in previous.Keys.Except(copies.Keys))
        {
            command.Parameters.Clear();command.CommandText="DELETE FROM exact_package_copies WHERE mod_id=$id";
            command.Parameters.AddWithValue("$id",id);await command.ExecuteNonQueryAsync(ct);
        }
        foreach(var (id,keeper) in copies.Where(pair=>previous.GetValueOrDefault(pair.Key)!=pair.Value))
        {
            command.Parameters.Clear();command.CommandText="INSERT INTO exact_package_copies(mod_id,keeper_id) VALUES($id,$keeper) ON CONFLICT(mod_id) DO UPDATE SET keeper_id=excluded.keeper_id";
            command.Parameters.AddWithValue("$id",id);command.Parameters.AddWithValue("$keeper",keeper);await command.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private const string ProtectedCopySql = """
        m.family_id IS NOT NULL
        OR EXISTS(SELECT 1 FROM profile_mods WHERE mod_id=m.id)
        OR EXISTS(SELECT 1 FROM deployment_manifest WHERE provider_mod_id=m.id)
        OR EXISTS(SELECT 1 FROM conflict_rules WHERE m.id IN (left_mod_id,right_mod_id,winner_mod_id))
        OR EXISTS(SELECT 1 FROM resource_providers WHERE mod_id=m.id)
        OR EXISTS(SELECT 1 FROM mod_family_members WHERE mod_id=m.id)
        OR EXISTS(SELECT 1 FROM family_preferences WHERE selected_mod_id=m.id)
        """;

    public Task<IReadOnlyList<ModDescriptor>> GetModsAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return GetModsAsync(false, ct);
    }

    public Task<IReadOnlyList<ModDescriptor>> GetModsAsync(bool includeShelfSources, CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        return GetModsAsync(includeShelfSources,includeShelfSources,ct);
    }

    public async Task<IReadOnlyList<ModDescriptor>> GetModsAsync(bool includeShelfSources, bool includeExactCopies, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var list = new List<ModDescriptor>();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        SELECT m.id,m.name,m.display_name,m.source_path,m.enabled,m.priority,m.family_id,CASE WHEN shelf.role='review' THEN 'Needs layout review' ELSE m.category END,m.source_url,m.nexus_mod_id,m.nexus_file_id,
               p.nexus_mod_uuid,p.nexus_version_id,p.nexus_previous_version_id,p.nexus_category,p.nexus_version,p.uploaded_at,
               CASE WHEN s.older_mod_id IS NULL THEN 0 ELSE 1 END,s.newer_mod_id,p.confidence_score,p.provenance_source,
               (SELECT value FROM settings WHERE key='preview:'||m.id),
               (shelf.role IS 'review' OR COALESCE((SELECT required FROM mod_revalidation rv WHERE rv.mod_id=m.id),0)),
               fm.role,shelf.role IS 'review'
        FROM mods m
        LEFT JOIN managed_shelf_entries shelf ON shelf.mod_id=m.id
        LEFT JOIN mod_provenance p ON p.mod_id=m.id
        LEFT JOIN mod_supersession s ON s.older_mod_id=m.id
        LEFT JOIN mod_family_members fm ON fm.mod_id=m.id AND fm.family_id=m.family_id
        WHERE ($all OR m.enabled OR shelf.role IS NULL OR shelf.role IN ('package','review')
            OR (shelf.role='duplicate' AND (
                NOT EXISTS(SELECT 1 FROM mods canonical WHERE canonical.id=shelf.canonical_id AND canonical.enabled)
                OR m.id IN (SELECT value FROM json_each($pending)) OR (
        """ + ProtectedCopySql + """
        ))))
        AND ($copies OR m.enabled OR NOT EXISTS(SELECT 1 FROM exact_package_copies d WHERE d.mod_id=m.id)
            OR m.id IN (SELECT value FROM json_each($pending)) OR (
        """ + ProtectedCopySql + """
        ))
        ORDER BY m.priority,m.name COLLATE NOCASE
        """;
        cmd.Parameters.AddWithValue("$all", includeShelfSources);
        cmd.Parameters.AddWithValue("$copies", includeExactCopies);
        cmd.Parameters.AddWithValue("$pending",System.Text.Json.JsonSerializer.Serialize(Volatile.Read(ref pendingModIds)));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4) != 0, r.GetInt32(5),
                r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10),
                r.IsDBNull(11) ? null : r.GetString(11), r.IsDBNull(12) ? null : r.GetString(12), r.IsDBNull(13) ? null : r.GetString(13),
                r.IsDBNull(14) ? NexusFileCategory.Unknown : Enum.Parse<NexusFileCategory>(r.GetString(14), true),
                r.IsDBNull(15) ? null : r.GetString(15), r.IsDBNull(16) ? null : DateTimeOffset.Parse(r.GetString(16), CultureInfo.InvariantCulture),
                r.GetInt64(17) != 0, r.IsDBNull(18) ? null : r.GetString(18), r.IsDBNull(19) ? 0 : r.GetInt32(19),
                r.IsDBNull(20) ? ProvenanceSource.Unknown : Enum.Parse<ProvenanceSource>(r.GetString(20), true),
                r.IsDBNull(21) ? null : r.GetString(21), r.GetInt64(22) != 0, r.IsDBNull(23) ? null : r.GetString(23)) { RequiresLayoutReview = r.GetBoolean(24) });
        }
        return list;
    }

    public async Task<string?> ShelfRoleAsync(string id, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT role FROM managed_shelf_entries WHERE mod_id=$id";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    public async Task<ILookup<string,(string Id,string Role,string? Canonical)>> ReadShelfEntriesAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT shelf_path,mod_id,role,canonical_id FROM managed_shelf_entries";
        var rows = new List<(string Shelf,string Id,string Role,string? Canonical)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add((reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3) ? null : reader.GetString(3)));
        return rows.ToLookup(row => row.Shelf, row => (row.Id,row.Role,row.Canonical), StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetShelfEntriesAsync(string shelf, IReadOnlyList<(string Id,string Role,string? Canonical)> entries, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var connection = await OpenAsync(ct);
        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM managed_shelf_entries WHERE shelf_path=$shelf";
        command.Parameters.AddWithValue("$shelf", shelf);
        await command.ExecuteNonQueryAsync(ct);
        command.CommandText = "INSERT INTO managed_shelf_entries(mod_id,shelf_path,role,canonical_id) VALUES($id,$shelf,$role,$canonical)";
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var role = command.Parameters.Add("$role", SqliteType.Text);
        var canonical = command.Parameters.Add("$canonical", SqliteType.Text);
        foreach (var entry in entries)
        {
            id.Value = entry.Id;
            role.Value = entry.Role;
            canonical.Value = (object?)entry.Canonical ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(ct);
        }
        transaction.Commit();
    }

    public async Task<IReadOnlySet<string>> GetModSourcePathsAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT source_path FROM mods";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) paths.Add(r.GetString(0));
        return paths;
    }

    /// <summary>
    /// Lightweight catalog snapshot; avoids loading file provenance, family members and previews
    /// every time the user refreshes an installed folder. It never modifies existing records.
    /// </summary>
    public async Task<IReadOnlyList<ModCatalogSourceIdentity>> GetModCatalogSourceIdentitiesAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var list = new List<ModCatalogSourceIdentity>();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id,name,source_path,nexus_mod_id,source_url,enabled=0 AND NOT EXISTS(SELECT 1 FROM mod_files WHERE mod_id=mods.id),
                family_id IS NOT NULL OR source_url IS NOT NULL OR nexus_mod_id IS NOT NULL
                OR EXISTS(SELECT 1 FROM deployment_manifest WHERE provider_mod_id=mods.id)
                OR EXISTS(SELECT 1 FROM profile_mods WHERE mod_id=mods.id)
                OR EXISTS(SELECT 1 FROM conflict_rules WHERE mods.id IN (left_mod_id,right_mod_id,winner_mod_id))
                OR EXISTS(SELECT 1 FROM mod_family_members WHERE mod_id=mods.id)
                OR EXISTS(SELECT 1 FROM mod_provenance WHERE mod_id=mods.id)
                OR EXISTS(SELECT 1 FROM mod_supersession WHERE mods.id IN (older_mod_id,newer_mod_id))
                OR EXISTS(SELECT 1 FROM resource_providers WHERE mod_id=mods.id)
            FROM mods
            """;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new(r.GetString(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4)) { CanInspectCollection = r.GetBoolean(5), HasSavedReferences = r.GetBoolean(6) });
        return list;
    }

    /// <summary>
    /// Repair an unedited display title from its original archive filename only.
    /// Filename-derived IDs are untrusted and must never become authoritative Nexus IDs
    /// or trigger an automatic family/overwrite inference. The ID/path/name guard
    /// prevents stale catalog snapshots from changing a different package.
    /// </summary>
    public async Task<int> BackfillArchiveDisplayNameAsync(
        IReadOnlyList<ModArchiveDisplayBackfill> candidates,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"candidates={candidates.Count}");
        if (candidates.Count == 0) return 0;
        await using var c = await OpenAsync(ct);
        await using var tx = c.BeginTransaction(deferred: false);
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
        UPDATE mods
        SET display_name=$display, updated_at=$now
        WHERE id=$id AND name=$name AND source_path=$source
          AND display_name=name AND nexus_mod_id IS NULL AND source_url IS NULL
        """;
        var pId = cmd.Parameters.Add("$id", SqliteType.Text);
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pSource = cmd.Parameters.Add("$source", SqliteType.Text);
        var pDisplay = cmd.Parameters.Add("$display", SqliteType.Text);
        var pNow = cmd.Parameters.Add("$now", SqliteType.Text);
        var updated = 0;
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        cmd.Prepare();
        foreach (var row in candidates)
        {
            ct.ThrowIfCancellationRequested();
            pId.Value = row.Id;
            pName.Value = row.Name;
            pSource.Value = row.SourcePath;
            pDisplay.Value = row.DisplayName;
            pNow.Value = now;
            updated += await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return updated;
    }

    public async Task SetEnabledAndPriorityAsync(IReadOnlyDictionary<string,(bool enabled,int priority)> state, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE mods SET enabled=$e,priority=$p,updated_at=$u WHERE id=$id";
        var pEnabled = cmd.Parameters.Add("$e", SqliteType.Integer);
        var pPriority = cmd.Parameters.Add("$p", SqliteType.Integer);
        var pUpdated = cmd.Parameters.Add("$u", SqliteType.Text);
        var pId = cmd.Parameters.Add("$id", SqliteType.Text);
        cmd.Prepare();
        var now = DateTimeOffset.UtcNow.ToString("O");
        foreach (var (id,v) in state)
        {
            ct.ThrowIfCancellationRequested();
            pEnabled.Value = v.enabled ? 1 : 0;
            pPriority.Value = v.priority;
            pUpdated.Value = now;
            pId.Value = id;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task InsertDiscoveredModAsync(string id, string name, string display, string path, int priority, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var connection = await OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        // A concurrent refresh or selection may already own this row. Discovery only inserts.
        cmd.CommandText = """
            INSERT INTO mods(id,name,display_name,source_path,enabled,priority,imported_at,updated_at)
            VALUES($id,$name,$display,$path,0,$priority,$now,$now)
            ON CONFLICT(id) DO NOTHING
            ON CONFLICT(source_path COLLATE NOCASE) DO NOTHING
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$display", display);
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$priority", priority);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertModAsync(ModDescriptor mod, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        INSERT INTO mods(id,name,display_name,source_path,enabled,priority,family_id,category,source_url,nexus_mod_id,nexus_file_id,imported_at,updated_at)
        VALUES($id,$n,$d,$s,$e,$p,$f,$c,$u,$nm,$nf,$now,$now)
        ON CONFLICT(id) DO UPDATE SET name=excluded.name,display_name=excluded.display_name,source_path=excluded.source_path,enabled=excluded.enabled,priority=excluded.priority,family_id=excluded.family_id,category=COALESCE(excluded.category,mods.category),source_url=COALESCE(excluded.source_url,mods.source_url),nexus_mod_id=COALESCE(excluded.nexus_mod_id,mods.nexus_mod_id),nexus_file_id=COALESCE(excluded.nexus_file_id,mods.nexus_file_id),updated_at=excluded.updated_at;
        """;
        cmd.Parameters.AddWithValue("$id",mod.Id);
        cmd.Parameters.AddWithValue("$n",mod.Name);
        cmd.Parameters.AddWithValue("$d",mod.DisplayName);
        cmd.Parameters.AddWithValue("$s",mod.SourcePath);
        cmd.Parameters.AddWithValue("$e",mod.Enabled?1:0);
        cmd.Parameters.AddWithValue("$p",mod.Priority);
        cmd.Parameters.AddWithValue("$f",(object?)mod.FamilyId??DBNull.Value);
        cmd.Parameters.AddWithValue("$c",(object?)mod.Category??DBNull.Value);
        cmd.Parameters.AddWithValue("$u",(object?)mod.SourceUrl??DBNull.Value);
        cmd.Parameters.AddWithValue("$nm",(object?)mod.NexusModId??DBNull.Value);
        cmd.Parameters.AddWithValue("$nf",(object?)mod.NexusFileId??DBNull.Value);
        cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ReplaceModFilesAsync(string modId, IReadOnlyList<ModFileDescriptor> files, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        await using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM mod_files WHERE mod_id=$id";
            del.Parameters.AddWithValue("$id",modId);
            await del.ExecuteNonQueryAsync(ct);
        }

        await using var insert = c.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT INTO mod_files(mod_id,path,blob_sha256,fast_hash,length,last_write_utc,file_class) VALUES($m,$p,$b,$f,$l,$u,$c)";
        var pMod = insert.Parameters.Add("$m", SqliteType.Text);
        var pPath = insert.Parameters.Add("$p", SqliteType.Text);
        var pBlob = insert.Parameters.Add("$b", SqliteType.Text);
        var pFast = insert.Parameters.Add("$f", SqliteType.Text);
        var pLen = insert.Parameters.Add("$l", SqliteType.Integer);
        var pUpdated = insert.Parameters.Add("$u", SqliteType.Text);
        var pClass = insert.Parameters.Add("$c", SqliteType.Text);
        insert.Prepare();

        await using var blob = c.CreateCommand();
        blob.Transaction = tx;
        blob.CommandText = "INSERT OR IGNORE INTO blobs(sha256,size,created_at,verified_at) VALUES($h,$s,$u,$u)";
        var bHash = blob.Parameters.Add("$h", SqliteType.Text);
        var bSize = blob.Parameters.Add("$s", SqliteType.Integer);
        var bUtc = blob.Parameters.Add("$u", SqliteType.Text);
        blob.Prepare();

        await using var armor = c.CreateCommand();
        armor.Transaction = tx;
        armor.CommandText = "INSERT INTO mod_file_armor(mod_id,path,model_id,component) VALUES($m,$p,$model,$component)";
        var aMod = armor.Parameters.Add("$m", SqliteType.Text);
        var aPath = armor.Parameters.Add("$p", SqliteType.Text);
        var aModel = armor.Parameters.Add("$model", SqliteType.Text);
        var aComponent = armor.Parameters.Add("$component", SqliteType.Text);
        armor.Prepare();

        var capturedAt = DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            bHash.Value = f.BlobSha256;
            bSize.Value = f.Length;
            bUtc.Value = capturedAt;
            await blob.ExecuteNonQueryAsync(ct);

            pMod.Value = modId;
            pPath.Value = f.Path;
            pBlob.Value = f.BlobSha256;
            pFast.Value = (object?)f.FastHash ?? DBNull.Value;
            pLen.Value = f.Length;
            pUpdated.Value = f.LastWriteUtc.ToString("O");
            pClass.Value = f.FileClass.ToString();
            await insert.ExecuteNonQueryAsync(ct);

            if (PathRules.TryGetArmorComponent(f.Path, out var modelId, out var component))
            {
                aMod.Value = modId;
                aPath.Value = f.Path;
                aModel.Value = modelId;
                aComponent.Value = component;
                await armor.ExecuteNonQueryAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<ModFileDescriptor>> GetModFilesAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var files = new List<ModFileDescriptor>();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT mod_id,path,blob_sha256,fast_hash,length,last_write_utc,file_class FROM mod_files";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            files.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.GetInt64(4),DateTimeOffset.Parse(r.GetString(5),CultureInfo.InvariantCulture),Enum.Parse<FileClass>(r.GetString(6))));
        return files;
    }

    public async Task SetFamilyIdAsync(string modId,string? familyId,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="UPDATE mods SET family_id=$f,updated_at=$u WHERE id=$m";
        cmd.Parameters.AddWithValue("$f",(object?)familyId??DBNull.Value);cmd.Parameters.AddWithValue("$u",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));cmd.Parameters.AddWithValue("$m",modId);await cmd.ExecuteNonQueryAsync(ct);
    }


    /// <summary>
    /// Persist a user-confirmed separation from an incorrect logical family. A unique family key
    /// takes precedence over stale source IDs and name/asset inference. Only overlay rules generated
    /// by the former manual family are removed; unrelated explicit human rules are retained.
    /// Neither package payloads nor live game files are changed.
    /// </summary>
    public async Task SeparateLogicalMemberAsync(string modId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"mod={modId}");
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("A source package is required.", nameof(modId));

        await using var c = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        string? previousFamily;
        await using (var read = c.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT family_id FROM mods WHERE id=$m";
            read.Parameters.AddWithValue("$m", modId);
            var value = await read.ExecuteScalarAsync(ct);
            if (value is null)
                throw new InvalidOperationException($"Source package '{modId}' no longer exists.");
            previousFamily = value is DBNull ? null : (string)value;
        }

        // Historical manually chained overlays have no persisted ownership foreign key.
        // Match their exact generated shape, including precedence and canonical reason;
        // broad prefix/suffix matches can erase unrelated user-authored overwrite rules.
        // Ambiguous rules remain intact for explicit conflict review.
        var ownedRules = new List<string>();
        if (!string.IsNullOrWhiteSpace(previousFamily) &&
            previousFamily.StartsWith("manual:", StringComparison.OrdinalIgnoreCase))
        {
            await using (var read = c.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = """
                    SELECT id,reason,left_mod_id,right_mod_id,winner_mod_id,path_pattern
                    FROM conflict_rules
                    WHERE kind=$kind AND scope=$scope AND explicit=1
                    AND (left_mod_id=$m OR right_mod_id=$m)
                    """;
                read.Parameters.AddWithValue("$kind", RuleKind.Overlay.ToString());
                read.Parameters.AddWithValue("$scope", RuleScope.ModPair.ToString());
                read.Parameters.AddWithValue("$m", modId);
                await using var reader = await read.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var id = reader.GetString(0);
                    if (!Guid.TryParseExact(id, "N", out _) ||
                        reader.IsDBNull(1) || reader.IsDBNull(2) ||
                        reader.IsDBNull(3) || reader.IsDBNull(4) ||
                        !reader.IsDBNull(5))
                        continue;

                    var lower = reader.GetString(2);
                    var higher = reader.GetString(3);
                    var winner = reader.GetString(4);
                    if (string.Equals(lower, higher, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(higher, winner, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var generatedReason =
                        $"Manual family chain: '{higher}' is a later optional/component layer above '{lower}' in family '{previousFamily}'.";
                    if (string.Equals(reader.GetString(1), generatedReason, StringComparison.Ordinal))
                        ownedRules.Add(id);
                }
            }
            foreach (var ruleId in ownedRules)
            {
                await using var deleteRule = c.CreateCommand();
                deleteRule.Transaction = tx;
                deleteRule.CommandText = "DELETE FROM conflict_rules WHERE id=$id";
                deleteRule.Parameters.AddWithValue("$id", ruleId);
                await deleteRule.ExecuteNonQueryAsync(ct);
            }
        }

        await using (var deleteMember = c.CreateCommand())
        {
            deleteMember.Transaction = tx;
            deleteMember.CommandText = "DELETE FROM mod_family_members WHERE mod_id=$m";
            deleteMember.Parameters.AddWithValue("$m", modId);
            await deleteMember.ExecuteNonQueryAsync(ct);
        }

        // Use a stable hash of the exact (case-sensitive) source ID. Merely lowercasing
        // the ID would collapse case-distinct packages in legacy libraries.
        var separateFamily = "manual:split:" +
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(modId))).ToLowerInvariant();
        await using (var update = c.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = "UPDATE mods SET family_id=$f,updated_at=$at WHERE id=$m";
            update.Parameters.AddWithValue("$f", separateFamily);
            update.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            update.Parameters.AddWithValue("$m", modId);
            if (await update.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException($"Source package '{modId}' changed during separation.");
        }

        await tx.CommitAsync(ct);
        MasterDebugLog.Write("FAMILY-MANUAL",
            $"SEPARATE source={modId}; priorFamily={previousFamily ?? "<none>"}; removedOwnedRules={ownedRules.Count}; marker={separateFamily}");
    }

    /// <summary>
    /// Persists an explicit user-authored logical family. The selected main package remains the
    /// family root; every supplied child is recorded as an Optional member and receives an explicit
    /// main->optional overlay rule. No source files are moved or modified.
    /// </summary>
    public Task<string> ChainManualFamilyAsync(
        string mainModId,
        IReadOnlyCollection<string> mainMemberIds,
        IReadOnlyList<IReadOnlyList<string>> optionalGroups,
        string familyName,
        CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ChainManualFamilyAsync(mainModId,mainMemberIds,optionalGroups,familyName,false,ct);
    }

    public async Task<string> ChainManualFamilyAsync(
        string mainModId,
        IReadOnlyCollection<string> mainMemberIds,
        IReadOnlyList<IReadOnlyList<string>> optionalGroups,
        string familyName,
        bool refuseExistingPairRules,
        CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"main={mainModId}; mainMembers={mainMemberIds.Count}; optionalGroups={optionalGroups.Count}");
        if(string.IsNullOrWhiteSpace(mainModId))throw new ArgumentException("A main mod is required.",nameof(mainModId));
        var mains=mainMemberIds.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var groups=optionalGroups
            .Select(g=>(IReadOnlyList<string>)g.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            .Where(g=>g.Count>0)
            .ToArray();
        var optionals=groups.SelectMany(x=>x).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(mains.Length==0||!mains.Contains(mainModId,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("The selected main mod must be part of the main member set.",nameof(mainMemberIds));
        if(optionals.Length==0)throw new ArgumentException("At least one optional child is required.",nameof(optionalGroups));
        if(mains.Intersect(optionals,StringComparer.OrdinalIgnoreCase).Any())throw new ArgumentException("A package cannot be both main and optional.",nameof(optionalGroups));
        if(optionals.Length!=groups.Sum(g=>g.Count))throw new ArgumentException("An optional package cannot appear in more than one chained group.",nameof(optionalGroups));

        await using var c=await OpenAsync(ct);
        await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(ct);
        var all=mains.Concat(optionals).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var priorFamily=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        var priorRole=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        await using(var read=c.CreateCommand())
        {
            read.Transaction=tx;
            var names=new string[all.Length];
            for(var i=0;i<all.Length;i++){names[i]="$m"+i.ToString(CultureInfo.InvariantCulture);read.Parameters.AddWithValue(names[i],all[i]);}
            read.CommandText=$"SELECT m.id,m.family_id,fm.role FROM mods m LEFT JOIN mod_family_members fm ON fm.mod_id=m.id AND fm.family_id=m.family_id WHERE m.id IN ({string.Join(',',names)})";
            await using var reader=await read.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))
            {
                priorFamily[reader.GetString(0)]=reader.IsDBNull(1)?null:reader.GetString(1);
                priorRole[reader.GetString(0)]=reader.IsDBNull(2)?null:reader.GetString(2);
            }
        }
        if(!priorFamily.TryGetValue(mainModId,out var existingFamily))throw new InvalidOperationException($"Main mod '{mainModId}' no longer exists.");
        var familyId=!string.IsNullOrWhiteSpace(existingFamily)&&existingFamily.StartsWith("manual:",StringComparison.OrdinalIgnoreCase)
            ?existingFamily
            :"manual:"+mainModId.ToLowerInvariant();
        var now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);

        // Remove stale pairwise relationships only BETWEEN the logical groups the user is now
        // chaining. Relationships inside an already-composite option are preserved. If the selected
        // conflict itself is an internal same-family choice, its singleton options are different
        // chain groups, so this explicit action correctly replaces the old relationship.
        var orderedGroups=new List<IReadOnlyList<string>>{mains};orderedGroups.AddRange(groups);
        var chainGroupByMod=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        for(var groupIndex=0;groupIndex<orderedGroups.Count;groupIndex++)foreach(var id in orderedGroups[groupIndex])chainGroupByMod[id]=groupIndex;
        for(var i=0;i<all.Length;i++)for(var j=i+1;j<all.Length;j++)
        {
            var a=all[i];var b=all[j];
            if(chainGroupByMod[a]==chainGroupByMod[b])continue;
            if(refuseExistingPairRules)
            {
                await using var check=c.CreateCommand();check.Transaction=tx;
                check.CommandText="SELECT id FROM conflict_rules WHERE scope=$scope AND ((left_mod_id=$a AND right_mod_id=$b) OR (left_mod_id=$b AND right_mod_id=$a)) LIMIT 1";
                check.Parameters.AddWithValue("$scope",RuleScope.ModPair.ToString());check.Parameters.AddWithValue("$a",a);check.Parameters.AddWithValue("$b",b);
                var existing=await check.ExecuteScalarAsync(ct);
                if(existing is not null)
                    throw new InvalidOperationException($"Suggested family confirmation cannot replace existing pair rule '{existing}' between '{a}' and '{b}'. Review the existing conflict rules explicitly.");
                continue;
            }
            await using var del=c.CreateCommand();del.Transaction=tx;
            del.CommandText="DELETE FROM conflict_rules WHERE scope=$scope AND ((left_mod_id=$a AND right_mod_id=$b) OR (left_mod_id=$b AND right_mod_id=$a))";
            del.Parameters.AddWithValue("$scope",RuleScope.ModPair.ToString());del.Parameters.AddWithValue("$a",a);del.Parameters.AddWithValue("$b",b);
            await del.ExecuteNonQueryAsync(ct);
        }

        await using(var fam=c.CreateCommand())
        {
            fam.Transaction=tx;fam.CommandText="INSERT INTO mod_families(id,name,created_at) VALUES($id,$n,$t) ON CONFLICT(id) DO UPDATE SET name=excluded.name";
            fam.Parameters.AddWithValue("$id",familyId);fam.Parameters.AddWithValue("$n",string.IsNullOrWhiteSpace(familyName)?mainModId:familyName);fam.Parameters.AddWithValue("$t",now);
            await fam.ExecuteNonQueryAsync(ct);
        }

        foreach(var id in all)
        {
            await using(var remove=c.CreateCommand())
            {
                remove.Transaction=tx;remove.CommandText="DELETE FROM mod_family_members WHERE mod_id=$m";remove.Parameters.AddWithValue("$m",id);await remove.ExecuteNonQueryAsync(ct);
            }
            await using(var update=c.CreateCommand())
            {
                update.Transaction=tx;update.CommandText="UPDATE mods SET family_id=$f,updated_at=$u WHERE id=$m";
                update.Parameters.AddWithValue("$f",familyId);update.Parameters.AddWithValue("$u",now);update.Parameters.AddWithValue("$m",id);await update.ExecuteNonQueryAsync(ct);
            }
            await using(var member=c.CreateCommand())
            {
                member.Transaction=tx;member.CommandText="INSERT INTO mod_family_members(family_id,mod_id,role,choice_group) VALUES($f,$m,$r,NULL)";
                member.Parameters.AddWithValue("$f",familyId);member.Parameters.AddWithValue("$m",id);
                var oldRole=priorRole.GetValueOrDefault(id);
                var role=StringComparer.OrdinalIgnoreCase.Equals(id,mainModId)?"Main"
                    :mains.Contains(id,StringComparer.OrdinalIgnoreCase)?oldRole??"Component"
                    :StringComparer.OrdinalIgnoreCase.Equals(oldRole,"Main")||string.IsNullOrWhiteSpace(oldRole)?"Optional":"Component";
                member.Parameters.AddWithValue("$r",role);
                await member.ExecuteNonQueryAsync(ct);
            }
        }

        // Persist a total cross-group order, not merely main->child stars. If Main, Optional A and
        // Optional B all carry the same shared base file, A->B ensures the two optional layers do
        // not reappear as a second conflict. We write the transitive closure so resolution remains
        // deterministic even when an intermediate optional group does not provide a particular path.
        var proposedEdges=new List<(string lower,string higher)>();
        for(var i=0;i<orderedGroups.Count-1;i++)for(var j=i+1;j<orderedGroups.Count;j++)
        foreach(var lower in orderedGroups[i])foreach(var higher in orderedGroups[j])proposedEdges.Add((lower,higher));

        // Fail before commit if the new explicit chain would create a precedence cycle through some
        // existing outside relationship. The whole transaction then rolls back without partially
        // rewriting family metadata.
        var graphRules=new List<ConflictRule>();
        await using(var graph=c.CreateCommand())
        {
            graph.Transaction=tx;graph.CommandText="SELECT id,left_mod_id,right_mod_id,winner_mod_id,explicit,created_at FROM conflict_rules WHERE kind=$kind AND scope=$scope AND left_mod_id IS NOT NULL AND right_mod_id IS NOT NULL AND winner_mod_id IS NOT NULL";
            graph.Parameters.AddWithValue("$kind",RuleKind.Overlay.ToString());graph.Parameters.AddWithValue("$scope",RuleScope.ModPair.ToString());
            await using var reader=await graph.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))graphRules.Add(new(reader.GetString(0),RuleKind.Overlay,RuleScope.ModPair,reader.GetString(1),reader.GetString(2),reader.GetString(3),null,"existing overlay",reader.GetInt64(4)!=0,DateTimeOffset.Parse(reader.GetString(5),CultureInfo.InvariantCulture)));
        }
        graphRules.AddRange(proposedEdges.Select((edge,index)=>new ConflictRule($"manual-chain-proposed-{index}",RuleKind.Overlay,RuleScope.ModPair,edge.lower,edge.higher,edge.higher,null,"manual chain validation",true,DateTimeOffset.UtcNow)));
        var cycle=RuleGraph.FindCycle(graphRules);
        if(cycle is not null)throw new InvalidOperationException($"Manual family chain would create a precedence cycle: {string.Join(" -> ",cycle)}");

        foreach(var (lower,higher) in proposedEdges)
        {
            await using var rule=c.CreateCommand();rule.Transaction=tx;
            rule.CommandText="""
            INSERT INTO conflict_rules(id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at)
            VALUES($id,$kind,$scope,$left,$right,$winner,NULL,$reason,1,$created)
            """;
            rule.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));rule.Parameters.AddWithValue("$kind",RuleKind.Overlay.ToString());rule.Parameters.AddWithValue("$scope",RuleScope.ModPair.ToString());
            rule.Parameters.AddWithValue("$left",lower);rule.Parameters.AddWithValue("$right",higher);rule.Parameters.AddWithValue("$winner",higher);
            rule.Parameters.AddWithValue("$reason",$"Manual family chain: '{higher}' is a later optional/component layer above '{lower}' in family '{familyId}'.");rule.Parameters.AddWithValue("$created",now);
            await rule.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        MasterDebugLog.Write("FAMILY-MANUAL",$"CHAIN family={familyId}; main={mainModId}; mainMembers=[{string.Join(" | ",mains)}]; optionalMembers=[{string.Join(" | ",optionals)}]");
        return familyId;
    }

    public async Task UpsertProvenanceAsync(ModProvenance provenance, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        INSERT INTO mod_provenance(mod_id,nexus_game_mod_id,nexus_mod_uuid,nexus_file_id,nexus_version_id,nexus_previous_version_id,nexus_category,nexus_file_name,nexus_version,uploaded_at,provenance_source,confidence_score,source_archive_name,metadata_json,updated_at)
        VALUES($m,$gm,$mu,$f,$v,$pv,$cat,$fn,$ver,$up,$src,$score,$archive,$json,$now)
        ON CONFLICT(mod_id) DO UPDATE SET nexus_game_mod_id=excluded.nexus_game_mod_id,nexus_mod_uuid=excluded.nexus_mod_uuid,nexus_file_id=excluded.nexus_file_id,nexus_version_id=excluded.nexus_version_id,nexus_previous_version_id=excluded.nexus_previous_version_id,nexus_category=excluded.nexus_category,nexus_file_name=excluded.nexus_file_name,nexus_version=excluded.nexus_version,uploaded_at=excluded.uploaded_at,provenance_source=excluded.provenance_source,confidence_score=excluded.confidence_score,source_archive_name=excluded.source_archive_name,metadata_json=excluded.metadata_json,updated_at=excluded.updated_at
        WHERE excluded.confidence_score >= mod_provenance.confidence_score
        """;
        cmd.Parameters.AddWithValue("$m",provenance.ModId);
        cmd.Parameters.AddWithValue("$gm",(object?)provenance.NexusGameModId??DBNull.Value);
        cmd.Parameters.AddWithValue("$mu",(object?)provenance.NexusModUuid??DBNull.Value);
        cmd.Parameters.AddWithValue("$f",(object?)provenance.NexusFileId??DBNull.Value);
        cmd.Parameters.AddWithValue("$v",(object?)provenance.NexusVersionId??DBNull.Value);
        cmd.Parameters.AddWithValue("$pv",(object?)provenance.NexusPreviousVersionId??DBNull.Value);
        cmd.Parameters.AddWithValue("$cat",provenance.NexusCategory.ToString());
        cmd.Parameters.AddWithValue("$fn",(object?)provenance.NexusFileName??DBNull.Value);
        cmd.Parameters.AddWithValue("$ver",(object?)provenance.NexusVersion??DBNull.Value);
        cmd.Parameters.AddWithValue("$up",(object?)provenance.UploadedAt?.ToString("O",CultureInfo.InvariantCulture)??DBNull.Value);
        cmd.Parameters.AddWithValue("$src",provenance.Source.ToString());
        cmd.Parameters.AddWithValue("$score",provenance.ConfidenceScore);
        cmd.Parameters.AddWithValue("$archive",(object?)provenance.SourceArchiveName??DBNull.Value);
        cmd.Parameters.AddWithValue("$json",(object?)provenance.MetadataJson??DBNull.Value);
        cmd.Parameters.AddWithValue("$now",provenance.UpdatedAt.ToString("O",CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string,ModProvenance>> GetProvenanceAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result=new Dictionary<string,ModProvenance>(StringComparer.OrdinalIgnoreCase);
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT mod_id,nexus_game_mod_id,nexus_mod_uuid,nexus_file_id,nexus_version_id,nexus_previous_version_id,nexus_category,nexus_file_name,nexus_version,uploaded_at,provenance_source,confidence_score,source_archive_name,metadata_json,updated_at FROM mod_provenance";
        await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))
        {
            var item=new ModProvenance(r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),
                Enum.Parse<NexusFileCategory>(r.GetString(6),true),r.IsDBNull(7)?null:r.GetString(7),r.IsDBNull(8)?null:r.GetString(8),r.IsDBNull(9)?null:DateTimeOffset.Parse(r.GetString(9),CultureInfo.InvariantCulture),
                Enum.Parse<ProvenanceSource>(r.GetString(10),true),r.GetInt32(11),r.IsDBNull(12)?null:r.GetString(12),r.IsDBNull(13)?null:r.GetString(13),DateTimeOffset.Parse(r.GetString(14),CultureInfo.InvariantCulture));
            result[item.ModId]=item;
        }
        return result;
    }

    public async Task ReplaceSupersessionAsync(IReadOnlyList<(string older,string newer,int score,string reason)> links,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await OpenAsync(ct);await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(ct);
        await ExecAsync(c,"DELETE FROM mod_supersession",ct,tx);
        await using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO mod_supersession(older_mod_id,newer_mod_id,confidence_score,reason,created_at) VALUES($o,$n,$s,$r,$t)";
        var po=cmd.Parameters.Add("$o",SqliteType.Text);var pn=cmd.Parameters.Add("$n",SqliteType.Text);var ps=cmd.Parameters.Add("$s",SqliteType.Integer);var pr=cmd.Parameters.Add("$r",SqliteType.Text);var pt=cmd.Parameters.Add("$t",SqliteType.Text);cmd.Prepare();
        var createdAt=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        foreach(var link in links){po.Value=link.older;pn.Value=link.newer;ps.Value=link.score;pr.Value=link.reason;pt.Value=createdAt;await cmd.ExecuteNonQueryAsync(ct);}
        await tx.CommitAsync(ct);
    }

    public async Task RecordResolverAuditAsync(IEnumerable<ResolverAudit> audits,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await OpenAsync(ct);await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(ct);
        await using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO resolver_audit(time,path,winner_mod_id,score,reason_code,explanation,evidence) VALUES($t,$p,$w,$s,$r,$x,$e)";
        var pt=cmd.Parameters.Add("$t",SqliteType.Text);var pp=cmd.Parameters.Add("$p",SqliteType.Text);var pw=cmd.Parameters.Add("$w",SqliteType.Text);var ps=cmd.Parameters.Add("$s",SqliteType.Integer);var pr=cmd.Parameters.Add("$r",SqliteType.Text);var px=cmd.Parameters.Add("$x",SqliteType.Text);var pe=cmd.Parameters.Add("$e",SqliteType.Text);cmd.Prepare();
        var auditTime=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        foreach(var a in audits){pt.Value=auditTime;pp.Value=a.Path;pw.Value=(object?)a.WinnerModId??DBNull.Value;ps.Value=a.Score;pr.Value=a.ReasonCode;px.Value=a.Explanation;pe.Value=a.Evidence;await cmd.ExecuteNonQueryAsync(ct);}
        await tx.CommitAsync(ct);
    }

    public async Task<GameBuildFingerprint?> GetGameBuildFingerprintAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT exe_path,length,last_write_utc,sha256 FROM game_build_state WHERE id=1";await using var r=await cmd.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))return null;
        return new(r.GetString(0),r.GetInt64(1),DateTimeOffset.Parse(r.GetString(2),CultureInfo.InvariantCulture),r.GetString(3));
    }

    public async Task SetGameBuildFingerprintAsync(GameBuildFingerprint fingerprint,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO game_build_state(id,exe_path,length,last_write_utc,sha256,observed_at) VALUES(1,$p,$l,$u,$s,$o) ON CONFLICT(id) DO UPDATE SET exe_path=excluded.exe_path,length=excluded.length,last_write_utc=excluded.last_write_utc,sha256=excluded.sha256,observed_at=excluded.observed_at";
        cmd.Parameters.AddWithValue("$p",fingerprint.ExecutablePath);cmd.Parameters.AddWithValue("$l",fingerprint.Length);cmd.Parameters.AddWithValue("$u",fingerprint.LastWriteUtc.ToString("O",CultureInfo.InvariantCulture));cmd.Parameters.AddWithValue("$s",fingerprint.Sha256);cmd.Parameters.AddWithValue("$o",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkRevalidationAsync(IEnumerable<string> modIds,string reason,bool required,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await OpenAsync(ct);await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(ct);
        await using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO mod_revalidation(mod_id,required,reason,detected_at) VALUES($m,$q,$r,$t) ON CONFLICT(mod_id) DO UPDATE SET required=excluded.required,reason=excluded.reason,detected_at=excluded.detected_at";
        var pm=cmd.Parameters.Add("$m",SqliteType.Text);var pq=cmd.Parameters.Add("$q",SqliteType.Integer);var pr=cmd.Parameters.Add("$r",SqliteType.Text);var pt=cmd.Parameters.Add("$t",SqliteType.Text);cmd.Prepare();
        foreach(var id in modIds){pm.Value=id;pq.Value=required?1:0;pr.Value=reason;pt.Value=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);await cmd.ExecuteNonQueryAsync(ct);}
        await tx.CommitAsync(ct);
    }

    public async Task<string?> GetSettingAsync(string key,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"key={key}");
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM settings WHERE key=$k";cmd.Parameters.AddWithValue("$k",key);return (string?)await cmd.ExecuteScalarAsync(ct);}

    public async Task SetSettingAsync(string key,string value,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"key={key}; value=<redacted-if-sensitive>");
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";cmd.Parameters.AddWithValue("$k",key);cmd.Parameters.AddWithValue("$v",value);await cmd.ExecuteNonQueryAsync(ct);}
    public async Task<IReadOnlyDictionary<string,string>> GetSettingsByPrefixAsync(string prefix,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"prefix={prefix}");
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT key,value FROM settings WHERE substr(key,1,length($p))=$p";
        cmd.Parameters.AddWithValue("$p",prefix);
        await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))result[r.GetString(0)]=r.GetString(1);
        return result;
    }

    public async Task ExecuteAsync(string sql, IReadOnlyDictionary<string,object?>? args = null, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"sql={CompactSql(sql)}; parameterCount={args?.Count ?? 0}");
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        if (args is not null) foreach (var kv in args) cmd.Parameters.AddWithValue(kv.Key,kv.Value??DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<T> InTransactionAsync<T>(Func<SqliteConnection,SqliteTransaction,CancellationToken,Task<T>> body, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod("sqlite-transaction");
        await using var c = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        MasterDebugLog.Write("DB-TX", "BEGIN SQLite transaction");
        try
        {
            var result = await body(c,tx,ct);
            await tx.CommitAsync(ct);
            MasterDebugLog.Write("DB-TX", "COMMIT SQLite transaction");
            return result;
        }
        catch(Exception ex)
        {
            MasterDebugLog.Write("DB-TX", "ROLLBACK SQLite transaction after failure", ex);
            try { await tx.RollbackAsync(CancellationToken.None); MasterDebugLog.Write("DB-TX", "ROLLBACK completed"); } catch(Exception rollbackEx) { MasterDebugLog.Write("DB-TX", "ROLLBACK itself failed", rollbackEx); }
            throw;
        }
    }

    private static string CompactSql(string sql)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var compact = string.Join(" ", sql.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 500 ? compact : compact[..500] + "…";
    }

    public async Task<string> IntegrityCheckAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check";
        return (string?)await cmd.ExecuteScalarAsync(ct) ?? "unknown";
    }

    public async Task CheckpointAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(PASSIVE)";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
