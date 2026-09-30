from __future__ import annotations

import ast
import json
import sys
import threading
import unittest
from pathlib import Path

PLUGIN_ROOT = Path(__file__).resolve().parents[1]
if str(PLUGIN_ROOT) not in sys.path:
    sys.path.insert(0, str(PLUGIN_ROOT))

from heaven_control_plane.adapters.heaven_bridge import CallableBridgeTransport, HeavenBridgeAdapter
from heaven_control_plane.protocol import PLUGIN_VERSION, SCHEMA_VERSION, ControlPlaneError
from heaven_control_plane.service import HeavenControlPlane


class FakeTransport:
    def __init__(self):
        self.calls = []
        self.response = {"status": "completed", "data": {"ok": True}}
        self.responses = {}
        self.error = None

    def request(self, action, params, timeout_seconds):
        self.calls.append((action, dict(params), timeout_seconds))
        if self.error:
            raise self.error
        return self.responses.get(action, self.response)


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

    def test_manifest_version_matches_protocol_version(self):
        manifest = json.loads((PLUGIN_ROOT / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(manifest["version"], PLUGIN_VERSION)

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

    def test_falsy_non_mapping_input_is_rejected(self):
        result = self.control.invoke("control.discovery", [])
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "INVALID_INPUT")

    def test_invalid_request_id_is_a_structured_error(self):
        result = self.control.invoke("control.discovery", {}, req_id="x" * 129)
        self.assertFalse(result["ok"])
        self.assertEqual(result["request_id"], "invalid-request")
        self.assertEqual(result["error"]["code"], "INVALID_REQUEST_ID")

    def test_capability_permissions_are_enforced_before_transport(self):
        restricted = HeavenControlPlane(
            HeavenBridgeAdapter(self.transport),
            granted_permissions={"control.read"},
        )
        allowed = restricted.invoke("control.discovery")
        self.assertTrue(allowed["ok"])
        denied = restricted.invoke("execution.run", {"command": "Write-Output nope"})
        self.assertFalse(denied["ok"])
        self.assertEqual(denied["error"]["code"], "PERMISSION_DENIED")
        self.assertEqual(denied["error"]["details"]["required_permission"], "execution.run")
        self.assertEqual(self.transport.calls, [])

    def test_oversized_request_is_rejected_before_transport(self):
        result = self.control.invoke("execution.run", {"command": "x" * 1_100_000})
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "INPUT_TOO_LARGE")
        self.assertEqual(self.transport.calls, [])

    def test_bridge_error_is_not_wrapped_as_success(self):
        self.transport.response = {
            "status": "error",
            "exit_code": 1,
            "error": {"code": "PATH_OUTSIDE_ALLOWED_ROOTS", "message": "blocked"},
        }
        result = self.control.invoke("filesystem.read", {"path": r"C:\repo\x.txt"})
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "PATH_OUTSIDE_ALLOWED_ROOTS")

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

    def test_filesystem_write_refuses_existing_rewrite_without_overwrite_confirmation(self):
        self.transport.responses["fs_info"] = {"status": "completed", "data": {"exists": True}}
        denied = self.control.invoke(
            "filesystem.write",
            {"path": r"C:\repo\a.txt", "content": "two", "mode": "rewrite"},
        )
        self.assertFalse(denied["ok"])
        self.assertEqual(denied["error"]["code"], "OVERWRITE_CONFIRMATION_REQUIRED")
        self.assertEqual([call[0] for call in self.transport.calls], ["fs_info"])

        allowed = self.control.invoke(
            "filesystem.write",
            {"path": r"C:\repo\a.txt", "content": "two", "mode": "rewrite", "overwrite": True},
        )
        self.assertTrue(allowed["ok"])
        self.assertEqual(self.transport.calls[-1][0], "fs_write")

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
        self.assertFalse(params["replace_all"])

        before = len(self.transport.calls)
        bulk = self.control.invoke(
            "filesystem.patch",
            {"path": r"C:\repo\a.txt", "old_string": "x", "new_string": "y", "replace_all": True},
        )
        self.assertFalse(bulk["ok"])
        self.assertEqual(bulk["error"]["code"], "BULK_PATCH_REQUIRES_EXACT_PRIMITIVE")
        self.assertEqual(len(self.transport.calls), before)

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
        self.assertIn("git ls-remote --exit-code origin refs/heads/main", command)
        self.assertNotIn("git fetch", command)
        self.assertIn("exact=($local -eq $remote)", command)

    def test_git_nonzero_exit_becomes_structured_error(self):
        self.transport.response = {"status": "completed", "exit_code": 128, "stderr": "fatal: not a git repository"}
        result = self.control.invoke("git.status", {"repo": r"C:\repo"})
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "GIT_COMMAND_FAILED")

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

    def test_artifact_secret_canary_is_refused(self):
        canaries = (
            "API_TOKEN=abcdefgh12345678",
            "refresh_token: abcdefgh12345678",
            "password=correct-horse-battery-staple",
            "Authorization: Bearer abcdefgh12345678",
            "ghp_" + "abcdefghijklmnopqrstuvwxyz",
        )
        for index, canary in enumerate(canaries):
            with self.subTest(canary=canary):
                with self.assertRaises(ControlPlaneError) as raised:
                    self.control.artifacts.put(f"secret-{index}", canary)
                self.assertEqual(raised.exception.code, "SECRET_ARTIFACT_BLOCKED")

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
    def test_bridge_adapter_actions_exist_in_canonical_worker(self):
        worker = PLUGIN_ROOT.parents[1] / "heaven-bridge" / "worker.py"
        tree = ast.parse(worker.read_text(encoding="utf-8"))
        actions = set()
        for node in tree.body:
            if not isinstance(node, ast.Assign):
                continue
            if not any(isinstance(target, ast.Name) and target.id == "DIRECT_ACTIONS" for target in node.targets):
                continue
            if isinstance(node.value, ast.Set):
                actions.update(
                    item.value
                    for item in node.value.elts
                    if isinstance(item, ast.Constant) and isinstance(item.value, str)
                )
        required = {"health", "cancel", "proc_run", "proc_start", "proc_read", "proc_input", "proc_kill", "proc_list_sessions", "fs_read", "fs_write", "fs_edit", "fs_info", "fs_list", "fs_search"}
        self.assertTrue(required.issubset(actions), required - actions)


    def test_phase1_discovery_includes_sessions_and_filesystem_search(self):
        result = self.control.invoke("control.discovery")
        names = {item["name"] for item in result["data"]["capabilities"]}
        self.assertTrue({
            "execution.session.start",
            "execution.session.read",
            "execution.session.input",
            "execution.session.stop",
            "execution.session.list",
            "filesystem.search",
        }.issubset(names))

    def test_session_start_maps_to_proc_start_with_bounds_and_host_handles(self):
        result = self.control.invoke(
            "execution.session.start",
            {
                "shell": "powershell",
                "command": "Write-Output ready",
                "cwd": r"C:\repo",
                "idle_timeout_seconds": 120,
                "max_runtime_seconds": 900,
                "env_from_host": ["GITHUB_TOKEN"],
            },
        )
        self.assertTrue(result["ok"])
        action, params, timeout = self.transport.calls[-1]
        self.assertEqual(action, "proc_start")
        self.assertEqual(timeout, 30)
        self.assertEqual(params["idle_timeout_seconds"], 120)
        self.assertEqual(params["max_runtime_seconds"], 900)
        self.assertEqual(params["env_from_host"], ["GITHUB_TOKEN"])

    def test_session_lifecycle_maps_to_existing_bridge_primitives(self):
        read = self.control.invoke("execution.session.read", {"session_id": "abc123", "max_chars": 5000})
        self.assertTrue(read["ok"])
        self.assertEqual(self.transport.calls[-1], ("proc_read", {"session_id": "abc123", "max_chars": 5000}, 15))

        send = self.control.invoke(
            "execution.session.input",
            {"session_id": "abc123", "input": "status", "newline": False},
        )
        self.assertTrue(send["ok"])
        self.assertEqual(
            self.transport.calls[-1],
            ("proc_input", {"session_id": "abc123", "input": "status", "newline": False}, 15),
        )

        stop = self.control.invoke("execution.session.stop", {"session_id": "abc123", "force": True})
        self.assertTrue(stop["ok"])
        self.assertEqual(self.transport.calls[-1], ("proc_kill", {"session_id": "abc123", "force": True}, 30))

        listed = self.control.invoke("execution.session.list", {})
        self.assertTrue(listed["ok"])
        self.assertEqual(self.transport.calls[-1], ("proc_list_sessions", {}, 15))

    def test_session_validation_fails_closed_before_transport(self):
        before = len(self.transport.calls)
        bad_bool = self.control.invoke(
            "execution.session.input",
            {"session_id": "abc123", "input": "x", "newline": "yes"},
        )
        self.assertFalse(bad_bool["ok"])
        self.assertEqual(bad_bool["error"]["code"], "INVALID_INPUT")

        bad_path = self.control.invoke(
            "execution.session.start",
            {"shell": "powershell", "cwd": r"C:\repo\..\secret"},
        )
        self.assertFalse(bad_path["ok"])
        self.assertEqual(bad_path["error"]["code"], "PATH_TRAVERSAL")
        self.assertEqual(len(self.transport.calls), before)

    def test_filesystem_search_maps_to_allowlisted_bridge_search(self):
        result = self.control.invoke(
            "filesystem.search",
            {
                "path": r"C:\repo",
                "pattern": "CapabilitySpec",
                "mode": "contents",
                "regex": False,
                "case_sensitive": True,
                "max_results": 25,
                "glob": "*.py",
            },
        )
        self.assertTrue(result["ok"])
        action, params, timeout = self.transport.calls[-1]
        self.assertEqual(action, "fs_search")
        self.assertEqual(timeout, 120)
        self.assertEqual(params["mode"], "content")
        self.assertEqual(params["pattern"], "CapabilitySpec")
        self.assertEqual(params["max_results"], 25)
        self.assertEqual(params["glob"], "*.py")

    def test_filesystem_search_rejects_invalid_mode_and_traversal(self):
        before = len(self.transport.calls)
        invalid = self.control.invoke(
            "filesystem.search",
            {"path": r"C:\repo", "pattern": "x", "mode": "everything"},
        )
        self.assertFalse(invalid["ok"])
        self.assertEqual(invalid["error"]["code"], "INVALID_SEARCH_MODE")

        traversal = self.control.invoke(
            "filesystem.search",
            {"path": r"C:\repo\..\secret", "pattern": "x"},
        )
        self.assertFalse(traversal["ok"])
        self.assertEqual(traversal["error"]["code"], "PATH_TRAVERSAL")
        self.assertEqual(len(self.transport.calls), before)


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
