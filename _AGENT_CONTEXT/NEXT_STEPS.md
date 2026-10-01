# v8.8.60 MHW product recovery — ordered next actions

1. Run the required exact-head gates for `fix/recovery-004-runtime-hardening-v8.8.60`, including strict build/analyzers, IntegrationTests, FunctionVerifier, product security, Toolbox ownership, and continuity verification.
2. Refresh canonical `main`, open PRs/branches, and mutable ownership immediately before integration; integrate RECOVERY-004 only if the exact candidate remains green and non-conflicting.
3. Persist exact commit/run evidence without recursively bumping the patch, mark RECOVERY-004 DONE, and retire its stale recovery branch only after semantic integration is proven.
4. Continue RECOVERY-002 catalog recovery on its existing owner lane; do not create a duplicate catalog implementation.
5. Continue RECOVERY-007 representative installed-game/runtime proof; integrated discovery source is not DONE until observed against representative Windows installs.
6. Complete RECOVERY-005 installed Windows/WPF visual/interaction acceptance for the already-integrated dark ComboBox chrome.
7. Keep reusable Agent Control/Heaven/plugin/toolbox work in `fengie/heaven-toolbox`.

## Current verification boundary

Focused Windows evidence applies to source checkpoint `5f11ce59712808ce259dbf72922cc011fb4319c1`: Release build 0 warnings / 0 errors, IntegrationTests 268/268, and FunctionVerifier 1469 functions with 0 trace gaps, 0 uncovered call sites, and 0 parse errors. Version/continuity metadata was updated afterward, so the final 8.8.60 branch still requires its normal exact-head gates before integration.

The attempted local Codex audit on `heaven` was quota-blocked without edits. Deterministic Heaven Bridge execution successfully performed the Windows build/test verification above; do not misreport the failed Codex dispatch as a source or runtime failure.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve the permanent MHW continuity constitution and active learned rules, and recursively propagate the same obligation. **Do not break the chain.**
