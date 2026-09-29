import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "..");

test("Agent Control launcher self-installs and verifies the heaven2 shortcut", async () => {
  const [batch, installer] = await Promise.all([
    readFile(path.join(root, "Start Agent Control.bat"), "utf8"),
    readFile(path.join(root, "Install-AgentControlShortcut.ps1"), "utf8"),
  ]);

  assert.match(batch, /Install-AgentControlShortcut\.ps1/);
  assert.match(batch, /heaven-bridge\\bootstrap\.ps1/);
  assert.ok(batch.indexOf("Install-AgentControlShortcut.ps1") < batch.indexOf("heaven-bridge\\bootstrap.ps1"));

  assert.match(installer, /Heaven Agent Control/);
  assert.match(installer, /Start Agent Control\.bat/);
  assert.match(installer, /CreateShortcut/);
  assert.match(installer, /IconLocation/);
  assert.match(installer, /Shortcut target verification failed/);
  assert.match(installer, /Shortcut icon verification failed/);
  assert.match(installer, /COMPUTERNAME.*heaven2/s);
});
