# Project Plan — Canonical Work Ledger

This file is the repository's single source of truth for **current planned, active, blocked, deferred, and recovery work**. Detailed design documents, issues, PRs, branches, and archive tags may carry evidence, but they do not replace this ledger.

## Operating rules

- Every substantial feature, recovery lane, or cross-cutting engineering change gets one stable ID here.
- Allowed statuses: `PLANNED`, `READY`, `ACTIVE`, `BLOCKED`, `DEFERRED`, `DONE`, `SUPERSEDED`.
- Every active item records an owner or explicit `unclaimed`, acceptance criteria, source/evidence, and one concrete next action.
- A checkbox is complete only when its statement is true on canonical `main` or exact referenced evidence.
- Branch cleanup is a **work-extraction exercise**, not a branch-count exercise. Before deleting a stale/divergent branch, inspect it for unique code, tests, research, fixes, or evidence. Any still-useful unique work must first be integrated or extracted into an actionable item here with exact provenance and a finish/supersede decision path.
- Archive tags preserve bytes and provenance; **an archive tag alone does not mean useful work was integrated, finished, or safely forgotten**.
- A recovery item may close only when its useful semantics are on canonical `main`, are intentionally superseded with rationale, or are rejected after review with the useful evidence preserved.
- Root `README.md` carries only a compact progress mirror. If it disagrees with this file, repair the README.
- Recurring blocked items use the repository's `DEFERRED-TO-LIVE` rotation rule rather than retry loops.

## Current recovery queue

These entries recover unique work that had been archived during the 2026-09-30 branch cleanup. Exact source tips remain immutable under `archive/branch-zero-20260930/...`; the work below is live unfinished work until each item reaches a documented terminal disposition.

| ID | Priority | Status | Source | Goal |
| --- | --- | --- | --- | --- |
| RECOVERY-001 | P0 | ACTIVE | `archive/branch-zero-20260930/agent-central-project-plan-v8.8.49-20260930-5fd5319c` | Restore centralized planning and make branch cleanup extract unique work before deletion. |
| RECOVERY-002 | P0 | ACTIVE | `archive/branch-zero-20260930/feat-catalog-browser-v8.8.50-20260930-a772aeee` | Finish the in-app catalog browser against current main. |
| RECOVERY-003 | P1 | READY | `archive/branch-zero-20260930/feature-manual-update-check-20260930-385c1146` | Restore and finish the manual “Check for updates” UI path. |
| RECOVERY-004 | P0 | READY | `archive/branch-zero-20260930/fix-audit-hardening-20260930-6fd76158`; `archive/branch-zero-20260930/fix-runtime-hardening-20260930-34df6690` | Reconcile overlapping updater/runtime hardening without regressing newer main. |
| RECOVERY-005 | P1 | READY | `archive/branch-zero-20260930/fix-dark-combobox-v8.8.53-20260930-9bfb3028` | Finish dark ComboBox chrome/contrast behavior and regression coverage. |
| RECOVERY-006 | P1 | READY | `archive/branch-zero-20260930/fix-heaven-auth-status-v8.8.52-20260930-9e482688` | Finish truthful Heaven auth-required status and blocked-state UI. |
| RECOVERY-007 | P0 | READY | `archive/branch-zero-20260930/fix-installed-game-discovery-20260930-691315f4` | Finish automatic universal installed-game discovery and lifecycle hardening. |
| RECOVERY-008 | P0 | DONE | `archive/branch-zero-20260930/fix-agent-coordination-leases-20260930-f742d988`; canonical `main` `be001c2582564e55a7561a97bff791bbf821621a` | Lease/ownership conflict hardening integrated as v8.8.54. |

## RECOVERY-001 — Central planning + safe branch cleanup

**Owner:** branch-cleanup recovery owner  
**Acceptance:** canonical plan exists; cleanup policy requires semantic extraction before deletion; global agent training carries the rule; recovered work remains visible until integrated/superseded/rejected.

- [x] Recover a canonical plan ledger from the archived planning work.
- [x] Enumerate every archived unique-work lane from the cleanup incident.
- [ ] Integrate this policy/ledger change to `main` after reconciling the active v8.8.54 lane.
- [ ] Add/retain mechanical validation that the canonical plan remains discoverable from agent bootstrap/handoff.
- [ ] After integration, mark this item DONE and keep the other recovery items live.

**Next action:** reconcile this v8.8.55 recovery-policy candidate onto the post-v8.8.54 main, run continuity/governance verification, and integrate.

## RECOVERY-002 — Catalog browser

**Owner:** active catalog recovery lane  
**Current live branches:** `fix/issue281-catalog-v8.8.55` (fresh-main recovery candidate); `fix/issue281-catalog-browser-recovery` (older preserved source lane)  
**Related issue:** #281  
**Acceptance:** current-main-compatible browse/search/detail UI, provider-neutral composition, focused tests, safe acquisition boundaries, current metadata/versioning, exact integration evidence.

