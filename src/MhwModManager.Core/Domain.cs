namespace MhwModManager.Core;

public enum FileClass
{
    Texture,
    Structural,
    GameData,
    Plugin,
    Executable,
    Other
}
public enum ConflictKind
{
    None,
    Identical,
    SharedTexture,
    TextureOverride,
    PatchOverlay,
    PatchSubset,
    SharedProvider,
    ModFamilyOption,
    UserOverlayRule,
    PossibleOverlay,
    HardStructural,
    HardGameData,
    HardUnknown,
    Incompatible
}
public enum RuleKind
{
    Overlay,
    Incompatible,
    ExactWinner,
    ResourceProvider
}
public enum RuleScope
{
    ModPair,
    ExactPath,
    PathPrefix,
    FileClass,
    ArmorComponent,
    ModFamily
}
public enum Confidence
{
    Explicit,
    High,
    Medium,
    Low
}
public enum ChangeKind
{
    Add,
    Replace,
    Remove,
    RestoreOriginal
}
public enum ExternalChangeKind
{
    Unchanged,
    DeployedModified,
    DeployedReplaced,
    DeployedDeleted,
    ManagedSourceChanged,
    OriginalChanged,
    UnmanagedAppeared
}
public enum OperationState
{
    Prepared,
    Applying,
    FilesWritten,
    StateCommitting,
    Committed,
    RollingBack,
    RolledBack,
    RecoveryRequired,
    Failed
}
public enum FailureCategory
{
    ExpectedTransient,
    UserActionRequired,
    RecoverableOperationFailure,
    DataIntegrityFailure,
    ProgrammingBug,
    FatalProcessState
}
public enum ProvenanceSource
{
    Unknown,
    LocalInference,
    NexusApi,
    ImportedSidecar,
    AdoptedManual
}
public enum NexusFileCategory
{
    Unknown,
    Main,
    Update,
    Optional,
    OldVersion,
    Miscellaneous,
    Removed,
    Archived
}
public enum EffectiveModState
{
    Disabled,
    FullyEffective,
    PartiallyOverridden,
    FullySuperseded,
    NeedsChoice,
    NeedsRevalidation
}

public sealed record ModDescriptor(
    string Id, string Name, string DisplayName, string SourcePath, bool Enabled, int Priority,
    string? FamilyId = null, string? Category = null, string? SourceUrl = null,
    string? NexusModId = null, string? NexusFileId = null,
    string? NexusModUuid = null, string? NexusVersionId = null, string? NexusPreviousVersionId = null,
    NexusFileCategory NexusCategory = NexusFileCategory.Unknown, string? NexusVersion = null,
    DateTimeOffset? NexusUploadedAt = null, bool IsSuperseded = false, string? SupersededByModId = null,
    int ProvenanceScore = 0, ProvenanceSource ProvenanceSource = ProvenanceSource.Unknown,
    string? PreviewPath = null, bool NeedsRevalidation = false, string? FamilyRole = null);

public sealed record ModFileDescriptor(
    string ModId, string Path, string BlobSha256, string? FastHash, long Length,
    DateTimeOffset LastWriteUtc, FileClass FileClass);

public sealed record ProviderCandidate(string ModId, string ModName, int Priority, ModFileDescriptor File);

public sealed record ConflictRule(
    string Id, RuleKind Kind, RuleScope Scope, string? LeftModId, string? RightModId,
    string? WinnerModId, string? PathPattern, string Reason, bool Explicit, DateTimeOffset CreatedUtc,
    int ResolverScore = 0, string? Evidence = null);

public sealed record PairStats(int LeftFiles, int RightFiles, int SharedPaths)
{
    public double SmallerOverlapRatio => Math.Min(LeftFiles, RightFiles) == 0 ? 0 : SharedPaths / (double)Math.Min(LeftFiles, RightFiles);
}

public sealed record ModContentStats(
    int TotalFiles, int TextureFiles, int StructuralFiles, int GameDataFiles,
    DateTimeOffset LatestWriteUtc)
{
    public double TextureRatio => TotalFiles == 0 ? 0 : TextureFiles / (double)TotalFiles;
}

public sealed record ConflictDecision(
    string Path, ConflictKind Kind, bool Blocking, string? WinnerModId,
    string ReasonCode, string Explanation, Confidence Confidence,
    string? RuleId = null, bool Inferred = false, int ResolverScore = 0, string? Evidence = null);

public sealed record DeploymentManifestEntry(
    string Path, string? ProviderModId, string? BlobSha256, string? ExpectedLiveSha256,
    string? RuleId, DateTimeOffset DeployedAt);

public sealed record DeploymentChange(
    int Sequence, ChangeKind Kind, string Path, string? BeforeBlobSha256,
    string? AfterBlobSha256, string? ProviderBefore, string? ProviderAfter,
    string? ExpectedLiveSha256, string? RuleId);

public sealed record DeploymentPlan(
    string Id, DateTimeOffset CreatedUtc, IReadOnlyList<DeploymentChange> Changes,
    IReadOnlyList<ConflictDecision> Conflicts, IReadOnlyList<string> Preconditions)
{
    public bool IsBlocked => Conflicts.Any(x => x.Blocking);
}

public sealed record PlannerSnapshot(
    IReadOnlyList<ModDescriptor> Mods,
    IReadOnlyList<ModFileDescriptor> Files,
    IReadOnlyList<ConflictRule> Rules,
    IReadOnlyDictionary<string,string> ExactWinners,
    IReadOnlyDictionary<string,string> ResourceProviders,
    IReadOnlyDictionary<string,DeploymentManifestEntry> CurrentManifest,
    IReadOnlyDictionary<string,string?> Originals);


public sealed record ModProvenance(
    string ModId, string? NexusGameModId, string? NexusModUuid, string? NexusFileId, string? NexusVersionId,
    string? NexusPreviousVersionId, NexusFileCategory NexusCategory, string? NexusFileName, string? NexusVersion,
    DateTimeOffset? UploadedAt, ProvenanceSource Source, int ConfidenceScore, string? SourceArchiveName,
    string? MetadataJson, DateTimeOffset UpdatedAt);

public sealed record AssetBundleDescriptor(string Key, string DisplayName, IReadOnlyList<string> Paths, FileClass FileClass);
public sealed record ConflictChoiceOption(string LogicalModId, string DisplayName, IReadOnlyList<string> MemberModIds, string? PreviewPath = null);
public sealed record EffectiveModSummary(string LogicalModId, EffectiveModState State, int WinningFiles, int ShadowedFiles, int TotalFiles, string Reason);
public sealed record GameBuildFingerprint(string ExecutablePath, long Length, DateTimeOffset LastWriteUtc, string Sha256);
public sealed record ResolverAudit(string Path, string? WinnerModId, int Score, string ReasonCode, string Explanation, string Evidence);

public sealed record ModFamilySuggestion(string ParentModId, string ChildModId, Confidence Confidence, string Reason);
public sealed record HealthIssue(string Code, string Severity, string Summary, string? Path = null, string? ModId = null, string? Detail = null);
public sealed record OperationResult(
    bool Success,
    string Message,
    string? OperationId = null,
    Exception? Exception = null,
    FailureCategory? FailureCategory = null,
    bool RollbackCompleted = false,
    int FilesChanged = 0);
