export const NO_WORK_TERMINAL_STATUSES = new Set([
  "done",
  "failed",
  "finished",
  "interrupted",
  "orphaned",
  "disconnected"
]);

export const SWARM_TAIL_UNFINISHED_STATUSES = new Set([
  "failed",
  "interrupted",
  "orphaned",
  "stale"
]);


function finiteNonZeroNumber(value) {
  if (value === null || value === undefined || String(value).trim() === "") return false;
  const number = Number(value);
  return Number.isFinite(number) && number !== 0;
}

export function hasDeterministicRuntimeFailure(agent) {
  const metadata = agent?.source_metadata && typeof agent.source_metadata === "object"
    ? agent.source_metadata
    : {};
  const completionEvidence = String(
    agent?.completionEvidence
    || metadata.completion_evidence
    || ""
  ).trim().toLowerCase();

  return finiteNonZeroNumber(agent?.exitCode)
    || finiteNonZeroNumber(metadata.exit_code)
    || ["spawn-error", "provider-capacity"].includes(completionEvidence);
}

export function swarmTailRecoveryRootId(agent) {
  return String(agent?.swarmTailRecoveryRootAgentId || agent?.recoveryRootAgentId || agent?.id || "").trim();
}

export function isSwarmTailUnfinishedCandidate(agent, task = null) {
  const status = String(agent?.status || agent?.state || "").trim().toLowerCase();
  if (!SWARM_TAIL_UNFINISHED_STATUSES.has(status)) return false;
  if (status === "capacity-blocked" || agent?.failureClass === "provider-capacity") return false;
  if (hasDeterministicRuntimeFailure(agent) && !hasSubstantiveWorkEvidence(agent)) return false;
  if (["retry-pending", "retry-waiting", "retry-dispatched"].includes(String(agent?.recoveryStatus || ""))) return false;
  if (String(agent?.recoveryStatus || "") === "work-verified-complete") return false;
  const supervisedStaleRecovery = String(agent?.swarmTailRecoveryCause || "") === "stale-progress-timeout";
  if (!supervisedStaleRecovery && (agent?.stopRequestedAt || agent?.completionEvidence === "verified-operator-stop")) return false;
  const taskStatus = String(task?.status || "").trim().toLowerCase();
  if (["done", "candidate", "finished", "stopped"].includes(taskStatus)) return false;
  return Boolean(String(task?.objective || agent?.task || "").trim());
}

