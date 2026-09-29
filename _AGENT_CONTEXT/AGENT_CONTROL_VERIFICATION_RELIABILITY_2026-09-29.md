# Agent Control Verification / Reliability Checkpoint — 2026-09-29

## Scope

Role: Verification, Stress & Reliability Lead.

This checkpoint is test/evidence work for `tools/agent-control/`. It does not change shipped application behavior and does not require an application version bump.

Task-start canonical `origin/main` observed locally: `ad3de9e78232fe9f66dace188c3e31dd4ced7a7c`.
Hosted verification branch was created from later canonical `main` `b31de615346a8bf8281a1a1e1055e092f9bdbe9a` and has been repeatedly reconciled as `main` advanced.

## Existing-suite audit

The Agent Control suite already exercised control-state migration, swarm/routing ownership, autonomy, authoritative process exit, deployment capacity, workflow leases, federation identity/liveness, Heaven bridge result validation, server persistence recovery, and autopilot recovery/transition behavior.

The federation tests were read at assertion level rather than inferred from their names. They proved same-source idempotence, explicit cross-provider correlation, separation of simultaneous uncorrelated ChatGPT sessions, fresh/stale/disconnected live-count semantics, terminal historical exclusion, reconnect identity stability, and mixed managed/external counts.

Canonical `main` later added Agent Control 0.5.1 liveness-scheduler coverage. Its test bodies were inspected before reconciliation. They prove managed-heartbeat freshness, stale/disconnected exclusion, terminal-history exclusion, live-only capacity, deterministic Heaven placement, fail-closed transport behavior, routing-manifest freshness, duplicate local/federated task ownership rejection, and restart behavior with stale owners.

This checkpoint does not claim exhaustive new runtime fault injection for process kill/stop, persistence write interruption, or lease mutation. Those areas remain covered only to the extent of the pre-existing suite unless separately evidenced.

## Added deterministic verification

### Persisted federation migration normalization

Invariant: malformed nested persisted collections must normalize to safe shapes, duplicate identity keys must deduplicate, and the disconnected threshold must never become shorter than the stale threshold.

Failure simulated: malformed provider metadata, non-array observations, duplicate/blank identity lists, and inverted stale/disconnected thresholds.

Why the prior suite was insufficient: existing federation tests exercised live reconciliation but did not directly characterize malformed persisted federation state.

Reproduction: `cd tools/agent-control && npm test`. Fixed timestamps; no sleeps.

### Malformed observation failure atomicity

Invariant: rejecting an unsupported normalized state must not mutate federation state.

Failure simulated: an observation with an unsupported state value.

Why the prior suite was insufficient: validation was exercised indirectly, but no assertion compared the complete pre/post federation state after rejection.

Reproduction: `cd tools/agent-control && npm test`. Deterministic and in-memory.

### Conflicting explicit identity failure atomicity

Invariant: a conflicting explicit `agent_id` must fail closed without partially modifying the already-correlated logical agent.

Failure simulated: same provider/source and strong correlation key presented with a different explicit agent identity.

Why the prior suite was insufficient: prior correlation tests proved successful convergence, not failure atomicity under an identity conflict.

Reproduction: `cd tools/agent-control && npm test`. Deterministic and in-memory.

### Duplicate-provider reconnect storm

Invariant: repeated duplicate observations from two providers for hundreds of logical workers must remain bounded to the logical-agent cardinality and must not inflate live counts or observation count.

Failure simulated: five deterministic observation cycles for 200 logical agents, each observed through ChatGPT and GitHub with the same strong correlation key.

Why the prior suite was insufficient: prior tests proved idempotence for individual identities but not bounded behavior across a realistic reconnect/duplicate-provider burst.

Reproduction: `cd tools/agent-control && npm test`. Uses fixed timestamps and bounded loops; no arbitrary sleeps.

## Executed evidence before latest reconciliation

