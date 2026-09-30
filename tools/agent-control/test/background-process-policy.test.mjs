import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const AGENT_ROOT = path.resolve(HERE, "..");
const REPO_ROOT = path.resolve(AGENT_ROOT, "..", "..");
const HELPER = path.join(AGENT_ROOT, "lib", "background-process.mjs");

function walkRuntimeMjs(dir) {
  const rows = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === "test" || entry.name === "node_modules" || entry.name === "data") continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) rows.push(...walkRuntimeMjs(full));
    else if (entry.isFile() && entry.name.endsWith(".mjs")) rows.push(full);
  }
  return rows;
}

function read(rel) {
  return fs.readFileSync(path.join(REPO_ROOT, rel), "utf8");
}

test("Agent Control runtime child processes must use the forced-hidden wrapper", () => {
  const helperSource = fs.readFileSync(HELPER, "utf8");
  const forced = helperSource.match(/\{\s*\.\.\.options,\s*windowsHide:\s*true\s*\}/g) || [];
  assert.equal(forced.length, 2, "both wrappers must force windowsHide after caller options");
  for (const file of walkRuntimeMjs(AGENT_ROOT)) {
    if (path.resolve(file) === path.resolve(HELPER)) continue;
    const source = fs.readFileSync(file, "utf8");
    assert.equal(source.includes('node:child_process'), false, path.relative(AGENT_ROOT, file) + " bypasses background-process.mjs");
  }
});

test("agent PowerShell bootstrap and recovery launchers stay hidden", () => {
  const bootstrap = read("heaven-bridge/bootstrap.ps1");
  const runner = read("heaven-bridge/install-github-runner.ps1");
  const sentinel = read("heaven-bridge/sentinel.ps1");
  const shortcut = read("tools/agent-control/Install-AgentControlShortcut.ps1");
  const startup = read("tools/agent-control/Install-StartupRestore.ps1");
  const restore = read("tools/agent-control/Restore-StartupSetup.ps1");
  const watchdog = read("tools/agent-control/Watch-AgentControl.ps1");
  assert.match(bootstrap, /Start-Process -WindowStyle Hidden -FilePath \$hostExe -Verb RunAs/);
  assert.match(bootstrap, /\$args = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass/);
  assert.match(bootstrap, /\$sentinelArguments = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass/);
  assert.match(bootstrap, /\$watchdogAction = New-ScheduledTaskAction[\s\S]*?-Argument \('-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File/);
  assert.match(runner, /Start-Process -WindowStyle Hidden -FilePath \$hostExe -Verb RunAs/);
  assert.match(runner, /\$args = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass/);
  assert.match(runner, /\$taskArguments = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File/);
  assert.match(runner, /Start-Process -FilePath \$cmdExe[\s\S]*?-WindowStyle Hidden -Wait/);
  assert.doesNotMatch(runner, /New-ScheduledTaskAction -Execute \$cmdExe/);
  assert.match(sentinel, /'-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File/);
  assert.match(sentinel, /\$watchdogArgs = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass/);
  assert.match(shortcut, /-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass/);
  assert.match(startup, /\$arguments = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass/);
  assert.match(startup, /\$watchdogArguments = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass/);
  assert.match(restore, /Start-Process -FilePath \$node[\s\S]*-WindowStyle Hidden/);
  assert.match(watchdog, /Start-Process -FilePath \$node[\s\S]*-WindowStyle Hidden/);
});
