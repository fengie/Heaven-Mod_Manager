from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable

from .protocol import CapabilitySpec, ControlPlaneError


@dataclass(frozen=True)
class CapabilityRegistry:
    _specs: dict[str, CapabilitySpec]

    @classmethod
    def from_specs(cls, specs: Iterable[CapabilitySpec]) -> "CapabilityRegistry":
        items = list(specs)
        mapping = {item.name: item for item in items}
        if len(mapping) != len(items):
            raise ValueError("duplicate capability name")
        return cls(mapping)

    def get(self, name: str) -> CapabilitySpec:
        try:
            return self._specs[name]
        except KeyError as exc:
            raise ControlPlaneError("UNKNOWN_CAPABILITY", f"unknown capability: {name}") from exc

    def discover(self) -> list[dict]:
        return [self._specs[name].as_dict() for name in sorted(self._specs)]


def build_registry() -> CapabilityRegistry:
    return CapabilityRegistry.from_specs(
        [
            CapabilitySpec("control.discovery", 1, "List stable capability contracts.", "control.read", 5, False, 131_072),
            CapabilitySpec("control.health", 1, "Report control-plane and bridge health.", "control.read", 15, False, 131_072),
            CapabilitySpec("control.cancel", 1, "Request cancellation of a bridge job.", "execution.cancel", 15, False, 65_536),
            CapabilitySpec("execution.run", 1, "Run one bounded command through Heaven Bridge proc_run.", "execution.run", 1800, True),
            CapabilitySpec("execution.session.start", 1, "Start a bounded persistent shell/process session.", "execution.session", 30, False, destructive=True),
            CapabilitySpec("execution.session.read", 1, "Read bounded output and state from a persistent session.", "execution.session.read", 15, False),
            CapabilitySpec("execution.session.input", 1, "Write bounded input to a live persistent session.", "execution.session", 15, False, destructive=True),
            CapabilitySpec("execution.session.stop", 1, "Stop a persistent session process tree.", "execution.session", 30, False, destructive=True),
            CapabilitySpec("execution.session.list", 1, "List known persistent sessions and lifecycle state.", "execution.session.read", 15, False),
            CapabilitySpec("filesystem.read", 1, "Read a bounded page of UTF-8 text.", "filesystem.read", 30, False),
            CapabilitySpec("filesystem.write", 1, "Write or append bounded UTF-8 text.", "filesystem.write", 30, False, destructive=True),
            CapabilitySpec("filesystem.patch", 1, "Replace an exact text occurrence through fs_edit.", "filesystem.write", 30, False, destructive=True),
            CapabilitySpec("filesystem.search", 1, "Search allowlisted files by name or bounded text content.", "filesystem.read", 120, False),
            CapabilitySpec("git.status", 1, "Read repository branch and working-tree status.", "repository.read", 30, False),
            CapabilitySpec("git.diff", 1, "Read a bounded repository diff.", "repository.read", 60, False),
            CapabilitySpec("git.verify_remote_main", 1, "Fetch origin/main and report exact local/remote identity.", "repository.verify", 120, False),
            CapabilitySpec("verification.detect", 1, "Detect supported project families from bounded repository markers.", "verification.read", 60, False, 131_072),
            CapabilitySpec("verification.run", 1, "Run one fixed build/test/lint/typecheck plan for a detected project.", "verification.run", 1800, True),
            CapabilitySpec("observability.logs.page", 1, "Page through sanitized in-memory audit records.", "observability.read", 5, False, 131_072),
            CapabilitySpec("observability.artifacts.page", 1, "Page a registered text artifact.", "observability.read", 5, False, 262_144),
        ]
    )
