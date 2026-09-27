using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class UpdateDiffService(ManagerDatabase db)
{
    public async Task<UpdateDiffSummary> CompareAsync(string olderModId, string newerModId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"older={olderModId}; newer={newerModId}");
        var oldFiles = await LoadAsync(olderModId, ct);
        var newFiles = await LoadAsync(newerModId, ct);
        var added = newFiles.Keys.Except(oldFiles.Keys, PathRules.Comparer).ToArray();
        var removed = oldFiles.Keys.Except(newFiles.Keys, PathRules.Comparer).ToArray();
        var shared = oldFiles.Keys.Intersect(newFiles.Keys, PathRules.Comparer).ToArray();
        var changed = shared.Where(p => !StringComparer.OrdinalIgnoreCase.Equals(oldFiles[p].sha, newFiles[p].sha)).ToArray();
        var unchanged = shared.Length - changed.Length;
        var structural = changed.Count(p => newFiles[p].cls == FileClass.Structural || oldFiles[p].cls == FileClass.Structural);
        var texture = changed.Count(p => newFiles[p].cls == FileClass.Texture || oldFiles[p].cls == FileClass.Texture);
        return new(olderModId, newerModId, added.Length, removed.Length, changed.Length, unchanged, structural, texture, changed.Order(PathRules.Comparer).Take(200).ToArray());
    }

    private async Task<Dictionary<string, (string sha, FileClass cls)>> LoadAsync(string modId, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var map = new Dictionary<string, (string, FileClass)>(PathRules.Comparer);
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT path,blob_sha256,file_class FROM mod_files WHERE mod_id=$m";
        cmd.Parameters.AddWithValue("$m", modId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) map[r.GetString(0)] = (r.GetString(1), Enum.Parse<FileClass>(r.GetString(2), true));
        return map;
    }
}
