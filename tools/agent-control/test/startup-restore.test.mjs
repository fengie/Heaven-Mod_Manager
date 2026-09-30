import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import assert from "node:assert/strict";
import { fileURLToPath } from "node:url";

const testDir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(testDir, "..");

function read(name) {
  return fs.readFileSync(path.join(root, name), "utf8");
}

test("manual launcher continually repairs startup restore registration", () => {
  const launcher = read("Start Agent Control.bat");
  assert.match(launcher, /Install-StartupRestore\.ps1/i);
  assert.match(launcher, /AGENT_CONTROL_REPO/);
});

test("startup restore is redundant, idempotent, and avoids duplicate Agent Control", () => {
  const installer = read("Install-StartupRestore.ps1");
  const restore = read("Restore-StartupSetup.ps1");
  const watchdog = read("Watch-AgentControl.ps1");
  const sync = read("Sync-AgentControlRuntime.ps1");

  assert.match(installer, /New-ScheduledTaskTrigger\s+-AtLogOn/i);
  assert.match(installer, /HeavenSetupRestore\.vbs/i);
  assert.match(installer, /Heaven Agent Control Watchdog/i);
  assert.match(installer, /RestartCount 255/i);
  assert.match(installer, /RestartInterval \(New-TimeSpan -Minutes 1\)/i);
  assert.match(installer, /MultipleInstances IgnoreNew/i);
  assert.match(installer, /HeavenAgentControlWatchdog\.vbs/i);
  assert.match(restore, /FileShare\]::None/);
  assert.match(restore, /Heaven Local Bridge Watchdog/i);
  assert.match(restore, /Test-LocalTcpPort/);
  assert.match(restore, /server\.mjs/);
  assert.match(restore, /startup-profile\.json/i);
  assert.match(restore, /Heaven Agent Control Watchdog/i);
  assert.match(watchdog, /while \(\$true\)/i);
  assert.match(watchdog, /api\/status/i);
  assert.match(watchdog, /controller-process\.json/i);
  assert.match(watchdog, /Test-IsOwnedAgentControlProcess/i);
  assert.match(watchdog, /restart_timestamps_utc/i);
  assert.match(watchdog, /cooldown_level/i);
  assert.match(installer, /Sync-AgentControlRuntime\.ps1/i);
  assert.match(installer, /Copy-Item[^\n]+runtimeSyncScript/i);
  assert.match(sync, /branch[^\n]+--show-current/i);
  assert.match(sync, /status[^\n]+--porcelain/i);
  assert.match(sync, /fetch[^\n]+origin[^\n]+refs\/heads\/main:refs\/remotes\/origin\/main/i);
  assert.match(sync, /merge-base[^\n]+--is-ancestor/i);
  assert.match(sync, /merge[^\n]+--ff-only/i);
  assert.doesNotMatch(sync, /reset\s+--hard|clean\s+-f/i);
  assert.match(restore, /refused_unowned_listener/i);
  assert.match(restore, /Test-IsOwnedAgentControlProcess/i);
  assert.match(restore, /controller\.sourceSha/i);
  assert.match(restore, /controller\.agentControlVersion/i);
  assert.match(watchdog, /Sync-AgentControlSource/i);
});

test("startup restore discovers every manifest-backed local plugin dynamically", () => {
  const restore = read("Restore-StartupSetup.ps1");
  assert.match(restore, /Join-Path \$RepoRoot 'plugins'/);
  assert.match(restore, /manifest\.json/i);
  assert.match(restore, /manifest_count/);
});

test("startup PowerShell avoids ambiguous variable-colon interpolation", () => {
  const scripts = [
    ["Install-StartupRestore.ps1", read("Install-StartupRestore.ps1")],
    ["Restore-StartupSetup.ps1", read("Restore-StartupSetup.ps1")],
    ["Watch-AgentControl.ps1", read("Watch-AgentControl.ps1")],
    ["Sync-AgentControlRuntime.ps1", read("Sync-AgentControlRuntime.ps1")],
  ];
  const unsafeVariableColon = /\$(?!(?:env|script|global|local|private|using):)[A-Za-z_][A-Za-z0-9_]*:/g;

  for (const [name, source] of scripts) {
    assert.deepEqual(
      source.match(unsafeVariableColon) ?? [],
      [],
      `${name} contains a PowerShell interpolation like $name: that must use ${name}: or a format expression`,
    );
  }
});
