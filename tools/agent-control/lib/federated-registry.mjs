import { createHash } from "node:crypto";

export const FEDERATION_VERSION = 1;
export const NORMALIZED_AGENT_STATES = new Set([
  "working",
  "tool_wait",
  "blocked",
  "idle",
  "done",
  "failed",
  "disconnected"
]);
export const LIVE_AGENT_STATES = new Set(["working", "tool_wait", "blocked", "idle"]);
export const DEFAULT_STALE_AFTER_MS = 120_000;
export const DEFAULT_DISCONNECTED_AFTER_MS = 300_000;

export const DEFAULT_PROVIDER_DEFINITIONS = Object.freeze([
  {
    id: "local-control",
    label: "Local Agent Control",
    kind: "local-worker",
    discovery: "automated",
    registration: "automated",
    description: "Workers launched and owned by this Agent Control process."
  },
  {
    id: "chatgpt",
    label: "ChatGPT sessions",
    kind: "chatgpt-session",
    discovery: "unavailable",
    registration: "bridge",
    description: "Automatic ChatGPT-project enumeration is unavailable; stable session observations can be registered through the bridge API."
  },
  {
    id: "github",
    label: "GitHub / CI",
    kind: "github",
    discovery: "bridge",
    registration: "bridge",
    description: "PR, workflow, and automation observations may be registered when stable GitHub identifiers are available."
  },
  {
    id: "heaven2-control",
    label: "heaven2 control state",
    kind: "control-machine",
    discovery: "bridge",
    registration: "bridge",
    description: "heaven2 remains the control/credential authority and can publish control-plane observations through the bridge API."
  }
]);

function iso(value = Date.now()) {
  return new Date(value).toISOString();
}

function validDate(value) {
  if (!value) return null;
  const ms = new Date(value).getTime();
  return Number.isFinite(ms) ? new Date(ms).toISOString() : null;
}

function uniqueStrings(values = []) {
  return [...new Set(values.map(value => String(value || "").trim()).filter(Boolean))];
}

function providerDefaults(definition) {
  return {
    id: definition.id,
    label: definition.label || definition.id,
    kind: definition.kind || "external",
    discovery: definition.discovery || "unavailable",
    registration: definition.registration || "unavailable",
    description: definition.description || "",
    status: definition.discovery === "automated" ? "unknown" : "registration-available",
    last_heartbeat_at: null,
    last_error: null,
    metadata: {}
  };
}

export function defaultFederationState() {
  return {
    version: FEDERATION_VERSION,
    stale_after_ms: DEFAULT_STALE_AFTER_MS,
    disconnected_after_ms: DEFAULT_DISCONNECTED_AFTER_MS,
    providers: DEFAULT_PROVIDER_DEFINITIONS.map(providerDefaults),
    agents: []
  };
}

export function migrateFederationState(value) {
  const base = defaultFederationState();
  if (!value || typeof value !== "object") return base;

  const providerMap = new Map(base.providers.map(item => [item.id, item]));
  for (const item of Array.isArray(value.providers) ? value.providers : []) {
    if (!item?.id) continue;
    providerMap.set(String(item.id), {
      ...(providerMap.get(String(item.id)) || providerDefaults({ id: String(item.id) })),
      ...item,
      id: String(item.id),
      metadata: item.metadata && typeof item.metadata === "object" ? item.metadata : {}
    });
  }

  return {
    version: FEDERATION_VERSION,
    stale_after_ms: Number.isFinite(Number(value.stale_after_ms)) ? Math.max(1_000, Number(value.stale_after_ms)) : base.stale_after_ms,
    disconnected_after_ms: Number.isFinite(Number(value.disconnected_after_ms))
      ? Math.max(Number(value.stale_after_ms) || base.stale_after_ms, Number(value.disconnected_after_ms))
      : base.disconnected_after_ms,
    providers: [...providerMap.values()],
    agents: (Array.isArray(value.agents) ? value.agents : []).map(agent => ({
      ...agent,
      agent_id: String(agent.agent_id || ""),
      correlation_keys: uniqueStrings(agent.correlation_keys),
      providers: uniqueStrings(agent.providers),
      observations: Array.isArray(agent.observations) ? agent.observations : []
    })).filter(agent => agent.agent_id)
  };
}

