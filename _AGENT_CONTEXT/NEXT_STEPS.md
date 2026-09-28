# Active CAS integrity checkpoint

Capture now rejects corrupt existing objects; restore validates private staged bytes before publication. Nine focused Windows regressions and the full local Windows gates pass for source 8334d725f6bdb73661e2f3ed71a04189db76950b: verifier 25/25, Integration 88/88, Automation 20/20, self-test 11/11, strict builds and ReadyToRun publish PASS. Hosted verification is pending; see EVIDENCE/cas-integrity-local-windows.log. Read `_AGENT_CONTEXT/CAS_INTEGRITY_CHECKPOINT.md` for design, reproduced failures, branch review, limits, and exact continuation.

Finish the exact Windows gates before taking recursive scanner/adoption/Smart Inbox containment as a separate boundary. The successor must inherit, preserve, and recursively propagate the permanent continuity constitution and active Learned Rules to the agent after them. Do not break the chain.

---
# Next steps

## Follow-up support integration — CLOSED

Exact documentation/continuity integration commit `027b6d9dc9b049d9e9857e5a0e4d021e31adf443` passed hosted Windows Release Gate `36343967045`: **25/25**, functions **612/612**, call sites **6480 / 0 uncovered**, Core **79/79**, Automation **20/20**, Integration **79/79**, self-test **11/11**, ReadyToRun publish PASS, release SHA-256 `DC5A5F8DA92BE6A7469F3C6072BA6A555E5AAF5FDE25439D064CD115BA201BD6`. Evidence/cache persistence: `dadbe73a48567b17c9814c483f654be00d1d810f`.

No production source or tests were integrated by this support harvest. The four new audits remain future independently scoped boundaries, not implementation claims. The current highest-value production action is still the CAS integrity checkpoint below.


## Current checkpoint — native ReplaceFileW failure semantics CLOSED

Final exact verified commit: `17abfb05d83ff38040eb9356d34fbb3131644801`.  
Production implementation merge: `6d52ede722f18fcdbe727ec44e027de3e1c69fb1`.  
Hosted Windows Release Gate: `36342205103`.  
Evidence/cache persistence: `689a17ce5dff18bd0bf1201205446edd51ada8b5`.  
Release SHA-256: `B88694E81A35DCFF0C8FF76EBD07908ECA47B0AA2777ABE26B38686635373468`.

The LR-003 boundary is complete. `ReplaceFileW == false` is no longer treated as proof that staged recovery material can be discarded: documented 1176/1177 partial-name-mutation outcomes preserve the replacement staging path and remain fail-closed in `RecoveryRequired`. Focused fixtures pin 1175/1176/1177 filesystem and operation/journal postconditions.

### Highest-value next independently verifiable boundary

Implement the **CAS integrity checkpoint** identified as step 3 in `TEST_GAP_AND_PERFORMANCE_AUDIT.md`.

Keep it test-first and narrow:

1. re-check canonical `main`, current continuity, and active Learned Rules;
2. inspect `BlobStore.CaptureWithHashAsync`, `BlobStore.RestoreAsync`, deployment/rollback CAS consumers, and the CAS sections of the filesystem/test-gap audits;
3. create a known blob, corrupt the existing hash-named CAS object without renaming it, and prove restore/deployment never accepts those bytes as the advertised SHA;
4. cover the existing-object capture path where a newly verified temp currently loses to an already-existing hash filename;
5. define the smallest safe behavior before implementation: reject/fail closed, or atomically replace a proven-corrupt object only when recovery invariants make that safe;
6. assert live destination bytes and operation/recovery state so a CAS integrity failure cannot silently become a committed deployment;
7. run the exact full hosted Windows Release Gate and persist exact evidence before opening another source boundary.

Do **not** combine this with recursive ModScanner/adoption/Smart Inbox reparse traversal, hardlink/migration redesign, async lifetime, diagnostics privacy, remote networking, backup, or Smart Pack work.

Recursive source/live reparse traversal remains a separate P0 finding in `WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`; address it after this CAS checkpoint unless new canonical evidence changes the priority.

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

## Verification truth

The support integration is documentation/continuity work and is now independently closed by hosted Windows Release Gate `36340312353` for exact integration commit `5619604e88a27176726ada8518f53d385abc7b0f`: repository verifier **25/25 PASS**, handoff continuity preflight PASS, release build/publish PASS, ReadyToRun fallback **False**, artifact SHA-256 `43C753174810650A4C4F8956F4329CD5A21A45B9253EDAE30E16EE0551F1FEBF`. Evidence/cache persistence: `4de981ab7ee47a3a8f0dd60f38e17644517b2ea3`.

Production source/tests were unchanged by the integration, and no cache was manually promoted outside the normal verifier/build path.

Historical failed planner runs `36335255922` and `36335692754` remain useful evidence in `VERIFICATION.md`; do not erase them.

## Continuity requirement

Before every future handoff, update the durable repository context, report verification only for exact inputs actually checked, commit/push meaningful checkpoints, and explicitly require your **successor** to inherit and recursively propagate the permanent continuity constitution to the **agent after them**.

**Do not break the chain.**
