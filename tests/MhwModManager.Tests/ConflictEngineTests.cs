using MhwModManager.Core;using Xunit;
namespace MhwModManager.Tests;
public sealed class ConflictEngineTests
{
    [Fact] public void Protected_loader_bootstrap_collision_fails_closed()
    {
        var p=@"root\dinput8.dll";
        var e=new ConflictEngine();
        var d=e.Decide(p,[C("loader-a","Loader A",p,"aa",1),C("loader-b","Loader B",p,"bb",99)],[],
            new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<(string,string),PairStats>());
        Assert.True(d.Blocking);
        Assert.Equal(ConflictKind.HardGameData,d.Kind);
        Assert.Equal("protected-bootstrap-collision",d.ReasonCode);
        Assert.Null(d.WinnerModId);
    }

    [Fact] public void Explicit_exact_winner_can_resolve_protected_loader_collision()
    {
        var p=@"root\loader.dll";
        var e=new ConflictEngine();
        var d=e.Decide(p,[C("loader-a","Loader A",p,"aa",1),C("loader-b","Loader B",p,"bb",99)],[],
            new Dictionary<string,string>{{p,"loader-a"}},new Dictionary<string,string>(),new Dictionary<(string,string),PairStats>());
        Assert.False(d.Blocking);
        Assert.Equal(ConflictKind.UserOverlayRule,d.Kind);
        Assert.Equal("loader-a",d.WinnerModId);
    }

    private static ProviderCandidate C(string id,string name,string path,string hash,int priority=0)=>new(id,name,priority,new(id,path,hash,null,1,DateTimeOffset.UtcNow,PathRules.ClassifyFile(path)));
    [Fact] public void Identical_is_non_blocking(){var e=new ConflictEngine();var d=e.Decide(@"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex",[C("a","A",@"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex","aa"),C("b","B",@"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex","aa",1)],[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<(string,string),PairStats>());Assert.Equal(ConflictKind.Identical,d.Kind);Assert.False(d.Blocking);}
    [Fact] public void Independent_texture_replacers_require_choice(){var p=@"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex";var e=new ConflictEngine();var d=e.Decide(p,[C("a","Red Recolor",p,"aa"),C("b","Blue Recolor",p,"bb",2)],[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<(string,string),PairStats>());Assert.Equal(ConflictKind.TextureOverride,d.Kind);Assert.Null(d.WinnerModId);Assert.True(d.Blocking);Assert.Equal("independent-texture-replacement",d.ReasonCode);}
    [Fact] public void Structural_overlap_fails_closed(){var p=@"nativePC\pl\f_equip\pl032_0010\body\mod\f_body032_0010.mod3";var e=new ConflictEngine();var d=e.Decide(p,[C("a","Armor A",p,"aa"),C("b","Unrelated B",p,"bb",2)],[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<(string,string),PairStats>());Assert.Equal(ConflictKind.HardStructural,d.Kind);Assert.True(d.Blocking);}
    [Fact] public void Child_option_overlays_base(){var p=@"nativePC\pl\f_equip\pl032_0010\wst\mod\f_wst032_0010.mod3";var e=new ConflictEngine();var d=e.Decide(p,[C("a","HPN Sexy Kulu-Ya-Ku",p,"aa"),C("b","HPN Sexy Kulu-Ya-Ku - Skimpy Waist",p,"bb",2)],[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<(string,string),PairStats>());Assert.Equal(ConflictKind.PatchOverlay,d.Kind);Assert.Equal("b",d.WinnerModId);}

    [Fact] public void Shared_resource_namespace_word_recolor_does_not_prove_lineage(){var p=@"nativePC\pl\f_equip\mod_generic\f_skin_NM.tex";var e=new ConflictEngine();var d=e.Decide(p,[C("a","Red Recolor",p,"aa"),C("b","Blue Recolor",p,"bb",2)],[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<(string,string),PairStats>());Assert.Equal(ConflictKind.TextureOverride,d.Kind);Assert.True(d.Blocking);Assert.Null(d.WinnerModId);Assert.Equal("independent-texture-replacement",d.ReasonCode);}
    [Fact] public void Family_texture_siblings_without_revision_evidence_require_choice(){var p=@"nativePC\pl\f_equip\mod_generic\f_skin_NM.tex";var e=new ConflictEngine();var now=DateTimeOffset.UnixEpoch;var candidates=new[]{C("a","Suite Texture",p,"aa",1),C("b","Suite Texture",p,"bb",5)};var mods=new Dictionary<string,ModDescriptor>(StringComparer.OrdinalIgnoreCase){{"a",new("a","Suite Texture","Suite Texture","",true,1,FamilyId:"manual:suite")},{"b",new("b","Suite Texture","Suite Texture","",true,5,FamilyId:"manual:suite")}};var stats=new Dictionary<string,ModContentStats>(StringComparer.OrdinalIgnoreCase){{"a",new(1,1,0,0,now)},{"b",new(1,1,0,0,now)}};var d=e.Decide(p,candidates,ConflictRuleIndex.Create([]),new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<(string,string),PairStats>(),mods,stats);Assert.Equal(ConflictKind.ModFamilyOption,d.Kind);Assert.True(d.Blocking);Assert.Null(d.WinnerModId);Assert.Equal("family-texture-choice",d.ReasonCode);}
    [Fact] public void Incompatible_rule_beats_old_exact_winner(){var p=@"nativePC\x.mod3";var e=new ConflictEngine();var r=new ConflictRule("r",RuleKind.Incompatible,RuleScope.ModPair,"a","b",null,null,"test",true,DateTimeOffset.UtcNow);var d=e.Decide(p,[C("a","A",p,"aa"),C("b","B",p,"bb",2)],[r],new Dictionary<string,string>{{p,"a"}},new Dictionary<string,string>(),new Dictionary<(string,string),PairStats>());Assert.Equal(ConflictKind.Incompatible,d.Kind);Assert.True(d.Blocking);}
    [Fact] public void Protected_bootstrap_collision_requires_explicit_choice(){var p=@"root\dinput8.dll";var e=new ConflictEngine();var d=e.Decide(p,[C("loader-a","Loader A",p,"aa"),C("loader-b","Loader B",p,"bb",2)],[],new Dictionary<string,string>(),new Dictionary<string,string>(),new Dictionary<(string,string),PairStats>());Assert.True(d.Blocking);Assert.Null(d.WinnerModId);Assert.Equal("protected-bootstrap-collision",d.ReasonCode);}
    [Fact] public void Explicit_exact_winner_can_resolve_protected_bootstrap_collision(){var p=@"root\dinput8.dll";var e=new ConflictEngine();var d=e.Decide(p,[C("loader-a","Loader A",p,"aa"),C("loader-b","Loader B",p,"bb",2)],[],new Dictionary<string,string>{{p,"loader-b"}},new Dictionary<string,string>(),new Dictionary<(string,string),PairStats>());Assert.False(d.Blocking);Assert.Equal("loader-b",d.WinnerModId);Assert.Equal("exact-file-winner",d.ReasonCode);}
}
