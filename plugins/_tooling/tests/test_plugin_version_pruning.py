from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from prune_outdated_plugins import SemVer, apply_prune, plan_prune


def write_plugin(directory: Path, name: str, version: str, *, manifest: str = "plugin.json") -> None:
    directory.mkdir(parents=True, exist_ok=True)
    target = directory / manifest
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps({"name": name, "version": version}), encoding="utf-8")


class PluginVersionPruningTests(unittest.TestCase):
    def test_semver_orders_prerelease_before_release(self) -> None:
        self.assertLess(SemVer.parse("1.2.3-alpha.1"), SemVer.parse("1.2.3"))
        self.assertLess(SemVer.parse("1.2.3-alpha.2"), SemVer.parse("1.2.3-alpha.10"))
        self.assertLess(SemVer.parse("1.2.3"), SemVer.parse("1.2.4"))

    def test_plan_removes_only_older_same_identity(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            write_plugin(root / "bridge-081", "heaven-local-bridge", "0.8.1")
            write_plugin(root / "bridge-082", "heaven-local-bridge", "0.8.2")
            write_plugin(root / "agent-control", "heaven-agent-control", "0.6.3")

            plan = plan_prune([root])
            self.assertEqual(["0.8.1"], [item["version"] for item in plan["remove"]])
            self.assertIn("0.8.2", [item["version"] for item in plan["keep"]])
            self.assertIn("0.6.3", [item["version"] for item in plan["keep"]])

    def test_apply_deletes_stale_copy_and_keeps_newest(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            old = root / "tool-1"
            new = root / "tool-2"
            write_plugin(old, "tool", "1.0.0")
            write_plugin(new, "tool", "2.0.0")

            plan = plan_prune([root])
            removed = apply_prune(plan, [root])

            self.assertEqual(1, len(removed))
            self.assertFalse(old.exists())
            self.assertTrue(new.exists())

    def test_windows_utf8_bom_manifests_are_supported(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            old = root / "tool-old"
            new = root / "tool-new"
            old.mkdir(parents=True)
            new.mkdir(parents=True)
            (old / "plugin.json").write_text(
                json.dumps({"name": "tool", "version": "1.0.0"}),
                encoding="utf-8-sig",
            )
            (new / "plugin.json").write_text(
                json.dumps({"name": "tool", "version": "2.0.0"}),
                encoding="utf-8-sig",
            )

            plan = plan_prune([root])
            self.assertEqual(["1.0.0"], [item["version"] for item in plan["remove"]])

    def test_non_semver_versions_are_never_deleted(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            write_plugin(root / "legacy", "tool", "latest")
            write_plugin(root / "stable", "tool", "2.0.0")
            plan = plan_prune([root])
            self.assertEqual([], plan["remove"])
            self.assertEqual("non-semver-version", plan["skipped"][0]["reason"])

    def test_canonical_repo_source_is_protected(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            base = Path(td)
            repo = base / "repo"
            installs = base / "installs"
            canonical = repo / "plugins" / "tool"
            old = installs / "tool-old"
            new = installs / "tool-new"
            write_plugin(canonical, "tool", "0.5.0", manifest="manifest.json")
            write_plugin(old, "tool", "1.0.0")
            write_plugin(new, "tool", "2.0.0")

            plan = plan_prune([repo / "plugins", installs], repo_root=repo)
            self.assertIn("canonical-repo-source", [item["reason"] for item in plan["keep"]])
            self.assertEqual([str(old.resolve())], [item["path"] for item in plan["remove"]])


if __name__ == "__main__":
    unittest.main()
