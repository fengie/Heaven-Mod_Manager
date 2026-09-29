# Agent Control Federation / Registry v2 — 2026-09-29

## Status and scope

This checkpoint is the Federation & Registry Lead implementation for Agent Control 0.5.1. It changes only the engineering control-plane tooling under `tools/agent-control/` plus repository continuity. It does not change the shipped WPF mod-manager executable or updater payload, so the root application version remains v8.8.6.

The implementation lineage is PR #101, branch `agent/federation-registry-20260929`. The branch was created from canonical `origin/main` at `ad3de9e78232fe9f66dace188c3e31dd4ced7a7c` and repeatedly reconciled as concurrent integration advanced canonical main. The final pre-merge canonical base recorded by this checkpoint is `9a051815882345c3b25f4eae8681002ff3a70f52`. Federation source stayed byte-identical across the later races; an upstream federation-test expansion was preserved and combined with the v2 invariant suite rather than overwritten.

## Schema / contract

Federation schema is now version **2**.

The logical agent model persists:

- stable `agent_id`;
- current provider plus all observed providers;
- provider/source identity (`provider`, `source_id`);
- runtime/provider runtime;
- session identity and conversation identity when available;
- role and machine;
- parent manager;
- task ID / task display metadata;
- branch and PR association;
- heartbeat timestamp and normalized lifecycle state;
- last-action timestamp / summary;
- created / finished timestamps;
- namespaced strong correlation keys;
- opaque `source_metadata`;
- per-provider/source observations.

The public provider abstraction is an AgentSource-style adapter exposed through `agentSourceAdapter(...)`:

- provider capability metadata;
- `discover` only when discovery is actually automated and a discovery callback exists;
- `normalize`;
- `ingest`;
- `reconcile`;
- provider `heartbeat`.

Unsupported discovery fails explicitly; the registry never fabricates provider enumeration.

## Provider support matrix

| Provider | Kind | Discovery | Registration / ingest | Notes |
| --- | --- | --- | --- | --- |
| `local-control` | local worker | automated | automated | Controller-owned managed agents are synchronized automatically. |
| `heaven-bridge` | remote worker | automated | automated | Authenticated Heaven Local Bridge represents supported heavy-worker execution. |
| `chatgpt` | ChatGPT session | unavailable | bridge/manual | Automatic ChatGPT project/session enumeration is not available; stable bridge observations are supported. |
| `github` | GitHub / CI | bridge | bridge | Stable run/workflow observations may be registered; PR/task metadata alone is not logical identity. |
| `heaven2-control` | control machine | bridge | bridge | heaven2 remains control/credential authority and may publish control-plane observations. |
| legacy persisted provider with no installed adapter | external | unavailable | unavailable | Preserved as `unsupported` for inspection only; cannot ingest new observations. |

## Identity and reconciliation invariants

Identity is fail-closed.

1. Exact `provider + source_id` is stable source identity and duplicate observations are idempotent.
2. Cross-provider merge requires explicit, namespaced strong evidence such as logical-agent, controller-agent, ChatGPT session, GitHub workflow-run, bridge-job, or work-item identity.
3. Task IDs, PR numbers, branches, titles, roles, machine labels, and GitHub PR metadata are not identity keys. Supplying them as correlation namespaces is rejected.
4. Similar display metadata never merges simultaneous sessions.
5. A correlation that matches more than one logical agent is rejected as ambiguous rather than guessing.
6. An existing explicit `agent_id` cannot be rebound to a new provider/source without exact-source or strong-correlation evidence.
7. Older provider/source observations are accepted as historical no-ops for that source and cannot regress a newer aggregate heartbeat/lifecycle.
8. `done` and `failed` remain historical; only fresh `working`, `tool_wait`, `blocked`, and `idle` states count live.
9. Unknown providers and persisted providers without an installed adapter cannot ingest fresh observations.
10. Malformed dates, PR numbers, metadata, correlation shapes, provider-health states, and unsafe correlation namespaces fail safely.

## Persistence / migration

