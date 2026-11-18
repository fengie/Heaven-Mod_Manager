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

        var preview = await s.PlannerSnapshots.LoadAsync(CancellationToken.None);
        var previewMain = parentRow.Members.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previewChild = childRow.Members.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mainPaths = preview.Files.Where(f => previewMain.Contains(f.ModId))
            .Select(f => f.Path).ToHashSet(PathRules.Comparer);
        var sharedPaths = preview.Files.Where(f => previewChild.Contains(f.ModId) && mainPaths.Contains(f.Path))
            .Select(f => f.Path).Distinct(PathRules.Comparer).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sharedPaths.Length == 0 || sharedPaths.Length > 20)
        {
            StatusText = "The suggested family needs detailed conflict review: either no current shared target or more than 20 affected paths.";
            return;
        }

        var existingPairRules = preview.Rules.Where(rule => rule.Scope == RuleScope.ModPair &&
            rule.LeftModId is not null && rule.RightModId is not null &&
            (previewMain.Contains(rule.LeftModId) && previewChild.Contains(rule.RightModId) ||
             previewMain.Contains(rule.RightModId) && previewChild.Contains(rule.LeftModId))).ToArray();
        if (existingPairRules.Length > 0)
        {
            StatusText = "An existing conflict rule would be replaced. Resolve it in the conflict editor; this shortcut has changed nothing.";
            return;
        }

        // This shortcut is an explicit mod-wide ordering decision, not automatic classification.
        // Show every affected existing file and warn about *future* overlaps before persisting it.
        if (MessageBox.Show(
                $"Attach '{childRow.DisplayName}' under '{parentRow.DisplayName}'?\n\n" +
                $"{selection.Evidence}\n\n" +
                $"Current overlapping target paths ({sharedPaths.Length}):\n" +
                string.Join("\n", sharedPaths) + "\n\n" +
                "IMPORTANT: This creates explicit MOD-WIDE overwrite precedence for these packages, including any new overlapping files imported later. " +
                "Matching paths alone do not prove compatibility. No game files are changed until Apply Changes.\n\nContinue?",
                "Review complete family overwrite scope",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
                if (!previewMain.SetEquals(mainIds) || !previewChild.SetEquals(childIds))
                    throw new InvalidOperationException("The family members changed since review; no ordering rule was saved.");
                var freshMainPaths = files.Where(f => previewMain.Contains(f.ModId))
                    .Select(f => f.Path).ToHashSet(PathRules.Comparer);
                var freshShared = files.Where(f => previewChild.Contains(f.ModId) && freshMainPaths.Contains(f.Path))
                    .Select(f => f.Path).Distinct(PathRules.Comparer).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                if (!freshShared.SequenceEqual(sharedPaths, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The overlapping files changed since review; inspect the new scope first.");
                // Recheck for existing pair rules *inside the same SQLite transaction*
                // that persists this family, closing the review-to-write race.
                var familyId = await s.Database.ChainManualFamilyAsync(
                    selection.ParentId, mainIds, [childIds], parentFamily.DisplayName,
                    refuseExistingPairRules: true, ct: ct);
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
