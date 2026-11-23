using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class GameProfileTests
{
    private static ProviderCandidate C(string id,string path,string hash,int priority=0)=>new(id,id,priority,new(id,path,hash,null,1,DateTimeOffset.UtcNow,PathRules.ClassifyFile(path)));

    [Fact]
    public void Generic_profile_maps_relative_executable_and_mod_root()
    {
        var profile=GameProfile.Generic("demo","Demo Game",Path.Combine(Path.GetTempPath(),"demo-game"),"Demo.exe",@"Mods");
        Assert.EndsWith(Path.Combine("demo-game","Demo.exe"),profile.ExecutablePath,StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("demo-game","Mods"),profile.LiveModRoot,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GameSupportTier.GenericFolder,profile.SupportTier);
    }

    [Fact]
    public void Generic_profile_rejects_traversal()
    {
        Assert.Throws<ArgumentException>(()=>GameProfile.Generic("bad","Bad",Path.GetTempPath(),"Bad.exe",@"..\Elsewhere"));
    }

    [Theory]
    [InlineData("safe-game",true)]
    [InlineData("game2",true)]
    [InlineData(@"..\\..\\escaped",false)]
    [InlineData(@"bad\\child",false)]
    [InlineData("bad/child",false)]
    [InlineData("Not Canonical",false)]
    [InlineData("UPPERCASE",false)]
    [InlineData(".",false)]
    [InlineData("",false)]
    public void Canonical_profile_id_requires_exact_normalized_single_segment(string id,bool expected)
    {
        Assert.Equal(expected,GameProfile.IsCanonicalId(id));
    }

    [Fact]
    public void Generic_conflict_engine_never_guesses_same_path_texture_winner()
    {
        var profile=GameProfile.Generic("demo","Demo",Path.GetTempPath(),"Demo.exe","Mods");
        var engine=new ConflictEngine(profile);
        var path=@"root\Mods\skin.dds";
        var d=engine.Decide(path,[C("a",path,"aa"),C("b",path,"bb",5)],[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<(string,string),PairStats>());
        Assert.True(d.Blocking);
        Assert.Null(d.WinnerModId);
        Assert.Equal("generic-texture-choice",d.ReasonCode);
    }

    [Theory]
    [InlineData("Mods")]
    [InlineData(@"BepInEx\plugins")]
    [InlineData(@"SomeGame\Content\Paks\~mods")]
    public void Generic_profile_accepts_common_relative_mod_roots(string modRoot)
    {
        var profile=GameProfile.Generic("demo","Demo",Path.Combine(Path.GetTempPath(),"demo-common"),"Demo.exe",modRoot);
        Assert.Equal(modRoot,profile.ModRootRelativePath,StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"..\Mods")]
    [InlineData(@"C:\Mods")]
    [InlineData(@"Mods\..\Elsewhere")]
    public void Generic_profile_rejects_unsafe_mod_roots(string modRoot)
    {
        Assert.Throws<ArgumentException>(()=>GameProfile.Generic("bad","Bad",Path.GetTempPath(),"Bad.exe",modRoot));
    }

    [Fact]
    public void Inferred_game_root_without_verified_loader_cannot_plan_a_live_write()
    {
        var game = GameProfile.Generic("unknown", "Unknown Game", Path.GetTempPath(),
            "Unknown.exe", "", adapterId: "generic-root");
        var mod = new ModDescriptor("unknown-mod", "Unknown mod", "Unknown mod", "src", true, 0);
        var file = new ModFileDescriptor(mod.Id, @"root\settings.bin", new string('a', 64),
            null, 24, DateTimeOffset.UnixEpoch, FileClass.GameData);
        var snapshot = new PlannerSnapshot([mod], [file], [],
            new Dictionary<string,string>(), new Dictionary<string,string>(),
            new Dictionary<string,DeploymentManifestEntry>(), new Dictionary<string,string?>());

        var plan = new DeploymentPlanner(new ConflictEngine(game), game).Build(snapshot);

        Assert.True(plan.IsBlocked);
        Assert.Empty(plan.Changes);
        Assert.Contains(plan.Conflicts, x => x.ReasonCode == "unverified-game-root-target" && x.Blocking);
    }

    [Theory]
    [InlineData("mhw", "nativePC")]
    [InlineData("unreal-paks", @"SB\Content\Paks\~mods")]
    [InlineData("bepinex", @"BepInEx\plugins")]
    [InlineData("mods-folder", "Mods")]
    public void Verified_existing_game_roots_preserve_direct_deployment_planning(string adapter, string modRoot)
    {
        var game = adapter == "mhw"
            ? GameProfile.MonsterHunterWorld(Path.GetTempPath())
            : GameProfile.Generic("other", "Other Game", Path.GetTempPath(),
                "Other.exe", modRoot, adapterId: adapter);
        var path = adapter == "mhw" ? @"nativePC\mod\a.bin"
            : @"root\" + modRoot + @"\a.bin";
        var mod = new ModDescriptor("a", "A", "A", "src", true, 0);
        var file = new ModFileDescriptor(mod.Id, path, new string('b', 64),
            null, 24, DateTimeOffset.UnixEpoch, FileClass.GameData);
        var snapshot = new PlannerSnapshot([mod], [file], [],
            new Dictionary<string,string>(), new Dictionary<string,string>(),
            new Dictionary<string,DeploymentManifestEntry>(), new Dictionary<string,string?>());

        var plan = new DeploymentPlanner(new ConflictEngine(game), game).Build(snapshot);

        Assert.False(plan.IsBlocked);
        Assert.Single(plan.Changes);
    }

}
