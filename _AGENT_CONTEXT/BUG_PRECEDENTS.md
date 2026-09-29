# Bug Precedents

This file is the canonical defect-prevention ledger for the MHW project. It is mandatory training material.

Every discovered bug/regression/process escape must produce or update an entry here before the work is considered complete. Entries are not blame records; they are durable engineering controls. Future agents must read materially relevant precedents before changing the same subsystem.

## Required entry template

### YYYY-MM-DD — <subsystem> — <short defect class>
- **Symptom:**
- **Root cause:**
- **Violated invariant / wrong assumption:**
- **Why prior defenses missed it:**
- **Direct fix:**
- **Preventive rule/process change:**
- **Regression coverage added/strengthened:**
- **Verification evidence/environment:**
- **Sibling/adjacent cases checked:**
- **References (SHA/PR/issue/log):**

## 2026-09-29 — repository-wide — escaped bugs require durable prevention controls
- **Symptom:** User-facing and engineering bugs were still able to appear after agent-authored work, despite substantial verification rules.
- **Root cause:** The governance model emphasized fixing and verifying individual tasks, but did not make every escaped bug automatically create a durable precedent, guideline update, and regression-prevention control.
- **Violated invariant / wrong assumption:** A successful local fix or passing existing test suite was treated as sufficient closure even when the defect exposed a missing invariant, test class, or process guard.
- **Why prior defenses missed it:** Lessons could remain task-local, in chat, or in a one-off fix instead of being propagated into mandatory training and mechanically enforceable checks.
- **Direct fix:** Added the mandatory bug-prevention and precedent protocol to `AGENTS.md` and made this ledger mandatory training material.
- **Preventive rule/process change:** Every bug now requires the closure chain: bug → root cause → precedent log → guideline/process change → regression coverage → verification evidence → propagation.
- **Regression coverage added/strengthened:** Governance now requires automated regression coverage whenever practical and stronger deterministic verification when automation is not feasible; reviewers must reject fixes that skip the chain.
- **Verification evidence/environment:** Follow-up remote fetch of canonical `main` confirmed the governance ledger, mandatory training references, generated-prompt prevention rule, prompt-library version `2026.09.29.5`, and regression assertions are present. Remote `main` was observed at `d08eb2845a4c07314aa4723a7b457525c99c566f` after the enforcement/test updates. Runtime Node execution was not performed through the current GitHub-only control surface, so no runtime-test claim is made here.
- **Sibling/adjacent cases checked:** Applies repository-wide to product bugs, updater/install defects, UI/runtime regressions, integration failures, false completion claims, and agent/process failures.
- **References (SHA/PR/issue/log):** `d941d759ac876e2e7ece0ff16171ceb698eaaf60` (AGENTS protocol), `e72bd81a367f5822596ba87a6966754a92bb132d` (precedent ledger), `fa04973fde2d5bec5e2b7940111d4c6f89ee12c3` (Learned Rules), `f323cd02b20d6cadb9e31e3e73411c786807a0ef` (swarm rules), `6ac8592a97e121b23167c69c4b62aa918ee56138` (start-here), `902ab11b679e5bd497d02a7586e93094ca3883c6` (Agent Control prompt enforcement), `d08eb2845a4c07314aa4723a7b457525c99c566f` (training-gate regression assertions).


## 2026-09-29 — integration tests — cancellation-unaware bounded waits recurred after a narrow fix
- **Symptom:** The Workflow Feature PR Gate still emitted xUnit1051 for a bounded `ManualResetEventSlim.Wait(TimeSpan)` in `DeploymentTests.cs` after PR #261 had already repaired another cancellation-unaware wait in the same test area.
- **Root cause:** The first repair targeted one reported call site instead of closing the sibling defect class across the integration-test boundary, and xUnit1051 remained informational in the relaxed diagnostic build.
- **Violated invariant / wrong assumption:** A cancellation-sensitive test suite must not introduce bounded waits that ignore the test cancellation token; fixing the first analyzer location is not proof that adjacent waits satisfy the same contract.
- **Why prior defenses missed it:** The original repair did not include a sibling scan or a mechanical severity rule for xUnit1051, so another equivalent wait remained source-valid and could recur in later branch verification output.
- **Direct fix:** Pass `TestToken` to the remaining deployment-event wait and add `xUnit1051` to `WarningsAsErrors` for the integration-test project.
- **Preventive rule/process change:** When an analyzer defect is repaired, inspect all sibling occurrences of the same API/pattern in the affected boundary. Cancellation-aware test APIs must use `TestContext.Current.CancellationToken` / the project `TestToken` alias whenever an overload accepts a token.
- **Regression coverage added/strengthened:** Integration-test builds now fail mechanically on xUnit1051 instead of merely printing the warning in the relaxed diagnostic pass.
- **Verification evidence/environment:** Source repair and hard-error policy are on PR #272; exact-head Heaven rerun is required and this entry must be updated with the green run before merge.
- **Sibling/adjacent cases checked:** Re-examined the reported `DeploymentTests.cs` timeout-wait path and the earlier PR #261 repair class; no claim is made for APIs that do not expose cancellation-aware overloads.
- **References (SHA/PR/issue/log):** PR #261; PR #272; failed Workflow Feature PR Gate run 36606848564; repair commits `7bb38fda4f6e887f64b131ced0bff2c735ad0d74` and the policy commit carrying this entry.

