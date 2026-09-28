# CAS root/leaf reparse policy audit — 2026-09-27

## Status

**Documentation-only support checkpoint.** This branch does not change production C#, tests, migration behavior, verification scripts, promoted verification caches, or the active CAS sharing-violation repair.

Canonical `main` inspected before this audit:

`d001870d4cd3549841d8511392ae7885f174bca2`

Canonical `main` rechecked and merged after hosted CAS repair evidence advanced:

`265d58d6a9d4ffb6ab62cde987ba6ad335eae05a`

Audit branch:

`agent/support-cas-reparse-policy-audit-20260927`

The active product boundary remains the exact CAS concurrency/sharing-violation re-verification described by `CAS_INTEGRITY_CHECKPOINT.md`. Hosted Windows Release Gate `36367883836` is now green for exact candidate `d001870d4cd3549841d8511392ae7885f174bca2`; the checkpoint remains open only for the required fresh local Windows verification. Do not begin a CAS containment production change until that local closure is recorded.

## Why this is an independent support task

The current CAS checkpoint explicitly leaves **CAS root/reparse/hardlink policy** outside its implemented content-integrity boundary and names a reparse-policy audit with real-link fixtures as useful parallel support work.

This audit narrows that suggestion to **CAS root and hash-leaf reparse containment**. It deliberately does not implement or redesign:

- the active `VerifyExistingAsync` sharing-violation retry;
- recursive `ModScanner`, unmanaged-adoption, or Smart Inbox traversal;
- legacy migration retry/convergence;
- the legacy-v7 hardlink migration policy;
- live `DeploymentExecutor` containment, which is already a separate closed checkpoint;
- native `ReplaceFileW` failure semantics, which are already a separate closed checkpoint.

Open support work already owns migration recovery and import publication. Keeping this document on the CAS source boundary avoids competing edits while still reducing uncertainty for the next production checkpoint.

## Scope and questions

Files inspected at canonical `main`:

- `src/MhwModManager.Filesystem/BlobStore.cs`
- `src/MhwModManager.Filesystem/AtomicFileOps.cs`
- `tests/MhwModManager.IntegrationTests/BlobIntegrityTests.cs`
- `tests/MhwModManager.IntegrationTests/HardeningTests.cs`
- `src/MhwModManager.Storage/LegacyV7Migrator.cs`
- `_AGENT_CONTEXT/WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`
- `_AGENT_CONTEXT/CAS_INTEGRITY_CHECKPOINT.md`

Questions answered:

1. Does current CAS content verification also prove that a CAS object physically resides under the intended CAS root?
2. What happens to the safety contract when the CAS root itself is a directory reparse point?
3. What happens when the hash-named leaf is a file reparse point whose target has matching or mismatching bytes?
4. Which current protections already prevent silent corrupt live publication?
5. What exact Windows fixtures should define the next containment contract before production code changes?
6. What is the smallest first implementation boundary, and what residual TOCTOU risk would remain?

## Existing strengths that materially change the risk

The original broad filesystem audit predates the completed corrupt-object CAS hardening. Current `BlobStore` is stronger than that historical snapshot.

### Existing-object capture trust now verifies bytes

`BlobStore.CaptureWithHashAsync` computes the source SHA while copying into a private `.capture-*.tmp` file. If the final hash path already exists, or appears after a concurrent move loses the race, `VerifyExistingAsync` opens that object and computes SHA-256 before the private verified temp is discarded or a blob row is trusted.

A same-length corrupt hash-named file is therefore no longer accepted merely because its filename is a SHA.

### Restore now verifies the actual staged bytes before publication

`BlobStore.RestoreAsync` passes the advertised SHA into `AtomicFileOps.ReplaceFromAsync`.

`AtomicFileOps` copies from the CAS path into a private same-destination-directory temp, durably flushes it, rewinds the still-exclusive staged handle, hashes those exact staged bytes, and refuses publication if the staged SHA does not match the advertised digest.

This is important: a reparse target containing wrong bytes should fail before the destination is published. The remaining reparse issue is therefore **physical containment and alias trust**, not an already-confirmed path to silent wrong-byte publication.

### Current race repair preserves mutation exclusion

`BlobStore.VerifyExistingAsync` keeps `FileShare.Read` and retries only Windows sharing/lock violations 32/33 with bounded cancellation-aware backoff. This audit does not propose weakening that behavior.

## Findings

