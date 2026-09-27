# Parallel support-branch integration — 2026-09-27

## Canonical baseline and limits

Integration began from canonical `fengie/mhw-mods` `main` at `6ada5a5c4cc83afadfba42bc6af6559540920e3d`.

The GitHub connector was the available repository surface. The user's Remote Desktop Commander device was offline, so there was no local checkout on which to run `git status` or local commands. Canonical remote history, branch comparisons, head diffs, and branch file contents were inspected instead. GitHub's connector comparison exposes merge base, ahead/behind counts, and changed-file stats but not the full ordered unique-commit list; the table therefore records the exact branch head plus connector-reported unique-commit count rather than inventing unavailable SHAs.

No examined support branch contained production C# or test changes relative to the integration baseline. The useful contributions are audits, documentation corrections, handoff knowledge, and durable Learned Rules.

## Inventory and disposition

| Branch | Fork / unique work | Actual changed scope | Disposition |
| --- | --- | --- | --- |
| `agent/support-3-mainwindow-ui-audit-20260927` | merge base `0e561f3`; 2 unique commits; head `f344d57` | MainWindow responsibility audit + research addendum | **Partial integrate.** Append the independent re-audit to current canonical documents; do not replace newer canonical state. Revalidated conclusion: stop page-model splitting; measure Dispatcher projection and lifetime issues instead. |
| `agent/support-5-async-lifetime-audit-20260927` | fork `6ada5a5`; 2 unique commits; head `dade879` | async/lifetime audit + branch-local current-state link | **Integrate audit.** Preserve staged-draft loss, foreground/background exclusion, close/cancel-and-await, and owned-task findings. Synthesize current-state linkage centrally. |
| `agent/support-5-multisource-bulk-fill-audit-20260927` | fork `6ada5a5`; 1 unique commit; head `7c30243` | standalone multi-source discovery/bulk-fill architecture audit | **Integrate.** Independent design research; no production changes. |
| `agent/support-6-mhw-semantic-coverage-fill-audit-20260927` | fork `6ada5a5`; 1 unique commit; head `137b725` | standalone MHW semantic coverage/gap-fill audit | **Integrate.** Independent design research; keep semantic utility separate from exact planner conflict safety. |
| `agent/support-7-legacy-migration-recovery-audit-20260927` | fork `6ada5a5`; 9 unique commits; head `369531a` | migration audit, corrected `docs/MIGRATION.md`, LR candidate, branch-local continuity updates | **Partial integrate.** Keep audit, migration documentation correction, and migration Learned Rule. Rebuild aggregate continuity metadata rather than copying branch-local snapshots. |
| `agent/support-7-state-backup-portability-audit-20260927` | fork `6ada5a5`; 1 unique commit; head `74ad398` | standalone backup/portability/recovery audit | **Integrate.** Independent recovery design; no production changes. |
| `agent/support-8-diagnostics-privacy-audit-20260927` | fork `6ada5a5`; 9 unique commits; head `db1973a` | diagnostics privacy audit, LR candidate, branch-local continuity updates | **Partial integrate.** Keep specialized audit and Learned Rule, renumbered to LR-006 because migration independently reserved LR-005. Rebuild aggregate continuity metadata centrally. |
| `agent/support-diagnostic-privacy-audit-20260927` | identical to `main`; 0 unique commits; head `6ada5a5` | none | **Skip.** Empty/abandoned duplicate; no work to recover. |
| `agent/support-network-preview-security-audit-20260927` | fork `6ada5a5`; 1 unique commit; head `db1b12e` | standalone remote-preview network trust audit | **Integrate.** Preserve egress/redirect/private-address/HTTPS/response-validation findings. |
| `agent/test-gap-performance-audit-20260927-v2` | merge base `0e561f3`; 8 unique commits; head `115b755` | test-gap/performance audit, LR-003, branch-local continuity updates | **Partial integrate.** Keep v2 audit + LR-003; preserve newer canonical SQLite findings and rebuild aggregate continuity state. |
| `agent/test-gap-performance-audit-20260927` | merge base `161b5fc`; 6 unique commits; head `9d327e6` | earlier version of same test-gap audit | **Skip as superseded.** v2 is later, explicitly reconciles the canonical deep SQLite audit, and contains the useful evolved findings. |
| `agent/windows-filesystem-safety-audit-20260927` | fork `6ada5a5`; 8 unique commits; head `10e0064` | filesystem safety audit, LR-004, branch-local continuity updates | **Partial integrate.** Keep audit + LR-004; synthesize handoff state centrally. |
| `audit/verification-infrastructure-20260927` | merge base `0e561f3`; 3 unique commits; head `c024c59` | verification/CI/supply-chain audit + branch-local read-order/manifest links | **Partial integrate.** Keep audit and link it from the aggregate read/handoff system; do not import stale branch snapshots. |

