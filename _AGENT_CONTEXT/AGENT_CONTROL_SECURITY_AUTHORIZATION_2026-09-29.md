# Agent Control Security & Authorization Closure — 2026-09-29

## Scope

Security/Authorization Lead review and implementation for `tools/agent-control/`.

Canonical security implementation merge: `9a051815882345c3b25f4eae8681002ff3a70f52` (PR #106).

This record describes the reviewed security boundary and exact evidence. It does not claim the entire repository or product is secure.

## Confirmed findings and fixes

### HIGH — client/provider supplied repository-write privilege input

The controller accepted a request-body `repositoryWriteAuthorized` flag on operator execution routes. That made a high-impact machine-policy authorization decision depend on client-supplied privilege input.

Fix:
- operator execution routes now derive the scoped repository-write authorization on the server;
- direct deploy, workflow execution, review dispatch, sync and self-improvement no longer rely on the request-body privilege bit for the authorization decision;
- the internal execution seam still receives an explicit server-supplied authorization parameter for machine-policy enforcement.

### HIGH — cross-task structured evidence / review-verdict spoofing

Structured verification evidence and integration review verdicts were protected by autonomy profile permissions but were not cryptographically scoped to the worker task producing them. Under a permissive controller profile, another local caller could attempt to write evidence for a different task/candidate.

Fix:
- each deployed task receives a 256-bit random capability;
- only the SHA-256 capability digest is persisted in control-plane state;
- the raw capability is passed only in the controller-owned child environment;
- verification evidence requires the exact task capability;
- review verdicts require the capability of the reviewer assigned to that exact candidate;
- reviewer identity is derived server-side instead of trusting a caller-supplied `by` field;
- the Heaven bridge remote worker is not given the capability; the controller-side bridge runner authenticates the loopback evidence relay.

### MEDIUM — concurrent stop and stale child identity hardening

Stop safety already required current controller-session ownership, the tracked child object, matching PID, and authoritative Heaven cancellation for remote work. Concurrent stop attempts and a ChildProcess already known to have exited were not separately rejected before process-tree termination.

Fix:
- concurrent stop operations are deduplicated per agent;
- process ownership proof now also requires the tracked child to have no exit code and no signal code before termination;
- uncertain ownership remains fail-closed and leases remain preserved when termination cannot be proven.

## Files changed by the security implementation

- `tools/agent-control/server.mjs`
- `tools/agent-control/lib/heaven-bridge-runner.mjs`
- `tools/agent-control/test/server-safety.test.mjs`
- `tools/agent-control/test/heaven-bridge-provider.test.mjs`

## Verification evidence

Superseded pre-scheduler candidate `620e40a4d3f8ebc340330ab867268c6d79199ad6`:
- Agent Control PR Gate run `36552664684`
- Ubuntu: PASS
- Windows: PASS

Reconciled security candidate `8c5baab33866eda6c6110f828dd39c35b29a3e6c`:
- Agent Control PR Gate run `36553102200`
- Ubuntu: PASS
- Windows: PASS

PR #106 merged the reconciled candidate as `9a051815882345c3b25f4eae8681002ff3a70f52`.
Remote main was then inspected and confirmed to contain the scoped task capability checks, authenticated bridge relay, server-derived operator repository-write authorization, concurrent-stop deduplication, and stronger live-child ownership proof.

A local Heaven verification command was attempted before CI but Desktop Commander reached its monthly usage limit before command execution. No local test pass is claimed from that attempt.

## Reviewed trust boundaries

- autonomy profile enforcement and fail-closed unknown profile behavior;
- operator deployment / workflow machine-policy authorization;
- worker-to-controller structured evidence mutation;
- reviewer-to-candidate verdict authorization;
- controller-owned child process identity;
- remote Heaven cancellation and local runner termination ordering;
- loopback-only server exposure and same-origin browser restriction;
- secret/capability persistence and relay scope.

## Remaining risks / follow-up boundaries

- The control plane intentionally trusts the local OS account / loopback boundary for operator control. If protection against other same-user local processes becomes a requirement, add an authenticated operator session/capability boundary rather than relying only on loopback.
- Continue testing PID reuse, already-exited processes, concurrent stop, restart/orphan recovery, and remote cancellation failures whenever process-lifecycle code changes.
- Continue treating external provider observations as telemetry, not authorization.
- Do not log, persist, or transmit raw task capabilities beyond the controller-owned child environment.

## Release gate

Security implementation is eligible only with exact-head Agent Control Linux/Windows gate evidence and verified presence on remote canonical `main`. Documentation-only closure changes do not alter shipped application version identity.
