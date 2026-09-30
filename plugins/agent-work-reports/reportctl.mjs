import path from "node:path";
import { fileURLToPath } from "node:url";
import { appendReport } from "./lib/report-store.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const DATA_DIR = process.env.AGENT_WORK_REPORTS_DATA_DIR || path.join(HERE, "data");
const REPORT_FILE = path.join(DATA_DIR, "reports.jsonl");
const PORT = Number(process.env.AGENT_WORK_REPORTS_PORT || 7341);
const BASE = process.env.AGENT_WORK_REPORTS_BASE_URL || `http://127.0.0.1:${PORT}`;

function usage() {
  console.log(`Agent Work Reports CLI

Usage:
  node reportctl.mjs <plan|progress|blocked|done|note> --summary "..." [options]

Options:
  --agent ID       Agent id; defaults to AGENT_CONTROL_AGENT_ID
  --task ID        Task id; defaults to AGENT_CONTROL_TASK_ID
  --progress N     0-100
  --details TEXT   Longer context
  --branch NAME    Branch/ref
  --status STATUS  Optional raw status
  --source NAME    Defaults to agent-cli

Examples:
  node reportctl.mjs plan --summary "Inspect updater path and tests"
  node reportctl.mjs progress --summary "Regression test added" --progress 55
  node reportctl.mjs blocked --summary "Waiting on exact-head CI"
  node reportctl.mjs done --summary "Merged and verified on main" --progress 100
`);
}

function parse(argv) {
  const phase = argv[2];
  if (!phase || ["-h", "--help", "help"].includes(phase)) return null;
  const flags = {};
  for (let i = 3; i < argv.length; i += 1) {
    const item = argv[i];
    if (!item.startsWith("--")) continue;
    const key = item.slice(2);
    const value = argv[i + 1] && !argv[i + 1].startsWith("--") ? argv[++i] : "true";
    flags[key] = value;
  }
  return {
    phase,
    agentId: flags.agent || process.env.AGENT_CONTROL_AGENT_ID,
    taskId: flags.task || process.env.AGENT_CONTROL_TASK_ID,
    summary: flags.summary,
    progress: flags.progress === undefined ? null : Number(flags.progress),
    details: flags.details || null,
    branch: flags.branch || null,
    status: flags.status || null,
    source: flags.source || "agent-cli"
  };
}

async function post(report) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 1500);
  try {
    const response = await fetch(new URL("/api/report", BASE), {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(report),
      signal: controller.signal
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    return await response.json();
  } finally {
    clearTimeout(timer);
  }
}

const report = parse(process.argv);
if (!report) {
  usage();
  process.exit(process.argv.length > 2 ? 0 : 2);
}

try {
  const result = await post(report);
  console.log(JSON.stringify({ ok: true, route: "server", report: result.report }, null, 2));
} catch (error) {
  const saved = appendReport(REPORT_FILE, { ...report, source: `${report.source}:offline` });
  console.log(JSON.stringify({ ok: true, route: "offline-log", serverError: error?.message || String(error), report: saved }, null, 2));
}
