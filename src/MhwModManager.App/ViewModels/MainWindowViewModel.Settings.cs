using System.IO;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Updater;

namespace MhwModManager.App.ViewModels;

public sealed record StorageCategoryRow(
    string Name,
    string Size,
    string Retention);

public static class StorageUsageProbe
{
    public static long MeasureTree(
        string root,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={root}");
        if (!Directory.Exists(root)) return 0;

        var snapshot = SafeRecursiveTraversal.Snapshot(root, ct);
        long total = 0;
        foreach (var file in snapshot.Files)
        {
            ct.ThrowIfCancellationRequested();
            var length = new FileInfo(file).Length;
            if (total > long.MaxValue - length)
                throw new IOException($"Storage measurement overflowed for root: {root}");
            total += length;
        }

        return total;
    }
}

public sealed partial class MainWindowViewModel
{
    [ObservableProperty] private bool autoUpdateEnabled;
    [ObservableProperty] private bool uiAnimationsEnabled;
    [ObservableProperty] private bool rememberLastTab;
    [ObservableProperty] private bool confirmBeforeApply;
    [ObservableProperty] private bool confirmBeforeDiscardStaged;
    [ObservableProperty] private bool backgroundMetadataRefreshEnabled;
    [ObservableProperty] private string storageStatusText =
        "Storage usage has not been measured yet. Opening Settings measures manager-owned roots without deleting anything.";
    [ObservableProperty] private string legalStatusText =
        "Privacy, legal, support, and local-data controls are bundled with this release.";

    private bool storageUsageLoaded;

    public ObservableRangeCollection<StorageCategoryRow> StorageCategories { get; } = [];

    partial void OnAutoUpdateEnabledChanged(bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        s.Settings.Update(settings=>settings.AutoUpdateEnabled=value);
        if(!UpdateClientService.CanSelfUpdate(UpdateClientService.GetInstallRoot()))
        {
            ProgramUpdateStatus="Self-update is disabled for this development/unmanaged installation.";
            return;
        }

        if(!value)
        {
            ProgramUpdateStatus="Automatic program updates are off. Manual checks remain available.";
            return;
        }

        ProgramUpdateStatus="Automatic program updates are on. Checking for updates…";
        if(stagedProgramUpdate is not null&&!stagedProgramUpdateRequestedManually)
            ScheduleStagedProgramHandoff();
        if(programUpdaterStarted)
            _=CheckAndStageProgramUpdateAsync(manual:false,backgroundCts.Token);
        else
            StartProgramUpdater();
    }

    partial void OnUiAnimationsEnabledChanged(bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        s.Settings.Update(settings=>settings.UiAnimationsEnabled=value);
        UiMotion.AnimationsEnabled=value;
    }

    partial void OnRememberLastTabChanged(bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        s.Settings.Update(settings=>
        {
            settings.RememberLastTab=value;
            if(value)settings.LastSelectedTab=Math.Clamp(SelectedTab,0,7);
        });
    }

    partial void OnConfirmBeforeApplyChanged(bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        s.Settings.Update(settings=>settings.ConfirmBeforeApply=value);
    }

    partial void OnConfirmBeforeDiscardStagedChanged(bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        s.Settings.Update(settings=>settings.ConfirmBeforeDiscardStaged=value);
    }

    partial void OnBackgroundMetadataRefreshEnabledChanged(bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        s.Settings.Update(settings=>settings.BackgroundMetadataRefreshEnabled=value);
    }

