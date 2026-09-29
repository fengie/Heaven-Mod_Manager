using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public enum RecipeMatchKind { FoundLocally, FoundThroughNexusIdentity, WrongVersion, Missing, HashMismatch, ReplacementAvailable, Ambiguous }
public sealed record RecipeMod(string Id, string DisplayName, bool Enabled, int Priority, string? FamilyId = null,
    string? Category = null, string? SourceUrl = null, string? NexusModId = null, string? NexusFileId = null,
    string? NexusModUuid = null, string? NexusVersionId = null, string? NexusPreviousVersionId = null,
    string? NexusCategory = null, string? NexusVersion = null, DateTimeOffset? NexusUploadedAt = null,
    bool IsSuperseded = false, string? SupersededByModId = null, IReadOnlyDictionary<string, string>? Files = null, string? FamilyRole = null);
public sealed record CollectionRecipe(int Format, DateTimeOffset ExportedAt, IReadOnlyList<RecipeMod> Mods,
    string? GameId = null, string? NexusDomain = null, GameBuildFingerprint? GameBuild = null);
public sealed record RecipeMatch(RecipeMod Entry, RecipeMatchKind Kind, string? LocalModId, string Detail, bool CanRestore);
public sealed record RecipeImportPreview(CollectionRecipe Recipe, IReadOnlyList<RecipeMatch> Matches);

