# Agent Control Deterministic Failure / Retry-Storm Handoff — 2026-09-30

## Status

Canonical `main` contains the retry-storm fix. The relevant Agent Control source/test blobs on current main remain identical to the exact source verified on Heaven.

## Incident

Perpetual swarm workers were exiting with authoritative process code 1 and empty/minimal terminal output. The controller then classified those exits as generic "no-work", requeued replacements, and could later select the same clean deterministic failures for swarm-tail recovery. This amplified one runtime failure into rapid replacement chains, retry exhaustion, and an autopilot safety-gate stop.

A failed implementation owner could also be routed into a repair lane despite there being no proven candidate to repair.

## Integrated fix commits

- `fe302e78c453dd4cfb1630402368cb0ac57da80b` — `fix(agent-control): stop retrying deterministic runtime failures`
- `dc6e2df967f68a67d8ae108ae1ebdd87071fc96c` — `test(agent-control): cover deterministic failure retry suppression`
- `d1def9486620f1cc3b9016227fd25f42e87b9b09` — `fix(agent-control): gate deterministic implementation failure`
- `3d918342b5a7d4c7d3b9fa25ae989e826b5ebd1c` — `test(agent-control): prevent meaningless repair after implementation crash`

## Behavior after fix

1. Authoritative nonzero exit codes, spawn failures, and provider-capacity completion evidence are deterministic runtime failures and outrank empty-output/no-work heuristics.
2. Deterministic runtime failures are not automatically no-work retried.
3. Swarm-tail recovery does not resurrect a clean deterministic failure.
4. If a deterministic failure left substantive durable work, that work remains eligible for preservation/recovery.
5. A failed implementation owner stops at a governed gate instead of spawning a meaningless repair lane.
6. Existing interrupted/stream-loss recovery remains available when execution outcome is genuinely unknown.

## Regression coverage

`tools/agent-control/test/no-work-recovery.test.mjs` covers:
- authoritative nonzero exit is not no-work;
- stream-loss metadata cannot override authoritative nonzero failure;
- clean deterministic failure is not resurrected by swarm-tail recovery;
- deterministic failure with substantive work remains recoverable.

`tools/agent-control/test/autopilot-core.test.mjs` covers failed implementation ownership gating.

## Exact verification evidence

Heaven Local Bridge job:
- `chatgpt-agentcontrol-retry-fix-verify-20260930-0437`
- target: `heaven`
- detached exact `origin/main` at `cbe685ad770952c771cf129e2b39b1fc62de63af`
- commands:
  - `node --test test/no-work-recovery.test.mjs test/autopilot-core.test.mjs`
  - `node --check server.mjs`
- result: **56 tests / 56 passed / 0 failed**, server syntax check passed, `AGENT_CONTROL_VERIFY_OK`, process exit 0.

A second verification at `ee428f106b3cff7f3a4f570a66666a5bfb70eb61` also passed the same 56/56 + syntax check.

At the time of this handoff, current main had advanced through unrelated commits, but the four relevant source/test blob SHAs were still exactly:
- `no-work-recovery.mjs`: `e8788b9aafac72bdc857786d33b72eff81dae3cb`
- `autopilot-core.mjs`: `bfa0171ab67027aeaa3c4a9e463a175c7f75c528`
- `no-work-recovery.test.mjs`: `663be18dc2172baf5d8162534f99916d9d87c410`
- `autopilot-core.test.mjs`: `5789ccc524c3a4709155736e4ccb6a1921f4428f`

Therefore the exact tested Agent Control fix content remains present on current canonical main.

## Prevention records

- `_AGENT_CONTEXT/BUG_PRECEDENTS.md` records the deterministic-runtime/no-work invariant violation and regression strategy.
- `_AGENT_CONTEXT/LEARNED_RULES.md` contains **LR-039 — deterministic runtime failure is not no-work**.

## Remaining diagnostic / next steps

The retry amplification is fixed. The original process exit-1 cause should still be diagnosed separately; the controller now preserves that first failure instead of obscuring it through automatic replacement storms.

Runtime truth:
- Agent Control control host: `heaven2`
- canonical local repo path observed: `C:\Users\fengc\local-ai-workspaces\mhw-mods`
- controller port observed: 7331
- runtime data defaults to `tools\agent-control\data`
- failed agent IDs worth inspecting include:
  - `manager-20260930042717-kmbi6`
  - `main-20260930042701-ldewu`
  - their immediate predecessors.

A focused bridge diagnostic was queued as:
- `chatgpt-agentcontrol-original-exit-diagnose-20260930-0445`

Lightweight direct runtime-file searches/reads were also queued for those two latest failed IDs. Consume those results if available before changing execution-provider behavior.

Both Heaven bridge workers were healthy, but recent heartbeat telemetry showed memory-headroom pressure. Treat that as a scheduling/capacity condition, not as proof of the original code-1 cause.

GitHub's self-hosted `Agent Control PR Gate` jobs were still queued when this handoff was written. Do not confuse the queued Actions runner lane with the successful exact-main Heaven Local Bridge verification above.

## Successor rule

Do not reintroduce retry-on-empty-output ahead of authoritative process outcome. Preserve this ordering:

**authoritative runtime outcome -> durable-work evidence -> transport/output heuristics -> bounded recovery decision**

If a future failure is deterministic and clean, surface the failure once with diagnostic evidence. If useful durable work exists, preserve/reconcile it. Never amplify one deterministic failure into a replacement storm.
