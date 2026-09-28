using System.IO.Compression;
using System.Text.Json;
using MhwModManager.Diagnostics;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class SupportBundlePrivacyTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-support-privacy-" + Guid.NewGuid().ToString("N"));

    public SupportBundlePrivacyTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task Support_bundle_sanitizes_structured_logs_before_sharing()
    {
        var stateRoot = Path.Combine(root, "state");
        var output = Path.Combine(root, "output");
        var db = new ManagerDatabase(Path.Combine(stateRoot, "manager.db"));
        await db.InitializeAsync(TestToken);

        var logDir = Path.Combine(stateRoot, "Next", "Logs");
        Directory.CreateDirectory(logDir);
        const string userPathCanary = @"C:\Users\Alice-Privacy-Canary\Private Mods\secret.mod";
        const string bearerCanary = "BEARER_CANARY_53CE90E4";
        const string urlTokenCanary = "URL_TOKEN_CANARY_887DFE31";
        var statePathCanary = Path.Combine(stateRoot, "Next", "Logs", "private.log");
        var remoteUrl = $"https://cdn.example/file.png?token={urlTokenCanary}&size=large";

        var line = JsonSerializer.Serialize(new
        {
            Level = "Information",
            RenderedMessage =
                $"Open {userPathCanary}; state={statePathCanary}; Authorization: Bearer {bearerCanary}; preview={remoteUrl}",
            Properties = new
            {
                UserPath = userPathCanary,
                StatePath = statePathCanary,
                Authorization = $"Bearer {bearerCanary}",
                PreviewUrl = remoteUrl,
                Nested = new { Items = new[] { userPathCanary, remoteUrl } },
                SafeFact = "archive-import"
            }
        });
        var localLog = Path.Combine(logDir, "manager-20260928.jsonl");
        await File.WriteAllTextAsync(localLog, line, TestToken);

        var telemetry = new DiagnosticTelemetry(db, Serilog.Log.Logger);
        var zipPath = await new SupportBundleService(db, stateRoot, telemetry).CreateAsync(output, TestToken);

        using var zip = ZipFile.OpenRead(zipPath);
        var logEntry = Assert.Single(zip.Entries, entry => entry.Name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase));
        await using var stream = logEntry.Open();
        using var reader = new StreamReader(stream);
        var exportedLine = await reader.ReadToEndAsync(TestToken);
        using var json = JsonDocument.Parse(exportedLine);

        var message = json.RootElement.GetProperty("RenderedMessage").GetString()!;
        var properties = json.RootElement.GetProperty("Properties");
        var userPath = properties.GetProperty("UserPath").GetString()!;
        var statePath = properties.GetProperty("StatePath").GetString()!;
        var authorization = properties.GetProperty("Authorization").GetString()!;
        var previewUrl = properties.GetProperty("PreviewUrl").GetString()!;
        var nestedItems = properties.GetProperty("Nested").GetProperty("Items");

        Assert.DoesNotContain("Alice-Privacy-Canary", exportedLine, StringComparison.Ordinal);
        Assert.DoesNotContain(stateRoot, exportedLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(bearerCanary, exportedLine, StringComparison.Ordinal);
        Assert.DoesNotContain(urlTokenCanary, exportedLine, StringComparison.Ordinal);

        Assert.Contains("<USER_PROFILE>", userPath, StringComparison.Ordinal);
        Assert.Contains("<STATE_ROOT>", statePath, StringComparison.Ordinal);
        Assert.Contains("Authorization: <redacted>", message, StringComparison.Ordinal);
        Assert.Equal("<redacted>", authorization);
        Assert.Contains("cdn.example/file.png?token=<redacted>&size=large", previewUrl, StringComparison.Ordinal);
        Assert.Contains("<USER_PROFILE>", nestedItems[0].GetString()!, StringComparison.Ordinal);
        Assert.Contains("token=<redacted>", nestedItems[1].GetString()!, StringComparison.Ordinal);
        Assert.Equal("archive-import", properties.GetProperty("SafeFact").GetString());

        var localLine = await File.ReadAllTextAsync(localLog, TestToken);
        using var localJson = JsonDocument.Parse(localLine);
        var localMessage = localJson.RootElement.GetProperty("RenderedMessage").GetString()!;
        Assert.Contains(userPathCanary, localMessage, StringComparison.Ordinal);
        Assert.Contains(bearerCanary, localMessage, StringComparison.Ordinal);
        Assert.Contains(urlTokenCanary, localMessage, StringComparison.Ordinal);
    }
}
