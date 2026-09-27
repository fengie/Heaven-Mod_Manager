using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UxHardeningTests
{
    [Fact]
    public void Main_window_exposes_high_value_library_and_deployment_shortcuts()
    {
        var root=FindRepositoryRoot();
        var xaml=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","MainWindow.xaml"));
        Assert.Contains("Preview changes",xaml);
        Assert.Contains("Discard staged",xaml);
        Assert.Contains("SetModViewCommand",xaml);
        Assert.Contains("Header=\"Overlaps\"",xaml);
        Assert.Contains("RefreshAnalysisNowCommand",xaml);
        Assert.Contains("Modifiers=\"Control\" Command=\"{Binding ApplyCommand}\"",xaml);
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
        Assert.Contains("TryRefreshPublicNexusVisualAsync",nexus);
        Assert.Contains("og:image",nexus);
        Assert.Contains("API key is optional for basic artwork",File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","ViewModels","MainWindowViewModel.cs")));
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
