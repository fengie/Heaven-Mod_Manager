# CAS filesystem identity / reparse safety audit — 2026-09-27

## Status

Documentation-only autonomous support checkpoint.

Canonical `main` inspected before task selection: `d001870d4cd3549841d8511392ae7885f174bca2`.

The exact hosted Windows Release Gate for that canonical source, run `36367883836`, was still **in progress** when this audit branch was created. This audit does not alter production C#, tests, workflows, verification caches, or the active CAS concurrency checkpoint.

Support branch:

`agent/support-cas-filesystem-identity-audit-20260927`

## Why this lane

The active production lane already owns CAS content-integrity/concurrency closure. The current `CAS_INTEGRITY_CHECKPOINT.md` explicitly leaves filesystem identity as separate work: CAS root/leaf reparse policy, hardlink policy, and real-link fixtures.

No open PR or other branch was found owning that narrower boundary. Existing LR-004 already governs physical/reparse containment, and LR-005 already governs restartable migration ownership/convergence, so this audit intentionally adds **no duplicate Learned Rule**.

## Scope

Inspected:

- `src/MhwModManager.Filesystem/BlobStore.cs`
- `src/MhwModManager.Filesystem/AtomicFileOps.cs`
- `src/MhwModManager.Storage/LegacyV7Migrator.cs`
- `src/MhwModManager.App/App.xaml.cs`
- `tests/MhwModManager.IntegrationTests/BlobIntegrityTests.cs`
- `tests/MhwModManager.IntegrationTests/HardeningTests.cs`
- `src/MhwModManager.Filesystem/DeploymentExecutor.cs`
- `src/MhwModManager.Filesystem/ArchiveInspector.cs`
- `_AGENT_CONTEXT/WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`
- `_AGENT_CONTEXT/TEST_GAP_AND_PERFORMANCE_AUDIT.md`
- `_AGENT_CONTEXT/CAS_INTEGRITY_CHECKPOINT.md`
- current continuity/handoff state.

Questions answered:

1. Does current content hashing also prove that the CAS pathname belongs to the intended state tree?
2. What happens if `BlobRoot` itself is a junction/reparse point?
3. What happens if a hash-named CAS leaf is a reparse point?
4. Does legacy migration still create hardlink aliases into CAS?
5. Which old filesystem-audit claims were fixed by the current CAS checkpoint, and which remain?
6. What exact Windows fixtures should be added before any production policy change?

Excluded:

- no recursive ModScanner/adoption/Smart Inbox containment implementation;
- no migration redesign;
- no hardlink removal;
- no handle-based TOCTOU redesign;
- no production behavior change;
- no new verification/cache promotion.

## Methodology

This audit re-read actual current source bodies rather than relying on the older filesystem audit alone.

It also checked current Microsoft documentation for Windows reparse points and hard links:

- Reparse points: <https://learn.microsoft.com/windows/win32/fileio/reparse-points>
- Reparse point operations: <https://learn.microsoft.com/windows/win32/fileio/reparse-point-operations>
- Symbolic-link effects on file APIs: <https://learn.microsoft.com/windows/win32/fileio/symbolic-link-effects-on-file-systems-functions>
- Hard links and junctions: <https://learn.microsoft.com/windows/win32/fileio/hard-links-and-junctions>

The documentation matters because normal file opens follow reparse targets unless opened with reparse-specific semantics, and because all hard links reference the same underlying file data: mutation through one alias is visible through the others.

## Existing strengths confirmed

### Content integrity is materially stronger than the old filesystem audit described

The old `WINDOWS_FILESYSTEM_SAFETY_AUDIT.md` said an existing SHA-named object could be trusted merely because the pathname existed. That part is now obsolete.

Current `BlobStore`:

- hashes an already-existing object before accepting it during capture;
- retries only transient Windows sharing/lock violations without weakening `FileShare.Read`;
- restores through `AtomicFileOps.ReplaceFromAsync(... expectedSha256 ...)`;
- validates the private staged replacement bytes before any live publication.

Source references:

