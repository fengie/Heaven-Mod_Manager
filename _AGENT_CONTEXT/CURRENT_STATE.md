# Current project state — v8.8.48 training-governance candidate

Canonical repository: `fengie/mhw-mods` / `main`.

Active governance candidate: `refactor/agent-training-v8.8.47-20260930`, based on `7def479cd4c327b0ed45790a5b28a6b68c5d3c67`.

## This candidate

The active agent-training system is being consolidated rather than expanded. The target architecture is:

- a compact root `AGENTS.md` router;
- one universal `_AGENT_TRAINING/AGENT_OPERATING_STANDARD.md`;
- task-relevant indexed policy/continuity instead of mandatory historical rereads;
- manager/swarm templates that contain only role deltas;
- concise current-state and handoff documents;
- semantic continuity checks plus hard byte budgets to prevent prompt growth.

This is governance/prompt/bootstrap/verification-tooling work. It does not change MHW product behavior.

## Product / verification state not changed by this candidate

The v8.8.47 Windows verification evidence on canonical source `ed02e96b5f05b51c4e53a30ca62d1d6b3d49462f` remains historical evidence for that source only. Do not transfer it to this governance candidate without fresh exact-source checks.

Agent Manager remains P0/functionality-lock until authenticated provider dispatch, observation, recovery, and stop behavior is proven end to end under the existing acceptance contract. Actual installed-client confirmation remains separate from disposable/release-CI proof.

## Concurrent work

Unrelated product branches and PRs keep their own ownership. In particular, the current Mods-width UI fix is not part of this training refactor. Before integration, refresh PRs/branches/leases and preserve any newer canonical work.

## Current risks

- Active training has accumulated duplicated startup, delivery, continuity, plugin, and bug-prevention prose across several owners.
- Old current-context files have carried historical revision stacks, increasing retrieval cost and stale-state risk.
- Prose-coupled handoff validation has encouraged duplicated phrases instead of validating a smaller semantic contract.
- The training refactor must preserve MHW-specific fail-closed invariants while moving them out of universal startup context.

See `NEXT_STEPS.md` for ordered actions and `NEXT-AGENT-START-HERE.md` for handoff.
