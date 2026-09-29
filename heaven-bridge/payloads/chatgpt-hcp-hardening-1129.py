from pathlib import Path

ROOT = Path('.')

def replace(path, old, new, count=1):
    p = ROOT / path
    text = p.read_text(encoding='utf-8')
    actual = text.count(old)
    if actual != count:
        raise SystemExit(f'{path}: expected {count} occurrence(s), found {actual}: {old[:80]!r}')
    p.write_text(text.replace(old, new, count), encoding='utf-8')

adapter = 'plugins/heaven-control-plane/heaven_control_plane/adapters/heaven_bridge.py'

replace(adapter,
'''class HeavenBridgeAdapter:
    """Compatibility adapter from stable control-plane names to existing bridge actions."""

    def __init__(self, transport: BridgeTransport):
        self.transport = transport

    def invoke(self, capability: CapabilitySpec, payload: Mapping[str, Any]) -> Mapping[str, Any]:
''',
'''class HeavenBridgeAdapter:
    """Compatibility adapter from stable control-plane names to existing bridge actions."""

    def __init__(self, transport: BridgeTransport):
        self.transport = transport

    def _request(self, action: str, params: Mapping[str, Any], timeout_seconds: int) -> Mapping[str, Any]:
        response = self.transport.request(action, params, timeout_seconds)
        if not isinstance(response, Mapping):
            raise ControlPlaneError("INVALID_BRIDGE_RESULT", "bridge result must be an object")
        status = str(response.get("status") or "").lower()
        raw_error = response.get("error")
        if status == "error" or raw_error:
            if isinstance(raw_error, Mapping):
                code = str(raw_error.get("code") or "BRIDGE_ERROR")
                message = str(raw_error.get("message") or "bridge request failed")
                details = raw_error.get("details") if isinstance(raw_error.get("details"), Mapping) else {}
            else:
                code = "BRIDGE_ERROR"
                message = str(response.get("stderr") or raw_error or "bridge request failed")
                details = {}
            raise ControlPlaneError(code, message, details)
        return response

    @staticmethod
    def _require_git_success(response: Mapping[str, Any]) -> Mapping[str, Any]:
        exit_code = response.get("exit_code")
        if exit_code is not None and int(exit_code) != 0:
            raise ControlPlaneError(
                "GIT_COMMAND_FAILED",
                "git command failed",
                {"exit_code": int(exit_code), "stderr": str(response.get("stderr") or "")[:2048]},
            )
        return response

    def invoke(self, capability: CapabilitySpec, payload: Mapping[str, Any]) -> Mapping[str, Any]:
''')

replace(adapter, 'return self.transport.request("health", {}, min(capability.timeout_seconds, 15))', 'return self._request("health", {}, min(capability.timeout_seconds, 15))')
replace(adapter, 'return self.transport.request("cancel", {"job_id": job_id}, min(capability.timeout_seconds, 15))', 'return self._request("cancel", {"job_id": job_id}, min(capability.timeout_seconds, 15))')
replace(adapter, 'return self.transport.request("proc_run", params, timeout + 15)', 'return self._request("proc_run", params, timeout + 15)')
replace(adapter, 'return self.transport.request("fs_read", params, capability.timeout_seconds)', 'return self._request("fs_read", params, capability.timeout_seconds)')

replace(adapter,
'''        if capability.name == "filesystem.write":
            content = require_string(data.get("content"), "content", allow_empty=True, max_length=1_000_000)
            mode = str(data.get("mode") or "rewrite").lower()
            if mode not in {"rewrite", "append"}:
                raise ControlPlaneError("INVALID_MODE", "mode must be rewrite or append")
            params = {"path": _safe_path(data.get("path")), "content": content, "mode": mode}
            return self.transport.request("fs_write", params, capability.timeout_seconds)
''',
'''        if capability.name == "filesystem.write":
            content = require_string(data.get("content"), "content", allow_empty=True, max_length=1_000_000)
            mode = str(data.get("mode") or "rewrite").lower()
            if mode not in {"rewrite", "append"}:
                raise ControlPlaneError("INVALID_MODE", "mode must be rewrite or append")
            path = _safe_path(data.get("path"))
            overwrite = data.get("overwrite", False)
            if not isinstance(overwrite, bool):
                raise ControlPlaneError("INVALID_INPUT", "overwrite must be a boolean")
            if mode == "rewrite" and not overwrite:
                info = self._request("fs_info", {"path": path}, capability.timeout_seconds)
                info_data = info.get("data") if isinstance(info.get("data"), Mapping) else {}
                if bool(info_data.get("exists")):
                    raise ControlPlaneError(
                        "OVERWRITE_CONFIRMATION_REQUIRED",
                        "path exists; set overwrite=true to replace it",
                        {"path": path},
                    )
            params = {"path": path, "content": content, "mode": mode}
            return self._request("fs_write", params, capability.timeout_seconds)
''')

