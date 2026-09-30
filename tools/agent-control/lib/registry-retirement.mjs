const RETIRABLE_MANAGED_STATUSES = new Set([
  "done",
  "failed",
  "finished",
  "stopped",
  "interrupted",
  "orphaned",
  "capacity-blocked"
]);

const LIVE_FEDERATED_STATES = new Set(["working", "tool_wait", "blocked", "idle"]);

function text(value) {
  return String(value ?? "").trim();
}

function providerId(value) {
  return text(value).toLowerCase();
}

export function retirementKey(provider, sourceId) {
  const normalizedProvider = providerId(provider);
  const normalizedSource = text(sourceId);
  if (!normalizedProvider || !normalizedSource) return null;
  return `${normalizedProvider}:${normalizedSource}`;
}

export function normalizeRetiredAgents(value, limit = 500) {
  const rows = Array.isArray(value) ? value : [];
  const seen = new Set();
  const normalized = [];
  for (let index = rows.length - 1; index >= 0 && normalized.length < limit; index -= 1) {
    const row = rows[index];
    if (!row || typeof row !== "object" || Array.isArray(row)) continue;
    const key = retirementKey(row.provider, row.sourceId) || (text(row.agentId) ? `agent:${text(row.agentId)}` : null);
    if (!key || seen.has(key)) continue;
    seen.add(key);
    normalized.push({ ...row, key });
  }
  return normalized.reverse();
}

export function isRetryExhaustedManagedAgent(agent) {
  if (!agent || typeof agent !== "object") return false;
  const status = text(agent.status || agent.state).toLowerCase();
  const recovery = text(agent.recoveryStatus || agent.recovery_status).toLowerCase();
  return recovery === "retry-exhausted" && RETIRABLE_MANAGED_STATUSES.has(status);
}

export function managedAgentRetirementDecision(agent, {
  managedAgentIds = [],
  retiredAgentIds = []
} = {}) {
  if (!agent || typeof agent !== "object") {
    return { retire: false, reason: null };
  }

  if (isRetryExhaustedManagedAgent(agent)) {
    return { retire: true, reason: "no-work-retry-exhausted" };
  }

  const status = text(agent.status || agent.state).toLowerCase();
  const recovery = text(agent.recoveryStatus || agent.recovery_status).toLowerCase();
  if (!RETIRABLE_MANAGED_STATUSES.has(status) || recovery !== "retry-dispatched") {
    return { retire: false, reason: null };
  }

  const replacementAgentId = text(
    agent.replacementAgentId
    || agent.replacement_agent_id
    || agent.replacementId
    || agent.replacement_id
  );
  if (!replacementAgentId || replacementAgentId === text(agent.id || agent.agent_id)) {
    return { retire: false, reason: null };
  }

  const known = new Set([
    ...(Array.isArray(managedAgentIds) ? managedAgentIds : []),
    ...(Array.isArray(retiredAgentIds) ? retiredAgentIds : [])
  ].map(text).filter(Boolean));

  if (!known.has(replacementAgentId)) {
    return { retire: false, reason: null };
  }

  return {
    retire: true,
    reason: "retry-dispatched-superseded",
    replacementAgentId
  };
}

export function recordAgentRetirement(state, {
  agentId = null,
  provider,
  sourceId,
  taskId = null,
  role = null,
  machine = null,
  branch = null,
  status = null,
  recoveryStatus = "retry-exhausted",
  reason = "retry-exhausted",
  retiredAt = new Date().toISOString()
} = {}) {
  if (!state || typeof state !== "object") throw new Error("control state is required");
  const key = retirementKey(provider, sourceId);
  if (!key) throw new Error("retirement provider and source id are required");
  const current = normalizeRetiredAgents(state.retiredAgents);
  const next = current.filter(item => item.key !== key && (!agentId || item.agentId !== agentId));
  next.push({
    key,
    agentId: text(agentId) || null,
    provider: providerId(provider),
    sourceId: text(sourceId),
    taskId: text(taskId) || null,
    role: text(role) || null,
    machine: text(machine) || null,
    branch: text(branch) || null,
    status: text(status) || null,
    recoveryStatus: text(recoveryStatus) || null,
    reason: text(reason) || "retry-exhausted",
    retiredAt
  });
  state.retiredAgents = normalizeRetiredAgents(next);
  return state.retiredAgents[state.retiredAgents.length - 1];
}

export function forgetFederatedAgent(federation, {
  agentId = null,
  provider = null,
  sourceId = null
} = {}) {
  if (!federation || typeof federation !== "object") return 0;
  const before = Array.isArray(federation.agents) ? federation.agents.length : 0;
  const key = retirementKey(provider, sourceId);
  federation.agents = (Array.isArray(federation.agents) ? federation.agents : []).filter(agent => {
    if (agentId && text(agent?.agent_id) === text(agentId)) return false;
    if (!key) return true;
    const direct = retirementKey(agent?.provider, agent?.source_id);
    if (direct === key) return false;
    return !(Array.isArray(agent?.observations) && agent.observations.some(
      observation => retirementKey(observation?.provider, observation?.source_id) === key
    ));
  });
  return before - federation.agents.length;
}

export function retiredObservationDecision(retiredAgents, observation = {}) {
  const provider = providerId(observation.provider);
  const sourceId = text(observation.source_id ?? observation.sourceId);
  const key = retirementKey(provider, sourceId);
  if (!key) return { suppress: false, reactivate: false, key: null, retirement: null };
  const retirement = normalizeRetiredAgents(retiredAgents).find(item => item.key === key) || null;
  if (!retirement) return { suppress: false, reactivate: false, key, retirement: null };
  const state = text(observation.state).toLowerCase();
  if (LIVE_FEDERATED_STATES.has(state)) {
    return { suppress: false, reactivate: true, key, retirement };
  }
  return { suppress: true, reactivate: false, key, retirement };
}

export function clearObservationRetirement(state, observation = {}) {
  if (!state || typeof state !== "object") return false;
  const key = retirementKey(observation.provider, observation.source_id ?? observation.sourceId);
  if (!key) return false;
  const before = normalizeRetiredAgents(state.retiredAgents);
  const after = before.filter(item => item.key !== key);
  state.retiredAgents = after;
  return after.length !== before.length;
}
