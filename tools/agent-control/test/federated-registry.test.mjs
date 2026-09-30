import test from "node:test";
import assert from "node:assert/strict";

import {
  DEFAULT_PROVIDER_DEFINITIONS,
  FEDERATION_VERSION,
  agentSourceAdapter,
  defaultFederationState,
  federationSnapshot,
  migrateFederationState,
  normalizeObservation,
  reconcileObservation,
  recordProviderHeartbeat,
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
  assert.equal(snapshot.counts.total, 0);
  assert.equal(snapshot.agents.length, 0);
  assert.equal(snapshot.history_agents[0].historical, true);
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


test("provider/source changes do not merge agents from task or PR metadata alone", () => {
  const federation = defaultFederationState();
  const chat = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-provider-change",
    state: "working",
    task_id: "task-42",
    task: "Same task",
    pr_number: 91
  });
  const github = reconcileObservation(federation, {
    provider: "github",
    source_id: "workflow-provider-change",
    state: "working",
    task_id: "task-42",
    task: "Same task",
    pr_number: 91
  });

  assert.notEqual(chat.agent_id, github.agent_id);
  assert.equal(federation.agents.length, 2);
});

test("provider/source changes reconcile when an explicit strong logical key is shared", () => {
  const federation = defaultFederationState();
  const chat = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-strong",
    state: "working",
    correlation: { logical_agent_id: "logical-worker-7" }
  });
  const github = reconcileObservation(federation, {
    provider: "github",
    source_id: "workflow-strong",
    state: "tool_wait",
    correlation: { logical_agent_id: "logical-worker-7" }
  });

  assert.equal(chat.agent_id, github.agent_id);
  assert.equal(federation.agents.length, 1);
  assert.deepEqual(new Set(github.providers), new Set(["chatgpt", "github"]));
});

test("late observations cannot regress a newer source heartbeat or lifecycle", () => {
  const federation = defaultFederationState();
  const current = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-monotonic",
    state: "working",
    heartbeat_at: "2026-09-29T08:10:00.000Z",
    last_action_summary: "newer"
  });
  const late = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-monotonic",
    state: "failed",
    heartbeat_at: "2026-09-29T08:00:00.000Z",
    last_action_summary: "older"
  });

  assert.equal(late.agent_id, current.agent_id);
  assert.equal(late.state, "working");
  assert.equal(late.heartbeat_at, "2026-09-29T08:10:00.000Z");
  assert.equal(late.last_action_summary, "newer");
  assert.equal(late.observations.length, 1);
  const snapshot = federationSnapshot(federation, { now: Date.parse("2026-09-29T08:10:01.000Z") });
  assert.equal(snapshot.counts.live, 1);
  assert.equal(snapshot.counts.failed, 0);
});

test("ambiguous strong correlation fails closed instead of merging two logical agents", () => {
  const federation = defaultFederationState();
  reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-a-ambiguous",
    state: "working",
    correlation_keys: ["worker:a"]
  });
  reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-b-ambiguous",
    state: "working",
    correlation_keys: ["worker:b"]
  });

  assert.throws(() => reconcileObservation(federation, {
    provider: "github",
    source_id: "workflow-ambiguous",
    state: "working",
    correlation_keys: ["worker:a", "worker:b"]
  }), /ambiguous/i);
  assert.equal(federation.agents.length, 2);
});

test("malformed and unknown provider data fails safely", () => {
  const federation = defaultFederationState();
  assert.throws(() => reconcileObservation(federation, {
    provider: "typo-provider",
    source_id: "unknown",
    state: "working"
  }), /Unsupported federation provider/);
  assert.throws(() => normalizeObservation(federation, {
    provider: "chatgpt",
    source_id: "bad-date",
    state: "working",
    heartbeat_at: "not-a-date"
  }), /heartbeat_at/);
  assert.throws(() => reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "bad-correlation",
    state: "working",
    correlation_keys: ["task:same-task"]
  }), /metadata, not a logical-agent identity key/);
  assert.throws(() => recordProviderHeartbeat(federation, "chatgpt", {
    status: "invented-health-state"
  }), /Unsupported provider health status/);
  assert.throws(() => reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "bad-correlation-array",
    state: "working",
    correlation_keys: "worker:not-an-array"
  }), /correlation_keys must be an array/);
  assert.throws(() => recordProviderHeartbeat(federation, "chatgpt", {
    metadata: "not-an-object"
  }), /metadata must be an object/);
});

