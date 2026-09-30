import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { fileURLToPath } from "node:url";
import { spawnHidden as spawn, execFileHidden as execFileAsync } from "./lib/background-process.mjs";
import { createHash, randomBytes, randomUUID, timingSafeEqual } from "node:crypto";
import { renderAgentPrompt, REQUIRED_REPOSITORY_TRAINING_PATHS } from "./lib/prompt-templates.mjs";
import { decideAutopilotAction, normalizeAutopilotState, transitionAutopilot } from "./lib/autopilot-core.mjs";
import {
  STATE_VERSION,
  AUTONOMY_PROFILES,
  WORKFLOW_PRESETS,
  applyPreLaunchFailure,
  agentExecutionModeDecision,
  autonomyPermissionDecision,
  canUseMachineForRepositoryWrite,
  classifyAuthoritativeExit,
  buildTaskGraph,
  buildSwarmPromptEvolutionContext,
  defaultControlState,
  deploymentBatchCapacity,
  deriveMission,
  interpretCommand,
  isActiveStatus as coreIsActiveStatus,
  isIntegrationEligible,
  migrateControlState,
  planWorkflow,
  providerCapacityActiveTerminationDecision,
  providerCapacityCircuit,
  isProviderCapacityErrorMessage,
  selectAgentTerminalMessage,
  recommendNextActions,
  roleCatalog,
  takeoverContext,
  workflowLeasePreflight,
  workflowPermission
} from "./lib/control-core.mjs";
import {
  federationSnapshot,
  reconcileObservation,
  recordProviderHeartbeat,
  syncManagedAgents
} from "./lib/federated-registry.mjs";
import {
  bridgeMachineStatus,
  bridgeResultSucceeded,
  cancelHeavenBridgeJob,
  inspectHeavenBridge,
  runHeaven2BridgeAction
} from "./lib/heaven-bridge-provider.mjs";
import { placementTransportDecision } from "./lib/liveness-scheduler.mjs";
import {
  looksLikeExecutionOpener,
  noWorkTerminationDecision,
  planSwarmTailRecoveryBatch,
  recoveryBackoffMs,
  recoveryBackoffWithJitterMs,
  recoveryMachineTarget,
  terminationReconciliationDecision
} from "./lib/no-work-recovery.mjs";
import { planGoToWorkRecoveries } from "./lib/go-to-work-recovery.mjs";
import {
  detectWorkHandoffAction,
  learnWorkHandoffSignature,
  normalizeWorkHandoffRegistry,
  recordWorkHandoffDrift
} from "./lib/work-handoff-signatures.mjs";
import { chooseBranchPlan, cleanupDisposition, BRANCH_POLICY_RESERVED } from "./lib/branch-lifecycle.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const PUBLIC_DIR = path.join(HERE, "public");
const DATA_DIR = process.env.AGENT_CONTROL_DATA_DIR || path.join(HERE, "data");
const STATE_FILE = path.join(DATA_DIR, "control-plane.json");
const STATE_BACKUP_FILE = path.join(DATA_DIR, "control-plane.json.bak");
const LEGACY_STATE_FILE = path.join(DATA_DIR, "agents.json");
const WORK_HANDOFF_SIGNATURES_FILE = path.join(DATA_DIR, "work-handoff-signatures.json");
const CONTROLLER_PID_FILE = path.join(DATA_DIR, "controller-process.json");
const FAILURE_LOG_FILE = path.join(DATA_DIR, "failures.jsonl");

const PORT = Number(process.env.AGENT_CONTROL_PORT || 7331);
const HOST = process.env.AGENT_CONTROL_HOST || "127.0.0.1";
const CONTROLLER_HOST = String(process.env.AGENT_CONTROL_CONTROLLER_HOST || "heaven2").trim().toLowerCase();
const ALLOW_NON_CONTROLLER_HOST = process.env.AGENT_CONTROL_ALLOW_NON_CONTROLLER_HOST === "1";
const REPO = process.env.AGENT_CONTROL_REPO || path.join(os.homedir(), "local-ai-workspaces", "mhw-mods");
const WORKTREE_ROOT = process.env.AGENT_WORKTREE_ROOT || path.join(os.homedir(), "agent-worktrees");
const MAX_DEPLOY_COUNT = Math.max(1, Math.min(48, Number(process.env.AGENT_CONTROL_MAX_DEPLOY_COUNT || 24) || 24));
const MAX_ACTIVE_AGENTS = Math.max(1, Math.min(48, Number(process.env.AGENT_CONTROL_MAX_ACTIVE || 24) || 24));
const EVENT_LIMIT = 1000;
const NOTIFICATION_LIMIT = 250;
const LEASE_TTL_MS = Number(process.env.AGENT_CONTROL_LEASE_TTL_MS || 120000);
const STALE_PROGRESS_MS = Number(process.env.AGENT_CONTROL_STALE_PROGRESS_MS || 900000);
const HEAVEN_BRIDGE_RUNNER = path.join(HERE, "lib", "heaven-bridge-runner.mjs");
const SESSION_ID = randomUUID();
const children = new Map();
const stopOperations = new Map();
let degradedReason = null;
let deployMutex = Promise.resolve();
let autopilotTickRunning = false;
let noWorkRecoveryTickRunning = false;
let goToWorkRecoveryTickRunning = false;
let swarmTailRecoveryTickRunning = false;
const noWorkRecoveryOperations = new Set();
const goToWorkRecoveryOperations = new Set();
const swarmTailRecoveryOperations = new Set();
const AUTOPILOT_TICK_MS = Math.max(1000, Number(process.env.AGENT_CONTROL_AUTOPILOT_TICK_MS || 4000));
const PERPETUAL_RECOVERY_WINDOW_MS = Math.max(60_000, Number(process.env.AGENT_CONTROL_PERPETUAL_RECOVERY_WINDOW_MS || 10 * 60_000));
const PERPETUAL_MAX_RECOVERIES_PER_WINDOW = Math.max(1, Number(process.env.AGENT_CONTROL_PERPETUAL_MAX_RECOVERIES_PER_WINDOW || 6));
const PERPETUAL_RETRY_BASE_MS = Math.max(5_000, Number(process.env.AGENT_CONTROL_PERPETUAL_RETRY_BASE_MS || 15_000));
const PERPETUAL_RETRY_MAX_MS = Math.max(PERPETUAL_RETRY_BASE_MS, Number(process.env.AGENT_CONTROL_PERPETUAL_RETRY_MAX_MS || 15 * 60_000));
const NO_WORK_RECOVERY_TICK_MS = Math.max(2000, Number(process.env.AGENT_CONTROL_NO_WORK_RECOVERY_TICK_MS || 5000));
const GO_TO_WORK_RECOVERY_TICK_MS = Math.max(5000, Number(process.env.AGENT_CONTROL_GO_TO_WORK_RECOVERY_TICK_MS || 10000));
const WORKFLOW_STARTUP_GUARD_MS = Math.max(250, Number(process.env.AGENT_CONTROL_WORKFLOW_STARTUP_GUARD_MS || 2500));

const rolePresets = roleCatalog();

function isoNow() {
  return new Date().toISOString();
}

function stableJitterUnit(seed) {
  const digest = createHash("sha256").update(String(seed || "agent-control-retry"), "utf8").digest();
  return digest.readUInt32BE(0) / 0xffffffff;
}

function taskCapabilityDigest(value) {
  return createHash("sha256").update(String(value || ""), "utf8").digest();
}

function createTaskCapability() {
  const token = randomBytes(32).toString("base64url");
  return { token, hash: taskCapabilityDigest(token).toString("hex") };
}

function taskCapabilityMatches(expectedHash, token) {
  const normalized = String(expectedHash || "").trim();
  const candidate = String(token || "").trim();
  if (!candidate || !/^[0-9a-f]{64}$/i.test(normalized)) return false;
  const expected = Buffer.from(normalized, "hex");
  const actual = taskCapabilityDigest(candidate);
  return expected.length === actual.length && timingSafeEqual(expected, actual);
}

function requestTaskCapability(req) {
  return String(req?.headers?.["x-agent-control-task-token"] || "").trim();
}

function repositoryTrainingPathsFor(role) {
  const paths = [...REQUIRED_REPOSITORY_TRAINING_PATHS];
  if (role === "manager") paths.push("_AGENT_TRAINING/PROMPT_TEMPLATES/01_MANAGER_ORCHESTRATOR.txt");
  return paths;
}

function buildRepositoryTrainingManifest(worktree, role) {
  const manifest = [];
  for (const relativePath of repositoryTrainingPathsFor(role)) {
    const absolutePath = path.join(worktree, relativePath);
    if (!fs.existsSync(absolutePath) || !fs.statSync(absolutePath).isFile()) {
      throw new Error(`Repository training gate failed: mandatory training source is missing: ${relativePath}`);
    }
    const content = fs.readFileSync(absolutePath);
    if (!content.length) {
      throw new Error(`Repository training gate failed: mandatory training source is empty: ${relativePath}`);
    }
    manifest.push(`${relativePath} — sha256:${createHash("sha256").update(content).digest("hex")} — ${content.length} bytes`);
  }
  return manifest;
}

