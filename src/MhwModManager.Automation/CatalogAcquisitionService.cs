using System.Net;
using System.Security.Cryptography;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public enum CatalogImportOutcomeKind
{
    Imported,
    Assisted,
    Unavailable
}

public sealed record CatalogImportOutcome(
    CatalogImportOutcomeKind Kind,
    string Message,
    string? InstalledModId = null,
    Uri? AssistedUri = null);

/// <summary>
/// Bridges provider acquisition into the manager's existing hardened archive import boundary.
/// Provider download URLs are treated as ephemeral transport details and are never persisted.
/// </summary>
public sealed class CatalogAcquisitionService
{
    private const long MaxDownloadBytes = 1024L * 1024L * 1024L;
    private const int BufferSize = 128 * 1024;

    private readonly HttpClient http;
    private readonly ArchiveImportService importer;
    private readonly InstalledCatalogOriginRepository origins;
    private readonly ManagerDatabase database;
    private readonly HashingService hashing;
    private readonly string downloadRoot;
    private readonly TimeProvider timeProvider;

    public CatalogAcquisitionService(
        HttpClient http,
        ArchiveImportService importer,
        InstalledCatalogOriginRepository origins,
        ManagerDatabase database,
        HashingService hashing,
        string downloadRoot,
        TimeProvider? timeProvider = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(origins);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(hashing);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadRoot);

        this.http = http;
        this.importer = importer;
        this.origins = origins;
        this.database = database;
        this.hashing = hashing;
        this.downloadRoot = Path.GetFullPath(downloadRoot);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CatalogImportOutcome> AcquireAsync(
        IModCatalogProvider provider,
        GameProfile game,
        CatalogMod mod,
        CatalogModFile file,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"provider={provider?.ProviderId}; mod={mod?.ProviderModId}; file={file?.ProviderFileId}");
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(file);
        ValidateIdentity(provider, game, mod, file);

        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(game, mod, file),
            ct).ConfigureAwait(false);

        if (resolution.Kind == CatalogAcquisitionKind.Unavailable)
        {
            return new CatalogImportOutcome(
                CatalogImportOutcomeKind.Unavailable,
                resolution.Message);
        }

        if (resolution.Kind == CatalogAcquisitionKind.Assisted)
        {
            if (resolution.AssistedUri is null)
                throw new InvalidDataException("Catalog provider returned assisted acquisition without an assisted URI.");
            ValidateHttpsUri(resolution.AssistedUri, "assisted");
            return new CatalogImportOutcome(
                CatalogImportOutcomeKind.Assisted,
                resolution.Message,
                AssistedUri: resolution.AssistedUri);
        }

        if (resolution.DownloadUri is null)
            throw new InvalidDataException("Catalog provider returned direct acquisition without a download URI.");
        ValidateHttpsUri(resolution.DownloadUri, "download");

        var tempArchive = AllocateDownloadPath(file.FileName);
        try
        {
            await DownloadBoundedAsync(resolution.DownloadUri, tempArchive, ct).ConfigureAwait(false);
            var archiveHash = await hashing.HashFileAsync(tempArchive, authoritative: true, ct).ConfigureAwait(false);
            var imported = await importer.ImportAsync(tempArchive, ct).ConfigureAwait(false);

            var installed = (await database.GetModsAsync(ct).ConfigureAwait(false))
                .SingleOrDefault(candidate =>
                    string.Equals(
                        Path.GetFullPath(candidate.SourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        Path.GetFullPath(imported.DestinationPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException(
                    "Catalog archive was imported, but the manager could not resolve the newly created library mod identity.");

            await origins.UpsertAsync(
                new InstalledCatalogOrigin(
                    installed.Id,
                    provider.ProviderId,
                    mod.ProviderModId,
                    file.ProviderFileId,
                    file.Version ?? mod.Version,
                    timeProvider.GetUtcNow(),
                    mod.SourceUrl,
                    archiveHash.Sha256,
                    ProviderMetadata: null),
                ct).ConfigureAwait(false);

            return new CatalogImportOutcome(
                CatalogImportOutcomeKind.Imported,
                $"Installed '{mod.Name}' through the verified archive import pipeline.",
                installed.Id);
        }
        finally
        {
            try
            {
                if (File.Exists(tempArchive))
                    File.Delete(tempArchive);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MasterDebugLog.Write(
                    "CATALOG-ACQUISITION",
                    $"Could not remove temporary catalog download '{tempArchive}'.",
                    ex);
            }
        }
    }

    private async Task DownloadBoundedAsync(
        Uri uri,
        string destination,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"host={uri.Host}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", "MHW-Manual-Mod-Manager");
        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Catalog download failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "unknown"}).",
                null,
                response.StatusCode);
        }

        var finalUri = response.RequestMessage?.RequestUri
            ?? throw new InvalidDataException("Catalog download response did not expose its final request URI.");
        ValidateHttpsUri(finalUri, "final download");

        if (response.Content.Headers.ContentLength is long declared
            && (declared <= 0 || declared > MaxDownloadBytes))
        {
            throw new InvalidDataException(
                $"Catalog download declared an unsupported size of {declared} bytes.");
        }

        Directory.CreateDirectory(downloadRoot);
        RejectReparsePoint(downloadRoot);

        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[BufferSize];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) break;
            if (total > MaxDownloadBytes - read)
                throw new InvalidDataException("Catalog download exceeded the 1 GiB safety budget.");
            total += read;
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        if (total == 0)
            throw new InvalidDataException("Catalog download returned an empty archive.");

        await output.FlushAsync(ct).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private string AllocateDownloadPath(string fileName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(downloadRoot);
        RejectReparsePoint(downloadRoot);
        var extension = Path.GetExtension(fileName);
        if (extension.Length is < 2 or > 12
            || extension.Skip(1).Any(character => !char.IsLetterOrDigit(character)))
        {
            extension = ".archive";
        }

        var destination = Path.GetFullPath(
            Path.Combine(downloadRoot, $"catalog-{Guid.NewGuid():N}{extension.ToLowerInvariant()}"));
        var prefix = downloadRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Catalog download path escaped the manager-owned staging directory.");
        return destination;
    }

    private static void RejectReparsePoint(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Catalog download directory is a reparse point: {path}");
    }

    private static void ValidateIdentity(
        IModCatalogProvider provider,
        GameProfile game,
        CatalogMod mod,
        CatalogModFile file)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!string.Equals(provider.ProviderId, mod.ProviderId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(provider.ProviderId, file.ProviderId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(mod.ProviderModId, file.ProviderModId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(mod.GameId, game.Id, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(file.ProviderFileId))
        {
            throw new InvalidDataException(
                "Catalog acquisition identities do not belong to one exact provider/game/mod/file tuple.");
        }
    }

    private static void ValidateHttpsUri(Uri uri, string purpose)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"purpose={purpose}");
        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidDataException(
                $"Catalog {purpose} URI must be credential-free absolute HTTPS.");
        }
    }
}
