using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class DeploymentReviewTests
{
    [Fact]
    public void Exact_plan_projects_provider_changes_and_resolved_overlaps_without_guessing()
    {
        var mods = new ModDescriptor[]
        {
            new("base", "Body", "Body", "base", true, 1),
            new("overlay", "Texture overlay", "Texture overlay", "overlay", true, 2)
        };
        const string path = @"nativePC\body.tex";
        const string unchanged = @"nativePC\detail.tex";
        var decision = new ConflictDecision(path, ConflictKind.UserOverlayRule, false, "overlay",
            "remembered-overlay", "Explicit overlay", Confidence.Explicit, "rule");
        var plan = new DeploymentPlan("review", DateTimeOffset.UnixEpoch,
            [new(1, ChangeKind.Replace, path, "old", "new", "base", "overlay", "old", "rule"),
             new(2, ChangeKind.RestoreOriginal, @"nativePC\stock.tex", "modded", "stock", "base", null, "modded", null)],
            [decision, decision with { Path = unchanged }], []);
        var rows = DeploymentReview.Project(plan, mods);
        Assert.Equal(3, rows.Count);
        var replacement = Assert.Single(rows, r => r.Path == path);
        Assert.Equal("Replace", replacement.Action);
        Assert.Equal("Body [base]", replacement.BeforeProvider);
        Assert.Equal("Texture overlay [overlay]", replacement.AfterProvider);
        Assert.Equal("remembered-overlay", replacement.ReasonCode);
        Assert.Contains("Explicit overlay", replacement.Detail);
        var restore = Assert.Single(rows, r => r.Action == "RestoreOriginal");
        Assert.Equal("Original backup", restore.AfterProvider);
        Assert.Equal("stock", restore.AfterDigest);
        var resolved = Assert.Single(rows, r => r.Path == unchanged);
        Assert.Equal("Resolved (no file change)", resolved.Action);
        Assert.Equal(replacement.AfterProvider, resolved.AfterProvider);
    }

    [Fact]
    public void Blocked_plan_lists_each_global_blocker_and_never_presents_executable_writes()
    {
        var plan = new DeploymentPlan("blocked", DateTimeOffset.UnixEpoch, [],
            [new("<family>", ConflictKind.Incompatible, true, null, "exclusive-family-choice", "Choose red or blue.", Confidence.Explicit),
             new("<family>", ConflictKind.Incompatible, true, null, "exclusive-family-choice", "Choose wide or narrow.", Confidence.Explicit),
             new(@"nativePC\safe.tex", ConflictKind.None, false, "base", "single-provider", "Safe", Confidence.High)], []);
        var rows = DeploymentReview.Project(plan, []);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("Blocked", row.Action));
        Assert.Contains(rows, row => row.Detail.Contains("red or blue", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Detail.Contains("wide or narrow", StringComparison.Ordinal));
    }

    [Fact]
    public void Identical_content_is_not_labelled_incompatible()
    {
        var plan = new DeploymentPlan("same", DateTimeOffset.UnixEpoch, [],
            [new(@"nativePC\shared.tex", ConflictKind.Identical, false, "a", "identical-content", "Same bytes.", Confidence.High)], []);
        var row = Assert.Single(DeploymentReview.Project(plan, []));
        Assert.Equal("Resolved (no file change)", row.Action);
        Assert.Equal("a", row.AfterProvider);
        Assert.Equal("identical-content", row.ReasonCode);
    }
}
