using System.Windows.Threading;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.App.ViewModels;

public sealed class ProfilesPageViewModel
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly ProfileRepository profiles;

    public ProfilesPageViewModel(ProfileRepository profiles)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.profiles=profiles;
    }

    public ObservableRangeCollection<ProfileSummary> Rows{get;}=[];

    public async Task RefreshAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var rows=await profiles.ListAsync(ct);
        await dispatcher.InvokeAsync(() =>
        {
            if (!Rows.SequenceEqual(rows)) Rows.ReplaceAll(rows);
        });
    }
}
