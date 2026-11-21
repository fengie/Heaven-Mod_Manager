using Microsoft.Data.Sqlite;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class AuthoredFamilyCatalogTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalPickerKeepsBothDocumentedAlternativesTogetherForTheIdentifiedGame(bool zip)
    {
        var ct = TestContext.Current.CancellationToken;
        var temp = Directory.CreateTempSubdirectory("family-picker-");
        try
        {
            var games = DiscoveredGameProfiles.Load().Select(profile=>DiscoveredGameProfiles.Isolate(profile,temp.FullName)).ToArray();
            var owner = games.First(profile=>profile.AdapterId=="unreal-paks");
            var manager=Path.Combine(temp.FullName,"Manager");
            var root=Directory.CreateDirectory(Path.Combine(temp.FullName,owner.Id + " Traveller Outfit")).FullName;
            foreach(var label in new[]{"WITH_Cape","NO_Cape"})
            {
                var folder=Directory.CreateDirectory(Path.Combine(root,label)).FullName;
                foreach(var extension in new[]{".pak",".utoc",".ucas"})
                    await File.WriteAllTextAsync(Path.Combine(folder,"outfit"+extension),label,ct);
            }
            await File.WriteAllTextAsync(Path.Combine(root,"outfit_readme.txt"),"Includes a cape version and another without it. Choose only one.",ct);
            var input=root;
            if(zip)
            {
                input=root+".zip";
                System.IO.Compression.ZipFile.CreateFromDirectory(root,input);
            }
            var intake=new GameModIntakeService(manager,Path.Combine(manager,"State"),()=>games);
            var result=await intake.ImportPathAsync(input,ct);
            var routed=Assert.Single(result.Items);
            Assert.Equal(GameModIntakeStatus.Routed,routed.Status);
            Assert.Equal(owner.Id,routed.GameId);
            var published=Assert.Single(Directory.EnumerateDirectories(GameModStorage.Resolve(manager,owner).InboxRoot));
            Assert.Equal(7,Directory.GetFiles(published,"*",SearchOption.AllDirectories).Length);
            Assert.Equal(1,(await intake.ImportPathAsync(input,ct)).AlreadyPresent);
        }
        finally { temp.Delete(true); }
    }

    [Fact]
    public async Task NestedChoicesShareTheirExistingShelfOwnershipAcrossRefreshes()
    {
        var ct=TestContext.Current.CancellationToken;
        var temp=Directory.CreateTempSubdirectory("family-nested-");
        var db=new ManagerDatabase(Path.Combine(temp.FullName,"manager.db"));
        try
        {
            await db.InitializeAsync(ct);
            var game=DiscoveredGameProfiles.Isolate(DiscoveredGameProfiles.Load().First(p=>p.AdapterId=="unreal-paks"),temp.FullName);
            var installed=Path.Combine(temp.FullName,"Installed");
            var shelf=Path.Combine(installed,"Various outfits");
            var family=Directory.CreateDirectory(Path.Combine(shelf,"Traveller")).FullName;
            await File.WriteAllTextAsync(Path.Combine(family,"README.txt"),"Choose one option only.",ct);
            foreach(var label in new[]{"NO_Cape","WITH_Cape"})
                await File.WriteAllTextAsync(Path.Combine(Directory.CreateDirectory(Path.Combine(family,label)).FullName,"costume.pak"),label,ct);
            var other=Directory.CreateDirectory(Path.Combine(shelf,"Another outfit")).FullName;
            await File.WriteAllTextAsync(Path.Combine(other,"other.pak"),"other",ct);
            await db.UpsertModAsync(new("legacy","Traveller","Traveller",family,false,1),ct);
            await db.SetShelfEntriesAsync(family,[("legacy","wrapper",null)],ct);
            var scanner=new ModScanner(db,new BlobStore(Path.Combine(temp.FullName,"blobs"),db),new HashingService(),game);
            var catalog=new CatalogService(db,scanner,installed,inspectShelf:(_,_)=>new([new(family,true),new(other,false)],true));
            await catalog.RefreshFoldersAsync(ct);
            await catalog.RefreshFoldersAsync(ct);
            Assert.Equal(3,(await db.GetModsAsync(ct)).Count);
            Assert.Contains((await db.ReadShelfEntriesAsync(ct))[shelf],item=>item.Id=="legacy" && item.Role=="wrapper");
        }
        finally
        {
            await using(var connection=await db.OpenAsync(CancellationToken.None)) SqliteConnection.ClearPool(connection);
            temp.Delete(true);
        }
    }

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
            await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SeparateLogicalMemberAsync(extra.Id,ct));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>db.ChainManualFamilyAsync(primary.Id,[primary.Id],[[extra.Id]],"Unrelated",ct));
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
