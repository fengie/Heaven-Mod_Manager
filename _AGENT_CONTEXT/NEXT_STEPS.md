# v8.8.66 MHW product — ordered next actions

1. Open and verify the #558 v8.8.66 provider-search candidate from `agent/issue-558-provider-search-20261002` against current `main`.
2. Require exact-head Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership gates; queued/cancelled/failed gates are not merge authorization.
3. Merge only after all required gates are green and fresh-main reconciliation preserves concurrent work.
4. Read back remote `main`, persist exact integration evidence/handoff, then release the #558 collaboration claim.
5. Keep #558 open for broader provider-aware pagination/browse expansion after the capability-gated remote-search tranche.
6. Separately obtain installed Windows/WPF acceptance for #556 and close it only if the published installed client shows readable selected/dropdown game text.
7. Continue #559 only as a non-overlapping lane for provider/category filters, sorting, provider health, loading/empty/stale/partial-failure states, and discovery UX.
8. Preserve #281 provider contracts, #350/#354 external constraints, RECOVERY-007 Windows discovery proof, and other documented evidence gaps.

## Closed verification boundary

v8.8.65 exact PR #562 head `236d3604f1df245f9c224eda3f21f2177979cfd2` passed Workflow Feature PR Gate `36963458484`, MHW Product Security Gate `36963458384`, and Heaven Toolbox Ownership Gate `36963458431`, then merged to canonical main as `c3f238cbe4f1850763736bac999af0577a4606c5`.

The v8.8.66 candidate does not inherit that verification.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve and recursively propagate the MHW continuity constitution. **Do not break the chain.**
