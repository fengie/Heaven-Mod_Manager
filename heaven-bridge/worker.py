import csv
import io
import json
import os
import re
import shutil
import subprocess
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(os.environ.get('HEAVEN_BRIDGE_REPO', str(Path.home() / 'HeavenBridgeRepo'))).resolve()
QUEUE = ROOT / "heaven-bridge" / "queue"
RESULTS = ROOT / "heaven-bridge" / "results"
STATE = Path.home() / "HeavenBridge"
LOG = STATE / "worker.log"
SESSIONS_DIR = STATE / "sessions"
BRANCH = "heaven-bridge"
SOURCE = "chatgpt-heaven-bridge-v2"
LEGACY_SOURCE = "chatgpt-heaven-bridge-v1"
MAX_TIMEOUT = 7200
MAX_OUTPUT = 120000
MAX_READ_BYTES = 2_000_000
MAX_SEARCH_FILE_BYTES = 2_000_000
MAX_RESULTS = 500
SESSIONS = {}

DIRECT_ACTIONS = {
    "health", "system_info",
    "fs_read", "fs_read_many", "fs_write", "fs_edit", "fs_mkdir",
    "fs_list", "fs_move", "fs_info", "fs_search",
    "proc_run", "proc_start", "proc_read", "proc_input", "proc_kill",
    "proc_list_sessions", "proc_list",
    "powershell", "cmd", "python", "codex",
}

def now():
    return datetime.now(timezone.utc).isoformat()

def log(msg):
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open("a", encoding="utf-8") as f:
        f.write(f"[{now()}] {msg}\n")

def git(*args, check=True):
    p = subprocess.run(
        ["git", *args],
        cwd=ROOT,
        capture_output=True,
        text=True,
        timeout=120,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
    if check and p.returncode != 0:
        raise RuntimeError((p.stderr or p.stdout or "git failed")[-4000:])
    return p

def sync():
    git("fetch", "origin", BRANCH)
    current = git("branch", "--show-current").stdout.strip()
    if current != BRANCH:
        git("checkout", "-B", BRANCH, f"origin/{BRANCH}")
    git("pull", "--rebase", "origin", BRANCH)

def safe_id(name):
    return bool(re.fullmatch(r"[A-Za-z0-9._-]{1,120}", name))

def cap_text(value, limit=MAX_OUTPUT):
    text = "" if value is None else str(value)
    return text if len(text) <= limit else text[-limit:]

def expand_path(value):
    if value is None or str(value).strip() == "":
        return Path.home()
    s = os.path.expandvars(os.path.expanduser(str(value)))
    return Path(s).resolve()

def params(job):
    value = job.get("params")
    return value if isinstance(value, dict) else {}

def shell_argv(shell, command):
    shell = (shell or "powershell").lower()
    if shell == "powershell":
        return ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command]
    if shell == "cmd":
        return ["cmd.exe", "/d", "/s", "/c", command]
    if shell == "python":
        return ["python.exe", "-c", command]
    raise ValueError(f"unsupported shell: {shell}")

