import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { fileURLToPath } from "node:url";
import { spawn, execFile } from "node:child_process";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);
const HERE = path.dirname(fileURLToPath(import.meta.url));
const PUBLIC_DIR = path.join(HERE, "public");
const DATA_DIR = path.join(HERE, "data");
const STATE_FILE = path.join(DATA_DIR, "agents.json");
const PORT = Number(process.env.AGENT_CONTROL_PORT || 7331);
const HOST = "127.0.0.1";
const REPO = process.env.AGENT_CONTROL_REPO || path.join(os.homedir(), "local-ai-workspaces", "mhw-mods");
const WORKTREE_ROOT = process.env.AGENT_WORKTREE_ROOT || path.join(os.homedir(), "agent-worktrees");
const MAX_DEPLOY_COUNT = 8;
const children = new Map();

const rolePresets = {
  manager: {
    label: "Manager",
    instructions: "Coordinate the swarm. Establish repo truth first, read continuity files, inspect other agent branches, delegate or integrate only when useful, avoid duplicating active work, and leave a concise handoff."
  },
  main: {
    label: "Main Programmer",
    instructions: "Act as the primary implementation agent. Own the requested implementation end to end, read repo instructions first, keep changes focused, verify thoroughly, commit useful checkpoints, and push your branch when possible."
  },
  support: {
    label: "Support Agent",
    instructions: "Act as an independent support agent. Read repo instructions and current canonical state first. Find a bounded, non-duplicative contribution that helps the requested task, verify it, commit/push frequently, and leave a precise handoff for the main agent."
  },
  reviewer: {
    label: "Reviewer",
    instructions: "Review the requested work independently. Prefer evidence, tests, race/security/edge-case analysis, and concrete patches only when justified. Do not rewrite unrelated work. Commit/push any useful fixes and leave a concise review handoff."
  }
};

fs.mkdirSync(DATA_DIR, { recursive: true });
fs.mkdirSync(WORKTREE_ROOT, { recursive: true });
if (!fs.existsSync(STATE_FILE)) fs.writeFileSync(STATE_FILE, JSON.stringify({ agents: [] }, null, 2));

function loadState() {
  try {
    const parsed = JSON.parse(fs.readFileSync(STATE_FILE, "utf8"));
    return { agents: Array.isArray(parsed.agents) ? parsed.agents : [] };
  } catch {
    return { agents: [] };
  }
}

function saveState(state) {
  const tmp = `${STATE_FILE}.tmp`;
  fs.writeFileSync(tmp, JSON.stringify(state, null, 2));
  fs.renameSync(tmp, STATE_FILE);
}

function slugify(text) {
  return String(text || "task")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 28) || "task";
}

function isoNow() {
  return new Date().toISOString();
}

function findCodex() {
  if (process.env.CODEX_EXE && fs.existsSync(process.env.CODEX_EXE)) return process.env.CODEX_EXE;
  const base = path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local"), "OpenAI", "Codex", "bin");
  if (!fs.existsSync(base)) throw new Error(`Codex install directory not found: ${base}`);
  const candidates = [];
  for (const entry of fs.readdirSync(base, { withFileTypes: true })) {
    if (!entry.isDirectory()) continue;
    const exe = path.join(base, entry.name, "codex.exe");
    if (fs.existsSync(exe)) candidates.push({ exe, mtime: fs.statSync(exe).mtimeMs });
  }
  candidates.sort((a, b) => b.mtime - a.mtime);
  if (!candidates.length) throw new Error("codex.exe was not found under the ChatGPT Codex install.");
  return candidates[0].exe;
}

