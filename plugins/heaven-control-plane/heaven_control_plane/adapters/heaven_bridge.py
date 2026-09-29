from __future__ import annotations

import re
from typing import Any, Callable, Mapping, Protocol

from ..protocol import CapabilitySpec, ControlPlaneError, bounded_int, require_mapping, require_string

BRIDGE_PROTOCOL = "chatgpt-heaven-bridge-v2"
_ALLOWED_SHELLS = {"powershell", "cmd", "python"}
_SECRET_ENV_RE = re.compile(r"(pass(?:word)?|token|secret|api[_-]?key|private[_-]?key|cookie|auth)", re.I)


class BridgeTransport(Protocol):
    def request(self, action: str, params: Mapping[str, Any], timeout_seconds: int) -> Mapping[str, Any]:
        ...


class CallableBridgeTransport:
    def __init__(self, fn: Callable[[str, Mapping[str, Any], int], Mapping[str, Any]]):
        self._fn = fn

    def request(self, action: str, params: Mapping[str, Any], timeout_seconds: int) -> Mapping[str, Any]:
        return self._fn(action, params, timeout_seconds)


def _safe_path(value: Any, field: str = "path") -> str:
    path = require_string(value, field, max_length=2048)
    if "\x00" in path:
        raise ControlPlaneError("INVALID_PATH", f"{field} contains a NUL byte")
    normalized = path.replace("\\", "/")
    if any(part == ".." for part in normalized.split("/")):
        raise ControlPlaneError("PATH_TRAVERSAL", f"{field} must not contain '..' segments")
    return path


def _validated_env(payload: Mapping[str, Any]) -> tuple[dict[str, str] | None, list[str] | None]:
    raw_env = payload.get("env")
    env: dict[str, str] | None = None
    if raw_env is not None:
        if not isinstance(raw_env, Mapping) or len(raw_env) > 64:
            raise ControlPlaneError("INVALID_INPUT", "env must be an object with at most 64 entries")
        env = {}
        for key, value in raw_env.items():
            key = require_string(key, "env key", max_length=128)
            if _SECRET_ENV_RE.search(key):
                raise ControlPlaneError(
                    "SECRET_INLINE_ENV_BLOCKED",
                    "secret-like environment values must use env_from_host handles",
                    {"key": key},
                )
            env[key] = require_string(str(value), f"env.{key}", allow_empty=True, max_length=8192)

    raw_handles = payload.get("env_from_host")
    handles: list[str] | None = None
    if raw_handles is not None:
        if not isinstance(raw_handles, list) or len(raw_handles) > 64:
            raise ControlPlaneError("INVALID_INPUT", "env_from_host must be a list with at most 64 names")
        handles = [require_string(v, "env_from_host item", max_length=128) for v in raw_handles]
    return env, handles


