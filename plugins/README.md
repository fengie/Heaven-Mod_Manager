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

## Initial layout

```text
plugins/
  README.md
  IMPLEMENTATION_SWARM_PROMPT.md
  heaven-control-plane/
    README.md
    src/
    tests/
    manifests/
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
