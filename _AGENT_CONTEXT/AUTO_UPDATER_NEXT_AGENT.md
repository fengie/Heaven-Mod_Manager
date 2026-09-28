# Updater: exact continuation checklist

## Current canonical updater state — 2026-09-28
- Starting `origin/main` and the historical updater branch `agent/auto-updater-20260928` both resolve to `a83dc6e047ccf98e896f10c25772df99b95426d1`; the canonical checkout was clean.
- PR #48 integrated the C11b publication and native-recovery closure. PRs #44–#46 and #49 remain open support PRs; inspect current branch ancestry/diffs before assigning any disposition.
- Hosted Windows Release Gate run **36428542918** passed repository verification, release build/package verification, and publication-policy tests, then failed only in `Publish-UpdaterRelease.ps1` when PowerShell strict mode accessed `tagName` on the empty first-release inventory. GitHub currently has no releases.
- Active repair branch/worktree: `agent/auto-updater-publication-fix-20260928`, based on the exact main SHA above. See `_AGENT_CONTEXT/AUTO_UPDATER_C12_RELEASE_DISCOVERY_REPAIR_2026-09-28.md`.
- C12 adds fail-closed release-list shape validation and tests for empty stdout/JSON, valid rows, malformed JSON, null rows, and missing properties. Windows policy test and diff check pass; full verifier/build, hosted rerun, publication, and installed-client old→new/rollback proof remain pending.
- Continue from this branch. Do not touch the separate clean canonical main worktree. After exact local gates, commit/push, integrate through the repository workflow, confirm release bytes, then complete disposable old→new and injected rollback with user-data and build-identity checks.



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

## Checkpoint C7 — exact published build identity
- DONE at exact production/test commit `f70fea687be362fb0869390b119f77417d9bf707`: `build-identity.json` is required updater-owned payload and validated as a published identity.
- Installed, staged, and rollback product manifests must own `build-identity.json`; release markers must exactly match their shipped identity, including timestamp, and marker/build channels must agree.
- Target `update-manifest.json`, staged `release-install.json`, and staged `build-identity.json` must agree on channel, product version, source SHA, and build number before backup/live mutation.
- Strong regression keeps bytes cryptographically self-consistent while making the semantic source identity disagree; apply fails before backup.
- Focused updater suite: **57/57 PASS** on Windows x64 / .NET SDK 10.0.401.
- Strict whole-solution build: **0 warnings / 0 errors**.
- No full Verify-Release/Build-Release/hosted release gate applies to these changed updater inputs yet.

### Next bounded work
1. Review updater mutation paths for collision races, physical-containment TOCTOU, rollback idempotency, and LR-003 native partial-replace failure handling. Add defensive fault fixtures; do not broaden into unrelated hardening.
2. Wire WPF startup acknowledgement, background check/stage, operation-aware handoff, helper copying, and minimal status/manual-check UI.
3. Then extend Build-Release and the existing Windows Release Gate for exact updater metadata/artifact generation and immutable verified publication.
4. Only after those independently green checkpoints, run the full release gates and disposable real old→new plus injected-rollback end-to-end tests.

Preserve and recursively propagate the continuity constitution and active Learned Rules to the successor, and require that successor to pass them to the agent after them.

## Checkpoint C8 — atomic new-path publication and trace-policy closure
- Exact production/test commit: `fe05fc0dd6542dc46e8fc05d4b15b7370b150b8b`.
- A new regression creates an unknown `new.dll` after preflight but immediately before file apply. Before repair, updater apply completed successfully and silently overwrote the user file; the regression failed because no exception was thrown.
- New-only product paths now use `AtomicFileOps.ReplaceFromAsync(... requireDestinationAbsent: true)`, which publishes by one no-overwrite same-directory rename instead of a `File.Exists` check followed by replacement.
- Same-process apply records only new paths whose publication actually returned successfully; if a later collision/failure occurs, rollback removes only those proven-published new paths and preserves a raced unknown path. Crash/restart rollback intentionally keeps the previous conservative hash-based behavior because ephemeral progress is not guessed after process loss.
- Shared AtomicFileOps default replacement behavior is unchanged. BlobStore was converted to named arguments only to preserve analyzer-clean calling after the optional policy parameter was added.
- Full integration initially exposed an inherited updater-helper direct `Process.Start` policy violation (153/154). Helper restart now routes through the existing `ProcessDebug.Start` trace wrapper; full integration then passed.
- Windows x64 / .NET SDK 10.0.401: updater focused **58/58 PASS**; native ReplaceFileW 1175/1176/1177 fixtures **3/3 PASS**; full IntegrationTests **154/154 PASS**; strict whole-solution build **0 warnings / 0 errors**.
- Analyzer feedback during repair was honored rather than suppressed: CA1068 kept CancellationToken last; CA1859 uses the private concrete HashSet type.
- Physical reparse/topology TOCTOU remains explicitly path-based and is not claimed solved by this checkpoint.
- No Verify-Release, Build-Release, hosted Windows Release Gate, or live old→new updater closure is claimed yet.

### Next bounded work
1. Add updater-specific LR-003 fault injection around native existing-file replacement (especially documented 1176/1177 partial-name-mutation outcomes) and prove backup/journal/recovery behavior without weakening fail-closed semantics.
2. Then wire WPF update client/lifetime integration.
3. Keep packaging/publication and full end-to-end release closure separate.

Preserve the permanent continuity constitution and active Learned Rules, and require the successor to recursively propagate them to the agent after them.


