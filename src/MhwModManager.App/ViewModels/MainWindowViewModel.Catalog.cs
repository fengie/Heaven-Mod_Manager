using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Diagnostics;
using MhwModManager.Storage;

namespace MhwModManager.App.ViewModels;

public sealed record CatalogModRow(CatalogMod Mod, bool IsStale)
{
    public string Name
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Mod.Name;
        }
    }
    public string Author
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return string.IsNullOrWhiteSpace(Mod.Author) ? "Unknown" : Mod.Author;
        }
    }
    public string Provider
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Mod.ProviderId.Trim().ToLowerInvariant() switch
            {
                "nexus" => "Nexus Mods",
                "gamebanana" => "GameBanana",
                "curseforge" => "CurseForge",
                _ => Mod.ProviderId
            };
        }
    }
    public string Version
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return string.IsNullOrWhiteSpace(Mod.Version) ? "—" : Mod.Version;
        }
    }
    public string Category
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return string.IsNullOrWhiteSpace(Mod.Category) ? "Uncategorized" : Mod.Category;
        }
    }
    public string Summary
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Mod.Summary;
        }
    }
    public string Updated
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Mod.UpdatedAt?.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture) ?? "—";
        }
    }
    public string Downloads
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Mod.Downloads?.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) ?? "—";
        }
    }
    public string CacheState
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return IsStale ? "Cached · refresh recommended" : "Current cache";
        }
    }
    public Uri? ThumbnailUri
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            if (!Uri.TryCreate(Mod.Thumbnail, UriKind.Absolute, out var uri)) return null;
            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
            if (!uri.IsDefaultPort && uri.Port != 443) return null;
            return uri;
        }
    }
}

public sealed record CatalogFileRow(CatalogModFile File)
{
    public string Name
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return File.Name;
        }
    }
    public string FileName
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return File.FileName;
        }
    }
    public string Version
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return string.IsNullOrWhiteSpace(File.Version) ? "—" : File.Version;
        }
    }
    public string Category
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return File.Category.ToString();
        }
    }
    public string Size
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return File.SizeBytes is long bytes ? FormatBytes(bytes) : "—";
        }
    }
    public string Uploaded
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return File.UploadedAt?.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture) ?? "—";
        }
    }

    private static string FormatBytes(long value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (value < 1024) return $"{value} B";
        if (value < 1024L * 1024L) return $"{value / 1024d:F1} KiB";
        if (value < 1024L * 1024L * 1024L) return $"{value / 1024d / 1024d:F1} MiB";
        return $"{value / 1024d / 1024d / 1024d:F1} GiB";
    }
}

public sealed record CatalogOriginStatusRow(
    string ModId,
    string Provider,
    string ProviderModId,
    string ProviderFileId,
    string State,
    string Message);

public sealed record CatalogBrowseResultState(
    bool HasQuery,
    bool HasResults,
    string Title,
    string Detail)
{
    public static CatalogBrowseResultState From(string? query, int resultCount)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var hasResults = resultCount > 0;
        if (hasResults) return new CatalogBrowseResultState(hasQuery, true, string.Empty, string.Empty);

        return hasQuery
            ? new CatalogBrowseResultState(
                true,
                false,
                "No mods match this search",
                "No cached or provider results match this search. Clear the search to return to all cached mods.")
            : new CatalogBrowseResultState(
                false,
                false,
                "No catalog mods yet",
                "Refresh configured providers to load browseable mods. Provider errors and configuration problems are reported separately.");
    }
}

public sealed partial class MainWindowViewModel
{
    private const int CatalogProviderRefreshLimit = 100;
    private const int CatalogVisibleResultLimit = 1000;

    private HttpClient? catalogHttp;
    private CatalogRepository? catalogRepository;
    private CatalogSyncService? catalogSync;
    private InstalledCatalogOriginRepository? catalogOrigins;
    private InstalledCatalogOriginChecker? catalogOriginChecker;
    private CatalogAcquisitionService? catalogAcquisition;
    private IReadOnlyList<IModCatalogProvider>? catalogProviders;
    private bool catalogLoaded;
    private CancellationTokenSource? catalogQueryCts;

    public ObservableRangeCollection<CatalogModRow> CatalogItems { get; } = [];
    public ObservableRangeCollection<CatalogFileRow> CatalogFiles { get; } = [];
    public ObservableRangeCollection<CatalogOriginStatusRow> CatalogOriginStatuses { get; } = [];

