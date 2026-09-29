from __future__ import annotations

import sys
import unittest
from pathlib import Path

PLUGIN_ROOT = Path(__file__).resolve().parents[1]
if str(PLUGIN_ROOT) not in sys.path:
    sys.path.insert(0, str(PLUGIN_ROOT))

from heaven_control_plane.protocol import ControlPlaneError
from heaven_control_plane.verification import (
    choose_project_type,
    detect_project_types,
    plan_verification,
)


class VerificationPlanningTests(unittest.TestCase):
    def test_detects_python_node_and_dotnet_markers_without_reading_contents(self):
        detected = detect_project_types(
            {
                "items": [
                    {"type": "file", "path": r"C:\repo\pyproject.toml"},
                    {"type": "file", "path": r"C:\repo\package.json"},
                    {"type": "file", "path": r"C:\repo\src\Demo.csproj"},
                    {"type": "dir", "path": r"C:\repo\node_modules"},
                ]
            }
        )
        self.assertEqual(detected["project_types"], ["python", "node", "dotnet"])
        self.assertTrue(detected["ambiguous"])
        self.assertTrue(detected["supported"])
        self.assertEqual(detected["markers"]["python"], ["pyproject.toml"])
        self.assertEqual(detected["markers"]["node"], ["package.json"])
        self.assertEqual(detected["markers"]["dotnet"], ["demo.csproj"])

    def test_detection_requires_bridge_listing_shape(self):
        with self.assertRaises(ControlPlaneError) as ctx:
            detect_project_types({"items": "not-a-list"})
        self.assertEqual(ctx.exception.code, "INVALID_LISTING")

    def test_plan_uses_fixed_commands_only(self):
        node = plan_verification("node", "test")
        self.assertEqual(node.command, "npm test")
        self.assertEqual(node.timeout_seconds, 1800)

        dotnet = plan_verification("dotnet", "typecheck")
        self.assertEqual(dotnet.command, "dotnet build --nologo --no-restore")
        self.assertEqual(dotnet.timeout_seconds, 900)

    def test_plan_rejects_unknown_kind_and_project_type(self):
        with self.assertRaises(ControlPlaneError) as ctx:
            plan_verification("ruby", "test")
        self.assertEqual(ctx.exception.code, "UNSUPPORTED_PROJECT_TYPE")

        with self.assertRaises(ControlPlaneError) as ctx:
            plan_verification("python", "deploy")
        self.assertEqual(ctx.exception.code, "INVALID_VERIFICATION_KIND")

    def test_choose_project_type_requires_explicit_choice_when_ambiguous(self):
        detection = {"project_types": ["python", "node"]}
        with self.assertRaises(ControlPlaneError) as ctx:
            choose_project_type(detection)
        self.assertEqual(ctx.exception.code, "AMBIGUOUS_PROJECT_TYPE")
        self.assertEqual(choose_project_type(detection, "node"), "node")

    def test_choose_project_type_refuses_undetected_requested_type(self):
        with self.assertRaises(ControlPlaneError) as ctx:
            choose_project_type({"project_types": ["python"]}, "dotnet")
        self.assertEqual(ctx.exception.code, "PROJECT_TYPE_NOT_DETECTED")


if __name__ == "__main__":
    unittest.main()
