using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ActiveInboxStageTests
{
    [Fact]
    public void Reload_keeps_only_pending_member_edits_and_preserves_new_catalog_defaults()
    {
        var changed = new ModDescriptor("old", "Old", "Old", "old", false, 10);
        var unchanged = new ModDescriptor("same", "Same", "Same", "same", true, 20);
        var added = new ModDescriptor("new", "New", "New", "new", false, 30);
        var original = new ModRowViewModel(new LogicalModFamily("f", "F", "F", [changed, unchanged], null), () => { });
        original.ApplyProfileState(new Dictionary<string, (bool, int)> { ["old"] = (true, 70), ["same"] = (true, 20) });
        var pending = original.StagedChanges().ToDictionary(x => x.Key, x => x.Value);

        // A new authoritative value on an untouched member takes precedence over its old projection.
        var family = new LogicalModFamily("f", "F", "F", [changed, unchanged with { Priority = 25 }, added], null);
        var refreshed = new ModRowViewModel(family, () => { }, pending);
        Assert.Equal((true, 70), refreshed.ExpandStage()["old"]);
        Assert.Equal((true, 25), refreshed.ExpandStage()["same"]);
        Assert.Equal((false, 30), refreshed.ExpandStage()["new"]);
        Assert.Single(refreshed.StagedChanges());
        refreshed.DiscardStaged();
        Assert.Equal((false, 10), refreshed.ExpandStage()["old"]);
    }
}
