# 2026-09-29 v8.8.7 reconciliation — CURRENT CRITICAL PATH

1. Re-fetch canonical `main` before every integration; this checkpoint audited source `fb3fb7ea5c5e4b133cea96f1c52dd9f4a3df327f`.
2. Run/inspect exact-current-main Windows release verification and persist evidence for the exact SHA checked. Do not extend hosted run `36541891969` beyond source `5abe40304dfcb48f96e750bd7da3d0075315625b`.
3. Complete installed-client updater success and injected-rollback E2E against the current release path, proving exact restart/health identity and preservation of seeded user/unknown data.
4. Re-query open PRs. Treat already-landed implementations with still-open stale PRs as reconciliation/cleanup items, not automatic merge candidates.
5. For overlapping Agent Control registry/dashboard/operator work, settle the shared registry contract first and re-diff dependent branches after each owner integration.
6. Each implementation owner remains responsible for current-main sync, verification, merge/push, and remote-main confirmation.
7. Preserve exact-SHA continuity evidence and recursively pass the continuity constitution to successors.

---

# 2026-09-29 updater publication closure — CURRENT CRITICAL PATH

1. **CLOSED:** PR #91 merged as `5abe40304dfcb48f96e750bd7da3d0075315625b`. Exact-main Windows Release Gate **36541891969** passed **25/25** on Windows x64 / .NET SDK 10.0.401. Function status is **748/748 verified**.
2. **CLOSED:** `Build-Release.ps1` produced updater build **61**. Release ZIP SHA-256: `C31CAA1F5CBA65EBF9D526B02BA718F807EBC18A7E7554269E3754D710F86420`.
3. **CLOSED:** updater publication policy passed and immutable release `updater-main-61` targets exact source `5abe40304dfcb48f96e750bd7da3d0075315625b` with exactly two assets: `MHW-Manual-Mod-Manager-v8.8.4-win-x64.zip` and `update-manifest.json` (manifest SHA-256 `4021E5303263A42255E80B40DA6C9C9349B055FB87BB970C2A71DA149DB4D2BE`).
4. **CURRENT CRITICAL PATH:** run a disposable installed-client old→new E2E against build 61. Seed `Mods`, `State`, and an unknown file; prove byte hashes remain unchanged, the exact target build restarts, and health acknowledgement succeeds.
5. **CURRENT CRITICAL PATH:** run a separate fault-injected rollback E2E. Prove previous owned executable/metadata/bytes are restored, new-only product files are removed, seeded user/unknown files remain unchanged, and no ambiguous recovery state is silently accepted.
6. Do **not** reopen C12, retry-tag propagation, or release publication unless new contradictory evidence appears. Keep unrelated PRs out of this lane. After both E2E scenarios pass, persist exact evidence, refresh continuity, and mark the automatic updater end-to-end closed.
7. Evidence/cache persistence for the hosted closure is `4f0e2402d3a61e9ba3db005f026b02ccb4aba7de`; it changes verification/evidence files only.

---

﻿# Current highest-priority next steps — 2026-09-29

1. **Updater implementation/publication is integrated and hosted-verified** at source `5abe40304dfcb48f96e750bd7da3d0075315625b`, Windows run **36541891969**, updater build **61**, tag `updater-main-61`.
2. Run the final disposable packaged-client E2E: a real older installed package discovers/applies build 61, preserves seeded `Mods`, `State`, and unknown-file hashes, and restarts with the exact target identity/health acknowledgement.
3. Run the paired injected startup/health-failure case and prove rollback restores the exact previous owned payload/executable identity while preserving the same seeded user-data hashes.
4. Persist exact E2E evidence. Only after both cases pass may the updater be labeled end-to-end complete.
5. Keep PR #90 separate; reconcile it onto current main and re-run its exact-candidate verification before making it ready again.

---

# v8.8.4 final support reconciliation — current next steps

