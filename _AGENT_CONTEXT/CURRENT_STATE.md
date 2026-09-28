## Current v8.8.2 support-bundle privacy candidate — local release-verified — 2026-09-28

Canonical main was re-established at `9dd91767880ae6c9dcb2a31d64410c1f0bd52827`, which already contains the updater REST tag-verification hardening and v8.8.1 metadata. The support-bundle privacy branch was merged forward normally and advanced to **v8.8.2**. Exact candidate source `828054cc02b47684e24765c215ae5fde5135e6f1` fixes the confirmed P1 raw structured-log share leak while preserving full-fidelity local logs.

Exact heaven2/Windows/.NET 10.0.401 local closure: publication-policy PASS; focused privacy **1/1**; verifier **25/25**; FunctionVerifier **735/735**, **7,821 / 0 uncovered**, **0 trace/parse gaps**; Core **79/79**; Automation **24/24**; Integration **178/178**; self-test **11/11**; strict builds/analyzers PASS; ReadyToRun app and updater-helper publish PASS; Build-Release PASS; updater build **297**; ZIP SHA-256 `97A81911C31DD8526598A6F845BACE47D3404F5DFB4C70A3C2FF1A80B9C237FA`.

Integration/hosted exact-main closure must still be checked against current GitHub state; do not relabel the local result as hosted evidence. Detailed handoff: `_AGENT_CONTEXT/SUPPORT_BUNDLE_SHARE_SANITIZATION_2026-09-28.md`.

## Current updater publication repair — 2026-09-28

Canonical start SHA: `a83dc6e047ccf98e896f10c25772df99b95426d1`; origin was exactly `https://github.com/fengie/mhw-mods.git`, fetch succeeded, and the canonical main worktree was clean. C11b is integrated on main. Hosted gate run 36428542918 passed verification/build/package/policy and failed at first-release discovery because the empty release list lacked a `tagName` property under PowerShell strict mode; GitHub releases API currently returns no releases.

C12 is isolated on `agent/auto-updater-publication-fix-20260928` and normalizes empty CLI release output while validating all nonempty rows. Exact code commit `9234c61c47f9ebc82b3a6ce546799ccaa6f395a2` is locally release-verified on heaven2/Windows/.NET 10.0.401: policy PASS; `Verify-Release.ps1` **25/25**; FunctionVerifier **727/727** with **7749 / 0 uncovered**; Core **79/79**; Automation **24/24**; Integration **173/173**; self-test **11/11**; strict builds **0 warnings/errors**; ReadyToRun app and updater-helper publish PASS; `Build-Release.ps1` PASS. Local updater build **230**, ZIP SHA-256 `E613A43E69B75B5CCFF87852F918D8BD270888B3F8D4A493E88A3A8DFD5E67D8`. Hosted rerun/publication and disposable live update/rollback proof remain pending. Detailed handoff: `_AGENT_CONTEXT/AUTO_UPDATER_C12_RELEASE_DISCOVERY_REPAIR_2026-09-28.md`.

# Active updater continuation — 2026-09-28

The user selected automatic updates as the current production boundary. Older archive-budget recommendations below are historical for this task. Work on `agent/auto-updater-20260928`; canonical starting main is `4fd61dd33609a7c55e5aedbaad026266a410f942`. Read `_AGENT_CONTEXT/AUTO_UPDATER_IMPLEMENTATION.md` and `_AGENT_CONTEXT/AUTO_UPDATER_NEXT_AGENT.md`.

The updater is INCOMPLETE only at the final hosted-publication and disposable end-to-end boundary. C11b implementation is now integrated on `agent/auto-updater-20260928` at exact commit `08054d96a8ba84819b6dfd775d56f9eeabc7986d`: immutable main-only GitHub Release publication policy, deterministic ZIP/build identity, exact two-asset publication, stale-main/evidence-only/monotonic-build refusal, no-clobber semantics, a final `origin/main` recheck immediately before irreversible publication, plus updater metadata/rollback LR-003 native-replacement coverage. Local heaven/Windows/.NET 10.0.401 verification for that exact commit passed `Verify-Release.ps1` **25/25**, FunctionVerifier **727/727**, Core **79/79**, Automation **24/24**, Integration **173/173**, self-test **11/11**, strict analyzers, ReadyToRun app publish, self-contained updater-helper publish, updater package verification, and publication-policy tests. `Build-Release.ps1` produced updater build **227** and ZIP SHA-256 `09ADF5E9B6C7C832633D7E5BA7C4DD2AA3EAB29F347E540B79542B731CD3665C`. No GitHub Release was published from this branch. Remaining work: integrate to eligible `main`, obtain the hosted Windows publication closure for that exact main SHA, verify the immutable release/assets, then run disposable real old→new plus injected-rollback tests with seeded user data. Preserve the permanent continuity constitution and active Learned Rules; require the successor to preserve and recursively propagate them to the agent after them.

Checkpoints C4-C9 are complete on the working updater branch through exact production/test commit `8cdf54d0bc4065a55124aef96c68b26de2a78f0c`. C8 reproduced a real post-preflight ownership race: a user file created at a newly introduced product path was silently overwritten. New product paths now publish with atomic no-overwrite rename semantics; same-process rollback tracks only newly published paths so the raced user file survives while prior mutations restore. Crash recovery remains conservative because this ephemeral progress is never invented after process loss. The same checkpoint also fixed an inherited updater-helper direct `Process.Start` bypass discovered by the full integration policy test. Windows/.NET 10.0.401 evidence: **58/58 updater tests**, **3/3 native 1175/1176/1177 fixtures**, **154/154 full integration**, strict whole-solution build **0 warnings / 0 errors**. No full updater release gate applies yet. The next bounded updater work is updater-specific LR-003 partial-native-replacement recovery characterization, followed by WPF integration.

