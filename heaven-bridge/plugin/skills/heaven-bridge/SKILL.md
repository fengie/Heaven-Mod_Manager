---
name: heaven-bridge
description: Primary computer-control and development path for the user's `heaven` worker. Use for local filesystem, code build/test/debug execution, persistent dev processes, binary transfer, search, terminal, process/session, screenshot/desktop control, automation, durable controller state, local-agent work, worker-fabric offloading, and token-efficient local preprocessing. Use this bridge instead of Remote Desktop Commander; do not fall back to Remote Desktop Commander unless the user explicitly authorizes it in the current request.
---

# Heaven Local Bridge v6

Use this as the default computer-control path for `heaven`.

## Canonical source vs transport branch

- Repository: `fengie/mhw-mods`.
- **Durable bridge implementation source:** `main` (`heaven-bridge/worker.py`, tests, bootstrap/manage scripts, workflow gate, README).
- **Live transport branch:** `heaven-bridge` only for `queue/`, `status/`, `results/`, controller transport/state, and worker heartbeat/result publication.
- Never treat the transport branch as the canonical implementation branch and never wholesale-merge its queue/results/status history into `main`.
- When development changes are complete, sync/rebase against current `main`, run the bridge test gates, push verified `main`, and keep the relay branch alive for transport.

## Architecture and machine policy

- ChatGPT -> private GitHub relay branch -> local worker on `heaven` -> Windows/files/processes/local agents/desktop -> result/status back through GitHub.
- Queue: `heaven-bridge/queue/<job-id>.json` on the `heaven-bridge` transport branch.
- Results: `heaven-bridge/results/<job-id>.json`.
- Status: `heaven-bridge/status/<job-id>.json` and `heaven-bridge/status/heartbeat.json`.
- Protocol remains `chatgpt-heaven-bridge-v2` for compatibility.
- Healthy implementation reports `data.worker_version: 6`.
- `heaven` is the worker/execution machine. `heaven2` is the main/control machine and credential authority.
- Keep secrets on `heaven2` unless runtime access is explicitly required. Never place secrets in queue/results/logs/commits.
- Do not expose an unauthenticated raw shell to the public internet.
- Do not use Remote Desktop Commander for heaven work unless the user explicitly authorizes it in the current request. A broken bridge is a bridge-repair task, not implicit permission to switch remote-control providers.

## Dispatch format

Create a unique job file on the `heaven-bridge` transport branch:

```json
{
  "id": "chatgpt-YYYYMMDD-HHMMSS-<suffix>",
  "source": "chatgpt-heaven-bridge-v2",
  "created_at": "<current ISO-8601 UTC timestamp>",
  "ttl_seconds": 3600,
  "action": "<action>",
  "params": {},
  "priority": "highest"
}
```

The `id` must match the queue filename. Use a current timestamp: the worker rejects expired jobs and jobs too far in the future. After dispatch, read `status/<id>.json` for running/completed state and `results/<id>.json` for the authoritative result. Queue creation alone is not completion.

The private-repo ACL is the compatibility auth mode when no local HMAC key is configured. If `HEAVEN_BRIDGE_HMAC_KEY` is configured on the worker, jobs must follow the worker's HMAC-SHA256 signing format; do not invent or transmit the secret through GitHub.

## Health and capability negotiation

Start a new workflow with `health` when worker state matters. Healthy v6 must report `status: completed`, `host: heaven`, `data.worker_version: 6`, `data.protocol: chatgpt-heaven-bridge-v2`, `data.elevated: true` when admin work is required, and advertised actions/capabilities. Also inspect `heaven-bridge/status/heartbeat.json` when diagnosing liveness. Treat advertised actions as capability negotiation.

Expected current capability families include:
- filesystem/text/binary/search: `fs_read`, `fs_read_many`, `fs_write`, `fs_edit`, `fs_mkdir`, `fs_list`, `fs_move`, `fs_info`, `fs_search`, `fs_copy`, `fs_delete`, `fs_read_binary`, `fs_write_binary`
- synchronous/persistent processes: `proc_run`, `proc_start`, `proc_input`, `proc_read`, `proc_kill`, `proc_list_sessions`, `proc_list`, `job_output_read`
- job control/waits: `job_status`, `cancel`, `wait_for` (`file_*`, `process_*`, `session_*`, and `window_*` conditions with bounded timeout/cancellation evidence)
- desktop: `screenshot`, `display_list`, `app_launch`, `window_list`, `window_focus`, `window_move`, `window_state`, `window_close`, `gui_cursor_get`, `gui_mouse_move`, `gui_mouse_click`, `gui_mouse_button`, `gui_mouse_scroll`, `gui_key`, `gui_type`, `gui_type_secret` (opaque handle + exact HWND only; credential bytes stay in the encrypted out-of-band inbox)
- semantic UIA: `uia_tree`, `uia_find`, `uia_focus`, `uia_invoke`, `uia_set_value`, `uia_toggle`, `uia_select`, `uia_expand`, `uia_collapse`; mutation searches fail closed on ambiguity/truncation, password values are never exposed, and `uia_set_value` requires explicit non-secret relay opt-in
- clipboard: `clipboard_read`, `clipboard_write` (relay reads require explicit opt-in)
- desktop shortcuts: `desktop_shortcut_create` for structured `.lnk` creation on the target user's Desktop
- controller/agents: `controller_checkpoint`, `codex`

