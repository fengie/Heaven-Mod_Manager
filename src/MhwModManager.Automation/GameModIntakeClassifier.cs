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

        // The intake inventory is capped at 50,000 entries. Never classify from
        // just the first 512 files: a later conflicting game identity could turn
        // an ambiguous bundle into a wrongly routed package.
        if (relativePaths.Count > 50_000)
            return new(GameModIntakeKind.Unknown, null,
                "Package identity inventory exceeds 50,000 paths; review smaller groups.", []);

        // A familiar archive title must never override a different game's strongly
        // characteristic payload, even if that game is not registered locally.
        var hasNativePcSignature = relativePaths.Any(HasNativePcSegment);
        var hasStellarBladeSignature = relativePaths.Any(HasStellarBladeSignature);
        if (hasNativePcSignature && hasStellarBladeSignature)
            return new(GameModIntakeKind.Ambiguous,null,
                "Package mixes distinct MHW and Stellar Blade layouts; split or review.",[]);

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
            if (hasNativePcSignature && !declared.IsMonsterHunterWorld)
                return new(GameModIntakeKind.Ambiguous, null,
                    "Declared game conflicts with Monster Hunter: World package files.",
                    [declared.Id, games.First(game => game.IsMonsterHunterWorld).Id]);
            if (hasStellarBladeSignature && !IsStellarBladeProfile(declared))
            {
                var stellarProfile = games.FirstOrDefault(IsStellarBladeProfile);
                return new(GameModIntakeKind.Ambiguous, null,
                    "Declared game conflicts with Stellar Blade package files.",
                    stellarProfile is null ? [declared.Id] : [declared.Id, stellarProfile.Id]);
            }
            return new(GameModIntakeKind.Identified, declared.Id,
                "Package manifest identifies a registered game.", [declared.Id]);
        }

        if (hasNativePcSignature && !games.Any(game => game.IsMonsterHunterWorld))
            return new(GameModIntakeKind.Unknown,null,
                "Package has an MHW nativePC layout, but Monster Hunter: World is not registered.",[]);
        if (hasStellarBladeSignature && !games.Any(IsStellarBladeProfile))
            return new(GameModIntakeKind.Unknown,null,
                "Package has a Stellar Blade layout, but Stellar Blade is not registered.",[]);

        var normalizedName = GameProfile.NormalizeId(sourceName);
        var normalizedEntries = relativePaths.Select(GameProfile.NormalizeId).ToArray();
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
            var nativePc = game.IsMonsterHunterWorld && hasNativePcSignature;
            var stellarBlade = IsStellarBladeProfile(game) && hasStellarBladeSignature;

            var score = nativePc || stellarBlade ? 95 : 0;
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

    /// <summary>Only a canonical Stellar Blade deployment tree is distinctive;
    /// a generic .pak, .utoc or .ucas extension never identifies its game.</summary>
    private static bool HasStellarBladeSignature(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 3 < segments.Length; i++)
            if (segments[i].Equals("SB", StringComparison.OrdinalIgnoreCase)
                && segments[i + 1].Equals("Content", StringComparison.OrdinalIgnoreCase)
                && segments[i + 2].Equals("Paks", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool IsStellarBladeProfile(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return game.Id.Equals("stellar-blade", StringComparison.OrdinalIgnoreCase)
            || GameProfile.NormalizeId(game.DisplayName).Equals("stellar-blade", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(game.NexusGameDomain)
                && GameProfile.NormalizeId(game.NexusGameDomain).Equals("stellarblade", StringComparison.OrdinalIgnoreCase));
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
