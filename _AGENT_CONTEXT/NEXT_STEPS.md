# v8.8.54 Agent coordination lease hardening — integration lane

1. Freeze the final candidate head and require exact-head Agent Control, Security Supply Chain, and all other applicable PR gates. Do not promote v8.8.53 evidence to the changed source.
2. On the live control plane, verify a case-variant duplicate routing assignment is rejected, a counted deploy with one explicit mutable boundary launches zero workers, and an ordinary single-owner dispatch still succeeds without retry/launcher storms.
3. Refresh canonical main immediately before merge. If main moved, reconcile semantically and rerun invalidated checks; otherwise merge only the exact verified head and verify remote main/version/plugin identity.
4. Preserve the archive/branch-zero-20260930 tags until each archived queue slice is semantically recovered or deliberately superseded with proof. Continue one owned slice at a time.

# v8.8.53 Agent Work Reports integration lane

1. Freeze the final reconciled #540 head while Plugin Toolbox, Security Supply Chain, and Agent Control PR gates run; do not append proof-only commits that invalidate exact-head evidence.
2. If required gates pass, smoke the loopback dashboard on heaven2: overall-work list, expandable agents/tasks, explicit checkpoint ingestion, Agent Control-unavailable fallback, visible/hidden refresh cadence, and on-demand logs.
3. Recheck canonical main immediately before merge. If main moved, reconcile semantically and rerun invalidated checks; otherwise merge only the exact verified head and verify remote main contains the reporting plugin plus v8.8.53 metadata.
4. After integration, continue the preserved recovery/branch queue one semantic slice at a time. Do not delete divergent work merely to reduce branch count.

# v8.8.52 Agent Manager integration lane

1. Freeze the current reconciled head while Agent Control, Security Supply Chain, and Workflow Feature gates run; do not append evidence commits that invalidate exact-head proof.
2. If all required gates pass, verify the compact overview, Inspector auto-open, reduced-motion behavior, and opt-in UI sound on the live heaven2 dashboard.
3. Recheck canonical main immediately before merge and merge only the exact verified head; preserve v8.8.51 icon integrity and v0.6.24 Agent Control identity.
4. After integration, advance #540 as the next visible patch and recover [RECOVER] PRs one semantic slice at a time. No divergent unique branch may be deleted/reset before preservation proof.

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
