# Heaven Local Bridge

Private, local-first desktop-control bridge for the `heaven` worker PC. Routine filesystem, terminal, process, build/test, and local-agent work runs on `heaven` without consuming Remote Desktop Commander Remote MCP quota.

## Architecture

```text
ChatGPT
  -> private GitHub relay (fengie/mhw-mods, branch heaven-bridge)
  -> heaven local worker
  -> Windows/files/processes/local Codex
  -> result/status back through GitHub
```

Machine roles:

- `heaven`: worker/execution machine.
- `heaven2`: main/control machine and credential authority.
- Secrets remain on `heaven2` unless a runtime task explicitly requires them.
- Heavy builds, tests, scans, indexing, agents, and automation run on `heaven`.

Relay paths:

- Queue: `heaven-bridge/queue/<job-id>.json`
- Results: `heaven-bridge/results/<job-id>.json`
- Status: `heaven-bridge/status/<job-id>.json`
- Heartbeat: `heaven-bridge/status/heartbeat.json`
- Protocol: `chatgpt-heaven-bridge-v2`

Do not use Remote Desktop Commander for `heaven` work unless the user explicitly authorizes it in the current request. If the Heaven Local Bridge is unhealthy, repair or queue recovery through the bridge/GitHub relay; do not silently switch remote-control providers.

## Worker v3

`heaven-bridge/worker.py` is the repository source of truth. `bootstrap.ps1` installs it to:

`%USERPROFILE%\.mhw-local-tools\heaven-desktop-worker.py`

The worker preserves v2 protocol compatibility and reports `worker_version: 3`.

Core capabilities:

- health and system/capability negotiation
- text reads/writes/edits/list/search/info/move
- safe copy/delete
- chunked base64 binary read/write
- synchronous terminal execution with persisted/paginated output
- multiple persistent process sessions with input/read/kill
- process-tree termination
- job cancellation and status
- bounded concurrent job execution
- TTL/replay/idempotency checks
- optional HMAC-SHA256 authentication
- structured error codes
- atomic result writes
- Git retry/backoff and serialized Git mutations
- heartbeat and sanitized local audit logging
- session idle/max-runtime cleanup
- configurable filesystem allowlists
- screenshot capture to a local PNG for later binary retrieval
- local Codex dispatch using an absolute user npm path fallback

Clipboard reading is intentionally not enabled by default because clipboards commonly contain credentials or other sensitive data.

## Job schema

```json
{
  "id": "chatgpt-YYYYMMDD-HHMMSS-suffix",
  "source": "chatgpt-heaven-bridge-v2",
  "action": "health",
  "params": {},
  "created_at": "2026-09-28T19:30:00Z",
  "ttl_seconds": 21600,
  "priority": "highest"
}
```

`job.id` must match the queue filename. Jobs outside their TTL, too far in the future, or replayed under the same ID with different content are rejected with structured errors.

## Authentication and integrity

The private GitHub repository and branch ACL are the baseline trust boundary.

For stronger per-job authentication, set `HEAVEN_BRIDGE_HMAC_KEY` in the worker environment. When configured, every new job must contain an HMAC-SHA256 signature in:

```json
{
  "auth": {
    "signature": "<64 lowercase hex characters>"
  }
}
```

The signature is computed over canonical JSON for the job with `auth.signature` omitted. `health` reports the active auth mode.

Never put passwords, access tokens, API keys, cookies, private keys, or other credentials into queue files or result files.

## Filesystem safety

Structured `fs_*` actions are limited to configured roots. Defaults include:

- the current user's home directory
- the bridge repository checkout
- `C:\HeavenServices` when present

Additional roots can be supplied through `HEAVEN_BRIDGE_ALLOWED_ROOTS`.

Delete operations refuse configured roots, the user home, bridge state directory, and drive roots. Directory copy/delete requires `recursive: true`.

### Common filesystem actions

`fs_read`, `fs_read_many`, `fs_write`, `fs_edit`, `fs_mkdir`, `fs_list`, `fs_move`, `fs_info`, `fs_search`, `fs_copy`, `fs_delete`, `fs_read_binary`, `fs_write_binary`.

