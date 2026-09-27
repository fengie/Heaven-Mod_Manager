namespace MhwModManager.Core;

public enum SmartPackCompatibilityState
{
    Compatible,
    Unknown,
    Incompatible
}

public enum SmartPackCompatibilityPolicy
{
    HighConfidenceOnly,
    AllowKnownCompatible
}

public enum SmartPackPriorityKind
{
    Author,
    Tag,
    Category,
    Rating,
    Recency,
    Popularity
}

public enum SmartPackDecisionOutcome
{
    Selected,
    Rejected
}

public sealed record SmartPackHardRequirementFailure(string Code, string Explanation);

public sealed record SmartPackPriorityRule(SmartPackPriorityKind Kind, string? Value = null);

public sealed record SmartPackCandidate(
    string CandidateId,
    string LogicalModId,
    string DisplayName,
    string? Author,
    IReadOnlyList<string> Tags,
    string? Category,
    double NormalizedPopularity,
    double? Rating,
    DateTimeOffset? UpdatedAt,
    SmartPackCompatibilityState Compatibility,
    Confidence CompatibilityConfidence,
    IReadOnlyList<SmartPackHardRequirementFailure> HardRequirementFailures,
    IReadOnlyList<string> HardConflictsWithLogicalModIds,
    string? PreferredTarget,
    IReadOnlyList<string> PossibleTargets,
    bool ConsumesExclusiveTarget = true);

public sealed record SmartPackPlannerRequest(
    IReadOnlyList<SmartPackCandidate> Candidates,
    IReadOnlyCollection<string> OccupiedTargets,
    IReadOnlyCollection<string> InstalledLogicalModIds,
    IReadOnlyList<SmartPackPriorityRule> Priorities,
    SmartPackCompatibilityPolicy CompatibilityPolicy = SmartPackCompatibilityPolicy.HighConfidenceOnly,
    bool FillRemainingByPopularity = true);

public sealed record SmartPackSelection(
    string CandidateId,
    string LogicalModId,
    string DisplayName,
    string? AssignedTarget,
    string ReasonCode,
    string Explanation);

public sealed record SmartPackDecision(
    string CandidateId,
    string LogicalModId,
    SmartPackDecisionOutcome Outcome,
    string? AssignedTarget,
    string ReasonCode,
    string Explanation);

public sealed record SmartPackPlan(
    IReadOnlyList<SmartPackSelection> Selected,
    IReadOnlyList<SmartPackDecision> Decisions,
    int InitialOccupiedTargetCount,
    int PlannedAssignedTargetCount);

