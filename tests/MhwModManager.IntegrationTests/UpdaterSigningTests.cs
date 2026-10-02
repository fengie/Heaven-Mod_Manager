using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UpdaterSigningTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now =
        new(2026, 10, 2, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_signature_is_accepted()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = CreateManifest(42);
        var envelope = UpdateSignedMetadataSigner.Sign(
            manifest,
            "release-2026-a",
            Now.AddMinutes(-5),
            Now.AddDays(7),
            key);
        var verifier = CreateVerifier(key, 1, null);

        verifier.Verify(envelope, manifest, 41);
    }

    [Fact]
    public void Wrong_key_and_manifest_mutation_are_rejected()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var wrong = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = CreateManifest(42);
        var envelope = UpdateSignedMetadataSigner.Sign(
            manifest,
            "release-2026-a",
            Now.AddMinutes(-5),
            Now.AddDays(7),
            signer);

        Assert.Throws<InvalidDataException>(
            () => CreateVerifier(wrong, 1, null).Verify(envelope, manifest, 41));

        var mutated = manifest with { Sha256 = new string('B', 64) };
        Assert.Throws<InvalidDataException>(
            () => CreateVerifier(signer, 1, null).Verify(envelope, mutated, 41));
    }

    [Fact]
    public void Unknown_key_malformed_signature_not_yet_valid_and_product_manifest_mutation_fail_closed()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = CreateManifest(42);
        var envelope = UpdateSignedMetadataSigner.Sign(
            manifest,
            "release-2026-a",
            Now.AddMinutes(-5),
            Now.AddDays(7),
            key);
        var verifier = CreateVerifier(key, 1, null);

        var unknownKey = envelope with
        {
            Payload = envelope.Payload with { SigningKeyId = "release-unknown" }
        };
        Assert.Throws<InvalidDataException>(
            () => verifier.Verify(unknownKey, manifest, 41));

        var malformedSignature = envelope with { SignatureBase64 = "not-base64!" };
        Assert.Throws<InvalidDataException>(
            () => verifier.Verify(malformedSignature, manifest, 41));

        var notYetValid = UpdateSignedMetadataSigner.Sign(
            manifest,
            "release-2026-a",
            Now.AddMinutes(1),
            Now.AddDays(1),
            key);
        Assert.Throws<InvalidDataException>(
            () => verifier.Verify(notYetValid, manifest, 41));

        var mutatedProductManifest = manifest with
        {
            ProductManifestSha256 = new string('D', 64)
        };
        Assert.Throws<InvalidDataException>(
            () => verifier.Verify(envelope, mutatedProductManifest, 41));
    }

    [Fact]
    public void Expiry_rollback_and_key_rotation_window_fail_closed()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = CreateManifest(42);
        var expired = UpdateSignedMetadataSigner.Sign(
            manifest,
            "release-2026-a",
            Now.AddDays(-2),
            Now.AddDays(-1),
            key);

        Assert.Throws<InvalidDataException>(
            () => CreateVerifier(key, 1, null).Verify(expired, manifest, 41));

        var valid = UpdateSignedMetadataSigner.Sign(
            manifest,
            "release-2026-a",
            Now.AddMinutes(-5),
            Now.AddDays(7),
            key);
        Assert.Throws<InvalidDataException>(
            () => CreateVerifier(key, 1, null).Verify(valid, manifest, 42));
        Assert.Throws<InvalidDataException>(
            () => CreateVerifier(key, 43, null).Verify(valid, manifest, 41));
        Assert.Throws<InvalidDataException>(
            () => CreateVerifier(key, 1, 41).Verify(valid, manifest, 41));
    }

    [Fact]
    public async Task GitHub_source_requires_and_verifies_signature_when_trust_is_configured()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var packageBytes = Encoding.UTF8.GetBytes("VERIFIED-SIGNED-PACKAGE");
        var manifest = CreateManifest(
            42,
            artifactSize: packageBytes.Length,
            artifactSha256: Convert.ToHexString(SHA256.HashData(packageBytes)));
        var envelope = UpdateSignedMetadataSigner.Sign(
            manifest,
            "release-2026-a",
            Now.AddMinutes(-5),
            Now.AddDays(7),
            key);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            UpdateProtocol.Json);
        var signatureBytes = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            UpdateProtocol.Json);
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
                    new
                    {
                        name = "update-manifest.json",
                        url = "https://api.github.com/assets/manifest",
                        size = (long)manifestBytes.Length
                    },
                    new
                    {
                        name = UpdateProtocol.SignedMetadataFileName,
                        url = "https://api.github.com/assets/signature",
                        size = (long)signatureBytes.Length
                    },
                    new
                    {
                        name = manifest.ArtifactName,
                        url = "https://api.github.com/assets/package",
                        size = manifest.ArtifactSize
                    }
                }
            }
        });

        using var http = new HttpClient(new FakeHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/releases", StringComparison.Ordinal))
                return JsonResponse(releasesJson);
            if (path == "/assets/manifest")
                return BinaryResponse(manifestBytes);
            if (path == "/assets/signature")
                return BinaryResponse(signatureBytes);
            throw new InvalidOperationException(
                $"Unexpected updater test URI: {request.RequestUri}");
        }));
        var current = new UpdateBuildIdentity(
            UpdateProtocol.BuildIdentitySchemaVersion,
            UpdateProtocol.Channel,
            "8.8.74",
            new string('0', 40),
            41,
            Now.AddDays(-1));

        var candidate = await new GitHubUpdateSource(
                http,
                signedMetadataVerifier: CreateVerifier(key, 1, null))
            .FindLatestAsync(
                current,
                token: null,
                ct: TestToken,
                repository: UpdateProtocol.PublicReleaseRepository);

        Assert.NotNull(candidate);
        Assert.Equal(42, candidate.Manifest.BuildNumber);
    }

    [Fact]
    public async Task GitHub_source_rejects_unsigned_release_when_trust_is_configured()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = CreateManifest(42);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            UpdateProtocol.Json);
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
                    new
                    {
                        name = "update-manifest.json",
                        url = "https://api.github.com/assets/manifest",
                        size = (long)manifestBytes.Length
                    },
                    new
                    {
                        name = manifest.ArtifactName,
                        url = "https://api.github.com/assets/package",
                        size = manifest.ArtifactSize
                    }
                }
            }
        });
        using var http = new HttpClient(new FakeHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/releases", StringComparison.Ordinal))
                return JsonResponse(releasesJson);
            if (path == "/assets/manifest")
                return BinaryResponse(manifestBytes);
            throw new InvalidOperationException(
                $"Unexpected updater test URI: {request.RequestUri}");
        }));
        var current = new UpdateBuildIdentity(
            UpdateProtocol.BuildIdentitySchemaVersion,
            UpdateProtocol.Channel,
            "8.8.74",
            new string('0', 40),
            41,
            Now.AddDays(-1));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new GitHubUpdateSource(
                    http,
                    signedMetadataVerifier: CreateVerifier(key, 1, null))
                .FindLatestAsync(
                    current,
                    token: null,
                    ct: TestToken,
                    repository: UpdateProtocol.PublicReleaseRepository));
    }

    private static UpdateManifest CreateManifest(
        long build,
        long artifactSize = 123,
        string? artifactSha256 = null) =>
        new(
            UpdateProtocol.ManifestSchemaVersion,
            UpdateProtocol.Channel,
            "8.8.74",
            new string('a', 40),
            build,
            "MHW-Manual-Mod-Manager-v8.8.74-win-x64.zip",
            artifactSize,
            artifactSha256 ?? new string('A', 64),
            new string('C', 64),
            "MHW Mod Manager.exe",
            UpdateProtocol.UpdaterProtocolVersion,
            Now.AddMinutes(-10));

    private static UpdateSignedMetadataVerifier CreateVerifier(
        ECDsa key,
        long first,
        long? last) =>
        new(
            [
                new(
                    "release-2026-a",
                    UpdateSigningAlgorithm.EcdsaP256Sha256P1363,
                    Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
                    first,
                    last)
            ],
            () => Now);

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage BinaryResponse(byte[] bytes) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };

    private sealed class FakeHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
