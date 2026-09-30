### 2026-09-30 — Release gate evidence — old success could mask a failed or running result
- **Symptom:** Heaven Workflows release_verify_gates returned ok for success + failure/cancelled/queued rows on the same exact commit, and for an in-progress row with a success conclusion.
- **Root cause / invariant:** Existential any-success aggregation discarded competing evidence and authoritative lifecycle status. A required gate needs one proven current result; unknown/partial/conflicting evidence cannot authorize publication.
- **Why defenses missed it:** Existing cases covered wrong commits, artifacts and confirmation, but not duplicate histories, pending reruns, status/conclusion conflicts or freshness at authorization. This repeats LR-054 weakest-evidence aggregation and strengthens enforcement.
- **Reproduction:** Added regressions failed four assertions against canonical c0637afa (old success plus failure/cancelled/queued; in-progress plus success), recorded in ignored BuildLogs/v8.8.42-workflow-repro.log.
- **Fix / prevention:** Reject ambiguous same-gate rows and incomplete/running statuses; collect bounded exact repo/SHA/event/branch Actions evidence via an authorized reader, retain the newest unambiguous workflow result, disclose incomplete pagination, expire snapshots and revalidate them at publication authorization.
- **Regression / verification:** Heaven Workflows 38/38 local tests pass, including provider failure redaction, pagination/count changes, latest pending/failure/recovery, scope mismatch, expiry, duplicate identities, oversized data and stale/tampered publication proof. Full toolbox and exact-head CI/runtime API evidence follow in EVIDENCE/v8.8.42-ci-evidence.md.
- **Sibling checks / propagation:** Existing exact source/artifact/plan/confirmation rules retained; cancellations/publication remain external mutations. Duplicate workflow names fail closed. GitHub main-run connector omission is handled by the existing Heaven Workflows owner, PG-005 adapter/contract, not a duplicate credential/transport stack. LR-056 and trainer README propagate the invariant.

### 2026-09-30 — Continuity negative fixtures — policy prose changes could leave mutations ineffective
- **Symptom:** The comment-hidden learned-rules fixture no longer removed the active requirement after AGENTS compacted its optional backticks; another Core Rule fixture depended on exact old wording.
- **Root cause / violated invariant:** Adversarial fixture mutations were coupled to formatting and did not assert that input changed. A no-op cannot prove validator resistance.
- **Why previous checks missed it:** Existing baseline tests used the original prose and lacked a mutation-effect guard.
- **Fix / prevention:** Accept optional backticks/concept-equivalent Core Rule phrasing and fail any negative fixture that leaves input unchanged; keep semantic rejection assertions intact.
- **Regression / evidence:** heaven2 handoff baseline and all 11 negative fixtures pass on the v8.8.41 candidate. Existing recursive, comment-hiding, read-order and version defenses remain unchanged.
- **Sibling cases / propagation:** Reviewed every mutation, strengthened the common harness, retained full policy history and propagated LR-055/byte-budget lesson. PR/head and full exact-gate evidence are recorded in EVIDENCE/v8.8.41-scalable-bootstrap.md.

### 2026-09-30 — Agent startup — indexed bootstrap still carried oversized mandatory history
- **Symptom:** v8.8.40 progressive startup still required roughly 120 KB of core text per ordinary worker and offered an index without bounded operational retrieval.
- **Root cause:** Core selection measured filenames rather than bytes; AGENTS/current revision accumulated repeated policies and historical candidate state. Deployment independently fetched the same remote twice.
- **Violated invariant:** Current truth and startup obligations must remain bounded, with complete historical knowledge accessible selectively; cached refs must never imply refreshed remote truth or inherited verification.
- **Why defenses missed it:** Previous tests pinned the core/index split and prompt wording, without enforcing aggregate byte budgets or exercising an actual retrieval CLI/API.
- **Corrective fix:** Compact entrypoint/revision, preserve complete prior documents in indexed policy/history, add expiring bounded bootstrap and hash-checked context pagination, and reuse the successful branch-inventory fetch.
- **Preventive rule/process:** Enforce 64 KiB aggregate core and 32 KiB packet budgets. Require hash/freshness/source checks, whitelisted local documents, disclosed partial ownership, explicit cached-ref labeling and risk-matched remote/lease refresh before mutation.
- **Regression coverage:** repository-bootstrap.test.mjs exercises real Git/CLI/HTTP, expiration, wrong head/role, changed/missing/empty/linked documents, pagination/UTF-8 bounds, large ownership and budget overflow. Full Agent Control, handoff negative fixtures and exact-head CI remain required.
- **Verification evidence/environment:** heaven2 focused bootstrap/training tests 15/15 passed; full-suite and deployment/release results will be appended in EVIDENCE/v8.8.41-scalable-bootstrap.md. No candidate inherits v8.8.40 release proof.
- **Sibling checks:** Manager-specific core included in budget; read-only endpoint uses existing Host/Origin guard and does not fetch/provider-scan; worker launch rejects mismatched reused-branch source; current state consumers retain version/priority/verification fields; prior policy and revision retained.
- **References:** branch codex/scalable-agent-bootstrap, base 6f3dd534757697bca7d2464ca7db4e9322ce763c; v8.8.40 archival source unchanged. Generic trainer and LR-055 propagate enforcement.

### 2026-09-30 — Agent Control retirement — remote execution could outlive local-wrapper retirement proof
- **Symptom:** A retry-exhausted Heaven Bridge worker could be removed from the live registry while its durable remote job was still queued/unclaimed, running, or otherwise unproven; stale provider observations could also resurrect a retired worker.
- **Root cause:** Retirement treated local-wrapper lifetime and ambiguous remote statuses such as `not_running` / generic non-`running` as sufficient stop evidence, while tombstone reactivation accepted a live-looking state without requiring monotonic raw heartbeat evidence newer than retirement.
- **Violated invariant / wrong assumption:** Local relay-wrapper death is not remote execution death, and a cached live state is not a new session. Registry retirement/reactivation must move monotonically with authoritative remote job and raw heartbeat evidence.
- **Why prior defenses missed it:** v8.8.26 tests covered local PID/worktree/divergence cleanup and terminal replay, but not queued-unclaimed Heaven jobs, running races after cancellation, status ambiguity, or stale live-state heartbeat replay.
- **Direct fix:** Accept only explicit processed remote terminal states (`completed|done|failed|error|timeout|cancelled`), re-cancel running races, fail closed on queued/unknown/unrecognized/cancel/status-authority failures, and require a raw explicit live heartbeat strictly newer than `retiredAt` before tombstone reactivation.
- **Preventive rule/process change:** Multi-hop retirement must close every execution layer with positive terminal proof; tombstone resurrection requires monotonic lifecycle evidence before normalization.
- **Regression coverage added/strengthened:** `registry-retirement.test.mjs` covers terminal whitelist, queued `not_running + unknown`, running re-cancel, cancellation/status authority failures, and stale/equal/missing/invalid/newer heartbeat cases.
- **Verification evidence/environment:** Exact-head Agent Control source/tests are required for v8.8.29; real heaven2→heaven1 retirement smoke remains required before runtime closure.
- **Sibling/adjacent cases checked:** v8.8.26 local ownership/dirty/diverged guards and v8.8.28 relay auto-discovery remain intact.
- **References (SHA/PR/issue/log):** issues #458 and #469; branch `fix/agent-control-remote-retirement-proof-20260930`.

### 2026-09-30 — Agent Control dashboard — agent cards were display-only despite interactive presentation
- **Symptom:** Clicking the body of a managed or federated bot card appeared to do nothing. Operators had to discover a small nested action such as **View log**, while backend/notification inspection concepts suggested the card itself should be actionable.
- **Root cause:** The dashboard rendered each bot as a plain `<article class="agent">` with nested buttons only. There was no card-level mouse/keyboard handler, and `showLog(id, button)` assumed a button object was always the caller.
- **Violated invariant / wrong assumption:** An operator surface that visually presents an entity as an actionable card must provide a real, keyboard-accessible entity action; nested controls must remain independent and must not be the only discoverable path to inspection.
- **Why prior defenses missed it:** Existing UI tests checked button markup, API routes, lifecycle state, and selected action wiring, but did not parse/pin card-body activation or keyboard behavior.
- **Direct fix:** Add inspectable managed/federated card semantics, mouse + Enter/Space activation, optional-button log inspection, linked-managed focus for federated sessions, external-session detail expansion, and a guard that ignores nested controls.
- **Preventive rule/process change:** For every clickable-looking entity/card, regression-test both the primary card action and nested control isolation. Generated inline JavaScript must parse as emitted, not only look valid in template source.
- **Regression coverage added/strengthened:** `operator-ui-cli.test.mjs` parses the emitted inline script and requires managed/federated card handlers, keyboard keys, nested-control exclusion, and optional-button log inspection.
- **Verification evidence/environment:** Exact-head Agent Control source gate and heaven2 live card-interaction smoke are required for v8.8.27; no live UI claim should be inferred from source alone.
- **Sibling/adjacent cases checked:** Existing **View log**, **Stop**, **Deploy reviewer**, and **Copy branch** actions remain nested controls and are excluded from card-level activation; retry-exhausted registry retirement remains owned by v8.8.26 lifecycle code.
- **References (SHA/PR/issue/log):** follow-up branch `fix/agent-control-card-inspection-20260930`; final PR/SHA to be recorded after integration.

### 2026-09-30 — Agent Control registry — terminal retry state was treated as a label instead of retirement
- **Symptom:** Agents remained visible as `failed · RETRY EXHAUSTED` after bounded recovery ended, and the federated registry could keep/recreate those dead entries on subsequent refreshes.
- **Root cause:** Retry exhaustion only changed `recoveryStatus` / task status and returned. `refreshState()` then synchronized every managed agent, including terminal failures, back into federation. There was no terminal retirement lifecycle, tombstone, or replay suppression.
- **Violated invariant / wrong assumption:** A terminal recovery decision is not complete until live ownership, process/worktree/lease cleanup, registry membership, and durable history are reconciled together. Marking a dead entity terminal must not leave it in the live registry.
- **Why prior defenses missed it:** Existing no-work tests proved bounded retry classification/backoff, while federation tests proved freshness/counting. Neither tested the cross-layer transition from final retry exhaustion to process cleanup and registry removal.
- **Direct fix:** Add retry-exhausted retirement with ownership-proven termination, dirty/diverged-work fail-closed preservation, clean worktree/lease release, managed/federated removal, durable tombstones, terminal replay suppression, and live-heartbeat reactivation.
- **Preventive rule/process change:** Every terminal lifecycle state must define both durable history semantics and live-registry retirement semantics. Registry synchronization must explicitly exclude retired terminal entities.
- **Regression coverage added/strengthened:** `registry-retirement.test.mjs`, federated anti-resurrection coverage, control-state migration assertions, and exact Agent Control package checks.
- **Verification evidence/environment:** Exact-head source/Node gate required on this candidate; heaven2 live retirement smoke remains required before runtime closure.
- **Sibling/adjacent cases checked:** Successful `done` integration candidates remain preserved; provider-capacity and uncertain ownership remain fail-closed; external terminal replay can reactivate only on a genuine live state.
- **References (SHA/PR/issue/log):** v8.8.26 implementation branch `fix/agent-registry-retirement-20260930`; final PR/SHA to be recorded after integration.

