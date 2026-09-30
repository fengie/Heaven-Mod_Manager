import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SERVER = path.resolve(HERE, "..", "server.mjs");

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
  assert.match(route, /workerSnapshot\(state, \{ inspectRemote: false \}\)/);
});
