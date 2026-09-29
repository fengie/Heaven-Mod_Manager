from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Any, Callable, Mapping

from .protocol import ControlPlaneError, PROTOCOL_VERSION

_SECRET_KEY_RE = re.compile(
    r"(?:^|_)(?:password|passwd|token|secret|api[_-]?key|private[_-]?key|cookie|recovery[_-]?code)(?:$|_)",
    re.IGNORECASE,
)
_MAX_TEXT_WRITE = 1_048_576
_MAX_PATCH_TEXT = 262_144
_MAX_COMMAND = 131_072
_MAX_PATH = 4096
_MAX_ENV_ITEMS = 128

Validator = Callable[[Mapping[str, Any]], dict[str, Any]]


@dataclass(frozen=True)
class CapabilitySpec:
    name: str
    description: str
    bridge_action: str | None
    validator: Validator
    required_grants: tuple[str, ...]
    timeout_max_seconds: int | None = None
    cancellable: bool = False
    destructive: bool = False
    pagination: Mapping[str, Any] | None = None
    version: str = PROTOCOL_VERSION

    def public(self) -> dict[str, Any]:
        return {
            "name": self.name,
            "version": self.version,
            "description": self.description,
            "bridge_action": self.bridge_action,
            "required_grants": list(self.required_grants),
            "timeout_max_seconds": self.timeout_max_seconds,
            "cancellable": self.cancellable,
            "destructive": self.destructive,
            "pagination": dict(self.pagination) if self.pagination else None,
        }


def _reject_unknown(params: Mapping[str, Any], allowed: set[str]) -> None:
    unknown = sorted(set(params) - allowed)
    if unknown:
        raise ControlPlaneError("UNKNOWN_PARAMETER", "request contains unsupported parameters", {"parameters": unknown})


def _path(value: Any, field: str = "path") -> str:
    if not isinstance(value, str) or not value.strip():
        raise ControlPlaneError("INVALID_PATH", f"{field} must be a non-empty string")
    if "\x00" in value or len(value) > _MAX_PATH:
        raise ControlPlaneError("INVALID_PATH", f"{field} is invalid or too long")
    return value


def _int(value: Any, field: str, minimum: int, maximum: int) -> int:
    try:
        number = int(value)
    except (TypeError, ValueError):
        raise ControlPlaneError("INVALID_PARAMETER", f"{field} must be an integer") from None
    if not minimum <= number <= maximum:
        raise ControlPlaneError(
            "INVALID_PARAMETER",
            f"{field} must be between {minimum} and {maximum}",
            {"field": field, "minimum": minimum, "maximum": maximum},
        )
    return number


def _no_params(params: Mapping[str, Any]) -> dict[str, Any]:
    _reject_unknown(params, set())
    return {}


def _health(params: Mapping[str, Any]) -> dict[str, Any]:
    _reject_unknown(params, set())
    return {}


def _cancel(params: Mapping[str, Any]) -> dict[str, Any]:
    _reject_unknown(params, {"job_id"})
    job_id = params.get("job_id")
    if not isinstance(job_id, str) or not job_id:
        raise ControlPlaneError("INVALID_JOB_ID", "job_id must be a non-empty string")
    return {"job_id": job_id}


