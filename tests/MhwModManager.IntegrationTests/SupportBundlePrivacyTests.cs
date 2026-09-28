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
}
