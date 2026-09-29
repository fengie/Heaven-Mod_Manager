from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass
from typing import Any, Mapping, Protocol, Sequence


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


SAFE_PARALLEL_CAPABILITIES = frozenset(
    {
        "control.health",
        "filesystem.read",
        "filesystem.search",
        "git.status",
        "git.diff",
        "git.verify_remote_main",
        "verification.detect",
        "verification.run",
        "index.stats",
        "index.search.text",
        "index.search.symbols",
        "observability.logs.page",
        "observability.artifacts.page",
    }
)
MAX_PARALLEL_TASKS = 16
MAX_WORKERS = 8


@dataclass(frozen=True)
class WorkflowResult:
    name: str
    ok: bool
    result: dict[str, Any]

    def as_dict(self) -> dict[str, Any]:
        return {"name": self.name, "ok": self.ok, "result": self.result}


class HeavenWorkflowPlugin:
    """Reusable high-level workflows composed from Heaven Control Plane capabilities."""

    def __init__(self, control_plane: ControlPlaneLike):
        self.control_plane = control_plane

    @staticmethod
    def _require_repo(value: Any) -> str:
        if not isinstance(value, str) or not value.strip():
            raise ValueError("repo must be a non-empty string")
        value = value.strip()
        if len(value) > 2048 or "\x00" in value:
            raise ValueError("repo is invalid")
        normalized = value.replace("\\", "/")
        if any(part == ".." for part in normalized.split("/")):
            raise ValueError("repo must not contain '..' path segments")
        return value

    @staticmethod
    def _is_ok(result: Mapping[str, Any]) -> bool:
        return str(result.get("status") or "").lower() == "ok"

    def repo_snapshot(self, repo: str, *, include_diff: bool = True) -> dict[str, Any]:
        repo = self._require_repo(repo)
        status = self.control_plane.invoke("git.status", {"repo": repo})
        output: dict[str, Any] = {"status": status}
        if include_diff:
            output["diff"] = self.control_plane.invoke("git.diff", {"repo": repo})
        output["remote_main"] = self.control_plane.invoke("git.verify_remote_main", {"repo": repo})
        return output

    def verify_repo(
        self,
        repo: str,
        *,
        project_type: str | None = None,
        kinds: Sequence[str] = ("test", "build"),
        timeout_seconds: int | None = None,
    ) -> dict[str, Any]:
        repo = self._require_repo(repo)
        if isinstance(kinds, str):
            raise ValueError("kinds must be a sequence, not a string")
        normalized = [str(kind).strip().lower() for kind in kinds]
        if not normalized or len(normalized) > 8:
            raise ValueError("kinds must contain between 1 and 8 entries")
        allowed = {"test", "build", "lint", "typecheck"}
        if any(kind not in allowed for kind in normalized):
            raise ValueError(f"kinds must be drawn from {sorted(allowed)}")

        detection = self.control_plane.invoke("verification.detect", {"repo": repo})
        runs: list[dict[str, Any]] = []
        for kind in normalized:
            payload: dict[str, Any] = {"repo": repo, "kind": kind}
            if project_type:
                payload["project_type"] = project_type
            if timeout_seconds is not None:
                if not isinstance(timeout_seconds, int) or not 1 <= timeout_seconds <= 1800:
                    raise ValueError("timeout_seconds must be an integer from 1 to 1800")
                payload["timeout_seconds"] = timeout_seconds
            result = self.control_plane.invoke("verification.run", payload)
            runs.append({"kind": kind, "result": result})
            if not self._is_ok(result):
                break
        return {
            "repo": repo,
            "detection": detection,
            "runs": runs,
            "ok": self._is_ok(detection) and all(self._is_ok(x["result"]) for x in runs),
        }

    def parallel_invoke(
        self,
        tasks: Sequence[Mapping[str, Any]],
        *,
        max_workers: int = 4,
    ) -> dict[str, Any]:
        if isinstance(tasks, (str, bytes)) or not isinstance(tasks, Sequence):
            raise ValueError("tasks must be a sequence")
        if not 1 <= len(tasks) <= MAX_PARALLEL_TASKS:
            raise ValueError(f"tasks must contain between 1 and {MAX_PARALLEL_TASKS} entries")
        if not isinstance(max_workers, int) or not 1 <= max_workers <= MAX_WORKERS:
            raise ValueError(f"max_workers must be an integer from 1 to {MAX_WORKERS}")

        normalized: list[tuple[int, str, str, Mapping[str, Any]]] = []
        for index, task in enumerate(tasks):
            if not isinstance(task, Mapping):
                raise ValueError("each task must be an object")
            name = str(task.get("name") or f"task-{index + 1}")
            capability = str(task.get("capability") or "")
            payload = task.get("payload") or {}
            if capability not in SAFE_PARALLEL_CAPABILITIES:
                raise ValueError(f"parallel capability is not allowlisted: {capability}")
            if not isinstance(payload, Mapping):
                raise ValueError("task payload must be an object")
            normalized.append((index, name[:128], capability, dict(payload)))

        results: list[WorkflowResult | None] = [None] * len(normalized)

        def run_one(item: tuple[int, str, str, Mapping[str, Any]]) -> tuple[int, WorkflowResult]:
            index, name, capability, payload = item
            result = self.control_plane.invoke(capability, payload)
            return index, WorkflowResult(name=name, ok=self._is_ok(result), result=result)

        with ThreadPoolExecutor(max_workers=min(max_workers, len(normalized))) as pool:
            futures = [pool.submit(run_one, item) for item in normalized]
            for future in as_completed(futures):
                index, result = future.result()
                results[index] = result

        completed = [item for item in results if item is not None]
        return {
            "ok": all(item.ok for item in completed),
            "count": len(completed),
            "results": [item.as_dict() for item in completed],
        }
