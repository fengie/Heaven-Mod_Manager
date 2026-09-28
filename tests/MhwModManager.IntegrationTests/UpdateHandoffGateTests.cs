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
}
