import { createHash } from "node:crypto";

export const FEDERATION_VERSION = 2;
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
export const RETIRED_MANAGED_AGENT_STATUSES = new Set(["failed", "finished", "stopped", "interrupted", "orphaned", "capacity-blocked"]);
export const TERMINAL_RECOVERY_STATUSES = new Set(["retry-exhausted", "retry-blocked", "retry-disabled"]);
export const DEFAULT_STALE_AFTER_MS = 120_000;
export const DEFAULT_DISCONNECTED_AFTER_MS = 300_000;
export const PROVIDER_HEALTH_STATES = new Set([
  "unknown",
  "registration-available",
  "online",
  "degraded",
  "offline",
  "disconnected",
  "unavailable",
  "unsupported",
  "unhealthy",
  "not-configured"
]);

const NON_IDENTITY_CORRELATION_NAMESPACES = new Set([
  "task",
  "task-id",
  "pr",
  "pull-request",
  "branch",
  "title",
  "display-title",
  "role",
  "machine",
  "github-pr"
]);
const RESERVED_CORRELATION_NAMESPACES = new Set(["source"]);

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
    id: "heaven-bridge",
    label: "Heaven Local Bridge",
    kind: "remote-worker",
    discovery: "automated",
    registration: "automated",
    description: "Authenticated relay-backed execution transport for the heaven worker when configured and healthy."
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

function normalizeProviderId(value) {
  return String(value || "").trim().toLowerCase();
}

function normalizeOptionalString(value) {
  if (value === undefined || value === null) return null;
  const normalized = String(value).trim();
  return normalized || null;
}

function normalizeRequiredDate(value, field, fallback = null) {
  if (value === undefined || value === null || value === "") return fallback;
  const normalized = validDate(value);
  if (!normalized) throw new Error(field + " must be a valid date/time value.");
  return normalized;
}

function normalizeMetadata(value, field = "source_metadata") {
  if (value === undefined || value === null) return {};
  if (typeof value !== "object" || Array.isArray(value)) {
    throw new Error(field + " must be an object when provided.");
  }
  return { ...value };
}

function normalizePrNumber(value) {
  if (value === undefined || value === null || value === "") return null;
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed <= 0) {
    throw new Error("pr_number must be a positive integer when provided.");
  }
  return parsed;
}

function correlationNamespace(key) {
  const value = String(key || "").trim();
  const separator = value.indexOf(":");
  if (separator <= 0 || separator === value.length - 1) return null;
  return value.slice(0, separator).toLowerCase();
}

function isIdentityCorrelationKey(key) {
  const namespace = correlationNamespace(key);
  return Boolean(namespace) &&
    !RESERVED_CORRELATION_NAMESPACES.has(namespace) &&
    !NON_IDENTITY_CORRELATION_NAMESPACES.has(namespace);
}

