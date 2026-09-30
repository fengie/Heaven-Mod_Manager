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
  const ids = [...html.matchAll(/(?:^|\s)id="([^"]+)"/gm)].map(match => match[1]);

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
    "managedAgentsSection",
    "attentionSection",
    "attentionSummary",
    "attentionAgents",
    "registryHistorySection",
    "registryHistorySummary",
    "registryHistory"
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
  assert.match(html, /const encodedBranch = escapeHtml\(encodeURIComponent\(String\(a\.branchName \?\? ""\)\)\)/);
  assert.match(html, /data-agent-action="copy-branch"[\s\S]*data-branch="\$\{encodedBranch\}"/);
  assert.match(html, /if \(action === "copy-branch"\) return copyText\(decodeStableId\(branch\)\)/);
  assert.doesNotMatch(html, /onclick="copyText\(/);

  const server = fs.readFileSync(path.join(ROOT, "server.mjs"), "utf8");
  assert.match(server, /bridgeMachineStatus\(heavenBridge\)/);
  assert.match(server, /pathname === "\/api\/swarm\/start"/);
  assert.match(server, /reason: "operator-start-perpetual-swarm"/);
  assert.match(server, /autonomyLevel: "engineering-autopilot"/);
  assert.match(server, /readOnly: false/);
  assert.match(server, /draining: false/);
  assert.match(server, /clearEmergencyStop: true/);
});

test("dashboard separates live registry, attention, and archived history surfaces", () => {
  const html = fs.readFileSync(path.join(ROOT, "public", "index.html"), "utf8");
  const server = fs.readFileSync(path.join(ROOT, "server.mjs"), "utf8");

  assert.match(html, /Needs Attention \/ recovery/);
  assert.match(html, /Registry history/);
  assert.match(html, /function renderAttention\(snapshot\)/);
  assert.match(html, /snapshot\.attentionAgents \|\| \[\]/);
  assert.match(html, /snapshot\.federation\?\.attention_agents \|\| \[\]/);
  assert.match(html, /function renderRegistryHistory\(snapshot\)/);
  assert.match(html, /snapshot\.managedHistory \|\| \[\]/);
  assert.match(html, /snapshot\.retiredAgents \|\| \[\]/);
  assert.match(html, /snapshot\.federation\?\.history_agents \|\| \[\]/);
  assert.match(html, /\.\.\.\(snapshot\?\.attentionAgents \|\| \[\]\)/);
  assert.match(html, /\.\.\.\(snapshot\?\.managedHistory \|\| \[\]\)/);
  assert.match(html, /\.\.\.\(federation\.attention_agents \|\| \[\]\)/);
  assert.match(html, /\.\.\.\(federation\.history_agents \|\| \[\]\)/);

  assert.match(server, /function managedRegistryViews\(state\)/);
  assert.match(server, /terminalRegistryStates = new Set\(\["done", "failed", "finished", "stopped", "capacity-blocked"\]\)/);
  assert.match(server, /agents: managedRegistry\.live/);
  assert.match(server, /attentionAgents: managedRegistry\.attention/);
  assert.match(server, /managedHistory: managedRegistry\.history/);
  assert.match(server, /retiredAgents: normalizeRetiredAgents\(state\.retiredAgents\)/);
  assert.match(server, /worktreeDirty === true/);
  assert.match(server, /worktreeClean === false/);
  assert.match(server, /registryRetirementStatus/);
  assert.match(server, /remoteTerminationPending/);
  assert.match(server, /recovery === "retry-exhausted"/);
});