1. Canonical v8.8.4 was pushed after a final race check and remote `origin/main` was verified at integration/evidence checkpoint `05d01c982249e5d8d654ad125925af3ac2a85a91`. This final handoff-only checkpoint changes no product source.
2. Inspect the exact-main hosted Windows Release Gate. Local closure applies to product/source `b48c1ff865ab41841d8c7eb931fca19f371f960e`; do not call hosted closure until CI passes canonical main.
3. If hosted evidence passes, persist it without changing product source and re-verify the resulting remote HEAD.
4. Keep follow-ups independent: remove legacy CAS hardlink aliasing; establish writer orphanhood/exclusive ownership before recovery takeover; span updater ownership across supported Windows sessions; leave broader preview redirect/image-content policy and active Updater/Agent-Control/frontend/game-profile lanes separate.
5. Preserve and recursively propagate the continuity constitution and active Learned Rules.

---

# v8.8.3 current next steps ? local release gate closed

1. Integrate exact locally release-verified checkpoint `26485dad2c931544728d108de9da66446dedf0a6` through the repository workflow without replaying stale branch-local continuity snapshots over newer main.
2. Inspect/run the exact-main hosted Windows Release Gate and persist its evidence before calling v8.8.3 fully closed.
3. Keep LR-008 whole-import staging/publication separate from this completed LR-011 repair.
4. Re-query live PR ownership. If no new owner exists after this lane closes, the highest-safety queued implementation candidate remains persisted game-profile ID path containment; launch-observation atomicity is another P1 boundary but should stay independent.
5. Preserve the permanent continuity constitution and active Learned Rules and require recursive propagation.

---

# v8.8.3 current next steps ? archive failure-cleanup candidate

1. Run `scripts/Verify-Release.ps1` on exact candidate `5688fe91c03b56b651a3e9d94d7111b974693ab9` plus current continuity inputs with the Windows marker restored for Remote Desktop Commander.
2. Run `scripts/Build-Release.ps1`; record exact artifact/build identity and SHA-256.
3. Commit/push verification and continuity evidence, integrate through the repository workflow, then inspect the exact-main hosted Windows gate before calling v8.8.3 closed.
4. Keep LR-008 whole-import staging/publication separate. After this lane closes, re-query live ownership; absent a new owner, the strongest queued P1 candidate is persisted game-profile ID path containment.
5. Preserve the permanent continuity constitution and active Learned Rules and require the successor to propagate them recursively.

---

# v8.8.2 next steps — current

1. Finish reconciling the metadata-only concurrent `main` advance, run the handoff/verification checks on the reconciled tree, commit intended continuity/evidence, and push canonical `main` without force.
2. Verify remote `main` resolves to the pushed commit and inspect its exact-main hosted Windows Release Gate; do not relabel local evidence as hosted evidence.
3. Re-query live PRs before assigning work. Do not duplicate Agent Control v2 or frontend lanes.
4. Remaining audited support boundaries include persisted game-profile ID path-containment repair, launch-observation persistence atomicity, duplicate-cleanup crash-durable reconciliation, broader LR-006 diagnostic export hardening, and remaining crash-bisector evidence/provenance work.
5. Preserve the permanent continuity constitution and active Learned Rules and require the successor to propagate them recursively.

---

## Current updater next steps — 2026-09-28

1. **DONE locally:** C12 first-publication discovery repair at exact code commit `9234c61c47f9ebc82b3a6ce546799ccaa6f395a2`; policy test, verifier **25/25**, FunctionVerifier **727/727**, Core **79/79**, Automation **24/24**, Integration **173/173**, self-test **11/11**, strict builds, ReadyToRun app/helper publish, package verification, and `Build-Release.ps1` all pass.
2. Commit/push the local-verification continuity checkpoint and integrate C12 through the repository workflow.
3. Run/inspect the exact-main Windows Release Gate and require successful immutable updater release publication with the exact two assets, tag target, sizes, and SHA-256 digests.
4. Run disposable installed-client old→new and fault-injected rollback, proving exact restarted build identity/health acknowledgement and unchanged seeded `Mods`, `State`, and unknown-file hashes.
5. Only then mark the automatic updater end-to-end complete.

