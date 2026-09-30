const MANAGED_FAILURE_STATES = new Set(["failed", "interrupted", "orphaned", "capacity-blocked"]);
const ACTIVE_RECOVERY_STATES = new Set([
  "retry-pending",
  "retry-waiting",
  "stream-lost-checking-work",
  "work-detected-incomplete"
]);

const normalize = value => String(value || "").trim().toLowerCase();

export function managedAgentRegistryDisposition(agent, {
  processAlive = false,
  ownedChildAlive = false
} = {}) {
  const status = normalize(agent?.status);
  const recoveryStatus = normalize(agent?.recoveryStatus);

  if (!MANAGED_FAILURE_STATES.has(status)) {
    return { retire: false, reason: "non-failure-state" };
  }
  if (ACTIVE_RECOVERY_STATES.has(recoveryStatus)) {
    return { retire: false, reason: `recovery-active:${recoveryStatus}` };
  }
  if (ownedChildAlive || processAlive) {
    return { retire: false, reason: "process-still-alive" };
  }

  return {
    retire: true,
    reason: recoveryStatus ? `${status}:${recoveryStatus}` : status
  };
}

export function federatedAgentRegistryDisposition(agent) {
  const state = normalize(agent?.state);
  const recoveryStatus = normalize(agent?.recovery_status);

  if (state !== "failed") {
    return { retire: false, reason: "non-failed-federated-state" };
  }
  if (ACTIVE_RECOVERY_STATES.has(recoveryStatus)) {
    return { retire: false, reason: `recovery-active:${recoveryStatus}` };
  }
  return {
    retire: true,
    reason: recoveryStatus ? `failed:${recoveryStatus}` : "failed"
  };
}

export function purgeFederatedAgentIds(federation, agentIds = []) {
  if (!Array.isArray(federation?.agents)) return 0;
  const ids = new Set((agentIds || []).map(value => String(value || "")).filter(Boolean));
  if (!ids.size) return 0;

  const before = federation.agents.length;
  federation.agents = federation.agents.filter(agent => {
    const metadata = agent?.source_metadata && typeof agent.source_metadata === "object"
      ? agent.source_metadata
      : {};
    const correlation = agent?.correlation && typeof agent.correlation === "object"
      ? agent.correlation
      : {};
    return !ids.has(String(agent?.agent_id || ""))
      && !ids.has(String(metadata.managed_agent_id || ""))
      && !ids.has(String(correlation.controller_agent_id || ""));
  });
  return before - federation.agents.length;
}

export function purgeFailedFederatedAgents(federation) {
  if (!Array.isArray(federation?.agents)) return [];
  const retired = [];
  federation.agents = federation.agents.filter(agent => {
    const disposition = federatedAgentRegistryDisposition(agent);
    if (!disposition.retire) return true;
    retired.push({
      id: agent?.agent_id || null,
      reason: disposition.reason
    });
    return false;
  });
  return retired;
}