PR #99, head `7344346297b1c97601448433ef03d5c1982c3c5f`, Agent Control PR Gate run `36552196616`: syntax checks and deterministic tests passed on both Ubuntu and Windows; Windows reported 75 tests / 75 pass / 0 fail.

After reconciling then-current `main`, head `a47f8029c03f62fa9f260f1c397877901cbab620`, run `36552388694` passed both platforms; Windows reported 75 / 75 / 0 and explicitly passed all four new tests.

After another `main` reconciliation, head `972fe61422ea3e818370ca5354c4e7e9c5f75b6f`, run `36552591552` passed both platforms; Windows reported 75 / 75 / 0 and explicitly passed all four new tests. GitHub tested that PR head in a synthetic merge against then-current base `bba07a6f84016d813c33bee8fe690871c901f045`.

These run IDs are checkpoint evidence only. Because canonical `main` continued to advance afterward, the final delivery owner must rerun/confirm the Agent Control PR Gate on the latest reconciled PR head before merging.

## Earlier harness error

The first run, `36552041031`, failed on both platforms because the new migration test initially omitted the `migrateFederationState` import. The other three new tests passed in that run. The import was fixed in `7344346297b1c97601448433ef03d5c1982c3c5f`; no production defect was inferred from this harness error.

## Evidence classification

Proven by executed hosted tests: the four added invariants above on the exact PR merge candidates named in the executed evidence section; syntax checks; the full Agent Control deterministic suite on both Ubuntu and Windows for those candidates.

Partially tested: the broader verification matrix (process stop ownership, persistence write interruption, lease mutation races, provider outage, autopilot mid-transition fault injection, integration-queue mutation). Existing tests cover parts of these boundaries, but this checkpoint does not claim new exhaustive fault injection for them.

Reasoned only: no claim beyond what source/test inspection directly supports.

Blocked environment paths: the local Codex worker on `heaven` was quota-blocked before repository mutation. Direct local execution then continued until the Remote Desktop Commander monthly MCP call limit paused local calls. Hosted GitHub Actions therefore became the independent execution path. These limits did not justify skipping the hosted verification gate.

## Delivery rule

Do not mark this checkpoint DONE until PR #99 has been reconciled with current `origin/main`, the affected exact-merge Agent Control gate is green, the change is merged to `main`, and remote `main` is re-read to prove the test file and this report are present. Never manually mark verification green.


## Final delivery closure

Final reconciled verification branch head: `85ef0155db25beb207cf6e6b08473fd6841f6dce`.

Agent Control PR Gate run `36552960456` executed the PR as synthetic merge `00b8b925ad274ce4b2a89bb8ea12c5c95e7fa234` against then-current canonical base `38772a9bcf8547402de7f98ada0740d7a6aa070f`. Both Ubuntu and Windows jobs passed syntax checks and the deterministic test suite. Windows reported 85 tests / 85 pass / 0 fail. The four reliability tests above and the new Agent Control 0.5.1 liveness/scheduler tests were explicitly green.

Immediately before merge, remote `main` was re-read and was exactly `38772a9bcf8547402de7f98ada0740d7a6aa070f`, the base used by the successful exact-merge gate.

PR #99 was merged with a merge commit as `c83ce7a47fb8d9f82e132f46c5e6f7f3a5a80f20`. Remote `main` then advanced by one unrelated updater-workflow-only commit to `7a0f0d0d5f5bda4ebf97436572bc5a5f79b43c7b`, whose parent is the reliability merge. The current remote `main` was re-read after that race: both `tools/agent-control/test/federated-registry.test.mjs` with all four added tests and this verification checkpoint are present.

STATUS: DONE for implementation, executed verification, PR merge, and remote-main presence proof.

Temporary branch cleanup is the only administrative remainder: PR #99 is closed/merged, but the available GitHub connector exposes ref creation/update and not ref deletion, while the local Desktop Commander path is currently usage-capped. The branch `agent/verification-reliability-20260929` therefore remains safe-to-delete rather than being falsely reported deleted.
