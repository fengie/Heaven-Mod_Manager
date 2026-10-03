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

The source-only v8.8.85 implementation head `67a55a581ae2bc78906d3252d44196857741131f` passed MHW Product Security run `37125208406` and Workflow Feature PR Gate run `37125208523`, including the exact repository verifier, builds, analyzers, focused regressions, and patch whitespace, before release metadata was synchronized.

That evidence proves the intended candidate-mode separation but is historical after the v8.8.85 metadata changes. The exact final PR #678 head must pass fresh Product Security and Workflow Feature gates before integration. After merge, canonical `main` remains subject to the Windows Release Gate's full strict metadata/handoff verifier; `-FeatureCandidate` is forbidden there.

The last closed canonical Windows release verification remains v8.8.84 source `2ea6d6dd3851f24a40e562074a816d9bd1e61883`, run `37124460532`, until v8.8.85 obtains its own exact-main closure.

## Remaining independent work

#673 adoption hardening, #668 Advanced Tools accessibility reconciliation, #642 atomic recipe export, #667 orphan snapshot reconciliation, #559/#558 catalog UX/scale, #350/#354 external security prerequisites, and RECOVERY-005/RECOVERY-007 remain independent.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, filesystem containment, and durable-evidence privacy boundary, and recursively propagate the same obligation.
