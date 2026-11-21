using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class InstalledPackageLibraryTests
{
    [Fact]
    public void DiscoveryGroupsOwnedAndPreviousLocationsAndExcludesUnprovenPackages()
    {
        var ct = TestContext.Current.CancellationToken;
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var root = Path.Combine(temp.FullName,"Steam","steamapps","common","Sample");
            var previous = Path.Combine(temp.FullName,"former");
            var steam = Path.Combine(temp.FullName,"OtherSteam");
            var guessed = Path.Combine(steam,"steamapps","common","Sample");
            var profile = GameProfile.Generic("sample","Sample",root,"Project/Binaries/Win64/Sample.exe","Project/Content/Paks","unreal-paks","123");
            string Put(string install,string path,string bytes = "test")
            {
                var file=Path.GetFullPath(Path.Combine(install,path));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file,bytes);
                return file;
            }
            const string mount = "Project/Content/Paks/";
            var first=Put(root,mount+"~mods/Outfit.pak");
            var toc=Put(root,mount+"~mods/Outfit.utoc");
            Put(root,mount+"~mods/Outfit.ucas");
            Put(previous,mount+"~mods/Outfit.pak");
            Put(guessed,mount+"~mods/Guessed.pak");
            Put(root,mount+"Stock.pak");
            Put(root,mount+"~mods/Incomplete.pak");
            Put(root,mount+"~mods/Incomplete.ucas");
            Put(root,"Mods/Sound/mod.json","{\"name\":\"Quiet sounds\"}");
            Put(root,"Mods/Sound/Sound.lua");
            Put(root,"Mods/Invalid/mod.json","broken");
            Put(root,"Mods/Invalid/Helper.dll");
            Put(root,"UE4SS/Mods/Actual/Scripts/main.lua");
            Put(root,"UE4SS/Mods/Actual/dlls/main.dll");
            Put(root,"Wrapper/Project/Binaries/Win64/bitfix/sig.lua");
            Put(root,"Wrapper/Project/Binaries/Win64/dsound.dll");
            Put(root,"reframework/autorun/Weather.lua");
            var runtime=Put(root,"Project/Binaries/Win64/nvngx_dlssnr.dll");
            var ini=Put(root,"Project/Binaries/Win64/settings.ini","user edit");
            var addon=Put(root,"Project/Binaries/Win64/renodx-dlss.addon64");
            var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(addon)));
            using (var large = File.OpenWrite(runtime)) large.SetLength(9*1024*1024);
            string Receipt(string[] dests) => JsonSerializer.Serialize(new{product="DLSS Nexus",status="Complete",files=dests.Select(path=>new{destination=path,installedHash=hash})});
            Put(root,"DLSS-Nexus-Backups/now/installation.json",Receipt([runtime,addon,ini]));
            Put(root,"DLSS-Nexus-Backups/stale/installation.json",Receipt([runtime,Path.Combine(root,"absent.dll")]));
            Put(root,"DLSS-Nexus-Backups/escape/installation.json",Receipt([runtime,Put(temp.FullName,"other.dll")]));
            var inventory = LocalModInventory.Scan([profile],[steam],[],new Dictionary<string,string[]>{{profile.Id,[previous]}},ct);
            var pair = Assert.Single(inventory,r=>r.Name=="sig.lua");
            Assert.Equal("Paired loader files · runtime use unconfirmed",pair.FileEvidence);
            Assert.True(File.Exists(Path.Combine(Directory.GetParent(Path.GetDirectoryName(pair.Location)!)!.FullName,"dsound.dll")));
            var none=new HashSet<string>(PathRules.Comparer);
            var all=LocalModInventory.InstalledPackages(inventory,profile,none,ct);
            Assert.Equal("Actual|Bitfix loader|DLSS Nexus|Outfit|Outfit|Quiet sounds|Weather",string.Join("|",all.Select(r=>r.DisplayName).Order(StringComparer.Ordinal)));
            var outfits=all.Where(r=>r.DisplayName=="Outfit").ToArray();
            Assert.Equal(2,outfits.Length);
            Assert.NotEqual(outfits[0].ItemId,outfits[1].ItemId);
            Assert.Equal(3,Assert.Single(outfits,r=>!r.OwnerLabel.Contains("Previous",StringComparison.Ordinal)).Locations.Count);
            Assert.Equal(2,Assert.Single(all,r=>r.DisplayName=="Quiet sounds").Locations.Count);
            Assert.Equal(2,Assert.Single(all,r=>r.DisplayName=="Actual").Locations.Count);
            Assert.Contains("source location",Assert.Single(all,r=>r.DisplayName=="Bitfix loader").OwnerLabel);
            Assert.Single(Assert.Single(all,r=>r.DisplayName=="DLSS Nexus").Locations);
            Assert.Contains(all,r=>r.DisplayName=="Weather");
            var partial=LocalModInventory.InstalledPackages(inventory,profile,new HashSet<string>([toc],PathRules.Comparer),ct);
            Assert.Equal(2,Assert.Single(partial,r=>r.OwnerLabel.Contains("partially managed",StringComparison.Ordinal)).Locations.Count);
            var owned=outfits.Single(r=>r.Locations.Contains(first)).Locations.ToHashSet(PathRules.Comparer);
            Assert.DoesNotContain(LocalModInventory.InstalledPackages(inventory,profile,owned,ct),r=>r.Locations.Contains(first));
            Assert.Equal("user edit",File.ReadAllText(ini));
        }
        finally {temp.Delete(true);}
    }

    [Fact]
    public Task CopiedLoaderRequiresBothExactMembersAndRetainsDisabledOwner() => IsolatedWpfSession.RunAsync(async (services,ct) =>
    {
        string Pair(string root)
        {
            var runtime = Directory.CreateDirectory(Path.Combine(root,"Project","Binaries","Win64")).FullName;
            var bitfix = Directory.CreateDirectory(Path.Combine(runtime,"bitfix")).FullName;
            File.WriteAllText(Path.Combine(bitfix,"sig.lua"),"original");
            var dll = Path.Combine(runtime,"dsound.dll");
            File.WriteAllText(dll,"matching");
            return dll;
        }
        var live = Pair(Path.Combine(services.Paths.GameRoot,"wrapped"));
        var source = Path.Combine(services.Paths.ToolRoot,"owned");
        var owned = Pair(source);
        var mod = new ModDescriptor("managed","Loader","My loader",source,false,7);
        await services.Database.UpsertModAsync(mod,ct);
        var observed = LocalModInventory.Scan([services.Paths.Game],[],[],ct:ct);
        var titles = new Dictionary<string,string>(PathRules.Comparer);
        var represented = await services.Adoption.RepresentedInventoryPathsAsync(observed,[mod],services.Executor.Destination,ct,titles);
        Assert.Contains(live,represented);
        Assert.Contains("My loader",titles.Values);
        Assert.Empty(LocalModInventory.InstalledPackages(observed,services.Paths.Game,represented,ct));
        File.WriteAllText(owned,"changed");
        represented = await services.Adoption.RepresentedInventoryPathsAsync(observed,[mod],services.Executor.Destination,ct);
        Assert.DoesNotContain(live,represented);
        Assert.Equal("Bitfix loader",Assert.Single(LocalModInventory.InstalledPackages(observed,services.Paths.Game,represented,ct)).DisplayName);
        Assert.False(Assert.Single(await services.Database.GetModsAsync(ct)).Enabled);
        Assert.Equal("matching",File.ReadAllText(live));
    });
}
