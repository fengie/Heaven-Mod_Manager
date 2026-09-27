using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;
using MhwModManager.Core;

namespace MhwModManager.Storage;

public sealed record MigrationResult(bool Performed,bool Success,string Message,string? ReportPath=null,string? BackupPath=null);

public sealed class LegacyV7Migrator(ManagerDatabase db,string toolRoot,string nextBlobRoot)
{
    private string ModsRoot=>Path.Combine(toolRoot,"Mods");
    private string LegacyRoot=>Path.Combine(toolRoot,"State","V2");
    private string LegacyState=>Path.Combine(LegacyRoot,"state.json");
    private string LegacyBlobs=>Path.Combine(LegacyRoot,"Blobs");

    public async Task<MigrationResult> MigrateIfNeededAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!File.Exists(LegacyState))return new(false,true,"No v7 State\\V2\\state.json was found.");
        if (await IsMigrationCompleteAsync(ct)) return new(false, true, "The verified v7 migration is already complete.");
        // A prior process may have died mid-migration. The legacy state is authoritative
        // until the explicit completion marker exists, so partial imported rows are disposable.
        await ResetIncompleteImportAsync(ct);
        var id=Guid.NewGuid().ToString("N");var stamp=DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);var backup=Path.Combine(toolRoot,"State","NextMigrationBackup",stamp);var report=Path.Combine(backup,"migration-report.md");Directory.CreateDirectory(backup);
        await db.ExecuteAsync("INSERT INTO migration_runs(id,started_at,legacy_root,backup_path,report_path,status) VALUES($id,$u,$l,$b,$r,'Running')",new Dictionary<string,object?>{{"$id",id},{"$u",DateTimeOffset.UtcNow.ToString("O")},{"$l",LegacyRoot},{"$b",backup},{"$r",report}},ct);
        try
        {
            CreatePointInTimeBackup(backup,ct);
            using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(LegacyState,ct));var root=doc.RootElement;
            if(!root.TryGetProperty("schema",out var schema)||schema.GetInt32()!=2)throw new InvalidDataException("Legacy state schema is not v2/v7 compatible.");
            var order = root.TryGetProperty("order", out var orderEl) && orderEl.ValueKind == JsonValueKind.Array
                ? orderEl.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray()
                : [];
            var orderIndex = order.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
            var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("expected", out var expectedForOwnership) && expectedForOwnership.ValueKind == JsonValueKind.Object)
                foreach (var p in expectedForOwnership.EnumerateObject()) expectedPaths.Add(PathRules.Normalize(p.Name));
            var nameToId=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var referencedHashes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var reportLines=new List<string>{"# MHW Manual Mod Manager v8 migration report","",$"Migration id: `{id}`",$"Started: {DateTimeOffset.UtcNow:O}",$"Legacy state: `{LegacyState}`",$"Backup: `{backup}`",""};
            if(root.TryGetProperty("mods",out var modsEl)&&modsEl.ValueKind==JsonValueKind.Object)
            {
                var fallbackPriority=order.Length;foreach(var modProp in modsEl.EnumerateObject()){ct.ThrowIfCancellationRequested();var name=modProp.Name;var modId=LegacyId(name);nameToId[name]=modId;var source=Path.Combine(ModsRoot,name);var display=ShortDisplayName(name);var priority=orderIndex.TryGetValue(name,out var orderPriority)?orderPriority:fallbackPriority++;await db.UpsertModAsync(new(modId,name,display,source,true,priority),ct);var files=new List<ModFileDescriptor>();if(modProp.Value.TryGetProperty("files",out var fEl)&&fEl.ValueKind==JsonValueKind.Object)foreach(var fp in fEl.EnumerateObject()){var key=PathRules.Normalize(fp.Name);var hash=(fp.Value.GetString()??"").ToLowerInvariant();if(hash.Length!=64)throw new InvalidDataException($"Invalid legacy SHA-256 for {name}: {key}");var blob=EnsureLegacyBlob(hash);referencedHashes.Add(hash);var info=new FileInfo(blob);files.Add(new(modId,key,hash,null,info.Length,info.LastWriteTimeUtc,PathRules.ClassifyFile(key)));await RegisterBlob(hash,info.Length,ct);}await db.ReplaceModFilesAsync(modId,files,ct);}
            }
            // Also catalog disabled folders without hashing them. Their files are captured lazily when enabled.
            if(Directory.Exists(ModsRoot))foreach(var dir in Directory.EnumerateDirectories(ModsRoot).Order(StringComparer.OrdinalIgnoreCase)){var name=Path.GetFileName(dir);if(nameToId.ContainsKey(name))continue;var modId=LegacyId(name);nameToId[name]=modId;await db.UpsertModAsync(new(modId,name,ShortDisplayName(name),dir,false,100000+nameToId.Count),ct);}

            if(root.TryGetProperty("bases",out var bases)&&bases.ValueKind==JsonValueKind.Object)foreach(var p in bases.EnumerateObject()){var normalized=PathRules.Normalize(p.Name);if(!expectedPaths.Contains(normalized))continue;string? hash=p.Value.ValueKind==JsonValueKind.String?p.Value.GetString():null;if(string.IsNullOrWhiteSpace(hash))continue;hash=hash.ToLowerInvariant();EnsureLegacyBlob(hash);referencedHashes.Add(hash);await RegisterBlob(hash,new FileInfo(Path.Combine(nextBlobRoot,hash)).Length,ct);await db.ExecuteAsync("INSERT OR REPLACE INTO original_files(path,blob_sha256,captured_at) VALUES($p,$b,$u)",new Dictionary<string,object?>{{"$p",normalized},{"$b",hash},{"$u",DateTimeOffset.UtcNow.ToString("O")}},ct);}

            if(root.TryGetProperty("winners",out var winners)&&winners.ValueKind==JsonValueKind.Object)foreach(var p in winners.EnumerateObject()){var name=p.Value.GetString();if(name is null||!nameToId.TryGetValue(name,out var winnerId))continue;await InsertRule(new(Guid.NewGuid().ToString("N"),RuleKind.ExactWinner,RuleScope.ExactPath,null,null,winnerId,PathRules.Normalize(p.Name),"Migrated v7 exact-file winner",true,DateTimeOffset.UtcNow),ct);}

            if(root.TryGetProperty("relations",out var relations)&&relations.ValueKind==JsonValueKind.Object)foreach(var rp in relations.EnumerateObject()){var v=rp.Value;if(v.ValueKind!=JsonValueKind.Object)continue;var a=v.TryGetProperty("a",out var ae)?ae.GetString():null;var b=v.TryGetProperty("b",out var be)?be.GetString():null;var mode=v.TryGetProperty("mode",out var me)?me.GetString():null;if(a is null||b is null||!nameToId.TryGetValue(a,out var aid)||!nameToId.TryGetValue(b,out var bid))continue;string? wid=null;var winnerName=v.TryGetProperty("winner",out var we)?we.GetString():null;if(winnerName is not null&&nameToId.TryGetValue(winnerName,out var winnerId))wid=winnerId;var kind=mode?.Equals("incompatible",StringComparison.OrdinalIgnoreCase)==true?RuleKind.Incompatible:RuleKind.Overlay;await InsertRule(new(Guid.NewGuid().ToString("N"),kind,RuleScope.ModPair,aid,bid,wid,null,"Migrated v7 mod relationship",true,DateTimeOffset.UtcNow),ct);}

            if(root.TryGetProperty("resourceProviders",out var res)&&res.ValueKind==JsonValueKind.Object)foreach(var p in res.EnumerateObject()){var name=p.Value.GetString();if(name is not null&&nameToId.TryGetValue(name,out var mid))await db.ExecuteAsync("INSERT OR REPLACE INTO resource_providers(namespace,mod_id) VALUES($n,$m)",new Dictionary<string,object?>{{"$n",p.Name.ToLowerInvariant()},{"$m",mid}},ct);}

            if(root.TryGetProperty("expected",out var expected)&&expected.ValueKind==JsonValueKind.Object)foreach(var p in expected.EnumerateObject()){var path=PathRules.Normalize(p.Name);var hash=(p.Value.GetString()??"").ToLowerInvariant();if(hash.Length!=64)continue;EnsureLegacyBlob(hash);referencedHashes.Add(hash);await RegisterBlob(hash,new FileInfo(Path.Combine(nextBlobRoot,hash)).Length,ct);var provider=await InferProviderAsync(path,hash,ct);await db.ExecuteAsync("INSERT OR REPLACE INTO deployment_manifest(path,provider_mod_id,blob_sha256,expected_live_sha256,rule_id,deployed_at) VALUES($p,$m,$b,$b,NULL,$u)",new Dictionary<string,object?>{{"$p",path},{"$m",provider},{"$b",hash},{"$u",DateTimeOffset.UtcNow.ToString("O")}},ct);}

            if(root.TryGetProperty("profiles",out var profiles)&&profiles.ValueKind==JsonValueKind.Object)foreach(var pp in profiles.EnumerateObject()){var profileId=Guid.NewGuid().ToString("N");await db.ExecuteAsync("INSERT INTO profiles(id,name,created_at,updated_at,legacy_json) VALUES($id,$n,$u,$u,$j)",new Dictionary<string,object?>{{"$id",profileId},{"$n",pp.Name},{"$u",DateTimeOffset.UtcNow.ToString("O")},{"$j",pp.Value.GetRawText()}},ct);if(pp.Value.TryGetProperty("order",out var po)&&po.ValueKind==JsonValueKind.Array){var i=0;foreach(var item in po.EnumerateArray()){var name=item.GetString();if(name is not null&&nameToId.TryGetValue(name,out var mid))await db.ExecuteAsync("INSERT OR REPLACE INTO profile_mods(profile_id,mod_id,enabled,priority) VALUES($p,$m,1,$i)",new Dictionary<string,object?>{{"$p",profileId},{"$m",mid},{"$i",i++}},ct);}}}

            await db.RebuildArmorIndexAsync(ct);

            // Validate every referenced content blob with authoritative SHA-256 before declaring success.
            foreach(var hash in referencedHashes){ct.ThrowIfCancellationRequested();await using var stream=File.OpenRead(Path.Combine(nextBlobRoot,hash));var actual=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).ToLowerInvariant();if(!StringComparer.OrdinalIgnoreCase.Equals(actual,hash))throw new InvalidDataException($"Blob verification failed: expected {hash}, got {actual}");}
            var imported=await db.GetModsAsync(ct);
            var enabled=imported.Where(x=>x.Enabled).OrderBy(x=>x.Priority).Select(x=>x.Name).ToArray();
            if(!enabled.SequenceEqual(order,StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Enabled order mismatch. Legacy=[{string.Join(", ",order)}] Imported=[{string.Join(", ",enabled)}]");
            var enabledCount=enabled.Length;
            reportLines.Add($"- Enabled mods and priority order validated: **{enabledCount}**");
            reportLines.Add($"- Total mod folders cataloged: **{imported.Count}**");
            reportLines.Add($"- Referenced blobs verified: **{referencedHashes.Count}**");
            reportLines.Add("- Legacy State\\V2 was not modified.");
            reportLines.Add("- v7 remains available in `legacy-v7` until v8 completes a verified deployment.");
            reportLines.Add("");
            reportLines.Add("Migration result: **PASS**");
            await File.WriteAllLinesAsync(report,reportLines,ct);
            await db.ExecuteAsync("INSERT OR REPLACE INTO schema_info(key,value) VALUES('legacy_migration_complete',$id)",new Dictionary<string,object?>{{"$id",id}},ct);
            await db.ExecuteAsync("UPDATE migration_runs SET status='Complete',completed_at=$u WHERE id=$id",new Dictionary<string,object?>{{"$u",DateTimeOffset.UtcNow.ToString("O")},{"$id",id}},ct);
            return new(true,true,"v7 state migrated and verified.",report,backup);
        }
        catch(Exception ex){try{await File.WriteAllTextAsync(report,$"# Migration FAILED\n\n{ex}\n",CancellationToken.None);await db.ExecuteAsync("UPDATE migration_runs SET status='Failed',completed_at=$u,error=$e WHERE id=$id",new Dictionary<string,object?>{{"$u",DateTimeOffset.UtcNow.ToString("O")},{"$e",ex.ToString()},{"$id",id}},CancellationToken.None);await ResetIncompleteImportAsync(CancellationToken.None);}catch{}return new(true,false,"Migration failed. Partial v8 import rows were discarded; v7 state and nativePC were left untouched.",report,backup);}
    }

    private async Task<bool> IsMigrationCompleteAsync(CancellationToken ct){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await db.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM schema_info WHERE key='legacy_migration_complete'";return await cmd.ExecuteScalarAsync(ct) is string value && !string.IsNullOrWhiteSpace(value);}
    private Task ResetIncompleteImportAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return db.ExecuteAsync("""
DELETE FROM operation_journal; DELETE FROM operations; DELETE FROM profile_rules; DELETE FROM profile_mods; DELETE FROM profiles;
DELETE FROM mod_family_members; DELETE FROM mod_families; DELETE FROM deployment_manifest; DELETE FROM original_files;
DELETE FROM conflict_rules; DELETE FROM resource_providers; DELETE FROM external_changes; DELETE FROM mod_files; DELETE FROM mods; DELETE FROM blobs;
DELETE FROM schema_info WHERE key='legacy_migration_complete';
""",null,ct);
    }

    private async Task InsertRule(ConflictRule r,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await db.ExecuteAsync("INSERT OR REPLACE INTO conflict_rules(id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at) VALUES($id,$k,$s,$l,$rr,$w,$p,$why,$e,$u)",new Dictionary<string,object?>{{"$id",r.Id},{"$k",r.Kind.ToString()},{"$s",r.Scope.ToString()},{"$l",r.LeftModId},{"$rr",r.RightModId},{"$w",r.WinnerModId},{"$p",r.PathPattern},{"$why",r.Reason},{"$e",r.Explicit?1:0},{"$u",r.CreatedUtc.ToString("O")}},ct);
    }
    private async Task RegisterBlob(string hash,long size,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await db.ExecuteAsync("INSERT OR IGNORE INTO blobs(sha256,size,created_at,verified_at) VALUES($h,$s,$u,$u)",new Dictionary<string,object?>{{"$h",hash},{"$s",size},{"$u",DateTimeOffset.UtcNow.ToString("O")}},ct);
    }
    private string EnsureLegacyBlob(string hash){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var src=Path.Combine(LegacyBlobs,hash);if(!File.Exists(src))throw new FileNotFoundException($"v7 referenced blob is missing: {hash}",src);Directory.CreateDirectory(nextBlobRoot);var dst=Path.Combine(nextBlobRoot,hash);if(!File.Exists(dst)){try{if(!OperatingSystem.IsWindows()||!CreateHardLink(dst,src,0))throw new IOException("Hardlink failed");}catch{File.Copy(src,dst,false);}}return dst;}
    private async Task<string?> InferProviderAsync(string path,string hash,CancellationToken ct){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await db.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT mf.mod_id FROM mod_files mf JOIN mods m ON m.id=mf.mod_id WHERE mf.path=$p AND mf.blob_sha256=$h AND m.enabled=1 ORDER BY m.priority DESC LIMIT 1";cmd.Parameters.AddWithValue("$p",path);cmd.Parameters.AddWithValue("$h",hash);return (string?)await cmd.ExecuteScalarAsync(ct);}
    private void CreatePointInTimeBackup(string backup,CancellationToken ct){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var included=new[]{"state.json","ui-settings.json","ui-draft.json"};foreach(var name in included){ct.ThrowIfCancellationRequested();var src=Path.Combine(LegacyRoot,name);if(File.Exists(src))CopyMetadata(src,Path.Combine(backup,name));}var hist=Path.Combine(LegacyRoot,"History");if(Directory.Exists(hist)){var dest=Path.Combine(backup,"History");foreach(var f in Directory.EnumerateFiles(hist,"*.json",SearchOption.AllDirectories)){ct.ThrowIfCancellationRequested();var rel=Path.GetRelativePath(hist,f);CopyMetadata(f,Path.Combine(dest,rel));}}File.WriteAllText(Path.Combine(backup,"legacy-location.txt"),LegacyRoot);foreach(var f in Directory.EnumerateFiles(backup,"*",SearchOption.AllDirectories))try{File.SetAttributes(f,File.GetAttributes(f)|FileAttributes.ReadOnly);}catch{}}
    private static void CopyMetadata(string src,string dst){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);File.Copy(src,dst,true);}
    [DllImport("kernel32.dll",EntryPoint="CreateHardLinkW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CreateHardLink(string newFileName,string existingFileName,nint securityAttributes);
    private static string LegacyId(string name){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var h=SHA256.HashData(Encoding.UTF8.GetBytes(name.ToLowerInvariant()));return "legacy-"+Convert.ToHexString(h.AsSpan(0,12)).ToLowerInvariant();}
    private static string ShortDisplayName(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return System.Text.RegularExpressions.Regex.Replace(name,@"-\d{2,7}-\d+(?:-\d+){0,6}(?:\s*\(\d+\))?$","",System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    }
}
