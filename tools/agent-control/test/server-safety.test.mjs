// Security closure checkpoint: this file is touched intentionally so the full post-merge Agent Control PR gate reruns against current canonical source.
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import http from "node:http";
import net from "node:net";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { fileURLToPath } from "node:url";
import { createHash } from "node:crypto";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SERVER = path.resolve(HERE, "..", "server.mjs");

async function freePort() {
  const server = net.createServer();
  await new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", resolve);
  });
  const port = server.address().port;
  await new Promise(resolve => server.close(resolve));
  return port;
}

function launch({ root, host = "127.0.0.1", port }) {
  const dataDir = path.join(root, "data");
  const repoDir = path.join(root, "repo");
  const worktreeRoot = path.join(root, "worktrees");
  fs.mkdirSync(dataDir, { recursive: true });
  fs.mkdirSync(repoDir, { recursive: true });
  fs.mkdirSync(worktreeRoot, { recursive: true });
  const child = spawn(process.execPath, [SERVER], {
    env: {
      ...process.env,
      AGENT_CONTROL_HOST: host,
      AGENT_CONTROL_ALLOW_NON_CONTROLLER_HOST: "1",
      AGENT_CONTROL_PORT: String(port),
      AGENT_CONTROL_DATA_DIR: dataDir,
      AGENT_CONTROL_REPO: repoDir,
      AGENT_WORKTREE_ROOT: worktreeRoot
    },
    stdio: ["ignore", "pipe", "pipe"],
    windowsHide: true
  });
  let output = "";
  child.stdout.on("data", chunk => { output += chunk.toString(); });
  child.stderr.on("data", chunk => { output += chunk.toString(); });
  return { child, dataDir, getOutput: () => output };
}

function getJson(port, pathname = "/api/snapshot", extraHeaders = {}) {
  return new Promise((resolve, reject) => {
    const request = http.get({ host: "127.0.0.1", port, path: pathname, timeout: 700, headers: extraHeaders }, response => {
      let body = "";
      response.setEncoding("utf8");
      response.on("data", chunk => { body += chunk; });
      response.on("end", () => {
        try {
          resolve({ status: response.statusCode, body: JSON.parse(body) });
        } catch (error) {
          reject(error);
        }
      });
    });
    request.on("timeout", () => request.destroy(new Error("timeout")));
    request.on("error", reject);
  });
}

function postJson(port, pathname, value = {}, extraHeaders = {}) {
  const payload = JSON.stringify(value);
  return new Promise((resolve, reject) => {
    const request = http.request({
      host: "127.0.0.1",
      port,
      path: pathname,
      method: "POST",
      timeout: 1500,
      headers: {
        "Content-Type": "application/json",
        "Content-Length": Buffer.byteLength(payload),
        ...extraHeaders
      }
    }, response => {
      let body = "";
      response.setEncoding("utf8");
      response.on("data", chunk => { body += chunk; });
      response.on("end", () => {
        try {
          resolve({ status: response.statusCode, body: JSON.parse(body) });
        } catch (error) {
          reject(error);
        }
      });
    });
    request.on("timeout", () => request.destroy(new Error("timeout")));
    request.on("error", reject);
    request.end(payload);
  });
}

async function waitForSnapshot(port, timeoutMs = 5000) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      return await getJson(port);
    } catch (error) {
      lastError = error;
      await new Promise(resolve => setTimeout(resolve, 75));
    }
  }
  throw lastError || new Error("server did not become ready");
}

async function closeChild(child) {
  if (child.exitCode !== null) return;
  child.kill();
  await Promise.race([
    once(child, "exit"),
    new Promise(resolve => setTimeout(resolve, 2000))
  ]);
  if (child.exitCode === null) child.kill("SIGKILL");
}

test("server refuses unauthenticated non-loopback binding", async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-bind-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const port = await freePort();
  const { child, getOutput } = launch({ root, host: "0.0.0.0", port });
  const [code] = await Promise.race([
    once(child, "exit"),
    new Promise((_, reject) => setTimeout(() => reject(new Error("server unexpectedly stayed alive")), 4000))
  ]);
  assert.notEqual(code, 0);
  assert.match(getOutput(), /Refusing unauthenticated non-loopback bind host/i);
});

test("server rejects untrusted Host headers", async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-host-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const port = await freePort();
  const { child } = launch({ root, port });
  t.after(() => closeChild(child));
  await waitForSnapshot(port);

  const response = await getJson(port, "/api/status", { Host: `evil.example:${port}` });
  assert.equal(response.status, 400);
  assert.match(response.body.error, /Host not allowed/i);
});

test("corrupt primary and backup state fail closed into read-only degraded mode", async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-corrupt-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const port = await freePort();
  const dataDir = path.join(root, "data");
  fs.mkdirSync(dataDir, { recursive: true });
  fs.writeFileSync(path.join(dataDir, "control-plane.json"), "{ definitely not json", "utf8");
  fs.writeFileSync(path.join(dataDir, "control-plane.json.bak"), "{ also not json", "utf8");
  const { child } = launch({ root, port });
  t.after(() => closeChild(child));
  const response = await waitForSnapshot(port);
  assert.equal(response.status, 200);
  assert.equal(response.body.controller.health.mode, "degraded");
  assert.equal(response.body.settings.readOnly, true);
  assert.equal(response.body.settings.dispatchPaused, true);
});

