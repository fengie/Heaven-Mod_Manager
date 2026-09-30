using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;

namespace MhwModManager.Storage;

public sealed record CatalogSyncState(
    string ProviderId,
    string GameId,
    string? Cursor,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastAttemptAt,
    string? LastError);

public sealed class CatalogCacheRepository(ManagerDatabase db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task UpsertSourceAsync(
        string providerId,
        string displayName,
        CatalogSourceKind sourceKind,
        CatalogProviderState healthState,
        DateTimeOffset? lastSyncAt = null,
        string? lastError = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        INSERT INTO catalog_sources(provider_id,display_name,source_kind,health_state,last_sync_at,last_error,updated_at)
        VALUES($provider,$display,$kind,$health,$sync,$error,$updated)
        ON CONFLICT(provider_id) DO UPDATE SET
            display_name=excluded.display_name,
            source_kind=excluded.source_kind,
            health_state=excluded.health_state,
            last_sync_at=excluded.last_sync_at,
            last_error=excluded.last_error,
            updated_at=excluded.updated_at
        """;
        cmd.Parameters.AddWithValue("$provider", providerId);
        cmd.Parameters.AddWithValue("$display", displayName);
        cmd.Parameters.AddWithValue("$kind", sourceKind.ToString());
        cmd.Parameters.AddWithValue("$health", healthState.ToString());
        cmd.Parameters.AddWithValue("$sync", Format(lastSyncAt));
        cmd.Parameters.AddWithValue("$error", (object?)lastError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertAsync(CachedCatalogMod cached, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(cached);

        var mod = cached.Mod;
        await using var c = await db.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

        await using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
            INSERT INTO catalog_items(
                canonical_id,provider_id,provider_mod_id,game_id,name,author,summary,description,
                category,tags_text,source_url,updated_at,mod_json)
            VALUES($canonical,$provider,$providerMod,$game,$name,$author,$summary,$description,
                $category,$tags,$url,$updated,$json)
            ON CONFLICT(canonical_id) DO UPDATE SET
                provider_id=excluded.provider_id,
                provider_mod_id=excluded.provider_mod_id,
                game_id=excluded.game_id,
                name=excluded.name,
                author=excluded.author,
                summary=excluded.summary,
                description=excluded.description,
                category=excluded.category,
                tags_text=excluded.tags_text,
                source_url=excluded.source_url,
                updated_at=excluded.updated_at,
                mod_json=excluded.mod_json
            """;
            cmd.Parameters.AddWithValue("$canonical", mod.CanonicalId);
            cmd.Parameters.AddWithValue("$provider", mod.ProviderId);
            cmd.Parameters.AddWithValue("$providerMod", mod.ProviderModId);
            cmd.Parameters.AddWithValue("$game", mod.GameId);
            cmd.Parameters.AddWithValue("$name", mod.Name);
            cmd.Parameters.AddWithValue("$author", mod.Author);
            cmd.Parameters.AddWithValue("$summary", mod.Summary);
            cmd.Parameters.AddWithValue("$description", mod.Description);
            cmd.Parameters.AddWithValue("$category", (object?)mod.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tags", string.Join(' ', mod.Tags));
            cmd.Parameters.AddWithValue("$url", mod.SourceUrl);
            cmd.Parameters.AddWithValue("$updated", Format(mod.UpdatedAt));
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(mod, JsonOptions));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var delete = c.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM catalog_files WHERE canonical_id=$canonical";
            delete.Parameters.AddWithValue("$canonical", mod.CanonicalId);
            await delete.ExecuteNonQueryAsync(ct);
        }

        await using (var insert = c.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
            INSERT INTO catalog_files(
                provider_id,provider_mod_id,provider_file_id,canonical_id,category,version,file_name,uploaded_at,file_json)
            VALUES($provider,$providerMod,$file,$canonical,$category,$version,$fileName,$uploaded,$json)
            """;
            var pProvider = insert.Parameters.Add("$provider", SqliteType.Text);
            var pProviderMod = insert.Parameters.Add("$providerMod", SqliteType.Text);
            var pFile = insert.Parameters.Add("$file", SqliteType.Text);
            var pCanonical = insert.Parameters.Add("$canonical", SqliteType.Text);
            var pCategory = insert.Parameters.Add("$category", SqliteType.Text);
            var pVersion = insert.Parameters.Add("$version", SqliteType.Text);
            var pFileName = insert.Parameters.Add("$fileName", SqliteType.Text);
            var pUploaded = insert.Parameters.Add("$uploaded", SqliteType.Text);
            var pJson = insert.Parameters.Add("$json", SqliteType.Text);

            foreach (var file in mod.Files)
            {
                ct.ThrowIfCancellationRequested();
                pProvider.Value = file.ProviderId;
                pProviderMod.Value = file.ProviderModId;
                pFile.Value = file.ProviderFileId;
                pCanonical.Value = mod.CanonicalId;
                pCategory.Value = file.Category.ToString();
                pVersion.Value = (object?)file.Version ?? DBNull.Value;
                pFileName.Value = file.FileName;
                pUploaded.Value = Format(file.UploadedAt);
                pJson.Value = JsonSerializer.Serialize(file, JsonOptions);
                await insert.ExecuteNonQueryAsync(ct);
            }
        }

        await using (var provenance = c.CreateCommand())
        {
            provenance.Transaction = tx;
            provenance.CommandText = """
            INSERT INTO catalog_provenance(canonical_id,fetched_at,expires_at,etag,last_modified,source_fingerprint)
            VALUES($canonical,$fetched,$expires,$etag,$modified,$fingerprint)
            ON CONFLICT(canonical_id) DO UPDATE SET
                fetched_at=excluded.fetched_at,
                expires_at=excluded.expires_at,
                etag=excluded.etag,
                last_modified=excluded.last_modified,
                source_fingerprint=excluded.source_fingerprint
            """;
            provenance.Parameters.AddWithValue("$canonical", mod.CanonicalId);
            provenance.Parameters.AddWithValue("$fetched", cached.Cache.FetchedAt.ToString("O", CultureInfo.InvariantCulture));
            provenance.Parameters.AddWithValue("$expires", Format(cached.Cache.ExpiresAt));
            provenance.Parameters.AddWithValue("$etag", (object?)cached.Cache.ETag ?? DBNull.Value);
            provenance.Parameters.AddWithValue("$modified", Format(cached.Cache.LastModified));
            provenance.Parameters.AddWithValue("$fingerprint", (object?)cached.Cache.SourceFingerprint ?? DBNull.Value);
            await provenance.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<CachedCatalogMod?> GetAsync(string canonicalId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalId);

        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        SELECT i.mod_json,p.fetched_at,p.expires_at,p.etag,p.last_modified,p.source_fingerprint
        FROM catalog_items i
        JOIN catalog_provenance p ON p.canonical_id=i.canonical_id
        WHERE i.canonical_id=$canonical
        """;
        cmd.Parameters.AddWithValue("$canonical", canonicalId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        var mod = JsonSerializer.Deserialize<CatalogMod>(r.GetString(0), JsonOptions)
            ?? throw new InvalidDataException($"Cached catalog item '{canonicalId}' could not be deserialized.");
        var metadata = new CatalogCacheMetadata(
            ParseRequired(r.GetString(1)),
            ParseNullable(r, 2),
            r.IsDBNull(3) ? null : r.GetString(3),
            ParseNullable(r, 4),
            r.IsDBNull(5) ? null : r.GetString(5));
        return new CachedCatalogMod(mod, metadata);
    }

    public async Task<IReadOnlyList<CachedCatalogMod>> SearchAsync(
        string? query,
        string? providerId = null,
        string? gameId = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));

        var results = new List<CachedCatalogMod>();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();

        var fts = BuildFtsQuery(query);
        cmd.CommandText = fts is null
            ? """
              SELECT i.mod_json,p.fetched_at,p.expires_at,p.etag,p.last_modified,p.source_fingerprint
              FROM catalog_items i
              JOIN catalog_provenance p ON p.canonical_id=i.canonical_id
              WHERE ($provider IS NULL OR i.provider_id=$provider)
                AND ($game IS NULL OR i.game_id=$game)
              ORDER BY COALESCE(i.updated_at,'') DESC,i.name COLLATE NOCASE
              LIMIT $limit
              """
            : """
              SELECT i.mod_json,p.fetched_at,p.expires_at,p.etag,p.last_modified,p.source_fingerprint
              FROM catalog_items_fts f
              JOIN catalog_items i ON i.canonical_id=f.canonical_id
              JOIN catalog_provenance p ON p.canonical_id=i.canonical_id
              WHERE catalog_items_fts MATCH $query
                AND ($provider IS NULL OR i.provider_id=$provider)
                AND ($game IS NULL OR i.game_id=$game)
              ORDER BY bm25(catalog_items_fts),COALESCE(i.updated_at,'') DESC
              LIMIT $limit
              """;
        cmd.Parameters.AddWithValue("$query", (object?)fts ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$provider", (object?)providerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$game", (object?)gameId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$limit", limit);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var mod = JsonSerializer.Deserialize<CatalogMod>(r.GetString(0), JsonOptions)
                ?? throw new InvalidDataException("Cached catalog search result could not be deserialized.");
            results.Add(new CachedCatalogMod(
                mod,
                new CatalogCacheMetadata(
                    ParseRequired(r.GetString(1)),
                    ParseNullable(r, 2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    ParseNullable(r, 4),
                    r.IsDBNull(5) ? null : r.GetString(5))));
        }

        return results;
    }

    public async Task SetSyncStateAsync(CatalogSyncState state, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(state);

        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        INSERT INTO catalog_sync_state(provider_id,game_id,cursor,last_success_at,last_attempt_at,last_error)
        VALUES($provider,$game,$cursor,$success,$attempt,$error)
        ON CONFLICT(provider_id,game_id) DO UPDATE SET
            cursor=excluded.cursor,
            last_success_at=excluded.last_success_at,
            last_attempt_at=excluded.last_attempt_at,
            last_error=excluded.last_error
        """;
        cmd.Parameters.AddWithValue("$provider", state.ProviderId);
        cmd.Parameters.AddWithValue("$game", state.GameId);
        cmd.Parameters.AddWithValue("$cursor", (object?)state.Cursor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$success", Format(state.LastSuccessAt));
        cmd.Parameters.AddWithValue("$attempt", Format(state.LastAttemptAt));
        cmd.Parameters.AddWithValue("$error", (object?)state.LastError ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<CatalogSyncState?> GetSyncStateAsync(
        string providerId,
        string gameId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        SELECT cursor,last_success_at,last_attempt_at,last_error
        FROM catalog_sync_state
        WHERE provider_id=$provider AND game_id=$game
        """;
        cmd.Parameters.AddWithValue("$provider", providerId);
        cmd.Parameters.AddWithValue("$game", gameId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new CatalogSyncState(
            providerId,
            gameId,
            r.IsDBNull(0) ? null : r.GetString(0),
            ParseNullable(r, 1),
            ParseNullable(r, 2),
            r.IsDBNull(3) ? null : r.GetString(3));
    }

    private static string? BuildFtsQuery(string? query)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(query)) return null;
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return null;
        return string.Join(" AND ", tokens.Select(token => """ + token.Replace(""", """", StringComparison.Ordinal) + """));
    }

    private static object Format(DateTimeOffset? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value is null ? DBNull.Value : value.Value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseRequired(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static DateTimeOffset? ParseNullable(SqliteDataReader reader, int ordinal)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return reader.IsDBNull(ordinal) ? null : ParseRequired(reader.GetString(ordinal));
    }
}
