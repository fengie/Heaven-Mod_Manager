using MhwModManager.Core;

namespace MhwModManager.Automation;

/// <summary>
/// A filesystem-free, conservative detector of independently installable package boundaries.
/// Names and extensions alone never prove that two loose payload files are separate mods.
/// </summary>
public enum ModBoundaryKind { Folder, Archive, Review, FileSet }

/// <param name="Files">Exact source-relative members for a virtual package; null for physical roots.</param>
/// <param name="SourcePrefix">Collection wrapper to remove from staged game-relative files.</param>
public sealed record ModBoundaryCandidate(
    string RelativePath, ModBoundaryKind Kind, string Evidence, IReadOnlyList<string>? Files = null,
    string? SourcePrefix = null);

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
    private const int MaxLevels = 64;
    private const int MaxPaths = 50_000;
    private const string StellarBladePakRoot = "SB/Content/Paks/~mods/";
    private static readonly HashSet<string> VariantLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "optional", "option", "variant", "alternative", "low", "high", "resolution",
        "hd", "sd", "lite", "performance", "quality", "default"
    };
    private static readonly HashSet<string> LayoutRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "nativePC", "GameRoot", "BepInEx", "reframework", "mods", "plugins",
        "MelonLoader", "UE4SS", "Paks", "Data"
    };
    private static readonly HashSet<string> StrongLoaderRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "nativePC", "BepInEx", "reframework", "MelonLoader", "UE4SS", "GameRoot"
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
        if (files.Length != paths.Count)
            throw new InvalidDataException("Duplicate or case-colliding mod file paths require review.");
        return new(Analyze(files, "", 0));
    }

    private static IReadOnlyList<ModBoundaryCandidate> Analyze(string[] relative, string prefix, int depth)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (depth > MaxLevels)
            return Review(prefix, "Package nesting exceeds the safe discovery depth.");

        if (relative.Length == 0) return [];

        // Partition each directory's already-scoped paths once. Repeatedly searching
        // the entire original inventory for each sibling would become quadratic on
        // thousands of mods and would stall the WPF import of large collections.
        var directFiles = new List<string>();
        var children = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in relative)
        {
            var slash = path.IndexOf('/');
            if (slash < 0)
            {
                directFiles.Add(path);
                continue;
            }
            var directory = path[..slash];
            if (!children.TryGetValue(directory, out var files))
                children.Add(directory, files = []);
            files.Add(path[(slash + 1)..]);
        }
        var directories = children.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

        if (relative.Any(path => path.StartsWith("fomod/", StringComparison.OrdinalIgnoreCase)
            && IsFomodConfiguration(path)))
            return Review(prefix, "Installer options must be selected; never split FOMOD variants.");

        var archived = directFiles.Where(IsArchive).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var otherFiles = directFiles.Where(file => !IsArchive(file) && !IsSupplementary(file)).ToArray();

        // A recognized shared Stellar Blade Paks folder can contain several complete
        // pak/utoc/ucas bundles. Carry the exact members as virtual package boundaries.
        // Never apply this split inside a named mod's own deployment layout.
        var pakGroups = DiscoverFlatStellarBladePaks(relative, prefix);
        if (pakGroups is not null) return pakGroups;

        // A named mod containing SB/Content/Paks is one installable unit. In particular,
        // don't descend into Content or Paks and strip the required SB deployment prefix.
        if (HasStellarBladeLayout(relative))
        {
            if (relative.Any(IsArchive) || relative.Any(IsFomodConfiguration) ||
                directories.Any(directory => !directory.Equals("SB", StringComparison.OrdinalIgnoreCase)
                    && !IsSupplementaryDirectory(directory)))
                return Review(prefix, "Stellar Blade deployment layout conflicts with another package or installer.");
            return [new(Display(prefix), ModBoundaryKind.Folder, "One Stellar Blade deployment layout.")];
        }

        // Known installation roots cannot safely be separated from another
        // explicitly packaged sibling. Keep that conflicting tree in review
        // rather than installing a partial nativePC/BepInEx/UE4SS payload.
        if (directories.Any(StrongLoaderRoots.Contains) &&
            (archived.Length > 0 || directories.Where(d => !StrongLoaderRoots.Contains(d))
                .Any(d => HasExplicitChildPackageEvidence(relative, d))))
            return Review(prefix, "Independent packages conflict with a shared loader root.");

        // A directory named Mods/Paks/etc can also be a wrapper around hundreds of
        // independently packaged mods. Inspect structural package evidence beneath
        // wrappers before collapsing the whole subtree into a single installation root.
        var independentRoots = HasAtLeastIndependentPackageRoots(relative, 2);
        var hasPackageRootEvidence = independentRoots || HasAtLeastIndependentPackageRoots(relative, 1);
        if (directories.Any(directory => LayoutRoots.Contains(directory)) && !hasPackageRootEvidence)
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
        // A stray file at a *collection* root must not conceal proven independent
        // package descendants. Keep the loose payload in review without publishing it.
        var reviewUnattributedFiles = otherFiles.Length > 0 && directories.Length > 0 && independentRoots;
        if (otherFiles.Length > 0 && !reviewUnattributedFiles)
        {
            if (otherFiles.Length == 1 && directories.Length == 0 && archived.Length == 0)
                return [new(Display(prefix), ModBoundaryKind.Folder, "One root-level payload.")];

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
            || children[directory].Any(path => !IsSupplementary(path))).ToArray();

        // Only siblings with matching package identity, after stripping known
        // variant tokens, are potential alternatives. One ordinary "High Heels"
        // mod must not suppress an unrelated hundred-mod collection.
        var ambiguousVariants = meaningfulDirectories
            .GroupBy(VariantFamily, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1 && group.Any(LooksLikeVariant))
            .SelectMany(group => group)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ambiguousVariants.Count == meaningfulDirectories.Length
            && meaningfulDirectories.Length > 1
            && meaningfulDirectories.Select(VariantFamily)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            return Review(prefix, "Potential mutually exclusive variants need review as one package.");

        var candidates = new List<ModBoundaryCandidate>();
        if (reviewUnattributedFiles)
            candidates.AddRange(Review(prefix, "Unattributed collection-level loose files need review."));
        foreach (var archive in archived)
            candidates.Add(new(Join(prefix, archive), ModBoundaryKind.Archive, "Independent nested archive."));

        foreach (var directory in meaningfulDirectories)
        {
            var child = Join(prefix, directory);
            if (ambiguousVariants.Contains(directory))
            {
                candidates.AddRange(Review(child, "Package variants share an identity with a sibling; choose the intended option."));
                continue;
            }
            var found = Analyze(children[directory].ToArray(), child, depth + 1);
            // Preserve independent siblings when one child needs review. The review
            // candidate is reported separately and never auto-installed.
            candidates.AddRange(found);
        }

        // A collection is only separated when each candidate is structurally independent.
        return candidates;
    }


    private static bool HasStellarBladeLayout(string[] relativePaths)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return relativePaths.Any(path =>
            path.StartsWith("SB/Content/Paks/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A loader-labelled collection wrapper is bypassed when one or more
    /// nested package roots have verified structural evidence. One nested
    /// package also must be unwrapped; otherwise its source collection
    /// wrapper becomes part of the installed deployment layout.
    /// </summary>
    private static bool HasAtLeastIndependentPackageRoots(string[] relativePaths, int minimumCount)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in relativePaths)
        {
            // Distinct archives nested below a loader-named collection are
            // independent inspectable packages. Do not classify their enclosing
            // Mods/Paks folder as one package before we reach these archives.
            if (path.Contains('/') && IsArchive(path))
            {
                roots.Add(path);
                if (roots.Count >= minimumCount) return true;
            }
            var segments = path.Split('/');
            if (segments.Length > 1 && PackageManifests.Contains(segments[^1]))
            {
                roots.Add(string.Join("/", segments.Take(segments.Length - 1)));
                if (roots.Count >= minimumCount) return true;
            }
            for (var i = 1; i < segments.Length - 1; i++)
            {
                var segment = segments[i];
                var isStellar = segment.Equals("SB", StringComparison.OrdinalIgnoreCase)
                    && i + 2 < segments.Length
                    && segments[i + 1].Equals("Content", StringComparison.OrdinalIgnoreCase)
                    && segments[i + 2].Equals("Paks", StringComparison.OrdinalIgnoreCase);
                var isLoader = segment.Equals("nativePC", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("reframework", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("MelonLoader", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("UE4SS", StringComparison.OrdinalIgnoreCase);
                if (!isStellar && !isLoader) continue;
                roots.Add(string.Join("/", segments.Take(i)));
                if (roots.Count >= minimumCount) return true;
                break;
            }
        }
        return false;
    }

    /// <summary>
    /// Only distinct complete triplets immediately inside the canonical shared
    /// Stellar Blade ~mods directory are individually staged. One mod wrapped in
    /// its own named folder is not decomposed, and non-triplet/variant packages
    /// remain under review rather than publishing incomplete pieces.
    /// </summary>
    private static IReadOnlyList<ModBoundaryCandidate>? DiscoverFlatStellarBladePaks(
        string[] relativePaths, string prefix)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var pakPaths = relativePaths.Where(path =>
            path.StartsWith(StellarBladePakRoot, StringComparison.OrdinalIgnoreCase)
            && !IsSupplementary(path)).ToArray();
        if (pakPaths.Length < 2) return null;

        var nonSupplementary = relativePaths.Where(path => !IsSupplementary(path)).ToArray();

        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in pakPaths)
        {
            var leaf = path[StellarBladePakRoot.Length..];
            if (leaf.Length == 0 || leaf.Contains('/'))
                return Review(prefix, "Nested Stellar Blade PAK layouts require package-boundary review.");
            var extension = Path.GetExtension(leaf);
            if (!extension.Equals(".pak", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".utoc", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".ucas", StringComparison.OrdinalIgnoreCase))
                return Review(prefix, "Non-PAK payload is mixed with the Stellar Blade bundle.");
            var stem = Path.GetFileNameWithoutExtension(leaf);
            if (!groups.TryGetValue(stem, out var members))
                groups.Add(stem, members = []);
            members.Add(path);
        }
        if (groups.Count < 2) return null;
        if (nonSupplementary.Any(path =>
                !path.StartsWith(StellarBladePakRoot, StringComparison.OrdinalIgnoreCase)))
            return Review(prefix, "Multiple PAK groups are coupled to unrelated package files and need review.");
        if (groups.Keys.Any(LooksLikeDependentOrVariant))
            return Review(prefix, "Stellar Blade main, patch or quality variants require manual selection.");
        // Multiple complete chunks in one named mod can be co-required, not
        // separate mods. Only a selected root or an explicitly named collection
        // permits automatic decomposition; elsewhere emit reviewable child rows.
        var isExplicitCollection = prefix.Length == 0 || IsCollectionWrapper(prefix);
        var complete = groups.Values.All(files => files.Count == 3 &&
            files.Select(file => Path.GetExtension(file))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3);
        return groups.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new ModBoundaryCandidate(Join(prefix, StellarBladePakRoot + pair.Key),
                isExplicitCollection && complete ? ModBoundaryKind.FileSet : ModBoundaryKind.Review,
                !complete ? "PAK bundle lacks a proven complete companion set."
                    : !isExplicitCollection ? "Multiple chunks in a named mod need dependency review before splitting."
                    : "Complete Stellar Blade PAK/UTOC/UCAS companion group.",
                pair.Value.Select(path => Join(prefix, path)).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                prefix.Length == 0 ? null : prefix)).ToArray();
    }

    private static bool IsCollectionWrapper(string prefix)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var leaf = prefix.Split('/')[^1].Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        return leaf is "mods" or "downloads" or "mod packs" or "mod library" or "archive vault"
            || leaf.Contains("collection", StringComparison.Ordinal);
    }

    private static bool LooksLikeDependentOrVariant(string stem)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var name = stem.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
            ? stem[..^2] : stem;
        var normalized = name.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return LooksLikeVariant(name) || tokens.Any(token => token is "main" or "base"
            or "patch" or "update" or "addon" or "add" or "fix" or "hotfix");
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
            if (PackageManifests.Contains(child) ||
                child.StartsWith("SB/Content/Paks/", StringComparison.OrdinalIgnoreCase)) return true;
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
        return tokens.Any(VariantLabels.Contains);
    }

    private static string VariantFamily(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = name.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        return string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !VariantLabels.Contains(token)));
    }
}
