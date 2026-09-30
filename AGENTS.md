# Agent Instructions

This repository is the canonical working state for MHW Manual Mod Manager and the user's cross-repository programming-agent training baseline: `fengie/mhw-mods`, branch `main`.

## Authority and truth

Higher-priority platform/safety instructions and the current user's explicit request outrank repository guidance. Repository files define engineering workflow and current project facts; fresh repository/runtime evidence outranks stale hashes, branch names, status claims, or chat memory.

Generic training belongs under `_AGENT_TRAINING/`. MHW-specific state, hazards, and continuity belong under `_AGENT_CONTEXT/`. Do not copy the same rule into both layers unless a small entry-point reminder prevents a realistic mistake.

## Compact bootstrap

Before task-specific reasoning, answering, planning, dispatch or action, every agent and recurring worker must refresh current MHW main and complete this bootstrap for every repository; then load target-repository rules:

1. Refresh canonical state: exact `origin/main`, assigned head/base, relevant diff/history, worktree status when available, and active ownership/PRs/leases that could collide.
2. Read the current core manifest in full: this file, `_AGENT_TRAINING/README.md`, `_AGENT_TRAINING/AGENT_OPERATING_STANDARD.md`, and `_AGENT_CONTEXT/CURRENT_REVISION.json`.
3. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full at startup using hash-verified pagination. Then use the hash-verified indexed manifest/search/pagination tooling for only the task-relevant sections of `NEXT-AGENT-START-HERE.md`, `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, `BUG_PRECEDENTS.md`, `LEARNED_RULES.md`, architecture, verification, policy, and other large context files. Do not reread historical ledgers end to end by default.
4. Inspect the source, tests, callers, contracts, and architecture around the boundary you will change. Discover/load task-relevant plugins or skills when they materially improve execution; do not enumerate unrelated capabilities as ceremony.
5. Recheck canonical state and ownership immediately before integration or destructive mutation.

Truncation, one failed tool/network route, or a missing preferred CLI is a routing problem, not proof that the task is blocked. Use another authorized route when practical. Report a blocker only with exact evidence after reasonable alternatives are exhausted.

## Execution contract

Default engineering flow:

**inspect → understand → choose the smallest coherent task → implement → test → verify → integrate → document → hand off**

Planning is preparation, not the deliverable, unless the user explicitly requests analysis/review only. Once the acceptance path is clear, implement rather than producing another audit or plan.

While changing code:

- preserve separation of concerns and existing ownership boundaries;
- solve the root cause with the smallest coherent change;
- avoid speculative features, premature abstractions, compatibility junk, and unrelated cleanup;
- validate assumptions against actual code and authoritative contracts;
- reproduce a defect before fixing it when practical;
- make failure states explicit and fail closed at destructive/security boundaries;
- remove dead code when safe instead of layering permanent exceptions around it;
- keep comments for intent, invariants, or non-obvious tradeoffs—not narration of obvious code;
- treat warnings, flaky tests, ignored failures, and unexplained state as engineering signals.

Detailed universal practice lives in `_AGENT_TRAINING/AGENT_OPERATING_STANDARD.md`; risk-specific verification lives in `_AGENT_TRAINING/VERIFICATION_DOCTRINE.md`.

## Verification and evidence

Run the narrowest useful check first, then broaden only as the risk, changed boundary, repository gate, or new evidence justifies. Behavior changes require appropriate test coverage; escaped bugs normally require a regression that fails on the old behavior.

Evidence belongs to exact inputs. Never claim a test, build, merge, push, release, runtime state, or fix that was not observed. Historical green evidence does not transfer across changed source unless an explicit fingerprint/cache rule proves equivalence. Never weaken tests, analyzers, security controls, or verification gates merely to get green.

## Collaboration and senior-agent efficiency

Use one primary owner per mutable boundary. Delegate only work that is meaningfully independent, bounded, and cheaper to synthesize than to perform inline. Agents editing the same mutable surface must coordinate rather than race.

Senior/premium context is best spent on architecture, root-cause reasoning, difficult decisions, review, integration, and verification. Mechanical retrieval, repetitive scans, isolated research, and independent test work may be delegated when that actually saves context or wall time. Do not create agents merely to occupy roles.

Recurring workers get a bounded recovery attempt. If an unchanged blocker remains, preserve an exact checkpoint and route the next iteration to different actionable work until the blocker materially changes.

## Git, integration, and visible progress

`GLOBAL_GIT_DIRECTIVE.md` is the canonical Git/integration policy. In short: finished owned work belongs on verified remote `main`; temporary branches/PRs are tools, not completion states. Preserve unique concurrent work, never force-push shared/canonical history, and verify the intended tree survived integration.

Every meaningful integrated change set updates the root `README.md`, `CHANGELOG.md`, and patch component in `VERSION.txt` with synchronized version metadata. Evidence-only persistence for the same change set does not recursively bump the patch.

## Safety and repository-specific invariants

Load task-applicable `_AGENT_TRAINING/REPOSITORY_POLICY_REFERENCE.md` and BUG_PRECEDENTS/LEARNED_RULES entries before implementation. Full constitution startup reading remains mandatory. MHW's filesystem, transactional deployment, updater/release, plugin, security, process-ownership, dependency-resolution, UI-lifetime, and machine-routing invariants remain binding where relevant. Repository organization follows `_AGENT_TRAINING/REPOSITORY_STRUCTURE.md`.

## Continuity and completion

A completed task leaves one durable, current handoff that lets a fresh successor continue without private chat history. Record the exact revision/branch, what changed, verification actually run, integration status, unresolved risks or assumptions, and ordered next actions. Supersede stale current-state notes instead of stacking another active-looking snapshot above them.

Preserve the permanent continuity constitution in `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`; changing a Core Rule requires explicit user authorization.


Each successor must inherit and preserve the constitution and recursively propagate it to the agent after them.
Bug closure requires root cause, precedent, preventive rule/process change, regression coverage, sibling review, exact risk-matched verification and propagation before completion. Persist PLUGIN-PREFLIGHT evidence after current capability discovery; honor purpose-built routing and legitimate authorization boundaries. Never suggest or initiate Work/Codex handoff without explicit current-task user request.
