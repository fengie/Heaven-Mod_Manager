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
                    games.FirstOrDefault(game => game.IsMonsterHunterWorld) is { } registeredMhw
                        ? [declared.Id, registeredMhw.Id] : [declared.Id]);
            if (hasStellarBladeSignature && !IsStellarBladeProfile(declared))
            {
                var stellarProfile = games.FirstOrDefault(IsStellarBladeProfile);
                return new(GameModIntakeKind.Ambiguous, null,
                    "Declared game conflicts with Stellar Blade package files.",
                    stellarProfile is null ? [declared.Id] : [declared.Id, stellarProfile.Id]);
            }
            // Game-root labels do not prove installation compatibility, but an
            // explicitly game-scoped payload for *another* registered game is
            // contradictory evidence. Require review rather than trusting the
            // package's unverified manifest over that concrete layout.
            var scopedConflicts = FindContradictoryGameScopes(games, declared, relativePaths);
            if (scopedConflicts.Length > 0)
                return new(GameModIntakeKind.Ambiguous, null,
                    "Declared game conflicts with another registered game's scoped payload directory.",
                    new[] { declared.Id }.Concat(scopedConflicts).ToArray());

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

    /// <summary>
    /// Negative evidence only: a distinct registered game's folder below a
    /// known collection wrapper (or at the package root) contradicts a declared
    /// game. It never positively identifies a game or authorizes installation.
    /// Scan all paths once; ignore documentation trees and non-directory names.
    /// </summary>
    private static string[] FindContradictoryGameScopes(
        GameProfile[] games, GameProfile declared, IReadOnlyList<string> relativePaths)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var gameIdsByAlias = new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(game.Id, declared.Id)) continue;
            foreach (var alias in new[] { game.Id, game.DisplayName, game.NexusGameDomain, game.SteamAppId })
            {
                if (string.IsNullOrWhiteSpace(alias)) continue;
                var normalized = GameProfile.NormalizeId(alias);
                if (normalized.Length < 4) continue;
                if (!gameIdsByAlias.TryGetValue(normalized, out var ids))
                    gameIdsByAlias[normalized] = ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ids.Add(game.Id);
            }
        }
        if (gameIdsByAlias.Count == 0) return [];

        var conflicting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in relativePaths)
        {
            var segments = path.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2 || IsDocumentationFolder(segments[0])) continue;

            var first = GameProfile.NormalizeId(segments[0]);
            if (gameIdsByAlias.TryGetValue(first, out var rootIds))
                conflicting.UnionWith(rootIds);
            for (var i = 0; i + 2 < segments.Length; i++)
            {
                if (!IsCollectionWrapper(segments[i])) continue;
                var scoped = GameProfile.NormalizeId(segments[i + 1]);
                if (gameIdsByAlias.TryGetValue(scoped, out var ids))
                    conflicting.UnionWith(ids);
            }
        }
        return conflicting.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsDocumentationFolder(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return name.Equals("docs", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("documentation", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("screenshots", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("images", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("readme", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("licenses", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCollectionWrapper(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return name.Equals("mods", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("games", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("game", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("modpacks", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("mod-packs", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("downloads", StringComparison.OrdinalIgnoreCase);
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
