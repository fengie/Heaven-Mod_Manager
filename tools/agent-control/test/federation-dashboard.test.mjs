import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  defaultFederationState,
  federationSnapshot,
  reconcileObservation,
  syncManagedAgents
} from "../lib/federated-registry.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const T0 = Date.parse("2026-09-29T09:00:00.000Z");

test("federation snapshot exposes active, waiting, blocked, idle, stale, and disconnected counts", () => {
  const federation = defaultFederationState();
  const observe = (source, state, heartbeatOffset = 0) => reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: source,
    state,
    heartbeat_at: new Date(T0 + heartbeatOffset).toISOString()
  }, { now: T0 });

  observe("working", "working");
  observe("waiting", "tool_wait");
  observe("blocked", "blocked");
  observe("idle", "idle");
  observe("stale", "working", -180_000);
  observe("disconnected", "working", -400_000);

  const snapshot = federationSnapshot(federation, { now: T0 });
  const counts = snapshot.counts;
  assert.equal(counts.live, 4);
  assert.equal(counts.active, 3);
  assert.equal(counts.tool_wait, 1);
  assert.equal(counts.blocked, 1);
  assert.equal(counts.idle, 1);
  assert.equal(counts.stale, 1);
  assert.equal(counts.disconnected, 1);
  assert.equal(counts.total, 4, "live registry total must exclude stale/disconnected history");
  assert.deepEqual(snapshot.agents.map(agent => agent.source_id).sort(), ["blocked", "idle", "waiting", "working"]);
  assert.deepEqual(snapshot.history_agents.map(agent => agent.source_id).sort(), ["disconnected", "stale"]);
});

test("quota-blocked managed agents are historical failures, not healthy live agents", () => {
  const federation = defaultFederationState();
  syncManagedAgents(federation, [{
    id: "quota-agent",
    role: "support",
    status: "capacity-blocked",
    lastMessage: "You've hit your usage limit.",
    heartbeatAt: new Date(T0).toISOString(),
    finishedAt: new Date(T0).toISOString()
  }], { hostname: "heaven2", now: T0 });

  const snapshot = federationSnapshot(federation, { now: T0 });
  const quotaAgent = snapshot.history_agents.find(agent => agent.agent_id === "quota-agent");
  assert.equal(snapshot.agents.some(agent => agent.agent_id === "quota-agent"), false);
  assert.equal(quotaAgent.effective_state, "failed");
  assert.equal(quotaAgent.live, false);
  assert.equal(quotaAgent.historical, true);
  assert.equal(snapshot.counts.failed, 1);
  assert.equal(snapshot.counts.live, 0);
  assert.equal(snapshot.counts.total, 0);
  assert.equal(snapshot.counts.historical, 1);
});

test("recovery-owned historical records stay in attention while exhausted failures archive", () => {
  const federation = defaultFederationState();
  for (const [source, recovery] of [["dirty", "work-detected-incomplete"], ["exhausted", "retry-exhausted"]]) {
    reconcileObservation(federation, {
      provider: "chatgpt",
      source_id: source,
      state: "failed",
      heartbeat_at: new Date(T0).toISOString()
    }, { now: T0 });
    federation.agents.find(agent => agent.source_id === source).recovery_status = recovery;
  }

  const snapshot = federationSnapshot(federation, { now: T0 });
  assert.deepEqual(snapshot.agents, []);
  assert.deepEqual(snapshot.attention_agents.map(agent => agent.source_id), ["dirty"]);
  assert.deepEqual(snapshot.history_agents.map(agent => agent.source_id), ["exhausted"]);
  assert.equal(snapshot.counts.total, 0);
  assert.equal(snapshot.counts.attention, 1);
  assert.equal(snapshot.counts.historical, 1);
});

test("fresh heartbeat deterministically re-registers a stale historical source", () => {
  const federation = defaultFederationState();
  reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "session-1",
    state: "working",
    heartbeat_at: new Date(T0 - 180_000).toISOString()
  }, { now: T0 });
  let snapshot = federationSnapshot(federation, { now: T0 });
  assert.equal(snapshot.agents.length, 0);
  assert.equal(snapshot.history_agents[0].source_id, "session-1");

  reconcileObservation(federation, {
    provider: "chatgpt",
    source_id: "session-1",
    state: "working",
    heartbeat_at: new Date(T0 + 1_000).toISOString()
  }, { now: T0 + 1_000 });
  snapshot = federationSnapshot(federation, { now: T0 + 1_000 });
  assert.equal(snapshot.agents[0].source_id, "session-1");
  assert.equal(snapshot.history_agents.length, 0);
});

test("dashboard has unique DOM ids and required federated operator surfaces", () => {
  const html = fs.readFileSync(path.join(HERE, "..", "public", "index.html"), "utf8");
  const ids = [...html.matchAll(/(?:^|\s)id="([^"]+)"/gm)].map(match => match[1]);
  assert.equal(new Set(ids).size, ids.length, "duplicate DOM ids make federated rendering ambiguous");
  assert.match(html, /Federated agent registry/);
  assert.match(html, /Control center[\s\S]*heaven2/);
  assert.match(html, /Resource worker center[\s\S]*heaven1/);
  assert.match(html, /runtime host: heaven/);
  assert.match(html, /Recent events/);
  assert.match(html, /Auto \/ heaven1 \(runtime: heaven\)/);
  assert.match(html, /counts\.active/);
  assert.match(html, /counts\.tool_wait/);
  assert.match(html, /counts\.blocked/);
  assert.match(html, /counts\.idle/);
  assert.match(html, /counts\.stale/);
  assert.match(html, /last_action_summary/);
  assert.match(html, /lease_id/);
  assert.match(html, /last_error/);
  assert.match(html, /PARTIAL COVERAGE/);
  assert.match(html, /coverageWarning/);
  assert.match(html, /federatedCount/);
  assert.match(html, /coverage\.authoritative/);
});

test("dashboard preserves active lifecycle controls and rejects stale poll overwrites", () => {
  const html = fs.readFileSync(path.join(HERE, "..", "public", "index.html"), "utf8");
  assert.match(html, /stoppableManagedStatuses = new Set\(\["reserved","starting","running","waiting","blocked","stale","stopping"\]\)/);
  assert.match(html, /stoppableManagedStatuses\.has\(a\.status\)/);
  assert.match(html, /const selectedMachine = \$\("machine"\)\.value \|\| "auto"/);
  assert.match(html, /refreshInFlight/);
  assert.match(html, /requestId < state\.refreshAppliedId/);
  assert.match(html, /requestId >= state\.refreshAppliedId/);
});

test("dashboard inline JavaScript parses cleanly", () => {
  const html = fs.readFileSync(path.join(HERE, "..", "public", "index.html"), "utf8");
  const scripts = [...html.matchAll(/<script(?:\s[^>]*)?>([\s\S]*?)<\/script>/gi)].map(match => match[1]);
  assert.ok(scripts.length > 0, "dashboard must contain at least one inline script");
  scripts.forEach((source, index) => {
    assert.doesNotThrow(
      () => new Function(source),
      `inline dashboard script ${index + 1} must parse before integration`
    );
  });
});
