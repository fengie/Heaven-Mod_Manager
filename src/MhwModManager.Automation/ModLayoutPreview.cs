using MhwModManager.Core;

namespace MhwModManager.Automation;

public enum ModLayoutGroupKind
{
    CompanionPattern,
    PartialCompanionPattern,
    PackageReview,
    ArchiveReview,
    Unclassified
}

public sealed record ModLayoutGroup(
    string RelativePath, ModLayoutGroupKind Kind, IReadOnlyList<string> Files, string Reason);

public sealed record ModLayoutInspection(
    string ProfileId, int FileCount, IReadOnlyList<ModLayoutGroup> Groups);

/// <summary>
/// Read-only, game-contextual inspection of an already bounded file inventory.
/// A companion pattern is a container-level grouping, not proof of an independent
/// mod, compatible selection, correct game identity, or permission to import it.
/// Every observed file belongs to exactly one result, including unknown files.
/// </summary>
public static class ModLayoutPreview
{
    private sealed record LayoutProfile(string Id, string[] GameKeys, string[] Companions);
    private sealed record Bucket(string RelativePath, ModLayoutGroupKind Kind, string Reason, List<string> Files);

    // These are versioned structural conventions, not universal validity rules.
    // Additional games reuse a profile or add a small data entry, not another scanner.
    private static readonly LayoutProfile[] Profiles =
    [
        new("unreal-pak-signature/v1", ["sifu"], [".pak", ".sig"]),
        new("unreal-iostore-companions/v1", ["stellar-blade", "stellarblade"], [".pak", ".utoc", ".ucas"])
    ];

