using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class FeatureVerificationMatrixTests
{
    [Fact]
    public void EveryRegisteredFeatureReferencesRealRepositoryFiles()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "docs", "verification", "feature-test-matrix.json")));
        var data = document.RootElement;
        Assert.Equal(1, data.GetProperty("schemaVersion").GetInt32());
        var features = data.GetProperty("features").EnumerateArray().ToArray();
        Assert.True(features.Length >= 15, "The inventory must cover the application's major subsystems.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in features)
        {
            var id = feature.GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.True(seen.Add(id!), $"Duplicate feature ID: {id}");
            var risk = feature.GetProperty("risk").GetString();
            Assert.True(risk is "critical" or "important", $"Invalid risk: {risk}");

            var source = RequiredPaths(root, feature, "productionPaths");
            var unit = RequiredPaths(root, feature, "unitTests");
            var integration = RequiredPaths(root, feature, "integrationTests");
            RequiredPaths(root, feature, "benchmarks");
            Assert.True(source > 0, $"Feature {id} has no production source mapping.");
            if (risk == "critical")
                Assert.True(unit + integration > 0, $"Critical feature lacks any mapped tests: {id}");
            Assert.Equal("unverified", feature.GetProperty("installedAcceptance").GetString());
            Assert.NotEmpty(feature.GetProperty("missingAcceptance").EnumerateArray());
        }
    }

    [Fact]
    public void CoverageHarnessIsExplicitlyInvokedAndPreservesDistinctSuiteResults()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows",
            "verification-code-coverage.yml"));
        var script = File.ReadAllText(Path.Combine(root, "scripts", "testing",
            "Measure-ProjectCoverage.ps1"));
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("schedule:", workflow, StringComparison.Ordinal);
        Assert.Contains("github.sha", workflow, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Testing.Extensions.CodeCoverage",
            File.ReadAllText(Path.Combine(root, "Directory.Packages.props")), StringComparison.Ordinal);
        Assert.Contains("MhwModManager.Tests.csproj", script, StringComparison.Ordinal);
        Assert.Contains("MhwModManager.AutomationTests.csproj", script, StringComparison.Ordinal);
        Assert.Contains("MhwModManager.IntegrationTests.csproj", script, StringComparison.Ordinal);
        Assert.Contains("'--coverage'", script, StringComparison.Ordinal);
        Assert.Contains("'cobertura'", script, StringComparison.Ordinal);
        Assert.Contains("Coverage was requested but produced no Cobertura XML", script, StringComparison.Ordinal);
        Assert.Contains("is NOT overall combined coverage", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FeatureLineEvidenceDeduplicatesSourcesAndRejectsAmbiguousEvidence()
    {
        var root = FindRepositoryRoot();
        var token = TestContext.Current.CancellationToken;
        var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(shell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(Path.Combine(root, "scripts", "testing", "Test-FeatureLineCoverage.ps1"));
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync(token);
        var errors = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        var actual = await output;
        var error = await errors;
        Assert.True(process.ExitCode == 0, actual + Environment.NewLine + error);
        Assert.Contains("PASS: Feature line evidence", actual, StringComparison.Ordinal);
    }

    private static int RequiredPaths(string root, JsonElement feature, string property)
    {
        var count = 0;
        foreach (var file in feature.GetProperty(property).EnumerateArray())
        {
            var relative = file.GetString();
            Assert.False(string.IsNullOrWhiteSpace(relative));
            Assert.False(Path.IsPathRooted(relative!));
            Assert.DoesNotContain("..", relative!, StringComparison.Ordinal);
            var resolved = Path.GetFullPath(Path.Combine(root,
                relative!.Replace('/', Path.DirectorySeparatorChar)));
            Assert.StartsWith(root + Path.DirectorySeparatorChar,
                resolved, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(resolved), $"Missing {property}: {relative}");
            count++;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null)
        {
            if (File.Exists(Path.Combine(folder.FullName, "MhwModManager.sln"))) return folder.FullName;
            folder = folder.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find repository root for feature matrix.");
    }
}
