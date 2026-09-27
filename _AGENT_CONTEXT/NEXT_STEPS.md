# Next steps

## Current repair revision

Read `AUDIT-2026-09-27.md`. Run `Test Everything.bat` on Windows for the exact
repaired source, then `Build.bat` for Windows publishing. Expect 61 integration
cases and the new cache regression preflight. Six historical Windows stages
remain reusable; FunctionVerifier and Filesystem-dependent checks must rerun.
Confirm all 23 currently unchecked functions only through the complete gate.
No further speculative refactor is needed to finish this repair.

## First action on a Windows machine

Run:

```powershell
.\Test Everything.bat
```

or directly:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Verify-Release.ps1
```

Use .NET SDK 10.0.401 or newer compatible SDK as enforced by the script/global.json.

## On failure

1. Read the newly written `BuildLogs/function-verification-*.json` first for parse/trace gaps.
2. Read `MHW-DEBUG-ALL.log` and the stage-specific build log.
3. Fix compile/analyzer/test failures without manually editing `verified` booleans.
4. Rerun the verifier. Exact unchanged stages already recorded in `.verification/stage-status.json` will show `PASS-CACHED`; changed/failed stages rerun. Full function promotion is still automatic only after a complete required PASS.

## On the next run

The FunctionVerifier and whole solution already compiled successfully in the second Windows run. Re-run `Test Everything.bat` to validate the four newly added entry traces and the later repair revision. Expect the function scan to have zero trace/call-site coverage gaps. Exact unaffected stages should show `PASS-CACHED`; stages whose fingerprints include edited App/Filesystem/verification inputs must rerun. If everything passes, the function confirmation step should promote all exact current function fingerprints to `verified: true`.

## On first complete PASS

Confirm `.verification/function-status.json` has every exact current body at `verified: true` with `verificationBasis: full-release-confirmation`. Commit both function and stage caches with the exact source they verify, and update `CURRENT_REVISION.json` to identify that source commit/evidence. Export a source ZIP only if one is useful for release/handoff.

## Architectural follow-up after v8.8 stabilizes

A sensible next structural project is WPF/application-boundary decomposition: split the very large `MainWindowViewModel` into feature-oriented presentation controllers/viewmodels while leaving transactional deployment/conflict/state logic in backend services. Do this separately from verification hardening.

## Before finishing any future repository task

1. Update `_AGENT_CONTEXT/` with everything materially learned in the task.
2. Update `CURRENT_REVISION.json` when source or verification status changes.
3. Update `handoff-manifest.json` if required context/tooling changes.
4. Run `scripts/Test-AgentHandoff.ps1`.
5. Commit the handoff/context changes with the code they describe.
6. If a source ZIP is needed, prefer `Build Source Handoff.bat` to package the source and emit a per-file hash manifest.
7. Tell the next agent to repeat these exact continuity steps. **Do not break the chain.**

## Recommended architecture sequence after v8.8 is compiler/test-confirmed

Do not immediately rewrite the transactional core. First extract feature-oriented application/presentation services from `MainWindowViewModel` and move its direct import/normalization filesystem work behind backend/application services. Then consider splitting `ManagerDatabase`, `NexusMetadataService`, and `AutoCompatibility` along cohesive responsibilities while preserving transaction/test boundaries.
