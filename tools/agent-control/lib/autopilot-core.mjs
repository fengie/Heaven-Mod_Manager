export const AUTOPILOT_PHASES = Object.freeze([
  "waiting-for-direction",
  "sync-plan",
  "implement",
  "verify",
  "review",
  "repair",
  "reverify",
  "integration-ready",
  "integrate",
  "hygiene",
  "expand",
  "cycle-checkpoint",
  "continuity",
  "safety-gate"
]);

const PASS_RESULTS = new Set(["pass", "passed", "success", "successful", "ok", "green"]);
const FAIL_RESULTS = new Set(["fail", "failed", "failure", "error", "red"]);

export function defaultAutopilotState() {
  return {
    enabled: false,
    paused: false,
    objective: "",
    overallGoal: "",
    phase: "waiting-for-direction",
    iteration: 0,
    repairLoops: 0,
    maxRepairLoops: 3,
    maxIterations: 40,
    perpetual: false,
    cycleNumber: 0,
    maxCycles: 0,
    phaseRetries: 0,
    maxPhaseRetries: 2,
    runId: null,
    baseBranch: "main",
    implementationAgentId: null,
    candidateAgentId: null,
    verificationAgentId: null,
    reviewAgentId: null,
    repairAgentId: null,
    integrationAgentId: null,
    hygieneAgentId: null,
    expansionAgentId: null,
    pendingReplacement: null,
    replacementCount: 0,
    recoveryHistory: [],
    recoveryCooldownLevel: 0,
    nextRetryAt: null,
    lastRecoveryAt: null,
    lastRecoveryReason: null,
    startedAt: null,
    updatedAt: null,
    lastTransitionAt: null,
    stopReason: null,
    lastError: null,
    lastCanonicalMainSha: null,
    lastTruthAt: null
  };
}

export function normalizeAutopilotState(value = {}) {
  const base = defaultAutopilotState();
  const phase = AUTOPILOT_PHASES.includes(value?.phase) ? value.phase : base.phase;
  return {
    ...base,
    ...(value && typeof value === "object" ? value : {}),
    phase,
    objective: String(value?.objective || ""),
    overallGoal: String(value?.overallGoal || "").trim(),
    iteration: Math.max(0, Number(value?.iteration || 0) || 0),
    repairLoops: Math.max(0, Number(value?.repairLoops || 0) || 0),
    maxRepairLoops: Number.isFinite(Number(value?.maxRepairLoops))
      ? Math.max(0, Math.min(20, Number(value.maxRepairLoops)))
      : base.maxRepairLoops,
    maxIterations: Number.isFinite(Number(value?.maxIterations))
      ? Math.max(1, Math.min(500, Number(value.maxIterations)))
      : base.maxIterations,
    perpetual: Boolean(value?.perpetual),
    cycleNumber: Math.max(0, Number(value?.cycleNumber || 0) || 0),
    maxCycles: Number.isFinite(Number(value?.maxCycles))
      ? Math.max(0, Math.min(100000, Number(value.maxCycles)))
      : base.maxCycles,
    phaseRetries: Math.max(0, Number(value?.phaseRetries || 0) || 0),
    maxPhaseRetries: Number.isFinite(Number(value?.maxPhaseRetries))
      ? Math.max(0, Math.min(20, Number(value.maxPhaseRetries)))
      : base.maxPhaseRetries,
    pendingReplacement: value?.pendingReplacement && typeof value.pendingReplacement === "object"
      ? { ...value.pendingReplacement }
      : null,
    replacementCount: Math.max(0, Math.floor(Number(value?.replacementCount || 0) || 0)),
    recoveryHistory: Array.isArray(value?.recoveryHistory)
      ? value.recoveryHistory.filter(item => typeof item === "string" && Number.isFinite(Date.parse(item))).slice(-100)
      : [],
    recoveryCooldownLevel: Math.max(0, Math.min(12, Math.floor(Number(value?.recoveryCooldownLevel || 0) || 0))),
    nextRetryAt: value?.nextRetryAt && Number.isFinite(Date.parse(value.nextRetryAt)) ? value.nextRetryAt : null,
    lastRecoveryAt: value?.lastRecoveryAt && Number.isFinite(Date.parse(value.lastRecoveryAt)) ? value.lastRecoveryAt : null,
    lastRecoveryReason: value?.lastRecoveryReason ? String(value.lastRecoveryReason) : null,
    enabled: Boolean(value?.enabled),
    paused: Boolean(value?.paused)
  };
}

