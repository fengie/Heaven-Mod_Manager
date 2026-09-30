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

function extractInlineDeclaration(source, signature) {
  const start = source.indexOf(signature);
  assert.notEqual(start, -1, `inline function must exist: ${signature}`);
  const openingBrace = source.indexOf("{", start);
  assert.notEqual(openingBrace, -1, `inline function must have a body: ${signature}`);
  let depth = 0;
  for (let index = openingBrace; index < source.length; index += 1) {
    if (source[index] === "{") depth += 1;
    else if (source[index] === "}" && --depth === 0) return source.slice(start, index + 1);
  }
  assert.fail(`inline function body is unterminated: ${signature}`);
}

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
    "events",
    "recommendationSummary",
    "recommendations",
    "managedAgentsSection"
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
  assert.doesNotMatch(primaryLaunch, /id="(?:autonomyLevel|routingManifest|controlReadOnly|task|overallGoal)"/i);
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
  assert.match(html, /function renderRecommendations\(snapshot\)/);
  assert.match(html, /snapshot\.suggestedActions \|\| \[\]/);
  assert.match(html, /async function runRecommendation\(index\)/);
  assert.match(html, /ui-manager-recommendation-resume/);
  assert.match(html, /\/api\/workflows\/\$\{encodeURIComponent\(action\.workflowId\)\}\/execute/);
  assert.match(html, /a\.status === "done" \? [\s\S]*Deploy reviewer/);
  assert.doesNotMatch(html, /stoppableManagedStatuses\.has\(a\.status\) \? [^\n]+ : `<button class="good" onclick="reviewAgent/);
  assert.match(html, /\$\("controlResume"\)\.textContent = settings\.emergencyStop \? "Clear emergency stop \+ resume" : "Resume"/);
  assert.match(html, /body\.clearEmergencyStop = Boolean\(settings\.emergencyStop\)/);
  assert.doesNotMatch(html, /if \(action === "resume"\) body\.clearEmergencyStop = false/);
  assert.match(html, /copyText\(decodeURIComponent\(\'\$\{encodeURIComponent\(String\(a\.branchName \?\? ""\)\)\}\'\)\)/);
  assert.doesNotMatch(html, /onclick="copyText\(\$\{JSON\.stringify\(a\.branchName\)\}\)"/);

  const server = fs.readFileSync(path.join(ROOT, "server.mjs"), "utf8");
  assert.match(server, /bridgeMachineStatus\(heavenBridge\)/);
  assert.match(server, /pathname === "\/api\/swarm\/start"/);
  assert.match(server, /reason: "operator-start-perpetual-swarm"/);
  assert.match(server, /autonomyLevel: "engineering-autopilot"/);
  assert.match(server, /readOnly: false/);
  assert.match(server, /draining: false/);
  assert.match(server, /clearEmergencyStop: true/);
});

test("dashboard agent cards are inspectable without hijacking nested controls", () => {
  const html = fs.readFileSync(path.join(ROOT, "public", "index.html"), "utf8");
  const scriptMatch = html.match(/<script>([\s\S]*?)<\/script>/i);
  assert.ok(scriptMatch, "dashboard inline script must exist");
  assert.doesNotThrow(() => new Function(scriptMatch[1]), "dashboard inline JavaScript must parse");

  assert.match(html, /managed-agent-card/);
  assert.match(html, /federated-agent-card/);
  const managedCardAttributes = html.match(/<article class="agent inspectable managed-agent-card"([^>]*)>/)?.[1] || "";
  const federatedCardAttributes = html.match(/<article class="agent inspectable federated-agent-card"([^>]*)>/)?.[1] || "";
  assert.match(managedCardAttributes, /tabindex="0"/);
  assert.match(managedCardAttributes, /aria-label="Managed agent /);
  assert.doesNotMatch(managedCardAttributes, /role=/, "preserve the native article semantics so details and nested action controls remain exposed");
  assert.match(federatedCardAttributes, /tabindex="0"/);
  assert.match(federatedCardAttributes, /aria-label="Federated agent /);
  assert.doesNotMatch(federatedCardAttributes, /role=/);
  assert.match(html, /<div class="agent-name">[\s\S]*?\$\{escapeHtml\(a\.status\)\}/);
  assert.match(html, /<div class="task">\$\{escapeHtml\(a\.task\)\}<\/div>/);
  assert.match(html, /\$\{escapeHtml\(a\.effective_state \|\| a\.state\)\}/);
  assert.match(html, /\$\{escapeHtml\(a\.task \|\| a\.last_action_summary \|\| "No current task summary"\)\}/);
  assert.match(html, /<button onclick="showLog\('\$\{encodeURIComponent\(a\.id\)\}', this\)"\>View log<\/button>/);
  assert.match(html, /function eventTargetsControl\(event\)/);
  assert.match(html, /closest\?\.\("button,a,input,select,textarea,summary"\)/);
  assert.match(html, /async function inspectManagedAgentCard\(event\)/);
  assert.match(html, /function inspectFederatedAgentCard\(event\)/);
  assert.match(html, /\["Enter", " "\]\.includes\(event\.key\)/);
  assert.match(html, /async function showLog\(id, button = null\)/);
  assert.match(html, /if \(button\) button\.textContent/);
  assert.match(html, /Federated agent is no longer in the live registry/);
  assert.match(html, /function bindManagedAgentCardInteractions\(\)/);
  assert.match(html, /function bindFederatedAgentCardInteractions\(\)/);
  assert.equal((html.match(/bindManagedAgentCardInteractions\(\);/g) || []).length, 1, "managed cards must receive exactly one inspection-binding pass per render");
  assert.equal((html.match(/bindFederatedAgentCardInteractions\(\);/g) || []).length, 1, "federated cards must receive exactly one inspection-binding pass per render");
  assert.doesNotMatch(html, /function bindAgentCardInteractions\(\)/, "global rebinding would attach duplicate managed listeners after federation rendering");
});

test("managed card click and keyboard inspection ignore nested action controls", async () => {
  const html = fs.readFileSync(path.join(ROOT, "public", "index.html"), "utf8");
  const script = html.match(/<script>([\s\S]*?)<\/script>/i)?.[1] || "";
  const declarations = [
    "function eventTargetsControl(event)",
    "async function inspectManagedAgentCard(event)",
    "function bindManagedAgentCardInteractions()"
  ].map(signature => extractInlineDeclaration(script, signature)).join("\n");
  const card = {
    dataset: { agentId: "managed%2F1" },
    listeners: {},
    addEventListener(type, handler) { this.listeners[type] = handler; }
  };
  const inspected = [];
  const handlers = new Function("document", "showLog", `${declarations}\nreturn { bindManagedAgentCardInteractions };`)({
    querySelectorAll: selector => selector === ".managed-agent-card" ? [card] : []
  }, async id => { inspected.push(id); });
  handlers.bindManagedAgentCardInteractions();

  const activate = async (type, key, nestedControl = null) => {
    let prevented = false;
    await card.listeners[type]({
      type, key, currentTarget: card,
      target: { closest: () => nestedControl },
      preventDefault() { prevented = true; }
    });
    return prevented;
  };

  await activate("click");
  assert.equal(await activate("keydown", "Enter"), true);
  assert.equal(await activate("keydown", " "), true);
  await activate("click", null, { tagName: "BUTTON" });
  await activate("keydown", "Enter", { tagName: "BUTTON" });
  await activate("keydown", "Escape");
  assert.deepEqual(inspected, ["managed%2F1", "managed%2F1", "managed%2F1"]);
});

test("federated card click and keyboard inspection expose external session details", () => {
  const html = fs.readFileSync(path.join(ROOT, "public", "index.html"), "utf8");
  const script = html.match(/<script>([\s\S]*?)<\/script>/i)?.[1] || "";
  const declarations = [
    "function eventTargetsControl(event)",
    "function inspectFederatedAgentCard(event)",
    "function bindFederatedAgentCardInteractions()"
  ].map(signature => extractInlineDeclaration(script, signature)).join("\n");
  const card = {
    dataset: { federatedId: "external%2F1" },
    listeners: {},
    addEventListener(type, handler) { this.listeners[type] = handler; }
  };
  const box = { textContent: "", toggles: 0, classList: { toggle() { box.toggles += 1; } } };
  const state = { snapshot: { agents: [], federation: { agents: [{
    agent_id: "external/1", provider: "remote", effective_state: "running", freshness: "fresh",
    heartbeat_at: "2026-09-30T12:00:00Z", last_action_summary: "Working on task"
  }] } } };
  const handlers = new Function("document", "state", "CSS", "toast", "recoveryLabel", `${declarations}\nreturn { bindFederatedAgentCardInteractions };`)({
    querySelectorAll: selector => selector === ".federated-agent-card" ? [card] : [],
    getElementById: id => id === "federated-log-external%2F1" ? box : null
  }, state, { escape: value => value }, () => {}, value => value);
  handlers.bindFederatedAgentCardInteractions();

  card.listeners.click({ type: "click", currentTarget: card, target: { closest: () => null } });
  card.listeners.keydown({ type: "keydown", key: "Enter", currentTarget: card, target: { closest: () => null }, preventDefault() {} });
  card.listeners.keydown({ type: "keydown", key: " ", currentTarget: card, target: { closest: () => null }, preventDefault() {} });
  card.listeners.click({ type: "click", currentTarget: card, target: { closest: () => ({ tagName: "BUTTON" }) } });
  assert.equal(box.toggles, 3);
  assert.match(box.textContent, /State: running · fresh/);
  assert.match(box.textContent, /Heartbeat: 2026-09-30T12:00:00Z/);
  assert.match(box.textContent, /Last action: Working on task/);
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
