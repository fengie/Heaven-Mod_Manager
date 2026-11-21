using Microsoft.Data.Sqlite;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class AuthoredFamilyCatalogTests
{
    [Fact]
    public async Task ActiveLegacyPackagePreviewsItsMainAndOptionalOverlayAndSurvivesChangedSource()
    {
        var ct = TestContext.Current.CancellationToken;
        var temp = Directory.CreateTempSubdirectory("family-legacy-");
        var db = new ManagerDatabase(Path.Combine(temp.FullName,"state.db"));
        try
        {
            await db.InitializeAsync(ct);
            var game = DiscoveredGameProfiles.Isolate(DiscoveredGameProfiles.Load().First(p=>p.IsMonsterHunterWorld),temp.FullName);
            var installed=Path.Combine(temp.FullName,"Installed");
            var root=Path.Combine(installed,"Traveller Outfit");
            var main=Path.Combine(root,"Main File - Outfit","nativePC","npc");
            var overlay=Path.Combine(root,"Optional File - Alternate","nativePC","npc");
            Directory.CreateDirectory(main);
            Directory.CreateDirectory(overlay);
            await File.WriteAllTextAsync(Path.Combine(root,"Download main file first..txt"), "Main file required.",ct);
            await File.WriteAllTextAsync(Path.Combine(main,"body.mod3"),"base",ct);
            await File.WriteAllTextAsync(Path.Combine(main,"other.mod3"),"other",ct);
            await File.WriteAllTextAsync(Path.Combine(overlay,"body.mod3"),"alternate",ct);
            var blobs=new BlobStore(Path.Combine(temp.FullName,"blobs"),db);
            var scanner=new ModScanner(db,blobs,new HashingService(),game);
            var catalog=new CatalogService(db,scanner,installed);
            var old=new ModDescriptor("existing-owner","Traveller Outfit","Traveller Outfit",root,true,12);
            await db.UpsertModAsync(old,ct);
            Assert.Equal(4,(await scanner.CaptureAsync(old,ct)).Count);
            var planner=new DeploymentPlanner(new ConflictEngine(game),game);
            var repo=new PlannerSnapshotRepository(db);
            var executor=new DeploymentExecutor(db,blobs,new HashingService(),game.GameRoot);
            var oldPlan=planner.Build(await repo.LoadAsync(ct));
            Assert.True((await executor.ApplyAsync(oldPlan,"Initial legacy layout",ct:ct)).Success);
            await catalog.RefreshFoldersAsync(ct);
            var rows=await db.GetModsAsync(ct);
            Assert.Equal(2,rows.Count);
            var primary=Assert.Single(rows,mod=>mod.FamilyRole=="Main");
            var extra=Assert.Single(rows,mod=>mod.FamilyRole=="Optional");
            Assert.Equal(old.Id,primary.Id);
            Assert.True(primary.Enabled);
            Assert.False(extra.Enabled);
            Assert.Equal(4,(await repo.LoadAsync(ct)).CurrentManifest.Count);
            await catalog.EnsureCapturedAsync(rows,ct);
            var snapshot=await repo.LoadAsync(ct);
            Assert.Equal(3,snapshot.Files.Count);
            var family=Assert.Single(LogicalModFamilies.Build(rows,game:game));
            var presets=new OutfitPresetService().Build(family);
            Assert.Single(presets.Single(p=>p.Name=="Default").EnabledMemberIds);
            var optionalOnly=WorkflowAnalysis.WithState(snapshot, rows.ToDictionary(m=>m.Id,m=>(m.Id==extra.Id,m.Priority)));
            Assert.True(planner.Build(optionalOnly).IsBlocked);
            var both=rows.ToDictionary(m=>m.Id,m=>(true,m.Priority));
            var corrected=planner.Build(WorkflowAnalysis.WithState(snapshot,both));
            Assert.False(corrected.IsBlocked);
            Assert.True((await executor.ApplyAsync(corrected,"Use alternate",both,ct:ct)).Success);
            Assert.Equal("alternate",await File.ReadAllTextAsync(Path.Combine(game.GameRoot,"nativePC","npc","body.mod3"),ct));
            var unexpected=Path.Combine(overlay,"unexpected.mod3");
            await File.WriteAllTextAsync(unexpected,"unattributed",ct);
            await Assert.ThrowsAsync<InvalidDataException>(()=>scanner.CaptureAsync(extra,ct));
            File.Delete(unexpected);
            var optionalDir=Directory.GetParent(Directory.GetParent(overlay)!.FullName)!.FullName;
            var parked=Path.Combine(temp.FullName,"offline");
            Directory.Move(optionalDir,parked);
            await catalog.RefreshFoldersAsync(ct);
            await catalog.RefreshFoldersAsync(ct);
            Assert.All(await db.GetModsAsync(ct),mod=>Assert.True(mod.RequiresLayoutReview));
            Directory.Move(parked,optionalDir);
            await catalog.RefreshFoldersAsync(ct);
            var restored=await db.GetModsAsync(ct);
            Assert.Equal(rows.Select(m=>m.Id).Order(),restored.Select(m=>m.Id).Order());
            Assert.DoesNotContain(restored,m=>m.RequiresLayoutReview);
        }
        finally
        {
            await using(var connection=await db.OpenAsync(CancellationToken.None)) SqliteConnection.ClearPool(connection);
            temp.Delete(true);
        }
    }
}
