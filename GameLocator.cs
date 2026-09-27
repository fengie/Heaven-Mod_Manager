using MhwModManager.Core;
using Microsoft.Win32;
namespace MhwModManager.Mhw;
public static class GameLocator
{
    public static string? Find()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var roots=new List<string>();try{using var k=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");var p=k?.GetValue("SteamPath") as string;if(!string.IsNullOrWhiteSpace(p))roots.Add(p.Replace('/','\\'));}catch{}
        roots.Add(@"C:\Program Files (x86)\Steam");
        foreach(var steam in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var libraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase){steam};var vdf=Path.Combine(steam,"steamapps","libraryfolders.vdf");if(File.Exists(vdf))foreach(var line in File.ReadLines(vdf)){var m=System.Text.RegularExpressions.Regex.Match(line,"^\\s*\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"");if(m.Success)libraries.Add(m.Groups[1].Value.Replace("\\\\","\\"));}
            foreach(var lib in libraries){var g=Path.Combine(lib,"steamapps","common","Monster Hunter World");if(File.Exists(Path.Combine(g,"MonsterHunterWorld.exe")))return g;}
        }
        return null;
    }
}
