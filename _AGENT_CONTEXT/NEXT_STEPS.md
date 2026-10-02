# v8.8.69 MHW product — ordered next actions

1. Open the reconciled v8.8.69 #558 provider-search PR from `agent/issue-558-provider-search-v8.8.69-r2-20261002` against current `main`.
2. Require exact-head Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership gates.
3. Fix concrete failures only; preserve the v8.8.68 selector peer-value acceptance and raw-`GameProfile` rejection.
4. Refresh canonical main and live PR/issue state immediately before integration; reconcile again if main moved.
5. Merge only the exact green v8.8.69 head and verify remote main contains the provider-search behavior.
6. Close PR #565 as SUPERSEDED only after its useful unique semantics are proven integrated.
7. Keep #558 open for broader provider-aware pagination/browse expansion and deterministic scale/performance coverage.
8. Continue #559 separately for filters, sorting, provider health, loading/empty/stale/partial-failure states, and discovery UX.

Issue #556 is closed with packaged E2E evidence at `601496093430214791395ba1bfcfadd3b0262ad2`; do not reopen it without contradictory runtime evidence.

Every successor must read and propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and `_AGENT_CONTEXT/LEARNED_RULES.md` onward to the next agent.
