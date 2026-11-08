using System.Windows.Threading;
using MhwModManager.App.ViewModels;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class AutoPopulateDispatcherRegressionTests
{
    [Fact]
    public async Task Background_build_keeps_dispatcher_responsive_and_preserves_result()
    {
        using var buildEntered=new ManualResetEventSlim(false);
        using var releaseBuild=new ManualResetEventSlim(false);
        using var dispatcherPulse=new ManualResetEventSlim(false);
        var completion=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher? dispatcher=null;

        var thread=new Thread(()=>
        {
            dispatcher=Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async ()=>
            {
                try
                {
                    var buildTask=MainWindowViewModel.RunAutoPopulateBuildAsync(
                        innerCt=>
                        {
                            buildEntered.Set();
                            releaseBuild.Wait(innerCt);
                            return Task.FromResult(42);
                        },
                        TestContext.Current.CancellationToken);

                    // Intentionally do not await this callback: the test thread must observe
                    // that the Dispatcher can service it while background planning is blocked.
                    _ = dispatcher.BeginInvoke(()=>dispatcherPulse.Set());
                    completion.TrySetResult(await buildTask);
                }
                catch(Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            });
            Dispatcher.Run();
        })
        {
            IsBackground=true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            Assert.True(
                buildEntered.Wait(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken),
                "Auto Populate background work did not start.");
            Assert.True(
                dispatcherPulse.Wait(TimeSpan.FromSeconds(2),TestContext.Current.CancellationToken),
                "The WPF Dispatcher could not service a queued callback while Auto Populate work was running.");

            releaseBuild.Set();
            var result=await completion.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.Equal(42,result);
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)),"The WPF Dispatcher test thread did not shut down.");
        }
        finally
        {
            releaseBuild.Set();
            dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Pre_cancelled_build_never_enters_background_work()
    {
        using var cts=new CancellationTokenSource();
        cts.Cancel();
        var entered=0;

        var task=MainWindowViewModel.RunAutoPopulateBuildAsync(
            _=>
            {
                Interlocked.Exchange(ref entered,1);
                return Task.FromResult(7);
            },
            cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);
        Assert.Equal(0,Volatile.Read(ref entered));
    }

    [Fact]
    public void Auto_populate_imports_newly_sorted_inbox_packages_before_building_the_safe_set()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MhwModManager.sln")))
            root = root.Parent;
        Assert.NotNull(root);

        var source = File.ReadAllText(Path.Combine(root!.FullName, "src", "MhwModManager.App",
            "ViewModels", "MainWindowViewModel.cs"));
        var start = source.IndexOf("private async Task AutoPopulate()", StringComparison.Ordinal);
        var finish = source.IndexOf("internal static Task<T> RunAutoPopulateBuildAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && finish > start);
        var action = source[start..finish];
        var inbox = action.IndexOf("s.Inbox.ProcessAsync(ct)", StringComparison.Ordinal);
        var reload = action.IndexOf("await ReloadMods(ct)", StringComparison.Ordinal);
        var build = action.IndexOf("service.BuildAsync", StringComparison.Ordinal);
        Assert.True(inbox >= 0 && reload > inbox && build > reload,
            "Auto Populate must first ingest and refresh the active game's newly sorted Inbox.");
        Assert.Contains("Mods.Count == 0", action, StringComparison.Ordinal);
        Assert.Contains("No installed mods", action, StringComparison.Ordinal);
    }

    [Fact]
    public void Mod_filters_wrap_and_primary_actions_remain_visible_without_horizontal_scroll()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MhwModManager.sln")))
            root = root.Parent;
        Assert.NotNull(root);

        var xaml = File.ReadAllText(Path.Combine(root!.FullName, "src", "MhwModManager.App", "MainWindow.xaml"));
        var mods = xaml.IndexOf("<TabItem Header=\"Mods\">", StringComparison.Ordinal);
        var nextTab = xaml.IndexOf("<TabItem Header=", mods + 1, StringComparison.Ordinal);
        Assert.True(mods >= 0 && nextTab > mods);
        var modPage = xaml[mods..nextTab];

        Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Auto\"", modPage, StringComparison.Ordinal);
        Assert.Contains("<WrapPanel x:Name=\"ModLibraryToolbar\"", modPage, StringComparison.Ordinal);
        Assert.Contains("Content=\"Select All\"", modPage, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding EnableVisibleCommand}\"", modPage, StringComparison.Ordinal);
        Assert.Contains("Content=\"Deselect All\"", modPage, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DisableVisibleCommand}\"", modPage, StringComparison.Ordinal);
        Assert.Contains("Content=\"Auto Populate\"", modPage, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AutoPopulateCommand}\"", modPage, StringComparison.Ordinal);
    }

}
