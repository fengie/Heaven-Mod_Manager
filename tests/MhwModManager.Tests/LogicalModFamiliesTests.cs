using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class LogicalModFamiliesTests
{
    [Fact]
    public void Main_and_component_packages_collapse_to_one_logical_mod()
    {
        var mods = new[]
        {
            Mod("base","HPN Sexy Kulu-Ya-Ku"),
            Mod("top","HPN Sexy Kulu-Ya-Ku - Top"),
            Mod("waist","HPN Sexy Kulu-Ya-Ku - Skimpy Waist"),
            Mod("cape","HPN Sexy Kulu-Ya-Ku - No Cape")
        };

        var family = Assert.Single(LogicalModFamilies.Build(mods));
        Assert.Equal(4, family.Members.Count);
        Assert.Equal("HPN Sexy Kulu-Ya-Ku", family.DisplayName);
        Assert.Equal("base", family.Members[0].Id);
    }

    [Fact]
    public void Same_nexus_mod_id_collapses_even_when_every_file_is_marked_main()
    {
        var mods = new[]
        {
            Mod("base","HPN Sexy Safi'jiiva", "777", NexusFileCategory.Main),
            Mod("top","HPN Sexy Safi'jiiva - Top", "777", NexusFileCategory.Main),
            Mod("waist","HPN Sexy Safi'jiiva - Waist", "777", NexusFileCategory.Main),
            Mod("nips","HPN Sexy Safi'jiiva - Free the Nipples", "777", NexusFileCategory.Main)
        };

        var family = Assert.Single(LogicalModFamilies.Build(mods));
        Assert.Equal(4, family.Members.Count);
        Assert.Equal("HPN Sexy Safi'jiiva", family.DisplayName);
    }

    [Fact]
    public void Different_nexus_mod_ids_do_not_collapse_just_because_both_are_hpn()
    {
        var mods = new[]
        {
            Mod("a","HPN Sexy Guildworks", "1001", NexusFileCategory.Main),
            Mod("b","HPN Sexy Guildworks", "1002", NexusFileCategory.Main)
        };

        Assert.Equal(2, LogicalModFamilies.Build(mods).Count);
    }

    [Fact]
    public void Hpn_free_the_nipples_suffix_is_a_component()
    {
        var mods = new[]
        {
            Mod("base","HPN Sexy Kulu-Ya-Ku"),
            Mod("nips","HPN Sexy Kulu-Ya-Ku - Free the Nipples")
        };

        var family = Assert.Single(LogicalModFamilies.Build(mods));
        Assert.Equal(2, family.Members.Count);
        Assert.Equal("HPN Sexy Kulu-Ya-Ku", family.DisplayName);
    }


    [Fact]
    public void Generic_same_source_page_groups_main_files_without_brand_knowledge()
    {
        var mods = new[]
        {
            Mod("base","Knight Commission Armor - Base", "8123", NexusFileCategory.Main),
            Mod("cape","Knight Commission Armor - No Cape", "8123", NexusFileCategory.Main),
            Mod("legs","Knight Commission Armor - Legs", "8123", NexusFileCategory.Main)
        };

        var family = Assert.Single(LogicalModFamilies.Build(mods));
        Assert.Equal(3, family.Members.Count);
    }

    [Fact]
    public void Local_component_packages_can_group_from_name_and_file_topology()
    {
        var mods = new[]
        {
            Mod("base","Commission Armor Rework"),
            Mod("waist","Commission Armor Rework - Waist")
        };
        var files = new[]
        {
            File("base", @"nativePC\pl\f_equip\pl001_0000\wst\f_wst001_0000.mod3"),
            File("base", @"nativePC\pl\f_equip\pl001_0000\body\f_body001_0000.mod3"),
            File("waist", @"nativePC\pl\f_equip\pl001_0000\wst\f_wst001_0000.mod3")
        };

        var family = Assert.Single(LogicalModFamilies.Build(mods,files));
        Assert.Equal(2, family.Members.Count);
    }

    [Fact]
    public void Similar_names_without_shared_layout_do_not_group()
    {
        var mods = new[]
        {
            Mod("a","Commission Armor Rework Red"),
            Mod("b","Commission Armor Rework Blue")
        };
        var files = new[]
        {
            File("a", @"nativePC\pl\f_equip\pl001_0000\body\a.mod3"),
            File("b", @"nativePC\pl\f_equip\pl099_0000\body\b.mod3")
        };

        Assert.Equal(2, LogicalModFamilies.Build(mods,files).Count);
    }


    [Fact]
    public void Separate_inferred_clusters_with_same_name_stem_do_not_collapse_together()
    {
        var mods = new[]
        {
            Mod("a-base","Commission Armor Rework"),
            Mod("a-waist","Commission Armor Rework - Waist"),
            Mod("b-base","Commission Armor Rework"),
            Mod("b-waist","Commission Armor Rework - Waist")
        };
        var files = new[]
        {
            File("a-base", @"nativePC\pl\f_equip\pl001_0000\wst\a.mod3"),
            File("a-waist", @"nativePC\pl\f_equip\pl001_0000\wst\a.mod3"),
            File("b-base", @"nativePC\pl\f_equip\pl099_0000\wst\b.mod3"),
            File("b-waist", @"nativePC\pl\f_equip\pl099_0000\wst\b.mod3")
        };

        var families=LogicalModFamilies.Build(mods,files);
        Assert.Equal(2,families.Count);
        Assert.All(families,f=>Assert.Equal(2,f.Members.Count));
    }

    [Fact]
    public void Shared_asset_identity_alone_does_not_merge_unrelated_replacements()
    {
        var mods = new[] { Mod("a","Dragon Knight"), Mod("b","Summer Dress") };
        var files = new[]
        {
            File("a", @"nativePC\pl\f_equip\pl001_0000\body\a.mod3"),
            File("b", @"nativePC\pl\f_equip\pl001_0000\body\b.mod3")
        };

        Assert.Equal(2, LogicalModFamilies.Build(mods,files).Count);
    }

    [Fact]
    public void Transitive_evidence_does_not_bypass_complete_link_hard_block()
    {
        var mods = new[]
        {
            Mod("alpha","Alpha Armor"),
            Mod("bridge","Alpha Armor - Waist"),
            Mod("beta","Beta Dress")
        };
        var files = new[]
        {
            File("alpha", @"nativePC\pl\f_equip\pl001_0000\wst\shared-a.mod3"),
            File("bridge", @"nativePC\pl\f_equip\pl001_0000\wst\shared-a.mod3"),
            File("bridge", @"nativePC\pl\f_equip\pl099_0000\body\shared-b.mod3"),
            File("beta", @"nativePC\pl\f_equip\pl099_0000\body\shared-b.mod3")
        };

        var families = LogicalModFamilies.Build(mods, files);

        Assert.Equal(2, families.Count);
        Assert.Contains(families, family => family.Members.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(new[] { "alpha", "bridge" }));
        Assert.Contains(families, family => family.Members.Count == 1 && family.Members[0].Id == "beta");
    }

    [Fact]
    public void Pick_one_alternative_stays_a_separate_logical_mod()
    {
        var mods = new[]
        {
            Mod("base","Orion Neo"),
            Mod("alt","Orion Neo - Alternative")
        };

        var families = LogicalModFamilies.Build(mods);
        Assert.Equal(2, families.Count);
    }

    [Fact]
    public void Texture_revisions_collapse_to_one_logical_mod()
    {
        var mods = new[]
        {
            Mod("v1","HPN Skin Textures v1.0"),
            Mod("v2","HPN Skin Textures v2.0"),
            Mod("updated","HPN Skin Textures Updated")
        };

        var family = Assert.Single(LogicalModFamilies.Build(mods));
        Assert.Equal(3, family.Members.Count);
        Assert.Equal("HPN Skin Textures", family.DisplayName);
    }

    [Fact]
    public void Different_hpn_armors_do_not_collapse_together()
    {
        var mods = new[]
        {
            Mod("guild","HPN Sexy Guildworks"),
            Mod("kulu","HPN Sexy Kulu-Ya-Ku"),
            Mod("npc","HPN Sexy NPCs")
        };

        Assert.Equal(3, LogicalModFamilies.Build(mods).Count);
    }


    [Fact]
    public void Arbitrary_versioned_structural_mods_remain_separate()
    {
        var mods = new[]
        {
            Mod("v1","Orion Neo v1.0"),
            Mod("v2","Orion Neo v2.0")
        };

        Assert.Equal(2, LogicalModFamilies.Build(mods).Count);
    }


    [Fact]
    public void Same_label_ambiguous_packages_remain_separate()
    {
        var mods = new[]
        {
            Mod("fatalis-a","Fatalis Patch"),
            Mod("fatalis-b","Fatalis Patch"),
            Mod("fatalis-c","Fatalis Patch")
        };

        Assert.Equal(3, LogicalModFamilies.Build(mods).Count);
    }

    private static ModFileDescriptor File(string modId,string path) =>
        new(modId,path,"sha-"+modId,null,1,DateTimeOffset.UnixEpoch,PathRules.ClassifyFile(path));

    private static ModDescriptor Mod(string id,string name,string? nexusModId=null,NexusFileCategory nexusCategory=NexusFileCategory.Unknown) =>
        new(id,name,name,id,false,0,NexusModId:nexusModId,NexusCategory:nexusCategory);
}
