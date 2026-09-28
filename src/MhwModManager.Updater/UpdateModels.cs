using System.Text.Json;
using System.Text.Json.Serialization;
using MhwModManager.Core;

namespace MhwModManager.Updater;

public static class UpdateProtocol
{
    public const int ManifestSchemaVersion = 1;
    public const int ProductManifestSchemaVersion = 1;
    public const int BuildIdentitySchemaVersion = 1;
    public const int InstallMarkerSchemaVersion = 1;
    public const int LaunchStateSchemaVersion = 1;
    public const string ProductId = "fengie/mhw-mods:MHW-Manual-Mod-Manager";
    public const int UpdaterProtocolVersion = 1;
    public const string Channel = "main";
    public const string ProductManifestFileName = "product-files.json";
    public const string BuildIdentityFileName = "build-identity.json";
    public const string InstallMarkerFileName = "release-install.json";
    public const string PendingFileName = "pending-update.json";
    public const string HelperDirectoryRelativePath = "UpdaterHelper";
    public const string HelperRelativePath =
        HelperDirectoryRelativePath + "/MHW Mod Manager Updater.exe";
    public const string CredentialTarget = "MhwModManager/GitHubUpdater/fengie/mhw-mods";
    public const string Repository = "fengie/mhw-mods";
    public const long MaxArtifactBytes = 1024L * 1024L * 1024L;
    public const long MaxExtractedBytes = 2L * 1024L * 1024L * 1024L;
    public const int MaxArchiveEntries = 20000;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}

public sealed record UpdateBuildIdentity(
    int SchemaVersion,
    string Channel,
    string ProductVersion,
    string SourceSha,
    long BuildNumber,
    DateTimeOffset BuiltUtc)
{
    public string DisplayId
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return $"{ProductVersion} â€¢ build {BuildNumber} â€¢ {ShortSha}";
        }
    }

    public string ShortSha
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return SourceSha.Length <= 12 ? SourceSha : SourceSha[..12];
        }
    }

    public static UpdateBuildIdentity Load(string installRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"installRoot={installRoot}");
        var path = Path.Combine(installRoot, UpdateProtocol.BuildIdentityFileName);
        if (!File.Exists(path))
            return new(UpdateProtocol.BuildIdentitySchemaVersion, UpdateProtocol.Channel,
                typeof(UpdateBuildIdentity).Assembly.GetName().Version?.ToString() ?? "dev", "development", 0, DateTimeOffset.MinValue);
        var value = JsonSerializer.Deserialize<UpdateBuildIdentity>(File.ReadAllText(path), UpdateProtocol.Json)
                    ?? throw new InvalidDataException("Build identity is empty.");
        if (value.SchemaVersion != UpdateProtocol.BuildIdentitySchemaVersion)
            throw new InvalidDataException($"Unsupported build identity schema {value.SchemaVersion}.");
        return value;
    }

    public void ValidatePublished()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={BuildNumber}");
        if (SchemaVersion != UpdateProtocol.BuildIdentitySchemaVersion)
            throw new InvalidDataException($"Unsupported build identity schema {SchemaVersion}.");
        if (!string.Equals(Channel, UpdateProtocol.Channel, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected build identity channel '{Channel}'.");
        if (string.IsNullOrWhiteSpace(ProductVersion))
            throw new InvalidDataException("Build identity product version is missing.");
        if (string.IsNullOrWhiteSpace(SourceSha) || SourceSha.Length < 7)
            throw new InvalidDataException("Build identity source SHA is missing or malformed.");
        if (BuildNumber <= 0)
            throw new InvalidDataException("Build identity build number must be positive.");
    }

    public static async Task<UpdateBuildIdentity> LoadRequiredAsync(
        string root,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={root}");
        var path = UpdatePathSafety.CombineUnderRoot(root, UpdateProtocol.BuildIdentityFileName);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(root, path);
        if (!File.Exists(path))
            throw new InvalidDataException($"Required build identity is missing: {path}");
        var value = JsonSerializer.Deserialize<UpdateBuildIdentity>(
                        await File.ReadAllTextAsync(path, ct), UpdateProtocol.Json)
                    ?? throw new InvalidDataException("Build identity is empty.");
        value.ValidatePublished();
        return value;
    }
}

