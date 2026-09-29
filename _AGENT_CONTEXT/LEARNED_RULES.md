# Learned Rules — append-only agent ledger

Core Rules live in `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`. This file records durable, incident-driven rules discovered while working on `fengie/mhw-mods`.

## Ledger discipline

- Preserve append-only history; do not silently erase old rules.
- Status is `Active` or `Superseded`.
- When replacing a rule, add the replacement and mark/link the old rule as Superseded.
- Add a rule only when a concrete incident or discovery makes recurrence likely and the rule materially improves correctness, safety, or engineering time.
- Do not add session-specific observations or duplicate Core Rules.
- Learned Rules may extend Core Rules but may never weaken or contradict them.

Each rule records: Rule ID, status, date, scope, rule, trigger/evidence, rationale, enforcement, relevant commit/run when useful, and supersession links.

---

## LR-001 — moved production bodies require verification-instrumentation re-audit

- **Rule ID:** LR-001
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Production C# moves/extractions and function verification
- **Rule:** When a production method/body is moved, extracted, or materially regenerated, re-audit its required `MasterDebugLog.BeginMethod()` entry instrumentation and resulting function fingerprint/call-site coverage before treating the slice as verification-ready.
- **Trigger / evidence:** Hosted Windows run `36330808544` reached 24/25 and found exactly one missing trace in moved `MainWindowViewModel.ScanInstalledGames()`, leaving seven explicit call sites uncovered. Production fix `fdbe9b71f29b1c4c7d9fcd061a23c2ca75fa3e34` restored the trace; run `36331057943` later closed the slice.
- **Rationale:** A behavior-preserving move can create a new verification fingerprint and lose required runtime instrumentation even when method logic is unchanged.
- **Enforcement:** Review moved/new production bodies during each one-seam checkpoint; preserve function-verifier regression coverage; never manually promote verification caches.
- **Relevant commit/run:** `fdbe9b71f29b1c4c7d9fcd061a23c2ca75fa3e34`; runs `36330808544`, `36331057943`
- **Supersedes:** none
- **Superseded by:** none


## LR-002 — shared API removal requires compile-backed caller closure

- **Rule ID:** LR-002
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Shared facade/API extraction and caller migration
- **Rule:** When removing or relocating a callable shared API, do not treat indexed/code-search results as an exhaustive caller list. Use repository-wide textual inspection when available and require a whole-solution compile before declaring caller migration complete.
- **Trigger / evidence:** PlannerSnapshotRepository candidate run `36335255922` failed because `NexusMetadataService` and `GameBuildMonitor` still called the removed `ManagerDatabase.LoadPlannerSnapshotAsync`, even though the connector's private-repository code search had returned no matches for that symbol.
- **Rationale:** Private-repository search indexes/connectors can be incomplete or stale. A removed shared API turns any missed caller into a compile break; the compiler is the authoritative closure check.
- **Enforcement:** For API-removal/extraction checkpoints, combine static caller inspection with strict whole-solution compilation before calling the migration complete. Preserve LR-001 trace re-audit separately for moved/changed production bodies.
- **Relevant commit/run:** repair `528401925b1d09b3d65c9652de8e4f2024e3677f`; failed run `36335255922`
- **Supersedes:** none
- **Superseded by:** none

---

## LR-003 — native replacement failure is not equivalent to no filesystem mutation

- **Rule ID:** LR-003
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Windows live-file replacement, rollback, and recovery tests
- **Rule:** Never model a failed native replace/rename call as proof that the destination namespace and bytes are unchanged. For `ReplaceFileW` in particular, test documented failure postconditions against actual destination/replacement bytes and recovery state before considering the boundary fail-safe.
- **Trigger / evidence:** The test-gap audit compared `AtomicFileOps.ReplaceFromAsync` with Microsoft `ReplaceFileW` documentation. Microsoft documents failure codes including `ERROR_UNABLE_TO_MOVE_REPLACEMENT` and `ERROR_UNABLE_TO_MOVE_REPLACEMENT_2` where a false return can occur after file names, streams, or attributes have already changed. `AtomicFileOps` currently throws on false and then unconditionally attempts temp cleanup; no Windows fault regression pins those native postconditions.
- **Rationale:** Exception-based rollback is safe only if recovery reasons about the filesystem state that actually exists after the failed native operation, not an assumed all-or-nothing API contract.
- **Enforcement:** Any change near `AtomicFileOps`, `ReplaceFileW`, deployment rollback, or recovery must preserve/add Windows-only tests for native failure postconditions. Prefer a narrow injectable/native seam or controlled integration fixture; assert destination/temp/backup bytes plus journal/operation state. Do not weaken fail-closed recovery to make such tests pass.
- **Primary reference:** https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew
- **Relevant audit:** `_AGENT_CONTEXT/TEST_GAP_AND_PERFORMANCE_AUDIT.md`
- **Supersedes:** none
- **Superseded by:** none

