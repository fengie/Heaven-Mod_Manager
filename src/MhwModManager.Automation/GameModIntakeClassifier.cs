using MhwModManager.Core;

namespace MhwModManager.Automation;

/// <summary>
/// Game identity evidence for an untrusted mod package. Unlike AutoCategoryService this
/// never classifies the *type* of mod, and never assumes the active game is the owner.
/// Generic .dll/.pak/.dds files are deliberately insufficient for game assignment.
/// </summary>
public enum GameModIntakeKind { Identified, Ambiguous, Unknown }

public sealed record GameModIntakeDetection(
    GameModIntakeKind Kind, string? GameId, string Reason, IReadOnlyList<string> CandidateGameIds);

public static class GameModIntakeClassifier
{
    public static GameModIntakeDetection Classify(
        IReadOnlyList<GameProfile> registeredGames,
        string sourceName,
        IReadOnlyList<string> relativePaths,
        string? declaredGame = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(registeredGames);
        ArgumentNullException.ThrowIfNull(relativePaths);

        var games = registeredGames.Where(game => GameProfile.IsCanonicalId(game.Id))
            .DistinctBy(game => game.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        if (games.Length == 0)
            return new(GameModIntakeKind.Unknown, null, "No registered game profiles.", []);

        // A declared game from a local/package manifest must resolve to exactly one
        // registered game; an unknown declaration must not silently fall through to a guess.
        if (!string.IsNullOrWhiteSpace(declaredGame))
        {
            var key = GameProfile.NormalizeId(declaredGame);
            var explicitMatches = games.Where(game =>
                game.Id.Equals(key, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(game.NexusGameDomain)
                    && GameProfile.NormalizeId(game.NexusGameDomain).Equals(key, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(game.SteamAppId)
                    && game.SteamAppId.Equals(declaredGame, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (explicitMatches.Length != 1)
                return new(GameModIntakeKind.Unknown, null, "Package game metadata is not unique among registered games.", []);

            var declared = explicitMatches[0];
            // A manifest can be mislabeled. Contradictory strong file signatures must
            // require review instead of overriding the contents or the declaration.
            if (relativePaths.Any(path => HasNativePcSegment(path)) && !declared.IsMonsterHunterWorld
                && games.Any(game => game.IsMonsterHunterWorld))
                return new(GameModIntakeKind.Ambiguous, null,
                    "Declared game conflicts with Monster Hunter: World package files.",
                    [declared.Id, games.First(game => game.IsMonsterHunterWorld).Id]);
            return new(GameModIntakeKind.Identified, declared.Id,
                "Package manifest identifies a registered game.", [declared.Id]);
        }

        var normalizedName = GameProfile.NormalizeId(sourceName);
        var normalizedEntries = relativePaths.Take(512).Select(GameProfile.NormalizeId).ToArray();
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                game.Id,
                GameProfile.NormalizeId(game.DisplayName),
                GameProfile.NormalizeId(Path.GetFileNameWithoutExtension(game.ExecutableRelativePath))
            };
            if (!string.IsNullOrWhiteSpace(game.NexusGameDomain))
                aliases.Add(GameProfile.NormalizeId(game.NexusGameDomain));
            // Distinct, well-known game-family alias; a lone "nier" is accepted only
            // when it identifies exactly one registered game.
            if (game.Id.Equals("nier-automata", StringComparison.OrdinalIgnoreCase)
                && games.Count(other => other.Id.Contains("nier", StringComparison.OrdinalIgnoreCase)) == 1)
                aliases.Add("nier");
            if (game.IsMonsterHunterWorld) aliases.Add("mhw");

            aliases.RemoveWhere(alias => alias.Length < 4 && alias != "mhw");
            var matchedName = aliases.Any(alias => HasTokenSequence(normalizedName, alias));
            var matchedEntries = normalizedEntries.Any(path => aliases.Any(alias => HasTokenSequence(path, alias)));
            var nativePc = game.IsMonsterHunterWorld && relativePaths.Any(HasNativePcSegment);

            var score = nativePc ? 95 : 0;
            if (matchedName || matchedEntries) score = Math.Max(score, 85);
            if (score > 0) scores.Add(game.Id, score);
        }

        if (scores.Count == 0)
            return new(GameModIntakeKind.Unknown, null, "No game-specific identity evidence; generic mod files cannot be routed safely.", []);

        var ordered = scores.OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        if (ordered.Length > 1 && ordered[1].Value >= ordered[0].Value - 15)
            return new(GameModIntakeKind.Ambiguous, null, "Package matches multiple registered games.",
                ordered.Where(pair => pair.Value >= ordered[0].Value - 15).Select(pair => pair.Key).ToArray());

        return new(GameModIntakeKind.Identified, ordered[0].Key,
            "Package name or game-specific internal path matches a registered game.", [ordered[0].Key]);
    }

    private static bool HasNativePcSegment(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.Equals("nativePC", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasTokenSequence(string normalized, string token)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(normalized)) return false;
        return ("-" + normalized + "-").Contains("-" + token + "-", StringComparison.OrdinalIgnoreCase);
    }
}
