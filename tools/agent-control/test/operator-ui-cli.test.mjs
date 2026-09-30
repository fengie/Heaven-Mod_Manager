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

test("dashboard makes Start Swarm the only normal startup action and hides tuning behind diagnostics", () => {
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
    "startSwarm",
    "overallGoal",
    "startSwarmStatus",
    "autopilotPerpetual",
    "controlSummary",
    "autonomyLevel",
    "routingManifest",
    "eventSummary",
    "events"
  ]) {
    assert.ok(ids.includes(id), `missing operator surface #${id}`);
  }

  const composerStart = html.indexOf('<aside class="card composer start-card">');
  const composerEnd = html.indexOf("</aside>", composerStart);
  assert.ok(composerStart >= 0 && composerEnd > composerStart);
  const primaryLaunch = html.slice(composerStart, composerEnd);

  assert.match(primaryLaunch, /id="startSwarm">START SWARM<\/button>/);
  assert.equal((primaryLaunch.match(/<button\b/g) || []).length, 1, "normal startup surface must expose exactly one action");
  assert.doesNotMatch(primaryLaunch, /<textarea\b|<input\b|<select\b/i);
  assert.doesNotMatch(primaryLaunch, /routing manifest|read-only|autonomy|deploy one role|custom objective|overall goal/i);
  assert.match(primaryLaunch, /handles autonomy, read-only, drain, routing freshness, worker placement, and perpetual cycling automatically/i);

  const diagnosticsAt = html.indexOf('<details class="card diagnostics">');
  const overallGoalAt = html.indexOf('id="overallGoal"');
  const taskAt = html.indexOf('id="task"');
  const routingAt = html.indexOf('id="routingManifest"');
  const readOnlyAt = html.indexOf('id="controlReadOnly"');
  assert.ok(diagnosticsAt >= 0);
  assert.ok(overallGoalAt > diagnosticsAt && taskAt > diagnosticsAt && routingAt > diagnosticsAt && readOnlyAt > diagnosticsAt);
  assert.match(html, /Advanced \/ diagnostics/);
  assert.match(html, /Optional launch customization/);
  assert.match(html, /You never need to fill this in to start the swarm/);

  assert.match(html, /Control center[\s\S]*heaven2/);
  assert.match(html, /Resource worker center[\s\S]*heaven1/);
  assert.match(html, /runtime host: heaven/);
  assert.match(html, /\/api\/swarm\/start/);
  assert.match(html, /const defaultSwarmObjective = /);
  assert.match(html, /async function startSwarm\(objectiveOverride=""\)/);
  assert.match(html, /Perpetual swarm resumed and is advancing again\./);
  assert.match(html, /objectiveOverride \|\| \$\("task"\)\.value\.trim\(\) \|\| defaultSwarmObjective/);
  assert.match(html, /overallGoal:\$\("overallGoal"\)\.value\.trim\(\)/);
  assert.match(html, /perpetual:true/);
  assert.match(html, /maxCycles:0/);
  assert.match(html, /perpetualOverride/);

  assert.match(html, /Auto \/ heaven1 \(runtime: heaven\)/);
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
  assert.match(html, /repositoryWriteAuthorized:true/);
  assert.match(html, /server-side authorization remains authoritative/i);

  const server = fs.readFileSync(path.join(ROOT, "server.mjs"), "utf8");
  assert.match(server, /bridgeMachineStatus\(heavenBridge\)/);
  assert.match(server, /pathname === "\/api\/swarm\/start"/);
  assert.match(server, /reason: "operator-start-perpetual-swarm"/);
  assert.match(server, /autonomyLevel: "engineering-autopilot"/);
  assert.match(server, /readOnly: false/);
  assert.match(server, /draining: false/);
  assert.match(server, /clearEmergencyStop: true/);
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
