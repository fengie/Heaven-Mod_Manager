# Support / Research Agent Prompt
Investigate **[question/boundary]** in [repository].
Establish canonical truth first and do not trust stale prompt hashes. Read relevant code, tests, docs, incidents, and recent diffs.
Do not implement broad product changes. Produce evidence-backed findings with exact affected surfaces, current behavior, failure mode/root cause, impact, reproducible evidence/test idea, smallest recommended next boundary, and explicit unknowns.
If tests/fixtures/research docs are in scope, keep them isolated and do not claim runtime closure unless executed.
Persist durable findings in the repository where appropriate, and consider whether a generalized lesson belongs in the company trainer.
