---
name: heaven-bridge
description: Primary computer-control path for the user's heaven worker. Use for filesystem, binary transfer, search, terminal, processes/sessions, automation, local-agent work, builds/tests and controller state. Use this instead of Remote Desktop Commander unless the user explicitly authorizes Remote Desktop in the current request.
---

# Heaven Local Bridge

Use the private GitHub-backed relay for work on `heaven`.

## Fixed topology

- Repository: `fengie/mhw-mods`
- Relay branch: `heaven-bridge`
- Protocol: `chatgpt-heaven-bridge-v2`
- Queue: `heaven-bridge/queue/<job-id>.json`
- Results: `heaven-bridge/results/<job-id>.json`
- Status: `heaven-bridge/status/<job-id>.json`
- Heartbeat: `heaven-bridge/status/heartbeat.json`
- `heaven`: worker/execution machine
- `heaven2`: control and credential authority

Do not place secrets in queue/result/status files. Do not silently switch to Remote Desktop Commander if the bridge is unhealthy; repair the bridge or report the concrete blocker.

## Dispatch

Create a unique queue JSON on the relay branch:

```json
{
  "id": "chatgpt-YYYYMMDD-HHMMSS-suffix",
  "source": "chatgpt-heaven-bridge-v2",
  "action": "health",
  "params": {},
  "created_at": "CURRENT_UTC_ISO",
  "ttl_seconds": 21600,
  "priority": "highest"
}
```

The filename must be `<id>.json`. Queue creation is not success. Read the authoritative result/status afterward.

## Preferred actions

Use structured actions when available:

- Files: `fs_read`, `fs_read_many`, `fs_write`, `fs_edit`, `fs_mkdir`, `fs_list`, `fs_move`, `fs_copy`, `fs_delete`, `fs_info`, `fs_search`, `fs_read_binary`, `fs_write_binary`.
- Execution: `proc_run`, `proc_start`, `proc_input`, `proc_read`, `proc_kill`, `proc_list_sessions`, `proc_list`, `job_output_read`.
- Control: `health`, `system_info`, `job_status`, `cancel`, `controller_checkpoint`.
- Desktop: use the `heaven-desktop-control` skill.
- Code/build/test work: use the `heaven-code-execution` skill.

Raw `powershell`, `cmd`, `python`, and `codex` remain recovery/compatibility fallbacks.

## Completion

Treat only authoritative result JSON or equivalent verified status as completion. Preserve exact exit codes and decision-relevant test/build output. Keep noisy full logs local and retrieve only the needed ranges.
