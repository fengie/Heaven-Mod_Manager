import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
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


function run(command, args, { cwd, expectFailure = false } = {}) {
  const result = spawnSync(command, args, {
    cwd,
    encoding: "utf8",
    windowsHide: true
  });
  if (!expectFailure && result.status !== 0) {
    throw new Error(`${command} ${args.join(" ")} failed (${result.status}): ${result.stderr || result.stdout}`);
  }
  if (expectFailure) assert.notEqual(result.status, 0, `expected ${command} to fail`);
  return result;
}

function git(cwd, ...args) {
  return run("git", args, { cwd });
}

function configureGit(cwd) {
  git(cwd, "config", "user.email", "agent-control-test@example.invalid");
  git(cwd, "config", "user.name", "Agent Control Test");
}

function runSyncGuard(repoRoot, expectFailure = false) {
  const quote = value => String(value).replaceAll("'", "''");
  return run("powershell.exe", [
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-Command",
    `& '${quote(path.join(root, "Sync-AgentControlRuntime.ps1"))}' -RepoRoot '${quote(repoRoot)}' | Out-Null`
  ], { expectFailure });
}

test("runtime freshness guard fast-forwards clean main and preserves unsafe checkouts", {
  skip: process.platform !== "win32"
}, t => {
  const sandbox = fs.mkdtempSync(path.join(os.tmpdir(), "agent-control-runtime-sync-"));
  t.after(() => fs.rmSync(sandbox, { recursive: true, force: true }));

  const remote = path.join(sandbox, "remote.git");
  const seed = path.join(sandbox, "seed");
  run("git", ["init", "--bare", remote]);
  run("git", ["init", "-b", "main", seed]);
  configureGit(seed);
  fs.writeFileSync(path.join(seed, "base.txt"), "base\n");
  git(seed, "add", "base.txt");
  git(seed, "commit", "-m", "base");
  git(seed, "remote", "add", "origin", remote);
  git(seed, "push", "-u", "origin", "main");
  run("git", ["--git-dir", remote, "symbolic-ref", "HEAD", "refs/heads/main"]);

  const clone = name => {
    const target = path.join(sandbox, name);
    run("git", ["clone", remote, target]);
    configureGit(target);
    return target;
  };

  const clean = clone("clean");
  fs.writeFileSync(path.join(seed, "remote-1.txt"), "remote-1\n");
  git(seed, "add", "remote-1.txt");
  git(seed, "commit", "-m", "remote one");
  git(seed, "push", "origin", "main");
  runSyncGuard(clean);
  assert.equal(git(clean, "rev-parse", "HEAD").stdout.trim(), git(seed, "rev-parse", "HEAD").stdout.trim());

  const dirtyBefore = git(clean, "rev-parse", "HEAD").stdout.trim();
  fs.writeFileSync(path.join(clean, "local-dirty.txt"), "preserve me\n");
  runSyncGuard(clean, true);
  assert.equal(git(clean, "rev-parse", "HEAD").stdout.trim(), dirtyBefore);
  assert.equal(fs.readFileSync(path.join(clean, "local-dirty.txt"), "utf8"), "preserve me\n");

  const nonMain = clone("non-main");
  git(nonMain, "checkout", "-b", "feature/test");
  runSyncGuard(nonMain, true);
  assert.equal(git(nonMain, "branch", "--show-current").stdout.trim(), "feature/test");

  const detached = clone("detached");
  git(detached, "checkout", "--detach", "HEAD");
  runSyncGuard(detached, true);
  assert.equal(git(detached, "branch", "--show-current").stdout.trim(), "");

  const ahead = clone("ahead");
  fs.writeFileSync(path.join(ahead, "ahead.txt"), "ahead\n");
  git(ahead, "add", "ahead.txt");
  git(ahead, "commit", "-m", "local ahead");
  const aheadSha = git(ahead, "rev-parse", "HEAD").stdout.trim();
  runSyncGuard(ahead, true);
  assert.equal(git(ahead, "rev-parse", "HEAD").stdout.trim(), aheadSha);

  const diverged = clone("diverged");
  fs.writeFileSync(path.join(diverged, "local.txt"), "local\n");
  git(diverged, "add", "local.txt");
  git(diverged, "commit", "-m", "local divergent");
  const divergentSha = git(diverged, "rev-parse", "HEAD").stdout.trim();
  fs.writeFileSync(path.join(seed, "remote-2.txt"), "remote-2\n");
  git(seed, "add", "remote-2.txt");
  git(seed, "commit", "-m", "remote two");
  git(seed, "push", "origin", "main");
  runSyncGuard(diverged, true);
  assert.equal(git(diverged, "rev-parse", "HEAD").stdout.trim(), divergentSha);
});
