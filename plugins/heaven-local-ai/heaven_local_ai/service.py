from __future__ import annotations

import base64
import re
from typing import Any, Mapping, Protocol, Sequence


_MODEL_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$")


class ControlPlaneLike(Protocol):
    def invoke(
        self,
        capability_name: str,
        payload: Mapping[str, Any] | None = None,
        *,
        req_id: str | None = None,
        permissions: Sequence[str] | None = None,
    ) -> dict[str, Any]:
        ...


class HeavenLocalAiPlugin:
    """Safe Ollama CLI offload through the existing bounded control-plane execution primitive."""

    def __init__(self, control_plane: ControlPlaneLike):
        self.control_plane = control_plane

    @staticmethod
    def _model(value: Any) -> str:
        model = str(value or "").strip()
        if not _MODEL_RE.fullmatch(model):
            raise ValueError("invalid Ollama model name")
        return model

    @staticmethod
    def _prompt(value: Any) -> str:
        if not isinstance(value, str) or not value.strip():
            raise ValueError("prompt must be a non-empty string")
        if len(value) > 50_000:
            raise ValueError("prompt exceeds 50000 characters")
        return value

    @staticmethod
    def _ps_decode(value: str) -> str:
        encoded = base64.b64encode(value.encode("utf-8")).decode("ascii")
        return f"$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encoded}')); "

    def models(self) -> dict[str, Any]:
        return self.control_plane.invoke(
            "execution.run",
            {
                "shell": "powershell",
                "command": "$ErrorActionPreference='Stop'; & ollama list",
                "timeout_seconds": 30,
            },
        )

    def generate(
        self,
        model: str,
        prompt: str,
        *,
        timeout_seconds: int = 600,
    ) -> dict[str, Any]:
        model = self._model(model)
        prompt = self._prompt(prompt)
        if not isinstance(timeout_seconds, int) or not 1 <= timeout_seconds <= 1800:
            raise ValueError("timeout_seconds must be between 1 and 1800")
        command = (
            "$ErrorActionPreference='Stop'; "
            + self._ps_decode(prompt)
            + f"& ollama run '{model}' $p"
        )
        return self.control_plane.invoke(
            "execution.run",
            {
                "shell": "powershell",
                "command": command,
                "timeout_seconds": timeout_seconds,
            },
        )

    def summarize(
        self,
        model: str,
        text: str,
        *,
        instruction: str = "Summarize this for an engineering agent. Preserve exact errors, file paths, decisions, blockers, and next actions. Be concise.",
        timeout_seconds: int = 600,
    ) -> dict[str, Any]:
        text = self._prompt(text)
        instruction = self._prompt(instruction)
        combined = f"{instruction}\n\n--- INPUT ---\n{text}"
        return self.generate(model, combined, timeout_seconds=timeout_seconds)
