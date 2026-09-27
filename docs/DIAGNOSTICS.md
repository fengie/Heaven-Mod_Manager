# Diagnostics and Hang Investigation

## Normal observability

Every substantial operation is wrapped by `DiagnosticTelemetry` and gets a correlation ID. Normal logs are structured JSONL and rotate by size/day. The SQLite `diagnostics` and `error_reports` tables contain operation timings, runtime counters, exception category/type, and native codes where available.

Set this before launch for more verbose diagnostic logging:

```powershell
$env:MHWMM_DIAGNOSTIC='1'
```

The WPF Dispatcher watchdog posts one lightweight heartbeat at a time. A delay above the threshold records `dispatcher.stall` with active operations and ThreadPool/runtime context; it does not restart or kill the process.

## Capture evidence during a freeze

Run:

```powershell
.\scripts\Capture-Diagnostics.ps1
```

or pass the process ID:

```powershell
.\scripts\Capture-Diagnostics.ps1 -ProcessId 12345 -DurationSeconds 20
```

The script uses whatever current .NET diagnostic tools are installed:

- `dotnet-stack`: managed stack snapshot
- `dotnet-counters`: short runtime counter capture
- `dotnet-trace`: EventPipe trace
- `dotnet-gcdump`: managed heap graph

It intentionally does **not** collect a full process dump by default because memory dumps can contain private data and mod metadata. If a full dump is required, collect it deliberately and review it before sharing.

Useful tools to install globally when needed:

```powershell
dotnet tool install --global dotnet-counters
dotnet tool install --global dotnet-trace
dotnet tool install --global dotnet-stack
dotnet tool install --global dotnet-gcdump
```

## What to look for

- Dispatcher delay + low CPU: blocking wait, deadlock, synchronous I/O, SQLite lock.
- Dispatcher delay + high CPU: expensive planner/layout/binding work on UI thread.
- ThreadPool queue growth/workers rising: starvation or unbounded fan-out.
- GC/heap continuously rising after repeated workflows: retained view models/events/caches/streams.
- SQLite BUSY/LOCKED: long transactions, connection misuse, checkpoint starvation, or writer contention.
- high hashing counts with high cache hit expectation: invalidation/cache bug.

## Support bundle

The in-app support bundle exports bounded, privacy-conscious metadata: environment/schema/migration status, recent structured diagnostics/errors, operations, manifest metadata, conflict/profile information, DB integrity status, and recent logs. It never automatically includes mod assets or CAS blobs.
