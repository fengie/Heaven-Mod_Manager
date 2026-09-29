# Agent Instructions

This repository is the canonical working state for MHW Manual Mod Manager.

## Mandatory pre-response repository training gate

Every agent, sub-agent, manager, reviewer, integration worker, recovery worker, automation, and successor that handles this repository must complete repository training **before answering the task prompt or taking task-specific action**. This is a hard startup gate, not advisory guidance.

Before the first task-facing response or action, the agent must:

1. Establish exact canonical repository truth: current `origin/main` SHA, assigned branch/base, worktree status, recent relevant history, open/relevant PRs and branches, and live Agent Control ownership/leases when available.
2. Read `AGENTS.md`, `NEXT-AGENT-START-HERE.md`, `_AGENT_TRAINING/README.md`, `_AGENT_TRAINING/PROMPT_TEMPLATES/00_SWARM_RULES.txt`, `_AGENT_CONTEXT/README_FIRST.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, `_AGENT_CONTEXT/CURRENT_STATE.md`, `_AGENT_CONTEXT/NEXT_STEPS.md`, `_AGENT_CONTEXT/VERIFICATION.md`, and `_AGENT_CONTEXT/LEARNED_RULES.md`.
3. Managers must also read `_AGENT_TRAINING/PROMPT_TEMPLATES/01_MANAGER_ORCHESTRATOR.txt` before responding or dispatching work.
4. Inspect the task-relevant source, tests, architecture docs, and nearby implementation before forming an answer or plan.
5. Treat the repository itself as the source of truth. Stale chat context, old SHAs, filenames, summaries, or prior-agent prose do not satisfy this gate.

Do not output a task analysis, plan, status answer, implementation claim, or recommendation before the gate is complete. If mandatory training material cannot be read or canonical state cannot be established, report `TRAINING-BLOCKED` with exact evidence instead of answering from partial context.

Agent Control must enforce this mechanically for spawned workers: required training sources must exist and be non-empty before worker launch, their exact hashes must be included in the generated training manifest, and the training-gate section must appear before the user/manager task in the generated prompt.

Every successor and sub-agent inherits this same gate. **No untrained agent gets to answer first and “catch up” afterward.**


## Default execution semantics

Unless the user explicitly says **read only**, **review only**, **summarize only**, **audit only**, or otherwise forbids mutation/execution, treat operational instructions as execution assignments.

- Reading, planning, auditing, and explaining are prerequisites when useful; they are not substitutes for implementation.
- Use the available tools and permissions to make concrete progress immediately.
- If a preferred execution path is unavailable, try another supported path before declaring a blocker.
- Do not hand work back merely because one tool, machine, or mode is unavailable when another authorized path can complete the task.
- Preserve safety, ownership, verification, and repository policy while executing.
- If a genuine external gate prevents completion, report PARTIAL/BLOCKED with the exact attempted operation and evidence.

## Heaven Local Bridge execution policy

For computer work on the device named `heaven`, use the repository-backed **Heaven Local Bridge** as the default execution path.

- Prefer Heaven Local Bridge for filesystem access, command execution, builds/tests, persistent processes, local agents, screenshots, and structured desktop actions.
- Do not silently fall back to Remote Desktop Commander. Use Remote Desktop Commander for `heaven` only when the user explicitly authorizes it in the current request.
- Treat a bridge failure as a bridge repair/recovery problem first.
- `heaven` is the worker/execution machine; `heaven2` is the main/control and credential-authority machine.
- **Operator-surface invariant:** the user interacts with `heaven2`. Dashboards, control panels, Agent Control, browser/UI automation, screenshots intended for operator interaction, app/window/mouse/keyboard work, and other human-facing desktop operations default to `heaven2`.
- Treat `heaven` as a delegated resource/worker by default. Do not move control panels or routine user interaction there merely because builds/tests/agents execute there. Interactive control of `heaven` requires an explicit worker-desktop request or a genuinely worker-specific GUI validation.
- New Heaven Bridge jobs must set top-level `target_host` explicitly: `heaven2` for control/interactive work, `heaven` for delegated heavy execution. Missing `target_host` is legacy compatibility behavior only and defaults to `heaven`.
- Never put credentials, tokens, passwords, cookies, private keys, or recovery codes into bridge queue/result/status payloads.
- The `heaven-bridge` Git branch is transport state, not the canonical development branch. Completed source changes still integrate to `main` under the rule below.
- For build/test/code execution details, the canonical plugin source is `heaven-bridge/plugin/`, including the `heaven-code-execution` skill.

### Capacity and runner circuit breakers

- A Codex/agent-dispatch response classified as `capacity-blocked`, quota-exhausted, or rate-limited is a routing signal, not a reason to keep retrying. Record the blocker once, respect any known reset time, and continue compatible work through Heaven Local Bridge `proc_run`, filesystem/process controls, or repository build/test capabilities.
- A GitHub-hosted Actions job that fails before allocation with evidence such as `runner_id=0` and `steps=[]` is infrastructure-blocked, not product-failed. Do not burn repeated hosted retries merely to reproduce the same allocation symptom.
- When a Windows build/test/E2E job can run equivalently on the verified Heaven self-hosted runner, prefer `runs-on: [self-hosted, Windows, X64, mhw-mods]` and make that result authoritative for repository progress.
- Use GitHub-hosted runners only when the task genuinely requires their clean image, operating system, or runner-scoped environment and no equivalent authorized Heaven path exists.
- Never report the repository as blocked solely because Codex capacity or GitHub-hosted allocation is unavailable while the Heaven execution path is healthy.


## Canonical plugin workspace

The canonical repository home for new plugins is `plugins/`.

- New plugin source, manifests, plugin-specific tests, packaging, and plugin documentation must be created under `plugins/<plugin-name>/`.
- Shared plugin modules belong under `plugins/_shared/`; shared plugin build/package tooling belongs under `plugins/_tooling/`.
- Do not create new top-level plugin directories or scatter new plugin implementations under unrelated `tools/`, `scripts/`, or application-source folders.
- Read `plugins/README.md` before creating or restructuring plugin code.
- The existing `heaven-bridge/` tree predates this rule and remains an active compatibility/runtime boundary. New Heaven control-plane/plugin-platform implementation belongs under `plugins/heaven-control-plane/`; migrate existing bridge plugin source only as an explicit, fully verified compatibility migration.
- Never maintain two independent copies of the same plugin capability. Reuse, extract, adapt, or migrate existing bridge primitives.
- The current implementation swarm directive is `plugins/IMPLEMENTATION_SWARM_PROMPT.md`.


## Mandatory plugin-first toolbox routing

Treat the repository/plugin toolbox as the first routing layer for any task that can be handled by an existing reusable capability.

Before reaching for a generic shell command, ad-hoc script, manual browser/UI sequence, direct API workaround, or another broad fallback:

1. inspect the currently available/installed plugin and capability surface relevant to the task;
2. check `plugins/README.md` and `plugins/PLUGIN_GAP_BACKLOG.md` for canonical ownership, existing implementations, planned gaps, and known precedence;
3. choose the narrowest purpose-built plugin/capability that safely completes the task;
4. prefer a higher-level structured plugin/workflow over raw primitives when both are available and the higher-level path preserves required safety/evidence;
5. only use a broader fallback when the correct plugin is unavailable, unhealthy, missing the required operation, or materially less safe/reliable for the specific task.

For `heaven`, this means preferring the most specific implemented toolbox capability (for example a workflow/control-plane/desktop capability) and then Heaven Local Bridge structured actions before raw shell or manual GUI automation. Remote Desktop Commander remains forbidden unless the user explicitly authorizes it for the current request.

### Mandatory plugin-gap capture

Whenever work reveals a reusable capability that would make the same class of task safer, faster, more reliable, less manual, or more token-efficient, treat that as a plugin/toolbox gap.

Before the current engineering task is considered complete:

- search existing plugin packages, active branches/PRs, and `plugins/PLUGIN_GAP_BACKLOG.md` to avoid duplicates;
- if the capability is already implemented, route future work to that plugin and improve its discovery/docs if necessary;
- if it is missing or materially incomplete, immediately add or update a durable plan in `plugins/PLUGIN_GAP_BACKLOG.md` for a future agent;
- record the triggering use case, proposed owning plugin (or justification for a new plugin), capability/API shape, security boundary, dependencies, acceptance tests, priority, and status;
- do not block the user's immediate task solely because the ideal plugin does not yet exist when a safe authorized fallback can complete the work;
- do not leave plugin ideas only in chat, agent memory, or a transient handoff.

Managers and future agents must treat the backlog as actionable engineering work: claim compatible items when capacity exists, keep ownership/status current, and integrate completed capabilities into verified remote `main`.

## Mandatory immediate release-on-ready rule

A completed user-facing version must not sit unreleased. **Release-ready means release now.**

- As soon as a version is integrated into canonical `main` and every required release gate for that version passes, the release-owning agent must publish it **immediately in the same execution cycle**.
- Do **not** wait for a separate user prompt, reminder, scheduled release window, batching opportunity, or later agent when publication is already eligible, unless the user explicitly says to hold or delay that specific release.
- `DONE`, `SHIPPED`, `release-ready`, `release-complete`, or equivalent status is forbidden before immutable publication and all required post-publication verification are complete.
- Publication must include the private updater release and the required public mirror described below. Verify tags, assets, digests, and source provenance before closing the task.
- If publication is blocked after the release gates pass, immediately attempt the authorized repair/retry path. Report `PARTIAL/BLOCKED` with exact evidence only when an external blocker genuinely prevents publication; never silently leave a verified version queued and unreleased.
- Release automation, managers, and successor agents must treat an eligible completed version as an active release obligation, not optional follow-up work.

## Mandatory private-to-public updater release publication

The canonical source repository is private `fengie/mhw-mods`; the credential-free updater feed is public `fengie/mhw-mod-manager-release`.

- Every successful updater release published from the private repository must be mirrored to the public release repository as part of the same release operation.
- The canonical automation is `.github/workflows/windows-release-gate.yml`: publish the immutable private updater release with `scripts/Publish-UpdaterRelease.ps1`, then mirror it with `scripts/Publish-PublicUpdaterRelease.ps1`.
- A release-owning agent must not report **SHIPPED**, **DONE**, or release-complete until it verifies that the latest private and public updater releases have the same `updater-main-<build>` tag, expected ZIP + `update-manifest.json` assets, and matching server SHA-256 digests for both assets.
- The public release body/source provenance must identify the private source SHA/build. Public updater assets must remain downloadable without private-repository credentials.
- If private publication succeeds but public mirroring fails or is unconfigured, the release is **PARTIAL/BLOCKED**, not complete. Repair/retry the public mirror before closing the release task; do not silently leave clients on an older public feed.
- Never commit or print `MHW_PUBLIC_RELEASE_TOKEN` or other credential values. Only the configured secret reference belongs in workflow/repository text.
- `scripts/Test-UpdaterReleasePolicy.ps1` must keep a regression assertion that the release workflow invokes both private publication and public mirroring in that order and wires the public-release secret.

## Mandatory `main` integration rule

`GLOBAL_GIT_DIRECTIVE.md` is a mandatory repository-wide operating rule for every development agent, sub-agent, swarm, manager, integration agent, reviewer, and automation.

- `main` is the canonical integration branch.
- BEFORE creating any branch, fetch/prune and inventory local/remote branches, open PRs, and live Agent Control tasks/leases. Reuse a safe compatible unowned branch instead of creating a duplicate.
- One mutable scope gets one active implementation branch/lease. Retry/successor branches are forbidden unless the existing branch is actively owned, incompatible, protected/reserved, unsafe to reuse, or contains unrelated unique work; record the reason before creating a replacement.
- Task branches are temporary workspaces only.
- The agent that creates a completed change owns it through validation, synchronization with current `main`, conflict resolution, integration into `main`, pushing `main`, verification that the change is present on remote `main`, and cleanup of the completed temporary branch.
- After verified integration, delete the completed local task branch and delete its remote counterpart if one exists and is no longer needed. Branch cleanup is part of completion, not optional housekeeping.
- Never delete `main`, the current/default branch, protected or active release branches, or any branch containing unique/unmerged commits that are not safely present on canonical remote `main`.
- A task is not complete merely because code is written, committed, pushed to a side branch, or a PR exists.
- If remote `main` moves during integration, reconcile and retry; never abandon finished work on a side branch solely because of the race.
- Never knowingly break `main` just to merge quickly; validation remains mandatory.
- `GLOBAL_GIT_DIRECTIVE.md` overrides older workflow guidance that parks ordinary completed work on long-lived branches for a separate integration agent.

For Agent Manager / engineering-swarm work, `_AGENT_TRAINING/PROMPT_TEMPLATES/00_SWARM_RULES.txt` is the mandatory shared contract and `_AGENT_TRAINING/PROMPT_TEMPLATES/01_MANAGER_ORCHESTRATOR.txt` is the manager-specific convergence contract. Runtime-generated swarm prompts must remain aligned with them.

Before modifying code or durable project state:

1. Verify the actual canonical `main` HEAD; repository state overrides stale chat/prompt state.
2. Inspect `git status` and preserve unexplained local work.
3. Inspect recent relevant Git history/diffs.
4. Read `_AGENT_TRAINING/README.md` and the trainer sections relevant to the task.
5. Read `NEXT-AGENT-START-HERE.md`.
6. Read `_AGENT_CONTEXT/CURRENT_REVISION.json`.
7. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`.
8. Read active rules in `_AGENT_CONTEXT/LEARNED_RULES.md`.
9. Follow the remaining read order in `_AGENT_CONTEXT/README_FIRST.md`.
10. Preserve documented verification, transaction, filesystem, recovery, safety, and UI/lifetime invariants unless an explicitly scoped task justifies changing them.
11. Keep one independently verifiable architecture/source boundary at a time.
12. Update durable handoff/context as you work, not only at the end.
13. At meaningful checkpoints, ask whether new reusable engineering knowledge belongs in `_AGENT_TRAINING/`; update it when the lesson is generalized, meaningful, understood, non-duplicate, and operational.
14. Commit and push meaningful checkpoints; do not leave expensive discoveries only in chat or local state. During long-running agent/tool sessions, keep those checkpoints short-interval and small enough that a stream/session cancellation cannot erase a substantial block of finished work.
15. Run the handoff/verification checks appropriate to the change and report verification only for exact inputs actually checked.
15a. After the completed change is verified on remote `main`, remove its temporary local and remote task branches after confirming they contain no unique work and are not protected/current/default branches. Prune stale remote-tracking refs.
16. For every product/source update that will ship in the application or a release (feature, bug fix, UI/workflow change, behavior change, storage/schema change, deployment/update logic, or other shipped code change), treat release identity and user-facing documentation as part of the same change:
    - bump the app version in `VERSION.txt` and `Directory.Build.props` (`<Version>`), and keep `<InformationalVersion>` semantically aligned;
    - update any release/build/install/update manifests or metadata that duplicate the app version or release identity;
    - update `README.md` so the current version and relevant behavior/workflow are accurately documented;
    - update `CHANGELOG.md` with the same version and a concise summary of the shipped change;
    - do not declare the work complete, merge it, or hand it off as release-ready while code and version/docs disagree.
17. Documentation-only, agent-policy-only, continuity/evidence-only, or test-only changes that do not alter the shipped application are exempt from an app-version bump, but any documentation they make stale must still be corrected.
18. In every handoff/PR summary for a shipped app change, explicitly state the new app version and identify the README/CHANGELOG/version files updated. A missing version/docs update is unfinished work, not an optional cleanup.
19. Explicitly pass this continuity obligation to your successor, and require that successor to pass it to the agent after them.

GitHub `fengie/mhw-mods` on `main` is the source of truth. Source ZIPs are optional export/release artifacts, not canonical development state.

The next agent must be able to continue without previous chat history.

Every agent must preserve this rule system and require its successor to preserve and recursively propagate it again. Core continuity rules may be weakened only with explicit user authorization.

If context, execution time, tool access, or usage allowance becomes dangerously low, stop expanding scope and enter the preservation mode defined in the continuity protocol.

**Do not break the chain.**
