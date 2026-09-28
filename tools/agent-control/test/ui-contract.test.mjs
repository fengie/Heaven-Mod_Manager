import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const html = fs.readFileSync(path.resolve(HERE, "..", "public", "index.html"), "utf8");

test("command center inline application script parses", () => {
  const start = html.indexOf("<script>") + "<script>".length;
  const end = html.indexOf("</script>", start);
  assert.ok(start > 7 && end > start);
  assert.doesNotThrow(() => new Function(html.slice(start, end)));
});

test("command center exposes governed daily-operation surfaces", () => {
  for (const text of ["Current mission", "Quick deploy", "Emergency Stop", "Release gate", "Global search", "Improve Agent Manager", "Objective optional"]) {
    assert.ok(html.toLowerCase().includes(text.toLowerCase()), `missing UI surface: ${text}`);
  }
});

test("command center uses current safety-hardened API contract", () => {
  for (const route of ["/api/control/settings", "/api/control/emergency-stop", "/api/control/stop-swarm", "/api/search?q=", "/api/improvements"]) {
    assert.ok(html.includes(route), `missing route ${route}`);
  }
  assert.equal(html.includes("/api/command/execute"), false);
  assert.equal(html.includes("/api/emergency-stop"), false);
});
