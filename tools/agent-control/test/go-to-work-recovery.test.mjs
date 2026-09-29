import test from "node:test";
import assert from "node:assert/strict";

import {
  chatgptConversationUrl,
  goToWorkPromptHint,
  goToWorkRecoveryCandidate,
  planGoToWorkRecoveries
} from "../lib/go-to-work-recovery.mjs";

test("builds a safe ChatGPT conversation URL from a conversation id", () => {
  assert.equal(
    chatgptConversationUrl({ provider: "chatgpt", conversation_id: "12345678-abcd" }),
    "https://chatgpt.com/c/12345678-abcd"
  );
});

test("rejects non-ChatGPT URLs from source metadata", () => {
  assert.equal(chatgptConversationUrl({
    provider: "chatgpt",
    source_metadata: { conversation_url: "https://example.com/c/12345678" }
  }), null);
});

test("recognizes the real ChatGPT Work handoff card wording", () => {
  assert.equal(goToWorkPromptHint("Go to Work"), true);
  assert.equal(goToWorkPromptHint("Continue in ChatGPT Work"), true);
  assert.equal(goToWorkPromptHint("Continue in Work"), true);
  assert.equal(goToWorkPromptHint("Stay in Chat"), true);
  assert.equal(goToWorkPromptHint("The agent is waiting for a normal reply"), false);
});

test("explicit handoff signal makes an active ChatGPT agent immediately eligible", () => {
  const decision = goToWorkRecoveryCandidate({
    provider: "chatgpt",
    state: "running",
    conversation_id: "abcdefghi",
    last_action_at: new Date().toISOString(),
    source_metadata: { go_to_work_pending: true }
  });
  assert.equal(decision.eligible, true);
  assert.equal(decision.reason, "handoff-signaled");
});

test("blocked ChatGPT sessions are checked even without an explicit handoff flag", () => {
  const decision = goToWorkRecoveryCandidate({
    provider: "chatgpt",
    state: "blocked",
    conversation_id: "abcdefghi"
  });
  assert.equal(decision.eligible, true);
  assert.equal(decision.reason, "state-blocked");
});

test("recently active running sessions are not disturbed", () => {
  const decision = goToWorkRecoveryCandidate({
    provider: "chatgpt",
    state: "running",
    conversation_id: "abcdefghi",
    last_action_at: new Date().toISOString()
  }, { staleAfterMs: 45_000 });
  assert.equal(decision.eligible, false);
  assert.equal(decision.reason, "progress-recent");
});

test("recovery planner prioritizes explicit handoff signals and remains bounded", () => {
  const old = new Date(Date.now() - 120_000).toISOString();
  const federation = {
    agents: [
      { agent_id: "stale-1", provider: "chatgpt", state: "running", conversation_id: "conv11111", last_action_at: old },
      { agent_id: "explicit", provider: "chatgpt", state: "running", conversation_id: "conv22222", source_metadata: { handoff_pending: true } },
      { agent_id: "stale-2", provider: "chatgpt", state: "blocked", conversation_id: "conv33333" }
    ]
  };
  const plan = planGoToWorkRecoveries(federation, { maxPerSweep: 2 });
  assert.equal(plan.length, 2);
  assert.equal(plan[0].agent.agent_id, "explicit");
});
