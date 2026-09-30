using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using MhwModManager.App.ViewModels;
using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UpdaterCoreTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-updater-core-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("dir/../../evil.dll")]
    [InlineData("C:/evil.dll")]
    [InlineData("file.txt:stream")]
    [InlineData("CON")]
    [InlineData("LPT1.txt")]
    public void Unsafe_update_paths_are_rejected(string path)
    {
        Assert.Throws<InvalidDataException>(() => UpdatePathSafety.NormalizeRelativeFilePath(path));
    }

    [Theory]
    [InlineData("Mods/user.mod")]
    [InlineData("State/Next/manager.db")]
    [InlineData("Inbox/drop.zip")]
    [InlineData("Mods Archive/old.zip")]
    [InlineData("Games/game/state.bin")]
    [InlineData("MHW-DEBUG-ALL.log")]
    public void Product_manifest_cannot_claim_user_or_mutable_runtime_data(string path)
    {
        var manifest = new ProductFileManifest(1,
            [new ProductFileEntry(path, 1, new string('A', 64))]);

        Assert.Throws<InvalidDataException>(manifest.Validate);
    }

    [Fact]
    public void Unsupported_update_manifest_schema_is_rejected()
    {
        var manifest = Manifest(buildNumber: 11) with { SchemaVersion = 99 };

        var ex = Assert.Throws<InvalidDataException>(manifest.Validate);

        Assert.Contains("schema", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Oversized_update_manifest_is_rejected()
    {
        var manifest = Manifest(buildNumber: 11) with { ArtifactSize = UpdateProtocol.MaxArtifactBytes + 1 };

        Assert.Throws<InvalidDataException>(manifest.Validate);
    }

    [Fact]
    public async Task Same_or_older_build_is_not_offered()
    {
        var current = Identity(10);
        var releases = ReleaseList((10, Manifest(10)), (9, Manifest(9)));
        using var http = new HttpClient(new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal)
                ? JsonResponse(releases)
                : throw new InvalidOperationException("Manifest asset should not be requested.")));
        var source = new GitHubUpdateSource(http);

        var candidate = await source.FindLatestAsync(current, "fixture-token", TestToken);

        Assert.Null(candidate);
    }

    [Fact]
    public async Task Public_release_feed_is_anonymous_and_uses_release_only_repository()
    {
        var current = Identity(10);
        var m11 = Manifest(11);
        var releases = ReleaseList((11, m11));
        string? releasePath = null;
        var sawAuthorization = false;
        using var http = new HttpClient(new FakeHandler(request =>
        {
            sawAuthorization |= request.Headers.Authorization is not null;
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
            {
                releasePath = request.RequestUri.AbsolutePath;
                return JsonResponse(releases);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/manifest-11", StringComparison.Ordinal))
                return JsonResponse(m11, "application/octet-stream");

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        }));
        var source = new GitHubUpdateSource(http);

        var candidate = await source.FindLatestAsync(
            current,
            token: null,
            ct: TestToken,
            repository: UpdateProtocol.PublicReleaseRepository);

        Assert.NotNull(candidate);
        Assert.Equal(11, candidate.Manifest.BuildNumber);
        Assert.Equal(
            $"/repos/{UpdateProtocol.PublicReleaseRepository}/releases",
            releasePath);
        Assert.False(sawAuthorization);
    }

    [Fact]
    public async Task Highest_newer_build_is_selected_even_if_release_order_is_stale()
    {
        var current = Identity(10);
        var m11 = Manifest(11);
        var m12 = Manifest(12);
        var releases = ReleaseList((11, m11), (9, Manifest(9)), (12, m12));
        using var http = new HttpClient(new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
                return JsonResponse(releases);
            if (request.RequestUri.AbsolutePath.EndsWith("/manifest-12", StringComparison.Ordinal))
                return JsonResponse(m12, "application/octet-stream");
            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        }));
        var source = new GitHubUpdateSource(http);

        var candidate = await source.FindLatestAsync(current, "fixture-token", TestToken);

        Assert.NotNull(candidate);
        Assert.Equal(12, candidate.Manifest.BuildNumber);
        Assert.Equal("sha-12-abcdef", candidate.Manifest.SourceSha);
    }

    [Fact]
    public async Task Release_discovery_skips_mutable_and_prerelease_builds()
    {
        var current = Identity(10);
        var m11 = Manifest(11);
        var releases = JsonSerializer.Serialize(new[]
        {
            ReleaseEntry(13, Manifest(13), immutable: false),
            ReleaseEntry(12, Manifest(12), prerelease: true),
            ReleaseEntry(11, m11)
        });
        using var http = new HttpClient(new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
                return JsonResponse(releases);
            if (request.RequestUri.AbsolutePath.EndsWith("/manifest-11", StringComparison.Ordinal))
                return JsonResponse(m11, "application/octet-stream");
            throw new InvalidOperationException($"Unsafe release asset was requested: {request.RequestUri}");
        }));
        var source = new GitHubUpdateSource(http);

        var candidate = await source.FindLatestAsync(current, "fixture-token", TestToken);

        Assert.NotNull(candidate);
        Assert.Equal(11, candidate.Manifest.BuildNumber);
    }

    [Fact]
    public async Task Only_mutable_or_prerelease_newer_releases_are_not_offered()
    {
        var current = Identity(10);
        var releases = JsonSerializer.Serialize(new[]
        {
            ReleaseEntry(12, Manifest(12), immutable: false),
            ReleaseEntry(11, Manifest(11), prerelease: true)
        });
        using var http = new HttpClient(new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal)
                ? JsonResponse(releases)
                : throw new InvalidOperationException("Untrusted release assets must not be requested.")));
        var source = new GitHubUpdateSource(http);

        var candidate = await source.FindLatestAsync(current, "fixture-token", TestToken);

        Assert.Null(candidate);
    }

    [Fact]
    public async Task Artifact_download_rejects_wrong_sha256_without_publishing_destination()
    {
        Directory.CreateDirectory(root);
        var bytes = Encoding.UTF8.GetBytes("DATA");
        var manifest = Manifest(11) with
        {
            ArtifactSize = bytes.Length,
            Sha256 = new string('0', 64)
        };
        var candidate = new UpdateCandidate(manifest,
            new Uri("https://api.github.com/assets/artifact"), new Uri("https://api.github.com/assets/manifest"));
        using var http = new HttpClient(new FakeHandler(_ => BytesResponse(bytes)));
        var source = new GitHubUpdateSource(http);
        var destination = Path.Combine(root, "update.zip");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => source.DownloadArtifactAsync(candidate, "fixture-token", destination, TestToken));

        Assert.Contains("SHA-256", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Artifact_download_accepts_exact_length_and_sha256()
    {
        Directory.CreateDirectory(root);
        var bytes = Encoding.UTF8.GetBytes("VERIFIED");
        var manifest = Manifest(11) with
        {
            ArtifactSize = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
        var candidate = new UpdateCandidate(manifest,
            new Uri("https://api.github.com/assets/artifact"), new Uri("https://api.github.com/assets/manifest"));
        using var http = new HttpClient(new FakeHandler(_ => BytesResponse(bytes)));
        var source = new GitHubUpdateSource(http);
        var destination = Path.Combine(root, "update.zip");

        await source.DownloadArtifactAsync(candidate, "fixture-token", destination, TestToken);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination, TestToken));
    }

    [Theory]
    [InlineData("https://example.invalid/assets/artifact")]
    [InlineData("http://api.github.com/assets/artifact")]
    [InlineData("https://api.github.com:444/assets/artifact")]
    [InlineData("https://user@api.github.com/assets/artifact")]
    public async Task Authenticated_download_rejects_untrusted_uri_before_transport(string uri)
    {
        Directory.CreateDirectory(root);
        var bytes = Encoding.UTF8.GetBytes("DATA");
        var manifest = Manifest(11) with
        {
            ArtifactSize = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
        var sent = false;
        using var http = new HttpClient(new FakeHandler(_ =>
        {
            sent = true;
            return BytesResponse(bytes);
        }));
        var source = new GitHubUpdateSource(http);
        var candidate = new UpdateCandidate(
            manifest, new Uri(uri), new Uri("https://api.github.com/assets/manifest"));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            source.DownloadArtifactAsync(
                candidate, "fixture-token", Path.Combine(root, "untrusted.zip"), TestToken));

        Assert.Contains("api.github.com", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(sent);
    }

    [Fact]
    public async Task Approved_api_host_receives_bearer_authentication()
    {
        Directory.CreateDirectory(root);
        var bytes = Encoding.UTF8.GetBytes("AUTH");
        var manifest = Manifest(11) with
        {
            ArtifactSize = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
        string? scheme = null;
        string? parameter = null;
        using var http = new HttpClient(new FakeHandler(request =>
        {
            scheme = request.Headers.Authorization?.Scheme;
            parameter = request.Headers.Authorization?.Parameter;
            return BytesResponse(bytes);
        }));
        var source = new GitHubUpdateSource(http);
        var candidate = new UpdateCandidate(
            manifest,
            new Uri("https://api.github.com/repos/fengie/mhw-mods/releases/assets/123"),
            new Uri("https://api.github.com/assets/manifest"));

        await source.DownloadArtifactAsync(
            candidate, "fixture-token", Path.Combine(root, "authorized.zip"), TestToken);

        Assert.Equal("Bearer", scheme);
        Assert.Equal("fixture-token", parameter);
    }

    [Fact]
    public async Task Dotnet_redirect_clears_bearer_before_release_asset_host()
    {
        Directory.CreateDirectory(root);
        var bytes = Encoding.UTF8.GetBytes("CDN!");
        await using var server = new RedirectTlsServer(bytes);
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            certificate is not null
            && string.Equals(
                certificate.GetCertHashString(),
                server.CertificateThumbprint,
                StringComparison.OrdinalIgnoreCase);
        handler.SslOptions.ApplicationProtocols = [SslApplicationProtocol.Http11];
        handler.ConnectCallback = async (_, ct) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(IPAddress.Loopback, server.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
        using var http = new HttpClient(handler);
        var source = new GitHubUpdateSource(http);
        var manifest = Manifest(11) with
        {
            ArtifactSize = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
        var candidate = new UpdateCandidate(
            manifest,
            new Uri("https://api.github.com/repos/fengie/mhw-mods/releases/assets/123"),
            new Uri("https://api.github.com/assets/manifest"));

        await source.DownloadArtifactAsync(
            candidate, "fixture-token", Path.Combine(root, "redirect.zip"), TestToken);
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestToken);

        Assert.Equal(2, server.Requests.Count);
        Assert.Contains("Host: api.github.com", server.Requests[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Authorization: Bearer fixture-token", server.Requests[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Host: release-assets.example", server.Requests[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization:", server.Requests[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Package_verifier_rejects_wrong_product_manifest_hash()
    {
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        var product = new ProductFileManifest(1, []);
        await File.WriteAllTextAsync(
            Path.Combine(payload, UpdateProtocol.ProductManifestFileName),
            JsonSerializer.Serialize(product, UpdateProtocol.Json), TestToken);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => UpdatePackageVerifier.VerifyAsync(payload, new string('F', 64), TestToken));
    }

    [Fact]
    public async Task Package_verifier_rejects_unknown_file_not_owned_by_manifest()
    {
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        var productPath = Path.Combine(payload, UpdateProtocol.ProductManifestFileName);
        var product = new ProductFileManifest(1, []);
        await File.WriteAllTextAsync(productPath, JsonSerializer.Serialize(product, UpdateProtocol.Json), TestToken);
        await File.WriteAllTextAsync(Path.Combine(payload, "surprise.dll"), "unknown", TestToken);
        var productHash = await UpdatePackageVerifier.HashFileAsync(productPath, TestToken);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => UpdatePackageVerifier.VerifyAsync(payload, productHash, TestToken));

        Assert.Contains("file set", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prepared_handoff_identity_is_invalidated_when_staged_update_is_replaced()
    {
        var method=typeof(MainWindowViewModel).GetMethod(
            "IsPreparedHandoffCurrent",
            System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var prepared=new PreparedUpdateHandoff(null!,"request.json","helper.exe");
        var first=new StagedUpdate(null!,"stage-a","manifest-a");
        var replacement=new StagedUpdate(null!,"stage-b","manifest-b");

        Assert.True((bool)method!.Invoke(null,[prepared,first,first])!);
        Assert.False((bool)method.Invoke(null,[prepared,first,replacement])!);
        Assert.False((bool)method.Invoke(null,[prepared,first,null])!);
        Assert.False((bool)method.Invoke(null,[null,first,first])!);
    }

    [Fact]
    public void Health_arguments_are_not_forwarded_to_restarted_application()
    {
        var filtered = UpdateArgumentSanitizer.RemoveHealthArguments(
        [
            "--normal", "value",
            UpdateHealthProtocol.TokenArgument, "secret",
            UpdateHealthProtocol.FileArgument, "health.json",
            UpdateHealthProtocol.AttemptArgument, "attempt",
            "--tail", "two words"
        ]);

        Assert.Equal(
            ["--normal", "value", "--tail", "two words"],
            filtered);
    }

    [Theory]
    [InlineData(UpdateHealthProtocol.TokenArgument)]
    [InlineData(UpdateHealthProtocol.FileArgument)]
    [InlineData(UpdateHealthProtocol.AttemptArgument)]
    public void Health_argument_sanitizer_rejects_dangling_owned_flag(string flag)
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdateArgumentSanitizer.RemoveHealthArguments(["--normal", "value", flag]));
    }

    [Fact]
    public void Health_argument_sanitizer_rejects_owned_flag_as_another_owned_flags_value()
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdateArgumentSanitizer.RemoveHealthArguments(
                [UpdateHealthProtocol.TokenArgument, UpdateHealthProtocol.FileArgument, "health.json"]));
    }

    [Fact]
    public async Task Handoff_copies_verified_owned_helper_closure_and_writes_sanitized_request()
    {
        var install = Path.Combine(root, "handoff-install");
        var managerHome = Path.Combine(root, "manager-home");
        var stage = Path.Combine(UpdatePackageStager.GetUpdaterRoot(), "tests", "handoff-" + Guid.NewGuid().ToString("N"), "stage");
        Directory.CreateDirectory(Path.Combine(install, "UpdaterHelper"));
        Directory.CreateDirectory(managerHome);
        Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(
            Path.Combine(install, "app.exe"), "OLD", TestToken);
        await File.WriteAllTextAsync(
            Path.Combine(install, UpdateProtocol.BuildIdentityFileName),
            JsonSerializer.Serialize(
                Identity(10),
                UpdateProtocol.Json),
            TestToken);
        var helper = Path.Combine(
            install,
            UpdateProtocol.HelperRelativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(helper, "HELPER-BYTES", TestToken);
        const string helperDependencyRelative =
            "UpdaterHelper/MHW Mod Manager Updater.runtimeconfig.json";
        var helperDependency = Path.Combine(
            install,
            helperDependencyRelative.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(helperDependency, "RUNTIME-CONFIG", TestToken);

        var installedManifest = new ProductFileManifest(
            1,
            [
                await EntryAsync(install, "app.exe"),
                await EntryAsync(install, UpdateProtocol.BuildIdentityFileName),
                await EntryAsync(install, UpdateProtocol.HelperRelativePath),
                await EntryAsync(install, helperDependencyRelative)
            ]);
        var installedManifestPath = Path.Combine(
            install,
            UpdateProtocol.ProductManifestFileName);
        await File.WriteAllTextAsync(
            installedManifestPath,
            JsonSerializer.Serialize(installedManifest, UpdateProtocol.Json),
            TestToken);
        var installedManifestHash = await UpdatePackageVerifier.HashFileAsync(
            installedManifestPath,
            TestToken);
        await File.WriteAllTextAsync(
            Path.Combine(install, UpdateProtocol.InstallMarkerFileName),
            JsonSerializer.Serialize(
                new ReleaseInstallMarker(
                    1,
                    UpdateProtocol.ProductId,
                    UpdateProtocol.Channel,
                    Identity(10),
                    "app.exe",
                    installedManifestHash),
                UpdateProtocol.Json),
            TestToken);

        await File.WriteAllTextAsync(
            Path.Combine(stage, "app.exe"), "NEW", TestToken);
        await File.WriteAllTextAsync(
            Path.Combine(stage, UpdateProtocol.BuildIdentityFileName),
            JsonSerializer.Serialize(Identity(11), UpdateProtocol.Json),
            TestToken);
        var stagedManifest = new ProductFileManifest(
            1,
            [
                await EntryAsync(stage, "app.exe"),
                await EntryAsync(stage, UpdateProtocol.BuildIdentityFileName)
            ]);
        var stagedManifestPath = Path.Combine(
            stage,
            UpdateProtocol.ProductManifestFileName);
        await File.WriteAllTextAsync(
            stagedManifestPath,
            JsonSerializer.Serialize(stagedManifest, UpdateProtocol.Json),
            TestToken);
        var stagedManifestHash = await UpdatePackageVerifier.HashFileAsync(
            stagedManifestPath,
            TestToken);
        await File.WriteAllTextAsync(
            Path.Combine(stage, UpdateProtocol.InstallMarkerFileName),
            JsonSerializer.Serialize(
                new ReleaseInstallMarker(
                    1,
                    UpdateProtocol.ProductId,
                    UpdateProtocol.Channel,
                    Identity(11),
                    "app.exe",
                    stagedManifestHash),
                UpdateProtocol.Json),
            TestToken);

        var manifest = Manifest(11) with
        {
            ProductManifestSha256 = stagedManifestHash,
            ArtifactSize = 1
        };
        var staged = new StagedUpdate(
            manifest,
            stage,
            stagedManifestPath);
        using var client = new UpdateClientService(
            new HttpClient(new FakeHandler(_ => throw new InvalidOperationException("Network must not be used."))));

        var prepared = await client.PrepareHandoffAsync(
            staged,
            install,
            [
                "--normal", "value",
                UpdateHealthProtocol.TokenArgument, "old-token",
                UpdateHealthProtocol.FileArgument, "old-health",
                UpdateHealthProtocol.AttemptArgument, "old-attempt"
            ],
            4321,
            TestToken,
            managerHomeRoot:managerHome);

        Assert.True(File.Exists(prepared.HelperExecutablePath));
        Assert.Equal(
            "HELPER-BYTES",
            await File.ReadAllTextAsync(prepared.HelperExecutablePath, TestToken));
        var copiedDependency = Path.Combine(
            Path.GetDirectoryName(prepared.HelperExecutablePath)!,
            Path.GetFileName(helperDependency));
        Assert.Equal(
            "RUNTIME-CONFIG",
            await File.ReadAllTextAsync(copiedDependency, TestToken));
        var request = await UpdateRequestStore.ReadAsync(
            prepared.RequestPath,
            TestToken);
        Assert.Equal(4321, request.CurrentProcessId);
        Assert.Equal(["--normal", "value"], request.RestartArguments);
        Assert.Equal(stage, request.StagingRoot);
        Assert.Equal(Path.GetFullPath(managerHome), request.ManagerHomeRoot);
        Assert.Equal(11, request.Manifest.BuildNumber);
    }

    [Fact]
    public async Task Handoff_rejects_helper_not_owned_by_installed_manifest()
    {
        var install = Path.Combine(root, "handoff-unowned");
        Directory.CreateDirectory(Path.Combine(install, "UpdaterHelper"));
        await File.WriteAllTextAsync(
            Path.Combine(install, "app.exe"), "OLD", TestToken);
        await File.WriteAllTextAsync(
            Path.Combine(install, UpdateProtocol.BuildIdentityFileName),
            JsonSerializer.Serialize(Identity(10), UpdateProtocol.Json),
            TestToken);
        await File.WriteAllTextAsync(
            Path.Combine(
                install,
                UpdateProtocol.HelperRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            "UNOWNED-HELPER",
            TestToken);
        var installedManifest = new ProductFileManifest(
            1,
            [
                await EntryAsync(install, "app.exe"),
                await EntryAsync(install, UpdateProtocol.BuildIdentityFileName)
            ]);
        var installedManifestPath = Path.Combine(
            install,
            UpdateProtocol.ProductManifestFileName);
        await File.WriteAllTextAsync(
            installedManifestPath,
            JsonSerializer.Serialize(installedManifest, UpdateProtocol.Json),
            TestToken);
        var installedHash = await UpdatePackageVerifier.HashFileAsync(
            installedManifestPath,
            TestToken);
        await File.WriteAllTextAsync(
            Path.Combine(install, UpdateProtocol.InstallMarkerFileName),
            JsonSerializer.Serialize(
                new ReleaseInstallMarker(
                    1, UpdateProtocol.ProductId, UpdateProtocol.Channel,
                    Identity(10), "app.exe", installedHash),
                UpdateProtocol.Json),
            TestToken);

        var stage = Path.Combine(UpdatePackageStager.GetUpdaterRoot(), "tests", "handoff-unowned-" + Guid.NewGuid().ToString("N"), "stage");
        Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(Path.Combine(stage, "app.exe"), "NEW", TestToken);
        await File.WriteAllTextAsync(
            Path.Combine(stage, UpdateProtocol.BuildIdentityFileName),
            JsonSerializer.Serialize(Identity(11), UpdateProtocol.Json),
            TestToken);
        var stagedManifest = new ProductFileManifest(
            1,
            [
                await EntryAsync(stage, "app.exe"),
                await EntryAsync(stage, UpdateProtocol.BuildIdentityFileName)
            ]);
        var stagedManifestPath = Path.Combine(stage, UpdateProtocol.ProductManifestFileName);
        await File.WriteAllTextAsync(
            stagedManifestPath,
            JsonSerializer.Serialize(stagedManifest, UpdateProtocol.Json),
            TestToken);
        var stagedHash = await UpdatePackageVerifier.HashFileAsync(
            stagedManifestPath, TestToken);
        await File.WriteAllTextAsync(
            Path.Combine(stage, UpdateProtocol.InstallMarkerFileName),
            JsonSerializer.Serialize(
                new ReleaseInstallMarker(
                    1, UpdateProtocol.ProductId, UpdateProtocol.Channel,
                    Identity(11), "app.exe", stagedHash),
                UpdateProtocol.Json),
            TestToken);
        var staged = new StagedUpdate(
            Manifest(11) with { ProductManifestSha256 = stagedHash, ArtifactSize = 1 },
            stage,
            stagedManifestPath);
        using var client = new UpdateClientService(
            new HttpClient(new FakeHandler(_ => throw new InvalidOperationException())));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.PrepareHandoffAsync(
                staged,
                install,
                [],
                1,
                TestToken));

        Assert.Contains("does not own updater helper", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProductFileEntry> EntryAsync(string rootPath, string relative)
    {
        var normalized = UpdatePathSafety.NormalizeRelativeFilePath(relative);
        var path = Path.Combine(
            rootPath,
            normalized.Replace('/', Path.DirectorySeparatorChar));
        var info = new FileInfo(path);
        return new ProductFileEntry(
            normalized,
            info.Length,
            await UpdatePackageVerifier.HashFileAsync(path, TestToken));
    }

    private static UpdateBuildIdentity Identity(long build) =>
        new(1, UpdateProtocol.Channel, "8.8.0", $"sha-{build}-abcdef", build, DateTimeOffset.UtcNow);

    private static UpdateManifest Manifest(long buildNumber) =>
        new(1, UpdateProtocol.Channel, "8.8.0", $"sha-{buildNumber}-abcdef", buildNumber,
            "update.zip", 4, new string('A', 64), new string('B', 64),
            "MHW Mod Manager.exe", 1, DateTimeOffset.UtcNow);

    private static string ReleaseList(params (long Build, UpdateManifest Manifest)[] releases) =>
        JsonSerializer.Serialize(releases.Select(x => ReleaseEntry(x.Build, x.Manifest)));

    private static object ReleaseEntry(
        long build,
        UpdateManifest manifest,
        bool draft = false,
        bool prerelease = false,
        bool immutable = true) =>
        new
        {
            tag_name = $"updater-main-{build}",
            draft,
            prerelease,
            immutable,
            assets = new object[]
            {
                new { name = "update-manifest.json", url = $"https://api.github.com/assets/manifest-{build}", size = 512 },
                new { name = manifest.ArtifactName, url = $"https://api.github.com/assets/artifact-{build}", size = manifest.ArtifactSize }
            }
        };

    private static HttpResponseMessage JsonResponse(object value, string contentType = "application/json") =>
        JsonResponse(value is string raw ? raw : JsonSerializer.Serialize(value), contentType);

    private static HttpResponseMessage JsonResponse(string json, string contentType = "application/json") =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, contentType)
        };

    private static HttpResponseMessage BytesResponse(byte[] bytes) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };

    private sealed class RedirectTlsServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly X509Certificate2 certificate;
        private readonly byte[] payload;
        private readonly CancellationTokenSource stop = new(TimeSpan.FromSeconds(15));
        private readonly Task serverTask;

        public RedirectTlsServer(byte[] payload)
        {
            this.payload = payload;
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var ephemeral = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
            certificate = X509CertificateLoader.LoadPkcs12(
                ephemeral.Export(X509ContentType.Pfx),
                password: null,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            serverTask = ServeAsync();
        }

        public int Port { get; }

        public string CertificateThumbprint => certificate.Thumbprint;

        public List<string> Requests { get; } = [];

        public Task Completion => serverTask;

        private async Task ServeAsync()
        {
            for (var i = 0; i < 2; i++)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                await using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate,
                        ApplicationProtocols = [SslApplicationProtocol.Http11]
                    },
                    stop.Token);

                using var reader = new StreamReader(
                    ssl, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, 4096, leaveOpen: true);
                var requestText = new StringBuilder();
                while (true)
                {
                    var line = await reader.ReadLineAsync(stop.Token);
                    if (line is null) break;
                    requestText.AppendLine(line);
                    if (line.Length == 0) break;
                }
                Requests.Add(requestText.ToString());

                if (i == 0)
                {
                    var redirect = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 302 Found\r\n" +
                        "Location: https://release-assets.example/asset\r\n" +
                        "Content-Length: 0\r\n" +
                        "Connection: close\r\n\r\n");
                    await ssl.WriteAsync(redirect, stop.Token);
                }
                else
                {
                    var header = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                    await ssl.WriteAsync(header, stop.Token);
                    await ssl.WriteAsync(payload, stop.Token);
                }
                await ssl.FlushAsync(stop.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            listener.Stop();
            stop.Cancel();
            try { await serverTask; }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException)
            {
            }
            stop.Dispose();
            certificate.Dispose();
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
