using System.Windows;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RefreshOverlaps(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var heatmap=await s.Inspector.HeatmapAsync(ct);
        var blockingBundles=Conflicts.Select(c=>c.BundleKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows=heatmap.Select(item=>
        {
            var blocking=blockingBundles.Contains(item.AssetKey);
            var providers=string.Join("  •  ",item.Providers);
            var resolution=blocking?"Needs choice":"Resolved overlay";
            var detail=blocking
                ?"These unrelated mods still need you to choose which one wins."
                :"This shared file is already handled automatically by load order, a known mod relationship, identical content, or a saved choice.";
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
            var explanation=await s.Inspector.ExplainWhyAsync(selected.PrimaryPath,ct);
            SelectedExplanation=explanation;
            ExplainWhyStatus=explanation is null
                ?"No enabled mod currently supplies this file."
                :explanation.Blocking
                    ?"The manager stopped because this file needs your choice."
                    :explanation.AppliedMatchesPlan
                        ?"The game is already using the mod selected by the current plan."
                        :"Your pending setup differs from the files currently installed. Apply Mod Changes to make them match.";
        });
    }
}
