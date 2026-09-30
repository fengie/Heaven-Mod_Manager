# READ THIS FIRST — MHW Manual Mod Manager v8.8.50

**Current reconciliation checkpoint — 2026-09-29 (v8.8.7):** canonical `main` was audited at `fb3fb7ea5c5e4b133cea96f1c52dd9f4a3df327f`, and the shipped identity is v8.8.7. Cross-session updater ownership, Agent Control 0.5.1 liveness, reliability coverage, security/authorization hardening, profile/UI integration, and updater E2E/release-gating follow-ups are canonical.

The last fully closed hosted verification recorded in continuity remains exact source `5abe40304dfcb48f96e750bd7da3d0075315625b` / run `36541891969`; do **not** extend it to later v8.8.7 commits. Re-query open PRs before integration and read `_AGENT_CONTEXT/RECONCILIATION_2026-09-29.md` for the live reconciliation rules and evidence limitations.


**Newest final support reconciliation:** canonical base `317ba6c86d54012a65a41772109a72566d29c0a9` already contains v8.8.3 archive-streaming cleanup and hosted evidence. v8.8.4 squash-integrates the reviewed support harvest: hardened save-snapshot retention, remote-preview egress hardening, and unique legacy-hardlink/live-writer/cross-session ownership audits preserved as LR-012 through LR-014. Exact local source `b48c1ff865ab41841d8c7eb931fca19f371f960e` passed Verify-Release **25/25**, functions **748/748** / **7,921** call sites / **0** uncovered, Core **79/79**, Automation **31/31**, Integration **199/199**, self-test **11/11**, and Build-Release/ReadyToRun/helper publish; updater build **326**, ZIP SHA-256 `0F9B9577190037F29B500D2A709356A5F11E1CABA9770343FAA89F160AE6B154`. Hosted exact-main verification remains pending. Read `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-28.md` and `_AGENT_CONTEXT/EVIDENCE/v8.8.4-local-windows-closure.md` first.

**Newest archive-streaming integration status:** canonical `main` now includes PR #57 at merge commit `a8b581176aac0e6bcf09c049285ed40f4b2b392c`, implementing streamed archive extraction cancellation plus actual-output budgeting and regressions. PR #57 records a fresh full local Windows verification on integration head `fd8b48fc92e6f5e64591fd1938b7ccce5ac94083`: verifier 25/25, functions 728/728, 7,772 explicit call sites / 0 uncovered, Core 79/79, Automation 24/24, Integration 177/177, self-test 11/11, strict builds, ReadyToRun/updater-helper packaging, and Build-Release PASS; artifact SHA-256 `AC3571853650CFA91243199B23A44007488F9244780FCBD18A7A38552B652734`. At the 2026-09-28 support checkpoint, exact merge commit `a8b581...` had no hosted workflow/status record, so do not promote last-closed exact verification on that basis. Read `_AGENT_CONTEXT/ARCHIVE_STREAMING_INTEGRATION_PROVENANCE_AUDIT_2026-09-28.md`. The automatic-updater boundary remains separately active.

**Latest integrated product verification:** recursive-source reparse containment plus archive extraction physical-root containment are CLOSED. Hosted Windows Release Gate `36392282315` passed exact production integration commit `d66bff290f197236ec43c9b37d2b015ab2ee5fe8`; evidence/cache persistence commit `a94066004660e4d542f5d4a528c7e5e22bdea9cb`; hosted release SHA-256 `A264C5108DDEA0E3301BFF0E33D7A3E7DA3A92A566739C865AE28B8111BFAFAB`. A separate local Windows run on the same production code plus documentation passed verifier 25/25, functions 615/615, call sites 6532/0 uncovered, Core 79/79, Automation 24/24, Integration 96/96, self-test 11/11, strict analyzers, and ReadyToRun publish; local ZIP SHA-256 `359B12050437AEF0EE9695FEFCE4B8424475413EF54299B2DAC345F5C4E32B21`. Read `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-28.md` before opening another production boundary.

GitHub repository `fengie/mhw-mods` on `main` is now the canonical development
state. The project originally advanced from the user-supplied
`v8.7.0-UniversalGameSupport` archive to **v8.8.0 Function Verification**.
Source ZIPs are retained as reproducible handoff/release exports, not as the primary
source of truth.

## User intent

The user wants a robust manual mod manager with strong failure diagnostics and incremental verification. Their explicit v8.8 request was:

- error-check function execution/calls as comprehensively as is safe without changing product semantics;
- keep a boolean known-good state for functions already confirmed;
- if a function is unchanged and already known-good, do not force it through the changed/new-function gate again;
- future repository revisions, and any exported source ZIPs, must preserve enough context that the next coding agent can continue without reconstructing project history from chat.

## Read order