## Overlap and conflict resolution

- **No production merge conflict exists.** All candidate work is documentation/research only.
- **MainWindow overlap:** the support re-audit is appended to the canonical responsibility audit rather than replacing it. The later PlannerSnapshotRepository closure remains authoritative.
- **SQLite overlap:** `SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md` already on canonical `main` remains the specialized authority for transaction details. The broader test/performance audit is complementary and explicitly defers to it.
- **Filesystem/test-gap overlap:** LR-003 covers native replacement failure postconditions; LR-004 covers physical containment/reparse traversal. Both are retained because they protect distinct invariants.
- **Learned Rule ID collision:** migration and diagnostics independently proposed LR-005. Migration retains LR-005 because the parallel branches had already reserved LR-003/LR-004 in sequence; diagnostics is integrated as LR-006. No rule text is discarded.
- **Stale continuity metadata:** support-branch copies of `CURRENT_REVISION.json`, `CURRENT_STATE.md`, `NEXT_STEPS.md`, `README_FIRST.md`, `NEXT-AGENT-START-HERE.md`, and `handoff-manifest.json` are not merged wholesale. Aggregate canonical versions are updated once after all audits are integrated.
- **Verification truth:** PlannerSnapshotRepository remains closed at exact verified commit `efe58f38c4780d40200bcf2b7bbb5914ecd8ebc3` / hosted run `36336190920`. Documentation integration does not transfer or fabricate product verification.

## Integration order used

1. copy standalone specialized audit documents;
2. integrate the migration documentation correction;
3. append the independent MainWindow re-audit to current canonical architecture/research docs;
4. integrate durable Learned Rules LR-003 through LR-006 with the rule-ID collision resolved;
5. synthesize canonical read order, current state, next steps, revision metadata, and handoff manifest;
6. compare the integration result against the pre-integration baseline and latest remote `main`;
7. merge only after continuity validation/hosted verification can be honestly characterized.

## Integrated knowledge that should shape future work

The support lanes reveal several independently scoped future checkpoints. They are not permission to combine unrelated fixes:

- Windows physical containment/reparse safety and native `ReplaceFileW` postconditions;
- corrupt/pre-existing CAS integrity and migration retry convergence;
- shell async/lifetime ownership and staged-draft preservation;
- support-bundle export privacy and protected secret handling;
- remote preview network egress policy;
- verification exact-input/supply-chain hardening;
- state backup/portability recovery design;
- multi-source discovery plus deterministic bulk-fill planning;
- MHW semantic coverage calculation separated from exact planner conflicts.

Choose one independently verifiable boundary at a time. Preserve the permanent continuity constitution and require every successor to pass it to the agent after them. **Do not break the chain.**

## Final canonical closure

The combined support integration was pushed to canonical `main` and verified on exact integration commit `5619604e88a27176726ada8518f53d385abc7b0f`.

Hosted Windows Release Gate `36340312353` completed successfully:

- repository verification: **25/25 PASS**;
- agent handoff continuity preflight: **PASS**;
- release build/publish: **PASS**;
- ReadyToRun fallback used: **False**;
- release artifact SHA-256: `43C753174810650A4C4F8956F4329CD5A21A45B9253EDAE30E16EE0551F1FEBF`;
- workflow evidence/cache persistence commit: `4de981ab7ee47a3a8f0dd60f38e17644517b2ea3`.

The workflow's persistence step also completed successfully. Follow-up handoff-only closure edits use `[skip ci]` and do not alter production source, tests, verifier scripts, workflows, or verification caches.

No support branches were deleted.

