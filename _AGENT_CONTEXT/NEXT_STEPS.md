# v8.8.86 MHW product — ordered next actions

1. Require fresh exact-final-head gates for metadata-synchronized PR #691; merge only that exact green head, verify remote `main`, then observe the canonical Windows release gate without `-FeatureCandidate`, persist exact closure evidence, and close #676.
2. Reconcile PR #674 (#673 adoption hardening) onto the new canonical baseline, discard stale release metadata, assign the next available patch, and rerun exact-head verification.
3. Reconcile PR #677 (#662 orphan snapshot pruning replacement) and PR #666 (#642 atomic recipe export) one at a time against fresh `main`; preserve code/tests and discard stale release/continuity state.
4. Promote already-green source-only PR #685 (#669 storage preflight) and #687 (#686 provenance generator) only after fresh-main reconciliation and synchronized independent patch metadata. Keep #688 as its own Browse Mods lane.
5. Keep draft #659 until every unique historical tranche is proven integrated, superseded, or extracted; continue #578/#558/#559 and representative RECOVERY-005/RECOVERY-007 acceptance without reopening closed work.

Every successor must read and propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`.
