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



---

## Follow-up support-branch integration pass — 2026-09-27

### Canonical baseline and execution surface

This follow-up pass re-established canonical truth at remote `main`:

`3991b2c1fffcb6e32cb3f0225919900455310fa9`

The connected GitHub repository remained the available execution surface. No local checkout was available, so there was no local `git status`; remote refs, merge bases, exact unique commit lists, changed-file inventories, branch file contents, open PRs, and source-path deltas were inspected instead.

The audited production/source paths underlying the four accepted later audits have **not changed** between each audit's fork point and `3991b2c1...`, so their source conclusions remain applicable to the pre-integration canonical source. This pass adds documentation/continuity only; it does not merge production C# or tests.

### Later candidate inventory and disposition

| Branch / PR | Fork point / exact unique commits | Actual changed scope | Disposition |
| --- | --- | --- | --- |
| `agent/support-launch-health-revalidation-audit-20260927` / PR #14 | merge base `6ada5a5c4cc83afadfba42bc6af6559540920e3d`; `d5e4b8778526122797c1dcbeaefeea086c49d7e6` | standalone launch-health / game-build revalidation audit | **Integrate audit only.** Current audited source paths are unchanged; preserve the findings as a separate future boundary. |
| `agent/support-mod-lifecycle-integrity-audit-20260927` / PR #15 | merge base `a6cfef0bb161a7846cfab7c0761f9f4d90ea46e1`; `59f904c87fb74a8221ca11bd9d0745b160204eb4`, `a8bade5db824917e183399a07fa1f4c80f4b3b37`, `24eb0560eefca3452b3ddd5709fff29a852d8c3b`, `5bd2ff0688c6a5e8669cdb5ab36d37f62a8f0dd5`, `1bc0f85cb25c701fbf249a9118e39bcfa77273d5`, `661f991e15d0d92348a5d649a572998b0f345c07`, `aa46c01f5b8f31bc6abd3de1a57e95b9df1a8f9a`, `ae26978696137ca31be5377e790c603a96d92583`, `795c9bfb86013cf59ad759a378fa719e1c400f7a` | lifecycle audit, LR candidate, branch-local continuity snapshots | **Partial integrate.** Keep the audit and entity-retirement rule as canonical LR-007; rebuild aggregate continuity from current main rather than copying stale snapshots. |
| `agent/support-crash-bisect-diagnosis-audit-20260927` / PR #16 | merge base `5619604e88a27176726ada8518f53d385abc7b0f`; `9e77172ec007ad03eebef92c7f12bcb4adb72885`, `6fbe005a2b487e41651db2004445d17a73e932aa` | crash-bisector evidence-integrity audit + LR candidate | **Partial integrate.** Keep the audit and rule, renumbered from branch-local LR-007 to canonical LR-009 because LR-007/LR-008 are already reserved by the coordinated lifecycle/import lanes. |
| `agent/support-import-publication-integrity-audit-20260927` / PR #18 | merge base `356fde242046b78e39c7266c57b27e52220141fa`; `308413a69230e2265029e022345bd05a0dd9a023`, `1f6570c5925298b13f54a56d34baec904b29465d`, `7e781b67cd18a5a7e3123b021950ce77777fe4e4`, `68527b7cf817d4c14116fa6639d627b3d864634b`, `62d03fffe7e75726c0a6c2a2ac239a77ccd515f2`, `9f72c276bfffdd47b47e8b63ca21045cb87cb36a`, `1b3a1ed5dc57fa63bbcc4b8858e1da8c54f5a8f2`, `ca8fd28056ab5b9d3cf9d4b223f8853b9aad5bae`, `5c1bf849e999c8ee4ba52ce03d7757638f084baa`, `edaa4f4f7c7f05c43f8e681aba796e55c36f74a3`, `ab0b4614aba9186dd071b4a96a3c2480ef77122c`, `80e191c5b074b34101a8ccd4a92f1d15a0f7620c`, `f18214a6aae708c0ff54d6563860c20d172a3ea0`, `38b2e7ef5ac0c70736744877d41bc495dea790e0`, `3022c4597c6eba5462d23663aaa3fdd55623fae8`, `cd48284742d20fae21d2935f07228edb76a46c07`, `95eb5c8410e89374c3c98aef63ab2d58b38c1764`, `7bbf62d593396879d2c5f6844db2c2d7686bca25`, `53d57ad72b25e6ab40daf723b87c6c6b4afc47cb`, `e91a0b2f932df5951914574378c2659e10bbffd8`, `b507871182df85bb30af6a171fa79fe365d2affc`, `94723c2fdd39aa01ffb19062795a2f7444af2bf0`, `19fbd7ad3b137e873e33166cb6ee4ef37f3a449c`, `a002284e22b83e4ef83950f904a149f1f3b225c9`, `69448717e59ef6d70d7a876897b999b8d092ecb1`, `6ede7e86b327c07ab55903f0070144822e39df94` | import-publication audit, LR-008, repeated upstream merges/restores, stale branch-local handoff snapshots | **Partial integrate.** Keep the audit + LR-008 only; do not replay the repeated branch-local continuity commits. |
| `agent/support-diagnostic-privacy-audit-20260927` | merge base `6ada5a5c4cc83afadfba42bc6af6559540920e3d`; `bab029d70b2ca50c8b7643d54920641bb1997d0e` | combined diagnostic privacy + remote-preview trust audit | **Skip as redundant/superseded.** Its substantive findings are already covered more completely by canonical `DIAGNOSTICS_PRIVACY_AND_SECRET_HANDLING_AUDIT.md` and `REMOTE_PREVIEW_NETWORK_TRUST_AUDIT.md`; no production/test delta exists to salvage. |

