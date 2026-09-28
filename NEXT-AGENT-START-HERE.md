# Active updater continuation — 2026-09-28

The user selected automatic updates as the current production boundary. Older archive-budget recommendations below are historical for this task. Work on `agent/auto-updater-20260928`; canonical starting main is `4fd61dd33609a7c55e5aedbaad026266a410f942`. Read `_AGENT_CONTEXT/AUTO_UPDATER_IMPLEMENTATION.md` and `_AGENT_CONTEXT/AUTO_UPDATER_NEXT_AGENT.md`.

The updater implementation is locally release-verified through C11b but is NOT yet hosted-main/end-to-end closed. Preserve the permanent continuity constitution and active Learned Rules; require the successor to preserve and recursively propagate them to the agent after them.

Updater checkpoints C4-C11b now culminate at exact integrated commit `08054d96a8ba84819b6dfd775d56f9eeabc7986d`. C9 closes updater-level LR-003 native 1176/1177 fail-closed behavior plus fresh-process recovery; C10 integrates/hardens WPF client lifetime and helper handoff; C11a builds/verifies deterministic updater ownership/install/build/update metadata and the complete helper invocation closure; C11b adds immutable exact-main GitHub Release policy, deterministic artifact identity, exact two-asset verification, stale-main/evidence-only/monotonic-build refusal, final pre-publication `origin/main` revalidation, and remaining metadata/rollback native replacement coverage. Exact local heaven/Windows/.NET 10.0.401 evidence at C11b: `Verify-Release.ps1` **25/25**, FunctionVerifier **727/727**, Core **79/79**, Automation **24/24**, Integration **173/173**, self-test **11/11**, strict analyzers, ReadyToRun app publish, updater-helper publish, package verifier, publication-policy tests, and `Build-Release.ps1` PASS; updater build **227**, ZIP SHA-256 `09ADF5E9B6C7C832633D7E5BA7C4DD2AA3EAB29F347E540B79542B731CD3665C`. No release was published from this branch. Next: integrate to eligible exact `main`, verify hosted immutable publication, then run disposable real old→new and injected rollback end-to-end tests with seeded user-data hashes and exact restart identity.

---
# 2026-09-28 support integration — CLOSED / HOSTED WINDOWS VERIFIED

Recursive source reparse containment and archive extraction physical-root containment are now integrated on canonical `main`. Exact integrated source `c6c70dd2f8db760ad236b0188cc7026a502afb7a` passed local `Verify-Release.ps1` **25/25**, functions **615/615**, call sites **6532 / 0 uncovered**, Core **79/79**, Automation **24/24**, Integration **96/96**, self-test **11/11**, strict analyzers, and `Build-Release.ps1` with ReadyToRun publish. Local ZIP SHA-256: `359B12050437AEF0EE9695FEFCE4B8424475413EF54299B2DAC345F5C4E32B21`.

Read `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-28.md` first. Older text below that names recursive source reparse containment as the next boundary is historical and superseded by this section. Hosted Windows Release Gate **36392282315** passed exact production integration commit `d66bff290f197236ec43c9b37d2b015ab2ee5fe8` and persisted evidence/cache state in `a94066004660e4d542f5d4a528c7e5e22bdea9cb`. The hosted release artifact SHA-256 is `A264C5108DDEA0E3301BFF0E33D7A3E7DA3A92A566739C865AE28B8111BFAFAB`. The strongest independently reproduced unimplemented production boundary is now archive extraction streaming cancellation / actual-output resource budgeting in `ARCHIVE_EXTRACTION_RESOURCE_CANCELLATION_AUDIT.md`.

Active Learned Rules are now **LR-001 through LR-010**. Preserve the permanent continuity constitution and require your successor to propagate it again.

---

# CAS integrity checkpoint — CLOSED

