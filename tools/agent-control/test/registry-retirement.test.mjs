import test from "node:test";
import assert from "node:assert/strict";

import {
  clearObservationRetirement,
  forgetFederatedAgent,
  isProvenRemoteTerminalJobState,
  isRetryExhaustedManagedAgent,
  proveRemoteJobStopped,
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

test("retired source reactivation requires a strictly newer explicit live heartbeat", () => {
  const retiredAt = "2026-09-30T07:00:00.000Z";
  const state = { retiredAgents: [] };
  recordAgentRetirement(state, {
    agentId: "chat-agent",
    provider: "chatgpt",
    sourceId: "conversation-123",
    status: "failed",
    retiredAt
  });

  const decide = observation => retiredObservationDecision(state.retiredAgents, {
    provider: "chatgpt",
    source_id: "conversation-123",
    ...observation
  });

  for (const heartbeat_at of ["2026-09-30T06:59:59.999Z", retiredAt, "not-a-time"]) {
    const decision = decide({ state: "working", heartbeat_at });
    assert.equal(decision.suppress, true, heartbeat_at);
    assert.equal(decision.reactivate, false, heartbeat_at);
  }

  const missing = decide({ state: "working" });
  assert.equal(missing.suppress, true);
  assert.equal(missing.reactivate, false);

  const terminal = decide({ state: "failed", heartbeat_at: "2026-09-30T07:00:01.000Z" });
  assert.equal(terminal.suppress, true);
  assert.equal(terminal.reactivate, false);

  const live = decide({ state: "working", heartbeat_at: "2026-09-30T07:00:00.001Z" });
  assert.equal(live.suppress, false);
  assert.equal(live.reactivate, true);

  assert.equal(clearObservationRetirement(state, {
    provider: "chatgpt",
    source_id: "conversation-123"
  }), true);
  assert.deepEqual(state.retiredAgents, []);
});

test("remote retirement proof accepts only explicit processed terminal states", async () => {
  for (const state of ["completed", "done", "failed", "error", "timeout", "cancelled"]) {
    assert.equal(isProvenRemoteTerminalJobState(state), true, state);
  }
  for (const state of ["", "unknown", "running", "queued", "not_running", "success"]) {
    assert.equal(isProvenRemoteTerminalJobState(state), false, state);
  }

  const proof = await proveRemoteJobStopped({
    remoteJobId: "remote-terminal",
    cancelJob: async () => ({ succeeded: true, cancelRequested: false, reason: "not_running" }),
    getStatus: async () => ({ succeeded: true, state: "completed" }),
    timeoutMs: 2,
    pollIntervalMs: 1,
    delay: async () => {}
  });
  assert.equal(proof.stopped, true);
  assert.equal(proof.state, "completed");
});

test("remote retirement proof fails closed for queued not-running plus unknown status", async () => {
  let statusCalls = 0;
  await assert.rejects(
    proveRemoteJobStopped({
      remoteJobId: "remote-queued",
      cancelJob: async () => ({ succeeded: true, cancelRequested: false, reason: "not_running" }),
      getStatus: async () => {
        statusCalls += 1;
        return { succeeded: true, state: "unknown" };
      },
      timeoutMs: 3,
      pollIntervalMs: 1,
      delay: async () => {}
    }),
    /termination was not proven; last state was unknown/
  );
  assert.equal(statusCalls, 3);
});

test("remote retirement proof re-cancels a job that becomes running and waits for terminal proof", async () => {
  let cancelCalls = 0;
  const statuses = ["running", "cancelled"];
  const proof = await proveRemoteJobStopped({
    remoteJobId: "remote-race",
    cancelJob: async () => {
      cancelCalls += 1;
      return cancelCalls === 1
        ? { succeeded: true, cancelRequested: false, reason: "not_running" }
        : { succeeded: true, cancelRequested: true, reason: "cancel_requested" };
    },
    getStatus: async () => ({ succeeded: true, state: statuses.shift() || "cancelled" }),
    timeoutMs: 3,
    pollIntervalMs: 1,
    delay: async () => {}
  });
  assert.equal(cancelCalls, 2);
  assert.equal(proof.state, "cancelled");
});

test("remote retirement proof fails closed on cancellation or status authority failures", async () => {
  await assert.rejects(
    proveRemoteJobStopped({
      remoteJobId: "cancel-failed",
      cancelJob: async () => ({ succeeded: false }),
      getStatus: async () => ({ succeeded: true, state: "cancelled" }),
      timeoutMs: 1,
      pollIntervalMs: 1,
      delay: async () => {}
    }),
    /cancellation was not authoritative/
  );

  await assert.rejects(
    proveRemoteJobStopped({
      remoteJobId: "cancel-unowned",
      cancelJob: async () => ({ succeeded: true, cancelRequested: false, reason: "denied" }),
      getStatus: async () => ({ succeeded: true, state: "cancelled" }),
      timeoutMs: 1,
      pollIntervalMs: 1,
      delay: async () => {}
    }),
    /cancellation ownership was not confirmed/
  );

  await assert.rejects(
    proveRemoteJobStopped({
      remoteJobId: "status-failed",
      cancelJob: async () => ({ succeeded: true, cancelRequested: true, reason: "cancel_requested" }),
      getStatus: async () => ({ succeeded: false, state: "unknown" }),
      timeoutMs: 1,
      pollIntervalMs: 1,
      delay: async () => {}
    }),
    /status lookup was not authoritative/
  );
});