export function planSwarmTailRecoveryBatch(state, {
  maxWorkers = 4,
  maxAttemptsPerRoot = 2,
  since = null,
  excludeAgentIds = [],
  now = Date.now()
} = {}) {
  const agents = Array.isArray(state?.agents) ? state.agents : [];
  const excluded = new Set((Array.isArray(excludeAgentIds) ? excludeAgentIds : [excludeAgentIds])
    .map(value => String(value || "").trim())
    .filter(Boolean));
  const sinceMs = Date.parse(String(since || ""));
  const inScope = agent => {
    if (!Number.isFinite(sinceMs)) return true;
    const startedMs = Date.parse(String(agent?.startedAt || agent?.source_metadata?.started_at || ""));
    return Number.isFinite(startedMs) && startedMs >= sinceMs;
  };
  const tasksById = new Map((Array.isArray(state?.tasks) ? state.tasks : []).map(task => [task.id, task]));
  const limit = Math.max(1, Math.floor(Number(maxWorkers) || 4));
  const activeRecoveryWorkers = agents.filter(agent =>
    inScope(agent)
    && agent?.swarmTailRecovery === true
    && ["reserved", "starting", "running", "waiting", "blocked", "stale", "stopping"].includes(String(agent?.status || ""))
  ).length;
  const availableRecoverySlots = Math.max(0, limit - activeRecoveryWorkers);
  if (availableRecoverySlots === 0) return [];
  const maxAttempts = Math.max(1, Math.floor(Number(maxAttemptsPerRoot) || 2));
  const selected = [];
  const seenRoots = new Set();

  for (const agent of agents) {
    if (!inScope(agent) || excluded.has(String(agent?.id || ""))) continue;
    const retryAt = Date.parse(String(agent?.swarmTailRecoveryNextAt || ""));
    if (Number.isFinite(retryAt) && retryAt > Number(now)) continue;
    const task = tasksById.get(agent?.taskId) || null;
    if (!isSwarmTailUnfinishedCandidate(agent, task)) continue;
    const rootId = swarmTailRecoveryRootId(agent);
    if (!rootId || seenRoots.has(rootId)) continue;

    const lineage = agents.filter(candidate => swarmTailRecoveryRootId(candidate) === rootId && candidate.id !== rootId);
    const otherLineage = lineage.filter(candidate => candidate.id !== agent.id);
    if (otherLineage.some(candidate => ["reserved", "starting", "running", "waiting", "blocked", "stale", "stopping"].includes(String(candidate?.status || "")))) continue;
    if (lineage.some(candidate => String(candidate?.status || "") === "done")) continue;
    const lineageAttempts = lineage.filter(candidate => candidate?.swarmTailRecovery === true || candidate?.retryOfAgentId).length;
    const dispatchFailures = Math.max(0, Math.floor(Number(agent?.swarmTailRecoveryDispatchFailures) || 0));
    const attempts = Math.max(lineageAttempts, dispatchFailures);
    if (attempts >= maxAttempts) continue;

    seenRoots.add(rootId);
    selected.push({ agent, task, rootId, attempt: attempts + 1 });
    if (selected.length >= availableRecoverySlots) break;
  }
  return selected;
}