### P1 — the CAS root has no explicit reparse policy

**Confirmed source behavior:** `BlobStore` accepts an arbitrary `root` string, exposes it as `Root`, and builds every CAS path with `Path.Combine(Root, ...)`. Capture calls `Directory.CreateDirectory(Root)`, creates private capture staging directly under `Root`, and publishes by `File.Move(temp, dest, false)`. Restore constructs its source with the same root.

No CAS path currently checks `FileAttributes.ReparsePoint`, opens the root without normal reparse processing, or validates root identity by handle/file ID.

If `Root` is an already-existing Windows directory junction/reparse point, ordinary pathname operations are expected to resolve through that reparse point. A real Windows fixture is still required before claiming the exact runtime postcondition for this repository.

**Risk:** the program can conceptually read/write CAS bytes through a physically redirected directory while believing they are under its configured state path. Content hashing still protects digest correctness, but it does not prove storage location, ownership, or immutability of the physical object.

**Severity:** P1. Current staged-byte verification substantially reduces the old corrupt-publication severity; the unresolved issue is a trust-boundary/containment and availability problem.

### P1 — a hash-named leaf has no explicit reparse policy

**Confirmed source behavior:** when `<Root>\<sha>` exists, capture calls `VerifyExistingAsync`, which opens it through an ordinary `FileStream`. Restore likewise opens the source through ordinary `FileStream` inside `AtomicFileOps`.

There is no leaf attribute check and no open requesting “open the reparse point itself” semantics.

Windows documents that an ordinary file open processes a symbolic link/reparse point unless `FILE_FLAG_OPEN_REPARSE_POINT` is requested. Therefore a file symlink at the hash path can resolve to a target elsewhere. If the target bytes match the advertised SHA, the current content checks can legitimately pass even though the object is not physically owned by the CAS directory.

**Risk:** the digest contract may be true while the stronger implied “immutable object under this store” contract is false. Another alias owner can alter or remove the physical target later, causing subsequent operations to fail closed or observe different bytes.

**Severity:** P1. Matching-byte redirection is a physical-containment/immutability defect; mismatching bytes are already caught before live publication.

### P2/P1 residual — a visible-reparse path check alone cannot close topology-swap races

The repository already learned this lesson for live deployment. A first CAS fix can reject visible reparse components close to use and materially improve safety, but a sequence such as:

1. inspect attributes;
2. conclude “not a reparse point”;
3. reopen by pathname;

still permits a topology swap between check and open by another actor with filesystem write access.

That residual must be documented rather than hidden. Full closure requires a stronger handle/identity design: for example, opening the root/leaf with reparse-aware Win32 flags, inspecting final resolved path or file identity, and retaining the relevant handle across the operation where practical.

The first CAS containment checkpoint does **not** need to solve every handle-relative filesystem race if its contract explicitly says so.

### Separate known boundary — legacy migration can intentionally create a hardlink alias

`LegacyV7Migrator.EnsureLegacyBlob` attempts `CreateHardLinkW(dst, src)` on Windows before falling back to a copy, then the migration hashes all referenced objects before declaring success.

A hardlink is another pathname to the same underlying file data, so later writes through a legacy alias can change bytes seen at the CAS pathname. This is a genuine immutability-policy question already documented by the broad filesystem audit.

It is **not owned by this branch**. Migration recovery is active parallel work, and the CAS checkpoint specifically requested reparse-policy design without changing migration semantics. A future independent migration-hardlink checkpoint should characterize alias mutation and choose copy-only, link-count enforcement, or another explicit policy.

## Current test gap

`BlobIntegrityTests` currently proves:

- corrupt existing objects are rejected;
- registered and unregistered corrupt recapture does not advance trust;
- corrupt restore does not publish an existing or absent destination;
- concurrent valid captures converge;
- capture/restore cancellation cleans private staging;
- corrupt CAS material causes deployment rollback or fail-closed recovery as appropriate.

It contains no junction/symlink/reparse fixture for the CAS root or hash leaf.

`HardeningTests` already provides a good repository precedent: `CreateDirectoryJunction` invokes `cmd.exe /d /c mklink /J`, checks the exit code, and asserts the created directory has `FileAttributes.ReparsePoint`. The live-deployment containment tests use real Windows junctions rather than mocked attributes.

The next CAS fixture should follow that honesty standard.

## Exact Windows regression/fault-test design

