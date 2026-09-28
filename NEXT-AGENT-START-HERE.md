# Recursive source reparse containment — IMPLEMENTED / LOCAL WINDOWS GREEN / HOSTED PENDING

Source implementation checkpoint: `f51f72927e8f90c264df1ef197ba6c9bbe2704de`.

Real-Windows tests-first evidence proved the prior defect in ModScanner, unmanaged adoption, and Smart Inbox, then the shared `SafeRecursiveTraversal` repair made Integration **91/91** and Automation **21/21** green. Full local `Verify-Release.ps1` passed **25/25** with functions **614/614**, call sites **6512 / 0 uncovered**, and `Build-Release.ps1` passed Core **79/79**, Automation **21/21**, Integration **91/91**, self-test **11/11**, ReadyToRun publish, release SHA-256 `D18E151644149EBCCC49ABCECB0FB84D51E1B95D7C407FB69D0D6FBA97CB3EE6`.

Do not call this boundary fully closed until the exact final branch candidate passes the hosted Windows Release Gate. See `_AGENT_CONTEXT/HEAVY_STRESS_TESTING_SAFETY_REPORT_2026-09-28.md` and `_AGENT_CONTEXT/EVIDENCE/recursive-source-reparse-local-windows-closure.md`.

---
# CAS integrity checkpoint â€” CLOSED

CAS integrity is now closed. Hosted Windows Release Gate `36367883836` passed exact repair candidate `d001870d4cd3549841d8511392ae7885f174bca2`. Fresh local Windows closure then passed on canonical `main` source `3556bddcd7c7f84c0efe2ff92f6d73e12842128f` (same production CAS code; later changes were verification evidence/documentation): focused `BlobIntegrityTests` **10/10**, repository verifier **25/25**, Core **79/79**, Automation **20/20**, Integration/fault injection **89/89**, self-test **11/11**, strict analyzers/build PASS, and win-x64 ReadyToRun publish PASS. Local release SHA-256: `54C53313567D96E0FE937746FE0F323E229717CE7CE296694FEC151F2E73FC79`. Remote Desktop Commander omitted the normal `OS=Windows_NT` environment variable; the host was independently confirmed as Win32NT/Windows, and the unchanged scripts were rerun with that standard process-local marker restored. See `_AGENT_CONTEXT/CAS_INTEGRITY_CHECKPOINT.md` and `_AGENT_CONTEXT/VERIFICATION.md`.

The next independent production boundary is recursive source reparse containment for ModScanner / unmanaged adoption / Smart Inbox. Keep CAS root/hash-leaf identity and digest-namespace findings as separately scoped future work. The successor must inherit, preserve, and recursively propagate the permanent continuity constitution and active Learned Rules to the agent after them. Do not break the chain.

---
# NEXT AGENT â€” START HERE

## Permanent recursive continuity obligation â€” read before any edit

You inherit the repository's **permanent continuity constitution**.

Before modifying this project:

1. inspect actual canonical `fengie/mhw-mods` `main`, `git status` when a checkout is available, and recent relevant history/diffs;
2. read `_AGENT_CONTEXT/CURRENT_REVISION.json`;
3. read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`;
4. read active `_AGENT_CONTEXT/LEARNED_RULES.md` (currently LR-001 through LR-009);
5. read `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-27.md`;
6. follow the remaining order in `_AGENT_CONTEXT/README_FIRST.md`.

Preserve the constitution. Obey active Learned Rules. Add a Learned Rule only when a concrete, durable discovery justifies it.

Before finishing, update durable repository handoff state and explicitly require your **successor** to inherit, preserve, and recursively propagate these same rules. That successor must repeat the requirement for the **agent after them**. The next agent must be able to continue without previous chat history.

**Do not break the chain.**

## CLOSED checkpoint: native ReplaceFileW failure postconditions

This LR-003 boundary is complete and hosted-Windows verified.

- final exact verified commit: `17abfb05d83ff38040eb9356d34fbb3131644801`
- production implementation merge: `6d52ede722f18fcdbe727ec44e027de3e1c69fb1`
- Windows Release Gate: `36342205103`
- evidence/cache persistence: `689a17ce5dff18bd0bf1201205446edd51ada8b5`
- verifier: **25/25 PASS**
- functions: **612/612**
- explicit call sites: **6480 / 0 uncovered**
- Core **79/79**, Automation **20/20**, Integration **79/79**, self-test **11/11**
- ReadyToRun self-contained publish: PASS
- release SHA-256: `B88694E81A35DCFF0C8FF76EBD07908ECA47B0AA2777ABE26B38686635373468`

`AtomicFileOps` now has a narrow injectable native replacement seam. Documented 1176/1177 partial-name-mutation failures preserve staged replacement bytes and remain fail-closed as `RecoveryRequired` / journal `Writing`; 1175 rolls back to exact BEFORE bytes with operation/journal `RolledBack`. Do not reopen this boundary to solve CAS, recursive traversal, or migration findings.

## Exact next programmer boundary

The next highest-value independent checkpoint is **recursive source reparse containment** for ModScanner, unmanaged adoption, and Smart Inbox.

Read `_AGENT_CONTEXT/RECURSIVE_SOURCE_REPARSE_CONTAINMENT_AUDIT.md` and the relevant filesystem-safety findings. Add Windows junction/symlink fixtures first, characterize each recursive traversal entry point, then apply the smallest fail-closed containment change required by evidence.

Keep CAS root/hash-leaf filesystem identity, digest namespace validation, migration, async, diagnostics, networking, backup, and Smart Pack as separate boundaries.

You inherit the permanent continuity constitution and active LR-001 through LR-009. Before finishing, update durable context and explicitly require your successor to inherit, preserve, and recursively propagate these same rules to the agent after them.

**Do not break the chain.**

## CLOSED checkpoint: Windows live DeploymentExecutor physical containment

This boundary is complete and hosted-Windows verified.

- exact verified merge: `356fde242046b78e39c7266c57b27e52220141fa`
- Windows Release Gate: `36341049469`
- evidence/cache persistence: `dc7eb83c94427479c59413c77935050dadf051ff`
- verifier: **25/25 PASS**
- functions: **611/611**
- explicit call sites: **6478 / 0 uncovered**
- Core **79/79**, Automation **20/20**, Integration **76/76**, self-test **11/11**
- ReadyToRun self-contained publish: PASS
- release SHA-256: `F7CBC330D652835FFBC6A24395D105FF509F3800FE21E741FBDD9BAE7D94433D`

The manager now fails closed on descendant reparse/junction traversal in live deployment preparation/preconditions, immediate Add/Replace/Remove mutation, rollback/startup recovery, pruning, and lock inspection. Focused Windows tests use real junctions.

Do not reopen this boundary to solve unrelated filesystem findings. The path-based guard has one explicitly documented residual risk: a topology swap after the final attribute check is still a TOCTOU window; no handle/file-ID atomicity is claimed.

## Exact next programmer boundary

The next highest-value independently verifiable step is **native `ReplaceFileW` failure-postcondition characterization** under LR-003.

Before modifying source, inspect actual canonical `main`, current continuity, `AtomicFileOps.ReplaceFromAsync`, `DeploymentExecutor` recovery, and the `ReplaceFileW` section of `WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`.

Keep that checkpoint narrow: introduce only the seam necessary to reproduce/document 1175/1176/1177 postconditions, assert actual destination/replacement recovery bytes and operation/journal state, and preserve fail-closed `RecoveryRequired`. Do not mix CAS, recursive scans, migration, async, diagnostics, networking, backup, or Smart Pack work into it.

You inherit the permanent continuity constitution and active LR-001 through LR-009. Before finishing, update durable context and explicitly require your successor to inherit, preserve, and recursively propagate these same rules to the agent after them.

**Do not break the chain.**

## Current canonical product checkpoint

PlannerSnapshotRepository is **CLOSED**.

- exact verified source/commit: `efe58f38c4780d40200bcf2b7bbb5914ecd8ebc3`
- hosted Windows Release Gate: `36336190920`
- evidence/cache persistence: `852f07b9d6ad0457c161df0aa1c8165981d349cf`
- repository verifier: **25/25**
- production fingerprints: **610/610**
- explicit call sites: **6456**, uncovered **0**
- Core: **79/79**
- Automation: **20/20**
- Integration/fault injection: **72/72**
- self-test: **11/11**
- App win-x64 analyzers: PASS
- ReadyToRun self-contained publish: PASS

The historical failed runs `36335255922` and `36335692754` are preserved in `VERIFICATION.md` with their root causes. No second storage production boundary has been started.

## Parallel support-agent integration

The integration agent inspected the recent support branches and recovered every worthwhile contribution without blindly merging stale branch-local handoff snapshots.

Canonical inventory and decisions:

`_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-27.md`

Important result: the support branches contributed **documentation/research only** relative to the integration baseline; none contained production C# or test changes that needed merging.

The combined integration is now closed by hosted Windows Release Gate `36340312353` for exact commit `5619604e88a27176726ada8518f53d385abc7b0f`: repository verifier **25/25 PASS**, handoff continuity preflight PASS, release build/publish PASS, ReadyToRun fallback **False**, artifact SHA-256 `43C753174810650A4C4F8956F4329CD5A21A45B9253EDAE30E16EE0551F1FEBF`. Evidence/cache persistence commit: `4de981ab7ee47a3a8f0dd60f38e17644517b2ea3`.

The later follow-up support harvest (PRs #14, #15, #16, #18) is also CLOSED: exact integration commit `027b6d9dc9b049d9e9857e5a0e4d021e31adf443`, hosted Windows Release Gate `36343967045`, evidence/cache persistence `dadbe73a48567b17c9814c483f654be00d1d810f`, verifier **25/25**, functions **612/612**, call sites **6480 / 0 uncovered**, Core **79/79**, Automation **20/20**, Integration **79/79**, self-test **11/11**, release SHA-256 `DC5A5F8DA92BE6A7469F3C6072BA6A555E5AAF5FDE25439D064CD115BA201BD6`. It was documentation/continuity only; do not treat its audit findings as implemented behavior.

New durable specialized audits cover:

- MainWindow ownership re-audit;
- async/background lifetime and cancellation;
- broad test/failure/performance gaps;
- Windows filesystem/reparse/CAS/native-replacement safety;
- legacy migration/recovery;
- verification infrastructure / CI supply chain;
- diagnostics privacy / secrets;
- remote preview network trust;
- state backup / portability;
- multi-source discovery / bulk fill;
- MHW semantic coverage / gap fill;
- launch-health/game-build revalidation;
- mod lifecycle / entity retirement integrity;
- crash-bisector diagnosis evidence integrity;
- import publication / catalog visibility integrity.

Migration documentation was corrected to match current source behavior.

## Active Learned Rules

Read the full ledger; do not rely only on this summary.

- LR-001 â€” moved production bodies require verification-instrumentation re-audit.
- LR-002 â€” shared API removal requires compile-backed caller closure.
- LR-003 â€” native replacement failure is not equivalent to no filesystem mutation.
- LR-004 â€” lexical containment is not physical filesystem containment.
- LR-005 â€” restartable migrations must prove ownership and convergence.
- LR-006 â€” shareable diagnostic artifacts require export-boundary sanitization.
- LR-007 â€” entity retirement must close live semantic references.
- LR-008 â€” import publication requires catalog-invisible staging.
- LR-009 â€” automated diagnosis must validate its control before persisting blame.

The diagnostics support branch originally also proposed LR-005; integration deliberately renumbered it LR-006 to preserve the append-only ledger without losing either rule. A later crash-diagnosis branch independently proposed LR-007; follow-up integration preserved lifecycle LR-007 and import LR-008, and renumbered crash diagnosis to LR-009 without changing the rule.

## What to do next

Do **not** automatically:

- extract another ManagerDatabase repository;
- reopen MainWindow page-model splitting;
- combine all support findings into one hardening change;
- weaken tests or verification to obtain a green result.

Read `_AGENT_CONTEXT/NEXT_STEPS.md` for the current recommendation. The current highest-safety candidate is the separate, test-first **recursive source reparse containment** checkpoint. Re-check current canonical source before acting; CAS integrity, Windows live containment, and native `ReplaceFileW` are closed.

Any production source change starts a new exact verification boundary and must earn a fresh full Windows Release Gate.

## Handoff finish rule

Before your task ends:

- preserve exact source/verification provenance;
- update `CURRENT_STATE.md`, `NEXT_STEPS.md`, `CURRENT_REVISION.json`, and any specialized audit affected by your work;
- keep `handoff-manifest.json` accurate;
- run the handoff validator and recursive-continuity negative fixtures through the normal verification path;
- push meaningful checkpoints without force-rewriting canonical history;
- explicitly require your successor to pass this same continuity system to the agent after them.

**Do not break the chain.**
