# Agent Instructions

This repository is the canonical working state for MHW Manual Mod Manager: fengie/mhw-mods, branch main.

## Mandatory pre-response repository training gate

Every agent and successor must complete a compact canonical bootstrap before task-specific reasoning or action. Repository truth overrides stale chat, summaries and old verification.

1. Establish exact current origin/main SHA, assigned base/head, git status, relevant history/diffs, branches/PRs, and live Agent Control ownership/leases when available. Never confuse a cached local ref with a refreshed remote.
2. Read AGENTS.md, _AGENT_TRAINING/README.md, _AGENT_CONTEXT/CURRENT_REVISION.json and _AGENT_CONTEXT/CONTINUITY_PROTOCOL.md in full; then read task-applicable _AGENT_CONTEXT/LEARNED_RULES.md entries. Managers additionally read _AGENT_TRAINING/PROMPT_TEMPLATES/01_MANAGER_ORCHESTRATOR.txt.
3. Use the hash-verified indexed manifest for NEXT-AGENT-START-HERE.md, _AGENT_CONTEXT/README_FIRST.md, CURRENT_STATE.md, NEXT_STEPS.md, VERIFICATION.md, BUG_PRECEDENTS.md, LEARNED_RULES.md, _AGENT_TRAINING/REPOSITORY_STRUCTURE.md, _AGENT_TRAINING/PROMPT_TEMPLATES/00_SWARM_RULES.txt and _AGENT_TRAINING/REPOSITORY_POLICY_REFERENCE.md. Expand relevant sections, source, tests and architecture; full historical rereads are unnecessary. Detailed policies in the reference remain binding and must be loaded before work in their domain.
4. Retrieve a bounded live packet with node tools/agent-control/agentctl.mjs bootstrap, or a local packet with node tools/agent-control/repository-context.mjs. Packets expire, identify source hashes and explicitly distinguish fresh remote evidence from local refs. Use the indexed SHA-256 with the context CLI to paginate safely; changed source hashes require regeneration.
5. Truncation means paginate/chunk. Missing gh, a failed network route or unavailable checkout means discover and try authorized GitHub connector/API, canonical heaven2/heaven worktree, Heaven Local Bridge, Agent Control or CI alternatives. TRAINING-BLOCKED/EXECUTION-BLOCKED requires exhausted reasonable authorized routes with exact evidence.
6. The controller must verify non-empty bounded sources, exact manifest hashes and assigned source identity before launch. Never promote a summary to verification or permission. Recheck canonical state and leases before mutation/integration.

No untrained agent answers first and catches up afterward. Delegation inherits these obligations. Premium reasoning belongs on diagnosis, architecture, review, integration and difficult decisions; delegate/offload mechanical work when useful. Do not select a model or change user model preferences without authorization.

## Mandatory task-review and plugin activation gate

Re-read the live task; identify actions, targets, machines, constraints and acceptance criteria. Discover the actual session capabilities, read relevant current skills and plugins/README.md plus plugins/PLUGIN_GAP_BACKLOG.md, honor user-named capabilities and select the narrowest applicable route. Persist PLUGIN-PREFLIGHT evidence before implementation. One failed lookup never proves absence. Proven outages require the existing local-replacement protocol, duplicate checks and a first concrete move in the natural owner; never bypass authentication, consent, quotas or safety controls. See the policy reference for enforcement details.

## Mandatory bug-prevention and precedent protocol

Before risky work, identify invariants, failure modes, state transitions, integration, restart/update/rollback boundaries and relevant BUG_PRECEDENTS entries. Reproduce defects when feasible. Closure requires bug -> root cause/violated invariant -> precedent log -> preventive rule/process change -> regression or deterministic verifier -> risk-matched exact evidence -> sibling checks -> propagation. Repeated escapes require stronger enforcement. Unit tests alone never establish integration/UI/runtime/process/update behavior. Do not weaken checks to get green.

### Mandatory visible-progress versioning

Every meaningful change set must update root README.md, increment the patch component of VERSION.txt, synchronize Directory.Build.props and continuity/release metadata, and update CHANGELOG.md in the same change set before completion. One integrated change set gets one patch increment; evidence-only attestation/publication of that same change set stays on its version. Independent code, configuration, tests, documentation or governance changes advance again.

### Mandatory completion handoff