## 2026-09-30 — One-click control actions must reconcile lifecycle state, not just the enabled bit

**Failure:** Agent Manager's primary **START SWARM** action treated any enabled autopilot as an already-running perpetual swarm. A paused perpetual run therefore produced a success-looking “already running” message while the scheduler intentionally did nothing; an enabled non-perpetual run was also mislabeled as perpetual. The function additionally normalized control/safety settings before validating an empty objective.

**Prevention rule:** Primary one-click controls must classify the authoritative lifecycle state and mode before reporting success or mutating unrelated safety/control state. For start/resume controls, distinguish at minimum: invalid request, conflicting active mode, paused resumable run, already-running same mode, and fresh start. Regression coverage must prove both state-transition behavior and operator-visible messaging.

**Guard:** `tools/agent-control/test/server-safety.test.mjs` asserts paused-run resume, explicit mode conflict, and validate-before-mutate ordering; `tools/agent-control/test/operator-ui-cli.test.mjs` protects the resumed-state dashboard message.

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

### 2026-09-29 — conflict/deployment — grouping and priority were treated as overwrite authority
- **Symptom:** The resolver could silently choose a same-family texture by configured priority, and exact-path planning could combine unrelated structural siblings in one atomic MHW asset bundle without observing a direct collision. Manual deployment/launch also did not re-run the dependency doctor at the final user-facing gate.
- **Root cause:** Family grouping, file-path collision detection, overwrite precedence, and dependency validity were implemented as adjacent concerns instead of independent safety proofs. A non-blocking resolver result also retained a priority fallback if it failed to name a candidate winner.
- **Violated invariant / wrong assumption:** Family membership and priority are not proof of overwrite direction; structural model/material/physics siblings are atomic even when filenames differ; every normal deployment/launch must prove enabled requirements immediately before proceeding; a non-blocking multi-provider decision must identify a real winner.
- **Why prior defenses missed it:** Existing tests emphasized exact same-path conflicts, deterministic family priority, and Auto Populate dependency closure. They did not adversarially cover disjoint files inside one structural bundle, malformed winner edges, ambiguous family texture siblings, or manual Apply/Launch dependency revalidation.
- **Direct fix:** Added enabled-rule schema validation, fail-closed winner invariants, high-confidence-only texture auto-selection, atomic structural-bundle checks, and dependency gates for Preview/Apply/modded launch/last-known-good restore.
- **Preventive rule/process change:** Conflict resolution must separate identity/grouping from precedence proof. Never use priority as an emergency safety fallback. Never compose an atomic structural bundle across providers without a complete overlay chain or strong one-main/dependent-family proof. Normal mutation/launch paths must revalidate dependencies at the boundary.
- **Regression coverage added/strengthened:** Added malformed-overlay, disjoint structural-sibling, proven main+optional bundle, ambiguous same-family texture, and randomized no-guess texture invariants.
- **Verification evidence/environment:** Reconciled implementation is on PR #369 from `integration/override-dependency-safety-currentmain-20260930-chatgpt`; Workflow Feature PR Gate / Windows CI evidence must be recorded here before merge.
- **Sibling/adjacent cases checked:** Exact winners, explicit incompatibilities, overlay cycles, dedicated/newer texture providers, Auto Populate closure, last-known-good restore, and crash-diagnosis subset behavior were reviewed. Crash-diagnosis probes remain intentionally exempt from dependency completeness because incomplete subsets are the diagnostic mechanism.
- **References (SHA/PR/issue/log):** integration work begins at `942193747c66`; final PR/CI pending.


## 2026-09-30 — Agent Control dashboard — operator affordances diverged from server contract
- **Symptom:** Backend-ranked manager recommendations existed but were invisible in the dashboard; non-stoppable managed terminal/failure states could advertise Deploy reviewer even when review was invalid; and Resume left an active emergency-stop latch set.
- **Root cause:** The dashboard duplicated server state/action semantics with hand-written presentation fallbacks instead of closing the UI contract over `suggestedActions`, review eligibility, and emergency-stop recovery state.
- **Violated invariant / wrong assumption:** A control surface must not hide actionable authoritative decisions or offer an action the server will reject; a control labeled Resume must either restore dispatch or explicitly state why the safety latch remains.
- **Why prior defenses missed it:** Backend tests pinned recommendation generation and capacity-blocked review refusal, while UI tests mostly checked route presence and broad lifecycle controls rather than state-valid affordances and end-to-end action reachability.
- **Direct fix:** Render `suggestedActions` with supported action buttons, restrict reviewer deployment to `done` agents, and make emergency-stop clearing state-aware, explicit, and confirmed.
- **Preventive rule/process change:** Treat operator action availability as a cross-layer contract: for every server-generated recommendation and state-dependent mutation, prove the UI either exposes the valid action or intentionally explains why no action exists.
- **Regression coverage added/strengthened:** `operator-ui-cli.test.mjs` pins recommendation rendering/action routing, done-only review affordance, and emergency-stop-aware Resume semantics.
- **Verification evidence/environment:** Canonical v8.8.24 tree contains the functional regression markers and its assembled inline JavaScript parses under V8; exact-head Node/Heaven runtime verification remains separately scoped.
- **Sibling/adjacent cases checked:** Stop eligibility remains pinned by LR-045; capacity-blocked review remains rejected server-side; unsupported recommendation types remain informational rather than guessed.
- **References (SHA/PR/issue/log):** PR #452 / merge `abafcda17e0614e2d8f0e095a802d1116bc751b2`.

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


## 2026-09-29 — Workflow Feature PR Gate — per-PR concurrency starved release-critical updater proof
- **Symptom:** Installed-client updater E2E run `36659154949` remained queued while newer Workflow Feature PR Gate runs repeatedly claimed both shared Heaven runners (`heaven` and `heaven-v2`). Many superseded feature runs cancelled only after consuming scheduling/runner capacity.
- **Root cause:** The feature gate used `group: workflow-feature-pr-gate-${{ github.ref }}`. `cancel-in-progress: true` therefore collapsed repeated pushes only within one PR; separate PRs retained separate concurrency groups and could fan out across the same two-runner pool.
- **Violated invariant / wrong assumption:** Concurrency scope for supersedable CI must match the scarce resource boundary. A per-PR cancellation key is insufficient when all PRs contend for one small shared self-hosted runner pool and release-critical proof uses that pool too.
- **Why prior defenses missed it:** CI noise controls were evaluated per branch/run, not at the shared-runner scheduling level. The workflow looked well-behaved in isolation because each PR cancelled its own stale runs, while cross-PR churn still created an effectively unbounded queue.
- **Direct fix:** Collapse Workflow Feature PR Gate runs into one repository-wide `workflow-feature-pr-gate` concurrency group with `cancel-in-progress: true`. New feature validation supersedes older feature validation regardless of PR, while release-critical workflows keep separate concurrency groups.
- **Preventive rule/process change:** For shared self-hosted capacity, define concurrency groups by operational priority/resource class rather than by branch unless simultaneous branch validation is intentionally provisioned. Supersedable feature verification must not be allowed to starve release/update/rollback proof.
- **Regression coverage added/strengthened:** `tools/agent-control/test/workflow-feature-pr-gate-concurrency.test.mjs` asserts the global concurrency key, cancellation flag, and absence of the old per-ref key.
- **Verification evidence/environment:** The change touches only the feature workflow plus governance/test files, which are release-irrelevant under `UpdaterReleasePolicy.ps1`; it does not trigger Windows Release Gate and does not invalidate exact-source updater E2E evidence.
- **Sibling/adjacent cases checked:** Windows Release Gate and Updater Installed Client E2E retain their own concurrency groups; their source/evidence pinning is unchanged.
- **References:** updater E2E run `36659154949`; competing feature-gate runs included `36659334396`, `36659186555`, `36659543467`, and `36659596149`.

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

## 2026-09-29 — GitHub Releases catalog — credentials and acquisition were not fully bound to canonical identity
- **Symptom:** The provider transport accepted an arbitrary HTTPS API base URI even when a GitHub bearer credential was configured, and direct acquisition validated provider/mod identity without also proving that the catalog mod belonged to the selected game.
- **Root cause:** Testability hooks and provider-neutral request objects were treated as harmless configuration after basic HTTPS/provider checks, but neither boundary carried the complete trust identity needed by the sensitive action.
- **Violated invariant / wrong assumption:** Secrets must be bound to their canonical egress origin, and download/acquisition authorization must validate provider + game + mod + file identity before resolving a direct artifact.
- **Why prior defenses missed it:** Existing coverage rejected non-HTTPS bases and mismatched provider/file IDs, but did not combine a credential with a custom HTTPS origin or mutate only the game identity while keeping the repository/file identity otherwise valid.
- **Direct fix:** Credential-bearing GitHub transports now reject non-`api.github.com` base origins before any request is created; direct acquisition now requires `request.Mod.GameId == request.Game.Id`.
- **Preventive rule/process change:** Treat injectable network origins and provider-neutral artifact references as trust boundaries. Any secret-bearing transport must pin secret egress to the intended origin, and any acquisition/install resolution must validate the full identity tuple before producing a usable download.
- **Regression coverage added/strengthened:** Added tests proving custom HTTPS origins cannot receive GitHub credentials and cross-game catalog mods cannot resolve direct release assets.
- **Verification evidence/environment:** GitHub Releases provider PR #322, commits `1a9f364edc5dd898c897576e49d416f96edecc50`, `00c292ca75cf63116ab1919acbf1582df1aa9710`, and `10eb572bfbb8cbbc3c85b48c725caf8712da809e`; exact-head PR gate remains authoritative.
- **Sibling/adjacent cases checked:** Nexus assisted acquisition already enforces provider/game/mod identity; GitHub asset URLs remain restricted to HTTPS `github.com`; credentials are not persisted in provider metadata.

## 2026-09-29 — GitHub Releases catalog — successful zero-budget response could still fan out another request
- **Symptom:** Curated multi-repository discovery consumed the authoritative rate-limit headers on each release response but did not use a successful response with `X-RateLimit-Remaining: 0` to stop the next repository request.
- **Root cause:** Rate-limit data was modeled as observability returned by the transport, not as an execution gate for the provider's own sequential fan-out.
- **Violated invariant / wrong assumption:** A successful request is not permission for another request when the provider's authoritative response budget says none remain.
- **Why prior defenses missed it:** Existing tests covered explicit 429/403 failures and the separate rate-limit status endpoint, but not the boundary where the final permitted request succeeds and exhausts the budget.
- **Direct fix:** Curated discovery now stops after processing a response whose parsed core hourly remaining count is zero.
- **Preventive rule/process change:** Any loop that fans out calls under a shared external quota must re-observe authoritative quota/circuit state between calls and stop before knowingly crossing the budget.
- **Regression coverage added/strengthened:** Added a two-source discovery test whose first successful response reports zero remaining and proves the second source is never queried.
- **Verification evidence/environment:** GitHub Releases provider PR #322, commits `3016f6936d7473f18bfa03cf37390152b6451df4` and `32565e58c15524ff89cbeaad9ccf26933b50e951`; exact-head gate remains authoritative.
- **Sibling/adjacent cases checked:** Explicit rate-limit failures remain non-retrying; the status endpoint reads `resources.core`; one-shot detail/acquisition calls do not contain provider-owned fan-out loops.

