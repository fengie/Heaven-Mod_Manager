import test from "node:test";
import assert from "node:assert/strict";

import {
  applyHeartbeatFreshness,
  collectAgentSource,
  createAgentSource,
  isLiveNormalizedAgent,
  normalizeAgentObservation,
  normalizedAgentCounts,
  providerHealth,
  reconcileAgentRegistry
} from "../lib/federation-core.mjs";

const T0 = Date.parse("2026-09-29T08:00:00.000Z");

test("ChatGPT sessions with similar titles remain distinct without explicit correlation", () => {
  const a = normalizeAgentObservation({
    provider: "chatgpt",
    chatSessionId: "chat-a",
    title: "Support Agent",
    state: "working",
    heartbeatAt: "2026-09-29T08:00:00Z"
  });
  const b = normalizeAgentObservation({
    provider: "chatgpt",
    chatSessionId: "chat-b",
    title: "Support Agent",
    state: "working",
    heartbeatAt: "2026-09-29T08:00:00Z"
  });

  assert.notEqual(a.agentId, b.agentId);
  assert.equal(a.displayName, b.displayName);
});

test("duplicate observations reconcile idempotently by provider identity", () => {
  const observation = {
    provider: "local-codex",
    processId: "pid-100",
    role: "support",
    machine: "heaven",
    state: "working",
    heartbeatAt: "2026-09-29T08:00:00Z"
  };

  const once = reconcileAgentRegistry([], [observation]);
  const twice = reconcileAgentRegistry(once, [observation]);
  assert.equal(once.length, 1);
  assert.equal(twice.length, 1);
  assert.equal(twice[0].sourceObservations.length, 1);
});

test("explicit correlation joins multi-provider observations into one logical agent", () => {
  const registry = reconcileAgentRegistry([], [{
    provider: "chatgpt",
    chatSessionId: "chat-42",
    correlationId: "task:83:agent-b",
    role: "support",
    state: "tool_wait",
    machine: "cloud",
    heartbeatAt: "2026-09-29T08:00:00Z"
  }, {
    provider: "github",
    githubRunId: "run-9001",
    correlationId: "task:83:agent-b",
    role: "support",
    state: "working",
    machine: "github",
    branch: "agent/federation",
    prNumber: 59,
    heartbeatAt: "2026-09-29T08:00:05Z"
  }]);

  assert.equal(registry.length, 1);
  assert.equal(registry[0].state, "working");
  assert.equal(registry[0].prNumber, 59);
  assert.equal(registry[0].sourceObservations.length, 2);
});

test("freshness marks stale and disconnected agents without reviving terminal history", () => {
  const registry = reconcileAgentRegistry([], [
    { provider: "chatgpt", chatSessionId: "fresh", state: "working", heartbeatAt: "2026-09-29T07:59:45Z" },
    { provider: "local", processId: "stale", state: "working", heartbeatAt: "2026-09-29T07:58:30Z" },
    { provider: "local", processId: "gone", state: "tool_wait", heartbeatAt: "2026-09-29T07:55:00Z" },
    { provider: "github", githubRunId: "done", state: "done", heartbeatAt: "2026-09-29T07:00:00Z" }
  ]);

  const freshened = applyHeartbeatFreshness(registry, {
    now: T0,
    staleAfterMs: 60_000,
    disconnectAfterMs: 180_000
  });
  const counts = normalizedAgentCounts(freshened);

  assert.equal(counts.totalLive, 1);
  assert.equal(counts.working, 1);
  assert.equal(counts.stale, 1);
  assert.equal(counts.disconnected, 1);
  assert.equal(counts.done, 1);
  assert.equal(freshened.find(agent => agent.sourceId === "done").heartbeatStatus, "terminal");
});

test("remote tool_wait is a live state while heartbeat is fresh", () => {
  const [agent] = applyHeartbeatFreshness(reconcileAgentRegistry([], [{
    provider: "chatgpt",
    chatSessionId: "remote-wait",
    machine: "cloud",
    state: "tool_wait",
    heartbeatAt: "2026-09-29T08:00:00Z"
  }]), { now: T0 });

  assert.equal(isLiveNormalizedAgent(agent), true);
  assert.equal(normalizedAgentCounts([agent]).toolWait, 1);
});

