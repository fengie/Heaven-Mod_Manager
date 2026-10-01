# v8.8.63 MHW product recovery — ordered next actions

1. Finish issue #554 / PR #555 first. Required exact-head gates are Workflow Feature PR Gate, MHW Product Security Gate, and Heaven Toolbox Ownership Gate; merge only when all three are green on one final head.
2. After green verification, refresh `main` and PR ownership/freshness, merge PR #555, verify the merged tree matches the verified PR source, close #554, and record exact run/merge evidence without another patch bump.
3. Keep issue #281 limited to real remaining contracts: supported Steam Workshop mappings/capabilities, optional Vortex interoperability, and future HTML adapters only with provider-specific compliance manifests and deterministic parser fixtures.
4. Continue issue #350 only within the checked-in independent-signing design. Full closure requires an external public/private key ceremony and one real signed release; never commit a fake production key.
5. Continue issue #354 only where real source-side work remains. Do not claim repository rulesets or Authenticode identity without external evidence.
6. Preserve RECOVERY-007 representative Windows installed-game/runtime proof and RECOVERY-005 installed WPF interaction acceptance as evidence gaps until observed.
7. Preserve older issue #281 recovery branches until remaining unique commits are explicitly classified as integrated, superseded, or rejected.
8. Keep reusable Agent Control/Heaven/plugin/toolbox work in `fengie/heaven-toolbox`.

## Current verification boundary

v8.8.63 candidate production source is `76205544ed23422cfb4056dfa88787a162e9a2fd`. Intermediate Workflow Feature runs identified real defects/invariants and were repaired; they are not closure evidence. Final closure requires all required gates green on the same exact PR #555 head.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve the permanent MHW continuity constitution and active learned rules, recursively propagate this obligation, and do not replace external security/runtime evidence with assumptions. **Do not break the chain.**
