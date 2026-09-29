# Agent Manager / Agent Control Plane 0.5.1 — 2026-09-29

## Scope

The Agent Manager implementation lives under `tools/agent-control/`. PR #59 / `feature/agent-control-plane-v2-20260928` is the integration lineage for the 0.5.0 federated control-plane work. This checkpoint exists so the next agent does not need chat history to reconstruct the architecture or safety contract.

The repository-wide `GLOBAL_GIT_DIRECTIVE.md` applies: this work is not complete until the validated candidate is reconciled with current canonical `main`, integrated, pushed, and verified on remote `main`.

## 0.5.1 liveness / scheduler contract

- Managed-worker capacity now uses the same freshness thresholds as the federated registry instead of treating stale local status records as active capacity.
- Fresh `working`, `tool_wait`, `blocked`, and `idle` participants are live; stale, disconnected, done, and failed records remain inspectable but do not consume live slots.
- Exact task IDs are rejected before dispatch when a fresh local or federated agent already owns the task, in addition to mutable-boundary lease checks.
- Heavy-work `auto` placement from `heaven2` deterministically selects `heaven` when policy prefers it. Authenticated transport unavailability is an explicit placement failure; there is no silent fallback to `heaven2`.
- Liveness/scheduler tests use injected timestamps/health rather than sleeps so fresh, stale, disconnected, reconnect/restart, routing-freshness, capacity, and placement behavior remain deterministic.

## Shipped architecture contract

- `heaven2` is the control plane and credential authority.
- `heaven` is the preferred heavy worker for builds, tests, Codex workers, indexing, worktrees, and automation.
- Authenticated cross-machine execution uses the private Heaven Local Bridge protocol `chatgpt-heaven-bridge-v2` and the dedicated `heaven-bridge` relay branch.
- Agent Control fails closed if the authenticated bridge or a fresh `heaven` heartbeat cannot be proven. It must not silently run requested heavy work on `heaven2`.
- The bridge-backed worker is represented as provider `heaven-bridge`; local controller-owned execution is provider `local-control`.
- The controller keeps ownership of the local runner process, while the runner submits authenticated remote jobs, validates authoritative result identity/host/protocol/status, transfers remote patches back, commits/pushes from the credential-authority side, and relays structured verification/review evidence back to loopback-only Agent Control APIs.
- Remote stop requests require an authoritative cancellation result for the owned bridge job before the local runner is terminated and its lease can be released.

## Federated registry / heartbeat contract

The normalized registry supports local controller workers plus provider observations for Heaven, ChatGPT sessions, GitHub/CI work, and heaven2 control state.

Normalized lifecycle states are:

`working`, `tool_wait`, `blocked`, `idle`, `done`, `failed`, `disconnected`.

Only fresh non-terminal live states contribute to Active Agents. Historical completed/failed observations, stale observations, and disconnected observations remain inspectable but do not inflate the live count.

Identity uses stable provider/source identity and explicit strong correlation keys. Similar titles are display metadata only and must not merge simultaneous ChatGPT sessions. Reconnects and duplicate observations are idempotently reconciled.

Automatic ChatGPT project/session enumeration is not available through the current API surface. ChatGPT therefore uses the implemented bridge/manual registration path; do not fake telemetry. GitHub/CI and heaven2 external observations also use explicit bridge registration where automatic discovery is unavailable.

## Local / remote Codex safety

The validated local and remote Codex contract uses approval-never workspace-write sandboxing:

`codex -a never -s workspace-write ... exec ...`

Do not reintroduce `--approve-for-me`, `danger-full-access`, or bypass-dangerously forms.

Remote workers on `heaven` do not push or publish. They leave source changes in the isolated remote workspace. The control-side runner transfers a binary Git patch back to the controller-owned worktree on `heaven2`, applies/commits it there, and only then pushes the task branch from the credential-authority side.

## Operator / autonomy contract

Server-side autonomy authorization remains authoritative; UI labels are not authorization.

Manual first-party dashboard and `agentctl.mjs` Deploy/Review operations explicitly authorize the scoped remote repository work created by that operator action. Governed engineering-autopilot implementation/verification/review/repair dispatches also explicitly authorize only their scoped remote work.

Unknown autonomy profiles fail closed. Safety controls remain available regardless of autonomy level. Stop actions only terminate proven owned processes.

## Verification evidence before final integration

During stabilization, the exact candidate `6c49852a72ba23d50cfca6ac77b1104c37728aa4` was reconciled to its then-current `main` and passed Agent Control PR Gate run `36548528019` on both `windows-latest` and `ubuntu-latest`: 71 tests / 71 pass / 0 fail on each platform, with syntax checks green.

A live private-relay acceptance on product head `085781aa57bc3bec3d07004cb72c7a5b2165c59f` returned `host: heaven`, protocol `chatgpt-heaven-bridge-v2`, status `done`, exit code 0, proving execution occurred on the heavy worker. Subsequent changes through `6c49852a...` were operator-control regression coverage only.

These are checkpoint facts, not permission to reuse stale evidence. If `main` or the PR head advances, reconcile and rerun affected exact-head gates before integration.

## Core liveness hardening checkpoint

Canonical `main` commit `486b909d9a92c1dc120fb28a37ce49d989bd1c59` closes a managed-worker liveness gap: a non-terminal local record with no heartbeat/update/start/create timestamp now fails closed as `disconnected` and cannot consume live capacity or ownership. Regression coverage explicitly proves both the missing-heartbeat state and capacity behavior, and existing tests that model active local workers now provide fresh heartbeat evidence.

Focused verification on the exact patched liveness module used Node 22 syntax checking plus deterministic missing/fresh/stale/capacity assertions and passed. Hosted Agent Control PR Gate run `36555647454` did **not** execute source verification: both `ubuntu-latest` and `windows-latest` jobs received no runner (`runner_id: 0`), executed zero steps, produced no logs, and failed before checkout. Do not misreport that run as a source/test failure or as passing evidence.

## Current known limitation

The intended clean limitation is provider discovery, not fake support: automatic enumeration of external ChatGPT sessions is unavailable. The normalized provider/registration contract exists so supported bridge observations can be registered and reconciled honestly.

## Continuity obligation

The successor must preserve this architecture and the permanent repository continuity constitution, re-read canonical `main`, re-query PR/issue ownership, and recursively pass the same obligation to the agent after them. Do not call Agent Manager shipped until remote canonical `main` is verified to contain the final tested integration.
