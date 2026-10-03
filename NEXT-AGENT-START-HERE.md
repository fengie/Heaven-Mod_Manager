# v8.8.84 save-snapshot orphan pruning — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: snapshot orphan pruning

## v8.8.84 behavior

- Reclaim immediate-child directories under `SaveBackupService.SnapshotRoot` when no live `save_snapshots` row owns them.
- Remove every valid DB-owned snapshot path from the orphan candidate set before retention/deletion handling so indexed payloads are never reclaimed as orphans.
- Preserve exact snapshot-root containment before destructive cleanup.
- Never recurse through orphan reparse points; unlink only the immediate link so external targets remain untouched.
- Treat `IOException` and `UnauthorizedAccessException` during orphan cleanup as retryable best-effort failures.
- Preserve the v8.8.83 self-cleaning test-scratch boundary and the v8.8.82 updater topology/storage-retention boundary.

## Verification boundary

The v8.8.83 candidate source `d7d15429bbcfb421e5d5b7120f7f54a830082aca` passed all exact-head PR gates, including Workflow Feature run `37108432265`, and integrated to canonical main as merge commit `ed7006e18d1fdbbdedd2209d4b766b5db938add9`. The merge commit itself was not separately reverified and must not be represented as the exact tested source.

v8.8.84 changes source/tests/release metadata after that boundary and therefore requires fresh exact-head verification before integration. The earlier #662 run `37107946505` proved the corrected snapshot logic and focused regressions, but failed release metadata parity before this 8.8.84 reconciliation; do not inherit it as final closure.

## Unresolved risks and next work

#559/#558 retain Browse Mods work; #350/#354 retain external prerequisites; RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent. The permanent stale-test-scratch reaper remains intentionally conservative around unmarked historical roots.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, snapshot containment/no-follow cleanup, the v8.8.83 test-scratch lifecycle boundary, updater request topology/storage retention, and durable-evidence privacy. The successor **must propagate** this obligation recursively. **Do not break the chain.**
