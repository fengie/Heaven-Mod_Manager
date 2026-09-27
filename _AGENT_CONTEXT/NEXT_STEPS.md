# Parallel support candidate — import publication isolation

A separate documentation-only support audit records a P1 import-publication boundary in `_AGENT_CONTEXT/IMPORT_PUBLICATION_CATALOG_VISIBILITY_AUDIT.md`.

The live-deployment reparse-containment checkpoint has now landed on canonical main; do not reopen or fold it into this import boundary. When import publication is prioritized, begin with regressions for mixed success/failure, cancellation, restart/process-death residue, retry convergence, and exactly-once successful publication. The intended production seam is catalog-invisible staging outside `ModsRoot` plus a commit-on-success final move shared by manual archive import and Smart Inbox. Do not combine it with mod retirement (PR #15), Smart Pack, native replacement, CAS work, migration, or deployment redesign.

This support branch adds LR-008. PR #15 independently reserves LR-007; preserve both.

---

# Next steps

## ACTIVE — verify and close Windows live-containment only

Canonical handoff base: `208a66da89632acf36c065dc3bfead76af8d6bf4`  
Production source: `b671bac33917649ff89e5e3b0866725f7b165232`  
Focused tests: `5479c2e2ef6f0c731ccad8d65fcacd558c1428c1`  
Branch: `agent/windows-live-containment-hardening-v3`

This is the only active production boundary.

Exact next action:

1. re-check canonical `main` and intervening commits before integration;
2. integrate the candidate without reverting the hosted support-integration closure;
3. run `scripts/Test-AgentHandoff.ps1` and the repository's exact full hosted Windows Release Gate;
4. fix any compiler/analyzer/test/function-verifier regression without weakening checks;
5. preserve any failed run and root cause;
6. persist exact SHA-bound evidence and close only this boundary;
7. keep the documented handle-level topology-swap TOCTOU limitation explicit.

### Recommended next programming boundary after this closes

Isolate native **`ReplaceFileW` failure postconditions** under LR-003. Add a narrow injectable native-replacement seam and Windows fixtures for documented 1175/1176/1177 states; assert actual destination/replacement recovery bytes and operation/journal state. A false native return must never be treated as proof that nothing changed.

Keep CAS corruption, recursive scanner/adoption/Inbox traversal, migration, async lifetime, diagnostics privacy, remote networking, backup, and Smart Pack as separate future boundaries.

The successor must inherit and recursively propagate the permanent continuity constitution.

**Do not break the chain.**

---

## Current checkpoint — parallel support-audit integration

PlannerSnapshotRepository remains **CLOSED** at exact verified commit `efe58f38c4780d40200bcf2b7bbb5914ecd8ebc3`, hosted Windows Release Gate `36336190920`, evidence/cache persistence `852f07b9d6ad0457c161df0aa1c8165981d349cf`.

The parallel support-agent branches have been inventoried and their worthwhile documentation/research integrated. Read `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-27.md` for the exact branch dispositions and overlap decisions. No production C# or test code came from those support branches.

Active Learned Rules are now LR-001 through LR-006. They protect moved-body verification, compile-backed API caller closure, native replacement failure semantics, physical filesystem containment, restartable migration convergence/ownership, and share-boundary diagnostic sanitization.

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

## Verification truth

The support integration is documentation/continuity work and is now independently closed by hosted Windows Release Gate `36340312353` for exact integration commit `5619604e88a27176726ada8518f53d385abc7b0f`: repository verifier **25/25 PASS**, handoff continuity preflight PASS, release build/publish PASS, ReadyToRun fallback **False**, artifact SHA-256 `43C753174810650A4C4F8956F4329CD5A21A45B9253EDAE30E16EE0551F1FEBF`. Evidence/cache persistence: `4de981ab7ee47a3a8f0dd60f38e17644517b2ea3`.

Production source/tests were unchanged by the integration, and no cache was manually promoted outside the normal verifier/build path.

Historical failed planner runs `36335255922` and `36335692754` remain useful evidence in `VERIFICATION.md`; do not erase them.

## Continuity requirement

Before every future handoff, update the durable repository context, report verification only for exact inputs actually checked, commit/push meaningful checkpoints, and explicitly require your **successor** to inherit and recursively propagate the permanent continuity constitution to the **agent after them**.

**Do not break the chain.**