## Checkpoint C9 — updater LR-003 native replacement recovery
- Exact production/test commit: `8cdf54d0bc4065a55124aef96c68b26de2a78f0c`.
- Added updater-level injectable apply replacement backend only for deterministic native failure fixtures; default production replacement remains the existing Windows `ReplaceFileW` backend.
- Characterization before repair: updater 1175 passed, while 1176/1177 failed because `UpdateInstaller` automatically rolled back through documented ambiguous native pathname mutation and recreated the destination.
- Repair: `Win32Exception` 1176/1177 now records `RollbackRequired` and preserves rollback backup plus native replacement evidence instead of guessing through ambiguous namespace state. Error 1175 retains ordinary deterministic rollback.
- Windows x64 / .NET SDK 10.0.401 on heaven: updater focused **61/61 PASS**; native 1175/1176/1177 **3/3 PASS**; full IntegrationTests **157/157 PASS**; strict whole-solution build **0 warnings / 0 errors**.
- No `Verify-Release.ps1`, `Build-Release.ps1`, hosted Windows Release Gate, cache promotion, or live old→new updater closure is claimed for this changed source.
- Next bounded work: WPF startup health acknowledgement, background check/stage, operation-aware handoff, verified helper copying, and minimal updater status/manual-check UX. Keep packaging/publication as the following independent checkpoint.
- Preserve the permanent continuity constitution and active Learned Rules; require the successor to recursively propagate them to the agent after them.

## Checkpoint C10 — WPF/client lifetime integration and handoff hardening
- Inherited WPF/client integration commit: `1fbdd0619cc4e7bee00d4eeb70de1d4d488166d4`.
- Exact hardened production/test source: `fdff9ed940b8801c1b17bedb6e6de523d7b807a6`.
- Startup health acknowledgement occurs only after the main window is initialized/shown, watchdog exists, and startup has otherwise completed.
- Background/manual update checks are non-fatal and stage before any shutdown.
- Restart arguments remove updater-owned health tuples and now reject malformed/dangling tuples.
- Handoff verifies and copies the complete product-owned `UpdaterHelper/` file closure, not only the executable.
- `UpdateHandoffGate` atomically excludes new foreground `RunBusy` work from the final helper-launch/shutdown window.
- Expected-red characterization before repair: **5/5 failed** for malformed health tuples / missing helper dependency copy.
- Final heaven/Windows/.NET 10.0.401 evidence: focused updater/handoff **74/74 PASS**; full IntegrationTests **170/170 PASS**; strict solution build **0 warnings / 0 errors**; handoff preflight PASS.
- Detailed evidence: `AUTO_UPDATER_C10_HARDENING_2026-09-28.md`.
- No full release gate or cache promotion is claimed.

## Checkpoint C11a — deterministic updater package generation
- Exact committed checkpoint: `2e136cc22f570a94db8da67c91aa755d4c7a3ce6`.
- `Build-Release.ps1` now generates exact `build-identity.json`, `product-files.json`, `release-install.json`, and `update-manifest.json`.
- Protected user/runtime roots plus mutable `MHW-DEBUG-ALL.log` are excluded from updater ownership.
- The updater helper is published self-contained as a multi-file invocation closure under `UpdaterHelper/`; C10 copies/verifies the complete owned closure before launch.
- `Test-UpdaterPackage.ps1` verifies artifact name/size/SHA-256, cross-file build identity, product-manifest SHA, all owned file size/hashes, protected-root exclusions, helper/restart executable ownership, and exact ZIP file-set equality.
- Exact clean-checkout local Windows release evidence: Core **79/79**, Automation **24/24**, Integration **170/170**, self-test **11/11**, analyzers/App publish/helper publish/package verifier PASS.
- Exact updater ZIP SHA-256: `8A65C28FD55C7FFE7638457BE11C6AF5C9EC71A3BE2E45EF70E3261B6E077B74`.
- Failed attempts and repairs are documented in `AUTO_UPDATER_C11_PACKAGING_2026-09-28.md`.
- No immutable release/publication or end-to-end closure is claimed.

## Checkpoint C11b — immutable publication policy and final race closure
- Integrated exact source: `08054d96a8ba84819b6dfd775d56f9eeabc7986d`.
- Adds deterministic ZIP/build timestamps derived from the exact commit; exact source/checkout guards; immutable `updater-main-<build>` publication; exact two-asset release verification; monotonic build, stale-main, evidence-only, orphan-tag, draft-release, and no-clobber refusal; plus a second `origin/main` fetch immediately before irreversible publication so a concurrent main push cannot expose stale updater bytes.
- Carries the remaining updater-specific LR-003 metadata/rollback replacement fixtures from support PR #48.
- Exact local heaven/Windows/.NET 10.0.401 evidence: publication-policy PASS; handoff PASS; strict build **0 warnings / 0 errors**; Integration **173/173**; `Verify-Release.ps1` **25/25**; FunctionVerifier **727/727**, **7749** explicit call sites / **0** uncovered, **0** trace gaps/parse errors; Core **79/79**; Automation **24/24**; self-test **11/11**; ReadyToRun app publish PASS; updater-helper publish PASS; package verifier PASS; `Build-Release.ps1` PASS.
- Local updater build **227**; ZIP SHA-256 `09ADF5E9B6C7C832633D7E5BA7C4DD2AA3EAB29F347E540B79542B731CD3665C`.
- No GitHub Release was published from the updater branch.

### Remaining final closure only
1. Integrate the verified updater source to eligible exact `main` without weakening the publication gate.
2. Run/inspect the hosted Windows gate on that exact main SHA and verify the immutable `updater-main-<build>` tag/release plus exact ZIP and `update-manifest.json` assets.
3. Run disposable real old→new update and injected rollback end-to-end tests with seeded user-data hashes and exact restart identity.
4. Only after those are independently green call the automatic updater end-to-end complete.

Preserve and recursively propagate the continuity constitution and active Learned Rules again.
