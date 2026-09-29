from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
TARGETS = [
    "heaven-control-plane",
    "heaven-workflows",
    "heaven-task-queue",
    "heaven-local-ai",
    "heaven-git-ops",
    "heaven-process-services",
    "heaven-file-ops",
    "heaven-desktop",
    "heaven-browser",
]

failed = []
for name in TARGETS:
    verify = ROOT / name / "verify.py"
    print(f"==> verifying {name}: {verify}")
    code = subprocess.call([sys.executable, str(verify)])
    if code:
        failed.append((name, code))

if failed:
    for name, code in failed:
        print(f"FAILED {name}: exit {code}", file=sys.stderr)
    raise SystemExit(1)

print("All plugin verification gates passed.")
