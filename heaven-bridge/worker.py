import base64
import collections
import concurrent.futures
import hashlib
import hmac
import json
import os
import re
import shutil
import subprocess
import threading
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path

WORKER_VERSION = 3
PROTOCOL = "chatgpt-heaven-bridge-v2"
LEGACY_PROTOCOL = "chatgpt-heaven-bridge-v1"
BRANCH = "heaven-bridge"

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

MAX_TIMEOUT = int(os.environ.get("HEAVEN_BRIDGE_MAX_TIMEOUT", "7200"))
MAX_OUTPUT_TAIL = int(os.environ.get("HEAVEN_BRIDGE_MAX_OUTPUT_TAIL", "60000"))
MAX_READ_BYTES = int(os.environ.get("HEAVEN_BRIDGE_MAX_READ_BYTES", "2000000"))
MAX_BINARY_CHUNK = int(os.environ.get("HEAVEN_BRIDGE_MAX_BINARY_CHUNK", "1000000"))
MAX_SEARCH_FILE_BYTES = int(os.environ.get("HEAVEN_BRIDGE_MAX_SEARCH_FILE_BYTES", "2000000"))
MAX_RESULTS = int(os.environ.get("HEAVEN_BRIDGE_MAX_RESULTS", "500"))
MAX_WORKERS = max(2, min(int(os.environ.get("HEAVEN_BRIDGE_MAX_WORKERS", "4")), 8))
DEFAULT_JOB_TTL = int(os.environ.get("HEAVEN_BRIDGE_DEFAULT_TTL", "21600"))
MAX_JOB_TTL = int(os.environ.get("HEAVEN_BRIDGE_MAX_TTL", "86400"))
FUTURE_SKEW_SECONDS = int(os.environ.get("HEAVEN_BRIDGE_FUTURE_SKEW", "300"))
HEARTBEAT_SECONDS = max(120, int(os.environ.get("HEAVEN_BRIDGE_HEARTBEAT_SECONDS", "300")))
RATE_LIMIT_PER_MINUTE = max(10, int(os.environ.get("HEAVEN_BRIDGE_RATE_PER_MINUTE", "60")))
DEFAULT_SESSION_IDLE = int(os.environ.get("HEAVEN_BRIDGE_SESSION_IDLE", "1800"))
DEFAULT_SESSION_MAX = int(os.environ.get("HEAVEN_BRIDGE_SESSION_MAX", "14400"))

GIT_LOCK = threading.RLock()
STATE_LOCK = threading.RLock()
RUNNING = {}
SESSIONS = {}
PROCESSED = {}
RECENT_STARTS = collections.deque()
LAST_HEARTBEAT = 0.0

SENSITIVE_ENV_RE = re.compile(r"(PASS|PASSWORD|TOKEN|SECRET|API[_-]?KEY|PRIVATE[_-]?KEY|COOKIE|AUTH)", re.I)

DIRECT_ACTIONS = {
    "health", "system_info", "job_status", "cancel", "job_output_read",
    "fs_read", "fs_read_many", "fs_write", "fs_edit", "fs_mkdir",
    "fs_list", "fs_move", "fs_copy", "fs_delete", "fs_info", "fs_search",
    "fs_read_binary", "fs_write_binary",
    "proc_run", "proc_start", "proc_read", "proc_input", "proc_kill",
    "proc_list_sessions", "proc_list", "screenshot",
    "powershell", "cmd", "python", "codex",
}
CONTROL_ACTIONS = {
    "health", "system_info", "job_status", "cancel",
    "proc_read", "proc_input", "proc_kill", "proc_list_sessions",
}
RAW_ACTIONS = {"powershell", "cmd", "python", "codex"}


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


