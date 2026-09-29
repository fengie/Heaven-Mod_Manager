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
  for (const key of ["tool_call_count", "tool_calls_count", "progress_events", "write_count", "test_run_count"]) {
    if (positiveNumber(metadata[key])) return true;
  }

  const message = String(agent?.lastMessage || agent?.last_action_summary || metadata.final_message || "");
  if (/\b(?:commit|committed)\s+[0-9a-f]{7,40}\b/i.test(message)) return true;
  if (/\bPR\s*#\d+\b/i.test(message)) return true;
  return false;
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

export function recoveryBackoffMs(dispatchFailures, {
  baseMs = 15_000,
  maxMs = 300_000
} = {}) {
  const failures = Math.max(1, Math.floor(Number(dispatchFailures) || 1));
  const base = Math.max(1_000, Number(baseMs) || 15_000);
  const maximum = Math.max(base, Number(maxMs) || 300_000);
  return Math.min(maximum, base * (2 ** Math.min(8, failures - 1)));
}
