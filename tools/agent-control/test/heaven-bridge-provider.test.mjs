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
  bridgeResultSucceeded,
  buildBridgeJob,
  buildLocalCodexArgs,
  buildRemoteCodexCommand,
  validateBridgeResult
} from "../lib/heaven-bridge-provider.mjs";

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
