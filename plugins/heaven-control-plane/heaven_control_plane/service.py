from __future__ import annotations

import time
from typing import Any, Iterable, Mapping

from .adapters.heaven_bridge import BRIDGE_PROTOCOL, HeavenBridgeAdapter
from .indexing import IndexCapabilityProvider
from .observability import ArtifactStore, AuditLog
from .protocol import (
    PLUGIN_VERSION,
    SCHEMA_VERSION,
    ControlPlaneError,
    bound_output,
    error_envelope,
    ok_envelope,
    request_id,
    require_mapping,
    utc_now,
    validate_request_size,
)
from .registry import CapabilityRegistry, build_registry


class HeavenControlPlane:
    def __init__(
        self,
        adapter: HeavenBridgeAdapter,
        *,
        registry: CapabilityRegistry | None = None,
        audit_log: AuditLog | None = None,
        artifacts: ArtifactStore | None = None,
        index_provider: IndexCapabilityProvider | None = None,
        granted_permissions: Iterable[str] | None = None,
    ):
        if isinstance(granted_permissions, str):
            raise ValueError("granted_permissions must be an iterable of permission names, not a string")
        self.adapter = adapter
        self.index_provider = index_provider
        self.registry = registry or build_registry(include_indexing=index_provider is not None)
        self.audit_log = audit_log or AuditLog()
        self.artifacts = artifacts or ArtifactStore()
        self.granted_permissions = (
            frozenset(str(value).strip() for value in granted_permissions if str(value).strip())
            if granted_permissions is not None
            else frozenset({"*"})
        )

    def invoke(
        self,
        capability_name: str,
        payload: Mapping[str, Any] | None = None,
        *,
        req_id: str | None = None,
        permissions: Iterable[str] | None = None,
    ) -> dict[str, Any]:
        started_at = utc_now()
        started_clock = time.monotonic()
        rid = "invalid-request"
        input_bytes = 0
        error_code: str | None = None
        truncated = False
        capability = None
        effective_permissions = self.granted_permissions

        try:
            rid = request_id(req_id)
            capability = self.registry.get(capability_name)
            if permissions is not None:
                if isinstance(permissions, str):
                    raise ControlPlaneError("INVALID_PERMISSIONS", "permissions must be an iterable of names")
                effective_permissions = frozenset(
                    str(value).strip() for value in permissions if str(value).strip()
                )
            if "*" not in effective_permissions and capability.permission not in effective_permissions:
                raise ControlPlaneError(
                    "PERMISSION_DENIED",
                    "capability permission was not granted",
                    {"required_permission": capability.permission},
                )
            data = require_mapping({} if payload is None else payload)
            input_bytes = validate_request_size(data)

            if capability.name == "control.discovery":
                raw: Any = {
                    "schema": SCHEMA_VERSION,
                    "plugin_version": PLUGIN_VERSION,
                    "bridge_protocol": BRIDGE_PROTOCOL,
                    "capabilities": self.registry.discover(),
                }
            elif capability.name == "observability.logs.page":
                raw = self.audit_log.page(offset=data.get("offset", 0), length=data.get("length", 100))
            elif capability.name == "observability.artifacts.page":
                raw = self.artifacts.page(
                    data.get("artifact_id"),
                    offset=data.get("offset", 0),
                    length=data.get("length", 200_000),
                )
            elif capability.name in IndexCapabilityProvider.CAPABILITIES:
                if self.index_provider is None:
                    raise ControlPlaneError(
                        "CAPABILITY_UNAVAILABLE",
                        "repository indexing provider is not configured",
                    )
                raw = self.index_provider.invoke(capability.name, data)
            else:
                raw = self.adapter.invoke(capability, data)

            bounded, truncated, _ = bound_output(raw, capability.max_output_bytes)
            finished_at = utc_now()
            return ok_envelope(
                req_id=rid,
                capability=capability,
                data=bounded,
                started_at=started_at,
                finished_at=finished_at,
                output_truncated=truncated,
            )
        except TimeoutError as exc:
            error = ControlPlaneError("TIMEOUT", "capability timed out", {"type": type(exc).__name__})
            error_code = error.code
            return error_envelope(
                req_id=rid,
                capability_name=capability_name,
                error=error,
                started_at=started_at,
                finished_at=utc_now(),
            )
        except ControlPlaneError as error:
            error_code = error.code
            return error_envelope(
                req_id=rid,
                capability_name=capability_name,
                error=error,
                started_at=started_at,
                finished_at=utc_now(),
            )
        except Exception as exc:
            error = ControlPlaneError(
                "INTERNAL_ERROR",
                "capability failed",
                {"type": type(exc).__name__},
            )
            error_code = error.code
            return error_envelope(
                req_id=rid,
                capability_name=capability_name,
                error=error,
                started_at=started_at,
                finished_at=utc_now(),
            )
        finally:
            self.audit_log.append(
                request_id=rid,
                capability=capability_name,
                capability_version=capability.version if capability else None,
                permission=capability.permission if capability else None,
                granted_permission_count=len(effective_permissions),
                status="error" if error_code else "ok",
                error_code=error_code,
                input_bytes=input_bytes,
                output_truncated=truncated,
                duration_ms=round((time.monotonic() - started_clock) * 1000, 3),
            )
