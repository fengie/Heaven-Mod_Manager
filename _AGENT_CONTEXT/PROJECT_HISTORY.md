# Relevant project history

## v8.6.x
The manager evolved from an MHW-focused manual mod manager into a heavily hardened transactional tool: SQLite state, CAS, deployment journaling/recovery, conflict/family inference, diagnostics, automation, visual metadata, Nexus integration, and increasingly strict verification. v8.6.27 added visual-source fallback behavior.

## v8.7.0 — Universal Game Support
The user supplied this exact version as the original baseline for the v8.8 work. It added real `GameProfile` / `GameProfileRegistry` architecture, per-game state/workspace behavior, generic target/scanner semantics, broader store discovery and game-aware services while retaining enhanced MHW semantics. The diff was focused rather than a rewrite.

## v8.8.0 — Function Verification
This revision adds incremental per-function/body verification and runtime nested-call error observation. The principal objective is to let unchanged known-good code remain trusted while forcing changed/new executable bodies back through a fail-closed verification path.

The user explicitly requested durable cross-agent continuity. The project originally carried that context through downloadable source ZIPs; GitHub `fengie/mhw-mods` on `main` is now the canonical state, with source ZIPs retained as optional exports. `_AGENT_CONTEXT/` remains the durable source/architecture/decision/verification memory.

### v8.8.0 handoff-hardening re-audit
After the first v8.8 package, the source was reread and the verification/handoff design was tightened without changing the version number: generated build C# exclusion, trusted snapshot self-validation, explicit-interface ID collision protection, explicit call-site coverage reporting, lightweight first-chance observation, and mechanically enforced agent-context propagation/source packaging were added.

### v8.8.0 checked-pass persistence follow-up
The user's first real Windows verifier run exposed compile/analyzer regressions in FunctionVerifier, the game-profile editor, genericized service call sites, and game discovery helpers. Those diagnostics were fixed. Verification was also made granular: unchanged trusted functions are persisted as true/false checklist entries during scan, and independent strict-build/test stages persist exact-input checks in `stage-status.json`. The first run's evidence is kept in `_AGENT_CONTEXT/EVIDENCE/`.

### v8.8.0 verification-closure follow-up
- Second Windows verifier run reached 24 PASS / 1 FAIL.
- Whole solution compiled strictly with 0 warnings / 0 errors; Automation tests 18/18, Integration/fault injection 43/43, and full automation self-test passed.
- Sole remaining failure was four missing function-entry traces, responsible for all 69 uncovered call sites.
- Added `MasterDebugLog.BeginMethod()` to exactly those four functions and preserved only exact-input cache entries that remain valid after the patch.

## v8.8.0 repair audit — 2026-09-27

Read `AUDIT-2026-09-27.md`. Added generic scan/blob recapture fixes, idle watcher optimization, stricter trace/checklist validation, accurate integration-stage inputs, and complete source packaging. SDK 10.0.401 strict solution build and 158 tests plus 11 self-test checks passed on Linux; Windows release confirmation pending.

## Git-first continuity transition — 2026-09-27

The full source tree is now stored in GitHub and future agents should treat repository commits/diffs as the authoritative development history. Added `AGENTS.md` and `_AGENT_CONTEXT/CURRENT_REVISION.json` so a fresh agent can establish instructions, revision lineage, verification applicability, and next action before reading historical material. Existing ZIP packaging remains supported as an export/release mechanism.
