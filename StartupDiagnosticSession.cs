using MhwModManager.Core;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MhwModManager.Diagnostics;

public enum StartupDiagnosticStatus { Info, Running, Passed, Failed, Skipped }

public sealed record StartupDiagnosticEntry(
    string Name,
    StartupDiagnosticStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double Milliseconds,
    string? Detail,
    string? ExceptionType,
    string? ExceptionMessage,
    string? ExceptionDetails);

public sealed class StartupDiagnosticSession
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object gate = new();
    private readonly List<StartupDiagnosticEntry> entries = [];
    private readonly DateTimeOffset startedAt = DateTimeOffset.Now;
    private bool completed;

    private StartupDiagnosticSession(string logDirectory, string stamp)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(logDirectory);
        UnifiedDebugLog.Configure(Directory.GetParent(logDirectory)?.FullName ?? logDirectory);
        TextLogPath = Path.Combine(logDirectory, $"startup-{stamp}.log");
        JsonReportPath = Path.Combine(logDirectory, $"startup-{stamp}.json");
        AppendLine($"Universal Mod Manager startup diagnostics\nStarted: {startedAt.ToString("O", CultureInfo.InvariantCulture)}\nPID: {Environment.ProcessId}\nOS: {Environment.OSVersion}\n.NET: {Environment.Version}\nBase directory: {AppContext.BaseDirectory}\n");
        PersistReport("RUNNING", null);
    }

    public string TextLogPath { get; }
    public string JsonReportPath { get; }

    public static StartupDiagnosticSession Start(string? preferredRoot = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var root = string.IsNullOrWhiteSpace(preferredRoot) ? AppContext.BaseDirectory : preferredRoot;
        var preferred = Path.Combine(root, "StartupLogs");
        try
        {
            Directory.CreateDirectory(preferred);
            return new(preferred, stamp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MhwModManager", "StartupLogs");
            Directory.CreateDirectory(fallback);
            var session = new StartupDiagnosticSession(fallback, stamp);
            session.Info("bootstrap.log-directory-fallback", $"Could not use '{preferred}': {ex.Message}. Using '{fallback}'.");
            return session;
        }
    }

    public void Info(string name, string? detail = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var now = DateTimeOffset.Now;
        AddEntry(new(name, StartupDiagnosticStatus.Info, now, now, 0, detail, null, null, null));
    }

    public void Run(string name, Action action, string? detail = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(action);
        Run(name, () =>
        {
            action();
            return true;
        }, detail);
    }

    public T Run<T>(string name, Func<T> action, string? detail = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(action);
        var started = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();
        WriteStart(name, detail);
        try
        {
            var result = action();
            sw.Stop();
            AddEntry(new(name, StartupDiagnosticStatus.Passed, started, DateTimeOffset.Now, sw.Elapsed.TotalMilliseconds, detail, null, null, null));
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            AddFailure(name, started, sw.Elapsed, detail, ex);
            throw;
        }
    }

    public async Task RunAsync(string name, Func<CancellationToken, Task> action, string? detail = null, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(action);
        var started = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();
        WriteStart(name, detail);
        try
        {
            await action(ct).ConfigureAwait(false);
            sw.Stop();
            AddEntry(new(name, StartupDiagnosticStatus.Passed, started, DateTimeOffset.Now, sw.Elapsed.TotalMilliseconds, detail, null, null, null));
        }
        catch (Exception ex)
        {
            sw.Stop();
            AddFailure(name, started, sw.Elapsed, detail, ex);
            throw;
        }
    }

    public async Task<T> RunAsync<T>(string name, Func<CancellationToken, Task<T>> action, string? detail = null, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(action);
        var started = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();
        WriteStart(name, detail);
        try
        {
            var result = await action(ct).ConfigureAwait(false);
            sw.Stop();
            AddEntry(new(name, StartupDiagnosticStatus.Passed, started, DateTimeOffset.Now, sw.Elapsed.TotalMilliseconds, detail, null, null, null));
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            AddFailure(name, started, sw.Elapsed, detail, ex);
            throw;
        }
    }

    public void RecordFailure(string name, Exception ex, string? detail = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(ex);
        var now = DateTimeOffset.Now;
        AddFailure(name, now, TimeSpan.Zero, detail, ex);
    }

    public void Complete(bool success, string? summary = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        lock (gate)
        {
            if (completed) return;
            completed = true;
            var finished = DateTimeOffset.Now;
            AppendLine($"\nStartup {(success ? "PASSED" : "FAILED")} at {finished.ToString("O", CultureInfo.InvariantCulture)} ({(finished - startedAt).TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)} ms).{(string.IsNullOrWhiteSpace(summary) ? string.Empty : " " + summary)}");
            PersistReport(success ? "PASS" : "FAIL", summary);
        }
    }

    private void WriteStart(string name, string? detail)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        lock (gate)
        {
            AppendLine($"{Timestamp()} START {name}{FormatDetail(detail)}");
            PersistReport("RUNNING", null);
        }
    }

    private void AddFailure(string name, DateTimeOffset started, TimeSpan elapsed, string? detail, Exception ex)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        AddEntry(new(name, StartupDiagnosticStatus.Failed, started, DateTimeOffset.Now, elapsed.TotalMilliseconds, detail, ex.GetType().FullName, ex.Message, ex.ToString()));
    }

    private void AddEntry(StartupDiagnosticEntry entry)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        lock (gate)
        {
            entries.Add(entry);
            var duration = entry.Milliseconds <= 0 ? string.Empty : $" ({entry.Milliseconds.ToString("F1", CultureInfo.InvariantCulture)} ms)";
            AppendLine($"{Timestamp()} {entry.Status.ToString().ToUpperInvariant(),-7} {entry.Name}{duration}{FormatDetail(entry.Detail)}");
            if (!string.IsNullOrWhiteSpace(entry.ExceptionDetails))
            {
                AppendLine(Indent(entry.ExceptionDetails));
            }
            PersistReport(completed ? "COMPLETE" : "RUNNING", null);
        }
    }

    private void PersistReport(string overall, string? summary)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var report = new
            {
                generatedAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                startedAt = startedAt.ToString("O", CultureInfo.InvariantCulture),
                overall,
                summary,
                processId = Environment.ProcessId,
                baseDirectory = AppContext.BaseDirectory,
                machine = Environment.MachineName,
                os = Environment.OSVersion.ToString(),
                dotnet = Environment.Version.ToString(),
                textLog = TextLogPath,
                entries = entries.ToArray()
            };
            var temp = JsonReportPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(report, JsonOptions), Encoding.UTF8);
            File.Move(temp, JsonReportPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            try { AppendLine($"{Timestamp()} WARNING Could not persist JSON startup report: {ex.Message}"); } catch { }
        }
    }

    private void AppendLine(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            File.AppendAllText(TextLogPath, value + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            UnifiedDebugLog.Write("STARTUP-LOG-WRITE", $"Could not append dedicated startup log: {TextLogPath}", ex);
        }
        UnifiedDebugLog.Write("STARTUP", value);
    }

    private static string Timestamp()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
    }
    private static string FormatDetail(string? detail)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return string.IsNullOrWhiteSpace(detail) ? string.Empty : " :: " + detail;
    }
    private static string Indent(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return string.Join(Environment.NewLine, value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => "    " + line));
    }
}
