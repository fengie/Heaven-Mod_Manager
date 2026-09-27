# NEXT AGENT — START HERE

## Active candidate: Activity page view model

Production source commit `b0881ee5780159b49c26b3e79e3a0b26b1b39a6b` extracts the Activity read/state seam
into `ActivityPageViewModel` while deliberately preserving
`ActivityRows` and `RefreshActivityCommand` bindings. It is not green until
a fresh Windows Release Gate passes. Do not start a second page extraction
before that closure.

## Current checkpoint: architecture / Explain Why is CLOSED

Hosted Windows Release Gate `36325133722` verified exact commit
`9717a22d3338f77e63cd409a80d2ec5fc3c924f2`; the last production-source edit is
`098d617bcb3dcdd044e3fdb8319ba506c97082af`. Verification is **25/25**, function fingerprints are
**607/607**, Core **79/79**, Automation **20/20**, Integration/fault injection
**62/62**, self-test **11/11**, and the ReadyToRun win-x64 publish passed.
Release SHA-256: `DF87A48716596ABFFF545DD6C73BAAE02954167424908850D943BFFA3833A2D6`.

The next source change must start a new verification boundary. The next intended
slice is one page-view-model extraction at a time, beginning with Activity.
## Immediate architecture-candidate verification update

Hosted run `36324750213` passed the exact repository verifier but the release
win-x64 analyzer gate found one CA1826 diagnostic in overlap path selection.
Production source commit `098d617bcb3dcdd044e3fdb8319ba506c97082af` contains the minimal analyzer-safe
fix. Treat it as unverified until a fresh full Windows Release Gate passes and
persists exact evidence.

Read `AGENTS.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, `_AGENT_CONTEXT/README_FIRST.md`, and `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` **before modifying this project**.

## Canonical repository state

GitHub repository `fengie/mhw-mods` on `main` is the canonical development state. Inspect recent commits/diffs relevant to the task before editing. Source ZIPs remain useful release/export artifacts, but they are not the primary source of truth.

## Current revision

v8.8.0 has a closed hosted Windows baseline: commit
`5f6789af499fcc1afe6cb5d38244927bb02335fb`, GitHub Actions run
`36321128433`, **25/25** verification stages passed, all 602 exact production
function fingerprints promoted, and the win-x64 release publish passed. Read
`_AGENT_CONTEXT/EVIDENCE/v8.8.0-hosted-windows-closure.log`.

The post-v8.8 architecture / Explain Why milestone is closed by run `36325133722` at exact commit `9717a22d3338f77e63cd409a80d2ec5fc3c924f2`. Any subsequent production-source change starts a new verification boundary and must pass a fresh Windows gate.

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