Text reads are paginated by line offset. Binary reads use byte offsets and bounded base64 chunks.

## Terminal and process control

Use `proc_run` for synchronous commands:

```json
{
  "action": "proc_run",
  "params": {
    "shell": "powershell",
    "command": "git status",
    "cwd": "C:\\Users\\Xxkan",
    "timeout_seconds": 300
  }
}
```

Full stdout/stderr are persisted locally. Use `job_output_read` to page through large output after the command finishes.

Persistent sessions:

- `proc_start`
- `proc_input`
- `proc_read`
- `proc_kill`
- `proc_list_sessions`

Sessions have configurable idle and maximum-runtime cleanup. Windows process termination uses tree termination so child processes do not remain orphaned.

Environment handling supports:

- non-sensitive inline `env`
- `env_from_host: ["NAME"]` to inherit named variables that already exist locally without returning their values

Secret-like inline environment keys are rejected.

## Durable controller checkpoints

Permanent autonomous development cycles must not depend on a separate direct ChatGPT-to-GitHub write after the work is finished. Use the structured `controller_checkpoint` action so the `heaven` worker owns the final authoritative refresh, stale-cycle check, atomic state write, commit, rebase, and push.

```json
{
  "action": "controller_checkpoint",
  "params": {
    "expected_previous_cycle_id": "2026-09-28T17:26-04:00-cycle-4",
    "state": {
      "schema": "permanent-dev-controller-v1",
      "cycle_id": "2026-09-28T20:30-04:00-cycle-5"
    }
  }
}
```

The action fails closed when the existing cycle no longer matches `expected_previous_cycle_id`, the new cycle ID does not advance, the controller file is missing/invalid, or the state contains secret-like field names. A cycle is not durably complete until the checkpoint result reports `persisted: true` and the state is read back from GitHub.

## Job control and progress

- `job_status`: returns running/completed/unknown state.
- `cancel`: requests cancellation of a running job.
- per-job status files are published under `heaven-bridge/status/`.
- `heartbeat.json` periodically publishes worker version, protocol, capabilities, and running job IDs.

Heartbeat commits are deliberately infrequent to avoid relay commit spam.

## Screenshots and GUI

`screenshot` captures the primary interactive display to a local PNG under `%USERPROFILE%\HeavenBridge\screenshots` and returns its path. Retrieve it with `fs_read_binary`.

App launching is already covered by `proc_start`/`proc_run`. Window-focus and clipboard-reading APIs are not enabled in v3 because they are more fragile/sensitive and should be added only with explicit reliability and privacy constraints.

## Raw shell fallback

Legacy `powershell`, `cmd`, `python`, and `codex` actions remain for compatibility and recovery. They are not a public network API and must never be exposed through an unauthenticated internet listener.

Prefer structured actions whenever one exists. Raw shell is an escape hatch, not the default interface.

## Startup and recovery

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\heaven-bridge\bootstrap.ps1
```

Bootstrap:

1. clones/updates the isolated `heaven-bridge` relay checkout;
2. copies repository `worker.py` to the local runtime path;
3. runs `python -m py_compile` before replacing the active file;
4. keeps a backup of the prior runtime worker;
5. configures a user-level Scheduled Task with restart-on-failure when available;
6. also writes a Startup-folder VBS fallback with an absolute `pythonw.exe` path;
7. starts the worker and restores the backup if the new worker immediately fails to remain running.

The relay branch is not a canonical development branch for the wider project.

## Testing

From the repository root:

```powershell
python -m py_compile heaven-bridge\worker.py
python -m unittest -v heaven-bridge\test_worker.py
```

The test suite covers TTL/future-skew validation, canonical duplicate hashing, allowlist/delete protections, binary pagination/roundtrip helpers, copy/delete behavior, structured errors, search-mode compatibility, secret-like inline env blocking, and health capabilities.

## Security boundary

This design intentionally does **not** expose an unauthenticated raw shell to the public internet.

An optional LAN-direct transport may be added later only if it is mutually authenticated, encrypted, replay-protected, bound to a trusted interface, and retains the GitHub relay as a safe fallback. ChatGPT cloud connectivity to a LAN endpoint should not be assumed.
