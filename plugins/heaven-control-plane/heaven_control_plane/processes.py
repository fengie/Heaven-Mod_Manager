from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any, Mapping

from .protocol import ControlPlaneError, bounded_int, require_mapping

PROCESS_WAIT_CONDITIONS = ("process_exists", "process_missing")


@dataclass(frozen=True)
class ProcessWaitPlan:
    condition: str
    pid: int
    timeout_seconds: int
    interval_ms: int
    soft_timeout: bool

    def as_dict(self) -> dict[str, Any]:
        return {
            "condition": self.condition,
            "pid": self.pid,
            "timeout_seconds": self.timeout_seconds,
            "interval_ms": self.interval_ms,
            "soft_timeout": self.soft_timeout,
        }


def process_list_params(limit: Any = None) -> dict[str, int]:
    return {
        "limit": bounded_int(
            limit,
            "limit",
            default=200,
            minimum=1,
            maximum=500,
        )
    }


def process_wait_plan(payload: Mapping[str, Any]) -> ProcessWaitPlan:
    data = require_mapping(payload)
    condition = data.get("condition")
    if not isinstance(condition, str) or condition not in PROCESS_WAIT_CONDITIONS:
        raise ControlPlaneError(
            "INVALID_WAIT_CONDITION",
            "condition must be process_exists or process_missing",
        )
    pid = bounded_int(data.get("pid"), "pid", default=0, minimum=1, maximum=2_147_483_647)
    timeout_seconds = bounded_int(
        data.get("timeout_seconds"),
        "timeout_seconds",
        default=30,
        minimum=1,
        maximum=300,
    )
    interval_ms = bounded_int(
        data.get("interval_ms"),
        "interval_ms",
        default=250,
        minimum=50,
        maximum=5000,
    )
    soft_timeout = data.get("soft_timeout", False)
    if not isinstance(soft_timeout, bool):
        raise ControlPlaneError("INVALID_INPUT", "soft_timeout must be a boolean")
    return ProcessWaitPlan(condition, pid, timeout_seconds, interval_ms, soft_timeout)


def normalize_process_list(response: Mapping[str, Any], *, limit: Any = None) -> dict[str, Any]:
    data = require_mapping(response, "bridge response")
    requested_limit = process_list_params(limit)["limit"]
    exit_code = data.get("exit_code")
    if exit_code is not None:
        try:
            parsed_exit = int(exit_code)
        except (TypeError, ValueError) as exc:
            raise ControlPlaneError("INVALID_BRIDGE_RESULT", "process list exit_code is malformed") from exc
        if parsed_exit != 0:
            raise ControlPlaneError(
                "PROCESS_LIST_FAILED",
                "process listing failed",
                {"exit_code": parsed_exit, "stderr": str(data.get("stderr") or "")[-2048:]},
            )

    raw_stdout = data.get("stdout")
    if not isinstance(raw_stdout, str):
        raise ControlPlaneError("INVALID_BRIDGE_RESULT", "process list stdout must be a string")
    text = raw_stdout.strip()
    if not text:
        rows: list[Any] = []
    else:
        try:
            decoded = json.loads(text)
        except json.JSONDecodeError as exc:
            raise ControlPlaneError("INVALID_BRIDGE_RESULT", "process list stdout is not valid JSON") from exc
        rows = decoded if isinstance(decoded, list) else [decoded]

    normalized: list[dict[str, Any]] = []
    for row in rows[:requested_limit]:
        if not isinstance(row, Mapping):
            raise ControlPlaneError("INVALID_BRIDGE_RESULT", "process list item must be an object")
        try:
            pid = int(row.get("Id"))
        except (TypeError, ValueError) as exc:
            raise ControlPlaneError("INVALID_BRIDGE_RESULT", "process id is malformed") from exc
        if pid <= 0:
            raise ControlPlaneError("INVALID_BRIDGE_RESULT", "process id must be positive")

        cpu_raw = row.get("CPU")
        working_set_raw = row.get("WorkingSet64")
        try:
            cpu_seconds = None if cpu_raw is None else float(cpu_raw)
            working_set_bytes = None if working_set_raw is None else int(working_set_raw)
        except (TypeError, ValueError) as exc:
            raise ControlPlaneError("INVALID_BRIDGE_RESULT", "process metrics are malformed") from exc

        path = row.get("Path")
        normalized.append(
            {
                "pid": pid,
                "name": str(row.get("ProcessName") or ""),
                "cpu_seconds": cpu_seconds,
                "working_set_bytes": working_set_bytes,
                "path": None if path is None else str(path),
            }
        )

    return {
        "limit": requested_limit,
        "count": len(normalized),
        "truncated": len(rows) > requested_limit,
        "items": normalized,
    }