## 2026-09-29 — GitHub Releases catalog — concrete collection was stored behind a slower interface and failed strict CA1859

- **Symptom:** The frozen GitHub Releases integration candidate failed the strict whole-solution gate because the curated source array was stored as `IReadOnlyList<GitHubReleaseCatalogSource>`, triggering CA1859 under warnings-as-errors.
- **Root cause:** Construction always materialized `sources` with `ToArray()`, but the field retained the broader interface type even though no alternate implementation was required.
- **Violated invariant / wrong assumption:** Strict-analyzer repositories require concrete hot-path/internal collection types when the implementation is fixed and abstraction provides no behavioral value.
- **Direct fix:** Store the already-materialized curated sources as `GitHubReleaseCatalogSource[]` and use `Length` for the empty-source check.
- **Preventive rule/process change:** Before integration handoff, inspect newly introduced private collection fields for CA1859 candidates: if construction always yields one concrete collection type and substitution is not part of the design, keep the concrete type internally.
- **Regression/verification:** Rerun the exact Heaven repository verification gate on the amended frozen integration head; do not treat downstream missing-assembly/test-executable errors from the failed Core build as independent defects.
- **Reference:** PR #323, failing Heaven run 36659334396.

## 2026-09-29 — Catalog integration — cancelled provider gates reached canonical main

- **Symptom:** GitHub Releases #327 and Nexus #330 reached canonical `main` without successful exact-head feature gates; GitHub still contained verifier/analyzer/xUnit compile defects, while GameBanana introduced the same public-getter trace-gap class.
- **Root cause:** Integration races treated cancelled verification as sufficient evidence and replacement branches did not reconcile known fixes from superseded lineages.
- **Violated invariant / wrong assumption:** A cancelled, pending, superseded, or older-SHA run is never merge evidence. Canonical integration requires a successful gate for the exact candidate head.
- **Direct fix:** Apply one atomic repair commit on the newest canonical main, preserve concurrent provider behavior fixes, close the GitHub compile/analyzer gaps, and trace all newly introduced provider/capability getters before running one combined gate.
- **Preventive rule/process change:** Bind merge decisions to exact SHA + successful required gate immediately before merge. Reconciled branches must carry every known defect fix from branches they supersede.
- **Regression/verification:** The atomic repair PR must pass the exact Heaven feature gate across the fully integrated provider set before merge.
- **Sibling/adjacent cases checked:** SQLite/FTS #325 did pass run 36659776837 before merge. GameBanana assisted-page fix #333 is preserved. Nexus provider entrypoints already contain first-statement traces on current main.
- **Reference:** #327, #330; cancelled runs 36660058628 and 36660257258; successful storage run 36659776837.

## 2026-09-29 — CI/release supply chain — mutable tools and fork PRs crossed privileged self-hosted trust boundaries

- **Symptom:** Security review found movable Action tags, persistent self-hosted PR jobs without a fork trust guard, no explicit transitive NuGet audit policy, and a write-capable release job that could execute arbitrary preinstalled `gh.exe` or a moving latest download.
- **Root cause:** CI trusted repository conventions and upstream mutable labels instead of enforcing immutable executable inputs and explicit trust boundaries.
- **Violated invariant:** A privileged or persistent runner must treat every executable input as part of its attack surface. “Official tool,” “private repository,” and “HTTPS” do not make mutable code immutable or fork PR code safe on a persistent machine.
- **Direct fix:** Pin Actions by SHA; reject fork PR execution on self-hosted jobs; disable persisted checkout credentials; pin and SHA-256 verify the release CLI; enable direct/transitive NuGet auditing and Dependabot; add a reusable CI security policy; complete end-to-end HMAC signing for Heaven Bridge hardened mode.
- **Preventive rule/process change:** The security policy is executed both as a dedicated workflow and by authoritative release verification; reusable lessons are codified in agent training.
- **Residual risk:** Main branch protection/rulesets, ephemeral runner isolation, and a trusted Windows publisher signing identity require repository/admin or identity configuration outside ordinary source changes.

## 2026-09-29 — generated CI policy — JavaScript replacement token corrupted PowerShell regex

