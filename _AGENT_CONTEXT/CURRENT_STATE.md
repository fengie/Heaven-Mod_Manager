# v8.8.85 feature verification decoupling — canonical state

v8.8.85 closes issue #675 by separating feature-candidate product verification from canonical release-version metadata without weakening the exact-main release/publication boundary.

## Behavior

- Workflow Feature PR Gate invokes `Verify-Release.ps1 -FeatureCandidate` on the exact PR head.
- Feature-candidate mode defers only global release-version surface parity: VERSION/build version, current-version README/CHANGELOG/handoff parity, and current-version closure projection.
- Repository identity, canonical-state shape, Toolbox ownership, continuity propagation, context budgets, verification-cache structure, CI security policy, function verification, strict builds/analyzers, tests, integration/fault injection, and self-test remain enforced.
- The canonical Windows Release Gate continues to invoke the default full verifier with no candidate relaxation.
- CI policy tests fail if candidate mode disappears from the PR gate or appears in the canonical release gate.
- A deterministic negative fixture proves release-version drift is rejected by canonical governance but accepted by feature-candidate governance.

## Verification boundary

Current hosted-Windows closure: v8.8.85 source `0ce34cf74ee15f33c7bf3b8a1a378a3622b7617c` passed run `37126561159` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.85-heaven-windows-closure.log`.

The tested source remains `0ce34cf74ee15f33c7bf3b8a1a378a3622b7617c` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent work

#673 adoption hardening, #668 Advanced Tools accessibility reconciliation, #642 atomic recipe export, #667 orphan snapshot reconciliation, #559/#558 catalog UX/scale, #350/#354 external security prerequisites, and RECOVERY-005/RECOVERY-007 remain independent.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, filesystem containment, and durable-evidence privacy boundary, and recursively propagate the same obligation.
