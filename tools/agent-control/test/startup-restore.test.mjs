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

  assert.match(installer, /New-ScheduledTaskTrigger\s+-AtLogOn/i);
  assert.match(installer, /HeavenSetupRestore\.vbs/i);
  assert.match(restore, /FileShare\]::None/);
  assert.match(restore, /Heaven Local Bridge Watchdog/i);
  assert.match(restore, /Test-LocalTcpPort/);
  assert.match(restore, /server\.mjs/);
  assert.match(restore, /startup-profile\.json/i);
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
