from __future__ import annotations

import hashlib
import json
import re
from typing import Any, Mapping, Protocol, Sequence

_CHANGE_ID_RE = re.compile(r"^[A-Za-z0-9_.:-]{1,128}$")
_CAPABILITY_RE = re.compile(r"^[a-z][a-z0-9_.-]{1,127}$")
_HANDLE_RE = re.compile(r"^[A-Za-z0-9_.:/-]{1,256}$")
_SENSITIVE_TERMS = ("password", "passwd", "secret", "token", "api_key", "apikey", "private_key")
_BLOCKED_CAPABILITIES = {
    "execution.run",
    "execution.session.start",
    "execution.session.input",
    "python",
    "powershell",
    "cmd",
    "codex",
}


class StateStoreLike(Protocol):
    def put_checkpoint(
        self, key: str, payload: Any, *, expected_revision: int | None = None
    ) -> Mapping[str, Any]:
        ...

    def get_checkpoint(self, key: str) -> Mapping[str, Any] | None:
        ...


class CapabilityInvokerLike(Protocol):
    def invoke(
        self,
        capability_name: str,
        payload: Mapping[str, Any] | None = None,
        **kwargs: Any,
    ) -> Mapping[str, Any]:
        ...


def _canonical_hash(value: Any) -> str:
    raw = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()


def _validate_persistable(value: Any, *, depth: int = 0) -> None:
    if depth > 16:
        raise ValueError("persisted rollback state is too deeply nested")
    if isinstance(value, Mapping):
        for key, nested in value.items():
            label = str(key).lower()
            if label.endswith("_handle"):
                if not isinstance(nested, str) or not _HANDLE_RE.fullmatch(nested):
                    raise ValueError(f"{key} must be an opaque handle identifier")
            elif any(term in label for term in _SENSITIVE_TERMS):
                raise ValueError(f"credential-bearing field is not persistable: {key}")
            _validate_persistable(nested, depth=depth + 1)
    elif isinstance(value, (list, tuple)):
        if len(value) > 512:
            raise ValueError("persisted rollback list exceeds 512 entries")
        for item in value:
            _validate_persistable(item, depth=depth + 1)
    elif isinstance(value, str):
        if len(value) > 32768:
            raise ValueError("persisted rollback string exceeds 32768 characters")
    elif value is not None and not isinstance(value, (bool, int, float)):
        raise ValueError(f"unsupported persisted rollback value type: {type(value).__name__}")