### Non-support branches deliberately excluded

- `agent/smart-pack-planner-core` / PR #13 is a separate production feature branch with C# and tests. It is not a support-audit contribution and remains outside this integration boundary.
- `codex/complete-mod-workflows` / PR #1 is an older broad production feature branch and remains out of scope.
- `agent/windows-live-containment-hardening*` and `agent/replacefilew-*` are implementation/history branches for boundaries already integrated and independently hosted-Windows verified on canonical main; their remaining branch divergence is not missing support work.
- `agent/architecture-explain-why` and `agent/integrate-support-audits-20260927` are ancestors of current main and contain no unique commits now.

### Canonical Learned Rule resolution

The follow-up pass preserves all independent durable rules without ID collision:

- LR-007 — entity retirement must close live semantic references;
- LR-008 — import publication requires catalog-invisible staging;
- LR-009 — automated diagnosis must validate its control before persisting blame.

The crash branch's parallel LR-007 proposal is renumbered to LR-009. Rule meaning is unchanged.

### Historical support branch exact-commit appendix

The earlier inventory remains authoritative for its original dispositions. Exact current unique-commit lists are recorded here so future agents do not need to reconstruct them:

- `agent/support-3-mainwindow-ui-audit-20260927`: `c9776623190f30e677169caa6b72bf66f4cb4c6a`, `f344d57e8b5a5198d66ad43c8e73e6e97414ffd3`.
- `agent/support-5-async-lifetime-audit-20260927`: `d030277a7d33e072f93a2142f33669700889a748`, `dade8794b6ae1c589d6af6835de20ddd791b7d4d`.
- `agent/support-5-multisource-bulk-fill-audit-20260927`: `7c30243d794e7e21d1778a6aaf4ce8ed4685de03`.
- `agent/support-6-mhw-semantic-coverage-fill-audit-20260927`: `137b72540f3dcc16b78e2b4ae3a24931417a2024`.
- `agent/support-7-legacy-migration-recovery-audit-20260927`: `f883e41f9cdfc59c96dc4c47f5897155f8eb79f6`, `43e9fcb80a2002002c0cf44742465811ae01f4c1`, `29007812cd51180926a2dcb60853bdff1f4c7a60`, `b5090625cd9c5b369ea1c89a139f656ffb3c5746`, `426687c88ed2f854e08a1a0afafd8a22ab24f27f`, `5f0b9a61f37760dfeb16f032826e9c2e59f26bfa`, `2a223ebdec5829c913d11155a32a843930b9c543`, `ec2f99f8cc682c9ea25e0d901b20b6b6d87a5928`, `369531a6585acc2001611803d7ef14d1791c3480`.
- `agent/support-7-state-backup-portability-audit-20260927`: `74ad39813a7a44e914d3a7ea6e34fe8292ccfcfb`.
- `agent/support-8-diagnostics-privacy-audit-20260927`: `a4230efc92041e3521eb8f02ece2639408d03d0c`, `dcc76baa4f855f4035e7ec883e734ad085cf91c7`, `a9a2e2cb47c105aa8a6a5051dea6611a645f8439`, `14ea4aa94bddafe73fa94b08fc88206e775c479a`, `28861ebbff1b0c4716263b01f052ba627f71cdeb`, `faa92f207931d3a5420c6b6e506bc238588c2e83`, `cc8de02bd293940dbfb2039c340d291021687762`, `34865739ea817215076860c34495376dfe5b1bcc`, `db1973aee39efeb8398d8b3d0f4d14f5263fd6e4`.
- `agent/support-network-preview-security-audit-20260927`: `db1b12e7e66db81366b0254cb55bc695b6a53182`.
- `agent/test-gap-performance-audit-20260927-v2`: `8bb47884c841ca8c27652777fc45da0d673389ef`, `455fd63d2e1e70b094de0669337720bcacdfb6bb`, `67e8334dc00e669d9a6f5454d17cfb0dd05920d0`, `439f16e48a375c97bf8569500e5a3a6c3821df7f`, `53c9c6909c56f515d4227badb3016286a2850546`, `8ca541d300777cafe4fcb7311d596a5d78f166c4`, `f365305a0bff6645210db85312715d14d858c16a`, `115b75589dd285a63623ee48bcb19b6684db362c`.
- `agent/test-gap-performance-audit-20260927`: `691d3d20ae8b51462f0794afa768c2e1c26c1e47`, `bb736c5080edc40997917a0a4102c51393b67044`, `5387afc9fb94746e75f0fcfbaf940269523c73b8`, `875aaf07e28708d79a3d20e9b4ae164e445af0c4`, `93d32c58931eeb60fb34a07bafe365cc93a778a0`, `9d327e624d36a4763f7922688557f7e1aae62377` (superseded by v2).
- `agent/windows-filesystem-safety-audit-20260927`: `8c0817d5fdf06f93beb1ef7f39bf36cf56ba698c`, `41c9ea030de975505bdce8520b7bcd12542f712b`, `5dc1c243ee5a2081023b3b0832f73a08ab73c0a2`, `3df066f4ca4ba960a6f97952c64b2001780bbf20`, `1804f0460d2e9dfd95f8195790dc61bea8e7d3a3`, `c82315e5f9421863e3c9644c51cd69a74fece48d`, `8bb372ca420b630616e35c3f7b5d5286d46b95cb`, `10e00643765ccf5ad6875a3a55bb4a055ef59f05`.
- `audit/verification-infrastructure-20260927`: `fc122369292a60c7d6a6967df9868d42f1e7c4d0`, `da3c19446039c5fbd513a6d00be317ce3c66b9fc`, `c024c5978c4a829daf0a55b46c8f18f349e20bf6`.

