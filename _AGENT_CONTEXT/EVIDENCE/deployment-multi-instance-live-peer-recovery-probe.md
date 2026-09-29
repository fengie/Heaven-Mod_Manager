# Deployment multi-instance live-peer recovery probe — 2026-09-28

Canonical source under test: `4fd61dd33609a7c55e5aedbaad026266a410f942`
Environment: `heaven2` / Windows / .NET SDK 10.0.401 / runtime 10.0.12
Probe type: disposable IntegrationTests characterization; probe source was not retained.

## Schedule

Two independent `ManagerDatabase` / `DeploymentExecutor` objects shared one database, blob root, and game root.

- Initial live file: `nativePC\x.tex = ORIGINAL`.
- Writer A planned Add/ownership of that path with payload `MOD`.
- A paused at the existing `after-file-write` fault-injection seam.
- At the pause, live bytes were asserted as `MOD`; A's journal had not yet advanced from `Writing`.
- Executor B called the real `RecoverIncompleteAsync`.
- B rolled A's still-active operation back; live bytes were asserted as `ORIGINAL`.
- A resumed and completed normally.

Before the final assertion, the probe asserted:

- A returned `Success == true`;
- planner snapshot contained `nativePC\x.tex`;
- provider was `m1`;
- manifest `ExpectedLiveSha256` equaled the `MOD` blob SHA-256.

The final invariant required live bytes to remain `MOD`.

## Build

```text
dotnet build tests\MhwModManager.IntegrationTests\MhwModManager.IntegrationTests.csproj -c Release -m:1 --nologo

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Direct xUnit v3 execution

Command shape:

```text
dotnet .\tests\MhwModManager.IntegrationTests\bin\Release\net10.0-windows10.0.19041.0\MhwModManager.IntegrationTests.dll \
  -method "MhwModManager.IntegrationTests.ConcurrentDeploymentProbeTests.Live_peer_recovery_can_rollback_active_writer_then_writer_commits_stale_manifest" \
  -noLogo -noColor -maxThreads 2
```

Observed twice:

```text
ConcurrentDeploymentProbeTests.Live_peer_recovery_can_rollback_active_writer_then_writer_commits_stale_manifest [FAIL]
  Assert.Equal() Failure: Strings differ
  Expected: "MOD"
  Actual:   "ORIGINAL"

=== TEST EXECUTION SUMMARY ===
MhwModManager.IntegrationTests  Total: 1, Errors: 0, Failed: 1, Skipped: 0, Not Run: 0
```

First direct reproduction duration: 0.314 s.
Second direct reproduction duration: 0.316 s.

This is expected-red characterization evidence. It demonstrates that recovery from a second live owner can revert bytes belonging to an active writer, after which the writer can still report success and durably commit metadata expecting the reverted bytes.

## Non-evidence / discarded attempts

An earlier two-writer scheduling probe failed before reaching the intended commit checkpoint, so it is not used as defect proof.

A `dotnet test --filter` invocation selected zero xUnit v3 tests (exit code 5). It is also not evidence. The direct xUnit v3 method runner above is the authoritative reproduction.

## Retention decision

The intentionally failing probe test was removed before the support branch commit. The exact schedule and observed output are retained here so a future implementation agent can recreate it as a red-first regression and then turn it green with an ownership lease.
