import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { fileURLToPath } from "node:url";
import { spawn, execFile } from "node:child_process";
import { promisify } from "node:util";
import { randomUUID } from "node:crypto";
import { renderAgentPrompt } from "./lib/prompt-templates.mjs";
import {
  STATE_VERSION,
  AUTONOMY_PROFILES,
  WORKFLOW_PRESETS,
  applyPreLaunchFailure,
  canUseMachineForRepositoryWrite,
  classifyAuthoritativeExit,
  buildTaskGraph,
  defaultControlState,
  deploymentBatchCapacity,
  deriveMission,
  interpretCommand,
  isActiveStatus as coreIsActiveStatus,
  isIntegrationEligible,
  migrateControlState,
  planWorkflow,
  recommendNextActions,
  roleCatalog,
  takeoverContext
} from "./lib/control-core.mjs";

const execFileAsync = promisify(execFile);
const HERE = path.dirname(fileURLToPath(import.meta.url));
const PUBLIC_DIR = path.join(HERE, "public");
const DATA_DIR = process.env.AGENT_CONTROL_DATA_DIR || path.join(HERE, "data");
const STATE_FILE = path.join(DATA_DIR, "control-plane.json");
const STATE_BACKUP_FILE = path.join(DATA_DIR, "control-plane.json.bak");
const LEGACY_STATE_FILE = path.join(DATA_DIR, "agents.json");

const PORT = Number(process.env.AGENT_CONTROL_PORT || 7331);
const HOST = process.env.AGENT_CONTROL_HOST || "127.0.0.1";
const REPO = process.env.AGENT_CONTROL_REPO || path.join(os.homedir(), "local-ai-workspaces", "mhw-mods");
const WORKTREE_ROOT = process.env.AGENT_WORKTREE_ROOT || path.join(os.homedir(), "agent-worktrees");
const MAX_DEPLOY_COUNT = Number(process.env.AGENT_CONTROL_MAX_DEPLOY_COUNT || 8);
const MAX_ACTIVE_AGENTS = Number(process.env.AGENT_CONTROL_MAX_ACTIVE || 8);
const EVENT_LIMIT = 1000;
const NOTIFICATION_LIMIT = 250;
const LEASE_TTL_MS = Number(process.env.AGENT_CONTROL_LEASE_TTL_MS || 120000);
const STALE_PROGRESS_MS = Number(process.env.AGENT_CONTROL_STALE_PROGRESS_MS || 900000);
const SESSION_ID = randomUUID();
const children = new Map();
let degradedReason = null;
let deployMutex = Promise.resolve();

const rolePresets = roleCatalog();

function isoNow() {
  return new Date().toISOString();
}

function defaultState() {
  return defaultControlState({ sessionId: SESSION_ID, hostname: os.hostname() });
}

function parseStateFile(file) {
  const parsed = JSON.parse(fs.readFileSync(file, "utf8"));
  return migrateControlState(parsed, { sessionId: SESSION_ID, hostname: os.hostname() });
}

fs.mkdirSync(DATA_DIR, { recursive: true });
fs.mkdirSync(WORKTREE_ROOT, { recursive: true });

if (!fs.existsSync(STATE_FILE)) {
  const initial = defaultState();
  if (fs.existsSync(LEGACY_STATE_FILE)) {
    try {
      const legacy = JSON.parse(fs.readFileSync(LEGACY_STATE_FILE, "utf8"));
      if (Array.isArray(legacy.agents)) {
        initial.agents = legacy.agents.map(agent => ({
          ...agent,
          machine: agent.machine || os.hostname(),
          taskId: agent.taskId || null,
          boundary: agent.boundary || null,
          priority: Number.isFinite(Number(agent.priority)) ? Number(agent.priority) : 50,
          status: coreIsActiveStatus(agent.status) ? "orphaned" : agent.status,
          completionEvidence: agent.completionEvidence || null
        }));
      }
    } catch {}
  }
  fs.writeFileSync(STATE_FILE, JSON.stringify(initial, null, 2));
  fs.copyFileSync(STATE_FILE, STATE_BACKUP_FILE);
}

function loadState() {
  try {
    const state = parseStateFile(STATE_FILE);
    degradedReason = null;
    return state;
  } catch (primaryError) {
    try {
      const state = parseStateFile(STATE_BACKUP_FILE);
      degradedReason = null;
      state.health.mode = "recovered";
      state.health.recoveredFromBackupAt = isoNow();
      state.health.degradedReason = `Primary state was unreadable and backup was used: ${primaryError.message || primaryError}`;
      return state;
    } catch (backupError) {
      degradedReason = `Control-plane state is unreadable. Primary: ${primaryError.message || primaryError}; backup: ${backupError.message || backupError}`;
      const state = defaultState();
      state.health.mode = "degraded";
      state.health.degradedReason = degradedReason;
      state.settings.readOnly = true;
      state.settings.dispatchPaused = true;
      return state;
    }
  }
}

function saveState(state) {
  if (degradedReason) throw new Error(`State registry is degraded; refusing mutation: ${degradedReason}`);
  state.version = STATE_VERSION;
  state.controller = { ...(state.controller || {}), sessionId: SESSION_ID, hostname: os.hostname() };
  if (state.events.length > EVENT_LIMIT) state.events = state.events.slice(-EVENT_LIMIT);
  if (state.notifications.length > NOTIFICATION_LIMIT) state.notifications = state.notifications.slice(-NOTIFICATION_LIMIT);
  const tmp = `${STATE_FILE}.tmp`;
  const serialized = JSON.stringify(state, null, 2);
  JSON.parse(serialized);
  fs.writeFileSync(tmp, serialized);
  try {
    JSON.parse(fs.readFileSync(STATE_FILE, "utf8"));
    fs.copyFileSync(STATE_FILE, STATE_BACKUP_FILE);
  } catch {}
  fs.renameSync(tmp, STATE_FILE);
}

function assertMutationsAllowed(state, { dispatch = false, safetyControl = false } = {}) {
  if (degradedReason || state.health?.mode === "degraded") throw new Error("Control-plane state is degraded; recover authoritative state before mutation.");
  if (!safetyControl && state.settings?.readOnly) throw new Error("Control plane is in read-only monitoring mode.");
  if (dispatch && state.settings?.emergencyStop) throw new Error("Emergency stop is active; autonomous dispatch is disabled.");
  if (dispatch && (state.settings?.dispatchPaused || state.settings?.draining)) throw new Error("Dispatch is paused.");
}

async function withDeployLock(fn) {
  const previous = deployMutex;
  let release;
  deployMutex = new Promise(resolve => { release = resolve; });
  await previous;
  try {
    return await fn();
  } finally {
    release();
  }
}

function addEvent(state, type, message, extra = {}) {
  state.events.push({
    at: isoNow(),
    type,
    message,
    ...extra
  });
}

