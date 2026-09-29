import { createHash } from "node:crypto";

export const NORMALIZED_AGENT_STATES = Object.freeze([
  "working",
  "tool_wait",
  "blocked",
  "idle",
  "done",
  "failed",
  "disconnected"
]);

export const LIVE_AGENT_STATES = new Set(["working", "tool_wait", "blocked", "idle"]);
export const TERMINAL_AGENT_STATES = new Set(["done", "failed"]);
export const SOURCE_MODES = new Set(["automated", "manual-bridge", "unavailable"]);

function text(value) {
  const normalized = String(value ?? "").trim();
  return normalized || null;
}

function time(value) {
  const parsed = Date.parse(String(value ?? ""));
  return Number.isFinite(parsed) ? new Date(parsed).toISOString() : null;
}

function latestIso(...values) {
  return values
    .map(time)
    .filter(Boolean)
    .sort()
    .at(-1) || null;
}

function firstIso(...values) {
  return values
    .map(time)
    .filter(Boolean)
    .sort()
    .at(0) || null;
}

function stableDigest(value) {
  return createHash("sha256").update(String(value)).digest("hex").slice(0, 20);
}

function normalizedState(value) {
  const raw = String(value ?? "").trim().toLowerCase().replaceAll("-", "_");
  const aliases = {
    running: "working",
    active: "working",
    waiting: "tool_wait",
    waiting_tool: "tool_wait",
    toolwait: "tool_wait",
    complete: "done",
    completed: "done",
    finished: "done",
    error: "failed",
    stale: "disconnected",
    offline: "disconnected"
  };
  const state = aliases[raw] || raw;
  return NORMALIZED_AGENT_STATES.includes(state) ? state : "idle";
}

function sourceIdentity(observation) {
  return text(
    observation.sourceId ??
    observation.source_id ??
    observation.providerAgentId ??
    observation.provider_agent_id ??
    observation.chatSessionId ??
    observation.chat_session_id ??
    observation.conversationId ??
    observation.conversation_id ??
    observation.processId ??
    observation.process_id ??
    observation.githubRunId ??
    observation.github_run_id
  );
}

function correlationIdentity(observation) {
  return text(
    observation.correlationId ??
    observation.correlation_id ??
    observation.logicalCorrelationId ??
    observation.logical_correlation_id
  );
}

export function normalizeAgentObservation(observation = {}, {
  now = new Date().toISOString()
} = {}) {
  const provider = text(observation.provider ?? observation.source ?? observation.runtime) || "unknown";
  const runtime = text(observation.runtime) || provider;
  const sourceId = sourceIdentity(observation);
  const correlationId = correlationIdentity(observation);
  const explicitAgentId = text(observation.agentId ?? observation.agent_id);
  const identitySeed = explicitAgentId
    ? `explicit:${explicitAgentId}`
    : correlationId
      ? `correlation:${correlationId}`
      : sourceId
        ? `source:${provider}:${sourceId}`
        : null;

  if (!identitySeed) {
    throw new Error("Agent observation requires agentId, correlationId, or stable source identity.");
  }

  const agentId = explicitAgentId || `agent-${stableDigest(identitySeed)}`;
  const heartbeatAt = time(observation.heartbeatAt ?? observation.heartbeat_at) || time(now);
  const createdAt = time(observation.createdAt ?? observation.created_at) || heartbeatAt;
  const finishedAt = time(observation.finishedAt ?? observation.finished_at);
  const lastActionAt = time(observation.lastActionAt ?? observation.last_action_at) || heartbeatAt;
  const state = normalizedState(observation.state ?? observation.status);

  return {
    agentId,
    provider,
    runtime,
    sourceId,
    correlationId,
    chatSessionId: text(observation.chatSessionId ?? observation.chat_session_id ?? observation.conversationId ?? observation.conversation_id),
    role: text(observation.role) || "unknown",
    machine: text(observation.machine) || "unknown",
    parentManagerId: text(observation.parentManagerId ?? observation.parent_manager_id),
    taskId: text(observation.taskId ?? observation.task_id),
    task: text(observation.task ?? observation.objective),
    branch: text(observation.branch ?? observation.branchName ?? observation.branch_name),
    prNumber: Number.isInteger(Number(observation.prNumber ?? observation.pr_number))
      ? Number(observation.prNumber ?? observation.pr_number)
      : null,
    heartbeatAt,
    state,
    heartbeatStatus: "fresh",
    lastActionAt,
    lastActionSummary: text(observation.lastActionSummary ?? observation.last_action_summary ?? observation.lastMessage),
    createdAt,
    finishedAt: TERMINAL_AGENT_STATES.has(state) ? (finishedAt || heartbeatAt) : finishedAt,
    displayName: text(observation.displayName ?? observation.display_name ?? observation.title),
    leaseId: text(observation.leaseId ?? observation.lease_id),
    capacity: Number.isFinite(Number(observation.capacity)) ? Number(observation.capacity) : null,
    opaqueIdentity: observation.opaqueIdentity ?? observation.opaque_identity ?? null,
    sourceObservations: [{
      provider,
      sourceId,
      observedAt: heartbeatAt,
      opaqueIdentity: observation.opaqueIdentity ?? observation.opaque_identity ?? null
    }]
  };
}

