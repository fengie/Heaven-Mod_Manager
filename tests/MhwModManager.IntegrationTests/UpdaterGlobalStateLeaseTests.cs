using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>Regression: distinct CI processes must not simultaneously mutate the
/// updater's per-user pending update and transaction state.</summary>
public sealed class UpdaterGlobalStateLeaseTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "mhwmm-updater-test-lease-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Exclusive_file_lease_prevents_a_second_writer_until_disposed()
    {
        var path = Path.Combine(root, "state.lock");
        using (var first = UpdaterGlobalStateProcessLease.Acquire(path, TimeSpan.Zero))
        {
            Assert.Throws<TimeoutException>(() =>
                UpdaterGlobalStateProcessLease.Acquire(path, TimeSpan.Zero));
        }

        using var successor = UpdaterGlobalStateProcessLease.Acquire(path, TimeSpan.Zero);
        Assert.True(successor.CanRead);
    }

    [Fact]
    public void Timeout_is_bounded_and_does_not_delete_another_owners_lease()
    {
        var path = Path.Combine(root, "retry.lock");
        using var owner = UpdaterGlobalStateProcessLease.Acquire(path, TimeSpan.Zero);
        Assert.Throws<TimeoutException>(() =>
            UpdaterGlobalStateProcessLease.Acquire(path, TimeSpan.FromMilliseconds(250)));

        Assert.True(File.Exists(path));
        Assert.True(owner.CanWrite);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
