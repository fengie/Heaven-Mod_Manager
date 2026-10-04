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

                    dispatcher.BeginInvoke(()=>dispatcherPulse.Set());
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
}
