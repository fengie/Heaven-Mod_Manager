# Auto-updater release/security audit — 2026-09-28

## Canonical source inspected

- Repository: `fengie/mhw-mods`
- Canonical branch: `main`
- Canonical HEAD inspected: `4fd61dd33609a7c55e5aedbaad026266a410f942`
- Support branch: `agent/support-auto-updater-release-security-audit-20260928`
- Scope: design/audit only. No production updater code, release workflow behavior, verification caches, or product tests are changed by this checkpoint.

This audit is intentionally separate from the current archive extraction streaming-cancellation/resource-budget production boundary.

## Why this was selected

Current support work already covers archive cancellation/resource budgets, archive physical-root containment, recursive source reparses, CAS identity/digest safety, diagnostics, migration, backup, networking, UI ownership, and related lanes. No updater-specific branch or pull request was visible when this audit started.

The user is separately pursuing an automatic updater. A release/update safety audit is therefore useful parallel work because it can define invariants and tests without competing for the implementation boundary.

## Files and behavior inspected

- `VERSION.txt`
- `Directory.Build.props`
- `scripts/Build-Release.ps1`
- `scripts/Verify-Release.ps1`
- `.github/workflows/windows-release-gate.yml`
- `src/MhwModManager.App/MhwModManager.App.csproj`
- `src/MhwModManager.App/App.xaml.cs`
- `src/MhwModManager.App/MainWindow.xaml.cs`
- `README.md`
- `_AGENT_TRAINING/CI_RELEASE_ENGINEERING.md`
- current continuity/support integration state and open PR/branch inventory

The audit distinguishes confirmed current repository behavior from requirements for a future updater.

---

# Executive result

There is no P0 in current shipped behavior because the inspected canonical source does not yet expose a self-update path.

There are, however, several **P1 design constraints that must be satisfied before an automatic updater is safe to ship**:

1. **Never replace/clean the entire tool root.** By default the application makes `AppContext.BaseDirectory` the tool root and stores `Mods`, `Inbox`, `Mods Archive`, `State`, and per-game workspaces beneath that root. Recursive directory replacement can destroy user state.
2. **Do not let the running WPF process update its own loaded executable/DLL set in place.** Stage and verify first, then hand off application to an external updater helper after the app exits.
3. **Publish immutable update metadata bound to the exact verified artifact.** The current Windows gate produces a ZIP and SHA-256 but does not publish a durable updater manifest/GitHub Release asset in the inspected workflow.
4. **Update authenticity/integrity and rollback are release invariants, not UI polish.** A download must be verified before use, apply must be journaled/recoverable, and a failed new-version startup must not strand a half-updated installation.
5. **Do not apply an update while the application reports a critical transactional operation.** `MainWindow.OnClosing` already blocks normal close during `CriticalOperation`; the updater must preserve that invariant rather than force-killing the process.

A safe implementation is feasible, but it should be built as a separate verified subsystem rather than as “download ZIP and overwrite folder.”

---

# Confirmed repository strengths

## Exact Windows release gate already exists

`.github/workflows/windows-release-gate.yml` checks out the exact revision, installs the pinned .NET SDK, records provenance, runs `Verify-Release.ps1`, runs `Build-Release.ps1`, and uploads build/verification evidence.

The workflow also already contains a correct exact-input race rule for verification-cache persistence: if `main` advances while an older run is finishing, the older run does not rebase/replay its evidence onto the newer source.

That exact-input discipline should be reused for update publication.

## Release artifact is already versioned and hashed

`Build-Release.ps1` reads `VERSION.txt`, publishes a self-contained `win-x64` build, creates:

`MHW-Manual-Mod-Manager-v<version>-win-x64.zip`

and computes a SHA-256 for the completed ZIP.

This is a strong base for update metadata because the updater can be bound to the exact artifact users receive rather than reconstructing files independently.

## Application already has explicit shutdown safety

`MainWindow.OnClosing` refuses normal application close while `MainWindowViewModel.CriticalOperation` is true.

The updater should integrate with this instead of introducing a second shutdown policy.

## Current product/data topology is explicit

