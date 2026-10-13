# Agent Command Center continuation — 2026-09-28

## Scope and provenance

This continuity record covers the first command-center upgrade wave for Heaven Agent Control.

- canonical repository: `fengie/mhw-mods`
- canonical `main` at the original implementation-wave branch construction: `a8b581176aac0e6bcf09c049285ed40f4b2b392c`
- latest canonical `main` observed while reconciling parallel work: `151a370ef6c0b3d4e6b1d8a306576af1ae231c40`
- safety-hardened control-plane implementation owner: draft PR #59, branch `feature/agent-control-plane-v2-20260928`
- exact v2 target head for this dependent command-center patch: `41d377d1c6ce5b0b0f747aa37484eaadc9033ce5`
- dependent continuation branch: `feature/agent-command-center-ui-20260928`
- dependent worktree used on heaven2: `C:\Users\fengc\mhw-agent-command-center-dependent-20260928`
- a current-main-rebased scratch branch was used only to prove the UI/CLI delta rebased cleanly; it is not the integration target.
- existing PR #47 and the interrupted local v2/v3 worktrees were not overwritten.

Repository routing issue/PR comments established PR #59 as the **sole broad Agent Control implementation lane**. This work is therefore packaged as a dependent command-center/UI+CLI patch targeting PR #59 rather than as a competing mainline control-plane implementation.

## Governance / machine policy preserved

Repository governance remains authoritative over this control plane. The normal repository read order, recursive continuity contract, evidence discipline, one-boundary ownership model, and no-fake-verification rule still apply.

Machine policy remains hard:

- **heaven** is a background worker by default. Without explicit per-task repository-write authorization, it must not clone/fetch/pull/push, change remotes/credentials, create repository worktrees, or mutate repository contents. Prefer headless/background execution and avoid foreground UI.
- **heaven2** is the primary development machine. Use it for repository development or Windows/MHW validation only when justified and preserve responsiveness.

This wave performed repository edits only on heaven2. Heaven was used only for read-only controller-state inspection.
## Why this branch exists

The older control-panel PR established a useful prototype but an independent safety audit identified high-impact lifecycle hazards: unsafe lease release after failed termination, PID reuse after controller restart, unproven completion after restart, non-loopback unauthenticated exposure, non-atomic capacity, and corrupt-state fail-open behavior.

The newer v2 branch already contained the stronger control-plane foundation: loopback-only startup, backup/degraded state recovery, restart uncertainty, serialized dispatch, lease/heartbeat lifecycle, process-termination proof, prompt provenance, workflow planning, task graph, release evidence, search, improvements, machine policy, and takeover state.

A separate interrupted local v3 worktree contained valuable command-center UX and CLI ideas, but it was based on older process/state code. This wave salvaged only the useful UI/CLI concepts and wired them to the newer safety-hardened backend rather than reviving stale server logic.

## Dependency blockers discovered during parallel review

An independent Support 1 verification of exact PR #59 head `41d377d1c6ce5b0b0f747aa37484eaadc9033ce5` found two P1 and two P2 backend defects. This dependent command-center patch does **not** claim to close them and must not be treated as merge-ready until the owning #59 implementation lane repairs and re-verifies them.

Confirmed P1 blockers:

- child-exit handling can load the whole registry, await Git evidence, then save the stale snapshot and overwrite a newer task/agent/lease/settings mutation;
- a worker already marked `stopping` can be classified `done` on exit code 0 and satisfy integration eligibility.

P2 follow-ups:

- failures after worktree creation but before durable agent registration can strand a reserved task/active lease with no agent owner;
- counted direct deploy can partially create workers and then return only an opaque request failure when a later item hits capacity.

The owning #59 lane should fix these with deterministic regressions before integration. This branch owns command-center/CLI product work only and should be rebased or cherry-picked onto the exact repaired #59 head after that safety review closes.

## Implemented in this wave

The command center now starts useful without an objective. The home surface shows real current mission, prioritized actions, swarm, task graph, integration queue, machines, system health, release gate, notifications, and recent activity.

One-click workflow presets are first-class. `usual-swarm` plans only missing useful roles toward:

1 Manager → 1 Primary Programmer → 4 independent support lanes.

Support lanes are task-sensitive and non-duplicative. Updater work currently produces architecture, tests/regression, adversarial/failure analysis, and Windows/packaging verification lanes.

