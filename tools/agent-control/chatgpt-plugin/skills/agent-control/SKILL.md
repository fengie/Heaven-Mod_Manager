---
name: agent-control
description: Operate the user's Heaven-hosted MHW agent control plane. Use when the user asks to inspect the swarm, list agents/workers/tasks/leases, deploy an MHW coding agent, launch a reviewer, read an agent's output, stop a managed agent, synchronize Git branch state, or inspect the integration queue.
---

# Heaven Agent Control

Use the authorized **Remote Desktop Commander** connection to operate the local controller on the device named `heaven`.

Do not use `heaven2` unless the user explicitly asks for it.

## Locate the CLI

Prefer these paths in order:

1. `%USERPROFILE%\agent-control-panel\tools\agent-control\agentctl.mjs`
2. `%USERPROFILE%\local-ai-workspaces\mhw-mods\tools\agent-control\agentctl.mjs`

Use Remote Desktop Commander to test which path exists. Do not guess a third path without inspecting the machine.

## Ensure the controller is running

First run:

```powershell
node <agentctl-path> status
```

If the request fails because the local controller is offline, start:

```powershell
Set-Location (Split-Path <agentctl-path>)
node .\server.mjs
```

Run the server as a long-lived process, then retry `status`.

## Read swarm state

Use:

```powershell
node <agentctl-path> snapshot
```

The snapshot is the authoritative runtime view for managed workers. It includes agents, tasks, workers, leases, integration candidates, observed swarm branches, telemetry, and recent controller events.

Be explicit that unrelated ChatGPT web conversations cannot be enumerated unless they were launched through the control plane or left observable Git state.

## Deploy an agent

Prefer a temporary task file instead of shell-embedding the user's prompt.

1. Use Remote Desktop Commander `write_file` to create a UTF-8 text file under `%TEMP%`, containing exactly the task the user wants assigned.
2. Run:

```powershell
node <agentctl-path> deploy --role <role> --task-file <temp-file> --base <branch> --priority <0-100>
```

Optional:

```text
--count N
--boundary NAME
--model MODEL
--machine auto
--depends TASK_ID[,TASK_ID...]
```

Roles:
- `manager`
- `main`
- `support`
- `reviewer`
- `test`
- `integration`
- `recovery`
- `release`

Default to one agent unless the user asks for more or the task clearly calls for a bounded fan-out already established in the conversation.

Default the base branch to the branch the user is actively targeting. If that is not established, use `main`.

For implementation work that could collide with another managed agent, give it a meaningful `--boundary`. For independent research/support work, the controller's automatic isolated lease is acceptable.

Never deploy a new implementation owner onto a named boundary that the snapshot already shows as actively leased.

## Review completed work

To deploy a reviewer against a managed agent's branch:

```powershell
node <agentctl-path> review <agent-id>
```

Use this when the user asks to review, audit, validate, or support a completed managed branch.

## Inspect output

```powershell
node <agentctl-path> log <agent-id>
```

Summarize the meaningful result. Do not dump large JSONL tails unless the user asks.

## Stop a managed worker

Only when the user explicitly asks to stop/cancel/kill that managed agent:

```powershell
node <agentctl-path> stop <agent-id>
```

Do not terminate unrelated processes or agents discovered only through Git branch observation.

## Sync Git state

Use:

```powershell
node <agentctl-path> sync
```

This fetches/prunes origin and refreshes the branch/integration view.

## Safety

- Keep `main` untouched.
- Use the controller's isolated worktrees.
- Respect active mutable-boundary leases.
- Do not claim an external observed branch is a live process.
- Do not claim verification or merge readiness beyond the repository's exact evidence.
- Do not automatically merge candidates unless the user separately authorizes that action and the repository's integration/verification rules are satisfied.
