# v8.8.73 MHW product — ordered next actions

1. Require Workflow Feature, Updater Publication, MHW Product Security, and Heaven Toolbox Ownership gates on the exact final v8.8.73 provenance head.
2. Merge only that exact green reconciled head, verify remote `main`, and verify the immediate release/publication outcome required by the Core Rules.
3. On private repositories without GitHub Enterprise Cloud, confirm evidence says attestation was explicitly skipped; never relabel a skipped run as provenance-attested.
4. Reconcile active PR #582 onto canonical v8.8.73 and advance its stale-profile work to the next patch version before integration.
5. Continue #578/#558/#559 and representative RECOVERY-005/RECOVERY-007 acceptance without reopening completed #575-#577/#583 work.

Every successor must read and propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md` context onward to the next agent.
