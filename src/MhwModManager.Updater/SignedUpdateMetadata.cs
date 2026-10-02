using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Updater;

public static class UpdateSigningAlgorithm
{
    public const string EcdsaP256Sha256P1363 = "ECDSA-P256-SHA256-P1363";
}

public sealed record UpdateSigningPublicKey(
    string KeyId,
    string Algorithm,
    string SubjectPublicKeyInfoBase64,
    long FirstAcceptedBuild,
    long? LastAcceptedBuild = null);

public sealed record UpdateSignedMetadataPayload(
    int SchemaVersion,
    string SigningKeyId,
    string Channel,
    string ProductVersion,
    string SourceSha,
    long BuildNumber,
    string ArtifactName,
    long ArtifactSize,
    string ArtifactSha256,
    string ProductManifestSha256,
    int MinimumUpdaterProtocol,
    DateTimeOffset IssuedUtc,
    DateTimeOffset ExpiresUtc)
{
    public static UpdateSignedMetadataPayload FromManifest(
        UpdateManifest manifest,
        string signingKeyId,
        DateTimeOffset issuedUtc,
        DateTimeOffset expiresUtc)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"build={manifest.BuildNumber}; key={signingKeyId}");
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKeyId);
        manifest.Validate();
        return new(
            UpdateProtocol.SignedMetadataSchemaVersion,
            signingKeyId.Trim(),
            manifest.Channel,
            manifest.ProductVersion,
            manifest.SourceSha,
            manifest.BuildNumber,
            manifest.ArtifactName,
            manifest.ArtifactSize,
            manifest.Sha256,
            manifest.ProductManifestSha256,
            manifest.MinimumUpdaterProtocol,
            issuedUtc.ToUniversalTime(),
            expiresUtc.ToUniversalTime());
    }

    public void ValidateShape()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"build={BuildNumber}; key={SigningKeyId}");
        if (SchemaVersion != UpdateProtocol.SignedMetadataSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported signed update metadata schema {SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(SigningKeyId)
            || SigningKeyId.Length > 128
            || SigningKeyId.Any(char.IsControl))
            throw new InvalidDataException("Signed update metadata key id is malformed.");
        if (!string.Equals(Channel, UpdateProtocol.Channel, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected signed update channel '{Channel}'.");
        if (string.IsNullOrWhiteSpace(ProductVersion))
            throw new InvalidDataException("Signed update product version is missing.");
        if ((SourceSha.Length is not (40 or 64))
            || SourceSha.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException(
                "Signed update source SHA must be the full hexadecimal commit identity.");
        if (BuildNumber <= 0)
            throw new InvalidDataException("Signed update build number must be positive.");
        if (ArtifactSize <= 0 || ArtifactSize > UpdateProtocol.MaxArtifactBytes)
            throw new InvalidDataException("Signed update artifact size is outside the allowed budget.");
        _ = UpdatePathSafety.NormalizeRelativeFilePath(ArtifactName);
        RequireSha256(ArtifactSha256, nameof(ArtifactSha256));
        RequireSha256(ProductManifestSha256, nameof(ProductManifestSha256));
        if (MinimumUpdaterProtocol < 1
            || MinimumUpdaterProtocol > UpdateProtocol.UpdaterProtocolVersion)
            throw new InvalidDataException(
                $"Signed update requires unsupported updater protocol {MinimumUpdaterProtocol}.");
        if (ExpiresUtc <= IssuedUtc)
            throw new InvalidDataException(
                "Signed update metadata expiry must be later than its issue time.");
    }

    private static void RequireSha256(string value, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"name={name}");
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException($"{name} must be a 64-character SHA-256.");
    }
}

public sealed record UpdateSignedMetadataEnvelope(
    int SchemaVersion,
    string Algorithm,
    UpdateSignedMetadataPayload Payload,
    string SignatureBase64);

public static class UpdateSignedMetadataCanonicalizer
{
    public static byte[] Canonicalize(UpdateSignedMetadataPayload payload)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"build={payload.BuildNumber}; key={payload.SigningKeyId}");
        ArgumentNullException.ThrowIfNull(payload);
        payload.ValidateShape();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", payload.SchemaVersion);
            writer.WriteString("signingKeyId", payload.SigningKeyId);
            writer.WriteString("channel", payload.Channel);
            writer.WriteString("productVersion", payload.ProductVersion);
            writer.WriteString("sourceSha", payload.SourceSha);
            writer.WriteNumber("buildNumber", payload.BuildNumber);
            writer.WriteString("artifactName", payload.ArtifactName);
            writer.WriteNumber("artifactSize", payload.ArtifactSize);
            writer.WriteString("artifactSha256", payload.ArtifactSha256);
            writer.WriteString(
                "productManifestSha256",
                payload.ProductManifestSha256);
            writer.WriteNumber(
                "minimumUpdaterProtocol",
                payload.MinimumUpdaterProtocol);
            writer.WriteString(
                "issuedUtc",
                payload.IssuedUtc.ToUniversalTime().ToString(
                    "O",
                    CultureInfo.InvariantCulture));
            writer.WriteString(
                "expiresUtc",
                payload.ExpiresUtc.ToUniversalTime().ToString(
                    "O",
                    CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}

public static class UpdateSignedMetadataSigner
{
    public static UpdateSignedMetadataEnvelope Sign(
        UpdateManifest manifest,
        string signingKeyId,
        DateTimeOffset issuedUtc,
        DateTimeOffset expiresUtc,
        ECDsa signingKey)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"build={manifest.BuildNumber}; key={signingKeyId}");
        ArgumentNullException.ThrowIfNull(signingKey);
        if (signingKey.KeySize != 256)
            throw new InvalidDataException(
                "Updater signing requires an ECDSA P-256 private key.");

        var payload = UpdateSignedMetadataPayload.FromManifest(
            manifest,
            signingKeyId,
            issuedUtc,
            expiresUtc);
        var canonical = UpdateSignedMetadataCanonicalizer.Canonicalize(payload);
        var signature = signingKey.SignData(
            canonical,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (signature.Length != 64)
            throw new CryptographicException(
                "ECDSA P-256 signer returned an unexpected signature length.");

        return new(
            UpdateProtocol.SignedMetadataSchemaVersion,
            UpdateSigningAlgorithm.EcdsaP256Sha256P1363,
            payload,
            Convert.ToBase64String(signature));
    }
}

public sealed class UpdateSignedMetadataVerifier
{
    private readonly Dictionary<string, UpdateSigningPublicKey> trustedKeys;
    private readonly Func<DateTimeOffset> utcNow;

    public UpdateSignedMetadataVerifier(
        IEnumerable<UpdateSigningPublicKey> trustedKeys,
        Func<DateTimeOffset>? utcNow = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(trustedKeys);
        var map = new Dictionary<string, UpdateSigningPublicKey>(
            StringComparer.Ordinal);
        foreach (var key in trustedKeys)
        {
            if (key is null)
                throw new InvalidDataException(
                    "Updater signing trust ring contains a null key.");
            if (string.IsNullOrWhiteSpace(key.KeyId) || !map.TryAdd(key.KeyId, key))
                throw new InvalidDataException(
                    $"Updater signing trust ring contains a duplicate or empty key id '{key.KeyId}'.");
            ValidateKeyRecord(key);
        }

        if (map.Count == 0)
            throw new InvalidDataException(
                "Updater signing trust ring must contain at least one public key.");

        this.trustedKeys = map;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public void Verify(
        UpdateSignedMetadataEnvelope envelope,
        UpdateManifest manifest,
        long currentBuildNumber)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"candidate={manifest.BuildNumber}; current={currentBuildNumber}");
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();

        if (envelope.SchemaVersion != UpdateProtocol.SignedMetadataSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported signed update envelope schema {envelope.SchemaVersion}.");
        if (!string.Equals(
                envelope.Algorithm,
                UpdateSigningAlgorithm.EcdsaP256Sha256P1363,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Unsupported updater signing algorithm '{envelope.Algorithm}'.");
        if (envelope.Payload is null)
            throw new InvalidDataException("Signed update payload is missing.");
        envelope.Payload.ValidateShape();

        var payload = envelope.Payload;
        if (!PayloadMatchesManifest(payload, manifest))
            throw new InvalidDataException(
                "Signed update metadata does not match update-manifest.json.");
        if (payload.BuildNumber <= currentBuildNumber)
            throw new InvalidDataException(
                $"Signed update build {payload.BuildNumber} does not advance current build {currentBuildNumber}.");

        var now = utcNow().ToUniversalTime();
        if (payload.IssuedUtc > now)
            throw new InvalidDataException(
                "Signed update metadata is not valid yet.");
        if (payload.ExpiresUtc <= now)
            throw new InvalidDataException(
                "Signed update metadata has expired.");

        if (!trustedKeys.TryGetValue(payload.SigningKeyId, out var trusted))
            throw new InvalidDataException(
                $"Signed update key id '{payload.SigningKeyId}' is not trusted.");
        if (payload.BuildNumber < trusted.FirstAcceptedBuild
            || (trusted.LastAcceptedBuild is long last
                && payload.BuildNumber > last))
            throw new InvalidDataException(
                $"Signing key '{trusted.KeyId}' is outside its accepted build window.");

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(envelope.SignatureBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException(
                "Signed update signature is not valid base64.",
                ex);
        }
        if (signature.Length != 64)
            throw new InvalidDataException(
                "Signed update signature must be a 64-byte P-256 P1363 signature.");

        byte[] spki;
        try
        {
            spki = Convert.FromBase64String(trusted.SubjectPublicKeyInfoBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException(
                $"Trusted updater key '{trusted.KeyId}' has invalid base64 key material.",
                ex);
        }

        using var verifier = ECDsa.Create();
        try
        {
            verifier.ImportSubjectPublicKeyInfo(spki, out var read);
            if (read != spki.Length || verifier.KeySize != 256)
                throw new InvalidDataException(
                    $"Trusted updater key '{trusted.KeyId}' is not a P-256 SPKI key.");
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException(
                $"Trusted updater key '{trusted.KeyId}' could not be imported.",
                ex);
        }

        var canonical = UpdateSignedMetadataCanonicalizer.Canonicalize(payload);
        if (!verifier.VerifyData(
                canonical,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new InvalidDataException(
                "Signed update metadata signature verification failed.");
    }

    private static bool PayloadMatchesManifest(
        UpdateSignedMetadataPayload payload,
        UpdateManifest manifest)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"build={manifest.BuildNumber}");
        return string.Equals(payload.Channel, manifest.Channel, StringComparison.Ordinal)
            && string.Equals(
                payload.ProductVersion,
                manifest.ProductVersion,
                StringComparison.Ordinal)
            && string.Equals(
                payload.SourceSha,
                manifest.SourceSha,
                StringComparison.OrdinalIgnoreCase)
            && payload.BuildNumber == manifest.BuildNumber
            && string.Equals(
                payload.ArtifactName,
                manifest.ArtifactName,
                StringComparison.Ordinal)
            && payload.ArtifactSize == manifest.ArtifactSize
            && string.Equals(
                payload.ArtifactSha256,
                manifest.Sha256,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                payload.ProductManifestSha256,
                manifest.ProductManifestSha256,
                StringComparison.OrdinalIgnoreCase)
            && payload.MinimumUpdaterProtocol == manifest.MinimumUpdaterProtocol;
    }

    private static void ValidateKeyRecord(UpdateSigningPublicKey key)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"key={key.KeyId}");
        if (!string.Equals(
                key.Algorithm,
                UpdateSigningAlgorithm.EcdsaP256Sha256P1363,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Trusted updater key '{key.KeyId}' uses unsupported algorithm '{key.Algorithm}'.");
        if (key.FirstAcceptedBuild <= 0)
            throw new InvalidDataException(
                $"Trusted updater key '{key.KeyId}' has an invalid first build.");
        if (key.LastAcceptedBuild is long last
            && last < key.FirstAcceptedBuild)
            throw new InvalidDataException(
                $"Trusted updater key '{key.KeyId}' has an invalid build window.");
        if (string.IsNullOrWhiteSpace(key.SubjectPublicKeyInfoBase64)
            || key.SubjectPublicKeyInfoBase64.Length > 4096)
            throw new InvalidDataException(
                $"Trusted updater key '{key.KeyId}' has invalid public key material.");
    }
}
