# NEXT AGENT — START HERE

Read `AGENTS.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, `_AGENT_CONTEXT/README_FIRST.md`, and `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` **before modifying this project**.

## Canonical repository state

GitHub repository `fengie/mhw-mods` on `main` is the canonical development state. Inspect recent commits/diffs relevant to the task before editing. Source ZIPs remain useful release/export artifacts, but they are not the primary source of truth.

## Current revision

v8.8.0 has a closed hosted Windows baseline: commit
`5f6789af499fcc1afe6cb5d38244927bb02335fb`, GitHub Actions run
`36321128433`, **25/25** verification stages passed, all 602 exact production
function fingerprints promoted, and the win-x64 release publish passed. Read
`_AGENT_CONTEXT/EVIDENCE/v8.8.0-hosted-windows-closure.log`.

A post-v8.8 architecture / Explain Why candidate is now being developed. Read
`_AGENT_CONTEXT/CURRENT_REVISION.json` and `CURRENT_STATE.md` for the exact
candidate SHA and verification status. Do **not** transfer the old green
fingerprints to changed source; the candidate must pass a fresh Windows gate.

## Mandatory continuity requirement

You are responsible for two deliverables:

1. the requested project work; and
2. a durable handoff of everything materially learned while doing that work.

Update `_AGENT_CONTEXT/` as you investigate and modify the project. Preserve architecture discoveries, execution/data flows, invariants, decisions and rationale, rejected approaches, regressions/root causes, verification evidence, uncertainties, changed files, debugging knowledge, and next steps.

Before finishing repository work:

- update `_AGENT_CONTEXT/` and `_AGENT_CONTEXT/CURRENT_REVISION.json` with material discoveries and the verification state that applies to the changed source;
- run `scripts/Test-AgentHandoff.ps1`;
- preserve `_AGENT_CONTEXT/handoff-manifest.json`, `CONTINUITY_PROTOCOL.md`, and `AGENTS.md`;
- commit the context/handoff updates with the code they describe;
- if producing a source ZIP, use `Build Source Handoff.bat` / `scripts/Build-Source-Handoff.ps1` when possible;
- explicitly tell the next agent to repeat this continuity practice.

The next agent must be able to continue without access to previous chat history.

**Do not break the chain.**

## Verification evidence

The canonical closed v8.8 evidence is
`_AGENT_CONTEXT/EVIDENCE/v8.8.0-hosted-windows-closure.log`. Earlier first and
second Windows logs remain useful historical evidence, but their 13/12 and 24/1
states are superseded by the final 25/25 hosted closure. For any later source
change, reuse cached stages/functions only when the verifier confirms exact input
fingerprints; never copy a green status manually.
