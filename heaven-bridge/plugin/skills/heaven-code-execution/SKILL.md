---
name: heaven-code-execution
description: Run, build, test, lint, benchmark and verify code on the user's heaven worker through Heaven Local Bridge. Use for repository implementation validation and local coding-agent execution instead of Remote Desktop Commander.
---

# Heaven Code Execution

Use Heaven Local Bridge as the default execution backend for code and repository work on `heaven`.

All agent-owned command, PowerShell, test/build, and local-agent child processes on both `heaven` and `heaven2` must stay in the background by default and must not steal focus. Use the bridge's structured execution paths, which enforce hidden/no-console Windows process creation. Request a visible console only when the task explicitly requires interactive visible terminal use.

## Core rule

For code verification, do not merely inspect files or claim that commands should pass. Execute the relevant commands through `proc_run` or a persistent process session and read the authoritative bridge result.

Use `proc_run` for bounded commands. Use `proc_start` + `proc_read`/`proc_input`/`proc_kill` for long-lived servers, REPLs, watchers or interactive test processes. Use `job_output_read` for paged output when stdout/stderr is large.

## What to run

Choose commands from the repository's own build/test configuration. Typical examples include:

- Python: `python -m py_compile`, `python -m unittest`, `python -m pytest`
- Node: `npm run check`, `npm test`, targeted `node --test`
- .NET: `dotnet build`, `dotnet test`, repository verification scripts
- Git: `git diff --check`, exact-HEAD/status checks
- Project scripts: the repository's documented verify/build/release gates

Do not invent success. Report the exact command, target revision when relevant, exit code and meaningful pass/fail counts.

## Exact-main verification

The relay checkout lives on branch `heaven-bridge`; do not switch that active transport checkout to `main`.

When validating canonical `main`, fetch current `origin/main` and use a temporary clone or detached worktree. Run tests there, record the exact commit SHA, then remove the temporary worktree. This lets the relay keep serving jobs while testing real `main`.

## Repository integration

`main` is canonical for completed development work. Temporary task branches are allowed only while work is active. After implementation is complete: fetch current `main`, reconcile, rerun affected validation, integrate to `main`, push and verify remote `main`, then delete completed temporary branches only after proving they contain no unique work.

Do not merge the relay branch wholesale into `main`; it contains transport queue/result/status traffic. Promote source changes selectively.

## Heavy/local-agent execution

Prefer `heaven` for builds, tests, scans, indexing and local coding-agent work. Use the existing Agent Control plane or bridge `codex` action for substantial coding tasks when appropriate. Keep credentials on `heaven2`; never route secrets through GitHub payloads.

## Token economy

Keep full logs on `heaven`. Return compact evidence first: revision, command, exit code, duration, pass/fail counts, key errors and relevant paths. Pull more output only when needed to diagnose a failure.
