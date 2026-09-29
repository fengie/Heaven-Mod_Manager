using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UxHardeningTests
{
    [Fact]
    public void Main_window_exposes_high_value_library_and_deployment_shortcuts()
    {
        var root=FindRepositoryRoot();
        var xaml=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","MainWindow.xaml"));
        Assert.Contains("Preview Mod Changes",xaml);
        Assert.Contains("Discard Pending Changes",xaml);
        Assert.Contains("SetModViewCommand",xaml);
        Assert.Contains("Header=\"Shared Files\"",xaml);
        Assert.Contains("RefreshAnalysisNowCommand",xaml);
        Assert.Contains("Modifiers=\"Control\" Command=\"{Binding ApplyCommand}\"",xaml);
        Assert.Contains("Content=\"Launch Game\"",xaml);
        Assert.Contains("Content=\"Install Mod\"",xaml);
        Assert.Contains("Header=\"More tools\"",xaml);
        Assert.DoesNotContain("JUST PLAY",xaml);
        Assert.DoesNotContain("Text=\"SMART VIEWS\"",xaml);
    }

    [Fact]
    public void Main_window_keeps_the_hero_first_visual_redesign()
    {
        var root=FindRepositoryRoot();
        var app=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","App.xaml"));
        var xaml=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","MainWindow.xaml"));

        Assert.Contains("x:Key=\"HeroCard\"",app);
        Assert.Contains("x:Key=\"MetricCard\"",app);
        Assert.Contains("x:Key=\"ActionTile\"",app);
        Assert.Contains("<RowDefinition Height=\"78\"/>",xaml);
        Assert.Contains("Text=\"CURRENT SETUP\"",xaml);
        Assert.Contains("Text=\"Quick actions\"",xaml);
        Assert.Contains("<UniformGrid Grid.Row=\"2\" Columns=\"4\"",xaml);
        Assert.DoesNotContain("Text=\"System status\"",xaml);
        Assert.DoesNotContain("Text=\"Start here\"",xaml);
    }

    [Fact]
    public void Mods_page_prioritizes_library_height_on_wide_layouts()
    {
        var root=FindRepositoryRoot();
        var xaml=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","MainWindow.xaml"));

        Assert.Contains("<ScrollViewer x:Name=\"ModLibraryToolbar\"",xaml);
        Assert.Contains("<Grid x:Name=\"ModLibraryRegion\" Grid.Row=\"2\">",xaml);
        Assert.Contains("<StackPanel Grid.Column=\"2\" Orientation=\"Horizontal\" VerticalAlignment=\"Center\" HorizontalAlignment=\"Right\">",xaml);
    }

    [Fact]
    public void Background_metadata_and_remote_visual_cache_are_hardened()
    {
        var root=FindRepositoryRoot();
        var vm=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","ViewModels","MainWindowViewModel.cs"));
        var nexus=File.ReadAllText(Path.Combine(root,"src","MhwModManager.Filesystem","NexusMetadataService.cs"));
        Assert.Contains("SemaphoreSlim metadataGate",vm);
        Assert.Contains("metadataGate.WaitAsync(0,ct)",vm);
        Assert.Contains("foreground/critical operation is active",vm);
        Assert.Contains(".part-",nexus);
        Assert.Contains("File.Move(temp,path,false)",nexus);
        Assert.Contains("12_000_000",nexus);
    }

    [Fact]
    public void Gallery_prefers_persisted_visuals_before_recursive_rescan()
    {
        var root=FindRepositoryRoot();
        var visuals=File.ReadAllText(Path.Combine(root,"src","MhwModManager.Filesystem","ModVisualService.cs"));
        Assert.Contains("result.Count<Math.Min(maxItems,4)",visuals);
    }

    [Fact]
    public void Visual_sync_has_no_key_fallback_and_vortex_picture_metadata_support()
    {
        var root=FindRepositoryRoot();
        var nexus=File.ReadAllText(Path.Combine(root,"src","MhwModManager.Filesystem","NexusMetadataService.cs"));
        Assert.Contains("pictureUrl",nexus);
        Assert.Contains("Nexus catalog/metadata network access is API-only",nexus);
        Assert.DoesNotContain("TryRefreshPublicNexusVisualAsync",nexus);
        Assert.DoesNotContain("og:image",nexus);
        Assert.DoesNotContain("NEXUS_API_KEY",nexus);
        Assert.DoesNotContain("nexus-api-key.txt",nexus);
        Assert.Contains("new CatalogCredentialStore(nextStateRoot).ReadSecret(\"nexus\")",nexus);
        Assert.Contains("Local and Vortex/sidecar artwork remain available without Nexus credentials",File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","ViewModels","MainWindowViewModel.cs")));
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
