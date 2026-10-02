# v8.8.72 MHW product — ordered next actions

1. Require Workflow Feature, MHW Product Security, Heaven Toolbox Ownership, and updater-publication PR validation on the exact final v8.8.72 head.
2. Fix concrete failures only; preserve stable save-copy verification, pre-publication canonical-main freshness, post-integration continuity semantics, and Browse Mods exact-file gating.
3. Refresh canonical `main` and mergeability immediately before integration; do not reuse predecessor gate results.
4. Merge only one exact green reconciled head, verify remote `main`, and confirm #575, #576, and #577 close. Keep #559 open for remaining UX scope.
5. Prioritize issue #578's stale-profile lifecycle edge cases, then continue #558/#559 and representative RECOVERY-005/RECOVERY-007 Windows/runtime acceptance.

Every successor must read and propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md` context onward to the next agent.
