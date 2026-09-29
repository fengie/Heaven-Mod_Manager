import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);

export const HEAVEN_BRIDGE_PROTOCOL = "chatgpt-heaven-bridge-v2";
export const HEAVEN_BRIDGE_BRANCH = "heaven-bridge";
export const HEAVEN_BRIDGE_HOST = "heaven";
export const HEAVEN2_BRIDGE_HOST = "heaven2";
export const DEFAULT_CONTROL_HOST = HEAVEN2_BRIDGE_HOST;
export const DEFAULT_HEARTBEAT_MAX_AGE_MS = 10 * 60_000;
export const DEFAULT_HEARTBEAT_REFRESH_COOLDOWN_MS = 30_000;
export const DEFAULT_RESULT_TIMEOUT_MS = 2 * 60 * 60_000;
const DEFAULT_RELAY_REPOSITORY = "fengie/mhw-mods";
const heartbeatRefreshAttempts = new Map();

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function clean(value) {
  return String(value ?? "").trim();
}

export function resolveHeavenRelayDir({
  configuredPath = process.env.AGENT_CONTROL_HEAVEN_RELAY_DIR,
  homeDir = os.homedir(),
  existsSync = fs.existsSync
} = {}) {
  const explicit = clean(configuredPath);
  if (explicit) return explicit;

  const home = clean(homeDir);
  if (!home) return "";

  const documentedDefault = path.join(home, "HeavenBridgeRepo");
  return existsSync(documentedDefault) ? documentedDefault : "";
}

function sanitizeId(value, max = 96) {
  const normalized = clean(value)
    .toLowerCase()
    .replace(/[^a-z0-9._-]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, max);
  if (!normalized) throw new Error("Bridge job id is empty after normalization.");
  return normalized;
}

function psQuote(value) {
  return `'${String(value ?? "").replaceAll("'", "''")}'`;
}

