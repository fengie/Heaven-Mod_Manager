using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;

namespace MhwModManager.Storage;

public sealed class CatalogRepository(ManagerDatabase db)
{
    private const int MaxSearchResults = 500;
    private const string ItemColumns = """
        i.canonical_id,i.provider_id,i.provider_mod_id,i.game_id,i.name,i.summary,i.description,i.author,
        i.version,i.category,i.tags_json,i.screenshots_json,i.thumbnail,i.created_at,i.updated_at,
        i.downloads,i.endorsements,i.rating,i.dependencies_json,i.source_url,i.provider_metadata,
        i.fetched_at,i.expires_at,i.etag,i.last_modified,i.source_fingerprint
        """;

    public async Task UpsertSourceAsync(
        string displayName,
        CatalogProviderCompliance compliance,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(compliance);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(compliance.ProviderId);

        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO catalog_sources(
                provider_id,display_name,source_kind,documentation_url,terms_url,terms_reviewed_on,review_interval_days,
                allows_catalog_discovery,allows_direct_download,allows_html_parsing,attribution_required,
                robots_url,robots_reviewed_on,attribution_text,disabled_reason,updated_at)
            VALUES(
                $provider,$display,$kind,$docs,$terms,$termsReviewed,$reviewDays,
                $discovery,$direct,$html,$attributionRequired,
                $robots,$robotsReviewed,$attribution,$disabled,$updated)
            ON CONFLICT(provider_id) DO UPDATE SET
                display_name=excluded.display_name,
                source_kind=excluded.source_kind,
                documentation_url=excluded.documentation_url,
                terms_url=excluded.terms_url,
                terms_reviewed_on=excluded.terms_reviewed_on,
                review_interval_days=excluded.review_interval_days,
                allows_catalog_discovery=excluded.allows_catalog_discovery,
                allows_direct_download=excluded.allows_direct_download,
                allows_html_parsing=excluded.allows_html_parsing,
                attribution_required=excluded.attribution_required,
                robots_url=excluded.robots_url,
                robots_reviewed_on=excluded.robots_reviewed_on,
                attribution_text=excluded.attribution_text,
                disabled_reason=excluded.disabled_reason,
                updated_at=excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("$provider", compliance.ProviderId);
        cmd.Parameters.AddWithValue("$display", displayName);
        cmd.Parameters.AddWithValue("$kind", compliance.SourceKind.ToString());
        cmd.Parameters.AddWithValue("$docs", compliance.DocumentationUri.AbsoluteUri);
        cmd.Parameters.AddWithValue("$terms", compliance.TermsUri.AbsoluteUri);
        cmd.Parameters.AddWithValue("$termsReviewed", compliance.TermsReviewedOn.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$reviewDays", compliance.ReviewIntervalDays);
        cmd.Parameters.AddWithValue("$discovery", compliance.AllowsCatalogDiscovery ? 1 : 0);
        cmd.Parameters.AddWithValue("$direct", compliance.AllowsDirectDownload ? 1 : 0);
        cmd.Parameters.AddWithValue("$html", compliance.AllowsHtmlParsing ? 1 : 0);
        cmd.Parameters.AddWithValue("$attributionRequired", compliance.AttributionRequired ? 1 : 0);
        cmd.Parameters.AddWithValue("$robots", DbValue(compliance.RobotsUri?.AbsoluteUri));
        cmd.Parameters.AddWithValue("$robotsReviewed", DbValue(compliance.RobotsReviewedOn?.ToString("O", CultureInfo.InvariantCulture)));
        cmd.Parameters.AddWithValue("$attribution", DbValue(compliance.AttributionText));
        cmd.Parameters.AddWithValue("$disabled", DbValue(compliance.DisabledReason));
        cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertAsync(CachedCatalogMod cached, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(cached);
        ValidateCachedMod(cached);

        await using var c = await db.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

        await EnsureSourceExistsAsync(c, tx, cached.Mod.ProviderId, ct);
        await UpsertItemAsync(c, tx, cached, ct);
        await ReplaceFilesAsync(c, tx, cached.Mod, ct);
        await UpsertProvenanceAsync(c, tx, cached, ct);
        await ReindexAsync(c, tx, cached.Mod, ct);

        await tx.CommitAsync(ct);
    }

    public async Task<CachedCatalogMod?> GetAsync(string canonicalId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalId);

        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {ItemColumns} FROM catalog_items i WHERE i.canonical_id=$id";
        cmd.Parameters.AddWithValue("$id", canonicalId);
        var rows = await ReadRowsAsync(cmd, ct);
        if (rows.Count == 0) return null;

        return await MaterializeAsync(c, rows[0], ct);
    }

    public async Task<IReadOnlyList<CachedCatalogMod>> SearchAsync(
        string? query,
        string? providerId = null,
        string? gameId = null,
        bool includeStale = true,
        int limit = 100,
        DateTimeOffset? now = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var boundedLimit = Math.Clamp(limit, 1, MaxSearchResults);
        var ftsQuery = BuildFtsQuery(query);

        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();

        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            where.Add("i.provider_id=$provider");
            cmd.Parameters.AddWithValue("$provider", providerId);
        }
        if (!string.IsNullOrWhiteSpace(gameId))
        {
            where.Add("i.game_id=$game");
            cmd.Parameters.AddWithValue("$game", gameId);
        }
        if (!includeStale)
        {
            where.Add("(i.expires_at IS NULL OR i.expires_at>$now)");
            cmd.Parameters.AddWithValue("$now", (now ?? DateTimeOffset.UtcNow).ToString("O", CultureInfo.InvariantCulture));
        }

        var suffix = where.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", where);
        if (ftsQuery is null)
        {
            var plainWhere = where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where);
            cmd.CommandText = $"""
                SELECT {ItemColumns}
                FROM catalog_items i
                {plainWhere}
                ORDER BY COALESCE(i.updated_at,i.fetched_at) DESC,i.name COLLATE NOCASE
                LIMIT $limit
                """;
        }
        else
        {
            cmd.CommandText = $"""
                SELECT {ItemColumns}
                FROM catalog_items_fts
                JOIN catalog_items i ON i.canonical_id=catalog_items_fts.canonical_id
                WHERE catalog_items_fts MATCH $query{suffix}
                ORDER BY bm25(catalog_items_fts),COALESCE(i.updated_at,i.fetched_at) DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$query", ftsQuery);
        }
        cmd.Parameters.AddWithValue("$limit", boundedLimit);

        var rows = await ReadRowsAsync(cmd, ct);
        var result = new List<CachedCatalogMod>(rows.Count);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            result.Add(await MaterializeAsync(c, row, ct));
        }
        return result;
    }

    private static async Task EnsureSourceExistsAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        string providerId,
        CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM catalog_sources WHERE provider_id=$provider LIMIT 1";
        cmd.Parameters.AddWithValue("$provider", providerId);
        if (await cmd.ExecuteScalarAsync(ct) is null)
            throw new InvalidOperationException($"Catalog source '{providerId}' must be registered before catalog items are persisted.");
    }

    private static async Task UpsertItemAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        CachedCatalogMod cached,
        CancellationToken ct)
    {
        var mod = cached.Mod;
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO catalog_items(
                canonical_id,provider_id,provider_mod_id,game_id,name,summary,description,author,version,category,
                tags_json,screenshots_json,thumbnail,created_at,updated_at,downloads,endorsements,rating,dependencies_json,
                source_url,provider_metadata,fetched_at,expires_at,etag,last_modified,source_fingerprint)
            VALUES(
                $canonical,$provider,$providerMod,$game,$name,$summary,$description,$author,$version,$category,
                $tags,$screenshots,$thumbnail,$created,$updated,$downloads,$endorsements,$rating,$dependencies,
                $sourceUrl,$metadata,$fetched,$expires,$etag,$lastModified,$fingerprint)
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
                screenshots_json=excluded.screenshots_json,
                thumbnail=excluded.thumbnail,
                created_at=excluded.created_at,
                updated_at=excluded.updated_at,
                downloads=excluded.downloads,
                endorsements=excluded.endorsements,
                rating=excluded.rating,
                dependencies_json=excluded.dependencies_json,
                source_url=excluded.source_url,
                provider_metadata=excluded.provider_metadata,
                fetched_at=excluded.fetched_at,
                expires_at=excluded.expires_at,
                etag=excluded.etag,
                last_modified=excluded.last_modified,
                source_fingerprint=excluded.source_fingerprint
            """;
        cmd.Parameters.AddWithValue("$canonical", mod.CanonicalId);
        cmd.Parameters.AddWithValue("$provider", mod.ProviderId);
        cmd.Parameters.AddWithValue("$providerMod", mod.ProviderModId);
        cmd.Parameters.AddWithValue("$game", mod.GameId);
        cmd.Parameters.AddWithValue("$name", mod.Name);
        cmd.Parameters.AddWithValue("$summary", mod.Summary);
        cmd.Parameters.AddWithValue("$description", mod.Description);
        cmd.Parameters.AddWithValue("$author", mod.Author);
        cmd.Parameters.AddWithValue("$version", DbValue(mod.Version));
        cmd.Parameters.AddWithValue("$category", DbValue(mod.Category));
        cmd.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(mod.Tags));
        cmd.Parameters.AddWithValue("$screenshots", JsonSerializer.Serialize(mod.Screenshots));
        cmd.Parameters.AddWithValue("$thumbnail", DbValue(mod.Thumbnail));
        cmd.Parameters.AddWithValue("$created", DbValue(Format(mod.CreatedAt)));
        cmd.Parameters.AddWithValue("$updated", DbValue(Format(mod.UpdatedAt)));
        cmd.Parameters.AddWithValue("$downloads", DbValue(mod.Downloads));
        cmd.Parameters.AddWithValue("$endorsements", DbValue(mod.Endorsements));
        cmd.Parameters.AddWithValue("$rating", DbValue(mod.Rating));
        cmd.Parameters.AddWithValue("$dependencies", JsonSerializer.Serialize(mod.Dependencies));
        cmd.Parameters.AddWithValue("$sourceUrl", mod.SourceUrl);
        cmd.Parameters.AddWithValue("$metadata", DbValue(mod.ProviderMetadata));
        cmd.Parameters.AddWithValue("$fetched", cached.Cache.FetchedAt.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$expires", DbValue(Format(cached.Cache.ExpiresAt)));
        cmd.Parameters.AddWithValue("$etag", DbValue(cached.Cache.ETag));
        cmd.Parameters.AddWithValue("$lastModified", DbValue(Format(cached.Cache.LastModified)));
        cmd.Parameters.AddWithValue("$fingerprint", DbValue(cached.Cache.SourceFingerprint));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ReplaceFilesAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        CatalogMod mod,
        CancellationToken ct)
    {
        await using (var delete = c.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM catalog_files WHERE canonical_id=$canonical";
            delete.Parameters.AddWithValue("$canonical", mod.CanonicalId);
            await delete.ExecuteNonQueryAsync(ct);
        }

        if (mod.Files.Count == 0) return;

        await using var insert = c.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO catalog_files(
                provider_id,provider_mod_id,provider_file_id,canonical_id,name,file_name,category,version,size_bytes,
                description,uploaded_at,required,recommended,dependencies_json,provider_metadata)
            VALUES(
                $provider,$providerMod,$providerFile,$canonical,$name,$fileName,$category,$version,$size,
                $description,$uploaded,$required,$recommended,$dependencies,$metadata)
            """;
        var pProvider = insert.Parameters.Add("$provider", SqliteType.Text);
        var pProviderMod = insert.Parameters.Add("$providerMod", SqliteType.Text);
        var pProviderFile = insert.Parameters.Add("$providerFile", SqliteType.Text);
        var pCanonical = insert.Parameters.Add("$canonical", SqliteType.Text);
        var pName = insert.Parameters.Add("$name", SqliteType.Text);
        var pFileName = insert.Parameters.Add("$fileName", SqliteType.Text);
        var pCategory = insert.Parameters.Add("$category", SqliteType.Text);
        var pVersion = insert.Parameters.Add("$version", SqliteType.Text);
        var pSize = insert.Parameters.Add("$size", SqliteType.Integer);
        var pDescription = insert.Parameters.Add("$description", SqliteType.Text);
        var pUploaded = insert.Parameters.Add("$uploaded", SqliteType.Text);
        var pRequired = insert.Parameters.Add("$required", SqliteType.Integer);
        var pRecommended = insert.Parameters.Add("$recommended", SqliteType.Integer);
        var pDependencies = insert.Parameters.Add("$dependencies", SqliteType.Text);
        var pMetadata = insert.Parameters.Add("$metadata", SqliteType.Text);
        insert.Prepare();

        foreach (var file in mod.Files)
        {
            ct.ThrowIfCancellationRequested();
            pProvider.Value = file.ProviderId;
            pProviderMod.Value = file.ProviderModId;
            pProviderFile.Value = file.ProviderFileId;
            pCanonical.Value = mod.CanonicalId;
            pName.Value = file.Name;
            pFileName.Value = file.FileName;
            pCategory.Value = file.Category.ToString();
            pVersion.Value = DbValue(file.Version);
            pSize.Value = DbValue(file.SizeBytes);
            pDescription.Value = DbValue(file.Description);
            pUploaded.Value = DbValue(Format(file.UploadedAt));
            pRequired.Value = file.Required ? 1 : 0;
            pRecommended.Value = file.Recommended ? 1 : 0;
            pDependencies.Value = JsonSerializer.Serialize(file.Dependencies ?? Array.Empty<CatalogDependency>());
            pMetadata.Value = DbValue(file.ProviderMetadata);
            await insert.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task UpsertProvenanceAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        CachedCatalogMod cached,
        CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO catalog_provenance(
                canonical_id,provider_id,source_url,fetched_at,etag,last_modified,source_fingerprint)
            VALUES($canonical,$provider,$source,$fetched,$etag,$lastModified,$fingerprint)
            ON CONFLICT(canonical_id,provider_id) DO UPDATE SET
                source_url=excluded.source_url,
                fetched_at=excluded.fetched_at,
                etag=excluded.etag,
                last_modified=excluded.last_modified,
                source_fingerprint=excluded.source_fingerprint
            """;
        cmd.Parameters.AddWithValue("$canonical", cached.Mod.CanonicalId);
        cmd.Parameters.AddWithValue("$provider", cached.Mod.ProviderId);
        cmd.Parameters.AddWithValue("$source", cached.Mod.SourceUrl);
        cmd.Parameters.AddWithValue("$fetched", cached.Cache.FetchedAt.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$etag", DbValue(cached.Cache.ETag));
        cmd.Parameters.AddWithValue("$lastModified", DbValue(Format(cached.Cache.LastModified)));
        cmd.Parameters.AddWithValue("$fingerprint", DbValue(cached.Cache.SourceFingerprint));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ReindexAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        CatalogMod mod,
        CancellationToken ct)
    {
        await using (var delete = c.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM catalog_items_fts WHERE canonical_id=$canonical";
            delete.Parameters.AddWithValue("$canonical", mod.CanonicalId);
            await delete.ExecuteNonQueryAsync(ct);
        }

        await using var insert = c.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO catalog_items_fts(canonical_id,name,author,summary,tags,category,description)
            VALUES($canonical,$name,$author,$summary,$tags,$category,$description)
            """;
        insert.Parameters.AddWithValue("$canonical", mod.CanonicalId);
        insert.Parameters.AddWithValue("$name", mod.Name);
        insert.Parameters.AddWithValue("$author", mod.Author);
        insert.Parameters.AddWithValue("$summary", mod.Summary);
        insert.Parameters.AddWithValue("$tags", string.Join(' ', mod.Tags));
        insert.Parameters.AddWithValue("$category", mod.Category ?? string.Empty);
        insert.Parameters.AddWithValue("$description", mod.Description);
        await insert.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<StoredItem>> ReadRowsAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var rows = new List<StoredItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new StoredItem(
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
                reader.GetString(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetInt64(15),
                reader.IsDBNull(16) ? null : reader.GetInt64(16),
                reader.IsDBNull(17) ? null : reader.GetDouble(17),
                reader.GetString(18),
                reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                reader.GetString(21),
                reader.IsDBNull(22) ? null : reader.GetString(22),
                reader.IsDBNull(23) ? null : reader.GetString(23),
                reader.IsDBNull(24) ? null : reader.GetString(24),
                reader.IsDBNull(25) ? null : reader.GetString(25)));
        }
        return rows;
    }

    private static async Task<CachedCatalogMod> MaterializeAsync(
        SqliteConnection c,
        StoredItem row,
        CancellationToken ct)
    {
        var files = await LoadFilesAsync(c, row.CanonicalId, ct);
        var mod = new CatalogMod(
            row.CanonicalId,
            row.ProviderId,
            row.ProviderModId,
            row.GameId,
            row.Name,
            row.Summary,
            row.Description,
            row.Author,
            row.Version,
            row.Category,
            Deserialize<IReadOnlyList<string>>(row.TagsJson),
            row.Thumbnail,
            Deserialize<IReadOnlyList<CatalogImage>>(row.ScreenshotsJson),
            ParseDate(row.CreatedAt),
            ParseDate(row.UpdatedAt),
            row.Downloads,
            row.Endorsements,
            row.Rating,
            Deserialize<IReadOnlyList<CatalogDependency>>(row.DependenciesJson),
            row.SourceUrl,
            files,
            row.ProviderMetadata);

        var cache = new CatalogCacheMetadata(
            ParseRequiredDate(row.FetchedAt),
            ParseDate(row.ExpiresAt),
            row.ETag,
            ParseDate(row.LastModified),
            row.SourceFingerprint);
        return new CachedCatalogMod(mod, cache);
    }

    private static async Task<IReadOnlyList<CatalogModFile>> LoadFilesAsync(
        SqliteConnection c,
        string canonicalId,
        CancellationToken ct)
    {
        var files = new List<CatalogModFile>();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT provider_id,provider_mod_id,provider_file_id,name,file_name,category,version,size_bytes,
                   description,uploaded_at,required,recommended,dependencies_json,provider_metadata
            FROM catalog_files
            WHERE canonical_id=$canonical
            ORDER BY rowid
            """;
        cmd.Parameters.AddWithValue("$canonical", canonicalId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var categoryText = reader.GetString(5);
            var category = Enum.TryParse<CatalogFileCategory>(categoryText, true, out var parsed)
                ? parsed
                : CatalogFileCategory.Unknown;
            files.Add(new CatalogModFile(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                category,
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : ParseDate(reader.GetString(9)),
                reader.GetInt64(10) != 0,
                reader.GetInt64(11) != 0,
                Deserialize<IReadOnlyList<CatalogDependency>>(reader.GetString(12)),
                reader.IsDBNull(13) ? null : reader.GetString(13)));
        }
        return files;
    }

    private static string? BuildFtsQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return null;
        return string.Join(" AND ", tokens.Select(token => $"\"{token.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));
    }

    private static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json)
                ?? throw new InvalidDataException("Catalog cache JSON field was null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Catalog cache JSON field is malformed.", ex);
        }
    }

    private static void ValidateCachedMod(CachedCatalogMod cached)
    {
        var mod = cached.Mod;
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.CanonicalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.ProviderModId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.GameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.SourceUrl);

        var fileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in mod.Files)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(file.ProviderId, mod.ProviderId)
                || !StringComparer.OrdinalIgnoreCase.Equals(file.ProviderModId, mod.ProviderModId))
            {
                throw new InvalidDataException("Catalog file provider identity does not match its parent mod.");
            }
            if (!fileIds.Add(file.ProviderFileId))
                throw new InvalidDataException($"Catalog mod contains duplicate provider file id '{file.ProviderFileId}'.");
        }
    }

    private static string? Format(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static DateTimeOffset ParseRequiredDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static object DbValue(object? value) => value ?? DBNull.Value;

    private sealed record StoredItem(
        string CanonicalId,
        string ProviderId,
        string ProviderModId,
        string GameId,
        string Name,
        string Summary,
        string Description,
        string Author,
        string? Version,
        string? Category,
        string TagsJson,
        string ScreenshotsJson,
        string? Thumbnail,
        string? CreatedAt,
        string? UpdatedAt,
        long? Downloads,
        long? Endorsements,
        double? Rating,
        string DependenciesJson,
        string SourceUrl,
        string? ProviderMetadata,
        string FetchedAt,
        string? ExpiresAt,
        string? ETag,
        string? LastModified,
        string? SourceFingerprint);
}
