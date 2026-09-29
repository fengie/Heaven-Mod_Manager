from __future__ import annotations

import json
import re
import sqlite3
import time
import uuid
from contextlib import contextmanager
from pathlib import Path
from typing import Any, Callable, Iterable, Iterator, Mapping


VALID_STATES = {"queued", "running", "blocked", "capacity_blocked", "completed", "failed", "cancelled"}
_IDENTIFIER_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
DEFAULT_CAPACITY_NOTICE = "Usage limit reached · agent stopped"
_HARD_USAGE_LIMIT_PATTERNS = (
    re.compile(r"you(?:'|’)ve\s+hit\s+your\s+usage\s+limit", re.IGNORECASE),
    re.compile(r"usage\s+limit.{0,240}(?:upgrade|purchase\s+more\s+credits|try\s+again\s+at)", re.IGNORECASE | re.DOTALL),
    re.compile(r"(?:credits?\s+(?:are\s+)?exhausted|no\s+credits?\s+remaining)", re.IGNORECASE),
    re.compile(r"insufficient[_\s-]*quota", re.IGNORECASE),
    re.compile(r"quota\s+exceeded.{0,160}(?:billing|credits?)", re.IGNORECASE | re.DOTALL),
)


def is_hard_usage_limit(value: Any) -> bool:
    """Return True only for hard provider quota/credit exhaustion, not ordinary transient rate limiting."""
    text = str(value or "")
    return any(pattern.search(text) for pattern in _HARD_USAGE_LIMIT_PATTERNS)


