from __future__ import annotations

import threading
import time
from typing import Any, Callable, Mapping, Protocol


class QueueLike(Protocol):
    def claim_task(self, worker_id: str, *, lease_seconds: int = 300) -> dict[str, Any] | None: ...
    def renew_lease(self, task_id: str, worker_id: str, *, lease_seconds: int = 300) -> None: ...
    def complete_task(self, task_id: str, worker_id: str, result: Any = None) -> dict[str, Any]: ...
    def fail_task(self, task_id: str, worker_id: str, error: str, *, retry: bool = True) -> dict[str, Any]: ...
    def handle_agent_failure(self, task_id: str, worker_id: str, output: Any, **kwargs: Any) -> dict[str, Any]: ...
    def get_task(self, task_id: str) -> dict[str, Any]: ...
    def acquire_resource(self, name: str, owner: str, *, lease_seconds: int = 300) -> bool: ...
    def release_resource(self, name: str, owner: str) -> bool: ...


class ClusterLike(Protocol):
    def choose_worker(
        self,
        required_capabilities: list[str],
        *,
        preferred_labels: list[str] = (),
        max_age_seconds: int = 120,
    ) -> dict[str, Any]: ...
    def reserve_worker(self, worker_id: str) -> dict[str, Any]: ...
    def release_worker(self, worker_id: str) -> dict[str, Any]: ...


Transport = Callable[[dict[str, Any], Mapping[str, Any]], Any]


