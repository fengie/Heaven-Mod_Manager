# v8.8.62 MHW product recovery — ordered next actions

1. Treat PR #553 / v8.8.62 as integrated and closed. Exact verified source is `9c2976ae96533b3d439610ef7c770a73d0e14fe3`; canonical merge is `3c19270a9dbf37c434aaa0654fdedcc8c28c6d71` with zero file differences from that source.
2. Before any new mutation, refresh current `fengie/heaven-toolbox@main`, MHW `main`, live findings/ownership, open PRs/issues, and branch ancestry. Do not revive a superseded recovery lane.
3. Keep issue #281 limited to real remaining contracts: Steam Workshop only for a game/profile with documented support/capabilities, optional Vortex metadata/import-export/handoff interoperability, and future HTML adapters only with provider-specific compliance manifests and deterministic fixtures.
4. Continue issue #350 independently signed updater metadata only within the checked-in design boundaries. Production closure requires a real external public/private key ceremony and one real signed release; never commit a fake production key.
5. Continue issue #354 source-side publisher/security hardening where source work remains useful. Repository rulesets are externally blocked on the current private-repository/account tier, and Authenticode publisher identity requires external certificate provisioning; do not claim either external control is enabled without evidence.
6. Preserve RECOVERY-007 representative Windows installed-game/runtime proof and RECOVERY-005 installed WPF interaction acceptance as evidence gaps until actually observed.
7. Preserve the older issue #281 recovery branches until their remaining unique commits are explicitly classified as integrated, superseded, or rejected; only then retire them.
8. Keep reusable Agent Control/Heaven/plugin/toolbox work in `fengie/heaven-toolbox`.

## Current verification boundary

v8.8.62 exact head `9c2976ae96533b3d439610ef7c770a73d0e14fe3` passed Workflow Feature `36869271453`, MHW Product Security `36869271833`, and Heaven Toolbox Ownership `36869271597`. PR #553 merged as `3c19270a9dbf37c434aaa0654fdedcc8c28c6d71`; post-merge comparison reports zero file differences. Subsequent `[skip ci]` continuity updates change no production source.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve the permanent MHW continuity constitution and active learned rules, recursively propagate this obligation, and do not replace external security/runtime evidence with assumptions. **Do not break the chain.**
