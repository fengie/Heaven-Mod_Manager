# Disposable packaged updater harness — 2026-09-28

## Status and exact scope

**Harness implemented; real packaged helper/app scenarios NOT RUN. No end-to-end or hosted-release closure is claimed.**

Task: `task-20260928133350-vrokm`; mutable boundary: `updater-disposable-e2e`.
Branch: `agent/control-test-narrow-test-scope-only-build-a-r-20260928133350-vrokm`.
Verified starting HEAD and remote `fengie/mhw-mods` main both exactly
`a83dc6e047ccf98e896f10c25772df99b95426d1`. Initial status was clean.
The commit containing this report identifies the harness source; production remains the exact starting source.

No production, publication, CI, verification-cache, main, or other checkout changes.
No release published, downloaded, or fabricated. No exploit/vulnerability reproduction.

## Repository reconnaissance

Read AGENTS, startup/read-order/continuity/active rules, updater implementation and continuation documents, relevant verification/safety/release/role doctrine, and inspected helper, installer, restart coordinator, health protocol, WPF startup/path discovery, package verifier and existing runtime/installer tests.
Relevant history includes `2e136cc` (package metadata), `99354ce` (metadata/rollback tests), and `08054d9` (publication race check).
Local and live remote updater/support branch inventories were searched; no disposable harness branch was found. Existing coverage already exercises coordinator seams, PID wait, health identity and helper resume; this addition targets packaged application/helper composition.

Initial sandboxed Git commands failed because the worktree Git metadata lives outside the permitted worktree. Read-only escalated Git commands established the branch/base/status/history/remote truth; no repair or checkout switch was attempted.

## Files and intended acceptance criteria

- `scripts/Test-UpdaterDisposable.ps1`: takes two existing ZIP/manifest pairs, verifies them with the unchanged package verifier, requires increasing build numbers and different source SHAs, then constructs fresh installs and staging areas.
- `scripts/Test-UpdaterDisposable.Tests.ps1`: independently exercises harness assertions without executing the orchestration or launching manager/updater processes.

The process harness refuses to run without `-DisposableProfile` and refuses any existing `%LOCALAPPDATA%/MhwModManager` directory. The helper resolves the Windows Known Folder directly; changing the `LOCALAPPDATA` environment variable is not accepted as proof of isolation. Run in a new disposable Windows account/VM, with no saved manager credentials or real game data. Each run consumes that fresh profile; discard/recreate it to repeat. No cleanup is guessed after failure.

For each scenario it verifies the actual old WPF process's startup acknowledgement, then terminates that fixture process before invoking a copied, verified old helper closure. It supplies explicit isolated manager/game environment paths and a non-executable game-presence file, never a real game install.

Success assertions: helper exit 0, Confirmed journal, new process health token/attempt/PID/path/source SHA/build identity, every target-owned byte/size, exact installed metadata, and unchanged seeded Mods/State/unknown-file hashes.

Rollback assertions: helper exit 5, RolledBack journal, no target health acknowledgement, a live restored old app acknowledging exact old build/source with a separate rollback token/attempt, restored old owned bytes/sizes and metadata hashes, removal of new-only owned paths, and unchanged seeded user-file hashes.

Fault injection modifies only the disposable target's root app runtimeconfig to invalid JSON and regenerates its staged ownership hash and marker. This permits the real installer to apply the deliberately non-starting payload and exercises real helper rollback. Input archives are untouched. **This fault variant is synthetic and is not the published target artifact**, even though its build-identity file is retained. Its injected runtimeconfig digest is separately recorded. It must never be uploaded as a release.

The helper's normal target health tuples are retained for success. Rollback restart arguments carry a separate old-build health tuple because the production helper otherwise restarts the old application without an acknowledgement request. This is direct helper orchestration, not the WPF user-click/handoff path.

