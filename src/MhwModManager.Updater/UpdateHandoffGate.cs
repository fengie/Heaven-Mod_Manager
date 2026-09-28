using MhwModManager.Core;

namespace MhwModManager.Updater;

public sealed class UpdateHandoffGate
{
    private readonly object sync = new();
    private bool foregroundActive;
    private bool handoffArmed;

    public bool TryBeginForeground()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        lock (sync)
        {
            if (foregroundActive || handoffArmed) return false;
            foregroundActive = true;
            return true;
        }
    }

    public void EndForeground()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        lock (sync)
        {            if (!foregroundActive)
                throw new InvalidOperationException(
                    "No foreground updater operation gate is active.");
            foregroundActive = false;
        }
    }

    public bool TryArmHandoff()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        lock (sync)
        {
            if (foregroundActive || handoffArmed) return false;
            handoffArmed = true;
            return true;
        }
    }

    public void DisarmHandoff()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        lock (sync)
        {
            if (!handoffArmed) return;
            handoffArmed = false;
        }
    }

    public bool IsHandoffArmed
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            lock (sync) return handoffArmed;
        }
    }
}
