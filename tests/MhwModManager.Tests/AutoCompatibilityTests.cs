using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class AutoCompatibilityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddYears(50);

    [Fact]
    public void Base_and_option_structural_files_auto_compose()
    {
        var mods = new[]
        {
            new ModDescriptor("base","HPN Sexy Kulu-Ya-Ku","HPN Sexy Kulu-Ya-Ku","base",true,0),
            new ModDescriptor("waist","HPN Sexy Kulu-Ya-Ku - Skimpy Waist","HPN Sexy Kulu-Ya-Ku - Skimpy Waist","waist",true,1)
        };
        var path = @"nativePC\pl\f_equip\pl032_0010\wst\mod\f_wst032_0010.mod3";
        var files = new[]
        {
            new ModFileDescriptor("base",path,"aa",null,100,Now,FileClass.Structural),
            new ModFileDescriptor("waist",path,"bb",null,100,Now,FileClass.Structural)
        };
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files));

        Assert.False(plan.IsBlocked);
        var decision = Assert.Single(plan.Conflicts);
        Assert.Equal(ConflictKind.ModFamilyOption, decision.Kind);
        Assert.Equal("waist", decision.WinnerModId);
        Assert.True(decision.Inferred);
    }

    [Fact]
    public void Main_marker_and_body_size_optional_auto_compose()
    {
        var mods = new[]
        {
            new ModDescriptor("base","Uber Bunny Suit Set Base Commission","Uber Bunny Suit Set Base Commission","base",true,100),
            new ModDescriptor("size5","Uber Bunny Suit Set Body Size 5","Uber Bunny Suit Set Body Size 5","size5",true,1)
        };
        var shared = @"nativePC\pl\f_equip\pl053_0000\body\mod\f_body053_0000.mod3";
        var files = new List<ModFileDescriptor>
        {
            new("base",shared,"aa",null,100,Now,FileClass.Structural),
            new("size5",shared,"bb",null,100,Now,FileClass.Structural)
        };
        for (var i = 0; i < 5; i++)
            files.Add(new("base",$@"nativePC\pl\f_equip\pl053_0000\extra{i}.tex",$"base{i}",null,100,Now,FileClass.Texture));

        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files));

        Assert.False(plan.IsBlocked);
        Assert.Equal("size5", plan.Conflicts.Single(x => PathRules.Comparer.Equals(x.Path,shared)).WinnerModId);
    }

    [Fact]
    public void Three_layer_base_option_hotfix_chain_auto_composes()
    {
        var mods = new[]
        {
            new ModDescriptor("base","Armor","Armor","base",true,0),
            new ModDescriptor("option","Armor - Open Top","Armor - Open Top","option",true,1),
            new ModDescriptor("fix","Armor - Open Top - Fix","Armor - Open Top - Fix","fix",true,2)
        };
        var path = @"nativePC\pl\f_equip\pl001_0000\body\mod\f_body001_0000.mod3";
        var files = mods.Select(m => new ModFileDescriptor(m.Id,path,m.Id.PadRight(64,'0'),null,100,Now,FileClass.Structural)).ToArray();
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files));

        Assert.False(plan.IsBlocked);
        Assert.Equal("fix", Assert.Single(plan.Conflicts).WinnerModId);
    }

    [Fact]
    public void Unrelated_structural_alternatives_still_block()
    {
        var mods = new[]
        {
            new ModDescriptor("neo","Orion Neo","Orion Neo","neo",true,0),
            new ModDescriptor("nova","Orion Nova","Orion Nova","nova",true,1)
        };
        var path = @"nativePC\pl\f_equip\pl001_0000\body\mod\f_body001_0000.mod3";
        var files = new[]
        {
            new ModFileDescriptor("neo",path,"aa",null,100,Now,FileClass.Structural),
            new ModFileDescriptor("nova",path,"bb",null,100,Now,FileClass.Structural)
        };
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files));

        Assert.True(plan.IsBlocked);
        Assert.Equal(ConflictKind.HardStructural, Assert.Single(plan.Conflicts).Kind);
    }

    [Fact]
    public void Same_label_patch_archives_do_not_guess_structural_order()
    {
        var mods = new[]
        {
            new ModDescriptor("p1","Fatalis Patch","Fatalis Patch","p1",true,100),
            new ModDescriptor("p2","Fatalis Patch","Fatalis Patch","p2",true,1)
        };
        var path = @"nativePC\pl\f_equip\pl123_0000\body\mod\f_body123_0000.mod3";
        var files = new[]
        {
            new ModFileDescriptor("p1",path,"aa",null,100,Now,FileClass.Structural),
            new ModFileDescriptor("p2",path,"bb",null,100,Now.AddMinutes(5),FileClass.Structural)
        };
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files));

        Assert.True(plan.IsBlocked);
        Assert.Equal(ConflictKind.HardStructural, Assert.Single(plan.Conflicts).Kind);
    }


    [Fact]
    public void Alternative_label_alone_does_not_auto_order_structural_files()
    {
        var mods = new[]
        {
            new ModDescriptor("base","Armor Set","Armor Set","base",true,1),
            new ModDescriptor("alt","Armor Set - Alternative","Armor Set - Alternative","alt",true,999)
        };
        var path = @"nativePC\pl\f_equip\pl111_0000\body\mod\f_body111_0000.mod3";
        var files = new[]
        {
            new ModFileDescriptor("base",path,"aa",null,100,Now,FileClass.Structural),
            new ModFileDescriptor("alt",path,"bb",null,100,Now,FileClass.Structural)
        };
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files));

        Assert.True(plan.IsBlocked);
        Assert.Equal(ConflictKind.PossibleOverlay, Assert.Single(plan.Conflicts).Kind);
    }

    [Fact]
    public void Explicit_incompatible_rule_beats_auto_family_inference()
    {
        var mods = new[]
        {
            new ModDescriptor("base","Armor","Armor","base",true,0),
            new ModDescriptor("option","Armor - No Cape","Armor - No Cape","option",true,1)
        };
        var path = @"nativePC\pl\f_equip\pl001_0000\body\mod\f_body001_0000.mod3";
        var files = new[]
        {
            new ModFileDescriptor("base",path,"aa",null,100,Now,FileClass.Structural),
            new ModFileDescriptor("option",path,"bb",null,100,Now,FileClass.Structural)
        };
        var incompatible = new ConflictRule("human",RuleKind.Incompatible,RuleScope.ModPair,"base","option",null,null,"user says no",true,Now);
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files, [incompatible]));

        Assert.True(plan.IsBlocked);
        Assert.Equal(ConflictKind.Incompatible, Assert.Single(plan.Conflicts).Kind);
    }

    [Fact]
    public void Dedicated_texture_pack_beats_incidental_hpn_armor_copy_even_with_lower_priority()
    {
        var mods = new[]
        {
            new ModDescriptor("armor","HPN Sexy Guildworks","HPN Sexy Guildworks","armor",true,900),
            new ModDescriptor("skin","Cleaner HPN Female Textures","Cleaner HPN Female Textures","skin",true,1)
        };
        var path = @"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex";
        var files = new List<ModFileDescriptor>
        {
            new("armor",path,"aa",null,100,Now,FileClass.Texture),
            new("skin",path,"bb",null,100,Now.AddDays(1),FileClass.Texture),
            new("armor",@"nativePC\pl\f_equip\pl050_0000\body\mod\f_body050_0000.mod3","cc",null,100,Now,FileClass.Structural),
            new("skin",@"nativePC\pl\f_equip\mod_hepsy\f_skin_BM.tex","dd",null,100,Now.AddDays(1),FileClass.Texture)
        };
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files));

        Assert.False(plan.IsBlocked);
        var decision = plan.Conflicts.Single(x => PathRules.Comparer.Equals(x.Path,path));
        Assert.Equal(ConflictKind.SharedTexture, decision.Kind);
        Assert.Equal("skin", decision.WinnerModId);
        Assert.Equal("dedicated-texture-provider", decision.ReasonCode);
    }

    [Fact]
    public void Newer_hpn_texture_revision_beats_old_revision_even_with_lower_priority()
    {
        var mods = new[]
        {
            new ModDescriptor("old","HPN Skin Textures v1.0","HPN Skin Textures v1.0","old",true,999),
            new ModDescriptor("new","HPN Skin Textures v2.0","HPN Skin Textures v2.0","new",true,1)
        };
        var path = @"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex";
        var files = new[]
        {
            new ModFileDescriptor("old",path,"aa",null,100,Now,FileClass.Texture),
            new ModFileDescriptor("new",path,"bb",null,100,Now.AddMinutes(1),FileClass.Texture)
        };
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files));

        Assert.False(plan.IsBlocked);
        var decision = Assert.Single(plan.Conflicts);
        Assert.Equal("new", decision.WinnerModId);
        Assert.Equal("newer-texture-revision", decision.ReasonCode);
    }

    [Fact]
    public void Plain_v2_texture_revision_beats_v1()
    {
        var mods = new[]
        {
            new ModDescriptor("old","HPN Body Texture v1","HPN Body Texture v1","old",true,500),
            new ModDescriptor("new","HPN Body Texture v2","HPN Body Texture v2","new",true,1)
        };
        var path = @"nativePC\pl\f_equip\mod_hepsy\f_skin_BM.tex";
        var files = new[]
        {
            new ModFileDescriptor("old",path,"aa",null,100,Now,FileClass.Texture),
            new ModFileDescriptor("new",path,"bb",null,100,Now,FileClass.Texture)
        };
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files));

        var decision = Assert.Single(plan.Conflicts);
        Assert.False(plan.IsBlocked);
        Assert.Equal("new", decision.WinnerModId);
        Assert.Equal("newer-texture-revision", decision.ReasonCode);
    }

    [Fact]
    public void Newer_nexus_style_texture_upload_timestamp_beats_older_archive()
    {
        var mods = new[]
        {
            new ModDescriptor("old","HPN Skin Textures-5179-1-0-1700000000","HPN Skin Textures","old",true,500),
            new ModDescriptor("new","HPN Skin Textures-5179-1-0-1710000000","HPN Skin Textures","new",true,1)
        };
        var path = @"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex";
        var files = new[]
        {
            new ModFileDescriptor("old",path,"aa",null,100,Now,FileClass.Texture),
            new ModFileDescriptor("new",path,"bb",null,100,Now,FileClass.Texture)
        };
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files));

        var decision = Assert.Single(plan.Conflicts);
        Assert.False(plan.IsBlocked);
        Assert.Equal("new", decision.WinnerModId);
        Assert.Equal("newer-texture-revision", decision.ReasonCode);
    }

    [Fact]
    public void Explicit_updated_hpn_texture_label_beats_old_texture_copy()
    {
        var mods = new[]
        {
            new ModDescriptor("old","HPN Skin Textures","HPN Skin Textures","old",true,999),
            new ModDescriptor("new","HPN Skin Textures Updated","HPN Skin Textures Updated","new",true,1)
        };
        var path = @"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex";
        var files = new[]
        {
            new ModFileDescriptor("old",path,"aa",null,100,Now.AddDays(2),FileClass.Texture),
            new ModFileDescriptor("new",path,"bb",null,100,Now,FileClass.Texture)
        };
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files));

        Assert.False(plan.IsBlocked);
        var decision = Assert.Single(plan.Conflicts);
        Assert.Equal("new", decision.WinnerModId);
        Assert.True(decision.ReasonCode is "updated-texture-provider" or "auto-composed-overlay");
    }

    [Fact]
    public void Proven_same_family_base_and_optional_never_become_hard_conflict()
    {
        var mods = new[]
        {
            new ModDescriptor("base","Future Outfit","Future Outfit","base",true,900,FamilyId:"family:future-outfit"),
            new ModDescriptor("option","Future Outfit - Open Top","Future Outfit - Open Top","option",true,1,FamilyId:"family:future-outfit")
        };
        var path = @"nativePC\pl\f_equip\pl211_0000\body\mod\f_body211_0000.mod3";
        var files = new[]
        {
            new ModFileDescriptor("base",path,"aa",null,100,Now,FileClass.Structural),
            new ModFileDescriptor("option",path,"bb",null,100,Now,FileClass.Structural)
        };

        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods,files));

        Assert.False(plan.IsBlocked);
        var decision = Assert.Single(plan.Conflicts);
        Assert.Equal(ConflictKind.ModFamilyOption,decision.Kind);
        Assert.Equal("option",decision.WinnerModId);
        Assert.DoesNotContain(plan.Conflicts,x=>x.Kind is ConflictKind.HardStructural or ConflictKind.HardGameData or ConflictKind.HardUnknown);
    }

    [Fact]
    public void Proven_same_family_unknown_subset_blocks_without_overlay_evidence()
    {
        var mods = new[]
        {
            new ModDescriptor("base","Future Armor Suite","Future Armor Suite","base",true,500,FamilyId:"manager:future-suite"),
            new ModDescriptor("piece","Future Armor Detail","Future Armor Detail","piece",true,1,FamilyId:"manager:future-suite")
        };
        var shared = @"nativePC\pl\f_equip\pl212_0000\wst\mod\f_wst212_0000.mod3";
        var files = new List<ModFileDescriptor>
        {
            new("base",shared,"aa",null,100,Now,FileClass.Structural),
            new("piece",shared,"bb",null,100,Now,FileClass.Structural)
        };
        for(var i=0;i<4;i++)
            files.Add(new("base",$@"nativePC\pl\f_equip\pl212_0000\body\tex\extra{i}.tex",$"base{i}",null,100,Now,FileClass.Texture));

        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods,files));

        Assert.True(plan.IsBlocked);
        var decision = plan.Conflicts.Single(x=>PathRules.Comparer.Equals(x.Path,shared));
        Assert.Equal(ConflictKind.ModFamilyOption,decision.Kind);
        Assert.Null(decision.WinnerModId);
        Assert.Equal("family-internal-choice",decision.ReasonCode);
    }

    [Fact]
    public void Local_name_only_plugin_update_does_not_override_binary_code()
    {
        var mods = new[]
        {
            new ModDescriptor("base","Native Helper","Native Helper","base",true,10),
            new ModDescriptor("update","Native Helper - Update","Native Helper - Update","update",true,20)
        };
        var path = @"root\native-helper.dll";
        var files = new[]
        {
            new ModFileDescriptor("base",path,"aa",null,100,Now,FileClass.Plugin),
            new ModFileDescriptor("update",path,"bb",null,100,Now,FileClass.Plugin)
        };

        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods,files));

        Assert.True(plan.IsBlocked);
        var decision = Assert.Single(plan.Conflicts);
        Assert.Equal("untrusted-code-overlay",decision.ReasonCode);
        Assert.Null(decision.WinnerModId);
    }

    [Fact]
    public void Verified_same_nexus_update_can_override_binary_code()
    {
        var mods = new[]
        {
            new ModDescriptor("base","Native Helper","Native Helper","base",true,10,
                NexusModId:"4242",NexusCategory:NexusFileCategory.Main),
            new ModDescriptor("update","Native Helper Update","Native Helper Update","update",true,20,
                NexusModId:"4242",NexusCategory:NexusFileCategory.Update)
        };
        var path = @"root\native-helper.dll";
        var files = new[]
        {
            new ModFileDescriptor("base",path,"aa",null,100,Now,FileClass.Plugin),
            new ModFileDescriptor("update",path,"bb",null,100,Now,FileClass.Plugin)
        };

        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods,files));

        Assert.False(plan.IsBlocked);
        var decision = Assert.Single(plan.Conflicts);
        Assert.Equal("update",decision.WinnerModId);
        Assert.True(decision.ResolverScore>=98);
    }

    [Fact]
    public void Proven_same_family_sibling_variants_become_internal_choice_not_hard_conflict()
    {
        var mods = new[]
        {
            new ModDescriptor("style-a","Future Style Crimson","Future Style Crimson","a",true,10,FamilyId:"nexus:9999"),
            new ModDescriptor("style-b","Future Style Azure","Future Style Azure","b",true,20,FamilyId:"nexus:9999")
        };
        var path = @"nativePC\pl\f_equip\pl213_0000\body\mod\f_body213_0000.mod3";
        var files = new[]
        {
            new ModFileDescriptor("style-a",path,"aa",null,100,Now,FileClass.Structural),
            new ModFileDescriptor("style-b",path,"bb",null,100,Now,FileClass.Structural)
        };

        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods,files));

        Assert.True(plan.IsBlocked);
        var decision = Assert.Single(plan.Conflicts);
        Assert.Equal(ConflictKind.ModFamilyOption,decision.Kind);
        Assert.Null(decision.WinnerModId);
        Assert.Equal("family-internal-choice",decision.ReasonCode);
        Assert.DoesNotContain(plan.Conflicts,x=>x.Kind is ConflictKind.HardStructural or ConflictKind.HardGameData or ConflictKind.HardUnknown);
    }

    [Fact]
    public void Explicit_incompatible_rule_still_blocks_members_of_same_family()
    {
        var mods = new[]
        {
            new ModDescriptor("left","Suite Left","Suite Left","left",true,1,FamilyId:"manual:suite"),
            new ModDescriptor("right","Suite Right","Suite Right","right",true,2,FamilyId:"manual:suite")
        };
        var path = @"nativePC\pl\f_equip\pl214_0000\body\mod\f_body214_0000.mod3";
        var files = new[]
        {
            new ModFileDescriptor("left",path,"aa",null,100,Now,FileClass.Structural),
            new ModFileDescriptor("right",path,"bb",null,100,Now,FileClass.Structural)
        };
        var rule = new ConflictRule("explicit-no",RuleKind.Incompatible,RuleScope.ModPair,"left","right",null,null,"Explicit user incompatibility",true,Now);

        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods,files,[rule]));

        Assert.True(plan.IsBlocked);
        Assert.Equal(ConflictKind.Incompatible,Assert.Single(plan.Conflicts).Kind);
    }

    [Fact]
    public void Explicit_resource_provider_pin_beats_automatic_texture_revision()
    {
        var mods = new[]
        {
            new ModDescriptor("old","HPN Skin Textures v1.0","HPN Skin Textures v1.0","old",true,1),
            new ModDescriptor("new","HPN Skin Textures v2.0","HPN Skin Textures v2.0","new",true,100)
        };
        var path = @"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex";
        var files = new[]
        {
            new ModFileDescriptor("old",path,"aa",null,100,Now,FileClass.Texture),
            new ModFileDescriptor("new",path,"bb",null,100,Now.AddDays(1),FileClass.Texture)
        };
        var resourceNamespace = PathRules.ResourceNamespace(path)!;
        var resources = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) { [resourceNamespace] = "old" };
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files, resourceProviders: resources));

        Assert.False(plan.IsBlocked);
        var decision = Assert.Single(plan.Conflicts);
        Assert.Equal("old", decision.WinnerModId);
        Assert.Equal(ConflictKind.SharedProvider, decision.Kind);
        Assert.Equal(Confidence.Explicit, decision.Confidence);
    }

    [Fact]
    public void Shared_texture_keeps_all_mods_enabled_and_chooses_one_exact_path_provider()
    {
        var mods = new[]
        {
            new ModDescriptor("base","Armor","Armor","base",true,10),
            new ModDescriptor("option","Armor - Open Top","Armor - Open Top","option",true,1),
            new ModDescriptor("skin","Cleaner Female Textures","Cleaner Female Textures","skin",true,20)
        };
        var path = @"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex";
        var files = new[]
        {
            new ModFileDescriptor("base",path,"aa",null,100,Now,FileClass.Texture),
            new ModFileDescriptor("option",path,"bb",null,100,Now,FileClass.Texture),
            new ModFileDescriptor("skin",path,"cc",null,100,Now,FileClass.Texture)
        };
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(Snapshot(mods, files));

        Assert.False(plan.IsBlocked);
        var decision = Assert.Single(plan.Conflicts);
        Assert.Equal(ConflictKind.SharedTexture, decision.Kind);
        Assert.Equal("skin", decision.WinnerModId);
        Assert.Single(plan.Changes);
        Assert.Equal("skin", plan.Changes[0].ProviderAfter);
    }

    private static PlannerSnapshot Snapshot(
        IReadOnlyList<ModDescriptor> mods,
        IReadOnlyList<ModFileDescriptor> files,
        IReadOnlyList<ConflictRule>? rules = null,
        IReadOnlyDictionary<string,string>? resourceProviders = null) =>
        new(mods, files, rules ?? [], new Dictionary<string,string>(), resourceProviders ?? new Dictionary<string,string>(),
            new Dictionary<string,DeploymentManifestEntry>(), new Dictionary<string,string?>());
}

