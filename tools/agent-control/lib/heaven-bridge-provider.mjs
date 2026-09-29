import fs from "node:fs";
import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);

export const HEAVEN_BRIDGE_PROTOCOL = "chatgpt-heaven-bridge-v2";
export const HEAVEN_BRIDGE_BRANCH = "heaven-bridge";
export const HEAVEN_BRIDGE_HOST = "heaven";
export const DEFAULT_HEARTBEAT_MAX_AGE_MS = 10 * 60_000;
export const DEFAULT_RESULT_TIMEOUT_MS = 2 * 60 * 60_000;
const DEFAULT_RELAY_REPOSITORY = "fengie/mhw-mods";

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function clean(value) {
  return String(value ?? "").trim();
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

async function git(relayDir, args, { maxBuffer = 4 * 1024 * 1024 } = {}) {
  const { stdout, stderr } = await execFileAsync("git", ["-C", relayDir, ...args], {
    windowsHide: true,
    maxBuffer
  });
  return `${stdout || ""}${stderr || ""}`.trim();
}

async function withRelayLock(relayDir, fn, { timeoutMs = 30_000, pollMs = 100 } = {}) {
  const gitDir = path.join(relayDir, ".git");
  if (!fs.existsSync(gitDir)) throw new Error("Heaven relay checkout is not a Git working tree.");
  const lockPath = path.join(gitDir, "agent-control-heaven-bridge.lock");
  const deadline = Date.now() + timeoutMs;
  let fd = null;
  while (Date.now() < deadline) {
    try {
      fd = fs.openSync(lockPath, "wx");
      fs.writeFileSync(fd, `${process.pid}\n`, "utf8");
      break;
    } catch (error) {
      if (error?.code !== "EEXIST") throw error;
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

function heartbeatPath(relayDir) {
  return path.join(relayDir, "heaven-bridge", "status", "heartbeat.json");
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
  maxAgeMs = DEFAULT_HEARTBEAT_MAX_AGE_MS
} = {}) {
  if (!heartbeat || typeof heartbeat !== "object") {
    return { healthy: false, reason: "heartbeat-malformed", heartbeat: null };
  }
  if (heartbeat.host !== HEAVEN_BRIDGE_HOST) {
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
  pull = true
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
  const dirty = await git(relayDir, ["status", "--porcelain"]);
  if (dirty) {
    throw new Error("Dedicated Heaven relay checkout is dirty; refusing to mix unrelated changes into relay traffic.");
  }
  if (pull) {
    await git(relayDir, ["fetch", "origin", HEAVEN_BRIDGE_BRANCH]);
    await git(relayDir, ["merge", "--ff-only", `origin/${HEAVEN_BRIDGE_BRANCH}`]);
  }
}

export async function inspectHeavenBridge({
  relayDir = process.env.AGENT_CONTROL_HEAVEN_RELAY_DIR,
  expectedRepository = process.env.AGENT_CONTROL_HEAVEN_RELAY_REPOSITORY || DEFAULT_RELAY_REPOSITORY,
  maxAgeMs = Number(process.env.AGENT_CONTROL_HEAVEN_HEARTBEAT_MAX_MS || DEFAULT_HEARTBEAT_MAX_AGE_MS),
  now = Date.now(),
  sync = true
} = {}) {
  if (!relayDir) {
    return { configured: false, healthy: false, reason: "relay-not-configured", machine: HEAVEN_BRIDGE_HOST };
  }
  try {
    return await withRelayLock(relayDir, async () => {
      await syncUnlocked(relayDir, { expectedRepository, pull: sync });
      const file = heartbeatPath(relayDir);
      if (!fs.existsSync(file)) {
        return { configured: true, healthy: false, reason: "heartbeat-missing", machine: HEAVEN_BRIDGE_HOST };
      }
      const assessment = heartbeatAssessment(readJson(file), { now, maxAgeMs });
      return {
        configured: true,
        healthy: assessment.healthy,
        reason: assessment.reason,
        machine: HEAVEN_BRIDGE_HOST,
        protocol: HEAVEN_BRIDGE_PROTOCOL,
        heartbeat: assessment.heartbeat
      };
    });
  } catch (error) {
    return {
      configured: true,
      healthy: false,
      reason: error?.message || String(error),
      machine: HEAVEN_BRIDGE_HOST,
      protocol: HEAVEN_BRIDGE_PROTOCOL
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
  priority = "highest",
  ttlSeconds = 21_600,
  createdAt = new Date().toISOString()
} = {}) {
  const jobId = sanitizeId(id);
  if (!clean(action)) throw new Error("bridge action is required");
  return {
    id: jobId,
    source: HEAVEN_BRIDGE_PROTOCOL,
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
  const terminal = new Set(["done", "failed", "timeout", "cancelled"]);
  if (!terminal.has(String(result.status || "").toLowerCase())) {
    throw new Error(`Bridge result has non-terminal or unsupported status "${result.status}".`);
  }
  return result;
}

export function bridgeResultSucceeded(result) {
  return result?.status === "done" && Number(result?.exit_code) === 0;
}

export async function submitHeavenBridgeJob(job, {
  relayDir = process.env.AGENT_CONTROL_HEAVEN_RELAY_DIR,
  expectedRepository = process.env.AGENT_CONTROL_HEAVEN_RELAY_REPOSITORY || DEFAULT_RELAY_REPOSITORY
} = {}) {
  if (!relayDir) throw new Error("AGENT_CONTROL_HEAVEN_RELAY_DIR is required for heaven execution.");
  return withRelayLock(relayDir, async () => {
    await syncUnlocked(relayDir, { expectedRepository, pull: true });
    const healthFile = heartbeatPath(relayDir);
    if (!fs.existsSync(healthFile)) throw new Error("Heaven relay heartbeat is missing.");
    const health = heartbeatAssessment(readJson(healthFile), {
      maxAgeMs: Number(process.env.AGENT_CONTROL_HEAVEN_HEARTBEAT_MAX_MS || DEFAULT_HEARTBEAT_MAX_AGE_MS)
    });
    if (!health.healthy) throw new Error(`Heaven relay is not healthy: ${health.reason}.`);

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
  relayDir = process.env.AGENT_CONTROL_HEAVEN_RELAY_DIR,
  timeoutMs = DEFAULT_RESULT_TIMEOUT_MS,
  pollMs = 1_500,
  expectedRepository = process.env.AGENT_CONTROL_HEAVEN_RELAY_REPOSITORY || DEFAULT_RELAY_REPOSITORY
} = {}) {
  if (!relayDir) throw new Error("AGENT_CONTROL_HEAVEN_RELAY_DIR is required for heaven execution.");
  const deadline = Date.now() + Math.max(1_000, Number(timeoutMs) || DEFAULT_RESULT_TIMEOUT_MS);
  while (Date.now() < deadline) {
    const result = await withRelayLock(relayDir, async () => {
      await syncUnlocked(relayDir, { expectedRepository, pull: true });
      const file = resultPath(relayDir, id);
      return fs.existsSync(file) ? readJson(file) : null;
    });
    if (result) return validateBridgeResult(result, { id, action });
    await delay(Math.max(250, Number(pollMs) || 1_500));
  }
  throw new Error(`Timed out waiting for authoritative Heaven Bridge result for ${id}.`);
}

export async function runHeavenBridgeAction({
  id,
  action,
  params = {},
  timeoutMs = DEFAULT_RESULT_TIMEOUT_MS,
  priority = "highest"
} = {}) {
  const job = buildBridgeJob({ id, action, params, priority });
  await submitHeavenBridgeJob(job);
  return waitForHeavenBridgeResult({ id: job.id, action: job.action, timeoutMs });
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
