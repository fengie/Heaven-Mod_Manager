# Auto-updater WPF integration audit

## Scope and repository truth
- Repository: `fengie/mhw-mods`.
- Audit branch: `agent/support-updater-wpf-integration-audit-20260928`.
- Audited updater base: `112be5bc51e660decf0a48e280afb92bc82a7133` (C9 continuity checkpoint).
- Production/test source for C9: `8cdf54d0bc4065a55124aef96c68b26de2a78f0c`.
- This is a documentation-only support audit. It intentionally does not compete with the primary updater implementation agent.
- C9 already closes updater-level LR-003 native 1175/1176/1177 behavior. The next bounded production lane is WPF/lifetime integration.

## Executive findings
1. The App project does not reference `MhwModManager.Updater`; WPF cannot call the health/check/staging APIs until that reference is added.
2. Health acknowledgement must be late enough to prove normal startup succeeded. The current startup path can still fail during migration, catalog refresh, Nexus/game-build work, deployment recovery, automation maintenance, MainWindow construction, or MainWindow initialization.
3. The helper currently waits only 90 seconds for startup health. A late, meaningful health checkpoint plus the existing startup workload can exceed that; the timeout and checkpoint must be designed together.
4. Update discovery/staging must not be inserted into the fatal startup path. The updater architecture already requires network/auth failures to be non-fatal to normal launch.
5. The existing WPF `CriticalOperation`/busy model is the correct handoff boundary, but a one-time check is insufficient: a new operation can start between “safe” observation and helper launch/shutdown.
6. The current UI already uses “Updates” for Nexus/mod updates. Product self-update state must use distinct names such as `ProductUpdateStatus` / `CheckProductUpdateCommand`.
## P0 restart-argument trap

A future update request must **not** copy the current process arguments verbatim into `UpdateApplyRequest.RestartArguments`.

After an updater-driven restart, the app process contains:
- `--mhw-update-health-token <old>`
- `--mhw-update-health-file <old>`
- `--mhw-update-health-attempt <old>`

The helper later appends the next update's fresh health arguments to `RestartArguments`. `UpdateHealthProtocol.GetArgument` returns the **first** matching flag. Therefore stale health arguments preserved from the current process would win over the fresh ones, causing the next update's health handshake to use the wrong token/file/attempt and time out or roll back.

Required contract:
- sanitize restart arguments before persisting an update request;
- remove each updater-owned health flag **and its following value**;
- reject malformed dangling updater-owned health flags rather than guessing;
- preserve unrelated user/application arguments in original order;
- add a regression that performs two logical updater restarts and proves only the newest health tuple is observed.

This should be fixed in a reusable updater-layer helper rather than hand-coded in WPF.

## Startup health acknowledgement

Recommended success checkpoint in current App startup:
1. complete all currently fatal startup work;
2. construct `MainWindow`;
3. await `MainWindow.InitializeAsync()`;
4. show the window;
5. initialize the watchdog;
6. then acknowledge updater health and mark startup complete.

Do not acknowledge immediately after `base.OnStartup`, service construction, or deployment recovery. Any fatal stage after acknowledgement would let the helper confirm a build that the user cannot actually start normally.

The health caller should:
- derive install root from `AppContext.BaseDirectory`, not `AppPaths.ToolRoot` (the latter can be redirected by manager-home environment variables);
- load `build-identity.json` with `UpdateBuildIdentity.LoadRequiredAsync`;
- load/validate `release-install.json` and require it to describe the same shipped build identity before acknowledgement;
- call `UpdateHealthProtocol.AcknowledgeIfRequestedAsync(e.Args, currentIdentity, ...)` only when the health tuple is present;
- log build number/short SHA/attempt only, never token contents.
## Health timeout contract

The helper currently uses `TimeSpan.FromSeconds(90)` in both launch and resume health paths.

That number is risky with the current startup sequence because normal launch performs disk/database work, optional migration, catalog refresh, Nexus/game-build work, deployment recovery, startup automation, and UI initialization before a meaningful late health checkpoint.

Recommendation:
- centralize the health timeout in `UpdateProtocol` or the apply request;
- choose a value compatible with worst-case supported startup, rather than duplicating a magic 90 seconds in helper methods;
- keep process-exit as an immediate negative signal;
- add a fixture where health arrives later than 90 seconds conceptually (use a short injected timeout/delay in tests, not a real multi-minute test);
- prove a slow-but-healthy launch does not get rolled back solely because optional startup work is slow.

Do not solve this by acknowledging health earlier than the real success boundary.

## Background check and staging

Start product update discovery only after successful normal startup and UI display. It should run under a dedicated lifetime cancellation token and never block the dispatcher.

Suggested state machine:
`DisabledDevelopment -> Idle -> Checking -> UpdateAvailable -> Staging -> ReadyToRestart -> Handoff`
with a separate non-fatal `CheckUnavailable`/error detail.

Rules:
- `UpdatePathSafety.IsDevelopmentLayout(installRoot)` disables self-apply in repository/development layouts.
- Missing credential means “update check unavailable/not configured,” not app startup failure.
- HTTP/auth/metadata/staging failures update product-update status and logs but do not terminate the app.
- Never surface or log the GitHub token.
- Background staging may continue while the user works because it writes only under LocalAppData; live installation mutation remains helper-only.
- Dispose/cancel the updater lifetime token from the same App/ViewModel lifetime that owns the task.
## Operation-aware handoff