function sourceKey(agent) {
  return agent?.provider && agent?.sourceId ? `${agent.provider}:${agent.sourceId}` : null;
}

function pickMatch(registry, observation) {
  const explicit = registry.find(agent => agent.agentId === observation.agentId);
  if (explicit) return explicit;

  if (observation.correlationId) {
    const correlated = registry.find(agent => agent.correlationId && agent.correlationId === observation.correlationId);
    if (correlated) return correlated;
  }

  const wanted = sourceKey(observation);
  if (wanted) {
    const bySource = registry.find(agent => {
      if (sourceKey(agent) === wanted) return true;
      return Array.isArray(agent.sourceObservations) && agent.sourceObservations.some(source => (
        source?.provider && source?.sourceId && `${source.provider}:${source.sourceId}` === wanted
      ));
    });
    if (bySource) return bySource;
  }

  return null;
}

function mergeObservation(existing, incoming) {
  const sources = new Map();
  for (const source of [...(existing.sourceObservations || []), ...(incoming.sourceObservations || [])]) {
    if (!source?.provider || !source?.sourceId) continue;
    const key = `${source.provider}:${source.sourceId}`;
    const prior = sources.get(key);
    if (!prior || Date.parse(source.observedAt || 0) >= Date.parse(prior.observedAt || 0)) {
      sources.set(key, source);
    }
  }

  const incomingIsNewer = Date.parse(incoming.heartbeatAt || 0) >= Date.parse(existing.heartbeatAt || 0);
  const newest = incomingIsNewer ? incoming : existing;
  const oldest = incomingIsNewer ? existing : incoming;

  return {
    ...oldest,
    ...newest,
    agentId: existing.agentId || incoming.agentId,
    correlationId: existing.correlationId || incoming.correlationId,
    createdAt: firstIso(existing.createdAt, incoming.createdAt),
    heartbeatAt: latestIso(existing.heartbeatAt, incoming.heartbeatAt),
    lastActionAt: latestIso(existing.lastActionAt, incoming.lastActionAt),
    finishedAt: newest.state && TERMINAL_AGENT_STATES.has(newest.state)
      ? (newest.finishedAt || newest.heartbeatAt)
      : null,
    sourceObservations: [...sources.values()]
  };
}

export function reconcileAgentRegistry(registry = [], observations = [], options = {}) {
  const next = Array.isArray(registry) ? registry.map(agent => structuredClone(agent)) : [];

  for (const raw of Array.isArray(observations) ? observations : []) {
    const normalized = normalizeAgentObservation(raw, options);
    const match = pickMatch(next, normalized);
    if (!match) {
      next.push(normalized);
      continue;
    }

    const index = next.indexOf(match);
    next[index] = mergeObservation(match, normalized);
  }

  return next;
}

