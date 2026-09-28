using System.Net;
using System.Security.Cryptography;
using System.Text;
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
        using var http = new HttpClient(new FakeHandler(request =>
        {
            sawBinaryAccept = request.Headers.Accept.Any(x =>
                string.Equals(
                    x.MediaType,
                    "application/octet-stream",
                    StringComparison.OrdinalIgnoreCase));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
        }));
        var destination = Path.Combine(root, "update.zip");

        await new GitHubUpdateSource(http).DownloadArtifactAsync(
            candidate,
            "fixture-token",
            destination,
            TestToken);

        Assert.True(sawBinaryAccept);
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
