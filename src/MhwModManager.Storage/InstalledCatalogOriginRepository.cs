using System.Globalization;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;

namespace MhwModManager.Storage;

public sealed class InstalledCatalogOriginRepository(ManagerDatabase db)
{
    private const int MaxIdentityLength = 512;
    private const int MaxUrlLength = 4096;
    private const int MaxMetadataLength = 16 * 1024;

    public async Task UpsertAsync(
        InstalledCatalogOrigin origin,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = Normalize(origin);

        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO installed_catalog_origins(
                mod_id,provider_id,provider_mod_id,provider_file_id,installed_version,
                downloaded_at,source_url,archive_sha256,provider_metadata,updated_at)
            VALUES(
                $mod,$provider,$providerMod,$providerFile,$version,
                $downloaded,$source,$sha,$metadata,$updated)
            ON CONFLICT(mod_id) DO UPDATE SET
                provider_id=excluded.provider_id,
                provider_mod_id=excluded.provider_mod_id,
                provider_file_id=excluded.provider_file_id,
                installed_version=excluded.installed_version,
                downloaded_at=excluded.downloaded_at,
                source_url=excluded.source_url,
                archive_sha256=excluded.archive_sha256,
                provider_metadata=excluded.provider_metadata,
                updated_at=excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("$mod", normalized.ModId);
        cmd.Parameters.AddWithValue("$provider", normalized.ProviderId);
        cmd.Parameters.AddWithValue("$providerMod", normalized.ProviderModId);
        cmd.Parameters.AddWithValue("$providerFile", normalized.ProviderFileId);
        cmd.Parameters.AddWithValue("$version", DbValue(normalized.InstalledVersion));
        cmd.Parameters.AddWithValue(
            "$downloaded",
            normalized.DownloadedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$source", normalized.SourceUrl);
        cmd.Parameters.AddWithValue("$sha", normalized.ArchiveSha256);
        cmd.Parameters.AddWithValue("$metadata", DbValue(normalized.ProviderMetadata));
        cmd.Parameters.AddWithValue(
            "$updated",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<InstalledCatalogOrigin?> GetAsync(
        string modId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalizedModId = NormalizeIdentity(modId, nameof(modId));

        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT mod_id,provider_id,provider_mod_id,provider_file_id,installed_version,
                   downloaded_at,source_url,archive_sha256,provider_metadata
            FROM installed_catalog_origins
            WHERE mod_id=$mod
            """;
        cmd.Parameters.AddWithValue("$mod", normalizedModId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadOrigin(reader);
    }

    public async Task<IReadOnlyList<InstalledCatalogOrigin>> GetAllAsync(
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var results = new List<InstalledCatalogOrigin>();

        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT mod_id,provider_id,provider_mod_id,provider_file_id,installed_version,
                   downloaded_at,source_url,archive_sha256,provider_metadata
            FROM installed_catalog_origins
            ORDER BY mod_id COLLATE NOCASE
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadOrigin(reader));

        return results;
    }

    public async Task DeleteAsync(
        string modId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalizedModId = NormalizeIdentity(modId, nameof(modId));

        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM installed_catalog_origins WHERE mod_id=$mod";
        cmd.Parameters.AddWithValue("$mod", normalizedModId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static InstalledCatalogOrigin ReadOrigin(SqliteDataReader reader)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new InstalledCatalogOrigin(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            DateTimeOffset.Parse(
                reader.GetString(5),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

    private static InstalledCatalogOrigin Normalize(InstalledCatalogOrigin origin)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(origin);

        var modId = NormalizeIdentity(origin.ModId, nameof(origin.ModId));
        var providerId = NormalizeIdentity(origin.ProviderId, nameof(origin.ProviderId));
        var providerModId = NormalizeIdentity(origin.ProviderModId, nameof(origin.ProviderModId));
        var providerFileId = NormalizeIdentity(origin.ProviderFileId, nameof(origin.ProviderFileId));
        var installedVersion = NormalizeOptional(origin.InstalledVersion, nameof(origin.InstalledVersion));
        var sourceUrl = NormalizeSourceUrl(origin.SourceUrl);
        var archiveSha256 = NormalizeSha256(origin.ArchiveSha256);
        var providerMetadata = NormalizeProviderMetadata(origin.ProviderMetadata);

        return origin with
        {
            ModId = modId,
            ProviderId = providerId,
            ProviderModId = providerModId,
            ProviderFileId = providerFileId,
            InstalledVersion = installedVersion,
            DownloadedAt = origin.DownloadedAt.ToUniversalTime(),
            SourceUrl = sourceUrl,
            ArchiveSha256 = archiveSha256,
            ProviderMetadata = providerMetadata
        };
    }

    private static string NormalizeIdentity(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > MaxIdentityLength)
            throw new ArgumentException(
                $"{parameterName} exceeds the maximum identity length.",
                parameterName);
        if (normalized.Any(char.IsControl))
            throw new ArgumentException(
                $"{parameterName} contains control characters.",
                parameterName);
        return normalized;
    }

    private static string? NormalizeOptional(string? value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return NormalizeIdentity(value, parameterName);
    }

    private static string NormalizeSourceUrl(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaxUrlLength
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidDataException(
                "Installed catalog source URL must be a durable credential-free HTTP(S) URL.");
        }

        var query = uri.Query.ToLowerInvariant();
        string[] forbidden =
        [
            "token=", "access_token=", "apikey=", "api_key=", "signature=", "sig=",
            "expires=", "x-amz-", "x-goog-", "authorization=", "auth=", "jwt="
        ];
        if (forbidden.Any(query.Contains))
            throw new InvalidDataException(
                "Installed catalog source URL must not contain credentials or expiring signatures.");

        return uri.AbsoluteUri;
    }

    private static string NormalizeSha256(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length != 64)
            throw new InvalidDataException("Installed catalog archive SHA-256 must contain 64 hexadecimal characters.");

        try
        {
            _ = Convert.FromHexString(normalized);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException(
                "Installed catalog archive SHA-256 is malformed.",
                ex);
        }

        return normalized.ToLowerInvariant();
    }

    private static string? NormalizeProviderMetadata(string? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized.Length > MaxMetadataLength)
            throw new InvalidDataException("Installed catalog provider metadata exceeds the persistence limit.");

        var lower = normalized.ToLowerInvariant();
        string[] forbidden =
        [
            "access_token", "api_key", "apikey", "authorization", "bearer ",
            "x-amz-signature", "x-goog-signature", "\"token\"", "\"secret\"",
            "signature=", "token=", "secret=", "jwt="
        ];
        if (forbidden.Any(lower.Contains))
            throw new InvalidDataException(
                "Installed catalog provider metadata appears to contain secret or signed-request material.");

        return normalized;
    }

    private static object DbValue(object? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value ?? DBNull.Value;
    }
}
