# Heaven Local Bridge

Private GitHub-backed dispatch bridge for the `heaven` worker machine.

## Flow

1. ChatGPT creates a JSON job in `heaven-bridge/queue/` on the private `heaven-bridge` branch.
2. `worker.py` on `heaven` pulls that branch using the machine's existing Git Credential Manager authentication.
3. The worker executes the job locally.
4. The worker writes `heaven-bridge/results/<job-id>.json`, commits it, and pushes it back.
5. ChatGPT reads the result through the GitHub connector.

This steady-state path does not require Remote Desktop Commander.

## Job schema

```json
{
  "id": "unique-job-id",
  "source": "chatgpt-heaven-bridge-v1",
  "created_at": "2026-09-28T18:00:00Z",
  "action": "codex",
  "payload": "Do the requested local task.",
  "cwd": "C:\\Users\\Xxkan",
  "timeout_seconds": 1800
}
```

Allowed actions: `codex`, `powershell`, `cmd`, `python`.

Do not place credentials, API keys, tokens, cookies, or other secrets in queue jobs or results.
