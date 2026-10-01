using CommunityToolkit.Mvvm.ComponentModel;
using MhwModManager.Core;
using MhwModManager.Updater;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty] private bool autoUpdateEnabled;
    [ObservableProperty] private bool uiAnimationsEnabled;
    [ObservableProperty] private bool rememberLastTab;
    [ObservableProperty] private bool confirmBeforeApply;
    [ObservableProperty] private bool confirmBeforeDiscardStaged;
    [ObservableProperty] private bool backgroundMetadataRefreshEnabled;

    partial void OnAutoUpdateEnabledChanged(bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        s.Settings.Update(settings=>settings.AutoUpdateEnabled=value);
        if(!UpdateClientService.CanSelfUpdate(UpdateClientService.GetInstallRoot()))
        {
            ProgramUpdateStatus="Self-update is disabled for this development/unmanaged installation.";
            return;
        }

        if(!value)
        {
            ProgramUpdateStatus="Automatic program updates are off. Manual checks remain available.";
            return;
        }

        ProgramUpdateStatus="Automatic program updates are on. Checking for updates…";
        if(stagedProgramUpdate is not null&&!stagedProgramUpdateRequestedManually)
            ScheduleStagedProgramHandoff();
        if(programUpdaterStarted)
            _=CheckAndStageProgramUpdateAsync(manual:false,backgroundCts.Token);
        else
            StartProgramUpdater();
    }

    partial void OnUiAnimationsEnabledChanged(bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        s.Settings.Update(settings=>settings.UiAnimationsEnabled=value);
        UiMotion.AnimationsEnabled=value;
    }

    partial void OnRememberLastTabChanged(bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        s.Settings.Update(settings=>
        {
            settings.RememberLastTab=value;
            if(value)settings.LastSelectedTab=Math.Clamp(SelectedTab,0,7);
        });
    }

    partial void OnConfirmBeforeApplyChanged(bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        s.Settings.Update(settings=>settings.ConfirmBeforeApply=value);
    }

    partial void OnConfirmBeforeDiscardStagedChanged(bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        s.Settings.Update(settings=>settings.ConfirmBeforeDiscardStaged=value);
    }

    partial void OnBackgroundMetadataRefreshEnabledChanged(bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"value={value}");
        s.Settings.Update(settings=>settings.BackgroundMetadataRefreshEnabled=value);
    }
}
