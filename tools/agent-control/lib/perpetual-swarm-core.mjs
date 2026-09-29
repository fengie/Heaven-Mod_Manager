const ACTIVE_STATUSES = new Set(["reserved", "starting", "running", "waiting", "blocked", "stale", "stopping"]);
const TERMINAL_STATUSES = new Set(["done", "failed", "finished", "stopped", "interrupted", "orphaned", "capacity-blocked"]);

export function defaultPerpetualSwarmState() {
  return {
    enabled: false,
    paused: false,
    objective: "",
    workflowId: "usual-swarm",
    baseBranch: "main",
    machine: "auto",
    generation: 0,
    replacementCount: 0,
    startedAt: null,
    updatedAt: null,
    lastActionAt: null,
    lastAction: null,
    lastReason: null,
    lastError: null,
    activeWaveId: null,
    lastWaveStartedAt: null,
    lastWaveFinishedAt: null,
    lastReplacementAt: null,
    nextActionAt: null,
    waveStarts: [],
    cooldownLevel: 0,
    staleAfterMs: 15 * 60_000,
    blockedAfterMs: 30 * 60_000,
    minWaveGapMs: 15_000,
    restartWindowMs: 10 * 60_000,
    maxWaveStartsPerWindow: 6,
    restartCooldownMs: 60_000,
    maxCooldownMs: 15 * 60_000
  };
}

function finiteBound(value, fallback, min, max) {
  const number = Number(value);
  if (!Number.isFinite(number)) return fallback;
  return Math.max(min, Math.min(max, Math.floor(number)));
}

function normalizeWaveStarts(value) {
  if (!Array.isArray(value)) return [];
  return value
    .map(item => typeof item === "string" ? item : item?.at)
    .map(item => new Date(item))
    .filter(item => Number.isFinite(item.getTime()))
    .map(item => item.toISOString())
    .slice(-100);
}

export function normalizePerpetualSwarmState(value = {}) {
  const base = defaultPerpetualSwarmState();
  const source = value && typeof value === "object" ? value : {};
  return {
    ...base,
    ...source,
    enabled: Boolean(source.enabled),
    paused: Boolean(source.paused),
    objective: String(source.objective || ""),
    workflowId: String(source.workflowId || base.workflowId),
    baseBranch: String(source.baseBranch || base.baseBranch),
    machine: String(source.machine || base.machine),
    generation: Math.max(0, Math.floor(Number(source.generation) || 0)),
    replacementCount: Math.max(0, Math.floor(Number(source.replacementCount) || 0)),
    waveStarts: normalizeWaveStarts(source.waveStarts),
    cooldownLevel: finiteBound(source.cooldownLevel, base.cooldownLevel, 0, 12),
    staleAfterMs: finiteBound(source.staleAfterMs, base.staleAfterMs, 60_000, 24 * 60 * 60_000),
    blockedAfterMs: finiteBound(source.blockedAfterMs, base.blockedAfterMs, 60_000, 48 * 60 * 60_000),
    minWaveGapMs: finiteBound(source.minWaveGapMs, base.minWaveGapMs, 1_000, 60 * 60_000),
    restartWindowMs: finiteBound(source.restartWindowMs, base.restartWindowMs, 60_000, 24 * 60 * 60_000),
    maxWaveStartsPerWindow: finiteBound(source.maxWaveStartsPerWindow, base.maxWaveStartsPerWindow, 1, 100),
    restartCooldownMs: finiteBound(source.restartCooldownMs, base.restartCooldownMs, 60_000, 24 * 60 * 60_000),
    maxCooldownMs: finiteBound(source.maxCooldownMs, base.maxCooldownMs, 60_000, 7 * 24 * 60 * 60_000)
  };
}

function timestamp(value) {
  const parsed = Date.parse(String(value || ""));
  return Number.isFinite(parsed) ? parsed : null;
}

function activeAgents(state) {
  return (Array.isArray(state?.agents) ? state.agents : []).filter(agent =>
    ACTIVE_STATUSES.has(String(agent?.status || "").toLowerCase())
  );
}

function progressAge(agent, now) {
  const at = timestamp(agent?.lastProgressAt || agent?.updatedAt || agent?.startedAt || agent?.createdAt);
  return at === null ? Number.POSITIVE_INFINITY : Math.max(0, now - at);
}

function stuckCandidate(agents, perpetual, now) {
  const ranked = agents
    .filter(agent => !agent?.stopRequestedAt)
    .filter(agent => String(agent?.status || "").toLowerCase() !== "stopping")
    .map(agent => {
      const status = String(agent?.status || "").toLowerCase();
      const ageMs = progressAge(agent, now);
      const threshold = ["waiting", "blocked"].includes(status)
        ? perpetual.blockedAfterMs
        : perpetual.staleAfterMs;
      const stuck = status === "stale" || ageMs >= threshold;
      return { agent, status, ageMs, threshold, stuck };
    })
    .filter(item => item.stuck)
    .sort((a, b) => b.ageMs - a.ageMs);
  return ranked[0] || null;
}