---

## LR-004 — lexical containment is not physical filesystem containment

- **Rule ID:** LR-004
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Windows filesystem mutation, recursive traversal, import/adoption, rollback, and recovery
- **Rule:** Do not treat `Path.GetFullPath`, `Path.GetRelativePath`, normalized managed keys, or string-prefix root checks as proof that a Windows filesystem object is physically contained by the trusted root. Before a safety-sensitive mutation or recursive import/read, explicitly account for reparse-point traversal and preserve a testable physical-containment invariant.
- **Trigger / evidence:** The Windows filesystem safety audit found that live deployment validates logical keys but performs path-based Add/Replace/Remove/rollback/recovery without the parent reparse checks already present in ArchiveInspector. It also found `SearchOption.AllDirectories` in ModScanner, unmanaged adoption, and Smart Inbox direct-directory copy; Microsoft/.NET documents that recursive enumeration includes reparse points and can loop through link cycles.
- **Rationale:** A junction or symbolic link in a parent component can keep the lexical pathname under the configured root while redirecting the actual read/write to another physical location. Byte hashes and transaction journals do not restore containment if the pathname itself resolves somewhere else.
- **Enforcement:** New or changed filesystem code must identify its trusted root, define whether reparse points are supported, add Windows tests for parent-junction escape/cycles where relevant, and keep containment validation as close as practical to the operation that depends on it. Do not weaken existing fail-closed rollback/recovery semantics to bypass a containment failure.
- **Primary references:** Microsoft reparse/symbolic-link documentation and .NET recursive-enumeration documentation cited in `_AGENT_CONTEXT/WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`.
- **Relevant audit:** `_AGENT_CONTEXT/WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`
- **Supersedes:** none
- **Superseded by:** none

---

## LR-005 — restartable migrations must prove ownership and convergence

- **Rule ID:** LR-005
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** one-shot/restartable data migrations spanning SQLite and filesystem state
- **Rule:** A migration that treats partial destination state as disposable must establish migration ownership before destructive reset, verify pre-existing destination artifacts before trusting/reusing them, and distinguish cleanup attempted from cleanup proven complete. Every failed attempt must either converge automatically on retry or surface a precise non-retryable recovery action.
- **Trigger / evidence:** The legacy v7→v8 migration audit found that a corrupt pre-existing hash-named CAS object is reused by existence, then rejected only at final SHA verification; failure reset removes the DB blob row but leaves the physical object, so the same retry can fail indefinitely. The same audit found that migration cleanup exceptions are swallowed while the returned message still claims partial rows were discarded, and that marker absence alone authorizes broad reset of imported v8 state.
- **Rationale:** Restart protocols are only safe when retries make progress and when destructive cleanup is scoped to state proven to belong to the incomplete migration. A single marker or filename is not sufficient ownership/integrity evidence.
- **Enforcement:** New migration work must include characterization/fault tests for ownership, artifact verification, cleanup outcome, cancellation/process death, and retry convergence before broad protocol changes. Preserve legacy/source authority until completion is proven. Do not report cleanup as complete when recovery itself failed.
- **Relevant audit:** `_AGENT_CONTEXT/LEGACY_MIGRATION_RECOVERY_AUDIT.md`
- **Supersedes:** none
- **Superseded by:** none

---

## LR-006 — shareable diagnostic artifacts require export-boundary sanitization