function authorizationError(message) {
  const error = new Error(message);
  error.statusCode = 403;
  return error;
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

function assertAutonomyPermission(state, permission, action = permission) {
  const decision = autonomyPermissionDecision(state, permission);
  if (decision.allowed) return decision;
  const error = new Error(
    `Autonomy level "${decision.level}" does not permit ${action}; required permission "${permission}".`
  );
  error.statusCode = 403;
  throw error;
}

function assertWorkflowAutonomy(state, workflowId) {
  const permission = workflowPermission(workflowId);
  if (!permission) {
    const error = new Error(`Workflow "${workflowId}" has no autonomy permission mapping and fails closed.`);
    error.statusCode = 403;
    throw error;
  }
  return assertAutonomyPermission(state, permission, `workflow execution "${workflowId}"`);
}

function assertAnyAutonomyPermission(state, permissions, action) {
  const candidates = permissions.map(permission => autonomyPermissionDecision(state, permission));
  const allowed = candidates.find(decision => decision.allowed);
  if (allowed) return allowed;
  const error = new Error(
    `Autonomy level "${candidates[0]?.level || "unknown"}" does not permit ${action}; requires one of: ${permissions.join(", ")}.`
  );
  error.statusCode = 403;
  throw error;
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
  const candidates = [];
  const pushCandidate = value => {
    if (typeof value === "string" && value.trim()) candidates.push(value.trim());
  };
  for (const line of lines) {
    try {
      const event = JSON.parse(line);
      pushCandidate(event?.error?.message);
      if (typeof event?.error === "string") pushCandidate(event.error);
      pushCandidate(event?.message);
      pushCandidate(event?.item?.text);
      pushCandidate(event?.response?.output_text);
      pushCandidate(event?.detail);
      pushCandidate(event?.reason);
      pushCandidate(event?.stderr);
      pushCandidate(event?.data?.stderr);
      pushCandidate(event?.result?.message);
      pushCandidate(event?.result?.error?.message);
      pushCandidate(event?.result?.stderr);
      pushCandidate(event?.result?.data?.stderr);
    } catch {
      pushCandidate(line);
    }
  }
  return candidates.find(isProviderCapacityErrorMessage) || candidates[0] || "";
}

function sanitizeFailureText(value, maxChars = 8000) {
  if (value === null || value === undefined) return null;
  let text = String(value).replace(/\0/g, "");
  if (!text) return null;
  text = text
    .replace(/(authorization\s*:\s*bearer\s+)[^\s"'<>]+/gi, "$1[REDACTED]")
    .replace(/((?:api[_-]?key|token|secret|password)\s*[=:]\s*)[^\s"'<>]+/gi, "$1[REDACTED]")
    .replace(/(AGENT_CONTROL_TASK_TOKEN\s*[=:]\s*)[^\s"'<>]+/gi, "$1[REDACTED]");
  return text.slice(0, Math.max(0, Number(maxChars) || 0));
}

function appendFailureLog(entry = {}) {
  const record = {
    schema: "agent-control/failure/v1",
    at: sanitizeFailureText(entry.at || isoNow(), 80),
    category: sanitizeFailureText(entry.category || "agent-failure", 128),
    phase: sanitizeFailureText(entry.phase || "unknown", 128),
    reason: sanitizeFailureText(entry.reason || "unknown", 512),
    agentId: sanitizeFailureText(entry.agentId, 256),
    taskId: sanitizeFailureText(entry.taskId, 256),
    workflowId: sanitizeFailureText(entry.workflowId, 256),
    swarmWaveId: sanitizeFailureText(entry.swarmWaveId, 256),
    role: sanitizeFailureText(entry.role, 128),
    status: sanitizeFailureText(entry.status, 128),
    executionMode: sanitizeFailureText(entry.executionMode, 128),
    executionProvider: sanitizeFailureText(entry.executionProvider, 128),
    machine: sanitizeFailureText(entry.machine, 256),
    branchName: sanitizeFailureText(entry.branchName, 512),
    exitCode: Number.isFinite(Number(entry.exitCode)) ? Number(entry.exitCode) : null,
    signal: sanitizeFailureText(entry.signal, 128),
    error: sanitizeFailureText(entry.error, 8000),
    lastMessage: sanitizeFailureText(entry.lastMessage, 8000)
  };
  try {
    fs.appendFileSync(FAILURE_LOG_FILE, `${JSON.stringify(record)}\n`, "utf8");
    return record;
  } catch {
    return null;
  }
}

function recordAgentFailure(agent, entry = {}) {
  if (!agent || agent.failureLoggedAt) return null;
  const record = appendFailureLog({
    ...entry,
    agentId: agent.id,
    taskId: agent.taskId,
    workflowId: agent.workflowId,
    swarmWaveId: agent.swarmWaveId,
    role: agent.role,
    status: agent.status,
    executionMode: agent.executionMode,
    executionProvider: agent.executionProvider,
    machine: agent.machine,
    branchName: agent.branchName,
    exitCode: entry.exitCode ?? agent.exitCode,
    signal: entry.signal ?? agent.signal,
    error: entry.error ?? agent.error,
    lastMessage: entry.lastMessage ?? agent.lastMessage
  });
  if (record) {
    agent.failureLoggedAt = record.at;
    agent.failureLogReason = record.reason;
  }
  return record;
}

function readFailureLog(limit = 80) {
  const bounded = Math.max(1, Math.min(500, Math.floor(Number(limit) || 80)));
  const lines = tailFile(FAILURE_LOG_FILE, 512 * 1024).split(/\r?\n/).filter(Boolean).reverse();
  const failures = [];
  for (const line of lines) {
    if (failures.length >= bounded) break;
    try {
      const parsed = JSON.parse(line);
      if (parsed && parsed.schema === "agent-control/failure/v1") failures.push(parsed);
    } catch {}
  }
  return failures;
}

async function git(args, cwd = REPO, options = {}) {
  const { stdout, stderr } = await execFileAsync("git", ["-C", cwd, ...args], {
    windowsHide: true,
    maxBuffer: options.maxBuffer || 4 * 1024 * 1024
  });
  return `${stdout || ""}${stderr || ""}`.trim();
}

async function listBranchInventory() {
  await git(["fetch", "origin", "--prune"]);
  const local = await git(["for-each-ref", "--format=%(refname:strip=2)", "refs/heads"]);
  const remote = await git(["for-each-ref", "--format=%(refname:strip=3)", "refs/remotes/origin"]);
  const inventory = new Map();

  for (const name of local.split(/\r?\n/).map(value => value.trim()).filter(Boolean)) {
    const row = inventory.get(name) || { name, local: false, remote: false };
    row.local = true;
    inventory.set(name, row);
  }
  for (const name of remote.split(/\r?\n/).map(value => value.trim()).filter(Boolean)) {
    if (name === "HEAD") continue;
    const row = inventory.get(name) || { name, local: false, remote: false };
    row.remote = true;
    inventory.set(name, row);
  }

  return [...inventory.values()].sort((a, b) => a.name.localeCompare(b.name));
}

async function listCheckedOutBranches() {
  const output = await git(["worktree", "list", "--porcelain"]);
  return output
    .split(/\r?\n/)
    .filter(line => line.startsWith("branch refs/heads/"))
    .map(line => line.slice("branch refs/heads/".length).trim())
    .filter(Boolean);
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

async function branchTipOnMain(branchSha) {
  if (!branchSha) return false;
  try {
    await git(["merge-base", "--is-ancestor", branchSha, "origin/main"]);
    return true;
  } catch {
    return false;
  }
}

async function branchDeltaMatchesMainTree(baseSha, branchSha) {
  if (!baseSha || !branchSha) return false;

  let changedPaths;
  try {
    const raw = await git(["diff", "--name-only", "--no-renames", baseSha, branchSha]);
    changedPaths = raw.split(/\r?\n/).map(value => value.trim()).filter(Boolean);
  } catch {
    return false;
  }

  if (!changedPaths.length) return true;

  async function objectAt(revision, relativePath) {
    try {
      return await git(["rev-parse", "--verify", `${revision}:${relativePath}`]);
    } catch {
      return null;
    }
  }

  for (const relativePath of changedPaths) {
    const [branchObject, mainObject] = await Promise.all([
      objectAt(branchSha, relativePath),
      objectAt("origin/main", relativePath)
    ]);
    if (branchObject !== mainObject) return false;
  }

  return true;
}

async function cleanupIntegratedBranchForTask(agent, task) {
  const branchName = String(task?.branchName || agent?.branchName || "").trim();
  if (!branchName || BRANCH_POLICY_RESERVED.includes(branchName)) {
    return { status: "preserved", reason: "reserved-or-missing-branch", remotePresent: false };
  }

  await git(["fetch", "origin", "--prune"]);

  let branchSha = agent?.currentSha || task?.branchSha || null;
  if (!branchSha) {
    for (const ref of [`refs/heads/${branchName}`, `refs/remotes/origin/${branchName}`]) {
      try {
        branchSha = await git(["rev-parse", "--verify", ref]);
        if (branchSha) break;
      } catch {}
    }
  }

  const remotePresentBefore = Boolean(await remoteHeadSha(branchName));
  if (!branchSha) {
    return {
      status: remotePresentBefore ? "cleanup-required" : "preserved",
      reason: remotePresentBefore ? "branch-tip-unresolved" : "branch-already-absent-without-tip-proof",
      remotePresent: remotePresentBefore,
      branchSha: null
    };
  }

  const integrated = await branchTipOnMain(branchSha);
  if (!integrated) {
    return {
      status: "preserved",
      reason: "unique-or-unmerged-work",
      remotePresent: remotePresentBefore,
      branchSha
    };
  }

  const baseSha = task?.baseSha || agent?.baseSha || null;
  const canonicalTreeContainsBranchDelta = await branchDeltaMatchesMainTree(baseSha, branchSha);
  if (!canonicalTreeContainsBranchDelta) {
    return {
      status: "preserved",
      reason: "branch-tip-ancestor-without-canonical-tree-proof",
      remotePresent: remotePresentBefore,
      branchSha
    };
  }

  if (agent?.worktree && fs.existsSync(agent.worktree)) {
    const dirty = await git(["status", "--porcelain"], agent.worktree);
    if (dirty) {
      return {
        status: "cleanup-required",
        reason: "dirty-worktree-after-integration",
        remotePresent: remotePresentBefore,
        branchSha
      };
    }
    try {
      await git(["worktree", "remove", agent.worktree]);
    } catch (error) {
      return {
        status: "cleanup-required",
        reason: `worktree-remove-failed:${error.message || error}`,
        remotePresent: remotePresentBefore,
        branchSha
      };
    }
  }

  try {
    await git(["rev-parse", "--verify", `refs/heads/${branchName}`]);
    await git(["branch", "-D", branchName]);
  } catch {}

  const remotePresent = Boolean(await remoteHeadSha(branchName));
  const disposition = cleanupDisposition({
    branchName,
    branchSha,
    mainContainsBranchTip: true,
    canonicalTreeContainsBranchDelta: true,
    worktreeDirty: false,
    deletionSucceeded: !remotePresent
  });

  return {
    status: disposition.status,
    reason: remotePresent ? "remote-branch-pending-lifecycle-enforcer" : disposition.reason,
    remotePresent,
    branchSha
  };
}

let branchCleanupReconcileRunning = false;
async function reconcileIntegratedBranchCleanup() {
  if (branchCleanupReconcileRunning || degradedReason) return;
  branchCleanupReconcileRunning = true;
  try {
    const initial = loadState();
    const candidates = initial.tasks
      .filter(task => ["candidate", "cleanup-required"].includes(String(task.status || "")))
      .map(task => ({ taskId: task.id, agentId: task.agentId, branchName: task.branchName }));

    for (const candidate of candidates) {
      const snapshot = loadState();
      const task = snapshot.tasks.find(item => item.id === candidate.taskId);
      const agent = snapshot.agents.find(item => item.id === candidate.agentId);
      if (!task || !agent || agent.status !== "done") continue;

      let outcome;
      try {
        outcome = await cleanupIntegratedBranchForTask(agent, task);
      } catch (error) {
        outcome = {
          status: "cleanup-required",
          reason: `cleanup-reconciliation-error:${error.message || error}`,
          remotePresent: true,
          branchSha: agent.currentSha || null
        };
      }

      const current = loadState();
      const currentTask = current.tasks.find(item => item.id === candidate.taskId);
      const currentAgent = current.agents.find(item => item.id === candidate.agentId);
      if (!currentTask || !currentAgent || currentAgent.status !== "done") continue;

      currentTask.branchCleanup = {
        status: outcome.status,
        reason: outcome.reason,
        branchSha: outcome.branchSha || currentAgent.currentSha || null,
        remotePresent: Boolean(outcome.remotePresent),
        checkedAt: isoNow()
      };

      if (outcome.status === "done") {
        currentTask.status = "done";
        currentTask.finishedAt ||= isoNow();
        currentTask.blockers = (currentTask.blockers || []).filter(item => item !== "branch-cleanup");
        currentTask.nextAction = null;
        addEvent(current, "branch.cleanup-complete", `${candidate.branchName} cleanup verified; task is complete`, {
          agentId: currentAgent.id,
          taskId: currentTask.id,
          branchName: candidate.branchName
        });
      } else if (outcome.status === "cleanup-required") {
        currentTask.status = "cleanup-required";
        currentTask.blockers = Array.from(new Set([...(currentTask.blockers || []), "branch-cleanup"]));
        currentTask.nextAction = outcome.remotePresent
          ? "Close/merge any remaining PR and let Branch Lifecycle Cleanup remove the proven-safe remote branch; completion will retry automatically."
          : "Resolve the recorded branch cleanup failure; the task cannot become done until cleanup verifies.";
        addEvent(current, "branch.cleanup-required", `${candidate.branchName} still requires cleanup: ${outcome.reason}`, {
          agentId: currentAgent.id,
          taskId: currentTask.id,
          branchName: candidate.branchName
        });
      } else {
        currentTask.status = "candidate";
        currentTask.nextAction = "Integrate the branch into current origin/main before cleanup can run; unique/unmerged work is preserved.";
      }

      saveState(current);
    }
  } finally {
    branchCleanupReconcileRunning = false;
  }
}

function isTerminalStatus(status) {
  return ["done", "failed", "finished", "stopped", "interrupted", "orphaned", "capacity-blocked"].includes(status);
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
  else if (agent.status === "capacity-blocked") {
    task.status = "blocked";
    task.blockers = Array.from(new Set([...(task.blockers || []), "codex-provider-capacity"]));
    task.nextAction = "Continue deterministic builds/tests/computer-control through direct Heaven Bridge actions or proc_run; retry Codex agent dispatch after provider capacity resets.";
  }
  else if (agent.status === "stopped") task.status = "stopped";
  else if (agent.status === "finished") task.status = "finished";
  else task.status = agent.status;

  if (isTerminalStatus(agent.status)) task.finishedAt ||= isoNow();
}

function markNoWorkRecoveryPending(state, agent, decision, leaseReason = "no-work-terminal") {
  agent.failureClass = "no-work";
  agent.recoveryStatus = "retry-pending";
  agent.recoveryReason = decision.reason;
  agent.recoveryDetectedAt ||= isoNow();
  agent.recoveryNextAt = null;
  agent.completionEvidence = "no-work-terminal";
  releaseLeaseForAgent(state, agent, leaseReason);
  updateTaskForAgent(state, agent);
  const task = state.tasks.find(item => item.id === agent.taskId);
  if (task) {
    task.status = "retry-pending";
    task.finishedAt = null;
    task.blockers = (task.blockers || []).filter(item => item !== "agent-no-work");
    task.nextAction = "Agent Control detected termination without substantive work and will dispatch one bounded replacement automatically.";
  }
}

function refreshState() {
  const state = loadState();
  let changed = false;
  const now = Date.now();

  for (const agent of state.agents) {
    const last = selectAgentTerminalMessage(
      readTextIfExists(agent.lastMessagePath),
      readLogSummary(agent.logPath)
    );
    if (last && last !== agent.lastMessage) {
      agent.lastMessage = last;
      agent.lastProgressAt = isoNow();
      agent.updatedAt = isoNow();
      changed = true;
    }

    const capacityTermination = providerCapacityActiveTerminationDecision(agent);
    if (capacityTermination.terminate) {
      const detectedAt = isoNow();
      const child = agent.ownerSessionId === SESSION_ID ? children.get(agent.id) : null;
      const ownsLiveProcess = Boolean(
        child
        && child.pid === agent.pid
        && child.exitCode === null
        && child.signalCode === null
        && isPidAlive(agent.pid)
      );

      agent.status = capacityTermination.status;
      agent.failureClass = capacityTermination.failureClass;
      agent.completionEvidence = capacityTermination.completionEvidence;
      agent.providerCapacityEvidence ||= agent.lastMessage || agent.error || null;
      agent.recoveryStatus = "provider-capacity";
      agent.recoveryNextAt = null;
      agent.providerCapacityDetectedAt ||= detectedAt;
      agent.finishedAt ||= detectedAt;
      agent.updatedAt = detectedAt;
      agent.heartbeatAt = detectedAt;
      releaseLeaseForAgent(state, agent, "provider-capacity-auto-termination");
      updateTaskForAgent(state, agent);
      recordAgentFailure(agent, {
        category: "provider-capacity",
        phase: "runtime-capacity-detection",
        reason: capacityTermination.reason
      });
      addEvent(state, "agent.capacity-auto-terminated", `${agent.id} hit a hard provider usage limit and was removed from the active swarm`, {
        agentId: agent.id,
        taskId: agent.taskId,
        reason: capacityTermination.reason,
        evidence: {
          pid: agent.pid || null,
          ownedProcess: ownsLiveProcess,
          executionProvider: agent.executionProvider || "local-control"
        }
      });
      addNotification(state, {
        severity: "warning",
        title: "Usage limit reached · agent stopped",
        message: `${agent.roleLabel || agent.id} hit a hard usage/credit limit. Agent Control removed it from the active swarm and will not treat it as healthy or auto-retry the same blocked provider.`,
        action: { type: "inspect-agent", agentId: agent.id },
        dedupeKey: `capacity-auto-terminated:${agent.id}`
      });
      changed = true;

      if (ownsLiveProcess) {
        void (async () => {
          if (agent.executionProvider === "heaven-bridge" && agent.remoteJobId) {
            const cancellation = await cancelHeavenBridgeJob(agent.remoteJobId, { reason: "provider-capacity" });
            if (!bridgeResultSucceeded(cancellation)) {
              throw new Error(`Heaven Bridge quota cancellation was not authoritative: ${cancellation.status} / ${cancellation.exit_code}.`);
            }
          }
          if (isPidAlive(agent.pid)) await killProcessTree(agent.pid);
          const exited = await waitForPidExit(agent.pid);
          if (!exited) throw new Error(`PID ${agent.pid} remained alive after provider-capacity termination.`);
        })().catch(error => {
          const failed = loadState();
          const current = failed.agents.find(item => item.id === agent.id);
          if (current) {
            current.providerCapacityTerminationError = error?.message || String(error);
            current.updatedAt = isoNow();
            addEvent(failed, "agent.capacity-auto-termination-failed", `Could not prove process-tree termination for ${agent.id}`, {
              agentId: agent.id,
              taskId: agent.taskId,
              reason: current.providerCapacityTerminationError
            });
            addNotification(failed, {
              severity: "error",
              title: "Quota-blocked agent termination failed",
              message: `${current.roleLabel || current.id} is quota-blocked, but Agent Control could not terminate its owned process tree automatically.`,
              action: { type: "inspect-agent", agentId: current.id },
              dedupeKey: `capacity-auto-termination-failed:${current.id}`
            });
            saveState(failed);
          }
        });
      }
      continue;
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
        agent.recoveryStatus = "stream-lost-checking-work";
        agent.recoveryDetectedAt ||= isoNow();
        const reconciliation = terminationReconciliationDecision({
          ...agent,
          status: "interrupted",
          worktreeDirty: agent.worktreeClean === false
        }, {
          expectsRepositoryWork: true,
          streamLost: true,
          durableEvidenceChecked: agent.worktreeClean === true
            && Boolean(agent.baseSha)
            && Boolean(agent.currentSha)
        });

        if (reconciliation.recoveryStatus === "no-durable-work-detected-retry") {
          markNoWorkRecoveryPending(state, agent, {
            ...reconciliation,
            noWork: true
          }, "owned-process-missing-no-durable-work");
          addEvent(state, "agent.no-work", `${agent.id} disappeared without durable work evidence`, {
            agentId: agent.id,
            taskId: agent.taskId,
            reason: reconciliation.reason
          });
          addNotification(state, {
            severity: "warning",
            title: "Stream lost · no durable work",
            message: `${agent.roleLabel || agent.id} terminated without durable work evidence. A bounded replacement is queued.`,
            action: { type: "inspect-agent", agentId: agent.id },
            dedupeKey: `no-work:${agent.id}`
          });
        } else if (reconciliation.recoveryStatus === "work-verified-complete") {
          agent.status = "done";
          agent.failureClass = null;
          agent.recoveryStatus = "work-verified-complete";
          agent.recoveryReason = reconciliation.reason;
          agent.completionEvidence = "durable-work-verified-after-stream-loss";
          releaseLeaseForAgent(state, agent, "stream-lost-work-verified-complete");
          updateTaskForAgent(state, agent);
          const task = state.tasks.find(item => item.id === agent.taskId);
          if (task) {
            task.status = "done";
            task.finishedAt ||= isoNow();
            task.nextAction = "No restart required; durable completion evidence was verified after the stream loss.";
          }
          addEvent(state, "agent.stream-loss-complete", `${agent.id} lost its stream after verified durable completion`, {
            agentId: agent.id,
            taskId: agent.taskId,
            reason: reconciliation.reason
          });
        } else if (reconciliation.recoveryStatus === "work-detected-incomplete") {
          agent.failureClass = "stream-lost";
          agent.recoveryStatus = "work-detected-incomplete";
          agent.recoveryReason = reconciliation.reason;
          agent.completionEvidence = "durable-work-detected";
          updateTaskForAgent(state, agent);
          const task = state.tasks.find(item => item.id === agent.taskId);
          if (task) {
            task.status = "needs-attention";
            task.finishedAt = null;
            task.nextAction = "Resume or reconcile the existing branch/PR/worktree. Do not restart the assignment from scratch.";
          }
          addEvent(state, "agent.stream-loss-incomplete", `${agent.id} lost its stream after producing durable work`, {
            agentId: agent.id,
            taskId: agent.taskId,
            reason: reconciliation.reason
          });
          addNotification(state, {
            severity: "warning",
            title: "Work detected · incomplete",
            message: `${agent.roleLabel || agent.id} has durable work but no verified completion evidence. Preserve and reconcile that work instead of starting over.`,
            action: { type: "generate-takeover", agentId: agent.id },
            dedupeKey: `stream-loss-incomplete:${agent.id}`
          });
        } else {
          agent.recoveryStatus = reconciliation.recoveryStatus || "work-unverified";
          agent.recoveryReason = reconciliation.reason;
          updateTaskForAgent(state, agent);
          addEvent(state, "agent.interrupted", `${agent.id} disappeared before authoritative completion could be established`, {
            agentId: agent.id,
            taskId: agent.taskId,
            reason: reconciliation.reason || "owned-process-missing-without-exit-event"
          });
          addNotification(state, {
            severity: "warning",
            title: "Worker needs reconciliation",
            message: `${agent.roleLabel || agent.id} stopped and Agent Control could not prove completion. Existing work is preserved for inspection.`,
            action: { type: "generate-takeover", agentId: agent.id },
            dedupeKey: `interrupted:${agent.id}`
          });
        }
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
    const staleProgressMs = swarmTailRecoveryConfig(state).staleAfterMs;
    const stalled = Number.isFinite(progressAt) && now - progressAt > staleProgressMs;
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

  const federationBefore = JSON.stringify(state.federation);
  syncManagedAgents(state.federation, state.agents, { hostname: os.hostname(), now });
  if (JSON.stringify(state.federation) !== federationBefore) changed = true;

  if (changed && !degradedReason) saveState(state);
  return state;
}

async function workerSnapshot(state = refreshState()) {
  const running = state.agents.filter(agent => coreIsActiveStatus(agent.status)).length;
  const hostname = os.hostname();
  const currentId = hostname.toLowerCase();
  const policies = state.settings?.machinePolicies || {};
  const configured = new Set(Object.keys(policies).map(value => value.toLowerCase()));
  configured.add(currentId);
  const heavenBridge = currentId === "heaven2" && configured.has("heaven")
    ? await inspectHeavenBridge({ sync: false })
    : null;

  return [...configured].map(id => {
    const policy = policies[id] || null;
    if (id !== currentId) {
      const bridgeBacked = id === "heaven" && currentId === "heaven2";
      return {
        id,
        name: policy?.label || id,
        kind: bridgeBacked ? "remote-provider" : "configured",
        provider: bridgeBacked ? "heaven-bridge" : null,
        status: bridgeBacked ? bridgeMachineStatus(heavenBridge) : "configured",
        controller: false,
        lastHeartbeat: bridgeBacked ? heavenBridge?.heartbeat?.updatedAt || null : null,
        running: bridgeBacked ? heavenBridge?.heartbeat?.running ?? null : null,
        capacity: null,
        availableSlots: null,
        providerHealth: bridgeBacked ? heavenBridge : null,
        policy
      };
    }
    return {
      id,
      name: hostname,
      kind: "local",
      provider: "local-control",
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

function federationCountCoverage(snapshot) {
  const providers = Array.isArray(snapshot?.providers) ? snapshot.providers : [];
  const chatgpt = providers.find(item => item.id === "chatgpt") || null;
  const incompleteProviders = [];
  if (!chatgpt || chatgpt.discovery !== "automated" || chatgpt.status !== "online") {
    incompleteProviders.push({
      id: "chatgpt",
      label: chatgpt?.label || "ChatGPT sessions",
      discovery: chatgpt?.discovery || "unavailable",
      status: chatgpt?.status || "missing",
      reason: chatgpt?.last_error || (chatgpt?.discovery === "automated"
        ? "automatic discovery is not currently online"
        : "automatic ChatGPT session discovery is unavailable")
    });
  }
  return {
    authoritative: incompleteProviders.length === 0,
    mode: incompleteProviders.length === 0 ? "authoritative" : "lower-bound",
    incomplete_provider_ids: incompleteProviders.map(item => item.id),
    incomplete_providers: incompleteProviders,
    message: incompleteProviders.length
      ? "Known-agent counts are a lower bound because not every ChatGPT session is discoverable."
      : "All configured agent-session sources are being discovered automatically."
  };
}

async function runtimeFederationSnapshot(state = refreshState()) {
  const snapshot = federationSnapshot(state.federation, { now: Date.now() });
  snapshot.coverage = federationCountCoverage(snapshot);
  if (os.hostname().toLowerCase() !== "heaven2") return snapshot;

  const health = await inspectHeavenBridge({ sync: false });
  const provider = snapshot.providers.find(item => item.id === "heaven-bridge");
  if (provider) {
    provider.status = health.healthy ? "online" : (health.configured ? "unhealthy" : "not-configured");
    provider.last_heartbeat_at = health.heartbeat?.updatedAt || null;
    provider.last_error = health.healthy ? null : (health.reason || null);
    provider.metadata = {
      ...(provider.metadata || {}),
      execution_authority: Boolean(health.healthy),
      protocol: health.protocol || null,
      heartbeat_age_ms: health.heartbeat?.ageMs ?? null,
      running: health.heartbeat?.running ?? null
    };
  }
  snapshot.coverage = federationCountCoverage(snapshot);
  return snapshot;
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
  const branchConflict = state.leases.find(lease =>
    lease.status === "active" &&
    lease.branchName === branchName &&
    lease.ownerAgentId !== agentId
  );
  if (branchConflict) {
    throw new Error(`Branch "${branchName}" is already leased by ${branchConflict.ownerAgentId}.`);
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
  appendFailureLog({
    category: "deployment",
    phase: "pre-launch",
    reason,
    taskId,
    workflowId: converged.task?.workflowId || null,
    swarmWaveId: converged.task?.swarmWaveId || null,
    role: converged.task?.role || null,
    status: "failed",
    executionMode: converged.task?.executionMode || null,
    machine: converged.task?.machine || null,
    branchName: converged.task?.retainedBranch || converged.task?.branchName || branchName || null,
    error: error?.message || String(error)
  });
  saveState(failed);
}

async function resolveWorkerPlacement(state, machine, { repositoryWriteAuthorized = false } = {}) {
  const hostname = os.hostname().toLowerCase();
  const requested = String(machine || "auto").trim().toLowerCase();
  const autoTarget = hostname === "heaven2" ? "heaven" : hostname;
  let decision = placementTransportDecision(state, machine, {
    controllerHostname: hostname,
    heavenTransportHealthy: false
  });

  if (["", "auto"].includes(requested) && decision.target !== autoTarget) {
    throw new Error(`Machine policy target mismatch: expected ${autoTarget}, resolved ${decision.target}.`);
  }

  const permission = canUseMachineForRepositoryWrite(state, decision.target, repositoryWriteAuthorized);
  if (!permission.allowed) {
    throw new Error(`Repository-writing task blocked by machine policy on ${decision.target}: ${permission.reason}`);
  }

  if (decision.controller === "heaven2" && decision.target === "heaven") {
    const health = await inspectHeavenBridge({ sync: true });
    decision = placementTransportDecision(state, machine, {
      controllerHostname: hostname,
      heavenTransportHealthy: Boolean(health?.healthy),
      heavenTransportReason: health?.reason || null
    });
    if (!decision.allowed) {
      throw new Error(decision.reason || "Authenticated Heaven Local Bridge is unavailable; refusing to silently execute heavy work on heaven2.");
    }
    return { machine: "heaven", provider: "heaven-bridge", remote: true, health };
  }

  if (!decision.allowed) throw new Error(decision.reason);
  return {
    machine: decision.target === hostname ? os.hostname() : decision.target,
    provider: decision.provider,
    remote: decision.remote
  };
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
  additionalConstraints = [],
  repositoryTrainingManifest = [],
  swarmEvolutionContext = []
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
    repositoryTrainingManifest,
    swarmEvolutionContext,
    assignment: { taskId, priority, boundary, baseBranch, baseSha, branchName }
  });
}

async function deployOne({
  role,
  task,
  baseBranch,
  model,
  executionMode = "",
  boundary,
  priority,
  machine,
  dependencies = [],
  targetAgentId = null,
  lane = null,
  repositoryWriteAuthorized = false,
  acceptanceCriteria = [],
  verification = [],
  additionalConstraints = [],
  recoveryContext = null,
  swarmContext = null
}) {
  if (!rolePresets[role]) throw new Error(`Unknown role: ${role}`);
  if (!task || !task.trim()) throw new Error("Task is required.");

  const execution = agentExecutionModeDecision(executionMode);
  if (!execution.allowed) {
    const error = new Error(execution.reason);
    error.code = execution.code;
    error.statusCode = 409;
    throw error;
  }

  const state = refreshState();
  assertMutationsAllowed(state, { dispatch: true });
  const capacity = providerCapacityCircuit(state);
  if (capacity.blocked && process.env.AGENT_CONTROL_IGNORE_PROVIDER_CAPACITY !== "1") {
    const until = capacity.blockedUntil ? ` until ${capacity.blockedUntil}` : "";
    const error = new Error(`Codex agent dispatch is capacity-blocked${until}. Direct Heaven Bridge proc_run/build/test/computer-control remains available; retry agent dispatch after provider capacity resets.`);
    error.code = "CODEX_PROVIDER_CAPACITY";
    error.statusCode = 503;
    throw error;
  }
  const running = state.agents.filter(agent => coreIsActiveStatus(agent.status)).length;
  if (running >= MAX_ACTIVE_AGENTS) throw new Error(`Worker capacity reached (${running}/${MAX_ACTIVE_AGENTS}).`);

  const placement = await resolveWorkerPlacement(state, machine, { repositoryWriteAuthorized });
  const assignedMachine = placement.machine;
  const requestedBase = (baseBranch || "main").trim();
  const branchInventory = await listBranchInventory();
  const checkedOutBranches = await listCheckedOutBranches();
  const branchPlan = chooseBranchPlan({
    task: task.trim(),
    boundary: (boundary || "").trim(),
    lane,
    baseBranch: requestedBase,
    branches: branchInventory,
    leases: state.leases,
    tasks: state.tasks,
    agents: state.agents,
    checkedOutBranches
  });

  const stamp = new Date().toISOString().replace(/[-:TZ.]/g, "").slice(0, 14);
  const suffix = Math.random().toString(36).slice(2, 7);
  const id = `${role}-${stamp}-${suffix}`;
  const taskId = `task-${stamp}-${suffix}`;
  const generatedBranchName = `agent/control-${role}-${slugify(task)}-${stamp}-${suffix}`;
  const branchName = branchPlan.mode === "reused" && branchPlan.branchName
    ? branchPlan.branchName
    : generatedBranchName;
  const effectiveBase = branchPlan.mode === "reused" ? branchName : requestedBase;
  const baseRef = await resolveBaseRef(effectiveBase);
  const baseSha = await git(["rev-parse", baseRef]);
  const mutableBoundary = (boundary || "").trim() || `isolated:${branchName}`;
  const normalizedPriority = normalizePriority(priority);
  const taskCapability = createTaskCapability();

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
    executionMode: execution.mode,
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
    baseBranch: effectiveBase,
    requestedBaseBranch: requestedBase,
    baseSha,
    branchName,
    branchDecision: branchPlan.mode,
    branchDecisionReason: branchPlan.reason,
    branchCandidates: branchPlan.candidates,
    branchPlan: {
      mode: branchPlan.mode,
      reason: branchPlan.reason,
      reused: branchPlan.mode === "reused",
      candidates: branchPlan.candidates
    },
    dependencies: Array.isArray(dependencies) ? dependencies.filter(Boolean) : [],
    acceptanceCriteria: Array.isArray(acceptanceCriteria) ? acceptanceCriteria.filter(Boolean) : [],
    verification: Array.isArray(verification) ? verification.filter(Boolean) : [],
    evidence: [],
    blockers: [],
    nextAction: null,
    promptTemplateId: null,
    promptTemplateVersion: null,
    promptHash: null,
    workerCapabilityHash: taskCapability.hash,
    repositoryWriteAuthorized: Boolean(repositoryWriteAuthorized),
    retryOfTaskId: recoveryContext?.retryOfTaskId || null,
    recoveryRootAgentId: recoveryContext?.rootAgentId || null,
    workflowId: swarmContext?.workflowId || null,
    swarmWaveId: swarmContext?.waveId || null,
    swarmStepIndex: Number.isFinite(Number(swarmContext?.stepIndex)) ? Number(swarmContext.stepIndex) : null,
    swarmStepTotal: Number.isFinite(Number(swarmContext?.totalSteps)) ? Number(swarmContext.totalSteps) : null,
    swarmPromptGeneration: null,
    swarmPromptContextHash: null
  };
  currentState.tasks.unshift(taskRecord);
  addEvent(currentState, "task.created", `${taskId} assigned to ${id}`, { agentId: id, taskId });
  saveState(currentState);

  try {
    if (branchPlan.mode === "reused") {
      const existing = branchInventory.find(item => item.name === branchName);
      if (!existing) throw new Error(`Reusable branch disappeared before worktree creation: ${branchName}`);
      if (existing.local) {
        await git(["worktree", "add", worktree, branchName]);
      } else if (existing.remote) {
        await git(["worktree", "add", "--track", "-b", branchName, worktree, `origin/${branchName}`]);
      } else {
        throw new Error(`Reusable branch has no local or remote ref: ${branchName}`);
      }
    } else {
      await git(["worktree", "add", "-b", branchName, worktree, baseRef]);
    }
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

  let codex = null;
  let prompt;
  let logFd = null;
  const remoteExecution = placement.provider === "heaven-bridge";
  try {
    const repositoryTrainingManifest = buildRepositoryTrainingManifest(worktree, role);
    if (!remoteExecution) codex = findCodex();
    const swarmEvolution = swarmContext
      ? buildSwarmPromptEvolutionContext(refreshState(), {
          ...swarmContext,
          mission: swarmContext.mission || task
        })
      : null;
    prompt = buildPrompt({
      role,
      task,
      baseBranch: effectiveBase,
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
      repositoryTrainingManifest,
      swarmEvolutionContext: swarmEvolution?.lines || [],
      additionalConstraints: [
        ...additionalConstraints,
        branchPlan.mode === "reused"
          ? `Continue the existing compatible branch ${branchName}; do not create a replacement branch for this scope.`
          : `No safe compatible branch was found; use the assigned branch ${branchName} and delete it after verified integration.`
      ]
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
      promptedTask.swarmPromptGeneration = swarmEvolution?.generation || null;
      promptedTask.swarmPromptContextHash = prompt.swarmContextHash || null;
      promptedTask.updatedAt = isoNow();
    }
    promptedState.promptHistory.unshift({
      taskId,
      agentId: id,
      templateId: prompt.templateId,
      templateVersion: prompt.templateVersion,
      sha256: prompt.sha256,
      promptPath,
      workflowId: swarmContext?.workflowId || null,
      swarmWaveId: swarmContext?.waveId || null,
      swarmStepIndex: Number.isFinite(Number(swarmContext?.stepIndex)) ? Number(swarmContext.stepIndex) : null,
      swarmStepTotal: Number.isFinite(Number(swarmContext?.totalSteps)) ? Number(swarmContext.totalSteps) : null,
      swarmPromptGeneration: swarmEvolution?.generation || null,
      swarmContextHash: prompt.swarmContextHash || null,
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
    "-a", "never",
    "-s", "workspace-write",
    "exec",
    "--json",
    "--skip-git-repo-check",
    "-C", worktree,
    "-o", lastMessagePath
  ];
  if (model && model.trim()) args.push("-m", model.trim());
  args.push("-");

  const remoteJobId = remoteExecution ? `agent-control-${id}-execute`.toLowerCase() : null;
  const runnerSpecPath = remoteExecution ? path.join(DATA_DIR, `${id}.heaven-runner.json`) : null;
  let executable = codex;
  if (remoteExecution) {
    executable = process.execPath;
    args.length = 0;
    args.push(HEAVEN_BRIDGE_RUNNER, runnerSpecPath);
    fs.writeFileSync(runnerSpecPath, JSON.stringify({
      agentId: id,
      taskId,
      role,
      task: task.trim(),
      targetAgentId,
      baseSha,
      branchName,
      localWorktree: worktree,
      promptPath,
      lastMessagePath,
      model: model?.trim() || null,
      repositoryWriteAuthorized: true,
      controlPort: PORT,
      remoteJobId
    }, null, 2), "utf8");
  }

  let child;
  try {
    child = spawn(executable, args, {
      cwd: worktree,
      stdio: ["pipe", logFd, logFd],
      windowsHide: true,
      detached: false,
      env: {
        ...process.env,
        AGENT_CONTROL_TASK_TOKEN: taskCapability.token,
        AGENT_CONTROL_AGENT_ID: id,
        AGENT_CONTROL_TASK_ID: taskId,
        AGENT_CONTROL_PORT: String(PORT)
      }
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

  if (remoteExecution) child.stdin.end();
  else child.stdin.end(prompt.rendered);
  fs.closeSync(logFd);

  const startedAt = isoNow();
  const agent = {
    id,
    role,
    roleLabel: rolePresets[role].label,
    executionMode: execution.mode,
    taskId,
    task: task.trim(),
    targetAgentId,
    lane,
    status: "running",
    pid: child.pid,
    ownerSessionId: SESSION_ID,
    processExecutable: executable,
    processStartedAt: startedAt,
    executionProvider: placement.provider,
    remoteJobId,
    runnerSpecPath,
    completionEvidence: null,
    heartbeatAt: startedAt,
    lastProgressAt: startedAt,
    machine: assignedMachine,
    baseBranch: effectiveBase,
    requestedBaseBranch: requestedBase,
    baseSha,
    branchName,
    branchDecision: branchPlan.mode,
    branchDecisionReason: branchPlan.reason,
    branchCandidates: branchPlan.candidates,
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
    repositoryWriteAuthorized: Boolean(repositoryWriteAuthorized),
    retryOfAgentId: recoveryContext?.retryOfAgentId || null,
    retryOfFederatedAgentId: recoveryContext?.retryOfFederatedAgentId || null,
    recoveryRootAgentId: recoveryContext?.rootAgentId || null,
    recoveryAttempt: Math.max(0, Math.floor(Number(recoveryContext?.attempt) || 0)),
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
  addEvent(startedState, "agent.started", `${id} started on ${assignedMachine}`, {
    agentId: id,
    taskId,
    branchName,
    branchDecision: branchPlan.mode,
    branchDecisionReason: branchPlan.reason
  });
  saveState(startedState);
  children.set(id, child);

  child.on("exit", async (code, signal) => {
    // Collect asynchronous Git evidence before opening the authoritative state
    // mutation. Never carry a whole-registry snapshot across an await.
    let currentSha = null;
    let worktreeStatus = null;
    try { currentSha = await git(["rev-parse", branchName]); } catch {}
    try { worktreeStatus = await git(["status", "--porcelain"], worktree); } catch {}

    const current = loadState();
    const item = current.agents.find(candidate => candidate.id === id);
    const ownsExit = item?.ownerSessionId === SESSION_ID && item?.pid === child.pid;
    if (item && ownsExit) {
      item.exitCode = code;
      item.signal = signal || null;
      item.lastMessage = selectAgentTerminalMessage(
        readTextIfExists(item.lastMessagePath),
        readLogSummary(item.logPath)
      );
      item.currentSha = currentSha || item.currentSha || null;
      if (worktreeStatus !== null) {
        item.worktreeClean = worktreeStatus.length === 0;
        item.worktreeDirty = worktreeStatus.length > 0;
        item.worktreeStatusSummary = worktreeStatus ? worktreeStatus.slice(0, 4000) : "";
      }
      const authoritativeStatus = classifyAuthoritativeExit(item, code);
      item.status = authoritativeStatus;
      if (authoritativeStatus === "capacity-blocked") {
        item.failureClass = "provider-capacity";
        item.providerCapacityEvidence = item.lastMessage || item.error || null;
        item.recoveryStatus = "provider-capacity";
        item.recoveryNextAt = null;
      }
      item.finishedAt = isoNow();
      item.updatedAt = isoNow();
      item.heartbeatAt = item.finishedAt;
      const noWorkDecision = item.worktreeClean === true
        ? noWorkTerminationDecision(item)
        : { noWork: false, retry: false, reason: "worktree-cleanliness-unproven" };
      if (noWorkDecision.noWork) {
        item.status = "failed";
        markNoWorkRecoveryPending(current, item, noWorkDecision, `no-work-process-exit:${code ?? "unknown"}`);
      } else {
        item.completionEvidence = item.status === "stopped"
          ? "verified-operator-stop"
          : item.status === "capacity-blocked"
            ? "provider-capacity"
            : "authoritative-exit";
        releaseLeaseForAgent(current, item, `authoritative-process-exit:${code ?? "unknown"}`);
        updateTaskForAgent(current, item);
      }
      if (item.status === "failed" || item.status === "capacity-blocked" || noWorkDecision.noWork) {
        recordAgentFailure(item, {
          category: item.status === "capacity-blocked" ? "provider-capacity" : "agent-exit",
          phase: "process-exit",
          reason: noWorkDecision.noWork
            ? noWorkDecision.reason
            : item.status === "capacity-blocked"
              ? "provider-capacity"
              : "nonzero-or-failed-authoritative-exit",
          exitCode: code,
          signal: signal || null
        });
      }
      addEvent(current, "agent.exited", `${id} exited with ${code ?? "unknown"}`, {
        agentId: id,
        taskId,
        reason: remoteExecution ? "authoritative-heaven-bridge-runner-exit" : "authoritative-child-exit-event",
        evidence: {
          exitCode: code,
          signal: signal || null,
          currentSha: item.currentSha || null,
          executionProvider: item.executionProvider || "local-control",
          remoteJobId: item.remoteJobId || null
        }
      });
      if (noWorkDecision.noWork) {
        addNotification(current, {
          severity: "warning",
          title: "Worker stalled before doing work",
          message: `${item.roleLabel || id} terminated after only startup/output preamble. Agent Control queued a bounded automatic replacement.`,
          action: { type: "inspect-agent", agentId: id },
          dedupeKey: `no-work:${id}`
        });
      } else if (item.status === "stopped") {
        addNotification(current, {
          severity: "info",
          title: "Agent stopped",
          message: `${item.roleLabel || id} exited after an operator stop request and is not eligible for integration.`,
          action: { type: "inspect-agent", agentId: id },
          dedupeKey: `stopped:${id}`
        });
      } else if (item.status === "capacity-blocked") {
        addNotification(current, {
          severity: "warning",
          title: "Agent provider capacity reached",
          message: `${item.roleLabel || id} could not continue because Codex provider capacity is exhausted. The Heaven Bridge remains available for direct builds, tests, filesystem/process work, and computer control.`,
          action: { type: "inspect-agent", agentId: id },
          dedupeKey: `capacity-blocked:${id}`
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
      if (noWorkDecision.noWork) setImmediate(() => void recoverNoWorkAgent(id));
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
      recordAgentFailure(item, {
        category: "spawn-error",
        phase: "process-error",
        reason: "spawn-error-no-owned-process",
        error: error.message || String(error)
      });
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

function noWorkRecoveryConfig(state) {
  const raw = state?.settings?.noWorkRecovery || {};
  return {
    enabled: raw.enabled !== false,
    maxRetries: Math.max(0, Math.floor(Number(raw.maxRetries) || 2)),
    maxDispatchFailures: Math.max(1, Math.floor(Number(raw.maxDispatchFailures) || 5)),
    retryBackoffMs: Math.max(1_000, Number(raw.retryBackoffMs) || 15_000),
    maxBackoffMs: Math.max(1_000, Number(raw.maxBackoffMs) || 300_000)
  };
}

function goToWorkRecoveryConfig(state) {
  const raw = state?.settings?.goToWorkRecovery || {};
  const browser = String(raw.browser || "brave").trim().toLowerCase();
  return {
    enabled: raw.enabled !== false,
    browser: ["brave", "edge", "chrome"].includes(browser) ? browser : "brave",
    staleAfterMs: Math.max(10_000, Number(raw.staleAfterMs) || 45_000),
    cooldownMs: Math.max(10_000, Number(raw.cooldownMs) || 60_000),
    maxPerSweep: Math.max(1, Math.min(8, Math.floor(Number(raw.maxPerSweep) || 2))),
    adaptiveSignatures: raw.adaptiveSignatures !== false
  };
}

function loadWorkHandoffSignatures() {
  try {
    if (!fs.existsSync(WORK_HANDOFF_SIGNATURES_FILE)) return normalizeWorkHandoffRegistry({});
    const parsed = JSON.parse(fs.readFileSync(WORK_HANDOFF_SIGNATURES_FILE, "utf8"));
    return normalizeWorkHandoffRegistry(parsed);
  } catch {
    return normalizeWorkHandoffRegistry({});
  }
}

function saveWorkHandoffSignatures(value) {
  const registry = normalizeWorkHandoffRegistry(value);
  fs.mkdirSync(DATA_DIR, { recursive: true });
  const tmp = `${WORK_HANDOFF_SIGNATURES_FILE}.tmp-${process.pid}-${Date.now()}`;
  fs.writeFileSync(tmp, `${JSON.stringify(registry, null, 2)}\n`, "utf8");
  fs.renameSync(tmp, WORK_HANDOFF_SIGNATURES_FILE);
  return registry;
}

function browserExecutable(browser) {
  return {
    brave: "brave.exe",
    edge: "msedge.exe",
    chrome: "chrome.exe"
  }[browser] || "brave.exe";
}

function bridgeDesktopJobId(action) {
  return `agent-control-${String(action || "desktop").replace(/[^a-z0-9_-]+/gi, "-")}-${Date.now()}-${randomUUID().slice(0, 8)}`;
}

async function runHeaven2DesktopAction(action, params, timeoutMs = 60_000) {
  const result = await runHeaven2BridgeAction({
    id: bridgeDesktopJobId(action),
    action,
    params,
    timeoutMs,
    priority: "high"
  });
  if (!bridgeResultSucceeded(result)) {
    throw new Error(`Heaven2 desktop action ${action} failed with status ${result?.status || "unknown"} / exit ${result?.exit_code ?? "unknown"}.`);
  }
  return result?.data ?? null;
}

function waitMs(ms) {
  return new Promise(resolve => setTimeout(resolve, Math.max(0, Number(ms) || 0)));
}

function swarmTailRecoveryConfig(state) {
  const raw = state?.settings?.swarmTailRecovery || {};
  return {
    enabled: raw.enabled !== false,
    replaceStale: raw.replaceStale !== false,
    staleAfterMs: Math.max(60_000, Number(raw.staleAfterMs) || STALE_PROGRESS_MS),
    retryBaseMs: Math.max(5_000, Number(raw.retryBaseMs) || 15_000),
    retryMaxMs: Math.max(15_000, Number(raw.retryMaxMs) || 300_000),
    maxWorkers: Math.max(1, Math.min(MAX_ACTIVE_AGENTS, Math.floor(Number(raw.maxWorkers) || 4))),
    maxAttemptsPerRoot: Math.max(1, Math.floor(Number(raw.maxAttemptsPerRoot) || 4)),
    armedAt: raw.armedAt || null,
    armedWorkflowId: raw.armedWorkflowId || null,
    armedMission: raw.armedMission || null,
    waveId: raw.waveId || null
  };
}

function liveRecoveryReplacement(state, source) {
  const root = source.recoveryRootAgentId || source.id;
  return state.agents.find(candidate =>
    candidate.id !== source.id &&
    coreIsActiveStatus(candidate.status) &&
    (
      candidate.retryOfAgentId === source.id ||
      candidate.recoveryRootAgentId === root
    )
  ) || null;
}

function liveBoundaryConflict(state, source) {
  const boundary = String(source.boundary || "").trim().toLowerCase();
  if (!boundary) return null;
  return state.leases.find(lease =>
    lease.status === "active" &&
    String(lease.boundary || "").trim().toLowerCase() === boundary &&
    lease.ownerAgentId !== source.id
  ) || null;
}

async function recoverNoWorkAgent(agentId) {
  if (noWorkRecoveryOperations.has(agentId)) return null;
  noWorkRecoveryOperations.add(agentId);
  try {
    const state = refreshState();
    const source = state.agents.find(item => item.id === agentId);
    if (!source || source.failureClass !== "no-work") return null;
    if (!["retry-pending", "retry-waiting"].includes(source.recoveryStatus)) return null;

    const config = noWorkRecoveryConfig(state);
    if (!config.enabled) {
      source.recoveryStatus = "retry-disabled";
      const task = state.tasks.find(item => item.id === source.taskId);
      if (task) {
        task.status = "failed";
        task.finishedAt ||= isoNow();
        task.nextAction = "Automatic no-work recovery is disabled.";
      }
      saveState(state);
      return null;
    }

    const nextAt = Date.parse(source.recoveryNextAt || "");
    if (Number.isFinite(nextAt) && nextAt > Date.now()) return null;

    const attempt = Math.max(0, Math.floor(Number(source.recoveryAttempt) || 0));
    if (attempt >= config.maxRetries) {
      source.recoveryStatus = "retry-exhausted";
      const task = state.tasks.find(item => item.id === source.taskId);
      if (task) {
        task.status = "failed";
        task.finishedAt ||= isoNow();
        task.blockers = Array.from(new Set([...(task.blockers || []), "no-work-retry-exhausted"]));
        task.nextAction = "Automatic no-work retry limit reached; inspect the provider/session failure before another dispatch.";
      }
      addNotification(state, {
        severity: "error",
        title: "No-work retry limit reached",
        message: `${source.roleLabel || source.id} exhausted ${config.maxRetries} automatic replacement attempts.`,
        action: { type: "inspect-agent", agentId: source.id },
        dedupeKey: `no-work-exhausted:${source.id}`
      });
      saveState(state);
      return null;
    }

    const existing = liveRecoveryReplacement(state, source);
    if (existing) {
      source.recoveryStatus = "retry-dispatched";
      source.replacementAgentId = existing.id;
      source.replacementTaskId = existing.taskId || null;
      source.recoveryNextAt = null;
      saveState(state);
      return existing;
    }

    const conflict = liveBoundaryConflict(state, source);
    if (conflict) {
      source.recoveryStatus = "retry-waiting";
      source.recoveryNextAt = new Date(Date.now() + config.retryBackoffMs).toISOString();
      source.recoveryWaitReason = `boundary-owned-by:${conflict.ownerAgentId}`;
      saveState(state);
      return null;
    }

    const task = state.tasks.find(item => item.id === source.taskId) || null;
    if (source.worktree && fs.existsSync(source.worktree)) {
      const residual = await git(["status", "--porcelain"], source.worktree);
      if (residual) {
        source.recoveryStatus = "retry-blocked";
        source.recoveryLastError = "Dead worker worktree contains uncommitted changes; refusing automatic cleanup or duplicate dispatch.";
        source.recoveryNextAt = null;
        if (task) {
          task.status = "blocked";
          task.blockers = Array.from(new Set([...(task.blockers || []), "no-work-worktree-became-dirty"]));
          task.nextAction = "Inspect and preserve the dead worker worktree before retrying.";
        }
        addNotification(state, {
          severity: "error",
          title: "Automatic retry preserved unexpected work",
          message: `${source.roleLabel || source.id} has uncommitted work in its worktree, so Agent Control refused to delete it or create a competing replacement.`,
          action: { type: "inspect-agent", agentId: source.id },
          dedupeKey: `no-work-dirty-worktree:${source.id}`
        });
        saveState(state);
        return null;
      }
      await git(["worktree", "remove", "--force", source.worktree]);
      await git(["worktree", "prune"]);
      source.worktreeReleasedAt = isoNow();
    }

    const replacement = await deployOne({
      role: rolePresets[source.role] ? source.role : "support",
      task: source.task || task?.objective || "Resume the interrupted execution assignment and complete it with verified evidence.",
      baseBranch: source.requestedBaseBranch || source.baseBranch || "main",
      model: source.model || "",
      boundary: source.boundary || task?.boundary || `retry:${source.id}`,
      priority: source.priority ?? task?.priority ?? 60,
      machine: source.machine || task?.machine || "auto",
      dependencies: task?.dependencies || source.dependencies || [],
      targetAgentId: source.targetAgentId || task?.targetAgentId || null,
      lane: source.lane || task?.lane || null,
      repositoryWriteAuthorized: Boolean(source.repositoryWriteAuthorized ?? task?.repositoryWriteAuthorized),
      acceptanceCriteria: task?.acceptanceCriteria || [],
      verification: task?.verification || [],
      additionalConstraints: [
        `Automatic no-work recovery retry ${attempt + 1}/${config.maxRetries} for ${source.id}.`,
        "Do not stop after announcing an execution plan. Perform the assigned work, verify it, and return concrete evidence."
      ],
      recoveryContext: {
        attempt: attempt + 1,
        retryOfAgentId: source.id,
        retryOfTaskId: source.taskId || null,
        rootAgentId: source.recoveryRootAgentId || source.id
      }
    });

    const linked = loadState();
    const original = linked.agents.find(item => item.id === source.id);
    const created = linked.agents.find(item => item.id === replacement.id);
    const originalTask = linked.tasks.find(item => item.id === source.taskId);
    const createdTask = linked.tasks.find(item => item.id === replacement.taskId);
    const rootId = source.recoveryRootAgentId || source.id;

    if (original) {
      original.recoveryStatus = "retry-dispatched";
      original.replacementAgentId = replacement.id;
      original.replacementTaskId = replacement.taskId;
      original.recoveryNextAt = null;
    }
    if (created) {
      created.retryOfAgentId = source.id;
      created.recoveryRootAgentId = rootId;
      created.recoveryAttempt = attempt + 1;
    }
    if (originalTask) {
      originalTask.status = "superseded";
      originalTask.finishedAt ||= isoNow();
      originalTask.nextAction = `Automatic replacement ${replacement.id} is running.`;
    }
    if (createdTask) {
      createdTask.retryOfTaskId = source.taskId || null;
      createdTask.recoveryRootAgentId = rootId;
    }
    addEvent(linked, "agent.no-work-requeued", `${source.id} was automatically replaced by ${replacement.id}`, {
      agentId: source.id,
      taskId: source.taskId,
      reason: source.recoveryReason || "no-work",
      evidence: { replacementAgentId: replacement.id, replacementTaskId: replacement.taskId, attempt: attempt + 1 }
    });
    saveState(linked);
    return created || replacement;
  } catch (error) {
    const failed = loadState();
    const source = failed.agents.find(item => item.id === agentId);
    if (source && source.failureClass === "no-work") {
      const config = noWorkRecoveryConfig(failed);
      const dispatchFailures = Math.max(0, Math.floor(Number(source.recoveryDispatchFailures) || 0)) + 1;
      source.recoveryDispatchFailures = dispatchFailures;
      source.recoveryLastError = error?.message || String(error);
      if (dispatchFailures >= config.maxDispatchFailures) {
        source.recoveryStatus = "retry-blocked";
        source.recoveryNextAt = null;
        const task = failed.tasks.find(item => item.id === source.taskId);
        if (task) {
          task.status = "blocked";
          task.blockers = Array.from(new Set([...(task.blockers || []), "no-work-retry-dispatch-failed"]));
          task.nextAction = "Automatic retry dispatch repeatedly failed; inspect Agent Control/provider health.";
        }
        addNotification(failed, {
          severity: "error",
          title: "Automatic retry is blocked",
          message: source.recoveryLastError,
          action: { type: "inspect-agent", agentId: source.id },
          dedupeKey: `no-work-retry-blocked:${source.id}`
        });
      } else {
        const waitMs = recoveryBackoffMs(dispatchFailures, {
          baseMs: config.retryBackoffMs,
          maxMs: config.maxBackoffMs
        });
        source.recoveryStatus = "retry-waiting";
        source.recoveryNextAt = new Date(Date.now() + waitMs).toISOString();
      }
      addEvent(failed, "agent.no-work-retry-failed", `Automatic replacement dispatch failed for ${source.id}`, {
        agentId: source.id,
        taskId: source.taskId,
        reason: source.recoveryLastError,
        evidence: { dispatchFailures }
      });
      saveState(failed);
    }
    return null;
  } finally {
    noWorkRecoveryOperations.delete(agentId);
  }
}

async function recoverFederatedNoWorkAgent(agentId) {
  const operationId = `federated:${agentId}`;
  if (noWorkRecoveryOperations.has(operationId)) return null;
  noWorkRecoveryOperations.add(operationId);
  try {
    const state = refreshState();
    const source = state.federation?.agents?.find(item => item.agent_id === agentId);
    if (!source || !["retry-pending", "retry-waiting"].includes(source.recovery_status)) return null;

    const config = noWorkRecoveryConfig(state);
    if (!config.enabled) {
      source.recovery_status = "retry-disabled";
      saveState(state);
      return null;
    }
    const nextAt = Date.parse(source.recovery_next_at || "");
    if (Number.isFinite(nextAt) && nextAt > Date.now()) return null;

    const attempt = Math.max(0, Math.floor(Number(source.recovery_attempt) || 0));
    if (attempt >= config.maxRetries) {
      source.recovery_status = "retry-exhausted";
      addNotification(state, {
        severity: "error",
        title: "Federated no-work retry limit reached",
        message: `${source.role || source.agent_id} exhausted ${config.maxRetries} automatic replacement attempts.`,
        action: { type: "inspect-federation", agentId: source.agent_id },
        dedupeKey: `federated-no-work-exhausted:${source.agent_id}`
      });
      saveState(state);
      return null;
    }

    const existing = state.agents.find(candidate =>
      coreIsActiveStatus(candidate.status) &&
      candidate.retryOfFederatedAgentId === source.agent_id
    );
    if (existing) {
      source.recovery_status = "retry-dispatched";
      source.replacement_agent_id = existing.id;
      source.replacement_task_id = existing.taskId || null;
      source.recovery_next_at = null;
      saveState(state);
      return existing;
    }

    const metadata = source.source_metadata && typeof source.source_metadata === "object"
      ? source.source_metadata
      : {};
    if (!source.task) {
      source.recovery_status = "retry-blocked";
      source.recovery_last_error = "Federated session did not provide a task body for safe retry.";
      saveState(state);
      return null;
    }

    const replacement = await deployOne({
      role: rolePresets[source.role] ? source.role : "support",
      task: source.task,
      baseBranch: metadata.base_branch || source.branch || "main",
      model: metadata.model || "",
      boundary: metadata.boundary || `federated-retry:${source.task_id || source.agent_id}`,
      priority: metadata.priority ?? 60,
      machine: recoveryMachineTarget(metadata.machine || source.machine, state.settings?.machinePolicies),
      dependencies: Array.isArray(metadata.dependencies) ? metadata.dependencies : [],
      targetAgentId: metadata.target_agent_id || null,
      lane: metadata.lane || null,
      repositoryWriteAuthorized: Boolean(metadata.repository_write_authorized),
      acceptanceCriteria: Array.isArray(metadata.acceptance_criteria) ? metadata.acceptance_criteria : [],
      verification: Array.isArray(metadata.verification) ? metadata.verification : [],
      additionalConstraints: [
        `Automatic recovery for federated session ${source.agent_id}; retry ${attempt + 1}/${config.maxRetries}.`,
        "Do not stop after announcing an execution plan. Perform the assigned work, verify it, and return concrete evidence."
      ],
      recoveryContext: {
        attempt: attempt + 1,
        retryOfFederatedAgentId: source.agent_id,
        retryOfTaskId: source.task_id || null,
        rootAgentId: source.agent_id
      }
    });

    const linked = loadState();
    const federated = linked.federation?.agents?.find(item => item.agent_id === source.agent_id);
    const created = linked.agents.find(item => item.id === replacement.id);
    if (federated) {
      federated.recovery_status = "retry-dispatched";
      federated.replacement_agent_id = replacement.id;
      federated.replacement_task_id = replacement.taskId;
      federated.recovery_next_at = null;
    }
    if (created) {
      created.retryOfFederatedAgentId = source.agent_id;
      created.recoveryRootAgentId = source.agent_id;
      created.recoveryAttempt = attempt + 1;
    }
    addEvent(linked, "federation.no-work-requeued", `${source.agent_id} was automatically replaced by ${replacement.id}`, {
      agentId: replacement.id,
      taskId: replacement.taskId,
      reason: source.recovery_reason || "federated-no-work",
      evidence: { federatedAgentId: source.agent_id, attempt: attempt + 1 }
    });
    saveState(linked);
    return created || replacement;
  } catch (error) {
    const failed = loadState();
    const source = failed.federation?.agents?.find(item => item.agent_id === agentId);
    if (source) {
      const config = noWorkRecoveryConfig(failed);
      const dispatchFailures = Math.max(0, Math.floor(Number(source.recovery_dispatch_failures) || 0)) + 1;
      source.recovery_dispatch_failures = dispatchFailures;
      source.recovery_last_error = error?.message || String(error);
      if (dispatchFailures >= config.maxDispatchFailures) {
        source.recovery_status = "retry-blocked";
        source.recovery_next_at = null;
      } else {
        source.recovery_status = "retry-waiting";
        source.recovery_next_at = new Date(Date.now() + recoveryBackoffMs(dispatchFailures, {
          baseMs: config.retryBackoffMs,
          maxMs: config.maxBackoffMs
        })).toISOString();
      }
      saveState(failed);
    }
    return null;
  } finally {
    noWorkRecoveryOperations.delete(operationId);
  }
}


async function dismissGoToWorkPrompt(agent, decision, config) {
  const operationId = String(agent?.agent_id || "");
  if (!operationId || goToWorkRecoveryOperations.has(operationId)) return null;
  goToWorkRecoveryOperations.add(operationId);

  let openedHwnd = null;
  let outcome = null;
  try {
    const beforeRows = await runHeaven2DesktopAction("window_list", { limit: 400, visible_only: true }, 45_000);
    const before = new Set((Array.isArray(beforeRows) ? beforeRows : []).map(row => Number(row?.hwnd)).filter(Number.isFinite));

    await runHeaven2DesktopAction("app_launch", {
      path: browserExecutable(config.browser),
      args: ["--new-window", decision.url],
      detached: true
    }, 45_000);

    for (const pause of [1200, 1800, 2500]) {
      await waitMs(pause);
      const rows = await runHeaven2DesktopAction("window_list", { limit: 400, visible_only: true }, 45_000);
      const candidates = (Array.isArray(rows) ? rows : []).filter(row =>
        !before.has(Number(row?.hwnd))
        && /chatgpt/i.test(String(row?.title || ""))
      );
      if (candidates.length) {
        openedHwnd = Number(candidates[0].hwnd);
        break;
      }
    }

    if (!Number.isInteger(openedHwnd) || openedHwnd <= 0) {
      throw new Error("Opened ChatGPT recovery window could not be identified safely.");
    }

    const tree = await runHeaven2DesktopAction("uia_tree", {
      hwnd: openedHwnd,
      include_root: false,
      max_nodes: 1000,
      max_depth: 12
    }, 45_000);
    let registry = loadWorkHandoffSignatures();
    const detection = detectWorkHandoffAction(tree, registry);

    if (!detection.detected) {
      if (config.adaptiveSignatures && detection.reason === "adaptive-signature-ambiguous") {
        registry = saveWorkHandoffSignatures(recordWorkHandoffDrift(registry, detection, tree));
        outcome = {
          dismissed: false,
          verified: false,
          driftDetected: true,
          registryVersion: registry.version,
          reason: detection.reason
        };
        return outcome;
      }
      outcome = { dismissed: false, verified: true, reason: "work-handoff-card-not-present" };
      return outcome;
    }

    const declineName = String(detection?.decline?.name || "").trim();
    const acceptName = String(detection?.accept?.name || "").trim();
    if (!declineName || !acceptName) {
      throw new Error("Work handoff detection produced incomplete action labels.");
    }

    await runHeaven2DesktopAction("uia_invoke", {
      hwnd: openedHwnd,
      selector: { name: declineName, control_type: "Button", enabled: true, offscreen: false },
      scope: "descendants",
      max_nodes: 1000,
      max_depth: 12
    }, 30_000);

    await waitMs(700);
    const remaining = await runHeaven2DesktopAction("uia_find", {
      hwnd: openedHwnd,
      selector: { name: acceptName, control_type: "Button", enabled: true, offscreen: false },
      scope: "descendants",
      max_nodes: 1000,
      max_depth: 12
    }, 30_000);

    if (Number(remaining?.count) > 0) {
      throw new Error(`Work handoff card remained visible after Agent Control invoked "${declineName}".`);
    }

    let learnedSignature = false;
    if (config.adaptiveSignatures && detection.learned) {
      const learned = learnWorkHandoffSignature(registry, detection, { verification: "card-cleared" });
      registry = saveWorkHandoffSignatures(learned);
      learnedSignature = true;
    }

    outcome = {
      dismissed: true,
      verified: true,
      learnedSignature,
      registryVersion: registry.version,
      declineLabel: declineName,
      acceptLabel: acceptName,
      reason: detection.learned ? "adaptive-signature-learned" : "known-signature-dismissed"
    };
    return outcome;
  } finally {
    if (Number.isInteger(openedHwnd) && openedHwnd > 0) {
      try {
        await runHeaven2DesktopAction("window_close", { hwnd: openedHwnd }, 30_000);
      } catch {}
    }
    goToWorkRecoveryOperations.delete(operationId);
  }
}

async function recoverGoToWorkAgent(agentId, decision, config) {
  const state = refreshState();
  const live = state.federation?.agents?.find(item => item.agent_id === agentId);
  if (!live) return null;
  const metadata = live.source_metadata && typeof live.source_metadata === "object"
    ? live.source_metadata
    : (live.source_metadata = {});

  const checkedAt = isoNow();
  try {
    const outcome = await dismissGoToWorkPrompt(live, decision, config);
    const next = loadState();
    const stored = next.federation?.agents?.find(item => item.agent_id === agentId);
    if (!stored) return outcome;
    stored.source_metadata = stored.source_metadata && typeof stored.source_metadata === "object"
      ? stored.source_metadata
      : {};
    stored.source_metadata.go_to_work_recovery_last_checked_at = checkedAt;
    stored.source_metadata.go_to_work_recovery_status = outcome?.dismissed ? "dismissed" : (outcome?.driftDetected ? "ui-drift-detected" : "not-present");
    stored.source_metadata.go_to_work_recovery_reason = outcome?.reason || decision.reason;
    stored.source_metadata.go_to_work_recovery_error_count = 0;
    if (outcome?.registryVersion) {
      stored.source_metadata.go_to_work_signature_registry_version = outcome.registryVersion;
    }
    if (outcome?.driftDetected) {
      addEvent(next, "federation.work-handoff-ui-drift", `${agentId} exposed an unrecognized Work handoff card shape`, {
        agentIds: [agentId],
        reason: outcome.reason,
        evidence: { registryVersion: outcome.registryVersion }
      });
      addNotification(next, {
        severity: "warning",
        title: "Work handoff UI changed",
        message: `${agentId}: Agent Control captured the new accessible card signature but refused to guess between ambiguous actions.`,
        action: { type: "inspect-agent", agentId },
        dedupeKey: `work-handoff-ui-drift:${agentId}:${outcome.registryVersion || "unknown"}`
      });
    }
    if (outcome?.dismissed) {
      stored.source_metadata.go_to_work_pending = false;
      stored.source_metadata.work_handoff_pending = false;
      stored.source_metadata.handoff_pending = false;
      stored.source_metadata.go_to_work_recovery_last_dismissed_at = checkedAt;
      stored.source_metadata.go_to_work_recovery_dismiss_count =
        Math.max(0, Number(stored.source_metadata.go_to_work_recovery_dismiss_count) || 0) + 1;
      addEvent(next, "federation.go-to-work-dismissed", `${agentId} had its Work handoff rejected automatically`, {
        agentIds: [agentId],
        reason: decision.reason,
        evidence: {
          verified: outcome?.verified === true,
          declineLabel: outcome?.declineLabel || null,
          acceptLabel: outcome?.acceptLabel || null,
          registryVersion: outcome?.registryVersion || null
        }
      });
      if (outcome?.learnedSignature) {
        addEvent(next, "federation.work-handoff-signature-learned", `${agentId} taught Agent Control a renamed Work handoff card`, {
          agentIds: [agentId],
          reason: outcome.reason,
          evidence: {
            declineLabel: outcome.declineLabel,
            acceptLabel: outcome.acceptLabel,
            registryVersion: outcome.registryVersion
          }
        });
      }
    }
    saveState(next);
    return outcome;
  } catch (error) {
    const next = loadState();
    const stored = next.federation?.agents?.find(item => item.agent_id === agentId);
    if (stored) {
      stored.source_metadata = stored.source_metadata && typeof stored.source_metadata === "object"
        ? stored.source_metadata
        : {};
      stored.source_metadata.go_to_work_recovery_last_checked_at = checkedAt;
      stored.source_metadata.go_to_work_recovery_status = "error";
      stored.source_metadata.go_to_work_recovery_last_error = error?.message || String(error);
      stored.source_metadata.go_to_work_recovery_error_count =
        Math.max(0, Number(stored.source_metadata.go_to_work_recovery_error_count) || 0) + 1;
      addEvent(next, "federation.go-to-work-recovery-error", `Automatic Work handoff dismissal failed for ${agentId}`, {
        agentIds: [agentId],
        reason: stored.source_metadata.go_to_work_recovery_last_error
      });
      if (stored.source_metadata.go_to_work_recovery_error_count >= 3) {
        addNotification(next, {
          severity: "warning",
          title: "Work handoff auto-dismiss needs attention",
          message: `${agentId}: ${stored.source_metadata.go_to_work_recovery_last_error}`,
          action: { type: "inspect-agent", agentId },
          dedupeKey: `go-to-work-recovery-error:${agentId}`
        });
      }
      saveState(next);
    }
    return null;
  }
}

async function reconcileGoToWorkRecoveries() {
  if (goToWorkRecoveryTickRunning) return;
  goToWorkRecoveryTickRunning = true;
  try {
    const state = refreshState();
    const config = goToWorkRecoveryConfig(state);
    if (!config.enabled || state.settings?.emergencyStop || state.settings?.readOnly) return;
    const plan = planGoToWorkRecoveries(state.federation, config);
    for (const item of plan) {
      await recoverGoToWorkAgent(item.agent.agent_id, item.decision, config);
    }
  } finally {
    goToWorkRecoveryTickRunning = false;
  }
}

async function reconcileNoWorkRecoveries() {
  if (noWorkRecoveryTickRunning) return;
  noWorkRecoveryTickRunning = true;
  try {
    const state = refreshState();
    const now = Date.now();
    const managed = state.agents.filter(agent => {
      if (agent.failureClass !== "no-work") return false;
      if (!["retry-pending", "retry-waiting"].includes(agent.recoveryStatus)) return false;
      const nextAt = Date.parse(agent.recoveryNextAt || "");
      return !Number.isFinite(nextAt) || nextAt <= now;
    }).slice(0, 2);
    for (const agent of managed) await recoverNoWorkAgent(agent.id);

    const federated = (state.federation?.agents || []).filter(agent => {
      if (!["retry-pending", "retry-waiting"].includes(agent.recovery_status)) return false;
      const nextAt = Date.parse(agent.recovery_next_at || "");
      return !Number.isFinite(nextAt) || nextAt <= now;
    }).slice(0, 2);
    for (const agent of federated) await recoverFederatedNoWorkAgent(agent.agent_id);
  } finally {
    noWorkRecoveryTickRunning = false;
  }
}

async function recoverSwarmTailAgent(sourceId, rootId, attempt) {
  const operationId = `swarm-tail:${rootId}`;
  if (swarmTailRecoveryOperations.has(operationId)) return null;
  swarmTailRecoveryOperations.add(operationId);
  try {
    let state = refreshState();
    if (state.settings?.dispatchPaused || state.settings?.emergencyStop || state.settings?.readOnly) return null;
    const capacity = providerCapacityCircuit(state);
    if (capacity.blocked) return null;

    let source = state.agents.find(item => item.id === sourceId);
    let task = state.tasks.find(item => item.id === source?.taskId) || null;
    if (!source || !task) return null;

    const config = swarmTailRecoveryConfig(state);
    let staleTakeoverPath = source.takeoverPath || null;
    const recoveringStale = String(source.status || "") === "stale";
    if (recoveringStale) {
      if (!config.replaceStale) return null;
      const phaseRef = perpetualPhaseWorkerRef(state.autopilot);
      if (state.autopilot?.perpetual && state.autopilot?.enabled && phaseRef?.agentId === source.id) return null;

      const preserved = buildTakeoverForAgent(source.id, { persist: true, safetyControl: true });
      staleTakeoverPath = preserved.takeoverPath || staleTakeoverPath;
      await stopAgent(source.id);

      state = refreshState();
      source = state.agents.find(item => item.id === sourceId);
      task = state.tasks.find(item => item.id === source?.taskId) || null;
      if (!source || !task) return null;

      source.status = "interrupted";
      source.completionEvidence = "stale-recovery-stop";
      source.swarmTailRecoveryCause = "stale-progress-timeout";
      source.swarmTailRecoveryStatus = "stale-stopped";
      source.swarmTailRecoveryTakeoverPath = staleTakeoverPath;
      source.updatedAt = isoNow();
      task.status = "retry-pending";
      task.finishedAt = null;
      task.updatedAt = isoNow();
      task.nextAction = "Supervisor proved the stale worker stopped, preserved takeover state, and is dispatching a one-for-one replacement.";
      addEvent(state, "swarm.stale-worker-stopped", `${source.id} stopped after progress-timeout takeover preservation`, {
        agentId: source.id,
        taskId: source.taskId,
        reason: "stale-progress-timeout",
        evidence: { rootAgentId: rootId, takeoverPath: staleTakeoverPath, attempt }
      });
      saveState(state);
    }

    const sourceWorktree = source.worktree && fs.existsSync(source.worktree) ? source.worktree : null;
    const recoveryTask = [
      `Finish unfinished work left by ${source.swarmTailRecoveryCause === "stale-progress-timeout" ? "a stale/stuck" : "a crashed/interrupted"} agent ${source.id}.`,
      `Original task: ${task.objective || source.task || "unknown"}`,
      `Original branch: ${source.branchName || task.branchName || "unknown"}.`,
      staleTakeoverPath ? `Persisted takeover state: ${staleTakeoverPath}.` : null,
      sourceWorktree ? `Preserved source worktree: ${sourceWorktree}. Inspect and recover any uncommitted changes before editing elsewhere.` : "No preserved source worktree is available; recover from durable branch/commit/task evidence.",
      "Do not restart completed portions from scratch. First inventory durable commits, dirty files, artifacts, tests, PRs, and integration state; then finish only what remains.",
      "Own the work through verification, integration to current canonical main when repository policy permits, remote-main confirmation, and safe cleanup."
    ].filter(Boolean).join("\n");

    const replacement = await deployOne({
      role: "recovery",
      task: recoveryTask,
      baseBranch: source.branchName || source.requestedBaseBranch || source.baseBranch || "main",
      model: source.model || "",
      executionMode: "direct",
      boundary: `swarm-tail-recovery:${rootId}`,
      priority: Math.max(90, Number(source.priority || task.priority || 0)),
      machine: recoveryMachineTarget(source.machine || task.machine, state.settings?.machinePolicies),
      dependencies: [],
      targetAgentId: source.id,
      lane: `swarm-tail-${rootId.slice(0, 12)}`,
      repositoryWriteAuthorized: Boolean(source.repositoryWriteAuthorized ?? task.repositoryWriteAuthorized),
      acceptanceCriteria: [
        "Preserve all useful durable work from the failed or stale agent.",
        "Finish the remaining scope instead of merely auditing it.",
        "Do not duplicate or overwrite newer canonical work.",
        "Integrate and clean up only after targeted verification succeeds."
      ],
      verification: task.verification || [],
      additionalConstraints: [
        `This is supervised recovery attempt ${attempt} for recovery root ${rootId}.`,
        source.swarmTailRecoveryCause === "stale-progress-timeout"
          ? "The previous worker was proven stopped after a progress timeout before this replacement was launched."
          : "This replacement owns only the unfinished lane left by the terminated worker.",
        "Use the default direct/non-Work execution path. Never hand off to Work unless the user explicitly requested Work for this task."
      ],
      recoveryContext: {
        attempt,
        retryOfAgentId: source.id,
        retryOfTaskId: source.taskId || null,
        rootAgentId: rootId
      },
      swarmContext: {
        workflowId: state.settings?.swarmTailRecovery?.armedWorkflowId || task.workflowId || "usual-swarm",
        waveId: state.settings?.swarmTailRecovery?.waveId || task.swarmWaveId || `recovery:${rootId}`,
        mission: state.settings?.swarmTailRecovery?.armedMission || task.objective || source.task || recoveryTask,
        stepIndex: Math.max(0, Number(attempt || 1) - 1),
        totalSteps: Math.max(1, Number(state.settings?.swarmTailRecovery?.maxAttemptsPerRoot || attempt || 1)),
        source: source.swarmTailRecoveryCause === "stale-progress-timeout"
          ? "swarm-stale-supervisor-recovery"
          : "swarm-tail-recovery"
      }
    });

    const linked = loadState();
    const original = linked.agents.find(item => item.id === source.id);
    const created = linked.agents.find(item => item.id === replacement.id);
    const originalTask = linked.tasks.find(item => item.id === source.taskId);
    const createdTask = linked.tasks.find(item => item.id === replacement.taskId);
    if (original) {
      original.swarmTailRecoveryStatus = "dispatched";
      original.swarmTailRecoveryReplacementAgentId = replacement.id;
      original.swarmTailRecoveryReplacementTaskId = replacement.taskId;
      original.swarmTailRecoveryRootAgentId = rootId;
      original.swarmTailRecoveryDispatchFailures = 0;
      original.swarmTailRecoveryNextAt = null;
      original.swarmTailRecoveryLastError = null;
    }
    if (created) {
      created.swarmTailRecovery = true;
      created.swarmTailRecoveryRootAgentId = rootId;
      created.swarmTailRecoveryAttempt = attempt;
    }
    if (originalTask) originalTask.nextAction = `Supervised recovery agent ${replacement.id} is finishing the remaining work.`;
    if (createdTask) {
      createdTask.swarmTailRecovery = true;
      createdTask.swarmTailRecoveryRootAgentId = rootId;
      createdTask.swarmTailRecoveryAttempt = attempt;
    }
    addEvent(linked, "swarm.tail-recovery-dispatched", `${source.id} unfinished work assigned to ${replacement.id}`, {
      agentId: replacement.id,
      taskId: replacement.taskId,
      reason: source.swarmTailRecoveryCause === "stale-progress-timeout" ? "stale-worker-replaced" : "unfinished-worker-replaced",
      evidence: { sourceAgentId: source.id, sourceTaskId: source.taskId, rootAgentId: rootId, attempt, takeoverPath: staleTakeoverPath }
    });
    addNotification(linked, {
      severity: "info",
      title: source.swarmTailRecoveryCause === "stale-progress-timeout" ? "Stale worker replaced" : "Cleanup wave dispatched",
      message: `${replacement.roleLabel || replacement.id} is finishing unfinished work from ${source.roleLabel || source.id}.`,
      action: { type: "inspect-agent", agentId: replacement.id },
      dedupeKey: `swarm-tail:${rootId}:${attempt}`
    });
    saveState(linked);
    return created || replacement;
  } catch (error) {
    const failed = loadState();
    const source = failed.agents.find(item => item.id === sourceId);
    if (source) {
      const config = swarmTailRecoveryConfig(failed);
      const failures = Math.max(0, Math.floor(Number(source.swarmTailRecoveryDispatchFailures) || 0)) + 1;
      const jitterUnit = stableJitterUnit(`${rootId}:${attempt}:${failures}:${error?.message || error}`);
      const retryDelayMs = recoveryBackoffWithJitterMs(failures, {
        baseMs: config.retryBaseMs,
        maxMs: config.retryMaxMs,
        jitterUnit
      });
      source.swarmTailRecoveryStatus = "dispatch-failed";
      source.swarmTailRecoveryDispatchFailures = failures;
      source.swarmTailRecoveryNextAt = new Date(Date.now() + retryDelayMs).toISOString();
      source.swarmTailRecoveryLastError = error?.message || String(error);
      addEvent(failed, "swarm.tail-recovery-dispatch-failed", `Recovery dispatch failed for ${sourceId}`, {
        agentId: sourceId,
        taskId: source.taskId,
        reason: source.swarmTailRecoveryLastError,
        evidence: { rootAgentId: rootId, attempt, dispatchFailures: failures, retryAt: source.swarmTailRecoveryNextAt }
      });
      saveState(failed);
    }
    return null;
  } finally {
    swarmTailRecoveryOperations.delete(operationId);
  }
}

async function reconcileSwarmTailRecoveries() {
  if (swarmTailRecoveryTickRunning) return;
  swarmTailRecoveryTickRunning = true;
  try {
    const state = refreshState();
    const config = swarmTailRecoveryConfig(state);
    if (!config.enabled || !config.armedAt || state.settings?.dispatchPaused || state.settings?.emergencyStop || state.settings?.readOnly) return;
    const armedMs = Date.parse(String(config.armedAt));
    if (!Number.isFinite(armedMs)) return;
    const scopedAgents = state.agents.filter(agent => {
      const startedMs = Date.parse(String(agent?.startedAt || ""));
      return Number.isFinite(startedMs) && startedMs >= armedMs;
    });

    const phaseRef = perpetualPhaseWorkerRef(state.autopilot);
    const excludeAgentIds = [];
    if (state.autopilot?.perpetual && state.autopilot?.enabled && phaseRef?.agentId) excludeAgentIds.push(phaseRef.agentId);
    if (!config.replaceStale) {
      excludeAgentIds.push(...scopedAgents.filter(agent => String(agent.status || "") === "stale").map(agent => agent.id));
    }
    const batch = planSwarmTailRecoveryBatch(state, {
      maxWorkers: config.maxWorkers,
      maxAttemptsPerRoot: config.maxAttemptsPerRoot,
      since: config.armedAt,
      excludeAgentIds
    });
    if (batch.length) {
      for (const item of batch) {
        await recoverSwarmTailAgent(item.agent.id, item.rootId, item.attempt);
      }
      return;
    }

    // Keep the recovery pool armed while any lane in this wave is still alive.
    // Dead lanes are replaced immediately above; wave completion is evaluated only
    // after all original/recovery workers have settled.
    if (scopedAgents.some(agent => coreIsActiveStatus(agent.status))) return;

    const rootIds = new Set(scopedAgents
      .filter(agent => ["failed", "interrupted"].includes(String(agent.status || "")))
      .map(agent => String(agent.swarmTailRecoveryRootAgentId || agent.recoveryRootAgentId || agent.id || "").trim())
      .filter(Boolean));
    const unresolvedRoots = [...rootIds].filter(rootId => !scopedAgents.some(agent =>
      String(agent.swarmTailRecoveryRootAgentId || agent.recoveryRootAgentId || agent.id || "").trim() === rootId
      && String(agent.status || "") === "done"
    ));

    const completed = loadState();
    const previous = completed.settings.swarmTailRecovery || {};
    completed.settings.swarmTailRecovery = {
      ...previous,
      armedAt: null,
      armedWorkflowId: null,
      armedMission: null,
      waveId: null,
      lastCompletedAt: isoNow(),
      lastUnresolvedRoots: unresolvedRoots
    };
    addEvent(completed, unresolvedRoots.length ? "swarm.tail-recovery-exhausted" : "swarm.tail-recovery-complete",
      unresolvedRoots.length
        ? `Cleanup wave ended with ${unresolvedRoots.length} unresolved recovery root(s)`
        : "Cleanup wave completed with no unresolved crashed-agent work", {
        reason: unresolvedRoots.length ? "recovery-attempts-exhausted" : "swarm-clean",
        evidence: {
          workflowId: config.armedWorkflowId,
          waveId: config.waveId,
          unresolvedRoots
        }
      });
    if (unresolvedRoots.length) {
      addNotification(completed, {
        severity: "warning",
        title: "Cleanup wave needs attention",
        message: `${unresolvedRoots.length} crashed-agent work item(s) remain after automatic recovery attempts.`,
        action: { type: "inspect-workers", ids: unresolvedRoots },
        dedupeKey: `swarm-tail-unresolved:${config.waveId || config.armedAt}`
      });
    }
    saveState(completed);
  } finally {
    swarmTailRecoveryTickRunning = false;
  }
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
  const existing = stopOperations.get(id);
  if (existing) return existing;
  const operation = stopAgentOnce(id);
  stopOperations.set(id, operation);
  try {
    return await operation;
  } finally {
    if (stopOperations.get(id) === operation) stopOperations.delete(id);
  }
}

async function stopAgentOnce(id) {
  const state = refreshState();
  assertMutationsAllowed(state, { safetyControl: true });
  const agent = state.agents.find(item => item.id === id);
  if (!agent) throw new Error("Agent not found.");
  if (isTerminalStatus(agent.status)) return agent;

  const child = children.get(id);
  const ownsProcess = agent.ownerSessionId === SESSION_ID
    && child
    && child.pid === agent.pid
    && child.exitCode === null
    && child.signalCode === null;
  if (!ownsProcess) {
    throw new Error(`Cannot safely stop ${id}: this controller session cannot prove a still-live owned child for PID ${agent.pid || "unknown"}. Preserve state and reconcile or replace instead.`);
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
    if (agent.executionProvider === "heaven-bridge" && agent.remoteJobId) {
      const cancellation = await cancelHeavenBridgeJob(agent.remoteJobId, { reason: "operator-stop" });
      if (!bridgeResultSucceeded(cancellation)) {
        throw new Error(`Heaven Bridge cancellation was not authoritative: ${cancellation.status} / ${cancellation.exit_code}.`);
      }
    }
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
      "refs/remotes/origin/integration",
      "refs/remotes/origin/fix",
      "refs/remotes/origin/recovery",
      "refs/remotes/origin/ops"
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
        task: managed?.task || null,
        branchDecision: state.tasks.find(task => task.branchName === branchName)?.branchDecision || null,
        branchDecisionReason: state.tasks.find(task => task.branchName === branchName)?.branchDecisionReason || null,
        branchCleanup: state.tasks.find(task => task.branchName === branchName)?.branchCleanup || null
      };
    });
  } catch {
    return [];
  }
}

function telemetry(state, queue, federation = federationSnapshot(state.federation)) {
  const managedAgents = state.agents;
  const done = managedAgents.filter(agent => agent.status === "done").length;
  const failed = managedAgents.filter(agent => agent.status === "failed").length;
  const stopped = managedAgents.filter(agent => agent.status === "stopped").length;
  const uncertain = managedAgents.filter(agent => ["interrupted", "orphaned", "stale"].includes(agent.status)).length;
  const finished = managedAgents.filter(agent => isTerminalStatus(agent.status));
  const completedWithOutcome = done + failed;
  const successRate = completedWithOutcome ? Math.round((done / completedWithOutcome) * 1000) / 10 : null;
  const durations = finished
    .filter(agent => agent.startedAt && agent.finishedAt)
    .map(agent => new Date(agent.finishedAt).getTime() - new Date(agent.startedAt).getTime())
    .filter(value => Number.isFinite(value) && value >= 0);
  const averageRuntimeMs = durations.length ? Math.round(durations.reduce((sum, value) => sum + value, 0) / durations.length) : null;

  return {
    running: federation.counts.live,
    working: federation.counts.working,
    toolWait: federation.counts.tool_wait,
    blocked: federation.counts.blocked,
    idle: federation.counts.idle,
    stale: federation.counts.stale,
    disconnected: federation.counts.disconnected,
    done,
    failed,
    stopped,
    uncertain,
    total: federation.counts.total,
    managedTotal: managedAgents.length,
    successRate,
    averageRuntimeMs,
    activeLeases: state.leases.filter(lease => lease.status === "active").length,
    integrationCandidates: queue.filter(item => item.state === "candidate").length,
    blockedIntegration: queue.filter(item => item.state === "preserved-noneligible").length,
    noChangeCompleted: queue.filter(item => item.state === "no-change").length,
    federated: federation.counts,
    coverage: federation.coverage || federationCountCoverage(federation)
  };
}

function readRepositoryContext() {
  const file = path.join(REPO, "_AGENT_CONTEXT", "CURRENT_REVISION.json");
  try {
    const parsed = JSON.parse(fs.readFileSync(file, "utf8"));
    return {
      currentVersion: parsed.currentVersion || parsed.version || null,
      nextMilestone: parsed.nextMilestone || parsed.next_milestone || parsed.nextAction || null,
      agentManagerPriority: parsed.agentManagerPriority && typeof parsed.agentManagerPriority === "object"
        ? parsed.agentManagerPriority
        : null,
      verificationCommit:
        parsed.verificationAppliesToCommit ||
        parsed.verifiedCommit ||
        parsed.lastVerifiedCommit ||
        parsed.lastClosedVerification?.sourceCommit ||
        null,
      updatedAt: parsed.updatedAt || parsed.generatedAt || null
    };
  } catch {
    return { currentVersion: null, nextMilestone: null, agentManagerPriority: null, verificationCommit: null, updatedAt: null };
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

function ingestFederatedObservations(body = {}) {
  const state = refreshState();
  assertMutationsAllowed(state);
  const raw = Array.isArray(body.observations) ? body.observations : [body];
  if (!raw.length) throw new Error("At least one federation observation is required.");

  const accepted = raw.map(item => reconcileObservation(state.federation, {
    ...item,
    provider: item?.provider || body.provider
  }, { now: Date.now() }));

  const federatedRecoveryIds = [];
  for (const agent of accepted) {
    const metadata = agent.source_metadata && typeof agent.source_metadata === "object"
      ? agent.source_metadata
      : {};
    const message = agent.last_action_summary || metadata.final_message || "";
    const expectsRepositoryWork = metadata.execution_assignment === true
      || looksLikeExecutionOpener(message)
      || Boolean(metadata.pr_number || metadata.pull_request_number || metadata.branch || agent.branch);
    const rawState = String(agent.state || "").trim().toLowerCase();
    const streamLost = ["disconnected", "interrupted", "orphaned"].includes(rawState)
      || metadata.stream_lost === true
      || metadata.response_stream_failed === true
      || metadata.transport_disconnected === true;
    const status = rawState === "disconnected" ? "interrupted" : rawState;
    const stored = state.federation.agents.find(item => item.agent_id === agent.agent_id);
    if (!stored || !expectsRepositoryWork || !["done", "failed", "finished", "interrupted", "orphaned", "disconnected"].includes(rawState)) continue;
    if (["retry-dispatched", "retry-blocked", "retry-exhausted"].includes(String(stored.recovery_status || ""))) continue;

    stored.recovery_status = "stream-lost-checking-work";
    stored.recovery_detected_at ||= isoNow();
    const durableEvidenceChecked = metadata.durable_evidence_checked === true
      || (
        Boolean(metadata.base_sha)
        && Boolean(metadata.current_sha)
        && Array.isArray(metadata.changed_files)
      );
    const decision = terminationReconciliationDecision({
      status,
      lastMessage: message,
      baseSha: metadata.base_sha,
      currentSha: metadata.current_sha,
      changedFiles: metadata.changed_files,
      prNumber: metadata.pr_number || metadata.pull_request_number,
      verificationResults: metadata.verification_results,
      source_metadata: metadata
    }, {
      expectsRepositoryWork,
      streamLost,
      durableEvidenceChecked
    });

    stored.recovery_reason = decision.reason;
    stored.recovery_next_at = null;

    if (decision.recoveryStatus === "work-verified-complete") {
      stored.recovery_status = "work-verified-complete";
      stored.recovery_last_error = null;
      addEvent(state, "federation.stream-loss-complete", `${stored.agent_id} has verified durable completion evidence`, {
        agentIds: [stored.agent_id],
        reason: decision.reason
      });
      continue;
    }

    if (decision.recoveryStatus === "work-detected-incomplete") {
      stored.recovery_status = "work-detected-incomplete";
      stored.recovery_last_error = null;
      addEvent(state, "federation.stream-loss-incomplete", `${stored.agent_id} has durable work without completion proof`, {
        agentIds: [stored.agent_id],
        reason: decision.reason
      });
      continue;
    }

    if (decision.recoveryStatus === "no-durable-work-detected-retry") {
      if (String(stored.recovery_status || "").startsWith("retry-")) continue;
      stored.recovery_status = stored.task ? "retry-pending" : "retry-blocked";
      if (!stored.task) stored.recovery_last_error = "Federated execution session terminated without a task body for safe retry.";
      if (stored.recovery_status === "retry-pending") federatedRecoveryIds.push(stored.agent_id);
      continue;
    }

    stored.recovery_status = decision.recoveryStatus || "work-unverified";
  }

  addEvent(state, "federation.observed", `Accepted ${accepted.length} federated agent observation${accepted.length === 1 ? "" : "s"}`, {
    agentIds: [...new Set(accepted.map(item => item.agent_id))]
  });
  saveState(state);
  for (const agentId of federatedRecoveryIds) {
    setImmediate(() => void recoverFederatedNoWorkAgent(agentId));
  }
  return {
    accepted: accepted.length,
    agents: accepted,
    federation: federationSnapshot(state.federation)
  };
}

function heartbeatFederatedProvider(providerId, body = {}) {
  const state = refreshState();
  assertMutationsAllowed(state);
  const provider = recordProviderHeartbeat(state.federation, providerId, {
    status: body.status || "online",
    at: body.at || Date.now(),
    error: body.error || null,
    metadata: body.metadata && typeof body.metadata === "object" ? body.metadata : null
  });
  addEvent(state, "federation.provider-heartbeat", `Provider ${provider.id} heartbeat`, {
    providerId: provider.id,
    status: provider.status
  });
  saveState(state);
  return provider;
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
  const workers = await workerSnapshot(state);
  const federation = await runtimeFederationSnapshot(state);
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
    telemetry: telemetry(state, queue, federation),
    federation,
    federatedAgents: federation.agents,
    providers: federation.providers,
    settings: state.settings,
    autopilot: state.autopilot,
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
    recentFailures: readFailureLog(80),
    roles: rolePresets
  };
}

async function deployReview(targetAgentId, body = {}) {
  const state = refreshState();
  assertAutonomyPermission(state, "request-review", "review dispatch");
  const target = state.agents.find(agent => agent.id === targetAgentId);
  if (!target) throw new Error("Target agent not found.");
  if (target.status === "capacity-blocked") {
    const error = new Error("The target agent is quota/capacity-blocked. Do not spawn a reviewer or takeover worker for a provider-capacity failure; keep the unfinished task visible and use normal Chat unless the user explicitly opts into Codex later.");
    error.code = "REVIEW_TARGET_CAPACITY_BLOCKED";
    error.statusCode = 409;
    throw error;
  }

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
    executionMode: body.executionMode || "",
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
    ],
    swarmContext: body.swarmContext || null
  });
}

async function previewWorkflow(workflowId, body = {}) {
  const state = refreshState();
  assertAutonomyPermission(state, "preview", `workflow preview "${workflowId}"`);
  const repositoryContext = readRepositoryContext();
  const mission = String(body.objective || "").trim() || deriveMission(state, repositoryContext);
  return planWorkflow(workflowId, {
    state,
    mission,
    baseBranch: body.baseBranch || "main",
    machine: body.machine || "auto",
    requireReconciledOwnership: Boolean(body.requireReconciledOwnership),
    now: Date.now()
  });
}

async function waitForWorkflowStartupViability(agentId, {
  timeoutMs = WORKFLOW_STARTUP_GUARD_MS,
  pollMs = 100
} = {}) {
  const deadline = Date.now() + Math.max(0, Number(timeoutMs) || 0);
  while (true) {
    const state = refreshState();
    const agent = state.agents.find(item => item.id === agentId) || null;
    const capacity = providerCapacityCircuit(state);
    if (capacity.blocked) {
      return {
        allowed: false,
        reason: "provider-capacity",
        status: agent?.status || null,
        lastMessage: agent?.lastMessage || null,
        blockedUntil: capacity.blockedUntil || null
      };
    }
    if (!agent) {
      return { allowed: false, reason: "launched-agent-missing", status: null, lastMessage: null };
    }
    const status = String(agent.status || "");
    if (["failed", "capacity-blocked", "stopped", "interrupted", "orphaned", "retry-pending"].includes(status)) {
      return {
        allowed: false,
        reason: agent.failureClass || `startup-terminal:${status}`,
        status,
        lastMessage: agent.lastMessage || agent.error || null
      };
    }
    if (Date.now() >= deadline) {
      return { allowed: true, reason: "startup-guard-passed", status, lastMessage: agent.lastMessage || null };
    }
    await waitMs(Math.min(Math.max(25, Number(pollMs) || 100), Math.max(25, deadline - Date.now())));
  }
}

async function executeWorkflow(workflowId, body = {}, { operatorInitiated = false } = {}) {
  return withDeployLock(async () => {
    const workflowStartedAt = isoNow();
    const requestedSwarmContext = body.swarmContext && typeof body.swarmContext === "object"
      ? body.swarmContext
      : null;
    const workflowWaveId = String(requestedSwarmContext?.waveId || "").trim() || randomUUID();
    const state = refreshState();
    assertMutationsAllowed(state, { dispatch: true });
    assertWorkflowAutonomy(state, workflowId);
    const plan = await previewWorkflow(workflowId, {
      ...body,
      requireReconciledOwnership: workflowId === "usual-swarm"
        ? (!operatorInitiated || Boolean(body.requireReconciledOwnership))
        : Boolean(body.requireReconciledOwnership)
    });
    if (plan.blocked?.length) return { plan, created: [], blocked: plan.blocked };

    const capacityState = refreshState();
    const capacity = deploymentBatchCapacity(capacityState, plan.steps.length, MAX_ACTIVE_AGENTS);
    if (!capacity.allowed) {
      return {
        plan,
        created: [],
        blocked: [{
          error: `Workflow requires ${plan.steps.length} free worker slots but only ${capacity.available} are available. No workers were launched.`
        }]
      };
    }

    const leasePreflight = workflowLeasePreflight(capacityState, plan.steps);
    if (!leasePreflight.allowed) {
      return {
        plan,
        created: [],
        blocked: [{
          error: `${leasePreflight.reason} No workers were launched.`
        }]
      };
    }

    const created = [];
    const blocked = [];
    let stepIndex = 0;
    for (const work of plan.steps) {
      const currentStepIndex = stepIndex;
      stepIndex += 1;
      try {
        const agent = await deployOne({
          role: work.role,
          task: work.task,
          baseBranch: body.baseBranch || plan.baseBranch || "main",
          model: body.model || "",
          executionMode: body.executionMode || "",
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
          ],
          swarmContext: {
            workflowId: String(requestedSwarmContext?.workflowId || workflowId).trim() || workflowId,
            waveId: workflowWaveId,
            mission: requestedSwarmContext?.mission || plan.mission || body.objective || work.task,
            stepIndex: currentStepIndex,
            totalSteps: plan.steps.length,
            source: requestedSwarmContext?.source || "one-click-workflow"
          }
        });
        created.push(agent);
        if (currentStepIndex < plan.steps.length - 1) {
          const startup = await waitForWorkflowStartupViability(agent.id);
          if (!startup.allowed) {
            const error = `Workflow startup guard stopped fan-out after ${agent.id}: ${startup.reason}.`;
            blocked.push({ work, error });
            appendFailureLog({
              category: "workflow-fanout",
              phase: "startup-guard",
              reason: startup.reason,
              agentId: agent.id,
              taskId: agent.taskId,
              workflowId,
              swarmWaveId: workflowWaveId,
              role: agent.role,
              status: startup.status || agent.status,
              executionMode: agent.executionMode,
              executionProvider: agent.executionProvider,
              machine: agent.machine,
              branchName: agent.branchName,
              lastMessage: startup.lastMessage,
              error
            });
            break;
          }
        }
      } catch (error) {
        blocked.push({ work, error: error.message || String(error) });
        appendFailureLog({
          category: "workflow-dispatch",
          phase: "step-launch",
          reason: error?.code || "workflow-step-dispatch-failed",
          workflowId,
          swarmWaveId: workflowWaveId,
          role: work.role,
          status: "failed",
          executionMode: body.executionMode || "direct",
          machine: work.machine || body.machine || "auto",
          error: error.message || String(error)
        });
        break;
      }
    }
    const shouldArmTailRecovery = created.length > 0 && (workflowId === "usual-swarm" || plan.steps.length > 1);
    if (shouldArmTailRecovery) {
      const linked = loadState();
      linked.settings.swarmTailRecovery = {
        ...(linked.settings.swarmTailRecovery || {}),
        enabled: linked.settings.swarmTailRecovery?.enabled !== false,
        maxWorkers: linked.settings.swarmTailRecovery?.maxWorkers || 4,
        maxAttemptsPerRoot: linked.settings.swarmTailRecovery?.maxAttemptsPerRoot || 2,
        armedAt: workflowStartedAt,
        armedWorkflowId: workflowId,
        armedMission: plan.mission || body.objective || null,
        waveId: workflowWaveId
      };
      addEvent(linked, "swarm.tail-recovery-armed", `Cleanup wave armed for ${workflowId}`, {
        reason: "swarm-workflow-started",
        evidence: {
          workflowId,
          waveId: linked.settings.swarmTailRecovery.waveId,
          armedAt: workflowStartedAt,
          createdAgentIds: created.map(agent => agent.id)
        }
      });
      saveState(linked);
    }
    return { plan, created, blocked };
  });
}

async function startUsualSwarm(body = {}) {
  updateControlSettings({
    autonomyLevel: "coordinate",
    dispatchPaused: false,
    readOnly: false,
    draining: false,
    clearEmergencyStop: true,
    reason: "operator-start-swarm"
  });

  return executeWorkflow("usual-swarm", {
    ...body,
    repositoryWriteAuthorized: true
  }, { operatorInitiated: true });
}

async function startPerpetualSwarm(body = {}) {
  updateControlSettings({
    autonomyLevel: "engineering-autopilot",
    dispatchPaused: false,
    readOnly: false,
    draining: false,
    clearEmergencyStop: true,
    reason: "operator-start-perpetual-swarm"
  });

  const objective = String(body.objective || "").trim();
  if (!objective) throw new Error("Perpetual swarm objective is required.");

  const before = refreshState();
  if (before.autopilot?.enabled) {
    return {
      perpetual: true,
      alreadyRunning: true,
      autopilot: before.autopilot
    };
  }

  startAutopilot({
    ...body,
    objective,
    perpetual: true
  });

  // Advance through sync-plan immediately so a one-click launch does useful work
  // without waiting for two background scheduler ticks.
  await autopilotStep();
  await autopilotStep();

  return {
    perpetual: true,
    alreadyRunning: false,
    autopilot: refreshState().autopilot
  };
}

function buildTakeoverForAgent(id, { persist = false, safetyControl = false } = {}) {
  const state = refreshState();
  if (persist && !safetyControl) {
    assertAutonomyPermission(state, "maintain-continuity", "persisted takeover state");
  }
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
  const preserved = buildTakeoverForAgent(id, { persist: true, safetyControl: true });
  const agent = await stopAgent(id);
  return { ...preserved, agent };
}

function createImprovementProposal(body = {}) {
  const request = String(body.request || body.objective || "").trim();
  if (!request) throw new Error("Improvement request is required.");
  const state = loadState();
  assertMutationsAllowed(state);
  assertAutonomyPermission(state, "recommend", "improvement proposal creation");
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

function recordTaskEvidence(taskId, body = {}, capabilityToken = null) {
  const state = loadState();
  assertMutationsAllowed(state);
  const evidenceType = String(body.type || "note").trim().toLowerCase();
  if (evidenceType === "verification") {
    assertAnyAutonomyPermission(state, ["run-tests", "maintain-continuity"], "verification evidence mutation");
  } else {
    assertAutonomyPermission(state, "maintain-continuity", "task evidence mutation");
  }
  const task = state.tasks.find(item => item.id === taskId);
  if (!task) throw new Error("Task not found.");
  if (!taskCapabilityMatches(task.workerCapabilityHash, capabilityToken)) {
    throw authorizationError("Task evidence mutation requires the scoped capability for this exact worker task.");
  }
  const evidence = {
    id: `evidence-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
    recordedAt: isoNow(),
    type: evidenceType,
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

function setReviewVerdict(agentId, body = {}, capabilityToken = null) {
  const verdict = String(body.verdict || "").trim().toLowerCase();
  if (!["approved", "changes-requested", "rejected"].includes(verdict)) {
    throw new Error("Review verdict must be approved, changes-requested, or rejected.");
  }
  const state = loadState();
  assertMutationsAllowed(state);
  assertAnyAutonomyPermission(state, ["request-review", "prepare-integration"], "integration review verdict");
  const agent = state.agents.find(item => item.id === agentId);
  if (!agent) throw new Error("Candidate agent not found.");

  const reviewerTask = state.tasks.find(task => taskCapabilityMatches(task.workerCapabilityHash, capabilityToken)) || null;
  const reviewer = reviewerTask ? state.agents.find(item => item.id === reviewerTask.agentId) || null : null;
  if (!reviewer || reviewer.role !== "reviewer" || reviewer.targetAgentId !== agentId) {
    throw authorizationError("Review verdict mutation requires the scoped capability of the reviewer assigned to this exact candidate.");
  }

  agent.reviewVerdict = verdict;
  agent.reviewVerdictAt = isoNow();
  agent.reviewVerdictBy = reviewer.id;
  agent.reviewEvidence = body.evidence || null;
  addEvent(state, "integration.review-verdict", `${agentId} review verdict: ${verdict}`, {
    agentId,
    reason: body.reason || null,
    evidence: body.evidence || null
  });
  saveState(state);
  return agent;
}

function updateRoutingManifest(body = {}) {
  const state = loadState();
  assertMutationsAllowed(state, { safetyControl: true });
  assertAutonomyPermission(state, "preview", "routing manifest update");

  if (body.clear === true) {
    state.settings.routingManifest = null;
    addEvent(state, "routing.manifest-cleared", "Routing manifest cleared", {
      reason: body.reason || "operator-clear"
    });
    saveState(state);
    return null;
  }

  if (!Array.isArray(body.assignments)) throw new Error("Routing manifest assignments must be an array.");
  const observedAt = body.observedAt ? new Date(body.observedAt) : new Date();
  if (!Number.isFinite(observedAt.getTime())) throw new Error("Routing manifest observedAt must be a valid timestamp.");
  const ttlMinutes = Math.max(1, Math.min(24 * 60, Number(body.ttlMinutes || 30)));
  const expiresAt = body.expiresAt ? new Date(body.expiresAt) : new Date(observedAt.getTime() + ttlMinutes * 60_000);
  if (!Number.isFinite(expiresAt.getTime()) || expiresAt <= observedAt) throw new Error("Routing manifest expiresAt must be after observedAt.");

  const assignments = body.assignments.map((assignment, index) => {
    const role = String(assignment?.role || "support").trim();
    if (!rolePresets[role]) throw new Error(`Routing assignment ${index + 1} uses unknown role "${role}".`);
    return {
      slotId: String(assignment?.slotId || `slot-${index + 1}`).trim(),
      role,
      lane: assignment?.lane ? String(assignment.lane).trim() : null,
      task: String(assignment?.task || assignment?.objective || "").trim(),
      boundary: assignment?.boundary ? String(assignment.boundary).trim() : null,
      machine: assignment?.machine ? String(assignment.machine).trim().toLowerCase() : "auto",
      priority: assignment?.priority === undefined ? null : normalizePriority(assignment.priority),
      status: String(assignment?.status || (assignment?.owner || assignment?.branch ? "claimed" : "open")).trim().toLowerCase(),
      owner: assignment?.owner ? String(assignment.owner).trim() : null,
      branch: assignment?.branch ? String(assignment.branch).trim() : null,
      mode: assignment?.mode ? String(assignment.mode).trim() : null,
      dependencies: Array.isArray(assignment?.dependencies) ? assignment.dependencies.map(value => String(value).trim()).filter(Boolean) : []
    };
  });

  const mode = String(body.mode || "authoritative").trim().toLowerCase();
  if (!["authoritative", "overlay"].includes(mode)) throw new Error("Routing manifest mode must be authoritative or overlay.");

  state.settings.routingManifest = {
    source: String(body.source || "operator").trim(),
    mode,
    observedAt: observedAt.toISOString(),
    expiresAt: expiresAt.toISOString(),
    assignments
  };
  addEvent(state, "routing.manifest-updated", "Routing manifest updated", {
    reason: body.reason || "operator-update",
    evidence: {
      source: state.settings.routingManifest.source,
      mode,
      assignments: assignments.length,
      observedAt: state.settings.routingManifest.observedAt,
      expiresAt: state.settings.routingManifest.expiresAt
    }
  });
  saveState(state);
  return state.settings.routingManifest;
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

function autopilotRoutingCurrent(state, now = Date.now()) {
  const manifest = state.settings?.routingManifest;
  if (!manifest || !Array.isArray(manifest.assignments)) return false;
  const observedAt = Date.parse(manifest.observedAt || "");
  const expiresAt = Date.parse(manifest.expiresAt || "");
  return Number.isFinite(observedAt) && Number.isFinite(expiresAt)
    && observedAt <= now && expiresAt > now;
}

function refreshPerpetualRoutingLease(state, now = Date.now()) {
  if (!state.autopilot?.perpetual || autopilotRoutingCurrent(state, now)) return state;
  const observedAt = new Date(now);
  const expiresAt = new Date(now + 10 * 60_000);
  const updated = loadState();
  updated.settings.routingManifest = {
    source: "perpetual-controller-reconciliation",
    mode: "overlay",
    observedAt: observedAt.toISOString(),
    expiresAt: expiresAt.toISOString(),
    assignments: []
  };
  addEvent(updated, "routing.manifest-autorenewed", "Perpetual cycle renewed reconciled ownership freshness", {
    reason: "perpetual-one-click-continuity",
    evidence: {
      source: updated.settings.routingManifest.source,
      mode: "overlay",
      observedAt: updated.settings.routingManifest.observedAt,
      expiresAt: updated.settings.routingManifest.expiresAt,
      registeredActiveAgents: updated.agents.filter(agent => coreIsActiveStatus(agent.status)).length
    }
  });
  saveState(updated);
  return refreshState();
}

function persistAutopilotPhase(phase, reason, patch = {}) {
  const state = loadState();
  state.autopilot = transitionAutopilot(state.autopilot, phase, { reason, patch });
  addEvent(state, "autopilot.transition", `Autopilot -> ${phase}`, {
    reason,
    evidence: { runId: state.autopilot.runId, iteration: state.autopilot.iteration }
  });
  saveState(state);
  return state.autopilot;
}

function patchAutopilot(patch = {}) {
  const state = loadState();
  state.autopilot = normalizeAutopilotState({ ...state.autopilot, ...patch, updatedAt: isoNow() });
  saveState(state);
  return state.autopilot;
}

function perpetualPhaseWorkerRef(autopilot = {}) {
  const fields = {
    implement: "implementationAgentId",
    verify: "verificationAgentId",
    reverify: "verificationAgentId",
    review: "reviewAgentId",
    repair: "repairAgentId",
    integrate: "integrationAgentId",
    hygiene: "hygieneAgentId",
    expand: "expansionAgentId"
  };
  const field = fields[String(autopilot.phase || "")] || null;
  return field ? { phase: autopilot.phase, field, agentId: autopilot[field] || null } : null;
}

function perpetualSafetyHoldReason(state) {
  if (!state.autopilot?.perpetual || !state.autopilot?.enabled) return null;
  if (state.health?.mode === "degraded") return "state-degraded";
  if (state.settings?.readOnly) return "read-only";
  if (state.settings?.emergencyStop) return "emergency-stop";
  if (state.settings?.dispatchPaused || state.settings?.draining) return "dispatch-paused";
  if (String(state.settings?.autonomyLevel || "") !== "engineering-autopilot") return "engineering-autopilot-permission-required";
  return null;
}

function perpetualRetryWaiting(autopilot, now = Date.now()) {
  const retryAt = Date.parse(String(autopilot?.nextRetryAt || ""));
  return Number.isFinite(retryAt) && retryAt > now
    ? { waiting: true, retryAt: new Date(retryAt).toISOString(), remainingMs: retryAt - now }
    : { waiting: false, retryAt: null, remainingMs: 0 };
}

function schedulePerpetualRetry(reason, {
  error = null,
  retryAt = null,
  patch = {},
  notify = false
} = {}) {
  const state = loadState();
  if (!state.autopilot?.perpetual || !state.autopilot?.enabled) return state.autopilot;
  const now = Date.now();
  const nowIso = new Date(now).toISOString();
  const history = (Array.isArray(state.autopilot.recoveryHistory) ? state.autopilot.recoveryHistory : [])
    .filter(item => Number.isFinite(Date.parse(item)) && now - Date.parse(item) <= PERPETUAL_RECOVERY_WINDOW_MS);
  history.push(nowIso);

  const previousLevel = Math.max(0, Number(state.autopilot.recoveryCooldownLevel || 0));
  const storm = history.length >= PERPETUAL_MAX_RECOVERIES_PER_WINDOW;
  const baselineLevel = history.length <= 1 ? 0 : previousLevel;
  const nextLevel = Math.min(12, storm ? baselineLevel + 1 : baselineLevel);
  const automaticDelay = Math.min(
    PERPETUAL_RETRY_MAX_MS,
    PERPETUAL_RETRY_BASE_MS * Math.pow(2, Math.min(8, nextLevel))
  );
  const requestedAt = Date.parse(String(retryAt || ""));
  const nextAt = Number.isFinite(requestedAt) && requestedAt > now
    ? requestedAt
    : now + automaticDelay;

  state.autopilot = normalizeAutopilotState({
    ...state.autopilot,
    ...patch,
    enabled: true,
    paused: false,
    recoveryHistory: history,
    recoveryCooldownLevel: nextLevel,
    nextRetryAt: new Date(nextAt).toISOString(),
    lastRecoveryAt: nowIso,
    lastRecoveryReason: String(reason || "perpetual-retry"),
    lastError: error ? String(error.message || error) : state.autopilot.lastError,
    updatedAt: nowIso
  });
  addEvent(state, "autopilot.perpetual-retry", "Perpetual cycle scheduled an automatic retry", {
    reason,
    evidence: {
      retryAt: state.autopilot.nextRetryAt,
      cooldownLevel: state.autopilot.recoveryCooldownLevel,
      recentRecoveries: history.length,
      pendingReplacement: state.autopilot.pendingReplacement?.sourceAgentId || null
    }
  });
  if (notify) {
    addNotification(state, {
      severity: "warning",
      title: "Perpetual cycle will retry automatically",
      message: String(error?.message || error || reason || "Recoverable condition"),
      action: { type: "inspect-autopilot" },
      dedupeKey: `perpetual-retry:${state.autopilot.runId}:${reason}`
    });
  }
  saveState(state);
  return state.autopilot;
}

function clearPerpetualRetry(patch = {}) {
  const state = loadState();
  if (!state.autopilot?.perpetual) return state.autopilot;
  const now = Date.now();
  const recent = (Array.isArray(state.autopilot.recoveryHistory) ? state.autopilot.recoveryHistory : [])
    .filter(item => Number.isFinite(Date.parse(item)) && now - Date.parse(item) <= PERPETUAL_RECOVERY_WINDOW_MS);
  state.autopilot = normalizeAutopilotState({
    ...state.autopilot,
    ...patch,
    nextRetryAt: null,
    recoveryHistory: recent,
    recoveryCooldownLevel: recent.length ? state.autopilot.recoveryCooldownLevel : 0,
    lastError: null,
    updatedAt: isoNow()
  });
  saveState(state);
  return state.autopilot;
}

function perpetualRecoverableGatePatch(state, reason) {
  const autopilot = state.autopilot || {};
  if (reason === "worker-capacity-unavailable" || reason === "routing-ownership-stale") return {};
  if (reason === "verification-evidence-missing") return { verificationAgentId: null };
  if (reason === "review-verdict-missing") return { reviewAgentId: null };
  if (reason === "candidate-missing") {
    return {
      phase: "sync-plan",
      implementationAgentId: null,
      candidateAgentId: null,
      verificationAgentId: null,
      reviewAgentId: null,
      repairAgentId: null,
      phaseRetries: 0,
      repairLoops: 0
    };
  }
  if (String(reason).startsWith("repair-budget-exhausted:")) {
    return {
      phase: "repair",
      repairLoops: 0,
      repairAgentId: null
    };
  }
  if (String(reason).startsWith("phase-retry-budget-exhausted:")) {
    const ref = perpetualPhaseWorkerRef(autopilot);
    return {
      phaseRetries: 0,
      ...(ref?.field ? { [ref.field]: null } : {})
    };
  }
  return null;
}

async function dispatchPerpetualReplacement(state, pending) {
  const source = state.agents.find(agent => agent.id === pending.sourceAgentId) || null;
  const task = state.tasks.find(item => item.id === (pending.sourceTaskId || source?.taskId)) || null;
  if (!source || !task) throw new Error("Perpetual replacement source/task disappeared from durable state.");

  const attempt = Math.max(1, Number(pending.attempt || 1));
  const sourceWorktree = source.worktree && fs.existsSync(source.worktree) ? source.worktree : null;
  const role = rolePresets[source.role] ? source.role : "recovery";
  const replacementSwarmContext = autopilotSwarmContext(
    state,
    pending.phase || "repair",
    task.objective || source.task || state.autopilot?.objective
  );
  return withDeployLock(() => deployOne({
    role,
    task: [
      `Perpetual recovery replacement for stale/stuck agent ${source.id} in phase ${pending.phase}.`,
      `Continue the original objective: ${task.objective || source.task || "unknown"}`,
      `Original branch: ${source.branchName || task.branchName || "unknown"}.`,
      pending.takeoverPath ? `Persisted takeover state: ${pending.takeoverPath}.` : null,
      sourceWorktree ? `Preserved source worktree: ${sourceWorktree}. Inspect it for durable or uncommitted evidence before changing overlapping files.` : null,
      "Inventory the preserved branch/worktree/takeover first. Continue only remaining work; do not restart completed work from scratch.",
      "Respect newer canonical-main work and live ownership. Verify the recovered result before declaring completion.",
      "Use direct non-Work execution. Never hand off to ChatGPT Work unless the user explicitly requested Work for this exact task."
    ].filter(Boolean).join("\n"),
    baseBranch: source.branchName || source.requestedBaseBranch || source.baseBranch || state.autopilot.baseBranch || "main",
    model: source.model || "",
    executionMode: "direct",
    boundary: `autopilot:perpetual-recovery:${state.autopilot.runId}:${pending.phase}:${attempt}`,
    priority: Math.max(95, Number(source.priority || task.priority || 0)),
    machine: recoveryMachineTarget(source.machine || task.machine, state.settings?.machinePolicies),
    dependencies: [],
    targetAgentId: source.targetAgentId || null,
    lane: `perpetual-recovery-${String(pending.phase || "phase")}`,
    repositoryWriteAuthorized: Boolean(source.repositoryWriteAuthorized ?? task.repositoryWriteAuthorized ?? true),
    acceptanceCriteria: Array.isArray(task.acceptanceCriteria) ? task.acceptanceCriteria : [],
    verification: Array.isArray(task.verification) ? task.verification : [],
    additionalConstraints: [
      "This is a one-for-one recovery owner for a controller-proven stopped stale worker.",
      "Preserve and reuse durable work; never duplicate an uncertain live owner."
    ],
    recoveryContext: {
      attempt,
      retryOfAgentId: source.id,
      retryOfTaskId: source.taskId || null,
      rootAgentId: source.recoveryRootAgentId || source.id
    },
    swarmContext: {
      ...(replacementSwarmContext || {
        workflowId: task.workflowId || "perpetual-autopilot",
        waveId: task.swarmWaveId || `perpetual:${state.autopilot.runId}:${Math.max(1, Number(state.autopilot.cycleNumber || 0) + 1)}`,
        mission: state.autopilot.objective,
        stepIndex: task.swarmStepIndex || 0,
        totalSteps: task.swarmStepTotal || 1
      }),
      source: "perpetual-stale-replacement"
    }
  }));
}

async function reconcilePerpetualReplacement(state) {
  if (!state.autopilot?.perpetual || !state.autopilot?.enabled) return null;
  const capacity = providerCapacityCircuit(state);
  if (capacity.blocked) {
    const existingRetry = Date.parse(String(state.autopilot.nextRetryAt || ""));
    const blockedUntil = Date.parse(String(capacity.blockedUntil || ""));
    if (
      state.autopilot.lastRecoveryReason !== "provider-capacity"
      || !Number.isFinite(existingRetry)
      || (Number.isFinite(blockedUntil) && existingRetry < blockedUntil)
    ) {
      schedulePerpetualRetry("provider-capacity", { retryAt: capacity.blockedUntil });
    }
    return { kind: "wait", reason: "provider-capacity", retryAt: capacity.blockedUntil };
  }

  const pending = state.autopilot.pendingReplacement;
  if (pending) {
    const waiting = perpetualRetryWaiting(state.autopilot);
    if (waiting.waiting) return { kind: "wait", reason: "replacement-cooldown", retryAt: waiting.retryAt };
    try {
      const replacement = await dispatchPerpetualReplacement(state, pending);
      const refreshed = loadState();
      const refField = pending.agentField;
      refreshed.autopilot = normalizeAutopilotState({
        ...refreshed.autopilot,
        ...(refField ? { [refField]: replacement.id } : {}),
        pendingReplacement: null,
        replacementCount: Number(refreshed.autopilot.replacementCount || 0) + 1,
        nextRetryAt: null,
        lastRecoveryAt: isoNow(),
        lastRecoveryReason: "stale-worker-replaced",
        lastError: null,
        updatedAt: isoNow()
      });
      addEvent(refreshed, "autopilot.stale-replaced", `${pending.sourceAgentId} replaced by ${replacement.id}`, {
        agentId: replacement.id,
        taskId: replacement.taskId,
        reason: pending.reason || "progress-timeout",
        evidence: { sourceAgentId: pending.sourceAgentId, takeoverPath: pending.takeoverPath || null }
      });
      saveState(refreshed);
      return { kind: "replaced", sourceAgentId: pending.sourceAgentId, replacementAgentId: replacement.id };
    } catch (error) {
      schedulePerpetualRetry("replacement-dispatch-failed", {
        error,
        patch: {
          pendingReplacement: {
            ...pending,
            attempt: Math.max(1, Number(pending.attempt || 1)) + 1,
            lastError: String(error.message || error)
          }
        },
        notify: true
      });
      return { kind: "wait", reason: "replacement-dispatch-failed", error: error.message || String(error) };
    }
  }

  const ref = perpetualPhaseWorkerRef(state.autopilot);
  if (!ref?.agentId) return null;
  const agent = state.agents.find(item => item.id === ref.agentId) || null;
  if (!agent || agent.status !== "stale") return null;

  const takeover = buildTakeoverForAgent(agent.id, { persist: true, safetyControl: true });
  try {
    await stopAgent(agent.id);
  } catch (error) {
    schedulePerpetualRetry("stale-stop-unproven", { error, notify: true });
    return { kind: "wait", reason: "stale-stop-unproven", error: error.message || String(error) };
  }

  const stopped = loadState();
  stopped.autopilot = normalizeAutopilotState({
    ...stopped.autopilot,
    pendingReplacement: {
      sourceAgentId: agent.id,
      sourceTaskId: agent.taskId || null,
      agentField: ref.field,
      phase: ref.phase,
      reason: "progress-timeout",
      takeoverPath: takeover.takeoverPath || null,
      detectedAt: isoNow(),
      attempt: 1
    },
    nextRetryAt: null,
    lastRecoveryAt: isoNow(),
    lastRecoveryReason: "perpetual-stale-replacement",
    updatedAt: isoNow()
  });
  addEvent(stopped, "autopilot.stale-replacement-pending", `${agent.id} stopped after durable takeover preservation`, {
    agentId: agent.id,
    taskId: agent.taskId,
    reason: "progress-timeout",
    evidence: { takeoverPath: takeover.takeoverPath || null, phase: ref.phase }
  });
  saveState(stopped);
  return reconcilePerpetualReplacement(refreshState());
}

function gateAutopilot(reason, error = null) {
  const state = loadState();
  state.autopilot = transitionAutopilot(state.autopilot, "safety-gate", {
    reason,
    patch: {
      enabled: false,
      paused: false,
      lastError: error ? String(error.message || error) : null
    }
  });
  addEvent(state, "autopilot.safety-gate", "Autopilot stopped at a governed safety gate", {
    reason,
    evidence: { runId: state.autopilot.runId, error: state.autopilot.lastError }
  });
  addNotification(state, {
    severity: "warning",
    title: "Autopilot safety gate",
    message: `${reason}. Review the evidence before resuming.`,
    action: { type: "inspect-autopilot" },
    dedupeKey: `autopilot-gate:${state.autopilot.runId}:${reason}`
  });
  saveState(state);
  return state.autopilot;
}

function startAutopilot(body = {}) {
  const objective = String(body.objective || "").trim();
  const overallGoal = String(body.overallGoal || "").trim();
  if (!objective) throw new Error("Autopilot objective is required.");
  if (os.hostname().toLowerCase() !== "heaven2") {
    const error = new Error("Engineering autopilot must run from heaven2, the control/credential authority.");
    error.statusCode = 409;
    throw error;
  }

  const state = loadState();
  assertMutationsAllowed(state);
  if (state.autopilot?.enabled) {
    const error = new Error("An autopilot run is already active or paused. Resume or stop it before starting a new big direction.");
    error.statusCode = 409;
    throw error;
  }
  state.settings.autonomyLevel = "engineering-autopilot";
  const now = isoNow();
  state.autopilot = normalizeAutopilotState({
    enabled: true,
    paused: false,
    objective,
    overallGoal,
    phase: "sync-plan",
    iteration: 0,
    repairLoops: 0,
    maxRepairLoops: body.maxRepairLoops === undefined ? 3 : Number(body.maxRepairLoops),
    maxIterations: body.maxIterations === undefined ? 40 : Number(body.maxIterations),
    perpetual: Boolean(body.perpetual),
    cycleNumber: 0,
    maxCycles: body.maxCycles === undefined ? 0 : Number(body.maxCycles),
    phaseRetries: 0,
    maxPhaseRetries: body.maxPhaseRetries === undefined ? 2 : Number(body.maxPhaseRetries),
    integrationAgentId: null,
    hygieneAgentId: null,
    expansionAgentId: null,
    runId: randomUUID(),
    baseBranch: String(body.baseBranch || "main").trim() || "main",
    startedAt: now,
    updatedAt: now,
    lastTransitionAt: now,
    stopReason: null,
    lastError: null,
    lastCanonicalMainSha: null,
    lastTruthAt: null
  });
  addEvent(state, "autopilot.started", state.autopilot.perpetual ? "Perpetual engineering cycle started" : "Engineering autopilot started", {
    reason: "operator-direction",
    evidence: {
      runId: state.autopilot.runId,
      objective,
      overallGoal: overallGoal || null,
      perpetual: state.autopilot.perpetual,
      cycleNumber: state.autopilot.cycleNumber
    }
  });
  saveState(state);
  return state.autopilot;
}

function pauseAutopilot(reason = "operator-pause") {
  const state = loadState();
  assertMutationsAllowed(state, { safetyControl: true });
  state.autopilot = normalizeAutopilotState({ ...state.autopilot, paused: true, updatedAt: isoNow() });
  addEvent(state, "autopilot.paused", "Engineering autopilot paused", { reason });
  saveState(state);
  return state.autopilot;
}

function resumeAutopilot(reason = "operator-resume") {
  const state = loadState();
  assertMutationsAllowed(state, { safetyControl: true });
  if (!state.autopilot?.objective) throw new Error("No autopilot objective exists to resume.");
  state.settings.autonomyLevel = "engineering-autopilot";
  const phase = state.autopilot.phase === "safety-gate" || state.autopilot.phase === "waiting-for-direction"
    ? "sync-plan"
    : state.autopilot.phase;
  state.autopilot = normalizeAutopilotState({
    ...state.autopilot,
    enabled: true,
    paused: false,
    phase,
    stopReason: null,
    lastError: null,
    updatedAt: isoNow()
  });
  addEvent(state, "autopilot.resumed", "Engineering autopilot resumed", { reason });
  saveState(state);
  return state.autopilot;
}

function stopAutopilot(reason = "operator-stop") {
  const state = loadState();
  assertMutationsAllowed(state, { safetyControl: true });
  state.autopilot = transitionAutopilot(state.autopilot, "waiting-for-direction", {
    reason,
    patch: { enabled: false, paused: false }
  });
  addEvent(state, "autopilot.stopped", "Engineering autopilot stopped", { reason });
  saveState(state);
  return state.autopilot;
}

async function reconcileAutopilotTruth() {
  if (os.hostname().toLowerCase() !== "heaven2") {
    throw new Error("Autopilot truth reconciliation is restricted to heaven2.");
  }
  const output = await git(["ls-remote", "origin", "refs/heads/main"]);
  const canonicalMainSha = String(output || "").trim().split(/\s+/)[0] || null;
  if (!canonicalMainSha || !/^[0-9a-f]{40}$/i.test(canonicalMainSha)) {
    throw new Error("Could not establish canonical origin/main SHA.");
  }
  let state = refreshState();
  state = refreshPerpetualRoutingLease(state);
  const routingCurrent = autopilotRoutingCurrent(state);
  const updated = loadState();
  updated.autopilot = normalizeAutopilotState({
    ...updated.autopilot,
    lastCanonicalMainSha: canonicalMainSha,
    lastTruthAt: isoNow()
  });
  saveState(updated);
  return { canonicalMainSha, routingCurrent };
}

function autopilotCandidate(state) {
  return state.agents.find(agent => agent.id === state.autopilot?.candidateAgentId) || null;
}

function autopilotOverallGoalLine(state) {
  const overallGoal = String(state.autopilot?.overallGoal || "").trim();
  return overallGoal ? `Overall goal: ${overallGoal}` : null;
}

function activeAgentManagerPriority() {
  const priority = readRepositoryContext().agentManagerPriority;
  return priority && String(priority.status || "").trim().toLowerCase() === "active"
    ? priority
    : null;
}

function agentManagerPriorityDirective() {
  const priority = activeAgentManagerPriority();
  if (!priority) return null;
  const goal = String(priority.goal || "Make Agent Manager work as intended end-to-end.").trim();
  const acceptance = Array.isArray(priority.completionRequires)
    ? priority.completionRequires.map(value => String(value || "").trim()).filter(Boolean)
    : [];
  return [
    "P0 AGENT MANAGER FUNCTIONALITY LOCK IS ACTIVE.",
    `Goal: ${goal}`,
    "Work only on Agent Manager / Agent Control functionality, reliability, orchestration, observability, recovery, routing, or directly required verification until _AGENT_CONTEXT/CURRENT_REVISION.json marks this priority complete.",
    "Do not spend this cycle on unrelated product features, polish, or speculative expansion.",
    acceptance.length ? `Completion gate: ${acceptance.join("; ")}` : null
  ].filter(Boolean).join(" ");
}

function autopilotSwarmContext(state, phase, mission = "") {
  if (!state.autopilot?.perpetual) return null;
  const phases = ["implement", "verify", "review", "repair", "integrate", "hygiene", "expand"];
  const normalizedPhase = phases.includes(String(phase || "")) ? String(phase) : "implement";
  const cycle = Math.max(1, Number(state.autopilot?.cycleNumber || 0) + 1);
  const runId = String(state.autopilot?.runId || "untracked").trim() || "untracked";
  return {
    workflowId: "perpetual-autopilot",
    waveId: `perpetual:${runId}:${cycle}`,
    mission: String(mission || state.autopilot?.objective || "Continuously improve the project.").trim(),
    stepIndex: phases.indexOf(normalizedPhase),
    totalSteps: phases.length,
    source: `one-click-perpetual:${normalizedPhase}`
  };
}

function autopilotImplementationObjective(state) {
  const overallGoalLine = autopilotOverallGoalLine(state);
  const managerPriority = agentManagerPriorityDirective();
  if (!state.autopilot?.perpetual) {
    return [managerPriority, overallGoalLine, state.autopilot?.objective || ""].filter(Boolean).join("\n");
  }
  const cycle = Number(state.autopilot?.cycleNumber || 0) + 1;
  return [
    `Perpetual engineering cycle #${cycle}.`,
    managerPriority,
    overallGoalLine,
    "Stabilize before expanding: inspect current canonical main and active ownership, then fix the highest-impact reproducible bug/regression or reliability weakness first.",
    managerPriority
      ? "If no actionable Agent Manager bug remains, close the next unmet Agent Manager completion-gate item; do not switch to unrelated repository work."
      : "If no actionable bug remains, implement the highest-value bounded planned improvement instead.",
    "Do the work rather than only audit it; add prevention/regression coverage; keep the repository releasable and leave exact evidence for verification/review.",
    `Standing direction: ${state.autopilot?.objective || "Continuously improve the project."}`
  ].filter(Boolean).join(" ");
}

async function dispatchAutopilotImplementation(state) {
  assertAutonomyPermission(state, "dispatch-support", "autopilot implementation dispatch");
  const objective = autopilotImplementationObjective(state);
  const result = await executeWorkflow("usual-swarm", {
    objective,
    baseBranch: state.autopilot.baseBranch || "main",
    requireReconciledOwnership: true,
    repositoryWriteAuthorized: true,
    swarmContext: autopilotSwarmContext(state, "implement", objective)
  });
  if (result.blocked?.length) {
    throw new Error(`Autopilot swarm dispatch blocked: ${JSON.stringify(result.blocked)}`);
  }

  const refreshed = refreshState();
  const createdMain = result.created.find(agent => agent.role === "main") || null;
  const matchingMain = refreshed.agents.find(agent =>
    agent.role === "main"
    && coreIsActiveStatus(agent.status)
    && agent.task === objective
  ) || null;
  const occupiedMain = refreshed.agents.find(agent =>
    agent.role === "main"
    && coreIsActiveStatus(agent.status)
  ) || null;
  const programmer = createdMain || matchingMain || occupiedMain;
  if (!programmer) {
    throw new Error("Routing ownership did not provide a managed primary programmer. Refusing to duplicate an externally owned main lane.");
  }
  return programmer;
}

async function dispatchAutopilotVerification(state) {
  assertAutonomyPermission(state, "run-tests", "autopilot verification dispatch");
  const candidate = autopilotCandidate(state);
  if (!candidate) throw new Error("Autopilot candidate is missing before verification.");
  return withDeployLock(() => deployOne({
    role: "test",
    task: [
      `Independently verify candidate ${candidate.id} on branch ${candidate.branchName}.`,
      autopilotOverallGoalLine(state),
      "Run the strongest targeted check/test/lint/type/build gates applicable to the change.",
      "Record structured task evidence with type=verification, exact sourceSha, command, and result=pass or fail before exiting.",
      `Use the task id in your assignment and POST that evidence to http://127.0.0.1:${PORT}/api/tasks/<task-id>/evidence with header X-Agent-Control-Task-Token set from AGENT_CONTROL_TASK_TOKEN. Never print that token.`
    ].filter(Boolean).join("\n"),
    baseBranch: candidate.branchName,
    boundary: `autopilot:verify:${state.autopilot.runId}:${state.autopilot.repairLoops}`,
    priority: 92,
    machine: "auto",
    targetAgentId: candidate.id,
    repositoryWriteAuthorized: true,
    verification: ["A pass requires structured verification evidence, not prose output."],
    swarmContext: autopilotSwarmContext(state, "verify", state.autopilot?.objective)
  }));
}

async function dispatchAutopilotReview(state) {
  assertAutonomyPermission(state, "request-review", "autopilot review dispatch");
  const candidate = autopilotCandidate(state);
  if (!candidate) throw new Error("Autopilot candidate is missing before review.");
  const task = [
    `Independently review candidate ${candidate.id} on branch ${candidate.branchName}.`,
    autopilotOverallGoalLine(state),
    "Inspect implementation, verification evidence, safety, regressions, ownership, docs and version consistency.",
    `Before exiting, record an explicit review verdict for candidate ${candidate.id}: approved, changes-requested, or rejected, with structured evidence.`,
    `POST the verdict to http://127.0.0.1:${PORT}/api/integration/${encodeURIComponent(candidate.id)}/review-verdict as JSON with verdict, reason, and evidence, using header X-Agent-Control-Task-Token from AGENT_CONTROL_TASK_TOKEN. Never print that token.`
  ].filter(Boolean).join("\n");
  return withDeployLock(() => deployReview(candidate.id, {
    task,
    boundary: `autopilot:review:${state.autopilot.runId}:${state.autopilot.repairLoops}`,
    priority: 93,
    machine: "auto",
    repositoryWriteAuthorized: true,
    swarmContext: autopilotSwarmContext(state, "review", state.autopilot?.objective)
  }));
}

async function dispatchAutopilotRepair(state) {
  assertAutonomyPermission(state, "dispatch-support", "autopilot repair dispatch");
  const candidate = autopilotCandidate(state);
  if (!candidate) throw new Error("Autopilot candidate is missing before repair.");
  const evidence = candidate.reviewEvidence ? JSON.stringify(candidate.reviewEvidence) : "No structured reviewEvidence payload was attached.";
  return withDeployLock(() => deployOne({
    role: "main",
    task: [
      `Repair candidate ${candidate.id} on branch ${candidate.branchName}.`,
      autopilotOverallGoalLine(state),
      `Review verdict: ${candidate.reviewVerdict || "verification failure"}.`,
      `Structured review evidence: ${evidence}`,
      "Keep the repair bounded, rerun focused checks, update docs/version when required, and do not merge or publish."
    ].filter(Boolean).join("\n"),
    baseBranch: candidate.branchName,
    boundary: `autopilot:repair:${state.autopilot.runId}:${state.autopilot.repairLoops + 1}`,
    priority: 96,
    machine: "auto",
    repositoryWriteAuthorized: true,
    swarmContext: autopilotSwarmContext(state, "repair", state.autopilot?.objective)
  }));
}

async function dispatchAutopilotIntegration(state) {
  assertAutonomyPermission(state, "prepare-integration", "autopilot reviewed integration");
  const candidate = autopilotCandidate(state);
  if (!candidate) throw new Error("Autopilot candidate is missing before integration.");
  if (candidate.reviewVerdict !== "approved") throw new Error("Autopilot refuses integration without an approved review verdict.");
  const candidateSha = String(candidate.currentSha || "").trim();
  return withDeployLock(() => deployOne({
    role: "integration",
    task: [
      `Integrate ONLY approved candidate ${candidate.id} from branch ${candidate.branchName} into canonical origin/main.`,
      autopilotOverallGoalLine(state),
      candidateSha ? `Expected candidate tip: ${candidateSha}.` : "Resolve and record the exact candidate tip before integration.",
      "Fetch/prune immediately, refresh origin/main, inspect the candidate diff and approval evidence, and preserve newer canonical behavior.",
      "Reconcile conflicts deliberately inside this candidate's scope, rerun the strongest affected checks, then merge/fast-forward the verified candidate into main and push main.",
      "Do not squash away the candidate identity: remote main must contain the verified candidate tip as an ancestor so Agent Control can prove integration.",
      "After push, fetch origin/main again and prove the candidate tip is contained. Delete local/remote temporary branches only when that containment proof makes deletion safe.",
      "Close a directly tied PR only after remote-main proof. Do not merge unrelated branches or publish a release unless the repository's standing release rules independently require it.",
      "If anything prevents safe integration, stop with exact branch/SHA/error evidence rather than forcing history."
    ].filter(Boolean).join("\n"),
    baseBranch: "main",
    boundary: `autopilot:integrate:${state.autopilot.runId}:${state.autopilot.cycleNumber}`,
    priority: 99,
    machine: "auto",
    targetAgentId: candidate.id,
    repositoryWriteAuthorized: true,
    verification: [
      "Remote origin/main contains the approved candidate tip as an ancestor.",
      "Affected verification was rerun after reconciliation.",
      "No unrelated unreviewed work was merged."
    ],
    swarmContext: autopilotSwarmContext(state, "integrate", state.autopilot?.objective)
  }));
}

async function dispatchAutopilotHygiene(state) {
  assertAutonomyPermission(state, "dispatch-support", "autopilot repository hygiene");
  return withDeployLock(() => deployOne({
    role: "cleanup",
    task: [
      `Perpetual engineering cycle #${Number(state.autopilot?.cycleNumber || 0) + 1} repository hygiene.`,
      autopilotOverallGoalLine(state),
      "Refresh canonical main, open PRs/issues, observed temporary branches, task/agent state, and continuity files.",
      "Finish or close evidence-backed completed work; delete only branches whose unique work is proven integrated; preserve anything unique, ambiguous, active, or newer than canonical behavior.",
      "Resolve stale PR/branch/issue bookkeeping, update durable continuity and bug-prevention lessons when applicable, and leave main/repository state easy for a fresh agent to understand.",
      "Do not create cleanup toil merely to make counts reach zero, and do not touch another active owner's mutable boundary."
    ].filter(Boolean).join("\n"),
    baseBranch: "main",
    boundary: `autopilot:hygiene:${state.autopilot.runId}:${state.autopilot.cycleNumber}`,
    priority: 88,
    machine: "auto",
    repositoryWriteAuthorized: true,
    verification: [
      "Every deleted branch has integration proof.",
      "Every closed issue/PR has completion or obsolescence evidence.",
      "Continuity remains truthful to current origin/main."
    ],
    swarmContext: autopilotSwarmContext(state, "hygiene", state.autopilot?.objective)
  }));
}

async function dispatchAutopilotExpansion(state) {
  assertAutonomyPermission(state, "dispatch-support", "autopilot next-cycle expansion");
  const managerPriority = agentManagerPriorityDirective();
  return withDeployLock(() => deployOne({
    role: "research",
    task: [
      `Prepare the next bounded work unit after perpetual engineering cycle #${Number(state.autopilot?.cycleNumber || 0) + 1}.`,
      managerPriority,
      autopilotOverallGoalLine(state),
      managerPriority
        ? "Inspect only Agent Manager / Agent Control runtime health, one-click orchestration, federated registry accuracy, machine routing, recovery behavior, provider failure handling, startup persistence, and exact verification evidence."
        : "Inspect current origin/main, open plans/issues, recent failures, verification gaps, user-facing friction, performance/reliability debt, and relevant external/current technical information when it materially improves the decision.",
      managerPriority
        ? "Choose the highest-impact unmet Agent Manager completion-gate item. Unrelated product features are out of scope while the P0 lock is active."
        : "Choose the highest-value unblocked next unit using this priority: correctness/data-loss/security risks; integration/release blockers; user-facing bugs; planned capability; toil/performance/polish.",
      "Update durable project planning/continuity with a concise next action, acceptance criteria, likely ownership boundary, and required verification. Do not implement product code in this expansion phase.",
      `Keep the standing direction in scope: ${state.autopilot?.objective || "Continuously improve the project."}`
    ].filter(Boolean).join("\n"),
    baseBranch: "main",
    boundary: `autopilot:expand:${state.autopilot.runId}:${state.autopilot.cycleNumber}`,
    priority: managerPriority ? 100 : 72,
    machine: "auto",
    lane: managerPriority ? "agent-manager-p0-next-cycle" : "perpetual-next-cycle",
    repositoryWriteAuthorized: true,
    verification: [managerPriority
      ? "Durable next-step context names one bounded Agent Manager P0 action and its exact verification contract."
      : "Durable next-step context names one bounded high-value action and its verification contract."],
    swarmContext: autopilotSwarmContext(state, "expand", managerPriority || state.autopilot?.objective)
  }));
}

async function autopilotIntegrationVerified(state) {
  if (state.autopilot?.phase !== "integrate") return false;
  const candidate = autopilotCandidate(state);
  if (!candidate?.currentSha) return false;
  await git(["fetch", "origin", "--prune"]);
  return branchTipOnMain(candidate.currentSha);
}

async function autopilotStep() {
  if (autopilotTickRunning) return null;
  autopilotTickRunning = true;
  try {
    let state = refreshState();
    if (!state.autopilot?.enabled || state.autopilot?.paused) return state.autopilot || null;

    if (state.autopilot.perpetual) {
      const holdReason = perpetualSafetyHoldReason(state);
      if (holdReason) {
        return {
          autopilot: state.autopilot,
          decision: { kind: "wait", reason: holdReason, perpetual: true }
        };
      }

      const recovery = await reconcilePerpetualReplacement(state);
      if (recovery) return { autopilot: refreshState().autopilot, decision: recovery };
      state = refreshState();

      const retry = perpetualRetryWaiting(state.autopilot);
      if (retry.waiting) {
        return {
          autopilot: state.autopilot,
          decision: { kind: "wait", reason: state.autopilot.lastRecoveryReason || "perpetual-cooldown", retryAt: retry.retryAt }
        };
      }
    }

    const truth = await reconcileAutopilotTruth();
    state = refreshState();
    const capacityAvailable = state.agents.filter(agent => coreIsActiveStatus(agent.status)).length < MAX_ACTIVE_AGENTS;
    const integrationVerified = await autopilotIntegrationVerified(state);
    const decision = decideAutopilotAction(state, {
      routingCurrent: truth.routingCurrent,
      capacityAvailable,
      integrationVerified
    });

    if (decision.kind === "idle" || decision.kind === "wait") return { autopilot: state.autopilot, decision };
    if (decision.kind === "gate") {
      if (state.autopilot?.perpetual) {
        const recoverablePatch = perpetualRecoverableGatePatch(state, decision.reason);
        const orphanHold = /reconciliation-required-orphaned$/.test(String(decision.reason || ""));
        if (recoverablePatch !== null || orphanHold) {
          const autopilot = schedulePerpetualRetry(decision.reason, {
            patch: recoverablePatch || {},
            notify: !["worker-capacity-unavailable", "routing-ownership-stale"].includes(decision.reason)
          });
          return {
            autopilot,
            decision: { ...decision, kind: "wait", perpetualRetry: true, retryAt: autopilot.nextRetryAt }
          };
        }
      }
      return { autopilot: gateAutopilot(decision.reason), decision };
    }
    if (decision.kind === "transition") {
      const patch = state.autopilot?.perpetual
        ? { ...(decision.patch || {}), nextRetryAt: null, lastError: null }
        : (decision.patch || {});
      return { autopilot: persistAutopilotPhase(decision.phase, decision.reason, patch), decision };
    }
    if (decision.kind === "integration-gate") {
      const candidate = autopilotCandidate(state);
      if (candidate) buildTakeoverForAgent(candidate.id, { persist: true });
      return {
        autopilot: persistAutopilotPhase("waiting-for-direction", decision.reason, { enabled: false, paused: false }),
        decision
      };
    }

    let agent;
    if (decision.kind === "dispatch-implementation") agent = await dispatchAutopilotImplementation(state);
    else if (decision.kind === "dispatch-verification") agent = await dispatchAutopilotVerification(state);
    else if (decision.kind === "dispatch-review") agent = await dispatchAutopilotReview(state);
    else if (decision.kind === "dispatch-repair") agent = await dispatchAutopilotRepair(state);
    else if (decision.kind === "dispatch-integration") agent = await dispatchAutopilotIntegration(state);
    else if (decision.kind === "dispatch-hygiene") agent = await dispatchAutopilotHygiene(state);
    else if (decision.kind === "dispatch-expansion") agent = await dispatchAutopilotExpansion(state);
    else {
      if (state.autopilot?.perpetual) {
        const autopilot = schedulePerpetualRetry(`unknown-decision:${decision.kind}`, { notify: true });
        return { autopilot, decision: { ...decision, kind: "wait", perpetualRetry: true } };
      }
      return { autopilot: gateAutopilot(`unknown-decision:${decision.kind}`), decision };
    }

    const patch = {};
    if (decision.kind === "dispatch-implementation") patch.implementationAgentId = agent.id;
    if (decision.kind === "dispatch-verification") patch.verificationAgentId = agent.id;
    if (decision.kind === "dispatch-review") patch.reviewAgentId = agent.id;
    if (decision.kind === "dispatch-repair") patch.repairAgentId = agent.id;
    if (decision.kind === "dispatch-integration") patch.integrationAgentId = agent.id;
    if (decision.kind === "dispatch-hygiene") patch.hygieneAgentId = agent.id;
    if (decision.kind === "dispatch-expansion") patch.expansionAgentId = agent.id;
    if (state.autopilot?.perpetual) {
      patch.nextRetryAt = null;
      patch.lastError = null;
    }
    return { autopilot: patchAutopilot(patch), decision, agentId: agent.id };
  } catch (error) {
    try {
      const state = loadState();
      if (state.autopilot?.enabled && state.autopilot?.perpetual) {
        const autopilot = schedulePerpetualRetry("autopilot-runtime-error", { error, notify: true });
        return {
          autopilot,
          decision: { kind: "wait", reason: "autopilot-runtime-error", perpetualRetry: true, retryAt: autopilot.nextRetryAt },
          error: error.message || String(error)
        };
      }
      return { autopilot: gateAutopilot("autopilot-runtime-error", error), error: error.message || String(error) };
    } catch {
      return { error: error.message || String(error) };
    }
  } finally {
    autopilotTickRunning = false;
  }
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

function allowedHost(req) {
  const host = String(req.headers.host || "").trim().toLowerCase();
  if (!host) return false;
  const allowed = new Set([
    `${String(HOST).trim().toLowerCase()}:${PORT}`,
    `localhost:${PORT}`,
    `127.0.0.1:${PORT}`,
    `[::1]:${PORT}`
  ]);
  return allowed.has(host);
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

    if (!allowedHost(req)) return sendJson(res, 400, { error: "Host not allowed." });
    if (!allowedOrigin(req)) return sendJson(res, 403, { error: "Origin not allowed." });

    if (req.method === "GET" && pathname === "/api/status") {
      const snapshot = await buildSnapshot();
      return sendJson(res, 200, {
        ok: true,
        generatedAt: snapshot.generatedAt,
        controller: snapshot.controller,
        telemetry: snapshot.telemetry,
        federation: {
          counts: snapshot.federation.counts,
          providers: snapshot.federation.providers
        },
        workers: snapshot.workers,
        roles: snapshot.roles
      });
    }

    if (req.method === "GET" && pathname === "/api/snapshot") {
      return sendJson(res, 200, await buildSnapshot());
    }

    if (req.method === "POST" && pathname === "/api/sync") {
      return sendJson(res, 200, await buildSnapshot({
        fetchRemote: true,
        repositoryWriteAuthorized: true
      }));
    }

    if (req.method === "GET" && pathname === "/api/agents") {
      return sendJson(res, 200, refreshState().agents);
    }

    if (req.method === "GET" && pathname === "/api/federation") {
      const state = refreshState();
      return sendJson(res, 200, await runtimeFederationSnapshot(state));
    }

    if (req.method === "GET" && pathname === "/api/providers") {
      const state = refreshState();
      return sendJson(res, 200, (await runtimeFederationSnapshot(state)).providers);
    }

    if (req.method === "POST" && (pathname === "/api/federation/observations" || pathname === "/api/federation/heartbeat")) {
      const body = await readJson(req);
      return sendJson(res, 200, ingestFederatedObservations(body));
    }

    const providerHeartbeatMatch = pathname.match(/^\/api\/federation\/providers\/([^/]+)\/heartbeat$/);
    if (req.method === "POST" && providerHeartbeatMatch) {
      const body = await readJson(req);
      return sendJson(res, 200, heartbeatFederatedProvider(providerHeartbeatMatch[1], body));
    }

    if (req.method === "GET" && pathname === "/api/tasks") {
      return sendJson(res, 200, refreshState().tasks);
    }

    if (req.method === "GET" && pathname === "/api/leases") {
      return sendJson(res, 200, refreshState().leases);
    }

    if (req.method === "GET" && pathname === "/api/workers") {
      return sendJson(res, 200, await workerSnapshot());
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

    if (req.method === "GET" && pathname === "/api/failures") {
      return sendJson(res, 200, {
        failures: readFailureLog(url.searchParams.get("limit") || 80)
      });
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

    if (req.method === "POST" && pathname === "/api/swarm/start") {
      const body = await readJson(req);
      return sendJson(res, 201, body.perpetual === true
        ? await startPerpetualSwarm(body)
        : await startUsualSwarm(body));
    }

    const workflowMatch = pathname.match(/^\/api\/workflows\/([^/]+)\/(preview|execute)$/);
    if (req.method === "POST" && workflowMatch) {
      const body = await readJson(req);
      const workflowId = workflowMatch[1];
      if (workflowMatch[2] === "preview") {
        return sendJson(res, 200, await previewWorkflow(workflowId, body));
      }
      return sendJson(res, 201, await executeWorkflow(workflowId, {
        ...body,
        repositoryWriteAuthorized: true
      }));
    }

    if (req.method === "GET" && pathname === "/api/autopilot") {
      return sendJson(res, 200, refreshState().autopilot);
    }

    if (req.method === "POST" && pathname === "/api/autopilot/start") {
      const body = await readJson(req);
      return sendJson(res, 200, startAutopilot(body));
    }

    if (req.method === "POST" && pathname === "/api/autopilot/pause") {
      const body = await readJson(req);
      return sendJson(res, 200, pauseAutopilot(body.reason || "operator-pause"));
    }

    if (req.method === "POST" && pathname === "/api/autopilot/resume") {
      const body = await readJson(req);
      return sendJson(res, 200, resumeAutopilot(body.reason || "operator-resume"));
    }

    if (req.method === "POST" && pathname === "/api/autopilot/stop") {
      const body = await readJson(req);
      return sendJson(res, 200, stopAutopilot(body.reason || "operator-stop"));
    }

    if (req.method === "POST" && pathname === "/api/autopilot/step") {
      return sendJson(res, 200, await autopilotStep());
    }

    if (req.method === "GET" && pathname === "/api/work-handoff-signatures") {
      return sendJson(res, 200, loadWorkHandoffSignatures());
    }

    if (req.method === "GET" && pathname === "/api/control/routing-manifest") {
      return sendJson(res, 200, refreshState().settings.routingManifest || null);
    }

    if (req.method === "POST" && pathname === "/api/control/routing-manifest") {
      const body = await readJson(req);
      return sendJson(res, 200, updateRoutingManifest(body));
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
        assertAutonomyPermission(capacityState, "dispatch-support", "direct deployment");
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
            executionMode: body.executionMode || "",
            boundary: effectiveBoundary,
            priority: body.priority,
            machine: body.machine || "auto",
            dependencies: Array.isArray(body.dependencies) ? body.dependencies : [],
            targetAgentId: body.targetAgentId || null,
            lane: body.lane || null,
            repositoryWriteAuthorized: true,
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
      const agent = await withDeployLock(() => deployReview(reviewMatch[1], {
        ...body,
        repositoryWriteAuthorized: true
      }));
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
      return sendJson(res, 201, recordTaskEvidence(evidenceMatch[1], body, requestTaskCapability(req)));
    }

    const reviewVerdictMatch = pathname.match(/^\/api\/integration\/([^/]+)\/review-verdict$/);
    if (req.method === "POST" && reviewVerdictMatch) {
      const body = await readJson(req);
      return sendJson(res, 200, setReviewVerdict(reviewVerdictMatch[1], body, requestTaskCapability(req)));
    }

    const improvementStartMatch = pathname.match(/^\/api\/improvements\/([^/]+)\/start$/);
    if (req.method === "POST" && improvementStartMatch) {
      const state = refreshState();
      const proposal = state.improvements.find(item => item.id === improvementStartMatch[1]);
      if (!proposal) return sendJson(res, 404, { error: "Improvement proposal not found." });
      const body = await readJson(req);
      const result = await executeWorkflow("self-improve", {
        ...body,
        objective: proposal.request,
        repositoryWriteAuthorized: true
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
    const statusCode = Number(error?.statusCode);
    return sendJson(res, Number.isInteger(statusCode) && statusCode >= 400 && statusCode <= 599 ? statusCode : 500, {
      error: error.message || String(error)
    });
  }
});

const RUNTIME_HOSTNAME = os.hostname().trim().toLowerCase();
if (!ALLOW_NON_CONTROLLER_HOST && RUNTIME_HOSTNAME !== CONTROLLER_HOST) {
  throw new Error(`Agent Control must run on ${CONTROLLER_HOST}; observed ${RUNTIME_HOSTNAME || "unknown"}. Set AGENT_CONTROL_ALLOW_NON_CONTROLLER_HOST=1 only for isolated tests or an explicit recovery override.`);
}

const LOOPBACK_HOSTS = new Set(["127.0.0.1", "localhost", "::1"]);
if (!LOOPBACK_HOSTS.has(String(HOST).trim().toLowerCase())) {
  throw new Error(`Refusing unauthenticated non-loopback bind host "${HOST}". Configure an authenticated remote-access boundary before exposing Agent Control beyond localhost.`);
}

function writeControllerProcessIdentity() {
  fs.writeFileSync(CONTROLLER_PID_FILE, JSON.stringify({
    pid: process.pid,
    sessionId: SESSION_ID,
    startedAt: isoNow(),
    serverPath: fileURLToPath(import.meta.url),
    host: os.hostname(),
    port: PORT
  }, null, 2), "utf8");
}

function clearControllerProcessIdentity() {
  try {
    if (!fs.existsSync(CONTROLLER_PID_FILE)) return;
    const current = JSON.parse(fs.readFileSync(CONTROLLER_PID_FILE, "utf8"));
    if (Number(current?.pid) === process.pid) fs.rmSync(CONTROLLER_PID_FILE, { force: true });
  } catch {}
}

process.once("exit", clearControllerProcessIdentity);

server.listen(PORT, HOST, () => {
  writeControllerProcessIdentity();
  console.log(`Heaven Agent Control Plane listening on http://${HOST}:${PORT}`);
  console.log(`Repo: ${REPO}`);
  console.log(`Worktrees: ${WORKTREE_ROOT}`);
  console.log(`Capacity: ${MAX_ACTIVE_AGENTS} active agents`);
});

const autopilotTimer = setInterval(() => {
  void autopilotStep();
}, AUTOPILOT_TICK_MS);
autopilotTimer.unref?.();

const noWorkRecoveryTimer = setInterval(() => {
  void reconcileNoWorkRecoveries().then(() => reconcileSwarmTailRecoveries());
}, NO_WORK_RECOVERY_TICK_MS);
noWorkRecoveryTimer.unref?.();
void reconcileNoWorkRecoveries().then(() => reconcileSwarmTailRecoveries());

const goToWorkRecoveryTimer = setInterval(() => {
  void reconcileGoToWorkRecoveries();
}, GO_TO_WORK_RECOVERY_TICK_MS);
goToWorkRecoveryTimer.unref?.();
void reconcileGoToWorkRecoveries();

const branchCleanupTimer = setInterval(() => {
  void reconcileIntegratedBranchCleanup();
}, 60_000);
branchCleanupTimer.unref?.();
void reconcileIntegratedBranchCleanup();
