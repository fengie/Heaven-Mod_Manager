using MhwModManager.Core;

namespace MhwModManager.Automation;

/// <summary>
/// A filesystem-free, conservative detector of independently installable package boundaries.
/// Names and extensions alone never prove that two loose payload files are separate mods.
/// </summary>
public enum ModBoundaryKind { Folder, Archive, Review }

public sealed record ModBoundaryCandidate(string RelativePath, ModBoundaryKind Kind, string Evidence);

public sealed record ModBoundaryResult(IReadOnlyList<ModBoundaryCandidate> Candidates)
{
    public bool NeedsReview
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Candidates.Any(candidate => candidate.Kind == ModBoundaryKind.Review);
        }
    }
}

/// <summary>
/// Finds package candidates from relative file paths (local folder snapshots or archive manifests).
/// Retains installation-root directories and installer options as one unit, but decomposes verified
/// sibling package roots and nested archives. This class does not read, extract, or mutate files.
/// </summary>
public static class ModBoundaryDiscovery
{
    private const int MaxLevels = 32;
    private const int MaxPaths = 50_000;
    private static readonly HashSet<string> LayoutRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "nativePC", "GameRoot", "BepInEx", "reframework", "mods", "plugins",
        "MelonLoader", "UE4SS", "Paks", "Data"
    };
    private static readonly HashSet<string> PackageManifests = new(StringComparer.OrdinalIgnoreCase)
    {
        "mod-intake.json", "mod.json", "manifest.json", "meta.json",
        "manifest.toml", "thunderstore.toml"
    };

    public static ModBoundaryResult Discover(IEnumerable<string> relativeFilePaths)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(relativeFilePaths);
        var paths = new List<string>();
        foreach (var original in relativeFilePaths)
        {
            if (string.IsNullOrWhiteSpace(original)) continue;
            var path = original.Replace('\\', '/').TrimEnd('/');
            if (path.StartsWith('/') || path.Split('/').Any(part =>
                    part is "" or "." or ".." || part.Contains(':')))
                throw new InvalidDataException("Invalid package-relative path in mod boundary discovery.");
            paths.Add(path);
            if (paths.Count > MaxPaths)
                throw new InvalidDataException("The mod discovery file-count budget was exceeded.");
        }
        var files = paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return new(Analyze(files, "", 0));
    }

    private static IReadOnlyList<ModBoundaryCandidate> Analyze(string[] paths, string prefix, int depth)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (depth > MaxLevels)
            return Review(prefix, "Package nesting exceeds the safe discovery depth.");

        var scoped = paths.Where(path => prefix.Length == 0 ||
            path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (scoped.Length == 0) return [];

        var relative = scoped.Select(path => prefix.Length == 0 ? path : path[(prefix.Length + 1)..]).ToArray();
        var directFiles = relative.Where(path => !path.Contains('/')).ToArray();
        var directories = relative.Where(path => path.Contains('/'))
            .Select(path => path[..path.IndexOf('/')])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();

        if (relative.Any(path => path.StartsWith("fomod/", StringComparison.OrdinalIgnoreCase)
            && IsFomodConfiguration(path)))
            return Review(prefix, "Installer options must be selected; never split FOMOD variants.");

        var archived = directFiles.Where(IsArchive).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var otherFiles = directFiles.Where(file => !IsArchive(file) && !IsSupplementary(file)).ToArray();

        // Loader roots are part of one mod's deployment layout, not independently installable mods.
        if (directories.Any(directory => LayoutRoots.Contains(directory)))
        {
            if (archived.Length > 0 ||
                directories.Where(directory => !LayoutRoots.Contains(directory))
                    .Any(directory => HasExplicitChildPackageEvidence(relative, directory)))
                return Review(prefix, "Independent package evidence conflicts with a shared loader root.");
            return [new(Display(prefix), ModBoundaryKind.Folder, "One game installation root.")];
        }

        if (directFiles.Any(file => PackageManifests.Contains(file)))
        {
            if (archived.Length > 0)
                return Review(prefix, "Package manifest with nested archives needs review.");
            return [new(Display(prefix), ModBoundaryKind.Folder, "One explicit package manifest.")];
        }

        // Anonymous loose DLL/PAK/DDS files cannot be confidently separated or auto-installed.
        if (otherFiles.Length > 0)
        {
            // A single root payload is one package even without a manifest. Its target
            // game still requires independent classifier evidence before any routing.
            if (otherFiles.Length == 1 && directories.Length == 0 && archived.Length == 0)
                return [new(Display(prefix), ModBoundaryKind.Folder, "One root-level payload.")];

            // Standalone downloaded archives remain independently identifiable even when
            // the monitored folder also contains an unrelated loose download. The loose
            // payload remains in review, never silently omitted from the report.
            if (archived.Length > 0 && directories.Length == 0)
            {
                var separate = archived.Select(archive => new ModBoundaryCandidate(
                    Join(prefix, archive), ModBoundaryKind.Archive, "Independent nested archive.")).ToList();
                separate.AddRange(Review(prefix, "Unattributed loose files remain outside automatic import."));
                return separate;
            }
            return Review(prefix, "Loose files have no proven independent mod boundaries.");
        }

        var meaningfulDirectories = directories.Where(directory => !IsSupplementaryDirectory(directory)
            || relative.Any(path => path.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase)
                && !IsSupplementary(path[(directory.Length + 1)..]))).ToArray();

        // Multiple variants are normally mutually exclusive versions of one mod, not multiple mods.
        if (meaningfulDirectories.Length > 1 && meaningfulDirectories.Any(LooksLikeVariant))
            return Review(prefix, "Potential option/quality variants need review as one package.");

        var candidates = new List<ModBoundaryCandidate>();
        foreach (var archive in archived)
            candidates.Add(new(Join(prefix, archive), ModBoundaryKind.Archive, "Independent nested archive."));

        foreach (var directory in meaningfulDirectories)
        {
            var child = Join(prefix, directory);
            var found = Analyze(paths, child, depth + 1);
            // Preserve independent siblings when one child needs review. The review
            // candidate is reported separately and never auto-installed.
            candidates.AddRange(found);
        }

        // A collection is only separated when each candidate is structurally independent.
        return candidates;
    }

    private static IReadOnlyList<ModBoundaryCandidate> Review(string prefix, string reason)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return [new(Display(prefix), ModBoundaryKind.Review, reason)];
    }

    private static string Display(string prefix)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return prefix.Length == 0 ? "." : prefix;
    }

    private static string Join(string prefix, string child)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return prefix.Length == 0 ? child : prefix + "/" + child;
    }

    private static bool IsArchive(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var extension = Path.GetExtension(path);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".rar", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".7z", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFomodConfiguration(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = path.Replace('\\', '/');
        return normalized.EndsWith("fomod/ModuleConfig.xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupplementary(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var file = Path.GetFileName(path.Replace('\\', '/'));
        var extension = Path.GetExtension(file);
        return file.StartsWith("readme", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("license", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("changelog", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("credits", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("screenshot", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("preview", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("thumb", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("cover", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExplicitChildPackageEvidence(string[] relativePaths, string directory)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var prefix = directory + "/";
        foreach (var path in relativePaths.Where(path =>
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            var child = path[prefix.Length..];
            if (PackageManifests.Contains(child)) return true;
            var slash = child.IndexOf('/');
            if (slash > 0 && LayoutRoots.Contains(child[..slash])) return true;
        }
        return false;
    }

    private static bool IsSupplementaryDirectory(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return name.Equals("docs", StringComparison.OrdinalIgnoreCase)
            || name.Equals("screenshots", StringComparison.OrdinalIgnoreCase)
            || name.Equals("documentation", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeVariant(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = name.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token => token is "optional" or "option" or "variant" or "alternative"
            or "low" or "high" or "resolution" or "hd" or "sd" or "lite"
            or "performance" or "quality" or "default");
    }
}
