from __future__ import annotations
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from tooling import GAP_FIELDS, build_index, draft_gap, duplicate_gaps, parse_gaps, resolve, validate_backlog, validate_catalog


class ToolingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.repo = ROOT.parents[1]
        cls.backlog = (cls.repo / "plugins" / "PLUGIN_GAP_BACKLOG.md").read_text(encoding="utf-8")

    def test_catalog_covers_runtime_plugins(self) -> None:
        self.assertEqual([], validate_catalog(self.repo))
        self.assertGreaterEqual(len(build_index(self.repo)["packages"]), 14)

    def test_specialized_git_router_wins(self) -> None:
        matches = resolve(["git"], {"machine":"heaven"}, root=self.repo)
        self.assertGreaterEqual(len(matches), 2)
        self.assertEqual("heaven-git-ops", matches[0]["plugin"])

    def test_exact_browser_capability_and_runtime_evidence(self) -> None:
        matches = resolve(["browser.navigate"], {"machine":"heaven2","runtime_capabilities":["browser.navigate"]}, root=self.repo)
        self.assertEqual(["heaven-browser"], [m["plugin"] for m in matches])
        self.assertEqual("available", matches[0]["availability"]["status"])

    def test_runtime_unknown_is_explicit(self) -> None:
        self.assertEqual("unknown", resolve(["browser.navigate"], {}, root=self.repo)[0]["availability"]["status"])

    def test_backlog_schema_and_duplicate_detection(self) -> None:
        self.assertEqual([], validate_backlog(self.backlog))
        dupes = duplicate_gaps(self.backlog, "toolbox capability index task router", "route tasks to plugin capability", "resolve_capabilities")
        self.assertTrue(dupes)
        self.assertEqual("PG-001", dupes[0]["id"])

    def test_gap_draft_has_all_fields(self) -> None:
        entry = parse_gaps(draft_gap(self.backlog, "example adapter", "fixture", "example.resolve", "Medium", "existing owner"))[0]
        for field in GAP_FIELDS:
            self.assertIn(field, entry["fields"])


if __name__ == "__main__":
    unittest.main()
