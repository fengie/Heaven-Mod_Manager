using System.Diagnostics;
using Xunit;

namespace MhwModManager.IntegrationTests;

// The xUnit collection prevents concurrency inside one test process, but the
// feature and publication CI workflows can run in different processes under
// the same Windows user. They share the real updater's pending-update.json.
[CollectionDefinition("Updater global state", DisableParallelization = true)]
public sealed class UpdaterGlobalStateFixture : ICollectionFixture<UpdaterGlobalStateProcessLease>
{
    public const string Name = "Updater global state";
}

/// <summary>
/// Holds an OS-level exclusive file share across all tests in the updater
/// collection. File handles are released by the OS if a test process crashes,
/// unlike a persistent lock flag, so a dead runner cannot wedge the next run.
/// </summary>
public sealed class UpdaterGlobalStateProcessLease : IDisposable
{
    private readonly FileStream stream;

    public UpdaterGlobalStateProcessLease()
        : this(Acquire(
            Path.Combine(Path.GetTempPath(), "mhwmm-updater-global-integration.lock"),
            TimeSpan.FromMinutes(12)))
    {
    }

    private UpdaterGlobalStateProcessLease(FileStream heldStream) => stream = heldStream;

    public static FileStream Acquire(string path, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
            {
                if (elapsed.Elapsed >= timeout)
                    throw new TimeoutException(
                        "Another Windows updater integration run holds the exclusive updater-state test lease.", ex);
                Thread.Sleep(200);
            }
        }
    }

    public void Dispose()
    {
        stream.Dispose();
        GC.SuppressFinalize(this);
    }
}
