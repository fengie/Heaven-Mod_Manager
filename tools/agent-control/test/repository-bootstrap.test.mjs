import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import net from "node:net";
import http from "node:http";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { execFileHidden } from "../lib/background-process.mjs";
import { buildRepositoryBootstrap, verifyRepositoryBootstrap, readRepositoryContext, repositoryManifest, MAX_BOOTSTRAP_BYTES, MAX_CORE_BYTES, BOOTSTRAP_TTL_MS } from "../lib/repository-bootstrap.mjs";
import { REQUIRED_REPOSITORY_TRAINING_PATHS, REPOSITORY_CONTEXT_INDEX_PATHS, renderAgentPrompt } from "../lib/prompt-templates.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, "../../..");
const HEAD = "a".repeat(40), NOW = Date.parse("2026-09-30T18:00:00Z");
const MANAGER = "_AGENT_TRAINING/PROMPT_TEMPLATES/01_MANAGER_ORCHESTRATOR.txt";
const git = async args => args[0] === "rev-parse" ? HEAD : args[0] === "branch" ? "codex/test" : "";
function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "mhw-bootstrap-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  for (const document of [...REQUIRED_REPOSITORY_TRAINING_PATHS, ...REPOSITORY_CONTEXT_INDEX_PATHS, MANAGER]) {
    const target = path.join(root, document);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, `Required policy for ${document}\nSecond line\nThird line`);
  }
  fs.writeFileSync(path.join(root, "_AGENT_CONTEXT/CURRENT_REVISION.json"), JSON.stringify({ canonicalRepository: "fengie/mhw-mods", canonicalBranch: "main", currentVersion: "8.8.41", status: "candidate", verificationAppliesToCommit: "b".repeat(40) }));
  return root;
}
const build = (root, options = {}) => buildRepositoryBootstrap({ root, now: NOW, git, ...options });

test("bootstrap separates exact source, historical verification and remote freshness", async t => {
  const root = fixture(t), packet = await build(root);
  assert.equal(packet.repository.head, HEAD);
  assert.equal(packet.current.verificationSource, "b".repeat(40));
  assert.equal(packet.repository.remoteFreshness, "local-ref-only");
  assert.equal(verifyRepositoryBootstrap(packet, { root, head: HEAD, now: NOW }), true);
  assert.equal((await build(root, { refreshedAt: new Date(NOW).toISOString() })).repository.remoteFreshness, "refreshed");
  await assert.rejects(build(root, { git: async () => "unknown" }), /exact Git/);
  await assert.rejects(build(root, { refreshedAt: "tomorrow" }), /refresh evidence/);
});

test("launch verification rejects expiration, changed head, missing core and changed documents", async t => {
  const root = fixture(t), packet = await build(root);
  assert.throws(() => verifyRepositoryBootstrap(packet, { root, head: HEAD, now: NOW + BOOTSTRAP_TTL_MS }), /expired/);
  assert.throws(() => verifyRepositoryBootstrap(packet, { root, head: "c".repeat(40), now: NOW }), /source/);
  const tampered = structuredClone(packet);
  tampered.manifests.core.pop();
  assert.throws(() => verifyRepositoryBootstrap(tampered, { root, head: HEAD, now: NOW }), /core\/context/);
  fs.appendFileSync(path.join(root, REPOSITORY_CONTEXT_INDEX_PATHS[0]), "\nNew policy");
  assert.throws(() => verifyRepositoryBootstrap(packet, { root, head: HEAD, now: NOW }), /source changed/);
});

test("manager core is enforced and unexpected roles are rejected", async t => {
  const root = fixture(t), packet = await build(root, { role: "manager" });
  assert.ok(packet.manifests.core.some(row => row.path === MANAGER));
  packet.manifests.core.pop();
  assert.throws(() => verifyRepositoryBootstrap(packet, { root, head: HEAD, now: NOW }), /core\/context/);
  await assert.rejects(build(root, { role: "imaginary" }), /role/);
  fs.unlinkSync(path.join(root, MANAGER));
  await assert.rejects(build(root, { role: "manager" }), /ENOENT/);
});

