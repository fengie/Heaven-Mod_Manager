# Agent Control terminal registry retirement handoff — 2026-09-30

## User-visible incident

The dashboard accumulated dead cards such as **failed · RETRY EXHAUSTED** and left them visible/clickable after recovery had no further legal action.

Two distinct defects contributed:

1. **Retry amplification (already repaired before this branch):** authoritative nonzero process exits could be classified as no-work, causing replacement storms until the retry budget was exhausted.
2. **Terminal registry leakage (this branch):** retry exhaustion only changed `recoveryStatus` and saved state. It did not retire the dead managed/federated identity, and normal refresh synchronized that record again forever.

## Candidate behavior

- `shouldRetireFromLiveRegistry()` retires only non-active records with no substantive durable work.
- Retry-exhausted managed/federated sources are removed immediately after task/event/failure evidence is persisted.
- Refresh sweeps legacy clean dead records before `syncManagedAgents()`, so old persisted debris disappears without a manual registry reset.
- Notification actions that target a retired ID are cleared.
- Managed retirement records `task.registryRetirement` for durable audit context.
- Dirty worktrees, commit/SHA divergence, changed files, PRs, artifacts, verification results, and other substantive evidence prevent retirement and remain available for recovery.

## Relevant candidate commits

- `deff943aaa509908b39a5b9f024c493947dc901b` — retirement predicate
- `4abd659f8c859f1343a2d7278eb20c507e887eec` — predicate regressions
- `831c2344418fb67037168a1f8b5fcb147e500951` — server retirement/sweep behavior
- `34318cc567457f7d8dd98f8ff310ff4f4d439919` — operator contract regression

Subsequent commits on the same branch advance the repository to v8.8.26 / Agent Control v0.6.5 and persist documentation/training.

## Required verification

From the exact candidate under `tools/agent-control`:

- `npm run check`
- `npm test`

On heaven2:

- restart/refresh Agent Control and confirm pre-existing clean **failed · RETRY EXHAUSTED** cards disappear;
- verify no replacement storm starts merely because the legacy records are swept;
- inspect recent failure/task/event diagnostics and confirm historical evidence remains;
- inject/observe a failed worker with dirty/substantive work and prove it stays preserved/actionable.

At this checkpoint both Remote Desktop Commander endpoints reported offline, so no new local runtime pass is claimed.

## Successor guidance

Keep the live registry and historical audit trail separate. Do not “fix” stale-card noise by deleting tasks/events/failure logs or by pruning dirty/unique work. The safe order is:

**authoritative runtime outcome → durable-work inspection → bounded recovery decision → evidence persistence → live-registry retirement when clean and terminal.**
