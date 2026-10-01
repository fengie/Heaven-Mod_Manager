# v8.8.64 rich Browse Mods — current handoff

Canonical MHW product repository: `fengie/mhw-mods`
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`
Active PR: #561
Candidate production/test source: `2b49f4f719dfcddfccd3a215a88595df5c779b5e`
Current branch: `fix/browse-mods-rich-ui-v8.8.64`

## Candidate change

Issues #556/#557 and the first tranche of #558 improve the in-app catalog:

- the header game selector uses an explicit `DisplayName` item template, preventing raw `GameProfile` record text;
- Browse Mods rows show safe HTTPS artwork plus summary, author, category, download count, provider, version, updated time, and cache state;
- the selected detail pane shows artwork and version/update context;
- WPF result rows use recycling virtualization;
- refresh capacity rises from 60 to 100 items/provider and cached visible results from 250 to 1000;
- focused source/XAML regressions pin these invariants.

## Verification state

v8.8.63 is integrated through PR #555 after exact-head Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership gates all passed. v8.8.64 is a **candidate** until the same required gates pass on one final PR #561 head.

The signed Heaven Bridge rejected unsigned ChatGPT dispatch with `AUTH_REQUIRED`; do not weaken HMAC. GitHub Actions is the authoritative verification environment for this lane.

## Unresolved work

- #558 remains open for deeper provider-aware pagination/search and catalog breadth beyond the first capacity bump.
- #559 remains open for sorting, filters, provider health, richer loading/empty/partial-failure states, and broader discovery UX.
- Installed Windows/WPF confirmation is still needed for the selector and live remote-thumbnail behavior.
- Issue #281 still owns real remaining provider contracts; do not fake unsupported Nexus search or Steam mappings.
- Existing external signing/ruleset/runtime-evidence gaps remain unchanged.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`. Refresh live ownership/state before mutation, preserve the continuity constitution and active learned rules, and recursively propagate this obligation. **Do not break the chain.**
