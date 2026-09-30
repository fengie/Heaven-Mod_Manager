# Agent Manager compact overview — v8.8.51 stacked candidate

## Scope
- Ten high-volume operational surfaces default to collapsed native `<details>` cards: machine pool, notifications, inspector, registry history, managed agents, federated registry, leases, integration queue, branches, and recent events.
- Closed cards form a responsive two-column overview. Opening one expands it full-width.
- Expanded content is bounded and scrolls internally instead of pushing the rest of Agent Manager off-screen.
- Managed/federated/notification inspection automatically opens Inspector.
- Notification summary exposes critical/warning counts while the list stays bounded to the newest 30 rendered entries.
- Backend lifecycle, ownership, routing, recovery, and action semantics are intentionally unchanged.

## Lineage / coordination
- Target product patch: v8.8.51; Agent Control/plugin: v0.6.24.
- Parent baseline: PR #528 v8.8.50 governance head `31307b621f3d58855389644c8c92c98022c47639`.
- This is intentionally stacked so the UI does not overwrite the governance refactor. Once #528 lands, reconcile onto fresh canonical main and rerun every required exact-head gate before merge.

## Required verification
1. `npm --prefix tools/agent-control run check`.
2. `npm --prefix tools/agent-control test`.
3. Security Supply Chain Gate, Agent Control PR Gate, and Workflow Feature PR Gate on the exact integration head.
4. Live heaven2 browser smoke after canonical runtime refresh:
   - all overview cards start closed;
   - summaries/counts are visible without scrolling through notification bodies;
   - opened panel expands full-width and long content scrolls internally;
   - severity totals are useful while Notifications is closed;
   - managed/federated/supported-notification Inspect opens Inspector;
   - normal auto-refresh preserves the DOM-native open/closed state and selected Inspector.

## Successor improvements / risks
- Persist per-user section preferences only if real operator use justifies it; native `details` state already survives in-page data refresh.
- Avoid global expand-all unless requested; it defeats the glanceable/noise-reduction goal.
- Do not claim Agent Manager P0 lifecycle completion from this presentation-only patch.
- Preserve recursive continuity and unresolved provider/auth/signing work.
