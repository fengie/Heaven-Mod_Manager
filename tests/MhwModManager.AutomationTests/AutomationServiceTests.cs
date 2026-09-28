using System.Diagnostics;
using System.Text.Json;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class AutomationServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions TestJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string root=Path.Combine(Path.GetTempPath(),"MhwAutomationTests-"+Guid.NewGuid().ToString("N"));
    public AutomationServiceTests()=>Directory.CreateDirectory(root);

    [Fact]
    public async Task SaveSnapshotCopiesExplicitSaveAndState()
    {
        var db=await CreateDbAsync("backup.db");
        var save=Path.Combine(root,"SAVEDATA1000");await File.WriteAllTextAsync(save,"save",TestContext.Current.CancellationToken);
        var old=Environment.GetEnvironmentVariable("MHW_SAVE_PATH");
        try
        {
            Environment.SetEnvironmentVariable("MHW_SAVE_PATH",save);
            var result=await new SaveBackupService(db,Path.Combine(root,"state"),GameProfile.MonsterHunterWorld(Path.Combine(root,"game"))).CreateAsync("test",TestContext.Current.CancellationToken);
            Assert.True(result.Success);Assert.Equal(1,result.FilesCopied);Assert.True(File.Exists(Path.Combine(result.SnapshotRoot,"snapshot.json")));
        }
        finally{Environment.SetEnvironmentVariable("MHW_SAVE_PATH",old);}
    }

    [Fact]
    public async Task UpdateDiffCountsStructuralAndTextureChanges()
    {
        var db=await CreateDbAsync("diff.db");
        await SeedModAsync(db,"old","Old");await SeedModAsync(db,"new","New");
        await db.ReplaceModFilesAsync("old",[
            ModFile("old",@"nativePC\x.mod3","a",FileClass.Structural),ModFile("old",@"nativePC\x.tex","b",FileClass.Texture)],TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("new",[
            ModFile("new",@"nativePC\x.mod3","c",FileClass.Structural),ModFile("new",@"nativePC\x.tex","d",FileClass.Texture),ModFile("new",@"nativePC\extra.tex","e",FileClass.Texture)],TestContext.Current.CancellationToken);
        var diff=await new UpdateDiffService(db).CompareAsync("old","new",TestContext.Current.CancellationToken);
        Assert.Equal(1,diff.Added);Assert.Equal(2,diff.Changed);Assert.Equal(1,diff.StructuralChanged);Assert.Equal(1,diff.TextureChanged);
    }

    [Fact]
    public async Task DuplicateAnalysisFindsExactPayloads()
    {
        var db=await CreateDbAsync("dupes.db");var a=Path.Combine(root,"A");var b=Path.Combine(root,"B");Directory.CreateDirectory(a);Directory.CreateDirectory(b);
        await db.UpsertModAsync(new("a","A","A",a,false,1),TestContext.Current.CancellationToken);await db.UpsertModAsync(new("b","B","B",b,false,2),TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("a",[ModFile("a",@"nativePC\same.tex","hash",FileClass.Texture)],TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("b",[ModFile("b",@"nativePC\same.tex","hash",FileClass.Texture)],TestContext.Current.CancellationToken);
        var result=await new DuplicateCleanupService(db,Path.Combine(root,"archive")).AnalyzeAsync(TestContext.Current.CancellationToken);
        Assert.Single(result.ExactDuplicates);
    }

    [Fact]
    public async Task EffectiveInspectorShowsWinnerAndShadowedProvider()
    {
        var db=await CreateDbAsync("inspect.db");await SeedModAsync(db,"a","A");await SeedModAsync(db,"b","B");
        await db.ReplaceModFilesAsync("a",[ModFile("a",@"nativePC\same.tex","a1",FileClass.Texture)],TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("b",[ModFile("b",@"nativePC\same.tex","b1",FileClass.Texture)],TestContext.Current.CancellationToken);
        await db.ExecuteAsync("INSERT INTO deployment_manifest(path,provider_mod_id,blob_sha256,expected_live_sha256,rule_id,deployed_at) VALUES($p,$m,$b,$e,NULL,$t)",new Dictionary<string,object?>{{"$p",@"nativePC\same.tex"},{"$m","b"},{"$b","b1"},{"$e","b1"},{"$t",DateTimeOffset.UtcNow.ToString("O",System.Globalization.CultureInfo.InvariantCulture)}},TestContext.Current.CancellationToken);
        var result=await new EffectiveInspectorService(new PlannerSnapshotRepository(db)).ExplainAsync(@"nativePC\same.tex",TestContext.Current.CancellationToken);
        Assert.NotNull(result);Assert.Equal("b",result!.EffectiveModId);Assert.Contains("a",result.ShadowedModIds);
    }

    [Fact]
    public async Task ExplainWhyUsesPlannerDecisionAndHumanRuleEvidence()
    {
        var db=await CreateDbAsync("explain-why.db");
        await SeedModAsync(db,"a","Base Provider");
        await SeedModAsync(db,"b","Chosen Provider");
        await db.ReplaceModFilesAsync("a",[ModFile("a",@"nativePC\same.tex","a1",FileClass.Texture)],TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("b",[ModFile("b",@"nativePC\same.tex","b1",FileClass.Texture)],TestContext.Current.CancellationToken);
        var now=DateTimeOffset.UtcNow.ToString("O",System.Globalization.CultureInfo.InvariantCulture);
        await db.ExecuteAsync(
            "INSERT INTO conflict_rules(id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at) VALUES('exact-b','ExactWinner','ExactPath',NULL,NULL,'b',$p,'User selected B',1,$t)",
            new Dictionary<string,object?>{{"$p",@"nativePC\same.tex"},{"$t",now}},
            TestContext.Current.CancellationToken);
        await db.ExecuteAsync(
            "INSERT INTO deployment_manifest(path,provider_mod_id,blob_sha256,expected_live_sha256,rule_id,deployed_at) VALUES($p,'b','b1','b1','exact-b',$t)",
            new Dictionary<string,object?>{{"$p",@"nativePC\same.tex"},{"$t",now}},
            TestContext.Current.CancellationToken);

        var planner=new DeploymentPlanner(new ConflictEngine());
        var result=await new EffectiveInspectorService(new PlannerSnapshotRepository(db),planner).ExplainWhyAsync(@"nativePC\same.tex",TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(result!.Blocking);
        Assert.Equal("b",result.PlannedWinnerModId);
        Assert.Equal("Chosen Provider",result.PlannedWinnerName);
        Assert.Equal("exact-file-winner",result.ReasonCode);
        Assert.Equal("Explicit human rule",result.RuleSource);
        Assert.True(result.AppliedMatchesPlan);
        Assert.Equal(2,result.Providers.Count);
        Assert.Single(result.Providers,x=>x.PlannedWinner&&x.AppliedProvider&&x.ModId=="b");
        Assert.Contains("saved exact-file winner",result.Explanation,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PresentationReadRepositoryCombinesAutomationActivity()
    {
        var db=await CreateDbAsync("presentation-read.db");
        var when=DateTimeOffset.UtcNow.ToString("O",System.Globalization.CultureInfo.InvariantCulture);
        await db.ExecuteAsync(
            "INSERT INTO automation_events(id,time,kind,severity,message,data_json) VALUES('event-1',$t,'test','Info','Readable activity',NULL)",
            new Dictionary<string,object?>{{"$t",when}},
            TestContext.Current.CancellationToken);

        var rows=await new PresentationReadRepository(db).GetRecentActivityAsync(10,TestContext.Current.CancellationToken);

        var row=Assert.Single(rows);
        Assert.Equal("event-1",row.Id);
        Assert.Equal("Info",row.State);
        Assert.Equal("Readable activity",row.Description);
    }

    [Fact]
    public void OutfitPresetsCollapseCommonOptionParts()
    {
        var family=new LogicalModFamily("f","HPN Test","HPN Test",[
            new("main","HPN Test","HPN Test","x",false,1),new("waist","HPN Test - Skimpy Waist","HPN Test - Skimpy Waist","y",false,2),new("cape","HPN Test - No Cape","HPN Test - No Cape","z",false,3)],null);
        var presets=new OutfitPresetService().Build(family);
        Assert.Contains(presets,x=>x.Name=="Skimpy");Assert.Contains(presets,x=>x.Name=="No Cape");
    }

    [Fact]
    public async Task DependencyDoctorFlagsNativePluginWithoutLoader()
    {
        var db=await CreateDbAsync("deps.db");var game=Path.Combine(root,"game");Directory.CreateDirectory(game);var modRoot=Path.Combine(root,"plugin");Directory.CreateDirectory(modRoot);
        await db.UpsertModAsync(new("p","Plugin","Plugin",modRoot,true,1),TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("p",[ModFile("p",@"nativePC\plugins\thing.dll","h",FileClass.Plugin)],TestContext.Current.CancellationToken);
        var result=await new DependencyDoctorService(db,game).ScanAsync(TestContext.Current.CancellationToken);
        Assert.Single(result);Assert.False(result[0].Ready);Assert.NotEmpty(result[0].Missing);
    }

    [Fact]
    public async Task ModTrustAccumulatesLaunchHistory()
    {
        var db=await CreateDbAsync("trust.db");await SeedModAsync(db,"a","A");var trust=new ModTrustService(db);
        await trust.RecordLaunchAsync(["a"],true,false,TestContext.Current.CancellationToken);await trust.RecordLaunchAsync(["a"],false,true,TestContext.Current.CancellationToken);
        var state=await trust.GetAsync("a",TestContext.Current.CancellationToken);Assert.NotNull(state);Assert.Equal(1,state!.SuccessfulLaunches);Assert.Equal(1,state.FailedLaunches);Assert.Equal(1,state.Rollbacks);
    }


    [Fact]
    public async Task AutoCategoryStartupScanToleratesRootDocumentationBeforeCapture()
    {
        var db = await CreateDbAsync("category-startup.db");
        var source = Path.Combine(root, "Uncaptured Armor");
        var armorDir = Path.Combine(source, "nativePC", "pl", "f_equip", "pl001_0000");
        Directory.CreateDirectory(armorDir);
        await File.WriteAllTextAsync(Path.Combine(source, "Troubleshootings.txt"), "notes", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(armorDir, "f_body.mod3"), "model", TestContext.Current.CancellationToken);
        await db.UpsertModAsync(new("uncaptured", "Uncaptured Armor", "Uncaptured Armor", source, true, 1), TestContext.Current.CancellationToken);

        var assigned = await new AutoCategoryService(db).AssignMissingAsync(TestContext.Current.CancellationToken);
        var mod = Assert.Single(await db.GetModsAsync(TestContext.Current.CancellationToken), x => x.Id == "uncaptured");

        Assert.Equal(1, assigned);
        Assert.Equal(AutomationCategory.Armor.ToString(), mod.Category);
    }


    [Fact]
    public async Task ManagerLogicalFileNameGroupsPackagesWithoutBrandSpecificRules()
    {
        var db=await CreateDbAsync("manager-family.db");
        var state=Path.Combine(root,"manager-family-state");Directory.CreateDirectory(state);
        var a=Path.Combine(root,"Generic Pack Base");var b=Path.Combine(root,"Generic Pack Component");
        Directory.CreateDirectory(a);Directory.CreateDirectory(b);
        await File.WriteAllTextAsync(Path.Combine(a,"meta.ini"),"logicalFileName=Generic Outfit Suite\nversion=1.0",TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(b,"meta.ini"),"logicalFileName=Generic Outfit Suite\nversion=1.0",TestContext.Current.CancellationToken);
        await db.UpsertModAsync(new("generic-a","Generic Pack Base","Generic Pack Base",a,true,1),TestContext.Current.CancellationToken);
        await db.UpsertModAsync(new("generic-b","Generic Pack Component","Generic Pack Component",b,true,2),TestContext.Current.CancellationToken);

        await new MhwModManager.Filesystem.NexusMetadataService(db,new PlannerSnapshotRepository(db),state).RefreshAsync(TestContext.Current.CancellationToken);
        var mods=await db.GetModsAsync(TestContext.Current.CancellationToken);
        var first=Assert.Single(mods,m=>m.Id=="generic-a");var second=Assert.Single(mods,m=>m.Id=="generic-b");
        Assert.False(string.IsNullOrWhiteSpace(first.FamilyId));
        Assert.Equal(first.FamilyId,second.FamilyId);
        Assert.StartsWith("manager:",first.FamilyId!,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SmartInboxImportsDirectoryAndLeavesProcessedCopy()
    {
        var db=await CreateDbAsync("inbox.db");var mods=Path.Combine(root,"Mods");var inbox=Path.Combine(root,"Inbox");var state=Path.Combine(root,"state");Directory.CreateDirectory(inbox);
        var source=Path.Combine(inbox,"Sample Armor");Directory.CreateDirectory(Path.Combine(source,"nativePC","pl","f_equip"));await File.WriteAllTextAsync(Path.Combine(source,"Troubleshootings.txt"),"notes",TestContext.Current.CancellationToken);await File.WriteAllTextAsync(Path.Combine(source,"nativePC","pl","f_equip","x.mod3"),"x",TestContext.Current.CancellationToken);
        var hash=new MhwModManager.Filesystem.HashingService();var blobs=new MhwModManager.Filesystem.BlobStore(Path.Combine(state,"Blobs"),db);var scanner=new MhwModManager.Filesystem.ModScanner(db,blobs,hash);var catalog=new MhwModManager.Filesystem.CatalogService(db,scanner,mods);
        var categories=new AutoCategoryService(db);var nexus=new MhwModManager.Filesystem.NexusMetadataService(db,new PlannerSnapshotRepository(db),state);
        var service=new SmartInboxService(db,new MhwModManager.Filesystem.ArchiveInspector(),catalog,nexus,categories,inbox,mods);
        var result=await service.ProcessAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1,result.Imported);Assert.True(Directory.Exists(Path.Combine(mods,"Sample Armor")));Assert.True(Directory.Exists(Path.Combine(inbox,"Processed")));
    }

    [Fact]
    public async Task SmartInboxDirectDirectoryRejectsDescendantJunctionWithoutPublishingPartialMod()
    {
        var db=await CreateDbAsync("inbox-reparse.db");
        var mods=Path.Combine(root,"Mods-reparse");
        var inbox=Path.Combine(root,"Inbox-reparse");
        var state=Path.Combine(root,"state-reparse");
        Directory.CreateDirectory(inbox);
        var source=Path.Combine(inbox,"Unsafe Pack");
        Directory.CreateDirectory(Path.Combine(source,"nativePC"));
        await File.WriteAllTextAsync(Path.Combine(source,"nativePC","safe.tex"),"SAFE",TestContext.Current.CancellationToken);
        var external=Path.Combine(root,"inbox-reparse-external");
        Directory.CreateDirectory(external);
        var sentinel=Path.Combine(external,"outside.tex");
        await File.WriteAllTextAsync(sentinel,"OUTSIDE",TestContext.Current.CancellationToken);
        CreateDirectoryJunction(Path.Combine(source,"escape"),external);

        var hash=new MhwModManager.Filesystem.HashingService();
        var blobs=new MhwModManager.Filesystem.BlobStore(Path.Combine(state,"Blobs"),db);
        var scanner=new MhwModManager.Filesystem.ModScanner(db,blobs,hash);
        var catalog=new MhwModManager.Filesystem.CatalogService(db,scanner,mods);
        var categories=new AutoCategoryService(db);
        var nexus=new MhwModManager.Filesystem.NexusMetadataService(db,new PlannerSnapshotRepository(db),state);
        var service=new SmartInboxService(db,new MhwModManager.Filesystem.ArchiveInspector(),catalog,nexus,categories,inbox,mods);

        var result=await service.ProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0,result.Imported);
        Assert.Equal(1,result.Skipped);
        Assert.True(Directory.Exists(source));
        Assert.False(Directory.Exists(Path.Combine(mods,"Unsafe Pack")));
        Assert.Empty(await db.GetModsAsync(TestContext.Current.CancellationToken));
        Assert.Equal("OUTSIDE",await File.ReadAllTextAsync(sentinel,TestContext.Current.CancellationToken));
    }


    [Fact]
    public async Task SmartInboxRejectsTopLevelReparseItemWithoutPublishing()
    {
        var db=await CreateDbAsync("inbox-root-reparse.db");
        var mods=Path.Combine(root,"Mods-root-reparse");
        var inbox=Path.Combine(root,"Inbox-root-reparse");
        var state=Path.Combine(root,"state-root-reparse");
        Directory.CreateDirectory(inbox);
        var external=Path.Combine(root,"inbox-root-reparse-external");
        Directory.CreateDirectory(Path.Combine(external,"nativePC"));
        var sentinel=Path.Combine(external,"nativePC","outside.tex");
        await File.WriteAllTextAsync(sentinel,"OUTSIDE",TestContext.Current.CancellationToken);
        var source=Path.Combine(inbox,"Linked Pack");
        CreateDirectoryJunction(source,external);

        var hash=new MhwModManager.Filesystem.HashingService();
        var blobs=new MhwModManager.Filesystem.BlobStore(Path.Combine(state,"Blobs"),db);
        var scanner=new MhwModManager.Filesystem.ModScanner(db,blobs,hash);
        var catalog=new MhwModManager.Filesystem.CatalogService(db,scanner,mods);
        var categories=new AutoCategoryService(db);
        var nexus=new MhwModManager.Filesystem.NexusMetadataService(db,new PlannerSnapshotRepository(db),state);
        var service=new SmartInboxService(db,new MhwModManager.Filesystem.ArchiveInspector(),catalog,nexus,categories,inbox,mods);

        var result=await service.ProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0,result.Imported);
        Assert.Equal(1,result.Skipped);
        Assert.True(Directory.Exists(source));
        Assert.False(Directory.Exists(Path.Combine(mods,"Linked Pack")));
        Assert.Empty(await db.GetModsAsync(TestContext.Current.CancellationToken));
        Assert.Equal("OUTSIDE",await File.ReadAllTextAsync(sentinel,TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SmartInboxReparseCycleFailsBoundedWithoutPublishing()
    {
        var db=await CreateDbAsync("inbox-cycle.db");
        var mods=Path.Combine(root,"Mods-cycle");
        var inbox=Path.Combine(root,"Inbox-cycle");
        var state=Path.Combine(root,"state-cycle");
        Directory.CreateDirectory(inbox);
        var source=Path.Combine(inbox,"Cyclic Pack");
        Directory.CreateDirectory(Path.Combine(source,"nativePC"));
        await File.WriteAllTextAsync(Path.Combine(source,"nativePC","safe.tex"),"SAFE",TestContext.Current.CancellationToken);
        CreateDirectoryJunction(Path.Combine(source,"loop"),source);

        var hash=new MhwModManager.Filesystem.HashingService();
        var blobs=new MhwModManager.Filesystem.BlobStore(Path.Combine(state,"Blobs"),db);
        var scanner=new MhwModManager.Filesystem.ModScanner(db,blobs,hash);
        var catalog=new MhwModManager.Filesystem.CatalogService(db,scanner,mods);
        var categories=new AutoCategoryService(db);
        var nexus=new MhwModManager.Filesystem.NexusMetadataService(db,new PlannerSnapshotRepository(db),state);
        var service=new SmartInboxService(db,new MhwModManager.Filesystem.ArchiveInspector(),catalog,nexus,categories,inbox,mods);

        var result=await service.ProcessAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken);

        Assert.Equal(0,result.Imported);
        Assert.Equal(1,result.Skipped);
        Assert.True(Directory.Exists(source));
        Assert.False(Directory.Exists(Path.Combine(mods,"Cyclic Pack")));
        Assert.Empty(await db.GetModsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SmartInboxSafeTreePreservesLegacyClassificationOrder()
    {
        var db=await CreateDbAsync("inbox-order.db");
        var mods=Path.Combine(root,"Mods-order");
        var inbox=Path.Combine(root,"Inbox-order");
        var state=Path.Combine(root,"state-order");
        Directory.CreateDirectory(inbox);
        var source=Path.Combine(inbox,"Ordering Pack");
        Directory.CreateDirectory(Path.Combine(source,"A"));
        Directory.CreateDirectory(Path.Combine(source,"B"));
        await File.WriteAllTextAsync(Path.Combine(source,"A","one.tex"),"ONE",TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(source,"A","two.tex"),"TWO",TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(source,"B","npc.bin"),"NPC",TestContext.Current.CancellationToken);

        var dbCategories=new AutoCategoryService(db);
        var legacyCategory=dbCategories.Classify(
            Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories)
                .Select(x=>Path.GetRelativePath(source,x)));
        Assert.Equal(AutomationCategory.Texture,legacyCategory);

        var hash=new MhwModManager.Filesystem.HashingService();
        var blobs=new MhwModManager.Filesystem.BlobStore(Path.Combine(state,"Blobs"),db);
        var scanner=new MhwModManager.Filesystem.ModScanner(db,blobs,hash);
        var catalog=new MhwModManager.Filesystem.CatalogService(db,scanner,mods);
        var nexus=new MhwModManager.Filesystem.NexusMetadataService(db,new PlannerSnapshotRepository(db),state);
        var service=new SmartInboxService(db,new MhwModManager.Filesystem.ArchiveInspector(),catalog,nexus,dbCategories,inbox,mods);

        var result=await service.ProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1,result.Imported);
        Assert.Equal(legacyCategory,Assert.Single(result.Items).Category);
    }

    [Fact]
    public async Task GpuIssueFallbackRanksNewTextureModFromLastLaunch()
    {
        var db=await CreateDbAsync("gpu-issues.db");
        await SeedModAsync(db,"stable","Stable Armor");
        await SeedModAsync(db,"skin","New Skin Pack");
        await db.ReplaceModFilesAsync("stable",[ModFile("stable",@"nativePC\pl\f_equip\x.mod3","a",FileClass.Structural)],TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("skin",Enumerable.Range(0,12).Select(i=>ModFile("skin",$@"nativePC\pl\f_equip\mod_body\skin{i}.tex","t{i}",FileClass.Texture)).ToArray(),TestContext.Current.CancellationToken);
        var previous=DateTimeOffset.UtcNow.AddMinutes(-10);var latest=DateTimeOffset.UtcNow.AddMinutes(-1);
        await InsertLaunchAsync(db,"good",previous,true,new Dictionary<string,ModState>{{"stable",new(true,1)},{"skin",new(false,2)}});
        await InsertLaunchAsync(db,"gpu",latest,true,new Dictionary<string,ModState>{{"stable",new(true,1)},{"skin",new(true,2)}});
        var trust=new ModTrustService(db);for(var i=0;i<4;i++)await trust.RecordLaunchAsync(["stable"],true,false,TestContext.Current.CancellationToken);
        var service=new ModIssueFallbackService(db,new ChangeTimelineService(db),trust);
        var result=await service.AnalyzeLatestLaunchAsync(ModIssueKind.GpuGraphicsCrash,TestContext.Current.CancellationToken);
        var suspect=Assert.Single(result.Suspects);
        Assert.Equal("skin",suspect.ModId);
        Assert.True(suspect.Score>=70);
        Assert.Contains("texture",suspect.Reason,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartupFailureMarksNewPluginAndSuccessfulLaunchClearsUnconfirmedMark()
    {
        var db=await CreateDbAsync("startup-issues.db");
        await SeedModAsync(db,"base","Base");await SeedModAsync(db,"plugin","New Plugin");
        await db.ReplaceModFilesAsync("plugin",[ModFile("plugin",@"nativePC\plugins\thing.dll","p",FileClass.Plugin)],TestContext.Current.CancellationToken);
        var previous=DateTimeOffset.UtcNow.AddMinutes(-10);var failed=DateTimeOffset.UtcNow.AddMinutes(-1);
        await InsertLaunchAsync(db,"good2",previous,true,new Dictionary<string,ModState>{{"base",new(true,1)},{"plugin",new(false,2)}});
        await InsertLaunchAsync(db,"bad2",failed,false,new Dictionary<string,ModState>{{"base",new(true,1)},{"plugin",new(true,2)}});
        var trust=new ModTrustService(db);var service=new ModIssueFallbackService(db,new ChangeTimelineService(db),trust);
        var result=await service.RecordLaunchFailureAsync("bad2",ModIssueKind.StartupCrash,TestContext.Current.CancellationToken);
        Assert.Equal("plugin",Assert.Single(result.Suspects).ModId);
        Assert.Single(await service.GetActiveAsync(TestContext.Current.CancellationToken));
        await service.RecordSuccessfulLaunchAsync(["base","plugin"],TestContext.Current.CancellationToken);
        Assert.Empty(await service.GetActiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BisectIssueMarkRemainsConfirmedAcrossNormalStartupSuccess()
    {
        var db=await CreateDbAsync("bisect-issues.db");await SeedModAsync(db,"culprit","Culprit");
        var service=new ModIssueFallbackService(db,new ChangeTimelineService(db),new ModTrustService(db));
        await service.MarkBisectResultAsync(["culprit"],"launch-x",TestContext.Current.CancellationToken);
        await service.RecordSuccessfulLaunchAsync(["culprit"],TestContext.Current.CancellationToken);
        var issue=Assert.Single(await service.GetActiveAsync(TestContext.Current.CancellationToken));
        Assert.True(issue.Confirmed);Assert.Equal(99,issue.Score);
    }

    private static async Task InsertLaunchAsync(ManagerDatabase db,string id,DateTimeOffset started,bool success,IReadOnlyDictionary<string,ModState> state)
    {
        var json=JsonSerializer.Serialize(state,TestJsonOptions);
        await db.ExecuteAsync("INSERT INTO launch_history(id,started_at,ended_at,mode,success,startup_survived,state_json,details) VALUES($i,$s,$e,'Modded',$ok,$ok,$j,'test')",new Dictionary<string,object?>{{"$i",id},{"$s",started.ToString("O",System.Globalization.CultureInfo.InvariantCulture)},{"$e",started.AddSeconds(15).ToString("O",System.Globalization.CultureInfo.InvariantCulture)},{"$ok",success?1:0},{"$j",json}},TestContext.Current.CancellationToken);
    }

    private static void CreateDirectoryJunction(string link,string target)
    {
        var info=new ProcessStartInfo("cmd.exe",$"/d /c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute=false,
            RedirectStandardOutput=true,
            RedirectStandardError=true,
            CreateNoWindow=true
        };
        using var process=Process.Start(info)??throw new InvalidOperationException("Could not start cmd.exe to create junction.");
        process.WaitForExit();
        if(process.ExitCode!=0)
            throw new IOException($"Could not create test junction '{link}' -> '{target}'. Exit={process.ExitCode}; stdout={process.StandardOutput.ReadToEnd()}; stderr={process.StandardError.ReadToEnd()}");
        Assert.True((File.GetAttributes(link)&FileAttributes.ReparsePoint)!=0);
    }

    private async Task<ManagerDatabase> CreateDbAsync(string name){var db=new ManagerDatabase(Path.Combine(root,name));await db.InitializeAsync(TestContext.Current.CancellationToken);return db;}
    private async Task SeedModAsync(ManagerDatabase db,string id,string name)=>await db.UpsertModAsync(new(id,name,name,Path.Combine(root,id),true,1),TestContext.Current.CancellationToken);
    private static ModFileDescriptor ModFile(string mod,string path,string sha,FileClass cls)=>new(mod,path,sha,null,1,DateTimeOffset.UtcNow,cls);
    public void Dispose(){try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}GC.SuppressFinalize(this);}
}
