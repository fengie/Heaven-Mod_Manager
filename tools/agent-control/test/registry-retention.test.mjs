import test from "node:test";
import assert from "node:assert/strict";

import {
  federatedAgentRegistryDisposition,
  managedAgentRegistryDisposition,
  purgeFailedFederatedAgents,
  purgeFederatedAgentIds
} from "../lib/registry-retention.mjs";

test("dead managed failures are retired instead of persisting in the registry", () => {
  assert.deepEqual(
    managedAgentRegistryDisposition({
      id: "dead-1",
      status: "failed",
      recoveryStatus: "retry-exhausted"
    }),
    { retire: true, reason: "failed:retry-exhausted" }
  );
  assert.equal(
    managedAgentRegistryDisposition({ id: "dead-2", status: "failed" }).retire,
    true
  );
  assert.equal(
    managedAgentRegistryDisposition({
      id: "retrying",
      status: "failed",
      recoveryStatus: "retry-pending"
    }).retire,
    false
  );
  assert.equal(
    managedAgentRegistryDisposition({
      id: "preserve-work",
      status: "interrupted",
      recoveryStatus: "work-detected-incomplete"
    }).retire,
    false
  );
  assert.equal(
    managedAgentRegistryDisposition({
      id: "still-running",
      status: "failed",
      recoveryStatus: "retry-exhausted"
    }, { processAlive: true }).retire,
    false
  );
});

test("failed federated records retire unless recovery still owns them", () => {
  assert.equal(
    federatedAgentRegistryDisposition({
      agent_id: "fed-failed",
      state: "failed",
      recovery_status: "retry-exhausted"
    }).retire,
    true
  );
  assert.equal(
    federatedAgentRegistryDisposition({
      agent_id: "fed-retrying",
      state: "failed",
      recovery_status: "retry-waiting"
    }).retire,
    false
  );
  assert.equal(
    federatedAgentRegistryDisposition({
      agent_id: "fed-live",
      state: "working"
    }).retire,
    false
  );
});

test("federated purge removes controller-linked tombstones without touching live peers", () => {
  const federation = {
    agents: [
      {
        agent_id: "logical-dead",
        state: "failed",
        source_metadata: { managed_agent_id: "managed-dead" }
      },
      {
        agent_id: "managed-dead",
        state: "failed"
      },
      {
        agent_id: "live-peer",
        state: "working",
        source_metadata: { managed_agent_id: "managed-live" }
      }
    ]
  };

  assert.equal(purgeFederatedAgentIds(federation, ["managed-dead"]), 2);
  assert.deepEqual(federation.agents.map(item => item.agent_id), ["live-peer"]);
});

test("bulk federated failure GC preserves retry-pending records", () => {
  const federation = {
    agents: [
      { agent_id: "exhausted", state: "failed", recovery_status: "retry-exhausted" },
      { agent_id: "blocked", state: "failed", recovery_status: "retry-blocked" },
      { agent_id: "pending", state: "failed", recovery_status: "retry-pending" },
      { agent_id: "working", state: "working" }
    ]
  };

  const retired = purgeFailedFederatedAgents(federation);
  assert.deepEqual(retired.map(item => item.id), ["exhausted", "blocked"]);
  assert.deepEqual(federation.agents.map(item => item.agent_id), ["pending", "working"]);
});
