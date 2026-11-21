using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{

    [ObservableProperty] private int outfitDetailsTab;

    [RelayCommand]
    private void ShowOutfitOptions(OutfitRow outfit)
    {
        using var trace = MasterDebugLog.BeginMethod();
        SelectedOutfit=outfit;
        OutfitDetailsTab=outfit.Available==0 ? 1 : 0;
    }

    [RelayCommand]
    private Task ChooseOutfit(ArmorOutfitOption option)
    {
        using var trace = MasterDebugLog.BeginMethod();
        return ChangeOutfit(new(option.ModelId,option.ModId));
    }

    [RelayCommand]
    private Task ClearOutfitChoice(OutfitRow outfit)
    {
        using var trace = MasterDebugLog.BeginMethod();
        return ChangeOutfit(new(outfit.ModelId,null));
    }

    private Task ChangeOutfit(ArmorOutfitSelection selection)
    {
        using var trace = MasterDebugLog.BeginMethod();
        return RunBusy("coverage.choose","Changing armor outfit","Checking the selected outfit and its game files…",false,async ct =>
        {
            var armor = SelectedOutfit;
            if (!HasSemanticCoverage || armor?.ModelId != selection.ModelId)
                throw new InvalidOperationException("Select an armor in the current game's Coverage page.");
            if (StagedCount != 0)
                throw new InvalidOperationException("Resolve pending mod changes before choosing an outfit.");
            if (s.ProcessGuard.GetKnownBlockers().Count > 0)
                throw new InvalidOperationException("Close the game before changing an armor outfit.");

            var snapshot = await s.PlannerSnapshots.LoadAsync(ct);
            var stage = snapshot.Mods.ToDictionary(m => m.Id,m => (enabled:m.Enabled,priority:m.Priority),PathRules.Comparer);
            if (selection.ModId is { } provider)
            {
                var family = LogicalModFamilies.Build(snapshot.Mods,snapshot.Files,s.Paths.Game)
                    .SingleOrDefault(f => f.Members.Any(m => m.Id == provider));
                if (family is null) throw new InvalidOperationException("This outfit is no longer available.");
                var row = new ModRowViewModel(family,() => {});
                if (!row.CanStage) throw new InvalidOperationException("This outfit needs layout review.");
                row.SetMemberEnabled(provider,true);
                foreach (var member in row.ExpandStage()) stage[member.Key] = member.Value;
                await s.Catalog.EnsureCapturedAsync(snapshot.Mods.Where(m => stage[m.Id].enabled &&
                    (!m.Enabled || m.Id == provider)).Select(m => m with { Enabled=true }).ToArray(),ct);
            }

            // Refresh the index after capture, including the membership of this exact armor model.
            snapshot = await s.PlannerSnapshots.LoadAsync(null,selection,ct);
            var metadata = await ArmorOutfitPreference.PrepareAsync(s.Database,selection,snapshot,ct);
            var staged = snapshot.Mods.Select(m => m with { Enabled=stage[m.Id].enabled, Priority=stage[m.Id].priority }).ToArray();
            var plan = await Task.Run(() => s.Planner.Build(snapshot with { Mods=staged }),ct);
            if (plan.IsBlocked)
                throw new InvalidOperationException(plan.Conflicts.First(c => c.Blocking).Explanation);
            var dependencies = await GetDependencyBlockersAsync(stage,plan,ct);
            if (dependencies.Length > 0) throw new InvalidOperationException(DescribeDependencyBlockers(dependencies));
            var outside = ArmorOutfitPreference.OutsideArmor(plan,selection.ModelId);
            if (outside.Count > 0)
            {
                var examples = string.Join(Environment.NewLine,outside.Take(6).Select(c => $"{c.Kind}  {c.Path}"));
                var answer = MessageBox.Show($"This change also affects {outside.Count} files outside {armor.Armor}." +
                    Environment.NewLine + examples + Environment.NewLine + "Apply these package changes with the outfit?",
                    "Additional package files",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                {
                    StatusText="Outfit choice cancelled.";
                    return;
                }
            }
            if (metadata.Count > 0 || plan.Changes.Count > 0 || staged.Where((m,i) => m.Enabled != snapshot.Mods[i].Enabled).Any())
            {
                var checkedPlan = plan with { Changes=plan.Changes.Select(change => change.Kind==ChangeKind.Add
                    ? change with { RequireAbsent=true } : change).ToArray() };
                var result = await s.Executor.ApplyWithMetadataAsync(checkedPlan,$"Choose outfit for {armor.Armor}",metadata,stage,ct:ct);
                if (!result.Success) throw result.Exception ?? new InvalidOperationException(result.Message);
                await ReloadMods(ct);
                await RefreshAnalysis(ct);
                await RefreshActivity(ct);
            }
            await LoadOutfits(ct);
            StatusText = selection.ModId is null ? "Armor uses the normal load order." : "Outfit applied.";
        });
    }

    private async Task LoadOutfits(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var selected=SelectedOutfit;
        var status=await Coverage.RefreshAsync(HasSemanticCoverage,ct);
        if (selected is not null)
            SelectedOutfit=OutfitRows.FirstOrDefault(r => r.ModelId==selected.ModelId && r.Armor==selected.Armor);
        if(!string.IsNullOrWhiteSpace(status))StatusText=status;
    }
}