0. `_AGENT_TRAINING/README.md` — company-level, project-agnostic engineering doctrine and living knowledge-maintenance rules.
1. `_AGENT_CONTEXT/CURRENT_REVISION.json` — machine-readable current status and the exact source commit verification applies to.
2. `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` — permanent Core Rules and recursive continuity constitution.
3. `_AGENT_CONTEXT/LEARNED_RULES.md` — active incident-driven rules; preserve append-only history.
4. `_AGENT_CONTEXT/PROJECT_PLAN.md` — canonical feature priorities, ownership, dependencies, progress checkboxes, and next actions.
5. `_AGENT_CONTEXT/CURRENT_STATE.md`
6. `_AGENT_CONTEXT/ARCHITECTURE.md`
7. `docs/FUNCTION-VERIFICATION.md`
8. `_AGENT_CONTEXT/DECISIONS.md`
9. `_AGENT_CONTEXT/KNOWN_ISSUES.md`
10. `_AGENT_CONTEXT/SOURCE_MAP.md`
11. `_AGENT_CONTEXT/VERIFICATION.md`
12. `_AGENT_CONTEXT/RESEARCH_FINDINGS.md`
13. `_AGENT_CONTEXT/NEXT_STEPS.md`
## Integrated specialized support audits

These documents preserve durable research and implementation history. **Read each document's newest status header**: recursive-source reparse containment and archive physical-root containment are now implemented; other audit findings may remain future work.

