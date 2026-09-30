import fs from "node:fs";
import path from "node:path";

export const REPORT_SCHEMA = "agent-work-report/v1";
export const REPORT_PHASES = new Set(["plan", "progress", "blocked", "done", "note"]);

function cleanText(value, max = 2048) {
  if (value === null || value === undefined) return null;
  const text = String(value)
    .replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/g, "")
    .trim();
  if (!text) return null;
  return text.slice(0, max);
}

function cleanId(value, max = 256) {
  const text = cleanText(value, max);
  if (!text) return null;
  if (!/^[A-Za-z0-9._:@/\\-]+$/.test(text)) {
    throw new Error("IDs may contain only letters, numbers, dot, underscore, colon, @, slash, backslash, and hyphen.");
  }
  return text;
}

export function normalizeReport(input = {}, { now = Date.now() } = {}) {
  if (!input || typeof input !== "object" || Array.isArray(input)) {
    throw new Error("Report body must be a JSON object.");
  }

  const phase = String(input.phase || "progress").trim().toLowerCase();
  if (!REPORT_PHASES.has(phase)) {
    throw new Error(`Unsupported report phase: ${phase}`);
  }

  const agentId = cleanId(input.agentId ?? input.agent_id ?? process.env.AGENT_CONTROL_AGENT_ID);
  if (!agentId) throw new Error("agentId is required.");

  const summary = cleanText(input.summary, 1600);
  if (!summary) throw new Error("summary is required.");

  const explicitProgress = input.progress === null || input.progress === undefined || input.progress === ""
    ? null
    : Number(input.progress);
  if (explicitProgress !== null && (!Number.isFinite(explicitProgress) || explicitProgress < 0 || explicitProgress > 100)) {
    throw new Error("progress must be between 0 and 100.");
  }

  const at = new Date(Number.isFinite(Number(input.at)) ? Number(input.at) : now);
  if (!Number.isFinite(at.getTime())) throw new Error("Invalid report timestamp.");

  return {
    schema: REPORT_SCHEMA,
    at: at.toISOString(),
    agentId,
    taskId: cleanId(input.taskId ?? input.task_id ?? process.env.AGENT_CONTROL_TASK_ID),
    phase,
    summary,
    details: cleanText(input.details, 8000),
    progress: explicitProgress === null ? null : Math.round(explicitProgress),
    branch: cleanText(input.branch, 512),
    source: cleanText(input.source, 128) || "agent",
    status: cleanText(input.status, 128)
  };
}

export function appendReport(filePath, input, options = {}) {
  const report = normalizeReport(input, options);
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.appendFileSync(filePath, `${JSON.stringify(report)}\n`, { encoding: "utf8", mode: 0o600 });
  return report;
}

export function readReports(filePath, { limit = 5000 } = {}) {
  const bounded = Math.min(10000, Math.max(1, Number(limit) || 5000));
  if (!fs.existsSync(filePath)) return [];
  const stat = fs.statSync(filePath);
  if (stat.size > 16 * 1024 * 1024) {
    throw new Error("Report log exceeds the 16 MiB safety limit; archive or compact it before continuing.");
  }

  const raw = fs.readFileSync(filePath, "utf8");
  const lines = raw.split(/\r?\n/).filter(Boolean).slice(-bounded);
  const reports = [];
  for (const line of lines) {
    try {
      const parsed = JSON.parse(line);
      if (parsed?.schema === REPORT_SCHEMA && parsed.agentId && parsed.summary) reports.push(parsed);
    } catch {
      // Preserve availability when one partial/corrupt line exists; valid reports remain visible.
    }
  }
  return reports;
}
