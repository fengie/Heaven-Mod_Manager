using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string AllCategories = "All categories";
    private static readonly HashSet<string> ViewModes = new(PathRules.Comparer)
    {
        "All", "Enabled", "Disabled", "Favorites", "Staged", "Updates", "FamilyLinks",
        "Issues", "Revalidate", "Superseded"
    };

    private readonly HashSet<string> favoriteLibraryKeys = new(PathRules.Comparer);
    private readonly Dictionary<string, string> customLibraryCategories = new(PathRules.Comparer);
    private bool suppressLibraryFilterRefresh;

    [ObservableProperty] private string categoryFilter = AllCategories;
    [ObservableProperty] private string newSavedViewName = "";
    [ObservableProperty] private SavedLibraryView? selectedSavedLibraryView;
    [ObservableProperty] private string customCategoryText = "";

    public ObservableRangeCollection<SavedLibraryView> SavedLibraryViews { get; } = [];
    public ObservableRangeCollection<string> LibraryCategories { get; } = [AllCategories];

    public string FavoriteActionLabel
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return SelectedMod?.IsFavorite == true ? "Remove favorite" : "Add favorite";
        }
    }

    private void LoadLibraryPreferences()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var settings = s.Settings.Current;
        favoriteLibraryKeys.UnionWith(settings.FavoriteLibraryKeys ?? []);
        foreach (var (key, value) in settings.CustomLibraryCategories ?? new Dictionary<string,string>())
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                customLibraryCategories[key] = value;
        SavedLibraryViews.ReplaceAll((settings.SavedLibraryViews ?? [])
            .Where(view=>view is not null && !string.IsNullOrWhiteSpace(view.Name) &&
                ViewModes.Contains(view.Mode) && view.Query is not null && !string.IsNullOrWhiteSpace(view.Category))
            .DistinctBy(view=>view.Name, PathRules.Comparer)
            .OrderBy(view=>view.Name,PathRules.Comparer));
    }

    private void ApplyLibraryPreferences(ModRowViewModel row)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        row.IsFavorite = favoriteLibraryKeys.Contains(row.LibraryPreferenceKey);
        row.CustomCategory = customLibraryCategories.GetValueOrDefault(row.LibraryPreferenceKey);
    }

    private void PersistLibraryPreferences()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        s.Settings.Update(settings=>
        {
            settings.FavoriteLibraryKeys = favoriteLibraryKeys.Order(PathRules.Comparer).ToArray();
            settings.CustomLibraryCategories = new Dictionary<string,string>(customLibraryCategories,PathRules.Comparer);
            settings.SavedLibraryViews = SavedLibraryViews.ToArray();
        });
    }

    private void RefreshLibraryCategories()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var categories = Mods.Select(row=>row.DisplayCategory).Append(CategoryFilter)
            .Where(category=>!string.IsNullOrWhiteSpace(category) && !PathRules.Comparer.Equals(category,AllCategories))
            .Distinct(PathRules.Comparer).Order(PathRules.Comparer).Prepend(AllCategories).ToArray();
        var selected = CategoryFilter;
        // Replacing ComboBox items can momentarily clear its selected value.
        suppressLibraryFilterRefresh = true;
        try { LibraryCategories.ReplaceAll(categories); CategoryFilter=selected; }
        finally { suppressLibraryFilterRefresh=false; }
    }

    partial void OnCategoryFilterChanged(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (suppressLibraryFilterRefresh) return;
        if (string.IsNullOrWhiteSpace(value)) { CategoryFilter=AllCategories; return; }
        RefreshLibraryFilters();
        NotifyVisibleModState();
    }

    [RelayCommand]
    private void SaveLibraryView()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var name = NewSavedViewName.Trim();
        if (name.Length == 0 || name.Length > 64 || SearchText.Length > 512 || !ViewModes.Contains(ModViewMode))
        {
            StatusText = "Enter a view name of 1–64 characters and use a valid filter (search up to 512 characters).";
            return;
        }
        var view = new SavedLibraryView(name, ModViewMode, SearchText, CategoryFilter);
        SavedLibraryViews.ReplaceAll(SavedLibraryViews.Where(item=>!PathRules.Comparer.Equals(item.Name,name))
            .Append(view).OrderBy(item=>item.Name,PathRules.Comparer).ToArray());
        SelectedSavedLibraryView=view;
        PersistLibraryPreferences();
        StatusText=$"Saved library view '{name}'. No activation state was saved or changed.";
    }

    [RelayCommand]
    private void UseSavedLibraryView()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (SelectedSavedLibraryView is not { } view || !SavedLibraryViews.Contains(view) || !ViewModes.Contains(view.Mode))
        {
            StatusText="Select a saved library view first.";
            return;
        }
        if (!LibraryCategories.Contains(view.Category,PathRules.Comparer)) LibraryCategories.Add(view.Category);
        suppressLibraryFilterRefresh = true;
        try { ModViewMode=view.Mode; SearchText=view.Query; CategoryFilter=view.Category; }
        finally { suppressLibraryFilterRefresh=false; }
        searchCts?.Cancel();
        RefreshLibraryFilters();
        NotifyVisibleModState();
        SelectedTab=1;
        StatusText=$"Restored view '{view.Name}'. Selection and activation were not changed.";
    }

    [RelayCommand]
    private void DeleteSavedLibraryView()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (SelectedSavedLibraryView is not { } view || !SavedLibraryViews.Remove(view)) return;
        SelectedSavedLibraryView=null;
        PersistLibraryPreferences();
        StatusText=$"Removed saved view '{view.Name}'. No mods changed.";
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (SelectedMod is not { } row || !Mods.Contains(row)) { StatusText="Highlight a managed mod first."; return; }
        if (!favoriteLibraryKeys.Add(row.LibraryPreferenceKey)) favoriteLibraryKeys.Remove(row.LibraryPreferenceKey);
        ApplyLibraryPreferences(row);
        PersistLibraryPreferences();
        RefreshLibraryFilters();
        NotifyVisibleModState();
        OnPropertyChanged(nameof(FavoriteActionLabel));
        StatusText=$"{row.DisplayName}: {(row.IsFavorite ? "added to" : "removed from")} favorites.";
    }

    [RelayCommand]
    private void AssignCustomCategory()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (SelectedMod is not { } row || !Mods.Contains(row)) { StatusText="Highlight a managed mod first."; return; }
        var category = CustomCategoryText.Trim();
        if (category.Length>64 || PathRules.Comparer.Equals(category,AllCategories))
        {
            StatusText="Choose a category of at most 64 characters, or leave it blank to restore the detected category.";
            return;
        }
        if (category.Length==0) customLibraryCategories.Remove(row.LibraryPreferenceKey);
        else customLibraryCategories[row.LibraryPreferenceKey]=category;
        ApplyLibraryPreferences(row);
        PersistLibraryPreferences();
        RefreshLibraryCategories();
        RefreshLibraryFilters();
        NotifyVisibleModState();
        StatusText=$"{row.DisplayName}: category '{row.DisplayCategory}'. No game files changed.";
    }

    [RelayCommand]
    private void MarkHighlighted(IList? selection)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        SetHighlightedMarks(selection,true);
    }

    [RelayCommand]
    private void UnmarkHighlighted(IList? selection)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        SetHighlightedMarks(selection,false);
    }

    private void SetHighlightedMarks(IList? selection, bool marked)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (selection is null) return;
        var visible = ModsView.Cast<ModRowViewModel>().ToHashSet();
        var targets = selection.OfType<ModRowViewModel>().Where(row=>visible.Contains(row) && row.CanStage).Distinct().ToArray();
        suppressBulkSelectionNotifications = true;
        try { foreach (var row in targets) row.BulkSelected=marked; }
        finally { suppressBulkSelectionNotifications=false; NotifyBulkSelection(); }
        StatusText=$"{(marked ? "Marked" : "Unmarked")} {targets.Length} highlighted managed mod(s). No game files changed.";
    }
}
