using MhwModManager.Core;

namespace MhwModManager.Updater;

public sealed record UpdateStorageCapacityEstimate(
    long ArtifactBytes,
    long ExtractionReserveBytes,
    long FreeSpaceHeadroomBytes,
    long RequiredAvailableBytes,
    long AvailableBytes)
{
    public bool HasCapacity
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod(
                $"available={AvailableBytes}; required={RequiredAvailableBytes}");
            return AvailableBytes >= RequiredAvailableBytes;
        }
    }
}

public static class UpdateStorageCapacityPolicy
{
    public const long MinimumExtractionReserveBytes = 256L * 1024L * 1024L;
    public const long FreeSpaceHeadroomBytes = 256L * 1024L * 1024L;
    public const int EstimatedExpansionFactor = 4;

    public static UpdateStorageCapacityEstimate Estimate(
        UpdateManifest manifest,
        long availableBytes)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"build={manifest.BuildNumber}; available={availableBytes}");
        manifest.Validate();
        if (availableBytes < 0)
            throw new ArgumentOutOfRangeException(
                nameof(availableBytes),
                "Available storage may not be negative.");

        var scaledExtraction = manifest.ArtifactSize >
                               UpdateProtocol.MaxExtractedBytes / EstimatedExpansionFactor
            ? UpdateProtocol.MaxExtractedBytes
            : manifest.ArtifactSize * EstimatedExpansionFactor;
        var extractionReserve = Math.Min(
            UpdateProtocol.MaxExtractedBytes,
            Math.Max(MinimumExtractionReserveBytes, scaledExtraction));
        var required = checked(
            manifest.ArtifactSize
            + extractionReserve
            + FreeSpaceHeadroomBytes);

        return new UpdateStorageCapacityEstimate(
            manifest.ArtifactSize,
            extractionReserve,
            FreeSpaceHeadroomBytes,
            required,
            availableBytes);
    }

    public static UpdateStorageCapacityEstimate EnsureCanStage(
        UpdateManifest manifest,
        string updaterRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"build={manifest.BuildNumber}; root={updaterRoot}");
        var fullRoot = Path.GetFullPath(updaterRoot);
        var volumeRoot = Path.GetPathRoot(fullRoot);
        if (string.IsNullOrWhiteSpace(volumeRoot))
            throw new IOException(
                $"Could not resolve the updater storage volume for '{fullRoot}'.");

        var drive = new DriveInfo(volumeRoot);
        return EnsureCanStage(manifest, drive.AvailableFreeSpace);
    }

    public static UpdateStorageCapacityEstimate EnsureCanStage(
        UpdateManifest manifest,
        long availableBytes)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"build={manifest.BuildNumber}; available={availableBytes}");
        var estimate = Estimate(manifest, availableBytes);
        if (!estimate.HasCapacity)
        {
            throw new IOException(
                "Not enough free disk space to stage the program update safely. "
                + $"Available={estimate.AvailableBytes} bytes; "
                + $"required={estimate.RequiredAvailableBytes} bytes "
                + $"(artifact={estimate.ArtifactBytes}, "
                + $"extraction reserve={estimate.ExtractionReserveBytes}, "
                + $"headroom={estimate.FreeSpaceHeadroomBytes}). "
                + "Use Settings > Storage to reclaim disposable updater storage "
                + "or free space, then try again.");
        }

        return estimate;
    }
}
