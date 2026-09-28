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
const STATE_FILE = path.join(DATA_DIR, "control-plane.json");
const LEGACY_STATE_FILE = path.join(DATA_DIR, "agents.json");

const PORT = Number(process.env.AGENT_CONTROL_PORT || 7331);
const HOST = process.env.AGENT_CONTROL_HOST || "127.0.0.1";
const REPO = process.env.AGENT_CONTROL_REPO || path.join(os.homedir(), "local-ai-workspaces", "mhw-mods");
const WORKTREE_ROOT = process.env.AGENT_WORKTREE_ROOT || path.join(os.homedir(), "agent-worktrees");
const MAX_DEPLOY_COUNT = Number(process.env.AGENT_CONTROL_MAX_DEPLOY_COUNT || 8);
const MAX_ACTIVE_AGENTS = Number(process.env.AGENT_CONTROL_MAX_ACTIVE || 8);
const EVENT_LIMIT = 500;
const STATE_VERSION = 2;
const children = new Map();

const rolePresets = {
  manager: {
    label: "Manager",
    instructions: "Coordinate the swarm. Establish repository truth first, inspect active tasks/leases/branches, prevent duplicate implementation, assign bounded work, and leave a durable handoff."
  },
  main: {
    label: "Main Programmer",
    instructions: "Own the requested implementation end to end. Read repository doctrine first, keep one architecture boundary at a time, verify thoroughly, checkpoint frequently, and push useful work."
  },
  support: {
    label: "Support Agent",
    instructions: "Find a bounded, non-duplicative contribution that helps the primary objective. Prefer investigation, focused fixes, tests, or evidence that the primary agent can consume."
  },
  reviewer: {
    label: "Reviewer",
    instructions: "Review independently. Look for correctness, security, races, regressions, missing tests, stale assumptions, and unsupported verification claims. Patch only when justified."
  },
  test: {
    label: "Test Agent",
    instructions: "Design and run targeted tests for the requested boundary. Prefer adversarial and failure-path coverage. Do not weaken tests to make work pass."
  },
  integration: {
    label: "Integration Agent",
    instructions: "Evaluate candidate branches against canonical repository truth. Identify overlaps, verification status, stale context, and safe integration order. Never blindly merge."
  },
  recovery: {
    label: "Recovery Agent",
    instructions: "Recover interrupted or stale work. Re-establish exact branch state, preserve useful work, verify what is actually complete, and leave a precise continuation point."
  },
  release: {
    label: "Release Agent",
    instructions: "Validate release readiness, exact-SHA evidence, packaging, rollback/update behavior, and handoff state. Do not claim release readiness beyond verified evidence."
  }
};

function isoNow() {
  return new Date().toISOString();
}

function defaultState() {
  return {
    version: STATE_VERSION,
    agents: [],
    tasks: [],
    leases: [],
    events: []
  };
}

fs.mkdirSync(DATA_DIR, { recursive: true });
fs.mkdirSync(WORKTREE_ROOT, { recursive: true });

if (!fs.existsSync(STATE_FILE)) {
  let initial = defaultState();
  if (fs.existsSync(LEGACY_STATE_FILE)) {
    try {
      const legacy = JSON.parse(fs.readFileSync(LEGACY_STATE_FILE, "utf8"));
      if (Array.isArray(legacy.agents)) {
        initial.agents = legacy.agents.map(agent => ({
          ...agent,
          machine: agent.machine || os.hostname(),
          taskId: agent.taskId || null,
          boundary: agent.boundary || null,
          priority: Number.isFinite(Number(agent.priority)) ? Number(agent.priority) : 50
        }));
      }
    } catch {}
  }
  fs.writeFileSync(STATE_FILE, JSON.stringify(initial, null, 2));
}

function loadState() {
  try {
    const parsed = JSON.parse(fs.readFileSync(STATE_FILE, "utf8"));
    return {
      version: STATE_VERSION,
      agents: Array.isArray(parsed.agents) ? parsed.agents : [],
      tasks: Array.isArray(parsed.tasks) ? parsed.tasks : [],
      leases: Array.isArray(parsed.leases) ? parsed.leases : [],
      events: Array.isArray(parsed.events) ? parsed.events : []
    };
  } catch {
    return defaultState();
  }
}

