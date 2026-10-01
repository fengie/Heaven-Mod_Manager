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

Global Agent Control / Heaven / plugin recovery work has transferred to `fengie/heaven-toolbox` issue #5 and is no longer MHW product work. The queue below is intentionally limited to MHW Manual Mod Manager work and its repository-local planning/recovery.

These entries recover unique work that had been archived during the 2026-09-30 branch cleanup. Exact source tips remain immutable under `archive/branch-zero-20260930/...`; the work below is live unfinished work until each item reaches a documented terminal disposition.

| ID | Priority | Status | Source | Goal |
| --- | --- | --- | --- | --- |
| RECOVERY-001 | P0 | DONE | `archive/branch-zero-20260930/agent-central-project-plan-v8.8.49-20260930-5fd5319c` | Restore centralized planning and make branch cleanup extract unique work before deletion. |
| RECOVERY-002 | P0 | DONE | `archive/branch-zero-20260930/feat-catalog-browser-v8.8.50-20260930-a772aeee` | Finish the in-app catalog browser against current main. |
| RECOVERY-003 | P1 | DONE | `archive/branch-zero-20260930/feature-manual-update-check-20260930-385c1146` | Restore and finish the manual “Check for updates” UI path. |
| RECOVERY-004 | P0 | DONE | `archive/branch-zero-20260930/fix-audit-hardening-20260930-6fd76158`; `archive/branch-zero-20260930/fix-runtime-hardening-20260930-34df6690` | Reconcile overlapping updater/runtime hardening without regressing newer main. |
| RECOVERY-005 | P1 | ACTIVE | `archive/branch-zero-20260930/fix-dark-combobox-v8.8.53-20260930-9bfb3028` | Finish dark ComboBox chrome/contrast behavior and regression coverage. |
| RECOVERY-007 | P0 | ACTIVE | `archive/branch-zero-20260930/fix-installed-game-discovery-20260930-691315f4` | Finish automatic universal installed-game discovery and lifecycle hardening. |

## Current issue work

| ID | Priority | Status | Source | Goal |
| --- | --- | --- | --- | --- |
| SECURITY-554 | P0 | ACTIVE | issue #554; `fix/issue-554-crawler-containment-v8.8.63` | Close permitted-crawler path/redirect containment gaps before any real HTML provider adapter is enabled. |

## SECURITY-554 — Permitted crawler path + redirect containment

**Owner:** ChatGPT live issue lane on `fix/issue-554-crawler-containment-v8.8.63`  
**Related issue:** #554  
**Acceptance:** segment-safe path prefixes; no implicit redirect following; bounded manual redirects validated before each follow-up request; disallowed targets never contacted; existing crawler safety/compliance behavior preserved; exact-head repository gates green before integration.

- [x] Replace raw path `StartsWith` authorization with segment-safe matching.
- [x] Reject malformed/ambiguous path-prefix manifests.
- [x] Move production crawler transport ownership inside the crawler and disable automatic redirects.
- [x] Validate and follow allowed redirects manually with a bounded hop count and loop detection.
- [x] Add deterministic tests for allowed redirects and for off-origin/HTTP/alternate-port/user-info/out-of-prefix/missing/looping/excessive redirects.
- [ ] Run required exact-head gates, reconcile fresh `main`, integrate, and close issue #554.

**Next action:** open the v8.8.63 PR, use GitHub Actions as the Windows/.NET verification environment, repair any exact-head failures, then merge only after required gates are green.

## RECOVERY-001 — Central planning + safe branch cleanup

**Owner:** completed on canonical main
**Acceptance:** canonical plan exists; cleanup policy requires semantic extraction before deletion; Heaven Toolbox carries the reusable global rule; recovered MHW product work remains visible until integrated/superseded/rejected.

- [x] Recover a canonical plan ledger from the archived planning work.
- [x] Enumerate every archived unique-work lane from the cleanup incident.
- [x] Integrate this policy/ledger change to `main` after reconciling the active v8.8.54 lane.
- [x] Add/retain mechanical validation that the canonical plan remains discoverable from agent bootstrap/handoff.
- [x] After integration, mark this item DONE and keep the other recovery items live.

**Next action:** DONE on canonical v8.8.55 main; continue the remaining recovery items.

**Branch disposition:** `fix/branch-cleanup-recovery-v8.8.55-20261001` at `b9b87f6eee78be2ab99b0fc69b13330f40da6c0f` is **INTEGRATED/SUPERSEDED**. Current MHW main retains the RECOVERY-001 ledger and cleanup-extraction contract; the branch's reusable training/Git-doctrine semantics moved to Heaven Toolbox during the v8.8.57 ownership cutover. The obsolete live branch may be retired without losing unique work.

## RECOVERY-002 — Catalog browser

**Owner:** completed on canonical v8.8.62 main via PR #553
**Preserved reference branches:** `fix/issue281-catalog-v8.8.55`; `fix/issue281-catalog-browser-recovery` (both retain unique historical commits and require ancestry classification before deletion)
**Related issue:** #281
**Acceptance:** current-main-compatible browse/search/detail UI, provider-neutral composition, focused tests, safe acquisition boundaries, current metadata/versioning, exact integration evidence.

- [x] Preserve exact archived source tip.
- [x] Rehydrate implementation work into a live recovery branch.
- [x] Review the archived UI/view-model code against current catalog contracts and newer main.
- [x] Repair/reconcile stale assumptions and metadata.
- [x] Run focused catalog/UI checks and required broader gates.
- [x] Integrate the verified semantic result to `main` via PR #553; defer branch retirement until unique historical ancestry is explicitly classified.