The natural-language command bar resolves common operator language before dispatch. Recommendation commands such as `check everyone` and `what should I do next?` refresh state without spawning work. Unknown commands are staged for explicit bounded deployment instead of being executed implicitly.
Additional real operator surfaces now include:

- exact prompt inspection with template/version/hash provenance;
- replacement/takeover generation with persisted evidence;
- autonomy profiles with explicit permissions;
- pause/resume, read-only, drain, preserve-and-stop, and Emergency Stop;
- machine-policy-aware remote sync controls;
- explicit review/integration queue;
- evidence-only release gate;
- global search across tasks, agents, events, improvements, commit subjects, and continuity text;
- feature/improvement proposal inbox with governed self-improvement startup;
- expanded `agentctl.mjs` parity for workflows, command resolution, release, search, improvements, prompt/takeover, autonomy, drain, stop-swarm, and emergency stop;
- command/workflow preview-by-default semantics in the CLI.

The browser and CLI are clients of the same local HTTP API. No production-path status, progress, release readiness, or machine availability is fabricated.

## Files changed in this wave

- `tools/agent-control/public/index.html`
- `tools/agent-control/agentctl.mjs`
- `tools/agent-control/lib/control-core.mjs`
- `tools/agent-control/test/control-core.test.mjs`
- `tools/agent-control/test/ui-contract.test.mjs` (new)
- `tools/agent-control/package.json`
- `tools/agent-control/README.md`
- `tools/agent-control/CONTROL_PLANE.md`
- this continuity record

Agent Control package version was advanced to `0.4.0`.
## Verification evidence

The final pre-commit verification on heaven2 ran from `tools/agent-control`:

`npm run check && npm test`

Result:

- syntax checks: passed
- Node tests: **16 passed / 0 failed**
- no skipped or cancelled tests

Covered contracts include prompt provenance, state migration, usual-swarm deduplication, support-current blocking, distinct updater lanes, natural-language recommendation resolution, heaven repository-write fail-closed policy, integration eligibility, recommendation priority, takeover state, non-loopback refusal, corrupt-state degraded recovery, backup recovery without false completion, command-center inline JavaScript parsing, required command-center surfaces, and current safe API routes.

`git diff --check` also passed.

An isolated real server was started on heaven2 at loopback port 17331 using temporary state and this worktree. Smoke verification proved:

- the shipped command-center page served successfully;
- Current Mission, Release Gate, and Global Search were present;
- snapshot mission resolved to `complete verified automatic updater; see AUTO_UPDATER_NEXT_AGENT.md` with no objective entered;
- Heaven appeared as a non-connected background-worker policy target;
- Heaven2 appeared online as the primary-development policy target;
- `usual-swarm` preview returned exactly Manager + Primary Programmer + architecture/tests/adversarial/windows support lanes;
- `check everyone` resolved to a high-confidence recommendation-only action with no deployment plan;
- `agentctl recommend`, workflow preview, and command resolution worked against the live isolated service.

The isolated smoke server and its generated temporary state were stopped/removed before commit.
## Known limitations / intentionally unclaimed behavior

This wave does **not** claim the full product vision is complete.

Still requiring separate architecture, implementation, review, and evidence:

- authenticated LAN/phone remote control;
- a true remote worker daemon and multi-machine scheduler (current machine registry/policy is real, but cross-machine execution is not fabricated);
- transactional Agent Manager self-update with staged install, migration backup, startup health check, and automatic rollback;
- self-installation of generated improvements;
- model/provider routing by cost/complexity/tool availability;
- token/API cost accounting;
- automatic repeated-workflow detection/productization;
- richer notification delivery outside the local UI;
- artifact/worktree retention/reaping policy;
- end-to-end release publication automation, which must remain repository-policy gated;
- authoritative MHW/Windows/package tests for features whose evidence requires the real product environment.

The current self-improvement workflow creates governed engineering work and upgrade candidates; it does not rewrite/install the live controller without a recovery path.

## Recommended next implementation wave

Prioritize **transactional Agent Manager self-update + rollback** as its own isolated boundary, because the user explicitly wants the control plane to improve and update itself safely. It should reuse the repository's updater doctrine: immutable artifact provenance, staged replacement, state/database migration backup, running-worker handoff/drain, post-update health probe, and automatic rollback.

In parallel only if ownership is non-overlapping, define an authenticated remote-worker protocol so Heaven can become a real background execution node without granting it canonical repository-write authority.

Before any new implementation, re-read canonical GitHub state, open PRs, active ownership, this file, and the repository's recursive continuity instructions. Preserve this constitution in the next handoff.
