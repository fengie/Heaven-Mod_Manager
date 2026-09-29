using System.IO.Compression;
using System.Text.Json;
using MhwModManager.Diagnostics;
using MhwModManager.Storage;
using Serilog;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class SupportBundlePrivacyTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root=Path.Combine(Path.GetTempPath(),"mhwmm-support-privacy-"+Guid.NewGuid().ToString("N"));

    public SupportBundlePrivacyTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try{Directory.Delete(root,true);}catch{}
    }

    [Fact]
    public async Task Support_bundle_sanitizes_exported_structured_logs_without_mutating_local_log()
    {
        var state=Path.Combine(root,"state");
        var output=Path.Combine(root,"out");
        Directory.CreateDirectory(output);
        var db=new ManagerDatabase(Path.Combine(state,"manager.db"));
        await db.InitializeAsync(TestToken);

        var logDir=Path.Combine(state,"Next","Logs");
        Directory.CreateDirectory(logDir);
        var logPath=Path.Combine(logDir,"manager-privacy-canary.jsonl");
        var logLine=JsonSerializer.Serialize(new Dictionary<string,object?>
        {
            ["Timestamp"]="2026-09-28T00:00:00Z",
            ["Level"]="Information",
            ["RenderedMessage"]=@"Path=C:\Users\Alice-Privacy-Canary\AppData\Local\MHWMM; download=https://cdn.example/file?token=URL_TOKEN_CANARY_123&mode=test",
            ["Properties"]=new Dictionary<string,object?>
            {
                ["Authorization"]="Bearer BEARER_CANARY_456",
                ["Nested"]=new Dictionary<string,object?> { ["apiKey"]="NEXUS_KEY_CANARY_789" },
                ["ManagedPath"]=@"nativePC\foo.tex"
            }
        });
        await File.WriteAllTextAsync(logPath,logLine+Environment.NewLine,TestToken);

        using var logger=new LoggerConfiguration().CreateLogger();
        var service=new SupportBundleService(db,state,new DiagnosticTelemetry(db,logger));
        var zip=await service.CreateAsync(output,TestToken);

        Assert.Contains("Alice-Privacy-Canary",await File.ReadAllTextAsync(logPath,TestToken));
        Assert.Contains("URL_TOKEN_CANARY_123",await File.ReadAllTextAsync(logPath,TestToken));
        Assert.Contains("BEARER_CANARY_456",await File.ReadAllTextAsync(logPath,TestToken));
        Assert.Contains("NEXUS_KEY_CANARY_789",await File.ReadAllTextAsync(logPath,TestToken));

        using var archive=ZipFile.OpenRead(zip);
        var environmentEntry=Assert.Single(archive.Entries,x=>x.Name=="environment.txt");
        using(var environmentReader=new StreamReader(environmentEntry.Open()))
        {
            var environmentText=await environmentReader.ReadToEndAsync(TestToken);
            var version=typeof(SupportBundleService).Assembly.GetName().Version;
            Assert.NotNull(version);
            Assert.Contains($"MHW Manual Mod Manager v{version.Major}.{version.Minor}.{version.Build} support bundle",environmentText);
            Assert.DoesNotContain("MHW Manual Mod Manager v8.3.0 support bundle",environmentText);
        }

        var logEntry=Assert.Single(archive.Entries,x=>x.Name=="manager-privacy-canary.jsonl");
        using var logReader=new StreamReader(logEntry.Open());
        var exportedLine=await logReader.ReadLineAsync(TestToken);
        Assert.NotNull(exportedLine);
        Assert.DoesNotContain("Alice-Privacy-Canary",exportedLine);
        Assert.DoesNotContain("URL_TOKEN_CANARY_123",exportedLine);
        Assert.DoesNotContain("BEARER_CANARY_456",exportedLine);
        Assert.DoesNotContain("NEXUS_KEY_CANARY_789",exportedLine);

        using var exported=JsonDocument.Parse(exportedLine);
        var rendered=exported.RootElement.GetProperty("RenderedMessage").GetString();
        Assert.NotNull(rendered);
        Assert.Contains("<absolute-path>",rendered);
        Assert.Contains("token=<redacted>",rendered);

        var properties=exported.RootElement.GetProperty("Properties");
        Assert.Equal("<redacted>",properties.GetProperty("Authorization").GetString());
        Assert.Equal("<redacted>",properties.GetProperty("Nested").GetProperty("apiKey").GetString());
        Assert.Equal(@"nativePC\foo.tex",properties.GetProperty("ManagedPath").GetString());

        var notice=Assert.Single(archive.Entries,x=>x.Name=="CONTENTS-AND-PRIVACY.txt");
        using var noticeReader=new StreamReader(notice.Open());
        var noticeText=await noticeReader.ReadToEndAsync(TestToken);
        Assert.Contains("sanitized during export",noticeText,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Review the archive before sharing",noticeText,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runtime_diagnostics_use_current_assembly_version_identity()
    {
        var state=Path.Combine(root,"versioned-logging");
        var logger=AppLogging.Create(state);
        try
        {
            logger.Information("version identity canary");
        }
        finally
        {
            if(logger is IDisposable disposable)disposable.Dispose();
        }

        var logFile=Assert.Single(Directory.EnumerateFiles(Path.Combine(state,"Next","Logs"),"manager-*.jsonl"));
        var line=File.ReadLines(logFile).Last(x=>x.Contains("version identity canary",StringComparison.Ordinal));
        using var document=JsonDocument.Parse(line);
        var version=typeof(AppLogging).Assembly.GetName().Version;
        Assert.NotNull(version);
        Assert.Equal($"UniversalModManager-v{version.Major}.{version.Minor}.{version.Build}",
            document.RootElement.GetProperty("Properties").GetProperty("app").GetString());

        var repoRoot=FindRepositoryRoot();
        var appSource=File.ReadAllText(Path.Combine(repoRoot,"src","MhwModManager.App","App.xaml.cs"));
        var loggingSource=File.ReadAllText(Path.Combine(repoRoot,"src","MhwModManager.Diagnostics","AppLogging.cs"));
        Assert.DoesNotContain("v8.8.6",appSource,StringComparison.Ordinal);
        Assert.DoesNotContain("UniversalModManager-v8.8.6",loggingSource,StringComparison.Ordinal);
        Assert.Contains("Assembly.GetName().Version",appSource,StringComparison.Ordinal);
        Assert.Contains("Assembly.GetName().Version",loggingSource,StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current=new DirectoryInfo(AppContext.BaseDirectory);
        while(current is not null)
        {
            if(File.Exists(Path.Combine(current.FullName,"MhwModManager.sln")))return current.FullName;
            current=current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
