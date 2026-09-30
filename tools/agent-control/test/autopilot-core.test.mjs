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
});test("restart normalization preserves perpetual resilience metadata", () => {
  const restored = normalizeAutopilotState({
    enabled: true,
    perpetual: true,
    objective: "Never stop",
    pendingReplacement: {
      sourceAgentId: "main-stale",
      agentField: "implementationAgentId",
      phase: "implement",
      attempt: 2
    },
    replacementCount: 7,
    recoveryHistory: ["2026-09-29T19:00:00.000Z", "not-a-date"],
    recoveryCooldownLevel: 4,
    nextRetryAt: "2026-09-29T19:05:00.000Z",
    lastRecoveryAt: "2026-09-29T19:01:00.000Z",
    lastRecoveryReason: "replacement-dispatch-failed"
  });
  assert.equal(restored.pendingReplacement.sourceAgentId, "main-stale");
  assert.equal(restored.replacementCount, 7);
  assert.deepEqual(restored.recoveryHistory, ["2026-09-29T19:00:00.000Z"]);
  assert.equal(restored.recoveryCooldownLevel, 4);
  assert.equal(restored.nextRetryAt, "2026-09-29T19:05:00.000Z");
  assert.equal(restored.lastRecoveryReason, "replacement-dispatch-failed");
});

test("sync-plan fails closed when ownership freshness is missing", () => {
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


test("perpetual mode ignores the ordinary transition budget", () => {
  const current = state({
    perpetual: true,
    phase: "sync-plan",
    iteration: 40,
    maxIterations: 40
  });
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.deepEqual(decision, { kind: "transition", phase: "implement", reason: "canonical-truth-reconciled" });
});

test("ordinary autopilot still stops at its transition budget", () => {
  const current = state({
    perpetual: false,
    phase: "sync-plan",
    iteration: 40,
    maxIterations: 40
  });
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.deepEqual(decision, { kind: "gate", reason: "iteration-budget-exhausted" });
});

test("approved perpetual candidate crosses into governed integration", () => {
  const current = state({
    perpetual: true,
    phase: "integration-ready",
    candidateAgentId: "main-1"
  });
  current.agents.push({ ...doneAgent("main-1"), reviewVerdict: "approved" });
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.equal(decision.kind, "transition");
  assert.equal(decision.phase, "integrate");
  assert.equal(decision.patch.phaseRetries, 0);
});

test("perpetual integration dispatches a worker when none exists", () => {
  const current = state({
    perpetual: true,
    phase: "integrate",
    candidateAgentId: "main-1",
    integrationAgentId: null
  });
  current.agents.push({ ...doneAgent("main-1"), reviewVerdict: "approved" });
  const decision = decideAutopilotAction(current, {
    routingCurrent: true,
    capacityAvailable: true,
    integrationVerified: false
  });
  assert.deepEqual(decision, { kind: "dispatch-integration" });
});

test("integration without canonical-main proof retries with a fresh worker", () => {
  const current = state({
    perpetual: true,
    phase: "integrate",
    candidateAgentId: "main-1",
    integrationAgentId: "integrator-1",
    phaseRetries: 0,
    maxPhaseRetries: 2
  });
  current.agents.push(
    { ...doneAgent("main-1"), reviewVerdict: "approved" },
    doneAgent("integrator-1")
  );
  const decision = decideAutopilotAction(current, {
    routingCurrent: true,
    capacityAvailable: true,
    integrationVerified: false
  });
  assert.equal(decision.kind, "transition");
  assert.equal(decision.phase, "integrate");
  assert.equal(decision.patch.integrationAgentId, null);
  assert.equal(decision.patch.phaseRetries, 1);
});

test("proven integration advances into repository hygiene", () => {
  const current = state({
    perpetual: true,
    phase: "integrate",
    candidateAgentId: "main-1",
    integrationAgentId: "integrator-1"
  });
  current.agents.push(
    { ...doneAgent("main-1"), reviewVerdict: "approved" },
    doneAgent("integrator-1")
  );
  const decision = decideAutopilotAction(current, {
    routingCurrent: true,
    capacityAvailable: true,
    integrationVerified: true
  });
  assert.deepEqual(decision, {
    kind: "transition",
    phase: "hygiene",
    reason: "canonical-main-integration-proven",
    patch: { phaseRetries: 0 }
  });
});

test("completed hygiene advances into expansion", () => {
  const current = state({
    perpetual: true,
    phase: "hygiene",
    hygieneAgentId: "cleanup-1"
  });
  current.agents.push(doneAgent("cleanup-1"));
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.deepEqual(decision, {
    kind: "transition",
    phase: "expand",
    reason: "repository-hygiene-complete",
    patch: { phaseRetries: 0 }
  });
});

test("completed expansion advances into a cycle checkpoint", () => {
  const current = state({
    perpetual: true,
    phase: "expand",
    expansionAgentId: "research-1"
  });
  current.agents.push(doneAgent("research-1"));
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.deepEqual(decision, {
    kind: "transition",
    phase: "cycle-checkpoint",
    reason: "next-cycle-plan-ready",
    patch: { phaseRetries: 0 }
  });
});

test("cycle checkpoint resets per-cycle ownership and repeats", () => {
  const current = state({
    perpetual: true,
    phase: "cycle-checkpoint",
    cycleNumber: 4,
    repairLoops: 2,
    phaseRetries: 1,
    implementationAgentId: "main-1",
    candidateAgentId: "main-1",
    verificationAgentId: "test-1",
    reviewAgentId: "review-1",
    repairAgentId: "repair-1",
    integrationAgentId: "integration-1",
    hygieneAgentId: "cleanup-1",
    expansionAgentId: "research-1"
  });
  const decision = decideAutopilotAction(current, { routingCurrent: true, capacityAvailable: true });
  assert.equal(decision.kind, "transition");
  assert.equal(decision.phase, "sync-plan");
  assert.equal(decision.patch.cycleNumber, 5);
  assert.equal(decision.patch.repairLoops, 0);
  assert.equal(decision.patch.phaseRetries, 0);
  for (const key of [
    "implementationAgentId",
    "candidateAgentId",
    "verificationAgentId",
    "reviewAgentId",
    "repairAgentId",
    "integrationAgentId",
    "hygieneAgentId",
    "expansionAgentId"
  ]) assert.equal(decision.patch[key], null);
});

test("perpetual phase retry budget stops repeated integration failure", () => {
  const current = state({
    perpetual: true,
    phase: "integrate",
    candidateAgentId: "main-1",
    integrationAgentId: "integrator-1",
    phaseRetries: 2,
    maxPhaseRetries: 2
  });
  current.agents.push(
    { ...doneAgent("main-1"), reviewVerdict: "approved" },
    doneAgent("integrator-1")
  );
  const decision = decideAutopilotAction(current, {
    routingCurrent: true,
    capacityAvailable: true,
    integrationVerified: false
  });
  assert.deepEqual(decision, {
    kind: "gate",
    reason: "phase-retry-budget-exhausted:integrate:candidate-not-on-canonical-main"
  });
});
