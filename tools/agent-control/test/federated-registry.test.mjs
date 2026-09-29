import test from "node:test";
import assert from "node:assert/strict";

import {
  DEFAULT_PROVIDER_DEFINITIONS,
  defaultFederationState,
  federationSnapshot,
  reconcileObservation,
  syncManagedAgents
} from "../lib/federated-registry.mjs";

test("provider catalog distinguishes automated, bridge, and unavailable discovery", () => {
  const providers = new Map(DEFAULT_PROVIDER_DEFINITIONS.map(item => [item.id, item]));
  assert.equal(providers.get("local-control").discovery, "automated");
  assert.equal(providers.get("chatgpt").discovery, "unavailable");
  assert.equal(providers.get("chatgpt").registration, "bridge");
  assert.equal(providers.get("github").registration, "bridge");
  assert.equal(providers.get("heaven2-control").registration, "bridge");
});

test("reconciliation is idempotent for the same provider source identity", () => {
  const federation = defaultFederationState();
  const first = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-123",
    role: "manager",
    state: "working",
    heartbeat_at: "2026-09-29T08:00:00.000Z"
  }, { now: Date.parse("2026-09-29T08:00:00.000Z") });
  const second = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-123",
    role: "manager",
    state: "tool_wait",
    heartbeat_at: "2026-09-29T08:00:30.000Z"
  }, { now: Date.parse("2026-09-29T08:00:30.000Z") });

  assert.equal(first.agent_id, second.agent_id);
  assert.equal(federation.agents.length, 1);
  assert.equal(second.state, "tool_wait");
  assert.equal(second.observations.length, 1);
});

test("explicit correlation reconciles the same logical worker across providers", () => {
  const federation = defaultFederationState();
  const chat = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-77",
    state: "working",
    correlation_keys: ["work-item:agent-control-59"],
    heartbeat_at: "2026-09-29T08:00:00.000Z"
  });
  const github = reconcileObservation(federation, {
    provider: "github",
    source_id: "workflow-run-999",
    state: "tool_wait",
    pr_number: 59,
    correlation_keys: ["work-item:agent-control-59"],
    heartbeat_at: "2026-09-29T08:00:10.000Z"
  });

  assert.equal(chat.agent_id, github.agent_id);
  assert.equal(federation.agents.length, 1);
  assert.deepEqual(new Set(github.providers), new Set(["chatgpt", "github"]));
  assert.equal(github.pr_number, 59);
});

test("simultaneous ChatGPT sessions remain distinct without explicit correlation", () => {
  const federation = defaultFederationState();
  const a = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-a",
    role: "support",
    task: "Same visible title",
    state: "working"
  });
  const b = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-b",
    role: "support",
    task: "Same visible title",
    state: "working"
  });

  assert.notEqual(a.agent_id, b.agent_id);
  assert.equal(federation.agents.length, 2);
});

test("heartbeat freshness excludes stale and disconnected records from live counts", () => {
  const federation = defaultFederationState();
  federation.stale_after_ms = 60_000;
  federation.disconnected_after_ms = 180_000;
  reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "fresh",
    state: "working",
    heartbeat_at: "2026-09-29T08:09:30.000Z"
  });
  reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "stale",
    state: "working",
    heartbeat_at: "2026-09-29T08:08:00.000Z"
  });
  reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "gone",
    state: "working",
    heartbeat_at: "2026-09-29T08:00:00.000Z"
  });

  const snapshot = federationSnapshot(federation, { now: Date.parse("2026-09-29T08:10:00.000Z") });
  assert.equal(snapshot.counts.live, 1);
  assert.equal(snapshot.counts.stale, 1);
  assert.equal(snapshot.counts.disconnected, 1);
});

test("completed historical tasks do not inflate live counts", () => {
  const federation = defaultFederationState();
  reconcileObservation(federation, {
    provider: "github",
    source_id: "run-complete",
    state: "done",
    heartbeat_at: "2026-09-29T08:09:59.000Z"
  });
  const snapshot = federationSnapshot(federation, { now: Date.parse("2026-09-29T08:10:00.000Z") });
  assert.equal(snapshot.counts.live, 0);
  assert.equal(snapshot.counts.done, 1);
  assert.equal(snapshot.agents[0].historical, true);
});

test("remote tool_wait is a live normalized state while its heartbeat is fresh", () => {
  const federation = defaultFederationState();
  reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-tool-wait",
    state: "tool_wait",
    machine: "cloud",
    heartbeat_at: "2026-09-29T08:09:50.000Z"
  });
  const snapshot = federationSnapshot(federation, { now: Date.parse("2026-09-29T08:10:00.000Z") });
  assert.equal(snapshot.counts.live, 1);
  assert.equal(snapshot.counts.tool_wait, 1);
});

test("reconnect preserves stable identity and returns a stale worker to live", () => {
  const federation = defaultFederationState();
  federation.stale_after_ms = 60_000;
  federation.disconnected_after_ms = 180_000;
  const first = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-reconnect",
    state: "working",
    heartbeat_at: "2026-09-29T08:00:00.000Z"
  });
  const before = federationSnapshot(federation, { now: Date.parse("2026-09-29T08:10:00.000Z") });
  assert.equal(before.counts.disconnected, 1);

  const reconnected = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-reconnect",
    state: "working",
    heartbeat_at: "2026-09-29T08:10:00.000Z"
  });
  const after = federationSnapshot(federation, { now: Date.parse("2026-09-29T08:10:01.000Z") });
  assert.equal(first.agent_id, reconnected.agent_id);
  assert.equal(after.counts.live, 1);
  assert.equal(after.counts.disconnected, 0);
});

