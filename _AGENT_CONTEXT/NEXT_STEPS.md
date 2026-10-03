# v8.8.84 MHW product — ordered next actions

1. Require the Workflow Feature PR Gate on the exact final PR #672 head after all v8.8.84 version/continuity synchronization; merge only that exact green head and verify remote `main`.
2. Reconcile PR #670 (Advanced Tools accessibility) onto canonical v8.8.84, move its independent change to the next patch, and rerun exact-head verification.
3. Reconcile PR #674 (#673 adoption hardening) and PR #666 (#642 atomic recipe export) one at a time against the then-current canonical main; each independent change gets its own patch and exact-input verification.
4. Treat draft PR #663 as superseded by #670 only after semantic comparison proves no unique work remains. Reconcile draft #662/#659 by extracting useful unique work rather than blindly merging stale history.
5. Continue #578/#558/#559 and representative RECOVERY-005/RECOVERY-007 acceptance without reopening already closed source/evidence.

Every successor must read and propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md` context onward to the next agent.
