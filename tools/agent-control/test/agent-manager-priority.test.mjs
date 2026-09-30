import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, "../../..");
const SERVER = path.join(ROOT, "tools", "agent-control", "server.mjs");

test("canonical continuity keeps Agent Manager P0 active until runtime proof closes it", () => {
  const current = JSON.parse(fs.readFileSync(path.join(ROOT, "_AGENT_CONTEXT", "CURRENT_REVISION.json"), "utf8"));
  const repoVersion = fs.readFileSync(path.join(ROOT, "VERSION.txt"), "utf8").trim();
  assert.equal(current.currentVersion, repoVersion, "P0 continuity must track the live repository version");
  assert.equal(current.agentManagerPriority?.status, "active");
  assert.equal(current.agentManagerPriority?.priority, "P0");
  assert.match(current.agentManagerPriority?.goal || "", /Agent Manager|Agent Control/i);
  assert.ok((current.agentManagerPriority?.completionRequires || []).length >= 6);
  assert.match(current.agentManagerPriority?.releasePolicy || "", /unrelated product features/i);
});

test("Agent Control injects the live P0 directive into implementation and expansion", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  assert.match(source, /function activeAgentManagerPriority\(\)/);
  assert.match(source, /function agentManagerPriorityDirective\(\)/);
  assert.match(source, /P0 AGENT MANAGER FUNCTIONALITY LOCK IS ACTIVE/);
  assert.match(source, /const managerPriority = agentManagerPriorityDirective\(\);/);
  assert.match(source, /lane: managerPriority \? "agent-manager-p0-next-cycle" : "perpetual-next-cycle"/);
  assert.match(source, /priority: managerPriority \? 100 : 72/);
  assert.match(source, /Unrelated product features are out of scope while the P0 lock is active/);
});

test("Agent Control runtime, documentation, and private plugin use one release identity", () => {
  const runtime = JSON.parse(fs.readFileSync(path.join(ROOT, "tools", "agent-control", "package.json"), "utf8"));
  const plugin = JSON.parse(fs.readFileSync(path.join(ROOT, "tools", "agent-control", "chatgpt-plugin", "plugin.json"), "utf8"));
  const readme = fs.readFileSync(path.join(ROOT, "tools", "agent-control", "README.md"), "utf8");
  assert.equal(plugin.version, runtime.version);
  assert.ok(readme.includes(`What v${runtime.version} does`), "Agent Control README must describe the live runtime version");
});

test("ChatGPT control skill heartbeats real stable sessions without inventing identities", () => {
  const skill = fs.readFileSync(path.join(ROOT, "tools", "agent-control", "chatgpt-plugin", "skills", "agent-control", "SKILL.md"), "utf8");
  assert.match(skill, /Registration is the default first step for this skill/);
  assert.match(skill, /register or heartbeat that identity before reading or changing swarm state/);
  assert.match(skill, /never synthesize one from a title/);
});
