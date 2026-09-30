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

  const counts = federationSnapshot(federation, { now: T0 }).counts;
  assert.equal(counts.live, 4);
  assert.equal(counts.active, 3);
  assert.equal(counts.tool_wait, 1);
  assert.equal(counts.blocked, 1);
  assert.equal(counts.idle, 1);
  assert.equal(counts.stale, 1);
  assert.equal(counts.disconnected, 1);
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
  const quotaAgent = snapshot.agents.find(agent => agent.agent_id === "quota-agent");
  assert.equal(quotaAgent.effective_state, "failed");
  assert.equal(quotaAgent.live, false);
  assert.equal(quotaAgent.historical, true);
  assert.equal(snapshot.counts.failed, 1);
  assert.equal(snapshot.counts.live, 0);
});

test("dashboard has unique DOM ids and required federated operator surfaces", () => {
  const html = fs.readFileSync(path.join(HERE, "..", "public", "index.html"), "utf8");
  const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);
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

test("dashboard inline JavaScript parses cleanly", () => {
  const html = fs.readFileSync(path.join(HERE, "..", "public", "index.html"), "utf8");
  const scripts = [...html.matchAll(/<script(?:\\s[^>]*)?>([\\s\\S]*?)<\\/script>/gi)].map(match => match[1]);
  assert.ok(scripts.length > 0, "dashboard must contain at least one inline script");
  scripts.forEach((source, index) => {
    assert.doesNotThrow(
      () => new Function(source),
      `inline dashboard script ${index + 1} must parse before integration`
    );
  });
});
