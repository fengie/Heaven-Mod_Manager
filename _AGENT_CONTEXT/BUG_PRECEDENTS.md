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