test("mixed runtime swarm includes managed local and external agents in one count", () => {
  const federation = defaultFederationState();
  syncManagedAgents(federation, [{
    id: "support-local",
    role: "support",
    status: "running",
    machine: "heaven",
    taskId: "task-local",
    task: "Run tests",
    branchName: "agent/local",
    heartbeatAt: "2026-09-29T08:09:55.000Z"
  }], { hostname: "heaven", now: Date.parse("2026-09-29T08:10:00.000Z") });

  reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-cloud",
    role: "manager",
    machine: "cloud",
    state: "working",
    heartbeat_at: "2026-09-29T08:09:58.000Z"
  });
  reconcileObservation(federation, {
    provider: "github",
    source_id: "run-ci",
    role: "verification",
    machine: "github",
    state: "tool_wait",
    heartbeat_at: "2026-09-29T08:09:59.000Z"
  });

  const snapshot = federationSnapshot(federation, { now: Date.parse("2026-09-29T08:10:01.000Z") });
  assert.equal(snapshot.counts.live, 3);
  assert.deepEqual(new Set(snapshot.agents.map(item => item.provider)), new Set(["local-control", "chatgpt", "github"]));
});


test("migration normalizes malformed nested collections and freshness thresholds", () => {
  const migrated = migrateFederationState({
    version: 0,
    stale_after_ms: 60_000,
    disconnected_after_ms: 30_000,
    providers: [{
      id: "custom-provider",
      discovery: "bridge",
      registration: "bridge",
      metadata: "not-an-object"
    }],
    agents: [{
      agent_id: "persisted-agent",
      providers: ["github", "github", ""],
      correlation_keys: ["work-item:legacy", "work-item:legacy", ""],
      observations: "not-an-array"
    }]
  });

  assert.equal(migrated.stale_after_ms, 60_000);
  assert.equal(migrated.disconnected_after_ms, 60_000);
  assert.deepEqual(
    migrated.providers.find(item => item.id === "custom-provider").metadata,
    {}
  );
  assert.deepEqual(migrated.agents[0].providers, ["github"]);
  assert.deepEqual(migrated.agents[0].correlation_keys, ["work-item:legacy"]);
  assert.deepEqual(migrated.agents[0].observations, []);
});

test("malformed observation rejection is failure-atomic", () => {
  const federation = defaultFederationState();
  const before = structuredClone(federation);

  assert.throws(() => reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-invalid-state",
    state: "definitely-not-normalized",
    heartbeat_at: "2026-09-29T08:00:00.000Z"
  }), /Unsupported normalized agent state/);

  assert.deepEqual(federation, before);
});

test("conflicting explicit agent identity fails closed without mutating correlated state", () => {
  const federation = defaultFederationState();
  const original = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-owned",
    state: "working",
    correlation_keys: ["work-item:owned"],
    heartbeat_at: "2026-09-29T08:00:00.000Z"
  });
  const before = structuredClone(federation);

  assert.throws(() => reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-owned",
    agent_id: "agent-conflicting-manual-id",
    state: "working",
    correlation_keys: ["work-item:owned"],
    heartbeat_at: "2026-09-29T08:00:01.000Z"
  }), /conflicts with reconciled agent/);

  assert.equal(federation.agents[0].agent_id, original.agent_id);
  assert.deepEqual(federation, before);
});

test("duplicate provider reconnect storm remains bounded to logical agents", () => {
  const federation = defaultFederationState();
  federation.stale_after_ms = 60_000;
  federation.disconnected_after_ms = 180_000;
  const base = Date.parse("2026-09-29T08:00:00.000Z");
  const logicalAgents = 200;

  for (let cycle = 0; cycle < 5; cycle += 1) {
    for (let index = 0; index < logicalAgents; index += 1) {
      const heartbeat = new Date(base + cycle * 1_000 + index).toISOString();
      const key = `work-item:storm-${index}`;

      reconcileObservation(federation, {
        provider: "chatgpt",
        source_id: `conversation-${index}`,
        state: cycle % 2 === 0 ? "working" : "tool_wait",
        correlation_keys: [key],
        heartbeat_at: heartbeat
      }, { now: base + cycle * 1_000 + index });

      reconcileObservation(federation, {
        provider: "github",
        source_id: `workflow-run-${index}`,
        state: "working",
        correlation_keys: [key],
        heartbeat_at: heartbeat
      }, { now: base + cycle * 1_000 + index });
    }
  }

  const snapshot = federationSnapshot(federation, { now: base + 6_000 });
  assert.equal(federation.agents.length, logicalAgents);
  assert.equal(snapshot.counts.live, logicalAgents);
  assert.equal(snapshot.counts.disconnected, 0);
  assert.equal(snapshot.counts.stale, 0);
  for (const agent of snapshot.agents) {
    assert.equal(agent.observations.length, 2);
    assert.deepEqual(new Set(agent.providers), new Set(["chatgpt", "github"]));
  }
});
