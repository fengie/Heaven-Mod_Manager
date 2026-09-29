import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from heaven_workflows import HeavenWorkflowPlugin


class FakeControlPlane:
    def __init__(self, failures=None):
        self.calls = []
        self.failures = set(failures or [])

    def invoke(self, capability_name, payload=None, **kwargs):
        self.calls.append((capability_name, dict(payload or {})))
        if capability_name in self.failures:
            return {"status": "error", "capability": capability_name}
        return {"status": "ok", "capability": capability_name, "data": dict(payload or {})}


class WorkflowTests(unittest.TestCase):
    def test_repo_snapshot_composes_git_reads(self):
        cp = FakeControlPlane()
        plugin = HeavenWorkflowPlugin(cp)
        result = plugin.repo_snapshot(r"C:\repo")
        self.assertEqual([c[0] for c in cp.calls], ["git.status", "git.diff", "git.verify_remote_main"])
        self.assertIn("remote_main", result)

    def test_verify_repo_stops_after_failure(self):
        cp = FakeControlPlane({"verification.run"})
        plugin = HeavenWorkflowPlugin(cp)
        result = plugin.verify_repo("repo", kinds=("test", "build"))
        self.assertFalse(result["ok"])
        self.assertEqual([c[0] for c in cp.calls], ["verification.detect", "verification.run"])

    def test_parallel_invoke_preserves_input_order(self):
        cp = FakeControlPlane()
        plugin = HeavenWorkflowPlugin(cp)
        result = plugin.parallel_invoke(
            [
                {"name": "health", "capability": "control.health"},
                {"name": "status", "capability": "git.status", "payload": {"repo": "r"}},
                {"name": "search", "capability": "filesystem.search", "payload": {"path": "r", "pattern": "x"}},
            ],
            max_workers=3,
        )
        self.assertTrue(result["ok"])
        self.assertEqual([x["name"] for x in result["results"]], ["health", "status", "search"])

    def test_parallel_invoke_rejects_raw_execution(self):
        cp = FakeControlPlane()
        plugin = HeavenWorkflowPlugin(cp)
        with self.assertRaises(ValueError):
            plugin.parallel_invoke([{"capability": "execution.run", "payload": {"command": "whoami"}}])

    def test_repo_rejects_traversal(self):
        cp = FakeControlPlane()
        plugin = HeavenWorkflowPlugin(cp)
        with self.assertRaises(ValueError):
            plugin.repo_snapshot("../secret")


if __name__ == "__main__":
    unittest.main()