test("backup recovery preserves uncertain work and refuses to call it complete", async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-backup-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const port = await freePort();
  const dataDir = path.join(root, "data");
  fs.mkdirSync(dataDir, { recursive: true });
  fs.writeFileSync(path.join(dataDir, "control-plane.json"), "{ broken", "utf8");
  fs.writeFileSync(path.join(dataDir, "control-plane.json.bak"), JSON.stringify({
    version: 2,
    agents: [{
      id: "support-old",
      role: "support",
      roleLabel: "Support Agent",
      taskId: "task-old",
      task: "Investigate rollback",
      status: "running",
      pid: 424242,
      branchName: "agent/old",
      baseBranch: "main",
      leaseId: "lease-old",
      startedAt: new Date().toISOString()
    }],
    tasks: [{ id: "task-old", objective: "Investigate rollback", status: "running", agentId: "support-old" }],
    leases: [{ id: "lease-old", boundary: "rollback", ownerAgentId: "support-old", taskId: "task-old", status: "active" }],
    events: []
  }, null, 2), "utf8");
  const { child } = launch({ root, port });
  t.after(() => closeChild(child));
  const response = await waitForSnapshot(port);
  assert.equal(response.status, 200);
  assert.equal(response.body.controller.health.mode, "recovered");
  const agent = response.body.agents.find(item => item.id === "support-old");
  assert.equal(agent.status, "orphaned");
  assert.notEqual(agent.completionEvidence, "authoritative-exit");
  const lease = response.body.leases.find(item => item.id === "lease-old");
  assert.equal(lease.status, "active");
});


test("authoritative exit gathers async evidence before fresh state mutation", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const start = source.indexOf('child.on("exit", async (code, signal) => {');
  const end = source.indexOf('child.on("error"', start);
  assert.ok(start >= 0 && end > start, "authoritative child exit handler must exist");
  const handler = source.slice(start, end);
  const evidenceAt = handler.indexOf('await git(["rev-parse", branchName])');
  const loadAt = handler.indexOf("const current = loadState()");
  const saveAt = handler.indexOf("saveState(current)");
  assert.ok(evidenceAt >= 0, "exit handler must collect branch evidence");
  assert.ok(loadAt > evidenceAt, "authoritative state must be loaded only after async evidence is collected");
  assert.ok(saveAt > loadAt, "fresh state must be saved after mutation");
  assert.equal(/\bawait\b/.test(handler.slice(loadAt, saveAt)), false, "no async yield may occur between authoritative state load and save");
  assert.match(handler, /ownerSessionId === SESSION_ID/);
  assert.match(handler, /item\?\.pid === child\.pid/);
});


test("provider-capacity evidence is selected before no-work recovery", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  assert.match(source, /return candidates\.find\(isProviderCapacityErrorMessage\) \|\| candidates\[0\] \|\| "";/);
  const refreshStart = source.indexOf("function refreshState() {");
  const refreshEnd = source.indexOf("\nasync function", refreshStart);
  assert.ok(refreshStart >= 0 && refreshEnd > refreshStart);
  const refresh = source.slice(refreshStart, refreshEnd);
  assert.match(refresh, /selectAgentTerminalMessage\(\s*readTextIfExists\(agent\.lastMessagePath\),\s*readLogSummary\(agent\.logPath\)\s*\)/s);
  assert.match(refresh, /recoveryStatus = "provider-capacity"/);
  const exitStart = source.indexOf('child.on("exit", async (code, signal) => {');
  const exitEnd = source.indexOf('child.on("error"', exitStart);
  assert.ok(exitStart >= 0 && exitEnd > exitStart);
  const handler = source.slice(exitStart, exitEnd);
  assert.match(handler, /selectAgentTerminalMessage\(\s*readTextIfExists\(item\.lastMessagePath\),\s*readLogSummary\(item\.logPath\)\s*\)/s);
  assert.ok(handler.indexOf("classifyAuthoritativeExit") < handler.indexOf("noWorkTerminationDecision"));
  assert.match(handler, /failureClass = "provider-capacity"/);
});

test("pre-launch setup is completed before worker spawn and has convergence cleanup", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const start = source.indexOf("async function deployOne({");
  const end = source.indexOf('child.on("exit"', start);
  assert.ok(start >= 0 && end > start, "deployOne implementation must exist");
  const deploy = source.slice(start, end);
  const findCodexAt = deploy.indexOf("codex = findCodex()");
  const promptWriteAt = deploy.indexOf("fs.writeFileSync(promptPath");
  const spawnAt = deploy.indexOf("child = spawn(executable");
  assert.ok(findCodexAt >= 0 && promptWriteAt > findCodexAt);
  assert.ok(spawnAt > promptWriteAt, "fallible prompt/provider setup must finish before spawning the owned worker process");
  assert.match(deploy, /reason: "pre-launch-setup-failed"/);
  assert.match(source, /applyPreLaunchFailure\(failed,/);
});