class RollbackCoordinator:
    """Durable, declarative rollback plans with optimistic checkpoint updates."""

    def __init__(self, store: StateStoreLike, invoker: CapabilityInvokerLike):
        self.store = store
        self.invoker = invoker

    @staticmethod
    def _change_id(value: Any) -> str:
        if not isinstance(value, str) or not _CHANGE_ID_RE.fullmatch(value):
            raise ValueError("change_id must match [A-Za-z0-9_.:-]{1,128}")
        return value

    @staticmethod
    def _state_id(value: Any, label: str) -> str:
        if not isinstance(value, str) or not value.strip() or len(value) > 256 or "\x00" in value:
            raise ValueError(f"{label} must be a non-empty string up to 256 characters")
        return value.strip()

    @staticmethod
    def _key(change_id: str) -> str:
        return f"rollback:{change_id}"

    @staticmethod
    def _is_ok(result: Mapping[str, Any]) -> bool:
        if result.get("ok") is True:
            return True
        return str(result.get("status") or "").lower() in {"ok", "success", "completed"}

    @staticmethod
    def _step_status(result: Mapping[str, Any]) -> str:
        value = str(result.get("status") or ("ok" if result.get("ok") is True else "unknown"))
        return value[:64]

    @classmethod
    def _steps(cls, value: Any) -> list[dict[str, Any]]:
        if isinstance(value, (str, bytes)) or not isinstance(value, Sequence) or not 1 <= len(value) <= 32:
            raise ValueError("rollback_steps must contain between 1 and 32 entries")
        normalized: list[dict[str, Any]] = []
        ids: set[str] = set()
        for item in value:
            if not isinstance(item, Mapping):
                raise ValueError("each rollback step must be an object")
            step_id = cls._state_id(item.get("step_id"), "step_id")
            if step_id in ids:
                raise ValueError("rollback step IDs must be unique")
            ids.add(step_id)
            capability = item.get("capability")
            if not isinstance(capability, str) or not _CAPABILITY_RE.fullmatch(capability):
                raise ValueError("rollback capability name is invalid")
            if capability in _BLOCKED_CAPABILITIES or capability.startswith("execution."):
                raise ValueError("raw execution capabilities are not allowed as rollback steps")
            payload = item.get("payload") or {}
            if not isinstance(payload, Mapping):
                raise ValueError("rollback payload must be an object")
            payload = dict(payload)
            _validate_persistable(payload)
            normalized.append(
                {
                    "step_id": step_id,
                    "capability": capability,
                    "payload": payload,
                }
            )
        return normalized

    def status(self, change_id: str) -> Mapping[str, Any] | None:
        return self.store.get_checkpoint(self._key(self._change_id(change_id)))

    def prepare_change(
        self,
        change_id: str,
        *,
        pre_state_id: str,
        rollback_steps: Sequence[Mapping[str, Any]],
        preconditions: Mapping[str, Any] | None = None,
        expected_post_state_id: str | None = None,
    ) -> Mapping[str, Any]:
        change_id = self._change_id(change_id)
        pre_state_id = self._state_id(pre_state_id, "pre_state_id")
        expected_post = (
            None
            if expected_post_state_id is None
            else self._state_id(expected_post_state_id, "expected_post_state_id")
        )
        if preconditions is not None and not isinstance(preconditions, Mapping):
            raise ValueError("preconditions must be an object")
        safe_preconditions = dict(preconditions or {})
        _validate_persistable(safe_preconditions)
        steps = self._steps(rollback_steps)
        spec = {
            "change_id": change_id,
            "pre_state_id": pre_state_id,
            "expected_post_state_id": expected_post,
            "preconditions": safe_preconditions,
            "rollback_steps": steps,
        }
        plan_id = _canonical_hash(spec)
        payload = {
            "schema": "heaven-change/v1",
            "change_id": change_id,
            "plan_id": plan_id,
            "status": "prepared",
            "pre_state_id": pre_state_id,
            "expected_post_state_id": expected_post,
            "post_state_id": None,
            "preconditions": safe_preconditions,
            "rollback_steps": steps,
            "rollback_completed": [],
            "commit_receipt_id": None,
            "rollback_receipt_id": None,
        }
        key = self._key(change_id)
        existing = self.store.get_checkpoint(key)
        if existing is not None:
            current = existing.get("payload") if isinstance(existing, Mapping) else None
            if isinstance(current, Mapping) and current.get("plan_id") == plan_id:
                return {**dict(existing), "idempotent": True}
            raise ValueError("change_id is already owned by a different rollback plan")
        stored = self.store.put_checkpoint(key, payload, expected_revision=None)
        return {"key": key, "plan": payload, "revision": stored.get("revision"), "idempotent": False}

    def commit_change(self, change_id: str, *, post_state_id: str) -> Mapping[str, Any]:
        change_id = self._change_id(change_id)
        post_state_id = self._state_id(post_state_id, "post_state_id")
        key = self._key(change_id)
        checkpoint = self.store.get_checkpoint(key)
        if checkpoint is None:
            raise KeyError(change_id)
        payload = dict(checkpoint.get("payload") or {})
        revision = checkpoint.get("revision")
        status = payload.get("status")
        if status == "rolled_back":
            raise ValueError("cannot commit a rolled-back change")
        if status == "committed":
            if payload.get("post_state_id") != post_state_id:
                raise ValueError("committed post_state_id does not match")
            return {
                "key": key,
                "revision": revision,
                "receipt_id": payload.get("commit_receipt_id"),
                "idempotent": True,
            }
        if status != "prepared":
            raise ValueError(f"change is not committable from status {status!r}")
        expected = payload.get("expected_post_state_id")
        if expected is not None and expected != post_state_id:
            raise ValueError("post_state_id does not match prepared expectation")
        receipt = {
            "plan_id": payload.get("plan_id"),
            "pre_state_id": payload.get("pre_state_id"),
            "post_state_id": post_state_id,
        }
        receipt_id = _canonical_hash(receipt)
        payload.update(
            {
                "status": "committed",
                "post_state_id": post_state_id,
                "commit_receipt_id": receipt_id,
            }
        )
        stored = self.store.put_checkpoint(key, payload, expected_revision=revision)
        return {
            "key": key,
            "revision": stored.get("revision"),
            "receipt_id": receipt_id,
            "pre_state_id": payload.get("pre_state_id"),
            "post_state_id": post_state_id,
            "idempotent": False,
        }

    def rollback_change(
        self,
        change_id: str,
        *,
        reason: str,
        confirm: bool = False,
    ) -> Mapping[str, Any]:
        if confirm is not True:
            raise ValueError("confirm=True is required for rollback mutations")
        change_id = self._change_id(change_id)
        if not isinstance(reason, str) or not reason.strip() or len(reason) > 512:
            raise ValueError("rollback reason must be 1..512 characters")
        reason = reason.strip()
        key = self._key(change_id)
        checkpoint = self.store.get_checkpoint(key)
        if checkpoint is None:
            raise KeyError(change_id)
        payload = dict(checkpoint.get("payload") or {})
        revision = checkpoint.get("revision")
        if payload.get("status") == "rolled_back":
            return {
                "key": key,
                "revision": revision,
                "receipt_id": payload.get("rollback_receipt_id"),
                "idempotent": True,
                "results": [],
            }
        if payload.get("status") not in {"prepared", "committed", "rollback_failed", "rolling_back"}:
            raise ValueError("change is not rollback-eligible")
        steps = self._steps(payload.get("rollback_steps"))
        completed = list(payload.get("rollback_completed") or [])
        completed_set = set(completed)
        results: list[dict[str, Any]] = []
        for step in reversed(steps):
            step_id = step["step_id"]
            if step_id in completed_set:
                continue
            result = self.invoker.invoke(step["capability"], step["payload"])
            ok = self._is_ok(result)
            status = self._step_status(result)
            results.append({"step_id": step_id, "capability": step["capability"], "ok": ok, "status": status})
            if not ok:
                payload.update(
                    {
                        "status": "rollback_failed",
                        "rollback_completed": completed,
                        "last_rollback_failure": {"step_id": step_id, "status": status},
                    }
                )
                stored = self.store.put_checkpoint(key, payload, expected_revision=revision)
                return {
                    "ok": False,
                    "key": key,
                    "revision": stored.get("revision"),
                    "failed_step_id": step_id,
                    "results": results,
                }
            completed.append(step_id)
            completed_set.add(step_id)
            payload.update(
                {
                    "status": "rolling_back",
                    "rollback_completed": completed,
                    "last_rollback_failure": None,
                }
            )
            stored = self.store.put_checkpoint(key, payload, expected_revision=revision)
            revision = stored.get("revision")
        receipt = {
            "plan_id": payload.get("plan_id"),
            "pre_state_id": payload.get("pre_state_id"),
            "post_state_id": payload.get("post_state_id"),
            "rollback_completed": completed,
            "reason": reason,
        }
        receipt_id = _canonical_hash(receipt)
        payload.update(
            {
                "status": "rolled_back",
                "rollback_completed": completed,
                "rollback_reason": reason,
                "rollback_receipt_id": receipt_id,
                "last_rollback_failure": None,
            }
        )
        stored = self.store.put_checkpoint(key, payload, expected_revision=revision)
        return {
            "ok": True,
            "key": key,
            "revision": stored.get("revision"),
            "receipt_id": receipt_id,
            "pre_state_id": payload.get("pre_state_id"),
            "post_state_id": payload.get("post_state_id"),
            "results": results,
            "idempotent": False,
        }
