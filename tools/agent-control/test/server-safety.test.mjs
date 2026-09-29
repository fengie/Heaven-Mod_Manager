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

function getJson(port, pathname = "/api/snapshot") {
  return new Promise((resolve, reject) => {
    const request = http.get({ host: "127.0.0.1", port, path: pathname, timeout: 700 }, response => {
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

function postJson(port, pathname, value = {}) {
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
        "Content-Length": Buffer.byteLength(payload)
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


test("pre-launch setup is completed before worker spawn and has convergence cleanup", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const start = source.indexOf("async function deployOne({");
  const end = source.indexOf('child.on("exit"', start);
  assert.ok(start >= 0 && end > start, "deployOne implementation must exist");
  const deploy = source.slice(start, end);
  const findCodexAt = deploy.indexOf("codex = findCodex()");
  const promptWriteAt = deploy.indexOf("fs.writeFileSync(promptPath");
  const spawnAt = deploy.indexOf("child = spawn(codex");
  assert.ok(findCodexAt >= 0 && promptWriteAt > findCodexAt);
  assert.ok(spawnAt > promptWriteAt, "fallible prompt/Codex setup must finish before spawning a worker");
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
  assert.equal(persisted.version, 5);
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
