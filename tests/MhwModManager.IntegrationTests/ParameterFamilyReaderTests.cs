using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ParameterFamilyReaderTests : IDisposable
{
    private static readonly string[] UnprovenHelpers = ["AltCameraTakedown","zNoTakeDownCam"];
    private static readonly string[] LightingGroups = ["Courtyard","River"];
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("parameter-family-");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Pair(string relative, string stem)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root.FullName,relative));
        foreach (var extension in new[]{".pak",".sig"})
            File.WriteAllText(Path.Combine(directory.FullName,stem+extension),stem+extension);
    }

    private void Seed()
    {
        File.WriteAllText(Path.Combine(root.FullName,"README.txt"),"Keep both .pak and .sig files together, with the same name. Choose per situation by mixing and matching based on personal preference.\r\n" +
            "NoLookAt = Prevents the camera from adjusting position to look at enemies when left at rest in 1vX scenarios\r\n" +
            "AltCameraTakedown = Keep the usual field of view during takedowns.\r\n" +
            "NoTakeDownCam = Prevent position and field of view adjustment during takedowns.");
        foreach (var setting in new[]{90,105,120,135})
        {
            foreach (var situation in new[]{"Explo","1v1","1vX"})
                Pair(setting+" FOV","AltCamera"+setting+"FOV"+situation);
            if (setting != 135)
                foreach (var room in new[]{"Flood","OldBoy","Stairs"})
                    Pair(Path.Combine(setting+" FOV","Fixed"),"AltCamera"+setting+"FOV"+room);
        }
        foreach (var room in new[]{"Flood","Stairs","Park","DataCenter"})
            Pair("NoFixedCam","zNoFixedCam"+room);
        foreach (var name in new[]{"AltCameraTakedown","zNoTakeDownCam","zNoLookAt"})
            Pair("Other",name);
    }

    [Fact]
    public void ShellMetadataInsideOptionsStaysInTheSourceAndOutOfEveryComponent()
    {
        Seed();
        var annotations=Directory.GetDirectories(root.FullName,"*",SearchOption.AllDirectories).Take(7)
            .Select((dir,i)=>Path.Combine(dir,i==0 ? "Thumbs.db" : "desktop.ini")).ToArray();
        foreach(var path in annotations) File.WriteAllText(path,"shell metadata");
        var parts=ParameterFamilyReader.Read(root.FullName,Ct);
        Assert.Equal(28,parts.Count);
        Assert.Equal(56,parts.SelectMany(part=>part.Files!).Count());
        Assert.All(annotations,path=>Assert.Equal("shell metadata",File.ReadAllText(path)));
    }

    [Fact]
    public void AuthorAndCompleteSourceProveActualSituationAndRoomChoicesWithoutInventingHelpers()
    {
        Seed();
        var parts = ParameterFamilyReader.Read(root.FullName,Ct);
        Assert.Equal(28,parts.Count);
        var expected = new Dictionary<string,int>{["Explo"]=4,["1v1"]=4,["1vX"]=4,["Flood"]=4,["Stairs"]=4,
            ["OldBoy"]=3,["Park"]=1,["DataCenter"]=1};
        Assert.Equal(expected.OrderBy(p=>p.Key),parts.Where(p=>p.Role=="Option").GroupBy(p=>p.ChoiceGroup!)
            .ToDictionary(group=>group.Key,group=>group.Count()).OrderBy(p=>p.Key));
        Assert.Equal("zNoLookAt",Assert.Single(parts,p=>p.Role=="Optional").Label);
        Assert.Equal(UnprovenHelpers,parts.Where(p=>p.Role=="Review").Select(p=>p.Label));
        var allFiles = parts.SelectMany(p=>p.Files!.Select(f=>Path.Combine(p.SourceDirectory,f))).ToArray();
        Assert.Equal(56,allFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(parts,p=>Assert.Equal(new[]{p.Label+".pak",p.Label+".sig"},p.Files));
        Assert.All(allFiles,path=>Assert.Equal(Path.GetFileName(path),File.ReadAllText(path)));
    }

    [Theory]
    [InlineData("missing companion")]
    [InlineData("extra file")]
    [InlineData("missing situation")]
    [InlineData("no author evidence")]
    [InlineData("author forbids mixing")]
    public void IncompleteOrContradictoryStructureStaysForReview(string mutation)
    {
        Seed();
        var directory = Path.Combine(root.FullName,"90 FOV");
        switch (mutation)
        {
            case "missing companion": File.Delete(Path.Combine(directory,"AltCamera90FOVExplo.sig")); break;
            case "extra file": File.WriteAllText(Path.Combine(directory,"unknown.dll"),"unknown"); break;
            case "missing situation":
                File.Delete(Path.Combine(directory,"AltCamera90FOVExplo.pak"));
                File.Delete(Path.Combine(directory,"AltCamera90FOVExplo.sig")); break;
            case "no author evidence": File.WriteAllText(Path.Combine(root.FullName,"README.txt"),"Downloads"); break;
            case "author forbids mixing": File.WriteAllText(Path.Combine(root.FullName,"README.txt"),"Do not mix and match .pak and .sig pairs."); break;
        }
        Assert.Empty(ParameterFamilyReader.Read(root.FullName,Ct));
    }

    [Fact]
    public void ASecondParameterAndDifferentSettingNamesUseTheSameBoundaries()
    {
        File.WriteAllText(Path.Combine(root.FullName,"README.txt"),"Mix and match by situation; keep both .pak and .sig companions.");
        foreach (var step in new[]{12,20})
            foreach (var situation in new[]{"River","Courtyard"})
                Pair(step+" Strength","Lighting"+step+"Strength"+situation);
        var parts = ParameterFamilyReader.Read(root.FullName,Ct);
        Assert.Equal(4,parts.Count);
        Assert.Equal(LightingGroups,parts.Select(p=>p.ChoiceGroup).Distinct().Order());
        Assert.All(parts,p=>Assert.Equal("Option",p.Role));
    }

    public void Dispose() => root.Delete(recursive:true);
}
