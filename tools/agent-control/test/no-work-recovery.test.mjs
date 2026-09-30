import test from "node:test";
import assert from "node:assert/strict";

import {
  hasSubstantiveWorkEvidence,
  hasVerifiedCompletionEvidence,
  looksLikeExecutionOpener,
  noWorkTerminationDecision,
  planSwarmTailRecoveryBatch,
  recoveryBackoffMs,
  recoveryBackoffWithJitterMs,
  recoveryMachineTarget,
  shouldRetireFromLiveRegistry,
  terminationReconciliationDecision
} from "../lib/no-work-recovery.mjs";

test("recognizes execution-assignment opener without treating ordinary final output as an opener", () => {
  assert.equal(looksLikeExecutionOpener("I’m treating this as an execution assignment: train on the repo first, inspect current agents/branches."), true);
  assert.equal(looksLikeExecutionOpener("I’m treating this as an execution assignment, not a review: I’ll use the bug-hunt brief as the operating plan."), true);
  assert.equal(looksLikeExecutionOpener("Implemented the fix, added regression coverage, and committed 1234567."), false);
});

test("successful-looking terminal agent with only the opener is classified as no-work", () => {
  const result = noWorkTerminationDecision({
    status: "done",
    baseSha: "aaaaaaaa",
    currentSha: "aaaaaaaa",
    lastMessage: "I’m treating this as an execution assignment: train on the repo first, inspect current agents/branches."
  });
  assert.equal(result.noWork, true);
  assert.equal(result.retry, true);
  assert.equal(result.reason, "terminated-after-execution-opener");
});

test("commit or changed-file evidence prevents no-work retry", () => {
  assert.equal(hasSubstantiveWorkEvidence({
    baseSha: "aaaaaaaa",
    currentSha: "bbbbbbbb"
  }), true);
  const result = noWorkTerminationDecision({
    status: "done",
    baseSha: "aaaaaaaa",
    currentSha: "bbbbbbbb",
    lastMessage: "I’m treating this as an execution assignment."
  });
  assert.equal(result.noWork, false);
  assert.equal(result.reason, "substantive-work-evidence");
});

test("read-only tool calls do not count as substantive execution work", () => {
  const result = noWorkTerminationDecision({
    state: "failed",
    last_action_summary: "I’m treating this as an execution assignment.",
    source_metadata: { execution_assignment: true, tool_call_count: 4 }
  });
  assert.equal(result.noWork, true);
});

test("write or verification evidence prevents a no-work retry", () => {
  const result = noWorkTerminationDecision({
    state: "failed",
    last_action_summary: "I’m treating this as an execution assignment.",
    source_metadata: { execution_assignment: true, write_count: 1 }
  });
  assert.equal(result.noWork, false);
});

test("recovery routes only to registered machines", () => {
  const policies = { heaven: {}, heaven2: {} };
  assert.equal(recoveryMachineTarget("cloud", policies), "auto");
  assert.equal(recoveryMachineTarget("heaven", policies), "heaven");
  assert.equal(recoveryMachineTarget("", policies), "auto");
});

test("dirty uncommitted work is preserved instead of auto-retried", () => {
  const result = noWorkTerminationDecision({
    status: "failed",
    baseSha: "aaaaaaaa",
    currentSha: "aaaaaaaa",
    worktreeDirty: true,
    lastMessage: "I’m treating this as an execution assignment."
  });
  assert.equal(result.noWork, false);
  assert.equal(result.reason, "substantive-work-evidence");
});

test("ordinary terminal summary is not retried merely because no commit exists", () => {
  const result = noWorkTerminationDecision({
    status: "done",
    baseSha: "aaaaaaaa",
    currentSha: "aaaaaaaa",
    lastMessage: "Reviewed the branch and found no code changes were required; verification passed."
  });
  assert.equal(result.noWork, false);
});

test("empty interrupted worker is retryable and backoff is bounded", () => {
  const result = noWorkTerminationDecision({ status: "interrupted", lastMessage: "" });
  assert.equal(result.noWork, true);
  assert.equal(result.reason, "terminated-without-output");
  assert.equal(recoveryBackoffMs(1, { baseMs: 1000, maxMs: 4000 }), 1000);
  assert.equal(recoveryBackoffMs(4, { baseMs: 1000, maxMs: 4000 }), 4000);
});



