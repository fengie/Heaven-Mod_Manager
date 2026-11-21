using MhwModManager.Core;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Diagnostics;
using System.Text;

namespace MhwModManager.Filesystem;

public sealed record LocalModLocation(string Game, string Name, string Evidence, string Location)
{
    public string? VerifiedWorkshopAppId { get; init; }
    public string? VerifiedWorkshopTitle { get; init; }
    public string? InstallationRoot { get; init; }
    public string? PreviousProfileId { get; init; }
    public string? FileEvidence { get; init; }
    public string LibraryStatus { get; init; } = "Evidence only";
    public string? RegisteredProfileId { get; init; }
}

public record ExternalLibraryReference(string ItemId, string DisplayName, string OwnerLabel, IReadOnlyList<string> Locations, bool IsSteamWorkshop = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceSummary
    {
        get
        {
            using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
            return string.Join(Environment.NewLine, Locations);
        }
    }
}

public sealed record WorkshopLibraryReference(string WorkshopId, IReadOnlyList<string> Sources, string? Title = null)
    : ExternalLibraryReference(WorkshopId, Title ?? $"Workshop item {WorkshopId}", "Installed by Steam Workshop", Sources, IsSteamWorkshop: true);

public static partial class LocalModInventory
{
    public static IReadOnlyList<LocalModLocation> Scan(IReadOnlyList<GameProfile> games,
        IEnumerable<string>? steamLibraries = null, IEnumerable<string>? standaloneLibraries = null, IReadOnlyDictionary<string,string[]>? previousRoots = null, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var rows = new List<LocalModLocation>();
        var discover = steamLibraries is null;
        var libraries = (steamLibraries ?? GameProfileRegistry.SteamLibraries()).ToArray();
        var seenRoots = new HashSet<string>(PathRules.Comparer);
        var workshopGames = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var game in games)
            if (game.SteamAppId is { Length: > 0 } registeredId && registeredId.All(char.IsAsciiDigit))
                workshopGames[registeredId] = game.DisplayName;
        var registered = games.Select(game => Path.GetFullPath(game.GameRoot)).ToHashSet(PathRules.Comparer);
        foreach (var game in games)
        {
            ct.ThrowIfCancellationRequested();
            var roots = new Dictionary<string, string>(PathRules.Comparer)
            {
                [Path.GetFullPath(game.GameRoot)] = "Registered installation"
            };
            foreach (var old in previousRoots?.GetValueOrDefault(game.Id) ?? [])
                if (Path.IsPathFullyQualified(old) && !registered.Contains(old))
                    roots.TryAdd(old, "Previous installation");
            var current = new DirectoryInfo(game.GameRoot);
            if (current.Parent?.Name.Equals("common", StringComparison.OrdinalIgnoreCase) == true &&
                current.Parent.Parent?.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) == true &&
                game.SteamAppId is { Length: > 0 } id && id.All(char.IsAsciiDigit))
            {
                // A registered installation may live on a library absent from the current Steam client configuration.
                var registeredLibrary = current.Parent.Parent.Parent;
                if (registeredLibrary is not null && !libraries.Contains(registeredLibrary.FullName, PathRules.Comparer))
                {
                    var workshop = Path.Combine(current.Parent.Parent.FullName, "workshop", "content", id);
                    if (Directory.Exists(workshop)) ScanWorkshop(game.DisplayName, workshop, rows, ct);
                }
                foreach (var library in libraries)
                {
                    var sibling = Path.Combine(library, "steamapps", "common", current.Name);
                    if (!registered.Contains(sibling))
                        roots.TryAdd(sibling, "Other library folder with matching install name");

                }
            }
            foreach (var (root, origin) in roots)
            {
                if (!seenRoots.Add(root)) continue;
                if (!Directory.Exists(root))
                {
                    if (registered.Contains(root)) rows.Add(new(game.DisplayName, "Installation unavailable", origin, root));
                    continue;
                }
                if (!PathRules.Comparer.Equals(root, game.GameRoot))
                    rows.Add(new(game.DisplayName, Path.GetFileName(root), origin, root));
                ScanGame(game.DisplayName, game.IsMonsterHunterWorld, root, origin, rows, ct,
                    PathRules.Comparer.Equals(root, game.GameRoot) ? game.Id : null,
                    origin == "Previous installation" ? game.Id : null);
            }
        }
        if (discover)
        {
            foreach (var game in GameProfileRegistry.DiscoverInstalledGameCandidates())
            {
                ct.ThrowIfCancellationRequested();
                if (game.SteamAppId is { Length: > 0 } id && id.All(char.IsAsciiDigit))
                    workshopGames.TryAdd(id, game.Name);
                if (seenRoots.Add(game.Root) && Directory.Exists(game.Root))
                    ScanGame(game.Name, false, game.Root, game.Store + " installation", rows, ct);
            }
        }
        foreach (var library in libraries)
        {
            var apps = Path.Combine(library, "steamapps");
            var common = Path.Combine(apps, "common");
            foreach (var other in SafeDirectories(common, rows, ct))
            {
                if (!seenRoots.Add(other.FullName)) continue;
                rows.Add(new(other.Name, "Unmatched game folder", "Steam library · ownership unverified", other.FullName));
                ScanGame(other.Name, false, other.FullName, "Unmatched Steam game folder", rows, ct);
            }
            foreach (var app in SafeDirectories(Path.Combine(apps, "workshop", "content"), rows, ct))
                if (app.Name.All(char.IsAsciiDigit))
                    ScanWorkshop(workshopGames.GetValueOrDefault(app.Name) ?? "Steam app " + app.Name, app.FullName, rows, ct);
        }
        var standalone = standaloneLibraries ?? (discover
            ? games.Select(game => Path.GetPathRoot(game.GameRoot))
                .OfType<string>().Distinct(PathRules.Comparer).Select(drive => Path.Combine(drive, "Games"))
            : []);
        foreach (var library in standalone.Distinct(PathRules.Comparer))
        {
            foreach (var folder in SafeDirectories(library, rows, ct))
            {
                if (seenRoots.Contains(folder.FullName)) continue;
                try
                {
                    if (!folder.EnumerateFiles("*.exe").Any(exe =>
                            !exe.Name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) &&
                            !exe.Name.StartsWith("setup", StringComparison.OrdinalIgnoreCase)) ||
                        !folder.EnumerateDirectories().Any(dir => KnownGameLayout(dir.Name))) continue;
                    seenRoots.Add(folder.FullName);
                    ScanGame(folder.Name, false, folder.FullName, "Standalone game candidate", rows, ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    rows.Add(new(folder.Name, "Standalone scan incomplete", ex.Message, folder.FullName));
                }
            }
        }
        return rows;
    }

    private static bool KnownGameLayout(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return name.Equals("Mods", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Content", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasLinkedAncestor(string root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    private static List<DirectoryInfo> SafeDirectories(string root, List<LocalModLocation> rows, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result = new List<DirectoryInfo>();
        try
        {
            if (!Directory.Exists(root) || HasLinkedAncestor(root)) return result;
            foreach (var child in new DirectoryInfo(root).EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                if ((child.Attributes & FileAttributes.ReparsePoint) == 0) result.Add(child);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            rows.Add(new("Library", "Folder scan incomplete", ex.Message, root));
        }
        return result;
    }

    public static IReadOnlyList<WorkshopLibraryReference> WorkshopLibrary(
        IEnumerable<LocalModLocation> inventory, string? steamAppId, IEnumerable<string> managedSources)
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        if (string.IsNullOrEmpty(steamAppId)) return [];
        var managed = managedSources.ToHashSet(PathRules.Comparer);
        return inventory.Where(row => row.VerifiedWorkshopAppId == steamAppId)
            .GroupBy(row => row.Name, StringComparer.Ordinal)
            .Where(group => !group.Any(row => managed.Contains(row.Location)))
            .Select(group => new WorkshopLibraryReference(group.Key, group.Select(row => row.Location)
                .Distinct(PathRules.Comparer).OrderBy(path => path, PathRules.Comparer).ToArray(),
                ConsensusWorkshopTitle(group)))
            .OrderBy(item => item.ItemId, StringComparer.Ordinal).ToArray();
    }

    private static string? ConsensusWorkshopTitle(IEnumerable<LocalModLocation> locations)
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        var titles = locations.Select(row => row.VerifiedWorkshopTitle).OfType<string>()
            .Distinct(StringComparer.Ordinal).Take(2).ToArray();
        return titles.Length == 1 ? titles[0] : null;
    }

    private static string? ReadWorkshopTitle(DirectoryInfo item)
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        try
        {
            var file = new FileInfo(Path.Combine(item.FullName, "data.json"));
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) return null;
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length is <= 0 or > 16 * 1024) return null;
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) return null;
            ReadOnlyMemory<byte> json = bytes;
            if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble)) json = json[Encoding.UTF8.Preamble.Length..];
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? id = null, folder = null, title = null;
            foreach (var field in root.EnumerateObject())
            {
                switch (field.Name)
                {
                    case "id":
                        if (id is not null || field.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) return null;
                        id = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() : field.Value.GetRawText();
                        break;
                    case "folderName":
                        if (folder is not null || field.Value.ValueKind != JsonValueKind.String) return null;
                        folder = field.Value.GetString();
                        break;
                    case "title":
                        if (title is not null || field.Value.ValueKind != JsonValueKind.String) return null;
                        title = field.Value.GetString()?.Trim();
                        break;
                }
            }
            return id == item.Name && (folder is null || folder == id) && title is { Length: > 0 and <= 256 }
                && !title.Any(character => char.IsControl(character) || char.GetUnicodeCategory(character) is
                    System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.LineSeparator or
                    System.Globalization.UnicodeCategory.ParagraphSeparator) ? title : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void ScanWorkshop(string game, string root, List<LocalModLocation> rows, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            if (HasLinkedAncestor(root)) return;
            var installed = WorkshopInstallRecords(root);
            foreach (var dir in new DirectoryInfo(root).EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                if ((dir.Attributes & FileAttributes.ReparsePoint) == 0 && dir.Name.All(char.IsAsciiDigit))
                {
                    var verified = installed.Contains(dir.Name);
                    rows.Add(new(game, dir.Name, verified
                        ? "Workshop install record and local content directory"
                        : "Workshop content directory · installation record unconfirmed", dir.FullName)
                    {
                        VerifiedWorkshopAppId = verified ? dir.Parent!.Name : null,
                        VerifiedWorkshopTitle = verified ? ReadWorkshopTitle(dir) : null
                    });
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            rows.Add(new(game, "Workshop scan incomplete", ex.Message, root));
        }
    }

    // Steam KeyValues is nested. IDs in WorkshopItemDetails or a quoted value
    // are not installation records. Only direct block keys in WorkshopItemsInstalled count.
    [GeneratedRegex("""//[^\r\n]*|"(?:\\.|[^"\\])*"|[{}]""", RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueTokens();

    private static HashSet<string> WorkshopInstallRecords(string contentRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result = new HashSet<string>(StringComparer.Ordinal);
        var app = new DirectoryInfo(contentRoot);
        var file = new FileInfo(Path.Combine(app.Parent!.Parent!.FullName, $"appworkshop_{app.Name}.acf"));
        if (!file.Exists || file.Length > 4 * 1024 * 1024 || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            return result;
        var depth = 0;
        var recordsDepth = -1;
        string? key = null;
        var matchesApp = false;
        foreach (Match match in KeyValueTokens().Matches(File.ReadAllText(file.FullName)))
        {
            var token = match.Value;
            if (token.StartsWith("//", StringComparison.Ordinal)) continue;
            if (token == "{")
            {
                if (depth == 1 && key == "WorkshopItemsInstalled") recordsDepth = depth + 1;
                else if (depth == recordsDepth && key is { Length: > 0 } && key.All(char.IsAsciiDigit)) result.Add(key);
                depth++;
                key = null;
            }
            else if (token == "}")
            {
                if (depth == recordsDepth) recordsDepth = -1;
                if (--depth < 0) return [];
                key = null;
            }
            else if (key is null) key = token[1..^1];
            else
            {
                if (depth == 1 && key.Equals("appid", StringComparison.OrdinalIgnoreCase))
                    matchesApp = token[1..^1] == app.Name;
                key = null;
            }
        }
        return matchesApp && depth == 0 ? result : [];
    }

    private static void ScanGame(string game, bool mhw, string root, string origin, List<LocalModLocation> rows, CancellationToken ct, string? registeredProfileId = null, string? previousProfileId = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var remaining = 300_000;
        var pending = new Stack<string>();
        pending.Push(root);
        try
        {
            if (Path.GetPathRoot(root) == root || HasLinkedAncestor(root)) return;
            while (pending.TryPop(out var dir))
            {
                try
                {
                    foreach (var item in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (--remaining < 0)
                        {
                            rows.Add(new(game, "Inventory scan limit reached", origin, root));
                            return;
                        }
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (item is DirectoryInfo)
                        {
                            pending.Push(item.FullName);
                            continue;
                        }
                        var relative = "/" + Path.GetRelativePath(root, item.FullName).Replace('\\', '/') + "/";
                        var evidence = Detect(relative, item.FullName, item.Extension, mhw);
                        if (evidence is not null) rows.Add(new(game, item.Name, origin + " · " + evidence, item.FullName) { RegisteredProfileId = registeredProfileId, PreviousProfileId = previousProfileId, InstallationRoot = root, FileEvidence = evidence });
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    rows.Add(new(game, "Folder scan incomplete", origin, dir));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            rows.Add(new(game, "Installation scan incomplete", origin, root));
        }
    }

    private static string? Detect(string path, string fullPath, string extension, bool mhw)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        const StringComparison cmp = StringComparison.OrdinalIgnoreCase;
        if (path.EndsWith("/installation.json/", cmp) && path.Contains("/DLSS-Nexus-Backups/", cmp) &&
            IsCompletedDlssReceipt(fullPath)) return "DLSS Nexus completed installation receipt";
        if (path.EndsWith("/dxgi.dll/", cmp))
        {
            var parent = Path.GetDirectoryName(fullPath)!;
            if (File.Exists(Path.Combine(parent, "ReShade.ini")) &&
                Directory.Exists(Path.Combine(parent, "reshade-shaders", "Shaders")) &&
                FileVersionInfo.GetVersionInfo(fullPath).ProductName?.Equals("ReShade", cmp) == true)
                return "ReShade injector and configuration";
        }
        if (path.EndsWith("/bitfix/sig.lua/", cmp) &&
            File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(fullPath))!, "dsound.dll")))
            return "Paired loader files · runtime use unconfirmed";
        if (extension.Equals(".pak", cmp) && (path.Contains("/Content/Paks/~mods/", cmp) ||
            path.Contains("/Content/Paks/LogicMods/", cmp))) return "Unreal mod container";
        if (extension.Equals(".dll", cmp) && path.Contains("/BepInEx/plugins/", cmp)) return "BepInEx plugin";
        if (extension.Equals(".lua", cmp) && path.Contains("/reframework/autorun/", cmp)) return "REFramework script";
        if (mhw && path.StartsWith("/nativePC/", cmp)) return "nativePC override";
        if (extension.Equals(".pak", cmp) && !path.Contains("/Content/Paks/", cmp) &&
            File.Exists(Path.ChangeExtension(fullPath, ".utoc")) && File.Exists(Path.ChangeExtension(fullPath, ".ucas")))
            return "Loose Unreal companion set outside standard Paks directory · review";
        if ((extension.Equals(".pak", cmp) || extension.Equals(".dll", cmp) || extension.Equals(".lua", cmp)) &&
            path.StartsWith("/Mods/", cmp)) return "Payload in game Mods directory";
        if (path.EndsWith("/mod.json/", cmp) && path.Contains("/Mods/", cmp))
            return "Mod manifest in Mods directory";
        if (path.Contains("/UE4SS/Mods/", cmp) && (path.EndsWith("/Scripts/main.lua/", cmp) ||
            path.EndsWith("/dlls/main.dll/", cmp))) return "UE4SS mod entry point";
        return null;
    }
    private static bool IsCompletedDlssReceipt(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (new FileInfo(path).Length > 1024 * 1024) return false;
        try
        {
            using var stream = File.OpenRead(path);
            using var json = JsonDocument.Parse(stream);
            var root = json.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("product", out var product) && product.ValueKind == JsonValueKind.String &&
                product.GetString() == "DLSS Nexus" &&
                root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String &&
                status.GetString() == "Complete" &&
                root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array &&
                files.GetArrayLength() > 0;
        }
        catch (JsonException) { return false; }
    }

}
