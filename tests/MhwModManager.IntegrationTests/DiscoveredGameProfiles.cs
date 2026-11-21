using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.IntegrationTests;

internal static class DiscoveredGameProfiles
{
    public static IReadOnlyList<GameProfile> Load()
    {
        var registry = Environment.GetEnvironmentVariable("MHW_DISCOVERED_GAME_REGISTRY");
        if (string.IsNullOrWhiteSpace(registry))
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName,"MhwModManager.sln")))
                directory = directory.Parent;
            registry = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Manager checkout required."),
                "tests","fixtures","discovered-games");
        }
        var path = Path.Combine(registry,"games.json");
        using var inventory = JsonDocument.Parse(File.ReadAllBytes(path));
        var expected = inventory.RootElement.GetArrayLength();
        var profiles = new GameProfileRegistry(registry).Load();
        if (expected == 0 || profiles.Count != expected)
            throw new InvalidDataException($"Registry contained {expected} entries. Manager accepted {profiles.Count}. Check missing, duplicate or unsafe profiles.");
        return profiles;
    }

    public static GameProfile Isolate(GameProfile original, string workspace)
    {
        var root = Path.Combine(Path.GetFullPath(workspace),"games",original.Id);
        return original with { GameRoot = root,
            SavePath = string.IsNullOrWhiteSpace(original.SavePath) ? null : Path.Combine(root,"save-fixture.dat") };
    }
}
