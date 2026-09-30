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
- `docs/research/` — external research and design notes.
- `docs/history/` — superseded release/repair notes kept only for historical context.
- `data/` — checked-in reference datasets.
- `legacy-v7/` — frozen legacy implementation/material.
- `_AGENT_CONTEXT/` — live multi-agent continuity, audits, checkpoints, and evidence only.
- `_AGENT_TRAINING/` — durable agent operating standards and prompt templates.
- `.verification/` — verification state/bootstrap evidence.

## Hygiene

Do not add ad-hoc research notes, committed runtime debug logs, queue payloads, generated artifacts, release output, or temporary investigation files to the root. Prefer the folders above and add generated/runtime paths to `.gitignore` where appropriate.

For structural moves, first check active PR/branch changed files. Avoid moving files another agent is editing; relocate them only when references can be updated atomically without creating merge conflicts.


## Subfolder expectations

Folders are not allowed to become new flat roots. When several files share a durable concern, add a named subfolder and move the whole concern together with its references.

Current high-value cleanup targets for the organizing agent:

- `scripts/build/` — build/source-handoff entrypoints and helpers.
- `scripts/release/` — release, updater publication, release policy, and release verification scripts.
- `scripts/testing/` — test runners and policy/regression test scripts; split further by subsystem when a test family grows.
- `scripts/diagnostics/` — diagnostics/debug/startup-measurement scripts.
- `scripts/benchmarks/` — benchmark launch automation.
- `docs/auto-modder/` — the existing AUTO-MODDER-* document family.
- `docs/updater/` — updater/release-channel documentation when moved as a coherent batch.
- `docs/verification/` — verification/failure/diagnostic documentation when their references can be updated coherently.

These are target ownership neighborhoods, not permission to perform blind moves. Before each batch, search every old path consumer, check active agent ownership, update all references atomically, and run the affected verification.

## Root cleanup

The current root `.bat` launchers are candidates for `scripts/entrypoints/windows/`, but treat them as possible human-facing compatibility contracts. Before removing a root launcher, verify repository references plus local/operator shortcuts or documented launch flows. If compatibility requires the root name, keep a minimal wrapper at root and place the real implementation in the organized script tree.

Do not move toolchain/discovery files such as the solution, `Directory.*.props`, `global.json`, primary README/security/changelog/version metadata, or mandatory top-level agent entrypoints solely to make the root visually smaller.

## Binding agent rule

The full mandatory rule is `../_AGENT_TRAINING/REPOSITORY_STRUCTURE.md`. All repository-changing agents must follow it. This document is the human-facing layout map; the training file defines placement, migration verification, and multi-agent coordination invariants.