function remoteMatchesExpected(remote, expected = DEFAULT_RELAY_REPOSITORY) {
  const value = clean(remote).replace(/\\/g, "/").replace(/\.git$/i, "").toLowerCase();
  const suffix = clean(expected)
    .replace(/^https?:\/\/github\.com\//i, "")
    .replace(/\.git$/i, "")
    .toLowerCase();
  return value.endsWith(`github.com/${suffix}`) || value.endsWith(`github.com:${suffix}`);
}

async function git(relayDir, args, { maxBuffer = 4 * 1024 * 1024, timeoutMs = 60_000 } = {}) {
  const { stdout, stderr } = await execFileAsync("git", ["-C", relayDir, ...args], {
    windowsHide: true,
    maxBuffer,
    timeout: Math.max(1_000, Number(timeoutMs) || 60_000)
  });
  return `${stdout || ""}${stderr || ""}`.trim();
}

function localPidAlive(pid) {
  const value = Number(pid);
  if (!Number.isInteger(value) || value <= 0) return false;
  try {
    process.kill(value, 0);
    return true;
  } catch (error) {
    return error?.code === "EPERM";
  }
}

export function assessRelayLock(lockPath, {
  now = Date.now(),
  staleAfterMs = 120_000
} = {}) {
  if (!fs.existsSync(lockPath)) return { exists: false, stale: false, ownerPid: null, ageMs: 0 };
  let stat;
  let raw = "";
  try {
    stat = fs.statSync(lockPath);
    raw = fs.readFileSync(lockPath, "utf8").trim();
  } catch {
    return { exists: true, stale: false, ownerPid: null, ageMs: 0 };
  }
  const ownerPid = Number.parseInt(raw.split(/\s+/)[0], 10);
  const ageMs = Math.max(0, Number(now) - stat.mtimeMs);
  const ownerAlive = localPidAlive(ownerPid);
  return {
    exists: true,
    stale: ageMs >= Math.max(1_000, Number(staleAfterMs) || 120_000) && !ownerAlive,
    ownerPid: Number.isInteger(ownerPid) ? ownerPid : null,
    ownerAlive,
    ageMs
  };
}

async function withRelayLock(relayDir, fn, {
  timeoutMs = 30_000,
  pollMs = 100,
  staleAfterMs = Number(process.env.AGENT_CONTROL_HEAVEN_LOCK_STALE_MS || 120_000)
} = {}) {
  const gitDir = path.join(relayDir, ".git");
  if (!fs.existsSync(gitDir)) throw new Error("Heaven relay checkout is not a Git working tree.");
  const lockPath = path.join(gitDir, "agent-control-heaven-bridge.lock");
  const deadline = Date.now() + timeoutMs;
  let fd = null;
  while (Date.now() < deadline) {
    try {
      fd = fs.openSync(lockPath, "wx");
      fs.writeFileSync(fd, `${process.pid} ${new Date().toISOString()}\n`, "utf8");
      break;
    } catch (error) {
      if (error?.code !== "EEXIST") throw error;
      const assessment = assessRelayLock(lockPath, { staleAfterMs });
      if (assessment.stale) {
        try {
          fs.unlinkSync(lockPath);
          continue;
        } catch (unlinkError) {
          if (unlinkError?.code !== "ENOENT") throw unlinkError;
        }
      }
      await delay(pollMs);
    }
  }
  if (fd === null) throw new Error("Timed out waiting for the Heaven relay checkout lock.");
  try {
    return await fn();
  } finally {
    try { fs.closeSync(fd); } catch {}
    try { fs.unlinkSync(lockPath); } catch {}
  }
}

function normalizeBridgeHost(host = HEAVEN_BRIDGE_HOST) {
  const value = clean(host).toLowerCase();
  if (!/^[a-z0-9][a-z0-9._-]{0,63}$/.test(value)) {
    throw new Error(`Invalid Heaven Bridge target host "${host}".`);
  }
  return value;
}

function heartbeatPath(relayDir, host = HEAVEN_BRIDGE_HOST) {
  return path.join(relayDir, "heaven-bridge", "status", "hosts", normalizeBridgeHost(host), "heartbeat.json");
}

function resolveHeartbeatPath(relayDir, host = HEAVEN_BRIDGE_HOST) {
  const normalized = normalizeBridgeHost(host);
  const scoped = heartbeatPath(relayDir, normalized);
  if (fs.existsSync(scoped)) return scoped;
  if (normalized === HEAVEN_BRIDGE_HOST) {
    const legacy = path.join(relayDir, "heaven-bridge", "status", "heartbeat.json");
    if (fs.existsSync(legacy)) return legacy;
  }
  return scoped;
}

function resultPath(relayDir, jobId) {
  return path.join(relayDir, "heaven-bridge", "results", `${jobId}.json`);
}

function queuePath(relayDir, jobId) {
  return path.join(relayDir, "heaven-bridge", "queue", `${jobId}.json`);
}

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, "utf8"));
}

function heartbeatAssessment(heartbeat, {
  now = Date.now(),
  maxAgeMs = DEFAULT_HEARTBEAT_MAX_AGE_MS,
  expectedHost = HEAVEN_BRIDGE_HOST
} = {}) {
  if (!heartbeat || typeof heartbeat !== "object") {
    return { healthy: false, reason: "heartbeat-malformed", heartbeat: null };
  }
  if (clean(heartbeat.host).toLowerCase() !== normalizeBridgeHost(expectedHost)) {
    return { healthy: false, reason: "heartbeat-host-mismatch", heartbeat: null };
  }
  if (heartbeat.protocol !== HEAVEN_BRIDGE_PROTOCOL) {
    return { healthy: false, reason: "heartbeat-protocol-mismatch", heartbeat: null };
  }
  const updatedAt = Date.parse(heartbeat.updated_at || "");
  if (!Number.isFinite(updatedAt)) {
    return { healthy: false, reason: "heartbeat-time-invalid", heartbeat: null };
  }
  const ageMs = Math.max(0, Number(now) - updatedAt);
  if (ageMs > maxAgeMs) {
    return { healthy: false, reason: "heartbeat-stale", heartbeat: { updatedAt: heartbeat.updated_at, ageMs } };
  }
  return {
    healthy: true,
    reason: null,
    heartbeat: {
      host: heartbeat.host,
      protocol: heartbeat.protocol,
      workerVersion: heartbeat.worker_version ?? null,
      updatedAt: heartbeat.updated_at,
      ageMs,
      running: Array.isArray(heartbeat.running) ? heartbeat.running.length : null
    }
  };
}