---
# Company programming-agent trainer — ACTIVE LIVING SYSTEM

A project-agnostic engineering trainer now lives in `_AGENT_TRAINING/`. It captures reusable company values, agent operating standards, development pipeline, role boundaries, multi-agent coordination, continuity/recovery, verification, destructive-operation safety, CI/release doctrine, knowledge maintenance, prompting guidance, and reusable role prompts.

This is a documentation/governance layer only; it does not change MHW product behavior or inherit/replace product verification. Future agents must read `_AGENT_TRAINING/README.md` during startup and evaluate reusable lessons for trainer updates at meaningful checkpoints. Project-specific architecture, current bugs, verification, branches, and next steps remain in `_AGENT_CONTEXT/`.

# 2026-09-28 support integration — CLOSED / hosted Windows verified

Canonical main now contains the recursive-source reparse hardening, its adversarial parity/root/cycle follow-up, archive extraction trusted-root physical containment, the associated Windows regressions, three independent support audits, and the durable LR-010 fail-before-mutation rule. Exact integrated source `c6c70dd2f8db760ad236b0188cc7026a502afb7a` passed local `Verify-Release.ps1` **25/25**, functions **615/615**, call sites **6532 / 0 uncovered**, Core **79/79**, Automation **24/24**, Integration **96/96**, self-test **11/11**, strict analyzers, and ReadyToRun release publish. Local ZIP SHA-256: `359B12050437AEF0EE9695FEFCE4B8424475413EF54299B2DAC345F5C4E32B21`.

Hosted Windows Release Gate `36392282315` passed exact production integration commit `d66bff290f197236ec43c9b37d2b015ab2ee5fe8`; evidence/cache persistence is `a94066004660e4d542f5d4a528c7e5e22bdea9cb`, and the hosted artifact SHA-256 is `A264C5108DDEA0E3301BFF0E33D7A3E7DA3A92A566739C865AE28B8111BFAFAB`. The recursive-source and archive physical-root implementation boundaries are closed. The separate runtime-confirmed archive streaming cancellation/resource-budget audit remains unimplemented.

See `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-28.md`.

# CAS integrity checkpoint — CLOSED

The CAS byte-integrity/concurrency repair is closed. Hosted Windows Release Gate `36367883836` passed exact repair candidate `d001870d4cd3549841d8511392ae7885f174bca2`. Fresh local Windows closure then passed on canonical source `3556bddcd7c7f84c0efe2ff92f6d73e12842128f`: focused `BlobIntegrityTests` **10/10**, verifier **25/25**, functions **613/613**, call sites **6494 / 0 uncovered**, Core **79/79**, Automation **20/20**, Integration/fault injection **89/89**, self-test **11/11**, strict analyzers/build PASS, and win-x64 ReadyToRun publish PASS. Local release SHA-256: `54C53313567D96E0FE937746FE0F323E229717CE7CE296694FEC151F2E73FC79`.

Remote Desktop Commander omitted the normal `OS=Windows_NT` environment variable, causing the first unchanged verifier invocation to report two Windows-only stages as skipped (23/25). The host was independently confirmed as Win32NT / `IsOSPlatform(Windows)=True`; rerunning the unchanged scripts with the standard process-local marker restored produced the green results above. The successor must inherit, preserve, and recursively propagate the permanent continuity constitution and active Learned Rules. Do not break the chain.

## Latest archive resource/cancellation support audit — documentation only

A specialized archive-ingestion audit at canonical base `831da365c0c67e0239ad668f60fe3c78513dc63b` runtime-reproduced a cancellation defect in `ArchiveInspector.ExtractSafelyAsync`: cancellation requested during a single 1 GiB compressed entry was not observed by the synchronous `WriteToFile`; extraction wrote the full 1 GiB and returned success. The audit also records that the 200 GiB metadata ceiling is not tied to destination free space and is not enforced against actual streamed output bytes. No production source/tests/caches changed. Read `_AGENT_CONTEXT/ARCHIVE_EXTRACTION_RESOURCE_CANCELLATION_AUDIT.md`. The reusable lesson was also promoted into `_AGENT_TRAINING/SAFETY_AND_DESTRUCTIVE_OPERATIONS.md` with provenance recorded in `_AGENT_TRAINING/PROVENANCE.md`. Keep this future checkpoint separate from the now-integrated recursive-source and archive physical-root containment work.

## Latest CAS support-audit harvest — documentation only

Integrated durable research from PRs #23, #25, and #22: CAS filesystem identity/reparse safety, CAS digest namespace validation, and recursive source reparse containment. PR #24 was reviewed but skipped as redundant with the broader filesystem-identity audit; its root/hash-leaf fixture guidance was already covered there. No production C#, tests, verification scripts, workflows, or verification caches changed. The CAS local Windows closure has since completed; these audits now remain future independently scoped work. Canonical integration commit: `1dc4655b2ade03338ef826690d8ed1dc5d31fa14`.

---
# Native ReplaceFileW failure-postcondition boundary — CLOSED / hosted Windows verified

Final exact verified commit: `17abfb05d83ff38040eb9356d34fbb3131644801`.  
Production implementation merge: `6d52ede722f18fcdbe727ec44e027de3e1c69fb1`.  
Hosted Windows Release Gate: `36342205103`.  
Workflow evidence/cache persistence: `689a17ce5dff18bd0bf1201205446edd51ada8b5`.

