import test from "node:test";
import assert from "node:assert/strict";

import { renderAgentPrompt } from "../lib/prompt-templates.mjs";
import {
  applyPreLaunchFailure,
  autonomyPermissionDecision,
  defaultControlState,
  deploymentBatchCapacity,
  migrateControlState,
  planWorkflow,
  interpretCommand,
  supportLanesFor,
  canUseMachineForRepositoryWrite,
  classifyAuthoritativeExit,
  isIntegrationEligible,
  recommendNextActions,
  takeoverContext,
  workflowLeasePreflight,
  workflowPermission
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
  assert.equal(migrated.version, 4);
  assert.equal(migrated.autopilot.phase, "waiting-for-direction");
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

test("authoritative routing manifest fills declared missing slots instead of inventing a second swarm", () => {
  const current = state();
  current.settings.routingManifest = {
    source: "github:issue-60",
    mode: "authoritative",
    observedAt: "2026-09-28T16:00:00.000Z",
    expiresAt: "2026-09-28T17:00:00.000Z",
    assignments: [
      { slotId: "manager", role: "manager", task: "Coordinate", status: "claimed", owner: "manager-session" },
      { slotId: "main", role: "main", task: "Agent Control v2", status: "claimed", branch: "feature/agent-control-plane-v2-20260928" },
      { slotId: "support-1", role: "support", lane: "safety", task: "Safety re-review", status: "claimed", branch: "agent/support-safety" },
      { slotId: "support-2", role: "support", lane: "workflow", task: "Product workflow audit", status: "claimed", branch: "agent/support-workflow" },
      { slotId: "support-3", role: "support", lane: "frontend", task: "Frontend finalization", status: "claimed", branch: "ui/frontend" },
      { slotId: "support-4", role: "support", lane: "updater", task: "Updater publication closure", status: "open", boundary: "support:updater", machine: "heaven" }
    ]
  };

  const plan = planWorkflow("usual-swarm", {
    state: current,
    mission: "Continue the live repository swarm",
    machine: "heaven2",
    requireReconciledOwnership: true,
    now: Date.parse("2026-09-28T16:30:00.000Z")
  });

  assert.equal(plan.blocked.length, 0);
  assert.equal(plan.steps.length, 1);
  assert.equal(plan.steps[0].lane, "updater");
  assert.equal(plan.steps[0].task, "Updater publication closure");
  assert.equal(plan.steps[0].machine, "heaven");
  assert.equal(plan.ownership.reconciled, true);
  assert.equal(plan.ownership.source, "github:issue-60");
});

test("routing overlay counts external role and support-lane ownership", () => {
  const current = state();
  current.settings.routingManifest = {
    source: "github",
    mode: "overlay",
    observedAt: "2026-09-28T16:00:00.000Z",
    expiresAt: "2026-09-28T17:00:00.000Z",
    assignments: [
      { role: "main", status: "claimed", branch: "feature/external-main" },
      { role: "support", lane: "architecture", status: "claimed", branch: "support/external-architecture" }
    ]
  };
  const plan = planWorkflow("usual-swarm", {
    state: current,
    mission: "Fix updater rollback",
    machine: "heaven2",
    now: Date.parse("2026-09-28T16:30:00.000Z")
  });
  assert.equal(plan.steps.filter(item => item.role === "main").length, 0);
  assert.ok(!plan.steps.some(item => item.lane === "architecture"));
});

test("stale external ownership does not occupy a lane forever", () => {
  const current = state();
  current.settings.routingManifest = {
    source: "github",
    mode: "overlay",
    observedAt: "2026-09-28T16:00:00.000Z",
    expiresAt: "2026-09-28T17:00:00.000Z",
    assignments: [
      { role: "support", lane: "architecture", status: "superseded", branch: "support/old" }
    ]
  };
  const plan = planWorkflow("usual-swarm", {
    state: current,
    mission: "Fix updater rollback",
    machine: "heaven2",
    now: Date.parse("2026-09-28T16:30:00.000Z")
  });
  assert.ok(plan.steps.some(item => item.lane === "architecture"));
});

test("automatic broad swarm dispatch fails closed without fresh ownership context", () => {
  const plan = planWorkflow("usual-swarm", {
    state: state(),
    mission: "Continue work",
    machine: "heaven2",
    requireReconciledOwnership: true,
    now: Date.parse("2026-09-28T16:30:00.000Z")
  });
  assert.equal(plan.steps.length, 0);
  assert.match(plan.blocked[0], /requires a current routing manifest/i);
  assert.equal(plan.ownership.reconciled, false);
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

test("autonomy profiles enforce declared permissions and workflow requirements", () => {
  const current = state();

  current.settings.autonomyLevel = "observe";
  assert.equal(autonomyPermissionDecision(current, "preview").allowed, false);
  assert.equal(autonomyPermissionDecision(current, "dispatch-support").allowed, false);

  current.settings.autonomyLevel = "assist";
  assert.equal(autonomyPermissionDecision(current, "preview").allowed, true);
  assert.equal(autonomyPermissionDecision(current, "recommend").allowed, true);
  assert.equal(autonomyPermissionDecision(current, "dispatch-support").allowed, false);

  current.settings.autonomyLevel = "coordinate";
  assert.equal(autonomyPermissionDecision(current, "dispatch-support").allowed, true);
  assert.equal(autonomyPermissionDecision(current, "request-review").allowed, true);
  assert.equal(autonomyPermissionDecision(current, "run-tests").allowed, true);
  assert.equal(autonomyPermissionDecision(current, "prepare-integration").allowed, false);
  assert.equal(autonomyPermissionDecision(current, "maintain-continuity").allowed, false);

  current.settings.autonomyLevel = "engineering-autopilot";
  assert.equal(autonomyPermissionDecision(current, "prepare-integration").allowed, true);
  assert.equal(autonomyPermissionDecision(current, "maintain-continuity").allowed, true);

  current.settings.autonomyLevel = "unexpected-level";
  const unknown = autonomyPermissionDecision(current, "preview");
  assert.equal(unknown.allowed, false);
  assert.match(unknown.reason, /fails closed/i);

  assert.equal(workflowPermission("usual-swarm"), "dispatch-support");
  assert.equal(workflowPermission("review"), "request-review");
  assert.equal(workflowPermission("verification"), "run-tests");
  assert.equal(workflowPermission("integration"), "prepare-integration");
  assert.equal(workflowPermission("continuity"), "maintain-continuity");
  assert.equal(workflowPermission("self-improve"), "prepare-integration");
  assert.equal(workflowPermission("unknown-workflow"), null);
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

test("operator stop intent dominates an authoritative zero exit", () => {
  const stopping = { status: "stopping" };
  const blockedAfterStop = { status: "blocked", stopRequestedAt: "2026-09-28T16:00:00.000Z" };
  assert.equal(classifyAuthoritativeExit(stopping, 0), "stopped");
  assert.equal(classifyAuthoritativeExit(blockedAfterStop, 0), "stopped");
  assert.equal(classifyAuthoritativeExit({ status: "running" }, 0), "done");
  assert.equal(classifyAuthoritativeExit({ status: "running" }, 1), "failed");
  assert.equal(isIntegrationEligible({
    status: classifyAuthoritativeExit(stopping, 0),
    exitCode: 0,
    completionEvidence: "verified-operator-stop"
  }), false);
});

test("counted deployment is rejected before partial launch when capacity is insufficient", () => {
  const current = state();
  for (let i = 0; i < 7; i += 1) {
    current.agents.push({ id: `active-${i}`, role: "support", status: "running" });
  }
  const capacity = deploymentBatchCapacity(current, 2, 8);
  assert.deepEqual(capacity, {
    count: 2,
    active: 7,
    maximum: 8,
    available: 1,
    allowed: false
  });
});

test("pre-launch failure releases reservation and preserves retained worktree evidence", () => {
  const current = state();
  current.tasks.push({
    id: "task-prelaunch",
    status: "starting",
    branchName: "agent/control-test"
  });
  current.leases.push({
    id: "lease-prelaunch",
    taskId: "task-prelaunch",
    status: "active"
  });

  const result = applyPreLaunchFailure(current, {
    taskId: "task-prelaunch",
    leaseId: "lease-prelaunch",
    error: "codex missing",
    reason: "pre-launch-setup-failed",
    at: "2026-09-28T16:30:00.000Z",
    retainedWorktree: "C:\\agent-worktrees\\support-test",
    retainedBranch: "agent/control-test"
  });

  assert.equal(result.task.status, "failed");
  assert.equal(result.task.error, "codex missing");
  assert.equal(result.task.retainedWorktree, "C:\\agent-worktrees\\support-test");
  assert.match(result.task.nextAction, /retained pre-launch worktree/i);
  assert.equal(result.lease.status, "released");
  assert.equal(result.lease.releaseReason, "pre-launch-setup-failed");
  assert.equal(result.lease.releasedAt, "2026-09-28T16:30:00.000Z");
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

test("workflow lease preflight rejects active and duplicate mutable boundaries before launch", () => {
  const current = state();
  current.leases.push({ id: "lease-1", boundary: "shared-api", status: "active" });

  const activeConflict = workflowLeasePreflight(current, [
    { role: "support", boundary: "shared-api" },
    { role: "test", boundary: "verification" }
  ]);
  assert.equal(activeConflict.allowed, false);
  assert.equal(activeConflict.boundary, "shared-api");

  const duplicatePlan = workflowLeasePreflight(state(), [
    { role: "support", boundary: "same-boundary" },
    { role: "test", boundary: "same-boundary" }
  ]);
  assert.equal(duplicatePlan.allowed, false);
  assert.equal(duplicatePlan.boundary, "same-boundary");

  assert.deepEqual(workflowLeasePreflight(state(), [
    { role: "support", boundary: "one" },
    { role: "test", boundary: "two" }
  ]), { allowed: true, boundary: null, reason: null });
});
