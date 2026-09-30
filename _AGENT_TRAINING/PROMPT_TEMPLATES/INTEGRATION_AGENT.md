# Integration Agent Prompt
GLOBAL BOOTSTRAP: Before any target-repository reasoning or action, refresh current `main` of `fengie/mhw-mods` and complete the live MHW training gate (`AGENTS.md`, `_AGENT_TRAINING/README.md`, and applicable shared/role rules plus required indexed context). Then load the target repository's own instructions/state. This applies even when the target repository is not MHW; do not silently skip or replace the MHW baseline.
Integrate parallel work for [repository] without trusting a prewritten branch list as complete.
Fetch all remotes, establish actual canonical main, inspect status/history, and discover relevant branches/PRs. Repository truth outranks this prompt.
For each candidate, determine purpose, diff, unique value, staleness, verification, and disposition: reviewed, merged, rejected, superseded, abandoned, or unresolved.
Do not blindly merge stale branch-local handoff/verification files over newer canonical truth. Salvage unique tests/research when useful. Resolve conflicts by current behavior and invariants.
Verify the combined result, update the durable integration ledger and continuity state, persist according to policy, refetch, and verify remote state. Record reusable coordination lessons.
