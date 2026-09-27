using System.Globalization;
using MhwModManager.Core;
using Serilog.Core;
using Serilog.Events;

namespace MhwModManager.Diagnostics;

/// <summary>
/// Diagnostics-layer facade over the dependency-free master log in Core.
/// </summary>
public static class UnifiedDebugLog
{
    public static string RootDirectory => MasterDebugLog.RootDirectory;
    public static string FilePath => MasterDebugLog.FilePath;
    public static void Configure(string? preferredRoot) => MasterDebugLog.Configure(preferredRoot);
    public static void Section(string area, string title) => MasterDebugLog.Section(area, title);
    public static void Write(string area, string message, Exception? exception = null) => MasterDebugLog.Write(area, message, exception);
}

public sealed class UnifiedDebugLogSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        var rendered = logEvent.RenderMessage(CultureInfo.InvariantCulture);
        var properties = logEvent.Properties.Count == 0
            ? string.Empty
            : " | " + string.Join(", ", logEvent.Properties.Select(x => $"{x.Key}={x.Value}"));
        MasterDebugLog.Write("RUNTIME", $"{logEvent.Level}: {rendered}{properties}", logEvent.Exception);
    }
}