async function syncUnlocked(relayDir, {
  expectedRepository = DEFAULT_RELAY_REPOSITORY,
  pull = true,
  requireClean = pull
} = {}) {
  if (!relayDir || !fs.existsSync(relayDir)) {
    throw new Error("AGENT_CONTROL_HEAVEN_RELAY_DIR is not configured to an existing dedicated relay checkout.");
  }
  const branch = await git(relayDir, ["branch", "--show-current"]);
  if (branch !== HEAVEN_BRIDGE_BRANCH) {
    throw new Error(`Dedicated Heaven relay checkout must stay on ${HEAVEN_BRIDGE_BRANCH}; observed ${branch || "detached"}.`);
  }
  const remote = await git(relayDir, ["remote", "get-url", "origin"]);
  if (!remoteMatchesExpected(remote, expectedRepository)) {
    throw new Error("Dedicated Heaven relay checkout origin is not the expected private repository.");
  }
  if (requireClean) {
    const dirty = await git(relayDir, ["status", "--porcelain"]);
    if (dirty) {
      throw new Error("Dedicated Heaven relay checkout is dirty; refusing to mix unrelated changes into relay traffic.");
    }
  }
  if (pull) {
    await git(relayDir, ["fetch", "origin", HEAVEN_BRIDGE_BRANCH]);
    await git(relayDir, ["merge", "--ff-only", `origin/${HEAVEN_BRIDGE_BRANCH}`]);
  }
}

export function bridgeMachineStatus(health) {
  if (health?.healthy) return "online";
  if (health?.configured === false) return "not-configured";
  return "presence-unknown";
}

export function shouldRefreshHeartbeat(assessment, {
  sync = false,
  now = Date.now(),
  lastAttemptAt = 0,
  cooldownMs = DEFAULT_HEARTBEAT_REFRESH_COOLDOWN_MS
} = {}) {
  if (sync || assessment?.healthy) return false;
  if (!["heartbeat-missing", "heartbeat-stale"].includes(String(assessment?.reason || ""))) return false;
  const current = Number(now);
  const previous = Number(lastAttemptAt) || 0;
  const cooldown = Math.max(1_000, Number(cooldownMs) || DEFAULT_HEARTBEAT_REFRESH_COOLDOWN_MS);
  return Number.isFinite(current) && current - previous >= cooldown;
}

function heartbeatRepoPaths(host = HEAVEN_BRIDGE_HOST) {
  const normalized = normalizeBridgeHost(host);
  const paths = [`heaven-bridge/status/hosts/${normalized}/heartbeat.json`];
  if (normalized === HEAVEN_BRIDGE_HOST) paths.push("heaven-bridge/status/heartbeat.json");
  return paths;
}

async function readHeartbeatFromRef(relayDir, host, ref = `origin/${HEAVEN_BRIDGE_BRANCH}`) {
  for (const repoPath of heartbeatRepoPaths(host)) {
    try {
      const raw = await git(relayDir, ["show", `${ref}:${repoPath}`]);
      return JSON.parse(raw);
    } catch {}
  }
  return null;
}

