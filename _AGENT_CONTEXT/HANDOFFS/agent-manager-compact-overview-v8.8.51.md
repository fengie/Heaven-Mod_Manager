# Agent Manager compact overview — v8.8.51 stacked candidate

## Scope
- Ten high-volume operational surfaces default to collapsed native `<details>` cards: machine pool, notifications, inspector, registry history, managed agents, federated registry, leases, integration queue, branches, and recent events.
- Closed cards form a responsive two-column overview. Opening one expands it full-width.
- Expanded content is bounded and scrolls internally instead of pushing the rest of Agent Manager off-screen.
- Managed/federated/notification inspection automatically opens Inspector.
- Notification summary exposes critical/warning counts while the list stays bounded to the newest 30 rendered entries.
- Interaction polish adds short compositor-friendly card/metric/toast/button motion, one-time staggered card entry, OS/browser reduced-motion handling, and hidden-page guards rather than continuous animation.
- Optional UI audio is local Web Audio synthesis, off until the operator enables it (or retains a previous opt-in), persisted in localStorage, and limited to direct clicks/toggles so refresh polling stays silent.
- Backend lifecycle, ownership, routing, recovery, and action semantics are intentionally unchanged.

## Lineage / coordination
- Target product patch: v8.8.51; Agent Control/plugin: v0.6.24.
- Parent baseline: PR #528 v8.8.50 governance head `31307b621f3d58855389644c8c92c98022c47639`.
- This is intentionally stacked so the UI does not overwrite the governance refactor. Once #528 lands, reconcile onto fresh canonical main and rerun every required exact-head gate before merge.

## Current exact checkpoint
- UI motion/audio implementation commit: `df4f1f3815cd2f6cd9fa30e6597535134395ee90`.
- Latest governance evidence was reconciled, then the current v8.8.50 governance head `5511155ec067f48f0744b20c24ea250554367243` was recorded as a second parent by merge commit `9950ced358b3cde55be5bd2ba82dac678f2674e4`; branch comparison is now behind 0.
- PR #531 is intentionally **closed without merge** for queue serialization. Its closure comment records that open stacked PRs were canceling PR #528's global Workflow Feature gate. Do not reopen until #528 reaches canonical main; the branch/work are preserved.
- Source-level verification on the reconciled tree: inline dashboard JavaScript parses; 10 compact sections are default-closed; reduced-motion and hidden-page guards are present; no persistent `translateZ(0)`, requestAnimationFrame loop, or refresh-triggered UI sound exists; metric motion short-circuits when values do not change.
- Full Node/CI and live heaven2 browser acceptance still belong to the post-#528 exact candidate and are not claimed by this checkpoint.

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
   - normal auto-refresh preserves the DOM-native open/closed state and selected Inspector;
   - UI sound starts off for a fresh profile, persists after explicit opt-in, and periodic refresh remains silent;
   - reduced-motion suppresses transition work, and metric changes do not animate while the page is hidden.

## Successor improvements / risks
- Persist per-user section preferences only if real operator use justifies it; native `details` state already survives in-page data refresh.
- Avoid global expand-all unless requested; it defeats the glanceable/noise-reduction goal.
- Do not claim Agent Manager P0 lifecycle completion from this presentation-only patch.
- Preserve recursive continuity and unresolved provider/auth/signing work.