test("authoritative nonzero exit is a deterministic failure, not no-work", () => {
  const result = noWorkTerminationDecision({
    status: "failed",
    exitCode: 1,
    completionEvidence: "authoritative-exit",
    lastMessage: ""
  });
  assert.equal(result.noWork, false);
  assert.equal(result.retry, false);
  assert.equal(result.reason, "deterministic-runtime-failure");
});

test("stream loss metadata cannot turn an authoritative nonzero exit into an automatic retry", () => {
  const result = terminationReconciliationDecision({
    state: "failed",
    exitCode: 1,
    completionEvidence: "authoritative-exit",
    last_action_summary: "",
    source_metadata: { stream_lost: true }
  }, { streamLost: true, durableEvidenceChecked: true });
  assert.equal(result.recoveryStatus, "work-unverified");
  assert.equal(result.retry, false);
  assert.equal(result.action, "inspect");
  assert.equal(result.reason, "deterministic-runtime-failure");
});

test("terminal clean failures retire from the live registry while recoverable work stays visible", () => {
  assert.equal(shouldRetireFromLiveRegistry({
    status: "failed",
    exitCode: 1,
    completionEvidence: "authoritative-exit"
  }), true);

  assert.equal(shouldRetireFromLiveRegistry({
    status: "failed",
    exitCode: 1,
    completionEvidence: "authoritative-exit",
    worktreeDirty: true
  }), false);

  assert.equal(shouldRetireFromLiveRegistry({
    status: "failed",
    failureClass: "no-work",
    recoveryStatus: "retry-exhausted"
  }), true);

  assert.equal(shouldRetireFromLiveRegistry({
    status: "failed",
    failureClass: "no-work",
    recoveryStatus: "retry-pending"
  }), false);

  assert.equal(shouldRetireFromLiveRegistry({
    status: "done",
    exitCode: 0,
    completionEvidence: "authoritative-exit"
  }), false);
});

test("stream loss with durable work is preserved as incomplete", () => {
  const result = terminationReconciliationDecision({
    state: "disconnected",
    last_action_summary: "Connection dropped while I was integrating the changes.",
    source_metadata: { pr_number: 219, changed_files: ["tools/agent-control/server.mjs"] }
  }, { streamLost: true, durableEvidenceChecked: true });
  assert.equal(result.recoveryStatus, "work-detected-incomplete");
  assert.equal(result.retry, false);
  assert.equal(result.action, "reconcile-existing-work");
});

test("verified durable integration is complete even when the response stream is lost", () => {
  const agent = {
    state: "disconnected",
    source_metadata: {
      pr_number: 219,
      pr_merged: true,
      verification_passed: true
    }
  };
  assert.equal(hasVerifiedCompletionEvidence(agent), true);
  const result = terminationReconciliationDecision(agent, { streamLost: true, durableEvidenceChecked: true });
  assert.equal(result.recoveryStatus, "work-verified-complete");
  assert.equal(result.retry, false);
  assert.equal(result.action, "complete");
});

test("stream loss with no durable work is retryable even if partial prose was emitted", () => {
  const result = terminationReconciliationDecision({
    state: "interrupted",
    last_action_summary: "I inspected the task and was about to start the implementation.",
    source_metadata: { stream_lost: true }
  }, { streamLost: true, durableEvidenceChecked: true });
  assert.equal(result.recoveryStatus, "no-durable-work-detected-retry");
  assert.equal(result.retry, true);
});

test("ordinary completed prose without durable proof is not blindly retried", () => {
  const result = terminationReconciliationDecision({
    state: "done",
    last_action_summary: "Reviewed the requested area; no repository changes were necessary."
  });
  assert.equal(result.recoveryStatus, "work-unverified");
  assert.equal(result.retry, false);
});


test("stream loss without an authoritative durable scan fails closed", () => {
  const result = terminationReconciliationDecision({
    state: "interrupted",
    last_action_summary: "I was working when the response stream disappeared.",
    source_metadata: { stream_lost: true }
  }, { streamLost: true, durableEvidenceChecked: false });
  assert.equal(result.recoveryStatus, "work-unverified");
  assert.equal(result.retry, false);
  assert.equal(result.reason, "durable-evidence-scan-incomplete");
});