export async function inspectHeavenBridge({
  host = HEAVEN_BRIDGE_HOST,
  relayDir = resolveHeavenRelayDir(),
  expectedRepository = process.env.AGENT_CONTROL_HEAVEN_RELAY_REPOSITORY || DEFAULT_RELAY_REPOSITORY,
  maxAgeMs = Number(process.env.AGENT_CONTROL_HEAVEN_HEARTBEAT_MAX_MS || DEFAULT_HEARTBEAT_MAX_AGE_MS),
  refreshCooldownMs = Number(process.env.AGENT_CONTROL_HEAVEN_HEALTH_REFRESH_COOLDOWN_MS || DEFAULT_HEARTBEAT_REFRESH_COOLDOWN_MS),
  now = Date.now(),
  sync = true
} = {}) {
  const targetHost = normalizeBridgeHost(host);
  if (!relayDir) {
    return { configured: false, healthy: false, reason: "relay-not-configured", machine: targetHost };
  }
  try {
    return await withRelayLock(relayDir, async () => {
      await syncUnlocked(relayDir, { expectedRepository, pull: sync, requireClean: sync });

      const assessLocal = () => {
        const file = resolveHeartbeatPath(relayDir, targetHost);
        if (!fs.existsSync(file)) {
          return { healthy: false, reason: "heartbeat-missing", heartbeat: null };
        }
        return heartbeatAssessment(readJson(file), { now, maxAgeMs, expectedHost: targetHost });
      };

      let assessment = assessLocal();
      let evidence = "local-checkout";
      let refreshAttempted = false;
      let refreshError = null;

      const refreshKey = `${path.resolve(relayDir)}\0${targetHost}`;
      const lastAttemptAt = heartbeatRefreshAttempts.get(refreshKey) || 0;
      if (shouldRefreshHeartbeat(assessment, {
        sync,
        now,
        lastAttemptAt,
        cooldownMs: refreshCooldownMs
      })) {
        refreshAttempted = true;
        heartbeatRefreshAttempts.set(refreshKey, Number(now));
        try {
          await git(relayDir, ["fetch", "origin", HEAVEN_BRIDGE_BRANCH]);
          const remoteHeartbeat = await readHeartbeatFromRef(relayDir, targetHost);
          if (remoteHeartbeat) {
            assessment = heartbeatAssessment(remoteHeartbeat, { now, maxAgeMs, expectedHost: targetHost });
            evidence = "remote-tracking-ref";
          } else {
            assessment = { healthy: false, reason: "heartbeat-missing", heartbeat: null };
            evidence = "remote-tracking-ref";
          }
        } catch (error) {
          refreshError = error?.message || String(error);
        }
      }

      return {
        configured: true,
        healthy: assessment.healthy,
        reason: assessment.reason,
        machine: targetHost,
        protocol: HEAVEN_BRIDGE_PROTOCOL,
        heartbeat: assessment.heartbeat,
        evidence,
        refresh_attempted: refreshAttempted,
        refresh_error: refreshError
      };
    });
  } catch (error) {
    return {
      configured: true,
      healthy: false,
      reason: error?.message || String(error),
      machine: targetHost,
      protocol: HEAVEN_BRIDGE_PROTOCOL,
      evidence: "inspection-error"
    };
  }
}

export function buildLocalCodexArgs({ worktree, lastMessagePath, model = null } = {}) {
  if (!clean(worktree)) throw new Error("worktree is required");
  if (!clean(lastMessagePath)) throw new Error("lastMessagePath is required");
  const args = ["-a", "never", "-s", "workspace-write"];
  if (clean(model)) args.push("-m", clean(model));
  args.push(
    "exec",
    "--json",
    "--skip-git-repo-check",
    "-C", clean(worktree),
    "-o", clean(lastMessagePath),
    "-"
  );
  return args;
}

export function buildRemoteCodexCommand({ workdir, promptPath, model = null } = {}) {
  if (!clean(workdir)) throw new Error("workdir is required");
  if (!clean(promptPath)) throw new Error("promptPath is required");
  const modelArg = clean(model) ? ` -m ${psQuote(clean(model))}` : "";
  return [
    `$prompt = Get-Content -Raw -LiteralPath ${psQuote(promptPath)}`,
    `$prompt | codex -a never -s workspace-write${modelArg} exec --skip-git-repo-check -C ${psQuote(workdir)} -`
  ].join("; ");
}

