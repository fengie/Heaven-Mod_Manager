# Heaven Local Bridge

Private, local-first multi-host computer-control bridge for `heaven2` and `heaven`. `heaven2` is the operator/control-plane desktop; `heaven` is delegated compute. Routine filesystem, terminal, process, build/test, local-agent, and structured desktop work can be targeted explicitly without consuming Remote Desktop Commander Remote MCP quota.

## Architecture

```text
ChatGPT
  -> private GitHub relay (fengie/mhw-mods, transport branch heaven-bridge)
  -> host-targeted worker on heaven2 or heaven
  -> Windows/files/processes/local Codex/UI
  -> result/status back through GitHub
```

Machine roles:

- `heaven2`: operator/control-plane machine, dashboard/control-panel host, browser/UI automation target, routine desktop-interaction target, and credential authority.
- `heaven`: worker/resource machine for builds, tests, scans, indexing, agents, worker-fabric tasks, and other delegated execution.
- The user-facing surface stays on `heaven2`; do not move dashboards/control panels or routine interaction to `heaven`.
- Secrets remain on `heaven2` unless a runtime task explicitly requires them.
- New jobs set top-level `target_host` explicitly. Interactive/control work uses `heaven2`; heavy delegated work uses `heaven`. Omission is legacy compatibility and defaults to `heaven`.

Relay paths:

- Queue: `heaven-bridge/queue/<job-id>.json`
- Results: `heaven-bridge/results/<job-id>.json`
- Status: `heaven-bridge/status/<job-id>.json`
- Per-host heartbeat: `heaven-bridge/status/hosts/<host>/heartbeat.json`
- Legacy heaven-only heartbeat mirror: `heaven-bridge/status/heartbeat.json`
- Protocol: `chatgpt-heaven-bridge-v2`

Do not use Remote Desktop Commander for `heaven` work unless the user explicitly authorizes it in the current request. If the Heaven Local Bridge is unhealthy, repair or queue recovery through the bridge/GitHub relay; do not silently switch remote-control providers.

## Worker v6

Canonical bridge source is versioned on `main`. The `heaven-bridge` branch is the private queue/status/results transport and compatibility mirror; do not merge its operational job history wholesale into `main`.

Bootstrap keeps two separate local checkouts: `%USERPROFILE%\HeavenBridgeRepo` tracks the operational `heaven-bridge` relay, while the disposable `%USERPROFILE%\HeavenBridgeSource` tracks canonical `main`. Worker/watchdog runtime files and their regression suite are installed only from the canonical-main source mirror. A bootstrap launched from the relay/runtime copy is self-contained for its initial Git recovery logic, refreshes that mirror, and then hands execution to `main`'s bootstrap, so it does not depend on neighboring relay helper files being present and later recovery fixes do not depend on manually mirroring the bootstrap file into transport. If GitHub is temporarily unavailable, bootstrap may use that mirror only when it is already a clean `main` checkout; otherwise recovery fails closed rather than trusting relay drift or local edits.

`heaven-bridge/worker.py` is the canonical worker source path. `bootstrap.ps1` installs the canonical-main copy to:

`%USERPROFILE%\.mhw-local-tools\heaven-desktop-worker.py`

The worker preserves v2 protocol compatibility and reports `worker_version: 6`.

Core capabilities:

- health and system/capability negotiation
- text reads/writes/edits/list/search/info/move
- safe copy/delete
- chunked base64 binary read/write
- synchronous terminal execution with persisted/paginated output
- multiple persistent process sessions with input/read/kill
- bounded structured `wait_for` polling for files, processes, sessions, and windows with cancellation/timeout evidence
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
- structured Windows desktop shortcut creation (`desktop_shortcut_create`) with target, arguments, working directory, description, icon, and overwrite controls
- clipboard text read/write with relay-aware privacy guards
- local Codex dispatch using an absolute user npm path fallback

Clipboard reads require an explicit per-job `allow_relay: true` opt-in because clipboard contents are returned through the private GitHub relay. Never use clipboard or GUI text actions to transmit secrets through this relay.

## Self-healing persistence

The bridge must not depend on the bridge itself for recovery.

Bootstrap installs three recovery layers on every bridge host:

- `Heaven Local Bridge` — the canonical interactive worker.
- `Heaven Local Bridge Watchdog` — a Git/network-independent interactive watchdog.
- `Heaven Local Bridge Sentinel` — a separate SYSTEM-owned machine-start supervisor that repairs the two interactive task definitions and the Startup fallback if they disappear or are disabled.

