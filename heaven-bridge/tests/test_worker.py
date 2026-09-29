import base64
import importlib.util
import json
import tempfile
import threading
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest import mock

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

    def run_job(self, action, params=None, job_id="test-job"):
        job = self.make_job(action, params=params, job_id=job_id)
        return worker.run_job(job_id, job, threading.Event())

    def test_health_capabilities(self):
        result = self.run_job("health", job_id="health-test")
        self.assertEqual(result["status"], "completed")
        self.assertEqual(result["data"]["worker_version"], 3)
        self.assertEqual(result["data"]["protocol"], worker.PROTOCOL)
        for action in ("fs_delete", "fs_copy", "fs_read_binary", "fs_write_binary", "job_output_read"):
            self.assertIn(action, result["data"]["actions"])

    def test_expired_job_rejected(self):
        job = self.make_job("health", job_id="expired-job")
        job["created_at"] = (datetime.now(timezone.utc) - timedelta(days=2)).isoformat()
        job["ttl_seconds"] = 60
        with self.assertRaises(worker.BridgeError) as ctx:
            worker.validate_job(job["id"], job)
        self.assertEqual(ctx.exception.code, "JOB_EXPIRED")

    def test_hash_ignores_auth_signature(self):
        job = self.make_job("health")
        job["auth"] = {"signature": "a" * 64}
        first = worker.job_hash(job)
        job["auth"]["signature"] = "b" * 64
        self.assertEqual(first, worker.job_hash(job))

    def test_replay_mismatch_rejected(self):
        original = self.make_job("health", job_id="replay-job")
        original_digest = worker.validate_job(original["id"], original)
        changed = dict(original)
        changed["priority"] = "changed"

        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            queue = root / "queue"
            results = root / "results"
            status = root / "status"
            queue.mkdir(parents=True)
            (queue / "replay-job.json").write_text(json.dumps(changed), encoding="utf-8")
            captured = {}

            def capture_result(job_id, job, body):
                captured["job_id"] = job_id
                captured["body"] = body

            with mock.patch.object(worker, "QUEUE", queue), \
                 mock.patch.object(worker, "RESULTS", results), \
                 mock.patch.object(worker, "STATUS_DIR", status), \
                 mock.patch.object(worker, "PROCESSED", {
                     "replay-job": {"hash": original_digest, "status": "completed"}
                 }), \
                 mock.patch.object(worker, "git_sync"), \
                 mock.patch.object(worker, "publish_result", side_effect=capture_result):
                worker.process_queue(mock.Mock())

        self.assertEqual(captured["job_id"], "replay-job")
        self.assertEqual(captured["body"]["status"], "error")
        self.assertEqual(captured["body"]["error"]["code"], "DUPLICATE_JOB_ID")

    def test_dangerous_root_delete_rejected(self):
        root = Path.home().resolve()
        self.assertTrue(worker.dangerous_delete_target(root, roots=[root]))

    def test_binary_copy_delete_roundtrip(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td).resolve()
            src = root / "source.bin"
            copied = root / "copied.bin"
            payload = b"\x00\x01heaven-bridge-v3\xff"

            with mock.patch.object(worker, "allowed_roots", return_value=[root]):
                write = self.run_job(
                    "fs_write_binary",
                    {"path": str(src), "content_b64": base64.b64encode(payload).decode("ascii")},
                    job_id="binary-write",
                )
                self.assertEqual(write["status"], "completed")

                read = self.run_job(
                    "fs_read_binary",
                    {"path": str(src), "offset": 0, "length": 1024},
                    job_id="binary-read",
                )
                self.assertEqual(base64.b64decode(read["data"]["content_b64"]), payload)

                cp = self.run_job(
                    "fs_copy",
                    {"source": str(src), "destination": str(copied)},
                    job_id="binary-copy",
                )
                self.assertTrue(copied.exists())
                self.assertEqual(cp["status"], "completed")

                deleted = self.run_job(
                    "fs_delete",
                    {"path": str(copied)},
                    job_id="binary-delete",
                )
                self.assertTrue(deleted["data"]["deleted"])
                self.assertFalse(copied.exists())

    def test_invalid_base64_has_structured_code(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td).resolve()
            target = root / "bad.bin"
            with mock.patch.object(worker, "allowed_roots", return_value=[root]):
                with self.assertRaises(worker.BridgeError) as ctx:
                    self.run_job(
                        "fs_write_binary",
                        {"path": str(target), "content_b64": "%%%"},
                        job_id="invalid-base64",
                    )
        self.assertEqual(ctx.exception.code, "INVALID_BASE64")


if __name__ == "__main__":
    unittest.main()
