namespace MhwModManager.Core;

public enum InstalledCatalogOriginCheckState
{
    Current,
    ExactFileMetadataChanged,
    SourceFileMissing,
    SourceModMissing,
    UpdatesUnsupported
}

public sealed record InstalledCatalogOriginCheckResult(
    InstalledCatalogOrigin Origin,
    InstalledCatalogOriginCheckState State,
    CatalogMod? CurrentMod,
    CatalogModFile? ExactFile,
    string Message);

public sealed class InstalledCatalogOriginChecker
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "This checker is an instance service API; preserving instance semantics avoids needless public API churn while providers remain injected per call.")]
    public async Task<InstalledCatalogOriginCheckResult> CheckAsync(
        IModCatalogProvider provider,
        GameProfile game,
        InstalledCatalogOrigin origin,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(origin);

        if (!string.Equals(
                provider.ProviderId,
                origin.ProviderId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Catalog provider does not match the installed origin provider.",
                nameof(provider));
        }

        if (!provider.Capabilities.HasFlag(CatalogProviderCapabilities.Updates))
        {
            return new InstalledCatalogOriginCheckResult(
                origin,
                InstalledCatalogOriginCheckState.UpdatesUnsupported,
                null,
                null,
                $"Provider '{provider.ProviderId}' does not advertise exact update checks.");
        }

        CatalogMod? mod;
        IReadOnlyList<CatalogModFile> files;

        if (provider is IInstalledCatalogOriginSnapshotProvider snapshotProvider)
        {
            var snapshot = await snapshotProvider
                .GetInstalledOriginSnapshotAsync(game, origin.ProviderModId, ct)
                .ConfigureAwait(false);
            if (snapshot is null)
                throw new InvalidDataException("Catalog provider returned a null installed-origin snapshot.");

            mod = snapshot.Mod;
            files = snapshot.Files
                ?? throw new InvalidDataException("Catalog provider returned a null installed-origin file list.");
        }
        else
        {
            mod = await provider.GetModAsync(
                game,
                origin.ProviderModId,
                ct).ConfigureAwait(false);

            if (mod is null)
            {
                return new InstalledCatalogOriginCheckResult(
                    origin,
                    InstalledCatalogOriginCheckState.SourceModMissing,
                    null,
                    null,
                    "The exact provider mod identity no longer resolves.");
            }

            files = await provider.GetModFilesAsync(
                game,
                origin.ProviderModId,
                ct).ConfigureAwait(false);
        }

        if (mod is null)
        {
            return new InstalledCatalogOriginCheckResult(
                origin,
                InstalledCatalogOriginCheckState.SourceModMissing,
                null,
                null,
                "The exact provider mod identity no longer resolves.");
        }

        ValidateModIdentity(provider.ProviderId, game.Id, origin.ProviderModId, mod);
        ValidateFileIdentities(provider.ProviderId, origin.ProviderModId, files);

        var exactMatches = files
            .Where(file => string.Equals(
                file.ProviderFileId,
                origin.ProviderFileId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (exactMatches.Length > 1)
            throw new InvalidDataException(
                "Catalog provider returned duplicate entries for the installed provider file identity.");

        if (exactMatches.Length == 0)
        {
            return new InstalledCatalogOriginCheckResult(
                origin,
                InstalledCatalogOriginCheckState.SourceFileMissing,
                mod,
                null,
                "The exact installed provider file identity is no longer present. No replacement was guessed.");
        }

        var exactFile = exactMatches[0];
        if (VersionChanged(origin.InstalledVersion, exactFile.Version))
        {
            return new InstalledCatalogOriginCheckResult(
                origin,
                InstalledCatalogOriginCheckState.ExactFileMetadataChanged,
                mod,
                exactFile,
                "The same provider file identity now reports different version metadata and requires review.");
        }

        return new InstalledCatalogOriginCheckResult(
            origin,
            InstalledCatalogOriginCheckState.Current,
            mod,
            exactFile,
            "The exact installed provider mod/file identity still resolves without version-metadata drift.");
    }

    private static void ValidateModIdentity(
        string providerId,
        string gameId,
        string providerModId,
        CatalogMod mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!string.Equals(mod.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(mod.ProviderModId, providerModId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(mod.GameId, gameId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Catalog provider returned a mod with mismatched provider/game identity during installed-origin checking.");
        }
    }

    private static void ValidateFileIdentities(
        string providerId,
        string providerModId,
        IReadOnlyList<CatalogModFile> files)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(files);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!string.Equals(file.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(file.ProviderModId, providerModId, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(file.ProviderFileId))
            {
                throw new InvalidDataException(
                    "Catalog provider returned a file with mismatched installed-origin identity.");
            }

            if (!ids.Add(file.ProviderFileId))
                throw new InvalidDataException(
                    $"Catalog provider returned duplicate file identity '{file.ProviderFileId}'.");
        }
    }

    private static bool VersionChanged(string? installedVersion, string? currentVersion)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(installedVersion)
            || string.IsNullOrWhiteSpace(currentVersion))
        {
            return false;
        }

        return !string.Equals(
            installedVersion.Trim(),
            currentVersion.Trim(),
            StringComparison.Ordinal);
    }
}
