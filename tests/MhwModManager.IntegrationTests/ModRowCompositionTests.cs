using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ModRowCompositionTests
{
    [Fact]
    public void Selecting_hpn_v42_stages_main_and_latest_legacy_3x_without_enabling_other_variants()
    {
        var members = new[]
        {
            new ModDescriptor(
                "main",
                "Main",
                "Main",
                "main",
                false,
                5,
                FamilyId: "nexus:1965",
                NexusModId: "1965"),
            new ModDescriptor(
                "v31",
                "Ver3.1 Highpoly Nude MOD with jiggle animation",
                "Ver3.1 Highpoly Nude MOD with jiggle animation",
                "v31",
                false,
                10,
                FamilyId: "nexus:1965",
                NexusModId: "1965"),
            new ModDescriptor(
                "v310",
                "Ver3.10 Beautiful_Tits_Highpoly Nude MOD with jiggle animation (Iceborne Compatible)",
                "Ver3.10 Beautiful_Tits_Highpoly Nude MOD with jiggle animation (Iceborne Compatible)",
                "v310",
                false,
                20,
                FamilyId: "nexus:1965",
                NexusModId: "1965"),
            new ModDescriptor(
                "v41",
                "Ver4.1 Normal_Highpoly Nude MOD with jiggle animation",
                "Ver4.1 Normal_Highpoly Nude MOD with jiggle animation",
                "v41",
                false,
                30,
                FamilyId: "nexus:1965",
                NexusModId: "1965"),
            new ModDescriptor(
                "v42-normal",
                "Ver4.2 Normal_Highpoly Nude MOD with jiggle animation",
                "Ver4.2 Normal_Highpoly Nude MOD with jiggle animation",
                "v42-normal",
                false,
                40,
                FamilyId: "nexus:1965",
                NexusModId: "1965"),
            new ModDescriptor(
                "v42-beautiful",
                "Ver4.2 Beautiful_Highpoly Nude MOD with jiggle animation",
                "Ver4.2 Beautiful_Highpoly Nude MOD with jiggle animation",
                "v42-beautiful",
                false,
                50,
                FamilyId: "nexus:1965",
                NexusModId: "1965")
        };
        var family = new LogicalModFamily(
            "logical:nexus:1965",
            "HPN",
            "HPN",
            members,
            "Armor");
        var row = new ModRowViewModel(family, static () => { });

        row.SetMemberEnabled("v42-normal", true);

        var staged = row.ExpandStage();
        Assert.True(staged["main"].enabled);
        Assert.False(staged["v31"].enabled);
        Assert.True(staged["v310"].enabled);
        Assert.False(staged["v41"].enabled);
        Assert.True(staged["v42-normal"].enabled);
        Assert.False(staged["v42-beautiful"].enabled);

        row.StagedEnabled = false;

        Assert.All(row.ExpandStage().Values, state => Assert.False(state.enabled));
    }

    [Fact]
    public void Similar_versions_on_an_unapproved_nexus_page_do_not_auto_stage_legacy_generation()
    {
        var members = new[]
        {
            new ModDescriptor(
                "v310",
                "Ver3.10 Highpoly Nude MOD with jiggle animation",
                "Ver3.10 Highpoly Nude MOD with jiggle animation",
                "v310",
                false,
                10,
                FamilyId: "nexus:9000",
                NexusModId: "9000"),
            new ModDescriptor(
                "v42",
                "Ver4.2 Highpoly Nude MOD with jiggle animation",
                "Ver4.2 Highpoly Nude MOD with jiggle animation",
                "v42",
                false,
                20,
                FamilyId: "nexus:9000",
                NexusModId: "9000")
        };
        var family = new LogicalModFamily(
            "logical:nexus:9000",
            "Other body",
            "Other body",
            members,
            "Armor");
        var row = new ModRowViewModel(family, static () => { });

        row.SetMemberEnabled("v42", true);

        var staged = row.ExpandStage();
        Assert.False(staged["v310"].enabled);
        Assert.True(staged["v42"].enabled);
    }
    [Fact]
    public void Selecting_no_bats_keeps_demon_lord_main_staged()
    {
        var members = new[]
        {
            new ModDescriptor(
                "main",
                "Main",
                "Main",
                "main",
                false,
                10,
                FamilyId: "nexus:4678",
                NexusModId: "4678"),
            new ModDescriptor(
                "no-bats",
                "No Bats",
                "No Bats",
                "no-bats",
                false,
                20,
                FamilyId: "nexus:4678",
                NexusModId: "4678")
        };
        var family = new LogicalModFamily(
            "logical:nexus:4678",
            "HPN Demon Lord",
            "HPN Demon Lord",
            members,
            "Armor");
        var row = new ModRowViewModel(family, static () => { });

        row.SetMemberEnabled("no-bats", true);

        Assert.True(row.ExpandStage()["main"].enabled);
        Assert.True(row.ExpandStage()["no-bats"].enabled);

        row.SetMemberEnabled("main", false);

        Assert.True(row.ExpandStage()["main"].enabled);
        Assert.True(row.ExpandStage()["no-bats"].enabled);
    }

}