    [ObservableProperty] private string catalogQuery = "";
    [ObservableProperty] private CatalogModRow? selectedCatalogItem;
    [ObservableProperty] private CatalogFileRow? selectedCatalogFile;
    [ObservableProperty] private string catalogStatusText = "Browse verified provider metadata without installing anything until you choose a file.";
    [ObservableProperty] private string catalogProviderSummary = "Providers not loaded yet.";

    public int CatalogItemCount
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return CatalogItems.Count;
        }
    }
    public bool HasCatalogQuery
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return CatalogBrowseResultState.From(CatalogQuery, CatalogItemCount).HasQuery;
        }
    }
    public bool HasCatalogResults
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return CatalogBrowseResultState.From(CatalogQuery, CatalogItemCount).HasResults;
        }
    }
    public string CatalogEmptyTitle
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return CatalogBrowseResultState.From(CatalogQuery, CatalogItemCount).Title;
        }
    }
    public string CatalogEmptyDetail
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return CatalogBrowseResultState.From(CatalogQuery, CatalogItemCount).Detail;
        }
    }
    public string CatalogResultCountLabel
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return $"{CatalogItemCount} {(CatalogItemCount == 1 ? "result" : "results")}";
        }
    }
    public Visibility CatalogResultsVisibility
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return HasCatalogResults ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    public Visibility CatalogEmptyVisibility
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return HasCatalogResults ? Visibility.Collapsed : Visibility.Visible;
        }
    }
    public Visibility CatalogClearSearchVisibility
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return HasCatalogQuery ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    public Visibility CatalogRefreshEmptyVisibility
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return HasCatalogQuery ? Visibility.Collapsed : Visibility.Visible;
        }
    }
    public int CatalogOriginAttentionCount
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return CatalogOriginStatuses.Count(row =>
                !string.Equals(row.State, InstalledCatalogOriginCheckState.Current.ToString(), StringComparison.OrdinalIgnoreCase));
        }
    }

    partial void OnCatalogQueryChanged(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        NotifyCatalogResultState();
        catalogQueryCts?.Cancel();
        catalogQueryCts?.Dispose();
        var cts = catalogQueryCts = new CancellationTokenSource();
        _ = DebounceCatalogQueryAsync(cts.Token);
    }

    private void NotifyCatalogResultState()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        OnPropertyChanged(nameof(CatalogItemCount));
        OnPropertyChanged(nameof(HasCatalogQuery));
        OnPropertyChanged(nameof(HasCatalogResults));
        OnPropertyChanged(nameof(CatalogEmptyTitle));
        OnPropertyChanged(nameof(CatalogEmptyDetail));
        OnPropertyChanged(nameof(CatalogResultCountLabel));
        OnPropertyChanged(nameof(CatalogResultsVisibility));
        OnPropertyChanged(nameof(CatalogEmptyVisibility));
        OnPropertyChanged(nameof(CatalogClearSearchVisibility));
        OnPropertyChanged(nameof(CatalogRefreshEmptyVisibility));
    }

    partial void OnSelectedCatalogItemChanged(CatalogModRow? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        SelectedCatalogFile = null;
        CatalogFiles.ReplaceAll(value?.Mod.Files.Select(file => new CatalogFileRow(file)) ?? []);
    }

    private async Task DebounceCatalogQueryAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            await Task.Delay(180, ct);
            await SearchCatalogCacheAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            MasterDebugLog.Write("CATALOG-UI", "Cached catalog search failed.", ex);
        }
    }

    private void EnsureCatalogRuntime()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (catalogRepository is not null) return;

        catalogHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        catalogRepository = new CatalogRepository(s.Database);
        catalogSync = new CatalogSyncService(catalogRepository);
        catalogOrigins = new InstalledCatalogOriginRepository(s.Database);
        catalogOriginChecker = new InstalledCatalogOriginChecker();
        catalogAcquisition = new CatalogAcquisitionService(
            catalogHttp,
            s.Importer,
            catalogOrigins,
            s.Database,
            s.Hashing,
            Path.Combine(s.Paths.NextStateRoot, "CatalogDownloads"));

        var providers = new List<IModCatalogProvider>();
        if (s.Paths.Game.IsMonsterHunterWorld)
        {
            providers.Add(new NexusV3CatalogProvider(new NexusV3Transport(catalogHttp)));
            providers.Add(new GameBananaCatalogProvider(new GameBananaTransport(catalogHttp)));

            var curseForgeApiKey = Environment.GetEnvironmentVariable("MOD_MANAGER_CURSEFORGE_API_KEY");
            var curseForgeGameIdText = Environment.GetEnvironmentVariable("MOD_MANAGER_CURSEFORGE_GAME_ID");
            if (!string.IsNullOrWhiteSpace(curseForgeApiKey)
                && int.TryParse(
                    curseForgeGameIdText,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var curseForgeGameId)
                && curseForgeGameId > 0)
            {
                providers.Add(new CurseForgeCatalogProvider(
                    new CurseForgeTransport(catalogHttp, curseForgeApiKey),
                    [new CurseForgeCatalogGameSource(
                        s.Paths.Game.Id,
                        s.Paths.Game.DisplayName,
                        curseForgeGameId)]));
            }
        }
        catalogProviders = providers;
    }

    private async Task EnsureCatalogLoadedAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (catalogLoaded)
        {
            await SearchCatalogCacheAsync(ct);
            return;
        }

        await RefreshCatalogCoreAsync(ct);
        catalogLoaded = true;
    }

    [RelayCommand]
    private async Task RefreshCatalog()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy(
            "catalog.refresh",
            "Refreshing mod catalog",
            "Contacting supported providers independently, then updating the local searchable cache…",
            true,
            async ct =>
            {
                await RefreshCatalogCoreAsync(ct);
                catalogLoaded = true;
            });
    }

    private async Task RefreshCatalogCoreAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        EnsureCatalogRuntime();
        var sync = catalogSync ?? throw new InvalidOperationException("Catalog sync runtime is unavailable.");
        var providers = catalogProviders ?? Array.Empty<IModCatalogProvider>();

        var successes = 0;
        var failures = 0;
        var discovered = 0;
        var details = new List<string>();

        foreach (var provider in providers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var mode = string.Equals(provider.ProviderId, "nexus", StringComparison.OrdinalIgnoreCase)
                    ? CatalogBrowseMode.Trending
                    : CatalogBrowseMode.RecentlyUpdated;
                var result = await sync.SyncAsync(
                    provider,
                    new CatalogBrowseRequest(s.Paths.Game, Query: null, Mode: mode, Limit: CatalogProviderRefreshLimit),
                    new CatalogSyncOptions(TimeSpan.FromMinutes(30), HydrateFiles: false),
                    ct);
                successes++;
                discovered += result.ItemCount;
                details.Add($"{provider.DisplayName}: {result.ItemCount}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;
                details.Add($"{provider.DisplayName}: unavailable");
                MasterDebugLog.Write(
                    "CATALOG-UI",
                    $"Provider refresh failed without blocking other providers. provider={provider.ProviderId}",
                    ex);
            }
        }

        await SearchCatalogCacheAsync(ct);
        CatalogProviderSummary = providers.Count == 0
            ? "No live catalog provider is configured for this game yet; cached items remain searchable."
            : $"{successes}/{providers.Count} providers refreshed · {discovered} item(s) received" +
              (failures > 0 ? $" · {failures} isolated failure(s)" : "");
        CatalogStatusText = details.Count == 0
            ? "Catalog cache loaded."
            : string.Join("  •  ", details);
    }

    [RelayCommand]
    private async Task ClearCatalogSearch()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (CatalogQuery.Length == 0) return;

        CatalogQuery = "";
        catalogQueryCts?.Cancel();
        catalogQueryCts?.Dispose();
        catalogQueryCts = null;
        await SearchCatalogCacheAsync(CancellationToken.None);
        CatalogStatusText = "Showing cached catalog results.";
    }

    [RelayCommand]
    private async Task SearchCatalog()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var query = CatalogQuery.Trim();
        if (query.Length == 0)
        {
            await SearchCatalogCacheAsync(CancellationToken.None);
            return;
        }

        await RunBusy(
            "catalog.search",
            "Searching mod catalog",
            "Querying providers that advertise text search, then updating the local cache…",
            true,
            ct => SearchCatalogProvidersAsync(query, ct));
    }

    private async Task SearchCatalogProvidersAsync(string query, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        EnsureCatalogRuntime();
        var sync = catalogSync ?? throw new InvalidOperationException("Catalog sync runtime is unavailable.");
        var providers = catalogProviders ?? Array.Empty<IModCatalogProvider>();
        var searchable = providers
            .Where(provider => provider.Capabilities.HasFlag(CatalogProviderCapabilities.Search))
            .ToArray();

        var successes = 0;
        var failures = 0;
        var discovered = 0;
        var details = new List<string>();

        foreach (var provider in searchable)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await sync.SyncAsync(
                    provider,
                    new CatalogBrowseRequest(
                        s.Paths.Game,
                        Query: query,
                        Limit: CatalogProviderRefreshLimit),
                    new CatalogSyncOptions(TimeSpan.FromMinutes(30), HydrateFiles: false),
                    ct);
                successes++;
                discovered += result.ItemCount;
                details.Add($"{provider.DisplayName}: {result.ItemCount}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;
                details.Add($"{provider.DisplayName}: unavailable");
                MasterDebugLog.Write(
                    "CATALOG-UI",
                    $"Provider search failed without blocking cache results. provider={provider.ProviderId}",
                    ex);
            }
        }

        await SearchCatalogCacheAsync(ct);

        if (searchable.Length == 0)
        {
            CatalogProviderSummary =
                "No configured provider advertises remote text search; showing matching cached items only.";
            CatalogStatusText =
                "Cached search complete. Nexus Mods and GameBanana are not probed for unsupported full-catalog search.";
            return;
        }

        CatalogProviderSummary =
            $"{successes}/{searchable.Length} search-capable provider(s) queried · {discovered} item(s) received" +
            (failures > 0 ? $" · {failures} isolated failure(s)" : "");
        CatalogStatusText = string.Join("  •  ", details);
    }

    private async Task SearchCatalogCacheAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        EnsureCatalogRuntime();
        var repository = catalogRepository ?? throw new InvalidOperationException("Catalog repository is unavailable.");
        var rows = await repository.SearchAsync(
            CatalogQuery,
            gameId: s.Paths.Game.Id,
            includeStale: true,
            limit: CatalogVisibleResultLimit,
            now: DateTimeOffset.UtcNow,
            ct: ct);
        var now = DateTimeOffset.UtcNow;
        var projected = rows
            .Select(row => new CatalogModRow(row.Mod, row.IsStale(now)))
            .ToArray();
        var selected = SelectedCatalogItem;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CatalogItems.ReplaceAll(projected);
            if (selected is not null)
            {
                SelectedCatalogItem = projected.FirstOrDefault(row =>
                    string.Equals(row.Mod.ProviderId, selected.Mod.ProviderId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(row.Mod.ProviderModId, selected.Mod.ProviderModId, StringComparison.OrdinalIgnoreCase));
            }
            NotifyCatalogResultState();
        });
    }

    [RelayCommand]
    private async Task LoadCatalogFiles()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var selected = SelectedCatalogItem;
        if (selected is null)
        {
            CatalogStatusText = "Select a catalog mod first.";
            return;
        }

        await RunBusy(
            "catalog.files",
            "Loading mod files",
            $"Loading provider file identities for {selected.Name}…",
            true,
            async ct =>
            {
                EnsureCatalogRuntime();
                var provider = FindCatalogProvider(selected.Mod.ProviderId)
                    ?? throw new InvalidOperationException(
                        $"Provider '{selected.Mod.ProviderId}' is not available in this app session.");
                var files = await provider.GetModFilesAsync(
                    s.Paths.Game,
                    selected.Mod.ProviderModId,
                    ct);
                CatalogFiles.ReplaceAll(files.Select(file => new CatalogFileRow(file)));
                SelectedCatalogFile = CatalogFiles.FirstOrDefault();
                CatalogStatusText = files.Count == 0
                    ? "This provider did not expose installable files for the selected mod."
                    : $"Loaded {files.Count} exact provider file(s). Select one before installing.";
            });
    }

    [RelayCommand]
    private async Task InstallCatalogFile()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var selected = SelectedCatalogItem;
        var selectedFile = SelectedCatalogFile;
        if (selected is null || selectedFile is null)
        {
            CatalogStatusText = "Select a catalog mod and an exact file first.";
            return;
        }

        await RunBusy(
            "catalog.install",
            "Installing catalog mod",
            "Resolving the provider-authorized download and routing the archive through the normal safety checks…",
            true,
            async ct =>
            {
                EnsureCatalogRuntime();
                var provider = FindCatalogProvider(selected.Mod.ProviderId)
                    ?? throw new InvalidOperationException(
                        $"Provider '{selected.Mod.ProviderId}' is not available in this app session.");
                var acquisition = catalogAcquisition
                    ?? throw new InvalidOperationException("Catalog acquisition service is unavailable.");

                var result = await acquisition.AcquireAsync(
                    provider,
                    s.Paths.Game,
                    selected.Mod,
                    selectedFile.File,
                    ct);

                if (result.Kind == CatalogImportOutcomeKind.Assisted)
                {
                    if (result.AssistedUri is null)
                        throw new InvalidDataException("Assisted catalog acquisition did not return a provider page.");
                    OpenExternalUri(result.AssistedUri);
                    CatalogStatusText = result.Message + " Put the downloaded archive in Inbox or use Install Mod; no provider identity is guessed.";
                    return;
                }

                CatalogStatusText = result.Message;
                if (result.Kind == CatalogImportOutcomeKind.Imported)
                {
                    await ReloadMods(ct);
                    await RefreshAnalysis(ct);
                    await CheckCatalogUpdatesCoreAsync(ct);
                }
            });
    }

    [RelayCommand]
    private void OpenCatalogSource()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var selected = SelectedCatalogItem;
        if (selected is null)
        {
            CatalogStatusText = "Select a catalog mod first.";
            return;
        }

        if (!Uri.TryCreate(selected.Mod.SourceUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            CatalogStatusText = "The cached source URL is not a safe HTTPS provider page.";
            return;
        }

        OpenExternalUri(uri);
    }

    [RelayCommand]
    private async Task CheckCatalogUpdates()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy(
            "catalog.installed-origin-check",
            "Checking exact mod origins",
            "Re-checking only recorded provider mod/file identities. Replacement files are never guessed…",
            true,
            CheckCatalogUpdatesCoreAsync);
    }

    private async Task CheckCatalogUpdatesCoreAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        EnsureCatalogRuntime();
        var origins = catalogOrigins ?? throw new InvalidOperationException("Catalog origin repository is unavailable.");
        var checker = catalogOriginChecker ?? throw new InvalidOperationException("Catalog origin checker is unavailable.");
        var rows = new List<CatalogOriginStatusRow>();

        foreach (var origin in await origins.GetAllAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            var provider = FindCatalogProvider(origin.ProviderId);
            if (provider is null)
            {
                rows.Add(new CatalogOriginStatusRow(
                    origin.ModId,
                    origin.ProviderId,
                    origin.ProviderModId,
                    origin.ProviderFileId,
                    InstalledCatalogOriginCheckState.UpdatesUnsupported.ToString(),
                    "The recorded provider is not active for this game/session."));
                continue;
            }

            try
            {
                var result = await checker.CheckAsync(provider, s.Paths.Game, origin, ct);
                rows.Add(new CatalogOriginStatusRow(
                    origin.ModId,
                    origin.ProviderId,
                    origin.ProviderModId,
                    origin.ProviderFileId,
                    result.State.ToString(),
                    result.Message));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                rows.Add(new CatalogOriginStatusRow(
                    origin.ModId,
                    origin.ProviderId,
                    origin.ProviderModId,
                    origin.ProviderFileId,
                    "CheckFailed",
                    ex.Message));
                MasterDebugLog.Write(
                    "CATALOG-UI",
                    $"Installed-origin check failed without guessing a replacement. mod={origin.ModId}; provider={origin.ProviderId}",
                    ex);
            }
        }

        CatalogOriginStatuses.ReplaceAll(rows);
        OnPropertyChanged(nameof(CatalogOriginAttentionCount));
        CatalogStatusText = rows.Count == 0
            ? "No installed catalog origins are recorded yet."
            : $"Checked {rows.Count} exact installed origin(s); {CatalogOriginAttentionCount} need review.";
    }

    private IModCatalogProvider? FindCatalogProvider(string providerId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return catalogProviders?.FirstOrDefault(provider =>
            string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
    }

    private static void OpenExternalUri(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"host={uri.Host}");
        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidDataException("Catalog links must be credential-free absolute HTTPS URLs.");
        }

        using var process = ProcessDebug.Start(
            new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            },
            "catalog-provider-link");
    }

    private void DisposeCatalogRuntime()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        catalogQueryCts?.Cancel();
        catalogQueryCts?.Dispose();
        catalogQueryCts = null;
        catalogHttp?.Dispose();
        catalogHttp = null;
    }
}