public sealed class SharedEmbeddedTextureResourceTests
{
    [Fact]
    public void Broader_outfit_mods_sharing_resource_texture_do_not_conflict()
    {
        var path = @"nativePC\pl\f_equip\mod_sharedbody\f_skin_NM.tex";
        var candidates = new[]
        {
            new ProviderCandidate("a","Skimpy Alpha",10,new("a",path,"aa",null,100,DateTimeOffset.UtcNow,FileClass.Texture)),
            new ProviderCandidate("b","Skimpy Beta",20,new("b",path,"bb",null,100,DateTimeOffset.UtcNow,FileClass.Texture))
        };
        var mods = new Dictionary<string,ModDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new("a","Skimpy Alpha","Skimpy Alpha","",true,10),
            ["b"] = new("b","Skimpy Beta","Skimpy Beta","",true,20)
        };
        var stats = new Dictionary<string,ModContentStats>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new(20,4,16,0,DateTimeOffset.UtcNow),
            ["b"] = new(18,3,15,0,DateTimeOffset.UtcNow)
        };
        var result = AutoCompatibility.SelectTextureProvider(path,candidates,mods,stats);
        Assert.Equal("b",result.WinnerModId);
        Assert.Equal("shared-embedded-resource",result.ReasonCode);
    }
}