test("counted deploy preflights whole-batch capacity before launching the first worker", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const route = source.slice(
    source.indexOf('if (req.method === "POST" && pathname === "/api/deploy")'),
    source.indexOf("const reviewMatch", source.indexOf('pathname === "/api/deploy"'))
  );
  const preflightAt = route.indexOf("deploymentBatchCapacity");
  const firstDeployAt = route.indexOf("await deployOne");
  assert.ok(preflightAt >= 0 && firstDeployAt > preflightAt);
  assert.match(route, /No workers were launched/);
});


test("assist autonomy blocks dispatch and governed mutations before side effects", async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-autonomy-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const port = await freePort();
  const { child } = launch({ root, port });
  t.after(() => closeChild(child));
  await waitForSnapshot(port);

  const deploy = await postJson(port, "/api/deploy", {
    role: "support",
    task: "Should not launch"
  });
  assert.equal(deploy.status, 403);
  assert.match(deploy.body.error, /dispatch-support/i);

  const review = await postJson(port, "/api/agents/missing/review", {});
  assert.equal(review.status, 403);
  assert.match(review.body.error, /request-review/i);

  const proposal = await postJson(port, "/api/improvements", {
    request: "Improve the controller safely"
  });
  assert.equal(proposal.status, 201);
  assert.ok(proposal.body.id);

  const start = await postJson(port, `/api/improvements/${encodeURIComponent(proposal.body.id)}/start`, {});
  assert.equal(start.status, 403);
  assert.match(start.body.error, /prepare-integration/i);

  const evidence = await postJson(port, "/api/tasks/missing/evidence", { type: "test" });
  assert.equal(evidence.status, 403);
  assert.match(evidence.body.error, /maintain-continuity/i);

  const verdict = await postJson(port, "/api/integration/missing/review-verdict", { verdict: "approved" });
  assert.equal(verdict.status, 403);
  assert.match(verdict.body.error, /prepare-integration/i);

  const takeover = await postJson(port, "/api/agents/missing/takeover", {});
  assert.equal(takeover.status, 403);
  assert.match(takeover.body.error, /maintain-continuity/i);

  const observed = await postJson(port, "/api/control/settings", { autonomyLevel: "observe" });
  assert.equal(observed.status, 200);

  const preview = await postJson(port, "/api/workflows/review/preview", { objective: "Review current work" });
  assert.equal(preview.status, 403);
  assert.match(preview.body.error, /preview/i);

  const routing = await postJson(port, "/api/control/routing-manifest", {
    source: "should-be-blocked",
    mode: "authoritative",
    assignments: []
  });
  assert.equal(routing.status, 403);
  assert.match(routing.body.error, /preview/i);
});


test("worker evidence and review verdicts require exact scoped task capabilities", async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-capability-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const port = await freePort();
  const dataDir = path.join(root, "data");
  fs.mkdirSync(dataDir, { recursive: true });

  const verifyToken = "verify-capability-token";
  const reviewerToken = "reviewer-capability-token";
  const candidateToken = "candidate-capability-token";
  const hash = value => createHash("sha256").update(value, "utf8").digest("hex");
  const now = new Date().toISOString();
  fs.writeFileSync(path.join(dataDir, "control-plane.json"), JSON.stringify({
    version: 5,
    settings: { autonomyLevel: "engineering-autopilot" },
    agents: [
      { id: "candidate-1", role: "main", roleLabel: "Primary Programmer", taskId: "task-candidate", task: "Implement", status: "done", exitCode: 0, completionEvidence: "authoritative-exit", targetAgentId: null, startedAt: now, finishedAt: now },
      { id: "verifier-1", role: "test", roleLabel: "Verification", taskId: "task-verify", task: "Verify", status: "done", exitCode: 0, completionEvidence: "authoritative-exit", targetAgentId: "candidate-1", startedAt: now, finishedAt: now },
      { id: "reviewer-1", role: "reviewer", roleLabel: "Reviewer", taskId: "task-review", task: "Review", status: "done", exitCode: 0, completionEvidence: "authoritative-exit", targetAgentId: "candidate-1", startedAt: now, finishedAt: now }
    ],
    tasks: [
      { id: "task-candidate", objective: "Implement", status: "candidate", agentId: "candidate-1", evidence: [], workerCapabilityHash: hash(candidateToken) },
      { id: "task-verify", objective: "Verify", status: "done", agentId: "verifier-1", evidence: [], workerCapabilityHash: hash(verifyToken) },
      { id: "task-review", objective: "Review", status: "done", agentId: "reviewer-1", evidence: [], workerCapabilityHash: hash(reviewerToken) }
    ],
    leases: [],
    events: [],
    notifications: [],
    improvements: [],
    promptHistory: []
  }, null, 2), "utf8");

  const { child } = launch({ root, port });
  t.after(() => closeChild(child));
  await waitForSnapshot(port);

  const missingEvidence = await postJson(port, "/api/tasks/task-verify/evidence", {
    type: "verification", result: "pass"
  });
  assert.equal(missingEvidence.status, 403);
  assert.match(missingEvidence.body.error, /scoped capability/i);

  const wrongEvidence = await postJson(port, "/api/tasks/task-verify/evidence", {
    type: "verification", result: "pass"
  }, { "X-Agent-Control-Task-Token": candidateToken });
  assert.equal(wrongEvidence.status, 403);

  const acceptedEvidence = await postJson(port, "/api/tasks/task-verify/evidence", {
    type: "verification", result: "pass", sourceSha: "abc"
  }, { "X-Agent-Control-Task-Token": verifyToken });
  assert.equal(acceptedEvidence.status, 201);
  assert.equal(acceptedEvidence.body.result, "pass");

  const forgedVerdict = await postJson(port, "/api/integration/candidate-1/review-verdict", {
    verdict: "approved", by: "forged-provider"
  }, { "X-Agent-Control-Task-Token": candidateToken });
  assert.equal(forgedVerdict.status, 403);

  const acceptedVerdict = await postJson(port, "/api/integration/candidate-1/review-verdict", {
    verdict: "approved", by: "forged-provider"
  }, { "X-Agent-Control-Task-Token": reviewerToken });
  assert.equal(acceptedVerdict.status, 200);
  assert.equal(acceptedVerdict.body.reviewVerdict, "approved");
  assert.equal(acceptedVerdict.body.reviewVerdictBy, "reviewer-1");
});

