using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed record CatalogAcquisitionResult(
    CatalogDownloadArtifact? Artifact,
    Uri? AssistedUri,
    string Message)
{
    public bool RequiresAssistedDownload => Artifact is null && AssistedUri is not null;
}

public sealed class CatalogInstallService(
    ManagerDatabase database,
    ModCatalogService catalog,
    CatalogDownloadManager downloads)
{
    public async Task<CatalogAcquisitionResult> AcquireAsync(
        GameProfile game,
        CatalogMod mod,
        CatalogModFile file,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={mod.ProviderId}; mod={mod.ProviderModId}; file={file.ProviderFileId}");
        var resolution = await catalog.ResolveDownloadAsync(new(game, mod, file), ct);
        if (!resolution.IsDirect || resolution.DownloadUri is null)
            return new(null, resolution.AssistedUri, resolution.Message);

        var artifact = await downloads.DownloadAsync(resolution.DownloadUri, file, progress, ct);
        return new(artifact, resolution.AssistedUri, resolution.Message);
    }

    public async Task<InstalledCatalogOrigin> AttachImportedOriginAsync(
        ArchiveImportResult imported,
        CatalogMod mod,
        CatalogModFile file,
        CatalogDownloadArtifact artifact,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={mod.ProviderId}; mod={mod.ProviderModId}; file={file.ProviderFileId}");
        var installed = (await database.GetModsAsync(ct))
            .FirstOrDefault(candidate => Path.GetFullPath(candidate.SourcePath)
                .Equals(Path.GetFullPath(imported.DestinationPath), StringComparison.OrdinalIgnoreCase));
        if (installed is null)
            throw new InvalidOperationException("The imported catalog archive was not registered in the local mod catalog.");

        var updated = installed with
        {
            SourceUrl = mod.SourceUrl,
            NexusModId = mod.ProviderId.Equals("nexus", StringComparison.OrdinalIgnoreCase) ? mod.ProviderModId : installed.NexusModId,
            NexusFileId = mod.ProviderId.Equals("nexus", StringComparison.OrdinalIgnoreCase) ? file.ProviderFileId : installed.NexusFileId
        };
        await database.UpsertModAsync(updated, ct);

        var origin = new InstalledCatalogOrigin(
            updated.Id,
            mod.ProviderId,
            mod.ProviderModId,
            file.ProviderFileId,
            file.Version ?? mod.Version,
            DateTimeOffset.UtcNow,
            mod.SourceUrl,
            artifact.Sha256,
            file.ProviderMetadata);
        await database.UpsertCatalogOriginAsync(origin, ct);

        if (mod.ProviderId.Equals("nexus", StringComparison.OrdinalIgnoreCase))
        {
            await database.UpsertProvenanceAsync(new(
                updated.Id,
                mod.ProviderModId,
                null,
                file.ProviderFileId,
                null,
                null,
                ToNexusCategory(file.Category),
                file.FileName,
                file.Version ?? mod.Version,
                file.UploadedAt,
                ProvenanceSource.NexusApi,
                100,
                Path.GetFileName(artifact.ArchivePath),
                file.ProviderMetadata,
                DateTimeOffset.UtcNow), ct);
        }

        return origin;
    }

    private static NexusFileCategory ToNexusCategory(CatalogFileCategory category)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return category switch
        {
            CatalogFileCategory.Main => NexusFileCategory.Main,
            CatalogFileCategory.Update => NexusFileCategory.Update,
            CatalogFileCategory.Optional => NexusFileCategory.Optional,
            CatalogFileCategory.OldVersion => NexusFileCategory.OldVersion,
            CatalogFileCategory.Miscellaneous => NexusFileCategory.Miscellaneous,
            CatalogFileCategory.Removed => NexusFileCategory.Removed,
            CatalogFileCategory.Archived => NexusFileCategory.Archived,
            _ => NexusFileCategory.Unknown
        };
    }
}
