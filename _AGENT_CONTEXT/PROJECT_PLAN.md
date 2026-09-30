# Project Plan — Canonical Feature Ledger

This file is the **single source of truth for current feature plans, priority, ownership, and progress** in this repository.

Specialized design documents may explain architecture, research, or implementation detail, but they must link back here and must not maintain a competing current-status checklist. Historical plans remain useful evidence, not live scheduling authority.

## Operating rules

- Every feature or substantial product/agent capability gets one stable ID here before implementation expands beyond a trivial local fix.
- Every entry carries priority, status, owner, dependencies, acceptance criteria, checkbox milestones, and one concrete next action.
- Allowed statuses are `PLANNED`, `READY`, `ACTIVE`, `BLOCKED`, `DEFERRED`, `DONE`, and `SUPERSEDED`.
- Update this file in the same checkpoint when work is proposed, claimed, materially advanced, blocked/deferred, integrated, completed, or superseded.
- A checkbox is ticked only when its statement is true on canonical repository state or exact referenced evidence. Design completion is not implementation completion; implementation is not verification; verification is not integration.
- Root `README.md` mirrors only the current top-level goals/progress and links here. If the mirror disagrees with this file, this file wins and the mirror must be repaired.
- `CURRENT_STATE.md`, `NEXT_STEPS.md`, handoffs, issues, PRs, and design docs may carry evidence or task detail but must point to the relevant plan ID instead of creating a second feature roadmap.
- One primary owner may mutate a plan item at a time. Support/review agents report evidence to that owner or update only clearly independent milestones.
- Blocked recurring work follows the repository DEFERRED-TO-LIVE rotation rule; do not spin on an unchanged blocker.

## Current priorities

| ID | Priority | Status | Progress | Goal |
| --- | --- | --- | --- | --- |
| PLAN-001 | P0 | ACTIVE | 5/6 | Centralize feature planning/progress and make it mechanically durable. |
| AGENT-001 | P0 | ACTIVE | 3/6 | Finish Agent Manager end-to-end reliability and operator trust. |
| CATALOG-001 | P0 | ACTIVE | 2/6 | Deliver a polished in-app mod browser/catalog with safe acquisition. |
| AGENT-002 | P1 | PLANNED | 0/5 | Show the canonical plan directly in Agent Manager with compact progress UI. |
| AGENT-003 | P1 | PLANNED | 0/6 | Make scheduling/claims/dependencies plan-aware and recovery-safe. |
| AUTO-001 | P1 | ACTIVE | 1/5 | Complete Auto Modder from typed contracts to safe usable workspace. |
| AGENT-004 | P2 | PLANNED | 0/5 | Add plan hygiene, progress evidence, and useful execution metrics. |

Priority order is intentional: finish the planning-system integration, close the existing Agent Manager P0 reliability contract, then continue the active Mod Browser/catalog. Lower-priority work may proceed only when it does not steal an owned mutable boundary or violate a current P0 lock.

## PLAN-001 — Centralized planning and progress governance

**Priority:** P0  
**Status:** ACTIVE  
**Owner:** current planning-governance integration owner  
**Dependencies:** canonical continuity/versioning rules  
**Design references:** this file; generic trainer planning rules

**Acceptance criteria:** one authoritative plan ledger exists; all future feature planning routes through it; README exposes concise current goals/progress; legacy plan files stop acting as independent status authorities; handoff validation catches missing/drifted planning invariants.

- [x] Define one canonical feature-plan/progress file.
- [x] Add compact agent-training rules instead of duplicating full planning policy everywhere.
- [x] Add README current-goals/progress mirror.
- [x] Redirect existing catalog/Auto-Modder/plugin plan documents to this ledger for status.
- [x] Extend handoff validation and negative fixtures for the planning invariant.
- [ ] Verify the exact candidate, integrate it to `main`, confirm remote survival, and then mark this item DONE.

**Next action:** run exact candidate governance/CI checks, merge to `main`, verify canonical content, then perform same-version evidence-only closure.

## AGENT-001 — Agent Manager P0 reliability closure

**Priority:** P0  
**Status:** ACTIVE  
**Owner:** Agent Manager / Agent Control primary owner  
**Dependencies:** authenticated provider/Bridge health; canonical runtime freshness  
**Design references:** `_AGENT_CONTEXT/AGENT_CONTROL_PLANE_2026-09-29.md`, current Agent Control handoffs

**Acceptance criteria:** the operator can trust Agent Manager to represent real live state and safely dispatch, observe, recover, stop, and retire work without stale ownership or false machine/provider claims.

- [x] Close core registry lifecycle, inspector, notification, and operator-action correctness defects.
- [x] Bound bootstrap/context retrieval and keep current runtime identity observable.
- [x] Add fail-closed stop/retirement/recovery ownership rules and regressions.
- [ ] Prove authenticated provider/Bridge health on the real operator path.
- [ ] Prove dispatch → observe → recover/retry → stop/retire end-to-end on current runtime.
- [ ] Close remaining P0 acceptance gaps and record exact operator/runtime evidence.

**Next action:** use the newest Agent Manager handoff/evidence to execute the first still-unproven real-runtime P0 acceptance slice; do not expand feature scope first.

## CATALOG-001 — In-app Mod Browser / federated catalog

**Priority:** P0  
**Status:** ACTIVE  
**Owner:** catalog/mod-browser feature owner  
**Dependencies:** provider compliance/auth boundaries; existing safe import pipeline  
**Design reference:** `docs/catalog/PROJECT_PLAN.md`

**Acceptance criteria:** users can browse/search useful mod sources directly in the manager with a polished responsive UI, inspect source-aware detail, and acquire only through supported provider flows into the existing safe import/install boundary.