### Follow-up integration order

1. add the four standalone specialized audit documents;
2. append LR-007/LR-008/LR-009 with the collision resolved;
3. update canonical read order, state, next steps, revision metadata and handoff manifest once from current main;
4. preserve the existing CAS-integrity next production boundary unchanged;
5. run the repository's exact hosted Windows Release Gate on the integrated canonical commit before calling this follow-up pass closed.

**Do not break the chain.**


### Follow-up canonical closure

The follow-up support integration was fast-forwarded to canonical `main` as exact integration commit:

`027b6d9dc9b049d9e9857e5a0e4d021e31adf443`

Hosted Windows Release Gate `36343967045` verified that exact commit on Windows X64 / .NET SDK 10.0.401:

- repository verification: **25/25 PASS**;
- agent handoff continuity preflight: **PASS**;
- production function fingerprints: **612/612** known-good/promoted;
- explicit call sites: **6480**, uncovered **0**;
- trace gaps / parse errors: **0 / 0**;
- Core tests: **79/79 PASS**;
- Automation tests: **20/20 PASS**;
- Integration/fault-injection tests: **79/79 PASS**;
- automation self-test: **11/11 PASS**;
- strict whole solution and App win-x64 compile/analyzers: **PASS**;
- self-contained ReadyToRun publish: **PASS**, fallback **False**;
- release artifact SHA-256: `DC5A5F8DA92BE6A7469F3C6072BA6A555E5AAF5FDE25439D064CD115BA201BD6`.

