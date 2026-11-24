using MhwModManager.Core;

namespace MhwModManager.Updater;

/// <summary>
/// Records a local discovery hint for the last successfully started packaged
/// installation. It is never a release attestation or a runtime-health claim.
/// </summary>
public static class UpdateInstallLocationReceipt
{
    public static Task<bool> TryRecordAsync(
        string installRoot, string localAppDataRoot,
        UpdateBuildIdentity runningBuild, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Task.FromResult(false);
    }
}
