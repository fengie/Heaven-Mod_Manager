import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { appendReport, readReports } from "./lib/report-store.mjs";
import { loadAgentControlLog, loadAgentControlSnapshot } from "./lib/agent-control-adapter.mjs";
import { buildViewModel } from "./lib/view-model.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const PUBLIC_DIR = path.join(HERE, "public");
const DATA_DIR = process.env.AGENT_WORK_REPORTS_DATA_DIR || path.join(HERE, "data");
const REPORT_FILE = path.join(DATA_DIR, "reports.jsonl");
const HOST = process.env.AGENT_WORK_REPORTS_HOST || "127.0.0.1";
const PORT = Number(process.env.AGENT_WORK_REPORTS_PORT || 7341);
const AGENT_CONTROL_BASE_URL = process.env.AGENT_CONTROL_BASE_URL || `http://127.0.0.1:${process.env.AGENT_CONTROL_PORT || 7331}`;
const MAX_BODY = 24 * 1024;

function isLoopback(host) {
  return ["127.0.0.1", "localhost", "::1", "[::1]"].includes(String(host).toLowerCase());
}
if (!isLoopback(HOST) && process.env.AGENT_WORK_REPORTS_ALLOW_NON_LOOPBACK !== "1") {
  throw new Error("Refusing non-loopback bind. Set AGENT_WORK_REPORTS_ALLOW_NON_LOOPBACK=1 only after adding an explicit authentication boundary.");
}
if (!Number.isInteger(PORT) || PORT < 1 || PORT > 65535) throw new Error("Invalid AGENT_WORK_REPORTS_PORT.");

function sendJson(res, status, value) {
  const body = Buffer.from(JSON.stringify(value));
  res.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": body.length,
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff"
  });
  res.end(body);
}

function sendFile(res, filePath, contentType) {
  try {
    const body = fs.readFileSync(filePath);
    res.writeHead(200, {
      "Content-Type": contentType,
      "Content-Length": body.length,
      "Cache-Control": "no-cache",
      "X-Content-Type-Options": "nosniff",
      "Content-Security-Policy": "default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'"
    });
    res.end(body);
  } catch {
    sendJson(res, 404, { ok: false, error: "Not found" });
  }
}

async function readJson(req) {
  let size = 0;
  const chunks = [];
  for await (const chunk of req) {
    size += chunk.length;
    if (size > MAX_BODY) throw Object.assign(new Error("Request body too large."), { statusCode: 413 });
    chunks.push(chunk);
  }
  if (!chunks.length) return {};
  try {
    return JSON.parse(Buffer.concat(chunks).toString("utf8"));
  } catch {
    throw Object.assign(new Error("Request body must be valid JSON."), { statusCode: 400 });
  }
}

function reportFilter(url) {
  const limit = Math.min(1000, Math.max(1, Number(url.searchParams.get("limit")) || 200));
  const agentId = url.searchParams.get("agentId");
  const taskId = url.searchParams.get("taskId");
  let reports = readReports(REPORT_FILE, { limit: 5000 });
  if (agentId) reports = reports.filter(item => item.agentId === agentId);
  if (taskId) reports = reports.filter(item => item.taskId === taskId);
  return reports.slice(-limit).reverse();
}

async function buildView() {
  const reports = readReports(REPORT_FILE, { limit: 5000 });
  let snapshot = null;
  let sourceError = null;
  try {
    snapshot = await loadAgentControlSnapshot({ base: AGENT_CONTROL_BASE_URL, timeoutMs: 2500 });
  } catch (error) {
    sourceError = error?.name === "AbortError" ? "Agent Control snapshot timed out." : (error?.message || String(error));
  }
  return buildViewModel({ snapshot, reports, sourceError });
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url || "/", `http://${HOST}:${PORT}`);
    const pathname = url.pathname;

    if (req.method === "GET" && pathname === "/") return sendFile(res, path.join(PUBLIC_DIR, "index.html"), "text/html; charset=utf-8");
    if (req.method === "GET" && pathname === "/app.js") return sendFile(res, path.join(PUBLIC_DIR, "app.js"), "text/javascript; charset=utf-8");
    if (req.method === "GET" && pathname === "/api/health") {
      return sendJson(res, 200, { ok: true, app: "agent-work-reports", generatedAt: new Date().toISOString(), agentControlBaseUrl: AGENT_CONTROL_BASE_URL });
    }
    if (req.method === "GET" && pathname === "/api/view") return sendJson(res, 200, await buildView());
    if (req.method === "GET" && pathname === "/api/reports") return sendJson(res, 200, { reports: reportFilter(url) });
    if (req.method === "POST" && pathname === "/api/report") {
      const body = await readJson(req);
      const report = appendReport(REPORT_FILE, body);
      return sendJson(res, 201, { ok: true, report });
    }

    const logMatch = pathname.match(/^\/api\/agents\/([^/]+)\/log$/);
    if (req.method === "GET" && logMatch) {
      const agentId = decodeURIComponent(logMatch[1]);
      try {
        const log = await loadAgentControlLog(agentId, { base: AGENT_CONTROL_BASE_URL, timeoutMs: 2500 });
        return sendJson(res, 200, { ok: true, agentId, source: "agent-control", log });
      } catch (error) {
        return sendJson(res, 502, { ok: false, agentId, error: error?.message || String(error) });
      }
    }

    sendJson(res, 404, { ok: false, error: "Not found" });
  } catch (error) {
    sendJson(res, Number(error?.statusCode) || 500, { ok: false, error: error?.message || String(error) });
  }
});

server.listen(PORT, HOST, () => {
  console.log(`Agent Work Reports listening on http://${HOST}:${PORT}`);
});
