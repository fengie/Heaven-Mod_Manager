# Plugin Workspace

This directory is the canonical repository home for **new plugin source, plugin manifests, plugin-specific tests, plugin packaging, and plugin documentation**.

## Standing rule

From 2026-09-29 forward:

- New plugins MUST be created under `plugins/<plugin-name>/`.
- New reusable plugin modules that are shared by more than one plugin belong under `plugins/_shared/`.
- Plugin-specific test suites belong beside the plugin under `plugins/<plugin-name>/tests/` unless the project framework requires another layout.
- Plugin packaging/build scripts belong inside the owning plugin directory or `plugins/_tooling/`.
- Do not create new top-level plugin directories.
- Do not scatter plugin source under `tools/`, `scripts/`, `src/`, or unrelated feature folders merely for convenience.
- Repository-wide scripts may invoke plugins, but the plugin implementation remains owned here.
- Every plugin must include a README describing purpose, exposed capabilities, security boundary, local dependencies, validation commands, and ownership/migration notes.


## Plugin-first routing and gap capture

The plugin workspace is not only an implementation location; it is the canonical routing and capability-improvement surface for agents.

Before using a generic/manual fallback for a task:

1. inspect the available plugin/toolbox capabilities;
2. choose the narrowest purpose-built implemented capability;
3. prefer structured workflow/control-plane functions over raw shell/browser/desktop primitives when they cover the same operation safely;
4. consult `PLUGIN_GAP_BACKLOG.md` before assuming a capability is missing.

When a reusable capability is missing or incomplete, update `PLUGIN_GAP_BACKLOG.md` immediately. Do not leave the idea only in chat or agent memory. Search active plugin branches/PRs first so the plan extends existing work rather than duplicating it.

If the relevant plugin/connector/tool is **proven unavailable in the current runtime**, ordinary backlog capture is not enough. Follow `LOCAL_REPLACEMENT_PROTOCOL.md`: in the same execution cycle, record the outage evidence, create/update the plan, claim/start the local replacement project, and make the first concrete repository implementation move. Prefer a compatibility adapter over duplicating existing Heaven/bridge primitives.

A gap entry must be implementation-ready enough for a future agent to pick up without the original conversation: triggering use case, proposed owner/plugin boundary, capability/API contract, security constraints, dependencies/reuse, acceptance tests, priority, and current status.

The immediate user task should still proceed through the safest authorized fallback when possible. Local replacement work exists to remove recurring external-plugin dependency over time, not to create artificial blockers or to bypass provider quotas/authentication.

## Existing Heaven Local Bridge compatibility

The existing `heaven-bridge/` tree predates this workspace and is currently active infrastructure. Its existing runtime/relay paths must not be broken by a cosmetic move.

For Heaven plugin work:

1. Treat `heaven-bridge/` as an active compatibility/runtime boundary.
2. Put **new plugin-platform implementation** under `plugins/heaven-control-plane/`.
3. Reuse or extract proven bridge primitives rather than duplicating them.
4. If/when the existing `heaven-bridge/plugin/` source is migrated here, do it as one verified migration:
   - update all scripts, workflows, docs, tests, manifests, and bootstrap references;
   - preserve a compatibility shim/path where required;
   - run the full bridge gate;
   - verify the worker on `heaven`;
   - merge to `main`, push, verify remote `main`, then remove any temporary branch.
5. Never leave two independently maintained copies of the same plugin implementation.

## Implemented packages

- `_tooling/` — generated capability index/resolver plus schema-safe plugin-gap planning and validation.
- `heaven-control-plane/` — stable structured execution, filesystem, Git verification, build/test plans, indexing, and observability over the existing Heaven Local Bridge.
- `heaven-workflows/` — reusable repository verification/snapshot workflows and bounded parallel structured-capability execution.
- `heaven-task-queue/` — SQLite dependency queue with worker leases, retries, heartbeats, and named resource locks.
- `heaven-local-ai/` — bounded local Ollama generation and engineering-context compression through the control plane.
- `heaven-git-ops/` — safe structured branch/integration/push workflows.
- `heaven-process-services/` — process, Windows service, dev-server, and TCP health lifecycle.
- `heaven-file-ops/` — bounded structured file metadata/list/copy/move/delete/binary operations.
- `heaven-desktop/` — stable display/window/input/UIA/screenshot control over existing bridge primitives.
- `heaven-browser/` — browser launch/navigation and accessible-control automation over desktop/UIA.
- `heaven-state-store/` — durable artifacts, optimistic checkpoints, and machine-health history.
- `heaven-database/` — contained SQLite inspection/query/mutation/integrity/backup operations.
- `heaven-visual/` — local image probe/tile/resize/crop preprocessing.
- `heaven-system-ops/` — Docker/WSL/Hyper-V/package/network/Tailscale/SSH/SCP operations.
- `heaven-cluster/` — capability-aware multi-machine worker registry, selection, reservation, and dispatch.

### Active local-replacement projects

- `remote-desktop-commander-local/` — compatibility/parity layer that routes Remote Desktop Commander-style machine operations to existing Heaven Local Bridge/plugin primitives when the external connector is unavailable; current first milestone is structured storage-audit parity.

Run the full plugin workspace gate with:

```powershell
python .\plugins\verify.py
```

## Initial layout

```text
plugins/
  README.md
  IMPLEMENTATION_SWARM_PROMPT.md
  verify.py
  heaven-control-plane/
  heaven-workflows/
  heaven-task-queue/
  heaven-local-ai/
  _shared/
  _tooling/
```

Directories may be added only when they have a clear ownership boundary.

## Architecture direction

The preferred shape is a **Heaven Control Plane** with narrowly scoped capabilities and optional specialized plugins layered on top, rather than hundreds of unrelated tiny plugins. The control plane should expose stable capability contracts for shell/processes, filesystem, Git/repository operations, builds/tests, browser/UI automation, jobs/agents, indexing/search, artifacts/checkpoints, health/metrics, and later multi-machine workers.

Implement high-level workflows only after the underlying primitive capabilities are reliable and tested.

## Delivery and Git rules

All plugin agents inherit `AGENTS.md`, `GLOBAL_GIT_DIRECTIVE.md`, the swarm rules, and the repository continuity protocol.

- `main` is canonical.
- Short-lived branches/worktrees are allowed during parallel work.
- Each owner integrates completed, validated work into current `main`.
- Push and verify remote `main`.
- Delete completed temporary branches after confirming no unique work remains.
- Documentation/plugin-policy-only changes do not require an application version bump.
- A plugin that changes shipped application behavior follows the repository's normal version/README/CHANGELOG rules.

## Security boundary

Plugins may automate and administer machines and repositories the user owns, but must not be designed to bypass service security, account quotas, OAuth authorization, or platform-enforced permissions.

Secrets should be referenced through capability/secret handles wherever possible rather than returned in plaintext.
