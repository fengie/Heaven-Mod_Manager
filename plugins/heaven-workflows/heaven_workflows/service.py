from __future__ import annotations

import hashlib
import json
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

    @staticmethod
    def _require_hex(value: Any, length: int, label: str) -> str:
        if not isinstance(value, str):
            raise ValueError(f"{label} must be a string")
        value = value.strip().lower()
        if len(value) != length or any(ch not in "0123456789abcdef" for ch in value):
            raise ValueError(f"{label} must be exactly {length} lowercase hex characters")
        return value

    @staticmethod
    def _require_release_text(value: Any, label: str, *, max_length: int = 128) -> str:
        if not isinstance(value, str) or not value.strip():
            raise ValueError(f"{label} must be a non-empty string")
        value = value.strip()
        if len(value) > max_length or "\x00" in value:
            raise ValueError(f"{label} is invalid")
        return value

    def release_plan(self, repo: str, *, candidate_sha: str, version: str, channel: str, required_gates: Sequence[str], artifacts: Sequence[Mapping[str, Any]]) -> dict[str, Any]:
        repo = self._require_repo(repo)
        candidate_sha = self._require_hex(candidate_sha, 40, "candidate_sha")
        version = self._require_release_text(version, "version", max_length=64)
        channel = self._require_release_text(channel, "channel", max_length=32)
        if isinstance(required_gates, (str, bytes)) or not 1 <= len(required_gates) <= 32:
            raise ValueError("required_gates must contain between 1 and 32 entries")
        gates = [self._require_release_text(x, "gate", max_length=128) for x in required_gates]
        if len(gates) != len(set(gates)):
            raise ValueError("required_gates must be unique")
        if isinstance(artifacts, (str, bytes)) or not 1 <= len(artifacts) <= 64:
            raise ValueError("artifacts must contain between 1 and 64 entries")
        normalized = []
        names = set()
        for item in artifacts:
            if not isinstance(item, Mapping):
                raise ValueError("each artifact plan must be an object")
            name = self._require_release_text(item.get("name"), "artifact name", max_length=256)
            if name in names:
                raise ValueError("artifact names must be unique")
            names.add(name)
            entry: dict[str, Any] = {"name": name}
            if item.get("sha256") is not None:
                entry["sha256"] = self._require_hex(item.get("sha256"), 64, "artifact sha256")
            if item.get("size") is not None:
                if not isinstance(item.get("size"), int) or item["size"] < 0:
                    raise ValueError("artifact size must be a non-negative integer")
                entry["size"] = item["size"]
            normalized.append(entry)
        spec = {"repo":repo,"candidate_sha":candidate_sha,"version":version,"channel":channel,"required_gates":sorted(gates),"artifacts":sorted(normalized,key=lambda x:x["name"])}
        canonical = json.dumps(spec, sort_keys=True, separators=(",", ":")).encode("utf-8")
        return {"schema":"heaven-release/plan/v1","plan_id":hashlib.sha256(canonical).hexdigest()[:24],**spec}

    def release_verify_gates(self, plan: Mapping[str, Any], statuses: Sequence[Mapping[str, Any]]) -> dict[str, Any]:
        candidate_sha = self._require_hex(plan.get("candidate_sha"), 40, "candidate_sha")
        required = plan.get("required_gates")
        if isinstance(required, (str, bytes)) or not isinstance(required, Sequence) or not required:
            raise ValueError("plan required_gates is invalid")
        if isinstance(statuses, (str, bytes)) or not isinstance(statuses, Sequence) or len(statuses) > 512:
            raise ValueError("statuses must be a bounded sequence")
        by_name: dict[str, list[Mapping[str, Any]]] = {}
        for row in statuses:
            if not isinstance(row, Mapping):
                raise ValueError("each status must be an object")
            name = self._require_release_text(row.get("name"), "status name", max_length=128)
            by_name.setdefault(name, []).append(row)
        failures, passed = [], []
        for gate in required:
            gate = self._require_release_text(gate, "gate", max_length=128)
            rows = by_name.get(gate, [])
            exact = [row for row in rows if str(row.get("sha") or "").lower() == candidate_sha]
            if not exact:
                failures.append({"gate":gate,"reason":"wrong_commit" if rows else "missing"}); continue
            success = [row for row in exact if str(row.get("conclusion") or row.get("result") or row.get("status") or "").lower() in {"success","passed","ok"}]
            if not success:
                failures.append({"gate":gate,"reason":"not_successful"}); continue
            passed.append(gate)
        return {"ok":not failures,"plan_id":plan.get("plan_id"),"candidate_sha":candidate_sha,"passed":passed,"failures":failures}

    def release_verify_artifacts(self, plan: Mapping[str, Any], artifacts: Sequence[Mapping[str, Any]]) -> dict[str, Any]:
        version = self._require_release_text(plan.get("version"), "version", max_length=64)
        channel = self._require_release_text(plan.get("channel"), "channel", max_length=32)
        expected = plan.get("artifacts")
        if isinstance(expected, (str, bytes)) or not isinstance(expected, Sequence) or not expected:
            raise ValueError("plan artifacts is invalid")
        if isinstance(artifacts, (str, bytes)) or not isinstance(artifacts, Sequence) or len(artifacts) > 64:
            raise ValueError("artifacts must be a bounded sequence")
        actual = {}
        for item in artifacts:
            if not isinstance(item, Mapping):
                raise ValueError("each artifact must be an object")
            name = self._require_release_text(item.get("name"), "artifact name", max_length=256)
            if name in actual:
                raise ValueError("artifact names must be unique")
            actual[name] = item
        failures, verified = [], []
        for spec in expected:
            name = self._require_release_text(spec.get("name"), "artifact name", max_length=256)
            item = actual.get(name)
            if item is None:
                failures.append({"artifact":name,"reason":"missing"}); continue
            digest = self._require_hex(item.get("sha256"), 64, "artifact sha256")
            size = item.get("size")
            if not isinstance(size, int) or size < 0:
                raise ValueError("artifact size must be a non-negative integer")
            if self._require_release_text(item.get("version"), "artifact version", max_length=64) != version:
                failures.append({"artifact":name,"reason":"version_mismatch"}); continue
            if self._require_release_text(item.get("channel"), "artifact channel", max_length=32) != channel:
                failures.append({"artifact":name,"reason":"channel_mismatch"}); continue
            if spec.get("sha256") is not None and digest != spec.get("sha256"):
                failures.append({"artifact":name,"reason":"digest_mismatch"}); continue
            if spec.get("size") is not None and size != spec.get("size"):
                failures.append({"artifact":name,"reason":"size_mismatch"}); continue
            verified.append({"name":name,"sha256":digest,"size":size,"version":version,"channel":channel})
        extras = sorted(set(actual) - {str(x.get("name")) for x in expected if isinstance(x, Mapping)})
        if extras:
            failures.append({"artifact":",".join(extras),"reason":"unexpected"})
        return {"ok":not failures,"plan_id":plan.get("plan_id"),"version":version,"channel":channel,"artifacts":sorted(verified,key=lambda x:x["name"]),"failures":failures}

    def release_authorize_publish(self, plan: Mapping[str, Any], gate_verification: Mapping[str, Any], artifact_verification: Mapping[str, Any], *, confirmation: str) -> dict[str, Any]:
        plan_id = self._require_release_text(plan.get("plan_id"), "plan_id", max_length=64)
        candidate_sha = self._require_hex(plan.get("candidate_sha"), 40, "candidate_sha")
        required_gates = set(plan.get("required_gates") or [])
        if gate_verification.get("plan_id") != plan_id or gate_verification.get("candidate_sha") != candidate_sha or gate_verification.get("ok") is not True or set(gate_verification.get("passed") or []) != required_gates:
            raise ValueError("required gates are not verified for this release plan")
        if artifact_verification.get("plan_id") != plan_id or artifact_verification.get("ok") is not True:
            raise ValueError("release artifacts are not verified for this release plan")
        rebound = self.release_verify_artifacts(plan, artifact_verification.get("artifacts") or [])
        if rebound.get("ok") is not True:
            raise ValueError("release artifact verification is not bound to this release plan")
        expected = f"CONFIRM PUBLISH {plan_id}"
        if confirmation != expected:
            raise ValueError("explicit publish confirmation does not match this release plan")
        artifacts = artifact_verification.get("artifacts")
        if not isinstance(artifacts, list) or not artifacts:
            raise ValueError("verified artifact receipt is missing")
        receipt = {"schema":"heaven-release/receipt/v1","plan_id":plan_id,"repo":plan.get("repo"),"source_sha":candidate_sha,"version":plan.get("version"),"channel":plan.get("channel"),"gates":sorted(gate_verification.get("passed") or []),"artifacts":artifacts}
        canonical = json.dumps(receipt, sort_keys=True, separators=(",", ":")).encode("utf-8")
        receipt["receipt_id"] = hashlib.sha256(canonical).hexdigest()
        return {"authorized":True,"confirmation_phrase":expected,"receipt":receipt}

    def release_reconcile_existing(self, receipt: Mapping[str, Any], existing_release: Mapping[str, Any] | None) -> dict[str, Any]:
        if existing_release is None:
            return {"action":"publish","reason":"no_existing_release"}
        mismatches = [field for field in ("source_sha","version","channel") if existing_release.get(field) != receipt.get(field)]
        def digest_map(value: Any) -> dict[str, str]:
            if not isinstance(value, Sequence) or isinstance(value, (str, bytes)):
                return {}
            return {item["name"]:item["sha256"].lower() for item in value if isinstance(item, Mapping) and isinstance(item.get("name"), str) and isinstance(item.get("sha256"), str)}
        if digest_map(existing_release.get("artifacts")) != digest_map(receipt.get("artifacts")):
            mismatches.append("artifacts")
        return {"action":"conflict","mismatches":sorted(set(mismatches))} if mismatches else {"action":"already_published","reason":"existing release exactly matches authorized receipt"}

    def release_plan_cancellations(self, candidate_sha: str, runs: Sequence[Mapping[str, Any]]) -> dict[str, Any]:
        candidate_sha = self._require_hex(candidate_sha, 40, "candidate_sha")
        if isinstance(runs, (str, bytes)) or not isinstance(runs, Sequence) or len(runs) > 512:
            raise ValueError("runs must be a bounded sequence")
        ids = []
        for run in runs:
            if not isinstance(run, Mapping):
                raise ValueError("each run must be an object")
            if str(run.get("head_sha") or "").lower() != candidate_sha and str(run.get("status") or "").lower() in {"queued","pending","in_progress"} and isinstance(run.get("id"), int) and run["id"] > 0:
                ids.append(run["id"])
        return {"candidate_sha":candidate_sha,"cancel_run_ids":sorted(set(ids)),"requires_confirmation":bool(ids)}

