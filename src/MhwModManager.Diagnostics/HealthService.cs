using System.Collections.Concurrent;
using MhwModManager.Core;
using MhwModManager.Storage;
using MhwModManager.Filesystem;

namespace MhwModManager.Diagnostics;

public sealed class HealthService(ManagerDatabase db,PlannerSnapshotRepository plannerSnapshots,HashingService hashing,BlobStore blobs,Func<string,string> destination,GameProfile? game = null)
{
    private const int HashParallelism=4;

    public Task<IReadOnlyList<HealthIssue>> ScanAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ScanAsync(ct, null);
    }

    // Only the launch gate supplies this snapshot, after resolving an entirely applied plan.
    // An ordinary library health scan remains strict about every enabled source.
    public async Task<IReadOnlyList<HealthIssue>> ScanAsync(CancellationToken ct,
        IReadOnlyDictionary<string, DeploymentManifestEntry>? appliedTextures)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var verifiedWinners = new ConcurrentDictionary<string, string>(PathRules.Comparer);
        var issues=new ConcurrentBag<HealthIssue>();
        var integrity=await db.IntegrityCheckAsync(ct);
        if(!integrity.Equals("ok",StringComparison.OrdinalIgnoreCase))
            issues.Add(new("DB_INTEGRITY","Error","SQLite integrity_check failed.",Detail:integrity));

        // Health does not need every mod file; avoid materializing the whole library just to
        // validate the current deployment/rules.
        var snap=await plannerSnapshots.LoadAsync(Array.Empty<string>(),ct);
        if (appliedTextures is not null)
        {
            foreach (var expected in appliedTextures.Values)
                if (!snap.CurrentManifest.TryGetValue(expected.Path, out var current) || current != expected)
                    issues.Add(new("DEPLOYED_TEXTURE_SNAPSHOT_CHANGED", "Error", "Texture deployment changed during launch checks. Recheck the setup.", expected.Path));
        }
        await Parallel.ForEachAsync(snap.CurrentManifest.Values,
            new ParallelOptions{MaxDegreeOfParallelism=HashParallelism,CancellationToken=ct},
            async(e,token)=>
            {
                var path=destination(e.Path);
                if(!File.Exists(path)){issues.Add(new("DEPLOYED_MISSING","Error","Managed file is missing.",e.Path));return;}
                var inspectWinner = appliedTextures is not null &&
                    appliedTextures.TryGetValue(e.Path, out var expected) && expected == e;
                if (inspectWinner && (e.ProviderModId is null || e.ExpectedLiveSha256 is not { Length: 64 } ||
                    !StringComparer.OrdinalIgnoreCase.Equals(e.BlobSha256, e.ExpectedLiveSha256)))
                    issues.Add(new("DEPLOYED_TEXTURE_UNVERIFIED", "Error", "The applied texture lacks a verified provider or content digest.", e.Path));
                if(e.ExpectedLiveSha256 is not null)
                {
                    try
                    {
                        var h=await hashing.HashFileAsync(path,true,token);
                        if(!StringComparer.OrdinalIgnoreCase.Equals(h.Sha256,e.ExpectedLiveSha256))
                            issues.Add(new("DEPLOYED_CHANGED",inspectWinner?"Error":"Warning","Managed file changed outside the manager.",e.Path,Detail:$"expected={e.ExpectedLiveSha256} actual={h.Sha256}"));
                        else if (inspectWinner && e.ProviderModId is { } winner &&
                            StringComparer.OrdinalIgnoreCase.Equals(e.BlobSha256, h.Sha256))
                        {
                            // Verify the actual deployed texture, not a shadowed source.
                            var prefix = new byte[16];
                            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                            var read = await stream.ReadAsync(prefix.AsMemory(), token);
                            var safety = TextureSafetyInspector.InspectTexHeader(prefix.AsSpan(0, read), stream.Length);
                            if (safety.Severity == TextureSafetySeverity.Error)
                                issues.Add(new("DEPLOYED_" + safety.Code, "Error",
                                    $"Unsafe deployed texture: {safety.Message}", e.Path, winner));
                            else
                                verifiedWinners.TryAdd(e.Path, winner);
                        }
                    }
                    catch(Exception ex){issues.Add(new("DEPLOYED_UNREADABLE","Error","Managed file could not be verified.",e.Path,Detail:ex.Message));}
                }
                if(e.BlobSha256 is not null&&!File.Exists(blobs.PathFor(e.BlobSha256)))
                    issues.Add(new("BLOB_MISSING","Error","A committed manifest references a missing content blob.",e.Path,Detail:e.BlobSha256));
            });

        foreach(var original in snap.Originals)
            if(original.Value is not null&&!File.Exists(blobs.PathFor(original.Value)))
                issues.Add(new("ORIGINAL_BLOB_MISSING","Error","An original/manual baseline blob is missing.",original.Key,Detail:original.Value));

        var mods = (await db.GetModsAsync(ct)).Where(m => m.Enabled).ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var modFiles = await db.GetModFilesAsync(ct);
        foreach (var f in modFiles.Where(f => (game is null || game.IsMonsterHunterWorld) && f.FileClass == FileClass.Texture && f.Path.EndsWith(".tex", StringComparison.OrdinalIgnoreCase) && mods.ContainsKey(f.ModId)))
        {
            ct.ThrowIfCancellationRequested();
            var mod = mods[f.ModId];
            var shadowed = verifiedWinners.TryGetValue(f.Path, out var winner) &&
                !PathRules.Comparer.Equals(winner, f.ModId);
            var source = ResolveSourceFile(mod.SourcePath, f.Path);
            if (source is null)
            {
                issues.Add(new("TEX_SOURCE_MISSING", "Warning", $"Enabled texture source is missing for {mod.DisplayName}.", f.Path, mod.Id, "The catalog may be stale; refresh the mod before deployment."));
                continue;
            }
            try
            {
                var info = new FileInfo(source);
                if (info.Length != f.Length)
                    issues.Add(new("TEX_SOURCE_CHANGED", "Warning", $"Texture source changed after it was indexed: {mod.DisplayName}.", f.Path, mod.Id, $"indexedLength={f.Length} currentLength={info.Length}"));

                var prefix = new byte[16];
                await using var fs = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var read = await fs.ReadAsync(prefix.AsMemory(0, prefix.Length), ct);
                var safety = TextureSafetyInspector.InspectTexHeader(prefix.AsSpan(0, read), info.Length);
                if (safety.Severity == TextureSafetySeverity.Error)
                {
                    MasterDebugLog.Write("TEXTURE-SAFETY", $"{(shadowed ? "SHADOWED" : "BLOCK")} mod={mod.Id}; name={mod.DisplayName}; path={f.Path}; code={safety.Code}; detail={safety.Message}");
                    issues.Add(new(safety.Code, shadowed ? "Warning" : "Error",
                        $"Unsafe texture in {mod.DisplayName}: {safety.Message}" +
                        (shadowed ? $" Not deployed; verified winner: {winner}." : ""), f.Path, mod.Id, source));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MasterDebugLog.Write("TEXTURE-SAFETY", $"READ-FAIL mod={mod.Id}; name={mod.DisplayName}; path={f.Path}", ex);
                issues.Add(new("TEX_SOURCE_UNREADABLE", shadowed ? "Warning" : "Error",
                    $"Texture source could not be validated for {mod.DisplayName}." +
                    (shadowed ? $" Not deployed; verified winner: {winner}." : ""), f.Path, mod.Id, ex.Message));
            }
        }

        var cycle=RuleGraph.FindCycle(snap.Rules);
        if(cycle is not null)issues.Add(new("RULE_CYCLE","Error","Overlay precedence contains a cycle.",Detail:string.Join(" -> ",cycle)));

        await using(var c=await db.OpenAsync(ct))
        await using(var cmd=c.CreateCommand())
        {
            cmd.CommandText="SELECT id,state,started_at FROM operations WHERE state IN ('Prepared','Applying','FilesWritten','StateCommitting','RollingBack','RecoveryRequired') ORDER BY started_at";
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))issues.Add(new("INCOMPLETE_OPERATION","Error",$"Transaction {r.GetString(0)} requires recovery.",Detail:$"state={r.GetString(1)} started={r.GetString(2)}"));
        }

        return issues.OrderByDescending(x=>x.Severity.Equals("Error",StringComparison.OrdinalIgnoreCase)).ThenBy(x=>x.Code,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static string? ResolveSourceFile(string root, string managedPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
        var normalized = PathRules.Normalize(managedPath);
        if (normalized.StartsWith("nativePC\\", StringComparison.OrdinalIgnoreCase))
        {
            var rel = normalized["nativePC\\".Length..];
            var p1 = Path.Combine(root, "nativePC", rel);
            if (File.Exists(p1)) return p1;
            var p2 = Path.Combine(root, rel);
            if (File.Exists(p2)) return p2;
        }
        if (normalized.StartsWith("root\\", StringComparison.OrdinalIgnoreCase))
        {
            var rel = normalized["root\\".Length..];
            var p = Path.Combine(root, "GameRoot", rel);
            if (File.Exists(p)) return p;
        }
        return null;
    }

}