The preceding exact-main run **36428542918** passed all verifier/build/package/policy stages and failed only before first release creation because empty release-list output was not normalized. No release/tag was created by that failed run. Local C12 build **230** ZIP SHA-256: `E613A43E69B75B5CCFF87852F918D8BD270888B3F8D4A493E88A3A8DFD5E67D8`. Preserve the permanent recursive continuity constitution and active Learned Rules.

## Support checkpoint — continuity validator adversarial hardening

An isolated support branch now implements the previously documented continuity-validator adversarial-fixture checkpoint. It strips HTML comments from semantic checks, rejects explicit successor-propagation/Core-Rule contradictions, scopes read-order validation to the active numbered section, and expands the negative suite from four to eight cases. Focused Windows handoff validation is green after final reconciliation to canonical `main` `151a370ef6c0b3d4e6b1d8a306576af1ae231c40`; full exact-source verifier/release-gate closure is not yet claimed. Read `_AGENT_CONTEXT/CONTINUITY_VALIDATOR_ADVERSARIAL_HARDENING_2026-09-28.md`. This support checkpoint does not replace or reorder the active updater production milestone.

# Active updater continuation — 2026-09-28

The user selected automatic updates as the current production boundary. Older archive-budget recommendations below are historical for this task. Work on `agent/auto-updater-20260928`; canonical starting main is `4fd61dd33609a7c55e5aedbaad026266a410f942`. Read `_AGENT_CONTEXT/AUTO_UPDATER_IMPLEMENTATION.md` and `_AGENT_CONTEXT/AUTO_UPDATER_NEXT_AGENT.md`.

The updater implementation is locally release-verified through C11b but is NOT yet hosted-main/end-to-end closed. Preserve the permanent continuity constitution and active Learned Rules; require the successor to preserve and recursively propagate them to the agent after them.

Updater checkpoints C4-C11b are integrated through exact commit `08054d96a8ba84819b6dfd775d56f9eeabc7986d`. C11b adds deterministic immutable private GitHub Release publication policy, exact two-asset verification, stale-main/evidence-only/monotonic-build refusal, final pre-publication `origin/main` revalidation, and closes the remaining updater metadata/rollback native-replacement fixtures. Exact local heaven/Windows/.NET 10.0.401 evidence: `Verify-Release.ps1` **25/25**, functions **727/727**, Core **79/79**, Automation **24/24**, Integration **173/173**, self-test **11/11**, strict analyzers/build, app ReadyToRun publish, updater-helper publish, package verifier and publication-policy tests PASS. `Build-Release.ps1` produced updater build **227** and ZIP SHA-256 `09ADF5E9B6C7C832633D7E5BA7C4DD2AA3EAB29F347E540B79542B731CD3665C`. No release was published from the updater branch. Exact next work: integrate this verified source to eligible `main`, run/inspect the hosted Windows gate and immutable `updater-main-<build>` release, then run disposable real old→new and injected-rollback end-to-end tests with seeded user-data hash checks and exact restart identity. The archive-budget recommendation immediately below remains separate historical guidance outside this selected updater task.

---
# Archive streaming integration status — IMPLEMENTED / LOCAL RELEASE-VERIFIED / EXACT-MAIN HOSTED GATE PENDING

Archive streaming cancellation and actual-output budgeting are no longer a future implementation candidate. PR #57 merged the production/test slice to canonical `main` at `a8b581176aac0e6bcf09c049285ed40f4b2b392c`.

Current source now:

- streams each archive entry through an async cancellation-aware read/write loop;
- counts actual decompressed bytes and fails before a write would exceed the operation budget;
- removes the currently owned partial output file on cancellation or actual-output-budget failure;
- preserves the existing trusted-root/path/reparse checks;
- includes focused regressions for single-entry mid-copy cancellation and actual-output budgeting.

