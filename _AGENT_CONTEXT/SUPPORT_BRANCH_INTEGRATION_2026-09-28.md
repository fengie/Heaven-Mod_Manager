# Support branch integration — 2026-09-28

## Canonical baseline and scope

- Canonical repository: `fengie/mhw-mods`, branch `main`.
- Pre-integration canonical baseline: `2d6969c7d94438dd64a540dab93fc9a910a8458b`.
- Integration was performed on `heaven2` from a clean main checkout after fetch/prune.
- Recovery branch: `integration-backup-20260928-0327` at the pre-integration baseline.
- The prior 2026-09-27 support inventory remains historical authority for older lanes already harvested there.
- No support branch was merged wholesale. Production/test commits, durable audit documents, and reusable process lessons were selected independently against current main.
- During finalization, canonical main advanced first with hosted evidence commit `a940660` and then with CI race-hardening commit `f825658`. Both were inspected and preserved by rebasing this integration on top; hosted caches remained authoritative and no concurrent canonical work was overwritten.

## Branch dispositions

| Branch | Work | Disposition |
| --- | --- | --- |
| `agent/recursive-source-reparse-hardening-20260927` | Safe recursive traversal for scanner/adoption/Smart Inbox plus adversarial follow-up tests | **Integrated selectively**: production/test commits `f51f729` and `742484b`; stale branch-local caches/handoff snapshots were not copied |
| `agent/recursive-source-reparse-stress-followup-20260928` | Same final recursive candidate | **Skipped duplicate**: same head `8bbb1ba` as the hardening branch |
| `agent/heavy-stress-safety-20260928` | Archive trusted-root containment, fail-before-mutation regressions, process checkpoint rule, evidence | **Integrated selectively**: `cf509b6`, `6ece2af`, `f46ead7`, `333b4ef`, `a1d0119`, `ea5c9f8`, plus durable evidence/LR-010; stale branch caches/handoff snapshots were not copied |
| `agent/support-recursive-source-candidate-audit-20260928` | Independent adversarial review of recursive candidate | **Integrated** as durable audit `RECURSIVE_SOURCE_REPARSE_CANDIDATE_ADVERSARIAL_REVIEW.md` |
| `agent/support-archive-hardening-audit-20260928` | Runtime reproduction of archive trusted-root junction escape | **Integrated** as durable physical-root audit |
| `agent/support-archive-resource-cancellation-audit-20260928` | Runtime reproduction of single-entry cancellation/resource-budget weakness | **Integrated** as future-boundary audit plus generic trainer lesson |

## Older unmerged-by-ancestry refs

The fetch also showed older 2026-09-27 support refs that are not ancestors of current main, including CAS identity/reparse/digest audits, ReplaceFileW characterization lanes, Windows live-containment lanes, diagnostics/network/migration/lifecycle audits, and early test-gap/UI audits. Their worthwhile findings were already harvested by the 2026-09-27 canonical integration and later closure commits. They were rechecked against current continuity and were **not re-integrated** merely because Git ancestry still shows them as unmerged. In particular, `agent/support-recursive-source-containment-audit-20260927` is patch-equivalent to content already present on main.

## Integration order and conflict decisions

1. Added the short-interval durable checkpoint rule first.
2. Integrated recursive-source production/tests, then its adversarial parity/root/cycle follow-up.
3. Ran focused Windows Automation and Integration tests before pushing the first checkpoint.
4. Integrated archive physical-root containment and its fail-before-mutation tests on top.
5. Re-ran focused Windows Automation and Integration tests and pushed a second checkpoint.
6. Integrated documentation-only audits and reusable safety lessons.
7. Preserved current-main architecture when `SmartInboxService.cs` and `HardeningTests.cs` overlapped; Git auto-merged the selected commits cleanly and the combined tests verified the semantics.
8. Did not import support-branch verification caches as canonical evidence. The combined source earned a fresh local Windows verifier/release run instead.

## Combined local Windows verification

