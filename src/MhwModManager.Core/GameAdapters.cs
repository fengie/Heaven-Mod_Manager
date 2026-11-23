namespace MhwModManager.Core;

public sealed record AdapterValidation(string Code, string Message, bool Blocking);
public sealed record SemanticAsset(string Namespace, string Component);
public sealed record AdapterDependency(string Name, string RelativePath, string Reason);

// Evaluates permission to make direct game-file changes. Format recognition,
// installed-game discovery and import staging never grant this permission.
public sealed record GameDirectFileAdmission(bool Allowed, string Code, string Reason);

/// <summary>Optional capability contract; third-party adapters must explicitly opt in.</summary>
public interface IGameDirectFileDeploymentPolicy
{
    GameDirectFileAdmission EvaluateDirectFileDeployment(GameProfile game);
}

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

public class FolderGameAdapter : IGameAdapter, IGameDirectFileDeploymentPolicy
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

    /// <summary>
    /// Existing identified folder-loader games retain ordinary transactional deployment.
    /// A newly inferred game root without a loader is staging-only until configured.
    /// </summary>
    public virtual GameDirectFileAdmission EvaluateDirectFileDeployment(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        if (game.AdapterId.Equals("inferred-unverified-root", StringComparison.OrdinalIgnoreCase))
            return new(false, "unverified-game-root-target",
                "This game was discovered without a verified mod root; import staging is allowed, but direct game-root writes require explicit configuration.");
        // Preserve existing user-configured/legacy generic-root profiles and custom
        // adapters with a recorded nonempty target path. No inferred unknown
        // executable can grant write authority to the whole game directory.
        if (string.IsNullOrWhiteSpace(game.ModRootRelativePath)
            && !game.AdapterId.Equals("generic-root", StringComparison.OrdinalIgnoreCase)
            && !game.AdapterId.Equals("generic-folder", StringComparison.OrdinalIgnoreCase)
            && !game.AdapterId.Equals("mhw", StringComparison.OrdinalIgnoreCase))
            return new(false, "adapter-deployment-unverified",
                "An unknown game adapter with no configured mod root cannot authorize direct file deployment.");
        return new(true, "direct-folder-deployment",
            "A configured folder-based deployment uses the existing journal, conflict review and Undo.");
    }

    public virtual IReadOnlyList<AdapterValidation> Validate(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var issues = new List<AdapterValidation>();
        if (!PathRules.IsSafeArchiveRelativePath(game.ExecutableRelativePath))
            issues.Add(new("unsafe-executable", "Executable must be a safe path relative to the game root.", true));

        if (!string.IsNullOrEmpty(game.ModRootRelativePath) && !PathRules.IsSafeArchiveRelativePath(game.ModRootRelativePath))
            issues.Add(new("unsafe-mod-root", "Mod root must be relative to the game root.", true));

        if (game.AdapterId.Equals("inferred-unverified-root", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("unverified-game-root-target",
                "Newly discovered game has no confirmed mod root; select a supported loader or explicit directory before deployment.", true));
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

/// <summary>
/// Recognizes known Crimson Desert on-disk layout without claiming binary
/// compatibility. Live PAZ/PAMT/PAPGT and ASI deployment is not supported.
/// </summary>
public sealed class CrimsonDesertGameAdapter : FolderGameAdapter
{
    public override string Id { get; } = "crimson-desert";
    public override string DisplayName { get; } = "Crimson Desert";

    public override GameDirectFileAdmission EvaluateDirectFileDeployment(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        return new(false, "crimson-deployment-unverified",
            "Crimson Desert packages may be staged for review, but no build-verified overlay backend exists. Live game file deployment is blocked.");
    }

    /// <summary>
    /// Legacy generic profiles pointing to a verified Crimson Desert layout
    /// receive the same conservative game protection as newly registered ones.
    /// A guessed title or executable name by itself is insufficient evidence.
    /// </summary>
    public static bool Matches(GameProfile? game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (game is null) return false;
        if (game.AdapterId.Equals("crimson-desert", StringComparison.OrdinalIgnoreCase)) return true;
        // A missing/broken meta registry during an interrupted game update
        // must not turn a known Crimson executable back into a writable generic
        // profile. HasGameLayout is for readiness, not for withdrawing protection.
        return game.ExecutableRelativePath.Equals(@"bin64\CrimsonDesert.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasGameLayout(string root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(root)) return false;
        return File.Exists(Path.Combine(root, "bin64", "CrimsonDesert.exe"))
            && File.Exists(Path.Combine(root, "meta", "0.papgt"))
            && File.Exists(Path.Combine(root, "meta", "0.paver"));
    }

    public override IReadOnlyList<GameProfile> Discover(IEnumerable<string> candidateRoots)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(candidateRoots);
        return candidateRoots.Where(HasGameLayout)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(root => GameProfile.CrimsonDesert(root))
            .ToArray();
    }

    public override string Category(string managedPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = managedPath.Replace('\\', '/');
        if (normalized.EndsWith(".field.json", StringComparison.OrdinalIgnoreCase))
            return "Crimson Desert field-patch manifest";
        if (normalized.EndsWith(".asi", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return "Native executable plugin - review required";
        if (normalized.EndsWith(".paz", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".pamt", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".papgt", StringComparison.OrdinalIgnoreCase))
            return "Pearl Abyss archive - protected";
        return base.Category(managedPath);
    }

    public override IReadOnlyList<AdapterValidation> Validate(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var issues = base.Validate(game).ToList();
        if (!HasGameLayout(game.GameRoot))
            issues.Add(new("crimson-layout-unverified",
                "The Crimson Desert executable and game archive registry were not found together.", true));
        if (!string.IsNullOrWhiteSpace(game.ModRootRelativePath))
            issues.Add(new("crimson-loader-unverified",
                "An arbitrary mod root does not establish a compatible Crimson Desert loader.", true));
        issues.Add(new("crimson-deployment-unverified",
            "Crimson Desert imports are staging-only. No build-verified overlay or ASI backend is installed.", true));
        return issues;
    }
}

public static class GameAdapters
{
    private static readonly Dictionary<string, IGameAdapter> Registered = new(StringComparer.OrdinalIgnoreCase)
    {
        ["generic-folder"] = new FolderGameAdapter(),
        ["mhw"] = new MonsterHunterWorldAdapter(),
        ["crimson-desert"] = new CrimsonDesertGameAdapter()
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
            return CrimsonDesertGameAdapter.Matches(game) ? Registered["crimson-desert"]
                : Registered.GetValueOrDefault(game?.AdapterId ?? "mhw", Registered["generic-folder"]);
    }

    /// <summary>
    /// Common admission point used by catalog capture, scanner and planner.
    /// Null preserves legacy MHW-only call sites; custom adapters without an
    /// explicit direct-file policy default to staging-only.
    /// </summary>
    public static GameDirectFileAdmission DirectFileAdmission(GameProfile? game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (game is null)
            return new(true, "legacy-mhw-context", "Legacy MHW context uses existing transactional deployment.");
        var adapter = Resolve(game);
        return adapter is IGameDirectFileDeploymentPolicy policy
            ? policy.EvaluateDirectFileDeployment(game)
            : new(false, "adapter-deployment-unverified",
                "This game adapter has not declared a verified direct-file deployment capability.");
    }
}