`AppPaths.Discover` resolves the default tool root from `MOD_MANAGER_HOME` / `MHW_MANAGER_HOME`, otherwise from `AppContext.BaseDirectory`.

For the Monster Hunter: World workspace, the default paths include:

- `<tool>\Mods`
- `<tool>\Inbox`
- `<tool>\Mods Archive`
- `<tool>\State\Next`

Generic games also keep isolated workspaces under the same tool/state root.

This makes the preservation boundary knowable and testable.

---

# Findings

## P1 — Whole-folder replacement would cross the user-data trust boundary

**Confirmed repository behavior:** `AppPaths.Discover` defaults `ToolRoot` to the executable base directory and places persistent mod/state workspaces under that root.

**Risk:** an updater that extracts a new release to the installation root and deletes/replaces the old directory recursively can delete or overwrite user-owned state even if the release ZIP itself contains no `State` or `Mods` directory.

**Required invariant:** updater apply logic may mutate only an explicit product-owned file set. It must never infer “everything under installation root belongs to the program.”

Recommended product-owned manifest:

`product-files.json`

For every file shipped by the release, record normalized relative path, length, and SHA-256. The updater should replace/remove only paths proven to be owned by the prior or new product manifest. Unknown files and user-data roots are preserved.

Explicit never-delete roots should include at least:

- `State/`
- `Mods/`
- `Inbox/`
- `Mods Archive/`
- `Games/`
- runtime/startup diagnostic folders and user-generated support material

## P1 — Update publication is not yet a stable discovery protocol

**Confirmed repository behavior:** the inspected `windows-release-gate.yml` uploads GitHub Actions artifacts with 30-day retention and persists verification evidence, but it does not define a stable updater feed or GitHub Release publication step.

**Risk:** an implementation that scrapes Actions artifacts, branch files, commit ZIPs, or “latest main” has ambiguous versioning, retention, artifact identity, and rollback semantics.

**Required invariant:** the updater consumes an immutable release manifest generated only after the exact Windows verification/build gate succeeds.

Suggested `update-manifest.json` fields:

- schema version
- channel (`stable`, optionally `edge`)
- product version
- monotonically ordered build/release sequence
- source commit SHA
- GitHub Actions run ID
- artifact filename
- artifact byte length
- artifact SHA-256
- product-file-manifest SHA-256
- minimum supported updater/bootstrap version
- publication timestamp
- optional release notes URL
- optional detached signature information

The manifest must be produced from the final artifact, not authored before the build and merely trusted afterward.

## P1 — “Update on every push” needs an explicit channel/version rule

`VERSION.txt` and `Directory.Build.props` currently both say `8.8.0`, while `InformationalVersion` is `8.8.0-function-verification`. `Build-Release.ps1` names the ZIP from `VERSION.txt`.

A Git push does not necessarily change the semantic version, and documentation/evidence-only commits can advance `main` without changing the application binary.

If every green `main` push is published as an update without a separate build sequence, multiple releases can have the same semantic version and users cannot reliably determine freshness.

Recommended policy:

- **stable channel:** publish/update only when the declared product version advances and the exact candidate passes the Windows release gate;
- **edge channel (optional):** publish every eligible green production build using a monotonic CI build number plus source SHA, with explicit opt-in;
- do not publish a new updater payload when the built product artifact hash is unchanged.

The stable updater should not silently turn every documentation commit into a user-visible release.

## P1 — Applying an update from inside the running WPF process is the wrong transaction boundary

The active process has its executable and assemblies loaded. It also owns application lifecycle/services and may be inside a transactional operation.

Required design:

1. app discovers metadata;
2. app downloads to a dedicated staging location;
3. app verifies manifest/authenticity/hash/size;
4. app fully extracts and validates staging without touching installed product files;
5. app writes an update journal;
6. app requests a normal shutdown only when `CriticalOperation == false`;
7. an **external updater helper** waits for the application PID to exit;
8. helper applies only product-owned paths;
9. helper launches the new application;
10. new application reports a startup-success handshake;
11. helper deletes rollback backup only after success; otherwise it restores the previous product files.