test("broad workflow execution fails closed until a routing manifest is current", async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-routing-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const port = await freePort();
  const { child } = launch({ root, port });
  t.after(() => closeChild(child));
  await waitForSnapshot(port);

  const settings = await postJson(port, "/api/control/settings", { autonomyLevel: "coordinate" });
  assert.equal(settings.status, 200);
  assert.equal(settings.body.autonomyLevel, "coordinate");

  const blocked = await postJson(port, "/api/workflows/usual-swarm/execute", {
    objective: "Continue current repository work"
  });
  assert.equal(blocked.status, 201);
  assert.equal(blocked.body.created.length, 0);
  assert.match(blocked.body.blocked[0], /current routing manifest/i);

  const manifest = await postJson(port, "/api/control/routing-manifest", {
    source: "test-routing-board",
    mode: "authoritative",
    ttlMinutes: 30,
    assignments: [
      { slotId: "manager", role: "manager", status: "claimed", owner: "manager" },
      { slotId: "main", role: "main", status: "claimed", branch: "feature/main" },
      { slotId: "support-1", role: "support", lane: "safety", status: "claimed", branch: "support/safety" }
    ]
  });
  assert.equal(manifest.status, 200);
  assert.equal(manifest.body.source, "test-routing-board");
  assert.equal(manifest.body.mode, "authoritative");

  const reconciled = await postJson(port, "/api/workflows/usual-swarm/execute", {
    objective: "Continue current repository work"
  });
  assert.equal(reconciled.status, 201);
  assert.equal(reconciled.body.created.length, 0);
  assert.equal(reconciled.body.blocked.length, 0);
  assert.equal(reconciled.body.plan.ownership.reconciled, true);
});

test("explicit Start Swarm normalizes operator friction without weakening automatic workflow routing safety", async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-one-click-start-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const port = await freePort();
  const { child } = launch({ root, port });
  t.after(() => closeChild(child));
  await waitForSnapshot(port);

  const manifest = await postJson(port, "/api/control/routing-manifest", {
    source: "test-occupied-swarm",
    mode: "authoritative",
    ttlMinutes: 30,
    assignments: [
      { slotId: "manager", role: "manager", status: "claimed", owner: "existing-manager" },
      { slotId: "main", role: "main", status: "claimed", owner: "existing-main" },
      { slotId: "support-1", role: "support", lane: "architecture", status: "claimed", owner: "existing-support-1" },
      { slotId: "support-2", role: "support", lane: "tests", status: "claimed", owner: "existing-support-2" },
      { slotId: "support-3", role: "support", lane: "adversarial", status: "claimed", owner: "existing-support-3" },
      { slotId: "support-4", role: "support", lane: "continuity", status: "claimed", owner: "existing-support-4" }
    ]
  });
  assert.equal(manifest.status, 200);

  const restricted = await postJson(port, "/api/control/settings", {
    autonomyLevel: "observe",
    dispatchPaused: true,
    readOnly: true,
    draining: true
  });
  assert.equal(restricted.status, 200);
  const stopped = await postJson(port, "/api/control/emergency-stop", {});
  assert.equal(stopped.status, 200);

  const started = await postJson(port, "/api/swarm/start", {
    objective: "Continue the highest-value unfinished project work"
  });
  assert.equal(started.status, 201);
  assert.equal(started.body.created.length, 0);
  assert.equal(started.body.blocked.length, 0);

  const snapshot = await getJson(port);
  assert.equal(snapshot.status, 200);
  assert.equal(snapshot.body.settings.autonomyLevel, "coordinate");
  assert.equal(snapshot.body.settings.dispatchPaused, false);
  assert.equal(snapshot.body.settings.readOnly, false);
  assert.equal(snapshot.body.settings.draining, false);
  assert.equal(snapshot.body.settings.emergencyStop, false);
});

