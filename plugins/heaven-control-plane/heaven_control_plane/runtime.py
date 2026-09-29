from __future__ import annotations

from datetime import datetime, timezone
from typing import Any, Callable, Mapping

from .bridge_adapter import BRIDGE_PROTOCOL, BridgeInvocation, HeavenBridgeAdapter
from .capabilities import CAPABILITIES, get_capability
from .protocol import (
    MAX_RESULT_BYTES,
    PROTOCOL_ID,
    PROTOCOL_VERSION,
    CapabilityRequest,
    ControlPlaneError,
    audit_param_summary,
    serialized_size,
)

Transport = Callable[[BridgeInvocation], Any]


class ControlPlane:
    def __init__(self, adapter: HeavenBridgeAdapter | None = None):
        self.adapter = adapter or HeavenBridgeAdapter()

    def discovery(self) -> dict[str, Any]:
        return {
            "protocol": PROTOCOL_ID,
            "version": PROTOCOL_VERSION,
            "bridge_protocol": BRIDGE_PROTOCOL,
            "capabilities": [CAPABILITIES[name].public() for name in sorted(CAPABILITIES)],
            "limits": {"result_bytes": MAX_RESULT_BYTES, "artifact_page_bytes": 500_000},
            "security": {
                "inline_secrets": False,
                "host_secret_injection": "env_from_host",
                "filesystem_enforcement": "delegated-to-heaven-bridge-allowlist",
                "raw_shell": "structured execution escape hatch only",
            },
        }

    def handle(self, value: Mapping[str, Any], transport: Transport | None = None) -> dict[str, Any]:
        started_at = self._now()
        try:
            request = CapabilityRequest.from_mapping(value)
            spec = get_capability(request.capability)
            if request.version != spec.version:
                raise ControlPlaneError(
                    "VERSION_MISMATCH",
                    f"{request.capability} requires version {spec.version}",
                    {"requested": request.version, "supported": spec.version},
                )
            params = spec.validator(request.params)
            audit = {
                "requested_at": started_at,
                "capability": request.capability,
                "capability_version": spec.version,
                "required_grants": list(spec.required_grants),
                "params": audit_param_summary(params),
            }
            if request.capability == "control.discovery":
                return self._success(request, self.discovery(), audit, started_at)
            if transport is None:
                raise ControlPlaneError("TRANSPORT_REQUIRED", "a bridge transport is required for this capability")
            invocation = self.adapter.prepare(spec, request, params)
            audit["bridge_action"] = invocation.action
            audit["bridge_job_id"] = invocation.job_id
            raw = transport(invocation)
            self._raise_bridge_status(raw)
            data = self.adapter.normalize(request.capability, raw)
            size = serialized_size(data)
            if size > MAX_RESULT_BYTES:
                details: dict[str, Any] = {"limit": MAX_RESULT_BYTES, "bytes": size}
                if isinstance(raw, dict) and isinstance(raw.get("output"), dict):
                    details["output"] = raw["output"]
                raise ControlPlaneError(
                    "OUTPUT_TOO_LARGE",
                    "bridge result exceeds the control-plane envelope; page persisted output with artifacts.read",
                    details,
                )
            return self._success(request, data, audit, started_at)
        except ControlPlaneError as exc:
            return self._failure(value, exc, started_at)
        except Exception as exc:
            return self._failure(
                value,
                ControlPlaneError("TRANSPORT_ERROR", "bridge transport failed", {"exception_type": type(exc).__name__}),
                started_at,
            )

    @staticmethod
    def _raise_bridge_status(raw: Any) -> None:
        if not isinstance(raw, dict):
            return
        status = str(raw.get("status") or "").lower()
        if status in {"timeout", "timed_out"}:
            raise ControlPlaneError("TIMEOUT", "bridge execution timed out")
        if status in {"cancelled", "canceled"}:
            raise ControlPlaneError("CANCELLED", "bridge execution was cancelled")
        if status in {"failed", "error"}:
            code = raw.get("error_code") or "BRIDGE_EXECUTION_FAILED"
            details = {}
            if "exit_code" in raw:
                details["exit_code"] = raw["exit_code"]
            if isinstance(raw.get("error"), dict):
                details["bridge_error"] = raw["error"]
                code = raw["error"].get("code") or code
            raise ControlPlaneError(str(code), "bridge capability execution failed", details)

    def _success(self, request: CapabilityRequest, data: Any, audit: dict[str, Any], started_at: str) -> dict[str, Any]:
        return {
            "protocol": PROTOCOL_ID,
            "version": PROTOCOL_VERSION,
            "request_id": request.request_id,
            "capability": request.capability,
            "ok": True,
            "data": data,
            "error": None,
            "audit": {**audit, "completed_at": self._now()},
        }

    def _failure(self, value: Mapping[str, Any], exc: ControlPlaneError, started_at: str) -> dict[str, Any]:
        request_id = value.get("request_id") if isinstance(value, Mapping) else None
        capability = value.get("capability") if isinstance(value, Mapping) else None
        return {
            "protocol": PROTOCOL_ID,
            "version": PROTOCOL_VERSION,
            "request_id": request_id if isinstance(request_id, str) else None,
            "capability": capability if isinstance(capability, str) else None,
            "ok": False,
            "data": None,
            "error": exc.as_dict(),
            "audit": {"requested_at": started_at, "completed_at": self._now()},
        }

    @staticmethod
    def _now() -> str:
        return datetime.now(timezone.utc).isoformat()

