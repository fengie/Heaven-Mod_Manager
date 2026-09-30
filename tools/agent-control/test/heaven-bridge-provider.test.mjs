import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import assert from "node:assert/strict";

import {
  HEAVEN_BRIDGE_HOST,
  HEAVEN2_BRIDGE_HOST,
  HEAVEN_BRIDGE_PROTOCOL,
  assessRelayLock,
  bridgeMachineStatus,
  bridgeResultSucceeded,
  buildBridgeJob,
  canonicalBridgeJob,
  buildLocalCodexArgs,
  buildRemoteCodexCommand,
  resolveHeavenRelayDir,
  resolveBridgeSigningKey,
  signBridgeJob,
  shouldRefreshHeartbeat,
  validateBridgeResult,
  verifyBridgeDocument
} from "../lib/heaven-bridge-provider.mjs";

test("bridge relay auto-discovers the documented per-user checkout", () => {
  const homeDir = path.join("home", "operator");
  const expected = path.join(homeDir, "HeavenBridgeRepo");

  assert.equal(resolveHeavenRelayDir({
    configuredPath: "",
    homeDir,
    existsSync: candidate => candidate === expected
  }), expected);

  const explicit = path.join("custom", "relay");
  assert.equal(resolveHeavenRelayDir({
    configuredPath: explicit,
    homeDir,
    existsSync: () => false
  }), explicit);

  assert.equal(resolveHeavenRelayDir({
    configuredPath: "",
    homeDir,
    existsSync: () => false
  }), "");
});

test("bridge jobs normalize ids and preserve bounded execution metadata", () => {
  const job = buildBridgeJob({
    id: "Support / Agent #1",
    action: "proc_run",
    params: { command: "echo ok" },
    ttlSeconds: 30,
    createdAt: "2026-09-29T09:00:00.000Z"
  });
  assert.equal(job.id, "support-agent-1");
  assert.equal(job.source, HEAVEN_BRIDGE_PROTOCOL);
  assert.equal(job.action, "proc_run");
  assert.equal(job.ttl_seconds, 60);
  assert.equal(job.params.command, "echo ok");
});

test("bridge HMAC signing matches worker canonicalization rules", () => {
  const job = buildBridgeJob({
    id: "signed-job",
    action: "proc_run",
    params: { z: 2, a: { y: true, x: "value" } },
    targetHost: HEAVEN2_BRIDGE_HOST,
    createdAt: "2026-09-29T09:00:00.000Z"
  });
  const signed = signBridgeJob(job, "0123456789abcdef0123456789abcdef");
  assert.match(signed.auth.signature, /^[0-9a-f]{64}$/);
  assert.equal(canonicalBridgeJob(signed), canonicalBridgeJob(job));

  const changed = signBridgeJob({ ...job, params: { ...job.params, z: 3 } }, "0123456789abcdef0123456789abcdef");
  assert.notEqual(changed.auth.signature, signed.auth.signature);
  assert.throws(() => signBridgeJob(job, ""), /HMAC key is required/i);\n  assert.throws(() => signBridgeJob(job, "too-short"), /at least 32 UTF-8 bytes/i);
});

test("bridge signing key resolves from machine-local file when env is absent", () => {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-hmac-test-"));
  try {
    const authDir = path.join(tmp, "HeavenBridge", "auth");
    fs.mkdirSync(authDir, { recursive: true });
    fs.writeFileSync(path.join(authDir, "hmac.key"), "machine-local-test-key\n", "utf8");
    assert.equal(resolveBridgeSigningKey({ env: {}, homeDir: tmp }), "machine-local-test-key");
    assert.equal(resolveBridgeSigningKey({
      env: { AGENT_CONTROL_HEAVEN_HMAC_KEY: "env-key" },
      homeDir: tmp
    }), "env-key");
  } finally {
    fs.rmSync(tmp, { recursive: true, force: true });
  }
});

test("cross-language HMAC fixture is byte-stable", () => {
  const job = buildBridgeJob({
    id: "canonical-fixture",
    action: "wait_for",
    params: {
      ratio: 1e-7,
      count: 3,
      large: 9007199254740992,
      "ユ": "😀"
    },
    targetHost: HEAVEN2_BRIDGE_HOST,
    createdAt: "2026-09-29T09:00:00.000Z"
  });
  const signed = signBridgeJob(job, "0123456789abcdef0123456789abcdef");
  assert.equal(signed.auth.canonical, "mhw-bridge-canon-v1");
  assert.equal(
    signed.auth.signature,
    "8fddef1c6127fb6d98149e16f9823379408b4a8e563fe60c0524f2231bd59252"
  );
  assert.equal(canonicalBridgeJob(signed), canonicalBridgeJob(job));
});