- **Symptom:** Security Supply Chain Gate run `36662225529` failed before policy evaluation with PowerShell parse errors; the generated file had a truncated `persist-credentials` regex and a duplicated tail.
- **Root cause:** JavaScript `String.replace` treated PowerShell's regex-ending `$'` sequence as a JavaScript replacement token rather than literal source.
- **Direct fix:** Regenerate the policy from the known-good base using replacement callbacks so replacement text is literal.
- **Prevention:** Cross-language generated source must use literal-safe/AST-safe generation and must be syntax-checked by the target runtime before it is accepted as security evidence.

## 2026-09-29 — mod.io transport strict-build regression — unresolved calls cascaded into analyzer/test failures

- **Symptom:** The authoritative Windows release verifier failed with `CS1717`, `CS1503`, and `CA1822` in `ModIoTransport.cs`, then downstream tests could not start because build outputs were missing.
- **Root cause:** The constructor assigned the `baseUri` field to itself after an unqualified assignment, and `GetModsAsync` passed a mutable `List<(string Key,string Value)>` into a helper requiring an array.
- **Direct fix:** Assign `this.baseUri = NormalizeBaseUri(apiBaseUri)` directly and convert the query list with `ToArray()` at the helper boundary.
- **Prevention:** Treat strict compile/analyzer failures as primary errors; fix the first compiler/type errors before interpreting cascaded analyzer or missing-binary test failures. Prefer direct field assignment and explicit collection-shape conversion at stable API boundaries.
## 2026-09-29 — Mod.io transport rebase — constructor self-assignment broke strict build

- **Symptom:** Exact-head verification for PR #359 failed in the strict whole-solution build with `CS1717` at `ModIoTransport.cs:82`: the normalized base URI field was assigned to itself.
- **Root cause:** Integration/transplant code split normalization into `baseUri = NormalizeBaseUri(apiBaseUri)` followed by `this.baseUri = baseUri`; because no local `baseUri` existed, both references resolved to the field and the second statement became a self-assignment.
- **Violated invariant / wrong assumption:** Constructor field initialization must be unambiguous under the repository's warnings-as-errors analyzer profile; visual similarity to a local-variable handoff is not evidence that a local exists.
- **Direct fix:** Assign the normalized value directly with `this.baseUri = NormalizeBaseUri(apiBaseUri);`.
- **Preventive rule/process change:** After source transplants/rebases, run the exact strict compile/analyzer gate before merge and inspect constructor/member assignments for field/local shadowing or self-assignment. Treat the first compiler/analyzer diagnostic as the root failure; downstream missing binaries/tests are cascade noise until it is fixed.
- **Regression/verification:** The corrected transport implementation is present on canonical `main`; future merges remain blocked on exact-head required gates.
- **Reference:** PR #359, Heaven Workflow Feature PR Gate run 36662934090.

## 2026-09-29 — CI security policy — partial text edit corrupted the security verifier itself
- **Symptom:** Canonical `main` contained a malformed `scripts/Test-CiSecurityPolicy.ps1`: a regex/string was truncated, later statements were spliced into it, and a duplicate block appeared after the final PASS line.
- **Root cause:** A security-policy edit was integrated as fragile partial text surgery without proving the final script parsed and executed as one coherent file.
- **Violated invariant / wrong assumption:** Security controls are production code. A scanner that cannot parse is not protection, even if the intended rules are correct.
- **Why prior defenses missed it:** The policy script changed concurrently with related workflow hardening, and integration evidence did not prove the exact final canonical file passed a syntax/runtime gate before merge.
- **Direct fix:** Replace the file from a clean complete implementation, preserve all intended action/runner/permission/NuGet/release-tool invariants, and make the dedicated supply-chain workflow run that reusable script.
- **Preventive rule/process change:** Never patch security-policy source by unverified substring splicing. Parse/execute the exact final policy artifact and trigger its gate when the policy file itself changes.
- **Regression coverage added/strengthened:** Security Supply Chain Gate includes the policy script in both push and pull-request path triggers and executes it directly; authoritative release verification also runs it.
- **Verification evidence/environment:** Branch `security/hardening-20260929-r4`; exact workflow/policy verification is required before integration.
- **Sibling/adjacent cases checked:** `Verify-Release.ps1`, the security workflow path filters, workflow action pins, self-hosted PR guards, persisted checkout credentials, write scopes, NuGet audit properties/overrides, and pinned GitHub CLI bootstrap invariants.
- **References:** issue #350 tracks the independent updater-signing follow-up.

## 2026-09-29 — dependency auditing — command-line flags silently overrode secure repository defaults
- **Symptom:** `Directory.Build.props` enabled direct/transitive NuGet auditing, but several workflows still passed `-p:NuGetAudit=false`, disabling the control on the exact CI paths meant to validate releases/features/performance.
- **Root cause:** Secure project defaults were treated as authoritative without scanning command-line/property overrides in workflow code.
- **Violated invariant / wrong assumption:** A security property is only enforced if no higher-precedence invocation can disable it.
- **Why prior defenses missed it:** The first supply-chain gate verified the project-level audit settings but did not reject contradictory workflow flags.
- **Direct fix:** Remove every CI `NuGetAudit=false` override and make `Test-CiSecurityPolicy.ps1` fail on future reintroduction.
- **Preventive rule/process change:** Security configuration review must include override precedence: CLI flags, environment variables, job-local settings, and generated config can defeat repository defaults.
- **Regression coverage added/strengthened:** Reusable CI policy scans every workflow for `NuGetAudit=false` in addition to asserting `NuGetAudit=true`, `NuGetAuditMode=all`, and `NuGetAuditLevel=low` in `Directory.Build.props`.
- **Verification evidence/environment:** Branch `security/hardening-20260929-r4`; security workflow plus release verifier must pass before merge.
- **Sibling/adjacent cases checked:** startup-performance, installed-client updater E2E, workflow-feature PR verification, and authoritative release restore.

## 2026-09-29 — mod.io modfile normalizer — root object was treated as a named property

- **Symptom:** Three authoritative core tests failed with `InvalidDataException: mod.io field 'mod.io modfile list response' must be an object` even though the fixture root was a valid JSON object containing a `data` array.
- **Root cause:** `NormalizeModFiles` called `RequireObject(root, context)`, a helper that interprets its second argument as a property name, instead of `RequireObjectValue(root, context)`, which validates the root element itself.
- **Direct fix:** Validate the document root with `RequireObjectValue`; retain `RequireObject` only for actual named child properties such as `download`.
- **Prevention:** Distinguish root-value validators from named-property accessors in parser APIs and keep representative list-response fixtures in the strict release test gate.

## 2026-09-29 — agent execution surfaces — console windows could interrupt the operator desktop
- **Symptom:** Agent-owned `cmd.exe`/PowerShell/helper launches could surface a console window or steal focus on `heaven2`/`heaven`, interrupting unrelated operator activity even though the underlying automation continued successfully.
- **Root cause:** Hidden/background behavior existed on many individual launch sites but was not a single mechanically enforced invariant. Bridge `app_launch` used detached-process semantics without an explicit no-console default, and elevation/recovery PowerShell seams did not uniformly carry `-WindowStyle Hidden`.
- **Violated invariant / wrong assumption:** Successful background automation must also be non-interrupting. Relying on each caller to remember a platform-specific hide flag is not an adequate process-creation contract.
- **Why prior defenses missed it:** Existing tests focused on execution success, persistence, security, and process ownership. They did not reject a new direct `node:child_process` import or assert hidden-window semantics across bridge app launch and PowerShell elevation/recovery seams.
- **Direct fix:** Centralize Agent Control child-process creation behind a non-overridable `windowsHide: true` wrapper; make Heaven Bridge `app_launch` force `CREATE_NO_WINDOW` for `cmd.exe`, `powershell.exe`, and `pwsh.exe` even if a caller requests `visible_console: true` while retaining visible launch support for intentional non-shell GUI apps; keep `-WindowStyle Hidden` on elevation, sentinel, watchdog, startup, and recovery PowerShell paths.
- **Preventive rule/process change:** Agent-owned Windows consoles on both machines are hidden by construction. Any visible console must be an explicit task/user requirement, never an incidental side effect of command execution.
- **Regression coverage added/strengthened:** Bridge unit coverage asserts that `cmd.exe`, Windows PowerShell, and PowerShell Core remain hidden even when generic app-launch visibility is requested, while intentional non-shell GUI visibility still works. Agent Control coverage proves caller options cannot override `windowsHide: true`, rejects runtime direct `node:child_process` imports outside the wrapper, and checks canonical PowerShell startup/elevation/recovery hidden markers.
- **Verification evidence/environment:** Run `heaven-bridge\\manage.ps1 TEST`, Agent Control `npm run check`, Agent Control `npm test`, and `git diff --check` on the exact integration candidate, then deploy canonical bridge/runtime recovery from exact `main` to both `heaven2` and `heaven`.
- **Sibling/adjacent cases checked:** Existing bridge `proc_run`, persistent sessions, raw PowerShell/CMD, Git/taskkill/UIA helpers already use `CREATE_NO_WINDOW`; Agent Control existing worker, Git, and taskkill calls already specified `windowsHide: true`; legacy user-invoked debug consoles and intentionally visible GUI applications are not reclassified as background agent consoles.

## 2026-09-29 — Heaven Bridge bootstrap — scheduled watchdog action missed the hidden-window invariant
- **Symptom:** The background-console hardening initially covered bridge command/session launches, bootstrap elevation, sentinel repair, direct watchdog fallback, and Agent Control children, but the canonical bootstrap-created `Heaven Local Bridge Watchdog` scheduled task still used PowerShell without `-WindowStyle Hidden`.
- **Root cause:** The first regression asserted representative hidden launch sites but did not enumerate every PowerShell task action created by bootstrap. A sibling launch seam remained outside the mechanical policy.
- **Violated invariant / wrong assumption:** A global non-interrupting-console guarantee cannot be proven by spot-checking representative launchers; every agent-owned console-host creation path must be covered.
- **Direct fix:** Add `-WindowStyle Hidden` to bootstrap's scheduled watchdog arguments.
- **Preventive rule/process change:** Hidden-console policy tests must enumerate bootstrap-created PowerShell scheduled actions as well as direct/fallback launchers. When one launch seam is fixed, sibling scheduled-task definitions are part of the mandatory defect-class audit.
- **Regression coverage added/strengthened:** `background-process-policy.test.mjs` now fails if bootstrap's watchdog scheduled action loses `-WindowStyle Hidden`.


## 2026-09-29 — repository reorganization — verification tests kept stale script paths
- **Symptom:** The exact release gate failed after PowerShell scripts were organized into purpose-specific subfolders because integration tests still opened `scripts/Verify-Release.ps1`, `scripts/Test-AgentHandoff.ps1`, and other former root paths.
- **Root cause:** The structural move updated launchers/workflows/docs but did not close all code/test references to moved files.
- **Violated invariant / wrong assumption:** Repository organization is a functional change whenever tests or tooling treat paths as contracts; a move is not complete until every authoritative consumer resolves the new path.
- **Direct fix:** Update integration tests to the canonical `scripts/release`, `scripts/build`, and `scripts/testing` paths.
- **Preventive rule/process change:** Any file/folder move must include caller/reference closure across source, tests, workflows, launchers, docs, and verification scripts before merge. Exact release verification must finish green before structural changes are integrated.
- **Regression coverage added/strengthened:** The existing fail-closed integration tests now read the organized canonical paths, so future drift fails immediately.

## 2026-09-29 — Auto Populate protected anchors — physically shadowed alternatives were still enabled
- **Symptom:** Auto Populate preserved the selected texture as the effective provider but still enabled an unrelated alternative whose entire closure contributed no effective file.
- **Root cause:** The protection check only rejected cases where a non-protected provider became the winner. It did not reject a candidate that lost every path to the protected/current safe set and therefore added no effective content.
- **Violated invariant / wrong assumption:** “The protected mod still wins” is weaker than “fill around the protected selection.” Auto-filled packages that contribute no effective output should not be enabled merely because the planner can resolve their collisions.
- **Direct fix:** Before accepting a candidate closure, require at least one newly added mod to be the effective provider of a planned path; otherwise skip the fully shadowed candidate.
- **Preventive rule/process change:** Auto Populate decisions must be judged on effective output, not only enabled-state conflict freedom. Protected anchors remain meaningful choices and should not accumulate inert/redundant alternatives around them.
- **Regression coverage added/strengthened:** `AutoPopulateFillsAroundExplicitStagedPreference` is the authoritative regression for this case.


## 2026-09-30 — conflict resolver — sequential N-way texture tournament could select a globally inconsistent winner
- **Symptom:** With three or more eligible texture providers, the resolver could automatically select one final provider even when pairwise precedence evidence was cyclic or non-transitive. The outcome was deterministic by provider iteration order but was not proven consistent against every competing provider.
- **Root cause:** `AutoCompatibility.SelectTextureProvider` used a provisional-winner tournament: compare A/B, compare that winner/C, and so on. If C replaced B, C was never rechecked against A. Pairwise preference was implicitly treated as transitive even though revision labels, update signals, and file-time evidence can disagree across different pairs.
- **Violated invariant / wrong assumption:** A file-level automatic override needs one provider that is proven safe against every competing provider. Deterministic ordering is not a substitute for a globally consistent precedence proof.
- **Why prior defenses missed it:** Existing tests covered two-provider decisions, straightforward revision chains, explicit overlays, and unrelated alternatives, but no three-provider cyclic/non-transitive fixture existed.
- **Direct fix:** For two providers, preserve the existing pairwise decision. For three or more, evaluate every provider pair, reject invalid/incomparable pairs, and auto-resolve only if exactly one provider beats all N-1 alternatives. Otherwise return a blocking `ambiguous-texture-precedence`/independent-replacement result instead of guessing.
- **Preventive rule/process change:** LR-036 requires a complete-dominator proof for N-way inferred override decisions and keeps dependency satisfaction, family membership, and overwrite precedence as separate proofs.
- **Regression coverage added/strengthened:** `AutoCompatibilityTests.Multi_provider_texture_precedence_requires_one_complete_dominator` constructs a three-provider non-transitive texture cycle and requires no winner.
- **Verification evidence/environment:** Product/test commits `287d1ec8bc76c21dfc1d50e494dfd05d5051f2aa` and `ccf1f1c4525464d396ba19a9dcdc07a74086b991` were merged by PR #394 as `b90e79acc487366f475edf31916337d57bbea59d`. Current-main exact verification remains required below because PR #394 was integrated before its required gates completed.
- **Sibling/adjacent cases checked:** The shared planner fails closed when a non-blocking decision does not name a valid candidate; same-family textures require High/Explicit texture evidence; normal Preview/Apply/Auto Populate/launch/restore validate requirements at their appropriate boundary; explicit incompatibility, exact-winner, resource-provider, overlay-cycle, protected-bootstrap, trusted-code, and atomic structural-bundle safeguards remain authoritative.
- **References (SHA/PR/issue/log):** PR #394; merge `b90e79acc487366f475edf31916337d57bbea59d`; LR-036.


## 2026-09-30 — integration automation — LR-035 recurred on conflict-resolver PR #394
- **Symptom:** The active conflict-resolver branch was merged and deleted while its owning task was still completing mandatory precedent/training work. At merge time the Workflow Feature PR Gate was still queued and the Security Supply Chain Gate had failed.
- **Root cause:** Integration automation again treated a branch/PR with implementation and tests as harvestable without an explicit owner-ready signal and without requiring every exact-candidate mandatory gate to have completed green.
- **Violated invariant / wrong assumption:** Branch existence, apparent code completeness, or a mergeable PR is not integration authorization. An active owner and pending/failed required checks mean the candidate is not ready.
- **Why prior defenses missed it:** LR-035 existed after the earlier PR #386 incident, but the integration path still lacked an effective mechanical owner-readiness + green-gates barrier.
- **Direct fix:** Strengthen LR-035 so explicit task-owner readiness and completed-successful required gates are mandatory; queued, skipped, cancelled, or failed required checks never authorize harvesting. Continue verification from current main rather than treating the premature merge as evidence.
- **Preventive rule/process change:** Integration/cleanup automation must consume a durable readiness marker/owner handoff and exact candidate gate state before merge/delete. A recurrence of LR-035 is a severity escalation and should be mechanically enforced in Agent Control/integration policy, not left as advisory prose.
- **Regression coverage added/strengthened:** Current-main verification is re-run after the inherited security-policy parser repair; integration automation still needs a dedicated readiness/gate-state regression in its owning control-plane lane.
- **Verification evidence/environment:** PR #394 head `ccf1f1c4525464d396ba19a9dcdc07a74086b991`: Security Supply Chain Gate run `36667038748` failed while Workflow Feature PR Gate run `36667038775` remained queued/cancelled; merge `b90e79acc487366f475edf31916337d57bbea59d` nevertheless landed on main.
- **Sibling/adjacent cases checked:** The same defect class previously occurred on PR #386 and is recorded by LR-035; recurrence confirms the prior prose-only control was insufficient.
- **References (SHA/PR/issue/log):** PR #394; runs `36667038748`, `36667038775`; LR-035.

## 2026-09-29 — Heaven Bridge authentication — missing HMAC silently widened execution authority
- **Symptom:** Elevated Heaven Bridge workers reported `private-repo-acl` and accepted unsigned repository relay jobs whenever HMAC was absent.
- **Root cause:** HMAC was implemented as an optional enhancement instead of a mandatory execution boundary.
- **Violated invariant / wrong assumption:** Repository write access is transport authority, not permission to execute arbitrary commands on a persistent elevated machine.
- **Why prior defenses missed it:** Canonicalization, replay/TTL checks, and HMAC tests exercised configured-key behavior but did not assert fail-closed behavior when the key was missing.
- **Direct fix:** Require HMAC by default, support a machine-local key file, require `mhw-bridge-canon-v1`, and move repo-ACL-only/legacy behavior behind explicit emergency opt-ins.
- **Preventive rule/process change:** Every privileged remote-execution transport must have a negative test proving that missing authentication disables execution rather than downgrading authority.
- **Regression coverage added/strengthened:** Worker and Agent Control tests cover missing-key rejection, explicit emergency fallback, legacy-canonical rejection, and machine-local key resolution; CI policy asserts the fail-closed invariants.
- **Sibling/adjacent cases checked:** replay/TTL, cross-language canonicalization, target-host binding, duplicate job ids, heartbeat auth mode, and Agent Control submission.

## 2026-09-29 — Heaven Bridge process isolation — selective environment API still leaked all host secrets
- **Symptom:** `build_env` started with `os.environ.copy()`, so spawned shells/sessions inherited worker secrets before the apparent `env_from_host` allowlist ran.
- **Root cause:** Selective forwarding was layered on top of full inheritance instead of building a clean child environment first.
- **Violated invariant / wrong assumption:** A host-environment allowlist must actually bound what crosses the process boundary.
- **Why prior defenses missed it:** Inline secret-like variables were blocked, but tests did not assert absence of ambient parent credentials inside child processes.
- **Direct fix:** Build child environments from a secret-scrubbed host environment, reject secret-like `env_from_host` requests, scrub internal helper subprocesses, and sanitize shell-association launch.
- **Preventive rule/process change:** Process-launcher review must verify the effective child environment, not merely the API shape used to add variables.
- **Regression coverage added/strengthened:** Tests assert token/HMAC variables do not propagate and CI rejects reintroduction of full `os.environ.copy()`.
- **Sibling/adjacent cases checked:** raw shell fallback, persistent sessions, Git helpers, taskkill, UIA, screenshot/display helpers, shortcut creation, process listing, and app launch.

## 2026-09-30 — branch-preservation merge made commits ancestors without preserving their tree changes
- **Symptom:** GitLab catalog implementation commits appeared in canonical `main` ancestry and `compare` reported their implementation commit as a merge base, yet all three production files and the regression test were absent from the canonical tree. A cleanup/preservation merge had recorded task branch tips without changing the main tree.
- **Root cause:** Integration/recovery logic treated commit ancestry as evidence that branch content had been integrated. Record-only branch-tip preservation can intentionally retain commit reachability while discarding the branch tree delta.
- **Violated invariant / wrong assumption:** Reachability is provenance evidence, not content-survival evidence. A branch is integrated only when the intended post-merge canonical tree/diff is present and verified.
- **Direct fix:** Recover the exact four-file GitLab slice from the preserved task ref onto a fresh current-main branch, verify the canonical diff, and re-run exact-head gates.
- **Preventive rule/process change:** LR-037 requires canonical-tree verification after every integration/harvest. Record-only preservation merges must be explicitly treated as non-integrating and excluded from “already merged” detection. Managers must verify intended paths/content (or an equivalent tree/diff invariant) on canonical `main` before retiring the source branch/task.
- **Regression coverage added/strengthened:** Multi-agent integration tooling should test a merge commit that retains a task commit as an ancestor while deliberately keeping the base tree, and must classify the task as preserved-but-not-integrated rather than complete.
- **References:** GitLab catalog recovery PR #409; original implementation commits `ccb5be87c4edf284331233a4c885fa532dd8f604`, `249aa94f667c49ec4e38d48136409416ad5abec4`, `3bd48709eba46a46896b27213a6dac9ec3cf7c85`, test commit `368b0efe81110171e346da4283aaf0ac752361ad`.

## 2026-09-29 — CI security verification — repeated supersession erased completion evidence
- **Symptom:** Security Supply Chain Gate runs for security-hardening commits repeatedly ended as cancelled while unrelated newer pushes continued, leaving no completed-successful canonical-main security evidence for the hardened state.
- **Root cause:** Repository-wide supersession/cancellation behavior treated required security verification like ordinary duplicate CI even though the security workflow itself intentionally disables push cancellation.
- **Violated invariant / wrong assumption:** A newer commit does not prove an older security-relevant change unless a completed-successful verification run on that newer descendant actually executes the same required checks.
- **Why prior defenses missed it:** The workflow-level concurrency policy protected push runs from self-cancellation, but external/manual cancellation policy was not constrained by the same invariant.
- **Direct fix:** Make canonical-main security verification non-disposable, require a completed-successful final-head-or-descendant run, and independently parse the security-policy script before executing it.
- **Preventive rule/process change:** Supersession automation must distinguish required canonical security evidence from ordinary redundant checks.
- **Regression coverage added/strengthened:** Security Supply Chain Gate now runs a standalone PowerShell parser preflight before the policy script; repository rules forbid treating cancelled/queued/skipped runs as successful evidence.
- **Sibling/adjacent cases checked:** workflow concurrency, exact-head integration evidence, release verification, persistent self-hosted runner trust, and the prior duplicated/corrupted security-policy incidents.

## 2026-09-30 — branch-preservation rollback silently removed Agent Control recovery hardening
- **Symptom:** Perpetual swarm workers exited with code 1, were repeatedly auto-requeued, then landed in `RETRY EXHAUSTED`; the dashboard still reported the perpetual swarm as started while integration remained empty.
- **Root cause:** Preservation commit `5471f80545a755dd049718fa6812fb890e6effa2` retained task ancestry while replacing the canonical Agent Control blobs with an older tree, removing stale-lane takeover, retry cooldown/jitter, dispatch-failure accounting, and the matching tests.
- **Violated invariant / wrong assumption:** A “tree unchanged”/record-only preservation merge must never be allowed to change canonical product blobs, and commit ancestry is not proof that the intended controller tree survived.
- **Direct fix:** Restored `no-work-recovery.mjs`, recovery defaults, targeted `server.mjs` supervised-recovery logic, and regression tests from the last known-good pre-preservation main state while preserving unrelated later server changes.
- **Preventive rule/process change:** Enforce LR-037 canonical-tree proof for branch-zero/preservation operations; compare protected control-plane blobs before and after any record-only merge and fail closed on drift.
- **Regression coverage added/strengthened:** Restored stale-lane candidate, phase-owner exclusion, retry cooldown/dispatch-budget, and jitter-bound tests; existing branch-lifecycle coverage rejects ancestry-only cleanup proof.
- **References:** bad boundary `5471f80545a755dd049718fa6812fb890e6effa2`; restoration commits `9e7dfbb7373a22a994e7505fac7ce58cf2657ef5`, `21d4087086886c2ebf6725f18738c4fd23114f0f`, `fd249748d5d376943f7449f5e9753fd032a14d4d`, `1d09dbf43a5393f9beb56bf43d8f7db5a4fcbcdf`, `eaafc9d223fc4ca2465f6fc0f02fb55db00d29b1`.

## 2026-09-30 — authoritative nonzero agent exits were misclassified as retryable no-work
- **Symptom:** Managed workers that exited authoritatively with code 1 and produced no durable work were classified as no-work/stream-loss candidates, automatically replaced, and then repeatedly replaced again until retry exhaustion.
- **Root cause:** Recovery classification treated empty terminal output as evidence of no work without first distinguishing a proven nonzero process exit from an unknown/lost execution stream.
- **Violated invariant / wrong assumption:** A deterministic runtime failure is not the same state as “execution may not have started.” Automatic no-work replacement is safe only when execution outcome is unknown and an authoritative durable-evidence scan proves no work; a proven nonzero exit must stop for diagnosis unless useful durable work exists to preserve/reconcile.
- **Direct fix:** Added deterministic-runtime-failure classification for nonzero exit codes/spawn failures/provider-capacity evidence, excluded clean deterministic failures from no-work and swarm-tail retry, preserved deterministic failures that left substantive work, and made failed implementation ownership gate instead of spawning a meaningless repair lane.
- **Preventive rule/process change:** Recovery state machines must classify process outcome before evaluating empty output. Unknown transport loss may use bounded no-work retry after evidence scan; authoritative nonzero exit may not.
- **Regression coverage added/strengthened:** Tests cover nonzero exit suppression, stream-loss metadata not overriding authoritative failure, swarm-tail non-resurrection of clean crashes, preservation of dirty work, and implementation-phase gating.
- **References:** `fe302e78c453dd4cfb1630402368cb0ac57da80b`, `dc6e2df967f68a67d8ae108ae1ebdd87071fc96c`, `d1def9486620f1cc3b9016227fd25f42e87b9b09`, `3d918342b5a7d4c7d3b9fa25ae989e826b5ebd1c`.



## 2026-09-30 — tracked-secret scanner — literal test canary blocked valid security verification
- **Symptom:** Security Supply Chain Gate failed on a control-plane regression test because a committed secret-rejection canary was itself a literal GitHub-token-shaped string.
- **Root cause:** The runtime negative test and the source-level tracked-secret scanner were given the same literal credential-shaped fixture, so the scanner correctly could not distinguish a test canary from a committed credential.
- **Violated invariant / wrong assumption:** Security tests must exercise credential patterns without requiring credential-shaped literals to live in tracked source.
- **Direct fix:** Build the GitHub token canary from non-secret string fragments at runtime while preserving the exact value seen by the artifact-secret validator.
- **Preventive rule/process change:** Credential-rejection regression fixtures must be source-scanner-safe by construction; synthesize high-risk canaries at runtime and keep the source scanner fail-closed.
- **Regression coverage added/strengthened:** Existing artifact-secret rejection test still exercises the GitHub-token-shaped runtime value; Security Supply Chain Gate on descendant commit `ee428f106b3cff7f3a4f570a66666a5bfb70eb61` completed successfully in run `36669778319`.
- **References:** failing run `36669264469`, job `109740579636`; fix `98c9d15a5b0aee8a34b86499c7545f41d83aa739`.

## 2026-09-30 — rollback durability — read-then-unconditional-write could steal ownership and generic fields could persist credentials
- **Symptom:** Two rollback plans using the same `change_id` could race between the initial read and checkpoint write, allowing the later unconditional upsert to replace the winner. Separately, credential-like values under innocuous field names such as `value` passed persistence validation even though rollback state claimed not to persist credentials.
- **Root cause:** Plan creation used a time-of-check/time-of-use ownership check without create-only compare-and-swap semantics, and secret validation trusted key names more than value content.
- **Violated invariant / wrong assumption:** Durable ownership is a storage-layer atomicity property, not a pre-write observation. Secret persistence policy must protect values as well as familiar secret field names.
- **Direct fix:** Add `expected_revision=0` create-only checkpoint CAS semantics; make rollback plan creation use it and reconcile same-plan races idempotently; reject high-confidence credential-like values before durable persistence.
- **Preventive rule/process change:** Any durable named owner/lease/plan creation must have an atomic create-only primitive. Any state store claiming secret exclusion must test both sensitive keys and credential-shaped values under generic keys.
- **Regression coverage added/strengthened:** State-store create-only CAS regression, concurrent competing rollback-owner regression, generic-key credential-value rejection regression, plus the independently integrated transition regression that forbids committing after rollback has started.
- **References:** fix `ee428f106b3cff7f3a4f570a66666a5bfb70eb61`; current-main rollback transition test `test_partial_rollback_cannot_be_committed_and_can_resume`.

## 2026-09-30 — browser URL validation — legacy relay path accepted embedded URL credentials
- **Symptom:** The deep Playwright provider rejected `https://user:pass@host/`, but the legacy desktop-browser `open`/navigation URL validator accepted it and could forward embedded credentials through relay-visible parameters.
- **Root cause:** Two browser surfaces implemented different URL-security invariants.
- **Violated invariant / wrong assumption:** Equivalent entry points must enforce the same credential-egress boundary; scheme validation alone is not credential validation.
- **Direct fix:** Reject parsed URL username/password components in the legacy browser validator before any bridge call.
- **Preventive rule/process change:** Shared security invariants must be checked across sibling/legacy adapters whenever a stricter provider is added; URLs carrying userinfo are credential-bearing inputs.
- **Regression coverage added/strengthened:** Legacy browser service test asserts embedded credentials are rejected and no bridge request is emitted.
- **References:** fix `2b828af3ead99192009b644a87d6e156f8535658`.

