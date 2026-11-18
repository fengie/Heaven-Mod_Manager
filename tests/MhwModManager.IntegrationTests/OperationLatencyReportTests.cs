using MhwModManager.Diagnostics;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class OperationLatencyReportTests
{
    [Fact]
    public void Grouped_completed_latencies_compute_nearest_rank_p50_p95_and_keep_failures_separate()
    {
        var samples = Enumerable.Range(1, 20)
            .Select(i => new OperationLatencySample("automation.inbox", "completed", i * 100d, 10, 1048576))
            .Append(new OperationLatencySample("automation.inbox", "failed", 5000, 0, 0))
            .Append(new OperationLatencySample("deployment.apply", "cancelled", 9000, 0, 0));

        var rows = OperationLatencyReportService.Summarize(samples);
        var inbox = Assert.Single(rows, row => row.Operation == "automation.inbox");
        Assert.Equal(20, inbox.Completed);
        Assert.Equal(1, inbox.Failed);
        Assert.Equal(0, inbox.Cancelled);
        Assert.Equal(1000, inbox.P50Ms);
        Assert.Equal(1900, inbox.P95Ms);
        Assert.Equal(2000, inbox.MaxMs);
        Assert.Equal(1, inbox.MeanAllocatedMiB);
        Assert.Contains(rows, row => row.Operation == "deployment.apply" && row.Completed == 0 && row.Cancelled == 1);
    }

    [Fact]
    public void Unknown_resource_metrics_are_excluded_without_erasing_valid_observed_zero()
    {
        var row = Assert.Single(OperationLatencyReportService.Summarize([
            new OperationLatencySample("mods.toggle", "completed", 100, double.NaN, -1),
            new OperationLatencySample("mods.toggle", "completed", 200, 3, 1048576),
            new OperationLatencySample("mods.toggle", "completed", 300, 0, 0)
        ]));

        Assert.Equal(3, row.Completed);
        Assert.Equal(200, row.P50Ms);
        Assert.Equal(300, row.P95Ms);
        Assert.Equal(1.5, row.MeanCpuMs);
        Assert.Equal(0.5, row.MeanAllocatedMiB);

        var allUnknown = Assert.Single(OperationLatencyReportService.Summarize([
            new OperationLatencySample("metadata.sync", "completed", 250, double.NaN, -1)
        ]));
        Assert.Equal(250, allUnknown.P50Ms);
        Assert.Null(allUnknown.MeanCpuMs);
        Assert.Null(allUnknown.MeanAllocatedMiB);
    }

    [Fact]
    public async Task Missing_cpu_and_allocation_json_are_reported_as_unknown_not_zero()
    {
        var root = Path.Combine(Path.GetTempPath(), "mhwmm-latency-null-" + Guid.NewGuid().ToString("N"));
        var ct = TestContext.Current.CancellationToken;
        try
        {
            Directory.CreateDirectory(root);
            var database = new ManagerDatabase(Path.Combine(root, "manager.db"));
            await database.InitializeAsync(ct);
            await using (var connection = await database.OpenAsync(ct))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO diagnostics(time, kind, elapsed_ms, data_json) " +
                    "VALUES ($time, 'operation.completed', 250, $data)";
                command.Parameters.AddWithValue("$time",
                    DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$data", "{\"operation\":\"metadata.sync\"}");
                await command.ExecuteNonQueryAsync(ct);
            }

            var file = await new OperationLatencyReportService(database).GenerateAsync(root, ct);
            var report = await File.ReadAllTextAsync(file, ct);
            var row = Assert.Single(report.Split('\n').Where(line =>
                line.TrimStart().StartsWith("metadata.sync", StringComparison.Ordinal)));
            var columns = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("n/a", columns[^2]);
            Assert.Equal("n/a", columns[^1]);
            Assert.Contains("250.0", row, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Empty_or_nonfinite_samples_never_create_fabricated_latency_measurements()
    {
        var rows = OperationLatencyReportService.Summarize([
            new OperationLatencySample("metadata.sync", "completed", double.NaN, 0, 0),
            new OperationLatencySample("metadata.sync", "failed", 0, 0, 0)]);
        var row = Assert.Single(rows);
        Assert.Equal(0, row.Completed);
        Assert.Equal(1, row.Failed);
        Assert.Null(row.P95Ms);
        Assert.Null(row.P50Ms);
    }
}
