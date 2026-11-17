using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class InboxProgressSurfaceTests
{
    [Fact]
    public void Long_running_operations_expose_elapsed_time_and_a_readable_title()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var vm = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App",
            "ViewModels", "MainWindowViewModel.cs"));

        Assert.Contains("Foreground=\"{StaticResource Text}\" Text=\"{Binding BusyTitle}\"", xaml);
        Assert.Contains("IsIndeterminate=\"{Binding BusyProgressIndeterminate}\"", xaml);
        Assert.Contains("Value=\"{Binding BusyProgressPercent}\"", xaml);
        Assert.Contains("Text=\"{Binding BusyProgressSummary}\"", xaml);
        Assert.Contains("Text=\"{Binding BusyElapsedText}\"", xaml);
        Assert.Contains("Text=\"{Binding BusyOperationId}\"", xaml);
        Assert.Contains("Value=\"{Binding BusyItemProgressPercent}\"", xaml);
        Assert.Contains("Visibility=\"{Binding BusyItemProgressVisibility}\"", xaml);
        Assert.Contains("Text=\"{Binding BusyItemProgressLabel}\"", xaml);
        Assert.Contains("OpenMasterLogCommand", xaml);
        Assert.Contains("private void OpenMasterLog()", vm);
        Assert.Contains("Operation progress", xaml);
        Assert.Contains("new System.Windows.Threading.DispatcherTimer", vm);
        Assert.Contains("s.Telemetry.TrackAsync(operationName", vm);
    }

    [Fact]
    public void Inbox_runs_off_dispatcher_and_reports_all_stages_before_refresh()
    {
        var root = FindRepositoryRoot();
        var vm = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App",
            "ViewModels", "MainWindowViewModel.cs"));
        var inbox = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.Automation",
            "SmartInboxService.cs"));
        Assert.Contains("await Task.Run(() => s.Inbox.ProcessAsync(ct, updates), ct)", vm);
        Assert.Contains("await ProcessInboxWithProgressAsync(ct)", vm);
        Assert.Contains("BusyDetail = \"Refreshing the visible mod list…\"", vm);
        Assert.Contains("ReportStage(completed, \"Indexing catalog\")", inbox);
        Assert.Contains("ReportStage(completed, \"Refreshing metadata\")", inbox);
        Assert.Contains("INBOX-PHASE", inbox);
        Assert.Contains("elapsedMs={itemTimer.Elapsed.TotalMilliseconds", inbox);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MhwModManager.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
