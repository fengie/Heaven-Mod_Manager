from __future__ import annotations

import sqlite3
from contextlib import closing
from pathlib import Path
from typing import Any, Sequence

_BLOCKED_SQL = ("ATTACH ", "DETACH ", "VACUUM INTO", "LOAD_EXTENSION", "WRITABLE_SCHEMA")


class DatabasePlugin:
    """Safe local SQLite inspection, query, mutation, integrity, and backup helpers."""

    def __init__(self, allowed_root: str | Path):
        self.root = Path(allowed_root).resolve()
        self.root.mkdir(parents=True, exist_ok=True)

    def _path(self, value: str | Path, *, must_exist: bool = True) -> Path:
        raw = Path(value)
        candidate = raw if raw.is_absolute() else self.root / raw

        # Resolve non-strictly first so an escaping path is rejected before
        # Windows can raise FileNotFoundError for an out-of-root target.
        resolved = candidate.resolve(strict=False)
        try:
            resolved.relative_to(self.root)
        except ValueError as exc:
            raise ValueError("database path escapes allowed root") from exc

        if must_exist:
            resolved = candidate.resolve(strict=True)
            try:
                resolved.relative_to(self.root)
            except ValueError as exc:
                raise ValueError("database path escapes allowed root") from exc
        return resolved

    @staticmethod
    def _guard_sql(sql: str) -> str:
        if not isinstance(sql, str) or not sql.strip() or len(sql) > 100_000:
            raise ValueError("SQL must be a non-empty string up to 100000 characters")
        upper = " ".join(sql.upper().split())
        if any(token in upper for token in _BLOCKED_SQL):
            raise ValueError("SQL contains a blocked cross-database or unsafe operation")
        return sql

    def schema(self, database: str | Path) -> dict[str, Any]:
        db = self._path(database)
        with closing(sqlite3.connect(db)) as c:
            c.row_factory = sqlite3.Row
            cur = c.execute(
                "SELECT type,name,tbl_name,sql FROM sqlite_master "
                "WHERE type IN ('table','view','index','trigger') ORDER BY type,name"
            )
            rows = cur.fetchall()
            cur.close()
        return {"database": str(db), "objects": [dict(r) for r in rows]}

    def query(
        self, database: str | Path, sql: str, params: Sequence[Any] = (), *, max_rows: int = 1000
    ) -> dict[str, Any]:
        db = self._path(database)
        sql = self._guard_sql(sql)
        if not isinstance(max_rows, int) or not 1 <= max_rows <= 10000:
            raise ValueError("max_rows must be 1..10000")
        with closing(sqlite3.connect(db)) as c:
            c.row_factory = sqlite3.Row
            c.execute("PRAGMA query_only=ON")
            cur = c.execute(sql, tuple(params))
            rows = cur.fetchmany(max_rows + 1)
            cols = [d[0] for d in cur.description] if cur.description else []
            cur.close()
        truncated = len(rows) > max_rows
        rows = rows[:max_rows]
        return {"columns": cols, "rows": [dict(r) for r in rows], "count": len(rows), "truncated": truncated}

    def execute(
        self, database: str | Path, sql: str, params: Sequence[Any] = (), *, confirm: bool = False
    ) -> dict[str, Any]:
        if not confirm:
            raise ValueError("confirm=True is required for database mutations")
        db = self._path(database)
        sql = self._guard_sql(sql)
        with closing(sqlite3.connect(db)) as c:
            before = c.total_changes
            cur = c.execute(sql, tuple(params))
            c.commit()
            result = {
                "rowcount": cur.rowcount,
                "changes": c.total_changes - before,
                "lastrowid": cur.lastrowid,
            }
            cur.close()
        return result

    def integrity_check(self, database: str | Path) -> dict[str, Any]:
        db = self._path(database)
        with closing(sqlite3.connect(db)) as c:
            cur = c.execute("PRAGMA integrity_check")
            rows = [r[0] for r in cur.fetchall()]
            cur.close()
        return {"ok": rows == ["ok"], "results": rows}

    def backup(self, database: str | Path, destination: str | Path, *, confirm: bool = False) -> dict[str, Any]:
        if not confirm:
            raise ValueError("confirm=True is required to create/replace a backup")
        src = self._path(database)
        dst = self._path(destination, must_exist=False)
        if src == dst:
            raise ValueError("backup destination must differ from source database")
        dst.parent.mkdir(parents=True, exist_ok=True)
        if dst.exists():
            dst.unlink()
        with closing(sqlite3.connect(src)) as source, closing(sqlite3.connect(dst)) as target:
            source.backup(target)
            target.commit()
        return {"source": str(src), "destination": str(dst), "bytes": dst.stat().st_size}
