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
1. DONE (focused Windows tests): journal-first interrupted-metadata recovery, identity mismatch rejection, and whole-backup validation before mutation. Focused updater suite now 39/39. Full release gate still pending.
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
## Checkpoint C3 — restart recovery order
- Extracted UpdateRestartCoordinator: target launch/health/confirmation failure enters rollback, and inability to prove target process exit blocks rollback while preserving backup/journal.
- Helper uses the coordinator and returns explicit process-stop success instead of swallowing stop failure and continuing mutation.
- Focused updater tests: 41/41 PASS; strict helper build: zero warnings/errors. Tests cover launch failure restoring previous bytes and stop refusal preserving the new bytes plus backup.
- Remaining: real helper-process crash/restart coverage, durable target PID tracking before resumed health recovery, filesystem collision races, WPF/packaging/publication integration, and all release gates.

## Checkpoint C4 — durable target launch recovery
- DONE: launch attempt is persisted before target start and completed with exact PID/process-start identity after launch.
- DONE: health acknowledgement requires the exact launch attempt and PID when known.
- DONE: restarted helper attaches to the existing tracked target; ambiguous pre-PID launch state never causes a duplicate relaunch or speculative rollback.
- DONE: real external-helper-process resume regression.
- Exact focused evidence before checkpoint commit: **47/47 PASS** on Windows x64 / .NET SDK 10.0.401; strict whole-solution build **0 warnings / 0 errors**.
- The updater remains INCOMPLETE and is not release-gate verified after these source changes.

### Next bounded work
1. Fix rollback restart identity so a renamed/moved target executable cannot prevent restart of the restored previous build. Add a regression with different old/new executable paths.
2. Enforce authenticated GitHub HTTPS/API-host trust before attaching the bearer token, with redirect/token-leak coverage.
3. Cross-check exact build identity across update manifest, staged install marker, and shipped build-identity metadata before live mutation.
4. Then resume filesystem collision-race/LR-003 review and WPF integration; keep packaging/publication as later independently verified checkpoints.

Preserve and recursively propagate the continuity constitution to the successor, and require that successor to pass it to the agent after them.

## Checkpoint C5 — previous-build restart identity
- DONE: release marker records the executable path and product ownership validates it.
- DONE: rollback restart loads the restored old marker instead of reusing the new manifest executable.
- DONE: renamed-executable rollback regression; focused suite **48/48 PASS**, strict solution build **0 warnings / 0 errors** on .NET 10.0.401.
- Next: authenticated GitHub HTTPS/API-host enforcement before attaching credentials; then exact update/marker/build-identity agreement.

## Checkpoint C6 — GitHub credential origin
- DONE: bearer-token requests restricted to HTTPS exact api.github.com/default port/no user-info before Authorization is attached.
- DONE: real .NET TLS redirect regression proves Authorization is cleared before the release-asset host.
- Focused updater suite: **54/54 PASS**; strict solution build: **0 warnings / 0 errors** on .NET 10.0.401.
- Next: exact build identity agreement across update-manifest.json, release-install.json, and build-identity.json before live mutation.
