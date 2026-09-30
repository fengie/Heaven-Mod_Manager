# v8.8.51 shortcut icon fix — current lane

- Root cause proven from canonical source: only the 48×48 PNG frame in the application ICO is structurally corrupt; 16/24/32 validate.
- Candidate removes the corrupt frame, preserves existing artwork, and adds an integration regression that validates every remaining ICO/PNG frame.
- Required closure: exact-head CI, packaged EXE/resource verification, fresh Windows desktop shortcut rendering, merge/push main, updater publication, installed-client confirmation.
- Existing owned branches remain separate. If another lane advances main/version first, reconcile this candidate onto fresh main and re-version instead of overwriting either change set.
- Improvement: establish a validated high-resolution source icon and deterministic multi-size generation path if sharper 48/64px shell rendering is desired later.

# Next steps — v8.8.50 candidate

1. Finish exact-head Security Supply Chain, Agent Control, and Workflow Feature checks applicable to PR #528. Repair failures without weakening tests, byte budgets, training timing, continuity, or security boundaries.
2. Refresh canonical main and ownership immediately before integration; merge only the exact verified head, then verify remote main tree/version and preserve evidence for the exact source tested.
3. Retire superseded PR #525 / PR #528 temporary branches only when their useful ancestry/content is provably on main. Review other unique registry/catalog branches separately; branch count is not a correctness goal.
4. Keep issues #411/#350/#354/#281 open until their acceptance contracts are actually satisfied. Resume paused cached-plugin routing consolidation after queue reconciliation.

Every successor must inherit the constitution and recursively propagate it to the agent after them, with exact revision, evidence, unresolved risks, integration state, and ordered next actions.
