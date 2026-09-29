import test from "node:test";
import assert from "node:assert/strict";

import {
  decideAutopilotAction,
  defaultAutopilotState,
  normalizeAutopilotState,
  transitionAutopilot
} from "../lib/autopilot-core.mjs";

function state(autopilot = {}) {
  return {
    health: { mode: "healthy" },
    settings: {
      autonomyLevel: "engineering-autopilot",
      readOnly: false,
      emergencyStop: false,
      dispatchPaused: false,
      draining: false
    },
    autopilot: normalizeAutopilotState({
      ...defaultAutopilotState(),
      enabled: true,
      objective: "Build the next Agent Manager capability",
      phase: "sync-plan",
      ...autopilot
    }),
    agents: [],
    tasks: []
  };
}function doneAgent(id, taskId = `task-${id}`) {
  return {
    id,
    taskId,
    status: "done",
    exitCode: 0,
    completionEvidence: "authoritative-exit"
  };
}

test("restart normalization preserves durable autopilot progress", () => {
  const restored = normalizeAutopilotState({
    enabled: true,
    objective: "Continue the big direction",
    phase: "review",
    iteration: 7,
    repairLoops: 2,
    candidateAgentId: "main-1",
    runId: "run-1"
  });
  assert.equal(restored.enabled, true);
  assert.equal(restored.phase, "review");
  assert.equal(restored.iteration, 7);
  assert.equal(restored.repairLoops, 2);
  assert.equal(restored.candidateAgentId, "main-1");
  assert.equal(restored.runId, "run-1");
});test("sync-plan fails closed when ownership freshness is missing", () => {
  const decision = decideAutopilotAction(state(), { routingCurrent: false, capacityAvailable: true });
  assert.deepEqual(decision, { kind: "gate", reason: "routing-ownership-stale" });
});

test("implementation waits for its active worker even when capacity is full", () => {
  const current = state({ phase: "implement", implementationAgentId: "main-1" });
  current.agents.push({ id: "main-1", status: "running" });
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: false });
  assert.deepEqual(decision, { kind: "wait", reason: "implementation-active" });
});

test("implementation completion advances from authoritative exit evidence only", () => {
  const current = state({ phase: "implement", implementationAgentId: "main-1" });
  current.agents.push(doneAgent("main-1"));
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.equal(decision.kind, "transition");
  assert.equal(decision.phase, "verify");
  assert.equal(decision.patch.candidateAgentId, "main-1");
});test("verification pass requires structured task evidence", () => {
  const current = state({
    phase: "verify",
    candidateAgentId: "main-1",
    verificationAgentId: "test-1"
  });
  current.agents.push(doneAgent("test-1", "task-test-1"));
  current.tasks.push({
    id: "task-test-1",
    evidence: [{ type: "verification", sourceSha: "a".repeat(40), result: "pass" }]
  });
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.deepEqual(decision, { kind: "transition", phase: "review", reason: "verification-passed" });
});

test("verification prose without structured evidence stops at a safety gate", () => {
  const current = state({
    phase: "verify",
    candidateAgentId: "main-1",
    verificationAgentId: "test-1"
  });
  current.agents.push(doneAgent("test-1", "task-test-1"));
  current.tasks.push({ id: "task-test-1", evidence: [] });
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.deepEqual(decision, { kind: "gate", reason: "verification-evidence-missing" });
});test("changes requested routes into bounded repair", () => {
  const current = state({ phase: "review", candidateAgentId: "main-1", repairLoops: 0, maxRepairLoops: 2 });
  current.agents.push({ ...doneAgent("main-1"), reviewVerdict: "changes-requested" });
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.deepEqual(decision, { kind: "transition", phase: "repair", reason: "review-changes-requested" });
});

test("authoritative repair advances to reverify and increments loop budget", () => {
  const current = state({
    phase: "repair",
    candidateAgentId: "main-1",
    repairAgentId: "repair-1",
    repairLoops: 0,
    maxRepairLoops: 2
  });
  current.agents.push(doneAgent("repair-1"));
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.equal(decision.kind, "transition");
  assert.equal(decision.phase, "reverify");
  assert.equal(decision.patch.candidateAgentId, "repair-1");
  assert.equal(decision.patch.repairLoops, 1);
  assert.equal(decision.patch.verificationAgentId, null);
});test("repair budget exhaustion stops instead of looping forever", () => {
  const current = state({ phase: "review", candidateAgentId: "main-1", repairLoops: 2, maxRepairLoops: 2 });
  current.agents.push({ ...doneAgent("main-1"), reviewVerdict: "rejected" });
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.deepEqual(decision, { kind: "gate", reason: "repair-budget-exhausted:review-rejected" });
});

test("continuity never crosses the final integration gate", () => {
  const decision = decideAutopilotAction(state({ phase: "continuity" }), {
    routingCurrent: true,
    capacityAvailable: true
  });
  assert.deepEqual(decision, { kind: "integration-gate", reason: "operator-integration-approval-required" });
});

test("transition bookkeeping is durable and monotonic", () => {
  const initial = normalizeAutopilotState({ enabled: true, objective: "x", phase: "sync-plan", iteration: 3 });
  const next = transitionAutopilot(initial, "implement", { reason: "reconciled", at: "2026-09-28T16:00:00.000Z" });
  assert.equal(next.phase, "implement");
  assert.equal(next.iteration, 4);
  assert.equal(next.lastTransitionAt, "2026-09-28T16:00:00.000Z");
});

test("restart orphan state requires reconciliation instead of duplicate repair", () => {
  const current = state({ phase: "implement", implementationAgentId: "main-1" });
  current.agents.push({
    id: "main-1",
    status: "orphaned",
    completionEvidence: null
  });
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.deepEqual(decision, {
    kind: "gate",
    reason: "implementation-reconciliation-required-orphaned"
  });
});

test("capacity failure is deterministic before a missing implementation worker dispatch", () => {
  const current = state({ phase: "implement", implementationAgentId: null });
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: false });
  assert.deepEqual(decision, { kind: "gate", reason: "worker-capacity-unavailable" });
});

test("explicit zero repair budget remains zero", () => {
  const normalized = normalizeAutopilotState({ maxRepairLoops: 0 });
  assert.equal(normalized.maxRepairLoops, 0);
});

test("failed repair consumes budget and clears the dead repair worker for a fresh attempt", () => {
  const current = state({
    phase: "repair",
    candidateAgentId: "main-1",
    repairAgentId: "repair-1",
    repairLoops: 0,
    maxRepairLoops: 2
  });
  current.agents.push({
    id: "repair-1",
    status: "failed",
    exitCode: 1,
    completionEvidence: "authoritative-exit"
  });

  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.equal(decision.kind, "transition");
  assert.equal(decision.phase, "repair");
  assert.equal(decision.patch.repairAgentId, null);
  assert.equal(decision.patch.repairLoops, 1);
});

test("failed repair stops when the repair budget is exhausted", () => {
  const current = state({
    phase: "repair",
    candidateAgentId: "main-1",
    repairAgentId: "repair-1",
    repairLoops: 1,
    maxRepairLoops: 1
  });
  current.agents.push({
    id: "repair-1",
    status: "failed",
    exitCode: 1,
    completionEvidence: "authoritative-exit"
  });

  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.equal(decision.kind, "gate");
  assert.match(decision.reason, /^repair-budget-exhausted:repair-failed$/);
});