- `BlobStore.cs:70-83` — existing/racing destination is verified before temp deletion.
- `BlobStore.cs:107-113` — restore supplies the advertised SHA to the atomic copy seam.
- `BlobStore.cs:116-141` — authoritative existing-object SHA verification.
- `AtomicFileOps.cs` — private staged bytes are hashed before rename/ReplaceFileW.

Consequence: a reparse/hardlink alias that later serves bytes inconsistent with the advertised SHA now fails closed before wrong bytes can be published to the live destination.

That is an important reduction in severity. Filesystem identity remains unresolved, but it is no longer equivalent to silent content corruption.

### Current Windows test infrastructure can create real junctions

`HardeningTests.cs:70-89` already has a working `mklink /J` directory-junction helper and current hosted Windows infrastructure already executes real-junction deployment containment tests.

This makes a CAS-root junction fixture practical without adding a privileged custom driver or speculative mock.

### Live deployment containment remains a separate closed boundary

`DeploymentExecutor` already checks descendant path components for reparse points close to live mutation/recovery. This audit does not reopen that code. The CAS state tree is a different trust root and currently has no corresponding guard.

## Findings

### P1 — CAS root can be redirected outside the intended manager state tree

Confirmed current source behavior.

`AppPaths.Discover` derives `BlobRoot` as `Path.Combine(next, "Blobs")` (`App.xaml.cs:363`) and passes it directly to `BlobStore` (`App.xaml.cs:81`).

`BlobStore` stores the supplied string unchanged as `Root` (`BlobStore.cs:17`). Capture then:

- calls `Directory.CreateDirectory(Root)` (`BlobStore.cs:35`);
- creates private capture temp files under `Root` (`BlobStore.cs:39`);
- constructs final object paths with `Path.Combine(Root, sha)` (`BlobStore.cs:18-22`);
- renames the private temp to the final hash path (`BlobStore.cs:77`).

There is no check that `Root` itself, or a descendant path component between the chosen trust anchor and `Root`, is a reparse point.

Therefore, if `State/.../Next/Blobs` is replaced with a directory junction, capture can create/write/delete the manager's CAS staging and hash-named objects in the junction target rather than the intended state directory.

This does **not** currently imply arbitrary named-file overwrite: final names are SHA-256 values and capture uses no-overwrite rename. But it is still a physical-containment violation and can expose captured package bytes outside the intended manager state namespace.

Classification: confirmed source behavior; real junction escape is not runtime-reproduced by this documentation-only agent.

### P2 — hash-leaf reparse identity is unsupported even though wrong bytes now fail closed

Confirmed policy gap; content corruption is mitigated.

If a hash-named file path is itself a symbolic-link/reparse object, current `File.Exists` / `FileStream` operations do not deliberately open the link object with `FILE_FLAG_OPEN_REPARSE_POINT` semantics. Microsoft documents that normal opens of symbolic links operate on the target.

Current capture and restore therefore reason about the target bytes, not the identity of the hash-named filesystem object.

However, the new CAS repair changes the practical consequence:

- capture re-hashes the opened target and rejects SHA mismatch;
- restore hashes the private staged copy and rejects SHA mismatch before publication.

So a leaf link whose target does **not** match the hash is rejected. A leaf link whose target *does* match can still be treated as a valid CAS object even though the store no longer owns the underlying filesystem identity.

That distinction should be explicit in the future contract: content-address correctness is currently enforced; exclusive/private CAS object identity is not.

### P1 — legacy migration still creates a mutable hardlink alias into the new CAS

Confirmed current source behavior.

`LegacyV7Migrator.EnsureLegacyBlob` still prefers `CreateHardLinkW(dst, src)` on Windows and falls back to copy only when hardlink creation fails:

- `LegacyV7Migrator.cs:110-112`
- P/Invoke declaration: `LegacyV7Migrator.cs:122`.

The migration later hashes every referenced new-CAS path before declaring migration success (`LegacyV7Migrator.cs:64-65`). That proves bytes at that moment.

It does **not** sever the alias.

Microsoft documents that hard-link paths reference the same file data and changes through one alias are visible through the others. Therefore post-migration mutation through the retained legacy `State\V2\Blobs\<sha>` pathname changes the bytes visible at the new CAS pathname too.