test("large ownership sets remain bounded, marked incomplete and omit task text", async t => {
  const root = fixture(t);
  const leases = Array.from({ length: 80 }, (_, id) => ({ id: String(id), boundary: "😀".repeat(200), agentId: "a".repeat(300), taskId: "t".repeat(300), branchName: "b".repeat(300), status: "active", objective: "SECRET OMITTED" }));
  leases.push({ status: "released" });
  const packet = await build(root, { leases });
  assert.equal(packet.ownership.count, 80);
  assert.equal(packet.ownership.truncated, true);
  assert.equal(packet.ownership.leases.length, 16);
  assert.ok(Buffer.byteLength(JSON.stringify(packet)) <= MAX_BOOTSTRAP_BYTES);
  assert.ok(Buffer.byteLength(packet.ownership.leases[0].boundary) <= 160);
  assert.ok(!JSON.stringify(packet).includes("SECRET OMITTED"));
});

test("context pagination is hash-checked, UTF-8 bounded and recoverable", async t => {
  const root = fixture(t), document = REPOSITORY_CONTEXT_INDEX_PATHS[0];
  fs.writeFileSync(path.join(root, document), "éé\nthird\nfinal");
  const expectedSha256 = repositoryManifest(root).context[0].sha256;
  const first = readRepositoryContext({ root, document, expectedSha256, maxBytes: 6 });
  assert.equal(first.text, "éé");
  assert.equal(first.nextLine, 2);
  const rest = readRepositoryContext({ root, document, expectedSha256, startLine: first.nextLine });
  assert.equal(rest.text, "third\nfinal");
  assert.equal(rest.nextLine, null);
  assert.throws(() => readRepositoryContext({ root, document, expectedSha256: "0".repeat(64) }), /hash changed/);
  assert.throws(() => readRepositoryContext({ root, document, expectedSha256, startLine: 9 }), /beyond/);
  assert.throws(() => readRepositoryContext({ root, document, expectedSha256, maxLines: 501 }), /bounds/);
  assert.throws(() => readRepositoryContext({ root, document, expectedSha256, maxBytes: 2 }), /exceeds/);
});

test("context refuses unindexed paths, empty sources and linked ancestors", async t => {
  const root = fixture(t), document = REPOSITORY_CONTEXT_INDEX_PATHS[0];
  assert.throws(() => readRepositoryContext({ root, document: "../outside", expectedSha256: "a".repeat(64) }), /not an indexed/);
  fs.writeFileSync(path.join(root, "AGENTS.md"), "");
  assert.throws(() => repositoryManifest(root), /non-empty/);
  fs.writeFileSync(path.join(root, "AGENTS.md"), "policy");
  const original = path.join(root, "_AGENT_TRAINING"), moved = path.join(root, "linked-training");
  fs.renameSync(original, moved);
  fs.symlinkSync(moved, original, process.platform === "win32" ? "junction" : "dir");
  assert.throws(() => repositoryManifest(root), /linked source/);
});

test("core growth has a deterministic budget instead of silently increasing premium startup", t => {
  const root = fixture(t);
  fs.writeFileSync(path.join(root, "AGENTS.md"), "x".repeat(MAX_CORE_BYTES));
  assert.throws(() => repositoryManifest(root), /byte budget/);
});

