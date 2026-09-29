import importlib.util
import json
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
    def test_health_and_system_info_report_elevation_boolean(self):
        health = hb.run_job("health-elevation", {"action": "health", "params": {}}, threading.Event())
        info = hb.run_job("system-elevation", {"action": "system_info", "params": {}}, threading.Event())
        self.assertIsInstance(health["data"]["elevated"], bool)
        self.assertIsInstance(info["data"]["elevated"], bool)
        self.assertEqual(health["data"]["elevated"], hb.is_process_elevated())

    def test_wait_for_file_success_and_soft_timeout(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td).resolve()
            target = root / "ready.flag"
            target.write_text("ok", encoding="utf-8")
            with patch.object(hb, "allowed_roots", return_value=[root]):
                ready = hb.run_job(
                    "wait-file-ready",
                    {
                        "action": "wait_for",
                        "params": {
                            "condition": "file_exists",
                            "path": str(target),
                            "timeout_seconds": 0.2,
                            "interval_ms": 50,
                        },
                    },
                    threading.Event(),
                )
                self.assertTrue(ready["data"]["satisfied"])
                self.assertFalse(ready["data"]["timed_out"])
                self.assertTrue(ready["data"]["observed"]["exists"])

                missing = hb.run_job(
                    "wait-file-missing",
                    {
                        "action": "wait_for",
                        "params": {
                            "condition": "file_exists",
                            "path": str(root / "missing.flag"),
                            "timeout_seconds": 0.05,
                            "interval_ms": 50,
                            "soft_timeout": True,
                        },
                    },
                    threading.Event(),
                )
                self.assertFalse(missing["data"]["satisfied"])
                self.assertTrue(missing["data"]["timed_out"])

    def test_wait_for_honors_cancellation_and_advertises_capability(self):
        event = threading.Event()
        event.set()
        with self.assertRaises(hb.BridgeError) as ctx:
            hb.wait_for_condition(
                {
                    "condition": "session_running",
                    "session_id": "missing-session",
                    "timeout_seconds": 1,
                },
                event,
            )
        self.assertEqual(ctx.exception.code, "WAIT_CANCELLED")

        health = hb.run_job("health-wait", {"action": "health", "params": {}}, threading.Event())
        self.assertIn("wait_for", health["data"]["actions"])

    def test_wait_for_process_exists_reports_identity(self):
        result = hb.run_job(
            "wait-process",
            {
                "action": "wait_for",
                "params": {
                    "condition": "process_exists",
                    "pid": os.getpid(),
                    "timeout_seconds": 0.2,
                    "interval_ms": 50,
                },
            },
            threading.Event(),
        )
        self.assertTrue(result["data"]["satisfied"])
        self.assertTrue(result["data"]["observed"]["exists"])
        self.assertEqual(result["data"]["observed"]["pid"], os.getpid())

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
        self.assertEqual(result["data"]["worker_version"], 6)
        self.assertEqual(result["data"]["protocol"], hb.PROTOCOL)
        for action in (
            "fs_delete", "fs_copy", "fs_read_binary", "fs_write_binary", "job_output_read", "cancel",
            "controller_checkpoint", "display_list", "clipboard_read", "clipboard_write", "app_launch", "desktop_shortcut_create",
            "window_list", "window_focus", "window_move", "window_state", "window_close",
            "gui_cursor_get", "gui_mouse_move", "gui_mouse_button", "gui_mouse_click",
            "gui_mouse_scroll", "gui_key", "gui_type",
            "uia_tree", "uia_find", "uia_focus", "uia_invoke", "uia_set_value",
            "uia_toggle", "uia_select", "uia_expand", "uia_collapse",
        ):
            self.assertIn(action, result["data"]["actions"])
        self.assertTrue(result["data"]["capabilities"]["session_restart_recovery"])
        self.assertEqual(result["data"]["capabilities"]["desktop_control"], os.name == "nt")
        self.assertTrue(result["data"]["capabilities"]["clipboard_relay_requires_opt_in"])
        self.assertEqual(result["data"]["capability_schema"], 2)
        self.assertEqual(result["data"]["features"]["uia"]["backend"], "windows-uia-powershell")
        self.assertFalse(result["data"]["features"]["uia"]["password_values_exposed"])
        self.assertFalse(result["data"]["features"]["secret_input"]["relay_secret_values_allowed"])
        self.assertTrue(result["data"]["features"]["uia"]["set_value_requires_relay_opt_in"])
        self.assertTrue(result["data"]["capabilities"]["uia_set_value_requires_relay_opt_in"])

    def test_secret_channel_is_not_advertised_without_encrypted_inbox(self):
        with tempfile.TemporaryDirectory() as td:
            with patch.dict(
                os.environ,
                {
                    "HEAVEN_BRIDGE_SECRET_INBOX": td,
                    "HEAVEN_BRIDGE_SECRET_INBOX_ENCRYPTED": "0",
                    "COMPUTERNAME": "heaven",
                },
            ):
                result = hb.run_job(
                    "health-secret-off",
                    {"action": "health", "params": {}},
                    threading.Event(),
                )
        self.assertFalse(result["data"]["features"]["secret_input"]["available"])
        self.assertNotIn("gui_type_secret", result["data"]["actions"])

    def test_secret_channel_requires_unc_and_live_encrypted_smb_connection(self):
        with patch.dict(
            os.environ,
            {"HEAVEN_BRIDGE_SECRET_INBOX": r"C:\\local\\secrets", "HEAVEN_BRIDGE_SECRET_INBOX_ENCRYPTED": "1"},
            clear=False,
        ):
            self.assertFalse(hb.secret_channel_status()["available"])

        encrypted = hb.subprocess.CompletedProcess(
            args=[],
            returncode=0,
            stdout='{"ServerName":"heaven2","ShareName":"secrets","Encrypted":true}\n',
            stderr="",
        )
        unencrypted = hb.subprocess.CompletedProcess(
            args=[],
            returncode=0,
            stdout='{"ServerName":"heaven2","ShareName":"secrets","Encrypted":false}\n',
            stderr="",
        )
        unc = r"\\\\heaven2\\secrets"
        with patch.object(hb.Path, "is_dir", return_value=True), \
                patch.object(hb.subprocess, "run", return_value=encrypted):
            self.assertTrue(hb.verify_secret_inbox_transport(unc))
        with patch.object(hb.Path, "is_dir", return_value=True), \
                patch.object(hb.subprocess, "run", return_value=unencrypted):
            self.assertFalse(hb.verify_secret_inbox_transport(unc))

    def test_secret_channel_ignores_legacy_encrypted_env_flag_without_verified_transport(self):
        with patch.dict(
            os.environ,
            {
                "HEAVEN_BRIDGE_SECRET_INBOX": r"\\\\heaven2\\secrets",
                "HEAVEN_BRIDGE_SECRET_INBOX_ENCRYPTED": "1",
            },
            clear=False,
        ), patch.object(hb, "verify_secret_inbox_transport", return_value=False):
            result = hb.secret_channel_status()
        self.assertFalse(result["available"])
        self.assertFalse(result["transport_verified"])

    def test_secret_type_consumes_once_without_relaying_canary(self):
        canary = "CANARY-secret-never-persist-7f2b"
        handle = "a" * 48
        hwnd = 4242
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            inbox = root / "inbox"
            consumed = root / "consumed"
            inbox.mkdir()
            now_utc = datetime.now(timezone.utc)
            envelope = {
                "schema": hb.SECRET_ENVELOPE_SCHEMA,
                "handle": handle,
                "destination": "heaven",
                "purpose": hb.SECRET_PURPOSE,
                "created_at": now_utc.isoformat(),
                "expires_at": (now_utc + timedelta(seconds=120)).isoformat(),
                "target_binding": hb.secret_target_binding(hwnd, "heaven"),
                "value": canary,
            }
            (inbox / f"{handle}.json").write_text(json.dumps(envelope), encoding="utf-8")
            relay_job = {
                "id": "secret-canary-job",
                "source": hb.PROTOCOL,
                "action": "gui_type_secret",
                "params": {"handle": handle, "hwnd": hwnd},
                "created_at": now_utc.isoformat(),
            }
            self.assertNotIn(canary, hb.canonical_job(relay_job).decode("utf-8"))

            env = {
                "HEAVEN_BRIDGE_SECRET_INBOX": str(inbox),
                "HEAVEN_BRIDGE_SECRET_INBOX_ENCRYPTED": "1",
                "COMPUTERNAME": "heaven",
            }
            typed = {}
            with patch.dict(os.environ, env), patch.object(hb, "SECRET_CONSUMED_DIR", consumed), \
                    patch.object(hb, "verify_secret_inbox_transport", return_value=True), \
                    patch.object(hb, "desktop_focus_window", return_value={"focused": True}), \
                    patch.object(hb, "desktop_type_text", side_effect=lambda p: typed.setdefault("value", p["text"]) or {"characters": 0}):
                health = hb.run_job("health-secret-on", {"action": "health", "params": {}}, threading.Event())
                self.assertTrue(health["data"]["features"]["secret_input"]["available"])
                self.assertIn("gui_type_secret", health["data"]["actions"])
                result = hb.run_job("secret-canary-job", relay_job, threading.Event())

                self.assertEqual(result["data"], {"consumed": True, "typed": True})
                self.assertEqual(typed["value"], canary)
                self.assertNotIn(canary, json.dumps(result))
                self.assertFalse((inbox / f"{handle}.json").exists())
                self.assertTrue((consumed / f"{handle}.used").exists())

                # Re-creating the same handle cannot replay it after the local guard exists.
                (inbox / f"{handle}.json").write_text(json.dumps(envelope), encoding="utf-8")
                with self.assertRaises(hb.BridgeError) as replay:
                    hb.run_job("secret-replay-job", {**relay_job, "id": "secret-replay-job"}, threading.Event())
                self.assertEqual(replay.exception.code, "SECRET_REPLAYED")

    def test_secret_envelope_ttl_destination_and_binding_fail_closed(self):
        hwnd = 9191
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            inbox = root / "inbox"
            consumed = root / "consumed"
            inbox.mkdir()
            env = {
                "HEAVEN_BRIDGE_SECRET_INBOX": str(inbox),
                "HEAVEN_BRIDGE_SECRET_INBOX_ENCRYPTED": "1",
                "COMPUTERNAME": "heaven",
            }

            def write_envelope(handle, *, destination="heaven", binding=None, created=None, expires=None):
                created = created or datetime.now(timezone.utc)
                expires = expires or (created + timedelta(seconds=120))
                doc = {
                    "schema": hb.SECRET_ENVELOPE_SCHEMA,
                    "handle": handle,
                    "destination": destination,
                    "purpose": hb.SECRET_PURPOSE,
                    "created_at": created.isoformat(),
                    "expires_at": expires.isoformat(),
                    "target_binding": binding or hb.secret_target_binding(hwnd, destination),
                    "value": "hidden-value",
                }
                (inbox / f"{handle}.json").write_text(json.dumps(doc), encoding="utf-8")

            with patch.dict(os.environ, env), patch.object(hb, "SECRET_CONSUMED_DIR", consumed), \
                    patch.object(hb, "verify_secret_inbox_transport", return_value=True):
                expired_handle = "b" * 48
                created = datetime.now(timezone.utc) - timedelta(minutes=3)
                write_envelope(expired_handle, created=created, expires=created + timedelta(seconds=60))
                with self.assertRaises(hb.BridgeError) as expired:
                    hb.consume_secret_envelope(expired_handle, hwnd)
                self.assertEqual(expired.exception.code, "SECRET_EXPIRED")

                destination_handle = "c" * 48
                write_envelope(destination_handle, destination="heaven2")
                with self.assertRaises(hb.BridgeError) as wrong_destination:
                    hb.consume_secret_envelope(destination_handle, hwnd)
                self.assertEqual(wrong_destination.exception.code, "SECRET_DESTINATION_MISMATCH")

                binding_handle = "d" * 48
                write_envelope(binding_handle, binding="0" * 64)
                with self.assertRaises(hb.BridgeError) as wrong_binding:
                    hb.consume_secret_envelope(binding_handle, hwnd)
                self.assertEqual(wrong_binding.exception.code, "SECRET_TARGET_MISMATCH")

    def test_secret_type_rejects_secret_values_in_relay_params(self):
        with self.assertRaises(hb.BridgeError) as ctx:
            hb.desktop_type_secret({"handle": "e" * 48, "hwnd": 123, "value": "must-not-cross-github"})
        self.assertEqual(ctx.exception.code, "SECRET_RELAY_VALUE_BLOCKED")

    def test_queue_order_prefers_control_then_priority_then_fifo(self):
        current = datetime(2026, 9, 29, 10, 4, tzinfo=timezone.utc)
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            jobs = {
                "normal-old.json": {"action": "proc_run", "priority": "normal", "created_at": "2026-09-29T10:00:00Z"},
                "highest-new.json": {"action": "proc_run", "priority": "highest", "created_at": "2026-09-29T10:02:00Z"},
                "highest-old.json": {"action": "proc_run", "priority": "highest", "created_at": "2026-09-29T10:01:00Z"},
                "control-low.json": {"action": "cancel", "priority": "lowest", "created_at": "2026-09-29T10:03:00Z"},
            }
            paths = []
            for name, job in jobs.items():
                path = root / name
                path.write_text(__import__("json").dumps(job), encoding="utf-8")
                paths.append(path)
            ordered = [
                p.name for p in sorted(paths, key=lambda path: hb.queue_order_key(path, current=current))
            ]
            self.assertEqual(ordered, ["control-low.json", "highest-old.json", "highest-new.json", "normal-old.json"])

    def test_queue_order_malformed_job_does_not_break_sort(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            bad = root / "bad.json"
            bad.write_text("{not-json", encoding="utf-8")
            normal = root / "normal.json"
            normal.write_text('{"action":"proc_run","priority":"normal","created_at":"2026-09-29T10:00:00Z"}', encoding="utf-8")
            ordered = sorted([normal, bad], key=hb.queue_order_key)
            self.assertEqual({p.name for p in ordered}, {"bad.json", "normal.json"})

    def test_queue_order_ages_low_priority_work(self):
        current = datetime(2026, 9, 29, 10, 0, tzinfo=timezone.utc)
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            old_low = root / "old-low.json"
            old_low.write_text(
                __import__("json").dumps({
                    "action": "proc_run",
                    "priority": "low",
                    "created_at": (current - timedelta(minutes=20)).isoformat(),
                }),
                encoding="utf-8",
            )
            fresh_high = root / "fresh-high.json"
            fresh_high.write_text(
                __import__("json").dumps({
                    "action": "proc_run",
                    "priority": "highest",
                    "created_at": (current - timedelta(seconds=1)).isoformat(),
                }),
                encoding="utf-8",
            )
            ordered = sorted(
                [fresh_high, old_low],
                key=lambda path: hb.queue_order_key(path, current=current),
            )
            self.assertEqual(ordered[0].name, "old-low.json")

    def test_queue_order_accepts_numeric_priority(self):
        current = datetime(2026, 9, 29, 10, 0, tzinfo=timezone.utc)
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            urgent = root / "urgent.json"
            urgent.write_text(
                __import__("json").dumps({
                    "action": "proc_run", "priority": 95, "created_at": current.isoformat()
                }),
                encoding="utf-8",
            )
            background = root / "background.json"
            background.write_text(
                __import__("json").dumps({
                    "action": "proc_run", "priority": 10, "created_at": current.isoformat()
                }),
                encoding="utf-8",
            )
            ordered = sorted(
                [background, urgent],
                key=lambda path: hb.queue_order_key(path, current=current),
            )
            self.assertEqual(ordered[0].name, "urgent.json")

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

    def test_desktop_shortcut_create_routes_structured_request(self):
        expected = {"path": r"C:\\Users\\Example\\Desktop\\Agent Control.lnk", "created": True}
        payload = {
            "name": "Agent Control",
            "target": r"C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
            "args": ["-NoProfile"],
            "overwrite": True,
        }
        with patch.object(hb, "desktop_create_shortcut", return_value=expected) as create:
            result = hb.run_job(
                "shortcut-create-test",
                {"action": "desktop_shortcut_create", "params": payload},
                threading.Event(),
            )
        self.assertEqual(result["status"], "completed")
        self.assertEqual(result["data"], expected)
        create.assert_called_once()

    def test_uia_request_validation_and_structured_route(self):
        with self.assertRaises(hb.BridgeError) as missing:
            hb._validate_uia_request({}, "uia_invoke")
        self.assertEqual(missing.exception.code, "UIA_SELECTOR_REQUIRED")
        with self.assertRaises(hb.BridgeError) as unknown:
            hb._validate_uia_request({"selector": {"regex": "unsafe"}}, "uia_find")
        self.assertEqual(unknown.exception.code, "INVALID_UIA_SELECTOR")
        request = hb._validate_uia_request(
            {"selector": {"automation_id": "SaveButton", "control_type": "Button"}, "wait_ms": 999999},
            "uia_invoke",
        )
        self.assertEqual(request["wait_ms"], hb.UIA_MAX_WAIT_MS)
        expected = {"invoked": True, "element": {"automation_id": "SaveButton"}}
        with patch.object(hb, "desktop_uia", return_value=expected) as semantic:
            result = hb.run_job(
                "uia-route-test",
                {"action": "uia_invoke", "params": {"selector": {"automation_id": "SaveButton"}}},
                threading.Event(),
            )
        self.assertEqual(result["data"], expected)
        semantic.assert_called_once()

    def test_uia_bounded_wait_retries_window_discovery(self):
        miss = hb.BridgeError("UIA_WINDOW_NOT_FOUND", "not ready")
        ready = {"count": 1, "items": [{"name": "ready"}]}
        with patch.object(hb, "_require_windows_desktop"), \
             patch.object(hb, "_run_uia_once", side_effect=[miss, ready]) as backend, \
             patch.object(hb.time, "sleep"):
            result = hb.desktop_uia({"title": "Eventually Ready", "wait_ms": 1000}, "uia_find")
        self.assertEqual(result["count"], 1)
        self.assertEqual(backend.call_count, 2)

    def test_uia_set_value_requires_explicit_nonsecret_relay_opt_in(self):
        with self.assertRaises(hb.BridgeError) as ctx:
            hb._validate_uia_request(
                {"selector": {"name": "Input"}, "value": "ordinary-text"},
                "uia_set_value",
            )
        self.assertEqual(ctx.exception.code, "UIA_RELAY_TEXT_OPT_IN_REQUIRED")
        request = hb._validate_uia_request(
            {"selector": {"name": "Input"}, "value": "ordinary-text", "allow_relay_text": True},
            "uia_set_value",
        )
        self.assertEqual(request["value"], "ordinary-text")

    def test_uia_backend_never_reads_password_values(self):
        script = MODULE_PATH.with_name("uia.ps1").read_text(encoding="utf-8")
        self.assertIn("IsPassword", script)
        self.assertIn("UIA_PASSWORD_VALUE_BLOCKED", script)
        self.assertNotIn("Current.Value", script)
        self.assertNotIn("Cached.Value", script)

    def test_uia_set_value_requires_explicit_nonsecret_relay_opt_in(self):
        with self.assertRaises(hb.BridgeError) as blocked:
            hb._validate_uia_request(
                {"selector": {"automation_id": "UserName"}, "value": "ordinary text"},
                "uia_set_value",
            )
        self.assertEqual(blocked.exception.code, "UIA_RELAY_TEXT_OPT_IN_REQUIRED")

        request = hb._validate_uia_request(
            {
                "selector": {"automation_id": "UserName"},
                "value": "ordinary text",
                "allow_relay_text": True,
            },
            "uia_set_value",
        )
        self.assertTrue(request["allow_relay_text"])
        self.assertEqual(request["value"], "ordinary text")

    def test_uia_mutations_fail_closed_when_search_is_truncated(self):
        script = MODULE_PATH.with_name("uia.ps1").read_text(encoding="utf-8")
        self.assertIn("UIA_SEARCH_TRUNCATED", script)
        self.assertIn("$found.truncated -and -not [bool]$request.first_match", script)

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

    def test_local_watchdog_heartbeat_is_network_independent_and_pid_bound(self):
        with tempfile.TemporaryDirectory() as td, \
             patch.object(hb, "LOCAL_HEARTBEAT", Path(td) / "worker-local-heartbeat.json"), \
             patch.object(hb, "current_host", return_value="heaven2"):
            hb.write_local_heartbeat()
            row = json.loads(hb.LOCAL_HEARTBEAT.read_text(encoding="utf-8"))
        self.assertEqual(row["host"], "heaven2")
        self.assertEqual(row["pid"], os.getpid())
        self.assertEqual(row["worker_version"], hb.WORKER_VERSION)
        self.assertEqual(row["protocol"], hb.PROTOCOL)
        self.assertIn("updated_at", row)

    def test_bootstrap_installs_indefinite_worker_and_watchdog_tasks(self):
        bootstrap = MODULE_PATH.with_name("bootstrap.ps1").read_text(encoding="utf-8")
        watchdog = MODULE_PATH.with_name("watchdog.ps1").read_text(encoding="utf-8")
        manage = MODULE_PATH.with_name("manage.ps1").read_text(encoding="utf-8")
        self.assertIn("-RestartCount 255", bootstrap)
        self.assertIn("-ExecutionTimeLimit ([TimeSpan]::Zero)", bootstrap)
        self.assertIn("$WatchdogTaskName = 'Heaven Local Bridge Watchdog'", bootstrap)
        self.assertIn("Register-ScheduledTask", bootstrap)
        self.assertIn("HeavenBridgeWatchdog.vbs", bootstrap)
        self.assertIn("worker-local-heartbeat.json", watchdog)
        self.assertIn("local heartbeat stale", watchdog)
        self.assertNotIn("git -C", watchdog)
        self.assertIn("@('Heaven Local Bridge Watchdog', 'Heaven Local Bridge')", manage)

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
