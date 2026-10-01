# Agent Instructions — MHW Manual Mod Manager

This repository, `fengie/mhw-mods@main`, is the canonical working state for the MHW Manual Mod Manager product only. The mandatory global programming-agent bootstrap, reusable engineering doctrine, shared Git policy, personal plugins, Heaven Bridge, Agent Control, and reusable operator/developer tools are owned by `fengie/heaven-toolbox@main`.

## Authority and truth

Higher-priority platform/safety instructions and the user's current explicit request outrank repository guidance. For MHW work, fresh MHW repository/runtime evidence defines product truth. Heaven Toolbox defines reusable global defaults; MHW defines only MHW-specific source, tests, `_AGENT_CONTEXT/`, product workflows/scripts, bugs, plans, release state, evidence, and continuity.

Do not recreate independent global training, plugin, bridge, Agent Control, or shared-tool copies in this repository. Reusable changes land in Heaven Toolbox first. MHW may keep only product-specific adapters when a real product dependency requires one.

## Mandatory Toolbox-first bootstrap

Before any task-specific reasoning, answering, planning, dispatch, or action, every agent and recurring worker must complete this bootstrap:

1. Refresh exact `fengie/heaven-toolbox@main`.
2. Read Toolbox `AGENTS.md`, `_AGENT_TRAINING/README.md`, `_AGENT_TRAINING/AGENT_OPERATING_STANDARD.md`, and `GLOBAL_GIT_DIRECTIVE.md`.
3. Load only additional Toolbox trainer/tool documents that are task-relevant.
4. Refresh exact `fengie/mhw-mods@main`, current ownership/PR/branch state, and the assigned worktree/head.
5. Read this file and `_AGENT_CONTEXT/CURRENT_REVISION.json`.
6. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full at startup, then use the MHW context index/router to retrieve only task-relevant `LEARNED_RULES.md`, `BUG_PRECEDENTS.md`, architecture, verification, policy, and other project context.
7. Immediately before integration or destructive mutation, refresh MHW canonical state and ownership again and preserve unique concurrent work.

When a MHW-specific rule and a generic Toolbox default differ, the more specific current MHW project rule applies unless it conflicts with higher-priority instructions or the user's current direction. A stale local cache of Toolbox is never authoritative over current Toolbox `main`.

## Execution contract

Default flow remains **inspect → understand → choose the smallest coherent task → implement → test → verify → integrate → document → hand off**.

Planning is not completion unless the user asked for analysis only. Reproduce defects before fixing them when practical, preserve separation of concerns, fail closed at destructive/security boundaries, and do not weaken tests or controls merely to get green.

Run the narrowest useful check first, then broaden according to risk and repository gates. Evidence attaches to exact source/artifacts; never claim a build, test, runtime check, merge, push, release, or fix that was not observed.

## MHW project invariants

MHW-specific safety, filesystem, transactional deployment, updater/release, process ownership, dependency resolution, UI lifetime, and machine-routing invariants live under `_AGENT_CONTEXT/` and product code/tests. The permanent continuity constitution is `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`; Core Rules require explicit user authorization to change.

The canonical active/recovery ledger is `_AGENT_CONTEXT/PROJECT_PLAN.md`. Branch cleanup means integrate or extract useful unique work before deleting a ref; archive-only preservation is not completion.

## Git and visible progress

Follow the current `fengie/heaven-toolbox@main:GLOBAL_GIT_DIRECTIVE.md`. Finished owned work belongs on verified remote `main`; temporary branches and PRs are coordination mechanisms, not completion states. Never force-push shared/canonical history, and verify that intended tree semantics survived integration.

Every meaningful integrated MHW product change set updates root `README.md`, `CHANGELOG.md`, and `VERSION.txt` with synchronized version metadata. Coordinate version bumps against fresh `main`.

The README is a current-state surface, not the historical release ledger. Its release section must show **at most three patch summaries**: the current version and the two immediately preceding patches. When a new patch lands, add it at the top and remove the oldest README patch entry instead of appending indefinitely; preserve complete history in `CHANGELOG.md` / GitHub Releases. Documentation-only cleanup that merely repairs README/version drift or enforces this retention policy does not consume a new product patch number by itself.

## Collaboration and continuity

Use one primary owner per mutable boundary. Senior context is best spent on architecture, difficult reasoning, review, integration, and verification; bounded mechanical work may be delegated when that is actually cheaper to synthesize.

A completed task must leave durable successor context that works without private chat history: exact revision, what changed, checks actually run, integration state, unresolved risks/assumptions, and ordered next actions.

Each successor must inherit and preserve `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, active Learned Rules, and the current project plan, and must propagate the same continuity obligation to the agent after them. Do not break the chain.