function addNotification(state, { severity = "info", title, message, action = null, dedupeKey = null }) {
  if (dedupeKey && state.notifications.some(item => item.dedupeKey === dedupeKey && !item.dismissedAt)) return;
  state.notifications.push({
    id: `notification-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
    createdAt: isoNow(),
    severity,
    title,
    message,
    action,
    dedupeKey,
    dismissedAt: null
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

async function resolveLocalBaseRef(baseBranch) {
  const remoteRef = `refs/remotes/origin/${baseBranch}`;
  try {
    await git(["rev-parse", "--verify", remoteRef]);
    return `origin/${baseBranch}`;
  } catch {}
  const localRef = `refs/heads/${baseBranch}`;
  await git(["rev-parse", "--verify", localRef]);
  return baseBranch;
}

async function remoteHeadSha(branch = "main") {
  try {
    const output = await execFileAsync("git", ["-C", REPO, "ls-remote", "origin", `refs/heads/${branch}`], {
      windowsHide: true,
      maxBuffer: 1024 * 1024
    });
    return String(output.stdout || "").trim().split(/\s+/)[0] || null;
  } catch {
    return null;
  }
}

function isTerminalStatus(status) {
  return ["done", "failed", "finished", "stopped", "interrupted", "orphaned"].includes(status);
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
  const now = Date.now();

  for (const agent of state.agents) {
    const last = readTextIfExists(agent.lastMessagePath) || readLogSummary(agent.logPath);
    if (last && last !== agent.lastMessage) {
      agent.lastMessage = last;
      agent.lastProgressAt = isoNow();
      agent.updatedAt = isoNow();
      changed = true;
    }

    if (!coreIsActiveStatus(agent.status)) continue;

    if (agent.ownerSessionId !== SESSION_ID || !children.has(agent.id)) {
      if (agent.status !== "orphaned") {
        agent.status = "orphaned";
        agent.updatedAt = isoNow();
        agent.interruptedAt = isoNow();
        agent.completionEvidence = null;
        updateTaskForAgent(state, agent);
        addEvent(state, "agent.orphaned", `${agent.id} requires restart reconciliation; process ownership is not proven`, {
          agentId: agent.id,
          taskId: agent.taskId,
          reason: "controller-session-mismatch"
        });
        addNotification(state, {
          severity: "warning",
          title: "Worker needs reconciliation",
          message: `${agent.roleLabel || agent.id} survived in durable state but this controller session cannot prove ownership of PID ${agent.pid || "unknown"}.`,
          action: { type: "inspect-agent", agentId: agent.id },
          dedupeKey: `orphaned:${agent.id}`
        });
        changed = true;
      }
      continue;
    }

    const child = children.get(agent.id);
    const alive = child?.pid === agent.pid && isPidAlive(agent.pid);
    if (!alive) {
      if (agent.status !== "stopping") {
        agent.status = "interrupted";
        agent.interruptedAt = isoNow();
        agent.updatedAt = isoNow();
        agent.completionEvidence = null;
        updateTaskForAgent(state, agent);
        addEvent(state, "agent.interrupted", `${agent.id} disappeared before an authoritative exit event`, {
          agentId: agent.id,
          taskId: agent.taskId,
          reason: "owned-process-missing-without-exit-event"
        });
        addNotification(state, {
          severity: "warning",
          title: "Worker interrupted",
          message: `${agent.roleLabel || agent.id} stopped without authoritative completion evidence. Its lease is preserved.`,
          action: { type: "generate-takeover", agentId: agent.id },
          dedupeKey: `interrupted:${agent.id}`
        });
        changed = true;
      }
      continue;
    }

    const heartbeatAt = isoNow();
    agent.heartbeatAt = heartbeatAt;
    const lease = state.leases.find(item => item.id === agent.leaseId);
    if (lease && lease.status === "active") {
      lease.heartbeatAt = heartbeatAt;
      lease.expiresAt = new Date(now + LEASE_TTL_MS).toISOString();
    }

    const progressAt = new Date(agent.lastProgressAt || agent.startedAt || heartbeatAt).getTime();
    const stalled = Number.isFinite(progressAt) && now - progressAt > STALE_PROGRESS_MS;
    if (stalled && agent.status === "running") {
      agent.status = "stale";
      agent.updatedAt = heartbeatAt;
      addEvent(state, "agent.stale", `${agent.id} has made no recorded progress within the stale threshold`, {
        agentId: agent.id,
        taskId: agent.taskId,
        reason: "progress-timeout"
      });
      addNotification(state, {
        severity: "warning",
        title: "Worker appears stale",
        message: `${agent.roleLabel || agent.id} is alive but has not produced recorded progress recently. Its lease remains held.`,
        action: { type: "inspect-agent", agentId: agent.id },
        dedupeKey: `stale:${agent.id}`
      });
    } else if (!stalled && agent.status === "stale") {
      agent.status = "running";
      agent.updatedAt = heartbeatAt;
      addEvent(state, "agent.resumed", `${agent.id} produced progress after being marked stale`, { agentId: agent.id, taskId: agent.taskId });
    }
    changed = true;
  }

  if (changed && !degradedReason) saveState(state);
  return state;
}

function workerSnapshot(state = refreshState()) {
  const running = state.agents.filter(agent => coreIsActiveStatus(agent.status)).length;
  const hostname = os.hostname();
  const currentId = hostname.toLowerCase();
  const policies = state.settings?.machinePolicies || {};
  const configured = new Set(Object.keys(policies).map(value => value.toLowerCase()));
  configured.add(currentId);

  return [...configured].map(id => {
    const policy = policies[id] || null;
    if (id !== currentId) {
      return {
        id,
        name: policy?.label || id,
        kind: "configured",
        status: "not-connected",
        controller: false,
        lastHeartbeat: null,
        running: null,
        capacity: null,
        availableSlots: null,
        policy
      };
    }
    return {
      id,
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
      arch: process.arch,
      policy
    };
  });
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

  const acquiredAt = isoNow();
  const lease = {
    id: `lease-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
    boundary,
    ownerAgentId: agentId,
    taskId,
    branchName,
    status: "active",
    acquiredAt,
    heartbeatAt: acquiredAt,
    expiresAt: new Date(Date.now() + LEASE_TTL_MS).toISOString(),
    releasedAt: null,
    releaseReason: null
  };
  state.leases.unshift(lease);
  return lease;
}

function failReservedDeployment({ taskId, leaseId, error, reason, worktree = null, branchName = null }) {
  const failed = loadState();
  const retainedWorktree = worktree && fs.existsSync(worktree) ? worktree : null;
  const converged = applyPreLaunchFailure(failed, {
    taskId,
    leaseId,
    error: error?.message || String(error),
    reason,
    at: isoNow(),
    retainedWorktree,
    retainedBranch: branchName
  });
  addEvent(failed, "task.failed", `Deployment failed before a worker process was launched for ${taskId}`, {
    taskId,
    reason,
    evidence: {
      error: error?.message || String(error),
      retainedWorktree: converged.task?.retainedWorktree || null,
      retainedBranch: converged.task?.retainedBranch || branchName || null
    }
  });
  addNotification(failed, {
    severity: "error",
    title: "Agent deployment failed before launch",
    message: error?.message || String(error),
    action: { type: "inspect-task", taskId },
    dedupeKey: `prelaunch-failed:${taskId}`
  });
  saveState(failed);
}

function assertWorkerPlacement(state, machine, { repositoryWriteAuthorized = false } = {}) {
  const requested = String(machine || "auto").trim().toLowerCase();
  const hostname = os.hostname().toLowerCase();
  const target = ["", "auto", "local"].includes(requested) ? hostname : requested;
  if (target !== hostname) {
    throw new Error(`Worker "${target}" is configured but no authenticated remote worker provider is connected. Refusing to fake remote execution.`);
  }
  const permission = canUseMachineForRepositoryWrite(state, target, repositoryWriteAuthorized);
  if (!permission.allowed) {
    throw new Error(`Repository-writing task blocked by machine policy on ${target}: ${permission.reason}`);
  }
  return os.hostname();
}

function buildPrompt({
  role,
  task,
  baseBranch,
  baseSha,
  branchName,
  taskId,
  boundary,
  priority,
  dependencies = [],
  lane = null,
  machine,
  repositoryWriteAuthorized = false,
  acceptanceCriteria = [],
  verification = [],
  additionalConstraints = []
}) {
  return renderAgentPrompt({
    role,
    task,
    lane,
    dependencies,
    machine,
    repositoryWriteAuthorized,
    acceptanceCriteria,
    verification,
    additionalConstraints,
    assignment: { taskId, priority, boundary, baseBranch, baseSha, branchName }
  });
}

async function deployOne({
  role,
  task,
  baseBranch,
  model,
  boundary,
  priority,
  machine,
  dependencies = [],
  targetAgentId = null,
  lane = null,
  repositoryWriteAuthorized = false,
  acceptanceCriteria = [],
  verification = [],
  additionalConstraints = []
}) {
  if (!rolePresets[role]) throw new Error(`Unknown role: ${role}`);
  if (!task || !task.trim()) throw new Error("Task is required.");

  const state = refreshState();
  assertMutationsAllowed(state, { dispatch: true });
  const running = state.agents.filter(agent => coreIsActiveStatus(agent.status)).length;
  if (running >= MAX_ACTIVE_AGENTS) throw new Error(`Worker capacity reached (${running}/${MAX_ACTIVE_AGENTS}).`);

  const assignedMachine = assertWorkerPlacement(state, machine, { repositoryWriteAuthorized });
  const base = (baseBranch || "main").trim();
  const baseRef = await resolveBaseRef(base);
  const baseSha = await git(["rev-parse", baseRef]);

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
  const promptPath = path.join(DATA_DIR, `${id}.prompt.txt`);

  const currentState = loadState();
  assertMutationsAllowed(currentState, { dispatch: true });
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
    status: "reserved",
    priority: normalizedPriority,
    lane,
    boundary: mutableBoundary,
    requestedAt: isoNow(),
    updatedAt: isoNow(),
    startedAt: null,
    finishedAt: null,
    agentId: id,
    targetAgentId,
    machine: assignedMachine,
    baseBranch: base,
    baseSha,
    branchName,
    dependencies: Array.isArray(dependencies) ? dependencies.filter(Boolean) : [],
    acceptanceCriteria: Array.isArray(acceptanceCriteria) ? acceptanceCriteria.filter(Boolean) : [],
    verification: Array.isArray(verification) ? verification.filter(Boolean) : [],
    evidence: [],
    blockers: [],
    nextAction: null,
    promptTemplateId: null,
    promptTemplateVersion: null,
    promptHash: null
  };
  currentState.tasks.unshift(taskRecord);
  addEvent(currentState, "task.created", `${taskId} assigned to ${id}`, { agentId: id, taskId });
  saveState(currentState);

  try {
    await git(["worktree", "add", "-b", branchName, worktree, baseRef]);
  } catch (error) {
    failReservedDeployment({
      taskId,
      leaseId: lease.id,
      error,
      reason: "worktree-create-failed",
      branchName
    });
    throw error;
  }

  let codex;
  let prompt;
  let logFd = null;
  try {
    codex = findCodex();
    prompt = buildPrompt({
      role,
      task,
      baseBranch: base,
      baseSha,
      branchName,
      taskId,
      boundary: mutableBoundary,
      priority: normalizedPriority,
      dependencies: taskRecord.dependencies,
      lane,
      machine: assignedMachine,
      repositoryWriteAuthorized,
      acceptanceCriteria: taskRecord.acceptanceCriteria,
      verification: taskRecord.verification,
      additionalConstraints
    });
    fs.writeFileSync(promptPath, prompt.rendered, "utf8");
    logFd = fs.openSync(logPath, "a");

    const promptedState = loadState();
    const promptedTask = promptedState.tasks.find(item => item.id === taskId);
    if (promptedTask) {
      promptedTask.status = "starting";
      promptedTask.promptTemplateId = prompt.templateId;
      promptedTask.promptTemplateVersion = prompt.templateVersion;
      promptedTask.promptHash = prompt.sha256;
      promptedTask.updatedAt = isoNow();
    }
    promptedState.promptHistory.unshift({
      taskId,
      agentId: id,
      templateId: prompt.templateId,
      templateVersion: prompt.templateVersion,
      sha256: prompt.sha256,
      promptPath,
      createdAt: isoNow()
    });
    saveState(promptedState);
  } catch (error) {
    if (logFd !== null) {
      try { fs.closeSync(logFd); } catch {}
    }
    failReservedDeployment({
      taskId,
      leaseId: lease.id,
      error,
      reason: "pre-launch-setup-failed",
      worktree,
      branchName
    });
    throw error;
  }

  const args = [
    "exec",
    "--json",
    "--approve-for-me",
    "-C", worktree,
    "-o", lastMessagePath
  ];
  if (model && model.trim()) args.push("-m", model.trim());
  args.push("-");

  let child;
  try {
    child = spawn(codex, args, {
      cwd: worktree,
      stdio: ["pipe", logFd, logFd],
      windowsHide: true,
      detached: false,
      env: { ...process.env }
    });
  } catch (error) {
    if (logFd !== null) {
      try { fs.closeSync(logFd); } catch {}
    }
    failReservedDeployment({
      taskId,
      leaseId: lease.id,
      error,
      reason: "spawn-threw-before-process-launch",
      worktree,
      branchName
    });
    throw error;
  }

  child.stdin.end(prompt.rendered);
  fs.closeSync(logFd);

  const startedAt = isoNow();
  const agent = {
    id,
    role,
    roleLabel: rolePresets[role].label,
    taskId,
    task: task.trim(),
    targetAgentId,
    lane,
    status: "running",
    pid: child.pid,
    ownerSessionId: SESSION_ID,
    processExecutable: codex,
    processStartedAt: startedAt,
    completionEvidence: null,
    heartbeatAt: startedAt,
    lastProgressAt: startedAt,
    machine: assignedMachine,
    baseBranch: base,
    baseSha,
    branchName,
    worktree,
    leaseId: lease.id,
    boundary: mutableBoundary,
    priority: normalizedPriority,
    dependencies: taskRecord.dependencies,
    logPath,
    lastMessagePath,
    promptPath,
    promptTemplateId: prompt.templateId,
    promptTemplateVersion: prompt.templateVersion,
    promptHash: prompt.sha256,
    lastMessage: "",
    model: model?.trim() || null,
    startedAt,
    updatedAt: startedAt,
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

  child.on("exit", async (code, signal) => {
    // Collect asynchronous Git evidence before opening the authoritative state
    // mutation. Never carry a whole-registry snapshot across an await.
    let currentSha = null;
    try { currentSha = await git(["rev-parse", branchName]); } catch {}

    const current = loadState();
    const item = current.agents.find(candidate => candidate.id === id);
    const ownsExit = item?.ownerSessionId === SESSION_ID && item?.pid === child.pid;
    if (item && ownsExit) {
      item.exitCode = code;
      item.signal = signal || null;
      item.status = classifyAuthoritativeExit(item, code);
      item.finishedAt = isoNow();
      item.updatedAt = isoNow();
      item.heartbeatAt = item.finishedAt;
      item.lastMessage = readTextIfExists(item.lastMessagePath) || readLogSummary(item.logPath);
      item.completionEvidence = item.status === "stopped" ? "verified-operator-stop" : "authoritative-exit";
      item.currentSha = currentSha || item.currentSha || null;
      releaseLeaseForAgent(current, item, `authoritative-process-exit:${code ?? "unknown"}`);
      updateTaskForAgent(current, item);
      addEvent(current, "agent.exited", `${id} exited with ${code ?? "unknown"}`, {
        agentId: id,
        taskId,
        reason: "authoritative-child-exit-event",
        evidence: { exitCode: code, signal: signal || null, currentSha: item.currentSha || null }
      });
      if (item.status === "stopped") {
        addNotification(current, {
          severity: "info",
          title: "Agent stopped",
          message: `${item.roleLabel || id} exited after an operator stop request and is not eligible for integration.`,
          action: { type: "inspect-agent", agentId: id },
          dedupeKey: `stopped:${id}`
        });
      } else if (item.status === "done") {
        addNotification(current, {
          severity: "success",
          title: "Agent work finished",
          message: `${item.roleLabel || id} completed with an authoritative exit. Review evidence before integration.`,
          action: { type: "inspect-agent", agentId: id },
          dedupeKey: `done:${id}`
        });
      } else {
        addNotification(current, {
          severity: "error",
          title: "Agent failed",
          message: item.lastMessage || `${item.roleLabel || id} exited with code ${code ?? "unknown"}.`,
          action: { type: "generate-takeover", agentId: id },
          dedupeKey: `failed:${id}`
        });
      }
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
      item.completionEvidence = "spawn-error";
      releaseLeaseForAgent(current, item, "spawn-error-no-owned-process");
      updateTaskForAgent(current, item);
      addEvent(current, "agent.error", error.message, {
        agentId: id,
        taskId,
        reason: "spawn-error-no-owned-process"
      });
      saveState(current);
    }
    children.delete(id);
  });

  return agent;
}

async function killProcessTree(pid) {
  if (process.platform === "win32") {
    await new Promise((resolve, reject) => {
      const killer = spawn("taskkill", ["/PID", String(pid), "/T", "/F"], {
        windowsHide: true,
        stdio: "ignore"
      });
      killer.on("exit", code => {
        if (code === 0) resolve();
        else reject(new Error(`taskkill exited with code ${code ?? "unknown"} for PID ${pid}`));
      });
      killer.on("error", reject);
    });
    return;
  }

  process.kill(pid, "SIGTERM");
}

async function waitForPidExit(pid, timeoutMs = 5000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (!isPidAlive(pid)) return true;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  return !isPidAlive(pid);
}

async function stopAgent(id) {
  const state = refreshState();
  assertMutationsAllowed(state, { safetyControl: true });
  const agent = state.agents.find(item => item.id === id);
  if (!agent) throw new Error("Agent not found.");
  if (isTerminalStatus(agent.status)) return agent;

  const child = children.get(id);
  const ownsProcess = agent.ownerSessionId === SESSION_ID && child && child.pid === agent.pid;
  if (!ownsProcess) {
    throw new Error(`Cannot safely stop ${id}: this controller session cannot prove ownership of PID ${agent.pid || "unknown"}. Preserve state and reconcile or replace instead.`);
  }
  if (!isPidAlive(agent.pid)) {
    agent.status = "interrupted";
    agent.interruptedAt = isoNow();
    agent.updatedAt = isoNow();
    agent.completionEvidence = null;
    updateTaskForAgent(state, agent);
    addEvent(state, "agent.interrupted", `${id} was already absent before stop could be proven`, {
      agentId: id,
      taskId: agent.taskId,
      reason: "process-absent-before-stop"
    });
    saveState(state);
    throw new Error(`Cannot prove a clean stop for ${id}; the process was already absent. Lease preserved for recovery.`);
  }

  agent.stopRequestedAt ||= isoNow();
  agent.status = "stopping";
  agent.updatedAt = isoNow();
  updateTaskForAgent(state, agent);
  addEvent(state, "agent.stopping", `Stop requested for ${id}`, {
    agentId: id,
    taskId: agent.taskId,
    reason: "operator-stop"
  });
  saveState(state);

  try {
    await killProcessTree(agent.pid);
    const exited = await waitForPidExit(agent.pid);
    if (!exited) throw new Error(`PID ${agent.pid} remained alive after the bounded termination wait.`);
  } catch (error) {
    const failedStop = loadState();
    const item = failedStop.agents.find(candidate => candidate.id === id);
    if (item) {
      item.status = "blocked";
      item.blocker = "termination-unproven";
      item.updatedAt = isoNow();
      updateTaskForAgent(failedStop, item);
      addEvent(failedStop, "agent.stop-failed", `Failed to prove termination of ${id}`, {
        agentId: id,
        taskId: item.taskId,
        reason: error.message || String(error)
      });
      addNotification(failedStop, {
        severity: "error",
        title: "Stop could not be proven",
        message: `${item.roleLabel || id} keeps its ownership lease because termination was not proven.`,
        action: { type: "inspect-agent", agentId: id },
        dedupeKey: `stop-failed:${id}`
      });
    }
    saveState(failedStop);
    throw error;
  }

  const stopped = loadState();
  const item = stopped.agents.find(candidate => candidate.id === id);
  if (item && !isTerminalStatus(item.status)) {
    item.status = "stopped";
    item.finishedAt = isoNow();
    item.updatedAt = isoNow();
    item.completionEvidence = "verified-operator-stop";
    releaseLeaseForAgent(stopped, item, "verified-user-stop");
    updateTaskForAgent(stopped, item);
    addEvent(stopped, "agent.stopped", `${id} stopped after process-exit proof`, {
      agentId: id,
      taskId: item.taskId,
      reason: "verified-process-exit"
    });
    saveState(stopped);
  }
  children.delete(id);
  return item || agent;
}

async function branchDivergence(agent) {
  try {
    const baseRef = await resolveLocalBaseRef(agent.baseBranch || "main");
    const [raw, currentSha, filesRaw] = await Promise.all([
      git(["rev-list", "--left-right", "--count", `${baseRef}...${agent.branchName}`]),
      git(["rev-parse", agent.branchName]),
      git(["diff", "--name-only", `${baseRef}...${agent.branchName}`])
    ]);
    const [behindRaw, aheadRaw] = raw.split(/\s+/);
    const behind = Number(behindRaw || 0);
    const ahead = Number(aheadRaw || 0);
    const changedFiles = filesRaw ? filesRaw.split(/\r?\n/).filter(Boolean) : [];
    return {
      ahead,
      behind,
      currentSha,
      changedFiles,
      state: ahead > 0 ? "candidate" : "no-change"
    };
  } catch (error) {
    return {
      ahead: null,
      behind: null,
      currentSha: agent.currentSha || null,
      changedFiles: [],
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
    const reviewers = state.agents.filter(candidate => candidate.role === "reviewer" && candidate.targetAgentId === agent.id);
    const latestReviewer = reviewers.sort((a, b) => String(b.finishedAt || b.startedAt || "").localeCompare(String(a.finishedAt || a.startedAt || "")))[0] || null;
    const eligible = isIntegrationEligible(agent);
    const reviewState = agent.reviewVerdict || (latestReviewer
      ? (latestReviewer.status === "done" ? "completed-unclassified" : latestReviewer.status)
      : "not-requested");
    const effectiveState = divergence.state === "candidate" && !eligible ? "preserved-noneligible" : divergence.state;
    queue.push({
      agentId: agent.id,
      taskId: agent.taskId,
      role: agent.role,
      task: agent.task,
      status: agent.status,
      branchName: agent.branchName,
      baseBranch: agent.baseBranch,
      baseSha: agent.baseSha || null,
      machine: agent.machine,
      finishedAt: agent.finishedAt,
      lastMessage: agent.lastMessage || "",
      completionEvidence: agent.completionEvidence || null,
      integrationEligible: eligible,
      reviewState,
      reviewerAgentId: latestReviewer?.id || null,
      riskLevel: divergence.changedFiles?.some(file => /updat|release|workflow|control|database|filesystem/i.test(file)) ? "high" : (divergence.changedFiles?.length > 12 ? "medium" : "normal"),
      ...divergence,
      state: effectiveState,
      integrationReady: eligible && divergence.ahead > 0 && reviewState === "approved"
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
  const running = agents.filter(agent => coreIsActiveStatus(agent.status)).length;
  const done = agents.filter(agent => agent.status === "done").length;
  const failed = agents.filter(agent => agent.status === "failed").length;
  const stopped = agents.filter(agent => agent.status === "stopped").length;
  const uncertain = agents.filter(agent => ["interrupted", "orphaned", "stale"].includes(agent.status)).length;
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
    uncertain,
    total: agents.length,
    successRate,
    averageRuntimeMs,
    activeLeases: state.leases.filter(lease => lease.status === "active").length,
    integrationCandidates: queue.filter(item => item.state === "candidate").length,
    blockedIntegration: queue.filter(item => item.state === "preserved-noneligible").length,
    noChangeCompleted: queue.filter(item => item.state === "no-change").length
  };
}

function readRepositoryContext() {
  const file = path.join(REPO, "_AGENT_CONTEXT", "CURRENT_REVISION.json");
  try {
    const parsed = JSON.parse(fs.readFileSync(file, "utf8"));
    return {
      currentVersion: parsed.currentVersion || parsed.version || null,
      nextMilestone: parsed.nextMilestone || parsed.next_milestone || parsed.nextAction || null,
      verificationCommit:
        parsed.verificationAppliesToCommit ||
        parsed.verifiedCommit ||
        parsed.lastVerifiedCommit ||
        parsed.lastClosedVerification?.sourceCommit ||
        null,
      updatedAt: parsed.updatedAt || parsed.generatedAt || null
    };
  } catch {
    return { currentVersion: null, nextMilestone: null, verificationCommit: null, updatedAt: null };
  }
}

async function repositorySnapshot() {
  const read = async args => {
    try { return await git(args); } catch { return null; }
  };
  const [remoteMainSha, localObservedMainSha, currentBranch, currentHead, status] = await Promise.all([
    remoteHeadSha("main"),
    read(["rev-parse", "origin/main"]),
    read(["branch", "--show-current"]),
    read(["rev-parse", "HEAD"]),
    read(["status", "--short"])
  ]);
  return {
    canonical: "fengie/mhw-mods",
    defaultBranch: "main",
    remoteMainSha,
    localObservedMainSha,
    localObservationMatchesRemote: Boolean(remoteMainSha && localObservedMainSha && remoteMainSha === localObservedMainSha),
    currentCheckoutBranch: currentBranch,
    currentCheckoutSha: currentHead,
    workingTreeClean: status === "",
    workingTreeSummary: status || ""
  };
}

function releaseGate(state, queue, repository, repositoryContext) {
  const pendingCandidates = queue.filter(item => item.state === "candidate" || item.state === "preserved-noneligible");
  const uncertain = state.agents.filter(agent => ["orphaned", "interrupted"].includes(agent.status));
  const exactVerification = repositoryContext.verificationCommit && repository.remoteMainSha
    ? repositoryContext.verificationCommit === repository.remoteMainSha
    : null;
  const checks = [
    {
      id: "canonical-remote",
      label: "Canonical remote SHA known",
      status: repository.remoteMainSha ? "pass" : "unknown",
      evidence: repository.remoteMainSha
    },
    {
      id: "exact-verification",
      label: "Stored verification applies to canonical main",
      status: exactVerification === true ? "pass" : (exactVerification === false ? "fail" : "unknown"),
      evidence: repositoryContext.verificationCommit || "No exact verification commit was discoverable."
    },
    {
      id: "integration-queue",
      label: "No unresolved integration candidates",
      status: pendingCandidates.length === 0 ? "pass" : "fail",
      evidence: pendingCandidates.map(item => ({ agentId: item.agentId, branchName: item.branchName, state: item.state }))
    },
    {
      id: "worker-certainty",
      label: "No unresolved interrupted/orphaned workers",
      status: uncertain.length === 0 ? "pass" : "fail",
      evidence: uncertain.map(agent => agent.id)
    },
    {
      id: "artifact-provenance",
      label: "Release artifact provenance recorded",
      status: "unknown",
      evidence: "No structured release-artifact evidence has been recorded in the control-plane registry."
    },
    {
      id: "rollback-proof",
      label: "Required update / rollback proof recorded",
      status: "unknown",
      evidence: "Control plane will not infer rollback success from source or ordinary tests."
    }
  ];
  return {
    ready: checks.every(check => check.status === "pass"),
    canonicalSha: repository.remoteMainSha,
    checks,
    policy: "Evidence-only readiness. Publication is never implied by a green source build."
  };
}

async function buildSnapshot({ fetchRemote = false, repositoryWriteAuthorized = false } = {}) {
  if (fetchRemote) {
    const beforeSync = loadState();
    assertMutationsAllowed(beforeSync);
    const machine = os.hostname().toLowerCase();
    const permission = canUseMachineForRepositoryWrite(beforeSync, machine, repositoryWriteAuthorized);
    if (!permission.allowed) throw new Error(`Repository sync blocked by machine policy on ${machine}: ${permission.reason}`);
    await git(["fetch", "origin", "--prune"]);
  }

  const state = refreshState();
  const repositoryContext = readRepositoryContext();
  const [queue, branches, repository] = await Promise.all([
    integrationQueue(state),
    observedBranches(state),
    repositorySnapshot()
  ]);
  const workers = workerSnapshot(state);
  const currentMission = deriveMission(state, repositoryContext);
  const suggestedActions = recommendNextActions({ state, integrationQueue: queue, repositoryContext });

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
      stateVersion: STATE_VERSION,
      sessionId: SESSION_ID,
      health: state.health
    },
    currentMission,
    suggestedActions,
    repository,
    repositoryContext,
    telemetry: telemetry(state, queue),
    settings: state.settings,
    autonomyProfiles: AUTONOMY_PROFILES,
    workflows: WORKFLOW_PRESETS,
    workers,
    taskGraph: buildTaskGraph(state.tasks),
    tasks: state.tasks,
    agents: state.agents,
    leases: state.leases,
    integrationQueue: queue,
    releaseGate: releaseGate(state, queue, repository, repositoryContext),
    notifications: state.notifications.slice(-80).reverse(),
    improvements: state.improvements,
    observedBranches: branches,
    recentEvents: state.events.slice(-120).reverse(),
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
    targetAgentId: target.id,
    repositoryWriteAuthorized: Boolean(body.repositoryWriteAuthorized),
    acceptanceCriteria: [
      "Return an explicit approved / changes-requested / rejected verdict with evidence.",
      "Inspect the candidate diff independently rather than trusting the implementer summary."
    ],
    verification: [
      "Run focused checks needed to validate review findings.",
      "Identify any exact-source verification gap that blocks integration."
    ]
  });
}

async function previewWorkflow(workflowId, body = {}) {
  const state = refreshState();
  const repositoryContext = readRepositoryContext();
  const mission = String(body.objective || "").trim() || deriveMission(state, repositoryContext);
  return planWorkflow(workflowId, {
    state,
    mission,
    baseBranch: body.baseBranch || "main",
    machine: body.machine || "auto"
  });
}

async function executeWorkflow(workflowId, body = {}) {
  return withDeployLock(async () => {
    const state = refreshState();
    assertMutationsAllowed(state, { dispatch: true });
    const plan = await previewWorkflow(workflowId, body);
    if (plan.blocked?.length) return { plan, created: [], blocked: plan.blocked };

    const created = [];
    const blocked = [];
    for (const work of plan.steps) {
      try {
        const agent = await deployOne({
          role: work.role,
          task: work.task,
          baseBranch: body.baseBranch || plan.baseBranch || "main",
          model: body.model || "",
          boundary: work.boundary,
          priority: work.priority,
          machine: work.machine || body.machine || "auto",
          dependencies: work.dependencies || [],
          lane: work.lane || null,
          repositoryWriteAuthorized: Boolean(body.repositoryWriteAuthorized),
          acceptanceCriteria: [
            "Stay inside the assigned ownership boundary.",
            "Produce reproducible evidence and a concise durable handoff.",
            "Do not duplicate an already active task or silently expand scope."
          ],
          verification: [
            "Run the strongest targeted checks applicable to the assigned lane.",
            "Tie claims to exact source/artifact identity and report any unverified environment honestly."
          ]
        });
        created.push(agent);
      } catch (error) {
        blocked.push({ work, error: error.message || String(error) });
        break;
      }
    }
    return { plan, created, blocked };
  });
}

function buildTakeoverForAgent(id, { persist = false } = {}) {
  const state = refreshState();
  const agent = state.agents.find(item => item.id === id);
  if (!agent) throw new Error("Agent not found.");
  const task = state.tasks.find(item => item.id === agent.taskId) || null;
  const handoff = takeoverContext(agent, task, task?.evidence || []);
  if (persist) {
    assertMutationsAllowed(state, { safetyControl: true });
    const takeoverPath = path.join(DATA_DIR, `${id}.takeover.json`);
    fs.writeFileSync(takeoverPath, JSON.stringify(handoff, null, 2), "utf8");
    agent.takeoverPath = takeoverPath;
    agent.updatedAt = isoNow();
    if (task) {
      task.takeoverPath = takeoverPath;
      task.updatedAt = isoNow();
    }
    addEvent(state, "agent.takeover-preserved", `Takeover state preserved for ${id}`, {
      agentId: id,
      taskId: agent.taskId,
      evidence: { takeoverPath }
    });
    saveState(state);
    return { handoff, takeoverPath };
  }
  return { handoff, takeoverPath: agent.takeoverPath || null };
}

async function preserveAndStop(id) {
  const preserved = buildTakeoverForAgent(id, { persist: true });
  const agent = await stopAgent(id);
  return { ...preserved, agent };
}

function createImprovementProposal(body = {}) {
  const request = String(body.request || body.objective || "").trim();
  if (!request) throw new Error("Improvement request is required.");
  const state = loadState();
  assertMutationsAllowed(state);
  const proposal = {
    id: `improvement-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
    request,
    problem: String(body.problem || request).trim(),
    expectedBenefit: String(body.expectedBenefit || "Reduce repeated operator friction while preserving repository governance.").trim(),
    status: "proposed",
    createdAt: isoNow(),
    updatedAt: isoNow(),
    source: body.source || "operator",
    implementationTaskId: null,
    upgradeCandidate: null
  };
  state.improvements.unshift(proposal);
  addEvent(state, "improvement.proposed", proposal.request, {
    improvementId: proposal.id,
    reason: proposal.problem
  });
  saveState(state);
  return proposal;
}

function recordTaskEvidence(taskId, body = {}) {
  const state = loadState();
  assertMutationsAllowed(state);
  const task = state.tasks.find(item => item.id === taskId);
  if (!task) throw new Error("Task not found.");
  const evidence = {
    id: `evidence-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
    recordedAt: isoNow(),
    type: String(body.type || "note"),
    sourceSha: body.sourceSha || null,
    command: body.command || null,
    result: body.result || null,
    artifactHash: body.artifactHash || null,
    environment: body.environment || null,
    details: body.details || null
  };
  task.evidence ||= [];
  task.evidence.push(evidence);
  task.updatedAt = isoNow();
  addEvent(state, "task.evidence-recorded", `Evidence recorded for ${taskId}`, {
    taskId,
    evidence: { id: evidence.id, type: evidence.type, sourceSha: evidence.sourceSha, result: evidence.result }
  });
  saveState(state);
  return evidence;
}

function setReviewVerdict(agentId, body = {}) {
  const verdict = String(body.verdict || "").trim().toLowerCase();
  if (!["approved", "changes-requested", "rejected"].includes(verdict)) {
    throw new Error("Review verdict must be approved, changes-requested, or rejected.");
  }
  const state = loadState();
  assertMutationsAllowed(state);
  const agent = state.agents.find(item => item.id === agentId);
  if (!agent) throw new Error("Candidate agent not found.");
  agent.reviewVerdict = verdict;
  agent.reviewVerdictAt = isoNow();
  agent.reviewVerdictBy = String(body.by || "operator");
  agent.reviewEvidence = body.evidence || null;
  addEvent(state, "integration.review-verdict", `${agentId} review verdict: ${verdict}`, {
    agentId,
    reason: body.reason || null,
    evidence: body.evidence || null
  });
  saveState(state);
  return agent;
}

function updateControlSettings(body = {}) {
  const state = loadState();
  if (degradedReason || state.health?.mode === "degraded") throw new Error("Cannot change control settings while state is degraded.");
  if (body.autonomyLevel !== undefined) {
    if (!AUTONOMY_PROFILES[body.autonomyLevel]) throw new Error("Unknown autonomy level.");
    state.settings.autonomyLevel = body.autonomyLevel;
  }
  if (body.dispatchPaused !== undefined) state.settings.dispatchPaused = Boolean(body.dispatchPaused);
  if (body.readOnly !== undefined) state.settings.readOnly = Boolean(body.readOnly);
  if (body.draining !== undefined) {
    state.settings.draining = Boolean(body.draining);
    if (state.settings.draining) state.settings.dispatchPaused = true;
  }
  if (body.clearEmergencyStop === true) {
    state.settings.emergencyStop = false;
    state.settings.dispatchPaused = false;
  }
  addEvent(state, "control.settings", "Control-plane operating settings changed", {
    reason: body.reason || "operator-action",
    evidence: {
      autonomyLevel: state.settings.autonomyLevel,
      dispatchPaused: state.settings.dispatchPaused,
      readOnly: state.settings.readOnly,
      emergencyStop: state.settings.emergencyStop,
      draining: Boolean(state.settings.draining)
    }
  });
  saveState(state);
  return state.settings;
}

async function emergencyStop() {
  const state = loadState();
  assertMutationsAllowed(state, { safetyControl: true });
  state.settings.emergencyStop = true;
  state.settings.dispatchPaused = true;
  state.settings.draining = false;
  addEvent(state, "control.emergency-stop", "Emergency stop activated", {
    reason: "operator-emergency-stop"
  });
  saveState(state);

  const owned = state.agents
    .filter(agent => coreIsActiveStatus(agent.status) && agent.ownerSessionId === SESSION_ID && children.has(agent.id))
    .map(agent => agent.id);
  const results = [];
  for (const id of owned) {
    try {
      const agent = await stopAgent(id);
      results.push({ id, stopped: agent.status === "stopped" || !coreIsActiveStatus(agent.status), status: agent.status });
    } catch (error) {
      results.push({ id, stopped: false, error: error.message || String(error) });
    }
  }
  return { settings: loadState().settings, results };
}

async function stopSwarm({ preserve = true } = {}) {
  const state = loadState();
  assertMutationsAllowed(state, { safetyControl: true });
  state.settings.dispatchPaused = true;
  addEvent(state, "control.stop-swarm", "Stop swarm requested", {
    reason: preserve ? "preserve-and-stop" : "stop"
  });
  saveState(state);

  const ids = state.agents.filter(agent => coreIsActiveStatus(agent.status)).map(agent => agent.id);
  const results = [];
  for (const id of ids) {
    try {
      const result = preserve ? await preserveAndStop(id) : { agent: await stopAgent(id) };
      results.push({ id, status: result.agent.status, takeoverPath: result.takeoverPath || null });
    } catch (error) {
      results.push({ id, status: "unresolved", error: error.message || String(error) });
    }
  }
  return { settings: loadState().settings, results };
}

async function searchControlPlane(query) {
  const q = String(query || "").trim();
  if (!q) return { query: q, results: [] };
  const state = refreshState();
  const needle = q.toLowerCase();
  const results = [];
  const add = (type, id, title, detail, at = null) => {
    if (results.length >= 80) return;
    results.push({ type, id, title, detail, at });
  };

  for (const task of state.tasks) {
    const haystack = JSON.stringify(task).toLowerCase();
    if (haystack.includes(needle)) add("task", task.id, task.objective, `${task.status} · ${task.branchName || "no branch"}`, task.updatedAt);
  }
  for (const agent of state.agents) {
    const haystack = `${agent.id} ${agent.role} ${agent.task} ${agent.lastMessage || ""} ${agent.branchName || ""}`.toLowerCase();
    if (haystack.includes(needle)) add("agent", agent.id, `${agent.roleLabel || agent.role}: ${agent.task}`, `${agent.status} · ${agent.branchName || "no branch"}`, agent.updatedAt);
  }
  for (const event of state.events.slice(-500)) {
    const haystack = JSON.stringify(event).toLowerCase();
    if (haystack.includes(needle)) add("event", event.type, event.message, event.reason || "", event.at);
  }
  for (const proposal of state.improvements) {
    if (JSON.stringify(proposal).toLowerCase().includes(needle)) add("improvement", proposal.id, proposal.request, proposal.status, proposal.updatedAt);
  }

  try {
    const commits = await git(["log", "--all", "--regexp-ignore-case", `--grep=${q}`, "--format=%H|%cI|%s", "-20"]);
    for (const line of commits.split(/\r?\n/).filter(Boolean)) {
      const [sha, at, ...subject] = line.split("|");
      add("commit", sha, subject.join("|"), sha, at);
    }
  } catch {}
  try {
    const findings = await git(["grep", "-n", "-i", "-F", "--", q, "_AGENT_CONTEXT"]);
    for (const line of findings.split(/\r?\n/).filter(Boolean).slice(0, 30)) {
      const first = line.indexOf(":");
      const second = line.indexOf(":", first + 1);
      const file = first >= 0 ? line.slice(0, first) : line;
      const lineNo = second > first ? line.slice(first + 1, second) : "";
      const textValue = second >= 0 ? line.slice(second + 1) : line;
      add("continuity", `${file}:${lineNo}`, file, textValue.trim());
    }
  } catch {}

  return { query: q, results: results.slice(0, 80) };
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
      const body = await readJson(req);
      return sendJson(res, 200, await buildSnapshot({
        fetchRemote: true,
        repositoryWriteAuthorized: Boolean(body.repositoryWriteAuthorized)
      }));
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

    if (req.method === "GET" && pathname === "/api/workflows") {
      const state = refreshState();
      return sendJson(res, 200, {
        workflows: WORKFLOW_PRESETS,
        autonomyProfiles: AUTONOMY_PROFILES,
        settings: state.settings
      });
    }

    if (req.method === "GET" && pathname === "/api/recommendations") {
      const snapshot = await buildSnapshot();
      return sendJson(res, 200, {
        mission: snapshot.currentMission,
        actions: snapshot.suggestedActions
      });
    }

    if (req.method === "GET" && pathname === "/api/release") {
      const snapshot = await buildSnapshot();
      return sendJson(res, 200, snapshot.releaseGate);
    }

    if (req.method === "GET" && pathname === "/api/search") {
      return sendJson(res, 200, await searchControlPlane(url.searchParams.get("q") || ""));
    }

    if (req.method === "POST" && pathname === "/api/command/resolve") {
      const body = await readJson(req);
      const interpretation = interpretCommand(body.command || body.objective || "");
      const plan = interpretation.workflowId
        ? await previewWorkflow(interpretation.workflowId, {
            ...body,
            objective: body.objective || body.command || ""
          })
        : null;
      return sendJson(res, 200, { interpretation, plan });
    }

    const workflowMatch = pathname.match(/^\/api\/workflows\/([^/]+)\/(preview|execute)$/);
    if (req.method === "POST" && workflowMatch) {
      const body = await readJson(req);
      const workflowId = workflowMatch[1];
      if (workflowMatch[2] === "preview") {
        return sendJson(res, 200, await previewWorkflow(workflowId, body));
      }
      return sendJson(res, 201, await executeWorkflow(workflowId, body));
    }

    if (req.method === "POST" && pathname === "/api/control/settings") {
      const body = await readJson(req);
      return sendJson(res, 200, updateControlSettings(body));
    }

    if (req.method === "POST" && pathname === "/api/control/pause") {
      const body = await readJson(req);
      return sendJson(res, 200, updateControlSettings({ dispatchPaused: true, reason: body.reason || "operator-pause" }));
    }

    if (req.method === "POST" && pathname === "/api/control/resume") {
      const body = await readJson(req);
      return sendJson(res, 200, updateControlSettings({
        dispatchPaused: false,
        draining: false,
        clearEmergencyStop: Boolean(body.clearEmergencyStop),
        reason: body.reason || "operator-resume"
      }));
    }

    if (req.method === "POST" && pathname === "/api/control/read-only") {
      const body = await readJson(req);
      return sendJson(res, 200, updateControlSettings({
        readOnly: body.enabled !== false,
        reason: body.reason || "operator-read-only"
      }));
    }

    if (req.method === "POST" && pathname === "/api/control/drain") {
      const body = await readJson(req);
      return sendJson(res, 200, updateControlSettings({ draining: true, reason: body.reason || "operator-drain" }));
    }

    if (req.method === "POST" && pathname === "/api/control/emergency-stop") {
      return sendJson(res, 200, await emergencyStop());
    }

    if (req.method === "POST" && pathname === "/api/control/stop-swarm") {
      const body = await readJson(req);
      return sendJson(res, 200, await stopSwarm({ preserve: body.preserve !== false }));
    }

    if (req.method === "POST" && pathname === "/api/improvements") {
      const body = await readJson(req);
      return sendJson(res, 201, createImprovementProposal(body));
    }

    if (req.method === "POST" && pathname === "/api/deploy") {
      const body = await readJson(req);
      const result = await withDeployLock(async () => {
        const count = Math.max(1, Math.min(MAX_DEPLOY_COUNT, Number(body.count || 1)));
        const capacityState = refreshState();
        assertMutationsAllowed(capacityState, { dispatch: true });
        const capacity = deploymentBatchCapacity(capacityState, count, MAX_ACTIVE_AGENTS);
        if (!capacity.allowed) {
          throw new Error(`Requested deployment batch of ${count} exceeds available worker capacity (${capacity.available} free of ${capacity.maximum}; ${capacity.active} active). No workers were launched.`);
        }
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
            dependencies: Array.isArray(body.dependencies) ? body.dependencies : [],
            targetAgentId: body.targetAgentId || null,
            lane: body.lane || null,
            repositoryWriteAuthorized: Boolean(body.repositoryWriteAuthorized),
            acceptanceCriteria: Array.isArray(body.acceptanceCriteria) ? body.acceptanceCriteria : [],
            verification: Array.isArray(body.verification) ? body.verification : [],
            additionalConstraints: Array.isArray(body.additionalConstraints) ? body.additionalConstraints : []
          }));
        }
        return created;
      });
      return sendJson(res, 201, { agents: result });
    }

    const reviewMatch = pathname.match(/^\/api\/agents\/([^/]+)\/review$/);
    if (req.method === "POST" && reviewMatch) {
      const body = await readJson(req);
      const agent = await withDeployLock(() => deployReview(reviewMatch[1], body));
      return sendJson(res, 201, { agent });
    }

    const promptMatch = pathname.match(/^\/api\/agents\/([^/]+)\/prompt$/);
    if (req.method === "GET" && promptMatch) {
      const state = refreshState();
      const agent = state.agents.find(item => item.id === promptMatch[1]);
      if (!agent) return sendJson(res, 404, { error: "Agent not found." });
      return sendJson(res, 200, {
        agentId: agent.id,
        taskId: agent.taskId,
        templateId: agent.promptTemplateId,
        templateVersion: agent.promptTemplateVersion,
        sha256: agent.promptHash,
        prompt: readTextIfExists(agent.promptPath)
      });
    }

    const takeoverMatch = pathname.match(/^\/api\/agents\/([^/]+)\/takeover$/);
    if (takeoverMatch && req.method === "GET") {
      return sendJson(res, 200, buildTakeoverForAgent(takeoverMatch[1], { persist: false }));
    }
    if (takeoverMatch && req.method === "POST") {
      return sendJson(res, 201, buildTakeoverForAgent(takeoverMatch[1], { persist: true }));
    }

    const preserveStopMatch = pathname.match(/^\/api\/agents\/([^/]+)\/preserve-stop$/);
    if (req.method === "POST" && preserveStopMatch) {
      return sendJson(res, 200, await preserveAndStop(preserveStopMatch[1]));
    }

    const evidenceMatch = pathname.match(/^\/api\/tasks\/([^/]+)\/evidence$/);
    if (req.method === "POST" && evidenceMatch) {
      const body = await readJson(req);
      return sendJson(res, 201, recordTaskEvidence(evidenceMatch[1], body));
    }

    const reviewVerdictMatch = pathname.match(/^\/api\/integration\/([^/]+)\/review-verdict$/);
    if (req.method === "POST" && reviewVerdictMatch) {
      const body = await readJson(req);
      return sendJson(res, 200, setReviewVerdict(reviewVerdictMatch[1], body));
    }

    const improvementStartMatch = pathname.match(/^\/api\/improvements\/([^/]+)\/start$/);
    if (req.method === "POST" && improvementStartMatch) {
      const state = refreshState();
      const proposal = state.improvements.find(item => item.id === improvementStartMatch[1]);
      if (!proposal) return sendJson(res, 404, { error: "Improvement proposal not found." });
      const body = await readJson(req);
      const result = await executeWorkflow("self-improve", {
        ...body,
        objective: proposal.request
      });
      const updated = loadState();
      const currentProposal = updated.improvements.find(item => item.id === proposal.id);
      if (currentProposal) {
        currentProposal.status = result.created.length ? "implementation-active" : "blocked";
        currentProposal.updatedAt = isoNow();
        currentProposal.implementationTaskId = result.created[0]?.taskId || null;
        saveState(updated);
      }
      return sendJson(res, 201, result);
    }

    const notificationMatch = pathname.match(/^\/api\/notifications\/([^/]+)\/dismiss$/);
    if (req.method === "POST" && notificationMatch) {
      const state = loadState();
      assertMutationsAllowed(state);
      const notification = state.notifications.find(item => item.id === notificationMatch[1]);
      if (!notification) return sendJson(res, 404, { error: "Notification not found." });
      notification.dismissedAt = isoNow();
      saveState(state);
      return sendJson(res, 200, notification);
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

const LOOPBACK_HOSTS = new Set(["127.0.0.1", "localhost", "::1"]);
if (!LOOPBACK_HOSTS.has(String(HOST).trim().toLowerCase())) {
  throw new Error(`Refusing unauthenticated non-loopback bind host "${HOST}". Configure an authenticated remote-access boundary before exposing Agent Control beyond localhost.`);
}

server.listen(PORT, HOST, () => {
  console.log(`Heaven Agent Control Plane listening on http://${HOST}:${PORT}`);
  console.log(`Repo: ${REPO}`);
  console.log(`Worktrees: ${WORKTREE_ROOT}`);
  console.log(`Capacity: ${MAX_ACTIVE_AGENTS} active agents`);
});