Exact integrated source verified before evidence/continuity persistence: `c6c70dd2f8db760ad236b0188cc7026a502afb7a`.

- `scripts/Verify-Release.ps1`: **25/25 PASS**.
- Production function inventory: **615/615 verified**.
- Explicit call sites: **6532 / 0 uncovered**; trace gaps **0**; parse errors **0**.
- Core unit tests: **79/79 PASS**.
- Automation tests: **24/24 PASS**.
- Integration/fault-injection tests: **96/96 PASS**.
- Automation self-test: **11/11 PASS**.
- Strict whole-solution/project analyzers: **PASS**, 0 warnings/errors.
- `scripts/Build-Release.ps1`: **PASS**.
- win-x64 self-contained ReadyToRun publish: **PASS**.
- Local release ZIP SHA-256: `359B12050437AEF0EE9695FEFCE4B8424475413EF54299B2DAC345F5C4E32B21`.

## Residuals and next boundary

The integrated reparse protections remain path-based. The documented check/open TOCTOU window and hardlink policy are not claimed solved. A dedicated file-symlink/reparse-leaf runtime fixture remains unexecuted because the local Windows account lacked symlink-creation privilege; directory-junction root/descendant/cycle cases are covered.

Archive extraction physical-root containment is implemented. The independent archive resource/cancellation audit remains **unimplemented**: a runtime probe showed cancellation during a single large entry can be ignored by the synchronous payload write and still return success. Keep streaming cancellation/actual-output budgeting as a separate production checkpoint.

Hosted Windows Release Gate `36392282315` passed exact production integration commit `d66bff290f197236ec43c9b37d2b015ab2ee5fe8`; GitHub Actions persisted the hosted verification cache/evidence at `a94066004660e4d542f5d4a528c7e5e22bdea9cb`. Hosted release ZIP SHA-256: `A264C5108DDEA0E3301BFF0E33D7A3E7DA3A92A566739C865AE28B8111BFAFAB`. The separate local Windows closure remains useful corroborating evidence for the same production code plus documentation.

## Continuity decision

LR-010 was added: a containment check after mutation is not fail-closed. The generic company safety doctrine also records the generalized pre-mutation validation lesson and the independent stream-level cancellation/resource-budget lesson.

The successor must inspect current `origin/main` before acting, preserve exact verification provenance, and inherit/preserve/recursively propagate the permanent continuity constitution and active Learned Rules to the agent after them. **Do not break the chain.**


## Second support harvest — v8.8.2 integration

Pre-integration canonical baseline: `9dd91767880ae6c9dcb2a31d64410c1f0bd52827` (v8.8.1 updater REST-tag verification). The clean canonical checkout on `heaven2` was fetched/pruned and fast-forwarded before integration. Branch-local verification caches and stale routing snapshots were not imported.

| Branch / PR | Purpose | Disposition |
| --- | --- | --- |
| `agent/support-game-profile-id-containment-audit-20260928` / #62 | Persisted `GameProfile.Id` workspace/state path-containment audit | **Partially integrated**: durable audit preserved; stale point-in-time routing edits skipped |
| `agent/support-launch-observation-atomicity-audit-20260928` / #64 | Launch-history/trust persistence atomicity audit | **Partially integrated**: specialized audit + discovery registration preserved; stale reconciliation snapshots skipped |
| `agent/support-profile-save-atomicity-20260928` / #69 | Fault-injection proof of existing profile-save transaction rollback | **Integrated**: regression + checkpoint + SQLite audit link |
| `agent/support-continuity-adversarial-fixtures-20260928` / #74 | Handoff validator semantic hardening | **Integrated**: validator, eight-fixture negative suite, checkpoint/discovery registration |
| `agent/support-crash-bisector-control-preflight-20260928` / #76 | Validate empty control and full suspect set before bisection | **Integrated selectively**: production/tests/checkpoint; branch merge commits and verification caches were not replayed |
| `agent/crash-bisector-evidence-integrity-20260928` / #73 | Alternate CB-01 implementation | **Superseded** by #76; its stale cache/handoff deltas were not imported |
| `agent/duplicate-cleanup-normal-failure-recovery-20260928` / #71 | Compensate DB-delete failure after duplicate archive move | **Integrated**: narrow production compensation + real SQLite trigger regression + checkpoint |
| `agent/support-bundle-share-sanitization-20260928` / #79 | Sanitize recent structured logs at support-bundle export | **Integrated selectively**: production/test/docs/evidence; branch-local `.verification` cache commit skipped |
| `agent/support-live-routing-reconciliation-20260928` / #70 | Point-in-time routing snapshot | **Skipped as stale**: snapshot predates current canonical v8.8.1 state and active lanes changed repeatedly |
| updater PRs #65/#77/#78 | Updater publication/tag verification | **Already canonical / superseded** by `9dd9176`; no duplicate integration |
| `feature/agent-control-plane-v2-20260928` / #59 | Agent Control v2 | **Left active/unresolved**; separate production lane, not a support-harvest merge |
| `ui/frontend-responsive-polish-20260928` / #55 | Frontend responsive UX | **Left active/unresolved**; separate production lane |

