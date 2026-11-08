using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task ImportArchive()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var dlg=new OpenFileDialog{Filter="Mod archives|*.zip;*.7z;*.rar|All files|*.*",Multiselect=false};
        if(dlg.ShowDialog()!=true)return;
        await RunBusy("archive.import","Checking Mod","Making sure the downloaded package is safe to import and finding its install options…",true,async ct=>
        {
            ArchiveImportResult imported;
            var inspection=await s.Archive.InspectAsync(dlg.FileName,ct);
            // Hash the selected source before import; the saved archive must still match
            // these bytes after the import succeeds.
            var originalHash=await s.Hashing.HashFileAsync(dlg.FileName,true,ct);
            var hasFomod=inspection.Entries.Any(entry =>
                entry.Key.Replace('/','\\').EndsWith(@"fomod\ModuleConfig.xml",StringComparison.OrdinalIgnoreCase));
            if(hasFomod)
            {
                var preparation=await s.Importer.PrepareFomodAsync(dlg.FileName,ct);
                try
                {
                    var chooser=new FomodInstallerWindow(preparation.Installer,s.Paths.Game)
                    {
                        Owner=Application.Current.MainWindow
                    };
                    if(chooser.ShowDialog()!=true)
                    {
                        StatusText="Mod installation canceled. The downloaded file was left unchanged.";
                        return;
                    }
                    imported=await s.Importer.CommitFomodAsync(preparation,chooser.SelectedOptions,s.Paths.Game,ct);
                }
                finally
                {
                    await s.Importer.CancelFomodAsync(preparation);
                }
            }
            else imported=await s.Importer.ImportAsync(dlg.FileName,ct);

            string? downloadWarning=null;
            try
            {
                await GameDownloadArchiveStore.SaveAsync(
                    s.Paths.DownloadsRoot,dlg.FileName,imported.DisplayName,originalHash.Sha256,ct);
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested)
            {
                downloadWarning=" Original archive retention was cancelled after the mod was imported.";
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            {
                MasterDebugLog.Write("ARCHIVE-IMPORT",
                    "The mod was imported, but its original archive could not be retained in game-scoped Downloads.",ex);
                downloadWarning=" The mod was imported, but its original archive could not be saved in this game's Downloads folder.";
            }

            await metadataGate.WaitAsync(ct);
            try{await s.Nexus.RefreshAsync(ct);}
            finally{metadataGate.Release();}
            await ReloadMods(ct);
            StatusText=$"Imported '{imported.DisplayName}'. It is OFF until you stage it."+downloadWarning;
        });
    }
}