test("reconnect updates an existing logical agent instead of duplicating it", () => {
  let registry = reconcileAgentRegistry([], [{
    provider: "heaven-bridge",
    sourceId: "worker-7",
    machine: "heaven",
    state: "working",
    heartbeatAt: "2026-09-29T07:50:00Z"
  }]);

  registry = applyHeartbeatFreshness(registry, {
    now: T0,
    staleAfterMs: 60_000,
    disconnectAfterMs: 180_000
  });
  assert.equal(registry[0].state, "disconnected");

  registry = reconcileAgentRegistry(registry, [{
    provider: "heaven-bridge",
    sourceId: "worker-7",
    machine: "heaven",
    state: "working",
    heartbeatAt: "2026-09-29T08:00:01Z",
    lastActionSummary: "reconnected"
  }]);

  assert.equal(registry.length, 1);
  assert.equal(registry[0].state, "working");
  assert.equal(registry[0].lastActionSummary, "reconnected");
});

test("historical failed and completed records never inflate live counts", () => {
  const registry = applyHeartbeatFreshness(reconcileAgentRegistry([], [
    { provider: "github", githubRunId: "old-pass", state: "done", heartbeatAt: "2026-09-29T08:00:00Z" },
    { provider: "github", githubRunId: "old-fail", state: "failed", heartbeatAt: "2026-09-29T08:00:00Z" }
  ]), { now: T0 });

  const counts = normalizedAgentCounts(registry);
  assert.equal(counts.totalLive, 0);
  assert.equal(counts.done, 1);
  assert.equal(counts.failed, 1);
});

test("unavailable providers are represented honestly without fabricated agents", async () => {
  const source = createAgentSource({
    id: "chatgpt-auto-discovery",
    mode: "unavailable",
    availability: async () => "no-session-discovery-api"
  });

  const result = await collectAgentSource(source);
  assert.equal(result.available, false);
  assert.equal(result.reason, "no-session-discovery-api");
  assert.deepEqual(result.observations, []);
});

test("manual bridge providers accept explicit registration observations", async () => {
  const source = createAgentSource({
    id: "chatgpt-registration",
    mode: "manual-bridge"
  });
  const result = await collectAgentSource(source, {
    observations: [{
      provider: "chatgpt",
      chatSessionId: "chat-manual-1",
      state: "working",
      heartbeatAt: "2026-09-29T08:00:00Z"
    }]
  });

  assert.equal(result.available, true);
  assert.equal(result.observations.length, 1);
  assert.equal(result.observations[0].provider, "chatgpt");
});

test("mixed-runtime registry exposes provider health and normalized live counts", async () => {
  const local = createAgentSource({
    id: "local-workers",
    mode: "automated",
    discover: async () => [{
      provider: "local-codex",
      processId: "p1",
      machine: "heaven",
      state: "working",
      heartbeatAt: "2026-09-29T08:00:00Z"
    }]
  });
  const chatgpt = createAgentSource({
    id: "chatgpt-registration",
    mode: "manual-bridge"
  });

  const localResult = await collectAgentSource(local);
  const chatResult = await collectAgentSource(chatgpt, {
    observations: [{
      provider: "chatgpt",
      chatSessionId: "c1",
      machine: "cloud",
      state: "blocked",
      heartbeatAt: "2026-09-29T08:00:00Z"
    }]
  });
  const registry = applyHeartbeatFreshness(reconcileAgentRegistry([], [
    ...localResult.observations,
    ...chatResult.observations
  ]), { now: T0 });

  const counts = normalizedAgentCounts(registry);
  assert.equal(counts.totalLive, 2);
  assert.equal(counts.working, 1);
  assert.equal(counts.blocked, 1);

  assert.deepEqual(providerHealth([local, chatgpt], [localResult, chatResult]), [
    { source: "local-workers", mode: "automated", available: true, observationCount: 1, reason: null },
    { source: "chatgpt-registration", mode: "manual-bridge", available: true, observationCount: 1, reason: null }
  ]);
});