test("canonical CLI and generated worker prompts retain bounded retrieval and core policy", async () => {
  const { stdout } = await execFileHidden(process.execPath, [path.join(HERE, "../repository-context.mjs"), "--role", "manager"]);
  const packet = JSON.parse(stdout);
  assert.ok(packet.manifests.core.reduce((sum, row) => sum + row.bytes, 0) <= MAX_CORE_BYTES);
  const row = packet.manifests.context.find(row => row.path === "_AGENT_TRAINING/REPOSITORY_POLICY_REFERENCE.md");
  const page = JSON.parse((await execFileHidden(process.execPath, [path.join(HERE, "../repository-context.mjs"), "--document", row.path, "--sha256", row.sha256, "--lines", "4"])).stdout);
  assert.equal(page.endLine, 4);
  const prompt = renderAgentPrompt({ role: "manager", task: "Verify startup", machine: "heaven2", assignment: {}, repositoryBootstrap: packet }).rendered;
  assert.ok(prompt.indexOf("LIVE REPOSITORY BOOTSTRAP") < prompt.lastIndexOf("USER / MANAGER TASK"));
  assert.match(prompt, /Local-ref-only evidence does not prove fresh remote main/);
  await assert.rejects(execFileHidden(process.execPath, [path.join(HERE, "../repository-context.mjs"), "--unsupported", "1"]), /Use --document/);
});

test("read-only HTTP bootstrap works on real Git, rejects untrusted Host and observes edits", async t => {
  const root = fixture(t);
  for (const args of [["init", "-b", "main"], ["add", "."], ["-c", "user.name=Bootstrap test", "-c", "user.email=bootstrap@example.invalid", "commit", "-m", "fixture"], ["update-ref", "refs/remotes/origin/main", "HEAD"]]) await execFileHidden("git", ["-C", root, ...args]);
  const reservation = net.createServer();
  await new Promise(resolve => reservation.listen(0, "127.0.0.1", resolve));
  const port = reservation.address().port;
  await new Promise(resolve => reservation.close(resolve));
  const child = spawn(process.execPath, [path.join(HERE, "../server.mjs")], { windowsHide: true, stdio: ["ignore", "pipe", "pipe"], env: { ...process.env, AGENT_CONTROL_ALLOW_NON_CONTROLLER_HOST: "1", AGENT_CONTROL_REPO: root, AGENT_CONTROL_DATA_DIR: path.join(root, "runtime-data"), AGENT_WORKTREE_ROOT: path.join(root, "worktrees"), AGENT_CONTROL_PORT: String(port) } });
  t.after(async () => { child.kill(); await new Promise(resolve => { if (child.exitCode !== null) return resolve(); child.once("exit", resolve); }); });
  let logs = "";
  child.stdout.on("data", value => logs += value);
  child.stderr.on("data", value => logs += value);
  const url = `http://127.0.0.1:${port}/api/bootstrap`;
  let response;
  for (let attempt = 0; attempt < 60; attempt++) {
    try { response = await fetch(url); break; } catch { await new Promise(resolve => setTimeout(resolve, 50)); }
  }
  assert.ok(response, logs);
  assert.equal(response.status, 200, logs);
  const first = await response.json();
  assert.equal(first.repository.remoteFreshness, "local-ref-only");
  assert.equal(verifyRepositoryBootstrap(first, { root, head: first.repository.head }), true);
  const concurrent = await Promise.all(Array.from({ length: 5 }, async () => (await fetch(url)).json()));
  assert.equal(new Set(concurrent.map(packet => packet.generatedAt)).size, 1, "simultaneous readers share in-flight work, without retaining a completed cache");
  const forbidden = await new Promise((resolve, reject) => {
    http.get(url, { headers: { Host: "evil.example" } }, response => { response.resume(); resolve(response.statusCode); }).on("error", reject);
  });
  assert.equal(forbidden, 400);
  fs.appendFileSync(path.join(root, "AGENTS.md"), "\nPolicy update");
  const second = await (await fetch(url)).json();
  assert.notEqual(second.manifests.core[0].sha256, first.manifests.core[0].sha256);
  assert.throws(() => verifyRepositoryBootstrap(first, { root, head: first.repository.head }), /source changed/);
  assert.equal(second.repository.dirty, true);
});