Final exact evidence:

- Windows X64 / .NET SDK 10.0.401;
- repository verifier **25/25 PASS**;
- production fingerprints **612/612** promoted;
- explicit call sites **6480**, uncovered **0**, trace gaps **0**, parse errors **0**;
- Core **79/79**;
- Automation **20/20**;
- Integration/fault injection **79/79**;
- strict solution/analyzer verification PASS;
- ReadyToRun self-contained win-x64 publish PASS with fallback **False**;
- release ZIP SHA-256: `B88694E81A35DCFF0C8FF76EBD07908ECA47B0AA2777ABE26B38686635373468`.

Closed behavior:

- `AtomicFileOps.ReplaceFromAsync` has a narrow injectable native replacement backend; normal production behavior still uses `ReplaceFileW`;
- error 1175 is characterized as recoverable without pathname mutation and ends with exact BEFORE bytes plus operation/journal `RolledBack`;
- documented partial-name-mutation errors 1176/1177 retain the staged AFTER bytes instead of deleting the only known replacement recovery material;
- 1176/1177 remain fail-closed as operation `RecoveryRequired` / journal `Writing`; no destructive guessing was added;
- the final tests assert filesystem bytes/path existence and durable operation/journal state.

No new Learned Rule was required because LR-003 already states the governing invariant.

## Follow-up support research integration — CLOSED / hosted Windows verified

Exact integration commit: `027b6d9dc9b049d9e9857e5a0e4d021e31adf443`.  
Hosted Windows Release Gate: `36343967045`.  
Workflow evidence/cache persistence: `dadbe73a48567b17c9814c483f654be00d1d810f`.

This documentation/continuity-only support harvest integrated later PRs #14, #15, #16, and #18 without replaying stale branch-local handoff snapshots. It added launch-health/game-build revalidation, mod-lifecycle referential integrity, crash-bisector diagnosis evidence, and import-publication/catalog-visibility audits; canonical Learned Rules now extend through LR-009.

Exact hosted evidence:

- repository verifier **25/25 PASS**;
- handoff continuity preflight **PASS**;
- functions **612/612**; explicit call sites **6480 / 0 uncovered**; trace gaps / parse errors **0 / 0**;
- Core **79/79**, Automation **20/20**, Integration/fault injection **79/79**, self-test **11/11**;
- strict whole-solution and App win-x64 compile/analyzers PASS;
- self-contained ReadyToRun publish PASS, fallback **False**;
- release ZIP SHA-256: `DC5A5F8DA92BE6A7469F3C6072BA6A555E5AAF5FDE25439D064CD115BA201BD6`.

No production C#, tests, verification scripts, workflows, or support-branch implementation code were merged in this follow-up. `agent/support-diagnostic-privacy-audit-20260927` was skipped as redundant with the already-canonical diagnostics-privacy and remote-preview trust audits. Support branches remain preserved.

That support-integration pass preserved CAS integrity as the next boundary at the time; CAS has since closed with hosted and local Windows evidence. Do not combine the new recursive source reparse boundary with launch-health, retirement, diagnosis, import publication, CAS filesystem-identity/digest-namespace work, migration, async, diagnostics, networking, backup, or Smart Pack work.

## Historical next boundary — completed 2026-09-28

Recursive source reparse containment for ModScanner, unmanaged adoption, and Smart Inbox was the next boundary at this historical checkpoint and is now integrated and hosted-Windows verified. The current separate candidate is archive streaming cancellation/resource budgeting.

The successor inherits the permanent continuity constitution and active LR-001 through LR-010, and must explicitly require its own successor to recursively propagate them to the agent after them.

**Do not break the chain.**

---

# Windows live DeploymentExecutor physical containment — CLOSED / hosted Windows verified

Exact verified integration commit: `356fde242046b78e39c7266c57b27e52220141fa`.  
Hosted Windows Release Gate: `36341049469`.  
Workflow evidence/cache persistence: `dc7eb83c94427479c59413c77935050dadf051ff`.

Final exact evidence:

- Windows X64 / .NET SDK 10.0.401;
- repository verifier **25/25 PASS**;
- production fingerprints **611/611** promoted;
- explicit call sites **6478**, uncovered **0**, trace gaps **0**, parse errors **0**;
- Core **79/79**;
- Automation **20/20**;
- Integration/fault injection **76/76**;
- automation self-test **11/11**;
- strict Filesystem/App and whole-solution analyzers/builds PASS;
- ReadyToRun restore and self-contained win-x64 publish PASS;
- release ZIP SHA-256: `F7CBC330D652835FFBC6A24395D105FF509F3800FE21E741FBDD9BAE7D94433D`.

Closed behavior:

- live deployment rejects an existing descendant reparse/junction component before capture/hash/precondition work;
- Add/Replace/Remove re-check containment immediately before live mutation;
- rollback and startup recovery reject unsafe topology before inspection and before restore/delete;
- restart recovery encountering a newly introduced parent junction fails closed into `RecoveryRequired` without touching the external target;
- empty-directory pruning and Restart Manager lock inspection share the same containment guard;
- focused Windows tests use real directory junctions and cover Add, Replace, Remove and crash/restart recovery.

The configured `gameRoot` remains the trusted anchor. Descendant reparse components are rejected. This remains path-based hardening, not handle/file-ID atomic containment: a topology swap after the final attribute check is a documented residual TOCTOU.

No new Learned Rule was needed: LR-004 already encodes this invariant.

## Exact next boundary

Do not broaden this work into CAS/scanner/migration/network/UI changes. The next recommended programmer boundary is **native `ReplaceFileW` failure-postcondition characterization** under LR-003, kept independently verifiable.