export function transitionAutopilot(current, phase, {
  reason = null,
  at = new Date().toISOString(),
  patch = {}
} = {}) {
  if (!AUTOPILOT_PHASES.includes(phase)) throw new Error(`Unknown autopilot phase: ${phase}`);
  const next = normalizeAutopilotState(current);
  return {
    ...next,
    ...patch,
    phase,
    iteration: next.iteration + (phase === next.phase ? 0 : 1),
    updatedAt: at,
    lastTransitionAt: at,
    stopReason: phase === "safety-gate" || phase === "waiting-for-direction" ? reason : null
  };
}

function agentById(state, id) {
  return id ? state.agents?.find(agent => agent.id === id) || null : null;
}

function taskById(state, id) {
  return id ? state.tasks?.find(task => task.id === id) || null : null;
}

function isActive(agent) {
  return ["reserved", "starting", "running", "waiting", "blocked", "stale", "stopping"].includes(String(agent?.status || ""));
}

function isAuthoritativeDone(agent) {
  return agent?.status === "done" && agent?.exitCode === 0 && agent?.completionEvidence === "authoritative-exit";
}

function evidenceResult(task) {
  const evidence = Array.isArray(task?.evidence) ? task.evidence : [];
  const relevant = evidence.filter(item => ["verification", "test", "tests"].includes(String(item?.type || "").toLowerCase()));
  for (const item of relevant.toReversed()) {
    const result = String(item?.result || "").trim().toLowerCase();
    if (PASS_RESULTS.has(result)) return "pass";
    if (FAIL_RESULTS.has(result)) return "fail";
  }
  return "missing";
}

function repairDecision(autopilot, reason) {
  if (autopilot.repairLoops >= autopilot.maxRepairLoops) {
    return { kind: "gate", reason: `repair-budget-exhausted:${reason}` };
  }
  return { kind: "transition", phase: "repair", reason };
}

function phaseRetryDecision(autopilot, phase, idField, reason) {
  if (autopilot.phaseRetries >= autopilot.maxPhaseRetries) {
    return { kind: "gate", reason: `phase-retry-budget-exhausted:${phase}:${reason}` };
  }
  return {
    kind: "transition",
    phase,
    reason,
    patch: {
      [idField]: null,
      phaseRetries: autopilot.phaseRetries + 1
    }
  };
}

function terminalFailureReason(agent, prefix) {
  if (!agent) return `${prefix}-missing`;
  return `${prefix}-${String(agent.status || "unknown")}`;
}

function requiresManualReconciliation(agent) {
  return ["orphaned", "interrupted", "stopped"].includes(String(agent?.status || ""));
}

