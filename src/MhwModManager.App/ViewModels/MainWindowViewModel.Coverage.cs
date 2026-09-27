using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task RefreshOutfits()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("outfits.refresh","Refreshing armor coverage","Querying the indexed armor-component map and effective winners…",true,LoadOutfits);
    }

    private async Task LoadOutfits(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var status=await Coverage.RefreshAsync(HasSemanticCoverage,ct);
        if(!string.IsNullOrWhiteSpace(status))StatusText=status;
    }
}
