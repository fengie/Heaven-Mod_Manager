using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ArmorOutfitSelectionTests : IAsyncDisposable
{
    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("mhw-outfit-");
    public async ValueTask DisposeAsync()
    {
        await using (var connection = await new ManagerDatabase(Path.Combine(temp.FullName,"manager.db")).OpenAsync())
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        temp.Delete(true);
    }

    [Fact]
    public async Task Choose_outfit_applies_only_armor_preference_and_undo_restores_winner()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(temp.FullName,"manager.db"));
        await db.InitializeAsync(ct);
        var blobs = new BlobStore(Path.Combine(temp.FullName,"blobs"),db);
        var game = Directory.CreateDirectory(Path.Combine(temp.FullName,"game")).FullName;
        var executor = new DeploymentExecutor(db,blobs,new HashingService(),game);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine());
        const string target = @"nativePC\pl\f_equip\pl126_0000\body\mod\body.mod3";
        const string companion = @"nativePC\pl\f_equip\pl128_0000\body\mod\body.mod3";
        const string oldPieceFile = @"nativePC\pl\f_equip\pl126_0000\body\mod\physics.ctc";
        const string other = @"nativePC\pl\f_equip\pl127_0000\body\mod\body.mod3";
        foreach (var id in new[]{"alpha","beta"})
        {
            var source = Path.Combine(temp.FullName,id);
            await File.WriteAllTextAsync(source,id,ct);
            var hash = await blobs.CaptureAsync(source,ct);
            await db.UpsertModAsync(new(id,id,"Same visible name",source,id=="alpha",10),ct);
            await db.ReplaceModFilesAsync(id,(id=="alpha" ? new[]{target,other,oldPieceFile} : new[]{target,companion})
                .Select(path=>new ModFileDescriptor(id,path,hash,null,id.Length,DateTimeOffset.UtcNow,FileClass.Structural)).ToArray(),ct);
        }
        var original = await snapshots.LoadAsync(ct);
        Assert.True((await executor.ApplyAsync(planner.Build(original),"original",ct:ct)).Success);
        var selection = new ArmorOutfitSelection("pl126_0000","beta");
        var snapshot = await snapshots.LoadAsync(null,selection,ct);
        Assert.Equal("beta",snapshot.ArmorOutfitChoices[selection.ModelId]);
        Assert.False(snapshot.ArmorOutfitChoices.ContainsKey("pl127_0000"));
        var changes = await ArmorOutfitPreference.PrepareAsync(db,selection,snapshot,ct);
        var enabled = snapshot.Mods.Select(m=>m with { Enabled=true }).ToArray();
        var plan = planner.Build(snapshot with { Mods=enabled });
        Assert.False(plan.IsBlocked);
        var extra = Assert.Single(ArmorOutfitPreference.OutsideArmor(plan,selection.ModelId));
        Assert.Equal(companion,extra.Path,ignoreCase:true);
        Assert.Equal(ChangeKind.Add,extra.Kind);
        Assert.False(File.Exists(Path.Combine(game,companion)));
        Assert.True((await executor.ApplyWithMetadataAsync(plan,"switch",changes,
            enabled.ToDictionary(m=>m.Id,m=>(true,m.Priority)),ct:ct)).Success);
        Assert.Equal("beta",await File.ReadAllTextAsync(Path.Combine(game,target),ct));
        Assert.Equal("alpha",await File.ReadAllTextAsync(Path.Combine(game,other),ct));
        Assert.False(File.Exists(Path.Combine(game,oldPieceFile)));
        var reopened = new PlannerSnapshotRepository(new ManagerDatabase(db.DatabasePath));
        Assert.Equal("beta",(await reopened.LoadAsync(ct)).ArmorOutfitChoices[selection.ModelId]);
        var options = (await new PresentationReadRepository(db).GetArmorOptionsAsync(ct))[selection.ModelId].ToArray();
        Assert.Equal(2,options.Length);
        var preferred = Assert.Single(options,o=>o.IsPreferred);
        Assert.Equal(("beta",1),(preferred.ModId,preferred.WinningFiles));
        Assert.Equal(0,Assert.Single(options,o=>!o.IsPreferred).WinningFiles);
        Assert.True((await executor.UndoLastAsync(ct)).Success);
        Assert.Equal("alpha",await File.ReadAllTextAsync(Path.Combine(game,target),ct));
        Assert.True(File.Exists(Path.Combine(game,oldPieceFile)));
        Assert.False(File.Exists(Path.Combine(game,companion)));
        Assert.Empty((await reopened.LoadAsync(ct)).ArmorOutfitChoices);
    }
}
