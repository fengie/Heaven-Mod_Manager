# v8.8.62 MHW product recovery — ordered next actions

1. Verify PR #553 at its exact final head with the current Windows feature/release, product-security, Toolbox-ownership, and continuity gates; repair any compiler/analyzer/test regression before integration.
2. Refresh canonical `main`, open PRs, and branch ownership immediately before integration; merge only if the exact candidate is green and non-conflicting.
3. After PR #553 lands, mark RECOVERY-002 complete only for the catalog UI/acquisition + CurseForge + crawler tranche actually verified. Keep conditional Steam Workshop and optional Vortex interoperability explicit instead of inventing unsupported mappings.
4. Continue issue #350 signed updater metadata from its checked-in ECDSA P-256 design. Source implementation may proceed, but production closure requires a real external public/private key ceremony and one real signed release; never commit a fake production key.
5. Continue issue #354 source-side publisher/security hardening. Repository rulesets are externally blocked on this private repository/account tier (GitHub returns 403 requiring Pro or public); do not claim that admin control is enabled.
6. Preserve RECOVERY-007 representative Windows installed-game/runtime proof and RECOVERY-005 installed WPF interaction acceptance as evidence gaps until actually observed.
7. Keep reusable Agent Control/Heaven/plugin/toolbox work in `fengie/heaven-toolbox`.

## Current verification boundary

PR #553 is the current v8.8.62 candidate. No exact-head Windows verification is claimed until the final candidate SHA completes the gates. The direct local Heaven/Agent Control execution namespace is not exposed in this ChatGPT runtime, so the repository's self-hosted Windows Actions runner is the deterministic execution fallback.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve the permanent MHW continuity constitution and active learned rules, recursively propagate this obligation, and do not replace external security evidence with assumptions. **Do not break the chain.**
