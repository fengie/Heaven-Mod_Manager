using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MhwModManager.Core;

namespace MhwModManager.Filesystem;

public sealed partial class GameProfileRegistry
{
    private readonly string stateRoot;
    private readonly string registryPath;
    private readonly string activePath;
    private readonly Func<IReadOnlyList<GameDiscoveryCandidate>>? discoveryOverride;
    private readonly object mutationGate=new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web){WriteIndented=true};

    public GameProfileRegistry(string stateRoot):this(stateRoot,null)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
    }

    internal GameProfileRegistry(string stateRoot,Func<IReadOnlyList<GameDiscoveryCandidate>>? discoveryOverride)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        this.stateRoot=Path.GetFullPath(stateRoot);
        registryPath=Path.Combine(this.stateRoot,"games.json");
        activePath=Path.Combine(this.stateRoot,"active-game.txt");
        this.discoveryOverride=discoveryOverride;
    }

    public string RegistryPath=>registryPath;

    public IReadOnlyList<GameProfile> Load()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            if(!File.Exists(registryPath))return Array.Empty<GameProfile>();
            var parsed=JsonSerializer.Deserialize<GameProfile[]>(File.ReadAllText(registryPath),JsonOptions)??Array.Empty<GameProfile>();
            return parsed.Where(IsUsable).DistinctBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x.DisplayName,StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException)
        { MasterDebugLog.Write("GAME-REGISTRY","Could not read game registry; leaving it untouched.",ex);return Array.Empty<GameProfile>(); }
    }

    public GameProfile? GetActive()
    {
        var all=Load();if(all.Count==0)return null;
        try
        {
            if(File.Exists(activePath))
            {
                var id=File.ReadAllText(activePath).Trim();
                var active=all.FirstOrDefault(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase));
                if(active is not null)return active;
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){MasterDebugLog.Write("GAME-REGISTRY","Could not read active-game marker.",ex);}
        return all[0];
    }

    public GameProfile EnsureMonsterHunterWorld(string root)
    {
        var profile=GameProfile.MonsterHunterWorld(root);
        var existing=Load().FirstOrDefault(x=>x.IsMonsterHunterWorld&&Path.GetFullPath(x.GameRoot).Equals(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase));
        if(existing is not null&&File.Exists(existing.ExecutablePath))return existing;
        Upsert(profile);return profile;
    }

    public GameProfile AddGenericFromExecutable(string executablePath)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        lock(mutationGate)
        {
            var profile=CreateFromExecutable(executablePath,null,null,null);Upsert(profile);SetActive(profile.Id);return profile;
        }
    }

    public GameProfile RepairFromExecutable(GameProfile existing,string executablePath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={existing.Id}");
        var full=Path.GetFullPath(executablePath);
        if(!File.Exists(full)||!full.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new FileNotFoundException("Select the game's Windows executable.",full);
        if(existing.IsMonsterHunterWorld&&!Path.GetFileName(full).Equals("MonsterHunterWorld.exe",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Monster Hunter: World profile must point to MonsterHunterWorld.exe.",nameof(executablePath));
        var root=InferGameRoot(full);
        var repaired=existing with
        {
            GameRoot=Path.GetFullPath(root),
            ExecutableRelativePath=GameProfile.NormalizeRelative(Path.GetRelativePath(root,full).Replace('/','\\'),false),
            ProcessName=Path.GetFileNameWithoutExtension(full)
        };
        Upsert(repaired);
        SetActive(repaired.Id);
        MasterDebugLog.Write("GAME-REGISTRY",$"Repaired profile id={repaired.Id}; root={repaired.GameRoot}; exe={repaired.ExecutableRelativePath}");
        return repaired;
    }

    public IReadOnlyList<GameProfile> DiscoverAndRegisterInstalledGames()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var candidates=discoveryOverride?.Invoke()??DiscoverInstalledGameCandidates();
        lock(mutationGate)return RegisterDiscoveredGames(candidates);
    }

    internal IReadOnlyList<GameProfile> RegisterDiscoveredGames(IEnumerable<GameDiscoveryCandidate> candidates)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(candidates);
        var candidateList=candidates
            .Where(x=>!string.IsNullOrWhiteSpace(x.Root))
            .DistinctBy(x=>Path.GetFullPath(x.Root),StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var existing=Load();
        var added=new List<GameProfile>();
        foreach(var item in candidateList)
        {
            try
            {
                var candidateRoot=Path.GetFullPath(item.Root);
                if(existing.Any(x=>Path.GetFullPath(x.GameRoot).Equals(candidateRoot,StringComparison.OrdinalIgnoreCase)))continue;
                var exe=item.Executable;
                if(string.IsNullOrWhiteSpace(exe)||!File.Exists(exe))exe=FindLikelyExecutable(candidateRoot,item.Name);
                if(string.IsNullOrWhiteSpace(exe)||!File.Exists(exe))continue;
                GameProfile profile;
                if(string.Equals(item.SteamAppId,"582010",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(exe).Equals("MonsterHunterWorld.exe",StringComparison.OrdinalIgnoreCase))
                    profile=GameProfile.MonsterHunterWorld(candidateRoot);
                else profile=CreateFromDiscoveredGame(item with{Root=candidateRoot},exe);
                Upsert(profile);added.Add(profile);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or System.Security.SecurityException)
            { MasterDebugLog.Write("GAME-DISCOVERY",$"Skipped discovered game '{item.Name}' at '{item.Root}'.",ex); }
        }
        MasterDebugLog.Write("GAME-DISCOVERY",$"Discovered={candidateList.Length}; registered={added.Count}");
        return added;
    }

    private static List<GameDiscoveryCandidate> DiscoverInstalledGameCandidates()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var candidates=new List<GameDiscoveryCandidate>();
        AddDiscoverySource(candidates,"Steam",DiscoverSteam);
        AddDiscoverySource(candidates,"Epic",DiscoverEpic);
        AddDiscoverySource(candidates,"GOG",DiscoverGog);
        AddDiscoverySource(candidates,"Xbox",DiscoverXbox);
        return candidates;
    }

    private static void AddDiscoverySource(List<GameDiscoveryCandidate> candidates,string source,Func<List<GameDiscoveryCandidate>> discover)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"source={source}");
        try{candidates.AddRange(discover());}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or System.Security.SecurityException)
        {MasterDebugLog.Write("GAME-DISCOVERY",$"{source} discovery failed; continuing with other installed-game sources.",ex);}
    }

    public void Upsert(GameProfile profile)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod($"game={profile.Id}");
        lock(mutationGate)
        {
            if(!IsUsable(profile))throw new ArgumentException("Game profile is invalid or points outside its game root.",nameof(profile));
            var list=Load().Where(x=>!x.Id.Equals(profile.Id,StringComparison.OrdinalIgnoreCase)).Append(profile).OrderBy(x=>x.DisplayName,StringComparer.OrdinalIgnoreCase).ToArray();
            Directory.CreateDirectory(stateRoot);AtomicWrite(registryPath,JsonSerializer.SerializeToUtf8Bytes(list,JsonOptions));
        }
    }

    public void SetActive(string id)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod($"game={id}");
        lock(mutationGate)
        {
            if(!Load().Any(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase)))throw new KeyNotFoundException($"Unknown game profile '{id}'.");
            Directory.CreateDirectory(stateRoot);AtomicWrite(activePath,System.Text.Encoding.UTF8.GetBytes(id.Trim()+Environment.NewLine));
        }
    }

    public static bool IsUsable(GameProfile profile)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            if(!GameProfile.IsCanonicalId(profile.Id)||string.IsNullOrWhiteSpace(profile.DisplayName)||string.IsNullOrWhiteSpace(profile.GameRoot)||!Path.IsPathRooted(profile.GameRoot))return false;
            _=GameProfile.NormalizeRelative(profile.ExecutableRelativePath,false);_=GameProfile.NormalizeRelative(profile.ModRootRelativePath,true);
            var root=Path.GetFullPath(profile.GameRoot).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            var exe=Path.GetFullPath(Path.Combine(profile.GameRoot,profile.ExecutableRelativePath));var live=Path.GetFullPath(profile.LiveModRoot);
            return exe.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&(live+Path.DirectorySeparatorChar).StartsWith(root,StringComparison.OrdinalIgnoreCase);
        }
        catch(Exception ex) when(ex is ArgumentException or NotSupportedException or PathTooLongException){return false;}
    }

    private GameProfile CreateFromExecutable(string executablePath,string? displayName,string? store,string? steamAppId)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        var full=Path.GetFullPath(executablePath);
        if(!File.Exists(full)||!full.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new FileNotFoundException("Select the game's Windows executable.",full);
        var root=InferGameRoot(full);
        return CreateGenericProfile(root,full,displayName,store,steamAppId);
    }

    private GameProfile CreateFromDiscoveredGame(GameDiscoveryCandidate item,string executablePath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"store={item.Store}; name={item.Name}");
        var root=Path.GetFullPath(item.Root);
        var full=Path.GetFullPath(executablePath);
        var prefix=root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
            return CreateFromExecutable(full,item.Name,item.Store,item.SteamAppId);
        return CreateGenericProfile(root,full,item.Name,item.Store,item.SteamAppId);
    }

    private GameProfile CreateGenericProfile(string root,string executablePath,string? displayName,string? store,string? steamAppId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var exe=Path.GetRelativePath(root,executablePath).Replace('/','\\');var name=string.IsNullOrWhiteSpace(displayName)?Path.GetFileNameWithoutExtension(executablePath):displayName.Trim();
        var baseId=GameProfile.NormalizeId(name);var ids=Load().Select(x=>x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);var id=baseId;var suffix=2;while(ids.Contains(id))id=$"{baseId}-{suffix++}";
        var layout=InferLayout(root);
        return GameProfile.Generic(id,name,root,exe,layout.ModRoot,layout.Adapter,steamAppId,null,store);
    }

    private static (string Adapter,string ModRoot) InferLayout(string root)
    {
        if(Directory.Exists(Path.Combine(root,"BepInEx","plugins")))return("bepinex",@"BepInEx\plugins");
        var unreal=FindExisting(root,@"Content\Paks\~mods");if(unreal is not null)return("unreal-paks",unreal);
        try
        {
            foreach(var child in Directory.EnumerateDirectories(root))
            {
                var rel=Path.GetFileName(child);var nested=Path.Combine(rel,"Content","Paks","~mods");
                if(Directory.Exists(Path.Combine(root,nested)))return("unreal-paks",nested);
                var paks=Path.Combine(rel,"Content","Paks");if(Directory.Exists(Path.Combine(root,paks)))return("unreal-paks",paks);
            }
        }catch(UnauthorizedAccessException){}
        var paksRoot=FindExisting(root,@"Content\Paks");if(paksRoot is not null)return("unreal-paks",paksRoot);
        var data=FindExisting(root,"Data","data");if(data is not null)return("data-folder",data);
        var mods=FindExisting(root,"Mods","mods");if(mods is not null)return("mods-folder",mods);
        if(Directory.Exists(Path.Combine(root,"reframework")))return("reframework","reframework");
        return("generic-root",string.Empty);
    }

    private static string InferGameRoot(string executable)
    {
        var parent=Directory.GetParent(executable)?.FullName??throw new InvalidOperationException("The selected executable has no parent directory.");
        var dir=new DirectoryInfo(parent);
        if(dir.Name.Equals("Win64",StringComparison.OrdinalIgnoreCase)&&dir.Parent?.Name.Equals("Binaries",StringComparison.OrdinalIgnoreCase)==true&&dir.Parent.Parent is not null)
            return dir.Parent.Parent.FullName;
        if(dir.Name.Equals("Win32",StringComparison.OrdinalIgnoreCase)&&dir.Parent?.Name.Equals("Binaries",StringComparison.OrdinalIgnoreCase)==true&&dir.Parent.Parent is not null)
            return dir.Parent.Parent.FullName;
        return parent;
    }

    private static string? FindExisting(string root,params string[] relative)=>relative.FirstOrDefault(x=>Directory.Exists(Path.Combine(root,x)));

    private static List<GameDiscoveryCandidate> DiscoverSteam()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})
        {
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
            {
                try
                {
                    using var baseKey=RegistryKey.OpenBaseKey(hive,view);
                    using var h=baseKey.OpenSubKey(@"Software\Valve\Steam");
                    foreach(var valueName in new[]{"SteamPath","InstallPath"})
                    {
                        if(h?.GetValue(valueName) is string p&&Directory.Exists(p))roots.Add(p.Replace('/','\\'));
                    }
                }
                catch(System.Security.SecurityException){}
            }
        }
        var pf=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);if(!string.IsNullOrWhiteSpace(pf)){var p=Path.Combine(pf,"Steam");if(Directory.Exists(p))roots.Add(p);}
        return DiscoverSteamFromRoots(roots);
    }

    internal static List<GameDiscoveryCandidate> DiscoverSteamFromRoots(IEnumerable<string> steamRoots)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(steamRoots);
        var roots=steamRoots.Where(Directory.Exists).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var libraries=new HashSet<string>(roots,StringComparer.OrdinalIgnoreCase);
        foreach(var steam in roots)
        {
            var vdf=Path.Combine(steam,"steamapps","libraryfolders.vdf");if(!File.Exists(vdf))continue;
            try{foreach(Match m in Regex.Matches(File.ReadAllText(vdf),"\\\"path\\\"\\s+\\\"(?<p>[^\\\"]+)\\\""))libraries.Add(m.Groups["p"].Value.Replace("\\\\","\\"));}catch(IOException){}
        }
        var result=new List<GameDiscoveryCandidate>();
        foreach(var lib in libraries)
        {
            var apps=Path.Combine(lib,"steamapps");if(!Directory.Exists(apps))continue;
            IEnumerable<string> manifests;
            try{manifests=Directory.EnumerateFiles(apps,"appmanifest_*.acf",SearchOption.TopDirectoryOnly).ToArray();}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){MasterDebugLog.Write("GAME-DISCOVERY",$"Could not enumerate Steam manifests at '{apps}'.",ex);continue;}
            foreach(var manifest in manifests)
            {
                try
                {
                    var text=File.ReadAllText(manifest);var id=VdfValue(text,"appid");var name=VdfValue(text,"name");var dir=VdfValue(text,"installdir");
                    if(string.IsNullOrWhiteSpace(dir))continue;var root=Path.Combine(apps,"common",dir);if(!Directory.Exists(root))continue;
                    result.Add(new(name??dir,root,null,"Steam",id));
                }catch(IOException){}
            }
        }
        return result;
    }

    private static List<GameDiscoveryCandidate> DiscoverEpic()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Epic","EpicGamesLauncher","Data","Manifests");
        if(!Directory.Exists(dir))return [];var result=new List<GameDiscoveryCandidate>();
        foreach(var file in Directory.EnumerateFiles(dir,"*.item",SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(file));var root=doc.RootElement;
                var name=GetJson(root,"DisplayName")??GetJson(root,"AppName")??"Epic game";var install=GetJson(root,"InstallLocation");var launch=GetJson(root,"LaunchExecutable");
                if(string.IsNullOrWhiteSpace(install)||!Directory.Exists(install))continue;string? exe=null;if(!string.IsNullOrWhiteSpace(launch)){var p=Path.Combine(install,launch.Replace('/','\\'));if(File.Exists(p))exe=p;}
                result.Add(new(name,install,exe,"Epic",null));
            }catch(Exception ex) when(ex is IOException or JsonException){}
        }
        return result;
    }

    private static List<GameDiscoveryCandidate> DiscoverGog()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result=new List<GameDiscoveryCandidate>();
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            try
            {
                using var baseKey=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);using var games=baseKey.OpenSubKey(@"SOFTWARE\GOG.com\Games");if(games is null)continue;
                foreach(var id in games.GetSubKeyNames())using(var k=games.OpenSubKey(id))
                {
                    var root=k?.GetValue("PATH") as string;if(string.IsNullOrWhiteSpace(root)||!Directory.Exists(root))continue;var name=k?.GetValue("GAMENAME") as string??Path.GetFileName(root);string? exe=null;
                    var info=Directory.EnumerateFiles(root,$"goggame-{id}.info",SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if(info is not null)exe=ReadGogExecutable(info,root);
                    result.Add(new(name,root,exe,"GOG",null));
                }
            }catch(Exception ex) when(ex is System.Security.SecurityException or IOException or UnauthorizedAccessException){}
        }
        return result;
    }

    private static string? ReadGogExecutable(string info,string root)
    {
        try
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(info));if(!doc.RootElement.TryGetProperty("playTasks",out var tasks)||tasks.ValueKind!=JsonValueKind.Array)return null;
            foreach(var task in tasks.EnumerateArray())if(task.TryGetProperty("path",out var p)&&p.ValueKind==JsonValueKind.String){var full=Path.Combine(root,p.GetString()!.Replace('/','\\'));if(File.Exists(full))return full;}
        }catch(Exception ex) when(ex is IOException or JsonException){}
        return null;
    }

    internal static string? FindLikelyExecutable(string root,string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var fullRoot=Path.GetFullPath(root);
            if(!Directory.Exists(fullRoot))return null;
            var normalized=GameProfile.NormalizeId(name).Replace("-",string.Empty,StringComparison.Ordinal);
            var queue=new Queue<(string Directory,int Depth)>();
            queue.Enqueue((fullRoot,0));
            var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates=new List<string>();
            const int maxDepth=4;
            const int maxDirectories=384;
            while(queue.Count>0&&visited.Count<maxDirectories)
            {
                var current=queue.Dequeue();
                if(!visited.Add(current.Directory))continue;
                string[] localExecutables;
                try{localExecutables=Directory.EnumerateFiles(current.Directory,"*.exe",SearchOption.TopDirectoryOnly).ToArray();}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){MasterDebugLog.Write("GAME-DISCOVERY",$"Could not inspect executables in '{current.Directory}'.",ex);localExecutables=[];}
                candidates.AddRange(localExecutables);
                var strongMatch=localExecutables
                    .Where(x=>!IsHelperExecutable(Path.GetFileNameWithoutExtension(x)))
                    .Select(x=>(Path:x,Normalized:GameProfile.NormalizeId(Path.GetFileNameWithoutExtension(x)).Replace("-",string.Empty,StringComparison.Ordinal)))
                    .Where(x=>x.Normalized.Equals(normalized,StringComparison.OrdinalIgnoreCase)
                        ||(normalized.Length>=5&&(x.Normalized.Contains(normalized,StringComparison.OrdinalIgnoreCase)||normalized.Contains(x.Normalized,StringComparison.OrdinalIgnoreCase))))
                    .OrderByDescending(x=>x.Normalized.Equals(normalized,StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x=>SafeFileLength(x.Path))
                    .Select(x=>x.Path)
                    .FirstOrDefault();
                if(strongMatch is not null)return strongMatch;
                if(current.Depth>=maxDepth)continue;
                string[] children;
                try{children=Directory.EnumerateDirectories(current.Directory).ToArray();}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){MasterDebugLog.Write("GAME-DISCOVERY",$"Could not inspect game subdirectories in '{current.Directory}'.",ex);continue;}
                foreach(var child in children
                    .Where(x=>!ShouldSkipExecutableSearchDirectory(Path.GetFileName(x)))
                    .OrderByDescending(x=>ExecutableSearchDirectoryPriority(Path.GetFileName(x))))
                {
                    try
                    {
                        if((new DirectoryInfo(child).Attributes&FileAttributes.ReparsePoint)!=0)continue;
                    }
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){continue;}
                    queue.Enqueue((child,current.Depth+1));
                }
            }
            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(x=>!IsHelperExecutable(Path.GetFileNameWithoutExtension(x)))
                .OrderByDescending(x=>GameProfile.NormalizeId(Path.GetFileNameWithoutExtension(x)).Replace("-",string.Empty,StringComparison.Ordinal).Contains(normalized,StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x=>ExecutableSearchDirectoryPriority(Path.GetFileName(Path.GetDirectoryName(x)??string.Empty)))
                .ThenBy(x=>Path.GetRelativePath(fullRoot,x).Count(ch=>ch==Path.DirectorySeparatorChar||ch==Path.AltDirectorySeparatorChar))
                .ThenByDescending(x=>SafeFileLength(x))
                .FirstOrDefault();
        }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException){return null;}
    }

    private static bool ShouldSkipExecutableSearchDirectory(string name)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        return name.Equals("_CommonRedist",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("redist",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("redistributable",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("installer",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("installers",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("support",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("directx",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("dotnet",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("easyanticheat",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("battleye",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("saved",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("mods",StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHelperExecutable(string name)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        return name.StartsWith("unins",StringComparison.OrdinalIgnoreCase)
            ||name.StartsWith("uninstall",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("CrashReportClient",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("CrashReporter",StringComparison.OrdinalIgnoreCase)
            ||name.StartsWith("UnityCrashHandler",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("crashpad_handler",StringComparison.OrdinalIgnoreCase)
            ||name.Contains("reportclient",StringComparison.OrdinalIgnoreCase)
            ||name.Contains("easyanticheat",StringComparison.OrdinalIgnoreCase)
            ||name.Contains("battleye",StringComparison.OrdinalIgnoreCase)
            ||name.Contains("redist",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("setup",StringComparison.OrdinalIgnoreCase)
            ||name.Equals("installer",StringComparison.OrdinalIgnoreCase);
    }

    private static int ExecutableSearchDirectoryPriority(string name)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        return name.Equals("Win64",StringComparison.OrdinalIgnoreCase)?6:
            name.Equals("Win32",StringComparison.OrdinalIgnoreCase)?5:
            name.Equals("Binaries",StringComparison.OrdinalIgnoreCase)?4:
            name.Equals("bin",StringComparison.OrdinalIgnoreCase)?3:
            name.Equals("x64",StringComparison.OrdinalIgnoreCase)?2:
            name.Equals("x86",StringComparison.OrdinalIgnoreCase)?1:0;
    }

    private static long SafeFileLength(string path)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        try{return new FileInfo(path).Length;}catch(IOException){return 0;}catch(UnauthorizedAccessException){return 0;}
    }

    private static List<GameDiscoveryCandidate> DiscoverXbox()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var roots=new List<string>();
        foreach(var drive in DriveInfo.GetDrives())
        {
            try
            {
                if(drive.DriveType!=DriveType.Fixed||!drive.IsReady)continue;
                var xbox=Path.Combine(drive.RootDirectory.FullName,"XboxGames");
                if(Directory.Exists(xbox))roots.Add(xbox);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        }
        return DiscoverXboxRoots(roots);
    }

    internal static List<GameDiscoveryCandidate> DiscoverXboxRoots(IEnumerable<string> xboxRoots)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(xboxRoots);
        var result=new List<GameDiscoveryCandidate>();
        foreach(var xboxRoot in xboxRoots.Where(Directory.Exists))
        {
            string[] gameDirectories;
            try{gameDirectories=Directory.EnumerateDirectories(xboxRoot).ToArray();}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){continue;}
            foreach(var gameDirectory in gameDirectories)
            {
                try
                {
                    var name=Path.GetFileName(gameDirectory);
                    if(string.IsNullOrWhiteSpace(name)||name.StartsWith('.'))continue;
                    var content=Path.Combine(gameDirectory,"Content");
                    var installRoot=Directory.Exists(content)?content:gameDirectory;
                    var exe=FindLikelyExecutable(installRoot,name);
                    if(string.IsNullOrWhiteSpace(exe))continue;
                    result.Add(new(name,installRoot,exe,"Xbox",null));
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){}
            }
        }
        return result;
    }

    private static string? VdfValue(string text,string key){var m=Regex.Match(text,$"\\\"{Regex.Escape(key)}\\\"\\s+\\\"(?<v>[^\\\"]*)\\\"",RegexOptions.IgnoreCase);return m.Success?m.Groups["v"].Value:null;}
    private static string? GetJson(JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
    private static void AtomicWrite(string path,byte[] bytes){var dir=Path.GetDirectoryName(path)!;Directory.CreateDirectory(dir);var temp=path+".tmp-"+Guid.NewGuid().ToString("N");File.WriteAllBytes(temp,bytes);File.Move(temp,path,true);}
}

internal sealed record GameDiscoveryCandidate(string Name,string Root,string? Executable,string Store,string? SteamAppId);

