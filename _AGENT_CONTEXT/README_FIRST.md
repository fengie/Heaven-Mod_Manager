# READ THIS FIRST — MHW Manual Mod Manager v8.8.0

**Latest revision:** the 2026-09-27 repair audit of the attached v8.8.0 ZIP is in
`AUDIT-2026-09-27.md`. Read it after the continuity protocol. Historical toolchain
limitations below do not describe this latest session; Linux build/tests now have
real evidence, while Windows release confirmation still must run.

This archive is a source handoff, not just a release snapshot. It was created from the user's supplied `v8.7.0-UniversalGameSupport` archive and advances it to **v8.8.0 Function Verification**.

## User intent

The user wants a robust manual mod manager with strong failure diagnostics and incremental verification. Their explicit v8.8 request was:

- error-check function execution/calls as comprehensively as is safe without changing product semantics;
- keep a boolean known-good state for functions already confirmed;
- if a function is unchanged and already known-good, do not force it through the changed/new-function gate again;
- future downloadable source zips must preserve enough context that the next coding agent can continue without reconstructing project history from chat.

## Read order

1. `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` — mandatory handoff rule; preserve it for the next agent.
2. `_AGENT_CONTEXT/CURRENT_STATE.md`
3. `_AGENT_CONTEXT/ARCHITECTURE.md`
4. `docs/FUNCTION-VERIFICATION.md`
5. `_AGENT_CONTEXT/DECISIONS.md`
6. `_AGENT_CONTEXT/KNOWN_ISSUES.md`
7. `_AGENT_CONTEXT/SOURCE_MAP.md`
8. `_AGENT_CONTEXT/VERIFICATION.md`
9. `_AGENT_CONTEXT/RESEARCH_FINDINGS.md`
10. `_AGENT_CONTEXT/NEXT_STEPS.md`

## Critical rule

Do **not** overwrite `.verification/function-status.json` just because source parses. Promotion means the required build/test/self-test pipeline passed. The scripts deliberately preserve the last known-good cache after failures.

## Baseline

The exact previous source supplied by the user was `MHW-Manual-Mod-Manager-v8.7.0-UniversalGameSupport.zip`. Its production C# source is preserved read-only at `.verification/trusted-v8.7.0-src.zip`, with matching SHA-256 entries in `.verification/trusted-v8.7.0-files.json`.

## Continuity is part of definition of done

Every future source handoff must update `_AGENT_CONTEXT/`, preserve `_AGENT_CONTEXT/handoff-manifest.json` and `CONTINUITY_PROTOCOL.md`, run `scripts/Test-AgentHandoff.ps1`, and explicitly tell the next agent to repeat the practice. Prefer `Build Source Handoff.bat` for packaging. The next agent should not need old chat history. **Do not break the chain.**
