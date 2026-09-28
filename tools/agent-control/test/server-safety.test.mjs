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
