using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class GameDeploymentAdmissionIntegrationTests
{
    [Fact]
    public void Fresh_unknown_executable_stages_only_while_legacy_explicit_game_root_is_preserved()
    {
        var home = Directory.CreateTempSubdirectory("umm-root-admission-");
        try
        {
            var root = Path.Combine(home.FullName, "game");
            Directory.CreateDirectory(root);
            var exe = Path.Combine(root, "UnknownGame.exe");
            File.WriteAllBytes(exe, [0x4d, 0x5a]);
            var registry = new GameProfileRegistry(Path.Combine(home.FullName, "state"));

            var fresh = registry.AddGenericFromExecutable(exe, activate: false);
            Assert.Equal("inferred-unverified-root", fresh.AdapterId);
            Assert.Equal(string.Empty, fresh.ModRootRelativePath);
            var admission = GameAdapters.DirectFileAdmission(fresh);
            Assert.False(admission.Allowed);
            Assert.Equal("unverified-game-root-target", admission.Code);
            Assert.Contains(GameAdapters.Resolve(fresh).Validate(fresh), x => x.Blocking
                && x.Code == "unverified-game-root-target");

            var configured = fresh with { ModRootRelativePath = "Mods" };
            Assert.True(GameAdapters.DirectFileAdmission(configured).Allowed);
            Assert.DoesNotContain(GameAdapters.Resolve(configured).Validate(configured),
                x => x.Code == "unverified-game-root-target");

            var legacy = GameProfile.Generic("old-unknown", "Old Unknown", root,
                "UnknownGame.exe", string.Empty, adapterId: "generic-root");
            var legacyAdmission = GameAdapters.DirectFileAdmission(legacy);
            Assert.True(legacyAdmission.Allowed);
            Assert.True(File.Exists(exe));
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Unknown_discovered_game_can_keep_staged_sources_but_cannot_capture_live_files()
    {
        var ct = TestContext.Current.CancellationToken;
        var home = Directory.CreateTempSubdirectory("umm-admission-");
        try
        {
            var gameRoot = Path.Combine(home.FullName, "UnknownGame");
            var source = Directory.CreateDirectory(Path.Combine(home.FullName, "incoming", "UnknownMod")).FullName;
            var file = Path.Combine(source, "payload.bin");
            await File.WriteAllTextAsync(file, "fixture", ct);
            var game = GameProfile.Generic("unknown", "Unknown", gameRoot, "Unknown.exe",
                "", adapterId: "inferred-unverified-root");
            var db = new ManagerDatabase(Path.Combine(home.FullName, "db", "manager.db"));
            await db.InitializeAsync(ct);
            var mod = new ModDescriptor("unknown-mod", "Unknown", "Unknown", source, false, 0);
            await db.UpsertModAsync(mod, ct);
            var scanner = new ModScanner(db, new BlobStore(Path.Combine(home.FullName, "blobs"), db),
                new HashingService(), game);
            var catalog = new CatalogService(db, scanner, Path.Combine(home.FullName, "managed"));

            await catalog.EnsureCapturedAsync([mod], ct);
            var rejected = await Assert.ThrowsAsync<InvalidDataException>(
                () => scanner.CaptureAsync(mod, ct));

            Assert.Contains("mod root", rejected.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("fixture", await File.ReadAllTextAsync(file, ct));
            Assert.False(Directory.Exists(gameRoot));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            home.Delete(recursive: true);
        }
    }
}
