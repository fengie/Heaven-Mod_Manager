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
        await Activity.RefreshAsync(ct);
    }
}