## 2026-09-30 — Reconcile terminal output channels before recovery classification

- **Failure mode:** provider/auth/quota failures can land in JSONL or plain stderr while the final-message file is empty or contains only startup prose. Preferring the final-message file hides the stronger diagnostic and can misroute recovery.
- **Prevention:** reconcile both output channels before exit classification. Provider-capacity evidence outranks benign/opening prose, is persisted on the agent record, and closes the provider-capacity circuit instead of entering no-work recovery.
- **Regression requirement:** cover structured JSON errors, plain stderr, and persisted capacity evidence surviving later non-quota summaries.

## 2026-09-30 — strict analyzer debt — root compile failures cascaded into missing-artifact noise
- **Symptom:** Windows Release Gate run `36669017493` failed first on six warnings-as-errors in `MhwModManager.Core`, then emitted many downstream missing-DLL/test failures. After the six production diagnostics were repaired, exact Heaven verification of `cde589f2330f3e10e92b860ffe16dcdb4fc0d9c2` built Core with 0 warnings / 0 errors and exposed four additional warnings-as-errors in `MhwModManager.Tests`.
- **Root cause:** Rapid catalog/provider integration reached canonical history without exact warnings-as-errors closure across both the production project and its affected test project. The escaped diagnostics covered CA1822, CA1826, CA1859, CA1861, xUnit2013, and xUnit1051.
- **Violated invariant / wrong assumption:** A warnings-as-errors repository is not integration-ready when only source inspection or a production-only compile is clean. The first compiler/analyzer diagnostic is the causal failure; missing downstream assemblies are cascade symptoms, not independent bugs.
- **Why prior defenses missed it:** Multiple fast-moving integration lanes advanced `main` while exact verification lagged. Existing functional tests could not execute once analyzer compilation failed, and source review did not substitute for the pinned analyzer profile.
- **Direct fix:** Preserve `InstalledCatalogOriginChecker`'s instance API with an explicit analyzer justification; use concrete/indexable collection semantics in Thunderstore/family/Mod DB internals; repair xUnit collection-size and cancellation-token usage; eliminate repeated constant-array analyzer violations in tests. Production fixes are canonical through `cde589f2330f3e10e92b860ffe16dcdb4fc0d9c2`; remaining test fixes are PR #437.
- **Preventive rule/process change:** Diagnose and repair the first compiler/analyzer failure before interpreting downstream build/test noise. For Core/catalog changes, require exact-candidate warnings-as-errors compilation of both `MhwModManager.Core` and `MhwModManager.Tests` (or the full repository verifier) before integration readiness.
- **Regression coverage added/strengthened:** The strict compiler/analyzer profile is the deterministic regression gate; existing behavioral tests remain unchanged except analyzer-safe assertion/cancellation/fixture construction. Exact Heaven job `job-20260930T044200Z-bugfix-core-analyzers` proves the production slice builds 0 warnings / 0 errors and independently reproduced the four test-project diagnostics.
- **Sibling/adjacent cases checked:** Thunderstore normalization, generic-family inference, Mod DB feed caching, installed-origin checking, Mod DB feed tests, logical-family tests, syndication transport tests, and GitLab catalog tests.
- **Verification/evidence:** failing Windows run `36669017493`, job `109739811849`; production exact Heaven job `job-20260930T044200Z-bugfix-core-analyzers`; PR #437 head `8bb9702880e5b550a1492fc3c2b839d670e2cd03` queued for exact Heaven and PR-gate verification.