def run_capture(argv, cwd, timeout, stdin=None):
    started = now()
    try:
        p = subprocess.run(
            argv,
            cwd=str(cwd),
            input=stdin,
            capture_output=True,
            text=True,
            timeout=timeout,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        return {
            "status": "done" if p.returncode == 0 else "failed",
            "exit_code": p.returncode,
            "started_at": started,
            "finished_at": now(),
            "stdout": cap_text(p.stdout),
            "stderr": cap_text(p.stderr),
        }
    except subprocess.TimeoutExpired as e:
        return {
            "status": "timeout",
            "exit_code": 124,
            "started_at": started,
            "finished_at": now(),
            "stdout": cap_text(e.stdout if isinstance(e.stdout, str) else ""),
            "stderr": cap_text(e.stderr if isinstance(e.stderr, str) else ""),
        }

def read_text_lines(path, offset=0, length=1000):
    raw = path.read_bytes()
    if len(raw) > MAX_READ_BYTES:
        raise ValueError(f"file exceeds {MAX_READ_BYTES} byte direct-read limit")
    text = raw.decode("utf-8", errors="replace")
    lines = text.splitlines()
    total = len(lines)
    offset = int(offset or 0)
    length = max(1, min(int(length or 1000), 5000))
    if offset < 0:
        start = max(0, total + offset)
    else:
        start = min(offset, total)
    chunk = lines[start:start + length]
    return {"path": str(path), "offset": start, "length": len(chunk), "total_lines": total, "content": "\n".join(chunk)}

def fs_list(root, depth=2, max_items=2000):
    root = expand_path(root)
    depth = max(1, min(int(depth or 2), 8))
    out = []
    base_parts = len(root.parts)
    for current, dirs, files in os.walk(root):
        cur = Path(current)
        cur_depth = len(cur.parts) - base_parts
        if cur_depth >= depth:
            dirs[:] = []
        for name in sorted(dirs):
            p = cur / name
            out.append({"type": "dir", "path": str(p)})
            if len(out) >= max_items:
                return out
        for name in sorted(files):
            p = cur / name
            try:
                st = p.stat()
                out.append({"type": "file", "path": str(p), "size": st.st_size, "modified": datetime.fromtimestamp(st.st_mtime, timezone.utc).isoformat()})
            except OSError:
                out.append({"type": "file", "path": str(p)})
            if len(out) >= max_items:
                return out
    return out

def fs_search(p):
    root = expand_path(p.get("path"))
    pattern = str(p.get("pattern") or "")
    if not pattern:
        raise ValueError("pattern is required")
    mode = str(p.get("mode") or "both").lower()
    use_regex = bool(p.get("regex", False))
    case_sensitive = bool(p.get("case_sensitive", False))
    max_results = max(1, min(int(p.get("max_results") or 100), MAX_RESULTS))
    glob = str(p.get("glob") or "*")
    flags = 0 if case_sensitive else re.IGNORECASE
    rx = re.compile(pattern, flags) if use_regex else None
    needle = pattern if case_sensitive else pattern.lower()
    results = []
    for file in root.rglob(glob):
        if len(results) >= max_results:
            break
        if file.is_dir():
            continue
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
    return results

def session_snapshot(sid, s):
    proc = s["proc"]
    return {
        "session_id": sid,
        "pid": proc.pid,
        "running": proc.poll() is None,
        "exit_code": proc.poll(),
        "shell": s["shell"],
        "command": s["command"],
        "cwd": s["cwd"],
        "started_at": s["started_at"],
        "stdout_path": str(s["stdout_path"]),
        "stderr_path": str(s["stderr_path"]),
    }

def read_tail(path, max_chars=MAX_OUTPUT):
    try:
        data = path.read_bytes()
        if len(data) > max_chars * 2:
            data = data[-max_chars * 2:]
        return data.decode("utf-8", errors="replace")[-max_chars:]
    except OSError:
        return ""

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
        raise ValueError(f"unsupported shell: {shell}")
    sid = uuid.uuid4().hex[:16]
    SESSIONS_DIR.mkdir(parents=True, exist_ok=True)
    stdout_path = SESSIONS_DIR / f"{sid}.out.log"
    stderr_path = SESSIONS_DIR / f"{sid}.err.log"
    out_f = stdout_path.open("a", encoding="utf-8")
    err_f = stderr_path.open("a", encoding="utf-8")
    proc = subprocess.Popen(
        argv,
        cwd=str(cwd),
        stdin=subprocess.PIPE,
        stdout=out_f,
        stderr=err_f,
        text=True,
        bufsize=1,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
    SESSIONS[sid] = {
        "proc": proc, "out_f": out_f, "err_f": err_f,
        "stdout_path": stdout_path, "stderr_path": stderr_path,
        "shell": shell, "command": command, "cwd": str(cwd), "started_at": now(),
    }
    return session_snapshot(sid, SESSIONS[sid])

def cleanup_finished_sessions():
    for sid, s in list(SESSIONS.items()):
        if s["proc"].poll() is not None:
            try: s["out_f"].flush()
            except Exception: pass
            try: s["err_f"].flush()
            except Exception: pass

def run_job(job):
    action = str(job.get("action") or job.get("kind") or "codex").lower()
    if action not in DIRECT_ACTIONS:
        raise ValueError(f"unsupported action: {action}")
    p = params(job)
    host = os.environ.get("COMPUTERNAME", "heaven")
    started = now()

    if action == "health":
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action,
                "data": {"worker_version": 2, "protocol": SOURCE, "actions": sorted(DIRECT_ACTIONS)}}

    if action == "system_info":
        data = {
            "host": host,
            "user": os.environ.get("USERNAME"),
            "home": str(Path.home()),
            "platform": os.name,
            "python": os.sys.version,
            "cwd": os.getcwd(),
            "drives": [f"{c}:\\" for c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ" if Path(f"{c}:\\").exists()],
        }
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action, "data": data}

    if action == "fs_read":
        data = read_text_lines(expand_path(p.get("path")), p.get("offset", 0), p.get("length", 1000))
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action, "data": data}

    if action == "fs_read_many":
        paths = p.get("paths") or []
        if not isinstance(paths, list) or not paths:
            raise ValueError("params.paths must be a non-empty list")
        data = []
        for value in paths[:50]:
            try:
                data.append(read_text_lines(expand_path(value), 0, p.get("length", 1000)))
            except Exception as e:
                data.append({"path": str(value), "error": str(e)})
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action, "data": data}

    if action == "fs_write":
        path = expand_path(p.get("path"))
        content = str(p.get("content") if p.get("content") is not None else "")
        mode = str(p.get("mode") or "rewrite").lower()
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("a" if mode == "append" else "w", encoding="utf-8") as f:
            f.write(content)
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action,
                "data": {"path": str(path), "bytes": path.stat().st_size, "mode": mode}}

    if action == "fs_edit":
        path = expand_path(p.get("path"))
        old = str(p.get("old_string") if p.get("old_string") is not None else "")
        new = str(p.get("new_string") if p.get("new_string") is not None else "")
        replace_all = bool(p.get("replace_all", False))
        text = path.read_text(encoding="utf-8")
        count = text.count(old)
        if count == 0:
            raise ValueError("old_string not found")
        if not replace_all and count != 1:
            raise ValueError(f"old_string occurs {count} times; set replace_all=true or provide a unique block")
        updated = text.replace(old, new) if replace_all else text.replace(old, new, 1)
        path.write_text(updated, encoding="utf-8")
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action,
                "data": {"path": str(path), "replacements": count if replace_all else 1}}

    if action == "fs_mkdir":
        path = expand_path(p.get("path"))
        path.mkdir(parents=True, exist_ok=True)
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action, "data": {"path": str(path)}}

    if action == "fs_list":
        root = expand_path(p.get("path"))
        data = {"root": str(root), "items": fs_list(root, p.get("depth", 2), min(int(p.get("max_items") or 2000), 5000))}
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action, "data": data}

    if action == "fs_move":
        src = expand_path(p.get("source"))
        dst = expand_path(p.get("destination"))
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(src), str(dst))
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action,
                "data": {"source": str(src), "destination": str(dst)}}

    if action == "fs_info":
        path = expand_path(p.get("path"))
        st = path.stat()
        data = {"path": str(path), "exists": True, "is_file": path.is_file(), "is_dir": path.is_dir(),
                "size": st.st_size, "modified": datetime.fromtimestamp(st.st_mtime, timezone.utc).isoformat()}
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action, "data": data}

    if action == "fs_search":
        data = {"results": fs_search(p)}
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action, "data": data}

    if action == "proc_run":
        command = str(p.get("command") or "")
        if not command.strip():
            raise ValueError("params.command is required")
        timeout = max(1, min(int(p.get("timeout_seconds") or 1800), MAX_TIMEOUT))
        data = run_capture(shell_argv(p.get("shell"), command), expand_path(p.get("cwd")), timeout)
        data.update({"host": host, "action": action})
        return data

    if action == "proc_start":
        data = start_session(p)
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action, "data": data}

    if action == "proc_read":
        sid = str(p.get("session_id") or "")
        if sid not in SESSIONS:
            raise ValueError("unknown session_id")
        s = SESSIONS[sid]
        try: s["out_f"].flush()
        except Exception: pass
        try: s["err_f"].flush()
        except Exception: pass
        data = session_snapshot(sid, s)
        data["stdout"] = read_tail(s["stdout_path"], int(p.get("max_chars") or MAX_OUTPUT))
        data["stderr"] = read_tail(s["stderr_path"], int(p.get("max_chars") or MAX_OUTPUT))
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action, "data": data}

    if action == "proc_input":
        sid = str(p.get("session_id") or "")
        if sid not in SESSIONS:
            raise ValueError("unknown session_id")
        s = SESSIONS[sid]
        if s["proc"].poll() is not None:
            raise ValueError("session process already exited")
        value = str(p.get("input") if p.get("input") is not None else "")
        newline = bool(p.get("newline", True))
        s["proc"].stdin.write(value + ("\n" if newline else ""))
        s["proc"].stdin.flush()
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action,
                "data": session_snapshot(sid, s)}

    if action == "proc_kill":
        sid = str(p.get("session_id") or "")
        if sid not in SESSIONS:
            raise ValueError("unknown session_id")
        s = SESSIONS[sid]
        if s["proc"].poll() is None:
            if bool(p.get("force", False)):
                s["proc"].kill()
            else:
                s["proc"].terminate()
            try: s["proc"].wait(timeout=5)
            except subprocess.TimeoutExpired: s["proc"].kill()
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action,
                "data": session_snapshot(sid, s)}

    if action == "proc_list_sessions":
        cleanup_finished_sessions()
        data = [session_snapshot(sid, s) for sid, s in SESSIONS.items()]
        return {"status": "completed", "exit_code": 0, "started_at": started, "finished_at": now(), "host": host, "action": action, "data": data}

    if action == "proc_list":
        command = "Get-Process | Sort-Object CPU -Descending | Select-Object -First 200 Id,ProcessName,CPU,WorkingSet64,Path | ConvertTo-Json -Depth 3"
        result = run_capture(shell_argv("powershell", command), Path.home(), 20)
        result.update({"host": host, "action": action})
        return result

    # Legacy / escape-hatch execution.
    payload = job.get("payload")
    if not isinstance(payload, str) or not payload.strip():
        raise ValueError("payload must be a non-empty string")
    cwd = expand_path(job.get("cwd"))
    timeout = max(1, min(int(job.get("timeout_seconds") or 1800), MAX_TIMEOUT))
    if action in ("powershell", "cmd", "python"):
        result = run_capture(shell_argv(action, payload), cwd, timeout)
    else:
        result = run_capture(["codex", "exec", "--skip-git-repo-check", "-"], cwd, timeout, stdin=payload)
    result.update({"host": host, "action": action})
    return result