Do not use `Process.Kill` as the normal update path.

## P1 — Update apply needs its own durable recovery journal

An update is a migration of the executable installation. It should not reuse the live-game deployment journal because the ownership and recovery targets differ.

Suggested phases:

- `Downloaded`
- `Verified`
- `Extracted`
- `ReadyToApply`
- `BackupCreated`
- `Applying`
- `AppliedAwaitingHealth`
- `Committed`
- `RollbackRequired`
- `RolledBack`

Journal fields should include source/target version, source commit, expected artifact hash, product-manifest hashes, install root identity, staged path, backup path, app PID, and last completed phase.

On helper/app restart, ambiguous state must fail closed and offer/perform deterministic rollback rather than guessing that files are complete.

## P1 — Update download integrity must be verified before extraction or execution

The existing build computes SHA-256 but the current inspected release workflow does not expose a durable update manifest carrying that identity.

Minimum integrity requirement:

- HTTPS only;
- fixed/expected release host policy;
- bounded redirects;
- expected content length/resource budget;
- write to a temporary/staging file;
- SHA-256 of the fully downloaded asset must equal immutable release metadata before extraction;
- extracted product files must match `product-files.json`;
- no executable from staging runs before verification.

For stronger authenticity, sign the release manifest with a key whose public verification material ships with the application, or adopt an equivalently verifiable release-signing/attestation model. A checksum fetched from the same compromised mutable source protects against corruption but not source compromise.

## P1 — Updater extraction inherits archive safety obligations

An updater archive is more privileged than a mod archive because its files become executable application code.

The extraction path must enforce, before mutation:

- lexical relative-path containment;
- physical trusted-root containment;
- rejection of reparse/symlink/junction redirection in the staging root;
- file-count and total-output budgets;
- per-stream cancellation;
- actual streamed-byte accounting rather than trusting archive metadata alone;
- duplicate/case-collision handling;
- destination free-space check with safety margin;
- no writes directly into the live installation during extraction.

The current archive hardening lessons apply conceptually, but an updater should have dedicated tests because its destination and trust model are different.

## P2 — Current release ZIP includes a mutable debug log

`Build-Release.ps1` copies the current `MHW-DEBUG-ALL.log` into the release folder when that file exists.

For a normal portable download this is mostly diagnostic packaging behavior. For an updater, however, treating every archive path as product-owned would allow a release payload to overwrite the user's existing root-level runtime log.

Recommendation: exclude mutable logs from the updater product-file manifest even if they remain in manually downloadable release bundles, or stop packaging mutable runtime logs into the update payload entirely.

## P2 — Development/source-checkout mode must not self-update

The app supports `MOD_MANAGER_HOME` / `MHW_MANAGER_HOME` overrides and has explicit behavior for running a locally built app from the repository's `release\MHW-Manual-Mod-Manager-v*` path while keeping the project root as the data/debug root.

A self-updater must not accidentally rewrite a developer/source checkout or CI/test directory.

Require an explicit packaged-install marker (for example `release-install.json`) before self-update is enabled. The marker should identify install mode, product ID, channel, and product-file manifest. Absence of the marker means “check/display only; do not self-apply.”

## P2 — Updater helper self-replacement needs a two-process/bootstrap plan

If the updater helper itself lives in the installed product directory, it cannot safely replace/delete its own running image as part of the same transaction.

Recommended pattern:

- copy the verified helper to a temporary/user-local execution path before application shutdown;
- run that copied helper;
- update the installed helper as ordinary product content;
- remove the temporary helper on successful commit or next startup cleanup.

## P2 — Multi-instance and privilege behavior must be explicit

Before apply, acquire a dedicated machine/user-scoped update mutex and verify no second manager instance is still using the installation.

If the installation directory is not writable, fail safely with an actionable prompt. Do not silently broaden filesystem permissions or auto-elevate without explicit user interaction.

---

# Proposed architecture

## Release producer

Add a release-publication step only after the exact repository verifier and `Build-Release.ps1` succeed.

Outputs:

1. versioned application ZIP;
2. `product-files.json`;
3. `update-manifest.json`;
4. SHA-256 values for both;
5. optional detached signature/attestation;
6. immutable GitHub Release assets or another immutable release location.

Publication must be idempotent for the same version/source/artifact identity and must refuse to replace an already-published immutable release with different bytes.

## In-app update client

Responsibilities:

- select channel;
- fetch bounded metadata;
- compare semantic version/build sequence;
- present release details;
- download with cancellation and resource budgets;
- verify artifact before extraction;
- stage safely;
- write/update journal;
- expose “restart to update” only when safe.

It must not mutate installed executable files.

## External updater helper

Responsibilities:

- verify the journal and staged payload again;
- wait for exact app PID exit;
- acquire update mutex;
- snapshot/backup only product-owned installed files;
- preserve user/unknown files;
- apply product-file manifest;
- verify installed bytes after apply;
- start new app with a health-token argument/environment value;
- wait for health confirmation;
- commit or rollback.

The helper should be intentionally small and dependency-light.

## Startup health acknowledgement

The new app should acknowledge health only after bootstrap has reached a meaningful safe point, not merely process creation.

A practical threshold is after:

- path discovery;
- database initialization/migration;
- interrupted deployment recovery;
- startup maintenance;
- main-window initialization/show

because `App.OnStartup` already models these phases.

If startup exits/crashes before acknowledgement, the helper should restore the previous product files while leaving `State`/mods untouched.

---

# Required tests before enabling automatic apply

## Release/CI tests

- version consistency: declared product version, assembly version, release manifest, tag/channel metadata;
- immutable artifact identity: manifest SHA-256 equals built ZIP SHA-256;
- source provenance: manifest source SHA equals exact gated source;
- product-file manifest enumerates the shipped application files;
- updater payload excludes user-data roots and mutable logs;
- publishing same release identity with different bytes fails;
- documentation-only/evidence-only commits do not create duplicate stable releases unless version policy explicitly allows it.

## Download tests

- valid manifest + valid artifact;
- hash mismatch;
- truncated response;
- larger-than-declared body;
- redirect to unexpected host;
- cancellation mid-download;
- insufficient disk space;
- resume/retry behavior if implemented.

## Extraction tests

- `../` traversal;
- rooted/drive/UNC paths;
- duplicate normalized path;
- case-collision path;
- junction/symlink/reparse escape from staging root;
- file-count bomb;
- compressed-size/expanded-size bomb;
- metadata understates actual output;
- cancellation during one very large entry;
- disk-full mid-entry;
- cleanup after failed extraction.

## Apply/recovery tests

Use a fake install root containing sentinel user data:

- `State\...`
- `Mods\...`
- `Inbox\...`
- unknown user files
- existing `MHW-DEBUG-ALL.log`

Then prove byte-for-byte preservation across:

- successful update;
- failure before first product replacement;
- failure after one/many product replacements;
- helper crash/restart at each journal phase;
- process still running/locked file;
- stale staged payload;
- tampered staged file after initial verification;
- downgrade/replay attempt;
- second manager instance;
- read-only/non-writable install;
- rollback after new app fails health handshake;
- rollback after new app starts then crashes before health acknowledgement.

## Product integration tests

- update action cannot force close during `CriticalOperation`;
- update preserves configured `MOD_MANAGER_HOME` semantics;
- packaged-install marker required for self-apply;
- source/dev checkout disables self-apply;
- successful update preserves active game profile, database, blobs, mod library, staged state, and rollback history;
- new app reports exact installed version/source identity for support diagnostics.

---

# Suggested implementation sequence

Keep the work independently verifiable.

### Checkpoint A — release metadata only

Add deterministic product-file/update manifest generation and tests. Do not change runtime update behavior.

### Checkpoint B — immutable publication only

Publish exact green Windows artifact + manifest to a durable release endpoint. Add race/idempotency tests. Do not auto-download in the app yet.

### Checkpoint C — check/download/stage only

The app may discover and download an update but cannot apply it. Add full network/hash/extraction fault coverage.

### Checkpoint D — external helper + journal

Implement apply/rollback against a fake install root first. Prove user-data preservation and interruption convergence.

