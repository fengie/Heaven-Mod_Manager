import json
import os
import re
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
QUEUE = ROOT / 'heaven-bridge' / 'queue'
RESULTS = ROOT / 'heaven-bridge' / 'results'
LOG = Path.home() / 'HeavenBridge' / 'worker.log'
BRANCH = 'heaven-bridge'
ALLOWED_ACTIONS = {'powershell', 'cmd', 'python', 'codex'}
MAX_TIMEOUT = 7200
MAX_OUTPUT = 120000

def now():
    return datetime.now(timezone.utc).isoformat()

def log(msg):
    LOG.parent.mkdir(parents=True, exist_ok=True)
    with LOG.open('a', encoding='utf-8') as f:
        f.write(f'[{now()}] {msg}\n')

def git(*args, check=True):
    p = subprocess.run(
        ['git', *args],
        cwd=ROOT,
        capture_output=True,
        text=True,
        timeout=120,
    )
    if check and p.returncode != 0:
        raise RuntimeError((p.stderr or p.stdout or 'git failed')[-4000:])
    return p

def sync():
    git('fetch', 'origin', BRANCH)
    current = git('branch', '--show-current').stdout.strip()
    if current != BRANCH:
        git('checkout', '-B', BRANCH, f'origin/{BRANCH}')
    git('pull', '--rebase', 'origin', BRANCH)

def safe_id(name):
    return bool(re.fullmatch(r'[A-Za-z0-9._-]{1,120}', name))

def run_job(job):
    action = str(job.get('action', 'codex')).lower()
    if action not in ALLOWED_ACTIONS:
        raise ValueError(f'unsupported action: {action}')

    payload = job.get('payload')
    if not isinstance(payload, str) or not payload.strip():
        raise ValueError('payload must be a non-empty string')

    cwd = job.get('cwd') or str(Path.home())
    timeout = int(job.get('timeout_seconds') or 1800)
    timeout = max(1, min(timeout, MAX_TIMEOUT))

    if action == 'powershell':
        argv = ['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', payload]
        stdin = None
    elif action == 'cmd':
        argv = ['cmd.exe', '/d', '/s', '/c', payload]
        stdin = None
    elif action == 'python':
        argv = ['python.exe', '-c', payload]
        stdin = None
    else:
        argv = ['codex', 'exec', '--skip-git-repo-check', '-']
        stdin = payload

    started = now()
    try:
        p = subprocess.run(
            argv,
            cwd=cwd,
            input=stdin,
            capture_output=True,
            text=True,
            timeout=timeout,
        )
        status = 'done' if p.returncode == 0 else 'failed'
        return {
            'status': status,
            'exit_code': p.returncode,
            'started_at': started,
            'finished_at': now(),
            'host': os.environ.get('COMPUTERNAME', 'heaven'),
            'action': action,
            'stdout': (p.stdout or '')[-MAX_OUTPUT:],
            'stderr': (p.stderr or '')[-MAX_OUTPUT:],
        }
    except subprocess.TimeoutExpired as e:
        return {
            'status': 'timeout',
            'exit_code': 124,
            'started_at': started,
            'finished_at': now(),
            'host': os.environ.get('COMPUTERNAME', 'heaven'),
            'action': action,
            'stdout': (e.stdout or '')[-MAX_OUTPUT:] if isinstance(e.stdout, str) else '',
            'stderr': (e.stderr or '')[-MAX_OUTPUT:] if isinstance(e.stderr, str) else '',
        }
    except Exception as e:
        return {
            'status': 'error',
            'exit_code': 1,
            'started_at': started,
            'finished_at': now(),
            'host': os.environ.get('COMPUTERNAME', 'heaven'),
            'action': action,
            'stdout': '',
            'stderr': repr(e),
        }

def publish_result(job_id, job, result):
    RESULTS.mkdir(parents=True, exist_ok=True)
    target = RESULTS / f'{job_id}.json'
    body = {
        'id': job_id,
        'source': job.get('source'),
        'created_at': job.get('created_at'),
        **result,
    }
    target.write_text(json.dumps(body, indent=2, ensure_ascii=False), encoding='utf-8')

    for attempt in range(5):
        try:
            git('add', str(target.relative_to(ROOT)))
            git('commit', '-m', f'heaven bridge result {job_id}', check=False)
            git('pull', '--rebase', 'origin', BRANCH)
            pushed = git('push', 'origin', BRANCH, check=False)
            if pushed.returncode == 0:
                return
            time.sleep(2 + attempt)
        except Exception as e:
            log(f'publish retry {attempt + 1} for {job_id}: {e}')
            time.sleep(2 + attempt)
    raise RuntimeError(f'could not publish result for {job_id}')

def process_once():
    sync()
    QUEUE.mkdir(parents=True, exist_ok=True)
    RESULTS.mkdir(parents=True, exist_ok=True)

    for path in sorted(QUEUE.glob('*.json')):
        job_id = path.stem
        if not safe_id(job_id):
            continue
        if (RESULTS / f'{job_id}.json').exists():
            continue
        try:
            job = json.loads(path.read_text(encoding='utf-8'))
            if job.get('source') != 'chatgpt-heaven-bridge-v1':
                continue
            log(f'starting {job_id} ({job.get("action", "codex")})')
            result = run_job(job)
            publish_result(job_id, job, result)
            log(f'finished {job_id}: {result["status"]}')
            return True
        except Exception as e:
            log(f'job {job_id} error: {e}')
    return False

def main():
    LOG.parent.mkdir(parents=True, exist_ok=True)
    log('worker starting')
    while True:
        try:
            did_work = process_once()
            time.sleep(1 if did_work else 5)
        except KeyboardInterrupt:
            return
        except Exception as e:
            log(f'loop error: {e}')
            time.sleep(10)

if __name__ == '__main__':
    main()
