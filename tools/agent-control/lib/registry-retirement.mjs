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
const MANAGED_FAILURE_STATUSES = new Set(["failed", "stopped", "capacity-blocked"]);
const FEDERATED_TERMINAL_STATES = new Set(["done", "failed"]);
const ACTIVE_REGISTRY_RECOVERY_STATES = new Set([
  "retry-pending",
  "retry-waiting",
  "retry-blocked",
  "stream-lost-checking-work",
  "work-detected-incomplete",
  "registry-retirement-blocked"
]);
const PROVEN_REMOTE_TERMINAL_STATES = new Set(["completed", "done", "failed", "error", "timeout", "cancelled"]);

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

export function managedAgentRegistryDisposition(agent, {
  processAlive = false,
  durableWork = false
} = {}) {
  if (!agent || typeof agent !== "object") return { retire: false, reason: "invalid-agent" };
  const status = text(agent.status || agent.state).toLowerCase();
  const recovery = text(agent.recoveryStatus || agent.recovery_status).toLowerCase();

  if (agent.remoteTerminationPending) {
    return { retire: false, reason: "recovery-active:remote-termination-pending" };
  }
  if (!MANAGED_FAILURE_STATUSES.has(status)) {
    return { retire: false, reason: "non-retirable-managed-status" };
  }
  if (ACTIVE_REGISTRY_RECOVERY_STATES.has(recovery)) {
    return { retire: false, reason: `recovery-active:${recovery}` };
  }
  if (processAlive) {
    return { retire: false, reason: "process-still-alive" };
  }
  if (durableWork) {
    return { retire: false, reason: "durable-work-pending-reconciliation" };
  }
  return { retire: true, reason: recovery ? `${status}:${recovery}` : status };
}

export function federatedAgentRegistryDisposition(agent, {
  now = Date.now(),
  retirementAfterMs = 1_800_000
} = {}) {
  if (!agent || typeof agent !== "object") return { retire: false, reason: "invalid-agent" };
  const state = text(agent.state).toLowerCase();
  const recovery = text(agent.recovery_status || agent.recoveryStatus).toLowerCase();

  if (ACTIVE_REGISTRY_RECOVERY_STATES.has(recovery)) {
    return { retire: false, reason: `recovery-active:${recovery}` };
  }
  if (FEDERATED_TERMINAL_STATES.has(state)) {
    return { retire: true, reason: `terminal:${state}` };
  }

  const heartbeatMs = Date.parse(text(agent.heartbeat_at ?? agent.heartbeatAt));
  const threshold = Math.max(1, Number(retirementAfterMs) || 1_800_000);
  if (!Number.isFinite(heartbeatMs)) {
    return { retire: true, reason: "disconnected:missing-heartbeat" };
  }
  if (Number(now) - heartbeatMs > threshold) {
    return { retire: true, reason: "disconnected:heartbeat-expired" };
  }
  return { retire: false, reason: state === "disconnected" ? "disconnected-grace-period" : "current-presence" };
}

export function retirementSourcesForFederatedAgent(agent) {
  if (!agent || typeof agent !== "object") return [];
  const sources = [];
  const seen = new Set();
  const add = (provider, sourceId) => {
    const key = retirementKey(provider, sourceId);
    if (!key || seen.has(key)) return;
    seen.add(key);
    sources.push({ provider: providerId(provider), sourceId: text(sourceId), key });
  };
  add(agent.provider, agent.source_id ?? agent.sourceId);
  for (const observation of Array.isArray(agent.observations) ? agent.observations : []) {
    add(observation?.provider, observation?.source_id ?? observation?.sourceId);
  }
  return sources;
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
  const next = current.filter(item => item.key !== key);
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
  if (!LIVE_FEDERATED_STATES.has(state)) {
    return { suppress: true, reactivate: false, key, retirement };
  }

  // Evaluate the raw provider heartbeat before federation normalization can
  // synthesize a current timestamp. Retirement only clears for genuinely
  // newer live evidence.
  const heartbeatRaw = observation.heartbeat_at ?? observation.heartbeatAt;
  const heartbeatMs = Date.parse(text(heartbeatRaw));
  const retiredAtMs = Date.parse(text(retirement.retiredAt));
  if (!Number.isFinite(heartbeatMs) || !Number.isFinite(retiredAtMs) || heartbeatMs <= retiredAtMs) {
    return { suppress: true, reactivate: false, key, retirement };
  }

  return { suppress: false, reactivate: true, key, retirement };
}


export function isProvenRemoteTerminalJobState(value) {
  return PROVEN_REMOTE_TERMINAL_STATES.has(text(value).toLowerCase());
}

export async function proveRemoteJobStopped({
  remoteJobId,
  cancelJob,
  getStatus,
  timeoutMs = 15_000,
  pollIntervalMs = 250,
  delay = ms => new Promise(resolve => setTimeout(resolve, ms))
} = {}) {
  const id = text(remoteJobId);
  if (!id) throw new Error("Remote job id is required for retirement proof.");
  if (typeof cancelJob !== "function") throw new Error("Remote cancellation callback is required.");
  if (typeof getStatus !== "function") throw new Error("Remote status callback is required.");

  const checkCancellation = async () => {
    const cancellation = await cancelJob(id);
    if (!cancellation?.succeeded) {
      throw new Error(`Remote job ${id} cancellation was not authoritative.`);
    }
    const reason = text(cancellation.reason).toLowerCase();
    if (cancellation.cancelRequested !== true && reason !== "not_running") {
      throw new Error(`Remote job ${id} cancellation ownership was not confirmed.`);
    }
    return cancellation;
  };

  await checkCancellation();

  const interval = Math.max(1, Math.floor(Number(pollIntervalMs) || 250));
  const timeout = Math.max(interval, Math.floor(Number(timeoutMs) || 15_000));
  let lastState = "unknown";

  for (let elapsed = 0; elapsed < timeout; elapsed += interval) {
    const status = await getStatus(id);
    if (!status?.succeeded) {
      throw new Error(`Remote job ${id} status lookup was not authoritative.`);
    }

    lastState = text(status.state).toLowerCase() || "unknown";
    if (isProvenRemoteTerminalJobState(lastState)) {
      return { required: true, stopped: true, state: lastState };
    }

    // A queued job can report not-running/unknown and start later. If it races
    // into running, reassert cancellation and keep waiting for terminal proof.
    if (lastState === "running") {
      await checkCancellation();
    }

    if (elapsed + interval < timeout) await delay(interval);
  }

  throw new Error(`Remote job ${id} termination was not proven; last state was ${lastState}.`);
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
