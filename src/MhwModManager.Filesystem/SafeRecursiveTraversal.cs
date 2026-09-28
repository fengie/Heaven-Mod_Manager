using MhwModManager.Core;

namespace MhwModManager.Filesystem;

public static class SafeRecursiveTraversal
{
    public static (IReadOnlyList<string> Directories, IReadOnlyList<string> Files) Snapshot(
        string root,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={root}");
        var fullRoot = EnsureRootIsNotReparse(root);

        var directories = new List<string>();
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(fullRoot);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();
            var childDirectories = new List<string>();
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         current, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Recursive source traversal rejected reparse point: {entry}");

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(entry);
                    childDirectories.Add(entry);
                }
                else
                {
                    files.Add(entry);
                }
            }

            for (var i = childDirectories.Count - 1; i >= 0; i--)
                pending.Push(childDirectories[i]);
        }

        return (directories, files);
    }

    public static string EnsureRootIsNotReparse(string root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={root}");
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Recursive source root is missing: {root}");

        var fullRoot = Path.GetFullPath(root);
        if ((File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Recursive source traversal rejected reparse point: {fullRoot}");
        return fullRoot;
    }
}
