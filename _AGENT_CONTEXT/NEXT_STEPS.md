# v8.8.69 MHW product — ordered next actions

1. Verify `fix/issue558-provider-search-v8.8.69-20261002` on one exact head with Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership.
2. Fix concrete failures only; preserve local-only query typing, capability-gated explicit provider Search, failure isolation, source-aware persistence, the 1,000-row repository/UI capacity contract, and v8.8.68 selector acceptance.
3. Refresh issue #558, PR #565, ownership, and canonical `main` immediately before integration. If main moved, reconcile and rerun exact-head gates.
4. Merge only the exact green v8.8.69 head and verify remote-main readback.
5. Mark stale PR #565 superseded only after its unique provider-search semantics are confirmed on canonical main.
6. Keep #558 open and continue provider-aware discovery/pagination. The default MHW provider set still lacks a search-capable remote provider unless CurseForge is explicitly configured.
7. Add deterministic scale/performance coverage for broader cached/provider result sets, then continue #559 filters/sorting/provider-health/loading/empty/stale/partial-failure UX.

Verified parent: v8.8.68 source `7def58c1b16d115e1555738ebad51717d1c1f752`; hosted Windows `36978710736` PASS 26/26; installed-client E2E `36979261045` PASS update + selector DisplayName + Switch/Settings + rollback.

Every successor must bootstrap from current `fengie/heaven-toolbox@main`, refresh current MHW main, read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, and propagate this obligation onward.