These tests should be introduced **after the required fresh local Windows verification closes the active CAS checkpoint**, so failures belong to a new exact verification boundary. The hosted repair gate is already green.

### 1. Root junction — capture must reject before external publication

Suggested name:

`Capture_rejects_reparse_CAS_root_before_external_write`

Arrange:

- Windows only;
- create an external directory with a sentinel;
- create the configured blob root as a directory junction targeting that external directory, using the established `mklink /J` helper pattern;
- construct `BlobStore` with that root;
- create a normal source and calculate/know its expected SHA.

Desired contract:

- capture fails with an explicit reparse/physical-containment error;
- external sentinel remains unchanged;
- no `.capture-*.tmp` survives;
- no hash-named object is published into the external target;
- no blob row is registered.

Characterization note: do not pre-write the expected current failure/pass result into the test. Run the fixture against current source first and record the actual external-directory postcondition.

### 2. Root junction — restore must reject even when external bytes are correct

Suggested name:

`Restore_rejects_reparse_CAS_root_before_reading_external_object`

Arrange:

- external directory contains `<sha>` with bytes that genuinely match `sha`;
- configured CAS root is a junction to that external directory;
- destination contains known `LIVE` bytes.

Desired contract:

- restore rejects because the CAS physical root is unsupported, **not** because content is corrupt;
- destination remains exactly `LIVE`;
- no destination `*.mhwmm.tmp` remains.

This is necessary because a wrong-byte fixture would only re-prove the existing SHA guard.

### 3. Hash-leaf symlink — capture must reject a matching target

Suggested name:

`Capture_rejects_reparse_hash_leaf_even_when_target_hash_matches`

Arrange:

- normal physical CAS directory;
- external regular file containing bytes whose SHA is `hash`;
- a file symbolic link at `<Root>\<hash>` targeting the external file;
- normal source with those same bytes.

Desired contract:

- capture rejects the reparse leaf;
- no database trust is added/advanced;
- external target remains untouched;
- private capture staging is cleaned.

Test-environment rule: create the file symlink with a helper that fails loudly if the Windows runner lacks capability. Do not silently skip a failed security fixture and report the suite green.

### 4. Hash-leaf symlink — restore must reject a matching target

Suggested name:

`Restore_rejects_reparse_hash_leaf_even_when_target_hash_matches`

Arrange the same matching-byte external target and hash-path symlink; create a live destination.

Desired contract:

- restore rejects on physical CAS policy even though the bytes hash correctly;
- live destination is unchanged;
- no staging remains.

This pins the distinction between **content integrity** and **storage containment**.

### 5. Per-operation revalidation — leaf replaced after an earlier valid capture

Suggested name:

`Restore_rechecks_CAS_leaf_after_valid_capture_is_replaced_by_reparse_point`

Arrange:

1. perform a normal valid capture;
2. delete the regular hash object;
3. replace it with a symlink to an external matching-byte target;
4. restore.

Desired contract: rejection. This prevents a future implementation from validating only at BlobStore construction or first capture.

### 6. Optional explicit fail-closed characterization for wrong target bytes

Suggested name:

`Restore_from_reparse_leaf_with_wrong_target_bytes_never_publishes`

This is lower priority because existing corrupt-object tests and staged-byte hashing already cover the core byte-integrity contract. If added, it should confirm that the destination is unchanged and that the failure remains fail-closed even before reparse rejection is implemented.

## Minimal future production checkpoint

Once the current CAS race checkpoint is fully closed by the outstanding local Windows gate, take the tests above first and make the smallest production correction that satisfies the chosen contract.

A reasonable first checkpoint to evaluate:

1. add a small BlobStore-local guard for the **configured CAS root itself** and for an **existing hash leaf**;
2. reject `FileAttributes.ReparsePoint` rather than following it;
3. run that guard on every capture/restore operation, not only construction;
4. re-check immediately before creating capture staging or opening an existing object;
5. preserve existing SHA verification, `FileShare.Read`, retry bounds, cancellation, and cleanup exactly;
6. do not refactor recursive source walkers or migration in the same change;
7. explicitly document the remaining path-check/open TOCTOU.

Do not claim this first guard proves that every ancestor of the configured CAS root is under a trusted physical anchor. `BlobStore` currently receives only the final root path; defining a trusted ancestor and proving final-path/file-ID membership is a stronger future design decision.

