# READ THIS FIRST — MHW Manual Mod Manager v8.8.0

**Latest closed product verification:** PlannerSnapshotRepository at `efe58f38c4780d40200bcf2b7bbb5914ecd8ebc3`, hosted Windows Release Gate `36336190920`. Parallel support-agent research has since been integrated as documentation only; read `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-27.md` before choosing a new production boundary.

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

1. `_AGENT_CONTEXT/CURRENT_REVISION.json` — machine-readable current status and the exact source commit verification applies to.
2. `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` — permanent Core Rules and recursive continuity constitution.
3. `_AGENT_CONTEXT/LEARNED_RULES.md` — active incident-driven rules; preserve append-only history.
4. `_AGENT_CONTEXT/CURRENT_STATE.md`
5. `_AGENT_CONTEXT/ARCHITECTURE.md`
6. `docs/FUNCTION-VERIFICATION.md`
7. `_AGENT_CONTEXT/DECISIONS.md`
8. `_AGENT_CONTEXT/KNOWN_ISSUES.md`
9. `_AGENT_CONTEXT/SOURCE_MAP.md`
10. `_AGENT_CONTEXT/VERIFICATION.md`
11. `_AGENT_CONTEXT/RESEARCH_FINDINGS.md`
12. `_AGENT_CONTEXT/NEXT_STEPS.md`

## Integrated specialized support audits

These documents are durable research, **not claims that their proposed fixes are implemented**. Read the ones intersecting your task:

- `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-27.md` — branch inventory, integration decisions, overlaps and skips.
- `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md` — specialized SQLite atomicity findings already canonical before this integration.
- `_AGENT_CONTEXT/MAINWINDOW_RESPONSIBILITY_AUDIT.md` — canonical ownership audit plus independent support re-audit.
- `_AGENT_CONTEXT/ASYNC_LIFETIME_CANCELLATION_AUDIT.md` — background-task, staging, close/cancel, and mutation-coordination risks.
- `_AGENT_CONTEXT/TEST_GAP_AND_PERFORMANCE_AUDIT.md` — broad failure-mode and scale coverage gaps.
- `_AGENT_CONTEXT/WINDOWS_FILESYSTEM_SAFETY_AUDIT.md` — reparse containment, CAS integrity, and native replacement semantics.
- `_AGENT_CONTEXT/LEGACY_MIGRATION_RECOVERY_AUDIT.md` — migration retry/ownership/cleanup/fidelity risks.
- `_AGENT_CONTEXT/VERIFICATION_INFRASTRUCTURE_AUDIT.md` — verifier, stage-cache, CI and supply-chain trust gaps.
- `_AGENT_CONTEXT/DIAGNOSTICS_PRIVACY_AND_SECRET_HANDLING_AUDIT.md` — share-boundary sanitization and credential handling.
- `_AGENT_CONTEXT/REMOTE_PREVIEW_NETWORK_TRUST_AUDIT.md` — remote-fetch destination/redirect/HTTP/response validation.
- `_AGENT_CONTEXT/STATE_BACKUP_PORTABILITY_RECOVERY_AUDIT.md` — full-state backup, CAS/DB consistency and portability.
- `_AGENT_CONTEXT/MULTISOURCE_MOD_DISCOVERY_BULK_FILL_AUDIT.md` — source-neutral acquisition/bulk-fill design.
- `_AGENT_CONTEXT/MHW_SEMANTIC_COVERAGE_BULK_FILL_AUDIT.md` — MHW physical-slot coverage and marginal selection semantics.
- `_AGENT_CONTEXT/MOD_LIFECYCLE_REFERENTIAL_INTEGRITY_AUDIT.md` — mod retirement, stale live references, same-path identity reuse, and delete/re-import integrity.

## Critical rule

Do **not** overwrite `.verification/function-status.json` just because source parses. Promotion means the required build/test/self-test pipeline passed. The scripts deliberately preserve the last known-good cache after failures.

## Baseline

The original supplied baseline was `MHW-Manual-Mod-Manager-v8.7.0-UniversalGameSupport.zip`. Its production C# source is preserved read-only at `.verification/trusted-v8.7.0-src.zip`, with matching SHA-256 entries in `.verification/trusted-v8.7.0-files.json`. The immediate parent of the current repair lineage is the already-created **v8.8.0 FunctionVerification** revision, not v8.7 directly.

## Continuity is part of definition of done

Every future repository change must keep the relevant `_AGENT_CONTEXT/` state current, preserve `AGENTS.md`, `_AGENT_CONTEXT/handoff-manifest.json`, `CONTINUITY_PROTOCOL.md`, and append-only `LEARNED_RULES.md`, run `scripts/Test-AgentHandoff.ps1` and the recursive-continuity negative fixtures through the normal verification harness, and explicitly require the next agent to preserve and recursively pass the same obligation to its successor. Commit handoff/context changes with the code they describe. If a source ZIP is exported, prefer `Build Source Handoff.bat` for packaging. The next agent should not need old chat history. **Do not break the chain.**
