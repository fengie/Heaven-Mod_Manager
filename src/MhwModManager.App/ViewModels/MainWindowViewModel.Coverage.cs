using System.Windows;
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
        if(!HasSemanticCoverage)
        {
            await Application.Current.Dispatcher.InvokeAsync(()=>OutfitRows.Clear());
            StatusText="This generic game profile has file-level coverage through Mods and Overlaps. Add a rich game adapter to define semantic outfit/asset slots.";
            return;
        }

        var coverage=await s.PresentationReads.GetOutfitCoverageAsync(ct);
        var rows=coverage.Select(x=>new OutfitRow(
            x.Armor,
            x.ModelId,
            x.AvailableProviders,
            x.WinningPieces,
            x.AvailableProviders==0?"Unmodded":string.IsNullOrWhiteSpace(x.WinningPieces)?"Available / off":"Active",
            x.PreviewPath,
            x.Providers)).ToArray();
        await Application.Current.Dispatcher.InvokeAsync(()=>OutfitRows.ReplaceAll(rows));
    }
}