function isPidAlive(pid) {
  if (!pid) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

function readTextIfExists(file) {
  try {
    return fs.existsSync(file) ? fs.readFileSync(file, "utf8").trim() : "";
  } catch {
    return "";
  }
}

function tailFile(file, maxChars = 16000) {
  try {
    if (!fs.existsSync(file)) return "";
    const stat = fs.statSync(file);
    const start = Math.max(0, stat.size - maxChars);
    const fd = fs.openSync(file, "r");
    const buffer = Buffer.alloc(stat.size - start);
    fs.readSync(fd, buffer, 0, buffer.length, start);
    fs.closeSync(fd);
    return buffer.toString("utf8");
  } catch {
    return "";
  }
}

function readLogSummary(file) {
  const lines = tailFile(file, 12000).split(/\r?\n/).filter(Boolean).reverse();
  for (const line of lines) {
    try {
      const event = JSON.parse(line);
      const message = event?.error?.message || event?.message || event?.item?.text;
      if (typeof message === "string" && message.trim()) return message.trim();
    } catch {}
  }
  return "";
}

function refreshState() {
  const state = loadState();
  let changed = false;
  for (const agent of state.agents) {
    const alive = isPidAlive(agent.pid);
    if (agent.status === "running" || agent.status === "starting") {
      if (!alive) {
        const last = readTextIfExists(agent.lastMessagePath);
        agent.status = last ? "done" : (agent.exitCode === 0 ? "done" : "finished");
        agent.finishedAt ||= isoNow();
        changed = true;
      }
    }
    const last = readTextIfExists(agent.lastMessagePath) || readLogSummary(agent.logPath);
    if (last && last !== agent.lastMessage) {
      agent.lastMessage = last;
      agent.updatedAt = isoNow();
      changed = true;
    }
  }
  if (changed) saveState(state);
  return state;
}

async function git(args, cwd = REPO) {
  const { stdout, stderr } = await execFileAsync("git", ["-C", cwd, ...args], {
    windowsHide: true,
    maxBuffer: 2 * 1024 * 1024
  });
  return `${stdout || ""}${stderr || ""}`.trim();
}

function buildPrompt(role, task, baseBranch, branchName) {
  const preset = rolePresets[role] || rolePresets.support;
  return [
    `You are a ${preset.label} in the user's mhw agent swarm.`,
    "",
    preset.instructions,
    "",
    "Hard requirements:",
    "- Work only in the worktree you were given. Never edit another agent's worktree.",
    "- Do not switch or force-update main. Your branch is isolated for a reason.",
    "- Start by reading AGENTS.md, NEXT-AGENT-START-HERE.md, and the relevant _AGENT_CONTEXT continuity files.",
    `- Your base branch was ${baseBranch}; your working branch is ${branchName}.`,
    "- Re-establish current repository truth before trusting old handoffs.",
    "- Keep scope tight. Do not duplicate work already present on another active branch.",
    "- Run targeted verification for your changes.",
    "- Commit meaningful checkpoints. Push your branch when credentials/network permit.",
    "- If you cannot complete everything, leave a precise handoff with what is done, what remains, and exact verification results.",
    "",
    "USER TASK:",
    task.trim()
  ].join("\n");
}

async function deployOne({ role, task, baseBranch, model }) {
  if (!rolePresets[role]) throw new Error(`Unknown role: ${role}`);
  if (!task || !task.trim()) throw new Error("Task is required.");
  const base = (baseBranch || "main").trim();
  const stamp = new Date().toISOString().replace(/[-:TZ.]/g, "").slice(0, 14);
  const suffix = Math.random().toString(36).slice(2, 6);
  const id = `${role}-${stamp}-${suffix}`;
  const branchName = `agent/control-${role}-${slugify(task)}-${stamp}-${suffix}`;
  const worktree = path.join(WORKTREE_ROOT, id);
  const logPath = path.join(DATA_DIR, `${id}.jsonl`);
  const lastMessagePath = path.join(DATA_DIR, `${id}.last.txt`);

  await git(["fetch", "origin", "--prune"]);
  await git(["rev-parse", "--verify", `refs/remotes/origin/${base}`]);
  await git(["worktree", "add", "-b", branchName, worktree, `origin/${base}`]);

  const codex = findCodex();
  const args = [
    "exec",
    "--json",
    "--approve-for-me",
    "-C", worktree,
    "-o", lastMessagePath
  ];
  if (model && model.trim()) args.push("-m", model.trim());
  args.push("-");

  const logFd = fs.openSync(logPath, "a");
  const child = spawn(codex, args, {
    cwd: worktree,
    stdio: ["pipe", logFd, logFd],
    windowsHide: true,
    detached: false,
    env: { ...process.env }
  });
  child.stdin.end(buildPrompt(role, task, base, branchName));
  fs.closeSync(logFd);

  const agent = {
    id,
    role,
    roleLabel: rolePresets[role].label,
    task: task.trim(),
    status: "running",
    pid: child.pid,
    baseBranch: base,
    branchName,
    worktree,
    logPath,
    lastMessagePath,
    lastMessage: "",
    model: model?.trim() || null,
    startedAt: isoNow(),
    updatedAt: isoNow(),
    finishedAt: null,
    exitCode: null
  };

  const state = loadState();
  state.agents.unshift(agent);
  saveState(state);
  children.set(id, child);

  child.on("exit", (code, signal) => {
    const current = loadState();
    const item = current.agents.find(a => a.id === id);
    if (item) {
      item.exitCode = code;
      item.signal = signal || null;
      item.status = code === 0 ? "done" : (item.status === "stopping" ? "stopped" : "failed");
      item.finishedAt = isoNow();
      item.updatedAt = isoNow();
      item.lastMessage = readTextIfExists(item.lastMessagePath) || readLogSummary(item.logPath);
      saveState(current);
    }
    children.delete(id);
  });

  child.on("error", (error) => {
    const current = loadState();
    const item = current.agents.find(a => a.id === id);
    if (item) {
      item.status = "failed";
      item.error = error.message;
      item.finishedAt = isoNow();
      item.updatedAt = isoNow();
      saveState(current);
    }
    children.delete(id);
  });

  return agent;
}

async function stopAgent(id) {
  const state = loadState();
  const agent = state.agents.find(a => a.id === id);
  if (!agent) throw new Error("Agent not found.");
  if (!isPidAlive(agent.pid)) {
    agent.status = agent.status === "running" ? "finished" : agent.status;
    agent.finishedAt ||= isoNow();
    saveState(state);
    return agent;
  }

  agent.status = "stopping";
  agent.updatedAt = isoNow();
  saveState(state);

  await new Promise(resolve => {
    const killer = spawn("taskkill", ["/PID", String(agent.pid), "/T", "/F"], {
      windowsHide: true,
      stdio: "ignore"
    });
    killer.on("exit", resolve);
    killer.on("error", resolve);
  });

  agent.status = "stopped";
  agent.finishedAt = isoNow();
  agent.updatedAt = isoNow();
  saveState(state);
  children.delete(id);
  return agent;
}

function sendJson(res, status, value) {
  const body = JSON.stringify(value, null, 2);
  res.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store",
    "Content-Length": Buffer.byteLength(body)
  });
  res.end(body);
}

