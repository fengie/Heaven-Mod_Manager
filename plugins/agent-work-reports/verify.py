from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
commands = [
    ["node", "--check", "server.mjs"],
    ["node", "--check", "reportctl.mjs"],
    ["node", "--check", "lib/report-store.mjs"],
    ["node", "--check", "lib/agent-control-adapter.mjs"],
    ["node", "--check", "lib/view-model.mjs"],
    ["node", "--check", "public/app.js"],
]
for command in commands:
    print("+", " ".join(command))
    code = subprocess.call(command, cwd=ROOT)
    if code:
        raise SystemExit(code)

tests = sorted(path.relative_to(ROOT).as_posix() for path in (ROOT / "test").glob("*.test.mjs"))
if not tests:
    raise SystemExit("No agent-work-reports tests were found.")
command = ["node", "--test", *tests]
print("+", " ".join(command))
code = subprocess.call(command, cwd=ROOT)
if code:
    raise SystemExit(code)

print("Agent Work Reports verification passed.")
