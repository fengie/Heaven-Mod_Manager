using System.Text.Json;
using System.Text.Json.Serialization;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public enum VortexInteropMatchKind
{
    FoundLocally,
    FoundThroughNexusIdentity,
    Missing,
    HashMismatch,
    Ambiguous
}

public sealed record VortexInteropGame(
    string VortexGameId,
    string? NexusPageId,
    string? SteamAppId,
    string ModPath,
    string Executable);

public sealed record VortexInteropMod(
    string LocalModId,
    string DisplayName,
    bool Enabled,
    int Priority,
    string? NexusModId = null,
    string? NexusFileId = null,
    string? Version = null,
    IReadOnlyDictionary<string, string>? Files = null);

public sealed record VortexInteropManifest(
    int Format,
    DateTimeOffset ExportedAt,
    VortexInteropGame Game,
    IReadOnlyList<VortexInteropMod> Mods);

public sealed record VortexInteropMatch(
    VortexInteropMod Entry,
    VortexInteropMatchKind Kind,
    string? LocalModId,
    string Detail,
    bool CanRestore);

public sealed record VortexInteropPreview(
    VortexInteropManifest Manifest,
    IReadOnlyList<VortexInteropMatch> Matches);

public sealed record SteamWorkshopInteropSupport(bool Supported, string Reason);

/// <summary>
/// Explicit compatibility contract for Vortex handoff metadata. This is intentionally not a
/// Vortex remote-catalog adapter and never reads Vortex private state or authenticated sessions.
/// </summary>
public static class VortexInteropContract
{
    public static VortexInteropGame? TryGetGame(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);

        if (!game.IsMonsterHunterWorld)
            return null;

        return new(
            "monsterhunterworld",
            game.NexusGameDomain,
            game.SteamAppId,
            game.ModRootRelativePath,
            game.ExecutableRelativePath);
    }

    public static SteamWorkshopInteropSupport GetSteamWorkshopSupport(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);

        if (game.IsMonsterHunterWorld)
        {
            return new(
                false,
                "Monster Hunter: World has no reviewed Steam Workshop contract in this manager. A Steam app ID alone is not evidence that Workshop discovery/acquisition is supported.");
        }

        return new(
            false,
            "Steam Workshop is disabled until this game profile has a reviewed, operation-specific Workshop contract.");
    }

    internal static VortexInteropGame RequireGame(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return TryGetGame(game)
            ?? throw new NotSupportedException(
                $"Vortex handoff is not configured for game profile '{game.Id}'.");
    }
}

