import test from "node:test";
import assert from "node:assert/strict";

import { renderAgentPrompt } from "../lib/prompt-templates.mjs";
import {
  defaultControlState,
  migrateControlState,
  planWorkflow,
  interpretCommand,
  supportLanesFor,
  canUseMachineForRepositoryWrite,
  isIntegrationEligible,
  recommendNextActions,
  takeoverContext
} from "../lib/control-core.mjs";

function state() {
  return defaultControlState({ sessionId: "session-test", hostname: "heaven2" });
}

test("prompt rendering preserves provenance and machine policy", () => {
  const result = renderAgentPrompt({
    role: "support",
    task: "Audit updater rollback",
    assignment: {
      taskId: "task-1",
      priority: 70,
      boundary: "support:rollback",
      baseBranch: "main",
      baseSha: "abc123",
      branchName: "agent/test"
    },
    machine: "heaven",
    repositoryWriteAuthorized: false
  });
  assert.equal(result.templateId, "role.support");
  assert.match(result.templateVersion, /^2026\./);
  assert.equal(result.sha256.length, 64);
  assert.match(result.rendered, /do not clone, fetch, pull, push/i);
  assert.match(result.rendered, /recursive continuity/i);
});

test("v2 state migrates without dropping durable records", () => {
  const migrated = migrateControlState({
    version: 2,
    agents: [{ id: "a1", status: "done" }],
    tasks: [{ id: "t1" }],
    leases: [{ id: "l1" }],
    events: [{ type: "old" }]
  }, { sessionId: "new-session", hostname: "heaven2" });
  assert.equal(migrated.version, 3);
  assert.equal(migrated.agents.length, 1);
  assert.equal(migrated.tasks.length, 1);
  assert.equal(migrated.controller.sessionId, "new-session");
  assert.equal(migrated.settings.autonomyLevel, "assist");
});

test("usual swarm fills only missing roles and distinct support lanes", () => {
  const current = state();
  current.agents.push({
    id: "manager-1",
    role: "manager",
    status: "running",
    task: "Fix updater",
    lane: null
  });
  current.agents.push({
    id: "support-1",
    role: "support",
    status: "running",
    task: "Architecture audit",
    lane: "architecture"
  });
  const plan = planWorkflow("usual-swarm", {
    state: current,
    mission: "Fix updater rollback",
    machine: "heaven2"
  });
  assert.equal(plan.steps.filter(item => item.role === "manager").length, 0);
  assert.equal(plan.steps.filter(item => item.role === "main").length, 1);
  const support = plan.steps.filter(item => item.role === "support");
  assert.equal(support.length, 3);
  assert.equal(new Set(support.map(item => item.lane)).size, 3);
  assert.ok(!support.some(item => item.lane === "architecture"));
});

test("support current programmer blocks cleanly without a primary", () => {
  const plan = planWorkflow("support-current", {
    state: state(),
    mission: "Fix updater",
    machine: "heaven2"
  });
  assert.equal(plan.steps.length, 0);
  assert.match(plan.blocked[0], /No active Primary Programmer/i);
});

test("updater support lanes are intentionally different", () => {
  const lanes = supportLanesFor("harden updater rollback and packaging");
  assert.deepEqual(lanes.map(item => item.id), ["architecture", "tests", "adversarial", "windows"]);
});

test("natural language command bar maps common operator language", () => {
  assert.equal(interpretCommand("deploy the usual swarm").workflowId, "usual-swarm");
  assert.equal(interpretCommand("support whoever is working on updater").workflowId, "support-current");
  assert.equal(interpretCommand("test everything").workflowId, "verification");
  assert.equal(interpretCommand("make the agent manager better").workflowId, "self-improve");
  assert.equal(interpretCommand("do something unusual").needsClarification, true);
});

test("heaven repository writes fail closed without per-task authorization", () => {
  const current = state();
  const denied = canUseMachineForRepositoryWrite(current, "heaven", false);
  const authorized = canUseMachineForRepositoryWrite(current, "heaven", true);
  const heaven2 = canUseMachineForRepositoryWrite(current, "heaven2", false);
  assert.equal(denied.allowed, false);
  assert.equal(authorized.allowed, true);
  assert.equal(heaven2.allowed, true);
});

test("integration eligibility requires authoritative successful exit", () => {
  assert.equal(isIntegrationEligible({ status: "done", exitCode: 0, completionEvidence: "authoritative-exit" }), true);
  assert.equal(isIntegrationEligible({ status: "done", exitCode: 0, completionEvidence: "reconciled-from-output" }), false);
  assert.equal(isIntegrationEligible({ status: "interrupted", exitCode: null, completionEvidence: null }), false);
});

test("recommendations prioritize uncertain worker reconciliation", () => {
  const current = state();
  current.agents.push({ id: "a1", role: "support", status: "orphaned", task: "Audit rollback" });
  const actions = recommendNextActions({ state: current, integrationQueue: [] });
  assert.equal(actions[0].kind, "recovery");
  assert.match(actions[0].title, /need reconciliation/i);
});

test("takeover context is concise and carries exact branch state", () => {
  const agent = {
    id: "a1",
    role: "main",
    taskId: "t1",
    status: "interrupted",
    branchName: "agent/work",
    baseBranch: "main",
    baseSha: "abc",
    currentSha: "def",
    machine: "heaven2",
    lastMessage: "Implemented half the boundary."
  };
  const task = {
    id: "t1",
    objective: "Finish safe stop logic",
    dependencies: ["t0"],
    blockers: ["Windows process test"],
    nextAction: "Run the stop regression."
  };
  const handoff = takeoverContext(agent, task, [{ type: "test", result: "pending" }]);
  assert.equal(handoff.branch, "agent/work");
  assert.equal(handoff.baseSha, "abc");
  assert.equal(handoff.currentSha, "def");
  assert.deepEqual(handoff.unresolved, ["Windows process test"]);
  assert.match(handoff.constraints.join(" "), /Do not duplicate/i);
});