test("dashboard renders notifications and a stable explicit inspector contract", () => {
  const html = fs.readFileSync(path.join(ROOT, "public", "index.html"), "utf8");
  const scriptMatch = html.match(/<script>([\s\S]*?)<\/script>/i);
  assert.ok(scriptMatch, "dashboard inline script must exist");
  assert.doesNotThrow(() => new Function(scriptMatch[1]), "dashboard inline JavaScript must parse");

  for (const id of [
    "notificationSection",
    "notificationSummary",
    "notifications",
    "inspectorSection",
    "inspectorSummary",
    "inspectorBody"
  ]) {
    assert.match(html, new RegExp(`id="${id}"`), `missing #${id}`);
  }

  assert.match(html, /selectedInspector:null/);
  assert.match(html, /inspectorNotice:null/);
  assert.match(html, /function renderNotifications\(snapshot\)/);
  assert.match(html, /snapshot\.notifications \|\| \[\]/);
  assert.match(html, /function runNotificationAction\(action\)/);
  assert.match(html, /action\.type === "inspect-agent"/);
  assert.match(html, /action\.type === "inspect-federation"/);
  assert.match(html, /\["inspect-agent", "inspect-federation"\]\.includes\(type\)/);
  assert.match(html, /function inspectManagedAgentById\(id,/);
  assert.match(html, /function inspectFederatedAgentById\(id,/);
  assert.match(html, /function renderInspector\(snapshot\)/);
  assert.match(html, /renderFederation\(snapshot\);\s*renderInspector\(snapshot\);/);
  assert.match(html, /state\.selectedInspector = null;\s*state\.inspectorNotice = \{ kind, id: rawId, message \}/);
  assert.match(html, /no longer in the live registry; it may have been retired/);
  assert.match(html, /if \(!agent \|\| !box\) \{\s*inspectorMissing\("managed", rawId\)/);

  const managedCardAttributes = html.match(/<article class="agent inspectable managed-agent-card"([^>]*)>/)?.[1] || "";
  const federatedCardAttributes = html.match(/<article class="agent inspectable federated-agent-card"([^>]*)>/)?.[1] || "";
  assert.doesNotMatch(managedCardAttributes, /tabindex=|role=/, "managed article is a grouping surface, not a fake button");
  assert.doesNotMatch(federatedCardAttributes, /tabindex=|role=/, "federated article is a grouping surface, not a fake button");
  assert.match(managedCardAttributes, /aria-label="Managed agent /);
  assert.match(federatedCardAttributes, /aria-label="Federated agent /);
  assert.match(html, /class="managed-agent-action" data-agent-action="inspect"/);
  assert.match(html, /class="federated-agent-action"/);
  assert.match(html, />Inspect<\/button>/);
  assert.doesNotMatch(html, /onclick="showLog\('\$\{encodeURIComponent\(a\.id\)\}/);
  assert.doesNotMatch(html, /onclick="stopAgent\('\$\{encodeURIComponent\(a\.id\)\}/);
  assert.doesNotMatch(html, /onclick="reviewAgent\('\$\{encodeURIComponent\(a\.id\)\}/);

  for (const field of [
    "executionProvider",
    "runtimeProvider",
    "leaseId",
    "branchName",
    "prNumber",
    "heartbeatAt",
    "lastMessage",
    "replacementAgentId",
    "recoveryNextAt",
    "source_metadata?.lease_id",
    "source_metadata?.boundary",
    "last_action_summary",
    "last_error"
  ]) {
    assert.ok(html.includes(field), `inspector must expose ${field}`);
  }
});

test("managed card convenience click and explicit Inspect control share the ID inspector without nested double-fire", async () => {
  const html = fs.readFileSync(path.join(ROOT, "public", "index.html"), "utf8");
  const script = html.match(/<script>([\s\S]*?)<\/script>/i)?.[1] || "";
  const declarations = [
    "function eventTargetsControl(event)",
    "function inspectManagedAgentCard(event)",
    "function bindManagedAgentCardInteractions()"
  ].map(signature => extractInlineDeclaration(script, signature)).join("\n");

  const card = {
    dataset: { agentKey: "managed%2F1" },
    listeners: {},
    addEventListener(type, handler) { this.listeners[type] = handler; }
  };
  const inspectButton = {
    dataset: { agentAction: "inspect", agentKey: "managed%2F1", branch: "" },
    listeners: {},
    addEventListener(type, handler) { this.listeners[type] = handler; }
  };
  const inspected = [];
  const actions = [];
  const handlers = new Function(
    "document",
    "inspectManagedAgentById",
    "runManagedAgentAction",
    `${declarations}\nreturn { bindManagedAgentCardInteractions };`
  )({
    querySelectorAll: selector => {
      if (selector === ".managed-agent-card") return [card];
      if (selector === ".managed-agent-action") return [inspectButton];
      return [];
    }
  }, id => { inspected.push(id); return true; }, async (action, id) => { actions.push([action, id]); });
  handlers.bindManagedAgentCardInteractions();

  card.listeners.click({
    type:"click",
    currentTarget:card,
    target:{ closest:() => null }
  });
  card.listeners.click({
    type:"click",
    currentTarget:card,
    target:{ closest:() => ({ tagName:"BUTTON" }) }
  });
  let stopped = false;
  await inspectButton.listeners.click({ stopPropagation(){ stopped = true; } });

  assert.equal(card.listeners.keydown, undefined, "generic article must not hide keyboard-button semantics");
  assert.deepEqual(inspected, ["managed%2F1"]);
  assert.deepEqual(actions, [["inspect", "managed%2F1"]]);
  assert.equal(stopped, true);
});

test("federated card convenience click and explicit Inspect control use the shared federated inspector", () => {
  const html = fs.readFileSync(path.join(ROOT, "public", "index.html"), "utf8");
  const script = html.match(/<script>([\s\S]*?)<\/script>/i)?.[1] || "";
  const declarations = [
    "function eventTargetsControl(event)",
    "function inspectFederatedAgentCard(event)",
    "function bindFederatedAgentCardInteractions()"
  ].map(signature => extractInlineDeclaration(script, signature)).join("\n");

  const card = {
    dataset: { federatedKey: "external%2F1" },
    listeners: {},
    addEventListener(type, handler) { this.listeners[type] = handler; }
  };
  const inspectButton = {
    dataset: { federatedKey: "external%2F1" },
    listeners: {},
    addEventListener(type, handler) { this.listeners[type] = handler; }
  };
  const inspected = [];
  const handlers = new Function(
    "document",
    "inspectFederatedAgentById",
    `${declarations}\nreturn { bindFederatedAgentCardInteractions };`
  )({
    querySelectorAll: selector => {
      if (selector === ".federated-agent-card") return [card];
      if (selector === ".federated-agent-action") return [inspectButton];
      return [];
    }
  }, id => { inspected.push(id); return true; });
  handlers.bindFederatedAgentCardInteractions();

  card.listeners.click({ currentTarget:card, target:{ closest:() => null } });
  card.listeners.click({ currentTarget:card, target:{ closest:() => ({ tagName:"BUTTON" }) } });
  let stopped = false;
  inspectButton.listeners.click({ stopPropagation(){ stopped = true; } });

  assert.equal(card.listeners.keydown, undefined);
  assert.deepEqual(inspected, ["external%2F1", "external%2F1"]);
  assert.equal(stopped, true);
});

test("notification action routing recognizes only exact inspect actions", () => {
  const html = fs.readFileSync(path.join(ROOT, "public", "index.html"), "utf8");
  const script = html.match(/<script>([\s\S]*?)<\/script>/i)?.[1] || "";
  const declaration = extractInlineDeclaration(script, "function runNotificationAction(action)");
  const calls = [];
  const run = new Function(
    "inspectManagedAgentById",
    "inspectFederatedAgentById",
    `${declaration}\nreturn runNotificationAction;`
  )(
    id => { calls.push(["managed", id]); return true; },
    id => { calls.push(["federated", id]); return true; }
  );

  assert.equal(run({ type:"inspect-agent", agentId:"managed/'quoted" }), true);
  assert.equal(run({ type:"inspect-federation", agentId:"external/1" }), true);
  assert.equal(run({ type:"inspect-task", agentId:"task/1" }), false);
  assert.equal(run({ type:"generate-takeover", agentId:"managed/1" }), false);
  assert.equal(run(null), false);
  assert.deepEqual(calls, [
    ["managed", "managed/'quoted"],
    ["federated", "external/1"]
  ]);
});

test("selected inspector survives refresh and missing or retired selections degrade deterministically", () => {
  const html = fs.readFileSync(path.join(ROOT, "public", "index.html"), "utf8");
  assert.match(html, /state\.snapshot = snapshot;\s*render\(snapshot\);/);
  assert.match(html, /renderAgents\(snapshot\);\s*renderFederation\(snapshot\);\s*renderInspector\(snapshot\);/);
  assert.match(html, /const selection = state\.selectedInspector;/);
  assert.match(html, /managedAgentById\(selection\.id, snapshot\)/);
  assert.match(html, /federatedAgentById\(selection\.id, snapshot\)/);
  assert.match(html, /if \(!agent\) return inspectorMissing\("managed", selection\.id\)/);
  assert.match(html, /if \(!agent\) return inspectorMissing\("federated", selection\.id\)/);
  assert.match(html, /state\.selectedInspector = null;/);
  assert.match(html, /state\.inspectorNotice = \{ kind, id: rawId, message \};/);
  assert.match(html, /inspectorSummary"\)\.textContent = "retired \/ missing"/);
  assert.match(html, /escapeHtml\(item\.title \|\| "Notification"\)/);
  assert.match(html, /escapeHtml\(item\.message \|\| item\.reason \|\| ""\)/);
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
