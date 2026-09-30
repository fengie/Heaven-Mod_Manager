> **Planning authority notice:** current feature status, priority, ownership, and progress are centralized in [`_AGENT_CONTEXT/PROJECT_PLAN.md`](../../_AGENT_CONTEXT/PROJECT_PLAN.md). This plugin plan is design/history detail only for current-status purposes.

# Local RDC Replacement Plan

## Trigger

On 2026-09-29 the Remote Desktop Commander connector was confirmed online at the device-registration layer but its command execution path was usage-paused. That made the external plugin unavailable for a local storage audit even though `heaven2` itself was still present.

## Goal

Provide a stable local replacement route for the useful Remote Desktop Commander capability surface by adapting existing Heaven Local Bridge/plugin primitives. The result should allow future agents to route locally as soon as the external connector is unavailable.

## Capability map

| Upstream-style capability | Local owner |
| --- | --- |
| device/host presence | Heaven bridge + control-plane health |
| filesystem read/list/metadata | `heaven-file-ops` |
| command/process execution | `heaven-control-plane` + `heaven-process-services` |
| service/process lifecycle | `heaven-process-services` |
| display/window/input/screenshot | `heaven-desktop` |
| browser/UI automation | `heaven-browser` |
| system/package/network operations | `heaven-system-ops` |
| persistent job/result transport | existing `heaven-bridge/` relay |

## Milestones

### M0 — project start
- [x] durable plan committed;
- [x] compatibility package created;
- [x] machine-readable replacement manifest created;
- [x] current outage mapped to existing local providers.

### M1 — storage-audit parity
- [ ] expose one structured read-only storage-audit workflow through the local control plane;
- [ ] return drive capacity, top user/profile directories, large cache/build/download roots, and largest files;
- [ ] bound recursion/output and surface access-denied paths rather than silently dropping them;
- [ ] verify on `heaven2` and `heaven`.

### M2 — routing/discovery
- [ ] make Agent Control/toolbox discovery advertise this compatibility provider;
- [ ] when Remote Desktop Commander is proven unavailable, prefer this local provider automatically for supported operations;
- [ ] never infer external-plugin failure from stale cache alone;
- [ ] record the outage and selected local provider in `PLUGIN-PREFLIGHT`.

### M3 — parity closure
- [ ] inventory the remaining Remote Desktop Commander operations actually used by this repository;
- [ ] map each to an existing Heaven provider or add only the missing thin adapter;
- [ ] add compatibility tests for routing, health semantics, structured error mapping, and result bounds;
- [ ] document unsupported operations explicitly.

## Acceptance criteria

- A Remote Desktop Commander usage/quota pause no longer blocks read-only filesystem/system/process/desktop tasks already supported by Heaven.
- External connector unavailability causes no retry storm.
- Existing Heaven primitives are reused rather than forked.
- Storage audit works without the external connector.
- Host presence and control-transport health remain distinct.
- No secrets are persisted or returned accidentally.
- The adapter cannot be used to bypass third-party authentication, quotas, or service controls.
- Completion is verified on canonical remote `main` and the backlog entry is updated with exact evidence.
