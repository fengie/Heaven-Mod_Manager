from __future__ import annotations

import sys
import unittest
from pathlib import Path

PLUGIN_ROOT = Path(__file__).resolve().parents[1]
if str(PLUGIN_ROOT) not in sys.path:
    sys.path.insert(0, str(PLUGIN_ROOT))

from heaven_control_plane.adapters.heaven_bridge import HeavenBridgeAdapter
from heaven_control_plane.service import HeavenControlPlane


class FakeTransport:
    def __init__(self):
        self.calls = []
        self.responses = {}

    def queue(self, action, *responses):
        self.responses.setdefault(action, []).extend(responses)

    def request(self, action, params, timeout_seconds):
        self.calls.append((action, dict(params), timeout_seconds))
        queue = self.responses.get(action, [])
        if queue:
            return queue.pop(0)
        return {"status": "completed", "data": {"ok": True}}


def listing(*paths, has_more=False, next_offset=None):
    items = [{"type": "file", "path": path} for path in paths]
    return {
        "status": "completed",
        "data": {
            "items": items,
            "has_more": has_more,
            "next_offset": len(items) if next_offset is None else next_offset,
        },
    }


class VerificationCapabilityTests(unittest.TestCase):
    def setUp(self):
        self.transport = FakeTransport()
        self.control = HeavenControlPlane(HeavenBridgeAdapter(self.transport))

    def test_discovery_exposes_verification_capabilities(self):
        result = self.control.invoke("control.discovery")
        names = {item["name"] for item in result["data"]["capabilities"]}
        self.assertIn("verification.detect", names)
        self.assertIn("verification.run", names)

    def test_detect_maps_to_bounded_fs_list(self):
        self.transport.queue("fs_list", listing(r"C:\repo\pyproject.toml"))
        result = self.control.invoke("verification.detect", {"repo": r"C:\repo"})
        self.assertTrue(result["ok"])
        self.assertEqual(result["data"]["project_types"], ["python"])
        action, params, timeout = self.transport.calls[-1]
        self.assertEqual(action, "fs_list")
        self.assertEqual(params["path"], r"C:\repo")
        self.assertEqual(params["depth"], 1)
        self.assertEqual(params["max_items"], 1000)
        self.assertEqual(timeout, 60)

    def test_detection_follows_bounded_pagination(self):
        self.transport.queue(
            "fs_list",
            listing(r"C:\repo\README.md", has_more=True, next_offset=1),
            listing(r"C:\repo\package.json", has_more=False, next_offset=2),
        )
        result = self.control.invoke("verification.detect", {"repo": r"C:\repo"})
        self.assertTrue(result["ok"])
        self.assertEqual(result["data"]["project_types"], ["node"])
        fs_calls = [call for call in self.transport.calls if call[0] == "fs_list"]
        self.assertEqual([call[1]["offset"] for call in fs_calls], [0, 1])

    def test_run_uses_fixed_plan_and_structured_cwd(self):
        self.transport.queue("fs_list", listing(r"C:\repo\package.json"))
        self.transport.queue(
            "proc_run",
            {"status": "completed", "exit_code": 0, "stdout": "ok", "stderr": ""},
        )
        result = self.control.invoke(
            "verification.run",
            {"repo": r"C:\repo", "kind": "test", "timeout_seconds": 120},
        )
        self.assertTrue(result["ok"])
        self.assertEqual(result["data"]["plan"]["project_type"], "node")
        self.assertEqual(result["data"]["plan"]["command"], "npm test")
        self.assertEqual(result["data"]["plan"]["timeout_seconds"], 120)
        action, params, timeout = self.transport.calls[-1]
        self.assertEqual(action, "proc_run")
        self.assertEqual(params["cwd"], r"C:\repo")
        self.assertEqual(params["command"], "npm test")
        self.assertEqual(params["timeout_seconds"], 120)
        self.assertEqual(timeout, 135)

    def test_ambiguous_project_requires_explicit_type(self):
        self.transport.queue(
            "fs_list",
            listing(r"C:\repo\pyproject.toml", r"C:\repo\package.json"),
        )
        result = self.control.invoke("verification.run", {"repo": r"C:\repo", "kind": "test"})
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "AMBIGUOUS_PROJECT_TYPE")
        self.assertEqual([call[0] for call in self.transport.calls], ["fs_list"])

    def test_explicit_type_selects_one_detected_project(self):
        self.transport.queue(
            "fs_list",
            listing(r"C:\repo\pyproject.toml", r"C:\repo\package.json"),
        )
        self.transport.queue(
            "proc_run",
            {"status": "completed", "exit_code": 0, "stdout": "", "stderr": ""},
        )
        result = self.control.invoke(
            "verification.run",
            {"repo": r"C:\repo", "project_type": "python", "kind": "lint"},
        )
        self.assertTrue(result["ok"])
        self.assertEqual(self.transport.calls[-1][1]["command"], "python -m compileall -q .")

    def test_nonzero_tool_exit_is_structured_failure(self):
        self.transport.queue("fs_list", listing(r"C:\repo\pyproject.toml"))
        self.transport.queue(
            "proc_run",
            {"status": "completed", "exit_code": 2, "stdout": "", "stderr": "tests failed"},
        )
        result = self.control.invoke("verification.run", {"repo": r"C:\repo", "kind": "test"})
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "VERIFICATION_FAILED")
        self.assertEqual(result["error"]["details"]["exit_code"], 2)

    def test_path_traversal_is_rejected_before_bridge(self):
        result = self.control.invoke(
            "verification.detect",
            {"repo": r"C:\repo\..\secret"},
        )
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "PATH_TRAVERSAL")
        self.assertEqual(self.transport.calls, [])

    def test_verification_permission_is_enforced(self):
        restricted = HeavenControlPlane(
            HeavenBridgeAdapter(self.transport),
            granted_permissions={"verification.read"},
        )
        result = restricted.invoke(
            "verification.run",
            {"repo": r"C:\repo", "kind": "test"},
        )
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "PERMISSION_DENIED")
        self.assertEqual(self.transport.calls, [])

    def test_scan_refuses_nonadvancing_pagination(self):
        self.transport.queue(
            "fs_list",
            listing(r"C:\repo\README.md", has_more=True, next_offset=0),
        )
        result = self.control.invoke("verification.detect", {"repo": r"C:\repo"})
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "INVALID_BRIDGE_RESULT")


if __name__ == "__main__":
    unittest.main()
