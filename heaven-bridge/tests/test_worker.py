import base64
import json
import shutil
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest import mock
import importlib.util

WORKER_PATH = Path(__file__).resolve().parents[1] / "worker.py"
spec = importlib.util.spec_from_file_location("heaven_bridge_worker", WORKER_PATH)
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class HeavenBridgeWorkerTests(unittest.TestCase):
    def make_job(self, action, params=None, job_id="test-job"):
        return {
            "id": job_id,
            "source": worker.SOURCE,
            "action": action,
            "params": params or {},
            "created_at": datetime.now(timezone.utc).isoformat(),
        }

    def test_health_capabilities(self):
        result = worker.run_job(self.make_job("health"))
        self.assertEqual(result["status"], "completed")
        self.assertEqual(result["data"]["worker_version"], 3)
        self.assertEqual(result["data"]["protocol"], "chatgpt-heaven-bridge-v2")
        for action in ("fs_delete", "fs_copy", "fs_read_binary", "fs_write_binary", "job_output_read"):
            self.assertIn(action, result["data"]["actions"])

    def test_expired_job_rejected(self):
        job = self.make_job("health", job_id="expired-job")
        job["created_at"] = (datetime.now(timezone.utc) - timedelta(days=2)).isoformat()
        job["ttl_seconds"] = 60
        with tempfile.TemporaryDirectory() as td, mock.patch.object(worker, "PROCESSED_DIR", Path(td)):
            with self.assertRaises(worker.BridgeError) as ctx:
                worker.verify_job(job, job["id"])
        self.assertEqual(ctx.exception.code, "JOB_EXPIRED")

    def test_hash_ignores_auth(self):
        job = self.make_job("health")
        a = worker.canonical_job_hash(job)
        job["auth"] = {"hmac_sha256": "different"}
        self.assertEqual(a, worker.canonical_job_hash(job))

    def test_replay_mismatch_rejected(self):
        job = self.make_job("health", job_id="replay-job")
        with tempfile.TemporaryDirectory() as td, mock.patch.object(worker, "PROCESSED_DIR", Path(td)):
            digest = worker.verify_job(job, job["id"])
            worker.mark_processed(job["id"], digest)
            changed = dict(job)
            changed["priority"] = "changed"
            with self.assertRaises(worker.BridgeError) as ctx:
                worker.verify_job(changed, job["id"])
        self.assertEqual(ctx.exception.code, "REPLAY_MISMATCH")

    def test_dangerous_root_delete_rejected(self):
        with self.assertRaises(worker.BridgeError) as ctx:
            worker.ensure_allowed(Path.home(), destructive=True)
        self.assertEqual(ctx.exception.code, "DANGEROUS_PATH")

    def test_binary_copy_delete_roundtrip(self):
        base = Path.home() / "HeavenBridge" / "test-sandbox"
        base.mkdir(parents=True, exist_ok=True)
        td = Path(tempfile.mkdtemp(dir=base))
        try:
            src = td / "source.bin"
            payload = b"\x00\x01heaven-bridge-v3\xff"
            write = worker.run_job(self.make_job("fs_write_binary", {"path": str(src), "base64": base64.b64encode(payload).decode("ascii")}))
            self.assertEqual(write["status"], "completed")
            read = worker.run_job(self.make_job("fs_read_binary", {"path": str(src), "offset_bytes": 0, "length_bytes": 1024}))
            self.assertEqual(base64.b64decode(read["data"]["base64"]), payload)
            copied = td / "copied.bin"
            cp = worker.run_job(self.make_job("fs_copy", {"source": str(src), "destination": str(copied)}))
            self.assertTrue(copied.exists())
            self.assertEqual(cp["status"], "completed")
            deleted = worker.run_job(self.make_job("fs_delete", {"path": str(copied)}))
            self.assertTrue(deleted["data"]["deleted"])
            self.assertFalse(copied.exists())
        finally:
            shutil.rmtree(td, ignore_errors=True)

    def test_invalid_base64_has_structured_code(self):
        with self.assertRaises(worker.BridgeError) as ctx:
            worker.run_job(self.make_job("fs_write_binary", {"path": str(Path.home() / "HeavenBridge" / "bad.bin"), "base64": "%%%"}))
        self.assertEqual(ctx.exception.code, "INVALID_BASE64")


if __name__ == "__main__":
    unittest.main()