function normalizedState(value) {
  const state = String(value || "").trim().toLowerCase();
  if (!NORMALIZED_AGENT_STATES.has(state)) {
    throw new Error(`Unsupported normalized agent state "${value}".`);
  }
  return state;
}

function deterministicAgentId(provider, sourceId) {
  const digest = createHash("sha256").update(`${provider}\0${sourceId}`).digest("hex").slice(0, 16);
  return `agent-${digest}`;
}

function observationKey(provider, sourceId) {
  return `source:${provider}:${sourceId}`;
}

function correlationKeys(observation) {
  const correlation = observation.correlation && typeof observation.correlation === "object"
    ? observation.correlation
    : {};
  return uniqueStrings([
    ...(Array.isArray(observation.correlation_keys) ? observation.correlation_keys : []),
    observationKey(observation.provider, observation.source_id),
    correlation.logical_agent_id ? `logical:${correlation.logical_agent_id}` : null,
    correlation.controller_agent_id ? `controller-agent:${correlation.controller_agent_id}` : null,
    correlation.chat_session_id ? `chat-session:${correlation.chat_session_id}` : null,
    correlation.github_run_id ? `github-run:${correlation.github_run_id}` : null
  ]);
}

function upsertProvider(federation, providerId, patch = {}) {
  let provider = federation.providers.find(item => item.id === providerId);
  if (!provider) {
    provider = providerDefaults({ id: providerId, label: providerId, discovery: "bridge", registration: "bridge" });
    federation.providers.push(provider);
  }
  Object.assign(provider, patch);
  provider.metadata = patch.metadata && typeof patch.metadata === "object"
    ? { ...(provider.metadata || {}), ...patch.metadata }
    : provider.metadata || {};
  return provider;
}

export function recordProviderHeartbeat(federation, providerId, {
  status = "online",
  at = Date.now(),
  error = null,
  metadata = null
} = {}) {
  const id = String(providerId || "").trim();
  if (!id) throw new Error("provider id is required");
  return upsertProvider(federation, id, {
    status: String(status || "online"),
    last_heartbeat_at: iso(at),
    last_error: error ? String(error) : null,
    ...(metadata && typeof metadata === "object" ? { metadata } : {})
  });
}

