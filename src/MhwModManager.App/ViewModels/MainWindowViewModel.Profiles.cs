using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task RefreshProfiles()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("profile.refresh","Profiles","Refreshing profile metadata…",true,RefreshProfiles);
    }

    private async Task RefreshProfiles(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await ProfilesPage.RefreshAsync(ct);
    }
}
