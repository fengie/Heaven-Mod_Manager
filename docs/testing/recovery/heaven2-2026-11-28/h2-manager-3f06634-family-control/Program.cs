using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
var trial=Path.GetFullPath(args[0]);
var home=Path.GetFullPath(args[1]);
Environment.SetEnvironmentVariable("MHW_MASTER_DEBUG_ROOT",Path.Combine(trial,"Debug"));
var physical=new GameProfileRegistry(Path.Combine(trial,"State")).Load().Single(x=>x.IsMonsterHunterWorld);
var game=physical with {GameRoot=Path.Combine(trial,"SentinelGame")};
Directory.CreateDirectory(game.GameRoot);
File.WriteAllText(Path.Combine(game.GameRoot,"untouched.txt"),"sentinel");
var originals=GameModStorage.Resolve(home,physical).InstalledRoot;
var source=Directory.EnumerateDirectories(originals)
    .Single(folder=>Path.GetFileName(folder).StartsWith("Original Handler Outfit replacer",StringComparison.OrdinalIgnoreCase));
var library=Path.Combine(trial,"ScratchMods");
var root=Path.Combine(library,"Original Handler Outfit replacer");
Directory.CreateDirectory(root);
var original=SafeRecursiveTraversal.Snapshot(source);
foreach(var p in original.Directories)Directory.CreateDirectory(Path.Combine(root,Path.GetRelativePath(source,p)));
foreach(var p in original.Files)
{
    var to=Path.Combine(root,Path.GetRelativePath(source,p));
    File.Copy(p,to);
    string Hash(string f){using var s=File.OpenRead(f);return Convert.ToHexString(SHA256.HashData(s));}
    if(Hash(p)!=Hash(to))throw new InvalidDataException("Scratch file wasn't copied exactly");
}
var state=Path.Combine(trial,"State","scratch");
Directory.CreateDirectory(state);
var db=new ManagerDatabase(Path.Combine(state,"manager.db"));
await db.InitializeAsync();
var scanner=new ModScanner(db,new BlobStore(Path.Combine(trial,"Cas"),db),new HashingService(),game);
var catalog=new CatalogService(db,scanner,library);
var records=new List<object>();
async Task View(string phase,bool shouldCapture)
{
    await catalog.RefreshFoldersAsync();
    var mods=(await db.GetModsAsync()).Where(m=>m.FamilyId?.StartsWith("authored:")==true).ToArray();
    var raw=AuthoredPackageFamily.Read(root,game,default);
    var optional=mods.SingleOrDefault(m=>m.FamilyRole=="Optional");
    string? error=null;
    var captured=Array.Empty<string>();
    if(shouldCapture&&optional!=null)
        try{ captured=(await scanner.CaptureAsync(optional)).Select(f=>f.Path).ToArray(); }
        catch(Exception ex) when(ex is IOException or InvalidDataException) {error=ex.GetType().Name;}
    records.Add(new{phase,FreshParts=raw.Count,RegisteredParts=mods.Length,VisibleOptional=optional!=null,error,captured,
        FilesystemSafe=Directory.GetFiles(game.GameRoot).Select(Path.GetFileName).SequenceEqual(["untouched.txt"])});
    Console.WriteLine($"{phase}: fresh {raw.Count}, retained {mods.Length}, captured {captured.Length}, error {error}");
}
await View("exact-real-copy",true);
var option=Path.Combine(root,"Optional File - No Stockings");
var away=Path.Combine(trial,"Offline");
Directory.Move(option,away);
await View("optional-folder-offline",true);
Directory.Move(away,option);
await View("optional-restored",true);
var inserted=Path.Combine(option,"nativePC","unverified-optional.mod3");
File.WriteAllText(inserted,"Disposable altered payload.");
await View("optional-no-longer-subset",true);
File.Delete(inserted);
await View("optional-original-restored",true);
var json=JsonSerializer.Serialize(records);
File.WriteAllText(Path.Combine(trial,"control-proof.json"),json);
