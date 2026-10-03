# v8.8.83 save-snapshot orphan pruning — candidate handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Candidate PR: #662

## v8.8.83 behavior

- Reclaim immediate-child directories under `SaveBackupService.SnapshotRoot` when no live `save_snapshots` row owns them.
- Preserve exact snapshot-root containment before destructive cleanup.
- Never recurse through orphan reparse points; unlink only the immediate link so external targets remain untouched.
- Treat `IOException` and `UnauthorizedAccessException` during orphan cleanup as retryable best-effort failures.
- Preserve indexed snapshot retention, stale-index cleanup, and the v8.8.82 updater topology/storage-retention boundaries.

## Verification boundary

The v8.8.83 candidate changes source/tests after the v8.8.82 tested source, so v8.8.82 evidence must not be inherited. PR #662 requires fresh exact-head verification before integration. No local Heaven execution route was exposed to the originating ChatGPT session, so no local test result is claimed here.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant learned rules. Preserve exact-input verification, snapshot containment/no-follow cleanup, updater request-topology, bounded updater storage retention, and durable-evidence privacy boundaries. The successor must propagate this continuity obligation to the next agent and require recursive propagation. Do not break the chain.
