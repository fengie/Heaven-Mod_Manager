namespace MhwModManager.Core;

public sealed record AdapterValidation(string Code, string Message, bool Blocking);
public sealed record SemanticAsset(string Namespace, string Component);
public sealed record AdapterDependency(string Name, string RelativePath, string Reason);

/// <summary>Adapters describe game knowledge. They never write live files or bypass the deployment journal.</summary>
public interface IGameAdapter
{
    string Id { get; }
    string DisplayName { get; }
    bool SupportsMhwConflictSemantics { get; }
    IReadOnlyList<GameProfile> Discover(IEnumerable<string> candidateRoots);
    string Executable(GameProfile game);
    IReadOnlyList<string> ModRoots(GameProfile game);
    IReadOnlyList<string> SavePaths(GameProfile game);
    string? NexusDomain(GameProfile game);
    SemanticAsset? ParseAsset(string managedPath);
    string Category(string managedPath);
    IReadOnlyList<AdapterDependency> Dependencies(GameProfile game);
    IReadOnlyList<AdapterValidation> Validate(GameProfile game);
}

public class FolderGameAdapter : IGameAdapter
{
    public virtual string Id { get; } = "generic-folder";
    public virtual string DisplayName { get; } = "Generic folder";
    public virtual bool SupportsMhwConflictSemantics { get; }

    public virtual IReadOnlyList<GameProfile> Discover(IEnumerable<string> candidateRoots)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(candidateRoots);
        return []; // Generic games need explicit executable/root configuration.
    }

    public string Executable(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return game.ExecutablePath;
    }

    public IReadOnlyList<string> ModRoots(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return [game.LiveModRoot];
    }

    public IReadOnlyList<string> SavePaths(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return string.IsNullOrWhiteSpace(game.SavePath) ? [] : [game.SavePath];
    }

    public string? NexusDomain(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return game.NexusGameDomain;
    }

    public virtual SemanticAsset? ParseAsset(string managedPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        _ = PathRules.Normalize(managedPath);
        return null;
    }

    public virtual string Category(string managedPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return PathRules.ClassifyFile(PathRules.Normalize(managedPath)).ToString();
    }

    public virtual IReadOnlyList<AdapterDependency> Dependencies(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        return [];
    }

    public virtual IReadOnlyList<AdapterValidation> Validate(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var issues = new List<AdapterValidation>();
        if (!PathRules.IsSafeArchiveRelativePath(game.ExecutableRelativePath))
            issues.Add(new("unsafe-executable", "Executable must be a safe path relative to the game root.", true));

        if (!string.IsNullOrEmpty(game.ModRootRelativePath) && !PathRules.IsSafeArchiveRelativePath(game.ModRootRelativePath))
            issues.Add(new("unsafe-mod-root", "Mod root must be relative to the game root.", true));

        if (issues.Count == 0 && !File.Exists(game.ExecutablePath))
            issues.Add(new("missing-executable", "The configured game executable was not found.", true));
        return issues;
    }
}

public sealed class MonsterHunterWorldAdapter : FolderGameAdapter
{
    public override string Id { get; } = "mhw";
    public override string DisplayName { get; } = "Monster Hunter: World";
    public override bool SupportsMhwConflictSemantics { get; } = true;

    public override IReadOnlyList<GameProfile> Discover(IEnumerable<string> candidateRoots)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return candidateRoots
            .Where(root => File.Exists(Path.Combine(root, "MonsterHunterWorld.exe")))
            .Select(GameProfile.MonsterHunterWorld)
            .ToArray();
    }
    public override SemanticAsset? ParseAsset(string managedPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return PathRules.TryGetArmorComponent(managedPath, out var model, out var component)
            ? new(model, component)
            : null;
    }
}

public static class GameAdapters
{
    private static readonly Dictionary<string, IGameAdapter> Registered = new(StringComparer.OrdinalIgnoreCase)
    {
        ["generic-folder"] = new FolderGameAdapter(),
        ["mhw"] = new MonsterHunterWorldAdapter()
    };

    private static readonly object Gate = new();

    public static void Register(IGameAdapter adapter)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter.Id);
        lock (Gate)
        {
            if (!Registered.TryAdd(adapter.Id, adapter))
                throw new InvalidOperationException("An adapter with this ID is already registered.");
        }
    }

    public static IGameAdapter Resolve(GameProfile? game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        lock (Gate)
            return Registered.GetValueOrDefault(game?.AdapterId ?? "mhw", Registered["generic-folder"]);
    }
}
