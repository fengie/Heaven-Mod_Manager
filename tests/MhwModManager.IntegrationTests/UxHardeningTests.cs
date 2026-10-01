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
        Assert.Contains("Content=\"Auto Populate\"",xaml);
        Assert.Contains("Command=\"{Binding AutoPopulateCommand}\"",xaml);
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
    public void Shared_ui_motion_is_lightweight_and_respects_windows_animation_settings()
    {
        var root=FindRepositoryRoot();
        var app=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","App.xaml"));
        var xaml=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","MainWindow.xaml"));
        var code=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","MainWindow.xaml.cs"));
        var motion=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","UiMotion.cs"));

        Assert.Contains("local:UiMotion.EnableHoverFeedback",app);
        Assert.Contains("x:Name=\"MainTabs\"",xaml);
        Assert.Contains("SelectionChanged=\"OnMainTabSelectionChanged\"",xaml);
        Assert.Contains("IsVisibleChanged=\"OnBusyOverlayIsVisibleChanged\"",xaml);
        Assert.Contains("SystemParameters.ClientAreaAnimation",motion);
        Assert.Contains("SystemParameters.ClientAreaAnimation",code);
        Assert.Contains("TranslateTransform",code);
        Assert.DoesNotContain("ThicknessAnimation",code);
        Assert.DoesNotContain("HeightProperty",code);
        Assert.DoesNotContain("WidthProperty",code);
    }

    [Fact]
    public void Mods_page_prioritizes_library_height_on_wide_layouts()
    {
        var root=FindRepositoryRoot();
        var xaml=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","MainWindow.xaml"));

        Assert.Contains("<ScrollViewer x:Name=\"ModLibraryToolbar\"",xaml);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"",xaml);
        Assert.Contains("<StackPanel Orientation=\"Horizontal\" VerticalAlignment=\"Center\">",xaml);
        Assert.Contains("<Grid x:Name=\"ModLibraryRegion\" Grid.Row=\"2\">",xaml);
        Assert.Contains("<StackPanel Grid.Column=\"1\" Margin=\"12,0,0,0\" Orientation=\"Horizontal\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Center\">",xaml);
        var modsTab=System.Xml.Linq.XDocument.Parse(xaml).Descendants()
            .Single(element=>element.Name.LocalName=="TabItem"&&(string?)element.Attribute("Header")=="Mods");
        var modsWrapper=Assert.Single(modsTab.Elements());
        Assert.Equal("Grid",modsWrapper.Name.LocalName);
        Assert.Equal("Stretch",(string?)modsWrapper.Attribute("HorizontalAlignment"));
        Assert.Null(modsWrapper.Attribute("Width"));
    }

    [Fact]
    public void Mods_empty_state_reacts_when_library_count_changes()
    {
        var root=FindRepositoryRoot();
        var xaml=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","MainWindow.xaml"));
        var vm=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","ViewModels","MainWindowViewModel.cs"));

        Assert.Contains("public int InstalledCount=>Mods.Count;",vm);
        Assert.Contains("<DataTrigger Binding=\"{Binding InstalledCount}\" Value=\"0\"><Setter Property=\"Visibility\" Value=\"Visible\"/></DataTrigger>",xaml);

        var changedStart=vm.IndexOf("private void Changed()",StringComparison.Ordinal);
        var changedEnd=vm.IndexOf("private async Task ReloadMods",changedStart,StringComparison.Ordinal);
        Assert.True(changedStart>=0&&changedEnd>changedStart);
        var changed=vm[changedStart..changedEnd];
        Assert.Contains("OnPropertyChanged(nameof(InstalledCount));",changed);
        Assert.Contains("OnPropertyChanged(nameof(InstalledCountLabel));",changed);
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

    [Fact]
    public void Dashboard_page_stretches_without_viewport_width_binding()
    {
        var root=FindRepositoryRoot();
        var xaml=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","MainWindow.xaml"));
        var document=System.Xml.Linq.XDocument.Parse(xaml);
        var dashboard=document.Descendants()
            .Single(element=>element.Name.LocalName=="TabItem"&&(string?)element.Attribute("Header")=="Dashboard");
        var scroll=dashboard.Descendants()
            .Single(element=>element.Name.LocalName=="ScrollViewer"&&element.Attributes().Any(attribute=>attribute.Name.LocalName=="Name"&&attribute.Value=="DashboardScroll"));
        var wrapper=Assert.Single(scroll.Elements());

        Assert.Equal("Stretch",(string?)scroll.Attribute("HorizontalContentAlignment"));
        Assert.Equal("Stretch",(string?)wrapper.Attribute("HorizontalAlignment"));
        Assert.Null(wrapper.Attribute("Width"));
    }

    [Fact]
    public void Initial_metadata_refresh_can_retry_after_a_failed_or_cancelled_attempt()
    {
        var root=FindRepositoryRoot();
        var vm=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","ViewModels","MainWindowViewModel.cs"));

        Assert.Contains("private bool initialMetadataRefreshCompleted;",vm);
        Assert.Contains("!initialMetadataRefreshCompleted&&!initialMetadataRefreshStarted",vm);
        var start=vm.IndexOf("private async Task EnsureInitialMetadataLoadedAsync",StringComparison.Ordinal);
        var end=vm.IndexOf("private async Task EnsureConflictPreviewsLoadedAsync",start,StringComparison.Ordinal);
        Assert.True(start>=0&&end>start);
        var method=vm[start..end];
        Assert.Contains("if(completed)initialMetadataRefreshCompleted=true;",method);
        Assert.Contains("initialMetadataRefreshStarted=false;",method);
    }

    [Fact]
    public void Startup_diagnostics_redact_updater_health_arguments_before_logging()
    {
        var root=FindRepositoryRoot();
        var app=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","App.xaml.cs"));

        Assert.Contains("UpdateHealthProtocol.FormatArgumentsForDiagnostics(e.Args)",app);
        Assert.DoesNotContain("string.Join(\" \",e.Args)",app);
    }

    [Fact]
    public void Updater_launch_boundary_serializes_staged_candidate_replacement()
    {
        var root=FindRepositoryRoot();
        var source=File.ReadAllText(Path.Combine(root,"src","MhwModManager.App","ViewModels","MainWindowViewModel.Updater.cs"));
        var start=source.IndexOf("private async Task ApplyStagedProgramUpdateWhenSafeAsync",StringComparison.Ordinal);
        var end=source.IndexOf("private static bool IsPreparedHandoffCurrent",start,StringComparison.Ordinal);
        Assert.True(start>=0&&end>start);
        var method=source[start..end];

        var launch=method.IndexOf("LaunchHelper(prepared)",StringComparison.Ordinal);
        Assert.True(launch>0);
        var gateAcquire=method.LastIndexOf("await programUpdateGate.WaitAsync(ct);",launch,StringComparison.Ordinal);
        var identityCheck=method.LastIndexOf("IsPreparedHandoffCurrent(prepared, preparedFor, stagedProgramUpdate)",launch,StringComparison.Ordinal);
        var gateRelease=method.IndexOf("programUpdateGate.Release();",launch,StringComparison.Ordinal);

        Assert.True(gateAcquire>=0);
        Assert.True(identityCheck>gateAcquire);
        Assert.True(gateRelease>launch);
        Assert.Contains("!AutoUpdateEnabled && !stagedProgramUpdateRequestedManually",method);
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
