import test from "node:test";
import assert from "node:assert/strict";

import {
  HEAVEN_BRIDGE_HOST,
  HEAVEN_BRIDGE_PROTOCOL,
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
  assert.match(source, /const remoteParent = path\.win32\.dirname\(remoteWorktree\)/);
  assert.match(source, /git\(spec\.localWorktree, \["push", "--set-upstream", "origin", spec\.branchName\]\)/);
  assert.doesNotMatch(source, /git -C \$\{psQuote\(remoteWorktree\)\} push/);
});
