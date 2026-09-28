using System.Diagnostics;
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
}
