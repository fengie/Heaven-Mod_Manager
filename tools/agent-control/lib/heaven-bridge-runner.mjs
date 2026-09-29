import fs from "node:fs";
import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import {
  bridgeResultSucceeded,
  buildRemoteCodexCommand,
  readHeavenBinaryFile,
  runHeavenBridgeAction
} from "./heaven-bridge-provider.mjs";

const execFileAsync = promisify(execFile);

function fail(message, code = 1) {
  process.stderr.write(`${message}\n`);
  process.exitCode = code;
}

function psQuote(value) {
  return `'${String(value ?? "").replaceAll("'", "''")}'`;
}

function safeId(value, max = 72) {
  return String(value || "agent")
    .toLowerCase()
    .replace(/[^a-z0-9._-]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, max) || "agent";
}

async function git(cwd, args, { allowFailure = false } = {}) {
  try {
    const { stdout, stderr } = await execFileAsync("git", ["-C", cwd, ...args], {
      windowsHide: true,
      maxBuffer: 8 * 1024 * 1024
    });
    return `${stdout || ""}${stderr || ""}`.trim();
  } catch (error) {
    if (allowFailure) return "";
    throw error;
  }
}

function writeLastMessage(spec, text) {
  const value = String(text || "").trim();
  if (!spec.lastMessagePath) return;
  fs.mkdirSync(path.dirname(spec.lastMessagePath), { recursive: true });
  fs.writeFileSync(spec.lastMessagePath, value ? `${value}\n` : "", "utf8");
}

