# Auto-updater runtime/recovery support audit — 2026-09-28

## Scope and repository truth

- Repository: `fengie/mhw-mods`
- Canonical `main` observed at audit start: `4fd61dd33609a7c55e5aedbaad026266a410f942`
- Senior implementation branch reviewed: `agent/auto-updater-20260928`
- At review time that branch was 2 commits ahead of `main` and contained Checkpoint A (discovery/download/staging) plus Checkpoint B (external helper/apply/rollback).
- Existing release/security support branch reviewed: `agent/support-auto-updater-release-security-audit-20260928`
- This support branch deliberately does **not** modify updater production code. It records concrete adversarial findings and regression targets so the senior agent can fix them in its own lane without merge collisions.
- `heaven2` dropped offline during this review, so no local Windows test execution is claimed here.

## Executive result

The senior branch has a strong safety baseline: explicit product ownership, verified staging, external helper apply, rollback backups, cross-process update serialization, and post-start health acknowledgement are already present.

### Reconciliation after senior checkpoint C2

While this audit was being written, the senior branch advanced from 2 to 4 commits ahead of `main`. Its new Checkpoint C2 now states that journal identity is checked before interrupted-metadata recovery and adds identity-mismatch regressions. That supersedes the lower-priority journal/source-SHA finding below; it is retained only as historical review context and should **not** be re-opened unless a newer regression disproves C2.

Checkpoint C4 closes duplicate-launch recovery; C5 closes rollback executable identity; C6 closes authenticated GitHub host enforcement and redirect token leakage. The only remaining live P1 finding from this audit is exact cross-file build identity agreement.

---

## P1 — Helper crash/restart can launch a duplicate updated app and the health acknowledgement is not launch-attempt-bound

### CLOSED by Checkpoint C4 — durable launch-attempt/process recovery

The production updater now persists a launch-attempt record before target process creation, then atomically records the launched PID plus exact process start time. Startup health is bound to the launch attempt and target PID in addition to token/build/source identity. On helper restart, a live tracked target is reattached and observed rather than relaunched; an ambiguous pre-PID launch is never guessed safe and does not trigger a duplicate target. PID reuse/identity ambiguity fails closed.

Windows/.NET 10.0.401 evidence for this checkpoint: focused updater suite **47/47 PASS**, strict whole-solution build **PASS with 0 warnings / 0 errors**, and a regression launches the real external `MHW Mod Manager Updater` process to prove resumed health confirmation completes without starting another target. One preceding test run failed only because the regression derived `bin\bin` instead of `bin\Release` for the helper fixture; correcting that test-path bug produced the green 47/47 run.

### Observed code path

In `src/MhwModManager.Updater.Helper/Program.cs`, when the helper restarts and finds:

`UpdateJournalPhase.AppliedAwaitingHealth`

it calls `UpdateHealthProtocol.WaitForHealthyAsync(... process: null, timeout: 1 second ...)`. If no health file arrives within that one-second window, it immediately calls `LaunchAndConfirmAsync` and starts another application process.

The journal/request does not persist the PID of the already-launched updated application, and the health record contains token/build/source but no launched PID or per-launch attempt identifier.

### Failure mode

1. helper applies the new payload;
2. helper launches new app A;
3. helper crashes before confirmation;
4. app A is still starting and needs more than one second to reach the health checkpoint;
5. helper is restarted;
6. restarted helper sees `AppliedAwaitingHealth`, waits one second with no process handle, then launches app B;
7. app A may then acknowledge the same health token/build/source.

This creates an avoidable duplicate manager instance. It also means the helper cannot prove which launched process produced the successful acknowledgement.

### Required fix

Persist enough launch state to resume the same attempt safely, for example:

- launched target PID;
- launch attempt ID / nonce;
- launch timestamp / health deadline.

On helper resume:

- if the recorded target PID is still alive, continue waiting for that attempt's health or exit;
- do not start a second target process merely because health took more than one second;
- bind the health record to the launch attempt (and preferably PID) as well as build/source;
- only create a new launch attempt once the prior attempt is proven gone/failed.

