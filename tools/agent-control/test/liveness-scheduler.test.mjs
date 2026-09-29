import test from "node:test";
import assert from "node:assert/strict";

import {
  defaultControlState,
  deploymentBatchCapacity,
  migrateControlState,
  planWorkflow,
  routingManifestStatus,
  workflowLeasePreflight
} from "../lib/control-core.mjs";
import {
  managedAgentLiveness,
  placementTransportDecision,
  resolveWorkerTarget
} from "../lib/liveness-scheduler.mjs";
import {
  federationSnapshot,
  reconcileObservation
} from "../lib/federated-registry.mjs";

function state() {
  return defaultControlState({ sessionId: "liveness-test", hostname: "heaven2" });
}

const T = Date.parse("2026-09-29T10:00:00.000Z");

test("fresh managed heartbeat is live and tool_wait remains live", () => {
  const fresh = managedAgentLiveness({
    status: "running",
    heartbeatAt: "2026-09-29T09:59:30.000Z"
  }, { now: T, staleAfterMs: 60_000, disconnectedAfterMs: 180_000 });
  const waiting = managedAgentLiveness({
    status: "waiting",
    heartbeatAt: "2026-09-29T09:59:45.000Z"
  }, { now: T, staleAfterMs: 60_000, disconnectedAfterMs: 180_000 });
  assert.equal(fresh.live, true);
  assert.equal(fresh.effectiveState, "working");
  assert.equal(waiting.live, true);
  assert.equal(waiting.effectiveState, "tool_wait");
});

test("managed records without heartbeat evidence fail closed as disconnected", () => {
  const missing = managedAgentLiveness({
    status: "running"
  }, { now: T, staleAfterMs: 60_000, disconnectedAfterMs: 180_000 });
  assert.equal(missing.freshness, "disconnected");
  assert.equal(missing.effectiveState, "disconnected");
  assert.equal(missing.heartbeatAgeMs, null);
  assert.equal(missing.live, false);
});

test("stale and disconnected thresholds exclude managed agents from live counts", () => {
  const stale = managedAgentLiveness({
    status: "running",
    heartbeatAt: "2026-09-29T09:58:30.000Z"
  }, { now: T, staleAfterMs: 60_000, disconnectedAfterMs: 180_000 });
  const gone = managedAgentLiveness({
    status: "running",
    heartbeatAt: "2026-09-29T09:55:00.000Z"
  }, { now: T, staleAfterMs: 60_000, disconnectedAfterMs: 180_000 });
  assert.equal(stale.freshness, "stale");
  assert.equal(stale.live, false);
  assert.equal(gone.freshness, "disconnected");
  assert.equal(gone.effectiveState, "disconnected");
  assert.equal(gone.live, false);
});

test("done and failed managed records are historical and never live", () => {
  for (const status of ["done", "failed"]) {
    const result = managedAgentLiveness({
      status,
      heartbeatAt: "2026-09-29T09:59:59.000Z"
    }, { now: T });
    assert.equal(result.historical, true);
    assert.equal(result.live, false);
  }
});

test("capacity does not count a managed record without heartbeat evidence", () => {
  const current = state();
  current.agents.push({ id: "missing-heartbeat", status: "running" });
  assert.deepEqual(deploymentBatchCapacity(current, 1, 1, { now: T }), {
    count: 1,
    active: 0,
    maximum: 1,
    available: 1,
    allowed: true
  });
});

test("capacity counts only presently live managed agents", () => {
  const current = state();
  current.federation.stale_after_ms = 60_000;
  current.federation.disconnected_after_ms = 180_000;
  current.agents.push(
    { id: "live-1", status: "running", heartbeatAt: "2026-09-29T09:59:50.000Z" },
    { id: "live-2", status: "blocked", heartbeatAt: "2026-09-29T09:59:40.000Z" },
    { id: "stale", status: "running", heartbeatAt: "2026-09-29T09:58:00.000Z" },
    { id: "done", status: "done", heartbeatAt: "2026-09-29T09:59:59.000Z" }
  );
  assert.deepEqual(deploymentBatchCapacity(current, 2, 3, { now: T }), {
    count: 2,
    active: 2,
    maximum: 3,
    available: 1,
    allowed: false
  });
});

