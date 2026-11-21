using System.Windows.Threading;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.App.ViewModels;

public sealed class ActivityPageViewModel
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly PresentationReadRepository reads;

    public ActivityPageViewModel(PresentationReadRepository reads)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.reads=reads;
    }

    public ObservableRangeCollection<ActivityRow> Rows{get;}=[];

    public async Task RefreshAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var entries=await reads.GetRecentActivityAsync(300,ct);
        var rows=entries.Select(x=>new ActivityRow(x.Id,x.State,x.Description,x.StartedAt)).ToArray();
        await dispatcher.InvokeAsync(()=>Rows.ReplaceAll(rows));
    }
}
