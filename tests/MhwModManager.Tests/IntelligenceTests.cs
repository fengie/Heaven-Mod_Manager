using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class IntelligenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddYears(50);

    [Fact]
    public void Nexus_optional_overrides_main_even_when_priority_is_lower()
    {
        var mods = new[]
        {
            Mod("main","HPN Armor Main",priority:900,nexusModId:"42",nexusUuid:"uuid-42",category:NexusFileCategory.Main,versionId:"main-v1",fileId:"file-main"),
            Mod("optional","HPN Armor Optional Waist",priority:1,nexusModId:"42",nexusUuid:"uuid-42",category:NexusFileCategory.Optional,versionId:"opt-v1",fileId:"file-opt")
        };
        var path=@"nativePC\pl\f_equip\pl032_0010\wst\mod\f_wst032_0010.mod3";
        var files=new[]
        {
            File("main",path,"aa",FileClass.Structural),
            File("optional",path,"bb",FileClass.Structural)
        };

        var plan=new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods,files));

        Assert.False(plan.IsBlocked);
        var decision=Assert.Single(plan.Conflicts);
        Assert.Equal("optional",decision.WinnerModId);
        Assert.InRange(decision.ResolverScore,99,100);
        Assert.Contains("Nexus",decision.Evidence??string.Empty);
    }

    [Fact]
    public void Nexus_update_overrides_optional_when_both_overlap()
    {
        var mods=new[]
        {
            Mod("optional","Armor Optional",priority:999,nexusModId:"42",nexusUuid:"uuid-42",category:NexusFileCategory.Optional,versionId:"opt-v1",fileId:"opt-file"),
            Mod("update","Armor Update",priority:1,nexusModId:"42",nexusUuid:"uuid-42",category:NexusFileCategory.Update,versionId:"upd-v1",fileId:"upd-file")
        };
        var path=@"nativePC\pl\f_equip\pl032_0010\body\mod\f_body032_0010.mod3";
        var plan=new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods,[File("optional",path,"aa",FileClass.Structural),File("update",path,"bb",FileClass.Structural)]));

        Assert.False(plan.IsBlocked);
        Assert.Equal("update",Assert.Single(plan.Conflicts).WinnerModId);
    }

    [Fact]
    public void Nexus_same_file_chain_hides_older_revision()
    {
        var mods=new[]
        {
            Mod("old","HPN Skin",nexusModId:"9",nexusUuid:"uuid-9",category:NexusFileCategory.Main,versionId:"v1",fileId:"file-chain",uploaded:Now),
            Mod("new","HPN Skin",nexusModId:"9",nexusUuid:"uuid-9",category:NexusFileCategory.Main,versionId:"v2",previous:"v1",fileId:"file-chain",uploaded:Now.AddDays(1))
        };

        var links=ProvenanceIntelligence.BuildSupersessionLinks(mods);

        var link=Assert.Single(links);
        Assert.Equal("old",link.older);
        Assert.Equal("new",link.newer);
        Assert.Equal(100,link.score);
    }

    [Fact]
    public void Different_known_nexus_file_chains_are_not_hidden_by_matching_labels()
    {
        var mods=new[]
        {
            Mod("main","Same Label",nexusModId:"9",nexusUuid:"uuid-9",category:NexusFileCategory.Main,fileId:"main-chain",uploaded:Now),
            Mod("optional","Same Label",nexusModId:"9",nexusUuid:"uuid-9",category:NexusFileCategory.Optional,fileId:"optional-chain",uploaded:Now.AddDays(1))
        };

        Assert.Empty(ProvenanceIntelligence.BuildSupersessionLinks(mods));
    }

    [Fact]
    public void Independent_texture_replacers_require_a_human_choice()
    {
        var mods=new[]
        {
            Mod("red","Red recolor",priority:1),
            Mod("blue","Blue recolor",priority:999)
        };
        var path=@"nativePC\pl\f_equip\somearmor\body\albd.tex";
        var plan=new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods,[File("red",path,"aa",FileClass.Texture),File("blue",path,"bb",FileClass.Texture)]));

        Assert.True(plan.IsBlocked);
        var conflict=Assert.Single(plan.Conflicts);
        Assert.Equal(ConflictKind.TextureOverride,conflict.Kind);
        Assert.Equal("independent-texture-replacement",conflict.ReasonCode);
    }


    [Fact]
    public void Dedicated_texture_pack_wins_globally_with_three_or_more_providers()
    {
        var mods=new[]
        {
            Mod("armor-a","HPN Armor A",priority:100),
            Mod("armor-b","HPN Armor B",priority:200),
            Mod("skin","Updated HPN Skin Textures",priority:1)
        };
        var path=@"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex";
        var files=new[]
        {
            File("armor-a",path,"aa",FileClass.Texture),
            File("armor-b",path,"bb",FileClass.Texture),
            File("skin",path,"cc",FileClass.Texture),
            File("armor-a",@"nativePC\pl\f_equip\pl001_0000\body\mod\a.mod3","dd",FileClass.Structural),
            File("armor-b",@"nativePC\pl\f_equip\pl002_0000\body\mod\b.mod3","ee",FileClass.Structural),
            File("skin",@"nativePC\pl\f_equip\mod_hepsy\f_skin_BM.tex","ff",FileClass.Texture)
        };

        var plan=new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods,files));

        Assert.False(plan.IsBlocked);
        var decision=plan.Conflicts.Single(x=>PathRules.Comparer.Equals(x.Path,path));
        Assert.Equal("skin",decision.WinnerModId);
        Assert.Equal("dedicated-texture-provider",decision.ReasonCode);
    }

    [Fact]
    public void Local_texture_v2_archives_v1_only_when_lineage_and_paths_match()
    {
        var mods=new[]
        {
            Mod("old","HPN Skin Textures v1"),
            Mod("new","HPN Skin Textures v2")
        };
        var shared=@"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex";
        var links=ProvenanceIntelligence.BuildLocalTextureSupersessionLinks(mods,[
            File("old",shared,"aa",FileClass.Texture),
            File("new",shared,"bb",FileClass.Texture)]);

        var link=Assert.Single(links);
        Assert.Equal("old",link.older);
        Assert.Equal("new",link.newer);
        Assert.Equal(94,link.score);
    }

    [Fact]
    public void Local_structural_v2_is_not_auto_archived()
    {
        var mods=new[]{Mod("old","Armor v1"),Mod("new","Armor v2")};
        var shared=@"nativePC\pl\f_equip\pl032_0010\body\mod\f_body032_0010.mod3";
        var links=ProvenanceIntelligence.BuildLocalTextureSupersessionLinks(mods,[
            File("old",shared,"aa",FileClass.Structural),
            File("new",shared,"bb",FileClass.Structural)]);

        Assert.Empty(links);
    }

    [Fact]
    public void Structural_siblings_share_one_atomic_bundle()
    {
        var basePath=@"nativePC\pl\f_equip\pl032_0010\body\mod\f_body032_0010";
        var keys=new[]
        {
            AssetBundles.KeyForPath(basePath+".mod3"),
            AssetBundles.KeyForPath(basePath+".mrl3"),
            AssetBundles.KeyForPath(@"nativePC\pl\f_equip\pl032_0010\body\ctc\f_body032_0010.ctc"),
            AssetBundles.KeyForPath(@"nativePC\pl\f_equip\pl032_0010\body\ctc\f_body032_0010.ccl")
        };

        Assert.Single(keys.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Superseded_enabled_source_is_excluded_from_planning()
    {
        var old=Mod("old","Armor v1",priority:999) with{Enabled=true,IsSuperseded=true,SupersededByModId="new"};
        var current=Mod("new","Armor v2",priority:1) with{Enabled=true};
        var path=@"nativePC\pl\f_equip\pl032_0010\body\mod\f_body032_0010.mod3";
        var plan=new DeploymentPlanner(new ConflictEngine()).Build(Snapshot([old,current],[File("old",path,"aa",FileClass.Structural),File("new",path,"bb",FileClass.Structural)]));

        Assert.False(plan.IsBlocked);
        Assert.Single(plan.Changes);
        Assert.Equal("new",plan.Changes[0].ProviderAfter);
    }

    [Fact]
    public void Logical_family_keeps_superseded_revision_archived_not_visible()
    {
        var old=Mod("old","HPN Skin Textures v1",enabled:false) with{IsSuperseded=true,SupersededByModId="new"};
        var current=Mod("new","HPN Skin Textures v2",enabled:false);

        var family=Assert.Single(LogicalModFamilies.Build([old,current]));

        Assert.Single(family.Members);
        Assert.Equal("new",family.Members[0].Id);
        Assert.Single(family.SupersededMembers);
        Assert.Equal("old",family.SupersededMembers[0].Id);
    }

    private static ModDescriptor Mod(
        string id,string name,bool enabled=true,int priority=0,string? nexusModId=null,string? nexusUuid=null,
        NexusFileCategory category=NexusFileCategory.Unknown,string? versionId=null,string? previous=null,string? fileId=null,DateTimeOffset? uploaded=null) =>
        new(id,name,name,id,enabled,priority,NexusModId:nexusModId,NexusFileId:fileId,NexusModUuid:nexusUuid,
            NexusVersionId:versionId,NexusPreviousVersionId:previous,NexusCategory:category,NexusUploadedAt:uploaded);

    private static ModFileDescriptor File(string modId,string path,string hash,FileClass fileClass) =>
        new(modId,path,hash,null,100,Now,fileClass);

    private static PlannerSnapshot Snapshot(ModDescriptor[] mods,ModFileDescriptor[] files) =>
        new(mods,files,[],new Dictionary<string,string>(),new Dictionary<string,string>(),
            new Dictionary<string,DeploymentManifestEntry>(),new Dictionary<string,string?>());
}