export function reconcileObservation(federation, rawObservation, { now = Date.now() } = {}) {
  if (!federation || typeof federation !== "object") throw new Error("federation state is required");
  const observation = rawObservation && typeof rawObservation === "object" ? rawObservation : {};
  const provider = String(observation.provider || "").trim().toLowerCase();
  const sourceId = String(observation.source_id || observation.sourceId || "").trim();
  if (!provider) throw new Error("observation provider is required");
  if (!sourceId) throw new Error("observation source_id is required");

  const state = normalizedState(observation.state);
  const heartbeatAt = validDate(observation.heartbeat_at || observation.heartbeatAt) || iso(now);
  const lastActionAt = validDate(observation.last_action_at || observation.lastActionAt) || heartbeatAt;
  const createdAt = validDate(observation.created_at || observation.createdAt) || heartbeatAt;
  const finishedAt = validDate(observation.finished_at || observation.finishedAt);
  const keys = correlationKeys({ ...observation, provider, source_id: sourceId });
  const explicitAgentId = String(observation.agent_id || observation.agentId || "").trim();

  let agent = explicitAgentId
    ? federation.agents.find(item => item.agent_id === explicitAgentId)
    : null;
  if (!agent) {
    agent = federation.agents.find(item =>
      (item.correlation_keys || []).some(key => keys.includes(key)) ||
      (item.observations || []).some(item => item.provider === provider && item.source_id === sourceId)
    );
  }

  if (!agent) {
    agent = {
      agent_id: explicitAgentId || deterministicAgentId(provider, sourceId),
      provider,
      providers: [provider],
      role: null,
      machine: null,
      parent_manager_id: null,
      task_id: null,
      task: null,
      branch: null,
      pr_number: null,
      heartbeat_at: heartbeatAt,
      state,
      last_action_at: lastActionAt,
      last_action_summary: null,
      created_at: createdAt,
      finished_at: null,
      correlation_keys: keys,
      source_metadata: {},
      observations: []
    };
    federation.agents.push(agent);
  } else if (explicitAgentId && agent.agent_id !== explicitAgentId) {
    throw new Error(`Observation agent_id "${explicitAgentId}" conflicts with reconciled agent "${agent.agent_id}".`);
  }

  agent.provider = provider;
  agent.providers = uniqueStrings([...(agent.providers || []), provider]);
  agent.role = observation.role ?? agent.role ?? null;
  agent.machine = observation.machine ?? agent.machine ?? null;
  agent.parent_manager_id = observation.parent_manager_id ?? observation.parentManagerId ?? agent.parent_manager_id ?? null;
  agent.task_id = observation.task_id ?? observation.taskId ?? agent.task_id ?? null;
  agent.task = observation.task ?? agent.task ?? null;
  agent.branch = observation.branch ?? agent.branch ?? null;
  agent.pr_number = observation.pr_number ?? observation.prNumber ?? agent.pr_number ?? null;
  agent.heartbeat_at = heartbeatAt;
  agent.state = state;
  agent.last_action_at = lastActionAt;
  agent.last_action_summary = observation.last_action_summary ?? observation.lastActionSummary ?? agent.last_action_summary ?? null;
  agent.created_at = validDate(agent.created_at) && new Date(agent.created_at) <= new Date(createdAt) ? agent.created_at : createdAt;
  agent.finished_at = ["done", "failed"].includes(state) ? (finishedAt || heartbeatAt) : null;
  agent.correlation_keys = uniqueStrings([...(agent.correlation_keys || []), ...keys]);
  if (observation.source_metadata && typeof observation.source_metadata === "object") {
    agent.source_metadata = { ...(agent.source_metadata || {}), ...observation.source_metadata };
  }

  const observed = {
    provider,
    source_id: sourceId,
    heartbeat_at: heartbeatAt,
    state,
    last_action_at: lastActionAt,
    metadata: observation.source_metadata && typeof observation.source_metadata === "object"
      ? observation.source_metadata
      : {}
  };
  const existingIndex = (agent.observations || []).findIndex(item => item.provider === provider && item.source_id === sourceId);
  if (existingIndex >= 0) agent.observations[existingIndex] = observed;
  else agent.observations.push(observed);

  upsertProvider(federation, provider, {
    status: "online",
    last_heartbeat_at: heartbeatAt,
    last_error: null
  });

  return agent;
}

function localState(status) {
  const value = String(status || "").toLowerCase();
  if (["reserved", "starting", "running", "stopping"].includes(value)) return "working";
  if (value === "waiting") return "tool_wait";
  if (["blocked", "stale"].includes(value)) return "blocked";
  if (["done", "finished", "stopped"].includes(value)) return "done";
  if (value === "failed") return "failed";
  if (["interrupted", "orphaned"].includes(value)) return "disconnected";
  return "idle";
}

