from __future__ import annotations
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from tooling import validate_backlog, validate_catalog


def main() -> int:
    repo = ROOT.parents[1]
    errors = validate_catalog(repo)
    errors += validate_backlog((repo / "plugins" / "PLUGIN_GAP_BACKLOG.md").read_text(encoding="utf-8"))
    if errors:
        for error in errors: print(error, file=sys.stderr)
        return 1
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.discover(str(ROOT / "tests"), pattern="test_*.py"))
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    raise SystemExit(main())