- [x] Preserve exact archived source tip.
- [x] Rehydrate implementation work into a live recovery branch.
- [ ] Review the archived UI/view-model code against current catalog contracts and newer main.
- [ ] Repair/reconcile stale assumptions and metadata.
- [ ] Run focused catalog/UI checks and required broader gates.
- [ ] Integrate the verified semantic result to `main`, then retire the recovery branch.

**Next action:** continue the existing recovery branch; do not create a duplicate catalog implementation lane.

## RECOVERY-003 — Manual update check

**Owner:** unclaimed  
**Acceptance:** user can explicitly trigger the existing updater check path; automatic updater behavior remains unchanged; UI/state regressions cover both routes.

- [x] Preserve exact archived source tip.
- [ ] Port only the current-compatible manual-check behavior/tests onto fresh main.
- [ ] Verify updater concurrency/state semantics against current updater code.
- [ ] Run updater/UI regressions and integrate.

**Next action:** claim the updater UI boundary after checking RECOVERY-004 ownership to avoid overlapping updater edits.

## RECOVERY-004 — Runtime/updater audit hardening

**Owner:** unclaimed  
**Acceptance:** semantically reconcile the two overlapping archived lanes; retain valid fixes for dashboard stretch, metadata retry, staged-update identity/mutation serialization, and diagnostic redaction without importing stale version/continuity snapshots.

- [x] Preserve both exact archived source tips.
- [ ] Diff both lanes against current updater/runtime code and deduplicate equivalent fixes.
- [ ] Reproduce which defects still exist on current main.
- [ ] Port only still-needed code/tests and add/retain regressions.
- [ ] Run focused updater/runtime checks, then required exact-head gates and integrate.

**Next action:** perform a semantic sibling audit of the two archive tags before writing code; do not cherry-pick stale full-history metadata.

## RECOVERY-005 — Dark ComboBox chrome

**Owner:** unclaimed  
**Acceptance:** remove jarring native white ComboBox chrome while preserving readable selected/dropdown text, theme behavior, accessibility, and non-laggy UI.

- [x] Preserve exact archived source tip.
- [ ] Compare archived template with current v8.8.49+ contrast fixes.
- [ ] Port only complementary styling and regressions that remain useful.
- [ ] Verify rendered behavior on Windows/WPF and integrate.

**Next action:** inspect current `App.xaml` ComboBox styles and archived template side by side before deciding what remains unique.

## RECOVERY-006 — Heaven auth status

**Owner:** unclaimed  
**Acceptance:** Agent Manager/Control distinguishes auth-required/degraded/blocked states truthfully and does not misreport machine absence; plugin/runtime identities remain synchronized.

- [x] Preserve exact archived source tip.
- [ ] Reconcile archived provider/status mapping with current Heaven Bridge HMAC/ACL behavior and issue #411.
- [ ] Port the still-valid UI/provider logic and tests.
- [ ] Run Agent Control/plugin verification and live status smoke, then integrate.

**Next action:** verify current provider schema/state names before porting; security boundaries remain fail closed.

## RECOVERY-007 — Installed-game discovery

**Owner:** unclaimed  
**Acceptance:** installed-game discovery works beyond MHW, stays off the UI thread, serializes registry mutation safely, avoids false executable filtering, and has lifecycle/regression coverage.

- [x] Preserve exact archived source tip.
- [ ] Reproduce current-main multi-game discovery behavior.
- [ ] Port current-compatible discovery/lifecycle changes and tests.
- [ ] Verify on Windows against representative installed games and integrate.

**Next action:** inspect current `GamesPageViewModel`, registry, and multi-game tests before applying archived deltas.

## RECOVERY-008 — Agent coordination leases

**Owner:** current Agent Control coordination owner  
**Canonical integration:** `main` `be001c2582564e55a7561a97bff791bbf821621a` (v8.8.54)  
**Acceptance:** one canonical current-main implementation rejects overlapping/case-insensitive ownership conflicts, passes Agent Control tests, and survives integration. Redundant historical/live refs may be retired only after confirming no additional unique semantics remain.

- [x] Preserve the deleted predecessor tip.
- [x] Rehydrate/reconcile work into current v8.8.54 lanes.
- [x] Select and verify one exact candidate.
- [x] Integrate to `main` and prove the intended tree survived.
- [ ] Retire redundant coordination/evidence branches only after confirming they add no unique semantics beyond canonical v8.8.54/evidence.

**Next action:** cleanup-only: classify remaining v8.8.54 coordination/evidence refs and retire only proven-redundant refs.

## Cleanup disposition contract

For every future branch cleanup, record one of these outcomes before deleting the branch ref:

1. **INTEGRATED** — useful semantics are verified on canonical `main` (tree/content proof, not ancestry alone).
2. **EXTRACTED** — useful unique work is represented by an actionable canonical-plan item with exact source ref/tag, acceptance criteria, and next action.
3. **SUPERSEDED** — another exact implementation intentionally replaces it; record replacement and why no unique semantics remain.
4. **REJECTED** — reviewed and intentionally not wanted; preserve useful evidence/rationale where appropriate.

`ARCHIVED` by itself is only a preservation mechanism and is **not** a terminal work disposition.
