from __future__ import annotations

import json
import sys
import threading
import unittest
from pathlib import Path

PLUGIN_ROOT = Path(__file__).resolve().parents[1]
if str(PLUGIN_ROOT) not in sys.path:
    sys.path.insert(0, str(PLUGIN_ROOT))

from heaven_control_plane.adapters.heaven_bridge import CallableBridgeTransport, HeavenBridgeAdapter
from heaven_control_plane.protocol import SCHEMA_VERSION
from heaven_control_plane.service import HeavenControlPlane


class FakeTransport:
    def __init__(self):
        self.calls = []
        self.response = {"status": "completed", "data": {"ok": True}}
        self.error = None

    def request(self, action, params, timeout_seconds):
        self.calls.append((action, dict(params), timeout_seconds))
        if self.error:
            raise self.error
        return self.response


class VerticalSliceTests(unittest.TestCase):
    def setUp(self):
        self.transport = FakeTransport()
        self.control = HeavenControlPlane(HeavenBridgeAdapter(self.transport))

    def test_discovery_is_versioned_and_complete(self):
        result = self.control.invoke("control.discovery")
        self.assertTrue(result["ok"])
        self.assertEqual(result["schema"], SCHEMA_VERSION)
        names = {item["name"] for item in result["data"]["capabilities"]}
        self.assertTrue({
            "control.health", "control.cancel", "execution.run",
            "filesystem.read", "filesystem.write", "filesystem.patch",
            "git.status", "git.diff", "git.verify_remote_main",
            "observability.logs.page", "observability.artifacts.page",
        }.issubset(names))
        for item in result["data"]["capabilities"]:
            self.assertIn("permission", item)
            self.assertIn("timeout_seconds", item)
            self.assertIn("cancellable", item)
            self.assertIn("max_output_bytes", item)

    def test_health_maps_to_existing_bridge_primitive(self):
        result = self.control.invoke("control.health")
        self.assertTrue(result["ok"])
        self.assertEqual(self.transport.calls[0], ("health", {}, 15))

    def test_unknown_capability_returns_structured_error(self):
        result = self.control.invoke("does.not.exist", {})
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "UNKNOWN_CAPABILITY")

    def test_malformed_input_returns_structured_error(self):
        result = self.control.invoke("execution.run", "not-an-object")
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "INVALID_INPUT")

    def test_oversized_request_is_rejected_before_transport(self):
        result = self.control.invoke("execution.run", {"command": "x" * 1_100_000})
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "INPUT_TOO_LARGE")
        self.assertEqual(self.transport.calls, [])

    def test_execution_maps_to_proc_run_and_bounds_timeout(self):
        result = self.control.invoke(
            "execution.run",
            {"shell": "powershell", "command": "Write-Output ok", "cwd": r"C:\repo", "timeout_seconds": 45},
        )
        self.assertTrue(result["ok"])
        action, params, timeout = self.transport.calls[0]
        self.assertEqual(action, "proc_run")
        self.assertEqual(params["timeout_seconds"], 45)
        self.assertEqual(timeout, 60)

    def test_execution_rejects_unsupported_shell(self):
        result = self.control.invoke("execution.run", {"shell": "bash", "command": "echo nope"})
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "INVALID_SHELL")

    def test_inline_secret_env_is_blocked_but_host_handle_is_allowed(self):
        blocked = self.control.invoke(
            "execution.run",
            {"command": "Write-Output ok", "env": {"API_TOKEN": "secret"}},
        )
        self.assertFalse(blocked["ok"])
        self.assertEqual(blocked["error"]["code"], "SECRET_INLINE_ENV_BLOCKED")

        allowed = self.control.invoke(
            "execution.run",
            {"command": "Write-Output ok", "env_from_host": ["API_TOKEN"]},
        )
        self.assertTrue(allowed["ok"])
        self.assertEqual(self.transport.calls[-1][1]["env_from_host"], ["API_TOKEN"])

    def test_filesystem_read_has_pagination_and_traversal_guard(self):
        ok = self.control.invoke("filesystem.read", {"path": r"C:\repo\README.md", "offset": 4, "length": 20})
        self.assertTrue(ok["ok"])
        action, params, _ = self.transport.calls[-1]
        self.assertEqual(action, "fs_read")
        self.assertEqual(params["offset"], 4)
        self.assertEqual(params["length"], 20)

        bad = self.control.invoke("filesystem.read", {"path": r"C:\repo\..\secret.txt"})
        self.assertFalse(bad["ok"])
        self.assertEqual(bad["error"]["code"], "PATH_TRAVERSAL")

    def test_filesystem_patch_matches_bridge_contract(self):
        result = self.control.invoke(
            "filesystem.patch",
            {"path": r"C:\repo\a.txt", "old_string": "old", "new_string": "new", "replace_all": False},
        )
        self.assertTrue(result["ok"])
        action, params, _ = self.transport.calls[-1]
        self.assertEqual(action, "fs_edit")
        self.assertEqual(params["old_string"], "old")
        self.assertEqual(params["new_string"], "new")

    def test_cancel_maps_to_bridge_job_id(self):
        result = self.control.invoke("control.cancel", {"job_id": "job-123"})
        self.assertTrue(result["ok"])
        self.assertEqual(self.transport.calls[-1], ("cancel", {"job_id": "job-123"}, 15))

    def test_git_commands_use_structured_cwd_not_path_interpolation(self):
        repo = r"C:\Users\owner\repo's copy"
        result = self.control.invoke("git.status", {"repo": repo})
        self.assertTrue(result["ok"])
        action, params, _ = self.transport.calls[-1]
        self.assertEqual(action, "proc_run")
        self.assertEqual(params["cwd"], repo)
        self.assertEqual(params["command"], "git status --short --branch")
        self.assertNotIn(repo, params["command"])

    def test_remote_main_verification_fetches_and_compares_exact_sha(self):
        result = self.control.invoke("git.verify_remote_main", {"repo": r"C:\repo"})
        self.assertTrue(result["ok"])
        command = self.transport.calls[-1][1]["command"]
        self.assertIn("git fetch --quiet origin main", command)
        self.assertIn("origin/main", command)
        self.assertIn("exact=($local -eq $remote)", command)

    def test_transport_timeout_becomes_structured_timeout(self):
        self.transport.error = TimeoutError("slow")
        result = self.control.invoke("control.health")
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "TIMEOUT")

    def test_output_is_bounded(self):
        self.transport.response = {"stdout": "x" * 400_000}
        result = self.control.invoke("execution.run", {"command": "Write-Output ok"})
        self.assertTrue(result["ok"])
        self.assertTrue(result["output_truncated"])
        self.assertTrue(result["data"]["truncated"])
        self.assertLessEqual(len(result["data"]["preview"].encode("utf-8")), 262_144)

    def test_audit_log_is_payload_free_and_paginated(self):
        secret = "do-not-record-this-command"
        self.control.invoke("execution.run", {"command": secret})
        page = self.control.invoke("observability.logs.page", {"offset": 0, "length": 50})
        rendered = json.dumps(page["data"])
        self.assertNotIn(secret, rendered)
        self.assertGreaterEqual(page["data"]["total"], 1)
        self.assertIn("duration_ms", page["data"]["items"][-1])

    def test_artifact_pagination(self):
        self.control.artifacts.put("a1", "abcdefghij", name="demo")
        page = self.control.invoke(
            "observability.artifacts.page",
            {"artifact_id": "a1", "offset": 3, "length": 4},
        )
        self.assertTrue(page["ok"])
        self.assertEqual(page["data"]["text"], "defg")
        self.assertTrue(page["data"]["has_more"])

    def test_audit_log_is_thread_safe_under_concurrent_calls(self):
        threads = [
            threading.Thread(target=lambda: self.control.invoke("control.discovery"))
            for _ in range(20)
        ]
        for thread in threads:
            thread.start()
        for thread in threads:
            thread.join()
        page = self.control.audit_log.page(offset=0, length=100)
        self.assertGreaterEqual(page["total"], 20)


class CallableTransportTests(unittest.TestCase):
    def test_callable_transport_contract(self):
        calls = []
        transport = CallableBridgeTransport(
            lambda action, params, timeout: calls.append((action, dict(params), timeout)) or {"status": "completed"}
        )
        result = transport.request("health", {}, 5)
        self.assertEqual(result["status"], "completed")
        self.assertEqual(calls, [("health", {}, 5)])


if __name__ == "__main__":
    unittest.main()