PR #57 records fresh local heaven2/Windows verification on integration head `fd8b48fc92e6f5e64591fd1938b7ccce5ac94083` after rebasing onto then-current main base `8d5cc311ed13f5cb7f0df1f9e7c3e8bf9fcaec82`: verifier **25/25**, functions **728/728**, call sites **7,772 / 0 uncovered**, Core **79/79**, Automation **24/24**, Integration/fault injection **177/177**, self-test **11/11**, strict build/analyzers PASS, `Build-Release.ps1` PASS, ReadyToRun/updater-helper packaging PASS; artifact SHA-256 `AC3571853650CFA91243199B23A44007488F9244780FCBD18A7A38552B652734`.

Do **not** relabel that integration-head evidence as an exact-main hosted result. At the 2026-09-28 support checkpoint, exact merge commit `a8b581...` had no GitHub workflow run or combined status. The repository's prior last-closed exact verification remains authoritative until a normal Windows Release Gate on canonical main or an eligible exact descendant records newer evidence.

Read `ARCHIVE_STREAMING_INTEGRATION_PROVENANCE_AUDIT_2026-09-28.md` before reopening this boundary. Remaining disk free-space reserve/compression-ratio policy is separate future work, not evidence that the streaming cancellation/output-budget implementation is still missing.

The active automatic-updater milestone remains the current selected production boundary.

---

# CAS integrity checkpoint — CLOSED

CAS integrity is closed with both hosted and fresh local Windows evidence. Hosted Release Gate `36367883836` passed exact repair candidate `d001870d4cd3549841d8511392ae7885f174bca2`. Local heaven2 verification on canonical source `3556bddcd7c7f84c0efe2ff92f6d73e12842128f` passed focused `BlobIntegrityTests` **10/10**, `Verify-Release.ps1` **25/25**, Core **79/79**, Automation **20/20**, Integration **89/89**, self-test **11/11**, and `Build-Release.ps1` including win-x64 ReadyToRun publish. Local artifact SHA-256: `54C53313567D96E0FE937746FE0F323E229717CE7CE296694FEC151F2E73FC79`.

Historical note: recursive source reparse containment was the next independent boundary at this CAS checkpoint and has since been integrated. Current priority is the hosted final-integration gate described at the top of this file. Future agents must still evaluate reusable lessons for promotion into the trainer while keeping transient MHW state in `_AGENT_CONTEXT/`, and recursively propagate the continuity constitution.

## Newly integrated support research

Documentation-only CAS support work has been harvested from PRs #23, #25, and #22. Read the new filesystem-identity, digest-namespace, and recursive-source-containment audits before starting those later checkpoints. PR #24 was skipped as redundant with the broader filesystem-identity audit. The local CAS gate is now closed; keep the remaining audit findings as separate implementation boundaries.

---
# Next steps

## Follow-up support integration — CLOSED

Exact documentation/continuity integration commit `027b6d9dc9b049d9e9857e5a0e4d021e31adf443` passed hosted Windows Release Gate `36343967045`: **25/25**, functions **612/612**, call sites **6480 / 0 uncovered**, Core **79/79**, Automation **20/20**, Integration **79/79**, self-test **11/11**, ReadyToRun publish PASS, release SHA-256 `DC5A5F8DA92BE6A7469F3C6072BA6A555E5AAF5FDE25439D064CD115BA201BD6`. Evidence/cache persistence: `dadbe73a48567b17c9814c483f654be00d1d810f`.

No production source or tests were integrated by that historical support harvest. Its audits were future boundaries at the time; later work has since implemented recursive source reparse containment. Use the current section at the top of this file for present priority.


## Current checkpoint — native ReplaceFileW failure semantics CLOSED

Final exact verified commit: `17abfb05d83ff38040eb9356d34fbb3131644801`.  
Production implementation merge: `6d52ede722f18fcdbe727ec44e027de3e1c69fb1`.  
Hosted Windows Release Gate: `36342205103`.  
Evidence/cache persistence: `689a17ce5dff18bd0bf1201205446edd51ada8b5`.  
Release SHA-256: `B88694E81A35DCFF0C8FF76EBD07908ECA47B0AA2777ABE26B38686635373468`.

