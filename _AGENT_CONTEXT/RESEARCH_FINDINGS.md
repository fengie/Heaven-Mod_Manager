# Re-audit and external research findings — v8.8.0 handoff hardening

These findings were gathered while re-reading the v8.8 source after the initial Function Verification implementation. They are guidance/evidence, not claims that every recommendation has already been implemented.

## Implemented in this revision

### 1. First-chance exception observation stays, hot-path full logging becomes opt-in

`.NET` raises `AppDomain.FirstChanceException` whenever a managed exception is thrown, before handler lookup. That is useful for associating nested exceptions with active function scopes, but writing a full stack trace for every throw can generate unnecessary I/O/noise for expected handled exceptions.

v8.8 now keeps lightweight counting/scope observation always active and only writes full `FIRST-CHANCE` exception records when `MHW_FIRST_CHANCE_DETAIL=1` (also accepts `true`, `yes`, `on`). Process exit records the total first-chance count.

Official references:
- https://learn.microsoft.com/dotnet/api/system.appdomain.firstchanceexception
- https://learn.microsoft.com/dotnet/core/diagnostics/built-in-metrics-runtime
- https://learn.microsoft.com/dotnet/core/diagnostics/eventpipe

### 2. Generated `obj`/`bin` C# must never enter function verification

The function scanner is a source verifier, not a generated-code verifier. A recursive `src/**/*.cs` scan can encounter SDK/WPF-generated code after a previous build. The scanner now explicitly excludes any file under `bin` or `obj`.

### 3. Trusted bootstrap must prove its own integrity

The embedded v8.7 source snapshot is used as verification evidence. It is now checked against `trusted-v8.7.0-files.json` during scanning: unmanifested files, missing manifested files, or hash mismatches fail closed.

### 4. Function IDs must not silently collide for explicit-interface implementations

Two explicit interface members can share the same member name/signature while belonging to different interfaces. Function IDs now include explicit-interface qualification for methods/properties/indexers/events to avoid incorrect cache aliasing.

### 5. Report explicit call-site coverage

The verifier now counts explicit invocation/object-construction/constructor-initializer call sites inside each source body. A body is call-site-covered when it is already known-good, has the required runtime function scope, or has the narrow tracer-recursion exemption. Reports include total/covered/uncovered explicit call-site counts.

This does **not** claim static proof for compiler-generated calls (property access, disposal, async state-machine internals, etc.). Exceptions from those still flow through the containing runtime scope.

## Strong future candidates (not implemented here)

### ActivitySource for coarse-grained operation tracing

Microsoft recommends `System.Diagnostics.ActivitySource` for custom distributed tracing. It offers parent/child correlation and listener-controlled sampling. It is a good candidate for major operations such as scan/plan/deploy/recover/Nexus refresh, but **not** a reason to replace every function scope with heavyweight spans. Keep per-function verification distinct from operation observability.

References:
- https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing
- https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs

### Source-generated structured logging for high-frequency diagnostics

Modern .NET recommends `LoggerMessageAttribute` source generation for high-performance structured logging because templates are parsed at compile time and allocations/boxing are reduced. Consider it if the diagnostic system is consolidated in a future architecture pass. Do not mix that migration into transactional/deployment changes casually.

Reference:
- https://learn.microsoft.com/dotnet/core/extensions/high-performance-logging

### Failure-triggered crash/hang diagnostics for tests

`dotnet test` supports blame/crash/hang diagnostics. A future Windows CI/harness improvement could rerun a failed/hung test stage with appropriate dump collection rather than enabling dump generation on every successful run.

Reference:
- https://learn.microsoft.com/dotnet/core/tools/dotnet-test-vstest

## Architecture debt confirmed by source re-audit

These are prioritized future refactor targets, not reasons to destabilize v8.8 now:

- `src/MhwModManager.App/ViewModels/MainWindowViewModel.cs` — ~1,245 lines; still contains direct file/directory normalization/import work. Split by feature/application use case.
- `src/MhwModManager.Storage/ManagerDatabase.cs` — ~694 lines; evaluate repository/query-command decomposition while preserving transaction boundaries.
- `src/MhwModManager.Filesystem/NexusMetadataService.cs` — ~685 lines; separate transport/cache/metadata interpretation where natural.
- `src/MhwModManager.Core/AutoCompatibility.cs` — ~681 lines; candidate for rule-family decomposition if tests remain strong.
- `src/MhwModManager.App/App.xaml.cs` — composition/bootstrap remains sizable; keep it composition-oriented and resist new domain/filesystem behavior.

The backend deployment/journal/CAS safety path remains materially healthier than the WPF presentation boundary. Refactor presentation/application boundaries before redesigning the transactional core without evidence.

## 2026-09-27 architecture research

- Current Microsoft .NET guidance favors centralized service registration and constructor injection; a Generic Host is available for desktop lifetime/DI, but this milestone intentionally avoids changing WPF lifetime composition at the same time as feature decomposition.
- Current Microsoft MVVM guidance supports incremental loose-coupling for complex multi-screen applications. The chosen path is therefore feature-boundary extraction first, page view models second.
- Microsoft.Data.Sqlite transaction guidance reinforces that transaction scope is the atomic unit. Repository extraction must not split existing deployment transactions.
- MO2 and Vortex conflict UIs both expose concrete competing providers/winner relationships. Explain Why follows that pattern while adding this manager's confidence/evidence/provenance data.
- Implementation consequence: Explain Why consumes the existing DeploymentPlanner/ConflictDecision output rather than creating a second resolver.

