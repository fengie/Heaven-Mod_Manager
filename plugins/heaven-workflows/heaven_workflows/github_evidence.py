"""Bounded GitHub Actions evidence; authentication belongs to the injected reader."""
from __future__ import annotations

import json
import re
from datetime import datetime, timedelta, timezone
from typing import Any, Callable, Mapping
from urllib.parse import urlencode

SCHEMA = "heaven-workflows/github-commit-evidence/v1"
TTL_SECONDS = 120
PAGE_SIZE = 50
MAX_PAGES = 4
MAX_PACKET_BYTES = 262_144
MAX_RESPONSE_BYTES = 2_097_152
EVENTS = {"push", "pull_request", "workflow_dispatch", "workflow_run", "schedule"}
STATUSES = {"completed", "in_progress", "queued", "requested", "waiting", "pending"}
CONCLUSIONS = {"success", "failure", "cancelled", "neutral", "skipped", "timed_out", "action_required", "stale", "startup_failure"}


class EvidenceUnavailable(ValueError):
    """The authorized provider did not produce trustworthy complete evidence."""


def _time(value: Any) -> datetime:
    if not isinstance(value, str):
        raise ValueError("Evidence timestamp is invalid.")
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("Evidence timestamp must carry a timezone.")
    return parsed.astimezone(timezone.utc)


def _text(value: Any, maximum: int = 128) -> str:
    if not isinstance(value, str) or not value or len(value.encode("utf-8")) > maximum or any(ord(c) < 32 for c in value):
        raise ValueError("Evidence text is invalid or exceeds its bound.")
    return value


def _integer(value: Any) -> int:
    if type(value) is not int or not 1 <= value <= 9_223_372_036_854_775_807:
        raise ValueError("Evidence identity must be a positive integer.")
    return value


def _scope(repository: str, sha: str, event: str, branch: str) -> dict[str, str]:
    if not isinstance(repository, str) or not re.fullmatch(r"[A-Za-z0-9_-]{1,100}/[A-Za-z0-9_.-]{1,100}", repository) or ".." in repository:
        raise ValueError("GitHub repository identity is invalid.")
    if not isinstance(sha, str) or not re.fullmatch(r"[a-f0-9]{40}", sha):
        raise ValueError("Exact source SHA is required.")
    if not isinstance(event, str) or event not in EVENTS:
        raise ValueError("GitHub event scope is invalid.")
    _text(branch, 200)
    if branch.startswith("-") or ".." in branch or any(c in branch for c in "?*[]\\~^:"):
        raise ValueError("GitHub branch scope is invalid.")
    return {"repository": repository, "sha": sha, "event": event, "branch": branch}


def _normalize(row: Mapping[str, Any], scope: Mapping[str, str]) -> dict[str, Any]:
    if row.get("head_sha") != scope["sha"] or row.get("event") != scope["event"] or row.get("head_branch") != scope["branch"]:
        raise ValueError("GitHub evidence returned a different source/event/branch.")
    run_id = _integer(row.get("id"))
    created = _time(row.get("created_at")).isoformat()
    status, conclusion = row.get("status"), row.get("conclusion")
    if not isinstance(status, str) or status not in STATUSES or (conclusion is not None and (not isinstance(conclusion, str) or conclusion not in CONCLUSIONS)):
        raise ValueError("GitHub lifecycle evidence is invalid.")
    return {"id": run_id, "workflow_id": _integer(row.get("workflow_id")), "run_attempt": _integer(row.get("run_attempt", 1)),
            "name": _text(row.get("name")), "sha": scope["sha"], "event": scope["event"], "branch": scope["branch"],
            "status": _text(row.get("status"), 32), "conclusion": row.get("conclusion") if row.get("conclusion") is None else _text(row.get("conclusion"), 32),
            "created_at": created, "url": f"https://github.com/{scope['repository']}/actions/runs/{run_id}"}


