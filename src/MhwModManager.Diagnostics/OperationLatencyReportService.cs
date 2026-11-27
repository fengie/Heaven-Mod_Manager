using System.Globalization;
using System.Text;
using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Diagnostics;

// Missing CPU and allocation evidence is represented by NaN and -1 respectively.
// A genuine observed zero is retained as a measurement, never a missing-data default.
public sealed record OperationLatencySample(string Operation, string Outcome, double ElapsedMs,
    double CpuMs, long AllocatedBytes);

public sealed record OperationLatencyRow(string Operation, int Completed, int Failed, int Cancelled,
    double? P50Ms, double? P95Ms, double? MaxMs, double? MeanCpuMs, double? MeanAllocatedMiB)
{
    public int Timed { get; init; }
}

/// <summary>
/// Read-only summary of bounded, already-recorded foreground operation timings.
/// No installed mod files, network requests, or global performance counters are collected.
/// </summary>
public sealed class OperationLatencyReportService(ManagerDatabase database)
{
    private const int MaxSamples = 10000;

    public async Task<string> GenerateAsync(string reportDirectory, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-7);
        var samples = new List<OperationLatencySample>();
        var malformed = 0;
        await using (var connection = await database.OpenAsync(ct))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT kind,
                       CASE WHEN typeof(elapsed_ms) IN ('integer', 'real') THEN elapsed_ms END,
                       data_json FROM diagnostics
                WHERE time >= $since AND kind IN ('operation.completed','operation.failed','operation.cancelled')
                ORDER BY time DESC LIMIT 10000
                """;
            command.Parameters.AddWithValue("$since", since.ToString("O", CultureInfo.InvariantCulture));
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(2)) { malformed++; continue; }
                try
                {
                    using var doc = JsonDocument.Parse(reader.GetString(2));
                    var data = doc.RootElement;
                    if (data.ValueKind != JsonValueKind.Object ||
                        !data.TryGetProperty("operation", out var operationProperty) ||
                        operationProperty.ValueKind != JsonValueKind.String)
                    {
                        malformed++;
                        continue;
                    }
                    var operation = operationProperty.GetString();
                    if (string.IsNullOrWhiteSpace(operation)) { malformed++; continue; }
                    var elapsed = reader.IsDBNull(1) ? double.NaN : reader.GetDouble(1);
                    var cpu = GetDouble(data, "cpuMs");
                    // The producer emits an Int64 byte counter. Do not round-trip
                    // it through double (Int64.MaxValue rounds up to 2^63).
                    var allocated = data.TryGetProperty("allocatedBytesDelta", out var bytes)
                        && bytes.ValueKind == JsonValueKind.Number
                        && bytes.TryGetInt64(out var count) && count >= 0 ? count : -1;
                    samples.Add(new OperationLatencySample(operation,
                        reader.GetString(0)["operation.".Length..],
                        elapsed, cpu, allocated));
                }
                catch (JsonException) { malformed++; }
            }
        }

        var rows = Summarize(samples);
        var output = new StringBuilder();
        output.AppendLine("UNIVERSAL MOD MANAGER — OPERATION LATENCY REPORT");
        output.AppendLine(FormattableString.Invariant($"Generated UTC: {now:O}"));
        output.AppendLine(FormattableString.Invariant($"Window: last 7 days; at most {MaxSamples:N0} latest operation records"));
        output.AppendLine(FormattableString.Invariant($"Read records: {samples.Count}; malformed/ignored: {malformed}"));
        output.AppendLine("DONE counts completed operations; TIMED counts those with a finite nonnegative duration. Only TIMED contributes to latency quantiles.");
        output.AppendLine("p50/p95 use nearest-rank statistics; small samples are not reliable performance budgets.");
        output.AppendLine("Actual UI responsiveness, input latency and real RAR throughput require separate measurements.");
        output.AppendLine();
        output.AppendLine(FormattableString.Invariant($"{"OPERATION",-34} {"DONE",5} {"TIMED",5} {"FAIL",5} {"CANCEL",6} {"P50 ms",10} {"P95 ms",10} {"MAX ms",10} {"CPU ms",10} {"ALLOC MiB",10}"));
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            output.AppendLine(
                FormattableString.Invariant($"{row.Operation,-34} {row.Completed,5} {row.Timed,5} {row.Failed,5} {row.Cancelled,6} ") +
                FormattableString.Invariant($"{Format(row.P50Ms),10} {Format(row.P95Ms),10} {Format(row.MaxMs),10} ") +
                FormattableString.Invariant($"{Format(row.MeanCpuMs),10} {Format(row.MeanAllocatedMiB),10}"));
        }
        if (!rows.Any(row => row.Timed > 0))
            output.AppendLine("No timed completed operations in this window. No latency comparison is available.");
        Directory.CreateDirectory(reportDirectory);
        var path = Path.Combine(reportDirectory, $"operation-latency-{now:yyyyMMdd-HHmmss}.txt");
        await File.WriteAllTextAsync(path, output.ToString(), ct);
        MasterDebugLog.Write("PERFORMANCE-REPORT",
            $"Generated operation latency summary: operations={rows.Count}; records={samples.Count}; ignored={malformed}");
        return path;
    }

    public static IReadOnlyList<OperationLatencyRow> Summarize(IEnumerable<OperationLatencySample> samples)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(samples);
        return samples.Where(s => !string.IsNullOrWhiteSpace(s.Operation) &&
                (s.Outcome is "completed" or "failed" or "cancelled"))
            .GroupBy(s => s.Operation, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var completed = group.Where(s => s.Outcome == "completed").ToArray();
                var elapsed = completed.Select(s => s.ElapsedMs)
                    .Where(v => double.IsFinite(v) && v >= 0).OrderBy(v => v).ToArray();
                var validCpu = completed.Where(s => double.IsFinite(s.CpuMs) && s.CpuMs >= 0)
                    .Select(s => s.CpuMs).ToArray();
                var validAllocations = completed.Where(s => s.AllocatedBytes >= 0)
                    .Select(s => s.AllocatedBytes).ToArray();
                return new OperationLatencyRow(group.Key, completed.Length,
                    group.Count(s => s.Outcome == "failed"),
                    group.Count(s => s.Outcome == "cancelled"),
                    Percentile(elapsed, .50), Percentile(elapsed, .95),
                    elapsed.Length > 0 ? elapsed[^1] : null,
                    validCpu.Length > 0 ? validCpu.Average() : null,
                    validAllocations.Length > 0 ? validAllocations.Average(v => (double)v) / 1048576d : null)
                { Timed = elapsed.Length };
            })
            .OrderByDescending(row => row.P95Ms ?? -1)
            .ThenBy(row => row.Operation, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static double? Percentile(double[] sortedValues, double percentile)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return sortedValues.Length == 0 ? null :
            sortedValues[Math.Clamp((int)Math.Ceiling(sortedValues.Length * percentile) - 1, 0, sortedValues.Length - 1)];
    }

    private static double GetDouble(JsonElement element, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out var number) && double.IsFinite(number)
            ? number : double.NaN;
    }

    private static string Format(double? number)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return number.HasValue ? number.Value.ToString("F1", CultureInfo.InvariantCulture) : "n/a";
    }
}