The LR-003 boundary is complete. `ReplaceFileW == false` is no longer treated as proof that staged recovery material can be discarded: documented 1176/1177 partial-name-mutation outcomes preserve the replacement staging path and remain fail-closed in `RecoveryRequired`. Focused fixtures pin 1175/1176/1177 filesystem and operation/journal postconditions.

### Historical independently verifiable boundary — completed 2026-09-28

This checkpoint called for **recursive source reparse containment** for ModScanner, unmanaged adoption, and Smart Inbox; that work is now integrated. The test-first requirements below are retained as historical acceptance criteria.

Keep it test-first and narrow:

1. re-check canonical `main`, current continuity, active Learned Rules, and `RECURSIVE_SOURCE_REPARSE_CONTAINMENT_AUDIT.md`;
2. identify each recursive traversal entry point and its current containment assumptions;
3. add real Windows junction/symlink fixtures that redirect traversal outside the configured source root;
4. prove the current behavior before changing production code;
5. implement the smallest fail-closed physical-containment guard required by those fixtures;
6. assert external redirected content is never scanned, adopted, staged, or imported;
7. run focused tests and then the repository's full exact Windows verification/build gates before closure.

Do **not** combine this with CAS root/hash-leaf identity, digest namespace validation, hardlink/migration redesign, async lifetime, diagnostics privacy, remote networking, backup, or Smart Pack work.

## Continuity

Preserve exact SHA/run evidence, update durable context, and explicitly require the successor to recursively propagate the permanent continuity constitution to the agent after them.

**Do not break the chain.**

---


## Current checkpoint — Windows live containment CLOSED

Exact verified commit: `356fde242046b78e39c7266c57b27e52220141fa`.  
Hosted Windows Release Gate: `36341049469`.  
Evidence/cache persistence: `dc7eb83c94427479c59413c77935050dadf051ff`.  
Release SHA-256: `F7CBC330D652835FFBC6A24395D105FF509F3800FE21E741FBDD9BAE7D94433D`.

The live `DeploymentExecutor` physical-containment boundary is complete and independently green. Do not reopen it merely because other filesystem findings remain.

### Highest-value next independently verifiable boundary

Implement **native `ReplaceFileW` failure-postcondition characterization** under LR-003.

Before editing:

1. re-check canonical `main`, current handoff, and active Learned Rules;
2. read the `ReplaceFileW` section of `WINDOWS_FILESYSTEM_SAFETY_AUDIT.md` and the relevant test-gap audit;
3. inspect `AtomicFileOps.ReplaceFromAsync`, `DeploymentExecutor` rollback/recovery, and existing Windows tests.

Keep the first slice narrow:

- introduce only the seam needed to control/native-characterize replacement outcomes;
- cover documented failure postconditions such as 1175, 1176, and 1177;
- assert actual destination/replacement/backup bytes and path existence, not only error codes;
- assert operation/journal state after failure and recovery;
- never treat `ReplaceFileW == false` as proof that the filesystem is unchanged;
- do not weaken `RecoveryRequired` fail-closed behavior to make tests pass.

Do **not** combine this with CAS corruption repair, recursive ModScanner/adoption/Smart Inbox containment, migration, async lifetime, diagnostics privacy, remote networking, backup, or Smart Pack work.

Residual live-containment risk remains documented: the closed reparse guard is path-based and cannot atomically prevent a topology swap after its final check. Handle-based containment would be a separate later boundary if justified.

## Continuity

Before every handoff, preserve exact SHA/run evidence, update durable context, run the repository handoff/verification checks, and explicitly require the successor to recursively propagate the permanent continuity constitution.

**Do not break the chain.**

---


## Current checkpoint — parallel support-audit integration

PlannerSnapshotRepository remains **CLOSED** at exact verified commit `efe58f38c4780d40200bcf2b7bbb5914ecd8ebc3`, hosted Windows Release Gate `36336190920`, evidence/cache persistence `852f07b9d6ad0457c161df0aa1c8165981d349cf`.

The parallel support-agent branches have been inventoried and their worthwhile documentation/research integrated. Read `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-27.md` for the exact branch dispositions and overlap decisions. No production C# or test code came from those support branches.

