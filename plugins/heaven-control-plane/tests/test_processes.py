from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path

PLUGIN_ROOT = Path(__file__).resolve().parents[1]
if str(PLUGIN_ROOT) not in sys.path:
    sys.path.insert(0, str(PLUGIN_ROOT))

from heaven_control_plane.processes import (
    normalize_process_list,
    process_list_params,
    process_wait_plan,
)
from heaven_control_plane.protocol import ControlPlaneError


class ProcessContractsTests(unittest.TestCase):
    def test_process_list_params_are_bounded(self):
        self.assertEqual(process_list_params(), {"limit": 200})
        self.assertEqual(process_list_params(500), {"limit": 500})
        with self.assertRaises(ControlPlaneError):
            process_list_params(501)

    def test_normalize_process_list_handles_singleton_json_object(self):
        response = {
            "status": "completed",
            "exit_code": 0,
            "stdout": json.dumps(
                {
                    "Id": 42,
                    "ProcessName": "demo",
                    "CPU": 1.25,
                    "WorkingSet64": 4096,
                    "Path": r"C:\demo.exe",
                }
            ),
        }
        result = normalize_process_list(response, limit=10)
        self.assertEqual(result["count"], 1)
        self.assertEqual(
            result["items"][0],
            {
                "pid": 42,
                "name": "demo",
                "cpu_seconds": 1.25,
                "working_set_bytes": 4096,
                "path": r"C:\demo.exe",
            },
        )

    def test_normalize_process_list_enforces_requested_limit(self):
        rows = [
            {"Id": index + 1, "ProcessName": f"p{index}", "CPU": None, "WorkingSet64": None, "Path": None}
            for index in range(5)
        ]
        result = normalize_process_list(
            {"status": "completed", "exit_code": 0, "stdout": json.dumps(rows)},
            limit=2,
        )
        self.assertEqual(result["count"], 2)
        self.assertTrue(result["truncated"])

    def test_normalize_process_list_rejects_bad_json_and_nonzero_exit(self):
        with self.assertRaises(ControlPlaneError) as ctx:
            normalize_process_list({"exit_code": 0, "stdout": "not-json"})
        self.assertEqual(ctx.exception.code, "INVALID_BRIDGE_RESULT")

        with self.assertRaises(ControlPlaneError) as ctx:
            normalize_process_list({"exit_code": 5, "stdout": "", "stderr": "blocked"})
        self.assertEqual(ctx.exception.code, "PROCESS_LIST_FAILED")
        self.assertEqual(ctx.exception.details["exit_code"], 5)

    def test_process_wait_plan_is_bounded_and_structured(self):
        plan = process_wait_plan(
            {
                "condition": "process_missing",
                "pid": 123,
                "timeout_seconds": 45,
                "interval_ms": 100,
                "soft_timeout": True,
            }
        )
        self.assertEqual(
            plan.as_dict(),
            {
                "condition": "process_missing",
                "pid": 123,
                "timeout_seconds": 45,
                "interval_ms": 100,
                "soft_timeout": True,
            },
        )

    def test_process_wait_plan_rejects_invalid_inputs(self):
        bad_payloads = [
            {"condition": "file_exists", "pid": 1},
            {"condition": "process_exists", "pid": 0},
            {"condition": "process_exists", "pid": 1, "timeout_seconds": 301},
            {"condition": "process_exists", "pid": 1, "interval_ms": 49},
            {"condition": "process_exists", "pid": 1, "soft_timeout": "yes"},
        ]
        for payload in bad_payloads:
            with self.subTest(payload=payload):
                with self.assertRaises(ControlPlaneError):
                    process_wait_plan(payload)


if __name__ == "__main__":
    unittest.main()
