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

## 2026-09-29 — Agent Control startup — internal control modes made the advertised one-click swarm path operator-hostile
- **Symptom:** Opening Agent Control still confronted the operator with autonomy mode, pause/read-only/drain/emergency state, raw routing-manifest JSON, multiple quick presets, and an explicit-routing freshness requirement before the normal swarm could run. From an empty controller, the advertised 1 Manager + 1 Primary + 8 Support plan could also exceed the default eight-worker capacity preflight.
- **Root cause:** Internal safety/coordination mechanisms were surfaced as normal startup configuration instead of being derived from the explicit operator intent to start. The generic automatic `usual-swarm` route was reused directly by the button, so its fail-closed routing requirement leaked into manual UX. Separately, the default swarm topology drifted beyond the controller's own default capacity.
- **Violated invariant / wrong assumption:** A first-class one-click operator workflow must be executable from its normal startup state without hand-editing internal manifests or modes. Internal fail-closed controls may govern automatic/background behavior, but an explicit operator Start action must translate the user's intent into the required safe runtime state. Default launch topology must fit default capacity.
- **Why prior defenses missed it:** Earlier regression coverage asserted that a swarm button existed, but did not assert that it was the sole primary startup action, that raw controls were hidden from the normal path, that its server route self-normalized ordinary control friction, or that the default topology fit capacity.
- **Direct fix:** Added a dedicated `POST /api/swarm/start` operator route. It moves a healthy controller to `coordinate`, clears pause/read-only/drain/emergency-stop state, and runs `usual-swarm` as an explicit operator start without requiring the operator to manufacture routing JSON. Automatic/background `usual-swarm` execution still requires fresh reconciled ownership. The dashboard now exposes one large **START SWARM** action in the normal launch surface and moves raw operator/routing/autopilot controls under Advanced / diagnostics. The normal topology is 1 Manager + 1 Primary Programmer + 4 Support lanes, which fits the default eight-worker cap.
- **Preventive rule/process change:** For startup-critical workflows, test the complete first-run path rather than mere control presence. The normal launch surface must have one obvious action; internal modes/manifests belong behind diagnostics; explicit operator intent may normalize reversible operating flags but must not bypass degraded-state, capacity, live-ownership, lease, machine, or authorization checks. Any default topology change must be tested against default controller capacity.
- **Regression coverage added/strengthened:** `operator-ui-cli.test.mjs` asserts exactly one primary startup button and diagnostics containment; `server-safety.test.mjs` covers Start Swarm control normalization while retaining generic automatic routing safety; `control-core.test.mjs` locks the 1+1+4 topology and proves it fits the default capacity.
- **Verification evidence/environment:** Agent Control PR gate and repository CI are required on the exact branch/main SHAs; local Desktop Commander execution was unavailable due its monthly quota, so no local Windows verification is claimed from that provider.
- **Sibling/adjacent cases checked:** Existing advanced autonomy, routing, pause/resume, drain, emergency-stop, per-role deployment, autopilot, and CLI controls remain available; server-side authorization remains authoritative; generic/background workflows retain the fresh-routing fail-closed rule.
- **References (SHA/PR/issue/log):** See the canonical commit/PR for the one-click Start Swarm startup simplification.


