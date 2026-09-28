# Automatic updater implementation checkpoint

## Canonical starting truth

- Repository: `fengie/mhw-mods`
- Selected branch: `agent/auto-updater-20260928`
- Canonical `origin/main` at start: `4fd61dd33609a7c55e5aedbaad026266a410f942`
- Working tree was clean before updater implementation began.
- The parallel `agent/support-auto-updater-release-security-audit-20260928` branch was inspected first. It contains audit/continuity documentation only; it does not contain updater production code.
- The updater is the user-selected production boundary for this assignment even though older continuity text still names archive resource budgeting as the default next boundary.

## Durable architecture decisions

1. Semantic product version remains separate from continuous build identity.
2. Every eligible published build carries product version, exact source SHA, monotonic GitHub run/build number, and build timestamp.
3. Publication uses durable immutable GitHub Release assets in the existing private repository, not Actions artifacts as the client feed.
4. Release tags use `updater-main-<build-number>`. Clients select the highest build number greater than the installed build, so stale release creation cannot make a client downgrade.
5. Private-repository access is authenticated locally. No PAT/token is embedded in source, Git history, application binaries, logs, or support bundles.
6. The client first checks the `MHW_MOD_MANAGER_GITHUB_TOKEN` process environment for controlled/test use, then Windows Credential Manager target `MhwModManager/GitHubUpdater/fengie/mhw-mods`.
7. Downloaded release ZIP bytes are streamed to disk, bounded, length-checked, and SHA-256 verified before publication into staging.
8. ZIP extraction is staged under LocalAppData, never into the live installation.
9. Staging rejects traversal/rooted/drive/device/ADS-like paths, case collisions, symbolic-link archive entries, reparse traversal, excessive entry count, and excessive actual streamed output.
10. `product-files.json` is the explicit application-owned payload boundary. User/runtime roots and unknown files are not updater-owned.
11. At minimum `Mods`, `State`, `Inbox`, `Mods Archive`, `Games`, support material, BuildLogs, and the mutable root `MHW-DEBUG-ALL.log` are excluded from updater ownership.
12. A packaged-install marker is required before self-apply. Repository/development release layouts are not eligible for destructive self-update.
13. Live replacement will be delegated to a separate helper copied to a LocalAppData execution directory before application shutdown.
14. The helper will own the update mutex, PID wait, backup, apply, installed-byte verification, restart, health acknowledgement, commit, and deterministic rollback.
15. The previous payload remains available until the new application reaches the defined startup-health checkpoint.
16. Network/auth/update errors are non-fatal to normal application startup and must never expose credentials.

## Checkpoint A implemented

New project: `src/MhwModManager.Updater`.

Implemented components:

- `UpdateModels.cs`
  - protocol constants;
  - exact build identity;
  - update manifest validation;
  - product-file manifest validation;
  - candidate/staging/apply/journal models;
  - GitHub release wire models.
- `UpdatePathSafety.cs`
  - normalized relative paths;
  - rooted/drive/ADS/traversal/Windows-device rejection;
  - protected user/runtime roots;
  - lexical containment;
  - existing-component reparse rejection;
  - component-by-component directory creation with containment checks;
  - development-layout recognition.
- `WindowsCredentialStore.cs`
  - local environment override;
  - Windows generic Credential Manager lookup;
  - no secret logging.
- `GitHubUpdateSource.cs`
  - authenticated private GitHub release discovery;
  - highest-build selection;
  - manifest/release/asset agreement;
  - bounded streaming artifact download;
  - exact size and SHA-256 verification;
  - temporary-file publication only after verification.
- `UpdatePackageStager.cs`
  - LocalAppData staging;
  - resource-bounded streaming ZIP extraction;
  - symlink/reparse/traversal/collision rejection;
  - exact product manifest SHA-256 verification;
  - exact staged file-set and per-file size/hash verification;
  - atomic pending-state JSON publication.
## Tests and evidence for Checkpoint A

Focused updater tests were added to `MhwModManager.IntegrationTests`.

Current focused result:

- `dotnet test tests\MhwModManager.IntegrationTests\MhwModManager.IntegrationTests.csproj -c Release --filter FullyQualifiedName~UpdaterCoreTests`
- **20/20 PASS**

The focused suite covers:

- traversal, rooted/drive, ADS, and Windows device-name rejection;
- protected user/runtime ownership rejection;
- unsupported manifest schema;
- oversized manifest artifact budget;
- same/older build -> no update;
- highest newer build selection despite stale release ordering;
- wrong artifact SHA-256 rejection without destination publication;
- exact artifact length/hash success;
- product-manifest hash mismatch;
- unknown staged-file rejection.

The first focused run caught a real Windows bug: after successful download verification the temporary ZIP stream remained open when rename publication was attempted. The regression failed with a Windows sharing violation. `GitHubUpdateSource` now disposes the output stream before `File.Move`; the same test then passed.

Strict compile after the repair:

- `dotnet build MhwModManager.sln -c Release -warnaserror`
- **PASS — 0 warnings / 0 errors**

This is not yet a release-gate claim. Production source changed and still requires the full repository verifier, complete test suites, exact Windows release build/gate, publication tests, helper/update integration tests, and live disposable old-to-new/rollback evidence before closure.

## Next exact implementation boundary

