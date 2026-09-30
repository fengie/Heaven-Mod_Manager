using System.Diagnostics;
using MhwModManager.Core;

namespace MhwModManager.App;

internal sealed class WpfMasterTraceListener : TraceListener
{
    public override void Write(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message)) MasterDebugLog.Write("WPF-TRACE", message);
    }

    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message)) MasterDebugLog.Write("WPF-TRACE", message);
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        MasterDebugLog.Write("WPF-TRACE", $"source={source}; type={eventType}; id={id}; message={message ?? string.Empty}");
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
    {
        var safeFormat = format ?? string.Empty;
        var message = args is null ? safeFormat : string.Format(System.Globalization.CultureInfo.InvariantCulture, safeFormat, args);
        TraceEvent(eventCache, source, eventType, id, message);
    }
}

internal static class WpfMasterTracing
{
    private static readonly WpfMasterTraceListener Listener = new();
    private static int installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref installed, 1) != 0) return;
        if (!MasterDebugLog.DetailedDiagnosticsEnabled && !IsEnabled(Environment.GetEnvironmentVariable("MHW_WPF_TRACE_DETAIL")))
        {
            MasterDebugLog.Write("WPF-TRACE", "WPF PresentationTraceSources are disabled in normal mode; enable detailed diagnostics or set MHW_WPF_TRACE_DETAIL=1 to collect them.");
            return;
        }

        try
        {
            Configure(PresentationTraceSources.AnimationSource, Listener);
            Configure(PresentationTraceSources.DataBindingSource, Listener);
            Configure(PresentationTraceSources.DependencyPropertySource, Listener);
            Configure(PresentationTraceSources.DocumentsSource, Listener);
            Configure(PresentationTraceSources.FreezableSource, Listener);
            Configure(PresentationTraceSources.HwndHostSource, Listener);
            Configure(PresentationTraceSources.MarkupSource, Listener);
            Configure(PresentationTraceSources.NameScopeSource, Listener);
            Configure(PresentationTraceSources.ResourceDictionarySource, Listener);
            Configure(PresentationTraceSources.RoutedEventSource, Listener);
            Configure(PresentationTraceSources.ShellSource, Listener);
            MasterDebugLog.Write("WPF-TRACE", "Installed WPF PresentationTraceSources listeners at SourceLevels.All.");
        }
        catch (Exception ex)
        {
            MasterDebugLog.Write("WPF-TRACE", "Could not install one or more WPF internal trace listeners.", ex);
        }
    }

    private static void Configure(TraceSource source, TraceListener listener)
    {
        source.Switch.Level = SourceLevels.All;
        source.Listeners.Add(listener);
    }

    private static bool IsEnabled(string? value) =>
        value is not null && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                              || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                              || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                              || value.Equals("on", StringComparison.OrdinalIgnoreCase));
}