## 2026-09-29 — Agent Control swarm — asynchronous provider failure allowed an entire wave to launch before the circuit opened
- **Symptom:** A one-click Agent Manager swarm could show every lane failed together instead of stopping after the first shared provider/startup failure. The run also lacked one durable structured incident record that made the common cause obvious after the fact.
- **Root cause:** `executeWorkflow` awaited only process creation. `deployOne` returned as soon as each child/bridge runner was spawned, while provider-capacity classification happened later from worker output/termination. The workflow loop therefore launched the next lane before the first lane's asynchronous quota/capacity failure could update the provider circuit. Six fast launches could all escape the pre-dispatch circuit check in the same temporal window.
- **Violated invariant / wrong assumption:** A multi-agent batch that shares one execution provider must re-observe startup/provider health between launches. "Spawn succeeded" is not evidence that the provider accepted useful work, and a shared hard-capacity failure must stop same-wave fan-out.
- **Why prior defenses missed it:** Existing quota tests proved classification, auto-termination, and fail-fast behavior *after* a capacity-blocked record existed. Existing workflow tests proved capacity/lease preflight *before* the first launch. No regression covered the interval where worker 1 has been spawned, worker 1 reports a shared provider failure asynchronously, and worker 2 is about to launch.
- **Direct fix:** Added `waitForWorkflowStartupViability` between multi-step launches. It refreshes worker output/state, lets the existing active-capacity detector open the circuit, and breaks the wave on capacity or terminal startup failure before the next lane launches. Added a structured `data/failures.jsonl` ledger with bounded secret-redacted fields for pre-launch failures, runtime capacity detection, process exit/error, workflow dispatch failure, and fan-out aborts. Snapshot/API surfaces expose recent failure records.
- **Preventive rule/process change:** Any batch launcher over a shared provider must include an inter-launch viability/circuit checkpoint, not only a preflight before the batch. Every terminal controller-owned failure path must emit durable sanitized diagnostic evidence that survives the transient UI/process.
- **Regression coverage added/strengthened:** `tools/agent-control/test/server-safety.test.mjs` now asserts launch → startup guard → stop-fan-out ordering, provider-circuit observation inside the guard, structured failure-ledger coverage across failure phases, redaction, and snapshot/API exposure.
- **Verification evidence/environment:** The change is routed through the Agent Control PR/CI gate before integration. Final merge evidence is recorded in the integrating commit/PR.
- **Sibling/adjacent cases checked:** Existing pre-launch capacity/lease checks remain authoritative; provider-capacity active termination remains the classifier; successful startup still proceeds; single-step workflows are not delayed by an unnecessary next-lane gate; no prompt body, task capability, or environment secret is written to the new ledger.
- **References (SHA/PR/issue/log):** implementation branch `agent-control-swarm-failure-log-20260929`; integration PR/merge SHA to be filled by canonical history.

## 2026-09-29 — GitHub Releases catalog — escaped newline token was written into C# test source
- **Symptom:** A newly added provider regression test contained the literal characters `\n` between two C# statements instead of an actual line break, making the test project syntactically invalid before semantic verification could begin.
- **Root cause:** A source-edit/generation path serialized a line break as text and the resulting file was committed without a syntax/build check of the changed C# project.
- **Violated invariant / wrong assumption:** Source-producing edits are not complete when the text looks structurally plausible in a patch. The emitted file must be valid source in the target language before it is presented as an integration candidate.
- **Why prior defenses missed it:** Review focused on provider behavior, compliance, schema drift, auth secrecy, and download boundaries. The branch had not yet passed a compile gate, and no pre-PR source-generation check caught escaped control-token artifacts.
- **Direct fix:** Replaced the literal escape token with a real newline in `GitHubReleasesCatalogProviderTests.cs` and re-routed the branch through the normal build/test gate.
- **Preventive rule/process change:** After any programmatic source rewrite, inspect the final emitted file rather than only the transformation input, and run the narrowest native syntax/build check before opening or declaring a PR ready. Treat visible serialized control tokens such as stray `\n`, `\r`, or escaped quote artifacts between statements as a source-generation defect class.
- **Regression coverage added/strengthened:** The authoritative C# build in the workflow feature gate remains the mechanical closure for this class; source-producing agents must run an equivalent focused compile before handoff when execution is available.
- **Verification evidence/environment:** GitHub Releases provider branch `agent/github-releases-catalog-20260929b`; fix commit `1dc71a9279162dff35e5eab2d3bc77ae73ecc430`; final merge evidence belongs to the integrating PR.
- **Sibling/adjacent cases checked:** Review the rest of generated/edited C# in the provider lane for escaped control-token artifacts and require the branch gate before integration.

