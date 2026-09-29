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


## Implemented platform slice (v0.3.0)

The first production slice is implemented as a small Python standard-library package:

- versioned capability registry plus structured success/error envelopes;
- discovery, health, and cancellation conventions;
- bounded structured command execution through existing Heaven Bridge `proc_run`;
- bounded filesystem read/write/exact-patch adapters over `fs_read`, `fs_write`, and `fs_edit`;
- Git status/diff/exact remote-`main` verification using `git ls-remote` against `refs/heads/main` and a structured working directory instead of path interpolation;
- payload-free bounded audit metadata and text-artifact pagination;
- enforced capability permissions with a trusted-local wildcard default and restricted permission sets for disposable workers;
- secret-canary refusal before text artifacts enter the in-memory artifact registry;
- inline secret-environment rejection with host-side `env_from_host` handles;
- traversal-segment rejection before filesystem/repository requests reach the bridge;
- explicit overwrite confirmation for existing-file rewrites, unique-only v1 text patching, and bridge-error propagation into structured control-plane failures;
- persistent session start/read/input/stop/list adapters over the bridge session lifecycle primitives;
- bounded allowlisted filesystem name/content search over `fs_search`;
- bounded project detection over paginated `fs_list`, with explicit ambiguity handling for mixed repositories;
- fixed-command build/test/lint/typecheck execution over `proc_run`, with structured nonzero-exit failures and per-project timeout ceilings;
- unit tests for success, malformed/oversized input, invalid request IDs, permission denial, timeout mapping, cancellation mapping, path traversal, session/search validation, output truncation, artifact secret refusal, pagination, bridge action compatibility, and concurrent audit writes.

Run the exact plugin gate from repository root:

```powershell
python .\plugins\heaven-control-plane\verify.py
```

The adapter is transport-injected on purpose: the active `heaven-bridge/` worker remains the runtime boundary while this package supplies stable contracts above its already-proven actions.


## Phase 1 tranche: persistent sessions and search

Version 0.2.0 extends the verified control-plane slice without introducing a second runtime. Persistent execution maps to Heaven Bridge `proc_start`, `proc_read`, `proc_input`, `proc_kill`, and `proc_list_sessions`; filesystem search maps to the existing allowlisted `fs_search` primitive. Session/search inputs are bounded and validated, mutating session operations carry destructive metadata, and bridge failures continue to propagate as structured control-plane errors.

## Phase 1 tranche: project verification

Version 0.3.0 adds `verification.detect` and `verification.run`. Detection reads only bounded filesystem marker metadata through the existing Heaven Bridge; execution selects from fixed Python, Node, and .NET command plans rather than accepting command text from callers. Mixed repositories require an explicit detected project type, traversal is rejected before transport, and failed tools return `VERIFICATION_FAILED` with bounded stderr metadata.
