# Updater: exact continuation checklist

## Start here
- Fetch all remotes, inspect status and recent commits. Preserve other agents' work.
- Canonical base: 4fd61dd33609a7c55e5aedbaad026266a410f942.
- Working branch: agent/auto-updater-20260928. Inherited implementation: e42fdd49f77766d1fda32c0dd3539ab46d6432e8.
- Read AGENTS.md, training README, continuity constitution, active Learned Rules, and AUTO_UPDATER_IMPLEMENTATION.md. Preserve and recursively propagate these requirements to your successor and require them to do so again.
- This is an incomplete feature. Do not merge, publish, or claim end-to-end completion from focused test results.

## Latest checkpoint
- Added real Windows regressions for cancellation during live replacement and collision with an unknown user file.
- Both failed against inherited code. Cancellation produced an AggregateException because rollback used the cancelled token. Unknown-file collision did not throw.
- Recovery now uses an independent ten-minute token. Preflight rejects incoming file collisions with paths absent from previous ownership metadata and rejects directory collisions.
- Focused updater suite: 35/35 PASS on Windows x64, .NET SDK 10.0.401.
- Full release gates have not run for these changed inputs. Verification caches have not been manually promoted.

## Next steps, in order
1. Repair interrupted-apply recovery: inspect the journal before validating potentially half-written installed metadata. Preserve and authenticate backup metadata; never recreate/delete the only recovery backup before recovery succeeds. Add deterministic interruption tests around both metadata writes.
2. Repair helper restart/health failures: start failure must enter recovery; rollback must require proof the new process exited. Test actual helper processes, not only JSON argument round trips.
3. Review all mutation paths for physical containment, collision races, rollback idempotency, and native partial-replace failures under LR-003. No exploit reproduction is needed; use defensive fault fixtures.
4. Wire WPF startup acknowledgement, background check/stage, safe operation-aware handoff, helper copying, and small update status/manual-check UI.
5. Extend Build-Release to publish helper and generate exact build identity, ownership manifest, install marker, and update manifest. Exclude runtime logs/user roots.
6. Extend the existing Windows Release Gate with verified immutable private GitHub Releases; test gate failure, evidence-only changes, and stale publication. Never put tokens in source or artifacts.
7. Run focused + affected tests, handoff, Verify-Release, Build-Release, hosted Windows gate, then disposable real old-to-new and injected-rollback tests. Verify seeded user data hashes and exact restart identity. Only then integrate.

## Commands from repository root
```powershell
dotnet test tests/MhwModManager.IntegrationTests/MhwModManager.IntegrationTests.csproj -c Release --filter 'FullyQualifiedName~UpdateInstallerTests|FullyQualifiedName~UpdateRuntimeTests|FullyQualifiedName~UpdaterCoreTests'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AgentHandoff.ps1
```

Commit and push each coherent tested checkpoint. Update this checklist and CURRENT_STATE/NEXT_STEPS/CURRENT_REVISION/VERIFICATION with exact evidence. Near context limits, finish the smallest safe unit, preserve and push it, and leave exact remaining steps. Do not break the chain.