export function decideAutopilotAction(controlState, {
  routingCurrent = false,
  capacityAvailable = true,
  integrationVerified = false
} = {}) {
  const autopilot = normalizeAutopilotState(controlState?.autopilot);
  if (!autopilot.enabled) return { kind: "idle", reason: "disabled" };
  if (autopilot.paused) return { kind: "idle", reason: "paused" };
  if (!autopilot.objective.trim()) return { kind: "gate", reason: "objective-missing" };
  if (controlState?.health?.mode === "degraded") return { kind: "gate", reason: "state-degraded" };
  if (controlState?.settings?.readOnly) return { kind: "gate", reason: "read-only" };
  if (controlState?.settings?.emergencyStop) return { kind: "gate", reason: "emergency-stop" };
  if (controlState?.settings?.dispatchPaused || controlState?.settings?.draining) return { kind: "gate", reason: "dispatch-paused" };
  if (String(controlState?.settings?.autonomyLevel || "") !== "engineering-autopilot") {
    return { kind: "gate", reason: "engineering-autopilot-permission-required" };
  }
  if (!autopilot.perpetual && autopilot.iteration >= autopilot.maxIterations) {
    return { kind: "gate", reason: "iteration-budget-exhausted" };
  }
  if (autopilot.perpetual && autopilot.maxCycles > 0 && autopilot.cycleNumber >= autopilot.maxCycles) {
    return { kind: "gate", reason: "cycle-budget-exhausted" };
  }

  if (autopilot.phase === "waiting-for-direction") return { kind: "idle", reason: "waiting-for-direction" };
  if (autopilot.phase === "safety-gate") return { kind: "idle", reason: autopilot.stopReason || "safety-gate" };
  if (!routingCurrent) return { kind: "gate", reason: "routing-ownership-stale" };

  if (autopilot.phase === "sync-plan") {
    return { kind: "transition", phase: "implement", reason: "canonical-truth-reconciled" };
  }

  if (autopilot.phase === "implement") {
    const agent = agentById(controlState, autopilot.implementationAgentId);
    if (!agent) return capacityAvailable
      ? { kind: "dispatch-implementation" }
      : { kind: "gate", reason: "worker-capacity-unavailable" };
    if (isActive(agent)) return { kind: "wait", reason: "implementation-active" };
    if (isAuthoritativeDone(agent)) {
      return { kind: "transition", phase: "verify", reason: "implementation-authoritative", patch: { candidateAgentId: agent.id } };
    }
    if (requiresManualReconciliation(agent)) {
      return { kind: "gate", reason: terminalFailureReason(agent, "implementation-reconciliation-required") };
    }
    return { kind: "gate", reason: terminalFailureReason(agent, "implementation") };
  }

  if (autopilot.phase === "verify" || autopilot.phase === "reverify") {
    const verifier = agentById(controlState, autopilot.verificationAgentId);
    if (!verifier) return capacityAvailable
      ? { kind: "dispatch-verification" }
      : { kind: "gate", reason: "worker-capacity-unavailable" };
    if (isActive(verifier)) return { kind: "wait", reason: "verification-active" };
    if (requiresManualReconciliation(verifier)) {
      return { kind: "gate", reason: terminalFailureReason(verifier, "verification-reconciliation-required") };
    }
    if (!isAuthoritativeDone(verifier)) return repairDecision(autopilot, terminalFailureReason(verifier, "verification"));
    const result = evidenceResult(taskById(controlState, verifier.taskId));
    if (result === "pass") return { kind: "transition", phase: "review", reason: "verification-passed" };
    if (result === "fail") return repairDecision(autopilot, "verification-failed");
    return { kind: "gate", reason: "verification-evidence-missing" };
  }

  if (autopilot.phase === "review") {
    const candidate = agentById(controlState, autopilot.candidateAgentId);
    if (!candidate) return { kind: "gate", reason: "candidate-missing" };
    if (candidate.reviewVerdict === "approved") {
      return { kind: "transition", phase: "integration-ready", reason: "review-approved" };
    }
    if (["changes-requested", "rejected"].includes(candidate.reviewVerdict)) {
      return repairDecision(autopilot, `review-${candidate.reviewVerdict}`);
    }
    const reviewer = agentById(controlState, autopilot.reviewAgentId);
    if (!reviewer) return capacityAvailable
      ? { kind: "dispatch-review" }
      : { kind: "gate", reason: "worker-capacity-unavailable" };
    if (isActive(reviewer)) return { kind: "wait", reason: "review-active" };
    if (isAuthoritativeDone(reviewer)) return { kind: "gate", reason: "review-verdict-missing" };
    if (requiresManualReconciliation(reviewer)) {
      return { kind: "gate", reason: terminalFailureReason(reviewer, "review-reconciliation-required") };
    }
    return repairDecision(autopilot, terminalFailureReason(reviewer, "review"));
  }

  if (autopilot.phase === "repair") {
    const repair = agentById(controlState, autopilot.repairAgentId);
    if (!repair) return capacityAvailable
      ? { kind: "dispatch-repair" }
      : { kind: "gate", reason: "worker-capacity-unavailable" };
    if (isActive(repair)) return { kind: "wait", reason: "repair-active" };
    if (requiresManualReconciliation(repair)) {
      return { kind: "gate", reason: terminalFailureReason(repair, "repair-reconciliation-required") };
    }
    if (!isAuthoritativeDone(repair)) {
      const reason = terminalFailureReason(repair, "repair");
      if (autopilot.repairLoops >= autopilot.maxRepairLoops) {
        return { kind: "gate", reason: `repair-budget-exhausted:${reason}` };
      }
      return {
        kind: "transition",
        phase: "repair",
        reason,
        patch: {
          repairAgentId: null,
          repairLoops: autopilot.repairLoops + 1
        }
      };
    }
    return {
      kind: "transition",
      phase: "reverify",
      reason: "repair-authoritative",
      patch: {
        candidateAgentId: repair.id,
        verificationAgentId: null,
        reviewAgentId: null,
        repairAgentId: null,
        repairLoops: autopilot.repairLoops + 1
      }
    };
  }

  if (autopilot.phase === "integration-ready") {
    return autopilot.perpetual
      ? { kind: "transition", phase: "integrate", reason: "review-approved-for-perpetual-integration", patch: { phaseRetries: 0 } }
      : { kind: "transition", phase: "continuity", reason: "integration-candidate-ready" };
  }

  if (autopilot.phase === "integrate") {
    const integration = agentById(controlState, autopilot.integrationAgentId);
    if (!integration) return capacityAvailable
      ? { kind: "dispatch-integration" }
      : { kind: "gate", reason: "worker-capacity-unavailable" };
    if (isActive(integration)) return { kind: "wait", reason: "integration-active" };
    if (requiresManualReconciliation(integration)) {
      return { kind: "gate", reason: terminalFailureReason(integration, "integration-reconciliation-required") };
    }
    if (!isAuthoritativeDone(integration)) {
      return phaseRetryDecision(autopilot, "integrate", "integrationAgentId", terminalFailureReason(integration, "integration"));
    }
    if (!integrationVerified) {
      return phaseRetryDecision(autopilot, "integrate", "integrationAgentId", "candidate-not-on-canonical-main");
    }
    return { kind: "transition", phase: "hygiene", reason: "canonical-main-integration-proven", patch: { phaseRetries: 0 } };
  }

  if (autopilot.phase === "hygiene") {
    const hygiene = agentById(controlState, autopilot.hygieneAgentId);
    if (!hygiene) return capacityAvailable
      ? { kind: "dispatch-hygiene" }
      : { kind: "gate", reason: "worker-capacity-unavailable" };
    if (isActive(hygiene)) return { kind: "wait", reason: "hygiene-active" };
    if (requiresManualReconciliation(hygiene)) {
      return { kind: "gate", reason: terminalFailureReason(hygiene, "hygiene-reconciliation-required") };
    }
    if (!isAuthoritativeDone(hygiene)) {
      return phaseRetryDecision(autopilot, "hygiene", "hygieneAgentId", terminalFailureReason(hygiene, "hygiene"));
    }
    return { kind: "transition", phase: "expand", reason: "repository-hygiene-complete", patch: { phaseRetries: 0 } };
  }

  if (autopilot.phase === "expand") {
    const expansion = agentById(controlState, autopilot.expansionAgentId);
    if (!expansion) return capacityAvailable
      ? { kind: "dispatch-expansion" }
      : { kind: "gate", reason: "worker-capacity-unavailable" };
    if (isActive(expansion)) return { kind: "wait", reason: "expansion-active" };
    if (requiresManualReconciliation(expansion)) {
      return { kind: "gate", reason: terminalFailureReason(expansion, "expansion-reconciliation-required") };
    }
    if (!isAuthoritativeDone(expansion)) {
      return phaseRetryDecision(autopilot, "expand", "expansionAgentId", terminalFailureReason(expansion, "expansion"));
    }
    return { kind: "transition", phase: "cycle-checkpoint", reason: "next-cycle-plan-ready", patch: { phaseRetries: 0 } };
  }

  if (autopilot.phase === "cycle-checkpoint") {
    return {
      kind: "transition",
      phase: "sync-plan",
      reason: "perpetual-cycle-complete",
      patch: {
        cycleNumber: autopilot.cycleNumber + 1,
        repairLoops: 0,
        phaseRetries: 0,
        implementationAgentId: null,
        candidateAgentId: null,
        verificationAgentId: null,
        reviewAgentId: null,
        repairAgentId: null,
        integrationAgentId: null,
        hygieneAgentId: null,
        expansionAgentId: null
      }
    };
  }

  if (autopilot.phase === "continuity") {
    return { kind: "integration-gate", reason: "operator-integration-approval-required" };
  }

  return { kind: "gate", reason: `unsupported-phase:${autopilot.phase}` };
}
