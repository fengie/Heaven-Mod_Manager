using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class GameDeploymentAdmissionIntegrationTests
{
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
                "", adapterId: "generic-root");
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

            Assert.Contains("verified loader", rejected.Message, StringComparison.OrdinalIgnoreCase);
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
