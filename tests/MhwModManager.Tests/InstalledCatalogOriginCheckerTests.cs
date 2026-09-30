using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class InstalledCatalogOriginCheckerTests
{
    [Fact]
    public async Task Exact_file_identity_and_version_remain_current()
    {
        var game = CreateGame();
        var file = CreateFile("file-1", "1.0.0");
        var provider = CreateProvider(game, [file]);
        var origin = CreateOrigin("file-1", "1.0.0");
        var checker = new InstalledCatalogOriginChecker();

        var result = await checker.CheckAsync(
            provider,
            game,
            origin,
            TestContext.Current.CancellationToken);

        Assert.Equal(InstalledCatalogOriginCheckState.Current, result.State);
        Assert.Equal("file-1", result.ExactFile?.ProviderFileId);
        Assert.Equal(1, provider.GetModCalls);
        Assert.Equal(1, provider.GetFilesCalls);
    }

    [Fact]
    public async Task Missing_exact_file_never_guesses_a_newer_looking_replacement()
    {
        var game = CreateGame();
        var provider = CreateProvider(
            game,
            [
                CreateFile("file-2", "2.0.0"),
                CreateFile("file-3", "3.0.0")
            ]);
        var checker = new InstalledCatalogOriginChecker();

        var result = await checker.CheckAsync(
            provider,
            game,
            CreateOrigin("file-1", "1.0.0"),
            TestContext.Current.CancellationToken);

        Assert.Equal(InstalledCatalogOriginCheckState.SourceFileMissing, result.State);
        Assert.Null(result.ExactFile);
        Assert.Contains("No replacement was guessed", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Same_file_identity_with_changed_version_metadata_requires_review()
    {
        var game = CreateGame();
        var provider = CreateProvider(game, [CreateFile("file-1", "1.0.1")]);
        var checker = new InstalledCatalogOriginChecker();

        var result = await checker.CheckAsync(
            provider,
            game,
            CreateOrigin("file-1", "1.0.0"),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            InstalledCatalogOriginCheckState.ExactFileMetadataChanged,
            result.State);
        Assert.Equal("1.0.1", result.ExactFile?.Version);
    }

    [Fact]
    public async Task Missing_exact_mod_is_reported_without_file_lookup()
    {
        var game = CreateGame();
        var provider = new FixtureProvider
        {
            Mod = null,
            Files = [CreateFile("file-1", "1.0.0")]
        };
        var checker = new InstalledCatalogOriginChecker();

        var result = await checker.CheckAsync(
            provider,
            game,
            CreateOrigin("file-1", "1.0.0"),
            TestContext.Current.CancellationToken);

        Assert.Equal(InstalledCatalogOriginCheckState.SourceModMissing, result.State);
        Assert.Equal(1, provider.GetModCalls);
        Assert.Equal(0, provider.GetFilesCalls);
    }

    [Fact]
    public async Task Provider_without_update_capability_is_not_queried()
    {
        var game = CreateGame();
        var provider = CreateProvider(game, [CreateFile("file-1", "1.0.0")]);
        provider.CapabilitiesValue = CatalogProviderCapabilities.Metadata;
        var checker = new InstalledCatalogOriginChecker();

        var result = await checker.CheckAsync(
            provider,
            game,
            CreateOrigin("file-1", "1.0.0"),
            TestContext.Current.CancellationToken);

        Assert.Equal(InstalledCatalogOriginCheckState.UpdatesUnsupported, result.State);
        Assert.Equal(0, provider.GetModCalls);
        Assert.Equal(0, provider.GetFilesCalls);
    }

    [Fact]
    public async Task Provider_identity_mismatch_fails_before_network()
    {
        var game = CreateGame();
        var provider = CreateProvider(game, [CreateFile("file-1", "1.0.0")]);
        var checker = new InstalledCatalogOriginChecker();
        var origin = CreateOrigin("file-1", "1.0.0") with
        {
            ProviderId = "different-provider"
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            checker.CheckAsync(
                provider,
                game,
                origin,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, provider.GetModCalls);
        Assert.Equal(0, provider.GetFilesCalls);
    }

    [Fact]
    public async Task Provider_returning_mismatched_mod_identity_fails_closed()
    {
        var game = CreateGame();
        var provider = CreateProvider(game, [CreateFile("file-1", "1.0.0")]);
        provider.Mod = provider.Mod! with { ProviderModId = "wrong-mod" };
        var checker = new InstalledCatalogOriginChecker();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            checker.CheckAsync(
                provider,
                game,
                CreateOrigin("file-1", "1.0.0"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Duplicate_provider_file_ids_fail_closed()
    {
        var game = CreateGame();
        var provider = CreateProvider(
            game,
            [
                CreateFile("file-1", "1.0.0"),
                CreateFile("file-1", "1.0.0")
            ]);
        var checker = new InstalledCatalogOriginChecker();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            checker.CheckAsync(
                provider,
                game,
                CreateOrigin("file-1", "1.0.0"),
                TestContext.Current.CancellationToken));
    }

    private static FixtureProvider CreateProvider(
        GameProfile game,
        IReadOnlyList<CatalogModFile> files)
    {
        return new FixtureProvider
        {
            Mod = new CatalogMod(
                CatalogMod.BuildCanonicalId("fixture-provider", "provider-mod-1"),
                "fixture-provider",
                "provider-mod-1",
                game.Id,
                "Fixture Mod",
                "summary",
                "description",
                "author",
                "1.0.0",
                "Utility",
                ["fixture"],
                null,
                [],
                null,
                null,
                null,
                null,
                null,
                [],
                "https://example.test/mods/provider-mod-1",
                files),
            Files = files
        };
    }

    private static CatalogModFile CreateFile(string providerFileId, string? version)
    {
        return new CatalogModFile(
            "fixture-provider",
            "provider-mod-1",
            providerFileId,
            "Main File",
            "fixture.zip",
            CatalogFileCategory.Main,
            version);
    }

    private static InstalledCatalogOrigin CreateOrigin(
        string providerFileId,
        string? installedVersion)
    {
        return new InstalledCatalogOrigin(
            "local-mod-1",
            "fixture-provider",
            "provider-mod-1",
            providerFileId,
            installedVersion,
            new DateTimeOffset(2026, 9, 30, 3, 0, 0, TimeSpan.Zero),
            "https://example.test/mods/provider-mod-1",
            new string('c', 64));
    }

    private static GameProfile CreateGame()
    {
        return GameProfile.Generic(
            "fixture-game",
            "Fixture Game",
            Path.Combine(Path.GetTempPath(), "mhwmm-installed-origin-checker"),
            "FixtureGame.exe",
            "Mods");
    }

    private sealed class FixtureProvider : IModCatalogProvider
    {
        public string ProviderId => "fixture-provider";
        public string DisplayName => "Fixture Provider";
        public CatalogProviderCapabilities Capabilities => CapabilitiesValue;
        public CatalogProviderCapabilities CapabilitiesValue { get; set; } =
            CatalogProviderCapabilities.Metadata
            | CatalogProviderCapabilities.FileList
            | CatalogProviderCapabilities.Updates;
        public CatalogProviderCompliance Compliance =>
            NexusV3CatalogPolicy.Compliance with { ProviderId = ProviderId };

        public CatalogMod? Mod { get; set; }
        public IReadOnlyList<CatalogModFile> Files { get; set; } = [];
        public int GetModCalls { get; private set; }
        public int GetFilesCalls { get; private set; }

        public Task<IReadOnlyList<CatalogGame>> GetGamesAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<CatalogGame> result = [];
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<CatalogMod>> SearchModsAsync(
            CatalogBrowseRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<CatalogMod> result = Mod is null ? [] : [Mod];
            return Task.FromResult(result);
        }

        public Task<CatalogMod?> GetModAsync(
            GameProfile game,
            string providerModId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            GetModCalls++;
            return Task.FromResult(Mod);
        }

        public Task<IReadOnlyList<CatalogModFile>> GetModFilesAsync(
            GameProfile game,
            string providerModId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            GetFilesCalls++;
            return Task.FromResult(Files);
        }

        public Task<CatalogAcquisitionResolution> ResolveAcquisitionAsync(
            CatalogAcquisitionRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(
                new CatalogAcquisitionResolution(
                    CatalogAcquisitionKind.Unavailable,
                    null,
                    null,
                    null,
                    "Fixture provider does not acquire."));
        }

        public Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(
                new CatalogProviderHealth(
                    ProviderId,
                    CatalogProviderState.Connected,
                    "Fixture healthy."));
        }
    }
}
