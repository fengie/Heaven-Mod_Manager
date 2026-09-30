import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

test("changelog does not contain escaped newline separators", async () => {
  const changelogUrl = new URL("../../../CHANGELOG.md", import.meta.url);
  const changelog = await readFile(changelogUrl, "utf8");
  assert.doesNotMatch(
    changelog,
    /\\n- /,
    "CHANGELOG.md contains a literal \\n before a list item; write a real newline instead"
  );
});
