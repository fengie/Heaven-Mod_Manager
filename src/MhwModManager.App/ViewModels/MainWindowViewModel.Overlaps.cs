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
                ?"Independent providers still require a decision."
                :"Informational overlap. Priority, family composition, identical bytes, or a shared-resource rule already determines the effective file provider.";
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
            ExplainWhyStatus="Select an overlap first.";
            return;
        }

        await RunBusy("analysis.explain-why","Explaining effective file","Replaying the deterministic planner against the indexed state and collecting its evidence…",true,async ct=>
        {
            var explanation=await s.Inspector.ExplainWhyAsync(selected.PrimaryPath,ct);
            SelectedExplanation=explanation;
            ExplainWhyStatus=explanation is null
                ?"No enabled provider currently supplies that path."
                :explanation.Blocking
                    ?"The resolver intentionally stopped without selecting a provider."
                    :explanation.AppliedMatchesPlan
                        ?"The applied provider matches the current deterministic plan."
                        :"The current plan differs from the applied manifest; Apply safely would reconcile it.";
        });
    }
}