public sealed class CollectionRecipeService(ManagerDatabase db, GameProfile? game = null)
{
    public async Task<string> ExportAsync(string destination, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var snapshot = await db.LoadPlannerSnapshotAsync(ct);
        var files = snapshot.Files.ToLookup(f => f.ModId, StringComparer.OrdinalIgnoreCase);
        var recipe = new CollectionRecipe(2, DateTimeOffset.UtcNow, snapshot.Mods.Select(m => new RecipeMod(
            m.Id, m.DisplayName, m.Enabled, m.Priority, m.FamilyId, m.Category, m.SourceUrl,
            m.NexusModId, m.NexusFileId, m.NexusModUuid, m.NexusVersionId, m.NexusPreviousVersionId,
            m.NexusCategory.ToString(), m.NexusVersion, m.NexusUploadedAt, m.IsSuperseded, m.SupersededByModId,
            files[m.Id].ToDictionary(f => f.Path, f => f.BlobSha256, PathRules.Comparer), m.FamilyRole)).ToArray(), game?.Id, game?.NexusGameDomain, await db.GetGameBuildFingerprintAsync(ct));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(recipe, AutomationJson.Options), ct);
        return destination;
    }

    public async Task<RecipeImportPreview> PreviewAsync(string source, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (new FileInfo(source).Length > 32 * 1024 * 1024) throw new InvalidDataException("Recipe exceeds the 32 MB limit.");
        await using var stream = File.OpenRead(source);
        var recipe = await JsonSerializer.DeserializeAsync<CollectionRecipe>(stream, AutomationJson.Options, ct)
            ?? throw new InvalidDataException("Recipe is empty.");
        if (recipe.Format is not (1 or 2) || recipe.Mods is null || recipe.Mods.Count > 10000) throw new InvalidDataException("Unsupported or invalid recipe format.");
        if (recipe.Mods.Any(m => m is null || string.IsNullOrWhiteSpace(m.Id)) || recipe.Mods.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != recipe.Mods.Count)
            throw new InvalidDataException("Recipe contains empty or duplicate mod IDs.");
        if (game is not null && recipe.GameId is not null && !StringComparer.OrdinalIgnoreCase.Equals(recipe.GameId, game.Id))
            throw new InvalidDataException("This recipe belongs to a different game workspace.");
        if (game is not null && recipe.NexusDomain is not null && !StringComparer.OrdinalIgnoreCase.Equals(recipe.NexusDomain, game.NexusGameDomain))
            throw new InvalidDataException("The recipe Nexus domain does not match this game.");
        var snapshot = await db.LoadPlannerSnapshotAsync(ct);
        var files = snapshot.Files.ToLookup(f => f.ModId, StringComparer.OrdinalIgnoreCase);
        var matches = new List<RecipeMatch>();
        foreach (var entry in recipe.Mods)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Files is not null)
                foreach (var file in entry.Files)
                {
                    _ = PathRules.Normalize(file.Key);
                    if (file.Value is null || file.Value.Length != 64 || !file.Value.All(Uri.IsHexDigit)) throw new InvalidDataException("Recipe contains an invalid SHA-256.");
                }
            var local = snapshot.Mods.FirstOrDefault(m => StringComparer.OrdinalIgnoreCase.Equals(m.Id, entry.Id));
            var kind = RecipeMatchKind.FoundLocally;
            if (local is null)
            {
                var candidates = snapshot.Mods.Where(m => !string.IsNullOrWhiteSpace(entry.NexusModId) &&
                    StringComparer.Ordinal.Equals(m.NexusModId, entry.NexusModId) && !string.IsNullOrWhiteSpace(entry.NexusFileId) &&
                    StringComparer.Ordinal.Equals(m.NexusFileId, entry.NexusFileId)).ToArray();
                if (candidates.Length > 1) { matches.Add(new(entry, RecipeMatchKind.Ambiguous, null, "Several local packages share this Nexus identity; select the intended package manually.", false)); continue; }
                local = candidates.SingleOrDefault(); kind = RecipeMatchKind.FoundThroughNexusIdentity;
            }
            if (local is null)
            {
                var related = snapshot.Mods.Any(m => !string.IsNullOrWhiteSpace(entry.NexusModId) && StringComparer.Ordinal.Equals(m.NexusModId, entry.NexusModId));
                matches.Add(new(entry, related ? RecipeMatchKind.WrongVersion : RecipeMatchKind.Missing, null,
                    related ? "The Nexus mod exists locally, but this file/version is missing." : "Import the original archive from its author, then preview again.", false)); continue;
            }
            if (local.IsSuperseded)
            {
                matches.Add(new(entry, RecipeMatchKind.ReplacementAvailable, local.Id, "A replacement is recorded: " + local.SupersededByModId + ". Review it in Updates.", false)); continue;
            }
            if ((!string.IsNullOrWhiteSpace(entry.NexusVersion) && !StringComparer.Ordinal.Equals(entry.NexusVersion, local.NexusVersion)) ||
                (!string.IsNullOrWhiteSpace(entry.NexusFileId) && !StringComparer.Ordinal.Equals(entry.NexusFileId, local.NexusFileId)))
            {
                matches.Add(new(entry, RecipeMatchKind.WrongVersion, local.Id, "Local version or Nexus file identity differs.", false)); continue;
            }
            var actual = files[local.Id].ToDictionary(f => f.Path, f => f.BlobSha256, PathRules.Comparer);
            if (entry.Files is { Count: > 0 } && (actual.Count != entry.Files.Count || entry.Files.Any(f => !actual.TryGetValue(PathRules.Normalize(f.Key), out var hash) || !StringComparer.OrdinalIgnoreCase.Equals(hash, f.Value))))
            {
                matches.Add(new(entry, RecipeMatchKind.HashMismatch, local.Id, "Captured file paths or SHA-256 values differ; rescan or obtain the matching archive.", false)); continue;
            }
            matches.Add(new(entry, kind, local.Id, entry.Files is { Count: > 0 } ? "Captured payload hashes match." : "Legacy or uncaptured recipe: payload identity is unverified.", true));
        }
        var duplicates = matches.Where(m => m.CanRestore).GroupBy(m => m.LocalModId, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(recipe, matches.Select(m => duplicates.Contains(m.LocalModId) ? m with { Kind = RecipeMatchKind.Ambiguous, CanRestore = false, Detail = "Multiple recipe entries map to one package." } : m).ToArray());
    }

    public async Task<string> ImportAsync(string source, string profileName, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // Re-read and re-match at import time; a stale preview cannot enable a replaced package.
        var preview = await PreviewAsync(source, ct);
        var state = (await db.GetModsAsync(ct)).ToDictionary(m => m.Id, m => (enabled: false, priority: m.Priority), StringComparer.OrdinalIgnoreCase);
        foreach (var match in preview.Matches.Where(m => m.CanRestore && m.LocalModId is not null))
            state[match.LocalModId!] = (match.Entry.Enabled && !match.Entry.IsSuperseded, match.Entry.Priority);
        return await new ProfileRepository(db).SaveAsync(profileName, state, ct: ct);
    }
    public async Task<int> RestoreFamiliesAsync(string source, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var preview = await PreviewAsync(source, ct);
        var mods = (await db.GetModsAsync(ct)).ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var groups = preview.Matches.Where(m => m.CanRestore && m.LocalModId is not null && !string.IsNullOrWhiteSpace(m.Entry.FamilyId))
            .GroupBy(m => m.Entry.FamilyId!, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var group in groups)
        {
            var existing = group.Select(m => mods[m.LocalModId!].FamilyId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (existing.Length > 1 || (existing.Length == 1 && existing[0] is not null && group.Any(m => mods[m.LocalModId!].FamilyId != m.Entry.FamilyId)))
                throw new InvalidDataException("A matched group already has local family membership. Review it in Rules before restoring recipe families.");
        }
        return await db.InTransactionAsync(async (c, tx, token) =>
        {
            var restored = 0;
            foreach (var group in groups)
            {
                if (group.All(m => mods[m.LocalModId!].FamilyId is not null)) continue;
                var family = "imported:" + Guid.NewGuid().ToString("N");
                await using (var create = c.CreateCommand())
                {
                    create.Transaction = tx; create.CommandText = "INSERT INTO mod_families(id,name,created_at) VALUES($i,$n,$u)";
                    create.Parameters.AddWithValue("$i", family); create.Parameters.AddWithValue("$n", group.First().Entry.DisplayName); create.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
                    await create.ExecuteNonQueryAsync(token);
                }
                foreach (var match in group)
                {
                    await using var cmd = c.CreateCommand(); cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE mods SET family_id=$f WHERE id=$m AND family_id IS NULL;";
                    cmd.Parameters.AddWithValue("$f", family); cmd.Parameters.AddWithValue("$m", match.LocalModId!);
                    if (await cmd.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Family changed since preview; nothing was restored.");
                    cmd.CommandText = "INSERT INTO mod_family_members(family_id,mod_id,role,choice_group) VALUES($f,$m,$r,NULL)";
                    cmd.Parameters.AddWithValue("$r", match.Entry.FamilyRole is "Main" or "Optional" or "Update" ? match.Entry.FamilyRole : "Main");
                    await cmd.ExecuteNonQueryAsync(token);
                }
                restored++;
            }
            return restored;
        }, ct);
    }

}
