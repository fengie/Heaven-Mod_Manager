import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, "../../..");
const SERVER = path.join(ROOT, "tools", "agent-control", "server.mjs");

test("canonical continuity actively enables Agent Manager P0 until runtime proof closes it", () => {
  const current = JSON.parse(fs.readFileSync(path.join(ROOT, "_AGENT_CONTEXT", "CURRENT_REVISION.json"), "utf8"));
  assert.equal(current.currentVersion, "8.8.24");
  assert.equal(current.agentManagerPriority?.status, "active");
  assert.equal(current.agentManagerPriority?.priority, "P0");
  assert.match(current.agentManagerPriority?.goal || "", /Agent Manager|Agent Control/i);
  assert.ok((current.agentManagerPriority?.completionRequires || []).length >= 7);
  assert.match(current.agentManagerPriority?.releasePolicy || "", /unrelated product features/i);
});

test("Agent Control consumes the active P0 marker in implementation and expansion", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  assert.match(source, /agentManagerPriority: parsed\.agentManagerPriority/);
  assert.match(source, /function activeAgentManagerPriority\(\)/);
  assert.match(source, /function agentManagerPriorityDirective\(\)/);
  assert.match(source, /P0 AGENT MANAGER FUNCTIONALITY LOCK IS ACTIVE/);
  assert.match(source, /priority: managerPriority \? 100 : 72/);
  assert.match(source, /lane: managerPriority \? "agent-manager-p0-next-cycle" : "perpetual-next-cycle"/);
  assert.match(source, /Unrelated product features are out of scope while the P0 lock is active/);
});

test("Agent Control runtime, plugin, and documentation share v0.6.2 identity", () => {
  const runtime = JSON.parse(fs.readFileSync(path.join(ROOT, "tools", "agent-control", "package.json"), "utf8"));
  const plugin = JSON.parse(fs.readFileSync(path.join(ROOT, "tools", "agent-control", "chatgpt-plugin", "plugin.json"), "utf8"));
  const readme = fs.readFileSync(path.join(ROOT, "tools", "agent-control", "README.md"), "utf8");
  assert.equal(runtime.version, "0.6.2");
  assert.equal(plugin.version, runtime.version);
  assert.match(readme, /What v0\.6\.2 does/);
});
