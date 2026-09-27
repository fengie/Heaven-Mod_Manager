# READ THIS FIRST — MHW Manual Mod Manager v8.8.0

**Latest revision:** the 2026-09-27 repair audit is in `AUDIT-2026-09-27.md`.
Read it after the continuity protocol. Historical toolchain limitations below do
not describe the latest verified source state; Linux build/tests have real evidence,
while Windows release confirmation still must run.

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
13. `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md` — specialized write-side transaction/failure audit.
14. `_AGENT_CONTEXT/LEGACY_MIGRATION_RECOVERY_AUDIT.md` — v7→v8 retry, ownership, semantic-fidelity, cleanup, backup and fault-matrix audit.

## Critical rule

Do **not** overwrite `.verification/function-status.json` just because source parses. Promotion means the required build/test/self-test pipeline passed. The scripts deliberately preserve the last known-good cache after failures.

## Baseline

The original supplied baseline was `MHW-Manual-Mod-Manager-v8.7.0-UniversalGameSupport.zip`. Its production C# source is preserved read-only at `.verification/trusted-v8.7.0-src.zip`, with matching SHA-256 entries in `.verification/trusted-v8.7.0-files.json`. The immediate parent of the current repair lineage is the already-created **v8.8.0 FunctionVerification** revision, not v8.7 directly.

## Continuity is part of definition of done

Every future repository change must keep the relevant `_AGENT_CONTEXT/` state current, preserve `AGENTS.md`, `_AGENT_CONTEXT/handoff-manifest.json`, `CONTINUITY_PROTOCOL.md`, and append-only `LEARNED_RULES.md`, run `scripts/Test-AgentHandoff.ps1` and the recursive-continuity negative fixtures through the normal verification harness, and explicitly require the next agent to preserve and recursively pass the same obligation to its successor. Commit handoff/context changes with the code they describe. If a source ZIP is exported, prefer `Build Source Handoff.bat` for packaging. The next agent should not need old chat history. **Do not break the chain.**
