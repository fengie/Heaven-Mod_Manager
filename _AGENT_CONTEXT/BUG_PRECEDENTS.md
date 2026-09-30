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

## 2026-09-30 — Never expose canonical updater release before the client feed

- **Incident:** updater build 218 / v8.8.20 became visible in `fengie/mhw-mods` at 2026-09-30 01:29:19Z. Windows Release Gate run `36655045833` was cancelled at 01:30:39Z by a newer `main` run while the public-feed mirror was still in progress. `fengie/mhw-mod-manager-release` was left with an abandoned draft `updater-main-218`, so installed clients still saw stable build 217 / v8.8.19 while the canonical GitHub repository already showed v8.8.20.
- **Primary root cause:** the cross-repository release transaction used `cancel-in-progress: true` and published the private/canonical release before the public feed consumed by installed clients.
- **Recurrence during repair:** fix commit `b35be018f74e90c879f3d056186061add2755f9f` reached `main`, but older stale integrations later replaced the release files with their pre-fix versions. This proved a release-only regression test was insufficient when stale whole-file trees can be integrated.
- **Required prevention:** publish/verify the public feed first; re-check canonical main after public asset upload; make the canonical/private publisher itself refuse visibility unless the exact public release already exists immutable with matching assets; publish canonical/private second; make the release transaction non-cancellable; recover automation-owned abandoned drafts; assert cross-repo parity; and independently enforce these invariants from the security-supply-chain policy.
- **Integration prevention:** any branch touching protected release/CI files must be reconciled with current `main` before integration. A stale branch tree is never acceptable evidence that unchanged-looking files are safe to replace.
- **User-facing invariant:** once a new canonical GitHub updater release is visible, every unauthenticated installed client must already be able to discover that exact build from the public feed.
