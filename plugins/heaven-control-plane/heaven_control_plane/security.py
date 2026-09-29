from __future__ import annotations

import sys
from pathlib import Path
from typing import Any, Mapping

from .protocol import ControlPlaneError, bounded_int, require_mapping, require_string

_SHARED_ROOT = Path(__file__).resolve().parents[2] / "_shared"
if str(_SHARED_ROOT) not in sys.path:
    sys.path.insert(0, str(_SHARED_ROOT))

from heaven_security import (  # noqa: E402
    PermissionBroker,
    SecretHandleError,
    SecretHandleResolver,
    validate_capability_permissions,
)


_SECRET_CAPABILITIES = {
    "execution.run",
    "execution.session.start",
}


def authorization_resource(payload: Mapping[str, Any]) -> str | None:
    for key in ("repo", "path", "cwd", "session_id", "job_id"):
        value = payload.get(key)
        if value is not None:
            text = str(value)
            if len(text) > 1900:
                text = text[:1900]
            return f"{key}:{text}"
    return None


def resolve_secret_bindings(
    payload: Mapping[str, Any],
    *,
    capability_name: str,
    resolver: SecretHandleResolver | None,
) -> dict[str, Any]:
    data = dict(payload)
    raw = data.get("secret_handles")
    if raw is None:
        return data
    if capability_name not in _SECRET_CAPABILITIES:
        raise ControlPlaneError(
            "SECRET_BINDING_TARGET_DENIED",
            "secret handles are not supported by this capability",
        )
    if resolver is None:
        raise ControlPlaneError(
            "SECRET_RESOLVER_UNAVAILABLE",
            "secret handle resolver is not configured",
        )
    if "env_from_host" in data:
        raise ControlPlaneError(
            "SECRET_BINDING_CONFLICT",
            "secret_handles cannot be combined with env_from_host",
        )
    if not isinstance(raw, list) or not 1 <= len(raw) <= 64:
        raise ControlPlaneError(
            "INVALID_INPUT",
            "secret_handles must be a list with 1..64 entries",
        )

    transport_handles: list[str] = []
    seen: set[str] = set()
    for item in raw:
        entry = require_mapping(item, "secret_handles item")
        handle = require_string(
            entry.get("handle"),
            "secret handle",
            max_length=256,
        )
        purpose = require_string(
            entry.get("purpose"),
            "secret purpose",
            max_length=128,
        )
        ttl = bounded_int(
            entry.get("ttl_seconds"),
            "secret ttl_seconds",
            default=300,
            minimum=1,
            maximum=86_400,
        )
        try:
            binding = resolver.resolve_secret(handle, purpose, ttl)
        except SecretHandleError as exc:
            raise ControlPlaneError(exc.code, exc.message) from exc
        if binding.transport_handle not in seen:
            seen.add(binding.transport_handle)
            transport_handles.append(binding.transport_handle)

    data.pop("secret_handles", None)
    data["env_from_host"] = transport_handles
    return data


__all__ = [
    "PermissionBroker",
    "SecretHandleResolver",
    "authorization_resource",
    "resolve_secret_bindings",
    "validate_capability_permissions",
]
