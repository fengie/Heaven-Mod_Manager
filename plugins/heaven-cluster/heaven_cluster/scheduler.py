from __future__ import annotations

import json
import sqlite3
import time
from contextlib import closing
from pathlib import Path
from typing import Any, Callable, Iterable, Mapping


class ClusterScheduler:
    """Capability-aware, load-aware worker registry and dispatcher."""

    def __init__(self, database: str | Path):
        self.database = str(database)
        Path(self.database).parent.mkdir(parents=True, exist_ok=True)
        self._init()

    def _connect(self) -> sqlite3.Connection:
        c = sqlite3.connect(self.database, timeout=30, isolation_level=None)
        c.row_factory = sqlite3.Row
        c.execute("PRAGMA busy_timeout=30000")
        return c

    def _init(self) -> None:
        with closing(self._connect()) as c:
            c.executescript(
                """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS workers(
                    id TEXT PRIMARY KEY,
                    endpoint TEXT NOT NULL,
                    capabilities_json TEXT NOT NULL,
                    labels_json TEXT NOT NULL,
                    capacity INTEGER NOT NULL,
                    running INTEGER NOT NULL DEFAULT 0,
                    paused INTEGER NOT NULL DEFAULT 0,
                    heartbeat_at REAL NOT NULL
                );
                """
            )

    @staticmethod
    def _id(value: Any) -> str:
        v = str(value or "").strip()
        if not v or len(v) > 128 or any(
            ch not in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._:-" for ch in v
        ):
            raise ValueError("invalid worker id")
        return v

    @staticmethod
    def _json_list(values: Iterable[str], limit: int = 128) -> str:
        if isinstance(values, (str, bytes)):
            raise ValueError("capabilities/labels must be an iterable of strings, not one string")
        items = sorted(set(str(x).strip() for x in values if str(x).strip()))
        if len(items) > limit or any(len(x) > 128 for x in items):
            raise ValueError("capability/label list exceeds bounds")
        return json.dumps(items, separators=(",", ":"))

    @staticmethod
    def _decode(row: sqlite3.Row) -> dict[str, Any]:
        capacity = int(row["capacity"])
        running = int(row["running"])
        return {
            "id": row["id"],
            "endpoint": row["endpoint"],
            "capabilities": json.loads(row["capabilities_json"]),
            "labels": json.loads(row["labels_json"]),
            "capacity": capacity,
            "running": running,
            "paused": bool(row["paused"]),
            "heartbeat_at": row["heartbeat_at"],
            "load": running / capacity,
        }

    def register_worker(
        self,
        worker_id: str,
        endpoint: str,
        capabilities: Iterable[str],
        *,
        labels: Iterable[str] = (),
        capacity: int = 1,
    ) -> dict[str, Any]:
        worker_id = self._id(worker_id)
        if not isinstance(endpoint, str) or not endpoint.strip() or len(endpoint) > 2048:
            raise ValueError("endpoint must be a non-empty opaque transport locator")
        if not isinstance(capacity, int) or not 1 <= capacity <= 1024:
            raise ValueError("capacity must be 1..1024")
        caps = self._json_list(capabilities)
        labs = self._json_list(labels)
        now = time.time()
        with closing(self._connect()) as c:
            c.execute(
                """INSERT INTO workers(id,endpoint,capabilities_json,labels_json,capacity,running,paused,heartbeat_at)
                   VALUES(?,?,?,?,?,0,0,?)
                   ON CONFLICT(id) DO UPDATE SET endpoint=excluded.endpoint,
                   capabilities_json=excluded.capabilities_json,labels_json=excluded.labels_json,
                   capacity=excluded.capacity,heartbeat_at=excluded.heartbeat_at""",
                (worker_id, endpoint.strip(), caps, labs, capacity, now),
            )
        return self.get_worker(worker_id)

    def heartbeat(self, worker_id: str, *, running: int | None = None) -> dict[str, Any]:
        worker_id = self._id(worker_id)
        now = time.time()
        with closing(self._connect()) as c:
            if running is None:
                changed = c.execute(
                    "UPDATE workers SET heartbeat_at=? WHERE id=?", (now, worker_id)
                ).rowcount
            else:
                if not isinstance(running, int) or running < 0:
                    raise ValueError("running must be >=0")
                changed = c.execute(
                    "UPDATE workers SET heartbeat_at=?,running=? WHERE id=?",
                    (now, running, worker_id),
                ).rowcount
        if not changed:
            raise KeyError(worker_id)
        return self.get_worker(worker_id)

    def get_worker(self, worker_id: str) -> dict[str, Any]:
        with closing(self._connect()) as c:
            row = c.execute(
                "SELECT * FROM workers WHERE id=?", (self._id(worker_id),)
            ).fetchone()
        if row is None:
            raise KeyError(worker_id)
        return self._decode(row)

    def set_paused(self, worker_id: str, paused: bool) -> dict[str, Any]:
        with closing(self._connect()) as c:
            changed = c.execute(
                "UPDATE workers SET paused=? WHERE id=?",
                (1 if paused else 0, self._id(worker_id)),
            ).rowcount
        if not changed:
            raise KeyError(worker_id)
        return self.get_worker(worker_id)

    def list_workers(self, *, max_age_seconds: int | None = None) -> list[dict[str, Any]]:
        if max_age_seconds is not None and (
            not isinstance(max_age_seconds, int) or max_age_seconds < 0
        ):
            raise ValueError("max_age_seconds must be >=0")
        with closing(self._connect()) as c:
            rows = c.execute("SELECT * FROM workers ORDER BY id").fetchall()
        items = [self._decode(row) for row in rows]
        if max_age_seconds is not None:
            cutoff = time.time() - max_age_seconds
            items = [x for x in items if x["heartbeat_at"] >= cutoff]
        return items

    def choose_worker(
        self,
        required_capabilities: Iterable[str],
        *,
        preferred_labels: Iterable[str] = (),
        max_age_seconds: int = 120,
    ) -> dict[str, Any]:
        if not isinstance(max_age_seconds, int) or max_age_seconds < 0:
            raise ValueError("max_age_seconds must be >=0")
        required = set(json.loads(self._json_list(required_capabilities)))
        preferred = set(json.loads(self._json_list(preferred_labels)))
        candidates = []
        for worker in self.list_workers(max_age_seconds=max_age_seconds):
            caps = set(worker["capabilities"])
            labels = set(worker["labels"])
            if (
                worker["paused"]
                or worker["running"] >= worker["capacity"]
                or not required.issubset(caps)
            ):
                continue
            label_score = len(preferred & labels)
            candidates.append(
                (-label_score, worker["load"], worker["running"], worker["id"], worker)
            )
        if not candidates:
            raise RuntimeError("no healthy worker satisfies required capabilities")
        candidates.sort(key=lambda x: x[:4])
        return candidates[0][4]

    def reserve_worker(self, worker_id: str) -> dict[str, Any]:
        worker_id = self._id(worker_id)
        with closing(self._connect()) as c:
            c.execute("BEGIN IMMEDIATE")
            try:
                row = c.execute(
                    "SELECT running,capacity,paused FROM workers WHERE id=?", (worker_id,)
                ).fetchone()
                if row is None:
                    raise KeyError(worker_id)
                if row["paused"] or row["running"] >= row["capacity"]:
                    raise RuntimeError("worker has no available capacity")
                c.execute("UPDATE workers SET running=running+1 WHERE id=?", (worker_id,))
                c.commit()
            except Exception:
                c.rollback()
                raise
        return self.get_worker(worker_id)

    def release_worker(self, worker_id: str) -> dict[str, Any]:
        worker_id = self._id(worker_id)
        with closing(self._connect()) as c:
            changed = c.execute(
                "UPDATE workers SET running=CASE WHEN running>0 THEN running-1 ELSE 0 END WHERE id=?",
                (worker_id,),
            ).rowcount
        if not changed:
            raise KeyError(worker_id)
        return self.get_worker(worker_id)

    def dispatch(
        self,
        task: Mapping[str, Any],
        required_capabilities: Iterable[str],
        dispatcher: Callable[[dict[str, Any], Mapping[str, Any]], Any],
        *,
        preferred_labels: Iterable[str] = (),
    ) -> dict[str, Any]:
        worker = self.choose_worker(
            required_capabilities, preferred_labels=preferred_labels
        )
        self.reserve_worker(worker["id"])
        try:
            result = dispatcher(worker, task)
            return {"worker": worker["id"], "result": result}
        finally:
            self.release_worker(worker["id"])