test("federated bridge observations drive normalized live counts without duplicate agents", async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-federation-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const port = await freePort();
  const { child, dataDir } = launch({ root, port });
  t.after(() => closeChild(child));
  await waitForSnapshot(port);

  const observed = await postJson(port, "/api/federation/observations", {
    provider: "chatgpt",
    source_id: "conversation-123",
    role: "manager",
    machine: "cloud",
    state: "working",
    task: "Coordinate Agent Control",
    correlation_keys: ["work-item:59"]
  });
  assert.equal(observed.status, 200);
  assert.equal(observed.body.accepted, 1);
  const firstId = observed.body.agents[0].agent_id;

  const heartbeat = await postJson(port, "/api/federation/heartbeat", {
    provider: "chatgpt",
    source_id: "conversation-123",
    state: "tool_wait",
    correlation_keys: ["work-item:59"]
  });
  assert.equal(heartbeat.status, 200);
  assert.equal(heartbeat.body.agents[0].agent_id, firstId);

  const correlated = await postJson(port, "/api/federation/observations", {
    provider: "github",
    source_id: "workflow-run-999",
    state: "tool_wait",
    pr_number: 59,
    correlation_keys: ["work-item:59"]
  });
  assert.equal(correlated.status, 200);
  assert.equal(correlated.body.agents[0].agent_id, firstId);

  const federation = await getJson(port, "/api/federation");
  assert.equal(federation.status, 200);
  assert.equal(federation.body.agents.length, 1);
  assert.equal(federation.body.counts.live, 1);
  assert.equal(federation.body.counts.tool_wait, 1);
  assert.deepEqual(new Set(federation.body.agents[0].providers), new Set(["chatgpt", "github"]));

  const snapshot = await getJson(port, "/api/snapshot");
  assert.equal(snapshot.body.telemetry.running, 1);
  assert.equal(snapshot.body.telemetry.total, 1);
  assert.equal(snapshot.body.federatedAgents.length, 1);

  const persisted = JSON.parse(fs.readFileSync(path.join(dataDir, "control-plane.json"), "utf8"));
  assert.equal(persisted.version, 9);
  assert.equal(persisted.federation.agents.length, 1);
});

test("stale external heartbeat is visible but excluded from active-agent count", async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-federation-stale-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const port = await freePort();
  const { child } = launch({ root, port });
  t.after(() => closeChild(child));
  await waitForSnapshot(port);

  const oldHeartbeat = new Date(Date.now() - 10 * 60 * 1000).toISOString();
  const observed = await postJson(port, "/api/federation/observations", {
    provider: "chatgpt",
    source_id: "conversation-stale",
    state: "working",
    heartbeat_at: oldHeartbeat
  });
  assert.equal(observed.status, 200);

  const federation = await getJson(port, "/api/federation");
  assert.equal(federation.body.counts.live, 0);
  assert.equal(federation.body.counts.disconnected, 1);
  assert.equal(federation.body.agents[0].effective_state, "disconnected");
});

test("overall swarm goal is persisted by autopilot and propagated through every perpetual phase", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const startAt = source.indexOf("function startAutopilot");
  const candidateAt = source.indexOf("function autopilotCandidate", startAt);
  assert.ok(startAt >= 0 && candidateAt > startAt);
  const startBlock = source.slice(startAt, candidateAt);
  assert.match(startBlock, /const overallGoal = String\(body\.overallGoal \|\| ""\)\.trim\(\)/);
  assert.match(startBlock, /overallGoal,/);

  const helperAt = source.indexOf("function autopilotOverallGoalLine");
  const stepAt = source.indexOf("async function autopilotStep", helperAt);
  assert.ok(helperAt >= 0 && stepAt > helperAt);
  const propagation = source.slice(helperAt, stepAt);
  for (const phase of [
    "dispatchAutopilotImplementation",
    "dispatchAutopilotVerification",
    "dispatchAutopilotReview",
    "dispatchAutopilotRepair",
    "dispatchAutopilotIntegration",
    "dispatchAutopilotHygiene",
    "dispatchAutopilotExpansion"
  ]) {
    const phaseAt = propagation.indexOf(`async function ${phase}`);
    assert.ok(phaseAt >= 0, `missing ${phase}`);
  }
  assert.ok((propagation.match(/autopilotOverallGoalLine\(state\)/g) || []).length >= 7);
});

test("engineering autopilot exposes governed control routes and a periodic internal loop", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  for (const route of [
    "/api/autopilot",
    "/api/autopilot/start",
    "/api/autopilot/pause",
    "/api/autopilot/resume",
    "/api/autopilot/stop",
    "/api/autopilot/step"
  ]) {
    assert.match(source, new RegExp(route.replaceAll("/", "\\/")));
  }
  assert.match(source, /setInterval\(\(\) => \{\s*void autopilotStep\(\)/);
  assert.match(source, /git\(\["ls-remote", "origin", "refs\/heads\/main"\]\)/);
  const autopilotCore = fs.readFileSync(path.resolve(HERE, "..", "lib", "autopilot-core.mjs"), "utf8");
  assert.match(autopilotCore, /operator-integration-approval-required/);
});

test("perpetual one-click path renews ownership freshness without reviving stale assignments", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  assert.match(source, /body\.perpetual === true\s*\? await startPerpetualSwarm\(body\)/);
  assert.match(source, /operator-start-perpetual-swarm/);

  const start = source.indexOf("function refreshPerpetualRoutingLease");
  const end = source.indexOf("function persistAutopilotPhase", start);
  assert.ok(start >= 0 && end > start);
  const block = source.slice(start, end);
  assert.match(block, /perpetual-controller-reconciliation/);
  assert.match(block, /mode:\s*"overlay"/);
  assert.match(block, /assignments:\s*\[\]/);
  assert.doesNotMatch(block, /\.\.\.state\.settings\?\.routingManifest/);
});