Before completion/termination leave ordered exact next actions, improvement opportunities, unresolved risks/debt/verification gaps, branch/revision/artifact evidence and ownership/integration status in authoritative continuity. Supersede stale notes. The next agent must continue without previous chat history.

## Mandatory main integration rule

GLOBAL_GIT_DIRECTIVE.md remains mandatory. Fetch/prune and inventory branches, PRs and leases before branch creation; reuse a safe compatible unowned branch. One mutable boundary has one owner. Preserve unique work and unexplained dirty files. The implementing owner finishes verification, reconciliation with fresh main, exact-head review/checks, authorized integration/push, remote-main confirmation and safe temporary-branch cleanup. No force pushes, blind merges or default/protected/unique-work branch deletion. PR-open or side-branch-pushed is incomplete. Publish eligible user-facing releases immediately through the immutable private updater and public mirror, verify tag/assets/digests/provenance and the actual installed-client identity. Never cancel required canonical-main security verification. Read reference release/integration sections before these operations.

## Mandatory machine, process and safety boundaries

Recurring agents perform bounded recovery in the current iteration; if still blocked, preserve exact revision/branch/artifact/error/evidence and next live/manual action as DEFERRED-TO-LIVE. The next scheduled iteration must choose different actionable unowned work unless the user reassigns the task or durable evidence shows the blocker cleared. Managers prevent unchanged-blocker retry loops. Deferral never authorizes deletion, closure, merge, release or completion. This applies to every recurring role; detailed policy is in the indexed reference.

heaven2 is operator/control/credential authority; heaven is delegated compute. Use Heaven Local Bridge for heaven, with explicit target_host; operator UI defaults to heaven2. Never use RDC without current-task authorization. A failed control path does not prove host absence. Bridge readiness requires current canonical worker, independent watchdog, separate SYSTEM sentinel and local recovery evidence. Read machine/recovery policies before deployment.

Every agent-owned cmd/PowerShell/process helper runs hidden/background by default: forced windowsHide, CREATE_NO_WINDOW or -WindowStyle Hidden. A visible shell requires explicit user request; requested GUI applications may be visible. Process ownership must be proven before replacement/termination. Capacity and runner-allocation failures route to compatible authorized paths with bounded retries, never retry storms. Lost streams require durable-work reconciliation before replacement, never blind full-task restart.

Never suggest or initiate Work/Codex handoffs unless the user explicitly requests that surface for the current task. Continue in the current chat using authorized capabilities. Do not relay or print credentials. Preserve least-privilege CI, full-SHA action pinning, same-repository secretless self-hosted PR guards, independently verified privileged tooling and dependency auditing. Signed/HMAC authorization boundaries remain fail-closed.

Preserve transactional deployment/journals, CAS integrity, rollback, filesystem/reparse/TOCTOU protections, native replacement failure semantics, immutable source libraries, deterministic planning, SQLite atomicity and UI lifetime/cancellation. Mod grouping, precedence and dependency satisfaction are separate proofs: family/priority never alone authorizes overwrites, atomic structural siblings cannot mix unrelated providers, and deployment/launch fail closed without proven requirements and a unique winner. Development validation processes must be tracked/closed and never masquerade as the installed client. Load applicable reference and precedents before modifying these domains.

## Repository organization and plugin lifecycle

Follow _AGENT_TRAINING/REPOSITORY_STRUCTURE.md: specific canonical domain homes, meaningful subfolders, root entrypoints only, moves update every consumer and verify old-path removal. New plugins belong under plugins/<owner>, shared code under plugins/_shared and tooling under plugins/_tooling; preserve the existing heaven-bridge compatibility boundary, avoid duplicate implementations. Verify replacement plugin versions and use plugins/_tooling/prune_outdated_plugins.py --apply to safely remove strictly older same-identity installed copies while preserving source/auth/config/ambiguous versions. Read reference lifecycle policies first.

## Permanent recursive continuity

Read and preserve _AGENT_CONTEXT/CONTINUITY_PROTOCOL.md and active Learned Rules. Core Rules can be weakened only with explicit user authorization. Every successor must inherit, preserve and recursively propagate this constitution to the agent after them. Keep durable checkpoints and verification tied to exact source/fingerprints. When resources become low, enter preservation mode. Do not break the chain.