replace(adapter,
'''        if capability.name == "filesystem.patch":
            old = require_string(data.get("old_string"), "old_string", max_length=250_000)
            new = require_string(data.get("new_string"), "new_string", allow_empty=True, max_length=250_000)
            params = {
                "path": _safe_path(data.get("path")),
                "old_string": old,
                "new_string": new,
                "replace_all": bool(data.get("replace_all", False)),
            }
            return self.transport.request("fs_edit", params, capability.timeout_seconds)
''',
'''        if capability.name == "filesystem.patch":
            old = require_string(data.get("old_string"), "old_string", max_length=250_000)
            new = require_string(data.get("new_string"), "new_string", allow_empty=True, max_length=250_000)
            if bool(data.get("replace_all", False)):
                raise ControlPlaneError(
                    "BULK_PATCH_REQUIRES_EXACT_PRIMITIVE",
                    "filesystem.patch v1 only permits the bridge's unique single-replacement mode",
                )
            params = {
                "path": _safe_path(data.get("path")),
                "old_string": old,
                "new_string": new,
                "replace_all": False,
            }
            return self._request("fs_edit", params, capability.timeout_seconds)
''')

replace(adapter,
'''            else:
                command = (
                    "$ErrorActionPreference='Stop'; "
                    "git fetch --quiet origin main; "
                    "$local=(git rev-parse HEAD).Trim(); "
                    "$remote=(git rev-parse origin/main).Trim(); "
                    "$branch=(git branch --show-current).Trim(); "
                    "[pscustomobject]@{branch=$branch;local=$local;remote_main=$remote;exact=($local -eq $remote)} "
                    "| ConvertTo-Json -Compress"
                )
                timeout = 120
            return self.transport.request(
                "proc_run",
                {"shell": "powershell", "command": command, "cwd": cwd, "timeout_seconds": timeout},
                timeout + 15,
            )
''',
'''            else:
                command = (
                    "$ErrorActionPreference='Stop'; "
                    "$local=(git rev-parse HEAD).Trim(); "
                    "$remoteLine=(git ls-remote --exit-code origin refs/heads/main | Select-Object -First 1); "
                    "if($LASTEXITCODE -ne 0 -or -not $remoteLine){ throw 'origin/main not found' }; "
                    "$remote=(($remoteLine -split '\\s+')[0]).Trim(); "
                    "$branch=(git branch --show-current).Trim(); "
                    "[pscustomobject]@{branch=$branch;local=$local;remote_main=$remote;exact=($local -eq $remote)} "
                    "| ConvertTo-Json -Compress"
                )
                timeout = 120
            response = self._request(
                "proc_run",
                {"shell": "powershell", "command": command, "cwd": cwd, "timeout_seconds": timeout},
                timeout + 15,
            )
            return self._require_git_success(response)
''')

replace('plugins/heaven-control-plane/heaven_control_plane/protocol.py', 'PLUGIN_VERSION = "0.1.0"', 'PLUGIN_VERSION = "0.1.1"')
replace('plugins/heaven-control-plane/manifest.json', '"version": "0.1.0"', '"version": "0.1.1"')
replace('plugins/heaven-control-plane/pyproject.toml', 'version = "0.1.0"', 'version = "0.1.1"')
replace('plugins/heaven-control-plane/README.md', '## Implemented vertical slice (v0.1.0)', '## Implemented vertical slice (v0.1.1)')
replace('plugins/heaven-control-plane/README.md',
'''- Git status/diff/exact remote-\`main\` verification using a structured working directory instead of path interpolation;
''',
'''- Git status/diff/exact remote-\`main\` verification using \`git ls-remote\` against \`refs/heads/main\` and a structured working directory instead of path interpolation;
''')
replace('plugins/heaven-control-plane/README.md',
'''- traversal-segment rejection before filesystem/repository requests reach the bridge;
''',
'''- traversal-segment rejection before filesystem/repository requests reach the bridge;
- explicit overwrite confirmation for existing-file rewrites, unique-only v1 text patching, and bridge-error propagation into structured control-plane failures;
''')

replace('plugins/heaven-control-plane/verify.py',
'''if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))


def main() -> int:
''',
'''if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from heaven_control_plane.protocol import PLUGIN_VERSION


def main() -> int:
''')
replace('plugins/heaven-control-plane/verify.py',
'''    if manifest.get("schema") != "heaven-control-plane/manifest/v1":
        raise SystemExit("invalid manifest schema")
''',
'''    if manifest.get("schema") != "heaven-control-plane/manifest/v1":
        raise SystemExit("invalid manifest schema")
    if manifest.get("version") != PLUGIN_VERSION:
        raise SystemExit("manifest version does not match protocol PLUGIN_VERSION")
''')