public sealed record ReleaseInstallMarker(
    int SchemaVersion,
    string ProductId,
    string Channel,
    UpdateBuildIdentity Build,
    string ExecutableRelativePath,
    string ProductManifestSha256)
{
    public void Validate()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={Build.BuildNumber}");
        if (SchemaVersion != UpdateProtocol.InstallMarkerSchemaVersion)
            throw new InvalidDataException($"Unsupported install marker schema {SchemaVersion}.");
        if (!string.Equals(ProductId, UpdateProtocol.ProductId, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected updater product id '{ProductId}'.");
        if (!string.Equals(Channel, UpdateProtocol.Channel, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected installed update channel '{Channel}'.");
        Build.ValidatePublished();
        if (!string.Equals(Build.Channel, Channel, StringComparison.Ordinal))
            throw new InvalidDataException("Install marker channel does not match its nested build identity.");
        UpdatePathSafety.NormalizeRelativeFilePath(ExecutableRelativePath);
        if (ProductManifestSha256.Length != 64 || ProductManifestSha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Install marker product manifest SHA-256 is malformed.");
    }

    public static async Task<ReleaseInstallMarker> LoadAsync(
        string installRoot,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"installRoot={installRoot}");
        var path = UpdatePathSafety.CombineUnderRoot(installRoot, UpdateProtocol.InstallMarkerFileName);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(installRoot, path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Installed release marker is missing.", path);
        var marker = JsonSerializer.Deserialize<ReleaseInstallMarker>(
                         await File.ReadAllTextAsync(path, ct), UpdateProtocol.Json)
                     ?? throw new InvalidDataException("Installed release marker is empty.");
        marker.Validate();
        return marker;
    }
}

public sealed record UpdateManifest(
    int SchemaVersion,
    string Channel,
    string ProductVersion,
    string SourceSha,
    long BuildNumber,
    string ArtifactName,
    long ArtifactSize,
    string Sha256,
    string ProductManifestSha256,
    string ExecutableRelativePath,
    int MinimumUpdaterProtocol,
    DateTimeOffset PublishedUtc)
{
    public void Validate()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={BuildNumber}");
        if (SchemaVersion != UpdateProtocol.ManifestSchemaVersion)
            throw new InvalidDataException($"Unsupported update manifest schema {SchemaVersion}.");
        if (!string.Equals(Channel, UpdateProtocol.Channel, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected update channel '{Channel}'.");
        if (string.IsNullOrWhiteSpace(ProductVersion))
            throw new InvalidDataException("Update product version is missing.");
        if (BuildNumber <= 0) throw new InvalidDataException("Update build number must be positive.");
        if (ArtifactSize <= 0 || ArtifactSize > UpdateProtocol.MaxArtifactBytes)
            throw new InvalidDataException($"Update artifact size {ArtifactSize} is outside the allowed budget.");
        RequireSha256(Sha256, nameof(Sha256));
        RequireSha256(ProductManifestSha256, nameof(ProductManifestSha256));
        UpdatePathSafety.NormalizeRelativeFilePath(ArtifactName);
        UpdatePathSafety.NormalizeRelativeFilePath(ExecutableRelativePath);
        if (MinimumUpdaterProtocol > UpdateProtocol.UpdaterProtocolVersion)
            throw new InvalidDataException($"This update requires updater protocol {MinimumUpdaterProtocol}.");
        if (string.IsNullOrWhiteSpace(SourceSha) || SourceSha.Length < 7)
            throw new InvalidDataException("Update source SHA is missing or malformed.");
    }

    private static void RequireSha256(string value, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"name={name}");
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException($"{name} must be a 64-character SHA-256.");
    }
}

public sealed record ProductFileEntry(string Path, long Size, string Sha256);

public sealed record ProductFileManifest(int SchemaVersion, IReadOnlyList<ProductFileEntry> Files)
{
    public void Validate()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"files={Files.Count}");
        if (SchemaVersion != UpdateProtocol.ProductManifestSchemaVersion)
            throw new InvalidDataException($"Unsupported product manifest schema {SchemaVersion}.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Files)
        {
            var normalized = UpdatePathSafety.NormalizeRelativeFilePath(entry.Path);
            if (!seen.Add(normalized))
                throw new InvalidDataException($"Duplicate/case-colliding product path '{entry.Path}'.");
            if (entry.Size < 0) throw new InvalidDataException($"Negative product file size for '{entry.Path}'.");
            if (entry.Sha256.Length != 64 || entry.Sha256.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException($"Invalid product SHA-256 for '{entry.Path}'.");
            UpdatePathSafety.RejectUserDataPath(normalized);
        }
    }
}

public sealed record UpdateCandidate(UpdateManifest Manifest, Uri ArtifactApiUri, Uri ManifestApiUri);

public sealed record StagedUpdate(UpdateManifest Manifest, string StagingRoot, string ProductManifestPath);

public enum UpdateJournalPhase
{
    Discovered,
    Downloaded,
    Verified,
    Staged,
    BackupCreated,
    Applying,
    AppliedAwaitingHealth,
    Confirmed,
    RollbackRequired,
    RolledBack,
    Failed
}

public enum UpdateApplyFaultPoint
{
    BeforeBackup,
    AfterBackup,
    BeforeFileApply,
    AfterFileApply,
    BeforeStaleOwnedRemoval,
    AfterStaleOwnedRemoval,
    BeforeInstalledVerification
}

public sealed record UpdateApplyRequest(
    UpdateManifest Manifest,
    string InstallRoot,
    string StagingRoot,
    string BackupRoot,
    string JournalPath,
    string PendingPath,
    string HealthFile,
    string HealthToken,
    int CurrentProcessId,
    IReadOnlyList<string> RestartArguments);

public sealed record UpdateJournal(
    UpdateJournalPhase Phase,
    long TargetBuildNumber,
    string TargetSourceSha,
    DateTimeOffset UpdatedUtc,
    string? Detail = null);

internal sealed record GitHubReleaseDto(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAssetDto> Assets);

internal sealed record GitHubAssetDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string ApiUrl,
    [property: JsonPropertyName("size")] long Size);