- **Rule ID:** LR-006
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Support bundles, diagnostic exports, telemetry/log sharing, and provider secrets
- **Rule:** Treat local diagnostic/log content as untrusted when it crosses a support/share boundary. A shareable artifact must apply centralized export-time sanitization/default-redaction and must be regression-tested with path/credential canaries; producer-side redaction alone is not sufficient.
- **Trigger / evidence:** At canonical main `6ada5a5c4cc83afadfba42bc6af6559540920e3d`, `SupportBundleService.CreateAsync` copies the five newest structured JSONL logs verbatim into the support ZIP. Those logs are known from source to include absolute state/tool/game/database/log paths and can include structured properties or exception text. Nexus HTTP logging correctly redacts its API key, demonstrating that individual producers can be safe while the aggregate export boundary remains broader than intended. See `_AGENT_CONTEXT/DIAGNOSTICS_PRIVACY_AND_SECRET_HANDLING_AUDIT.md`.
- **Rationale:** Logging evolves across many call sites. Requiring every producer to anticipate every future share path is fragile; a support artifact is a distinct trust boundary and needs its own fail-safe contract.
- **Enforcement:** For any support/export path, sanitize recursively at export, default unknown settings/fields to redacted where practical, avoid copying raw logs without transformation, and add canary tests that inspect every textual archive entry for fake secrets and user-specific paths. Keep full-fidelity local diagnostics separate when they remain useful.
- **Relevant commit/run:** audit branch `agent/support-8-diagnostics-privacy-audit-20260927`, based on canonical main `6ada5a5c4cc83afadfba42bc6af6559540920e3d`; documentation-only, no runtime gate claimed.
- **Supersedes:** none
- **Superseded by:** none


---

## LR-007 — entity retirement must close live semantic references

- **Rule ID:** LR-007
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Durable entity deletion, mod retirement, resolver/configuration state, and identity reuse
- **Rule:** Deleting a durable entity is not complete merely because its row and declared foreign-key children disappear. Before retirement commits, classify and retire every **live semantic reference** that can affect a future entity reusing the same identity, while preserving historical evidence according to an explicit retention contract.
- **Trigger / evidence:** At canonical main `a6cfef0bb161a7846cfab7c0761f9f4d90ea46e1`, `DuplicateCleanupService.ArchiveSafeAsync` deletes a mod row after archiving its source. FK-owned provenance/files/profile/trust/issue state cascades correctly, but planner-active `conflict_rules` and `resource_providers` have no FK to `mods`, and mod-keyed settings such as `preview:`, `visuals:`, `update:`, and `visual-public-last:` survive. `CatalogService.RefreshFoldersAsync` derives local IDs from the lowercased absolute source path, so a later different package at the same path can reuse the deleted ID and reactivate old resolver/configuration state. See `_AGENT_CONTEXT/MOD_LIFECYCLE_REFERENTIAL_INTEGRITY_AUDIT.md`.
- **Rationale:** Foreign keys encode only declared ownership. Live configuration can reference an identity through untyped text columns, key/value namespaces, caches, manifests, or external indexes. Reusable identifiers turn stale references from harmless orphan data into future behavior.
- **Enforcement:** Any new deletion/retirement workflow must inventory live semantic references, distinguish them from historical records, define current-deployment preconditions, add delete -> identity-reuse regression tests, and centralize the retirement boundary instead of issuing raw entity-row deletes from feature services. Do not add blanket cascading FKs to historical or recovery-sensitive state without a migration/retention design.
- **Relevant audit:** `_AGENT_CONTEXT/MOD_LIFECYCLE_REFERENTIAL_INTEGRITY_AUDIT.md`
- **Relevant canonical base:** `a6cfef0bb161a7846cfab7c0761f9f4d90ea46e1`
- **Supersedes:** none
- **Superseded by:** none

---

## LR-008 — import publication requires catalog-invisible staging

