# v8.8.67 MHW product — ordered next actions

1. Open and verify the reconciled #558 v8.8.67 provider-search candidate from `agent/issue-558-provider-search-v8.8.67-20261002` against current `main`.
2. Require exact-head Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership gates; queued, cancelled, failed, or stale-head gates are not merge authorization.
3. Refresh canonical main and the #558 claim immediately before integration; if main moved, reconcile again and rerun exact-head gates.
4. Merge only after all required gates are green, then read back canonical main and persist exact integration evidence.
5. Release the #558 collaboration claim only after canonical integration is confirmed.
6. Keep #558 open for broader provider-aware pagination/browse expansion and deterministic scale/performance coverage.
7. Separately finish #556 publication/installed-client selector E2E acceptance if it has not already been closed by newer canonical evidence.
8. Continue #559 only as a non-overlapping lane for provider/category filters, sorting, provider health, loading/empty/stale/partial-failure states, and discovery UX.
9. Preserve #281 provider contracts, #350/#354 external constraints, RECOVERY-007 Windows discovery proof, and other documented evidence gaps.

## Verification lineage

v8.8.66 PR #563 exact head `e47d0a91e966b5bcd86e69825af5d77970fdb500` passed Workflow Feature `36966157999`, MHW Product Security `36966158024`, and Heaven Toolbox Ownership `36966158016`, then merged as `5459db663f55388e72da97a79cb6e22ff0673048`.

The earlier #558 PR #564 also reached green gates on head `117c17b9dff6535bf939dd8b65ef54892f6592e9`, but those runs are superseded by the later main reconciliation and v8.8.67 version boundary.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`, reads `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and `_AGENT_CONTEXT/LEARNED_RULES.md`, and recursively propagates the continuity obligation. **Do not break the chain.**

The successor must propagate this continuity obligation to the next agent.
