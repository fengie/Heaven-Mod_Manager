using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class LocalModInventoryTests
{
    [Fact]
    public void Current_previous_unmatched_and_workshop_roots_stay_separate()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var external = Path.Combine(temp.FullName, "external");
            var old = Path.Combine(temp.FullName, "internal");
            var current = Path.Combine(external, "steamapps", "common", "Game");
            var stale = Path.Combine(old, "steamapps", "common", "Game");
            var standalone = Path.Combine(temp.FullName, "standalone");
            var prior = Path.Combine(temp.FullName, "old-standalone");
            var unknown = Path.Combine(old, "steamapps", "common", "Another");
            var workshop = Path.Combine(old, "steamapps", "workshop", "content", "123", "456");
            static string Write(string root, string path)
            {
                var file = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "fixture");
                return Path.GetFullPath(file);
            }
            var currentMod = Write(current, "Project/Content/Paks/~mods/Active.pak");
            var stock = Write(current, "Project/Content/Paks/pakchunk0.pak");
            var staleMod = Write(stale, "outfits/example.pak");
            Write(stale, "outfits/example.utoc");
            Write(stale, "outfits/example.ucas");
            var runtime = Write(current, "Engine/Binaries/ThirdParty/CEF3/resources.pak");
            var plugin = Write(standalone, "BepInEx/plugins/Example/plugin.dll");
            var receipt = Write(standalone, "DLSS-Nexus-Backups/one/installation.json");
            File.WriteAllText(receipt, """{"product":"DLSS Nexus","status":"Complete","files":[{}]}""");
            var bad = Write(standalone, "DLSS-Nexus-Backups/two/installation.json");
            var enabler = Write(stale, "package/project/Binaries/Win64/bitfix/sig.lua");
            Write(stale, "package/project/Binaries/Win64/dsound.dll");
            var priorMod = Write(prior, "Project/Content/Paks/~mods/foo.pak");
            var other = Write(unknown, "reframework/autorun/feature.lua");
            Write(workshop, "mod.pak");
            Write(Path.GetDirectoryName(workshop)!, "777/metadata.json");
            File.WriteAllText(Path.Combine(old, "steamapps", "workshop", "appworkshop_123.acf"),
                """ "AppWorkshop" { "appid" "123" "WorkshopItemsInstalled" { "456" { "manifest" "1" } } "WorkshopItemDetails" { "777" { } } } """);
            var looseLibrary = Path.Combine(temp.FullName, "more games");
            var looseGame = Path.Combine(looseLibrary, "Independent");
            Write(looseGame, "Independent.exe");
            var looseMod = Write(looseGame, "Mods/custom.lua");
            var game = GameProfile.Generic("game", "Game", current, "Game.exe", "Project/Content/Paks", steamAppId: "123");
            var separate = GameProfile.Generic("other", "Other", standalone, "Game.exe", "BepInEx/plugins");
            var result = LocalModInventory.Scan([game, separate], [old, external], [looseLibrary], new Dictionary<string,string[]> { [separate.Id] = [prior] }, TestContext.Current.CancellationToken);
            Assert.Contains(result, x => x.Location == looseMod && x.Game == "Independent");
            Assert.Contains(result, x => x.Location == currentMod && x.Evidence.StartsWith("Registered", StringComparison.Ordinal));
            Assert.DoesNotContain(result, x => x.Location == stock || x.Location == runtime);
            Assert.Contains(result, x => x.Location == staleMod && x.Evidence.Contains("Other library", StringComparison.Ordinal));
            Assert.Contains(result, x => x.Location == receipt && x.Evidence.Contains("receipt", StringComparison.Ordinal));
            Assert.DoesNotContain(result, x => x.Location == bad);
            Assert.Contains(result, x => x.Location == enabler && x.Evidence.Contains("unconfirmed", StringComparison.Ordinal));
            Assert.Contains(result, x => x.Location == priorMod && x.Evidence.Contains("Previous", StringComparison.Ordinal));
            Assert.Contains(result, x => x.Location == plugin && x.Game == "Other");
            Assert.Contains(result, x => x.Location == other && x.Evidence.Contains("Unmatched", StringComparison.Ordinal));
            Assert.Contains(result, x => x.Location == workshop && x.Game == "Game" && x.Name == "456" && x.Evidence.Contains("install record", StringComparison.Ordinal));
            Assert.Contains(result, x => x.Name == "777" && x.Evidence.Contains("unconfirmed", StringComparison.Ordinal));
            Assert.Equal("fixture", File.ReadAllText(staleMod));
        }
        finally { temp.Delete(true); }
    }

    [Fact]
    public void Existing_profile_retains_previous_directory_after_move_and_reopen()
    {
        var home = Directory.CreateTempSubdirectory();
        try
        {
            var registry = new GameProfileRegistry(Path.Combine(home.FullName, "state"));
            var first = Path.Combine(home.FullName, "first");
            var second = Path.Combine(home.FullName, "second");
            var profile = GameProfile.Generic("custom", "Custom", first, "Game.exe", "");
            registry.Upsert(profile);
            registry.Upsert(profile with { GameRoot = second });
            var current = Assert.Single(registry.Load());
            Assert.Equal(first, Assert.Single(registry.ReadInstallHistory()[current.Id]));
            registry.Upsert(current);
            Assert.Equal(first, Assert.Single(new GameProfileRegistry(Path.Combine(home.FullName, "state")).ReadInstallHistory()[current.Id]));
        }
        finally { home.Delete(true); }
    }
}

