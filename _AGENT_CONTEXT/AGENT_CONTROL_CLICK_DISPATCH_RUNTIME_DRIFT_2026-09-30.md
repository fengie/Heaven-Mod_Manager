# Agent Control click-dispatch and stale-runtime incident — 2026-09-30

## Symptom

Agent Control accepted operator clicks and created manager/programmer/support cards, but the jobs immediately failed, retried, and accumulated as `failed · RETRY EXHAUSTED`. The live dashboard showed no active leases while dozens of managed records remained visible.

## Live evidence

- The live controller on `heaven2` was listening on `127.0.0.1:7331`.
- It was running from `C:\Users\fengc\local-ai-workspaces\mhw-mods` at `5a3219d49056e4a8957f162b3d8913478d687a1e`, version `8.8.19` / Agent Control `0.5.8`.
- That checkout reported `main...origin/main [behind 1205]` when inspected.
- Independent manager, programmer, and support-agent logs all exited with code 1 at `submitHeavenBridgeJob()` with:
  `AGENT_CONTROL_HEAVEN_RELAY_DIR is required for bridge execution.`
- Startup persistence was hard-pinned to that stale checkout by `%LOCALAPPDATA%\MHW-Agent-Control\Run-StartupRestore.ps1`.
- Current source already let health inspection discover the documented `%USERPROFILE%\HeavenBridgeRepo` fallback, but dispatch/result-wait still read the environment variable directly. A controller could therefore report bridge presence while click-dispatched execution failed.

## Root cause

Two independent defects compounded:

1. **Relay resolution drift:** bridge health and bridge execution used different relay-directory resolution rules. The startup environment omitted `AGENT_CONTROL_HEAVEN_RELAY_DIR`, so dispatch failed before a remote agent could acquire work.
2. **Runtime source drift:** persistent startup restored a clean but obsolete local `main` checkout without synchronizing it to canonical `origin/main`. A healthy TCP listener was therefore not evidence that the controller code was current.

The terminal-card/retry-exhaustion retention defect is tracked and fixed in the separate retry-exhausted lifecycle lane; this incident deliberately does not duplicate that implementation.

## Required invariant

- Health inspection, job submission, and result waiting must resolve the same dedicated relay checkout: explicit `AGENT_CONTROL_HEAVEN_RELAY_DIR` first; otherwise the documented per-user `HeavenBridgeRepo` only when it exists; otherwise fail closed with an actionable error.
- Before startup launches Agent Control from canonical `main`, a clean runtime checkout must fetch and fast-forward to `origin/main`. Dirty, non-main, or diverged runtime work must never be rewritten automatically.
- If startup fast-forwards while a controller is already listening, replacement is allowed only after proving the listener PID matches the persisted Agent Control controller identity and a Node `server.mjs` process.

## Fix

- `heaven-bridge-provider.mjs`: dispatch and result wait now share `resolveHeavenRelayDir()` with bridge health inspection.
- `Restore-StartupSetup.ps1`: clean canonical runtime source is fetched/fast-forwarded before launch; dirty/non-main/diverged trees are preserved and reported; a stale already-running controller is replaced only with explicit process-ownership proof.
- Startup also exports the documented relay checkout into the launched process when no explicit relay environment is set.
- Regression tests mechanically lock the relay-resolution and startup-freshness invariants.

## Verification

Run on the exact integration candidate:

```powershell
node --test tools/agent-control/test/heaven-bridge-provider.test.mjs tools/agent-control/test/startup-restore.test.mjs
npm --prefix tools/agent-control run check
npm --prefix tools/agent-control test
git diff --check
```

After integration, synchronize the live clean runtime checkout to exact canonical `main`, rerun the startup installer/restore, verify `/api/status`, and exercise one bounded click-dispatch smoke. The smoke is successful only if the spawned agent gets past relay submission without the missing-relay error.

## Successor notes

- Keep relay checkout discovery centralized; do not reintroduce direct `process.env.AGENT_CONTROL_HEAVEN_RELAY_DIR` defaults on execution paths.
- Startup freshness must remain fail-safe: never auto-reset a dirty or diverged checkout.
- A future improvement should expose the running controller source SHA/version directly in `/api/status` so runtime drift is visible without host inspection.
- Coordinate with the retry-exhausted cleanup lane before integration because both touch Agent Control lifecycle evidence and release metadata.