## 2026-09-30 — deep browser lifecycle — failed navigation leaked owned resources and stale tab identity
- **Symptom:** A failed initial `page.goto` left an owned Playwright browser/context/session registered; a failed new-tab navigation left the failed tab registered and active; listing an externally closed active tab removed only the forward tab entry while leaving reverse page identity and active selection stale. Separately, redacted IPv6 metadata lost required host brackets (for example, `http://[::1]:9222/a` became malformed `http://::1:9222/a`).
- **Root cause:** Session/tab creation mutated registries and host resources before fallible navigation without a transactional rollback path. Closed-page cleanup was duplicated instead of centralized. URL display reconstruction treated an IPv6 hostname like an ordinary DNS name.
- **Violated invariant / wrong assumption:** A failed resource-owning operation must leave no newly owned resources or registry identity behind. All removal paths for the same indexed object must repair every index/active pointer consistently. URL redaction may remove sensitive components but must preserve the structural syntax required to identify the same authority.
- **Direct fix:** Close owned context/browser and unregister the session on failed initial navigation; close/unregister failed new tabs and restore the previous active tab; route closed-tab pruning through one `_forget_page` helper; bracket IPv6 host literals when reconstructing display URLs.
- **Preventive rule/process change:** Treat resource-owning constructors/open operations as transactions: either return a fully usable registered object or undo every acquired resource and registry mutation. Centralize multi-index removal, and add round-trip/shape tests for structured metadata renderers.
- **Regression coverage added/strengthened:** Tests cover failed initial navigation cleanup, failed new-tab cleanup with active-tab restoration, externally closed active-tab pruning through both direct lookup and public `tabs()`, and IPv6 display URL reconstruction with query stripping.
- **Verification/evidence:** Canonical code commits `04e706ba2a8b7e0de5ab0461af3702b9db854a33`, `339b4caf7fdd00eb0aa6d7b29c3149a9537d9f71`, and `db27ed786337529fa5674ff735c5d06038a9270f`; PR #438 merged as `b9b3cd9ec0c76c44556bf3f1e3014f2d5a122620`. The exact post-merge Plugin Toolbox Gate was still queued at handoff; do not report it as passed until a completed-successful run exists.

## 2026-09-30 — Never expose canonical updater release before the client feed

## 2026-09-30 — regression test copied the wrong module alias and failed before testing behavior
- **Symptom:** Exact Heaven verification of the HMAC-rotation candidate stopped with two `NameError: name 'hb' is not defined` failures before the new rotation assertions could execute.
- **Root cause:** The new test code copied the `hb` alias convention from a different bridge test file even though `heaven-bridge/tests/test_worker.py` imports the module as `worker`.
- **Violated invariant / wrong assumption:** Regression tests are production changes for completion purposes: copied test snippets must be reconciled to the destination file's imports/names before integration, and a newly added regression must itself execute before the fix is considered complete.
- **Direct fix:** Replaced every introduced `hb.*` reference with the destination file's canonical `worker.*` alias.
- **Preventive rule/process change:** Before integrating copied/moved test logic, inspect the destination module's local import aliases/fixtures and run the exact affected test file; static-looking test additions are not exempt from execution-backed verification.
- **Regression coverage added/strengthened:** Exact commit `b0b4bf2300efe347aada7e4b5e699cb4b89188c6` passed 19 Python worker tests and 17 Node Agent Control bridge tests, including rotation acceptance, revocation, key-id tamper rejection, and canonical signing fixtures.
- **References:** failing Heaven job `chatgpt-20260930-045100-hmac-rotation-exact-verify`; fix `b0b4bf2300efe347aada7e4b5e699cb4b89188c6`; passing rerun `chatgpt-20260930-045500-hmac-rotation-main-rerun`.

## 2026-09-30 — Agent Control polling overwrote operator state and hid Stop on active agents
- **Symptom:** The dashboard could silently reset a manually selected worker target to Auto during its four-second refresh cycle, a slower older snapshot could render after a newer sync response, and active managed workers in waiting/blocked/stale states were shown a reviewer action instead of Stop.
- **Root cause:** Rendering rebuilt operator-owned form state from defaults on every poll; snapshot requests had no in-flight/sequence arbitration; dashboard action eligibility duplicated only three lifecycle strings instead of matching the server's authoritative active-state model.
- **Violated invariant / wrong assumption:** Observational refresh must not own mutable operator intent, and a client must not invent a narrower lifecycle model than the control authority.
- **Why prior defenses missed it:** Existing dashboard tests checked DOM uniqueness, required surfaces, counters, and JavaScript parseability, but did not pin asynchronous refresh ordering, preservation of operator selections, or action coverage for all active managed states.
- **Direct fix:** Preserve the current machine target when rebuilding options, serialize normal polling and reject stale responses by request sequence, suppress stale failure/offline rendering, and expose Stop for reserved/starting/running/waiting/blocked/stale/stopping.
- **Preventive rule/process change:** LR-045 requires polling control planes to preserve operator intent and use the authoritative lifecycle model.
- **Regression coverage added/strengthened:** `federation-dashboard.test.mjs` now pins lifecycle Stop coverage, selected-machine preservation, and request-sequence stale-response guards; its existing inline-JavaScript parse regression remains in force.
- **Verification/evidence:** implementation `3ab57f1da041171e043a65078d3294335bcee97c`, regression test `39a2657182797473aa61a7e4ba5e84a7b8f73341`, Agent Control package v0.5.10.


## 2026-09-30 — Agent Control generated Copy branch markup broke quoting and nested plugin identity drifted
- **Symptom:** The managed-agent **Copy branch** action embedded `JSON.stringify(a.branchName)` inside an already double-quoted `onclick` attribute, producing malformed markup for ordinary branch names. Separately, the nested Codex plugin manifest still reported v0.6.1 while the runtime/root plugin were v0.6.3.
- **Root cause:** Generated operator markup combined two independent quoting layers without an encoding boundary, and the release-identity regression covered only one of the two distributable plugin manifests.
- **Violated invariant / wrong assumption:** Data inserted into generated executable markup must cross an explicit encoding boundary; every runtime/plugin manifest representing one component must participate in the same release-identity invariant.
- **Direct fix:** URI-encode branch values before inline-handler interpolation and decode them only on invocation; align runtime, root plugin, and nested Codex plugin at v0.6.4.
- **Preventive rule/process change:** Validate emitted operator markup rather than only template source, and enumerate every mirrored/distributable manifest in release-identity tests.
- **Regression coverage added/strengthened:** `operator-ui-cli.test.mjs` requires the encoded Copy branch form and rejects the raw JSON.stringify handler; `agent-manager-priority.test.mjs` now requires the nested Codex manifest to match the runtime version.