export function buildBridgeJob({
  id,
  action,
  params = {},
  targetHost = HEAVEN_BRIDGE_HOST,
  priority = "highest",
  ttlSeconds = 21_600,
  createdAt = new Date().toISOString()
} = {}) {
  const jobId = sanitizeId(id);
  if (!clean(action)) throw new Error("bridge action is required");
  return {
    id: jobId,
    source: HEAVEN_BRIDGE_PROTOCOL,
    target_host: normalizeBridgeHost(targetHost),
    action: clean(action),
    params: params && typeof params === "object" ? params : {},
    created_at: createdAt,
    ttl_seconds: Math.max(60, Math.min(86_400, Number(ttlSeconds) || 21_600)),
    priority: clean(priority) || "highest"
  };
}

export function validateBridgeResult(result, { id, action, requireHost = HEAVEN_BRIDGE_HOST } = {}) {
  if (!result || typeof result !== "object") throw new Error("Bridge result is missing or malformed.");
  if (id && result.id !== id) throw new Error("Bridge result job identity mismatch.");
  if (result.source !== HEAVEN_BRIDGE_PROTOCOL) throw new Error("Bridge result protocol/source mismatch.");
  if (action && result.action !== action) throw new Error("Bridge result action mismatch.");
  if (requireHost && result.host !== requireHost) throw new Error("Bridge result host mismatch.");
  const terminal = new Set(["completed", "done", "failed", "error", "timeout", "cancelled"]);
  if (!terminal.has(String(result.status || "").toLowerCase())) {
    throw new Error(`Bridge result has non-terminal or unsupported status "${result.status}".`);
  }
  return result;
}

export function bridgeResultSucceeded(result) {
  const status = String(result?.status || "").toLowerCase();
  return (status === "completed" || status === "done") && Number(result?.exit_code) === 0;
}

export async function submitHeavenBridgeJob(job, {
  relayDir = process.env.AGENT_CONTROL_HEAVEN_RELAY_DIR,
  expectedRepository = process.env.AGENT_CONTROL_HEAVEN_RELAY_REPOSITORY || DEFAULT_RELAY_REPOSITORY
} = {}) {
  if (!relayDir) throw new Error("AGENT_CONTROL_HEAVEN_RELAY_DIR is required for bridge execution.");
  const targetHost = normalizeBridgeHost(job?.target_host || HEAVEN_BRIDGE_HOST);
  return withRelayLock(relayDir, async () => {
    await syncUnlocked(relayDir, { expectedRepository, pull: true });
    const healthFile = resolveHeartbeatPath(relayDir, targetHost);
    if (!fs.existsSync(healthFile)) throw new Error(`Heaven Bridge heartbeat is missing for ${targetHost}.`);
    const health = heartbeatAssessment(readJson(healthFile), {
      maxAgeMs: Number(process.env.AGENT_CONTROL_HEAVEN_HEARTBEAT_MAX_MS || DEFAULT_HEARTBEAT_MAX_AGE_MS),
      expectedHost: targetHost
    });
    if (!health.healthy) throw new Error(`Heaven Bridge target ${targetHost} is not healthy: ${health.reason}.`);

    const file = queuePath(relayDir, job.id);
    if (fs.existsSync(file)) {
      const existing = readJson(file);
      if (JSON.stringify(existing) !== JSON.stringify(job)) {
        throw new Error(`Bridge job id ${job.id} already exists with different content.`);
      }
      return job;
    }
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, `${JSON.stringify(job, null, 2)}\n`, "utf8");
    await git(relayDir, ["add", path.relative(relayDir, file).replace(/\\/g, "/")]);
    await git(relayDir, ["commit", "-m", `agent control relay job ${job.id}`]);

    let lastError = null;
    for (let attempt = 0; attempt < 5; attempt += 1) {
      try {
        await git(relayDir, ["pull", "--rebase", "origin", HEAVEN_BRIDGE_BRANCH]);
        await git(relayDir, ["push", "origin", `HEAD:${HEAVEN_BRIDGE_BRANCH}`]);
        return job;
      } catch (error) {
        lastError = error;
        await delay(250 * (attempt + 1));
      }
    }
    throw lastError || new Error("Unable to publish Heaven relay job.");
  });
}