function saveState(state) {
  state.version = STATE_VERSION;
  if (state.events.length > EVENT_LIMIT) state.events = state.events.slice(-EVENT_LIMIT);
  const tmp = `${STATE_FILE}.tmp`;
  fs.writeFileSync(tmp, JSON.stringify(state, null, 2));
  fs.renameSync(tmp, STATE_FILE);
}

function addEvent(state, type, message, extra = {}) {
  state.events.push({
    at: isoNow(),
    type,
    message,
    ...extra
  });
}

function slugify(text, max = 32) {
  return String(text || "task")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, max) || "task";
}

function normalizePriority(value) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) return 50;
  return Math.max(0, Math.min(100, Math.round(parsed)));
}

function findCodex() {
  if (process.env.CODEX_EXE && fs.existsSync(process.env.CODEX_EXE)) return process.env.CODEX_EXE;

  const localAppData = process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local");
  const base = path.join(localAppData, "OpenAI", "Codex", "bin");
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

function codexStatus() {
  try {
    return { available: true, path: findCodex(), error: null };
  } catch (error) {
    return { available: false, path: null, error: error.message || String(error) };
  }
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
    return file && fs.existsSync(file) ? fs.readFileSync(file, "utf8").trim() : "";
  } catch {
    return "";
  }
}