/// <summary>
/// Pure provider-neutral planner for selecting a compatible proposed mod pack from normalized
/// candidate evidence. It performs no discovery, downloads, persistence, archive mutation or
/// deployment; upstream adapters remain responsible for proving compatibility and safe targets.
/// </summary>
public sealed class SmartPackPlanner
{
    public SmartPackPlan Build(SmartPackPlannerRequest request)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidates);
        ArgumentNullException.ThrowIfNull(request.OccupiedTargets);
        ArgumentNullException.ThrowIfNull(request.InstalledLogicalModIds);
        ArgumentNullException.ThrowIfNull(request.Priorities);

        var stableStringComparison = (string left, string right) =>
        {
            var comparison = StringComparer.OrdinalIgnoreCase.Compare(left, right);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left, right);
        };

        var candidates = request.Candidates.ToArray();
        var candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.CandidateId))
                throw new ArgumentException("Every Smart Pack candidate requires a stable candidate/source identity.", nameof(request));
            if (string.IsNullOrWhiteSpace(candidate.LogicalModId))
                throw new ArgumentException($"Candidate '{candidate.CandidateId}' requires a normalized logical mod identity.", nameof(request));
            if (!candidateIds.Add(candidate.CandidateId))
                throw new ArgumentException($"Duplicate Smart Pack candidate identity '{candidate.CandidateId}'.", nameof(request));
            ArgumentNullException.ThrowIfNull(candidate.Tags);
            ArgumentNullException.ThrowIfNull(candidate.HardRequirementFailures);
            ArgumentNullException.ThrowIfNull(candidate.HardConflictsWithLogicalModIds);
            ArgumentNullException.ThrowIfNull(candidate.PossibleTargets);
        }

        var occupiedTargets = request.OccupiedTargets
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedLogicalIds = request.InstalledLogicalModIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var conflictIndex = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!conflictIndex.TryGetValue(candidate.LogicalModId, out var conflicts))
                conflictIndex[candidate.LogicalModId] = conflicts = new(StringComparer.OrdinalIgnoreCase);

            foreach (var other in candidate.HardConflictsWithLogicalModIds.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                conflicts.Add(other);
                if (!conflictIndex.TryGetValue(other, out var reverse))
                    conflictIndex[other] = reverse = new(StringComparer.OrdinalIgnoreCase);
                reverse.Add(candidate.LogicalModId);
            }
        }

        var hasSelectorPriority = request.Priorities.Any(x =>
            x.Kind is SmartPackPriorityKind.Author or SmartPackPriorityKind.Tag or SmartPackPriorityKind.Category);
        var hasNumericPriority = request.Priorities.Any(x =>
            x.Kind is SmartPackPriorityKind.Rating or SmartPackPriorityKind.Recency or SmartPackPriorityKind.Popularity);
        var hasExplicitPopularityPriority = request.Priorities.Any(x => x.Kind == SmartPackPriorityKind.Popularity);

        var selectorMatches = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var matches = false;
            foreach (var priority in request.Priorities)
            {
                if (string.IsNullOrWhiteSpace(priority.Value))
                    continue;

                if (priority.Kind == SmartPackPriorityKind.Author &&
                    !string.IsNullOrWhiteSpace(candidate.Author) &&
                    StringComparer.OrdinalIgnoreCase.Equals(candidate.Author, priority.Value))
                {
                    matches = true;
                    break;
                }

                if (priority.Kind == SmartPackPriorityKind.Tag &&
                    candidate.Tags.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x, priority.Value)))
                {
                    matches = true;
                    break;
                }

                if (priority.Kind == SmartPackPriorityKind.Category &&
                    !string.IsNullOrWhiteSpace(candidate.Category) &&
                    StringComparer.OrdinalIgnoreCase.Equals(candidate.Category, priority.Value))
                {
                    matches = true;
                    break;
                }
            }
            selectorMatches[candidate.CandidateId] = matches;
        }

        var ordered = candidates.ToArray();
        Array.Sort(ordered, Comparer<SmartPackCandidate>.Create((left, right) =>
        {
            foreach (var priority in request.Priorities)
            {
                var comparison = 0;
                switch (priority.Kind)
                {
                    case SmartPackPriorityKind.Author:
                    {
                        var leftMatch = !string.IsNullOrWhiteSpace(priority.Value) &&
                                        !string.IsNullOrWhiteSpace(left.Author) &&
                                        StringComparer.OrdinalIgnoreCase.Equals(left.Author, priority.Value);
                        var rightMatch = !string.IsNullOrWhiteSpace(priority.Value) &&
                                         !string.IsNullOrWhiteSpace(right.Author) &&
                                         StringComparer.OrdinalIgnoreCase.Equals(right.Author, priority.Value);
                        comparison = rightMatch.CompareTo(leftMatch);
                        break;
                    }
                    case SmartPackPriorityKind.Tag:
                    {
                        var leftMatch = !string.IsNullOrWhiteSpace(priority.Value) &&
                                        left.Tags.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x, priority.Value));
                        var rightMatch = !string.IsNullOrWhiteSpace(priority.Value) &&
                                         right.Tags.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x, priority.Value));
                        comparison = rightMatch.CompareTo(leftMatch);
                        break;
                    }
                    case SmartPackPriorityKind.Category:
                    {
                        var leftMatch = !string.IsNullOrWhiteSpace(priority.Value) &&
                                        !string.IsNullOrWhiteSpace(left.Category) &&
                                        StringComparer.OrdinalIgnoreCase.Equals(left.Category, priority.Value);
                        var rightMatch = !string.IsNullOrWhiteSpace(priority.Value) &&
                                         !string.IsNullOrWhiteSpace(right.Category) &&
                                         StringComparer.OrdinalIgnoreCase.Equals(right.Category, priority.Value);
                        comparison = rightMatch.CompareTo(leftMatch);
                        break;
                    }
                    case SmartPackPriorityKind.Rating:
                    {
                        var leftRating = left.Rating is double lr && double.IsFinite(lr) ? lr : double.NegativeInfinity;
                        var rightRating = right.Rating is double rr && double.IsFinite(rr) ? rr : double.NegativeInfinity;
                        comparison = rightRating.CompareTo(leftRating);
                        break;
                    }
                    case SmartPackPriorityKind.Recency:
                    {
                        var leftRecency = left.UpdatedAt?.UtcTicks ?? long.MinValue;
                        var rightRecency = right.UpdatedAt?.UtcTicks ?? long.MinValue;
                        comparison = rightRecency.CompareTo(leftRecency);
                        break;
                    }
                    case SmartPackPriorityKind.Popularity:
                    {
                        var leftPopularity = double.IsFinite(left.NormalizedPopularity) ? left.NormalizedPopularity : 0d;
                        var rightPopularity = double.IsFinite(right.NormalizedPopularity) ? right.NormalizedPopularity : 0d;
                        comparison = rightPopularity.CompareTo(leftPopularity);
                        break;
                    }
                }

                if (comparison != 0)
                    return comparison;
            }

            if (request.FillRemainingByPopularity && !hasExplicitPopularityPriority)
            {
                var leftPopularity = double.IsFinite(left.NormalizedPopularity) ? left.NormalizedPopularity : 0d;
                var rightPopularity = double.IsFinite(right.NormalizedPopularity) ? right.NormalizedPopularity : 0d;
                var popularityComparison = rightPopularity.CompareTo(leftPopularity);
                if (popularityComparison != 0)
                    return popularityComparison;
            }

            var logicalComparison = stableStringComparison(left.LogicalModId, right.LogicalModId);
            if (logicalComparison != 0)
                return logicalComparison;
            return stableStringComparison(left.CandidateId, right.CandidateId);
        }));

        var decisions = new Dictionary<string, SmartPackDecision>(StringComparer.OrdinalIgnoreCase);
        var eligible = new List<SmartPackCandidate>(ordered.Length);

        foreach (var candidate in ordered)
        {
            if (candidate.Compatibility == SmartPackCompatibilityState.Incompatible)
            {
                decisions[candidate.CandidateId] = new(
                    candidate.CandidateId,
                    candidate.LogicalModId,
                    SmartPackDecisionOutcome.Rejected,
                    null,
                    "compatibility-incompatible",
                    "Rejected because normalized compatibility evidence marks this candidate incompatible.");
                continue;
            }

            if (candidate.Compatibility == SmartPackCompatibilityState.Unknown)
            {
                decisions[candidate.CandidateId] = new(
                    candidate.CandidateId,
                    candidate.LogicalModId,
                    SmartPackDecisionOutcome.Rejected,
                    null,
                    "compatibility-unknown",
                    "Rejected because compatibility is unknown; unattended Smart Pack planning fails closed.");
                continue;
            }

            if (request.CompatibilityPolicy == SmartPackCompatibilityPolicy.HighConfidenceOnly &&
                candidate.CompatibilityConfidence is not Confidence.Explicit and not Confidence.High)
            {
                decisions[candidate.CandidateId] = new(
                    candidate.CandidateId,
                    candidate.LogicalModId,
                    SmartPackDecisionOutcome.Rejected,
                    null,
                    "compatibility-confidence-too-low",
                    $"Rejected because compatibility confidence is {candidate.CompatibilityConfidence}, below the High Confidence Only policy.");
                continue;
            }

            var hardFailure = candidate.HardRequirementFailures
                .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Code, StringComparer.Ordinal)
                .ThenBy(x => x.Explanation, StringComparer.Ordinal)
                .FirstOrDefault();
            if (hardFailure is not null)
            {
                decisions[candidate.CandidateId] = new(
                    candidate.CandidateId,
                    candidate.LogicalModId,
                    SmartPackDecisionOutcome.Rejected,
                    null,
                    string.IsNullOrWhiteSpace(hardFailure.Code) ? "hard-requirement-failed" : hardFailure.Code,
                    string.IsNullOrWhiteSpace(hardFailure.Explanation)
                        ? "Rejected because a normalized hard requirement failed."
                        : hardFailure.Explanation);
                continue;
            }

            if (installedLogicalIds.Contains(candidate.LogicalModId))
            {
                decisions[candidate.CandidateId] = new(
                    candidate.CandidateId,
                    candidate.LogicalModId,
                    SmartPackDecisionOutcome.Rejected,
                    null,
                    "already-installed-equivalent",
                    $"Rejected because logical mod '{candidate.LogicalModId}' is already installed.");
                continue;
            }

            if (hasSelectorPriority && !hasNumericPriority && !selectorMatches[candidate.CandidateId] &&
                !request.FillRemainingByPopularity)
            {
                decisions[candidate.CandidateId] = new(
                    candidate.CandidateId,
                    candidate.LogicalModId,
                    SmartPackDecisionOutcome.Rejected,
                    null,
                    "no-priority-match",
                    "Rejected because the candidate matched none of the configured selector priorities and popularity fallback is disabled.");
                continue;
            }

            var normalizedTargets = candidate.PossibleTargets
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x, StringComparer.Ordinal)
                .ToArray();

            if (candidate.ConsumesExclusiveTarget && normalizedTargets.Length == 0)
            {
                decisions[candidate.CandidateId] = new(
                    candidate.CandidateId,
                    candidate.LogicalModId,
                    SmartPackDecisionOutcome.Rejected,
                    null,
                    "no-safe-target",
                    "Rejected because no proven-safe exclusive target was supplied.");
                continue;
            }

            eligible.Add(candidate);
        }

        var candidateById = eligible.ToDictionary(x => x.CandidateId, StringComparer.OrdinalIgnoreCase);
        var targetOptions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in eligible.Where(x => x.ConsumesExclusiveTarget))
        {
            var all = candidate.PossibleTargets
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x, StringComparer.Ordinal)
                .ToList();

            if (!string.IsNullOrWhiteSpace(candidate.PreferredTarget))
            {
                var preferredIndex = all.FindIndex(x => StringComparer.OrdinalIgnoreCase.Equals(x, candidate.PreferredTarget));
                if (preferredIndex > 0)
                {
                    var preferred = all[preferredIndex];
                    all.RemoveAt(preferredIndex);
                    all.Insert(0, preferred);
                }
            }

            targetOptions[candidate.CandidateId] = all.ToArray();
        }

        var selectedLogicalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedCandidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedInOrder = new List<string>();
        var assignmentByCandidate = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var candidateByTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var selectionReason = new Dictionary<string, (string Code, string Explanation)>(StringComparer.OrdinalIgnoreCase);

        Func<string, HashSet<string>, HashSet<string>, bool>? tryAssign = null;
        tryAssign = (candidateId, visitedCandidates, visitedTargets) =>
        {
            if (!visitedCandidates.Add(candidateId))
                return false;
            if (!targetOptions.TryGetValue(candidateId, out var options))
                return false;

            foreach (var target in options)
            {
                if (occupiedTargets.Contains(target) || !visitedTargets.Add(target))
                    continue;

                if (!candidateByTarget.TryGetValue(target, out var occupantId))
                {
                    candidateByTarget[target] = candidateId;
                    assignmentByCandidate[candidateId] = target;
                    return true;
                }

                if (tryAssign(occupantId, visitedCandidates, visitedTargets))
                {
                    candidateByTarget[target] = candidateId;
                    assignmentByCandidate[candidateId] = target;
                    return true;
                }
            }

            return false;
        };

        foreach (var candidate in eligible)
        {
            if (selectedLogicalIds.Contains(candidate.LogicalModId))
            {
                decisions[candidate.CandidateId] = new(
                    candidate.CandidateId,
                    candidate.LogicalModId,
                    SmartPackDecisionOutcome.Rejected,
                    null,
                    "duplicate-logical-mod",
                    $"Rejected because another selected candidate already represents logical mod '{candidate.LogicalModId}'.");
                continue;
            }

            if (conflictIndex.TryGetValue(candidate.LogicalModId, out var hardConflicts))
            {
                var activeConflict = hardConflicts
                    .Where(x => installedLogicalIds.Contains(x) || selectedLogicalIds.Contains(x))
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (activeConflict is not null)
                {
                    decisions[candidate.CandidateId] = new(
                        candidate.CandidateId,
                        candidate.LogicalModId,
                        SmartPackDecisionOutcome.Rejected,
                        null,
                        "hard-conflict",
                        $"Rejected because it has a normalized hard conflict with logical mod '{activeConflict}'.");
                    continue;
                }
            }

            if (candidate.ConsumesExclusiveTarget &&
                !tryAssign(candidate.CandidateId, new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase)))
            {
                decisions[candidate.CandidateId] = new(
                    candidate.CandidateId,
                    candidate.LogicalModId,
                    SmartPackDecisionOutcome.Rejected,
                    null,
                    "no-free-compatible-target",
                    "Rejected because no proven-safe target remained after protected occupied targets and higher-priority compatible selections.");
                continue;
            }

            selectedLogicalIds.Add(candidate.LogicalModId);
            selectedCandidateIds.Add(candidate.CandidateId);
            selectedInOrder.Add(candidate.CandidateId);

            var reasonCode = string.Empty;
            var reasonText = string.Empty;
            foreach (var priority in request.Priorities)
            {
                if (priority.Kind == SmartPackPriorityKind.Author &&
                    !string.IsNullOrWhiteSpace(priority.Value) &&
                    !string.IsNullOrWhiteSpace(candidate.Author) &&
                    StringComparer.OrdinalIgnoreCase.Equals(candidate.Author, priority.Value))
                {
                    reasonCode = "priority-author";
                    reasonText = $"matched preferred author '{priority.Value}'";
                    break;
                }

                if (priority.Kind == SmartPackPriorityKind.Tag &&
                    !string.IsNullOrWhiteSpace(priority.Value) &&
                    candidate.Tags.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x, priority.Value)))
                {
                    reasonCode = "priority-tag";
                    reasonText = $"matched preferred tag '{priority.Value}'";
                    break;
                }

                if (priority.Kind == SmartPackPriorityKind.Category &&
                    !string.IsNullOrWhiteSpace(priority.Value) &&
                    !string.IsNullOrWhiteSpace(candidate.Category) &&
                    StringComparer.OrdinalIgnoreCase.Equals(candidate.Category, priority.Value))
                {
                    reasonCode = "priority-category";
                    reasonText = $"matched preferred category '{priority.Value}'";
                    break;
                }

                if (priority.Kind == SmartPackPriorityKind.Rating && candidate.Rating is double rating && double.IsFinite(rating))
                {
                    reasonCode = "priority-rating";
                    reasonText = "was ordered by the configured rating priority";
                    break;
                }

                if (priority.Kind == SmartPackPriorityKind.Recency && candidate.UpdatedAt is not null)
                {
                    reasonCode = "priority-recency";
                    reasonText = "was ordered by the configured recency priority";
                    break;
                }

                if (priority.Kind == SmartPackPriorityKind.Popularity)
                {
                    reasonCode = "priority-popularity";
                    reasonText = "was ordered by the configured normalized-popularity priority";
                    break;
                }
            }

            if (string.IsNullOrEmpty(reasonCode))
            {
                if (request.FillRemainingByPopularity)
                {
                    reasonCode = "fallback-popularity";
                    reasonText = "was eligible for normalized-popularity fallback";
                }
                else
                {
                    reasonCode = "coverage-fill";
                    reasonText = "was eligible under the configured priorities";
                }
            }

            selectionReason[candidate.CandidateId] = (reasonCode, reasonText);
        }

        var selected = new List<SmartPackSelection>(selectedInOrder.Count);
        foreach (var candidateId in selectedInOrder)
        {
            var candidate = candidateById[candidateId];
            assignmentByCandidate.TryGetValue(candidateId, out var assignedTarget);
            var reason = selectionReason[candidateId];
            var targetExplanation = candidate.ConsumesExclusiveTarget
                ? assignedTarget is not null &&
                  !string.IsNullOrWhiteSpace(candidate.PreferredTarget) &&
                  !StringComparer.OrdinalIgnoreCase.Equals(candidate.PreferredTarget, assignedTarget)
                    ? $" Final matching assigned proven-safe alternate target '{assignedTarget}' instead of preferred target '{candidate.PreferredTarget}'."
                    : $" Assigned target '{assignedTarget}'."
                : " This candidate does not consume an exclusive target.";
            var explanation = $"Selected because it {reason.Explanation}.{targetExplanation}";

            selected.Add(new(
                candidate.CandidateId,
                candidate.LogicalModId,
                candidate.DisplayName,
                assignedTarget,
                reason.Code,
                explanation));
            decisions[candidate.CandidateId] = new(
                candidate.CandidateId,
                candidate.LogicalModId,
                SmartPackDecisionOutcome.Selected,
                assignedTarget,
                reason.Code,
                explanation);
        }

        var decisionList = decisions.Values
            .OrderBy(x => x.LogicalModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LogicalModId, StringComparer.Ordinal)
            .ThenBy(x => x.CandidateId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.CandidateId, StringComparer.Ordinal)
            .ToArray();

        return new(
            selected,
            decisionList,
            occupiedTargets.Count,
            assignmentByCandidate.Count);
    }
}