## 2026-09-30 — plugin version pruning — Windows UTF-8 BOM made valid manifests invisible
- **Symptom:** The first live Windows smoke for the stale-plugin pruner reported no discovered copies, so the old `0.8.1` fixture remained beside `0.8.2` even though the unit suite was green.
- **Root cause:** PowerShell 5.1 `Set-Content -Encoding UTF8` emitted a UTF-8 BOM, while the pruner decoded manifests with plain `utf-8`; `json.loads` therefore rejected the BOM-prefixed document and discovery silently skipped it.
- **Violated invariant / wrong assumption:** Manifest discovery must accept the repository's supported Windows text encodings, and a green cross-platform fixture is not proof that a Windows-produced manifest can be parsed.
- **Why prior defenses missed it:** Unit fixtures were written with Python's BOM-free UTF-8 writer, so they never exercised the exact Windows producer used by the live smoke.
- **Direct fix:** Decode plugin manifests with `utf-8-sig`, which accepts both BOM and BOM-free UTF-8 without weakening JSON validation.
- **Preventive rule/process change:** Any automation that consumes JSON/text emitted by Windows PowerShell must include an exact Windows-encoding fixture or live smoke before integration; encoding compatibility belongs in the parser contract.
- **Regression coverage added/strengthened:** Added `test_windows_utf8_bom_manifests_are_supported` to the plugin-pruner suite and retained the live two-version deletion smoke.
- **Sibling/adjacent cases checked:** Direct `manifest.json`, `plugin.json`, and `.codex-plugin/plugin.json` discovery all use the same decoder; non-SemVer and canonical-source protections remain fail-closed.
- **Verification/evidence:** Failing live job `chatgpt-20260930-verify-plugin-pruner-heaven2-a1`; fix commit `ccff83a93c39233a0231456ac60b919cf003d8f9`; regression commit `44e293003a7b4b06c77270f27f4f85beb6d0f1ff`.

## 2026-09-30 — plugin-pruner install verification — persisted audit JSON and Startup VBS were malformed
- **Symptom:** The pruner scheduled task installed and ran on both `heaven2` and `heaven`, but post-install `ConvertFrom-Json` failed on `last-prune.json`; inspection also showed the Startup fallback contained `CreateObject(""WScript.Shell"")` instead of valid VBScript quoting.
- **Root cause:** The Python writer appended the two literal characters `\\n` instead of a newline, and the PowerShell generator over-escaped quotes inside a single-quoted PowerShell string.
- **Violated invariant / wrong assumption:** A persisted machine-readable artifact is not valid merely because its producer ran successfully; it must round-trip through its real consumer. Generated recovery scripts must be syntax-valid in the target interpreter, not just syntactically valid in the generator language.
- **Why prior defenses missed it:** Unit tests verified pruning behavior and PowerShell parser validity, but did not parse the persisted audit file or execute/validate the generated `.vbs` fallback.
- **Direct fix:** Write a real newline after JSON and emit `CreateObject("WScript.Shell")` in the generated fallback.
- **Preventive rule/process change:** Persistent JSON/log outputs require consumer round-trip tests, and generated cross-language startup/recovery artifacts require target-interpreter validation or execution before integration.
- **Regression coverage added/strengthened:** Added CLI log round-trip JSON coverage plus installer-source assertions for valid WScript quoting; live reinstall will run the Startup fallback with `cscript.exe` and reparse the generated audit JSON.
- **Sibling/adjacent cases checked:** Task action uses `pythonw.exe` with a dedicated `--log`; scheduled-task registration and Startup fallback point at the same runtime/pruner arguments.
- **Verification/evidence:** Failing install jobs `chatgpt-20260930-install-plugin-pruner-heaven2-a1` and `chatgpt-20260930-install-plugin-pruner-heaven-a1`; fixes `9d2a78266ddb89d0489d9eada2108546ccdbeedb` and `d0716ff3c2b477d766a3685dcad597c9670d6491`; exact heaven2 verification `chatgpt-20260930-verify-plugin-pruner-runtime-fix-a1` passed the full plugin gate, JSON consumer round-trip, and deletion smoke.


## 2026-09-30 — Agent Control health found Heaven relay but execution bypassed the resolver
- **Symptom:** Clicking/dispatching created managed main/manager workers, but they exited almost immediately with authoritative exit code 1 and then entered recovery.
- **Root cause:** `resolveHeavenRelayDir()` already supported the documented per-user `~/HeavenBridgeRepo` fallback and health inspection used it. `submitHeavenBridgeJob()` and `waitForHeavenBridgeResult()` instead defaulted directly to `process.env.AGENT_CONTROL_HEAVEN_RELAY_DIR` and threw when that environment variable was absent.
- **Authoritative evidence:** `main-20260930042701-ldewu` and `manager-20260930042717-kmbi6` both recorded `Error: AGENT_CONTROL_HEAVEN_RELAY_DIR is required for bridge execution.` before authoritative exit code 1.
- **Violated invariant / wrong assumption:** Provider health/preflight and real execution must share one configuration resolver; green discovery is meaningless if submit/wait reimplement it differently.
- **Why prior defenses missed it:** Existing tests covered discovery in isolation but did not pin actual execution resolution to the documented fallback.
- **Direct fix:** Add `resolveExecutionRelayDir()`; explicit caller paths remain authoritative, otherwise submit/result-wait reuse `resolveHeavenRelayDir()`.
- **Preventive rule/process change:** LR-051 requires provider preflight and execution to share one authoritative resolution path.
- **Regression coverage added/strengthened:** `heaven-bridge-provider.test.mjs` now covers documented fallback, explicit override, and absent-fallback fail-closed behavior; live restarted heaven2→heaven1 proof remains required.

## 2026-09-30 — agent startup governance — truncated context or missing preferred tool caused false blocking
- **Symptom:** A senior programming agent refused to process its task after large mandatory continuity reads were truncated and a preferred GitHub CLI was unavailable, even though alternate repository routes existed.
- **Root cause:** The startup contract required end-to-end reads of a large fixed corpus and gave the fail-closed training-blocked instruction stronger wording than the later fallback/wraparound rules. Agent Control mechanically reproduced that ordering in every generated prompt.
- **Violated invariant / wrong assumption:** Proving canonical repository truth does not require rereading every historical byte through one interface. Tool-output truncation, one missing CLI, one failed network route, or one unavailable checkout are recoverable routing conditions unless reasonable authorized alternatives also fail.
- **Why prior defenses missed it:** Regression coverage only proved that the old full-read gate appeared before the task and contained every path; it did not test context efficiency, pagination recovery, alternate-tool routing, or evidence requirements for declaring a blocker.
- **Direct fix:** Split startup into a small full-read core and hash-verified indexed context; require task-relevant expansion, pagination/chunking for truncation, GitHub/Heaven/CI fallback routing, and evidence-backed blocker criteria. Generated Agent Control prompts now encode these rules before the task.
- **Preventive rule/process change:** Startup success means proving canonical truth efficiently. Large historical ledgers remain available and hash-pinned but are read selectively; premium/senior agents delegate mechanical retrieval and preserve context for high-value reasoning.
- **Regression coverage added/strengthened:** `repository-training-gate.test.mjs` now verifies that large ledgers are indexed rather than full-read core, prompt generation includes pagination and missing-tool fallbacks, and blocked status requires exhausted authorized routes.
- **Verification evidence/environment:** Exact-head Agent Control/CI verification is required before this candidate is integrated; no inherited green result is claimed.
- **Sibling/adjacent cases checked:** Missing preferred GitHub CLI, truncated tool output, unavailable local checkout, single network-path failure, large CURRENT_STATE/NEXT_STEPS/VERIFICATION rereads, and manager-specific training.
- **References (SHA/PR/issue/log):** v8.8.40 efficient-bootstrap candidate branch `fix/efficient-agent-bootstrap-v8.8.40-20260930`.

## 2026-09-30 — verification recovery — stale repair fixtures introduced independent gate failures
- **Symptom:** Recovering #507's verifier repairs onto current-main lineage still left the broad gate red on a literal escaped newline in a planner-test comment, one untraced nested GitLab helper, and a dependency-cycle fixture that accidentally exercised MHW atomic-bundle safety.
- **Root cause:** #507's latest source was treated as useful repair material but had never earned a completed green exact-head run. Its cycle fixture mixed an unrelated structural-bundle invariant into a dependency-cycle test, and one authored comment preserved an escaped newline literally.
- **Violated invariant / wrong assumption:** A stale repair branch is candidate material, not verified truth. Regression fixtures must isolate the behavior they claim to test, and function instrumentation applies to newly introduced local helpers.
- **Why prior defenses missed it:** The latest #507 head had no completed green Workflow Feature run. The defects became visible only after #510 exercised the full verifier on fresh-main lineage.
- **Direct fix:** Preserve the validator's stable training-gate heading, trace the nested catalog normalizer, replace the escaped newline with a real line break, and make the dependency-cycle fixture use non-structural unique files so atomic-bundle semantics cannot mask dependency behavior.
- **Preventive rule/process change:** Transplant stale repair work narrowly and require exact-head verification before promotion; do not weaken unrelated production invariants to make a fixture pass.
- **Regression coverage:** Existing handoff continuity, function verifier, planner invariant, and hard-dependency-cycle tests exercise these boundaries.
- **Verification evidence:** Exact-head PR #510 Agent Control, Workflow Feature, and Security gates are required before integration.
- **Adjacent cases checked:** Stable handoff heading contract, nested local-function tracing, generated literal escape handling, and MHW structural fixture interference.
- **Learned-rule decision:** No new rule ID is needed; the existing exact-head verification doctrine plus LR-053's fallback/provenance requirements already cover the reusable lesson.


## 2026-09-30 — texture resolver — multi-provider aggregation upgraded weak evidence into a guessed winner
- **Symptom:** Exact-head Workflow Feature verification on PR #510 failed the randomized same-family texture invariant because some 3+ provider collisions were reported non-blocking with an automatically selected winner.
- **Root cause:** `SelectTextureProvider` counted pairwise winners even when those pair decisions were only `Confidence.Medium` priority tie-breaks, then synthesized a `Confidence.High` `texture-complete-dominance` result when one provider won every weak pair.
- **Violated invariant / wrong assumption:** Combining several weak precedence signals cannot increase their authority. Multi-provider automatic selection requires every supporting pairwise proof to be independently high/explicit confidence.
- **Why prior defenses missed it:** Two-provider callers already rejected medium/low confidence after selection, but the 3+ aggregation path rebuilt the final result as High and bypassed that safeguard. Existing complete-dominator coverage used genuinely high-confidence revision evidence only.
- **Direct fix:** Reject any medium/low pairwise winner before it contributes to multi-provider dominance; return a blocking `texture-evidence-insufficient` result instead of promoting profile priority into overwrite authority.
- **Preventive rule/process change:** LR-054 requires aggregate decisions to preserve the weakest required evidence threshold rather than strengthening it by vote/count/dominance.
- **Regression coverage added/strengthened:** Added a deterministic three-provider same-lineage priority-only regression and retained the randomized family-texture invariant plus the genuine high-confidence complete-dominator test.
- **Verification evidence/environment:** Exact-head PR #510 Workflow Feature gate must pass after this repair; Agent Control and Security gates were already green on the immediately preceding head.
- **Sibling/adjacent cases checked:** two-provider medium-confidence family texture handling, 3+ genuine revision dominance, dedicated texture-provider dominance, and explicit resource-provider precedence.
- **References:** PR #510; failed Workflow Feature run 36749989372.


