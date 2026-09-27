using System.Windows;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task RefreshActivity()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("activity.refresh","Activity","Reading recent transactional history…",true,RefreshActivity);
    }

    private async Task RefreshActivity(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var entries=await s.PresentationReads.GetRecentActivityAsync(300,ct);
        var rows=entries.Select(x=>new ActivityRow(x.Id,x.State,x.Description,x.StartedAt)).ToArray();
        await Application.Current.Dispatcher.InvokeAsync(()=>ActivityRows.ReplaceAll(rows));
    }
}