## Development and test workflow

For repository work, the bridge is intended to replace Remote Desktop command-by-command operation:

1. Use a dedicated clone/worktree under an allowed root. Fetch GitHub and treat `origin/main` as implementation truth.
2. Use `proc_run` for noninteractive Git operations, formatters, compilers, unit tests, integration tests, package managers, linters, and build commands. Persist full logs locally and return compact summaries.
3. Use `proc_start` for persistent shells/dev servers/watchers, then `proc_input`, `proc_read`, and `proc_kill` for interaction/lifecycle. Use a separate `proc_run` for health checks or HTTP probes while a dev server is running.
4. Use structured filesystem/search actions to inspect or edit only the files needed. Prefer `fs_search` and bounded reads over dumping whole repositories.
5. Use screenshots/window/mouse/keyboard/app-launch only when the workflow genuinely requires GUI validation; do not claim GUI state without evidence.
6. Run the repo's bridge gates before publishing implementation changes. For the current repository this includes Python syntax compile plus `heaven-bridge/test_worker.py` and discovery under `heaven-bridge/tests`.
7. Push completed durable changes to `main`; do not leave finished bridge implementation solely on temporary branches. Never merge transport queue/results/status history into `main`.
8. Local Codex is optional acceleration, not a dependency. If `codex` hits quota or is unavailable, continue with structured bridge actions and `proc_run`; do not treat this as bridge failure and do not fall back to Remote Desktop Commander.

## Durable controller checkpoint invariant

Permanent development cycles must never finish with a separate direct ChatGPT -> GitHub write of `heaven-bridge/controller/state.json`. Use the bridge-owned `controller_checkpoint` action with `expected_previous_cycle_id`. The worker performs the state write, commit, rebase, and push under the relay Git lock. A cycle is not durably complete until the checkpoint result reports `persisted: true` and ChatGPT reads the controller state back from GitHub.

## Job status, concurrency, cancellation

Use `job_status` for current state and `cancel` when a long-running job should be stopped. Do not launch duplicate jobs merely because a result is delayed; unique IDs plus idempotency/result-cache recovery are the normal rule. Worker v5 supports bounded concurrency; Git publication is serialized separately.

Priority scheduling keeps control-plane actions responsive even when ordinary worker slots are saturated. Ordinary jobs accept named priorities or numeric 0-100 values and age upward over time, preventing lower-priority work from starving indefinitely.

## Screenshot and desktop control

When `health` advertises desktop capabilities, use the structured screenshot/window/mouse/keyboard/app/clipboard actions. Screenshots may return local PNG paths; retrieve bytes with `fs_read_binary` only when necessary. Clipboard read relay requires explicit opt-in. Do not emulate desktop actions with raw shell when structured actions exist.

## Worker fabric and automatic offloading

`heaven` has a local Docker worker fabric inside the existing Ubuntu WSL2 runtime. Prefer it for independent Linux-compatible CPU-heavy build/test/scan/transform/batch work that does not need Windows GUI state or credentials. Base workers are `heaven-worker-01` through `heaven-worker-04`; burst workers are `heaven-worker-05` through `heaven-worker-08`. Keep Windows-only, GUI, hardware-control, credential-sensitive, or machine-administration commands on the host worker. Never copy heaven2 credentials into a worker container merely to make offloading easier.

## Token economy

Do work locally first and return decision-relevant evidence rather than raw chatter. For noisy commands, persist full stdout/stderr locally and return exit code, duration, important counts/paths/versions, relevant error lines, and a short tail. Before reading a large repo/diff, ask the machine for compact context: branch/HEAD, `git status --short`, `git diff --stat`, recent commits, and changed filenames. Pull full diffs only where needed.

## Local-model economy

Local Ollama/Aider models may be used for low-risk compression or repetitive implementation work, but underlying logs/files remain authoritative and tests must verify correctness. Do not use a local model as the sole basis for high-stakes or irreversible changes.

## Resource-aware behavior

`heaven` is a shared interactive Windows machine. Check available host/WSL memory before burst parallelism or large models; prefer short-lived local model loads, bounded worker memory, and reduced concurrency over heavy paging.

## Repository and reliability policy

- `main` is the canonical integration branch for durable bridge implementation.
- The `heaven-bridge` branch is the long-lived relay/transport branch and is an exception to normal temporary-branch cleanup.
- Temporary implementation branches/worktrees are allowed only while active; once complete, integrate verified work into `main`, push/verify remote `main`, then delete finished temporary branches when safe.
- Never reset/rewrite/force-update canonical branches.
- The relay uses Git retry/rebase handling, atomic local result/cache writes, heartbeat, status records, audit logs, bounded concurrency, TTL/rate limits, and duplicate protection.
- Multiple agents may write the relay branch concurrently. Treat transient GitHub conflicts as retryable and do not create duplicate logical jobs unnecessarily.

## Completion rule

A bridge operation is complete only when result JSON or equivalent verified status confirms the operation. A durable implementation change is complete only after relevant local tests pass and remote `main` is verified to contain the final commit. Permanent development cycles additionally require a verified `controller_checkpoint` and readback. Never claim queue submission, execution, or state persistence succeeded without authoritative evidence.
