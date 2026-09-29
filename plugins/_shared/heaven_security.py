from __future__ import annotations

import re
import time
from dataclasses import dataclass
from typing import Any, Iterable, Mapping, Protocol


_HANDLE_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:/-]{0,255}$")
_TRANSPORT_HANDLE_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_.:-]{0,127}$")
_PERMISSION_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._*-]{0,127}$")


class SecretHandleError(Exception):
    """Safe secret-handle failure that never includes secret material."""

    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = str(code)
        self.message = str(message)


@dataclass(frozen=True)
class SecretBinding:
    """Ephemeral non-secret binding reference suitable for execution transport."""

    transport_handle: str
    purpose: str
    expires_at: float

    def as_dict(self) -> dict[str, Any]:
        return {
            "binding": "opaque",
            "purpose": self.purpose,
            "expires_at": self.expires_at,
        }

    def __repr__(self) -> str:
        return (
            "SecretBinding(transport_handle=<opaque>, "
            f"purpose={self.purpose!r}, expires_at={self.expires_at!r})"
        )


class SecretBackend(Protocol):
    def resolve_reference(
        self,
        handle: str,
        purpose: str,
        ttl_seconds: int,
        *,
        now: float,
    ) -> SecretBinding:
        ...


class SecretHandleDirectory:
    """In-memory directory of opaque secret handles to host-owned references.

    This is not a vault: entries contain only non-secret transport references.
    Actual credential values remain in the OS/user-authorized backing store.
    """

    def __init__(self):
        self._entries: dict[str, dict[str, Any]] = {}

    @staticmethod
    def _handle(value: Any) -> str:
        handle = str(value or "").strip()
        if not _HANDLE_RE.fullmatch(handle):
            raise ValueError("invalid opaque secret handle")
        return handle

    @staticmethod
    def _transport_handle(value: Any) -> str:
        handle = str(value or "").strip()
        if not _TRANSPORT_HANDLE_RE.fullmatch(handle):
            raise ValueError("invalid host transport handle")
        return handle

    @staticmethod
    def _purposes(values: Iterable[str]) -> frozenset[str]:
        if isinstance(values, (str, bytes)):
            raise ValueError("purposes must be an iterable")
        out = frozenset(str(value or "").strip() for value in values if str(value or "").strip())
        if not out or len(out) > 64 or any(len(value) > 128 for value in out):
            raise ValueError("invalid purpose set")
        return out

    def register(
        self,
        handle: str,
        transport_handle: str,
        *,
        purposes: Iterable[str],
        expires_at: float,
    ) -> None:
        opaque = self._handle(handle)
        transport = self._transport_handle(transport_handle)
        allowed = self._purposes(purposes)
        expiry = float(expires_at)
        if expiry <= time.time():
            raise ValueError("secret handle expiry must be in the future")
        self._entries[opaque] = {
            "transport_handle": transport,
            "purposes": allowed,
            "expires_at": expiry,
        }

    def revoke(self, handle: str) -> bool:
        return self._entries.pop(self._handle(handle), None) is not None

    def resolve_reference(
        self,
        handle: str,
        purpose: str,
        ttl_seconds: int,
        *,
        now: float,
    ) -> SecretBinding:
        opaque = self._handle(handle)
        entry = self._entries.get(opaque)
        if entry is None:
            raise SecretHandleError("SECRET_HANDLE_UNKNOWN", "secret handle is unknown or revoked")
        if float(entry["expires_at"]) <= now:
            raise SecretHandleError("SECRET_HANDLE_EXPIRED", "secret handle has expired")
        if purpose not in entry["purposes"]:
            raise SecretHandleError("SECRET_PURPOSE_DENIED", "secret handle is not authorized for this purpose")
        expires_at = min(float(entry["expires_at"]), now + ttl_seconds)
        return SecretBinding(
            transport_handle=str(entry["transport_handle"]),
            purpose=purpose,
            expires_at=expires_at,
        )


