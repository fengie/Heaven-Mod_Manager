# Agent Manager compact overview — v8.8.50 candidate

## Scope completed
- Replaced the long vertical operational stack with 10 native default-collapsed `<details>` cards: machine pool, notifications, inspector, registry history, managed agents, federated registry, leases, integration queue, branches, and recent events.
- Collapsed cards form a two-column at-a-glance overview; any opened card expands full-width.
- Expanded bodies are height-bounded and scroll internally, so large notification/agent/event sets do not bury the rest of the dashboard.
- Managed/federated/notification inspection automatically opens the Inspector.
- Notification summary includes critical/warning counts and rendering stays bounded to the newest 30 items.
- Added focused operator UI regression coverage.
- Agent Control/plugin identity is 0.6.23. Root product patch is reserved as 8.8.50 because active PR #526 owns v8.8.49.

## Integration / ownership
- Working branch: `fix/agent-manager-compact-ui-v8.8.50-20260930`.
- Base at branch creation: main `5152771d7ae45bade8c624945c4447cd2ec24868`.
- PR #526 is the active v8.8.49 UI-fix owner and overlaps version/README/CHANGELOG/continuity metadata, not Agent Manager source.
- Do not merge this branch ahead of #526 without reconciling version/history intentionally. After #526 resolves, fetch fresh main, reconcile this branch, rerun exact-head checks, then merge/push/verify remote main.

## Required verification
1. `npm --prefix tools/agent-control run check`.
2. `npm --prefix tools/agent-control test`.
3. Required Agent Control / security PR gates on the exact reconciled head.
4. Live heaven2 browser smoke after canonical runtime refresh:
   - all overview cards start closed;
   - all counts/status pills are visible without traversing notification content;
   - opening a card expands it full-width and long content scrolls inside it;
   - notification severity totals are visible while collapsed;
   - managed, federated, and supported notification Inspect actions open the Inspector automatically;
   - auto-refresh preserves the user's open/closed details state and selected Inspector.

## Improvement opportunities / risks
- Consider optional user-persisted open/closed preferences only if operators ask for it; native details state already survives ordinary in-page refresh because the DOM nodes are not replaced.
- Do not add a global expand-all control unless real usage shows it helps; the current goal is reduced visual noise.
- Keep backend lifecycle/ownership semantics unchanged; this change is presentation-only.
- Preserve recursive continuity and pass this handoff to the next agent.