The existing `MainWindowViewModel.RunBusy` sets `CriticalOperation=true` for non-cancellable operations, and `MainWindow.OnClosing` blocks ordinary close while that flag is true. Reuse this invariant; do not bypass it with `Environment.Exit`, process kill, or unconditional shutdown.

A safe handoff needs an atomic UI/lifetime gate:
1. user invokes “Restart to update”;
2. acquire an updater-handoff gate that prevents any new `RunBusy` operation from starting;
3. verify no busy/critical operation is active;
4. verify the staged update still matches the intended candidate and current installed identity;
5. create request paths/token under the updater root;
6. copy/verify the helper into an updater-owned LocalAppData execution directory;
7. durably write the request;
8. start the copied helper and prove process start succeeded;
9. only then call normal WPF shutdown;
10. if any pre-shutdown step fails, release the handoff gate and leave the current app fully running.

The check and gate must be one protocol. A plain `if (!CriticalOperation)` followed by asynchronous preparation leaves a race where another command can begin before shutdown.

`RunBusy` should reject new work while a handoff is armed, and periodic metadata refresh should treat an armed handoff like a critical operation.
## Verified helper copying

The helper is a separate project/assembly (`MHW Mod Manager Updater`). Current `Build-Release.ps1` publishes only the App project and does not yet establish a packaged helper contract; packaging/publication is the next independent checkpoint.

For C10 WPF integration:
- do not assume a repository `bin` path;
- define the packaged helper location relative to the install root;
- copy every file required for that helper invocation to an updater-owned LocalAppData execution directory before app shutdown;
- use create-new / collision-safe destination naming per attempt;
- verify copied bytes against trusted packaged ownership metadata before launch;
- ensure the execution directory is not a reparse point and stays under `UpdatePackageStager.GetUpdaterRoot()`;
- leave the installed helper untouched during handoff; live replacement occurs only after the app exits;
- if helper packaging is not present in the current build, keep apply/restart disabled while check/stage can still be tested independently.

Do not make WPF integration silently invent packaging metadata. Build-Release remains the authority for what is actually shipped.

## Minimal UI surface

Avoid the existing mod-update terms `UpdateCount`, `UpdatesViewLabel`, and DB keys prefixed `update:`.

A minimal product-updater surface can fit the existing dashboard/status area:
- text: `ProductUpdateStatus`;
- manual action: `CheckProductUpdateCommand`;
- when staged: `RestartToUpdateCommand`;
- optional display of target product version/build/short SHA.

No credential input is required in this slice. Credential management can remain Windows Credential Manager / environment override as already implemented.
## Recommended implementation order

### Slice C10a — health/lifetime plumbing
- Add App -> Updater project reference.
- Add restart-argument sanitization and tests in updater core.
- Add packaged-install identity validation helper if needed so the app does not duplicate C7 equality rules.
- Wire late startup health acknowledgement.
- Centralize/adjust health timeout.
- Tests: success, fatal-before-health, identity mismatch, stale health args, development layout.

### Slice C10b — check/stage client
- Add one app-layer product-update coordinator/service.
- Construct HttpClient/GitHubUpdateSource/UpdatePackageStager there.
- Begin background check only after successful startup.
- Keep errors non-fatal and secret-safe.
- Expose immutable/status properties to the VM.
- Tests: no candidate, candidate, missing credential, auth/network failure, cancellation/disposal.

### Slice C10c — UI + handoff
- Add distinct product-update status/manual-check UX.
- Add the handoff gate and integrate it into `RunBusy`.
- Build request with sanitized restart args and exact current PID/install root.
- Copy + verify helper, write request, start helper, then normal shutdown.
- Tests: busy operation defers/rejects handoff; helper copy/request/start failure leaves app alive; successful handoff blocks new work and closes only after helper start.
## Verification expectations

For every production method added/moved in App/VM/updater layers:
- preserve `MasterDebugLog.BeginMethod()` entry tracing per LR-001;
- run the function verifier; do not manually promote caches;
- strict whole-solution build with warnings as errors;
- focused updater tests plus affected App/integration tests;
- full IntegrationTests;
- `scripts/Test-AgentHandoff.ps1`.

Because this machine (`heaven`) currently has only .NET SDK 7.0.401 while the repo pins 10.0.401, this support audit makes **no compile/test claim**. C9 itself was verified by the primary updater lane on a suitable Windows/.NET 10.0.401 environment.

Do not run full Verify-Release / Build-Release merely to validate an unfinished C10 sub-slice unless the primary agent chooses to close that exact source boundary. Packaging/publication and hosted release closure remain the following independent checkpoint.

## Acceptance tests that should exist before calling WPF integration complete
- no health file when startup fails before the real success checkpoint;
- exact target build/PID/attempt health acknowledgement after successful startup;
- stale health flags removed from preserved restart arguments;
- background check failure cannot fail startup;
- development layout cannot self-apply;
- handoff cannot race a foreground critical operation;
- new foreground operation cannot start after handoff is armed;
- helper copy/request/start failure does not shut down the app;
- helper process starts before WPF shutdown begins;
- product-update cancellation/disposal leaves no unobserved task exception;
- UI never exposes GitHub credential/token text;
- product self-update wording is distinct from mod/Nexus “Updates”.

## Handoff to primary updater agent

The highest-value blocker discovered here is the stale health-argument replay bug. Fix that before constructing `UpdateApplyRequest.RestartArguments` from `e.Args`.

Then implement the WPF slice in the order above. Keep C10 independent from Build-Release/publication so failures are attributable. Preserve the permanent continuity constitution and active Learned Rules, and require the successor to recursively propagate them.
