export const AUTOPILOT_PHASES = Object.freeze([
  "waiting-for-direction",
  "sync-plan",
  "implement",
  "verify",
  "review",
  "repair",
  "reverify",
  "integration-ready",
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
    phase: "waiting-for-direction",
    iteration: 0,
    repairLoops: 0,
    maxRepairLoops: 3,
    maxIterations: 40,
    runId: null,
    baseBranch: "main",
    implementationAgentId: null,
    candidateAgentId: null,
    verificationAgentId: null,
    reviewAgentId: null,
    repairAgentId: null,
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
    iteration: Math.max(0, Number(value?.iteration || 0) || 0),
    repairLoops: Math.max(0, Number(value?.repairLoops || 0) || 0),
    maxRepairLoops: Number.isFinite(Number(value?.maxRepairLoops))
      ? Math.max(0, Math.min(20, Number(value.maxRepairLoops)))
      : base.maxRepairLoops,
    maxIterations: Number.isFinite(Number(value?.maxIterations))
      ? Math.max(1, Math.min(500, Number(value.maxIterations)))
      : base.maxIterations,
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

function terminalFailureReason(agent, prefix) {
  if (!agent) return `${prefix}-missing`;
  return `${prefix}-${String(agent.status || "unknown")}`;
}

function requiresManualReconciliation(agent) {
  return ["orphaned", "interrupted", "stopped"].includes(String(agent?.status || ""));
}

export function decideAutopilotAction(controlState, {
  routingCurrent = false,
  capacityAvailable = true
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
  if (autopilot.iteration >= autopilot.maxIterations) return { kind: "gate", reason: "iteration-budget-exhausted" };

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
    return repairDecision(autopilot, terminalFailureReason(agent, "implementation"));
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
    return { kind: "transition", phase: "continuity", reason: "integration-candidate-ready" };
  }

  if (autopilot.phase === "continuity") {
    return { kind: "integration-gate", reason: "operator-integration-approval-required" };
  }

  return { kind: "gate", reason: `unsupported-phase:${autopilot.phase}` };
}
