using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;

namespace MhwModManager.Storage;

public sealed partial class ManagerDatabase
{
    public async Task UpsertCachedCatalogModAsync(
        CachedCatalogMod cached,
        string sourceDisplayName,
        string sourceKind,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={cached.Mod.ProviderId}; mod={cached.Mod.ProviderModId}");
        ArgumentNullException.ThrowIfNull(cached);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKind);

        var mod = cached.Mod;
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.CanonicalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.ProviderModId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.GameId);
        var sourceUrl = EnsureDurableUrl(mod.SourceUrl, nameof(mod.SourceUrl));
        var thumbnail = TryGetDurableUrl(mod.Thumbnail);
        var screenshots = mod.Screenshots
            .Select(image => (image, url: TryGetDurableUrl(image.Url)))
            .Where(pair => pair.url is not null)
            .Select(pair => pair.image with { Url = pair.url! })
            .ToArray();
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using var connection = await OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using (var source = connection.CreateCommand())
        {
            source.Transaction = transaction;
            source.CommandText = """
            INSERT INTO catalog_sources(provider_id,display_name,source_kind,enabled,updated_at)
            VALUES($p,$d,$k,1,$u)
            ON CONFLICT(provider_id) DO UPDATE SET
                display_name=excluded.display_name,
                source_kind=excluded.source_kind,
                enabled=1,
                updated_at=excluded.updated_at
            """;
            source.Parameters.AddWithValue("$p", mod.ProviderId);
            source.Parameters.AddWithValue("$d", sourceDisplayName);
            source.Parameters.AddWithValue("$k", sourceKind);
            source.Parameters.AddWithValue("$u", now);
            await source.ExecuteNonQueryAsync(ct);
        }

        await using (var item = connection.CreateCommand())
        {
            item.Transaction = transaction;
            item.CommandText = """
            INSERT INTO catalog_items(
                canonical_id,provider_id,provider_mod_id,game_id,name,summary,description,author,version,category,
                tags_json,thumbnail,screenshots_json,created_at,updated_at,downloads,endorsements,rating,dependencies_json,
                source_url,fetched_at,expires_at,etag,last_modified,source_fingerprint)
            VALUES(
                $id,$p,$pm,$g,$n,$s,$d,$a,$v,$c,$tags,$thumb,$shots,$created,$updated,$downloads,$endorsements,$rating,$deps,
                $url,$fetched,$expires,$etag,$lastmod,$fingerprint)
            ON CONFLICT(canonical_id) DO UPDATE SET
                provider_id=excluded.provider_id,
                provider_mod_id=excluded.provider_mod_id,
                game_id=excluded.game_id,
                name=excluded.name,
                summary=excluded.summary,
                description=excluded.description,
                author=excluded.author,
                version=excluded.version,
                category=excluded.category,
                tags_json=excluded.tags_json,
                thumbnail=excluded.thumbnail,
                screenshots_json=excluded.screenshots_json,
                created_at=excluded.created_at,
                updated_at=excluded.updated_at,
                downloads=excluded.downloads,
                endorsements=excluded.endorsements,
                rating=excluded.rating,
                dependencies_json=excluded.dependencies_json,
                source_url=excluded.source_url,
                fetched_at=excluded.fetched_at,
                expires_at=excluded.expires_at,
                etag=excluded.etag,
                last_modified=excluded.last_modified,
                source_fingerprint=excluded.source_fingerprint
            """;
            item.Parameters.AddWithValue("$id", mod.CanonicalId);
            item.Parameters.AddWithValue("$p", mod.ProviderId);
            item.Parameters.AddWithValue("$pm", mod.ProviderModId);
            item.Parameters.AddWithValue("$g", mod.GameId);
            item.Parameters.AddWithValue("$n", mod.Name);
            item.Parameters.AddWithValue("$s", mod.Summary);
            item.Parameters.AddWithValue("$d", mod.Description);
            item.Parameters.AddWithValue("$a", mod.Author);
            item.Parameters.AddWithValue("$v", DbValue(mod.Version));
            item.Parameters.AddWithValue("$c", DbValue(mod.Category));
            item.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(mod.Tags));
            item.Parameters.AddWithValue("$thumb", DbValue(thumbnail));
            item.Parameters.AddWithValue("$shots", JsonSerializer.Serialize(screenshots));
            item.Parameters.AddWithValue("$created", DbDate(mod.CreatedAt));
            item.Parameters.AddWithValue("$updated", DbDate(mod.UpdatedAt));
            item.Parameters.AddWithValue("$downloads", DbValue(mod.Downloads));
            item.Parameters.AddWithValue("$endorsements", DbValue(mod.Endorsements));
            item.Parameters.AddWithValue("$rating", DbValue(mod.Rating));
            item.Parameters.AddWithValue("$deps", JsonSerializer.Serialize(mod.Dependencies));
            item.Parameters.AddWithValue("$url", sourceUrl);
            item.Parameters.AddWithValue("$fetched", cached.Cache.FetchedAt.ToString("O", CultureInfo.InvariantCulture));
            item.Parameters.AddWithValue("$expires", DbDate(cached.Cache.ExpiresAt));
            item.Parameters.AddWithValue("$etag", DbValue(cached.Cache.ETag));
            item.Parameters.AddWithValue("$lastmod", DbDate(cached.Cache.LastModified));
            item.Parameters.AddWithValue("$fingerprint", DbValue(cached.Cache.SourceFingerprint));
            await item.ExecuteNonQueryAsync(ct);
        }

        await using (var deleteFiles = connection.CreateCommand())
        {
            deleteFiles.Transaction = transaction;
            deleteFiles.CommandText = "DELETE FROM catalog_files WHERE canonical_id=$id";
            deleteFiles.Parameters.AddWithValue("$id", mod.CanonicalId);
            await deleteFiles.ExecuteNonQueryAsync(ct);
        }

        foreach (var file in mod.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(file.ProviderId, mod.ProviderId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(file.ProviderModId, mod.ProviderModId, StringComparison.Ordinal))
                throw new InvalidDataException("Catalog file identity must match its parent catalog mod.");

            await using var insertFile = connection.CreateCommand();
            insertFile.Transaction = transaction;
            insertFile.CommandText = """
            INSERT INTO catalog_files(
                canonical_id,provider_id,provider_mod_id,provider_file_id,name,file_name,category,version,size_bytes,
                description,uploaded_at,required,recommended,dependencies_json)
            VALUES($id,$p,$pm,$pf,$n,$fn,$c,$v,$size,$d,$uploaded,$required,$recommended,$deps)
            """;
            insertFile.Parameters.AddWithValue("$id", mod.CanonicalId);
            insertFile.Parameters.AddWithValue("$p", file.ProviderId);
            insertFile.Parameters.AddWithValue("$pm", file.ProviderModId);
            insertFile.Parameters.AddWithValue("$pf", file.ProviderFileId);
            insertFile.Parameters.AddWithValue("$n", file.Name);
            insertFile.Parameters.AddWithValue("$fn", file.FileName);
            insertFile.Parameters.AddWithValue("$c", file.Category.ToString());
            insertFile.Parameters.AddWithValue("$v", DbValue(file.Version));
            insertFile.Parameters.AddWithValue("$size", DbValue(file.SizeBytes));
            insertFile.Parameters.AddWithValue("$d", DbValue(file.Description));
            insertFile.Parameters.AddWithValue("$uploaded", DbDate(file.UploadedAt));
            insertFile.Parameters.AddWithValue("$required", file.Required ? 1 : 0);
            insertFile.Parameters.AddWithValue("$recommended", file.Recommended ? 1 : 0);
            insertFile.Parameters.AddWithValue("$deps", JsonSerializer.Serialize(file.Dependencies ?? []));
            await insertFile.ExecuteNonQueryAsync(ct);
        }

        await using (var provenance = connection.CreateCommand())
        {
            provenance.Transaction = transaction;
            provenance.CommandText = """
            INSERT INTO catalog_provenance(canonical_id,provider_id,provider_mod_id,source_url,observed_at,source_fingerprint)
            VALUES($id,$p,$pm,$url,$observed,$fingerprint)
            ON CONFLICT(canonical_id) DO UPDATE SET
                provider_id=excluded.provider_id,
                provider_mod_id=excluded.provider_mod_id,
                source_url=excluded.source_url,
                observed_at=excluded.observed_at,
                source_fingerprint=excluded.source_fingerprint
            """;
            provenance.Parameters.AddWithValue("$id", mod.CanonicalId);
            provenance.Parameters.AddWithValue("$p", mod.ProviderId);
            provenance.Parameters.AddWithValue("$pm", mod.ProviderModId);
            provenance.Parameters.AddWithValue("$url", sourceUrl);
            provenance.Parameters.AddWithValue("$observed", cached.Cache.FetchedAt.ToString("O", CultureInfo.InvariantCulture));
            provenance.Parameters.AddWithValue("$fingerprint", DbValue(cached.Cache.SourceFingerprint));
            await provenance.ExecuteNonQueryAsync(ct);
        }

        await using (var deleteFts = connection.CreateCommand())
        {
            deleteFts.Transaction = transaction;
            deleteFts.CommandText = "DELETE FROM catalog_items_fts WHERE canonical_id=$id";
            deleteFts.Parameters.AddWithValue("$id", mod.CanonicalId);
            await deleteFts.ExecuteNonQueryAsync(ct);
        }

        await using (var insertFts = connection.CreateCommand())
        {
            insertFts.Transaction = transaction;
            insertFts.CommandText = """
            INSERT INTO catalog_items_fts(canonical_id,name,author,summary,tags,category,description)
            VALUES($id,$name,$author,$summary,$tags,$category,$description)
            """;
            insertFts.Parameters.AddWithValue("$id", mod.CanonicalId);
            insertFts.Parameters.AddWithValue("$name", mod.Name);
            insertFts.Parameters.AddWithValue("$author", mod.Author);
            insertFts.Parameters.AddWithValue("$summary", mod.Summary);
            insertFts.Parameters.AddWithValue("$tags", string.Join(' ', mod.Tags));
            insertFts.Parameters.AddWithValue("$category", mod.Category ?? string.Empty);
            insertFts.Parameters.AddWithValue("$description", mod.Description);
            await insertFts.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<CachedCatalogMod?> GetCachedCatalogModAsync(
        string providerId,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}; mod={providerModId}");
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerModId);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT canonical_id
        FROM catalog_items
        WHERE provider_id=$p AND provider_mod_id=$m
        LIMIT 1
        """;
        command.Parameters.AddWithValue("$p", providerId);
        command.Parameters.AddWithValue("$m", providerModId);
        var canonicalId = await command.ExecuteScalarAsync(ct) as string;
        return canonicalId is null ? null : await ReadCachedCatalogModAsync(connection, canonicalId, ct);
    }

    public async Task<IReadOnlyList<CachedCatalogMod>> SearchCachedCatalogModsAsync(
        CatalogSearchRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={request.GameId}; query={request.Query}");
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GameId);

        var limit = Math.Clamp(request.Limit, 1, 500);
        var providerIds = request.ProviderIds?
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        var ftsQuery = BuildFtsQuery(request.Query);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        var sql = new StringBuilder();
        if (ftsQuery is null)
        {
            sql.Append("SELECT i.canonical_id FROM catalog_items i WHERE i.game_id=$game");
        }
        else
        {
            sql.Append("""
                SELECT i.canonical_id
                FROM catalog_items_fts f
                JOIN catalog_items i ON i.canonical_id=f.canonical_id
                WHERE catalog_items_fts MATCH $query AND i.game_id=$game
                """);
            command.Parameters.AddWithValue("$query", ftsQuery);
        }

        command.Parameters.AddWithValue("$game", request.GameId);
        if (providerIds.Length > 0)
        {
            sql.Append(" AND i.provider_id IN (");
            for (var i = 0; i < providerIds.Length; i++)
            {
                if (i > 0) sql.Append(',');
                var parameterName = "$provider" + i.ToString(CultureInfo.InvariantCulture);
                sql.Append(parameterName);
                command.Parameters.AddWithValue(parameterName, providerIds[i]);
            }
            sql.Append(')');
        }

        if (!request.IncludeStale)
        {
            sql.Append(" AND (i.expires_at IS NULL OR i.expires_at>$now)");
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }

        sql.Append(ftsQuery is null
            ? " ORDER BY COALESCE(i.updated_at,i.fetched_at) DESC, i.downloads DESC LIMIT $limit"
            : " ORDER BY bm25(catalog_items_fts), COALESCE(i.updated_at,i.fetched_at) DESC LIMIT $limit");
        command.Parameters.AddWithValue("$limit", limit);
        command.CommandText = sql.ToString();

        var canonicalIds = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                canonicalIds.Add(reader.GetString(0));
        }

        var results = new List<CachedCatalogMod>(canonicalIds.Count);
        foreach (var canonicalId in canonicalIds)
        {
            ct.ThrowIfCancellationRequested();
            var cached = await ReadCachedCatalogModAsync(connection, canonicalId, ct);
            if (cached is not null) results.Add(cached);
        }
        return results;
    }

    public async Task<CatalogProvenance?> GetCatalogProvenanceAsync(
        string providerId,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}; mod={providerModId}");
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT canonical_id,provider_id,provider_mod_id,source_url,observed_at,source_fingerprint
        FROM catalog_provenance
        WHERE provider_id=$p AND provider_mod_id=$m
        LIMIT 1
        """;
        command.Parameters.AddWithValue("$p", providerId);
        command.Parameters.AddWithValue("$m", providerModId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    public async Task UpsertCatalogSyncStateAsync(CatalogSyncState state, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={state.ProviderId}; scope={state.ScopeKey}");
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ScopeKey);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO catalog_sync_state(provider_id,scope_key,cursor,last_success_at,last_attempt_at,last_error,etag,last_modified)
        VALUES($p,$s,$c,$success,$attempt,$error,$etag,$lastmod)
        ON CONFLICT(provider_id,scope_key) DO UPDATE SET
            cursor=excluded.cursor,
            last_success_at=excluded.last_success_at,
            last_attempt_at=excluded.last_attempt_at,
            last_error=excluded.last_error,
            etag=excluded.etag,
            last_modified=excluded.last_modified
        """;
        command.Parameters.AddWithValue("$p", state.ProviderId);
        command.Parameters.AddWithValue("$s", state.ScopeKey);
        command.Parameters.AddWithValue("$c", DbValue(state.Cursor));
        command.Parameters.AddWithValue("$success", DbDate(state.LastSuccessAt));
        command.Parameters.AddWithValue("$attempt", DbDate(state.LastAttemptAt));
        command.Parameters.AddWithValue("$error", DbValue(state.LastError));
        command.Parameters.AddWithValue("$etag", DbValue(state.ETag));
        command.Parameters.AddWithValue("$lastmod", DbDate(state.LastModified));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<CatalogSyncState?> GetCatalogSyncStateAsync(
        string providerId,
        string scopeKey,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}; scope={scopeKey}");
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT provider_id,scope_key,cursor,last_success_at,last_attempt_at,last_error,etag,last_modified
        FROM catalog_sync_state
        WHERE provider_id=$p AND scope_key=$s
        """;
        command.Parameters.AddWithValue("$p", providerId);
        command.Parameters.AddWithValue("$s", scopeKey);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            ReadDate(reader, 3),
            ReadDate(reader, 4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            ReadDate(reader, 7));
    }

    public async Task UpsertCatalogRateStateAsync(CatalogProviderHealth health, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={health.ProviderId}; state={health.State}");
        ArgumentNullException.ThrowIfNull(health);
        ArgumentException.ThrowIfNullOrWhiteSpace(health.ProviderId);
        var rate = health.RateLimit;

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO catalog_rate_state(
            provider_id,state,message,hourly_limit,hourly_remaining,daily_limit,daily_remaining,retry_after,observed_at,checked_at)
        VALUES($p,$state,$message,$hl,$hr,$dl,$dr,$retry,$observed,$checked)
        ON CONFLICT(provider_id) DO UPDATE SET
            state=excluded.state,
            message=excluded.message,
            hourly_limit=excluded.hourly_limit,
            hourly_remaining=excluded.hourly_remaining,
            daily_limit=excluded.daily_limit,
            daily_remaining=excluded.daily_remaining,
            retry_after=excluded.retry_after,
            observed_at=excluded.observed_at,
            checked_at=excluded.checked_at
        """;
        command.Parameters.AddWithValue("$p", health.ProviderId);
        command.Parameters.AddWithValue("$state", health.State.ToString());
        command.Parameters.AddWithValue("$message", health.Message);
        command.Parameters.AddWithValue("$hl", DbValue(rate?.HourlyLimit));
        command.Parameters.AddWithValue("$hr", DbValue(rate?.HourlyRemaining));
        command.Parameters.AddWithValue("$dl", DbValue(rate?.DailyLimit));
        command.Parameters.AddWithValue("$dr", DbValue(rate?.DailyRemaining));
        command.Parameters.AddWithValue("$retry", DbDate(rate?.RetryAfter));
        command.Parameters.AddWithValue("$observed", DbDate(rate?.ObservedAt));
        command.Parameters.AddWithValue("$checked", DbDate(health.CheckedAt));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<CatalogProviderHealth?> GetCatalogRateStateAsync(string providerId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}");
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT provider_id,state,message,hourly_limit,hourly_remaining,daily_limit,daily_remaining,retry_after,observed_at,checked_at
        FROM catalog_rate_state
        WHERE provider_id=$p
        """;
        command.Parameters.AddWithValue("$p", providerId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var rate = reader.IsDBNull(3) && reader.IsDBNull(4) && reader.IsDBNull(5) && reader.IsDBNull(6) &&
                   reader.IsDBNull(7) && reader.IsDBNull(8)
            ? null
            : new CatalogRateLimit(
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                ReadDate(reader, 7),
                ReadDate(reader, 8));

        return new(
            reader.GetString(0),
            Enum.Parse<CatalogProviderState>(reader.GetString(1), true),
            reader.GetString(2),
            rate,
            ReadDate(reader, 9));
    }

    public async Task UpsertCatalogLinkAsync(CatalogLink link, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"left={link.LeftCanonicalId}; right={link.RightCanonicalId}");
        ArgumentNullException.ThrowIfNull(link);
        ArgumentException.ThrowIfNullOrWhiteSpace(link.LeftCanonicalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(link.RightCanonicalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(link.EvidenceValue);
        if (string.Equals(link.LeftCanonicalId, link.RightCanonicalId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A catalog item cannot link to itself.", nameof(link));

        var left = string.Compare(link.LeftCanonicalId, link.RightCanonicalId, StringComparison.OrdinalIgnoreCase) <= 0
            ? link.LeftCanonicalId
            : link.RightCanonicalId;
        var right = string.Equals(left, link.LeftCanonicalId, StringComparison.Ordinal)
            ? link.RightCanonicalId
            : link.LeftCanonicalId;

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO catalog_links(left_canonical_id,right_canonical_id,evidence_kind,evidence_value,observed_at)
        VALUES($l,$r,$k,$v,$o)
        ON CONFLICT(left_canonical_id,right_canonical_id,evidence_kind,evidence_value)
        DO UPDATE SET observed_at=excluded.observed_at
        """;
        command.Parameters.AddWithValue("$l", left);
        command.Parameters.AddWithValue("$r", right);
        command.Parameters.AddWithValue("$k", link.EvidenceKind.ToString());
        command.Parameters.AddWithValue("$v", link.EvidenceValue);
        command.Parameters.AddWithValue("$o", link.ObservedAt.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CatalogLink>> GetCatalogLinksAsync(string canonicalId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"canonical={canonicalId}");
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT left_canonical_id,right_canonical_id,evidence_kind,evidence_value,observed_at
        FROM catalog_links
        WHERE left_canonical_id=$id OR right_canonical_id=$id
        ORDER BY observed_at DESC
        """;
        command.Parameters.AddWithValue("$id", canonicalId);
        var result = new List<CatalogLink>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                Enum.Parse<CatalogLinkEvidenceKind>(reader.GetString(2), true),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture)));
        }
        return result;
    }

    private static async Task<CachedCatalogMod?> ReadCachedCatalogModAsync(
        SqliteConnection connection,
        string canonicalId,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"canonical={canonicalId}");
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT canonical_id,provider_id,provider_mod_id,game_id,name,summary,description,author,version,category,
               tags_json,thumbnail,screenshots_json,created_at,updated_at,downloads,endorsements,rating,dependencies_json,
               source_url,fetched_at,expires_at,etag,last_modified,source_fingerprint
        FROM catalog_items
        WHERE canonical_id=$id
        """;
        command.Parameters.AddWithValue("$id", canonicalId);

        CatalogMod modWithoutFiles;
        CatalogCacheMetadata cache;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return null;

            modWithoutFiles = new CatalogMod(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                JsonSerializer.Deserialize<string[]>(reader.GetString(10)) ?? [],
                reader.IsDBNull(11) ? null : reader.GetString(11),
                JsonSerializer.Deserialize<CatalogImage[]>(reader.GetString(12)) ?? [],
                ReadDate(reader, 13),
                ReadDate(reader, 14),
                reader.IsDBNull(15) ? null : reader.GetInt64(15),
                reader.IsDBNull(16) ? null : reader.GetInt64(16),
                reader.IsDBNull(17) ? null : reader.GetDouble(17),
                JsonSerializer.Deserialize<CatalogDependency[]>(reader.GetString(18)) ?? [],
                reader.GetString(19),
                [],
                ProviderMetadata: null);
            cache = new CatalogCacheMetadata(
                DateTimeOffset.Parse(reader.GetString(20), CultureInfo.InvariantCulture),
                ReadDate(reader, 21),
                reader.IsDBNull(22) ? null : reader.GetString(22),
                ReadDate(reader, 23),
                reader.IsDBNull(24) ? null : reader.GetString(24));
        }

        var files = await ReadCatalogFilesAsync(connection, canonicalId, ct);
        return new(modWithoutFiles with { Files = files }, cache);
    }

    private static async Task<IReadOnlyList<CatalogModFile>> ReadCatalogFilesAsync(
        SqliteConnection connection,
        string canonicalId,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"canonical={canonicalId}");
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT provider_id,provider_mod_id,provider_file_id,name,file_name,category,version,size_bytes,description,
               uploaded_at,required,recommended,dependencies_json
        FROM catalog_files
        WHERE canonical_id=$id
        ORDER BY uploaded_at DESC, provider_file_id
        """;
        command.Parameters.AddWithValue("$id", canonicalId);
        var files = new List<CatalogModFile>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            files.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                Enum.Parse<CatalogFileCategory>(reader.GetString(5), true),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                ReadDate(reader, 9),
                reader.GetInt64(10) != 0,
                reader.GetInt64(11) != 0,
                JsonSerializer.Deserialize<CatalogDependency[]>(reader.GetString(12)) ?? [],
                ProviderMetadata: null));
        }
        return files;
    }

    private static string? BuildFtsQuery(string? query)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(query)) return null;
        var terms = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(raw => new string(raw.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-').ToArray()))
            .Where(term => term.Length > 0)
            .Select(term => """ + term.Replace(""", """", StringComparison.Ordinal) + ""*")
            .ToArray();
        return terms.Length == 0 ? null : string.Join(" AND ", terms);
    }

    private static object DbValue<T>(T? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value is null ? DBNull.Value : value;
    }

    private static object DbDate(DateTimeOffset? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value is null ? DBNull.Value : value.Value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ReadDate(SqliteDataReader reader, int ordinal)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
    }

    private static string EnsureDurableUrl(string url, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var durable = TryGetDurableUrl(url);
        return durable ?? throw new InvalidDataException($"{parameterName} must be a durable non-secret HTTP(S) source URL.");
    }

    private static string? TryGetDurableUrl(string? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return null;

        var query = uri.Query.AsSpan();
        if (query.IsEmpty) return uri.AbsoluteUri;

        var lower = uri.Query.ToLowerInvariant();
        string[] forbidden =
        [
            "token=", "access_token=", "apikey=", "api_key=", "key=", "signature=", "sig=", "expires=",
            "x-amz-", "x-goog-", "authorization=", "auth=", "jwt="
        ];
        return forbidden.Any(token => lower.Contains(token, StringComparison.Ordinal)) ? null : uri.AbsoluteUri;
    }
}