### Checkpoint E — controlled UI/restart integration

Wire “restart to update” into the WPF shell while preserving `CriticalOperation` shutdown safety.

### Checkpoint F — full Windows release gate + end-to-end installed update

Test old verified release -> new candidate, failure/rollback, then exact full repository Windows gate. Persist exact update artifact hashes and source SHAs.

Do not combine this sequence with archive resource hardening or unrelated filesystem/CAS work.

---

# Things deliberately not changed

- no production C#;
- no WPF/UI;
- no release workflow;
- no build script;
- no verification script or cache;
- no archive extraction implementation;
- no deployment/recovery implementation;
- no version number;
- no current next-production-boundary priority.

This support task is design evidence for the updater lane only.

---

# Verification actually performed

- Verified canonical `main` HEAD through GitHub compare: `4fd61dd33609a7c55e5aedbaad026266a410f942`.
- Inspected current open PRs and branches; no updater-specific branch/PR was visible at task selection.
- Inspected the exact files listed in the methodology section.
- Confirmed `VERSION.txt` = `8.8.0`.
- Confirmed `Directory.Build.props` declares `Version=8.8.0` and `InformationalVersion=8.8.0-function-verification`.
- Confirmed `Build-Release.ps1` publishes a versioned win-x64 folder/ZIP and computes SHA-256.
- Confirmed the current Windows workflow uploads Actions artifacts/evidence and does not include a GitHub Release/update-feed publication step in the inspected file.
- Confirmed `AppPaths.Discover` defaults persistent workspace/state beneath the application/tool root.
- Confirmed `MainWindow.OnClosing` blocks normal close during `CriticalOperation`.
- Confirmed `Verify-Release.ps1` fingerprints `Directory.Build.props` as project input and records `VERSION.txt` in stage-cache metadata; this audit did not find a dedicated updater-manifest/version-publication gate because none is implemented yet.

No build, test, Windows runtime, network download, update apply, or crash-recovery experiment was run for this documentation-only checkpoint.

---

# Unresolved implementation questions

These should be resolved by the updater programmer before Checkpoint A/B:

1. Is the intended user channel stable-only, or should an opt-in edge channel update on every green `main` production build?
2. Should update authenticity use a detached release signature/public key, GitHub attestation verification, Authenticode, or a staged combination?
3. Should portable installations remain fully self-contained, or should future versions separate the stable data root from versioned binaries?
4. What exact startup phase constitutes a successful post-update health acknowledgement?
5. Should manually downloadable release ZIPs continue containing `MHW-DEBUG-ALL.log`, while updater payloads exclude it?
6. What is the minimum supported old-version updater/bootstrap protocol before a forced manual update is required?

---

# Success criteria for the future updater boundary

The updater is not “done” merely because it can download and launch a new ZIP.

It is complete only when:

- update discovery is deterministic and version/channel aware;
- release metadata is bound to an exact green source/artifact;
- downloaded/staged bytes are verified before execution;
- extraction is physically contained and resource bounded;
- apply occurs outside the running app;
- only product-owned paths are mutated;
- `State`, mods, game workspaces, unknown files, and user diagnostics are preserved;
- interrupted apply converges by journaled retry or rollback;
- failed new-version startup rolls back;
- dev/source-checkout mode cannot self-overwrite;
- exact Windows end-to-end tests cover old -> new, tamper, interruption, and rollback;
- the final candidate earns a fresh full Windows Release Gate.

---

# Parallel-agent integration notes

- This audit does not supersede `ARCHIVE_EXTRACTION_RESOURCE_CANCELLATION_AUDIT.md`; updater extraction should inherit its general lesson but remain a separate implementation boundary.
- It does not change the current canonical recommendation that archive streaming cancellation/actual-output budgeting is the next production checkpoint.
- If an updater implementation branch appears while this PR is open, compare it against these invariants rather than blindly merging branch-local continuity snapshots.
- Preserve newer canonical verification/cache state during integration.
- The successor must inherit, preserve, and recursively propagate the permanent continuity constitution and active Learned Rules to the agent after them.

**Do not break the chain.**