If the threat model later requires complete reparse-race closure, investigate a dedicated handle-based checkpoint using Windows APIs such as `CreateFileW` with `FILE_FLAG_OPEN_REPARSE_POINT`, directory-handle semantics, `GetFinalPathNameByHandleW`, and/or file identity. That should be a separate measured change, not smuggled into the first fixture-driven fix.

## External platform evidence used

Primary Microsoft references consulted:

- CreateFileW / `FILE_FLAG_OPEN_REPARSE_POINT`: https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew
- Hard links and junctions: https://learn.microsoft.com/windows/win32/fileio/hard-links-and-junctions
- File attribute reparse-point flag: https://learn.microsoft.com/dotnet/api/system.io.fileattributes
- Hard-link semantics: https://learn.microsoft.com/windows-server/administration/windows-commands/fsutil-hardlink

These sources define platform semantics. They do not substitute for repository-specific Windows execution; the fixtures above are still required.

## Things deliberately not changed

- No `BlobStore` production edit.
- No `AtomicFileOps` edit.
- No test edit.
- No migration edit.
- No recursive traversal edit.
- No verification-cache promotion.
- No `CURRENT_REVISION.json`, `CURRENT_STATE.md`, `NEXT_STEPS.md`, or active CAS checkpoint rewrite while another agent owns that boundary.
- No new Learned Rule. LR-004 already captures the durable rule that lexical/pathname containment does not prove physical containment.

## Verification actually performed

Connector/static verification against canonical GitHub state:

- confirmed canonical `main` and exact HEAD before selecting work;
- inspected recent canonical commits and all open PR ownership;
- read the repository continuity/start/handoff files and active CAS checkpoint;
- inspected the actual current `BlobStore`, `AtomicFileOps`, CAS test assertions, live-junction test helper/assertions, migration hardlink body, and broad filesystem audit;
- checked current Microsoft primary documentation for ordinary reparse processing, explicit reparse-point open semantics, junctions, and hardlinks;
- rechecked canonical `main` before branch creation;
- observed `main` advance to `265d58d6a9d4ffb6ab62cde987ba6ad335eae05a`, inspected the new hosted-green/local-pending CAS evidence, and merged that current main into this branch without force-pushing.

Not executed in this environment:

- local `git status`;
- PowerShell handoff validator;
- .NET restore/build/tests;
- Windows junction/symlink fixtures;
- file-ID/link-count characterization;
- hosted Windows Release Gate.

No runtime claim in this document is presented as reproduced unless it was already established by canonical repository evidence.

## Integration / parallel-work notes

This audit is intentionally a standalone documentation artifact to minimize stale handoff-file conflicts while the main CAS agent is updating the live checkpoint.

Before integrating:

1. re-read the then-current `main`, especially `BlobStore.cs` and `CAS_INTEGRITY_CHECKPOINT.md`;
2. if the active CAS gate has changed the relevant source contract, reconcile this audit rather than blindly merging stale conclusions;
3. add this audit to the canonical support-audit discovery list / handoff manifest when the live continuity files are stable;
4. do not import stale branch-local `CURRENT_*` snapshots;
5. keep legacy-migration hardlink semantics under its own independently verifiable checkpoint;
6. keep recursive scanner/adoption/Inbox traversal under its own independently verifiable checkpoint.

## Recommended next independent checkpoint

**After the outstanding focused/local `Verify-Release.ps1` and `Build-Release.ps1` checks close the already-hosted-green CAS repair:** add the real Windows root-junction and matching-byte hash-leaf-symlink tests above as a test-first CAS physical-containment checkpoint. Characterize current behavior first, then implement only the smallest fail-closed root/leaf guard needed by those tests.

Do not combine that checkpoint with migration hardlink policy or recursive source/live traversal.

## Successor handoff

The successor must verify actual canonical `main` before editing, read `AGENTS.md`, `NEXT-AGENT-START-HERE.md`, `CURRENT_REVISION.json`, `CONTINUITY_PROTOCOL.md`, active `LEARNED_RULES.md`, `CAS_INTEGRITY_CHECKPOINT.md`, and this audit.

Preserve the active content-integrity and recovery invariants. Bind every green verification claim to the exact source SHA actually checked. Update durable handoff state when integrating this audit or implementing its tests.

The successor must explicitly require its own successor to inherit, preserve, and recursively propagate the same continuity constitution and active Learned Rules to the agent after them.

**Do not break the chain.**