### Regression test

Inject helper interruption after target process creation but before health confirmation. Restart the helper while the first target process remains alive. Assert:

- no second target process is launched;
- the original target can acknowledge health successfully;
- an acknowledgement from the wrong attempt/PID cannot confirm a later attempt.

---

## P1 — Rollback restart always uses the *new* manifest executable path

### CLOSED by Checkpoint C5 — restored release owns its restart executable

`release-install.json` now records `ExecutableRelativePath`. The installed/staged/rollback product manifests must own the declared executable, the staged marker must agree with the target manifest, and rollback reloads the restored marker before restart. A Windows regression updates from `old-manager.exe` to `new-manager.exe`, forces target launch failure, proves rollback restores `old-manager.exe`, removes the new executable, and passes the restored old executable path to restart. Focused updater suite: **48/48 PASS**; strict whole-solution build: **0 warnings / 0 errors** on .NET SDK 10.0.401.

### Observed code path

`Program.StartApplication` resolves the executable from:

`request.Manifest.ExecutableRelativePath`

The same method is used after rollback.

### Failure mode

If a future update legitimately moves or renames the executable:

- old build executable: `MHW Mod Manager.exe`
- new build executable: `MHW Manual Mod Manager.exe`

then a failed new-build health check can correctly restore the old payload, but the helper will still try to launch the new executable path from the rolled-back installation. Rollback can therefore restore bytes successfully yet fail to restore service.

### Required fix

Capture the previous build's restart executable independently of the target manifest, e.g. in the installed release marker or rollback metadata.

Use:

- target executable for launching the new build;
- previous executable for restart after rollback.

Do not assume the executable relative path is invariant forever.

### Regression test

Create an old fixture whose executable path differs from the target manifest. Force target startup-health failure. Assert rollback restores the old payload and restarts the old executable path.

---

## P1 — Authenticated GitHub requests do not enforce an HTTPS/API-host allowlist before attaching the bearer token

### CLOSED by Checkpoint C6 — authenticated origin enforcement + real redirect proof

Authenticated updater requests now reject any non-HTTPS URI, any host other than exact `api.github.com`, non-default ports, and user-info **before** creating a request with the bearer token. Windows/.NET 10.0.401 regressions prove rejected off-host/insecure candidates never reach transport and approved API requests receive the token. A real loopback TLS redirect fixture exercises `SocketsHttpHandler`: the first `api.github.com` request carries `Authorization: Bearer`, GitHub-style `302` follows to a simulated release-asset host, and the redirected request contains no Authorization header. This matches Microsoft .NET 10 redirect documentation and GitHub's documented release-asset `200`/`302` behavior. Focused updater suite: **54/54 PASS**; strict solution build: **0 warnings / 0 errors**. The first TLS-fixture attempt failed because Windows Schannel rejected an ephemeral server key; the fixture was corrected to re-import a persisted user key rather than weakening certificate validation.

### Observed code path

`GitHubUpdateSource.CreateRequest(method, uri, token)` accepts an arbitrary URI string and unconditionally adds:

`Authorization: Bearer <token>`

`UpdateCandidate` exposes artifact/manifest URIs, and the normal discovery path accepts asset API URLs from release JSON without an explicit scheme/host check.

### Risk

The intended source is GitHub's authenticated API. The code should make that trust boundary explicit rather than relying on all callers and future refactors to keep candidate URIs safe.

A malformed/poisoned candidate or future persistence path must never be able to send the private-repository credential to an arbitrary host.

### Required fix

Before attaching credentials:

- require `https`;
- require the authenticated API request host to be exactly the approved GitHub API host (`api.github.com` for the current repository);
- treat release-asset download redirects separately so credentials are not forwarded to an unapproved redirect host;
- fail closed before network I/O for an off-host candidate URI.

If redirect handling remains automatic, add a test proving the Authorization header is not forwarded off the API host.

### Regression tests

