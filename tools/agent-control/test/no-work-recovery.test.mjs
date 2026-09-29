import test from "node:test";
import assert from "node:assert/strict";

import {
  hasSubstantiveWorkEvidence,
  looksLikeExecutionOpener,
  noWorkTerminationDecision,
  recoveryBackoffMs
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

test("tool-call evidence from a federated provider prevents false no-work classification", () => {
  const result = noWorkTerminationDecision({
    state: "failed",
    last_action_summary: "I’m treating this as an execution assignment.",
    source_metadata: { execution_assignment: true, tool_call_count: 4 }
  });
  assert.equal(result.noWork, false);
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