Output is `summary.json` within the newly generated disposable profile run directory. It contains archive SHA-256s, cases, seed hashes, journal phases, helper exit codes, restarted build metadata and injected-file digest. Raw logs/requests remain local to the disposable VM and may include paths/tokens; do not export them unsanitized. On failure the summary says FAIL, preserves completed-case evidence, and reports only the exception type. Processes and recovery evidence are deliberately retained for diagnosis; discard the VM only after inspecting it.

## Commands actually run and results

Windows PowerShell 5.1; installed .NET SDKs reported by `dotnet --list-sdks`: 7.0.401 and 10.0.401. No .NET build/test gate was run for this PowerShell-only change.

```powershell
git status --short
git branch --show-current
git rev-parse HEAD
git ls-remote https://github.com/fengie/mhw-mods.git refs/heads/main
git log -5 --oneline
git branch -a --list '*updater*' '*disposable*'
git ls-remote --heads origin '*disposable*' '*updater*'
```

Results: clean assigned branch; local and remote exact base match as above; relevant active branches inspected without modifying them.

PowerShell AST parse of the harness: PASS, zero parse errors.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-UpdaterDisposable.ps1
# Expected refusal, exit 1: requires disposable profile.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-UpdaterDisposable.ps1 -DisposableProfile
# Expected refusal, exit 1: four explicit input paths required.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-UpdaterDisposable.Tests.ps1
# PASS: 11 contract checks; no app/helper launch.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AgentHandoff.ps1
# PASS: version 8.8.0, 79 required files.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AgentHandoff-NegativeFixtures.ps1
# PASS: baseline accepted, all four broken-continuity fixtures rejected.
```

The 11 contract checks cover valid owned bytes, changed digest, incorrect length, wrong health token, wrong attempt, wrong build number, wrong source SHA, wrong executable path, process predating the run, valid current-process identity, and normal non-reparse ancestry. Tiny fixtures remain under ignored worktree `artifacts/`. Reparse refusal is implemented but was not exercised with a junction fixture; no containment race claim is made.

## Blockers, limitations and exact next command

The only ZIP found in the assigned worktree was the trusted source-baseline archive, not an executable updater package. No eligible old/new release pair or fresh disposable profile was supplied. Hosted publication was already reported blocked by the task; its state was not independently re-verified or changed here. Building/publishing release artifacts and CI work belong to other agents.

Prerequisites: obtain two legitimately built, updater-capable win-x64 ZIPs and matching manifests from the release owner, with distinct source SHAs and increasing build numbers. Confirm their release provenance separately. Copy this branch's scripts into a fresh disposable Windows VM/account with an interactive desktop, no manager profile, no saved updater credentials, and no real game data. Then run (replace placeholders with those existing input files):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-UpdaterDisposable.ps1 `
  -DisposableProfile `
  -OldArtifactPath '<old-package.zip>' -OldManifestPath '<old-update-manifest.json>' `
  -NewArtifactPath '<new-package.zip>' -NewManifestPath '<new-update-manifest.json>'
```

Inspect both cases and retained process/recovery evidence. Persist sanitized exact package digests and observed outcomes. If it fails, preserve the failure and fix only the test boundary or return a product defect to its owner; never relax assertions to get green.

Not proven: packaged execution, runtimeconfig failure behavior on the target machine, full-directory user-state immutability (the running app legitimately writes state), live download/authentication/immutable release provenance, WPF update initiation/handoff, PID wait during live shutdown (old fixture is stopped first), crash/resume, or hosted old-to-new closure. Contract checks cannot substitute for these runs. The main process orchestration remains unvalidated until those prerequisites are available.

## Continuity and knowledge checkpoint

Reusable lesson review: existing verification doctrine already requires exact artifact/process evidence and honest limitations, and existing physical-containment doctrine covers Known Folder isolation. No duplicate trainer rule was added, and no global canonical-state snapshot was overwritten by this support branch.

The successor must inherit, read, preserve and recursively propagate `AGENTS.md`, the permanent continuity constitution, and active Learned Rules. Update this evidence with exact commands/results and commit/push the next coherent checkpoint. Require your successor to pass the same obligation to the agent after them. Do not break the chain.