Shipped v8.8.2 scope is deliberately limited to crash-bisector preflight, duplicate-cleanup ordinary-failure compensation, and support-bundle structured-log sanitization. The game-profile ID and launch-observation findings remain regression-first implementation follow-ups; duplicate cleanup is not yet crash-durable; broader LR-006 diagnostics sanitization remains open.

## Second-harvest combined verification

Exact locally verified integration source: `dbfaccba6ec15ed1c509ba47194c3e98c4b0c31d`.

- `scripts/Verify-Release.ps1`: **25/25 PASS**; FunctionVerifier **736/736**, **7,842 / 0 uncovered**, 0 trace gaps, 0 parse errors.
- Core **79/79**; Automation **28/28**; Integration/fault injection **178/178**; self-test **11/11**; strict builds/analyzers **0 warnings / 0 errors**.
- `scripts/Build-Release.ps1`: **PASS**; ReadyToRun app and updater helper publish PASS; updater build **309**.
- Local v8.8.2 ZIP SHA-256: `9E07AB718E6094CD90C36E7E20D8DDBD282D9F97A161BFCECBACA844ACFE9086`.
- Handoff preflight passed and all eight adversarial negative fixtures failed closed.

During verification canonical `main` advanced from `9dd9176` to metadata-only `2a0acd9951d67b724a43ef79ec7078d3cc412ddc`; that delta changes the hosted-evidence path/version and continuity metadata only. It is reconciled before push, and the v8.8.2 hosted workflow is updated to write v8.8.2 evidence. Exact-main hosted verification remains pending until the canonical push completes and the workflow runs.

## Final reconciliation — v8.8.4 support recovery hardening

Initial final-harvest inventory began from `86d6f9cb07fa15574aad4cc6b0c9cfd84d011c07`. Before promotion, canonical `origin/main` advanced to `317ba6c86d54012a65a41772109a72566d29c0a9` with the complete v8.8.3 archive-streaming cleanup release and hosted evidence. The candidate was therefore rebuilt from `317ba6c` by squash-merging only the reviewed harvest; newer archive code, tests, verification state, and evidence were preserved. Candidate support refs were re-inventoried with patch-equivalence, unique commits, changed-file lists, and direct diff review; unmerged ancestry alone was never treated as a merge requirement.