The successor inherits the permanent continuity constitution, active LR-001 through LR-006, and must require its own successor to recursively propagate them again.

**Do not break the chain.**

---


# Parallel support-agent integration — documentation/continuity checkpoint

Integration base: canonical `main` at `6ada5a5c4cc83afadfba42bc6af6559540920e3d`.

The recent support branches were inspected from actual branch-vs-main diffs and branch file contents. Durable inventory and disposition:

- `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-27.md`

No examined support branch contained production C# or test changes relative to the integration base. The integrated work is documentation, research, one migration-guide correction, and durable Learned Rules. Stale branch-local copies of canonical continuity state were deliberately not merged wholesale.

Integrated specialized knowledge now includes:

- async/background lifetime, shutdown and staged-draft risks;
- Windows physical containment/reparse, CAS and native replacement safety;
- broad test/failure/performance gaps;
- legacy migration retry/ownership/cleanup/fidelity risks;
- diagnostics share-boundary privacy and secret handling;
- remote preview egress/redirect/HTTP/response validation;
- verification exact-input/stage-cache/CI supply-chain risks;
- full-state backup/portability recovery design;
- source-neutral multi-provider bulk-fill design;
- MHW semantic physical-slot coverage/gap-fill design;
- an independent MainWindow re-audit that reaffirms the stop-page-model-splitting decision.

`docs/MIGRATION.md` now describes the current restart protocol, backup limits, completion marker/run-status split, best-effort cleanup, and known hardening work.

Active Learned Rules are now **LR-001 through LR-006**. The integration resolved one parallel numbering collision by keeping migration as LR-005 and renumbering diagnostics export sanitization to LR-006.

## Verification status for this checkpoint

Parallel support-audit integration is **CLOSED and hosted-Windows verified**.

- exact verified integration commit: `5619604e88a27176726ada8518f53d385abc7b0f`;
- hosted Windows Release Gate: `36340312353`;
- runner / SDK: Windows X64 / .NET 10.0.401;
- repository verifier: **25/25 PASS**;
- agent-handoff continuity preflight: **PASS**;
- release build/publish: **PASS**;
- ReadyToRun fallback used: **False**;
- release ZIP SHA-256: `43C753174810650A4C4F8956F4329CD5A21A45B9253EDAE30E16EE0551F1FEBF`;
- evidence/cache persistence commit: `4de981ab7ee47a3a8f0dd60f38e17644517b2ea3`.

Production executable source and tests were unchanged by this integration, so the PlannerSnapshotRepository production behavior remains the same as the previously closed boundary. The new hosted run nevertheless verified the changed handoff/continuity inputs on their exact repository SHA. No cache was manually promoted outside the normal gate.

A local `git status` / local runtime run was unavailable during integration because the connected Remote Desktop Commander device was offline. Remote GitHub state plus the hosted Windows gate provide the authoritative closure evidence.

## Next boundary

Do not reopen PlannerSnapshotRepository, continue page-model splitting, or bundle all findings into one production change. Read `NEXT_STEPS.md`.

The highest-safety candidate identified independently by the test-gap and Windows-filesystem audits is a **test-first Windows physical-containment / native-`ReplaceFileW` characterization checkpoint**. Re-check current `main` before acting and keep that checkpoint independently verifiable.

The successor must inherit the permanent continuity constitution, obey LR-001 through LR-006, update durable handoff state, and explicitly require its successor to propagate the same rules to the agent after them.

**Do not break the chain.**

---

# PlannerSnapshotRepository boundary CLOSED — hosted Windows verified

Exact verified repository commit: `efe58f38c4780d40200bcf2b7bbb5914ecd8ebc3`.
Last production repair source: `528401925b1d09b3d65c9652de8e4f2024e3677f`.
Hosted Windows Release Gate: `36336190920`.
Workflow evidence/cache persistence: `852f07b9d6ad0457c161df0aa1c8165981d349cf`.

Final evidence:

- Windows X64 / .NET SDK 10.0.401;
- continuity preflight PASS and all four recursive-continuity negative fixtures rejected;
- repository verifier **25/25**;
- production fingerprints **610/610** promoted;
- explicit call sites **6456**, uncovered **0**, trace gaps **0**, parse errors **0**;
- Core **79/79**;
- Automation **20/20**;
- Integration/fault injection **72/72**;
- self-test **11/11**;
- App win-x64 compile/analyzers PASS;
- ReadyToRun restore and self-contained publish PASS;
- release ZIP SHA-256: `226DFA5C4E21A184B8895D27EAA069046B71A33CA50F1CE224EC06BBF908D12E`.

The extraction remains exactly the intended read-only boundary:

- `PlannerSnapshotRepository` owns planner-input query assembly;
- `ManagerDatabase.GetModsAsync` remains in `ManagerDatabase`;
- mod state is still read first through its own connection, then files/rules/resources/manifest/originals through a second `OpenAsync` connection;
- full, filtered, empty-filter, cancellation, and representative planner-output parity are regression-covered;
- the legacy first-casing SQLite filter behavior is deliberately preserved rather than redesigned;
- `DeploymentExecutor` and all documented write-side SQLite transaction owners were untouched.

Historical failed attempts remain useful evidence:

- `36335255922`: two LR-001 trace gaps plus two missed callers (`NexusMetadataService`, `GameBuildMonitor`); compiler-backed closure produced LR-002.
- `36335692754`: **24/25**; production checks were clean and the only failure was an incorrect new test expectation about preserved filter casing behavior.

No second production boundary was started.

