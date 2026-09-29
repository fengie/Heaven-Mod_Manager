export const NO_WORK_TERMINAL_STATUSES = new Set([
  "done",
  "failed",
  "finished",
  "interrupted",
  "orphaned",
  "disconnected"
]);

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

  const acceptanceComplete = explicitTrue(
    agent?.acceptanceCriteriaComplete,
    metadata.acceptance_criteria_complete
  );

  return integrated && (verificationPassed || acceptanceComplete);
}

export function terminationReconciliationDecision(agent, {
  expectsRepositoryWork = true,
  streamLost = false,
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

  const noWork = noWorkTerminationDecision(agent, {
    expectsRepositoryWork,
    maxOpeningMessageChars
  });
  if (noWork.noWork || transportLost) {
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
    reason: noWork.reason || "terminal-output-without-durable-proof"
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