test("one-click perpetual start resumes paused runs and rejects non-perpetual mode conflicts", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const start = source.indexOf("async function startPerpetualSwarm");
  const end = source.indexOf("function buildTakeoverForAgent", start);
  assert.ok(start >= 0 && end > start);
  const block = source.slice(start, end);

  const validateAt = block.indexOf('if (!objective) throw new Error("Perpetual swarm objective is required.")');
  const normalizeAt = block.indexOf("updateControlSettings({");
  assert.ok(validateAt >= 0 && normalizeAt > validateAt, "invalid starts must not clear safety/control state");

  assert.match(block, /before\.autopilot\?\.enabled && !before\.autopilot\.perpetual/);
  assert.match(block, /error\.statusCode = 409/);
  assert.match(block, /before\.autopilot\.paused/);
  assert.match(block, /resumeAutopilot\("operator-start-perpetual-swarm-resume"\)/);
  assert.match(block, /resumed: true/);
  assert.match(block, /alreadyRunning: false/);
});

test("perpetual recovery preserves takeover before proven stop and persists replacement before redispatch", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const dispatchStart = source.indexOf("async function dispatchPerpetualReplacement");
  const start = source.indexOf("async function reconcilePerpetualReplacement");
  const end = source.indexOf("function gateAutopilot", start);
  assert.ok(dispatchStart >= 0 && start > dispatchStart);
  assert.ok(start >= 0 && end > start);
  const dispatchBlock = source.slice(dispatchStart, start);
  const block = source.slice(start, end);

  const capacityAt = block.indexOf("providerCapacityCircuit(state)");
  const takeoverAt = block.indexOf("buildTakeoverForAgent(agent.id, { persist: true, safetyControl: true })");
  const stopAt = block.indexOf("await stopAgent(agent.id)");
  const pendingAt = block.indexOf("pendingReplacement:", stopAt);
  const redispatchAt = block.indexOf("return reconcilePerpetualReplacement(refreshState())");

  assert.ok(capacityAt >= 0);
  assert.ok(takeoverAt > capacityAt);
  assert.ok(stopAt > takeoverAt);
  assert.ok(pendingAt > stopAt);
  assert.ok(redispatchAt > pendingAt);
  assert.match(block, /replacement-dispatch-failed/);
  assert.match(block, /autopilot\.stale-replacement-pending/);
  assert.match(dispatchBlock, /executionMode: "direct"/);
});

test("perpetual runtime errors and recoverable gates schedule retries instead of disabling the run", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const start = source.indexOf("async function autopilotStep");
  const end = source.indexOf("function sendJson", start);
  assert.ok(start >= 0 && end > start);
  const block = source.slice(start, end);
  assert.match(block, /schedulePerpetualRetry\("autopilot-runtime-error"/);
  assert.match(block, /perpetualRecoverableGatePatch/);
  assert.match(block, /perpetualSafetyHoldReason/);
  assert.match(block, /reconcilePerpetualReplacement/);
});

test("controller publishes process identity for ownership-verified watchdog restart", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  assert.match(source, /controller-process\.json/);
  assert.match(source, /function writeControllerProcessIdentity/);
  assert.match(source, /pid: process\.pid/);
  assert.match(source, /writeControllerProcessIdentity\(\)/);
  assert.match(source, /process\.once\("exit", clearControllerProcessIdentity\)/);
});

test("swarm recovery dispatch is evaluated before waiting for the active wave to finish", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const start = source.indexOf("async function reconcileSwarmTailRecoveries");
  const end = source.indexOf("async function killProcessTree", start);
  const block = source.slice(start, end);
  const planAt = block.indexOf("planSwarmTailRecoveryBatch");
  const completionGateAt = block.indexOf("if (scopedAgents.some(agent => coreIsActiveStatus(agent.status))) return;");
  assert.ok(planAt >= 0);
  assert.ok(completionGateAt > planAt);
});

test("workflow execution preflights the full plan before launching its first worker", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const start = source.indexOf("async function executeWorkflow");
  const end = source.indexOf("function buildTakeoverForAgent", start);
  const block = source.slice(start, end);
  const capacityAt = block.indexOf("deploymentBatchCapacity");
  const leaseAt = block.indexOf("workflowLeasePreflight");
  const loopAt = block.indexOf("for (const work of plan.steps)");
  const deployAt = block.indexOf("await deployOne");
  assert.ok(capacityAt >= 0);
  assert.ok(leaseAt > capacityAt);
  assert.ok(loopAt > leaseAt);
  assert.ok(deployAt > leaseAt);
  assert.match(block, /No workers were launched/);
});

test("local Codex launch uses approval-never workspace-write contract", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  assert.match(source, /const args = \[\s*"-a", "never",\s*"-s", "workspace-write",\s*"exec",\s*"--json",\s*"--skip-git-repo-check"/);
  assert.doesNotMatch(source, /--approve-for-me|danger-full-access|dangerously-bypass-approvals-and-sandbox/);
});