export async function waitForHeavenBridgeResult({
  id,
  action,
  targetHost = HEAVEN_BRIDGE_HOST,
  relayDir = process.env.AGENT_CONTROL_HEAVEN_RELAY_DIR,
  timeoutMs = DEFAULT_RESULT_TIMEOUT_MS,
  pollMs = 1_500,
  expectedRepository = process.env.AGENT_CONTROL_HEAVEN_RELAY_REPOSITORY || DEFAULT_RELAY_REPOSITORY
} = {}) {
  if (!relayDir) throw new Error("AGENT_CONTROL_HEAVEN_RELAY_DIR is required for bridge execution.");
  const deadline = Date.now() + Math.max(1_000, Number(timeoutMs) || DEFAULT_RESULT_TIMEOUT_MS);
  while (Date.now() < deadline) {
    const result = await withRelayLock(relayDir, async () => {
      await syncUnlocked(relayDir, { expectedRepository, pull: true });
      const file = resultPath(relayDir, id);
      return fs.existsSync(file) ? readJson(file) : null;
    });
    if (result) return validateBridgeResult(result, { id, action, requireHost: normalizeBridgeHost(targetHost) });
    await delay(Math.max(250, Number(pollMs) || 1_500));
  }
  throw new Error(`Timed out waiting for authoritative Heaven Bridge result for ${id}.`);
}

export async function runHeavenBridgeAction({
  id,
  action,
  params = {},
  targetHost = HEAVEN_BRIDGE_HOST,
  timeoutMs = DEFAULT_RESULT_TIMEOUT_MS,
  priority = "highest"
} = {}) {
  const job = buildBridgeJob({ id, action, params, targetHost, priority });
  await submitHeavenBridgeJob(job);
  return waitForHeavenBridgeResult({ id: job.id, action: job.action, targetHost: job.target_host, timeoutMs });
}

export async function runHeaven2BridgeAction(options = {}) {
  return runHeavenBridgeAction({ ...options, targetHost: HEAVEN2_BRIDGE_HOST });
}

export async function cancelHeavenBridgeJob(targetJobId, { reason = "agent-control-stop" } = {}) {
  const target = sanitizeId(targetJobId);
  const cancelId = sanitizeId(`cancel-${target}-${Date.now()}`);
  return runHeavenBridgeAction({
    id: cancelId,
    action: "cancel",
    params: { job_id: target, reason },
    timeoutMs: 60_000
  });
}

export async function readHeavenBinaryFile(remotePath, { jobPrefix, chunkSize = 900_000 } = {}) {
  const chunks = [];
  let offset = 0;
  for (let index = 0; index < 4096; index += 1) {
    const id = sanitizeId(`${jobPrefix}-read-${index}`);
    const result = await runHeavenBridgeAction({
      id,
      action: "fs_read_binary",
      params: { path: remotePath, offset, length: chunkSize },
      timeoutMs: 120_000
    });
    if (!bridgeResultSucceeded(result)) {
      throw new Error(`Binary read failed with status ${result.status} / exit ${result.exit_code}.`);
    }
    const data = result.data || {};
    chunks.push(Buffer.from(String(data.content_b64 || ""), "base64"));
    if (data.eof) return Buffer.concat(chunks);
    if (!Number.isFinite(Number(data.next_offset)) || Number(data.next_offset) <= offset) {
      throw new Error("Heaven binary read did not advance.");
    }
    offset = Number(data.next_offset);
  }
  throw new Error("Heaven binary read exceeded the maximum chunk count.");
}