test("bridge jobs can explicitly target heaven2 while preserving heaven legacy default", () => {
  const legacy = buildBridgeJob({
    id: "legacy-default",
    action: "health",
    createdAt: "2026-09-29T09:00:00.000Z"
  });
  assert.equal(legacy.target_host, HEAVEN_BRIDGE_HOST);

  const control = buildBridgeJob({
    id: "control-host",
    action: "window_list",
    targetHost: HEAVEN2_BRIDGE_HOST,
    createdAt: "2026-09-29T09:00:00.000Z"
  });
  assert.equal(control.target_host, "heaven2");

  const result = {
    id: control.id,
    source: HEAVEN_BRIDGE_PROTOCOL,
    host: HEAVEN2_BRIDGE_HOST,
    action: control.action,
    status: "completed",
    exit_code: 0
  };
  assert.equal(validateBridgeResult(result, {
    id: control.id,
    action: control.action,
    requireHost: HEAVEN2_BRIDGE_HOST
  }), result);
});

test("stale or missing local bridge heartbeat triggers a bounded authoritative refresh", () => {
  const stale = { healthy: false, reason: "heartbeat-stale" };
  const missing = { healthy: false, reason: "heartbeat-missing" };
  const healthy = { healthy: true, reason: null };
  const malformed = { healthy: false, reason: "heartbeat-protocol-mismatch" };

  assert.equal(shouldRefreshHeartbeat(stale, {
    sync: false,
    now: 120_000,
    lastAttemptAt: 0,
    cooldownMs: 30_000
  }), true);
  assert.equal(shouldRefreshHeartbeat(missing, {
    sync: false,
    now: 120_000,
    lastAttemptAt: 100_000,
    cooldownMs: 30_000
  }), false);
  assert.equal(shouldRefreshHeartbeat(healthy, { sync: false, now: 120_000 }), false);
  assert.equal(shouldRefreshHeartbeat(malformed, { sync: false, now: 120_000 }), false);
  assert.equal(shouldRefreshHeartbeat(stale, { sync: true, now: 120_000 }), false);
});

test("transport failure never becomes a false host-offline status", () => {
  assert.equal(bridgeMachineStatus({ configured: true, healthy: true }), "online");
  assert.equal(bridgeMachineStatus({
    configured: true,
    healthy: false,
    reason: "heartbeat-stale"
  }), "presence-unknown");
  assert.equal(bridgeMachineStatus({
    configured: true,
    healthy: false,
    reason: "Dedicated Heaven relay checkout is dirty"
  }), "presence-unknown");
  assert.equal(bridgeMachineStatus({ configured: false, healthy: false }), "not-configured");
});

test("authenticated bridge documents reject tampering", () => {
  const key = "0123456789abcdef0123456789abcdef";
  const document = signBridgeJob({
    id: "signed-result",
    source: HEAVEN_BRIDGE_PROTOCOL,
    host: HEAVEN_BRIDGE_HOST,
    action: "proc_run",
    status: "done",
    exit_code: 0
  }, key);
  assert.equal(verifyBridgeDocument(document, key), document);
  assert.throws(
    () => verifyBridgeDocument({ ...document, exit_code: 7 }, key),
    /signature verification failed/i
  );
  assert.throws(
    () => verifyBridgeDocument({ ...document, auth: undefined }, key),
    /canonical format/i
  );
});

test("bridge results require exact job, action, protocol, host and terminal state", () => {
  const result = {
    id: "job-1",
    source: HEAVEN_BRIDGE_PROTOCOL,
    host: HEAVEN_BRIDGE_HOST,
    action: "proc_run",
    status: "done",
    exit_code: 0
  };
  assert.equal(validateBridgeResult(result, { id: "job-1", action: "proc_run" }), result);
  assert.equal(bridgeResultSucceeded(result), true);
  assert.throws(() => validateBridgeResult({ ...result, host: "heaven2" }, { id: "job-1", action: "proc_run" }), /host mismatch/i);
  assert.throws(() => validateBridgeResult({ ...result, status: "running" }, { id: "job-1", action: "proc_run" }), /non-terminal/i);
});

