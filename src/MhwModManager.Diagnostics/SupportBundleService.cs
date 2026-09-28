using MhwModManager.Core;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MhwModManager.Storage;

namespace MhwModManager.Diagnostics;

public sealed class SupportBundleService(ManagerDatabase db,string stateRoot,DiagnosticTelemetry telemetry)
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    public async Task<string> CreateAsync(string outputDirectory,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(outputDirectory);
        var temp=Path.Combine(Path.GetTempPath(),"MHWMM-support-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var info=new[]
            {
                "MHW Manual Mod Manager v8.3.0 support bundle",
                $"Generated: {DateTimeOffset.UtcNow:O}",
                $"OS: {Environment.OSVersion}",
                $"Runtime: {Environment.Version}",
                $"Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
                $"Database schema: {await ReadScalarAsync("SELECT value FROM schema_info WHERE key='version'",ct)??"unknown"}",
                $"Migration: {await ReadScalarAsync("SELECT value FROM schema_info WHERE key='legacy_migration_complete'",ct)??"not-complete"}",
                $"Diagnostic mode: {telemetry.DetailedMode}",
                $"Active operations at export: {string.Join(", ",telemetry.ActiveOperations.Select(x=>x.Name))}"
            };
            await File.WriteAllLinesAsync(Path.Combine(temp,"environment.txt"),info,ct);
            await File.WriteAllTextAsync(Path.Combine(temp,"db-integrity.txt"),await db.IntegrityCheckAsync(ct),ct);

            await ExportRowsAsync(Path.Combine(temp,"recent-diagnostics.json"),
                "SELECT time,operation_id,kind,elapsed_ms,data_json FROM diagnostics ORDER BY id DESC LIMIT 1000",ct);
            await ExportRowsAsync(Path.Combine(temp,"recent-errors.json"),
                "SELECT id,time,operation_id,category,exception_type,message,native_code FROM error_reports ORDER BY time DESC LIMIT 200",ct);
            await ExportRowsAsync(Path.Combine(temp,"recent-operations.json"),
                "SELECT id,state,description,started_at,committed_at,error,parent_operation_id FROM operations ORDER BY started_at DESC LIMIT 200",ct);
            await ExportRowsAsync(Path.Combine(temp,"manifest-summary.json"),
                "SELECT path,provider_mod_id,blob_sha256,expected_live_sha256,rule_id,deployed_at FROM deployment_manifest ORDER BY path",ct,5000);
            await ExportRowsAsync(Path.Combine(temp,"conflict-rules.json"),
                "SELECT id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at FROM conflict_rules ORDER BY created_at",ct,5000);
            await ExportRowsAsync(Path.Combine(temp,"profiles.json"),
                "SELECT p.id,p.name,p.updated_at,SUM(CASE WHEN pm.enabled=1 THEN 1 ELSE 0 END) AS enabled_count FROM profiles p LEFT JOIN profile_mods pm ON pm.profile_id=p.id GROUP BY p.id ORDER BY p.name",ct,1000);
            await ExportRowsAsync(Path.Combine(temp,"incomplete-transactions.json"),
                "SELECT id,state,description,started_at,error FROM operations WHERE state NOT IN ('Committed','RolledBack','Failed') ORDER BY started_at",ct,1000);
            await ExportRedactedSettingsAsync(Path.Combine(temp,"settings-redacted.json"),ct);
            await ExportCountsAsync(Path.Combine(temp,"state-counts.json"),ct);

            var logDir=Path.Combine(stateRoot,"Next","Logs");
            if(Directory.Exists(logDir))
                foreach(var f in Directory.EnumerateFiles(logDir,"*.jsonl").OrderByDescending(x=>x).Take(5))
                    await ExportSanitizedLogAsync(f,Path.Combine(temp,Path.GetFileName(f)),ct);

            var stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture);
            var zip=Path.Combine(outputDirectory,$"MHWMM-Support-{stamp}.zip");
            ZipFile.CreateFromDirectory(temp,zip,CompressionLevel.Fastest,false);
            return zip;
        }
        finally
        {
            try{if(Directory.Exists(temp))Directory.Delete(temp,true);}catch{}
        }
    }

    private async Task ExportSanitizedLogAsync(string source,string destination,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var reader=new StreamReader(input);
        await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        await using var writer=new StreamWriter(output);
        while(await reader.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(SanitizeStructuredLogLine(line).AsMemory(),ct);
        }
    }

    private string SanitizeStructuredLogLine(string line)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        JsonNode? root;
        try { root=JsonNode.Parse(line); }
        catch(JsonException) { return SanitizeText(line); }
        if(root is null)return SanitizeText(line);

        var pending=new Stack<JsonNode>();
        pending.Push(root);
        while(pending.Count>0)
        {
            var node=pending.Pop();
            if(node is JsonObject obj)
            {
                foreach(var property in obj.ToArray())
                {
                    var child=property.Value;
                    var key=property.Key;
                    var sensitive=key.Contains("token",StringComparison.OrdinalIgnoreCase)
                        ||key.Contains("secret",StringComparison.OrdinalIgnoreCase)
                        ||key.Contains("password",StringComparison.OrdinalIgnoreCase)
                        ||key.Contains("api_key",StringComparison.OrdinalIgnoreCase)
                        ||key.Contains("apikey",StringComparison.OrdinalIgnoreCase)
                        ||key.Contains("credential",StringComparison.OrdinalIgnoreCase)
                        ||key.Equals("authorization",StringComparison.OrdinalIgnoreCase);
                    if(sensitive){obj[key]="<redacted>";continue;}
                    if(child is JsonValue value&&value.TryGetValue<string>(out var text))obj[key]=SanitizeText(text);
                    else if(child is not null)pending.Push(child);
                }
            }
            else if(node is JsonArray array)
            {
                for(var i=0;i<array.Count;i++)
                {
                    var child=array[i];
                    if(child is JsonValue value&&value.TryGetValue<string>(out var text))array[i]=SanitizeText(text);
                    else if(child is not null)pending.Push(child);
                }
            }
        }
        return root.ToJsonString();
    }

    private string SanitizeText(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var sanitized=value;
        var roots=new[]
        {
            (Path.GetFullPath(stateRoot).TrimEnd(Path.DirectorySeparatorChar),"<STATE_ROOT>"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar),"<USER_PROFILE>"),
            (Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),"<TEMP>")
        };
        foreach(var (rootPath,replacement) in roots)
            if(!string.IsNullOrWhiteSpace(rootPath))
                sanitized=sanitized.Replace(rootPath,replacement,StringComparison.OrdinalIgnoreCase);

        sanitized=Regex.Replace(sanitized,@"(?i)\b[A-Z]:[\\/]+Users[\\/]+[^\\/:*?""<>|\r\n]+","<USER_PROFILE>");
        sanitized=Regex.Replace(sanitized,@"(?i)\bAuthorization\s*[:=]\s*(?:Bearer\s+)?[^\s,;""']+","Authorization: <redacted>");
        sanitized=Regex.Replace(sanitized,@"(?i)([?&](?:token|access_token|api_key|apikey|key|secret|password|authorization)=)[^&#\s""']+","$1<redacted>");
        return sanitized;
    }

    private async Task<string?> ReadScalarAsync(string sql,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await db.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText=sql;return (await cmd.ExecuteScalarAsync(ct))?.ToString();
    }

    private async Task ExportRowsAsync(string path,string sql,CancellationToken ct,int maxRows=1000)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var rows=new List<Dictionary<string,object?>>(Math.Min(maxRows,256));
        await using var c=await db.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText=sql;await using var r=await cmd.ExecuteReaderAsync(ct);
        while(rows.Count<maxRows&&await r.ReadAsync(ct))
        {
            var row=new Dictionary<string,object?>(StringComparer.OrdinalIgnoreCase);
            for(var i=0;i<r.FieldCount;i++)row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i);
            // Do not export large stack traces/state JSON through generic queries.
            rows.Add(row);
        }
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(rows,IndentedJson),ct);
    }
    private async Task ExportRedactedSettingsAsync(string path,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var settings=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        await using var c=await db.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT key,value FROM settings ORDER BY key";await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))
        {
            var key=r.GetString(0);
            var secret=key.Contains("token",StringComparison.OrdinalIgnoreCase)||key.Contains("secret",StringComparison.OrdinalIgnoreCase)||key.Contains("password",StringComparison.OrdinalIgnoreCase)||key.Contains("api_key",StringComparison.OrdinalIgnoreCase)||key.Contains("apikey",StringComparison.OrdinalIgnoreCase);
            settings[key]=secret?"<redacted>":(r.IsDBNull(1)?null:r.GetString(1));
        }
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(settings,IndentedJson),ct);
    }

    private async Task ExportCountsAsync(string path,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var tables=new[]{"mods","mod_files","blobs","deployment_manifest","original_files","conflict_rules","profiles","operations","diagnostics","error_reports"};
        var counts=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        await using var c=await db.OpenAsync(ct);
        foreach(var table in tables)
        {
            await using var cmd=c.CreateCommand();cmd.CommandText=$"SELECT COUNT(*) FROM {table}";
            counts[table]=Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)??0,CultureInfo.InvariantCulture);
        }
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(counts,IndentedJson),ct);
    }

}
