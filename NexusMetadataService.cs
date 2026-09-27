using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

/// <summary>
/// Best-effort Nexus provenance enrichment. Local sidecars/folder names work offline; if a Nexus API
/// key is present, v3 metadata is used as stronger evidence for main/optional/update/version lineage.
/// </summary>
public sealed partial class NexusMetadataService(ManagerDatabase db,string nextStateRoot,GameProfile? game = null)
{
    private readonly string? gameDomain = game?.NexusGameDomain ?? (game is null || game.IsMonsterHunterWorld ? "monsterhunterworld" : null);
    private static readonly HttpClient Http=CreateHttpClient();

    public Task<MetadataRefreshResult> RefreshAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return RefreshAsync(false,ct);
    }

    public async Task<MetadataRefreshResult> RefreshAsync(bool forceLive,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mods=await db.GetModsAsync(ct);
        var localCount=0;
        var localVisualCount=0;
        var declaredVisualCount=0;
        var publicVisualCount=0;
        foreach(var mod in mods)
        {
            ct.ThrowIfCancellationRequested();
            var local=ReadLocalProvenance(mod);
            if(local is not null){await db.UpsertProvenanceAsync(local,ct);localCount++;}
            var localPreviews=ModVisualService.DiscoverPackageImages(mod.SourcePath,8).ToArray();
            if(localPreviews.Length>0)
            {
                await PersistVisualsAsync(mod,localPreviews,ct);
                localVisualCount+=localPreviews.Length;
            }
            declaredVisualCount+=await TryRefreshDeclaredVisualsAsync(mod,ct);
        }
        // Local provenance may have just discovered Nexus ids from the folder/archive name. Reload
        // descriptors before the no-key artwork pass so the first manual sync can use them immediately.
        mods=await db.GetModsAsync(ct);

        // Nexus/Vortex archives normally do not contain their web screenshots. Even without an API
        // key, recover a main preview from known Nexus page identity. Force sync may fill the whole
        // library; background sync is deliberately capped so it never turns into a page-scraping storm.
        var publicBudget=forceLive?60:8;
        foreach(var mod in mods.Where(m=>!string.IsNullOrWhiteSpace(m.NexusModId)).OrderByDescending(m=>m.Enabled).ThenByDescending(m=>m.Priority))
        {
            if(publicBudget<=0)break;
            ct.ThrowIfCancellationRequested();
            if(await HasUsablePreviewAsync(mod.Id,ct))continue;
            var added=await TryRefreshPublicNexusVisualAsync(mod,forceLive,ct);
            if(added>0)publicVisualCount+=added;
            publicBudget--;
        }

        var key=ReadApiKey();
        var apiCount=0;
        var visualCount=0;
        var updateAvailableCount=0;
        var liveAllowed=!string.IsNullOrWhiteSpace(key)&&await ShouldRunLiveSyncAsync(forceLive,ct);
        var updateCheckAllowed=liveAllowed&&await ShouldRunUpdateCheckAsync(forceLive,ct);
        if(liveAllowed)
        {
            mods=await db.GetModsAsync(ct);
            foreach(var mod in mods.Where(m=>!string.IsNullOrWhiteSpace(m.NexusModId)))
            {
                ct.ThrowIfCancellationRequested();
                var enriched=await TryFetchNexusAsync(mod,key!,ct);
                if(enriched is null)continue;
                await db.UpsertProvenanceAsync(enriched,ct);apiCount++;
                visualCount+=await TryRefreshNexusVisualsAsync(mod,enriched.NexusModUuid,key!,ct);
                if(updateCheckAllowed&&await TryCheckUpdateAsync(mod,enriched,key!,ct))updateAvailableCount++;
            }
            var now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
            await db.SetSettingAsync("nexus:last-live-sync",now,ct);
            if(updateCheckAllowed)await db.SetSettingAsync("nexus:last-update-check",now,ct);
        }

        mods=await db.GetModsAsync(ct);
        await ApplyManagerFamilyHintsAsync(mods.ToArray(),ct);
        mods=await db.GetModsAsync(ct);
        await ApplyNexusFamilyHintsAsync(mods.ToArray(),ct);
        mods=await db.GetModsAsync(ct);
        var snapshot=await db.LoadPlannerSnapshotAsync(ct);
        var supersession=ProvenanceIntelligence.BuildSupersessionLinks(mods)
            .Concat(ProvenanceIntelligence.BuildLocalTextureSupersessionLinks(mods,snapshot.Files))
            .GroupBy(x=>x.older,StringComparer.OrdinalIgnoreCase)
            .Select(g=>g.OrderByDescending(x=>x.score).First())
            .ToArray();
        await db.ReplaceSupersessionAsync(supersession,ct);
        return new(localCount,apiCount,supersession.Length,!string.IsNullOrWhiteSpace(key),liveAllowed,visualCount,updateAvailableCount,localVisualCount,declaredVisualCount,publicVisualCount);
    }

    private static ModProvenance? ReadLocalProvenance(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var now=DateTimeOffset.UtcNow;
        var sidecar=Path.Combine(mod.SourcePath,"mod-manager.meta.json");
        if(!File.Exists(sidecar))sidecar=Path.Combine(mod.SourcePath,"mhw-manager.meta.json");
        if(File.Exists(sidecar))
        {
            try
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(sidecar));
                var root=doc.RootElement;
                var gameId=GetString(root,"nexusModId")??mod.NexusModId;
                var fileId=GetString(root,"nexusFileId")??mod.NexusFileId;
                var versionId=GetString(root,"nexusVersionId");
                var previous=GetString(root,"nexusPreviousVersionId");
                var category=ParseCategory(GetString(root,"nexusCategory"));
                var version=GetString(root,"version");
                var uploaded=TryDate(GetString(root,"uploadedAt"));
                return new(mod.Id,gameId,GetString(root,"nexusModUuid"),fileId,versionId,previous,category,GetString(root,"nexusFileName"),version,uploaded,
                    ProvenanceSource.ImportedSidecar,100,GetString(root,"sourceArchive"),root.GetRawText(),now);
            }
            catch(JsonException){ }
        }

        var metaIni=Path.Combine(mod.SourcePath,"meta.ini");
        if(File.Exists(metaIni))
        {
            var values=ParseIni(metaIni);
            values.TryGetValue("modid",out var modId);values.TryGetValue("fileid",out var fileId);values.TryGetValue("version",out var version);values.TryGetValue("installationfile",out var archive);
            if(!string.IsNullOrWhiteSpace(modId)||!string.IsNullOrWhiteSpace(fileId))
                return new(mod.Id,modId??mod.NexusModId,null,fileId,null,null,NexusFileCategory.Unknown,null,version,null,ProvenanceSource.ImportedSidecar,95,archive,null,now);
        }

        var nexusId=mod.NexusModId??ParseNexusModId(mod.SourceUrl)??ParseNexusModId(mod.Name);
        if(string.IsNullOrWhiteSpace(nexusId))return null;
        return new(mod.Id,nexusId,null,mod.NexusFileId,null,null,NexusFileCategory.Unknown,null,null,null,ProvenanceSource.LocalInference,70,Path.GetFileName(mod.SourcePath),null,now);
    }

    private async Task<ModProvenance?> TryFetchNexusAsync(ModDescriptor mod,string apiKey,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            // MO2/Vortex sidecars often provide the game-scoped file-version id directly.
            if(TryPositiveLong(mod.NexusFileId,out _))
            {
                if(string.IsNullOrWhiteSpace(gameDomain))return null;
                var versionDoc=await GetJsonAsync($"games/{gameDomain}/mod-file-versions/{Uri.EscapeDataString(mod.NexusFileId!)}",apiKey,ct);
                if(versionDoc is not null)
                {
                    using(versionDoc)
                    {
                        var version=versionDoc.RootElement.GetProperty("data");
                        var file=version.GetProperty("file");
                        var fileId=GetString(file,"id");
                        var modUuid=await ResolveModUuidAsync(gameDomain!,mod.NexusModId!,apiKey,ct);
                        var previous=await ResolvePreviousVersionAsync(fileId,GetString(version,"id"),apiKey,ct);
                        return BuildApiProvenance(mod,modUuid,fileId,version,previous,versionDoc.RootElement.GetRawText());
                    }
                }
            }

            if(string.IsNullOrWhiteSpace(gameDomain))return null;
            var uuid=await ResolveModUuidAsync(gameDomain,mod.NexusModId!,apiKey,ct);
            if(string.IsNullOrWhiteSpace(uuid))return null;
            using var filesDoc=await GetJsonAsync($"mods/{Uri.EscapeDataString(uuid)}/files",apiKey,ct);
            if(filesDoc is null)return null;
            var files=filesDoc.RootElement.GetProperty("data").GetProperty("mod_files").EnumerateArray().ToArray();
            var ranked=files.Select(f=>new{File=f.Clone(),Score=NameSimilarity(mod.DisplayName,GetString(f,"name")??string.Empty)})
                .OrderByDescending(x=>x.Score).Take(4).ToArray();
            var matches=new List<ApiCandidate>();
            foreach(var candidate in ranked)
            {
                var fileId=GetString(candidate.File,"id");if(string.IsNullOrWhiteSpace(fileId))continue;
                using var versionsDoc=await GetJsonAsync($"mod-files/{Uri.EscapeDataString(fileId)}/versions",apiKey,ct);
                if(versionsDoc is null)continue;
                foreach(var version in versionsDoc.RootElement.GetProperty("data").GetProperty("versions").EnumerateArray())
                {
                    var score=candidate.Score+NameSimilarity(mod.DisplayName,GetString(version,"name")??string.Empty);
                    matches.Add(new(fileId,version.Clone(),score,versionsDoc.RootElement.GetRawText()));
                }
            }
            var orderedMatches=matches.OrderByDescending(x=>x.Score).ToArray();
            if(orderedMatches.Length==0||orderedMatches[0].Score<90)return null;
            if(orderedMatches.Length>1&&orderedMatches[0].Score<150&&orderedMatches[0].Score-orderedMatches[1].Score<15)return null;
            var best=orderedMatches[0];
            var prev=await ResolvePreviousVersionAsync(best.FileId,GetString(best.Version,"id"),apiKey,ct);
            var provenance=BuildApiProvenance(mod,uuid,best.FileId,best.Version,prev,best.RawJson);
            return provenance with{ConfidenceScore=Math.Clamp(70+best.Score/3,90,99)};
        }
        catch(HttpRequestException){return null;}
        catch(JsonException){return null;}
    }

    private static ModProvenance BuildApiProvenance(ModDescriptor mod,string? uuid,string? fileId,JsonElement version,string? previous,string raw)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var category=ParseCategory(GetString(version,"category"));
        return new(mod.Id,mod.NexusModId,uuid,fileId,GetString(version,"id"),previous,category,
            version.TryGetProperty("file",out var file)?GetString(file,"name"):null,GetString(version,"version"),TryDate(GetString(version,"uploaded_at")),
            ProvenanceSource.NexusApi,99,Path.GetFileName(mod.SourcePath),raw,DateTimeOffset.UtcNow);
    }

    private static async Task<string?> ResolveModUuidAsync(string gameDomain,string gameScopedId,string apiKey,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var doc=await GetJsonAsync($"games/{gameDomain}/mods/{Uri.EscapeDataString(gameScopedId)}",apiKey,ct);
        return doc is null?null:GetString(doc.RootElement.GetProperty("data"),"id");
    }

    private static async Task<string?> ResolvePreviousVersionAsync(string? fileId,string? currentVersionId,string apiKey,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(string.IsNullOrWhiteSpace(fileId)||string.IsNullOrWhiteSpace(currentVersionId))return null;
        using var doc=await GetJsonAsync($"mod-files/{Uri.EscapeDataString(fileId)}/versions",apiKey,ct);if(doc is null)return null;
        var versions=doc.RootElement.GetProperty("data").GetProperty("versions").EnumerateArray()
            .Select(v=>new{Id=GetString(v,"id"),Position=ParsePosition(GetString(v,"position")),Uploaded=TryDate(GetString(v,"uploaded_at"))}).ToArray();
        var current=versions.FirstOrDefault(v=>StringComparer.OrdinalIgnoreCase.Equals(v.Id,currentVersionId));if(current is null)return null;
        return versions.Where(v=>!StringComparer.OrdinalIgnoreCase.Equals(v.Id,currentVersionId))
            .Where(v=>v.Position<current.Position||(v.Position==current.Position&&v.Uploaded<current.Uploaded))
            .OrderByDescending(v=>v.Position).ThenByDescending(v=>v.Uploaded).Select(v=>v.Id).FirstOrDefault();
    }


    private async Task ApplyManagerFamilyHintsAsync(ModDescriptor[] mods,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var hinted=mods.Select(m=>(mod:m,hint:ReadManagerFamilyHint(m)))
            .Where(x=>!string.IsNullOrWhiteSpace(x.hint)).ToArray();
        foreach(var group in hinted.GroupBy(x=>x.hint!,StringComparer.OrdinalIgnoreCase))
        {
            var members=group.Select(x=>x.mod).Where(m=>!m.IsSuperseded).ToArray();
            if(members.Length<2)continue;
            var family="manager:"+NormalizeName(group.Key);
            MasterDebugLog.Write("FAMILY",$"Persist manager-metadata family id={family}; members={members.Length}; names=[{string.Join(" | ",members.Select(x=>x.DisplayName))}]");
            foreach(var member in members)
            {
                var canAutoAssign=string.IsNullOrWhiteSpace(member.FamilyId)||
                    member.FamilyId.StartsWith("manager:",StringComparison.OrdinalIgnoreCase)||
                    member.FamilyId.StartsWith("nexus:",StringComparison.OrdinalIgnoreCase);
                if(canAutoAssign&&!StringComparer.OrdinalIgnoreCase.Equals(member.FamilyId,family))
                    await db.SetFamilyIdAsync(member.Id,family,ct);
            }
        }
    }

    private static string? ReadManagerFamilyHint(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var json=Path.Combine(mod.SourcePath,"mod-manager.meta.json");
        if(!File.Exists(json))json=Path.Combine(mod.SourcePath,"mhw-manager.meta.json");
        if(File.Exists(json))
        {
            try
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(json));
                var root=doc.RootElement;
                foreach(var key in new[]{"familyId","logicalFileName","logical_filename","vortexLogicalFileName"})
                {
                    var value=GetString(root,key);if(!string.IsNullOrWhiteSpace(value))return value.Trim();
                }
            }
            catch(JsonException){ }
        }
        var ini=Path.Combine(mod.SourcePath,"meta.ini");
        if(File.Exists(ini))
        {
            var values=ParseIni(ini);
            foreach(var key in new[]{"logicalFileName","logical_filename","familyId"})
                if(values.TryGetValue(key,out var value)&&!string.IsNullOrWhiteSpace(value))return value.Trim();
        }

        // FOMOD metadata is explicit author-supplied installer identity. Vortex consumes the same
        // installer format, so use its module/name as a strong hint when separate source packages
        // carry the same installer identity. The choices themselves remain independent components.
        foreach(var xmlPath in new[]{Path.Combine(mod.SourcePath,"fomod","info.xml"),Path.Combine(mod.SourcePath,"fomod","ModuleConfig.xml")})
        {
            if(!File.Exists(xmlPath))continue;
            try
            {
                var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};
                using var reader=XmlReader.Create(xmlPath,settings);
                var doc=XDocument.Load(reader,LoadOptions.None);
                var name=doc.Descendants().FirstOrDefault(e=>
                    e.Name.LocalName.Equals("Name",StringComparison.OrdinalIgnoreCase)||
                    e.Name.LocalName.Equals("moduleName",StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
                if(!string.IsNullOrWhiteSpace(name))return name;
            }
            catch(IOException){ }
            catch(UnauthorizedAccessException){ }
            catch(XmlException){ }
        }
        return null;
    }

    private async Task ApplyNexusFamilyHintsAsync(ModDescriptor[] mods,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach(var group in mods.Where(m=>!string.IsNullOrWhiteSpace(m.NexusModId)).GroupBy(m=>m.NexusModId!,StringComparer.OrdinalIgnoreCase))
        {
            var members=group.Where(m=>!m.IsSuperseded).ToArray();
            if(members.Length<2)continue;

            // Nexus file categories are author-entered metadata, not structural truth. Any mod page
            // may publish several base/component/variant archives under the same coarse category.
            // Shared source identity is therefore stronger family evidence than Main/Optional labels.
            // Preserve manual/manager family assignments; only fill missing or prior Nexus hints.
            var family=$"nexus:{group.Key}";
            MasterDebugLog.Write("FAMILY",$"Persist Nexus family id={family}; members={members.Length}; names=[{string.Join(" | ",members.Select(x=>x.DisplayName))}]");
            foreach(var member in members)
            {
                var canAutoAssign=string.IsNullOrWhiteSpace(member.FamilyId)||member.FamilyId.StartsWith("nexus:",StringComparison.OrdinalIgnoreCase);
                if(canAutoAssign&&!StringComparer.OrdinalIgnoreCase.Equals(member.FamilyId,family))
                    await db.SetFamilyIdAsync(member.Id,family,ct);
            }
        }
    }

    private async Task<bool> ShouldRunLiveSyncAsync(bool forceLive,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(forceLive)return true;
        var last=await db.GetSettingAsync("nexus:last-live-sync",ct);
        if(!DateTimeOffset.TryParse(last,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var timestamp))return true;
        return DateTimeOffset.UtcNow-timestamp>=TimeSpan.FromHours(6);
    }

    private async Task<bool> ShouldRunUpdateCheckAsync(bool forceLive,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(forceLive)return true;
        var last=await db.GetSettingAsync("nexus:last-update-check",ct);
        if(!DateTimeOffset.TryParse(last,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var timestamp))return true;
        return DateTimeOffset.UtcNow-timestamp>=TimeSpan.FromHours(24);
    }

    private async Task<bool> HasUsablePreviewAsync(string modId,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var preview=await db.GetSettingAsync("preview:"+modId,ct);
        return !string.IsNullOrWhiteSpace(preview)&&File.Exists(preview);
    }

    private async Task PersistVisualsAsync(ModDescriptor mod,IEnumerable<string> paths,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var existing=new List<string>();
        var old=await db.GetSettingAsync("visuals:"+mod.Id,ct);
        if(!string.IsNullOrWhiteSpace(old))try{existing.AddRange(JsonSerializer.Deserialize<string[]>(old)??[]);}catch(JsonException){}
        var merged=paths.Concat(existing).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
        if(merged.Length==0)return;
        await db.SetSettingAsync("visuals:"+mod.Id,JsonSerializer.Serialize(merged),ct);
        await db.SetSettingAsync("preview:"+mod.Id,merged[0],ct);
    }

    private async Task<int> TryRefreshDeclaredVisualsAsync(ModDescriptor mod,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"modId={mod.Id}");
        var urls=ReadDeclaredImageUrls(mod.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        if(urls.Length==0)return 0;
        var cached=new List<string>();
        foreach(var url in urls)
        {
            var path=await CacheRemoteImageAsync(mod.Id,url,ct);
            if(path is not null)cached.Add(path);
        }
        if(cached.Count>0)
        {
            await PersistVisualsAsync(mod,cached,ct);
            MasterDebugLog.Write("VISUALS",$"Imported Vortex/sidecar artwork mod={mod.DisplayName}; images={cached.Count}");
        }
        return cached.Count;
    }

    private static IEnumerable<string> ReadDeclaredImageUrls(string root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"pictureUrl","picture_url","imageUrl","image_url","thumbnailUrl","thumbnail_url","previewUrl","preview_url"};
        var json=Path.Combine(root,"mod-manager.meta.json");
        if(!File.Exists(json))json=Path.Combine(root,"mhw-manager.meta.json");
        if(File.Exists(json))
        {
            JsonDocument? doc=null;
            try{doc=JsonDocument.Parse(File.ReadAllText(json));}
            catch(JsonException){}catch(IOException){}catch(UnauthorizedAccessException){}
            if(doc is not null)
            {
                using(doc)
                {
                    foreach(var value in EnumerateJsonImageUrls(doc.RootElement,keys))yield return value;
                }
            }
        }
        foreach(var iniName in new[]{"meta.ini","vortex.meta.ini"})
        {
            var ini=Path.Combine(root,iniName);if(!File.Exists(ini))continue;
            Dictionary<string,string> values;
            try{values=ParseIni(ini);}catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}
            foreach(var key in keys)if(values.TryGetValue(key,out var value)&&IsHttpImageUrl(value))yield return value;
        }
    }

    private static IEnumerable<string> EnumerateJsonImageUrls(JsonElement element,HashSet<string> keys)
    {
        switch(element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach(var property in element.EnumerateObject())
                {
                    if(keys.Contains(property.Name)&&property.Value.ValueKind==JsonValueKind.String)
                    {
                        var value=property.Value.GetString();if(IsHttpImageUrl(value))yield return value!;
                    }
                    foreach(var nested in EnumerateJsonImageUrls(property.Value,keys))yield return nested;
                }
                break;
            case JsonValueKind.Array:
                foreach(var item in element.EnumerateArray())foreach(var nested in EnumerateJsonImageUrls(item,keys))yield return nested;
                break;
        }
    }

    private async Task<int> TryRefreshPublicNexusVisualAsync(ModDescriptor mod,bool force,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"modId={mod.Id}; nexus={mod.NexusModId}");
        if(string.IsNullOrWhiteSpace(mod.NexusModId)||string.IsNullOrWhiteSpace(gameDomain))return 0;
        var stampKey="visual-public-last:"+mod.Id;
        var last=await db.GetSettingAsync(stampKey,ct);
        if(!force&&DateTimeOffset.TryParse(last,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var previous)&&DateTimeOffset.UtcNow-previous<TimeSpan.FromDays(2))return 0;
        await db.SetSettingAsync(stampKey,DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture),ct);
        try
        {
            var page=$"https://www.nexusmods.com/{gameDomain}/mods/{Uri.EscapeDataString(mod.NexusModId)}";
            using var request=new HttpRequestMessage(HttpMethod.Get,page);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
            request.Headers.Referrer=new Uri("https://www.nexusmods.com/");
            MasterDebugLog.Write("HTTP",$"Public Nexus visual request START mod={mod.NexusModId}");
            using var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            MasterDebugLog.Write("HTTP",$"Public Nexus visual request END mod={mod.NexusModId}; status={(int)response.StatusCode} {response.StatusCode}");
            if(!response.IsSuccessStatusCode)return 0;
            var length=response.Content.Headers.ContentLength;if(length is>4_000_000)return 0;
            var html=await response.Content.ReadAsStringAsync(ct);
            if(html.Length>4_000_000)return 0;
            var url=ExtractMetaImageUrl(html);
            if(url is null)return 0;
            var cached=await CacheRemoteImageAsync(mod.Id,url,ct);
            if(cached is null)return 0;
            await PersistVisualsAsync(mod,[cached],ct);
            MasterDebugLog.Write("VISUALS",$"Public Nexus artwork fallback refreshed mod={mod.DisplayName}; nexus={mod.NexusModId}");
            return 1;
        }
        catch(HttpRequestException ex){MasterDebugLog.Write("VISUALS",$"Public Nexus artwork fallback failed mod={mod.DisplayName}",ex);return 0;}
        catch(IOException ex){MasterDebugLog.Write("VISUALS",$"Public Nexus artwork cache failed mod={mod.DisplayName}",ex);return 0;}
    }

    private static string? ExtractMetaImageUrl(string html)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach(var match in MetaTagRegex().Matches(html).Cast<Match>())
        {
            var tag=match.Value;
            if(!MetaPropertyRegex().IsMatch(tag))continue;
            var content=MetaContentRegex().Match(tag);if(!content.Success)continue;
            var url=System.Net.WebUtility.HtmlDecode(content.Groups[1].Value).Trim();
            if(url.StartsWith("//",StringComparison.Ordinal))url="https:"+url;
            if(IsHttpImageUrl(url))return url;
        }
        return null;
    }

    private static bool IsHttpImageUrl(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&(uri.Scheme==Uri.UriSchemeHttps||uri.Scheme==Uri.UriSchemeHttp);

    private async Task<int> TryRefreshNexusVisualsAsync(ModDescriptor mod,string? nexusUuid,string apiKey,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"modId={mod.Id}; nexus={mod.NexusModId}");
        if(string.IsNullOrWhiteSpace(nexusUuid))return 0;
        try
        {
            using var doc=await GetJsonAsync($"mods/{Uri.EscapeDataString(nexusUuid)}",apiKey,ct);
            if(doc is null||!doc.RootElement.TryGetProperty("data",out var data))return 0;
            var urls=new[]{GetString(data,"thumbnail_url"),GetString(data,"picture_url"),GetString(data,"image_url")}
                .Where(x=>Uri.TryCreate(x,UriKind.Absolute,out var u)&&(u.Scheme==Uri.UriSchemeHttps||u.Scheme==Uri.UriSchemeHttp))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(4).Cast<string>().ToArray();
            if(urls.Length==0)return 0;
            var cached=new List<string>();
            foreach(var url in urls)
            {
                var path=await CacheRemoteImageAsync(mod.Id,url,ct);
                if(path is not null)cached.Add(path);
            }
            if(cached.Count==0)return 0;
            await PersistVisualsAsync(mod,cached,ct);
            MasterDebugLog.Write("VISUALS",$"Nexus artwork refreshed mod={mod.DisplayName}; images={cached.Count}");
            return cached.Count;
        }
        catch(HttpRequestException ex){MasterDebugLog.Write("VISUALS",$"Nexus artwork refresh failed mod={mod.DisplayName}",ex);return 0;}
        catch(JsonException ex){MasterDebugLog.Write("VISUALS",$"Nexus artwork metadata parse failed mod={mod.DisplayName}",ex);return 0;}
        catch(IOException ex){MasterDebugLog.Write("VISUALS",$"Nexus artwork cache failed mod={mod.DisplayName}",ex);return 0;}
    }

    private async Task<string?> CacheRemoteImageAsync(string modId,string url,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        var dir=Path.Combine(nextStateRoot,"PreviewCache","Nexus",SanitizeFileName(modId));
        Directory.CreateDirectory(dir);
        PruneStalePreviewParts(dir);
        var existing=Directory.EnumerateFiles(dir,key+".*").FirstOrDefault();if(existing is not null)return existing;
        using var response=await Http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)return null;
        var media=response.Content.Headers.ContentType?.MediaType??string.Empty;
        var ext=media.ToLowerInvariant() switch
        {
            "image/png"=>".png",
            "image/bmp"=>".bmp",
            "image/gif"=>".gif",
            "image/jpeg" or "image/jpg"=>".jpg",
            _=>null
        };
        if(ext is null){MasterDebugLog.Write("VISUALS",$"Remote preview uses unsupported WPF image media type '{media}': {url}");return null;}
        var length=response.Content.Headers.ContentLength;if(length is>12_000_000)return null;
        var path=Path.Combine(dir,key+ext);
        var temp=path+".part-"+Guid.NewGuid().ToString("N");
        try
        {
            await using var input=await response.Content.ReadAsStreamAsync(ct);
            await using var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
            var buffer=new byte[81920];
            long total=0;
            while(true)
            {
                var read=await input.ReadAsync(buffer,ct);
                if(read==0)break;
                total+=read;
                if(total>12_000_000){MasterDebugLog.Write("VISUALS",$"Remote preview exceeded cache limit and was discarded: {url}");return null;}
                await output.WriteAsync(buffer.AsMemory(0,read),ct);
            }
            await output.FlushAsync(ct);
            File.Move(temp,path,false);
            return path;
        }
        finally
        {
            try{if(File.Exists(temp))File.Delete(temp);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        }
    }

    private async Task<bool> TryCheckUpdateAsync(ModDescriptor mod,ModProvenance current,string apiKey,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"modId={mod.Id}");
        if(string.IsNullOrWhiteSpace(current.NexusFileId)||current.UploadedAt is null)return false;
        try
        {
            using var doc=await GetJsonAsync($"mod-files/{Uri.EscapeDataString(current.NexusFileId)}/versions",apiKey,ct);
            if(doc is null)return false;
            var versions=doc.RootElement.GetProperty("data").GetProperty("versions").EnumerateArray();
            DateTimeOffset? latest=null;
            foreach(var v in versions)
            {
                var d=TryDate(GetString(v,"uploaded_at"));
                if(d.HasValue&&(!latest.HasValue||d.Value>latest.Value))latest=d;
            }
            var available=latest.HasValue&&latest.Value>current.UploadedAt.Value.AddMinutes(1);
            await db.SetSettingAsync("update:"+mod.Id,available?latest!.Value.ToString("O",CultureInfo.InvariantCulture):string.Empty,ct);
            if(available)MasterDebugLog.Write("UPDATES",$"Update available mod={mod.DisplayName}; current={current.UploadedAt:O}; latest={latest:O}");
            return available;
        }
        catch(HttpRequestException){return false;}catch(JsonException){return false;}
    }

    private static void PruneStalePreviewParts(string dir)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var cutoff=DateTime.UtcNow.AddDays(-1);
            foreach(var file in Directory.EnumerateFiles(dir,"*.part-*",SearchOption.TopDirectoryOnly))
            {
                try{if(File.GetLastWriteTimeUtc(file)<cutoff)File.Delete(file);}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
    }

    private static string SanitizeFileName(string value)
    {
        foreach(var c in Path.GetInvalidFileNameChars())value=value.Replace(c,'_');
        return value;
    }

    private string? ReadApiKey()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var env=Environment.GetEnvironmentVariable("NEXUS_API_KEY");if(!string.IsNullOrWhiteSpace(env))return env.Trim();
        var path=Path.Combine(nextStateRoot,"nexus-api-key.txt");
        if(!File.Exists(path))return null;
        var value=File.ReadAllText(path).Trim();return value.Length==0?null:value;
    }

    private static async Task<JsonDocument?> GetJsonAsync(string relative,string apiKey,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.nexusmods.com/v3/"+relative);
        request.Headers.TryAddWithoutValidation("apikey",apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        MasterDebugLog.Write("HTTP", $"Nexus request START GET /v3/{relative}; apiKey=<redacted>");
        using var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        MasterDebugLog.Write("HTTP", $"Nexus request END GET /v3/{relative}; status={(int)response.StatusCode} {response.StatusCode}; contentLength={response.Content.Headers.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}");
        if(!response.IsSuccessStatusCode)return null;
        await using var stream=await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream,cancellationToken:ct);
    }

    private static HttpClient CreateHttpClient(){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var h=new HttpClient{Timeout=TimeSpan.FromSeconds(12)};h.DefaultRequestHeaders.UserAgent.ParseAdd("Universal-Mod-Manager/8.7.0");return h;}
    private static string? GetString(JsonElement e,string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return e.TryGetProperty(name,out var v)&&v.ValueKind!=JsonValueKind.Null?v.ToString():null;
    }
    private static DateTimeOffset? TryDate(string? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var d)?d:null;
    }
    private static decimal ParsePosition(string? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return decimal.TryParse(value,NumberStyles.Number,CultureInfo.InvariantCulture,out var p)?p:0m;
    }
    private static bool TryPositiveLong(string? value,out long parsed)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out parsed)&&parsed>0;
    }
    private static NexusFileCategory ParseCategory(string? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value?.ToLowerInvariant() switch{"main"=>NexusFileCategory.Main,"update"=>NexusFileCategory.Update,"optional"=>NexusFileCategory.Optional,"old_version"=>NexusFileCategory.OldVersion,"miscellaneous"=>NexusFileCategory.Miscellaneous,"removed"=>NexusFileCategory.Removed,"archived"=>NexusFileCategory.Archived,_=>NexusFileCategory.Unknown};
    }

    private static Dictionary<string,string> ParseIni(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var raw in File.ReadLines(path)){var line=raw.Trim();if(line.Length==0||line.StartsWith(';')||line.StartsWith('#')||line.StartsWith('['))continue;var i=line.IndexOf('=');if(i<=0)continue;result[line[..i].Trim()]=line[(i+1)..].Trim();}
        return result;
    }

    private static string? ParseNexusModId(string? value){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(string.IsNullOrWhiteSpace(value))return null;var url=UrlModRegex().Match(value);if(url.Success)return url.Groups[2].Value;var name=ArchiveModRegex().Match(value);return name.Success?name.Groups[1].Value:null;}
    private static int NameSimilarity(string a,string b){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var at=Tokens(a);var bt=Tokens(b);if(at.Count==0||bt.Count==0)return 0;var hit=at.Count(bt.Contains);return (int)Math.Round(100d*hit/Math.Max(at.Count,bt.Count));}
    private static HashSet<string> Tokens(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return TokenRegex().Matches(value.ToLowerInvariant()).Cast<Match>().Select(m=>m.Value).Where(v=>v.Length>1&&!StopWords.Contains(v)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    private static string NormalizeName(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return string.Join('-',Tokens(value).Order(StringComparer.OrdinalIgnoreCase));
    }
    private static readonly HashSet<string> StopWords=new(StringComparer.OrdinalIgnoreCase){"main","optional","option","update","updated","patch","fix","hotfix","file","version","v"};



    private sealed record ApiCandidate(string FileId,JsonElement Version,int Score,string RawJson);
    public sealed record MetadataRefreshResult(int LocalRecords,int ApiRecords,int SupersededRecords,bool ApiEnabled,bool LiveSyncRan,int VisualsRefreshed,int UpdatesAvailable,int LocalVisuals,int DeclaredVisuals,int PublicVisuals);

    [GeneratedRegex(@"nexusmods\.com/([^/\s]+)/mods/(\d+)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex UrlModRegex();
    [GeneratedRegex(@"-(\d{2,7})-\d+(?:-\d+){0,7}(?:\s*\(\d+\))?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex ArchiveModRegex();
    [GeneratedRegex(@"[a-z0-9]+",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex TokenRegex();
    [GeneratedRegex(@"<meta\b[^>]*>",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex MetaTagRegex();
    [GeneratedRegex("(?:property|name)\\s*=\\s*[\"'](?:og:image|twitter:image)[\"']",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex MetaPropertyRegex();
    [GeneratedRegex("content\\s*=\\s*[\"']([^\"']+)[\"']",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex MetaContentRegex();
}