function safetyHoldReason(controlState) {
  if (controlState?.health?.mode === "degraded") return "state-degraded";
  if (controlState?.settings?.emergencyStop) return "emergency-stop";
  if (controlState?.settings?.readOnly) return "read-only";
  if (controlState?.settings?.draining) return "draining";
  if (controlState?.settings?.dispatchPaused) return "dispatch-paused";
  return null;
}

export function perpetualSwarmDecision(controlState, {
  now = Date.now(),
  providerCapacity = null
} = {}) {
  const perpetual = normalizePerpetualSwarmState(controlState?.perpetualSwarm);
  if (!perpetual.enabled) return { kind: "idle", reason: "disabled" };
  if (perpetual.paused) return { kind: "idle", reason: "paused" };

  const safetyReason = safetyHoldReason(controlState);
  if (safetyReason) return { kind: "wait", reason: safetyReason };

  const nextActionMs = timestamp(perpetual.nextActionAt);
  if (nextActionMs !== null && nextActionMs > now) {
    return { kind: "wait", reason: "cooldown", nextActionAt: new Date(nextActionMs).toISOString() };
  }

  if (providerCapacity?.blocked) {
    return {
      kind: "wait",
      reason: "provider-capacity",
      nextActionAt: providerCapacity.blockedUntil || null,
      sourceAgentId: providerCapacity.sourceAgentId || null
    };
  }

  const active = activeAgents(controlState);
  const stuck = stuckCandidate(active, perpetual, now);
  if (stuck) {
    return {
      kind: "replace-stuck",
      reason: stuck.status === "stale" ? "agent-marked-stale" : "progress-timeout",
      agentId: stuck.agent.id,
      progressAgeMs: Number.isFinite(stuck.ageMs) ? stuck.ageMs : null,
      thresholdMs: stuck.threshold
    };
  }

  if (active.length) {
    return { kind: "wait", reason: "active-workers", activeAgentIds: active.map(agent => agent.id) };
  }

  const tail = controlState?.settings?.swarmTailRecovery;
  if (tail?.enabled !== false && tail?.armedAt) {
    return { kind: "wait", reason: "tail-recovery-active", waveId: tail.waveId || null };
  }

  const lastWaveMs = timestamp(perpetual.lastWaveStartedAt);
  if (lastWaveMs !== null && now - lastWaveMs < perpetual.minWaveGapMs) {
    return {
      kind: "wait",
      reason: "minimum-wave-gap",
      nextActionAt: new Date(lastWaveMs + perpetual.minWaveGapMs).toISOString()
    };
  }

  const recentStarts = perpetual.waveStarts
    .map(timestamp)
    .filter(at => at !== null && now - at <= perpetual.restartWindowMs);

  if (recentStarts.length >= perpetual.maxWaveStartsPerWindow) {
    const factor = Math.pow(2, Math.min(8, perpetual.cooldownLevel));
    const delayMs = Math.min(perpetual.maxCooldownMs, perpetual.restartCooldownMs * factor);
    return {
      kind: "cooldown",
      reason: "restart-intensity",
      delayMs,
      nextActionAt: new Date(now + delayMs).toISOString(),
      recentStarts: recentStarts.length
    };
  }

  return {
    kind: "launch-wave",
    reason: "no-active-work",
    generation: perpetual.generation + 1,
    workflowId: perpetual.workflowId || "usual-swarm"
  };
}

export function recordPerpetualWaveStart(value, {
  at = new Date().toISOString(),
  waveId = null
} = {}) {
  const current = normalizePerpetualSwarmState(value);
  return normalizePerpetualSwarmState({
    ...current,
    generation: current.generation + 1,
    activeWaveId: waveId,
    lastWaveStartedAt: at,
    lastActionAt: at,
    lastAction: "launch-wave",
    lastReason: "no-active-work",
    lastError: null,
    nextActionAt: null,
    waveStarts: [...current.waveStarts, at],
    cooldownLevel: 0,
    updatedAt: at
  });
}

export function recordPerpetualCooldown(value, {
  at = new Date().toISOString(),
  nextActionAt,
  reason = "restart-intensity"
} = {}) {
  const current = normalizePerpetualSwarmState(value);
  return normalizePerpetualSwarmState({
    ...current,
    cooldownLevel: Math.min(12, current.cooldownLevel + 1),
    lastActionAt: at,
    lastAction: "cooldown",
    lastReason: reason,
    nextActionAt: nextActionAt || current.nextActionAt,
    updatedAt: at
  });
}

export function isPerpetualTerminalStatus(status) {
  return TERMINAL_STATUSES.has(String(status || "").toLowerCase());
}