const EXECUTION_OPENING_PATTERNS = [
  /\b(?:i['’]?m|i am)\s+treating\s+this\s+as\s+an?\s+execution\s+assignment\b/i,
  /\bnot\s+a\s+review\b.{0,160}\b(?:i['’]?ll|i will)\s+use\b/i,
  /\btrain(?:ing)?\s+on\s+the\s+repo\s+first\b/i
];

function arrayHasValues(value) {
  return Array.isArray(value) && value.some(item => {
    if (item === null || item === undefined) return false;
    if (typeof item === "string") return item.trim().length > 0;
    return true;
  });
}

function positiveNumber(value) {
  const number = Number(value);
  return Number.isFinite(number) && number > 0;
}

export function looksLikeExecutionOpener(value, { maxChars = 1800 } = {}) {
  const text = String(value || "").trim();
  if (!text || text.length > maxChars) return false;
  return EXECUTION_OPENING_PATTERNS.some(pattern => pattern.test(text));
}

export function hasSubstantiveWorkEvidence(agent) {
  if (agent?.worktreeDirty === true || agent?.source_metadata?.worktree_dirty === true) return true;
  const currentSha = String(agent?.currentSha || agent?.current_sha || "").trim();
  const baseSha = String(agent?.baseSha || agent?.base_sha || "").trim();
  if (currentSha && baseSha && currentSha !== baseSha) return true;

  for (const key of ["changedFiles", "artifacts", "verificationResults", "commits"]) {
    if (arrayHasValues(agent?.[key])) return true;
  }
  for (const key of ["commitSha", "prNumber", "pullRequestNumber", "artifactPath"]) {
    if (agent?.[key]) return true;
  }

  const metadata = agent?.source_metadata && typeof agent.source_metadata === "object"
    ? agent.source_metadata
    : {};
  for (const key of ["changed_files", "artifacts", "verification_results", "commits"]) {
    if (arrayHasValues(metadata[key])) return true;
  }
  for (const key of ["commit_sha", "current_commit_sha", "pr_number", "pull_request_number", "artifact_path"]) {
    if (metadata[key]) return true;
  }
  for (const key of ["write_count", "test_run_count", "verification_count", "commit_count", "artifact_count"]) {
    if (positiveNumber(metadata[key])) return true;
  }

  const message = String(agent?.lastMessage || agent?.last_action_summary || metadata.final_message || "");
  if (/\b(?:commit|committed)\s+[0-9a-f]{7,40}\b/i.test(message)) return true;
  if (/\bPR\s*#\d+\b/i.test(message)) return true;
  return false;
}

function explicitTrue(...values) {
  return values.some(value => value === true);
}

function verificationArrayPassed(value) {
  if (!Array.isArray(value) || value.length === 0) return false;
  return value.every(item => {
    if (item === true) return true;
    if (!item || typeof item !== "object") return false;
    const status = String(item.status || item.result || item.outcome || "").trim().toLowerCase();
    return ["pass", "passed", "success", "succeeded", "green", "ok"].includes(status);
  });
}

export function shouldRetireFromLiveRegistry(agent) {
  const status = String(agent?.status || agent?.state || "").trim().toLowerCase();
  const recoveryStatus = String(agent?.recoveryStatus || agent?.recovery_status || "").trim().toLowerCase();

  if (["reserved", "starting", "running", "waiting", "blocked", "stale", "stopping"].includes(status)) return false;
  if (hasSubstantiveWorkEvidence(agent)) return false;

  if (["retry-dispatched", "retry-exhausted", "retry-disabled"].includes(recoveryStatus)) return true;
  if (status === "capacity-blocked" || recoveryStatus === "provider-capacity") return true;
  if (status === "failed" && hasDeterministicRuntimeFailure(agent)) return true;

  return false;
}

export function hasVerifiedCompletionEvidence(agent) {
  const metadata = agent?.source_metadata && typeof agent.source_metadata === "object"
    ? agent.source_metadata
    : {};

  if (explicitTrue(
    agent?.completionVerified,
    agent?.verifiedComplete,
    metadata.completion_verified,
    metadata.verified_complete
  )) return true;

  const integrated = explicitTrue(
    agent?.integratedToMain,
    agent?.mergedToMain,
    agent?.prMerged,
    metadata.integrated_to_main,
    metadata.merged_to_main,
    metadata.pr_merged,
    metadata.pull_request_merged
  );
  const verificationPassed = explicitTrue(
    agent?.verificationPassed,
    metadata.verification_passed,
    metadata.tests_passed,
    metadata.release_gate_passed
  ) || verificationArrayPassed(agent?.verificationResults)
    || verificationArrayPassed(metadata.verification_results);

  const releaseRequired = explicitTrue(
    agent?.releaseRequired,
    metadata.release_required
  );
  const releaseVerified = explicitTrue(
    agent?.releaseVerified,
    metadata.release_verified,
    metadata.release_published_and_verified
  );

  return integrated && verificationPassed && (!releaseRequired || releaseVerified);
}

export function terminationReconciliationDecision(agent, {
  expectsRepositoryWork = true,
  streamLost = false,
  durableEvidenceChecked = false,
  maxOpeningMessageChars = 1800
} = {}) {
  const status = String(agent?.status || agent?.state || "").trim().toLowerCase();
  const metadata = agent?.source_metadata && typeof agent.source_metadata === "object"
    ? agent.source_metadata
    : {};
  if (!expectsRepositoryWork || !NO_WORK_TERMINAL_STATUSES.has(status)) {
    return {
      reconcile: false,
      recoveryStatus: null,
      retry: false,
      action: "none",
      reason: null
    };
  }

  const transportLost = streamLost === true
    || status === "disconnected"
    || explicitTrue(
      metadata.stream_lost,
      metadata.response_stream_failed,
      metadata.transport_disconnected
    );

  if (hasVerifiedCompletionEvidence(agent)) {
    return {
      reconcile: true,
      recoveryStatus: "work-verified-complete",
      retry: false,
      action: "complete",
      reason: "durable-completion-evidence"
    };
  }

  if (hasSubstantiveWorkEvidence(agent)) {
    return {
      reconcile: true,
      recoveryStatus: "work-detected-incomplete",
      retry: false,
      action: "reconcile-existing-work",
      reason: "durable-work-without-completion-proof"
    };
  }

  if (hasDeterministicRuntimeFailure(agent)) {
    return {
      reconcile: true,
      recoveryStatus: "work-unverified",
      retry: false,
      action: "inspect",
      reason: "deterministic-runtime-failure"
    };
  }

  const noWork = noWorkTerminationDecision(agent, {
    expectsRepositoryWork,
    maxOpeningMessageChars
  });
  if ((noWork.noWork || transportLost) && durableEvidenceChecked) {
    return {
      reconcile: true,
      recoveryStatus: "no-durable-work-detected-retry",
      retry: true,
      action: "retry",
      reason: noWork.reason || "stream-lost-without-durable-work"
    };
  }

  return {
    reconcile: true,
    recoveryStatus: "work-unverified",
    retry: false,
    action: "inspect",
    reason: durableEvidenceChecked
      ? (noWork.reason || "terminal-output-without-durable-proof")
      : "durable-evidence-scan-incomplete"
  };
}

export function noWorkTerminationDecision(agent, {
  expectsRepositoryWork = true,
  maxOpeningMessageChars = 1800
} = {}) {
  const status = String(agent?.status || agent?.state || "").trim().toLowerCase();
  if (!expectsRepositoryWork || !NO_WORK_TERMINAL_STATUSES.has(status)) {
    return { noWork: false, retry: false, reason: null, openingOnly: false, emptyOutput: false };
  }
  if (hasDeterministicRuntimeFailure(agent)) {
    return { noWork: false, retry: false, reason: "deterministic-runtime-failure", openingOnly: false, emptyOutput: false };
  }
  if (hasSubstantiveWorkEvidence(agent)) {
    return { noWork: false, retry: false, reason: "substantive-work-evidence", openingOnly: false, emptyOutput: false };
  }

  const metadata = agent?.source_metadata && typeof agent.source_metadata === "object"
    ? agent.source_metadata
    : {};
  const message = String(agent?.lastMessage || agent?.last_action_summary || metadata.final_message || "").trim();
  const emptyOutput = message.length === 0;
  const openingOnly = looksLikeExecutionOpener(message, { maxChars: maxOpeningMessageChars });

  if (!emptyOutput && !openingOnly) {
    return { noWork: false, retry: false, reason: "terminal-output-not-opening-only", openingOnly, emptyOutput };
  }

  return {
    noWork: true,
    retry: true,
    reason: openingOnly ? "terminated-after-execution-opener" : "terminated-without-output",
    openingOnly,
    emptyOutput
  };
}

export function recoveryMachineTarget(value, machinePolicies = {}) {
  const candidate = String(value || "").trim().toLowerCase();
  if (!candidate || candidate === "auto") return "auto";
  const known = Object.keys(machinePolicies || {}).some(name => String(name).trim().toLowerCase() === candidate);
  return known ? candidate : "auto";
}

export function recoveryBackoffMs(dispatchFailures, {
  baseMs = 15_000,
  maxMs = 300_000
} = {}) {
  const failures = Math.max(1, Math.floor(Number(dispatchFailures) || 1));
  const base = Math.max(1_000, Number(baseMs) || 15_000);
  const maximum = Math.max(base, Number(maxMs) || 300_000);
  return Math.min(maximum, base * (2 ** Math.min(8, failures - 1)));
}

export function recoveryBackoffWithJitterMs(dispatchFailures, {
  baseMs = 15_000,
  maxMs = 300_000,
  jitterUnit = 0.5,
  minFactor = 0.5
} = {}) {
  const capped = recoveryBackoffMs(dispatchFailures, { baseMs, maxMs });
  const unit = Math.max(0, Math.min(1, Number(jitterUnit) || 0));
  const floorFactor = Math.max(0, Math.min(1, Number(minFactor) || 0.5));
  const factor = floorFactor + ((1 - floorFactor) * unit);
  return Math.max(1_000, Math.floor(capped * factor));
}
