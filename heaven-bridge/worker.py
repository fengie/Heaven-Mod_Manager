import base64
import collections
import concurrent.futures
import ctypes
import hashlib
import hmac
import json
import math
import os
import re
import shutil
import subprocess
import struct
import threading
import time
import uuid
from ctypes import wintypes
from datetime import datetime, timezone
from pathlib import Path

WORKER_VERSION = 7
PROTOCOL = "chatgpt-heaven-bridge-v2"
LEGACY_PROTOCOL = "chatgpt-heaven-bridge-v1"
BRANCH = "heaven-bridge"
LEGACY_DEFAULT_HOST = "heaven"
CONTROL_HOST = "heaven2"
HOST_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,63}$")

def _float_env(name, default, minimum, maximum):
    try:
        value = float(os.environ.get(name, str(default)))
    except (TypeError, ValueError):
        value = float(default)
    return max(float(minimum), min(float(value), float(maximum)))


def _default_worker_limit():
    logical = max(2, int(os.cpu_count() or 4))
    return max(8, min(24, (logical * 3 + 3) // 4))

ROOT = Path(os.environ.get("HEAVEN_BRIDGE_REPO", str(Path.home() / "HeavenBridgeRepo"))).resolve()
QUEUE = ROOT / "heaven-bridge" / "queue"
RESULTS = ROOT / "heaven-bridge" / "results"
STATUS_DIR = ROOT / "heaven-bridge" / "status"

STATE = Path.home() / "HeavenBridge"
LOG = STATE / "worker.log"
AUDIT_LOG = STATE / "audit.jsonl"
SESSIONS_DIR = STATE / "sessions"
OUTPUTS_DIR = STATE / "outputs"
SCREENSHOTS_DIR = STATE / "screenshots"
LOCKS_DIR = STATE / "locks"
CACHE_DIR = STATE / "result-cache"
PROCESSED_LOG = STATE / "processed.jsonl"
WORKER_LOCK_PATH = LOCKS_DIR / "worker-instance.lock"
LOCAL_HEARTBEAT = STATE / "worker-local-heartbeat.json"
LOCAL_PROGRESS = STATE / "worker-loop-progress.json"
CONTROLLER_STATE_PATH = "heaven-bridge/controller/state.json"

MAX_TIMEOUT = int(os.environ.get("HEAVEN_BRIDGE_MAX_TIMEOUT", "7200"))
MAX_OUTPUT_TAIL = int(os.environ.get("HEAVEN_BRIDGE_MAX_OUTPUT_TAIL", "60000"))
MAX_READ_BYTES = int(os.environ.get("HEAVEN_BRIDGE_MAX_READ_BYTES", "2000000"))
MAX_BINARY_CHUNK = int(os.environ.get("HEAVEN_BRIDGE_MAX_BINARY_CHUNK", "1000000"))
MAX_SEARCH_FILE_BYTES = int(os.environ.get("HEAVEN_BRIDGE_MAX_SEARCH_FILE_BYTES", "2000000"))
MAX_RESULTS = int(os.environ.get("HEAVEN_BRIDGE_MAX_RESULTS", "500"))
LOGICAL_CPUS = max(2, int(os.cpu_count() or 4))
AUTO_MAX_WORKERS = _default_worker_limit()
MAX_WORKERS = max(2, min(int(os.environ.get("HEAVEN_BRIDGE_MAX_WORKERS", str(AUTO_MAX_WORKERS))), 32))
MEMORY_RESERVE_GB = _float_env("HEAVEN_BRIDGE_MEMORY_RESERVE_GB", 4.0, 1.0, 64.0)
MEMORY_RESERVE_PERCENT = _float_env("HEAVEN_BRIDGE_MEMORY_RESERVE_PERCENT", 12.0, 0.0, 50.0)
MEMORY_PER_WORKER_GB = _float_env("HEAVEN_BRIDGE_MEMORY_PER_WORKER_GB", 1.25, 0.25, 16.0)
MAX_STARTS_PER_TICK = max(
    1,
    min(
        int(os.environ.get("HEAVEN_BRIDGE_MAX_STARTS_PER_TICK", str(max(2, min(8, MAX_WORKERS // 3))))),
        16,
    ),
)
DEFAULT_JOB_TTL = int(os.environ.get("HEAVEN_BRIDGE_DEFAULT_TTL", "21600"))
MAX_JOB_TTL = int(os.environ.get("HEAVEN_BRIDGE_MAX_TTL", "86400"))
FUTURE_SKEW_SECONDS = int(os.environ.get("HEAVEN_BRIDGE_FUTURE_SKEW", "300"))
HEARTBEAT_SECONDS = max(120, int(os.environ.get("HEAVEN_BRIDGE_HEARTBEAT_SECONDS", "300")))
LOCAL_HEARTBEAT_SECONDS = max(5, min(int(os.environ.get("HEAVEN_BRIDGE_LOCAL_HEARTBEAT_SECONDS", "15")), 60))
RATE_LIMIT_PER_MINUTE = max(10, int(os.environ.get("HEAVEN_BRIDGE_RATE_PER_MINUTE", "60")))
QUEUE_PRIORITY_AGING_SECONDS = max(30, int(os.environ.get("HEAVEN_BRIDGE_PRIORITY_AGING_SECONDS", "300")))
DEFAULT_SESSION_IDLE = int(os.environ.get("HEAVEN_BRIDGE_SESSION_IDLE", "1800"))
DEFAULT_SESSION_MAX = int(os.environ.get("HEAVEN_BRIDGE_SESSION_MAX", "14400"))
SESSION_METADATA_VERSION = 1
SESSION_RETENTION_SECONDS = max(3600, int(os.environ.get("HEAVEN_BRIDGE_SESSION_RETENTION", "86400")))

GIT_LOCK = threading.RLock()
STATE_LOCK = threading.RLock()
RUNNING = {}
SESSIONS = {}
PROCESSED = {}
RECENT_STARTS = collections.deque()
LAST_HEARTBEAT = 0.0


class _MemoryStatusEx(ctypes.Structure):
    _fields_ = [
        ("dwLength", wintypes.DWORD),
        ("dwMemoryLoad", wintypes.DWORD),
        ("ullTotalPhys", ctypes.c_ulonglong),
        ("ullAvailPhys", ctypes.c_ulonglong),
        ("ullTotalPageFile", ctypes.c_ulonglong),
        ("ullAvailPageFile", ctypes.c_ulonglong),
        ("ullTotalVirtual", ctypes.c_ulonglong),
        ("ullAvailVirtual", ctypes.c_ulonglong),
        ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
    ]


def memory_snapshot():
    if os.name == "nt":
        status = _MemoryStatusEx()
        status.dwLength = ctypes.sizeof(_MemoryStatusEx)
        try:
            ok = ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status))
        except Exception:
            ok = 0
        if ok:
            return {
                "total_bytes": int(status.ullTotalPhys),
                "available_bytes": int(status.ullAvailPhys),
                "load_percent": int(status.dwMemoryLoad),
            }

    try:
        page_size = int(os.sysconf("SC_PAGE_SIZE"))
        total_pages = int(os.sysconf("SC_PHYS_PAGES"))
        available_pages = int(os.sysconf("SC_AVPHYS_PAGES"))
        return {
            "total_bytes": page_size * total_pages,
            "available_bytes": page_size * available_pages,
            "load_percent": None,
        }
    except (AttributeError, OSError, TypeError, ValueError):
        return None


def worker_capacity_snapshot(active_noncontrol=0):
    active = max(0, int(active_noncontrol or 0))
    memory = memory_snapshot()
    base = {
        "logical_cpus": LOGICAL_CPUS,
        "auto_max_workers": AUTO_MAX_WORKERS,
        "configured_max_workers": MAX_WORKERS,
        "max_starts_per_tick": MAX_STARTS_PER_TICK,
        "active_noncontrol": active,
        "memory_reserve_gb": MEMORY_RESERVE_GB,
        "memory_reserve_percent": MEMORY_RESERVE_PERCENT,
        "memory_per_worker_gb": MEMORY_PER_WORKER_GB,
    }
    if not memory:
        effective = max(active, MAX_WORKERS)
        return {
            **base,
            "effective_max_workers": effective,
            "available_start_slots": max(0, MAX_WORKERS - active),
            "memory": None,
            "limited_by": "configured-limit",
        }

    gib = float(1024 ** 3)
    total_gb = memory["total_bytes"] / gib
    available_gb = memory["available_bytes"] / gib
    reserve_gb = max(MEMORY_RESERVE_GB, total_gb * MEMORY_RESERVE_PERCENT / 100.0)
    startable_by_memory = max(0, int((available_gb - reserve_gb) // MEMORY_PER_WORKER_GB))
    effective = max(active, min(MAX_WORKERS, active + startable_by_memory))
    available_slots = max(0, effective - active)
    return {
        **base,
        "effective_max_workers": effective,
        "available_start_slots": available_slots,
        "memory": {
            "total_gb": round(total_gb, 2),
            "available_gb": round(available_gb, 2),
            "load_percent": memory.get("load_percent"),
            "effective_reserve_gb": round(reserve_gb, 2),
        },
        "limited_by": "memory-headroom" if effective < MAX_WORKERS else "configured-limit",
    }


HMAC_KEY_ENV = "HEAVEN_BRIDGE_HMAC_KEY"
HMAC_KEY_FILE_ENV = "HEAVEN_BRIDGE_HMAC_KEY_FILE"
ALLOW_REPO_ACL_ONLY_ENV = "HEAVEN_BRIDGE_ALLOW_INSECURE_REPO_ACL_ONLY"
ALLOW_LEGACY_HMAC_ENV = "HEAVEN_BRIDGE_ALLOW_LEGACY_HMAC_CANONICAL"
MIN_HMAC_KEY_BYTES = 32
SENSITIVE_ENV_RE = re.compile(r"(PASS(?:WORD)?|TOKEN|SECRET|HMAC[_-]?KEY|API[_-]?KEY|PRIVATE[_-]?KEY|COOKIE|AUTH|CREDENTIAL|BEARER|CONNECTION[_-]?STRING)", re.I)
CONTROLLER_SECRET_KEY_RE = re.compile(r"(?:^|[_-])(pass(?:word)?|token|secret|api[_-]?key|private[_-]?key|cookie)(?:$|[_-])", re.I)
UIA_MAX_NODES = max(10, min(int(os.environ.get("HEAVEN_BRIDGE_UIA_MAX_NODES", "250")), 1000))
UIA_MAX_DEPTH = max(1, min(int(os.environ.get("HEAVEN_BRIDGE_UIA_MAX_DEPTH", "6")), 12))
UIA_MAX_WAIT_MS = max(0, min(int(os.environ.get("HEAVEN_BRIDGE_UIA_MAX_WAIT_MS", "10000")), 30000))
UIA_ACTIONS = {
    "uia_tree", "uia_find", "uia_focus", "uia_invoke", "uia_set_value",
    "uia_toggle", "uia_select", "uia_expand", "uia_collapse",
}

SECRET_ENVELOPE_SCHEMA = "heaven-secret-envelope-v1"
SECRET_PURPOSE = "gui_type_secret"
SECRET_INBOX_ENV = "HEAVEN_BRIDGE_SECRET_INBOX"
SECRET_MAX_TTL_SECONDS = max(30, min(int(os.environ.get("HEAVEN_BRIDGE_SECRET_MAX_TTL", "300")), 900))
SECRET_MAX_FILE_BYTES = max(1024, min(int(os.environ.get("HEAVEN_BRIDGE_SECRET_MAX_FILE_BYTES", "65536")), 262144))
SECRET_MAX_CHARACTERS = max(1, min(int(os.environ.get("HEAVEN_BRIDGE_SECRET_MAX_CHARACTERS", "10000")), 100000))
SECRET_CONSUMED_DIR = STATE / "secret-consumed"

DIRECT_ACTIONS = {
    "health", "system_info", "job_status", "cancel", "job_output_read", "controller_checkpoint",
    "fs_read", "fs_read_many", "fs_write", "fs_edit", "fs_mkdir",
    "fs_list", "fs_move", "fs_copy", "fs_delete", "fs_info", "fs_search",
    "fs_read_binary", "fs_write_binary",
    "proc_run", "proc_start", "proc_read", "proc_input", "proc_kill",
    "proc_list_sessions", "proc_list", "wait_for", "screenshot", "display_list",
    "clipboard_read", "clipboard_write", "app_launch", "desktop_shortcut_create",
    "window_list", "window_focus", "window_move", "window_state", "window_close",
    "gui_cursor_get", "gui_mouse_move", "gui_mouse_button", "gui_mouse_click", "gui_mouse_scroll", "gui_key", "gui_type", "gui_type_secret",
    *UIA_ACTIONS,
    "powershell", "cmd", "python", "codex",
}
CONTROL_ACTIONS = {
    "health", "system_info", "job_status", "cancel", "controller_checkpoint",
    "proc_read", "proc_input", "proc_kill", "proc_list_sessions",
}
JOB_PRIORITY_RANK = {
    "highest": 0,
    "critical": 0,
    "urgent": 0,
    "high": 1,
    "normal": 2,
    "default": 2,
    "low": 3,
    "lowest": 4,
}

RAW_ACTIONS = {"powershell", "cmd", "python", "codex"}


def current_host():
    raw = os.environ.get("HEAVEN_BRIDGE_HOST") or os.environ.get("COMPUTERNAME") or LEGACY_DEFAULT_HOST
    host = str(raw).strip().casefold()
    if not host or not HOST_RE.fullmatch(host):
        raise BridgeError("INVALID_WORKER_HOST", "worker host identity is invalid", {"host": str(raw)})
    return host


def job_target_host(job):
    raw = job.get("target_host") if isinstance(job, dict) else None
    if raw is None or not str(raw).strip():
        return LEGACY_DEFAULT_HOST
    host = str(raw).strip().casefold()
    if not HOST_RE.fullmatch(host):
        raise BridgeError("INVALID_TARGET_HOST", "target_host must be a simple machine name", {"target_host": str(raw)})
    return host


def job_targets_this_worker(job):
    return job_target_host(job) == current_host()


class BridgeError(Exception):
    def __init__(self, code, message, details=None):
        super().__init__(message)
        self.code = code
        self.message = message
        self.details = details or {}

    def as_dict(self):
        out = {"code": self.code, "message": self.message}
        if self.details:
            out["details"] = self.details
        return out


def now():
    return datetime.now(timezone.utc).isoformat()


def utcnow():
    return datetime.now(timezone.utc)


def is_process_elevated():
    if os.name != "nt":
        return False
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def parse_time(value):
    if not value:
        raise BridgeError("INVALID_CREATED_AT", "created_at is required")
    s = str(value).strip()
    if s.endswith("Z"):
        s = s[:-1] + "+00:00"
    try:
        dt = datetime.fromisoformat(s)
    except ValueError as e:
        raise BridgeError("INVALID_CREATED_AT", "created_at must be ISO-8601") from e
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt.astimezone(timezone.utc)


def log(msg):
    STATE.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as f:
        f.write(f"[{now()}] {msg}\n")


def audit(event, **fields):
    STATE.mkdir(parents=True, exist_ok=True)
    row = {"ts": now(), "event": event, **fields}
    with AUDIT_LOG.open("a", encoding="utf-8") as f:
        f.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n")


def atomic_write_text(path, text):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + f".tmp-{os.getpid()}-{uuid.uuid4().hex[:8]}")
    tmp.write_text(text, encoding="utf-8")
    os.replace(tmp, path)


def atomic_write_bytes(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + f".tmp-{os.getpid()}-{uuid.uuid4().hex[:8]}")
    tmp.write_bytes(data)
    os.replace(tmp, path)


_WORKER_LOCK_HANDLE = None


def acquire_worker_instance_lock():
    global _WORKER_LOCK_HANDLE
    LOCKS_DIR.mkdir(parents=True, exist_ok=True)
    handle = WORKER_LOCK_PATH.open("a+b")
    try:
        handle.seek(0)
        if handle.read(1) == b"":
            handle.seek(0)
            handle.write(b"0")
            handle.flush()
        handle.seek(0)
        if os.name == "nt":
            import msvcrt
            msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError as e:
        handle.close()
        raise BridgeError("WORKER_ALREADY_RUNNING", "another Heaven Bridge worker already holds the instance lock") from e
    handle.seek(0)
    handle.truncate()
    handle.write(f"{os.getpid()} {now()}".encode("utf-8"))
    handle.flush()
    _WORKER_LOCK_HANDLE = handle
    return handle


def release_worker_instance_lock():
    global _WORKER_LOCK_HANDLE
    handle = _WORKER_LOCK_HANDLE
    _WORKER_LOCK_HANDLE = None
    if handle is None:
        return
    try:
        handle.seek(0)
        if os.name == "nt":
            import msvcrt
            msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
        else:
            import fcntl
            fcntl.flock(handle.fileno(), fcntl.LOCK_UN)
    except OSError:
        pass
    finally:
        handle.close()


def git(*args, check=True, timeout=120):
    p = subprocess.run(
        ["git", *args],
        cwd=ROOT,
        capture_output=True,
        text=True,
        timeout=timeout,
        env=safe_process_env(),
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
    if check and p.returncode != 0:
        raise BridgeError("GIT_FAILED", (p.stderr or p.stdout or "git failed")[-4000:], {"args": list(args)})
    return p


def git_sync(max_attempts=5):
    with GIT_LOCK:
        last = None
        for attempt in range(max_attempts):
            try:
                git("fetch", "origin", BRANCH)
                current = git("branch", "--show-current").stdout.strip()
                if current != BRANCH:
                    git("checkout", "-B", BRANCH, f"origin/{BRANCH}")
                p = git("pull", "--rebase", "origin", BRANCH, check=False)
                if p.returncode == 0:
                    return
                git("rebase", "--abort", check=False)
                last = (p.stderr or p.stdout)[-4000:]
            except Exception as e:
                last = repr(e)
                git("rebase", "--abort", check=False)
            time.sleep(min(10, 1.5 ** attempt))
        raise BridgeError("GIT_SYNC_FAILED", "could not synchronize relay", {"last_error": last})


def publish_json(relative_path, body, message, max_attempts=6):
    relative_path = str(relative_path).replace("\\", "/")
    target = ROOT / relative_path
    signed_body = sign_relay_document(body)
    payload = json.dumps(signed_body, indent=2, ensure_ascii=False)
    # Serialize both the working-tree write and all git operations. Writing tracked
    # relay files outside GIT_LOCK lets concurrent publishers create unstaged
    # changes while another thread is rebasing, which can starve result publication.
    with GIT_LOCK:
        atomic_write_text(target, payload)
        last = None
        for attempt in range(max_attempts):
            try:
                # Recover tracked worker-owned relay state left dirty by an interrupted
                # publisher before attempting a rebase. Otherwise one stale status file
                # can make every pull fail while heartbeat commits accumulate locally.
                for owned_path in ("heaven-bridge/status", "heaven-bridge/results", CONTROLLER_STATE_PATH):
                    git("add", "-u", owned_path, check=False)
                git("add", relative_path)
                git("commit", "-m", message, check=False)
                pull = git("pull", "--rebase", "origin", BRANCH, check=False)
                if pull.returncode != 0:
                    git("rebase", "--abort", check=False)
                    last = (pull.stderr or pull.stdout)[-4000:]
                    time.sleep(min(10, 1.5 ** attempt))
                    continue
                pushed = git("push", "origin", BRANCH, check=False)
                if pushed.returncode == 0:
                    return
                last = (pushed.stderr or pushed.stdout)[-4000:]
            except Exception as e:
                last = repr(e)
                git("rebase", "--abort", check=False)
            time.sleep(min(12, 1.5 ** attempt))
    raise BridgeError("RESULT_PUBLISH_FAILED", "could not publish relay JSON", {"path": relative_path, "last_error": last})


def find_sensitive_controller_key(value, path="$"):
    if isinstance(value, dict):
        for key, child in value.items():
            key_text = str(key)
            child_path = f"{path}.{key_text}"
            if CONTROLLER_SECRET_KEY_RE.search(key_text):
                return child_path
            found = find_sensitive_controller_key(child, child_path)
            if found:
                return found
    elif isinstance(value, list):
        for index, child in enumerate(value):
            found = find_sensitive_controller_key(child, f"{path}[{index}]")
            if found:
                return found
    return None


def write_controller_checkpoint(state, expected_previous_cycle_id):
    if not isinstance(state, dict):
        raise BridgeError("INVALID_CONTROLLER_STATE", "params.state must be a JSON object")
    if state.get("schema") != "permanent-dev-controller-v1":
        raise BridgeError("INVALID_CONTROLLER_STATE", "unsupported controller state schema", {"schema": state.get("schema")})
    cycle_id = str(state.get("cycle_id") or "").strip()
    expected = str(expected_previous_cycle_id or "").strip()
    if not cycle_id:
        raise BridgeError("INVALID_CONTROLLER_STATE", "state.cycle_id is required")
    if not expected:
        raise BridgeError("EXPECTED_PREVIOUS_CYCLE_REQUIRED", "params.expected_previous_cycle_id is required")
    sensitive_path = find_sensitive_controller_key(state)
    if sensitive_path:
        raise BridgeError("CONTROLLER_STATE_SECRET_KEY_BLOCKED", "controller state contains a secret-like field name", {"path": sensitive_path})

    # Hold the relay git lock from authoritative refresh through the checkpoint
    # publication so two controllers cannot both pass the same stale-state check.
    with GIT_LOCK:
        git_sync()
        target = ROOT / CONTROLLER_STATE_PATH
        if not target.exists():
            raise BridgeError("CONTROLLER_STATE_MISSING", "controller state file is missing")
        try:
            previous = json.loads(target.read_text(encoding="utf-8-sig"))
        except Exception as e:
            raise BridgeError("CONTROLLER_STATE_INVALID", "existing controller state is not valid JSON", {"error": repr(e)})
        actual = str(previous.get("cycle_id") or "").strip()
        if actual != expected:
            raise BridgeError("STALE_CONTROLLER_STATE", "controller state advanced since this cycle started", {"expected_previous_cycle_id": expected, "actual_cycle_id": actual})
        if cycle_id == actual:
            raise BridgeError("CONTROLLER_CYCLE_NOT_ADVANCED", "new controller state must advance cycle_id", {"cycle_id": cycle_id})
        publish_json(CONTROLLER_STATE_PATH, state, f"heaven bridge controller checkpoint {cycle_id}")
    return {"path": CONTROLLER_STATE_PATH, "previous_cycle_id": actual, "cycle_id": cycle_id, "persisted": True}


def safe_id(name):
    return bool(re.fullmatch(r"[A-Za-z0-9._-]{1,120}", str(name)))


def _canonical_utf8(value):
    # Match WHATWG/Node UTF-8 encoding semantics: combine valid surrogate pairs and
    # replace lone surrogate code points with U+FFFD before encoding.
    text = str(value)
    chars = []
    index = 0
    while index < len(text):
        code = ord(text[index])
        if 0xD800 <= code <= 0xDBFF and index + 1 < len(text):
            low = ord(text[index + 1])
            if 0xDC00 <= low <= 0xDFFF:
                scalar = 0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00)
                chars.append(chr(scalar))
                index += 2
                continue
        if 0xD800 <= code <= 0xDFFF:
            chars.append("\uFFFD")
        else:
            chars.append(text[index])
        index += 1
    return "".join(chars).encode("utf-8")


def _canonical_json_value(value):
    if value is None:
        return ["n"]
    if isinstance(value, bool):
        return ["b", 1 if value else 0]
    if isinstance(value, str):
        return ["s", base64.b64encode(_canonical_utf8(value)).decode("ascii")]
    if isinstance(value, int):
        if abs(value) <= 9007199254740991:
            return ["i", str(value)]
        numeric = float(value)
        if not math.isfinite(numeric):
            raise BridgeError("AUTH_CANONICAL_NUMBER", "bridge job contains a numeric value outside binary64 range")
        return ["f", struct.pack(">d", numeric).hex()]
    if isinstance(value, float):
        if not math.isfinite(value):
            raise BridgeError("AUTH_CANONICAL_NUMBER", "bridge job contains a non-finite numeric value")
        if value.is_integer() and abs(value) <= 9007199254740991:
            return ["i", str(int(value))]
        return ["f", struct.pack(">d", value).hex()]
    if isinstance(value, list):
        return ["a", [_canonical_json_value(item) for item in value]]
    if isinstance(value, dict):
        rows = []
        for key in sorted(value.keys(), key=lambda item: _canonical_utf8(item)):
            key_bytes = _canonical_utf8(key)
            rows.append([
                base64.b64encode(key_bytes).decode("ascii"),
                _canonical_json_value(value[key]),
            ])
        return ["o", rows]
    raise BridgeError("AUTH_CANONICAL_TYPE", f"bridge job contains unsupported JSON type: {type(value).__name__}")


def canonical_auth_job_v1(job):
    copy = json.loads(json.dumps(job))
    auth = copy.get("auth")
    if isinstance(auth, dict):
        auth.pop("signature", None)
        auth.pop("canonical", None)
        if not auth:
            copy.pop("auth", None)
    canonical = ["mhw-bridge-canon-v1", _canonical_json_value(copy)]
    return json.dumps(canonical, separators=(",", ":"), ensure_ascii=True).encode("ascii")

def sign_relay_document(document):
    body = json.loads(json.dumps(document))
    key = load_hmac_key()
    if not key:
        return body
    auth = body.get("auth") if isinstance(body.get("auth"), dict) else {}
    auth.pop("signature", None)
    auth["canonical"] = "mhw-bridge-canon-v1"
    body["auth"] = auth
    signature = hmac.new(key.encode("utf-8"), canonical_auth_job_v1(body), hashlib.sha256).hexdigest()
    body["auth"]["signature"] = signature
    return body


def canonical_job(job):
    # Legacy canonicalization is retained for replay hashes and pre-v1 HMAC clients.
    copy = json.loads(json.dumps(job))
    auth = copy.get("auth")
    if isinstance(auth, dict):
        auth.pop("signature", None)
        if not auth:
            copy.pop("auth", None)
    return json.dumps(copy, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def job_hash(job):
    return hashlib.sha256(canonical_job(job)).hexdigest()


def validate_job_time(job, current=None):
    current = current or utcnow()
    created = parse_time(job.get("created_at"))
    ttl = int(job.get("ttl_seconds") or DEFAULT_JOB_TTL)
    ttl = max(30, min(ttl, MAX_JOB_TTL))
    age = (current - created).total_seconds()
    if age > ttl:
        raise BridgeError("JOB_EXPIRED", "job exceeded its TTL", {"age_seconds": int(age), "ttl_seconds": ttl})
    if age < -FUTURE_SKEW_SECONDS:
        raise BridgeError("JOB_FROM_FUTURE", "job created_at is too far in the future", {"skew_seconds": int(-age)})
    return {"created_at": created.isoformat(), "ttl_seconds": ttl, "age_seconds": int(age)}


def env_flag(name):
    return str(os.environ.get(name) or "").strip().casefold() in {"1", "true", "yes", "on"}


def hmac_key_path():
    configured = str(os.environ.get(HMAC_KEY_FILE_ENV) or "").strip()
    if configured:
        return Path(os.path.expandvars(os.path.expanduser(configured))).resolve()
    return (STATE / "auth" / "hmac.key").resolve()


def load_hmac_key():
    inline = os.environ.get(HMAC_KEY_ENV)
    if inline:
        return validate_hmac_key_strength(inline, HMAC_KEY_ENV)
    path = hmac_key_path()
    try:
        value = path.read_text(encoding="utf-8").strip()
    except FileNotFoundError:
        return ""
    except OSError as exc:
        raise BridgeError("AUTH_KEY_UNREADABLE", "HMAC key file exists but cannot be read", {"path": str(path)}) from exc
    if not value:
        raise BridgeError("AUTH_KEY_EMPTY", "HMAC key file is empty", {"path": str(path)})
    if len(value.encode("utf-8")) < MIN_HMAC_KEY_BYTES:
        raise BridgeError(
            "AUTH_KEY_WEAK",
            f"HMAC key must be at least {MIN_HMAC_KEY_BYTES} UTF-8 bytes",
            {"path": str(path), "minimum_bytes": MIN_HMAC_KEY_BYTES},
        )
    return value


def validate_hmac_key_strength(value, source="environment"):
    key = str(value or "")
    if key and len(key.encode("utf-8")) < MIN_HMAC_KEY_BYTES:
        raise BridgeError(
            "AUTH_KEY_WEAK",
            f"HMAC key must be at least {MIN_HMAC_KEY_BYTES} UTF-8 bytes",
            {"source": source, "minimum_bytes": MIN_HMAC_KEY_BYTES},
        )
    return key


def auth_mode():
    if load_hmac_key():
        return "hmac-sha256"
    if env_flag(ALLOW_REPO_ACL_ONLY_ENV):
        return "private-repo-acl-explicit-insecure"
    return "hmac-required"


def verify_auth(job):
    key = load_hmac_key()
    if not key:
        if env_flag(ALLOW_REPO_ACL_ONLY_ENV):
            return {"mode": "private-repo-acl-explicit-insecure", "verified": True}
        raise BridgeError(
            "AUTH_HMAC_NOT_CONFIGURED",
            "Heaven Bridge HMAC authentication is required; unsigned repository-relay execution is disabled by default",
        )
    auth = job.get("auth") if isinstance(job.get("auth"), dict) else {}
    signature = str(auth.get("signature") or "")
    if not re.fullmatch(r"[0-9a-fA-F]{64}", signature):
        raise BridgeError("AUTH_REQUIRED", "HMAC signature is required")
    canonical_version = str(auth.get("canonical") or "").strip()
    if canonical_version:
        if canonical_version != "mhw-bridge-canon-v1":
            raise BridgeError("AUTH_CANONICAL_UNSUPPORTED", "unsupported HMAC canonical format", {"canonical": canonical_version})
        payload = canonical_auth_job_v1(job)
    else:
        if not env_flag(ALLOW_LEGACY_HMAC_ENV):
            raise BridgeError(
                "AUTH_CANONICAL_REQUIRED",
                "versioned HMAC canonicalization is required; legacy JSON signing needs an explicit emergency compatibility opt-in",
            )
        payload = canonical_job(job)
    expected = hmac.new(key.encode("utf-8"), payload, hashlib.sha256).hexdigest()
    if not hmac.compare_digest(signature.lower(), expected.lower()):
        raise BridgeError("AUTH_INVALID", "HMAC signature verification failed")
    return {
        "mode": "hmac-sha256",
        "verified": True,
        "canonical": canonical_version or "legacy-json-sort",
    }


def load_processed():
    PROCESSED.clear()
    if not PROCESSED_LOG.exists():
        return
    try:
        for line in PROCESSED_LOG.read_text(encoding="utf-8", errors="ignore").splitlines():
            try:
                row = json.loads(line)
                if safe_id(row.get("id")) and isinstance(row.get("hash"), str):
                    PROCESSED[row["id"]] = row
            except Exception:
                continue
    except OSError:
        pass


def mark_processed(job_id, digest, status, cache_path):
    row = {"id": job_id, "hash": digest, "status": status, "cache_path": str(cache_path), "finished_at": now()}
    with STATE_LOCK:
        PROCESSED[job_id] = row
        PROCESSED_LOG.parent.mkdir(parents=True, exist_ok=True)
        with PROCESSED_LOG.open("a", encoding="utf-8") as f:
            f.write(json.dumps(row, separators=(",", ":")) + "\n")


def allowed_roots():
    roots = [Path.home().resolve(), ROOT]
    services = Path(r"C:\HeavenServices")
    if services.exists():
        roots.append(services.resolve())
    extra = os.environ.get("HEAVEN_BRIDGE_ALLOWED_ROOTS", "")
    for raw in extra.split(os.pathsep):
        raw = raw.strip()
        if raw:
            try:
                roots.append(Path(os.path.expandvars(os.path.expanduser(raw))).resolve())
            except OSError:
                pass
    unique = []
    seen = set()
    for p in roots:
        key = os.path.normcase(str(p))
        if key not in seen:
            seen.add(key)
            unique.append(p)
    return unique


def path_is_within(path, root):
    try:
        return os.path.commonpath([os.path.normcase(str(path)), os.path.normcase(str(root))]) == os.path.normcase(str(root))
    except ValueError:
        return False


def ensure_allowed(path, roots=None):
    path = Path(path).resolve()
    roots = roots or allowed_roots()
    if not any(path_is_within(path, root) for root in roots):
        raise BridgeError("PATH_NOT_ALLOWED", "path is outside configured filesystem roots", {"path": str(path)})
    return path


def expand_path(value, check_allowed=False):
    if value is None or str(value).strip() == "":
        p = Path.home()
    else:
        p = Path(os.path.expandvars(os.path.expanduser(str(value))))
    p = p.resolve()
    return ensure_allowed(p) if check_allowed else p


def dangerous_delete_target(path, roots=None):
    path = Path(path).resolve()
    roots = roots or allowed_roots()
    forbidden = {os.path.normcase(str(Path.home().resolve())), os.path.normcase(str(ROOT.resolve())), os.path.normcase(str(STATE.resolve()))}
    for root in roots:
        forbidden.add(os.path.normcase(str(Path(root).resolve())))
    anchor = Path(path.anchor) if path.anchor else None
    if anchor:
        forbidden.add(os.path.normcase(str(anchor.resolve())))
    return os.path.normcase(str(path)) in forbidden


def params(job):
    value = job.get("params")
    return value if isinstance(value, dict) else {}


def cap_text(value, limit=MAX_OUTPUT_TAIL):
    text = "" if value is None else str(value)
    return text if len(text) <= limit else text[-limit:]


def safe_process_env():
    return {
        str(key): str(value)
        for key, value in os.environ.items()
        if not SENSITIVE_ENV_RE.search(str(key))
    }


def build_env(p):
    env = safe_process_env()
    requested = p.get("env_from_host") or []
    if requested:
        if not isinstance(requested, list):
            raise BridgeError("INVALID_ENV", "env_from_host must be a list of variable names")
        selected = {}
        for name in requested[:100]:
            name = str(name)
            if SENSITIVE_ENV_RE.search(name):
                raise BridgeError("SENSITIVE_HOST_ENV_BLOCKED", f"secret-like host environment variable is blocked: {name}")
            if name in os.environ:
                selected[name] = os.environ[name]
        env.update(selected)
    inline = p.get("env") or {}
    if inline:
        if not isinstance(inline, dict):
            raise BridgeError("INVALID_ENV", "env must be an object")
        for k, v in inline.items():
            k = str(k)
            if SENSITIVE_ENV_RE.search(k):
                raise BridgeError("SECRET_INLINE_ENV_BLOCKED", f"inline secret-like environment variable is blocked: {k}")
            env[k] = str(v)
    return env


CONSOLE_SHELL_NAMES = frozenset({
    "cmd", "cmd.exe",
    "powershell", "powershell.exe",
    "pwsh", "pwsh.exe",
})


def console_launch_policy(target, requested_visible_console=False):
    name = Path(str(target or "")).name.casefold()
    is_console_shell = name in CONSOLE_SHELL_NAMES
    requested = bool(requested_visible_console)
    return {
        "visible_console": requested and not is_console_shell,
        "visible_console_suppressed": requested and is_console_shell,
        "is_console_shell": is_console_shell,
    }


def shell_argv(shell, command):
    shell = (shell or "powershell").lower()
    if shell == "powershell":
        return ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command]
    if shell == "cmd":
        return ["cmd.exe", "/d", "/s", "/c", command]
    if shell == "python":
        return ["python.exe", "-c", command]
    raise BridgeError("UNSUPPORTED_SHELL", f"unsupported shell: {shell}")


def find_codex():
    candidates = [
        Path.home() / "AppData/Roaming/npm/codex.cmd",
        Path.home() / "AppData/Roaming/npm/codex.exe",
    ]
    for p in candidates:
        if p.exists():
            return str(p)
    found = shutil.which("codex")
    if found:
        return found
    raise BridgeError("CODEX_NOT_FOUND", "Codex CLI was not found in worker PATH or standard user npm location")


def kill_process_tree(pid, force=True):
    if os.name == "nt":
        args = ["taskkill", "/PID", str(pid), "/T"]
        if force:
            args.append("/F")
        subprocess.run(
            args, capture_output=True, text=True, timeout=30,
            env=safe_process_env(),
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        return
    try:
        os.kill(pid, 9 if force else 15)
    except ProcessLookupError:
        pass


def read_text_lines(path, offset=0, length=1000):
    path = ensure_allowed(path)
    raw = path.read_bytes()
    if len(raw) > MAX_READ_BYTES:
        raise BridgeError("FILE_TOO_LARGE", f"file exceeds {MAX_READ_BYTES} byte direct text-read limit", {"size": len(raw)})
    text = raw.decode("utf-8", errors="replace")
    lines = text.splitlines()
    total = len(lines)
    offset = int(offset or 0)
    length = max(1, min(int(length or 1000), 5000))
    start = max(0, total + offset) if offset < 0 else min(offset, total)
    chunk = lines[start:start + length]
    next_offset = start + len(chunk)
    return {
        "path": str(path), "offset": start, "length": len(chunk), "total_lines": total,
        "next_offset": next_offset, "eof": next_offset >= total, "content": "\n".join(chunk),
    }


def read_binary(path, offset=0, length=MAX_BINARY_CHUNK):
    path = ensure_allowed(path)
    size = path.stat().st_size
    offset = max(0, min(int(offset or 0), size))
    length = max(1, min(int(length or MAX_BINARY_CHUNK), MAX_BINARY_CHUNK))
    with path.open("rb") as f:
        f.seek(offset)
        data = f.read(length)
    next_offset = offset + len(data)
    return {
        "path": str(path), "offset": offset, "length": len(data), "total_bytes": size,
        "next_offset": next_offset, "eof": next_offset >= size,
        "content_b64": base64.b64encode(data).decode("ascii"),
    }


def write_binary(path, content_b64, mode="rewrite"):
    path = ensure_allowed(path)
    try:
        data = base64.b64decode(str(content_b64), validate=True)
    except Exception as e:
        raise BridgeError("INVALID_BASE64", "content_b64 is not valid base64") from e
    if len(data) > MAX_BINARY_CHUNK:
        raise BridgeError("BINARY_CHUNK_TOO_LARGE", "binary chunk exceeds configured maximum", {"max_bytes": MAX_BINARY_CHUNK})
    path.parent.mkdir(parents=True, exist_ok=True)
    mode = str(mode or "rewrite").lower()
    if mode not in ("rewrite", "append"):
        raise BridgeError("INVALID_MODE", "mode must be rewrite or append")
    with path.open("ab" if mode == "append" else "wb") as f:
        f.write(data)
    return {"path": str(path), "bytes_written": len(data), "total_bytes": path.stat().st_size, "mode": mode}


def fs_list(root, depth=2, max_items=1000, offset=0):
    root = ensure_allowed(root)
    depth = max(1, min(int(depth or 2), 8))
    max_items = max(1, min(int(max_items or 1000), 5000))
    offset = max(0, int(offset or 0))
    items = []
    base_parts = len(root.parts)
    for current, dirs, files in os.walk(root):
        cur = Path(current)
        cur_depth = len(cur.parts) - base_parts
        if cur_depth >= depth:
            dirs[:] = []
        for name in sorted(dirs):
            items.append({"type": "dir", "path": str(cur / name)})
        for name in sorted(files):
            p = cur / name
            try:
                st = p.stat()
                items.append({"type": "file", "path": str(p), "size": st.st_size,
                              "modified": datetime.fromtimestamp(st.st_mtime, timezone.utc).isoformat()})
            except OSError:
                items.append({"type": "file", "path": str(p)})
        if len(items) >= offset + max_items + 1:
            break
    page = items[offset:offset + max_items]
    return {"root": str(root), "offset": offset, "items": page,
            "next_offset": offset + len(page), "has_more": len(items) > offset + len(page)}


def fs_search(p):
    root = ensure_allowed(expand_path(p.get("path")))
    pattern = str(p.get("pattern") or "")
    if not pattern:
        raise BridgeError("INVALID_SEARCH", "pattern is required")
    mode = str(p.get("mode") or "both").lower()
    aliases = {"name": "name", "names": "name", "content": "content", "contents": "content", "both": "both"}
    if mode not in aliases:
        raise BridgeError("INVALID_SEARCH_MODE", "mode must be name/names, content/contents, or both")
    mode = aliases[mode]
    use_regex = bool(p.get("regex", False))
    case_sensitive = bool(p.get("case_sensitive", False))
    max_results = max(1, min(int(p.get("max_results") or 100), MAX_RESULTS))
    glob = str(p.get("glob") or "*")
    flags = 0 if case_sensitive else re.IGNORECASE
    try:
        rx = re.compile(pattern, flags) if use_regex else None
    except re.error as e:
        raise BridgeError("INVALID_REGEX", str(e)) from e
    needle = pattern if case_sensitive else pattern.lower()
    results = []
    scanned = 0
    for file in root.rglob(glob):
        if len(results) >= max_results:
            break
        if file.is_dir():
            continue
        scanned += 1
        rel = str(file.relative_to(root))
        hay = rel if case_sensitive else rel.lower()
        name_match = bool(rx.search(rel) if rx else needle in hay)
        if mode in ("name", "both") and name_match:
            results.append({"type": "name", "path": str(file)})
            if len(results) >= max_results:
                break
        if mode not in ("content", "both"):
            continue
        try:
            if file.stat().st_size > MAX_SEARCH_FILE_BYTES:
                continue
            with file.open("r", encoding="utf-8", errors="ignore") as f:
                for line_no, line in enumerate(f, 1):
                    target = line if case_sensitive else line.lower()
                    matched = bool(rx.search(line) if rx else needle in target)
                    if matched:
                        results.append({"type": "content", "path": str(file), "line": line_no, "text": line.rstrip()[:1200]})
                        if len(results) >= max_results:
                            break
        except (OSError, UnicodeError):
            pass
    return {"root": str(root), "results": results, "scanned_files": scanned, "truncated": len(results) >= max_results}


def output_paths(job_id):
    OUTPUTS_DIR.mkdir(parents=True, exist_ok=True)
    return OUTPUTS_DIR / f"{job_id}.stdout.log", OUTPUTS_DIR / f"{job_id}.stderr.log"


def run_capture(job_id, argv, cwd, timeout, stdin=None, cancel_event=None, env=None):
    stdout_path, stderr_path = output_paths(job_id)
    started = now()
    flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
    if os.name == "nt":
        flags |= getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    with stdout_path.open("wb") as out_f, stderr_path.open("wb") as err_f:
        proc = subprocess.Popen(
            argv, cwd=str(cwd), stdin=subprocess.PIPE if stdin is not None else None,
            stdout=out_f, stderr=err_f, env=env if env is not None else safe_process_env(), creationflags=flags,
        )
        if stdin is not None and proc.stdin:
            data = stdin.encode("utf-8")
            proc.stdin.write(data)
            proc.stdin.close()
        deadline = time.monotonic() + timeout
        cancelled = False
        timed_out = False
        while proc.poll() is None:
            if cancel_event is not None and cancel_event.is_set():
                cancelled = True
                kill_process_tree(proc.pid, True)
                break
            if time.monotonic() >= deadline:
                timed_out = True
                kill_process_tree(proc.pid, True)
                break
            time.sleep(0.2)
        try:
            code = proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            kill_process_tree(proc.pid, True)
            code = 124
    stdout = read_tail(stdout_path, MAX_OUTPUT_TAIL)
    stderr = read_tail(stderr_path, MAX_OUTPUT_TAIL)
    if cancelled:
        status, code = "cancelled", 130
    elif timed_out:
        status, code = "timeout", 124
    else:
        status = "done" if code == 0 else "failed"
    return {
        "status": status, "exit_code": code, "started_at": started, "finished_at": now(),
        "stdout": stdout, "stderr": stderr,
        "output": {
            "stdout_path": str(stdout_path), "stderr_path": str(stderr_path),
            "stdout_bytes": stdout_path.stat().st_size if stdout_path.exists() else 0,
            "stderr_bytes": stderr_path.stat().st_size if stderr_path.exists() else 0,
            "tail_chars": MAX_OUTPUT_TAIL,
        },
    }


def read_tail(path, max_chars=MAX_OUTPUT_TAIL):
    try:
        data = Path(path).read_bytes()
        if len(data) > max_chars * 3:
            data = data[-max_chars * 3:]
        return data.decode("utf-8", errors="replace")[-max_chars:]
    except OSError:
        return ""


def read_output_chunk(job_id, stream="stdout", offset=0, length=200000):
    if not safe_id(job_id):
        raise BridgeError("INVALID_JOB_ID", "invalid job id")
    if stream not in ("stdout", "stderr"):
        raise BridgeError("INVALID_STREAM", "stream must be stdout or stderr")
    stdout_path, stderr_path = output_paths(job_id)
    path = stdout_path if stream == "stdout" else stderr_path
    if not path.exists():
        raise BridgeError("OUTPUT_NOT_FOUND", "output stream not found", {"job_id": job_id, "stream": stream})
    size = path.stat().st_size
    offset = max(0, min(int(offset or 0), size))
    length = max(1, min(int(length or 200000), 500000))
    with path.open("rb") as f:
        f.seek(offset)
        data = f.read(length)
    next_offset = offset + len(data)
    return {
        "job_id": job_id, "stream": stream, "offset": offset, "length": len(data), "total_bytes": size,
        "next_offset": next_offset, "eof": next_offset >= size,
        "content": data.decode("utf-8", errors="replace"),
    }


def _session_time(value, code="INVALID_SESSION_METADATA"):
    if not value:
        raise BridgeError(code, "session timestamp is required")
    text = str(value).strip()
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    try:
        value = datetime.fromisoformat(text)
    except ValueError as e:
        raise BridgeError(code, "session timestamp must be ISO-8601") from e
    if value.tzinfo is None:
        value = value.replace(tzinfo=timezone.utc)
    return value.astimezone(timezone.utc)


def _wall_elapsed(value):
    try:
        return max(0.0, (utcnow() - _session_time(value)).total_seconds())
    except BridgeError:
        return float("inf")


def session_metadata_path(sid):
    if not safe_id(sid):
        raise BridgeError("INVALID_SESSION_ID", "invalid session_id")
    return SESSIONS_DIR / f"{sid}.json"


def process_identity(pid):
    pid = int(pid)
    if pid <= 0:
        return None
    if os.name == "nt":
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        open_process = kernel32.OpenProcess
        open_process.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        open_process.restype = wintypes.HANDLE
        get_process_times = kernel32.GetProcessTimes
        get_process_times.argtypes = [
            wintypes.HANDLE,
            ctypes.POINTER(wintypes.FILETIME),
            ctypes.POINTER(wintypes.FILETIME),
            ctypes.POINTER(wintypes.FILETIME),
            ctypes.POINTER(wintypes.FILETIME),
        ]
        get_process_times.restype = wintypes.BOOL
        close_handle = kernel32.CloseHandle
        close_handle.argtypes = [wintypes.HANDLE]
        close_handle.restype = wintypes.BOOL

        handle = open_process(0x1000, False, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
        if not handle:
            err = ctypes.get_last_error()
            if err in (87, 1168):  # invalid parameter / not found: process is gone
                return None
            raise BridgeError(
                "SESSION_IDENTITY_UNAVAILABLE",
                "unable to query session process identity",
                {"pid": pid, "winerror": err},
            )
        try:
            creation = wintypes.FILETIME()
            exit_time = wintypes.FILETIME()
            kernel = wintypes.FILETIME()
            user = wintypes.FILETIME()
            if not get_process_times(handle, creation, exit_time, kernel, user):
                err = ctypes.get_last_error()
                if err in (6, 87, 1168):
                    return None
                raise BridgeError(
                    "SESSION_IDENTITY_UNAVAILABLE",
                    "unable to query session process creation time",
                    {"pid": pid, "winerror": err},
                )
            token = (int(creation.dwHighDateTime) << 32) | int(creation.dwLowDateTime)
            return {"pid": pid, "creation_token": f"win-filetime:{token}"}
        finally:
            close_handle(handle)

    stat_path = Path(f"/proc/{pid}/stat")
    try:
        raw = stat_path.read_text(encoding="utf-8")
    except FileNotFoundError:
        return None
    except OSError as e:
        raise BridgeError(
            "SESSION_IDENTITY_UNAVAILABLE",
            "unable to query session process identity",
            {"pid": pid, "error": str(e)},
        ) from e
    end_comm = raw.rfind(")")
    fields = raw[end_comm + 2:].split() if end_comm >= 0 else []
    if len(fields) <= 19:
        raise BridgeError("SESSION_IDENTITY_UNAVAILABLE", "process identity record is malformed", {"pid": pid})
    return {"pid": pid, "creation_token": f"procfs-starttime:{fields[19]}"}


def process_identity_matches(expected, current):
    if not isinstance(expected, dict) or not isinstance(current, dict):
        return False
    return (
        int(expected.get("pid") or -1) == int(current.get("pid") or -2)
        and str(expected.get("creation_token") or "") != ""
        and str(expected.get("creation_token")) == str(current.get("creation_token"))
    )


def session_process_state(s):
    proc = s.get("proc")
    if proc is not None:
        code = proc.poll()
        return {
            "running": code is None,
            "exit_code": code,
            "recovery_state": "local-live" if code is None else "exited",
        }

    pid = int(s.get("pid") or 0)
    expected = s.get("process_identity")
    if not expected:
        return {
            "running": False,
            "exit_code": s.get("exit_code"),
            "recovery_state": "identity-unavailable",
        }
    try:
        current = process_identity(pid)
    except BridgeError as e:
        return {
            "running": False,
            "exit_code": s.get("exit_code"),
            "recovery_state": "identity-unproven",
            "identity_error": e.code,
        }
    if current is None:
        return {"running": False, "exit_code": s.get("exit_code"), "recovery_state": "exited"}
    if not process_identity_matches(expected, current):
        return {
            "running": False,
            "exit_code": s.get("exit_code"),
            "recovery_state": "identity-mismatch",
        }
    return {"running": True, "exit_code": None, "recovery_state": "recovered-live"}


def _session_idle_seconds(s):
    if s.get("last_activity_mono") is not None:
        return max(0.0, time.monotonic() - s["last_activity_mono"])
    return _wall_elapsed(s.get("last_activity_at"))


def _session_runtime_seconds(s):
    if s.get("started_mono") is not None:
        return max(0.0, time.monotonic() - s["started_mono"])
    return _wall_elapsed(s.get("started_at"))


def session_snapshot(sid, s):
    state = session_process_state(s)
    data = {
        "session_id": sid,
        "pid": int(s.get("pid") or (s.get("proc").pid if s.get("proc") is not None else 0)),
        "running": bool(state["running"]),
        "exit_code": state.get("exit_code"),
        "shell": s.get("shell"),
        "command": s.get("command") if not s.get("recovered") else None,
        "cwd": s.get("cwd"),
        "started_at": s.get("started_at"),
        "last_activity_at": s.get("last_activity_at"),
        "idle_seconds": int(_session_idle_seconds(s)),
        "idle_timeout_seconds": s.get("idle_timeout_seconds"),
        "max_runtime_seconds": s.get("max_runtime_seconds"),
        "stdout_path": str(s.get("stdout_path")),
        "stderr_path": str(s.get("stderr_path")),
        "recovered": bool(s.get("recovered", False)),
        "stdin_available": bool(s.get("stdin_available", not s.get("recovered", False))),
        "recovery_state": state.get("recovery_state"),
    }
    if state.get("identity_error"):
        data["identity_error"] = state["identity_error"]
    return data


def persist_session_metadata(sid, s):
    state = session_process_state(s)
    body = {
        "schema": "heaven-bridge-session-v1",
        "version": SESSION_METADATA_VERSION,
        "session_id": sid,
        "pid": int(s.get("pid") or (s.get("proc").pid if s.get("proc") is not None else 0)),
        "process_identity": s.get("process_identity"),
        "shell": s.get("shell"),
        "cwd": s.get("cwd"),
        "started_at": s.get("started_at"),
        "last_activity_at": s.get("last_activity_at"),
        "idle_timeout_seconds": int(s.get("idle_timeout_seconds") or DEFAULT_SESSION_IDLE),
        "max_runtime_seconds": int(s.get("max_runtime_seconds") or DEFAULT_SESSION_MAX),
        "stdout_file": f"{sid}.out.log",
        "stderr_file": f"{sid}.err.log",
        "exit_code": state.get("exit_code"),
        "recovery_state": state.get("recovery_state"),
    }
    atomic_write_text(session_metadata_path(sid), json.dumps(body, ensure_ascii=False, indent=2, sort_keys=True) + "\n")


def touch_session_activity(sid, s):
    s["last_activity_at"] = now()
    if s.get("last_activity_mono") is not None:
        s["last_activity_mono"] = time.monotonic()
    persist_session_metadata(sid, s)


def recover_sessions():
    SESSIONS_DIR.mkdir(parents=True, exist_ok=True)
    loaded = 0
    live = 0
    blocked = 0
    for metadata_path in sorted(SESSIONS_DIR.glob("*.json")):
        sid = metadata_path.stem
        if not safe_id(sid):
            audit("session_recovery_skip", path=str(metadata_path), reason="invalid_session_id")
            continue
        try:
            row = json.loads(metadata_path.read_text(encoding="utf-8"))
            if row.get("schema") != "heaven-bridge-session-v1" or int(row.get("version") or 0) != SESSION_METADATA_VERSION:
                raise BridgeError("INVALID_SESSION_METADATA", "unsupported session metadata version")
            if str(row.get("session_id") or "") != sid:
                raise BridgeError("INVALID_SESSION_METADATA", "session metadata id does not match filename")
            pid = int(row.get("pid") or 0)
            if pid <= 0:
                raise BridgeError("INVALID_SESSION_METADATA", "session pid is invalid")
            _session_time(row.get("started_at"))
            _session_time(row.get("last_activity_at"))
            entry = {
                "proc": None,
                "out_f": None,
                "err_f": None,
                "pid": pid,
                "process_identity": row.get("process_identity"),
                "stdout_path": SESSIONS_DIR / f"{sid}.out.log",
                "stderr_path": SESSIONS_DIR / f"{sid}.err.log",
                "shell": str(row.get("shell") or ""),
                "command": None,
                "cwd": str(row.get("cwd") or ""),
                "started_at": str(row.get("started_at")),
                "started_mono": None,
                "last_activity_mono": None,
                "last_activity_at": str(row.get("last_activity_at")),
                "idle_timeout_seconds": max(60, min(int(row.get("idle_timeout_seconds") or DEFAULT_SESSION_IDLE), 86400)),
                "max_runtime_seconds": max(300, min(int(row.get("max_runtime_seconds") or DEFAULT_SESSION_MAX), 172800)),
                "exit_code": row.get("exit_code"),
                "recovered": True,
                "stdin_available": False,
            }
            state = session_process_state(entry)
            entry["recovery_state"] = state.get("recovery_state")
            with STATE_LOCK:
                SESSIONS[sid] = entry
            loaded += 1
            if state["running"]:
                live += 1
            elif state.get("recovery_state") in ("identity-unavailable", "identity-unproven", "identity-mismatch"):
                blocked += 1
            audit(
                "session_recovered",
                session_id=sid,
                pid=pid,
                running=bool(state["running"]),
                recovery_state=state.get("recovery_state"),
            )
        except Exception as e:
            blocked += 1
            audit("session_recovery_skip", path=str(metadata_path), reason=error_dict(e).get("code"), message=str(e)[:500])
    return {"loaded": loaded, "live": live, "blocked": blocked}


def _close_session_handles(s):
    for key in ("out_f", "err_f"):
        handle = s.get(key)
        if handle is not None:
            try:
                handle.flush()
                handle.close()
            except Exception:
                pass
            s[key] = None


def _remove_session_artifacts(sid):
    if not safe_id(sid):
        return
    for path in (
        SESSIONS_DIR / f"{sid}.json",
        SESSIONS_DIR / f"{sid}.out.log",
        SESSIONS_DIR / f"{sid}.err.log",
    ):
        try:
            path.unlink(missing_ok=True)
        except OSError:
            pass


def kill_recovered_session(sid, s, force=True):
    pid = int(s.get("pid") or 0)
    expected = s.get("process_identity")
    if not expected:
        raise BridgeError(
            "SESSION_IDENTITY_UNAVAILABLE",
            "recovered session has no strong process identity; refusing to kill by PID alone",
            {"session_id": sid, "pid": pid},
        )
    try:
        current = process_identity(pid)
    except BridgeError as e:
        raise BridgeError(
            "SESSION_IDENTITY_UNPROVEN",
            "cannot prove recovered session process ownership; refusing to kill",
            {"session_id": sid, "pid": pid, "cause": e.code},
        ) from e
    if current is None:
        s["recovery_state"] = "exited"
        persist_session_metadata(sid, s)
        return
    if not process_identity_matches(expected, current):
        raise BridgeError(
            "SESSION_IDENTITY_MISMATCH",
            "PID identity no longer matches the recovered session; refusing to kill",
            {"session_id": sid, "pid": pid},
        )

    kill_process_tree(pid, force)
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        try:
            current = process_identity(pid)
        except BridgeError as e:
            raise BridgeError(
                "SESSION_KILL_UNVERIFIED",
                "session termination was requested but process identity can no longer be verified",
                {"session_id": sid, "pid": pid, "cause": e.code},
            ) from e
        if current is None or not process_identity_matches(expected, current):
            s["recovery_state"] = "terminated"
            persist_session_metadata(sid, s)
            return
        time.sleep(0.1)
    raise BridgeError(
        "SESSION_KILL_UNVERIFIED",
        "recovered session process remained alive after termination request",
        {"session_id": sid, "pid": pid},
    )


def start_session(p):
    shell = str(p.get("shell") or "powershell").lower()
    command = str(p.get("command") or "")
    cwd = expand_path(p.get("cwd"))
    if command:
        argv = shell_argv(shell, command)
    elif shell == "powershell":
        argv = ["powershell.exe", "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-NoExit"]
    elif shell == "cmd":
        argv = ["cmd.exe", "/d"]
    elif shell == "python":
        argv = ["python.exe", "-i"]
    else:
        raise BridgeError("UNSUPPORTED_SHELL", f"unsupported shell: {shell}")
    sid = uuid.uuid4().hex[:16]
    SESSIONS_DIR.mkdir(parents=True, exist_ok=True)
    stdout_path = SESSIONS_DIR / f"{sid}.out.log"
    stderr_path = SESSIONS_DIR / f"{sid}.err.log"
    out_f = stdout_path.open("a", encoding="utf-8")
    err_f = stderr_path.open("a", encoding="utf-8")
    flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
    if os.name == "nt":
        flags |= getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    proc = subprocess.Popen(
        argv, cwd=str(cwd), stdin=subprocess.PIPE, stdout=out_f, stderr=err_f,
        text=True, bufsize=1, env=build_env(p), creationflags=flags,
    )

    identity = None
    identity_error = None
    for _ in range(5):
        try:
            identity = process_identity(proc.pid)
            identity_error = None
        except BridgeError as e:
            identity_error = e
        if identity is not None or proc.poll() is not None:
            break
        time.sleep(0.02)
    if proc.poll() is None and identity is None:
        kill_process_tree(proc.pid, True)
        try:
            proc.wait(timeout=10)
        except Exception:
            pass
        out_f.close()
        err_f.close()
        if identity_error is not None:
            raise identity_error
        raise BridgeError(
            "SESSION_IDENTITY_UNAVAILABLE",
            "unable to establish strong session process identity; process was terminated",
            {"pid": proc.pid},
        )

    t = time.monotonic()
    started_at = now()
    entry = {
        "proc": proc,
        "out_f": out_f,
        "err_f": err_f,
        "pid": proc.pid,
        "process_identity": identity,
        "stdout_path": stdout_path,
        "stderr_path": stderr_path,
        "shell": shell,
        "command": command,
        "cwd": str(cwd),
        "started_at": started_at,
        "started_mono": t,
        "last_activity_mono": t,
        "last_activity_at": started_at,
        "idle_timeout_seconds": max(60, min(int(p.get("idle_timeout_seconds") or DEFAULT_SESSION_IDLE), 86400)),
        "max_runtime_seconds": max(300, min(int(p.get("max_runtime_seconds") or DEFAULT_SESSION_MAX), 172800)),
        "exit_code": proc.poll(),
        "recovered": False,
        "stdin_available": proc.stdin is not None,
    }
    with STATE_LOCK:
        SESSIONS[sid] = entry
    persist_session_metadata(sid, entry)
    return session_snapshot(sid, entry)


def cleanup_sessions():
    with STATE_LOCK:
        items = list(SESSIONS.items())
    for sid, s in items:
        state = session_process_state(s)
        if state["running"]:
            expired = _session_runtime_seconds(s) > s["max_runtime_seconds"]
            idle = _session_idle_seconds(s) > s["idle_timeout_seconds"]
            if expired or idle:
                reason = "max_runtime" if expired else "idle_timeout"
                try:
                    if s.get("recovered"):
                        kill_recovered_session(sid, s, True)
                    else:
                        kill_process_tree(int(s["pid"]), True)
                    audit("session_cleanup", session_id=sid, pid=int(s["pid"]), reason=reason)
                except BridgeError as e:
                    audit(
                        "session_cleanup_blocked",
                        session_id=sid,
                        pid=int(s["pid"]),
                        reason=reason,
                        error_code=e.code,
                    )
                state = session_process_state(s)

        if not state["running"]:
            _close_session_handles(s)
            if s.get("exit_code") != state.get("exit_code"):
                s["exit_code"] = state.get("exit_code")
                persist_session_metadata(sid, s)
            if _wall_elapsed(s.get("last_activity_at")) > SESSION_RETENTION_SECONDS:
                with STATE_LOCK:
                    SESSIONS.pop(sid, None)
                _remove_session_artifacts(sid)
                audit("session_pruned", session_id=sid, pid=int(s.get("pid") or 0))



def _require_windows_desktop():
    if os.name != "nt":
        raise BridgeError("DESKTOP_UNSUPPORTED", "desktop control is only available on Windows")
    return ctypes.WinDLL("user32", use_last_error=True)


def _desktop_window_rows(limit=200, visible_only=True):
    user32 = _require_windows_desktop()
    limit = max(1, min(int(limit or 200), 1000))
    rows = []
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    def callback(hwnd, _lparam):
        if visible_only and not user32.IsWindowVisible(hwnd):
            return True
        length = int(user32.GetWindowTextLengthW(hwnd))
        if length <= 0:
            return True
        buf = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(hwnd, buf, len(buf))
        title = buf.value.strip()
        if not title:
            return True
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        rect = wintypes.RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(rect))
        rows.append({
            "hwnd": int(hwnd),
            "pid": int(pid.value),
            "title": title,
            "visible": bool(user32.IsWindowVisible(hwnd)),
            "minimized": bool(user32.IsIconic(hwnd)),
            "rect": {
                "x": int(rect.left), "y": int(rect.top),
                "width": max(0, int(rect.right - rect.left)),
                "height": max(0, int(rect.bottom - rect.top)),
            },
        })
        return len(rows) < limit

    cb = callback_type(callback)
    if not user32.EnumWindows(cb, 0):
        err = ctypes.get_last_error()
        if err:
            raise BridgeError("WINDOW_ENUM_FAILED", "EnumWindows failed", {"win32_error": err})
    return rows


def _resolve_window(p):
    user32 = _require_windows_desktop()
    if p.get("hwnd") is not None:
        hwnd = int(p.get("hwnd"))
        if hwnd <= 0 or not user32.IsWindow(hwnd):
            raise BridgeError("WINDOW_NOT_FOUND", "window handle is not valid", {"hwnd": hwnd})
        return hwnd

    title = str(p.get("title") or "").strip().casefold()
    pid = int(p.get("pid") or 0)
    if not title and pid <= 0:
        raise BridgeError("WINDOW_SELECTOR_REQUIRED", "provide hwnd, pid, or title")

    matches = []
    for row in _desktop_window_rows(limit=1000, visible_only=bool(p.get("visible_only", True))):
        if pid > 0 and row["pid"] != pid:
            continue
        if title and title not in row["title"].casefold():
            continue
        matches.append(row)

    if not matches:
        raise BridgeError("WINDOW_NOT_FOUND", "no matching desktop window found")
    if len(matches) > 1 and not bool(p.get("first_match", False)):
        raise BridgeError(
            "WINDOW_AMBIGUOUS",
            "multiple desktop windows matched; provide hwnd or a narrower selector",
            {"matches": matches[:10], "total": len(matches)},
        )
    return int(matches[0]["hwnd"])


def wait_for_condition(p, cancel_event=None):
    condition = str(p.get("condition") or "").strip().lower()
    supported = {
        "file_exists", "file_missing",
        "process_exists", "process_missing",
        "session_running", "session_stopped",
        "window_exists", "window_missing",
    }
    if condition not in supported:
        raise BridgeError(
            "INVALID_WAIT_CONDITION",
            "condition must be one of: " + ", ".join(sorted(supported)),
        )

    timeout_seconds = max(0.05, min(float(p.get("timeout_seconds") or 30), 300.0))
    interval_ms = max(50, min(int(p.get("interval_ms") or 250), 5000))
    soft_timeout = bool(p.get("soft_timeout", False))
    started_mono = time.monotonic()
    deadline = started_mono + timeout_seconds
    observed = None

    def inspect():
        if condition in ("file_exists", "file_missing"):
            path = expand_path(p.get("path"), True)
            exists = path.exists()
            return (exists if condition == "file_exists" else not exists), {
                "path": str(path), "exists": bool(exists)
            }

        if condition in ("process_exists", "process_missing"):
            pid = int(p.get("pid") or 0)
            if pid <= 0:
                raise BridgeError("WAIT_PID_REQUIRED", "pid must be a positive integer for process waits")
            identity = process_identity(pid)
            exists = identity is not None
            return (exists if condition == "process_exists" else not exists), {
                "pid": pid, "exists": bool(exists),
                "creation_token": identity.get("creation_token") if identity else None,
            }

        if condition in ("session_running", "session_stopped"):
            sid = str(p.get("session_id") or "").strip()
            if not sid:
                raise BridgeError("WAIT_SESSION_REQUIRED", "session_id is required for session waits")
            with STATE_LOCK:
                session = SESSIONS.get(sid)
                snapshot = session_snapshot(sid, session) if session is not None else None
            running = bool(snapshot and snapshot.get("running"))
            satisfied = running if condition == "session_running" else not running
            return satisfied, {
                "session_id": sid,
                "known": snapshot is not None,
                "running": running,
                "exit_code": snapshot.get("exit_code") if snapshot else None,
            }

        selector = {
            key: p.get(key)
            for key in ("hwnd", "pid", "title", "visible_only", "first_match")
            if p.get(key) is not None
        }
        try:
            hwnd = _resolve_window(selector)
            exists = True
            row = next((r for r in _desktop_window_rows(1000, bool(selector.get("visible_only", True)))
                        if int(r.get("hwnd") or 0) == int(hwnd)), None)
            observed_window = row or {"hwnd": int(hwnd)}
        except BridgeError as exc:
            if exc.code == "WINDOW_NOT_FOUND":
                exists = False
                observed_window = None
            elif condition == "window_missing" and exc.code == "WINDOW_AMBIGUOUS":
                exists = True
                observed_window = {"ambiguous": True, "matches": exc.details.get("matches", [])[:10]}
            else:
                raise
        return (exists if condition == "window_exists" else not exists), {
            "exists": bool(exists), "window": observed_window
        }

    while True:
        if cancel_event is not None and cancel_event.is_set():
            raise BridgeError(
                "WAIT_CANCELLED",
                "wait was cancelled",
                {"condition": condition, "waited_ms": int((time.monotonic() - started_mono) * 1000)},
            )

        satisfied, observed = inspect()
        waited_ms = int((time.monotonic() - started_mono) * 1000)
        if satisfied:
            return {
                "condition": condition,
                "satisfied": True,
                "timed_out": False,
                "waited_ms": waited_ms,
                "observed": observed,
            }

        if time.monotonic() >= deadline:
            data = {
                "condition": condition,
                "satisfied": False,
                "timed_out": True,
                "waited_ms": waited_ms,
                "observed": observed,
            }
            if soft_timeout:
                return data
            raise BridgeError("WAIT_TIMEOUT", "wait condition was not satisfied before timeout", data)

        time.sleep(min(interval_ms / 1000.0, max(0.0, deadline - time.monotonic())))


def desktop_focus_window(p):
    user32 = _require_windows_desktop()
    hwnd = _resolve_window(p)
    user32.ShowWindowAsync(hwnd, 9)  # SW_RESTORE
    user32.BringWindowToTop(hwnd)
    focused = bool(user32.SetForegroundWindow(hwnd))
    return {"hwnd": hwnd, "focused": focused}


def desktop_move_window(p):
    user32 = _require_windows_desktop()
    hwnd = _resolve_window(p)
    rect = wintypes.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
        raise BridgeError("WINDOW_RECT_FAILED", "failed to read window rectangle", {"hwnd": hwnd})
    x = int(p.get("x") if p.get("x") is not None else rect.left)
    y = int(p.get("y") if p.get("y") is not None else rect.top)
    width = int(p.get("width") if p.get("width") is not None else rect.right - rect.left)
    height = int(p.get("height") if p.get("height") is not None else rect.bottom - rect.top)
    if width < 1 or height < 1 or width > 32768 or height > 32768:
        raise BridgeError("INVALID_WINDOW_SIZE", "window width/height must be between 1 and 32768")
    ok = bool(user32.MoveWindow(hwnd, x, y, width, height, True))
    if not ok:
        raise BridgeError("WINDOW_MOVE_FAILED", "MoveWindow failed", {"hwnd": hwnd, "win32_error": ctypes.get_last_error()})
    return {"hwnd": hwnd, "rect": {"x": x, "y": y, "width": width, "height": height}}


def desktop_window_state(p):
    user32 = _require_windows_desktop()
    hwnd = _resolve_window(p)
    state = str(p.get("state") or "restore").lower()
    commands = {
        "hide": 0, "normal": 1, "maximize": 3, "show": 5,
        "minimize": 6, "restore": 9,
    }
    if state not in commands:
        raise BridgeError("INVALID_WINDOW_STATE", "state must be hide, normal, maximize, show, minimize, or restore")
    user32.ShowWindowAsync(hwnd, commands[state])
    return {"hwnd": hwnd, "state": state}


def desktop_close_window(p):
    user32 = _require_windows_desktop()
    hwnd = _resolve_window(p)
    ok = bool(user32.PostMessageW(hwnd, 0x0010, 0, 0))  # WM_CLOSE
    if not ok:
        raise BridgeError("WINDOW_CLOSE_FAILED", "failed to post WM_CLOSE", {"hwnd": hwnd, "win32_error": ctypes.get_last_error()})
    return {"hwnd": hwnd, "close_requested": True}


def _cursor_position(user32=None):
    user32 = user32 or _require_windows_desktop()
    point = wintypes.POINT()
    if not user32.GetCursorPos(ctypes.byref(point)):
        raise BridgeError("CURSOR_READ_FAILED", "GetCursorPos failed", {"win32_error": ctypes.get_last_error()})
    return int(point.x), int(point.y)


def desktop_mouse_move(p):
    user32 = _require_windows_desktop()
    x = int(p.get("x"))
    y = int(p.get("y"))
    duration_ms = max(0, min(int(p.get("duration_ms") or 0), 5000))
    if duration_ms:
        sx, sy = _cursor_position(user32)
        steps = max(2, min(120, duration_ms // 8 or 2))
        for step in range(1, steps + 1):
            px = round(sx + (x - sx) * step / steps)
            py = round(sy + (y - sy) * step / steps)
            if not user32.SetCursorPos(px, py):
                raise BridgeError("CURSOR_MOVE_FAILED", "SetCursorPos failed", {"win32_error": ctypes.get_last_error()})
            time.sleep(duration_ms / steps / 1000.0)
    elif not user32.SetCursorPos(x, y):
        raise BridgeError("CURSOR_MOVE_FAILED", "SetCursorPos failed", {"win32_error": ctypes.get_last_error()})
    cx, cy = _cursor_position(user32)
    return {"x": cx, "y": cy}


def desktop_mouse_button(p):
    user32 = _require_windows_desktop()
    if p.get("x") is not None or p.get("y") is not None:
        if p.get("x") is None or p.get("y") is None:
            raise BridgeError("INVALID_POINTER", "x and y must be provided together")
        desktop_mouse_move({"x": p.get("x"), "y": p.get("y"), "duration_ms": p.get("duration_ms", 0)})
    button = str(p.get("button") or "left").lower()
    state = str(p.get("state") or "").lower()
    flags = {
        "left": {"down": 0x0002, "up": 0x0004},
        "right": {"down": 0x0008, "up": 0x0010},
        "middle": {"down": 0x0020, "up": 0x0040},
    }
    if button not in flags:
        raise BridgeError("INVALID_MOUSE_BUTTON", "button must be left, right, or middle")
    if state not in ("down", "up"):
        raise BridgeError("INVALID_MOUSE_STATE", "state must be down or up")
    user32.mouse_event(flags[button][state], 0, 0, 0, 0)
    x, y = _cursor_position(user32)
    return {"button": button, "state": state, "x": x, "y": y}


def desktop_mouse_click(p):
    user32 = _require_windows_desktop()
    if p.get("x") is not None or p.get("y") is not None:
        if p.get("x") is None or p.get("y") is None:
            raise BridgeError("INVALID_POINTER", "x and y must be provided together")
        desktop_mouse_move({"x": p.get("x"), "y": p.get("y"), "duration_ms": p.get("duration_ms", 0)})
    button = str(p.get("button") or "left").lower()
    flags = {
        "left": (0x0002, 0x0004),
        "right": (0x0008, 0x0010),
        "middle": (0x0020, 0x0040),
    }.get(button)
    if not flags:
        raise BridgeError("INVALID_MOUSE_BUTTON", "button must be left, right, or middle")
    count = max(1, min(int(p.get("count") or 1), 3))
    interval_ms = max(0, min(int(p.get("interval_ms") or 80), 1000))
    for index in range(count):
        user32.mouse_event(flags[0], 0, 0, 0, 0)
        user32.mouse_event(flags[1], 0, 0, 0, 0)
        if index + 1 < count and interval_ms:
            time.sleep(interval_ms / 1000.0)
    x, y = _cursor_position(user32)
    return {"button": button, "count": count, "x": x, "y": y}


def desktop_mouse_scroll(p):
    user32 = _require_windows_desktop()
    clicks = max(-100, min(int(p.get("clicks") or 0), 100))
    if clicks == 0:
        raise BridgeError("INVALID_SCROLL", "clicks must be a non-zero integer")
    horizontal = bool(p.get("horizontal", False))
    flag = 0x1000 if horizontal else 0x0800
    user32.mouse_event(flag, 0, 0, clicks * 120, 0)
    return {"clicks": clicks, "horizontal": horizontal}


def _vk_code(name):
    key = str(name or "").strip().lower()
    table = {
        "backspace": 0x08, "tab": 0x09, "enter": 0x0D, "shift": 0x10, "ctrl": 0x11,
        "control": 0x11, "alt": 0x12, "pause": 0x13, "capslock": 0x14, "escape": 0x1B,
        "esc": 0x1B, "space": 0x20, "pageup": 0x21, "pagedown": 0x22, "end": 0x23,
        "home": 0x24, "left": 0x25, "up": 0x26, "right": 0x27, "down": 0x28,
        "insert": 0x2D, "delete": 0x2E, "win": 0x5B, "windows": 0x5B,
    }
    if key in table:
        return table[key]
    if re.fullmatch(r"f(?:[1-9]|1[0-9]|2[0-4])", key):
        return 0x70 + int(key[1:]) - 1
    if len(key) == 1 and (key.isalpha() or key.isdigit()):
        return ord(key.upper())
    raise BridgeError("INVALID_KEY", "unsupported key name", {"key": name})


def desktop_key(p):
    user32 = _require_windows_desktop()
    key = _vk_code(p.get("key"))
    modifiers = p.get("modifiers") or []
    if isinstance(modifiers, str):
        modifiers = [x.strip() for x in modifiers.split("+") if x.strip()]
    if not isinstance(modifiers, list):
        raise BridgeError("INVALID_MODIFIERS", "modifiers must be a list or + separated string")
    modifier_codes = [_vk_code(x) for x in modifiers]
    count = max(1, min(int(p.get("count") or 1), 20))
    for code in modifier_codes:
        user32.keybd_event(code, 0, 0, 0)
    try:
        for _ in range(count):
            user32.keybd_event(key, 0, 0, 0)
            user32.keybd_event(key, 0, 0x0002, 0)
    finally:
        for code in reversed(modifier_codes):
            user32.keybd_event(code, 0, 0x0002, 0)
    return {"key": str(p.get("key")), "modifiers": [str(x) for x in modifiers], "count": count}


def desktop_type_text(p):
    user32 = _require_windows_desktop()
    value = str(p.get("text") if p.get("text") is not None else "")
    if len(value) > 10000:
        raise BridgeError("TEXT_TOO_LARGE", "gui_type is limited to 10000 characters per job")

    ulong_ptr = wintypes.WPARAM

    class MouseInput(ctypes.Structure):
        _fields_ = [
            ("dx", wintypes.LONG), ("dy", wintypes.LONG), ("mouseData", wintypes.DWORD),
            ("dwFlags", wintypes.DWORD), ("time", wintypes.DWORD), ("dwExtraInfo", ulong_ptr),
        ]

    class KeyboardInput(ctypes.Structure):
        _fields_ = [
            ("wVk", wintypes.WORD), ("wScan", wintypes.WORD), ("dwFlags", wintypes.DWORD),
            ("time", wintypes.DWORD), ("dwExtraInfo", ulong_ptr),
        ]

    class HardwareInput(ctypes.Structure):
        _fields_ = [("uMsg", wintypes.DWORD), ("wParamL", wintypes.WORD), ("wParamH", wintypes.WORD)]

    class InputUnion(ctypes.Union):
        _fields_ = [("mi", MouseInput), ("ki", KeyboardInput), ("hi", HardwareInput)]

    class Input(ctypes.Structure):
        _anonymous_ = ("u",)
        _fields_ = [("type", wintypes.DWORD), ("u", InputUnion)]

    units = value.encode("utf-16-le")
    sent_units = 0
    for offset in range(0, len(units), 2):
        scan = int.from_bytes(units[offset:offset + 2], "little")
        down = Input(type=1, ki=KeyboardInput(0, scan, 0x0004, 0, 0))
        up = Input(type=1, ki=KeyboardInput(0, scan, 0x0004 | 0x0002, 0, 0))
        if user32.SendInput(1, ctypes.byref(down), ctypes.sizeof(Input)) != 1:
            raise BridgeError("KEY_INPUT_FAILED", "SendInput key-down failed", {"win32_error": ctypes.get_last_error()})
        if user32.SendInput(1, ctypes.byref(up), ctypes.sizeof(Input)) != 1:
            raise BridgeError("KEY_INPUT_FAILED", "SendInput key-up failed", {"win32_error": ctypes.get_last_error()})
        sent_units += 1
    return {"characters": len(value), "utf16_units": sent_units}


def _secret_unc_parts(raw):
    value = str(raw or "").strip()
    match = re.fullmatch(r"\\\\([^\\]+)\\([^\\]+)(?:\\.*)?", value)
    if not match:
        return None
    return match.group(1), match.group(2)


def verify_secret_inbox_transport(raw=None):
    if os.name != "nt":
        return False
    value = str(raw if raw is not None else os.environ.get(SECRET_INBOX_ENV) or "").strip()
    parts = _secret_unc_parts(value)
    if not parts:
        return False
    server, share = parts
    try:
        inbox = Path(os.path.expandvars(os.path.expanduser(value)))
        if not inbox.is_dir():
            return False
    except OSError:
        return False

    env = safe_process_env()
    env["HEAVEN_SECRET_VERIFY_SERVER"] = server
    env["HEAVEN_SECRET_VERIFY_SHARE"] = share
    command = (
        "$c = Get-SmbConnection -ServerName $env:HEAVEN_SECRET_VERIFY_SERVER -ErrorAction Stop | "
        "Where-Object { $_.ShareName -eq $env:HEAVEN_SECRET_VERIFY_SHARE } | "
        "Select-Object -First 1 -Property ServerName,ShareName,Encrypted; "
        "if ($null -eq $c) { exit 3 }; "
        "$c | ConvertTo-Json -Compress"
    )
    try:
        proc = subprocess.run(
            ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=10,
            env=env,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
    except (OSError, subprocess.TimeoutExpired):
        return False
    if proc.returncode != 0:
        return False
    lines = [line.strip() for line in (proc.stdout or "").splitlines() if line.strip()]
    if not lines:
        return False
    try:
        row = json.loads(lines[-1])
    except json.JSONDecodeError:
        return False
    return bool(
        isinstance(row, dict)
        and str(row.get("ServerName") or "").casefold() == server.casefold()
        and str(row.get("ShareName") or "").casefold() == share.casefold()
        and row.get("Encrypted") is True
    )


def secret_channel_status():
    raw = str(os.environ.get(SECRET_INBOX_ENV) or "").strip()
    available = bool(raw and verify_secret_inbox_transport(raw))
    return {
        "available": available,
        "transport": "encrypted-smb-inbox-v1" if available else "not_configured",
        "relay_secret_values_allowed": False,
        "single_use": True,
        "destination_bound": True,
        "target_bound": True,
        "transport_verified": available,
    }


def advertised_actions():
    actions = set(DIRECT_ACTIONS)
    if not secret_channel_status()["available"]:
        actions.discard("gui_type_secret")
    return sorted(actions)


def secret_target_binding(hwnd, destination=None):
    try:
        hwnd_value = int(hwnd)
    except (TypeError, ValueError) as exc:
        raise BridgeError("SECRET_TARGET_REQUIRED", "gui_type_secret requires a numeric params.hwnd") from exc
    if hwnd_value <= 0:
        raise BridgeError("SECRET_TARGET_REQUIRED", "gui_type_secret requires a positive params.hwnd")
    host = str(destination or os.environ.get("COMPUTERNAME") or "heaven").strip().casefold()
    if not host:
        raise BridgeError("SECRET_DESTINATION_INVALID", "secret destination host is empty")
    material = f"{SECRET_PURPOSE}|{host}|hwnd:{hwnd_value}".encode("utf-8")
    return hashlib.sha256(material).hexdigest()


def _parse_secret_timestamp(value, field):
    if not value:
        raise BridgeError("SECRET_ENVELOPE_INVALID", f"secret envelope is missing {field}")
    text = str(value).strip()
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    try:
        parsed = datetime.fromisoformat(text)
    except ValueError as exc:
        raise BridgeError("SECRET_ENVELOPE_INVALID", f"secret envelope {field} must be ISO-8601") from exc
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def _secret_inbox_path():
    status = secret_channel_status()
    if not status["available"]:
        raise BridgeError(
            "SECRET_CHANNEL_UNAVAILABLE",
            "credential-safe GUI input requires a locally configured encrypted secret inbox",
        )
    return Path(os.path.expandvars(os.path.expanduser(str(os.environ.get(SECRET_INBOX_ENV)))))


def _validate_secret_handle(handle):
    value = str(handle or "").strip()
    if not re.fullmatch(r"[A-Fa-f0-9]{32,120}", value):
        raise BridgeError("SECRET_HANDLE_INVALID", "secret handle must be a 32-120 character hexadecimal opaque id")
    return value.lower()


def consume_secret_envelope(handle, hwnd, current=None):
    handle = _validate_secret_handle(handle)
    inbox = _secret_inbox_path()
    envelope = inbox / f"{handle}.json"
    claim = inbox / f".claim-{handle}-{os.getpid()}-{uuid.uuid4().hex}.json"

    if (SECRET_CONSUMED_DIR / f"{handle}.used").exists():
        raise BridgeError("SECRET_REPLAYED", "secret handle has already been consumed")

    try:
        os.replace(envelope, claim)
    except FileNotFoundError as exc:
        raise BridgeError("SECRET_HANDLE_NOT_FOUND", "secret handle is missing or already consumed") from exc
    except OSError as exc:
        raise BridgeError("SECRET_CLAIM_FAILED", "could not atomically claim the secret envelope") from exc

    SECRET_CONSUMED_DIR.mkdir(parents=True, exist_ok=True)
    marker = SECRET_CONSUMED_DIR / f"{handle}.used"
    try:
        try:
            with marker.open("x", encoding="utf-8") as f:
                f.write(json.dumps({"handle": handle, "claimed_at": now()}, separators=(",", ":")))
        except FileExistsError as exc:
            raise BridgeError("SECRET_REPLAYED", "secret handle has already been consumed") from exc
        except OSError as exc:
            raise BridgeError("SECRET_REPLAY_GUARD_FAILED", "could not persist the secret replay guard") from exc

        try:
            size = claim.stat().st_size
        except OSError as exc:
            raise BridgeError("SECRET_ENVELOPE_INVALID", "claimed secret envelope is unreadable") from exc
        if size <= 0 or size > SECRET_MAX_FILE_BYTES:
            raise BridgeError("SECRET_ENVELOPE_INVALID", "secret envelope size is outside the allowed budget")

        try:
            doc = json.loads(claim.read_text(encoding="utf-8"))
        except Exception as exc:
            raise BridgeError("SECRET_ENVELOPE_INVALID", "secret envelope is not valid UTF-8 JSON") from exc
        if not isinstance(doc, dict):
            raise BridgeError("SECRET_ENVELOPE_INVALID", "secret envelope must be a JSON object")
        if doc.get("schema") != SECRET_ENVELOPE_SCHEMA:
            raise BridgeError("SECRET_ENVELOPE_INVALID", "unsupported secret envelope schema")
        if str(doc.get("handle") or "").strip().lower() != handle:
            raise BridgeError("SECRET_ENVELOPE_INVALID", "secret envelope handle does not match its opaque id")
        if str(doc.get("purpose") or "") != SECRET_PURPOSE:
            raise BridgeError("SECRET_PURPOSE_MISMATCH", "secret envelope purpose does not match gui_type_secret")

        expected_host = str(os.environ.get("COMPUTERNAME") or "heaven").strip().casefold()
        destination = str(doc.get("destination") or "").strip().casefold()
        if destination != expected_host:
            raise BridgeError("SECRET_DESTINATION_MISMATCH", "secret envelope is bound to a different destination host")

        expected_binding = secret_target_binding(hwnd, expected_host)
        actual_binding = str(doc.get("target_binding") or "").strip().lower()
        if not re.fullmatch(r"[0-9a-f]{64}", actual_binding) or not hmac.compare_digest(actual_binding, expected_binding):
            raise BridgeError("SECRET_TARGET_MISMATCH", "secret envelope is bound to a different GUI target")

        created = _parse_secret_timestamp(doc.get("created_at"), "created_at")
        expires = _parse_secret_timestamp(doc.get("expires_at"), "expires_at")
        if expires <= created:
            raise BridgeError("SECRET_ENVELOPE_INVALID", "secret envelope expiration must be after creation")
        ttl = (expires - created).total_seconds()
        if ttl > SECRET_MAX_TTL_SECONDS:
            raise BridgeError("SECRET_TTL_TOO_LONG", "secret envelope TTL exceeds the configured maximum")

        current = current or utcnow()
        if (created - current).total_seconds() > FUTURE_SKEW_SECONDS:
            raise BridgeError("SECRET_FROM_FUTURE", "secret envelope creation time is too far in the future")
        if current >= expires:
            raise BridgeError("SECRET_EXPIRED", "secret envelope has expired")

        value = doc.get("value")
        if not isinstance(value, str) or not value:
            raise BridgeError("SECRET_ENVELOPE_INVALID", "secret envelope value must be a non-empty string")
        if len(value) > SECRET_MAX_CHARACTERS:
            raise BridgeError("SECRET_TOO_LARGE", "secret value exceeds the configured character budget")
        return value
    finally:
        try:
            claim.unlink(missing_ok=True)
        except OSError:
            pass


def desktop_type_secret(p):
    if not isinstance(p, dict):
        raise BridgeError("INVALID_SECRET_INPUT_PARAMS", "gui_type_secret params must be an object")
    unknown = sorted(set(p) - {"handle", "hwnd"})
    if unknown:
        if any(CONTROLLER_SECRET_KEY_RE.search(str(key)) or str(key).lower() in ("text", "value") for key in unknown):
            raise BridgeError("SECRET_RELAY_VALUE_BLOCKED", "secret values must never be included in GitHub relay params")
        raise BridgeError("INVALID_SECRET_INPUT_PARAMS", "gui_type_secret accepts only handle and hwnd", {"fields": unknown})

    handle = _validate_secret_handle(p.get("handle"))
    try:
        hwnd = int(p.get("hwnd"))
    except (TypeError, ValueError) as exc:
        raise BridgeError("SECRET_TARGET_REQUIRED", "gui_type_secret requires a numeric params.hwnd") from exc
    if hwnd <= 0:
        raise BridgeError("SECRET_TARGET_REQUIRED", "gui_type_secret requires a positive params.hwnd")

    if not secret_channel_status()["available"]:
        raise BridgeError("SECRET_CHANNEL_UNAVAILABLE", "credential-safe GUI input is not configured")

    # Resolve/focus the exact target before consuming the one-time secret.
    desktop_focus_window({"hwnd": hwnd})
    value = consume_secret_envelope(handle, hwnd)
    try:
        desktop_type_text({"text": value})
    finally:
        value = None
    return {"consumed": True, "typed": True}



def _validate_uia_request(p, operation):
    if not isinstance(p, dict):
        raise BridgeError("INVALID_UIA_REQUEST", "UI Automation params must be an object")
    selector = p.get("selector")
    if selector is not None and not isinstance(selector, dict):
        raise BridgeError("INVALID_UIA_SELECTOR", "selector must be an object")
    allowed_selector = {
        "automation_id", "name", "name_contains", "control_type",
        "class_name", "process_id", "enabled", "offscreen",
    }
    if isinstance(selector, dict):
        unknown = sorted(set(selector) - allowed_selector)
        if unknown:
            raise BridgeError("INVALID_UIA_SELECTOR", "selector contains unsupported fields", {"fields": unknown})
        for key in ("automation_id", "name", "name_contains", "control_type", "class_name"):
            if key in selector and len(str(selector[key])) > 512:
                raise BridgeError("INVALID_UIA_SELECTOR", f"selector.{key} is too long")
    if operation not in ("uia_tree", "uia_find") and not selector:
        raise BridgeError("UIA_SELECTOR_REQUIRED", "semantic UI actions require params.selector")
    scope = str(p.get("scope") or "descendants").lower()
    if scope not in ("children", "descendants"):
        raise BridgeError("INVALID_UIA_SCOPE", "scope must be children or descendants")
    if operation == "uia_set_value":
        if not bool(p.get("allow_relay_text", False)):
            raise BridgeError(
                "UIA_RELAY_TEXT_OPT_IN_REQUIRED",
                "uia_set_value persists params.value in the private relay; set allow_relay_text=true only for explicitly non-secret text",
            )
        if p.get("value") is None:
            raise BridgeError("UIA_VALUE_REQUIRED", "uia_set_value requires params.value")
        if len(str(p.get("value"))) > 10000:
            raise BridgeError("TEXT_TOO_LARGE", "uia_set_value is limited to 10000 characters")
    return {
        **p,
        "operation": operation,
        "scope": scope,
        "max_nodes": max(10, min(int(p.get("max_nodes") or UIA_MAX_NODES), UIA_MAX_NODES)),
        "max_depth": max(1, min(int(p.get("max_depth") or UIA_MAX_DEPTH), UIA_MAX_DEPTH)),
        "wait_ms": max(0, min(int(p.get("wait_ms") or 0), UIA_MAX_WAIT_MS)),
    }


def _run_uia_once(request):
    script = ROOT / "heaven-bridge" / "uia.ps1"
    if not script.is_file():
        raise BridgeError("UIA_BACKEND_UNAVAILABLE", "UI Automation backend script is missing")
    env = safe_process_env()
    raw = json.dumps(request, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    env["HEAVEN_UIA_REQUEST"] = base64.b64encode(raw).decode("ascii")
    timeout_seconds = max(5, min(30, 6 + int(request.get("wait_ms") or 0) // 1000))
    try:
        proc = subprocess.run(
            ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script)],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout_seconds,
            env=env,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
    except subprocess.TimeoutExpired as exc:
        raise BridgeError("UIA_TIMEOUT", "Windows UI Automation backend exceeded its bounded timeout") from exc
    if proc.returncode != 0:
        raise BridgeError("UIA_BACKEND_FAILED", "Windows UI Automation backend failed", {"exit_code": proc.returncode})
    lines = [line.strip() for line in (proc.stdout or "").splitlines() if line.strip()]
    if not lines:
        raise BridgeError("UIA_BACKEND_INVALID", "Windows UI Automation backend returned no structured result")
    try:
        result = json.loads(lines[-1])
    except json.JSONDecodeError as exc:
        raise BridgeError("UIA_BACKEND_INVALID", "Windows UI Automation backend returned invalid JSON") from exc
    if not result.get("ok"):
        raise BridgeError(
            str(result.get("code") or "UIA_FAILED"),
            str(result.get("message") or "UI Automation operation failed"),
            result.get("details"),
        )
    return result.get("data")


def desktop_uia(p, operation):
    _require_windows_desktop()
    request = _validate_uia_request(p, operation)
    wait_ms = int(request.get("wait_ms") or 0)
    deadline = time.monotonic() + wait_ms / 1000.0
    while True:
        try:
            data = _run_uia_once(request)
            if operation == "uia_find" and wait_ms and not (data or {}).get("count") and time.monotonic() < deadline:
                time.sleep(0.15)
                continue
            return data
        except BridgeError as exc:
            if wait_ms and exc.code in ("UIA_NOT_FOUND", "UIA_WINDOW_NOT_FOUND") and time.monotonic() < deadline:
                time.sleep(0.15)
                continue
            raise


def _open_clipboard(user32, attempts=20):
    for _ in range(attempts):
        if user32.OpenClipboard(None):
            return
        time.sleep(0.05)
    raise BridgeError("CLIPBOARD_BUSY", "clipboard could not be opened", {"win32_error": ctypes.get_last_error()})


def desktop_clipboard_read():
    user32 = _require_windows_desktop()
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    user32.GetClipboardData.argtypes = [wintypes.UINT]
    user32.GetClipboardData.restype = wintypes.HANDLE
    kernel32.GlobalLock.argtypes = [wintypes.HANDLE]
    kernel32.GlobalLock.restype = ctypes.c_void_p
    kernel32.GlobalUnlock.argtypes = [wintypes.HANDLE]
    kernel32.GlobalUnlock.restype = wintypes.BOOL
    _open_clipboard(user32)
    try:
        if not user32.IsClipboardFormatAvailable(13):  # CF_UNICODETEXT
            return {"text": "", "characters": 0, "format": "unicode_text", "available": False}
        handle = user32.GetClipboardData(13)
        if not handle:
            raise BridgeError("CLIPBOARD_READ_FAILED", "GetClipboardData failed", {"win32_error": ctypes.get_last_error()})
        kernel32.GlobalLock.restype = ctypes.c_void_p
        ptr = kernel32.GlobalLock(handle)
        if not ptr:
            raise BridgeError("CLIPBOARD_READ_FAILED", "GlobalLock failed", {"win32_error": ctypes.get_last_error()})
        try:
            text = ctypes.wstring_at(ptr)
        finally:
            kernel32.GlobalUnlock(handle)
    finally:
        user32.CloseClipboard()
    if len(text) > 200000:
        return {"text": text[:200000], "characters": len(text), "format": "unicode_text", "available": True, "truncated": True}
    return {"text": text, "characters": len(text), "format": "unicode_text", "available": True, "truncated": False}


def desktop_clipboard_write(value):
    user32 = _require_windows_desktop()
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.GlobalAlloc.argtypes = [wintypes.UINT, ctypes.c_size_t]
    kernel32.GlobalAlloc.restype = wintypes.HANDLE
    kernel32.GlobalLock.argtypes = [wintypes.HANDLE]
    kernel32.GlobalLock.restype = ctypes.c_void_p
    kernel32.GlobalUnlock.argtypes = [wintypes.HANDLE]
    kernel32.GlobalUnlock.restype = wintypes.BOOL
    kernel32.GlobalFree.argtypes = [wintypes.HANDLE]
    kernel32.GlobalFree.restype = wintypes.HANDLE
    user32.SetClipboardData.argtypes = [wintypes.UINT, wintypes.HANDLE]
    user32.SetClipboardData.restype = wintypes.HANDLE
    text = str(value if value is not None else "")
    if len(text) > 1000000:
        raise BridgeError("TEXT_TOO_LARGE", "clipboard_write is limited to 1000000 characters")
    raw = (text + "\0").encode("utf-16-le")
    handle = kernel32.GlobalAlloc(0x0002, len(raw))  # GMEM_MOVEABLE
    if not handle:
        raise BridgeError("CLIPBOARD_WRITE_FAILED", "GlobalAlloc failed", {"win32_error": ctypes.get_last_error()})
    ptr = kernel32.GlobalLock(handle)
    if not ptr:
        kernel32.GlobalFree(handle)
        raise BridgeError("CLIPBOARD_WRITE_FAILED", "GlobalLock failed", {"win32_error": ctypes.get_last_error()})
    try:
        ctypes.memmove(ptr, raw, len(raw))
    finally:
        kernel32.GlobalUnlock(handle)

    transferred = False
    _open_clipboard(user32)
    try:
        if not user32.EmptyClipboard():
            raise BridgeError("CLIPBOARD_WRITE_FAILED", "EmptyClipboard failed", {"win32_error": ctypes.get_last_error()})
        if not user32.SetClipboardData(13, handle):
            raise BridgeError("CLIPBOARD_WRITE_FAILED", "SetClipboardData failed", {"win32_error": ctypes.get_last_error()})
        transferred = True
    finally:
        user32.CloseClipboard()
        if not transferred:
            kernel32.GlobalFree(handle)
    return {"characters": len(text), "format": "unicode_text"}



def desktop_create_shortcut(job_id, p, cancel_event):
    if os.name != "nt":
        raise BridgeError("DESKTOP_UNSUPPORTED", "desktop_shortcut_create is only available on Windows")

    name = str(p.get("name") or "").strip()
    if name.lower().endswith(".lnk"):
        name = name[:-4].rstrip()
    if not name:
        raise BridgeError("SHORTCUT_NAME_REQUIRED", "params.name is required")
    if len(name) > 180 or any(ch in name for ch in '<>:"/\\|?*'):
        raise BridgeError("INVALID_SHORTCUT_NAME", "params.name contains invalid Windows filename characters")

    target = str(p.get("target") or p.get("path") or "").strip()
    if not target:
        raise BridgeError("SHORTCUT_TARGET_REQUIRED", "params.target or params.path is required")

    raw_args = p.get("args") or []
    if isinstance(raw_args, list):
        if any(not isinstance(x, str) for x in raw_args):
            raise BridgeError("INVALID_SHORTCUT_ARGS", "params.args list must contain only strings")
        arguments = subprocess.list2cmdline(raw_args)
    elif isinstance(raw_args, str):
        arguments = raw_args
    else:
        raise BridgeError("INVALID_SHORTCUT_ARGS", "params.args must be a string or list of strings")

    working_directory = str(p.get("working_directory") or p.get("cwd") or "").strip()
    description = str(p.get("description") or "").strip()
    if len(description) > 1024:
        raise BridgeError("SHORTCUT_DESCRIPTION_TOO_LONG", "params.description is limited to 1024 characters")

    icon_path = str(p.get("icon_path") or "").strip()
    try:
        icon_index = int(p.get("icon_index") or 0)
    except (TypeError, ValueError) as exc:
        raise BridgeError("INVALID_ICON_INDEX", "params.icon_index must be an integer") from exc

    location = str(p.get("location") or "desktop").strip().lower()
    if location != "desktop":
        raise BridgeError("INVALID_SHORTCUT_LOCATION", "only location=desktop is currently supported")

    env = safe_process_env()
    env.update({
        "HLB_SHORTCUT_NAME": name,
        "HLB_SHORTCUT_TARGET": target,
        "HLB_SHORTCUT_ARGUMENTS": arguments,
        "HLB_SHORTCUT_WORKDIR": working_directory,
        "HLB_SHORTCUT_DESCRIPTION": description,
        "HLB_SHORTCUT_ICON": icon_path,
        "HLB_SHORTCUT_ICON_INDEX": str(icon_index),
        "HLB_SHORTCUT_OVERWRITE": "1" if bool(p.get("overwrite", False)) else "0",
    })
    script = r"""
$ErrorActionPreference = 'Stop'
$desktop = [Environment]::GetFolderPath('Desktop')
if (-not $desktop) { throw 'Desktop folder could not be resolved' }
$shortcutPath = Join-Path $desktop ($env:HLB_SHORTCUT_NAME + '.lnk')
if ((Test-Path -LiteralPath $shortcutPath) -and $env:HLB_SHORTCUT_OVERWRITE -ne '1') {
    throw 'SHORTCUT_EXISTS'
}
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $env:HLB_SHORTCUT_TARGET
$shortcut.Arguments = $env:HLB_SHORTCUT_ARGUMENTS
if ($env:HLB_SHORTCUT_WORKDIR) { $shortcut.WorkingDirectory = $env:HLB_SHORTCUT_WORKDIR }
if ($env:HLB_SHORTCUT_DESCRIPTION) { $shortcut.Description = $env:HLB_SHORTCUT_DESCRIPTION }
if ($env:HLB_SHORTCUT_ICON) {
    $shortcut.IconLocation = $env:HLB_SHORTCUT_ICON + ',' + $env:HLB_SHORTCUT_ICON_INDEX
}
$shortcut.Save()
$verify = $wsh.CreateShortcut($shortcutPath)
[pscustomobject]@{
    path = $shortcutPath
    target = $verify.TargetPath
    arguments = $verify.Arguments
    working_directory = $verify.WorkingDirectory
    icon_location = $verify.IconLocation
} | ConvertTo-Json -Compress
"""
    result = run_capture(
        job_id,
        shell_argv("powershell", script),
        Path.home(),
        30,
        cancel_event=cancel_event,
        env=env,
    )
    if result.get("exit_code") != 0:
        message = (result.get("stderr") or result.get("stdout") or "shortcut creation failed")[-4000:]
        code = "SHORTCUT_EXISTS" if "SHORTCUT_EXISTS" in message else "SHORTCUT_CREATE_FAILED"
        raise BridgeError(code, message)
    raw = (result.get("stdout") or "").strip()
    try:
        data = json.loads(raw.splitlines()[-1]) if raw else {}
    except json.JSONDecodeError as exc:
        raise BridgeError("SHORTCUT_CREATE_FAILED", "shortcut verification returned invalid JSON") from exc
    data["created"] = True
    return data

def desktop_launch_app(p):
    if os.name != "nt":
        raise BridgeError("DESKTOP_UNSUPPORTED", "app_launch is only available on Windows")
    target = str(p.get("path") or p.get("target") or "").strip()
    if not target:
        raise BridgeError("APP_TARGET_REQUIRED", "params.path or params.target is required")
    args = p.get("args") or []
    if not isinstance(args, list) or any(not isinstance(x, str) for x in args):
        raise BridgeError("INVALID_APP_ARGS", "params.args must be a list of strings")
    cwd = expand_path(p.get("cwd")) if p.get("cwd") else Path.home()
    resolved = shutil.which(target) or target
    launch_policy = console_launch_policy(resolved, p.get("visible_console", False))
    visible_console = launch_policy["visible_console"]
    flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    if os.name == "nt":
        if visible_console:
            flags |= getattr(subprocess, "CREATE_NEW_CONSOLE", 0)
        else:
            flags |= getattr(subprocess, "CREATE_NO_WINDOW", 0)
    try:
        proc = subprocess.Popen(
            [resolved, *args], cwd=str(cwd), stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            env=safe_process_env(), creationflags=flags,
        )
        return {
            "target": target,
            "resolved": str(resolved),
            "pid": proc.pid,
            "started": True,
            "visible_console": visible_console,
            "visible_console_suppressed": launch_policy["visible_console_suppressed"],
        }
    except OSError as direct_error:
        if args:
            raise BridgeError("APP_LAUNCH_FAILED", "direct launch failed and shell association cannot safely accept args") from direct_error
        association_env = safe_process_env()
        association_env["HLB_APP_TARGET"] = target
        association = subprocess.run(
            ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
             "Start-Process -FilePath $env:HLB_APP_TARGET"],
            capture_output=True, text=True, timeout=30, env=association_env,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        if association.returncode != 0:
            raise BridgeError(
                "APP_LAUNCH_FAILED",
                (association.stderr or association.stdout or str(direct_error))[-4000:],
                {"target": target},
            ) from direct_error
        return {"target": target, "pid": None, "started": True, "via": "shell_association"}


def desktop_display_list(job_id, cancel_event):
    command = (
        "Add-Type -AssemblyName System.Windows.Forms; "
        "$i=0; @([System.Windows.Forms.Screen]::AllScreens | ForEach-Object { "
        "[pscustomobject]@{index=$i;device=$_.DeviceName;primary=$_.Primary;"
        "x=$_.Bounds.X;y=$_.Bounds.Y;width=$_.Bounds.Width;height=$_.Bounds.Height;"
        "work_x=$_.WorkingArea.X;work_y=$_.WorkingArea.Y;work_width=$_.WorkingArea.Width;work_height=$_.WorkingArea.Height}; $i++ "
        "}) | ConvertTo-Json -Compress"
    )
    result = run_capture(job_id, shell_argv("powershell", command), Path.home(), 30, cancel_event=cancel_event, env=safe_process_env())
    if result["exit_code"] != 0:
        raise BridgeError("DISPLAY_ENUM_FAILED", result.get("stderr") or "display enumeration failed")
    raw = (result.get("stdout") or "").strip()
    if not raw:
        return []
    try:
        value = json.loads(raw)
    except json.JSONDecodeError as e:
        raise BridgeError("DISPLAY_ENUM_FAILED", "display enumeration returned invalid JSON", {"stdout": raw[-2000:]}) from e
    return value if isinstance(value, list) else [value]

def make_result(job, action, status="completed", exit_code=0, data=None, stdout=None, stderr=None, started_at=None):
    out = {
        "status": status, "exit_code": exit_code, "started_at": started_at or now(), "finished_at": now(),
        "host": current_host(), "action": action,
    }
    if data is not None:
        out["data"] = data
    if stdout is not None:
        out["stdout"] = stdout
    if stderr is not None:
        out["stderr"] = stderr
    return out


def run_job(job_id, job, cancel_event):
    action = str(job.get("action") or job.get("kind") or "codex").lower()
    if action not in DIRECT_ACTIONS:
        raise BridgeError("UNSUPPORTED_ACTION", f"unsupported action: {action}")
    p = params(job)
    started = now()

    if action == "health":
        secret_status = secret_channel_status()
        with STATE_LOCK:
            active_noncontrol = sum(1 for x in RUNNING.values() if x.get("future") is not None)
        capacity = worker_capacity_snapshot(active_noncontrol)
        data = {
            "worker_version": WORKER_VERSION, "protocol": PROTOCOL, "actions": advertised_actions(),
            "host": current_host(), "control_host": CONTROL_HOST, "legacy_default_host": LEGACY_DEFAULT_HOST,
            "auth_mode": auth_mode(), "max_workers": MAX_WORKERS, "rate_limit_per_minute": RATE_LIMIT_PER_MINUTE,
            "resources": capacity,
            "elevated": is_process_elevated(),
            "allowed_roots": [str(x) for x in allowed_roots()],
            "capabilities": {
                "concurrency": True, "job_ttl": True, "idempotency": True, "optional_hmac": False,
                "hmac_required_by_default": True, "legacy_hmac_requires_explicit_opt_in": True,
                "authenticated_relay_results": True, "minimum_hmac_key_bytes": MIN_HMAC_KEY_BYTES,
                "child_environment_secret_stripping": True,
                "binary_files": True, "file_delete": True, "file_copy": True, "output_pagination": True,
                "process_tree_kill": True, "session_timeouts": True, "session_restart_recovery": True,
                "heartbeat": True, "audit_log": True,
                "screenshot": True, "screenshot_all_displays": True,
                "desktop_control": os.name == "nt", "mouse_control": os.name == "nt",
                "keyboard_control": os.name == "nt", "window_control": os.name == "nt",
                "display_enumeration": os.name == "nt", "app_launch": os.name == "nt",
                "desktop_shortcut_create": os.name == "nt",
                "clipboard_read": os.name == "nt", "clipboard_write": os.name == "nt",
                "uia_semantic_control": os.name == "nt", "uia_password_values_redacted": True,
                "uia_password_set_value_blocked": True, "uia_set_value_relay_opt_in": True, "uia_set_value_requires_relay_opt_in": True,
                "clipboard_relay_requires_opt_in": True, "credential_safe_gui_input": secret_status["available"], "public_raw_shell": False,
            },
            "capability_schema": 2,
            "features": {
                "desktop": {"version": 3, "coordinate_fallback": True, "shortcut_create": os.name == "nt"},
                "uia": {
                    "version": 1, "available": os.name == "nt", "backend": "windows-uia-powershell",
                    "actions": sorted(UIA_ACTIONS), "max_nodes": UIA_MAX_NODES, "max_depth": UIA_MAX_DEPTH,
                    "max_wait_ms": UIA_MAX_WAIT_MS, "password_values_exposed": False,
                    "password_set_value_allowed": False, "set_value_requires_relay_opt_in": True,
                },
                "secret_input": {"version": 1, **secret_status},
            },
        }
        return make_result(job, action, data=data, started_at=started)

    if action == "system_info":
        data = {
            "host": current_host(), "user": os.environ.get("USERNAME"),
            "home": str(Path.home()), "platform": os.name, "python": os.sys.version, "cwd": os.getcwd(),
            "drives": [f"{c}:\\" for c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ" if Path(f"{c}:\\").exists()],
            "worker_version": WORKER_VERSION, "protocol": PROTOCOL,
            "elevated": is_process_elevated(),
        }
        return make_result(job, action, data=data, started_at=started)

    if action == "job_status":
        target = str(p.get("job_id") or "")
        with STATE_LOCK:
            info = RUNNING.get(target)
            processed = PROCESSED.get(target)
        if info:
            data = {"job_id": target, "state": "running", "action": info["action"], "started_at": info["started_at"],
                    "cancel_requested": info["cancel_event"].is_set()}
        elif processed:
            data = {"job_id": target, "state": processed.get("status", "completed"), "finished_at": processed.get("finished_at")}
        else:
            data = {"job_id": target, "state": "unknown"}
        return make_result(job, action, data=data, started_at=started)

    if action == "cancel":
        target = str(p.get("job_id") or "")
        if not safe_id(target):
            raise BridgeError("INVALID_JOB_ID", "params.job_id is required")
        with STATE_LOCK:
            info = RUNNING.get(target)
        if not info:
            return make_result(job, action, data={"job_id": target, "cancel_requested": False, "reason": "not_running"}, started_at=started)
        info["cancel_event"].set()
        return make_result(job, action, data={"job_id": target, "cancel_requested": True}, started_at=started)

    if action == "job_output_read":
        data = read_output_chunk(str(p.get("job_id") or ""), str(p.get("stream") or "stdout"),
                                 p.get("offset", 0), p.get("length", 200000))
        return make_result(job, action, data=data, started_at=started)

    if action == "controller_checkpoint":
        data = write_controller_checkpoint(p.get("state"), p.get("expected_previous_cycle_id"))
        return make_result(job, action, data=data, started_at=started)

    if action == "fs_read":
        data = read_text_lines(expand_path(p.get("path"), True), p.get("offset", 0), p.get("length", 1000))
        return make_result(job, action, data=data, started_at=started)

    if action == "fs_read_many":
        paths = p.get("paths") or []
        if not isinstance(paths, list) or not paths:
            raise BridgeError("INVALID_PATHS", "params.paths must be a non-empty list")
        data = []
        for value in paths[:50]:
            try:
                data.append(read_text_lines(expand_path(value, True), p.get("offset", 0), p.get("length", 1000)))
            except Exception as e:
                data.append({"path": str(value), "error": error_dict(e)})
        return make_result(job, action, data=data, started_at=started)

    if action == "fs_read_binary":
        return make_result(job, action, data=read_binary(expand_path(p.get("path"), True), p.get("offset", 0), p.get("length", MAX_BINARY_CHUNK)), started_at=started)

    if action == "fs_write_binary":
        data = write_binary(expand_path(p.get("path"), True), p.get("content_b64", ""), p.get("mode", "rewrite"))
        return make_result(job, action, data=data, started_at=started)

    if action == "fs_write":
        path = expand_path(p.get("path"), True)
        content = str(p.get("content") if p.get("content") is not None else "")
        mode = str(p.get("mode") or "rewrite").lower()
        if mode not in ("rewrite", "append"):
            raise BridgeError("INVALID_MODE", "mode must be rewrite or append")
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("a" if mode == "append" else "w", encoding="utf-8") as f:
            f.write(content)
        return make_result(job, action, data={"path": str(path), "bytes": path.stat().st_size, "mode": mode}, started_at=started)

    if action == "fs_edit":
        path = expand_path(p.get("path"), True)
        old = str(p.get("old_string") if p.get("old_string") is not None else "")
        new = str(p.get("new_string") if p.get("new_string") is not None else "")
        replace_all = bool(p.get("replace_all", False))
        text = path.read_text(encoding="utf-8")
        count = text.count(old)
        if count == 0:
            raise BridgeError("TEXT_NOT_FOUND", "old_string not found")
        if not replace_all and count != 1:
            raise BridgeError("AMBIGUOUS_EDIT", f"old_string occurs {count} times")
        updated = text.replace(old, new) if replace_all else text.replace(old, new, 1)
        atomic_write_text(path, updated)
        return make_result(job, action, data={"path": str(path), "replacements": count if replace_all else 1}, started_at=started)

    if action == "fs_mkdir":
        path = expand_path(p.get("path"), True)
        path.mkdir(parents=True, exist_ok=True)
        return make_result(job, action, data={"path": str(path)}, started_at=started)

    if action == "fs_list":
        data = fs_list(expand_path(p.get("path"), True), p.get("depth", 2), p.get("max_items", 1000), p.get("offset", 0))
        return make_result(job, action, data=data, started_at=started)

    if action == "fs_move":
        src = expand_path(p.get("source"), True)
        dst = expand_path(p.get("destination"), True)
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(src), str(dst))
        return make_result(job, action, data={"source": str(src), "destination": str(dst)}, started_at=started)

    if action == "fs_copy":
        src = expand_path(p.get("source"), True)
        dst = expand_path(p.get("destination"), True)
        recursive = bool(p.get("recursive", False))
        if src.is_dir():
            if not recursive:
                raise BridgeError("RECURSIVE_REQUIRED", "recursive=true is required to copy a directory")
            if dst.exists():
                shutil.copytree(src, dst, dirs_exist_ok=True)
            else:
                shutil.copytree(src, dst)
        else:
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
        return make_result(job, action, data={"source": str(src), "destination": str(dst), "recursive": recursive}, started_at=started)

    if action == "fs_delete":
        path = expand_path(p.get("path"), True)
        recursive = bool(p.get("recursive", False))
        if dangerous_delete_target(path):
            raise BridgeError("DANGEROUS_DELETE_BLOCKED", "refusing to delete a protected root", {"path": str(path)})
        if not path.exists():
            return make_result(job, action, data={"path": str(path), "deleted": False, "reason": "not_found"}, started_at=started)
        if path.is_dir():
            if not recursive:
                raise BridgeError("RECURSIVE_REQUIRED", "recursive=true is required to delete a directory")
            shutil.rmtree(path)
        else:
            path.unlink()
        return make_result(job, action, data={"path": str(path), "deleted": True, "recursive": recursive}, started_at=started)

    if action == "fs_info":
        path = expand_path(p.get("path"), True)
        if not path.exists():
            return make_result(job, action, data={"path": str(path), "exists": False}, started_at=started)
        st = path.stat()
        data = {"path": str(path), "exists": True, "is_file": path.is_file(), "is_dir": path.is_dir(),
                "size": st.st_size, "modified": datetime.fromtimestamp(st.st_mtime, timezone.utc).isoformat()}
        return make_result(job, action, data=data, started_at=started)

    if action == "fs_search":
        return make_result(job, action, data=fs_search(p), started_at=started)

    if action == "proc_run":
        command = str(p.get("command") or "")
        if not command.strip():
            raise BridgeError("MISSING_COMMAND", "params.command is required")
        timeout = max(1, min(int(p.get("timeout_seconds") or 1800), MAX_TIMEOUT))
        result = run_capture(job_id, shell_argv(p.get("shell"), command), expand_path(p.get("cwd")), timeout,
                             cancel_event=cancel_event, env=build_env(p))
        result.update({"host": current_host(), "action": action})
        return result

    if action == "proc_start":
        return make_result(job, action, data=start_session(p), started_at=started)

    if action == "proc_read":
        sid = str(p.get("session_id") or "")
        with STATE_LOCK:
            s = SESSIONS.get(sid)
        if not s:
            raise BridgeError("UNKNOWN_SESSION", "unknown session_id")
        try:
            if s.get("out_f") is not None:
                s["out_f"].flush()
            if s.get("err_f") is not None:
                s["err_f"].flush()
        except Exception:
            pass
        touch_session_activity(sid, s)
        data = session_snapshot(sid, s)
        max_chars = max(1000, min(int(p.get("max_chars") or MAX_OUTPUT_TAIL), 500000))
        data["stdout"] = read_tail(s["stdout_path"], max_chars)
        data["stderr"] = read_tail(s["stderr_path"], max_chars)
        return make_result(job, action, data=data, started_at=started)

    if action == "proc_input":
        sid = str(p.get("session_id") or "")
        with STATE_LOCK:
            s = SESSIONS.get(sid)
        if not s:
            raise BridgeError("UNKNOWN_SESSION", "unknown session_id")
        if s.get("recovered"):
            raise BridgeError(
                "SESSION_INPUT_UNAVAILABLE",
                "stdin cannot be reattached after worker restart; recovered session is read/kill only",
                {"session_id": sid, "pid": int(s.get("pid") or 0)},
            )
        if s["proc"].poll() is not None:
            raise BridgeError("SESSION_EXITED", "session process already exited")
        value = str(p.get("input") if p.get("input") is not None else "")
        s["proc"].stdin.write(value + ("\n" if bool(p.get("newline", True)) else ""))
        s["proc"].stdin.flush()
        touch_session_activity(sid, s)
        return make_result(job, action, data=session_snapshot(sid, s), started_at=started)

    if action == "proc_kill":
        sid = str(p.get("session_id") or "")
        with STATE_LOCK:
            s = SESSIONS.get(sid)
        if not s:
            raise BridgeError("UNKNOWN_SESSION", "unknown session_id")
        if s.get("recovered"):
            kill_recovered_session(sid, s, bool(p.get("force", True)))
        elif s["proc"].poll() is None:
            kill_process_tree(s["proc"].pid, bool(p.get("force", True)))
            try:
                s["proc"].wait(timeout=10)
            except subprocess.TimeoutExpired:
                kill_process_tree(s["proc"].pid, True)
        if not s.get("recovered"):
            s["exit_code"] = s["proc"].poll()
            persist_session_metadata(sid, s)
        return make_result(job, action, data=session_snapshot(sid, s), started_at=started)

    if action == "proc_list_sessions":
        cleanup_sessions()
        with STATE_LOCK:
            data = [session_snapshot(sid, s) for sid, s in SESSIONS.items()]
        return make_result(job, action, data=data, started_at=started)

    if action == "wait_for":
        return make_result(job, action, data=wait_for_condition(p, cancel_event), started_at=started)

    if action == "display_list":
        return make_result(job, action, data=desktop_display_list(job_id, cancel_event), started_at=started)

    if action == "clipboard_read":
        if not bool(p.get("allow_relay", False)):
            raise BridgeError(
                "CLIPBOARD_RELAY_OPT_IN_REQUIRED",
                "clipboard_read returns clipboard text through the private relay; set params.allow_relay=true only for an explicit clipboard-read request",
            )
        return make_result(job, action, data=desktop_clipboard_read(), started_at=started)

    if action == "clipboard_write":
        return make_result(job, action, data=desktop_clipboard_write(p.get("text")), started_at=started)

    if action == "app_launch":
        return make_result(job, action, data=desktop_launch_app(p), started_at=started)

    if action == "desktop_shortcut_create":
        return make_result(job, action, data=desktop_create_shortcut(job_id, p, cancel_event), started_at=started)

    if action == "window_list":
        return make_result(
            job, action,
            data=_desktop_window_rows(p.get("limit", 200), bool(p.get("visible_only", True))),
            started_at=started,
        )

    if action == "window_focus":
        return make_result(job, action, data=desktop_focus_window(p), started_at=started)

    if action == "window_move":
        return make_result(job, action, data=desktop_move_window(p), started_at=started)

    if action == "window_state":
        return make_result(job, action, data=desktop_window_state(p), started_at=started)

    if action == "window_close":
        return make_result(job, action, data=desktop_close_window(p), started_at=started)

    if action == "gui_cursor_get":
        x, y = _cursor_position()
        return make_result(job, action, data={"x": x, "y": y}, started_at=started)

    if action == "gui_mouse_move":
        return make_result(job, action, data=desktop_mouse_move(p), started_at=started)

    if action == "gui_mouse_button":
        return make_result(job, action, data=desktop_mouse_button(p), started_at=started)

    if action == "gui_mouse_click":
        return make_result(job, action, data=desktop_mouse_click(p), started_at=started)

    if action == "gui_mouse_scroll":
        return make_result(job, action, data=desktop_mouse_scroll(p), started_at=started)

    if action == "gui_key":
        return make_result(job, action, data=desktop_key(p), started_at=started)

    if action == "gui_type":
        return make_result(job, action, data=desktop_type_text(p), started_at=started)

    if action == "gui_type_secret":
        return make_result(job, action, data=desktop_type_secret(p), started_at=started)

    if action in UIA_ACTIONS:
        return make_result(job, action, data=desktop_uia(p, action), started_at=started)

    if action == "proc_list":
        limit = max(1, min(int(p.get("limit") or 200), 500))
        command = f"Get-Process | Sort-Object CPU -Descending | Select-Object -First {limit} Id,ProcessName,CPU,WorkingSet64,Path | ConvertTo-Json -Depth 3"
        result = run_capture(job_id, shell_argv("powershell", command), Path.home(), 30, cancel_event=cancel_event, env=safe_process_env())
        result.update({"host": current_host(), "action": action})
        return result

    if action == "screenshot":
        SCREENSHOTS_DIR.mkdir(parents=True, exist_ok=True)
        target = SCREENSHOTS_DIR / f"{job_id}.png"
        ps_target = str(target).replace("'", "''")
        scope = str(p.get("scope") or "primary").lower()
        monitor_index = p.get("monitor_index")
        if scope not in ("primary", "all", "monitor"):
            raise BridgeError("INVALID_SCREENSHOT_SCOPE", "scope must be primary, all, or monitor")
        if scope == "monitor" and monitor_index is None:
            raise BridgeError("MONITOR_INDEX_REQUIRED", "monitor_index is required when scope=monitor")
        if scope == "all":
            bounds_expr = "[System.Windows.Forms.SystemInformation]::VirtualScreen"
        elif scope == "monitor":
            bounds_expr = f"[System.Windows.Forms.Screen]::AllScreens[{int(monitor_index)}].Bounds"
        else:
            bounds_expr = "[System.Windows.Forms.Screen]::PrimaryScreen.Bounds"
        command = (
            "Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; "
            f"$b={bounds_expr}; "
            "$bmp=New-Object System.Drawing.Bitmap $b.Width,$b.Height; "
            "$g=[System.Drawing.Graphics]::FromImage($bmp); "
            "$g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size); "
            f"$bmp.Save('{ps_target}',[System.Drawing.Imaging.ImageFormat]::Png); "
            "$g.Dispose(); $bmp.Dispose(); "
            "$b.X.ToString()+','+$b.Y.ToString()+','+$b.Width.ToString()+'x'+$b.Height.ToString()"
        )
        result = run_capture(job_id, shell_argv("powershell", command), Path.home(), 30, cancel_event=cancel_event, env=safe_process_env())
        if result["exit_code"] != 0 or not target.exists():
            raise BridgeError("SCREENSHOT_FAILED", result.get("stderr") or "screenshot capture failed")
        return make_result(
            job, action,
            data={
                "path": str(target), "bytes": target.stat().st_size,
                "display": result.get("stdout", "").strip(), "scope": scope,
                "monitor_index": int(monitor_index) if monitor_index is not None else None,
            },
            started_at=started,
        )

    # Raw fallback actions. These are intentionally retained only for compatibility/recovery.
    payload = job.get("payload")
    if not isinstance(payload, str) or not payload.strip():
        raise BridgeError("MISSING_PAYLOAD", "payload must be a non-empty string")
    cwd = expand_path(job.get("cwd"))
    timeout = max(1, min(int(job.get("timeout_seconds") or 1800), MAX_TIMEOUT))
    if action in ("powershell", "cmd", "python"):
        argv = shell_argv(action, payload)
        stdin = None
    else:
        codex = find_codex()
        if codex.lower().endswith((".cmd", ".bat")):
            argv = ["cmd.exe", "/d", "/s", "/c", "call", codex, "exec", "--sandbox", "workspace-write", "--skip-git-repo-check", "-"]
        else:
            argv = [codex, "exec", "--sandbox", "workspace-write", "--skip-git-repo-check", "-"]
        stdin = payload
    result = run_capture(job_id, argv, cwd, timeout, stdin=stdin, cancel_event=cancel_event, env=safe_process_env())
    result.update({"host": current_host(), "action": action})
    return result


def error_dict(exc):
    if isinstance(exc, BridgeError):
        return exc.as_dict()
    return {"code": "INTERNAL_ERROR", "message": str(exc) or exc.__class__.__name__}


def result_envelope(job_id, job, result, digest=None):
    out = {"id": job_id, "source": job.get("source"), "created_at": job.get("created_at"), **result}
    if digest:
        out["job_hash"] = digest
    return out


def failure_result(job_id, job, exc, digest=None):
    action = str(job.get("action") or job.get("kind") or "unknown")
    err = error_dict(exc)
    result = {
        "status": "error", "exit_code": 1, "started_at": now(), "finished_at": now(),
        "host": current_host(), "action": action,
        "error": err, "stderr": err["message"], "stdout": "",
    }
    return result_envelope(job_id, job, result, digest)


def validate_job(job_id, job):
    job_target_host(job)
    if not safe_id(job_id):
        raise BridgeError("INVALID_JOB_ID", "invalid queue filename/job id")
    if str(job.get("id") or "") != job_id:
        raise BridgeError("JOB_ID_MISMATCH", "job.id must match queue filename", {"filename_id": job_id, "job_id": job.get("id")})
    source = job.get("source")
    if source not in (PROTOCOL, LEGACY_PROTOCOL):
        raise BridgeError("INVALID_PROTOCOL", "unsupported source/protocol", {"source": source})
    validate_job_time(job)
    verify_auth(job)
    return job_hash(job)


def cache_result(job_id, body):
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    path = CACHE_DIR / f"{job_id}.json"
    atomic_write_text(path, json.dumps(body, indent=2, ensure_ascii=False))
    return path


def publish_result(job_id, job, body):
    publish_json(f"heaven-bridge/results/{job_id}.json", body, f"heaven bridge result {job_id}")


def publish_status(job_id, state, action=None, **extra):
    body = {"id": job_id, "state": state, "action": action, "host": current_host(),
            "worker_version": WORKER_VERSION, "protocol": PROTOCOL, "updated_at": now(), **extra}
    try:
        publish_json(f"heaven-bridge/status/{job_id}.json", body, f"heaven bridge status {job_id} {state}", max_attempts=3)
    except Exception as e:
        log(f"status publish failed for {job_id}: {e}")


def execute_job(job_id, job, digest, cancel_event):
    action = str(job.get("action") or job.get("kind") or "codex").lower()
    audit("job_start", id=job_id, action=action, hash=digest)
    publish_status(job_id, "running", action=action)
    try:
        result = run_job(job_id, job, cancel_event)
        body = result_envelope(job_id, job, result, digest)
    except Exception as e:
        body = failure_result(job_id, job, e, digest)
    cache = cache_result(job_id, body)
    try:
        publish_result(job_id, job, body)
    finally:
        mark_processed(job_id, digest, body.get("status", "unknown"), cache)
        publish_status(job_id, body.get("status", "unknown"), action=action, exit_code=body.get("exit_code"))
        audit("job_finish", id=job_id, action=action, hash=digest, status=body.get("status"), exit_code=body.get("exit_code"),
              error_code=(body.get("error") or {}).get("code"))
        with STATE_LOCK:
            RUNNING.pop(job_id, None)
        try:
            (LOCKS_DIR / f"{job_id}.lock").unlink(missing_ok=True)
        except OSError:
            pass
    return body


def rate_limit_ok():
    t = time.monotonic()
    while RECENT_STARTS and t - RECENT_STARTS[0] > 60:
        RECENT_STARTS.popleft()
    if len(RECENT_STARTS) >= RATE_LIMIT_PER_MINUTE:
        return False
    RECENT_STARTS.append(t)
    return True


def claim(job_id):
    LOCKS_DIR.mkdir(parents=True, exist_ok=True)
    path = LOCKS_DIR / f"{job_id}.lock"
    try:
        fd = os.open(str(path), os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        os.write(fd, f"{os.getpid()} {now()}".encode("utf-8"))
        os.close(fd)
        return True
    except FileExistsError:
        return False


def heartbeat(force=False):
    global LAST_HEARTBEAT
    t = time.monotonic()
    if not force and t - LAST_HEARTBEAT < HEARTBEAT_SECONDS:
        return
    with STATE_LOCK:
        running = [{"id": jid, "action": info["action"], "started_at": info["started_at"]} for jid, info in RUNNING.items()]
        active_noncontrol = sum(1 for info in RUNNING.values() if info.get("future") is not None)
    host = current_host()
    body = {
        "host": host, "pid": os.getpid(), "worker_version": WORKER_VERSION,
        "protocol": PROTOCOL, "auth_mode": auth_mode(), "elevated": is_process_elevated(), "updated_at": now(), "running": running,
        "resources": worker_capacity_snapshot(active_noncontrol),
        "capabilities": advertised_actions(), "control_host": CONTROL_HOST, "legacy_default_host": LEGACY_DEFAULT_HOST,
    }
    try:
        publish_json(f"heaven-bridge/status/hosts/{host}/heartbeat.json", body, f"heaven bridge heartbeat {host}", max_attempts=3)
        if host == LEGACY_DEFAULT_HOST:
            publish_json("heaven-bridge/status/heartbeat.json", body, "heaven bridge heartbeat legacy", max_attempts=3)
        LAST_HEARTBEAT = t
    except Exception as e:
        log(f"heartbeat publish failed: {e}")


def write_local_heartbeat():
    """Write a Git/network-independent liveness signal for the local watchdog."""
    body = {
        "host": current_host(),
        "pid": os.getpid(),
        "worker_version": WORKER_VERSION,
        "protocol": PROTOCOL,
        "updated_at": now(),
    }
    atomic_write_text(LOCAL_HEARTBEAT, json.dumps(body, indent=2, ensure_ascii=False))


def local_heartbeat_loop(stop_event):
    while not stop_event.is_set():
        try:
            write_local_heartbeat()
        except Exception as e:
            log(f"local heartbeat write failed: {e}")
        stop_event.wait(LOCAL_HEARTBEAT_SECONDS)


def write_loop_progress():
    body = {
        "host": current_host(),
        "pid": os.getpid(),
        "worker_version": WORKER_VERSION,
        "protocol": PROTOCOL,
        "updated_at": now(),
    }
    atomic_write_text(LOCAL_PROGRESS, json.dumps(body, indent=2, ensure_ascii=False))


def restore_cached_result(job_id, row):
    cache_path = Path(row.get("cache_path") or "")
    if not cache_path.exists():
        return False
    try:
        body = json.loads(cache_path.read_text(encoding="utf-8"))
        publish_json(f"heaven-bridge/results/{job_id}.json", body, f"heaven bridge restore result {job_id}")
        return True
    except Exception as e:
        log(f"restore cached result failed for {job_id}: {e}")
        return False


def queue_priority_rank(value):
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        numeric = max(0.0, min(float(value), 100.0))
        if numeric >= 75:
            return 0
        if numeric >= 50:
            return 1
        if numeric >= 25:
            return 2
        return 3
    labels = {
        "highest": 0, "critical": 0, "urgent": 0,
        "high": 1,
        "normal": 2, "default": 2,
        "low": 3,
        "lowest": 4,
    }
    return labels.get(str(value or "normal").strip().lower(), 2)


def queue_order_key(path, current=None):
    """Order control jobs first, then effective priority, then FIFO created_at.

    Ordinary jobs age toward the highest priority so sustained high-priority
    traffic cannot starve older low-priority work. Malformed jobs stay
    processable so normal validation can publish a structured failure.
    """
    current = current or utcnow()
    try:
        job = json.loads(path.read_text(encoding="utf-8-sig"))
    except Exception:
        return (1, 2, 0.0, path.name)

    action = str(job.get("action") or job.get("kind") or "codex").lower()
    try:
        created = parse_time(job.get("created_at"))
        created_rank = created.timestamp()
    except Exception:
        created = current
        created_rank = 0.0

    if action in CONTROL_ACTIONS:
        return (0, 0, created_rank, path.name)

    base = queue_priority_rank(job.get("priority"))
    age_seconds = max(0.0, (current - created).total_seconds())
    promotions = int(age_seconds // QUEUE_PRIORITY_AGING_SECONDS)
    effective = max(0, base - promotions)
    return (1, effective, created_rank, path.name)


def process_queue(executor):
    git_sync()
    QUEUE.mkdir(parents=True, exist_ok=True)
    RESULTS.mkdir(parents=True, exist_ok=True)
    STATUS_DIR.mkdir(parents=True, exist_ok=True)

    files = sorted(QUEUE.glob("*.json"), key=queue_order_key)
    with STATE_LOCK:
        initial_active_noncontrol = sum(1 for x in RUNNING.values() if x.get("future") is not None)
    capacity = worker_capacity_snapshot(initial_active_noncontrol)
    start_budget = min(MAX_STARTS_PER_TICK, capacity["available_start_slots"])

    for path in files:
        job_id = path.stem
        if not safe_id(job_id):
            continue
        if (RESULTS / f"{job_id}.json").exists():
            continue
        with STATE_LOCK:
            if job_id in RUNNING:
                continue
        try:
            job = json.loads(path.read_text(encoding="utf-8-sig"))
            if not job_targets_this_worker(job):
                continue
            digest = validate_job(job_id, job)
        except Exception as e:
            dummy = {}
            try:
                dummy = json.loads(path.read_text(encoding="utf-8-sig"))
            except Exception:
                pass
            body = failure_result(job_id, dummy, e)
            try:
                publish_result(job_id, dummy, body)
            except Exception as pub:
                log(f"failed to publish validation error {job_id}: {pub}")
            continue

        previous = PROCESSED.get(job_id)
        if previous:
            if previous.get("hash") != digest:
                body = failure_result(job_id, job, BridgeError("DUPLICATE_JOB_ID", "job id was previously used with different content"), digest)
                publish_result(job_id, job, body)
            else:
                restore_cached_result(job_id, previous)
            continue

        action = str(job.get("action") or job.get("kind") or "codex").lower()
        if action not in CONTROL_ACTIONS and start_budget <= 0:
            continue
        if action not in CONTROL_ACTIONS and not rate_limit_ok():
            log("rate limit reached; deferring non-control jobs")
            continue

        if not claim(job_id):
            continue
        cancel_event = threading.Event()
        info = {"action": action, "started_at": now(), "cancel_event": cancel_event, "digest": digest}
        with STATE_LOCK:
            RUNNING[job_id] = info

        if action in CONTROL_ACTIONS:
            execute_job(job_id, job, digest, cancel_event)
        else:
            future = executor.submit(execute_job, job_id, job, digest, cancel_event)
            info["future"] = future
            start_budget -= 1



def clean_stale_locks():
    LOCKS_DIR.mkdir(parents=True, exist_ok=True)
    cutoff = time.time() - max(MAX_TIMEOUT, DEFAULT_SESSION_MAX, 21600)
    for p in LOCKS_DIR.glob("*.lock"):
        if p == WORKER_LOCK_PATH:
            continue
        try:
            if p.stat().st_mtime < cutoff:
                p.unlink(missing_ok=True)
        except OSError:
            pass


def main():
    acquire_worker_instance_lock()
    try:
        for d in (STATE, SESSIONS_DIR, OUTPUTS_DIR, SCREENSHOTS_DIR, LOCKS_DIR, CACHE_DIR):
            d.mkdir(parents=True, exist_ok=True)
        load_processed()
        clean_stale_locks()
        recovery = recover_sessions()
        log(
            f"worker starting host={current_host()} version={WORKER_VERSION} protocol={PROTOCOL} max_workers={MAX_WORKERS} "
            f"auto_max_workers={AUTO_MAX_WORKERS} max_starts_per_tick={MAX_STARTS_PER_TICK} "
            f"auth={auth_mode()} sessions_loaded={recovery['loaded']} sessions_live={recovery['live']} "
            f"sessions_blocked={recovery['blocked']}"
        )
        audit("worker_start", pid=os.getpid(), version=WORKER_VERSION, protocol=PROTOCOL, auth_mode=auth_mode())
        local_heartbeat_stop = threading.Event()
        write_local_heartbeat()
        local_heartbeat_thread = threading.Thread(
            target=local_heartbeat_loop,
            args=(local_heartbeat_stop,),
            name="heaven-local-heartbeat",
            daemon=True,
        )
        local_heartbeat_thread.start()
        executor = concurrent.futures.ThreadPoolExecutor(max_workers=MAX_WORKERS, thread_name_prefix="heaven-job")
        try:
            while True:
                try:
                    write_loop_progress()
                    cleanup_sessions()
                    heartbeat()
                    process_queue(executor)
                    time.sleep(1.5)
                except KeyboardInterrupt:
                    break
                except Exception as e:
                    log(f"loop error: {e}")
                    audit("loop_error", error_code=error_dict(e).get("code"), message=str(e)[:1000])
                    time.sleep(5)
        finally:
            with STATE_LOCK:
                for info in RUNNING.values():
                    info["cancel_event"].set()
            executor.shutdown(wait=False, cancel_futures=True)
            local_heartbeat_stop.set()
            local_heartbeat_thread.join(timeout=2)
            try:
                for local_path in (LOCAL_HEARTBEAT, LOCAL_PROGRESS):
                    if local_path.exists():
                        row = json.loads(local_path.read_text(encoding="utf-8"))
                        if int(row.get("pid") or 0) == os.getpid():
                            local_path.unlink(missing_ok=True)
            except Exception as e:
                log(f"local heartbeat cleanup failed: {e}")
            audit("worker_stop", pid=os.getpid())
            log("worker exiting")
    finally:
        release_worker_instance_lock()


if __name__ == "__main__":
    main()