tests='plugins/heaven-control-plane/tests/test_vertical_slice.py'
replace(tests, 'from heaven_control_plane.protocol import SCHEMA_VERSION', 'from heaven_control_plane.protocol import PLUGIN_VERSION, SCHEMA_VERSION')
replace(tests,
'''        self.response = {"status": "completed", "data": {"ok": True}}
        self.error = None

    def request(self, action, params, timeout_seconds):
        self.calls.append((action, dict(params), timeout_seconds))
        if self.error:
            raise self.error
        return self.response
''',
'''        self.response = {"status": "completed", "data": {"ok": True}}
        self.responses = {}
        self.error = None

    def request(self, action, params, timeout_seconds):
        self.calls.append((action, dict(params), timeout_seconds))
        if self.error:
            raise self.error
        return self.responses.get(action, self.response)
''')
replace(tests,
'''    def test_health_maps_to_existing_bridge_primitive(self):
''',
'''    def test_manifest_version_matches_protocol_version(self):
        manifest = json.loads((PLUGIN_ROOT / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(manifest["version"], PLUGIN_VERSION)

    def test_health_maps_to_existing_bridge_primitive(self):
''')
replace(tests,
'''    def test_execution_maps_to_proc_run_and_bounds_timeout(self):
''',
'''    def test_bridge_error_is_not_wrapped_as_success(self):
        self.transport.response = {
            "status": "error",
            "exit_code": 1,
            "error": {"code": "PATH_OUTSIDE_ALLOWED_ROOTS", "message": "blocked"},
        }
        result = self.control.invoke("filesystem.read", {"path": r"C:\\repo\\x.txt"})
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "PATH_OUTSIDE_ALLOWED_ROOTS")

    def test_execution_maps_to_proc_run_and_bounds_timeout(self):
''')
replace(tests,
'''    def test_filesystem_patch_matches_bridge_contract(self):
''',
'''    def test_filesystem_write_refuses_existing_rewrite_without_overwrite_confirmation(self):
        self.transport.responses["fs_info"] = {"status": "completed", "data": {"exists": True}}
        denied = self.control.invoke(
            "filesystem.write",
            {"path": r"C:\\repo\\a.txt", "content": "two", "mode": "rewrite"},
        )
        self.assertFalse(denied["ok"])
        self.assertEqual(denied["error"]["code"], "OVERWRITE_CONFIRMATION_REQUIRED")
        self.assertEqual([call[0] for call in self.transport.calls], ["fs_info"])

        allowed = self.control.invoke(
            "filesystem.write",
            {"path": r"C:\\repo\\a.txt", "content": "two", "mode": "rewrite", "overwrite": True},
        )
        self.assertTrue(allowed["ok"])
        self.assertEqual(self.transport.calls[-1][0], "fs_write")

    def test_filesystem_patch_matches_bridge_contract(self):
''')
replace(tests,
'''        self.assertEqual(params["old_string"], "old")
        self.assertEqual(params["new_string"], "new")

    def test_cancel_maps_to_bridge_job_id(self):
''',
'''        self.assertEqual(params["old_string"], "old")
        self.assertEqual(params["new_string"], "new")
        self.assertFalse(params["replace_all"])

        before = len(self.transport.calls)
        bulk = self.control.invoke(
            "filesystem.patch",
            {"path": r"C:\\repo\\a.txt", "old_string": "x", "new_string": "y", "replace_all": True},
        )
        self.assertFalse(bulk["ok"])
        self.assertEqual(bulk["error"]["code"], "BULK_PATCH_REQUIRES_EXACT_PRIMITIVE")
        self.assertEqual(len(self.transport.calls), before)

    def test_cancel_maps_to_bridge_job_id(self):
''')
replace(tests,
'''        command = self.transport.calls[-1][1]["command"]
        self.assertIn("git fetch --quiet origin main", command)
        self.assertIn("origin/main", command)
        self.assertIn("exact=($local -eq $remote)", command)

    def test_transport_timeout_becomes_structured_timeout(self):
''',
'''        command = self.transport.calls[-1][1]["command"]
        self.assertIn("git ls-remote --exit-code origin refs/heads/main", command)
        self.assertNotIn("git fetch", command)
        self.assertIn("exact=($local -eq $remote)", command)

    def test_git_nonzero_exit_becomes_structured_error(self):
        self.transport.response = {"status": "completed", "exit_code": 128, "stderr": "fatal: not a git repository"}
        result = self.control.invoke("git.status", {"repo": r"C:\\repo"})
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "GIT_COMMAND_FAILED")

    def test_transport_timeout_becomes_structured_timeout(self):
''')

print('patched canonical Heaven control plane hardening')
