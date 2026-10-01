# v8.8.64 rich Browse Mods — current handoff

Canonical MHW product repository: `fengie/mhw-mods`
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`
Active PR: none for the integrated v8.8.64 tranche
Verified source/head: `3ced8b41041909d91b06902631ef36085bfd7489`
Merge commit: `a39064643f97aa5a0df80bf7c917d260b3d7e7a1`
Current branch: `main`

## Integrated change

Issues #556/#557 and the first tranche of #558 are now canonical:

- the header game selector uses an explicit `DisplayName` item template, preventing raw `GameProfile` record text;
- Browse Mods rows show safe HTTPS artwork plus summary, author, category, download count, provider, version, updated time, and cache state;
- the selected detail pane shows artwork and version/update context;
- WPF result rows use recycling virtualization;
- refresh capacity is 100 items/provider and cached visible results are 1000;
- focused source/XAML regressions pin these invariants.

## Verification state

Exact head `3ced8b41041909d91b06902631ef36085bfd7489` passed Workflow Feature PR Gate 36895834301, MHW Product Security Gate 36895834289, and Heaven Toolbox Ownership Gate 36895834269, then merged through PR #561 as `a39064643f97aa5a0df80bf7c917d260b3d7e7a1`.

The signed Heaven Bridge rejected unsigned ChatGPT dispatch with `AUTH_REQUIRED`; do not weaken HMAC. GitHub Actions provided the authoritative verification for this lane.

## Unresolved risks and remaining work

- #558 remains open for deeper provider-aware pagination/search and catalog breadth beyond the first capacity bump.
- #559 remains open for sorting, filters, provider health, richer loading/empty/partial-failure states, and broader discovery UX.
- Installed Windows/WPF confirmation is still needed for the selector and live remote-thumbnail behavior.
- Issue #281 still owns real remaining provider contracts; do not fake unsupported Nexus search or Steam mappings.
- Existing external signing/ruleset/runtime-evidence gaps remain unchanged.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and `_AGENT_CONTEXT/LEARNED_RULES.md` in full, refresh live ownership/state before mutation, preserve the continuity constitution and active learned rules, and recursively propagate this obligation. **Do not break the chain.**

The successor must propagate this continuity obligation to the next agent.
