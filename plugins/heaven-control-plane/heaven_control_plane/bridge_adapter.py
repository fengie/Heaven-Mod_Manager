from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any

from .capabilities import CapabilitySpec
from .protocol import CapabilityRequest, ControlPlaneError

BRIDGE_PROTOCOL = "chatgpt-heaven-bridge-v2"


@dataclass(frozen=True)
class BridgeInvocation:
    job_id: str
    action: str
    params: dict[str, Any]
    ttl_seconds: int = 21_600
    priority: str = "normal"

    def as_job(self, created_at: str) -> dict[str, Any]:
        return {
            "id": self.job_id,
            "source": BRIDGE_PROTOCOL,
            "action": self.action,
            "params": dict(self.params),
            "created_at": created_at,
            "ttl_seconds": self.ttl_seconds,
            "priority": self.priority,
        }


class HeavenBridgeAdapter:
    """Compatibility layer only; the Heaven Bridge remains the implementation owner."""

    _GIT_STATUS = "git status --short --branch"
    _GIT_DIFF = "git diff --no-ext-diff --no-color"
    _GIT_VERIFY_REMOTE_MAIN = (
        "$ErrorActionPreference='Stop'; "
        "git fetch origin main --quiet; if($LASTEXITCODE -ne 0){ exit $LASTEXITCODE }; "
        "$local=(git rev-parse HEAD).Trim(); "
        "$remote=(git rev-parse origin/main).Trim(); "
        "$branch=(git branch --show-current).Trim(); "
        "[pscustomobject]@{branch=$branch;local_head=$local;remote_main=$remote;exact_equal=($local -eq $remote)} "
        "| ConvertTo-Json -Compress"
    )

    def prepare(self, spec: CapabilitySpec, request: CapabilityRequest, params: dict[str, Any]) -> BridgeInvocation:
        if spec.bridge_action is None:
            raise ControlPlaneError("LOCAL_CAPABILITY", f"{spec.name} is served locally and has no bridge action")
        action = spec.bridge_action
        prepared = dict(params)
        if spec.name.startswith("git."):
            command = {
                "git.status": self._GIT_STATUS,
                "git.diff": self._GIT_DIFF,
                "git.verify_remote_main": self._GIT_VERIFY_REMOTE_MAIN,
            }[spec.name]
            prepared = {
                "shell": "powershell",
                "command": command,
                "timeout_seconds": params["timeout_seconds"],
            }
            if "cwd" in params:
                prepared["cwd"] = params["cwd"]
        return BridgeInvocation(
            job_id=request.request_id,
            action=action,
            params=prepared,
            priority="highest" if spec.name in {"control.health", "control.cancel"} else "normal",
        )

    def normalize(self, capability: str, raw: Any) -> Any:
        if capability != "git.verify_remote_main":
            return raw
        if not isinstance(raw, dict):
            raise ControlPlaneError("INVALID_BRIDGE_RESULT", "bridge result must be an object")
        stdout = raw.get("stdout")
        if not isinstance(stdout, str):
            data = raw.get("data")
            if isinstance(data, dict):
                stdout = data.get("stdout")
        if not isinstance(stdout, str):
            raise ControlPlaneError("INVALID_BRIDGE_RESULT", "remote-main verification did not return stdout")
        lines = [line.strip() for line in stdout.splitlines() if line.strip()]
        if not lines:
            raise ControlPlaneError("INVALID_BRIDGE_RESULT", "remote-main verification returned empty stdout")
        try:
            parsed = json.loads(lines[-1])
        except json.JSONDecodeError as exc:
            raise ControlPlaneError("INVALID_BRIDGE_RESULT", "remote-main verification returned invalid JSON") from exc
        if not parsed.get("exact_equal"):
            raise ControlPlaneError(
                "REMOTE_MAIN_MISMATCH",
                "HEAD does not exactly equal origin/main",
                {
                    "branch": parsed.get("branch"),
                    "local_head": parsed.get("local_head"),
                    "remote_main": parsed.get("remote_main"),
                },
            )
        return parsed

