using System.Text.Json;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class VortexInteropServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "MhwVortexInteropTests-" + Guid.NewGuid().ToString("N"));

    public VortexInteropServiceTests() => Directory.CreateDirectory(root);

    [Fact]
    public void MonsterHunterWorldContractMatchesSupportedVortexIdentityWithoutInventingWorkshop()
    {
        var game = GameProfile.MonsterHunterWorld(Path.Combine(root, "game"));

        var vortex = Assert.IsType<VortexInteropGame>(VortexInteropContract.TryGetGame(game));
        Assert.Equal("monsterhunterworld", vortex.VortexGameId);
        Assert.Equal("monsterhunterworld", vortex.NexusPageId);
        Assert.Equal("582010", vortex.SteamAppId);
        Assert.Equal("nativePC", vortex.ModPath);
        Assert.Equal("MonsterHunterWorld.exe", vortex.Executable);

        var workshop = VortexInteropContract.GetSteamWorkshopSupport(game);
        Assert.False(workshop.Supported);
        Assert.Contains("no reviewed Steam Workshop contract", workshop.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportPreviewAndImportAsProfileRoundTripLocalIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(root, "roundtrip.db"));
        await db.InitializeAsync(ct);
        await db.UpsertModAsync(new ModDescriptor(
            "local-a",
            "Local A",
            "Local A",
            Path.Combine(root, "mods", "a"),
            true,
            17,
            SourceUrl: "https://www.nexusmods.com/monsterhunterworld/mods/123",
            NexusModId: "123",
            NexusFileId: "456",
            NexusVersion: "1.2.3"), ct);

        var service = new VortexInteropService(
            db,
            GameProfile.MonsterHunterWorld(Path.Combine(root, "game")));
        var handoff = Path.Combine(root, "roundtrip.vortexhandoff.json");

        await service.ExportAsync(handoff, ct);
        var preview = await service.PreviewAsync(handoff, ct);

        var match = Assert.Single(preview.Matches);
        Assert.Equal(VortexInteropMatchKind.FoundLocally, match.Kind);
        Assert.True(match.CanRestore);
        Assert.Equal("local-a", match.LocalModId);

        var profileId = await service.ImportAsProfileAsync(handoff, "Vortex handoff", ct);
        var state = await new ProfileRepository(db).LoadAsync(profileId, ct);
        Assert.Equal((true, 17), state["local-a"]);
    }

    [Fact]
    public async Task PreviewRejectsTraversalBeforeMatching()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(root, "traversal.db"));
        await db.InitializeAsync(ct);
        var service = new VortexInteropService(
            db,
            GameProfile.MonsterHunterWorld(Path.Combine(root, "game")));

        var handoff = new VortexInteropManifest(
            1,
            DateTimeOffset.UtcNow,
            new("monsterhunterworld", "monsterhunterworld", "582010", "nativePC", "MonsterHunterWorld.exe"),
            [
                new(
                    "evil",
                    "Traversal",
                    true,
                    1,
                    Files: new Dictionary<string, string>
                    {
                        ["..\\outside.bin"] = new string('A', 64)
                    })
            ]);
        var path = Path.Combine(root, "traversal.vortexhandoff.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(handoff), ct);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.PreviewAsync(path, ct));
    }

    [Fact]
    public async Task ExportDoesNotCarryCredentialBearingSourceUrls()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(root, "credential-export.db"));
        await db.InitializeAsync(ct);
        await db.UpsertModAsync(new ModDescriptor(
            "local-secret",
            "Local Secret",
            "Local Secret",
            Path.Combine(root, "mods", "secret"),
            true,
            1,
            SourceUrl: "https://example.invalid/mod?token=CANARY_VORTEX_SECRET"), ct);

        var service = new VortexInteropService(
            db,
            GameProfile.MonsterHunterWorld(Path.Combine(root, "game")));
        var path = Path.Combine(root, "credential-export.vortexhandoff.json");

        await service.ExportAsync(path, ct);
        var json = await File.ReadAllTextAsync(path, ct);

        Assert.DoesNotContain("CANARY_VORTEX_SECRET", json, StringComparison.Ordinal);
        Assert.DoesNotContain("?token=", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceUrl", json, StringComparison.OrdinalIgnoreCase);
        var exported = JsonSerializer.Deserialize<VortexInteropManifest>(json);
        Assert.NotNull(exported);
        Assert.Single(exported!.Mods);
    }

    [Fact]
    public async Task PreviewRejectsCredentialUrlFieldsAndUnknownFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(root, "credential-import.db"));
        await db.InitializeAsync(ct);
        var service = new VortexInteropService(
            db,
            GameProfile.MonsterHunterWorld(Path.Combine(root, "game")));

        var handoff = new VortexInteropManifest(
            1,
            DateTimeOffset.UtcNow,
            new("monsterhunterworld", "monsterhunterworld", "582010", "nativePC", "MonsterHunterWorld.exe"),
            [
                new(
                    "credential-bearing",
                    "Credential bearing",
                    true,
                    1)
            ]);
        var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var validJson = JsonSerializer.Serialize(handoff, webJson);
        var credentialJson = validJson.Replace(
            "\"priority\":1",
            "\"priority\":1,\"sourceUrl\":\"https://example.invalid/mod?token=CANARY_VORTEX_IMPORT\"",
            StringComparison.Ordinal);
        Assert.NotEqual(validJson, credentialJson);
        var credentialPath = Path.Combine(root, "credential-import.vortexhandoff.json");
        await File.WriteAllTextAsync(credentialPath, credentialJson, ct);

        await Assert.ThrowsAsync<JsonException>(
            () => service.PreviewAsync(credentialPath, ct));

        var unknownFieldPath = Path.Combine(root, "unknown-field.vortexhandoff.json");
        await File.WriteAllTextAsync(
            unknownFieldPath,
            validJson.Insert(1, "\"futureSecret\":\"CANARY_VORTEX_UNKNOWN\","),
            ct);

        await Assert.ThrowsAsync<JsonException>(
            () => service.PreviewAsync(unknownFieldPath, ct));
    }

    [Fact]
    public async Task PreviewRejectsDifferentVortexGameContract()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(root, "wrong-game.db"));
        await db.InitializeAsync(ct);
        var service = new VortexInteropService(
            db,
            GameProfile.MonsterHunterWorld(Path.Combine(root, "game")));

        var handoff = new VortexInteropManifest(
            1,
            DateTimeOffset.UtcNow,
            new("some-other-game", "monsterhunterworld", "582010", "nativePC", "MonsterHunterWorld.exe"),
            []);
        var path = Path.Combine(root, "wrong-game.vortexhandoff.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(handoff), ct);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.PreviewAsync(path, ct));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        GC.SuppressFinalize(this);
    }
}