function tailFile(file, maxChars = 18000) {
  try {
    if (!file || !fs.existsSync(file)) return "";
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
  const lines = tailFile(file, 14000).split(/\r?\n/).filter(Boolean).reverse();
  for (const line of lines) {
    try {
      const event = JSON.parse(line);
      const candidates = [
        event?.error?.message,
        event?.message,
        event?.item?.text,
        event?.response?.output_text
      ];
      const message = candidates.find(value => typeof value === "string" && value.trim());
      if (message) return message.trim();
    } catch {}
  }
  return "";
}

async function git(args, cwd = REPO, options = {}) {
  const { stdout, stderr } = await execFileAsync("git", ["-C", cwd, ...args], {
    windowsHide: true,
    maxBuffer: options.maxBuffer || 4 * 1024 * 1024
  });
  return `${stdout || ""}${stderr || ""}`.trim();
}

async function resolveBaseRef(baseBranch) {
  await git(["fetch", "origin", "--prune"]);
  const remoteRef = `refs/remotes/origin/${baseBranch}`;
  try {
    await git(["rev-parse", "--verify", remoteRef]);
    return `origin/${baseBranch}`;
  } catch {}

  const localRef = `refs/heads/${baseBranch}`;
  await git(["rev-parse", "--verify", localRef]);
  return baseBranch;
}

function isTerminalStatus(status) {
  return ["done", "failed", "finished", "stopped"].includes(status);
}

function releaseLeaseForAgent(state, agent, reason) {
  if (!agent?.leaseId) return;
  const lease = state.leases.find(item => item.id === agent.leaseId);
  if (!lease || lease.status !== "active") return;
  lease.status = "released";
  lease.releasedAt = isoNow();
  lease.releaseReason = reason || "agent-finished";
}

function updateTaskForAgent(state, agent) {
  if (!agent?.taskId) return;
  const task = state.tasks.find(item => item.id === agent.taskId);
  if (!task) return;

  task.updatedAt = isoNow();
  task.branchName = agent.branchName;
  if (agent.status === "done") task.status = "candidate";
  else if (agent.status === "failed") task.status = "failed";
  else if (agent.status === "stopped") task.status = "stopped";
  else if (agent.status === "finished") task.status = "finished";
  else task.status = agent.status;

  if (isTerminalStatus(agent.status)) task.finishedAt ||= isoNow();
}

function refreshState() {
  const state = loadState();
  let changed = false;

  for (const agent of state.agents) {
    const alive = isPidAlive(agent.pid);

    if (["running", "starting", "stopping"].includes(agent.status) && !alive) {
      const last = readTextIfExists(agent.lastMessagePath);
      if (agent.status === "stopping") agent.status = "stopped";
      else agent.status = last ? "done" : (agent.exitCode === 0 ? "done" : "finished");
      agent.finishedAt ||= isoNow();
      agent.updatedAt = isoNow();
      releaseLeaseForAgent(state, agent, "process-ended");
      updateTaskForAgent(state, agent);
      addEvent(state, "agent.reconciled", `${agent.id} is no longer running`, { agentId: agent.id, taskId: agent.taskId });
      changed = true;
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

function workerSnapshot(state = refreshState()) {
  const running = state.agents.filter(agent => ["running", "starting", "stopping"].includes(agent.status)).length;
  const hostname = os.hostname();
  return [{
    id: hostname.toLowerCase(),
    name: hostname,
    kind: "local",
    status: "online",
    controller: true,
    lastHeartbeat: isoNow(),
    running,
    capacity: MAX_ACTIVE_AGENTS,
    availableSlots: Math.max(0, MAX_ACTIVE_AGENTS - running),
    cpus: os.cpus()?.length || null,
    totalMemoryBytes: os.totalmem(),
    freeMemoryBytes: os.freemem(),
    platform: process.platform,
    arch: process.arch
  }];
}

function activeLeaseForBoundary(state, boundary) {
  if (!boundary) return null;
  return state.leases.find(lease => lease.status === "active" && lease.boundary.toLowerCase() === boundary.toLowerCase()) || null;
}

function acquireLease(state, { boundary, agentId, taskId, branchName }) {
  const conflict = activeLeaseForBoundary(state, boundary);
  if (conflict) {
    throw new Error(`Mutable boundary "${boundary}" is already leased by ${conflict.ownerAgentId}.`);
  }

  const lease = {
    id: `lease-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
    boundary,
    ownerAgentId: agentId,
    taskId,
    branchName,
    status: "active",
    acquiredAt: isoNow(),
    releasedAt: null,
    releaseReason: null
  };
  state.leases.unshift(lease);
  return lease;
}

function assertWorkerPlacement(machine) {
  const requested = String(machine || "auto").trim().toLowerCase();
  const hostname = os.hostname().toLowerCase();
  if (["", "auto", "local", hostname].includes(requested)) return os.hostname();
  throw new Error(`Worker "${machine}" is not registered on this controller yet. This v1 controller can deploy only on ${os.hostname()}.`);
}

function buildPrompt({ role, task, baseBranch, branchName, taskId, boundary, priority, dependencies = [] }) {
  const preset = rolePresets[role] || rolePresets.support;
  return [
    `You are a ${preset.label} in the user's MHW engineering swarm.`,
    "",
    preset.instructions,
    "",
    "CONTROL-PLANE ASSIGNMENT:",
    `- task id: ${taskId}`,
    `- priority: ${priority}/100`,
    `- mutable boundary lease: ${boundary}`,
    `- base branch: ${baseBranch}`,
    `- working branch: ${branchName}`,
    `- dependencies: ${dependencies.length ? dependencies.join(", ") : "none declared"}`,
    "",
    "Hard requirements:",
    "- Work only in the worktree you were given. Never edit another agent's worktree.",
    "- Never switch, reset, or force-update main.",
    "- Read AGENTS.md, NEXT-AGENT-START-HERE.md, _AGENT_TRAINING/README.md, and the relevant _AGENT_CONTEXT files before meaningful edits.",
    "- Re-establish current repository truth instead of trusting stale chat or branch notes.",
    "- Respect the mutable-boundary lease. Do not silently expand into another agent's implementation surface.",
    "- Search for active/relevant branches before duplicating expensive work.",
    "- Keep scope tight and one independently verifiable architecture boundary at a time.",
    "- Run targeted verification for the exact source you changed.",
    "- Commit and push meaningful checkpoints frequently enough that interruption cannot erase substantial work.",
    "- Do not claim verification you did not actually run.",
    "- If incomplete, leave a precise durable handoff with what is done, what remains, blockers, exact branch/SHA, and exact verification.",
    "",
    "USER TASK:",
    task.trim()
  ].join("\n");
}

async function deployOne({ role, task, baseBranch, model, boundary, priority, machine, dependencies = [], targetAgentId = null }) {
  if (!rolePresets[role]) throw new Error(`Unknown role: ${role}`);
  if (!task || !task.trim()) throw new Error("Task is required.");

  const state = refreshState();
  const running = state.agents.filter(agent => ["running", "starting", "stopping"].includes(agent.status)).length;
  if (running >= MAX_ACTIVE_AGENTS) throw new Error(`Worker capacity reached (${running}/${MAX_ACTIVE_AGENTS}).`);

  const assignedMachine = assertWorkerPlacement(machine);
  const base = (baseBranch || "main").trim();
  const baseRef = await resolveBaseRef(base);

  const stamp = new Date().toISOString().replace(/[-:TZ.]/g, "").slice(0, 14);
  const suffix = Math.random().toString(36).slice(2, 7);
  const id = `${role}-${stamp}-${suffix}`;
  const taskId = `task-${stamp}-${suffix}`;
  const branchName = `agent/control-${role}-${slugify(task)}-${stamp}-${suffix}`;
  const mutableBoundary = (boundary || "").trim() || `isolated:${branchName}`;
  const normalizedPriority = normalizePriority(priority);

  const worktree = path.join(WORKTREE_ROOT, id);
  const logPath = path.join(DATA_DIR, `${id}.jsonl`);
  const lastMessagePath = path.join(DATA_DIR, `${id}.last.txt`);

  const currentState = loadState();
  const lease = acquireLease(currentState, {
    boundary: mutableBoundary,
    agentId: id,
    taskId,
    branchName
  });

  const taskRecord = {
    id: taskId,
    objective: task.trim(),
    role,
    roleLabel: rolePresets[role].label,
    status: "starting",
    priority: normalizedPriority,
    boundary: mutableBoundary,
    requestedAt: isoNow(),
    updatedAt: isoNow(),
    startedAt: null,
    finishedAt: null,
    agentId: id,
    targetAgentId,
    machine: assignedMachine,
    baseBranch: base,
    branchName,
    dependencies: Array.isArray(dependencies) ? dependencies.filter(Boolean) : []
  };
  currentState.tasks.unshift(taskRecord);
  addEvent(currentState, "task.created", `${taskId} assigned to ${id}`, { agentId: id, taskId });
  saveState(currentState);

  try {
    await git(["worktree", "add", "-b", branchName, worktree, baseRef]);
  } catch (error) {
    const failed = loadState();
    const taskItem = failed.tasks.find(item => item.id === taskId);
    if (taskItem) {
      taskItem.status = "failed";
      taskItem.finishedAt = isoNow();
      taskItem.updatedAt = isoNow();
      taskItem.error = error.message || String(error);
    }
    const leaseItem = failed.leases.find(item => item.id === lease.id);
    if (leaseItem) {
      leaseItem.status = "released";
      leaseItem.releasedAt = isoNow();
      leaseItem.releaseReason = "worktree-create-failed";
    }
    addEvent(failed, "task.failed", `Failed to create worktree for ${taskId}`, { taskId });
    saveState(failed);
    throw error;
  }

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

  const prompt = buildPrompt({
    role,
    task,
    baseBranch: base,
    branchName,
    taskId,
    boundary: mutableBoundary,
    priority: normalizedPriority,
    dependencies: taskRecord.dependencies
  });
  child.stdin.end(prompt);
  fs.closeSync(logFd);

  const agent = {
    id,
    role,
    roleLabel: rolePresets[role].label,
    taskId,
    task: task.trim(),
    targetAgentId,
    status: "running",
    pid: child.pid,
    machine: assignedMachine,
    baseBranch: base,
    branchName,
    worktree,
    leaseId: lease.id,
    boundary: mutableBoundary,
    priority: normalizedPriority,
    dependencies: taskRecord.dependencies,
    logPath,
    lastMessagePath,
    lastMessage: "",
    model: model?.trim() || null,
    startedAt: isoNow(),
    updatedAt: isoNow(),
    finishedAt: null,
    exitCode: null
  };

  const startedState = loadState();
  startedState.agents.unshift(agent);
  const startedTask = startedState.tasks.find(item => item.id === taskId);
  if (startedTask) {
    startedTask.status = "running";
    startedTask.startedAt = agent.startedAt;
    startedTask.updatedAt = agent.updatedAt;
  }
  addEvent(startedState, "agent.started", `${id} started on ${assignedMachine}`, { agentId: id, taskId });
  saveState(startedState);
  children.set(id, child);

  child.on("exit", (code, signal) => {
    const current = loadState();
    const item = current.agents.find(candidate => candidate.id === id);
    if (item) {
      item.exitCode = code;
      item.signal = signal || null;
      item.status = code === 0 ? "done" : (item.status === "stopping" ? "stopped" : "failed");
      item.finishedAt = isoNow();
      item.updatedAt = isoNow();
      item.lastMessage = readTextIfExists(item.lastMessagePath) || readLogSummary(item.logPath);
      releaseLeaseForAgent(current, item, `process-exit:${code ?? "unknown"}`);
      updateTaskForAgent(current, item);
      addEvent(current, "agent.exited", `${id} exited with ${code ?? "unknown"}`, { agentId: id, taskId });
      saveState(current);
    }
    children.delete(id);
  });

  child.on("error", error => {
    const current = loadState();
    const item = current.agents.find(candidate => candidate.id === id);
    if (item) {
      item.status = "failed";
      item.error = error.message;
      item.finishedAt = isoNow();
      item.updatedAt = isoNow();
      releaseLeaseForAgent(current, item, "spawn-error");
      updateTaskForAgent(current, item);
      addEvent(current, "agent.error", error.message, { agentId: id, taskId });
      saveState(current);
    }
    children.delete(id);
  });

  return agent;
}

async function killProcessTree(pid) {
  if (process.platform === "win32") {
    await new Promise(resolve => {
      const killer = spawn("taskkill", ["/PID", String(pid), "/T", "/F"], {
        windowsHide: true,
        stdio: "ignore"
      });
      killer.on("exit", resolve);
      killer.on("error", resolve);
    });
    return;
  }

  try { process.kill(pid, "SIGTERM"); } catch {}
}

async function stopAgent(id) {
  const state = refreshState();
  const agent = state.agents.find(item => item.id === id);
  if (!agent) throw new Error("Agent not found.");

  if (!isPidAlive(agent.pid)) {
    if (["running", "starting", "stopping"].includes(agent.status)) agent.status = "finished";
    agent.finishedAt ||= isoNow();
    agent.updatedAt = isoNow();
    releaseLeaseForAgent(state, agent, "already-not-running");
    updateTaskForAgent(state, agent);
    saveState(state);
    return agent;
  }

  agent.status = "stopping";
  agent.updatedAt = isoNow();
  updateTaskForAgent(state, agent);
  addEvent(state, "agent.stopping", `Stop requested for ${id}`, { agentId: id, taskId: agent.taskId });
  saveState(state);

  await killProcessTree(agent.pid);

  const stopped = loadState();
  const item = stopped.agents.find(candidate => candidate.id === id);
  if (item) {
    item.status = "stopped";
    item.finishedAt = isoNow();
    item.updatedAt = isoNow();
    releaseLeaseForAgent(stopped, item, "user-stop");
    updateTaskForAgent(stopped, item);
    addEvent(stopped, "agent.stopped", `${id} stopped`, { agentId: id, taskId: item.taskId });
  }
  saveState(stopped);
  children.delete(id);
  return item || agent;
}

async function branchDivergence(agent) {
  try {
    const baseRef = await resolveBaseRef(agent.baseBranch || "main");
    const raw = await git(["rev-list", "--left-right", "--count", `${baseRef}...${agent.branchName}`]);
    const [behindRaw, aheadRaw] = raw.split(/\s+/);
    const behind = Number(behindRaw || 0);
    const ahead = Number(aheadRaw || 0);
    return {
      ahead,
      behind,
      state: ahead > 0 ? "candidate" : "no-change"
    };
  } catch (error) {
    return {
      ahead: null,
      behind: null,
      state: "unknown",
      error: error.message || String(error)
    };
  }
}

async function integrationQueue(state = refreshState()) {
  const candidates = state.agents.filter(agent => isTerminalStatus(agent.status));
  const queue = [];
  for (const agent of candidates) {
    const divergence = await branchDivergence(agent);
    queue.push({
      agentId: agent.id,
      taskId: agent.taskId,
      role: agent.role,
      task: agent.task,
      status: agent.status,
      branchName: agent.branchName,
      baseBranch: agent.baseBranch,
      machine: agent.machine,
      finishedAt: agent.finishedAt,
      lastMessage: agent.lastMessage || "",
      ...divergence
    });
  }
  return queue.sort((a, b) => String(b.finishedAt || "").localeCompare(String(a.finishedAt || "")));
}

async function observedBranches(state = refreshState()) {
  try {
    const output = await git([
      "for-each-ref",
      "--format=%(refname:short)|%(objectname:short)|%(committerdate:iso8601)",
      "refs/remotes/origin/agent",
      "refs/remotes/origin/support",
      "refs/remotes/origin/feature",
      "refs/remotes/origin/ui",
      "refs/remotes/origin/integration"
    ]);
    if (!output) return [];

    const managedByBranch = new Map(state.agents.map(agent => [agent.branchName, agent]));
    return output.split(/\r?\n/).filter(Boolean).map(line => {
      const [ref, sha, committedAt] = line.split("|");
      const branchName = ref.replace(/^origin\//, "");
      const managed = managedByBranch.get(branchName);
      return {
        branchName,
        sha,
        committedAt,
        managed: Boolean(managed),
        agentId: managed?.id || null,
        status: managed?.status || "observed",
        task: managed?.task || null
      };
    });
  } catch {
    return [];
  }
}

function telemetry(state, queue) {
  const agents = state.agents;
  const running = agents.filter(agent => ["running", "starting", "stopping"].includes(agent.status)).length;
  const done = agents.filter(agent => agent.status === "done").length;
  const failed = agents.filter(agent => agent.status === "failed").length;
  const stopped = agents.filter(agent => agent.status === "stopped").length;
  const finished = agents.filter(agent => isTerminalStatus(agent.status));
  const completedWithOutcome = done + failed;
  const successRate = completedWithOutcome ? Math.round((done / completedWithOutcome) * 1000) / 10 : null;
  const durations = finished
    .filter(agent => agent.startedAt && agent.finishedAt)
    .map(agent => new Date(agent.finishedAt).getTime() - new Date(agent.startedAt).getTime())
    .filter(value => Number.isFinite(value) && value >= 0);
  const averageRuntimeMs = durations.length ? Math.round(durations.reduce((sum, value) => sum + value, 0) / durations.length) : null;

  return {
    running,
    done,
    failed,
    stopped,
    total: agents.length,
    successRate,
    averageRuntimeMs,
    activeLeases: state.leases.filter(lease => lease.status === "active").length,
    integrationCandidates: queue.filter(item => item.state === "candidate").length,
    noChangeCompleted: queue.filter(item => item.state === "no-change").length
  };
}

async function buildSnapshot({ fetchRemote = false } = {}) {
  if (fetchRemote) {
    try { await git(["fetch", "origin", "--prune"]); } catch {}
  }

  const state = refreshState();
  const [queue, branches] = await Promise.all([
    integrationQueue(state),
    observedBranches(state)
  ]);
  const workers = workerSnapshot(state);

  return {
    ok: true,
    generatedAt: isoNow(),
    controller: {
      host: os.hostname(),
      bindHost: HOST,
      port: PORT,
      repo: REPO,
      worktreeRoot: WORKTREE_ROOT,
      codex: codexStatus(),
      stateVersion: STATE_VERSION
    },
    telemetry: telemetry(state, queue),
    workers,
    tasks: state.tasks,
    agents: state.agents,
    leases: state.leases,
    integrationQueue: queue,
    observedBranches: branches,
    recentEvents: state.events.slice(-80).reverse(),
    roles: rolePresets
  };
}

async function deployReview(targetAgentId, body = {}) {
  const state = refreshState();
  const target = state.agents.find(agent => agent.id === targetAgentId);
  if (!target) throw new Error("Target agent not found.");

  const task = body.task?.trim() || [
    `Review the work produced by agent ${target.id}.`,
    `Target task: ${target.task}`,
    `Target branch: ${target.branchName}`,
    "Re-establish canonical repository truth, inspect the target diff and verification evidence, identify correctness/safety/integration issues, and commit only justified review fixes."
  ].join("\n");

  return deployOne({
    role: "reviewer",
    task,
    baseBranch: target.branchName,
    model: body.model || "",
    boundary: body.boundary || `review:${target.branchName}`,
    priority: body.priority ?? Math.max(60, Number(target.priority || 50)),
    machine: body.machine || "auto",
    dependencies: [target.taskId].filter(Boolean),
    targetAgentId: target.id
  });
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
      if (body.length > 1024 * 1024) {
        reject(new Error("Request body too large."));
        req.destroy();
      }
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

function serveStatic(res, pathname) {
  const relative = pathname === "/" ? "index.html" : pathname.replace(/^\/+/, "");
  const file = path.resolve(PUBLIC_DIR, relative);
  const rel = path.relative(PUBLIC_DIR, file);
  if (rel.startsWith("..") || path.isAbsolute(rel) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) return false;

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
  if (!origin) return true;
  const allowed = new Set([
    `http://${HOST}:${PORT}`,
    `http://localhost:${PORT}`,
    `http://127.0.0.1:${PORT}`
  ]);
  return allowed.has(origin);
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, `http://${HOST}:${PORT}`);
    const pathname = decodeURIComponent(url.pathname);

    if (!allowedOrigin(req)) return sendJson(res, 403, { error: "Origin not allowed." });

    if (req.method === "GET" && pathname === "/api/status") {
      const snapshot = await buildSnapshot();
      return sendJson(res, 200, {
        ok: true,
        generatedAt: snapshot.generatedAt,
        controller: snapshot.controller,
        telemetry: snapshot.telemetry,
        workers: snapshot.workers,
        roles: snapshot.roles
      });
    }

    if (req.method === "GET" && pathname === "/api/snapshot") {
      return sendJson(res, 200, await buildSnapshot());
    }

    if (req.method === "POST" && pathname === "/api/sync") {
      return sendJson(res, 200, await buildSnapshot({ fetchRemote: true }));
    }

    if (req.method === "GET" && pathname === "/api/agents") {
      return sendJson(res, 200, refreshState().agents);
    }

    if (req.method === "GET" && pathname === "/api/tasks") {
      return sendJson(res, 200, refreshState().tasks);
    }

    if (req.method === "GET" && pathname === "/api/leases") {
      return sendJson(res, 200, refreshState().leases);
    }

    if (req.method === "GET" && pathname === "/api/workers") {
      return sendJson(res, 200, workerSnapshot());
    }

    if (req.method === "GET" && pathname === "/api/integration") {
      return sendJson(res, 200, await integrationQueue());
    }

    if (req.method === "GET" && pathname === "/api/branches") {
      return sendJson(res, 200, await observedBranches());
    }

    if (req.method === "POST" && pathname === "/api/deploy") {
      const body = await readJson(req);
      const count = Math.max(1, Math.min(MAX_DEPLOY_COUNT, Number(body.count || 1)));
      const created = [];
      for (let index = 0; index < count; index += 1) {
        const explicitBoundary = String(body.boundary || "").trim();
        const effectiveBoundary = explicitBoundary && count > 1 ? `${explicitBoundary}#${index + 1}` : explicitBoundary;
        created.push(await deployOne({
          role: body.role || "support",
          task: body.task || "",
          baseBranch: body.baseBranch || "main",
          model: body.model || "",
          boundary: effectiveBoundary,
          priority: body.priority,
          machine: body.machine || "auto",
          dependencies: Array.isArray(body.dependencies) ? body.dependencies : []
        }));
      }
      return sendJson(res, 201, { agents: created });
    }

    const reviewMatch = pathname.match(/^\/api\/agents\/([^/]+)\/review$/);
    if (req.method === "POST" && reviewMatch) {
      const body = await readJson(req);
      return sendJson(res, 201, { agent: await deployReview(reviewMatch[1], body) });
    }

    const stopMatch = pathname.match(/^\/api\/agents\/([^/]+)\/stop$/);
    if (req.method === "POST" && stopMatch) {
      return sendJson(res, 200, await stopAgent(stopMatch[1]));
    }

    const logMatch = pathname.match(/^\/api\/agents\/([^/]+)\/log$/);
    if (req.method === "GET" && logMatch) {
      const state = refreshState();
      const agent = state.agents.find(item => item.id === logMatch[1]);
      if (!agent) return sendJson(res, 404, { error: "Agent not found." });
      return sendJson(res, 200, {
        id: agent.id,
        taskId: agent.taskId,
        status: agent.status,
        branchName: agent.branchName,
        boundary: agent.boundary,
        lastMessage: readTextIfExists(agent.lastMessagePath) || readLogSummary(agent.logPath),
        tail: tailFile(agent.logPath)
      });
    }

    if (req.method === "GET" && serveStatic(res, pathname)) return;
    return sendJson(res, 404, { error: "Not found." });
  } catch (error) {
    return sendJson(res, 500, { error: error.message || String(error) });
  }
});

server.listen(PORT, HOST, () => {
  console.log(`Heaven Agent Control Plane listening on http://${HOST}:${PORT}`);
  console.log(`Repo: ${REPO}`);
  console.log(`Worktrees: ${WORKTREE_ROOT}`);
  console.log(`Capacity: ${MAX_ACTIVE_AGENTS} active agents`);
});
