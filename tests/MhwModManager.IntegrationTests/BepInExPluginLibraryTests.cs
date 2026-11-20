using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class BepInExPluginLibraryTests
{
    [Fact]
    public void Exact_registered_plugin_metadata_and_bytes_determine_external_rows()
    {
        var home = Directory.CreateTempSubdirectory();
        try
        {
            var game = GameProfile.Generic("a","Game",Path.Combine(home.FullName,"game"),"Game.exe","BepInEx/plugins");
            var plugins = Directory.CreateDirectory(Path.Combine(game.GameRoot,"BepInEx","plugins")).FullName;
            var other = Directory.CreateDirectory(Path.Combine(home.FullName,"managed","wrapper","GameRoot","BepInEx","plugins")).FullName;
            var first = Path.Combine(plugins,"one.dll");
            var second = Path.Combine(plugins,"two.dll");
            var fake = Path.Combine(plugins,"spoof.dll");
            WritePlugin(first);
            WritePlugin(second);
            WritePlugin(fake, "AnotherAssembly");
            File.WriteAllText(Path.Combine(plugins,"broken.dll"),"incomplete");
            var scan = LocalModInventory.Scan([game], [], [], ct: TestContext.Current.CancellationToken);
            var both = LocalModInventory.RegisteredPackageLibrary(scan,game,[], TestContext.Current.CancellationToken);
            Assert.Equal(2,both.Count);
            Assert.All(both, row => { Assert.Equal("fixture.id",row.ItemId); Assert.Equal("Example plugin",row.DisplayName); });
            Assert.NotEqual(both[0].OwnerLabel,both[1].OwnerLabel);
            File.Copy(first, Path.Combine(other,"renamed.dll"));
            var remaining = Assert.Single(LocalModInventory.RegisteredPackageLibrary(scan,game,[Path.Combine(home.FullName,"managed")], TestContext.Current.CancellationToken));
            Assert.Equal(second,Assert.Single(remaining.Locations));
            Assert.Empty(LocalModInventory.RegisteredPackageLibrary(scan,game with { Id="inactive" },[], TestContext.Current.CancellationToken));
            Assert.Empty(BepInExPluginMetadata.Read(fake, TestContext.Current.CancellationToken));
            Assert.Equal(4,Directory.GetFiles(plugins).Length);
        }
        finally { home.Delete(true); }
    }

    [Fact]
    public void ReShade_pair_requires_matching_registered_runtime_and_both_managed_digests()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var profile = GameProfile.Generic("owner","Game",Path.Combine(temp.FullName,"game"),"Game.exe","");
            var runtime = Directory.CreateDirectory(Path.Combine(profile.GameRoot,"Project","Binaries","Win64")).FullName;
            File.WriteAllText(Path.Combine(runtime,"Game-Win64-Shipping.exe"),"fixture");
            var dll = Path.Combine(runtime,"dxgi.dll");
            var ini = Path.Combine(runtime,"ReShade.ini");
            File.WriteAllText(dll,"injector fixture");
            File.WriteAllText(ini,"configuration fixture");
            // Detection verifies the vendor's version resource and shader directory. Exercise its downstream receipt here.
            var verified = new LocalModLocation("Game","dxgi.dll","Registered installation · ReShade injector and configuration",dll)
                { RegisteredProfileId = profile.Id };
            var managed = Directory.CreateDirectory(Path.Combine(temp.FullName,"managed")).FullName;
            File.Copy(dll,Path.Combine(managed,"dxgi.dll"));
            File.WriteAllText(Path.Combine(managed,"ReShade.ini"),"other settings");
            var row = Assert.Single(LocalModInventory.RegisteredPackageLibrary([verified],profile,[managed],TestContext.Current.CancellationToken));
            Assert.Equal("ReShade",row.DisplayName);
            Assert.Equal(2,row.Locations.Count);
            File.Copy(ini,Path.Combine(managed,"ReShade.ini"),true);
            Assert.Empty(LocalModInventory.RegisteredPackageLibrary([verified],profile,[managed],TestContext.Current.CancellationToken));
            Assert.Empty(LocalModInventory.RegisteredPackageLibrary([verified with { RegisteredProfileId = null }],profile,[],TestContext.Current.CancellationToken));
        }
        finally { temp.Delete(true); }
    }

    internal static void WritePlugin(string path, string attributeAssembly = "BepInEx")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Fixture.dll"),metadata.GetOrAddGuid(Guid.NewGuid()),default,default);
        var version = new Version(1,0,0,0);
        metadata.AddAssembly(metadata.GetOrAddString("Fixture"),version,default,default,0,AssemblyHashAlgorithm.None);
        var reference = metadata.AddAssemblyReference(metadata.GetOrAddString(attributeAssembly),version,default,default,0,default);
        var owner = metadata.AddTypeReference(reference,metadata.GetOrAddString("BepInEx"),metadata.GetOrAddString("BepInPlugin"));
        var constructor = metadata.AddMemberReference(owner,metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(new byte[] {0x20,3,1,0x0e,0x0e,0x0e}));
        var field = MetadataTokens.FieldDefinitionHandle(1);
        var method = MetadataTokens.MethodDefinitionHandle(1);
        metadata.AddTypeDefinition(0,default,metadata.GetOrAddString("<Module>"),default,field,method);
        var plugin = metadata.AddTypeDefinition(TypeAttributes.Public,default,metadata.GetOrAddString("Plugin"),default,field,method);
        var args = new BlobBuilder();
        args.WriteUInt16(1);
        args.WriteSerializedString("fixture.id");
        args.WriteSerializedString("Example plugin");
        args.WriteSerializedString("1.0");
        args.WriteUInt16(0);
        metadata.AddCustomAttribute(plugin,constructor,metadata.GetOrAddBlob(args));
        var pe = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata),new BlobBuilder());
        var bytes = new BlobBuilder();
        pe.Serialize(bytes);
        File.WriteAllBytes(path,bytes.ToArray());
    }
}
