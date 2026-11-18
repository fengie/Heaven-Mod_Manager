using System.Windows;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Automation;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task ConfirmSuggestedFamily(ModFamilySuggestionRow? selection)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (selection is null) return;

        var parentRow = Mods.FirstOrDefault(row => row.ContainsMember(selection.ParentId));
        var childRow = Mods.FirstOrDefault(row => row.ContainsMember(selection.ChildId));
        if (parentRow is null || childRow is null || ReferenceEquals(parentRow, childRow))
        {
            StatusText = "That family suggestion is no longer valid. Refresh the library before choosing a parent.";
            return;
        }

        if (MessageBox.Show(
                $"Attach '{childRow.DisplayName}' under '{parentRow.DisplayName}'?\n\n" +
                $"{selection.Evidence}\n\n" +
                "This saves an explicit parent/add-on ordering rule. The source archives stay independent; game files do not change until Apply Changes.",
                "Confirm mod family relationship",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var staged = CaptureStage();
        await RunBusy(
            "family.confirm-suggestion",
            "Saving Mod Relationship",
            $"Checking the file evidence and attaching '{childRow.DisplayName}' under '{parentRow.DisplayName}'…",
            true,
            async ct =>
            {
                // The library may have changed after the initial suggestion was shown.
                // Validate against fresh database snapshots before persisting any explicit rules.
                var mods = await s.Database.GetModsAsync(ct);
                var files = await s.Database.GetModFilesAsync(ct);
                var stillSupported = ModRelationshipInference.Suggest(mods, files)
                    .Any(candidate =>
                        candidate.ParentModId.Equals(selection.ParentId, StringComparison.OrdinalIgnoreCase) &&
                        candidate.ChildModId.Equals(selection.ChildId, StringComparison.OrdinalIgnoreCase));
                if (!stillSupported)
                    throw new InvalidOperationException("The proposed relationship is stale or no longer supported by the current file paths.");

                var families = LogicalModFamilies.Build(mods, files, s.Paths.Game);
                var parentFamily = families.FirstOrDefault(f => f.Members.Any(m =>
                    m.Id.Equals(selection.ParentId, StringComparison.OrdinalIgnoreCase)));
                var childFamily = families.FirstOrDefault(f => f.Members.Any(m =>
                    m.Id.Equals(selection.ChildId, StringComparison.OrdinalIgnoreCase)));
                if (parentFamily is null || childFamily is null || ReferenceEquals(parentFamily, childFamily))
                    throw new InvalidOperationException("The selected parent or add-on changed. Refresh and review the relationship again.");

                var mainIds = parentFamily.Members.Select(m => m.Id).ToArray();
                var childIds = childFamily.Members.Select(m => m.Id).ToArray();
                var familyId = await s.Database.ChainManualFamilyAsync(
                    selection.ParentId, mainIds, [childIds], parentFamily.DisplayName, ct);
                await ReloadMods(ct);

                // Family regrouping recreates rows; preserve every unrelated pending toggle
                // rather than applying or discarding the user's staged changes.
                suppressChanged = true;
                try
                {
                    foreach (var row in Mods) row.ApplyProfileState(staged);
                }
                finally
                {
                    suppressChanged = false;
                    Changed();
                }

                await RefreshAnalysis(ct);
                StatusText = $"Linked '{childRow.DisplayName}' as an add-on of '{parentRow.DisplayName}'. Apply Changes is still required to change installed game files.";
                await s.Timeline.RecordAsync("family.suggestion-confirmed", AutomationSeverity.Info,
                    $"User confirmed a suggested family relationship: {childRow.DisplayName} under {parentRow.DisplayName}.",
                    new { familyId, selection.ParentId, selection.ChildId, selection.Score }, ct);
            });
    }
}
