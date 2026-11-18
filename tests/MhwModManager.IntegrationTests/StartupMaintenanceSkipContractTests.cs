using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class StartupMaintenanceSkipContractTests
{
    [Fact]
    public void SkipControlIsOffDuringMandatoryRecoveryAndScopedToOptionalAutomation()
    {
        var app = Read("src", "MhwModManager.App", "App.xaml.cs");
        var skipEnable = app.IndexOf("optionalSkipVisibility?.Invoke(true)", StringComparison.Ordinal);
        var recovery = app.IndexOf("\"startup.recovery\"", StringComparison.Ordinal);
        var automation = app.IndexOf("\"startup.automation\"", StringComparison.Ordinal);
        var skipDisable = app.IndexOf("optionalSkipVisibility?.Invoke(false)", StringComparison.Ordinal);
        Assert.True(recovery >= 0 && skipEnable > recovery);
        Assert.True(automation > skipEnable && skipDisable > automation);
        Assert.Contains("optionalSkipRequested", app, StringComparison.Ordinal);
    }

    [Fact]
    public void SplashOnlyOffersSafeAfterCurrentStageSkipAndReportsThatItIsPending()
    {
        var splash = Read("src", "MhwModManager.App", "StartupWindow.cs");
        Assert.Contains("Finish current stage, then open app", splash, StringComparison.Ordinal);
        Assert.Contains("Visibility.Collapsed", splash, StringComparison.Ordinal);
        Assert.Contains("SkipRequested", splash, StringComparison.Ordinal);
        Assert.Contains("SetMaintenanceSkipVisible", splash, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinatorDefersNonessentialStagesWhenRequested()
    {
        var coordinator = Read("src", "MhwModManager.Automation", "AutomationCoordinator.cs");
        Assert.Contains("shouldSkip", coordinator, StringComparison.Ordinal);
        Assert.Contains("Deferred remaining automatic maintenance", coordinator, StringComparison.Ordinal);
        Assert.Contains("inbox.ProcessAsync(inboxProgress, refreshMetadata: false,", coordinator, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MhwModManager.sln")))
            dir = dir.Parent;
        if (dir is null) throw new DirectoryNotFoundException("Repository root unavailable.");
        return File.ReadAllText(Path.Combine(dir.FullName, Path.Combine(parts)));
    }
}
