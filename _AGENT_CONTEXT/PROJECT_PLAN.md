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
| SECURITY-554 | P0 | DONE | issue #554; PR #555 | Close permitted-crawler path/redirect containment gaps before any real HTML provider adapter is enabled. |
| BROWSE-556 | P1 | DONE | issue #556; PR #567; hosted Windows `36978710736`; installed-client E2E `36979261045` | v8.8.68 packaged ComboBox peer-value selector, Switch/Settings, update, and rollback acceptance passed; issue closed. |
| BROWSE-557 | P1 | DONE | issue #557; PR #561 | Deliver artwork-backed rich Browse Mods results and details. |
| CATALOG-SCALE-558 | P1 | ACTIVE | issue #558; PR #561 first tranche; PR #565 v8.8.69 integrated | Expand catalog breadth safely with capability-gated search, coherent cache capacity, and provider-aware scaling. |
| BROWSE-UX-559 | P1 | READY | issue #559 | Add filters, sorting, provider health, and richer discovery states. |
| CATALOG-UPDATES-569 | P1 | ACTIVE | issue #569; PR #573 | Eliminate duplicate installed-origin provider detail hydration while preserving exact update identity and fail-closed replacement selection. |
| RECOVERY-578 | P0 | READY | issue #578; follow-up to #571 / PR #572 | Recover canonical MHW identity from stale generic profiles and handle multiple same-root profiles safely. |

## SECURITY-554 — Permitted crawler path + redirect containment

**Owner:** completed on canonical v8.8.63 main via PR #555
**Related issue:** #554
**Acceptance:** segment-safe path prefixes; no implicit redirect following; bounded manual redirects validated before each follow-up request; disallowed targets never contacted; existing crawler safety/compliance behavior preserved; exact-head repository gates green before integration.

- [x] Replace raw path `StartsWith` authorization with segment-safe matching.
- [x] Reject malformed/ambiguous path-prefix manifests.
- [x] Move production crawler transport ownership inside the crawler and disable automatic redirects.
- [x] Validate and follow allowed redirects manually with a bounded hop count and loop detection.
- [x] Add deterministic tests for allowed redirects and for off-origin/HTTP/alternate-port/user-info/out-of-prefix/missing/looping/excessive redirects.
- [x] Required exact-head gates passed; PR #555 integrated to canonical main and closed issue #554.

**Next action:** DONE. Preserve crawler containment while continuing only provider-specific adapters with reviewed contracts.

## BROWSE-556 / BROWSE-557 / CATALOG-SCALE-558 / BROWSE-UX-559 — Catalog UX modernization

**Owner:** #556 completed via PR #567 / v8.8.68; #558 v8.8.69 provider-search/capacity tranche integrated via PR #565; #559 remains ready. Both must preserve the closed selector boundary.
**Integrated PRs:** #561 at merge `a39064643f97aa5a0df80bf7c917d260b3d7e7a1`; #562 at merge `c3f238cbe4f1850763736bac999af0577a4606c5`; #567 at merge `7def58c1b16d115e1555738ebad51717d1c1f752`
**Acceptance:** selector displays a human game name; Browse Mods exposes artwork and useful metadata without weakening safe acquisition; larger result sets remain virtualized; provider breadth grows only through capabilities each provider actually supports.

- [x] v8.8.64 added an explicit `DisplayName` item template for dropdown rows.
- [x] v8.8.65 follow-up: make the custom ComboBox closed selected presenter reuse `ItemTemplate`/selector/string format so the selected game cannot fall back to raw `GameProfile` text.
- [x] Exact PR #562 head `236d3604f1df245f9c224eda3f21f2177979cfd2` passed Workflow Feature run `36963458484`, Product Security run `36963458384`, and Toolbox Ownership run `36963458431`, then merged as `c3f238cbe4f1850763736bac999af0577a4606c5`.
- [x] Add a packaged-client UI Automation hook for the rendered selected game text while retaining character ellipsis.
- [x] Extend the real updater installed-client E2E to assert the rendered game DisplayName plus enabled Switch/Settings actions and persist that evidence.
- [x] v8.8.66 passed exact-head PR gates, merged via PR #563, and published immutable updater-main-380.
- [x] Preserve Updater Installed Client E2E #289 as a real failed selector-acceptance result rather than closing #556.
- [x] Add v8.8.67 stable selector automation identity, raw-tree UIA traversal, and console-first failure diagnostics.
- [x] v8.8.67 exact-head gates passed; PR #566 merged as `af73d3ce55b3bcbeb5d34184506829e57aca9cba`; Windows Release Gate #381 published immutable updater-main-381.
- [x] Preserve E2E #290 attempt 2 as deterministic evidence that the closed `ActiveGameSelector` peer exposes zero raw descendants; do not waive or loop the failed child-UIA assertion.
- [x] Add v8.8.68 `AutomationProperties.ItemStatus={Binding SelectedGame.DisplayName}` plus `TextSearch.TextPath=DisplayName` and switch packaged acceptance to the visible/bounded ComboBox peer value.
- [x] v8.8.68 PR #567 merged as `7def58c1b16d115e1555738ebad51717d1c1f752`; hosted Windows run `36978710736` passed 26/26 and installed-client E2E `36979261045` passed selector peer DisplayName, Switch/Settings, real update, and rollback; issue #556 closed.
- [x] Add safe HTTPS thumbnails, summary/author/category/download metadata, provider labels, version and update time to Browse Mods.
- [x] Add selected-mod artwork and preserve exact-file install/source-page behavior.
- [x] Enable recycled row virtualization.
- [x] Raise first-tranche refresh/cache limits to 100/provider and 1000 visible cached rows.
- [x] Add focused source/XAML regression guards.
- [x] Exact head `3ced8b41041909d91b06902631ef36085bfd7489` passed all required PR #561 gates and integrated #556/#557 as `a39064643f97aa5a0df80bf7c917d260b3d7e7a1`.
- [x] v8.8.69 candidate: explicit Search queries only providers advertising `CatalogProviderCapabilities.Search`; text-entry debounce remains local-cache-only.
- [x] v8.8.69 candidate: raise the repository search clamp from 500 to 1000 to match the virtualized UI capacity, with a deterministic >500-row regression.
- [x] PR #565 exact head `fdecfcccf9e04850acb983e91582d3c915283efa` passed Workflow Feature `36981898989`, MHW Product Security `36981899021`, and Heaven Toolbox Ownership `36981898930`, then squash-merged as `7ee321a349098849b9e4db302b20dfa1595ca13b`.
- [ ] Continue #558 with broader provider-aware pagination/browse expansion and deterministic scale/performance coverage.
- [ ] Continue #559 with filters/sorting/provider health/loading/empty/partial-failure states.

