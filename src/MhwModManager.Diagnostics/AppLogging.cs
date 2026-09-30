using MhwModManager.Core;
using Serilog;
using Serilog.Events;

namespace MhwModManager.Diagnostics;

public static class AppLogging
{
    public static ILogger Create(string stateRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var logDir=Path.Combine(stateRoot,"Next","Logs");
        Directory.CreateDirectory(logDir);
        var detailed=MasterDebugLog.DetailedDiagnosticsEnabled;
        UnifiedDebugLog.Write("RUNTIME", $"Application logger initializing. StateRoot={stateRoot}; DetailedMode={detailed}");
        var version=typeof(AppLogging).Assembly.GetName().Version;
        var appIdentity=version is null?"UniversalModManager-vunknown":$"UniversalModManager-v{version.Major}.{version.Minor}.{version.Build}";
        return new LoggerConfiguration()
            .MinimumLevel.Is(detailed ? LogEventLevel.Debug : LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("app",appIdentity)
            .Enrich.WithProperty("pid",Environment.ProcessId)
            .WriteTo.Sink(new UnifiedDebugLogSink(),detailed ? LogEventLevel.Debug : LogEventLevel.Information)
            .WriteTo.File(new Serilog.Formatting.Json.JsonFormatter(),Path.Combine(logDir,"manager-.jsonl"),restrictedToMinimumLevel:detailed?LogEventLevel.Debug:LogEventLevel.Information,rollingInterval:RollingInterval.Day,retainedFileCountLimit:14,fileSizeLimitBytes:50*1024*1024,rollOnFileSizeLimit:true,shared:false)
            .CreateLogger();
    }
}
