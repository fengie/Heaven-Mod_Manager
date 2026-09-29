import base64
import shutil
import tempfile
import threading
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
import importlib.util

WORKER_PATH = Path(__file__).resolve().parents[1] / "worker.py"
spec = importlib.util.spec_from_file_location("heaven_bridge_worker", WORKER_PATH)
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class HeavenBridgeWorkerTests(unittest.TestCase):
    def make_job(self, action, params=None, job_id="test-job"):
        return {
            "id": job_id,
            "source": worker.PROTOCOL,
            "action": action,
            "params": params or {},
            "created_at": datetime.now(timezone.utc).isoformat(),
        }

    def run_job(self, job):
        return worker.run_job(job["id"], job, threading.Event())

    def test_health_capabilities(self):
        result = self.run_job(self.make_job("health", job_id="health-capabilities"))
        self.assertEqual(result["status"], "completed")
        self.assertEqual(result["data"]["worker_version"], 4)
        self.assertEqual(result["data"]["protocol"], "chatgpt-heaven-bridge-v2")
        for action in ("fs_delete", "fs_copy", "fs_read_binary", "fs_write_binary", "job_output_read"):
            self.assertIn(action, result["data"]["actions"])

    def test_priority_queue_ordering_keeps_control_responsive(self):
        base = Path.home() / "HeavenBridge" / "test-sandbox"
        base.mkdir(parents=True, exist_ok=True)
        td = Path(tempfile.mkdtemp(dir=base))
        try:
            regular = td / "regular.json"
            regular.write_text('{"action":"proc_run","priority":"highest","created_at":"2026-09-29T10:00:00Z"}', encoding="utf-8")
            control = td / "control.json"
            control.write_text('{"action":"job_status","priority":"lowest","created_at":"2026-09-29T10:05:00Z"}', encoding="utf-8")
            self.assertEqual(sorted([regular, control], key=worker.queue_order_key)[0].name, "control.json")
        finally:
            shutil.rmtree(td, ignore_errors=True)

    def test_priority_queue_ages_low_priority_work(self):
        current = datetime(2026, 9, 29, 10, 0, tzinfo=timezone.utc)
        base = Path.home() / "HeavenBridge" / "test-sandbox"
        base.mkdir(parents=True, exist_ok=True)
        td = Path(tempfile.mkdtemp(dir=base))
        try:
            old_low = td / "old-low.json"
            old_low.write_text(
                '{"action":"proc_run","priority":"low","created_at":"2026-09-29T09:40:00+00:00"}',
                encoding="utf-8",
            )
            fresh_high = td / "fresh-high.json"
            fresh_high.write_text(
                '{"action":"proc_run","priority":"highest","created_at":"2026-09-29T09:59:59+00:00"}',
                encoding="utf-8",
            )
            ordered = sorted(
                [fresh_high, old_low],
                key=lambda path: worker.queue_order_key(path, current=current),
            )
            self.assertEqual(ordered[0].name, "old-low.json")
        finally:
            shutil.rmtree(td, ignore_errors=True)

    def test_priority_queue_accepts_numeric_priority(self):
        current = datetime(2026, 9, 29, 10, 0, tzinfo=timezone.utc)
        base = Path.home() / "HeavenBridge" / "test-sandbox"
        base.mkdir(parents=True, exist_ok=True)
        td = Path(tempfile.mkdtemp(dir=base))
        try:
            urgent = td / "urgent.json"
            urgent.write_text(
                '{"action":"proc_run","priority":95,"created_at":"2026-09-29T10:00:00+00:00"}',
                encoding="utf-8",
            )
            background = td / "background.json"
            background.write_text(
                '{"action":"proc_run","priority":10,"created_at":"2026-09-29T10:00:00+00:00"}',
                encoding="utf-8",
            )
            ordered = sorted(
                [background, urgent],
                key=lambda path: worker.queue_order_key(path, current=current),
            )
            self.assertEqual(ordered[0].name, "urgent.json")
        finally:
            shutil.rmtree(td, ignore_errors=True)

    def test_expired_job_rejected(self):
        job = self.make_job("health", job_id="expired-job")
        job["created_at"] = (datetime.now(timezone.utc) - timedelta(days=2)).isoformat()
        job["ttl_seconds"] = 60
        with self.assertRaises(worker.BridgeError) as ctx:
            worker.validate_job(job["id"], job)
        self.assertEqual(ctx.exception.code, "JOB_EXPIRED")

    def test_hash_ignores_auth_signature(self):
        job = self.make_job("health", job_id="hash-auth")
        a = worker.job_hash(job)
        job["auth"] = {"signature": "0" * 64}
        self.assertEqual(a, worker.job_hash(job))

    def test_payload_change_changes_replay_hash(self):
        job = self.make_job("health", job_id="replay-job")
        digest = worker.validate_job(job["id"], job)
        changed = dict(job)
        changed["priority"] = "changed"
        changed_digest = worker.validate_job(changed["id"], changed)
        self.assertNotEqual(digest, changed_digest)

    def test_dangerous_root_delete_rejected(self):
        self.assertTrue(worker.dangerous_delete_target(Path.home()))
        job = self.make_job("fs_delete", {"path": str(Path.home())}, job_id="dangerous-delete")
        with self.assertRaises(worker.BridgeError) as ctx:
            self.run_job(job)
        self.assertEqual(ctx.exception.code, "DANGEROUS_DELETE_BLOCKED")

    def test_binary_copy_delete_roundtrip(self):
        base = Path.home() / "HeavenBridge" / "test-sandbox"
        base.mkdir(parents=True, exist_ok=True)
        td = Path(tempfile.mkdtemp(dir=base))
        try:
            src = td / "source.bin"
            payload = b"\x00\x01heaven-bridge-v3\xff"
            write = self.run_job(
                self.make_job(
                    "fs_write_binary",
                    {"path": str(src), "content_b64": base64.b64encode(payload).decode("ascii")},
                    job_id="binary-write",
                )
            )
            self.assertEqual(write["status"], "completed")

            read = self.run_job(
                self.make_job(
                    "fs_read_binary",
                    {"path": str(src), "offset": 0, "length": 1024},
                    job_id="binary-read",
                )
            )
            self.assertEqual(base64.b64decode(read["data"]["content_b64"]), payload)

            copied = td / "copied.bin"
            cp = self.run_job(
                self.make_job(
                    "fs_copy",
                    {"source": str(src), "destination": str(copied)},
                    job_id="binary-copy",
                )
            )
            self.assertTrue(copied.exists())
            self.assertEqual(cp["status"], "completed")

            deleted = self.run_job(
                self.make_job("fs_delete", {"path": str(copied)}, job_id="binary-delete")
            )
            self.assertTrue(deleted["data"]["deleted"])
            self.assertFalse(copied.exists())
        finally:
            shutil.rmtree(td, ignore_errors=True)

    def test_invalid_base64_has_structured_code(self):
        target = Path.home() / "HeavenBridge" / "bad.bin"
        job = self.make_job(
            "fs_write_binary",
            {"path": str(target), "content_b64": "%%%"},
            job_id="invalid-base64",
        )
        with self.assertRaises(worker.BridgeError) as ctx:
            self.run_job(job)
        self.assertEqual(ctx.exception.code, "INVALID_BASE64")


if __name__ == "__main__":
    unittest.main()
