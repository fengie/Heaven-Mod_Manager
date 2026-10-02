# v8.8.67 provider-aware Browse Mods search — candidate handoff

Canonical MHW product repository: `fengie/mhw-mods`
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`
Current branch: `agent/issue-558-provider-search-v8.8.67-20261002`
Issue: #558
Immediate integrated parent: v8.8.66 PR #563 merge `5459db663f55388e72da97a79cb6e22ff0673048`

## Candidate change

v8.8.67 advances #558 without inventing unsupported provider behavior:

- typing in Browse Mods remains a 180 ms **local cache/FTS filter only**;
- pressing **Search** explicitly queries only configured providers whose `Capabilities` advertise `CatalogProviderCapabilities.Search`;
- returned provider rows still enter through `CatalogSyncService` and the source-aware cache/provenance boundary before the UI reads them;
- provider search failures are isolated and matching cached rows remain usable;
- Nexus Mods and GameBanana are not probed for unsupported full-catalog text search;
- configured CurseForge can use its existing official paged search implementation to discover matches not already cached;
- Browse Mods explains the cached-versus-provider search distinction in tooltips/status text;
- focused integration regression coverage protects capability gating and proves the debounce path remains cache-only.

This tranche does **not** complete #558. Broader provider-aware pagination/browse expansion and deterministic scale/performance coverage remain open.

## Reconciliation with v8.8.66

PR #563 landed while the first #558 candidate was being verified. Its packaged selector UI Automation work is preserved on this branch, including the `ActiveGameDisplayName` automation ID and its regression/E2E contracts.

PR #563 exact head `e47d0a91e966b5bcd86e69825af5d77970fdb500` passed:
- Workflow Feature PR Gate `36966157999`;
- MHW Product Security Gate `36966158024`;
- Heaven Toolbox Ownership Gate `36966158016`.

It merged as `5459db663f55388e72da97a79cb6e22ff0673048`. Publication and packaged installed-client selector acceptance remain separate #556 closure work unless newer canonical evidence records them.

## Superseded #558 verification evidence

The first #558 PR #564 was based on the pre-#563 main and therefore must not be merged after main advanced.

- Head `a81891537cdb767df03e0f0fbf0204743bab07ee` ran Workflow Feature `36966116030`: repository verification produced **25 PASS / 1 FAIL**. The sole failure was handoff-manifest version metadata; builds/analyzers, function verification, 323 Core tests, 79 Automation tests, 274 Integration/fault tests, and full self-test all passed.
- Repaired head `117c17b9dff6535bf939dd8b65ef54892f6592e9` then passed Workflow Feature `36966345299`, Product Security `36966345328`, and Toolbox Ownership `36966345237`.
- Those greens are useful evidence for the semantic patch but are **not merge authorization** for v8.8.67 because canonical main advanced through PR #563 and the product version changed.

The v8.8.67 reconciled head must pass all required gates independently.

## Local delegation evidence

A Heaven worker was reached successfully through the signed bridge, but the delegated local Codex lane was blocked by the worker account usage limit plus workspace/network restrictions. Do not retry that same local Codex path until durable blocker evidence changes. The heaven2 HMAC boundary was not weakened.

## Unresolved risks and remaining work

- **Unresolved risk:** v8.8.67 production/source changes are unverified until Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership gates pass on the exact final reconciled head.
- **Unresolved risk:** #556 source/UIA gate is integrated, but published installed-client selector evidence is still required before #556 closes unless newer canonical evidence supersedes this handoff.
- #558 still needs broader provider-aware pagination/browse expansion plus deterministic scale/performance coverage.
- #559 remains separate work for filters, sorting, provider health, and richer loading/empty/stale/partial-failure states.
- #350/#354 retain their documented external signing/ruleset/certificate blockers.
- Conditional Steam Workshop and optional Vortex interoperability remain under #281 and must not be invented as ordinary MHW sources.

## Ordered continuation

1. Open the reconciled v8.8.67 #558 PR from `agent/issue-558-provider-search-v8.8.67-20261002` to `main`.
2. Require Workflow Feature, Product Security, and Toolbox Ownership gates on the **exact final head**; repair only evidence-backed failures.
3. Refresh canonical `main` and live ownership immediately before integration. If main moved, reconcile again rather than merging stale.
4. Merge only when all required exact-head gates are green.
5. Read back canonical `main`, record exact integration evidence, then release the #558 collaboration claim.
6. Keep #558 open for broader pagination/browse/scale work after this tranche.
7. Separately finish #556 publication/installed-client E2E acceptance when that lane is available.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and `_AGENT_CONTEXT/LEARNED_RULES.md` in full, refresh live claims/issues before mutation, preserve the continuity constitution and active learned rules, and recursively propagate this obligation. **Do not break the chain.**

The successor must propagate this continuity obligation to the next agent.
