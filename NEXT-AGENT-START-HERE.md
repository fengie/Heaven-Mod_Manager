# NEXT AGENT — START HERE

## Permanent recursive continuity obligation — read before any edit

You inherit the repository's **permanent continuity constitution**.

Before modifying this project:

1. inspect actual canonical `fengie/mhw-mods` `main`, `git status` when a checkout is available, and recent relevant history/diffs;
2. read `_AGENT_CONTEXT/CURRENT_REVISION.json`;
3. read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`;
4. read active `_AGENT_CONTEXT/LEARNED_RULES.md` (currently LR-001 through LR-006);
5. read `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-27.md`;
6. follow the remaining order in `_AGENT_CONTEXT/README_FIRST.md`.

Preserve the constitution. Obey active Learned Rules. Add a Learned Rule only when a concrete, durable discovery justifies it.

Before finishing, update durable repository handoff state and explicitly require your **successor** to inherit, preserve, and recursively propagate these same rules. That successor must repeat the requirement for the **agent after them**. The next agent must be able to continue without previous chat history.

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
- MHW semantic coverage / gap fill.

Migration documentation was corrected to match current source behavior.

## Active Learned Rules

Read the full ledger; do not rely only on this summary.

- LR-001 — moved production bodies require verification-instrumentation re-audit.
- LR-002 — shared API removal requires compile-backed caller closure.
- LR-003 — native replacement failure is not equivalent to no filesystem mutation.
- LR-004 — lexical containment is not physical filesystem containment.
- LR-005 — restartable migrations must prove ownership and convergence.
- LR-006 — shareable diagnostic artifacts require export-boundary sanitization.

The diagnostics support branch originally also proposed LR-005; integration deliberately renumbered it LR-006 to preserve the append-only ledger without losing either rule.

## What to do next

Do **not** automatically:

- extract another ManagerDatabase repository;
- reopen MainWindow page-model splitting;
- combine all support findings into one hardening change;
- weaken tests or verification to obtain a green result.

Read `_AGENT_CONTEXT/NEXT_STEPS.md` for the current recommendation. The highest-safety candidate is a separate, test-first Windows filesystem physical-containment/native-`ReplaceFileW` characterization checkpoint, because two independent support audits identified that boundary. Re-check current canonical source before acting.

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
