export const MANAGED_LIVE_STATUSES = new Set([
  "reserved",
  "starting",
  "running",
  "waiting",
  "blocked",
  "stopping",
  "idle"
]);

export const MANAGED_TERMINAL_STATUSES = new Set([
  "done",
  "failed",
  "finished",
  "stopped",
  "interrupted",
  "orphaned"
]);

const NORMALIZED_LIVE_STATES = new Set(["working", "tool_wait", "blocked", "idle"]);

function normalizeHost(value) {
  return String(value || "").trim().toLowerCase();
}

function validTimestamp(value) {
  if (!value) return null;
  const timestamp = Date.parse(value);
  return Number.isFinite(timestamp) ? timestamp : null;
}

function managedEffectiveState(status) {
  const value = String(status || "").trim().toLowerCase();
  if (["reserved", "starting", "running", "stopping"].includes(value)) return "working";
  if (value === "waiting") return "tool_wait";
  if (["blocked", "stale"].includes(value)) return "blocked";
  if (value === "idle") return "idle";
  if (["done", "finished", "stopped"].includes(value)) return "done";
  if (value === "failed") return "failed";
  if (["interrupted", "orphaned"].includes(value)) return "disconnected";
  return "idle";
}

export function livenessThresholds(state) {
  const staleAfterMs = Math.max(1_000, Number(state?.federation?.stale_after_ms) || 120_000);
  const disconnectedAfterMs = Math.max(
    staleAfterMs,
    Number(state?.federation?.disconnected_after_ms) || 300_000
  );
  return { staleAfterMs, disconnectedAfterMs };
}

export function managedAgentLiveness(agent, {
  now = Date.now(),
  staleAfterMs = 120_000,
  disconnectedAfterMs = 300_000
} = {}) {
  const status = String(agent?.status || "").trim().toLowerCase();
  const terminal = MANAGED_TERMINAL_STATUSES.has(status);
  let effectiveState = managedEffectiveState(status);
  let freshness = terminal ? "historical" : "fresh";

  const heartbeatMs = validTimestamp(
    agent?.heartbeatAt || agent?.updatedAt || agent?.startedAt || agent?.createdAt
  );
  const ageMs = heartbeatMs === null ? null : Math.max(0, Number(now) - heartbeatMs);

  if (!terminal) {
    if (["interrupted", "orphaned"].includes(status)) {
      freshness = "disconnected";
      effectiveState = "disconnected";
    } else if (status === "stale") {
      freshness = ageMs !== null && ageMs > disconnectedAfterMs ? "disconnected" : "stale";
      if (freshness === "disconnected") effectiveState = "disconnected";
    } else if (ageMs !== null && ageMs > disconnectedAfterMs) {
      freshness = "disconnected";
      effectiveState = "disconnected";
    } else if (ageMs !== null && ageMs > staleAfterMs) {
      freshness = "stale";
    }
  }

  return {
    status,
    effectiveState,
    freshness,
    heartbeatAgeMs: ageMs,
    historical: terminal,
    live: freshness === "fresh" && MANAGED_LIVE_STATUSES.has(status) && NORMALIZED_LIVE_STATES.has(effectiveState)
  };
}

export function countLiveManagedAgents(state, { now = Date.now() } = {}) {
  const thresholds = livenessThresholds(state);
  return (state?.agents || []).filter(agent =>
    managedAgentLiveness(agent, { now, ...thresholds }).live
  ).length;
}

export function deploymentCapacity(state, requestedCount, maxActiveAgents, { now = Date.now() } = {}) {
  const count = Math.max(1, Math.floor(Number(requestedCount) || 1));
  const maximum = Math.max(0, Math.floor(Number(maxActiveAgents) || 0));
  const active = countLiveManagedAgents(state, { now });
  const available = Math.max(0, maximum - active);
  return {
    count,
    active,
    maximum,
    available,
    allowed: count <= available
  };
}

export function resolveWorkerTarget(state, machine = "auto", {
  controllerHostname,
  preferHeavy = true
} = {}) {
  const controller = normalizeHost(controllerHostname);
  if (!controller) throw new Error("controller hostname is required");

  const requested = normalizeHost(machine || "auto");
  let target;
  if (!requested || requested === "auto") {
    const heavenPolicy = state?.settings?.machinePolicies?.heaven;
    target = controller === "heaven2" && preferHeavy && heavenPolicy?.preferredForHeavyWork
      ? "heaven"
      : controller;
  } else {
    target = requested === "local" ? controller : requested;
  }

  return {
    requested: requested || "auto",
    controller,
    target,
    policy: state?.settings?.machinePolicies?.[target] || null,
    remote: target !== controller
  };
}

export function placementTransportDecision(state, machine = "auto", {
  controllerHostname,
  preferHeavy = true,
  heavenTransportHealthy = false,
  heavenTransportReason = null
} = {}) {
  const target = resolveWorkerTarget(state, machine, { controllerHostname, preferHeavy });
  if (!target.policy) {
    return {
      ...target,
      allowed: false,
      code: "machine-policy-missing",
      reason: `Machine ${target.target} is not registered in policy.`,
      provider: null
    };
  }

  if (!target.remote) {
    return {
      ...target,
      allowed: true,
      code: null,
      reason: null,
      provider: "local-control"
    };
  }

  if (target.controller === "heaven2" && target.target === "heaven") {
    if (!heavenTransportHealthy) {
      return {
        ...target,
        allowed: false,
        code: "transport-unavailable",
        reason: `Authenticated Heaven Local Bridge is unavailable (${heavenTransportReason || "unknown"}); refusing to silently execute heavy work on heaven2.`,
        provider: "heaven-bridge"
      };
    }
    return {
      ...target,
      allowed: true,
      code: null,
      reason: null,
      provider: "heaven-bridge"
    };
  }

  return {
    ...target,
    allowed: false,
    code: "transport-unreachable",
    reason: `Worker "${target.target}" is not reachable through an authenticated configured provider from controller "${target.controller}".`,
    provider: null
  };
}
