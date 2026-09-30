import test from "node:test";
import assert from "node:assert/strict";

import {
  clearObservationRetirement,
  forgetFederatedAgent,
  isRetryExhaustedManagedAgent,
  recordAgentRetirement,
  retiredObservationDecision
} from "../lib/registry-retirement.mjs";

test("only terminal retry-exhausted managed agents qualify for retirement", () => {
  assert.equal(isRetryExhaustedManagedAgent({
    status: "failed",
    recoveryStatus: "retry-exhausted"
  }), true);
  assert.equal(isRetryExhaustedManagedAgent({
    status: "running",
    recoveryStatus: "retry-exhausted"
  }), false);
  assert.equal(isRetryExhaustedManagedAgent({
    status: "done",
    recoveryStatus: "work-verified-complete"
  }), false);
});

test("retirement ledger deduplicates the same provider source while preserving durable summary", () => {
  const state = { retiredAgents: [] };
  recordAgentRetirement(state, {
    agentId: "agent-1",
    provider: "local-control",
    sourceId: "agent-1",
    taskId: "task-1",
    role: "support",
    status: "failed",
    reason: "no-work-retry-exhausted",
    retiredAt: "2026-09-30T07:00:00.000Z"
  });
  recordAgentRetirement(state, {
    agentId: "agent-1",
    provider: "local-control",
    sourceId: "agent-1",
    taskId: "task-1",
    role: "support",
    status: "failed",
    reason: "no-work-retry-exhausted",
    retiredAt: "2026-09-30T07:01:00.000Z"
  });

  assert.equal(state.retiredAgents.length, 1);
  assert.equal(state.retiredAgents[0].agentId, "agent-1");
  assert.equal(state.retiredAgents[0].taskId, "task-1");
  assert.equal(state.retiredAgents[0].retiredAt, "2026-09-30T07:01:00.000Z");
});

test("federated pruning removes the retired logical agent and leaves unrelated history intact", () => {
  const federation = {
    agents: [
      {
        agent_id: "failed-agent",
        provider: "local-control",
        source_id: "failed-agent",
        observations: [{ provider: "local-control", source_id: "failed-agent" }]
      },
      {
        agent_id: "done-candidate",
        provider: "local-control",
        source_id: "done-candidate",
        observations: [{ provider: "local-control", source_id: "done-candidate" }]
      }
    ]
  };

  const removed = forgetFederatedAgent(federation, {
    agentId: "failed-agent",
    provider: "local-control",
    sourceId: "failed-agent"
  });

  assert.equal(removed, 1);
  assert.deepEqual(federation.agents.map(agent => agent.agent_id), ["done-candidate"]);
});

test("terminal observations for retired sources are suppressed but a real live heartbeat reactivates them", () => {
  const state = { retiredAgents: [] };
  recordAgentRetirement(state, {
    agentId: "chat-agent",
    provider: "chatgpt",
    sourceId: "conversation-123",
    status: "failed"
  });

  const terminal = retiredObservationDecision(state.retiredAgents, {
    provider: "chatgpt",
    source_id: "conversation-123",
    state: "failed"
  });
  assert.equal(terminal.suppress, true);
  assert.equal(terminal.reactivate, false);

  const live = retiredObservationDecision(state.retiredAgents, {
    provider: "chatgpt",
    source_id: "conversation-123",
    state: "working"
  });
  assert.equal(live.suppress, false);
  assert.equal(live.reactivate, true);
  assert.equal(clearObservationRetirement(state, {
    provider: "chatgpt",
    source_id: "conversation-123"
  }), true);
  assert.deepEqual(state.retiredAgents, []);
});