Build the external apply/rollback helper and its deterministic journal first against disposable install roots. Prove user/unknown-file preservation, stale-owned-file retirement, current-process wait, concurrent serialization, injected partial-apply rollback, restart arguments, and post-start health acknowledgement before wiring automatic restart into WPF.

After that boundary is independently green, implement client coordination/UI/startup health integration, then CI updater packaging/publication, then the full end-to-end disposable installation test.

## Continuity

This updater lane inherits the permanent continuity constitution and active Learned Rules. The successor must read, preserve, and recursively propagate those rules to the agent after them. Do not weaken verification or filesystem/recovery invariants to make updater work pass.

## Checkpoint B — apply/rollback helper

Implemented:

- `UpdateInstaller` with packaged-install marker validation, target-build anti-downgrade check, staged marker agreement, exact previous-payload backup, application-owned replacement, conservative stale-owned retirement, installed-byte verification, explicit journal transitions, rollback, and post-health confirmation.
- `release-install.json` is separate updater metadata rather than a `product-files.json` member. This avoids a cryptographic hash cycle because the install marker carries the product-manifest hash. It is still required inside the verified ZIP, validated before apply, backed up, applied, and validated after apply.
- Modified stale files are preserved rather than deleted when their current bytes no longer match the prior owned-file hash.
- Rollback backups record hashes of the exact pre-update bytes, so rollback restores the actual pre-update installation rather than assuming the old manifest still describes every byte.
- New-only files are deleted during rollback only if they still hash to the just-applied update bytes. Unexpectedly modified bytes make rollback fail closed instead of guessing.
- `MhwModManager.Updater.Helper` is a separate executable. It waits for the old PID, owns a cross-process single-writer named semaphore, applies the update, restarts the app with a health token/file, waits for exact build identity acknowledgement, confirms and retires backup on success, or stops the failed new process and rolls back/restarts the previous payload.
- The helper recognizes `AppliedAwaitingHealth` after its own interruption and resumes health confirmation instead of re-backing-up the already-updated install.
- Cross-process serialization uses a named semaphore rather than a named mutex because helper awaits may resume on another thread and mutex release is thread-affine.

Verification for this checkpoint:

- updater focused tests: **33/33 PASS**
- installer/runtime subset: **13/13 PASS**
- strict whole-solution build before final checkpoint cleanup: **PASS, 0 warnings / 0 errors**
- real disposable filesystem tests prove user `Mods`, `State`, and unknown files survive successful updates and injected rollback paths.
- fault injection currently covers after-file replacement and after-stale-owned deletion; further fault points remain to be exercised before final closure.

## Checkpoint C2 — interrupted metadata recovery
- Journal identity is checked before normal installed metadata validation. Applying/BackupCreated/RollbackRequired recover first using a separate bounded recovery token, then reload the restored installation.
- Rollback prevalidates all backup bytes, previous marker/product-manifest agreement and previous ownership before mutating the live tree.
- Three new journal/metadata regressions failed on the preceding checkpoint and passed after repair. Added corrupt-backup regression proves new live files remain untouched if recovery material is corrupt.
- Focused updater tests: 39/39 PASS (Windows x64, SDK 10.0.401). Full release verification remains pending.
- Next: helper start/stop failure recovery; see AUTO_UPDATER_NEXT_AGENT.md. Successor must preserve and recursively propagate continuity.

## Checkpoint C3 — restart recovery order
- Extracted UpdateRestartCoordinator: target launch/health/confirmation failure enters rollback, and inability to prove target process exit blocks rollback while preserving backup/journal.
- Helper uses the coordinator and returns explicit process-stop success instead of swallowing stop failure and continuing mutation.
- Focused updater tests: 41/41 PASS; strict helper build: zero warnings/errors. Tests cover launch failure restoring previous bytes and stop refusal preserving the new bytes plus backup.
- Remaining: real helper-process crash/restart coverage, durable target PID tracking before resumed health recovery, filesystem collision races, WPF/packaging/publication integration, and all release gates.

## Checkpoint C4 — crash-safe target launch identity
- Added durable target-launch state written before process creation, then completed with exact PID + process start time immediately after launch.
- Startup-health acknowledgement is now bound to launch attempt + PID as well as token/build/source identity.
- Helper resume attaches to the exact tracked process instead of launching a duplicate. If a helper dies after recording the attempt but before recording PID, recovery remains fail-closed: it waits for that attempt's health and otherwise preserves backup/journal rather than guessing whether a target was started.
- PID reuse or inability to prove process identity is treated as ambiguous recovery and does not authorize rollback/live mutation.
- Added a real external-helper regression that launches MHW Mod Manager Updater.dll and proves an interrupted launch can be confirmed without relaunching the target.
- Windows x64 / .NET SDK 10.0.401 focused updater suite: **47/47 PASS**.
- Strict whole-solution build: **PASS, 0 warnings / 0 errors**.
- One preceding regression run failed because the test harness constructed bin\\bin instead of bin\\Release for the helper fixture; the production updater was not implicated. The corrected harness passed.
- Full release verification remains pending for these changed production inputs.
- The runtime/recovery support audit has been harvested into this branch. Its duplicate-launch P1 is closed by C4; remaining P1s are rollback executable identity, authenticated GitHub host enforcement, and exact build-metadata agreement.
