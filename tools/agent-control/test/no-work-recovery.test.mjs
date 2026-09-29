import test from "node:test";
import assert from "node:assert/strict";

import {
  hasSubstantiveWorkEvidence,
  hasVerifiedCompletionEvidence,
  looksLikeExecutionOpener,
  noWorkTerminationDecision,
  planSwarmTailRecoveryBatch,
  recoveryBackoffMs,
  recoveryMachineTarget,
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


test("end-of-swarm recovery waits until the active wave drains", () => {
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
  assert.deepEqual(batch, []);
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
