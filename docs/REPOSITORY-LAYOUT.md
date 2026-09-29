# Repository layout

Keep the repository root intentionally small. Root files should be limited to project entry points, build metadata, high-signal handoff files, and user-facing launchers that depend on running from the root.

## Placement rules

- `src/` — production .NET projects.
- `tests/` — automated test projects.
- `benchmarks/` — performance harnesses.
- `scripts/` — build, verification, packaging, and automation PowerShell.
- `tools/` — developer utilities and standalone helper programs.
- `plugins/` — plugin implementations and plugin-specific docs/tests.
- `docs/` — durable product/architecture documentation.
- `docs/research/` — external research and historical design notes.
- `data/` — checked-in reference datasets.
- `legacy-v7/` — frozen legacy implementation/material.
- `_AGENT_CONTEXT/` — live multi-agent continuity, audits, checkpoints, and evidence only.
- `_AGENT_TRAINING/` — durable agent operating standards and prompt templates.
- `.verification/` — verification state/bootstrap evidence.

## Hygiene

Do not add ad-hoc research notes, debug logs, queue payloads, generated artifacts, release output, or temporary investigation files to the root. Prefer the folders above and add generated/runtime paths to `.gitignore` where appropriate.

For structural moves, first check active PR/branch changed files. Avoid moving files another agent is editing; relocate them only when references can be updated atomically without creating merge conflicts.
