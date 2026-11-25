using System.Windows;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RefreshOverlaps(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var heatmap=await s.Inspector.HeatmapAsync(CaptureStage(),ct);
        var rows=heatmap.Select(item=>
        {
            var blocking=item.NeedsChoice;
            var providers=string.Join("  •  ",item.Providers);
            var resolution=blocking?"Needs choice":"Resolved in plan";
            var detail=blocking
                ?"The pending resolver found an unresolved choice or global blocker. Inspect the exact file decision."
                :"The pending resolver has a winner. This does not mean the file is already deployed.";
            var primaryPath=item.Paths is { Count: > 0 } ? item.Paths[0] : item.AssetKey;
            return new AssetOverlapRow(item.AssetKey,item.DisplayName,item.ProviderCount,providers,resolution,detail,primaryPath);
        }).OrderByDescending(x=>StringComparer.OrdinalIgnoreCase.Equals(x.Resolution,"Needs choice"))
          .ThenByDescending(x=>x.ProviderCount)
          .ThenBy(x=>x.DisplayName,StringComparer.OrdinalIgnoreCase)
          .Take(500)
          .ToArray();
        await Application.Current.Dispatcher.InvokeAsync(()=>
        {
            OverlapRows.ReplaceAll(rows);
            OnPropertyChanged(nameof(OverlapCount));
        });
    }

    [RelayCommand]
    private void ShowOverlaps()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        SelectedTab=6;
    }

    [RelayCommand]
    private async Task ExplainSelectedOverlap()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var selected=SelectedOverlap;
        if(selected is null)
        {
            ExplainWhyStatus="Select a shared file first.";
            return;
        }

        await RunBusy("analysis.explain-why","Explaining File Choice","Checking the current mod setup and collecting the reason for this file choice…",true,async ct=>
        {
            var explanation=await s.Inspector.ExplainWhyAsync(selected.PrimaryPath,CaptureStage(),ct);
            SelectedExplanation=explanation;
            ExplainWhyStatus=explanation is null
                ?"No enabled mod currently supplies this file."
                :explanation.Blocking
                    ?"The manager stopped because this file needs your choice."
                    :explanation.AppliedMatchesPlan
                        ?"Recorded applied provider and digest match the pending plan."
                        :"Your pending setup differs from the files currently installed. Check Needs attention for any unresolved choices.";
        });
    }
}