| Branch | Disposition |
| --- | --- |
| `agent/support-save-snapshot-prune-integrity-20260928` | **Integrated manually/hardened.** The finding and regression were valid, but the branch implementation was not accepted verbatim: persisted `root_path` may not authorize recursive deletion outside an enumerated direct child of `SnapshotRoot`, and over-limit metadata is retired only after owned payload deletion succeeds. Added an outside-root sentinel regression. |
| `agent/support-remote-preview-egress-hardening-20260928` | **Integrated selectively.** Production egress policy, network regressions, specialized audit, and local evidence preserved; stale branch handoff/cache state skipped. Current v8.8.3 identity was preserved during conflict resolution. |
| `agent/support-legacy-migration-hardlink-runtime-20260928` | **Documentation/research integrated selectively.** Runtime audit + raw evidence + generalized immutable-byte-ownership doctrine preserved as LR-012; stale routing snapshots skipped. |
| `agent/support-multi-instance-mutation-audit-20260928` | **Documentation/research integrated selectively.** Deterministic live-peer recovery audit + evidence + writer-orphanhood doctrine preserved as LR-013; stale routing snapshots skipped. |
| `agent/support-updater-cross-session-ownership-audit-20260928` | **Documentation/research integrated selectively.** Cross-session single-writer audit preserved as LR-014; stale updater routing edits skipped. |
| support-bundle share-safety / CAS-reparse / diagnostic-privacy historical refs | **Skipped as superseded/redundant.** Their useful behavior/findings are already represented by newer canonical implementations/audits and prior integration ledgers. |
| continuity-drift / live-routing / PR-disposition snapshot branches | **Skipped as stale point-in-time coordination state.** Current Git/PR state and canonical continuity outrank snapshots. |
| older updater C9/C10/WPF/packaging support audits | **Skipped as superseded by later canonical updater implementation and verification records.** |
| Agent Control v2, frontend/UI, updater implementation, game-profile implementation lanes | **Left separate/active.** They are independently owned production lanes, not support-harvest material. |

Conflict decisions:
- Save-snapshot support code was repaired rather than blindly cherry-picked because its DB-provided path/deletion ordering weakened the existing containment/cleanup invariants.
- Remote-preview `NexusMetadataService.cs` overlap was resolved against current v8.8.3 source; the obsolete shared HTTP client was removed and the new split clients retained, with User-Agent synchronized to v8.8.3.
- Parallel Learned Rule numbering collided with canonical LR-011. Canonical assignments are LR-012 immutable content-store byte ownership, LR-013 writer-orphanhood before recovery takeover, and LR-014 cross-session mutation ownership.

Final local Windows closure on exact product/source `b48c1ff865ab41841d8c7eb931fca19f371f960e`: Verify-Release **25/25**; FunctionVerifier **748/748**, **7,921** explicit call sites / **0** uncovered; Core **79/79**; Automation **31/31**; Integration **199/199**; self-test **11/11**; strict builds/analyzers PASS; Build-Release ReadyToRun/helper publish PASS; updater build **326**; ZIP SHA-256 `0F9B9577190037F29B500D2A709356A5F11E1CABA9770343FAA89F160AE6B154`. Canonical main was pushed after the final race check and remote `origin/main` was verified at integration/evidence checkpoint `05d01c982249e5d8d654ad125925af3ac2a85a91`. This final continuity-only checkpoint changes no product source; hosted exact-main verification remains pending. No support-branch `.verification` cache was imported as canonical proof, and no published history was rewritten.

The successor must inspect live `origin/main` and open work before acting, preserve exact verification provenance, inherit the permanent continuity constitution and active Learned Rules, and recursively propagate them to the agent after them. **Do not break the chain.**

## 2026-09-29 post-updater support inventory

Canonical remote `main` was re-established from live GitHub state before this inventory. The current updater/publication closure is already canonical and the repository's current-state instructions explicitly keep the disposable installed-client E2E as the only active updater completion boundary. This pass therefore reviewed divergent support work for patch-equivalence and current usefulness without merging unrelated production lanes into that E2E boundary.