CAS integrity is now closed. Hosted Windows Release Gate `36367883836` passed exact repair candidate `d001870d4cd3549841d8511392ae7885f174bca2`. Fresh local Windows closure then passed on canonical `main` source `3556bddcd7c7f84c0efe2ff92f6d73e12842128f` (same production CAS code; later changes were verification evidence/documentation): focused `BlobIntegrityTests` **10/10**, repository verifier **25/25**, Core **79/79**, Automation **20/20**, Integration/fault injection **89/89**, self-test **11/11**, strict analyzers/build PASS, and win-x64 ReadyToRun publish PASS. Local release SHA-256: `54C53313567D96E0FE937746FE0F323E229717CE7CE296694FEC151F2E73FC79`. Remote Desktop Commander omitted the normal `OS=Windows_NT` environment variable; the host was independently confirmed as Win32NT/Windows, and the unchanged scripts were rerun with that standard process-local marker restored. See `_AGENT_CONTEXT/CAS_INTEGRITY_CHECKPOINT.md` and `_AGENT_CONTEXT/VERIFICATION.md`.

Historical note: recursive source reparse containment for ModScanner / unmanaged adoption / Smart Inbox was the next boundary at this CAS checkpoint and has since been integrated and hosted-Windows verified. Current action is the archive streaming cancellation/resource-budget boundary stated at the top of this file. Keep CAS root/hash-leaf identity and digest-namespace findings separately scoped. The successor must inherit, preserve, and recursively propagate the permanent continuity constitution and active Learned Rules to the agent after them. Do not break the chain.

---
# NEXT AGENT — START HERE

## Permanent recursive continuity obligation — read before any edit

You inherit the repository's **permanent continuity constitution**.

Before modifying this project:

1. inspect actual canonical `fengie/mhw-mods` `main`, `git status` when a checkout is available, and recent relevant history/diffs;
2. read `_AGENT_TRAINING/README.md` and the company-doctrine sections relevant to the task;
3. read `_AGENT_CONTEXT/CURRENT_REVISION.json`;
4. read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`;
5. read active `_AGENT_CONTEXT/LEARNED_RULES.md` (currently LR-001 through LR-010);
6. read `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-28.md`, then the 2026-09-27 historical inventory as needed;
7. follow the remaining order in `_AGENT_CONTEXT/README_FIRST.md`.

The authority hierarchy is company engineering doctrine → project-specific operating rules/current repository truth → current task instructions, with more specific and newer verified repository truth taking precedence when guidance conflicts.

Preserve the constitution. Obey active Learned Rules. Add a project Learned Rule only when a concrete, durable discovery justifies it. Before finishing any meaningful engineering task, also ask whether the work revealed a reusable lesson that should update `_AGENT_TRAINING/`.

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

## Historical next programmer boundary — completed 2026-09-28

This section originally selected **recursive source reparse containment** for ModScanner, unmanaged adoption, and Smart Inbox. That checkpoint is now integrated; its instructions below are retained as historical acceptance criteria.

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

- LR-001 — moved production bodies require verification-instrumentation re-audit.
- LR-002 — shared API removal requires compile-backed caller closure.
- LR-003 — native replacement failure is not equivalent to no filesystem mutation.
- LR-004 — lexical containment is not physical filesystem containment.
- LR-005 — restartable migrations must prove ownership and convergence.
- LR-006 — shareable diagnostic artifacts require export-boundary sanitization.
- LR-007 — entity retirement must close live semantic references.
- LR-008 — import publication requires catalog-invisible staging.
- LR-009 — automated diagnosis must validate its control before persisting blame.
- LR-010 — a containment check after mutation is not fail-closed.

The diagnostics support branch originally also proposed LR-005; integration deliberately renumbered it LR-006 to preserve the append-only ledger without losing either rule. A later crash-diagnosis branch independently proposed LR-007; follow-up integration preserved lifecycle LR-007 and import LR-008, and renumbered crash diagnosis to LR-009 without changing the rule.

## What to do next

Do **not** automatically:

- extract another ManagerDatabase repository;
- reopen MainWindow page-model splitting;
- combine all support findings into one hardening change;
- weaken tests or verification to obtain a green result.

Read `_AGENT_CONTEXT/NEXT_STEPS.md` for the current recommendation. First close the final integrated main with the repository-native hosted Windows gate. After hosted closure, the current strongest separately scoped candidate is **archive extraction streaming cancellation / actual-output resource budgeting**. Re-check canonical source before acting; CAS integrity, Windows live containment, native `ReplaceFileW`, recursive source reparse containment, and archive physical-root containment are implemented.

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
