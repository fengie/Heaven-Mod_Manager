import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const html = fs.readFileSync(path.join(HERE, "..", "public", "index.html"), "utf8");

test("operator UI motion stays subtle and respects reduced-motion preferences", () => {
  assert.match(html, /--motion-fast:120ms/);
  assert.match(html, /--motion-base:180ms/);
  assert.match(html, /--motion-slow:240ms/);
  assert.match(html, /@media \(prefers-reduced-motion: reduce\)/);
  assert.match(html, /animation-duration:\.01ms !important/);
  assert.match(html, /transition-duration:\.01ms !important/);
});

test("agent logs animate through a CSS state instead of snapping display on and off", () => {
  assert.match(html, /\.log\.show\s*\{/);
  assert.match(html, /classList\.toggle\("show"\)/);
  assert.doesNotMatch(html, /box\.style\.display\s*=/);
});
