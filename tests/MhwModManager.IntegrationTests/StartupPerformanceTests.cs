using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Diagnostics;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class StartupPerformanceTests : IDisposable
{
    private static CancellationToken TestToken=>TestContext.Current.CancellationToken;
    private readonly string root=Path.Combine(Path.GetTempPath(),"umm-startup-perf-"+Guid.NewGuid().ToString("N"));

    public StartupPerformanceTests()=>Directory.CreateDirectory(root);

    [Fact]
    public void TerminalStartupReportReplacesTheSnapshotHeldByTheBenchmark()
    {
        var session = StartupDiagnosticSession.Start(MasterDebugLog.RootDirectory);
        using var activeReader = File.Open(session.JsonReportPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        session.Complete(true, "Completed under observation");
        using var reader = new StreamReader(activeReader);
        using var initial = JsonDocument.Parse(reader.ReadToEnd());
        Assert.Equal("RUNNING", initial.RootElement.GetProperty("overall").GetString());
        using var final = JsonDocument.Parse(File.ReadAllText(session.JsonReportPath));
        Assert.Equal("PASS", final.RootElement.GetProperty("overall").GetString());
        Assert.Equal("Completed under observation", final.RootElement.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Unchanged_game_build_metadata_skips_fingerprint_rewrite()
    {
        var gameRoot=Path.Combine(root,"game");
        Directory.CreateDirectory(gameRoot);
        var exe=Path.Combine(gameRoot,"Game.exe");
        var bytes=new byte[1024*1024];
        Random.Shared.NextBytes(bytes);
        await File.WriteAllBytesAsync(exe,bytes,TestToken);

        var profile=GameProfile.Generic("perf-game","Performance Fixture",gameRoot,"Game.exe","");
        var db=new ManagerDatabase(Path.Combine(root,"state","manager.db"));
        await db.InitializeAsync(TestToken);

        var info=new FileInfo(exe);
        var sha=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await db.SetGameBuildFingerprintAsync(new(
            exe,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc,TimeSpan.Zero),
            sha),TestToken);

        const string sentinel="2000-01-01T00:00:00.0000000+00:00";
        await db.ExecuteAsync(
            "UPDATE game_build_state SET observed_at=$observed WHERE id=1",
            new Dictionary<string,object?>{{"$observed",sentinel}},
            TestToken);

        var monitor=new GameBuildMonitor(db,new PlannerSnapshotRepository(db),profile);
        var result=await monitor.CheckAsync(TestToken);

        Assert.False(result.Changed);
        await using var connection=await db.OpenAsync(TestToken);
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT observed_at FROM game_build_state WHERE id=1";
        Assert.Equal(sentinel,(string?)await command.ExecuteScalarAsync(TestToken));
    }

    public void Dispose()
    {
        try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        GC.SuppressFinalize(this);
    }
}