## 2026-09-29 — Mods UI — windowed library remained cramped after prior compaction
- **Symptom:** The Mods tab still exposed too little usable library area in windowed mode even after the v8.8.16 “larger mod library workspace” change.
- **Root cause:** The page retained large outer margins, a multi-line header, a wrapping filter/bulk-action toolbar, and a two-line footer. At narrower widths, wrapping auxiliary controls consumed vertical space that should have belonged to the star-sized mod library region.
- **Violated invariant / wrong assumption:** A dense library page must prioritize the primary data surface. Auxiliary controls may shrink, elide, or scroll, but should not repeatedly add rows and squeeze the library at the minimum supported window size.
- **Why prior defenses missed it:** The earlier regression guarded that the layout was more compact than before, but it did not lock in a no-wrap/primary-surface-first invariant for the windowed worst case.
- **Direct fix:** Reduced Mods-page margins, made the header and footer single-row, made toolbar controls compact, and switched Filters/Bulk Actions from wrapping layout to one horizontal scrollable strip.
- **Preventive rule/process change:** Resizable data-management pages must give their primary star-sized content region first claim on remaining space; secondary command groups should prefer horizontal overflow/ellipsis over vertical wrapping when the latter materially shrinks the primary surface.
- **Regression coverage added/strengthened:** Added `ModsPagePrioritizesWindowedLibraryViewport` to assert compact margins, horizontal toolbar overflow, the star-sized library region, compact controls, and the one-line footer detail tooltip.
- **Verification evidence/environment:** Deterministic XAML regression added in the exact product change; canonical Windows verification/release gate is required on the resulting main commit before completion is claimed.
- **Sibling/adjacent cases checked:** Preserved search, filters, bulk actions, staged summary, plan detail access, DataGrid scrolling, empty states, and existing command bindings.
- **References (SHA/PR/issue/log):** Product commit SHA recorded by Git history; release-gate evidence is attached to the exact main SHA.


## 2026-09-29 — Agent Control dashboard — workflow capability lost its one-click UI entry point
- **Symptom:** The Agent Control backend still supported the `usual-swarm` workflow, but the dashboard no longer exposed the one-click swarm deploy action; operators were left with only per-role deploy buttons.
- **Root cause:** Dashboard reconciliation preserved individual role controls while omitting the workflow-level action. No regression assertion treated the swarm button and its workflow endpoint as a required operator surface.
- **Violated invariant / wrong assumption:** A supported first-class workflow must retain an explicit operator entry point unless it is intentionally deprecated together with its backend contract.
- **Why prior defenses missed it:** Existing operator-UI coverage checked lifecycle counters and safety/control endpoints but did not assert the presence of the swarm workflow control.
- **Direct fix:** Restored a `Deploy Usual Swarm` button that posts the current objective/base branch/model/machine to `/api/workflows/usual-swarm/execute` and surfaces blocked workflow reasons instead of silently doing nothing.
- **Preventive rule/process change:** Whenever an operator-facing workflow is added, removed, renamed, or reconciled, keep a paired invariant: backend workflow contract + visible UI entry point + regression assertion for both.
- **Regression coverage added/strengthened:** `tools/agent-control/test/operator-ui-cli.test.mjs` now requires the `deploySwarm` DOM control, button label, and `usual-swarm` execute endpoint wiring.
- **Verification evidence/environment:** The repair is layered onto the latest observed canonical main and must pass the Agent Control tests before closure.
- **Sibling/adjacent cases checked:** Existing per-role deploy controls remain present and share the same busy/disabled guard with the restored swarm action.
- **References (SHA/PR/issue/log):** See the canonical Git commit titled `Agent Control: restore one-click swarm deploy`.


