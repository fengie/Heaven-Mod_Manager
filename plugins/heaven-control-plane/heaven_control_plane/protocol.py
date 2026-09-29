from __future__ import annotations

import json
import uuid
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from typing import Any, Mapping

PLUGIN_VERSION = "0.2.0"
SCHEMA_VERSION = "heaven-control-plane/v1"
MAX_REQUEST_BYTES = 1_048_576
DEFAULT_MAX_OUTPUT_BYTES = 262_144


class ControlPlaneError(Exception):
    def __init__(self, code: str, message: str, details: Mapping[str, Any] | None = None):
        super().__init__(message)
        self.code = code
        self.message = message
        self.details = dict(details or {})

    def as_dict(self) -> dict[str, Any]:
        out: dict[str, Any] = {"code": self.code, "message": self.message}
        if self.details:
            out["details"] = self.details
        return out


@dataclass(frozen=True)
class CapabilitySpec:
    name: str
    version: int
    description: str
    permission: str
    timeout_seconds: int
    cancellable: bool
    max_output_bytes: int = DEFAULT_MAX_OUTPUT_BYTES
    destructive: bool = False

    def as_dict(self) -> dict[str, Any]:
        return asdict(self)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def request_id(value: str | None = None) -> str:
    if value:
        value = str(value).strip()
        if not value or len(value) > 128:
            raise ControlPlaneError("INVALID_REQUEST_ID", "request_id must be 1-128 characters")
        return value
    return uuid.uuid4().hex


def require_mapping(value: Any, field: str = "input") -> dict[str, Any]:
    if not isinstance(value, Mapping):
        raise ControlPlaneError("INVALID_INPUT", f"{field} must be an object")
    out = dict(value)
    validate_request_size(out)
    return out


def require_string(
    value: Any,
    field: str,
    *,
    allow_empty: bool = False,
    max_length: int = 32_768,
) -> str:
    if not isinstance(value, str):
        raise ControlPlaneError("INVALID_INPUT", f"{field} must be a string")
    if not allow_empty and not value.strip():
        raise ControlPlaneError("INVALID_INPUT", f"{field} must not be empty")
    if len(value) > max_length:
        raise ControlPlaneError("INPUT_TOO_LARGE", f"{field} exceeds {max_length} characters")
    return value


def bounded_int(
    value: Any,
    field: str,
    *,
    default: int,
    minimum: int,
    maximum: int,
) -> int:
    if value is None:
        return default
    if isinstance(value, bool):
        raise ControlPlaneError("INVALID_INPUT", f"{field} must be an integer")
    try:
        parsed = int(value)
    except (TypeError, ValueError) as exc:
        raise ControlPlaneError("INVALID_INPUT", f"{field} must be an integer") from exc
    if parsed < minimum or parsed > maximum:
        raise ControlPlaneError(
            "INVALID_INPUT",
            f"{field} must be between {minimum} and {maximum}",
        )
    return parsed


def validate_request_size(payload: Mapping[str, Any], *, maximum: int = MAX_REQUEST_BYTES) -> int:
    try:
        encoded = json.dumps(payload, ensure_ascii=False, separators=(",", ":"), default=str).encode("utf-8")
    except (TypeError, ValueError) as exc:
        raise ControlPlaneError("INVALID_INPUT", "input must be JSON-serializable") from exc
    size = len(encoded)
    if size > maximum:
        raise ControlPlaneError(
            "INPUT_TOO_LARGE",
            f"request payload exceeds {maximum} bytes",
            {"bytes": size, "maximum": maximum},
        )
    return size


def bound_output(payload: Any, maximum: int) -> tuple[Any, bool, int]:
    encoded = json.dumps(payload, ensure_ascii=False, separators=(",", ":"), default=str).encode("utf-8")
    size = len(encoded)
    if size <= maximum:
        return payload, False, size
    preview = encoded[: max(0, maximum - 512)].decode("utf-8", errors="replace")
    return {
        "truncated": True,
        "original_bytes": size,
        "maximum_bytes": maximum,
        "preview": preview,
        "pagination_hint": "Use the capability's offset/length or the bridge job_output_read primitive.",
    }, True, size


def ok_envelope(
    *,
    req_id: str,
    capability: CapabilitySpec,
    data: Any,
    started_at: str,
    finished_at: str,
    output_truncated: bool,
) -> dict[str, Any]:
    return {
        "schema": SCHEMA_VERSION,
        "plugin_version": PLUGIN_VERSION,
        "request_id": req_id,
        "capability": capability.name,
        "capability_version": capability.version,
        "ok": True,
        "started_at": started_at,
        "finished_at": finished_at,
        "output_truncated": output_truncated,
        "data": data,
    }


def error_envelope(
    *,
    req_id: str,
    capability_name: str,
    error: ControlPlaneError,
    started_at: str,
    finished_at: str,
) -> dict[str, Any]:
    return {
        "schema": SCHEMA_VERSION,
        "plugin_version": PLUGIN_VERSION,
        "request_id": req_id,
        "capability": capability_name,
        "ok": False,
        "started_at": started_at,
        "finished_at": finished_at,
        "error": error.as_dict(),
    }
