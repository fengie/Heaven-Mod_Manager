using System.Globalization;
using System.Text.Json;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.SelfTest;

internal static class Program
{
    private sealed record CaseResult(string Name,bool Passed,double Milliseconds,string? Error,string? Detail);
    private static readonly List<CaseResult> Results=[];
    private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true};

    public static async Task<int> Main(string[] args)
    {
        var reportRoot=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(Environment.CurrentDirectory,"BuildLogs");Directory.CreateDirectory(reportRoot);
        var stamp=DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture);
        var temp=Path.Combine(Path.GetTempPath(),"MhwManagerSelfTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            await Run("Category classifier",()=>{if(AutoCategoryService.ClassifyPaths([@"nativePC\pl\f_equip\x.mod3"])!=AutomationCategory.Armor)throw new InvalidOperationException("Armor category was not inferred.");return Task.CompletedTask;});
            await Run("Crash bisector",async()=>{var r=await new CrashBisectorEngine().RunAsync(["one","two","three"],(set,_)=>Task.FromResult(set.Contains("two")));if(!r.Isolated||r.Suspects.Single()!="two")throw new InvalidOperationException(r.Message);});
            await Run("SQLite schema",async()=>{var db=await Db("schema.db");if(await db.IntegrityCheckAsync()!="ok")throw new InvalidOperationException("SQLite integrity check failed.");});
            await Run("Timeline persistence",async()=>{var db=await Db("timeline.db");var t=new ChangeTimelineService(db);await t.RecordAsync("selftest",AutomationSeverity.Info,"hello");var rows=await t.RecentAsync();if(rows.Count!=1||rows[0].Message!="hello")throw new InvalidOperationException("Timeline event did not round-trip.");});
            await Run("Last-known-good round trip",async()=>{var db=await Db("lkg.db");var svc=new LastKnownGoodService(db);await svc.RecordAsync(null);var state=await svc.LoadAsync();if(state is null)throw new InvalidOperationException("Last-known-good state did not round-trip.");});
            await Run("Save snapshot",async()=>{var db=await Db("backup.db");var save=Path.Combine(temp,"SAVEDATA1000");await File.WriteAllTextAsync(save,"selftest");var old=Environment.GetEnvironmentVariable("MHW_SAVE_PATH");try{Environment.SetEnvironmentVariable("MHW_SAVE_PATH",save);var result=await new SaveBackupService(db,Path.Combine(temp,"state"),GameProfile.MonsterHunterWorld(Path.Combine(temp,"game"))).CreateAsync("selftest");if(!result.Success||result.FilesCopied!=1)throw new InvalidOperationException(result.Message);}finally{Environment.SetEnvironmentVariable("MHW_SAVE_PATH",old);}});
            await Run("Update diff",async()=>{var db=await Db("diff.db");await Seed(db,"old","Old");await Seed(db,"new","New");await db.ReplaceModFilesAsync("old",[ModFile("old",@"nativePC\x.mod3","a",FileClass.Structural)]);await db.ReplaceModFilesAsync("new",[ModFile("new",@"nativePC\x.mod3","b",FileClass.Structural),ModFile("new",@"nativePC\y.tex","c",FileClass.Texture)]);var d=await new UpdateDiffService(db).CompareAsync("old","new");if(d.Changed!=1||d.Added!=1||d.StructuralChanged!=1)throw new InvalidOperationException("Update diff counts were wrong.");});
            await Run("Duplicate detector",async()=>{var db=await Db("dupe.db");var a=Path.Combine(temp,"A");var b=Path.Combine(temp,"B");Directory.CreateDirectory(a);Directory.CreateDirectory(b);await db.UpsertModAsync(new("a","A","A",a,false,1));await db.UpsertModAsync(new("b","B","B",b,false,2));await db.ReplaceModFilesAsync("a",[ModFile("a",@"nativePC\same.tex","h",FileClass.Texture)]);await db.ReplaceModFilesAsync("b",[ModFile("b",@"nativePC\same.tex","h",FileClass.Texture)]);var d=await new DuplicateCleanupService(db,Path.Combine(temp,"archive")).AnalyzeAsync();if(d.ExactDuplicates.Count!=1)throw new InvalidOperationException("Exact duplicate group was not detected.");});
            await Run("Recipe export",async()=>{var db=await Db("recipe.db");var path=Path.Combine(temp,"recipe.json");await new CollectionRecipeService(db).ExportAsync(path);using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(path));if(doc.RootElement.GetProperty("format").GetInt32()!=1)throw new InvalidOperationException("Recipe format missing.");});
            await Run("Effective provider inspector",async()=>{var db=await Db("provider.db");await Seed(db,"a","A");await Seed(db,"b","B");await db.ReplaceModFilesAsync("a",[ModFile("a",@"nativePC\same.tex","a1",FileClass.Texture)]);await db.ReplaceModFilesAsync("b",[ModFile("b",@"nativePC\same.tex","b1",FileClass.Texture)]);await db.ExecuteAsync("INSERT INTO deployment_manifest(path,provider_mod_id,blob_sha256,expected_live_sha256,rule_id,deployed_at) VALUES($p,$m,$b,$e,NULL,$t)",new Dictionary<string,object?>{{"$p",@"nativePC\same.tex"},{"$m","b"},{"$b","b1"},{"$e","b1"},{"$t",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)}});var e=await new EffectiveInspectorService(new PlannerSnapshotRepository(db)).ExplainAsync(@"nativePC\same.tex");if(e?.EffectiveModId!="b"||!e.ShadowedModIds.Contains("a"))throw new InvalidOperationException("Provider provenance was wrong.");});
            await Run("Outfit preset inference",()=>{var family=new LogicalModFamily("f","HPN","HPN",[new("m","HPN","HPN","m",false,1),new("w","HPN - Skimpy Waist","HPN - Skimpy Waist","w",false,2)],null);var presets=new OutfitPresetService().Build(family);if(!presets.Any(x=>x.Name=="Skimpy"))throw new InvalidOperationException("Skimpy preset was not inferred.");return Task.CompletedTask;});
        }
        finally { try{Directory.Delete(temp,true);}catch(IOException){}catch(UnauthorizedAccessException){} }
        var jsonPath=Path.Combine(reportRoot,"selftest-"+stamp+".json");var mdPath=Path.Combine(reportRoot,"selftest-"+stamp+".md");
        await File.WriteAllTextAsync(jsonPath,JsonSerializer.Serialize(new{generatedAt=DateTimeOffset.Now,total=Results.Count,passed=Results.Count(x=>x.Passed),failed=Results.Count(x=>!x.Passed),results=Results},JsonOptions));
        var lines=new List<string>{"# MHW Mod Manager self-test","","Generated: "+DateTimeOffset.Now.ToString("O",CultureInfo.InvariantCulture),"",$"**Result: {Results.Count(x=>x.Passed)}/{Results.Count} passed**","","| Test | Result | ms | Detail |","|---|---:|---:|---|"};
        lines.AddRange(Results.Select(r=>"| "+Escape(r.Name)+" | "+(r.Passed?"PASS":"FAIL")+" | "+r.Milliseconds.ToString("F1",CultureInfo.InvariantCulture)+" | "+Escape(r.Passed?r.Detail??"":r.Error??"")+" |"));
        await File.WriteAllLinesAsync(mdPath,lines);
        Console.WriteLine("SELFTEST REPORT: "+mdPath);Console.WriteLine("SELFTEST JSON: "+jsonPath);
        return Results.Any(x=>!x.Passed)?2:0;

        async Task<ManagerDatabase> Db(string name){var db=new ManagerDatabase(Path.Combine(temp,name));await db.InitializeAsync();return db;}
        async Task Seed(ManagerDatabase db,string id,string name)=>await db.UpsertModAsync(new(id,name,name,Path.Combine(temp,id),true,1));
    }

    private static ModFileDescriptor ModFile(string mod,string path,string sha,FileClass cls)=>new(mod,path,sha,null,1,DateTimeOffset.UtcNow,cls);
    private static async Task Run(string name,Func<Task> body){var sw=System.Diagnostics.Stopwatch.StartNew();try{await body();sw.Stop();Results.Add(new(name,true,sw.Elapsed.TotalMilliseconds,null,"ok"));Console.WriteLine("PASS: "+name);}catch(Exception ex){sw.Stop();Results.Add(new(name,false,sw.Elapsed.TotalMilliseconds,ex.ToString(),null));Console.WriteLine("FAIL: "+name+" -> "+ex.Message);}}
    private static string Escape(string value)=>value.Replace("|","\\|",StringComparison.Ordinal).Replace("\r"," ",StringComparison.Ordinal).Replace("\n"," ",StringComparison.Ordinal);
}
