# v8.8.86 MHW product — ordered next actions

1. Require exact-final-head Product Security and Workflow Feature verification for PR #679 after its v8.8.85 reconciliation; merge only that exact green head, verify remote `main`, then observe the canonical Windows release gate without `-FeatureCandidate`, persist exact closure evidence, close #676, and retire its temporary branch/claim.
2. Reconcile PR #674 (#673 adoption hardening) onto the new canonical baseline, discard its stale v8.8.85 release metadata, assign the next available patch, and rerun exact-head verification before integration.
3. Reconcile PR #670 (Advanced Tools accessibility) and PR #666 (#642 atomic recipe export) one at a time against then-current canonical `main`; each independent change gets its own patch and exact-input verification.
4. Treat draft PR #663 as superseded by #670 only after semantic comparison proves no unique work remains. Reconcile draft #662/#659 by extracting useful unique work rather than blindly merging stale history.
5. Continue #578/#558/#559 and representative RECOVERY-005/RECOVERY-007 acceptance without reopening already closed source/evidence.

Every successor must read and propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md` context onward to the next agent.
