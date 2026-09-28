using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public async Task Malformed_update_manifest_is_rejected()
    {
        var current = Identity(10);
        var releases = ReleaseList((11, Manifest(11)));
        using var http = new HttpClient(new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
                return JsonResponse(releases);
            if (request.RequestUri.AbsolutePath.EndsWith("/manifest-11", StringComparison.Ordinal))
                return JsonResponse("{ definitely-not-json", "application/octet-stream");
            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        }));
        var source = new GitHubUpdateSource(http);

        await Assert.ThrowsAsync<JsonException>(
            () => source.FindLatestAsync(current, "fixture-token", TestToken));
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
    public async Task Artifact_download_rejects_truncated_response_without_publishing_destination()
    {
        Directory.CreateDirectory(root);
        var bytes = Encoding.UTF8.GetBytes("SHORT");
        var manifest = Manifest(11) with
        {
            ArtifactSize = bytes.Length + 1,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
        var candidate = new UpdateCandidate(manifest,
            new Uri("https://api.github.com/assets/artifact"), new Uri("https://api.github.com/assets/manifest"));
        using var http = new HttpClient(new FakeHandler(_ => BytesResponse(bytes)));
        var destination = Path.Combine(root, "truncated.zip");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new GitHubUpdateSource(http).DownloadArtifactAsync(
                candidate, "fixture-token", destination, TestToken));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Artifact_download_rejects_oversized_response_without_publishing_destination()
    {
        Directory.CreateDirectory(root);
        var bytes = Encoding.UTF8.GetBytes("TOO-LONG");
        var manifest = Manifest(11) with
        {
            ArtifactSize = bytes.Length - 1,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
        var candidate = new UpdateCandidate(manifest,
            new Uri("https://api.github.com/assets/artifact"), new Uri("https://api.github.com/assets/manifest"));
        using var http = new HttpClient(new FakeHandler(_ => BytesResponse(bytes)));
        var destination = Path.Combine(root, "oversized.zip");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new GitHubUpdateSource(http).DownloadArtifactAsync(
                candidate, "fixture-token", destination, TestToken));

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
    [InlineData("../escape.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("C:/drive.dll")]
    [InlineData("file.txt:stream")]
    [InlineData("CON")]
    public async Task Staging_rejects_unsafe_zip_entry_paths_through_real_extraction(string entryName)
    {
        var zip = BuildZip(entryName);
        var manifest = Manifest(11) with
        {
            ArtifactSize = zip.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(zip))
        };
        var candidate = new UpdateCandidate(manifest,
            new Uri("https://api.github.com/assets/artifact"), new Uri("https://api.github.com/assets/manifest"));
        using var http = new HttpClient(new FakeHandler(_ => BytesResponse(zip)));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new UpdatePackageStager(new GitHubUpdateSource(http))
                .StageAsync(candidate, "fixture-token", TestToken));
    }

    [Fact]
    public async Task Staging_rejects_symbolic_link_zip_entry()
    {
        var zip = BuildZip("link.dll", symbolicLink: true);
        var manifest = Manifest(11) with
        {
            ArtifactSize = zip.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(zip))
        };
        var candidate = new UpdateCandidate(manifest,
            new Uri("https://api.github.com/assets/artifact"), new Uri("https://api.github.com/assets/manifest"));
        using var http = new HttpClient(new FakeHandler(_ => BytesResponse(zip)));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new UpdatePackageStager(new GitHubUpdateSource(http))
                .StageAsync(candidate, "fixture-token", TestToken));
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

    private static byte[] BuildZip(string entryName, bool symbolicLink = false)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            if (symbolicLink)
                entry.ExternalAttributes = (0xA000 | 0x1FF) << 16;
            using var entryStream = entry.Open();
            entryStream.Write([1, 2, 3, 4]);
        }
        return memory.ToArray();
    }

    private static UpdateBuildIdentity Identity(long build) =>
        new(1, UpdateProtocol.Channel, "8.8.0", $"sha-{build}-abcdef", build, DateTimeOffset.UtcNow);

    private static UpdateManifest Manifest(long buildNumber) =>
        new(1, UpdateProtocol.Channel, "8.8.0", $"sha-{buildNumber}-abcdef", buildNumber,
            "update.zip", 4, new string('A', 64), new string('B', 64),
            "MHW Mod Manager.exe", 1, DateTimeOffset.UtcNow);

    private static string ReleaseList(params (long Build, UpdateManifest Manifest)[] releases)
    {
        var values = releases.Select(x => new
        {
            tag_name = $"updater-main-{x.Build}",
            draft = false,
            assets = new object[]
            {
                new { name = "update-manifest.json", url = $"https://api.github.com/assets/manifest-{x.Build}", size = 512 },
                new { name = x.Manifest.ArtifactName, url = $"https://api.github.com/assets/artifact-{x.Build}", size = x.Manifest.ArtifactSize }
            }
        });
        return JsonSerializer.Serialize(values);
    }

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

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