| Branch / lane | Current comparison / finding | Disposition |
| --- | --- | --- |
| `support/updater-artifact-accept-20260928` | Old support commit adds binary `application/octet-stream` Accept handling for GitHub release assets. Current `main` already contains that behavior in `GitHubUpdateSource.DownloadArtifactAsync`. | **Skip as already present on main.** |
| `support/updater-c9-recovery-review-20260928` | Test-only fresh-process 1176/1177 recovery proof. Current `main` already contains the same `Ambiguous_native_replace_failure_recovers_then_retries_on_next_apply` regression. | **Skip as already present on main.** |
| `support/updater-wpf-restart-args-20260928` | Older helper-side stale-health-argument stripping. Current `main` sanitizes earlier in `UpdateClientService` through `UpdateArgumentSanitizer.RemoveHealthArguments`, and the canonical C10 version also rejects malformed owned health flags. | **Skip as superseded by stricter canonical architecture; do not duplicate parsing in the helper.** |
| `support/updater-c11-gap-closure-20260928`, `support/updater-publication-race-20260928`, `agent/updater-tag-verification-race-20260928`, `agent/updater-existing-release-rest-ref-main-reconcile-20260929` | Live compare shows no unique commits ahead of current main for the reconciled branches; the REST-ref retry fix is already canonical through PR #91. | **Skip as already canonical / ancestry-complete.** |
| older C12/C13 updater publication branches, including `agent/auto-updater-publication-fix-20260928`, `agent/auto-updater-postupload-publication-20260928`, and `agent/updater-publication-verification-20260928` | Still appear divergent by ancestry, but their useful publication/retry behavior has been superseded by the verified PR #91 closure and immutable `updater-main-61` publication. Their branch-local handoff/version snapshots are stale. | **Skip wholesale; preserve current main and exact hosted evidence.** |
| `support/updater-publication-input-scope-20260928` | Contains a unique policy/test idea that documentation-only paths should not trigger updater publication. Current main intentionally still classifies several docs/root files as release-relevant. | **Defer, not merge now.** This is verification/release-policy behavior and is unrelated to the current disposable installed-client E2E; reassess as a separately verified post-E2E boundary if still desired. |
| `agent/game-profile-id-containment-v8.8.5-reconcile` / PR #90 | Unique production/tests remain useful, but the branch is behind current updater-fixed main. Its own continuity says the repaired candidate reached 24/25 because handoff metadata failed, then metadata was repaired; the exact new head still lacks the required 25/25 rerun and has no hosted workflow evidence. | **Leave draft/defer.** Reconcile onto current main and rerun exact-candidate verification before integration. |
| `feature/agent-control-plane-v2-20260928` / PR #59 | Active independently owned production lane with its own remaining blockers. | **Leave separate.** |
| frontend/UI lane / PR #55 | Active independently owned production lane. | **Leave separate.** |

### Conflict and architecture decisions

- Unmerged Git ancestry was not treated as evidence that code is missing. Current-file inspection was used to distinguish patch-equivalent/superseded updater work from genuinely unique support work.
- The helper restart-argument support patch was deliberately not replayed because canonical C10 already removes updater health arguments before the request is written and has stricter malformed-input handling. Duplicating the same policy in the helper would create a second parsing authority.
- PR #90 was not merged simply because its source fix is useful: exact verification is incomplete for its repaired head, it is behind current main, and current canonical instructions explicitly keep game-profile work out of the updater E2E lane.
- The docs-only updater publication-scope idea is preserved as a follow-up finding, not silently lost, but changing release-input classification now would widen the active boundary and change what “green/publishable” means.

### Verification and remaining work

This pass verified integration disposition through live branch comparisons, direct commit/patch inspection, current-main source inspection, open-PR state, and exact continuity/verification records. No production source, tests, verification cache, release metadata, or app version were changed by this inventory. Because this pass used the GitHub repository connector rather than a local checkout, there is no local working tree whose `git status` can be claimed; no local cleanliness claim is made.

The next task remains the repository-declared automatic-updater disposable installed-client E2E: prove old→new build 61 with exact restart/health identity and unchanged seeded `Mods`, `State`, and unknown-file hashes, then prove the separate fault-injected rollback path. Do not merge PR #90, Agent Control, frontend, or other unrelated support work into that E2E lane.

The successor inherits the permanent continuity constitution and active Learned Rules, must preserve them, and must require its successor to recursively propagate them again. **Do not break the chain.**

