using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UpdaterStorageCapacityPolicyTests
{
    [Fact]
    public void Capacity_estimate_reserves_artifact_expansion_and_headroom()
    {
        var manifest = Manifest(artifactSize: 64L * 1024L * 1024L);

        var estimate = UpdateStorageCapacityPolicy.Estimate(
            manifest,
            availableBytes: long.MaxValue / 4);

        Assert.Equal(manifest.ArtifactSize, estimate.ArtifactBytes);
        Assert.True(
            estimate.ExtractionReserveBytes
            >= UpdateStorageCapacityPolicy.MinimumExtractionReserveBytes);
        Assert.Equal(
            UpdateStorageCapacityPolicy.FreeSpaceHeadroomBytes,
            estimate.FreeSpaceHeadroomBytes);
        Assert.Equal(
            estimate.ArtifactBytes
            + estimate.ExtractionReserveBytes
            + estimate.FreeSpaceHeadroomBytes,
            estimate.RequiredAvailableBytes);
        Assert.True(estimate.HasCapacity);
    }

    [Fact]
    public void Capacity_preflight_rejects_before_download_when_one_byte_short()
    {
        var manifest = Manifest(artifactSize: 128L * 1024L * 1024L);
        var required = UpdateStorageCapacityPolicy.Estimate(
            manifest,
            availableBytes: long.MaxValue / 4).RequiredAvailableBytes;

        var ex = Assert.Throws<IOException>(() =>
            UpdateStorageCapacityPolicy.EnsureCanStage(
                manifest,
                availableBytes: required - 1));

        Assert.Contains(
            "Not enough free disk space",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Settings > Storage",
            ex.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Capacity_preflight_accepts_exact_required_space()
    {
        var manifest = Manifest(artifactSize: 32L * 1024L * 1024L);
        var required = UpdateStorageCapacityPolicy.Estimate(
            manifest,
            availableBytes: long.MaxValue / 4).RequiredAvailableBytes;

        var estimate = UpdateStorageCapacityPolicy.EnsureCanStage(
            manifest,
            availableBytes: required);

        Assert.True(estimate.HasCapacity);
        Assert.Equal(required, estimate.AvailableBytes);
    }

    [Fact]
    public void Capacity_estimate_caps_expansion_at_extraction_resource_budget()
    {
        var manifest = Manifest(UpdateProtocol.MaxArtifactBytes);

        var estimate = UpdateStorageCapacityPolicy.Estimate(
            manifest,
            availableBytes: long.MaxValue / 4);

        Assert.Equal(
            UpdateProtocol.MaxExtractedBytes,
            estimate.ExtractionReserveBytes);
    }

    private static UpdateManifest Manifest(long artifactSize) =>
        new(
            UpdateProtocol.ManifestSchemaVersion,
            UpdateProtocol.Channel,
            "8.8.86",
            "abcdef0123456789abcdef0123456789abcdef01",
            999,
            "release.zip",
            artifactSize,
            new string('A', 64),
            new string('B', 64),
            "MHW Mod Manager.exe",
            UpdateProtocol.UpdaterProtocolVersion,
            DateTimeOffset.UtcNow);
}
