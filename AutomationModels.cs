namespace MhwModManager.Automation;

public enum AutomationSeverity { Info, Warning, Blocker }
public enum LaunchMode { Modded, Vanilla, Diagnostic }
public enum ModIssueKind { StartupCrash, GameCrash, GpuGraphicsCrash, BisectIsolated }
public enum AutomationCategory { Armor, Weapon, Npc, Handler, Palico, Body, Texture, Ui, Audio, Plugin, Gameplay, QuestData, Mixed, Unknown }

public sealed record AutomationFinding(string Code, AutomationSeverity Severity, string Summary, string Detail, string? ModId = null);
public sealed record AutomationEvent(string Id, DateTimeOffset Time, string Kind, AutomationSeverity Severity, string Message, string? DataJson);
public sealed record SaveSnapshotResult(bool Success, string SnapshotId, string SnapshotRoot, string? SaveSource, int FilesCopied, string Message);
public sealed record LastKnownGoodState(DateTimeOffset RecordedAt, string? SnapshotRoot, string? GameBuildSha256, IReadOnlyDictionary<string, ModState> Mods);
public sealed record ModState(bool Enabled, int Priority);
public sealed record UpdateDiffSummary(string OlderModId, string NewerModId, int Added, int Removed, int Changed, int Unchanged, int StructuralChanged, int TextureChanged, IReadOnlyList<string> ChangedPaths);
public sealed record InboxItemResult(string Source, string? Destination, bool Imported, AutomationCategory Category, string Message);
public sealed record InboxRunResult(int Found, int Imported, int Skipped, IReadOnlyList<InboxItemResult> Items);
public sealed record DependencyStatus(string ModId, string ModName, bool Ready, IReadOnlyList<string> Missing, IReadOnlyList<string> Evidence);
public sealed record DuplicateGroup(string Fingerprint, IReadOnlyList<string> ModIds, IReadOnlyList<string> SourcePaths, long ReclaimableBytes, string RecommendedKeeperId);
public sealed record DuplicateAnalysis(IReadOnlyList<DuplicateGroup> ExactDuplicates, IReadOnlyList<string> SupersededModIds, long ReclaimableBytes);
public sealed record TrustSnapshot(string ModId, int SuccessfulLaunches, int FailedLaunches, int Rollbacks, DateTimeOffset? LastSuccess, DateTimeOffset? LastFailure);
public sealed record LaunchHealthReport(bool Ready, IReadOnlyList<AutomationFinding> Findings)
{
    public int Blockers => Findings.Count(x => x.Severity == AutomationSeverity.Blocker);
    public int Warnings => Findings.Count(x => x.Severity == AutomationSeverity.Warning);
}
public sealed record LaunchObservation(bool Started, bool StartupSurvived, int? ExitCode, TimeSpan ObservedFor, string Message);
public sealed record CrashBisectResult(bool Isolated, IReadOnlyList<string> Suspects, int Probes, string Message);
public sealed record ModIssueSuspect(string ModId, string DisplayName, ModIssueKind Kind, int Score, string Reason, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, int FailureCount, bool Confirmed, string? LastLaunchId);
public sealed record IssueDiagnosisResult(ModIssueKind Kind, string LaunchId, string? BaselineLaunchId, IReadOnlyList<ModIssueSuspect> Suspects, string Message);
public sealed record GameUpdateImpactReport(bool Changed, int Revalidate, int TextureOnly, int OtherLowRisk, IReadOnlyList<string> HighRiskMods, string Message);
public sealed record EffectiveFileProvider(string Path, string? EffectiveModId, string? EffectiveModName, IReadOnlyList<string> ShadowedModIds, string? Sha256);
public sealed record AssetHeatmapRow(string AssetKey, string DisplayName, int ProviderCount, bool NeedsChoice, IReadOnlyList<string> Providers);
public sealed record OutfitPreset(string FamilyId, string Name, IReadOnlySet<string> EnabledMemberIds, string Description);
public sealed record StartupMaintenanceResult(InboxRunResult Inbox, DuplicateAnalysis Duplicates, int CategoriesAssigned, IReadOnlyList<DependencyStatus> Dependencies, string Summary);
