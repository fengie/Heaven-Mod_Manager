from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Iterable, Mapping

from .protocol import ControlPlaneError, require_string

VERIFICATION_KINDS = ("build", "test", "lint", "typecheck")
PROJECT_TYPES = ("python", "node", "dotnet")

_PROJECT_MARKERS: dict[str, tuple[str, ...]] = {
    "python": ("pyproject.toml", "setup.py", "requirements.txt"),
    "node": ("package.json",),
    "dotnet": (".sln", ".slnx", ".csproj", ".fsproj", ".vbproj"),
}

_COMMANDS: dict[str, dict[str, str]] = {
    "python": {
        "build": "python -m build",
        "test": "python -m unittest discover",
        "lint": "python -m compileall -q .",
        "typecheck": "python -m mypy .",
    },
    "node": {
        "build": "npm run build",
        "test": "npm test",
        "lint": "npm run lint",
        "typecheck": "npm run typecheck",
    },
    "dotnet": {
        "build": "dotnet build --nologo",
        "test": "dotnet test --nologo",
        "lint": "dotnet format --verify-no-changes --no-restore",
        "typecheck": "dotnet build --nologo --no-restore",
    },
}

_TIMEOUTS = {
    "build": 1800,
    "test": 1800,
    "lint": 900,
    "typecheck": 900,
}


@dataclass(frozen=True)
class VerificationPlan:
    project_type: str
    kind: str
    command: str
    timeout_seconds: int

    def as_dict(self) -> dict[str, Any]:
        return {
            "project_type": self.project_type,
            "kind": self.kind,
            "command": self.command,
            "timeout_seconds": self.timeout_seconds,
        }


def _basename(value: Any) -> str:
    text = str(value or "").replace("\\", "/").rstrip("/")
    return text.rsplit("/", 1)[-1].lower()


def detect_project_types(listing: Mapping[str, Any] | Iterable[Any]) -> dict[str, Any]:
    """Detect supported project families from a bounded directory listing."""

    if isinstance(listing, Mapping):
        raw_items = listing.get("items")
        if not isinstance(raw_items, list):
            raise ControlPlaneError("INVALID_LISTING", "filesystem listing must contain an items array")
        items = raw_items
    else:
        items = list(listing)

    names: list[str] = []
    for item in items[:5000]:
        if isinstance(item, Mapping):
            if str(item.get("type") or "").lower() == "dir":
                continue
            path = item.get("path")
        else:
            path = item
        name = _basename(path)
        if name:
            names.append(name)

    detected: list[str] = []
    markers: dict[str, list[str]] = {}
    unique_names = sorted(set(names))
    for project_type in PROJECT_TYPES:
        hits: list[str] = []
        for name in unique_names:
            for marker in _PROJECT_MARKERS[project_type]:
                if marker.startswith("."):
                    if name.endswith(marker):
                        hits.append(name)
                elif name == marker:
                    hits.append(name)
        if hits:
            detected.append(project_type)
            markers[project_type] = sorted(set(hits))

    return {
        "project_types": detected,
        "markers": markers,
        "ambiguous": len(detected) > 1,
        "supported": bool(detected),
    }


def plan_verification(project_type: Any, kind: Any) -> VerificationPlan:
    project_type = require_string(project_type, "project_type", max_length=32).lower()
    kind = require_string(kind, "kind", max_length=32).lower()
    if project_type not in PROJECT_TYPES:
        raise ControlPlaneError(
            "UNSUPPORTED_PROJECT_TYPE",
            f"project_type must be one of {list(PROJECT_TYPES)}",
        )
    if kind not in VERIFICATION_KINDS:
        raise ControlPlaneError(
            "INVALID_VERIFICATION_KIND",
            f"kind must be one of {list(VERIFICATION_KINDS)}",
        )
    return VerificationPlan(
        project_type=project_type,
        kind=kind,
        command=_COMMANDS[project_type][kind],
        timeout_seconds=_TIMEOUTS[kind],
    )


def choose_project_type(detection: Mapping[str, Any], requested: Any = None) -> str:
    detected = detection.get("project_types")
    if not isinstance(detected, list):
        raise ControlPlaneError("INVALID_DETECTION", "project detection result is malformed")

    if requested is not None:
        requested_type = require_string(requested, "project_type", max_length=32).lower()
        if requested_type not in PROJECT_TYPES:
            raise ControlPlaneError(
                "UNSUPPORTED_PROJECT_TYPE",
                f"project_type must be one of {list(PROJECT_TYPES)}",
            )
        if requested_type not in detected:
            raise ControlPlaneError(
                "PROJECT_TYPE_NOT_DETECTED",
                f"{requested_type} markers were not detected at the repository root",
                {"detected": list(detected)},
            )
        return requested_type

    if not detected:
        raise ControlPlaneError("PROJECT_TYPE_NOT_DETECTED", "no supported project markers were detected")
    if len(detected) > 1:
        raise ControlPlaneError(
            "AMBIGUOUS_PROJECT_TYPE",
            "multiple project types were detected; project_type is required",
            {"detected": list(detected)},
        )
    return str(detected[0])