test("bridge result vocabulary matches real structured and process actions", () => {
  const base = {
    id: "job-real",
    source: HEAVEN_BRIDGE_PROTOCOL,
    host: HEAVEN_BRIDGE_HOST
  };
  for (const fixture of [
    { action: "proc_run", status: "done", exit_code: 0, success: true },
    { action: "fs_write", status: "completed", exit_code: 0, success: true },
    { action: "fs_read_binary", status: "completed", exit_code: 0, success: true },
    { action: "proc_run", status: "failed", exit_code: 1, success: false },
    { action: "proc_run", status: "timeout", exit_code: 124, success: false },
    { action: "proc_run", status: "cancelled", exit_code: 130, success: false },
    { action: "fs_write", status: "error", exit_code: 1, success: false }
  ]) {
    const result = { ...base, ...fixture };
    delete result.success;
    assert.equal(validateBridgeResult(result, { id: base.id, action: fixture.action }), result);
    assert.equal(bridgeResultSucceeded(result), fixture.success);
  }
});

test("relay lock assessment only recovers old locks whose local owner is gone", () => {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-lock-test-"));
  const lock = path.join(tmp, "lock");
  try {
    fs.writeFileSync(lock, "999999999 2026-09-29T00:00:00Z\n", "utf8");
    const old = new Date(Date.now() - 10 * 60_000);
    fs.utimesSync(lock, old, old);
    const stale = assessRelayLock(lock, { now: Date.now(), staleAfterMs: 60_000 });
    assert.equal(stale.exists, true);
    assert.equal(stale.stale, true);

    fs.writeFileSync(lock, `${process.pid} now\n`, "utf8");
    fs.utimesSync(lock, old, old);
    const live = assessRelayLock(lock, { now: Date.now(), staleAfterMs: 60_000 });
    assert.equal(live.ownerAlive, true);
    assert.equal(live.stale, false);
  } finally {
    fs.rmSync(tmp, { recursive: true, force: true });
  }
});

test("remote Codex command quotes paths and keeps workspace-write sandboxing", () => {
  const command = buildRemoteCodexCommand({
    workdir: "C:\\Temp\\Agent Work",
    promptPath: "C:\\Temp\\Agent Work\\prompt.txt",
    model: "gpt-5.6"
  });
  assert.match(command, /Get-Content -Raw -LiteralPath/);
  assert.match(command, /workspace-write/);
  assert.match(command, /--skip-git-repo-check/);
  assert.match(command, /gpt-5\.6/);
  assert.match(command, /Agent Work/);
});

test("local Codex args carry exact worktree and output paths", () => {
  const args = buildLocalCodexArgs({
    worktree: "C:\\agent-worktrees\\support",
    lastMessagePath: "C:\\data\\last.txt",
    model: "gpt-5.6"
  });
  assert.deepEqual(args.slice(0, 4), ["-a", "never", "-s", "workspace-write"]);
  assert.ok(args.includes("-C"));
  assert.ok(args.includes("C:\\agent-worktrees\\support"));
  assert.ok(args.includes("-o"));
  assert.ok(args.includes("C:\\data\\last.txt"));
  assert.ok(args.includes("gpt-5.6"));
});

test("remote runner stages new files and pushes only from the control-side worktree", async () => {
  const fs = await import("node:fs");
  const path = await import("node:path");
  const { fileURLToPath } = await import("node:url");
  const here = path.dirname(fileURLToPath(import.meta.url));
  const source = fs.readFileSync(path.resolve(here, "..", "lib", "heaven-bridge-runner.mjs"), "utf8");
  assert.match(source, /git -C \$\{psQuote\(remoteWorktree\)\} add -A/);
  assert.match(source, /diff --cached --binary/);
  assert.match(source, /AGENT_CONTROL_TASK_TOKEN/);
  assert.match(source, /X-Agent-Control-Task-Token/);
  assert.match(source, /const remoteParent = path\.win32\.dirname\(remoteWorktree\)/);
  assert.match(source, /git\(spec\.localWorktree, \["push", "--set-upstream", "origin", spec\.branchName\]\)/);
  assert.doesNotMatch(source, /git -C \$\{psQuote\(remoteWorktree\)\} push/);
});
