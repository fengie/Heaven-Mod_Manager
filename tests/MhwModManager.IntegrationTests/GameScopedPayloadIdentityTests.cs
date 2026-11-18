using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// A declared game is a routing hint, not permission to disregard contradictory
/// payload directories for other registered games in mixed-source mod packs.
/// </summary>
public sealed class GameScopedPayloadIdentityTests
{
    private static IReadOnlyList<GameProfile> RegisteredGames =>
    [
        GameProfile.MonsterHunterWorld(Path.Combine(Path.GetTempPath(), "mhw-routing-evidence")),
        GameProfile.Generic("nier-automata", "NieR:Automata",
            Path.Combine(Path.GetTempPath(), "nier-routing-evidence"), "NieRAutomata.exe", "mods"),
        GameProfile.Generic("another-unity-game", "Another Unity Game",
            Path.Combine(Path.GetTempPath(), "unity-routing-evidence"), "UnityGame.exe", "BepInEx/plugins"),
        GameProfile.Generic("stardew-valley", "Stardew Valley",
            Path.Combine(Path.GetTempPath(), "stardew-routing-evidence"), "Stardew Valley.exe", "Mods")
    ];

    [Fact]
    public void Declared_game_cannot_override_another_registered_game_payload_scope()
    {
        var detection = GameModIntakeClassifier.Classify(RegisteredGames, "download.zip",
            ["mods/another-unity-game/BepInEx/plugins/realplugin.dll"], "nier-automata");

        Assert.Equal(GameModIntakeKind.Ambiguous, detection.Kind);
        Assert.Null(detection.GameId);
        Assert.Contains("nier-automata", detection.CandidateGameIds);
        Assert.Contains("another-unity-game", detection.CandidateGameIds);
    }

    [Fact]
    public void A_conflicting_game_scope_after_many_unrelated_files_is_still_considered()
    {
        var entries = Enumerable.Range(0, 2048)
            .Select(i => $"resources/common-{i:D4}.dds").ToList();
        entries.Add("Archive/Mods/STARDew-Valley/Content/critical.dll");

        var detection = GameModIntakeClassifier.Classify(RegisteredGames, "unnamed.rar",
            entries, "nier-automata");

        Assert.Equal(GameModIntakeKind.Ambiguous, detection.Kind);
        Assert.Null(detection.GameId);
        Assert.Contains("stardew-valley", detection.CandidateGameIds);
    }

    [Fact]
    public void Multiple_distinct_game_scopes_are_all_reported_without_picking_a_winner()
    {
        var detection = GameModIntakeClassifier.Classify(RegisteredGames, "download.zip",
            ["Mods/another-unity-game/main.dll",
             "Games/stardew-valley/Mods/flowers.dll"], "nier-automata");

        Assert.Equal(GameModIntakeKind.Ambiguous, detection.Kind);
        Assert.Equal(3, detection.CandidateGameIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Docs_and_archive_titles_mentioning_other_games_are_not_payload_scope_proof()
    {
        var detection = GameModIntakeClassifier.Classify(RegisteredGames,
            "another-unity-game-notes.zip",
            ["mods/outfit.dds", "docs/another-unity-game-readme.txt",
             "screenshots/stardew-valley-preview.png"], "nier-automata");

        Assert.Equal(GameModIntakeKind.Identified, detection.Kind);
        Assert.Equal("nier-automata", detection.GameId);
    }

    [Fact]
    public void Unregistered_declared_game_remains_unknown_even_if_scope_matches_other_games()
    {
        var detection = GameModIntakeClassifier.Classify(RegisteredGames, "download.zip",
            ["mods/another-unity-game/plugin.dll"], "unregistered-game");

        Assert.Equal(GameModIntakeKind.Unknown, detection.Kind);
        Assert.Null(detection.GameId);
    }
}
