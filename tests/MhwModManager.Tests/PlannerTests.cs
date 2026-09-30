using MhwModManager.Core;using Xunit;
namespace MhwModManager.Tests;
public sealed class PlannerTests
{
    [Fact] public void Same_input_is_deterministic_except_plan_identity(){var now=DateTimeOffset.UnixEpoch;var mods=new[]{new ModDescriptor("a","A","A","A",true,0),new ModDescriptor("b","B","B","B",true,1)};var files=new[]{new ModFileDescriptor("a",@"nativePC\x.tex","aa",null,1,now,FileClass.Texture),new ModFileDescriptor("b",@"nativePC\x.tex","bb",null,1,now,FileClass.Texture)};var s=new PlannerSnapshot(mods,files,[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<string,DeploymentManifestEntry>(),new Dictionary<string,string?>());var p=new DeploymentPlanner(new ConflictEngine());var a=p.Build(s);var b=p.Build(s);Assert.Equal(a.Changes.Select(x=>(x.Kind,x.Path,x.AfterBlobSha256)),b.Changes.Select(x=>(x.Kind,x.Path,x.AfterBlobSha256)));Assert.Equal(a.Conflicts.Select(x=>(x.Kind,x.WinnerModId)),b.Conflicts.Select(x=>(x.Kind,x.WinnerModId)));}
    [Fact] public void Disabled_provider_is_not_considered(){var now=DateTimeOffset.UnixEpoch;var mods=new[]{new ModDescriptor("a","A","A","A",true,0),new ModDescriptor("b","B","B","B",false,1)};var files=new[]{new ModFileDescriptor("a",@"nativePC\x.mod3","aa",null,1,now,FileClass.Structural),new ModFileDescriptor("b",@"nativePC\x.mod3","bb",null,1,now,FileClass.Structural)};var s=new PlannerSnapshot(mods,files,[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<string,DeploymentManifestEntry>(),new Dictionary<string,string?>());var plan=new DeploymentPlanner(new ConflictEngine()).Build(s);Assert.False(plan.IsBlocked);Assert.Single(plan.Changes);Assert.Equal("a",plan.Changes[0].ProviderAfter);}

    [Fact] public void File_directory_collision_is_always_blocking()
    {
        var now=DateTimeOffset.UnixEpoch;
        var mods=new[]{new ModDescriptor("file","File Provider","File Provider","file",true,10),new ModDescriptor("child","Child Provider","Child Provider","child",true,20)};
        var files=new[]{
            new ModFileDescriptor("file",@"nativePC\plugins\collision","aa",null,1,now,FileClass.Other),
            new ModFileDescriptor("child",@"nativePC\plugins\collision\child.bin","bb",null,1,now,FileClass.GameData)
        };
        var snapshot=new PlannerSnapshot(mods,files,[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<string,DeploymentManifestEntry>(),new Dictionary<string,string?>());

        var plan=new DeploymentPlanner(new ConflictEngine()).Build(snapshot);

        Assert.True(plan.IsBlocked);
        Assert.Contains(plan.Conflicts,x=>x.ReasonCode=="file-directory-collision");
        Assert.Empty(plan.Changes);
    }
}