function readJson(req) {
  return new Promise((resolve, reject) => {
    let body = "";
    req.on("data", chunk => {
      body += chunk;
      if (body.length > 1024 * 1024) req.destroy();
    });
    req.on("end", () => {
      try {
        resolve(body ? JSON.parse(body) : {});
      } catch {
        reject(new Error("Invalid JSON body."));
      }
    });
    req.on("error", reject);
  });
}

function serveStatic(req, res, pathname) {
  const relative = pathname === "/" ? "index.html" : pathname.replace(/^\/+/, "");
  const file = path.resolve(PUBLIC_DIR, relative);
  if (!file.startsWith(path.resolve(PUBLIC_DIR)) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
    return false;
  }
  const ext = path.extname(file).toLowerCase();
  const contentType = {
    ".html": "text/html; charset=utf-8",
    ".js": "text/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".svg": "image/svg+xml"
  }[ext] || "application/octet-stream";
  res.writeHead(200, { "Content-Type": contentType, "Cache-Control": "no-store" });
  fs.createReadStream(file).pipe(res);
  return true;
}

function allowedOrigin(req) {
  const origin = req.headers.origin;
  return !origin || origin === `http://${HOST}:${PORT}` || origin === `http://localhost:${PORT}`;
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, `http://${HOST}:${PORT}`);
    const pathname = decodeURIComponent(url.pathname);

    if (!allowedOrigin(req)) return sendJson(res, 403, { error: "Origin not allowed." });

    if (req.method === "GET" && pathname === "/api/status") {
      const state = refreshState();
      const running = state.agents.filter(a => a.status === "running" || a.status === "starting").length;
      return sendJson(res, 200, {
        ok: true,
        host: os.hostname(),
        repo: REPO,
        worktreeRoot: WORKTREE_ROOT,
        codex: findCodex(),
        running,
        total: state.agents.length,
        roles: rolePresets
      });
    }

    if (req.method === "GET" && pathname === "/api/agents") {
      const state = refreshState();
      return sendJson(res, 200, state.agents);
    }

    if (req.method === "POST" && pathname === "/api/deploy") {
      const body = await readJson(req);
      const count = Math.max(1, Math.min(MAX_DEPLOY_COUNT, Number(body.count || 1)));
      const created = [];
      for (let i = 0; i < count; i += 1) {
        created.push(await deployOne({
          role: body.role || "support",
          task: body.task || "",
          baseBranch: body.baseBranch || "main",
          model: body.model || ""
        }));
      }
      return sendJson(res, 201, { agents: created });
    }

    const stopMatch = pathname.match(/^\/api\/agents\/([^/]+)\/stop$/);
    if (req.method === "POST" && stopMatch) {
      return sendJson(res, 200, await stopAgent(stopMatch[1]));
    }

    const logMatch = pathname.match(/^\/api\/agents\/([^/]+)\/log$/);
    if (req.method === "GET" && logMatch) {
      const state = refreshState();
      const agent = state.agents.find(a => a.id === logMatch[1]);
      if (!agent) return sendJson(res, 404, { error: "Agent not found." });
      return sendJson(res, 200, {
        id: agent.id,
        status: agent.status,
        lastMessage: readTextIfExists(agent.lastMessagePath) || readLogSummary(agent.logPath),
        tail: tailFile(agent.logPath)
      });
    }

    if (req.method === "GET" && serveStatic(req, res, pathname)) return;
    return sendJson(res, 404, { error: "Not found." });
  } catch (error) {
    return sendJson(res, 500, { error: error.message || String(error) });
  }
});

server.listen(PORT, HOST, () => {
  console.log(`Heaven Agent Control listening on http://${HOST}:${PORT}`);
  console.log(`Repo: ${REPO}`);
  console.log(`Worktrees: ${WORKTREE_ROOT}`);
});
