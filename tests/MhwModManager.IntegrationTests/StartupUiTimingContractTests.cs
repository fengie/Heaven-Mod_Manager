using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// A process-ready stopwatch cannot replace measured time in the initial WPF
/// library read and conflict calculation. Catch missing telemetry at source
/// and measurement plumbing before any benchmark claims a startup win.
/// </summary>
public sealed class StartupUiTimingContractTests
{
    [Fact]
    public void InitialLibraryLoadAndConflictAnalysisEmitElapsedMeasurements()
    {
        var source = Read("src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs");
        Assert.Contains("STARTUP-UI-PHASE", source, StringComparison.Ordinal);
        Assert.Contains("phase=reload-mods; elapsedMs=", source, StringComparison.Ordinal);
        Assert.Contains("phase=conflict-analysis; elapsedMs=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupBenchmarkRejectsMissingPhaseDurationsInsteadOfReturningNull()
    {
        var measurement = Read("scripts", "diagnostics", "Measure-StartupPerformance.ps1");
        var parser = Read("scripts", "diagnostics", "Read-StartupUiPhase.ps1");
        var workflow = Read(".github", "workflows", "startup-performance-gate.yml");

        Assert.Contains("Read-StartupUiPhaseMs", measurement, StringComparison.Ordinal);
        Assert.Contains("reload_mods_ms = Read-StartupUiPhaseMs", measurement, StringComparison.Ordinal);
        Assert.Contains("refresh_analysis_ms = Read-StartupUiPhaseMs", measurement, StringComparison.Ordinal);
        Assert.Contains("Missing startup UI phase", parser, StringComparison.Ordinal);
        Assert.Contains("Test-StartupUiPhaseTelemetry.ps1", workflow, StringComparison.Ordinal);
    }

    private static string Read(params string[] pathParts)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MhwModManager.sln")))
            dir = dir.Parent;
        if (dir is null) throw new DirectoryNotFoundException("Repository root unavailable.");
        return File.ReadAllText(Path.Combine(dir.FullName, Path.Combine(pathParts)));
    }
}
