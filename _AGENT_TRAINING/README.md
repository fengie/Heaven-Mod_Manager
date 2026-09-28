# Generic Programming-Agent Trainer

This directory is the company-level engineering doctrine derived from reusable lessons learned across software projects. It is intentionally project-agnostic.

## Authority and hierarchy

Use this order when deciding how to work:

1. company engineering doctrine in this directory;
2. repository-specific operating rules and architecture;
3. current canonical repository state;
4. current task instructions.

More specific, newer repository truth may override generic guidance. A task prompt never overrides verified repository truth.

## Required startup behavior

Before meaningful engineering work:
- establish the canonical repository, branch, revision, and working-tree state;
- read the project's own startup/continuity rules;
- read the trainer sections relevant to the task;
- identify invariants, dangerous boundaries, and verification obligations before editing.

## Required completion behavior

Before declaring a task complete, ask:

> Did this work reveal reusable engineering knowledge that belongs in the company trainer?

If yes, generalize the lesson, verify the root cause, update the appropriate trainer document, and keep project-specific details in the project layer.

Documentation is part of the engineering pipeline. Update meaningful truth at the same checkpoint as the work that changed it; do not wait for a final cleanup pass.

## Contents

- COMPANY_ENGINEERING_VALUES.md — durable values and how to verify them.
- AGENT_OPERATING_STANDARD.md — mandatory behavior for programming agents.
- DEVELOPMENT_PIPELINE.md — selectable end-to-end engineering stages.
- AGENT_ROLES.md — role boundaries for implementation, review, testing, integration, recovery, and release.
- MULTI_AGENT_COORDINATION.md — branch ownership, status, integration, and stale-work handling.
- CONTINUITY_PROTOCOL.md — minimum durable state and interruption recovery.
- VERIFICATION_DOCTRINE.md — evidence standards and test strategy.
- SAFETY_AND_DESTRUCTIVE_OPERATIONS.md — fail-closed rules for destructive/stateful work.
- CI_RELEASE_ENGINEERING.md — build, CI, artifact, update, and rollback doctrine.
- KNOWLEDGE_MAINTENANCE.md — how this living body of knowledge evolves.
- PROMPTING_GUIDE.md — how to write high-quality programming-agent prompts.
- PROVENANCE.md — lightweight mapping from generic doctrine to the MHW evidence that motivated it.
- PROMPT_TEMPLATES/ — reusable role prompts.

## What this is not

This is not an architecture document, bug tracker, project handoff, current branch inventory, or substitute for source code and tests. Those belong in the project layer.

The long-term rule is simple:

> Every project should make the next project better.
