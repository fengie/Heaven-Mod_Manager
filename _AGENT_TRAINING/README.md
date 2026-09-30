# Programming-Agent Trainer

This directory is the reusable engineering layer for programming agents. It is intentionally smaller and more stable than project state.

## How to use it

For any repository assignment, bootstrap from current `fengie/mhw-mods` training first, then load the target repository's own instructions and current state. The bootstrap is progressive: read the compact core, then retrieve only task-relevant indexed material.

Do not turn the trainer into an archive. Git history, project evidence, incident ledgers, and old handoffs belong outside the active instruction path.

## Authority and evidence

Instruction priority is:

1. platform/safety requirements and the current user's explicit instructions;
2. the target repository's current, specific operating rules;
3. this generic trainer.

Facts use a different rule: current repository/runtime evidence outranks stale factual claims in prompts, summaries, or old documentation. A user may change scope or policy; an old hash may not overrule a freshly observed hash.

## Mandatory core

- `AGENT_OPERATING_STANDARD.md` — default programming behavior and completion standard.
- `AGENTS.md` at repository root — project entry point, bootstrap, and repository-specific invariants.
- `_AGENT_CONTEXT/CURRENT_REVISION.json` — compact current-state/verification identity.

Read larger documents only when their domain is relevant. The hash-verified repository context index is the normal discovery path.

## Reference map

- `DEVELOPMENT_PIPELINE.md` — risk-calibrated stage selection.
- `VERIFICATION_DOCTRINE.md` — evidence ladder and exact-source verification.
- `MULTI_AGENT_COORDINATION.md` — ownership, delegation, recovery, and convergence.
- `AGENT_ROLES.md` — role-specific responsibilities.
- `SAFETY_AND_DESTRUCTIVE_OPERATIONS.md` — destructive/stateful boundaries.
- `CI_RELEASE_ENGINEERING.md` — build, CI, supply chain, release, update, rollback.
- `REPOSITORY_STRUCTURE.md` — repository organization and safe moves.
- `KNOWLEDGE_MAINTENANCE.md` — how to improve this trainer without making it grow by default.
- `PROMPTING_GUIDE.md` — task/agent prompt design.
- `COMPANY_ENGINEERING_VALUES.md` — durable values.
- `PROVENANCE.md` and `PROJECT_LESSON_CATALOG.md` — provenance/reference, not routine startup reading.
- `PROMPT_TEMPLATES/` — role deltas; they must not duplicate the common operating standard.

## Maintenance rule

Persistent instructions have recurring context cost. Prefer, in order:

**delete → merge → rewrite → relocate → add**

Add a new permanent rule only when a concrete failure class is not already covered by a stronger existing invariant and the expected behavioral value justifies repeated context cost. Favor mechanical enforcement/tests over repeated prose.
