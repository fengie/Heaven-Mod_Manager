import json
import sys
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PLUGINS = ROOT.parent
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(PLUGINS / "_shared"))

from heaven_security import SecretHandleDirectory, SecretHandleResolver
from heaven_control_plane.adapters.heaven_bridge import (
    CallableBridgeTransport,
    HeavenBridgeAdapter,
)
from heaven_control_plane.service import HeavenControlPlane


class RecordingTransport:
    def __init__(self):
        self.calls = []

    def request(self, action, params, timeout_seconds):
        self.calls.append((action, dict(params), timeout_seconds))
        return {"status": "ok", "data": {"accepted": True}}


class SecurityBrokerIntegrationTests(unittest.TestCase):
    def _control_plane(self, *, permissions, resolver=None):
        self.transport = RecordingTransport()
        adapter = HeavenBridgeAdapter(CallableBridgeTransport(self.transport.request))
        return HeavenControlPlane(
            adapter,
            granted_permissions=permissions,
            secret_resolver=resolver,
        )

    def test_secret_handle_resolves_to_host_binding_without_result_or_audit_leak(self):
        directory = SecretHandleDirectory()
        directory.register(
            "opaque:build-auth",
            "BUILD_AUTH_REF",
            purposes=["execution.run"],
            expires_at=time.time() + 120,
        )
        resolver = SecretHandleResolver(directory)
        cp = self._control_plane(
            permissions=["execution.run"],
            resolver=resolver,
        )

        result = cp.invoke(
            "execution.run",
            {
                "shell": "powershell",
                "command": "Write-Output ok",
                "secret_handles": [
                    {
                        "handle": "opaque:build-auth",
                        "purpose": "execution.run",
                        "ttl_seconds": 30,
                    }
                ],
            },
        )

        self.assertTrue(result["ok"])
        self.assertEqual(len(self.transport.calls), 1)
        action, params, _ = self.transport.calls[0]
        self.assertEqual(action, "proc_run")
        self.assertEqual(params["env_from_host"], ["BUILD_AUTH_REF"])
        serialized = json.dumps(result)
        self.assertNotIn("opaque:build-auth", serialized)
        audit = json.dumps(cp.audit_log.page(offset=0, length=10))
        self.assertNotIn("opaque:build-auth", audit)

    def test_denied_mutation_never_reaches_transport(self):
        cp = self._control_plane(permissions=["filesystem.read"])
        result = cp.invoke(
            "filesystem.write",
            {
                "path": r"C:\repo\blocked.txt",
                "content": "x",
                "overwrite": True,
            },
        )
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "PERMISSION_DENIED")
        self.assertEqual(self.transport.calls, [])

    def test_missing_secret_resolver_fails_closed_before_transport(self):
        cp = self._control_plane(permissions=["execution.run"])
        result = cp.invoke(
            "execution.run",
            {
                "command": "Write-Output ok",
                "secret_handles": [
                    {
                        "handle": "opaque:missing-resolver",
                        "purpose": "execution.run",
                    }
                ],
            },
        )
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "SECRET_RESOLVER_UNAVAILABLE")
        self.assertEqual(self.transport.calls, [])

    def test_secret_purpose_denial_is_safe_and_does_not_reach_transport(self):
        directory = SecretHandleDirectory()
        directory.register(
            "opaque:release-only",
            "RELEASE_ONLY_REF",
            purposes=["release.publish"],
            expires_at=time.time() + 120,
        )
        cp = self._control_plane(
            permissions=["execution.run"],
            resolver=SecretHandleResolver(directory),
        )
        result = cp.invoke(
            "execution.run",
            {
                "command": "Write-Output ok",
                "secret_handles": [
                    {
                        "handle": "opaque:release-only",
                        "purpose": "execution.run",
                    }
                ],
            },
        )
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"]["code"], "SECRET_PURPOSE_DENIED")
        self.assertNotIn("opaque:release-only", json.dumps(result))
        self.assertEqual(self.transport.calls, [])


if __name__ == "__main__":
    unittest.main()