async function main() {
  const specPath = process.argv[2];
  if (!specPath) throw new Error("Runner spec path is required.");
  const spec = JSON.parse(fs.readFileSync(specPath, "utf8"));
  for (const key of ["agentId", "taskId", "baseSha", "branchName", "localWorktree", "promptPath"]) {
    if (!String(spec[key] || "").trim()) throw new Error(`Runner spec is missing ${key}.`);
  }
  if (!spec.repositoryWriteAuthorized) {
    throw new Error("Heaven repository-writing task requires explicit per-task repositoryWriteAuthorized=true.");
  }

  const prefix = safeId(`agent-control-${spec.agentId}`);
  const prepareId = `${prefix}-prepare`;
  const writePromptId = `${prefix}-prompt`;
  const executeId = String(spec.remoteJobId || `${prefix}-execute`);
  const patchId = `${prefix}-patch`;
  const remoteRepoUrl = String(spec.remoteRepoUrl || "https://github.com/fengie/mhw-mods.git");
  const remoteLeaf = safeId(spec.agentId, 48);
  const remotePathExpression = `Join-Path $env:TEMP ${psQuote(`agent-control-${remoteLeaf}`)}`;

  const prepareCommand = [
    "$ErrorActionPreference = 'Stop'",
    `$dest = ${remotePathExpression}`,
    "if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }",
    `git clone --no-checkout ${psQuote(remoteRepoUrl)} $dest | Out-Null`,
    "git -C $dest fetch origin --prune | Out-Null",
    `git -C $dest checkout --detach ${psQuote(spec.baseSha)} | Out-Null`,
    `git -C $dest switch -c ${psQuote(spec.branchName)} | Out-Null`,
    "Write-Output ('REMOTE_WORKDIR=' + $dest)"
  ].join("; ");

  const prepared = await runHeavenBridgeAction({
    id: prepareId,
    action: "proc_run",
    params: {
      shell: "powershell",
      cwd: "C:\\Users\\Xxkan",
      timeout_seconds: Math.max(120, Number(spec.prepareTimeoutSeconds || 600)),
      command: prepareCommand
    },
    timeoutMs: Math.max(180_000, Number(spec.prepareTimeoutMs || 900_000))
  });
  if (!bridgeResultSucceeded(prepared)) {
    writeLastMessage(spec, prepared.stderr || prepared.stdout || `Remote prepare failed: ${prepared.status}`);
    throw new Error(`Heaven workspace preparation failed with ${prepared.status} / ${prepared.exit_code}.`);
  }
  const match = String(prepared.stdout || "").match(/REMOTE_WORKDIR=(.+)\s*$/m);
  if (!match) throw new Error("Heaven prepare result did not return REMOTE_WORKDIR.");
  const remoteWorktree = match[1].trim();
  const remoteParent = path.win32.dirname(remoteWorktree);
  const remotePromptPath = path.win32.join(remoteParent, `${remoteLeaf}.agent-control-prompt.txt`);
  const remotePatchPath = path.win32.join(remoteParent, `${remoteLeaf}.agent-control.patch`);

  const basePrompt = fs.readFileSync(spec.promptPath, "utf8");
  const remotePrompt = [
    basePrompt.trimEnd(),
    "",
    "REMOTE HEAVEN EXECUTION CONTRACT:",
    "- You are running in an isolated workspace on heaven; heaven2 remains control/credential authority.",
    "- Do not push, publish, change remotes, or move credentials.",
    "- Do not commit. Leave all source edits in the working tree; the controller will transfer a binary patch back to heaven2.",
    "- Run the requested verification that is possible in this workspace and report exact commands/results.",
    ""
  ].join("\n");

  const promptWrite = await runHeavenBridgeAction({
    id: writePromptId,
    action: "fs_write",
    params: { path: remotePromptPath, content: remotePrompt, mode: "rewrite" },
    timeoutMs: 120_000
  });
  if (!bridgeResultSucceeded(promptWrite)) {
    throw new Error(`Heaven prompt write failed with ${promptWrite.status} / ${promptWrite.exit_code}.`);
  }

  const executeCommand = buildRemoteCodexCommand({
    workdir: remoteWorktree,
    promptPath: remotePromptPath,
    model: spec.model || null
  });
  const executed = await runHeavenBridgeAction({
    id: executeId,
    action: "proc_run",
    params: {
      shell: "powershell",
      cwd: remoteWorktree,
      timeout_seconds: Math.max(60, Number(spec.executionTimeoutSeconds || 7200)),
      command: executeCommand
    },
    timeoutMs: Math.max(120_000, Number(spec.executionTimeoutMs || 7_500_000))
  });
  const textualOutput = [executed.stdout, executed.stderr].filter(Boolean).join("\n").trim();
  writeLastMessage(spec, textualOutput);
  if (!bridgeResultSucceeded(executed)) {
    throw new Error(`Heaven Codex execution failed with authoritative status ${executed.status} / exit ${executed.exit_code}.`);
  }

  const patchCommand = [
    "$ErrorActionPreference = 'Stop'",
    `git -C ${psQuote(remoteWorktree)} add -A`,
    `git -C ${psQuote(remoteWorktree)} diff --cached --binary ${psQuote(spec.baseSha)} -- . | Set-Content -LiteralPath ${psQuote(remotePatchPath)} -Encoding utf8`,
    `$bytes = (Get-Item -LiteralPath ${psQuote(remotePatchPath)}).Length`,
    "Write-Output ('PATCH_BYTES=' + $bytes)"
  ].join("; ");
  const patched = await runHeavenBridgeAction({
    id: patchId,
    action: "proc_run",
    params: {
      shell: "powershell",
      cwd: remoteWorktree,
      timeout_seconds: 120,
      command: patchCommand
    },
    timeoutMs: 180_000
  });
  if (!bridgeResultSucceeded(patched)) {
    throw new Error(`Heaven patch generation failed with ${patched.status} / ${patched.exit_code}.`);
  }

  const patchBuffer = await readHeavenBinaryFile(remotePatchPath, { jobPrefix: prefix });
  if (patchBuffer.length > 0) {
    const localPatch = path.join(path.dirname(spec.promptPath), `${safeId(spec.agentId)}.remote.patch`);
    fs.writeFileSync(localPatch, patchBuffer);
    try {
      const currentBranch = await git(spec.localWorktree, ["branch", "--show-current"]);
      if (currentBranch !== spec.branchName) {
        throw new Error(`Local Agent Control worktree branch changed unexpectedly: ${currentBranch}.`);
      }
      await git(spec.localWorktree, ["apply", "--index", "--3way", localPatch]);
      const staged = await git(spec.localWorktree, ["diff", "--cached", "--name-only"]);
      if (staged) {
        await git(spec.localWorktree, ["commit", "-m", `Agent ${spec.role || "worker"}: ${String(spec.task || spec.taskId).slice(0, 72)}`]);
      }
    } finally {
      try { fs.unlinkSync(localPatch); } catch {}
    }
  }

  const currentSha = await git(spec.localWorktree, ["rev-parse", "HEAD"]);
  let pushed = false;
  if (currentSha !== spec.baseSha) {
    await git(spec.localWorktree, ["push", "--set-upstream", "origin", spec.branchName]);
    pushed = true;
  }
  process.stdout.write(JSON.stringify({
    provider: "heaven-bridge",
    protocol: "chatgpt-heaven-bridge-v2",
    host: "heaven",
    remoteJobId: executeId,
    patchBytes: patchBuffer.length,
    currentSha,
    pushed,
    textOnly: patchBuffer.length === 0
  }) + "\n");
}

main().catch(error => {
  fail(error?.stack || error?.message || String(error));
});
