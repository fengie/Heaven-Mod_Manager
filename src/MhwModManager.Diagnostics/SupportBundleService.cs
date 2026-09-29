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
    private static readonly Regex SecretAssignmentPattern = new(
        @"(?i)(?<prefix>\b(?:authorization)\b\s*[:=]\s*bearer\s+|\b(?:token|access[_-]?token|api[_-]?key|apikey|password|secret|credential|cookie)\b\s*[:=]\s*)(?<value>[^\s,;""'&}\]]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WindowsAbsolutePathPattern = new(
        @"(?<![A-Za-z0-9_])(?:[A-Za-z]:\\|\\\\)[^""'\r\n<>|,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
                $"MHW Manual Mod Manager v{GetProductVersion()} support bundle",
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
            await WritePrivacyNoticeAsync(Path.Combine(temp,"CONTENTS-AND-PRIVACY.txt"),ct);

            var logDir=Path.Combine(stateRoot,"Next","Logs");
            if(Directory.Exists(logDir))
                foreach(var f in Directory.EnumerateFiles(logDir,"*.jsonl").OrderByDescending(x=>x).Take(5))
                    await CopySanitizedStructuredLogAsync(f,Path.Combine(temp,Path.GetFileName(f)),ct);

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

    private static string GetProductVersion()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var version=typeof(SupportBundleService).Assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
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
            settings[key]=IsSensitiveName(key)?"<redacted>":(r.IsDBNull(1)?null:SanitizeText(r.GetString(1)));
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

    private static async Task WritePrivacyNoticeAsync(string path,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await File.WriteAllLinesAsync(path,
        [
            "This support bundle is intended for troubleshooting and may be shared with support.",
            "Recent structured logs are sanitized during export: secret-like fields/assignments and absolute Windows paths are redacted.",
            "The full-fidelity local logs remain unchanged on this computer.",
            "Other bundle entries can still describe installed mods, profiles, conflict rules, deployment paths, hashes, and operation state.",
            "Review the archive before sharing it with another person or service."
        ],ct);
    }

    private async Task CopySanitizedStructuredLogAsync(string source,string destination,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var input=new StreamReader(source);
        using var output=new StreamWriter(destination,false);
        while(await input.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            await output.WriteLineAsync(SanitizeStructuredLogLine(line).AsMemory(),ct);
        }
    }

    private string SanitizeStructuredLogLine(string line)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var parsed=JsonNode.Parse(line);
            var sanitized=SanitizeJsonNode(parsed);
            return sanitized?.ToJsonString()??"null";
        }
        catch(JsonException)
        {
            return SanitizeText(line);
        }
    }

    private JsonNode? SanitizeJsonNode(JsonNode? node)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(node is null)return null;
        if(node is JsonObject obj)
        {
            var sanitized=new JsonObject();
            foreach(var pair in obj)
                sanitized[pair.Key]=IsSensitiveName(pair.Key)?JsonValue.Create("<redacted>"):SanitizeJsonNode(pair.Value);
            return sanitized;
        }
        if(node is JsonArray array)
        {
            var sanitized=new JsonArray();
            foreach(var item in array)sanitized.Add(SanitizeJsonNode(item));
            return sanitized;
        }
        if(node is JsonValue value&&value.TryGetValue<string>(out var text))
            return JsonValue.Create(SanitizeText(text));
        return node.DeepClone();
    }

    private string SanitizeText(string text)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var sanitized=text;
        foreach(var item in SensitiveRoots())
            sanitized=sanitized.Replace(item.Value,item.Replacement,StringComparison.OrdinalIgnoreCase);
        sanitized=SecretAssignmentPattern.Replace(sanitized,m=>m.Groups["prefix"].Value+"<redacted>");
        return WindowsAbsolutePathPattern.Replace(sanitized,"<absolute-path>");
    }

    private (string Value,string Replacement)[] SensitiveRoots()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var roots=new List<(string Value,string Replacement)>
        {
            (stateRoot,"<state-root>"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user-profile>"),
            (Path.GetTempPath(),"<temp-root>"),
            (AppContext.BaseDirectory,"<app-root>")
        };
        return roots
            .Where(x=>!string.IsNullOrWhiteSpace(x.Value))
            .OrderByDescending(x=>x.Value.Length)
            .ToArray();
    }

    private static bool IsSensitiveName(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return name.Contains("token",StringComparison.OrdinalIgnoreCase)
            ||name.Contains("secret",StringComparison.OrdinalIgnoreCase)
            ||name.Contains("password",StringComparison.OrdinalIgnoreCase)
            ||name.Contains("api_key",StringComparison.OrdinalIgnoreCase)
            ||name.Contains("apikey",StringComparison.OrdinalIgnoreCase)
            ||name.Contains("authorization",StringComparison.OrdinalIgnoreCase)
            ||name.Contains("credential",StringComparison.OrdinalIgnoreCase)
            ||name.Contains("cookie",StringComparison.OrdinalIgnoreCase)
            ||name.Contains("bearer",StringComparison.OrdinalIgnoreCase);
    }
}