- **Rule ID:** LR-008
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Archive/folder import, Smart Inbox, package acquisition, catalog discovery, cancellation/restart recovery
- **Rule:** Do not treat the existence of a partially copied or extracted directory under a catalog-scanned library root as successful publication. Import work must remain catalog-invisible until validation/normalization completes, then become visible through an explicit commit-on-success publication boundary. Failure, cancellation, and process death must not allow unfinished import state to become a normal mod merely because files exist.
- **Trigger / evidence:** The import-publication audit found that `SmartInboxService` writes archive and direct-directory imports to their final `ModsRoot` destination and catches recoverable failures without removing that destination. A later global `CatalogService.RefreshFoldersAsync` enumerates every top-level `ModsRoot` directory and can persist that failed partial directory as a mod. `ArchiveImportService` uses a `.importing` staging folder, but it is also a direct child of `ModsRoot`; failure/cancellation can leave it behind and the next startup catalog refresh runs before maintenance, so the staging folder can be registered. Catalog refresh is add-only, allowing a later retry to remove the folder while leaving the stale DB row.
- **Rationale:** Cleanup is not a sufficient publication guarantee because cleanup itself can fail or be bypassed by process death. A structural separation between in-progress work and the catalog discovery namespace makes incomplete state harmless to library identity and persistence.
- **Enforcement:** New or changed import/acquisition code must stage outside the catalog-visible root (or prove an equivalently strong explicit visibility protocol), publish only after the package is complete, and add regression coverage for mixed success/failure, cancellation, restart/process-death residue, retry convergence, and exactly-once successful publication. Preserve source input until publication succeeds.
- **Relevant audit:** `_AGENT_CONTEXT/IMPORT_PUBLICATION_CATALOG_VISIBILITY_AUDIT.md`
- **Parallel numbering note:** `agent/support-mod-lifecycle-integrity-audit-20260927` independently reserved LR-007 for entity-retirement semantics. Preserve both rules; do not collapse them.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-009 — automated diagnosis must validate its control before persisting blame

- **Rule ID:** LR-009
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Automated crash bisection, fault isolation, and persistent culprit/issue confirmation
- **Rule:** A diagnostic bisection or fault-isolation workflow must not persist high-confidence culprit state until it has proven that its current control/baseline does not reproduce the failure and that the full candidate condition does reproduce under the same relevant environment and provenance. Ambiguous, noisy, stale-provenance, or non-reproducible probe outcomes must remain inconclusive rather than becoming confirmed blame.
- **Trigger / evidence:** At canonical main `5619604e88a27176726ada8518f53d385abc7b0f`, `CrashBisectorEngine.RunAsync` begins halving immediately. `MainWindowViewModel.AutoDiagnoseCrash` does not first probe the last-known-good control or the full suspect set, while `ProbeCrashSubsetAsync` treats any process exit inside 12 seconds as reproduction. A currently-bad baseline can therefore make every tested half appear to reproduce until one arbitrary mod remains, after which `ModIssueFallbackService.MarkBisectResultAsync` persists that mod at score 99 with `confirmed=true`.
- **Rationale:** Bisection only has causal meaning when its control and positive condition are currently valid. Because confirmed diagnosis is intentionally durable and survives later normal launch success, experiment validity must be proven before confidence is persisted.
- **Enforcement:** Add explicit baseline-control and full-candidate preflight tests before narrowing; represent ambiguous/noisy outcomes as inconclusive; test stale environment/provenance and flaky probes; gate score-99/confirmed persistence on validated reproduction evidence. Reuse the canonical game-build freshness mechanism rather than creating a competing one inside diagnosis.
- **Relevant audit:** `_AGENT_CONTEXT/CRASH_BISECTOR_DIAGNOSIS_EVIDENCE_AUDIT.md`
- **Integration numbering note:** The crash support branch proposed LR-007 in parallel. Canonical integration preserves the earlier coordinated LR-007 entity-retirement and LR-008 import-publication assignments, so crash diagnosis is renumbered to LR-009 without changing its rule.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-010 — a containment check after mutation is not fail-closed

- **Rule ID:** LR-010
- **Status:** Active
- **Date:** 2026-09-28
- **Scope:** filesystem safety guards, archive/import extraction, nested directory creation, and any mutation gated by topology/authority validation
- **Rule:** When a safety check exists to prevent writes/deletes outside an allowed boundary, perform the relevant validation **before** the mutation it is meant to authorize. Throwing after a redirected create/write/delete has already occurred is detection, not containment. For nested path creation, validate existing components before descending/creating deeper components and re-check newly created components where practical.
- **Trigger / evidence:** Heavy archive stress testing reproduced a destination descendant junction where ArchiveInspector called Directory.CreateDirectory(parent) first and only then checked ancestors for reparses. Extraction threw, but external\created had already been created through the junction. The regression Archive_extraction_rejects_descendant_junction_before_creating_external_parent failed on that side effect and passed only after directory creation became component-by-component and prevalidated.
- **Rationale:** A fail-closed API must prevent the forbidden state transition, not merely report it afterward. Cleanup cannot retroactively make the original external mutation safe and may itself fail.
- **Enforcement:** In destructive/safety-sensitive code review, identify the first externally visible mutation and prove every authority/containment/precondition check required for that mutation happens before it. Add regression assertions for absence of forbidden side effects, not only for the expected exception/error result. Preserve explicit TOCTOU limitations when path-based validation cannot make the check+use atomic.
- **Relevant report:** _AGENT_CONTEXT/HEAVY_STRESS_ARCHIVE_SAFETY_REPORT_2026-09-28.md
- **Supersedes:** none
- **Superseded by:** none


