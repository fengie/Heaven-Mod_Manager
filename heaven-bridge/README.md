# Heaven Local Bridge

Private local-execution bridge for the `heaven` worker PC. It replaces routine Remote Desktop Commander usage with a private GitHub relay while keeping execution local.

## Architecture

```
ChatGPT
  -> private GitHub repo fengie/mhw-mods
     branch: heaven-bridge
     queue:   heaven-bridge/queue/<job-id>.json
  -> worker on heaven
  -> Windows/files/processes/local Codex
  -> heaven-bridge/results/<job-id>.json
  -> ChatGPT
```

Machine policy:
- `heaven` is the worker/execution machine.
- `heaven2` is the main/control machine and credential authority.
- Secrets stay on `heaven2` unless a runtime task explicitly needs them.
- GitHub is repository truth. Development stays on `heaven-bridge` unless explicitly authorized otherwise.
- No unauthenticated public raw shell is exposed.

## Protocol

Protocol remains `chatgpt-heaven-bridge-v2` for compatibility. Worker implementation version is reported separately by `health`.

Base job:

```json
{
  "id": "unique-job-id",
  "source": "chatgpt-heaven-bridge-v2",
  "created_at": "2026-09-28T19:00:00Z",
  "ttl_seconds": 86400,
  "action": "health",
  "params": {}
}
```

The worker rejects expired/future-skewed jobs, validates filename/job-id consistency, hashes jobs canonically for replay protection, applies a per-minute rate limit, and publishes structured errors.

If `HEAVEN_BRIDGE_HMAC_KEY` is configured on `heaven`, jobs require `auth.hmac_sha256`. The signature is HMAC-SHA256 over the ASCII canonical-job SHA256 digest. When the key is absent, unsigned jobs are accepted only through the private relay for backward compatibility.

Never place passwords, tokens, API keys, cookies, private keys, or other secrets in queue jobs, result files, logs, or commits.

## Structured actions

Preferred actions:
- Health/system: `health`, `system_info`
- Files: `fs_read`, `fs_read_many`, `fs_write`, `fs_edit`, `fs_mkdir`, `fs_list`, `fs_move`, `fs_copy`, `fs_delete`, `fs_info`, `fs_search`
- Binary: `fs_read_binary`, `fs_write_binary` using paged/chunked base64
- Processes: `proc_run`, `proc_start`, `proc_read`, `proc_input`, `proc_kill`, `proc_list_sessions`, `proc_list`
- Large command output: `job_output_read`
- Recovery/escape hatch: `powershell`, `cmd`, `python`, `codex`

Structured actions should be used before raw shell. Raw actions are compatibility/recovery surfaces, not the default API.

Filesystem actions are restricted to configured roots. Safe defaults include the current user's home, the relay checkout, and `C:\HeavenServices` when present. Additional roots may be supplied with `HEAVEN_BRIDGE_ALLOWED_ROOTS`. Destructive operations reject allowed-root and drive-root deletion.

`fs_delete` requires `recursive:true` for recursive directory deletion. `fs_copy` requires `overwrite:true` to replace an existing destination.

## Processes and sessions

`proc_run` is synchronous. Output is returned inline up to the normal cap and is also persisted locally for paging with `job_output_read`.

`proc_start` creates a persistent PowerShell/CMD/Python session. Sessions have idle and maximum-lifetime limits and are cleaned automatically. `proc_kill` terminates the process tree on Windows.

Environment handling supports explicit non-secret inline variables and named inheritance from the worker's local environment. Secret-like inline variable names are rejected. Environment values are never echoed merely because they were inherited.

## Startup and recovery

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\heaven-bridge\bootstrap.ps1
```

The bootstrap:
1. clones or updates only the `heaven-bridge` branch;
2. syntax-checks the repository worker;
3. atomically installs it to `%USERPROFILE%\.mhw-local-tools\heaven-desktop-worker.py`;
4. installs a Startup fallback;
5. best-effort installs a user Scheduled Task with restart-on-failure;
6. replaces only existing bridge worker processes;
7. verifies that the new worker remains running.

## Security model

The GitHub repository/branch is the control plane; Windows execution stays local. The worker adds TTL validation, replay hashes, optional HMAC authentication, path allowlists, dangerous-root protections, structured error codes, local JSONL audit logging, output limits/paging, process-session expiry, and rate limiting.

Optional future LAN-direct transport must use authenticated encryption and preserve the GitHub relay as a safe fallback. Do not bind an unauthenticated raw shell or command endpoint to the public internet.

## Verification

Run locally on `heaven`:

```powershell
python -m py_compile .\heaven-bridge\worker.py
python -m unittest discover -s .\heaven-bridge\tests -p "test_*.py" -v
```

A healthy worker returns `status: completed`, `host: heaven`, `data.worker_version: 3`, and `data.protocol: chatgpt-heaven-bridge-v2`.
