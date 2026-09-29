import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  defaultFederationState,
  federationSnapshot,
  reconcileObservation
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

test("dashboard has unique DOM ids and required federated operator surfaces", () => {
  const html = fs.readFileSync(path.join(HERE, "..", "public", "index.html"), "utf8");
  const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);
  assert.equal(new Set(ids).size, ids.length, "duplicate DOM ids make federated rendering ambiguous");
  assert.match(html, /Federated agent registry/);
  assert.match(html, /Recent events/);
  assert.match(html, /Auto \/ heaven/);
  assert.match(html, /counts\.active/);
  assert.match(html, /counts\.tool_wait/);
  assert.match(html, /counts\.blocked/);
  assert.match(html, /counts\.idle/);
  assert.match(html, /counts\.stale/);
  assert.match(html, /last_action_summary/);
  assert.match(html, /lease_id/);
  assert.match(html, /last_error/);
});
