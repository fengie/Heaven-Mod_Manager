from __future__ import annotations

import threading
from collections import deque
from dataclasses import dataclass
from typing import Any

from .protocol import ControlPlaneError, bounded_int, require_string, utc_now


class AuditLog:
    """Bounded, payload-free audit metadata.

    Deliberately stores no command text, file contents, environment values, or bridge output.
    """

    def __init__(self, max_records: int = 1000):
        self._records: deque[dict[str, Any]] = deque(maxlen=max(10, int(max_records)))
        self._lock = threading.Lock()

    def append(self, **record: Any) -> None:
        row = {"ts": utc_now(), **record}
        with self._lock:
            self._records.append(row)

    def page(self, *, offset: int = 0, length: int = 100) -> dict[str, Any]:
        offset = bounded_int(offset, "offset", default=0, minimum=0, maximum=1_000_000)
        length = bounded_int(length, "length", default=100, minimum=1, maximum=500)
        with self._lock:
            rows = list(self._records)
        page = rows[offset : offset + length]
        return {
            "offset": offset,
            "length": len(page),
            "total": len(rows),
            "has_more": offset + len(page) < len(rows),
            "items": page,
        }


@dataclass
class _Artifact:
    name: str
    text: str
    media_type: str


class ArtifactStore:
    """Small in-memory text artifact registry used by compound capabilities/tests.

    Durable bridge/process output remains on the Heaven worker and is paged there.
    """

    def __init__(self, max_artifacts: int = 128, max_artifact_bytes: int = 2_000_000):
        self.max_artifacts = max_artifacts
        self.max_artifact_bytes = max_artifact_bytes
        self._items: dict[str, _Artifact] = {}
        self._order: deque[str] = deque()
        self._lock = threading.Lock()

    def put(self, artifact_id: str, text: str, *, name: str | None = None, media_type: str = "text/plain") -> None:
        artifact_id = require_string(artifact_id, "artifact_id", max_length=128)
        text = require_string(text, "text", allow_empty=True, max_length=self.max_artifact_bytes)
        if len(text.encode("utf-8")) > self.max_artifact_bytes:
            raise ControlPlaneError("INPUT_TOO_LARGE", "artifact exceeds byte budget")
        item = _Artifact(name=name or artifact_id, text=text, media_type=media_type)
        with self._lock:
            if artifact_id not in self._items:
                self._order.append(artifact_id)
            self._items[artifact_id] = item
            while len(self._order) > self.max_artifacts:
                evicted = self._order.popleft()
                self._items.pop(evicted, None)

    def page(self, artifact_id: str, *, offset: int = 0, length: int = 200_000) -> dict[str, Any]:
        artifact_id = require_string(artifact_id, "artifact_id", max_length=128)
        offset = bounded_int(offset, "offset", default=0, minimum=0, maximum=20_000_000)
        length = bounded_int(length, "length", default=200_000, minimum=1, maximum=200_000)
        with self._lock:
            item = self._items.get(artifact_id)
        if item is None:
            raise ControlPlaneError("ARTIFACT_NOT_FOUND", f"artifact not found: {artifact_id}")
        chunk = item.text[offset : offset + length]
        return {
            "artifact_id": artifact_id,
            "name": item.name,
            "media_type": item.media_type,
            "offset": offset,
            "length": len(chunk),
            "total": len(item.text),
            "has_more": offset + len(chunk) < len(item.text),
            "text": chunk,
        }