function normalizeSuppliedCorrelationKeys(values = []) {
  const normalized = [];
  for (const raw of Array.isArray(values) ? values : []) {
    const key = String(raw || "").trim();
    if (!key) continue;
    const namespace = correlationNamespace(key);
    if (!namespace) {
      throw new Error("correlation_keys must be namespaced as namespace:value.");
    }
    if (RESERVED_CORRELATION_NAMESPACES.has(namespace)) {
      throw new Error("source:* correlation keys are reserved for provider/source identity.");
    }
    if (NON_IDENTITY_CORRELATION_NAMESPACES.has(namespace)) {
      throw new Error("Correlation namespace " + namespace + " is metadata, not a logical-agent identity key.");
    }
    const separator = key.indexOf(":");
    normalized.push(namespace + ":" + key.slice(separator + 1));
  }
  return uniqueStrings(normalized);
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
  if (!value || typeof value !== "object" || Array.isArray(value)) return base;

  const providerMap = new Map(base.providers.map(item => [item.id, item]));
  const ensurePersistedProvider = providerId => {
    const id = normalizeProviderId(providerId);
    if (!id) return null;
    if (!providerMap.has(id)) {
      providerMap.set(id, {
        ...providerDefaults({
          id,
          label: id,
          kind: "external",
          discovery: "unavailable",
          registration: "unavailable",
          description: "Persisted provider has no installed adapter in this build."
        }),
        status: "unsupported"
      });
    }
    return providerMap.get(id);
  };

  for (const item of Array.isArray(value.providers) ? value.providers : []) {
    const id = normalizeProviderId(item?.id);
    if (!id) continue;
    const defaults = providerMap.get(id) || providerDefaults({ id, label: id });
    const status = PROVIDER_HEALTH_STATES.has(String(item.status || ""))
      ? String(item.status)
      : defaults.status;
    providerMap.set(id, {
      ...defaults,
      ...item,
      id,
      status,
      last_heartbeat_at: validDate(item.last_heartbeat_at),
      metadata: item.metadata && typeof item.metadata === "object" && !Array.isArray(item.metadata)
        ? { ...item.metadata }
        : {}
    });
  }

  const normalizePersistedObservation = raw => {
    if (!raw || typeof raw !== "object" || Array.isArray(raw)) return null;
    const provider = normalizeProviderId(raw.provider);
    const sourceId = String(raw.source_id || raw.sourceId || "").trim();
    if (!provider || !sourceId) return null;
    ensurePersistedProvider(provider);
    let state = "disconnected";
    try {
      state = normalizedState(raw.state);
    } catch {
      state = "disconnected";
    }
    return {
      provider,
      source_id: sourceId,
      runtime: normalizeOptionalString(raw.runtime) || provider,
      session_id: normalizeOptionalString(raw.session_id || raw.sessionId),
      conversation_id: normalizeOptionalString(raw.conversation_id || raw.conversationId),
      heartbeat_at: validDate(raw.heartbeat_at || raw.heartbeatAt),
      state,
      last_action_at: validDate(raw.last_action_at || raw.lastActionAt || raw.heartbeat_at || raw.heartbeatAt),
      metadata: raw.metadata && typeof raw.metadata === "object" && !Array.isArray(raw.metadata)
        ? { ...raw.metadata }
        : {}
    };
  };

  const mergeObservations = observations => {
    const bySource = new Map();
    for (const observation of observations.filter(Boolean)) {
      const key = observation.provider + "\0" + observation.source_id;
      const existing = bySource.get(key);
      const existingMs = existing?.heartbeat_at ? new Date(existing.heartbeat_at).getTime() : Number.NEGATIVE_INFINITY;
      const incomingMs = observation.heartbeat_at ? new Date(observation.heartbeat_at).getTime() : Number.NEGATIVE_INFINITY;
      if (!existing || incomingMs >= existingMs) bySource.set(key, observation);
    }
    return [...bySource.values()];
  };

  const migrateAgent = raw => {
    if (!raw || typeof raw !== "object" || Array.isArray(raw)) return null;
    const agentId = String(raw.agent_id || raw.agentId || "").trim();
    if (!agentId) return null;
    const observations = mergeObservations((Array.isArray(raw.observations) ? raw.observations : [])
      .map(normalizePersistedObservation));
    const latest = [...observations].sort((a, b) =>
      new Date(b.heartbeat_at || 0).getTime() - new Date(a.heartbeat_at || 0).getTime())[0] || null;
    const provider = normalizeProviderId(raw.provider) || latest?.provider || null;
    if (provider) ensurePersistedProvider(provider);
    const providers = uniqueStrings([
      ...(Array.isArray(raw.providers) ? raw.providers.map(normalizeProviderId) : []),
      provider,
      ...observations.map(item => item.provider)
    ]);
    for (const id of providers) ensurePersistedProvider(id);

    let state = latest?.state || "disconnected";
    try {
      state = normalizedState(raw.state || state);
    } catch {
      state = latest?.state || "disconnected";
    }

    return {
      ...raw,
      agent_id: agentId,
      provider,
      source_id: normalizeOptionalString(raw.source_id || raw.sourceId) || latest?.source_id || null,
      providers,
      runtime: normalizeOptionalString(raw.runtime) || latest?.runtime || provider,
      session_id: normalizeOptionalString(raw.session_id || raw.sessionId) || latest?.session_id || null,
      conversation_id: normalizeOptionalString(raw.conversation_id || raw.conversationId) || latest?.conversation_id || null,
      role: raw.role ?? null,
      machine: raw.machine ?? null,
      parent_manager_id: raw.parent_manager_id ?? raw.parentManagerId ?? null,
      task_id: raw.task_id ?? raw.taskId ?? null,
      task: raw.task ?? null,
      branch: raw.branch ?? null,
      pr_number: Number.isInteger(Number(raw.pr_number ?? raw.prNumber)) && Number(raw.pr_number ?? raw.prNumber) > 0
        ? Number(raw.pr_number ?? raw.prNumber)
        : null,
      heartbeat_at: validDate(raw.heartbeat_at || raw.heartbeatAt) || latest?.heartbeat_at || null,
      state,
      last_action_at: validDate(raw.last_action_at || raw.lastActionAt) || latest?.last_action_at || latest?.heartbeat_at || null,
      last_action_summary: raw.last_action_summary ?? raw.lastActionSummary ?? null,
      created_at: validDate(raw.created_at || raw.createdAt) || latest?.heartbeat_at || null,
      finished_at: validDate(raw.finished_at || raw.finishedAt),
      correlation_keys: uniqueStrings([
        ...(Array.isArray(raw.correlation_keys) ? raw.correlation_keys : []),
        ...observations.map(item => observationKey(item.provider, item.source_id))
      ]),
      source_metadata: raw.source_metadata && typeof raw.source_metadata === "object" && !Array.isArray(raw.source_metadata)
        ? { ...raw.source_metadata }
        : {},
      observations
    };
  };

  const agentMap = new Map();
  for (const raw of Array.isArray(value.agents) ? value.agents : []) {
    const migrated = migrateAgent(raw);
    if (!migrated) continue;
    const existing = agentMap.get(migrated.agent_id);
    if (!existing) {
      agentMap.set(migrated.agent_id, migrated);
      continue;
    }
    const existingMs = new Date(existing.heartbeat_at || 0).getTime();
    const migratedMs = new Date(migrated.heartbeat_at || 0).getTime();
    const newer = migratedMs >= existingMs ? migrated : existing;
    const older = newer === migrated ? existing : migrated;
    agentMap.set(migrated.agent_id, {
      ...older,
      ...newer,
      providers: uniqueStrings([...(older.providers || []), ...(newer.providers || [])]),
      correlation_keys: uniqueStrings([...(older.correlation_keys || []), ...(newer.correlation_keys || [])]),
      source_metadata: { ...(older.source_metadata || {}), ...(newer.source_metadata || {}) },
      observations: mergeObservations([...(older.observations || []), ...(newer.observations || [])])
    });
  }

  const staleAfterMs = Number.isFinite(Number(value.stale_after_ms))
    ? Math.max(1_000, Number(value.stale_after_ms))
    : base.stale_after_ms;
  const disconnectedAfterMs = Number.isFinite(Number(value.disconnected_after_ms))
    ? Math.max(staleAfterMs, Number(value.disconnected_after_ms))
    : base.disconnected_after_ms;

  return {
    version: FEDERATION_VERSION,
    stale_after_ms: staleAfterMs,
    disconnected_after_ms: disconnectedAfterMs,
    providers: [...providerMap.values()],
    agents: [...agentMap.values()]
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
  const correlation = observation.correlation && typeof observation.correlation === "object" && !Array.isArray(observation.correlation)
    ? observation.correlation
    : {};
  return uniqueStrings([
    ...normalizeSuppliedCorrelationKeys(observation.correlation_keys),
    observationKey(observation.provider, observation.source_id),
    correlation.logical_agent_id ? "logical:" + String(correlation.logical_agent_id).trim() : null,
    correlation.controller_agent_id ? "controller-agent:" + String(correlation.controller_agent_id).trim() : null,
    correlation.chat_session_id ? "chat-session:" + String(correlation.chat_session_id).trim() : null,
    correlation.github_run_id ? "github-run:" + String(correlation.github_run_id).trim() : null,
    correlation.bridge_job_id ? "bridge-job:" + String(correlation.bridge_job_id).trim() : null,
    correlation.work_item_id ? "work-item:" + String(correlation.work_item_id).trim() : null
  ]);
}

function requireProvider(federation, providerId) {
  const id = normalizeProviderId(providerId);
  const provider = (federation?.providers || []).find(item => normalizeProviderId(item.id) === id);
  if (!provider) {
    throw new Error('Unsupported federation provider "' + providerId + '". Register an adapter before ingesting observations.');
  }
  if (provider.status === "unsupported" ||
      (provider.discovery === "unavailable" && provider.registration === "unavailable")) {
    throw new Error('Federation provider "' + id + '" is preserved for compatibility but has no installed adapter.');
  }
  return provider;
}


function upsertProvider(federation, providerId, patch = {}) {
  const provider = requireProvider(federation, providerId);
  const next = { ...patch };
  if (next.status !== undefined && !PROVIDER_HEALTH_STATES.has(String(next.status))) {
    throw new Error('Unsupported provider health status "' + next.status + '".');
  }
  if (next.last_heartbeat_at) {
    const incoming = new Date(next.last_heartbeat_at).getTime();
    const existing = new Date(provider.last_heartbeat_at || 0).getTime();
    if (Number.isFinite(existing) && existing > incoming) delete next.last_heartbeat_at;
  }
  Object.assign(provider, next);
  provider.metadata = patch.metadata && typeof patch.metadata === "object" && !Array.isArray(patch.metadata)
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
  const id = normalizeProviderId(providerId);
  if (!id) throw new Error("provider id is required");
  requireProvider(federation, id);
  if (metadata !== null && metadata !== undefined &&
      (typeof metadata !== "object" || Array.isArray(metadata))) {
    throw new Error("provider heartbeat metadata must be an object when provided.");
  }
  const heartbeatAt = normalizeRequiredDate(at, "provider heartbeat at", iso(Date.now()));
  return upsertProvider(federation, id, {
    status: String(status || "online"),
    last_heartbeat_at: heartbeatAt,
    last_error: error ? String(error) : null,
    ...(metadata && typeof metadata === "object" && !Array.isArray(metadata) ? { metadata } : {})
  });
}

export function normalizeObservation(federation, rawObservation, { now = Date.now() } = {}) {
  if (!rawObservation || typeof rawObservation !== "object" || Array.isArray(rawObservation)) {
    throw new Error("federation observation must be an object");
  }
  const provider = normalizeProviderId(rawObservation.provider);
  const sourceId = String(rawObservation.source_id || rawObservation.sourceId || "").trim();
  if (!provider) throw new Error("observation provider is required");
  if (!sourceId) throw new Error("observation source_id is required");
  requireProvider(federation, provider);

  if (rawObservation.correlation !== undefined &&
      (rawObservation.correlation === null || typeof rawObservation.correlation !== "object" || Array.isArray(rawObservation.correlation))) {
    throw new Error("correlation must be an object when provided.");
  }
  if (rawObservation.correlation_keys !== undefined && !Array.isArray(rawObservation.correlation_keys)) {
    throw new Error("correlation_keys must be an array when provided.");
  }
  const correlation = rawObservation.correlation ? { ...rawObservation.correlation } : {};
  const heartbeatAt = normalizeRequiredDate(
    rawObservation.heartbeat_at ?? rawObservation.heartbeatAt,
    "heartbeat_at",
    iso(now)
  );
  const lastActionAt = normalizeRequiredDate(
    rawObservation.last_action_at ?? rawObservation.lastActionAt,
    "last_action_at",
    heartbeatAt
  );
  const createdAt = normalizeRequiredDate(
    rawObservation.created_at ?? rawObservation.createdAt,
    "created_at",
    heartbeatAt
  );
  const finishedAt = normalizeRequiredDate(
    rawObservation.finished_at ?? rawObservation.finishedAt,
    "finished_at",
    null
  );
  const sessionId = normalizeOptionalString(
    rawObservation.session_id ?? rawObservation.sessionId ?? correlation.session_id ?? correlation.chat_session_id
  );
  const conversationId = normalizeOptionalString(
    rawObservation.conversation_id ?? rawObservation.conversationId ?? correlation.conversation_id
  );

  const normalized = {
    provider,
    source_id: sourceId,
    agent_id: normalizeOptionalString(rawObservation.agent_id ?? rawObservation.agentId),
    runtime: normalizeOptionalString(rawObservation.runtime ?? rawObservation.provider_runtime) || provider,
    session_id: sessionId,
    conversation_id: conversationId,
    role: rawObservation.role ?? null,
    machine: rawObservation.machine ?? null,
    parent_manager_id: rawObservation.parent_manager_id ?? rawObservation.parentManagerId ?? null,
    task_id: rawObservation.task_id ?? rawObservation.taskId ?? null,
    task: rawObservation.task ?? null,
    branch: rawObservation.branch ?? null,
    pr_number: normalizePrNumber(rawObservation.pr_number ?? rawObservation.prNumber),
    heartbeat_at: heartbeatAt,
    state: normalizedState(rawObservation.state),
    last_action_at: lastActionAt,
    last_action_summary: rawObservation.last_action_summary ?? rawObservation.lastActionSummary ?? null,
    created_at: createdAt,
    finished_at: finishedAt,
    correlation,
    correlation_keys: Array.isArray(rawObservation.correlation_keys) ? rawObservation.correlation_keys : [],
    source_metadata: normalizeMetadata(rawObservation.source_metadata)
  };
  normalized.correlation_keys = correlationKeys(normalized).filter(key => key !== observationKey(provider, sourceId));
  return normalized;
}


export function reconcileObservation(federation, rawObservation, { now = Date.now() } = {}) {
  if (!federation || typeof federation !== "object") throw new Error("federation state is required");
  const observation = normalizeObservation(federation, rawObservation, { now });
  const provider = observation.provider;
  const sourceId = observation.source_id;
  const sourceKey = observationKey(provider, sourceId);
  const strongKeys = observation.correlation_keys.filter(isIdentityCorrelationKey);
  const explicitAgentId = observation.agent_id;

  const sameSource = agent =>
    (agent.observations || []).some(item => item.provider === provider && item.source_id === sourceId) ||
    (agent.correlation_keys || []).includes(sourceKey);
  const strongMatch = agent =>
    strongKeys.length > 0 &&
    (agent.correlation_keys || []).some(key => isIdentityCorrelationKey(key) && strongKeys.includes(key));

  let candidates = federation.agents.filter(agent => sameSource(agent) || strongMatch(agent));
  if (explicitAgentId) {
    const explicit = federation.agents.find(item => item.agent_id === explicitAgentId);
    if (explicit) {
      if ((explicit.observations || []).length > 0 && !sameSource(explicit) && !strongMatch(explicit)) {
        throw new Error('Observation agent_id "' + explicitAgentId + '" lacks source or strong-correlation evidence for this observation.');
      }
      candidates = [explicit, ...candidates.filter(item => item !== explicit)];
    } else if (candidates.length) {
      throw new Error('Observation agent_id "' + explicitAgentId + '" conflicts with reconciled agent "' + candidates[0].agent_id + '".');
    }
  }

  const uniqueCandidates = [...new Map(candidates.map(agent => [agent.agent_id, agent])).values()];
  if (uniqueCandidates.length > 1) {
    throw new Error("Observation correlation is ambiguous across multiple logical agents.");
  }

  let agent = uniqueCandidates[0] || null;
  if (!agent) {
    agent = {
      agent_id: explicitAgentId || deterministicAgentId(provider, sourceId),
      provider,
      source_id: sourceId,
      providers: [provider],
      runtime: observation.runtime,
      session_id: observation.session_id,
      conversation_id: observation.conversation_id,
      role: null,
      machine: null,
      parent_manager_id: null,
      task_id: null,
      task: null,
      branch: null,
      pr_number: null,
      heartbeat_at: observation.heartbeat_at,
      state: observation.state,
      last_action_at: observation.last_action_at,
      last_action_summary: null,
      created_at: observation.created_at,
      finished_at: null,
      correlation_keys: [sourceKey, ...strongKeys],
      source_metadata: {},
      observations: []
    };
    federation.agents.push(agent);
  }

  agent.observations = Array.isArray(agent.observations) ? agent.observations : [];
  const existingIndex = agent.observations.findIndex(item => item.provider === provider && item.source_id === sourceId);
  const existingObservation = existingIndex >= 0 ? agent.observations[existingIndex] : null;
  const incomingMs = new Date(observation.heartbeat_at).getTime();
  const existingObservationMs = existingObservation?.heartbeat_at
    ? new Date(existingObservation.heartbeat_at).getTime()
    : Number.NEGATIVE_INFINITY;

  if (existingObservation && incomingMs < existingObservationMs) {
    return agent;
  }

  const observed = {
    provider,
    source_id: sourceId,
    runtime: observation.runtime,
    session_id: observation.session_id,
    conversation_id: observation.conversation_id,
    heartbeat_at: observation.heartbeat_at,
    state: observation.state,
    last_action_at: observation.last_action_at,
    metadata: observation.source_metadata
  };
  if (existingIndex >= 0) agent.observations[existingIndex] = observed;
  else agent.observations.push(observed);

  agent.providers = uniqueStrings([...(agent.providers || []), provider]);
  agent.correlation_keys = uniqueStrings([...(agent.correlation_keys || []), sourceKey, ...strongKeys]);
  agent.created_at = validDate(agent.created_at) && new Date(agent.created_at) <= new Date(observation.created_at)
    ? agent.created_at
    : observation.created_at;
  if (observation.source_metadata && typeof observation.source_metadata === "object") {
    agent.source_metadata = { ...(agent.source_metadata || {}), ...observation.source_metadata };
  }

  const currentAgentMs = agent.heartbeat_at ? new Date(agent.heartbeat_at).getTime() : Number.NEGATIVE_INFINITY;
  if (!Number.isFinite(currentAgentMs) || incomingMs >= currentAgentMs) {
    agent.provider = provider;
    agent.source_id = sourceId;
    agent.runtime = observation.runtime ?? agent.runtime ?? provider;
    agent.session_id = observation.session_id ?? agent.session_id ?? null;
    agent.conversation_id = observation.conversation_id ?? agent.conversation_id ?? null;
    agent.role = observation.role ?? agent.role ?? null;
    agent.machine = observation.machine ?? agent.machine ?? null;
    agent.parent_manager_id = observation.parent_manager_id ?? agent.parent_manager_id ?? null;
    agent.task_id = observation.task_id ?? agent.task_id ?? null;
    agent.task = observation.task ?? agent.task ?? null;
    agent.branch = observation.branch ?? agent.branch ?? null;
    agent.pr_number = observation.pr_number ?? agent.pr_number ?? null;
    agent.heartbeat_at = observation.heartbeat_at;
    agent.state = observation.state;
    agent.last_action_at = observation.last_action_at;
    agent.last_action_summary = observation.last_action_summary ?? agent.last_action_summary ?? null;
    agent.finished_at = ["done", "failed"].includes(observation.state)
      ? (observation.finished_at || observation.heartbeat_at)
      : null;
  }

  upsertProvider(federation, provider, {
    status: "online",
    last_heartbeat_at: observation.heartbeat_at,
    last_error: null
  });

  return agent;
}

export function agentSourceAdapter(federation, providerId, { discover = null } = {}) {
  const provider = requireProvider(federation, providerId);
  const id = normalizeProviderId(provider.id);
  return Object.freeze({
    id,
    kind: provider.kind,
    discovery: provider.discovery,
    registration: provider.registration,
    async discover(...args) {
      if (provider.discovery !== "automated") {
        throw new Error('Provider "' + id + '" does not support automatic discovery.');
      }
      if (typeof discover !== "function") {
        throw new Error('Provider "' + id + '" has no discovery callback configured in this process.');
      }
      const observations = await discover(...args);
      if (!Array.isArray(observations)) throw new Error("Provider discovery must return an array of observations.");
      return observations.map(item => normalizeObservation(federation, { ...item, provider: id }));
    },
    normalize(raw, options) {
      return normalizeObservation(federation, { ...raw, provider: id }, options);
    },
    ingest(raw, options) {
      return reconcileObservation(federation, { ...raw, provider: id }, options);
    },
    reconcile(raw, options) {
      return reconcileObservation(federation, { ...raw, provider: id }, options);
    },
    heartbeat(options) {
      return recordProviderHeartbeat(federation, id, options);
    }
  });
}


function localState(status) {
  const value = String(status || "").toLowerCase();
  if (["reserved", "starting", "running", "stopping"].includes(value)) return "working";
  if (value === "waiting") return "tool_wait";
  if (["blocked", "stale"].includes(value)) return "blocked";
  if (["done", "finished", "stopped"].includes(value)) return "done";
  if (["failed", "capacity-blocked"].includes(value)) return "failed";
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
    const provider = String(managed.executionProvider || managed.runtimeProvider || managed.provider || "local-control").trim().toLowerCase() || "local-control";
    reconcileObservation(federation, {
      provider,
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
        managed_status: managed.status || null,
        managed_recovery_status: managed.recoveryStatus || null,
        pid: managed.pid || null,
        lease_id: managed.leaseId || null,
        boundary: managed.boundary || null,
        lane: managed.lane || null,
        execution_provider: provider
      }
    }, { now });
    changed += 1;
  }

  const localManaged = managedAgents.filter(managed => !managed?.executionProvider || managed.executionProvider === "local-control").length;
  recordProviderHeartbeat(federation, "local-control", {
    status: "online",
    at: now,
    metadata: { hostname, managed_agents: localManaged }
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

export function shouldExposeFederatedAgent(agent, { managedAgents = null } = {}) {
  if (!agent || typeof agent !== "object") return false;

  const metadata = agent.source_metadata && typeof agent.source_metadata === "object"
    ? agent.source_metadata
    : {};
  const recoveryStatus = String(
    agent.recovery_status
    || agent.recoveryStatus
    || metadata.managed_recovery_status
    || ""
  ).trim().toLowerCase();

  if (TERMINAL_RECOVERY_STATUSES.has(recoveryStatus)) return false;
  if (agent.effective_state === "failed" || agent.state === "failed") return false;
  if (agent.freshness === "disconnected" || agent.effective_state === "disconnected") return false;

  const managedId = String(metadata.managed_agent_id || "").trim();
  if (!managedId) return true;

  const currentManaged = Array.isArray(managedAgents)
    ? managedAgents.find(item => String(item?.id || "") === managedId)
    : null;
  if (Array.isArray(managedAgents) && !currentManaged) return false;

  const managedStatus = String(currentManaged?.status || metadata.managed_status || "").trim().toLowerCase();
  const managedRecoveryStatus = String(
    currentManaged?.recoveryStatus
    || metadata.managed_recovery_status
    || ""
  ).trim().toLowerCase();

  if (TERMINAL_RECOVERY_STATUSES.has(managedRecoveryStatus)) return false;
  return !RETIRED_MANAGED_AGENT_STATUSES.has(managedStatus);
}

export function federationSnapshot(federation, { now = Date.now(), managedAgents = null } = {}) {
  const staleAfterMs = Number(federation?.stale_after_ms) || DEFAULT_STALE_AFTER_MS;
  const disconnectedAfterMs = Number(federation?.disconnected_after_ms) || DEFAULT_DISCONNECTED_AFTER_MS;
  const materializedAgents = (federation?.agents || []).map(agent => materializeAgent(agent, {
    now,
    staleAfterMs,
    disconnectedAfterMs
  }));
  const agents = materializedAgents.filter(agent => shouldExposeFederatedAgent(agent, { managedAgents }));

  const counts = {
    live: agents.filter(agent => agent.live).length,
    active: agents.filter(agent => agent.live && ["working", "tool_wait", "blocked"].includes(agent.effective_state)).length,
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
