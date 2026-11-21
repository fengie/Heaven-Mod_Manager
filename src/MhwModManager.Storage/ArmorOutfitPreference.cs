using MhwModManager.Core;

namespace MhwModManager.Storage;

public static class ArmorOutfitPreference
{
    public static async Task<IReadOnlyList<MetadataRowChange>> PrepareAsync(ManagerDatabase database,
        ArmorOutfitSelection selection, PlannerSnapshot snapshot, CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        if (selection.ModId is { } provider)
        {
            var paths = snapshot.Files.Where(f => f.ModId == provider &&
                PathRules.TryGetArmorComponent(f.Path,out var model,out _) &&
                PathRules.Comparer.Equals(model,selection.ModelId)).Select(f => f.Path).ToArray();
            if (paths.Length == 0)
                throw new InvalidOperationException("This outfit no longer supplies files for the selected armor.");

        }

        var rows = await DeploymentMetadata.ReadRowsAsync(database,"armor_outfit_choices",ct);
        var previous = rows.SingleOrDefault(row => PathRules.Comparer.Equals(row.Key["model_id"],selection.ModelId));
        if (previous?.Before?["mod_id"] == selection.ModId) return [];
        Dictionary<string,string?>? next = selection.ModId is null ? null :
            new() { ["model_id"]=selection.ModelId, ["mod_id"]=selection.ModId };
        return [new("armor_outfit_choices", new() { ["model_id"]=selection.ModelId },previous?.Before,next)];
    }
}
