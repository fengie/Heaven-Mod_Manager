# Repository policy reference

The complete operating policies below remain binding. AGENTS.md is the compact entrypoint; load relevant sections of this reference before work in that domain. The startup gate in current AGENTS.md is authoritative. This preserves the v8.8.40 policy text and its detailed safeguards without forcing every agent to reread it end to end.

# Agent Instructions

## Recurring-agent blocked-task rotation — current canonical addition (15e23ef9)

Scheduled/recurring agents must not burn future iterations on a task that is already proven blocked.

- During the current iteration, the owning agent may perform the normal bounded recovery/fallback attempts required by repository policy.
- If the task is still blocked when that iteration ends, preserve an exact durable checkpoint/handoff and classify the task as DEFERRED-TO-LIVE (or an equivalent explicit state) for the user's live/manual agents.
- On the next scheduled iteration, that recurring lane must not select or retry the same blocked task as its primary work unless the user explicitly reassigns it or durable evidence shows the blocker has materially cleared.
- The next scheduled iteration must choose a different actionable, unowned task and make concrete progress there. Re-reading or re-reporting the same unchanged blocker is not useful work.
- Deferral is preservation, not abandonment: do not delete, close, merge, release, or mark the blocked task complete merely because it was rotated out. Preserve exact revision/branch/artifact/error/evidence and the next manual/live action.
- Managers must prevent scheduled lanes from cycling indefinitely on the same blocker and should reserve deferred tasks for live/manual agents unless a real unblock signal appears.

This rule applies to managers, programmers, recovery workers, integration workers, QA/release workers, janitors, and any future recurring automation.

This repository is the canonical working state for MHW Manual Mod Manager.

## Mandatory pre-response repository training gate

Every agent, sub-agent, manager, reviewer, integration worker, recovery worker, automation, and successor that handles this repository must complete a **compact canonical bootstrap** before task-specific reasoning or action. This remains a hard startup gate, but startup success is defined by proving current repository truth efficiently—not by rereading every historical byte through one tool.

Before the first task-facing response or action, the agent must:

