using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RefreshProfiles(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await ProfilesPage.RefreshAsync(ct);
    }
}
