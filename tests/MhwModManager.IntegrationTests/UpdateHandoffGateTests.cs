using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UpdateHandoffGateTests
{
    [Fact]
    public void Handoff_cannot_arm_while_foreground_operation_is_active()
    {
        var gate = new UpdateHandoffGate();

        Assert.True(gate.TryBeginForeground());
        Assert.False(gate.TryArmHandoff());

        gate.EndForeground();
        Assert.True(gate.TryArmHandoff());
    }

    [Fact]
    public void Armed_handoff_blocks_new_foreground_work_until_disarmed()
    {
        var gate = new UpdateHandoffGate();

        Assert.True(gate.TryArmHandoff());
        Assert.True(gate.IsHandoffArmed);
        Assert.False(gate.TryBeginForeground());

        gate.DisarmHandoff();
        Assert.False(gate.IsHandoffArmed);
        Assert.True(gate.TryBeginForeground());
        gate.EndForeground();
    }

    [Fact]
    public void Foreground_gate_serializes_foreground_operations()
    {
        var gate = new UpdateHandoffGate();

        Assert.True(gate.TryBeginForeground());
        Assert.False(gate.TryBeginForeground());

        gate.EndForeground();
        Assert.True(gate.TryBeginForeground());
        gate.EndForeground();
    }

    [Fact]
    public void Safe_handoff_invalidates_prepared_request_when_staged_update_identity_changes()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Updater.cs"));
        var start = source.IndexOf(
            "private async Task ApplyStagedProgramUpdateWhenSafeAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf("private bool HasActiveGameProcess()", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var method = source[start..end];
        Assert.Contains("StagedUpdate? preparedFor = null;", method);
        Assert.Contains("if (!ReferenceEquals(preparedFor, staged))", method);
        Assert.Contains("preparedFor = staged;", method);
        Assert.Contains("if (!ReferenceEquals(stagedProgramUpdate, staged))", method);
        Assert.DoesNotContain("PrepareHandoffAsync(\n                    stagedProgramUpdate", method);

        var launch = method.IndexOf("LaunchHelper(prepared)", StringComparison.Ordinal);
        Assert.True(launch > 0);
        var beforeLaunch = method[..launch];
        var updateGateAcquire = beforeLaunch.LastIndexOf("await programUpdateGate.WaitAsync(ct);", StringComparison.Ordinal);
        var identityGuard = beforeLaunch.LastIndexOf("!ReferenceEquals(stagedProgramUpdate, staged)", StringComparison.Ordinal);
        Assert.True(updateGateAcquire >= 0);
        Assert.True(identityGuard > updateGateAcquire);
        Assert.Contains("programUpdateGate.Release();", method[launch..]);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MhwModManager.sln"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