class QueueClusterDispatcher:
    """Bridge TaskQueue leases to capability-aware ClusterScheduler workers."""

    def __init__(self, queue: QueueLike, cluster: ClusterLike, *, dispatcher_id: str = "cluster-dispatcher"):
        dispatcher_id = str(dispatcher_id or "").strip()
        if not dispatcher_id or len(dispatcher_id) > 128:
            raise ValueError("dispatcher_id must be 1..128 characters")
        self.queue = queue
        self.cluster = cluster
        self.dispatcher_id = dispatcher_id

    @staticmethod
    def _string_list(value: Any, field: str, *, limit: int = 64) -> list[str]:
        if value is None:
            return []
        if isinstance(value, (str, bytes)) or not isinstance(value, (list, tuple, set)):
            raise ValueError(f"{field} must be a sequence of strings")
        items: list[str] = []
        seen: set[str] = set()
        for raw in value:
            item = str(raw or "").strip()
            if not item:
                continue
            if len(item) > 256:
                raise ValueError(f"{field} entries must be <=256 characters")
            if item not in seen:
                seen.add(item)
                items.append(item)
        if len(items) > limit:
            raise ValueError(f"{field} exceeds {limit} entries")
        return items

    def _requirements(self, task: Mapping[str, Any]) -> tuple[list[str], list[str], list[str]]:
        payload = task.get("payload") or {}
        if not isinstance(payload, Mapping):
            raise ValueError("task payload must be an object")
        required = self._string_list(payload.get("required_capabilities"), "required_capabilities")
        preferred = self._string_list(payload.get("preferred_labels"), "preferred_labels")
        resources = sorted(self._string_list(payload.get("resources"), "resources"))
        return required, preferred, resources

    def _reserve_compatible_worker(
        self,
        required: list[str],
        preferred: list[str],
        *,
        max_age_seconds: int,
        attempts: int = 8,
    ) -> dict[str, Any]:
        last_error: Exception | None = None
        for _ in range(attempts):
            worker = self.cluster.choose_worker(
                required,
                preferred_labels=preferred,
                max_age_seconds=max_age_seconds,
            )
            try:
                self.cluster.reserve_worker(worker["id"])
                return worker
            except RuntimeError as exc:
                last_error = exc
                time.sleep(0)
        if last_error is not None:
            raise last_error
        raise RuntimeError("no worker reservation could be acquired")

    def dispatch_once(
        self,
        transport: Transport,
        *,
        lease_seconds: int = 300,
        worker_max_age_seconds: int = 120,
        lease_renew_interval: float | None = None,
    ) -> dict[str, Any]:
        if not callable(transport):
            raise ValueError("transport must be callable")
        if not isinstance(lease_seconds, int) or not 5 <= lease_seconds <= 86_400:
            raise ValueError("lease_seconds must be between 5 and 86400")
        if not isinstance(worker_max_age_seconds, int) or worker_max_age_seconds < 0:
            raise ValueError("worker_max_age_seconds must be >=0")

        task = self.queue.claim_task(self.dispatcher_id, lease_seconds=lease_seconds)
        if task is None:
            return {"status": "idle"}

        task_id = str(task["id"])
        owner = f"{self.dispatcher_id}:{task_id}"
        worker: dict[str, Any] | None = None
        acquired: list[str] = []
        stop_renew = threading.Event()
        lost_lease = threading.Event()
        renew_thread: threading.Thread | None = None

        try:
            try:
                required, preferred, resources = self._requirements(task)
            except Exception as exc:
                failed = self.queue.fail_task(
                    task_id,
                    self.dispatcher_id,
                    f"invalid dispatch metadata: {exc}",
                    retry=False,
                )
                return {"status": "failed", "task": failed, "error": str(exc)}

            for resource in resources:
                if not self.queue.acquire_resource(resource, owner, lease_seconds=lease_seconds):
                    for held in reversed(acquired):
                        self.queue.release_resource(held, owner)
                    acquired.clear()
                    retried = self.queue.fail_task(
                        task_id,
                        self.dispatcher_id,
                        f"resource unavailable: {resource}",
                        retry=True,
                    )
                    return {"status": "deferred", "task": retried, "resource": resource}
                acquired.append(resource)

            try:
                worker = self._reserve_compatible_worker(
                    required,
                    preferred,
                    max_age_seconds=worker_max_age_seconds,
                )
            except Exception as exc:
                retried = self.queue.fail_task(
                    task_id,
                    self.dispatcher_id,
                    f"worker unavailable: {exc}",
                    retry=True,
                )
                return {"status": "deferred", "task": retried, "error": str(exc)}

            interval = lease_renew_interval
            if interval is None:
                interval = max(1.0, min(30.0, lease_seconds / 3.0))
            if not isinstance(interval, (int, float)) or interval <= 0 or interval >= lease_seconds:
                raise ValueError("lease_renew_interval must be >0 and less than lease_seconds")

            def renew_loop() -> None:
                while not stop_renew.wait(float(interval)):
                    try:
                        self.queue.renew_lease(
                            task_id,
                            self.dispatcher_id,
                            lease_seconds=lease_seconds,
                        )
                    except Exception:
                        lost_lease.set()
                        return

            renew_thread = threading.Thread(
                target=renew_loop,
                name=f"lease-renew-{task_id}",
                daemon=True,
            )
            renew_thread.start()

            try:
                result = transport(worker, task)
            except Exception as exc:
                current = self.queue.get_task(task_id)
                if current.get("state") != "running" or current.get("lease_owner") != self.dispatcher_id:
                    return {"status": current.get("state", "lease_lost"), "task": current}
                failure = self.queue.handle_agent_failure(
                    task_id,
                    self.dispatcher_id,
                    str(exc),
                    retry=True,
                )
                return {
                    "status": "capacity_blocked" if failure.get("hard_usage_limit") else failure["task"]["state"],
                    "task": failure["task"],
                    "worker": worker["id"],
                    "error": str(exc)[:4096],
                    "terminate_agent": bool(failure.get("terminate_agent")),
                }

            current = self.queue.get_task(task_id)
            if lost_lease.is_set() or current.get("state") != "running" or current.get("lease_owner") != self.dispatcher_id:
                return {
                    "status": current.get("state", "lease_lost"),
                    "task": current,
                    "worker": worker["id"],
                }

            receipt = {"worker": worker["id"], "result": result}
            completed = self.queue.complete_task(
                task_id,
                self.dispatcher_id,
                receipt,
            )
            return {
                "status": "completed",
                "task": completed,
                "worker": worker["id"],
                "receipt": receipt,
            }
        finally:
            stop_renew.set()
            if renew_thread is not None:
                renew_thread.join(timeout=1.0)
            if worker is not None:
                try:
                    self.cluster.release_worker(worker["id"])
                except Exception:
                    pass
            for resource in reversed(acquired):
                try:
                    self.queue.release_resource(resource, owner)
                except Exception:
                    pass
