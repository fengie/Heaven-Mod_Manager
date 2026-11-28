using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

var trial=Path.GetFullPath(args[0]);
var home=Path.GetFullPath(args[1]);
Environment.SetEnvironmentVariable("MHW_MASTER_DEBUG_ROOT",Path.Combine(trial,"Debug"));
var games=new GameProfileRegistry(Path.Combine(trial,"State")).Load();
using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
var ct=timeout.Token;
var service=new GameModIntakeService(home,Path.Combine(trial,"State"),()=>games,new ArchiveInspector());
var results=new List<object>();
string Sha(string path){using var s=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(s));}
string Seal(string path)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
    SafeRecursiveTraversal.Snapshot(path,ct).Files.Order().Select(f=>new{Path=Path.GetRelativePath(path,f),Hash=Sha(f)}))));
var resultsPath=Path.Combine(trial,"family-candidate-proof.json");
void Flush()=>File.WriteAllText(resultsPath,JsonSerializer.Serialize(results));
foreach(var (id,title) in new[]{("sifu","Alternative Camera v0.9.2")})
{
    var game=games.Single(g=>g.Id==id);
    var roots=GameModStorage.Resolve(home,game);
    var dbPath=Path.Combine(trial,"Baseline",id,"manager.db");
    var db=new ManagerDatabase(dbPath);
    await db.InitializeAsync(ct);
    var snapRepo=new PlannerSnapshotRepository(db);
    var initial=await snapRepo.LoadAsync(ct);
    var source=(await db.GetModsAsync(true,ct)).Single(m=>m.DisplayName==title);
    var sourceSeal=Seal(source.SourcePath);
    var read=AuthoredPackageFamily.Read(source.SourcePath,game,ct);
    var targetScope=id=="monster-hunter-world"?source.SourcePath:Path.GetDirectoryName(source.SourcePath)!;
    var engine=new ConflictEngine(game);
    var planner=new DeploymentPlanner(engine,game);
    var scratch=Path.Combine(trial,"Scratch",id);
    var blobs=new BlobStore(Path.Combine(trial,"ScratchCas",id),db);
    var scanner=new ModScanner(db,blobs,new HashingService(),game);
    CatalogService Catalog(ManagerDatabase d,ModScanner s)=>new(d,s,scratch,roots.InstalledRoot,
        (dir,c)=>id=="sifu"&&PathRules.Comparer.Equals(dir,targetScope)?service.SelectableManagedFolders(dir,game,c):[],
        (dir,c)=>id=="sifu"&&PathRules.Comparer.Equals(dir,targetScope)?service.InspectManagedShelf(dir,game,c):throw new IOException("Outside scoped audit."));
    var clock=Stopwatch.StartNew();
    var catalog=Catalog(db,scanner);
    await catalog.RefreshFoldersAsync(ct);
    var firstMs=clock.ElapsedMilliseconds;
    var first=await snapRepo.LoadAsync(ct);
    var all=await db.GetModsAsync(true,ct);
    var members=all.Where(m=>m.FamilyId?.StartsWith("authored:",StringComparison.Ordinal)==true&&
       (PathRules.Comparer.Equals(m.SourcePath,source.SourcePath)||m.SourcePath.StartsWith(source.SourcePath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))).ToArray();
    var ordinary=LogicalModFamilies.Build(first.Mods,first.Files,game).Where(f=>f.Members.Any(m=>members.Any(x=>x.Id==m.Id))).ToArray();
    clock.Restart();
    var reopenedDb=new ManagerDatabase(dbPath);
    await reopenedDb.InitializeAsync(ct);
    var reopenedScanner=new ModScanner(reopenedDb,new BlobStore(Path.Combine(trial,"ScratchCas",id),reopenedDb),new HashingService(),game);
    await Catalog(reopenedDb,reopenedScanner).RefreshFoldersAsync(ct);
    var repeatMs=clock.ElapsedMilliseconds;
    var reopened=(await reopenedDb.GetModsAsync(true,ct)).Where(m=>members.Any(a=>a.Id==m.Id)).ToArray();
    var choices=ordinary.SelectMany(f=>new OutfitPresetService().Build(f)).ToArray();
    var planInfo=new List<object>();
    var captures=new List<object>();
    var owners=initial.CurrentManifest.Where(row=>row.Value.ProviderModId==source.Id).ToDictionary(x=>x.Key,x=>x.Value,PathRules.Comparer);
    if(members.Length==2&&id=="monster-hunter-world")
    {
        foreach(var m in members)
        {
            var f=await scanner.CaptureAsync(m,ct);
            var role=m.FamilyRole!;
            var part=read.Single(x=>x.Role==role);
            var match=f.All(x=>File.Exists(Path.Combine(part.SourceDirectory,x.Path)) &&
                       Sha(Path.Combine(part.SourceDirectory,x.Path)).Equals(x.BlobSha256,StringComparison.OrdinalIgnoreCase));
            captures.Add(new{m.DisplayName,role,count=f.Count,match,wrapped=f.Count(m=>m.Path.Split(Path.DirectorySeparatorChar).Count(x=>x.Equals("nativePC",StringComparison.OrdinalIgnoreCase))>1)});
        }
        var captured=await snapRepo.LoadAsync(ct);
        var sourceIds=members.Select(m=>m.Id).ToHashSet();
        var filtered=captured with{Mods=captured.Mods.Where(m=>sourceIds.Contains(m.Id)).ToArray(),
            Files=captured.Files.Where(m=>sourceIds.Contains(m.ModId)).ToArray(),CurrentManifest=owners};
        foreach(var choice in new[]{"Main","Main+Optional","Optional"})
        {
            var proposed=filtered with{Mods=filtered.Mods.Select(m=>m with{Enabled=choice.Split('+').Contains(m.FamilyRole)}).ToArray()};
            var plan=planner.Build(proposed);
            var target=plan.Changes.Where(c=>c.Path.EndsWith("npc002.mod3",StringComparison.OrdinalIgnoreCase))
                .Select(c=>new{c.Path,c.Kind,c.ProviderAfter,c.AfterBlobSha256});
            planInfo.Add(new{choice,plan.IsBlocked,changes=plan.Changes.GroupBy(x=>x.Kind.ToString()).ToDictionary(x=>x.Key,x=>x.Count()),
                conflicts=plan.Conflicts.Select(x=>new{x.ReasonCode,x.Blocking}).Distinct(),target});
        }
    }
        if (id=="sifu")
    {
        foreach(var part in members)
        {
            var originalOption=read.Single(f=>f.Label==part.DisplayName);
            string? blocked=null;
            try{
                var indexed=await scanner.CaptureAsync(part,ct);
                bool exact=indexed.All(t=>Sha(Path.Combine(originalOption.SourceDirectory,Path.GetFileName(t.Path))).Equals(t.BlobSha256,StringComparison.OrdinalIgnoreCase));
                captures.Add(new{part.Id,part.DisplayName,part.ChoiceGroup,part.FamilyRole,part.Enabled,part.RequiresLayoutReview,Files=indexed.Select(x=>x.Path),exact});
            }
            catch(InvalidDataException e){blocked=e.Message;captures.Add(new{part.Id,part.DisplayName,part.ChoiceGroup,part.FamilyRole,part.Enabled,part.RequiresLayoutReview,blocked});}
        }
        var captured=await snapRepo.LoadAsync(ct);
        var ids=members.Select(m=>m.Id).ToHashSet();
        var subset=captured with{Mods=captured.Mods.Where(m=>ids.Contains(m.Id)).ToArray(),Files=captured.Files.Where(f=>ids.Contains(f.ModId)).ToArray(),CurrentManifest=owners};
        foreach(var selected in new[]{
            new[]{"AltCamera90FOVExplo","AltCamera105FOV1v1","AltCamera135FOV1vX","zNoFixedCamFlood","AltCamera120FOVStairs","zNoLookAt"},
            new[]{"AltCamera90FOVExplo","AltCamera105FOVExplo"},
            new[]{"zNoFixedCamFlood","AltCamera120FOVFlood"}
        })
        {
            var plan=planner.Build(subset with {Mods=subset.Mods.Select(m=>m with{Enabled=selected.Contains(m.DisplayName)}).ToArray()});
            planInfo.Add(new{selected,plan.IsBlocked,Changes=plan.Changes.Select(x=>new{x.Path,x.AfterBlobSha256}),Conflicts=plan.Conflicts.Select(x=>x.ReasonCode).Distinct()});
        }
    }
    var originalStill=first.Mods.SingleOrDefault(m=>m.Id==source.Id);
    var sameSelections=initial.Mods.Where(m=>m.Enabled).Select(m=>m.Id).Order().SequenceEqual(first.Mods.Where(m=>m.Enabled).Select(m=>m.Id).Order());
    var sameManifest=JsonSerializer.Serialize(initial.CurrentManifest)==JsonSerializer.Serialize(first.CurrentManifest);
    var sameInitialFiles=initial.Files.Where(m=>m.ModId==source.Id).SequenceEqual(first.Files.Where(m=>m.ModId==source.Id));
    var sourceUnchanged=sourceSeal==Seal(source.SourcePath);
    results.Add(new{id,title,read=read.Select(p=>new{p.Label,p.Role,p.ChoiceGroup}),members=members.Select(m=>new{m.Id,m.DisplayName,m.Enabled,m.FamilyRole,m.ChoiceGroup}),
        originalBefore=new{source.Id,source.Enabled,source.RequiresLayoutReview}, originalAfter=originalStill is null?null:new{originalStill.Id,originalStill.Enabled,originalStill.RequiresLayoutReview},
        firstMs,repeatMs,owners=owners.Count,sameSelections,sameManifest,sameInitialFiles,sourceUnchanged,
        compare=members.Select(m=>(m.Id,m.FamilyRole,m.ChoiceGroup)).SequenceEqual(reopened.Select(m=>(m.Id,m.FamilyRole,m.ChoiceGroup))),
        families=ordinary.Select(f=>new{f.DisplayName,Count=f.Members.Count}),choices,captures,planInfo});
    Flush();
    Console.WriteLine($"{id}: reader {read.Count}, catalog family members {members.Length}, original manifest {owners.Count}, unchanged enabled={sameSelections}, first {firstMs}ms / reopen {repeatMs}ms");
}