class HeavenBridgeAdapter:
    """Compatibility adapter from stable control-plane names to existing bridge actions."""

    def __init__(self, transport: BridgeTransport):
        self.transport = transport

    def _request(self, action: str, params: Mapping[str, Any], timeout_seconds: int) -> Mapping[str, Any]:
        response = self.transport.request(action, params, timeout_seconds)
        if not isinstance(response, Mapping):
            raise ControlPlaneError("INVALID_BRIDGE_RESULT", "bridge result must be an object")
        status = str(response.get("status") or "").lower()
        raw_error = response.get("error")
        if status == "error" or raw_error:
            if isinstance(raw_error, Mapping):
                code = str(raw_error.get("code") or "BRIDGE_ERROR")
                message = str(raw_error.get("message") or "bridge request failed")
                details = raw_error.get("details") if isinstance(raw_error.get("details"), Mapping) else {}
            else:
                code = "BRIDGE_ERROR"
                message = str(response.get("stderr") or raw_error or "bridge request failed")
                details = {}
            raise ControlPlaneError(code, message, details)
        return response

    @staticmethod
    def _require_git_success(response: Mapping[str, Any]) -> Mapping[str, Any]:
        exit_code = response.get("exit_code")
        if exit_code is not None and int(exit_code) != 0:
            raise ControlPlaneError(
                "GIT_COMMAND_FAILED",
                "git command failed",
                {"exit_code": int(exit_code), "stderr": str(response.get("stderr") or "")[:2048]},
            )
        return response

    def invoke(self, capability: CapabilitySpec, payload: Mapping[str, Any]) -> Mapping[str, Any]:
        data = require_mapping(payload)

        if capability.name == "control.health":
            return self._request("health", {}, min(capability.timeout_seconds, 15))

        if capability.name == "control.cancel":
            job_id = require_string(data.get("job_id"), "job_id", max_length=200)
            return self._request("cancel", {"job_id": job_id}, min(capability.timeout_seconds, 15))

        if capability.name == "execution.run":
            shell = str(data.get("shell") or "powershell").lower()
            if shell not in _ALLOWED_SHELLS:
                raise ControlPlaneError("INVALID_SHELL", f"shell must be one of {sorted(_ALLOWED_SHELLS)}")
            command = require_string(data.get("command"), "command", max_length=32_768)
            timeout = bounded_int(
                data.get("timeout_seconds"),
                "timeout_seconds",
                default=capability.timeout_seconds,
                minimum=1,
                maximum=capability.timeout_seconds,
            )
            params: dict[str, Any] = {"shell": shell, "command": command, "timeout_seconds": timeout}
            if data.get("cwd") is not None:
                params["cwd"] = _safe_path(data.get("cwd"), "cwd")
            env, handles = _validated_env(data)
            if env is not None:
                params["env"] = env
            if handles is not None:
                params["env_from_host"] = handles
            return self._request("proc_run", params, timeout + 15)

        if capability.name == "filesystem.read":
            params = {
                "path": _safe_path(data.get("path")),
                "offset": bounded_int(data.get("offset"), "offset", default=0, minimum=0, maximum=50_000_000),
                "length": bounded_int(data.get("length"), "length", default=1000, minimum=1, maximum=5000),
            }
            return self._request("fs_read", params, capability.timeout_seconds)

        if capability.name == "filesystem.write":
            content = require_string(data.get("content"), "content", allow_empty=True, max_length=1_000_000)
            mode = str(data.get("mode") or "rewrite").lower()
            if mode not in {"rewrite", "append"}:
                raise ControlPlaneError("INVALID_MODE", "mode must be rewrite or append")
            path = _safe_path(data.get("path"))
            overwrite = data.get("overwrite", False)
            if not isinstance(overwrite, bool):
                raise ControlPlaneError("INVALID_INPUT", "overwrite must be a boolean")
            if mode == "rewrite" and not overwrite:
                info = self._request("fs_info", {"path": path}, capability.timeout_seconds)
                info_data = info.get("data") if isinstance(info.get("data"), Mapping) else {}
                if bool(info_data.get("exists")):
                    raise ControlPlaneError(
                        "OVERWRITE_CONFIRMATION_REQUIRED",
                        "path exists; set overwrite=true to replace it",
                        {"path": path},
                    )
            params = {"path": path, "content": content, "mode": mode}
            return self._request("fs_write", params, capability.timeout_seconds)

        if capability.name == "filesystem.patch":
            old = require_string(data.get("old_string"), "old_string", max_length=250_000)
            new = require_string(data.get("new_string"), "new_string", allow_empty=True, max_length=250_000)
            if bool(data.get("replace_all", False)):
                raise ControlPlaneError(
                    "BULK_PATCH_REQUIRES_EXACT_PRIMITIVE",
                    "filesystem.patch v1 only permits the bridge's unique single-replacement mode",
                )
            params = {
                "path": _safe_path(data.get("path")),
                "old_string": old,
                "new_string": new,
                "replace_all": False,
            }
            return self._request("fs_edit", params, capability.timeout_seconds)

        if capability.name in {"git.status", "git.diff", "git.verify_remote_main"}:
            cwd = _safe_path(data.get("repo"), "repo")
            if capability.name == "git.status":
                command = "git status --short --branch"
                timeout = 30
            elif capability.name == "git.diff":
                staged = bool(data.get("staged", False))
                command = "git diff --cached --no-ext-diff --unified=3" if staged else "git diff --no-ext-diff --unified=3"
                timeout = 60
            else:
                command = (
                    "$ErrorActionPreference='Stop'; "
                    "$local=(git rev-parse HEAD).Trim(); "
                    "$remoteLine=(git ls-remote --exit-code origin refs/heads/main | Select-Object -First 1); "
                    "if($LASTEXITCODE -ne 0 -or -not $remoteLine){ throw 'origin/main not found' }; "
                    "$remote=(($remoteLine -split '\\s+')[0]).Trim(); "
                    "$branch=(git branch --show-current).Trim(); "
                    "[pscustomobject]@{branch=$branch;local=$local;remote_main=$remote;exact=($local -eq $remote)} "
                    "| ConvertTo-Json -Compress"
                )
                timeout = 120
            response = self._request(
                "proc_run",
                {"shell": "powershell", "command": command, "cwd": cwd, "timeout_seconds": timeout},
                timeout + 15,
            )
            return self._require_git_success(response)

        raise ControlPlaneError("ADAPTER_UNSUPPORTED", f"no Heaven Bridge adapter for {capability.name}")
