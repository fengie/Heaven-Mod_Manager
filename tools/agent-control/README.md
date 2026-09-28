# Heaven Agent Control

A zero-dependency local control panel for the user's coding-agent swarm.

## What it does

- Runs only on `127.0.0.1:7331` by default.
- Deploys Manager, Main Programmer, Support, and Reviewer Codex agents.
- Gives every agent a fresh Git worktree and unique `agent/control-*` branch.
- Tracks PID, task, branch, timestamps, status, JSONL output, and the final Codex message.
- Lets you stop a managed worker from the UI.
- Provides `agentctl.mjs` so ChatGPT can operate the same control plane through Remote Desktop Commander.

It intentionally does **not** enumerate unrelated ChatGPT web conversations. ChatGPT does not expose a general API for listing every active chat/agent. The dashboard is authoritative for agents launched through this control plane.

## Start

From this directory:

```bat
Start Agent Control.bat
```

Or:

```powershell
$env:AGENT_CONTROL_REPO="$env:USERPROFILE\local-ai-workspaces\mhw-mods"
node .\server.mjs
```

Then open `http://127.0.0.1:7331`.

## CLI

```powershell
node .\agentctl.mjs status
node .\agentctl.mjs list
node .\agentctl.mjs deploy --role support --task "Audit updater rollback" --count 2 --base agent/auto-updater-20260928
node .\agentctl.mjs log <agent-id>
node .\agentctl.mjs stop <agent-id>
```

## Environment variables

- `AGENT_CONTROL_PORT` — default `7331`
- `AGENT_CONTROL_REPO` — default `%USERPROFILE%\local-ai-workspaces\mhw-mods`
- `AGENT_WORKTREE_ROOT` — default `%USERPROFILE%\agent-worktrees`
- `CODEX_EXE` — optional explicit path to `codex.exe`; otherwise the newest ChatGPT Codex install is discovered automatically.

## Safety / isolation

The server binds to localhost and rejects cross-origin browser requests. It never checks out or force-updates `main`. A deployment fetches the selected base branch, creates a new worktree/branch, and launches Codex there with the `workspace-write` sandbox and automatic approval review. Stopping a worker only targets the PID that this control panel launched.