The current CAS checkpoint again prevents silent wrong live publication: subsequent capture/restore checks should reject the changed bytes. But the alias can still turn a previously verified CAS object into a later integrity failure and can force deployment/recovery to fail closed until the artifact is repaired.

This is a real immutability/availability gap, not evidence that the legacy product actually mutates those files after migration.

Keep implementation separate from the reparse checkpoint because LR-005 migration ownership/retry rules apply.

### P2 — current CAS tests prove byte integrity, not path identity

`BlobIntegrityTests` now covers:

- corrupt existing object rejection;
- corrupt restore rejection;
- concurrent same-digest capture;
- cancellation cleanup;
- deployment/recovery corruption handling.

Representative references:

- `BlobIntegrityTests.cs:39`
- `BlobIntegrityTests.cs:57`
- `BlobIntegrityTests.cs:72`
- `BlobIntegrityTests.cs:96`.

There is no current fixture for:

- `BlobRoot` as a directory junction;
- a reparse object at a hash leaf;
- mutation through a hardlink alias after migration;
- physical-containment assertions for capture temp/final object creation.

The current test suite can therefore be fully green while the CAS namespace is physically redirected.

### P2 — a path-check fix alone will retain a topology-swap TOCTOU

This is a design limitation, not a newly reproduced bug.

The deployment hardening work already documents that checking path attributes and then reopening by pathname cannot atomically prevent another actor from swapping filesystem topology after the check.

The same applies to a first CAS reparse guard.

A first implementation checkpoint does not need to solve handle-based race closure. It must state the residual honestly and avoid claiming stronger guarantees than it implements.

## Trust-anchor decision required before implementation

Do not add an indiscriminate "reject every reparse point from the volume root downward" rule without deciding what the manager considers its trusted configured root.

The application allows `MOD_MANAGER_HOME` / `MHW_MANAGER_HOME` and otherwise uses the executable directory as `ToolRoot`. A user may intentionally place the whole manager tree under a linked path.

A coherent first policy is:

> Treat the configured `ToolRoot` (or another explicitly chosen state anchor) as the trust anchor. Reject unsupported reparse traversal **below that anchor** into `State/.../Next/Blobs` and reject reparse hash leaves. Do not silently redefine the user's configured anchor.

This mirrors the live deployment model, where the configured game root is the trust anchor and descendant reparse traversal is rejected.

The exact anchor should be chosen explicitly in tests before production code changes.

## Proposed regression / fault fixtures

### Checkpoint A — CAS root physical-containment tests first

Windows-only real-junction tests, reusing the existing `HardeningTests.CreateDirectoryJunction` approach:

1. **capture rejects BlobRoot junction**
   - create an external directory with a sentinel;
   - replace/create the intended `Blobs` directory as a junction to the external directory;
   - call `CaptureAsync`;
   - expected future contract: fail before creating external `.capture-*.tmp` or SHA objects;
   - sentinel and external directory contents remain unchanged.

2. **restore rejects BlobRoot junction even when external bytes have the correct SHA**
   - place a valid SHA-named object in the external target;
   - junction `BlobRoot` to it;
   - call `RestoreAsync`;
   - expected future contract: physical-containment failure even though content hash is correct;
   - live destination remains unchanged/absent;
   - no destination temp residue.

3. **normal non-reparse BlobRoot still captures/restores**
   - characterization proving the guard does not change ordinary semantics.

4. **root-junction failure does not register a blobs row**
   - preserve current transaction/data-integrity expectations.

This is the strongest first fixture because directory junction creation already works in the repository's Windows test environment.

### Checkpoint B — hash-leaf reparse policy fixture

If the hosted runner can create unprivileged file symbolic links, add:

- correct-content file symlink at `BlobStore.PathFor(hash)` -> external file;
- wrong-content file symlink at the same path;
- capture and restore assertions for the chosen fail-closed identity policy.

If hosted symlink creation is unavailable, do **not** weaken the required root-junction fixture or fake a passing link test. Document the environment constraint and use a narrow Win32 fixture only if it can run reliably without elevated privileges.

### Checkpoint C — hardlink alias migration characterization, separately

Under the migration audit/LR-005 boundary:

1. create a real same-volume legacy blob;
2. run the relevant migration import path;
3. prove whether new CAS and legacy blob share identity/link count;
4. mutate the legacy alias after successful migration;
5. prove current restore rejects the now-corrupt CAS rather than publishing it;
6. future desired contract: mutation of the legacy pathname must not mutate the new CAS object at all.

The likely smallest semantic fix is to stop publishing legacy blobs as hardlinks into the immutable CAS and instead publish independently owned verified bytes. That is a migration behavior change and needs its own exact tests/gate.

## Recommended implementation shape

For the first production checkpoint only:

- define the CAS physical trust anchor explicitly;
- add a small reusable descendant-reparse guard rather than embedding ad hoc checks in each call;
- verify the CAS root before staging/publishing;
- verify an existing hash leaf is not an unsupported reparse object before trusting/opening it;
- keep authoritative SHA verification even after identity checks;
- retain the current sharing-violation retry contract;
- preserve fail-closed behavior;
- explicitly document the remaining path-check/open TOCTOU.

Do **not** remove SHA verification because reparse checks were added. Identity and content integrity defend different invariants.

Do **not** fold legacy hardlink removal into this same production checkpoint.

## Existing rules that already govern this work

No new Learned Rule is justified.

- **LR-004 — lexical containment is not physical filesystem containment** already requires a trusted root, explicit reparse policy, and Windows link fixtures.
- **LR-005 — restartable migrations must prove ownership and convergence** governs any future change to legacy migration publication/retry behavior.
- Current CAS integrity evidence requires exact-source verification and forbids manual cache promotion.

## Verification actually performed

Performed in this support audit:

- verified canonical GitHub `main` at `d001870d4cd3549841d8511392ae7885f174bca2`;
- inspected recent commits, open PRs, and branches;
- confirmed `agent/cas-integrity-20260927` was identical to canonical main at the time checked;
- confirmed Windows Release Gate `36367883836` for `d001870...` was in progress;
- inspected actual current `BlobStore`, `AtomicFileOps`, migration, app-path composition, junction test helper, deployment containment, and blob-integrity test bodies;
- reconciled old filesystem-audit CAS claims against the newly landed content-verification implementation;
- checked current Microsoft primary documentation for reparse and hardlink semantics;
- confirmed no PR/branch matched this specific CAS filesystem-identity lane before branch creation.

Not performed:

- no local checkout or `git status`;
- no `.NET` build/test execution;
- no PowerShell handoff validator;
- no real CAS-root junction runtime reproduction;
- no file-symlink fixture;
- no hardlink mutation runtime characterization;
- no hosted Windows Release Gate for this documentation-only branch;
- no verification cache promotion.

## Parallel-agent integration notes

The active CAS production/gate checkpoint remains authoritative for:

- content SHA validation;
- same-digest concurrent capture sharing-violation retry;
- cancellation/staging cleanup;
- exact Windows release closure.

This audit must **not** be used to delay, rewrite, or weaken that active gate.

If the active gate closes while this PR is pending, integrate this audit as documentation/research and update its historical statement rather than reopening the closed content-integrity boundary.

The earlier `WINDOWS_FILESYSTEM_SAFETY_AUDIT.md` remains the broad authority for repository-wide filesystem safety. This document is the narrower current-source authority for **CAS filesystem identity after the content-integrity repair**.

## Recommended next independent checkpoint

After the active CAS concurrency gate is green and evidence is persisted:

**Add Windows root-junction characterization tests for BlobStore, define the CAS trust anchor, then implement only the smallest reparse-containment guard needed to make those tests pass.**

Keep legacy hardlink de-aliasing as a later, migration-specific checkpoint.

## Successor handoff

Read this audit together with:

- `_AGENT_CONTEXT/CAS_INTEGRITY_CHECKPOINT.md`
- `_AGENT_CONTEXT/WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`
- `_AGENT_CONTEXT/LEGACY_MIGRATION_RECOVERY_AUDIT.md`
- LR-004 and LR-005.

Do not infer that any risk documented here has been fixed merely because this audit is merged.

The successor must inherit and preserve the permanent continuity constitution and active Learned Rules, update durable state for the exact boundary it changes, and explicitly require its successor to recursively propagate the same obligations to the agent after them.

**Do not break the chain.**