class TaskQueue:
    """SQLite-backed task queue with dependencies, leases, retries, workers, and resource locks."""

    def __init__(self, database: str | Path):
        self.database = str(database)
        Path(self.database).parent.mkdir(parents=True, exist_ok=True)
        self._initialize()

    def _connect(self) -> sqlite3.Connection:
        conn = sqlite3.connect(self.database, timeout=30, isolation_level=None)
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA foreign_keys=ON")
        conn.execute("PRAGMA busy_timeout=30000")
        return conn

    @contextmanager
    def _connection(self) -> Iterator[sqlite3.Connection]:
        conn = self._connect()
        try:
            yield conn
        finally:
            conn.close()

    def _initialize(self) -> None:
        with self._connection() as conn:
            conn.executescript(
                """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS tasks (
                    id TEXT PRIMARY KEY,
                    kind TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    state TEXT NOT NULL,
                    priority INTEGER NOT NULL DEFAULT 0,
                    attempts INTEGER NOT NULL DEFAULT 0,
                    max_attempts INTEGER NOT NULL DEFAULT 3,
                    capacity_scope TEXT,
                    lease_owner TEXT,
                    lease_expires_at REAL,
                    result_json TEXT,
                    error TEXT,
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS dependencies (
                    task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
                    depends_on TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
                    PRIMARY KEY(task_id, depends_on)
                );
                CREATE TABLE IF NOT EXISTS workers (
                    id TEXT PRIMARY KEY,
                    metadata_json TEXT NOT NULL,
                    heartbeat_at REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS resource_locks (
                    name TEXT PRIMARY KEY,
                    owner TEXT NOT NULL,
                    expires_at REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS capacity_blocks (
                    scope TEXT PRIMARY KEY,
                    reason TEXT NOT NULL,
                    notice TEXT NOT NULL,
                    retry_after REAL,
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_tasks_ready
                    ON tasks(state, priority DESC, created_at ASC);
                """
            )
            columns = {row["name"] for row in conn.execute("PRAGMA table_info(tasks)").fetchall()}
            if "capacity_scope" not in columns:
                conn.execute("ALTER TABLE tasks ADD COLUMN capacity_scope TEXT")

    @staticmethod
    def _identifier(value: Any, field: str) -> str:
        identifier = str(value or "").strip()
        if not _IDENTIFIER_RE.fullmatch(identifier):
            raise ValueError(f"{field} must match {_IDENTIFIER_RE.pattern}")
        return identifier

    @staticmethod
    def _json(value: Any, *, max_bytes: int = 256_000) -> str:
        raw = json.dumps(value, separators=(",", ":"), ensure_ascii=False)
        if len(raw.encode("utf-8")) > max_bytes:
            raise ValueError("JSON payload exceeds queue limit")
        return raw

    @staticmethod
    def _row(row: sqlite3.Row) -> dict[str, Any]:
        return {
            "id": row["id"],
            "kind": row["kind"],
            "payload": json.loads(row["payload_json"]),
            "state": row["state"],
            "priority": row["priority"],
            "attempts": row["attempts"],
            "max_attempts": row["max_attempts"],
            "capacity_scope": row["capacity_scope"],
            "lease_owner": row["lease_owner"],
            "lease_expires_at": row["lease_expires_at"],
            "result": json.loads(row["result_json"]) if row["result_json"] else None,
            "error": row["error"],
            "created_at": row["created_at"],
            "updated_at": row["updated_at"],
        }

    def create_task(
        self,
        kind: str,
        payload: Mapping[str, Any] | None = None,
        *,
        dependencies: Iterable[str] = (),
        priority: int = 0,
        max_attempts: int = 3,
        task_id: str | None = None,
        capacity_scope: str | None = None,
    ) -> dict[str, Any]:
        kind = str(kind).strip()
        if not kind or len(kind) > 128:
            raise ValueError("kind must be a non-empty string up to 128 characters")
        if not isinstance(priority, int) or not -1000 <= priority <= 1000:
            raise ValueError("priority must be between -1000 and 1000")
        if not isinstance(max_attempts, int) or not 1 <= max_attempts <= 100:
            raise ValueError("max_attempts must be between 1 and 100")
        dep_ids = list(
            dict.fromkeys(self._identifier(x, "dependency") for x in dependencies if str(x).strip())
        )
        if len(dep_ids) > 128:
            raise ValueError("too many dependencies")
        tid = self._identifier(task_id or uuid.uuid4().hex, "task_id")
        scope = None if capacity_scope is None else self._identifier(capacity_scope, "capacity_scope")
        now = time.time()
        payload_json = self._json(dict(payload or {}))
        with self._connection() as conn:
            conn.execute("BEGIN IMMEDIATE")
            for dep in dep_ids:
                if dep == tid:
                    raise ValueError("task cannot depend on itself")
                if conn.execute("SELECT 1 FROM tasks WHERE id=?", (dep,)).fetchone() is None:
                    raise ValueError(f"unknown dependency: {dep}")
            conn.execute(
                """INSERT INTO tasks(id,kind,payload_json,state,priority,max_attempts,capacity_scope,created_at,updated_at)
                   VALUES(?,?,?,?,?,?,?,?,?)""",
                (tid, kind, payload_json, "queued", priority, max_attempts, scope, now, now),
            )
            conn.executemany(
                "INSERT INTO dependencies(task_id,depends_on) VALUES(?,?)",
                [(tid, dep) for dep in dep_ids],
            )
            conn.commit()
        return self.get_task(tid)

    def get_task(self, task_id: str) -> dict[str, Any]:
        with self._connection() as conn:
            row = conn.execute("SELECT * FROM tasks WHERE id=?", (task_id,)).fetchone()
        if row is None:
            raise KeyError(task_id)
        return self._row(row)

    def register_worker(self, worker_id: str, metadata: Mapping[str, Any] | None = None) -> None:
        worker_id = self._identifier(worker_id, "worker_id")
        metadata_json = self._json(dict(metadata or {}), max_bytes=32_000)
        now = time.time()
        with self._connection() as conn:
            conn.execute(
                """INSERT INTO workers(id,metadata_json,heartbeat_at) VALUES(?,?,?)
                   ON CONFLICT(id) DO UPDATE SET metadata_json=excluded.metadata_json, heartbeat_at=excluded.heartbeat_at""",
                (worker_id, metadata_json, now),
            )

    def heartbeat(self, worker_id: str) -> None:
        now = time.time()
        with self._connection() as conn:
            changed = conn.execute("UPDATE workers SET heartbeat_at=? WHERE id=?", (now, worker_id)).rowcount
        if not changed:
            raise KeyError(worker_id)

    def _reap_expired(self, conn: sqlite3.Connection, now: float) -> None:
        conn.execute(
            """UPDATE tasks
               SET state=CASE WHEN attempts < max_attempts THEN 'queued' ELSE 'failed' END,
                   lease_owner=NULL, lease_expires_at=NULL,
                   error=CASE WHEN attempts < max_attempts THEN error ELSE COALESCE(error,'lease expired') END,
                   updated_at=?
               WHERE state='running' AND lease_expires_at IS NOT NULL AND lease_expires_at < ?""",
            (now, now),
        )
        conn.execute("DELETE FROM resource_locks WHERE expires_at < ?", (now,))
        conn.execute(
            "DELETE FROM capacity_blocks WHERE retry_after IS NOT NULL AND retry_after <= ?",
            (now,),
        )

    def claim_task(self, worker_id: str, *, lease_seconds: int = 300) -> dict[str, Any] | None:
        if not isinstance(lease_seconds, int) or not 5 <= lease_seconds <= 86_400:
            raise ValueError("lease_seconds must be between 5 and 86400")
        now = time.time()
        with self._connection() as conn:
            conn.execute("BEGIN IMMEDIATE")
            self._reap_expired(conn, now)
            row = conn.execute(
                """
                SELECT t.*
                FROM tasks t
                WHERE t.state='queued'
                  AND t.attempts < t.max_attempts
                  AND (
                    t.capacity_scope IS NULL OR NOT EXISTS (
                      SELECT 1 FROM capacity_blocks c WHERE c.scope=t.capacity_scope
                    )
                  )
                  AND NOT EXISTS (
                    SELECT 1
                    FROM dependencies d
                    JOIN tasks dep ON dep.id=d.depends_on
                    WHERE d.task_id=t.id AND dep.state!='completed'
                  )
                ORDER BY t.priority DESC, t.created_at ASC
                LIMIT 1
                """
            ).fetchone()
            if row is None:
                conn.commit()
                return None
            expires = now + lease_seconds
            conn.execute(
                """UPDATE tasks SET state='running', attempts=attempts+1, lease_owner=?,
                   lease_expires_at=?, updated_at=? WHERE id=?""",
                (worker_id, expires, now, row["id"]),
            )
            conn.commit()
        return self.get_task(row["id"])

    def renew_lease(self, task_id: str, worker_id: str, *, lease_seconds: int = 300) -> None:
        if not isinstance(lease_seconds, int) or not 5 <= lease_seconds <= 86_400:
            raise ValueError("lease_seconds must be between 5 and 86400")
        now = time.time()
        with self._connection() as conn:
            changed = conn.execute(
                """UPDATE tasks SET lease_expires_at=?, updated_at=?
                   WHERE id=? AND state='running' AND lease_owner=?""",
                (now + lease_seconds, now, task_id, worker_id),
            ).rowcount
        if not changed:
            raise ValueError("task is not leased by worker")

    def complete_task(self, task_id: str, worker_id: str, result: Any = None) -> dict[str, Any]:
        now = time.time()
        result_json = self._json(result)
        with self._connection() as conn:
            conn.execute("BEGIN IMMEDIATE")
            changed = conn.execute(
                """UPDATE tasks SET state='completed', result_json=?, error=NULL,
                   lease_owner=NULL, lease_expires_at=NULL, updated_at=?
                   WHERE id=? AND state='running' AND lease_owner=?""",
                (result_json, now, task_id, worker_id),
            ).rowcount
            conn.execute("DELETE FROM resource_locks WHERE owner=?", (f"{worker_id}:{task_id}",))
            conn.commit()
        if not changed:
            raise ValueError("task is not leased by worker")
        return self.get_task(task_id)

    def fail_task(self, task_id: str, worker_id: str, error: str, *, retry: bool = True) -> dict[str, Any]:
        error = str(error)[:4096]
        now = time.time()
        with self._connection() as conn:
            conn.execute("BEGIN IMMEDIATE")
            row = conn.execute(
                "SELECT attempts,max_attempts FROM tasks WHERE id=? AND state='running' AND lease_owner=?",
                (task_id, worker_id),
            ).fetchone()
            if row is None:
                conn.rollback()
                raise ValueError("task is not leased by worker")
            state = "queued" if retry and row["attempts"] < row["max_attempts"] else "failed"
            conn.execute(
                """UPDATE tasks SET state=?, error=?, lease_owner=NULL, lease_expires_at=NULL, updated_at=?
                   WHERE id=?""",
                (state, error, now, task_id),
            )
            conn.execute("DELETE FROM resource_locks WHERE owner=?", (f"{worker_id}:{task_id}",))
            conn.commit()
        return self.get_task(task_id)

    def capacity_block_task(
        self,
        task_id: str,
        worker_id: str,
        reason: str,
        *,
        capacity_scope: str | None = None,
        notice: str = DEFAULT_CAPACITY_NOTICE,
        retry_after: float | None = None,
    ) -> dict[str, Any]:
        """Mark a hard-capacity failure terminal and suppress new work for the same execution scope."""
        task_id = self._identifier(task_id, "task_id")
        worker_id = self._identifier(worker_id, "worker_id")
        reason = str(reason)[:4096]
        notice = str(notice or DEFAULT_CAPACITY_NOTICE).strip()[:512] or DEFAULT_CAPACITY_NOTICE
        now = time.time()
        if retry_after is not None:
            if not isinstance(retry_after, (int, float)) or retry_after <= now:
                raise ValueError("retry_after must be a future unix timestamp")
            retry_after = float(retry_after)

        with self._connection() as conn:
            conn.execute("BEGIN IMMEDIATE")
            row = conn.execute(
                """SELECT capacity_scope FROM tasks
                   WHERE id=? AND state='running' AND lease_owner=?""",
                (task_id, worker_id),
            ).fetchone()
            if row is None:
                conn.rollback()
                raise ValueError("task is not leased by worker")
            scope_value = capacity_scope if capacity_scope is not None else row["capacity_scope"]
            scope = self._identifier(scope_value, "capacity_scope")
            result_json = self._json(
                {
                    "notice": notice,
                    "capacity_scope": scope,
                    "retry_after": retry_after,
                    "terminal_reason": "hard_usage_limit",
                },
                max_bytes=32_000,
            )
            conn.execute(
                """INSERT INTO capacity_blocks(scope,reason,notice,retry_after,created_at,updated_at)
                   VALUES(?,?,?,?,?,?)
                   ON CONFLICT(scope) DO UPDATE SET
                     reason=excluded.reason,
                     notice=excluded.notice,
                     retry_after=excluded.retry_after,
                     updated_at=excluded.updated_at""",
                (scope, reason, notice, retry_after, now, now),
            )
            conn.execute(
                """UPDATE tasks SET state='capacity_blocked', error=?, result_json=?,
                   lease_owner=NULL, lease_expires_at=NULL, updated_at=? WHERE id=?""",
                (reason, result_json, now, task_id),
            )
            conn.execute("DELETE FROM resource_locks WHERE owner=?", (f"{worker_id}:{task_id}",))
            conn.commit()
        return self.get_task(task_id)

    def handle_agent_failure(
        self,
        task_id: str,
        worker_id: str,
        output: Any,
        *,
        capacity_scope: str | None = None,
        retry: bool = True,
        retry_after: float | None = None,
        notice: str = DEFAULT_CAPACITY_NOTICE,
        terminate: Callable[[], Any] | None = None,
    ) -> dict[str, Any]:
        """Classify an agent failure and make hard quota exhaustion terminal.

        Hard usage limits release the task lease and resource locks, suppress the same execution
        scope, and request immediate process termination. Callers may pass their owned-process
        terminator directly or use the returned terminate_agent flag to kill it themselves.
        """
        error_text = str(output or "")
        if not is_hard_usage_limit(error_text):
            task = self.fail_task(task_id, worker_id, error_text, retry=retry)
            return {
                "task": task,
                "hard_usage_limit": False,
                "terminate_agent": False,
                "notice": None,
                "terminated": False,
                "termination_error": None,
            }

        task = self.capacity_block_task(
            task_id,
            worker_id,
            error_text,
            capacity_scope=capacity_scope,
            notice=notice,
            retry_after=retry_after,
        )
        terminated = False
        termination_error = None
        if terminate is not None:
            try:
                terminate()
                terminated = True
            except Exception as exc:
                termination_error = f"{type(exc).__name__}: {exc}"[:1024]
        return {
            "task": task,
            "hard_usage_limit": True,
            "terminate_agent": True,
            "notice": notice,
            "terminated": terminated,
            "termination_error": termination_error,
        }

    def capacity_status(self, scope: str) -> dict[str, Any] | None:
        scope = self._identifier(scope, "capacity_scope")
        now = time.time()
        with self._connection() as conn:
            conn.execute("BEGIN IMMEDIATE")
            self._reap_expired(conn, now)
            row = conn.execute("SELECT * FROM capacity_blocks WHERE scope=?", (scope,)).fetchone()
            conn.commit()
        return None if row is None else dict(row)

    def list_capacity_blocks(self, *, limit: int = 100) -> list[dict[str, Any]]:
        if not isinstance(limit, int) or not 1 <= limit <= 1000:
            raise ValueError("limit must be between 1 and 1000")
        now = time.time()
        with self._connection() as conn:
            conn.execute("BEGIN IMMEDIATE")
            self._reap_expired(conn, now)
            rows = conn.execute(
                "SELECT * FROM capacity_blocks ORDER BY updated_at DESC LIMIT ?",
                (limit,),
            ).fetchall()
            conn.commit()
        return [dict(row) for row in rows]

    def clear_capacity(self, scope: str) -> bool:
        """Re-enable future tasks for a scope without resurrecting the killed historical run."""
        scope = self._identifier(scope, "capacity_scope")
        with self._connection() as conn:
            return bool(conn.execute("DELETE FROM capacity_blocks WHERE scope=?", (scope,)).rowcount)

    def block_task(self, task_id: str, reason: str) -> dict[str, Any]:
        now = time.time()
        with self._connection() as conn:
            changed = conn.execute(
                """UPDATE tasks SET state='blocked', error=?, lease_owner=NULL,
                   lease_expires_at=NULL, updated_at=? WHERE id=? AND state NOT IN ('completed','failed','cancelled','capacity_blocked')""",
                (str(reason)[:4096], now, task_id),
            ).rowcount
        if not changed:
            raise ValueError("task cannot be blocked")
        return self.get_task(task_id)

    def cancel_task(self, task_id: str, reason: str = "cancelled") -> dict[str, Any]:
        task_id = self._identifier(task_id, "task_id")
        now = time.time()
        with self._connection() as conn:
            conn.execute("BEGIN IMMEDIATE")
            row = conn.execute(
                "SELECT lease_owner FROM tasks WHERE id=? AND state NOT IN ('completed','failed','cancelled','capacity_blocked')",
                (task_id,),
            ).fetchone()
            if row is None:
                conn.rollback()
                raise ValueError("task cannot be cancelled")
            conn.execute(
                """UPDATE tasks SET state='cancelled', error=?, lease_owner=NULL,
                   lease_expires_at=NULL, updated_at=? WHERE id=?""",
                (str(reason)[:4096], now, task_id),
            )
            if row["lease_owner"]:
                conn.execute(
                    "DELETE FROM resource_locks WHERE owner=?",
                    (f"{row['lease_owner']}:{task_id}",),
                )
            conn.commit()
        return self.get_task(task_id)

    def acquire_resource(self, name: str, owner: str, *, lease_seconds: int = 300) -> bool:
        name = str(name).strip()
        owner = str(owner).strip()
        if not name or len(name) > 256 or not owner or len(owner) > 256:
            raise ValueError("resource lock name/owner is invalid")
        if not isinstance(lease_seconds, int) or not 5 <= lease_seconds <= 86_400:
            raise ValueError("lease_seconds must be between 5 and 86400")
        now = time.time()
        with self._connection() as conn:
            conn.execute("BEGIN IMMEDIATE")
            self._reap_expired(conn, now)
            row = conn.execute("SELECT owner FROM resource_locks WHERE name=?", (name,)).fetchone()
            if row is not None and row["owner"] != owner:
                conn.commit()
                return False
            conn.execute(
                """INSERT INTO resource_locks(name,owner,expires_at) VALUES(?,?,?)
                   ON CONFLICT(name) DO UPDATE SET owner=excluded.owner, expires_at=excluded.expires_at""",
                (name, owner, now + lease_seconds),
            )
            conn.commit()
        return True

    def release_resource(self, name: str, owner: str) -> bool:
        with self._connection() as conn:
            return bool(conn.execute("DELETE FROM resource_locks WHERE name=? AND owner=?", (name, owner)).rowcount)

    def list_tasks(self, *, state: str | None = None, limit: int = 100) -> list[dict[str, Any]]:
        if state is not None and state not in VALID_STATES:
            raise ValueError("invalid task state")
        if not isinstance(limit, int) or not 1 <= limit <= 1000:
            raise ValueError("limit must be between 1 and 1000")
        with self._connection() as conn:
            if state is None:
                rows = conn.execute(
                    "SELECT * FROM tasks ORDER BY created_at DESC LIMIT ?", (limit,)
                ).fetchall()
            else:
                rows = conn.execute(
                    "SELECT * FROM tasks WHERE state=? ORDER BY created_at DESC LIMIT ?",
                    (state, limit),
                ).fetchall()
        return [self._row(row) for row in rows]
