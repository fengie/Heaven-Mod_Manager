using MhwModManager.Core;

#pragma warning disable CA1861 // Test fixtures intentionally use compact inline collections for readability.
using Xunit;

namespace MhwModManager.Tests;

public sealed class SmartPackPlannerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddYears(50);

    [Fact]
    public void Incompatible_favorite_author_never_beats_compatible_candidate()
    {
        var plan = Build(
            [
                C("fav","logical-fav","Favorite",author:"Fav",target:"slot-1",compatibility:SmartPackCompatibilityState.Incompatible,popularity:.9),
                C("safe","logical-safe","Safe",author:"Other",target:"slot-1",popularity:.1)
            ],
            [new(SmartPackPriorityKind.Author,"Fav")]);

        Assert.Equal("safe", Assert.Single(plan.Selected).CandidateId);
        var rejected = plan.Decisions.Single(x => x.CandidateId == "fav");
        Assert.Equal(SmartPackDecisionOutcome.Rejected, rejected.Outcome);
        Assert.Equal("compatibility-incompatible", rejected.ReasonCode);
    }

    [Fact]
    public void Favorite_author_outranks_popularity_when_both_are_eligible()
    {
        var plan = Build(
            [
                C("fav","logical-fav","Favorite",author:"Fav",target:"slot-1",popularity:.1),
                C("popular","logical-pop","Popular",author:"Other",target:"slot-1",popularity:.99)
            ],
            [new(SmartPackPriorityKind.Author,"Fav")]);

        var selected = Assert.Single(plan.Selected);
        Assert.Equal("fav", selected.CandidateId);
        Assert.Equal("priority-author", selected.ReasonCode);
    }

    [Fact]
    public void Higher_priority_tag_outranks_lower_priority_tag()
    {
        var plan = Build(
            [
                C("elegant","logical-e","Elegant",tags:["Elegant"],target:"slot-1",popularity:.1),
                C("fantasy","logical-f","Fantasy",tags:["Fantasy"],target:"slot-1",popularity:.99)
            ],
            [
                new(SmartPackPriorityKind.Tag,"Elegant"),
                new(SmartPackPriorityKind.Tag,"Fantasy")
            ]);

        Assert.Equal("elegant", Assert.Single(plan.Selected).CandidateId);
    }

    [Fact]
    public void Popularity_fallback_fills_remaining_compatible_capacity()
    {
        var plan = Build(
            [
                C("priority","logical-p","Priority",tags:["Elegant"],target:"slot-1",popularity:.1),
                C("fallback-high","logical-h","High",target:"slot-2",popularity:.9),
                C("fallback-low","logical-l","Low",target:"slot-2",popularity:.2)
            ],
            [new(SmartPackPriorityKind.Tag,"Elegant")]);

        Assert.Equal(2, plan.Selected.Count);
        Assert.Equal("priority", plan.Selected[0].CandidateId);
        Assert.Equal("fallback-high", plan.Selected[1].CandidateId);
        Assert.Equal("fallback-popularity", plan.Selected[1].ReasonCode);
        Assert.Equal("no-free-compatible-target", plan.Decisions.Single(x => x.CandidateId == "fallback-low").ReasonCode);
    }

    [Fact]
    public void Two_selected_candidates_never_own_same_exclusive_target()
    {
        var plan = Build(
            [
                C("a","logical-a","A",target:"slot-1",popularity:.9),
                C("b","logical-b","B",target:"slot-1",popularity:.8)
            ],
            []);

        Assert.Single(plan.Selected);
        Assert.Single(plan.Selected.Select(x => x.AssignedTarget).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hard_conflict_prevents_incompatible_pair_selection()
    {
        var plan = Build(
            [
                C("a","logical-a","A",author:"Fav",target:"slot-1",conflicts:["logical-b"]),
                C("b","logical-b","B",target:"slot-2")
            ],
            [new(SmartPackPriorityKind.Author,"Fav")]);

        Assert.Equal("a", Assert.Single(plan.Selected).CandidateId);
        var rejected = plan.Decisions.Single(x => x.CandidateId == "b");
        Assert.Equal("hard-conflict", rejected.ReasonCode);
        Assert.Contains("logical-a", rejected.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Safe_alternate_target_allows_deterministic_augmenting_assignment()
    {
        var plan = Build(
            [
                C("a","logical-a","A",author:"Fav",preferredTarget:"slot-1",targets:["slot-1","slot-2"]),
                C("b","logical-b","B",preferredTarget:"slot-1",targets:["slot-1"])
            ],
            [new(SmartPackPriorityKind.Author,"Fav")]);

        Assert.Equal(2, plan.Selected.Count);
        Assert.Equal("slot-2", plan.Selected.Single(x => x.CandidateId == "a").AssignedTarget);
        Assert.Equal("slot-1", plan.Selected.Single(x => x.CandidateId == "b").AssignedTarget);
        Assert.Contains("proven-safe alternate", plan.Selected.Single(x => x.CandidateId == "a").Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_compatibility_is_rejected_under_high_confidence_policy()
    {
        var plan = Build(
            [C("unknown","logical-u","Unknown",target:"slot-1",compatibility:SmartPackCompatibilityState.Unknown)],
            []);

        Assert.Empty(plan.Selected);
        Assert.Equal("compatibility-unknown", Assert.Single(plan.Decisions).ReasonCode);
    }

    [Fact]
    public void Mirror_candidates_with_same_logical_identity_cannot_both_be_selected()
    {
        var plan = Build(
            [
                C("mirror-a","logical-one","Mirror A",target:"slot-1",popularity:.9),
                C("mirror-b","logical-one","Mirror B",target:"slot-2",popularity:.8)
            ],
            []);

        Assert.Single(plan.Selected);
        Assert.Equal("mirror-a", plan.Selected[0].CandidateId);
        Assert.Equal("duplicate-logical-mod", plan.Decisions.Single(x => x.CandidateId == "mirror-b").ReasonCode);
    }

    [Fact]
    public void Installed_equivalents_and_occupied_targets_are_protected()
    {
        var plan = Build(
            [
                C("installed","logical-installed","Installed",target:"slot-2"),
                C("occupied","logical-new","Occupied",target:"slot-1")
            ],
            [],
            occupied:["slot-1"],
            installed:["logical-installed"]);

        Assert.Empty(plan.Selected);
        Assert.Equal("already-installed-equivalent", plan.Decisions.Single(x => x.CandidateId == "installed").ReasonCode);
        Assert.Equal("no-free-compatible-target", plan.Decisions.Single(x => x.CandidateId == "occupied").ReasonCode);
    }

    [Fact]
    public void Unsafe_candidate_leaves_capacity_empty_instead_of_manufacturing_choice()
    {
        var plan = Build(
            [C("bad","logical-bad","Bad",target:"slot-1",compatibility:SmartPackCompatibilityState.Incompatible)],
            []);

        Assert.Empty(plan.Selected);
        Assert.Equal(0, plan.PlannedAssignedTargetCount);
    }

    [Fact]
    public void Input_enumeration_order_does_not_change_plan()
    {
        var candidates = new[]
        {
            C("a","logical-a","A",author:"Fav",preferredTarget:"slot-1",targets:["slot-1","slot-2"],popularity:.1),
            C("b","logical-b","B",preferredTarget:"slot-1",targets:["slot-1"],popularity:.9),
            C("c","logical-c","C",target:"slot-3",popularity:.8)
        };
        var priorities = new[] { new SmartPackPriorityRule(SmartPackPriorityKind.Author,"Fav") };

        var forward = Build(candidates, priorities);
        var reverse = Build(candidates.Reverse().ToArray(), priorities);

        Assert.Equal(
            forward.Selected.Select(x => (x.CandidateId,x.AssignedTarget)).ToArray(),
            reverse.Selected.Select(x => (x.CandidateId,x.AssignedTarget)).ToArray());
        Assert.Equal(
            forward.Decisions.Select(x => (x.CandidateId,x.Outcome,x.AssignedTarget,x.ReasonCode)).ToArray(),
            reverse.Decisions.Select(x => (x.CandidateId,x.Outcome,x.AssignedTarget,x.ReasonCode)).ToArray());
    }

    [Fact]
    public void Explanation_reports_actual_selection_cause()
    {
        var plan = Build(
            [
                C("tagged","logical-t","Tagged",tags:["Elegant"],target:"slot-1",popularity:.1),
                C("popular","logical-p","Popular",target:"slot-1",popularity:.99)
            ],
            [new(SmartPackPriorityKind.Tag,"Elegant")]);

        var selected = Assert.Single(plan.Selected);
        Assert.Equal("priority-tag", selected.ReasonCode);
        Assert.Contains("Elegant", selected.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-free-compatible-target", plan.Decisions.Single(x => x.CandidateId == "popular").ReasonCode);
    }

    [Fact]
    public void Preference_never_overrides_failed_hard_requirement()
    {
        var plan = Build(
            [
                C(
                    "fav","logical-fav","Favorite",author:"Fav",target:"slot-1",
                    failures:[new("missing-dependency","Rejected because mandatory dependency X is unavailable.")]),
                C("safe","logical-safe","Safe",target:"slot-1")
            ],
            [new(SmartPackPriorityKind.Author,"Fav")]);

        Assert.Equal("safe", Assert.Single(plan.Selected).CandidateId);
        var rejected = plan.Decisions.Single(x => x.CandidateId == "fav");
        Assert.Equal("missing-dependency", rejected.ReasonCode);
        Assert.Contains("mandatory dependency", rejected.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Low_confidence_known_compatibility_is_rejected_in_strict_mode()
    {
        var plan = Build(
            [C("low","logical-low","Low",target:"slot-1",confidence:Confidence.Medium)],
            []);

        Assert.Empty(plan.Selected);
        Assert.Equal("compatibility-confidence-too-low", Assert.Single(plan.Decisions).ReasonCode);
    }

    [Fact]
    public void Global_candidate_does_not_consume_exclusive_target()
    {
        var plan = Build(
            [
                C("global","logical-global","Framework",consumesTarget:false),
                C("outfit","logical-outfit","Outfit",target:"slot-1")
            ],
            []);

        Assert.Equal(2, plan.Selected.Count);
        Assert.Null(plan.Selected.Single(x => x.CandidateId == "global").AssignedTarget);
        Assert.Equal(1, plan.PlannedAssignedTargetCount);
    }

    private static SmartPackPlan Build(
        IReadOnlyList<SmartPackCandidate> candidates,
        IReadOnlyList<SmartPackPriorityRule> priorities,
        IReadOnlyCollection<string>? occupied = null,
        IReadOnlyCollection<string>? installed = null,
        bool fillRemainingByPopularity = true) =>
        new SmartPackPlanner().Build(new(
            candidates,
            occupied ?? [],
            installed ?? [],
            priorities,
            SmartPackCompatibilityPolicy.HighConfidenceOnly,
            fillRemainingByPopularity));

    private static SmartPackCandidate C(
        string candidateId,
        string logicalModId,
        string displayName,
        string? author = null,
        string[]? tags = null,
        string? category = null,
        double popularity = .5,
        double? rating = null,
        DateTimeOffset? updatedAt = null,
        SmartPackCompatibilityState compatibility = SmartPackCompatibilityState.Compatible,
        Confidence confidence = Confidence.High,
        SmartPackHardRequirementFailure[]? failures = null,
        string[]? conflicts = null,
        string? target = null,
        string? preferredTarget = null,
        string[]? targets = null,
        bool consumesTarget = true)
    {
        var resolvedTargets = targets ?? (target is null ? [] : [target]);
        return new(
            candidateId,
            logicalModId,
            displayName,
            author,
            tags ?? [],
            category,
            popularity,
            rating,
            updatedAt ?? Now,
            compatibility,
            confidence,
            failures ?? [],
            conflicts ?? [],
            preferredTarget ?? target,
            resolvedTargets,
            consumesTarget);
    }
}
