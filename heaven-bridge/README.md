# Heaven Local Bridge

Private, local-first desktop-control bridge for the `heaven` worker PC. Routine filesystem, terminal, process, build/test, and local-agent work runs on `heaven` without consuming Remote Desktop Commander Remote MCP quota.

## Architecture

```text
ChatGPT
  -> private GitHub relay (fengie/mhw-mods, transport branch heaven-bridge)
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

Canonical bridge source is versioned on `main`. The `heaven-bridge` branch is the private queue/status/results transport and runtime mirror; do not merge its operational job history wholesale into `main`.

`heaven-bridge/worker.py` is the canonical worker source path. `bootstrap.ps1` installs the relay-mirrored copy to:

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
- screenshot capture for the primary display, one monitor, or the full virtual desktop
- structured display/window enumeration and window focus/move/state/close
- structured cursor, mouse button/click/scroll, keyboard shortcut, and Unicode text input
- structured app launch without shell interpolation
- clipboard text read/write with relay-aware privacy guards
- local Codex dispatch using an absolute user npm path fallback

Clipboard reads require an explicit per-job `allow_relay: true` opt-in because clipboard contents are returned through the private GitHub relay. Never use clipboard or GUI text actions to transmit secrets through this relay.

## Operator quickstart

Run these from the repository root on `heaven`:

```powershell
.\heaven-bridge\manage.ps1 START
.\heaven-bridge\manage.ps1 STATUS
.\heaven-bridge\manage.ps1 TEST
.\heaven-bridge\manage.ps1 STOP
.\heaven-bridge\manage.ps1 RECOVER
```

- `START` uses the hardened bootstrap when the canonical v3 worker is not already running, then performs the same health checks as `STATUS`.
- `STATUS` verifies there is exactly one canonical v3 worker, no legacy `agent-bridge` worker, the checkout is on `heaven-bridge`, the installed runtime matches repository `worker.py`, and the heartbeat is current. It exits nonzero when any of those invariants are false.
- `TEST` compiles the worker and both committed test suites, runs both suites, and parses the PowerShell bootstrap/operator scripts.
- `STOP` stops only the canonical v3 worker (and its canonical scheduled task if present); it does not kill unrelated Python or PowerShell processes.
- `RECOVER` runs the hardened bootstrap and then requires `STATUS` to become healthy. Bootstrap preserves a dirty/diverged relay HEAD and tracked diff under `%USERPROFILE%\HeavenBridge\bootstrap-recovery` before realigning the disposable relay checkout.

If `STATUS` reports legacy workers or a legacy scheduled task, treat that as a split-brain startup problem to retire explicitly; do not ignore it merely because the v3 heartbeat is healthy.

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

### Session recovery across worker restarts

`proc_start` persists non-secret ownership metadata under `%USERPROFILE%\HeavenBridge\sessions`. The persisted record intentionally omits the command text and environment. A replacement worker reconciles those records on startup and proves ownership using the PID plus the process creation identity; it never treats a stale PID alone as permission to control or terminate a process.

For an identity-proven recovered session:

- `proc_list_sessions` and `proc_read` remain available, including the existing stdout/stderr logs.
- `proc_kill` re-verifies process identity immediately before termination and verifies that the original process is gone afterward.
- `proc_input` fails closed with `SESSION_INPUT_UNAVAILABLE` because stdin cannot be safely reattached after the worker process has restarted.
- snapshots report `recovered: true`, `stdin_available: false`, and do not reconstruct/persist the original command.

If process identity is missing, cannot be queried, or no longer matches, the worker reports the session as non-running/unproven and refuses to kill by PID alone. Dead/recovered session evidence is retained for a bounded period (24 hours by default, configurable with `HEAVEN_BRIDGE_SESSION_RETENTION`) before its session metadata/log artifacts are pruned.

This behavior is exercised by the bridge regression suite and should be included in restart-style release validation via the normal `RECOVER` path.

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
- Control-plane jobs such as `health`, `job_status`, `cancel`, and checkpoints are serviced ahead of ordinary execution jobs so a saturated worker can still be observed or stopped.
- Queue scheduling honors named `priority` values (`highest`/`critical`/`urgent`, `high`, `normal`/`default`, `low`, `lowest`) and numeric priorities from 0-100. Ordinary jobs age upward over time so older lower-priority work cannot starve behind a continuous high-priority stream; control-plane actions remain ahead of ordinary work and bypass its start-rate/capacity gating.
- per-job status files are published under `heaven-bridge/status/`.
- `heartbeat.json` periodically publishes worker version, protocol, capabilities, and running job IDs.

Heartbeat commits are deliberately infrequent to avoid relay commit spam.

## Local desktop control

The bridge now exposes structured Win32 desktop primitives so routine interactive control no longer depends on a Remote Desktop MCP.

Observation actions:

- `display_list`: enumerate active displays and their working areas.
- `window_list`: enumerate titled top-level windows with HWND, PID, visibility, minimized state, and bounds.
- `gui_cursor_get`: read the current cursor position.
- `screenshot`: capture `scope: "primary"`, `"all"`, or `"monitor"` (with `monitor_index`) to a local PNG, then retrieve it through `fs_read_binary` when image bytes are needed.

Interaction actions:

- `gui_mouse_move`: move the pointer, optionally over a bounded duration.
- `gui_mouse_button`: press or release left/right/middle independently; use this for drag sequences.
- `gui_mouse_click`: single/double/triple click at the current or supplied coordinates.
- `gui_mouse_scroll`: vertical or horizontal wheel input.
- `gui_key`: named key/shortcut input such as Ctrl+L, Alt+F4, arrows, or function keys.
- `gui_type`: inject Unicode text through Win32 `SendInput`.
- `window_focus`, `window_move`, `window_state`, `window_close`: manage a window selected by HWND, PID, or title.
- `app_launch`: launch an executable with an argv list and no shell interpolation.
- `clipboard_write`: place Unicode text on the interactive clipboard.
- `clipboard_read`: read Unicode text only when `params.allow_relay=true` is supplied for an explicit clipboard-read request.

Desktop selectors fail closed on ambiguous window matches unless `first_match:true` is explicitly supplied. Clipboard reads are additionally capped and report truncation.

### Sensitive input boundary

The GitHub queue/result relay is private but it is still persisted transport. Do **not** put passwords, access tokens, API keys, cookies, private keys, recovery codes, or other secrets into `clipboard_write`, `gui_type`, command payloads, queue params, results, or controller state.

For full credential-safe desktop parity, secrets should be referenced by a local named-secret handle owned by the credential authority and resolved only on the destination machine; the secret value itself must never enter GitHub. Until that channel is implemented, credential entry remains outside the bridge's safe structured surface.

## Agent code, build, and test workflow

Use the bridge as the normal execution surface for repository work on `heaven`, not as a last-resort remote shell.

- Use `proc_run` with an explicit `cwd` and timeout for bounded builds, linters, test suites, Git commands, and one-shot scripts. Treat exit code `0` plus the command's own assertions as success.
- Full stdout/stderr are persisted locally. Keep relay results compact and use `job_output_read` only when additional failing output is needed.
- Use `proc_start` / `proc_read` / `proc_input` / `proc_kill` for dev servers, watchers, REPL-like processes, and other long-lived commands.
- Run independent test shards or build jobs concurrently when useful; use `job_status` and `cancel` instead of launching duplicate work.
- Use `fs_search`, `fs_read`, `fs_edit`, `fs_write`, copy/delete, and binary actions for source inspection and patching before falling back to raw PowerShell/CMD/Python.
- Before integration, capture branch/HEAD, `git status --short`, changed files, and `git diff --check`; after integration, verify the pushed remote `main` SHA and rerun the relevant exact-main tests on `heaven`.
- Local Codex is optional acceleration. If its quota/runtime is unavailable, structured bridge actions and `proc_run` remain a complete code/build/test path.
- Do not use Remote Desktop Commander for these workflows unless the user explicitly authorizes it in the current request.

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

Use the operator gate from the repository root:

```powershell
.\heaven-bridge\manage.ps1 TEST
```

That command compiles `worker.py`, runs both `heaven-bridge/test_worker.py` and `heaven-bridge/tests/test_worker.py`, and parses `bootstrap.ps1` plus `manage.ps1`. The suites cover TTL/future-skew validation, canonical duplicate hashing, allowlist/delete protections, binary pagination/roundtrip helpers, copy/delete behavior, structured errors, search-mode compatibility, secret-like inline env blocking, singleton/stale-lock behavior, bootstrap handoff safety, Codex batch invocation, and health capabilities.

## Security boundary

This design intentionally does **not** expose an unauthenticated raw shell to the public internet.

An optional LAN-direct transport may be added later only if it is mutually authenticated, encrypted, replay-protected, bound to a trusted interface, and retains the GitHub relay as a safe fallback. ChatGPT cloud connectivity to a LAN endpoint should not be assumed.
