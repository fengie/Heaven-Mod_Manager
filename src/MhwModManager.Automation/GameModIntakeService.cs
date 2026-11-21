using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.Automation;

public enum GameModIntakeStatus { Routed, AlreadyPresent, NeedsReview, Skipped }

public sealed record GameModIntakeItem(
    string SourcePath, string? GameId, GameModIntakeStatus Status, string Reason);

public sealed record GameModIntakeProgress(int Completed, int Total, string CurrentPath, string Stage);

public sealed record GameModIntakeScan(IReadOnlyList<GameModIntakeItem> Items)
{
    public int Routed { get { using var __mhwTrace = MasterDebugLog.BeginMethod(); return Items.Count(item => item.Status == GameModIntakeStatus.Routed); } }
    public int AlreadyPresent { get { using var __mhwTrace = MasterDebugLog.BeginMethod(); return Items.Count(item => item.Status == GameModIntakeStatus.AlreadyPresent); } }
    public int NeedsReview { get { using var __mhwTrace = MasterDebugLog.BeginMethod(); return Items.Count(item => item.Status == GameModIntakeStatus.NeedsReview); } }
    public int Skipped { get { using var __mhwTrace = MasterDebugLog.BeginMethod(); return Items.Count(item => item.Status == GameModIntakeStatus.Skipped); } }
}

