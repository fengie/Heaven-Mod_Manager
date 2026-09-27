# NEXT AGENT — START HERE

Read `_AGENT_CONTEXT/README_FIRST.md` and `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` **before modifying this project**.

## Current repair revision

Read `_AGENT_CONTEXT/AUDIT-2026-09-27.md` and `REPAIR-NOTES.md` next. They supersede
older statements that no SDK/PowerShell were available. The repaired source builds
strictly with SDK 10.0.401 and passes 158 tests plus 11 self-test checks on Linux.
Windows UI/locking/publish validation is still outstanding. Preserve the exact
579 checked function fingerprints and six reusable prior Windows stage checks;
the 23 changed/new functions are deliberately not release-promoted.

## Mandatory continuity requirement

You are responsible for two deliverables:

1. the requested project work; and
2. a durable handoff of everything materially learned while doing that work.

Update `_AGENT_CONTEXT/` as you investigate and modify the project. Preserve architecture discoveries, execution/data flows, invariants, decisions and rationale, rejected approaches, regressions/root causes, verification evidence, uncertainties, changed files, debugging knowledge, and next steps.

Before producing the next source zip:

- run `scripts/Test-AgentHandoff.ps1`;
- preserve `_AGENT_CONTEXT/handoff-manifest.json` and `CONTINUITY_PROTOCOL.md`;
- use `Build Source Handoff.bat` / `scripts/Build-Source-Handoff.ps1` when possible;
- explicitly tell the next agent to repeat this continuity practice.

The next agent must be able to continue without access to previous chat history.

**Do not break the chain.**

## Latest Windows evidence

Read `_AGENT_CONTEXT/EVIDENCE/v8.8.0-first-windows-verification.log` before touching verification code. Some checks are already proven and persisted in `.verification/stage-status.json`; do not delete/re-run them casually. Cache reuse is valid only for exact input fingerprints. Functions are similarly represented as exact-fingerprint booleans in `.verification/function-status.json`. Preserve and extend both mechanisms for the next agent.

## Latest Windows verification evidence

Read `_AGENT_CONTEXT/EVIDENCE/v8.8.0-second-windows-verification.log` as the newest authoritative run. It reached **24 PASS / 1 FAIL**: every compile/analyzer/test/self-test stage passed, and the only failing stage was the function fingerprint scan because four changed functions lacked `MasterDebugLog.BeginMethod()` entry scopes. This packaged revision adds those four scopes only. Because two production files changed to add those scopes, checks whose fingerprints include `MhwModManager.Filesystem` or `MhwModManager.App` must rerun; do not falsely carry their prior green state across the new fingerprints. Exact unaffected cached checks remain checked.
