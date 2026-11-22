namespace MhwModManager.Core;

/// <summary>A physical package boundary inside a managed shelf. Review units retain all source files.</summary>
public sealed record ManagedCollectionUnit(string SourcePath, bool NeedsReview);

public sealed record ManagedCollectionLayout(IReadOnlyList<ManagedCollectionUnit> Units, bool FullyAccounted);


public static class ManagedCollectionNotes
{
    public static bool IsShellMetadata(string path)
    {
        using var trace = MasterDebugLog.BeginMethod();
        var name = Path.GetFileName(path);
        return name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNote(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var name = Path.GetFileName(path);
        return IsShellMetadata(name) ||
            (Path.GetExtension(name).ToLowerInvariant() is ".txt" or ".md" &&
                (name.StartsWith("readme", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("Installation Instructions.txt", StringComparison.OrdinalIgnoreCase)));
    }
}