test("release-required work is complete only after release verification", () => {
  const incomplete = {
    state: "disconnected",
    source_metadata: {
      pr_merged: true,
      verification_passed: true,
      release_required: true,
      release_verified: false
    }
  };
  assert.equal(hasVerifiedCompletionEvidence(incomplete), false);
  const complete = structuredClone(incomplete);
  complete.source_metadata.release_verified = true;
  assert.equal(hasVerifiedCompletionEvidence(complete), true);
});


test("mid-swarm recovery replaces a crashed lane while other workers keep running", () => {
  const batch = planSwarmTailRecoveryBatch({
    agents: [
      { id: "running", status: "running", taskId: "t-running", task: "still working" },
      { id: "crashed", status: "failed", taskId: "t-crashed", task: "unfinished" }
    ],
    tasks: [
      { id: "t-running", status: "running", objective: "still working" },
      { id: "t-crashed", status: "failed", objective: "unfinished" }
    ]
  });
  assert.equal(batch.length, 1);
  assert.equal(batch[0].rootId, "crashed");
  assert.equal(batch[0].attempt, 1);
});

test("mid-swarm recovery treats orphaned lanes as unfinished", () => {
  const batch = planSwarmTailRecoveryBatch({
    agents: [{ id: "orphaned", status: "orphaned", taskId: "t-orphaned", task: "resume me" }],
    tasks: [{ id: "t-orphaned", status: "running", objective: "resume me" }]
  });
  assert.equal(batch.length, 1);
  assert.equal(batch[0].rootId, "orphaned");
});

test("mid-swarm recovery treats stale-progress lanes as unfinished", () => {
  const batch = planSwarmTailRecoveryBatch({
    agents: [{ id: "stale-support", status: "stale", taskId: "t-stale", task: "resume me" }],
    tasks: [{ id: "t-stale", status: "stale", objective: "resume me" }]
  });
  assert.equal(batch.length, 1);
  assert.equal(batch[0].rootId, "stale-support");
});

test("recovery planner can exclude the phase-owned stale worker to prevent duplicate recovery", () => {
  const batch = planSwarmTailRecoveryBatch({
    agents: [
      { id: "phase-main", status: "stale", taskId: "t-main", task: "phase owned" },
      { id: "support-stale", status: "stale", taskId: "t-support", task: "support owned" }
    ],
    tasks: [
      { id: "t-main", status: "stale", objective: "phase owned" },
      { id: "t-support", status: "stale", objective: "support owned" }
    ]
  }, { excludeAgentIds: ["phase-main"] });
  assert.equal(batch.length, 1);
  assert.equal(batch[0].rootId, "support-stale");
});

test("recovery planner respects retry cooldown and dispatch-failure budget", () => {
  const base = {
    agents: [{
      id: "stale-root",
      status: "interrupted",
      taskId: "t-stale",
      task: "resume me",
      swarmTailRecoveryCause: "stale-progress-timeout",
      stopRequestedAt: "2026-09-29T20:00:00.000Z",
      swarmTailRecoveryDispatchFailures: 1,
      swarmTailRecoveryNextAt: "2026-09-29T20:02:00.000Z"
    }],
    tasks: [{ id: "t-stale", status: "retry-pending", objective: "resume me" }]
  };
  assert.deepEqual(planSwarmTailRecoveryBatch(base, {
    maxAttemptsPerRoot: 2,
    now: Date.parse("2026-09-29T20:01:00.000Z")
  }), []);
  const retry = planSwarmTailRecoveryBatch(base, {
    maxAttemptsPerRoot: 2,
    now: Date.parse("2026-09-29T20:03:00.000Z")
  });
  assert.equal(retry.length, 1);
  assert.equal(retry[0].attempt, 2);

  base.agents[0].swarmTailRecoveryDispatchFailures = 2;
  assert.deepEqual(planSwarmTailRecoveryBatch(base, {
    maxAttemptsPerRoot: 2,
    now: Date.parse("2026-09-29T20:03:00.000Z")
  }), []);
});

test("jittered recovery backoff remains bounded and spreads retries", () => {
  const low = recoveryBackoffWithJitterMs(3, { baseMs: 1000, maxMs: 8000, jitterUnit: 0 });
  const high = recoveryBackoffWithJitterMs(3, { baseMs: 1000, maxMs: 8000, jitterUnit: 1 });
  assert.equal(low, 2000);
  assert.equal(high, 4000);
});

