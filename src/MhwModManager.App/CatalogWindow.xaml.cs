using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MhwModManager.Automation;
using MhwModManager.Core;

namespace MhwModManager.App;

public partial class CatalogWindow : Window, IDisposable
{
    private readonly AppServices services;
    private readonly List<CatalogFileChoice> fileChoices = [];
    private CancellationTokenSource? requestCts;
    private CatalogMod? selectedMod;

    public CatalogWindow()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        InitializeComponent();
        services = App.Services;
        GameTitle.Text = services.Paths.Game.DisplayName;
        Loaded += CatalogWindowLoaded;
        Closed += CatalogWindowClosed;
    }

    private async void CatalogWindowLoaded(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RefreshCatalogAsync();
    }

    private void CatalogWindowClosed(object? sender, EventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Dispose();
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        requestCts?.Cancel();
        requestCts?.Dispose();
        requestCts = null;
        GC.SuppressFinalize(this);
    }

    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RefreshCatalogAsync();
    }

    private async void SearchKeyDown(object sender, KeyEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await RefreshCatalogAsync();
    }

    private async Task RefreshCatalogAsync()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        requestCts?.Cancel();
        requestCts?.Dispose();
        var cts = requestCts = new CancellationTokenSource();

        try
        {
            SetBusy(true, "Loading catalog…");
            var request = new CatalogBrowseRequest(
                services.Paths.Game,
                SearchBox.Text,
                SelectedBrowseMode(),
                150);
            var result = await services.RemoteCatalog.BrowseAsync(request, cts.Token);
            ModsGrid.ItemsSource = result.Mods;
            ResultCountText.Text = $"{result.Mods.Count} mods";
            SyncText.Text = result.SyncedAt is null
                ? "Not synced"
                : $"Last synced {result.SyncedAt.Value.ToLocalTime():g}" + (result.FromCache ? " · cached" : string.Empty);

            if (result.Mods.Count > 0)
            {
                ModsGrid.SelectedIndex = 0;
                StatusText.Text = "Choose a mod to see its exact provider files.";
            }
            else
            {
                ClearDetails();
                StatusText.Text = "No catalog results are available yet. Connect Nexus Mods or change the search.";
            }
            await RefreshProviderHealthAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            MasterDebugLog.Write("CATALOG-UI", "Catalog refresh failed.", ex);
            StatusText.Text = $"Catalog refresh failed: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(requestCts, cts)) SetBusy(false, StatusText.Text);
        }
    }

    private async void ConnectNexusClick(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var secret = NexusKeyBox.Password;
        if (string.IsNullOrWhiteSpace(secret))
        {
            StatusText.Text = "Enter a Nexus API key first.";
            return;
        }

        try
        {
            SetBusy(true, "Validating Nexus credentials…");
            var result = await services.RemoteCatalog.AuthenticateAsync("nexus", secret, CancellationToken.None);
            NexusKeyBox.Clear();
            StatusText.Text = result.Message;
            await RefreshProviderHealthAsync(CancellationToken.None);
            if (result.Success) await RefreshCatalogAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MasterDebugLog.Write("CATALOG-AUTH", "Nexus authentication failed.", ex);
            StatusText.Text = $"Could not connect Nexus Mods: {ex.Message}";
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void ModSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ModsGrid.SelectedItem is not CatalogMod mod) return;
        await LoadModDetailsAsync(mod);
    }

    private async Task LoadModDetailsAsync(CatalogMod summary)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={summary.ProviderId}; mod={summary.ProviderModId}");
        try
        {
            SetBusy(true, $"Loading {summary.Name}…");
            var detail = await services.RemoteCatalog.GetModAsync(
                summary.ProviderId,
                services.Paths.Game,
                summary.ProviderModId,
                CancellationToken.None) ?? summary;
            selectedMod = detail;

            ModTitleText.Text = detail.Name;
            ModMetaText.Text = $"by {detail.Author} · {detail.ProviderId} · version {detail.Version ?? "unknown"}" +
                               (detail.UpdatedAt is null ? string.Empty : $" · updated {detail.UpdatedAt.Value.ToLocalTime():d}");
            ModSummaryText.Text = detail.Summary;
            ModDescriptionText.Text = detail.Description;
            SourceButton.IsEnabled = Uri.TryCreate(detail.SourceUrl, UriKind.Absolute, out _);

            var files = detail.Files.Count > 0
                ? detail.Files
                : await services.RemoteCatalog.GetFilesAsync(detail.ProviderId, services.Paths.Game, detail.ProviderModId, CancellationToken.None);
            BuildFileChoices(files);
            StatusText.Text = files.Count == 0
                ? "This provider returned no downloadable files for the selected mod."
                : "Choose one Main file and any Optional/Update files you actually want.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            MasterDebugLog.Write("CATALOG-DETAIL", $"Failed loading mod={summary.ProviderModId}", ex);
            selectedMod = summary;
            ModTitleText.Text = summary.Name;
            ModMetaText.Text = $"by {summary.Author} · {summary.ProviderId}";
            ModSummaryText.Text = summary.Summary;
            ModDescriptionText.Text = summary.Description;
            SourceButton.IsEnabled = Uri.TryCreate(summary.SourceUrl, UriKind.Absolute, out _);
            fileChoices.Clear();
            ApplyFileVisibility();
            StatusText.Text = $"Could not load provider files: {ex.Message}";
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private void BuildFileChoices(IReadOnlyList<CatalogModFile> files)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"count={files.Count}");
        fileChoices.Clear();
        foreach (var file in files)
            fileChoices.Add(new(file));

        var defaultMain = fileChoices
            .Where(choice => choice.File.Category == CatalogFileCategory.Main)
            .OrderByDescending(choice => choice.File.Recommended)
            .ThenByDescending(choice => choice.File.UploadedAt)
            .FirstOrDefault();
        if (defaultMain is not null) defaultMain.IsSelected = true;

        ApplyFileVisibility();
        FileHintText.Text = $"{fileChoices.Count(choice => choice.File.Category == CatalogFileCategory.Main)} Main · " +
                            $"{fileChoices.Count(choice => choice.File.Category == CatalogFileCategory.Optional)} Optional · " +
                            $"{fileChoices.Count(choice => choice.File.Category == CatalogFileCategory.Update)} Updates · " +
                            $"{fileChoices.Count(choice => choice.IsOld)} old/archived";
        InstallButton.IsEnabled = fileChoices.Any(choice => choice.IsSelected);
    }

    private void FileSelectionClick(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (sender is not CheckBox checkBox || checkBox.DataContext is not CatalogFileChoice choice) return;
        choice.IsSelected = checkBox.IsChecked == true;
        if (choice.IsSelected && choice.File.Category == CatalogFileCategory.Main)
        {
            foreach (var other in fileChoices.Where(item =>
                         !ReferenceEquals(item, choice) &&
                         item.File.Category == CatalogFileCategory.Main))
                other.IsSelected = false;
            FilesGrid.Items.Refresh();
        }
        InstallButton.IsEnabled = selectedMod is not null && fileChoices.Any(item => item.IsSelected);
    }

    private void ShowOldFilesClick(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ApplyFileVisibility();
    }

    private void ApplyFileVisibility()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        FilesGrid.ItemsSource = fileChoices
            .Where(choice => ShowOldFilesBox.IsChecked == true || !choice.IsOld)
            .OrderBy(choice => choice.CategoryOrder)
            .ThenByDescending(choice => choice.File.UploadedAt)
            .ToArray();
    }

    private async void InstallSelectedClick(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (selectedMod is null) return;
        var selected = fileChoices.Where(choice => choice.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            StatusText.Text = "Select at least one file to install.";
            return;
        }

        var installed = 0;
        InstallButton.IsEnabled = false;
        try
        {
            foreach (var choice in selected)
            {
                StatusText.Text = $"Resolving {choice.File.Name}…";
                var progress = new Progress<double>(value =>
                    StatusText.Text = $"Downloading {choice.File.Name} · {value:P0}");
                var acquisition = await services.CatalogInstall.AcquireAsync(
                    services.Paths.Game,
                    selectedMod,
                    choice.File,
                    progress,
                    CancellationToken.None);

                CatalogDownloadArtifact artifact;
                if (acquisition.Artifact is not null)
                {
                    artifact = acquisition.Artifact;
                }
                else if (acquisition.AssistedUri is not null)
                {
                    OpenUri(acquisition.AssistedUri);
                    var decision = MessageBox.Show(
                        this,
                        acquisition.Message + "\n\nComplete the authorized download on Nexus Mods, then click OK and choose the downloaded archive. The manager will continue through the same safety checks and installer.",
                        "Nexus download authorization required",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Information);
                    if (decision != MessageBoxResult.OK) continue;

                    var picker = new OpenFileDialog
                    {
                        Title = $"Choose the downloaded file for {choice.File.Name}",
                        Filter = "Mod archives|*.zip;*.7z;*.rar|All files|*.*",
                        Multiselect = false
                    };
                    if (picker.ShowDialog(this) != true) continue;
                    artifact = await services.CatalogDownloads.AcceptLocalArchiveAsync(picker.FileName, CancellationToken.None);
                }
                else
                {
                    MessageBox.Show(this, acquisition.Message, "Download unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                    continue;
                }

                var imported = await ImportThroughExistingPipelineAsync(artifact.ArchivePath);
                if (imported is null) continue;
                await services.CatalogInstall.AttachImportedOriginAsync(imported, selectedMod, choice.File, artifact, CancellationToken.None);
                installed++;
            }

            if (installed > 0)
            {
                if (Owner is MainWindow mainWindow) await mainWindow.RefreshAfterCatalogInstallAsync();
                StatusText.Text = $"Installed {installed} selected catalog file(s). They are OFF until you stage/apply them.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            MasterDebugLog.Write("CATALOG-INSTALL", "Catalog installation failed.", ex);
            MessageBox.Show(this, ex.Message, "Catalog install stopped safely", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = $"Install stopped safely: {ex.Message}";
        }
        finally
        {
            InstallButton.IsEnabled = selectedMod is not null && fileChoices.Any(choice => choice.IsSelected);
        }
    }

    private async Task<ArchiveImportResult?> ImportThroughExistingPipelineAsync(string archivePath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"archive={Path.GetFileName(archivePath)}");
        var inspection = await services.Archive.InspectAsync(archivePath, CancellationToken.None);
        var hasFomod = inspection.Entries.Any(entry =>
            entry.Key.Replace('/', '\\').EndsWith(@"fomod\ModuleConfig.xml", StringComparison.OrdinalIgnoreCase));
        if (!hasFomod)
            return await services.Importer.ImportAsync(archivePath, CancellationToken.None);

        var preparation = await services.Importer.PrepareFomodAsync(archivePath, CancellationToken.None);
        var chooser = new FomodInstallerWindow(preparation.Installer, services.Paths.Game) { Owner = this };
        if (chooser.ShowDialog() != true)
        {
            await services.Importer.CancelFomodAsync(preparation);
            return null;
        }
        return await services.Importer.CommitFomodAsync(preparation, chooser.SelectedOptions, services.Paths.Game, CancellationToken.None);
    }

    private void OpenSourceClick(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (selectedMod is null || !Uri.TryCreate(selectedMod.SourceUrl, UriKind.Absolute, out var uri)) return;
        OpenUri(uri);
    }

    private static void OpenUri(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"host={uri.Host}");
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps,StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttp,StringComparison.OrdinalIgnoreCase)) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private async Task RefreshProviderHealthAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var health = await services.RemoteCatalog.GetProviderHealthAsync(ct);
        var nexus = health.FirstOrDefault(item => item.ProviderId.Equals("nexus", StringComparison.OrdinalIgnoreCase));
        if (nexus is null)
        {
            ProviderHealthText.Text = "Nexus: unavailable";
            return;
        }
        var quota = nexus.RateLimit?.DailyRemaining is int daily
            ? $" · {daily:N0} daily requests left"
            : string.Empty;
        ProviderHealthText.Text = $"Nexus: {nexus.State}{quota}";
    }

    private CatalogBrowseMode SelectedBrowseMode()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return BrowseModeBox.SelectedIndex switch
        {
            1 => CatalogBrowseMode.RecentlyUpdated,
            2 => CatalogBrowseMode.Latest,
            3 => CatalogBrowseMode.Popular,
            _ => CatalogBrowseMode.Trending
        };
    }

    private void SetBusy(bool busy, string status)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"busy={busy}");
        StatusText.Text = status;
        SearchBox.IsEnabled = !busy;
        BrowseModeBox.IsEnabled = !busy;
    }

    private void ClearDetails()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        selectedMod = null;
        ModTitleText.Text = "Select a mod";
        ModMetaText.Text = string.Empty;
        ModSummaryText.Text = string.Empty;
        ModDescriptionText.Text = string.Empty;
        SourceButton.IsEnabled = false;
        InstallButton.IsEnabled = false;
        fileChoices.Clear();
        ApplyFileVisibility();
        FileHintText.Text = string.Empty;
    }
}

