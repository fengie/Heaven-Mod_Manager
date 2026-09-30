# Next steps — v8.8.48 training-governance candidate

1. Finish the active-training consolidation on `refactor/agent-training-v8.8.47-20260930`: compact core, role-delta prompts, current-context cleanup, semantic validators, and lower enforced byte budgets.
2. Run focused verification: Agent Control repository-bootstrap tests, handoff validator, negative fixtures, syntax/static checks, and any workflow/tooling gate required by the changed files.
3. Measure before/after active startup/prompt bytes and inspect the final diff for lost repository-specific invariants or new duplication.
4. Refresh `origin/main`, open PRs/branches, and ownership immediately before integration. Reconcile if main moved; rerun invalidated checks on the reconciled SHA.
5. Integrate only the verified governance boundary, push/verify remote `main`, then safely retire the temporary branch if no unique work remains.
6. After this refactor, resume the existing Agent Manager P0/functionality-lock work from fresh canonical state. Keep unrelated UI/product branches under their own owners.

## Improvement opportunities after this patch

- Measure actual agent success/retry/token behavior under the smaller prompts before reducing budgets further.
- Continue replacing prose checks with behavioral/structural tests when an invariant is mechanically enforceable.
- Review large indexed reference documents for obsolete policy only when evidence shows they materially affect task retrieval; do not launch a broad rewrite merely to make files shorter.
- Revisit learned-rule promotion criteria after several incidents to verify that new rules are being merged into existing invariants rather than appended reflexively.

## Handoff requirement

Record exact final SHA, verification commands/results, integration status, remaining risks, and the next actionable boundary. Do not restore revision-by-revision history into this current file.