    [RelayCommand]
    private void OpenPrivacyPolicy()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        OpenBundledComplianceDocument("PRIVACY.md");
    }

    [RelayCommand]
    private void OpenTermsOfUse()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        OpenBundledComplianceDocument("TERMS.md");
    }

    [RelayCommand]
    private void OpenRefundPolicy()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        OpenBundledComplianceDocument("REFUND_POLICY.md");
    }

    [RelayCommand]
    private void OpenCookiePolicy()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        OpenBundledComplianceDocument("COOKIE_POLICY.md");
    }

    [RelayCommand]
    private void OpenThirdPartyNotices()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        OpenBundledComplianceDocument("THIRD_PARTY_NOTICES.md");
    }

    [RelayCommand]
    private void OpenDataDeletionGuide()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        OpenBundledComplianceDocument("DATA-DELETION.md");
    }

    [RelayCommand]
    private void OpenSupportDetails()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        OpenBundledComplianceDocument("SUPPORT.md");
    }

    [RelayCommand]
    private void OpenAppDataFolder()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            Directory.CreateDirectory(s.Paths.StateRoot);
            ProcessDebug.Start(new ProcessStartInfo(s.Paths.StateRoot) { UseShellExecute = true }, "open-manager-data-folder");
            LegalStatusText = "Opened the manager data folder. Close the app before deleting state files.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            LegalStatusText = $"Could not open the manager data folder: {ex.Message}";
            MasterDebugLog.Write("LEGAL-UI", "Opening manager data folder failed.", ex);
        }
    }

    private void OpenBundledComplianceDocument(string fileName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"file={fileName}");
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path))
            {
                LegalStatusText = $"{fileName} is missing from this installation. Reinstall the current verified build.";
                return;
            }

            ProcessDebug.Start(new ProcessStartInfo(path) { UseShellExecute = true }, $"open-compliance-document:{fileName}");
            LegalStatusText = $"Opened {fileName}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            LegalStatusText = $"Could not open {fileName}: {ex.Message}";
            MasterDebugLog.Write("LEGAL-UI", $"Opening compliance document failed: {fileName}", ex);
        }
    }

    private async Task EnsureStorageUsageLoadedAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (storageUsageLoaded) return;

        try
        {
            await RefreshStorageUsageCoreAsync(ct);
            storageUsageLoaded = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StorageStatusText =
                $"Storage measurement stopped safely: {ex.Message}";
            MasterDebugLog.Write(
                "STORAGE-UI",
                "Initial storage measurement failed.",
                ex);
        }
    }

    [RelayCommand]
    private async Task RefreshStorageUsage()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy(
            "storage.measure",
            "Measuring manager storage",
            "Reading manager-owned storage roots without changing files…",
            true,
            async ct =>
            {
                await RefreshStorageUsageCoreAsync(ct);
                storageUsageLoaded = true;
            });
    }

    [RelayCommand]
    private async Task ReclaimUpdaterStorage()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy(
            "storage.reclaim-updater",
            "Reclaiming updater storage",
            "Removing only updater-owned orphan staging and terminal recovery data that the existing safety boundary proves reclaimable…",
            true,
            async ct =>
            {
                var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
                var before = await MeasureOwnedRootAsync(updaterRoot, ct);
                UpdateStorageCleanupResult result;
                try
                {
                    result = await UpdateStorageMaintenance.RunAsync(
                        message => MasterDebugLog.Write("STORAGE-UI", message),
                        ct);
                }
                catch (Exception ex) when (
                    ex is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException)
                {
                    StorageStatusText =
                        $"Reclaim stopped safely without touching Mods or State: {ex.Message}";
                    throw;
                }

                var after = await MeasureOwnedRootAsync(updaterRoot, ct);
                var reclaimed = Math.Max(0, before - after);
                await RefreshStorageUsageCoreAsync(ct);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StorageStatusText =
                        $"Reclaimed {FormatStorageBytes(reclaimed)} from updater-owned disposable data. " +
                        $"Removed {result.DeletedStagingAttempts} orphan staging attempt(s) and " +
                        $"{result.DeletedTransactions} terminal recovery transaction(s); " +
                        $"{result.DeferredEntries} entry/entries were preserved or deferred.";
                });
            });
    }

    private async Task RefreshStorageUsageCoreAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
        var specifications = new (string Name, string Root, string Retention)[]
        {
            (
                "Installed Mods",
                s.Paths.ModsRoot,
                "Durable mod library. Reclaim never deletes this root."),
            (
                "Archived Mods",
                s.Paths.ModsArchiveRoot,
                "Durable user archive. Reclaim never deletes this root."),
            (
                "Manager State (total)",
                s.Paths.StateRoot,
                "Durable state/database total; includes managed state subdirectories and is not additive with the rows below."),
            (
                "Managed Package Blobs",
                s.Paths.BlobRoot,
                "Durable content-addressed package data. Reclaim never deletes this root."),
            (
                "Catalog Download Scratch",
                Path.Combine(s.Paths.NextStateRoot, "CatalogDownloads"),
                "Temporary provider downloads. Successful imports self-delete; crash/deletion residue is lease-owned and reclaimed at startup or before the next direct acquisition after a one-hour grace period, with a 512 MiB reclaimable-residue quota. Active, unknown, and unleased files are preserved."),
            (
                "Updater Staging",
                Path.Combine(updaterRoot, "staging"),
                "Updater-owned staging. Only old orphan attempts proven safe are reclaimable."),
            (
                "Updater Recovery",
                Path.Combine(updaterRoot, "transactions"),
                "Rollback/recovery data. Only terminal confirmed or rolled-back transactions proven safe are reclaimable.")
        };

        var measurements = await Task.WhenAll(
            specifications.Select(async specification =>
            {
                try
                {
                    var bytes = await MeasureOwnedRootAsync(
                        specification.Root,
                        ct);
                    return new StorageCategoryRow(
                        specification.Name,
                        FormatStorageBytes(bytes),
                        specification.Retention);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (
                    ex is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException)
                {
                    MasterDebugLog.Write(
                        "STORAGE-UI",
                        $"Storage measurement refused category={specification.Name}",
                        ex);
                    return new StorageCategoryRow(
                        specification.Name,
                        "Unavailable",
                        specification.Retention + " Measurement was refused because the root could not be traversed safely.");
                }
            }));

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            StorageCategories.ReplaceAll(measurements);
            StorageStatusText =
                "Storage totals refreshed from the current manager-owned roots. Durable rows are shown for visibility only; the reclaim action is limited to updater-owned disposable data.";
        });
    }

    private static Task<long> MeasureOwnedRootAsync(
        string root,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={root}");
        return Task.Run(
            () => StorageUsageProbe.MeasureTree(root, ct),
            ct);
    }

    public static string FormatStorageBytes(long value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (value < 1024) return $"{value} B";
        if (value < 1024L * 1024L) return $"{value / 1024d:F1} KiB";
        if (value < 1024L * 1024L * 1024L) return $"{value / 1024d / 1024d:F1} MiB";
        return $"{value / 1024d / 1024d / 1024d:F1} GiB";
    }
}