1. Establish exact canonical repository truth: current `origin/main` SHA, assigned branch/base, worktree status when a checkout is available, recent relevant history, open/relevant PRs and branches, and live Agent Control ownership/leases when available.
2. Read the **core startup set** in full: `AGENTS.md`, `_AGENT_TRAINING/README.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, and `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`; then read the task-applicable entries from `_AGENT_CONTEXT/LEARNED_RULES.md` identified by the indexed manifest/search. The Learned Rules ledger remains hash-indexed and does not require an end-to-end reread unless the task needs it. Managers must also read `_AGENT_TRAINING/PROMPT_TEMPLATES/01_MANAGER_ORCHESTRATOR.txt`.
3. Treat the controller's hash-verified **indexed context manifest** as provenance for larger continuity/training sources such as `NEXT-AGENT-START-HERE.md`, `_AGENT_TRAINING/PROMPT_TEMPLATES/00_SWARM_RULES.txt`, `_AGENT_CONTEXT/README_FIRST.md`, `CURRENT_STATE.md`, `NEXT_STEPS.md`, `VERIFICATION.md`, `BUG_PRECEDENTS.md`, `LEARNED_RULES.md`, and repository-structure guidance. Do **not** reread those files end-to-end by default.
4. Expand indexed context selectively: search/read the newest and task-relevant sections, applicable bug precedents/learned rules, relevant source/tests/architecture, and any continuity entry needed to resolve ownership, verification, or risk. Before risky or bug-fix work, materially relevant precedent/rule entries remain mandatory.
5. Treat output truncation as a pagination/chunking condition, not a blocker. Read the remaining ranges or use search/find. Treat a missing preferred CLI (including `gh`), one failed network route, or one unavailable local checkout as a routing condition: try authorized Git/GitHub connector/API, existing `heaven2`/`heaven` canonical worktrees, Heaven Local Bridge, Agent Control, or repository CI as applicable.
6. Report `TRAINING-BLOCKED` or `EXECUTION-BLOCKED` only after reasonable authorized fallback routes are exhausted. The report must list attempted routes and exact evidence. A truncated response, missing `gh`, or absence of a local checkout by itself is never sufficient.
7. Optimize scarce senior/premium capacity for architecture, root-cause synthesis, review, integration, and high-risk decisions. Mechanical context collection, pagination, branch inventory, log extraction, and repetitive verification should be delegated/offloaded when possible and returned as compact evidence packets.

Repository state remains authoritative over stale chat context, old SHAs, filenames, summaries, or prior-agent prose. The bootstrap/index split never permits guessing from summaries: targeted source expansion is required whenever the compact evidence is insufficient for the task.

Agent Control must enforce this mechanically for spawned workers: core and indexed sources must exist and be non-empty, exact hashes must be included in generated manifests, the compact bootstrap gate must appear before the user/manager task, and generated prompts must explicitly encode pagination and fallback routing. Every successor and sub-agent inherits the same gate. **No untrained agent gets to answer first and “catch up” afterward.**

## Non-interruptive agent shell execution

On both `heaven2` and `heaven`, every agent-spawned Command Prompt, Windows PowerShell, or PowerShell Core process must run hidden/background by default and must not steal keyboard focus or open a visible console window over the user's desktop.

- Use the repository's shared background/hidden process helpers and Heaven Bridge structured execution/session actions.
- Do not request `CREATE_NEW_CONSOLE`, `visible_console=true`, or equivalent for `cmd.exe`, `powershell.exe`, or `pwsh.exe`.
- GUI applications may still be launched visibly when the task actually requires a GUI. A human-visible shell window is allowed only when the user explicitly asks for that foreground shell for the current task.
- New process-launch paths must include regression coverage proving shell visibility cannot be accidentally re-enabled by an option override.

## Mandatory bug-prevention and precedent protocol

Bugs are prevention failures, not routine cleanup. Every agent must optimize for preventing defects from escaping into canonical `main`, releases, updater paths, or user-visible behavior.

### Prevention-first execution
- Before changing a risky or user-facing path, identify its invariants, failure modes, state transitions, integration boundaries, restart/update/rollback behavior, and likely regression surface. Do not rely on the happy path alone.
- Read relevant entries in `_AGENT_CONTEXT/BUG_PRECEDENTS.md` before implementation or review. Existing precedents are binding engineering constraints for materially similar work.
- Reproduce a reported defect before fixing it when feasible. Convert the reproduction into an automated regression test or durable verifier whenever technically practical.
- A fix is incomplete if it only patches the observed symptom. Identify the root cause and the missed invariant, contract, test, review step, or process control that allowed the bug to escape.
- Verification must match the risk. Unit tests alone are insufficient for bugs involving integration, startup, updater/install flows, persistence, concurrency, process lifetime, UI state, machine routing, branch/integration state, or real user-visible behavior.
- For operator-facing behavior, verify the actual user path on the appropriate machine/environment when available; do not substitute an internal API/unit assertion for an observable UI/runtime claim.
- Review changed code for adjacent instances of the same defect class. Fix or explicitly rule out sibling cases before closure.
- For mod conflict/dependency work, treat grouping, precedence, and requirement satisfaction as separate proofs: family membership or configured priority alone never authorizes an overwrite; atomic MHW structural siblings must not mix unrelated providers; and normal deployment/launch paths must fail closed when dependencies or a unique resolver winner are not proven.

### Mandatory bug-to-precedent closure
Whenever any bug, regression, escaped defect, false completion claim, broken integration, or process failure is discovered, the owning agent must complete this chain before marking the work done:

**bug → root cause → precedent log → guideline/process change → regression coverage → verification evidence → propagation**

The agent must immediately add or update an entry in `_AGENT_CONTEXT/BUG_PRECEDENTS.md` containing, at minimum:
1. date and affected subsystem;
2. user-visible or engineering symptom;
3. root cause;
4. the invariant/assumption that was violated;
5. why existing tests/review/process failed to catch it;
6. the direct corrective fix;
7. the preventive rule or process change;
8. regression tests/verifiers added or strengthened;
9. exact verification evidence and environment;
10. adjacent/sibling cases checked;
11. links/SHAs/PRs/issues when available.

If the incident reveals a reusable lesson, also update `_AGENT_CONTEXT/LEARNED_RULES.md`, `AGENTS.md`, training templates, verifier scripts, Agent Control enforcement, or other durable governance as appropriate. Process bugs require process fixes, not just product-code fixes.

### Completion and review gate
- `DONE`, `FIXED`, `SHIPPED`, merge, and release claims are forbidden while the required precedent entry, preventive change, regression coverage, or verification evidence is missing.
- Managers/reviewers/integration agents must reject or redispatch work that fixes a bug without completing the prevention chain.
- When a regression test cannot reasonably be automated, record why and add the strongest durable deterministic verification available.
- If the preventive rule can be mechanically enforced, prefer enforcement in code/tests/CI/Agent Control over prose alone.
- Repeated occurrence of an already logged defect class is a severity escalation: inspect why the prior prevention control failed and strengthen that control before closing the new incident.

### Mandatory visible-progress versioning
Every completed meaningful change set must make project progression visible **before** any `DONE`, `FIXED`, `SHIPPED`, merge-complete, handoff-complete, or release-complete claim.

- Update the root `README.md` in the same change set with a concise, versioned summary of what changed and why it matters. Keep the newest patch summary near the top so the user can gauge progression at a glance.
- Increment the patch component in `VERSION.txt` for every completed meaningful change set and update every canonical version identity that must match it, including `Directory.Build.props` and continuity/release metadata.
- Update `CHANGELOG.md` with the detailed change list for that patch. README is the fast progress view; CHANGELOG is the detailed historical record.
- One integrated meaningful change set gets one patch increment. Verification/evidence-only persistence, generated verification caches, immutable release publication/mirroring, or other attestations that only record proof for that exact change set remain on the same patch. If such a follow-up independently changes source, configuration, tests, documentation, governance, automation, UX, or behavior, it is a new meaningful change set and must advance the patch again.
- Managers, reviewers, integration agents, and successors must reject or repair completion claims when the README progress entry, patch bump, changelog entry, or required version synchronization is missing.

### Mandatory completion handoff
Every agent that finishes a task must leave a durable continuation note **before** claiming `DONE`, `FIXED`, `SHIPPED`, handing off, or terminating the lane.

The completion handoff must include:
- the exact next step(s), ordered and written so a successor can act without reconstructing private chat history;
- concrete improvement opportunities discovered during the task (quality, performance, UX, architecture, tests, verification, docs, automation, maintainability, or workflow). If no worthwhile improvement is known, say so explicitly and state what was considered;
- unresolved risks, assumptions, technical debt, blockers, and verification gaps;
- the current branch/revision/commit and the relevant artifacts, tests, commands, or evidence needed to continue safely;
- ownership/integration status when another agent, branch, PR, machine, or external dependency is involved.

Write these notes into the repository's authoritative continuity/handoff surfaces for the task (for example `_AGENT_CONTEXT/NEXT_STEPS.md`, current-state/task handoff records, or the relevant subsystem handoff), updating or superseding stale notes instead of creating contradictory duplicates.

Managers, reviewers, recovery agents, and integration agents must reject or repair a completion claim that lacks actionable next-step notes and improvement opportunities. A task can be functionally complete while still having useful follow-on work; record that work instead of discarding it.

## Mandatory plugin version replacement lifecycle

Local/custom plugin upgrades must not leave stale installed versions behind. When a newer version of an existing plugin identity is installed:

- verify the replacement version first using the owning plugin's required checks/runtime smoke;
- in the same execution cycle, remove strictly older installed/runtime copies of that same manifest identity;
- use `plugins/_tooling/prune_outdated_plugins.py --apply` (or the owning updater's equivalent safe replacement primitive) rather than ad-hoc broad deletion;
- preserve canonical Git source/history, user configuration, OAuth/auth state, secrets, and unrelated plugins;
- never delete a non-SemVer or ambiguously identified copy automatically; report it for explicit reconciliation instead;
- keep the `MHW Plugin Version Pruner` startup/maintenance task installed on both `heaven2` and `heaven` as a safety net for stale local copies.

Managers/reviewers must treat a completed plugin upgrade that leaves a proven older installed copy as incomplete work.

## Mandatory task-review and plugin activation gate

Repository training is necessary but not sufficient. **After training and before any task-facing plan, answer, dispatch, tool choice, or task-specific action**, every agent must perform a task/plugin preflight.

1. Re-read the complete current task and extract its required actions, targets, machines, artifacts, constraints, explicitly named tools/plugins, and acceptance/verification obligations. Do not route from a stale summary when the live task text is available.
2. Discover the **actual plugin/tool surface available in the current session/runtime**. Enumerate or search installed plugins, connectors, skills, repository toolbox capabilities, and task-specific integrations before concluding that a capability is unavailable. A remembered limitation, a previous chat's tool list, or one failed lookup is not proof of absence.
3. Map the task to every materially relevant purpose-built capability. For each selected plugin/capability, load/read its current skill, manifest, README, or operating instructions **before using it**. When the platform supports explicit activation/selection of an already-authorized plugin, activate/select it before generic fallbacks. Never bypass a required user authorization/consent boundary.
4. If the user explicitly names a plugin/capability for the task, treat that as a routing requirement unless it is proven unavailable, unsafe, or incapable of the required operation. Do not silently substitute a broad shell/browser/desktop/API path.
5. Record a concise `PLUGIN-PREFLIGHT` evidence block in agent state/handoff containing: task summary; capabilities considered; plugin(s)/skill(s) loaded or activated; selected execution route; unavailable/inapplicable capabilities and evidence; and any fallback reason. Do not claim `plugin unavailable` without positive discovery evidence from the current runtime.
6. If the correct reusable capability is missing or incomplete, use the safest authorized fallback only after the discovery step, and follow the mandatory plugin-gap capture rule. A missing connector on one chat/tool surface is a routing/discovery problem first, not permission to ignore the toolbox.
7. Reviewers and integration agents must treat a missing or false plugin preflight as a process defect. They should stop task-specific execution/merge when the omission could have changed routing, safety, machine selection, or verification.

For this repository, **plugin activation** means proving that the agent discovered the relevant capability, loaded/read its current operating instructions, confirmed that it covers the requested operation and permission boundary, and selected it as the execution path when applicable. It never means bypassing OAuth, account permissions, user consent, repository protection, or platform safety controls.

Managers must enforce this mechanically in every dispatched task: preserve the user's actual task text or an exact scoped assignment, include this gate before the implementation prompt, require `PLUGIN-PREFLIGHT` evidence from the worker, and reject/redispatch workers that skipped discovery or claimed a plugin was unavailable without current-runtime evidence.

## Always collaboration-ready

Every prompt and task is **collaboration-ready by default**. Agents must remain prepared to work with other agents whenever collaboration can materially improve throughput, expertise, independent verification, recovery, or convergence.

- Before and during execution, assess whether another agent's existing work, specialization, review, or bounded parallel subtask would help.
- Be ready to delegate or hand off precisely scoped subtasks, consume another agent's artifacts/results, coordinate ownership, and integrate the combined result into one verified outcome.
- Discover relevant active/partial agent work before duplicating it. Prefer continuation, recovery, or integration of useful existing work over redundant reimplementation.
- Preserve the one-primary-owner rule for each mutable boundary. Collaboration does not authorize racing edits, duplicate branches, conflicting leases, or blind merges.
- A trivial task may remain single-agent when spawning or coordination would add no material value; **readiness to collaborate is mandatory, gratuitous swarm overhead is not**.
- Successors, reviewers, recovery agents, managers, and generated prompts inherit this rule recursively.

## Default execution semantics

Unless the user explicitly says **read only**, **review only**, **summarize only**, **audit only**, or otherwise forbids mutation/execution, treat operational instructions as execution assignments.

- Reading, planning, auditing, and explaining are prerequisites when useful; they are not substitutes for implementation.
- Use the available tools and permissions to make concrete progress immediately.
- If a preferred execution path is unavailable, try another supported path before declaring a blocker.
- Do not hand work back merely because one tool, machine, or mode is unavailable when another authorized path can complete the task.
- Preserve safety, ownership, verification, and repository policy while executing.
- If a genuine external gate prevents completion, report PARTIAL/BLOCKED with the exact attempted operation and evidence.


### Mandatory research-and-wraparound rule

A blocked or unavailable direct solution is **not** a stopping condition by itself. When the obvious path cannot satisfy the user's underlying requirement, agents must research and pursue an authorized alternate path instead of handing the limitation back to the user.

1. Prove the direct-path limitation with current evidence. Do not treat stale memory, one failed call, or an assumed platform limitation as proof.
2. Research the missing capability, constraint, protocol, API, tool/plugin surface, repository implementation, and relevant authoritative documentation needed to understand viable alternatives.
3. Enumerate materially viable authorized routes, including existing plugins/tools, local implementations, adapters/wrappers, alternate APIs, machine-local execution, repository automation, or a newly implemented compatibility layer.
4. Prefer the narrowest, safest, most maintainable workaround that still fulfills the **underlying user outcome**, not merely the literal failed mechanism.
5. When no direct integration exists but the requirement is technically achievable, build or extend a wrapper/adapter/bridge/local replacement rather than stopping at "unsupported." Reuse existing repository primitives and ownership boundaries before creating parallel implementations.
6. Verify the workaround end to end against the original acceptance criteria. A fallback is not successful merely because it runs; it must actually deliver the requested result.
7. Record durable knowledge when the workaround reveals a reusable capability, constraint, or failure mode, and promote general lessons into `_AGENT_TRAINING/`.

Wraparounds must **not** bypass authentication, authorization, user consent, safety controls, destructive-operation protections, repository governance, or other legitimate hard boundaries. A task may be declared genuinely blocked only after reasonable current research and authorized alternate paths have been exhausted or shown incapable, with exact evidence preserved.

## Mandatory no-Work-handoff execution mode

Normal Chat is the preferred interactive surface. ChatGPT Work is deny-by-default.

- Never invoke, suggest, request, or hand off to ChatGPT Work unless the user explicitly asks for Work mode for the current task. Permission is task-local and never carries forward to a later task, retry, reviewer, replacement, or sub-agent.
- If the current ChatGPT session can execute the task with available tools, execute it in the current chat. Do not ask the user to switch modes merely because the task is long, multi-step, involves code/files/computer use, or one tool failed.
- Agent Control must keep work moving on an authorized non-Work path when arbitrary normal-Chat spawning is unavailable. The direct local worker/Codex/Heaven Bridge/repository route may be used as a non-Work execution fallback instead of failing closed or handing work back.
- Work and Codex are distinct execution surfaces. `executionMode="work"` is external-only and must never be silently translated into Codex; ordinary chat/direct/Codex execution must never escalate into Work.
- A provider usage-limit/quota error is a routing signal. Preserve unfinished work and use another authorized non-Work path where possible; do not create retry storms on the same blocked provider.
- Managers, reviewers, recovery agents, plugins, and successors must preserve this rule recursively and include it in dispatched prompts.

## Heaven Local Bridge execution policy

For computer work on the device named `heaven`, use the repository-backed **Heaven Local Bridge** as the default execution path.

- Prefer Heaven Local Bridge for filesystem access, command execution, builds/tests, persistent processes, local agents, screenshots, and structured desktop actions.
- Do not silently fall back to Remote Desktop Commander. Use Remote Desktop Commander for `heaven` only when the user explicitly authorizes it in the current request.
- Treat a bridge failure as a bridge repair/recovery problem first.
- Bridge persistence is part of machine readiness: both `heaven2` and `heaven` must keep the canonical interactive worker, independent local watchdog, and separate SYSTEM-owned sentinel installed, elevated/current, and self-healing. The sentinel must use a different principal/startup failure domain and repair the interactive task definitions plus Startup fallback. A transient worker process without all recovery owners/local-heartbeat paths is not a healthy control channel. If this invariant fails, repair persistence before treating the host as available.
- `heaven` is the worker/execution machine; `heaven2` is the main/control and credential-authority machine.
- **Operator-surface invariant:** the user interacts with `heaven2`. Dashboards, control panels, Agent Control, browser/UI automation, screenshots intended for operator interaction, app/window/mouse/keyboard work, and other human-facing desktop operations default to `heaven2`.
- Treat `heaven` as a delegated resource/worker by default. Do not move control panels or routine user interaction there merely because builds/tests/agents execute there. Interactive control of `heaven` requires an explicit worker-desktop request or a genuinely worker-specific GUI validation.
- New Heaven Bridge jobs must set top-level `target_host` explicitly: `heaven2` for control/interactive work, `heaven` for delegated heavy execution. Missing `target_host` is legacy compatibility behavior only and defaults to `heaven`.
- Never put credentials, tokens, passwords, cookies, private keys, or recovery codes into bridge queue/result/status payloads.
- The `heaven-bridge` Git branch is transport state, not the canonical development branch. Completed source changes still integrate to `main` under the rule below. Bridge bootstrap/runtime deployment must source worker/watchdog code from a clean canonical-`main` source mirror, never from relay-branch drift; the relay checkout is only queue/status/results transport plus compatibility history.
- For build/test/code execution details, the canonical plugin source is `heaven-bridge/plugin/`, including the `heaven-code-execution` skill.
- **Non-interrupting console invariant:** on both `heaven2` and `heaven`, every agent-owned command shell, PowerShell host, terminal helper, build/test child, local-agent process, and recovery child must default to hidden/background/no-console execution and must not steal focus. Node launchers must route through the forced `windowsHide: true` wrapper; Python/Win32 process creation must use `CREATE_NO_WINDOW` for console children; PowerShell `Start-Process` paths for console hosts must use `-WindowStyle Hidden`; scheduled/shortcut PowerShell arguments must carry `-WindowStyle Hidden`. A visible console is allowed only when the user or task explicitly requires an interactive visible terminal; GUI applications intentionally requested by the user may remain visible.

### Capacity and runner circuit breakers

- A Codex/agent-dispatch response classified as `capacity-blocked`, quota-exhausted, or rate-limited is a routing signal, not a reason to keep retrying. Record the blocker once, respect any known reset time, and continue compatible work through Heaven Local Bridge `proc_run`, filesystem/process controls, or repository build/test capabilities.
- A GitHub-hosted Actions job that fails before allocation with evidence such as `runner_id=0` and `steps=[]` is infrastructure-blocked, not product-failed. Do not burn repeated hosted retries merely to reproduce the same allocation symptom.
- When a Windows build/test/E2E job can run equivalently on the verified Heaven self-hosted runner, prefer `runs-on: [self-hosted, Windows, X64, mhw-mods]` and make that result authoritative for repository progress.
- Use GitHub-hosted runners only when the task genuinely requires their clean image, operating system, or runner-scoped environment and no equivalent authorized Heaven path exists.
- Never report the repository as blocked solely because Codex capacity or GitHub-hosted allocation is unavailable while the Heaven execution path is healthy.



## Mandatory repository organization / library layout rule

The repository must remain an organized library. Every new file belongs in the most specific relevant folder, and closely related files must be grouped into meaningful subfolders instead of accumulating in the root or a broad catch-all directory.

- Follow `_AGENT_TRAINING/REPOSITORY_STRUCTURE.md` for canonical homes, folder/subfolder decisions, migration safety, and verification.
- Root-level additions require a real repository/toolchain/discoverability reason; convenience is not sufficient.
- Reuse or refine existing folder taxonomy before inventing overlapping categories.
- File moves must update every consumer and verify old-path removal, builds/tests/scripts/CI/packaging/launch paths as applicable.
- When another agent is already reorganizing the repository, coordinate with and extend that work rather than launching a conflicting bulk move.
- Reviewers/integrators must treat misplaced files, avoidable root clutter, duplicate folder concepts, and stale path references as integration defects.

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

### Mandatory plugin outage -> local replacement autostart

Whenever work reveals a reusable capability gap, preserve it durably. When a plugin, connector, or tool that is materially relevant to the current task is **proven unavailable in the current runtime**, escalate beyond ordinary gap capture: the same execution cycle must create/update the implementation plan **and start a local replacement project**.

Current-runtime proof is required. Missing/removed capability, usage/quota pause, provider outage, unsupported required operation, or a broken integration after the normal supported connection flow are valid triggers. One failed call, stale health cache, or remembered limitation is not enough.

Before the current engineering task is considered complete:

- record the outage and its task impact in the `PLUGIN-PREFLIGHT` evidence;
- search existing plugin packages, `heaven-bridge/`, active branches/PRs, and `plugins/PLUGIN_GAP_BACKLOG.md` to avoid duplicates;
- if the capability is already implemented locally, route future work to it and improve discovery/compatibility docs rather than creating a second implementation;
- if it is missing or materially incomplete, immediately create/update the durable backlog plan and create/claim the local project under `plugins/` (or extend the existing natural owner in place);
- a current-task outage item must not be left merely `PLANNED` when repository mutation is available: claim it, record owner/project path, and make the first concrete implementation move (project README/plan plus a machine-readable contract, first test, or adapter boundary);
- record the triggering use case, upstream capability surface, proposed local owner, capability/API shape, security boundary, dependencies/reuse, acceptance tests, priority, and status;
- follow `plugins/LOCAL_REPLACEMENT_PROTOCOL.md`;
- do not block the user's immediate task solely because the ideal plugin does not yet exist when a safe authorized fallback can complete the work;
- never design a local replacement to bypass provider quotas, OAuth/authentication, account permissions, anti-abuse controls, or other service-side authorization;
- do not leave plugin ideas or outage plans only in chat, agent memory, or a transient handoff.

Managers and future agents must treat outage-triggered replacement projects as active engineering work, not a someday backlog. Keep ownership/status current, reuse existing Heaven/bridge primitives, integrate completed capabilities into verified remote `main`, and close/supersede the backlog entry only with exact completion evidence.

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
- The canonical automation is `.github/workflows/windows-release-gate.yml`: publish the immutable private updater release with `scripts/release/Publish-UpdaterRelease.ps1`, then mirror it with `scripts/release/Publish-PublicUpdaterRelease.ps1`.
- A release-owning agent must not report **SHIPPED**, **DONE**, or release-complete until it verifies that the latest private and public updater releases have the same `updater-main-<build>` tag, expected ZIP + `update-manifest.json` assets, and matching server SHA-256 digests for both assets.
- The public release body/source provenance must identify the private source SHA/build. Public updater assets must remain downloadable without private-repository credentials.
- If private publication succeeds but public mirroring fails or is unconfigured, the release is **PARTIAL/BLOCKED**, not complete. Repair/retry the public mirror before closing the release task; do not silently leave clients on an older public feed.
- Never commit or print `MHW_PUBLIC_RELEASE_TOKEN` or other credential values. Only the configured secret reference belongs in workflow/repository text.
- `scripts/testing/Test-UpdaterReleasePolicy.ps1` must keep a regression assertion that the release workflow invokes both private publication and public mirroring in that order and wires the public-release secret.

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


## Control-path diagnosis rule

A missing bridge heartbeat, missing result, unavailable plugin surface, queued self-hosted job, `runner_id=0`, or failed remote-control action proves only that the **control path is unavailable or unhealthy**. It does **not** prove that the target computer is powered off, disconnected, or otherwise offline.

- Never report `heaven2` or `heaven` itself as offline solely from bridge/runner evidence.
- If the user is actively interacting from the target machine, treat host presence as established and diagnose the bridge, watchdog, runner, relay, or plugin surface separately.
- Use precise wording such as `heaven2 control path unavailable`, `bridge worker not publishing`, or `self-hosted runner unallocated` until host-level reachability is independently proven.
- Recovery work must continue against the failed control component; do not convert a control-channel failure into a host-availability conclusion.
- Read-only health/status surfaces must not trust a cached local relay snapshot for a negative availability claim. If local heartbeat evidence is missing or stale, refresh an authoritative source before degrading the status when that refresh is safe and bounded.
- Model **host presence** and **control-path health** as separate states. When transport health is bad and independent host presence is not established, report `presence-unknown` (or an equivalent neutral state), not `offline` / `not-connected`.
- A dirty, diverged, or stale relay checkout may block mutation/write readiness, but it must not by itself become evidence that the remote host is offline. Read-only presence checks and write-readiness checks require separate semantics and regression coverage.


## Stream/response failure reconciliation rule

A failed ChatGPT/agent response stream is **not** proof that the assigned work failed and is never, by itself, permission to restart the whole assignment.

- Treat a lost response/transport stream as **execution state unknown** and reconcile durable evidence first.
- Surface the lifecycle as: `STREAM LOST · CHECKING WORK` → exactly one of `WORK DETECTED · INCOMPLETE`, `WORK VERIFIED · COMPLETE`, or `NO DURABLE WORK DETECTED · RETRY`.
- Durable work includes, when applicable, commits/SHA divergence, changed files, dirty worktrees, artifacts, PR/branch evidence, test/verification records, or other externally persisted execution outputs.
- If durable work exists but completion is not proven, preserve it and resume/reconcile that exact work. **Do not restart from scratch and do not dispatch a competing full-task duplicate.**
- Mark work complete only from durable completion evidence (for example explicit completion verification, or integrated/merged-to-main evidence plus passing verification/acceptance evidence). A cheerful final message alone is not completion proof.
- Automatic replacement is allowed only when no durable work is detected, and it must remain bounded/backed off as Agent Control's retry policy requires.
- Managers/reviewers must apply this rule after UI/WebSocket/network/stream failures and use repository/CI/worker evidence as the source of truth.


## Operator validation / installed-client identity invariant

Development and validation launches on the operator machine must never be left looking like the user's installed MHW Mod Manager.

- Any agent, test, UI-validation task, or automation that launches `MHW Mod Manager.exe` from a repository checkout, build output, validation checkout, staging directory, or other non-installed path on `heaven2` must track that process and close it when validation is complete, unless the user explicitly asked to keep that exact validation instance open.
- Never use a still-running development/validation window as evidence that the updater-managed desktop installation has reached the same version. Treat process executable path, packaged `build-identity.json`, and installed executable version as the identity proof.
- After a user-facing updater release is publicly mirrored, operator-facing verification on `heaven2` must include launching the actual desktop shortcut (or other canonical user launcher) and verifying that the resulting process resolves to the updater-managed install and expected published build.
- The updater mutates a packaged install in place, so a version string embedded in the parent folder name may be stale. Do not infer the running version from the folder name or shortcut label.
- If a temporary validation instance and the installed client are both present, close the temporary instance before presenting or validating the installed client so the user cannot mistake one for the other.

## Security boundary invariants

Security-sensitive automation is fail-closed:

- Every external GitHub Action reference must be pinned to a full 40-character commit SHA. Keep readable version tags in comments, not as executable refs.
- A persistent self-hosted runner must never execute fork pull-request code. Any workflow with both `pull_request` and `runs-on: [self-hosted,...]` requires an explicit same-repository head guard. PR validation is secretless and read-only: no `secrets.*` references and no write token scopes.
- Never introduce `pull_request_target` for PR-controlled source execution without a separately reviewed isolated design.
- Keep `GITHUB_TOKEN` permissions explicit and least-privilege. New `contents: write` workflows require a documented mutation need and security review.
- Privileged release tooling must be immutable and independently verified before execution; do not trust arbitrary preinstalled tools or moving latest downloads.
- Do not suppress NuGet/dependency vulnerability auditing to obtain a green build.
- Run `scripts/testing/Test-CiSecurityPolicy.ps1` when changing workflow/security/release infrastructure; `scripts/release/Verify-Release.ps1` runs it as an authoritative preflight.
- Heaven Bridge execution authentication should use HMAC in hardened deployments. Secret key material stays machine-local and never enters Git queue/result/state files.
- Treat updater hashes as integrity checks, not a complete publisher-authenticity root. Preserve a path toward independently signed releases/metadata and key rotation/recovery.

## Required security verification must complete

Required security verification on canonical `main` must be allowed to finish. Do not manually cancel, auto-cancel, or classify a Security Supply Chain Gate run as disposable merely because a newer `main` push exists. Pull-request runs may use ordinary supersession cancellation, but canonical-main security verification is continuity evidence, not queue noise.

When security-relevant files change repeatedly, the acceptable end state is a **completed-successful security gate covering the final security-relevant head or a later descendant that contains it**. A chain of queued/cancelled runs is not verification. Integration/release/cleanup automation must not treat cancelled, skipped, queued, or never-allocated security checks as equivalent to success.


## Cross-repository bootstrap authority — preserved full contract (AGENTS.md)

## Global cross-repository training authority

This repository is the user's canonical cross-repository agent training source, not only the operating guide for MHW work.

Before any agent, sub-agent, manager, reviewer, recovery worker, scheduled worker, or automation performs repository-specific reasoning or work on **any repository**, it must first refresh the current `main` of `fengie/mhw-mods` and complete the current MHW bootstrap/training gate. At minimum, load the current `AGENTS.md`, `_AGENT_TRAINING/README.md`, and the applicable shared/role prompt rules; use the indexed/bootstrap tooling required by the live MHW instructions rather than relying on stale copies.

Only after that global bootstrap may the agent load the target repository's own instructions, state, architecture, and task context. Target-repository rules add repository-specific requirements; they do not silently replace or skip the MHW training baseline. Higher-priority platform/safety instructions and an explicit newer user instruction still take precedence.

Dispatchers and managers must propagate this requirement into every worker assignment, including work whose target repository is not `fengie/mhw-mods`. If the MHW training source cannot be reached, use the authorized fallback routes required by the live training policy; if those are exhausted, report `TRAINING-BLOCKED` instead of proceeding untrained.



## Cross-repository bootstrap authority — preserved full contract (_AGENT_TRAINING/README.md)

## Mandatory cross-repository bootstrap

This directory is the canonical training handbook for the user's agents across repositories. Before working on any target repository, every agent must first refresh the current `main` of `fengie/mhw-mods` and complete the live MHW bootstrap defined by `AGENTS.md` and this trainer, including any applicable shared/role prompt contract and indexed context required by the current instructions.

After that baseline is loaded, read and obey the target repository's local instructions and current state. Local repository rules are additional/specific operating context; they are not permission to skip the MHW training baseline. Managers, schedulers, and sub-agent dispatchers must carry this rule into every assignment. If the MHW source is temporarily unavailable, exhaust the authorized fallbacks defined by the live training policy and fail closed as `TRAINING-BLOCKED` rather than silently using stale training.




## Manager global bootstrap — preserved full contract

Before any target-repository reasoning, planning, dispatch, review, or mutation, refresh current `main` of `fengie/mhw-mods` and complete its live agent-training bootstrap (`AGENTS.md`, `_AGENT_TRAINING/README.md`, and the applicable shared/role training rules plus any live indexed context they require). This applies even when the assigned target is a different repository. Only after the MHW baseline is loaded may you load the target repository's own rules/state and begin target-specific work. Managers and successors must propagate this gate recursively. Do not substitute stale copied training for current MHW truth; exhaust authorized fallbacks and report `TRAINING-BLOCKED` if the canonical training source cannot be established.


## Manager canonical truth — preserved full contract

## Canonical truth
Before meaningful changes:
1. Fetch/refresh remote state when possible.
2. Identify exact current `origin/main` SHA.
3. Inspect branch, worktree status, recent history, relevant PRs/branches, and continuity files.
4. Read repository instructions (`AGENTS.md`, `NEXT-AGENT-START-HERE.md`, `_AGENT_CONTEXT/README_FIRST.md`, `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, `_AGENT_CONTEXT/CURRENT_STATE.md`, `_AGENT_CONTEXT/NEXT_STEPS.md`, `_AGENT_CONTEXT/VERIFICATION.md`, `_AGENT_CONTEXT/LEARNED_RULES.md`) when present.
5. Treat repository state as authoritative over stale chat text, old SHAs, or this prompt.
6. Re-check canonical state before every merge/rebase/cherry-pick and immediately before pushing `main`.
