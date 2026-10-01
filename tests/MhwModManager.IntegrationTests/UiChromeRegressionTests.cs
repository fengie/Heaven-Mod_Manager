using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UiChromeRegressionTests
{
    [Fact]
    public void App_owned_scrollbar_chrome_stays_dark()
    {
        var root=FindRepositoryRoot();
        var app=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","App.xaml"));

        Assert.Contains("x:Key=\"DarkScrollBarThumb\"",app);
        Assert.Contains("<Style TargetType=\"ScrollBar\">",app);
        Assert.Contains("x:Name=\"PART_Track\"",app);
        Assert.Contains("Background\" Value=\"#0A1018\"",app);
        Assert.Contains("ScrollBar.PageLeftCommand",app);
        Assert.Contains("ScrollBar.PageRightCommand",app);
    }

    [Fact]
    public void Main_window_requests_dark_native_title_bar()
    {
        var root=FindRepositoryRoot();
        var code=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","MainWindow.xaml.cs"));

        Assert.Contains("SourceInitialized+=OnSourceInitialized",code);
        Assert.Contains("useImmersiveDarkMode=20",code);
        Assert.Contains("useImmersiveDarkModeBefore20H1=19",code);
        Assert.Contains("[DllImport(\"dwmapi.dll\",ExactSpelling=true)]",code);\n        Assert.Contains("DefaultDllImportSearchPaths(DllImportSearchPath.System32)",code);
        Assert.Contains("DwmSetWindowAttribute",code);
    }

    [Fact]
    public void Build_identity_display_text_is_ascii_stable_and_has_no_mojibake()
    {
        var identity=new UpdateBuildIdentity(
            UpdateProtocol.BuildIdentitySchemaVersion,
            UpdateProtocol.Channel,
            "8.8.60",
            "1234567890abcdef",
            373,
            DateTimeOffset.UnixEpoch);

        Assert.Equal("8.8.60 | build 373 | 1234567890ab",identity.DisplayId);
        Assert.DoesNotContain("â",identity.DisplayId,StringComparison.Ordinal);
        Assert.DoesNotContain("€¢",identity.DisplayId,StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current=new DirectoryInfo(AppContext.BaseDirectory);
        while(current is not null)
        {
            if(File.Exists(Path.Combine(current.FullName,"MhwModManager.sln")))return current.FullName;
            current=current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
