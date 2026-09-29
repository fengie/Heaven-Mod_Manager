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
const ROOT = path.join(HERE, "..");
const T0 = Date.parse("2026-09-29T09:00:00.000Z");

test("federation snapshot separates live lifecycle and freshness counts", () => {
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
  assert.equal(counts.working, 1);
  assert.equal(counts.tool_wait, 1);
  assert.equal(counts.blocked, 1);
  assert.equal(counts.idle, 1);
  assert.equal(counts.stale, 1);
  assert.equal(counts.disconnected, 1);
});

test("dashboard exposes truthful operator visibility and server-backed controls", () => {
  const html = fs.readFileSync(path.join(ROOT, "public", "index.html"), "utf8");
  const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);

  assert.equal(new Set(ids).size, ids.length, "dashboard DOM ids must remain unique");
  for (const id of [
    "working",
    "waiting",
    "blocked",
    "idle",
    "stale",
    "disconnected",
    "controlSummary",
    "autonomyLevel",
    "routingManifest",
    "deploySwarm",
    "quickContinue",
    "quickSwarm",
    "quickFix",
    "quickPlan",
    "quickVerify",
    "quickBacklog",
    "quickRelease",
    "quickAutopilot",
    "eventSummary",
    "events"
  ]) {
    assert.ok(ids.includes(id), `missing operator surface #${id}`);
  }

  assert.match(html, /Auto \/ heaven/);
  assert.match(html, /presence-unknown/);
  assert.match(html, /counts\.working/);
  assert.match(html, /counts\.tool_wait/);
  assert.match(html, /counts\.blocked/);
  assert.match(html, /counts\.idle/);
  assert.match(html, /last_error/);
  assert.match(html, /last_action_summary/);
  assert.match(html, /lease_id/);
  assert.match(html, /reviewState/);
  assert.match(html, /integrationReady/);
  assert.match(html, /recentEvents/);
  assert.match(html, /\/api\/control\/settings/);
  assert.match(html, /\/api\/control\/pause/);
  assert.match(html, /\/api\/control\/resume/);
  assert.match(html, /\/api\/control\/read-only/);
  assert.match(html, /\/api\/control\/drain/);
  assert.match(html, /\/api\/control\/emergency-stop/);
  assert.match(html, /\/api\/control\/routing-manifest/);
  assert.match(html, /\/api\/workflows\/usual-swarm\/execute/);
  assert.match(html, /Deploy Usual Swarm/);
  assert.match(html, /One-click launch/);
  assert.match(html, /Continue Project/);
  assert.match(html, /Fix Bugs/);
  assert.match(html, /Implement Planned/);
  assert.match(html, /Verify & Repair/);
  assert.match(html, /Clean Backlog/);
  assert.match(html, /Release Ready/);
  assert.match(html, /Start Autopilot/);
  assert.match(html, /const presetObjectives = \{/);
  assert.match(html, /presetObjectives\.continue/);
  assert.match(html, /async function runPreset\(key\)/);
  assert.match(html, /async function deploySwarm\(objectiveOverride=""\)/);
  assert.match(html, /objectiveOverride \|\| \$\("task"\)\.value\.trim\(\) \|\| presetObjectives\.continue/);
  assert.match(html, /quick-action\[data-preset\]/);
  assert.match(html, /repositoryWriteAuthorized:true/);
  assert.match(html, /server-side authorization remains authoritative/i);

  const server = fs.readFileSync(path.join(ROOT, "server.mjs"), "utf8");
  assert.match(server, /bridgeMachineStatus\(heavenBridge\)/);
});

test("CLI keeps JSON output and exposes matching operator controls with explicit failure semantics", () => {
  const cli = fs.readFileSync(path.join(ROOT, "agentctl.mjs"), "utf8");

  for (const command of [
    "events",
    "control",
    "pause",
    "resume",
    "read-only",
    "drain",
    "emergency-stop",
    "stop-swarm"
  ]) {
    assert.match(cli, new RegExp(`command === "${command}"`));
  }

  assert.match(cli, /\/api\/control\/pause/);
  assert.match(cli, /\/api\/control\/resume/);
  assert.match(cli, /\/api\/control\/read-only/);
  assert.match(cli, /\/api\/control\/drain/);
  assert.match(cli, /\/api\/control\/emergency-stop/);
  assert.match(cli, /\/api\/control\/stop-swarm/);
  assert.match(cli, /repositoryWriteAuthorized: true/);
  assert.match(cli, /console\.log\(JSON\.stringify\(value, null, 2\)\)/);
  assert.match(cli, /process\.exitCode = 1/);
  assert.match(cli, /process\.exitCode = 2/);
  assert.match(cli, /read-only requires on or off/);
});
