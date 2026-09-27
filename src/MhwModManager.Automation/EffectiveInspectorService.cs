using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class EffectiveInspectorService(ManagerDatabase db)
{
    public async Task<EffectiveFileProvider?> ExplainAsync(string path, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var providers = new List<(string id,string name,string sha)>();
        await using var c = await db.OpenAsync(ct);
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT m.id,m.display_name,mf.blob_sha256 FROM mod_files mf JOIN mods m ON m.id=mf.mod_id WHERE mf.path=$p ORDER BY m.priority";
            cmd.Parameters.AddWithValue("$p", PathRules.Normalize(path));
            await using var r = await cmd.ExecuteReaderAsync(ct); while(await r.ReadAsync(ct))providers.Add((r.GetString(0),r.GetString(1),r.GetString(2)));
        }
        if (providers.Count == 0) return null;
        await using var manifest = c.CreateCommand(); manifest.CommandText="SELECT provider_mod_id,blob_sha256 FROM deployment_manifest WHERE path=$p"; manifest.Parameters.AddWithValue("$p",PathRules.Normalize(path));
        await using var mr = await manifest.ExecuteReaderAsync(ct); string? winner=null,sha=null; if(await mr.ReadAsync(ct)){winner=mr.IsDBNull(0)?null:mr.GetString(0);sha=mr.IsDBNull(1)?null:mr.GetString(1);} 
        var winnerName=providers.FirstOrDefault(x=>StringComparer.OrdinalIgnoreCase.Equals(x.id,winner)).name;
        return new(PathRules.Normalize(path),winner,winnerName,providers.Where(x=>!StringComparer.OrdinalIgnoreCase.Equals(x.id,winner)).Select(x=>x.id).ToArray(),sha);
    }

    public async Task<IReadOnlyList<AssetHeatmapRow>> HeatmapAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var map = new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        await using var c=await db.OpenAsync(ct); await using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT mf.path,m.display_name FROM mod_files mf JOIN mods m ON m.id=mf.mod_id WHERE m.enabled=1";
        await using var r=await cmd.ExecuteReaderAsync(ct); while(await r.ReadAsync(ct)){var key=AssetBundles.KeyForPath(r.GetString(0));if(!map.TryGetValue(key,out var set))map[key]=set=new(StringComparer.OrdinalIgnoreCase);set.Add(r.GetString(1));}
        return map.Where(x=>x.Value.Count>1).Select(x=>new AssetHeatmapRow(x.Key,AssetBundles.DisplayNameForPath(x.Key),x.Value.Count,x.Value.Count>1,x.Value.Order(StringComparer.OrdinalIgnoreCase).ToArray())).OrderByDescending(x=>x.ProviderCount).ThenBy(x=>x.DisplayName,StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