test("heaven2 auto placement uses authenticated Heaven Local Bridge and fails closed", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const start = source.indexOf("async function resolveWorkerPlacement");
  const end = source.indexOf("function buildPrompt", start);
  assert.ok(start >= 0 && end > start);
  const placement = source.slice(start, end);
  assert.match(placement, /hostname === "heaven2" \? "heaven"/);
  assert.match(placement, /await inspectHeavenBridge\(\{ sync: true \}\)/);
  assert.match(placement, /refusing to silently execute heavy work on heaven2/i);
  assert.match(placement, /provider: "heaven-bridge"/);
});

test("remote Heaven execution keeps an owned local runner and authoritative cancellation path", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const start = source.indexOf("async function deployOne({");
  const end = source.indexOf('child.on("exit"', start);
  const deploy = source.slice(start, end);
  assert.match(deploy, /HEAVEN_BRIDGE_RUNNER/);
  assert.match(deploy, /executionProvider: placement\.provider/);
  assert.match(deploy, /remoteJobId/);
  assert.match(deploy, /AGENT_CONTROL_TASK_TOKEN:\s*taskCapability\.token/);
  assert.match(source, /cancelHeavenBridgeJob\(agent\.remoteJobId/);
  assert.match(source, /bridgeResultSucceeded\(cancellation\)/);
});

test("stop safety deduplicates concurrent termination and rejects exited child identities", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  assert.match(source, /const stopOperations = new Map\(\)/);
  const start = source.indexOf("async function stopAgent(id)");
  const end = source.indexOf("async function branchDivergence", start);
  const block = source.slice(start, end);
  assert.match(block, /stopOperations\.get\(id\)/);
  assert.match(block, /stopAgentOnce\(id\)/);
  assert.match(block, /child\.exitCode === null/);
  assert.match(block, /child\.signalCode === null/);
});


test("engineering autopilot explicitly authorizes scoped heaven repository work", () => {
  const source = fs.readFileSync(path.join(HERE, "..", "server.mjs"), "utf8");
  const implementation = source.slice(
    source.indexOf("async function dispatchAutopilotImplementation"),
    source.indexOf("async function dispatchAutopilotVerification")
  );
  const verification = source.slice(
    source.indexOf("async function dispatchAutopilotVerification"),
    source.indexOf("async function dispatchAutopilotReview")
  );
  const review = source.slice(
    source.indexOf("async function dispatchAutopilotReview"),
    source.indexOf("async function dispatchAutopilotRepair")
  );
  const repair = source.slice(
    source.indexOf("async function dispatchAutopilotRepair"),
    source.indexOf("async function autopilotStep")
  );

  for (const section of [implementation, verification, review, repair]) {
    assert.match(section, /repositoryWriteAuthorized:\s*true/);
  }
});


test("operator routes derive scoped Heaven repository authorization server-side", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const deployRoute = source.slice(
    source.indexOf('if (req.method === "POST" && pathname === "/api/deploy")'),
    source.indexOf("const reviewMatch", source.indexOf('pathname === "/api/deploy"'))
  );
  assert.match(deployRoute, /repositoryWriteAuthorized:\s*true/);
  assert.doesNotMatch(deployRoute, /Boolean\(body\.repositoryWriteAuthorized\)/);

  const reviewRoute = source.slice(
    source.indexOf("const reviewMatch"),
    source.indexOf("const promptMatch", source.indexOf("const reviewMatch"))
  );
  assert.match(reviewRoute, /repositoryWriteAuthorized:\s*true/);

  const workflowRoute = source.slice(
    source.indexOf("const workflowMatch"),
    source.indexOf('pathname === "/api/autopilot"', source.indexOf("const workflowMatch"))
  );
  assert.match(workflowRoute, /repositoryWriteAuthorized:\s*true/);
});


test("multi-step workflows gate fan-out on startup viability after every launched worker", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const start = source.indexOf("async function executeWorkflow");
  const end = source.indexOf("function buildTakeoverForAgent", start);
  assert.ok(start >= 0 && end > start);
  const block = source.slice(start, end);
  const deployAt = block.indexOf("const agent = await deployOne");
  const createdAt = block.indexOf("created.push(agent)", deployAt);
  const guardAt = block.indexOf("await waitForWorkflowStartupViability(agent.id)", createdAt);
  const stopAt = block.indexOf("if (!startup.allowed)", guardAt);
  assert.ok(deployAt >= 0);
  assert.ok(createdAt > deployAt);
  assert.ok(guardAt > createdAt);
  assert.ok(stopAt > guardAt);
  assert.match(block.slice(stopAt), /blocked\.push\(\{ work, error \}\)[\s\S]*?break;/);

  const guardStart = source.indexOf("async function waitForWorkflowStartupViability");
  assert.ok(guardStart >= 0 && guardStart < start);
  const guard = source.slice(guardStart, start);
  assert.match(guard, /const state = refreshState\(\)/);
  assert.match(guard, /providerCapacityCircuit\(state\)/);
  assert.match(guard, /reason: "provider-capacity"/);
  assert.match(guard, /\["failed", "capacity-blocked", "stopped", "interrupted", "orphaned", "retry-pending"\]/);
});

