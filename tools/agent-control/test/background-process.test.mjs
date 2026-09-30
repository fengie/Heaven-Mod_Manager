import assert from "node:assert/strict";
import test from "node:test";

import { hiddenWindowsOptions } from "../lib/background-process.mjs";

test("agent child processes always request hidden Windows consoles", () => {
  const defaulted = hiddenWindowsOptions();
  assert.equal(defaulted.windowsHide, true);

  const forced = hiddenWindowsOptions({
    windowsHide: false,
    cwd: "C:\\Temp",
    env: { EXAMPLE: "1" }
  });
  assert.equal(forced.windowsHide, true);
  assert.equal(forced.cwd, "C:\\Temp");
  assert.deepEqual(forced.env, { EXAMPLE: "1" });
});
