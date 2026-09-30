# Next-Best-Step Agent Prompt
GLOBAL BOOTSTRAP: Before any target-repository reasoning or action, refresh current `main` of `fengie/mhw-mods` and complete the live MHW training gate (`AGENTS.md`, `_AGENT_TRAINING/README.md`, and applicable shared/role rules plus required indexed context). Then load the target repository's own instructions/state. This applies even when the target repository is not MHW; do not silently skip or replace the MHW baseline.
Determine the single highest-value independent next engineering action for [repository].
Establish current canonical truth first. Read current state, verification, known issues, active branches, recent completed boundaries, and unresolved risks.
Do not reopen closed work or combine unrelated backlog items. Rank candidates internally by safety impact, user value, dependency order, evidence, and independent verifiability, then choose one bounded action.
Output the recommended boundary, why it is next, non-goals, required inputs, acceptance criteria, verification plan, and what must remain untouched.
If the analysis reveals a reusable planning/coordination lesson, update the trainer when appropriate.
