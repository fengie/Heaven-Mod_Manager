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
        await RunBusy("archive.import","Inspecting archive","Checking paths and extracting to a quarantined staging folder…",true,async ct=>
        {
            ArchiveImportResult imported;
            var inspection=await s.Archive.InspectAsync(dlg.FileName,ct);
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
                        StatusText="FOMOD import canceled; the source archive was left untouched.";
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

            await metadataGate.WaitAsync(ct);
            try{await s.Nexus.RefreshAsync(ct);}
            finally{metadataGate.Release();}
            await ReloadMods(ct);
            StatusText=$"Imported '{imported.DisplayName}'. It is OFF until you stage it.";
        });
    }
}