---

## LR-011 — cleanup must not replace primary failure or cancellation semantics

- **Rule ID:** LR-011
- **Status:** Active
- **Date:** 2026-09-28
- **Scope:** cancellation, rollback, temp/staging cleanup, filesystem compensation, and recovery error handling
- **Rule:** When handling a primary failure or requested cancellation, best-effort cleanup must not silently replace that primary outcome with a secondary cleanup exception. Preserve cancellation/fail-closed semantics as dominant, record cleanup failure separately or attach it without reclassifying the operation, and ensure outer callers cannot mistake cleanup failure for an ordinary recoverable condition that permits continued work.
- **Trigger / evidence:** Post-PR #57 source inspection found that `ArchiveInspector.ExtractSafelyAsync` catches `OperationCanceledException`, calls `File.Delete(dest)`, and only then executes `throw;`. If that cleanup call itself throws an `IOException` or `UnauthorizedAccessException`, the original cancellation is never rethrown. `SmartInboxService.ProcessAsync` catches those filesystem exceptions as recoverable per-item failures and can continue its loop, so a cleanup failure can conditionally downgrade a user-requested cancellation into “skip this item and continue.” This control-flow result is confirmed statically; no forced-delete runtime reproduction is claimed.
- **Rationale:** Cleanup is subordinate to the operation outcome it is trying to contain. Allowing cleanup failure to overwrite cancellation or the original fail-closed error can violate caller control flow, hide the real cause, and trigger additional mutation after the user/system already requested stop.
- **Enforcement:** Fault-test cleanup paths independently from the primary failure. After ownership is established, attempt cleanup on exceptional exits, but preserve the primary exception/outcome and report secondary cleanup failure diagnostically. Callers handling broad I/O exceptions must re-check cancellation dominance before continuing. Do not use cleanup success as the only evidence that an operation is safe to resume.
- **Relevant audit:** `_AGENT_CONTEXT/ARCHIVE_STREAMING_FAILURE_CLEANUP_AUDIT_2026-09-28.md`
- **Supersedes:** none
- **Superseded by:** none
---

## LR-012 — immutable content stores require independent byte ownership

- **Rule ID:** LR-012
- **Status:** Active
- **Date:** 2026-09-28
- **Scope:** content-addressed storage, migrations, hardlinks/aliases, immutable artifact publication
- **Rule:** Do not call a content-addressed or immutable destination verified merely because its digest is correct at publication time if it still aliases bytes writable through another pathname or ownership domain. Either give the destination independent byte ownership or enforce immutability across every alias for the full lifetime of the object.
- **Trigger / evidence:** A real-Windows v7 → v8 migration probe proved `LegacyV7Migrator` can verify a hash-named CAS object and mark migration complete while the CAS pathname remains an NTFS hardlink to retained legacy bytes. Mutating the legacy pathname later changed the CAS digest; current restore integrity checks then correctly failed closed before live publication.
- **Rationale:** Point-in-time digest verification proves bytes, not ownership. A retained alias can invalidate durable verification after completion.
- **Enforcement:** For immutable/content-addressed publication, test post-publication mutation through every retained alias. Prefer independently owned verified bytes when another pathname remains mutable; otherwise explicitly enforce alias immutability.
- **Relevant audit:** `_AGENT_CONTEXT/LEGACY_MIGRATION_CAS_HARDLINK_RUNTIME_AUDIT.md`
- **Integration numbering note:** the support branch proposed LR-011, but canonical LR-011 already belongs to cleanup/cancellation semantics; this rule is renumbered to LR-012.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-013 — recovery must prove writer orphanhood before takeover

