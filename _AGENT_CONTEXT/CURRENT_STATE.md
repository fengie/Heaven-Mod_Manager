# v8.8.69 provider-aware catalog search — candidate state

Issue #556 is **DONE**. Issue #558 remains **ACTIVE**.

v8.8.68 PR #567 merged as `7def58c1b16d115e1555738ebad51717d1c1f752`; hosted Windows run `36978710736` passed 26/26, and installed-client E2E `36979261045` passed real update, selected DisplayName, enabled Switch/Settings, and rollback. Preserve that completed boundary.

## Candidate change

PR #565 is reconciled onto fresh main `3291b4c9270c4cb2a4beac2900ce724dd00ef0e6` rather than merging its stale v8.8.67 metadata.

- Explicit Browse Mods Search queries only providers advertising `CatalogProviderCapabilities.Search`.
- Per-keystroke query debounce remains cache-only.
- Remote rows persist through `CatalogSyncService` before display.
- Provider failures are isolated so cached results remain usable.
- Unsupported provider search modes are not probed.
- `CatalogRepository` now permits up to 1000 search results, matching the already-virtualized UI capacity.
- Regression coverage proves a 600-row search is no longer truncated at the former 500-row ceiling.

## Verification boundary

The old #565 green runs are superseded. v8.8.69 requires Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on one exact final reconciled head before merge.

The Heaven Bridge health probe succeeded on `heaven`; a read-only local Codex review dispatch then failed on local Codex usage quota until 2026-10-07. No local-agent verification is claimed.

## Remaining catalog work

#558 remains open for broader provider-aware pagination/browse expansion and deterministic scale/performance coverage. #559 remains separate UX work.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, and propagate the continuity obligation to the next agent.