## 2026-09-29 — Agent Control startup — PowerShell variable followed by colon broke parser
- **Symptom:** The first live heaven2 startup-restore validation failed before installation because `Restore-StartupSetup.ps1` would not parse.
- **Root cause:** A double-quoted log string used `$taskName:`; PowerShell interpreted the colon as part of a scoped variable reference instead of literal punctuation.
- **Violated invariant / wrong assumption:** New Windows startup scripts must be parser-valid before they are allowed to register persistence or become a recovery dependency.
- **Why prior defenses missed it:** The initial Node regression tests asserted structural startup behavior but did not exercise PowerShell lexical/parser rules, so a platform-specific syntax class escaped repository-only review.
- **Direct fix:** Changed the interpolation to `${taskName}:`.
- **Preventive rule/process change:** PowerShell emitted by Agent Control must avoid ambiguous `$identifier:` interpolation; use braced variables or formatting. Startup/recovery scripts require an explicit PowerShell parser gate before installation.
- **Regression coverage added/strengthened:** `startup-restore.test.mjs` now scans both startup PowerShell scripts for unsafe variable-colon interpolation; heaven2 validation parses both files with `System.Management.Automation.Language.Parser` before execution.
- **Verification evidence/environment:** Heaven2 bridge job `chatgpt-startup-setup-restore-heaven2-20260929-1819` reproduced the parser failure after safely fast-forwarding a clean local main. Repaired job `chatgpt-startup-setup-restore-heaven2-retry-20260929-1836` then passed native PowerShell parsing, installed the `Heaven Setup Restore` logon task, verified the Startup fallback, found both Heaven Bridge persistence tasks, reported Agent Control healthy, discovered 14/14 manifest-backed local plugins ready, and passed Agent Control tests 162/162.
- **Sibling/adjacent cases checked:** Both `Install-StartupRestore.ps1` and `Restore-StartupSetup.ps1` are covered by the new class-level check.
- **References (SHA/PR/issue/log):** failed relay result `chatgpt-startup-setup-restore-heaven2-20260929-1819`; fix `f78ac9337b6df935170bfcf62b726cd003d16896`; regression `989bebbb567cb98bffd4f41e206e09ea4c9878af`.

## 2026-09-29 — Agent Control scheduler — global quiescence gate stranded failed lanes
- **Symptom:** A failed, interrupted, or orphaned worker could remain unfinished while unrelated workers in the same swarm were still healthy and running; replacement work was deferred until the entire swarm drained.
- **Root cause:** The recovery planner and reconciler both applied a global active-wave gate before recovery planning, making recovery depend on swarm-wide quiescence rather than the failed lineage's state.
- **Violated invariant / wrong assumption:** Failure recovery is a per-lineage continuity concern. One lane dying must not wait for unrelated healthy lanes to finish before a bounded replacement can resume its unfinished work.
- **Why prior defenses missed it:** Existing regression coverage explicitly encoded end-of-swarm-only recovery and did not exercise the mixed state where one lane is dead while another remains active.
- **Direct fix:** Recovery planning now runs while the wave is active; failed/interrupted/orphaned roots can be claimed immediately, active recovery workers consume the configured recovery-pool budget, one lineage retains one active recovery owner, and the wave-completion gate runs only after recovery dispatch has been considered.
- **Preventive rule/process change:** Multi-agent schedulers must test partial-wave failure. Recovery admission must be scoped to the failed lineage and bounded global recovery capacity, never blocked solely because unrelated workers remain active.
- **Regression coverage added/strengthened:** Added tests proving mid-wave failed-lane replacement, orphaned-lane eligibility, recovery-pool slot accounting, and server ordering that evaluates recovery before wave completion.
- **Verification evidence/environment:** Changes and regression sources are committed on canonical main. Runtime Node tests could not be executed in this chat because the legacy Desktop Commander channel was quota-blocked and the direct main commits had no attached CI runs; no runtime-pass claim is made here.
- **Sibling/adjacent cases checked:** Preserved existing retry lineage limits, successful/exhausted-lineage suppression, provider-capacity exclusion, operator-stop handling, branch/worktree evidence preservation, and direct non-Work execution.
- **References (SHA/PR/issue/log):** 120c8ecadf8f65dbcd1404a96195f2f4b3fd74cd, a273bab9108456d3aa8f7cbe5eff24bd76d4d976, 8b5fa46abb2992adf65afc04f07ee41d8aa724fc, 4e9f1d1cb8ae08ebef95f2c8ebc65eba42b71641, 8aad70de686920cd02a3c7f7ca1dac7bc04e70e4.
