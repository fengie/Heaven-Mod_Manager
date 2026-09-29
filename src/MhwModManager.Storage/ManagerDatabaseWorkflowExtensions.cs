using MhwModManager.Core;

namespace MhwModManager.Storage;

/// <summary>
/// Compatibility facade for selectively ported workflow services. Planner reads stay centralized
/// in PlannerSnapshotRepository rather than restoring the legacy database implementation.
/// </summary>
public static class ManagerDatabaseWorkflowExtensions
{
    public static async Task<PlannerSnapshot> LoadPlannerSnapshotAsync(
        this ManagerDatabase db,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return await new PlannerSnapshotRepository(db).LoadAsync(ct);
    }

    public static async Task<PlannerSnapshot> LoadPlannerSnapshotAsync(
        this ManagerDatabase db,
        IReadOnlyCollection<string>? fileModIds,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return await new PlannerSnapshotRepository(db).LoadAsync(fileModIds, ct);
    }
}
