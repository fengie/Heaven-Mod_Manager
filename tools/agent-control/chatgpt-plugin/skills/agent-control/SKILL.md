---
name: agent-control
description: Operate the user's heaven2-hosted MHW agent control plane. Use when the user asks to inspect the swarm, list agents/workers/tasks/leases, deploy an MHW coding agent, launch a reviewer, read an agent's output, stop a managed agent, synchronize Git branch state, or inspect the integration queue.
---

# Heaven Agent Control

Use the user's authorized **Heaven Local Bridge** to operate the local controller on the device named `heaven2`. Every bridge job that starts, reads, or controls Agent Control must set top-level `"target_host": "heaven2"`; the controller's `127.0.0.1` belongs to heaven2. Do not silently substitute Remote Desktop Commander when Heaven Local Bridge can perform the action.

`heaven2` is the operator/control-plane machine and credential authority. All Agent Control dashboards, CLI/controller interaction, human-facing control surfaces, browser/UI automation, and routine desktop interaction belong there. `heaven` is a worker/resource machine used for heavy builds, tests, scans, local agents, indexing, and other delegated execution. Do not move the control plane or operator UI to `heaven` merely because work executes there.

## Locate the CLI

Prefer these paths in order:

1. `%USERPROFILE%\agent-control-panel\tools\agent-control\agentctl.mjs`
2. `%USERPROFILE%\local-ai-workspaces\mhw-mods\tools\agent-control\agentctl.mjs`

Use Heaven Local Bridge with `target_host: heaven2` to test which path exists. Do not guess a third path without inspecting the machine.

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

The snapshot combines managed local workers with the normalized federated registry. It includes tasks, workers, leases, integration candidates, providers, federated agents, heartbeat freshness, observed swarm branches, telemetry, and recent controller events.

Automatic enumeration of arbitrary ChatGPT project conversations is unavailable. When a stable ChatGPT session identity is available through the bridge, register/heartbeat it rather than inventing telemetry.

**Registration is the default first step for this skill.** Whenever the current ChatGPT runtime exposes a real stable session/conversation identity, register or heartbeat that identity before reading or changing swarm state, and heartbeat it again after meaningful state transitions. This keeps the federated registry current without pretending that undiscoverable sessions are known. If no stable identity is exposed, continue normally and leave coverage explicitly partial; never synthesize one from a title.

## Execution mode

Normal Chat is preferred. ChatGPT Work handoff is deny-by-default.

- Never trigger, request, suggest, or route through a ChatGPT Work handoff unless the user explicitly asks for Work mode for the current task.
- A long/complex task, file creation, code changes, browser/computer use, a failed tool, or missing preferred capability is not permission to switch to Work.
- If the current ChatGPT conversation can execute the task, do the work here. Do not tell the user to switch modes as a fallback.
- Agent Control cannot auto-create arbitrary normal ChatGPT conversations. When it must dispatch, use the default direct non-Work local worker path instead of failing closed. Heaven Local Bridge and repository tooling remain valid non-Work routes.
- Work and Codex are distinct. An explicit Work request must not be translated into Codex, and ordinary Chat/direct/Codex execution must never escalate into Work.
- A Codex quota/capacity failure must not trigger a retry storm on the same blocked provider; preserve work and route through another authorized non-Work path when possible.

## Deploy an agent (non-Work by default)

Prefer a temporary task file instead of shell-embedding the user's prompt.

1. Use Heaven Local Bridge with `target_host: heaven2` to create a UTF-8 task file under `%TEMP%`, containing exactly the task the user wants assigned.
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

## Federated session registration

Use `node <agentctl-path> federation` and `providers` to inspect normalized provider state.

When the bridge has a real stable external identity, create a small JSON observation and run:

```powershell
node <agentctl-path> federation-register --file <observation.json>
node <agentctl-path> federation-heartbeat --file <heartbeat.json>
```

Required observation fields are `provider`, `source_id`, and normalized `state`. Prefer real stable provider IDs; never use a chat title as identity. Use explicit `correlation_keys` only when two provider observations are known to represent the same logical worker.

A fresh registration/heartbeat is observability evidence, not remote execution authority.

## Adaptive ChatGPT Work handoff recovery

Agent Control treats ChatGPT Work handoffs as UI that may change over time. The controller keeps a mutable, versioned signature registry at `tools/agent-control/data/work-handoff-signatures.json` on heaven2 (the directory is gitignored) and exposes it at `GET /api/work-handoff-signatures`.

- Known labels are handled normally.
- If ChatGPT renames the card/buttons but the accessible UI still exposes one clear Work action and one clear non-Work action on the same action row, Agent Control chooses the non-Work action, verifies the Work action disappeared, then learns those labels into the registry.
- If the changed UI is ambiguous, Agent Control does **not** guess. It records a bounded drift observation in the registry and raises a control-plane warning so the plugin can be updated deliberately.
- Learned runtime signatures update behavior immediately without modifying tracked repository files or dirtying canonical `main`.
- Never reinterpret an unknown button as permission to enter Work. The only adaptive action allowed is preserving normal Chat/non-Work execution.