The next agent must inherit the permanent continuity constitution, obey active Learned Rules, preserve exact verification and transaction invariants, and explicitly require its successor to pass the same rule system onward again.

**Do not break the chain.**

---

# Governance checkpoint CLOSED — permanent recursive continuity

A governance-only checkpoint now installs the repository-level continuity constitution requested by the user.

- `AGENTS.md` remains concise and points every agent into the permanent system before edits.
- `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` is the permanent Core-Rule constitution. Recursive propagation itself may be weakened only with explicit user authorization.
- `_AGENT_CONTEXT/LEARNED_RULES.md` is the append-only Active/Superseded ledger for durable incident-driven rules.
- `scripts/Test-AgentHandoff.ps1` validates the invariant by concept rather than exact prose.
- `scripts/Test-AgentHandoff-NegativeFixtures.ps1` deliberately breaks learned-rules linkage, successor propagation, and Core-Rule protection and requires the real validator to fail closed.
- `scripts/Verify-Release.ps1` runs both the positive handoff preflight and negative fixtures.
- No production C# changed. The previously closed product baseline remains exact commit `106a4569b572473394aa075bcfa5d9c03f2fe44d`, hosted run `36331057943`.
- Hosted Windows Release Gate `36333960215` verified exact governance commit `73f1298455ec4c651e211488ececf9803504e60d` on Windows x64 / .NET SDK 10.0.401.
- `scripts/Test-AgentHandoff.ps1` passed; the baseline negative-fixture copy passed; all four recursive-continuity negative fixtures were rejected as intended.
- Repository verification finished **25/25** with **615/615** production fingerprints, **6389** explicit call sites / **0** uncovered, Core **79/79**, Automation **20/20**, Integration/fault injection **66/66**, self-test **11/11**, App win-x64 analyzers PASS, and ReadyToRun publish PASS.
- Release SHA-256: `8A8D78DA53AE81703091683F5BC25D298C7BDEE3FB831098040D91EB2F85AAF4`.
- Workflow evidence/cache persistence commit: `6bc50de3f07015b63c58ac6bfba3b7bfce9a104c`.
- The next product boundary is now **PlannerSnapshotRepository only**.

Every future handoff must explicitly require the successor to preserve and recursively pass this system to the agent after them.

**Do not break the chain.**

---

# Current state — v8.8.0

## MainWindow re-audit / storage transaction design COMPLETE — documentation checkpoint

Canonical audit base: `0129607a0558da6a596e1688a04f3051e5f6ce40`.

No production source changed in this checkpoint. The closed hosted Windows
verification therefore still applies to exact commit
`106a4569b572473394aa075bcfa5d9c03f2fe44d`, with last production source
`fdbe9b71f29b1c4c7d9fcd061a23c2ca75fa3e34`.

The remaining `MainWindowViewModel` responsibilities were re-audited by
state ownership and coupling. Decision: **stop page-model splitting now**.
Activity, Coverage, Profiles, and Games are the clean passive/read seams already
closed. Mods/ModsView, Conflicts, IssueSuspects, Overlaps, staging/apply,
Nexus/import, profile mutations, launch/crash diagnosis, health/support and
global busy/status are either cross-feature workflows or mutually coupled state.

Durable audit:
- `_AGENT_CONTEXT/MAINWINDOW_RESPONSIBILITY_AUDIT.md`

The fallback ManagerDatabase transaction-boundary audit is also complete.
Critical existing transaction owners are documented, especially
DeploymentExecutor prepared-journal, final commit, rollback, mod-file replacement,
manual family chaining, profile save, adoption, trust and issue batches.

Durable audit:
- `_AGENT_CONTEXT/STORAGE_TRANSACTION_BOUNDARY_AUDIT.md`

Exactly one first storage extraction is recommended: a **read-only
PlannerSnapshotRepository** containing the existing
`LoadPlannerSnapshotAsync` query assembly. Do not combine it with another
repository move, transaction redesign, Generic Host/DI work, or MainWindow
decomposition. Preserve the current query and connection semantics on the first
move, add focused parity tests, then require a fresh full Windows Release Gate
before any second production change.


## Games list-presentation slice CLOSED — hosted Windows

The Games extraction is fully green.

- exact verified commit: `106a4569b572473394aa075bcfa5d9c03f2fe44d`
- last production-source change: `fdbe9b71f29b1c4c7d9fcd061a23c2ca75fa3e34`
- Windows Release Gate: `36331057943`
- repository verification: **25/25 PASS**
- production fingerprints: **615/615 promoted**
- explicit call sites: **6389**, uncovered **0**
- trace gaps / parse errors: **0 / 0**
- Core: **79/79**
- Automation: **20/20**
- Integration/fault injection: **66/66**
- self-test: **11/11**
- App win-x64 compile/analyzers: PASS
- ReadyToRun self-contained publish: PASS
- release ZIP SHA-256: `4872ABDA6D548CB9F97668AF1A3019AC44B146A9A66876F92B065D7009455189`
- evidence/cache persistence commit: `750a3232ad9ac82bd1587ddd903709b886c9b8bb`

The first Games run `36330808544` is superseded. It reached 24/25 and exposed
the one missing entry trace in the newly moved `ScanInstalledGames` method.
The final source adds that trace and a regression assertion.

The verified architecture keeps only passive list state in
`GamesPageViewModel`. Selection, registry mutation, discovery, active-game
changes, restart/shutdown, busy/status coordination, AppPaths discovery, and
startup service reconstruction remain shell/application responsibilities.

## Profiles read/list page-view-model slice CLOSED — hosted Windows

