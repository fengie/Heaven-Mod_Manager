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
        var detailed=string.Equals(Environment.GetEnvironmentVariable("MOD_MANAGER_DIAGNOSTIC") ?? Environment.GetEnvironmentVariable("MHWMM_DIAGNOSTIC"),"1",StringComparison.OrdinalIgnoreCase);
        UnifiedDebugLog.Write("RUNTIME", $"Application logger initializing. StateRoot={stateRoot}; DetailedMode={detailed}");
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("app","UniversalModManager-v8.8.0")
            .Enrich.WithProperty("pid",Environment.ProcessId)
            .WriteTo.Sink(new UnifiedDebugLogSink(),LogEventLevel.Debug)
            .WriteTo.File(new Serilog.Formatting.Json.JsonFormatter(),Path.Combine(logDir,"manager-.jsonl"),restrictedToMinimumLevel:detailed?LogEventLevel.Debug:LogEventLevel.Information,rollingInterval:RollingInterval.Day,retainedFileCountLimit:14,fileSizeLimitBytes:50*1024*1024,rollOnFileSizeLimit:true,shared:false)
            .CreateLogger();
    }
}
