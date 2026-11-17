using MhwModManager.Diagnostics;
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
