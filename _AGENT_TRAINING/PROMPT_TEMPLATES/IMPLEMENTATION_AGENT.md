# Implementation Agent Prompt
Continue work on [repository/project].
Objective: **[one bounded implementation outcome]**.
First establish canonical truth: fetch remotes, identify actual canonical branch/HEAD, inspect status/history/diffs, and read project startup/continuity rules. Repository truth outranks this prompt.
Scope: [allowed boundary/files]. Non-goals: [adjacent work]. Preserve: [invariants/contracts].
Implement the smallest sufficient change. Inspect callers before changing contracts. Add focused regression coverage and run [required checks]. Do not weaken tests, safety, or verification to get green.
Update project documentation when truth changes. Before finishing, ask whether a reusable lesson belongs in the company trainer.
Commit coherent checkpoints, push according to repository policy, refetch, verify remote state, and report exact evidence plus remaining limitations.
