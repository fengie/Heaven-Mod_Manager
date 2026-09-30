using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MhwModManager.Core;

public enum CatalogSyncFailureKind
{
    None,
    Offline,
    AuthenticationRequired,
    RateLimited,
    Timeout,
    SchemaDrift,
    ProviderError,
    Cancelled
}

public sealed record CatalogSyncState(
    string ProviderId,
    string? Cursor,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    int ConsecutiveFailures,
    CatalogSyncFailureKind LastFailureKind = CatalogSyncFailureKind.None);

public sealed record CatalogProvenanceRecord(
    string CanonicalId,
    string ProviderId,
    string ProviderModId,
    string SourceUrl,
    DateTimeOffset FetchedAt,
    string? ETag,
    DateTimeOffset? LastModified,
    string? SourceFingerprint);

public sealed class CatalogSqliteStore
{
    private const int SchemaVersion = 1;
    private readonly string databasePath;
    private readonly string connectionString;

    public CatalogSqliteStore(string databasePath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (string.Equals(databasePath.Trim(), ":memory:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Catalog persistence requires a file-backed SQLite database.", nameof(databasePath));

        this.databasePath = Path.GetFullPath(databasePath);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this.databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;

            CREATE TABLE IF NOT EXISTS catalog_sources (
                provider_id TEXT PRIMARY KEY COLLATE NOCASE,
                source_kind INTEGER NOT NULL,
                display_name TEXT NOT NULL,
                documentation_url TEXT NOT NULL,
                terms_url TEXT NOT NULL,
                terms_reviewed_on TEXT NOT NULL,
                allows_catalog_discovery INTEGER NOT NULL,
                allows_direct_download INTEGER NOT NULL,
                allows_html_parsing INTEGER NOT NULL,
                enabled INTEGER NOT NULL,
                disabled_reason TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS catalog_items (
                canonical_id TEXT PRIMARY KEY COLLATE NOCASE,
                provider_id TEXT NOT NULL COLLATE NOCASE,
                provider_mod_id TEXT NOT NULL,
                game_id TEXT NOT NULL COLLATE NOCASE,
                name TEXT NOT NULL,
                summary TEXT NOT NULL,
                description TEXT NOT NULL,
                author TEXT NOT NULL,
                version TEXT NULL,
                category TEXT NULL,
                tags_json TEXT NOT NULL,
                thumbnail TEXT NULL,
                screenshots_json TEXT NOT NULL,
                created_at TEXT NULL,
                updated_at TEXT NULL,
                downloads INTEGER NULL,
                endorsements INTEGER NULL,
                rating REAL NULL,
                dependencies_json TEXT NOT NULL,
                source_url TEXT NOT NULL,
                fetched_at TEXT NOT NULL,
                expires_at TEXT NULL,
                etag TEXT NULL,
                last_modified TEXT NULL,
                source_fingerprint TEXT NULL,
                UNIQUE(provider_id, provider_mod_id),
                FOREIGN KEY(provider_id) REFERENCES catalog_sources(provider_id)
            );

            CREATE TABLE IF NOT EXISTS catalog_files (
                provider_id TEXT NOT NULL COLLATE NOCASE,
                provider_mod_id TEXT NOT NULL,
                provider_file_id TEXT NOT NULL,
                canonical_id TEXT NOT NULL COLLATE NOCASE,
                name TEXT NOT NULL,
                file_name TEXT NOT NULL,
                category INTEGER NOT NULL,
                version TEXT NULL,
                size_bytes INTEGER NULL,
                description TEXT NULL,
                uploaded_at TEXT NULL,
                required INTEGER NOT NULL,
                recommended INTEGER NOT NULL,
                dependencies_json TEXT NOT NULL,
                PRIMARY KEY(provider_id, provider_mod_id, provider_file_id),
                FOREIGN KEY(canonical_id) REFERENCES catalog_items(canonical_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS catalog_provenance (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                canonical_id TEXT NOT NULL COLLATE NOCASE,
                provider_id TEXT NOT NULL COLLATE NOCASE,
                provider_mod_id TEXT NOT NULL,
                source_url TEXT NOT NULL,
                fetched_at TEXT NOT NULL,
                etag TEXT NULL,
                last_modified TEXT NULL,
                source_fingerprint TEXT NULL,
                FOREIGN KEY(canonical_id) REFERENCES catalog_items(canonical_id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_catalog_provenance_item
                ON catalog_provenance(canonical_id, id DESC);

            CREATE TABLE IF NOT EXISTS catalog_sync_state (
                provider_id TEXT PRIMARY KEY COLLATE NOCASE,
                cursor TEXT NULL,
                last_attempt_at TEXT NULL,
                last_success_at TEXT NULL,
                consecutive_failures INTEGER NOT NULL,
                last_failure_kind INTEGER NOT NULL,
                FOREIGN KEY(provider_id) REFERENCES catalog_sources(provider_id)
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS catalog_items_fts USING fts5(
                canonical_id UNINDEXED,
                name,
                author,
                summary,
                tags,
                category,
                description,
                tokenize='unicode61 remove_diacritics 2'
            );
            """;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = $"PRAGMA user_version={SchemaVersion};";
        await versionCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertSourceAsync(
        CatalogProviderCompliance compliance,
        string displayName,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(compliance);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        CatalogProviderComplianceValidator.EnsureUsable(
            compliance,
            DateOnly.FromDateTime(DateTime.UtcNow));

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO catalog_sources (
                provider_id, source_kind, display_name, documentation_url, terms_url,
                terms_reviewed_on, allows_catalog_discovery, allows_direct_download,
                allows_html_parsing, enabled, disabled_reason
            ) VALUES (
                $provider_id, $source_kind, $display_name, $documentation_url, $terms_url,
                $terms_reviewed_on, $allows_catalog_discovery, $allows_direct_download,
                $allows_html_parsing, $enabled, $disabled_reason
            )
            ON CONFLICT(provider_id) DO UPDATE SET
                source_kind=excluded.source_kind,
                display_name=excluded.display_name,
                documentation_url=excluded.documentation_url,
                terms_url=excluded.terms_url,
                terms_reviewed_on=excluded.terms_reviewed_on,
                allows_catalog_discovery=excluded.allows_catalog_discovery,
                allows_direct_download=excluded.allows_direct_download,
                allows_html_parsing=excluded.allows_html_parsing,
                enabled=excluded.enabled,
                disabled_reason=excluded.disabled_reason;
            """;
        AddParameter(command, "$provider_id", NormalizeId(compliance.ProviderId, nameof(compliance.ProviderId)));
        AddParameter(command, "$source_kind", (int)compliance.SourceKind);
        AddParameter(command, "$display_name", displayName.Trim());
        AddParameter(command, "$documentation_url", compliance.DocumentationUri.AbsoluteUri);
        AddParameter(command, "$terms_url", compliance.TermsUri.AbsoluteUri);
        AddParameter(command, "$terms_reviewed_on", compliance.TermsReviewedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddParameter(command, "$allows_catalog_discovery", compliance.AllowsCatalogDiscovery ? 1 : 0);
        AddParameter(command, "$allows_direct_download", compliance.AllowsDirectDownload ? 1 : 0);
        AddParameter(command, "$allows_html_parsing", compliance.AllowsHtmlParsing ? 1 : 0);
        AddParameter(command, "$enabled", compliance.Enabled ? 1 : 0);
        AddParameter(command, "$disabled_reason", compliance.DisabledReason);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertModAsync(CachedCatalogMod cached, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(cached);
        ValidateCatalogMod(cached.Mod);

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await EnsureSourceRegisteredAsync(connection, cached.Mod.ProviderId, ct).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        await UpsertItemAsync(connection, transaction, cached, ct).ConfigureAwait(false);
        await ReplaceFilesAsync(connection, transaction, cached.Mod, ct).ConfigureAwait(false);
        await ReplaceFtsAsync(connection, transaction, cached.Mod, ct).ConfigureAwait(false);
        await InsertProvenanceAsync(connection, transaction, cached, ct).ConfigureAwait(false);

        transaction.Commit();
    }

    public async Task<CachedCatalogMod?> GetAsync(string canonicalId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalId);

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var row = await ReadSingleRowAsync(connection, canonicalId.Trim(), ct).ConfigureAwait(false);
        if (row is null)
            return null;

        return await MaterializeAsync(connection, row, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CachedCatalogMod>> SearchAsync(
        string? query,
        string? providerId = null,
        string? gameId = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit), "Catalog search limit must be between 1 and 500.");

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var rows = new List<CatalogRow>();
        await using (var command = connection.CreateCommand())
        {
            var hasQuery = !string.IsNullOrWhiteSpace(query);
            command.CommandText = hasQuery
                ? """
                    SELECT i.canonical_id, i.provider_id, i.provider_mod_id, i.game_id,
                           i.name, i.summary, i.description, i.author, i.version, i.category,
                           i.tags_json, i.thumbnail, i.screenshots_json, i.created_at, i.updated_at,
                           i.downloads, i.endorsements, i.rating, i.dependencies_json, i.source_url,
                           i.fetched_at, i.expires_at, i.etag, i.last_modified, i.source_fingerprint
                    FROM catalog_items_fts
                    JOIN catalog_items i ON i.canonical_id = catalog_items_fts.canonical_id
                    WHERE catalog_items_fts MATCH $query
                      AND ($provider_id IS NULL OR i.provider_id = $provider_id)
                      AND ($game_id IS NULL OR i.game_id = $game_id)
                    ORDER BY bm25(catalog_items_fts), COALESCE(i.updated_at, i.fetched_at) DESC
                    LIMIT $limit;
                    """
                : """
                    SELECT i.canonical_id, i.provider_id, i.provider_mod_id, i.game_id,
                           i.name, i.summary, i.description, i.author, i.version, i.category,
                           i.tags_json, i.thumbnail, i.screenshots_json, i.created_at, i.updated_at,
                           i.downloads, i.endorsements, i.rating, i.dependencies_json, i.source_url,
                           i.fetched_at, i.expires_at, i.etag, i.last_modified, i.source_fingerprint
                    FROM catalog_items i
                    WHERE ($provider_id IS NULL OR i.provider_id = $provider_id)
                      AND ($game_id IS NULL OR i.game_id = $game_id)
                    ORDER BY COALESCE(i.updated_at, i.fetched_at) DESC
                    LIMIT $limit;
                    """;

            AddParameter(command, "$query", hasQuery ? BuildFtsQuery(query!) : null);
            AddParameter(command, "$provider_id", string.IsNullOrWhiteSpace(providerId) ? null : NormalizeId(providerId, nameof(providerId)));
            AddParameter(command, "$game_id", string.IsNullOrWhiteSpace(gameId) ? null : gameId.Trim());
            AddParameter(command, "$limit", limit);

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                rows.Add(ReadRow(reader));
        }

        var results = new List<CachedCatalogMod>(rows.Count);
        foreach (var row in rows)
            results.Add(await MaterializeAsync(connection, row, ct).ConfigureAwait(false));
        return results;
    }

    public async Task UpsertSyncStateAsync(CatalogSyncState state, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(state);
        if (state.ConsecutiveFailures < 0)
            throw new ArgumentOutOfRangeException(nameof(state), "ConsecutiveFailures cannot be negative.");

        var providerId = NormalizeId(state.ProviderId, nameof(state.ProviderId));
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await EnsureSourceRegisteredAsync(connection, providerId, ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO catalog_sync_state (
                provider_id, cursor, last_attempt_at, last_success_at,
                consecutive_failures, last_failure_kind
            ) VALUES (
                $provider_id, $cursor, $last_attempt_at, $last_success_at,
                $consecutive_failures, $last_failure_kind
            )
            ON CONFLICT(provider_id) DO UPDATE SET
                cursor=excluded.cursor,
                last_attempt_at=excluded.last_attempt_at,
                last_success_at=excluded.last_success_at,
                consecutive_failures=excluded.consecutive_failures,
                last_failure_kind=excluded.last_failure_kind;
            """;
        AddParameter(command, "$provider_id", providerId);
        AddParameter(command, "$cursor", NormalizeCursor(state.Cursor));
        AddParameter(command, "$last_attempt_at", FormatDate(state.LastAttemptAt));
        AddParameter(command, "$last_success_at", FormatDate(state.LastSuccessAt));
        AddParameter(command, "$consecutive_failures", state.ConsecutiveFailures);
        AddParameter(command, "$last_failure_kind", (int)state.LastFailureKind);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<CatalogSyncState?> GetSyncStateAsync(string providerId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        providerId = NormalizeId(providerId, nameof(providerId));

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT provider_id, cursor, last_attempt_at, last_success_at,
                   consecutive_failures, last_failure_kind
            FROM catalog_sync_state
            WHERE provider_id = $provider_id;
            """;
        AddParameter(command, "$provider_id", providerId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return new CatalogSyncState(
            ProviderId: reader.GetString(0),
            Cursor: reader.IsDBNull(1) ? null : reader.GetString(1),
            LastAttemptAt: ParseDate(reader, 2),
            LastSuccessAt: ParseDate(reader, 3),
            ConsecutiveFailures: reader.GetInt32(4),
            LastFailureKind: (CatalogSyncFailureKind)reader.GetInt32(5));
    }

    public async Task<IReadOnlyList<CatalogProvenanceRecord>> GetProvenanceAsync(
        string canonicalId,
        int limit = 50,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalId);
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT canonical_id, provider_id, provider_mod_id, source_url,
                   fetched_at, etag, last_modified, source_fingerprint
            FROM catalog_provenance
            WHERE canonical_id = $canonical_id
            ORDER BY id DESC
            LIMIT $limit;
            """;
        AddParameter(command, "$canonical_id", canonicalId.Trim());
        AddParameter(command, "$limit", limit);

        var results = new List<CatalogProvenanceRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new CatalogProvenanceRecord(
                CanonicalId: reader.GetString(0),
                ProviderId: reader.GetString(1),
                ProviderModId: reader.GetString(2),
                SourceUrl: reader.GetString(3),
                FetchedAt: ParseRequiredDate(reader.GetString(4)),
                ETag: reader.IsDBNull(5) ? null : reader.GetString(5),
                LastModified: ParseDate(reader, 6),
                SourceFingerprint: reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return results;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EnsureSourceRegisteredAsync(
        SqliteConnection connection,
        string providerId,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM catalog_sources WHERE provider_id = $provider_id LIMIT 1;";
        AddParameter(command, "$provider_id", NormalizeId(providerId, nameof(providerId)));
        var found = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (found is null)
            throw new InvalidOperationException($"Catalog provider '{providerId}' must be registered before persisting catalog data.");
    }

    private static async Task UpsertItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CachedCatalogMod cached,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mod = cached.Mod;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalog_items (
                canonical_id, provider_id, provider_mod_id, game_id, name, summary, description,
                author, version, category, tags_json, thumbnail, screenshots_json, created_at,
                updated_at, downloads, endorsements, rating, dependencies_json, source_url,
                fetched_at, expires_at, etag, last_modified, source_fingerprint
            ) VALUES (
                $canonical_id, $provider_id, $provider_mod_id, $game_id, $name, $summary, $description,
                $author, $version, $category, $tags_json, $thumbnail, $screenshots_json, $created_at,
                $updated_at, $downloads, $endorsements, $rating, $dependencies_json, $source_url,
                $fetched_at, $expires_at, $etag, $last_modified, $source_fingerprint
            )
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
                source_fingerprint=excluded.source_fingerprint;
            """;
        AddParameter(command, "$canonical_id", mod.CanonicalId);
        AddParameter(command, "$provider_id", NormalizeId(mod.ProviderId, nameof(mod.ProviderId)));
        AddParameter(command, "$provider_mod_id", mod.ProviderModId);
        AddParameter(command, "$game_id", mod.GameId);
        AddParameter(command, "$name", mod.Name);
        AddParameter(command, "$summary", mod.Summary);
        AddParameter(command, "$description", mod.Description);
        AddParameter(command, "$author", mod.Author);
        AddParameter(command, "$version", mod.Version);
        AddParameter(command, "$category", mod.Category);
        AddParameter(command, "$tags_json", JsonSerializer.Serialize(mod.Tags));
        AddParameter(command, "$thumbnail", mod.Thumbnail);
        AddParameter(command, "$screenshots_json", JsonSerializer.Serialize(mod.Screenshots));
        AddParameter(command, "$created_at", FormatDate(mod.CreatedAt));
        AddParameter(command, "$updated_at", FormatDate(mod.UpdatedAt));
        AddParameter(command, "$downloads", mod.Downloads);
        AddParameter(command, "$endorsements", mod.Endorsements);
        AddParameter(command, "$rating", mod.Rating);
        AddParameter(command, "$dependencies_json", JsonSerializer.Serialize(mod.Dependencies));
        AddParameter(command, "$source_url", mod.SourceUrl);
        AddParameter(command, "$fetched_at", FormatDate(cached.Cache.FetchedAt));
        AddParameter(command, "$expires_at", FormatDate(cached.Cache.ExpiresAt));
        AddParameter(command, "$etag", cached.Cache.ETag);
        AddParameter(command, "$last_modified", FormatDate(cached.Cache.LastModified));
        AddParameter(command, "$source_fingerprint", cached.Cache.SourceFingerprint);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task ReplaceFilesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogMod mod,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM catalog_files WHERE canonical_id = $canonical_id;";
            AddParameter(delete, "$canonical_id", mod.CanonicalId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var file in mod.Files)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO catalog_files (
                    provider_id, provider_mod_id, provider_file_id, canonical_id, name, file_name,
                    category, version, size_bytes, description, uploaded_at, required, recommended,
                    dependencies_json
                ) VALUES (
                    $provider_id, $provider_mod_id, $provider_file_id, $canonical_id, $name, $file_name,
                    $category, $version, $size_bytes, $description, $uploaded_at, $required, $recommended,
                    $dependencies_json
                );
                """;
            AddParameter(insert, "$provider_id", NormalizeId(file.ProviderId, nameof(file.ProviderId)));
            AddParameter(insert, "$provider_mod_id", file.ProviderModId);
            AddParameter(insert, "$provider_file_id", file.ProviderFileId);
            AddParameter(insert, "$canonical_id", mod.CanonicalId);
            AddParameter(insert, "$name", file.Name);
            AddParameter(insert, "$file_name", file.FileName);
            AddParameter(insert, "$category", (int)file.Category);
            AddParameter(insert, "$version", file.Version);
            AddParameter(insert, "$size_bytes", file.SizeBytes);
            AddParameter(insert, "$description", file.Description);
            AddParameter(insert, "$uploaded_at", FormatDate(file.UploadedAt));
            AddParameter(insert, "$required", file.Required ? 1 : 0);
            AddParameter(insert, "$recommended", file.Recommended ? 1 : 0);
            AddParameter(insert, "$dependencies_json", JsonSerializer.Serialize(file.Dependencies ?? Array.Empty<CatalogDependency>()));
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task ReplaceFtsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogMod mod,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM catalog_items_fts WHERE canonical_id = $canonical_id;";
            AddParameter(delete, "$canonical_id", mod.CanonicalId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO catalog_items_fts (
                canonical_id, name, author, summary, tags, category, description
            ) VALUES (
                $canonical_id, $name, $author, $summary, $tags, $category, $description
            );
            """;
        AddParameter(insert, "$canonical_id", mod.CanonicalId);
        AddParameter(insert, "$name", mod.Name);
        AddParameter(insert, "$author", mod.Author);
        AddParameter(insert, "$summary", mod.Summary);
        AddParameter(insert, "$tags", string.Join(' ', mod.Tags));
        AddParameter(insert, "$category", mod.Category ?? string.Empty);
        AddParameter(insert, "$description", mod.Description);
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertProvenanceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CachedCatalogMod cached,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalog_provenance (
                canonical_id, provider_id, provider_mod_id, source_url,
                fetched_at, etag, last_modified, source_fingerprint
            ) VALUES (
                $canonical_id, $provider_id, $provider_mod_id, $source_url,
                $fetched_at, $etag, $last_modified, $source_fingerprint
            );
            """;
        AddParameter(command, "$canonical_id", cached.Mod.CanonicalId);
        AddParameter(command, "$provider_id", NormalizeId(cached.Mod.ProviderId, nameof(cached.Mod.ProviderId)));
        AddParameter(command, "$provider_mod_id", cached.Mod.ProviderModId);
        AddParameter(command, "$source_url", cached.Mod.SourceUrl);
        AddParameter(command, "$fetched_at", FormatDate(cached.Cache.FetchedAt));
        AddParameter(command, "$etag", cached.Cache.ETag);
        AddParameter(command, "$last_modified", FormatDate(cached.Cache.LastModified));
        AddParameter(command, "$source_fingerprint", cached.Cache.SourceFingerprint);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<CatalogRow?> ReadSingleRowAsync(
        SqliteConnection connection,
        string canonicalId,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT canonical_id, provider_id, provider_mod_id, game_id,
                   name, summary, description, author, version, category,
                   tags_json, thumbnail, screenshots_json, created_at, updated_at,
                   downloads, endorsements, rating, dependencies_json, source_url,
                   fetched_at, expires_at, etag, last_modified, source_fingerprint
            FROM catalog_items
            WHERE canonical_id = $canonical_id;
            """;
        AddParameter(command, "$canonical_id", canonicalId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadRow(reader) : null;
    }

    private static CatalogRow ReadRow(SqliteDataReader reader)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new CatalogRow(
            CanonicalId: reader.GetString(0),
            ProviderId: reader.GetString(1),
            ProviderModId: reader.GetString(2),
            GameId: reader.GetString(3),
            Name: reader.GetString(4),
            Summary: reader.GetString(5),
            Description: reader.GetString(6),
            Author: reader.GetString(7),
            Version: reader.IsDBNull(8) ? null : reader.GetString(8),
            Category: reader.IsDBNull(9) ? null : reader.GetString(9),
            TagsJson: reader.GetString(10),
            Thumbnail: reader.IsDBNull(11) ? null : reader.GetString(11),
            ScreenshotsJson: reader.GetString(12),
            CreatedAt: ParseDate(reader, 13),
            UpdatedAt: ParseDate(reader, 14),
            Downloads: reader.IsDBNull(15) ? null : reader.GetInt64(15),
            Endorsements: reader.IsDBNull(16) ? null : reader.GetInt64(16),
            Rating: reader.IsDBNull(17) ? null : reader.GetDouble(17),
            DependenciesJson: reader.GetString(18),
            SourceUrl: reader.GetString(19),
            FetchedAt: ParseRequiredDate(reader.GetString(20)),
            ExpiresAt: ParseDate(reader, 21),
            ETag: reader.IsDBNull(22) ? null : reader.GetString(22),
            LastModified: ParseDate(reader, 23),
            SourceFingerprint: reader.IsDBNull(24) ? null : reader.GetString(24));
    }

    private static async Task<CachedCatalogMod> MaterializeAsync(
        SqliteConnection connection,
        CatalogRow row,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var files = await ReadFilesAsync(connection, row.CanonicalId, ct).ConfigureAwait(false);
        var mod = new CatalogMod(
            CanonicalId: row.CanonicalId,
            ProviderId: row.ProviderId,
            ProviderModId: row.ProviderModId,
            GameId: row.GameId,
            Name: row.Name,
            Summary: row.Summary,
            Description: row.Description,
            Author: row.Author,
            Version: row.Version,
            Category: row.Category,
            Tags: Deserialize<List<string>>(row.TagsJson),
            Thumbnail: row.Thumbnail,
            Screenshots: Deserialize<List<CatalogImage>>(row.ScreenshotsJson),
            CreatedAt: row.CreatedAt,
            UpdatedAt: row.UpdatedAt,
            Downloads: row.Downloads,
            Endorsements: row.Endorsements,
            Rating: row.Rating,
            Dependencies: Deserialize<List<CatalogDependency>>(row.DependenciesJson),
            SourceUrl: row.SourceUrl,
            Files: files,
            ProviderMetadata: null);

        return new CachedCatalogMod(
            mod,
            new CatalogCacheMetadata(
                FetchedAt: row.FetchedAt,
                ExpiresAt: row.ExpiresAt,
                ETag: row.ETag,
                LastModified: row.LastModified,
                SourceFingerprint: row.SourceFingerprint));
    }

    private static async Task<IReadOnlyList<CatalogModFile>> ReadFilesAsync(
        SqliteConnection connection,
        string canonicalId,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT provider_id, provider_mod_id, provider_file_id, name, file_name,
                   category, version, size_bytes, description, uploaded_at, required,
                   recommended, dependencies_json
            FROM catalog_files
            WHERE canonical_id = $canonical_id
            ORDER BY rowid;
            """;
        AddParameter(command, "$canonical_id", canonicalId);

        var files = new List<CatalogModFile>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            files.Add(new CatalogModFile(
                ProviderId: reader.GetString(0),
                ProviderModId: reader.GetString(1),
                ProviderFileId: reader.GetString(2),
                Name: reader.GetString(3),
                FileName: reader.GetString(4),
                Category: (CatalogFileCategory)reader.GetInt32(5),
                Version: reader.IsDBNull(6) ? null : reader.GetString(6),
                SizeBytes: reader.IsDBNull(7) ? null : reader.GetInt64(7),
                Description: reader.IsDBNull(8) ? null : reader.GetString(8),
                UploadedAt: ParseDate(reader, 9),
                Required: reader.GetInt32(10) != 0,
                Recommended: reader.GetInt32(11) != 0,
                Dependencies: Deserialize<List<CatalogDependency>>(reader.GetString(12)),
                ProviderMetadata: null));
        }

        return files;
    }

    private static T Deserialize<T>(string json) where T : class, new()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return JsonSerializer.Deserialize<T>(json) ?? new T();
    }

    private static void ValidateCatalogMod(CatalogMod mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.CanonicalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.ProviderModId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.GameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.SourceUrl);

        var expectedCanonical = CatalogMod.BuildCanonicalId(mod.ProviderId, mod.ProviderModId);
        if (!string.Equals(mod.CanonicalId, expectedCanonical, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Catalog canonical id '{mod.CanonicalId}' does not match provider identity '{expectedCanonical}'.");

        if (!Uri.TryCreate(mod.SourceUrl, UriKind.Absolute, out var sourceUri)
            || !string.Equals(sourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Catalog SourceUrl must be an absolute HTTPS provider page URL.");
        }

        foreach (var file in mod.Files)
        {
            if (!string.Equals(file.ProviderId, mod.ProviderId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(file.ProviderModId, mod.ProviderModId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Catalog file identity must match its parent mod provider identity.");
            }
        }
    }

    private static string BuildFtsQuery(string query)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var terms = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizeFtsTerm)
            .Where(static term => term.Length > 0)
            .Take(16)
            .ToArray();

        if (terms.Length == 0)
            throw new ArgumentException("Catalog search query must contain searchable letters or digits.", nameof(query));

        return string.Join(" AND ", terms.Select(static term => $""{term}""));
    }

    private static string SanitizeFtsTerm(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var chars = value.Where(static ch => char.IsLetterOrDigit(ch) || ch is '_' or '-').Take(64).ToArray();
        return new string(chars);
    }

    private static string NormalizeId(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Identifier is required.", parameterName);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 128 || normalized.Any(static ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')))
            throw new ArgumentException("Identifier contains unsupported characters.", parameterName);
        return normalized;
    }

    private static string? NormalizeCursor(string? cursor)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        var trimmed = cursor.Trim();
        if (trimmed.Length > 4096 || trimmed.Contains('\r') || trimmed.Contains('\n'))
            throw new ArgumentException("Catalog sync cursor is invalid.", nameof(cursor));
        return trimmed;
    }

    private static string? FormatDate(DateTimeOffset? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTimeOffset value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseDate(SqliteDataReader reader, int ordinal)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return reader.IsDBNull(ordinal) ? null : ParseRequiredDate(reader.GetString(ordinal));
    }

    private static DateTimeOffset ParseRequiredDate(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private sealed record CatalogRow(
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
        string? Thumbnail,
        string ScreenshotsJson,
        DateTimeOffset? CreatedAt,
        DateTimeOffset? UpdatedAt,
        long? Downloads,
        long? Endorsements,
        double? Rating,
        string DependenciesJson,
        string SourceUrl,
        DateTimeOffset FetchedAt,
        DateTimeOffset? ExpiresAt,
        string? ETag,
        DateTimeOffset? LastModified,
        string? SourceFingerprint);
}
