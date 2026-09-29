from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from heaven_control_plane.protocol import PLUGIN_VERSION
from heaven_control_plane.registry import build_registry
from heaven_control_plane.security import validate_capability_permissions


def main() -> int:
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("schema") != "heaven-control-plane/manifest/v1":
        raise SystemExit("invalid manifest schema")
    if manifest.get("version") != PLUGIN_VERSION:
        raise SystemExit("manifest version does not match protocol PLUGIN_VERSION")

    registry = build_registry(include_indexing=True)
    discovered = registry.discover()
    registry_permissions = {
        item["name"]: item["permission"]
        for item in discovered
    }
    capabilities = manifest.get("capabilities")
    if (
        not isinstance(capabilities, list)
        or len(capabilities) != len(set(capabilities))
        or set(capabilities) != set(registry_permissions)
    ):
        raise SystemExit("manifest capabilities do not exactly match registry")
    try:
        validate_capability_permissions(
            manifest.get("capability_permissions"),
            registry_permissions,
        )
    except ValueError as exc:
        raise SystemExit(str(exc)) from exc

    suite = unittest.defaultTestLoader.discover(
        str(ROOT / "tests"),
        pattern="test_*.py",
    )
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    raise SystemExit(main())
