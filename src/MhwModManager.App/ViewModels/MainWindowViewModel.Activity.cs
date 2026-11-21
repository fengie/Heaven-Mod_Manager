using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RefreshActivity(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await Activity.RefreshAsync(ct);
    }
}
