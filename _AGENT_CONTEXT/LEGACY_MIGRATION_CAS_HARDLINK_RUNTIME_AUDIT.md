# Legacy migration CAS hardlink runtime audit — 2026-09-28

## Status

**Runtime-confirmed support finding; no production behavior changed.**

- Canonical repository: `fengie/mhw-mods`
- Canonical commit inspected: `4fd61dd33609a7c55e5aedbaad026266a410f942`
- Support branch: `agent/support-legacy-migration-hardlink-runtime-20260928`
- Environment: `heaven2`, real Windows, .NET SDK 10.0.401
- Durable raw evidence: `_AGENT_CONTEXT/EVIDENCE/legacy-migration-hardlink-runtime-2026-09-28.txt`

This audit characterizes one residual explicitly left open by the latest filesystem/CAS integration: the v7 migration's Windows hardlink alias between retained legacy blobs and the v8 content-addressed store.

## Why this task was selected

The active programmer owns archive streaming cancellation/resource budgeting, and a parallel support lane owns auto-updater release security. Recursive reparse and archive physical-root containment are already closed. Existing filesystem/CAS audits identify migration hardlink aliasing as a P1 future boundary but explicitly lacked a real-Windows mutation reproduction.

This made hardlink characterization high-value, independent, and nonduplicative: it could convert a documented semantic risk into durable runtime evidence without modifying the active production boundary.

## Exact scope

Inspected and runtime-exercised:
- `src/MhwModManager.Storage/LegacyV7Migrator.cs`;
- `src/MhwModManager.Filesystem/BlobStore.cs`;
- `src/MhwModManager.Filesystem/AtomicFileOps.cs`;
- existing CAS/filesystem/migration audits and current continuity state.
Deliberately excluded:
- archive streaming implementation;
- updater/release work;
- reparse containment already closed on main;
- CAS digest namespace redesign;
- handle-based TOCTOU redesign;
- production hardlink removal;
- unrelated migration recovery changes.

## Confirmed current behavior

`LegacyV7Migrator.EnsureLegacyBlob` still creates the v8 CAS pathname with `CreateHardLinkW(dst, src)` on Windows when possible, falling back to `File.Copy` only when hardlink creation fails. At the end of migration, every referenced CAS pathname is SHA-256 verified before the completion marker is written.

The current CAS layer is stronger than the historical audit baseline: `BlobStore.RestoreAsync` passes the expected SHA-256 into `AtomicFileOps.ReplaceFromAsync`, which hashes the private staged replacement before publication. A corrupt CAS object therefore fails closed before live destination publication.

## Runtime reproduction

A disposable console probe referenced the current Storage and Filesystem projects and used only public product entry points:

1. create a minimal valid v7 State/V2 tree with one referenced legacy blob;
2. initialize a fresh `ManagerDatabase`;
3. execute `LegacyV7Migrator.MigrateIfNeededAsync`;
4. verify that migration succeeds and the CAS object's SHA-256 matches its filename;
5. mutate the retained legacy blob pathname after migration;
6. hash the CAS pathname again;
7. invoke migration again;
8. invoke `BlobStore.RestoreAsync` from the now-mutated CAS object.
Observed:
- initial migration: **success**;
- initial CAS SHA-256: exact expected hash;
- `fsutil hardlink list`: both the retained legacy pathname and v8 CAS pathname referenced the same NTFS hardlink object;
- mutating only the legacy pathname changed the bytes visible through the CAS pathname;
- the CAS SHA-256 changed from `cac5679d...95367` to `42ce5ced...e3861`;
- second migration: **skipped as already complete**, with no revalidation;
- restore: **InvalidDataException**, staged CAS integrity mismatch;
- restore destination: **not created**.

The first disposable probe compile attempt failed before execution only because its TargetFramework was generic `net10.0`; Filesystem requires the Windows TFM. Retargeting only the disposable probe to `net10.0-windows10.0.19041.0` produced the successful reproduction above. No repository product source was changed for the probe.

## Finding — P1: completed migration leaves immutable CAS aliased to retained mutable legacy bytes

This is now runtime-confirmed, not merely inferred from source/API semantics.

The migration proves the bytes at completion time but does not give the new CAS independent ownership of those bytes. Because the legacy and CAS pathnames remain hardlinks, a later write through the retained legacy pathname mutates the hash-named CAS object after its verification timestamp and after the durable migration-complete marker.

A later `MigrateIfNeededAsync` call trusts the completion marker and does not re-hash those already-imported blobs.
### Severity and present blast radius

**P1 integrity/recovery invariant defect, not P0 silent live-file corruption under current source.**

Reasons:
- the class contract describes the CAS as immutable SHA-256-addressed content;
- durable DB/migration state can claim completion while the referenced physical object no longer matches its digest;
- mutation after completion can make future restore/deployment operations fail until the object is repaired or recaptured;
- the migration itself will not self-heal merely by being invoked again.

Mitigating current strength:
- current restore staging re-hashes against the expected digest before publication;
- the reproduced corrupt object therefore did **not** reach the live restore destination;
- current duplicate capture also verifies an existing hash-named object rather than trusting existence.

No evidence from this audit shows an externally mutated legacy alias can silently publish wrong bytes through the current restore path.

## Missing regression coverage

No test currently references `LegacyV7Migrator` directly under `tests/`, and no regression pins post-migration alias independence.

Recommended future test-first checkpoint:
1. create a real-Windows v7 migration fixture on one NTFS volume;
2. migrate one referenced blob successfully;
3. mutate the retained v7 blob after the completion marker;
4. require the v8 CAS bytes to remain unchanged;
5. assert a normal restore still succeeds from the original digest;
6. assert retry/completion semantics remain convergent;
7. run the full exact Windows release gate for the production repair.
## Implementation guidance

The smallest coherent production repair is likely migration-specific: publish independently owned CAS bytes rather than retaining a hardlink to a legacy pathname that remains outside the v8 CAS ownership boundary. A byte copy/private-temp + verified publication path is easier to reason about than trying to enforce immutability across every possible legacy alias.

Do not combine that repair with archive streaming budgets, digest namespace validation, root/reparse policy, or general handle-based TOCTOU work. Preserve the current final digest verification and current restore fail-closed staging check.

## Verification actually performed

- fetched/rechecked current `origin/main`;
- inspected current source bodies and existing specialized audits;
- executed full public-entry-point migration on real Windows;
- used `fsutil hardlink list` to confirm both pathnames are hardlinks to the same object;
- mutated the retained legacy path and observed CAS digest change;
- reran migration and observed completion-marker skip;
- invoked current `BlobStore.RestoreAsync` and observed fail-closed digest rejection before destination publication.

Raw exact output is preserved in the evidence file named above.

## Not verified / not changed

- no production C# was modified;
- no production regression test was added in this support lane;
- no full `Verify-Release.ps1` or `Build-Release.ps1` claim is made for this documentation-only audit;
- no CAS root/reparse/digest-namespace behavior was recharacterized;
- no path-check/open TOCTOU closure was attempted.

## Parallel-agent and successor handoff

Do not displace the current archive streaming cancellation/resource-budget production checkpoint. Keep this hardlink defect as a separate migration/CAS-identity implementation boundary.

The successor must re-check current `origin/main`, preserve this evidence, preserve LR-001 through LR-010 and the permanent continuity constitution, and explicitly require its own successor to propagate the same system to the agent after them. **Do not break the chain.**