test("recovery pool subtracts already-running recovery workers from its worker budget", () => {
  const agents = [
    { id: "active-recovery", status: "running", taskId: "task-active", task: "recover root-a", swarmTailRecovery: true, recoveryRootAgentId: "root-a" },
    { id: "failed-b", status: "failed", taskId: "task-b", task: "finish b" },
    { id: "failed-c", status: "failed", taskId: "task-c", task: "finish c" },
    { id: "failed-d", status: "failed", taskId: "task-d", task: "finish d" }
  ];
  const tasks = [
    { id: "task-active", status: "running", objective: "recover root-a" },
    { id: "task-b", status: "failed", objective: "finish b" },
    { id: "task-c", status: "failed", objective: "finish c" },
    { id: "task-d", status: "failed", objective: "finish d" }
  ];
  const batch = planSwarmTailRecoveryBatch({ agents, tasks }, { maxWorkers: 2, maxAttemptsPerRoot: 2 });
  assert.equal(batch.length, 1);
  assert.notEqual(batch[0].rootId, "root-a");
});

test("end-of-swarm recovery selects up to four distinct unfinished roots", () => {
  const agents = Array.from({ length: 6 }, (_, index) => ({
    id: `failed-${index}`,
    status: "failed",
    taskId: `task-${index}`,
    task: `finish ${index}`
  }));
  const tasks = agents.map((agent, index) => ({
    id: agent.taskId,
    status: "failed",
    objective: `finish ${index}`
  }));
  const batch = planSwarmTailRecoveryBatch({ agents, tasks }, { maxWorkers: 4, maxAttemptsPerRoot: 2 });
  assert.equal(batch.length, 4);
  assert.equal(new Set(batch.map(item => item.rootId)).size, 4);
  assert.ok(batch.every(item => item.attempt === 1));
});

test("end-of-swarm recovery does not duplicate successful or exhausted recovery lineages", () => {
  const agents = [
    { id: "root-done", status: "failed", taskId: "task-done", task: "one" },
    { id: "closer-done", status: "done", taskId: "task-closer-done", task: "one", retryOfAgentId: "root-done", recoveryRootAgentId: "root-done", swarmTailRecovery: true },
    { id: "root-exhausted", status: "failed", taskId: "task-exhausted", task: "two" },
    { id: "closer-1", status: "failed", taskId: "task-closer-1", task: "two", retryOfAgentId: "root-exhausted", recoveryRootAgentId: "root-exhausted", swarmTailRecovery: true },
    { id: "closer-2", status: "failed", taskId: "task-closer-2", task: "two", retryOfAgentId: "closer-1", recoveryRootAgentId: "root-exhausted", swarmTailRecovery: true }
  ];
  const tasks = agents.map(agent => ({ id: agent.taskId, status: agent.status === "done" ? "candidate" : "failed", objective: agent.task }));
  const batch = planSwarmTailRecoveryBatch({ agents, tasks }, { maxWorkers: 4, maxAttemptsPerRoot: 2 });
  assert.deepEqual(batch, []);
});


test("swarm-tail recovery does not resurrect a clean deterministic nonzero failure", () => {
  const batch = planSwarmTailRecoveryBatch({
    agents: [{
      id: "failed-runtime",
      status: "failed",
      exitCode: 1,
      completionEvidence: "authoritative-exit",
      taskId: "t-runtime",
      task: "do work"
    }],
    tasks: [{ id: "t-runtime", status: "failed", objective: "do work" }]
  });
  assert.deepEqual(batch, []);
});

test("swarm-tail recovery still preserves deterministic failures that left substantive work", () => {
  const batch = planSwarmTailRecoveryBatch({
    agents: [{
      id: "failed-with-work",
      status: "failed",
      exitCode: 1,
      completionEvidence: "authoritative-exit",
      worktreeDirty: true,
      taskId: "t-work",
      task: "finish preserved work"
    }],
    tasks: [{ id: "t-work", status: "failed", objective: "finish preserved work" }]
  });
  assert.equal(batch.length, 1);
  assert.equal(batch[0].rootId, "failed-with-work");
});

