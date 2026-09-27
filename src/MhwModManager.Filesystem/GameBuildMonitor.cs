using System.Security.Cryptography;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

public sealed class GameBuildMonitor(ManagerDatabase db,PlannerSnapshotRepository plannerSnapshots,GameProfile game)
{
    public async Task<GameBuildCheckResult> CheckAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.DisplayName}");
        var exe=game.ExecutablePath;
        if(!File.Exists(exe))return new(false,0,$"{Path.GetFileName(exe)} was not found for {game.DisplayName}.");
        var info=new FileInfo(exe);
        await using var stream=new FileStream(exe,FileMode.Open,FileAccess.Read,FileShare.ReadWrite,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan);
        var sha=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).ToLowerInvariant();
        var current=new GameBuildFingerprint(exe,info.Length,new DateTimeOffset(info.LastWriteTimeUtc,TimeSpan.Zero),sha);
        var previous=await db.GetGameBuildFingerprintAsync(ct);
        if(previous is null){await db.SetGameBuildFingerprintAsync(current,ct);return new(false,0,$"Recorded initial {game.DisplayName} build fingerprint.");}
        if(StringComparer.OrdinalIgnoreCase.Equals(previous.Sha256,current.Sha256)){await db.SetGameBuildFingerprintAsync(current,ct);return new(false,0,$"{game.DisplayName} build unchanged.");}

        var snapshot=await plannerSnapshots.LoadAsync(ct);
        var risky=snapshot.Files.Where(f=>f.FileClass is FileClass.Plugin or FileClass.Executable or FileClass.GameData)
            .Select(f=>f.ModId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(risky.Length>0)await db.MarkRevalidationAsync(risky,$"{Path.GetFileName(exe)} changed since this setup was last validated; plugin/executable/game-data mods should be revalidated before being trusted.",true,ct);
        await db.SetGameBuildFingerprintAsync(current,ct);
        return new(true,risky.Length,$"{game.DisplayName} changed. Marked {risky.Length} binary/plugin/game-data mod(s) for revalidation; ordinary asset-only mods remain valid.");
    }

    public sealed record GameBuildCheckResult(bool Changed,int MarkedMods,string Message);
}
