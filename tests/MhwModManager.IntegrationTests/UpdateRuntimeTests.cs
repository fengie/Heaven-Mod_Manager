using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UpdateRuntimeTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(
        UpdatePackageStager.GetUpdaterRoot(), "tests", "runtime-" + Guid.NewGuid().ToString("N"));

    public UpdateRuntimeTests()
    {
        UpdatePackageStager.EnsureUpdaterRoot(UpdatePackageStager.GetUpdaterRoot());
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task Concurrent_update_attempt_is_serialized()
    {
        var install = Path.Combine(Path.GetTempPath(), "mhwmm-mutex-" + Guid.NewGuid().ToString("N"));
        using var first = UpdateMutexLease.Acquire(install, TimeSpan.Zero);

        var error = await Task.Run(() =>
            Record.Exception(() => { using var second = UpdateMutexLease.Acquire(install, TimeSpan.Zero); }));

        Assert.IsType<IOException>(error);
    }

    [Fact]
    public async Task Global_update_lease_blocks_a_separate_process()
    {
        if (!OperatingSystem.IsWindows()) return;
        var install = Path.Combine(Path.GetTempPath(), "mhwmm-global-" + Guid.NewGuid().ToString("N"));
        using var owner = StartExternalSemaphoreOwner(GetGlobalSemaphoreName(install));
        try
        {
            Assert.Equal("READY", await owner.StandardOutput.ReadLineAsync(TestToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestToken));

            var error = Record.Exception(
                () => { using var second = UpdateMutexLease.Acquire(install, TimeSpan.Zero); });

            Assert.IsType<IOException>(error);
        }
        finally
        {
            if (!owner.HasExited)
            {
                owner.Kill(entireProcessTree: true);
                await owner.WaitForExitAsync(TestToken);
            }
        }
    }

    [Fact]
    public async Task Crashed_global_owner_does_not_permanently_block_updates()
    {
        if (!OperatingSystem.IsWindows()) return;
        var install = Path.Combine(Path.GetTempPath(), "mhwmm-crash-" + Guid.NewGuid().ToString("N"));
        using var owner = StartExternalSemaphoreOwner(GetGlobalSemaphoreName(install));
        Assert.Equal("READY", await owner.StandardOutput.ReadLineAsync(TestToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestToken));

        owner.Kill(entireProcessTree: true);
        await owner.WaitForExitAsync(TestToken);

        using var lease = UpdateMutexLease.Acquire(install, TimeSpan.Zero);
        Assert.NotNull(lease);
    }

    [Fact]
    public void Different_installations_do_not_block_each_other()
    {
        using var first = UpdateMutexLease.Acquire(
            Path.Combine(Path.GetTempPath(), "mhwmm-a-" + Guid.NewGuid().ToString("N")), TimeSpan.Zero);
        using var second = UpdateMutexLease.Acquire(
            Path.Combine(Path.GetTempPath(), "mhwmm-b-" + Guid.NewGuid().ToString("N")), TimeSpan.Zero);
    }

    [Fact]
    public void Named_object_type_collision_fails_closed()
    {
        if (!OperatingSystem.IsWindows()) return;
        var install = Path.Combine(Path.GetTempPath(), "mhwmm-collision-" + Guid.NewGuid().ToString("N"));
        using var collision = new EventWaitHandle(
            false, EventResetMode.ManualReset, GetGlobalSemaphoreName(install));

        var error = Record.Exception(
            () => { using var lease = UpdateMutexLease.Acquire(install, TimeSpan.Zero); });

        Assert.NotNull(error);
    }

    [Fact]
    public async Task Process_waiter_waits_for_real_process_exit()
    {
        using var process = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -Command Start-Sleep -Milliseconds 450")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start wait fixture.");
        var stopwatch = Stopwatch.StartNew();

        await UpdateProcessWaiter.WaitForExitAsync(process.Id, TimeSpan.FromSeconds(5), TestToken);

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(250));
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task Process_waiter_times_out_without_overwriting_running_process()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            UpdateProcessWaiter.WaitForExitAsync(
                Environment.ProcessId, TimeSpan.FromMilliseconds(100), TestToken));
    }

    [Fact]
    public async Task Startup_health_acknowledgement_records_exact_build_identity()
    {
        var health = Path.Combine(root, "health.json");
        var identity = new UpdateBuildIdentity(
            1, UpdateProtocol.Channel, "8.8.0", "abcdef1234567890", 42, DateTimeOffset.UtcNow);
        var args = new[]
        {
            UpdateHealthProtocol.TokenArgument, "token-42",
            UpdateHealthProtocol.FileArgument, health,
            UpdateHealthProtocol.AttemptArgument, "attempt-42"
        };

        await UpdateHealthProtocol.AcknowledgeIfRequestedAsync(args, identity, null, TestToken);

        var record = JsonSerializer.Deserialize<UpdateStartupHealth>(
            await File.ReadAllTextAsync(health, TestToken), UpdateProtocol.Json);
        Assert.NotNull(record);
        Assert.Equal("token-42", record.Token);
        Assert.Equal("attempt-42", record.AttemptId);
        Assert.Equal(Environment.ProcessId, record.ProcessId);
        Assert.Equal(42, record.BuildNumber);
        Assert.Equal(identity.SourceSha, record.SourceSha);
    }

    [Fact]
    public async Task Health_wait_rejects_wrong_build_and_accepts_exact_build()
    {
        var health = Path.Combine(root, "health-wait.json");
        var target = new UpdateManifest(
            1, UpdateProtocol.Channel, "8.8.0", "abcdef1234567890", 42,
            "update.zip", 1, new string('A', 64), new string('B', 64),
            "app.exe", 1, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(
            health,
            JsonSerializer.Serialize(
                new UpdateStartupHealth("token", "attempt", Environment.ProcessId, 41, target.SourceSha, DateTimeOffset.UtcNow),
                UpdateProtocol.Json),
            TestToken);

        Assert.False(await UpdateHealthProtocol.WaitForHealthyAsync(
            health, "token", "attempt", Environment.ProcessId, target, null,
            TimeSpan.FromMilliseconds(300), TestToken));

        await File.WriteAllTextAsync(
            health,
            JsonSerializer.Serialize(
                new UpdateStartupHealth("token", "attempt", Environment.ProcessId, 42, target.SourceSha, DateTimeOffset.UtcNow),
                UpdateProtocol.Json),
            TestToken);

        Assert.True(await UpdateHealthProtocol.WaitForHealthyAsync(
            health, "token", "attempt", Environment.ProcessId, target, null,
            TimeSpan.FromSeconds(1), TestToken));
    }

    [Fact]
    public async Task Health_wait_rejects_wrong_attempt_and_process_identity()
    {
        var health = Path.Combine(root, "health-identity.json");
        var target = new UpdateManifest(
            1, UpdateProtocol.Channel, "8.8.0", "abcdef1234567890", 42,
            "update.zip", 1, new string('A', 64), new string('B', 64),
            "app.exe", 1, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(
            health,
            JsonSerializer.Serialize(
                new UpdateStartupHealth(
                    "token", "attempt-a", Environment.ProcessId, 42, target.SourceSha, DateTimeOffset.UtcNow),
                UpdateProtocol.Json),
            TestToken);

        Assert.False(await UpdateHealthProtocol.WaitForHealthyAsync(
            health, "token", "attempt-b", Environment.ProcessId, target, null,
            TimeSpan.FromMilliseconds(150), TestToken));
        Assert.False(await UpdateHealthProtocol.WaitForHealthyAsync(
            health, "token", "attempt-a", Environment.ProcessId + 100000, target, null,
            TimeSpan.FromMilliseconds(150), TestToken));
    }

    [Fact]
    public async Task Launch_state_round_trip_preserves_exact_process_identity()
    {
        var request = new UpdateApplyRequest(
            new UpdateManifest(
                1, UpdateProtocol.Channel, "8.8.0", "abcdef1234567890", 42,
                "update.zip", 10, new string('A', 64), new string('B', 64),
                "app.exe", 1, DateTimeOffset.UtcNow),
            @"C:\fixture",
            Path.Combine(root, "stage"),
            Path.Combine(root, "backup"),
            Path.Combine(root, "journal"),
            Path.Combine(root, "pending"),
            Path.Combine(root, "health"),
            "token",
            1234,
            []);

        var launch = await UpdateLaunchStateStore.BeginAsync(request, TestToken);
        using var current = Process.GetCurrentProcess();
        var started = await UpdateLaunchStateStore.RecordStartedAsync(request, launch, current, TestToken);
        var loaded = await UpdateLaunchStateStore.ReadAsync(request, TestToken);

        Assert.NotNull(loaded);
        Assert.Equal(started.AttemptId, loaded.AttemptId);
        Assert.Equal(Environment.ProcessId, loaded.ProcessId);
        Assert.NotNull(loaded.ProcessStartUtc);
        using var reopened = UpdateLaunchStateStore.TryOpenTrackedProcess(loaded);
        Assert.NotNull(reopened);
        Assert.Equal(Environment.ProcessId, reopened.Id);
    }

    [Fact]
    public async Task Apply_request_round_trips_without_losing_restart_arguments()
    {
        var requestPath = Path.Combine(root, "apply-request.json");
        var request = new UpdateApplyRequest(
            new UpdateManifest(
                1, UpdateProtocol.Channel, "8.8.0", "abcdef1234567890", 42,
                "update.zip", 10, new string('A', 64), new string('B', 64),
                "app.exe", 1, DateTimeOffset.UtcNow),
            @"C:\fixture",
            Path.Combine(root, "stage"),
            Path.Combine(root, "backup"),
            Path.Combine(root, "journal"),
            Path.Combine(root, "pending"),
            Path.Combine(root, "health"),
            "token",
            1234,
            ["--one", "two words"]);

        await UpdateRequestStore.WriteAsync(requestPath, request, TestToken);
        var loaded = await UpdateRequestStore.ReadAsync(requestPath, TestToken);

        Assert.Equal(request.Manifest.BuildNumber, loaded.Manifest.BuildNumber);
        Assert.Equal(request.CurrentProcessId, loaded.CurrentProcessId);
        Assert.Equal(request.RestartArguments, loaded.RestartArguments);
    }
    private static string GetGlobalSemaphoreName(string installRoot)
    {
        var normalized = Path.GetFullPath(installRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToUpperInvariant())));
        return $"Global\\MHWMM.Update.{hash[..24]}";
    }

    private static Process StartExternalSemaphoreOwner(string name)
    {
        var escapedName = name.Replace("'", "''", StringComparison.Ordinal);
        var script =
            "$s=[System.Threading.Semaphore]::new(1,1,'" + escapedName + "');" +
            "if(-not $s.WaitOne(0)){ exit 3 };" +
            "[Console]::Out.WriteLine('READY');[Console]::Out.Flush();" +
            "Start-Sleep -Seconds 30";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        return Process.Start(start)
            ?? throw new InvalidOperationException("Could not start semaphore owner fixture.");
    }

}
