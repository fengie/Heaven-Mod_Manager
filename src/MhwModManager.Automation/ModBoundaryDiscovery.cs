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
    string? SourcePrefix = null, bool RequiresSelection = false);

public sealed record ModBoundaryResult(IReadOnlyList<ModBoundaryCandidate> Candidates)
{
    /// <summary>All observed normalized files, including unresolved and supplementary files.
    /// Null means the caller supplied no inventory; an observed empty folder uses an empty list.</summary>
    public IReadOnlyList<string>? FileInventory { get; init; }

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

    public static ModBoundaryResult Discover(IEnumerable<string> relativeFilePaths, string? sourceName = null)
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
        return new(Analyze(files, "", 0, sourceName)) { FileInventory = Array.AsReadOnly(files) };
    }

    private static IReadOnlyList<ModBoundaryCandidate> Analyze(string[] relative, string prefix, int depth, string? sourceName)
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

        // A PAZ/PAMT set is one transport unit only with its own matching
        // archive index. Keep its exact bytes together for staging; this is NOT
        // permission to rewrite vanilla archives or meta/0.papgt.
        if (IsPearlAbyssArchiveSet(relative))
            return [new(Display(prefix), ModBoundaryKind.Folder,
                "One PAZ/PAMT archive set; live loader compatibility requires review.")];

        if (relative.Any(path => path.StartsWith("fomod/", StringComparison.OrdinalIgnoreCase)
            && IsFomodConfiguration(path)))
            return Review(prefix, "Installer options must be selected; never split FOMOD variants.");

        var opposites = directories.Select(directory => (directory, option: UnrealPakLayout.ContrastOption(directory)))
            .Where(pair => pair.option.HasValue)
            .GroupBy(pair => pair.option!.Value.Feature, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(pair => pair.option!.Value.Enabled).Distinct().Count() == 2)
            .SelectMany(group => group.Select(pair => pair.directory)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (opposites.Count > 1 && directories.All(directory =>
                opposites.Contains(directory) || IsSupplementaryDirectory(directory)))
            return Review(prefix, "Choose between complementary variants of one package.", requiresSelection: true);

        var archived = directFiles.Where(IsArchive).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var otherFiles = directFiles.Where(file => !IsArchive(file) && !IsSupplementary(file)).ToArray();

        // A project-qualified shared Paks folder can contain several complete
        // pak/utoc/ucas bundles. Carry the exact members as virtual package boundaries.
        // Never apply this split inside a named mod's own deployment layout.
        var pakGroups = DiscoverSharedPakGroups(relative, prefix, sourceName);
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

        var mounts = relative.Where(UnrealPakLayout.IsMember)
            .Select(UnrealPakLayout.ProjectMount).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (mounts.Length > 0)
        {
            var payload = relative.Where(path => !IsSupplementary(path) || UnrealPakLayout.IsMember(path))
                .Where(path => !IsPackageManifest(path)).ToArray();
            if (mounts.Length != 1 || payload.Any(path =>
                    !path.StartsWith(mounts[0] + "/", StringComparison.OrdinalIgnoreCase)) ||
                !UnrealPakLayout.Complete(payload))
                return Review(prefix, "Project Paks layout has incomplete companions or conflicting files.");
            return [new(Display(prefix), ModBoundaryKind.Folder, "One project-qualified Unreal Paks layout.")];
        }

        // Known installation roots cannot safely be separated from another
        // explicitly packaged sibling. Keep that conflicting tree in review
        // rather than installing a partial nativePC/BepInEx/UE4SS payload.
        if (directories.Any(StrongLoaderRoots.Contains))
        {
            if (archived.Length > 0 || directories.Where(d => !StrongLoaderRoots.Contains(d))
                .Any(d => HasExplicitChildPackageEvidence(relative, d)))
                return Review(prefix, "Independent packages conflict with a shared loader root.");

            // Do not strip BepInEx/nativePC/UE4SS from nested child packages.
            // Re-rooting those packages safely needs a game-specific adapter;
            // preserve an unambiguous single root and review any multiple roots.
            if (HasAtLeastIndependentPackageRoots(relative, 2))
                return Review(prefix, "Multiple packages under one loader root require layout-preserving review.");
            return [new(Display(prefix), ModBoundaryKind.Folder, "One game installation root.")];
        }

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

        // A declared target game does not establish whether several loose containers
        // are alternatives or co-required. Preserve the set as one reviewable source.
        if (directories.Length == 0 && archived.Length == 0)
        {
            var containers = otherFiles.Where(file => !IsPackageManifest(file)).ToArray();
            if (containers.Length > 1 && containers.All(UnrealPakLayout.IsMember) &&
                containers.Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
                return Review(prefix, "Multiple container stems share one source; review their package choices.", requiresSelection: true);
        }

        if (directFiles.Any(file => IsPackageManifest(file)))
        {
            if (archived.Length > 0)
                return Review(prefix, "Package manifest with nested archives needs review.");
            if (directFiles.Any(UnrealPakLayout.IsMember) &&
                relative.Any(path => path.Contains('/') && UnrealPakLayout.IsMember(path)))
                return Review(prefix, "The declared package combines root containers with nested components. Review the layout together.",
                    requiresSelection: true);
            return [new(Display(prefix), ModBoundaryKind.Folder, "One explicit package manifest.")];
        }

        // Anonymous loose DLL/PAK/DDS files cannot be confidently separated or auto-installed.
        // A stray file at a *collection* root must not conceal proven independent
        // package descendants. Keep the loose payload in review without publishing it.
        if (directories.Length > 0 && otherFiles.Any(UnrealPakLayout.IsMember) &&
            UnrealPakLayout.HasBundledEditorProject(relative))
            return [new(Display(prefix), ModBoundaryKind.Folder, "One finished Unreal package with its editor project.")];

        var reviewUnattributedFiles = otherFiles.Length > 0 && directories.Length > 0 && independentRoots;
        if (otherFiles.Length > 0 && !reviewUnattributedFiles)
        {
            if (directories.Length == 0 && archived.Length == 0 && otherFiles.Any(UnrealPakLayout.IsMember))
                return UnrealPakLayout.Complete(otherFiles, singleStem: true)
                    ? [new(Display(prefix), ModBoundaryKind.Folder, "One complete Unreal container set.")]
                    : Review(prefix, "Unreal container companions are incomplete or ambiguous.");

            if (otherFiles.Length == 1 && directories.Length == 0 && archived.Length == 0)
                return [new(Display(prefix), ModBoundaryKind.Folder, "One root-level payload.")];

            // A separate documentation or unproven nested folder does not make
            // standalone archive downloads inseparable. Hold the loose files
            // and unproven folder content for review while retaining safe archive
            // boundaries. Manifest and strong-loader conflicts were rejected above.
            if (archived.Length > 0)
            {
                var separate = archived.Select(archive => new ModBoundaryCandidate(
                    Join(prefix, archive), ModBoundaryKind.Archive, "Independent nested archive.")).ToList();
                separate.AddRange(Review(prefix,
                    "Unattributed loose files and unproven nested folders remain outside automatic import."));
                return separate;
            }
            return Review(prefix, "Loose files have no proven independent mod boundaries.");
        }

        var meaningfulDirectories = directories.Where(directory => !IsSupplementaryDirectory(directory)
            || children[directory].Any(path => !IsSupplementary(path))).ToArray();

        // Alternate settings for one authored package may each contain a complete
        // container. A documented set of sibling values for the same parameter is
        // a choice, even when additional companion folders share its package root.
        if (directFiles.Any(ManagedCollectionNotes.IsNote) &&
            meaningfulDirectories.Select(ChoiceParameter).OfType<string>()
                .GroupBy(parameter => parameter, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() >= 2) &&
            relative.All(path => ManagedCollectionNotes.IsNote(path) || IsSupplementary(path) || UnrealPakLayout.IsMember(path)))
            return Review(prefix, "Documented alternative settings need selection as one package.", requiresSelection: true);

        // Only siblings with matching package identity, after stripping known
        // variant tokens, are potential alternatives. One ordinary "High Heels"
        // mod must not suppress an unrelated hundred-mod collection.
        var ambiguousVariants = meaningfulDirectories
            .GroupBy(VariantFamily, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1 && group.Any(LooksLikeVariant))
            .SelectMany(group => group)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ambiguousVariants.UnionWith(opposites);
        if (ambiguousVariants.Count == meaningfulDirectories.Length
            && meaningfulDirectories.Length > 1
            && meaningfulDirectories.Select(VariantFamily)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            return Review(prefix, "Potential mutually exclusive variants need review as one package.", requiresSelection: true);

        var candidates = new List<ModBoundaryCandidate>();
        if (reviewUnattributedFiles)
        {
            var examples = string.Join(", ", otherFiles.Take(3).Select(file => file.Length <= 100 ? file : file[..100] + "..."));
            candidates.AddRange(Review(prefix, $"Unattributed collection-level loose files ({otherFiles.Length}): {examples}. Review their intended package."));
        }
        foreach (var archive in archived)
            candidates.Add(new(Join(prefix, archive), ModBoundaryKind.Archive, "Independent nested archive."));

        foreach (var directory in meaningfulDirectories)
        {
            var child = Join(prefix, directory);
            if (ambiguousVariants.Contains(directory))
            {
                candidates.AddRange(Review(child, "Package variants share an identity with a sibling; choose the intended option.", requiresSelection: true));
                continue;
            }
            var found = Analyze(children[directory].ToArray(), child, depth + 1, sourceName);
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
            // A container below a named child of a collection needs its own
            // inspection, including a review result for orphaned companions.
            if (segments.Length >= 2 && UnrealPakLayout.IsMember(path) &&
                !segments.Contains("Content", StringComparer.OrdinalIgnoreCase) &&
                !segments.Contains("~mods", StringComparer.OrdinalIgnoreCase) &&
                !segments.Any(StrongLoaderRoots.Contains))
            {
                roots.Add(string.Join("/", segments.Take(segments.Length - 1)));
                if (roots.Count >= minimumCount) return true;
            }
            if (segments.Length > 1 && IsPackageManifest(segments[^1]))
            {
                roots.Add(string.Join("/", segments.Take(segments.Length - 1)));
                if (roots.Count >= minimumCount) return true;
            }
            for (var i = 1; i < segments.Length - 1; i++)
            {
                var segment = segments[i];
                var isProjectMount = UnrealPakLayout.IsMember(path)
                    && i + 2 < segments.Length
                    && segments[i + 1].Equals("Content", StringComparison.OrdinalIgnoreCase)
                    && segments[i + 2].Equals("Paks", StringComparison.OrdinalIgnoreCase);
                var isLoader = segment.Equals("nativePC", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("reframework", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("MelonLoader", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("UE4SS", StringComparison.OrdinalIgnoreCase);
                if (!isProjectMount && !isLoader) continue;
                roots.Add(string.Join("/", segments.Take(i)));
                if (roots.Count >= minimumCount) return true;
                break;
            }
        }
        return false;
    }

    /// <summary>
    /// Only distinct complete IoStore sets immediately inside a project-qualified
    /// shared ~mods directory are individually staged. One mod wrapped in
    /// its own named folder is not decomposed, and non-triplet/variant packages
    /// remain under review rather than publishing incomplete pieces.
    /// </summary>
    private static IReadOnlyList<ModBoundaryCandidate>? DiscoverSharedPakGroups(
        string[] relativePaths, string prefix, string? sourceName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // A root manifest declares one authored package, even in a folder called Collection.
        if (relativePaths.Any(IsPackageManifest)) return null;
        var mounts = relativePaths.Where(UnrealPakLayout.IsMember)
            .Select(UnrealPakLayout.ProjectMount).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        if (mounts.Length != 1) return null;
        var sharedRoot = mounts[0] + "/~mods/";
        var pakPaths = relativePaths.Where(path =>
            path.StartsWith(sharedRoot, StringComparison.OrdinalIgnoreCase)
            && !IsSupplementary(path)).ToArray();
        if (pakPaths.Length < 2) return null;

        var nonSupplementary = relativePaths.Where(path => !IsSupplementary(path)).ToArray();

        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in pakPaths)
        {
            var leaf = path[sharedRoot.Length..];
            if (leaf.Length == 0 || leaf.Contains('/'))
                return Review(prefix, "Nested Unreal PAK layouts require package-boundary review.");
            if (!UnrealPakLayout.IsMember(leaf))
                return Review(prefix, "Non-container payload is mixed with the Unreal bundle.");
            var stem = Path.GetFileNameWithoutExtension(leaf);
            if (!groups.TryGetValue(stem, out var members))
                groups.Add(stem, members = []);
            members.Add(path);
        }
        if (groups.Count < 2) return null;
        if (nonSupplementary.Any(path =>
                !path.StartsWith(sharedRoot, StringComparison.OrdinalIgnoreCase)))
            return Review(prefix, "Multiple PAK groups are coupled to unrelated package files and need review.");
        // A single incomplete group or a labelled patch/variant must not suppress
        // unrelated complete mods in a collection. Hold every member of a
        // potentially dependent family together, including its unlabelled base.
        var reviewFamilies = groups.Keys.Where(LooksLikeDependentOrVariant)
            .Select(PakFamily).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Multiple complete chunks inside a named mod can be co-required. Only
        // an explicit shared collection may decompose unrelated complete sets.
        var isExplicitCollection = prefix.Length == 0
            ? sourceName is null || IsCollectionWrapper(sourceName)
            : IsCollectionWrapper(prefix);
        if (!isExplicitCollection && prefix.Length == 0 && sourceName is not null)
        {
            if (groups.Values.All(members => UnrealPakLayout.Complete(members, singleStem: true)) &&
                groups.Values.All(members => members.Any(file => file.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase))) &&
                reviewFamilies.Count == 0)
                return [new(Display(prefix), ModBoundaryKind.Folder, "One authored multi-container package.")];
            return Review(prefix, "One authored package has incomplete or potentially alternative container sets.");
        }
        return groups.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair =>
            {
                var complete = pair.Value.Any(file => file.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase)) &&
                    UnrealPakLayout.Complete(pair.Value, singleStem: true);
                var dependent = reviewFamilies.Contains(PakFamily(pair.Key));
                var kind = isExplicitCollection && complete && !dependent
                    ? ModBoundaryKind.FileSet : ModBoundaryKind.Review;
                var evidence = !complete ? "PAK bundle lacks a proven complete companion set."
                    : dependent ? "Related main, patch or variant PAK packages need manual selection."
                    : !isExplicitCollection ? "Multiple chunks in a named mod need dependency review before splitting."
                    : "Complete independent Unreal IoStore companion group.";
                return new ModBoundaryCandidate(Join(prefix, sharedRoot + pair.Key),
                    kind, evidence,
                    pair.Value.Select(path => Join(prefix, path))
                        .Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                    prefix.Length == 0 ? null : prefix);
            }).ToArray();
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
            or "core" or "root" or "patch" or "update" or "updated" or "addon"
            or "add" or "fix" or "fixed" or "hotfix");
    }

    private static string PakFamily(string stem)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var name = stem.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
            ? stem[..^2] : stem;
        // Use the same variant-family normalization as folder discovery; a
        // role-labelled sibling cannot silently be treated as an independent mod.
        var normalized = VariantFamily(name.Replace("add-on", "addon",
            StringComparison.OrdinalIgnoreCase));
        return string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token is not ("main" or "base" or "core" or "root"
                or "patch" or "update" or "updated" or "addon" or "add"
                or "fix" or "fixed" or "hotfix")));
    }

    private static IReadOnlyList<ModBoundaryCandidate> Review(string prefix, string reason, bool requiresSelection = false)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return [new(Display(prefix), ModBoundaryKind.Review, reason, RequiresSelection: requiresSelection)];
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

    private static bool IsPackageManifest(string file)
    {
        // DMM v3/v3.1 files may be named "anything.field.json" and may
        // reference companion DDS/WEM files. Never split that authored set.
        return PackageManifests.Contains(file)
            || (!file.Contains('/') && !file.Contains('\\')
                && file.EndsWith(".field.json", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPearlAbyssArchiveSet(string[] relativePaths)
    {
        var payload = relativePaths.Where(path => !IsSupplementary(path)).ToArray();
        if (payload.Length < 2 || payload.Length > 128) return false;
        if (payload.Any(path => !path.EndsWith(".paz", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".pamt", StringComparison.OrdinalIgnoreCase)))
            return false;

        var indices = payload.Where(path => path.EndsWith(".pamt", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (indices.Length != 1 || !Path.GetFileName(indices[0]).Equals("0.pamt", StringComparison.OrdinalIgnoreCase))
            return false;
        var parent = indices[0].Contains('/') ? indices[0][..indices[0].LastIndexOf('/')] : "";
        var archives = payload.Where(path => path.EndsWith(".paz", StringComparison.OrdinalIgnoreCase)).ToArray();
        return archives.Any(path => Path.GetFileName(path).Equals("0.paz", StringComparison.OrdinalIgnoreCase))
            && archives.All(path => (path.Contains('/') ? path[..path.LastIndexOf('/')] : "")
                .Equals(parent, StringComparison.OrdinalIgnoreCase));
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
        return file.Equals("Installation Instructions.txt", StringComparison.OrdinalIgnoreCase) || file.StartsWith("readme", StringComparison.OrdinalIgnoreCase)
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
            if (IsPackageManifest(child) ||
                child.StartsWith("SB/Content/Paks/", StringComparison.OrdinalIgnoreCase) ||
                (UnrealPakLayout.IsMember(child) && UnrealPakLayout.ProjectMount(child) is not null)) return true;
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

    private static string? ChoiceParameter(string directory)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var parts = directory.Replace('_', ' ').Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;
        var unit = int.TryParse(parts[0], out _) ? parts[1] : int.TryParse(parts[1], out _) ? parts[0] : "";
        return unit.ToLowerInvariant() is "fov" or "fps" or "resolution" ? unit : null;
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
