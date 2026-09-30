# Agent Control registry retirement — 2026-09-30

## Objective

Remove dead failed work from Agent Control's active managed/federated registries while preserving bounded retry behavior, live process ownership, durable task history, and substantive partial-work evidence.

## Canonical source and candidate

- Canonical repository: `fengie/mhw-mods`
- Canonical branch/base: `main` at `7c5a03abeafe05b0afe51bdcec7b3dcfa8fbc26e`
- Candidate branch: `fix/agent-control-registry-retirement-20260930`
- Candidate source after implementation hardening: `32633ccf` (`fix(agent-control): preserve retry lineage during registry cleanup`)
- Branch pushed to origin for durable recovery; subsequent release/continuity edits may advance the branch beyond the implementation checkpoint above.
- Root patch release target: v8.8.26; Agent Control package and both private plugin manifests: v0.6.5.

## Diagnosis and implementation

Dead failed records were retaining space in the live registry. Existing retry lineage also feeds the swarm-tail planner's per-root attempt count, so removing a `retry-dispatched` record can reset the bounded retry ceiling. The generic managed-retirement path separately needed proof that a child owned by the current controller is no longer alive.

The candidate integrates retry-exhausted lineage cleanup and deterministic dead-record pruning, while preserving tasks, events, and substantive failed-work evidence. Retry-dispatched, retry-pending, and retrying states stay in the registry until the root is exhausted or retries are disabled. Generic process cleanup checks current-session child identity, pid equality, child exit/signal state, and OS pid liveness before retirement. Lease release remains tied to the retired managed record.

## Verification completed

- `node --test tools/agent-control/test/*.test.mjs` — 225/225 passing locally after the implementation hardening checkpoint `32633ccf`.
- `node --test tools/agent-control/test/server-safety.test.mjs` — 34/34 passing, including a real isolated server startup against seeded persisted control state.
- `git diff --check` — passed before implementation commit.
- Runtime syntax coverage is included in the package's `check` script. Running the npm launcher in this environment failed because its configured global npm CLI path is missing; direct Node test execution works.

## Durable regression and rule

- `tools/agent-control/test/no-work-recovery.test.mjs`: retry state remains until exhaustion/disablement.
- `tools/agent-control/test/server-safety.test.mjs`: real startup removes exhausted dead root, keeps dispatched lineage, releases its lease, and retains task/event/substantive failure evidence.
- `tools/agent-control/test/operator-ui-cli.test.mjs`: generic retirement requires proving a managed current-session child is dead.
- `_AGENT_CONTEXT/LEARNED_RULES.md`: LR-050.
- `_AGENT_CONTEXT/BUG_PRECEDENTS.md`: cleanup regression precedent.

## Still required — do not mark Agent Manager P0 complete

1. Run the package's exact-head `npm run check && npm test` (or equivalent direct Node commands if the npm installation remains unavailable) after all release/continuity edits and record the final full SHA.
2. Obtain hosted exact-head checks for the pushed final candidate.
3. On heaven2, smoke controller startup, server/CLI/dashboard, registry visibility, plugin v0.6.5 identity, and safe operator controls.
4. Prove the delegated heaven1 worker route through runtime host `heaven`, or show a specific fail-closed provider-health reason.
5. Exercise START SWARM/perpetual orchestration for dispatch, federated visibility, recovery, explicit stop, and unique mutable-boundary ownership.
6. Heartbeat a real stable ChatGPT session if the runtime exposes one; never synthesize an identity.
7. Preserve Agent Manager P0 as active until every required gate has exact-head evidence.

## Successor startup gate

Before further changes, read root `AGENTS.md`, `_AGENT_CONTEXT/NEXT_STEPS.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, `_AGENT_CONTEXT/CURRENT_STATE.md`, LR-050, and the Agent Manager P0 gates. Re-fetch canonical `origin/main`, check branch/PR ownership, and bind every new verification result to the exact tested SHA. Do not merge stale sibling branches wholesale; the current candidate was rebuilt from the exact canonical main base.