test("auto placement on heaven2 selects heaven when heavy-worker policy is enabled", () => {
  const current = state();
  const target = resolveWorkerTarget(current, "auto", { controllerHostname: "heaven2" });
  assert.equal(target.target, "heaven");
  assert.equal(target.remote, true);
  assert.equal(target.policy.preferredForHeavyWork, true);
});

test("unavailable heaven transport fails closed instead of falling back to heaven2", () => {
  const current = state();
  const decision = placementTransportDecision(current, "auto", {
    controllerHostname: "heaven2",
    heavenTransportHealthy: false,
    heavenTransportReason: "heartbeat-stale"
  });
  assert.equal(decision.allowed, false);
  assert.equal(decision.code, "transport-unavailable");
  assert.equal(decision.target, "heaven");
  assert.match(decision.reason, /refusing to silently execute heavy work on heaven2/i);
});

test("healthy heaven transport yields authenticated remote placement", () => {
  const current = state();
  const decision = placementTransportDecision(current, "auto", {
    controllerHostname: "heaven2",
    heavenTransportHealthy: true
  });
  assert.equal(decision.allowed, true);
  assert.equal(decision.provider, "heaven-bridge");
  assert.equal(decision.remote, true);
});

test("expired routing manifest is explicitly stale", () => {
  const current = state();
  current.settings.routingManifest = {
    source: "test",
    mode: "authoritative",
    observedAt: "2026-09-29T09:00:00.000Z",
    expiresAt: "2026-09-29T09:30:00.000Z",
    assignments: []
  };
  const status = routingManifestStatus(current, T);
  assert.equal(status.available, true);
  assert.equal(status.current, false);
  assert.equal(status.reason, "routing-manifest-expired");
});

test("duplicate task ownership is rejected for live local and federated owners", () => {
  const local = state();
  local.agents.push({
    id: "local-owner",
    status: "running",
    taskId: "task-owned",
    heartbeatAt: "2026-09-29T09:59:50.000Z"
  });
  const localResult = workflowLeasePreflight(local, [
    { taskId: "task-owned", boundary: "new-boundary" }
  ], { now: T });
  assert.equal(localResult.allowed, false);
  assert.equal(localResult.taskId, "task-owned");

  const remote = state();
  reconcileObservation(remote.federation, {
    provider: "chatgpt",
    source_id: "remote-owner",
    state: "working",
    task_id: "task-remote",
    heartbeat_at: "2026-09-29T09:59:50.000Z"
  });
  const remoteResult = workflowLeasePreflight(remote, [
    { taskId: "task-remote", boundary: "other-boundary" }
  ], { now: T });
  assert.equal(remoteResult.allowed, false);
  assert.equal(remoteResult.taskId, "task-remote");
});

test("restart with old heartbeat data does not resurrect stale owners or inflate capacity", () => {
  const migrated = migrateControlState({
    version: 5,
    federation: {
      stale_after_ms: 60_000,
      disconnected_after_ms: 180_000,
      agents: [{
        agent_id: "remote-old",
        provider: "chatgpt",
        providers: ["chatgpt"],
        state: "working",
        heartbeat_at: "2026-09-29T09:50:00.000Z",
        correlation_keys: ["source:chatgpt:remote-old"],
        observations: []
      }]
    },
    agents: [{
      id: "local-old",
      role: "main",
      status: "running",
      heartbeatAt: "2026-09-29T09:50:00.000Z"
    }]
  }, { sessionId: "restart", hostname: "heaven2" });

  assert.equal(deploymentBatchCapacity(migrated, 1, 1, { now: T }).active, 0);
  assert.equal(federationSnapshot(migrated.federation, { now: T }).counts.live, 0);

  const plan = planWorkflow("usual-swarm", {
    state: migrated,
    mission: "Resume current implementation",
    machine: "heaven",
    now: T
  });
  assert.equal(plan.steps.filter(step => step.role === "main").length, 1);
});