public sealed class CatalogFileChoice
{
    public CatalogFileChoice(CatalogModFile file)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"file={file.ProviderFileId}");
        File = file;
        Category = file.Category switch
        {
            CatalogFileCategory.Main => "Main",
            CatalogFileCategory.Optional => "Optional",
            CatalogFileCategory.Update => "Update",
            CatalogFileCategory.Miscellaneous => "Misc",
            CatalogFileCategory.OldVersion => "Old",
            CatalogFileCategory.Archived => "Archived",
            CatalogFileCategory.Removed => "Removed",
            _ => "Other"
        };
        Name = file.Name;
        Version = file.Version ?? string.Empty;
        Description = file.Description ?? string.Empty;
        SizeLabel = FormatSize(file.SizeBytes);
        IsOld = file.Category is CatalogFileCategory.OldVersion or CatalogFileCategory.Archived;
        CategoryOrder = file.Category switch
        {
            CatalogFileCategory.Main => 0,
            CatalogFileCategory.Optional => 1,
            CatalogFileCategory.Update => 2,
            CatalogFileCategory.Miscellaneous => 3,
            CatalogFileCategory.OldVersion => 4,
            CatalogFileCategory.Archived => 5,
            _ => 6
        };
    }

    public CatalogModFile File { get; }
    public string Category { get; }
    public string Name { get; }
    public string Version { get; }
    public string Description { get; }
    public string SizeLabel { get; }
    public bool IsOld { get; }
    public int CategoryOrder { get; }
    public bool IsSelected { get; set; }

    private static string FormatSize(long? bytes)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (bytes is null or <= 0) return "—";
        var value = (double)bytes.Value;
        if (value >= 1024 * 1024 * 1024) return $"{value / (1024 * 1024 * 1024):0.##} GB";
        if (value >= 1024 * 1024) return $"{value / (1024 * 1024):0.##} MB";
        if (value >= 1024) return $"{value / 1024:0.##} KB";
        return $"{value:0} B";
    }
}