All scheduled definitions use `MultipleInstances IgnoreNew`, restart once per minute with the maximum Task Scheduler restart count, and explicitly set an infinite execution time limit. The worker/watchdog run in the logged-in user's interactive session; the sentinel deliberately uses a different principal and machine-start trigger so one user-session persistence failure cannot remove every recovery owner.

The worker writes `%USERPROFILE%\HeavenBridge\worker-local-heartbeat.json` every 15 seconds from a dedicated local thread and `worker-loop-progress.json` from the queue-processing loop. The watchdog checks the exact canonical worker process plus these local signals every 30 seconds. A missing worker is restarted immediately; a dead/frozen process heartbeat is repaired after 120 seconds, and a queue loop that makes no progress for 15 minutes is recycled. The longer loop window avoids treating ordinary transient Git/network stalls as immediate process failure. The watchdog never needs GitHub, relay state, or a healthy worker to make the repair decision.

Task Scheduler is not the sole persistence path. Bootstrap also installs a Startup-folder watchdog recovery shim. That shim first hands ownership to the elevated scheduled watchdog; only when Task Scheduler cannot provide it does the shim remain as the direct recovery owner. The SYSTEM sentinel continuously recreates that shim and the interactive task definitions if they are removed or disabled. On `heaven2`, it also restores a missing recovery `Heaven Agent Control.lnk`; the normal Agent Control launcher upgrades that recovery link to the branded shortcut on launch. The old direct-worker Startup fallback remains retired so it cannot race the elevated task and capture the worker singleton with a non-elevated process.

`manage.ps1 STATUS` is healthy only when the worker, watchdog, SYSTEM sentinel, current runtime copies, scheduled-task run levels, local heartbeat, and remote relay heartbeat all agree. `STOP` deliberately stops the watchdog before the worker so an intentional shutdown is not auto-repaired.

Install/repair this persistence on **both `heaven2` and `heaven`**. A host is not considered bridge-ready merely because a worker process happens to exist.

## Operator quickstart

Run these from the repository root on each machine where bridge control is required. For the intended topology, install/run the worker on both `heaven2` and `heaven`; the worker derives its host identity from `HEAVEN_BRIDGE_HOST` or `COMPUTERNAME`:

```powershell
.\heaven-bridge\manage.ps1 START
.\heaven-bridge\manage.ps1 STATUS
.\heaven-bridge\manage.ps1 TEST
.\heaven-bridge\manage.ps1 STOP
.\heaven-bridge\manage.ps1 RECOVER
```

- `START` uses the hardened bootstrap when the canonical v6 worker is not already running, then performs the same health checks as `STATUS`.
- `STATUS` verifies there is exactly one canonical v6 worker, no legacy `agent-bridge` worker, the checkout is on `heaven-bridge`, the installed runtime matches repository `worker.py`, and the heartbeat is current, the canonical task is configured at `Highest`, and the live heartbeat reports `elevated: true`. It exits nonzero when any of those invariants are false.
- `TEST` compiles the worker and both committed test suites, runs both suites, and parses the PowerShell bootstrap/operator scripts.
- `STOP` stops only the canonical v6 worker (and its canonical scheduled task if present); it does not kill unrelated Python or PowerShell processes.
- `RECOVER` runs the hardened bootstrap and then requires `STATUS` to become healthy. Bootstrap preserves a dirty/diverged relay HEAD and tracked diff under `%USERPROFILE%\HeavenBridge\bootstrap-recovery` before realigning the disposable relay checkout.

If `STATUS` reports legacy workers or a legacy scheduled task, treat that as a split-brain startup problem to retire explicitly; do not ignore it merely because the v6 heartbeat is healthy.

## Job schema

```json
{
  "id": "chatgpt-YYYYMMDD-HHMMSS-suffix",
  "source": "chatgpt-heaven-bridge-v2",
  "target_host": "heaven2",
  "action": "health",
  "params": {},
  "created_at": "2026-09-28T19:30:00Z",
  "ttl_seconds": 21600,
  "priority": "highest"
}
```