The workflow persisted exact evidence/promoted-cache state in commit `dadbe73a48567b17c9814c483f654be00d1d810f`. The integration commit changed documentation/continuity only; no production C#, tests, verification scripts, workflows, or pre-existing verification-cache semantics were modified by the support harvest itself.

The current canonical handoff commit is the commit containing this closure note; retrieve the exact current SHA from `main`. Support branches were deliberately preserved as historical evidence and were not deleted.


---

## CAS support-audit harvest — 2026-09-27 late pass

### Canonical baseline

This pass re-established remote canonical `main` at `265d58d6a9d4ffb6ab62cde987ba6ad335eae05a`. The active CAS production source had already passed hosted Windows Release Gate `36367883836`; the separately required fresh local Windows verification was still pending because the authorized runner was not executing commands. This pass therefore integrated **documentation/research only** and did not alter or claim closure of the active CAS gate.

### Inventory and disposition

| Branch / PR | Fork relationship to current main | Actual changed scope | Disposition |
| --- | --- | --- | --- |
| `agent/support-cas-filesystem-identity-audit-20260927` / PR #23 | diverged; merge base `e5325317cb7005bdf9d3082a033ab95e666ebbf9`; 5 commits ahead, 1 behind when inventoried | `CAS_FILESYSTEM_IDENTITY_REPARSE_AUDIT.md` plus branch-local README/manifest linkage | **Integrate audit.** Broader current-source authority for CAS root/hash-leaf physical identity, reparse policy, trust-anchor choice, and legacy hardlink-alias risk. Rebuild canonical linkage from current main rather than replaying branch-local snapshots. |
| `agent/support-cas-digest-namespace-audit-20260927` / PR #25 | diverged; merge base `d001870d4cd3549841d8511392ae7885f174bca2`; 4 commits ahead, 2 behind when inventoried | digest namespace audit plus branch-local README/manifest linkage | **Integrate audit.** Independent SHA-token/path-namespace boundary: exact 64-hex normalization, malformed persisted/legacy identifiers, and traversal-safe regression fixtures. |
| `agent/support-recursive-source-containment-audit-20260927` / PR #22 | diverged; merge base `d001870d4cd3549841d8511392ae7885f174bca2`; 1 commit ahead, 2 behind when inventoried | standalone recursive-source containment audit | **Integrate.** Independent future boundary for ModScanner, unmanaged adoption, and Smart Inbox; no production changes. |
| `agent/support-cas-reparse-policy-audit-20260927` / PR #24 | ahead of `265d58d...` by 3 commits when inventoried | standalone narrower CAS root/hash-leaf reparse audit | **Skip as redundant.** Its substantive root-junction/hash-leaf policy and Windows fixture guidance is already covered by the broader filesystem-identity audit. No production/test delta exists to salvage, and keeping both as canonical authorities would create duplicate guidance. |

### Integration decisions

- No production C#, tests, schemas, workflows, verification scripts, or promoted caches are integrated by this pass.
- The active CAS byte-integrity/concurrency behavior and exact hosted evidence remain unchanged.
- The three accepted audits are linked from `README_FIRST.md` and required by `handoff-manifest.json`.
- The CAS reparse-policy duplicate is deliberately not added as a second canonical authority.
- These audits define **future independent checkpoints** only. The fresh local Windows CAS verification remains the immediate required action before any production implementation begins.
- Support branches are preserved as historical evidence; none are deleted.

### Verification honesty

Performed through the connected GitHub repository surface:

- re-established canonical remote main and inspected branch-vs-main merge bases/ahead-behind/file scopes;
- read the actual new audit contents and reconciled overlap;
- confirmed the accepted work is documentation/research only;
- rebuilt canonical continuity linkage rather than importing stale branch-local handoff files.

Not performed in this pass:

- no local `git status` because the authorized Windows runner advertised online but did not execute terminal/ping calls;
- no local PowerShell handoff validator;
- no local .NET tests/build;
- no fresh local Windows CAS gate;
- no verification-cache promotion.

The successor must still finish the exact fresh local Windows CAS closure before starting the production checkpoints described by these audits, preserve the permanent continuity constitution and active Learned Rules, and explicitly require its successor to recursively propagate them to the agent after them. **Do not break the chain.**