The Profiles read/list extraction is fully green.

- exact verified commit: `04bc05779f5d94fa3e2e8cc3bf80fbc6fbed09b8`
- last production-source change: `19a1ad4a4e665e4ce7f38586dea5f08f1c3acdf0`
- Windows Release Gate: `36328183152`
- repository verification: **25/25 PASS**
- production fingerprints: **613/613 promoted**
- explicit call sites: **6385**, uncovered **0**
- Core: **79/79**
- Automation: **20/20**
- Integration/fault injection: **65/65**
- self-test: **11/11**
- App win-x64 compile/analyzers: PASS
- ReadyToRun self-contained publish: PASS
- release ZIP SHA-256: `A150FFA7B832C56535A9CA19DCFEDD7640C3BE1F8E9ACB4D40881CB8C69F93B8`
- evidence/cache persistence commit: `122bcdb525bb432e73dff6f5887a63e065246964`

This verifies `ProfilesPageViewModel` owning profile list reads/state while
preserving the legacy `Profiles` / `RefreshProfilesCommand` binding surface.
Profile mutations, selected-profile behavior, mod staging/application, and
shell-global busy/status ownership remain in `MainWindowViewModel`.


## Coverage page-view-model slice CLOSED — hosted Windows

The Coverage extraction is fully green.

- exact verified commit: `e3ed3be730000d1829b02e5d2d29b3f23ca52d94`
- last production-source change: `855f6e5eb4998aa442538636b76f5c644146eb6a`
- Windows Release Gate: `36327634813`
- repository verification: **25/25 PASS**
- production fingerprints: **611/611 promoted**
- explicit call sites: **6379**, uncovered **0**
- Core: **79/79**
- Automation: **20/20**
- Integration/fault injection: **64/64**
- self-test: **11/11**
- App win-x64 compile/analyzers: PASS
- ReadyToRun self-contained publish: PASS
- release ZIP SHA-256: `40B47B6E3C9CF21A0945095FE28E118540B415FBD9177190BDB99FE80C9657A8`
- evidence/cache persistence commit: `e62e7ad5d93cdb6c6ae3d6e8562d6fae667e9c02`

This verifies the real `CoveragePageViewModel` extraction while preserving the
legacy `OutfitRows` / `RefreshOutfitsCommand` binding surface and keeping
shell-global busy/status ownership in `MainWindowViewModel`.


## Activity page-view-model slice CLOSED — hosted Windows

The first post-closure architecture slice is fully green.

- exact verified commit: `5eab48f0a2139e3aee96a7c71e4466d2e1168877`
- last production-source change: `e2396c7c91c5d8d88fe229603689539b5cdfb2da`
- Windows Release Gate: `36325994246`
- repository verification: **25/25 PASS**
- production fingerprints: **609/609 promoted**
- explicit call sites: **6375**, uncovered **0**
- Core: **79/79**
- Automation: **20/20**
- Integration/fault injection: **63/63**
- self-test: **11/11**
- App win-x64 compile/analyzers: PASS
- ReadyToRun self-contained publish: PASS
- release ZIP SHA-256: `7ED67747DADE8BD3E56D30139BF39886F5E0A293F9CAE9B2887D06D720F589AF`
- evidence/cache persistence commit: `f0221e545ab4bf75b989d985dc3e5a90e2faa6fc`

This verifies the real `ActivityPageViewModel` extraction while preserving the
legacy `ActivityRows` / `RefreshActivityCommand` binding surface. The malformed
intermediate run `36325764389` remains superseded historical evidence only.

## Activity candidate serialization correction

The first Activity extraction source commit `e2396c7c91c5d8d88fe229603689539b5cdfb2da` accidentally
contained literal `\\n` text in two generated replacement strings inside
`MainWindowViewModel.cs`. This was detected by source inspection before relying
on CI. Run `36325764389` is superseded.

Corrected production source: `e2396c7c91c5d8d88fe229603689539b5cdfb2da`.

The corrected source contains normal C# declarations and constructor statements.
No intended Activity architecture or runtime behavior changed; only the connector
serialization defect was removed. A fresh complete Windows Release Gate is
required for this corrected source.

## Activity page-view-model candidate — awaiting hosted Windows verification

Production source commit `e2396c7c91c5d8d88fe229603689539b5cdfb2da` begins the first post-closure
architecture slice.

- Added `ActivityPageViewModel` to own recent Activity read projection and row
  collection state.
- `MainWindowViewModel` composes the page model but continues exposing the
  same `ActivityRows` collection reference.
- `RefreshActivityCommand` and `RunBusy` remain in `MainWindowViewModel`,
  preserving cross-page/global operation coordination.
- The existing XAML binding surface is unchanged.
- A source-level integration guard asserts the page seam and the preserved
  bindings.
- No database/deployment/conflict/filesystem semantics changed.

This candidate does **not** inherit the closed green state from
`9717a22d3338f77e63cd409a80d2ec5fc3c924f2`. A fresh Windows gate is required.

## Architecture / Explain Why milestone CLOSED — hosted Windows

The integrated architecture/Explain Why checkpoint is now fully closed.

- exact verified commit: `9717a22d3338f77e63cd409a80d2ec5fc3c924f2`
- last production-source change: `098d617bcb3dcdd044e3fdb8319ba506c97082af`
- GitHub Actions Windows Release Gate: `36325133722`
- repository verification: **25/25 PASS**
- production function fingerprints: **607/607 promoted**
- Core tests: **79/79**
- Automation tests: **20/20**
- Integration/fault injection: **62/62**
- automation self-test: **11/11**
- win-x64 compile/analyzers: PASS
- self-contained ReadyToRun publish: PASS
- release ZIP SHA-256: `DF87A48716596ABFFF545DD6C73BAAE02954167424908850D943BFFA3833A2D6`
- promoted-cache/evidence persistence commit: `702c9055bff19caa80fdd60e29e891181932217a`