test("agent failures persist to a redacted durable ledger and are exposed in snapshots", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  assert.match(source, /FAILURE_LOG_FILE = path\.join\(DATA_DIR, "failures\.jsonl"\)/);
  assert.match(source, /function sanitizeFailureText/);
  assert.match(source, /\[REDACTED\]/);
  assert.match(source, /function appendFailureLog/);
  assert.match(source, /schema: "agent-control\/failure\/v1"/);
  assert.match(source, /function recordAgentFailure/);
  assert.match(source, /category: "deployment"[\s\S]*?phase: "pre-launch"/);
  assert.match(source, /category: "provider-capacity"[\s\S]*?phase: "runtime-capacity-detection"/);
  assert.match(source, /phase: "process-exit"/);
  assert.match(source, /phase: "process-error"/);
  assert.match(source, /recentFailures: readFailureLog\(80\)/);
  assert.match(source, /pathname === "\/api\/failures"/);

  const helperStart = source.indexOf("function appendFailureLog");
  const helperEnd = source.indexOf("async function git", helperStart);
  assert.ok(helperStart >= 0 && helperEnd > helperStart);
  const helper = source.slice(helperStart, helperEnd);
  assert.doesNotMatch(helper, /promptPath|workerCapabilityHash|AGENT_CONTROL_TASK_TOKEN:/);
});


test("perpetual one-click prompts evolve across every controller phase", () => {
  const source = fs.readFileSync(SERVER, "utf8");

  const helperStart = source.indexOf("function autopilotSwarmContext");
  const helperEnd = source.indexOf("function autopilotImplementationObjective", helperStart);
  assert.ok(helperStart >= 0 && helperEnd > helperStart);
  const helper = source.slice(helperStart, helperEnd);
  assert.match(helper, /workflowId:\s*"perpetual-autopilot"/);
  assert.match(helper, /waveId:\s*`perpetual:\$\{runId\}:\$\{cycle\}`/);
  assert.match(helper, /source:\s*`one-click-perpetual:\$\{normalizedPhase\}`/);
  assert.match(helper, /phases\.indexOf\(normalizedPhase\)/);

  const workflowStart = source.indexOf("async function executeWorkflow");
  const workflowEnd = source.indexOf("async function startUsualSwarm", workflowStart);
  assert.ok(workflowStart >= 0 && workflowEnd > workflowStart);
  const workflow = source.slice(workflowStart, workflowEnd);
  assert.match(workflow, /const requestedSwarmContext = body\.swarmContext/);
  assert.match(workflow, /requestedSwarmContext\?\.workflowId \|\| workflowId/);
  assert.match(workflow, /requestedSwarmContext\?\.mission \|\| plan\.mission/);
  assert.match(workflow, /requestedSwarmContext\?\.source \|\| "one-click-workflow"/);

  const reviewStart = source.indexOf("async function deployReview");
  const reviewEnd = source.indexOf("async function previewWorkflow", reviewStart);
  assert.ok(reviewStart >= 0 && reviewEnd > reviewStart);
  assert.match(source.slice(reviewStart, reviewEnd), /swarmContext:\s*body\.swarmContext \|\| null/);

  const expectations = [
    ["dispatchAutopilotImplementation", "dispatchAutopilotVerification", /autopilotSwarmContext\(state, "implement", objective\)/],
    ["dispatchAutopilotVerification", "dispatchAutopilotReview", /autopilotSwarmContext\(state, "verify", state\.autopilot\?\.objective\)/],
    ["dispatchAutopilotReview", "dispatchAutopilotRepair", /autopilotSwarmContext\(state, "review", state\.autopilot\?\.objective\)/],
    ["dispatchAutopilotRepair", "dispatchAutopilotIntegration", /autopilotSwarmContext\(state, "repair", state\.autopilot\?\.objective\)/],
    ["dispatchAutopilotIntegration", "dispatchAutopilotHygiene", /autopilotSwarmContext\(state, "integrate", state\.autopilot\?\.objective\)/],
    ["dispatchAutopilotHygiene", "dispatchAutopilotExpansion", /autopilotSwarmContext\(state, "hygiene", state\.autopilot\?\.objective\)/],
    ["dispatchAutopilotExpansion", "autopilotIntegrationVerified", /autopilotSwarmContext\(state, "expand", managerPriority \|\| state\.autopilot\?\.objective\)/]
  ];

  for (const [startName, endName, pattern] of expectations) {
    const start = source.indexOf(`async function ${startName}`);
    const end = source.indexOf(`async function ${endName}`, start);
    assert.ok(start >= 0 && end > start, `missing block ${startName}`);
    assert.match(source.slice(start, end), pattern);
  }

  const replacementStart = source.indexOf("async function dispatchPerpetualReplacement");
  const replacementEnd = source.indexOf("async function reconcilePerpetualReplacement", replacementStart);
  assert.ok(replacementStart >= 0 && replacementEnd > replacementStart);
  const replacement = source.slice(replacementStart, replacementEnd);
  assert.match(replacement, /const replacementSwarmContext = autopilotSwarmContext/);
  assert.match(replacement, /source:\s*"perpetual-stale-replacement"/);
});