def git(*args, check=True, timeout=120):
    p = subprocess.run(
        ["git", *args],
        cwd=ROOT,
        capture_output=True,
        text=True,
        timeout=timeout,
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
    atomic_write_text(target, json.dumps(body, indent=2, ensure_ascii=False))
    with GIT_LOCK:
        last = None
        for attempt in range(max_attempts):
            try:
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


def safe_id(name):
    return bool(re.fullmatch(r"[A-Za-z0-9._-]{1,120}", str(name)))


def canonical_job(job):
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


def auth_mode():
    return "hmac-sha256" if os.environ.get("HEAVEN_BRIDGE_HMAC_KEY") else "private-repo-acl"


def verify_auth(job):
    key = os.environ.get("HEAVEN_BRIDGE_HMAC_KEY")
    if not key:
        return {"mode": "private-repo-acl", "verified": True}
    auth = job.get("auth") if isinstance(job.get("auth"), dict) else {}
    signature = str(auth.get("signature") or "")
    if not re.fullmatch(r"[0-9a-fA-F]{64}", signature):
        raise BridgeError("AUTH_REQUIRED", "HMAC signature is required")
    expected = hmac.new(key.encode("utf-8"), canonical_job(job), hashlib.sha256).hexdigest()
    if not hmac.compare_digest(signature.lower(), expected.lower()):
        raise BridgeError("AUTH_INVALID", "HMAC signature verification failed")
    return {"mode": "hmac-sha256", "verified": True}


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


def build_env(p):
    env = os.environ.copy()
    requested = p.get("env_from_host") or []
    if requested:
        if not isinstance(requested, list):
            raise BridgeError("INVALID_ENV", "env_from_host must be a list of variable names")
        selected = {}
        for name in requested[:100]:
            name = str(name)
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
        subprocess.run(args, capture_output=True, text=True, timeout=30, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
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
            stdout=out_f, stderr=err_f, env=env, creationflags=flags,
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


def session_snapshot(sid, s):
    proc = s["proc"]
    now_mono = time.monotonic()
    return {
        "session_id": sid, "pid": proc.pid, "running": proc.poll() is None, "exit_code": proc.poll(),
        "shell": s["shell"], "command": s["command"], "cwd": s["cwd"], "started_at": s["started_at"],
        "last_activity_at": s["last_activity_at"], "idle_seconds": int(max(0, now_mono - s["last_activity_mono"])),
        "idle_timeout_seconds": s["idle_timeout_seconds"], "max_runtime_seconds": s["max_runtime_seconds"],
        "stdout_path": str(s["stdout_path"]), "stderr_path": str(s["stderr_path"]),
    }


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
    t = time.monotonic()
    entry = {
        "proc": proc, "out_f": out_f, "err_f": err_f, "stdout_path": stdout_path, "stderr_path": stderr_path,
        "shell": shell, "command": command, "cwd": str(cwd), "started_at": now(),
        "started_mono": t, "last_activity_mono": t, "last_activity_at": now(),
        "idle_timeout_seconds": max(60, min(int(p.get("idle_timeout_seconds") or DEFAULT_SESSION_IDLE), 86400)),
        "max_runtime_seconds": max(300, min(int(p.get("max_runtime_seconds") or DEFAULT_SESSION_MAX), 172800)),
    }
    with STATE_LOCK:
        SESSIONS[sid] = entry
    return session_snapshot(sid, entry)


def cleanup_sessions():
    with STATE_LOCK:
        items = list(SESSIONS.items())
    t = time.monotonic()
    for sid, s in items:
        proc = s["proc"]
        if proc.poll() is None:
            expired = (t - s["started_mono"]) > s["max_runtime_seconds"]
            idle = (t - s["last_activity_mono"]) > s["idle_timeout_seconds"]
            if expired or idle:
                kill_process_tree(proc.pid, True)
                audit("session_cleanup", session_id=sid, pid=proc.pid, reason="max_runtime" if expired else "idle_timeout")
        if proc.poll() is not None:
            try:
                s["out_f"].flush()
                s["err_f"].flush()
            except Exception:
                pass


def make_result(job, action, status="completed", exit_code=0, data=None, stdout=None, stderr=None, started_at=None):
    out = {
        "status": status, "exit_code": exit_code, "started_at": started_at or now(), "finished_at": now(),
        "host": os.environ.get("COMPUTERNAME", "heaven"), "action": action,
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
        data = {
            "worker_version": WORKER_VERSION, "protocol": PROTOCOL, "actions": sorted(DIRECT_ACTIONS),
            "auth_mode": auth_mode(), "max_workers": MAX_WORKERS, "rate_limit_per_minute": RATE_LIMIT_PER_MINUTE,
            "allowed_roots": [str(x) for x in allowed_roots()],
            "capabilities": {
                "concurrency": True, "job_ttl": True, "idempotency": True, "optional_hmac": True,
                "binary_files": True, "file_delete": True, "file_copy": True, "output_pagination": True,
                "process_tree_kill": True, "session_timeouts": True, "heartbeat": True, "audit_log": True,
                "screenshot": True, "clipboard_read": False, "public_raw_shell": False,
            },
        }
        return make_result(job, action, data=data, started_at=started)

    if action == "system_info":
        data = {
            "host": os.environ.get("COMPUTERNAME", "heaven"), "user": os.environ.get("USERNAME"),
            "home": str(Path.home()), "platform": os.name, "python": os.sys.version, "cwd": os.getcwd(),
            "drives": [f"{c}:\\" for c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ" if Path(f"{c}:\\").exists()],
            "worker_version": WORKER_VERSION, "protocol": PROTOCOL,
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
        result.update({"host": os.environ.get("COMPUTERNAME", "heaven"), "action": action})
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
            s["out_f"].flush()
            s["err_f"].flush()
        except Exception:
            pass
        s["last_activity_mono"] = time.monotonic()
        s["last_activity_at"] = now()
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
        if s["proc"].poll() is not None:
            raise BridgeError("SESSION_EXITED", "session process already exited")
        value = str(p.get("input") if p.get("input") is not None else "")
        s["proc"].stdin.write(value + ("\n" if bool(p.get("newline", True)) else ""))
        s["proc"].stdin.flush()
        s["last_activity_mono"] = time.monotonic()
        s["last_activity_at"] = now()
        return make_result(job, action, data=session_snapshot(sid, s), started_at=started)

    if action == "proc_kill":
        sid = str(p.get("session_id") or "")
        with STATE_LOCK:
            s = SESSIONS.get(sid)
        if not s:
            raise BridgeError("UNKNOWN_SESSION", "unknown session_id")
        if s["proc"].poll() is None:
            kill_process_tree(s["proc"].pid, bool(p.get("force", True)))
            try:
                s["proc"].wait(timeout=10)
            except subprocess.TimeoutExpired:
                kill_process_tree(s["proc"].pid, True)
        return make_result(job, action, data=session_snapshot(sid, s), started_at=started)

    if action == "proc_list_sessions":
        cleanup_sessions()
        with STATE_LOCK:
            data = [session_snapshot(sid, s) for sid, s in SESSIONS.items()]
        return make_result(job, action, data=data, started_at=started)

    if action == "proc_list":
        limit = max(1, min(int(p.get("limit") or 200), 500))
        command = f"Get-Process | Sort-Object CPU -Descending | Select-Object -First {limit} Id,ProcessName,CPU,WorkingSet64,Path | ConvertTo-Json -Depth 3"
        result = run_capture(job_id, shell_argv("powershell", command), Path.home(), 30, cancel_event=cancel_event, env=os.environ.copy())
        result.update({"host": os.environ.get("COMPUTERNAME", "heaven"), "action": action})
        return result

    if action == "screenshot":
        SCREENSHOTS_DIR.mkdir(parents=True, exist_ok=True)
        target = SCREENSHOTS_DIR / f"{job_id}.png"
        ps_target = str(target).replace("'", "''")
        command = (
            "Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; "
            "$b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; "
            "$bmp=New-Object System.Drawing.Bitmap $b.Width,$b.Height; "
            "$g=[System.Drawing.Graphics]::FromImage($bmp); "
            "$g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size); "
            f"$bmp.Save('{ps_target}',[System.Drawing.Imaging.ImageFormat]::Png); "
            "$g.Dispose(); $bmp.Dispose(); "
            "$b.Width.ToString()+'x'+$b.Height.ToString()"
        )
        result = run_capture(job_id, shell_argv("powershell", command), Path.home(), 30, cancel_event=cancel_event, env=os.environ.copy())
        if result["exit_code"] != 0 or not target.exists():
            raise BridgeError("SCREENSHOT_FAILED", result.get("stderr") or "screenshot capture failed")
        return make_result(job, action, data={"path": str(target), "bytes": target.stat().st_size, "display": result.get("stdout", "").strip()}, started_at=started)

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
            argv = ["cmd.exe", "/d", "/s", "/c", f'"{codex}" exec --skip-git-repo-check -']
        else:
            argv = [codex, "exec", "--skip-git-repo-check", "-"]
        stdin = payload
    result = run_capture(job_id, argv, cwd, timeout, stdin=stdin, cancel_event=cancel_event, env=os.environ.copy())
    result.update({"host": os.environ.get("COMPUTERNAME", "heaven"), "action": action})
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
        "host": os.environ.get("COMPUTERNAME", "heaven"), "action": action,
        "error": err, "stderr": err["message"], "stdout": "",
    }
    return result_envelope(job_id, job, result, digest)


def validate_job(job_id, job):
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
    body = {"id": job_id, "state": state, "action": action, "host": os.environ.get("COMPUTERNAME", "heaven"),
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
    body = {
        "host": os.environ.get("COMPUTERNAME", "heaven"), "pid": os.getpid(), "worker_version": WORKER_VERSION,
        "protocol": PROTOCOL, "auth_mode": auth_mode(), "updated_at": now(), "running": running,
        "capabilities": sorted(DIRECT_ACTIONS),
    }
    try:
        publish_json("heaven-bridge/status/heartbeat.json", body, "heaven bridge heartbeat", max_attempts=3)
        LAST_HEARTBEAT = t
    except Exception as e:
        log(f"heartbeat publish failed: {e}")


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


def process_queue(executor):
    git_sync()
    QUEUE.mkdir(parents=True, exist_ok=True)
    RESULTS.mkdir(parents=True, exist_ok=True)
    STATUS_DIR.mkdir(parents=True, exist_ok=True)

    files = sorted(QUEUE.glob("*.json"))
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

        if not rate_limit_ok():
            log("rate limit reached; deferring new jobs")
            break

        action = str(job.get("action") or job.get("kind") or "codex").lower()
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

        with STATE_LOCK:
            active_noncontrol = sum(1 for x in RUNNING.values() if x.get("future") is not None)
        if active_noncontrol >= MAX_WORKERS:
            break


def clean_stale_locks():
    LOCKS_DIR.mkdir(parents=True, exist_ok=True)
    cutoff = time.time() - max(MAX_TIMEOUT, DEFAULT_SESSION_MAX, 21600)
    for p in LOCKS_DIR.glob("*.lock"):
        try:
            if p.stat().st_mtime < cutoff:
                p.unlink(missing_ok=True)
        except OSError:
            pass


def main():
    for d in (STATE, SESSIONS_DIR, OUTPUTS_DIR, SCREENSHOTS_DIR, LOCKS_DIR, CACHE_DIR):
        d.mkdir(parents=True, exist_ok=True)
    load_processed()
    clean_stale_locks()
    log(f"worker starting version={WORKER_VERSION} protocol={PROTOCOL} max_workers={MAX_WORKERS} auth={auth_mode()}")
    audit("worker_start", pid=os.getpid(), version=WORKER_VERSION, protocol=PROTOCOL, auth_mode=auth_mode())
    executor = concurrent.futures.ThreadPoolExecutor(max_workers=MAX_WORKERS, thread_name_prefix="heaven-job")
    try:
        while True:
            try:
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
        audit("worker_stop", pid=os.getpid())
        log("worker exiting")


if __name__ == "__main__":
    main()