export function syncManagedAgents(federation, managedAgents = [], {
  hostname = null,
  now = Date.now()
} = {}) {
  let changed = 0;
  for (const managed of managedAgents) {
    if (!managed?.id) continue;
    reconcileObservation(federation, {
      provider: "local-control",
      source_id: String(managed.id),
      agent_id: String(managed.id),
      role: managed.role || null,
      machine: managed.machine || hostname || null,
      parent_manager_id: managed.parentManagerId || null,
      task_id: managed.taskId || null,
      task: managed.task || null,
      branch: managed.branchName || null,
      heartbeat_at: managed.heartbeatAt || managed.updatedAt || managed.finishedAt || managed.startedAt || iso(now),
      state: localState(managed.status),
      last_action_at: managed.lastProgressAt || managed.updatedAt || managed.finishedAt || managed.startedAt || iso(now),
      last_action_summary: managed.lastMessage || managed.error || managed.status || null,
      created_at: managed.startedAt || managed.createdAt || iso(now),
      finished_at: managed.finishedAt || null,
      correlation: {
        controller_agent_id: String(managed.id)
      },
      source_metadata: {
        managed_agent_id: String(managed.id),
        pid: managed.pid || null,
        lease_id: managed.leaseId || null,
        boundary: managed.boundary || null
      }
    }, { now });
    changed += 1;
  }

  recordProviderHeartbeat(federation, "local-control", {
    status: "online",
    at: now,
    metadata: { hostname, managed_agents: managedAgents.length }
  });
  return changed;
}

export function materializeAgent(agent, {
  now = Date.now(),
  staleAfterMs = DEFAULT_STALE_AFTER_MS,
  disconnectedAfterMs = DEFAULT_DISCONNECTED_AFTER_MS
} = {}) {
  const heartbeatMs = new Date(agent.heartbeat_at || 0).getTime();
  const ageMs = Number.isFinite(heartbeatMs) ? Math.max(0, now - heartbeatMs) : Number.POSITIVE_INFINITY;
  const terminal = agent.state === "done" || agent.state === "failed";
  let freshness = terminal ? "historical" : "fresh";
  let effectiveState = agent.state;

  if (!terminal) {
    if (!Number.isFinite(heartbeatMs) || ageMs > disconnectedAfterMs || agent.state === "disconnected") {
      freshness = "disconnected";
      effectiveState = "disconnected";
    } else if (ageMs > staleAfterMs) {
      freshness = "stale";
    }
  }

  const live = freshness === "fresh" && LIVE_AGENT_STATES.has(effectiveState);
  return {
    ...agent,
    effective_state: effectiveState,
    freshness,
    heartbeat_age_ms: Number.isFinite(ageMs) ? ageMs : null,
    live,
    historical: terminal
  };
}

export function federationSnapshot(federation, { now = Date.now() } = {}) {
  const staleAfterMs = Number(federation?.stale_after_ms) || DEFAULT_STALE_AFTER_MS;
  const disconnectedAfterMs = Number(federation?.disconnected_after_ms) || DEFAULT_DISCONNECTED_AFTER_MS;
  const agents = (federation?.agents || []).map(agent => materializeAgent(agent, {
    now,
    staleAfterMs,
    disconnectedAfterMs
  }));

  const counts = {
    live: agents.filter(agent => agent.live).length,
    working: agents.filter(agent => agent.live && agent.effective_state === "working").length,
    tool_wait: agents.filter(agent => agent.live && agent.effective_state === "tool_wait").length,
    blocked: agents.filter(agent => agent.live && agent.effective_state === "blocked").length,
    idle: agents.filter(agent => agent.live && agent.effective_state === "idle").length,
    stale: agents.filter(agent => agent.freshness === "stale").length,
    disconnected: agents.filter(agent => agent.freshness === "disconnected").length,
    done: agents.filter(agent => agent.effective_state === "done").length,
    failed: agents.filter(agent => agent.effective_state === "failed").length,
    total: agents.length
  };

  return {
    version: federation?.version || FEDERATION_VERSION,
    stale_after_ms: staleAfterMs,
    disconnected_after_ms: disconnectedAfterMs,
    counts,
    providers: (federation?.providers || []).map(provider => ({ ...provider })),
    agents
  };
}