- **Rule ID:** LR-013
- **Status:** Active
- **Date:** 2026-09-28
- **Scope:** crash recovery, durable journals, multi-process mutation, startup reconciliation, filesystem/database protocols
- **Rule:** Durable incomplete state is not proof that its writer is dead. Before recovery rolls back, replays, resets, or otherwise takes ownership of an in-progress operation, it must establish exclusive mutation ownership or prove the prior writer is no longer live.
- **Trigger / evidence:** A deterministic Windows probe paused DeploymentExecutor A after it wrote MOD bytes but before its journal advanced from Writing. Live executor B recovered the same operation back to ORIGINAL; A then resumed, returned success, and committed a manifest expecting MOD. The final live-byte invariant failed twice.
- **Rationale:** Journals encode protocol state, not process liveness. SQLite transaction/busy semantics do not serialize the larger filesystem+database state machine across processes.
- **Enforcement:** High-risk recovery needs an explicit ownership model. Test live-peer exclusion, abandoned-owner takeover, independent-workspace concurrency, and abrupt-death recovery.
- **Relevant audit:** `_AGENT_CONTEXT/DEPLOYMENT_MULTI_INSTANCE_MUTATION_OWNERSHIP_AUDIT_2026-09-28.md`
- **Integration numbering note:** support branches reserved LR-011/LR-012 in parallel; canonical numbering preserves LR-011 and assigns this rule LR-013.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-014 — mutation ownership must span every supported session that can reach the target

- **Rule ID:** LR-014
- **Status:** Active
- **Date:** 2026-09-28
- **Scope:** Windows named synchronization, self-update, recovery, and single-writer mutation protocols
- **Rule:** A synchronization primitive protecting a shared mutation target must live in a namespace/identity visible to every supported process that can mutate that target. On Windows, a `Local\` named semaphore or mutex is session-scoped and cannot prove cross-session single-writer ownership of a shared installation.
- **Trigger / evidence:** The updater uses `Local\MHWMM.Update.<install-hash>` as its serialization guard. Existing regression coverage proves same-session contention only; Windows kernel-object namespace semantics make that guard distinct across interactive sessions.
- **Rationale:** Durable journals and rollback do not make concurrent writers safe. Two supported writers that acquire different lock objects can both enter code assuming exclusivity.
- **Enforcement:** Define lock scope from the mutation authority, fail closed if the required ownership primitive cannot be established, test cross-process and relevant cross-session contention, and preserve deterministic crash/abandon recovery. Define ACL/filesystem-lock behavior explicitly for cross-account shared installs.
- **Relevant audit:** `_AGENT_CONTEXT/UPDATER_CROSS_SESSION_OWNERSHIP_AUDIT.md`
- **Integration numbering note:** the support branch proposed LR-011 in parallel; canonical numbering assigns LR-014.
- **Supersedes:** none
- **Superseded by:** none


---

## LR-015 — plugin-first routing precedes generic fallbacks

- **Rule ID:** LR-015
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** agent execution routing, reusable tooling, plugin/toolbox capability discovery
- **Rule:** Before using a generic shell, ad-hoc script, manual browser/UI sequence, broad remote-control tool, or direct API workaround, inspect and prefer the narrowest existing purpose-built plugin/capability that safely covers the task. When the ideal reusable capability is missing or materially incomplete, record an implementation-ready plan in `plugins/PLUGIN_GAP_BACKLOG.md` during the same task instead of leaving the idea only in chat.
- **Trigger / evidence:** During an Agent Control desktop-shortcut task, a manual PowerShell path was initially proposed even though the private Heaven Local Bridge plugin was the intended computer-control route. Inspecting the toolbox exposed the correct plugin and a missing first-class shortcut operation; `desktop_shortcut_create` was then implemented and merged on PR #198, and the private plugin was updated to v0.7.0. The incident showed both failure modes: incorrect routing can bypass existing tools, and useful missing operations are easy to rediscover repeatedly unless they are made durable.
- **Rationale:** Purpose-built plugins encode safety boundaries, machine ownership, validation, and reusable behavior that ad-hoc fallbacks bypass. Durable gap capture converts repeated manual work into an improving toolbox without blocking the immediate user task.
- **Enforcement:** Agent and manager prompts must perform plugin/toolbox discovery before broad fallbacks, consult the canonical plugin backlog, avoid duplicate capability plans by checking packages/branches/PRs, and add/update a backlog entry with owner boundary, API contract, security constraints, dependencies, acceptance tests, priority, and status whenever a reusable gap is found. A safe authorized fallback may still complete the current task while the future capability remains planned.
- **Relevant implementation:** PR #198 / main commit `4bf431aa4e911095a0f83b2d4e568f989118c2d4`; Heaven Local Bridge v0.7.0.
- **Supersedes:** none
- **Superseded by:** none