    private static readonly HashSet<string> MetadataNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "mod-intake.json", "mod.json", "manifest.json", "meta.json",
        "manifest.toml", "thunderstore.toml"
    };

    private static readonly HashSet<string> SharedLoaderRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "UE4SS", "BepInEx", "reframework", "MelonLoader", "nativePC", "LogicMods"
    };

    public static ModLayoutInspection Inspect(
        ModBoundaryResult discovery, GameProfile game, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(game);
        var files = discovery.FileInventory ?? throw new InvalidOperationException(
            "The boundary result has no observed file inventory; inspection cannot claim completeness.");
        var profile = ResolveProfile(game);
        var scopes = ReviewScopes(files, ct);
        var buckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var scope = FindReviewScope(file, scopes);
            if (scope is not null)
            {
                Add(buckets, scope, ModLayoutGroupKind.PackageReview, scopes[scope], file);
                continue;
            }

            var directory = Parent(file);
            var leaf = file[(file.LastIndexOf('/') + 1)..];
            var dot = leaf.LastIndexOf('.');
            var extension = dot > 0 ? leaf[dot..] : string.Empty;
            if (profile is not null && profile.Companions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                var stem = leaf[..dot];
                var key = directory.Length == 0 ? stem : directory + "/" + stem;
                Add(buckets, key, ModLayoutGroupKind.CompanionPattern, string.Empty, file);
            }
            else if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                     || extension.Equals(".rar", StringComparison.OrdinalIgnoreCase)
                     || extension.Equals(".7z", StringComparison.OrdinalIgnoreCase))
            {
                Add(buckets, file, ModLayoutGroupKind.ArchiveReview,
                    "Archive boundary only. Its contents were not expanded by this read-only folder preview; use the existing bounded archive importer.", file);
            }
            else
            {
                Add(buckets, directory, ModLayoutGroupKind.Unclassified,
                    "Observed files not assigned to a supported companion pattern. They remain visible and unchanged; they are not discarded or declared non-mod files.", file);
            }
        }

        var groups = new List<ModLayoutGroup>(buckets.Count);
        foreach (var bucket in buckets.Values)
        {
            ct.ThrowIfCancellationRequested();
            var kind = bucket.Kind;
            var reason = bucket.Reason;
            if (kind == ModLayoutGroupKind.CompanionPattern && profile is not null)
            {
                var extensions = bucket.Files.Select(Path.GetExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missing = profile.Companions.Where(extension => !extensions.Contains(extension)).ToArray();
                kind = missing.Length == 0 ? ModLayoutGroupKind.CompanionPattern : ModLayoutGroupKind.PartialCompanionPattern;
                reason = missing.Length == 0
                    ? $"Complete filename companion pattern ({profile.Id}). This is not proof of an independently installable mod; variants, shared files and dependencies still require review."
                    : $"Partial filename companion pattern ({profile.Id}); this profile did not observe {string.Join(", ", missing)}. This is not proof that the package is invalid: other supported layouts may differ. Review without guessing or dropping files.";
            }
            groups.Add(new ModLayoutGroup(
                bucket.RelativePath.Length == 0 ? "." : bucket.RelativePath,
                kind, Array.AsReadOnly(bucket.Files.Order(StringComparer.OrdinalIgnoreCase).ToArray()), reason));
        }

        if (groups.Sum(group => group.Files.Count) != files.Count)
            throw new InvalidOperationException("The layout preview did not account for every observed file.");
        return new ModLayoutInspection(profile?.Id ?? "generic/v1", files.Count,
            Array.AsReadOnly(groups.OrderBy(group => group.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Kind).ToArray()));
    }

    private static LayoutProfile? ResolveProfile(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // The registered game supplies context. Never infer it from a random PAK
        // extension or from the source folder name, and never change routing here.
        var keys = new[] { game.Id, game.NexusGameDomain ?? string.Empty };
        return Profiles.FirstOrDefault(profile => keys.Any(key =>
            profile.GameKeys.Contains(key, StringComparer.OrdinalIgnoreCase)));
    }

    private static Dictionary<string, string> ReviewScopes(IReadOnlyList<string> files, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var scopes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        const string fomod = "fomod/ModuleConfig.xml";
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var leaf = file[(file.LastIndexOf('/') + 1)..];
            if (MetadataNames.Contains(leaf))
                scopes.TryAdd(Parent(file),
                    "Package metadata requires semantic review. Its presence is not permission to split dependent components; the complete subtree is retained together.");
            if (file.Equals(fomod, StringComparison.OrdinalIgnoreCase)
                || file.EndsWith("/" + fomod, StringComparison.OrdinalIgnoreCase))
                scopes[file[..^fomod.Length].TrimEnd('/')] =
                    "FOMOD options and conditional files must be reviewed together; no installer choice was executed or inferred.";

            var parts = file.Split('/');
            for (var index = 0; index < parts.Length - 1; index++)
            {
                if (!SharedLoaderRoots.Contains(parts[index])) continue;
                scopes.TryAdd(string.Join("/", parts.Take(index + 1)),
                    "Shared loader or deployment subtree. Keep its files together for review rather than treating each script, DLL or resource as an independent mod.");
                break;
            }
        }
        return scopes;
    }

    private static string? FindReviewScope(string file, IReadOnlyDictionary<string, string> scopes)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (scopes.ContainsKey(string.Empty)) return string.Empty;
        // Outermost applicable scope wins. Prefix lookups avoid comparing every
        // file with every package, which would be quadratic on large collections.
        for (var slash = file.IndexOf('/'); slash >= 0; slash = file.IndexOf('/', slash + 1))
        {
            var prefix = file[..slash];
            if (scopes.ContainsKey(prefix)) return prefix;
        }
        return null;
    }

    private static string Parent(string file)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var slash = file.LastIndexOf('/');
        return slash < 0 ? string.Empty : file[..slash];
    }

    private static void Add(Dictionary<string, Bucket> buckets, string path,
        ModLayoutGroupKind kind, string reason, string file)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var key = ((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + path;
        if (!buckets.TryGetValue(key, out var bucket))
            buckets.Add(key, bucket = new Bucket(path, kind, reason, []));
        bucket.Files.Add(file);
    }
}