The previous CA1826 failure was repaired by direct `IReadOnlyList` Count/indexer
access in overlap primary-path selection; no resolver/deployment semantics changed.
This checkpoint is the required stable base for the next incremental architecture
slice. Any new production-source edit invalidates the applicable source evidence
until a fresh verifier/gate run confirms the changed fingerprints.
## Architecture candidate release-gate follow-up — 2026-09-27

GitHub Actions run `36324750213` on integrated main checkpoint
`35abe5c7425456675086cdc38455a8447d3560c5` passed the exact repository
verification gate, including the continuity preflight, strict builds/tests,
Automation **20/20**, Integration/fault injection **62/62**, and all **11**
self-tests. The release stage then failed at the dedicated win-x64
compile/analyzer gate on a single diagnostic:

- `MainWindowViewModel.Overlaps.cs:22` — CA1826, LINQ
  `FirstOrDefault()` used on indexable `IReadOnlyList<string>`.

Production source commit `098d617bcb3dcdd044e3fdb8319ba506c97082af` changes only that expression to
`Count` + indexer access while preserving the same empty-list fallback to the
asset key. No deployment, conflict-resolution, database, or filesystem safety
semantics changed. A fresh complete Windows Release Gate is required before the
architecture/Explain Why milestone can be marked closed.

## Architecture / Explain Why candidate — 2026-09-27

Working branch: `agent/architecture-explain-why`. Candidate production source:
`5c1937e557aa9996cef493709e94a6aa611d4e1c` (later branch commits update handoff docs only).
This candidate is **not yet Windows-verified** and must not inherit the closed v8.8
fingerprints simply because its parent release was green.

Implemented in this candidate:

- `PresentationReadRepository` owns Activity and Outfit/Coverage read projections that
  were previously handwritten SQL inside `MainWindowViewModel`.
- `ArchiveImportService` owns archive validation, quarantine extraction, single-wrapper
  normalization, destination naming, publication into the mod library, and catalog refresh.
- `EffectiveInspectorService.ExplainWhyAsync` replays the configured `DeploymentPlanner`
  and exposes the resulting `ConflictDecision` plus applied-manifest state, provider
  priority, logical family role, Nexus lineage, provenance, confidence, score and evidence.
- The Overlaps tab now supports `Explain selected` with progressive detail instead of
  requiring users to infer resolver behavior from overwrite rows.
- Activity, Coverage, Import, and Overlap/Explain methods moved into partial feature
  files. The central `MainWindowViewModel.cs` fell from about 72.6 KB to 65.7 KB while
  retaining the same WPF binding type and commands.
- Regression coverage was added for planner-backed Explain Why, presentation reads,
  and the new XAML binding surface.

No deployment executor, CAS, journal, rollback/recovery, TOCTOU, `ReplaceFileW`, or
live-tree safety semantics were redesigned. FOMOD and the enhanced-game adapter redesign
remain explicitly out of scope.


## Latest state: 2026-09-27 repair audit

The exact parent is the supplied v8.8.0 FunctionVerification ZIP (four trace-scope
fixes already included). See `AUDIT-2026-09-27.md` for the new source changes.
SDK 10.0.401 strict Release solution build passed with zero warnings/errors;
Core 79/79, Automation 18/18, Integration 61/61, self-test 11/11 passed on Linux.
Windows validation is still required. The current scan persists all 602 function
entries: 579 exact known-good true and 23 changed/new false, with zero gaps.
The original seven stage-cache records are retained byte-for-byte; six still match
their Windows fingerprints, while FunctionVerifier changed and must rerun.
No local Linux results were imported as Windows release checks or function promotion.

Sections below document the preceding revisions, not the latest verification status.

## What changed from v8.7.0

Product behavior and the universal-game architecture were intentionally left largely intact. This is a hardening/versioning release centered on verification and diagnostics.

- Version bumped to `8.8.0` / `8.8.0-function-verification`.
- Added `MhwModManager.FunctionVerifier` to the solution.
- Added persistent per-function/body boolean verification state.
- Added a read-only v8.7 source/hash bootstrap so unchanged functions can stay known-good even when another function in the same file changes.
- Changed/new production executable bodies must have an entry method trace or an explicit recursion exemption.
- `MasterDebugLog` now maintains an async-flow scope chain and records first-chance exceptions against all active scopes.
- Scope completion records `PASS-CHECK`, `ERROR-CHECK`, or `PASS-WITH-ERROR-CHECK` as appropriate; exceptions are never swallowed by this mechanism.
- `Verify-Release.ps1` scans before compilation and only promotes the cache after all required verification stages pass.
- `Build-Release.ps1` delays cache promotion until the complete Windows build/test/self-test/publish path succeeds.
- Added integration regressions for the new fail-closed verification behavior.

## Source delta

Relative to the supplied v8.7 archive, the deliberately modified product C# files are primarily:

- `src/MhwModManager.Core/MasterDebugLog.cs`
- `src/MhwModManager.App/App.xaml.cs` (version/trace message)
- `src/MhwModManager.Diagnostics/AppLogging.cs` (version)
- `src/MhwModManager.Filesystem/NexusMetadataService.cs` (User-Agent version)

Plus verification tests, scripts, solution/project metadata, docs, and the new verifier tool.

## Verification state at handoff

