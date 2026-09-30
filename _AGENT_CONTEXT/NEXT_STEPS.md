# v8.8.51 compact Agent Manager overview — next

1. Verify the stacked candidate against PR #528 latest exact governance head with Agent Control syntax/tests plus required security gates available on the stacked base.
2. After PR #528 is canonical, reconcile this v8.8.51 candidate onto fresh main and require exact-head Agent Control, Security Supply Chain, and Workflow Feature gates before merge.
3. Smoke the live heaven2 dashboard: all overview cards initially closed, counts visible without traversing content, notification severity summary correct, one opened card full-width with internal scrolling, managed/federated/notification Inspect automatically opens Inspector, interaction motion remains smooth under normal refresh, reduced-motion disables visible animation, and UI sound is silent until opt-in then persists without sounding on polling.
4. Preserve existing P0/auth/signing risks; do not infer control-plane lifecycle completion from this presentation-only patch.

---
# Next steps — v8.8.50 candidate

1. Finish exact-head Security Supply Chain, Agent Control, and Workflow Feature checks applicable to PR #528. Repair failures without weakening tests, byte budgets, training timing, continuity, or security boundaries.
2. Refresh canonical main and ownership immediately before integration; merge only the exact verified head, then verify remote main tree/version and preserve evidence for the exact source tested.
3. Retire superseded PR #525 / PR #528 temporary branches only when their useful ancestry/content is provably on main. Review other unique registry/catalog branches separately; branch count is not a correctness goal.
4. Keep issues #411/#350/#354/#281 open until their acceptance contracts are actually satisfied. Resume paused cached-plugin routing consolidation after queue reconciliation.

Every successor must inherit the constitution and recursively propagate it to the agent after them, with exact revision, evidence, unresolved risks, integration state, and ordered next actions.
