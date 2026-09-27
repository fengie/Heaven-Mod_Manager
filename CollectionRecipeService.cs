using MhwModManager.Core;
using System.Text.Json;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class CollectionRecipeService(ManagerDatabase db)
{
    public async Task<string> ExportAsync(string destination, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"destination={destination}");
        var mods = await db.GetModsAsync(ct);
        var build = await db.GetGameBuildFingerprintAsync(ct);
        var recipe = new
        {
            format = 1,
            exportedAt = DateTimeOffset.UtcNow,
            gameBuild = build,
            mods = mods.Select(m => new
            {
                m.Id, m.DisplayName, m.Enabled, m.Priority, m.FamilyId, m.Category, m.SourceUrl,
                m.NexusModId, m.NexusFileId, m.NexusModUuid, m.NexusVersionId, m.NexusPreviousVersionId,
                nexusCategory = m.NexusCategory.ToString(), m.NexusVersion, m.NexusUploadedAt, m.IsSuperseded, m.SupersededByModId
            })
        };
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(recipe, AutomationJson.Options), ct);
        return destination;
    }
}
