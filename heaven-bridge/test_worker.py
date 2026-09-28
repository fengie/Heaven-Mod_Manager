import importlib.util
import os
import tempfile
import threading
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import patch

MODULE_PATH = Path(__file__).with_name("worker.py")
spec = importlib.util.spec_from_file_location("heaven_bridge_worker", MODULE_PATH)
hb = importlib.util.module_from_spec(spec)
spec.loader.exec_module(hb)


class HeavenBridgeWorkerTests(unittest.TestCase):
    def test_canonical_hash_ignores_signature_only(self):
        base = {
            "id": "x1",
            "source": hb.PROTOCOL,
            "action": "health",
            "params": {},
            "created_at": datetime.now(timezone.utc).isoformat(),
            "auth": {"signature": "a" * 64, "key_id": "default"},
        }
        other = dict(base)
        other["auth"] = {"signature": "b" * 64, "key_id": "default"}
        self.assertEqual(hb.job_hash(base), hb.job_hash(other))
        changed = dict(base)
        changed["action"] = "system_info"
        self.assertNotEqual(hb.job_hash(base), hb.job_hash(changed))

    def test_ttl_accepts_fresh_and_rejects_expired(self):
        current = datetime(2026, 9, 28, 20, 0, tzinfo=timezone.utc)
        fresh = {"created_at": (current - timedelta(seconds=30)).isoformat(), "ttl_seconds": 60}
        meta = hb.validate_job_time(fresh, current=current)
        self.assertLessEqual(meta["age_seconds"], 30)
        expired = {"created_at": (current - timedelta(seconds=61)).isoformat(), "ttl_seconds": 60}
        with self.assertRaises(hb.BridgeError) as ctx:
            hb.validate_job_time(expired, current=current)
        self.assertEqual(ctx.exception.code, "JOB_EXPIRED")

    def test_ttl_rejects_future_skew(self):
        current = datetime(2026, 9, 28, 20, 0, tzinfo=timezone.utc)
        future = {"created_at": (current + timedelta(seconds=hb.FUTURE_SKEW_SECONDS + 1)).isoformat()}
        with self.assertRaises(hb.BridgeError) as ctx:
            hb.validate_job_time(future, current=current)
        self.assertEqual(ctx.exception.code, "JOB_FROM_FUTURE")

    def test_path_allowlist_and_dangerous_delete(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td).resolve()
            inside = root / "a" / "b.txt"
            inside.parent.mkdir()
            inside.write_text("x", encoding="utf-8")
            self.assertEqual(hb.ensure_allowed(inside, roots=[root]), inside.resolve())
            outside = root.parent / "outside-heaven-bridge-test.txt"
            with self.assertRaises(hb.BridgeError) as ctx:
                hb.ensure_allowed(outside, roots=[root])
            self.assertEqual(ctx.exception.code, "PATH_NOT_ALLOWED")
            self.assertTrue(hb.dangerous_delete_target(root, roots=[root]))
            self.assertFalse(hb.dangerous_delete_target(inside, roots=[root]))

    def test_binary_roundtrip_and_pagination(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td).resolve()
            target = root / "blob.bin"
            with patch.object(hb, "allowed_roots", return_value=[root]):
                first = hb.write_binary(target, "AAECAwQ=", "rewrite")
                self.assertEqual(first["total_bytes"], 5)
                page = hb.read_binary(target, offset=1, length=3)
                self.assertEqual(page["content_b64"], "AQID")
                self.assertEqual(page["next_offset"], 4)
                self.assertFalse(page["eof"])
                last = hb.read_binary(target, offset=4, length=3)
                self.assertTrue(last["eof"])

    def test_copy_and_safe_delete(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td).resolve()
            src = root / "src"
            dst = root / "dst"
            src.mkdir()
            (src / "a.txt").write_text("hello", encoding="utf-8")
            with patch.object(hb, "allowed_roots", return_value=[root]):
                copy_result = hb.run_job(
                    "copy-job",
                    {"action": "fs_copy", "params": {"source": str(src), "destination": str(dst), "recursive": True}},
                    threading.Event(),
                )
                self.assertEqual((dst / "a.txt").read_text(encoding="utf-8"), "hello")
                self.assertEqual(copy_result["status"], "completed")
                delete_result = hb.run_job(
                    "delete-job",
                    {"action": "fs_delete", "params": {"path": str(dst), "recursive": True}},
                    threading.Event(),
                )
                self.assertTrue(delete_result["data"]["deleted"])
                self.assertFalse(dst.exists())
                with self.assertRaises(hb.BridgeError) as ctx:
                    hb.run_job(
                        "danger",
                        {"action": "fs_delete", "params": {"path": str(root), "recursive": True}},
                        threading.Event(),
                    )
                self.assertEqual(ctx.exception.code, "DANGEROUS_DELETE_BLOCKED")

    def test_search_accepts_plural_names_mode(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td).resolve()
            target = root / "heaven_bridge_verify.txt"
            target.write_text("needle", encoding="utf-8")
            with patch.object(hb, "allowed_roots", return_value=[root]):
                data = hb.fs_search({"path": str(root), "pattern": "heaven_bridge_verify", "mode": "names", "glob": "*"})
            self.assertEqual(len(data["results"]), 1)
            self.assertEqual(data["results"][0]["type"], "name")

    def test_secret_like_inline_env_is_blocked(self):
        with self.assertRaises(hb.BridgeError) as ctx:
            hb.build_env({"env": {"API_KEY": "do-not-put-secrets-here"}})
        self.assertEqual(ctx.exception.code, "SECRET_INLINE_ENV_BLOCKED")

    def test_structured_error_shape(self):
        err = hb.BridgeError("EXAMPLE_CODE", "example", {"x": 1})
        body = hb.failure_result(
            "job-1",
            {"action": "health", "source": hb.PROTOCOL, "created_at": datetime.now(timezone.utc).isoformat()},
            err,
            "abc",
        )
        self.assertEqual(body["error"]["code"], "EXAMPLE_CODE")
        self.assertEqual(body["status"], "error")
        self.assertEqual(body["job_hash"], "abc")

    def test_health_capabilities(self):
        result = hb.run_job(
            "health-test",
            {"action": "health", "params": {}},
            threading.Event(),
        )
        self.assertEqual(result["data"]["worker_version"], 3)
        self.assertEqual(result["data"]["protocol"], hb.PROTOCOL)
        for action in ("fs_delete", "fs_copy", "fs_read_binary", "fs_write_binary", "job_output_read", "cancel"):
            self.assertIn(action, result["data"]["actions"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