test("persisted registry reload preserves logical identity and upgrades schema safely", () => {
  const federation = defaultFederationState();
  const first = reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "conversation-persisted",
    state: "working",
    runtime: "gpt-5.6-sol",
    session_id: "session-persisted",
    conversation_id: "conversation-persisted",
    correlation: { logical_agent_id: "persistent-logical-agent" },
    source_metadata: { provider_payload: { opaque: true } },
    heartbeat_at: "2026-09-29T08:10:00.000Z"
  });

  const persisted = JSON.parse(JSON.stringify(federation));
  persisted.version = 1;
  const restored = migrateFederationState(persisted);
  const second = reconcileObservation(restored, {
    provider: "chatgpt",
    source_id: "conversation-persisted",
    state: "tool_wait",
    correlation: { logical_agent_id: "persistent-logical-agent" },
    heartbeat_at: "2026-09-29T08:10:10.000Z"
  });

  assert.equal(restored.version, FEDERATION_VERSION);
  assert.equal(first.agent_id, second.agent_id);
  assert.equal(restored.agents.length, 1);
  assert.equal(second.runtime, "chatgpt");
  assert.equal(second.session_id, "session-persisted");
  assert.equal(second.conversation_id, "conversation-persisted");
  assert.deepEqual(second.source_metadata.provider_payload, { opaque: true });
});

test("agent source adapter exposes honest discovery and bridge ingestion contracts", async () => {
  const federation = defaultFederationState();
  const chatgpt = agentSourceAdapter(federation, "chatgpt");

  assert.equal(chatgpt.discovery, "unavailable");
  assert.equal(chatgpt.registration, "bridge");
  await assert.rejects(() => chatgpt.discover(), /does not support automatic discovery/);

  const ingested = chatgpt.ingest({
    source_id: "conversation-adapter",
    state: "working",
    correlation: { chat_session_id: "session-adapter" }
  });
  assert.equal(ingested.provider, "chatgpt");
  assert.equal(ingested.session_id, "session-adapter");
  assert.equal(federation.agents.length, 1);
});

test("migration preserves unsupported persisted providers without treating them as installed adapters", () => {
  const restored = migrateFederationState({
    version: 1,
    providers: [],
    agents: [{
      agent_id: "agent-legacy-provider",
      provider: "legacy-cloud",
      state: "working",
      heartbeat_at: "2026-09-29T08:10:00.000Z",
      observations: [{
        provider: "legacy-cloud",
        source_id: "legacy-1",
        state: "working",
        heartbeat_at: "2026-09-29T08:10:00.000Z"
      }]
    }]
  });

  const provider = restored.providers.find(item => item.id === "legacy-cloud");
  assert.equal(provider.status, "unsupported");
  assert.equal(provider.registration, "unavailable");
  assert.throws(() => agentSourceAdapter(restored, "legacy-cloud"), /no installed adapter/);
  assert.throws(() => reconcileObservation(restored, {
    provider: "legacy-cloud",
    source_id: "legacy-2",
    state: "working"
  }), /no installed adapter/);
});


test("managed retry-exhausted workers are never re-synchronized into the federated live registry", () => {
  const federation = defaultFederationState();
  syncManagedAgents(federation, [{
    id: "dead-retry",
    role: "support",
    status: "failed",
    recoveryStatus: "retry-exhausted",
    machine: "heaven2",
    heartbeatAt: "2026-09-30T07:00:00.000Z"
  }, {
    id: "live-worker",
    role: "support",
    status: "running",
    machine: "heaven2",
    heartbeatAt: "2026-09-30T07:00:00.000Z"
  }], { hostname: "heaven2", now: Date.parse("2026-09-30T07:00:00.000Z") });

  assert.deepEqual(federation.agents.map(agent => agent.agent_id), ["live-worker"]);
  const localProvider = federation.providers.find(provider => provider.id === "local-control");
  assert.equal(localProvider.metadata.managed_agents, 1);
});