`job.id` must match the queue filename. `target_host` must name the intended worker. New callers must provide it; missing values retain the legacy `heaven` default only so old jobs continue working. Each worker ignores jobs addressed to the other host. Jobs outside their TTL, too far in the future, or replayed under the same ID with different content are rejected with structured errors.

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
- `status/hosts/<host>/heartbeat.json` periodically publishes that worker's version, protocol, capabilities, and running job IDs. `heaven` also mirrors the legacy `status/heartbeat.json` path for compatibility.

Heartbeat commits are deliberately infrequent to avoid relay commit spam.

## Local desktop control

The bridge exposes structured Win32 desktop primitives so routine interactive control no longer depends on a Remote Desktop MCP. **Default all human-facing desktop actions to `target_host: heaven2`.** Target `heaven` interactively only when the user explicitly asks for the worker desktop or a worker-specific GUI validation genuinely requires it.

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
- `desktop_shortcut_create`: create or replace a `.lnk` on the current user's Desktop with structured target/args/working-directory/description/icon fields.
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


### Semantic Windows UI Automation

Worker v4 adds bounded semantic control through Windows UI Automation. Use `uia_tree` / `uia_find` to inspect controls by stable accessibility properties, then `uia_focus`, `uia_invoke`, `uia_set_value`, `uia_toggle`, `uia_select`, `uia_expand`, or `uia_collapse`. Selectors can use `automation_id`, `name`, `name_contains`, `control_type`, `class_name`, `process_id`, `enabled`, and `offscreen`, with optional window scoping by `hwnd`, `pid`, or title.

Semantic actions fail closed when multiple controls match unless `first_match=true` is explicit, and mutation actions also fail closed if `max_nodes` truncates the search before uniqueness can be proven. Tree depth, node count, text lengths, and waits are bounded. Element descriptors never include ValuePattern text. Password controls are marked with `is_password=true`, and `uia_set_value` refuses them because relay-carried secret values are not credential-safe. `uia_set_value` additionally requires `allow_relay_text=true` so callers explicitly acknowledge that the supplied non-secret text is persisted in the private relay. Coordinate mouse/keyboard actions remain available as a fallback.



## Credential-safe GUI secret input (worker v5)

GitHub relay JSON is persisted, so credentials must **never** be placed in `gui_type`, `clipboard_write`, `uia_set_value`, queue params, result JSON, logs, or controller checkpoints. Worker v5 adds `gui_type_secret`, which carries only an opaque one-time handle plus an exact top-level window `hwnd`.

The credential value stays out-of-band:

1. On `heaven2`, create a dedicated SMB share for one-time credential envelopes. Require SMB encryption on the share and restrict its ACL to the credential-authority account and the `heaven` worker identity.
2. On `heaven`, configure `HEAVEN_BRIDGE_SECRET_INBOX=\\heaven2\<encrypted-share>`, then restart the bridge worker. The worker requires a UNC path and verifies the live Heaven-side SMB connection with `Get-SmbConnection`; `health.features.secret_input.available` remains false and `gui_type_secret` is not advertised unless the exact server/share connection reports `Encrypted=True`. The old `HEAVEN_BRIDGE_SECRET_INBOX_ENCRYPTED` flag is not trusted as transport evidence.
3. Use `heaven-bridge/New-HeavenSecretEnvelope.ps1 -InboxPath <UNC> -Hwnd <window-handle>` on `heaven2`. It prompts with `Read-Host -AsSecureString`, verifies the active SMB connection is encrypted, writes a short-lived destination/HWND-bound envelope, and prints only non-secret metadata: `handle`, `hwnd`, destination, and expiry.
4. Submit `gui_type_secret` through the normal private relay using only `{"handle":"<opaque-hex>","hwnd":12345}`. The worker validates/focuses that exact HWND before consuming the envelope, atomically claims the handle, persists a local replay guard, validates destination/purpose/TTL/target binding, deletes the envelope, injects the value directly with Win32 `SendInput`, and returns only `{"consumed":true,"typed":true}`.

A claimed handle is single-use even if validation or input later fails. Re-create a fresh handle rather than retrying a consumed one. The envelope TTL is bounded (default helper TTL 120 seconds; worker maximum 300 seconds unless explicitly lowered/raised within the hard cap). Secret values are never copied to clipboard or passed through the UIA PowerShell environment.

The bridge test suite includes canary checks ensuring the secret value is absent from canonical relay jobs and returned result payloads, plus expiry, destination-binding, target-binding, unavailable-channel, relay-value rejection, replay, live SMB-encryption verification, and a forced publication-failure regression proving no secret-bearing temp envelope survives a failed rename.
