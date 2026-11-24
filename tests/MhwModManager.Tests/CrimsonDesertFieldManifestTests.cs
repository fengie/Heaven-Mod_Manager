using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class CrimsonDesertFieldManifestTests
{
    [Fact]
    public void Swiss_and_dmm_field_shapes_have_the_same_target_key_for_the_same_table_edit()
    {
        const string swiss = """{"format":3,"format_minor":1,"targets":[{"kind":"table","table":"ItemInfo","ops":[{"entry":"Sample","key":"13810","field":"discardable","value":0}]}]}""";
        const string dmm = """{"format":3,"format_minor":1,"modinfo":{"title":"Example"},"targets":[{"file":"iteminfo.pabgb","intents":[{"entry":"Sample","key":13810,"field":"discardable","op":"set","new":0}]}]}""";

        var first = CrimsonDesertFieldManifestInspector.Inspect(swiss);
        var second = CrimsonDesertFieldManifestInspector.Inspect(dmm);

        Assert.True(first.IsValid, first.Reason);
        Assert.True(second.IsValid, second.Reason);
        Assert.Equal(Assert.Single(first.Claims).TargetKey, Assert.Single(second.Claims).TargetKey);
        Assert.Empty(CrimsonDesertFieldManifestInspector.FindClaimCollisions([first, second]));
    }

    [Fact]
    public void Different_table_fields_compose_only_as_advisory_nonoverlapping_metadata()
    {
        var first = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"targets":[{"kind":"table","table":"ItemInfo","ops":[{"key":"id_10","field":"buy_price","value":1}]}]}""");
        var second = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"targets":[{"kind":"table","table":"ItemInfo","ops":[{"key":"id_10","field":"max_count","value":2}]}]}""");

        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.Empty(CrimsonDesertFieldManifestInspector.FindClaimCollisions([first, second]));
    }

    [Fact]
    public void Two_different_values_on_one_record_field_are_a_review_collision()
    {
        var first = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"targets":[{"kind":"table","table":"ItemInfo","ops":[{"key":"id_10","field":"buy_price","value":1}]}]}""");
        var second = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"targets":[{"kind":"table","table":"ItemInfo","ops":[{"key":"id_10","field":"buy_price","value":2}]}]}""");

        var collisions = CrimsonDesertFieldManifestInspector.FindClaimCollisions([first, second]);

        Assert.Equal("table:iteminfo:id_10:buy_price", Assert.Single(collisions));
    }

    [Fact]
    public void Assets_and_paloc_use_virtual_resource_keys_without_accessing_game_files()
    {
        var inspected = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"format_minor":1,"targets":[{"kind":"asset","asset_type":"dds","file":"icons/a.dds","vpath":"/ui/icon/a.dds"},{"kind":"paloc","language":"english(us)","entries":[{"category":0,"key":"STR_TEST","value":"Example"}]}]}""");

        Assert.True(inspected.IsValid, inspected.Reason);
        Assert.Contains(inspected.Claims, x => x.TargetKey == "asset:ui/icon/a.dds" && !x.IsSimpleSet);
        Assert.Contains(inspected.Claims, x => x.TargetKey == "paloc:english(us):0:str_test" && !x.IsSimpleSet);
    }

    [Theory]
    [InlineData("""{"format":4,"targets":[{"kind":"table","table":"ItemInfo","ops":[{"key":"x","field":"y","value":1}]}]}""")]
    [InlineData("""{"format":3,"targets":[{"kind":"asset","file":"../outside.dll","vpath":"/ui/asset.dds"}]}""")]
    [InlineData("""{"format":3,"targets":[{"kind":"asset","file":"ok.dds","vpath":"../../outside.dds"}]}""")]
    [InlineData("""{"format":3,"targets":[{"kind":"table","table":"ItemInfo","ops":[{"key":"x","field":"count"}]}]}""")]
    [InlineData("""{"format":3,"targets":[{"kind":"table","table":"ItemInfo","ops":[{"key":"x","field":"count","value":1},{"key":"x","field":"count","value":2}]}]}""")]
    public void Unsafe_or_ambiguous_manifests_are_not_silently_accepted(string json)
    {
        var inspected = CrimsonDesertFieldManifestInspector.Inspect(json);
        Assert.False(inspected.IsValid);
        Assert.Empty(inspected.Claims);
    }

    [Fact]
    public void Large_manifest_is_bounded_before_parsing_untrusted_fields()
    {
        var text = new string(' ', CrimsonDesertFieldManifestInspector.MaxUtf8Bytes + 1);
        var inspected = CrimsonDesertFieldManifestInspector.Inspect(text);
        Assert.False(inspected.IsValid);
    }

    [Fact]
    public void A_thousand_independent_table_claims_remain_bounded_and_have_no_false_collisions()
    {
        var operations = string.Join(",", Enumerable.Range(0, 1_000)
            .Select(i => "{\"key\":\"id_" + i + "\",\"field\":\"buy_price\",\"value\":" + i + "}"));
        var text = "{\"format\":3,\"targets\":[{\"kind\":\"table\",\"table\":\"ItemInfo\",\"ops\":[" +
            operations + "]}]}";

        var inspected = CrimsonDesertFieldManifestInspector.Inspect(text);

        Assert.True(inspected.IsValid, inspected.Reason);
        Assert.Equal(1_000, inspected.Claims.Count);
        Assert.Empty(CrimsonDesertFieldManifestInspector.FindClaimCollisions([inspected, inspected]));
    }

    [Fact]
    public void Nonset_operations_cannot_be_marked_as_independently_composable()
    {
        var inspected = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"targets":[{"file":"iteminfo.staticinfobody","intents":[{"key":10,"op":"delete_record"}]}]}""");

        Assert.True(inspected.IsValid, inspected.Reason);
        Assert.False(Assert.Single(inspected.Claims).IsSimpleSet);
        Assert.Single(CrimsonDesertFieldManifestInspector.FindClaimCollisions([inspected, inspected]));
    }

    [Fact]
    public void Record_wide_operation_must_conflict_with_a_field_edit_on_the_same_record()
    {
        var remove = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"targets":[{"kind":"table","table":"ItemInfo","ops":[{"key":"id_10","op":"delete_record"}]}]}""");
        var edit = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"targets":[{"kind":"table","table":"ItemInfo","ops":[{"key":"id_10","field":"buy_price","value":5}]}]}""");

        Assert.True(remove.IsValid, remove.Reason);
        Assert.True(edit.IsValid, edit.Reason);
        Assert.Contains(CrimsonDesertFieldManifestInspector.FindClaimCollisions([remove, edit]),
            x => x.StartsWith("table:iteminfo:id_10:", StringComparison.Ordinal));
        Assert.Contains(CrimsonDesertFieldManifestInspector.FindClaimCollisions([edit, remove]),
            x => x.StartsWith("table:iteminfo:id_10:", StringComparison.Ordinal));
    }

    [Fact]
    public void Record_wide_operation_must_not_conflict_with_an_unrelated_record_or_table()
    {
        var remove = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"targets":[{"kind":"table","table":"ItemInfo","ops":[{"key":"id_10","op":"delete_record"}]}]}""");
        var unrelated = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"targets":[{"kind":"table","table":"ItemInfo","ops":[{"key":"id_11","field":"buy_price","value":5}]},{"kind":"table","table":"Skill","ops":[{"key":"id_10","field":"buy_price","value":7}]}]}""");

        Assert.True(remove.IsValid, remove.Reason);
        Assert.True(unrelated.IsValid, unrelated.Reason);
        Assert.Empty(CrimsonDesertFieldManifestInspector.FindClaimCollisions([remove, unrelated]));
    }


    [Fact]
    public void Dmm_entry_identity_is_preferred_over_numeric_key_when_it_changes_across_updates()
    {
        var first = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"target":"iteminfo.pabgb","intents":[{"op":"set","entry":"ITEM_LONGSWORD_01","key":123,"field":"buy_price","new":50}]}""");
        var second = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"target":"iteminfo.pabgb","intents":[{"op":"set","entry":"ITEM_LONGSWORD_01","key":987,"field":"buy_price","new":55}]}""");
        Assert.True(first.IsValid, first.Reason);
        Assert.True(second.IsValid, second.Reason);
        Assert.Equal(Assert.Single(first.Claims).TargetKey, Assert.Single(second.Claims).TargetKey);
        Assert.Single(CrimsonDesertFieldManifestInspector.FindClaimCollisions([first, second]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Nested_field_and_parent_edit_overlap_in_both_orders(bool reverse)
    {
        var parent = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"target":"skill.pabgb","intents":[{"op":"set","key":42,"field":"cooltime","new":{"a":10,"b":30}}]}""");
        var child = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"target":"skill.pabgb","intents":[{"op":"set","key":42,"field":"cooltime.a","new":2}]}""");
        Assert.True(parent.IsValid, parent.Reason);
        Assert.True(child.IsValid, child.Reason);
        var collisions = reverse
            ? CrimsonDesertFieldManifestInspector.FindClaimCollisions([child, parent])
            : CrimsonDesertFieldManifestInspector.FindClaimCollisions([parent, child]);
        Assert.Single(collisions);
    }

    [Fact]
    public void Dmm_match_selector_is_not_treated_as_independent_of_concrete_table_rows()
    {
        var matching = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"target":"iteminfo.pabgb","intents":[{"op":"set","match":{"equip_type_info":2667368044},"field":"equipable_level","new":1}]}""");
        var concrete = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"target":"iteminfo.pabgb","intents":[{"op":"set","key":1234,"field":"equipable_level","new":5}]}""");
        Assert.True(matching.IsValid, matching.Reason);
        Assert.True(concrete.IsValid, concrete.Reason);
        Assert.NotEmpty(CrimsonDesertFieldManifestInspector.FindClaimCollisions([matching, concrete]));
        Assert.NotEmpty(CrimsonDesertFieldManifestInspector.FindClaimCollisions([concrete, matching]));
    }

    [Fact]
    public void Dmm_relative_operations_are_staged_but_never_marked_as_simple_composable_sets()
    {
        var observed = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"target":"iteminfo.pabgb","intents":[{"op":"add","key":10,"field":"buy_price","new":1},{"op":"scale","key":11,"field":"sell_price","factor":2},{"op":"list_append","key":12,"field":"effects","new":3}]}""");
        Assert.True(observed.IsValid, observed.Reason);
        Assert.Equal(3, observed.Claims.Count);
        Assert.All(observed.Claims, c => Assert.False(c.IsSimpleSet));
    }

    [Fact]
    public void Dmm_multi_target_requires_file_in_each_target_and_rejects_skipped_intents()
    {
        var observed = CrimsonDesertFieldManifestInspector.Inspect(
            """{"format":3,"targets":[{"target":"skill.pabgb","intents":[{"op":"set","key":42,"field":"cooltime","new":1}]}]}""");
        Assert.False(observed.IsValid);
        Assert.Empty(observed.Claims);
    }


}