This environment did **not** contain the pinned .NET SDK and network access could not resolve Microsoft's SDK host, so the authoritative .NET 10.0.401 compile/test run could not be executed here. Static checks were performed; see `VERIFICATION.md`.

Therefore `.verification/function-status.json` is intentionally still a bootstrap cache. Do not manually mark the new v8.8 bodies verified. Run the Windows verifier first.

## Re-audit hardening added after the first v8.8 package

- Added mandatory propagating agent continuity protocol, machine-readable handoff manifest, handoff preflight, and source-handoff packager.
- Function verifier excludes generated `src/**/bin/**` and `src/**/obj/**` C# so repeat builds cannot create false new-function failures.
- Trusted v8.7 source zip is now hash-validated against its manifest during every scan; missing/unmanifested/mismatched source fails closed.
- Function IDs now distinguish explicit-interface members to avoid cache collisions.
- Function reports include explicit call-site counts and whether those call sites are covered by a known-good/traced/exempt containing body.
- First-chance exceptions are still observed by every active runtime function scope, but full first-chance stack logging is opt-in with `MHW_FIRST_CHANCE_DETAIL=1`; this avoids mandatory disk I/O for every handled throw.
- `MasterDebugLog` now has a thread-static first-chance recursion guard and writes the total first-chance count at process exit.

These are hardening changes inside the existing 8.8.0 source handoff, not a product-feature redesign.

## Windows verification evidence received after packaging

The user ran `Test Everything.bat` on Windows with .NET SDK 10.0.401 and supplied `_AGENT_CONTEXT/EVIDENCE/v8.8.0-first-windows-verification.log`. That run established real partial evidence instead of only static inspection.

Confirmed PASS on that exact source/input state:
- PowerShell syntax sweep, report serialization preflight, agent-handoff preflight, and restore.
- strict compile/analyzers: Core, Storage, Mhw, Diagnostics, Automation, UnitTests, Benchmarks.
- Core unit tests: **79/79 passed**.

Failures exposed by the run and fixed in the current package:
- FunctionVerifier accepted only `ParameterListSyntax`; indexers use `BracketedParameterListSyntax`. It now accepts `BaseParameterListSyntax`.
- FunctionVerifier nested DTO `FormatVersion` initializers accidentally referenced their own instance property. They now qualify `Program.FormatVersion`.
- `GameProfileEditorWindow` used invalid two-argument WPF `Thickness` constructors and lacked `System.IO` for `Path` / `PathTooLongException`.
- tests/self-test still called the pre-v8.7 `SaveBackupService` constructor and two integration tests passed raw game-root strings into services that now require `GameProfile`.
- strict analyzer CA1859 on private Steam/GOG discovery methods; private discovery methods now return concrete `List<DiscoveredGame>`.

### Checked-state behavior now

`.verification/function-status.json` is a true/false function checklist rewritten on every scan. Safe exact unchanged functions are checked immediately even if later unrelated stages fail. `.verification/stage-status.json` stores independently passing strict build/test stages by exact project/dependency/toolchain fingerprint. The current package pre-checks only evidence whose fingerprint is still identical after the fixes: Core, Storage, Mhw, UnitTests, Benchmarks, and the 79/79 Core test stage. Diagnostics/Automation are intentionally not pre-checked because Filesystem changed.

The source-handoff packager was also corrected to exclude `SOURCE_HANDOFF_MANIFEST.json` from its own file-hash inventory; otherwise it could hash the previous manifest and then overwrite it. The continuity preflight now validates both verification-cache schemas.

## Second authoritative Windows run and verification-closure patch

The user ran the corrected v8.8.0 verifier again on Windows/.NET 10.0.401. Result: **24 passed / 1 failed**. The relaxed whole solution and strict whole solution both compiled with **0 warnings / 0 errors**; Automation tests passed **18/18**; Integration + fault-injection passed **43/43**; the full automation self-test passed every listed check. The sole failure was the function fingerprint scan: 602 functions total, 582 known-good, 20 changed/new, with exactly four trace gaps (`GameProfileEditorWindow.AddField`, `GameProfileRegistry.DiscoverSteam`, `DiscoverEpic`, `DiscoverGog`). Those four gaps accounted for all 69 uncovered explicit call sites.

This packaged revision adds only `MasterDebugLog.BeginMethod()` entry scopes to those four functions. No functional game/deployment behavior was intentionally changed. Since `GameProfileEditorWindow.cs` and `GameProfileRegistry.cs` changed, stage-cache entries whose dependency fingerprints include App/Filesystem are intentionally invalidated on the next run. Exact unaffected green evidence remains cached; `strict:FunctionVerifier` is now also pre-checked from the second run because the verifier project itself is unchanged by this patch.


## Independent SQLite transaction audit support finding

Support Agent 1 completed an independent static cross-check of SQLite write ownership and filesystem/DB atomicity. Durable findings are in `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`; the canonical storage audit links it. Audit document commit: `5fd7f41f6229474907aa751fd410bcf32dbe2d50`; link commit: `d6cd98c5fb85207530ce433443e32eab7f5a50b5`.

The deployment journal/final-commit/rollback transactions were independently confirmed. Four non-deployment consistency defects/gaps were documented for later isolated checkpoints: duplicate cleanup move-before-delete recovery, trust-vs-launch-history split persistence, snapshot-prune DB/payload drift, and legacy migration run-status closure. No production source was changed by this support audit, and no local/hosted Windows execution is claimed for it.

Any successor working on these findings must preserve the permanent recursive continuity constitution and explicitly pass that requirement to its successor. Do not break the chain.