def _execution(params: Mapping[str, Any]) -> dict[str, Any]:
    allowed = {"shell", "command", "cwd", "timeout_seconds", "env", "env_from_host"}
    _reject_unknown(params, allowed)
    command = params.get("command")
    if not isinstance(command, str) or not command.strip():
        raise ControlPlaneError("MISSING_COMMAND", "command must be a non-empty string")
    if len(command.encode("utf-8")) > _MAX_COMMAND:
        raise ControlPlaneError("INPUT_TOO_LARGE", "command exceeds the structured execution limit")
    shell = str(params.get("shell") or "powershell").lower()
    if shell not in {"powershell", "cmd", "python"}:
        raise ControlPlaneError("INVALID_SHELL", "shell must be powershell, cmd, or python")
    out: dict[str, Any] = {
        "shell": shell,
        "command": command,
        "timeout_seconds": _int(params.get("timeout_seconds", 1800), "timeout_seconds", 1, 3600),
    }
    if params.get("cwd") is not None:
        out["cwd"] = _path(params["cwd"], "cwd")
    env = params.get("env")
    if env is not None:
        if not isinstance(env, dict) or len(env) > _MAX_ENV_ITEMS:
            raise ControlPlaneError("INVALID_ENV", "env must be an object with at most 128 entries")
        clean_env: dict[str, str] = {}
        for key, value in env.items():
            if not isinstance(key, str) or not key or _SECRET_KEY_RE.search(key):
                raise ControlPlaneError(
                    "SECRET_INLINE_ENV_BLOCKED",
                    "secret-like inline environment keys are not allowed; use env_from_host",
                    {"key": str(key)},
                )
            if not isinstance(value, (str, int, float, bool)):
                raise ControlPlaneError("INVALID_ENV", "inline environment values must be scalar")
            clean_env[key] = str(value)
        out["env"] = clean_env
    inherited = params.get("env_from_host")
    if inherited is not None:
        if not isinstance(inherited, list) or len(inherited) > _MAX_ENV_ITEMS:
            raise ControlPlaneError("INVALID_ENV", "env_from_host must be a list with at most 128 names")
        if any(not isinstance(name, str) or not name for name in inherited):
            raise ControlPlaneError("INVALID_ENV", "env_from_host entries must be non-empty strings")
        out["env_from_host"] = list(inherited)
    return out


def _fs_read(params: Mapping[str, Any]) -> dict[str, Any]:
    _reject_unknown(params, {"path", "offset", "length"})
    return {
        "path": _path(params.get("path")),
        "offset": _int(params.get("offset", 0), "offset", 0, 2_000_000_000),
        "length": _int(params.get("length", 1000), "length", 1, 5000),
    }


def _fs_write(params: Mapping[str, Any]) -> dict[str, Any]:
    _reject_unknown(params, {"path", "content", "mode"})
    path = _path(params.get("path"))
    content = params.get("content", "")
    if not isinstance(content, str):
        raise ControlPlaneError("INVALID_CONTENT", "content must be a string")
    if len(content.encode("utf-8")) > _MAX_TEXT_WRITE:
        raise ControlPlaneError("INPUT_TOO_LARGE", "content exceeds the 1 MiB structured write limit")
    mode = str(params.get("mode") or "rewrite").lower()
    if mode not in {"rewrite", "append"}:
        raise ControlPlaneError("INVALID_MODE", "mode must be rewrite or append")
    return {"path": path, "content": content, "mode": mode}


def _fs_patch(params: Mapping[str, Any]) -> dict[str, Any]:
    _reject_unknown(params, {"path", "old_string", "new_string", "replace_all"})
    path = _path(params.get("path"))
    old = params.get("old_string")
    new = params.get("new_string", "")
    if not isinstance(old, str) or not old:
        raise ControlPlaneError("INVALID_PATCH", "old_string must be a non-empty string")
    if not isinstance(new, str):
        raise ControlPlaneError("INVALID_PATCH", "new_string must be a string")
    if len(old.encode("utf-8")) > _MAX_PATCH_TEXT or len(new.encode("utf-8")) > _MAX_PATCH_TEXT:
        raise ControlPlaneError("INPUT_TOO_LARGE", "patch text exceeds the 256 KiB per-field limit")
    return {"path": path, "old_string": old, "new_string": new, "replace_all": bool(params.get("replace_all", False))}


def _git(params: Mapping[str, Any]) -> dict[str, Any]:
    _reject_unknown(params, {"cwd", "timeout_seconds"})
    out = {"timeout_seconds": _int(params.get("timeout_seconds", 120), "timeout_seconds", 1, 600)}
    if params.get("cwd") is not None:
        out["cwd"] = _path(params["cwd"], "cwd")
    return out