## 2026-09-30 — repository context navigation — union whitelist widened the indexed-reader contract
- **Symptom:** Pre-merge review showed that a caller holding the valid SHA-256 for a mandatory core training file could pass that core path to the context pagination/navigation helper even though the bootstrap surface describes the capability as indexed-context retrieval.
- **Root cause:** `documentBytes` correctly used one union set for all bootstrap-manifest documents, and the retrieval helper reused it without applying the narrower `REPOSITORY_CONTEXT_INDEX_PATHS` authority class.
- **Violated invariant / wrong assumption:** Exact hashes and path containment prove identity/safety, not that a document belongs to the capability's authorized subset. A union whitelist must not be treated as a narrower whitelist.
- **Why prior defenses missed it:** Existing pagination tests rejected paths outside the union but did not attempt a valid-hash core document. The new navigation acceptance language made the narrower contract explicit and exposed the gap during review before integration.
- **Direct fix:** Add a dedicated indexed-context set and reject non-indexed documents before hash/file reads in the shared retrieval path, covering both pagination and search/heading modes.
- **Preventive rule/process change:** LR-057 requires subset-specific authorization whenever a manifest/registry union contains multiple authority classes.
- **Regression coverage added/strengthened:** `repository-bootstrap.test.mjs` now proves both `readRepositoryContext` and `findRepositoryContext` reject a core document even with its correct manifest SHA-256.
- **Verification evidence/environment:** Exact-head PR #515 Agent Control, Workflow Feature and Security gates are required after this repair; the earlier PR runs are stale and cannot authorize merge.
- **Sibling/adjacent cases checked:** indexed path containment, linked-source refusal, source-size bounds, stale-hash rejection, core manifest construction, manager-only core training, and navigation query/result/byte limits.
- **References:** PR #515; v8.8.43 bounded context-navigation candidate.

## 2026-09-30 — context retrieval — serialization exceeded the advertised output budget
- Symptom: actual navigation CLI on LEARNED_RULES with literal `rule` and 50 results emitted 9,368 bytes although compact helper JSON was 8,141 bytes under the advertised 8,192-byte ceiling.
- Root cause: helper measured compact JSON but CLI added pretty-print whitespace. Sibling pagination measured raw text without JSON envelope/escaping, allowing quotes/control characters to expand beyond the ceiling.
- Violated invariant: an output budget applies at the final observable serialization boundary, including envelope, escaping and newline.
- Missed prevention: tests measured helper objects/strings, not actual maximum-result CLI stdout; pagination UTF-8 tests did not exercise serialization expansion.
- Direct fix: compact CLI JSON; navigation includes newline in byte accounting; pagination reduces whole lines until its JSON envelope/newline fits the global ceiling and preserves exact continuation.
- Preventive rule: LR-058 and generic trainer require actual emitted-byte tests, escaped/Unicode fixtures and lossless continuation. No larger limit or weakened check.
- Regression: actual CLI maximum-result test plus quote/emoji pagination walk proving every line is retrieved exactly once, with each emitted page <=8,192 bytes.
- Evidence: reproduction on PR #515 source 2031c405, hidden local Node on heaven2; focused integration tests 12/12 pass after repair. Historical candidate status is superseded by exact source e655e465: PR #517/main controller 269/269, 26/26 Windows, security, immutable build 352 and updater/rollback 36772774812 pass; live 0.6.20 navigation smoke passes. Full closure is in EVIDENCE/v8.8.43-context-navigation.md.
- Siblings: pagination raw-text/envelope expansion, navigation query/result/snippet bounds, indexed whitelist, stale hashes, bootstrap budget and CLI formatting; bootstrap packet unchanged in structure and still bounded.
- References: v8.8.43 integration evidence; integration branch codex/context-navigation-integration.

## 2026-09-30 — Agent Control state — repeated Windows BOM defect could discard newer ownership
- Symptom: valid BOM-prefixed primary JSON was rejected and an older backup was selected, dropping tasks/leases unique to the primary. Live controller carried a historical BOM fallback incident.
- Root cause: plain JSON.parse on decoded UTF-8 did not accept the supported Windows producer's single leading BOM; the same mismatch existed in backup validation and legacy migration.
- Violated invariant: supported encoding cannot demote valid authoritative current ownership to stale recovery state. Backup is a corruption fallback, not an encoding fallback.
- Missed prevention: prior plugin-pruner BOM precedent was not propagated into controller state consumers; fixtures only wrote BOM-free JSON. This repeated class is escalated to a shared state decoder and real server/persistence tests.
- Fix: one readStateJson decoder removes only one leading BOM before strict JSON parsing. Primary/backup, legacy import and pre-save backup validation reuse it; writers stay BOM-free.
- Prevention: LR-059 requires producer-format fixtures across the entire read/validate/backup/migrate chain, retaining strict malformed-data handling. Generic trainer propagation.
- Regression: newer primary vs stale backup; settings save retains newest backup, then corrupt primary recovers those records; BOM backup recovery; legacy unmerged agent stays orphaned; duplicate/misplaced BOM and malformed JSON stay degraded/read-only/paused.
- Evidence: isolated hidden Node/HTTP on heaven2 reproduced recovered instead of healthy before fix; four focused BOM tests and 273 full tests pass afterward, including actual PS5 UTF8 producer. PR #519 and exact main 45529dfa pass controller/security/26 Windows checks; immutable build 353 parity and disposable update/rollback 36775736821 pass. Guarded live heaven2 controller 0.6.21 source/status/CLI proof is in EVIDENCE/v8.8.44-agent-state-encoding.md.
- Siblings: three state-read sites and pre-save validation centralized; plugin pruner already uses UTF-8-sig; generated state writers stay BOM-free. Network/auth payload parsers unchanged; historical recovery warning is not erased.
- References: v8.8.44-agent-state-encoding evidence; prior plugin-pruner Windows BOM incident.


## 2026-09-30 — exact federation identity and governance integration drift (v8.8.47)
- Symptom/root cause: federation inspector redirected a requested federated record to managed lineage; tests stubbed the final selector and missed linked records. Invariant: inspect-federation must preserve exact logical identity; metadata links never authorize substitution.
- Fix/prevention: recover two-line redirect removal from8f78d21d; execute real inline inspector plus notification routing for linked/arbitrary IDs and missing records. Card/explicit actions already share the same function; managed inspection remains separate. Regression fails on main then passes on repair. Actual browser/runtime proof and issue461 closure remain pending.
- Concurrent main4ba4832d (v46) also omitted handoff version synchronization and exceeded manager core65536. Exact Windows36779491892/controller36779492028 failed, security36779491982 passed. Core guards already caught the defect; integration bypassed them. Repair synchronized47 metadata and move repeated P0 acceptance detail into required indexed history, preserving rules/budget. Require real manager bootstrap and handoff checks before any governance merge; no copied prior green.
- Evidence/environment: heaven2 Windows PowerShell/Node; operator-ui regression observed managed-1 instead of requested federated ID. Handoff mismatch reproduced locally. Full candidate checks/publication pending; record exact follow-up evidence before closure. Siblings: all federation UI routes share selector; other indexed role/core rules retained. No auth bypass, live user-data changes or unsupported width-fix claim.

- Review prevention evidence v47: required archived contract initially outside retrieval whitelist; added actual index/retrieval regression. A local edit defaulted to Windows code page and changed README punctuation; restored exact UTF-8 history. Prior encoding rule applies to reads as well as writes; always specify both and inspect historical diff. Review found these before integration. Final275/275 plus handoff baseline/negatives and whitespace pass; manager64950bytes.


## 2026-09-30 - Mods width report / wrapper contract (v8.8.48)
- Report: Mods content collapsed to one-card width despite full shell. Source has redundant direct-wrapper Width bound to ancestor ActualWidth. Underlying populated-window trigger remains unproven: minimal and actual styled probes both resize old/new correctly. Do not promote suspected feedback path to proven root cause.
- Corrective scope: reconcile original PR522/e0b423e7, remove explicit Width and preserve Stretch/margins/star library/toolbar. Invariant: direct selected Mods wrapper uses automatic available-width layout. Prior text guard could miss equivalent formatting or match unrelated grids; parsed direct-wrapper guard fails old binding and accepts candidate.
- Prevention/regression: verify direct XAML parent/child contract and actual styled resize topology, retaining full VM/runtime gap. Sibling pages retain their existing container-specific width contracts (Dashboard binds named ScrollViewer ViewportWidth); no source-level sibling transplant needed. Runtime probes1040/1480/1920/1040 => wrapper804/1244/1684/804 and library788/1228/1668/788; actual ancestorPART_SelectedContentHost. No user install/data/UI activation. Exact .NET/CI evidence pending; no reported-bug FIXED claim before actual acceptance.


## 2026-09-30 — ComboBox contrast workaround — readable text introduced a bright native surface
- **Symptom:** The v8.8.49 installed UI rendered the GAME selector as a large white Windows control inside the otherwise dark application shell.
- **Root cause:** The prior contrast repair paired `SystemColors.ControlBrushKey` with `SystemColors.ControlTextBrushKey`. That solved selected-text contrast by explicitly opting back into the Windows native light control surface instead of making the control obey the application palette.
- **Violated invariant / wrong assumption:** Theme correctness is a whole-control visual contract. Readable foreground/background values are insufficient when platform chrome still violates the product palette.
- **Why prior defenses missed it:** The regression asserted only the matched system brush pair. It proved text contrast syntactically but did not prove dark-theme chrome, popup/item styling, or installed runtime appearance.
- **Direct fix:** Give ComboBox and ComboBoxItem an application-owned template using the existing Text/Panel/Border/Accent resources for selected value, arrow, popup, hover, focus, and selection states.
- **Preventive rule/process change:** When native control chrome conflicts with the product theme, own the template rather than swapping to a system surface. Theme regressions must assert the intended palette/template boundary and still require real installed-client visual acceptance before closure.
- **Regression coverage added/strengthened:** `ComboBoxesOwnDarkThemeChromeInsteadOfUsingWindowsLightSystemSurface` requires the custom ComboBox template and `PART_Popup`, pins dark palette resources, and rejects both Windows system control brush keys.
- **Sibling/adjacent cases checked:** Global ComboBox styling remains centralized in `App.xaml`; item popup styling follows the same palette; the selected GAME binding and Switch/Settings behavior are unchanged.
- **Verification/evidence:** User-provided v8.8.49 build screenshot demonstrates the escaped bright surface. Exact-head v8.8.53 CI and installed-client visual confirmation remain required.