- candidate URI `https://example.invalid/asset` is rejected before send;
- candidate URI `http://api.github.com/...` is rejected before send;
- approved `https://api.github.com/...` receives auth;
- redirect to the release asset CDN succeeds only without leaking the bearer token.

This closes a requirement already identified by the release/security audit: fixed/expected release-host policy.

---

## P1 — Target build metadata is not fully cross-checked across update manifest, install marker, and shipped build identity

### Observed code path

`UpdateInstaller.ValidateAndLoadAsync` checks the staged install marker against the update manifest for:

- build number;
- source SHA;
- product-manifest SHA-256.

It does **not** currently prove exact agreement for all identity fields such as product version/channel, nor does it semantically validate the shipped `build-identity.json` against the target update manifest before live mutation.

`ReleaseInstallMarker.Validate` validates its top-level channel but does not require the nested `Build.Channel` to equal the protocol channel.

`UpdateBuildIdentity.Load` primarily validates schema version.

### Risk

Publication should generate consistent metadata, but the client is the final fail-closed boundary. A packaging/publication bug should not allow mutually inconsistent metadata to become the installed truth.

### Required fix

Before backup/live mutation, cross-check the exact target identity among:

- `update-manifest.json`;
- staged `release-install.json`;
- staged `build-identity.json`.

At minimum require exact agreement on:

- schema/protocol compatibility;
- channel;
- product version;
- source SHA;
- build number.

Also require the installed old marker's nested build identity to be internally consistent with its top-level channel/product assumptions.

### Regression tests

- staged marker product version disagrees with update manifest -> reject before backup;
- staged marker nested build channel disagrees -> reject before backup;
- staged `build-identity.json` source SHA/build number/product version disagrees -> reject before backup;
- a fully consistent fixture still applies.

---

## CLOSED BY LATER SENIOR CHECKPOINT C2 — Recovery transaction identity

### Current status

The senior branch's later Checkpoint C2 reports that journal identity is now checked before normal installed-metadata validation, and that new identity-mismatch regressions failed before the repair and pass afterward. Treat this item as closed by newer production work unless later code review or tests show otherwise.

---

## Lower-priority observations for the senior agent

These are worth tracking but are less urgent than the P1 items above:

1. Release discovery fetches only the first 100 releases. The current highest-build selection is correct within that page, but long-lived continuous publication should eventually have an explicit pagination/retention policy.
2. Discovery currently does not distinguish prerelease releases. If the publication workflow can ever create prereleases, decide explicitly whether the `main` channel should ignore them.
3. `Local\\MHWMM.Update.<hash>` serializes helper processes within a Windows session. If cross-session updates of the same writable installation are in scope, document or strengthen that boundary.
4. PID reuse is theoretically possible between application exit and helper lookup. This is low risk on the current desktop model but can be reduced if the request records process start identity as well as PID.

---

## Integration guidance

This support audit is intentionally orthogonal to:

- the senior production branch `agent/auto-updater-20260928`;
- the release/security audit branch;
- archive resource/cancellation hardening;
- unrelated CAS/filesystem support lanes.

Recommended senior-agent order:

1. bind helper health recovery to one launch attempt;
2. make rollback restart use prior-build executable metadata;
3. add authenticated-host enforcement;
4. enforce exact cross-file build identity agreement;
5. add source SHA to recovery transaction identity;
6. run focused updater tests;
7. continue WPF/CI publication integration.

Do not merge this branch wholesale if newer production work has already implemented these findings. Harvest only still-relevant audit content/tests.

## Verification claim

This checkpoint is a source review only.

- Reviewed senior updater discovery/download/staging code.
- Reviewed path-safety and package-verification code.
- Reviewed installer/apply/rollback code.
- Reviewed helper/restart/health code.
- Reviewed updater integration tests currently present on the senior branch.
- No production source was changed here.
- No Windows test/build result is claimed because the remote Windows device became unavailable during this support pass.

The next agent must preserve the repository continuity constitution and recursively propagate it again.