def publish_result(job_id, job, result):
    RESULTS.mkdir(parents=True, exist_ok=True)
    target = RESULTS / f"{job_id}.json"
    body = {
        "id": job_id,
        "source": job.get("source"),
        "created_at": job.get("created_at"),
        **result,
    }
    target.write_text(json.dumps(body, indent=2, ensure_ascii=False), encoding="utf-8")
    for attempt in range(6):
        try:
            git("add", str(target.relative_to(ROOT)))
            git("commit", "-m", f"heaven bridge result {job_id}", check=False)
            git("pull", "--rebase", "origin", BRANCH)
            pushed = git("push", "origin", BRANCH, check=False)
            if pushed.returncode == 0:
                return
            time.sleep(1 + attempt)
        except Exception as e:
            log(f"publish retry {attempt + 1} for {job_id}: {e}")
            time.sleep(1 + attempt)
    raise RuntimeError(f"could not publish result for {job_id}")

def process_once():
    sync()
    QUEUE.mkdir(parents=True, exist_ok=True)
    RESULTS.mkdir(parents=True, exist_ok=True)
    for path in sorted(QUEUE.glob("*.json")):
        job_id = path.stem
        if not safe_id(job_id):
            continue
        if (RESULTS / f"{job_id}.json").exists():
            continue
        try:
            job = json.loads(path.read_text(encoding="utf-8"))
            source = job.get("source")
            if source not in (SOURCE, LEGACY_SOURCE):
                continue
            log(f"starting {job_id} ({job.get('action') or job.get('kind') or 'codex'})")
            result = run_job(job)
            publish_result(job_id, job, result)
            log(f"finished {job_id}: {result['status']}")
            return True
        except Exception as e:
            log(f"job {job_id} error: {e}")
            failure = {
                "status": "error",
                "exit_code": 1,
                "started_at": now(),
                "finished_at": now(),
                "host": os.environ.get("COMPUTERNAME", "heaven"),
                "action": str(job.get("action") or job.get("kind") or "unknown") if "job" in locals() else "unknown",
                "stdout": "",
                "stderr": repr(e),
            }
            try:
                publish_result(job_id, job if "job" in locals() else {}, failure)
                return True
            except Exception as publish_error:
                log(f"failed to publish error for {job_id}: {publish_error}")
    return False

def main():
    STATE.mkdir(parents=True, exist_ok=True)
    SESSIONS_DIR.mkdir(parents=True, exist_ok=True)
    log(f"worker starting protocol={SOURCE}")
    while True:
        try:
            cleanup_finished_sessions()
            did_work = process_once()
            time.sleep(0.75 if did_work else 2)
        except KeyboardInterrupt:
            return
        except Exception as e:
            log(f"loop error: {e}")
            time.sleep(5)

if __name__ == "__main__":
    main()