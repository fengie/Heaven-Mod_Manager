from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

PLUGIN_ROOT = Path(__file__).resolve().parents[1]
if str(PLUGIN_ROOT) not in sys.path:
    sys.path.insert(0, str(PLUGIN_ROOT))

from heaven_control_plane.adapters.heaven_bridge import HeavenBridgeAdapter
from heaven_control_plane.indexing import IndexCapabilityProvider, RepositoryIndex
from heaven_control_plane.protocol import PLUGIN_VERSION
from heaven_control_plane.service import HeavenControlPlane


class FakeTransport:
    def __init__(self):
        self.calls = []

    def request(self, action, params, timeout_seconds):
        self.calls.append((action, dict(params), timeout_seconds))
        return {"status": "completed", "data": {"ok": True}}


class IndexServiceIntegrationTests(unittest.TestCase):
    def make_provider(self):
        temp = tempfile.TemporaryDirectory()
        root = Path(temp.name)
        (root / "src").mkdir()
        (root / "src" / "demo.py").write_text(
            "class Demo:\n"
            "    pass\n\n"
            "def helper():\n"
            "    return 'needle'\n",
            encoding="utf-8",
        )
        return temp, IndexCapabilityProvider(RepositoryIndex(root))

    def test_discovery_only_exposes_indexing_when_provider_is_configured(self):
        transport = FakeTransport()
        plain = HeavenControlPlane(HeavenBridgeAdapter(transport))
        plain_names = {
            item["name"]
            for item in plain.invoke("control.discovery")["data"]["capabilities"]
        }
        self.assertIn("verification.detect", plain_names)
        self.assertIn("verification.run", plain_names)
        self.assertFalse(any(name.startswith("index.") for name in plain_names))

        temp, provider = self.make_provider()
        try:
            configured = HeavenControlPlane(
                HeavenBridgeAdapter(transport),
                index_provider=provider,
            )
            names = {
                item["name"]
                for item in configured.invoke("control.discovery")["data"]["capabilities"]
            }
            self.assertTrue(
                {
                    "index.refresh",
                    "index.stats",
                    "index.search.text",
                    "index.search.symbols",
                }.issubset(names)
            )
            self.assertIn("verification.run", names)
        finally:
            temp.cleanup()

    def test_indexing_dispatch_is_local_bounded_and_permissioned(self):
        transport = FakeTransport()
        temp, provider = self.make_provider()
        try:
            control = HeavenControlPlane(
                HeavenBridgeAdapter(transport),
                index_provider=provider,
                granted_permissions={"repository.read", "control.read"},
            )
            refreshed = control.invoke("index.refresh", {})
            self.assertTrue(refreshed["ok"])
            self.assertEqual(refreshed["data"]["indexed_files"], 1)

            searched = control.invoke(
                "index.search.text",
                {"query": "needle", "length": 10, "context_lines": 1},
            )
            self.assertTrue(searched["ok"])
            self.assertEqual(searched["data"]["items"][0]["path"], "src/demo.py")
            self.assertEqual(transport.calls, [])

            denied = control.invoke(
                "index.search.symbols",
                {"query": "Demo"},
                permissions={"control.read"},
            )
            self.assertFalse(denied["ok"])
            self.assertEqual(denied["error"]["code"], "PERMISSION_DENIED")
        finally:
            temp.cleanup()

    def test_manifest_advertises_supported_optional_indexing_surface(self):
        manifest = json.loads((PLUGIN_ROOT / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(manifest["version"], PLUGIN_VERSION)
        self.assertEqual(manifest["optional_providers"]["indexing"], "IndexCapabilityProvider")
        self.assertTrue(
            {
                "verification.detect",
                "verification.run",
                "index.refresh",
                "index.stats",
                "index.search.text",
                "index.search.symbols",
            }.issubset(set(manifest["capabilities"]))
        )


if __name__ == "__main__":
    unittest.main()
