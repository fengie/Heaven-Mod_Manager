---
name: heaven-agent-orchestration
description: Coordinate local agents and worker-fabric jobs on the user's `heaven` machine through Heaven Local Bridge. Use for parallel implementation, review, tests, scans, integration support, and token-efficient offloading. Prefer the existing Agent Control plane/local workers when Codex is quota-limited; never fall back to Remote Desktop Commander unless the user explicitly authorizes it.
---

# Heaven Local Agent Orchestration

Use this skill when a task benefits from multiple independent workers or substantial local offload on `heaven`.

## Routing

- `heaven` is the execution/worker machine. `heaven2` is the control and credential-authority machine.
- Durable repository work lands on `main`. The `heaven-bridge` branch is transport/runtime state only.
- Prefer the existing Agent Control plane (`agentctl`) for multi-agent cycles and the Docker worker fabric for independent Linux-compatible work.
- The bridge `codex` action is optional acceleration, not a dependency. If Codex reports quota/credits/runtime unavailability, do not keep retrying it. Continue with Agent Control/local workers, structured `fs_*` actions, and `proc_run`.
- Never use Remote Desktop Commander unless the user explicitly authorizes it in the current request.

## Multi-agent execution

For substantial work, split along non-overlapping boundaries such as implementation, security review, tests, performance, and integration. Give each worker an explicit repository/ref, ownership boundary, validation command, expected artifact, and merge rule. Use isolated worktrees/branches for concurrent source edits.

Do not let parallel agents all mutate `main` independently. Completed branches must be reconciled against current `origin/main`, validated, integrated to `main`, pushed, verified remotely, and then cleaned up. Never merge relay queue/results/status history into `main`.

## Agent Control

When Agent Control is available on `heaven`, prefer it for long multi-agent cycles because orchestration and polling remain local and reduce chat/relay traffic. Return the cycle id/state path and then inspect compact cycle state or final artifacts rather than streaming every sub-agent message through the relay.

If a cycle is already active for the same objective, inspect/reuse it instead of starting a duplicate cycle. Treat local agent output as proposals until repository tests and exact-HEAD verification pass.

## Worker fabric

Use the existing base workers `heaven-worker-01` through `heaven-worker-04` and burst workers `heaven-worker-05` through `heaven-worker-08` for independent Linux-compatible build/test/scan/transform jobs. Keep Windows GUI, hardware control, credential-sensitive tasks, and machine administration on the host worker. Never copy heaven2 credentials into containers.

## Control-plane reliability

A queued job is not completion. Read status/result evidence. If relay scheduling is congested, avoid submitting duplicate logical jobs. Prefer control actions (`health`, `job_status`, `cancel`) and one compact management probe. Until priority-aware scheduling is advertised by health, be aware that older filename-sorted work can delay later jobs even when `priority` says `highest`; do not interpret queue delay as worker death.

## Token economy

Do the noisy work locally. Ask workers to persist full logs and return compact evidence: branch/HEAD, changed files, exit code, pass/fail counts, important errors, artifact paths, and a short tail. Use local models for low-risk summarization/repetitive transformations, but never trust a model summary over tests or repository state.

## Security

Do not place passwords, API keys, tokens, cookies, private keys, or recovery codes into agent prompts, relay payloads, queue/results, logs, controller state, screenshots, or container environments. Credential-safe cross-machine handling must use the bridge's direct secret channel once that capability is explicitly advertised; otherwise keep secrets on `heaven2`.

## Completion

Parallel work is complete only when the integration owner has reconciled all useful changes, run the relevant exact-main gates, pushed verified `main`, and removed finished temporary branches/worktrees that contain no unique work.
