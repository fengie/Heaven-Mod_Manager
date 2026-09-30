# Generic Programming-Agent Trainer

This directory is the company-level engineering doctrine derived from reusable lessons learned across software projects. It is intentionally project-agnostic.


## Mandatory task/plugin preflight

Every trained agent must still perform a fresh task-specific plugin preflight before planning or execution. Re-read the full assigned task, discover the actual plugins/connectors/skills/toolbox capabilities available in that runtime, load/read the instructions for relevant capabilities, and use/activate the narrowest applicable purpose-built route. Persist `PLUGIN-PREFLIGHT` evidence. User-named plugins are mandatory routes unless current-runtime evidence proves them unavailable, unsafe, or insufficient. Managers must embed and verify this gate for every dispatch; stale assumptions or a single failed lookup are not acceptable evidence that a plugin is unavailable.

## Authority and hierarchy

Use this order when deciding how to work:

1. company engineering doctrine in this directory;
2. repository-specific operating rules and architecture;
3. current canonical repository state;
4. current task instructions.

More specific, newer repository truth may override generic guidance. A task prompt never overrides verified repository truth.

## Required startup behavior

Repository training remains a pre-response gate, but it is intentionally **progressive and hash-verified** rather than a forced reread of the entire historical corpus.

Before the first task-facing answer, plan, dispatch, or task-specific action:
- establish the canonical repository, branch, revision, working-tree state when available, relevant PR/branch ownership, and current verification boundary;
- read the repository's compact core startup set in full;
- use the controller-generated hash/index manifest to prove the larger continuity/training corpus belongs to the assigned revision;
- expand only task-relevant sections of large indexed files, plus materially relevant bug precedents/learned rules and nearby source/tests;
- paginate/chunk truncated reads instead of treating truncation as failure;
- route around a missing preferred CLI, network path, or checkout using authorized GitHub connectors/APIs, canonical worktrees, Heaven Local Bridge/Agent Control, or repository CI;
- declare a training/execution blocker only after reasonable authorized fallbacks are exhausted and record the attempted routes/evidence.

Senior/premium agents should consume compact evidence packets and spend scarce context on architecture, diagnosis, review, integration, and verification decisions; mechanical retrieval and repetitive evidence gathering should be offloaded when practical.

No agent should answer first and “catch up” on repository context afterward. Progressive startup changes **how much is reread**, not the requirement to prove canonical truth and inspect task-relevant evidence. Propagate the same gate to successors and sub-agents.

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
- PROJECT_LESSON_CATALOG.md — portable rules promoted from real project incidents and workflow discoveries.
- PROMPTING_GUIDE.md — how to write high-quality programming-agent prompts.
- PROVENANCE.md — lightweight mapping from generic doctrine to the MHW evidence that motivated it.
- PROMPT_TEMPLATES/ — reusable role prompts.

## What this is not

This is not an architecture document, bug tracker, project handoff, current branch inventory, or substitute for source code and tests. Those belong in the project layer.

The long-term rule is simple:

> Every project should make the next project better.

## Mandatory continuous-learning contract
Whenever work exposes a bug, regression, false completion, process/agent failure, release failure, coordination failure, user correction, or durable workflow improvement, generic promotion is a required completion gate.

A qualifying lesson is closed only when project evidence is durable, root cause/invariant are stated, generic trainer coverage was checked, missing/weak doctrine was updated, and enforcement was strengthened where practical. Repeated escape of an already-documented class means the prevention mechanism itself failed and must be strengthened.


## Repository structure training

`REPOSITORY_STRUCTURE.md` is mandatory training for every repository-changing agent. It defines the organized-library placement invariant, canonical directory homes, subfolder rules, safe move protocol, verification requirements, and multi-agent coordination for reorganization work.
