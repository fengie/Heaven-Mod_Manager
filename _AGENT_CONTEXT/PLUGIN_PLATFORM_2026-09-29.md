# Plugin Platform Continuity — 2026-09-29

## Status

The repository now has a canonical top-level plugin workspace at `plugins/`.

Policy is documented in:

- `plugins/README.md`
- `AGENTS.md`
- `README.md`

The implementation swarm directive is:

- `plugins/IMPLEMENTATION_SWARM_PROMPT.md`

## Architecture decision

Prefer one extensible **Heaven Control Plane** with stable, narrowly scoped capabilities and optional specialized plugins above it, rather than hundreds of unrelated tiny plugins.

Initial implementation path:

- `plugins/heaven-control-plane/`

The existing `heaven-bridge/` directory remains active compatibility/runtime infrastructure. It predates the new plugin workspace and must not be broken by a cosmetic move. New platform code should reuse or adapt working bridge primitives. Any later relocation of `heaven-bridge/plugin/` must be an explicit, fully verified migration with updated bootstrap/workflow/test/docs references and compatibility shims where required.

## First production priorities

Implement in this order:

1. shell execution and persistent sessions;
2. filesystem read/write/patch/search;
3. Git and automatic integration to `main`;
4. build/test/lint/typecheck execution;
5. process/service/dev-server management;
6. browser automation and screenshots;
7. Windows UI Automation / keyboard / mouse / window control;
8. structured worker/agent task queue;
9. repository indexing and semantic/code search;
10. logs, artifacts, checkpoints, and machine health.

After those are stable, expand into Docker/VM/WSL, local AI, databases/networking, CI/release automation, multi-machine orchestration, resource scheduling, display/streaming controls, diagnostics, installer testing, and specialized workflows.

## Delivery model

Use the repository swarm rules. Split independent boundaries across worktrees/short-lived branches, keep ownership explicit, land small validated tranches, and integrate each completed tranche into current remote `main`.

Completion means the implementation is present and verified on remote `main`, with completed temporary branches safely deleted.

## Security boundary

Local plugins may automate machines and repositories the user owns. They must not be designed to bypass external service security, account quotas, OAuth authorization, or platform-enforced permissions.

Keep secrets out of Git, GitHub relay payloads, logs, artifacts, checkpoints, and fixtures. Prefer host-side secret injection and scoped capability handles.

## Next action

Start the swarm using `plugins/IMPLEMENTATION_SWARM_PROMPT.md`.

The first swarm cycle should land:

- the control-plane skeleton and capability protocol;
- discovery/health;
- at least one real vertical slice spanning execution, filesystem, Git/repo validation, logs/artifacts, and tests;
- compatibility mapping to existing Heaven Local Bridge primitives;
- exact-main verification.
