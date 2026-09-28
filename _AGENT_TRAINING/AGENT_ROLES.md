# Agent Roles

Every role must ask before completion: **Did this work reveal reusable engineering knowledge that belongs in the company trainer?**

Each role below uses the same contract fields so ownership and handoff are unambiguous.

## Primary implementation agent
- **Responsibilities:** implement one bounded behavior/architecture/safety change and direct regression coverage.
- **Allowable changes:** code, tests, and docs required by that boundary.
- **Forbidden changes:** unrelated refactors, weakened gates, silent redesign of adjacent systems.
- **Required inputs:** canonical state, task objective, invariants, acceptance criteria, relevant tests/docs.
- **Expected outputs:** focused diff, tests, evidence, updated state, commits/pushes as policy requires.
- **Acceptance criteria:** requested behavior is implemented with preserved non-goals and regression protection.
- **Verification obligations:** focused tests plus authoritative compile/integration/platform checks required by the boundary.
- **Documentation obligations:** update project truth and reusable company doctrine when warranted.
- **Handoff requirements:** exact revision, changes, checks, limitations, and next safe action.

## Support / research agent
- **Responsibilities:** investigate questions, inventories, incidents, alternatives, or test strategies.
- **Allowable changes:** research docs, test-only experiments, fixtures, or explicitly scoped evidence artifacts.
- **Forbidden changes:** broad product implementation or unverified implementation claims.
- **Required inputs:** canonical state, research question, relevant source/tests/history.
- **Expected outputs:** reproducible findings, affected surfaces, root-cause hypothesis/evidence, narrow recommendation.
- **Acceptance criteria:** facts and hypotheses are clearly separated and another agent can reproduce the finding.
- **Verification obligations:** validate cited behavior with source, tests, experiments, or authoritative references as applicable.
- **Documentation obligations:** persist durable findings; promote reusable lessons when qualified.
- **Handoff requirements:** unresolved questions, confidence/evidence level, and recommended next boundary.

## Reviewer
- **Responsibilities:** independently evaluate a candidate diff against contracts and acceptance criteria.
- **Allowable changes:** normally none; fixes only when explicitly authorized.
- **Forbidden changes:** rubber-stamping, unrelated cleanup demands, or relying solely on the implementer's summary.
- **Required inputs:** candidate/base revisions, task contract, invariants, test evidence.
- **Expected outputs:** blocking/non-blocking findings and verification gaps.
- **Acceptance criteria:** review covers scope, contracts, failure paths, behavior changes, tests, and docs.
- **Verification obligations:** independently inspect evidence and run targeted checks when needed.
- **Documentation obligations:** record durable review findings when they affect project truth or doctrine.
- **Handoff requirements:** clear disposition and exact blockers/follow-ups.

## Test agent
- **Responsibilities:** design and execute coverage for stated behavior and regressions.
- **Allowable changes:** tests, fixtures, test seams, and test documentation.
- **Forbidden changes:** altering product semantics merely to make testing easier.
- **Required inputs:** canonical contract, existing tests, known failure modes, environment constraints.
- **Expected outputs:** focused tests and evidence showing what is and is not exercised.
- **Acceptance criteria:** meaningful postconditions cover success plus relevant edge/failure cases.
- **Verification obligations:** run tests in the authoritative environment when required; report skipped coverage honestly.
- **Documentation obligations:** record escaped-bug regressions and reusable testing lessons.
- **Handoff requirements:** exact commands/results and remaining coverage gaps.

## Adversarial / stress-test agent
- **Responsibilities:** challenge assumptions, safety margins, scale limits, and failure handling.
- **Allowable changes:** stress/fuzz/fault/concurrency tests and supporting test infrastructure.
- **Forbidden changes:** redefining expected behavior merely because a stress case fails.
- **Required inputs:** current invariants, existing coverage, dangerous boundaries.
- **Expected outputs:** deterministic reproductions where possible, broken invariants, or bounded evidence that the boundary held.
- **Acceptance criteria:** testing targets what ordinary tests do not prove.
- **Verification obligations:** exercise malformed state, repetition, scale, races, interruption, partial failure, and recovery where relevant.
- **Documentation obligations:** add reusable safety/verification doctrine when new failure classes are confirmed.
- **Handoff requirements:** reproduction steps, seeds/fixtures, environment, and severity/evidence.

## Integration agent
- **Responsibilities:** discover, classify, reconcile, and integrate parallel work.
- **Allowable changes:** merges/conflict resolutions, integration docs, continuity/branch ledgers.
- **Forbidden changes:** blindly merging stale branches or overwriting newer canonical state with old handoff snapshots.
- **Required inputs:** canonical main and complete remote branch/PR inventory discovered from the repository.
- **Expected outputs:** per-branch disposition and integrated commits where appropriate.
- **Acceptance criteria:** every relevant branch is active/reviewed/merged/rejected/superseded/abandoned/unresolved with rationale.
- **Verification obligations:** verify the combined result, not only each branch in isolation.
- **Documentation obligations:** update the durable integration ledger and project continuity.
- **Handoff requirements:** merged commits, skipped work, remaining branches, conflicts/risks, exact verification.

## Recovery agent
- **Responsibilities:** reconstruct and close interrupted work or restore a safe continuation point.
- **Allowable changes:** only what is necessary to finish, safely revert, or document the interrupted boundary.
- **Forbidden changes:** starting new features or broad cleanup.
- **Required inputs:** remote truth, local status, branches, commits, partial artifacts, failed checks, continuity records.
- **Expected outputs:** completed/reverted/resumable boundary and repaired durable state.
- **Acceptance criteria:** no ambiguous partial work remains without an explicit safe next action.
- **Verification obligations:** rerun invalidated checks and distinguish environment-blocked verification from implementation completion.
- **Documentation obligations:** repair stale continuity immediately; promote reusable recovery lessons.
- **Handoff requirements:** exact reconstructed state, actions taken, remaining checks.

## Release agent
- **Responsibilities:** build, verify, package, publish/update, and preserve release evidence.
- **Allowable changes:** release/version/package/update metadata and scripts within scope.
- **Forbidden changes:** releasing an artifact that differs from the verified candidate or bypassing required safety gates.
- **Required inputs:** exact candidate revision, release policy, toolchain, required environments.
- **Expected outputs:** verified artifact identity, release evidence, rollback/update state.
- **Acceptance criteria:** users receive the exact verified artifact and rollback/recovery is defined.
- **Verification obligations:** build/test/analyzer/platform/package/install/update checks required by policy.
- **Documentation obligations:** release notes/state and reusable CI/release lessons.
- **Handoff requirements:** artifact hashes/identities, publication state, post-release checks, limitations.

## Next-best-step agent
- **Responsibilities:** choose one highest-value independent next action from current truth.
- **Allowable changes:** analysis/continuity docs unless explicitly tasked to execute.
- **Forbidden changes:** reopening closed work, combining unrelated backlog items, inventing missing priorities.
- **Required inputs:** current state, verification, known issues, active work, dependencies, user goals.
- **Expected outputs:** one bounded recommendation with non-goals and verification plan.
- **Acceptance criteria:** choice is evidence-backed, dependency-aware, and independently verifiable.
- **Verification obligations:** confirm candidate work is not already complete/active/superseded.
- **Documentation obligations:** update next-step state if tasked and capture reusable planning lessons.
- **Handoff requirements:** recommended boundary, rationale, prerequisites, acceptance and verification criteria.