**Next action:** continue #558 with broader provider-aware pagination/scale work while preserving integrated v8.8.69 behavior; #559 remains a separate UX tranche.

## CATALOG-UPDATES-569 — Installed-origin provider snapshot reuse

**Owner:** issue #569 / PR #573, v8.8.71 candidate
**Acceptance:** avoid redundant provider detail hydration when one authoritative response can supply both mod metadata and exact file identity; preserve provider capability declarations, cancellation, failure isolation/health state, exact identity validation, and fail-closed replacement selection.

- [x] Add an optional installed-origin snapshot provider contract with the existing two-call provider interface retained as fallback.
- [x] Implement GameBanana snapshot reuse so one exact installed-origin update check performs one mod-detail request.
- [x] Add deterministic request-count coverage and preserve exact file/current-state assertions.
- [x] Reconcile source/tests onto canonical v8.8.70 main and advance synchronized release/continuity metadata to v8.8.71.
- [ ] Pass Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on one exact final PR #573 head.
- [ ] Squash-merge the exact green head, verify canonical main, and close issue #569.

**Next action:** run all three exact-head gates for v8.8.71 PR #573, refresh main immediately before integration, and merge only that final green candidate.

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

**Owner:** v8.8.70 PR #572 integrated the primary stale-profile lifecycle repair; issue #578 tracks post-merge edge cases; original RECOVERY-007 discovery source is integrated
**Acceptance:** installed-game discovery works beyond MHW, stays off the UI thread, serializes registry mutation safely, avoids false executable filtering, and has lifecycle/regression coverage.

- [x] Preserve exact archived source tip.
- [x] Reproduce current-main multi-game discovery behavior.
- [x] Port current-compatible discovery/lifecycle changes and tests.
- [x] Integrate the original universal installed-game discovery source/lifecycle changes on canonical main.
- [x] v8.8.70 PR #572 integrated the primary stale same-root repair while preserving ID/active selection and filling missing discovery metadata.
- [x] v8.8.70 hardens the known-MHW non-`MonsterHunterWorld.exe` repair guard using persisted MHW state or Steam app 582010.
- [ ] Resolve issue #578: a stale generic MHW profile repaired with a valid MHW discovery must recover the canonical MHW adapter/integration shape.
- [ ] Resolve issue #578: when multiple profiles share one root, any live same-root profile must suppress automatic repair/duplicate registration.
- [ ] Verify on Windows against representative installed games before marking RECOVERY-007 DONE.

**Next action:** resolve issue #578's two lifecycle edge cases with deterministic regressions, then perform representative installed-game Windows/runtime proof before marking RECOVERY-007 DONE.


## Cleanup disposition contract

For every future branch cleanup, record one of these outcomes before deleting the branch ref:

1. **INTEGRATED** — useful semantics are verified on canonical `main` (tree/content proof, not ancestry alone).
2. **EXTRACTED** — useful unique work is represented by an actionable canonical-plan item with exact source ref/tag, acceptance criteria, and next action.
3. **SUPERSEDED** — another exact implementation intentionally replaces it; record replacement and why no unique semantics remain.
4. **REJECTED** — reviewed and intentionally not wanted; preserve useful evidence/rationale where appropriate.

`ARCHIVED` by itself is only a preservation mechanism and is **not** a terminal work disposition.
