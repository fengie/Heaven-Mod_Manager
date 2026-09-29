using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UpdaterGitHubAssetDownloadTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "mhwmm-updater-asset-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task FindLatest_accepts_utf8_bom_manifest_asset()
    {
        var packageBytes = Encoding.UTF8.GetBytes("VERIFIED");
        var manifest = new UpdateManifest(
            UpdateProtocol.ManifestSchemaVersion,
            UpdateProtocol.Channel,
            "8.8.0",
            "abcdef1234567890",
            42,
            "update.zip",
            packageBytes.Length,
            Convert.ToHexString(SHA256.HashData(packageBytes)),
            new string('A', 64),
            "MHW Mod Manager.exe",
            UpdateProtocol.UpdaterProtocolVersion,
            DateTimeOffset.UtcNow);
        var json = JsonSerializer.SerializeToUtf8Bytes(manifest, UpdateProtocol.Json);
        var manifestBytes = new byte[json.Length + 3];
        manifestBytes[0] = 0xEF;
        manifestBytes[1] = 0xBB;
        manifestBytes[2] = 0xBF;
        Buffer.BlockCopy(json, 0, manifestBytes, 3, json.Length);

        var releasesJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                tag_name = "updater-main-42",
                draft = false,
                prerelease = false,
                immutable = true,
                assets = new[]
                {
                    new { name = "update-manifest.json", url = "https://api.github.com/assets/manifest", size = (long)manifestBytes.Length },
                    new { name = "update.zip", url = "https://api.github.com/assets/package", size = (long)packageBytes.Length }
                }
            }
        });

        using var http = new HttpClient(new FakeHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/releases", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releasesJson, Encoding.UTF8, "application/json")
                };
            if (path == "/assets/manifest")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(manifestBytes)
                };
            throw new InvalidOperationException($"Unexpected updater test URI: {request.RequestUri}");
        }));

        var current = new UpdateBuildIdentity(
            UpdateProtocol.BuildIdentitySchemaVersion,
            UpdateProtocol.Channel,
            "8.8.0",
            "abcdef0",
            41,
            DateTimeOffset.UtcNow);

        var candidate = await new GitHubUpdateSource(http).FindLatestAsync(
            current,
            "fixture-token",
            TestToken);

        Assert.NotNull(candidate);
        Assert.Equal(42, candidate.Manifest.BuildNumber);
        Assert.Equal("abcdef1234567890", candidate.Manifest.SourceSha);
    }

    [Fact]
    public async Task Artifact_download_requests_binary_media_type()
    {
        Directory.CreateDirectory(root);
        var bytes = Encoding.UTF8.GetBytes("VERIFIED");
        var manifest = new UpdateManifest(            UpdateProtocol.ManifestSchemaVersion,
            UpdateProtocol.Channel,
            "8.8.0",
            "abcdef1234567890",
            42,
            "update.zip",
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)),
            new string('A', 64),
            "MHW Mod Manager.exe",
            UpdateProtocol.UpdaterProtocolVersion,
            DateTimeOffset.UtcNow);
        var candidate = new UpdateCandidate(
            manifest,
            new Uri("https://api.github.com/assets/1"),
            new Uri("https://api.github.com/assets/2"));
        var sawBinaryAccept = false;
        var sawAuthorization = false;
        using var http = new HttpClient(new FakeHandler(request =>
        {
            sawBinaryAccept = request.Headers.Accept.Any(x =>
                string.Equals(
                    x.MediaType,
                    "application/octet-stream",
                    StringComparison.OrdinalIgnoreCase));
            sawAuthorization = request.Headers.Authorization is not null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
        }));
        var destination = Path.Combine(root, "update.zip");

        await new GitHubUpdateSource(http).DownloadArtifactAsync(
            candidate,
            token: null,
            destination,
            TestToken);

        Assert.True(sawBinaryAccept);
        Assert.False(sawAuthorization);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination, TestToken));
    }

    private sealed class FakeHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