class SecretHandleResolver:
    def __init__(
        self,
        backend: SecretBackend,
        *,
        max_ttl_seconds: int = 900,
        now_fn=time.time,
    ):
        if not isinstance(max_ttl_seconds, int) or not 1 <= max_ttl_seconds <= 86_400:
            raise ValueError("max_ttl_seconds must be 1..86400")
        self.backend = backend
        self.max_ttl_seconds = max_ttl_seconds
        self.now_fn = now_fn

    def resolve_secret(
        self,
        handle: str,
        purpose: str,
        ttl_seconds: int,
    ) -> SecretBinding:
        opaque = str(handle or "").strip()
        scoped_purpose = str(purpose or "").strip()
        if not _HANDLE_RE.fullmatch(opaque):
            raise SecretHandleError("SECRET_HANDLE_INVALID", "secret handle is invalid")
        if not scoped_purpose or len(scoped_purpose) > 128:
            raise SecretHandleError("SECRET_PURPOSE_INVALID", "secret purpose is invalid")
        if not isinstance(ttl_seconds, int) or not 1 <= ttl_seconds <= self.max_ttl_seconds:
            raise SecretHandleError(
                "SECRET_TTL_INVALID",
                f"secret ttl must be 1..{self.max_ttl_seconds} seconds",
            )
        binding = self.backend.resolve_reference(
            opaque,
            scoped_purpose,
            ttl_seconds,
            now=float(self.now_fn()),
        )
        if not isinstance(binding, SecretBinding):
            raise SecretHandleError("SECRET_BACKEND_INVALID", "secret backend returned an invalid binding")
        if not _TRANSPORT_HANDLE_RE.fullmatch(binding.transport_handle):
            raise SecretHandleError("SECRET_BACKEND_INVALID", "secret backend returned an invalid transport reference")
        return binding


@dataclass(frozen=True)
class AuthorizationDecision:
    allowed: bool
    reason: str
    required_permission: str

    def as_dict(self) -> dict[str, Any]:
        return {
            "allowed": self.allowed,
            "reason": self.reason,
            "required_permission": self.required_permission,
        }


class PermissionBroker:
    """Fail-closed permission matcher for control-plane capabilities."""

    @staticmethod
    def _permission(value: Any, field: str) -> str:
        permission = str(value or "").strip()
        if not _PERMISSION_RE.fullmatch(permission):
            raise ValueError(f"{field} is invalid")
        return permission

    @staticmethod
    def _matches(grant: str, required: str) -> bool:
        if grant == "*" or grant == required:
            return True
        if grant.endswith(".*"):
            prefix = grant[:-1]
            return required.startswith(prefix)
        return False

    def authorize(
        self,
        *,
        capability: str,
        required_permission: str,
        resource: str | None,
        mutation: bool,
        granted_permissions: Iterable[str],
    ) -> AuthorizationDecision:
        capability_name = str(capability or "").strip()
        if not capability_name or len(capability_name) > 256:
            return AuthorizationDecision(False, "capability identifier is invalid", str(required_permission or ""))
        try:
            required = self._permission(required_permission, "required_permission")
        except ValueError:
            return AuthorizationDecision(False, "capability permission declaration is invalid", str(required_permission or ""))

        if isinstance(granted_permissions, (str, bytes)):
            return AuthorizationDecision(False, "granted permissions must be an iterable", required)

        grants: set[str] = set()
        try:
            for value in granted_permissions:
                grants.add(self._permission(value, "granted permission"))
        except ValueError:
            return AuthorizationDecision(False, "granted permission declaration is invalid", required)

        if resource is not None:
            resource_text = str(resource)
            if "\x00" in resource_text or len(resource_text) > 2048:
                return AuthorizationDecision(False, "resource identifier is invalid", required)

        if any(self._matches(grant, required) for grant in grants):
            reason = "explicit permission granted"
            if "*" in grants:
                reason = "trusted wildcard permission granted"
            return AuthorizationDecision(True, reason, required)

        action = "mutation" if mutation else "read"
        return AuthorizationDecision(
            False,
            f"{action} requires permission {required}",
            required,
        )


def validate_capability_permissions(
    manifest_permissions: Mapping[str, Any],
    registry_permissions: Mapping[str, Any],
) -> None:
    if not isinstance(manifest_permissions, Mapping):
        raise ValueError("manifest capability_permissions must be an object")
    manifest = {str(key): str(value) for key, value in manifest_permissions.items()}
    registry = {str(key): str(value) for key, value in registry_permissions.items()}
    if manifest != registry:
        missing = sorted(set(registry) - set(manifest))
        extra = sorted(set(manifest) - set(registry))
        mismatched = sorted(
            key
            for key in set(manifest) & set(registry)
            if manifest[key] != registry[key]
        )
        raise ValueError(
            "capability permission declarations mismatch "
            f"(missing={missing}, extra={extra}, mismatched={mismatched})"
        )
