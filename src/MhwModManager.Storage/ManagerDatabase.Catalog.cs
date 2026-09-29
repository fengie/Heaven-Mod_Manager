using System.Globalization;
using MhwModManager.Core;

namespace MhwModManager.Storage;

public sealed partial class ManagerDatabase
{
    public async Task UpsertCatalogOriginAsync(InstalledCatalogOrigin origin, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"mod={origin.ModId}; provider={origin.ProviderId}");
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO catalog_install_origins(
            mod_id,provider_id,provider_mod_id,provider_file_id,installed_version,
            downloaded_at,source_url,archive_sha256,provider_metadata_json)
        VALUES($m,$p,$pm,$pf,$v,$d,$u,$h,$j)
        ON CONFLICT(mod_id) DO UPDATE SET
            provider_id=excluded.provider_id,
            provider_mod_id=excluded.provider_mod_id,
            provider_file_id=excluded.provider_file_id,
            installed_version=excluded.installed_version,
            downloaded_at=excluded.downloaded_at,
            source_url=excluded.source_url,
            archive_sha256=excluded.archive_sha256,
            provider_metadata_json=excluded.provider_metadata_json
        """;
        command.Parameters.AddWithValue("$m", origin.ModId);
        command.Parameters.AddWithValue("$p", origin.ProviderId);
        command.Parameters.AddWithValue("$pm", origin.ProviderModId);
        command.Parameters.AddWithValue("$pf", origin.ProviderFileId);
        command.Parameters.AddWithValue("$v", (object?)origin.InstalledVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$d", origin.DownloadedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$u", origin.SourceUrl);
        command.Parameters.AddWithValue("$h", origin.ArchiveSha256);
        command.Parameters.AddWithValue("$j", (object?)origin.ProviderMetadata ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, InstalledCatalogOrigin>> GetCatalogOriginsAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result = new Dictionary<string, InstalledCatalogOrigin>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT mod_id,provider_id,provider_mod_id,provider_file_id,installed_version,
               downloaded_at,source_url,archive_sha256,provider_metadata_json
        FROM catalog_install_origins
        """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var origin = new InstalledCatalogOrigin(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8));
            result[origin.ModId] = origin;
        }
        return result;
    }

    public async Task<IReadOnlyList<InstalledCatalogOrigin>> GetCatalogOriginsForProviderModAsync(
        string providerId,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}; mod={providerModId}");
        var result = new List<InstalledCatalogOrigin>();
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT mod_id,provider_id,provider_mod_id,provider_file_id,installed_version,
               downloaded_at,source_url,archive_sha256,provider_metadata_json
        FROM catalog_install_origins
        WHERE provider_id=$p AND provider_mod_id=$m
        ORDER BY downloaded_at DESC
        """;
        command.Parameters.AddWithValue("$p", providerId);
        command.Parameters.AddWithValue("$m", providerModId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }
        return result;
    }
}