def _artifact_read(params: Mapping[str, Any]) -> dict[str, Any]:
    _reject_unknown(params, {"job_id", "stream", "offset", "length"})
    job_id = params.get("job_id")
    if not isinstance(job_id, str) or not job_id:
        raise ControlPlaneError("INVALID_JOB_ID", "job_id must be a non-empty string")
    stream = str(params.get("stream") or "stdout")
    if stream not in {"stdout", "stderr"}:
        raise ControlPlaneError("INVALID_STREAM", "stream must be stdout or stderr")
    return {
        "job_id": job_id,
        "stream": stream,
        "offset": _int(params.get("offset", 0), "offset", 0, 2_000_000_000),
        "length": _int(params.get("length", 200_000), "length", 1, 500_000),
    }


CAPABILITIES: dict[str, CapabilitySpec] = {
    "control.discovery": CapabilitySpec(
        "control.discovery", "Discover stable control-plane capabilities and metadata.", None, _no_params, ("control.read",)
    ),
    "control.health": CapabilitySpec(
        "control.health", "Read Heaven Bridge worker health and compatibility metadata.", "health", _health, ("control.read",)
    ),
    "control.cancel": CapabilitySpec(
        "control.cancel", "Request cancellation of a bridge job by stable request/job id.", "cancel", _cancel, ("jobs.cancel",), destructive=True
    ),
    "execution.run": CapabilitySpec(
        "execution.run", "Run a bounded command through the proven Heaven Bridge proc_run primitive.", "proc_run", _execution,
        ("execution.run",), timeout_max_seconds=3600, cancellable=True,
        pagination={"capability": "artifacts.read", "streams": ["stdout", "stderr"]},
    ),
    "filesystem.read": CapabilitySpec(
        "filesystem.read", "Read bounded UTF-8 text through the bridge allowlisted filesystem primitive.", "fs_read", _fs_read,
        ("filesystem.read",), pagination={"offset": "line", "max_length": 5000},
    ),
    "filesystem.write": CapabilitySpec(
        "filesystem.write", "Write or append bounded UTF-8 text through the bridge filesystem primitive.", "fs_write", _fs_write,
        ("filesystem.write",), destructive=True,
    ),
    "filesystem.patch": CapabilitySpec(
        "filesystem.patch", "Apply exact text replacement through the bridge fs_edit primitive.", "fs_edit", _fs_patch,
        ("filesystem.write",), destructive=True,
    ),
    "git.status": CapabilitySpec(
        "git.status", "Read repository status using a fixed, non-interpolated Git command.", "proc_run", _git, ("git.read",),
        timeout_max_seconds=600, cancellable=True,
    ),
    "git.diff": CapabilitySpec(
        "git.diff", "Read the current worktree diff using a fixed, non-interpolated Git command.", "proc_run", _git, ("git.read",),
        timeout_max_seconds=600, cancellable=True,
        pagination={"capability": "artifacts.read", "streams": ["stdout", "stderr"]},
    ),
    "git.verify_remote_main": CapabilitySpec(
        "git.verify_remote_main", "Fetch origin/main and prove whether HEAD exactly equals canonical remote main.", "proc_run", _git,
        ("git.read", "network.github"), timeout_max_seconds=600, cancellable=True,
    ),
    "artifacts.read": CapabilitySpec(
        "artifacts.read", "Page persisted stdout/stderr produced by an earlier bridge execution job.", "job_output_read", _artifact_read,
        ("artifacts.read",), pagination={"offset": "byte", "max_length": 500000},
    ),
}


def get_capability(name: str) -> CapabilitySpec:
    spec = CAPABILITIES.get(name)
    if spec is None:
        raise ControlPlaneError("UNKNOWN_CAPABILITY", f"unknown capability: {name}")
    return spec