- `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-27.md` — historical branch inventory, integration decisions, overlaps and skips.
- `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-28.md` — current recursive/archive support integration, dispositions, combined local verification, and residuals.
- `_AGENT_CONTEXT/RECURSIVE_SOURCE_REPARSE_CANDIDATE_ADVERSARIAL_REVIEW.md` — independent review that found and drove the safe-tree ordering and source-root follow-up fixes.
- `_AGENT_CONTEXT/HEAVY_STRESS_TESTING_SAFETY_REPORT_2026-09-28.md` — recursive traversal stress evidence.
- `_AGENT_CONTEXT/HEAVY_STRESS_ARCHIVE_SAFETY_REPORT_2026-09-28.md` — archive trusted-root/fail-before-mutation stress evidence.
- `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md` — specialized SQLite atomicity findings already canonical before this integration.
- `_AGENT_CONTEXT/GAME_PROFILE_ID_PATH_CONTAINMENT_AUDIT_2026-09-28.md` — documentation-only audit of malformed persisted game-profile IDs escaping manager-owned workspace/state roots; repair remains regression-first.
- `_AGENT_CONTEXT/LAUNCH_OBSERVATION_ATOMICITY_AUDIT_2026-09-28.md` — specialized launch-history/trust all-or-nothing, cancellation, idempotency, and diagnosis-evidence audit.
- `_AGENT_CONTEXT/PROFILE_SAVE_TRANSACTION_ATOMICITY_CHECKPOINT_2026-09-28.md` — Windows fault-injection proof that existing-profile save replacement rolls back profile metadata and membership atomically when the replacement insert fails.
- `_AGENT_CONTEXT/CRASH_BISECTOR_CONTROL_PREFLIGHT_CHECKPOINT_2026-09-28.md` — v8.8.2 control/full-suspect preflight implementation and branch verification evidence; residual diagnosis-evidence risks remain separate.
- `_AGENT_CONTEXT/DUPLICATE_CLEANUP_NORMAL_FAILURE_RECOVERY_2026-09-28.md` — v8.8.2 ordinary delete-failure compensation checkpoint; abrupt process-death recovery remains open.
- `_AGENT_CONTEXT/MAINWINDOW_RESPONSIBILITY_AUDIT.md` — canonical ownership audit plus independent support re-audit.
- `_AGENT_CONTEXT/ASYNC_LIFETIME_CANCELLATION_AUDIT.md` — background-task, staging, close/cancel, and mutation-coordination risks.
- `_AGENT_CONTEXT/TEST_GAP_AND_PERFORMANCE_AUDIT.md` — broad failure-mode and scale coverage gaps.
- `_AGENT_CONTEXT/WINDOWS_FILESYSTEM_SAFETY_AUDIT.md` — reparse containment, CAS integrity, and native replacement semantics.
- `_AGENT_CONTEXT/CAS_FILESYSTEM_IDENTITY_REPARSE_AUDIT.md` — current-source CAS root/hash-leaf physical identity, reparse policy, and legacy hardlink-alias findings after content-integrity hardening.
- `_AGENT_CONTEXT/CAS_DIGEST_NAMESPACE_VALIDATION_AUDIT.md` — SHA-256 identifier/path namespace validation, malformed legacy/persisted digest handling, and regression-first guidance.
- `_AGENT_CONTEXT/RECURSIVE_SOURCE_REPARSE_CONTAINMENT_AUDIT.md` — ModScanner, unmanaged-adoption, and Smart Inbox recursive reparse traversal findings and test contract.
- `_AGENT_CONTEXT/ARCHIVE_EXTRACTION_PHYSICAL_ROOT_AUDIT.md` — runtime-reproduced archive/import destination-root ancestor-junction escape and test-first closure guidance.
- `_AGENT_CONTEXT/LEGACY_MIGRATION_RECOVERY_AUDIT.md` — migration retry/ownership/cleanup/fidelity risks.
- `_AGENT_CONTEXT/VERIFICATION_INFRASTRUCTURE_AUDIT.md` — verifier, stage-cache, CI and supply-chain trust gaps.
- `_AGENT_CONTEXT/CONTINUITY_VALIDATOR_ADVERSARIAL_HARDENING_2026-09-28.md` — implemented adversarial handoff-validator hardening for comments, negation, authorization contradiction, and read-order deception; exact hosted gate still required before closure.
- `_AGENT_CONTEXT/DIAGNOSTICS_PRIVACY_AND_SECRET_HANDLING_AUDIT.md` — share-boundary sanitization and credential handling.
- `_AGENT_CONTEXT/SUPPORT_BUNDLE_SHARE_SANITIZATION_2026-09-28.md` — isolated LR-006 implementation checkpoint for sanitized recent-log export, privacy notice, and generated-bundle canary regression; broader diagnostics privacy remains open.
- `_AGENT_CONTEXT/REMOTE_PREVIEW_NETWORK_TRUST_AUDIT.md` — remote-fetch destination/redirect/HTTP/response validation.
- `_AGENT_CONTEXT/STATE_BACKUP_PORTABILITY_RECOVERY_AUDIT.md` — full-state backup, CAS/DB consistency and portability.
- `_AGENT_CONTEXT/MULTISOURCE_MOD_DISCOVERY_BULK_FILL_AUDIT.md` — source-neutral acquisition/bulk-fill design.
- `_AGENT_CONTEXT/MHW_SEMANTIC_COVERAGE_BULK_FILL_AUDIT.md` — MHW physical-slot coverage and marginal selection semantics.
- `_AGENT_CONTEXT/LAUNCH_HEALTH_REVALIDATION_AUDIT.md` — game-build freshness, runtime revalidation semantics, launch gating, and duplicate-launch safety.
- `_AGENT_CONTEXT/MOD_LIFECYCLE_REFERENTIAL_INTEGRITY_AUDIT.md` — mod retirement, stale live references, same-path identity reuse, and delete/re-import integrity.
- `_AGENT_CONTEXT/CRASH_BISECTOR_DIAGNOSIS_EVIDENCE_AUDIT.md` — baseline/provenance/reproducibility requirements before persistent automated culprit confirmation.
- `_AGENT_CONTEXT/IMPORT_PUBLICATION_CATALOG_VISIBILITY_AUDIT.md` — catalog-invisible staging and commit-on-success publication for archive/Smart Inbox imports.
- `_AGENT_CONTEXT/ARCHIVE_EXTRACTION_RESOURCE_CANCELLATION_AUDIT.md` — runtime-reproduced single-entry cancellation failure plus archive byte/disk-budget hardening guidance.
- `_AGENT_CONTEXT/ARCHIVE_STREAMING_INTEGRATION_PROVENANCE_AUDIT_2026-09-28.md` — post-PR-#57 source/verification provenance repair: implemented on main, integration-head local release evidence, exact-main hosted evidence still distinct.

## Critical rule

Do **not** overwrite `.verification/function-status.json` just because source parses. Promotion means the required build/test/self-test pipeline passed. The scripts deliberately preserve the last known-good cache after failures.

## Baseline

The original supplied baseline was `MHW-Manual-Mod-Manager-v8.7.0-UniversalGameSupport.zip`. Its production C# source is preserved read-only at `.verification/trusted-v8.7.0-src.zip`, with matching SHA-256 entries in `.verification/trusted-v8.7.0-files.json`. The immediate parent of the current repair lineage is the already-created **v8.8.0 FunctionVerification** revision, not v8.7 directly.

## Continuity is part of definition of done

Every future repository change must keep the relevant `_AGENT_CONTEXT/` state current, preserve `AGENTS.md`, `_AGENT_CONTEXT/handoff-manifest.json`, `CONTINUITY_PROTOCOL.md`, and append-only `LEARNED_RULES.md`, run `scripts/testing/Test-AgentHandoff.ps1` and the recursive-continuity negative fixtures through the normal verification harness, and explicitly require the next agent to preserve and recursively pass the same obligation to its successor. Commit handoff/context changes with the code they describe. If a source ZIP is exported, prefer `Build Source Handoff.bat` for packaging. The next agent should not need old chat history. **Do not break the chain.**
