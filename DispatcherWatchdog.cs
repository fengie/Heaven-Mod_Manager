using MhwModManager.Core;
using System.Diagnostics;
using System.Windows.Threading;
using MhwModManager.Diagnostics;
using Serilog;

namespace MhwModManager.App;

/// <summary>
/// Posts one lightweight heartbeat at a time. If the Dispatcher cannot service it promptly,
/// a background timer records the stall without adding an unbounded queue of Dispatcher work.
/// </summary>
public sealed class DispatcherWatchdog : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly DiagnosticTelemetry telemetry;
    private readonly ILogger log;
    private readonly CancellationTokenSource cts = new();
    private readonly TimeSpan interval = TimeSpan.FromSeconds(1);
    private readonly TimeSpan stallThreshold = TimeSpan.FromSeconds(2);
    private readonly Task loop;
    private long pendingStarted;
    private long lastReported;
    private int pending;

    public DispatcherWatchdog(Dispatcher dispatcher, DiagnosticTelemetry telemetry, ILogger log)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.dispatcher=dispatcher;
        this.telemetry=telemetry;
        this.log=log;
        loop=Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var timer=new PeriodicTimer(interval);
        try
        {
            while(await timer.WaitForNextTickAsync(cts.Token))
            {
                if(Interlocked.CompareExchange(ref pending,1,0)==0)
                {
                    Interlocked.Exchange(ref pendingStarted,Stopwatch.GetTimestamp());
                    _=dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(HeartbeatCompleted));
                    continue;
                }

                var elapsed=Stopwatch.GetElapsedTime(Interlocked.Read(ref pendingStarted));
                if(elapsed<stallThreshold)continue;
                var now=Stopwatch.GetTimestamp();
                var sinceLast=lastReported==0?TimeSpan.MaxValue:Stopwatch.GetElapsedTime(Interlocked.Read(ref lastReported),now);
                if(sinceLast<stallThreshold)continue;
                Interlocked.Exchange(ref lastReported,now);
                var active=telemetry.ActiveOperations.Select(x=>x.Name).Distinct().ToArray();
                log.Warning("Dispatcher heartbeat delayed {ElapsedMs} ms. Active operations: {Operations}",elapsed.TotalMilliseconds,active);
                await telemetry.RecordSignalAsync("dispatcher.stall",null,elapsed.TotalMilliseconds,
                    new Dictionary<string,object?>
                    {
                        ["activeOperations"]=active,
                        ["managedThreadId"]=Environment.CurrentManagedThreadId
                    },cts.Token);
            }
        }
        catch(OperationCanceledException) when(cts.IsCancellationRequested){}
        catch(Exception ex){log.Warning(ex,"Dispatcher watchdog stopped unexpectedly");}
    }

    private void HeartbeatCompleted()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var elapsed=Stopwatch.GetElapsedTime(Interlocked.Read(ref pendingStarted));
        Interlocked.Exchange(ref pending,0);
        if(elapsed>=stallThreshold)
            log.Warning("Dispatcher recovered after {ElapsedMs} ms",elapsed.TotalMilliseconds);
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        cts.Cancel();
        cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