def collect_commit_runs(repository: str, sha: str, reader: Callable[[str], Mapping[str, Any]], *, event: str = "push", branch: str = "main", now: datetime | None = None) -> dict[str, Any]:
    scope = _scope(repository, sha, event, branch)
    if not callable(reader):
        raise EvidenceUnavailable("Authorized GitHub read provider is not configured.")
    observed = _time((now or datetime.now(timezone.utc)).isoformat())
    runs: dict[int, dict[str, Any]] = {}
    total = None
    reason = None
    for page in range(1, MAX_PAGES + 1):
        query = urlencode({"head_sha": sha, "event": event, "branch": branch, "per_page": PAGE_SIZE, "page": page})
        try:
            response = reader(f"/repos/{repository}/actions/runs?{query}")
        except Exception:
            raise EvidenceUnavailable("GitHub evidence read failed; inspect the authorized transport locally.") from None
        if not isinstance(response, Mapping) or len(json.dumps(response).encode("utf-8")) > MAX_RESPONSE_BYTES:
            raise EvidenceUnavailable("GitHub evidence response is invalid or oversized.")
        count, rows = response.get("total_count"), response.get("workflow_runs")
        if type(count) is not int or count < 0 or not isinstance(rows, list) or len(rows) > PAGE_SIZE:
            raise EvidenceUnavailable("GitHub evidence pagination is malformed.")
        if total is None:
            total = count
        elif total != count:
            reason = "snapshot_changed"
        for raw in rows:
            if not isinstance(raw, Mapping):
                raise EvidenceUnavailable("GitHub run evidence is malformed.")
            normalized = _normalize(raw, scope)
            old = runs.get(normalized["id"])
            if old and old != normalized:
                reason = "snapshot_changed"
            runs[normalized["id"]] = normalized
        if len(runs) == total:
            break
        if len(runs) > total or len(rows) < PAGE_SIZE:
            reason = reason or "incomplete_pagination"
            break
    if len(runs) != total:
        reason = reason or "page_limit"
    latest: dict[int, dict[str, Any]] = {}
    for row in runs.values():
        old = latest.get(row["workflow_id"])
        if old and row["created_at"] == old["created_at"] and row["id"] != old["id"]:
            reason = "ambiguous_run_order"
        if old is None or (_time(row["created_at"]), row["id"]) > (_time(old["created_at"]), old["id"]):
            latest[row["workflow_id"]] = row
    packet = {"schema": SCHEMA, "scope": scope, "observed_at": observed.isoformat(), "expires_at": (observed + timedelta(seconds=TTL_SECONDS)).isoformat(),
              "complete": reason is None, "incomplete_reason": reason, "total_count": total, "fetched_count": len(runs), "runs": sorted(latest.values(), key=lambda row: row["workflow_id"])}
    if len(json.dumps(packet).encode("utf-8")) > MAX_PACKET_BYTES:
        raise EvidenceUnavailable("GitHub evidence exceeded its output budget.")
    return packet


def validate_commit_evidence(packet: Mapping[str, Any], repository: str, sha: str, *, event: str = "push", branch: str = "main", now: datetime | None = None) -> list[Mapping[str, Any]]:
    scope = _scope(repository, sha, event, branch)
    if not isinstance(packet, Mapping) or packet.get("schema") != SCHEMA or packet.get("scope") != scope:
        raise EvidenceUnavailable("GitHub evidence scope/source mismatch.")
    if packet.get("complete") is not True or packet.get("incomplete_reason") is not None:
        raise EvidenceUnavailable("GitHub evidence is incomplete; refresh or expand the authorized query.")
    current = _time((now or datetime.now(timezone.utc)).isoformat())
    observed, expires = _time(packet.get("observed_at")), _time(packet.get("expires_at"))
    if observed > current or expires <= current or (expires - observed).total_seconds() != TTL_SECONDS:
        raise EvidenceUnavailable("GitHub evidence is expired or has invalid freshness.")
    if len(json.dumps(packet).encode("utf-8")) > MAX_PACKET_BYTES:
        raise EvidenceUnavailable("GitHub evidence exceeded its output budget.")
    rows = packet.get("runs")
    if not isinstance(rows, list) or len(rows) > MAX_PAGES * PAGE_SIZE:
        raise EvidenceUnavailable("GitHub evidence rows are invalid.")
    total, fetched = packet.get("total_count"), packet.get("fetched_count")
    if type(total) is not int or type(fetched) is not int or total != fetched or not len(rows) <= fetched <= MAX_PAGES * PAGE_SIZE:
        raise EvidenceUnavailable("GitHub evidence count/completeness mismatch.")
    identities = set()
    for row in rows:
        if not isinstance(row, Mapping) or row.get("sha") != sha or row.get("event") != event or row.get("branch") != branch:
            raise EvidenceUnavailable("GitHub run evidence scope/source mismatch.")
        identity = _integer(row.get("workflow_id"))
        if identity in identities:
            raise EvidenceUnavailable("GitHub evidence has duplicate workflow identities.")
        identities.add(identity)
        _integer(row.get("id"))
        _integer(row.get("run_attempt"))
        _time(row.get("created_at"))
        _text(row.get("name"))
        _text(row.get("status"), 32)
        if row.get("conclusion") is not None:
            _text(row.get("conclusion"), 32)
        status, conclusion = row.get("status"), row.get("conclusion")
        if status not in STATUSES or (conclusion is not None and conclusion not in CONCLUSIONS):
            raise EvidenceUnavailable("GitHub lifecycle evidence is invalid.")
    return rows