`migrateFederationState` upgrades v1 or partially malformed persisted registry state to v2 without destructive reset.

- Default installed provider definitions are restored.
- Persisted provider health/metadata is normalized.
- Unknown legacy providers referenced by persisted data are retained as `unsupported`, not silently promoted to working adapters.
- Persisted observations are normalized and duplicate provider/source records collapse to the newest heartbeat.
- Duplicate persisted logical-agent rows with the same `agent_id` reconcile deterministically, preserving provider/correlation/source metadata.
- Runtime/session/conversation/source metadata survives reload.
- Invalid persisted lifecycle values degrade to `disconnected` rather than manufacturing a live worker.
- Stale/disconnected thresholds are normalized and preserved when valid.

No destructive registry reset is required for the schema upgrade.

## Verification

Required exact-head gate: **Agent Control PR Gate**, Node 22, syntax checks plus `node --test test/*.test.mjs` on both Ubuntu and Windows.

- Reconciled implementation checkpoint `580d11338cb37a017952ae8737430db34742cfee`: GitHub Actions run **36552185691** passed on both `ubuntu-latest` and `windows-latest`.
- Reconciled Agent Control 0.5.1 + liveness checkpoint `61ce8f5124495eccbbc3348547f6e063fccd9f3d`: GitHub Actions run **36553004754** passed on both `ubuntu-latest` and `windows-latest`.
- Reconciled checkpoint `e13b6ba9fb5cc52c079e6f52749634c973f292fa`, which combines the concurrent upstream federation-test expansion with the v2 invariant suite: GitHub Actions run **36553210697** passed on both `ubuntu-latest` and `windows-latest`.
- The branch is reconciled again onto canonical base `9a051815882345c3b25f4eae8681002ff3a70f52`. The resulting final PR head must pass the same exact-head matrix gate before merge; do not infer final verification solely from the earlier green checkpoints.
- Local Heaven test execution could not be used after the Remote Desktop Commander monthly tool quota was exhausted; no local-pass claim is made from that path.

Regression coverage now explicitly proves:

- duplicate identical provider/source observations are idempotent;
- reconnect preserves logical identity;
- simultaneous distinct ChatGPT sessions remain distinct;
- provider/source identifier changes do not merge unrelated agents from task/PR metadata;
- explicit strong cross-provider correlation can preserve one logical identity;
- stale/late observations cannot regress a newer live agent;
- ambiguous correlation fails closed;
- GitHub task/PR associations are not identity keys;
- persisted reload/migration preserves logical identity;
- unsupported persisted providers remain non-ingestable;
- malformed/unknown provider data fails safely;
- AgentSource discovery capability is represented honestly.

## Interfaces other leads consume

- Liveness/Scheduler: continue deriving freshness/live capacity from `federationSnapshot`; no task/PR metadata may be interpreted as identity.
- UI/CLI: provider capability and unsupported status are explicit; display metadata remains display-only.
- Core Implementation: ingest external observations through normalization/reconciliation rather than writing agent rows directly.
- Verification/Reliability: use the v2 invariants above as the minimum federation regression suite.
- Security/Authorization: federation registration represents observations/identity only; it does not grant process authority or autonomy permissions.

## Remaining provider limitations

- Automatic ChatGPT project/session enumeration is still unavailable and must not be faked.
- GitHub/CI and heaven2 external observations require supported bridge/manual registration unless a real automated adapter is added later.
- Provider health proves provider availability, not authority to stop/kill arbitrary external processes.
- The registry does not infer identity from titles, tasks, PRs, branches, or other convenient but unsafe metadata.

## Continuity obligation

Before any successor changes Agent Control, re-establish current canonical `origin/main`, inspect PR/branch ownership, read this checkpoint plus `_AGENT_CONTEXT/AGENT_CONTROL_PLANE_2026-09-29.md`, preserve the permanent continuity constitution, and recursively pass the same obligation to the agent after them. Finished Federation/Registry work belongs on verified remote `main`, not a stranded task branch.