- [x] Land provider-neutral contracts, compliance model, deterministic fixtures, and Nexus v3 transport boundary.
- [x] Implement Nexus normalization, file hydration, health/rate/auth state, and assisted-acquisition policy.
- [ ] Persist source-aware cache/provenance and local search/index state.
- [ ] Deliver integrated browse/search/detail UI with useful filters, images, source badges, loading/empty/error states, and no overlay/layout regressions.
- [ ] Route direct/assisted acquisition through the existing validated archive/import boundary and prove installed-origin update linkage.
- [ ] Add the next provider slice (GameBanana and/or curated GitHub Releases) without provider-specific UI leakage.

**Next action:** continue the active Mod Browser UI/cache integration from current main, preserving the existing provider-neutral boundary and fixing user-visible layout defects as part of acceptance.

## AGENT-002 — Agent Manager plan dashboard

**Priority:** P1  
**Status:** PLANNED  
**Owner:** unclaimed  
**Dependencies:** PLAN-001; Agent Manager P0 should be stable enough to avoid UI churn  
**Design reference:** `_AGENT_CONTEXT/PROJECT_PLAN.md`

**Acceptance criteria:** Agent Manager shows the canonical plan at a glance without forcing long scrolling, with collapsed-by-default sections and drill-down for evidence/detail.

- [ ] Read/parse the canonical ledger through one bounded owner instead of scraping arbitrary Markdown throughout the UI.
- [ ] Show priority, status, progress, owner, blocker/dependency, and next action in compact cards/rows.
- [ ] Collapse completed/deferred/detail sections by default while keeping P0/ACTIVE work immediately visible.
- [ ] Add filters for status/priority/owner and a focused item inspector with linked branch/PR/evidence.
- [ ] Add executable UI/state regressions for stale refresh, missing plan, malformed plan, and operator selection preservation.

**Next action:** claim only after AGENT-001's mutable dashboard boundary is available.

## AGENT-003 — Plan-aware scheduling, claims, and recovery

**Priority:** P1  
**Status:** PLANNED  
**Owner:** unclaimed  
**Dependencies:** PLAN-001; AGENT-001  
**Design references:** generic multi-agent coordination and continuity rules

**Acceptance criteria:** autonomous workers select ready work from the central plan, respect dependencies/ownership, and leave recoverable progress that another agent can resume without chat history.

- [ ] Define a bounded machine-readable projection of plan IDs/status/dependencies/next-action without making a second source of truth.
- [ ] Add atomic claim/lease semantics so two agents cannot independently own the same mutable plan item.
- [ ] Make scheduler selection prefer READY/ACTIVE unblocked work and skip unmet dependencies or unchanged DEFERRED blockers.
- [ ] Require each worker checkpoint to attach branch/SHA, completed milestones, verification, blocker, and successor action to its plan ID.
- [ ] Reassign interrupted work from the exact unfinished milestone rather than restarting the whole feature.
- [ ] Add mixed-state tests: healthy worker continues while a failed sibling is recovered against the same plan ledger.

**Next action:** design the projection/claim boundary after Agent Manager P0 ownership is stable; reuse existing task/lease primitives.

## AUTO-001 — Auto Modder / Mod Builder

**Priority:** P1  
**Status:** ACTIVE  
**Owner:** Auto Modder feature owner  
**Dependencies:** safe import/deployment pipeline; verified format adapters  
**Design reference:** `docs/AUTO-MODDER-PLAN.md`

**Acceptance criteria:** a user can choose a supported recipe, fill human-readable inputs, preview exact typed changes, build a provenance-rich mod package, and add it through the normal safe manager workflow.

- [x] Complete M0 typed recipe/schema, patch-plan, sandbox, capability/version, manifest, threat-model, and synthetic-fixture contracts.
- [ ] Complete real format-adapter execution and entity/catalog lookup for the first useful recipe family.
- [ ] Build the WPF workspace: recipe gallery, dynamic form, validation, preview/diff, and advanced/developer detail.
- [ ] Produce generated packages through sandbox/provenance and route them through normal library/import/deployment.
- [ ] Add recipe SDK/docs/fixtures and end-to-end regression coverage for supported formats and failure cases.

**Next action:** keep implementation on one narrow first recipe/adapter slice; do not broaden to arbitrary binary scripting or a second deployment path.

## AGENT-004 — Plan hygiene, evidence, and metrics

**Priority:** P2  
**Status:** PLANNED  
**Owner:** unclaimed  
**Dependencies:** PLAN-001; useful runtime history from AGENT-003

**Acceptance criteria:** planning stays low-maintenance and honest while giving the operator useful signals rather than vanity metrics.

- [ ] Detect duplicate feature IDs, invalid status transitions, impossible progress counts, missing next actions, and stale owners.
- [ ] Verify README summary remains a projection of current top-level plan items rather than an independent roadmap.
- [ ] Surface plan age, blocked duration, retries/rework, verification failures, and completion throughput by item/role without rewarding low-quality churn.
- [ ] Flag features with repeated regressions or reopened milestones so prevention work becomes visible.
- [ ] Archive DONE/SUPERSEDED detail compactly without deleting provenance or bloating the active view.

**Next action:** defer until real plan history exists; first measure what operators actually use.

## Adding or changing a feature

Use the smallest entry that keeps coordination unambiguous:

1. Choose a stable ID and priority.
2. State one outcome-oriented goal and acceptance criteria.
3. List only meaningful independently checkable milestones.
4. Record owner, dependencies, design/evidence references, and one next action.
5. Update status/checks as truth changes; never pre-tick future work.
6. Keep detailed architecture in the subsystem's normal docs, linked here.
7. Keep README's summary short and synchronized.

If a proposal is rejected or replaced, mark it `SUPERSEDED` with the replacement/reason rather than deleting history.
