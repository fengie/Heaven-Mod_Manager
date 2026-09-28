using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