/// <summary>
/// Imports and exports a small, documented Vortex handoff manifest. The manifest carries identity,
/// desired enabled/priority state, and optional content hashes only. It never carries archives,
/// credentials, cookies, signed URLs, or instructions to mutate the live game tree.
/// </summary>
public sealed class VortexInteropService(ManagerDatabase db, GameProfile game)
{
    private const int CurrentFormat = 1;
    private const long MaxManifestBytes = 32L * 1024 * 1024;
    private const int MaxMods = 10_000;
    private static readonly JsonSerializerOptions StrictJson = new(AutomationJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<string> ExportAsync(string destination, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var vortexGame = VortexInteropContract.RequireGame(game);
        var snapshot = await db.LoadPlannerSnapshotAsync(ct);
        var files = snapshot.Files.ToLookup(file => file.ModId, StringComparer.OrdinalIgnoreCase);

        var mods = snapshot.Mods.Select(mod => new VortexInteropMod(
            mod.Id,
            mod.DisplayName,
            mod.Enabled,
            mod.Priority,
            mod.NexusModId,
            mod.NexusFileId,
            mod.NexusVersion,
            files[mod.Id].ToDictionary(
                file => file.Path,
                file => file.BlobSha256,
                PathRules.Comparer))).ToArray();

        var manifest = new VortexInteropManifest(
            CurrentFormat,
            DateTimeOffset.UtcNow,
            vortexGame,
            mods);

        var fullPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(
            fullPath,
            JsonSerializer.Serialize(manifest, AutomationJson.Options),
            ct);
        return fullPath;
    }

    public async Task<VortexInteropPreview> PreviewAsync(string source, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var manifest = await ReadAndValidateAsync(source, ct);
        var snapshot = await db.LoadPlannerSnapshotAsync(ct);
        var files = snapshot.Files.ToLookup(file => file.ModId, StringComparer.OrdinalIgnoreCase);
        var matches = new List<VortexInteropMatch>(manifest.Mods.Count);

        foreach (var entry in manifest.Mods)
        {
            ct.ThrowIfCancellationRequested();

            var local = snapshot.Mods.FirstOrDefault(mod =>
                StringComparer.OrdinalIgnoreCase.Equals(mod.Id, entry.LocalModId));
            var kind = VortexInteropMatchKind.FoundLocally;

            if (local is null
                && !string.IsNullOrWhiteSpace(entry.NexusModId)
                && !string.IsNullOrWhiteSpace(entry.NexusFileId))
            {
                var candidates = snapshot.Mods.Where(mod =>
                    StringComparer.Ordinal.Equals(mod.NexusModId, entry.NexusModId)
                    && StringComparer.Ordinal.Equals(mod.NexusFileId, entry.NexusFileId)).ToArray();

                if (candidates.Length > 1)
                {
                    matches.Add(new(
                        entry,
                        VortexInteropMatchKind.Ambiguous,
                        null,
                        "Several local packages share this exact Nexus mod/file identity; review them manually.",
                        false));
                    continue;
                }

                local = candidates.SingleOrDefault();
                kind = VortexInteropMatchKind.FoundThroughNexusIdentity;
            }

            if (local is null)
            {
                matches.Add(new(
                    entry,
                    VortexInteropMatchKind.Missing,
                    null,
                    "The package is not present locally. Obtain the archive from its original provider, import it normally, then preview the handoff again.",
                    false));
                continue;
            }

            var actual = files[local.Id].ToDictionary(
                file => file.Path,
                file => file.BlobSha256,
                PathRules.Comparer);
            if (entry.Files is { Count: > 0 }
                && (actual.Count != entry.Files.Count
                    || entry.Files.Any(file =>
                        !actual.TryGetValue(PathRules.Normalize(file.Key), out var hash)
                        || !StringComparer.OrdinalIgnoreCase.Equals(hash, file.Value))))
            {
                matches.Add(new(
                    entry,
                    VortexInteropMatchKind.HashMismatch,
                    local.Id,
                    "The local package payload does not match the hashes carried by the handoff.",
                    false));
                continue;
            }

            matches.Add(new(
                entry,
                kind,
                local.Id,
                entry.Files is { Count: > 0 }
                    ? "Exact local payload hashes match the handoff."
                    : "Identity matched, but the handoff did not carry payload hashes.",
                true));
        }

        var duplicateTargets = matches
            .Where(match => match.CanRestore && match.LocalModId is not null)
            .GroupBy(match => match.LocalModId!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var safeMatches = matches.Select(match =>
            match.LocalModId is not null && duplicateTargets.Contains(match.LocalModId)
                ? match with
                {
                    Kind = VortexInteropMatchKind.Ambiguous,
                    CanRestore = false,
                    Detail = "Multiple handoff entries resolve to the same local package."
                }
                : match).ToArray();

        return new(manifest, safeMatches);
    }

    public async Task<string> ImportAsProfileAsync(
        string source,
        string profileName,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        // Re-read and re-match at import time so a stale preview cannot enable changed content.
        var preview = await PreviewAsync(source, ct);
        var state = (await db.GetModsAsync(ct)).ToDictionary(
            mod => mod.Id,
            mod => (enabled: false, priority: mod.Priority),
            StringComparer.OrdinalIgnoreCase);

        foreach (var match in preview.Matches.Where(match =>
                     match.CanRestore && match.LocalModId is not null))
        {
            state[match.LocalModId!] = (match.Entry.Enabled, match.Entry.Priority);
        }

        return await new ProfileRepository(db).SaveAsync(profileName.Trim(), state, ct: ct);
    }

    private async Task<VortexInteropManifest> ReadAndValidateAsync(
        string source,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var info = new FileInfo(source);
        if (!info.Exists)
            throw new FileNotFoundException("Vortex handoff file was not found.", source);
        if (info.Length > MaxManifestBytes)
            throw new InvalidDataException("Vortex handoff exceeds the 32 MB limit.");

        await using var stream = File.OpenRead(source);
        var manifest = await JsonSerializer.DeserializeAsync<VortexInteropManifest>(
            stream,
            StrictJson,
            ct) ?? throw new InvalidDataException("Vortex handoff is empty.");

        if (manifest.Format != CurrentFormat
            || manifest.Game is null
            || manifest.Mods is null
            || manifest.Mods.Count > MaxMods)
        {
            throw new InvalidDataException("Unsupported or invalid Vortex handoff format.");
        }

        var expected = VortexInteropContract.RequireGame(game);
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                manifest.Game.VortexGameId,
                expected.VortexGameId)
            || !StringComparer.OrdinalIgnoreCase.Equals(
                manifest.Game.NexusPageId ?? string.Empty,
                expected.NexusPageId ?? string.Empty)
            || !StringComparer.Ordinal.Equals(
                manifest.Game.SteamAppId ?? string.Empty,
                expected.SteamAppId ?? string.Empty)
            || !PathRules.Comparer.Equals(
                GameProfile.NormalizeRelative(manifest.Game.ModPath, allowEmpty: true),
                GameProfile.NormalizeRelative(expected.ModPath, allowEmpty: true))
            || !StringComparer.OrdinalIgnoreCase.Equals(
                GameProfile.NormalizeRelative(manifest.Game.Executable, allowEmpty: false),
                GameProfile.NormalizeRelative(expected.Executable, allowEmpty: false)))
        {
            throw new InvalidDataException(
                "The Vortex handoff belongs to a different or unsupported game contract.");
        }

        if (manifest.Mods.Any(mod =>
                mod is null
                || string.IsNullOrWhiteSpace(mod.LocalModId)
                || string.IsNullOrWhiteSpace(mod.DisplayName))
            || manifest.Mods.Select(mod => mod.LocalModId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Mods.Count)
        {
            throw new InvalidDataException(
                "Vortex handoff contains empty or duplicate mod identities.");
        }

        foreach (var mod in manifest.Mods)
        {
            ct.ThrowIfCancellationRequested();
            if (mod.Priority < 0)
                throw new InvalidDataException("Vortex handoff contains an invalid priority.");

            if (mod.Files is null)
                continue;

            foreach (var file in mod.Files)
            {
                try
                {
                    _ = PathRules.Normalize(file.Key);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidDataException(
                        "Vortex handoff contains an unsafe managed path.",
                        ex);
                }

                if (string.IsNullOrWhiteSpace(file.Value)
                    || file.Value.Length != 64
                    || !file.Value.All(Uri.IsHexDigit))
                {
                    throw new InvalidDataException(
                        "Vortex handoff contains an invalid SHA-256.");
                }
            }
        }

        return manifest;
    }


}
