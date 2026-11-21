using System.Windows;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.App.ViewModels;

public sealed class CoveragePageViewModel
{
    private readonly PresentationReadRepository reads;

    public CoveragePageViewModel(PresentationReadRepository reads)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.reads=reads;
    }

    public ObservableRangeCollection<OutfitRow> Rows{get;}=[];

    public async Task<string?> RefreshAsync(bool hasSemanticCoverage,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"semanticCoverage={hasSemanticCoverage}");
        if(!hasSemanticCoverage)
        {
            await Application.Current.Dispatcher.InvokeAsync(Rows.Clear);
            return "This generic game profile has file-level coverage through Mods and Overlaps. Add a rich game adapter to define semantic outfit/asset slots.";
        }

        var coverage=await reads.GetOutfitCoverageAsync(ct);
        var options=await reads.GetArmorOptionsAsync(ct);
        var rows=coverage.Select(x=>new OutfitRow(
            x.Armor,
            x.ModelId,
            options[x.ModelId].Count(),
            x.WinningPieces,
            !options[x.ModelId].Any()?"Unmodded":string.IsNullOrWhiteSpace(x.WinningPieces)?"Available / off":"Active",
            x.PreviewPath,
            string.Join(", ",options[x.ModelId].Select(o=>o.DisplayName))) { Options=options[x.ModelId].ToArray() }).ToArray();
        await Application.Current.Dispatcher.InvokeAsync(()=>Rows.ReplaceAll(rows));
        return null;
    }
}