**Next action:** DONE on canonical v8.8.62 main at merge `3c19270a9dbf37c434aaa0654fdedcc8c28c6d71`. Keep issue #281 open only for its real conditional/optional follow-up contracts; preserve the two historical recovery branches until their unique ancestry is classified.

## RECOVERY-003 — Manual update check + user update preference

**Owner:** completed on canonical main
**Status:** DONE
**Acceptance:** user can explicitly trigger the existing verified updater path, can disable background automatic checks/installation, and both routes preserve current updater concurrency/safe-handoff guarantees.

- [x] Preserve exact archived manual-check behavior and reuse the existing updater command.
- [x] Expose the manual updater action in the Settings tab.
- [x] Add a persistent automatic-update preference without removing manual update/install intent.
- [x] Gate background checks and staged automatic handoff when automatic updates are disabled.
- [x] Add preference/updater/UI regression coverage.
- [x] Run exact-head required gates and integrate to `main` via PR #550.

**Integrated evidence:** PR #550 head `b7db699ab105a036b249c8b373f561327be4d2d5` passed Workflow Feature PR Gate run 645, MHW Product Security Gate run 635, and Heaven Toolbox Ownership Gate run 35, then merged as `3e9729ffa4e90660e939657e3a72f9c541d62344`. Follow-up hosted/installed-client evidence was persisted on main without another product patch.

**Next action:** DONE; preserve the Settings preference gates while reconciling RECOVERY-004.

## RECOVERY-004 — Runtime/updater audit hardening

**Owner:** completed on canonical main via PR #552
**Status:** DONE
**Acceptance:** semantically reconcile the two overlapping archived lanes; retain valid fixes for dashboard stretch, metadata retry, staged-update identity/mutation serialization, and diagnostic redaction without importing stale version/continuity snapshots.

- [x] Preserve both exact archived source tips.
- [x] Diff both lanes against current updater/runtime code and deduplicate equivalent fixes.
- [x] Reproduce all four still-missing defects on current 8.8.59 main.
- [x] Port only still-needed code/tests while preserving the 8.8.59 Settings/manual-update semantics.
- [x] Run focused Windows build/integration/function-verifier checks.
- [ ] Run required exact-head gates, refresh main, integrate, and retire the stale recovery branch.

**Focused evidence:** source checkpoint `5f11ce59712808ce259dbf72922cc011fb4319c1` built Release with 0 warnings / 0 errors; IntegrationTests passed 268/268; FunctionVerifier reported 1469 functions with 0 trace gaps, 0 uncovered call sites, and 0 parse errors.

**Next action:** finish 8.8.60 continuity/version metadata, run exact-head required gates, then integrate only if fresh-main reconciliation remains non-conflicting.

**Integrated evidence (RECOVERY-004):** PR #552 head `38d0fcaa853c8951e1bb0043cb962fdb2261a92f` passed Workflow Feature PR Gate run 649, MHW Product Security Gate run 640, and Heaven Toolbox Ownership Gate run 37, then merged to canonical main as `2ace37e2731dc9282e04cc42d77c701ff12e1751`.

## RECOVERY-005 — Dark ComboBox chrome

**Owner:** integrated source/test on canonical main; runtime acceptance pending
**Status:** ACTIVE
**Acceptance:** remove jarring native white ComboBox chrome while preserving readable selected/dropdown text, theme behavior, accessibility, and non-laggy UI.

- [x] Preserve exact archived source tip.
- [x] Compare archived template with current v8.8.49+ contrast fixes.
- [x] Port only complementary styling and regression coverage that remains useful.
- [x] Run exact-head MHW gates on the v8.8.58 candidate.
- [x] Integrate the verified source/test tranche to canonical main via PR #549.
- [ ] Verify installed Windows/WPF appearance and interaction, then mark DONE.

**Integrated result:** app-owned ComboBox/ComboBoxItem templates replace the v8.8.49 Windows system-brush workaround while retaining readable selected/dropdown text and existing bindings.

**Automated evidence:** PR #549 head `9c9894d39f5875ef5271260638fb07d26c66da8d` passed Workflow Feature PR Gate run 636 (`36811068797`), MHW Product Security Gate run 626 (`36811068802`), and Heaven Toolbox Ownership Gate run 20 (`36811068784`), then merged as `b270494c047c2de24b2c7aa94bd710c21527d72a`.

**Next action:** perform installed-client visual/interaction proof before marking DONE.

## RECOVERY-007 — Installed-game discovery

**Owner:** ChatGPT recovery lane on `fix/installed-game-discovery-v8.8.56-20260930`
**Acceptance:** installed-game discovery works beyond MHW, stays off the UI thread, serializes registry mutation safely, avoids false executable filtering, and has lifecycle/regression coverage.

- [x] Preserve exact archived source tip.
- [x] Reproduce current-main multi-game discovery behavior.
- [x] Port current-compatible discovery/lifecycle changes and tests.
- [ ] Verify on Windows against representative installed games and integrate.

**Next action:** run exact-head focused/required gates on the v8.8.56 candidate, reconcile fresh `main`, then integrate and perform Windows installed-game/runtime proof before marking DONE.


## Cleanup disposition contract

For every future branch cleanup, record one of these outcomes before deleting the branch ref:

1. **INTEGRATED** — useful semantics are verified on canonical `main` (tree/content proof, not ancestry alone).
2. **EXTRACTED** — useful unique work is represented by an actionable canonical-plan item with exact source ref/tag, acceptance criteria, and next action.
3. **SUPERSEDED** — another exact implementation intentionally replaces it; record replacement and why no unique semantics remain.
4. **REJECTED** — reviewed and intentionally not wanted; preserve useful evidence/rationale where appropriate.

`ARCHIVED` by itself is only a preservation mechanism and is **not** a terminal work disposition.
