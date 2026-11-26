using MhwModManager.Core;

namespace MhwModManager.App;

/// <summary>Presentation-only settings. The canonical tab indices and game/mod state do not move.</summary>
public sealed record WorkspaceLayout(IReadOnlyList<string> NavigationOrder, double SidebarWidth, double? ModToolsHeight)
{
    public const double DefaultSidebarWidth = 216;
    public const double MinimumSidebarWidth = 160;
    public const double MaximumSidebarWidth = 340;
    public const double MinimumToolsHeight = 80;
    public const double MaximumToolsHeight = 340;

    private static readonly System.Collections.ObjectModel.ReadOnlyCollection<string> DefaultNavigation = Array.AsReadOnly(new[]
    {
        "DashboardTab", "ModsTab", "CoverageTab", "AttentionTab", "ProfilesTab",
        "HistoryTab", "SharedFilesTab", "SettingsTab", "BrowseModsTab", "InstalledFilesTab"
    });

    public static WorkspaceLayout Default => new(DefaultNavigation, DefaultSidebarWidth, null);

    public static WorkspaceLayout Normalize(WorkspaceLayout? saved)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var width = saved?.SidebarWidth ?? DefaultSidebarWidth;
        if (!double.IsFinite(width)) width = DefaultSidebarWidth;
        var tools = saved?.ModToolsHeight;
        if (tools is double value)
            tools = double.IsFinite(value) ? Math.Clamp(value, MinimumToolsHeight, MaximumToolsHeight) : null;
        return new(NormalizeOrder(saved?.NavigationOrder),
            Math.Clamp(width, MinimumSidebarWidth, MaximumSidebarWidth), tools);
    }

    public static IReadOnlyList<string> NormalizeOrder(IEnumerable<string>? saved)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var known = new HashSet<string>(DefaultNavigation, StringComparer.Ordinal);
        var result = new List<string>(DefaultNavigation.Count);
        foreach (var id in saved ?? [])
            if (id is not null && known.Remove(id)) result.Add(id);
        foreach (var id in DefaultNavigation)
            if (known.Remove(id)) result.Add(id);
        return result.AsReadOnly();
    }

    public static IReadOnlyList<string> Reorder(IEnumerable<string>? saved, string source, string target, bool after)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result = NormalizeOrder(saved).ToList();
        if (StringComparer.Ordinal.Equals(source, target) ||
            !result.Contains(source, StringComparer.Ordinal) ||
            !result.Contains(target, StringComparer.Ordinal)) return result.AsReadOnly();
        result.Remove(source);
        var targetIndex = result.IndexOf(target);
        result.Insert(targetIndex + (after ? 1 : 0), source);
        return result.AsReadOnly();
    }
}
