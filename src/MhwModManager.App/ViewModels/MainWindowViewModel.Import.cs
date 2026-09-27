using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MhwModManager.Core;

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
            var imported=await s.Importer.ImportAsync(dlg.FileName,ct);
            await metadataGate.WaitAsync(ct);
            try{await s.Nexus.RefreshAsync(ct);}
            finally{metadataGate.Release();}
            await ReloadMods(ct);
            StatusText=$"Imported '{imported.DisplayName}'. It is OFF until you stage it.";
        });
    }
}