Active Learned Rules are now LR-001 through LR-009. They protect moved-body verification, compile-backed API caller closure, native replacement failure semantics, physical filesystem containment, restartable migration convergence/ownership, and share-boundary diagnostic sanitization.

## Recommended next independently verifiable boundary

Do **not** automatically start another ManagerDatabase extraction, reopen MainWindow page-model splitting, or bundle the support findings into one mega-hardening change.

The highest-safety next candidate supported independently by the test-gap and Windows-filesystem audits is a **test-first Windows filesystem physical-containment / native-replacement characterization checkpoint**:

1. re-check actual canonical `main`, current handoff, and active Learned Rules;
2. read `WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`, `TEST_GAP_AND_PERFORMANCE_AUDIT.md`, the deployment/recovery architecture, and current Windows tests;
3. first add focused Windows regressions for parent junction/reparse escape in Add/Replace/Remove/rollback/startup recovery and for documented `ReplaceFileW` failure postconditions;
4. do not weaken fail-closed recovery or path safety to make those tests pass;
5. only then implement the smallest production seam needed to satisfy one coherent invariant;
6. run the exact full Windows Release Gate and persist exact evidence before opening another safety boundary.

This is a recommendation, not permission to combine reparse containment, CAS redesign, migration redesign, async lifetime, diagnostics privacy, remote networking, CI supply-chain, backup, and bulk-fill work in one source checkpoint.

## Other preserved future boundaries

Keep these separate and consult their specialized audits before implementation:

- async/background lifetime ownership and staged-draft preservation;
- corrupt CAS object trust and legacy migration retry convergence;
- diagnostic support-bundle export sanitization and protected credential storage;
- remote preview egress/redirect/private-address/HTTPS/response validation;
- exact-input verification/stage-cache and CI supply-chain hardening;
- full manager backup/portability recovery capsule;
- source-neutral multi-provider acquisition;
- MHW physical-slot coverage calculator and marginal bulk-fill selection.
- launch-health/game-build revalidation semantics; read `LAUNCH_HEALTH_REVALIDATION_AUDIT.md` and keep runtime evidence separate from deployment success;
- mod retirement / same-ID re-import lifecycle closure; read `MOD_LIFECYCLE_REFERENTIAL_INTEGRITY_AUDIT.md` and centralize live semantic retirement separately from historical evidence;
- crash-bisector diagnosis evidence integrity; read `CRASH_BISECTOR_DIAGNOSIS_EVIDENCE_AUDIT.md` and validate baseline + full-candidate reproduction before persistent blame;
- catalog-invisible import publication; read `IMPORT_PUBLICATION_CATALOG_VISIBILITY_AUDIT.md` and stage outside catalog-visible roots until commit-on-success.
- archive extraction resource/cancellation hardening; read `ARCHIVE_EXTRACTION_RESOURCE_CANCELLATION_AUDIT.md`. A Windows runtime probe confirmed that canceling during a single 1 GiB entry can still finish the full write and return success. Keep this separate from the active recursive-reparse boundary.

## Verification truth

The support integration is documentation/continuity work and is now independently closed by hosted Windows Release Gate `36340312353` for exact integration commit `5619604e88a27176726ada8518f53d385abc7b0f`: repository verifier **25/25 PASS**, handoff continuity preflight PASS, release build/publish PASS, ReadyToRun fallback **False**, artifact SHA-256 `43C753174810650A4C4F8956F4329CD5A21A45B9253EDAE30E16EE0551F1FEBF`. Evidence/cache persistence: `4de981ab7ee47a3a8f0dd60f38e17644517b2ea3`.

Production source/tests were unchanged by the integration, and no cache was manually promoted outside the normal verifier/build path.

Historical failed planner runs `36335255922` and `36335692754` remain useful evidence in `VERIFICATION.md`; do not erase them.

## Continuity requirement

Before every future handoff, update the durable repository context, report verification only for exact inputs actually checked, commit/push meaningful checkpoints, and explicitly require your **successor** to inherit and recursively propagate the permanent continuity constitution to the **agent after them**.

**Do not break the chain.**
