using MhwModManager.Core;

namespace MhwModManager.Updater;

public static class UpdatePathSafety
{
    private static readonly HashSet<string> ReservedRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mods", "State", "Inbox", "Mods Archive", "Games", "Support Bundles", "BuildLogs"
    };
    private static readonly HashSet<string> ReservedDeviceNames = BuildReservedDeviceNames();

    public static string NormalizeRelativeFilePath(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}");
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Empty updater path.");
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':') || Path.IsPathRooted(normalized))
            throw new InvalidDataException($"Rooted, drive-qualified, device, or ADS updater path rejected: {path}");
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) throw new InvalidDataException($"Empty updater path rejected: {path}");
        foreach (var segment in segments)
        {
            if (segment is "." or "..") throw new InvalidDataException($"Traversal updater path rejected: {path}");
            if (segment.EndsWith(' ') || segment.EndsWith('.'))
                throw new InvalidDataException($"Ambiguous Windows updater path rejected: {path}");
            var stem = segment.Split('.')[0];
            if (ReservedDeviceNames.Contains(stem))
                throw new InvalidDataException($"Windows device-name updater path rejected: {path}");
        }
        return string.Join('/', segments);
    }

    public static void RejectUserDataPath(string normalizedPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={normalizedPath}");
        var first = normalizedPath.Split('/')[0];
        if (ReservedRoots.Contains(first))
            throw new InvalidDataException($"Updater payload may not own user/runtime root '{first}'.");
        if (string.Equals(normalizedPath, "MHW-DEBUG-ALL.log", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Updater payload may not own the mutable master debug log.");
    }

    public static string CombineUnderRoot(string root, string relativePath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={root}; relative={relativePath}");
        var normalized = NormalizeRelativeFilePath(relativePath);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Updater path escapes trusted root: {relativePath}");
        return combined;
    }

    public static void EnsureExistingComponentsNotReparse(string root, string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={root}; path={path}");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullRoot) && (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Updater trusted root is a reparse point: {fullRoot}");
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new IOException($"Updater path is outside trusted root: {fullPath}");
        var current = fullRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrEmpty(segment) || segment == ".") continue;
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current)) break;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Updater path traverses a reparse point: {current}");
        }
    }
    public static void CreateDirectorySafely(string root, string directory)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={root}; directory={directory}");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(directory);
        if (!target.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(target, fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Updater directory escapes trusted root: {target}");
        if (!Directory.Exists(fullRoot)) Directory.CreateDirectory(fullRoot);
        if ((File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Updater trusted root is a reparse point: {fullRoot}");
        var relative = Path.GetRelativePath(fullRoot, target);
        var current = fullRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrEmpty(segment) || segment == ".") continue;
            current = Path.Combine(current, segment);
            if (File.Exists(current)) throw new IOException($"Updater directory path collides with a file: {current}");
            if (Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Updater directory path traverses a reparse point: {current}");
                continue;
            }
            EnsureExistingComponentsNotReparse(fullRoot, Path.GetDirectoryName(current) ?? fullRoot);
            Directory.CreateDirectory(current);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Created updater directory unexpectedly became a reparse point: {current}");
        }
    }

    private static HashSet<string> BuildReservedDeviceNames()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL" };
        for (var i = 1; i <= 9; i++)
        {
            names.Add($"COM{i}");
            names.Add($"LPT{i}");
        }
        return names;
    }

    public static bool IsDevelopmentLayout(string installRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"installRoot={installRoot}");
        try
        {
            var versionDir = new DirectoryInfo(Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar));
            var releaseDir = versionDir.Parent;
            var projectRoot = releaseDir?.Parent;
            return releaseDir is not null && projectRoot is not null
                && string.Equals(releaseDir.Name, "release", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(projectRoot.FullName, "Build.bat"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }
}