/// <summary>
/// Safe, repeatable intake from explicitly designated directories, independent of the
/// currently active game. Copies high-confidence packages into the owning game's Inbox;
/// SmartInbox remains the sole importer and no source or live game files are moved/deleted.
/// </summary>
public sealed class GameModIntakeService
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };
    private const int MaxSources = 12;
    private const int MaxEntriesPerSource = 50_000;
    private const int MaxFolderFiles = 50_000;
    private const int MaxContainerDepth = 64;
    private const int MaxNestedArchiveDepth = 4;
    private const long MaxArchiveBytes = 8L * 1024 * 1024 * 1024;
    private const int MaxGameManifestBytes = 16 * 1024;
    // Generic mod metadata is often larger than an app-owned game declaration.
    // Inspect it within a separate capped budget instead of rejecting ordinary mods.
    private const int MaxThirdPartyManifestBytes = 256 * 1024;
    private const int MaxArchiveManifestScanBytes = 4 * 1024 * 1024;
    private const int IoBufferSize = 128 * 1024;
    private const string CollectionCopyLimitError =
        "Mod intake exceeded its cumulative selected-collection byte budget; import a smaller batch.";
    private readonly string managerHome;
    private readonly string stateRoot;
    private readonly string settingsPath;
    private readonly Func<IReadOnlyList<GameProfile>> loadGames;
    private readonly ArchiveInspector archive;
    private readonly long collectionCopyBudget;
    private readonly int inventoryEntryBudget;

    public string ReportPath
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Path.Combine(stateRoot, "last-mod-intake-report.json");
        }
    }

    public GameModIntakeService(
        string managerHome, string stateRoot, Func<IReadOnlyList<GameProfile>> loadGames,
        ArchiveInspector? archive = null, long collectionCopyBudget = MaxArchiveBytes,
        int inventoryEntryBudget = MaxFolderFiles)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(managerHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        ArgumentNullException.ThrowIfNull(loadGames);
        if (collectionCopyBudget <= 0 || collectionCopyBudget > MaxArchiveBytes)
            throw new ArgumentOutOfRangeException(nameof(collectionCopyBudget),
                "The collection copy budget must be between 1 byte and 8 GiB.");
        if (inventoryEntryBudget <= 0 || inventoryEntryBudget > MaxFolderFiles)
            throw new ArgumentOutOfRangeException(nameof(inventoryEntryBudget),
                "The filesystem inventory budget must be between 1 and 50,000 entries.");
        this.collectionCopyBudget = collectionCopyBudget;
        this.inventoryEntryBudget = inventoryEntryBudget;
        this.managerHome = Path.GetFullPath(managerHome);
        this.stateRoot = Path.GetFullPath(stateRoot);
        this.loadGames = loadGames;
        this.archive = archive ?? new ArchiveInspector();
        settingsPath = Path.Combine(this.stateRoot, "mod-intake-folders.json");
    }

    public IReadOnlyList<string> LoadSources()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!File.Exists(settingsPath)) return [];
        RefuseReparseComponents(settingsPath);
        using var reader = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (reader.Length > 16 * 1024)
            throw new InvalidDataException("Auto-sort source configuration exceeds its 16 KiB limit.");
        using var textReader = new StreamReader(reader, Encoding.UTF8, true, 1024, leaveOpen: true);
        string[] sources;
        try
        {
            sources = JsonSerializer.Deserialize<string[]>(textReader.ReadToEnd())
                ?? throw new InvalidDataException("Auto-sort source configuration is null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Auto-sort source configuration is malformed.", ex);
        }
        if (sources.Length > MaxSources || sources.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
            throw new InvalidDataException("Auto-sort source configuration contains an invalid path.");
        return sources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void AddSource(string sourceFolder)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var full = ValidateSource(sourceFolder);
        var old = LoadSources();
        if (old.Contains(full, StringComparer.OrdinalIgnoreCase)) return;
        if (old.Count >= MaxSources)
            throw new InvalidDataException("At most 12 intake folders can be watched.");
        SaveSources([.. old, full]);
    }

    public void RemoveSource(string sourceFolder)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        var full = Path.GetFullPath(sourceFolder);
        var existing = LoadSources();
        SaveSources(existing.Where(path => !path.Equals(full, StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    public async Task<GameModIntakeScan> ScanAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result = new List<GameModIntakeItem>();
        var receipts = LoadReceipts();
        var games = loadGames().Where(game => GameProfile.IsCanonicalId(game.Id))
            .DistinctBy(game => game.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var source in LoadSources())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                ValidateSource(source);
                await ScanSourceAsync(source, games, result, receipts, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                result.Add(new(source, null, GameModIntakeStatus.NeedsReview, ex.Message));
            }
        }

        var report = new GameModIntakeScan(result);
        try
        {
            Directory.CreateDirectory(stateRoot);
            SaveAtomic(ReportPath, JsonSerializer.Serialize(report, PrettyJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MasterDebugLog.Write("MOD-INTAKE", "Could not save local auto-sort review report.", ex);
        }
        return report;
    }

    /// <summary>
    /// One-shot intake of a selected folder or archive, without registering a watcher.
    /// The bulk overload shares game discovery, receipts, and report publication across
    /// a selection rather than restarting that work for each archive.
    /// </summary>
    public async Task<GameModIntakeScan> ImportPathAsync(string inputPath, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        var full = Path.GetFullPath(inputPath);
        if (!Directory.Exists(full) && !File.Exists(full))
            throw new FileNotFoundException("Selected mod input does not exist.", full);
        return await ImportPathsCoreAsync([full], null, false, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Explicit bulk selections can independently stage each downloaded archive; legacy
    /// one-path collection imports retain their original shared physical-copy budget.
    /// Progress events are advisory and never authorize installation or bypass safety review.
    /// All source paths remain unchanged. The owning game's Inbox stages accepted candidates.
    /// </summary>
    public async Task<GameModIntakeScan> ImportPathsAsync(
        IEnumerable<string> inputPaths, IProgress<GameModIntakeProgress>? progress = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return await ImportPathsCoreAsync(inputPaths, progress, true, ct).ConfigureAwait(false);
    }

    private async Task<GameModIntakeScan> ImportPathsCoreAsync(
        IEnumerable<string> inputPaths, IProgress<GameModIntakeProgress>? progress,
        bool independentArchiveFolder, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(inputPaths);
        var inputs = inputPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (inputs.Length == 0)
            return new GameModIntakeScan([]);
        if (inputs.Length > 2048)
            throw new InvalidDataException("Import at most 2,048 selected items at a time.");

        var games = loadGames().Where(game => GameProfile.IsCanonicalId(game.Id))
            .DistinctBy(game => game.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var receipts = LoadReceipts();
        var items = new List<GameModIntakeItem>();
        for (var index = 0; index < inputs.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var input = inputs[index];
            progress?.Report(new(index, inputs.Length, input ?? "", "Inspecting"));
            try
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(input);
                var full = Path.GetFullPath(input);
                var isDirectory = Directory.Exists(full);
                if (!isDirectory && !File.Exists(full))
                    throw new FileNotFoundException("Selected mod input does not exist.", full);
                if (isDirectory)
                {
                    ValidateSource(full);
                    // A directory containing only downloaded archives is a batch of
                    // independent user-selected packages, not one nested mod pack.
                    // Give each archive its own existing 8 GiB bounded intake budget:
                    // otherwise the old collection-wide limit rejects unrelated
                    // large archives after the first few files.
                    var topEntries = Directory.EnumerateFileSystemEntries(full)
                        .Take(MaxEntriesPerSource + 1).ToArray();
                    if (topEntries.Length > MaxEntriesPerSource)
                        throw new InvalidDataException("The selected folder exceeds the safe entry limit.");
                    if (independentArchiveFolder && topEntries.Length > 0 && topEntries.All(path =>
                            File.Exists(path) && IsSupportedArchive(path)))
                    {
                        for (var archiveIndex = 0; archiveIndex < topEntries.Length; archiveIndex++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var archivePath = topEntries[archiveIndex];
                            progress?.Report(new(index, inputs.Length, archivePath,
                                $"Archive {archiveIndex + 1}/{topEntries.Length}"));
                            try
                            {
                                RefuseReparseComponents(archivePath);
                                await ClassifyAndRouteAsync(full, archivePath, false,
                                    games, items, receipts, ct,
                                    copyProgress: (copied, length) => progress?.Report(new(
                                        index, inputs.Length, archivePath,
                                        $"Copying {copied / 1048576d:F1} / {length / 1048576d:F1} MiB")))
                                    .ConfigureAwait(false);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                or InvalidDataException or JsonException)
                            {
                                items.Add(new(archivePath, null, GameModIntakeStatus.NeedsReview, ex.Message));
                            }
                        }
                    }
                    else
                    {
                        await ScanSourceAsync(full, games, items, receipts, ct,
                            progressCandidate: (completed, total, candidate) =>
                                progress?.Report(new(index, inputs.Length, candidate,
                                    $"Scanning folder {completed}/{total}"))).ConfigureAwait(false);
                    }
                }
                else
                {
                    // A manual selection is a read-only source. Never interpret the
                    // selected path as authorization to install into a live game.
                    RefuseReparseComponents(full);
                    foreach (var game in games)
                        if (ContainsPath(game.GameRoot, full))
                            throw new InvalidDataException("Cannot import directly from a live game installation.");
                    if (IsSupportedArchive(full))
                        await ClassifyAndRouteAsync(Path.GetDirectoryName(full)!, full, false,
                            games, items, receipts, ct,
                            copyProgress: (copied, length) => progress?.Report(new(
                                index, inputs.Length, full,
                                $"Copying {copied / 1048576d:F1} / {length / 1048576d:F1} MiB")))
                            .ConfigureAwait(false);
                    else
                        items.Add(new(full, null, GameModIntakeStatus.NeedsReview,
                            "Select a folder or a ZIP, RAR or 7z archive."));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException or JsonException or ArgumentException or NotSupportedException)
            {
                items.Add(new(input ?? "", null, GameModIntakeStatus.NeedsReview, ex.Message));
            }
            progress?.Report(new(index + 1, inputs.Length, input ?? "", "Completed"));
        }

        var report = new GameModIntakeScan(items);
        try
        {
            Directory.CreateDirectory(stateRoot);
            SaveAtomic(ReportPath, JsonSerializer.Serialize(report, PrettyJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MasterDebugLog.Write("MOD-INTAKE", "Could not save bulk mod-intake report.", ex);
        }
        return report;
    }

    /// <summary>
    /// Explicit user assignment of one independently packaged, review-only
    /// filesystem source. This is not an override for multi-mod bundles,
    /// FOMOD options, verified game evidence, or archive-child virtual paths.
    /// No live game installation is touched; the existing safe Inbox path owns publication.
    /// </summary>
    public async Task<GameModIntakeScan> AssignReviewedPackageAsync(
        string inputPath, string gameId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        if (inputPath.Contains("!/", StringComparison.Ordinal))
            throw new InvalidDataException("Nested archive children need separate package review; do not assign the outer archive.");

        var source = Path.GetFullPath(inputPath);
        var isDirectory = Directory.Exists(source);
        if (!isDirectory && (!File.Exists(source) || !IsSupportedArchive(source)))
            throw new InvalidDataException("Manual assignment requires one existing folder or ZIP/RAR/7z package.");

        var games = loadGames().Where(game => GameProfile.IsCanonicalId(game.Id))
            .DistinctBy(game => game.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var game = games.SingleOrDefault(candidate =>
            candidate.Id.Equals(gameId, StringComparison.OrdinalIgnoreCase));
        if (game is null)
            throw new InvalidDataException("Select a currently registered game before assigning this package.");

        if (isDirectory)
            ValidateSource(source);
        else
        {
            RefuseReparseComponents(source);
            if (ContainsPath(managerHome, source))
                throw new InvalidDataException("Cannot assign a package from the manager's own library.");
            foreach (var registered in loadGames())
                if (ContainsPath(registered.GameRoot, source))
                    throw new InvalidDataException("Cannot import directly from a live game installation.");
        }

        // Reinspect the selected physical source at confirmation time. A report
        // row is not a trusted filesystem capability and may have become stale.
        string[] files;
        if (isDirectory)
        {
            files = SnapshotFolderFiles(source, ct)
                .Select(path => Path.GetRelativePath(source, path)).ToArray();
        }
        else
        {
            var inspection = await archive.InspectAsync(source, ct).ConfigureAwait(false);
            if (inspection.HasSuspiciousPaths)
                throw new InvalidDataException("Unsafe archive paths cannot be manually assigned.");
            new IntakeBudget(collectionCopyBudget).Account(inspection);
            files = inspection.Entries.Where(entry => !entry.IsDirectory)
                .Select(entry => entry.Key).ToArray();
        }

        var boundaries = ModBoundaryDiscovery.Discover(files);
        if (boundaries.Candidates.Count != 1 ||
            boundaries.Candidates[0].Kind != ModBoundaryKind.Folder ||
            boundaries.Candidates[0].RelativePath != ".")
            throw new InvalidDataException(
                "Manual game assignment requires one independent package. Split or review nested mods and installer options separately.");

        var output = new List<GameModIntakeItem>();
        var sourceRoot = isDirectory ? source : Path.GetDirectoryName(source)!;
        await ClassifyAndRouteAsync(sourceRoot, source, isDirectory, games, output,
            LoadReceipts(), ct, manualGameId: game.Id).ConfigureAwait(false);
        if (output.Count != 1 ||
            output[0].Status is not (GameModIntakeStatus.Routed or GameModIntakeStatus.AlreadyPresent))
            throw new InvalidDataException("The source changed or could not be safely assigned; no manual route was confirmed.");
        return new GameModIntakeScan(output);
    }

    private sealed class IntakeBudget
    {
        public IntakeBudget(long copyBudget)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            RemainingActualBytes = copyBudget;
        }

        public long RemainingExpandedBytes { get; private set; } = MaxArchiveBytes;
        public int RemainingEntries { get; private set; } = MaxEntriesPerSource;
        public long RemainingActualBytes { get; private set; }

        public void AccountActual(long bytes)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            if (bytes < 0 || bytes > RemainingActualBytes)
                throw new InvalidDataException(CollectionCopyLimitError);
            RemainingActualBytes -= bytes;
        }

        public void Account(ArchiveInspection inspection)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            if (inspection.ExpandedBytes > RemainingExpandedBytes ||
                inspection.Entries.Count > RemainingEntries)
                throw new InvalidDataException("Nested mod-pack inspection exceeded its cumulative safety budget.");
            RemainingExpandedBytes -= inspection.ExpandedBytes;
            RemainingEntries -= inspection.Entries.Count;
        }
    }

    private async Task ScanSourceAsync(
        string root, IReadOnlyList<GameProfile> games, List<GameModIntakeItem> output,
        HashSet<string> receipts, CancellationToken ct, bool requireMultipleBoundaries = false,
        Action<int, int, string>? progressCandidate = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var paths = SnapshotFolderFiles(root, ct).Select(path => Path.GetRelativePath(root, path)).ToArray();
        var discovered = ModBoundaryDiscovery.Discover(paths);
        // Reanalyzing an existing tracked package must never copy its whole root as
        // a duplicate when structural evidence no longer proves several child mods.
        if (requireMultipleBoundaries &&
            discovered.Candidates.Count(candidate => candidate.Kind != ModBoundaryKind.Review) < 2)
        {
            output.Add(new(root, null, GameModIntakeStatus.Skipped,
                "The tracked source does not contain at least two independently identifiable mods."));
            return;
        }
        // Share one inspection and physical-copy budget across every candidate from
        // this selected collection. Nested archive recursion carries the same instance.
        var collectionBudget = new IntakeBudget(collectionCopyBudget);
        var candidateIndex = 0;
        foreach (var candidate in discovered.Candidates)
        {
            ct.ThrowIfCancellationRequested();
            var entry = candidate.RelativePath == "." ? root
                : Path.Combine(root, candidate.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            progressCandidate?.Invoke(++candidateIndex, discovered.Candidates.Count, entry);
            if (candidate.Kind == ModBoundaryKind.Review)
            {
                output.Add(new(entry, null, GameModIntakeStatus.NeedsReview, candidate.Evidence));
                continue;
            }
            try
            {
                if (candidate.Kind == ModBoundaryKind.FileSet)
                    await RouteFileSetAsync(root, candidate, games, output, receipts,
                        0, collectionBudget, entry, ct).ConfigureAwait(false);
                else
                {
                    RefuseReparseComponents(entry);
                    await ClassifyAndRouteAsync(root, entry,
                        candidate.Kind == ModBoundaryKind.Folder,
                        games, output, receipts, ct, 0, collectionBudget).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                output.Add(new(entry, null, GameModIntakeStatus.NeedsReview, ex.Message));
            }
        }
    }

    /// <summary>
    /// Read-only structural preview for one existing tracked, direct-child library package.
    /// Unlike normal designated-folder intake this narrowly permits manager-owned sources,
    /// but never scans the library root or a live game installation.
    /// </summary>
    public ModBoundaryResult PreviewManagedCollection(
        string sourcePath, GameProfile game, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var source = ValidateManagedCollectionSource(sourcePath, game);
        return ModBoundaryDiscovery.Discover(SnapshotFolderFiles(source, ct)
            .Select(file => Path.GetRelativePath(source, file)));
    }

    public ManagedCollectionLayout InspectManagedShelf(string source, GameProfile game, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var inventory = PreviewManagedCollection(source, game, ct).FileInventory ?? [];
        var files = inventory.Where(path => !ManagedCollectionNotes.IsNote(path)).ToArray();
        if (files.Length == 0 || files.Any(path => !path.Contains('/')))
            return new([], false);

        return PartitionShelf(source, files, ct);
    }

    private static ManagedCollectionLayout PartitionShelf(string source, string[] files, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        while (files.Length > 0 && files.All(path => path.Contains('/')))
        {
            var prefix = files[0][..(files[0].IndexOf('/') + 1)];
            if (!files.All(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) break;
            // Never unwrap a shared loader, installer, manifest or project-qualified root.
            var enclosing = ModBoundaryDiscovery.Discover(files).Candidates;
            if (enclosing.Count < 2 || enclosing.Any(c => c.RelativePath == ".")) return new([], false);
            source = Path.Combine(source, prefix.TrimEnd('/'));
            files = files.Select(path => path[prefix.Length..]).ToArray();
        }
        if (files.Any(path => !path.Contains('/'))) return new([], false);
        var branches = files.GroupBy(path => path[..path.IndexOf('/')], StringComparer.OrdinalIgnoreCase).ToArray();
        var boundaries = ModBoundaryDiscovery.Discover(files);
        if (branches.Length < 2 || boundaries.Candidates.All(candidate => candidate.RelativePath == "."))
            return new([], false);
        var units = new List<ManagedCollectionUnit>();
        foreach (var branch in branches)
        {
            ct.ThrowIfCancellationRequested();
            var relative = branch.Select(path => path[(branch.Key.Length + 1)..]).ToArray();
            var branchPath = Path.Combine(source, branch.Key);
            var containers = relative.Where(UnrealPakLayout.IsMember).ToArray();
            // A project source tree may accompany a finished package. Its editor files
            // are attributable only when the same subtree contains a project descriptor.
            var editorRoots = relative.Where(path => path.Contains('/') && path.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase))
                .Select(path => path[..(path.IndexOf('/') + 1)]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var clean = relative.All(path => UnrealPakLayout.IsMember(path) ||
                editorRoots.Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase)));
            var protectedChoice = boundaries.Candidates.Any(c => c.Kind == ModBoundaryKind.Review &&
                c.RelativePath.Equals(branch.Key, StringComparison.OrdinalIgnoreCase) &&
                c.Evidence.Contains("variant", StringComparison.OrdinalIgnoreCase));
            if (clean && !protectedChoice && UnrealPakLayout.Complete(containers, singleStem: true))
            {
                var parent = Path.GetDirectoryName(containers[0]) ?? "";
                if (parent.EndsWith("~mods", StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(parent).Equals("~mods", StringComparison.OrdinalIgnoreCase))
                    parent = Path.GetDirectoryName(parent) ?? "";
                units.Add(new(Path.GetFullPath(Path.Combine(branchPath, parent)), false));
            }
            else
            {
                // Only descend through proven independent siblings. Reused output names
                // are commonly alternate versions that must stay together for review.
                var distinctOutputs = containers.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == containers.Length;
                var children = !protectedChoice && clean && containers.Length > 1 && distinctOutputs &&
                    relative.All(path => path.Contains('/')) ? PartitionShelf(branchPath, relative, ct) : new([], false);
                if (children.FullyAccounted) units.AddRange(children.Units);
                else units.Add(new(branchPath, true));
            }
        }
        return units.Any(unit => !unit.NeedsReview) ? new(units, true) : new([], false);
    }

    /// <summary>Return physical, independently selectable Unreal packages in the registered library.
    /// Discovery reads filenames only. Capturing the bytes belongs to selection and Preview.</summary>
    public IReadOnlyList<string> SelectableManagedFolders(string source, GameProfile game, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (game.AdapterId != "unreal-paks") return [];
        var preview = PreviewManagedCollection(source, game, ct);
        var inventory = preview.FileInventory ?? [];
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var members = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in inventory)
        {
            ct.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(file)?.Replace('\\', '/') ?? "";
            if (!members.TryGetValue(dir, out var group)) members[dir] = group = [];
            group.Add(file);
            // Ancestor counts ensure a package with unrelated sidecars never becomes selectable.
            while (dir.Length > 0)
            {
                counts[dir] = counts.GetValueOrDefault(dir) + 1;
                var separator = dir.LastIndexOf('/');
                if (separator < 0) break;
                dir = dir[..separator];
            }
        }
        var boundaries = preview.Candidates;
        // An unattributed PAK at the shelf root remains unselected. Re-evaluate only
        // its nested siblings with the same manifest, variant and package checks.
        var loose = inventory.Where(file => !file.Contains('/')).ToArray();
        if (boundaries.All(candidate => candidate.Kind == ModBoundaryKind.Review) &&
            loose.Length > 0 && loose.All(UnrealPakLayout.IsMember))
            boundaries = ModBoundaryDiscovery.Discover(inventory.Where(file => file.Contains('/'))).Candidates;
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in boundaries.Where(c => c.Kind == ModBoundaryKind.Folder))
        {
            var leaf = candidate.RelativePath.Replace('\\', '/');
            if (!members.TryGetValue(leaf, out var files) || !UnrealPakLayout.Complete(files, singleStem: true)) continue;
            var name = leaf.EndsWith("/~mods", StringComparison.OrdinalIgnoreCase) ? leaf[..^6] : leaf;
            if (name == "." || name.Equals("~mods", StringComparison.OrdinalIgnoreCase) ||
                !counts.TryGetValue(name, out var total) || total != files.Count) continue;
            selected.Add(Path.GetFullPath(Path.Combine(source, name)));
        }
        return selected.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Read-only complete file-accounting preview of a managed package. Unlike
    /// automatic import this reports even one, incomplete, ambiguous or unknown
    /// companion group. It never writes to Inbox, State or a live game tree.
    /// </summary>
    public ModLayoutInspection PreviewManagedCollectionLayout(
        string sourcePath, GameProfile game, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var inventory = PreviewManagedCollection(sourcePath, game, ct);
        return ModLayoutPreview.Inspect(inventory, game, ct);
    }

    /// <summary>
    /// Copies only proven independent children of an already tracked source to their
    /// registered game Inboxes. The original package, database ID, activation and
    /// live-game files are never modified or retired by reanalysis.
    /// </summary>
    public async Task<GameModIntakeScan> ImportManagedCollectionAsync(
        string sourcePath, GameProfile game, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var source = ValidateManagedCollectionSource(sourcePath, game);
        var boundaries = PreviewManagedCollection(source, game, ct);
        if (boundaries.Candidates.Count(candidate => candidate.Kind != ModBoundaryKind.Review) < 2)
            return new GameModIntakeScan([
                new GameModIntakeItem(source, null, GameModIntakeStatus.Skipped,
                    "The tracked source is not a proven multi-mod collection; nothing was copied.")
            ]);

        var games = loadGames().Where(candidate => GameProfile.IsCanonicalId(candidate.Id))
            .DistinctBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var items = new List<GameModIntakeItem>();
        await ScanSourceAsync(source, games, items, LoadReceipts(), ct,
            requireMultipleBoundaries: true).ConfigureAwait(false);
        var report = new GameModIntakeScan(items);
        try
        {
            Directory.CreateDirectory(stateRoot);
            SaveAtomic(ReportPath, JsonSerializer.Serialize(report, PrettyJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MasterDebugLog.Write("MOD-INTAKE", "Could not persist the managed-collection reanalysis report.", ex);
        }
        return report;
    }

    private string ValidateManagedCollectionSource(string sourcePath, GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(game);
        var registeredGames = loadGames().Where(candidate => GameProfile.IsCanonicalId(candidate.Id))
            .ToArray();
        var registered = registeredGames.SingleOrDefault(candidate =>
            candidate.Id.Equals(game.Id, StringComparison.OrdinalIgnoreCase));
        if (registered is null)
            throw new InvalidDataException("Reanalysis requires the current registered game profile.");

        var source = Path.GetFullPath(sourcePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException("Tracked mod source directory is unavailable: " + source);

        var library = GameModStorage.Resolve(managerHome, registered);
        var parent = Path.GetDirectoryName(source);
        var isDirectPackage = new[] { library.InstalledRoot, library.LegacyInstalledRoot }
            .Any(root => string.Equals(parent,
                Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase));
        if (!isDirectPackage)
            throw new InvalidDataException(
                "Only an existing direct-child installed library package can be reanalyzed.");

        if (registeredGames.Any(candidate => ContainsPath(candidate.GameRoot, source)))
            throw new InvalidDataException("Live game files cannot be reanalyzed as managed packages.");

        RefuseReparseComponents(source);
        return source;
    }

    /// <summary>
    /// Materialize one exact, verified companion group into dedicated catalog-invisible
    /// scratch storage. Reuse the existing folder classifier, digest/receipts and Inbox
    /// publication contract rather than adding a second importer.
    /// </summary>
    private async Task RouteFileSetAsync(
        string sourceDirectory, ModBoundaryCandidate candidate, IReadOnlyList<GameProfile> games,
        List<GameModIntakeItem> output, HashSet<string> receipts,
        int nestedDepth, IntakeBudget budget, string provenance, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (candidate.Kind != ModBoundaryKind.FileSet || candidate.Files is not { Count: > 0 })
            throw new InvalidDataException("A virtual package must identify its exact source files.");

        var scratch = CreateScratchPath();
        var package = Path.Combine(scratch, SafePackageName(candidate.RelativePath));
        try
        {
            Directory.CreateDirectory(package);
            long copiedBytes = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var buffer = new byte[IoBufferSize];
            foreach (var relative in candidate.Files)
            {
                ct.ThrowIfCancellationRequested();
                var normalized = relative.Replace('\\', '/');
                if (normalized.StartsWith('/') || normalized.Split('/').Any(part =>
                        part is "" or "." or ".." || part.Contains(':')) || !seen.Add(normalized))
                    throw new InvalidDataException("An unsafe or duplicate virtual mod-package path was refused.");

                var gameRelative = normalized;
                if (!string.IsNullOrWhiteSpace(candidate.SourcePrefix))
                {
                    var wrapper = candidate.SourcePrefix.Replace('\\', '/').TrimEnd('/');
                    if (!normalized.StartsWith(wrapper + "/", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("A virtual mod-package member did not belong to its source wrapper.");
                    gameRelative = normalized[(wrapper.Length + 1)..];
                }
                var input = Path.GetFullPath(Path.Combine(sourceDirectory,
                    normalized.Replace('/', Path.DirectorySeparatorChar)));
                var destination = Path.GetFullPath(Path.Combine(package,
                    gameRelative.Replace('/', Path.DirectorySeparatorChar)));
                if (!ContainsPath(sourceDirectory, input) || !ContainsPath(package, destination))
                    throw new InvalidDataException("A virtual mod-package path escaped its staging boundary.");
                RefuseReparseComponents(input);
                RefuseReparseComponents(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                RefuseReparseComponents(destination);

                await using var reader = new FileStream(input, FileMode.Open, FileAccess.Read,
                    FileShare.Read, IoBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var writer = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, IoBufferSize, FileOptions.Asynchronous);
                int count;
                while ((count = await reader.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    if (count > MaxArchiveBytes - copiedBytes)
                        throw new InvalidDataException("A virtual mod package exceeded its 8 GiB staging budget.");
                    copiedBytes += count;
                    budget.AccountActual(count);
                    await writer.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                }
            }
            await ClassifyAndRouteAsync(sourceDirectory, package, true, games, output,
                receipts, ct, nestedDepth, budget, provenance).ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                // Cleanup must still check the full owned scratch tree even when
                // the requested import was cancelled.
                SafeRecursiveTraversal.EnsureNoReparsePoints(scratch, CancellationToken.None);
                Directory.Delete(scratch, recursive: true);
            }
        }
    }

    private async Task ClassifyAndRouteAsync(
        string sourceRoot, string source, bool isDirectory, IReadOnlyList<GameProfile> games,
        List<GameModIntakeItem> output, HashSet<string> receipts, CancellationToken ct,
        int nestedDepth = 0, IntakeBudget? budget = null, string? provenance = null,
        string? gameContextHint = null, string? manualGameId = null,
        Action<long, long>? copyProgress = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        budget ??= new IntakeBudget(collectionCopyBudget);
        var reportSource = provenance ?? source;
        if (nestedDepth > MaxNestedArchiveDepth)
        {
            output.Add(new(reportSource, null, GameModIntakeStatus.NeedsReview,
                "Nested mod archives exceed the safe depth limit."));
            return;
        }

        string[] paths;
        if (isDirectory)
        {
            paths = SnapshotFolderFiles(source, ct).Select(path => Path.GetRelativePath(source, path)).ToArray();
            if (paths.Any(path => path.Replace('\\', '/')
                .EndsWith("fomod/ModuleConfig.xml", StringComparison.OrdinalIgnoreCase)))
            {
                output.Add(new(reportSource, null, GameModIntakeStatus.NeedsReview,
                    "FOMOD installer options need manual selection; source left untouched."));
                return;
            }
        }
        else
        {
            var info = await archive.InspectAsync(source, ct).ConfigureAwait(false);
            if (info.HasSuspiciousPaths)
                throw new InvalidDataException("Unsafe archive paths require manual review.");
            budget.Account(info);
            var discovery = ModBoundaryDiscovery.Discover(info.Entries
                .Where(entry => !entry.IsDirectory).Select(entry => entry.Key));
            // No extraction is necessary when every candidate requires review.
            // Preserve each incomplete group as a separate evidence-bearing row.
            if (discovery.Candidates.Count > 0 &&
                discovery.Candidates.All(candidate => candidate.Kind == ModBoundaryKind.Review))
            {
                foreach (var candidate in discovery.Candidates)
                {
                    var childProvenance = candidate.RelativePath == "." ? reportSource
                        : reportSource + "!/" + candidate.RelativePath;
                    output.Add(new(childProvenance, null, GameModIntakeStatus.NeedsReview,
                        candidate.Evidence));
                }
                return;
            }

            var singlePackage = discovery.Candidates.Count == 1
                && discovery.Candidates[0].Kind == ModBoundaryKind.Folder
                && discovery.Candidates[0].RelativePath == ".";
            if (!singlePackage)
            {
                if (discovery.Candidates.Count == 0)
                {
                    output.Add(new(reportSource, null, GameModIntakeStatus.NeedsReview,
                        "Archive has no safely identifiable mod packages."));
                    return;
                }
                if (nestedDepth >= MaxNestedArchiveDepth)
                {
                    output.Add(new(reportSource, null, GameModIntakeStatus.NeedsReview,
                        "Nested mod archives exceed the safe depth limit."));
                    return;
                }

                var scratch = CreateScratchPath();
                var trustedRoot = Path.GetDirectoryName(scratch)!;
                try
                {
                    if (budget.RemainingActualBytes <= 0)
                        throw new InvalidDataException("Nested mod-pack output exhausted its cumulative byte budget.");
                    await archive.ExtractSafelyAsync(source, scratch, trustedRoot,
                        new ArchiveExtractionLimits(MaxArchiveBytes, budget.RemainingActualBytes), ct)
                        .ConfigureAwait(false);
                    long expandedBytes = 0;
                    foreach (var file in SnapshotFolderFiles(scratch, ct))
                    {
                        var length = new FileInfo(file).Length;
                        if (length > budget.RemainingActualBytes - expandedBytes)
                            throw new InvalidDataException("Nested mod-pack actual expansion exceeded its budget.");
                        expandedBytes += length;
                    }
                    budget.AccountActual(expandedBytes);
                    foreach (var candidate in discovery.Candidates)
                    {
                        ct.ThrowIfCancellationRequested();
                        var child = candidate.RelativePath == "." ? scratch
                            : Path.Combine(scratch, candidate.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                        var childProvenance = reportSource + "!/" + candidate.RelativePath;
                        var childGameContextHint = discovery.Candidates.Count == 1
                            && candidate.Kind == ModBoundaryKind.Folder
                            ? gameContextHint ?? Path.GetFileName(source) : null;
                        if (candidate.Kind == ModBoundaryKind.Review)
                        {
                            output.Add(new(childProvenance, null, GameModIntakeStatus.NeedsReview, candidate.Evidence));
                            continue;
                        }
                        try
                        {
                            if (candidate.Kind == ModBoundaryKind.FileSet)
                                await RouteFileSetAsync(scratch, candidate, games, output, receipts,
                                    nestedDepth + 1, budget, childProvenance, ct).ConfigureAwait(false);
                            else
                            {
                                RefuseReparseComponents(child);
                                await ClassifyAndRouteAsync(sourceRoot, child,
                                    candidate.Kind == ModBoundaryKind.Folder,
                                    games, output, receipts, ct, nestedDepth + 1, budget, childProvenance,
                                    childGameContextHint).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
                        {
                            output.Add(new(childProvenance, null, GameModIntakeStatus.NeedsReview, ex.Message));
                        }
                    }
                }
                finally
                {
                    // Only delete our dedicated scratch; never traverse a substituted reparse point.
                    if (Directory.Exists(scratch))
                    {
                        SafeRecursiveTraversal.EnsureNoReparsePoints(scratch);
                        Directory.Delete(scratch, recursive: true);
                    }
                }
                return;
            }

            paths = info.Entries.Take(MaxFolderFiles).Select(entry => entry.Key).ToArray();
        }

        var declared = isDirectory ? ReadFolderGameHint(source) : ReadArchiveGameHint(source);
        var context = Path.GetRelativePath(sourceRoot, source);
        if (Path.IsPathRooted(context) || context.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            context = Path.GetFileName(source);
        if (!string.IsNullOrWhiteSpace(gameContextHint))
            context = gameContextHint + " " + context;
        var detection = GameModIntakeClassifier.Classify(games, context, paths, declared);
        if (manualGameId is not null && detection.Kind == GameModIntakeKind.Identified)
            throw new InvalidDataException(
                "This package already has a supported game identity; rescan instead of overriding strong evidence.");
        if (manualGameId is null && detection.Kind != GameModIntakeKind.Identified)
        {
            output.Add(new(reportSource, null, GameModIntakeStatus.NeedsReview, detection.Reason));
            return;
        }
        var resolvedGameId = manualGameId ?? detection.GameId!;
        var owner = games.Single(game => game.Id.Equals(resolvedGameId, StringComparison.OrdinalIgnoreCase));
        var library = GameModStorage.Resolve(managerHome, owner);
        GameModStorage.EnsureDirectories(library);
        RefuseReparseComponents(library.InboxRoot);
        var wasCreated = isDirectory
            ? await CopyDirectoryIntoInboxAsync(source, library.InboxRoot, owner.Id, receipts, budget, ct).ConfigureAwait(false)
            : await CopyArchiveIntoInboxAsync(source, library.InboxRoot, owner.Id, receipts, budget, ct, copyProgress).ConfigureAwait(false);
        output.Add(new(reportSource, owner.Id,
            wasCreated ? GameModIntakeStatus.Routed : GameModIntakeStatus.AlreadyPresent,
            wasCreated
                ? manualGameId is null ? detection.Reason
                    : "Manually assigned to the selected registered game after explicit review; original source preserved."
                : "Identical mod already exists in this game's Inbox or processed receipts."));
    }

    private async Task<bool> CopyArchiveIntoInboxAsync(string source, string inboxRoot, string gameId,
        HashSet<string> receipts, IntakeBudget budget, CancellationToken ct,
        Action<long, long>? copyProgress = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var inputBytes = new FileInfo(source).Length;
        if (inputBytes > MaxArchiveBytes)
            throw new InvalidDataException("Archive exceeds the automatic intake 8 GiB copy limit.");
        if (inputBytes > budget.RemainingActualBytes)
            throw new InvalidDataException(CollectionCopyLimitError);
        var scratch = CreateScratchPath();
        try
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                IoBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(scratch, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                IoBufferSize, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[IoBufferSize];
                int count;
                long total = 0;
                long nextProgress = 0;
                while ((count = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > MaxArchiveBytes) throw new InvalidDataException("Archive grew beyond the intake copy budget.");
                    budget.AccountActual(count);
                    digest.AppendData(buffer.AsSpan(0, count));
                    await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                    // Report copied bytes, not just archive count. Synchronous progress
                    // callbacks may cancel; never publish a partially copied package.
                    if (copyProgress is not null && (total >= nextProgress || total == inputBytes))
                    {
                        copyProgress(total, inputBytes);
                        ct.ThrowIfCancellationRequested();
                        nextProgress = total + 4L * 1024 * 1024;
                    }
                }
                await output.FlushAsync(ct).ConfigureAwait(false);
            }
            var sha = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            var receiptKey = ReceiptKey(gameId, sha, isDirectory: false);
            if (receipts.Contains(receiptKey)) return false;
            // A second manager process may have published a receipt since this import
            // loaded its initial snapshot. Serialize only final publication, not I/O.
            await using var receiptLease = await AcquireReceiptWriterLockAsync(ct).ConfigureAwait(false);
            var durableReceipts = LoadReceipts();
            if (durableReceipts.Contains(receiptKey))
            {
                receipts.UnionWith(durableReceipts);
                return false;
            }
            var filename = SafeDisplayStem(source) + "-" + sha + Path.GetExtension(source).ToLowerInvariant();
            var destination = Path.Combine(inboxRoot, filename);
            RefuseReparseComponents(inboxRoot);
            if (File.Exists(destination))
            {
                RefuseReparseComponents(destination);
                await using var existing = File.OpenRead(destination);
                var existingSha = Convert.ToHexString(await SHA256.HashDataAsync(existing, ct).ConfigureAwait(false));
                if (!existingSha.Equals(sha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("An existing auto-sorted package is corrupt; it was not overwritten.");
                RememberReceipt(receipts, durableReceipts, receiptKey);
                return false;
            }
            File.Move(scratch, destination);
            RememberReceipt(receipts, durableReceipts, receiptKey);
            return true;
        }
        finally
        {
            if (File.Exists(scratch)) File.Delete(scratch);
        }
    }

    private async Task<bool> CopyDirectoryIntoInboxAsync(string source, string inboxRoot, string gameId,
        HashSet<string> receipts, IntakeBudget budget, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var files = SnapshotFolderFiles(source, ct);
        // Reject a too-large candidate before allocating scratch or spending the
        // shared budget, so a smaller unrelated sibling can still be imported.
        long plannedBytes = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            RefuseReparseComponents(file);
            var bytes = new FileInfo(file).Length;
            if (bytes < 0 || bytes > budget.RemainingActualBytes - plannedBytes)
                throw new InvalidDataException(CollectionCopyLimitError);
            plannedBytes += bytes;
        }
        var scratch = CreateScratchPath();
        Directory.CreateDirectory(scratch);
        try
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                RefuseReparseComponents(file);
                var relative = Path.GetRelativePath(source, file);
                digest.AppendData(Encoding.UTF8.GetBytes(relative.Replace('\\', '/').ToLowerInvariant() + "\n"));
                var target = Path.Combine(scratch, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                    IoBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    IoBufferSize, FileOptions.Asynchronous);
                var buffer = new byte[IoBufferSize];
                int count;
                while ((count = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > MaxArchiveBytes) throw new InvalidDataException("Folder exceeds the 8 GiB intake copy budget.");
                    budget.AccountActual(count);
                    digest.AppendData(buffer.AsSpan(0, count));
                    await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                }
            }
            var sha = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            var receiptKey = ReceiptKey(gameId, sha, isDirectory: true);
            if (receipts.Contains(receiptKey)) return false;
            // A second manager process may have published a receipt since this import
            // loaded its initial snapshot. Serialize only final publication, not I/O.
            await using var receiptLease = await AcquireReceiptWriterLockAsync(ct).ConfigureAwait(false);
            var durableReceipts = LoadReceipts();
            if (durableReceipts.Contains(receiptKey))
            {
                receipts.UnionWith(durableReceipts);
                return false;
            }
            var destination = Path.Combine(inboxRoot, SafeDisplayStem(source) + "-" + sha);
            RefuseReparseComponents(inboxRoot);
            if (Directory.Exists(destination))
            {
                RefuseReparseComponents(destination);
                var other = await HashDirectoryAsync(destination, ct).ConfigureAwait(false);
                if (!other.Equals(sha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("An existing auto-sorted folder is corrupt; it was not overwritten.");
                RememberReceipt(receipts, durableReceipts, receiptKey);
                return false;
            }
            Directory.Move(scratch, destination);
            RememberReceipt(receipts, durableReceipts, receiptKey);
            return true;
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    private async Task<string> HashDirectoryAsync(string directory, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in SnapshotFolderFiles(directory, ct))
        {
            var relative = Path.GetRelativePath(directory, file);
            digest.AppendData(Encoding.UTF8.GetBytes(relative.Replace('\\', '/').ToLowerInvariant() + "\n"));
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, IoBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[IoBufferSize];
            int count;
            while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                digest.AppendData(buffer.AsSpan(0, count));
        }
        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }

    private string[] SnapshotFolderFiles(string folder, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var files = new List<string>();
        var queue = new Queue<(string Folder, int Depth)>();
        var visitedEntries = 0;
        queue.Enqueue((folder, 0));
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (current, depth) = queue.Dequeue();
            RefuseReparseComponents(current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                ct.ThrowIfCancellationRequested();
                // Count each enumerated file or directory once, including empty
                // directories removed from the queue on earlier iterations.
                // Queue size + file count alone does not bound total traversal.
                if (++visitedEntries > inventoryEntryBudget)
                    throw new InvalidDataException($"Mod intake exceeded its {inventoryEntryBudget:N0}-file-and-folder safety budget; import a smaller collection.");
                RefuseReparseComponents(entry);
                if (Directory.Exists(entry))
                {
                    if (depth >= MaxContainerDepth)
                        throw new InvalidDataException("Mod intake exceeded the safe 64-directory-level traversal limit; use a shallower selected collection.");
                    queue.Enqueue((entry, depth + 1));
                }
                else if (File.Exists(entry)) files.Add(entry);
            }
        }
        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsSupportedArchive(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".rar" or ".7z";
    }

    private static string SafePackageName(string source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var name = Directory.Exists(source) ? Path.GetFileName(source) : Path.GetFileNameWithoutExtension(source);
        var slug = GameProfile.NormalizeId(name);
        return slug[..Math.Min(slug.Length, 48)];
    }

    // Retain the original human title separately from SHA-based receipt identity.
    // Keep the original letter case and readable spaces; the suffix still makes
    // archived downloads unique. Unsafe or excessive names use the safe slug.
    private static string SafeDisplayStem(string source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var name = Directory.Exists(source)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(source))
            : Path.GetFileNameWithoutExtension(source);
        name = name.Length > 80 ? name[..80].TrimEnd(' ', '.') : name;
        return PathRules.IsSafeArchiveRelativePath(name) ? name : SafePackageName(source);
    }

    private string CreateScratchPath()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var root = Path.Combine(stateRoot, "ModIntakeScratch");
        RefuseReparseComponents(stateRoot);
        if (Directory.Exists(root)) RefuseReparseComponents(root);
        Directory.CreateDirectory(root);
        return Path.Combine(root, "intake-" + Guid.NewGuid().ToString("N"));
    }

    private static string? ReadFolderGameHint(string folder)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        string? declared = null;
        // Match archive intake: all recognized game declarations must agree.
        // Returning the first file permits a later conflicting manifest to
        // silently route an unpacked mod to the wrong registered game.
        foreach (var name in new[] { "mod-intake.json", "mod.json", "manifest.json", "meta.json" })
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path)) continue;
            RefuseReparseComponents(path);
            var appOwned = name.Equals("mod-intake.json", StringComparison.OrdinalIgnoreCase);
            var maxBytes = appOwned ? MaxGameManifestBytes : MaxThirdPartyManifestBytes;
            if (new FileInfo(path).Length > maxBytes)
                throw new InvalidDataException(appOwned
                    ? "App-owned game manifest exceeds the 16 KiB metadata budget; manual review required."
                    : "Third-party game metadata exceeds the bounded 256 KiB inspection limit; manual review required.");
            using var input = File.OpenRead(path);
            var hint = ReadBoundedManifestGame(input, maxBytes);
            if (string.IsNullOrWhiteSpace(hint)) continue;
            if (declared is not null &&
                !GameProfile.NormalizeId(hint).Equals(GameProfile.NormalizeId(declared),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Folder declares conflicting target games; manual review required.");
            declared = hint;
        }
        return declared;
    }

    private static string? ReadArchiveGameHint(string archive)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return null;
        using var zip = ZipFile.OpenRead(archive);
        // The archive has already passed the bounded ArchiveInspector scan.
        // Do not silently ignore game declarations after the first 1,024
        // entries: large mod packages can otherwise be routed by filename
        // despite having explicit (or contradictory) game metadata later.
        if (zip.Entries.Count > MaxEntriesPerSource)
            throw new InvalidDataException("ZIP game metadata exceeds the 50,000-entry intake limit.");
        string? declared = null;
        long inspectedMetadataBytes = 0;
        foreach (var entry in zip.Entries)
        {
            var filename = Path.GetFileName(entry.FullName.Replace('\\', '/'));
            if (filename is null || !new[] { "mod-intake.json", "mod.json", "manifest.json", "meta.json" }
                .Contains(filename, StringComparer.OrdinalIgnoreCase)) continue;
            var appOwned = filename.Equals("mod-intake.json", StringComparison.OrdinalIgnoreCase);
            var maxBytes = appOwned ? MaxGameManifestBytes : MaxThirdPartyManifestBytes;
            if (entry.Length < 0 || entry.CompressedLength < 0 ||
                entry.Length > maxBytes || entry.CompressedLength > maxBytes)
                throw new InvalidDataException(appOwned
                    ? "App-owned game manifest exceeds the 16 KiB metadata budget; manual review required."
                    : "Third-party game metadata exceeds the bounded 256 KiB inspection limit; manual review required.");
            if (entry.Length > MaxArchiveManifestScanBytes - inspectedMetadataBytes)
                throw new InvalidDataException("Archive game metadata exceeds the cumulative 4 MiB inspection limit; manual review required.");
            inspectedMetadataBytes += entry.Length;
            using var input = entry.Open();
            var hint = ReadBoundedManifestGame(input, maxBytes);
            if (string.IsNullOrWhiteSpace(hint)) continue;
            if (declared is not null &&
                !GameProfile.NormalizeId(hint).Equals(GameProfile.NormalizeId(declared),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Archive declares conflicting target games; manual review required.");
            declared = hint;
        }
        return declared;
    }

    private static string? ReadBoundedManifestGame(Stream input, int maxBytes)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // Advertised sizes in untrusted compressed headers are not an actual-output limit.
        // The same decoded-content ceiling applies even if an archive underreports its sizes.
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1024, leaveOpen: true);
        var buffer = new char[maxBytes + 1];
        var length = reader.ReadBlock(buffer, 0, buffer.Length);
        if (length > maxBytes)
            throw new InvalidDataException("Game metadata exceeds its bounded decoded-content inspection limit; manual review required.");
        return TryReadJsonGame(new string(buffer, 0, length));
    }

    private static string? TryReadJsonGame(string text)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!new[] { "gameId", "nexusGameDomain", "gameDomain", "game" }
                    .Contains(property.Name, StringComparer.OrdinalIgnoreCase)) continue;
                if (property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    private string ValidateSource(string sourceFolder)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        var full = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var volume = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(volume) || full.Equals(volume.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a dedicated downloads/mods folder, not a drive root.");
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Auto-sort source folder does not exist: " + full);
        if (ContainsPath(managerHome, full) || ContainsPath(full, managerHome))
        {
            // The manager root itself, its ancestors, and generated mod folders must not
            // become scan sources; this prevents re-import loops and accidental whole-tree scans.
            throw new InvalidDataException("Auto-sort sources cannot contain or be inside the manager's own library.");
        }
        foreach (var game in loadGames())
            if (ContainsPath(game.GameRoot, full) || ContainsPath(full, game.GameRoot))
                throw new InvalidDataException("Cannot auto-sort from or across a live game installation directory.");
        RefuseReparseComponents(full);
        return full;
    }

    private static bool ContainsPath(string root, string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var canonical = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return canonical.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase)
            || canonical.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static void RefuseReparseComponents(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? throw new InvalidDataException("Path has no filesystem root.");
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Auto-sort refuses linked/junction source or destination paths.");
        }
    }

    private HashSet<string> LoadReceipts()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var path = Path.Combine(stateRoot, "mod-intake-receipts.json");
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RefuseReparseComponents(path);
        // Readers may overlap the atomic same-directory rename on another process.
        // Share-delete lets them finish reading the old complete file snapshot.
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var entries = JsonSerializer.Deserialize<string[]>(reader)
            ?? throw new InvalidDataException("Mod intake receipt index was empty.");
        if (entries.Length > 50000 || entries.Any(entry => entry.Length != 64 || entry.Any(ch => !Uri.IsHexDigit(ch))))
            throw new InvalidDataException("Mod intake receipt index is malformed or exceeds its supported limit.");
        return new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase);
    }

    private static string ReceiptKey(string gameId, string sha, bool isDirectory)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var token = gameId + "|" + (isDirectory ? "folder" : "archive") + "|" + sha;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    /// <summary>
    /// The caller holds the permanent Windows file-share lease across receipt
    /// reread, Inbox publication and durable index replacement. The lease file
    /// must not be deleted: unlink/recreate would allow two independent owners.
    /// </summary>
    private async Task<FileStream> AcquireReceiptWriterLockAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        RefuseReparseComponents(stateRoot);
        Directory.CreateDirectory(stateRoot);
        var lockPath = Path.Combine(stateRoot, "mod-intake-receipts.lock");
        for (var retry = 0; ; retry++)
        {
            ct.ThrowIfCancellationRequested();
            RefuseReparseComponents(lockPath);
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            }
            catch (IOException ex) when (
                ((ex.HResult & 0xFFFF) == 32 || (ex.HResult & 0xFFFF) == 33) && retry < 120)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }
    }

    private void RememberReceipt(HashSet<string> receipts, HashSet<string> durableReceipts, string key)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (durableReceipts.Add(key))
        {
            if (durableReceipts.Count > 50_000)
                throw new InvalidDataException("Mod intake receipt index exceeds its 50,000-entry safety limit.");
            var path = Path.Combine(stateRoot, "mod-intake-receipts.json");
            RefuseReparseComponents(path);
            SaveAtomic(path, JsonSerializer.Serialize(
                durableReceipts.Order(StringComparer.OrdinalIgnoreCase).ToArray()));
        }
        receipts.UnionWith(durableReceipts);
    }

    private void SaveSources(IReadOnlyList<string> sources)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(stateRoot);
        SaveAtomic(settingsPath, JsonSerializer.Serialize(sources, PrettyJson));
    }

    private static void SaveAtomic(string path, string contents)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var temp = path + ".intake-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
