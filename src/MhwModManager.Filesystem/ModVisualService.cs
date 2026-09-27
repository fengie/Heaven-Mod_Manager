using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

/// <summary>
/// Resolves user-facing artwork without touching the managed source tree. Sources are cached Nexus
/// thumbnails, explicit/local package screenshots, and existing per-mod preview metadata.
/// </summary>
public sealed class ModVisualService(ManagerDatabase db)
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase){".png",".jpg",".jpeg",".bmp",".gif"};
    private static readonly string[] PreferredNames = ["thumbnail","preview","screenshot","cover","image","photo"];

    public async Task<IReadOnlyList<string>> GetGalleryAsync(IEnumerable<ModDescriptor> members,int maxItems=16,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result=new List<string>(maxItems);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var mod in members)
        {
            ct.ThrowIfCancellationRequested();
            Add(mod.PreviewPath);
            var stored=await db.GetSettingAsync("visuals:"+mod.Id,ct);
            if(!string.IsNullOrWhiteSpace(stored))
            {
                try
                {
                    foreach(var p in JsonSerializer.Deserialize<string[]>(stored)??[])Add(p);
                }
                catch(JsonException){}
            }
            // Metadata sync already persists the best local images. Avoid recursively walking a
            // large mod folder every time the user clicks it unless the cached gallery is sparse.
            if(result.Count<Math.Min(maxItems,4))
                foreach(var p in DiscoverPackageImages(mod.SourcePath,maxItems-result.Count))Add(p);
            if(result.Count>=maxItems)break;
        }
        return result;

        void Add(string? path)
        {
            if(result.Count>=maxItems||string.IsNullOrWhiteSpace(path)||!File.Exists(path))return;
            var full=Path.GetFullPath(path);
            if(seen.Add(full))result.Add(full);
        }
    }

    internal static IReadOnlyList<string> DiscoverPackageImages(string root,int limit)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(limit<=0)return [];
        var result=new List<string>(limit);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in FindInstallerImages(root).Concat(FindLocalImages(root,limit)))
        {
            if(result.Count>=limit)break;
            if(seen.Add(p))result.Add(p);
        }
        return result;
    }

    private static IEnumerable<string> FindInstallerImages(string root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach(var xmlPath in new[]{Path.Combine(root,"fomod","info.xml"),Path.Combine(root,"fomod","ModuleConfig.xml")})
        {
            if(!File.Exists(xmlPath))continue;
            XDocument doc;
            try
            {
                var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};
                using var reader=XmlReader.Create(xmlPath,settings);doc=XDocument.Load(reader,LoadOptions.None);
            }
            catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}catch(XmlException){continue;}
            var values=doc.Descendants().Where(e=>e.Name.LocalName.Equals("Image",StringComparison.OrdinalIgnoreCase))
                .Select(e=>e.Value?.Trim()).Where(v=>!string.IsNullOrWhiteSpace(v)).Cast<string>();
            foreach(var value in values)
            {
                var normalized=value.Replace('/',Path.DirectorySeparatorChar).Replace('\\',Path.DirectorySeparatorChar);
                foreach(var baseDir in new[]{Path.GetDirectoryName(xmlPath)!,root})
                {
                    string candidate;
                    try{candidate=Path.GetFullPath(Path.Combine(baseDir,normalized));}
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){continue;}
                    var safeRoot=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                    if(candidate.StartsWith(safeRoot,StringComparison.OrdinalIgnoreCase)&&File.Exists(candidate)&&ImageExtensions.Contains(Path.GetExtension(candidate)))
                    {
                        yield return candidate;break;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> FindLocalImages(string root,int limit)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(limit<=0||!Directory.Exists(root))yield break;
        var candidates=new List<(string path,int score)>();
        try
        {
            foreach(var file in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Take(1500))
            {
                var ext=Path.GetExtension(file);if(!ImageExtensions.Contains(ext))continue;
                var info=new FileInfo(file);if(info.Length<8*1024||info.Length>30L*1024*1024)continue;
                var stem=Path.GetFileNameWithoutExtension(file);
                var score=PreferredNames.Any(x=>stem.Contains(x,StringComparison.OrdinalIgnoreCase))?100:0;
                score-=Math.Min(50,file.Count(c=>c==Path.DirectorySeparatorChar));
                candidates.Add((file,score));
            }
        }
        catch(IOException){yield break;}
        catch(UnauthorizedAccessException){yield break;}
        foreach(var item in candidates.OrderByDescending(x=>x.score).ThenBy(x=>x.path,StringComparer.OrdinalIgnoreCase).Take(limit))yield return item.path;
    }
}
