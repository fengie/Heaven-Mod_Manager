**Latest research handoff:** RESEARCH_NEXT_AGENT_2026-09-27.md consolidates priorities, source-backed findings and remaining acceptance.

**New research:** Read RESEARCH_FOMOD_2026-09-27.md for source-audited Inbox/FOMOD follow-ups; no production fixes are claimed.

**Complete session knowledge:** Read `SESSION_HANDOFF_2026-09-27.md` after CHECKPOINT.md. It records the user’s exact continuity instructions, implementation map, limits, tested commits, runtime/publishing details, and native acceptance checklist. Future agents must maintain and propagate this knowledge.

**Checkpoint cadence:** Read `CHECKPOINT.md` first for the active plan, remote branch/PR, and exact next action. The user requires meaningful progress to be pushed throughout the task.

**Workflow expansion:** Read `WORKFLOW_IMPLEMENTATION.md` and `../docs/WORKFLOWS.md` for the new feature branch. Its evidence and Windows UI limitations supersede the older repair-only status below.

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
2. `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` — mandatory handoff rule; preserve it for the next agent.
3. `_AGENT_CONTEXT/CURRENT_STATE.md`
4. `_AGENT_CONTEXT/ARCHITECTURE.md`
5. `docs/FUNCTION-VERIFICATION.md`
6. `_AGENT_CONTEXT/DECISIONS.md`
7. `_AGENT_CONTEXT/KNOWN_ISSUES.md`
8. `_AGENT_CONTEXT/SOURCE_MAP.md`
9. `_AGENT_CONTEXT/VERIFICATION.md`
10. `_AGENT_CONTEXT/RESEARCH_FINDINGS.md`
11. `_AGENT_CONTEXT/NEXT_STEPS.md`

## Critical rule

Do **not** overwrite `.verification/function-status.json` just because source parses. Promotion means the required build/test/self-test pipeline passed. The scripts deliberately preserve the last known-good cache after failures.

## Baseline

The original supplied baseline was `MHW-Manual-Mod-Manager-v8.7.0-UniversalGameSupport.zip`. Its production C# source is preserved read-only at `.verification/trusted-v8.7.0-src.zip`, with matching SHA-256 entries in `.verification/trusted-v8.7.0-files.json`. The immediate parent of the current repair lineage is the already-created **v8.8.0 FunctionVerification** revision, not v8.7 directly.

## Continuity is part of definition of done

Every future repository change must keep the relevant `_AGENT_CONTEXT/` state current, preserve `AGENTS.md`, `_AGENT_CONTEXT/handoff-manifest.json` and `CONTINUITY_PROTOCOL.md`, run `scripts/Test-AgentHandoff.ps1`, and explicitly tell the next agent to repeat the practice. Commit handoff/context changes with the code they describe. If a source ZIP is exported, prefer `Build Source Handoff.bat` for packaging. The next agent should not need old chat history. **Do not break the chain.**
