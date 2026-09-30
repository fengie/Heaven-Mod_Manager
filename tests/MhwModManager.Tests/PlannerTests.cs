using MhwModManager.Core;using Xunit;
namespace MhwModManager.Tests;
public sealed class PlannerTests
{
    [Fact] public void Same_input_is_deterministic_except_plan_identity(){var now=DateTimeOffset.UnixEpoch;var mods=new[]{new ModDescriptor("a","A","A","A",true,0),new ModDescriptor("b","B","B","B",true,1)};var files=new[]{new ModFileDescriptor("a",@"nativePC\x.tex","aa",null,1,now,FileClass.Texture),new ModFileDescriptor("b",@"nativePC\x.tex","bb",null,1,now,FileClass.Texture)};var s=new PlannerSnapshot(mods,files,[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<string,DeploymentManifestEntry>(),new Dictionary<string,string?>());var p=new DeploymentPlanner(new ConflictEngine());var a=p.Build(s);var b=p.Build(s);Assert.Equal(a.Changes.Select(x=>(x.Kind,x.Path,x.AfterBlobSha256)),b.Changes.Select(x=>(x.Kind,x.Path,x.AfterBlobSha256)));Assert.Equal(a.Conflicts.Select(x=>(x.Kind,x.WinnerModId)),b.Conflicts.Select(x=>(x.Kind,x.WinnerModId)));}
    [Fact] public void Disabled_provider_is_not_considered(){var now=DateTimeOffset.UnixEpoch;var mods=new[]{new ModDescriptor("a","A","A","A",true,0),new ModDescriptor("b","B","B","B",false,1)};var files=new[]{new ModFileDescriptor("a",@"nativePC\x.mod3","aa",null,1,now,FileClass.Structural),new ModFileDescriptor("b",@"nativePC\x.mod3","bb",null,1,now,FileClass.Structural)};var s=new PlannerSnapshot(mods,files,[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<string,DeploymentManifestEntry>(),new Dictionary<string,string?>());var plan=new DeploymentPlanner(new ConflictEngine()).Build(s);Assert.False(plan.IsBlocked);Assert.Single(plan.Changes);Assert.Equal("a",plan.Changes[0].ProviderAfter);}

    [Fact]
    public void Malformed_enabled_overlay_rule_blocks_before_resolution()
    {
        var now=DateTimeOffset.UnixEpoch;
        var mods=new[]{new ModDescriptor("a","A","A","A",true,0),new ModDescriptor("b","B","B","B",true,1)};
        var path=@"nativePC\fixture\same.mod3";
        var files=new[]{new ModFileDescriptor("a",path,"aa",null,1,now,FileClass.Structural),new ModFileDescriptor("b",path,"bb",null,1,now,FileClass.Structural)};
        var invalid=new ConflictRule("bad-overlay",RuleKind.Overlay,RuleScope.ModPair,"a","b","not-an-endpoint",null,"invalid fixture",true,now);
        var snapshot=new PlannerSnapshot(mods,files,[invalid],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<string,DeploymentManifestEntry>(),new Dictionary<string,string?>());

        var plan=new DeploymentPlanner(new ConflictEngine()).Build(snapshot);

        Assert.True(plan.IsBlocked);
        var blocker=Assert.Single(plan.Conflicts);
        Assert.Equal("<rules>",blocker.Path);
        Assert.Equal("invalid-overlay-rule",blocker.ReasonCode);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void Unrelated_structural_siblings_in_one_atomic_bundle_block_even_without_same_path()
    {
        var now=DateTimeOffset.UnixEpoch;
        var mods=new[]{new ModDescriptor("model","Model Mod","Model Mod","A",true,0),new ModDescriptor("material","Material Mod","Material Mod","B",true,1)};
        var files=new[]{
            new ModFileDescriptor("model",@"nativePC\fixture\armor\body\asset.mod3","aa",null,1,now,FileClass.Structural),
            new ModFileDescriptor("material",@"nativePC\fixture\armor\body\asset.mrl3","bb",null,1,now,FileClass.Structural)
        };
        var snapshot=new PlannerSnapshot(mods,files,[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<string,DeploymentManifestEntry>(),new Dictionary<string,string?>());

        var plan=new DeploymentPlanner(new ConflictEngine()).Build(snapshot);

        Assert.True(plan.IsBlocked);
        var blocker=Assert.Single(plan.Conflicts.Where(x=>x.Blocking));
        Assert.Equal("bundle-mixed-providers",blocker.ReasonCode);
        Assert.Equal(ConflictKind.HardStructural,blocker.Kind);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void Proven_main_and_optional_family_may_compose_distinct_structural_siblings()
    {
        var now=DateTimeOffset.UnixEpoch;
        var mods=new[]{
            new ModDescriptor("main","Armor Main","Armor Main","A",true,0,FamilyId:"family:armor",NexusCategory:NexusFileCategory.Main),
            new ModDescriptor("option","Armor Optional Physics","Armor Optional Physics","B",true,1,FamilyId:"family:armor",NexusCategory:NexusFileCategory.Optional)
        };
        var files=new[]{
            new ModFileDescriptor("main",@"nativePC\fixture\armor\body\asset.mod3","aa",null,1,now,FileClass.Structural),
            new ModFileDescriptor("option",@"nativePC\fixture\armor\body\asset.mrl3","bb",null,1,now,FileClass.Structural)
        };
        var snapshot=new PlannerSnapshot(mods,files,[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<string,DeploymentManifestEntry>(),new Dictionary<string,string?>());

        var plan=new DeploymentPlanner(new ConflictEngine()).Build(snapshot);

        Assert.False(plan.IsBlocked);
        Assert.Equal(2,plan.Changes.Count);
        Assert.Contains(plan.Changes,x=>x.ProviderAfter=="main");
        Assert.Contains(plan.Changes,x=>x.ProviderAfter=="option");
    }

}