export function applyHeartbeatFreshness(registry = [], {
  now = Date.now(),
  staleAfterMs = 60_000,
  disconnectAfterMs = 180_000
} = {}) {
  const timestamp = Number(now);

  return registry.map(agent => {
    const copy = structuredClone(agent);
    if (TERMINAL_AGENT_STATES.has(copy.state)) {
      copy.heartbeatStatus = "terminal";
      return copy;
    }

    const heartbeat = Date.parse(copy.heartbeatAt || "");
    if (!Number.isFinite(heartbeat)) {
      copy.state = "disconnected";
      copy.heartbeatStatus = "disconnected";
      return copy;
    }

    const age = Math.max(0, timestamp - heartbeat);
    if (age > disconnectAfterMs) {
      copy.state = "disconnected";
      copy.heartbeatStatus = "disconnected";
    } else if (age > staleAfterMs) {
      copy.heartbeatStatus = "stale";
    } else {
      copy.heartbeatStatus = "fresh";
    }
    copy.heartbeatAgeMs = age;
    return copy;
  });
}

export function isLiveNormalizedAgent(agent) {
  return LIVE_AGENT_STATES.has(String(agent?.state || "")) && agent?.heartbeatStatus === "fresh";
}

export function normalizedAgentCounts(registry = []) {
  const counts = {
    totalLive: 0,
    working: 0,
    toolWait: 0,
    blocked: 0,
    idle: 0,
    stale: 0,
    disconnected: 0,
    done: 0,
    failed: 0
  };

  for (const agent of registry) {
    if (isLiveNormalizedAgent(agent)) counts.totalLive += 1;
    if (agent?.heartbeatStatus === "stale") counts.stale += 1;
    if (agent?.state === "working" && agent?.heartbeatStatus === "fresh") counts.working += 1;
    if (agent?.state === "tool_wait" && agent?.heartbeatStatus === "fresh") counts.toolWait += 1;
    if (agent?.state === "blocked" && agent?.heartbeatStatus === "fresh") counts.blocked += 1;
    if (agent?.state === "idle" && agent?.heartbeatStatus === "fresh") counts.idle += 1;
    if (agent?.state === "disconnected") counts.disconnected += 1;
    if (agent?.state === "done") counts.done += 1;
    if (agent?.state === "failed") counts.failed += 1;
  }

  return counts;
}

export function createAgentSource({
  id,
  mode,
  discover = null,
  normalize = normalizeAgentObservation,
  heartbeat = null,
  reconcile = null,
  availability = null
}) {
  const sourceId = text(id);
  if (!sourceId) throw new Error("Agent source requires an id.");
  if (!SOURCE_MODES.has(mode)) throw new Error(`Unknown agent source mode: ${mode}`);
  if (mode === "automated" && typeof discover !== "function") {
    throw new Error("Automated agent source requires discover().");
  }

  return Object.freeze({
    id: sourceId,
    mode,
    discover,
    normalize,
    heartbeat,
    reconcile,
    availability
  });
}

export async function collectAgentSource(source, context = {}) {
  if (!source || !SOURCE_MODES.has(source.mode)) {
    throw new Error("Invalid agent source.");
  }

  if (source.mode === "unavailable") {
    return {
      source: source.id,
      mode: source.mode,
      available: false,
      observations: [],
      reason: typeof source.availability === "function"
        ? await source.availability(context)
        : "provider-unavailable"
    };
  }

  const raw = source.mode === "automated"
    ? await source.discover(context)
    : (Array.isArray(context.observations) ? context.observations : []);

  const observations = [];
  for (const item of Array.isArray(raw) ? raw : []) {
    observations.push(source.normalize(item, context));
  }

  return {
    source: source.id,
    mode: source.mode,
    available: true,
    observations,
    reason: null
  };
}

export function providerHealth(sources = [], collections = []) {
  const byId = new Map(collections.map(item => [item.source, item]));
  return sources.map(source => {
    const observed = byId.get(source.id);
    return {
      source: source.id,
      mode: source.mode,
      available: observed ? observed.available : source.mode !== "unavailable",
      observationCount: observed?.observations?.length || 0,
      reason: observed?.reason || null
    };
  });
}
