using MhwModManager.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MhwModManager.Storage;
using Serilog;
using Serilog.Context;

namespace MhwModManager.Diagnostics;

public sealed record ActiveOperation(string CorrelationId,string Name,DateTimeOffset StartedUtc);

/// <summary>
/// Lightweight operation tracing used in normal builds. Diagnostic mode adds more runtime counters,
/// but recording a diagnostic failure is never allowed to break the actual mod operation.
/// </summary>
public sealed class DiagnosticTelemetry(ManagerDatabase db, ILogger log)
{
    private readonly ConcurrentDictionary<string,ActiveOperation> active = new(StringComparer.OrdinalIgnoreCase);
    public bool DetailedMode { get; } = string.Equals(Environment.GetEnvironmentVariable("MOD_MANAGER_DIAGNOSTIC") ?? Environment.GetEnvironmentVariable("MHWMM_DIAGNOSTIC"),"1",StringComparison.OrdinalIgnoreCase);
    public IReadOnlyCollection<ActiveOperation> ActiveOperations => active.Values.ToArray();

    public async Task TrackAsync(string operation, Func<string,CancellationToken,Task> action, IReadOnlyDictionary<string,object?>? metadata = null, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await TrackAsync<object?>(operation, async (id,token) => { await action(id,token); return null; }, metadata, ct);
    }

    public async Task<T> TrackAsync<T>(string operation, Func<string,CancellationToken,Task<T>> action, IReadOnlyDictionary<string,object?>? metadata = null, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var __operationTrace = MasterDebugLog.Begin("OPERATION", operation, metadata is null ? null : "metadataKeys=" + string.Join(",", metadata.Keys));
        var id = Guid.NewGuid().ToString("N");
        var started = DateTimeOffset.UtcNow;
        active[id] = new(id,operation,started);
        var sw = Stopwatch.StartNew();
        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var allocatedBefore = GC.GetTotalAllocatedBytes(false);
        using var c1 = LogContext.PushProperty("CorrelationId",id);
        using var c2 = LogContext.PushProperty("Operation",operation);
        log.Information("Operation started");
        try
        {
            var result = await action(id,ct);
            await RecordCompletionAsync(id,operation,"completed",sw,cpuBefore,allocatedBefore,metadata,null,CancellationToken.None);
            __operationTrace.Success($"correlationId={id}");
            return result;
        }
        catch(OperationCanceledException)
        {
            await RecordCompletionAsync(id,operation,"cancelled",sw,cpuBefore,allocatedBefore,metadata,null,CancellationToken.None);
            __operationTrace.Success($"cancelled; correlationId={id}");
            throw;
        }
        catch(Exception ex)
        {
            var assessment=ExceptionPolicy.Assess(ex);
            log.Error(ex,"Operation failed with category {FailureCategory}",assessment.Category);
            await RecordCompletionAsync(id,operation,"failed",sw,cpuBefore,allocatedBefore,metadata,ex,CancellationToken.None);
            await RecordExceptionAsync(id,assessment,ex,CancellationToken.None);
            __operationTrace.Fail(ex,$"category={assessment.Category}; correlationId={id}");
            throw;
        }
        finally
        {
            active.TryRemove(id,out _);
        }
    }

    public async Task RecordSignalAsync(string kind, string? operationId, double? elapsedMs, IReadOnlyDictionary<string,object?>? data = null, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var json = data is null ? null : JsonSerializer.Serialize(data);
            UnifiedDebugLog.Write("TELEMETRY", $"Signal kind={kind}; operationId={operationId ?? "<none>"}; elapsedMs={elapsedMs?.ToString(CultureInfo.InvariantCulture) ?? "<none>"}; data={json ?? "<none>"}");
            await db.ExecuteAsync(
                "INSERT INTO diagnostics(time,operation_id,kind,elapsed_ms,data_json) VALUES($t,$o,$k,$e,$d)",
                new Dictionary<string,object?>
                {
                    ["$t"]=DateTimeOffset.UtcNow.ToString("O"),["$o"]=operationId,["$k"]=kind,["$e"]=elapsedMs,["$d"]=json
                },ct);
        }
        catch(Exception diagnosticFailure)
        {
            log.Warning(diagnosticFailure,"Could not persist diagnostic event {Kind}",kind);
        }
    }

    private async Task RecordCompletionAsync(string id,string operation,string outcome,Stopwatch sw,TimeSpan cpuBefore,long allocatedBefore,IReadOnlyDictionary<string,object?>? metadata,Exception? ex,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        sw.Stop();
        ThreadPool.GetAvailableThreads(out var workerAvailable,out var ioAvailable);
        ThreadPool.GetMaxThreads(out var workerMax,out var ioMax);
        var data = new Dictionary<string,object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["operation"]=operation,
            ["outcome"]=outcome,
            ["cpuMs"]=(Process.GetCurrentProcess().TotalProcessorTime-cpuBefore).TotalMilliseconds,
            ["allocatedBytesDelta"]=Math.Max(0,GC.GetTotalAllocatedBytes(false)-allocatedBefore),
            ["managedThreadId"]=Environment.CurrentManagedThreadId,
            ["threadPoolWorkerInUse"]=workerMax-workerAvailable,
            ["threadPoolIoInUse"]=ioMax-ioAvailable,
            ["threadPoolPendingWorkItems"]=ThreadPool.PendingWorkItemCount,
            ["gcHeapBytes"]=GC.GetTotalMemory(false),
            ["gcGen0Collections"]=GC.CollectionCount(0),
            ["gcGen1Collections"]=GC.CollectionCount(1),
            ["gcGen2Collections"]=GC.CollectionCount(2),
            ["processThreads"]=Process.GetCurrentProcess().Threads.Count,
            ["processHandles"]=Process.GetCurrentProcess().HandleCount,
            ["exceptionType"]=ex?.GetType().FullName
        };
        if(metadata is not null)foreach(var kv in metadata)data[kv.Key]=kv.Value;
        log.Information("Operation {Outcome} in {ElapsedMs} ms",outcome,sw.Elapsed.TotalMilliseconds);
        await RecordSignalAsync("operation."+outcome,id,sw.Elapsed.TotalMilliseconds,data,ct);
    }

    private async Task RecordExceptionAsync(string operationId,ExceptionAssessment assessment,Exception ex,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            await db.ExecuteAsync(
                "INSERT INTO error_reports(id,time,operation_id,category,exception_type,message,details,native_code) VALUES($id,$t,$o,$c,$x,$m,$d,$n)",
                new Dictionary<string,object?>
                {
                    ["$id"]=Guid.NewGuid().ToString("N"),["$t"]=DateTimeOffset.UtcNow.ToString("O"),["$o"]=operationId,
                    ["$c"]=assessment.Category.ToString(),["$x"]=ex.GetType().FullName??ex.GetType().Name,["$m"]=ex.Message,
                    ["$d"]=ex.ToString(),["$n"]=assessment.NativeCode
                },ct);
        }
        catch(Exception diagnosticFailure){log.Warning(diagnosticFailure,"Could not persist exception report");}
    }
}
