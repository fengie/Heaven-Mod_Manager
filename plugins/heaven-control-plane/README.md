# Heaven Control Plane

Canonical new-plugin implementation area for the Heaven local execution/control platform.

This directory is intentionally separate from the active `heaven-bridge/` runtime boundary while the platform is being extracted and generalized. Do not duplicate working bridge logic: adapt/reuse it behind stable interfaces.

## Initial module boundaries

- `protocol/` — capability schema/versioning, discovery, typed result/error contracts.
- `execution/` — shell, sessions, processes, services, dev servers.
- `filesystem/` — read/write/patch/search/copy/move/delete/hash/diff.
- `git/` — Git/repository functions and safe `main` integration workflow.
- `verification/` — build/test/lint/typecheck/project detection.
- `browser/` — browser automation, screenshots, DOM/console/network inspection.
- `desktop/` — Windows UIA, windows, keyboard, mouse, screenshots, clipboard boundaries.
- `workers/` — task queue, agent lifecycle, worktrees, locks, messaging.
- `index/` — repository indexing, symbol/text/semantic search, context reduction.
- `observability/` — logs, artifacts, checkpoints, metrics, crash/debug bundles.
- `adapters/heaven-bridge/` — compatibility adapter to proven bridge primitives.

Create these directories only when implementation lands; avoid empty scaffolding that implies nonexistent functionality.

## First vertical slice

The first implementation should prove the complete architecture with a small production path:

1. capability discovery/health;
2. one structured command-execution primitive;
3. one filesystem read/write/patch primitive set;
4. Git status/diff plus exact remote-main verification;
5. structured logs/artifact output;
6. unit/integration tests;
7. a Heaven Bridge compatibility adapter where the primitive already exists.

Then expand module-by-module using `../IMPLEMENTATION_SWARM_PROMPT.md`.

## Required properties

Every capability needs validated input, bounded output, structured errors, timeout/cancel semantics where applicable, authorization/capability metadata, audit metadata, and tests.

Prefer safe structured functions over raw shell. High-level workflows compose primitives rather than bypassing their checks.

## Compatibility

`heaven-bridge/` remains active infrastructure until a verified migration says otherwise. Any migration must update every runtime/bootstrap/workflow/test/doc reference and preserve compatibility during rollout.
