import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, "..");
const SERVER = path.join(ROOT, "server.mjs");
const RESTORE = path.join(ROOT, "Restore-StartupSetup.ps1");
const WATCHDOG = path.join(ROOT, "Watch-AgentControl.ps1");

function functionBlock(source, startMarker, endMarker) {
  const start = source.indexOf(startMarker);
  assert.ok(start >= 0, `missing ${startMarker}`);
  const end = source.indexOf(endMarker, start + startMarker.length);
  assert.ok(end > start, `missing end marker ${endMarker}`);
  return source.slice(start, end);
}

test("status health avoids heavyweight controller work", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const start = source.indexOf('pathname === "/api/status"');
  assert.ok(start >= 0);
  const end = source.indexOf("\n    if (req.method", start + 1);
  const route = source.slice(start, end >= 0 ? end : source.length);

  assert.doesNotMatch(route, /buildSnapshot/);
  assert.doesNotMatch(route, /refreshState/);
  assert.doesNotMatch(route, /repositorySnapshot/);
  assert.doesNotMatch(route, /inspectHeavenBridge/);
  assert.match(route, /loadState/);
  assert.match(route, /buildHealthSnapshot/);

  const helper = functionBlock(source, "function buildHealthSnapshot", "async function buildSnapshot");
  assert.doesNotMatch(helper, /\bgit\s*\(/);
  assert.doesNotMatch(helper, /repositorySnapshot|integrationQueue|observedBranches|inspectHeavenBridge/);
  assert.match(helper, /federationSnapshot/);
  assert.match(helper, /lightweightWorkerSnapshot/);
});

test("full dashboard snapshot reuses one Heaven Bridge assessment", () => {
  const source = fs.readFileSync(SERVER, "utf8");
  const block = functionBlock(source, "async function buildSnapshot", "async function deployReview");

  assert.equal(
    (block.match(/inspectHeavenBridge\(\{ sync: false \}\)/g) || []).length,
    1,
    "buildSnapshot must assess Heaven Bridge at most once"
  );
  assert.match(block, /workerSnapshot\(state, heavenBridgeAssessment\)/);
  assert.match(block, /runtimeFederationSnapshot\(state, heavenBridgeAssessment\)/);

  const worker = functionBlock(source, "async function workerSnapshot", "function federationCountCoverage");
  assert.match(worker, /heavenBridgeAssessment === undefined/);

  const federation = functionBlock(source, "async function runtimeFederationSnapshot", "function activeLeaseForBoundary");
  assert.match(federation, /heavenBridgeAssessment === undefined/);
});

test("startup restore and watchdog use the lightweight status endpoint", () => {
  const restore = fs.readFileSync(RESTORE, "utf8");
  const watchdog = fs.readFileSync(WATCHDOG, "utf8");

  assert.match(restore, /127\.0\.0\.1:7331\/api\/status/);
  assert.match(restore, /TimeoutSec 3/);
  assert.match(watchdog, /127\.0\.0\.1:7331\/api\/status/);
  assert.match(watchdog, /TimeoutSec 3/);
});
