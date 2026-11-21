using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

public sealed partial class CatalogService
{
    private static string LocalId(string source)
    {
        using var trace = MasterDebugLog.BeginMethod();
        return "local-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToLowerInvariant())).AsSpan(0,12)).ToLowerInvariant();
    }

    private async Task<bool> DiscoverFamilyAsync(string root, int priority, CancellationToken ct, string? enclosingShelf=null)
    {
        using var trace = MasterDebugLog.BeginMethod();
        var parts = await Task.Run(() => AuthoredPackageFamily.Read(root,scanner.Profile,ct),ct);
        if (parts.Count < 2) return false;
        await using var connection = await db.OpenAsync(ct);
        using var tx = connection.BeginTransaction(deferred:false);
        await using var cmd = connection.CreateCommand();
        cmd.Transaction=tx;
        var family = "authored:" + LocalId(root);
        var title = ModDisplayNames.StripTransportDigest(NexusArchiveIdentity.TryParse(Path.GetFileName(root),out var identity)
            ? identity.DisplayName : Path.GetFileName(root));
        cmd.Parameters.AddWithValue("$family",family);
        cmd.Parameters.AddWithValue("$root",root);
        cmd.Parameters.AddWithValue("$shelf",enclosingShelf ?? root);
        cmd.Parameters.AddWithValue("$title",title);
        cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));
        cmd.CommandText="SELECT id,enabled,family_id FROM mods WHERE source_path=$root COLLATE NOCASE";
        string? original = null;
        bool protectedOriginal = false;
        await using (var read=await cmd.ExecuteReaderAsync(ct))
            if (await read.ReadAsync(ct))
            {
                original=read.GetString(0);
                protectedOriginal=read.GetBoolean(1);
                if (!read.IsDBNull(2) && read.GetString(2)!=family) return false;
            }
        if (original is not null && !parts.Any(part=>part.Role=="Main"))
        {
            cmd.CommandText=@"SELECT EXISTS(SELECT 1 FROM profile_mods WHERE mod_id=$id UNION ALL
                SELECT 1 FROM deployment_manifest WHERE provider_mod_id=$id UNION ALL
                SELECT 1 FROM conflict_rules WHERE $id IN (left_mod_id,right_mod_id,winner_mod_id))";
            cmd.Parameters.AddWithValue("$id",original);
            if (protectedOriginal || await cmd.ExecuteScalarAsync(ct) is 1L) return false;
            cmd.CommandText=@"INSERT INTO managed_shelf_entries(mod_id,shelf_path,role)
                VALUES($id,$shelf,'wrapper') ON CONFLICT(mod_id) DO UPDATE SET role='wrapper',shelf_path=$shelf,canonical_id=NULL";
            await cmd.ExecuteNonQueryAsync(ct);
            cmd.Parameters.RemoveAt("$id");
        }
        cmd.CommandText="INSERT OR IGNORE INTO mod_families(id,name,created_at) VALUES($family,$title,$now)";
        await cmd.ExecuteNonQueryAsync(ct);
        var members = new List<(string Id,string Role)>();
        foreach (var part in parts)
        {
            var path = part.Files is null ? part.SourceDirectory : Path.Combine(part.SourceDirectory,
                part.Files.First(file=>Path.GetExtension(file).Equals(".pak",StringComparison.OrdinalIgnoreCase)));
            if (part.Role=="Main" && original is not null) path=root;
            cmd.Parameters.AddWithValue("$path",path);
            cmd.CommandText="SELECT id,family_id FROM mods WHERE source_path=$path COLLATE NOCASE";
            string id = LocalId(path);
            await using (var read=await cmd.ExecuteReaderAsync(ct))
                if(await read.ReadAsync(ct))
                {
                    id=read.GetString(0);
                    if (!read.IsDBNull(1) && read.GetString(1)!=family) return false;
                }
            cmd.Parameters.AddWithValue("$id",id);
            cmd.Parameters.AddWithValue("$part",part.Label);
            cmd.Parameters.AddWithValue("$priority",priority++);
            cmd.Parameters.AddWithValue("$dir",part.SourceDirectory);
            cmd.Parameters.AddWithValue("$files",part.Files is null ? DBNull.Value : JsonSerializer.Serialize(part.Files));
            cmd.Parameters.AddWithValue("$role",part.Role);
            cmd.Parameters.AddWithValue("$choice",(object?)part.ChoiceGroup??DBNull.Value);
            cmd.CommandText="""
                INSERT INTO mods(id,name,display_name,source_path,priority,family_id,imported_at,updated_at)
                VALUES($id,$part,$part,$path,$priority,$family,$now,$now)
                ON CONFLICT(id) DO UPDATE SET family_id=$family WHERE mods.family_id IS NOT $family;
                INSERT INTO mod_family_members(family_id,mod_id,role,choice_group) VALUES($family,$id,$role,$choice)
                ON CONFLICT(family_id,mod_id) DO UPDATE SET role=$role,choice_group=$choice WHERE role IS NOT $role OR choice_group IS NOT $choice;
                INSERT INTO authored_component_sources(mod_id,package_root,source_directory,label,files_json) VALUES($id,$root,$dir,$part,$files)
                ON CONFLICT(mod_id) DO UPDATE SET source_directory=$dir,label=$part,files_json=$files,verified=1 WHERE label IS NOT $part OR source_directory IS NOT $dir OR files_json IS NOT $files OR verified=0;
                DELETE FROM managed_shelf_entries WHERE mod_id=$id;
                """;
            await cmd.ExecuteNonQueryAsync(ct);
            members.Add((id,part.Role));
            foreach(var key in new[]{"$path","$id","$part","$priority","$dir","$files","$role","$choice"}) cmd.Parameters.RemoveAt(key);
        }
        var main=members.SingleOrDefault(member=>member.Role=="Main");
        if (main.Id is not null)
            foreach(var optional in members.Where(member=>member.Role=="Optional"))
            {
                cmd.CommandText="""
                    INSERT OR IGNORE INTO conflict_rules(id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,reason,explicit,created_at)
                    VALUES($rule,'Overlay','ModPair',$base,$addon,$addon,'Authored optional overlay of the main package',0,$now)
                    """;
                cmd.Parameters.AddWithValue("$rule",family+":"+optional.Id);
                cmd.Parameters.AddWithValue("$base",main.Id);
                cmd.Parameters.AddWithValue("$addon",optional.Id);
                await cmd.ExecuteNonQueryAsync(ct);
                foreach(var key in new[]{"$rule","$base","$addon"}) cmd.Parameters.RemoveAt(key);
            }
        cmd.CommandText="UPDATE authored_component_sources SET verified=0 WHERE verified=1 AND package_root=$root AND mod_id NOT IN (SELECT value FROM json_each($current))";
        cmd.Parameters.AddWithValue("$current", JsonSerializer.Serialize(members.Select(member=>member.Id)));
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
