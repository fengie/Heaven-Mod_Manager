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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web){WriteIndented=true};

    public GameProfileRegistry(string stateRoot)
    {
        this.stateRoot=Path.GetFullPath(stateRoot);
        registryPath=Path.Combine(this.stateRoot,"games.json");
        activePath=Path.Combine(this.stateRoot,"active-game.txt");
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
        var profile=CreateFromExecutable(executablePath,null,null,null);Upsert(profile);SetActive(profile.Id);return profile;
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
        var candidates=new List<DiscoveredGame>();
        candidates.AddRange(DiscoverSteam());
        candidates.AddRange(DiscoverEpic());
        candidates.AddRange(DiscoverGog());
        var existing=Load();
        var added=new List<GameProfile>();
        foreach(var item in candidates.DistinctBy(x=>Path.GetFullPath(x.Root),StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if(existing.Any(x=>Path.GetFullPath(x.GameRoot).Equals(Path.GetFullPath(item.Root),StringComparison.OrdinalIgnoreCase)))continue;
                var exe=item.Executable;
                if(string.IsNullOrWhiteSpace(exe)||!File.Exists(exe))exe=FindLikelyExecutable(item.Root,item.Name);
                if(string.IsNullOrWhiteSpace(exe)||!File.Exists(exe))continue;
                GameProfile profile;
                if(string.Equals(item.SteamAppId,"582010",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(exe).Equals("MonsterHunterWorld.exe",StringComparison.OrdinalIgnoreCase))
                    profile=GameProfile.MonsterHunterWorld(item.Root);
                else profile=CreateFromExecutable(exe,item.Name,item.Store,item.SteamAppId);
                Upsert(profile);added.Add(profile);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
            { MasterDebugLog.Write("GAME-DISCOVERY",$"Skipped discovered game '{item.Name}' at '{item.Root}'.",ex); }
        }
        MasterDebugLog.Write("GAME-DISCOVERY",$"Discovered={candidates.Count}; registered={added.Count}");
        return added;
    }

    public void Upsert(GameProfile profile)
    {
        if(!IsUsable(profile))throw new ArgumentException("Game profile is invalid or points outside its game root.",nameof(profile));
        var list=Load().Where(x=>!x.Id.Equals(profile.Id,StringComparison.OrdinalIgnoreCase)).Append(profile).OrderBy(x=>x.DisplayName,StringComparer.OrdinalIgnoreCase).ToArray();
        Directory.CreateDirectory(stateRoot);AtomicWrite(registryPath,JsonSerializer.SerializeToUtf8Bytes(list,JsonOptions));
    }

    public void SetActive(string id)
    {
        if(!Load().Any(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase)))throw new KeyNotFoundException($"Unknown game profile '{id}'.");
        Directory.CreateDirectory(stateRoot);AtomicWrite(activePath,System.Text.Encoding.UTF8.GetBytes(id.Trim()+Environment.NewLine));
    }

    public static bool IsUsable(GameProfile profile)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(profile.Id)||string.IsNullOrWhiteSpace(profile.DisplayName)||string.IsNullOrWhiteSpace(profile.GameRoot)||!Path.IsPathRooted(profile.GameRoot))return false;
            _=GameProfile.NormalizeRelative(profile.ExecutableRelativePath,false);_=GameProfile.NormalizeRelative(profile.ModRootRelativePath,true);
            var root=Path.GetFullPath(profile.GameRoot).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            var exe=Path.GetFullPath(Path.Combine(profile.GameRoot,profile.ExecutableRelativePath));var live=Path.GetFullPath(profile.LiveModRoot);
            return exe.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&(live+Path.DirectorySeparatorChar).StartsWith(root,StringComparison.OrdinalIgnoreCase);
        }
        catch(Exception ex) when(ex is ArgumentException or NotSupportedException or PathTooLongException){return false;}
    }

    private GameProfile CreateFromExecutable(string executablePath,string? displayName,string? store,string? steamAppId)
    {
        var full=Path.GetFullPath(executablePath);
        if(!File.Exists(full)||!full.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new FileNotFoundException("Select the game's Windows executable.",full);
        var root=InferGameRoot(full);
        var exe=Path.GetRelativePath(root,full).Replace('/','\\');var name=string.IsNullOrWhiteSpace(displayName)?Path.GetFileNameWithoutExtension(full):displayName.Trim();
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

    private static List<DiscoveredGame> DiscoverSteam()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
            {
                using var baseKey=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,view);
                using var h=baseKey.OpenSubKey(@"Software\Valve\Steam");
                if(h?.GetValue("SteamPath") is string p&&Directory.Exists(p))roots.Add(p.Replace('/','\\'));
            }
        }
        catch(System.Security.SecurityException){}
        var pf=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);if(!string.IsNullOrWhiteSpace(pf)){var p=Path.Combine(pf,"Steam");if(Directory.Exists(p))roots.Add(p);}
        var libraries=new HashSet<string>(roots,StringComparer.OrdinalIgnoreCase);
        foreach(var steam in roots)
        {
            var vdf=Path.Combine(steam,"steamapps","libraryfolders.vdf");if(!File.Exists(vdf))continue;
            try{foreach(Match m in Regex.Matches(File.ReadAllText(vdf),"\\\"path\\\"\\s+\\\"(?<p>[^\\\"]+)\\\""))libraries.Add(m.Groups["p"].Value.Replace("\\\\","\\"));}catch(IOException){}
        }
        var result=new List<DiscoveredGame>();
        foreach(var lib in libraries)
        {
            var apps=Path.Combine(lib,"steamapps");if(!Directory.Exists(apps))continue;
            foreach(var manifest in Directory.EnumerateFiles(apps,"appmanifest_*.acf",SearchOption.TopDirectoryOnly))
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

    private static List<DiscoveredGame> DiscoverEpic()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Epic","EpicGamesLauncher","Data","Manifests");
        if(!Directory.Exists(dir))return [];var result=new List<DiscoveredGame>();
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

    private static List<DiscoveredGame> DiscoverGog()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result=new List<DiscoveredGame>();
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

    private static string? FindLikelyExecutable(string root,string name)
    {
        try
        {
            var dirs=new List<string>{root,Path.Combine(root,"bin"),Path.Combine(root,"Bin"),Path.Combine(root,"Binaries","Win64"),Path.Combine(root,"Binaries","Win32")};
            foreach(var child in Directory.EnumerateDirectories(root))
            {
                dirs.Add(Path.Combine(child,"Binaries","Win64"));dirs.Add(Path.Combine(child,"Binaries","Win32"));
            }
            var normalized=GameProfile.NormalizeId(name).Replace("-",string.Empty,StringComparison.Ordinal);
            var candidates=new List<string>();
            foreach(var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
            {
                try{candidates.AddRange(Directory.EnumerateFiles(dir,"*.exe",SearchOption.TopDirectoryOnly));}
                catch(UnauthorizedAccessException){}
            }
            return candidates.Where(x=>!x.Contains("redist",StringComparison.OrdinalIgnoreCase)&&!x.Contains("unins",StringComparison.OrdinalIgnoreCase)&&!x.Contains("crash",StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x=>GameProfile.NormalizeId(Path.GetFileNameWithoutExtension(x)).Replace("-",string.Empty,StringComparison.Ordinal).Contains(normalized,StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x=>new FileInfo(x).Length).FirstOrDefault();
        }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return null;}
    }

    private static string? VdfValue(string text,string key){var m=Regex.Match(text,$"\\\"{Regex.Escape(key)}\\\"\\s+\\\"(?<v>[^\\\"]*)\\\"",RegexOptions.IgnoreCase);return m.Success?m.Groups["v"].Value:null;}
    private static string? GetJson(JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
    private static void AtomicWrite(string path,byte[] bytes){var dir=Path.GetDirectoryName(path)!;Directory.CreateDirectory(dir);var temp=path+".tmp-"+Guid.NewGuid().ToString("N");File.WriteAllBytes(temp,bytes);File.Move(temp,path,true);}
    private sealed record DiscoveredGame(string Name,string Root,string? Executable,string Store,string? SteamAppId);
}
