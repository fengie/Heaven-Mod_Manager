namespace MhwModManager.Core;

public sealed class AutoModBuildSandbox
{
    private readonly object _gate = new();
    private readonly string _root;
    private readonly string _rootWithSeparator;
    private readonly AutoModBuildLimits _limits;
    private readonly HashSet<string> _reservedPaths = new(PathRules.Comparer);
    private long _reservedBytes;

    public AutoModBuildSandbox(string root, AutoModBuildLimits? limits = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _root = Path.GetFullPath(root);
        _rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : string.Concat(_root, Path.DirectorySeparatorChar);
        _limits = limits ?? new AutoModBuildLimits();

        if (_limits.MaxFiles <= 0 || _limits.MaxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits), "Auto Mod build limits must be positive.");

        Directory.CreateDirectory(_root);
    }

    public string ResolveOutputPath(string relativePath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = PathRules.Normalize(relativePath);
        var platformRelative = normalized.Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_root, platformRelative));

        if (!fullPath.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Auto Mod output escaped the build sandbox.");

        return fullPath;
    }

    public async Task<string> WriteAsync(
        string relativePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = PathRules.Normalize(relativePath);
        var fullPath = ResolveOutputPath(normalized);
        Reserve(normalized, content.Length);

        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Auto Mod output path has no parent directory.");
        Directory.CreateDirectory(parent);

        var temporaryPath = string.Concat(fullPath, ".automod-tmp-", Guid.NewGuid().ToString("N"));
        var published = false;

        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content.ToArray(), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: false);
            published = true;
            return normalized;
        }
        finally
        {
            if (!published)
            {
                TryDelete(temporaryPath);
                ReleaseReservation(normalized, content.Length);
            }
        }
    }

    private void Reserve(string normalizedPath, int byteCount)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        lock (_gate)
        {
            if (_reservedPaths.Contains(normalizedPath))
                throw new InvalidOperationException("Auto Mod build attempted to write the same output path twice.");

            if (_reservedPaths.Count >= _limits.MaxFiles)
                throw new InvalidOperationException("Auto Mod build exceeded the configured output file-count budget.");

            var nextBytes = checked(_reservedBytes + byteCount);
            if (nextBytes > _limits.MaxBytes)
                throw new InvalidOperationException("Auto Mod build exceeded the configured output byte budget.");

            _reservedPaths.Add(normalizedPath);
            _reservedBytes = nextBytes;
        }
    }

    private void ReleaseReservation(string normalizedPath, int byteCount)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        lock (_gate)
        {
            if (_reservedPaths.Remove(normalizedPath))
                _reservedBytes -= byteCount;
        }
    }

    private static void TryDelete(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
