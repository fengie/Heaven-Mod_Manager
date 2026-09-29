import importlib.util
import os
import tempfile
import threading
import time
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
        for action in (
            "fs_delete", "fs_copy", "fs_read_binary", "fs_write_binary", "job_output_read", "cancel",
            "controller_checkpoint", "display_list", "clipboard_read", "clipboard_write", "app_launch",
            "window_list", "window_focus", "window_move", "window_state", "window_close",
            "gui_cursor_get", "gui_mouse_move", "gui_mouse_button", "gui_mouse_click",
            "gui_mouse_scroll", "gui_key", "gui_type",
        ):
            self.assertIn(action, result["data"]["actions"])
        self.assertTrue(result["data"]["capabilities"]["session_restart_recovery"])
        self.assertEqual(result["data"]["capabilities"]["desktop_control"], os.name == "nt")
        self.assertTrue(result["data"]["capabilities"]["clipboard_relay_requires_opt_in"])

    def test_clipboard_read_requires_explicit_relay_opt_in(self):
        with self.assertRaises(hb.BridgeError) as ctx:
            hb.run_job(
                "clipboard-guard-test",
                {"action": "clipboard_read", "params": {}},
                threading.Event(),
            )
        self.assertEqual(ctx.exception.code, "CLIPBOARD_RELAY_OPT_IN_REQUIRED")

    def test_structured_desktop_action_routes_without_raw_payload(self):
        expected = {"x": 321, "y": 654}
        with patch.object(hb, "desktop_mouse_move", return_value=expected) as move:
            result = hb.run_job(
                "mouse-move-test",
                {"action": "gui_mouse_move", "params": {"x": 321, "y": 654}},
                threading.Event(),
            )
        self.assertEqual(result["status"], "completed")
        self.assertEqual(result["data"], expected)
        move.assert_called_once_with({"x": 321, "y": 654})

    def test_codex_batch_wrapper_uses_call_arguments(self):
        captured = {}

        def fake_run_capture(job_id, argv, cwd, timeout, stdin=None, cancel_event=None, env=None):
            captured["argv"] = argv
            captured["stdin"] = stdin
            return {"status": "done", "exit_code": 0}

        codex_path = r"C:\\Program Files\\nodejs\\codex.cmd"
        with patch.object(hb, "find_codex", return_value=codex_path), \
             patch.object(hb, "run_capture", side_effect=fake_run_capture):
            result = hb.run_job(
                "codex-wrapper-test",
                {"action": "codex", "payload": "echo test"},
                threading.Event(),
            )

        self.assertEqual(
            captured["argv"],
            ["cmd.exe", "/d", "/s", "/c", "call", codex_path, "exec", "--sandbox", "workspace-write", "--skip-git-repo-check", "-"],
        )
        self.assertEqual(captured["stdin"], "echo test")
        self.assertEqual(result["status"], "done")

    def test_controller_checkpoint_is_stale_safe_and_secret_safe(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            state_path = root / hb.CONTROLLER_STATE_PATH
            state_path.parent.mkdir(parents=True, exist_ok=True)
            state_path.write_text(
                '{"schema":"permanent-dev-controller-v1","cycle_id":"cycle-1"}',
                encoding="utf-8",
            )
            next_state = {
                "schema": "permanent-dev-controller-v1",
                "cycle_id": "cycle-2",
                "selected_task": {"target": "checkpoint durability"},
            }
            published = {}

            def fake_publish(path, body, message, max_attempts=6):
                published["path"] = path
                published["body"] = body
                published["message"] = message

            with patch.object(hb, "ROOT", root), \
                 patch.object(hb, "git_sync"), \
                 patch.object(hb, "publish_json", side_effect=fake_publish):
                result = hb.write_controller_checkpoint(next_state, "cycle-1")
                self.assertTrue(result["persisted"])
                self.assertEqual(result["previous_cycle_id"], "cycle-1")
                self.assertEqual(result["cycle_id"], "cycle-2")
                self.assertEqual(published["path"], hb.CONTROLLER_STATE_PATH)

                with self.assertRaises(hb.BridgeError) as stale:
                    hb.write_controller_checkpoint(next_state, "wrong-cycle")
                self.assertEqual(stale.exception.code, "STALE_CONTROLLER_STATE")

                secret_state = dict(next_state)
                secret_state["credentials"] = {"api_key": "must-not-enter-relay-state"}
                with self.assertRaises(hb.BridgeError) as secret:
                    hb.write_controller_checkpoint(secret_state, "cycle-1")
                self.assertEqual(secret.exception.code, "CONTROLLER_STATE_SECRET_KEY_BLOCKED")

    def test_controller_checkpoint_requires_previous_cycle(self):
        with self.assertRaises(hb.BridgeError) as ctx:
            hb.write_controller_checkpoint(
                {"schema": "permanent-dev-controller-v1", "cycle_id": "cycle-2"},
                "",
            )
        self.assertEqual(ctx.exception.code, "EXPECTED_PREVIOUS_CYCLE_REQUIRED")

    def test_bootstrap_static_verification_precedes_singleton_handoff(self):
        bootstrap = MODULE_PATH.with_name("bootstrap.ps1").read_text(encoding="utf-8")
        compile_idx = bootstrap.index("& $python -m py_compile $staged")
        suite_idx = bootstrap.index("& $python -m unittest -q 'heaven-bridge\\test_worker.py'")
        stop_idx = bootstrap.index("foreach ($oldPid in $oldWorkerIds)")
        start_idx = bootstrap.index("$candidate = Start-Process")
        self.assertLess(compile_idx, suite_idx)
        self.assertLess(suite_idx, stop_idx)
        self.assertLess(stop_idx, start_idx)
        self.assertIn("process-lifetime singleton lock", bootstrap)
        self.assertIn("Copy-Item $BackupWorker $RuntimeWorker -Force", bootstrap)
        self.assertIn("backup worker was restored and restarted", bootstrap)
        self.assertIn("Start-ScheduledTask -TaskName $TaskName", bootstrap)

    def test_bootstrap_preserves_relay_before_destructive_realign(self):
        bootstrap = MODULE_PATH.with_name("bootstrap.ps1").read_text(encoding="utf-8")
        preserve_idx = bootstrap.index("& git -C $RepoRoot branch $backupBranch $head")
        reset_idx = bootstrap.index("Invoke-GitChecked @('reset', '--hard', 'HEAD')")
        self.assertLess(preserve_idx, reset_idx)
        self.assertIn("bootstrap-recovery", bootstrap)
        self.assertIn("status --porcelain --untracked-files=no", bootstrap)
        self.assertIn("rev-list --left-right --count", bootstrap)
        self.assertIn("working-tree.patch", bootstrap)
        self.assertIn("merge', '--ff-only'", bootstrap)
        self.assertIn("$LASTEXITCODE -ne 0", bootstrap)

    def test_worker_instance_lock_is_releasable(self):
        with tempfile.TemporaryDirectory() as td, \
             patch.object(hb, "LOCKS_DIR", Path(td)), \
             patch.object(hb, "WORKER_LOCK_PATH", Path(td) / "worker-instance.lock"):
            handle = hb.acquire_worker_instance_lock()
            try:
                self.assertFalse(handle.closed)
                handle.seek(0)
                self.assertIn(str(os.getpid()), handle.read().decode("utf-8"))
            finally:
                hb.release_worker_instance_lock()
            self.assertTrue(handle.closed)

    def _recovered_session_entry(self, root, sid="session1", pid=4242, token="token-a"):
        started = datetime.now(timezone.utc).isoformat()
        return {
            "proc": None,
            "out_f": None,
            "err_f": None,
            "pid": pid,
            "process_identity": {"pid": pid, "creation_token": token},
            "stdout_path": Path(root) / f"{sid}.out.log",
            "stderr_path": Path(root) / f"{sid}.err.log",
            "shell": "powershell",
            "command": None,
            "cwd": str(root),
            "started_at": started,
            "started_mono": None,
            "last_activity_mono": None,
            "last_activity_at": started,
            "idle_timeout_seconds": 600,
            "max_runtime_seconds": 3600,
            "exit_code": None,
            "recovered": True,
            "stdin_available": False,
        }

    def test_session_metadata_persists_and_recovers_without_command(self):
        class FakeProc:
            pid = 4242
            stdin = object()

            def poll(self):
                return None

        with tempfile.TemporaryDirectory() as td, \
             patch.object(hb, "SESSIONS_DIR", Path(td)), \
             patch.object(hb, "SESSIONS", {}), \
             patch.object(hb.subprocess, "Popen", return_value=FakeProc()), \
             patch.object(hb, "process_identity", return_value={"pid": 4242, "creation_token": "token-a"}):
            snapshot = hb.start_session({
                "shell": "powershell",
                "command": "Write-Output TOP_LEVEL_COMMAND_MUST_NOT_BE_PERSISTED",
                "cwd": td,
            })
            sid = snapshot["session_id"]
            metadata_path = Path(td) / f"{sid}.json"
            row = __import__("json").loads(metadata_path.read_text(encoding="utf-8"))
            self.assertEqual(row["session_id"], sid)
            self.assertEqual(row["process_identity"]["creation_token"], "token-a")
            self.assertNotIn("command", row)
            self.assertNotIn("TOP_LEVEL_COMMAND_MUST_NOT_BE_PERSISTED", metadata_path.read_text(encoding="utf-8"))

            hb._close_session_handles(hb.SESSIONS[sid])
            hb.SESSIONS.clear()
            recovered = hb.recover_sessions()
            self.assertEqual(recovered, {"loaded": 1, "live": 1, "blocked": 0})
            restored = hb.session_snapshot(sid, hb.SESSIONS[sid])
            self.assertTrue(restored["running"])
            self.assertTrue(restored["recovered"])
            self.assertFalse(restored["stdin_available"])
            self.assertIsNone(restored["command"])

    def test_recovered_proc_kill_requires_identity_and_verifies_exit(self):
        with tempfile.TemporaryDirectory() as td:
            sid = "recovered-kill"
            entry = self._recovered_session_entry(td, sid=sid)
            alive = {"value": True}

            def fake_identity(pid):
                if alive["value"]:
                    return {"pid": pid, "creation_token": "token-a"}
                return None

            def fake_kill(pid, force=True):
                alive["value"] = False

            with patch.object(hb, "SESSIONS", {sid: entry}), \
                 patch.object(hb, "SESSIONS_DIR", Path(td)), \
                 patch.object(hb, "process_identity", side_effect=fake_identity), \
                 patch.object(hb, "kill_process_tree", side_effect=fake_kill) as kill:
                result = hb.run_job(
                    "kill-recovered",
                    {"action": "proc_kill", "params": {"session_id": sid, "force": True}},
                    threading.Event(),
                )
            kill.assert_called_once_with(4242, True)
            self.assertFalse(result["data"]["running"])
            self.assertEqual(result["data"]["recovery_state"], "exited")

    def test_recovered_proc_kill_refuses_pid_identity_mismatch(self):
        with tempfile.TemporaryDirectory() as td:
            sid = "recovered-mismatch"
            entry = self._recovered_session_entry(td, sid=sid)
            with patch.object(hb, "SESSIONS", {sid: entry}), \
                 patch.object(hb, "process_identity", return_value={"pid": 4242, "creation_token": "token-b"}), \
                 patch.object(hb, "kill_process_tree") as kill:
                with self.assertRaises(hb.BridgeError) as ctx:
                    hb.run_job(
                        "kill-mismatch",
                        {"action": "proc_kill", "params": {"session_id": sid}},
                        threading.Event(),
                    )
            self.assertEqual(ctx.exception.code, "SESSION_IDENTITY_MISMATCH")
            kill.assert_not_called()

    def test_recovered_proc_input_is_rejected(self):
        with tempfile.TemporaryDirectory() as td:
            sid = "recovered-input"
            entry = self._recovered_session_entry(td, sid=sid)
            with patch.object(hb, "SESSIONS", {sid: entry}):
                with self.assertRaises(hb.BridgeError) as ctx:
                    hb.run_job(
                        "input-recovered",
                        {"action": "proc_input", "params": {"session_id": sid, "input": "hello"}},
                        threading.Event(),
                    )
            self.assertEqual(ctx.exception.code, "SESSION_INPUT_UNAVAILABLE")

    def test_recovered_dead_session_is_not_reported_running(self):
        with tempfile.TemporaryDirectory() as td:
            sid = "recovered-dead"
            entry = self._recovered_session_entry(td, sid=sid)
            with patch.object(hb, "process_identity", return_value=None):
                snapshot = hb.session_snapshot(sid, entry)
            self.assertFalse(snapshot["running"])
            self.assertEqual(snapshot["recovery_state"], "exited")
            self.assertIsNone(snapshot["exit_code"])

    def test_clean_stale_locks_preserves_worker_instance_lock(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            worker_lock = root / "worker-instance.lock"
            stale_job = root / "old-job.lock"
            worker_lock.write_text("worker", encoding="utf-8")
            stale_job.write_text("job", encoding="utf-8")
            old = time.time() - max(hb.MAX_TIMEOUT, hb.DEFAULT_SESSION_MAX, 21600) - 10
            os.utime(worker_lock, (old, old))
            os.utime(stale_job, (old, old))
            with patch.object(hb, "LOCKS_DIR", root), patch.object(hb, "WORKER_LOCK_PATH", worker_lock):
                hb.clean_stale_locks()
            self.assertTrue(worker_lock.exists())
            self.assertFalse(stale_job.exists())

    def test_publish_write_is_serialized_by_git_lock(self):
        wrote = threading.Event()

        class FakeGitResult:
            returncode = 0
            stdout = ""
            stderr = ""

        def fake_write(path, payload):
            wrote.set()

        with tempfile.TemporaryDirectory() as td, \
             patch.object(hb, "ROOT", Path(td)), \
             patch.object(hb, "atomic_write_text", side_effect=fake_write), \
             patch.object(hb, "git", return_value=FakeGitResult()):
            hb.GIT_LOCK.acquire()
            try:
                thread = threading.Thread(
                    target=hb.publish_json,
                    args=("heaven-bridge/status/test.json", {"ok": True}, "test publish"),
                    daemon=True,
                )
                thread.start()
                time.sleep(0.05)
                self.assertFalse(wrote.is_set(), "relay file was written while another publisher held GIT_LOCK")
            finally:
                hb.GIT_LOCK.release()
            thread.join(timeout=1)
            self.assertFalse(thread.is_alive())
            self.assertTrue(wrote.is_set())


if __name__ == "__main__":
    unittest.main(verbosity=2)
